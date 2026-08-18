using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Reflection;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UE5DumpUI.Core;
using UE5DumpUI.Helpers;
using UE5DumpUI.Models;
using UE5DumpUI.Services;

namespace UE5DumpUI.ViewModels;

/// <summary>
/// ViewModel for the Global Pointers panel.
/// </summary>
public partial class PointerPanelViewModel : ViewModelBase
{
    private readonly IPlatformService _platform;
    private readonly IDumpService? _dump;
    private readonly ILoggingService? _log;
    private readonly IAobMakerBridge? _aobMaker;
    private readonly AobUsageService? _aobUsage;
    private readonly IExperimentalGate? _experimentalGate;
    private readonly ISnapshotStore? _snapshotStore;
    private readonly IPipeClient? _pipe;
    private readonly ILogCompressionService? _logCompression;

    /// <summary>
    /// Compress archived logs older than <see cref="Constants.LogAutoCompressMinAgeDays"/>
    /// days at every startup. Persisted (System section of ui-options.json), <b>default
    /// OFF</b> — opt-in, because a launch should not rewrite the user's files unasked.
    /// The startup pass itself runs from <c>App</c>; this property only records the
    /// intent, because the sweep begins before this ViewModel exists.
    /// </summary>
    [ObservableProperty] private bool _autoCompressLogs;

    /// <summary>False on a non-NTFS log volume — the button and checkbox hide rather than
    /// offering something that can only ever report "not supported".</summary>
    [ObservableProperty] private bool _logCompressionSupported;

    [ObservableProperty] private bool _isCompressingLogs;

    [ObservableProperty] private string _gObjectsAddress = "";
    [ObservableProperty] private string _gNamesAddress = "";
    [ObservableProperty] private string _gWorldAddress = "";
    [ObservableProperty] private string _sparseDelegatesAddress = "";
    [ObservableProperty] private int _ueVersion;
    [ObservableProperty] private bool _versionDetected = true;
    [ObservableProperty] private bool _isUserOverride;
    [ObservableProperty] private bool _isLowConfidence;
    // Default true so a DLL that predates get_offsets shows no banner — see EngineState.
    [ObservableProperty] private bool _offsetsValidated = true;
    [ObservableProperty] private bool _offsetsProbeRan;
    [ObservableProperty] private string _offsetsFallbackReason = "";
    [ObservableProperty] private bool _isVersionTooOld;
    [ObservableProperty] private string _publisherThumbprint = "";
    [ObservableProperty] private string _selectedUeVersionOverride = "Auto";
    [ObservableProperty] private bool _isApplyingOverride;
    /// <summary>
    /// Per-game GameThreadDispatch invoke timeout (ms). 5000 is the Stark default;
    /// any value other than 5000 indicates a user override active for this game.
    /// </summary>
    [ObservableProperty] private int _invokeTimeoutMs = Constants.StarkDefaultInvokeTimeoutMs;
    [ObservableProperty] private bool _isApplyingInvokeTimeout;
    [ObservableProperty] private int _totalObjects;
    [ObservableProperty] private bool _hasData;

    // Scan method for each pointer: "aob", "data_scan", "string_ref", "pointer_scan", "not_found"
    [ObservableProperty] private string _gObjectsMethod = "aob";
    [ObservableProperty] private string _gNamesMethod = "aob";
    [ObservableProperty] private string _gWorldMethod = "aob";
    [ObservableProperty] private string _sparseDelegatesMethod = "not_found";

    // Pattern IDs: which AOB pattern won the scan (e.g. "GOBJ_V1")
    [ObservableProperty] private string _gObjectsPatternId = "";
    [ObservableProperty] private string _gNamesPatternId = "";
    [ObservableProperty] private string _gWorldPatternId = "";
    [ObservableProperty] private string _sparseDelegatesPatternId = "";

    // AOB scan hit addresses (instruction that references the pointer)
    [ObservableProperty] private string _gObjectsScanAddr = "";
    [ObservableProperty] private string _gNamesScanAddr = "";
    [ObservableProperty] private string _gWorldScanAddr = "";
    [ObservableProperty] private string _sparseDelegatesScanAddr = "";

    // Per-target scan statistics (for red/green indicator)
    [ObservableProperty] private int _gObjectsPatternsHit;
    [ObservableProperty] private int _gNamesPatternsHit;
    [ObservableProperty] private int _gWorldPatternsHit;

    // --- GWorld AOB metadata (for CreateSymbolScript) ---
    [ObservableProperty] private string _gworldAob = "";
    [ObservableProperty] private int _gworldAobPos;
    [ObservableProperty] private int _gworldAobLen;
    [ObservableProperty] private string _moduleName = "";

    // --- GEngine (&GEngine slot) + its AOB metadata, same contract as GWorld's ---
    [ObservableProperty] private string _gEngineAddress = "";
    [ObservableProperty] private string _gEngineMethod = "not_found";
    [ObservableProperty] private string _gEnginePatternId = "";
    [ObservableProperty] private string _gEngineScanAddr = "";
    [ObservableProperty] private string _gengineAob = "";
    [ObservableProperty] private int _gengineAobPos;
    [ObservableProperty] private int _gengineAobLen;

    // --- AOBMaker CE Plugin bridge ---
    [ObservableProperty] private bool _isAobMakerAvailable;

    // --- Extra Scan state ---
    [ObservableProperty] private bool _isScanning;
    [ObservableProperty] private string _scanStatusText = "";
    [ObservableProperty] private bool _scanComplete;
    [ObservableProperty] private string _scanResultText = "";

    // --- Cache management ---
    [ObservableProperty] private string _peHash = "";
    [ObservableProperty] private string _cacheStatusText = "";

    // --- Maintenance (open log folder / remove all snapshot data) ---
    [ObservableProperty] private string _maintenanceStatusText = "";

    // --- Pipe Activity log (System tab): live tail of UI<->DLL pipe traffic ---
    // Newest-first ring buffer (newest at top, so the latest line is always
    // visible at the default scroll position), fed by IPipeClient.Activity
    // (raised on pipe threads). Entries are coalesced onto the UI thread so a
    // burst (snapshot streaming, tree scroll) can't flood the dispatcher.
    private const int PipeLogCap = 200;
    /// <summary>The displayed lines (newest first), capped at <see cref="PipeLogCap"/>.</summary>
    public ObservableCollection<PipeLogEntry> PipeLog { get; } = new();
    /// <summary>When true, new pipe lines are dropped (the list freezes for reading).</summary>
    [ObservableProperty] private bool _pipeLogPaused;
    /// <summary>True while the log is empty — drives the placeholder text.</summary>
    public bool PipeLogIsEmpty => PipeLog.Count == 0;
    private readonly ConcurrentQueue<PipeLogEntry> _pipeLogPending = new();
    private int _pipeLogFlushScheduled;

    // --- Diagnostics: build version + Self-Test ---

    /// <summary>UI's own build number (compiled in via AssemblyVersion). Constant
    /// per UE5DumpUI binary, computed once from the entry assembly's version.
    ///
    /// Two traps stacked here:
    /// 1. Native AOT single-file publish trims <c>GetExecutingAssembly()</c> →
    ///    returns no version. Use <c>GetEntryAssembly()</c> instead (the same
    ///    fix <c>MainWindowViewModel.GetAppVersion</c> uses for the title bar).
    ///    See build 657 lesson.
    /// 2. Our AssemblyVersion is laid out as <c>Major.Minor.Patch.Build</c>
    ///    (e.g. 1.0.0.661) but .NET's <see cref="System.Version"/> names the
    ///    four parts <c>Major.Minor.Build.Revision</c> — so what we call "build"
    ///    sits in <see cref="System.Version.Revision"/>, not
    ///    <see cref="System.Version.Build"/>. Reading <c>.Build</c> returned the
    ///    patch digit (0), which is why the System tab Diagnostics showed
    ///    "UI build: 0" through builds 657–660 even after the AOT-trim fix.
    /// </summary>
    public int UiBuildNumber { get; } = ReadUiBuildNumber();

    private static int ReadUiBuildNumber()
    {
        // System.Version.Revision returns -1 if the version was constructed
        // with fewer than 4 components. Our csproj always sets 4, but guard
        // anyway so we never show "-1" in the UI badge.
        var rev = Assembly.GetEntryAssembly()?.GetName().Version?.Revision ?? 0;
        return rev > 0 ? rev : 0;
    }

    [ObservableProperty] private int _dllBuildNumber;

    /// <summary>True when the DLL reports the same build number as the UI.
    /// False means the DLL is stale (forgot to redeploy after rebuild) — common
    /// in proxy mode. ⚠ shown next to the version display.</summary>
    public bool BuildVersionsMatch => HasData && DllBuildNumber > 0 && DllBuildNumber == UiBuildNumber;

    /// <summary>True when DLL reports a build number AND it differs from UI's.</summary>
    public bool BuildVersionMismatch => HasData && DllBuildNumber > 0 && DllBuildNumber != UiBuildNumber;

    /// <summary>True when DLL is pre-build-653 and doesn't report build_number at all.</summary>
    public bool BuildVersionUnknown => HasData && DllBuildNumber == 0;

    /// <summary>Drives the GLOBAL stale-DLL badge in the main window top bar:
    /// true when the DLL is stale (mismatch) OR pre-dates the build probe
    /// (unknown). Surfaced from EVERY tab — not just Diagnostics — so a
    /// hand-deployed old proxy DLL is noticed before scanning with mismatched
    /// offsets (the "forgot where I deployed" trap).</summary>
    public bool ShowGlobalBuildWarning => BuildVersionMismatch || BuildVersionUnknown;

    /// <summary>Text for the global stale-DLL badge, with the actual build
    /// numbers when known.</summary>
    public string GlobalBuildWarningText =>
        BuildVersionMismatch
            ? $"⚠ DLL build {DllBuildNumber} ≠ UI {UiBuildNumber} — stale, redeploy (close game first)"
        : BuildVersionUnknown
            ? "⚠ DLL pre-dates the build probe — assume stale, redeploy"
        : "";

    // --- FUObjectItem layout (UE5.7+ packed-mode awareness) ---
    [ObservableProperty] private bool _itemPacked;
    [ObservableProperty] private string _itemLayoutMode = "classic";
    [ObservableProperty] private int _itemObjOffset;

    /// <summary>Drives the GLOBAL "unverified packed layout" badge in the top bar:
    /// true when connected to a game running the UE5.7+ *** UNVERIFIED *** packed
    /// FUObjectItem layout. Surfaced from every tab so the user knows reconstructed
    /// addresses and exports are best-effort.</summary>
    public bool ShowPackedLayoutBadge => HasData && ItemPacked;

    /// <summary>Text for the global unverified-packed-layout badge.</summary>
    public string PackedLayoutBadgeText =>
        "⚠ Unverified UE5.7+ packed layout — addresses best-effort";

    // --- Self-Test state ---
    [ObservableProperty] private bool _isSelfTesting;
    [ObservableProperty] private string _selfTestResultText = "";
    [ObservableProperty] private bool _selfTestPassed;
    [ObservableProperty] private bool _selfTestFailed;
    [ObservableProperty] private bool _selfTestHasResult;

    /// <summary>
    /// Legacy warning binding (kept for compatibility with existing axaml). True when
    /// version detection failed AND no user override is active. The new 3-state badge
    /// uses ShowLowConfidenceWarning / ShowUserOverrideBadge instead.
    /// </summary>
    public bool ShowVersionWarning => HasData && !VersionDetected && !IsUserOverride;

    /// <summary>True when ueVersion came from a user-set persistent override.</summary>
    public bool ShowUserOverrideBadge => HasData && IsUserOverride;

    /// <summary>True when detection succeeded but used the low-confidence Tier 3 / publisher-bias path.</summary>
    public bool ShowLowConfidenceWarning => HasData && IsLowConfidence && !IsUserOverride;

    /// <summary>
    /// True when the DLL says at least one FField/FProperty/UStruct offset is an unmeasured
    /// UE-version guess. Everything downstream — Live Walker values, every export, every
    /// Force/Freeze write — is derived from those offsets, and until this banner existed the
    /// DLL computed the verdict and nobody asked for it. (audit #5 U3/X3)
    /// <para>
    /// Suppressed under <see cref="IsVersionTooOld"/>: there the DLL deliberately skipped the
    /// whole scan, which the refusal banner already explains — a second warning saying the
    /// offsets are unmeasured would be true, redundant, and read as a separate fault.
    /// </para>
    /// </summary>
    public bool ShowOffsetsUnvalidatedWarning => HasData && !IsVersionTooOld && !OffsetsValidated;

    /// <summary>
    /// Why the offsets are not trustworthy. Splits on whether detection RAN, because the two
    /// have different remedies: a give-up mid-probe is a per-game layout problem worth
    /// reporting, while "never ran" means the walker is on pure defaults.
    /// </summary>
    public string OffsetsUnvalidatedText =>
        !OffsetsProbeRan
            ? "⚠ Offset detection never ran — the walker is using UE-version DEFAULTS. "
              + "Values, exports and freezes may all be wrong."
            : string.IsNullOrEmpty(OffsetsFallbackReason)
                ? "⚠ Dynamic offsets were not fully measured — some are UE-version defaults. "
                  + "Values below and every export derived from them may be wrong."
                : $"⚠ Dynamic offsets only partially measured ({OffsetsFallbackReason}) — the rest "
                  + "are UE-version defaults. Values below and every export derived from them may be wrong.";

    /// <summary>True when version was detected with high confidence and no override is in effect.</summary>
    public bool ShowVersionDetectedBadge => HasData && VersionDetected && !IsUserOverride && !IsLowConfidence;

    /// <summary>
    /// Sentinel UeVersion meaning "positively identified as pre-UE4 (UE3)" rather than
    /// "UE 4.0-4.10". Mirrors <c>Grimoire::PRE_UE4_SENTINEL_VERSION</c> in the DLL.
    /// </summary>
    public const int PreUE4SentinelVersion = 300;

    /// <summary>True when the engine is UE 4.0-4.10, so the DLL skipped the scan outright.
    /// Pre-4.11 has no FUObjectItem — the object array holds raw UObjectBase* at stride 8 in an
    /// INLINE chunk table, which the layout presets cannot express — so every pointer being
    /// empty is by design here, not a scan that failed. Shown instead of the usual
    /// "not found" text, which would send the user hunting for a pattern that cannot exist.
    /// Excludes the pre-UE4 sentinel, which gets its own message: telling a UE3 user to
    /// "set a UE version override" is advice that cannot work at any version.</summary>
    public bool ShowVersionTooOldWarning => HasData && IsVersionTooOld
        && UeVersion != PreUE4SentinelVersion;

    /// <summary>True when the engine was positively identified as pre-UE4 (Unreal Engine 3).
    /// A different object model rather than an older version of this one — no FUObjectArray,
    /// no FNamePool — so unlike every other warning here there is nothing to tune, and both
    /// the override and Extra Scan are disabled rather than merely useless. The discriminator
    /// is the version number already on the wire, so this needed no new pipe field.</summary>
    public bool ShowPreUE4Warning => HasData && IsVersionTooOld
        && UeVersion == PreUE4SentinelVersion;

    /// <summary>True when a SquareEnix (or future) publisher thumbprint was matched.</summary>
    public bool ShowPublisherHint => HasData && !string.IsNullOrEmpty(PublisherThumbprint);

    /// <summary>Human-readable publisher label (e.g. "SQUARE_ENIX" → "Square Enix").</summary>
    public string PublisherLabel => PublisherThumbprint switch
    {
        "SQUARE_ENIX" => "Square Enix",
        _ => PublisherThumbprint,
    };

    /// <summary>List of override choices for the ComboBox (display strings).</summary>
    public static System.Collections.Generic.IReadOnlyList<string> UeVersionOverrideOptions { get; } = new[]
    {
        "Auto",
        "UE 4.18", "UE 4.19", "UE 4.20", "UE 4.21", "UE 4.22", "UE 4.23",
        "UE 4.24", "UE 4.25", "UE 4.26", "UE 4.27",
        "UE 5.0", "UE 5.1", "UE 5.2", "UE 5.3", "UE 5.4",
        "UE 5.5", "UE 5.6", "UE 5.7", "UE 5.8",
    };

    // A REFUSED engine (IsVersionTooOld) silences every per-pointer failure line below. Nothing
    // was tried there — Genau's gate returns before the first pattern runs — so "AOB failed" and
    // "All AOB patterns failed" are not just noise, they are false, and they directly contradict
    // the banner that says every pointer is empty by design. The banner is the whole explanation.
    // Verified against a live UE3 run: without this the panel read "🔴 All AOB patterns failed" +
    // "⚠ AOB failed — found via not found" on all three pointers after a scan that never happened.

    /// <summary>
    /// Did the NORMAL signature scan resolve this pointer, as opposed to a fallback or a
    /// recovery path?
    ///
    /// <para>These predicates used to ask <c>method != "aob"</c>, which was the same question
    /// only for as long as a successful scan could report nothing but <c>"aob"</c>. It cannot:
    /// the signature tables also hold symbol exports and CallFollow entries, and those are the
    /// STRONGEST results available (priority 0, tried first, immune to a recompile shuffling
    /// bytes) — not a fallback and not a recovery. The DLL now labels them honestly, so the
    /// test has to be a membership check rather than an equality one. Measured on Satisfactory
    /// (UE 5.6), where GObjects, GNames, GWorld and GEngine ALL resolve by export: with the
    /// old test every one of them would have raised a "found via fallback" warning and GWorld
    /// would additionally have claimed to be "recovered".</para>
    ///
    /// <para>Recovery paths (<c>engine_recovery</c>, <c>instance_scan_recovery</c>) and
    /// <c>not_found</c> are deliberately absent — they are the cases these predicates exist to
    /// catch.</para>
    /// </summary>
    private static bool IsDirectScan(string? method) =>
        method is "aob" or "symbol" or "symbol_call_follow" or "call_follow";

    /// <summary>True when GObjects was found via fallback (not a direct scan hit).</summary>
    public bool ShowGObjectsWarning => HasData && !IsVersionTooOld && !IsDirectScan(GObjectsMethod);

    /// <summary>True when GNames was found via fallback (not a direct scan hit).</summary>
    public bool ShowGNamesWarning => HasData && !IsVersionTooOld && !IsDirectScan(GNamesMethod);

    /// <summary>True when GWorld was not found at all.</summary>
    public bool ShowGWorldWarning => HasData && !IsVersionTooOld && GWorldMethod == "not_found";

    /// <summary>True when GWorld was found via a recovery path (not a direct scan hit, and not
    /// missing) — e.g. instance_scan_recovery / engine_recovery. Surfaces the method so a
    /// recovered GWorld reads as such instead of looking like a normal scan hit.</summary>
    public bool ShowGWorldRecovered =>
        HasData && GWorldMethod != "not_found" && !IsDirectScan(GWorldMethod);

    /// <summary>True when ALL GObjects AOB patterns failed (0 hits). Never on a refused engine,
    /// where 0 hits means 0 patterns TRIED.</summary>
    public bool GObjectsAobAllFailed => HasData && !IsVersionTooOld && GObjectsPatternsHit == 0;

    /// <summary>True when ALL GNames AOB patterns failed (0 hits). See GObjectsAobAllFailed.</summary>
    public bool GNamesAobAllFailed => HasData && !IsVersionTooOld && GNamesPatternsHit == 0;

    /// <summary>True when ALL GWorld AOB patterns failed (0 hits). See GObjectsAobAllFailed.</summary>
    public bool GWorldAobAllFailed => HasData && !IsVersionTooOld && GWorldPatternsHit == 0;

    /// <summary>Formatted scan method label for GObjects.</summary>
    public string GObjectsMethodLabel => FormatMethodLabel(GObjectsMethod);

    /// <summary>Formatted scan method label for GNames.</summary>
    public string GNamesMethodLabel => FormatMethodLabel(GNamesMethod);

    /// <summary>Formatted scan method label for GWorld.</summary>
    public string GWorldMethodLabel => FormatMethodLabel(GWorldMethod);

    /// <summary>True when GObjects has a non-empty pattern ID to display.</summary>
    public bool HasGObjectsPatternId => HasData && !string.IsNullOrEmpty(GObjectsPatternId);

    /// <summary>True when GNames has a non-empty pattern ID to display.</summary>
    public bool HasGNamesPatternId => HasData && !string.IsNullOrEmpty(GNamesPatternId);

    /// <summary>True when GWorld has a non-empty pattern ID to display.</summary>
    public bool HasGWorldPatternId => HasData && !string.IsNullOrEmpty(GWorldPatternId);

    /// <summary>True when GObjects has a non-zero AOB scan address.</summary>
    public bool HasGObjectsScanAddr => HasData && IsNonZeroAddr(GObjectsScanAddr);
    /// <summary>True when GNames has a non-zero AOB scan address.</summary>
    public bool HasGNamesScanAddr => HasData && IsNonZeroAddr(GNamesScanAddr);
    /// <summary>True when GWorld has a non-zero AOB scan address.</summary>
    public bool HasGWorldScanAddr => HasData && IsNonZeroAddr(GWorldScanAddr);

    /// <summary>True when SparseDelegates has a non-empty pattern ID to display.</summary>
    public bool HasSparseDelegatesPatternId => HasData && !string.IsNullOrEmpty(SparseDelegatesPatternId);
    /// <summary>True when SparseDelegates has a non-zero AOB scan address.</summary>
    public bool HasSparseDelegatesScanAddr => HasData && IsNonZeroAddr(SparseDelegatesScanAddr);
    /// <summary>True when SparseDelegates was successfully resolved (UE 4.23+ + AOB hit).</summary>
    public bool IsSparseDelegatesFound => HasData && IsNonZeroAddr(SparseDelegatesAddress);
    /// <summary>True when UE &lt; 4.23 — sparse delegates did not exist yet.
    /// Silent on a refused engine (both flavours): for UE 4.10 the statement is factually true,
    /// but printing it beside "this engine is unsupported" is noise that implies the rest of the
    /// panel works. The &gt;= 400 floor is kept as defence-in-depth so the pre-UE4 sentinel (300)
    /// can never read as a real sub-4.23 version even if IsVersionTooOld were somehow false.</summary>
    public bool IsSparseDelegatesUnsupported => HasData && !IsVersionTooOld
        && UeVersion >= 400 && UeVersion < 423;
    /// <summary>True when UE 4.23+ but AOB scan didn't find the static (warning state).</summary>
    public bool IsSparseDelegatesNotFound => HasData && UeVersion >= 423
        && SparseDelegatesMethod == "not_found";

    /// <summary>True when the &amp;GEngine slot was resolved.</summary>
    public bool IsGEngineFound => HasData && IsNonZeroAddr(GEngineAddress);
    /// <summary>True when GEngine has a pattern ID to display.</summary>
    public bool HasGEnginePatternId => HasData && !string.IsNullOrEmpty(GEnginePatternId);
    /// <summary>True when GEngine has a non-zero AOB scan address.</summary>
    public bool HasGEngineScanAddr => HasData && IsNonZeroAddr(GEngineScanAddr);
    /// <summary>True when no GEngine AOB validated — engine lookups fall back to the
    /// GObjects walk and a GameEngine-rooted CE export cannot be made restart-proof.
    /// Silent on a refused engine: FindGEngineSlot never ran, so the method is only
    /// "not_found" because it is the struct default.</summary>
    public bool IsGEngineNotFound => HasData && !IsVersionTooOld && GEngineMethod == "not_found";

    /// <summary>
    /// True when Extra Scan button should be visible:
    /// connected, not already scanning, and some pointer is missing.
    /// Never on a refused engine — Extra Scan probes the same UE4/UE5 presets and the same
    /// hardcoded UObject::Class chain, so it is a guaranteed no-op there, and offering it would
    /// contradict the banner that says it cannot help. The DLL refuses the command too.
    /// </summary>
    public bool CanExtraScan => HasData && !IsScanning && !IsVersionTooOld
        && (IsPointerMissing(GObjectsAddress) || GWorldMethod == "not_found");

    // --- AOBMaker button enable state ---
    /// <summary>Can register GWorld address as CE symbol via CreateSymbolScript (requires AOB data).</summary>
    public bool CanRegisterGWorldSymbol => IsAobMakerAvailable
        && IsNonZeroAddr(GWorldAddress) && !string.IsNullOrEmpty(GworldAob);

    /// <summary>Can send GObjects pointer to CE hex view (data address).</summary>
    public bool CanHexGObjects => IsAobMakerAvailable && IsNonZeroAddr(GObjectsAddress);
    /// <summary>Can send GNames pointer to CE hex view (data address).</summary>
    public bool CanHexGNames => IsAobMakerAvailable && IsNonZeroAddr(GNamesAddress);
    /// <summary>Can send GWorld pointer to CE hex view (data address).</summary>
    public bool CanHexGWorld => IsAobMakerAvailable && IsNonZeroAddr(GWorldAddress);
    /// <summary>Can send FSparseDelegateStorage pointer to CE hex view (data address).</summary>
    public bool CanHexSparseDelegates => IsAobMakerAvailable && IsNonZeroAddr(SparseDelegatesAddress);
    /// <summary>Can send the &amp;GEngine slot to CE hex view (data address).</summary>
    public bool CanHexGEngine => IsAobMakerAvailable && IsNonZeroAddr(GEngineAddress);

    /// <summary>Can register the &amp;GEngine SLOT as a CE symbol via CreateSymbolScript.
    /// Same contract as GWorld: the AOB triple is what makes the symbol restart-proof, so a
    /// resolved address alone is not enough.</summary>
    public bool CanRegisterGEngineSymbol => IsAobMakerAvailable
        && IsNonZeroAddr(GEngineAddress) && !string.IsNullOrEmpty(GengineAob);

    /// <summary>Can send GObjects AOB scan hit address to CE disassembler (code address).</summary>
    public bool CanAsmGObjectsScan => IsAobMakerAvailable && IsNonZeroAddr(GObjectsScanAddr);
    /// <summary>Can send GNames AOB scan hit address to CE disassembler (code address).</summary>
    public bool CanAsmGNamesScan => IsAobMakerAvailable && IsNonZeroAddr(GNamesScanAddr);
    /// <summary>Can send GWorld AOB scan hit address to CE disassembler (code address).</summary>
    public bool CanAsmGWorldScan => IsAobMakerAvailable && IsNonZeroAddr(GWorldScanAddr);

    /// <summary>True when cache management buttons should be shown (connected + has AobUsageService).</summary>
    public bool CanManageCache => HasData && _aobUsage != null;

    /// <summary>True when the clear-this-game button should be enabled (has PE hash).</summary>
    public bool CanClearGameCache => CanManageCache && !string.IsNullOrEmpty(PeHash);

    /// <summary>Fired when rescan results have been applied — MainWindowVM re-fetches state.</summary>
    public event Action? RescanApplied;

    /// <summary>Fired after every snapshot DB file was removed (Remove All Snapshot Data)
    /// — MainWindowVM refreshes the experimental Snapshot / SPC / Pivot tabs so their
    /// now-stale lists clear.</summary>
    public event Action? SnapshotDataRemoved;

    public PointerPanelViewModel(IPlatformService platform, IDumpService? dump = null,
                                ILoggingService? log = null, IAobMakerBridge? aobMaker = null,
                                AobUsageService? aobUsage = null,
                                IExperimentalGate? experimentalGate = null,
                                ISnapshotStore? snapshotStore = null,
                                IPipeClient? pipeClient = null,
                                ILogCompressionService? logCompression = null)
    {
        _platform = platform;
        _dump = dump;
        _log = log;
        _aobMaker = aobMaker;
        _aobUsage = aobUsage;
        _experimentalGate = experimentalGate;
        _snapshotStore = snapshotStore;
        _pipe = pipeClient;
        _logCompression = logCompression;
        LogCompressionSupported = logCompression?.IsSupported(platform.GetLogDirectoryPath()) ?? false;

        // Subscribe to the live pipe activity tail (System-tab Pipe Activity card).
        if (pipeClient != null)
            pipeClient.Activity += OnPipeActivity;

        // Reflect external flips (e.g. another System-tab instance) back to the
        // checkbox, plus the lock state (which disables the checkbox once an
        // experimental tab has been opened).
        if (experimentalGate != null)
            experimentalGate.Changed += (_, _) =>
            {
                OnPropertyChanged(nameof(ExperimentalEnabled));
                OnPropertyChanged(nameof(CanToggleExperimental));
            };
    }

    /// <summary>
    /// False once the experimental opt-in has been locked (the user opened an
    /// experimental tab while enabled). The System-tab checkbox binds its
    /// <c>IsEnabled</c> to this so a locked opt-in can no longer be unticked.
    /// </summary>
    public bool CanToggleExperimental => !(_experimentalGate?.IsLocked ?? false);

    /// <summary>
    /// Opt-in toggle for the experimental analysis tabs (Snapshot / SPC Query /
    /// Class Pivot), surfaced as the System-tab credit checkbox. Backed by the
    /// shared <see cref="IExperimentalGate"/> (persisted across restarts).
    /// </summary>
    public bool ExperimentalEnabled
    {
        get => _experimentalGate?.IsEnabled ?? false;
        set
        {
            if (_experimentalGate == null || _experimentalGate.IsEnabled == value) return;
            _experimentalGate.IsEnabled = value;
            OnPropertyChanged();
        }
    }

    public void Update(EngineState state)
    {
        GObjectsAddress = state.GObjectsAddr;
        GNamesAddress = state.GNamesAddr;
        GWorldAddress = state.GWorldAddr;
        SparseDelegatesAddress = state.SparseDelegatesAddr;
        UeVersion = state.UEVersion;
        VersionDetected = state.VersionDetected;
        IsUserOverride = state.IsUserOverride;
        IsLowConfidence = state.IsLowConfidence;
        IsVersionTooOld = state.IsVersionTooOld;
        OffsetsValidated = state.OffsetsValidated;
        OffsetsProbeRan = state.OffsetsProbeRan;
        OffsetsFallbackReason = state.OffsetsFallbackReason;
        PublisherThumbprint = state.PublisherThumbprint;
        // Re-sync the ComboBox selection to whatever the DLL actually has (override or auto).
        // _suppressOverrideSelectionEvent gates the partial method so this assignment doesn't
        // re-trigger the apply path.
        _suppressOverrideSelectionEvent = true;
        SelectedUeVersionOverride = state.IsUserOverride ? VersionToLabel(state.UEVersion) : "Auto";
        _suppressOverrideSelectionEvent = false;
        TotalObjects = state.ObjectCount;
        ItemPacked = state.ItemPacked;
        ItemLayoutMode = state.ItemLayoutMode;
        ItemObjOffset = state.ItemObjOffset;
        // Ambient flag so static export utilities can embed the best-effort note.
        Services.PackedLayoutNotice.IsActive = state.ItemPacked;
        GObjectsMethod = state.GObjectsMethod;
        GNamesMethod = state.GNamesMethod;
        GWorldMethod = state.GWorldMethod;
        SparseDelegatesMethod = state.SparseDelegatesMethod;
        GObjectsPatternId = state.GObjectsPatternId;
        GNamesPatternId = state.GNamesPatternId;
        GWorldPatternId = state.GWorldPatternId;
        SparseDelegatesPatternId = state.SparseDelegatesPatternId;
        GObjectsPatternsHit = state.GObjectsPatternsHit;
        GNamesPatternsHit = state.GNamesPatternsHit;
        GWorldPatternsHit = state.GWorldPatternsHit;
        GObjectsScanAddr = state.GObjectsScanAddr;
        GNamesScanAddr = state.GNamesScanAddr;
        GWorldScanAddr = state.GWorldScanAddr;
        SparseDelegatesScanAddr = state.SparseDelegatesScanAddr;
        GworldAob = state.GWorldAob;
        GworldAobPos = state.GWorldAobPos;
        GworldAobLen = state.GWorldAobLen;
        GEngineAddress = state.GEngine;
        GEngineMethod = state.GEngineMethod;
        GEnginePatternId = state.GEnginePatternId;
        GEngineScanAddr = state.GEngineScanAddr;
        GengineAob = state.GEngineAob;
        GengineAobPos = state.GEngineAobPos;
        GengineAobLen = state.GEngineAobLen;
        ModuleName = state.ModuleName;
        PeHash = state.PeHash;
        DllBuildNumber = state.DllBuildNumber;
        // Re-sync the invoke timeout from the DLL (already-applied per-game override or default).
        // _suppressInvokeTimeoutEvent prevents the partial-changed handler from firing the apply
        // round-trip when this is just a refresh.
        _suppressInvokeTimeoutEvent = true;
        InvokeTimeoutMs = state.InvokeTimeoutMs > 0 ? state.InvokeTimeoutMs : Constants.StarkDefaultInvokeTimeoutMs;
        _suppressInvokeTimeoutEvent = false;
        HasData = true;
        // Reset scan state on fresh update
        IsScanning = false;
        ScanComplete = false;
        ScanStatusText = "";
        ScanResultText = "";
        CacheStatusText = "";
        NotifyComputedProperties();
        // Check AOBMaker availability in background (fire-and-forget)
        _ = CheckAobMakerAsync();
    }

    /// <summary>Check AOBMaker availability (called after data load and on tab switch).</summary>
    public async Task CheckAobMakerAsync()
    {
        if (_aobMaker == null) return;
        try
        {
            IsAobMakerAvailable = await _aobMaker.CheckAvailabilityAsync();
            NotifyAobMakerProperties();
        }
        catch { IsAobMakerAvailable = false; }
    }

    /// <summary>Hide the stale pointer block + all HasData-gated badges/actions on
    /// disconnect so a reconnect never offers copy/register on the previous game's
    /// addresses (audit X5). <see cref="Update"/> repopulates on reconnect. Minimal
    /// by design: resetting the UE-override / invoke-timeout inputs would fire a pipe
    /// round-trip via their setters, so only <see cref="HasData"/> is reset. The pipe
    /// activity log is kept for post-mortem.</summary>
    public void ClearOnDisconnect()
    {
        HasData = false;
        NotifyComputedProperties();
    }

    private void NotifyComputedProperties()
    {
        OnPropertyChanged(nameof(ShowVersionWarning));
        OnPropertyChanged(nameof(ShowUserOverrideBadge));
        OnPropertyChanged(nameof(ShowLowConfidenceWarning));
        OnPropertyChanged(nameof(ShowVersionDetectedBadge));
        // Both refusal banners. ShowVersionTooOldWarning was MISSING here before the pre-UE4
        // work: IsVersionTooOld is an [ObservableProperty] with no NotifyPropertyChangedFor, so
        // the computed property never re-raised and the red banner only rendered if the binding
        // happened to be evaluated for the first time after Update() had already run. On any
        // refresh of an already-attached panel it stayed hidden.
        OnPropertyChanged(nameof(ShowVersionTooOldWarning));
        OnPropertyChanged(nameof(ShowPreUE4Warning));
        // Same trap the two lines above document: these are [ObservableProperty] with no
        // NotifyPropertyChangedFor, so the computed pair must be raised by hand or the banner
        // only ever appears if the binding happens to evaluate first after Update().
        OnPropertyChanged(nameof(ShowOffsetsUnvalidatedWarning));
        OnPropertyChanged(nameof(OffsetsUnvalidatedText));
        OnPropertyChanged(nameof(ShowInvokeTimeoutOverrideBadge));
        OnPropertyChanged(nameof(ShowPublisherHint));
        OnPropertyChanged(nameof(PublisherLabel));
        OnPropertyChanged(nameof(ShowGObjectsWarning));
        OnPropertyChanged(nameof(ShowGNamesWarning));
        OnPropertyChanged(nameof(ShowGWorldWarning));
        OnPropertyChanged(nameof(ShowGWorldRecovered));
        OnPropertyChanged(nameof(GObjectsAobAllFailed));
        OnPropertyChanged(nameof(GNamesAobAllFailed));
        OnPropertyChanged(nameof(GWorldAobAllFailed));
        OnPropertyChanged(nameof(GObjectsMethodLabel));
        OnPropertyChanged(nameof(GNamesMethodLabel));
        OnPropertyChanged(nameof(GWorldMethodLabel));
        OnPropertyChanged(nameof(HasGObjectsPatternId));
        OnPropertyChanged(nameof(HasGNamesPatternId));
        OnPropertyChanged(nameof(HasGWorldPatternId));
        OnPropertyChanged(nameof(HasGObjectsScanAddr));
        OnPropertyChanged(nameof(HasGNamesScanAddr));
        OnPropertyChanged(nameof(HasGWorldScanAddr));
        OnPropertyChanged(nameof(HasSparseDelegatesPatternId));
        OnPropertyChanged(nameof(HasSparseDelegatesScanAddr));
        OnPropertyChanged(nameof(IsSparseDelegatesFound));
        OnPropertyChanged(nameof(IsSparseDelegatesUnsupported));
        OnPropertyChanged(nameof(IsSparseDelegatesNotFound));
        OnPropertyChanged(nameof(IsGEngineFound));
        OnPropertyChanged(nameof(HasGEnginePatternId));
        OnPropertyChanged(nameof(HasGEngineScanAddr));
        OnPropertyChanged(nameof(IsGEngineNotFound));
        OnPropertyChanged(nameof(CanExtraScan));
        OnPropertyChanged(nameof(CanManageCache));
        OnPropertyChanged(nameof(CanClearGameCache));
        OnPropertyChanged(nameof(BuildVersionsMatch));
        OnPropertyChanged(nameof(BuildVersionMismatch));
        OnPropertyChanged(nameof(BuildVersionUnknown));
        // The global top-bar badge mirror (MainWindowViewModel) listens for these two;
        // without re-raising them here an Update() that doesn't change DllBuildNumber's
        // value (e.g. a reconnect/refresh to the same DLL) would leave the badge stale.
        OnPropertyChanged(nameof(ShowGlobalBuildWarning));
        OnPropertyChanged(nameof(GlobalBuildWarningText));
        // Global top-bar unverified-packed-layout badge mirror (MainWindowViewModel).
        OnPropertyChanged(nameof(ShowPackedLayoutBadge));
        OnPropertyChanged(nameof(PackedLayoutBadgeText));
        OnPropertyChanged(nameof(CanSelfTest));
        NotifyAobMakerProperties();
    }

    // Recompute computed props when DllBuildNumber arrives separately from HasData.
    partial void OnDllBuildNumberChanged(int value)
    {
        OnPropertyChanged(nameof(BuildVersionsMatch));
        OnPropertyChanged(nameof(BuildVersionMismatch));
        OnPropertyChanged(nameof(BuildVersionUnknown));
        OnPropertyChanged(nameof(ShowGlobalBuildWarning));
        OnPropertyChanged(nameof(GlobalBuildWarningText));
    }

    private void NotifyAobMakerProperties()
    {
        OnPropertyChanged(nameof(CanHexGObjects));
        OnPropertyChanged(nameof(CanHexGNames));
        OnPropertyChanged(nameof(CanHexGWorld));
        OnPropertyChanged(nameof(CanHexSparseDelegates));
        OnPropertyChanged(nameof(CanHexGEngine));
        OnPropertyChanged(nameof(CanAsmGObjectsScan));
        OnPropertyChanged(nameof(CanAsmGNamesScan));
        OnPropertyChanged(nameof(CanAsmGWorldScan));
        OnPropertyChanged(nameof(CanRegisterGWorldSymbol));
        OnPropertyChanged(nameof(CanRegisterGEngineSymbol));
    }

    private bool _suppressOverrideSelectionEvent;
    private bool _suppressInvokeTimeoutEvent;

    /// <summary>True when the active timeout differs from Stark's 5000ms default.</summary>
    public bool ShowInvokeTimeoutOverrideBadge => HasData && InvokeTimeoutMs != Constants.StarkDefaultInvokeTimeoutMs;

    /// <summary>
    /// Auto-fired by [ObservableProperty] when SelectedUeVersionOverride changes.
    /// Sends the new value over the pipe; on success, the DLL re-fetch updates everything.
    /// </summary>
    partial void OnSelectedUeVersionOverrideChanged(string value)
    {
        if (_suppressOverrideSelectionEvent) return;
        if (_dump == null) return;
        if (!HasData) return;
        // Fire-and-forget — the apply command itself awaits the pipe round-trip.
        _ = ApplyOverrideAsync(value);
    }

    /// <summary>
    /// Auto-fired when the invoke timeout NumericUpDown changes. Debounce comes from
    /// the bound ValueChanged event itself (UI fires once per commit, not per keystroke).
    /// </summary>
    partial void OnInvokeTimeoutMsChanged(int value)
    {
        OnPropertyChanged(nameof(ShowInvokeTimeoutOverrideBadge));
        if (_suppressInvokeTimeoutEvent) return;
        if (_dump == null) return;
        if (!HasData) return;
        _ = ApplyInvokeTimeoutAsync(value);
    }

    private async Task ApplyInvokeTimeoutAsync(int timeoutMs)
    {
        if (_dump == null) return;
        try
        {
            ClearError();
            IsApplyingInvokeTimeout = true;
            // 5000 = the Stark default → treat as "clear override" so the JSON stays clean.
            int payload = timeoutMs == Constants.StarkDefaultInvokeTimeoutMs ? 0 : timeoutMs;
            var newState = await _dump.SetInvokeTimeoutAsync(payload, persist: true);
            Update(newState);
        }
        catch (Exception ex)
        {
            SetError(ex);
            _log?.Error(Constants.LogCatInit, "Failed to apply invoke timeout", ex);
        }
        finally
        {
            IsApplyingInvokeTimeout = false;
        }
    }

    private async Task ApplyOverrideAsync(string label)
    {
        if (_dump == null) return;
        try
        {
            ClearError();
            IsApplyingOverride = true;
            int version = LabelToVersion(label);  // 0 = Auto (clear)
            var newState = await _dump.SetUeVersionOverrideAsync(version, persist: true);
            Update(newState);
            RescanApplied?.Invoke();   // tell MainWindowVM to refresh other panels
        }
        catch (Exception ex)
        {
            SetError(ex);
            _log?.Error(Constants.LogCatInit, "Failed to apply UE version override", ex);
        }
        finally
        {
            IsApplyingOverride = false;
        }
    }

    /// <summary>"UE 4.27" → 427, "UE 5.4" → 504, "Auto" → 0.</summary>
    private static int LabelToVersion(string label)
    {
        if (string.IsNullOrEmpty(label) || label == "Auto") return 0;
        // Format: "UE M.N"  (M = 4 or 5, N = 0..27)
        var span = label.AsSpan().TrimStart();
        if (span.StartsWith("UE ")) span = span.Slice(3);
        int dot = span.IndexOf('.');
        if (dot <= 0) return 0;
        if (!int.TryParse(span.Slice(0, dot), out int major)) return 0;
        if (!int.TryParse(span.Slice(dot + 1), out int minor)) return 0;
        return major * 100 + minor;
    }

    /// <summary>504 → "UE 5.4", 427 → "UE 4.27", 0 → "Auto".</summary>
    private static string VersionToLabel(int version)
    {
        if (version <= 0) return "Auto";
        int major = version / 100;
        int minor = version % 100;
        return $"UE {major}.{minor}";
    }

    private static string FormatMethodLabel(string method) => method switch
    {
        "data_scan" => "data scan",
        "data_scan_recovery" => "data scan recovery",
        "data_heuristic" => "data heuristic",
        "instance_scan" => "instance scan",
        "instance_scan_recovery" => "instance scan recovery",
        "engine_recovery" => "engine recovery",
        "string_ref" => "string ref",
        "pointer_scan" => "pointer scan",
        "not_found" => "not found",
        _ => method,
    };

    private static bool IsPointerMissing(string addr)
        => string.IsNullOrEmpty(addr) || addr == "0x0" || addr == "0x00000000" || addr == "0";

    /// <summary>True when the address string represents a non-zero value (not empty, "0", or "0x0").</summary>
    private static bool IsNonZeroAddr(string? addr)
        => !string.IsNullOrEmpty(addr) && addr != "0" && addr != "0x0" && addr != "0x00000000";

    /// <summary>Strip leading "0x" or "0X" prefix for clipboard copy.</summary>
    private static string StripHexPrefix(string addr)
        => addr.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? addr[2..] : addr;

    // --- Extra Scan ---

    [RelayCommand]
    private async Task ExtraScanAsync()
    {
        if (_dump == null) return;

        try
        {
            ClearError();
            IsScanning = true;
            ScanComplete = false;
            ScanStatusText = Res.Get("str.Pointers.Scan.Starting");
            ScanResultText = "";
            OnPropertyChanged(nameof(CanExtraScan));

            var startResult = await _dump.StartRescanAsync();

            if (!startResult.ScanningGObjects && !startResult.ScanningGWorld)
            {
                ScanStatusText = Res.Get("str.Pointers.Scan.NothingToScan");
                IsScanning = false;
                OnPropertyChanged(nameof(CanExtraScan));
                return;
            }

            _log?.Info(Constants.LogCatInit,
                $"Extra Scan started: GObjects={startResult.ScanningGObjects}, GWorld={startResult.ScanningGWorld}");

            // Poll status every 1.5 seconds
            while (true)
            {
                await Task.Delay(1500);

                var status = await _dump.GetRescanStatusAsync();
                ScanStatusText = status.StatusText;

                if (!status.Running && status.Phase >= 3)
                {
                    // Scan complete — apply if anything was found
                    ScanComplete = true;

                    if (status.FoundGObjects || status.FoundGWorld)
                    {
                        ScanStatusText = Res.Get("str.Pointers.Scan.Applying");
                        var newState = await _dump.ApplyRescanAsync();

                        var parts = new List<string>();
                        if (status.FoundGObjects) parts.Add($"GObjects: {status.GObjectsAddr}");
                        if (status.FoundGWorld) parts.Add($"GWorld: {status.GWorldAddr}");
                        ScanResultText = $"Found: {string.Join(", ", parts)}";
                        ScanStatusText = Res.Get("str.Pointers.Scan.Applied");

                        _log?.Info(Constants.LogCatInit, $"Extra Scan complete: {ScanResultText}");

                        // Notify MainWindowVM to refresh all panels
                        RescanApplied?.Invoke();
                    }
                    else
                    {
                        ScanResultText = Res.Get("str.Pointers.Scan.NoResults");
                        ScanStatusText = Res.Get("str.Pointers.Scan.CompleteNoResults");
                        _log?.Info(Constants.LogCatInit, "Extra Scan complete: no results");
                    }
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            ScanStatusText = Res.Format("str.Pointers.Scan.Error", ex.Message);
            SetError(ex);
            _log?.Error(Constants.LogCatInit, "Extra Scan failed", ex);
        }
        finally
        {
            IsScanning = false;
            OnPropertyChanged(nameof(CanExtraScan));
        }
    }

    // --- Test button: simulate Extra Scan for development testing ---

    [RelayCommand]
    private async Task TestExtraScanAsync()
    {
        if (_dump == null) return;

        try
        {
            ClearError();
            IsScanning = true;
            ScanComplete = false;
            ScanStatusText = Res.Get("str.Pointers.TestScan.Starting");
            ScanResultText = "";
            OnPropertyChanged(nameof(CanExtraScan));

            // Simulate scan phases with delays
            ScanStatusText = Res.Get("str.Pointers.TestScan.GObjects");
            await Task.Delay(2000);

            ScanStatusText = Res.Get("str.Pointers.TestScan.GWorld");
            await Task.Delay(2000);

            // After simulation, do a real get_pointers to show current state
            ScanComplete = true;
            ScanStatusText = Res.Get("str.Pointers.TestScan.Complete");
            ScanResultText = Res.Get("str.Pointers.TestScan.Result");

            _log?.Info(Constants.LogCatInit, "Test Extra Scan simulation complete");
        }
        catch (Exception ex)
        {
            ScanStatusText = Res.Format("str.Pointers.TestScan.Error", ex.Message);
            SetError(ex);
        }
        finally
        {
            IsScanning = false;
            OnPropertyChanged(nameof(CanExtraScan));
        }
    }

    // --- AOBMaker CE Plugin: data pointer → hex view (memory dump) ---

    [RelayCommand]
    private async Task HexGObjectsAsync()
    {
        if (_aobMaker == null || !IsNonZeroAddr(GObjectsAddress)) return;
        await _aobMaker.NavigateHexViewAsync(StripHexPrefix(GObjectsAddress));
    }

    [RelayCommand]
    private async Task HexGNamesAsync()
    {
        if (_aobMaker == null || !IsNonZeroAddr(GNamesAddress)) return;
        await _aobMaker.NavigateHexViewAsync(StripHexPrefix(GNamesAddress));
    }

    [RelayCommand]
    private async Task HexGWorldAsync()
    {
        if (_aobMaker == null || !IsNonZeroAddr(GWorldAddress)) return;
        await _aobMaker.NavigateHexViewAsync(StripHexPrefix(GWorldAddress));
    }

    [RelayCommand]
    private async Task HexSparseDelegatesAsync()
    {
        if (_aobMaker == null || !IsNonZeroAddr(SparseDelegatesAddress)) return;
        await _aobMaker.NavigateHexViewAsync(StripHexPrefix(SparseDelegatesAddress));
    }

    [RelayCommand]
    private async Task HexGEngineAsync()
    {
        if (_aobMaker == null || !IsNonZeroAddr(GEngineAddress)) return;
        await _aobMaker.NavigateHexViewAsync(StripHexPrefix(GEngineAddress));
    }

    // --- AOBMaker CE Plugin: scan address → disassembler (code) ---

    [RelayCommand]
    private async Task AsmGObjectsScanAsync()
    {
        if (_aobMaker == null || !IsNonZeroAddr(GObjectsScanAddr)) return;
        await _aobMaker.NavigateDisassemblerAsync(StripHexPrefix(GObjectsScanAddr));
    }

    [RelayCommand]
    private async Task AsmGNamesScanAsync()
    {
        if (_aobMaker == null || !IsNonZeroAddr(GNamesScanAddr)) return;
        await _aobMaker.NavigateDisassemblerAsync(StripHexPrefix(GNamesScanAddr));
    }

    [RelayCommand]
    private async Task AsmGWorldScanAsync()
    {
        if (_aobMaker == null || !IsNonZeroAddr(GWorldScanAddr)) return;
        await _aobMaker.NavigateDisassemblerAsync(StripHexPrefix(GWorldScanAddr));
    }

    // --- AOBMaker CE Plugin: register GWorld as AOB-scan-based CE symbol ---

    [RelayCommand]
    private async Task RegisterGWorldSymbolAsync()
    {
        if (_aobMaker == null || string.IsNullOrEmpty(GworldAob)) return;

        string symbolName = "gworld_addr";
        string module = !string.IsNullOrEmpty(ModuleName) ? ModuleName : "game.exe";

        // Send CreateSymbolScript — the CE Plugin's BuildSymbolScanScript() generates
        // a full AA script that: AOBScanModule for the pattern, reads the RIP-relative
        // displacement at 'pos', calculates final address using 'aoblen', and registers
        // it as a CE symbol. This survives game restarts (re-scans on enable).
        bool success = await _aobMaker.CreateSymbolScriptAsync(
            name: $"GWorld → {symbolName}",
            aob: GworldAob,
            pos: GworldAobPos,
            aoblen: GworldAobLen,
            symbol: symbolName,
            module: module,
            autoActivate: true);

        if (success)
            _log?.Info(Constants.LogCatInit,
                $"Created CE symbol script '{symbolName}' (AOB: {GworldAob}, pos={GworldAobPos}, len={GworldAobLen})");
        else
            _log?.Warn(Constants.LogCatInit,
                $"Failed to create CE symbol script '{symbolName}'");
    }

    // --- AOBMaker CE Plugin: register &GEngine as AOB-scan-based CE symbol ---
    //
    // The symbol points at the SLOT, not at the UEngine object, which is the whole reason this
    // is worth having: the slot address is restart-stable, so a GameEngine-rooted CE record
    // auto-follows engine recreation instead of freezing a stale UEngine* snapshot. Same
    // contract as gworld_addr.

    [RelayCommand]
    private async Task RegisterGEngineSymbolAsync()
    {
        if (_aobMaker == null || string.IsNullOrEmpty(GengineAob)) return;

        string symbolName = "gengine_addr";
        string module = !string.IsNullOrEmpty(ModuleName) ? ModuleName : "game.exe";

        bool success = await _aobMaker.CreateSymbolScriptAsync(
            name: $"&GEngine → {symbolName}",
            aob: GengineAob,
            pos: GengineAobPos,
            aoblen: GengineAobLen,
            symbol: symbolName,
            module: module,
            autoActivate: true);

        if (success)
            _log?.Info(Constants.LogCatInit,
                $"Created CE symbol script '{symbolName}' (AOB: {GengineAob}, pos={GengineAobPos}, len={GengineAobLen})");
        else
            _log?.Warn(Constants.LogCatInit,
                $"Failed to create CE symbol script '{symbolName}'");
    }

    // --- Cache management ---

    [RelayCommand]
    private async Task ClearGameCacheAsync()
    {
        if (_aobUsage == null || string.IsNullOrEmpty(PeHash)) return;

        try
        {
            var removed = await _aobUsage.DeleteGameAsync(PeHash);
            CacheStatusText = removed
                ? Res.Get("str.Pointers.Cache.GameCleared")
                : Res.Get("str.Pointers.Cache.GameNotFound");
            _log?.Info(Constants.LogCatInit, $"ClearGameCache: PE={PeHash}, removed={removed}");
        }
        catch (Exception ex)
        {
            CacheStatusText = Res.Format("str.Pointers.Cache.Error", ex.Message);
            _log?.Error(Constants.LogCatInit, "ClearGameCache failed", ex);
        }
    }

    [RelayCommand]
    private async Task ResetAllCacheAsync()
    {
        if (_aobUsage == null) return;

        try
        {
            var success = await _aobUsage.ResetAllAsync();
            CacheStatusText = success
                ? Res.Get("str.Pointers.Cache.AllReset")
                : Res.Get("str.Pointers.Cache.ResetFailed");
            _log?.Info(Constants.LogCatInit, $"ResetAllCache: success={success}");
        }
        catch (Exception ex)
        {
            CacheStatusText = Res.Format("str.Pointers.Cache.Error", ex.Message);
            _log?.Error(Constants.LogCatInit, "ResetAllCache failed", ex);
        }
    }

    // --- Maintenance: open log folder + remove all snapshot databases ---

    [RelayCommand]
    private async Task OpenLogFolderAsync()
    {
        try
        {
            var logDir = _platform.GetLogDirectoryPath();
            // The logger normally creates this on startup, but be defensive so the
            // button still opens something sensible on a brand-new install.
            System.IO.Directory.CreateDirectory(logDir);
            await _platform.RevealInExplorerAsync(logDir);
        }
        catch (Exception ex)
        {
            MaintenanceStatusText = Res.Format("str.System.OpenLogFolder.Error", ex.Message);
            _log?.Error(Constants.LogCatInit, "OpenLogFolder failed", ex);
        }
    }

    /// <summary>
    /// Manual log-compression sweep. Uses the SHORT idle window
    /// (<see cref="Constants.LogCompressMinIdleHours"/>) rather than the automatic pass's
    /// 7-day floor: the user pressed the button, so "compress everything that isn't being
    /// written right now" is what they asked for.
    /// </summary>
    [RelayCommand]
    private async Task CompressLogsAsync()
    {
        if (_logCompression == null || IsCompressingLogs) return;

        IsCompressingLogs = true;
        MaintenanceStatusText = Res.Get("str.System.CompressLogs.Running");
        try
        {
            var r = await _logCompression.CompressAsync(
                _platform.GetLogDirectoryPath(),
                TimeSpan.FromHours(Constants.LogCompressMinIdleHours),
                Constants.LogCompressMinSizeBytes);

            MaintenanceStatusText = !r.Supported
                ? Res.Get("str.System.CompressLogs.Unsupported")
                : r.NothingToDo
                    ? Res.Format("str.System.CompressLogs.NothingToDo",
                                 r.SkippedAlreadyCompressed, r.SkippedTooSmall + r.SkippedTooFresh + r.SkippedLive)
                    : Res.Format("str.System.CompressLogs.Result",
                                 r.Compressed, Mb(r.BytesBefore), Mb(r.BytesAfter), Mb(r.BytesSaved), r.Failed);

            _log?.Info(Constants.LogCatInit,
                $"CompressLogs: compressed={r.Compressed} failed={r.Failed} " +
                $"saved={r.BytesSaved} supported={r.Supported}");
        }
        catch (Exception ex)
        {
            MaintenanceStatusText = Res.Format("str.System.CompressLogs.Error", ex.Message);
            _log?.Error(Constants.LogCatInit, "CompressLogs failed", ex);
        }
        finally
        {
            IsCompressingLogs = false;
        }
    }

    private static string Mb(long bytes) =>
        (bytes / (1024.0 * 1024.0)).ToString("0.0", CultureInfo.InvariantCulture);

    [RelayCommand]
    private async Task RemoveAllSnapshotsAsync()
    {
        // Defense in depth: the button is hidden unless experimental is enabled, but
        // guard the command too (and no-op without a store).
        if (_snapshotStore == null || !(_experimentalGate?.IsEnabled ?? false)) return;

        bool confirmed = await UE5DumpUI.Views.ConfirmDialog.ShowAsync(
            Res.Get("str.System.RemoveAllSnapshots.ConfirmTitle"),
            Res.Get("str.System.RemoveAllSnapshots.ConfirmMessage"),
            Res.Get("str.System.RemoveAllSnapshots.ConfirmYes"));
        if (!confirmed) return;

        try
        {
            var r = await _snapshotStore.DeleteAllSnapshotDatabasesAsync();
            MaintenanceStatusText = r.Deleted == 0 && r.Skipped == 0
                ? Res.Get("str.System.RemoveAllSnapshots.None")
                : r.Skipped > 0
                    ? Res.Format("str.System.RemoveAllSnapshots.ResultPartial", r.Deleted, r.Skipped)
                    : Res.Format("str.System.RemoveAllSnapshots.Result", r.Deleted);
            _log?.Info(Constants.LogCatView,
                $"RemoveAllSnapshots: deleted={r.Deleted}, skipped={r.Skipped}");
            // Let the Snapshot / SPC / Pivot tabs drop their now-stale lists.
            SnapshotDataRemoved?.Invoke();
        }
        catch (Exception ex)
        {
            MaintenanceStatusText = Res.Format("str.System.RemoveAllSnapshots.Error", ex.Message);
            _log?.Error(Constants.LogCatView, "RemoveAllSnapshots failed", ex);
        }
    }

    // --- Diagnostics (Sense) ---
    //
    // docs/multipipe-eval.md names DLL-side serial-dispatch head-of-line blocking
    // as the root cause of UI lag, but nothing measured it — so "should Phase 1
    // (non-blocking dispatch) be built?" was a blind decision. These numbers are
    // the evidence. Sits next to Pipe Activity on purpose: that card shows WHAT
    // crossed the pipe, this one shows what it COST.

    [ObservableProperty] private string _diagSummary = "";
    [ObservableProperty] private string _diagProcess = "";
    [ObservableProperty] private string _diagGameThread = "";
    [ObservableProperty] private string _diagStatus = "";
    [ObservableProperty] private bool _diagBusy;

    /// <summary>Poll interval for auto-refresh. Deliberately unhurried: every poll is
    /// itself a dispatch, so a fast timer would inflate the very numbers it reports
    /// (<c>get_diagnostics</c> already shows up in its own table). 5 s is slow enough
    /// to stay in the noise while still making CPU% — which needs two samples to
    /// difference — meaningful over a rolling window.</summary>
    private const int DiagAutoRefreshSeconds = 5;

    private DispatcherTimer? _diagTimer;

    [ObservableProperty] private bool _diagAutoRefresh;

    partial void OnDiagAutoRefreshChanged(bool value)
    {
        if (value)
        {
            _diagTimer ??= new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(DiagAutoRefreshSeconds),
            };
            // Re-subscribe defensively: the timer instance is reused across
            // toggles, and a double subscription would double the poll rate.
            _diagTimer.Tick -= OnDiagTimerTick;
            _diagTimer.Tick += OnDiagTimerTick;
            _diagTimer.Start();
            _ = RefreshDiagnosticsAsync();   // don't make the user wait one interval
        }
        else
        {
            _diagTimer?.Stop();
        }
    }

    private void OnDiagTimerTick(object? sender, EventArgs e)
    {
        // Never stack requests: a slow snapshot (the first one measured 125 ms) must
        // not queue up behind itself and turn the poll into a burst.
        if (DiagBusy) return;
        _ = RefreshDiagnosticsAsync();
    }

    /// <summary>Called when the user navigates away from the System tab. Stops the
    /// poll so a forgotten toggle doesn't keep adding pipe traffic — and skewing the
    /// dispatch numbers — while the user works somewhere else. The checkbox stays
    /// ticked, so returning to the tab resumes.</summary>
    public void OnLeavingTab() => _diagTimer?.Stop();

    /// <summary>Called when the user navigates INTO the System tab: resume the poll
    /// if it was left enabled.</summary>
    public void OnEnteringTab()
    {
        if (DiagAutoRefresh) _diagTimer?.Start();
    }

    /// <summary>Per-command dispatch cost, heaviest total first.</summary>
    public ObservableCollection<DiagnosticsCommandEntry> DiagCommands { get; } = new();

    private static string Mib(long bytes) =>
        (bytes / (1024.0 * 1024.0)).ToString("N1", CultureInfo.InvariantCulture) + " MiB";

    [RelayCommand]
    private async Task RefreshDiagnosticsAsync()
    {
        if (_dump == null) { DiagStatus = Res.Get("str.System.Diag.NotConnected"); return; }
        DiagBusy = true;
        try
        {
            var d = await _dump.GetDiagnosticsAsync();

            DiagSummary = Res.Format("str.System.Diag.Summary",
                d.TotalDispatches,
                (d.UptimeMs / 1000.0).ToString("N1", CultureInfo.InvariantCulture),
                d.BusyPercent.ToString("N1", CultureInfo.InvariantCulture),
                d.GObjectsCount);

            // CPU is -1 until a SECOND sample exists to difference against — show a
            // dash rather than a misleading 0%.
            DiagProcess = Res.Format("str.System.Diag.Process",
                Mib(d.Process.WorkingSetBytes),
                Mib(d.Process.PrivateBytes),
                d.Process.HasCpu
                    ? d.Process.CpuPercent.ToString("N1", CultureInfo.InvariantCulture) + "%"
                    : "—",
                d.Process.ThreadCount,
                d.Process.HandleCount);

            // The PE hook is installed lazily by the first invoke, so "never fired"
            // is the NORMAL state on a fresh connection — show it as such instead of
            // a meaningless age.
            DiagGameThread = Res.Format("str.System.Diag.GameThread",
                d.GameThread.HookActive ? "on" : "off",
                d.GameThread.Responsive
                    ? Res.Get("str.System.Diag.Responsive")
                    : Res.Get("str.System.Diag.Stalled"),
                d.GameThread.HasFired
                    ? Res.Format("str.System.Diag.LastFire", d.GameThread.MsSinceLastFire)
                    : Res.Get("str.System.Diag.NeverFired"),
                d.GameThread.HookFireCount);

            DiagCommands.Clear();
            foreach (var c in d.Commands) DiagCommands.Add(c);
            DiagStatus = "";
        }
        catch (Exception ex)
        {
            DiagStatus = Res.Format("str.System.Diag.Error", ex.Message);
            _log?.Error(Constants.LogCatView, "RefreshDiagnostics failed", ex);
        }
        finally
        {
            DiagBusy = false;
        }
    }

    [RelayCommand]
    private async Task ResetDiagnosticsAsync()
    {
        if (_dump == null) { DiagStatus = Res.Get("str.System.Diag.NotConnected"); return; }
        try
        {
            await _dump.ResetDiagnosticsAsync();
            DiagCommands.Clear();
            DiagSummary = DiagProcess = DiagGameThread = "";
            DiagStatus = Res.Get("str.System.Diag.Reset.Done");
            // Immediately re-read so the card shows a live (empty) baseline rather
            // than looking broken until the user clicks Refresh.
            await RefreshDiagnosticsAsync();
        }
        catch (Exception ex)
        {
            DiagStatus = Res.Format("str.System.Diag.Error", ex.Message);
            _log?.Error(Constants.LogCatView, "ResetDiagnostics failed", ex);
        }
    }

    // --- Pipe Activity log ---

    /// <summary>
    /// Pipe-thread callback: queue the line and coalesce a single UI-thread flush
    /// per burst (DispatcherPriority.Background yields to real UI work, so even a
    /// snapshot streaming hundreds of chunks can't starve the UI).
    /// </summary>
    private void OnPipeActivity(PipeLogEntry entry)
    {
        if (PipeLogPaused) return;
        _pipeLogPending.Enqueue(entry);
        // Bound the pending buffer so a busy UI thread can't let it grow without
        // limit; drop the oldest beyond a generous margin over the display cap.
        while (_pipeLogPending.Count > PipeLogCap * 4 && _pipeLogPending.TryDequeue(out _)) { }
        if (Interlocked.Exchange(ref _pipeLogFlushScheduled, 1) == 0)
            Dispatcher.UIThread.Post(FlushPipeLog, DispatcherPriority.Background);
    }

    /// <summary>UI-thread: drain the pending queue into the bound collection
    /// (newest first) and trim to the cap.</summary>
    private void FlushPipeLog()
    {
        Interlocked.Exchange(ref _pipeLogFlushScheduled, 0);
        bool wasEmpty = PipeLog.Count == 0;
        while (_pipeLogPending.TryDequeue(out var entry))
            PipeLog.Insert(0, entry);
        while (PipeLog.Count > PipeLogCap)
            PipeLog.RemoveAt(PipeLog.Count - 1);
        if (wasEmpty != (PipeLog.Count == 0))
            OnPropertyChanged(nameof(PipeLogIsEmpty));
    }

    [RelayCommand]
    private void ClearPipeLog()
    {
        while (_pipeLogPending.TryDequeue(out _)) { }
        PipeLog.Clear();
        OnPropertyChanged(nameof(PipeLogIsEmpty));
    }

    // --- Clipboard copy commands ---

    [RelayCommand]
    private async Task CopyGObjectsAsync()
    {
        if (!string.IsNullOrEmpty(GObjectsAddress))
            await _platform.CopyToClipboardAsync(StripHexPrefix(GObjectsAddress));
    }

    [RelayCommand]
    private async Task CopyGNamesAsync()
    {
        if (!string.IsNullOrEmpty(GNamesAddress))
            await _platform.CopyToClipboardAsync(StripHexPrefix(GNamesAddress));
    }

    [RelayCommand]
    private async Task CopyGWorldAsync()
    {
        if (!string.IsNullOrEmpty(GWorldAddress))
            await _platform.CopyToClipboardAsync(StripHexPrefix(GWorldAddress));
    }

    [RelayCommand]
    private async Task CopyGObjectsScanAddrAsync()
    {
        if (!string.IsNullOrEmpty(GObjectsScanAddr))
            await _platform.CopyToClipboardAsync(StripHexPrefix(GObjectsScanAddr));
    }

    [RelayCommand]
    private async Task CopyGNamesScanAddrAsync()
    {
        if (!string.IsNullOrEmpty(GNamesScanAddr))
            await _platform.CopyToClipboardAsync(StripHexPrefix(GNamesScanAddr));
    }

    [RelayCommand]
    private async Task CopyGWorldScanAddrAsync()
    {
        if (!string.IsNullOrEmpty(GWorldScanAddr))
            await _platform.CopyToClipboardAsync(StripHexPrefix(GWorldScanAddr));
    }

    [RelayCommand]
    private async Task CopySparseDelegatesAsync()
    {
        if (!string.IsNullOrEmpty(SparseDelegatesAddress))
            await _platform.CopyToClipboardAsync(StripHexPrefix(SparseDelegatesAddress));
    }

    [RelayCommand]
    private async Task CopyGEngineAsync()
    {
        if (!string.IsNullOrEmpty(GEngineAddress))
            await _platform.CopyToClipboardAsync(StripHexPrefix(GEngineAddress));
    }

    [RelayCommand]
    private async Task CopySparseDelegatesScanAddrAsync()
    {
        if (!string.IsNullOrEmpty(SparseDelegatesScanAddr))
            await _platform.CopyToClipboardAsync(StripHexPrefix(SparseDelegatesScanAddr));
    }

    // ===================================================================
    // Self-Test: PE-hook smoke test via auto-picked KismetMathLibrary helper
    //
    // Rationale: the build-647 wrong-vtable-slot bug slept for 600+ builds
    // because "result=0 from ProcessEvent" passed for success. Build 648's
    // pattern scanner + post-install fire-counter caught the misdetection
    // automatically — this button surfaces the SAME guarantee to end users:
    // one-click verification that the hook is on the right slot AND that
    // ProcessEvent actually executes the requested function body.
    //
    // Auto-pick: try a small ordered list of universal BlueprintFunctionLibrary
    // helpers (Add_IntInt → Multiply_IntInt → Add_FloatFloat → ...) until one
    // resolves on this game. All entries are FUNC_Native|FUNC_Static so the
    // call uses Mimic's static-native fast path (no game-thread queue) and
    // works on idle main-menu / loading screens. First-hit wins keeps the
    // pick deterministic across runs.
    //
    // Success criterion: ResultHex bytes at the return offset match the
    // expected value. We never trust pipe result=0 alone — that's exactly
    // the trap the build-647 bug fell into.
    // ===================================================================

    /// <summary>Self-test can run when connected. AOBMaker isn't needed.</summary>
    public bool CanSelfTest => HasData && _dump != null && !IsSelfTesting;

    /// <summary>Ordered candidate list. Each entry: function name on
    /// KismetMathLibrary + how to encode the 8-byte input + how to decode the
    /// return-slot bytes + expected return + display label.
    /// First entry whose lookup succeeds is the one used.</summary>
    private static readonly SelfTestCandidate[] _selfTestCandidates =
    {
        // Add_IntInt: most universal — pure integer math, present on every
        // UE 4.x and 5.x build. 3 + 4 = 7 (parmsSize=12: 4+4+4).
        new("Add_IntInt",            "03000000 04000000 00000000", 8, "int32",  7.0,  "Add_IntInt(3,4)"),
        new("Multiply_IntInt",       "03000000 04000000 00000000", 8, "int32",  12.0, "Multiply_IntInt(3,4)"),
        // Float variants — UE 4.x uses Float, UE 5.x converted many to Double.
        new("Add_FloatFloat",        "00004040 00008040 00000000", 8, "float",  7.0,  "Add_FloatFloat(3.0,4.0)"),
        new("Multiply_FloatFloat",   "00004040 00008040 00000000", 8, "float",  12.0, "Multiply_FloatFloat(3.0,4.0)"),
        // Double variants — UE 5.0+ only (parmsSize=24: 8+8+8).
        new("Add_DoubleDouble",      "0000000000000840 0000000000001040 0000000000000000", 16, "double", 7.0,  "Add_DoubleDouble(3.0,4.0)"),
        new("Multiply_DoubleDouble", "0000000000000840 0000000000001040 0000000000000000", 16, "double", 12.0, "Multiply_DoubleDouble(3.0,4.0)"),
    };

    private record SelfTestCandidate(
        string FuncName,
        string InputHex,       // hex string with spaces (stripped before sending)
        int    ReturnOffset,   // byte offset of return slot in params buffer
        string ReturnType,     // "int32" | "float" | "double"
        double Expected,       // expected return value
        string DisplayLabel);  // e.g. "Add_IntInt(3,4)"

    [RelayCommand]
    private async Task SelfTestAsync()
    {
        if (_dump == null) return;

        try
        {
            ClearError();
            IsSelfTesting = true;
            SelfTestHasResult = false;
            SelfTestPassed = false;
            SelfTestFailed = false;
            SelfTestResultText = Res.Get("str.System.SelfTest.Running");
            OnPropertyChanged(nameof(CanSelfTest));

            // Probe each candidate until one resolves. "Resolves" = the DLL's
            // invoke_function returns without "Function not found". We attempt
            // the actual call to avoid a separate find_function pipe round-trip
            // — net cost is the same as probing.
            foreach (var cand in _selfTestCandidates)
            {
                var probeResult = await TrySelfTestCandidate(cand);
                if (probeResult != null)
                {
                    // First hit — verify + report.
                    var (actual, hex, passed, invokeResult) = probeResult.Value;
                    if (passed)
                    {
                        SelfTestPassed = true;
                        SelfTestResultText = string.Format(
                            CultureInfo.InvariantCulture,
                            "✓ {0} = {1}  →  PE hook verified",
                            cand.DisplayLabel, FormatActual(actual, cand.ReturnType));
                    }
                    else
                    {
                        // A wrong answer has TWO possible causes and the invoke alone
                        // cannot separate them (working-lessons §4.4). Ask the DLL which
                        // one this is instead of asserting the wrong one, as the old
                        // "re-deploy the DLL" text did. ([PEHOOK-2026-08-17])
                        var cause = await ClassifySelfTestFailureAsync(invokeResult);
                        SelfTestFailed = true;
                        SelfTestResultText = Res.Format(
                            "str.System.SelfTest.Fail",
                            cand.DisplayLabel,
                            FormatActual(cand.Expected, cand.ReturnType),
                            FormatActual(actual, cand.ReturnType),
                            Res.Get(SelfTestAdvice.KeyFor(cause)),
                            hex);
                        _log?.Warn(Constants.LogCatInit,
                            $"Self-Test: {cand.DisplayLabel} FAILED — hook verdict={cause}");
                    }
                    SelfTestHasResult = true;
                    _log?.Info(Constants.LogCatInit,
                        $"Self-Test: {cand.DisplayLabel} expected={cand.Expected} actual={actual} pass={passed}");
                    return;
                }
            }

            // All candidates missing — unusual but possible (custom game build
            // with stripped KismetMathLibrary, or DLL pre-build-653 without the
            // direct_call flag support).
            SelfTestFailed = true;
            SelfTestHasResult = true;
            SelfTestResultText = Res.Get("str.System.SelfTest.NoCandidate");
            _log?.Warn(Constants.LogCatInit,
                "Self-Test: no testable KismetMathLibrary helper found in this game");
        }
        catch (Exception ex)
        {
            SelfTestFailed = true;
            SelfTestHasResult = true;
            SelfTestResultText = Res.Format("str.System.SelfTest.Error", ex.Message);
            SetError(ex);
            _log?.Error(Constants.LogCatInit, "Self-Test failed", ex);
        }
        finally
        {
            IsSelfTesting = false;
            OnPropertyChanged(nameof(CanSelfTest));
        }
    }

    /// <summary>
    /// Ask the DLL what its ProcessEvent hook is actually doing, so a failed
    /// Self-Test can advise the right remedy. Only runs on the failure path — one
    /// extra pipe round-trip, and only when something is already wrong.
    ///
    /// A probe that throws yields <see cref="SelfTestFailureCause.Unknown"/>, not a
    /// default guess: the whole point is to stop claiming a cause we have not
    /// measured.
    /// </summary>
    private async Task<SelfTestFailureCause> ClassifySelfTestFailureAsync(int invokeResult)
    {
        // A refused invoke needs no telemetry to explain and no round-trip to
        // confirm — nothing ran, so no hook reading is relevant to it.
        if (invokeResult != 0) return SelfTestFailureCause.NotDispatched;

        try
        {
            // limit:0 — the per-command table is irrelevant here; we want the
            // game_thread block only.
            var d = await _dump!.GetDiagnosticsAsync(limit: 0);
            return SelfTestAdvice.Classify(
                invokeResult:    invokeResult,
                haveDiagnostics: true,
                hookActive:      d.GameThread.HookActive,
                hookHasFired:    d.GameThread.HasFired);
        }
        catch (Exception ex)
        {
            _log?.Warn(Constants.LogCatInit,
                $"Self-Test: could not read hook diagnostics ({ex.Message}) — advising without a verdict");
            return SelfTestFailureCause.Unknown;
        }
    }

    /// <summary>Try one candidate. Returns (actualValue, rawHex, passed, result) on
    /// an invoke that the DLL accepted or refused, or null when the function isn't
    /// present on this game.
    ///
    /// <para><c>result</c> is carried out deliberately. It used to be discarded, so
    /// a REFUSED invoke (the DLL returning -3 without ever calling ProcessEvent)
    /// was indistinguishable from a call that ran and wrote nothing — the return
    /// slot is untouched either way.</para></summary>
    private async Task<(double actual, string hex, bool passed, int result)?> TrySelfTestCandidate(SelfTestCandidate cand)
    {
        if (_dump == null) return null;

        int parmsSize = cand.ReturnType == "double" ? 24 : 12;
        string paramsHexClean = cand.InputHex.Replace(" ", "");

        InvokeFunctionResult res;
        try
        {
            res = await _dump.InvokeFunctionAsync(
                funcName:     cand.FuncName,
                className:    "KismetMathLibrary",
                parmsSize:    parmsSize,
                paramsHex:    paramsHexClean,
                directCall:   true);  // bypass GameThreadDispatch (Native|Static)
        }
        catch (Exception ex) when (ex.Message.Contains("Function not found")
                                   || ex.Message.Contains("No instance found"))
        {
            // Not on this game — try next candidate.
            return null;
        }

        // Decode return value from result_hex (DLL returns full params buffer
        // post-call). result=0 means ProcessEvent dispatch reported success;
        // we still verify by reading the return slot to catch wrong-hook cases.
        // A non-zero result means the call never happened — the caller needs that
        // to avoid explaining an untouched buffer as a no-op.
        double actual = DecodeReturnFromHex(res.ResultHex, cand.ReturnOffset, cand.ReturnType);
        bool passed = res.Result == 0 && ValuesMatch(actual, cand.Expected, cand.ReturnType);
        return (actual, res.ResultHex, passed, res.Result);
    }

    /// <summary>Parse N bytes from result_hex at byte offset, interpret as the
    /// given UE type, return as double for uniform handling. Returns NaN on
    /// any malformed input — caller treats that as "fail".</summary>
    private static double DecodeReturnFromHex(string resultHex, int byteOffset, string type)
    {
        if (string.IsNullOrEmpty(resultHex)) return double.NaN;
        int hexOffset = byteOffset * 2;
        int needBytes = type == "double" ? 8 : 4;
        if (resultHex.Length < hexOffset + needBytes * 2) return double.NaN;

        var bytes = new byte[needBytes];
        for (int i = 0; i < needBytes; ++i)
        {
            if (!byte.TryParse(
                    resultHex.AsSpan(hexOffset + i * 2, 2),
                    NumberStyles.HexNumber, CultureInfo.InvariantCulture, out bytes[i]))
                return double.NaN;
        }
        return type switch
        {
            "int32"  => BitConverter.ToInt32(bytes, 0),
            "float"  => BitConverter.ToSingle(bytes, 0),
            "double" => BitConverter.ToDouble(bytes, 0),
            _        => double.NaN,
        };
    }

    private static bool ValuesMatch(double actual, double expected, string type)
    {
        if (double.IsNaN(actual)) return false;
        if (type == "int32") return (int)actual == (int)expected;
        // float/double: epsilon comparison — Add_FloatFloat(3,4) is exact 7.0
        // but defending against rounding noise on Multiply_DoubleDouble etc.
        return Math.Abs(actual - expected) < 1e-5;
    }

    private static string FormatActual(double v, string type) => type switch
    {
        "int32" => ((int)v).ToString(CultureInfo.InvariantCulture),
        _       => v.ToString("0.######", CultureInfo.InvariantCulture),
    };
}
