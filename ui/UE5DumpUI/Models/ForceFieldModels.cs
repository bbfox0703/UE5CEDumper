namespace UE5DumpUI.Models;

/// <summary>
/// One active force-field hold-job (Solide), returned by <c>get_forced_fields</c>.
/// The DLL holds <see cref="FieldName"/> at <see cref="Value"/> on every live
/// instance of <see cref="ClassName"/> via a write-on-drift re-assert worker;
/// <see cref="Held"/> is the honesty signal (0 = the class/field resolved nothing).
/// </summary>
public sealed class ForcedFieldInfo
{
    public string ClassName { get; init; } = "";
    public string FieldName { get; init; } = "";
    /// <summary>"bool" | "object_null" | "numeric".</summary>
    public string Kind { get; init; } = "bool";
    /// <summary>Desired value (bool: 1/0; numeric: the absolute value).</summary>
    public double Value { get; init; }
    /// <summary>Live instance count the field resolved on last tick.</summary>
    public int Held { get; init; }
    /// <summary>One live owner address (Locate-in-GWorld handoff), "" if none.</summary>
    public string OwnerAddr { get; init; } = "";
    public int FieldOffset { get; init; } = -1;
    /// <summary>
    /// The DLL's instance pool hit its cap, so <see cref="Held"/> is a FLOOR, not a total —
    /// more live instances of this class exist and are NOT held. Without this, "resolved
    /// nothing" and "resolved more than the cap and discarded the rest" look identical.
    /// There is deliberately no true total: counting past the cap would cost a full
    /// GObjects walk on every re-assert tick.
    /// </summary>
    public bool Truncated { get; init; }
}

/// <summary>
/// Result of a <c>force_field</c> call: the live "N held" instance count.
/// </summary>
public sealed class ForceFieldResult
{
    /// <summary>Instances the field resolved + is being held on (0 = matched nothing).</summary>
    public int Held { get; init; }
    public bool Resolved { get; init; }
    /// <summary>Solide::ForceResult (0 = OK, negative = error).</summary>
    public int Code { get; init; }
    /// <summary>
    /// The instance pool hit the DLL's cap — <see cref="Held"/> is a floor, more live
    /// instances exist unheld. See <see cref="ForcedFieldInfo.Truncated"/>.
    /// </summary>
    public bool Truncated { get; init; }
}

/// <summary>
/// One ranked stealth/noise/visibility-meter candidate (Solide), returned by
/// <c>find_stealth_meter</c>. Holding it at 0 is done via <c>force_field</c> with
/// this <see cref="ClassName"/> + <see cref="FieldName"/>.
/// </summary>
/// <summary>[SOLIDE-RELATED-STOPS] find_stealth_meter's candidates and why its search of the player's objects
/// stopped short (null from an older DLL, or when it did not stop).</summary>
public sealed class StealthMeterResult
{
    public IReadOnlyList<StealthCandidate> Candidates { get; init; } = new List<StealthCandidate>();
    public RelatedObjectsStops? Stops { get; init; }
}

public sealed class StealthCandidate
{
    public string ClassName { get; init; } = "";
    public string ClassAddr { get; init; } = "";
    public string FieldName { get; init; } = "";
    /// <summary>Reflected numeric type (FloatProperty / DoubleProperty / ...).</summary>
    public string PropType { get; init; } = "";
    public string OwnerAddr { get; init; } = "";
    /// <summary>Live value (for the UI readout).</summary>
    public double Current { get; init; }
    /// <summary>MatchStealthField keyword score (higher = stronger).</summary>
    public int Score { get; init; }
}
