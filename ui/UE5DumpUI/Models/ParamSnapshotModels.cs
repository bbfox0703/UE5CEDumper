namespace UE5DumpUI.Models;

// [LIVEFUNCS-STEP2] Following functions by name, and parameter snapshots: what pe_profile_start sends by name and what
// pe_snap_layouts / pe_snap_get read back after Stop. The protocol is docs/pipe-protocol.md, "Following functions by
// name, and parameter snapshots"; the design, docs/live-funcs-timeline-plan.md ("Step 2 design").

/// <summary>A function's name as the DLL's name pool numbers it: the function's FName ints and its class's. Stable for
/// the life of the game process, unlike an address (a widget's functions unload when it closes and come back
/// elsewhere), so the UI keeps it for the connection and sends it back to tick or choose by name (T10).</summary>
public readonly record struct NameKey(int FnIdx, int FnNum, int ClsIdx, int ClsNum);

/// <summary>A function asked for by name: the strings the user saw, and every key that rendered to them in this
/// process (one string can come from two int pairs). The DLL refuses a key that no longer renders to both.</summary>
public sealed class NamedFunction
{
    public string ClassName { get; init; } = "";
    public string FuncName  { get; init; } = "";
    public IReadOnlyList<NameKey> Keys { get; init; } = Array.Empty<NameKey>();
    /// <summary>The largest parameter block seen under the name; sizes its snapshot ring. 0: never read.</summary>
    public int ParmsSize { get; init; }
}

/// <summary>What a Start asks of the parameter snapshots.</summary>
public sealed class SnapshotStartOptions
{
    public IReadOnlyList<NamedFunction> Funcs { get; init; } = Array.Empty<NamedFunction>();
    /// <summary>The snapshot buffer: a power of two from 8 to 128 MB.</summary>
    public long Bytes { get; init; }
    public int PerRingPerSec { get; init; } = 1000;
    public int TotalPerSec { get; init; } = 10000;
}

/// <summary>The trace's snapshot buffer as the DLL armed it (<c>trace.snap</c>).</summary>
public sealed class SnapInfo
{
    public bool  Allocated     { get; init; }
    public long  Bytes         { get; init; }
    /// <summary>K: the calls each ring keeps.</summary>
    public ulong SlotsPerRing  { get; init; }
    public int   Rings         { get; init; }
    public int   PerRingPerSec { get; init; }
    public int   TotalPerSec   { get; init; }
    /// <summary>In-scope calls recorded without their parameters: over the budget.</summary>
    public ulong SkippedBudget { get; init; }
    /// <summary>Lone and excluded-but-chosen calls over the budget: not recorded at all.</summary>
    public ulong DroppedBudget { get; init; }
}

/// <summary>What the Start made of the names it was sent (<c>trace.names</c>).</summary>
public sealed class StartNames
{
    public int Ticks  { get; init; }
    public int Chosen { get; init; }
    public IReadOnlyList<(string ClassName, string FuncName, string Why)> Refused { get; init; }
        = Array.Empty<(string, string, string)>();
}

/// <summary>What became of one followed name in the recording (the Stop reply's <c>names</c>).</summary>
public sealed class FollowedName
{
    public string  ClassName { get; init; } = "";
    public string  FuncName  { get; init; } = "";
    public NameKey Key       { get; init; }
    public bool    Tick      { get; init; }
    public bool    Chosen    { get; init; }
    /// <summary>Distinct addresses that matched the name; 0: never called in the recording.</summary>
    public long    Addresses { get; init; }
    public long    Arms      { get; init; }
    public long    ArmsFull  { get; init; }
    public bool    NotCalled => Addresses == 0;
}

/// <summary>One snapshot ring's window.</summary>
public sealed class SnapRingInfo
{
    public int   Ring          { get; init; }
    public int   Cap           { get; init; }
    public ulong Written       { get; init; }
    public ulong FirstValid    { get; init; }
    public ulong SkippedBudget { get; init; }
    public ulong DroppedBudget { get; init; }
}

/// <summary>One parameter of a chosen function's layout.</summary>
public sealed class SnapParam
{
    public string Name     { get; init; } = "";
    public string Type     { get; init; } = "";
    public int    Offset   { get; init; }
    public int    Size     { get; init; }
    public int    ArrayDim { get; init; } = 1;
    public ulong  Flags    { get; init; }
    /// <summary>"in", "const_ref", "out", "in_out" or "return".</summary>
    public string Kind     { get; init; } = "in";
    public string Struct   { get; init; } = "";
    public string ObjClass { get; init; } = "";
    public string Enum     { get; init; } = "";
    public IReadOnlyList<SnapParam> Sub { get; init; } = Array.Empty<SnapParam>();
}

/// <summary>A chosen function's parameters, as read once per load (an arm) while it was alive.</summary>
public sealed class SnapLayout
{
    public string ClassName     { get; init; } = "";
    public string FuncName      { get; init; } = "";
    public uint   FunctionFlags { get; init; }
    public int    ParmsSize     { get; init; }
    public int    NumParms      { get; init; }
    public int    LayoutEnd     { get; init; }
    public IReadOnlyList<SnapParam> Params { get; init; } = Array.Empty<SnapParam>();
    /// <summary>The same list, under the name view models read it by: a view model reading `.Params` is what
    /// FunctionParametersTests guards against (a function's raw property chain, locals and all).</summary>
    public IReadOnlyList<SnapParam> Fields => Params;
}

/// <summary>One arm: one load of a chosen function, the slots it wrote, and what became of its layout.</summary>
public sealed class SnapArm
{
    public int    Index         { get; init; }
    public int    Ring          { get; init; }
    public ulong  Addr          { get; init; }
    public string ClassName     { get; init; } = "";
    public string FuncName      { get; init; } = "";
    public uint   FunctionFlags { get; init; }
    public int    ParmsSize     { get; init; }
    public int    NumParms      { get; init; }
    /// <summary>"read", "doubtful", "failed", "unloaded_before_read", "replaced_before_read",
    /// "not_read_before_stop" or "pending".</summary>
    public string State         { get; init; } = "";
    public string Why           { get; init; } = "";
    /// <summary>The arm-to-read wait (ms); null when its layout was never read.</summary>
    public long?  ReadMs        { get; init; }
    /// <summary>Its layout, or null when it has none: its slots stay raw.</summary>
    public SnapLayout? Layout   { get; init; }
}

/// <summary>A page of pe_snap_layouts.</summary>
public sealed class SnapLayoutsPage
{
    public TraceInfo Info { get; init; } = new();
    public bool Stale { get; init; }
    public int Total { get; init; }
    public int Next { get; init; }
    public IReadOnlyList<SnapRingInfo> Rings { get; init; } = Array.Empty<SnapRingInfo>();
    public IReadOnlyList<SnapArm> Arms { get; init; } = Array.Empty<SnapArm>();
}

/// <summary>How far to trust one decoded value (Ubel::SnapMark).</summary>
public enum SnapMark
{
    /// <summary>From the copy alone.</summary>
    Exact = 0,
    /// <summary>Names what is at that address NOW, which may not be what was there at the call.</summary>
    Now = 1,
    /// <summary>An address that holds no live object now.</summary>
    Gone = 2,
    /// <summary>Not in this copy: past its end, or a value this copy does not carry.</summary>
    Missing = 3,
    /// <summary>A string's or a container's header only; its data was not copied.</summary>
    Header = 4,
    /// <summary>A type the decoder does not read: hex.</summary>
    Raw = 5,
}

/// <summary>One decoded parameter value; a struct's members in <see cref="Sub"/>.</summary>
public sealed class SnapValue
{
    public string Text { get; init; } = "";
    public SnapMark Mark { get; init; }
    public IReadOnlyList<SnapValue> Sub { get; init; } = Array.Empty<SnapValue>();
}

/// <summary>One snapshot slot: an entry copy or the copy after the call.</summary>
public sealed class SnapSlot
{
    public const int NullParamsFlag = 1;
    public const int CopyFaultFlag  = 2;
    public const int TruncatedFlag  = 4;

    public ulong  Index    { get; init; }
    public ulong  EntrySeq { get; init; }
    public bool   IsAfter  { get; init; }
    public int    Len      { get; init; }
    public int    Flags    { get; init; }
    public int    Arm      { get; init; }
    public byte[] Data     { get; init; } = Array.Empty<byte>();
    /// <summary>Lined up with the arm's layout parameters; null when the arm has no layout.</summary>
    public IReadOnlyList<SnapValue>? Values { get; init; }
    /// <summary>Why there are no values: the arm's layout state.</summary>
    public string RawOnly  { get; init; } = "";
}

/// <summary>A page of pe_snap_get.</summary>
public sealed class SnapPage
{
    public TraceInfo Info { get; init; } = new();
    public bool Stale { get; init; }
    public int Ring { get; init; }
    public int Count { get; init; }
    public ulong Next { get; init; }
    public ulong Orphans { get; init; }
    public IReadOnlyList<SnapSlot> Items { get; init; } = Array.Empty<SnapSlot>();
}
