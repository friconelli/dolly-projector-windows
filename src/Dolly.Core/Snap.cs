using System.Text.Json.Nodes;

namespace Dolly;

public sealed record Track(int Id, string T, bool Sel);
public sealed record Chapter(string T, double S);
public sealed record PresetInfo(string Name, string Bg, bool BgAudio, string Text, double Secs);
public sealed record AudioDev(string Id, string T);
public sealed class ItemState
{
    public string Kind = "film", Alang = "", Slang = "", Text = "", Name = "", Bg = ""; public double? Pre, Post, Vol; public double Secs, Split; public bool Ok = true, BgAudio; public int Loop; public string? Submode;
    public string Label => Kind == "film" ? System.IO.Path.GetFileNameWithoutExtension(Name) : Name;   // i nomi di pausa/nero possono contenere un punto
}

/// Istantanea dello stato del motore, decodificata dal JSON (la usa l'interfaccia).
public sealed class Snap
{
    public JsonObject J = new();
    public bool Ok = true, Auto = true, Loop, Playing, Pause;
    public string Name = L.Tr("Scena"), Mode = "idle", Label = "", DefSub = "file", Folder = ""; public string? Err;
    public int Idx = -1, Sel, Next = -1, Restarts, Pid; public double Left, DefPre, DefPost, Time, Dur;
    public (int Idx, double Pos)? Resume; public int? Chapter;
    public List<ItemState> Items = new(); public List<string> Lib = new(), Saved = new(); public List<PresetInfo> Presets = new();
    public List<Track> Audio = new(), Sub = new(), Video = new(); public List<Chapter> Chapters = new(); public List<AudioDev> Adevs = new();
    public double?[] Ab = { null, null }; public JsonObject Props = new();
    public string? InfoRes, InfoVcodec, InfoAcodec; public double? InfoFps; public int InfoDropped;

    public double D(string k, double def = 0) => Props.Get(k).D() ?? def;
    public bool B(string k) => Props.Get(k).B() ?? false;
    public string S(string k) => Props.Get(k).S() ?? "";
    public JsonNode? P(string k) => Props.Get(k);

    public Snap() { }
    public Snap(JsonObject j)
    {
        J = j;
        Ok = j.Get("ok").B() ?? true; Name = j.Get("name").S() ?? Name; Mode = j.Get("mode").S() ?? "idle"; Idx = j.Get("idx").I() ?? -1; Sel = j.Get("sel").I() ?? 0; Next = j.Get("next").I() ?? -1;
        Label = j.Get("label").S() ?? ""; Left = j.Get("left").D() ?? 0; Err = j.Get("err").S(); Restarts = j.Get("restarts").I() ?? 0; Auto = j.Get("auto").B() ?? true; Loop = j.Get("loop").B() ?? false;
        DefSub = j.Get("defsub").S() ?? "file"; Pid = j.Get("pid").I() ?? 0; Folder = j.Get("folder").S() ?? ""; DefPre = j.Get("defpre").D() ?? 0; DefPost = j.Get("defpost").D() ?? 0;
        if (j.Get("resume") is JsonObject r && r.Get("idx").I() is int ri) Resume = (ri, r.Get("pos").D() ?? 0);
        Playing = j.Get("playing").B() ?? false; Pause = j.Get("pause").B() ?? false; Time = j.Get("time").D() ?? 0; Dur = j.Get("dur").D() ?? 0; Chapter = j.Get("chapter").I();
        foreach (var o in (j.Get("items") as JsonArray)?.OfType<JsonObject>() ?? Enumerable.Empty<JsonObject>())
            Items.Add(new ItemState
            {
                Kind = o.Get("kind").S() ?? "film", Pre = o.Get("pre").D(), Post = o.Get("post").D(), Vol = o.Get("vol").D(), Alang = o.Get("alang").S() ?? "", Slang = o.Get("slang").S() ?? "", Secs = o.Get("secs").D() ?? 0,
                Text = o.Get("text").S() ?? "", Name = o.Get("name").S() ?? "", Ok = o.Get("ok").B() ?? true, Loop = o.Get("loop").I() ?? 0, Split = o.Get("split").D() ?? 0, Bg = o.Get("bg").S() ?? "",
                BgAudio = o.Get("bgaudio").B() ?? false, Submode = o.Get("submode").S()
            });
        Lib = (j.Get("lib") as JsonArray)?.Select(x => x.S() ?? "").ToList() ?? new();
        Saved = (j.Get("saved") as JsonArray)?.Select(x => x.S() ?? "").ToList() ?? new();
        Presets = (j.Get("presets") as JsonArray)?.OfType<JsonObject>().Select(p => new PresetInfo(p.Get("name").S() ?? "", p.Get("bg").S() ?? "", p.Get("bgaudio").B() ?? false, p.Get("text").S() ?? "", p.Get("secs").D() ?? 0)).ToList() ?? new();
        List<Track> Tr(string k) => (j.Get(k) as JsonArray)?.OfType<JsonObject>().Select(t => new Track(t.Get("id").I() ?? 0, t.Get("t").S() ?? "", t.Get("sel").B() ?? false)).ToList() ?? new();
        Audio = Tr("audio"); Sub = Tr("sub"); Video = Tr("video");
        Chapters = (j.Get("chapters") as JsonArray)?.OfType<JsonObject>().Select(c => new Chapter(c.Get("t").S() ?? "", c.Get("s").D() ?? 0)).ToList() ?? new();
        Adevs = (j.Get("adevs") as JsonArray)?.OfType<JsonObject>().Select(a => new AudioDev(a.Get("id").S() ?? "", a.Get("t").S() ?? "")).ToList() ?? new();
        if (j.Get("ab") is JsonArray ab && ab.Count == 2) Ab = new[] { ab[0].D(), ab[1].D() };
        Props = j.Get("props") as JsonObject ?? new();
        if (j.Get("info") is JsonObject inf) { InfoRes = inf.Get("res").S(); InfoFps = inf.Get("fps").D(); InfoVcodec = inf.Get("vcodec").S(); InfoAcodec = inf.Get("acodec").S(); InfoDropped = inf.Get("dropped").I() ?? 0; }
    }
}
