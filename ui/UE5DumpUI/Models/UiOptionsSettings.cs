using System.Text.Json.Serialization;

namespace UE5DumpUI.Models;

/// <summary>
/// Persisted panel/Live-Walker OPTIONS (stable user preferences only) — the
/// toggles, dropdowns, formats and limits the user sets and expects to survive a
/// UI restart. Stored globally (machine-wide) as a single JSON file under
/// %LOCALAPPDATA%\UE5CEDumper\ui-options.json. See <see cref="Services.UiOptionsStore"/>.
///
/// Deliberately EXCLUDED (session-only by nature): search/filter/query text boxes,
/// live-data selections + results, addresses, one-shot modes, transient view state
/// (tab index, panel-collapse), busy/status flags. Also excluded because already
/// persisted elsewhere: snapshot quota + the experimental opt-in (ExperimentalGate /
/// experimental.json), UE-version override + invoke timeout (DLL per-game state),
/// teleport hotkeys (TeleportHotkeyStore), per-game class denylists (SnapshotStore).
///
/// Each sub-object's property defaults MUST match the corresponding ViewModel's
/// [ObservableProperty] initializer — a value missing from an older file hydrates
/// to the model default, which must equal what the VM would otherwise show.
///
/// NOTE: the JSON context intentionally does NOT use DefaultIgnoreCondition. Several
/// options default to true (DedupSharedObjects, GameOnly, ParallelScan …); with
/// WhenWritingDefault a user turning one OFF (= the bool type-default false) would be
/// omitted and silently revert to ON on the next launch. Every field is written.
/// </summary>
public sealed class UiOptionsSettings
{
    /// <summary>Schema version for future migrations (v1 = initial).</summary>
    public int SchemaVersion { get; set; } = 1;

    public MainUiOptions Main { get; set; } = new();
    public LiveWalkerUiOptions LiveWalker { get; set; } = new();
    public ValueSearchUiOptions ValueSearch { get; set; } = new();
    public SnapshotUiOptions Snapshot { get; set; } = new();
    public InstanceFinderUiOptions InstanceFinder { get; set; } = new();
    public PropertySearchUiOptions PropertySearch { get; set; } = new();
    public TeleportUiOptions Teleport { get; set; } = new();
    public SpcUiOptions Spc { get; set; } = new();
    public PivotUiOptions Pivot { get; set; } = new();
    public InterestingFuncsUiOptions InterestingFuncs { get; set; } = new();
    public InterestingPropsUiOptions InterestingProps { get; set; } = new();
    public ConsoleUiOptions Console { get; set; } = new();
    public GameClassFilterUiOptions GameClassFilter { get; set; } = new();
    public ProxyDeployUiOptions ProxyDeploy { get; set; } = new();
    public SystemUiOptions System { get; set; } = new();
}

/// <summary>System-tab maintenance preferences.</summary>
public sealed class SystemUiOptions
{
    /// <summary>
    /// Compress archived logs older than <c>Constants.LogAutoCompressMinAgeDays</c> at
    /// startup. <b>Default OFF</b> — opt-in. It is cheap (2.8 s for a 102 MB backlog, idle
    /// priority) and reversible (<c>compact /u</c>), but it rewrites files on disk without
    /// being asked, and a launch that silently touches the user's logs is not something to
    /// turn on for them. The manual button next to it does the same work on demand.
    ///
    /// <para>Read directly by <c>App.OnFrameworkInitializationCompleted</c> as well as by
    /// the checkbox, because the sweep has to start before the window exists.</para>
    /// </summary>
    public bool AutoCompressLogs { get; set; }
}

/// <summary>Top-bar display controls (master — fan out to child VMs on change).</summary>
public sealed class MainUiOptions
{
    public int SelectedAddressFormatIndex { get; set; }
    public bool CollapsePointerNodes { get; set; }
    public int ArrayLimitExponent { get; set; } = 7;
    public int DropDownLimitExponent { get; set; } = 9;
    public int CsxDrilldownDepth { get; set; }
    public int PreviewLimit { get; set; } = 2;
    public int DeepScanElemCapExponent { get; set; } = 8;
    public int CeStringLengthExponent { get; set; } = 8;   // 2^8 = 256 (CE String leaf <Length>)
    public int FabricateArrayCountExponent { get; set; } = 2;   // 2^2 = 4 rows (default); 0 = off, 2^N = Copy CE Field array fabricate count
}

public sealed class LiveWalkerUiOptions
{
    public bool CollapseChain { get; set; }
    public bool DescShowOffset { get; set; }
    public bool DescShowType { get; set; }
    // AOB-anchored CE export preference (the "AOB" item in the Live Walker export-
    // options dropdown). Persisted as the user's INTENT: a fallback-GWorld game that
    // force-unchecks the live box must NOT overwrite this. Default OFF (matches the
    // VM's _aobSymbolPreference). See LiveWalkerViewModel.ReconcileAobSymbol.
    public bool UseAobSymbol { get; set; }
    public bool FlattenGasAttributes { get; set; }
    public bool FlattenLeafStructs { get; set; }
    public bool FlattenLeafRecords { get; set; }
    public bool CollapseLeafPointers { get; set; }
    // Alternating record-row colours (RGB hex "RRGGBB"; null = unset). Default: on, Even = azure.
    public bool FlattenColorEnabled { get; set; } = true;
    public string? FlattenColorEven { get; set; } = "0080FF";
    public string? FlattenColorOdd { get; set; }
    public bool DedupSharedObjects { get; set; } = true;
    public bool ExcludeSystemComponents { get; set; } = true;
    public int GWorldLocateDepth { get; set; } = 7;
    public bool GWorldLocateDeep { get; set; }
    public int AutoRefreshIntervalSec { get; set; } = 10;   // Constants.DefaultAutoRefreshIntervalSec
}

public sealed class ValueSearchUiOptions
{
    public ValueScanDataType SelectedDataType { get; set; } = ValueScanDataType.Int32;
    public ValueScanType SelectedScanType { get; set; } = ValueScanType.Exact;
    public bool GameOnly { get; set; } = true;
    public int MaxResults { get; set; } = Constants.DefaultMaxQueryRows;
    public int ScanTimeoutSeconds { get; set; } = 25;
    public bool ParallelScan { get; set; } = true;
    public bool BatchRead { get; set; } = true;
    public bool DeepScan { get; set; }
    public bool CrossObjectScan { get; set; }
    public bool NativeCScan { get; set; }
    public bool NewestFirst { get; set; }
    public bool PreFilterNoise { get; set; }
    public FloatRoundMode RoundingMode { get; set; } = FloatRoundMode.Round;
    public bool CaseSensitive { get; set; }
}

public sealed class SnapshotUiOptions
{
    public bool GameOnly { get; set; } = true;
    public bool AutoSkipNoise { get; set; } = true;
    public bool IncludeNativeFields { get; set; }
    public string SelectedScope { get; set; } = "NumericNoByte";
    public string SelectedFamily { get; set; } = "All numeric";
    public string SelectedMaxDataset { get; set; } = "Off";
    public bool ShowUsageBar { get; set; } = true;
    public bool GroupDeep { get; set; }
    public FloatRoundMode RoundingMode { get; set; } = FloatRoundMode.Round;

    // Auto snapshot SETTINGS (defaults MUST match SnapshotViewModel's [ObservableProperty]
    // initializers). The running toggle (AutoSnapshotEnabled) is deliberately NOT here —
    // it is session-only and never auto-resumes.
    public int AutoSnapshotIntervalSec { get; set; } = 900;
    public AutoSnapshotRetentionMode RetentionMode { get; set; } = AutoSnapshotRetentionMode.KeepRecent;
    public int AutoSnapshotCount { get; set; } = 10;
    public bool AutoSnapshotAdjustQuota { get; set; }
    public int SnapshotMinFreePercent { get; set; } = 10;
    public int SnapshotMinFreeGb { get; set; } = 50;
}

public sealed class InstanceFinderUiOptions
{
    public bool ExactMatch { get; set; }
    public bool NewestFirst { get; set; }
    public int InstanceSearchCap { get; set; } = Constants.DefaultInstanceSearchCap;
    public int DeepScanElemCap { get; set; } = 256;
}

public sealed class PropertySearchUiOptions
{
    public bool GameClassesOnly { get; set; } = true;
    public bool DeepSearch { get; set; }
    public int PropertySearchCap { get; set; } = Constants.DefaultPropertySearchCap;
}

public sealed class TeleportUiOptions
{
    public double ZOffset { get; set; } = 100.0;
    public int TraceChannel { get; set; }
    public bool FallbackToCenter { get; set; } = true;
    public bool CursorHotkeyEnabled { get; set; }
    public double RelativeDistance { get; set; } = 100.0;
    public bool RelativeHorizontal { get; set; } = true;
    public bool CoordSetRotation { get; set; }
    public bool AutoRefresh { get; set; }
    // Time dilation (Hemmung) — last-used per-lever slider values (whole-world +
    // player), so the Time card pre-fills the user's preference across UI restarts.
    // The two levers are independent and held together by the DLL. NOT auto-applied
    // on load (the live DLL state, read back on connect, wins when a dilation is
    // held). Defaults MUST match TeleportViewModel's _worldTimeDilation /
    // _pawnTimeDilation.
    public double WorldTimeDilation { get; set; } = 1.0;
    public double PawnTimeDilation { get; set; } = 1.0;
}

public sealed class SpcUiOptions
{
    public string SelectedJoinMode { get; set; } = "Strict";
    public FloatRoundMode RoundingMode { get; set; } = FloatRoundMode.Round;
}

public sealed class PivotUiOptions
{
    public string SelectedSource { get; set; } = "Snapshot";
    public string SelectedKeyMode { get; set; } = "Identity (object path)";
}

public sealed class InterestingFuncsUiOptions
{
    public bool GameOnly { get; set; } = true;
    public bool ShowAll { get; set; }
}

public sealed class InterestingPropsUiOptions
{
    public bool GameOnly { get; set; } = true;
    public bool UnusualOnly { get; set; }
    public bool ShowAll { get; set; }
}

public sealed class ConsoleUiOptions
{
    public bool GameOnly { get; set; }
}

public sealed class GameClassFilterUiOptions
{
    public bool GameClassesOnly { get; set; } = true;
    public int ClassListCap { get; set; } = Constants.DefaultClassListCap;
}

public sealed class ProxyDeployUiOptions
{
    public ProxyType SelectedProxyType { get; set; } = ProxyType.Version;
    public bool ForceOverwrite { get; set; }

    /// <summary>Scan source: false = Steam library (default), true = generic drive scan.</summary>
    public bool ScanDrivesMode { get; set; }

    /// <summary>Opt-in (default ON): show the per-game suggested-proxy column.</summary>
    public bool LkgSuggestEnabled { get; set; } = true;

    /// <summary>Proxy the user last deployed per game, keyed by game folder name
    /// (stable across reinstall/patch). Feeds the suggestion as a mini "last known
    /// good". Written whenever a deploy records a new pick.</summary>
    public Dictionary<string, ProxyType> LastManualProxyByGame { get; set; } = new();

    /// <summary>.exe file names of games the user has successfully loaded via UI
    /// injection. Feeds the "injection · no proxy deployed" known-good suggestion.</summary>
    public List<string> InjectedGameExes { get; set; } = new();

    /// <summary>Proxy CONFIRMED to have actually loaded a game (the DLL self-reported
    /// a proxy load_mode and the session stayed connected past the stability dwell),
    /// keyed by .exe file name. The strongest known-good signal — takes priority over
    /// a merely-deployed pick. Survives reinstall/patch (exe name is stable).</summary>
    public Dictionary<string, ProxyType> ConfirmedProxyByExe { get; set; } = new();
}

/// <summary>
/// Source-generated JSON context (AOT/trimming — reflection JSON is disabled).
/// All nested sub-objects are reachable from the root type, so only the root needs
/// registering. WriteIndented for human-readability; NO DefaultIgnoreCondition
/// (every field is written — see the UiOptionsSettings remarks).
/// </summary>
[JsonSerializable(typeof(UiOptionsSettings))]
// Explicitly registered (mirrors AobUsageJsonContext's Dictionary registration) so
// the enum-valued map round-trips under source-gen without reflection fallback.
[JsonSerializable(typeof(Dictionary<string, ProxyType>))]
[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal partial class UiOptionsJsonContext : JsonSerializerContext
{
}
