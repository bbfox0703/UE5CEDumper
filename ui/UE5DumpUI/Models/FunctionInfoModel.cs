namespace UE5DumpUI.Models;

/// <summary>
/// Represents a UFunction with its parameters.
/// Used by SDK generation and UFunction invoke script generation.
/// </summary>
public sealed class FunctionInfoModel
{
    public string Name { get; init; } = "";
    public string FullName { get; init; } = "";
    public string Address { get; init; } = "";
    public uint FunctionFlags { get; init; }
    public byte NumParms { get; init; }
    public ushort ParmsSize { get; init; }
    public ushort ReturnValueOffset { get; init; } = 0xFFFF;
    public List<FunctionParamModel> Params { get; init; } = new();
    public string ReturnType { get; init; } = "";

    /// <summary>Input-only parameters (excludes return param).</summary>
    public IEnumerable<FunctionParamModel> InputParams
        => Params.Where(p => !p.IsReturn);

    /// <summary>Decode FunctionFlags to human-readable tags.</summary>
    public static string DecodeFunctionFlags(uint flags)
    {
        var list = new List<string>(4);
        if ((flags & 0x0000_0400) != 0) list.Add("Native");
        if ((flags & 0x0400_0000) != 0) list.Add("BlueprintCallable");
        if ((flags & 0x0800_0000) != 0) list.Add("BlueprintEvent");
        if ((flags & 0x0200_0000) != 0) list.Add("BlueprintPure");
        if ((flags & 0x0000_0800) != 0) list.Add("Event");
        if ((flags & 0x0000_2000) != 0) list.Add("Static");
        if ((flags & 0x0000_4000) != 0) list.Add("Const");
        if ((flags & 0x0002_0000) != 0) list.Add("Exec");
        if ((flags & 0x0008_0000) != 0) list.Add("Net");
        return string.Join(", ", list);
    }
}

/// <summary>
/// Represents a single parameter of a UFunction.
/// </summary>
public sealed class FunctionParamModel
{
    public string Name { get; init; } = "";
    public string TypeName { get; init; } = "";
    public int Size { get; init; }
    public int Offset { get; init; } = -1;
    public bool IsOut { get; init; }
    public bool IsReturn { get; init; }
}
