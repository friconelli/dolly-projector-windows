using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace Dolly.App;

/// Colori dell'app: seguono il tema chiaro/scuro di Windows (i controlli standard li gestisce il tema Fluent di WPF).
public static class Theme
{
    public static bool Dark { get; private set; }
    public static Brush Bg = null!, Panel = null!, Card = null!, Line = null!, Text = null!, Muted = null!, Faint = null!, Accent = null!, AccentSoft = null!, Cue = null!, CueSoft = null!, Orange = null!, Hover = null!, Track = null!;

    static SolidColorBrush B(string hex) { var b = (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!; b.Freeze(); return b; }
    public static SolidColorBrush Hex(string hex) => B(hex);

    public static void Init()
    {
        try { Dark = (int)(Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 1) ?? 1) == 0; } catch { Dark = false; }
        if (Dark)
        {
            Bg = B("#1C1C1E"); Panel = B("#232326"); Card = B("#2C2C30"); Line = B("#3A3A3F"); Text = B("#F5F5F7"); Muted = B("#A1A1A6"); Faint = B("#6E6E73");
            Hover = B("#33FFFFFF"); Track = B("#3A3A3F");
        }
        else
        {
            Bg = B("#F4F4F6"); Panel = B("#ECECEF"); Card = B("#FFFFFF"); Line = B("#D9D9DE"); Text = B("#1D1D1F"); Muted = B("#6E6E73"); Faint = B("#A1A1A6");
            Hover = B("#14000000"); Track = B("#D9D9DE");
        }
        Accent = B("#0A84FF"); AccentSoft = B("#330A84FF"); Cue = B("#E0392E"); CueSoft = B("#26E0392E"); Orange = B("#FF9F0A");
    }
}
