using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Dolly;

namespace Dolly.App;

/// Pannello destro a schede: ogni scheda si costruisce una volta e si aggiorna ad ogni istantanea.
public sealed class Inspector : Grid
{
    readonly Ctx c; readonly List<Action<Snap>> updaters = new(); readonly List<Border> tabBtns = new(); readonly List<UIElement> pages = new();
    int tab = int.TryParse(Environment.GetEnvironmentVariable("DOLLY_TAB"), out var t0) ? t0 : 0;
    readonly ScrollViewer scroll = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };

    public Inspector(Ctx c)
    {
        this.c = c; Background = Theme.Panel;
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); RowDefinitions.Add(new RowDefinition());
        var tabs = new Grid { Margin = new Thickness(8) };
        var defs = new (string glyph, string name)[] { ("", L.Tr("Scena")), ("", L.Tr("Audio")), ("", L.Tr("Sottotitoli")), ("", L.Tr("Immagine")), ("", L.Tr("Altro")) };
        for (int i = 0; i < defs.Length; i++)
        {
            tabs.ColumnDefinitions.Add(new ColumnDefinition()); int ix = i;
            var b = new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(0, 7, 0, 7), Cursor = Cursors.Hand, Background = Brushes.Transparent, Child = Ui.V(3, Ui.Glyph(defs[i].glyph, 15), Ui.Text(defs[i].name, 10.5, FontWeights.Medium)) };
            ((StackPanel)b.Child).Children[1].SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center);
            b.MouseLeftButtonUp += (_, _) => Select(ix); SetColumn(b, i); tabs.Children.Add(b); tabBtns.Add(b);
        }
        var tb = new Border { Child = tabs, BorderBrush = Theme.Line, BorderThickness = new Thickness(0, 0, 0, 1) }; SetRow(tb, 0); Children.Add(tb);
        pages.Add(Page(SceneTab())); pages.Add(Page(AudioTab())); pages.Add(Page(SubsTab())); pages.Add(Page(ImageTab())); pages.Add(Page(MoreTab()));
        SetRow(scroll, 1); Children.Add(scroll); Select(Math.Max(0, Math.Min(4, tab)));
    }
    static UIElement Page(params UIElement[] cards) { var sp = new StackPanel { Margin = new Thickness(12) }; for (int i = 0; i < cards.Length; i++) { if (i > 0) ((FrameworkElement)cards[i]).Margin = new Thickness(0, 12, 0, 0); sp.Children.Add(cards[i]); } return sp; }
    void Select(int i)
    {
        tab = i; scroll.Content = pages[i];
        for (int k = 0; k < tabBtns.Count; k++)
        {
            bool on = k == i; tabBtns[k].Background = on ? Theme.AccentSoft : Brushes.Transparent;
            var sp = (StackPanel)tabBtns[k].Child; ((TextBlock)sp.Children[0]).Foreground = on ? Theme.Accent : Theme.Muted; ((TextBlock)sp.Children[1]).Foreground = on ? Theme.Accent : Theme.Muted;
        }
    }
    public void Update(Snap s) { foreach (var u in updaters) u(s); }

    // MARK: helpers
    Ui.Field Field(string label, string ph, Func<Snap, string> get, Action<string> commit, double w = 90) { var f = new Ui.Field(label, ph, commit, w); updaters.Add(s => f.Set(get(s))); return f; }
    PropSlider Slider(string prop, string label, double lo, double hi, double step, double? reset = null, Func<double, string>? fmt = null)
    {
        var sl = new PropSlider(label, lo, hi, step, v => c.Set(prop, v), fmt, reset); updaters.Add(s => sl.Set(s.D(prop, reset ?? lo))); return sl;
    }
    CheckBox Check(string label, Func<Snap, bool> get, Action<bool> set) { var ck = Ui.Check(label, set); updaters.Add(s => ck.IsChecked = get(s)); return ck; }

    // MARK: Scena
    UIElement[] SceneTab()
    {
        var defsubs = new[] { ("file", L.Tr("Come nel file")), ("none", L.Tr("Nessuno")), ("forced", L.Tr("Solo forzati")), ("full", L.Tr("Completi")) };
        var seg = new List<Border>(); var segRow = new Grid();
        for (int i = 0; i < defsubs.Length; i++)
        {
            segRow.ColumnDefinitions.Add(new ColumnDefinition()); var key = defsubs[i].Item1;
            var b = new Border { Padding = new Thickness(4, 5, 4, 5), Cursor = Cursors.Hand, Child = new TextBlock { Text = defsubs[i].Item2, FontSize = 11.5, HorizontalAlignment = HorizontalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis }, BorderBrush = Theme.Line, BorderThickness = new Thickness(i == 0 ? 1 : 0, 1, 1, 1), CornerRadius = new CornerRadius(i == 0 ? 6 : 0, i == defsubs.Length - 1 ? 6 : 0, i == defsubs.Length - 1 ? 6 : 0, i == 0 ? 6 : 0) };
            b.MouseLeftButtonUp += (_, _) => c.Cmd("defaults", ("defsub", key)); SetColumn(b, i); segRow.Children.Add(b); seg.Add(b);
        }
        var desc = Ui.Muted("");
        updaters.Add(s =>
        {
            for (int i = 0; i < seg.Count; i++) { bool on = defsubs[i].Item1 == s.DefSub; seg[i].Background = on ? Theme.Accent : Brushes.Transparent; ((TextBlock)seg[i].Child).Foreground = on ? Brushes.White : Theme.Text; }
            desc.Text = s.DefSub switch { "file" => L.Tr("Si accende la traccia che il file indica come predefinita."), "none" => L.Tr("Nessun sottotitolo, a meno che un film non ne chieda."), "forced" => L.Tr("Solo i sottotitoli “forzati”: le traduzioni dei dialoghi stranieri."), _ => L.Tr("Sottotitoli completi nella lingua dell’audio.") };
        });
        var folderName = Ui.Text("", 13); var lib = new StackPanel { Visibility = Visibility.Collapsed }; bool libOpen = false; var libToggle = new Button { Content = L.Tr("Mostra l'elenco dei film"), HorizontalAlignment = HorizontalAlignment.Left, BorderThickness = new Thickness(0), Background = Brushes.Transparent, Foreground = Theme.Accent, Padding = new Thickness(0) };
        libToggle.Click += (_, _) => { libOpen = !libOpen; lib.Visibility = libOpen ? Visibility.Visible : Visibility.Collapsed; libToggle.Content = libOpen ? L.Tr("Nascondi l'elenco") : L.Tr("Mostra l'elenco dei film"); };
        string libSig = "";
        updaters.Add(s =>
        {
            folderName.Text = System.IO.Path.GetFileName(s.Folder.TrimEnd('\\', '/')) is { Length: > 0 } n ? n : s.Folder;
            var sig = string.Join("|", s.Lib); if (sig == libSig) return; libSig = sig; lib.Children.Clear();
            for (int i = 0; i < s.Lib.Count; i++) { int ix = i; var g = new Grid(); g.ColumnDefinitions.Add(new ColumnDefinition()); g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); var t = Ui.Text(System.IO.Path.GetFileNameWithoutExtension(s.Lib[i]), 12.5); var a = Ui.IconBtn("", L.Tr("Aggiungi alla scaletta"), () => c.Cmd("addlib", ("i", ix)), 12); Grid.SetColumn(a, 1); g.Children.Add(t); g.Children.Add(a); lib.Children.Add(g); }
        });
        var chg = Ui.Btn(L.Tr("Cambia…"), () => Main?.ChangeFolder(), L.Tr("La scaletta riparte dai film della nuova cartella")); var frow = new Grid(); frow.ColumnDefinitions.Add(new ColumnDefinition()); frow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); Grid.SetColumn(chg, 1); frow.Children.Add(folderName); frow.Children.Add(chg);
        return new UIElement[]
        {
            Ui.Card(L.Tr("Nero e avanzamento"),
                Field(L.Tr("Nero iniziale (s)"), "0", s => Ui.Num(s.DefPre), v => c.Cmd("defaults", ("defpre", Ui.ParseNum(v) ?? 0))),
                Field(L.Tr("Nero finale (s)"), "0", s => Ui.Num(s.DefPost), v => c.Cmd("defaults", ("defpost", Ui.ParseNum(v) ?? 0))),
                Ui.Muted(L.Tr("Ogni elemento può avere il suo valore (icona delle impostazioni nella scaletta). Tra due elementi il nero è: finale del primo + iniziale del secondo.")),
                Check(L.Tr("Avanza automaticamente"), s => s.Auto, v => c.Cmd("defaults", ("auto", v))),
                Check(L.Tr("Ripeti la scaletta"), s => s.Loop, v => c.Cmd("defaults", ("loop", v)))),
            Ui.Card(L.Tr("Sottotitoli della scena"), segRow, desc),
            Ui.Card(L.Tr("Cartella dei film"), frow, libToggle, lib)
        };
    }
    public MainWindow? Main { get; set; }

    // MARK: Audio
    UIElement[] AudioTab()
    {
        var dev = new Pick(ix => { }); var devs = new List<string>();
        var devPick = new Pick(ix => { if (ix >= 0 && ix < devs.Count) c.Set("audio-device", devs[ix]); });
        var track = new Pick(id => c.Cmd("aid", ("v", id.ToString())));
        var chans = new[] { ("auto-safe", L.Tr("Originali")), ("stereo", L.Tr("Stereo (mix)")), ("mono", L.Tr("Mono")), ("5.1", "5.1") };
        var chPick = new Pick(ix => c.Set("audio-channels", chans[ix].Item1));
        updaters.Add(s =>
        {
            devs = s.Adevs.Select(a => a.Id).ToList();
            devPick.Fill(s.Adevs.Select((a, ix) => (ix, a.T)), Math.Max(-1, devs.IndexOf(s.S("audio-device"))));
            track.Fill(s.Audio.Select(a => (a.Id, a.T)), s.Audio.FirstOrDefault(a => a.Sel)?.Id ?? 0); track.IsEnabled = s.Audio.Count > 0;
            var cur = s.S("audio-channels"); chPick.Fill(chans.Select((x, ix) => (ix, x.Item2)), Math.Max(0, Array.FindIndex(chans, x => x.Item1 == (cur.Length == 0 ? "auto-safe" : cur))));
        });
        return new UIElement[] { Ui.Card(L.Tr("Uscita e tracce"), Ui.Row(L.Tr("Uscita"), devPick), Ui.Row(L.Tr("Traccia"), track), Ui.Row(L.Tr("Canali"), chPick), Slider("audio-delay", L.Tr("Sincronia"), -5, 5, .05, 0, v => v.ToString("0.00") + " s")) };
    }

    // MARK: Sottotitoli
    static readonly Dictionary<string, JsonObject> SubPresets = new()
    {
        ["classic"] = new() { ["sub-font"] = "Segoe UI", ["sub-font-size"] = 42, ["sub-color"] = "#FFFFFF", ["sub-border-color"] = "#000000", ["sub-border-size"] = 2.2, ["sub-back-color"] = "#00000000", ["sub-shadow-offset"] = 1, ["sub-bold"] = false, ["sub-italic"] = false, ["sub-pos"] = 96 },
        ["yellow"] = new() { ["sub-font"] = "Arial", ["sub-font-size"] = 44, ["sub-color"] = "#FFE000", ["sub-border-color"] = "#000000", ["sub-border-size"] = 3.5, ["sub-back-color"] = "#00000000", ["sub-shadow-offset"] = 1.5, ["sub-bold"] = true, ["sub-italic"] = false, ["sub-pos"] = 96 },
        ["big"] = new() { ["sub-font"] = "Segoe UI", ["sub-font-size"] = 58, ["sub-color"] = "#FFFFFF", ["sub-border-color"] = "#000000", ["sub-border-size"] = 4, ["sub-back-color"] = "#80000000", ["sub-shadow-offset"] = 0, ["sub-bold"] = true, ["sub-italic"] = false, ["sub-pos"] = 94 }
    };
    static readonly string[] FontNames = Fonts.SystemFontFamilies.Select(f => f.Source).OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase).ToArray();

    /// Campione di colore: apre il selettore di Windows e manda il colore a mpv (#RRGGBB oppure #AARRGGBB).
    UIElement Swatch(string label, string key, bool alpha)
    {
        var box = new Border { Width = 52, Height = 28, CornerRadius = new CornerRadius(6), BorderBrush = Theme.Line, BorderThickness = new Thickness(1), Cursor = Cursors.Hand, ToolTip = L.Tr("Scegli colore") }; string cur = "#FFFFFF";
        Color Parse(string h) { h = h.TrimStart('#'); if (h.Length == 8) return Color.FromArgb(Convert.ToByte(h[..2], 16), Convert.ToByte(h[2..4], 16), Convert.ToByte(h[4..6], 16), Convert.ToByte(h[6..8], 16)); if (h.Length == 6) return Color.FromRgb(Convert.ToByte(h[..2], 16), Convert.ToByte(h[2..4], 16), Convert.ToByte(h[4..6], 16)); return Colors.White; }
        updaters.Add(s => { var v = s.S(key); if (v.Length == 0) v = "#FFFFFF"; cur = v; try { box.Background = new SolidColorBrush(Parse(v)); } catch { } });
        box.MouseLeftButtonUp += (_, _) =>
        {
            var col = Parse(cur); var d = new System.Windows.Forms.ColorDialog { FullOpen = true, Color = System.Drawing.Color.FromArgb(255, col.R, col.G, col.B) };
            if (d.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
            int a = alpha ? (cur.TrimStart('#').Length == 8 ? Convert.ToInt32(cur.TrimStart('#')[..2], 16) : 255) : 255; if (alpha && a == 0) a = 128;
            c.Set(key, alpha ? $"#{a:X2}{d.Color.R:X2}{d.Color.G:X2}{d.Color.B:X2}" : $"#{d.Color.R:X2}{d.Color.G:X2}{d.Color.B:X2}");
        };
        return Ui.V(5, box, Ui.Text(label, 11, null, Theme.Muted));
    }
    UIElement[] SubsTab()
    {
        var track = new Pick(id => c.Cmd("sid", ("v", id < 0 ? "no" : id.ToString())));
        updaters.Add(s => track.Fill(new[] { (-1, L.Tr("Nessuno")) }.Concat(s.Sub.Select(x => (x.Id, x.T))), s.Sub.FirstOrDefault(x => x.Sel)?.Id ?? -1));
        var show = Check(L.Tr("Mostra sottotitoli"), s => s.P("sub-visibility").B() ?? true, v => c.Set("sub-visibility", v));
        var warn = Ui.Text("ⓘ " + L.Tr("Questa traccia è un’immagine (PGS/DVD): font e colori non si possono cambiare, valgono solo posizione e scala."), 11.5, null, Theme.Orange, true);
        updaters.Add(s => { var t = s.Sub.FirstOrDefault(x => x.Sel)?.T.ToLowerInvariant() ?? ""; warn.Visibility = t.Contains("pgs") || t.Contains("dvd_sub") || t.Contains("dvb") || t.Contains("xsub") ? Visibility.Visible : Visibility.Collapsed; });
        var presets = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var (key, name) in new[] { ("classic", L.Tr("Classico")), ("yellow", L.Tr("Giallo")), ("big", L.Tr("Grande")) }) { var k = key; var b = Ui.Btn(name, () => c.Cmd("setmany", ("props", SubPresets[k]))); b.Margin = new Thickness(0, 0, 6, 0); presets.Children.Add(b); }
        presets.Children.Add(Ui.Btn(L.Tr("Ripristina"), () => { if (c.Confirm(L.Tr("Ripristinare lo stile iniziale dei sottotitoli (font, colori, dimensione, bordo, posizione)?"))) c.Cmd("resetsubstyle"); }, L.Tr("Riporta tutto lo stile dei sottotitoli allo stato iniziale")));
        var fontPick = new ComboBox { MinHeight = 28, HorizontalAlignment = HorizontalAlignment.Stretch, IsEditable = false }; bool fq = false;
        foreach (var f in FontNames) fontPick.Items.Add(f);
        fontPick.SelectionChanged += (_, _) => { if (!fq && fontPick.SelectedItem is string f) c.Set("sub-font", f); };
        var bold = new CheckBox { Content = "B", FontWeight = FontWeights.Bold, ToolTip = L.Tr("Grassetto") }; bold.Click += (_, _) => c.Set("sub-bold", bold.IsChecked == true);
        var ital = new CheckBox { Content = "I", FontStyle = FontStyles.Italic, ToolTip = L.Tr("Corsivo") }; ital.Click += (_, _) => c.Set("sub-italic", ital.IsChecked == true);
        updaters.Add(s => { fq = true; var f = s.S("sub-font"); if (f.Length > 0 && !fontPick.Items.Contains(f)) fontPick.Items.Insert(0, f); fontPick.SelectedItem = f.Length > 0 ? f : null; fq = false; bold.IsChecked = s.B("sub-bold"); ital.IsChecked = s.B("sub-italic"); });
        var fr = new Grid(); fr.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(112) }); fr.ColumnDefinitions.Add(new ColumnDefinition()); fr.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); fr.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var fl = Ui.Text(L.Tr("Font"), 12.5, null, Theme.Muted); SetColumn(fl, 0); SetColumn(fontPick, 1); bold.Margin = new Thickness(8, 0, 0, 0); ital.Margin = new Thickness(8, 0, 0, 0); SetColumn(bold, 2); SetColumn(ital, 3); fr.Children.Add(fl); fr.Children.Add(fontPick); fr.Children.Add(bold); fr.Children.Add(ital);
        var sw = new Grid(); for (int i = 0; i < 3; i++) sw.ColumnDefinitions.Add(new ColumnDefinition());
        var s1 = Swatch(L.Tr("Testo"), "sub-color", false); var s2 = Swatch(L.Tr("Bordo"), "sub-border-color", false); var s3 = Swatch(L.Tr("Sfondo"), "sub-back-color", true); SetColumn(s1, 0); SetColumn(s2, 1); SetColumn(s3, 2); sw.Children.Add(s1); sw.Children.Add(s2); sw.Children.Add(s3);
        var bgAlpha = new PropSlider(L.Tr("Opacità sfondo"), 0, 255, 5, v => { var cur = c.S.S("sub-back-color").TrimStart('#'); var rgb = cur.Length == 8 ? cur[2..] : cur.Length == 6 ? cur : "000000"; c.Set("sub-back-color", $"#{(int)v:X2}{rgb}"); }, v => $"{(int)(v / 255 * 100)}%");
        updaters.Add(s => { var cur = s.S("sub-back-color").TrimStart('#'); bgAlpha.Set(cur.Length == 8 ? Convert.ToInt32(cur[..2], 16) : 0); });
        var ass = Check(L.Tr("Usa il mio stile anche sui sottotitoli già stilizzati (ASS)"), s => s.S("sub-ass-override") != "scale" && s.S("sub-ass-override") != "no", v => c.Set("sub-ass-override", v ? "force" : "scale")); ass.Content = new TextBlock { Text = L.Tr("Usa il mio stile anche sui sottotitoli già stilizzati (ASS)"), TextWrapping = TextWrapping.Wrap, Foreground = Theme.Text, FontSize = 12.5 };
        return new UIElement[]
        {
            Ui.Card(L.Tr("Traccia"), Ui.Row(L.Tr("Traccia"), track), Slider("sub-delay", L.Tr("Ritardo"), -10, 10, .1, 0, v => v.ToString("0.0") + " s"), show),
            Ui.Card(L.Tr("Stile (salvato come predefinito)"), warn, presets, fr, Slider("sub-font-size", L.Tr("Dimensione"), 20, 120, 1, null, v => ((int)v).ToString()), sw, bgAlpha,
                Slider("sub-border-size", L.Tr("Bordo"), 0, 10, .25), Slider("sub-shadow-offset", L.Tr("Ombra"), 0, 10, .25), Slider("sub-pos", L.Tr("Posizione"), 0, 100, 1, null, v => ((int)v).ToString()), Slider("sub-scale", L.Tr("Scala"), .3, 3, .05, 1), ass)
        };
    }

    // MARK: Immagine
    UIElement[] ImageTab()
    {
        var cards = new List<UIElement>();
        var col = new List<UIElement>(); foreach (var (p, n) in new[] { ("brightness", L.Tr("Luminosità")), ("contrast", L.Tr("Contrasto")), ("saturation", L.Tr("Saturazione")), ("gamma", "Gamma"), ("hue", L.Tr("Tonalità")) }) col.Add(Slider(p, n, -100, 100, 1, 0, v => ((int)v).ToString()));
        var aspects = new[] { ("-1", L.Tr("Originali")), ("16:9", "16:9"), ("4:3", "4:3"), ("1.85:1", "1.85:1"), ("2.35:1", "2.35:1"), ("2.39:1", "2.39:1"), ("1:1", "1:1") };
        var ap = new Pick(ix => c.Set("video-aspect-override", aspects[ix].Item1));
        updaters.Add(s =>
        {
            var v = s.P("video-aspect-override"); int sel = 0;
            if (v.D() is double x) foreach (var (t, r) in new[] { ("16:9", 16.0 / 9), ("4:3", 4.0 / 3), ("1.85:1", 1.85), ("2.35:1", 2.35), ("2.39:1", 2.39), ("1:1", 1.0) }) if (Math.Abs(r - x) < .01) sel = Array.FindIndex(aspects, a => a.Item1 == t);
            ap.Fill(aspects.Select((a, ix) => (ix, a.Item2)), sel);
        });
        var geo = new List<UIElement> { Ui.Row(L.Tr("Proporzioni"), ap) };
        foreach (var (p, n, lo, hi, st) in new[] { ("video-zoom", L.Tr("Zoom"), -1.0, 2.0, .01), ("video-pan-x", L.Tr("Sposta ←→"), -1.0, 1.0, .01), ("video-pan-y", L.Tr("Sposta ↑↓"), -1.0, 1.0, .01), ("panscan", L.Tr("Riempi (taglia)"), 0.0, 1.0, .05) }) geo.Add(Slider(p, n, lo, hi, st, 0));
        var deint = Check(L.Tr("Deinterlaccia"), s => s.B("deinterlace"), v => c.Set("deinterlace", v));
        var drow = new Grid(); drow.ColumnDefinitions.Add(new ColumnDefinition()); drow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); var rb = Ui.Btn(L.Tr("Ripristina"), () => c.Cmd("resetvideo")); SetColumn(rb, 1); drow.Children.Add(deint); drow.Children.Add(rb); geo.Add(drow);
        return new UIElement[] { Ui.Card(L.Tr("Immagine"), col.ToArray()), Ui.Card(L.Tr("Inquadratura"), geo.ToArray()) };
    }

    // MARK: Altro
    UIElement[] MoreTab()
    {
        var chap = new Pick(i => c.Cmd("chapter", ("v", i))); updaters.Add(s => { chap.Fill(s.Chapters.Select((x, i) => (i, $"{x.T} ({U.Fmt(x.S)})")), s.Chapter ?? 0); chap.IsEnabled = s.Chapters.Count > 0; });
        var ab = Ui.Btn(L.Tr("Loop A-B"), () => c.Cmd("abloop")); updaters.Add(s => ab.Content = s.Ab[0] != null ? L.Tr("Loop A-B attivo") : L.Tr("Loop A-B"));
        var fb = Ui.IconBtn("", L.Tr("Un fotogramma indietro"), () => c.Cmd("frameback"), 13, Theme.Text); var ff = Ui.IconBtn("", L.Tr("Un fotogramma avanti"), () => c.Cmd("frame"), 13, Theme.Text);
        var msg = new TextBox { MinHeight = 28, ToolTip = L.Tr("Es. Si prega di spegnere i cellulari") }; var show = Ui.Btn(L.Tr("Mostra 8″"), () => c.Cmd("text", ("v", msg.Text), ("ms", 8000)));
        var mrow = new Grid(); mrow.ColumnDefinitions.Add(new ColumnDefinition()); mrow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); show.Margin = new Thickness(8, 0, 0, 0); SetColumn(show, 1); mrow.Children.Add(msg); mrow.Children.Add(show);
        var prev = Check(L.Tr("Anteprima del proiettore attiva"), s => Settings.GetBool("preview") ?? true, v => { Settings.Set("preview", v); c.E?.SetPreview(v); });
        var info = Ui.Text("—", 12, null, Theme.Muted, true); info.FontFamily = Ui.Mono;
        updaters.Add(s => info.Text = s.Playing ? L.Trf("%@ · %@\n%@\naudio %@\nfotogrammi persi: %d", s.InfoRes ?? "", s.InfoFps is double f ? f.ToString("0.000") + " fps" : "", s.InfoVcodec ?? "", s.InfoAcodec ?? "", s.InfoDropped) : "—");
        var rc = new RemoteCard(c);
        return new UIElement[]
        {
            Ui.Card(L.Tr("Capitoli e fotogramma"), Ui.Row(L.Tr("Capitolo"), chap), Ui.H(8, ab, fb, ff)),
            rc, Ui.Card(L.Tr("Messaggio sullo schermo della sala"), mrow), Ui.Card(L.Tr("Anteprima e informazioni"), prev, info)
        };
    }
}

/// Scheda "Telecomando dal telefono" (scheda Altro).
public sealed class RemoteCard : Border
{
    readonly CheckBox sw; readonly TextBlock problem = Ui.Text("", 12, null, Brushes.Red, true), url = Ui.Text("", 13, null, null, true), pin = Ui.Text("", 20, FontWeights.SemiBold), others = Ui.Muted(""), nonet = Ui.Muted("");
    readonly Image qr = new() { Width = 112, Height = 112, Stretch = Stretch.Fill }; readonly Border qrBox; readonly StackPanel detail = new(); string qrFor = "";
    public RemoteCard(Ctx c)
    {
        var r = RemoteControl.Shared;
        sw = Ui.Check(L.Tr("Attiva il telecomando"), on => { r.SetEnabled(on); Refresh(); });
        url.FontFamily = Ui.Mono; pin.FontFamily = Ui.Mono; qrBox = new Border { Child = qr, Background = Brushes.White, CornerRadius = new CornerRadius(6), Padding = new Thickness(4), VerticalAlignment = VerticalAlignment.Top };
        var np = Ui.Btn(L.Tr("Nuovo PIN"), () => { r.RegeneratePin(); Refresh(); });
        var right = Ui.V(4, Ui.Text(L.Tr("Indirizzo"), 11, null, Theme.Muted), url, Ui.Text(L.Tr("PIN"), 11, null, Theme.Muted), Ui.H(10, pin, np));
        var row = Ui.H(12, qrBox, right); detail.Children.Add(row); others.Margin = new Thickness(0, 6, 0, 0); detail.Children.Add(others);
        var note = Ui.Muted(L.Tr("Il telefono e il PC devono stare sulla stessa rete; non serve internet. Inquadra il QR o scrivi l'indirizzo nel browser del telefono.")); var fw = Ui.Muted(L.Tr("Windows potrebbe chiedere di consentire la rete: scegli \"Reti private\"."));
        var sp = new StackPanel(); sp.Children.Add(new TextBlock { Text = L.Tr("Telecomando dal telefono").ToUpperInvariant(), FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = Theme.Muted, Margin = new Thickness(0, 0, 0, 10) });
        foreach (var e in new UIElement[] { sw, problem, detail, nonet, note, fw }) { if (e is FrameworkElement fe && sp.Children.Count > 1) fe.Margin = new Thickness(fe.Margin.Left, fe.Margin.Top + 8, fe.Margin.Right, fe.Margin.Bottom); sp.Children.Add(e); }
        Child = sp; Background = Theme.Card; BorderBrush = Theme.Line; BorderThickness = new Thickness(1); CornerRadius = new CornerRadius(10); Padding = new Thickness(12);
        r.Changed += () => Dispatcher.BeginInvoke(Refresh); Refresh();
    }
    void Refresh()
    {
        var r = RemoteControl.Shared; sw.IsChecked = r.Enabled;
        problem.Text = r.Problem ?? ""; problem.Visibility = r.Problem != null ? Visibility.Visible : Visibility.Collapsed;
        bool has = r.Enabled && r.Urls.Count > 0; detail.Visibility = has ? Visibility.Visible : Visibility.Collapsed;
        nonet.Text = L.Tr("Nessuna rete trovata: collega il PC al Wi-Fi o a un router."); nonet.Visibility = r.Enabled && r.Urls.Count == 0 && r.Problem == null ? Visibility.Visible : Visibility.Collapsed;
        if (has)
        {
            url.Text = r.Urls[0]; pin.Text = r.Pin; others.Text = L.Tr("Altri indirizzi: ") + string.Join("  ", r.Urls.Skip(1)); others.Visibility = r.Urls.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
            if (qrFor != r.Urls[0]) { qrFor = r.Urls[0]; qr.Source = Qr(r.Urls[0]); }
        }
    }
    public static BitmapImage? Qr(string text)
    {
        try
        {
            using var gen = new QRCoder.QRCodeGenerator(); using var data = gen.CreateQrCode(text, QRCoder.QRCodeGenerator.ECCLevel.M);
            var png = new QRCoder.PngByteQRCode(data).GetGraphic(8);
            var b = new BitmapImage(); using var ms = new MemoryStream(png); b.BeginInit(); b.CacheOption = BitmapCacheOption.OnLoad; b.StreamSource = ms; b.EndInit(); b.Freeze(); return b;
        }
        catch { return null; }
    }
}
