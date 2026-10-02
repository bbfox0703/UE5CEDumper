using System.Text;
using System.Text.Json;
using UE5DumpUI.Models;

namespace UE5DumpUI.Services;

/// <summary>
/// Reads a "Dump All" JSON-Lines file (see <see cref="DumpAllService"/>) back
/// into a flattened, searchable corpus for the Dump Explorer panel. A class or
/// struct line becomes one type row plus one row per property and, for a class,
/// one row per function. An enum line becomes one enum row plus one row per
/// enumerator. Members carry the owning type's object path and dump-time
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
    /// the meta header and every well-formed class, struct, and enum line are
    /// returned. Instance lines are not rows.
    /// </summary>
    public static async Task<DumpFileModel> ReadAsync(
        string filePath,
        IProgress<int>? progress = null,
        CancellationToken ct = default)
    {
        DumpMetaLine? meta = null;
        var entries = new List<DumpEntry>();
        int classCount = 0, propCount = 0, funcCount = 0;

        await using var fs = new FileStream(
            filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, useAsync: true);
        using var reader = new StreamReader(fs, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);

        int lineNo = 0;
        string? line;
        while ((line = await reader.ReadLineAsync(ct)) is not null)
        {
            lineNo++;
            if (line.Length == 0) continue;

            // Writer emits kind first, with no space. Instance lines are not
            // rows: a full object index is hundreds of thousands of them.
            if (line.StartsWith("{\"kind\":\"instance\"", StringComparison.Ordinal))
                continue;

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
                    AppendType(entries, probe, DumpEntryKind.Struct, ref classCount, ref propCount, ref funcCount);
                    break;
                case "enum":
                    AppendEnum(entries, line);
                    break;
                case "meta":
                    try
                    {
                        meta = JsonSerializer.Deserialize(line, DumpJsonlContext.Default.DumpMetaLine);
                    }
                    catch (JsonException) { /* leave meta null */ }
                    break;
                // "error" / "summary" lines carry no browsable metadata.
            }

            if ((lineNo & 0x3FF) == 0)
                progress?.Report(entries.Count);
        }

        return new DumpFileModel
        {
            Meta = meta,
            Entries = entries,
            ClassCount = classCount,
            PropertyCount = propCount,
            FunctionCount = funcCount,
        };
    }

    private static void AppendEnum(List<DumpEntry> entries, string line)
    {
        DumpEnumLine? en;
        try
        {
            en = JsonSerializer.Deserialize(line, DumpJsonlContext.Default.DumpEnumLine);
        }
        catch (JsonException)
        {
            return;
        }
        if (en is null || string.IsNullOrEmpty(en.Name)) return;

        var path = en.Path ?? "";
        var meta = string.IsNullOrEmpty(en.Meta) ? "Enum" : en.Meta;
        entries.Add(new DumpEntry
        {
            Kind = DumpEntryKind.Enum,
            Name = en.Name,
            OwnerClass = "",
            OwnerMeta = meta,
            TypeInfo = "",
            Offset = -1,
            Path = path,
            ClassAddr = en.Addr ?? "",
            Haystack = BuildHaystack(en.Name, meta, "", path),
        });
        if (en.Entries is not { Count: > 0 }) return;
        foreach (var entry in en.Entries)
        {
            entries.Add(new DumpEntry
            {
                Kind = DumpEntryKind.Enumerator,
                Name = entry.N,
                OwnerClass = en.Name,
                OwnerMeta = meta,
                TypeInfo = entry.V.ToString(),
                Offset = -1,
                Path = path,
                ClassAddr = en.Addr ?? "",
                Haystack = BuildHaystack(entry.N, en.Name, entry.V.ToString(), path),
            });
        }
    }

    private static void AppendType(
        List<DumpEntry> entries, DumpClassLine c, DumpEntryKind rowKind,
        ref int classCount, ref int propCount, ref int funcCount)
    {
        var path = c.Path ?? "";
        var classAddr = c.Addr ?? "";
        var meta = c.Meta ?? "";

        var superInfo = string.IsNullOrEmpty(c.Super) ? "" : $": {c.Super}";
        entries.Add(new DumpEntry
        {
            Kind = rowKind,
            Name = c.Name,
            OwnerClass = "",
            OwnerMeta = meta,
            TypeInfo = superInfo,
            Offset = -1,
            Path = path,
            ClassAddr = classAddr,
            Haystack = BuildHaystack(c.Name, meta, superInfo, path),
        });
        if (rowKind == DumpEntryKind.Class)
            classCount++;

        // Property rows.
        if (c.Props is { Count: > 0 })
        {
            foreach (var p in c.Props)
            {
                var typeInfo = ComposePropType(p);
                entries.Add(new DumpEntry
                {
                    Kind = DumpEntryKind.Property,
                    Name = p.Name,
                    OwnerClass = c.Name,
                    OwnerMeta = meta,
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
                var sig = string.IsNullOrEmpty(f.ReturnType)
                    ? $"({f.NumParms})"
                    : $"{f.ReturnType} ({f.NumParms})";
                entries.Add(new DumpEntry
                {
                    Kind = DumpEntryKind.Function,
                    Name = f.Name,
                    OwnerClass = c.Name,
                    OwnerMeta = meta,
                    TypeInfo = sig,
                    Offset = -1,
                    Path = path,
                    ClassAddr = classAddr,
                    FuncAddr = f.Addr ?? "",
                    Haystack = BuildHaystack(f.Name, c.Name, sig, path, ParamHaystack(f)),
                });
                funcCount++;
            }
        }
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

    /// <summary>Parm names and types for the function haystack. Empty when the dump
    /// predates the <c>params</c> array, so those rows search as they did before.</summary>
    private static string ParamHaystack(DumpFuncLine f)
    {
        if (f.Params is not { Count: > 0 }) return "";
        var sb = new StringBuilder();
        foreach (var p in f.Params)
        {
            if (sb.Length > 0) sb.Append(' ');
            sb.Append(p.Name).Append(' ').Append(p.Type);
            if (!string.IsNullOrEmpty(p.StructType))
                sb.Append(' ').Append(p.StructType);
            if (!string.IsNullOrEmpty(p.ObjClass))
                sb.Append(' ').Append(p.ObjClass);
        }
        return sb.ToString();
    }

    /// <summary>Space-joined, lower-cased once at parse time so live filtering is a cheap Contains.</summary>
    private static string BuildHaystack(string a, string b, string c, string d, string e = "")
    {
        var sb = new StringBuilder(a.Length + b.Length + c.Length + d.Length + e.Length + 4);
        sb.Append(a).Append(' ').Append(b).Append(' ').Append(c).Append(' ').Append(d);
        if (e.Length > 0)
            sb.Append(' ').Append(e);
        return sb.ToString().ToLowerInvariant();
    }
}
