using CommunityToolkit.Mvvm.ComponentModel;

namespace UE5DumpUI.Models;

/// <summary>
/// Data types supported by the Value Search workflow. Wire string
/// matches DLL-side <c>ValueScan::DataType</c> exactly so the pipe
/// round-trip is just <c>nameof(ValueScanDataType.X)</c>.
///
/// Numeric primitives shipped in build 738 (MVP). Phase 2 (build 750)
/// added FString / FName / FText (Phase 2A) and FVector / FRotator
/// (Phase 2B); FTransform is wire-stable but the DLL returns zero
/// candidates pending per-version Translation offset detection. See
/// memory project_value_search_caveats for the TArray&lt;T&gt; gap.
///
/// Native C++ fields (non-UPROPERTY) carry no reflection metadata, so no
/// <c>ValueScanDataType</c> reaches them through the property walk. They ARE
/// reachable with the opt-in Native-C scan (<c>nativeC: true</c>), which reads
/// each object's unmanaged holes raw at the requested width — numeric types
/// only. The UI Value Search tab MUST surface both: the limitation and the
/// toggle that lifts it. "Unreachable regardless of data type" was written
/// before Native-C shipped and is no longer true. (audit #5 AE31)
/// </summary>
public enum ValueScanDataType
{
    // Numeric primitives (MVP, build 738)
    Int8,
    Int16,
    Int32,
    Int64,
    UInt8,
    UInt16,
    UInt32,
    UInt64,
    Float,
    Double,
    Bool,
    // String types (Phase 2A, build 750)
    FString,
    FName,
    FText,
    // Vector types (Phase 2B, build 750). FTransform reserved but
    // currently returns zero hits — see VectorStructNames in DLL.
    FVector,
    FRotator,
    FTransform,
    // Multi-numeric meta type (build 794). Scans every word/dword/qword/
    // float/double UPROPERTY in one pass, comparing the value against
    // each field using that field's own declared width — no need to
    // know whether the value is stored as int or float up front. The
    // "no byte" variant excludes 1-byte (Int8/UInt8/Bool) fields to keep
    // the candidate set from exploding on small values. Wire string
    // "NumericNoByte" matches DLL ValueScan::DataType::NumericNoByte.
    NumericNoByte,
    // Multi-numeric meta type WITH 1-byte fields (build 796). Same as
    // NumericNoByte but additionally includes Int8/UInt8 (still excludes
    // Bool). WARNING: small values (0/1/255) match a very large number of
    // 1-byte fields — the VM surfaces a result-volume warning when this is
    // selected. Wire string "NumericAll" matches DLL DataType::NumericAll.
    NumericAll,
}

/// <summary>
/// Scan comparison predicates. Targeted predicates (Exact / Bigger /
/// Smaller / Between / Contains / StartsWith / EndsWith) compare
/// against user-supplied values; prev-value predicates (Changed /
/// Unchanged / Increased / Decreased) compare against each candidate's
/// stored snapshot from the previous round.
///
/// Per-type validity:
///   Numeric: Exact / Bigger / Smaller / Between / Changed / Unchanged
///            / Increased / Decreased
///   String:  Exact / Contains / StartsWith / EndsWith / Changed / Unchanged
///   Vector:  Exact / Bigger / Smaller / Between / Changed / Unchanged
///            / Increased / Decreased  (component-wise per axis)
/// </summary>
public enum ValueScanType
{
    Exact,
    Bigger,
    Smaller,
    Between,
    Changed,
    Unchanged,
    Increased,
    Decreased,
    // String-only substring predicates (Phase 2A, build 750)
    Contains,
    StartsWith,
    EndsWith,
}

/// <summary>
/// One row in the Value Search result DataGrid. Cached metadata from
/// the FIRST scan is reused across all refines so the user can sort /
/// filter on class/field/owner without DLL round-trips.
/// </summary>
public class ValueCandidate
{
    public string Addr              { get; set; } = "";   // "0x..."
    public string InstanceAddr      { get; set; } = "";   // "0x..."
    public int    InstanceIndex     { get; set; }
    public int    FieldOffset       { get; set; }
    public string InstanceName      { get; set; } = "";
    public string ClassName         { get; set; } = "";
    public string DefiningClassName { get; set; } = "";
    public string FieldName         { get; set; } = "";
    public string FieldType         { get; set; } = "";   // "FloatProperty" / ...
    public byte   BoolFieldMask     { get; set; } = 0xFF;
    public string Value             { get; set; } = "";   // formatted current value

    /// <summary>Native-C (P1): true when this hit came from an UNMANAGED hole
    /// (a non-UPROPERTY raw offset) rather than a reflected field. Drives the
    /// "Origin" badge so users can tell native guesses from reflected matches.</summary>
    public bool   IsNativeField     { get; set; }
    /// <summary>Native-C: the width the raw bytes were interpreted at (e.g.
    /// "Int32"). Empty for reflected fields.</summary>
    public string GuessedType       { get; set; } = "";

    public string OffsetHex => $"0x{FieldOffset:X}";

    /// <summary>Origin badge for the DataGrid: "Native-C (Int32)" for a raw-hole
    /// hit, "Reflected" otherwise. Lets the user sort/group native guesses apart
    /// from reflected matches (native first-scan is intentionally noisy).</summary>
    public string Origin => IsNativeField
        ? (string.IsNullOrEmpty(GuessedType) ? "Native-C" : $"Native-C ({GuessedType})")
        : "Reflected";

    /// <summary>Compact location label for the DataGrid: "ClassName.FieldName".
    /// If the defining class differs from the owning class, it's shown
    /// in brackets to surface inheritance ("BP_Player_C.Health (ACharacter)").</summary>
    public string LocationLabel
    {
        get
        {
            if (!string.IsNullOrEmpty(DefiningClassName)
                && DefiningClassName != ClassName)
            {
                return $"{ClassName}.{FieldName}  ({DefiningClassName})";
            }
            return $"{ClassName}.{FieldName}";
        }
    }
}

/// <summary>
/// Response from <c>begin_value_scan</c>. The DLL retains the full
/// candidate set in a session keyed by <see cref="SessionId"/>; the
/// client echoes the same vector here on first scan and on every
/// refine. <see cref="DeadlineHit"/> tells the UI to show a "scan
/// truncated" warning so the user knows to narrow the predicate.
/// </summary>
/// <summary>One class-noise histogram entry: a class name and how many of the
/// session's candidates belong to it. The DLL computes this over the WHOLE
/// session (pre-filter, pre-exclude) and returns the Top-N; the Value Search
/// class-filter picker is driven by it (server-side sibling of the client-side
/// histogram on the Instance / Interesting / Property Search tabs).</summary>
public sealed class ClassCount
{
    public string ClassName { get; set; } = "";
    public int    Count     { get; set; }
}

public class ValueScanBeginResult
{
    public ulong  SessionId      { get; set; }
    public string DataType       { get; set; } = "";
    public int    Total          { get; set; }
    public int    ScannedClasses { get; set; }
    public int    ScannedObjects { get; set; }
    public long   DurationMs     { get; set; }
    public bool   DeadlineHit    { get; set; }
    public List<ValueCandidate> Candidates { get; set; } = new();
    /// <summary>Top-N class histogram over the full session (for the class filter).</summary>
    public List<ClassCount> ClassHistogram { get; set; } = new();
    /// <summary>True number of distinct classes (≥ ClassHistogram.Count when capped).</summary>
    public int ClassDistinct { get; set; }
}

/// <summary>Response from <c>refine_value_scan</c>. <see cref="Total"/> is the
/// surviving count (the full pruned set lives in the DLL session);
/// <see cref="Candidates"/> is only the FIRST PAGE in scan order (V3-C). The
/// UI re-pages / filters / sorts via <c>query_candidates</c>.</summary>
public class ValueScanRefineResult
{
    public ulong  SessionId  { get; set; }
    public string DataType   { get; set; } = "";
    public string ScanType   { get; set; } = "";
    public int    Total      { get; set; }
    public long   DurationMs { get; set; }
    public List<ValueCandidate> Candidates { get; set; } = new();
    public List<ClassCount> ClassHistogram { get; set; } = new();
    public int ClassDistinct { get; set; }
}

/// <summary>Response from <c>query_candidates</c> (V3-C server-side window).
/// The DLL filters + sorts the WHOLE session set and returns only the
/// requested window. <see cref="Total"/> is the full session size,
/// <see cref="FilteredTotal"/> the count after the keyword filter, and
/// <see cref="Candidates"/> the [<see cref="Offset"/>, Offset+limit) slice in
/// the requested sort order.</summary>
public class ValueScanWindowResult
{
    public ulong  SessionId     { get; set; }
    public string DataType      { get; set; } = "";
    public int    Total         { get; set; }
    public int    FilteredTotal { get; set; }
    public int    Offset        { get; set; }
    public List<ValueCandidate> Candidates { get; set; } = new();
}

// ===================== Multiple values group scan (build 1276) =====================

/// <summary>
/// One input value row of a "Group" scan (2..4 rows). The user supplies a value,
/// a per-slot width scope, and (P2) a per-slot scan type. The width scope fans
/// out over numeric widths (NumericNoByte default / NumericAll). The scan type is
/// a first-scan targeted predicate (Exact / Bigger / Smaller) on the first scan,
/// and additionally a prev-value predicate (Changed / Unchanged / Increased /
/// Decreased) on a refine — for those the value box is hidden. Observable so the
/// editable grid's value-cell visibility reacts to the scan-type cell.
/// </summary>
public partial class GroupSlotInput : ObservableObject
{
    [ObservableProperty] private ValueScanDataType _dataType = ValueScanDataType.NumericNoByte;
    [ObservableProperty] private ValueScanType     _scanType = ValueScanType.Exact;
    [ObservableProperty] private string            _value    = "";
    [ObservableProperty] private string            _value2   = "";

    /// <summary>The owning panel's rounding mode, pushed in by the VM so this row can
    /// render its own live <see cref="BetweenPreview"/>. Display-only — the actual
    /// scan always uses the panel-level mode; never persisted on the row.</summary>
    [ObservableProperty] private FloatRoundMode _roundMode = FloatRoundMode.Round;

    /// <summary>False for prev-value predicates (Changed / Increased / Decreased /
    /// Unchanged): they compare against the previous round, so no value is needed
    /// and the grid hides the value box — mirroring single mode.</summary>
    public bool RequiresValueInput => !IsPrevValueScanType(ScanType);

    /// <summary>True only for Between — reveals the second (upper bound) value box.</summary>
    public bool RequiresValue2Input => ScanType == ValueScanType.Between;

    /// <summary>Live "what will this actually match" preview for a Between row, shown
    /// after the upper-bound box (e.g. <c>→ int 12~13 · float 11.5~13.2</c> under Round).
    /// Group rows always scan a mixed numeric corpus → both interpretations. Empty for
    /// non-Between predicates or unparseable bounds (the grid hides the label).</summary>
    public string BetweenPreview => ScanType == ValueScanType.Between
        ? RoundModePreview.Between(Value, Value2, RoundMode, RoundModePreview.Scope.Both)
        : "";

    partial void OnScanTypeChanged(ValueScanType value)
    {
        OnPropertyChanged(nameof(RequiresValueInput));
        OnPropertyChanged(nameof(RequiresValue2Input));
        OnPropertyChanged(nameof(BetweenPreview));
    }

    partial void OnValueChanged(string value)     => OnPropertyChanged(nameof(BetweenPreview));
    partial void OnValue2Changed(string value)    => OnPropertyChanged(nameof(BetweenPreview));
    partial void OnRoundModeChanged(FloatRoundMode value) => OnPropertyChanged(nameof(BetweenPreview));

    private static bool IsPrevValueScanType(ValueScanType st) =>
        st is ValueScanType.Changed or ValueScanType.Unchanged
           or ValueScanType.Increased or ValueScanType.Decreased;
}

/// <summary>
/// One slot's converging match inside a <see cref="GroupCandidate"/>. While the
/// scan narrows, <see cref="MatchedOffsets"/> may list several offsets; once a
/// single offset remains the field is identified (<see cref="Locked"/>). The
/// representative (first) match carries the resolved field name / offset / type
/// + leaf value + leaf address so a per-slot row can drive the same handoffs
/// (Open in Live Walker / Locate in GWorld / Copy) as a single-value candidate.
/// </summary>
public class GroupSlotMatch
{
    public int       SlotIndex      { get; set; }
    public string    Value          { get; set; } = "";   // the slot's target value
    public string    ScanType       { get; set; } = "Exact";  // per-slot predicate (P2)
    public string    Value2         { get; set; } = "";   // Between upper bound (P2)
    public string    FieldName      { get; set; } = "";   // representative match
    public int       FieldOffset    { get; set; }
    public string    FieldType      { get; set; } = "";
    public byte      BoolFieldMask  { get; set; } = 0xFF;
    public string    LeafValue      { get; set; } = "";   // current value at the leaf
    public string    Addr           { get; set; } = "";   // leaf address (owner + offset)
    public string    InstanceAddr   { get; set; } = "";   // candidate actor (denormalized for self-contained handoffs)
    public string    OwnerAddr      { get; set; } = "";   // object directly holding the leaf — actor, or owned sub-object (P4)
    public string    ClassName      { get; set; } = "";   // candidate actor's class (denormalized)
    public string    OwnerClass     { get; set; } = "";   // class of the leaf's direct owner — owned sub-object for a cross-object leaf (P4 inc 2)

    /// <summary>The object a per-slot handoff should target: the leaf's direct owner
    /// (an owned sub-object for a cross-object leaf), falling back to the candidate
    /// actor when the DLL didn't supply one (own-block leaf / older payload).</summary>
    public string HandoffAddr => string.IsNullOrEmpty(OwnerAddr) ? InstanceAddr : OwnerAddr;

    /// <summary>The class a per-slot Pivot handoff should target: the leaf's direct
    /// owner class (the owned sub-object's class for a cross-object leaf), falling back
    /// to the candidate actor's class when the DLL didn't supply one (own-block leaf /
    /// older payload). For an own-block leaf this equals <see cref="ClassName"/>.</summary>
    public string PivotClassName => string.IsNullOrEmpty(OwnerClass) ? ClassName : OwnerClass;
    public List<int> MatchedOffsets { get; set; } = new();
    public bool      Locked         { get; set; }

    /// <summary>True when <see cref="Addr"/> is genuinely THIS LEAF's address.
    /// Set only by the live decoder (<c>DumpService.ParseGroupSlotLeaf</c>), whose
    /// <c>addr</c> comes from <c>GroupSlotMatch::leafAddr</c>. The Snapshot / SPC
    /// builders cannot capture an array element's heap address and put the owning
    /// object's base in <see cref="Addr"/> instead, so for them this stays false and
    /// the detail row shows no location rather than a wrong one.</summary>
    public bool      HasLeafAddress { get; set; }

    /// <summary>How many leaves this slot ACTUALLY kept (DLL <c>match_count</c>, build
    /// 2690+). <see cref="FieldName"/> is ONE witness out of this many, chosen so the
    /// row reads as a real assignment — it is not the only field that matched. Left 0
    /// by the Snapshot / SPC paths, which build these models client-side; they populate
    /// <see cref="MatchedOffsets"/> instead, which is why <c>MatchingFieldCount</c>
    /// falls back to it.</summary>
    public int MatchCount { get; set; }

    /// <summary>Every leaf this slot kept, BY NAME — empty until fetched on demand
    /// via <c>query_group_slot_leaves</c> (live pipe sessions only).
    ///
    /// Exists because a row can only display one assignment. On the DumperTest
    /// sample, slot 0 kept {Health.CurrentValue, TickCount} and slot 1 kept 36
    /// leaves including FrozenInt; the row showed the Health pair, and the only
    /// trace of the rest was <see cref="MatchedOffsets"/> — raw integers that
    /// cannot tell anyone that 1308 is <c>FrozenInt</c>. Reported as a missed
    /// match four separate times.
    ///
    /// Each entry is itself a <see cref="GroupSlotMatch"/> carrying this slot's
    /// predicate and owner, so every per-slot handoff (Live Walker / Copy / Pivot
    /// / Locate) works on a leaf with no extra plumbing. Observable so the lazy
    /// fill reaches the view without the model needing change notification.</summary>
    public System.Collections.ObjectModel.ObservableCollection<GroupSlotMatch> Leaves { get; } = new();

    /// <summary>Gate for the "All fields" button — deliberately STRICTER than
    /// <see cref="HiddenLeavesSuffix"/>. The button fires a pipe command, so it must
    /// require the one signal that proves the server can answer it: <c>match_count</c>,
    /// which only a build-2690+ DLL sends. Falling back to <see cref="MatchedOffsets"/>
    /// here (as the display annotation does) would offer the button against an older
    /// DLL that has no <c>query_group_slot_leaves</c>, and against the Snapshot / SPC
    /// models, which have no pipe session at all.</summary>
    public bool HasHiddenLeaves => MatchCount > 1;

    /// <summary>Master-row annotation: "(+35)" when 35 other fields also matched,
    /// empty when the displayed value is the whole story. Counted through
    /// <c>MatchingFieldCount</c>, so Snapshot Group / SPC Group / Class Pivot get the
    /// same warning from their own offset lists — the ValueSearch-only fix is how
    /// this defect came back as a separate report last time.</summary>
    public string HiddenLeavesSuffix =>
        MatchingFieldCount > 1 ? $" (+{MatchingFieldCount - 1})" : "";

    /// <summary>Native-C (P2): true when this slot matched an UNMANAGED hole (a raw,
    /// non-UPROPERTY offset) rather than a reflected field. Drives the Origin badge.</summary>
    public bool      IsNativeField  { get; set; }
    /// <summary>Native-C: the interpreted width (e.g. "Int32"); empty for reflected.</summary>
    public string    GuessedType    { get; set; } = "";

    /// <summary>Origin badge: "Native-C (Int32)" for a raw-hole slot, else "Reflected".</summary>
    public string Origin => IsNativeField
        ? (string.IsNullOrEmpty(GuessedType) ? "Native-C" : $"Native-C ({GuessedType})")
        : "Reflected";

    public string OffsetHex => $"0x{FieldOffset:X}";

    // Match criterion: the target value for a targeted slot, or a directional
    // token for a prev-value slot (which carries no value).
    private string Criterion => ScanType switch
    {
        "Increased" => "↑ increased",
        "Decreased" => "↓ decreased",
        "Changed"   => "≠ changed",
        "Unchanged" => "= unchanged",
        "Between"   => $"{Value}..{Value2}",
        _           => Value,
    };

    // Where the value lives — or "" when nothing TRUE can be said.
    //
    // A DEEP / container leaf has no object-relative offset (it is reached through a
    // container, so offset is stored as 0), and printing "→ 0x0" for it is false. The
    // absolute address is the honest answer — but ONLY where Addr really is the leaf's
    // address, which is the live pipe path alone (Fern.cpp GroupLeafToJson emits
    // GroupSlotMatch::leafAddr). The Snapshot / SPC builders cannot capture an array
    // element's heap address at all and set Addr = AddrPlusOffset(objAddr, 0) — the
    // OWNING OBJECT's base. Inferring "deep leaf" from `FieldOffset == 0` there would
    // print the UObject header as the place the value lives: a plausible, copyable,
    // WRONG address, which is worse than the obviously-unknown 0x0 it replaced. So the
    // producer states it explicitly instead of the consumer guessing.
    private string Locator =>
        FieldOffset != 0                                     ? OffsetHex
        : HasLeafAddress && !string.IsNullOrEmpty(Addr)      ? Addr
                                                             : "";

    // Omit the arrow entirely rather than point it at something untrue.
    private string LocatorSuffix => Locator.Length > 0 ? $" → {Locator}" : "";

    // How many fields this slot matched. MatchCount is the DLL's own count (2690+);
    // MatchedOffsets is the fallback, which is what the Snapshot / SPC builders fill
    // (SnapshotStore.BuildGroupCandidate / BuildSpcGroupCandidate — one entry per
    // DISTINCT matching field, deliberately not deduped by offset). One number, one
    // preferred source. Display only — the pipe-backed button gates on MatchCount.
    private int MatchingFieldCount => MatchCount > 0 ? MatchCount : MatchedOffsets.Count;

    // Detail-row caption: "Str  24 → 0x20  (IntProperty)" once locked (or
    // "Str  ↑ increased → 0x20 ..." for a prev-value slot); unlocked it names the
    // DISPLAYED field and says how many others matched the same value, because a bare
    // "36 candidate offset(s)" told a user nothing about which fields those were.
    public string DisplayLabel =>
        Locked
            ? $"{(string.IsNullOrEmpty(FieldName) ? "?" : FieldName)}  {Criterion}{LocatorSuffix}  ({FieldType})"
            : $"{(string.IsNullOrEmpty(FieldName) ? "?" : FieldName)}  {Criterion}{LocatorSuffix}"
              + $"  — 1 of {MatchingFieldCount} matching field(s)";
    public string LockLabel => Locked ? "🔒" : $"×{MatchingFieldCount}";
}

/// <summary>
/// One group hit: an owning UObject plus its per-slot matches. A logical match
/// means the object simultaneously holds every slot's value at a distinct
/// offset. Mirrors <see cref="ValueCandidate"/>'s owner fields so the master row
/// can hand off to Instance Finder / Class Pivot.
/// </summary>
public class GroupCandidate
{
    public string InstanceAddr      { get; set; } = "";
    public int    InstanceIndex     { get; set; }
    public string InstanceName      { get; set; } = "";
    public string ClassName         { get; set; } = "";
    public string DefiningClassName { get; set; } = "";
    public List<GroupSlotMatch> Slots { get; set; } = new();

    public string LocationLabel =>
        (!string.IsNullOrEmpty(DefiningClassName) && DefiningClassName != ClassName)
            ? $"{ClassName}  ({DefiningClassName})" : ClassName;
    // Compact master-row summary: "Health.BaseValue=513.36, Health.CurrentValue=513.36".
    // Shows each slot's ACTUAL current leaf value (LeafValue) — NOT the query target /
    // Between bound, which is misleading (an Exact 513 search hides the real 513.36; a
    // Between 510..514 shows the bound 510). Matches the SPC group display, and the
    // rendered LeafValue keeps the int/float distinction (513 vs 513.36). Falls back to
    // the target only if a path left LeafValue empty.
    // The "(+35)" suffix says the displayed field is ONE witness of 36, not the
    // only match. Without it a valid row reads as an exhaustive answer, and the
    // fields it happened not to show read as misses — the report this whole
    // area kept producing. Absent only when the slot matched exactly one field.
    // It DOES appear on the Snapshot / SPC paths: they leave MatchCount at 0 but
    // fill MatchedOffsets, and MatchingFieldCount falls back to that on purpose —
    // fixing this in ValueSearch alone is how the defect came back as its own report.
    public string SlotSummary =>
        string.Join(", ", Slots.Select(s =>
            $"{(string.IsNullOrEmpty(s.FieldName) ? "?" : s.FieldName)}={(string.IsNullOrEmpty(s.LeafValue) ? s.Value : s.LeafValue)}{s.HiddenLeavesSuffix}"));
    public bool AllLocked => Slots.Count > 0 && Slots.All(s => s.Locked);

    // ---- Locked-offset table (P2) ----
    // The actionable output once every slot has converged to a single offset:
    // the class plus each value's byte offset, ready to rebuild a struct / pointer
    // chain. (Export it from Live Walker — the panel only displays it here.)
    public bool HasOffsetTable => AllLocked;
    public string OffsetTable =>
        string.Join(", ", Slots.Select(s =>
            $"{(string.IsNullOrEmpty(s.FieldName) ? "?" : s.FieldName)}@{s.OffsetHex}"));
    public string OffsetTableLabel => $"🔒 {ClassName} — {OffsetTable}";
}

/// <summary>Response from <c>begin_group_scan</c> — object-level candidates.</summary>
public class GroupScanBeginResult
{
    public ulong SessionId      { get; set; }
    public int   Total          { get; set; }
    public int   SlotCount      { get; set; }
    public int   ScannedClasses { get; set; }
    public int   ScannedObjects { get; set; }
    public long  DurationMs     { get; set; }
    public bool  DeadlineHit    { get; set; }
    /// <summary>At least one object matched a slot with MORE fields than the per-slot
    /// leaf cap kept, so that slot's witness list — and the "All fields" popup built
    /// from it — is a page, and a later Changed/Decreased refine can only re-read what
    /// was kept.
    ///
    /// <para>Not the same claim as <see cref="DeadlineHit"/>: that one bounds how many
    /// OBJECTS were examined, this one bounds how many WITNESSES an examined object
    /// kept. Until the DLL added <c>per_slot_cap_hit</c> (audit #5 AE13) the fact
    /// existed only as a <c>LOG_WARN</c> line in the DLL's own log, so no DTO could
    /// carry it and the UI presented a capped list as complete. False on an older DLL,
    /// which is the correct default — it means "no evidence of truncation".</para></summary>
    public bool  PerSlotCapHit  { get; set; }
    /// <summary>The cap the DLL actually applied, after its own 8..4096 clamp. Not
    /// derivable client-side: the request omits the key when it equals the UI default.
    /// 0 from a DLL that predates the key.</summary>
    public int   PerSlotCap     { get; set; }
    public List<GroupCandidate> Candidates { get; set; } = new();
    public List<ClassCount> ClassHistogram { get; set; } = new();
    public int ClassDistinct { get; set; }
}

/// <summary>Response from <c>refine_group_scan</c> — surviving object count + first page.</summary>
public class GroupScanRefineResult
{
    public ulong SessionId  { get; set; }
    public int   Total      { get; set; }
    public long  DurationMs { get; set; }
    /// <summary>Inherited from the Begin scan via the DLL session — a refine prunes the
    /// stored pool and never re-runs the matcher, so it cannot recompute this. (AE13)</summary>
    public bool  PerSlotCapHit { get; set; }
    public int   PerSlotCap    { get; set; }
    public List<GroupCandidate> Candidates { get; set; } = new();
    public List<ClassCount> ClassHistogram { get; set; } = new();
    public int ClassDistinct { get; set; }
}

/// <summary>Response from <c>query_group_candidates</c> — server-side window.</summary>
public class GroupScanWindowResult
{
    public ulong SessionId     { get; set; }
    public int   Total         { get; set; }
    public int   FilteredTotal { get; set; }
    public int   Offset        { get; set; }
    /// <summary>Repeated on every window so a panel that only ever queries — after a
    /// sort change, a filter, a Load More — still learns the set was capped. (AE13)</summary>
    public bool  PerSlotCapHit { get; set; }
    public int   PerSlotCap    { get; set; }
    public List<GroupCandidate> Candidates { get; set; } = new();
}
