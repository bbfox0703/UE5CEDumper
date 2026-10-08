using UE5DumpUI.Models;

namespace UE5DumpUI.Helpers;

/// <summary>
/// [LIVEFUNCS-STEP2] Functions followed by name across fetches: Live Funcs' trace ticks, and what it chooses for the
/// snapshot buffer. One store for every such list because the refresh rules are subtle and must not be written twice.
///
/// <para>A name is Class::Func, the row's two strings: a class is named by its short name, so two classes in different
/// folders share one name and are followed together -- what the DLL matches by name does too. Under each name the store
/// keeps what the DLL is sent: the LIVE addresses the last fetch showed (for a DLL that predates names; a dead address
/// is never sent), every name key a row carried (the name pool's ints, stable for the connection: one string can come
/// from two int pairs), and the largest parameter block seen, which sizes a snapshot ring.</para>
///
/// <para>A fetch refreshes a name it shows: its addresses become that page's live ones (an unloaded row adds none),
/// its keys only grow. A name the page does not show keeps everything -- a fetch limit cut it, not the game. A
/// disconnect clears the store: the keys belong to the process that is gone.</para>
/// </summary>
public sealed class FunctionTickSet
{
    private sealed class Entry
    {
        public string ClassName = "";
        public string FuncName = "";
        public HashSet<string> Addrs = new(StringComparer.OrdinalIgnoreCase);
        public readonly HashSet<NameKey> Keys = new();
        public int ParmsSize;
    }

    private readonly Dictionary<string, Entry> _names = new(StringComparer.Ordinal);

    public static string NameOf(PeProfileEntry e) => $"{e.ClassName}::{e.FuncName}";

    public int Count => _names.Count;
    public bool Contains(string name) => _names.ContainsKey(name);
    public bool Contains(PeProfileEntry row) => _names.ContainsKey(NameOf(row));

    /// <summary>The names, sorted, for the panel's list and the Call Trace tab's copy.</summary>
    public IReadOnlyList<string> Names => _names.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList();

    /// <summary>Follow or stop following <paramref name="row"/>'s name, taking what every same-named row of
    /// <paramref name="page"/> carries. Returns whether the name is followed now.</summary>
    public bool Toggle(PeProfileEntry row, IEnumerable<PeProfileEntry> page)
    {
        string name = NameOf(row);
        if (_names.Remove(name)) return false;
        var e = new Entry { ClassName = row.ClassName, FuncName = row.FuncName };
        Take(e, row, e.Addrs);
        foreach (var r in page) if (NameOf(r) == name) Take(e, r, e.Addrs);
        _names[name] = e;
        return true;
    }

    /// <summary>Follow <paramref name="row"/>'s name if it is not followed yet (a bulk choice).</summary>
    public void Add(PeProfileEntry row, IEnumerable<PeProfileEntry> page)
    {
        if (!_names.ContainsKey(NameOf(row))) Toggle(row, page);
    }

    public void Clear() => _names.Clear();

    /// <summary>A fetch's rows: every followed name the page shows takes its live addresses from it.</summary>
    public void Refresh(IEnumerable<PeProfileEntry> page)
    {
        var shown = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var r in page)
        {
            string name = NameOf(r);
            if (!_names.TryGetValue(name, out var e)) continue;
            if (!shown.TryGetValue(name, out var addrs))
                shown[name] = addrs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Take(e, r, addrs);
        }
        foreach (var kv in shown) _names[kv.Key].Addrs = kv.Value;
    }

    private static void Take(Entry e, PeProfileEntry r, HashSet<string> addrs)
    {
        if (!r.IsUnloaded && !string.IsNullOrEmpty(r.FuncAddr)) addrs.Add(r.FuncAddr);
        if (r.FnameKey is { } key) e.Keys.Add(key);
        e.ParmsSize = Math.Max(e.ParmsSize, r.ParmsSize);
    }

    /// <summary>Every live address of every name, for a DLL that predates names.</summary>
    public IReadOnlyList<string> LiveAddresses()
        => _names.Values.SelectMany(e => e.Addrs).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>The names that carry a key, as the DLL takes them by name.</summary>
    public IReadOnlyList<NamedFunction> Named()
        => _names.OrderBy(kv => kv.Key, StringComparer.Ordinal).Where(kv => kv.Value.Keys.Count > 0)
                 .Select(kv => new NamedFunction
                 {
                     ClassName = kv.Value.ClassName,
                     FuncName = kv.Value.FuncName,
                     Keys = kv.Value.Keys.OrderBy(k => k.FnIdx).ThenBy(k => k.FnNum).ThenBy(k => k.ClsIdx)
                                        .ThenBy(k => k.ClsNum).ToList(),
                     ParmsSize = kv.Value.ParmsSize,
                 }).ToList();

    /// <summary>Followed names with no key (from a DLL that predates them): only their live addresses can be sent.</summary>
    public int KeylessCount => _names.Values.Count(e => e.Keys.Count == 0);

    /// <summary>The live addresses a name holds now; empty when every row of it is unloaded.</summary>
    public IReadOnlyCollection<string> AddressesOf(string name)
        => _names.TryGetValue(name, out var e) ? e.Addrs : Array.Empty<string>();

    public int ParmsSizeOf(string name) => _names.TryGetValue(name, out var e) ? e.ParmsSize : 0;
}
