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

/// <summary>[P1-ENUMNAMES] A <c>list_enums</c> reply, with what the list alone cannot say.</summary>
public sealed class EnumListResult
{
    public List<EnumDefinition> Enums { get; init; } = new();

    /// <summary>The walk was cut short (a cancel): the list is partial.</summary>
    public bool Truncated { get; init; }

    /// <summary>UEnum::Names was never located on this build, so every enum's entries are empty. Only a search
    /// that ran to completion latches this; a cancelled one retries.</summary>
    public bool EnumNamesFailed { get; init; }
}
