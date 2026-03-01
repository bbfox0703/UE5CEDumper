namespace UE5DumpUI.Models;

/// <summary>
/// Represents a UEnum object with its entries (name/value pairs).
/// Used by SDK generation, USMAP export, and symbol export.
/// </summary>
public sealed class EnumDefinition
{
    public string Address { get; init; } = "";
    public string Name { get; init; } = "";
    public string FullPath { get; init; } = "";
    public List<EnumEntryValue> Entries { get; init; } = new();
}
