using UE5DumpUI.Core;
using UE5DumpUI.Models;
using UE5DumpUI.Services;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// Stub IDumpService that returns empty results — sufficient for
/// testing CSX generation where StructProperty resolution is not needed.
/// </summary>
public sealed class StubDumpService : IDumpService
{
    private readonly Dictionary<string, InstanceWalkResult> _structResults = new();
    private readonly Dictionary<string, ClassInfoModel> _classResults = new();

    /// <summary>Register a struct walk result for testing struct flattening and drilldown.</summary>
    public void RegisterStruct(string addr, InstanceWalkResult result)
        => _structResults[addr] = result;

    /// <summary>Register a class walk result for testing class-based lookups.</summary>
    public void RegisterClass(string addr, ClassInfoModel result)
        => _classResults[addr] = result;

    public Task<InstanceWalkResult> WalkInstanceAsync(string addr, string? classAddr = null,
        int arrayLimit = 64, int previewLimit = 2, CancellationToken ct = default)
    {
        if (_structResults.TryGetValue(addr, out var result))
            return Task.FromResult(result);
        return Task.FromResult(new InstanceWalkResult { Fields = new List<LiveFieldValue>() });
    }

    public Task<ClassInfoModel> WalkClassAsync(string addr, CancellationToken ct = default)
    {
        if (_classResults.TryGetValue(addr, out var result))
            return Task.FromResult(result);
        return Task.FromResult(new ClassInfoModel { Fields = new List<FieldInfoModel>() });
    }

    // Unused stubs — throw NotImplementedException to catch unexpected calls
    public Task<EngineState> InitAsync(CancellationToken ct = default) => throw new NotImplementedException();
    public Task<EngineState> GetPointersAsync(CancellationToken ct = default) => throw new NotImplementedException();
    public Task<int> GetObjectCountAsync(CancellationToken ct = default) => throw new NotImplementedException();
    public Task<ObjectListResult> GetObjectListAsync(int offset, int limit, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<ObjectDetail> GetObjectAsync(string addr, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<ObjectDetail> FindObjectAsync(string path, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<ObjectListResult> SearchObjectsAsync(string query, int limit = 200, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<byte[]> ReadMemAsync(string addr, int size, CancellationToken ct = default) => throw new NotImplementedException();
    public Task WriteMemAsync(string addr, byte[] data, CancellationToken ct = default) => throw new NotImplementedException();
    public Task WatchAsync(string addr, int size, int intervalMs, CancellationToken ct = default) => throw new NotImplementedException();
    public Task UnwatchAsync(string addr, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<WorldWalkResult> WalkWorldAsync(int actorLimit = 200, int arrayLimit = 64, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<FindInstancesResult> FindInstancesAsync(string className, bool exactMatch = false, int limit = 500, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<CePointerInfo> GetCePointerInfoAsync(string addr, int fieldOffset = 0, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<ArrayElementsResult> ReadArrayElementsAsync(string addr, int fieldOffset, string innerAddr, string innerType, int elemSize, int offset = 0, int limit = 64, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<AddressLookupResult> FindByAddressAsync(string addr, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<List<EnumDefinition>> ListEnumsAsync(CancellationToken ct = default) => throw new NotImplementedException();
    public Task<List<FunctionInfoModel>> WalkFunctionsAsync(string addr, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<PropertySearchResult> SearchPropertiesAsync(string query, string[]? types = null, bool gameOnly = true, int limit = 200, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<ClassListResult> ListClassesAsync(bool gameOnly = true, int limit = 5000, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<RescanStartResult> StartRescanAsync(CancellationToken ct = default) => throw new NotImplementedException();
    public Task<RescanStatusResult> GetRescanStatusAsync(CancellationToken ct = default) => throw new NotImplementedException();
    public Task<EngineState> ApplyRescanAsync(CancellationToken ct = default) => throw new NotImplementedException();
    public Task<EngineState> TriggerScanAsync(CancellationToken ct = default) => throw new NotImplementedException();
    public Task<InvokeFunctionResult> InvokeFunctionAsync(string funcName, string? instanceAddr = null, string? className = null, int parmsSize = 0, string? paramsHex = null, CancellationToken ct = default) => throw new NotImplementedException();
}

public class CsxExportServiceTests
{
    private readonly StubDumpService _dump = new();

    [Fact]
    public async Task GenerateCsx_IntProperty_EmitsCorrectElement()
    {
        var fields = new List<LiveFieldValue>
        {
            new() { Name = "Health", TypeName = "IntProperty", Offset = 0x120, Size = 4, HexValue = "64000000" }
        };

        var csx = await CsxExportService.GenerateCsxAsync(_dump, "TestStruct", fields);

        Assert.Contains("Vartype=\"4 Bytes\"", csx);
        Assert.Contains("Bytesize=\"4\"", csx);
        Assert.Contains("Offset=\"288\"", csx); // 0x120 = 288
        Assert.Contains("OffsetHex=\"00000120\"", csx);
        Assert.Contains("Description=\"Health\"", csx);
        Assert.Contains("DisplayMethod=\"unsigned integer\"", csx);
    }

    [Fact]
    public async Task GenerateCsx_FloatProperty_EmitsFloat()
    {
        var fields = new List<LiveFieldValue>
        {
            new() { Name = "Speed", TypeName = "FloatProperty", Offset = 0x50, Size = 4 }
        };

        var csx = await CsxExportService.GenerateCsxAsync(_dump, "TestStruct", fields);

        Assert.Contains("Vartype=\"Float\"", csx);
        Assert.Contains("Bytesize=\"4\"", csx);
    }

    [Fact]
    public async Task GenerateCsx_BoolProperty_NoBitmask_EmitsByte()
    {
        var fields = new List<LiveFieldValue>
        {
            new() { Name = "bIsAlive", TypeName = "BoolProperty", Offset = 0x10, Size = 1,
                     BoolBitIndex = -1, BoolFieldMask = 0 }
        };

        var csx = await CsxExportService.GenerateCsxAsync(_dump, "TestStruct", fields);

        Assert.Contains("Vartype=\"Byte\"", csx);
        Assert.Contains("Bytesize=\"1\"", csx);
        Assert.Contains("Description=\"bIsAlive\"", csx);
        // No bitmask info appended when BoolBitIndex is -1
        Assert.DoesNotContain("bit ", csx);
    }

    [Fact]
    public async Task GenerateCsx_BoolProperty_WithBitmask_AppendsBitInfo()
    {
        var fields = new List<LiveFieldValue>
        {
            new() { Name = "bIsVisible", TypeName = "BoolProperty", Offset = 0x240, Size = 1,
                     BoolBitIndex = 5, BoolFieldMask = 0x20 },
            new() { Name = "bIsLightingScenario", TypeName = "BoolProperty", Offset = 0x240, Size = 1,
                     BoolBitIndex = 0, BoolFieldMask = 0x01 },
        };

        var csx = await CsxExportService.GenerateCsxAsync(_dump, "TestStruct", fields);

        Assert.Contains("Description=\"bIsVisible (bit 5, mask 0x20)\"", csx);
        Assert.Contains("Description=\"bIsLightingScenario (bit 0, mask 0x01)\"", csx);
        // Both at same offset
        Assert.Contains("OffsetHex=\"00000240\"", csx);
    }

    [Fact]
    public async Task GenerateCsx_StrProperty_EmitsPointerWithUnicodeChild()
    {
        var fields = new List<LiveFieldValue>
        {
            new() { Name = "PlayerName", TypeName = "StrProperty", Offset = 0x30, Size = 8,
                     HexValue = "0000018AF21C3E20" }
        };

        var csx = await CsxExportService.GenerateCsxAsync(_dump, "TestStruct", fields);

        Assert.Contains("Vartype=\"Pointer\"", csx);
        Assert.Contains("Bytesize=\"8\"", csx);
        Assert.Contains("Description=\"PlayerName\"", csx);
        // Child structure with Unicode String
        Assert.Contains("Vartype=\"Unicode String\"", csx);
        Assert.Contains("Bytesize=\"18\"", csx);
    }

    [Fact]
    public async Task GenerateCsx_ObjectProperty_EmitsPointerNoChild()
    {
        var fields = new List<LiveFieldValue>
        {
            new() { Name = "Target", TypeName = "ObjectProperty", Offset = 0x80, Size = 8,
                     PtrAddress = "0x18AAD37FB00" }
        };

        var csx = await CsxExportService.GenerateCsxAsync(_dump, "TestStruct", fields);

        Assert.Contains("Vartype=\"Pointer\"", csx);
        Assert.Contains("Description=\"Target\"", csx);
        // No dummy child — CE handles native pointer dereference
        Assert.DoesNotContain("Description=\"dummy\"", csx);
        Assert.Contains("/>", csx); // Self-closing element
    }

    [Fact]
    public async Task GenerateCsx_StructProperty_FlattensInlineFields()
    {
        // Register struct inner fields
        _dump.RegisterStruct("0x1000", new InstanceWalkResult
        {
            Fields = new List<LiveFieldValue>
            {
                new() { Name = "X", TypeName = "FloatProperty", Offset = 0, Size = 4 },
                new() { Name = "Y", TypeName = "FloatProperty", Offset = 4, Size = 4 },
                new() { Name = "Z", TypeName = "FloatProperty", Offset = 8, Size = 4 },
            }
        });

        var fields = new List<LiveFieldValue>
        {
            new() { Name = "Location", TypeName = "StructProperty", Offset = 0x100, Size = 12,
                     StructTypeName = "FVector", StructDataAddr = "0x1000", StructClassAddr = "0x2000" }
        };

        var csx = await CsxExportService.GenerateCsxAsync(_dump, "TestStruct", fields);

        // Fields should be flattened with "FVector / X" naming
        Assert.Contains("Description=\"FVector / X\"", csx);
        Assert.Contains("Description=\"FVector / Y\"", csx);
        Assert.Contains("Description=\"FVector / Z\"", csx);
        // Offsets should be parent offset + inner offset
        Assert.Contains("OffsetHex=\"00000100\"", csx); // X: 0x100 + 0
        Assert.Contains("OffsetHex=\"00000104\"", csx); // Y: 0x100 + 4
        Assert.Contains("OffsetHex=\"00000108\"", csx); // Z: 0x100 + 8
    }

    [Fact]
    public async Task GenerateCsx_StructName_AppearsInRootElement()
    {
        var fields = new List<LiveFieldValue>
        {
            new() { Name = "HP", TypeName = "IntProperty", Offset = 0, Size = 4 }
        };

        var csx = await CsxExportService.GenerateCsxAsync(_dump, "PlayerData_123", fields);

        Assert.Contains("Name=\"PlayerData_123\"", csx);
        Assert.Contains("<Structures>", csx);
        Assert.Contains("</Structures>", csx);
    }

    [Fact]
    public async Task GenerateCsx_MapProperty_EmitsPointerNoChild()
    {
        var fields = new List<LiveFieldValue>
        {
            new() { Name = "Attributes", TypeName = "MapProperty", Offset = 0x30, Size = 8,
                     MapCount = 10, MapKeyType = "NameProperty", MapValueType = "ObjectProperty",
                     MapDataAddr = "0x18A8FD1E170" }
        };

        var csx = await CsxExportService.GenerateCsxAsync(_dump, "TestStruct", fields);

        Assert.Contains("Vartype=\"Pointer\"", csx);
        Assert.Contains("Description=\"Attributes\"", csx);
        // No dummy child — CE handles native pointer dereference
        Assert.DoesNotContain("Description=\"dummy\"", csx);
    }

    [Fact]
    public async Task GenerateCsx_UnknownProperty_FallsBackToArrayOfByte()
    {
        var fields = new List<LiveFieldValue>
        {
            new() { Name = "CustomField", TypeName = "SomeUnknownProperty", Offset = 0x40, Size = 16 }
        };

        var csx = await CsxExportService.GenerateCsxAsync(_dump, "TestStruct", fields);

        Assert.Contains("Vartype=\"Array of byte\"", csx);
        Assert.Contains("Bytesize=\"16\"", csx);
        Assert.Contains("DisplayMethod=\"hexadecimal\"", csx);
    }

    // --- Drilldown tests ---

    [Fact]
    public async Task GenerateCsx_DrilldownZero_NoChildForObjectProperty()
    {
        // depth=0 should produce pointer with no child structure
        var fields = new List<LiveFieldValue>
        {
            new() { Name = "Owner", TypeName = "ObjectProperty", Offset = 0x80, Size = 8,
                     PtrAddress = "0x18A00000100", PtrClassName = "Actor",
                     PtrClassAddr = "0xCLASS_ACTOR" }
        };

        var csx = await CsxExportService.GenerateCsxAsync(_dump, "TestStruct", fields, drilldownDepth: 0);

        Assert.DoesNotContain("Description=\"dummy\"", csx);
        Assert.DoesNotContain("Description=\"Health\"", csx);
        // Should be self-closing pointer element
        Assert.Contains("Vartype=\"Pointer\"", csx);
    }

    [Fact]
    public async Task GenerateCsx_DrilldownOne_RealChildStructure()
    {
        // Register the target instance by PtrAddress (WalkInstanceAsync lookup)
        _dump.RegisterStruct("0x18A00000100", new InstanceWalkResult
        {
            ClassName = "Actor",
            Fields = new List<LiveFieldValue>
            {
                new() { Name = "Health", TypeName = "FloatProperty", Offset = 0x100, Size = 4 },
                new() { Name = "MaxHealth", TypeName = "FloatProperty", Offset = 0x104, Size = 4 },
                new() { Name = "bIsAlive", TypeName = "BoolProperty", Offset = 0x108, Size = 1 },
            }
        });

        var fields = new List<LiveFieldValue>
        {
            new() { Name = "Owner", TypeName = "ObjectProperty", Offset = 0x80, Size = 8,
                     PtrAddress = "0x18A00000100", PtrClassName = "Actor",
                     PtrClassAddr = "0xCLASS_ACTOR" }
        };

        var csx = await CsxExportService.GenerateCsxAsync(_dump, "TestStruct", fields, drilldownDepth: 1);

        // Should have real child structure named "Actor"
        Assert.Contains("Name=\"Actor\"", csx);
        // Should have real field elements inside the child
        Assert.Contains("Description=\"Health\"", csx);
        Assert.Contains("Description=\"MaxHealth\"", csx);
        Assert.Contains("Description=\"bIsAlive\"", csx);
        // Should NOT have dummy placeholder
        Assert.DoesNotContain("Description=\"dummy\"", csx);
    }

    [Fact]
    public async Task GenerateCsx_DrilldownOne_NullPointer_NoChild()
    {
        // ObjectProperty with empty PtrAddress should have no child (CE native deref)
        var fields = new List<LiveFieldValue>
        {
            new() { Name = "Target", TypeName = "ObjectProperty", Offset = 0x90, Size = 8,
                     PtrAddress = "0x0", PtrClassName = "", PtrClassAddr = "" }
        };

        var csx = await CsxExportService.GenerateCsxAsync(_dump, "TestStruct", fields, drilldownDepth: 1);

        Assert.DoesNotContain("Description=\"dummy\"", csx);
        Assert.Contains("Description=\"Target\"", csx);
    }

    [Fact]
    public async Task GenerateCsx_DrilldownOne_NestedObjectProperty_NoChild()
    {
        // Register target instance that itself has an ObjectProperty
        _dump.RegisterStruct("0x18A00000200", new InstanceWalkResult
        {
            ClassName = "Pawn",
            Fields = new List<LiveFieldValue>
            {
                new() { Name = "Controller", TypeName = "ObjectProperty", Offset = 0x200, Size = 8,
                         PtrAddress = "0x18A00000400", PtrClassName = "Controller",
                         PtrClassAddr = "0xCLASS_CONTROLLER" },
                new() { Name = "MovementSpeed", TypeName = "FloatProperty", Offset = 0x208, Size = 4 },
            }
        });

        var fields = new List<LiveFieldValue>
        {
            new() { Name = "Pawn", TypeName = "ObjectProperty", Offset = 0x60, Size = 8,
                     PtrAddress = "0x18A00000200", PtrClassName = "Pawn",
                     PtrClassAddr = "0xCLASS_PAWN" }
        };

        var csx = await CsxExportService.GenerateCsxAsync(_dump, "TestStruct", fields, drilldownDepth: 1);

        // Top-level child should be real (Pawn fields)
        Assert.Contains("Name=\"Pawn\"", csx);
        Assert.Contains("Description=\"MovementSpeed\"", csx);
        // Nested ObjectProperty (Controller) should be present but with no child (depth exhausted)
        Assert.Contains("Description=\"Controller\"", csx);
        Assert.DoesNotContain("Description=\"dummy\"", csx);
    }

    [Fact]
    public async Task GenerateCsx_DrilldownOne_StructFlattenedWithPointerDrilldown()
    {
        // Test that struct flattening still works AND pointer drilldown works together

        // Struct inner fields (for struct resolution via WalkInstanceAsync at struct data addr)
        _dump.RegisterStruct("0x5000", new InstanceWalkResult
        {
            Fields = new List<LiveFieldValue>
            {
                new() { Name = "X", TypeName = "FloatProperty", Offset = 0, Size = 4 },
                new() { Name = "Ref", TypeName = "ObjectProperty", Offset = 8, Size = 8,
                         PtrAddress = "0x18A00000300", PtrClassName = "Widget",
                         PtrClassAddr = "0xCLASS_WIDGET" },
            }
        });

        // Widget instance for drilldown (looked up by PtrAddress)
        _dump.RegisterStruct("0x18A00000300", new InstanceWalkResult
        {
            ClassName = "Widget",
            Fields = new List<LiveFieldValue>
            {
                new() { Name = "Opacity", TypeName = "FloatProperty", Offset = 0x10, Size = 4 },
            }
        });

        var fields = new List<LiveFieldValue>
        {
            new() { Name = "Data", TypeName = "StructProperty", Offset = 0x100, Size = 16,
                     StructTypeName = "FMyData", StructDataAddr = "0x5000", StructClassAddr = "0x6000" }
        };

        var csx = await CsxExportService.GenerateCsxAsync(_dump, "TestStruct", fields, drilldownDepth: 1);

        // Struct fields should be flattened inline
        Assert.Contains("Description=\"FMyData / X\"", csx);
        // Struct's inner ObjectProperty should get real child structure
        Assert.Contains("Name=\"Widget\"", csx);
        Assert.Contains("Description=\"Opacity\"", csx);
    }

    [Fact]
    public async Task GenerateCsx_DrilldownTwo_RecursiveExpansion()
    {
        // Chain: root → Actor (depth 1) → PlayerController (depth 2)
        _dump.RegisterStruct("0x18A00000100", new InstanceWalkResult
        {
            ClassName = "Actor",
            Fields = new List<LiveFieldValue>
            {
                new() { Name = "Health", TypeName = "FloatProperty", Offset = 0x100, Size = 4 },
                new() { Name = "Controller", TypeName = "ObjectProperty", Offset = 0x200, Size = 8,
                         PtrAddress = "0x18A00000400", PtrClassName = "PlayerController",
                         PtrClassAddr = "0xCLASS_PC" },
            }
        });

        _dump.RegisterStruct("0x18A00000400", new InstanceWalkResult
        {
            ClassName = "PlayerController",
            Fields = new List<LiveFieldValue>
            {
                new() { Name = "InputComponent", TypeName = "ObjectProperty", Offset = 0x300, Size = 8,
                         PtrAddress = "0x18A00000500", PtrClassName = "InputComponent",
                         PtrClassAddr = "0xCLASS_INPUT" },
                new() { Name = "PlayerIndex", TypeName = "IntProperty", Offset = 0x308, Size = 4 },
            }
        });

        var fields = new List<LiveFieldValue>
        {
            new() { Name = "Owner", TypeName = "ObjectProperty", Offset = 0x80, Size = 8,
                     PtrAddress = "0x18A00000100", PtrClassName = "Actor",
                     PtrClassAddr = "0xCLASS_ACTOR" }
        };

        var csx = await CsxExportService.GenerateCsxAsync(_dump, "TestStruct", fields, drilldownDepth: 2);

        // Depth 1: Actor expanded with real fields
        Assert.Contains("Name=\"Actor\"", csx);
        Assert.Contains("Description=\"Health\"", csx);
        // Depth 2: PlayerController expanded inside Actor
        Assert.Contains("Name=\"PlayerController\"", csx);
        Assert.Contains("Description=\"PlayerIndex\"", csx);
        // Depth 3: InputComponent NOT expanded (no child, depth exhausted)
        Assert.Contains("Description=\"InputComponent\"", csx);
        // No dummy — depth exhausted means no child structure
        Assert.DoesNotContain("Description=\"dummy\"", csx);
    }

    [Fact]
    public async Task GenerateCsx_DrilldownCycleDetection_NoCrash()
    {
        // Circular reference: A → B → A (should not infinite-loop)
        _dump.RegisterStruct("0xA", new InstanceWalkResult
        {
            ClassName = "NodeA",
            Fields = new List<LiveFieldValue>
            {
                new() { Name = "Next", TypeName = "ObjectProperty", Offset = 0x10, Size = 8,
                         PtrAddress = "0xB", PtrClassName = "NodeB", PtrClassAddr = "0xCLASS_B" },
            }
        });

        _dump.RegisterStruct("0xB", new InstanceWalkResult
        {
            ClassName = "NodeB",
            Fields = new List<LiveFieldValue>
            {
                new() { Name = "Back", TypeName = "ObjectProperty", Offset = 0x10, Size = 8,
                         PtrAddress = "0xA", PtrClassName = "NodeA", PtrClassAddr = "0xCLASS_A" },
            }
        });

        var fields = new List<LiveFieldValue>
        {
            new() { Name = "Root", TypeName = "ObjectProperty", Offset = 0x80, Size = 8,
                     PtrAddress = "0xA", PtrClassName = "NodeA", PtrClassAddr = "0xCLASS_A" }
        };

        // Should complete without stack overflow or infinite loop
        var csx = await CsxExportService.GenerateCsxAsync(_dump, "TestStruct", fields, drilldownDepth: 3);

        // NodeA expanded at depth 1
        Assert.Contains("Name=\"NodeA\"", csx);
        // NodeB expanded at depth 2
        Assert.Contains("Name=\"NodeB\"", csx);
        // NodeA's "Back" pointer to 0xA should NOT re-expand (already visited) → no child
        Assert.DoesNotContain("Description=\"dummy\"", csx);
    }

    // --- Container drilldown tests ---

    [Fact]
    public async Task GenerateCsx_MapProperty_DrilldownOne_ShowsMapElements()
    {
        // MapProperty with inline elements should expand to show map entries
        var fields = new List<LiveFieldValue>
        {
            new() { Name = "GlobalAttributes", TypeName = "MapProperty", Offset = 0x30, Size = 8,
                     MapCount = 3, MapKeyType = "NameProperty", MapValueType = "ObjectProperty",
                     MapKeySize = 8, MapValueSize = 8,
                     MapDataAddr = "0x18CB9A7EBA0",
                     MapElements = new List<ContainerElementValue>
                     {
                         new() { Index = 0, Key = "structure", Value = "",
                                  KeyPtrName = "", KeyPtrAddress = "", KeyPtrClassName = "",
                                  ValuePtrName = "structure", ValuePtrAddress = "0xAAA", ValuePtrClassName = "ItemAttribute" },
                         new() { Index = 1, Key = "firepower", Value = "",
                                  KeyPtrName = "", KeyPtrAddress = "", KeyPtrClassName = "",
                                  ValuePtrName = "firepower", ValuePtrAddress = "0xBBB", ValuePtrClassName = "ItemAttribute" },
                         new() { Index = 2, Key = "expertise", Value = "",
                                  KeyPtrName = "", KeyPtrAddress = "", KeyPtrClassName = "",
                                  ValuePtrName = "expertise", ValuePtrAddress = "0xCCC", ValuePtrClassName = "ItemAttribute" },
                     }
            }
        };

        var csx = await CsxExportService.GenerateCsxAsync(_dump, "PlayerData", fields, drilldownDepth: 1);

        // Map should have a child structure named "GlobalAttributes"
        Assert.Contains("Name=\"GlobalAttributes\"", csx);
        // Map elements should appear as named fields
        Assert.Contains("Description=\"[0] structure\"", csx);
        Assert.Contains("Description=\"[1] firepower\"", csx);
        Assert.Contains("Description=\"[2] expertise\"", csx);
        // Elements should be Pointer type (ObjectProperty values)
        // Stride = AlignUp(8+8, 4) + 8 = 24; value offset = index*24 + 8(keySize)
        Assert.Contains("Offset=\"8\"", csx);   // [0]: 0*24+8 = 8
        Assert.Contains("Offset=\"32\"", csx);  // [1]: 1*24+8 = 32
        Assert.Contains("Offset=\"56\"", csx);  // [2]: 2*24+8 = 56
    }

    [Fact]
    public async Task GenerateCsx_MapProperty_DrilldownTwo_ExpandsElementPointers()
    {
        // Register target instances that map value pointers point to
        _dump.RegisterStruct("0xAAA", new InstanceWalkResult
        {
            ClassName = "ItemAttribute",
            Fields = new List<LiveFieldValue>
            {
                new() { Name = "BaseValue", TypeName = "FloatProperty", Offset = 0x30, Size = 4 },
                new() { Name = "CurrentValue", TypeName = "FloatProperty", Offset = 0x34, Size = 4 },
            }
        });

        var fields = new List<LiveFieldValue>
        {
            new() { Name = "Attrs", TypeName = "MapProperty", Offset = 0x30, Size = 8,
                     MapCount = 1, MapKeyType = "NameProperty", MapValueType = "ObjectProperty",
                     MapKeySize = 8, MapValueSize = 8,
                     MapDataAddr = "0x1000",
                     MapElements = new List<ContainerElementValue>
                     {
                         new() { Index = 0, Key = "structure",
                                  ValuePtrName = "structure", ValuePtrAddress = "0xAAA",
                                  ValuePtrClassName = "ItemAttribute" },
                     }
            }
        };

        var csx = await CsxExportService.GenerateCsxAsync(_dump, "PlayerData", fields, drilldownDepth: 2);

        // Layer 1: Map elements
        Assert.Contains("Name=\"Attrs\"", csx);
        Assert.Contains("Description=\"[0] structure\"", csx);
        // Layer 2: ItemAttribute fields inside the map element's pointer target
        Assert.Contains("Name=\"ItemAttribute\"", csx);
        Assert.Contains("Description=\"BaseValue\"", csx);
        Assert.Contains("Description=\"CurrentValue\"", csx);
    }

    [Fact]
    public async Task GenerateCsx_ArrayProperty_DrilldownOne_ShowsPointerElements()
    {
        // ArrayProperty with ObjectProperty inner type should expand to show pointer elements
        var fields = new List<LiveFieldValue>
        {
            new() { Name = "Actors", TypeName = "ArrayProperty", Offset = 0x50, Size = 8,
                     ArrayCount = 2, ArrayInnerType = "ObjectProperty", ArrayElemSize = 8,
                     ArrayDataAddr = "0x5000",
                     ArrayElements = new List<ArrayElementValue>
                     {
                         new() { Index = 0, PtrAddress = "0xD01", PtrName = "Player", PtrClassName = "Actor" },
                         new() { Index = 1, PtrAddress = "0xD02", PtrName = "Enemy", PtrClassName = "Actor" },
                     }
            }
        };

        var csx = await CsxExportService.GenerateCsxAsync(_dump, "TestStruct", fields, drilldownDepth: 1);

        // Array should have a child structure
        Assert.Contains("Name=\"Actors\"", csx);
        Assert.Contains("Description=\"[0] Player\"", csx);
        Assert.Contains("Description=\"[1] Enemy\"", csx);
        // Elements at sequential offsets: index * elemSize
        Assert.Contains("Offset=\"0\"", csx);   // [0]: 0*8 = 0
        Assert.Contains("Offset=\"8\"", csx);   // [1]: 1*8 = 8
    }

    [Fact]
    public async Task GenerateCsx_MapProperty_DepthZero_NoChild()
    {
        // At depth=0, MapProperty should have no child even with elements
        var fields = new List<LiveFieldValue>
        {
            new() { Name = "MyMap", TypeName = "MapProperty", Offset = 0x30, Size = 8,
                     MapCount = 1, MapKeyType = "IntProperty", MapValueType = "IntProperty",
                     MapKeySize = 4, MapValueSize = 4,
                     MapDataAddr = "0x1000",
                     MapElements = new List<ContainerElementValue>
                     {
                         new() { Index = 0, Key = "42", Value = "100" },
                     }
            }
        };

        var csx = await CsxExportService.GenerateCsxAsync(_dump, "TestStruct", fields, drilldownDepth: 0);

        // Should NOT have child structure
        Assert.DoesNotContain("Name=\"MyMap\"", csx);
        // But should still have the pointer element
        Assert.Contains("Description=\"MyMap\"", csx);
        Assert.Contains("Vartype=\"Pointer\"", csx);
    }

    // --- Struct array drilldown tests ---

    [Fact]
    public async Task GenerateCsx_ArrayProperty_StructInner_DrilldownOne_FlattenSubFields()
    {
        // ArrayProperty with StructProperty inner type and Phase F sub-fields
        // e.g., MissionSaveState [2 x TaskSaveGameData (0x140)]
        var fields = new List<LiveFieldValue>
        {
            new() { Name = "MissionSaveState", TypeName = "ArrayProperty", Offset = 0x3F8, Size = 8,
                     ArrayCount = 2, ArrayInnerType = "StructProperty", ArrayElemSize = 0x140,
                     ArrayStructType = "TaskSaveGameData",
                     ArrayDataAddr = "0x5000",
                     ArrayElements = new List<ArrayElementValue>
                     {
                         new() { Index = 0, StructFields = new List<StructSubFieldValue>
                         {
                             new() { Name = "TaskId", TypeName = "IntProperty", Offset = 0, Size = 4 },
                             new() { Name = "TaskName", TypeName = "StrProperty", Offset = 8, Size = 8 },
                             new() { Name = "bCompleted", TypeName = "BoolProperty", Offset = 0x10, Size = 1 },
                         }},
                         new() { Index = 1, StructFields = new List<StructSubFieldValue>
                         {
                             new() { Name = "TaskId", TypeName = "IntProperty", Offset = 0, Size = 4 },
                             new() { Name = "TaskName", TypeName = "StrProperty", Offset = 8, Size = 8 },
                             new() { Name = "bCompleted", TypeName = "BoolProperty", Offset = 0x10, Size = 1 },
                         }},
                     }
            }
        };

        var csx = await CsxExportService.GenerateCsxAsync(_dump, "TestStruct", fields, drilldownDepth: 1);

        // Child structure should be named after the field
        Assert.Contains("Name=\"MissionSaveState\"", csx);
        // Element [0] sub-fields at absolute offsets (0 * 0x140 + sub.Offset)
        Assert.Contains("Description=\"[0] / TaskId\"", csx);
        Assert.Contains("Description=\"[0] / TaskName\"", csx);
        Assert.Contains("Description=\"[0] / bCompleted\"", csx);
        // Element [1] sub-fields at absolute offsets (1 * 0x140 + sub.Offset)
        Assert.Contains("Description=\"[1] / TaskId\"", csx);
        Assert.Contains("Description=\"[1] / TaskName\"", csx);
        // Verify offset calculation: [1]/TaskId = 1 * 0x140 + 0 = 320
        Assert.Contains("Offset=\"320\"", csx);
        // [1]/TaskName = 1 * 0x140 + 8 = 328
        Assert.Contains("Offset=\"328\"", csx);
        // Proper type mapping for sub-fields
        Assert.Contains("Vartype=\"4 Bytes\"", csx);  // IntProperty
        Assert.Contains("Vartype=\"Pointer\"", csx);   // StrProperty
        Assert.Contains("Vartype=\"Byte\"", csx);      // BoolProperty
    }

    [Fact]
    public async Task GenerateCsx_ArrayProperty_ScalarInner_DrilldownOne_ShowsElements()
    {
        // ArrayProperty with FloatProperty inner type — each element is a simple scalar
        var fields = new List<LiveFieldValue>
        {
            new() { Name = "Weights", TypeName = "ArrayProperty", Offset = 0x50, Size = 8,
                     ArrayCount = 3, ArrayInnerType = "FloatProperty", ArrayElemSize = 4,
                     ArrayDataAddr = "0x6000",
                     ArrayElements = new List<ArrayElementValue>
                     {
                         new() { Index = 0, Value = "1.0" },
                         new() { Index = 1, Value = "0.5" },
                         new() { Index = 2, Value = "0.0" },
                     }
            }
        };

        var csx = await CsxExportService.GenerateCsxAsync(_dump, "TestStruct", fields, drilldownDepth: 1);

        // Child structure for the array
        Assert.Contains("Name=\"Weights\"", csx);
        // Elements with value hints in description
        Assert.Contains("Description=\"[0] 1.0\"", csx);
        Assert.Contains("Description=\"[1] 0.5\"", csx);
        Assert.Contains("Description=\"[2] 0.0\"", csx);
        // Proper type mapping: FloatProperty → Float
        Assert.Contains("Vartype=\"Float\"", csx);
        // Sequential offsets: index * elemSize (4)
        Assert.Contains("Offset=\"0\"", csx);   // [0]: 0*4 = 0
        Assert.Contains("Offset=\"4\"", csx);   // [1]: 1*4 = 4
        Assert.Contains("Offset=\"8\"", csx);   // [2]: 2*4 = 8
    }

    [Fact]
    public async Task GenerateCsx_SetProperty_ScalarElem_DrilldownOne_ShowsElements()
    {
        // SetProperty with NameProperty element type (non-pointer)
        var fields = new List<LiveFieldValue>
        {
            new() { Name = "Tags", TypeName = "SetProperty", Offset = 0x60, Size = 8,
                     SetCount = 2, SetElemType = "NameProperty", SetElemSize = 8,
                     SetDataAddr = "0x7000",
                     SetElements = new List<ContainerElementValue>
                     {
                         new() { Index = 0, Key = "Hostile" },
                         new() { Index = 1, Key = "Boss" },
                     }
            }
        };

        var csx = await CsxExportService.GenerateCsxAsync(_dump, "TestStruct", fields, drilldownDepth: 1);

        // Child structure
        Assert.Contains("Name=\"Tags\"", csx);
        // Elements with key labels
        Assert.Contains("Description=\"[0] Hostile\"", csx);
        Assert.Contains("Description=\"[1] Boss\"", csx);
        // NameProperty → 8 Bytes
        Assert.Contains("Vartype=\"8 Bytes\"", csx);
        // TSparseArray stride: AlignUp(8, 4) + 8 = 16
        Assert.Contains("Offset=\"0\"", csx);   // [0]: 0*16 = 0
        Assert.Contains("Offset=\"16\"", csx);  // [1]: 1*16 = 16
    }

    [Fact]
    public async Task GenerateCsx_StructProperty_WithInnerArrayContainer_Drilldown()
    {
        // StructProperty flattened inline, containing an ArrayProperty that should also drilldown.
        // Simulates: StationCargoSaveState (StructProperty at 0x138) with inner Cargo (ArrayProperty).

        // Register the struct's inner fields (from WalkInstanceAsync at struct data addr)
        _dump.RegisterStruct("0x8000", new InstanceWalkResult
        {
            Fields = new List<LiveFieldValue>
            {
                new() { Name = "StationId", TypeName = "IntProperty", Offset = 0, Size = 4 },
                new() { Name = "Cargo", TypeName = "ArrayProperty", Offset = 0x10, Size = 8,
                         ArrayCount = 2, ArrayInnerType = "ObjectProperty", ArrayElemSize = 8,
                         ArrayDataAddr = "0x9000",
                         ArrayElements = new List<ArrayElementValue>
                         {
                             new() { Index = 0, PtrAddress = "0xE01", PtrName = "FuelCell", PtrClassName = "CargoItem" },
                             new() { Index = 1, PtrAddress = "0xE02", PtrName = "Ore", PtrClassName = "CargoItem" },
                         }
                },
            }
        });

        // Register cargo item instances for depth-2 resolution
        _dump.RegisterStruct("0xE01", new InstanceWalkResult
        {
            ClassName = "CargoItem",
            Fields = new List<LiveFieldValue>
            {
                new() { Name = "Quantity", TypeName = "IntProperty", Offset = 0x20, Size = 4 },
            }
        });

        var fields = new List<LiveFieldValue>
        {
            new() { Name = "StationCargoSaveState", TypeName = "StructProperty", Offset = 0x138, Size = 0x40,
                     StructTypeName = "FStationCargo", StructDataAddr = "0x8000", StructClassAddr = "0xC000" }
        };

        var csx = await CsxExportService.GenerateCsxAsync(_dump, "TestStruct", fields, drilldownDepth: 2);

        // Struct fields flattened inline at layer 0
        Assert.Contains("Description=\"FStationCargo / StationId\"", csx);
        // Inner ArrayProperty (Cargo) should have a child structure with pointer elements
        Assert.Contains("Description=\"FStationCargo / Cargo\"", csx);
        Assert.Contains("Name=\"Cargo\"", csx);
        Assert.Contains("Description=\"[0] FuelCell\"", csx);
        Assert.Contains("Description=\"[1] Ore\"", csx);
        // At depth-2, pointer targets should be expanded
        Assert.Contains("Name=\"CargoItem\"", csx);
        Assert.Contains("Description=\"Quantity\"", csx);
    }

    [Fact]
    public async Task GenerateCsx_ArrayProperty_StructInner_NoSubFields_EmitsRawBlock()
    {
        // Struct array where Phase F sub-fields are not available → raw bytes blocks
        var fields = new List<LiveFieldValue>
        {
            new() { Name = "History", TypeName = "ArrayProperty", Offset = 0x80, Size = 8,
                     ArrayCount = 2, ArrayInnerType = "StructProperty", ArrayElemSize = 32,
                     ArrayStructType = "FHistoryEntry",
                     ArrayDataAddr = "0xA000",
                     ArrayElements = new List<ArrayElementValue>
                     {
                         new() { Index = 0, StructFields = null },
                         new() { Index = 1, StructFields = new List<StructSubFieldValue>() },
                     }
            }
        };

        var csx = await CsxExportService.GenerateCsxAsync(_dump, "TestStruct", fields, drilldownDepth: 1);

        // Child structure
        Assert.Contains("Name=\"History\"", csx);
        // Elements without sub-fields → raw blocks with struct type label
        Assert.Contains("Description=\"[0] FHistoryEntry\"", csx);
        Assert.Contains("Description=\"[1] FHistoryEntry\"", csx);
        // Raw byte size = elemSize = 32
        Assert.Contains("Bytesize=\"32\"", csx);
    }

    [Fact]
    public async Task GenerateCsx_ArrayProperty_StructInner_WithPointerSubField_DrilldownTwo()
    {
        // Struct array where sub-fields include ObjectProperty with resolved pointer info.
        // Depth 2: struct array expansion (depth 1) + pointer sub-field drilldown (depth 2).
        // Simulates: Ships [9 x ShipData] → [0] / Inventory → Inventory object fields

        // Register the Inventory instance (pointed to by sub-field pointer)
        _dump.RegisterStruct("0x14FE83DEC00", new InstanceWalkResult
        {
            ClassName = "Inventory",
            Fields = new List<LiveFieldValue>
            {
                new() { Name = "TitleText", TypeName = "TextProperty", Offset = 0x28, Size = 8 },
                new() { Name = "Cargo", TypeName = "ArrayProperty", Offset = 0xD8, Size = 8,
                         ArrayCount = 234, ArrayInnerType = "ObjectProperty", ArrayElemSize = 8 },
                new() { Name = "bRespectStackLimits", TypeName = "BoolProperty", Offset = 0xE8, Size = 1 },
            }
        });

        var fields = new List<LiveFieldValue>
        {
            new() { Name = "Ships", TypeName = "ArrayProperty", Offset = 0x468, Size = 8,
                     ArrayCount = 9, ArrayInnerType = "StructProperty", ArrayElemSize = 0x3E0,
                     ArrayStructType = "ShipData",
                     ArrayDataAddr = "0x5000",
                     ArrayElements = new List<ArrayElementValue>
                     {
                         new() { Index = 0, StructFields = new List<StructSubFieldValue>
                         {
                             new() { Name = "Name", TypeName = "NameProperty", Offset = 0, Size = 8 },
                             new() { Name = "Inventory", TypeName = "ObjectProperty", Offset = 0x10, Size = 8,
                                     PtrAddress = "0x14FE83DEC00", PtrName = "Inventory_2147445402",
                                     PtrClassName = "Inventory", PtrClassAddr = "0xCLASS_INV" },
                             new() { Name = "HealthRatio", TypeName = "FloatProperty", Offset = 0x360, Size = 4 },
                         }},
                     }
            }
        };

        var csx = await CsxExportService.GenerateCsxAsync(_dump, "TestStruct", fields, drilldownDepth: 2);

        // Layer 1: Struct array expansion — sub-fields flattened
        Assert.Contains("Name=\"Ships\"", csx);
        Assert.Contains("Description=\"[0] / Name\"", csx);
        Assert.Contains("Description=\"[0] / Inventory\"", csx);
        Assert.Contains("Description=\"[0] / HealthRatio\"", csx);
        // Layer 2: Inventory pointer resolved into child structure
        Assert.Contains("Name=\"Inventory\"", csx);
        Assert.Contains("Description=\"TitleText\"", csx);
        Assert.Contains("Description=\"Cargo\"", csx);
        Assert.Contains("Description=\"bRespectStackLimits\"", csx);
    }
}
