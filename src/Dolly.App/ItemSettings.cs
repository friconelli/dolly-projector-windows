using System.Windows;
using System.Windows.Controls;
using Dolly;

namespace Dolly.App;

/// Impostazioni di un singolo elemento della scaletta (riquadro a comparsa, niente accordion).
public sealed class ItemSettingsView : StackPanel
{
    readonly Ctx c; readonly int i; List<Action<ItemState, Snap>> refreshers = new();
    public ItemSettingsView(Ctx c, int i) { this.c = c; this.i = i; }

    /// Cambia quando cambia la "forma" del pannello (quali campi compaiono): allora si ricostruisce.
    public string Shape(ItemState it) => $"{it.Kind}/{it.Split > 0}/{it.Loop > 0}/{it.Bg.Length > 0}";

    Ui.Field Opt(string label, string ph, Func<ItemState, string> get, Action<string> commit, double w = 76)
    {
        var f = new Ui.Field(label, ph, commit, w); refreshers.Add((it, s) => f.Set(get(it))); return f;
    }
    Ui.Field Txt(string label, string ph, Func<ItemState, string> get, Action<string> commit)
    {
        var f = new Ui.Field(label, ph, commit, 0, 112, true); refreshers.Add((it, s) => f.Set(get(it))); return f;
    }
    static Border Sep() => new() { Height = 1, Background = Theme.Line, Margin = new Thickness(0, 8, 0, 8) };

    public void Build(ItemState it, Snap s)
    {
        Children.Clear(); refreshers = new();
        Children.Add(Ui.Text(it.Label, 15, FontWeights.SemiBold));
        void Add(UIElement e) { if (e is FrameworkElement fe) fe.Margin = new Thickness(fe.Margin.Left, fe.Margin.Top + 9, fe.Margin.Right, fe.Margin.Bottom); Children.Add(e); }
        if (c.Selection.Contains(i) && c.Selection.Count > 1) Add(Ui.Text(L.Trf("Le modifiche valgono per i %d elementi selezionati", c.Selection.Count), 11.5, null, Theme.Accent, true));
        if (it.Kind == "pausa" || it.Kind == "nero")
        {
            if (it.Kind == "pausa") Add(Txt(L.Tr("Testo"), L.Tr("Intervallo"), x => x.Text, v => c.SetItem(i, "text", v)));
            Add(Opt(L.Tr("Durata (s)"), "", x => Ui.Num(x.Secs), v => { if (Ui.ParseNum(v) is double d) c.SetItem(i, "secs", d); }));
            if (it.Kind == "pausa") Add(Bg(it, s));
        }
        else
        {
            Add(Opt(L.Tr("Nero prima (s)"), Ui.Num(s.DefPre), x => Ui.Num(x.Pre), v => c.SetItem(i, "pre", v)));
            Add(Opt(L.Tr("Nero dopo (s)"), Ui.Num(s.DefPost), x => Ui.Num(x.Post), v => c.SetItem(i, "post", v)));
            Add(Opt("Volume %", L.Tr("invariato"), x => Ui.Num(x.Vol), v => c.SetItem(i, "vol", v)));
            Add(Sep());
            Add(Txt(L.Tr("Audio (lingua)"), "ita,it", x => x.Alang, v => c.SetItem(i, "alang", v)));
            Add(Txt(L.Tr("Sott. (lingua)"), L.Tr("es. ita"), x => x.Slang, v => c.SetItem(i, "slang", v)));
            var modes = new[] { ("", L.Tr("Come la scena")), ("file", L.Tr("Come nel file")), ("none", L.Tr("Nessuno")), ("forced", L.Tr("Solo forzati")), ("full", L.Tr("Completi")) };
            var pick = new Pick(ix => c.SetItem(i, "submode", modes[ix].Item1));
            refreshers.Add((x, _) => pick.Fill(modes.Select((m, ix) => (ix, m.Item2)), Math.Max(0, Array.FindIndex(modes, m => m.Item1 == (x.Submode ?? "")))));
            pick.Fill(modes.Select((m, ix) => (ix, m.Item2)), Math.Max(0, Array.FindIndex(modes, m => m.Item1 == (it.Submode ?? ""))));
            Add(Ui.Row(L.Tr("Sottotitoli"), pick));
            Add(Ui.Muted(L.Tr("Lingue: codici a 3 lettere separati da virgola (ita, eng, fre…). Se il film è in corso, la modifica vale subito.")));
            Add(Sep());
            var reps = new[] { L.Tr("Nessuna"), L.Tr("Infinita"), L.Tr("Un numero di volte") };
            var rp = new Pick(ix => c.SetItem(i, "loop", ix == 0 ? 0 : ix == 1 ? -1 : 1));
            int RepIx(ItemState x) => x.Loop < 0 ? 1 : x.Loop == 0 ? 0 : 2;
            refreshers.Add((x, _) => rp.Fill(reps.Select((r, ix) => (ix, r)), RepIx(x))); rp.Fill(reps.Select((r, ix) => (ix, r)), RepIx(it));
            Add(Ui.Row(L.Tr("Ripetizione"), rp));
            if (it.Loop > 0) Add(Opt(L.Tr("Volte in più"), "1", x => x.Loop.ToString(), v => c.SetItem(i, "loop", Ui.ParseNum(v) ?? 1)));
            Add(Sep());
            Add(Opt(L.Tr("Intervallo a (min)"), L.Tr("nessuno"), x => x.Split > 0 ? Ui.Num(Math.Round(x.Split / 6) / 10) : "", v => c.SetItem(i, "split", (Ui.ParseNum(v) ?? 0) * 60)));
            if (it.Split > 0)
            {
                Add(Opt(L.Tr("Durata (min)"), "10", x => Ui.Num(x.Secs / 60), v => c.SetItem(i, "secs", Math.Max(1, Ui.ParseNum(v) ?? 10) * 60)));
                Add(Txt(L.Tr("Testo"), L.Tr("Intervallo"), x => x.Text, v => c.SetItem(i, "text", v)));
                Add(Bg(it, s));
            }
            Add(Ui.Muted(L.Tr("Il film si ferma al minuto indicato, la sala mostra testo e conto alla rovescia, poi riparte da lì.")));
            Add(Ui.Muted(L.Tr("Un film in ripetizione continua finché non togli “Ripeti” dai controlli; poi la scaletta prosegue.")));
        }
    }
    public void Refresh(ItemState it, Snap s) { foreach (var r in refreshers) r(it, s); }

    /// Sfondo (immagine o video in loop) e preset di un intervallo.
    UIElement Bg(ItemState it, Snap s)
    {
        var sp = new StackPanel(); void Add(UIElement e) { if (e is FrameworkElement fe && sp.Children.Count > 0) fe.Margin = new Thickness(fe.Margin.Left, fe.Margin.Top + 8, fe.Margin.Right, fe.Margin.Bottom); sp.Children.Add(e); }
        var name = Ui.Text(it.Bg.Length == 0 ? L.Tr("nero") : System.IO.Path.GetFileName(it.Bg), 12.5, null, Theme.Muted);
        var choose = Ui.Btn(L.Tr("Scegli…"), () => { var p = c.PickFiles(L.Tr("Immagine o video per lo sfondo dell'intervallo"), "Immagini e video|*.jpg;*.jpeg;*.png;*.gif;*.bmp;*.mp4;*.mov;*.mkv;*.m4v", false); if (p.Length > 0) c.SetItem(i, "bg", p[0]); });
        var row = new StackPanel { Orientation = Orientation.Horizontal }; var lab = Ui.Text(L.Tr("Sfondo"), 12.5, null, Theme.Muted); lab.Width = 112; row.Children.Add(lab); row.Children.Add(name); choose.Margin = new Thickness(10, 0, 0, 0); row.Children.Add(choose);
        if (it.Bg.Length > 0) { var rm = Ui.Btn(L.Tr("Togli"), () => c.SetItem(i, "bg", "")); rm.Margin = new Thickness(6, 0, 0, 0); row.Children.Add(rm); }
        Add(row);
        if (it.Bg.Length > 0) { var ck = Ui.Check(L.Tr("Audio dello sfondo"), v => c.SetItem(i, "bgaudio", v)); ck.IsChecked = it.BgAudio; Add(ck); }
        var apply = new Button { Content = L.Tr("Applica…"), Padding = new Thickness(10, 4, 10, 4) };
        apply.Click += (_, _) =>
        {
            var m = new ContextMenu { PlacementTarget = apply, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
            if (c.S.Presets.Count == 0) m.Items.Add(new MenuItem { Header = L.Tr("Nessun preset salvato"), IsEnabled = false });
            foreach (var p in c.S.Presets) { var pn = p.Name; var mi = new MenuItem { Header = pn }; mi.Click += (_, _) => c.Cmd("preset_apply", ("i", i), ("name", pn)); m.Items.Add(mi); }
            if (c.S.Presets.Count > 0) { m.Items.Add(new Separator()); var d = new MenuItem { Header = L.Tr("Elimina") }; foreach (var p in c.S.Presets) { var pn = p.Name; var mi = new MenuItem { Header = pn }; mi.Click += (_, _) => c.Cmd("preset_delete", ("name", pn)); d.Items.Add(mi); } m.Items.Add(d); }
            m.IsOpen = true;
        };
        var prow = new StackPanel { Orientation = Orientation.Horizontal }; var pl = Ui.Text(L.Tr("Preset"), 12.5, null, Theme.Muted); pl.Width = 112; prow.Children.Add(pl); prow.Children.Add(apply); Add(prow);
        var pn_ = new TextBox { MinHeight = 28, Width = 170 }; pn_.ToolTip = L.Tr("Nome del preset");
        var save = Ui.Btn(L.Tr("Salva"), () => { if (pn_.Text.Trim().Length > 0) { c.Cmd("preset_save", ("i", i), ("name", pn_.Text)); pn_.Text = ""; } });
        var srow = new StackPanel { Orientation = Orientation.Horizontal }; srow.Children.Add(pn_); save.Margin = new Thickness(8, 0, 0, 0); srow.Children.Add(save); Add(srow);
        Add(Ui.Muted(L.Tr("Un preset ricorda sfondo, testo e durata e vale per tutte le scalette.")));
        return sp;
    }
}
