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
    public List<FieldInfoModel> Fields { get; init; } = new();
}
