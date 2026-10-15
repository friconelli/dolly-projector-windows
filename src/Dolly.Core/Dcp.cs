using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace Dolly;

/// DCP (Digital Cinema Package) non cifrati: dal CPL si costruisce un'unica "lista di montaggio" (EDL) per mpv, con un flusso video (JPEG 2000, XYZ) e uno audio (PCM)
/// che attraversano tutti i reel senza interruzioni. I sottotitoli di testo (Interop e SMPTE) diventano un file SRT con i tempi della sequenza intera.
/// Un elemento della scaletta che sia un DCP ha come percorso il file CPL (.xml): la cartella del pacchetto è quella che lo contiene.
public static class Dcp
{
    public sealed record Prepared(string Edl, string? Subs, double Duration);

    static readonly object lk = new();
    static readonly Dictionary<string, (DateTime, List<string>)> cplCache = new();
    static readonly Dictionary<string, string> titleCache = new();

    // MARK: rilevamento
    /// Il file ASSETMAP di una cartella (Interop: ASSETMAP, SMPTE: ASSETMAP.xml).
    public static string? AssetMapPath(string dir)
    {
        foreach (var n in new[] { "ASSETMAP.xml", "ASSETMAP", "assetmap.xml", "assetmap" }) { var p = Path.Combine(dir, n); if (File.Exists(p)) return p; }
        return null;
    }
    /// Un percorso che finisce in .xml dentro la scaletta è sempre un DCP (gli altri film sono mp4/avi/mkv).
    public static bool IsCplPath(string p) => p.EndsWith(".xml", StringComparison.OrdinalIgnoreCase);

    /// Il documento è davvero un CPL? (si guarda solo l'inizio del file)
    public static bool SniffCpl(string path)
    {
        try { using var f = File.OpenRead(path); var b = new byte[4096]; int n = f.Read(b, 0, b.Length); return Encoding.UTF8.GetString(b, 0, n).Contains("CompositionPlaylist"); }
        catch { return false; }
    }

    static XElement? Doc(string path)
    {
        try { using var r = XmlReader.Create(path, new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null }); return XDocument.Load(r).Root; }
        catch { return null; }
    }
    static XElement? Doc(byte[] data)
    {
        try { using var r = XmlReader.Create(new MemoryStream(data), new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null }); return XDocument.Load(r).Root; }
        catch { return null; }
    }

    /// Mappa id -> percorso assoluto dei file del pacchetto, letta dall'ASSETMAP.
    public static Dictionary<string, string> AssetMap(string dir)
    {
        var m = new Dictionary<string, string>();
        if (AssetMapPath(dir) is not string ap || Doc(ap) is not XElement root) return m;
        foreach (var a in root.Desc("Asset"))
        {
            var id = a.Text("Id"); var path = a.Desc("Path").FirstOrDefault()?.Value.Trim();
            if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(path)) continue;
            if (path.StartsWith("file://", StringComparison.OrdinalIgnoreCase)) { try { path = new Uri(path).LocalPath; } catch { path = path.Substring(7); } }
            m[CleanId(id)] = Path.IsPathRooted(path) ? path : Path.Combine(dir, path);
        }
        return m;
    }
    public static string CleanId(string s) => s.ToLowerInvariant().Replace("urn:uuid:", "").Trim();

    /// I CPL (uno per ogni versione del film) di una cartella-DCP; vuoto se la cartella non è un DCP. Il risultato resta in cache finché l'ASSETMAP non cambia.
    public static List<string> Cpls(string dir)
    {
        if (AssetMapPath(dir) is not string ap) return new();
        var mod = File.GetLastWriteTimeUtc(ap);
        lock (lk) { if (cplCache.TryGetValue(dir, out var c) && c.Item1 == mod) return c.Item2; }
        var found = AssetMap(dir).Values.Where(p => p.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) && File.Exists(p) && SniffCpl(p)).OrderBy(x => x, StringComparer.Ordinal).ToList();
        if (found.Count == 0)   // ASSETMAP incompleto: si cercano comunque i CPL accanto ad esso
            found = Directory.GetFiles(dir, "*.xml").Where(SniffCpl).OrderBy(x => x, StringComparer.Ordinal).ToList();
        lock (lk) cplCache[dir] = (mod, found);
        return found;
    }

    /// Titolo del film (ContentTitleText del CPL), in cache.
    public static string Title(string cpl)
    {
        lock (lk) { if (titleCache.TryGetValue(cpl, out var c)) return c; }
        string t = "";
        if (Doc(cpl) is XElement r) t = r.Text("ContentTitleText") ?? r.Text("AnnotationText") ?? "";
        if (t.Length == 0) t = Path.GetFileName(Path.GetDirectoryName(cpl) ?? "");
        lock (lk) titleCache[cpl] = t;
        return t;
    }

    // MARK: preparazione della riproduzione
    sealed record Asset(string Id, double Rate, double Entry, double Dur, bool Encrypted, string? Path)
    {
        public double Start => Entry / Rate; public double Length => Dur / Rate;
    }
    sealed record Cue(double T0, double T1, string Text);

    static double Rate(string? s)
    {
        var p = (s ?? "").Split(new[] { ' ', '/' }, StringSplitOptions.RemoveEmptyEntries).Select(x => double.TryParse(x, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0).ToArray();
        if (p.Length == 0 || p[0] <= 0) return 24;
        return p.Length > 1 && p[1] > 0 ? p[0] / p[1] : p[0];
    }
    static double Num(string? s, double def) => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : def;
    static Asset MakeAsset(XElement e, Dictionary<string, string> amap)
    {
        var id = CleanId(e.Text("Id") ?? ""); var r = Rate(e.Text("EditRate") ?? e.Text("FrameRate"));
        double entry = Num(e.Text("EntryPoint"), 0), intr = Num(e.Text("IntrinsicDuration"), 0), dur = Num(e.Text("Duration"), Math.Max(0, intr - entry));
        return new Asset(id, r, entry, dur, e.Kid("KeyId") != null, amap.TryGetValue(id, out var p) ? p : null);
    }

    /// Legge il CPL e prepara la lista di montaggio per mpv. Lancia con un messaggio chiaro se il DCP è cifrato, 3D, incompleto o non valido.
    public static Prepared Prepare(string cpl, string cacheDir)
    {
        var dir = Path.GetDirectoryName(cpl) ?? "";
        if (Doc(cpl) is not XElement root || root.Ln() != "CompositionPlaylist") throw new DollyError(L.Trf("DCP non valido: %@", Path.GetFileName(cpl)));
        var amap = AssetMap(dir);
        if (amap.Count == 0) throw new DollyError(L.Tr("DCP incompleto: manca il file ASSETMAP"));
        var pics = new List<Asset>(); var snds = new List<Asset?>(); var subs = new List<Asset?>();
        foreach (var reel in root.Desc("Reel"))
        {
            var list = reel.Kid("AssetList"); if (list == null) continue;
            if (list.Kid("MainStereoscopicPicture") != null) throw new DollyError(L.Tr("DCP 3D: non supportato"));
            var p = list.Kid("MainPicture"); if (p == null) continue;
            pics.Add(MakeAsset(p, amap)); snds.Add(list.Kid("MainSound") is { } s ? MakeAsset(s, amap) : null); subs.Add(list.Kid("MainSubtitle") is { } t ? MakeAsset(t, amap) : null);
        }
        if (pics.Count == 0) throw new DollyError(L.Tr("il DCP non contiene immagini"));
        if (pics.Any(a => a.Encrypted) || snds.Any(a => a?.Encrypted == true)) throw new DollyError(L.Tr("il DCP è cifrato: serve una KDM e Dolly non può proiettarlo"));
        string File_(Asset a)
        {
            if (a.Path == null || !File.Exists(a.Path)) throw new DollyError(L.Trf("DCP incompleto: manca il file %@", a.Path != null ? Path.GetFileName(a.Path) : a.Id));
            return a.Path;
        }
        string Seg(Asset a) { var p = File_(a); return $"%{Encoding.UTF8.GetByteCount(p)}%{p},{a.Start.ToString("F6", CultureInfo.InvariantCulture)},{a.Length.ToString("F6", CultureInfo.InvariantCulture)}"; }
        var edl = "edl://!new_stream;!no_chapters;" + string.Join(";", pics.Select(Seg));
        if (snds.All(a => a != null)) edl += ";!new_stream;!no_chapters;" + string.Join(";", snds.Select(a => Seg(a!)));   // audio solo se ce l'hanno tutti i reel
        double total = pics.Sum(a => a.Length);

        // sottotitoli di testo: un unico SRT con i tempi della sequenza intera
        var cues = new List<Cue>(); double offset = 0;
        for (int i = 0; i < pics.Count; i++)
        {
            if (subs[i] is { Encrypted: false, Path: { } sp } s && File.Exists(sp)) cues.AddRange(ParseSubtitles(sp, offset - s.Start));
            offset += pics[i].Length;
        }
        string? srt = null;
        if (cues.Count > 0)
        {
            try { Directory.CreateDirectory(cacheDir); var path = Path.Combine(cacheDir, "dcp-" + Hash(cpl) + ".srt"); File.WriteAllText(path, SrtText(cues), new UTF8Encoding(false)); srt = path; } catch { }
        }
        return new Prepared(edl, srt, total);
    }
    static string Hash(string s) { ulong h = 5381; foreach (var b in Encoding.UTF8.GetBytes(s)) h = (h * 33) ^ b; return h.ToString("x"); }

    // MARK: sottotitoli
    static int IndexOf(byte[] data, byte[] pat, int from)
    {
        var i = data.AsSpan(from).IndexOf(pat); return i < 0 ? -1 : from + i;
    }
    /// L'XML dei sottotitoli: un file .xml (Interop) oppure incorporato in un MXF (SMPTE, non cifrato).
    static XElement? SubtitleDoc(string path)
    {
        byte[] data; try { data = File.ReadAllBytes(path); } catch { return null; }
        if (path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)) return Doc(data);
        int a = IndexOf(data, Encoding.ASCII.GetBytes("<?xml"), 0); if (a < 0) return null;
        foreach (var tag in new[] { "</SubtitleReel>", "</DCSubtitle>" })
        {
            int e = IndexOf(data, Encoding.ASCII.GetBytes(tag), a);
            if (e >= 0) return Doc(data.AsSpan(a, e + tag.Length - a).ToArray());
        }
        return null;
    }
    /// "HH:MM:SS:FF" (SMPTE: fotogrammi), "HH:MM:SS:TTT" (Interop: 1/250 di secondo) o "HH:MM:SS.mmm".
    static double? Timecode(string s, double rate, bool interop)
    {
        var t = s.Trim(); var inv = CultureInfo.InvariantCulture;
        int dot = t.LastIndexOf('.');
        if (dot > 0 && t.Count(c => c == ':') == 2)
        {
            var p = t.Substring(0, dot).Split(':').Select(x => double.TryParse(x, NumberStyles.Float, inv, out var v) ? v : double.NaN).ToArray();
            if (p.Length != 3 || p.Any(double.IsNaN) || !double.TryParse("0" + t.Substring(dot), NumberStyles.Float, inv, out var f)) return null;
            return p[0] * 3600 + p[1] * 60 + p[2] + f;
        }
        var q = t.Split(':').Select(x => double.TryParse(x, NumberStyles.Float, inv, out var v) ? v : double.NaN).ToArray();
        if (q.Length != 4 || q.Any(double.IsNaN)) return null;
        return q[0] * 3600 + q[1] * 60 + q[2] + q[3] / (interop ? 250 : Math.Max(1, rate));
    }
    static List<Cue> ParseSubtitles(string path, double offset)
    {
        var res = new List<Cue>(); if (SubtitleDoc(path) is not XElement root) return res;
        bool interop = root.Ln() == "DCSubtitle"; double r = Num(root.Text("TimeCodeRate"), Rate(root.Text("EditRate")));
        foreach (var s in root.Desc("Subtitle"))
        {
            var a = s.Attribute("TimeIn")?.Value; var b = s.Attribute("TimeOut")?.Value; if (a == null || b == null) continue;
            if (Timecode(a, r, interop) is not double t0 || Timecode(b, r, interop) is not double t1) continue;
            var lines = s.Desc("Text").Select(x => x.Value.Trim()).Where(x => x.Length > 0).ToList();
            if (lines.Count == 0) continue;   // (sottotitoli a immagini PNG: non supportati)
            res.Add(new Cue(Math.Max(0, t0 + offset), Math.Max(0, t1 + offset), string.Join("\n", lines)));
        }
        return res;
    }
    static string SrtText(List<Cue> cues)
    {
        static string Ts(double x) { long ms = (long)Math.Round(x * 1000); return $"{ms / 3_600_000:00}:{ms / 60_000 % 60:00}:{ms / 1000 % 60:00},{ms % 1000:000}"; }
        return string.Join("\n", cues.OrderBy(c => c.T0).Select((c, i) => $"{i + 1}\n{Ts(c.T0)} --> {Ts(c.T1)}\n{c.Text}\n"));
    }
}

static class XmlExt
{
    /// Nome senza prefisso di namespace.
    public static string Ln(this XElement e) => e.Name.LocalName;
    public static IEnumerable<XElement> Kids(this XElement e, string n) => e.Elements().Where(x => x.Name.LocalName == n);
    public static XElement? Kid(this XElement e, string n) => e.Kids(n).FirstOrDefault();
    public static string? Text(this XElement e, string n) => e.Kid(n)?.Value.Trim();
    /// Tutti i discendenti con quel nome (in ordine di documento).
    public static IEnumerable<XElement> Desc(this XElement e, string n) => e.Descendants().Where(x => x.Name.LocalName == n);
}
