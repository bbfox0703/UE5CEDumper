using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using UE5DumpUI.Models;

namespace UE5DumpUI.Services;

/// <summary>
/// [DUMPDIFF-UI] Dump Explorer's Compare: a C# port of <c>scripts/analysis/diff_dumps.py</c>, which a release user
/// does not have (dist\ ships nothing under scripts/analysis/). The script stays the reference; the 24 cases in
/// scripts/analysis/fixtures/diff_dumps/ are what both must compute, so follow the script rule for rule, its quirks
/// included: a duplicate path keeps the FIRST record, a duplicate member name keeps its first PLACE and its LAST value
/// (a Python dict built from a list), a missing key differs from an empty one, ties keep file order (Python's sort is
/// stable, List.Sort is not), and paths sort by code point.
/// </summary>
public static class DumpDiffService
{
    // ---------------------------------------------------------------- loading

    /// <summary>Read a Dump All file. A line that is not JSON is skipped and counted, as the script skips it.</summary>
    /// <exception cref="DumpDiffObjectIndexException">The file is Dump All's object index.</exception>
    public static async Task<DumpDiffInput> LoadAsync(string path, IProgress<long>? linesRead = null,
        CancellationToken ct = default)
    {
        var d = new DumpDiffInput { FilePath = path };
        using var reader = new StreamReader(path, new UTF8Encoding(false), detectEncodingFromByteOrderMarks: true);
        long n = 0;
        string? raw;
        while ((raw = await reader.ReadLineAsync(ct).ConfigureAwait(false)) is not null)
        {
            if ((++n & 0x3FFF) == 0) linesRead?.Report(n);
            var line = raw.Trim();
            if (line.Length == 0) continue;
            DumpDiffLine? rec;
            try
            {
                rec = JsonSerializer.Deserialize(line, DumpDiffJsonContext.Default.DumpDiffLine);
            }
            catch (JsonException)
            {
                d.BadLines++;
                continue;
            }
            if (rec is null) { d.BadLines++; continue; }
            switch (rec.Kind)
            {
                case "meta":
                    if (rec.File == "objects")
                        throw new DumpDiffObjectIndexException(Path.GetFileName(path), rec.ClassDump ?? "");
                    d.Meta = rec;
                    break;
                case "class": d.Classes.Add(rec); break;
                case "struct": d.Structs.Add(rec); break;
                case "enum": d.Enums.Add(rec); break;
                case "error": d.Errors.Add(rec); break;
                case "summary":
                    d.Summary = rec;
                    d.HasSummary = true;
                    break;
            }
        }
        linesRead?.Report(n);
        return d;
    }

    // ---------------------------------------------------------------- the diff

    public static DumpDiffResult Diff(DumpDiffInput oldDump, DumpDiffInput newDump, bool includeEngine)
    {
        var r = new DumpDiffResult { OldDump = oldDump, NewDump = newDump, IncludeEngine = includeEngine };
        (r.AddedClasses, r.RemovedClasses, r.ChangedClasses, r.UnchangedClasses) =
            DiffTypes(oldDump.Classes, newDump.Classes, includeEngine, isStruct: false);

        r.StructsSkipped = SkipReason(oldDump, newDump, "struct");
        if (r.StructsSkipped.Length == 0)
            (r.AddedStructs, r.RemovedStructs, r.ChangedStructs, r.UnchangedStructs) =
                DiffTypes(oldDump.Structs, newDump.Structs, includeEngine, isStruct: true);

        r.EnumsSkipped = SkipReason(oldDump, newDump, "enum");
        if (r.EnumsSkipped.Length == 0)
            DiffEnums(r, oldDump, newDump, includeEngine);

        foreach (var (label, d) in Sides(oldDump, newDump))
            if (d.ParamsFromNumParms != 0)
                r.ParamNotes.Add($"{d.ParamsFromNumParms} function(s) in the {label} dump had their parameters taken " +
                                 "from num_parms (a DLL older than build 3622), so a parameter change there may be " +
                                 "that approximation");

        // A cut-off dump lacks what it never reached: that is not a removal (new side) or an addition (old side).
        if (!newDump.HasSummary)
        {
            r.RemovedClasses = new(); r.RemovedStructs = new(); r.RemovedEnums = new();
            r.DumpNotes.Add("the new dump has no summary line: it was cut off mid-write, so a type it lacks is not " +
                            "reported as removed");
        }
        if (!oldDump.HasSummary)
        {
            r.AddedClasses = new(); r.AddedStructs = new(); r.AddedEnums = new();
            r.DumpNotes.Add("the old dump has no summary line: it was cut off mid-write, so a type it lacks is not " +
                            "reported as added");
        }
        foreach (var (label, d) in Sides(oldDump, newDump))
            if (d.Errors.Count > 0)
                r.DumpNotes.Add($"the {label} dump has {d.Errors.Count} error line(s), walks that failed; a type " +
                                "listed as missing because of one is tagged");
        return r;
    }

    private static (string Label, DumpDiffInput Dump)[] Sides(DumpDiffInput o, DumpDiffInput n) =>
        new[] { ("old", o), ("new", n) };

    /// <summary>Strip the leading slashes, so '//Script/X/Y' and '/Script/X/Y' match. Case is kept: UE paths
    /// preserve it.</summary>
    internal static string NormalizePath(string? path) => (path ?? "").TrimStart('/');

    /// <summary>path -> record, engine types dropped unless asked for. A record with no path is keyed by name; a
    /// duplicate path (a dumper bug) keeps the first.</summary>
    private static OrderedMap<DumpDiffLine> IndexByPath(List<DumpDiffLine> records, bool includeEngine)
    {
        var map = new OrderedMap<DumpDiffLine>();
        foreach (var rec in records)
        {
            if (!includeEngine && DumpAllService.IsEnginePath(rec.Path ?? "")) continue;
            var key = NormalizePath(rec.Path);
            if (key.Length == 0) key = "::name::" + (rec.Name ?? "");
            map.AddFirst(key, rec);
        }
        return map;
    }

    private static (List<DumpDiffLine> Added, List<DumpDiffLine> Removed, List<DumpDiffTypeChange> Changed, int Unchanged)
        DiffTypes(List<DumpDiffLine> oldRecords, List<DumpDiffLine> newRecords, bool includeEngine, bool isStruct)
    {
        var oldIdx = IndexByPath(oldRecords, includeEngine);
        var newIdx = IndexByPath(newRecords, includeEngine);
        var added = new List<DumpDiffLine>();
        var removed = oldIdx.Where(kv => !newIdx.ContainsKey(kv.Key)).Select(kv => kv.Value).ToList();
        var changed = new List<DumpDiffTypeChange>();
        int unchanged = 0;
        foreach (var (path, newCls) in newIdx)
        {
            if (!oldIdx.TryGetValue(path, out var oldCls))
            {
                added.Add(newCls);
                continue;
            }
            var props = DiffProps(oldCls, newCls);
            var funcs = DiffFuncs(oldCls, newCls);
            long sizeDelta = (newCls.PropsSize ?? 0) - (oldCls.PropsSize ?? 0);
            if (props.Count > 0 || funcs.Count > 0 || sizeDelta != 0)
                changed.Add(new DumpDiffTypeChange
                {
                    Name = newCls.Name ?? oldCls.Name ?? "",
                    Path = path,
                    Old = oldCls,
                    New = newCls,
                    PropChanges = props,
                    FuncChanges = funcs,
                    IsStruct = isStruct,
                });
            else
                unchanged++;
        }
        // OrderBy is stable, as the script's sort is: records with equal keys keep their file order.
        return (added.OrderBy(c => NormalizePath(c.Path), CodePointComparer.Instance).ToList(),
                removed.OrderBy(c => NormalizePath(c.Path), CodePointComparer.Instance).ToList(),
                changed.OrderBy(c => c.Path, CodePointComparer.Instance).ToList(),
                unchanged);
    }

    /// <summary>Per-property changes, matched by name within the type.</summary>
    private static List<DumpDiffPropChange> DiffProps(DumpDiffLine oldCls, DumpDiffLine newCls)
    {
        var oldProps = ByName(oldCls.Props, p => p.Name);
        var newProps = ByName(newCls.Props, p => p.Name);
        var changes = new List<DumpDiffPropChange>();
        foreach (var (name, op) in oldProps)
        {
            if (!newProps.TryGetValue(name, out var np))
            {
                changes.Add(new(name, DumpDiffPropKind.Removed, op, null));
                continue;
            }
            // A type change is its own kind: FloatProperty -> DoubleProperty is not binary-compatible.
            bool typeChanged = op.Type != np.Type || op.InnerType != np.InnerType || op.StructType != np.StructType
                               || op.ObjClass != np.ObjClass || op.Enum != np.Enum;
            bool moved = op.Offset != np.Offset || op.Size != np.Size;
            if (typeChanged) changes.Add(new(name, DumpDiffPropKind.TypeChanged, op, np));
            else if (moved) changes.Add(new(name, DumpDiffPropKind.Moved, op, np));
        }
        foreach (var (name, np) in newProps)
            if (!oldProps.ContainsKey(name))
                changes.Add(new(name, DumpDiffPropKind.Added, null, np));
        return changes;
    }

    /// <summary>Functions' bodies are not dumped, so a signature is the metadata that is: the return type, the
    /// parameter count and size, the flags, and the parameters themselves when both files carry them.</summary>
    private static bool SignatureDiffers(DumpDiffFunc a, DumpDiffFunc b) =>
        a.ReturnType != b.ReturnType || a.NumParms != b.NumParms || a.ParmsSize != b.ParmsSize
        || a.Flags != b.Flags || ParamsDiffer(a, b);

    /// <summary>Only when both sides carry parameters (Dump All from build 3622): an older file has none, and that is
    /// not a change.</summary>
    internal static bool ParamsDiffer(DumpDiffFunc? a, DumpDiffFunc? b)
    {
        if (a?.Params is not { } pa || b?.Params is not { } pb) return false;
        if (pa.Count != pb.Count) return true;
        for (int i = 0; i < pa.Count; i++)
        {
            DumpDiffParam x = pa[i], y = pb[i];
            if (x.Name != y.Name || x.Type != y.Type || x.StructType != y.StructType || x.ObjClass != y.ObjClass
                || (x.Out ?? false) != (y.Out ?? false) || (x.Ret ?? false) != (y.Ret ?? false)
                || x.Offset != y.Offset || x.Size != y.Size)
                return true;
        }
        return false;
    }

    private static List<DumpDiffFuncChange> DiffFuncs(DumpDiffLine oldCls, DumpDiffLine newCls)
    {
        var oldFuncs = ByName(oldCls.Funcs, f => f.Name);
        var newFuncs = ByName(newCls.Funcs, f => f.Name);
        var changes = new List<DumpDiffFuncChange>();
        foreach (var (name, of) in oldFuncs)
        {
            if (!newFuncs.TryGetValue(name, out var nf))
            {
                changes.Add(new(name, DumpDiffFuncKind.Removed, of, null));
                continue;
            }
            if (SignatureDiffers(of, nf))
                changes.Add(new(name, DumpDiffFuncKind.SignatureChanged, of, nf));
        }
        foreach (var (name, nf) in newFuncs)
            if (!oldFuncs.ContainsKey(name))
                changes.Add(new(name, DumpDiffFuncKind.Added, null, nf));
        return changes;
    }

    private static List<DumpDiffEnumEntryChange> DiffEnumEntries(DumpDiffLine oldE, DumpDiffLine newE)
    {
        var oldV = ByName(oldE.Entries, e => e.Name);
        var newV = ByName(newE.Entries, e => e.Name);
        var changes = new List<DumpDiffEnumEntryChange>();
        foreach (var (name, ov) in oldV)
        {
            if (!newV.TryGetValue(name, out var nv))
                changes.Add(new(name, DumpDiffEnumEntryKind.Removed, ov.Value, null));
            else if (nv.Value != ov.Value)
                changes.Add(new(name, DumpDiffEnumEntryKind.ValueChanged, ov.Value, nv.Value));
        }
        foreach (var (name, nv) in newV)
            if (!oldV.ContainsKey(name))
                changes.Add(new(name, DumpDiffEnumEntryKind.Added, null, nv.Value));
        return changes;
    }

    /// <summary>Why structs or enums cannot be compared, or "" when they can.</summary>
    private static string SkipReason(DumpDiffInput oldDump, DumpDiffInput newDump, string what)
    {
        var sides = Sides(oldDump, newDump);
        if (what == "struct")
        {
            foreach (var (label, d) in sides)
                if (!d.CarriesStructs) return MissingLines(label, d, "struct", 3620);
            return "";
        }
        foreach (var (label, d) in sides)
            if (!d.CarriesEnums) return MissingLines(label, d, "enum", 3621);
        foreach (var (label, d) in sides)
            if (!d.EnumsListed) return $"the {label} dump's enum list could not be read (see its list_enums error line)";
        return "";
    }

    /// <summary>A dump of a build that writes these lines, cut off before them, is not a dump of an older build.</summary>
    private static string MissingLines(string label, DumpDiffInput d, string what, int since) =>
        !d.HasSummary && d.DumperBuild >= since
            ? $"the {label} dump ends before its {what} lines (it has no summary line: it was cut off mid-write)"
            : $"the {label} dump has no {what} lines (Dump All writes them from build {since})";

    /// <summary>What a list could not say is not reported as a change: with no member names an enum's entries are
    /// empty, and a cut-short list lacks enums it never reached.</summary>
    private static void DiffEnums(DumpDiffResult r, DumpDiffInput oldDump, DumpDiffInput newDump, bool includeEngine)
    {
        bool namesOk = true;
        foreach (var (label, d) in Sides(oldDump, newDump))
        {
            if (!d.EnumNamesFailed) continue;
            namesOk = false;
            r.EnumNotes.Add($"enum member names were unavailable in the {label} dump, so enumerators are not compared");
        }
        if (oldDump.EnumsTruncated)
            r.EnumNotes.Add("the old dump's enum list was cut short, so an enum it lacks is not reported as added");
        if (newDump.EnumsTruncated)
            r.EnumNotes.Add("the new dump's enum list was cut short, so an enum it lacks is not reported as removed");

        var oldIdx = IndexByPath(oldDump.Enums, includeEngine);
        var newIdx = IndexByPath(newDump.Enums, includeEngine);
        var removed = new List<DumpDiffLine>();
        var added = new List<DumpDiffLine>();
        var changed = new List<DumpDiffEnumChange>();
        if (!newDump.EnumsTruncated)
            removed = oldIdx.Where(kv => !newIdx.ContainsKey(kv.Key)).Select(kv => kv.Value).ToList();
        foreach (var (path, ne) in newIdx)
        {
            if (!oldIdx.TryGetValue(path, out var oe))
            {
                if (!oldDump.EnumsTruncated) added.Add(ne);
                continue;
            }
            if (!namesOk)
            {
                r.UncomparedEnums++;
                continue;
            }
            var changes = DiffEnumEntries(oe, ne);
            if (changes.Count > 0) changed.Add(new DumpDiffEnumChange { Name = ne.Name ?? "", Path = path, Changes = changes });
            else r.UnchangedEnums++;
        }
        r.AddedEnums = added.OrderBy(e => NormalizePath(e.Path), CodePointComparer.Instance).ToList();
        r.RemovedEnums = removed.OrderBy(e => NormalizePath(e.Path), CodePointComparer.Instance).ToList();
        r.ChangedEnums = changed.OrderBy(e => e.Path, CodePointComparer.Instance).ToList();
    }

    // ---------------------------------------------------------------- what the report draws on

    /// <summary>The names of a dump's error lines: a type missing from that dump with one of these names was not read
    /// there, which is not the same as gone. Error lines carry a name, not a path.</summary>
    internal static HashSet<string> FailedNames(DumpDiffInput d)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var e in d.Errors)
            if (!string.IsNullOrEmpty(e.Name)) set.Add(e.Name);
        return set;
    }

    internal static DumpDiffCounts CountChanges(List<DumpDiffTypeChange> changed)
    {
        var c = new DumpDiffCounts();
        foreach (var cd in changed)
        {
            bool hadMove = false, hadSig = false;
            foreach (var pc in cd.PropChanges)
            {
                switch (pc.Kind)
                {
                    case DumpDiffPropKind.Added: c.PropAdded++; break;
                    case DumpDiffPropKind.Removed: c.PropRemoved++; break;
                    case DumpDiffPropKind.Moved: c.PropMoved++; hadMove = true; break;
                    case DumpDiffPropKind.TypeChanged: c.PropTypeChanged++; hadMove = true; break;
                }
            }
            foreach (var fc in cd.FuncChanges)
            {
                switch (fc.Kind)
                {
                    case DumpDiffFuncKind.Added: c.FuncAdded++; break;
                    case DumpDiffFuncKind.Removed: c.FuncRemoved++; break;
                    case DumpDiffFuncKind.SignatureChanged: c.FuncSignatureChanged++; hadSig = true; break;
                }
            }
            if (hadMove) c.TypesWithMovedFields++;
            if (hadSig) c.TypesWithSigChanges++;
            if (cd.PropsSizeDelta != 0) c.TypesWithSizeDelta++;
        }
        return c;
    }

    /// <summary>"0x40", or "?" when the dump has no offset. A negative value prints as the script prints it.</summary>
    internal static string FormatOffset(long? v) =>
        v is not { } x ? "?" : x < 0 ? "0x-" + (-x).ToString("X", CultureInfo.InvariantCulture)
                                     : "0x" + x.ToString("X", CultureInfo.InvariantCulture);

    internal static string FormatNumber(long? v) => v?.ToString(CultureInfo.InvariantCulture) ?? "?";

    /// <summary>A property's type with its inner / struct / object / enum qualifier, so a type change is unambiguous.</summary>
    internal static string FormatPropType(DumpDiffProp p)
    {
        var t = p.Type ?? "?";
        var extras = new List<string>(4);
        if (!string.IsNullOrEmpty(p.InnerType)) extras.Add("inner=" + p.InnerType);
        if (!string.IsNullOrEmpty(p.StructType)) extras.Add("struct=" + p.StructType);
        if (!string.IsNullOrEmpty(p.ObjClass)) extras.Add("obj=" + p.ObjClass);
        if (!string.IsNullOrEmpty(p.Enum)) extras.Add("enum=" + p.Enum);
        return extras.Count > 0 ? $"{t} ({string.Join(", ", extras)})" : t;
    }

    /// <summary>A function's parameters as compared: each argument with its offset and size, an out one marked, then
    /// the return entry, whose class or struct the return-type column does not carry.</summary>
    internal static string FormatParams(DumpDiffFunc f)
    {
        static string One(DumpDiffParam p)
        {
            var t = p.Type ?? "?";
            if (!string.IsNullOrEmpty(p.StructType)) t += "<" + p.StructType + ">";
            if (!string.IsNullOrEmpty(p.ObjClass)) t += ":" + p.ObjClass;
            return $"{t} {p.Name ?? "?"}@{FormatOffset(p.Offset)}/{FormatNumber(p.Size)}";
        }

        var ps = f.Params ?? new List<DumpDiffParam>();
        var args = string.Join(", ", ps.Where(p => !(p.Ret ?? false)).Select(p => ((p.Out ?? false) ? "out " : "") + One(p)));
        var ret = ps.FirstOrDefault(p => p.Ret ?? false);
        return $"({args})" + (ret is null ? "" : " -> " + One(ret));
    }

    // ---------------------------------------------------------------- helpers

    /// <summary>Members by name, as the script's dict comprehension builds them: a nameless member is ignored, and a
    /// duplicate name keeps its first place and takes the last value.</summary>
    private static OrderedMap<T> ByName<T>(List<T>? items, Func<T, string?> name)
    {
        var map = new OrderedMap<T>();
        if (items is null) return map;
        foreach (var item in items)
        {
            var n = name(item);
            if (!string.IsNullOrEmpty(n)) map.SetLast(n, item);
        }
        return map;
    }

    /// <summary>An insertion-ordered map: Python's dict, which the diff's order and its duplicate rules come from.</summary>
    private sealed class OrderedMap<T> : IEnumerable<KeyValuePair<string, T>>
    {
        private readonly List<KeyValuePair<string, T>> _items = new();
        private readonly Dictionary<string, int> _index = new(StringComparer.Ordinal);

        /// <summary>Keep the first value of a repeated key.</summary>
        public void AddFirst(string key, T value)
        {
            if (_index.ContainsKey(key)) return;
            _index[key] = _items.Count;
            _items.Add(new(key, value));
        }

        /// <summary>Keep the first position of a repeated key, with the last value.</summary>
        public void SetLast(string key, T value)
        {
            if (_index.TryGetValue(key, out var i)) _items[i] = new(key, value);
            else { _index[key] = _items.Count; _items.Add(new(key, value)); }
        }

        public bool ContainsKey(string key) => _index.ContainsKey(key);

        public bool TryGetValue(string key, out T value)
        {
            if (_index.TryGetValue(key, out var i)) { value = _items[i].Value; return true; }
            value = default!;
            return false;
        }

        public IEnumerator<KeyValuePair<string, T>> GetEnumerator() => _items.GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    /// <summary>Python compares strings by code point; ordinal UTF-16 order differs above U+FFFF (a surrogate pair
    /// sorts before U+E000..U+FFFF).</summary>
    private sealed class CodePointComparer : IComparer<string>
    {
        public static readonly CodePointComparer Instance = new();

        public int Compare(string? x, string? y)
        {
            if (ReferenceEquals(x, y)) return 0;
            if (x is null) return -1;
            if (y is null) return 1;
            var ex = x.EnumerateRunes();
            var ey = y.EnumerateRunes();
            while (true)
            {
                bool hx = ex.MoveNext(), hy = ey.MoveNext();
                if (!hx || !hy) return hx == hy ? 0 : hx ? 1 : -1;
                int c = ex.Current.Value.CompareTo(ey.Current.Value);
                if (c != 0) return c;
            }
        }
    }
}
