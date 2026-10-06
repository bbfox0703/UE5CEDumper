using System.Globalization;
using System.Text;
using System.Text.Json;
using UE5DumpUI.Core;
using UE5DumpUI.Models;

namespace UE5DumpUI.Services;

/// <summary>
/// [EXTPR-539-540-2026-10-02] D4: the object index, Dump All's opt-in second file
/// (<c>&lt;name&gt;.objects.jsonl</c>). Every object get_object_list returns — packages, types, class-default
/// objects and instances alike, as Dumper-7's and RE-UE4SS's object dumps have them — one JSON line each:
/// <list type="bullet">
///   <item><c>{"kind":"meta","file":"objects", ...}</c> — first line; the class dump's identity (module,
///     pe_hash, UE version, object count, dumper build) and its file name, so the two files pair.</item>
///   <item><c>{"kind":"object","index":...,"addr":...,"name":...,"class":...,"outer":...,"path":...}</c> —
///     <c>index</c> is the GObjects slot (Dumper-7's lesson: two files match by slot within one session).
///     A DLL older than build 3625 sends none, and then the line has none: the handler skips null and unnamed
///     slots, so a row's place in the page is not its slot.</item>
///   <item><c>{"kind":"summary","objects_written":...,"objects_total":...,"index_missing":...}</c> — last.
///     <c>objects_total</c> is the pool's SLOT count, null and unnamed slots included, so it exceeds
///     <c>objects_written</c> on any pool with holes.</item>
/// </list>
/// Addresses are valid for that run of the game only. The file can run to hundreds of megabytes on a big pool,
/// so the caller shows <see cref="EstimateAsync"/>'s numbers and asks before writing it (D4.2).
/// </summary>
public static class ObjectIndexService
{
    /// <summary>The index file beside a class dump: <c>game-dump.jsonl</c> → <c>game-dump.objects.jsonl</c>.</summary>
    public static string FileNameFor(string classDumpPath)
        => Path.Combine(Path.GetDirectoryName(classDumpPath) ?? "",
                        Path.GetFileNameWithoutExtension(classDumpPath) + ".objects.jsonl");

    /// <summary>
    /// Fetch ONE page, serialize it exactly as <see cref="GenerateAsync"/> will, and scale it to the pool:
    /// rows and bytes per scanned slot × the pool's slot count, and the page's round trip × the page count.
    /// The pool's slot count includes the null and unnamed slots the list skips, so objects are estimated from
    /// the page's rows per slot, not taken as the slot count. An approximation, since the pool's later pages
    /// are not its first.
    /// </summary>
    public static async Task<ObjectIndexEstimate> EstimateAsync(
        IDumpService dump, CancellationToken ct = default, TimeProvider? clock = null)
    {
        clock ??= TimeProvider.System;
        long start = clock.GetTimestamp();
        var page = await dump.GetObjectIndexPageAsync(0, Constants.GObjectsWalkPageSize, ct);
        var elapsed = clock.GetElapsedTime(start);
        int scanned = page.Scanned > 0 ? page.Scanned : page.Objects.Count;
        if (page.Total <= 0 || scanned <= 0)
            return new ObjectIndexEstimate(0, 0, 0, TimeSpan.Zero);

        long pageBytes = 0;
        var sb = new StringBuilder(256);
        foreach (var o in page.Objects)
        {
            sb.Clear();
            AppendObjectLine(sb, o);
            pageBytes += Encoding.UTF8.GetByteCount(sb.ToString()) + 1;   // + the "\n"
        }
        int pages = (page.Total + scanned - 1) / scanned;
        long bytes = (long)Math.Round((double)pageBytes / scanned * page.Total) + EnvelopeBytes;
        int objects = (int)Math.Round((double)page.Objects.Count / scanned * page.Total);
        return new ObjectIndexEstimate(objects, page.Total, bytes, elapsed * pages);
    }

    /// <summary>Write the index to <paramref name="output"/>. <paramref name="classDumpFile"/> is the class
    /// dump's file name, recorded so the two files pair.</summary>
    public static async Task<ObjectIndexResult> GenerateAsync(
        IDumpService dump, EngineState engineState, Stream output, int dumperBuild, string classDumpFile,
        IProgress<DumpProgress>? progress = null, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        await using var writer = new StreamWriter(output, new UTF8Encoding(false), bufferSize: 64 * 1024, leaveOpen: true)
        {
            NewLine = "\n",
            AutoFlush = false,
        };

        var sb = new StringBuilder(512);
        sb.Append("{\"kind\":\"meta\",\"file\":\"objects\"");
        sb.Append(CultureInfo.InvariantCulture, $",\"ue_version\":{engineState.UEVersion}");
        AppendJsonString(sb, ",\"module\":", engineState.ModuleName ?? "");
        AppendJsonString(sb, ",\"pe_hash\":", engineState.PeHash ?? "");
        sb.Append(CultureInfo.InvariantCulture, $",\"object_count\":{engineState.ObjectCount}");
        sb.Append(CultureInfo.InvariantCulture, $",\"dumper_build\":{dumperBuild}");
        AppendJsonString(sb, ",\"class_dump\":", classDumpFile ?? "");
        AppendJsonString(sb, ",\"dumped_at\":", DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture));
        sb.Append('}');
        await writer.WriteLineAsync(sb.ToString().AsMemory(), ct);

        int written = 0, total = 0;
        bool indexMissing = false;
        int offset = 0;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var page = await dump.GetObjectIndexPageAsync(offset, Constants.GObjectsWalkPageSize, ct);
            total = page.Total;
            foreach (var o in page.Objects)
            {
                sb.Clear();
                AppendObjectLine(sb, o);
                await writer.WriteLineAsync(sb.ToString().AsMemory(), ct);
                written++;
                if (o.Index is null) indexMissing = true;
            }
            int advanced = page.Scanned > 0 ? page.Scanned : page.Objects.Count;
            offset += advanced;
            progress?.Report(new DumpProgress(Phase: "Writing the object index", Done: Math.Min(offset, total),
                                              Total: total));
            if (advanced == 0 || offset >= total) break;
        }

        sb.Clear();
        sb.Append("{\"kind\":\"summary\"");
        sb.Append(CultureInfo.InvariantCulture, $",\"objects_written\":{written}");
        sb.Append(CultureInfo.InvariantCulture, $",\"objects_total\":{total}");
        sb.Append(",\"index_missing\":").Append(indexMissing ? "true" : "false");
        sb.Append('}');
        await writer.WriteLineAsync(sb.ToString().AsMemory(), ct);
        await writer.FlushAsync(ct);
        return new ObjectIndexResult(written, total, indexMissing);
    }

    /// <summary>The meta and summary lines' share of an estimate: small next to the pool, and roughly fixed.</summary>
    private const int EnvelopeBytes = 400;

    private static void AppendObjectLine(StringBuilder sb, UObjectNode o)
    {
        sb.Append("{\"kind\":\"object\"");
        if (o.Index is int index)
            sb.Append(CultureInfo.InvariantCulture, $",\"index\":{index}");
        AppendJsonString(sb, ",\"addr\":", o.Address);
        AppendJsonString(sb, ",\"name\":", o.Name);
        AppendJsonString(sb, ",\"class\":", o.ClassName);
        AppendJsonString(sb, ",\"outer\":", o.OuterAddr);
        AppendJsonString(sb, ",\"path\":", o.FullPath);
        sb.Append('}');
    }

    private static void AppendJsonString(StringBuilder sb, string prefix, string value)
    {
        sb.Append(prefix).Append('"').Append(JsonEncodedText.Encode(value ?? "").ToString()).Append('"');
    }
}

/// <summary>What the object index will roughly cost: objects (estimated from the first page's rows per slot),
/// the pool's slot count, file bytes, time to write.</summary>
public sealed record ObjectIndexEstimate(int Objects, int Slots, long Bytes, TimeSpan Duration);

/// <summary>What the object index wrote. <see cref="IndexMissing"/>: the DLL sent no GObjects index for some
/// object (a DLL older than build 3625).</summary>
public sealed record ObjectIndexResult(int Written, int Total, bool IndexMissing);
