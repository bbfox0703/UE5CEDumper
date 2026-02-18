namespace UE5DumpUI.Models;

/// <summary>
/// Represents a single field/property within a UClass.
/// </summary>
public sealed class FieldInfoModel
{
    public string Address { get; init; } = "";
    public string Name { get; init; } = "";
    public string TypeName { get; init; } = "";
    public int Offset { get; init; }
    public int Size { get; init; }
}
