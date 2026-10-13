using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Text.Json.Nodes;

namespace Dolly;

/// Telecomando dal telefono: piccolo server web sulla rete locale (nessuna connessione a internet). Spento di default; si attiva dall'interfaccia.
/// Il telefono apre l'indirizzo mostrato (o inquadra il QR), inserisce il PIN e comanda la proiezione. Solo i comandi elencati in `Allowed` sono accettati.
public sealed class RemoteControl
{
    public static readonly RemoteControl Shared = new();
    public bool Enabled { get; private set; }
    public string Pin { get; private set; } = "";
    public List<string> Urls { get; private set; } = new();
    public string? Problem { get; private set; }
    public event Action? Changed;

    readonly object lk = new();
    Engine? engine;
    public Engine? Eng { get { lock (lk) return engine; } set { lock (lk) engine = value; } }
    List<string> tokens = new(); List<double> fails = new();
    TcpListener? listener; int port = 8484;
    static string CFile => Path.Combine(Engine.HomePath, "remote.json");

    RemoteControl()
    {
        if (U.ReadJson(CFile) is JsonObject o) { Enabled = o.Get("enabled").B() ?? false; Pin = o.Get("pin").S() ?? ""; }
        Pin = NewPin(Pin); tokens = new();   // PIN nuovo a ogni avvio dell'app: i telefoni devono rifare l'accesso
        Save();
        if (Enabled) Start();
    }
    static string NewPin(string old = "") { string p; do { p = Random.Shared.Next(0, 10000).ToString("0000"); } while (p == old); return p; }
    void Save()
    {
        try
        {
            Directory.CreateDirectory(Engine.HomePath);
            U.WriteJson(CFile, new JsonObject { ["enabled"] = Enabled, ["pin"] = Pin, ["tokens"] = U.Arr(tokens.Cast<object?>()) });
        }
        catch { }
    }
    public void SetEnabled(bool on)
    {
        if (on) { if (!Enabled) RegeneratePin(); Start(); } else Stop();
        Enabled = on && Problem == null; if (on && Problem != null) Stop();
        Save(); Changed?.Invoke();
    }
    /// Nuovo PIN: i telefoni già collegati devono rifare l'accesso.
    public void RegeneratePin() { lock (lk) tokens = new(); Pin = NewPin(Pin); Save(); Changed?.Invoke(); }

    // MARK: server
    void Start()
    {
        Stop();
        int first = int.TryParse(Environment.GetEnvironmentVariable("DOLLY_REMOTE_PORT"), out var fp) ? fp : 8484;   // (la variabile serve solo ai collaudi)
        TcpListener? l = null;
        for (int p = first; p <= first + 10; p++)
        {
            try { l = new TcpListener(IPAddress.Any, p); l.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, !OperatingSystem.IsWindows()); l.Start(16); port = p; break; }
            catch { l = null; }
        }
        if (l == null) { Problem = L.Tr("Non riesco ad aprire una porta di rete."); Urls = new(); Changed?.Invoke(); return; }
        Problem = null; listener = l;
        Urls = LanAddresses().Select(a => $"http://{a}:{port}/").ToList(); Changed?.Invoke();
        new Thread(() =>
        {
            while (true)
            {
                TcpClient c; try { c = l.AcceptTcpClient(); } catch { break; }
                Task.Run(() => Handle(c));
            }
        }) { IsBackground = true, Name = "dolly.remote" }.Start();
    }
    void Stop() { try { listener?.Stop(); } catch { } listener = null; Urls = new(); Changed?.Invoke(); }

    /// Indirizzi IPv4 del PC sulla rete locale (Wi-Fi, Ethernet, hotspot): quelli che il telefono può raggiungere. Le schede virtuali (VM, VPN) si escludono.
    public static List<string> LanAddresses()
    {
        var res = new List<(string, string)>();
        foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (ni.OperationalStatus != OperationalStatus.Up || ni.NetworkInterfaceType == NetworkInterfaceType.Loopback || ni.NetworkInterfaceType == NetworkInterfaceType.Tunnel) continue;
            var desc = (ni.Name + " " + ni.Description).ToLowerInvariant();
            if (System.Text.RegularExpressions.Regex.IsMatch(desc, "virtual|vmware|vbox|hyper-v|vethernet|loopback|docker|tap-|tailscale|zerotier")) continue;
            var props = ni.GetIPProperties();
            foreach (var ua in props.UnicastAddresses)
            {
                if (ua.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                var s = ua.Address.ToString(); if (s.StartsWith("169.254.")) continue;   // indirizzi "link-local": nessuna rete vera
                res.Add((ni.Name, s));
            }
        }
        return res.OrderBy(x => x.Item1, StringComparer.Ordinal).Select(x => x.Item2).ToList();
    }

    static void Reply(Stream st, int code, string type, byte[] body)
    {
        var text = code switch { 200 => "OK", 204 => "No Content", 400 => "Bad Request", 401 => "Unauthorized", 404 => "Not Found", 429 => "Too Many Requests", 503 => "Service Unavailable", _ => "OK" };
        var head = $"HTTP/1.1 {code} {text}\r\nContent-Type: {type}\r\nContent-Length: {body.Length}\r\nCache-Control: no-store\r\nX-Content-Type-Options: nosniff\r\nConnection: close\r\n\r\n";
        var h = Encoding.UTF8.GetBytes(head);
        try { st.Write(h, 0, h.Length); st.Write(body, 0, body.Length); st.Flush(); } catch { }
    }
    static void Json(Stream st, int code, JsonNode o) => Reply(st, code, "application/json", Encoding.UTF8.GetBytes(o.ToJsonString()));

    /// Comandi che il telefono può inviare (niente scelta di cartelle, scalette, file o impostazioni dell'app).
    static bool Allowed(JsonObject d)
    {
        switch (d.Get("a").S() ?? "")
        {
            case "toggle": case "play": case "next": case "prev": case "stop": case "seek": case "skipgap": case "extend": case "resume": return true;
            case "set": return new[] { "volume", "mute" }.Contains(d.Get("p").S() ?? "");
            case "setitem": return d.Select(kv => kv.Key).All(k => k is "a" or "i" or "loop");
            default: return false;
        }
    }

    static string Page()
    {
        using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("Dolly.remote.html")!;
        using var r = new StreamReader(s, Encoding.UTF8);
        return r.ReadToEnd().Replace("__LANG__", Lang.Code);
    }

    void Handle(TcpClient c)
    {
        using var _c = c;
        try
        {
            c.ReceiveTimeout = 5000; c.SendTimeout = 5000;
            using var st = c.GetStream();
            var buf = new List<byte>(); var chunk = new byte[8192]; int headEnd = -1;
            while (headEnd < 0)
            {
                int n = st.Read(chunk, 0, chunk.Length); if (n <= 0 || buf.Count > 16384) return;
                buf.AddRange(new ArraySegment<byte>(chunk, 0, n)); headEnd = IndexOf(buf, new byte[] { 13, 10, 13, 10 });
            }
            var head = Encoding.UTF8.GetString(buf.GetRange(0, headEnd).ToArray());
            var lines = head.Split("\r\n"); var parts = lines[0].Split(' ');
            if (parts.Length < 2) return;
            string method = parts[0], full = parts[1]; var path = full.Split('?')[0];
            int len = 0; string hdrToken = "";
            foreach (var l in lines.Skip(1))
            {
                var low = l.ToLowerInvariant();
                if (low.StartsWith("content-length:")) int.TryParse(l.Substring(15).Trim(), out len);
                if (low.StartsWith("x-token:")) hdrToken = l.Substring(8).Trim();
            }
            if (len > 4096) { Reply(st, 400, "text/plain", Array.Empty<byte>()); return; }
            var body = buf.GetRange(headEnd + 4, buf.Count - headEnd - 4);
            while (body.Count < len) { int n = st.Read(chunk, 0, chunk.Length); if (n <= 0) break; body.AddRange(new ArraySegment<byte>(chunk, 0, n)); }
            JsonObject obj; try { obj = JsonNode.Parse(Encoding.UTF8.GetString(body.ToArray())) as JsonObject ?? new(); } catch { obj = new(); }

            if (method == "GET" && (path == "/" || path == "/index.html")) { Reply(st, 200, "text/html; charset=utf-8", Encoding.UTF8.GetBytes(Page())); return; }
            if (method == "POST" && path == "/api/login")
            {
                double now = U.Now; bool blocked; lock (lk) { fails = fails.Where(f => now - f < 60).ToList(); blocked = fails.Count >= 5; }
                if (blocked) { Json(st, 429, new JsonObject { ["error"] = L.Tr("troppi tentativi") }); return; }
                if ((obj.Get("pin").S() ?? "") == Pin)
                {
                    var t = Guid.NewGuid().ToString("N");
                    lock (lk) tokens = tokens.Append(t).TakeLast(10).ToList(); Save(); Json(st, 200, new JsonObject { ["token"] = t });
                }
                else { lock (lk) fails.Add(now); Json(st, 401, new JsonObject { ["error"] = L.Tr("PIN errato") }); }
                return;
            }
            var tok = hdrToken;
            if (tok.Length == 0 && full.Contains("?t=")) tok = new string(full.Substring(full.IndexOf("t=") + 2).Take(32).ToArray());
            bool ok; lock (lk) ok = tok.Length > 0 && tokens.Contains(tok);
            if (!ok) { Json(st, 401, new JsonObject { ["error"] = L.Tr("accesso necessario") }); return; }
            var e = Eng;
            if (e == null) { Json(st, 503, new JsonObject { ["error"] = L.Tr("player non pronto") }); return; }

            if (method == "GET" && path == "/api/state")
            {
                var raw = e.StateObjSync(); var s = new JsonObject();
                foreach (var k in new[] { "ok", "name", "mode", "idx", "sel", "label", "left", "playing", "pause", "time", "dur" }) s[k] = U.J(raw.Get(k));
                var items = new JsonArray();
                foreach (var i in (raw.Get("items") as JsonArray)?.OfType<JsonObject>() ?? Enumerable.Empty<JsonObject>())
                {
                    var o = new JsonObject(); foreach (var k in new[] { "kind", "name", "secs", "text", "ok", "loop" }) o[k] = U.J(i.Get(k)); items.Add(o);
                }
                s["items"] = items; var pr = raw.Get("props") as JsonObject;
                s["props"] = new JsonObject { ["volume"] = U.J(pr?.Get("volume")), ["mute"] = U.J(pr?.Get("mute")) };
                Json(st, 200, s);
            }
            else if (method == "GET" && path == "/api/preview")
            {
                var j = e.PreviewSync(); if (j != null) Reply(st, 200, "image/jpeg", j); else Reply(st, 204, "text/plain", Array.Empty<byte>());
            }
            else if (method == "POST" && path == "/api/cmd")
            {
                if (!Allowed(obj)) { Json(st, 400, new JsonObject { ["error"] = L.Tr("comando non ammesso") }); return; }
                var err = e.ActSync(obj); Json(st, 200, err != null ? new JsonObject { ["error"] = err } : new JsonObject());
            }
            else Reply(st, 404, "text/plain", Array.Empty<byte>());
        }
        catch { }
    }
    static int IndexOf(List<byte> b, byte[] pat)
    {
        for (int i = 0; i + pat.Length <= b.Count; i++) { int j = 0; while (j < pat.Length && b[i + j] == pat[j]) j++; if (j == pat.Length) return i; }
        return -1;
    }
}
