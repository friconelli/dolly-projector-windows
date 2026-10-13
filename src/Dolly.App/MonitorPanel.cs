using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Dolly;

namespace Dolly.App;

/// Barra di avanzamento sottile in stile lettore professionale.
public sealed class ScrubBar : Grid
{
    readonly Border track = new() { Height = 5, CornerRadius = new CornerRadius(3), Background = Theme.Track, VerticalAlignment = VerticalAlignment.Center };
    readonly Border fill = new() { Height = 5, CornerRadius = new CornerRadius(3), Background = Theme.Accent, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
    double value, total = 1; bool enabled; double? drag; readonly Action<double> commit;
    public ScrubBar(Action<double> commit)
    {
        this.commit = commit; Height = 20; Background = Brushes.Transparent; Cursor = Cursors.Hand; Children.Add(track); Children.Add(fill);
        SizeChanged += (_, _) => Paint();
        MouseLeftButtonDown += (_, e) => { if (!enabled) return; CaptureMouse(); Move(e); };
        MouseMove += (_, e) => { if (IsMouseCaptured) Move(e); };
        MouseLeftButtonUp += (_, _) => { if (!IsMouseCaptured) return; ReleaseMouseCapture(); if (drag is double d) { this.commit(d); Task.Delay(400).ContinueWith(_ => Dispatcher.BeginInvoke(() => { drag = null; Paint(); })); } };
    }
    void Move(MouseEventArgs e) { var x = e.GetPosition(this).X; drag = Math.Max(0, Math.Min(1, x / Math.Max(1, ActualWidth))) * total; Paint(); }
    public void Set(double v, double t, bool en) { value = v; total = Math.Max(1, t); enabled = en; fill.Background = en ? Theme.Accent : Theme.Faint; if (drag == null) Paint(); }
    void Paint() { var f = Math.Max(0, Math.Min(1, (drag ?? value) / total)); fill.Width = ActualWidth * f; }
}

public sealed class MonitorPanel : Grid
{
    readonly Ctx c;
    readonly Image img = new() { Stretch = Stretch.Uniform };
    readonly TextBlock center = Ui.Text("", 20, null, new SolidColorBrush(Color.FromArgb(190, 255, 255, 255)), true), badgeText = Ui.Text("", 10, FontWeights.Bold, Brushes.White), note = Ui.Text("", 10, null, new SolidColorBrush(Color.FromArgb(128, 255, 255, 255)));
    readonly Border badge = new() { CornerRadius = new CornerRadius(20), Padding = new Thickness(8, 3, 8, 3), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(10) };
    readonly TextBlock tc = Ui.Text("", 30, FontWeights.Medium), rem = Ui.Text("", 14, null, Theme.Muted), tot = Ui.Text("", 14, null, Theme.Faint), gapLabel = Ui.Text("", 13, null, Theme.Muted);
    readonly ScrubBar scrub; readonly Button play, skip, plus; readonly TextBlock playGlyph = Ui.Glyph("", 22, Brushes.White);
    readonly Ui.PillToggle loop, hidePic, mute; readonly Slider vol = new() { Minimum = 0, Maximum = 130, Width = 130, VerticalAlignment = VerticalAlignment.Center, IsMoveToPointEnabled = true }; readonly TextBlock volText = Ui.Text("", 11, null, Theme.Muted);
    DateTime volAt = DateTime.MinValue; bool volSet; bool previewOn = true; string lastMode = "";

    public MonitorPanel(Ctx c)
    {
        this.c = c; Margin = new Thickness(16);
        var sp = new StackPanel();
        // monitor 16:9
        var scr = new Grid { Background = Brushes.Black };
        scr.Children.Add(img); center.TextAlignment = TextAlignment.Center; center.HorizontalAlignment = HorizontalAlignment.Center; scr.Children.Add(center);
        badge.Child = badgeText; scr.Children.Add(badge); note.HorizontalAlignment = HorizontalAlignment.Right; note.VerticalAlignment = VerticalAlignment.Top; note.Margin = new Thickness(10); scr.Children.Add(note);
        var frame = new Border { Child = scr, CornerRadius = new CornerRadius(10), ClipToBounds = true, BorderBrush = Theme.Line, BorderThickness = new Thickness(1), Background = Brushes.Black };
        frame.SizeChanged += (_, e) => frame.Height = e.NewSize.Width * 9 / 16;
        frame.ContextMenu = new ContextMenu(); var mi = new MenuItem { Header = L.Tr("Anteprima attiva"), IsCheckable = true, IsChecked = Settings.GetBool("preview") ?? true };
        mi.Click += (_, _) => { Settings.Set("preview", mi.IsChecked); previewOn = mi.IsChecked; c.E?.SetPreview(mi.IsChecked); if (!mi.IsChecked) img.Source = null; }; frame.ContextMenu.Items.Add(mi);
        previewOn = Settings.GetBool("preview") ?? true;
        sp.Children.Add(frame);
        // timecode + scrub
        var t = new Grid { Margin = new Thickness(0, 14, 0, 0) }; t.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); t.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); t.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); t.ColumnDefinitions.Add(new ColumnDefinition());
        tc.FontFamily = Ui.Mono; rem.FontFamily = Ui.Mono; tot.FontFamily = Ui.Mono; rem.Margin = new Thickness(14, 0, 0, 0); tot.Margin = new Thickness(10, 0, 0, 0); gapLabel.Margin = new Thickness(14, 0, 0, 0);
        Grid.SetColumn(tc, 0); Grid.SetColumn(rem, 1); Grid.SetColumn(tot, 2); Grid.SetColumn(gapLabel, 1); Grid.SetColumnSpan(gapLabel, 3);
        t.Children.Add(tc); t.Children.Add(rem); t.Children.Add(tot); t.Children.Add(gapLabel); sp.Children.Add(t);
        scrub = new ScrubBar(v => c.Cmd("seek", ("v", v))) { Margin = new Thickness(0, 6, 0, 0) }; sp.Children.Add(scrub);
        // trasporto
        var prev = Ui.IconBtn("", L.Tr("Inizio del film / precedente"), () => c.Cmd("prev"), 18, Theme.Text);
        play = new Button { Content = playGlyph, Width = 54, Height = 54, ToolTip = L.Tr("Avvia / pausa (spazio)"), Background = Theme.Accent, BorderThickness = new Thickness(0), Padding = new Thickness(0) };
        play.Template = RoundTemplate(); play.Click += (_, _) => c.Cmd("toggle");
        var next = Ui.IconBtn("", L.Tr("Successivo"), () => c.Cmd("next"), 18, Theme.Text);
        var stop = Ui.IconBtn("", L.Tr("Ferma e schermo nero"), () => { if (c.Confirm(L.Tr("Fermare e tornare allo schermo nero?"))) c.Cmd("stop"); }, 16, Theme.Text);
        skip = Ui.Btn(L.Tr("Salta"), () => c.Cmd("skipgap")); plus = Ui.Btn(L.Tr("+1 min"), () => c.Cmd("extend", ("v", 60)));
        var tr = Ui.H(18, prev, play, next, stop, skip, plus); tr.HorizontalAlignment = HorizontalAlignment.Center; tr.Margin = new Thickness(0, 16, 0, 0); sp.Children.Add(tr);
        // interruttori + volume
        loop = new Ui.PillToggle("", L.Tr("Ripeti"), on => c.Set("loop-file", on ? "inf" : "no"), L.Tr("Il film in corso ricomincia da capo; togli per proseguire con la scaletta"));
        hidePic = new Ui.PillToggle("", L.Tr("Nero immagine"), on => c.Cmd("vid", ("v", on ? "no" : "auto")), L.Tr("Toglie l’immagine, l’audio continua"));
        mute = new Ui.PillToggle("", L.Tr("Muto"), on => c.Set("mute", on));
        vol.ValueChanged += (_, e) => { if (volSet) return; volAt = DateTime.UtcNow; volText.Text = ((int)e.NewValue).ToString(); c.Set("volume", e.NewValue); };
        var row = new Grid { Margin = new Thickness(0, 16, 0, 0) }; row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var pills = Ui.H(8, loop, hidePic, mute); Grid.SetColumn(pills, 0); row.Children.Add(pills);
        volText.Width = 28; volText.TextAlignment = TextAlignment.Right; volText.FontFamily = Ui.Mono;
        var v = Ui.H(6, Ui.Glyph("", 12), vol, Ui.Glyph("", 12), volText); Grid.SetColumn(v, 2); row.Children.Add(v);
        sp.Children.Add(row);
        Children.Add(sp);
    }
    static ControlTemplate RoundTemplate()
    {
        var t = new ControlTemplate(typeof(Button));
        var f = new FrameworkElementFactory(typeof(Border)); f.SetValue(Border.CornerRadiusProperty, new CornerRadius(27)); f.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Button.BackgroundProperty));
        var p = new FrameworkElementFactory(typeof(ContentPresenter)); p.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center); p.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center); f.AppendChild(p);
        t.VisualTree = f; return t;
    }

    /// Un nuovo JPEG dell'anteprima dal motore (thread qualsiasi).
    public void SetPreview(byte[]? jpg)
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (jpg == null || !previewOn) { img.Source = null; return; }
            try { var b = new BitmapImage(); using var ms = new MemoryStream(jpg); b.BeginInit(); b.CacheOption = BitmapCacheOption.OnLoad; b.StreamSource = ms; b.EndInit(); b.Freeze(); img.Source = b; } catch { }
        });
    }

    public void Update(Snap s)
    {
        bool g = s.Mode == "gap" || s.Mode == "wait";
        tc.Text = g ? U.Fmt(s.Left) : TC(s.Time); tc.Foreground = g ? Theme.Orange : Theme.Text;
        rem.Visibility = tot.Visibility = g ? Visibility.Collapsed : Visibility.Visible; gapLabel.Visibility = g ? Visibility.Visible : Visibility.Collapsed;
        gapLabel.Text = s.Mode == "gap" ? L.Trf("nero prima di “%@”", s.Next >= 0 && s.Next < s.Items.Count ? s.Items[s.Next].Label : "") : L.Tr("intervallo");
        rem.Text = "−" + TC(Math.Max(0, s.Dur - s.Time)); tot.Text = "/ " + TC(s.Dur);
        scrub.Set(s.Time, s.Dur, s.Mode == "playing");
        playGlyph.Text = s.Mode == "playing" && !s.Pause ? "" : "";
        skip.Visibility = g ? Visibility.Visible : Visibility.Collapsed; plus.Visibility = s.Mode == "wait" ? Visibility.Visible : Visibility.Collapsed;
        loop.Set(s.S("loop-file") is { Length: > 0 } lf && lf != "no"); hidePic.Set(s.Video.Count > 0 && !s.Video.Any(x => x.Sel)); mute.Set(s.B("mute"));
        if (!vol.IsMouseCaptureWithin && (DateTime.UtcNow - volAt).TotalMilliseconds > 600) { volSet = true; vol.Value = Math.Min(130, s.D("volume", 100)); volText.Text = ((int)vol.Value).ToString(); volSet = false; }
        badgeText.Text = s.Mode == "playing" ? L.Tr("PROIETTORE") : s.Mode == "wait" ? L.Tr("INTERVALLO") : L.Tr("NERO");
        badge.Background = s.Mode == "playing" ? new SolidColorBrush(Color.FromArgb(230, 224, 57, 46)) : new SolidColorBrush(Color.FromArgb(46, 255, 255, 255));
        bool showImg = previewOn && s.Mode == "playing" && img.Source != null;
        img.Visibility = showImg ? Visibility.Visible : Visibility.Collapsed;
        if (s.Mode == "wait") center.Text = s.Idx >= 0 && s.Idx < s.Items.Count && s.Items[s.Idx].Kind == "pausa" ? s.Items[s.Idx].Text + "\n" + U.Fmt(s.Left) : "";
        else if (s.Mode == "playing") center.Text = showImg ? "" : (previewOn ? L.Tr("Anteprima in arrivo…") : L.Tr("Anteprima spenta"));
        else center.Text = L.Tr("NERO");
        center.FontSize = s.Mode == "wait" ? 22 : 13;
        note.Text = previewOn && s.Mode == "playing" ? L.Tr("anteprima a scatti") : "";
        if (s.Mode != "playing" && lastMode == "playing") img.Source = null;
        lastMode = s.Mode;
    }
    static string TC(double s) { int t = Math.Max(0, (int)s); return $"{t / 3600:00}:{t % 3600 / 60:00}:{t % 60:00}"; }
}
