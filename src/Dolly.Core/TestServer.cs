using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;

namespace Dolly;

/// Interfaccia HTTP locale (solo 127.0.0.1) per i collaudi automatici: stessa API della versione Mac, così i test girano sul codice reale.
/// Si attiva solo con --test-api PORTA; l'app normale non apre nessuna porta.
public sealed class TestServer
{
    readonly Engine engine; readonly int port;
    public TestServer(Engine e, int port) { engine = e; this.port = port; }

    public void Start()
    {
        var l = new TcpListener(IPAddress.Loopback, port);
        try { l.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true); l.Start(64); } catch { throw new DollyError($"porta {port} non disponibile"); }
        new Thread(() => { while (true) { TcpClient c; try { c = l.AcceptTcpClient(); } catch { continue; } Task.Run(() => Handle(c)); } }) { IsBackground = true }.Start();
    }

    static void Reply(Stream st, int code, string type, byte[] body)
    {
        var h = Encoding.UTF8.GetBytes($"HTTP/1.1 {code} {(code == 200 ? "OK" : "No Content")}\r\nContent-Type: {type}\r\nContent-Length: {body.Length}\r\nCache-Control: no-store\r\nConnection: close\r\n\r\n");
        try { st.Write(h, 0, h.Length); st.Write(body, 0, body.Length); st.Flush(); } catch { }
    }

    void Handle(TcpClient c)
    {
        using var _c = c;
        try
        {
            c.ReceiveTimeout = 5000; using var st = c.GetStream();
            var buf = new List<byte>(); var chunk = new byte[65536]; int he = -1;
            while (he < 0) { int n = st.Read(chunk, 0, chunk.Length); if (n <= 0) return; buf.AddRange(new ArraySegment<byte>(chunk, 0, n)); he = Idx(buf); }
            var head = Encoding.UTF8.GetString(buf.GetRange(0, he).ToArray()); var lines = head.Split("\r\n"); var parts = lines[0].Split(' ');
            if (parts.Length < 2) return;
            string method = parts[0], path = parts[1]; int len = 0;
            foreach (var l in lines.Skip(1)) if (l.ToLowerInvariant().StartsWith("content-length:")) int.TryParse(l.Substring(15).Trim(), out len);
            var body = buf.GetRange(he + 4, buf.Count - he - 4);
            while (body.Count < len) { int n = st.Read(chunk, 0, chunk.Length); if (n <= 0) break; body.AddRange(new ArraySegment<byte>(chunk, 0, n)); }
            if (method == "GET")
            {
                if (path.StartsWith("/api/state")) Reply(st, 200, "application/json", Encoding.UTF8.GetBytes(engine.StateSync()));
                else if (path.StartsWith("/api/preview")) { var j = engine.PreviewSync(); if (j != null) Reply(st, 200, "image/jpeg", j); else Reply(st, 204, "text/plain", Array.Empty<byte>()); }
                else Reply(st, 200, "text/html; charset=utf-8", Encoding.UTF8.GetBytes("<!doctype html><html><head><title>Dolly</title></head><body>Dolly (interfaccia di collaudo)</body></html>"));
                return;
            }
            JsonObject obj; try { obj = JsonNode.Parse(Encoding.UTF8.GetString(body.ToArray())) as JsonObject ?? new(); } catch { obj = new(); }
            var o = new JsonObject();
            if (path.StartsWith("/api/pick")) o["paths"] = new JsonArray();
            else if (obj.Get("a").S() == "quit") { Reply(st, 200, "application/json", Encoding.UTF8.GetBytes("{}")); engine.Shutdown(); Environment.Exit(0); }
            else if (engine.ActSync(obj) is string e) o["error"] = e;
            Reply(st, 200, "application/json", Encoding.UTF8.GetBytes(o.ToJsonString()));
        }
        catch { }
    }
    static int Idx(List<byte> b) { for (int i = 0; i + 4 <= b.Count; i++) if (b[i] == 13 && b[i + 1] == 10 && b[i + 2] == 13 && b[i + 3] == 10) return i; return -1; }
}
