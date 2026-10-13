using System.Runtime.InteropServices;
using Dolly;

// Avvio senza finestra per i collaudi: --test-api PORTA gira come il motore dell'app e offre l'interfaccia di collaudo.
string? Arg(string n) { var a = Environment.GetCommandLineArgs().Skip(1).ToList(); var i = a.IndexOf(n); return i >= 0 && i + 1 < a.Count ? a[i + 1] : null; }
var args_ = Environment.GetCommandLineArgs().Skip(1).ToList();
var mpvBin = Environment.GetEnvironmentVariable("DOLLY_MPV") ?? "/Applications/mpv.app/Contents/MacOS/mpv";

if (Arg("--test-api") is string p && int.TryParse(p, out var port))
{
    var mpvArg = Arg("--mpv");
    var folder = args_.FirstOrDefault(a => !a.StartsWith("--") && !int.TryParse(a, out _) && a != mpvArg) ?? "~/Desktop";
    var extra = (mpvArg ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
    try
    {
        var engine = new Engine(folder, mpvBin, extra, args_.Contains("--windowed"), int.TryParse(Arg("--screen"), out var sc) ? sc : 0, args_.Contains("--autoresume"), args_.Contains("--reset-playlist"));
        new TestServer(engine, port).Start();
        RemoteControl.Shared.Eng = engine; if (args_.Contains("--remote")) RemoteControl.Shared.SetEnabled(true);   // telecomando da telefono, solo se richiesto
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; engine.Shutdown(); Environment.Exit(0); };
        AppDomain.CurrentDomain.ProcessExit += (_, _) => { try { engine.Shutdown(); } catch { } };
        PosixSignalRegistration.Create(PosixSignal.SIGTERM, _ => { engine.Shutdown(); Environment.Exit(0); });
        Thread.Sleep(Timeout.Infinite);
    }
    catch (Exception e) { U.Log("errore:", e.Message); Environment.Exit(1); }
}
else Console.Error.WriteLine("uso: Dolly.Cli CARTELLA --test-api PORTA [--mpv \"opzioni\"] [--screen N] [--windowed] [--remote] [--autoresume] [--reset-playlist]");
