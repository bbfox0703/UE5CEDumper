using UE5DumpUI.Models;

namespace UE5DumpUI.Core;

/// <summary>
/// Business logic service for interacting with the UE5 Dumper DLL via pipe.
/// </summary>
public interface IDumpService
{
    Task<EngineState> InitAsync(CancellationToken ct = default);
    Task<EngineState> GetPointersAsync(CancellationToken ct = default);

    /// <summary>
    /// Set or clear the user UE version override for the current game.
    /// version=0 clears the override; non-zero sets it. The override persists in the
    /// HintCache JSON file (per game) and survives game restarts. Returns the updated
    /// EngineState (re-fetched after the override took effect).
    /// </summary>
    Task<EngineState> SetUeVersionOverrideAsync(int version, bool persist = true, CancellationToken ct = default);

    /// <summary>
    /// Set or clear the per-game GameThreadDispatch invoke timeout in milliseconds.
    /// timeoutMs=0 clears the override (revert to Stark::kDefaultInvokeTimeoutMs = 5000ms).
    /// Persisted in the same HintCache JSON keyed by PE hash; ResetAllCache wipes it
    /// alongside everything else. Returns the updated EngineState.
    /// </summary>
    Task<EngineState> SetInvokeTimeoutAsync(int timeoutMs, bool persist = true, CancellationToken ct = default);

    Task<int> GetObjectCountAsync(CancellationToken ct = default);
    /// <summary>
    /// Paginate the GObjects pool. <paramref name="includePath"/> requests the
    /// DLL emit each object's full path (Ubel::GetFullName) as <c>full_path</c>,
    /// surfaced on <see cref="UObjectNode.FullPath"/>. Off by default so the hot
    /// Object Tree paginate stays lean (a path string per object is ~19 MB over
    /// 486K objects); only DumpAllService's GameOnly pass sets it, to skip
    /// engine-package classes before walking them. Placed after <c>ct</c> to keep
    /// the existing 3-arg call sites unchanged.
    /// </summary>
    Task<ObjectListResult> GetObjectListAsync(int offset, int limit, CancellationToken ct = default, bool includePath = false);
    Task<ObjectDetail> GetObjectAsync(string addr, CancellationToken ct = default);
    Task<ObjectDetail> FindObjectAsync(string path, CancellationToken ct = default);
    /// <summary>Server-side keyword search over all objects. <paramref name="query"/> is
    /// space=AND (each term matches object name OR class name); <paramref name="instancesOnly"/>
    /// hides the reflection/type layer. Result <c>Truncated</c> flags a hit cap.</summary>
    Task<ObjectListResult> SearchObjectsAsync(string query, int limit = 200, bool instancesOnly = false, CancellationToken ct = default);
    Task<ClassInfoModel> WalkClassAsync(string addr, CancellationToken ct = default);

    /// <summary>
    /// Batched class schema walk — drops N pipe round-trips down to one
    /// for callers that need to walk many classes (Full SDK export,
    /// Dump All Metadata stream). Each returned element is byte-
    /// identical to a single <see cref="WalkClassAsync"/> call: the DLL
    /// implementation is a trivial loop over <c>Ubel::WalkClassEx</c>
    /// and the wire encoding is the same JSON shape as walk_class's
    /// "class" field, wrapped in a "classes" array.
    ///
    /// Result count equals the input count, in order. Empty / invalid
    /// addresses still emit a row (mirrors the single-call behaviour
    /// where WalkClassEx on a bad address returns an empty ClassInfo).
    /// Caller should chunk to keep pipe payloads bounded (~200 addrs
    /// per call is a safe default).
    /// </summary>
    Task<List<ClassInfoModel>> WalkClassesBatchAsync(string[] addrs, CancellationToken ct = default);
    Task<byte[]> ReadMemAsync(string addr, int size, CancellationToken ct = default);
    Task WriteMemAsync(string addr, byte[] data, CancellationToken ct = default);
    Task WatchAsync(string addr, int size, int intervalMs, CancellationToken ct = default);
    Task UnwatchAsync(string addr, CancellationToken ct = default);

    // --- Live Data Walker ---
    /// <param name="lean">Ask the DLL to omit the keys a CE XML export never reads
    /// (per-instance header, every decoded VALUE — <c>hex</c> / <c>value</c> /
    /// <c>str_value</c> / element hex …). Measured at ~24-38% of the payload
    /// (multipipe-eval.md §10.6), and it costs the UI parse too, so batching's
    /// residual payload-proportional IPC shrinks with it. <b>Only for the CE XML
    /// export path</b> — CSX and the Live Walker grid DO read those keys. Older
    /// DLLs ignore the flag and return the full shape, which is still correct.</param>
    Task<InstanceWalkResult> WalkInstanceAsync(string addr, string? classAddr = null, int arrayLimit = 64, int previewLimit = 2, bool fillGaps = false, bool lean = false, CancellationToken ct = default);

    /// <summary>
    /// Walk N instances in as few round-trips as possible (chunked at ~200).
    ///
    /// <para><b>Why it exists (measured, multipipe-eval.md §10.4):</b> a Copy CE XML
    /// issued <b>20,357</b> single <c>walk_instance</c> calls, and the cost split as
    /// dll 30% / <b>ipc 59-73%</b> / ui ~0%. Per call the round-trip overhead
    /// (0.16-0.21 ms) is roughly TWICE the actual walk (0.08 ms) — so collapsing the
    /// calls, not changing the dispatch model, is the lever.</para>
    ///
    /// <para>Results are positionally aligned with <paramref name="items"/>. A chunk
    /// that fails for any reason — including an older DLL that does not know the
    /// command — is replayed as single calls, so behaviour degrades rather than
    /// losing data.</para>
    /// </summary>
    /// <param name="lean">See <see cref="WalkInstanceAsync"/> — same contract, and
    /// the same "CE XML export only" restriction.</param>
    Task<IReadOnlyList<InstanceWalkResult>> WalkInstanceBatchAsync(
        IReadOnlyList<(string Addr, string? ClassAddr)> items,
        int arrayLimit = 64, int previewLimit = 2, bool fillGaps = false,
        bool lean = false, CancellationToken ct = default);
    Task<WorldWalkResult> WalkWorldAsync(int actorLimit = 200, int arrayLimit = 64, CancellationToken ct = default);
    // newestFirst: scan GObjects from the high (most-recently-allocated) end so
    // the newest runtime spawns survive the limit cap (catch a just-spawned
    // enemy). Default low->high keeps the oldest matches (CDO / class-default /
    // earliest instances — good for finding a Blueprint's template/defaults).
    // nameFilter: optional case-insensitive substring on the OBJECT name; ANDed
    // with the class query. Either query may be empty (class-only, name-only, or
    // both), but not both — the DLL scans all GObjects so name search isn't
    // bounded by the client-side result cap.
    // excludeClasses: server-side class-noise filter — the DLL skips these classes
    // (EXACT, case-sensitive) BEFORE the result cap, so a wanted instance past the
    // cap survives once the noise classes ahead of it are excluded. The response
    // carries a full-pool class histogram (ClassHistogram / ClassDistinct) that
    // includes excluded classes so the picker can still untick them.
    Task<FindInstancesResult> FindInstancesAsync(string className, bool exactMatch = false, int limit = 500, bool newestFirst = false, string nameFilter = "", IReadOnlyList<string>? excludeClasses = null, CancellationToken ct = default);
    Task<CePointerInfo> GetCePointerInfoAsync(string addr, int fieldOffset = 0, CancellationToken ct = default);

    /// <summary>
    /// Calibrate / force-enable the UE5.7+ *** UNVERIFIED *** packed FUObjectItem reconstruction
    /// at runtime (no DLL rebuild). Pass alignBits&lt;=0 / ptrMaskBits==0 / serialOff&lt;0 to leave
    /// a field unchanged; force=true switches the live layout to packed unconditionally. Returns
    /// the resulting layout state plus reconstructed object samples for eyeball calibration.
    /// </summary>
    Task<PackedConstsResult> SetPackedConstsAsync(int alignBits = 0, ulong ptrMaskBits = 0, bool force = false, int serialOff = -1, CancellationToken ct = default);

    // --- DataTable Row Browsing ---
    Task<DataTableWalkResult> WalkDataTableRowsAsync(string addr, int offset = 0, int limit = 64, CancellationToken ct = default);

    // --- Array Element Reading (Phase B) ---
    Task<ArrayElementsResult> ReadArrayElementsAsync(
        string instanceAddr, int fieldOffset,
        string innerAddr, string innerType, int elemSize,
        int offset = 0, int limit = 64, CancellationToken ct = default);

    // --- Address-to-Instance Reverse Lookup ---
    // containerElemCap: per-container element probe cap for the recursive deep
    // container scan (the fallback that finds values in separately-allocated
    // nested containers). Higher = deeper coverage, slower.
    Task<AddressLookupResult> FindByAddressAsync(string addr, int containerElemCap = 256, CancellationToken ct = default);

    // --- Reverse Reference Search (logical-owner navigation) ---
    Task<FindReferencesResult> FindReferencesToUObjectAsync(
        string addr, int maxResults = 32, CancellationToken ct = default);

    // --- Forward Object-Graph Path Search ("Locate in GWorld" / "in GameEngine") ---
    // Compute the shortest pointer chain from a root down to a target (a UObject,
    // or a property value whose owning object is passed via objectAddr). Used by
    // Live Walker to replace its breadcrumb spine and land on the target.
    // rootKind selects the BFS root: "gworld" (default — the live UWorld) or
    // "engine" (the live UGameEngine; reaches engine-layer objects the GWorld
    // graph never touches, but NOT a superset — world actors are typically
    // not_reachable from the engine).
    // deep: opt-in deep traversal — the forward BFS also follows object pointers
    // inside one struct-element container level (TArray<FStruct> etc.), reaching
    // objects referenced only from a struct-array element (Value Search "Deep"
    // analogue). containerDepth (>1) lets the DLL attribute a bare value address
    // in a deeply-nested heap container to its owning object via the deep
    // container scan (otherwise "invalid_target"). Both heavier; default off.
    Task<GWorldPathResult> FindPathFromGWorldAsync(
        string target, string? objectAddr = null, int maxDepth = 5,
        CancellationToken ct = default, string rootKind = "gworld",
        bool deep = false, int containerDepth = 1);

    // --- Related-object graph (forward, owned) — "Related Objects" panel ---
    // Given a UObject (typically an actor), list itself, its class/outer, its
    // Controller<->Pawn counterpart, and the sub-objects it OWNS (components, and
    // for GAS games the AbilitySystemComponent -> its UAttributeSets). The fast
    // forward view; the reverse "who references this object" is
    // FindReferencesToUObjectAsync.
    Task<RelatedObjectsResult> GetRelatedObjectsAsync(
        string addr, int maxResults = 128, CancellationToken ct = default);

    // --- Current-target auto-detect (Related Objects Phase 2, Edel) ---
    // Resolve GWorld -> PlayerController -> Pawn and return a ranked list of
    // candidate "current target" actors (best-first). The top candidate is the
    // auto-pick; the chain diagnostics say where detection stopped on failure.
    Task<CurrentTargetResult> DetectCurrentTargetAsync(
        int maxCandidates = 8, CancellationToken ct = default);

    // --- Live Walker "Start from GameEngine" ---
    // Resolve the live GEngine object (by reflected GameViewport member, not by
    // class name) so the Live Walker can root on it. Found=false when no live
    // engine exists yet (e.g. pre-init).
    Task<GameEngineResult> ResolveGameEngineAsync(CancellationToken ct = default);

    // --- Property Bytecode Cross-Reference ("which methods use this field?") ---
    // Static Kismet-bytecode scan; Blueprint/script functions only (native
    // functions have empty bytecode and are invisible).
    Task<FindPropertyXrefsResult> FindPropertyXrefsAsync(
        string propAddr, bool gameOnly = true, int maxResults = 200,
        CancellationToken ct = default);

    // --- Class-level reflection xref ("which functions take this class?") ---
    // Reflection scan over every UFunction's parameter chain; finds functions
    // declaring `classAddr` as a direct param/return. Unlike the per-field
    // bytecode scan above, this ALSO catches native functions. Reuses the
    // FindPropertyXrefsResult shape (Kind = "param"/"return").
    Task<FindPropertyXrefsResult> FindFunctionsByClassAsync(
        string classAddr, bool gameOnly = true, int maxResults = 200,
        CancellationToken ct = default);

    // Resolve a UFunction's native code entry point (UFunction->Func) — the .text
    // address for the xref dialog's "Disassemble in CE" push. Returns "" if the
    // offset isn't detected or the slot isn't a code pointer.
    Task<string> GetFunctionCodeAddrAsync(string funcAddr, CancellationToken ct = default);

    // Reverse edge: the properties a single UFunction reads/writes.
    Task<FunctionPropRefsResult> WalkFunctionPropsAsync(
        string funcAddr, CancellationToken ct = default);

    // --- Enum Enumeration ---
    Task<List<EnumDefinition>> ListEnumsAsync(CancellationToken ct = default);

    // --- Function Walking (for SDK export) ---
    Task<List<FunctionInfoModel>> WalkFunctionsAsync(string addr, CancellationToken ct = default);

    // --- Property Keyword Search ---
    // deep: opt-in descent into nested struct + struct-typed container element
    // schemas so a field like SaveSlotList[].MsTuneData.GP is findable by name.
    // Default off — the shallow direct-field search is unchanged.
    Task<PropertySearchResult> SearchPropertiesAsync(
        string query, string[]? types = null, bool gameOnly = true,
        bool deep = false, int limit = 200, CancellationToken ct = default);

    /// <summary>
    /// Batched property search — DLL walks GObjects once and checks
    /// every property against every query. Drops the multi-keyword
    /// sweep time from ~42s (sequential pipe calls each re-walking
    /// GObjects) to ~1.5s for a 36-query / 4400-class game. Used by
    /// the Interesting Properties tab Load command.
    ///
    /// Each query gets its own dedup index + maxResults limit, returned
    /// in order inside <see cref="PropertySearchBatchResult.PerQuery"/>.
    /// Preview values are NOT resolved on the batch path (the tab
    /// doesn't display them; user opens a row in Live Walker to read
    /// the live value).
    /// </summary>
    Task<PropertySearchBatchResult> SearchPropertiesBatchAsync(
        string[] queries, string[]? types = null, bool gameOnly = true,
        int limitPerQuery = 200, CancellationToken ct = default);

    // --- Game Class List ---
    Task<ClassListResult> ListClassesAsync(
        bool gameOnly = true, int limit = 5000, CancellationToken ct = default);

    // --- Class-noise auto-detect (class-filter Phase 3) ---
    // Classify class names as engine/system "noise" (safe-by-construction:
    // engine package OR super-chain to a pure-engine leaf base). Backs the opt-in
    // "auto-detect system classes" pre-tick in the class-noise picker.
    Task<IReadOnlyList<NoiseClassInfo>> DetectNoiseClassesAsync(
        IReadOnlyList<string> classNames, CancellationToken ct = default);

    // --- Value Search (CE-style First Scan + Next Scan) ---
    //
    // Walks GObjects + UProperty metadata for every UPROPERTY field
    // matching `dataType` across all UObject instances, applying the
    // scan predicate. Returns enriched candidates + a session id for
    // follow-up RefineValueScanAsync calls.
    //
    // For ValueScanType.Between, both `value` and `value2` must be
    // populated. For ValueScanType.Exact/Bigger/Smaller `value2` is
    // ignored. Prev-value predicates (Changed/Unchanged/Increased/
    // Decreased) are NOT valid for the first scan — caller must use
    // RefineValueScanAsync for those.
    //
    // Native C++ fields (non-UPROPERTY) are not REFLECTED, so they are invisible
    // to the default walk — but they are reachable: pass `nativeC: true` (18 lines
    // below) and the DLL additionally scans each object's unmanaged holes (the byte
    // ranges inside the object that no property covers) via the Guess-What
    // heuristic, numeric types only. The UI's Value Search tab has BOTH banners —
    // str.VS.Banner names the limitation and points at the Native-C toggle,
    // str.VS.NativeBanner describes the scan once it is on. Saying "NOT visible to
    // this scan" full stop predated the Native-C work and had this interface
    // contradicting its own parameter list. (audit #5 AE31)
    // V3-C: the DLL session OWNS the full candidate set; begin/refine return
    // `Total` (full count) plus only the FIRST PAGE (`pageSize`, scan order).
    // The UI is a windowed view that pages / filters / sorts server-side via
    // QueryCandidatesAsync.
    Task<ValueScanBeginResult> BeginValueScanAsync(
        ValueScanDataType dataType,
        ValueScanType scanType,
        string value,
        string? value2 = null,
        bool gameOnly = true,
        int maxResults = Constants.ScanSessionMaxResults,
        Models.FloatRoundMode roundMode = Models.FloatRoundMode.Round,
        bool caseSensitive = false,
        bool parallel = true,
        bool batchRead = true,
        bool deep = false,
        bool nativeC = false,
        bool newestFirst = false,
        int pageSize = Constants.ScanSessionPageSize,
        int deadlineMs = Constants.ScanSessionDeadlineMs,
        bool autoSkipNoise = false,
        CancellationToken ct = default);

    Task<ValueScanRefineResult> RefineValueScanAsync(
        ulong sessionId,
        ValueScanType scanType,
        string? value = null,
        string? value2 = null,
        Models.FloatRoundMode roundMode = Models.FloatRoundMode.Round,
        bool caseSensitive = false,
        int pageSize = Constants.ScanSessionPageSize,
        CancellationToken ct = default);

    // V3-C: server-side window over the session's full candidate set. Filters
    // (case-insensitive substring across the displayed columns) + sorts
    // (sortKey / sortDesc) over the WHOLE set in the DLL and returns only
    // [offset, offset+limit). sortKey wire strings: "" / "scan" / "addr" /
    // "value" / "class" / "field" / "instance" / "type" / "offset" / "index".
    Task<ValueScanWindowResult> QueryCandidatesAsync(
        ulong sessionId,
        int offset,
        int limit,
        string? filter = null,
        string? sortKey = null,
        bool sortDesc = false,
        IReadOnlyList<string>? excludeClasses = null,
        CancellationToken ct = default);

    Task EndValueScanAsync(ulong sessionId, CancellationToken ct = default);

    // --- Multiple values group scan (build 1276) ---
    // Find objects holding ALL of N values (2..4) at distinct numeric-property
    // offsets. Object-level candidates with nested per-slot matches; otherwise
    // the same windowed session lifecycle as the single-value scan.
    Task<GroupScanBeginResult> BeginGroupScanAsync(
        IReadOnlyList<GroupSlotInput> slots,
        bool gameOnly = true,
        int maxResults = Constants.ScanSessionMaxResults,
        bool deep = false,
        bool crossObject = false,
        bool nativeC = false,
        bool newestFirst = false,
        int pageSize = Constants.ScanSessionPageSize,
        int deadlineMs = Constants.ScanSessionDeadlineMs,
        bool autoSkipNoise = false,
        Models.FloatRoundMode roundMode = Models.FloatRoundMode.Round,
        int perSlotCap = Constants.GroupPerSlotCap,
        CancellationToken ct = default);

    Task<GroupScanRefineResult> RefineGroupScanAsync(
        ulong sessionId,
        IReadOnlyList<GroupSlotInput> slots,
        int pageSize = Constants.ScanSessionPageSize,
        Models.FloatRoundMode roundMode = Models.FloatRoundMode.Round,
        CancellationToken ct = default);

    Task<GroupScanWindowResult> QueryGroupCandidatesAsync(
        ulong sessionId,
        int offset,
        int limit,
        string? filter = null,
        string? sortKey = null,
        bool sortDesc = false,
        IReadOnlyList<string>? excludeClasses = null,
        CancellationToken ct = default);

    /// <summary>Every leaf ONE slot of ONE group candidate kept, BY NAME. A results
    /// row can only display one assignment out of the many an object may satisfy;
    /// this is how the rest become visible and actionable. Fetched on demand for an
    /// expanded row — a page of candidates carries far too many leaves to inline.</summary>
    Task<IReadOnlyList<GroupSlotMatch>> QueryGroupSlotLeavesAsync(
        ulong sessionId,
        GroupSlotMatch slot,
        string instanceAddr,
        string className,
        int offset = 0,
        int limit = 0,
        CancellationToken ct = default);

    Task EndGroupScanAsync(ulong sessionId, CancellationToken ct = default);

    // --- Snapshot Capture (experimental — Phase A) ---
    // Stateless cursor pagination (like get_object_list): begin returns the
    // total object count for progress; each chunk streams [offset, offset+limit)
    // objects with their numeric UPROPERTY values. Advance offset by the
    // returned Scanned, NOT Objects.Count. dataType is a multi-numeric meta
    // type ("NumericNoByte" / "NumericAll").
    Task<int> BeginSnapshotAsync(string dataType, CancellationToken ct = default);

    // numericFamily ("Any" / "IntegersOnly" / "FloatsOnly") is an orthogonal
    // type narrowing applied on top of dataType: keep integer leaves, float
    // leaves, or both — cuts the snapshot DB at the source for type-specific hunts.
    Task<SnapshotChunkResult> SnapshotChunkAsync(
        string dataType, bool gameOnly, int offset, int limit,
        bool nativeC = false, bool autoSkipNoise = true,
        string numericFamily = "Any", CancellationToken ct = default);

    // --- All Functions Enumeration (Interesting Functions Finder) ---
    Task<AllFunctionsResult> ListAllFunctionsAsync(
        bool gameOnly = true, int limit = 100000, CancellationToken ct = default);

    // --- Live ProcessEvent Profiler (Live Funcs) ---
    // Start recording per-UFunction fire counts (forces the game-thread PE hook
    // to install). Returns hook-active + a reason string when it isn't.
    Task<PeProfileStartResult> PeProfileStartAsync(CancellationToken ct = default);
    // Stop recording (idempotent). Counts are retained for a subsequent get.
    Task PeProfileStopAsync(CancellationToken ct = default);
    // Fetch the ranked fire-count table (top <paramref name="limit"/> by count).
    Task<PeProfileResult> PeProfileGetAsync(int limit = 200, CancellationToken ct = default);

    /// <summary>
    /// Fetch a <c>get_diagnostics</c> snapshot: how long each pipe command has
    /// occupied the DLL's dispatcher, plus Win32 process facts and game-thread
    /// health. Read-only and safe to poll.
    /// </summary>
    Task<DiagnosticsResult> GetDiagnosticsAsync(int limit = 25, CancellationToken ct = default);

    /// <summary>Clear the dispatch counters and restart the uptime clock, so a
    /// measurement can be scoped to one deliberate action.</summary>
    Task ResetDiagnosticsAsync(CancellationToken ct = default);

    // --- Extra Scan (user-triggered aggressive fallback) ---
    Task<RescanStartResult> StartRescanAsync(CancellationToken ct = default);
    Task<RescanStatusResult> GetRescanStatusAsync(CancellationToken ct = default);
    Task<EngineState> ApplyRescanAsync(CancellationToken ct = default);

    // --- Trigger Scan (proxy DLL deferred scan) ---
    /// <summary>
    /// Start async AOB scan. Used when proxy DLL starts without scanning.
    /// Returns immediately — poll progress with GetScanStatusAsync().
    /// Also safe to call in CE/manual mode — UE5_Init is idempotent.
    /// </summary>
    Task TriggerScanAsync(CancellationToken ct = default);

    /// <summary>
    /// Poll scan progress after TriggerScanAsync(). Returns phase, status text,
    /// and full EngineState when scan is complete (phase >= 7).
    /// </summary>
    Task<ScanStatusResult> GetScanStatusAsync(CancellationToken ct = default);

    // --- UFunction Invocation via Pipe ---

    /// <summary>
    /// Invoke a UFunction via ProcessEvent through the pipe.
    /// The DLL executes ProcessEvent in-process, bypassing CE's executeCodeEx.
    /// Works even when games block CreateRemoteThread.
    /// </summary>
    /// <param name="funcName">UFunction name to invoke.</param>
    /// <param name="instanceAddr">Hex address of target UObject instance (optional if className provided).</param>
    /// <param name="className">Class name to auto-resolve instance (optional if instanceAddr provided).</param>
    /// <param name="parmsSize">Total parameter buffer size from UFunction.</param>
    /// <param name="paramsHex">Hex-encoded param bytes (optional).</param>
    /// <param name="directCall">When true, force the DLL-side UE5_CallProcessEventDirect path
    ///     (bypass GameThreadDispatch). Caller asserts the function is FUNC_Native|FUNC_Static
    ///     (e.g. KismetMathLibrary helpers) — required by the System tab Self-Test which must
    ///     succeed on idle main-menu / loading screens where the game thread isn't pumping.</param>
    /// <param name="stringParams">String INPUT params the DLL must build as by-value FStrings
    ///     (their 16-byte slots stay zeroed in <paramref name="paramsHex"/>). See
    ///     <see cref="InvokeStringParam"/>. Null / empty when the function takes no string args.</param>
    Task<InvokeFunctionResult> InvokeFunctionAsync(
        string funcName,
        string? instanceAddr = null,
        string? className = null,
        int parmsSize = 0,
        string? paramsHex = null,
        bool directCall = false,
        IReadOnlyList<InvokeStringParam>? stringParams = null,
        CancellationToken ct = default);

    /// <summary>
    /// Read the live Debug Camera state (DLL-side two-hop reflection read of
    /// DebugCameraController.OriginalControllerRef). 1 = ON, 0 = OFF,
    /// -1 = unknown / no live CheatManager.
    /// </summary>
    Task<int> GetDebugCameraStateAsync(CancellationToken ct = default);

    /// <summary>
    /// Force Debug Camera ON (<paramref name="enable"/>=true) or OFF. The DLL
    /// reads state, toggles only when needed, and on a disable that the game's
    /// stripped ToggleDebugCamera can't honour, switches the local player's
    /// controller back to the original PlayerController. Returns the resulting
    /// state (1 = ON, 0 = OFF, -1 = error).
    /// </summary>
    Task<int> SetDebugCameraAsync(bool enable, CancellationToken ct = default);

    // === God Mode (Solitar: force AActor.bCanBeDamaged) ===

    /// <summary>
    /// Read the live God Mode state — the DLL observes the local pawn's
    /// AActor.bCanBeDamaged bit. 1 = immune (ON), 0 = can be damaged (OFF),
    /// negative = no pawn / unresolvable.
    /// </summary>
    Task<int> GetGodModeAsync(CancellationToken ct = default);

    /// <summary>
    /// Force God Mode on/off. ON ⇒ bCanBeDamaged forced FALSE on the local pawn
    /// and re-asserted on a timer (survives respawns). Returns the observed live
    /// state (1/0) or a negative Solitar::ProtectResult.
    /// </summary>
    Task<int> SetGodModeAsync(bool enable, CancellationToken ct = default);

    // === Foreground lock (Grausam: keep the game thread alive when unfocused) ===

    /// <summary>
    /// Read the foreground-lock state. Returns 1 = on, 0 = off.
    /// </summary>
    Task<int> GetForegroundLockAsync(CancellationToken ct = default);

    /// <summary>
    /// Enable/disable the foreground lock. ON ⇒ the DLL hooks GetForegroundWindow so
    /// the game always believes it is the foreground app, defeating UE's
    /// t.IdleWhenNotForeground idle / focus-loss pause (keeps ProcessEvent invokes and
    /// POV reads working while our UI or CE holds the foreground). Returns 1 = on,
    /// 0 = off, negative = hook-install error.
    /// </summary>
    Task<int> SetForegroundLockAsync(bool enable, CancellationToken ct = default);

    // === Movement tuning (Laufen: force per-pawn CMC float knobs) ===

    /// <summary>
    /// Read all movement knobs (walk speed / gravity / jump) on the current
    /// pawn's CharacterMovement: live value, captured base, multiplier, active
    /// state, and each field's owner+offset for the "Locate in GWorld" handoff.
    /// <see cref="MovementParams.HasCmc"/> is false on pawns with no CMC.
    /// </summary>
    Task<MovementParams> GetMovementParamsAsync(CancellationToken ct = default);

    /// <summary>
    /// Fetch the decomposed *GWorld->Pawn offset chain + per-field offsets baked
    /// into a no-DLL standalone CE-Lua trainer (see project-standalone-ce-lua-trainer).
    /// Call during normal gameplay (a live pawn). <see cref="TrainerOffsets.Code"/>
    /// is non-zero when the chain/pawn can't be resolved.
    /// </summary>
    Task<TrainerOffsets> GetTrainerOffsetsAsync(CancellationToken ct = default);

    /// <summary>
    /// Force a movement knob (<paramref name="knob"/> = "walk_speed" | "gravity"
    /// | "jump") to its captured base × <paramref name="multiplier"/> and hold it
    /// with a re-assert worker (survives respawns / per-tick overwrites).
    /// Returns the observed result (State 1 = active, negative = no pawn / no CMC).
    /// </summary>
    Task<MovementSetResult> SetMovementMultiplierAsync(string knob, double multiplier, CancellationToken ct = default);

    /// <summary>
    /// Disable a movement knob — restore its captured base and stop holding it.
    /// Returns the post-reset snapshot of that knob.
    /// </summary>
    Task<MovementSetResult> ResetMovementAsync(string knob, CancellationToken ct = default);

    /// <summary>
    /// Set the pawn's gravity DIRECTION (UE5.4+ GravityDirection) to (x,y,z)
    /// (normalized DLL-side, held by the re-assert worker). (0,0,0) = OFF (restore
    /// the captured default). State 1 = active, 0 = off, negative = not reflected
    /// (pre-5.4) / no pawn.
    /// </summary>
    Task<MovementVectorResult> SetGravityDirectionAsync(double x, double y, double z, CancellationToken ct = default);

    /// <summary>Restore gravity direction to its captured default.</summary>
    Task<MovementVectorResult> ResetGravityDirectionAsync(CancellationToken ct = default);

    // === Time dilation (Hemmung: global slow-mo / freeze / speed-up) ===

    /// <summary>
    /// Read both time-dilation levers (Hemmung): global
    /// <c>AWorldSettings::TimeDilation</c> (whole-world speed) and per-pawn
    /// <c>AActor::CustomTimeDilation</c> — live value, captured natural base, held
    /// value, active state, and each owner+offset for the "Locate in GWorld" handoff.
    /// </summary>
    Task<TimeState> GetTimeStateAsync(CancellationToken ct = default);

    /// <summary>
    /// Read the FULL God Mode state (Solitar): what the user asked for, what the
    /// pawn's <c>bCanBeDamaged</c> actually reads, and whether a canonical target
    /// was resolvable at all.
    ///
    /// <para>The single tri-state <c>get_god_mode</c> collapses those three into
    /// one number, so it cannot distinguish "GodMode was never enabled" from "the
    /// hold is engaged but there is no pawn to write to yet" (audit #5 AD4).</para>
    /// </summary>
    Task<ProtectState> GetProtectStateAsync(CancellationToken ct = default);

    /// <summary>
    /// Hold a dilation lever (<paramref name="target"/> = "global" | "pawn") at the
    /// absolute <paramref name="value"/> (1.0 = normal, 0.5 = half, 0 = frozen; clamped
    /// DLL-side to [0,100]) with a re-assert worker that fights the game's per-tick
    /// overwrites. Returns the observed result (State 1 = active, negative = no
    /// WorldSettings / no pawn / not reflected).
    /// </summary>
    Task<TimeDilationSetResult> SetTimeDilationAsync(string target, double value, CancellationToken ct = default);

    /// <summary>
    /// Disable a dilation lever — restore its captured natural value and stop
    /// holding it. Returns the post-reset snapshot of that lever.
    /// </summary>
    Task<TimeDilationSetResult> ResetTimeDilationAsync(string target, CancellationToken ct = default);

    // === Fly (Dunste: no-gravity keyboard-driven 3D flight) ===

    /// <summary>Apply whichever of {enable, speed, preset, noclip} are non-null and
    /// return the live fly state. Input (WASD/numpad/arrows) is sampled DLL-side;
    /// this only toggles + configures. enable=true forces MOVE_Flying + starts the
    /// worker; enable=false restores the captured MovementMode + stops it. noclip
    /// switches position-drive (fly through walls) vs velocity (collision).</summary>
    Task<FlyStatus> FlySetAsync(bool? enable, double? speed, int? preset, bool? noclip, CancellationToken ct = default);

    /// <summary>Poll the live fly state (active / preset / speed / MovementMode).</summary>
    Task<FlyStatus> FlyGetStateAsync(CancellationToken ct = default);

    // === See-through occluders (Schlacht) ===

    /// <summary>Apply whichever of {enable, count} are non-null and return the live
    /// status. When on, each tick traces the camera→view ray and hides the nearest
    /// <paramref name="count"/> non-Pawn occluders (SetActorHiddenInGame);
    /// Pawns/Characters (enemies/NPCs/player) are never hidden. enable=false un-hides
    /// everything and stops the worker; count sets the pierce depth (>=1).</summary>
    Task<SeeThroughStatus> SeeThroughSetAsync(bool? enable, int? count, CancellationToken ct = default);

    /// <summary>Poll the live see-through status (active / has-target / hidden count).</summary>
    Task<SeeThroughStatus> SeeThroughGetStateAsync(CancellationToken ct = default);

    // === Force-field hold + stealth meter (Solide) ===

    /// <summary>
    /// Force a discovered reflected field to a value on ALL live instances of
    /// <paramref name="className"/> and hold it via a re-assert worker.
    /// <paramref name="kind"/> = "bool" (uses <paramref name="on"/>) | "object_null"
    /// (value ignored) | "numeric" (uses <paramref name="value"/>). Returns the live
    /// "N held" instance count (0 = the class/field matched nothing — a no-op signal).
    /// </summary>
    Task<ForceFieldResult> ForceFieldAsync(string className, string fieldName, string kind, double value = 0, bool on = false, CancellationToken ct = default);

    /// <summary>Remove a hold-job (best-effort restore the captured base on live
    /// instances; object-null is not reversible). Returns the DLL code (0 = OK).</summary>
    Task<int> ResetFieldAsync(string className, string fieldName, CancellationToken ct = default);

    /// <summary>Remove ALL hold-jobs (best-effort restore each). Returns 0 on OK.</summary>
    Task<int> ResetAllFieldsAsync(CancellationToken ct = default);

    /// <summary>Snapshot the active hold-jobs + their live held counts.</summary>
    Task<IReadOnlyList<ForcedFieldInfo>> GetForcedFieldsAsync(CancellationToken ct = default);

    /// <summary>
    /// Auto-find the player's stealth/noise/visibility/detection meter: resolve the
    /// local pawn + its owned components, keyword-score every reflected numeric field,
    /// return the ranked candidates (best first). Read-only. Empty list = none found.
    /// </summary>
    Task<IReadOnlyList<StealthCandidate>> FindStealthMeterAsync(int max = 8, CancellationToken ct = default);

    // === Teleport (Wirbel: marker save/recall + cursor teleport) ===
    // docs/teleport-spec.md §7. Model Code/Codes carry the DLL's Wirbel
    // result code (0 = OK, negatives mapped by TeleportCodes).

    /// <summary>Read the current pawn pose (location + control rotation + map).</summary>
    Task<TeleportPose> TeleportGetPoseAsync(CancellationToken ct = default);

    /// <summary>Save the current pose into marker slot 0..2.</summary>
    Task<TeleportPose> TeleportSaveMarkerAsync(int slot, CancellationToken ct = default);

    /// <summary>Recall to marker slot 0..2. Refused on map mismatch unless
    /// <paramref name="force"/>.</summary>
    Task<TeleportResult> TeleportRecallMarkerAsync(int slot, bool force, CancellationToken ct = default);

    /// <summary>Recall to an explicit pose (BugItGo interop). Bypasses the
    /// marker store and the map check. Rotation restored only when supplied.</summary>
    Task<TeleportResult> TeleportRecallExplicitAsync(double x, double y, double z,
        double? pitch = null, double? yaw = null, double? roll = null,
        CancellationToken ct = default);

    /// <summary>Teleport to the world position under the mouse cursor (or the
    /// screen center when <paramref name="fallbackCenter"/> and no cursor).</summary>
    Task<TeleportResult> TeleportToCursorAsync(double zOffset, int channel,
        bool fallbackCenter, CancellationToken ct = default);

    /// <summary>Read all marker slots. The response also carries the system
    /// "last" slot as a sentinel entry with <see cref="TeleportMarker.Slot"/> ==
    /// -1 (the pose auto-saved before the most recent jump).</summary>
    Task<List<TeleportMarker>> TeleportGetMarkersAsync(CancellationToken ct = default);

    /// <summary>Clear marker slot 0..2.</summary>
    Task<int> TeleportClearMarkerAsync(int slot, CancellationToken ct = default);

    /// <summary>Recall the system "last" pose — the position auto-saved DLL-side
    /// right before the most recent recall / force / BugItGo / cursor teleport,
    /// so a teleport that went wrong can be undone. One-way restore (the slot is
    /// never overwritten by this call); map check is skipped.</summary>
    Task<TeleportResult> TeleportRecallLastAsync(CancellationToken ct = default);

    /// <summary>Read the camera POV (read-only): the on-screen camera's world
    /// location, rotation and FOV via APlayerCameraManager, plus a best-effort
    /// pawn location for the camera↔pawn delta. There is no Set POV — the view
    /// is recomputed every tick (see <see cref="TeleportPov"/>).</summary>
    Task<TeleportPov> TeleportGetPovAsync(CancellationToken ct = default);

    /// <summary>Teleport along the pawn's facing direction by <paramref name="distance"/>
    /// unreal units (negative = backward). <paramref name="horizontal"/> keeps Z
    /// (ground-plane move); when false the full 3D forward (including pitch) is
    /// used. Returns the resulting pose so the caller can show the landed
    /// X/Y/Z/Pitch/Yaw. The pre-jump pose is auto-saved (RecallLast undoes it).</summary>
    Task<TeleportPose> TeleportRelativeAsync(double distance, bool horizontal,
        CancellationToken ct = default);

    /// <summary>Force the mouse cursor on (<paramref name="show"/>=true) / off by
    /// writing the local PlayerController's bShowMouseCursor. Returns the resulting
    /// state: 1 = on, 0 = off, -1 = error/unresolved.</summary>
    Task<int> SetMouseCursorAsync(bool show, CancellationToken ct = default);

    /// <summary>Read the current bShowMouseCursor state: 1 = on, 0 = off,
    /// -1 = error/unresolved.</summary>
    Task<int> GetMouseCursorAsync(CancellationToken ct = default);
}
