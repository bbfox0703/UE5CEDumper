using System.Globalization;
using System.Text;
using System.Text.Json;
using UE5DumpUI.Models;

namespace UE5DumpUI.Services;

/// <summary>
/// Reads a "Dump All" JSON-Lines file (see <see cref="DumpAllService"/>) back
/// into a flattened, searchable corpus for the Dump Explorer panel. Every
/// class or struct line is expanded into its own row + one row per property +
/// one row per function, and every enum line into its row + one row per
/// enumerator, all carrying the owning type's object path and dump-time
/// address so a single per-type live match classifies the whole family.
///
/// Purely client-side and offline: parsing a dump requires no live game. The
/// live-match / jump features (which DO need a connected game) live in the
/// ViewModel; this reader only turns the file into rows.
/// </summary>
public static class DumpJsonlReader
{
    /// <summary>
    /// Parse the JSON-Lines file at <paramref name="filePath"/>. Malformed
    /// lines are skipped (a dump can be truncated mid-write if the game exited);
    /// the meta header and every well-formed type line are returned.
    /// </summary>
    public static async Task<DumpFileModel> ReadAsync(
        string filePath,
        IProgress<int>? progress = null,
        CancellationToken ct = default)
    {
        DumpMetaLine? meta = null;
        DumpSummaryLine? summary = null;
        var entries = new List<DumpEntry>();
        // Enum lines come before the summary, whose enum_names_failed decides how an empty enum reads; their rows
        // are written once the file is read.
        var enumLines = new List<DumpEnumLine>();
        int classCount = 0, structCount = 0, enumCount = 0, propCount = 0, funcCount = 0;

        await using var fs = new FileStream(
            filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, useAsync: true);
        using var reader = new StreamReader(fs, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);

        int lineNo = 0;
        string? line;
        while ((line = await reader.ReadLineAsync(ct)) is not null)
        {
            lineNo++;
            if (line.Length == 0) continue;

            DumpClassLine? probe;
            try
            {
                probe = JsonSerializer.Deserialize(line, DumpJsonlContext.Default.DumpClassLine);
            }
            catch (JsonException)
            {
                continue;   // skip a corrupt/partial line
            }
            if (probe is null) continue;

            switch (probe.Kind)
            {
                case "class":
                    AppendType(entries, probe, DumpEntryKind.Class, ref classCount, ref propCount, ref funcCount);
                    break;
                case "struct":
                    AppendType(entries, probe, DumpEntryKind.Struct, ref structCount, ref propCount, ref funcCount);
                    break;
                case "enum":
                    try
                    {
                        var e = JsonSerializer.Deserialize(line, DumpJsonlContext.Default.DumpEnumLine);
                        if (e is not null) enumLines.Add(e);
                    }
                    catch (JsonException) { /* skip a corrupt enum line */ }
                    break;
                case "summary":
                    try
                    {
                        summary = JsonSerializer.Deserialize(line, DumpJsonlContext.Default.DumpSummaryLine);
                    }
                    catch (JsonException) { /* leave summary null */ }
                    break;
                case "meta":
                    try
                    {
                        meta = JsonSerializer.Deserialize(line, DumpJsonlContext.Default.DumpMetaLine);
                    }
                    catch (JsonException) { /* leave meta null */ }
                    // [EXTPR-539-540-2026-10-02] Dump All's object index shares the extension and can hold over a
                    // million object lines, none of them browsable here: stop at its meta line.
                    if (meta?.File == "objects")
                        return new DumpFileModel { Meta = meta, IsObjectIndex = true, ClassDumpFile = meta.ClassDump };
                    break;
                // "error" lines carry no browsable metadata.
            }

            if ((lineNo & 0x3FF) == 0)
                progress?.Report(entries.Count);
        }

        bool namesFailed = summary?.EnumNamesFailed == true;
        foreach (var e in enumLines)
            AppendEnum(entries, e, namesFailed, ref enumCount);

        return new DumpFileModel
        {
            Meta = meta,
            Entries = entries,
            EnumsListed = summary?.EnumsListed,
            EnumNamesFailed = namesFailed,
            EnumsTruncated = summary?.EnumsTruncated == true,
            ClassCount = classCount,
            StructCount = structCount,
            EnumCount = enumCount,
            PropertyCount = propCount,
            FunctionCount = funcCount,
        };
    }

    /// <summary>A class or struct line: they share a shape, a struct's having no functions.</summary>
    private static void AppendType(
        List<DumpEntry> entries, DumpClassLine c, DumpEntryKind kind,
        ref int typeCount, ref int propCount, ref int funcCount)
    {
        var path = c.Path ?? "";
        var classAddr = c.Addr ?? "";

        // The type's own row.
        var superInfo = string.IsNullOrEmpty(c.Super) ? "" : $": {c.Super}";
        entries.Add(new DumpEntry
        {
            Kind = kind,
            OwnerKind = kind,
            Name = c.Name,
            OwnerClass = "",
            TypeInfo = superInfo,
            Offset = -1,
            Path = path,
            ClassAddr = classAddr,
            Haystack = BuildHaystack(c.Name, c.Meta, superInfo, path),
        });
        typeCount++;

        // Property rows.
        if (c.Props is { Count: > 0 })
        {
            foreach (var p in c.Props)
            {
                var typeInfo = ComposePropType(p);
                entries.Add(new DumpEntry
                {
                    Kind = DumpEntryKind.Property,
                    OwnerKind = kind,
                    Name = p.Name,
                    OwnerClass = c.Name,
                    TypeInfo = typeInfo,
                    Offset = p.Offset,
                    Path = path,
                    ClassAddr = classAddr,
                    Haystack = BuildHaystack(p.Name, c.Name, typeInfo, path),
                });
                propCount++;
            }
        }

        // Function rows.
        if (c.Funcs is { Count: > 0 })
        {
            foreach (var f in c.Funcs)
            {
                var sig = ComposeSignature(f);
                entries.Add(new DumpEntry
                {
                    Kind = DumpEntryKind.Function,
                    OwnerKind = kind,
                    Name = f.Name,
                    OwnerClass = c.Name,
                    TypeInfo = sig,
                    Offset = -1,
                    Path = path,
                    ClassAddr = classAddr,
                    FuncAddr = f.Addr ?? "",
                    Haystack = BuildHaystack(f.Name, c.Name, sig, path),
                });
                funcCount++;
            }
        }
    }

    /// <summary>[EXTPR-539-540-2026-10-02] An enum line: its row, then one row per enumerator. The enum's
    /// dump-time address stands in for the class address every member row carries.</summary>
    private static void AppendEnum(List<DumpEntry> entries, DumpEnumLine e, bool namesFailed, ref int enumCount)
    {
        var path = e.Path ?? "";
        var addr = e.Addr ?? "";
        int n = e.Entries?.Count ?? 0;
        // With no member names (the summary's enum_names_failed) every enum has none: "0 entries" would read as
        // an empty enum.
        var typeInfo = namesFailed && n == 0 ? "entries unreadable" : n.ToString(CultureInfo.InvariantCulture) + " entries";
        entries.Add(new DumpEntry
        {
            Kind = DumpEntryKind.Enum,
            OwnerKind = DumpEntryKind.Enum,
            Name = e.Name,
            OwnerClass = "",
            TypeInfo = typeInfo,
            Offset = -1,
            Path = path,
            ClassAddr = addr,
            Haystack = BuildHaystack(e.Name, "Enum", typeInfo, path),
        });
        enumCount++;

        if (e.Entries is null) return;
        foreach (var x in e.Entries)
        {
            var value = "= " + x.Value.ToString(CultureInfo.InvariantCulture);
            entries.Add(new DumpEntry
            {
                Kind = DumpEntryKind.Enumerator,
                OwnerKind = DumpEntryKind.Enum,
                Name = x.Name,
                OwnerClass = e.Name,
                TypeInfo = value,
                Offset = -1,
                Path = path,
                ClassAddr = addr,
                Haystack = BuildHaystack(x.Name, e.Name, value, path),
            });
        }
    }

    /// <summary>[EXTPR-539-540-2026-10-02] A function row's signature: the return type in front, then the
    /// arguments, an out one marked. A dump from before build 3622 has no params, so it shows the count it
    /// recorded instead.</summary>
    internal static string ComposeSignature(DumpFuncLine f)
    {
        var sb = new StringBuilder();
        // The return in front: from its params entry when there is one, which says which struct or class comes
        // back (return_type is only the property type).
        var ret = f.Params?.FirstOrDefault(p => p.Ret);
        if (ret is not null) AppendParamType(sb, ret).Append(' ');
        else if (!string.IsNullOrEmpty(f.ReturnType)) sb.Append(f.ReturnType).Append(' ');
        sb.Append('(');
        if (f.Params is null)
            return sb.Append(f.NumParms.ToString(CultureInfo.InvariantCulture)).Append(')').ToString();
        bool first = true;
        foreach (var p in f.Params)
        {
            if (p.Ret) continue;
            if (!first) sb.Append(", ");
            first = false;
            if (p.Out) sb.Append("out ");
            AppendParamType(sb, p).Append(' ').Append(p.Name);
        }
        return sb.Append(')').ToString();
    }

    private static StringBuilder AppendParamType(StringBuilder sb, DumpFuncParamLine p)
    {
        sb.Append(p.Type);
        if (!string.IsNullOrEmpty(p.StructType)) sb.Append('<').Append(p.StructType).Append('>');
        if (!string.IsNullOrEmpty(p.ObjClass)) sb.Append(':').Append(p.ObjClass);
        return sb;
    }

    /// <summary>Human-readable type string: base type plus struct/inner/enum/obj-class detail.</summary>
    internal static string ComposePropType(DumpPropLine p)
    {
        var sb = new StringBuilder(p.Type);
        if (!string.IsNullOrEmpty(p.StructType))
            sb.Append('<').Append(p.StructType).Append('>');
        if (!string.IsNullOrEmpty(p.InnerType))
        {
            sb.Append('<').Append(p.InnerType);
            if (!string.IsNullOrEmpty(p.InnerStructType))
                sb.Append(':').Append(p.InnerStructType);
            sb.Append('>');
        }
        if (!string.IsNullOrEmpty(p.ObjClass))
            sb.Append(':').Append(p.ObjClass);
        if (!string.IsNullOrEmpty(p.Enum))
            sb.Append('(').Append(p.Enum).Append(')');
        return sb.ToString();
    }

    /// <summary>Space-joined, lower-cased once at parse time so live filtering is a cheap Contains.</summary>
    private static string BuildHaystack(string a, string b, string c, string d)
    {
        var sb = new StringBuilder(a.Length + b.Length + c.Length + d.Length + 3);
        sb.Append(a).Append(' ').Append(b).Append(' ').Append(c).Append(' ').Append(d);
        return sb.ToString().ToLowerInvariant();
    }
}
