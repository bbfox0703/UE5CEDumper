namespace UE5DumpUI.Models;

/// <summary>
/// Represents a single field/property within a UClass.
/// Includes extended type metadata populated by WalkClassEx.
/// </summary>
public sealed class FieldInfoModel
{
    public string Address { get; init; } = "";
    public string Name { get; init; } = "";
    public string TypeName { get; init; } = "";
    public int Offset { get; init; }
    public int Size { get; init; }

    // Extended type metadata (from walk_class JSON)
    public string StructType { get; init; } = "";        // StructProperty -> UScriptStruct name
    public string ObjClassName { get; init; } = "";      // ObjectProperty/ClassProperty -> target class name
    public string InnerType { get; init; } = "";         // ArrayProperty -> inner element type
    public string InnerStructType { get; init; } = "";   // ArrayProperty of struct -> struct name
    public string InnerObjClass { get; init; } = "";     // ArrayProperty of object -> class name
    public string KeyType { get; init; } = "";           // MapProperty -> key type
    public string KeyStructType { get; init; } = "";     // MapProperty key struct name
    public string ValueType { get; init; } = "";         // MapProperty -> value type
    public string ValueStructType { get; init; } = "";   // MapProperty value struct name
    public string ElemType { get; init; } = "";          // SetProperty -> element type
    public string ElemStructType { get; init; } = "";    // SetProperty element struct name
    public string EnumName { get; init; } = "";          // EnumProperty/ByteProperty -> UEnum name
    // [A4-USMAP-CONTAINER-ENUM] a container inner's UEnum (a TEnumAsByte or an EnumProperty inner); "" when none
    public string InnerEnumName { get; init; } = "";     // ArrayProperty / OptionalProperty inner
    public string ElemEnumName { get; init; } = "";      // SetProperty element
    public string KeyEnumName { get; init; } = "";       // MapProperty key
    public string ValueEnumName { get; init; } = "";     // MapProperty value
    public int BoolFieldMask { get; init; }              // BoolProperty -> FieldMask byte

    // Reflection / layout metadata (feed the auto-detect scorer)
    public ulong PropertyFlags { get; init; }            // CPF_* flags (SaveGame/BlueprintVisible/Net/Transient/EditConst/...); 0 when absent
    public int ArrayDim { get; init; } = 1;              // Static C-array dim (Type Foo[N]); 1 = scalar. Full footprint = Size * ArrayDim.
}
