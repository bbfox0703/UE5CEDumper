using System.Collections.ObjectModel;

namespace UE5DumpUI.Models;

/// <summary>
/// Represents a UObject in the object tree.
/// </summary>
public sealed class UObjectNode
{
    public string Address { get; init; } = "";
    public string Name { get; init; } = "";
    public string ClassName { get; init; } = "";
    public string OuterAddr { get; init; } = "";
    public string FullPath { get; init; } = "";
    public bool IsExpanded { get; set; }
    public ObservableCollection<UObjectNode> Children { get; } = new();
}
