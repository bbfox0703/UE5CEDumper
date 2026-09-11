namespace UE5DumpUI.Models;

/// <summary>
/// One UFunction that references a target FProperty in its Kismet bytecode.
/// Result of the static property cross-reference scan (find_property_xrefs):
/// "which methods use this field?".
///
/// Coverage is Blueprint/script functions only — native (FUNC_Native)
/// functions have empty bytecode and are invisible to this scan.
/// </summary>
public sealed class PropertyXrefMatch
{
    public string FunctionAddress { get; init; } = "";

    /// <summary>Short UFunction name (e.g. "ExecuteUbergraph_BP_Door").</summary>
    public string FunctionName { get; init; } = "";

    /// <summary>Full path (e.g. "/Game/.../BP_Door.BP_Door_C.ReceiveTick").</summary>
    public string FunctionFullName { get; init; } = "";

    /// <summary>Owning UClass short name.</summary>
    public string OwnerClassName { get; init; } = "";

    public string OwnerClassAddress { get; init; } = "";

    /// <summary>How many times the FProperty* appears in this function's bytecode.</summary>
    public int Occurrences { get; init; }

    /// <summary>
    /// Of <see cref="Occurrences"/>, how many are assignment destinations
    /// (EX_Let* LHS) — i.e. the function WRITES the field. Reads =
    /// Occurrences - WriteCount. Best-effort: wrapped LHS (Other.Field /
    /// Struct.Member / Arr[i] = x) isn't detected and counts as a read.
    /// </summary>
    public int WriteCount { get; init; }

    /// <summary>Compact access summary: "3W / 4R", "write", or "read".</summary>
    public string AccessSummary =>
        WriteCount <= 0 ? "read"
        : WriteCount >= Occurrences ? "write"
        : $"{WriteCount}W / {Occurrences - WriteCount}R";

    /// <summary>
    /// Reference kind, derived from the opcode byte preceding the matched
    /// pointer: "instance" (EX_InstanceVariable 0x01 — class member access,
    /// the common case), "local" (EX_LocalVariable 0x00), or "ref"
    /// (matched but preceding byte not a known variable opcode).
    /// </summary>
    public string Kind { get; init; } = "";

    /// <summary>
    /// v2a: for hits inside a Blueprint ubergraph (ExecuteUbergraph_*), the BP
    /// event(s) whose entry offset precedes the reference (comma-joined). Empty
    /// for non-ubergraph functions. Best-effort (nearest-preceding heuristic).
    /// </summary>
    public string EventName { get; init; } = "";
}

/// <summary>Diagnostic counters for a property cross-reference scan.</summary>
public sealed class PropertyXrefScanStats
{
    public int FunctionsScanned { get; init; }
    public int FunctionsWithScript { get; init; }
    public int ObjectsTotal { get; init; }
    public long DurationMs { get; init; }
    public bool DeadlineHit { get; init; }

    /// <summary>[W3-XREF-CAP] The scan stopped at its result cap: the results are a PREFIX and more may exist.
    /// Its own flag, never folded into <see cref="DeadlineHit"/> -- a different cause, different advice.</summary>
    public bool CapHit { get; init; }

    /// <summary>[W3-XREF-CAP] The effective cap the DLL applied (0 from a DLL that did not say).</summary>
    public int Cap { get; init; }
}

/// <summary>Result of a find_property_xrefs scan.</summary>
public sealed class FindPropertyXrefsResult
{
    public string QueryAddress { get; init; } = "";
    public List<PropertyXrefMatch> Xrefs { get; init; } = new();
    public PropertyXrefScanStats? Scan { get; init; }
}
