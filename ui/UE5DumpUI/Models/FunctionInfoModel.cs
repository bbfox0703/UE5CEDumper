namespace UE5DumpUI.Models;

/// <summary>
/// Represents a UFunction with its parameters.
/// Used by SDK generation for full C++ headers with function signatures.
/// </summary>
public sealed class FunctionInfoModel
{
    public string Name { get; init; } = "";
    public string FullName { get; init; } = "";
    public string Address { get; init; } = "";
    public uint FunctionFlags { get; init; }
    public List<FunctionParamModel> Params { get; init; } = new();
    public string ReturnType { get; init; } = "";
}

/// <summary>
/// Represents a single parameter of a UFunction.
/// </summary>
public sealed class FunctionParamModel
{
    public string Name { get; init; } = "";
    public string TypeName { get; init; } = "";
    public int Size { get; init; }
    public bool IsOut { get; init; }
    public bool IsReturn { get; init; }
}
