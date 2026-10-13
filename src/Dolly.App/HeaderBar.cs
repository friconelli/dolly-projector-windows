using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Dolly;

namespace Dolly.App;

public sealed class HeaderBar : Border
{
    readonly Ctx c; readonly TextBlock title = Ui.Text("", 15, FontWeights.SemiBold), err = Ui.Text("", 12.5, null, Theme.Orange);
    readonly Button resume; readonly Border pill = new() { CornerRadius = new CornerRadius(20), Padding = new Thickness(12, 5, 12, 5) };
    readonly Ellipse dot = new() { Width = 8, Height = 8 }; readonly TextBlock pillText = Ui.Text("", 12, FontWeights.SemiBold);
    public HeaderBar(Ctx c)
    {
        this.c = c; Padding = new Thickness(16, 10, 16, 10); BorderBrush = Theme.Line; BorderThickness = new Thickness(0, 0, 0, 1);
        var g = new Grid(); for (int i = 0; i < 5; i++) g.ColumnDefinitions.Add(new ColumnDefinition { Width = i == 1 ? new GridLength(1, GridUnitType.Star) : GridLength.Auto });
        var ring = new Ellipse { Width = 15, Height = 15, Stroke = Theme.Cue, StrokeThickness = 3.5, Margin = new Thickness(0, 0, 10, 0) };
        Grid.SetColumn(ring, 0); g.Children.Add(ring);
        Grid.SetColumn(title, 1); g.Children.Add(title);
        err.Margin = new Thickness(10, 0, 10, 0); Grid.SetColumn(err, 2); g.Children.Add(err);
        resume = Ui.Btn("", () => c.Cmd("resume")); resume.Margin = new Thickness(0, 0, 10, 0); resume.Visibility = Visibility.Collapsed; Grid.SetColumn(resume, 3); g.Children.Add(resume);
        pill.Child = Ui.H(6, dot, pillText); Grid.SetColumn(pill, 4); g.Children.Add(pill);
        Child = g;
    }
    public void Update(Snap s)
    {
        string cur = s.Idx >= 0 && s.Idx < s.Items.Count ? s.Items[s.Idx].Label : "";
        title.Text = s.Mode switch
        {
            "gap" => s.Label.Length == 0 ? L.Tr("Nero") : s.Label,
            "playing" or "wait" => cur,
            _ => s.Sel >= 0 && s.Sel < s.Items.Count ? L.Tr("Prossimo: ") + s.Items[s.Sel].Label : L.Tr("Nessun elemento")
        };
        if (s.Err != null) { err.Text = "⚠ " + s.Err; err.Visibility = Visibility.Visible; }
        else if (s.Restarts > 0) { err.Text = L.Trf("player riavviato %d×", s.Restarts); err.Foreground = Theme.Muted; err.Visibility = Visibility.Visible; }
        else err.Visibility = Visibility.Collapsed;
        if (s.Err != null) err.Foreground = Theme.Orange;
        if (s.Resume is { } r && s.Mode == "idle" && r.Idx < s.Items.Count) { resume.Content = L.Trf("Riprendi %@ da %@", s.Items[r.Idx].Label, U.Fmt(r.Pos)); resume.Visibility = Visibility.Visible; }
        else resume.Visibility = Visibility.Collapsed;
        var looping = s.S("loop-file") is { Length: > 0 } v && v != "no";
        string txt = !s.Ok ? L.Tr("player non risponde") : s.Mode switch
        {
            "gap" => L.Trf("NERO %d″", (int)s.Left),
            "wait" => L.Trf("INTERVALLO %@", U.Fmt(s.Left)),
            "playing" => s.Pause ? L.Tr("IN PAUSA") : (looping ? L.Tr("IN ONDA · RIPETE") : L.Tr("IN ONDA")),
            _ => L.Tr("PRONTO")
        };
        Brush col = !s.Ok ? Brushes.Red : s.Mode == "playing" ? (s.Pause ? Theme.Orange : Theme.Cue) : (s.Mode == "gap" || s.Mode == "wait") ? Theme.Orange : Theme.Muted;
        pillText.Text = txt; pillText.Foreground = col; dot.Fill = col;
        var bc = ((SolidColorBrush)col).Color; pill.Background = new SolidColorBrush(Color.FromArgb(36, bc.R, bc.G, bc.B));
    }
}
