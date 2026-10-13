using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Dolly;

/// Lingua dell'interfaccia: "auto" (segue il sistema), "it" o "en". DOLLY_LANG serve ai collaudi e agli screenshot.
public static class Lang
{
    public static string Setting
    {
        get => Environment.GetEnvironmentVariable("DOLLY_LANG") ?? Settings.GetString("lang") ?? "auto";
        set => Settings.Set("lang", value);
    }
    public static bool IsEnglish => Setting switch
    {
        "it" => false,
        "en" => true,
        _ => !System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals("it", StringComparison.OrdinalIgnoreCase)
    };
    public static string Code => IsEnglish ? "en" : "it";

    static Dictionary<string, string>? en;
    public static Dictionary<string, string> English
    {
        get
        {
            if (en != null) return en;
            using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("Dolly.en.json");
            en = s == null ? new() : JsonSerializer.Deserialize<Dictionary<string, string>>(s) ?? new();
            return en;
        }
    }
}

public static class L
{
    /// Testo dell'interfaccia: la chiave è l'italiano, in inglese si cerca la traduzione (se manca resta l'italiano).
    public static string Tr(string s) => Lang.IsEnglish && Lang.English.TryGetValue(s, out var e) ? e : s;

    /// Come Tr(), per testi con parti variabili (%@ stringhe, %d interi): i segnaposto si riempiono in ordine.
    public static string Trf(string s, params object?[] args)
    {
        var t = Tr(s); int i = 0;
        t = Regex.Replace(t, "%[@d]", _ => i < args.Length ? Convert.ToString(args[i++], System.Globalization.CultureInfo.InvariantCulture) ?? "" : "");
        return t;
    }
}

/// Preferenze dell'app (al posto di UserDefaults): un piccolo file JSON nella cartella dei dati.
public static class Settings
{
    static readonly object lk = new();
    static string File_ => Path.Combine(Engine.HomePath, "settings.json");
    static System.Text.Json.Nodes.JsonObject Load() => U.ReadJson(File_) as System.Text.Json.Nodes.JsonObject ?? new();
    public static string? GetString(string k) { lock (lk) return Load().Get(k).S(); }
    public static bool? GetBool(string k) { lock (lk) return Load().Get(k).B(); }
    public static void Set(string k, object? v)
    {
        lock (lk)
        {
            var o = Load(); o[k] = U.J(v);
            Directory.CreateDirectory(Engine.HomePath); U.WriteJson(File_, o);
        }
    }
}
