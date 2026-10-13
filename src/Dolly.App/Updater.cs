using System.Diagnostics;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Windows;
using Dolly;

namespace Dolly.App;

/// Controllo degli aggiornamenti dal menu (nessun controllo automatico in background): legge version.json dal sito e, se c'è una versione più nuova, apre la pagina di download.
public static class Updater
{
    public static string Current => Environment.GetEnvironmentVariable("DOLLY_VERSION") ?? typeof(Updater).Assembly.GetName().Version?.ToString(3) ?? "0";
    static string InfoUrl => Environment.GetEnvironmentVariable("DOLLY_UPDATE_URL") ?? "https://www.dollyprojector.app/version.json";
    public static bool IsNewer(string a, string b)
    {
        var x = a.Split('.').Select(s => int.TryParse(s, out var v) ? v : 0).ToArray(); var y = b.Split('.').Select(s => int.TryParse(s, out var v) ? v : 0).ToArray();
        for (int i = 0; i < Math.Max(x.Length, y.Length); i++) { int p = i < x.Length ? x[i] : 0, q = i < y.Length ? y[i] : 0; if (p != q) return p > q; }
        return false;
    }
    public static async void Check(Ctx c)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) }; http.DefaultRequestHeaders.UserAgent.ParseAdd("DollyProjector/" + Current);
            var o = JsonNode.Parse(await http.GetStringAsync(InfoUrl)) as JsonObject ?? throw new DollyError(L.Tr("risposta del sito non valida"));
            var v = o.Get("win_version").S() ?? o.Get("version").S() ?? throw new DollyError(L.Tr("risposta del sito non valida"));
            if (!IsNewer(v, Current)) { MessageBox.Show(c.Owner!, L.Trf("Hai già l'ultima versione (%@).", Current), L.Tr("Aggiornamento")); return; }
            if (MessageBox.Show(c.Owner!, L.Trf("È disponibile Dolly Projector %@. Aprire la pagina di download?", v) + "\n\n" + (o.Get("notes").S() ?? ""), L.Tr("Aggiornamento"), MessageBoxButton.OKCancel) == MessageBoxResult.OK)
                Process.Start(new ProcessStartInfo(o.Get("win_page").S() ?? "https://www.dollyprojector.app/") { UseShellExecute = true });
        }
        catch (Exception e) { MessageBox.Show(c.Owner!, e.Message, L.Tr("Aggiornamento non riuscito"), MessageBoxButton.OK, MessageBoxImage.Warning); }
    }
}
