using CommunityToolkit.Mvvm.ComponentModel;
using UE5DumpUI.Services;

namespace UE5DumpUI.Models;

/// <summary>
/// Wraps a <see cref="PropertySearchMatch"/> with
/// <see cref="PropertyScoringTable.Score"/> output for display in the
/// Interesting Properties Finder DataGrid. Mirrors
/// <see cref="ScoredFunctionRow"/> on the Function side.
///
/// The Unusual Location ⚠ badge is the B'-specific signal: a property
/// at a higher-than-normal class bonus AND sitting in a non-canonical
/// container (LocalPlayer / HUD / GameViewportClient / etc).
/// </summary>
public sealed partial class ScoredPropertyRow : ObservableObject
{
    public required PropertySearchMatch Match            { get; init; }
    public required int                 FinalScore       { get; init; }
    public required PropertyCategory    Category         { get; init; }
    public required int                 KeywordHits      { get; init; }
    public required int                 ClassBonus       { get; init; }
    public required bool                IsUnusualLocation{ get; init; }

    // Structural signals (P2a). StructuralBonus = GAS boost + type prior;
    // PairBonus = Current/Max stat-family bonus (both already folded into
    // FinalScore, surfaced here for the score breakdown tooltip).
    public int                          StructuralBonus  { get; init; }
    public bool                         IsGasAttribute   { get; init; }
    public int                          PairBonus        { get; init; }

    // ------------------------------------------------------------------
    // Display helpers (DataGrid bindings)
    // ------------------------------------------------------------------

    public string CategoryLabel => PropertyScoringTable.DisplayName(Category);
    public string CategoryColor => PropertyScoringTable.CategoryColor(Category);

    /// <summary>Compact unusual-location badge. Empty string when the
    /// owning class is one of the conventional containers so the
    /// column stays clean for the bulk of hits.</summary>
    public string UnusualBadge => IsUnusualLocation ? "⚠ Unusual" : "";

    /// <summary>Tooltip for the score column.</summary>
    public string ScoreTooltip
        => $"FinalScore={FinalScore} = keywords({KeywordHits} hits) " +
           $"+ classBonus={ClassBonus}" +
           (StructuralBonus != 0 ? $" + structural={StructuralBonus}" : "") +
           (PairBonus != 0 ? $" + pair={PairBonus}" : "") +
           (IsGasAttribute
               ? "  (GAS — FGameplayAttributeData, a near-certain gameplay stat)"
               : "") +
           (PairBonus != 0
               ? "  (part of a Current/Max stat family in this class)"
               : "") +
           (IsUnusualLocation
               ? "  (⚠ class is in the Unusual Location list — properties " +
                 "here are often overlooked because they sit outside the " +
                 "conventional Character/Pawn/PlayerState containers)"
               : "");

    // Forwarded match properties (so AXAML bindings don't have to walk
    // through .Match.X — keeps the AXAML readable).
    public string ClassName         => Match.ClassName;
    public string ClassAddr         => Match.ClassAddr;
    public string ClassPath         => Match.ClassPath;
    public string PropName          => Match.PropName;
    public string PropType          => Match.PropType;
    public string TypeDisplay       => Match.TypeDisplay;
    public string OffsetHex         => Match.OffsetHex;
    public int    PropOffset        => Match.PropOffset;
    /// <summary>Engine-reported byte width — carried through to
    /// <c>FreezeScriptParams.PropertySize</c> so the batch-CT path picks an
    /// EnumProperty's writer by its real width (audit #5 Y15).</summary>
    public int    PropSize          => Match.PropSize;
    public string DefiningClassName => Match.DefiningClassName;
    public int    InheritedByCount  => Match.InheritedByCount;
    public string InheritanceBadge  => Match.InheritanceBadge;

    /// <summary>Batch "Find Funcs" result: which UFunctions reference this
    /// property. Format "N · func1, func2[, …]" / "0" / "—" / "" (not run).</summary>
    [ObservableProperty] private string _xrefInfo = "";
}
