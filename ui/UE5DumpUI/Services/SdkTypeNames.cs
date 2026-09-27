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
/// <para><b>Uniqueness</b> (whole pool only): a name held by more than one type is qualified with the
/// holder's outers, nearest first — <c>AnimBlueprintGeneratedConstantData_ABP_Quinn_C</c> — at the
/// smallest depth that tells every holder apart, so the result does not depend on GObjects order.
/// The one exception keeps the plain name: the single native (<c>/Script</c>) holder, else the single
/// holder whose UE name needed no sanitising. Dumper-7 puts such types in package namespaces; this
/// header is flat, so the package goes into the name.</para>
///
/// <para><b>References</b>: the super is found by <c>SuperAddress</c>, exactly. A member names only
/// a short type name, so among several holders the one of the right kind (a pointer names a class,
/// a by-value member a struct) whose path shares the most with the referring type's wins — a
/// heuristic, and the only one the wire allows. A name outside the pool is sanitised only.</para>
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
    private readonly Dictionary<string, int> _byAddress = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<int>> _byName = new(StringComparer.Ordinal);

    /// <summary>A pool of one: a single exported struct, so references to its own name follow it.</summary>
    internal static SdkTypeNames Single(string name, string? fullPath) =>
        new(new[] { new Entry("", name ?? "", null, fullPath ?? "") });

    internal SdkTypeNames(IReadOnlyList<Entry> entries)
    {
        _entries = entries.ToArray();
        _names = new string[_entries.Length];
        _pathTokens = _entries.Select(e => PathTokens(e.FullPath)).ToArray();

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
    /// The emitted super name: the pool entry at <paramref name="superAddress"/> when there is one,
    /// else a by-name reference of the same kind as the type that inherits.
    /// </summary>
    internal string Super(string? superAddress, string superName, bool? isClass, string? fromPath)
    {
        if (!string.IsNullOrEmpty(superAddress) && _byAddress.TryGetValue(superAddress, out int i))
            return _names[i];
        var kind = isClass switch { true => Kind.Class, false => Kind.Struct, null => Kind.Any };
        return Reference(superName, kind, fromPath);
    }

    /// <summary>The emitted name for a type a member (or a super) names by its short UE name.</summary>
    internal string Reference(string raw, Kind kind, string? fromPath)
    {
        if (string.IsNullOrEmpty(raw)) return raw;

        // The pool holds classes and structs only; an enum of the same name is a different type.
        if (kind != Kind.Enum && _byName.TryGetValue(raw, out var holders))
        {
            var fitting = kind switch
            {
                Kind.Class => holders.Where(h => _entries[h].IsClass != false).ToList(),
                Kind.Struct => holders.Where(h => _entries[h].IsClass != true).ToList(),
                _ => holders,
            };
            if (fitting.Count == 0) fitting = holders;
            if (fitting.Count == 1) return _names[fitting[0]];

            var from = PathTokens(fromPath);
            int best = fitting[0], bestScore = -1;
            foreach (int h in fitting)
            {
                int score = CommonPrefix(_pathTokens[h], from) * 2 + (IsNative(h) ? 1 : 0);
                if (score > bestScore) { best = h; bestScore = score; }
            }
            return _names[best];
        }

        var id = SdkMemberNames.MakeIdentifier(raw);
        return SdkMemberNames.IsReservedIdentifier(id) ? id + "_0" : id;
    }

    // ------------------------------------------------------------------

    private static bool Reserved(string name) =>
        SdkMemberNames.IsReservedIdentifier(name) || BuiltIns.Contains(name);

    private bool IsNative(int i) => _pathTokens[i].Length > 0 && _pathTokens[i][0] == "Script";

    private void Assign()
    {
        var baseNames = _entries.Select(e => SdkMemberNames.MakeIdentifier(e.Name ?? "")).ToArray();
        var groups = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        var order = new List<string>();
        for (int i = 0; i < _entries.Length; i++)
        {
            if (!groups.TryGetValue(baseNames[i], out var g))
            {
                groups[baseNames[i]] = g = new List<int>();
                order.Add(baseNames[i]);
            }
            g.Add(i);
        }

        var taken = new HashSet<string>(StringComparer.Ordinal);

        // Pass 1: the holder that keeps the plain name, if any.
        var toQualify = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        foreach (var name in order)
        {
            var group = groups[name];
            int keeper = -1;
            if (!Reserved(name))
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

        // Pass 2: everyone else is qualified with its outers, at one depth for the whole group.
        foreach (var name in order)
        {
            if (!toQualify.TryGetValue(name, out var group)) continue;
            var usable = group.ToDictionary(h => h, h => Qualifiers(h, name));
            int maxDepth = usable.Values.Max(u => u.Count);
            bool done = false;

            for (int depth = 1; depth <= maxDepth && !done; depth++)
            {
                if (usable.Values.Any(u => u.Count == 0)) break;
                var candidates = group.Select(h => Qualified(name, usable[h], depth)).ToList();
                if (candidates.Distinct(StringComparer.Ordinal).Count() != candidates.Count) continue;
                if (candidates.Any(c => taken.Contains(c) || Reserved(c))) continue;
                for (int k = 0; k < group.Count; k++)
                {
                    _names[group[k]] = candidates[k];
                    taken.Add(candidates[k]);
                }
                done = true;
            }
            if (done) continue;

            // No path tells them apart (a failed walk has no path): number what is left, in order.
            foreach (int h in group)
            {
                var stem = usable[h].Count > 0 ? Qualified(name, usable[h], maxDepth) : name;
                var candidate = stem;
                for (int n = 0; taken.Contains(candidate) || Reserved(candidate); n++)
                    candidate = stem + "_" + n.ToString(CultureInfo.InvariantCulture);
                _names[h] = candidate;
                taken.Add(candidate);
            }
        }
    }

    /// <summary>
    /// The holder's outers, outermost first, as identifiers — without the ones that only repeat the
    /// type's own name (a Blueprint's package <c>BP_Door</c> beside its class <c>BP_Door_C</c>, a
    /// user-defined struct's package beside the struct), which would tell no two holders apart.
    /// </summary>
    private List<string> Qualifiers(int h, string baseName)
    {
        var tokens = _pathTokens[h];
        var result = new List<string>();
        for (int i = 0; i < tokens.Length - 1; i++)
        {
            var t = SdkMemberNames.MakeIdentifier(tokens[i]);
            if (t == baseName || t + "_C" == baseName || t == baseName + "_C") continue;
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
