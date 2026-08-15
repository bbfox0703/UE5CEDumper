using System.Collections.Generic;
using UE5DumpUI.Services;

namespace UE5DumpUI.Models;

/// <summary>
/// One auto-detected candidate player-stat field, produced by the experimental
/// "Detect Player Stats" panel (P4). Wraps a scored <see cref="PropertySearchMatch"/>
/// with the live confirmation signals gathered at detect time (a live instance
/// exists, the value is plausible, has a Max sibling, is a GAS attribute, and —
/// opt-in — decreased across two snapshots) plus a combined confidence.
///
/// LOW-ACCURACY heuristic by design — the whole panel carries a "reference only"
/// disclaimer. These rows are a shortlist to verify, never a guarantee.
/// </summary>
public sealed class DetectedStat
{
    public required PropertySearchMatch Match { get; init; }
    public required PropertyCategory Category { get; init; }
    public required int BaseScore { get; init; }     // scorer FinalScore (+ pair)
    public required int Confidence { get; init; }     // base + confirmation boosts
    public required bool IsConfirmed { get; init; }

    // Confirmation signals

    /// <summary>
    /// Whether this row's class was live-probed at all. Detect stops probing after
    /// <c>MaxClassesProbed</c> classes, and past that cap every signal below is false for
    /// the same reason a DISPROVEN row's is: nothing was measured. The two rendered
    /// identically, so a real stat sitting at rank 31 looked exactly like one the probe had
    /// examined and rejected. (audit #5 AF2)
    /// </summary>
    public bool WasProbed { get; init; } = true;

    public bool LiveInstanceExists { get; init; }
    public bool ValuePlausible { get; init; }
    public bool HasMaxSibling { get; init; }
    public bool IsGasAttribute { get; init; }
    public bool SnapshotDecreased { get; init; }

    /// <summary>Live typed value read from a representative instance ("" if none).</summary>
    public string LiveValue { get; init; } = "";

    /// <summary>When the snapshot signal fired, the rendered old→new change that
    /// backs it (e.g. "320.23 → 300.33"); "" otherwise. Coarse: matched by field
    /// name against the two most-recent snapshots' diff.</summary>
    public string SnapshotChange { get; init; } = "";

    // Forwarded for the DataGrid + cross-tab handoffs
    public string ClassName => Match.ClassName;
    public string DefiningClassName => Match.DefiningClassName;
    public string PropName => Match.PropName;
    public string PropType => Match.PropType;
    public int PropOffset => Match.PropOffset;
    public string OffsetHex => Match.OffsetHex;

    public string CategoryLabel => PropertyScoringTable.DisplayName(Category);
    public string CategoryColor => PropertyScoringTable.CategoryColor(Category);

    /// <summary>Compact badge for the first column: confirmed / guess / never-checked.
    /// The third is not a weaker guess — it is the absence of evidence, and calling it a
    /// guess is the AF2 defect.</summary>
    public string ConfirmBadge =>
        IsConfirmed ? "✓ confirmed" : WasProbed ? "· guess" : "? not checked";

    /// <summary>Foreground colour for the badge — green confirmed, grey guess, amber unchecked.</summary>
    public string ConfirmColor =>
        IsConfirmed ? "#6A9955" : WasProbed ? "#808080" : "#C08A3E";

    /// <summary>Human-readable roll-up of the signals that fired, for the grid + tooltip.</summary>
    public string SignalSummary
    {
        get
        {
            var parts = new List<string>();
            // Say so FIRST when nothing was measured: every other signal below is absent for
            // a different reason than "we looked and it wasn't there".
            if (!WasProbed) parts.Add("not live-probed (past the class cap)");
            if (LiveInstanceExists) parts.Add("live");
            if (ValuePlausible && LiveValue.Length > 0) parts.Add($"={LiveValue}");
            else if (LiveValue.Length > 0) parts.Add(LiveValue);
            if (HasMaxSibling) parts.Add("has Max");
            if (IsGasAttribute) parts.Add("GAS");
            if (SnapshotDecreased) parts.Add("▼ on event");
            return string.Join("  ·  ", parts);
        }
    }
}
