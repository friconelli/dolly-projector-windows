using Dolly;
using WF = System.Windows.Forms;

namespace Dolly.App;

/// Scelte dell'utente ricordate tra una volta e l'altra (cartella dei film, schermo della sala).
public static class AppState
{
    public static string? Folder => Settings.GetString("folder") is { Length: > 0 } f && Directory.Exists(f) ? f : null;
    static int? ScreenSetting => Settings.GetString("screen") is { } s && int.TryParse(s, out var v) ? v : null;
    /// Schermo della sala: quello scelto dal menu, altrimenti l'ultimo (con 2 schermi è l'altro rispetto allo schermo principale).
    public static int ScreenIndex { get { int n = WF.Screen.AllScreens.Length; return ScreenSetting is int s ? (s < 0 ? 0 : Math.Min(s, n - 1)) : n - 1; } }
    public static bool WindowedMode => ScreenSetting is int s ? s < 0 : WF.Screen.AllScreens.Length <= 1;   // prima scelta con un solo schermo: finestra, per non coprire i controlli
}
