namespace UE5DumpUI.Models;

/// <summary>
/// One UFunction observed firing through the game's ProcessEvent during a
/// Live PE Profiler recording window (<c>pe_profile_get</c>). Ranked by
/// <see cref="Count"/> — the behaviour-based counterpart to the name-heuristic
/// Interesting Functions finder: the function the game ACTUALLY called when the
/// user performed an in-game action.
///
/// Init-only (hand-parsed from the pipe JsonObject like <see cref="AllFunctionEntry"/>): the Start → Stop → Get
/// flow produces an immutable snapshot per fetch. The one exception is <see cref="IsTicked"/>, which the call
/// trace's tick column changes on a row already on screen, so the row notifies.
/// </summary>
public sealed partial class PeProfileEntry : CommunityToolkit.Mvvm.ComponentModel.ObservableObject
{
    /// <summary>[LIVEFUNCS-TIMELINE-2026-10-04] Ticked for the call trace: the next traced Start records only this
    /// function's calls and what they call. The ticks live in LiveFuncsViewModel, keyed by Class::Func; a fetch
    /// copies them onto its new rows.</summary>
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty] private bool _isTicked;

    /// <summary>[LIVEFUNCS-STEP2] Chosen for parameter snapshots: the next traced Start copies its parameter block on
    /// each call. Kept by name in LiveFuncsViewModel, like the ticks.</summary>
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty] private bool _isSnapChosen;

    /// <summary>[LIVEFUNCS-STEP3] Chosen for a native stack: the next traced Start takes the return addresses above each
    /// of its calls. Kept by name in LiveFuncsViewModel, like the parameter choice.</summary>
    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty] private bool _isStackChosen;

    public string ClassName { get; init; } = "";
    public string FuncName  { get; init; } = "";
    public string FuncAddr  { get; init; } = "";
    public byte   NumParms  { get; init; }
    public ushort ParmsSize { get; init; }
    public long   Count     { get; init; }
    /// <summary>Call-stream position of this function's FIRST fire during the recording
    /// (1-based; 0 if unknown). The causal signal: an action's entry point fires before
    /// the reactions it triggers, so sorting NEW rows by FirstSeq ascending puts the true
    /// opener above its downstream widget-creation / On* effects.</summary>
    public long   FirstSeq  { get; init; }
    public uint   FunctionFlags { get; init; }

    /// <summary>[TRACE-UNLOADED-NAMES] The game unloaded this function after it fired (a closed UI, content streamed
    /// out): every field is what the recording read at its first call, and <see cref="FuncAddr"/> is dead -- never
    /// sent back to the DLL (a trace tick, a disassembly).</summary>
    public bool   IsUnloaded { get; init; }
    /// <summary>With <see cref="IsUnloaded"/>: another function has taken the address since.</summary>
    public bool   IsRecycled { get; init; }
    /// <summary>Another function took this address DURING the recording (review DLL-3): the count is both functions',
    /// and the name the latest one's.</summary>
    public bool   IsReused   { get; init; }
    /// <summary>[LIVEFUNCS-STEP2] The name key the row's names are rendered from (T10): what a tick or a snapshot
    /// choice by name sends back. Null from a DLL that predates it.</summary>
    public NameKey? FnameKey { get; init; }
    /// <summary>[LIVEFUNCS-STEP2] The function fired every frame through the recording (the DLL's IsPerFrame).</summary>
    public bool   IsPerFrame { get; init; }

    /// <summary>[LIVEFUNCS-STEP2] A row the trace can tick: by name when it has a key, else by a live address (D1).</summary>
    public bool IsTickable => FnameKey != null || (!IsUnloaded && !string.IsNullOrEmpty(FuncAddr));

    /// <summary>[LIVEFUNCS-STEP2] A row whose parameters can be chosen: chosen by name, so it needs a key, unloaded or
    /// not; left out when the DLL read it as having no parameters. Flags of 0 are an offset never decided, not
    /// "none": such a row stays choosable and the DLL decides.</summary>
    public bool CanChooseSnapshot => FnameKey != null && !(NumParms == 0 && FunctionFlags != 0);

    /// <summary>[LIVEFUNCS-STEP3] A row whose native stack can be chosen: by name, so it needs a key. Every call has a
    /// stack, so <see cref="CanChooseSnapshot"/>'s parameters rule does not apply (D1).</summary>
    public bool CanChooseStack => FnameKey != null;

    // UE FunctionFlags (ObjectMacros.h) relevant to "is this a thing I can CALL vs
    // an event the engine fires AT me". Event/delegate signatures are reactions,
    // not entry points — the flood you see when profiling an interaction.
    private const uint FUNC_Event             = 0x0000_0800;
    private const uint FUNC_MulticastDelegate = 0x0001_0000;
    private const uint FUNC_Delegate          = 0x0010_0000;
    private const uint FUNC_BlueprintCallable = 0x0400_0000;
    private const uint FUNC_BlueprintEvent    = 0x0800_0000;
    private const uint FUNC_Native            = 0x0000_0400;

    /// <summary>Event handler / delegate signature — a reaction the engine dispatches
    /// (On*/callbacks), NOT an imperative function you'd invoke to drive the flow.</summary>
    public bool IsEventLike =>
        (FunctionFlags & (FUNC_Event | FUNC_MulticastDelegate | FUNC_Delegate)) != 0;

    /// <summary>Short kind badge: "Deleg" / "Event" / "Call" / "native" / "".</summary>
    public string TypeLabel =>
        (FunctionFlags & (FUNC_MulticastDelegate | FUNC_Delegate)) != 0 ? "Deleg"
        : (FunctionFlags & (FUNC_Event | FUNC_BlueprintEvent)) != 0     ? "Event"
        : (FunctionFlags & FUNC_BlueprintCallable) != 0                 ? "Call"
        : (FunctionFlags & FUNC_Native) != 0                           ? "native"
        : "";

    /// <summary>Owning class derives from UUserWidget/UWidget — the transient UI
    /// created BY the action (e.g. a shop widget), not its opener. The UI can hide
    /// these so the persistent opener (controller / subsystem / component) surfaces.</summary>
    public bool   IsWidget  { get; init; }

    /// <summary>"NumParms (ParmsSize B)" e.g. "2 (5B)"; empty for no-arg funcs.</summary>
    public string ParamsLabel => NumParms == 0 ? "" : $"{NumParms} ({ParmsSize}B)";

    // --- Cadence (Phase E): inter-arrival stats from the DLL, so the UI can flag a
    // regularly-firing TIMER callback vs Tick vs an input-driven one-off. ---

    /// <summary>Mean inter-arrival gap between fires (ms); 0 with fewer than 2 fires.</summary>
    public double MeanPeriodMs { get; init; }
    /// <summary>Coefficient of variation of the gaps (stddev/mean) — regularity: a
    /// steady timer is near 0, a bursty/input-driven function is high. 0 with &lt;2 gaps.</summary>
    public double Cv { get; init; }
    /// <summary>Number of inter-arrival gaps measured (== Count-1). Need enough to
    /// trust the cadence (a couple of fires prove nothing).</summary>
    public long GapSamples { get; init; }

    // Classification bands. A timer callback fires at a steady period (low CV) that
    // is NOT the per-frame Tick band and is within a plausible gameplay-timer window.
    private const double PeriodicCvMax     = 0.25;    // ≤ ⇒ regular
    private const double FrameBandMaxMs    = 40.0;    // ≤ ⇒ likely Tick (per-frame), not a timer
    private const double TimerBandMaxMs    = 30_000.0;// ≥ ⇒ too slow to be a gameplay timer here
    private const long   MinGapSamples     = 3;       // need ≥3 gaps (≥4 fires) to trust the CV

    /// <summary>Heuristic: this function fires at a regular cadence consistent with a
    /// TIMER callback (SetTimerByFunctionName / a BP timer event) rather than Tick or
    /// input. Requires enough samples, a low CV, and a period outside the per-frame
    /// band but within a plausible gameplay-timer window. An aid, not proof — native
    /// C++/lambda timers bypass ProcessEvent entirely and never appear here.</summary>
    public bool IsPeriodic =>
        GapSamples >= MinGapSamples &&
        Cv <= PeriodicCvMax &&
        MeanPeriodMs > FrameBandMaxMs &&
        MeanPeriodMs <= TimerBandMaxMs;

    /// <summary>Human period readout for the grid: "0.5 s" / "250 ms" / "" (too few
    /// fires). Independent of <see cref="IsPeriodic"/> so any measured cadence shows.</summary>
    public string PeriodLabel =>
        GapSamples < 1 ? "" :
        MeanPeriodMs >= 1000.0 ? $"{MeanPeriodMs / 1000.0:0.##} s"
                               : $"{MeanPeriodMs:0} ms";

    /// <summary>Small kind badge for the grid — "Timer" marks a periodic callback,
    /// else "UI" marks a transient widget method.</summary>
    public string Kind => IsPeriodic ? "Timer" : IsWidget ? "UI" : "";

    // --- Baseline-diff display state (set by LiveFuncsViewModel when Diff mode is
    // on; 0 / false otherwise). The diff is client-side — the DLL knows nothing of
    // it. Mutable by design: rows are transient per fetch and never persisted. ---

    /// <summary>Count minus this Class::Func's baseline count (== Count when new).</summary>
    public long Delta { get; set; }
    /// <summary>True when this Class::Func was absent from the baseline entirely —
    /// the strongest "fired because of the action" signal.</summary>
    public bool IsNew { get; set; }

    /// <summary>Δ-column label: "NEW" (absent from baseline) / "+N" (fired more) /
    /// "" (unchanged, or not in diff mode) / "-N" (fired less).</summary>
    public string DeltaLabel =>
        IsNew ? "NEW" : Delta > 0 ? $"+{Delta}" : Delta < 0 ? Delta.ToString() : "";
}

/// <summary>
/// Result of <c>pe_profile_start</c>. <see cref="HookActive"/> false means the
/// game-thread ProcessEvent hook isn't installed, so counts will stay 0;
/// <see cref="Detail"/> is the DLL's specific reason (ProcessEvent not detected
/// vs found-but-hook-couldn't-install) so the UI can advise the right remedy.
/// </summary>
public sealed class PeProfileStartResult
{
    public bool   HookActive { get; init; }
    public string Detail     { get; init; } = "";
    /// <summary>[LIVEFUNCS-TIMELINE-2026-10-04] The armed trace, when the Start asked for one.</summary>
    public TraceInfo? Trace  { get; init; }
}

/// <summary>
/// Result of <c>pe_profile_get</c>: the ranked fire-count table plus the
/// aggregate counters (distinct functions + total PE calls) shown in the status
/// line so the user can gauge how much fired during the window.
/// </summary>
public sealed class PeProfileResult
{
    /// <summary>[LIVEFUNCS-HIDE-PERFRAME] How many distinct functions the DLL left out as per-frame (still counted in
    /// <see cref="DistinctFuncs"/>); null when it was not asked, or is older than the option and left nothing out.</summary>
    public int? PerFrameHidden { get; init; }
    /// <summary>The addresses (func_addr) of those functions, when the DLL names them (build 3630+); empty otherwise.</summary>
    public IReadOnlyList<string> PerFrameFuncs { get; init; } = Array.Empty<string>();

    public bool Recording     { get; init; }
    public int  DistinctFuncs { get; init; }
    public long TotalCalls    { get; init; }
    /// <summary>[LIVEFUNCS-TIMELINE-2026-10-04] The window the counts cover (ms), from the table's earliest fire to its
    /// latest; null from a DLL older than the field. With <see cref="TotalCalls"/> it is the call rate the trace
    /// slider estimates its seconds from.</summary>
    public long? WindowMs     { get; init; }
    /// <summary>[TRACE-UNLOADED-NAMES] Over the whole table, not the page: the functions no longer at their address
    /// (sent as rows marked <see cref="PeProfileEntry.IsUnloaded"/>, named from their first call) and the ones with no
    /// name at all (never sent), with their calls. Null from a DLL older than the fields.</summary>
    public int?  UnloadedFuncs { get; init; }
    public long? UnloadedCalls { get; init; }
    public int?  UnnamedFuncs  { get; init; }
    public long? UnnamedCalls  { get; init; }
    public List<PeProfileEntry> Entries { get; init; } = new();
}
