namespace UE5DumpUI.Models;

/// <summary>
/// Represents a single row in the hex viewer (16 bytes per row).
/// </summary>
public sealed class HexViewRow
{
    public string Offset { get; init; } = "";
    public string HexPart { get; init; } = "";
    public string AsciiPart { get; init; } = "";
}
