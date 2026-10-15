using System.Diagnostics;
using System.Windows;
using Dolly;
using Microsoft.Win32;

namespace Dolly.App;

public partial class App : Application
{
    readonly Ctx ctx = new(); MainWindow win = null!; Mutex? single; Engine? engine; readonly object lk = new();
    static string Marker => Path.Combine(Engine.HomePath, "running");   // presente solo mentre l'app è aperta: se c'è all'avvio, la chiusura precedente è stata imprevista
    bool confirmed;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try { Directory.CreateDirectory(Engine.HomePath); var log = new StreamWriter(Path.Combine(Engine.HomePath, "Dolly.log"), true) { AutoFlush = true }; Console.SetError(log); } catch { }   // registro per capire cosa è successo dopo una proiezione
        single = new Mutex(true, "Global\\DollyProjector.app", out bool first);
        if (!first) { MessageBox.Show(L.Tr("Dolly Projector è già aperto."), "Dolly Projector"); Shutdown(); return; }
        Theme.Init();
        DispatcherUnhandledException += (_, a) => { U.Log("errore non gestito:", a.Exception); a.Handled = true; };
        // la cartella dei film è facoltativa: senza, l'app parte con la scaletta vuota (i film si aggiungono dal pulsante + o trascinandoli)
        bool unclean = File.Exists(Marker); try { File.WriteAllText(Marker, ""); } catch { }
        win = new MainWindow(ctx); MainWindow = win; win.Closing += OnClosing; win.Show();
        StartEngine(unclean, false);
    }

    bool ChooseFolder(Window? owner)
    {
        var d = new OpenFolderDialog { Title = L.Tr("Scegli la cartella che contiene i film (mp4, avi, mkv)") }; if (AppState.Folder is string f) d.InitialDirectory = f;
        if (d.ShowDialog(owner) != true) return false; Settings.Set("folder", d.FolderName); return true;
    }

    /// Dove sta mpv: nella cartella dell'app (mpv\mpv.exe), altrimenti DOLLY_MPV, altrimenti nel PATH.
    static string? FindMpv()
    {
        var inApp = Path.Combine(AppContext.BaseDirectory, "mpv", "mpv.exe"); if (File.Exists(inApp)) return inApp;
        var env = Environment.GetEnvironmentVariable("DOLLY_MPV"); if (env != null && File.Exists(env)) return env;
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)) { try { var p = Path.Combine(dir.Trim('"'), "mpv.exe"); if (File.Exists(p)) return p; } catch { } }
        return null;
    }

    /// Il player può impiegare qualche secondo ad avviarsi: la finestra c'è subito.
    void StartEngine(bool autoresume, bool resetPlaylist)
    {
        var f = AppState.Folder ?? "";
        win.SetStarting(true);
        bool windowed = AppState.WindowedMode; int screen = AppState.ScreenIndex; var mpv = FindMpv();
        Task.Run(() =>
        {
            Engine? e = null; Exception? err = mpv == null ? new DollyError(L.Tr("Impossibile trovare mpv: reinstalla Dolly Projector.")) : null;
            if (mpv != null) { try { e = new Engine(f, mpv, Array.Empty<string>(), windowed, screen, autoresume, resetPlaylist); } catch (Exception ex) { err = ex; } }
            if (e != null) lock (lk) engine = e;
            Dispatcher.BeginInvoke(() =>
            {
                win.SetStarting(false);
                if (e != null)
                {
                    ctx.E = e; e.Snapshot += win.OnSnapshot; e.PreviewChanged += win.OnPreview; RemoteControl.Shared.Eng = e; win.MoveControlsAway();
                    Task.Run(() => { try { win.OnSnapshot(e.StateObjSync()); } catch { } });
                }
                else ctx.Error(L.Trf("Non riesco ad avviare il player: %@", err?.Message ?? ""));
            });
        });
    }
    void StopEngine()
    {
        Engine? e; lock (lk) { e = engine; engine = null; }
        RemoteControl.Shared.Eng = null; ctx.E = null;
        if (e != null) { e.Snapshot -= win.OnSnapshot; e.PreviewChanged -= win.OnPreview; e.Shutdown(); }
    }
    public void RestartEngine(Window w)
    {
        if (!ctx.Confirm(L.Tr("Cambiare impostazione interrompe la proiezione in corso. Continuare?"))) return;
        StopEngine(); Task.Delay(400).ContinueWith(_ => Dispatcher.BeginInvoke(() => StartEngine(false, false)));
    }
    public void ChangeFolder(Window w)
    {
        var m = ctx.S.Mode;
        if ((m == "playing" || m == "wait" || m == "gap") && !ctx.Confirm(L.Tr("Cambiare cartella interrompe la proiezione in corso e la scaletta riparte dai film della nuova cartella. Continuare?"))) return;
        bool had = AppState.Folder != null;   // la prima volta che si sceglie una cartella la scaletta già costruita resta com'è
        if (!ChooseFolder(w)) return;
        StopEngine(); Task.Delay(400).ContinueWith(_ => Dispatcher.BeginInvoke(() => StartEngine(false, had)));
    }

    void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        var m = ctx.S.Mode;
        if (!confirmed && (m == "playing" || m == "wait") && MessageBox.Show(win, L.Tr("Il film in proiezione si interrompe e lo schermo della sala torna al nero."), L.Tr("Chiudere Dolly Projector?"), MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK) { e.Cancel = true; return; }
        confirmed = true; StopEngine(); try { File.Delete(Marker); } catch { }
    }
    protected override void OnExit(ExitEventArgs e) { StopEngine(); try { single?.ReleaseMutex(); } catch { } base.OnExit(e); }
}
