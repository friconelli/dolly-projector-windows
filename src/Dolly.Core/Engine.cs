using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Dolly;

/// La regia: sequenza della scena (film, neri, intervalli), controllo di mpv, riavvio se si pianta, salvataggio.
/// Tutto lo stato vive sulla coda `q`; l'interfaccia riceve un'istantanea aggiornata ogni secondo.
public sealed class Engine
{
    public static string HomePath => Environment.GetEnvironmentVariable("DOLLY_HOME") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Dolly");

    public readonly string Folder, Home;
    public event Action<JsonObject>? Snapshot;          // istantanea per l'interfaccia (ogni ~1 s e dopo ogni comando)
    public event Action<byte[]?>? PreviewChanged;       // JPEG di ciò che si vede in sala, finché si proietta
    public JsonObject? LastState { get; private set; }

    readonly SerialQueue q = new();
    Mpv mpv;
    Timer? timer; int tickQueued;
    List<Item> items = new(); string name = L.Tr("Scena"); double defpre, defpost; bool auto = true, loop; string defsub = "file";
    JsonObject prefs = new(); JsonObject? resume; List<JsonObject> presets = new();
    bool splitting; int splitDone = -1;                  // intervallo a metà film in corso / già fatto per questo film
    string mode = "idle"; int idx = -1, sel, next = -1; double until; string label = "";
    double pos, dur, loadedAt, lastSave; int shown, retries, fails, restarts, tickCount;
    string? err; bool quitting; byte[]? pvData; double pvT;
    bool previewOn = Settings.GetBool("preview") ?? true;
    bool langPending; int langTries;                     // scelta di audio/sottotitoli del film appena partito
    bool loopFile;                                       // "ripeti questo film": vale solo per il film in corso
    Process? caff;

    static JsonNode? C(JsonNode? n) => n?.DeepClone();

    public Engine(string folder, string mpvBinary, string[] extra, bool windowed, int screen, bool autoresume, bool resetPlaylist = false)
    {
        Folder = ExpandHome(folder); Home = HomePath;
        Directory.CreateDirectory(Path.Combine(Home, "playlists"));
        mpv = new Mpv(mpvBinary, extra, windowed, screen);
        LoadCurrent(); KillOrphans();
        presets = (U.ReadJson(Path.Combine(Home, "presets.json")) as JsonArray)?.OfType<JsonObject>().Select(o => (JsonObject)o.DeepClone()).ToList() ?? new();
        if (resetPlaylist) { items = Model.VideosIn(Folder).Select(p => new Item(p)).ToList(); idx = -1; sel = 0; resume = null; }   // cartella cambiata: la scaletta riparte dai suoi film
        mpv.Start(); ApplyPrefs();
        if (autoresume && resume != null && resume.Get("idx").I() is int ri && ri < items.Count && U.Now - (resume.Get("t").D() ?? 0) < 60)   // riavvio dopo un arresto imprevisto, a proiezione in corso
        {
            U.Log("riprendo da solo:", resume.ToJsonString()); Load(ri, resume.Get("pos").D() ?? 0);
        }
        KeepAwake();
        timer = new Timer(_ => { if (Interlocked.Exchange(ref tickQueued, 1) == 0) q.Async(TickLoop); }, null, 250, 250);
    }

    static string ExpandHome(string p) => p.StartsWith("~") ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), p.TrimStart('~', '/', '\\')) : p;

    void KeepAwake()
    {
        if (OperatingSystem.IsWindows()) Win32.KeepAwake(true);
        else if (File.Exists("/usr/bin/caffeinate"))
        {
            try { caff = Process.Start(new ProcessStartInfo("/usr/bin/caffeinate", new[] { "-dis", "-w", Environment.ProcessId.ToString() }) { UseShellExecute = false }); } catch { }
        }
    }

    // MARK: persistenza
    string CFile => Path.Combine(Home, "current.json");
    JsonObject Dump() => new()
    {
        ["name"] = name, ["items"] = U.Arr(items.Select(i => (object?)i.ToJson())), ["defpre"] = defpre, ["defpost"] = defpost, ["auto"] = auto, ["loop"] = loop, ["defsub"] = defsub,
        ["prefs"] = C(prefs), ["resume"] = C(resume)
    };
    void Save() => U.WriteJson(CFile, Dump());
    void WritePresets() => U.WriteJson(Path.Combine(Home, "presets.json"), U.Arr(presets.Select(p => (object?)p)));
    static readonly string[] SubModes = { "file", "none", "forced", "full" };
    void LoadCurrent()
    {
        var d = U.ReadJson(CFile) as JsonObject ?? new();
        name = d.Get("name").S() ?? L.Tr("Scena"); defpre = d.Get("defpre").D() ?? 0; defpost = d.Get("defpost").D() ?? 0;
        auto = d.Get("auto").B() ?? true; loop = d.Get("loop").B() ?? false;
        prefs = new JsonObject(); if (d.Get("prefs") is JsonObject pf) foreach (var kv in pf) if (kv.Key != "loop-file" && kv.Key != "speed") prefs[kv.Key] = C(kv.Value);
        defsub = SubModes.Contains(d.Get("defsub").S() ?? "") ? d.Get("defsub").S()! : "file";
        resume = d.Get("resume") as JsonObject is JsonObject r ? (JsonObject)r.DeepClone() : null;
        items = (d.Get("items") as JsonArray)?.OfType<JsonObject>().Select(o => new Item(o)).ToList() ?? new();
        if (items.Count == 0) items = Model.VideosIn(Folder).Select(p => new Item(p)).ToList();
    }
    string PlPath(string n)
    {
        var ok = new string(n.Where(c => char.IsLetterOrDigit(c) || " -_()".Contains(c)).ToArray()).Trim();
        return Path.Combine(Home, "playlists", ok + ".json");
    }
    List<string> SavedLists()
    {
        var dir = Path.Combine(Home, "playlists");
        if (!Directory.Exists(dir)) return new();
        var l = Directory.GetFiles(dir, "*.json").Select(f => Path.GetFileNameWithoutExtension(f)).ToList(); l.Sort(string.CompareOrdinal); return l;
    }

    // MARK: mpv
    void KillOrphans()
    {
        // se una copia precedente è stata uccisa di forza, il suo mpv resterebbe aperto sullo schermo: lo chiudiamo
        // (su Windows ci pensa il job object: mpv muore insieme all'app)
        if (OperatingSystem.IsWindows()) return;
        try
        {
            var p = Process.Start(new ProcessStartInfo("/bin/ps", new[] { "-axo", "pid=,args=" }) { RedirectStandardOutput = true, UseShellExecute = false });
            var o = p!.StandardOutput.ReadToEnd(); p.WaitForExit();
            var re = new Regex(@"--input-ipc-server=\S*dolly-(\d+)-\d+\.sock");
            foreach (var line in o.Split('\n'))
            {
                var m = re.Match(line); if (!m.Success) continue;
                int owner = int.Parse(m.Groups[1].Value); var first = line.Trim().Split(' ')[0];
                if (owner == Environment.ProcessId || !int.TryParse(first, out var pid)) continue;
                bool alive; try { Process.GetProcessById(owner); alive = true; } catch { alive = false; }
                if (!alive) { U.Log("chiudo un mpv rimasto aperto:", pid); try { Process.GetProcessById(pid).Kill(); } catch { } }
            }
        }
        catch { }
    }

    /// Stile iniziale dei sottotitoli. Lo stile di Dolly vale anche sui sottotitoli già stilizzati (ASS): altrimenti il cambio di font o colore non si vedrebbe su quei film.
    public static readonly Dictionary<string, object> SubStyleDefaults = new()
    {
        ["sub-font"] = "sans-serif", ["sub-font-size"] = 38.0, ["sub-color"] = "#FFFFFF", ["sub-border-color"] = "#000000", ["sub-border-size"] = 1.65,
        ["sub-back-color"] = "#00000000", ["sub-shadow-offset"] = 0.0, ["sub-bold"] = false, ["sub-italic"] = false, ["sub-pos"] = 100.0, ["sub-scale"] = 1.0, ["sub-ass-override"] = "force"
    };

    JsonNode?[]? Try(params object[][] cmds) { try { return mpv.Ipc(cmds, 2); } catch { return null; } }

    void ApplyPrefs()
    {
        var cmds = new List<object[]> { new object[] { "set_property", "sub-ass-override", "force" } };
        cmds.AddRange(prefs.Select(kv => new object[] { "set_property", kv.Key, kv.Value! }));
        try { mpv.Ipc(cmds.ToArray(), 2); } catch (Exception e) { U.Log("prefs:", e.Message); }
    }
    void Respawn(string why)
    {
        U.Log("RIAVVIO mpv:", why); restarts++;
        mpv.Kill();
        try { mpv.Start(); } catch (Exception e) { U.Log("mpv non riparte:", e.Message); return; }
        fails = 0; ApplyPrefs();
        if (mode == "playing") Load(idx, Math.Max(0, pos - 1)); else if (mode == "wait") shown = 0;
    }
    double Pre(int i) => items[i].Pre ?? defpre;
    double Post(int i) => items[i].Post ?? defpost;

    // MARK: sequenza
    void Load(int i, double start = 0)
    {
        if (i < 0 || i >= items.Count) return;
        var it = items[i];
        splitting = false; if (start == 0) splitDone = -1;
        if (it.Kind == "pausa" || it.Kind == "nero")   // intervallo (testo e conto alla rovescia) o nero puro: schermo nero per un tempo
        {
            StartBg(it); mode = "wait"; idx = i; sel = i; until = U.Now + it.Secs; shown = 0; err = null; return;
        }
        if (!File.Exists(it.Path)) { err = L.Trf("file mancante: %@", it.Title); U.Log(err); Advance(i); return; }
        // lingue e livello di questo film (le opzioni valgono per il file che sta per essere aperto)
        var sm = SubMode(it);
        var cmds = new List<object[]> {
            new object[] { "set_property", "alang", it.Alang.Length == 0 ? "ita,it,eng,en" : it.Alang }, new object[] { "set_property", "slang", it.Slang },
            new object[] { "set_property", "sid", sm == "none" ? "no" : "auto" }, new object[] { "set_property", "aid", "auto" }, new object[] { "set_property", "vid", "auto" } };   // le scelte fatte su un film non devono passare al successivo
        if (it.Vol is double v) { cmds.Add(new object[] { "set_property", "volume", v }); prefs["volume"] = v; }
        langPending = it.Kind == "film" && (it.Alang.Length > 0 || it.Slang.Length > 0 || sm == "forced" || sm == "full"); langTries = 0;
        if (start == 0) loopFile = it.Loop != 0;   // un nuovo film parte con la sua ripetizione; una ripresa dopo un blocco mantiene lo stato attuale
        cmds.Add(new object[] { "set_property", "loop-file", start == 0 ? (it.Loop < 0 ? "inf" : it.Loop > 0 ? it.Loop.ToString() : "no") : (loopFile ? "inf" : "no") });
        object[] ld = start > 0 ? new object[] { "loadfile", it.Path, "replace", -1, "start=" + start.ToString(CultureInfo.InvariantCulture) } : new object[] { "loadfile", it.Path, "replace" };
        try { cmds.Add(ld); cmds.Add(new object[] { "set_property", "pause", false }); mpv.Ipc(cmds.ToArray(), 2); }
        catch (Exception e) { U.Log("loadfile:", e.Message); return; }
        mode = "playing"; idx = i; sel = i; loadedAt = U.Now; pos = start; dur = 0; err = null;
        if (start == 0) retries = 0;
    }
    /// Schermo dell'intervallo: lo sfondo scelto in loop (opzioni valide solo per quel file), altrimenti nero.
    void StartBg(Item it)
    {
        if (it.Bg.Length == 0 || !File.Exists(it.Bg)) { Try(new object[] { "stop" }); return; }
        var opts = $"loop-file=inf,image-display-duration=inf,mute={(it.BgAudio ? "no" : "yes")},pause=no,aid={(it.BgAudio ? "auto" : "no")},sid=no";
        try { mpv.Ipc(new object[][] { new object[] { "set_property", "loop-file", "no" }, new object[] { "loadfile", it.Bg, "replace", -1, opts }, new object[] { "set_property", "pause", false } }, 2); }
        catch (Exception e) { U.Log("sfondo:", e.Message); Try(new object[] { "stop" }); }
    }
    /// Fine dell'intervallo a metà film: il film riparte dal punto in cui si era fermato.
    void EndSplit() { Try(new object[] { "show-text", "", 1 }); Load(idx, items[idx].Split); }
    void Gap(int i, double secs, string lab)
    {
        if (mode == "playing" || mode == "wait") Try(new object[] { "stop" });
        mode = "gap"; next = i; sel = i; until = U.Now + secs; label = lab;
        if (secs <= 0) Load(i);
    }
    void StopAll() { Try(new object[] { "stop" }); mode = "idle"; resume = null; Save(); }
    /// L'elemento i è finito (o saltato): passa al successivo con il nero tra i due.
    void Advance(int i)
    {
        int n = i + 1;
        if (n >= items.Count)
        {
            if (!(loop && auto && items.Count > 0)) { mode = "idle"; resume = null; sel = Math.Max(0, Math.Min(i, items.Count - 1)); Save(); return; }
            n = 0;
        }
        sel = n;
        if (!auto) { mode = "idle"; resume = null; Save(); return; }
        double secs = Post(i) + Pre(n);
        Gap(n, secs, secs > 0 ? L.Tr("Nero tra i film") : "");
    }
    void TickLoop()
    {
        Interlocked.Exchange(ref tickQueued, 0);
        try { Tick(); } catch (Exception e) { U.Log("errore tick:", e.Message); }
        tickCount++; if (tickCount % 4 == 0) { Publish(); RefreshPreview(); }
    }
    void Tick()
    {
        if (quitting) return;
        if (!mpv.Alive) { Respawn(L.Tr("processo terminato")); return; }
        double now = U.Now;
        if (mode == "gap" && now >= until) Load(next);
        else if (mode == "wait")
        {
            double left = until - now;
            if (left <= 0)
            {
                if (splitting) { EndSplit(); return; }
                if (items[idx].Kind == "pausa") Try(new object[] { "show-text", "", 1 });
                Advance(idx); return;
            }
            if ((int)left != shown && (items[idx].Kind == "pausa" || splitting))   // una volta al secondo: testo + tempo che manca, sullo schermo della sala
            {
                shown = (int)left; var txt = items[idx].Kind == "pausa" || items[idx].Text.Length > 0 ? items[idx].Text : L.Tr("Intervallo");
                Try(new object[] { "show-text", (txt.Length == 0 ? "" : txt + "\n") + $"{(int)left / 60}:{(int)left % 60:00}", 1500 });
            }
        }
        else if (mode == "playing")
        {
            Dictionary<string, JsonNode?> r;
            try { r = mpv.Get(new[] { "idle-active", "time-pos", "duration", "pause" }); fails = 0; }
            catch (Exception e) { fails++; U.Log(L.Tr("mpv non risponde"), fails, e.Message); if (fails >= 3) Respawn(L.Tr("non risponde")); return; }
            if (r.GetValueOrDefault("idle-active").B() == true)
            {
                if (now - loadedAt < 2) return;   // caricamento in corso
                if (dur > 0 && pos < dur - 5 && retries < 2)   // fine anomala: riprova dal punto in cui era
                {
                    retries++; var msg = L.Trf("interrotto a %ds, riprendo", (int)pos); U.Log(msg); Load(idx, Math.Max(0, pos - 1)); err = msg;
                }
                else
                {
                    if (dur == 0) err = L.Trf("%@: non riproducibile, passo oltre", items[idx].Title);
                    Advance(idx);
                }
            }
            else
            {
                if (r.GetValueOrDefault("time-pos").D() is double t && t != 0) pos = t;
                double sp = items[idx].Split;   // punto di divisione: il film si ferma e parte l'intervallo (secs/text del film)
                if (sp > 0 && items[idx].Kind == "film" && splitDone != idx && pos >= sp && pos < sp + 3 && !(r.GetValueOrDefault("pause").B() ?? false))
                {
                    splitDone = idx; StartBg(items[idx]); mode = "wait"; splitting = true; until = now + items[idx].Secs; shown = 0; return;
                }
                if (r.GetValueOrDefault("duration").D() is double d && d != 0) dur = d;
                if (langPending) ApplyLangs(items[idx]);
                if (now - lastSave > 2) { lastSave = now; resume = new JsonObject { ["idx"] = idx, ["pos"] = pos, ["t"] = now }; Save(); }
            }
        }
    }

    // MARK: lingue
    static readonly Dictionary<string, string[]> Alias = new()
    {
        ["fre"] = new[] { "fra", "fr" }, ["fra"] = new[] { "fre", "fr" }, ["ger"] = new[] { "deu", "de" }, ["deu"] = new[] { "ger", "de" },
        ["dut"] = new[] { "nld", "nl" }, ["chi"] = new[] { "zho", "zh" }, ["gre"] = new[] { "ell", "el" }
    };
    static bool LangMatch(string track, string want)
    {
        var t = track.ToLowerInvariant(); var w = want.ToLowerInvariant().Trim();
        if (t.Length == 0 || w.Length == 0) return false;
        if (t == w || t.StartsWith(w) || w.StartsWith(t)) return true;
        return (Alias.TryGetValue(t, out var a) && a.Contains(w)) || (Alias.TryGetValue(w, out var b) && b.Contains(t));
    }
    /// Modo sottotitoli effettivo di un film: il suo, altrimenti quello della scena (una lingua scritta sul film vale come "completi" se la scena dice "come nel file" o "nessuno").
    string SubMode(Item it)
    {
        if (it.Submode != null) return it.Submode;
        if (it.Slang.Length > 0 && (defsub == "file" || defsub == "none")) return "full";
        return defsub;
    }
    static bool IsForced(JsonObject t) =>
        (t.Get("forced").B() ?? false) || Regex.IsMatch(t.Get("title").S() ?? "", "forz|forced|signs|songs|cartell", RegexOptions.IgnoreCase);

    /// Sceglie audio e sottotitoli del film in corso secondo lingue e modo (come nel file / nessuno / solo forzati / completi).
    /// Tra tracce nella stessa lingua distingue le "forzate" (poche battute, solo dialoghi stranieri) da quelle complete.
    void ApplyLangs(Item it)
    {
        langTries++; if (langTries > 20) { langPending = false; return; }
        Dictionary<string, JsonNode?> r; try { r = mpv.Get(new[] { "track-list" }); } catch { return; }
        if (r.GetValueOrDefault("track-list") is not JsonArray tla) return;
        var tl = tla.OfType<JsonObject>().ToList();
        if (!tl.Any(t => t.Get("type").S() == "audio")) return;
        langPending = false;
        var cmds = new List<object[]>();
        var audios = tl.Where(t => t.Get("type").S() == "audio").ToList();
        var audioLang = audios.FirstOrDefault(a => a.Get("selected").B() ?? false)?.Get("lang").S() ?? "";
        var wantA = it.Alang.Split(',', StringSplitOptions.RemoveEmptyEntries);
        foreach (var w in wantA)
        {
            var hit = audios.FirstOrDefault(a => LangMatch(a.Get("lang").S() ?? "", w) && a.Get("id").I() != null);
            if (hit != null) { cmds.Add(new object[] { "set_property", "aid", hit.Get("id").I()! }); audioLang = hit.Get("lang").S() ?? audioLang; break; }
        }
        var mode_ = SubMode(it);
        var subs = tl.Where(t => t.Get("type").S() == "sub").ToList();
        var wantS = it.Slang.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList();
        if (wantS.Count == 0 && (mode_ == "forced" || mode_ == "full") && audioLang.Length > 0) wantS = new() { audioLang };
        int? Pick(List<JsonObject> pool)
        {
            foreach (var w in wantS) { var t = pool.FirstOrDefault(x => LangMatch(x.Get("lang").S() ?? "", w)); if (t?.Get("id").I() is int id) return id; }
            return null;
        }
        switch (mode_)
        {
            case "none": cmds.Add(new object[] { "set_property", "sid", "no" }); break;
            case "forced":
                {
                    var f = subs.Where(IsForced).ToList();
                    var id = Pick(f) ?? f.FirstOrDefault()?.Get("id").I();
                    if (id != null) { cmds.Add(new object[] { "set_property", "sid", id.Value }); cmds.Add(new object[] { "set_property", "sub-visibility", true }); }
                    else cmds.Add(new object[] { "set_property", "sid", "no" });
                    break;
                }
            case "full":
                {
                    var full = subs.Where(s => !IsForced(s)).ToList();
                    var id = Pick(full) ?? full.FirstOrDefault()?.Get("id").I();
                    if (id != null) { cmds.Add(new object[] { "set_property", "sid", id.Value }); cmds.Add(new object[] { "set_property", "sub-visibility", true }); }
                    else cmds.Add(new object[] { "set_property", "sid", "no" });
                    break;
                }
            default:   // "file": decide il file (traccia predefinita); se è scritta una lingua si prende la traccia completa in quella lingua
                {
                    int? id = null;
                    if (it.Slang.Length > 0) id = Pick(subs.Where(s => !IsForced(s)).ToList()) ?? Pick(subs);
                    if (id != null) { cmds.Add(new object[] { "set_property", "sid", id.Value }); cmds.Add(new object[] { "set_property", "sub-visibility", true }); }
                    else cmds.Add(new object[] { "set_property", "sid", "auto" });
                    break;
                }
        }
        if (cmds.Count > 0) Try(cmds.ToArray());
    }

    // MARK: comandi
    int Base() => (mode == "playing" || mode == "wait") ? idx : sel;
    void StartItem(int i) { if (i >= 0 && i < items.Count) Gap(i, Pre(i), Pre(i) > 0 ? L.Tr("Nero prima del film") : ""); }
    static double Num(JsonObject d, string k) => d.Get(k).D() ?? throw new DollyError(L.Trf("valore non valido per %@", k));
    static double? OptNum(JsonNode? v)
    {
        if (v == null || (v.S() == "")) return null;
        var x = v.D() ?? throw new DollyError(L.Tr("valore non valido"));
        return Math.Max(0, x);
    }
    void SetProp(string p, JsonNode? v)
    {
        if (!Model.Props.TryGetValue(p, out var k)) throw new DollyError(L.Trf("proprietà non ammessa: %@", p));
        object val;
        switch (k.kind)
        {
            case 'b': val = v.B() ?? false; break;
            case 'f': { var x = v.D() ?? throw new DollyError(L.Tr("numero non valido")); val = Math.Max(k.lo, Math.Min(k.hi, x)); break; }
            default: { var s = v.S() ?? ""; val = s.Length > 100 ? s.Substring(0, 100) : s; break; }
        }
        if (p.EndsWith("color") && !Regex.IsMatch((string)val, "^#([0-9A-Fa-f]{6}|[0-9A-Fa-f]{8})$")) throw new DollyError(L.Trf("colore non valido: %@", val));
        if (p == "sub-ass-override" && !new[] { "no", "yes", "scale", "force", "strip" }.Contains((string)val)) throw new DollyError(L.Trf("valore non valido: %@", val));
        if (p == "loop-file") { loopFile = (val as string) != "no"; mpv.Ipc(new object[][] { new object[] { "set_property", p, val } }, 2); return; }
        prefs[p] = U.J(val); mpv.Ipc(new object[][] { new object[] { "set_property", p, val } }, 2);
    }
    void RemoveItem(int i)
    {
        if (i < 0 || i >= items.Count) return;
        items.RemoveAt(i);
        if (idx == i && (mode == "playing" || mode == "wait")) StopAll(); else if (idx > i) idx--;
        if (mode == "gap" && next == i) StopAll(); else if (next > i) next--;
        sel = Math.Min(sel <= i ? sel : sel - 1, Math.Max(0, items.Count - 1));
    }
    void MoveItem(int i, int dlt)
    {
        int j = i + dlt;
        if (i < 0 || i >= items.Count || j < 0 || j >= items.Count) return;
        (items[i], items[j]) = (items[j], items[i]);
        int Fix(int v) => v == i ? j : v == j ? i : v;
        idx = Fix(idx); next = Fix(next); sel = Fix(sel);
    }
    void ReorderItem(int f, int t)   // trascinamento: l'elemento f va in posizione t
    {
        if (f < 0 || f >= items.Count || t < 0 || t >= items.Count || f == t) return;
        var it = items[f]; items.RemoveAt(f); items.Insert(t, it);
        int Fix(int k) => k == f ? t : (f < t && k > f && k <= t) ? k - 1 : (f > t && k >= t && k < f) ? k + 1 : k;
        idx = Fix(idx); next = Fix(next); sel = Fix(sel);
    }
    void ImportM3U(string path)
    {
        var baseDir = Path.GetDirectoryName(path) ?? "";
        string text = ""; try { text = File.ReadAllText(path); } catch { }
        var paths = text.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith('#'))
            .Select(l => Path.IsPathRooted(l) ? l : Path.Combine(baseDir, l)).ToList();
        if (mode != "idle") StopAll();
        name = Path.GetFileNameWithoutExtension(path); idx = -1; sel = 0;
        items = paths.SelectMany(Model.VideosIn).Select(p => new Item(p)).ToList();
    }
    void LoadList(string n)
    {
        if (U.ReadJson(PlPath(n)) is not JsonObject d) throw new DollyError(L.Tr("scaletta non trovata"));
        if (mode != "idle") StopAll();
        name = d.Get("name").S() ?? n; items = (d.Get("items") as JsonArray)?.OfType<JsonObject>().Select(o => new Item(o)).ToList() ?? new();
        defpre = d.Get("defpre").D() ?? 0; defpost = d.Get("defpost").D() ?? 0; auto = d.Get("auto").B() ?? true; loop = d.Get("loop").B() ?? false;
        defsub = SubModes.Contains(d.Get("defsub").S() ?? "") ? d.Get("defsub").S()! : "file"; idx = -1; sel = 0;
    }
    static string Cut(string? s, int n) { s ??= ""; return s.Length > n ? s.Substring(0, n) : s; }

    public void Act(JsonObject d)
    {
        var a = d.Get("a").S() ?? throw new DollyError(L.Tr("comando mancante"));
        switch (a)
        {
            case "play": StartItem((int)Num(d, "i")); break;
            case "toggle":   // play/pausa: se non sta suonando niente parte l'elemento selezionato
                if (mode == "playing") mpv.Ipc(new object[][] { new object[] { "cycle", "pause" } }, 2); else if (mode == "idle") StartItem(sel); break;
            case "skipgap":
                if (mode == "gap") Load(next);
                else if (mode == "wait") { if (splitting) EndSplit(); else { Try(new object[] { "show-text", "", 1 }); Advance(idx); } }
                break;
            case "extend": if (mode == "wait") until += d.Get("v").D() ?? 60; break;   // intervallo più lungo
            case "next": StartItem(Base() + 1); break;
            case "prev":   // va sempre all'elemento precedente; solo sul primo (non c'è un precedente) riporta il film all'inizio
                if (Base() == 0) { if (mode == "playing") mpv.Ipc(new object[][] { new object[] { "seek", 0, "absolute" } }, 2); else StartItem(0); } else StartItem(Base() - 1);
                break;
            case "stop": StopAll(); break;
            case "resume": if (resume != null && resume.Get("idx").I() is int ri && ri < items.Count) Load(ri, resume.Get("pos").D() ?? 0); break;
            case "seek": mpv.Ipc(new object[][] { new object[] { "seek", Num(d, "v"), d.Get("m").S() ?? "absolute" } }, 2); break;
            case "frame": mpv.Ipc(new object[][] { new object[] { "frame-step" } }, 2); break;
            case "frameback": mpv.Ipc(new object[][] { new object[] { "frame-back-step" } }, 2); break;
            case "abloop": mpv.Ipc(new object[][] { new object[] { "ab-loop" } }, 2); break;
            case "chapter": mpv.Ipc(new object[][] { new object[] { "set_property", "chapter", (int)Num(d, "v") } }, 2); break;
            case "aid": case "sid": case "vid":
                {
                    var v = d.Get("v").S() ?? "no";
                    object val = (v == "no" || v == "auto") ? v : (int.TryParse(v, out var n) ? n : 0);
                    mpv.Ipc(new object[][] { new object[] { "set_property", a, val } }, 2); break;
                }
            case "set": SetProp(d.Get("p").S() ?? "", d.Get("v")); break;
            case "resetsubstyle":   // torna allo stile iniziale e dimentica quello salvato
                foreach (var (p, v) in SubStyleDefaults) { prefs.Remove(p); mpv.Ipc(new object[][] { new object[] { "set_property", p, v } }, 2); }
                break;
            case "setmany": if (d.Get("props") is JsonObject pm) foreach (var kv in pm.ToList()) SetProp(kv.Key, kv.Value); break;
            case "text": mpv.Ipc(new object[][] { new object[] { "show-text", Cut(d.Get("v").S(), 200), d.Get("ms").I() ?? 5000 } }, 2); break;
            case "resetvideo":
                foreach (var p in new[] { "brightness", "contrast", "saturation", "gamma", "hue", "video-zoom", "video-pan-x", "video-pan-y", "panscan" }) SetProp(p, JsonValue.Create(0.0));
                SetProp("video-aspect-override", JsonValue.Create("-1"));
                break;
            case "defaults":
                if (d.Get("defpre").D() is double dp) defpre = Math.Max(0, dp);
                if (d.Get("defpost").D() is double dq) defpost = Math.Max(0, dq);
                if (d.Get("auto").B() is bool ab) auto = ab;
                if (d.Get("loop").B() is bool lb) loop = lb;
                if (d.Get("defsub").S() is string ds && SubModes.Contains(ds)) { defsub = ds; if (mode == "playing") { langPending = true; langTries = 0; } }   // vale subito sul film in corso
                break;
            case "add":   // "at": posizione di inserimento (trascinamento tra due righe); senza, in fondo
                {
                    var fresh = ((d.Get("paths") as JsonArray)?.Select(x => x.S() ?? "") ?? Enumerable.Empty<string>()).SelectMany(p => Model.VideosIn(ExpandHome(p))).Select(p => new Item(p)).ToList();
                    if (d.Get("at").I() is int at && at >= 0 && at <= items.Count && fresh.Count > 0)
                    {
                        items.InsertRange(at, fresh);
                        int Shift(int k) => k >= at ? k + fresh.Count : k;   // chi sta dopo il punto di inserimento scala in avanti
                        if (idx >= 0) idx = Shift(idx); if (next >= 0) next = Shift(next); sel = Shift(sel);
                    }
                    else items.AddRange(fresh);
                    break;
                }
            case "addblack": items.Add(new Item("", "nero", Math.Max(0.5, d.Get("secs").D() ?? 5))); break;
            case "addpause": items.Add(new Item("", "pausa", Math.Max(1, d.Get("secs").D() ?? 600), Cut(d.Get("text").S() ?? L.Tr("Intervallo"), 100))); break;
            case "addlib":
                {
                    var fs = Model.VideosIn(Folder);
                    if (d.Get("i").I() is int li) { if (li < 0 || li >= fs.Count) throw new DollyError(L.Tr("indice fuori range")); items.Add(new Item(fs[li])); }
                    else items.AddRange(fs.Select(p => new Item(p)));
                    break;
                }
            case "remove": RemoveItem(d.Get("i").I() ?? -1); break;
            case "move": MoveItem(d.Get("i").I() ?? -1, d.Get("d").I() ?? 0); break;
            case "reorder": ReorderItem(d.Get("from").I() ?? -1, d.Get("to").I() ?? -1); break;
            case "clear": if (mode != "idle") StopAll(); items = new(); sel = 0; idx = -1; break;
            case "setitem": SetItem(d); break;
            case "preset_save":   // salva lo sfondo/testo/durata dell'elemento i come preset (stesso nome = sostituisce)
                {
                    var i = d.Get("i").I(); var n = d.Get("name").S();
                    if (i == null || i < 0 || i >= items.Count || string.IsNullOrEmpty(n)) throw new DollyError(L.Tr("indice fuori range"));
                    var it = items[i.Value]; presets.RemoveAll(p => p.Get("name").S() == n);
                    presets.Add(new JsonObject { ["name"] = Cut(n, 60), ["bg"] = it.Bg, ["bgaudio"] = it.BgAudio, ["text"] = it.Text, ["secs"] = it.Secs }); WritePresets();
                    break;
                }
            case "preset_delete": presets.RemoveAll(p => p.Get("name").S() == d.Get("name").S()); WritePresets(); break;
            case "preset_apply":
                {
                    var i = d.Get("i").I(); var p = presets.FirstOrDefault(x => x.Get("name").S() == d.Get("name").S());
                    if (i == null || i < 0 || i >= items.Count || p == null) throw new DollyError(L.Tr("preset non trovato"));
                    var it = items[i.Value]; it.Bg = p.Get("bg").S() ?? ""; it.BgAudio = p.Get("bgaudio").B() ?? false; it.Text = p.Get("text").S() ?? ""; it.Secs = p.Get("secs").D() ?? it.Secs;
                    break;
                }
            case "rename": name = Cut(d.Get("v").S(), 60); break;
            case "pl_save":
                if (d.Get("name").S() is string pn && pn.Length > 0) name = Cut(pn, 60);
                U.WriteJson(PlPath(name), Dump()); break;
            case "pl_load": LoadList(d.Get("name").S() ?? ""); break;
            case "pl_delete": try { File.Delete(PlPath(d.Get("name").S() ?? "")); } catch { } break;
            case "pl_import": ImportM3U(d.Get("path").S() ?? ""); break;
        }
        Save();
    }
    void SetItem(JsonObject d)
    {
        var i0 = d.Get("i").I();
        if (i0 == null || i0 < 0 || i0 >= items.Count) throw new DollyError(L.Tr("indice fuori range"));
        int i = i0.Value; var it = items[i];
        if (d.Has("pre")) it.Pre = OptNum(d.Get("pre"));
        if (d.Has("post")) it.Post = OptNum(d.Get("post"));
        if (d.Has("vol")) it.Vol = OptNum(d.Get("vol"));
        if (d.Get("secs").D() is double sv) it.Secs = Math.Max(it.Kind == "nero" ? 0.5 : 1, sv);
        if (d.Get("bg").S() is string bg) it.Bg = bg;
        if (d.Get("bgaudio").B() is bool ba) it.BgAudio = ba;
        if (d.Get("split").D() is double sp) it.Split = Math.Max(0, sp);
        if (d.Has("loop")) it.Loop = Math.Max(-1, Math.Min(99, (int)(d.Get("loop").D() ?? 0)));
        if (d.Has("submode"))
        {
            var m = d.Get("submode").S() ?? ""; it.Submode = SubModes.Contains(m) ? m : null;
            if (i == idx && mode == "playing") { langPending = true; langTries = 0; }
        }
        if ((d.Has("alang") || d.Has("slang")) && i == idx && mode == "playing") { langPending = true; langTries = 0; }   // vale subito sul film in corso
        foreach (var k in new[] { "alang", "slang", "text" })
        {
            if (!d.Has(k) || d.Get(k) == null) continue;
            var s = Cut(d.Get(k).S() ?? "", 100);
            switch (k) { case "alang": it.Alang = s; break; case "slang": it.Slang = s; break; default: it.Text = s; break; }
        }
    }

    // MARK: stato e anteprima
    public JsonObject State()
    {
        double now = U.Now;
        var st = new JsonObject
        {
            ["ok"] = true, ["name"] = name, ["mode"] = mode, ["idx"] = idx, ["sel"] = sel, ["next"] = next, ["label"] = label,
            ["left"] = (mode == "gap" || mode == "wait") ? Math.Round(Math.Max(0, (until - now) * 10)) / 10 : 0.0, ["err"] = err, ["restarts"] = restarts,
            ["auto"] = auto, ["loop"] = loop, ["defsub"] = defsub, ["pid"] = mpv.Proc is { HasExited: false } p0 ? p0.Id : 0, ["defpre"] = defpre, ["defpost"] = defpost, ["resume"] = C(resume), ["folder"] = Folder,
            ["items"] = U.Arr(items.Select(i => (object?)new JsonObject
            {
                ["kind"] = i.Kind, ["pre"] = U.J(i.Pre), ["post"] = U.J(i.Post), ["vol"] = U.J(i.Vol), ["alang"] = i.Alang, ["slang"] = i.Slang,
                ["secs"] = i.Secs, ["text"] = i.Text, ["name"] = i.Title, ["ok"] = i.Kind != "film" || File.Exists(i.Path), ["loop"] = i.Loop, ["split"] = i.Split, ["bg"] = i.Bg, ["bgaudio"] = i.BgAudio, ["submode"] = U.J(i.Submode)
            })),
            ["presets"] = U.Arr(presets.Select(p => (object?)p)), ["lib"] = U.Arr(Model.VideosIn(Folder).Select(p => (object?)Path.GetFileName(p))), ["saved"] = U.Arr(SavedLists().Select(s => (object?)s)),
            ["playing"] = false, ["pause"] = false, ["time"] = 0.0, ["dur"] = 0.0, ["audio"] = new JsonArray(), ["sub"] = new JsonArray(), ["video"] = new JsonArray(), ["chapters"] = new JsonArray(),
            ["chapter"] = null, ["ab"] = new JsonArray(null, null), ["props"] = new JsonObject(), ["adevs"] = new JsonArray(), ["info"] = new JsonObject()
        };
        var extra = new[] { "pause", "time-pos", "duration", "track-list", "chapter-list", "chapter", "ab-loop-a", "ab-loop-b", "video-params", "container-fps", "video-codec",
                            "audio-codec-name", "frame-drop-count", "decoder-frame-drop-count", "path", "audio-device-list" };
        Dictionary<string, JsonNode?> r;
        try { r = mpv.Get(extra.Concat(Model.ReadProps)); }
        catch (Exception e) { st["ok"] = false; st["err"] = L.Trf("mpv non risponde: %@", e.Message); return st; }
        var tl = (r.GetValueOrDefault("track-list") as JsonArray)?.OfType<JsonObject>().ToList() ?? new();
        JsonArray Tr(string type)
        {
            var arr = new JsonArray();
            foreach (var x in tl.Where(t => t.Get("type").S() == type))
            {
                var parts = new List<string>();
                foreach (var k in new[] { "lang", "title", "codec" }) { var s = x.Get(k).S(); if (!string.IsNullOrEmpty(s)) parts.Add(s); }
                if (x.Get("demux-channel-count").I() is int n && n > 0) parts.Add($"{n}ch");
                int id = x.Get("id").I() ?? 0;
                arr.Add(new JsonObject { ["id"] = id, ["t"] = parts.Count == 0 ? L.Trf("traccia %d", id) : string.Join(" · ", parts), ["sel"] = x.Get("selected").B() ?? false });
            }
            return arr;
        }
        var vp = r.GetValueOrDefault("video-params") as JsonObject ?? new();
        var chs = new JsonArray(); int ci = 0;
        foreach (var c in (r.GetValueOrDefault("chapter-list") as JsonArray)?.OfType<JsonObject>() ?? Enumerable.Empty<JsonObject>())
        { chs.Add(new JsonObject { ["t"] = c.Get("title").S() ?? L.Trf("Capitolo %d", ci + 1), ["s"] = c.Get("time").D() ?? 0 }); ci++; }
        st["playing"] = r.ContainsKey("path"); st["pause"] = r.GetValueOrDefault("pause").B() ?? false; st["time"] = r.GetValueOrDefault("time-pos").D() ?? 0.0; st["dur"] = r.GetValueOrDefault("duration").D() ?? 0.0;
        st["audio"] = Tr("audio"); st["sub"] = Tr("sub"); st["video"] = Tr("video"); st["chapters"] = chs; st["chapter"] = C(r.GetValueOrDefault("chapter"));
        st["ab"] = new JsonArray(U.J(r.GetValueOrDefault("ab-loop-a").D()), U.J(r.GetValueOrDefault("ab-loop-b").D()));   // "no" quando non impostato
        var props = new JsonObject(); foreach (var k in Model.ReadProps) props[k] = C(r.GetValueOrDefault(k)); st["props"] = props;
        var adevs = new JsonArray();
        foreach (var ad in (r.GetValueOrDefault("audio-device-list") as JsonArray)?.OfType<JsonObject>() ?? Enumerable.Empty<JsonObject>())
            adevs.Add(new JsonObject { ["id"] = ad.Get("name").S() ?? "", ["t"] = ad.Get("description").S() ?? ad.Get("name").S() ?? "" });
        st["adevs"] = adevs;
        st["info"] = new JsonObject
        {
            ["res"] = vp.Count == 0 ? "" : $"{vp.Get("w").I() ?? 0}×{vp.Get("h").I() ?? 0}", ["fps"] = C(r.GetValueOrDefault("container-fps")), ["vcodec"] = C(r.GetValueOrDefault("video-codec")),
            ["acodec"] = C(r.GetValueOrDefault("audio-codec-name")), ["dropped"] = (r.GetValueOrDefault("frame-drop-count").I() ?? 0) + (r.GetValueOrDefault("decoder-frame-drop-count").I() ?? 0)
        };
        return st;
    }
    void Publish() { var s = State(); LastState = s; Snapshot?.Invoke(s); }
    void RefreshPreview()
    {
        if (!previewOn || mode != "playing") { if (pvData != null) { pvData = null; PreviewChanged?.Invoke(null); } return; }
        PreviewChanged?.Invoke(Preview());
    }
    public void SetPreview(bool on) => q.Async(() => previewOn = on);
    /// JPEG di ciò che si vede ora sullo schermo della sala (null se è nero). In cache per ~0,45 s.
    byte[]? Preview()
    {
        if (mode != "playing") return null;
        if (U.Now - pvT < 0.45) return pvData;
        var f = Path.Combine(Path.GetTempPath(), $"dolly-{Environment.ProcessId}-preview.jpg");
        try { mpv.Ipc(new object[][] { new object[] { "screenshot-to-file", f, "window" } }, 8); pvData = File.ReadAllBytes(f); } catch { pvData = null; }
        pvT = U.Now; return pvData;
    }

    // MARK: ingressi pubblici (da qualunque thread)
    int pubPending;
    void SchedulePublish()   // trascinando uno slider arrivano decine di comandi: l'istantanea si aggiorna una volta ogni ~0,12 s
    {
        if (Interlocked.Exchange(ref pubPending, 1) == 1) return;
        q.After(120, () => { Interlocked.Exchange(ref pubPending, 0); Publish(); });
    }
    public void Send(JsonObject d, Action<string?>? done = null)
    {
        q.Async(() =>
        {
            string? e = null;
            try { Act(d); } catch (Exception ex) { e = ex.Message; U.Log(L.Tr("errore comando"), d.ToJsonString(), ex.Message); }
            SchedulePublish();
            done?.Invoke(e);
        });
    }
    public string? ActSync(JsonObject d) => q.Sync(() => { try { Act(d); return (string?)null; } catch (Exception ex) { U.Log(L.Tr("errore comando"), d.ToJsonString(), ex.Message); return ex.Message; } });
    public string StateSync() => q.Sync(() => State().ToJsonString());
    public JsonObject StateObjSync() => q.Sync(() => State());
    public byte[]? PreviewSync() => q.Sync(() => Preview());
    public void Shutdown()
    {
        q.Sync(() => { quitting = true; timer?.Dispose(); mpv.Kill(); try { caff?.Kill(); } catch { } if (OperatingSystem.IsWindows()) Win32.KeepAwake(false); });
    }
}
