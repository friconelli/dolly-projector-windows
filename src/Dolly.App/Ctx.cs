using System.Text.Json.Nodes;
using System.Windows;
using Microsoft.Win32;
using Dolly;

namespace Dolly.App;

/// Stato condiviso dai pannelli: il motore, l'ultima istantanea e l'invio dei comandi.
public sealed class Ctx
{
    public Engine? E;
    public Snap S = new();
    public Window? Owner;
    public Action<string>? ShowError_;

    public void Cmd(string a, params (string k, object? v)[] kv)
    {
        if (E == null) return;
        var o = new JsonObject { ["a"] = a };
        foreach (var (k, v) in kv) o[k] = U.J(v);
        E.Send(o);
    }
    public void Set(string prop, object? v) => Cmd("set", ("p", prop), ("v", v));
    /// Un campo di un elemento; se l'elemento fa parte di una selezione di più, vale per tutti i selezionati.
    public void SetItem(int i, string key, object? v)
    {
        var targets = Selection.Contains(i) && Selection.Count > 1 ? Selection.OrderBy(x => x).ToList() : new List<int> { i };
        foreach (var k in targets) Cmd("setitem", ("i", k), (key, v));
    }

    // MARK: selezione nella scaletta (thread dell'interfaccia)
    public HashSet<int> Selection { get; private set; } = new();
    int? anchor;
    public event Action? SelectionChanged;
    public void SetSelection(IEnumerable<int> s) { Selection = s.ToHashSet(); SelectionChanged?.Invoke(); }
    /// Clic su una riga: semplice = solo quella; Ctrl = aggiunge/toglie; Maiusc = intervallo dall'ultima scelta (con Ctrl si aggiunge all'esistente).
    public void TapSelect(int i, bool ctrl, bool shift)
    {
        if (shift && anchor is int a) { var r = Enumerable.Range(Math.Min(a, i), Math.Abs(a - i) + 1); SetSelection(ctrl ? Selection.Union(r) : r); }
        else if (ctrl) { var s = Selection.ToHashSet(); if (!s.Remove(i)) s.Add(i); anchor = i; SetSelection(s); }
        else { anchor = i; SetSelection(new[] { i }); }
    }
    public void SelectAll() { anchor = 0; SetSelection(Enumerable.Range(0, S.Items.Count)); }
    /// Toglie dalla scaletta gli elementi selezionati (dopo conferma).
    public void RemoveSelected()
    {
        var t = Selection.OrderBy(x => x).ToList(); if (t.Count == 0) return;
        if (!Confirm(t.Count == 1 ? L.Tr("Togliere dalla scaletta?") : L.Trf("Togliere %d elementi dalla scaletta?", t.Count))) return;
        SetSelection(Array.Empty<int>()); Cmd("removemany", ("indices", U.Arr(t.Cast<object?>())));
    }
    /// Sposta di una posizione ogni elemento selezionato (restano distanziati come sono; quelli già in cima/in fondo restano fermi).
    public void MoveSelection(int dlt)
    {
        int n = S.Items.Count; var now = new HashSet<int>();
        foreach (var i in dlt < 0 ? Selection.OrderBy(x => x) : Selection.OrderByDescending(x => x))
        {
            if (i < 0 || i >= n) continue; int t = i + dlt;
            if (t >= 0 && t < n && !now.Contains(t)) { Cmd("move", ("i", i), ("d", dlt)); now.Add(t); } else now.Add(i);
        }
        SetSelection(now);
    }

    public bool Confirm(string msg, string? ok = null)
        => MessageBox.Show(Owner!, msg, "Dolly Projector", MessageBoxButton.OKCancel, MessageBoxImage.Question) == MessageBoxResult.OK;
    public void Confirm(string msg, Action yes) { if (Confirm(msg)) yes(); }
    public void Error(string msg) => MessageBox.Show(Owner!, msg, L.Tr("Errore"), MessageBoxButton.OK, MessageBoxImage.Warning);

    public string[] PickFiles(string prompt, string filter, bool multiple = true)
    {
        var d = new OpenFileDialog { Title = prompt, Filter = filter, Multiselect = multiple };
        return d.ShowDialog(Owner) == true ? d.FileNames : Array.Empty<string>();
    }
    public string? PickFolder(string prompt, string? start = null)
    {
        var d = new OpenFolderDialog { Title = prompt }; if (start != null && Directory.Exists(start)) d.InitialDirectory = start;
        return d.ShowDialog(Owner) == true ? d.FolderName : null;
    }
}

/// ComboBox che manda il comando solo quando è l'utente a cambiare la scelta (non quando l'istantanea aggiorna l'elenco).
public sealed class Pick : System.Windows.Controls.ComboBox
{
    bool quiet; string sig = "";
    readonly Action<int> onPick; List<int> ids = new();
    public Pick(Action<int> onPick) { this.onPick = onPick; MinHeight = 28; HorizontalAlignment = HorizontalAlignment.Stretch; SelectionChanged += (_, _) => { if (!quiet && SelectedIndex >= 0 && SelectedIndex < ids.Count) this.onPick(ids[SelectedIndex]); }; }
    /// items: (valore, testo). selected: il valore da mostrare come scelto.
    public void Fill(IEnumerable<(int id, string text)> items, int selected)
    {
        var list = items.ToList(); var s = string.Join("\u0001", list.Select(x => x.id + ":" + x.text));
        quiet = true;
        if (s != sig) { sig = s; Items.Clear(); ids = list.Select(x => x.id).ToList(); foreach (var x in list) Items.Add(x.text); }
        var ix = ids.IndexOf(selected); if (SelectedIndex != ix) SelectedIndex = ix;
        quiet = false;
    }
}
