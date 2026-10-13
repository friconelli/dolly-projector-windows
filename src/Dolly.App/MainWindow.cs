using System.Diagnostics;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Dolly;
using WF = System.Windows.Forms;

namespace Dolly.App;

public sealed class MainWindow : Window
{
    readonly Ctx c; HeaderBar header = null!; ScalettaPanel scal = null!; MonitorPanel mon = null!; Inspector insp = null!;
    readonly DockPanel root = new(); readonly Border starting;
    Snap last = new();

    public MainWindow(Ctx c)
    {
        this.c = c; c.Owner = this;
        Title = "Dolly Projector"; Width = 1320; Height = 900; MinWidth = 1100; MinHeight = 640; Background = Theme.Bg; WindowStartupLocation = WindowStartupLocation.CenterScreen;
        starting = new Border { Background = new SolidColorBrush(Color.FromArgb(220, Theme.Dark ? (byte)28 : (byte)244, Theme.Dark ? (byte)28 : (byte)244, Theme.Dark ? (byte)30 : (byte)246)), Child = Ui.V(10, Ui.Text(L.Tr("Avvio del player…"), 15, null, null), new ProgressBar { IsIndeterminate = true, Width = 220, Height = 4 }) };
        ((StackPanel)starting.Child).HorizontalAlignment = HorizontalAlignment.Center; ((StackPanel)starting.Child).VerticalAlignment = VerticalAlignment.Center;
        Content = new Grid { Children = { root, starting } };
        BuildContent();
        PreviewKeyDown += OnKey;
        Loaded += (_, _) => Task.Delay(300).ContinueWith(_ => Dispatcher.BeginInvoke(() => Keyboard.ClearFocus()));   // nessun campo selezionato all'avvio: la tastiera comanda subito il player
    }

    /// (Ri)costruisce menu e pannelli: serve anche quando cambia la lingua.
    public void BuildContent()
    {
        root.Children.Clear();
        var menu = BuildMenu(); DockPanel.SetDock(menu, Dock.Top); root.Children.Add(menu);
        header = new HeaderBar(c); DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
        var g = new Grid(); g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(340) }); g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 520 }); g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(360) });
        scal = new ScalettaPanel(c); mon = new MonitorPanel(c); insp = new Inspector(c) { Main = this };
        Grid.SetColumn(scal, 0); Grid.SetColumn(mon, 1); Grid.SetColumn(insp, 2);
        var l1 = new Border { Width = 1, Background = Theme.Line, HorizontalAlignment = HorizontalAlignment.Right }; var l2 = new Border { Width = 1, Background = Theme.Line, HorizontalAlignment = HorizontalAlignment.Left };
        Grid.SetColumn(l1, 0); Grid.SetColumn(l2, 2);
        g.Children.Add(scal); g.Children.Add(mon); g.Children.Add(insp); g.Children.Add(l1); g.Children.Add(l2);
        root.Children.Add(g);
        if (starting.Child is StackPanel sp && sp.Children[0] is TextBlock tb) tb.Text = L.Tr("Avvio del player…");
        Apply(last);
    }

    public void SetStarting(bool on) => starting.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
    public void OnSnapshot(JsonObject j) => Dispatcher.BeginInvoke(() => Apply(new Snap(j)));
    public void OnPreview(byte[]? jpg) => mon.SetPreview(jpg);
    void Apply(Snap s)
    {
        last = s; c.S = s;
        try { header.Update(s); scal.Update(s); mon.Update(s); insp.Update(s); } catch (Exception e) { U.Log("aggiornamento interfaccia:", e); }
    }

    // MARK: menu
    Menu BuildMenu()
    {
        var m = new Menu { Background = Theme.Panel, Foreground = Theme.Text };
        var app = new MenuItem { Header = "Dolly Projector" }; m.Items.Add(app);
        app.Items.Add(Mi(L.Tr("Cambia cartella dei film…"), ChangeFolder));
        var sc = new MenuItem { Header = L.Tr("Schermo della sala") }; sc.SubmenuOpened += (_, _) => FillScreens(sc); sc.Items.Add(new MenuItem()); app.Items.Add(sc);
        app.Items.Add(Mi(L.Tr("Controlla aggiornamenti…"), () => Updater.Check(c)));
        var rm = new MenuItem { Header = L.Tr("Telecomando dal telefono"), IsCheckable = true, IsChecked = RemoteControl.Shared.Enabled };
        rm.Click += (_, _) => ToggleRemote(rm); app.Items.Add(rm);
        app.Items.Add(Mi(L.Tr("Dona"), () => Process.Start(new ProcessStartInfo("https://buymeacoffee.com/friconellit") { UseShellExecute = true })));
        var lm = new MenuItem { Header = L.Tr("Lingua") };
        foreach (var (title, code) in new[] { (L.Tr("Automatica"), "auto"), ("Italiano", "it"), ("English", "en") })
        {
            var k = code; var it = new MenuItem { Header = title, IsCheckable = true, IsChecked = Lang.Setting == code }; it.Click += (_, _) => { Lang.Setting = k; BuildContent(); }; lm.Items.Add(it);
        }
        app.Items.Add(lm); app.Items.Add(new Separator());
        app.Items.Add(Mi(L.Tr("Esci"), Close));
        return m;
    }
    static MenuItem Mi(string h, Action a) { var mi = new MenuItem { Header = h }; mi.Click += (_, _) => a(); return mi; }
    void ToggleRemote(MenuItem rm)
    {
        var r = RemoteControl.Shared; r.SetEnabled(rm.IsChecked); rm.IsChecked = r.Enabled;
        if (!r.Enabled) return;
        Dispatcher.BeginInvoke(async () =>
        {
            await Task.Delay(400);
            var msg = r.Urls.Count > 0 ? L.Trf("Sul telefono (stessa rete del PC, anche senza internet) inquadra il codice o apri:\n%@\n\nPIN: %@", r.Urls[0], r.Pin) : (r.Problem ?? L.Tr("Nessuna rete trovata: collega il PC al Wi-Fi o a un router."));
            var w = new Window { Title = L.Tr("Telecomando attivo"), Owner = this, SizeToContent = SizeToContent.WidthAndHeight, WindowStartupLocation = WindowStartupLocation.CenterOwner, ResizeMode = ResizeMode.NoResize, Background = Theme.Bg };
            var sp = new StackPanel { Margin = new Thickness(20) };
            if (r.Urls.Count > 0 && RemoteCard.Qr(r.Urls[0]) is { } q) sp.Children.Add(new Image { Source = q, Width = 160, Height = 160, Margin = new Thickness(0, 0, 0, 12), HorizontalAlignment = HorizontalAlignment.Center });
            sp.Children.Add(new TextBlock { Text = msg, Foreground = Theme.Text, TextWrapping = TextWrapping.Wrap, MaxWidth = 380 });
            var ok = new Button { Content = "OK", Padding = new Thickness(24, 5, 24, 5), HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0), IsDefault = true }; ok.Click += (_, _) => w.Close(); sp.Children.Add(ok);
            w.Content = sp; w.ShowDialog();
        });
    }

    // MARK: schermo della sala
    public static WF.Screen[] Screens => WF.Screen.AllScreens;
    void FillScreens(MenuItem sc)
    {
        sc.Items.Clear(); var all = Screens; int cur = AppState.ScreenIndex; bool win = AppState.WindowedMode;
        for (int i = 0; i < all.Length; i++)
        {
            int ix = i; var s = all[i]; var name = $"{i + 1} · {s.DeviceName.TrimStart('\\', '.')}  {s.Bounds.Width}×{s.Bounds.Height}{(s.Primary ? L.Tr(" (principale)") : "")}";
            var mi = new MenuItem { Header = name, IsChecked = !win && ix == cur }; mi.Click += (_, _) => PickScreen(ix); sc.Items.Add(mi);
        }
        sc.Items.Add(new Separator());
        var w = new MenuItem { Header = L.Tr("Finestra (per le prove)"), IsChecked = win }; w.Click += (_, _) => PickScreen(-1); sc.Items.Add(w);
    }
    void PickScreen(int tag)
    {
        void Apply_() { Settings.Set("screen", tag); ((App)Application.Current).RestartEngine(this); }
        if (tag >= 0 && Screens.Length == 1) { if (c.Confirm(L.Tr("Con un solo schermo il player occuperà tutto lo schermo e coprirà i controlli (torni ai controlli con Alt+Tab). Continuare?"))) Apply_(); }
        else Apply_();
    }
    public void ChangeFolder() => ((App)Application.Current).ChangeFolder(this);

    /// Se il player va a schermo intero sullo stesso schermo dei controlli, la finestra dei controlli passa su un altro schermo (se c'è).
    public void MoveControlsAway()
    {
        var all = Screens; if (AppState.WindowedMode || all.Length < 2) return;
        var target = all[Math.Min(AppState.ScreenIndex, all.Length - 1)];
        var cur = WF.Screen.FromHandle(new WindowInteropHelper(this).Handle);
        if (cur.DeviceName != target.DeviceName) return;
        var other = all.First(s => s.DeviceName != target.DeviceName); var f = other.WorkingArea; var dpi = VisualTreeHelper.GetDpi(this);
        Left = (f.Left + f.Width / 2.0) / dpi.DpiScaleX - Width / 2; Top = (f.Top + f.Height / 2.0) / dpi.DpiScaleY - Height / 2;
    }

    // MARK: tastiera
    void OnKey(object sender, KeyEventArgs e)
    {
        if (c.E == null) return;
        if (Keyboard.FocusedElement is TextBox || Keyboard.FocusedElement is ComboBox { IsEditable: true }) return;   // si sta scrivendo in un campo
        if ((Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Alt)) != 0) return;
        bool big = (Keyboard.Modifiers & ModifierKeys.Shift) != 0; var s = c.S;
        switch (e.Key)
        {
            case Key.Space: c.Cmd("toggle"); break;
            case Key.Left: c.Cmd("seek", ("v", big ? -60 : -10), ("m", "relative")); break;
            case Key.Right: c.Cmd("seek", ("v", big ? 60 : 10), ("m", "relative")); break;
            case Key.Up: c.Set("volume", s.D("volume", 100) + 5); break;
            case Key.Down: c.Set("volume", s.D("volume", 100) - 5); break;
            case Key.M: c.Set("mute", !s.B("mute")); break;
            default: return;
        }
        e.Handled = true;
    }
}
