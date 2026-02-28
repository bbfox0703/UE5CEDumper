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

    /// <summary>Register a struct walk result for testing struct flattening.</summary>
    public void RegisterStruct(string addr, InstanceWalkResult result)
        => _structResults[addr] = result;

    public Task<InstanceWalkResult> WalkInstanceAsync(string addr, string? classAddr = null,
        int arrayLimit = 64, CancellationToken ct = default)
    {
        if (_structResults.TryGetValue(addr, out var result))
            return Task.FromResult(result);
        return Task.FromResult(new InstanceWalkResult { Fields = new List<LiveFieldValue>() });
    }

    // Unused stubs — throw NotImplementedException to catch unexpected calls
    public Task<EngineState> InitAsync(CancellationToken ct = default) => throw new NotImplementedException();
    public Task<EngineState> GetPointersAsync(CancellationToken ct = default) => throw new NotImplementedException();
    public Task<int> GetObjectCountAsync(CancellationToken ct = default) => throw new NotImplementedException();
    public Task<ObjectListResult> GetObjectListAsync(int offset, int limit, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<ObjectDetail> GetObjectAsync(string addr, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<ObjectDetail> FindObjectAsync(string path, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<ObjectListResult> SearchObjectsAsync(string query, int limit = 200, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<ClassInfoModel> WalkClassAsync(string addr, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<byte[]> ReadMemAsync(string addr, int size, CancellationToken ct = default) => throw new NotImplementedException();
    public Task WriteMemAsync(string addr, byte[] data, CancellationToken ct = default) => throw new NotImplementedException();
    public Task WatchAsync(string addr, int size, int intervalMs, CancellationToken ct = default) => throw new NotImplementedException();
    public Task UnwatchAsync(string addr, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<WorldWalkResult> WalkWorldAsync(int actorLimit = 200, int arrayLimit = 64, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<FindInstancesResult> FindInstancesAsync(string className, int limit = 500, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<CePointerInfo> GetCePointerInfoAsync(string addr, int fieldOffset = 0, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<ArrayElementsResult> ReadArrayElementsAsync(string addr, int fieldOffset, string innerAddr, string innerType, int elemSize, int offset = 0, int limit = 64, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<AddressLookupResult> FindByAddressAsync(string addr, CancellationToken ct = default) => throw new NotImplementedException();
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
    public async Task GenerateCsx_ObjectProperty_EmitsDummyChild()
    {
        var fields = new List<LiveFieldValue>
        {
            new() { Name = "Target", TypeName = "ObjectProperty", Offset = 0x80, Size = 8,
                     PtrAddress = "0x18AAD37FB00" }
        };

        var csx = await CsxExportService.GenerateCsxAsync(_dump, "TestStruct", fields);

        Assert.Contains("Vartype=\"Pointer\"", csx);
        Assert.Contains("Description=\"Target\"", csx);
        // Dummy child structure
        Assert.Contains("Description=\"dummy\"", csx);
        Assert.Contains("Autocreated from", csx);
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
    public async Task GenerateCsx_MapProperty_EmitsPointerWithDummy()
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
        Assert.Contains("Autocreated from", csx);
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
}
