using System.Diagnostics;
using System.IO.Pipes;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;

namespace Dolly;

/// mpv come processo separato, pilotato via IPC (named pipe su Windows, socket Unix altrove): se mpv si pianta non porta giù l'app.
public sealed class Mpv
{
    public readonly string Binary; readonly string[] extra; readonly bool windowed; readonly int screen;
    public Process? Proc { get; private set; }
    public string Sock { get; private set; } = "";
    int rid;

    public Mpv(string binary, string[] extra, bool windowed, int screen) { Binary = binary; this.extra = extra; this.windowed = windowed; this.screen = screen; }

    public void Start()
    {
        var tag = $"dolly-{Environment.ProcessId}-{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";
        Sock = OperatingSystem.IsWindows() ? @"\\.\pipe\" + tag : Path.Combine(Path.GetTempPath(), tag + ".sock");
        var a = new List<string> {
            "--idle=yes", "--force-window=yes", "--keep-open=no", "--no-osc", "--no-input-default-bindings", "--no-input-cursor",
            "--osd-level=0", "--osd-font-size=64", "--osd-align-x=center", "--osd-align-y=center", "--osd-bold=yes",
            "--screenshot-format=jpeg", "--screenshot-jpeg-quality=60", "--cursor-autohide=always", "--audio-display=no", "--no-terminal",
            "--hwdec=auto-safe", "--alang=ita,it,eng,en", "--subs-match-os-language=no", "--no-resume-playback", "--demuxer-readahead-secs=30",
            $"--screen={screen}", $"--input-ipc-server={Sock}" };
        if (windowed) a.Add("--no-border"); else { a.Add("--fs"); a.Add($"--fs-screen={screen}"); }
        a.AddRange(extra);
        var psi = new ProcessStartInfo(Binary) { UseShellExecute = false, CreateNoWindow = true };
        foreach (var x in a) psi.ArgumentList.Add(x);
        Proc = Process.Start(psi) ?? throw new DollyError(L.Tr("mpv non è partito"));
        Win32.BindToJob(Proc);
        for (int i = 0; i < 300; i++)
        {
            if (Proc.HasExited) break;
            try { Ipc(new object[][] { new object[] { "get_property", "idle-active" } }, 1); return; } catch { }
            Thread.Sleep(100);
        }
        throw new DollyError(L.Tr("mpv non è partito"));
    }
    public bool Alive => Proc != null && !Proc.HasExited;
    public void Kill()
    {
        if (Proc == null || Proc.HasExited) return;
        try { Proc.Kill(true); Proc.WaitForExit(3000); } catch { }
        if (!OperatingSystem.IsWindows()) try { File.Delete(Sock); } catch { }
    }

    Stream Connect(double timeout)
    {
        if (OperatingSystem.IsWindows())
        {
            var p = new NamedPipeClientStream(".", Sock.Substring(@"\\.\pipe\".Length), PipeDirection.InOut, PipeOptions.Asynchronous);
            try { p.Connect((int)(timeout * 1000)); } catch { p.Dispose(); throw new DollyError(L.Tr("mpv non risponde (connect)")); }
            return p;
        }
        var s = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified) { SendTimeout = (int)(timeout * 1000), ReceiveTimeout = (int)(timeout * 1000) };
        try { s.Connect(new UnixDomainSocketEndPoint(Sock)); } catch { s.Dispose(); throw new DollyError(L.Tr("mpv non risponde (connect)")); }
        return new NetworkStream(s, true);
    }

    /// Più comandi su un'unica connessione. Lancia se mpv non risponde; null per le proprietà non disponibili.
    public JsonNode?[] Ipc(object[][] cmds, double timeout = 2)
    {
        using var st = Connect(timeout);
        var ids = new Dictionary<int, int>(); var outp = new JsonNode?[cmds.Length];
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeout));
        try
        {
            for (int i = 0; i < cmds.Length; i++)
            {
                rid++; ids[rid] = i;
                var msg = new JsonObject { ["command"] = U.Arr(cmds[i]), ["request_id"] = rid };
                var data = Encoding.UTF8.GetBytes(msg.ToJsonString() + "\n");
                st.WriteAsync(data, 0, data.Length, cts.Token).GetAwaiter().GetResult();
            }
            st.Flush();
        }
        catch (Exception) { throw new DollyError(L.Tr("mpv non risponde (invio)")); }
        var buf = new List<byte>(); var chunk = new byte[65536]; int left = cmds.Length;
        while (left > 0)
        {
            int n;
            try { n = st.ReadAsync(chunk, 0, chunk.Length, cts.Token).GetAwaiter().GetResult(); }
            catch (Exception) { throw new DollyError(L.Tr("mpv non risponde (timeout)")); }
            if (n == 0) throw new DollyError(L.Tr("mpv ha chiuso la connessione"));
            buf.AddRange(new ArraySegment<byte>(chunk, 0, n));
            int nl;
            while ((nl = buf.IndexOf(10)) >= 0)
            {
                var line = Encoding.UTF8.GetString(buf.GetRange(0, nl).ToArray()); buf.RemoveRange(0, nl + 1);
                JsonObject? r; try { r = JsonNode.Parse(line) as JsonObject; } catch { continue; }
                var id = r?.Get("request_id").I();
                if (r == null || id == null || !ids.TryGetValue(id.Value, out var i)) continue;
                if (r.Get("error").S() == "success") outp[i] = r.Get("data")?.DeepClone();
                left--;
            }
        }
        return outp;
    }
    public JsonNode?[] Ipc(params object[][] cmds) => Ipc(cmds, 2);

    public Dictionary<string, JsonNode?> Get(IEnumerable<string> props)
    {
        var list = props.ToList();
        var r = Ipc(list.Select(p => new object[] { "get_property", p }).ToArray(), 2);
        var d = new Dictionary<string, JsonNode?>();
        for (int i = 0; i < list.Count; i++) if (r[i] != null) d[list[i]] = r[i];
        return d;
    }
}
