using System.Globalization;

namespace UE5DumpUI.Services;

/// <summary>
/// The C++ name of every TYPE the SDK header spells — a struct's own name, its super, and every
/// pointee / struct / enum a member names — so a definition and every reference to it agree.
///
/// <para>They were emitted verbatim. Measured on a real whole-pool export (DumperTest 5.4, 7,894
/// structs): every AnimBlueprint carries its own <c>AnimBlueprintGeneratedConstantData</c>, so two
/// AnimBPs define one struct twice (C2011), and a child AnimBP's came out as
/// <c>struct X : public X</c>. <c>NameTypes.h</c>'s <c>INVALID_OBJECTNAME_CHARACTERS</c> allows
/// <c>-</c>, so an asset name can carry one into a class name; and a pool type named like a type
/// this header spells itself (<c>TArray</c>) collides with it (C2990).</para>
///
/// <para><b>Uniqueness</b>: a name held by more than one type is qualified with the holder's outers,
/// nearest first — <c>AnimBlueprintGeneratedConstantData_ABP_Quinn_C</c>. Every decision depends on
/// the set of types, never on GObjects order: a group of holders moves out a level together until its
/// names differ from each other, from every other group's, and from every type's own name. The
/// single native (<c>/Script</c>) holder, else the single holder whose UE name needed no sanitising,
/// keeps the plain name — unless a type OUTSIDE the pool (a super the export does not define) is
/// spelled that way. Dumper-7 puts such types in package namespaces; this header is flat, so the
/// package goes into the name.</para>
///
/// <para><b>References</b>: the super is found by <c>SuperAddress</c>, and is never the struct
/// itself. A member names only a short type name on the wire, so among several holders the one of
/// the right kind (a pointer names a class, a by-value member a struct) whose path shares the most
/// with the referring type wins, ties to the lower path — a heuristic, and the only one the wire
/// allows. With no holder of the right kind, or none at all, the name is sanitised only.</para>
/// </summary>
internal sealed class SdkTypeNames
{
    /// <summary>One type the header defines. <see cref="IsClass"/> is null when the caller does not know.</summary>
    internal readonly record struct Entry(string Address, string Name, bool? IsClass, string FullPath);

    /// <summary>What a reference names: a pointer or subclass names a class, a by-value member a struct.</summary>
    internal enum Kind { Any, Class, Struct, Enum }

    /// <summary>
    /// The type spellings this header writes itself; a pool type of the same name would redefine
    /// one. <c>SdkTypeNameTests</c> derives them from <c>MapCppDecl</c> so a new spelling cannot slip
    /// past this list.
    /// </summary>
    internal static readonly IReadOnlySet<string> BuiltIns = new HashSet<string>(StringComparer.Ordinal)
    {
        "FName", "FString", "FUtf8String", "FAnsiString", "FText", "FFieldPath",
        "FScriptDelegate", "FMulticastScriptDelegate", "FMulticastInlineDelegate", "FMulticastSparseDelegate",
        "TArray", "TMap", "TSet", "TSubclassOf", "TWeakObjectPtr", "TSoftObjectPtr", "TSoftClassPtr",
        "TLazyObjectPtr", "TScriptInterface", "UObject", "UClass", "IInterface",
    };

    /// <summary>No pool: every reference is only sanitised.</summary>
    internal static readonly SdkTypeNames None = new(Array.Empty<Entry>());

    private readonly Entry[] _entries;
    private readonly string[] _names;
    private readonly string[][] _pathTokens;
    private readonly HashSet<string> _outside;
    private readonly Dictionary<string, int> _byAddress = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<int>> _byName = new(StringComparer.Ordinal);

    /// <summary>
    /// A pool of one: a single exported struct. Its super is outside the pool — the type the user
    /// already has under that name — so if the two share a spelling, the exported struct is the one
    /// renamed.
    /// </summary>
    internal static SdkTypeNames Single(string name, string? fullPath, string? superName) =>
        new(new[] { new Entry("", name ?? "", null, fullPath ?? "") },
            string.IsNullOrEmpty(superName) ? null : new[] { superName });

    /// <param name="entries">The types the header defines.</param>
    /// <param name="outsideNames">UE names of types the header references but does not define (a super
    /// missing from the pool). No entry may be spelled like one.</param>
    internal SdkTypeNames(IReadOnlyList<Entry> entries, IEnumerable<string>? outsideNames = null)
    {
        _entries = entries.ToArray();
        _names = new string[_entries.Length];
        _pathTokens = _entries.Select(e => PathTokens(e.FullPath)).ToArray();
        _outside = new HashSet<string>(
            (outsideNames ?? Array.Empty<string>()).Where(n => !string.IsNullOrEmpty(n)).Select(Sanitised),
            StringComparer.Ordinal);

        for (int i = 0; i < _entries.Length; i++)
        {
            if (!string.IsNullOrEmpty(_entries[i].Address)) _byAddress.TryAdd(_entries[i].Address, i);
            var raw = _entries[i].Name ?? "";
            if (!_byName.TryGetValue(raw, out var list)) _byName[raw] = list = new List<int>();
            list.Add(i);
        }
        Assign();
    }

    /// <summary>The emitted name of the i-th entry.</summary>
    internal string NameAt(int index) => _names[index];

    /// <summary>
    /// The emitted super name of entry <paramref name="self"/>: the pool entry at
    /// <paramref name="superAddress"/> when there is one, else a by-name reference of the same kind.
    /// Never the struct itself — a type cannot inherit from itself, so a same-named super is another
    /// type even when the pool cannot say which.
    /// </summary>
    internal string Super(int self, string? superAddress, string superName)
    {
        if (!string.IsNullOrEmpty(superAddress) && _byAddress.TryGetValue(superAddress, out int i) && i != self)
            return _names[i];

        var kind = _entries[self].IsClass switch { true => Kind.Class, false => Kind.Struct, null => Kind.Any };
        var name = Reference(superName, kind, _entries[self].FullPath, exclude: self);

        // Only reachable when the super is outside the pool, the caller did not declare it so, and
        // it sanitises to this struct's own name: spell it apart rather than inherit from itself.
        for (int n = 0; name == _names[self]; n++)
            name = Sanitised(superName) + "_" + n.ToString(CultureInfo.InvariantCulture);
        return name;
    }

    /// <summary>The emitted name for a type a member (or a super) names by its short UE name.</summary>
    internal string Reference(string raw, Kind kind, string? fromPath) => Reference(raw, kind, fromPath, -1);

    private string Reference(string raw, Kind kind, string? fromPath, int exclude)
    {
        if (string.IsNullOrEmpty(raw)) return raw;

        // The pool holds classes and structs only; an enum of the same name is a different type.
        if (kind != Kind.Enum && _byName.TryGetValue(raw, out var holders))
        {
            // A holder whose kind is unknown (a pool of one) fits either way. One whose kind is KNOWN
            // to be the other never fits: a by-value member cannot name a class, nor a pointer a struct.
            var fitting = holders.Where(h => h != exclude && kind switch
            {
                Kind.Class => _entries[h].IsClass != false,
                Kind.Struct => _entries[h].IsClass != true,
                _ => true,
            }).ToList();

            if (fitting.Count == 1) return _names[fitting[0]];
            if (fitting.Count > 1)
            {
                var from = PathTokens(fromPath);
                int best = -1, bestScore = -1;
                foreach (int h in fitting)
                {
                    int score = CommonPrefix(_pathTokens[h], from) * 2 + (IsNative(h) ? 1 : 0);
                    if (score > bestScore || (score == bestScore && Before(h, best)))
                    {
                        best = h;
                        bestScore = score;
                    }
                }
                return _names[best];
            }
        }

        return Sanitised(raw);
    }

    // ------------------------------------------------------------------

    /// <summary>A name that is not a pool type: sanitised, and moved off a keyword.</summary>
    private static string Sanitised(string raw)
    {
        var id = SdkMemberNames.MakeIdentifier(raw);
        return SdkMemberNames.IsReservedIdentifier(id) ? id + "_0" : id;
    }

    private static bool Reserved(string name) =>
        SdkMemberNames.IsReservedIdentifier(name) || BuiltIns.Contains(name);

    private bool IsNative(int i) => _pathTokens[i].Length > 0 && _pathTokens[i][0] == "Script";

    /// <summary>An order that does not depend on GObjects order: the path, then the emitted name.</summary>
    private bool Before(int a, int b)
    {
        int c = string.CompareOrdinal(_entries[a].FullPath ?? "", _entries[b].FullPath ?? "");
        if (c != 0) return c < 0;
        c = string.CompareOrdinal(_names[a], _names[b]);
        return c != 0 ? c < 0 : a < b;
    }

    private void Assign()
    {
        var baseNames = _entries.Select(e => SdkMemberNames.MakeIdentifier(e.Name ?? "")).ToArray();
        var groups = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        foreach (var (name, i) in baseNames.Select((n, i) => (n, i)))
        {
            if (!groups.TryGetValue(name, out var g)) groups[name] = g = new List<int>();
            g.Add(i);
        }

        // Every type's own name, and every outside type's, is off limits to a qualified name.
        var forbidden = new HashSet<string>(baseNames, StringComparer.Ordinal);
        forbidden.UnionWith(_outside);
        var taken = new HashSet<string>(StringComparer.Ordinal);

        // Pass 1: the holder that keeps the plain name, if any.
        var toQualify = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        foreach (var (name, group) in groups)
        {
            int keeper = -1;
            if (!Reserved(name) && !_outside.Contains(name))
            {
                if (group.Count == 1)
                {
                    keeper = group[0];
                }
                else
                {
                    var clean = group.Where(h => (_entries[h].Name ?? "") == name).ToList();
                    var native = clean.Where(IsNative).ToList();
                    if (native.Count == 1) keeper = native[0];
                    else if (clean.Count == 1) keeper = clean[0];
                }
            }
            if (keeper >= 0)
            {
                _names[keeper] = name;
                taken.Add(name);
            }
            var rest = group.Where(h => h != keeper).ToList();
            if (rest.Count > 0) toQualify[name] = rest;
        }

        // Pass 2: every other holder is qualified with its outers. All groups step out together: a
        // group moves one level out while its names collide with each other, with another group's,
        // or with a forbidden name -- so no group wins a name by being met first.
        var usable = new Dictionary<int, List<string>>();
        var depth = new Dictionary<string, int>(StringComparer.Ordinal);
        var exhausted = new List<string>();
        foreach (var (name, group) in toQualify)
        {
            var lists = group.Select(h => Qualifiers(h, name, skipSelfNamed: true)).ToList();
            // Dropping a self-named outer must not erase the only difference between two paths.
            if (HasDuplicateList(lists))
                lists = group.Select(h => Qualifiers(h, name, skipSelfNamed: false)).ToList();
            for (int k = 0; k < group.Count; k++) usable[group[k]] = lists[k];

            if (lists.Any(l => l.Count == 0)) exhausted.Add(name);   // a failed walk has no path
            else depth[name] = 1;
        }

        while (depth.Count > 0)
        {
            var candidates = depth.ToDictionary(
                g => g.Key,
                g => toQualify[g.Key].Select(h => Qualified(g.Key, usable[h], g.Value)).ToList(),
                StringComparer.Ordinal);
            var uses = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var c in candidates.Values.SelectMany(l => l))
                uses[c] = uses.TryGetValue(c, out int u) ? u + 1 : 1;

            var bad = candidates
                .Where(g => g.Value.Any(c => uses[c] > 1 || forbidden.Contains(c) || Reserved(c)))
                .Select(g => g.Key).ToList();
            if (bad.Count == 0)
            {
                foreach (var (name, names) in candidates)
                {
                    var group = toQualify[name];
                    for (int k = 0; k < group.Count; k++) _names[group[k]] = names[k];
                    taken.UnionWith(names);
                }
                break;
            }
            foreach (var name in bad)
            {
                int next = depth[name] + 1;
                if (next > toQualify[name].Max(h => usable[h].Count))
                {
                    depth.Remove(name);
                    exhausted.Add(name);
                }
                else
                {
                    depth[name] = next;
                }
            }
        }

        // What no path tells apart is numbered -- in path order, never GObjects order.
        foreach (var name in exhausted.OrderBy(n => n, StringComparer.Ordinal))
        {
            var group = toQualify[name];
            int maxDepth = group.Max(h => usable[h].Count);
            var ordered = group.ToList();
            ordered.Sort((a, b) =>
            {
                int c = string.CompareOrdinal(_entries[a].FullPath ?? "", _entries[b].FullPath ?? "");
                return c != 0 ? c : a.CompareTo(b);
            });
            foreach (int h in ordered)
            {
                var stem = usable[h].Count > 0 ? Qualified(name, usable[h], maxDepth) : name;
                var candidate = stem;
                for (int n = 0; taken.Contains(candidate) || (forbidden.Contains(candidate) && candidate != name)
                                || _outside.Contains(candidate) || Reserved(candidate); n++)
                    candidate = stem + "_" + n.ToString(CultureInfo.InvariantCulture);
                _names[h] = candidate;
                taken.Add(candidate);
            }
        }
    }

    private static bool HasDuplicateList(List<List<string>> lists)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var l in lists)
            if (!seen.Add(string.Join("\u0001", l))) return true;
        return false;
    }

    /// <summary>
    /// The holder's outers, outermost first, as identifiers. With <paramref name="skipSelfNamed"/>,
    /// without the ones that only repeat the type's own name (a Blueprint's package <c>BP_Door</c>
    /// beside its class <c>BP_Door_C</c>, a user-defined struct's package beside the struct).
    /// </summary>
    private List<string> Qualifiers(int h, string baseName, bool skipSelfNamed)
    {
        var tokens = _pathTokens[h];
        var result = new List<string>();
        for (int i = 0; i < tokens.Length - 1; i++)
        {
            var t = SdkMemberNames.MakeIdentifier(tokens[i]);
            if (skipSelfNamed && (t == baseName || t + "_C" == baseName || t == baseName + "_C")) continue;
            result.Add(t);
        }
        return result;
    }

    private static string Qualified(string baseName, List<string> qualifiers, int depth)
    {
        int take = Math.Min(depth, qualifiers.Count);
        return baseName + "_" + string.Join("_", qualifiers.Skip(qualifiers.Count - take));
    }

    /// <summary><c>Ubel::GetFullName</c> joins outers with '/' and the last hop with '.'; a
    /// subobject path uses ':'. Tokens are the names between them.</summary>
    private static string[] PathTokens(string? path) =>
        string.IsNullOrEmpty(path)
            ? Array.Empty<string>()
            : path.Split(new[] { '/', '.', ':' }, StringSplitOptions.RemoveEmptyEntries);

    private static int CommonPrefix(string[] a, string[] b)
    {
        int n = 0;
        while (n < a.Length && n < b.Length && string.Equals(a[n], b[n], StringComparison.Ordinal)) n++;
        return n;
    }
}
