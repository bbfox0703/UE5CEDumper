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

    /// <summary>
    /// The immediate super's PropertiesSize — the offset at which this class's OWN properties begin.
    /// 0 when the DLL did not supply it (older DLL, or the class has no super).
    /// <para>
    /// <see cref="Fields"/> carries the <b>entire</b> SuperStruct chain: the DLL prepends every
    /// inherited property. A consumer that must tell own from inherited — the SDK header emitter,
    /// which would otherwise re-declare every base property inside a struct that already inherits it
    /// — cannot derive this boundary from anything else on the wire (audit #5 W2).
    /// </para>
    /// </summary>
    public int SuperPropertiesSize { get; init; }

    /// <summary>Lowest Offset among this class's OWN properties, or <c>-1</c> when it declares none
    /// (or the DLL predates the field). ⚠ NOT interchangeable with <see cref="SuperPropertiesSize"/>:
    /// UE reports an EMPTY USTRUCT's PropertiesSize as 1 (CppStructOps-&gt;GetSize()), while C++
    /// empty-base optimisation puts the derived struct's first member at offset 0 — so the super's
    /// size is one too high to use as a floor. A negative value means NO INFORMATION and must fall
    /// back to SuperPropertiesSize; folding it into a min() would re-emit the whole inherited
    /// chain (audit #5 W2).</summary>
    public int OwnPropertiesStart { get; init; } = -1;
    public List<FieldInfoModel> Fields { get; init; } = new();
}
