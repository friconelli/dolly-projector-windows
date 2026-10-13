using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Dolly;

namespace Dolly.App;

/// Una riga della scaletta (si aggiorna sul posto, così il pannello delle impostazioni aperto non si perde).
public sealed class ItemRow : Border
{
    public readonly int I; readonly Ctx c; public readonly Button Gear; public readonly Popup Pop; ItemSettingsView? view; string shape = "";
    readonly Border grip, playBox; readonly TextBlock playGlyph = Ui.Glyph("", 12), title = Ui.Text("", 13, FontWeights.Medium), cap = Ui.Muted("", 11), loopIcon = Ui.Glyph("", 11);
    public ItemRow(Ctx c, int i, Action<int, MouseEventArgs, bool> gripMove, Action<int> gripEnd, Action<int> openGear)
    {
        this.c = c; I = i; CornerRadius = new CornerRadius(9); Padding = new Thickness(6, 4, 6, 4); Margin = new Thickness(0, 1, 0, 1); BorderThickness = new Thickness(1.2); BorderBrush = Brushes.Transparent; Background = Brushes.Transparent;
        var g = new Grid(); foreach (var w in new[] { 18.0, 40, -1, 20, 32 }) g.ColumnDefinitions.Add(new ColumnDefinition { Width = w < 0 ? new GridLength(1, GridUnitType.Star) : new GridLength(w) });
        grip = new Border { Background = Brushes.Transparent, Cursor = Cursors.SizeNS, ToolTip = L.Tr("Trascina per cambiare l'ordine"), Child = Ui.Glyph("", 11, Theme.Faint) };
        grip.MouseLeftButtonDown += (_, e) => { grip.CaptureMouse(); gripMove(i, e, true); e.Handled = true; };
        grip.MouseMove += (_, e) => { if (grip.IsMouseCaptured) gripMove(i, e, false); };
        grip.MouseLeftButtonUp += (_, e) => { if (grip.IsMouseCaptured) { grip.ReleaseMouseCapture(); gripEnd(i); } };
        Grid.SetColumn(grip, 0); g.Children.Add(grip);
        playBox = new Border { Width = 30, Height = 30, CornerRadius = new CornerRadius(7), Background = Theme.Hover, Cursor = Cursors.Hand, ToolTip = L.Tr("Avvia"), Child = playGlyph };
        playBox.MouseLeftButtonUp += (_, _) => Play();
        Grid.SetColumn(playBox, 1); g.Children.Add(playBox);
        var tx = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) }; tx.Children.Add(title); tx.Children.Add(cap);
        Grid.SetColumn(tx, 2); g.Children.Add(tx);
        Grid.SetColumn(loopIcon, 3); g.Children.Add(loopIcon);
        Gear = Ui.IconBtn("", L.Tr("Impostazioni di questo elemento"), () => openGear(i), 13);
        Pop = new Popup { PlacementTarget = Gear, Placement = PlacementMode.Right, StaysOpen = false, AllowsTransparency = true, PopupAnimation = PopupAnimation.Fade };
        Grid.SetColumn(Gear, 4); g.Children.Add(Gear); g.Children.Add(Pop);
        Child = g;
        MouseLeftButtonDown += (_, e) => { if (e.ClickCount == 2 && !grip.IsMouseOver && !Gear.IsMouseOver) { Play(); e.Handled = true; } };
        MouseEnter += (_, _) => { if (Background == Brushes.Transparent) Background = Theme.Hover; }; MouseLeave += (_, _) => { if (Equals(Background, Theme.Hover)) Background = Brushes.Transparent; };
        var cm = new ContextMenu();
        cm.Items.Add(Mi(L.Tr("Avvia"), Play)); cm.Items.Add(Mi(L.Tr("Impostazioni…"), () => openGear(i))); cm.Items.Add(new Separator());
        cm.Items.Add(Mi(L.Tr("Sposta su"), () => c.Cmd("move", ("i", i), ("d", -1)))); cm.Items.Add(Mi(L.Tr("Sposta giù"), () => c.Cmd("move", ("i", i), ("d", 1)))); cm.Items.Add(new Separator());
        cm.Items.Add(Mi(L.Tr("Rimuovi dalla scaletta"), () => { if (c.Confirm(L.Tr("Togliere dalla scaletta?"))) c.Cmd("remove", ("i", i)); }));
        ContextMenu = cm;
    }
    static MenuItem Mi(string h, Action a) { var m = new MenuItem { Header = h }; m.Click += (_, _) => a(); return m; }
    void Play() { if (c.S.Mode == "idle") c.Cmd("play", ("i", I)); else if (c.Confirm(L.Tr("Cambiare? Quello in corso si interrompe."))) c.Cmd("play", ("i", I)); }

    public void SetDropMark(bool top, bool bottom)
    {
        BorderThickness = new Thickness(1.2, top ? 3 : 1.2, 1.2, bottom ? 3 : 1.2);
    }

    public void Update(Snap s, ItemState it)
    {
        bool cur = (s.Mode == "playing" || s.Mode == "wait") && s.Idx == I, nxt = (s.Mode == "gap" && s.Next == I) || (s.Mode == "idle" && s.Sel == I);
        title.Text = it.Label; title.Foreground = it.Ok ? Theme.Text : Brushes.Red; title.TextDecorations = it.Ok ? null : TextDecorations.Strikethrough;
        var cp = Caption(s, it); cap.Text = cp; cap.Visibility = cp.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        playBox.Background = cur ? Theme.Cue : Theme.Hover;
        playGlyph.Text = cur ? "" : it.Kind == "pausa" ? "" : it.Kind == "nero" ? "" : ""; playGlyph.Foreground = cur ? Brushes.White : Theme.Muted;
        Background = cur ? Theme.CueSoft : Brushes.Transparent; BorderBrush = nxt ? Theme.Orange : Brushes.Transparent;
        loopIcon.Visibility = it.Loop != 0 ? Visibility.Visible : Visibility.Collapsed; loopIcon.ToolTip = it.Loop < 0 ? L.Tr("Si ripete all'infinito") : L.Trf("Si ripete %d volte", it.Loop);
        if (Pop.IsOpen) RefreshPopup(s, it);
    }
    public void OpenPopup(Snap s, ItemState it)
    {
        view = new ItemSettingsView(c, I); shape = view.Shape(it); view.Build(it, s);
        Pop.Child = new Border { Child = view, Background = Theme.Card, BorderBrush = Theme.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Width = 340, Padding = new Thickness(14), Margin = new Thickness(6), Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 14, Opacity = .3, ShadowDepth = 2 } };
        Pop.IsOpen = true;
    }
    void RefreshPopup(Snap s, ItemState it)
    {
        if (view == null) return;
        var sh = view.Shape(it);
        if (sh != shape) { if (view.IsKeyboardFocusWithin) return; shape = sh; view.Build(it, s); }
        else view.Refresh(it, s);
    }
    public static string Caption(Snap s, ItemState it)
    {
        var p = new List<string>();
        if (it.Kind == "pausa" || it.Kind == "nero") p.Add(U.Fmt(it.Secs));
        if (it.Pre is double a) p.Add(L.Trf("nero prima %d″", (int)a));
        if (it.Post is double b) p.Add(L.Trf("nero dopo %d″", (int)b));
        if (it.Alang.Length > 0 || it.Slang.Length > 0) p.Add((it.Alang.Length == 0 ? "—" : it.Alang) + " → " + (it.Slang.Length == 0 ? "—" : it.Slang));
        if (it.Submode is string m) p.Add(m switch { "file" => L.Tr("sott. del file"), "none" => L.Tr("senza sott."), "forced" => L.Tr("sott. forzati"), "full" => L.Tr("sott. completi"), _ => "" });
        if (it.Vol is double v) p.Add(L.Trf("vol %d%%", (int)v));
        return string.Join(" · ", p);
    }
}

public sealed class ScalettaPanel : Grid
{
    readonly Ctx c; readonly TextBox nameBox = new() { FontSize = 14, FontWeight = FontWeights.SemiBold, BorderThickness = new Thickness(0), Background = Brushes.Transparent, VerticalContentAlignment = VerticalAlignment.Center };
    readonly StackPanel list = new(); readonly ScrollViewer scroll; readonly TextBlock count = Ui.Muted(""); readonly StackPanel empty; readonly Border dropLine = new() { Height = 3, Background = Theme.Accent, CornerRadius = new CornerRadius(2), Margin = new Thickness(6, 0, 6, 0), Visibility = Visibility.Collapsed };
    List<ItemRow> rows = new(); string structSig = ""; int dragFrom = -1, dragTo = -1; Snap s = new();
    Popup? addPop;

    public ScalettaPanel(Ctx c)
    {
        this.c = c; Background = Theme.Panel;
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); RowDefinitions.Add(new RowDefinition()); RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        // intestazione: nome + menu
        var top = new Grid { Margin = new Thickness(14, 8, 8, 8) }; top.ColumnDefinitions.Add(new ColumnDefinition()); top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        nameBox.ToolTip = L.Tr("Nome della scena");
        nameBox.KeyDown += (_, e) => { if (e.Key == Key.Enter) { c.Cmd("rename", ("v", nameBox.Text)); Keyboard.ClearFocus(); } };
        nameBox.LostFocus += (_, _) => c.Cmd("rename", ("v", nameBox.Text));
        var more = Ui.IconBtn("", "", () => { }, 15, Theme.Text); more.Click += (_, _) => ShowMenu(more);
        Grid.SetColumn(nameBox, 0); Grid.SetColumn(more, 1); top.Children.Add(nameBox); top.Children.Add(more);
        var topB = new Border { Child = top, BorderBrush = Theme.Line, BorderThickness = new Thickness(0, 0, 0, 1) }; SetRow(topB, 0); Children.Add(topB);
        // elenco
        empty = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(20) };
        empty.Children.Add(Ui.Glyph("", 30, Theme.Faint)); empty.Children.Add(new TextBlock { Text = L.Tr("Scaletta vuota"), Foreground = Theme.Muted, Margin = new Thickness(0, 8, 0, 4), HorizontalAlignment = HorizontalAlignment.Center });
        empty.Children.Add(new TextBlock { Text = L.Tr("Aggiungi film con il pulsante + qui sotto, oppure trascinali qui da Esplora risorse."), Foreground = Theme.Faint, FontSize = 11.5, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, MaxWidth = 250 });
        var host = new Grid { Background = Brushes.Transparent, AllowDrop = true };
        list.Margin = new Thickness(8, 6, 8, 6);
        scroll = new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        host.Children.Add(scroll); host.Children.Add(empty);
        host.DragOver += OnDragOver; host.DragLeave += (_, _) => ClearMarks(); host.Drop += OnDrop;
        SetRow(host, 1); Children.Add(host);
        // piede
        var bottom = new Grid { Margin = new Thickness(12, 8, 12, 8) }; bottom.ColumnDefinitions.Add(new ColumnDefinition()); bottom.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var add = new Button { Content = Ui.H(6, Ui.Glyph("", 13, Theme.Text), Ui.Text(L.Tr("Aggiungi"), 12.5)), Padding = new Thickness(10, 4, 10, 4), HorizontalAlignment = HorizontalAlignment.Left }; add.Click += (_, _) => ShowAddMenu(add);
        Grid.SetColumn(add, 0); Grid.SetColumn(count, 1); bottom.Children.Add(add); bottom.Children.Add(count);
        var botB = new Border { Child = bottom, BorderBrush = Theme.Line, BorderThickness = new Thickness(0, 1, 0, 0) }; SetRow(botB, 2); Children.Add(botB);
    }

    // MARK: menu
    static MenuItem Mi(string h, Action a, bool enabled = true) { var m = new MenuItem { Header = h, IsEnabled = enabled }; m.Click += (_, _) => a(); return m; }
    void ShowMenu(FrameworkElement at)
    {
        var m = new ContextMenu { PlacementTarget = at, Placement = PlacementMode.Bottom };
        m.Items.Add(Mi(L.Tr("Salva la scena"), () => { c.Cmd("rename", ("v", nameBox.Text)); c.Cmd("pl_save", ("name", nameBox.Text)); }));
        var open = new MenuItem { Header = L.Tr("Apri una scena salvata"), IsEnabled = s.Saved.Count > 0 };
        foreach (var n in s.Saved) { var name = n; open.Items.Add(Mi(name, () => { if (s.Items.Count == 0 || c.Confirm(L.Trf("Sostituire la scaletta corrente con “%@”?", name))) c.Cmd("pl_load", ("name", name)); })); }
        m.Items.Add(open);
        var del = new MenuItem { Header = L.Tr("Elimina una scena salvata"), IsEnabled = s.Saved.Count > 0 };
        foreach (var n in s.Saved) { var name = n; del.Items.Add(Mi(name, () => { if (c.Confirm(L.Trf("Eliminare la scena salvata “%@”?", name))) c.Cmd("pl_delete", ("name", name)); })); }
        m.Items.Add(del); m.Items.Add(new Separator());
        m.Items.Add(Mi(L.Tr("Importa una playlist .m3u…"), () => { var p = c.PickFiles(L.Tr("Scegli una scaletta (.m3u)"), "Playlist|*.m3u;*.m3u8", false); if (p.Length > 0) c.Cmd("pl_import", ("path", p[0])); }));
        m.Items.Add(Mi(L.Tr("Svuota la scaletta"), () => { if (c.Confirm(L.Tr("Svuotare la scaletta?"))) c.Cmd("clear"); }));
        m.IsOpen = true;
    }
    void ShowAddMenu(FrameworkElement at)
    {
        var m = new ContextMenu { PlacementTarget = at, Placement = PlacementMode.Top };
        m.Items.Add(Mi(L.Tr("Film (mp4, avi, mkv)…"), () => { var p = c.PickFiles(L.Tr("Aggiungi film (mp4, avi, mkv)"), "Film|*.mp4;*.avi;*.mkv"); if (p.Length > 0) c.Cmd("add", ("paths", U.Arr(p))); }));
        m.Items.Add(Mi(L.Tr("Tutti i film di una cartella…"), () => { var p = c.PickFolder(L.Tr("Aggiungi tutti i film di una cartella")); if (p != null) c.Cmd("add", ("paths", U.Arr(new object[] { p }))); }));
        m.Items.Add(Mi(L.Tr("Tutta la libreria"), () => c.Cmd("addlib"))); m.Items.Add(new Separator());
        m.Items.Add(Mi(L.Tr("Nero (secondi)…"), () => PopForm(at, L.Tr("Aggiungi un nero"), new[] { ("5", "secondi") }, L.Tr("Schermo nero tra due elementi, senza scritte."), v => c.Cmd("addblack", ("secs", Ui.ParseNum(v[0]) ?? 5)))));
        m.Items.Add(Mi(L.Tr("Intervallo con conto alla rovescia…"), () => PopForm(at, L.Tr("Aggiungi un intervallo"), new[] { ("10", "minuti"), (L.Tr("Intervallo"), "") }, L.Tr("Sullo schermo della sala compaiono il testo e il tempo che manca."), v => c.Cmd("addpause", ("secs", (Ui.ParseNum(v[0]) ?? 10) * 60), ("text", v[1])))));
        m.IsOpen = true;
    }
    /// Piccolo modulo a comparsa (nero / intervallo).
    void PopForm(FrameworkElement at, string title, (string def, string unit)[] fields, string hint, Action<string[]> ok)
    {
        var boxes = fields.Select(f => new TextBox { Text = f.def, MinHeight = 28, Width = f.unit.Length > 0 ? 60 : double.NaN }).ToArray();
        var sp = new StackPanel { Width = 260 }; sp.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, FontSize = 14, Foreground = Theme.Text, Margin = new Thickness(0, 0, 0, 8) });
        for (int i = 0; i < boxes.Length; i++)
        {
            if (fields[i].unit.Length > 0) { var h = Ui.H(8, boxes[i], Ui.Text(fields[i].unit, 12.5, null, Theme.Muted)); h.Margin = new Thickness(0, 0, 0, 8); sp.Children.Add(h); }
            else { boxes[i].Margin = new Thickness(0, 0, 0, 8); sp.Children.Add(boxes[i]); }
        }
        sp.Children.Add(Ui.Muted(hint));
        var pop = new Popup { PlacementTarget = at, Placement = PlacementMode.Top, StaysOpen = false, AllowsTransparency = true, IsOpen = false };
        var okb = Ui.Btn(L.Tr("Aggiungi"), () => { pop.IsOpen = false; ok(boxes.Select(b => b.Text).ToArray()); }, null, true); okb.HorizontalAlignment = HorizontalAlignment.Right; okb.Margin = new Thickness(0, 10, 0, 0); okb.IsDefault = true; sp.Children.Add(okb);
        pop.Child = new Border { Child = sp, Background = Theme.Card, BorderBrush = Theme.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Padding = new Thickness(14), Margin = new Thickness(6) };
        pop.IsOpen = true; boxes[0].Focus(); boxes[0].SelectAll();
    }

    // MARK: aggiornamento
    public void Update(Snap sn)
    {
        s = sn; if (!nameBox.IsKeyboardFocusWithin && nameBox.Text != s.Name) nameBox.Text = s.Name;
        count.Text = L.Trf("%d elementi", s.Items.Count);
        empty.Visibility = s.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        var sig = string.Join("\u0001", s.Items.Select(i => i.Kind + "|" + i.Name));
        if (sig != structSig && dragFrom < 0)
        {
            structSig = sig; list.Children.Clear(); rows = new();
            for (int i = 0; i < s.Items.Count; i++) { var r = new ItemRow(c, i, GripMove, GripEnd, OpenGear); rows.Add(r); list.Children.Add(r); }
            list.Children.Add(new Border { Height = 36 }); list.Children.Add(dropLine);
        }
        for (int i = 0; i < rows.Count && i < s.Items.Count; i++) rows[i].Update(s, s.Items[i]);
    }
    void OpenGear(int i) { if (i < rows.Count && i < s.Items.Count) { foreach (var r in rows) if (r != rows[i]) r.Pop.IsOpen = false; if (rows[i].Pop.IsOpen) rows[i].Pop.IsOpen = false; else rows[i].OpenPopup(s, s.Items[i]); } }

    // MARK: riordino con la maniglia
    int RowAt(Point pInList)
    {
        int best = 0; double bd = double.MaxValue;
        for (int i = 0; i < rows.Count; i++) { var top = rows[i].TranslatePoint(new Point(0, 0), list).Y; var mid = top + rows[i].ActualHeight / 2; var d = Math.Abs(mid - pInList.Y); if (d < bd) { bd = d; best = i; } }
        return best;
    }
    void GripMove(int i, MouseEventArgs e, bool start)
    {
        if (start) dragFrom = i;
        dragTo = RowAt(e.GetPosition(list)); ClearMarks();
        if (dragTo != dragFrom && dragTo >= 0 && dragTo < rows.Count) rows[dragTo].SetDropMark(dragTo < dragFrom, dragTo > dragFrom);
        if (dragFrom >= 0 && dragFrom < rows.Count) rows[dragFrom].Opacity = .55;
    }
    void GripEnd(int i)
    {
        int f = dragFrom, t = dragTo; dragFrom = dragTo = -1; ClearMarks();
        if (f >= 0 && t >= 0 && f != t) c.Cmd("reorder", ("from", f), ("to", t));
    }
    void ClearMarks() { foreach (var r in rows) { r.SetDropMark(false, false); if (dragFrom < 0) r.Opacity = 1; } dropLine.Visibility = Visibility.Collapsed; }

    // MARK: file trascinati da Esplora risorse (inseriti nel punto in cui si rilasciano)
    int InsertionAt(DragEventArgs e)
    {
        var y = e.GetPosition(list).Y;
        for (int i = 0; i < rows.Count; i++) { var top = rows[i].TranslatePoint(new Point(0, 0), list).Y; if (y < top + rows[i].ActualHeight / 2) return i; }
        return rows.Count;
    }
    void OnDragOver(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) { e.Effects = DragDropEffects.None; return; }
        e.Effects = DragDropEffects.Copy; ClearMarks(); int at = InsertionAt(e);
        if (at < rows.Count) rows[at].SetDropMark(true, false); else dropLine.Visibility = Visibility.Visible;
        e.Handled = true;
    }
    void OnDrop(object sender, DragEventArgs e)
    {
        int at = InsertionAt(e); ClearMarks();
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files) { var sorted = files.OrderBy(x => x, StringComparer.Ordinal).ToArray(); c.Cmd("add", ("paths", U.Arr(sorted)), ("at", at)); }
        e.Handled = true;
    }
}
