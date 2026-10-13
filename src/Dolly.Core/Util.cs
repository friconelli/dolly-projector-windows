using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Dolly;

public class DollyError : Exception { public DollyError(string m) : base(m) { } }

public static class U
{
    public static void Log(params object?[] a) =>
        Console.Error.WriteLine(DateTime.Now.ToString("HH:mm:ss") + " " + string.Join(" ", a.Select(x => x?.ToString() ?? "null")));

    public static double Now => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;

    // Lettura "tollerante" dei valori che arrivano da mpv / dal telecomando (numeri, stringhe, booleani…), come asDouble/asBool/asString della versione Mac.
    public static double? D(this JsonNode? n)
    {
        if (n is not JsonValue) return null;
        switch (n.GetValueKind())
        {
            case JsonValueKind.Number: return double.Parse(n.ToJsonString(), CultureInfo.InvariantCulture);
            case JsonValueKind.String: return double.TryParse(n.GetValue<string>(), NumberStyles.Float, CultureInfo.InvariantCulture, out var x) ? x : null;
            default: return null;
        }
    }
    public static int? I(this JsonNode? n) => n.D() is double d ? (int)d : null;
    public static bool? B(this JsonNode? n)
    {
        if (n is not JsonValue) return null;
        switch (n.GetValueKind())
        {
            case JsonValueKind.True: return true;
            case JsonValueKind.False: return false;
            case JsonValueKind.Number: return n.D() != 0;
            default: return null;
        }
    }
    public static string? S(this JsonNode? n)
    {
        if (n is not JsonValue) return null;
        switch (n.GetValueKind())
        {
            case JsonValueKind.String: return n.GetValue<string>();
            case JsonValueKind.Number: return n.ToJsonString();
            default: return null;
        }
    }
    public static bool Has(this JsonObject o, string k) => o.ContainsKey(k);
    public static JsonNode? Get(this JsonObject o, string k) => o.TryGetPropertyValue(k, out var v) ? v : null;
    public static JsonNode? J(object? v) => v switch
    {
        null => null,
        JsonNode n => n.DeepClone(),
        string s => JsonValue.Create(s),
        bool b => JsonValue.Create(b),
        int i => JsonValue.Create(i),
        double d => JsonValue.Create(d),
        long l => JsonValue.Create(l),
        _ => JsonSerializer.SerializeToNode(v)
    };
    public static JsonArray Arr(IEnumerable<object?> xs) { var a = new JsonArray(); foreach (var x in xs) a.Add(J(x)); return a; }

    public static string Fmt(double s)
    {
        int t = Math.Max(0, (int)Math.Round(s, MidpointRounding.AwayFromZero)); int h = t / 3600, m = t % 3600 / 60, sec = t % 60;
        return h > 0 ? $"{h}:{m:00}:{sec:00}" : $"{m}:{sec:00}";
    }

    public static void WriteJson(string path, JsonNode obj)
    {
        try
        {
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, obj.ToJsonString());
            File.Move(tmp, path, true);
        }
        catch (Exception e) { Log("scrittura non riuscita:", path, e.Message); }
    }
    public static JsonNode? ReadJson(string path)
    {
        try { return JsonNode.Parse(File.ReadAllText(path)); } catch { return null; }
    }
}

/// Coda seriale con un solo thread (come la DispatchQueue della versione Mac): tutto lo stato del motore vive qui.
public sealed class SerialQueue
{
    readonly System.Collections.Concurrent.BlockingCollection<Action> q = new();
    readonly Thread th;
    public SerialQueue() { th = new Thread(Loop) { IsBackground = true, Name = "dolly.engine" }; th.Start(); }
    void Loop() { foreach (var a in q.GetConsumingEnumerable()) { try { a(); } catch (Exception e) { U.Log("errore nella coda:", e); } } }
    public void Async(Action a) { if (!q.IsAddingCompleted) q.Add(a); }
    public void After(int ms, Action a) { Task.Delay(ms).ContinueWith(_ => Async(a)); }
    public T Sync<T>(Func<T> f)
    {
        if (Thread.CurrentThread == th) return f();
        T res = default!; Exception? err = null; using var done = new ManualResetEventSlim();
        Async(() => { try { res = f(); } catch (Exception e) { err = e; } finally { done.Set(); } });
        done.Wait();
        if (err != null) throw err;
        return res;
    }
    public void Sync(Action a) => Sync<int>(() => { a(); return 0; });
    public void Stop() => q.CompleteAdding();
}
