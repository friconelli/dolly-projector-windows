using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Dolly;

namespace Dolly.App;

/// Piccole fabbriche di controlli (l'interfaccia è costruita in codice: niente XAML da tenere allineato).
public static class Ui
{
    public static readonly FontFamily Icons = new("Segoe Fluent Icons, Segoe MDL2 Assets");
    public static readonly FontFamily Mono = new("Cascadia Mono, Consolas");

    public static TextBlock Text(string s, double size = 13, FontWeight? w = null, Brush? fg = null, bool wrap = false)
        => new() { Text = s, FontSize = size, FontWeight = w ?? FontWeights.Normal, Foreground = fg ?? Theme.Text, TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap, TextTrimming = wrap ? TextTrimming.None : TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
    public static TextBlock Muted(string s, double size = 11.5) => Text(s, size, null, Theme.Muted, true);
    public static TextBlock Glyph(string g, double size = 14, Brush? fg = null) => new() { Text = g, FontFamily = Icons, FontSize = size, Foreground = fg ?? Theme.Muted, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };

    public static StackPanel V(double gap = 8, params UIElement[] c) { var p = new StackPanel { Orientation = Orientation.Vertical }; for (int i = 0; i < c.Length; i++) { if (i > 0 && c[i] is FrameworkElement fe) fe.Margin = new Thickness(fe.Margin.Left, fe.Margin.Top + gap, fe.Margin.Right, fe.Margin.Bottom); p.Children.Add(c[i]); } return p; }
    public static StackPanel H(double gap = 8, params UIElement[] c) { var p = new StackPanel { Orientation = Orientation.Horizontal }; for (int i = 0; i < c.Length; i++) { if (i > 0 && c[i] is FrameworkElement fe) fe.Margin = new Thickness(fe.Margin.Left + gap, fe.Margin.Top, fe.Margin.Right, fe.Margin.Bottom); p.Children.Add(c[i]); } return p; }

    public static Button Btn(string text, Action a, string? tip = null, bool primary = false)
    {
        var b = new Button { Content = text, Padding = new Thickness(12, 5, 12, 5), ToolTip = tip, MinHeight = 28 };
        if (primary) { b.Background = Theme.Accent; b.Foreground = Brushes.White; }
        b.Click += (_, _) => a(); return b;
    }
    /// Bottone con icona (glifo Segoe) senza cornice.
    public static Button IconBtn(string glyph, string tip, Action a, double size = 14, Brush? fg = null)
    {
        var b = new Button { Content = Glyph(glyph, size, fg), ToolTip = tip, Width = 30, Height = 28, Padding = new Thickness(0), Background = Brushes.Transparent, BorderThickness = new Thickness(0), Foreground = fg ?? Theme.Muted };
        b.Click += (_, _) => a(); return b;
    }
    public static Border Card(string? title, params UIElement[] children)
    {
        var sp = new StackPanel();
        if (title != null) sp.Children.Add(new TextBlock { Text = title.ToUpperInvariant(), FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = Theme.Muted, Margin = new Thickness(0, 0, 0, 10) });
        for (int i = 0; i < children.Length; i++) { if (children[i] is FrameworkElement fe && (i > 0 || title != null)) fe.Margin = new Thickness(fe.Margin.Left, fe.Margin.Top + (i > 0 ? 9 : 0), fe.Margin.Right, fe.Margin.Bottom); sp.Children.Add(children[i]); }
        return new Border { Child = sp, Background = Theme.Card, BorderBrush = Theme.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Padding = new Thickness(12) };
    }
    /// Riga "etichetta | controllo" con l'etichetta di larghezza fissa.
    public static Grid Row(string label, UIElement ctl, double labelW = 112)
    {
        var g = new Grid(); g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(labelW) }); g.ColumnDefinitions.Add(new ColumnDefinition());
        var l = Text(label, 12.5, null, Theme.Muted); Grid.SetColumn(l, 0); g.Children.Add(l);
        if (ctl is FrameworkElement fe) fe.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(ctl, 1); g.Children.Add(ctl); return g;
    }
    public static Border Pill(string text, Brush fg, Brush bg, double size = 12)
        => new() { Background = bg, CornerRadius = new CornerRadius(20), Padding = new Thickness(11, 4, 11, 4), Child = Text(text, size, FontWeights.SemiBold, fg) };

    /// Campo numerico/testuale: si conferma con Invio o uscendo dal campo; vuoto = valore generale.
    public sealed class Field : Grid
    {
        public readonly TextBox Box = new() { MinHeight = 28, VerticalContentAlignment = VerticalAlignment.Center };
        string last = "";
        public Field(string label, string placeholder, Action<string> commit, double width = 90, double labelW = 112, bool wide = false)
        {
            ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(labelW) }); ColumnDefinitions.Add(new ColumnDefinition());
            var l = Text(label, 12.5, null, Theme.Muted); SetColumn(l, 0); Children.Add(l);
            Box.ToolTip = placeholder.Length > 0 ? placeholder : null; if (!wide) { Box.Width = width; Box.HorizontalAlignment = HorizontalAlignment.Left; }
            SetColumn(Box, 1); Children.Add(Box);
            void Go() { if (Box.Text != last) { last = Box.Text; commit(Box.Text); } else commit(Box.Text); }
            Box.KeyDown += (_, e) => { if (e.Key == Key.Enter) Go(); };
            Box.LostFocus += (_, _) => Go();
            Placeholder = placeholder;
        }
        public string Placeholder { get; }
        public void Set(string v) { if (!Box.IsKeyboardFocusWithin && Box.Text != v) { Box.Text = v; last = v; } }
    }
    public static string Num(double? v) => v is double d ? (d == Math.Round(d) ? ((long)d).ToString() : d.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)) : "";
    public static double? ParseNum(string s) => double.TryParse(s.Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : null;

    /// Interruttore a due stati con aspetto a "pillola" (come i PillToggle della versione Mac).
    public sealed class PillToggle : Border
    {
        readonly TextBlock g, t; bool on; readonly Action<bool> set;
        public PillToggle(string glyph, string label, Action<bool> set, string? tip = null)
        {
            this.set = set; g = Glyph(glyph, 13); t = Text(label, 12, FontWeights.Medium);
            var sp = H(6, g, t); Child = sp; Padding = new Thickness(10, 5, 10, 5); CornerRadius = new CornerRadius(20); Cursor = Cursors.Hand; ToolTip = tip; Paint();
            MouseLeftButtonUp += (_, _) => { this.set(!on); };
        }
        public void Set(bool v) { if (v != on) { on = v; Paint(); } }
        void Paint() { Background = on ? Theme.Accent : Theme.Hover; g.Foreground = t.Foreground = on ? Brushes.White : Theme.Text; }
    }
    public static CheckBox Check(string label, Action<bool> set, bool wrap = false)
    {
        var c = new CheckBox { Content = wrap ? new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap, Foreground = Theme.Text } : (object)label, Foreground = Theme.Text };
        c.Click += (_, _) => set(c.IsChecked == true); return c;
    }
    public static ComboBox Combo(Action<int> pick, params string[] items)
    {
        var c = new ComboBox { MinHeight = 28, HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (var i in items) c.Items.Add(i);
        c.SelectionChanged += (_, _) => { if (c.IsDropDownOpen || c.IsKeyboardFocusWithin) pick(c.SelectedIndex); };
        return c;
    }
}

/// Slider legato a una proprietà di mpv: manda il valore mentre lo si trascina; non si lascia sovrascrivere dalle istantanee durante il gesto.
public sealed class PropSlider : Grid
{
    readonly Slider sl = new() { VerticalAlignment = VerticalAlignment.Center, IsMoveToPointEnabled = true };
    readonly TextBlock val = Ui.Text("", 12, null, Theme.Muted); readonly Func<double, string> fmt; readonly double reset; readonly bool hasReset;
    DateTime userAt = DateTime.MinValue; bool setting;
    public PropSlider(string label, double min, double max, double step, Action<double> send, Func<double, string>? fmt = null, double? reset = null)
    {
        this.fmt = fmt ?? (v => v.ToString("0.00")); hasReset = reset != null; this.reset = reset ?? 0;
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(112) }); ColumnDefinitions.Add(new ColumnDefinition()); ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(58) }); ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var l = Ui.Text(label, 12.5, null, Theme.Muted); SetColumn(l, 0); Children.Add(l);
        sl.Minimum = min; sl.Maximum = max; sl.SmallChange = step; sl.TickFrequency = step; sl.IsSnapToTickEnabled = true; SetColumn(sl, 1); Children.Add(sl);
        val.TextAlignment = TextAlignment.Right; val.FontFamily = Ui.Mono; SetColumn(val, 2); Children.Add(val);
        if (hasReset) { var r = Ui.IconBtn("", "Valore iniziale", () => { send(this.reset); Set(this.reset, true); }, 11); SetColumn(r, 3); Children.Add(r); }
        sl.ValueChanged += (_, e) => { if (setting) return; userAt = DateTime.UtcNow; val.Text = this.fmt(e.NewValue); send(e.NewValue); };
    }
    public void Set(double v, bool force = false)
    {
        if (!force && (sl.IsMouseCaptureWithin || (DateTime.UtcNow - userAt).TotalMilliseconds < 600)) return;
        setting = true; sl.Value = Math.Max(sl.Minimum, Math.Min(sl.Maximum, v)); val.Text = fmt(v); setting = false;
    }
}
