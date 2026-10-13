using System.Text.Json.Nodes;

namespace Dolly;

public static class Model
{
    public static readonly HashSet<string> VideoExt = new() { "mp4", "avi", "mkv" };   // gli unici formati supportati

    /// Proprietà impostabili dal telecomando: nome -> (tipo, min, max). Tutto il resto viene rifiutato.
    public static readonly Dictionary<string, (char kind, double lo, double hi)> Props = new()
    {
        ["sub-font"] = ('s', 0, 0), ["sub-font-size"] = ('f', 10, 150), ["sub-color"] = ('s', 0, 0), ["sub-border-color"] = ('s', 0, 0), ["sub-border-size"] = ('f', 0, 15),
        ["sub-back-color"] = ('s', 0, 0), ["sub-shadow-offset"] = ('f', 0, 15), ["sub-bold"] = ('b', 0, 0), ["sub-italic"] = ('b', 0, 0), ["sub-ass-override"] = ('s', 0, 0),
        ["audio-device"] = ('s', 0, 0), ["volume"] = ('f', 0, 130), ["mute"] = ('b', 0, 0), ["speed"] = ('f', 0.25, 4), ["audio-delay"] = ('f', -10, 10), ["sub-delay"] = ('f', -30, 30),
        ["sub-scale"] = ('f', 0.3, 3), ["sub-pos"] = ('f', 0, 100), ["sub-visibility"] = ('b', 0, 0), ["video-zoom"] = ('f', -1, 2), ["video-pan-x"] = ('f', -1, 1),
        ["video-pan-y"] = ('f', -1, 1), ["panscan"] = ('f', 0, 1), ["brightness"] = ('f', -100, 100), ["contrast"] = ('f', -100, 100), ["saturation"] = ('f', -100, 100),
        ["gamma"] = ('f', -100, 100), ["hue"] = ('f', -100, 100), ["video-aspect-override"] = ('s', 0, 0), ["deinterlace"] = ('b', 0, 0), ["loop-file"] = ('s', 0, 0),
        ["audio-channels"] = ('s', 0, 0), ["ontop"] = ('b', 0, 0)
    };
    public static readonly string[] ReadProps = Props.Keys.ToArray();

    public static List<string> VideosIn(string path)
    {
        if (Directory.Exists(path))
        {
            var names = Directory.GetFiles(path).Select(Path.GetFileName).Where(n => n != null).Select(n => n!).ToList();
            names.Sort(string.CompareOrdinal);
            return names.Where(n => !n.StartsWith('.') && VideoExt.Contains(Path.GetExtension(n).TrimStart('.').ToLowerInvariant())).Select(n => Path.Combine(path, n)).ToList();
        }
        if (File.Exists(path) && VideoExt.Contains(Path.GetExtension(path).TrimStart('.').ToLowerInvariant())) return new() { path };
        return new();
    }
}

/// Elemento di scaletta: film (mp4/avi/mkv) | pausa (intervallo con testo e conto alla rovescia) | nero (schermo nero per N secondi, senza scritte).
public sealed class Item
{
    public string Kind = "film", Path = "", Alang = "", Slang = "", Text = "", Bg = "";
    public double? Pre, Post, Vol;
    public double Secs = 600, Split;       // Split: secondi dall'inizio a cui il film si ferma per un intervallo (0 = nessuno)
    public int Loop;                       // ripetizioni: 0 nessuna, -1 all'infinito, N volte in più
    public bool BgAudio;                   // sfondo dell'intervallo: immagine o video in loop, con o senza audio
    public string? Submode;                // nil = predefinito della scena; "file" | "none" | "forced" | "full"

    public Item() { }
    public Item(string path, string kind = "film", double secs = 600, string text = "") { Path = path; Kind = kind; Secs = secs; Text = text; }
    public Item(JsonObject d)
    {
        Kind = d.Get("kind").S() ?? "film"; Path = d.Get("path").S() ?? "";
        Pre = d.Get("pre").D(); Post = d.Get("post").D(); Vol = d.Get("vol").D();
        Alang = d.Get("alang").S() ?? ""; Slang = d.Get("slang").S() ?? ""; Secs = d.Get("secs").D() ?? 600; Text = d.Get("text").S() ?? "";
        Loop = d.Get("loop").I() ?? 0; Split = d.Get("split").D() ?? 0; Bg = d.Get("bg").S() ?? ""; BgAudio = d.Get("bgaudio").B() ?? false;
        var sm = d.Get("submode").S(); Submode = string.IsNullOrEmpty(sm) ? null : sm;
    }
    public JsonObject ToJson() => new()
    {
        ["kind"] = Kind, ["path"] = Path, ["pre"] = U.J(Pre), ["post"] = U.J(Post), ["vol"] = U.J(Vol),
        ["alang"] = Alang, ["slang"] = Slang, ["secs"] = Secs, ["text"] = Text, ["loop"] = Loop, ["split"] = Split, ["bg"] = Bg, ["bgaudio"] = BgAudio, ["submode"] = U.J(Submode)
    };
    public string Title => Kind == "pausa" ? (Text.Length == 0 ? L.Tr("Pausa") : Text)
        : Kind == "nero" ? L.Trf("Nero (%@ s)", Secs == Math.Round(Secs) ? ((int)Secs).ToString() : Secs.ToString(System.Globalization.CultureInfo.InvariantCulture))
        : System.IO.Path.GetFileName(Path);
}
