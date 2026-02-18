namespace UE5DumpUI.Models;

/// <summary>
/// Detailed information about a single UObject.
/// </summary>
public sealed class ObjectDetail
{
    public string Address { get; init; } = "";
    public string Name { get; init; } = "";
    public string FullName { get; init; } = "";
    public string ClassName { get; init; } = "";
    public string ClassAddr { get; init; } = "";
    public string OuterName { get; init; } = "";
    public string OuterAddr { get; init; } = "";
}
