using System.Text;
using System.Text.Json;
using UE5DumpUI.Core;
using UE5DumpUI.Models;

namespace UE5DumpUI.Services;

/// <summary>
/// Streams a full classes + properties + functions dump as JSON Lines
/// (one JSON object per line). Used as input for offline analysis —
/// Python scripts under <c>scripts/analysis/</c> aggregate across
/// multiple game dumps to inform keyword tables, class-location
/// bonuses, and threshold calibration. Replaces hand-curated guesses
/// with empirically-grounded patterns.
///
/// All work is orchestrated client-side via the existing pipe
/// endpoints (<c>get_object_list</c> + <c>walk_class</c> +
/// <c>walk_functions</c>); no new DLL command is required. The
/// trade-off is per-class round-trips — ~30-60 seconds for a
/// 3-5k-class game — vs. zero DLL maintenance burden.
///
/// **BPGC inclusion**: unlike <c>SearchProperties</c> (build 671
/// `IsClassLikeMeta` fix) the dumper accepts every class-flavoured
/// metaclass (Class + BlueprintGeneratedClass + AnimBPGC +
/// WidgetBPGC + DynamicClass) so game-specific BP classes — where
/// almost all cheat-relevant properties live — are NOT dropped.
/// </summary>
public static class DumpAllService
{
    /// <summary>
    /// Class-flavoured metaclass names accepted by the dumper. Mirrors
    /// <c>Aura::IsClassLikeMeta</c> on the DLL side — keep in sync if
    /// new UClass subclasses appear in future UE versions.
    /// </summary>
    private static readonly HashSet<string> ClassLikeMetas = new(StringComparer.Ordinal)
    {
        "Class",
        "BlueprintGeneratedClass",
        "AnimBlueprintGeneratedClass",
        "WidgetBlueprintGeneratedClass",
        "DynamicClass",
    };

    /// <summary>
    /// Default chunk size for the batched walk_class fan-out. Internal
    /// so tests can hit chunk-boundary edge cases at (n-1)/n/(n+1)
    /// counts. Matches <see cref="SdkExportService.FullSdkBatchChunkSize"/>.
    /// </summary>
    internal const int WalkClassBatchChunkSize = 200;

    /// <summary>
    /// Engine-package path prefixes — kept in sync with the DLL's
    /// <c>Aura::IsEnginePackage</c> (dll/src/Aura.h) so the client-side GameOnly skip
    /// matches what the DLL treats as an engine package. Prefixes have NO trailing
    /// separator: <see cref="IsEnginePath"/> checks the terminator (<c>/</c>, <c>.</c>,
    /// or end-of-string) explicitly, and first collapses <c>Ubel::GetFullName</c>'s
    /// double-leading-slash form.
    /// </summary>
    private static readonly string[] EnginePathPrefixes =
    {
        "/Script/Engine", "/Script/CoreUObject", "/Script/CoreOnline",
        "/Script/UMG", "/Script/Slate", "/Script/SlateCore", "/Script/InputCore",
        "/Script/EnhancedInput", "/Script/PhysicsCore", "/Script/NavigationSystem",
        "/Script/AIModule", "/Script/Niagara", "/Script/Paper2D",
        "/Script/CinematicCamera", "/Script/GameplayCameras", "/Script/MovieScene",
        "/Script/LevelSequence", "/Script/Landscape", "/Script/Foliage",
        "/Script/AnimGraphRuntime", "/Script/AudioMixer", "/Script/ChaosCloth",
        "/Script/ChaosSolverEngine", "/Script/ClothingSystemRuntimeNv",
        "/Script/GeometryCollectionEngine", "/Script/FieldSystemEngine",
        "/Script/ProceduralMeshComponent", "/Script/GameplayTags",
        "/Script/GameplayTasks", "/Script/GameplayAbilities", "/Script/PacketHandler",
        "/Script/PropertyAccess", "/Script/DeveloperSettings", "/Script/AssetRegistry",
        "/Script/MediaAssets", "/Script/HeadMountedDisplay",
    };

    /// <summary>
    /// Generate a JSON-Lines dump and stream it to
    /// <paramref name="output"/>. Output schema (one object per line):
    /// <list type="bullet">
    ///   <item><c>{"kind":"meta", ...}</c> — first line; UE version,
    ///     module, object count, build, options.</item>
    ///   <item><c>{"kind":"class", "name":..., "props":[...],
    ///     "funcs":[...]}</c> — one per class-like object.</item>
    ///   <item><c>{"kind":"error", "addr":..., "msg":...}</c> — when a
    ///     specific class walk fails; iteration continues.</item>
    ///   <item><c>{"kind":"summary", ...}</c> — last line; counters.</item>
    /// </list>
    /// Returns a <see cref="DumpResult"/> carrying the same counters the
    /// summary line reports, so the caller can compose an honest completion
    /// message from what the dump actually produced (classes emitted / errors)
    /// rather than from the output file's byte length (audit X4).
    /// </summary>
    public static async Task<DumpResult> GenerateAsync(
        IDumpService dump,
        EngineState engineState,
        Stream output,
        DumpOptions? options = null,
        IProgress<DumpProgress>? progress = null,
        CancellationToken ct = default)
    {
        options ??= new DumpOptions();

        await using var writer = new StreamWriter(output, new UTF8Encoding(false), bufferSize: 64 * 1024, leaveOpen: true)
        {
            NewLine = "\n",
            AutoFlush = false,
        };

        // ----- Meta line -----
        await WriteMetaLineAsync(writer, engineState, options, ct);

        // ----- Pass 1 (optional): count live instances per class name -----
        Dictionary<string, int>? instanceCounts = null;
        int total = 0;
        if (options.IncludeInstanceCounts)
        {
            instanceCounts = new Dictionary<string, int>(StringComparer.Ordinal);
            int offset = 0;
            const int pageSize = Constants.GObjectsWalkPageSize;
            do
            {
                ct.ThrowIfCancellationRequested();
                var page = await dump.GetObjectListAsync(offset, pageSize, ct);
                total = page.Total;
                foreach (var obj in page.Objects)
                {
                    if (string.IsNullOrEmpty(obj.ClassName)) continue;
                    instanceCounts.TryGetValue(obj.ClassName, out var c);
                    instanceCounts[obj.ClassName] = c + 1;
                }
                offset += page.Scanned > 0 ? page.Scanned : page.Objects.Count;
                progress?.Report(new DumpProgress(
                    Phase: "Counting instances",
                    Done: offset,
                    Total: total));
                if (offset >= total) break;
            } while (true);
        }

        // ----- Pass 2: walk every class-like object -----
        //
        // Classes are walked in chunks of WalkClassBatchChunkSize via
        // walk_class_batch (build 693). The DLL batch is a trivial loop
        // over Ubel::WalkClassEx — same function the single walk_class
        // path uses — so each batch element is byte-identical to a
        // single-call result. WalkFunctions stays single-call per
        // emitted class for now (separate optimisation candidate).
        // Any batch failure (pipe exception, unexpected element count)
        // is caught and the chunk is replayed via single WalkClassAsync
        // calls so per-class error attribution is preserved.
        int classesEmitted = 0;
        int classesSkipped = 0;
        int errors = 0;
        int scannedObjects = 0;
        {
            int offset = 0;
            const int pageSize = Constants.GObjectsWalkPageSize;
            var chunkBuffer = new List<UObjectNode>(WalkClassBatchChunkSize);

            do
            {
                ct.ThrowIfCancellationRequested();
                // Request per-object full paths only when GameOnly needs them for the
                // pre-walk engine-package skip below — otherwise stay on the lean path.
                var page = await dump.GetObjectListAsync(offset, pageSize, ct, includePath: options.GameOnly);
                total = page.Total;

                foreach (var obj in page.Objects)
                {
                    scannedObjects++;
                    if (!ClassLikeMetas.Contains(obj.ClassName))
                    {
                        continue;
                    }
                    // Pre-walk GameOnly skip: with include_path=true the object list now
                    // carries obj.FullPath (normally == the walked classInfo.FullPath), so
                    // engine classes are dropped BEFORE the walk_class round-trip. The
                    // post-walk skip in FlushClassChunkAsync stays as the authoritative
                    // backstop: it still fires when obj.FullPath is unavailable (a caller
                    // that didn't request include_path) or diverges from the walked path
                    // (the object's outer chain changed between the two pipe calls). An
                    // empty obj.FullPath simply doesn't match here (IsEnginePath("")==false),
                    // so it falls through to be walked and re-checked on classInfo.FullPath.
                    if (options.GameOnly && IsEnginePath(obj.FullPath))
                    {
                        classesSkipped++;
                        continue;
                    }

                    chunkBuffer.Add(obj);
                    if (chunkBuffer.Count >= WalkClassBatchChunkSize)
                    {
                        var (emitted, errs, skipped) = await FlushClassChunkAsync(
                            dump, writer, chunkBuffer, instanceCounts, options,
                            classesEmitted, progress, ct);
                        classesEmitted += emitted;
                        errors         += errs;
                        classesSkipped += skipped;
                        chunkBuffer.Clear();
                    }
                }

                offset += page.Scanned > 0 ? page.Scanned : page.Objects.Count;
                if (offset >= total) break;
            } while (true);

            // Final partial chunk.
            if (chunkBuffer.Count > 0)
            {
                var (emitted, errs, skipped) = await FlushClassChunkAsync(
                    dump, writer, chunkBuffer, instanceCounts, options,
                    classesEmitted, progress, ct);
                classesEmitted += emitted;
                errors         += errs;
                classesSkipped += skipped;
                chunkBuffer.Clear();
            }
        }

        // ----- Summary line -----
        await WriteSummaryLineAsync(writer, classesEmitted, classesSkipped, errors, scannedObjects, ct);
        await writer.FlushAsync();

        progress?.Report(new DumpProgress(
            Phase: $"Done — {classesEmitted} classes",
            Done: classesEmitted,
            Total: classesEmitted));

        return new DumpResult(classesEmitted, classesSkipped, errors, scannedObjects);
    }

    /// <summary>
    /// Walk a chunk of class objects and emit one class line per entry.
    /// Tries the batched walk_class_batch path first; on any failure
    /// (pipe exception, unexpected result count) falls back to single
    /// WalkClassAsync calls so per-class error attribution survives.
    /// WalkFunctions is invoked single-call per emitted class — that
    /// half of the per-class round-trip cost is a separate batching
    /// candidate (build 693 only batches walk_class).
    ///
    /// Returns (emittedDelta, errorsDelta, skippedDelta) so the caller can
    /// maintain the cumulative <c>classesEmitted</c> / <c>errors</c> /
    /// <c>classesSkipped</c> counters the summary line reports. The GameOnly
    /// engine-package skip is applied HERE (post-walk) because the walked
    /// <c>classInfo.FullPath</c> is the only reliable package path — the
    /// object-list <c>obj.FullPath</c> is always empty (get_object_list carries
    /// no path).
    /// </summary>
    private static async Task<(int emitted, int errors, int skipped)> FlushClassChunkAsync(
        IDumpService dump,
        TextWriter writer,
        List<UObjectNode> chunk,
        Dictionary<string, int>? instanceCounts,
        DumpOptions options,
        int cumulativeEmittedBefore,
        IProgress<DumpProgress>? progress,
        CancellationToken ct)
    {
        if (chunk.Count == 0) return (0, 0, 0);

        // Try the batched path first.
        var addrs = new string[chunk.Count];
        for (int i = 0; i < chunk.Count; i++) addrs[i] = chunk[i].Address;

        List<ClassInfoModel>? batchResult = null;
        try
        {
            var fetched = await dump.WalkClassesBatchAsync(addrs, ct);
            if (fetched.Count == chunk.Count)
                batchResult = fetched;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // batchResult stays null → fallback below preserves
            // per-class error rows (kind=error) the way the pre-batch
            // path did.
        }

        int emittedThisChunk = 0;
        int errorsThisChunk = 0;
        int skippedThisChunk = 0;

        for (int i = 0; i < chunk.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var obj = chunk[i];
            try
            {
                var classInfo = batchResult != null
                    ? batchResult[i]
                    : await dump.WalkClassAsync(obj.Address, ct);

                // GameOnly engine-package skip — applied on the walked path (the
                // reliable one) rather than obj.FullPath (always empty). Skips
                // BEFORE WalkFunctions so an engine class costs no extra round-trip.
                if (options.GameOnly && IsEnginePath(classInfo.FullPath))
                {
                    skippedThisChunk++;
                    continue;
                }

                List<FunctionInfoModel>? functions = null;
                if (options.IncludeFunctions)
                {
                    functions = await dump.WalkFunctionsAsync(obj.Address, ct);
                }

                int instCount = 0;
                if (instanceCounts != null)
                {
                    instanceCounts.TryGetValue(classInfo.Name, out instCount);
                }

                await WriteClassLineAsync(writer, obj, classInfo, functions, instCount, ct);
                emittedThisChunk++;

                // Preserves the pre-batch path's per-50-class progress
                // granularity, measured against the cumulative emit
                // count (not the chunk-local count). Matches old
                // behaviour exactly so progress messages line up.
                int cumulative = cumulativeEmittedBefore + emittedThisChunk;
                if ((cumulative % 50) == 0)
                {
                    progress?.Report(new DumpProgress(
                        Phase: "Walking classes",
                        Done: cumulative,
                        Total: -1));
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                errorsThisChunk++;
                await WriteErrorLineAsync(writer, obj.Address, obj.Name, ex.Message, ct);
            }
        }

        return (emittedThisChunk, errorsThisChunk, skippedThisChunk);
    }

    // ------------------------------------------------------------------
    // Internal: writers
    // ------------------------------------------------------------------

    private static async Task WriteMetaLineAsync(
        TextWriter w, EngineState es, DumpOptions opts, CancellationToken ct)
    {
        var sb = new StringBuilder(512);
        sb.Append("{\"kind\":\"meta\"");
        sb.Append(",\"ue_version\":").Append(es.UEVersion);
        sb.Append(",\"ue_version_detected\":").Append(es.VersionDetected ? "true" : "false");
        sb.Append(",\"is_user_override\":").Append(es.IsUserOverride ? "true" : "false");
        AppendJsonString(sb, ",\"module\":", es.ModuleName ?? "");
        AppendJsonString(sb, ",\"module_base\":", es.ModuleBase ?? "");
        AppendJsonString(sb, ",\"gobjects\":", es.GObjectsAddr ?? "");
        AppendJsonString(sb, ",\"gnames\":",   es.GNamesAddr ?? "");
        AppendJsonString(sb, ",\"gworld\":",   es.GWorldAddr ?? "");
        sb.Append(",\"object_count\":").Append(es.ObjectCount);
        // FUObjectItem layout — lets offline analysis flag UE5.7+ (un)packed dumps.
        // "packed57" addresses are UNVERIFIED reconstructions (no shipping game uses
        // it yet), so a dump captured under it must be treated as best-effort.
        AppendJsonString(sb, ",\"item_layout\":", es.ItemLayoutMode);
        sb.Append(",\"item_obj_offset\":").Append(es.ItemObjOffset);
        sb.Append(",\"packed_unverified\":").Append(es.ItemPacked ? "true" : "false");
        AppendJsonString(sb, ",\"pe_hash\":", es.PeHash ?? "");
        AppendJsonString(sb, ",\"publisher_thumbprint\":", es.PublisherThumbprint ?? "");
        sb.Append(",\"dumper_build\":").Append(opts.DumperBuildNumber);
        AppendJsonString(sb, ",\"dumper_commit\":", opts.DumperCommit ?? "");
        AppendJsonString(sb, ",\"dumped_at\":", DateTime.UtcNow.ToString("o"));
        sb.Append(",\"options\":{");
        sb.Append("\"game_only\":").Append(opts.GameOnly ? "true" : "false");
        sb.Append(",\"include_functions\":").Append(opts.IncludeFunctions ? "true" : "false");
        sb.Append(",\"include_instance_counts\":").Append(opts.IncludeInstanceCounts ? "true" : "false");
        sb.Append("}}");
        await w.WriteLineAsync(sb.ToString().AsMemory(), ct);
    }

    private static async Task WriteClassLineAsync(
        TextWriter w,
        UObjectNode obj,
        ClassInfoModel classInfo,
        List<FunctionInfoModel>? functions,
        int instanceCount,
        CancellationToken ct)
    {
        var sb = new StringBuilder(classInfo.Fields.Count * 100 + 256);
        sb.Append("{\"kind\":\"class\"");
        AppendJsonString(sb, ",\"name\":", classInfo.Name);
        AppendJsonString(sb, ",\"addr\":", obj.Address);
        AppendJsonString(sb, ",\"path\":", classInfo.FullPath);
        AppendJsonString(sb, ",\"meta\":", obj.ClassName);  // "Class" or "BlueprintGeneratedClass" etc.
        AppendJsonString(sb, ",\"super\":", classInfo.SuperName);
        AppendJsonString(sb, ",\"super_addr\":", classInfo.SuperAddress);
        sb.Append(",\"is_bpgc\":").Append(obj.ClassName != "Class" ? "true" : "false");
        sb.Append(",\"props_size\":").Append(classInfo.PropertiesSize);
        sb.Append(",\"instance_count\":").Append(instanceCount);
        sb.Append(",\"props\":[");
        for (int i = 0; i < classInfo.Fields.Count; i++)
        {
            if (i > 0) sb.Append(',');
            var f = classInfo.Fields[i];
            sb.Append('{');
            AppendJsonString(sb, "\"name\":", f.Name);
            AppendJsonString(sb, ",\"type\":", f.TypeName);
            sb.Append(",\"offset\":").Append(f.Offset);
            sb.Append(",\"size\":").Append(f.Size);
            // Reflection flags + static-array dim (auto-detect features).
            // prop_flags as an "0x" hex string — CPF_* is uint64 with high
            // bits set. Both omitted at defaults (flags 0 / dim 1).
            if (f.PropertyFlags != 0)
                sb.Append(",\"prop_flags\":\"0x").Append(f.PropertyFlags.ToString("X")).Append('"');
            if (f.ArrayDim != 1)
                sb.Append(",\"array_dim\":").Append(f.ArrayDim);
            if (!string.IsNullOrEmpty(f.StructType))
                AppendJsonString(sb, ",\"struct_type\":", f.StructType);
            if (!string.IsNullOrEmpty(f.InnerType))
                AppendJsonString(sb, ",\"inner_type\":", f.InnerType);
            if (!string.IsNullOrEmpty(f.InnerStructType))
                AppendJsonString(sb, ",\"inner_struct_type\":", f.InnerStructType);
            if (!string.IsNullOrEmpty(f.ObjClassName))
                AppendJsonString(sb, ",\"obj_class\":", f.ObjClassName);
            if (!string.IsNullOrEmpty(f.EnumName))
                AppendJsonString(sb, ",\"enum\":", f.EnumName);
            sb.Append('}');
        }
        sb.Append(']');

        if (functions != null)
        {
            sb.Append(",\"funcs\":[");
            for (int i = 0; i < functions.Count; i++)
            {
                if (i > 0) sb.Append(',');
                var fn = functions[i];
                sb.Append('{');
                AppendJsonString(sb, "\"name\":", fn.Name);
                AppendJsonString(sb, ",\"addr\":", fn.Address);
                AppendJsonString(sb, ",\"return_type\":", fn.ReturnType);
                sb.Append(",\"num_parms\":").Append(fn.NumParms);
                sb.Append(",\"parms_size\":").Append(fn.ParmsSize);
                sb.Append(",\"flags\":\"0x").Append(fn.FunctionFlags.ToString("X")).Append('"');
                sb.Append('}');
            }
            sb.Append(']');
        }
        sb.Append('}');
        await w.WriteLineAsync(sb.ToString().AsMemory(), ct);
    }

    private static async Task WriteErrorLineAsync(
        TextWriter w, string addr, string name, string msg, CancellationToken ct)
    {
        var sb = new StringBuilder(256);
        sb.Append("{\"kind\":\"error\"");
        AppendJsonString(sb, ",\"addr\":", addr);
        AppendJsonString(sb, ",\"name\":", name);
        AppendJsonString(sb, ",\"msg\":", msg);
        sb.Append('}');
        await w.WriteLineAsync(sb.ToString().AsMemory(), ct);
    }

    private static async Task WriteSummaryLineAsync(
        TextWriter w, int emitted, int skipped, int errors, int scanned, CancellationToken ct)
    {
        var sb = new StringBuilder(128);
        sb.Append("{\"kind\":\"summary\"");
        sb.Append(",\"classes_emitted\":").Append(emitted);
        sb.Append(",\"classes_skipped_engine\":").Append(skipped);
        sb.Append(",\"errors\":").Append(errors);
        sb.Append(",\"objects_scanned\":").Append(scanned);
        sb.Append('}');
        await w.WriteLineAsync(sb.ToString().AsMemory(), ct);
    }

    /// <summary>
    /// JSON-escape <paramref name="value"/> and append <paramref name="prefix"/>
    /// + the encoded string to <paramref name="sb"/>. Uses
    /// <see cref="JsonEncodedText"/> so the escape rules match
    /// System.Text.Json exactly (covers control chars, quotes, backslash,
    /// non-BMP UTF-16 correctly).
    /// </summary>
    private static void AppendJsonString(StringBuilder sb, string prefix, string value)
    {
        sb.Append(prefix);
        sb.Append('"');
        sb.Append(JsonEncodedText.Encode(value ?? "").ToString());
        sb.Append('"');
    }

    /// <summary>Check whether <paramref name="fullPath"/> belongs to one of the known
    /// engine packages. Case-sensitive (UE paths are case-sensitive by convention).
    /// Faithful C# port of the DLL's <c>Aura::IsEnginePackage</c> (dll/src/Aura.h):
    /// <c>Ubel::GetFullName</c> emits engine paths as <c>//Script/Engine/Class</c> — a
    /// DOUBLE leading slash with <c>/</c> separators (dot is used only for sub-objects
    /// at depth &gt; 2, never for a top-level class). A naive <c>StartsWith("/Script/Engine.")</c>
    /// never matched that real wire form, silently making GameOnly a no-op. So collapse
    /// the leading-slash run to a single <c>/</c>, then require the prefix to be followed
    /// by end-of-string, <c>/</c>, or <c>.</c>.</summary>
    internal static bool IsEnginePath(string fullPath)
    {
        if (string.IsNullOrEmpty(fullPath)) return false;

        int firstNonSlash = 0;
        while (firstNonSlash < fullPath.Length && fullPath[firstNonSlash] == '/') firstNonSlash++;
        if (firstNonSlash >= fullPath.Length) return false;   // empty / all slashes
        string path = "/" + fullPath.Substring(firstNonSlash);

        foreach (var prefix in EnginePathPrefixes)
        {
            if (path.StartsWith(prefix, StringComparison.Ordinal) &&
                (path.Length == prefix.Length || path[prefix.Length] == '/' || path[prefix.Length] == '.'))
                return true;
        }
        return false;
    }

    /// <summary>Whitelist check — exposed for tests.</summary>
    internal static bool IsClassLikeMetaName(string meta) => ClassLikeMetas.Contains(meta);
}

/// <summary>What a dump run actually produced — the same counters the trailing
/// <c>{"kind":"summary"}</c> line reports. Returned by
/// <see cref="DumpAllService.GenerateAsync"/> so callers can report success
/// (and its scale) from what happened, not from the file's byte length.</summary>
public sealed record DumpResult(
    int ClassesEmitted, int ClassesSkippedEngine, int Errors, int ObjectsScanned);

/// <summary>Options controlling what the dumper emits.</summary>
public sealed record DumpOptions(
    bool GameOnly = false,
    bool IncludeFunctions = true,
    bool IncludeInstanceCounts = true,
    int DumperBuildNumber = 0,
    string? DumperCommit = null);

/// <summary>Progress payload — Done/Total of -1 means "indeterminate".</summary>
public sealed record DumpProgress(string Phase, int Done, int Total);
