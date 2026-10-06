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
    /// <summary>[EXTPR-539-540-2026-10-02] The GObjects slot, when the page was asked for it
    /// (<see cref="Core.IDumpService.GetObjectIndexPageAsync"/>); null otherwise, and from a DLL that predates
    /// the key.</summary>
    public int? Index { get; init; }
    public bool IsExpanded { get; set; }

    // Lazy-initialized to save ~64 bytes per node when Children is not used.
    // Object Tree displays a flat ListBox and never accesses Children.
    private ObservableCollection<UObjectNode>? _children;
    public ObservableCollection<UObjectNode> Children => _children ??= new();
}
