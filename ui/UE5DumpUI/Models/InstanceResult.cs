using CommunityToolkit.Mvvm.ComponentModel;

namespace UE5DumpUI.Models;

/// <summary>
/// A single instance found by FindInstances.
/// </summary>
public sealed partial class InstanceResult : ObservableObject
{
    public string Address { get; init; } = "";
    public int Index { get; init; }
    public string Name { get; init; } = "";
    public string ClassName { get; init; } = "";
    /// <summary>UClass* address — the key for find_functions_by_class.</summary>
    public string ClassAddress { get; init; } = "";
    public string OuterAddr { get; init; } = "";

    /// <summary>Batch "Find Func" result: which UFunctions take this instance's
    /// class as a param/return. Format "N · func1, func2[, …]" / "0" / "—" / "".</summary>
    [ObservableProperty] private string _xrefInfo = "";

    /// <summary>
    /// Address as ulong for AOT-safe hex sorting (0 on parse failure) — the same shape
    /// RelatedObject.AddressValue already uses, so the two "Address" columns finally sort
    /// the same way. [PARAMSSORT-2026-08-22]
    ///
    /// <para>Equal-length UPPERCASE hex compares identically ordinally and numerically, and
    /// this host emits 13 characters for all 137 instances of Object, so the string comparer
    /// did not MISBEHAVE here. That is a property of this heap's layout, not of the comparer:
    /// any result set mixing a 12- and a 13-character address — a game with a static
    /// FUObjectArray, a 0x7FF… module-resident object — orders them by first character.</para>
    /// </summary>
    public ulong AddressValue =>
        ulong.TryParse(Address.Replace("0x", "", System.StringComparison.OrdinalIgnoreCase),
            System.Globalization.NumberStyles.HexNumber, null, out var v) ? v : 0UL;
}
