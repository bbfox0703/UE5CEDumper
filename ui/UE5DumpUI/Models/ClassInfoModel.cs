namespace UE5DumpUI.Models;

/// <summary>
/// Represents the full structure info of a UClass.
/// </summary>
public sealed class ClassInfoModel
{
    public string Name { get; init; } = "";
    public string FullPath { get; init; } = "";
    public string SuperAddress { get; init; } = "";
    public string SuperName { get; init; } = "";
    public int PropertiesSize { get; init; }
    public List<FieldInfoModel> Fields { get; init; } = new();
}
