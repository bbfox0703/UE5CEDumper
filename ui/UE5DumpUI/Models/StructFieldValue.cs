namespace UE5DumpUI.Models;

/// <summary>
/// One decoded sub-field of a StructProperty return value from an
/// invoke. Lives separately from <see cref="FunctionParamModel.StructFields"/>
/// (which only carries layout metadata) — this record adds the
/// decoded <see cref="Value"/> string so the InvokeParamDialog result
/// panel can render a property-grid of FVector / FRotator / FHitResult
/// / user-USTRUCT fields without re-running the byte→typed-value
/// decode pipeline on every render.
///
/// Used as the row type for the dialog's structured-return DataGrid.
/// Pure data record — IO-free, AOT-friendly.
/// </summary>
public sealed record StructFieldValue(
    string Name,
    string Type,
    string Value,
    int    Offset);
