using UE5DumpUI.Core;
using UE5DumpUI.Models;
using UE5DumpUI.Services;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// Stub IDumpService that returns empty results — sufficient for
/// testing CSX generation where StructProperty resolution is not needed.
/// Other test files may sub-class to override one or two methods (e.g.
/// <c>InterestingFunctionsViewModelTests.FakeDumpService</c> overrides
/// <c>ListAllFunctionsAsync</c>).
/// </summary>
public class StubDumpService : IDumpService
{
    private readonly Dictionary<string, InstanceWalkResult> _structResults = new();
    private readonly Dictionary<string, ClassInfoModel> _classResults = new();

    /// <summary>Register a struct walk result for testing struct flattening and drilldown.</summary>
    public void RegisterStruct(string addr, InstanceWalkResult result)
        => _structResults[addr] = result;

    /// <summary>Register a class walk result for testing class-based lookups.</summary>
    public void RegisterClass(string addr, ClassInfoModel result)
        => _classResults[addr] = result;

    public virtual Task<InstanceWalkResult> WalkInstanceAsync(string addr, string? classAddr = null,
        int arrayLimit = 64, int previewLimit = 2, bool fillGaps = false, bool lean = false,
        CancellationToken ct = default)
    {
        if (_structResults.TryGetValue(addr, out var result))
            return Task.FromResult(result);
        return Task.FromResult(new InstanceWalkResult { Fields = new List<LiveFieldValue>() });
    }

    public virtual Task<ClassInfoModel> WalkClassAsync(string addr, CancellationToken ct = default)
    {
        if (_classResults.TryGetValue(addr, out var result))
            return Task.FromResult(result);
        return Task.FromResult(new ClassInfoModel { Fields = new List<FieldInfoModel>() });
    }

    /// <summary>
    /// Default test-stub implementation that delegates to N single
    /// <see cref="WalkClassAsync"/> calls. This mirrors the DLL-side
    /// contract (batch = loop over singles) so callers under test see
    /// byte-identical results whether they go through the single or
    /// batched code path. Override in a sub-stub to inject batch-
    /// specific behaviour (e.g. forced failure for fallback testing).
    /// </summary>
    public virtual async Task<List<ClassInfoModel>> WalkClassesBatchAsync(string[] addrs, CancellationToken ct = default)
    {
        var result = new List<ClassInfoModel>(addrs.Length);
        foreach (var addr in addrs)
        {
            ct.ThrowIfCancellationRequested();
            result.Add(await WalkClassAsync(addr, ct));
        }
        return result;
    }

    // Unused stubs — throw NotImplementedException to catch unexpected calls
    public Task<EngineState> InitAsync(CancellationToken ct = default) => throw new NotImplementedException();
    // virtual: DumpExplorer's cross-game match gate reads the live identity through this,
    // so its tests must be able to supply one. Non-overriding stubs keep throwing.
    public virtual Task<EngineState> GetPointersAsync(CancellationToken ct = default) => throw new NotImplementedException();
    public virtual Task<TrainerOffsets> GetTrainerOffsetsAsync(CancellationToken ct = default) => throw new NotImplementedException();
    public Task<EngineState> SetUeVersionOverrideAsync(int version, bool persist = true, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<EngineState> SetInvokeTimeoutAsync(int timeoutMs, bool persist = true, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<int> GetObjectCountAsync(CancellationToken ct = default) => throw new NotImplementedException();
    public virtual Task<ObjectListResult> GetObjectListAsync(int offset, int limit, CancellationToken ct = default, bool includePath = false) => throw new NotImplementedException();
    // virtual: ClassStructViewModelConcurrencyTests overrides this to gate the
    // metaclass lookup, which is the extra round-trip that makes an instance
    // selection lose to a later class-like one (audit #5 AE2). Without the
    // keyword that branch is unreachable from a test and its negative control
    // reports green against broken code.
    public virtual Task<ObjectDetail> GetObjectAsync(string addr, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<ObjectDetail> FindObjectAsync(string path, CancellationToken ct = default) => throw new NotImplementedException();
    public virtual Task<ObjectListResult> SearchObjectsAsync(string query, int limit = 200, bool instancesOnly = false, CancellationToken ct = default) => throw new NotImplementedException();
    public virtual Task<PeProfileStartResult> PeProfileStartAsync(CancellationToken ct = default) => throw new NotImplementedException();
    public virtual Task PeProfileStopAsync(CancellationToken ct = default) => throw new NotImplementedException();
    public virtual Task<PeProfileResult> PeProfileGetAsync(int limit = 200, CancellationToken ct = default) => throw new NotImplementedException();
    public virtual Task<IReadOnlyList<InstanceWalkResult>> WalkInstanceBatchAsync(IReadOnlyList<(string Addr, string? ClassAddr)> items, int arrayLimit = 64, int previewLimit = 2, bool fillGaps = false, bool lean = false, CancellationToken ct = default) => throw new NotImplementedException();
    public virtual Task<DiagnosticsResult> GetDiagnosticsAsync(int limit = 25, CancellationToken ct = default) => throw new NotImplementedException();
    public virtual Task ResetDiagnosticsAsync(CancellationToken ct = default) => throw new NotImplementedException();
    public Task<byte[]> ReadMemAsync(string addr, int size, CancellationToken ct = default) => throw new NotImplementedException();
    public virtual Task WriteMemAsync(string addr, byte[] data, CancellationToken ct = default) => throw new NotImplementedException();
    public Task WatchAsync(string addr, int size, int intervalMs, CancellationToken ct = default) => throw new NotImplementedException();
    public Task UnwatchAsync(string addr, CancellationToken ct = default) => throw new NotImplementedException();
    public virtual Task<WorldWalkResult> WalkWorldAsync(int actorLimit = 200, int arrayLimit = 64, CancellationToken ct = default)
        => Task.FromResult(new WorldWalkResult
        {
            WorldAddr = "0xA8B0", WorldName = "TestWorld",
            LevelAddr = "0x4500", LevelName = "PersistentLevel", LevelOffset = 0x30,
        });
    public virtual Task<FindInstancesResult> FindInstancesAsync(string className, bool exactMatch = false, int limit = 500, bool newestFirst = false, string nameFilter = "", IReadOnlyList<string>? excludeClasses = null, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<CePointerInfo> GetCePointerInfoAsync(string addr, int fieldOffset = 0, CancellationToken ct = default) => throw new NotImplementedException();
    public virtual Task<PackedConstsResult> SetPackedConstsAsync(int alignBits = 0, ulong ptrMaskBits = 0, bool force = false, int serialOff = -1, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<ArrayElementsResult> ReadArrayElementsAsync(string addr, int fieldOffset, string innerAddr, string innerType, int elemSize, int offset = 0, int limit = 64, CancellationToken ct = default) => throw new NotImplementedException();
    public virtual Task<AddressLookupResult> FindByAddressAsync(string addr, int containerElemCap = 256, CancellationToken ct = default) => throw new NotImplementedException();
    public virtual Task<FindReferencesResult> FindReferencesToUObjectAsync(string addr, int maxResults = 32, CancellationToken ct = default) => throw new NotImplementedException();
    public virtual Task<RelatedObjectsResult> GetRelatedObjectsAsync(string addr, int maxResults = 128, CancellationToken ct = default) => throw new NotImplementedException();
    public virtual Task<CurrentTargetResult> DetectCurrentTargetAsync(int maxCandidates = 8, CancellationToken ct = default) => throw new NotImplementedException();
    public virtual Task<GameEngineResult> ResolveGameEngineAsync(CancellationToken ct = default) => throw new NotImplementedException();
    public virtual Task<GWorldPathResult> FindPathFromGWorldAsync(string target, string? objectAddr = null, int maxDepth = 5, CancellationToken ct = default, string rootKind = "gworld", bool deep = false, int containerDepth = 1) => throw new NotImplementedException();
    public virtual Task<FindPropertyXrefsResult> FindPropertyXrefsAsync(string propAddr, bool gameOnly = true, int maxResults = 200, CancellationToken ct = default) => throw new NotImplementedException();
    public virtual Task<FindPropertyXrefsResult> FindFunctionsByClassAsync(string classAddr, bool gameOnly = true, int maxResults = 200, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<string> GetFunctionCodeAddrAsync(string funcAddr, CancellationToken ct = default) => throw new NotImplementedException();
    public virtual Task<FunctionPropRefsResult> WalkFunctionPropsAsync(string funcAddr, CancellationToken ct = default) => throw new NotImplementedException();
    // virtual: the USMAP exporter collects enums before it collects classes, so a test of
    // the CLASS collector (audit #5 W8) has to be able to answer this without throwing.
    public virtual Task<List<EnumDefinition>> ListEnumsAsync(CancellationToken ct = default) => throw new NotImplementedException();
    public virtual Task<List<FunctionInfoModel>> WalkFunctionsAsync(string addr, CancellationToken ct = default) => throw new NotImplementedException();
    public virtual Task<PropertySearchResult> SearchPropertiesAsync(string query, string[]? types = null, bool gameOnly = true, bool deep = false, int limit = 200, CancellationToken ct = default) => throw new NotImplementedException();
    public virtual Task<PropertySearchBatchResult> SearchPropertiesBatchAsync(string[] queries, string[]? types = null, bool gameOnly = true, int limitPerQuery = 200, CancellationToken ct = default) => throw new NotImplementedException();
    public virtual Task<ClassListResult> ListClassesAsync(bool gameOnly = true, int limit = 5000, CancellationToken ct = default) => throw new NotImplementedException();
    public virtual Task<IReadOnlyList<NoiseClassInfo>> DetectNoiseClassesAsync(IReadOnlyList<string> classNames, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<NoiseClassInfo>>(new List<NoiseClassInfo>());
    public virtual Task<AllFunctionsResult> ListAllFunctionsAsync(bool gameOnly = true, int limit = 100000, CancellationToken ct = default) => throw new NotImplementedException();
    public virtual Task<ValueScanBeginResult> BeginValueScanAsync(ValueScanDataType dataType, ValueScanType scanType, string value, string? value2 = null, bool gameOnly = true, int maxResults = 50000, FloatRoundMode roundMode = FloatRoundMode.Round, bool caseSensitive = false, bool parallel = true, bool batchRead = true, bool deep = false, bool nativeC = false, bool newestFirst = false, int pageSize = 1000, int deadlineMs = 15000, bool autoSkipNoise = false, CancellationToken ct = default) => throw new NotImplementedException();
    public virtual Task<ValueScanRefineResult> RefineValueScanAsync(ulong sessionId, ValueScanType scanType, string? value = null, string? value2 = null, FloatRoundMode roundMode = FloatRoundMode.Round, bool caseSensitive = false, int pageSize = 1000, CancellationToken ct = default) => throw new NotImplementedException();
    public virtual Task<ValueScanWindowResult> QueryCandidatesAsync(ulong sessionId, int offset, int limit, string? filter = null, string? sortKey = null, bool sortDesc = false, IReadOnlyList<string>? excludeClasses = null, CancellationToken ct = default) => throw new NotImplementedException();
    public virtual Task EndValueScanAsync(ulong sessionId, CancellationToken ct = default) => throw new NotImplementedException();
    public virtual Task<GroupScanBeginResult> BeginGroupScanAsync(IReadOnlyList<GroupSlotInput> slots, bool gameOnly = true, int maxResults = 50000, bool deep = false, bool crossObject = false, bool nativeC = false, bool newestFirst = false, int pageSize = 1000, int deadlineMs = 15000, bool autoSkipNoise = false, FloatRoundMode roundMode = FloatRoundMode.Round, int perSlotCap = 256, CancellationToken ct = default) => throw new NotImplementedException();
    public virtual Task<GroupScanRefineResult> RefineGroupScanAsync(ulong sessionId, IReadOnlyList<GroupSlotInput> slots, int pageSize = 1000, FloatRoundMode roundMode = FloatRoundMode.Round, CancellationToken ct = default) => throw new NotImplementedException();
    public virtual Task<GroupScanWindowResult> QueryGroupCandidatesAsync(ulong sessionId, int offset, int limit, string? filter = null, string? sortKey = null, bool sortDesc = false, IReadOnlyList<string>? excludeClasses = null, CancellationToken ct = default) => throw new NotImplementedException();
    public virtual Task<IReadOnlyList<GroupSlotMatch>> QueryGroupSlotLeavesAsync(ulong sessionId, GroupSlotMatch slot, string instanceAddr, string className, int offset = 0, int limit = 0, CancellationToken ct = default) => throw new NotImplementedException();
    public virtual Task EndGroupScanAsync(ulong sessionId, CancellationToken ct = default) => throw new NotImplementedException();
    public virtual Task<int> BeginSnapshotAsync(string dataType, CancellationToken ct = default) => throw new NotImplementedException();
    public virtual Task<SnapshotChunkResult> SnapshotChunkAsync(string dataType, bool gameOnly, int offset, int limit, bool nativeC = false, bool autoSkipNoise = true, string numericFamily = "Any", CancellationToken ct = default) => throw new NotImplementedException();
    public Task<RescanStartResult> StartRescanAsync(CancellationToken ct = default) => throw new NotImplementedException();
    public Task<RescanStatusResult> GetRescanStatusAsync(CancellationToken ct = default) => throw new NotImplementedException();
    public Task<EngineState> ApplyRescanAsync(CancellationToken ct = default) => throw new NotImplementedException();
    public Task TriggerScanAsync(CancellationToken ct = default) => throw new NotImplementedException();
    public Task<ScanStatusResult> GetScanStatusAsync(CancellationToken ct = default) => throw new NotImplementedException();
    public virtual Task<InvokeFunctionResult> InvokeFunctionAsync(string funcName, string? instanceAddr = null, string? className = null, int parmsSize = 0, string? paramsHex = null, bool directCall = false, IReadOnlyList<InvokeStringParam>? stringParams = null, CancellationToken ct = default) => throw new NotImplementedException();
    public virtual Task<int> GetDebugCameraStateAsync(CancellationToken ct = default) => throw new NotImplementedException();
    public virtual Task<int> SetDebugCameraAsync(bool enable, CancellationToken ct = default) => throw new NotImplementedException();
    public virtual Task<int> GetGodModeAsync(CancellationToken ct = default) => throw new NotImplementedException();
    public virtual Task<int> SetGodModeAsync(bool enable, CancellationToken ct = default) => throw new NotImplementedException();
    public virtual Task<int> GetForegroundLockAsync(CancellationToken ct = default) => throw new NotImplementedException();
    public virtual Task<int> SetForegroundLockAsync(bool enable, CancellationToken ct = default) => throw new NotImplementedException();
    public virtual Task<MovementParams> GetMovementParamsAsync(CancellationToken ct = default) => throw new NotImplementedException();
    public virtual Task<MovementSetResult> SetMovementMultiplierAsync(string knob, double multiplier, CancellationToken ct = default) => throw new NotImplementedException();
    public virtual Task<MovementSetResult> ResetMovementAsync(string knob, CancellationToken ct = default) => throw new NotImplementedException();
    public virtual Task<MovementVectorResult> SetGravityDirectionAsync(double x, double y, double z, CancellationToken ct = default) => throw new NotImplementedException();
    public virtual Task<MovementVectorResult> ResetGravityDirectionAsync(CancellationToken ct = default) => throw new NotImplementedException();
    public virtual Task<TimeState> GetTimeStateAsync(CancellationToken ct = default) => throw new NotImplementedException();
    public virtual Task<ProtectState> GetProtectStateAsync(CancellationToken ct = default) => throw new NotImplementedException();
    public virtual Task<TimeDilationSetResult> SetTimeDilationAsync(string target, double value, CancellationToken ct = default) => throw new NotImplementedException();
    public virtual Task<TimeDilationSetResult> ResetTimeDilationAsync(string target, CancellationToken ct = default) => throw new NotImplementedException();
    public virtual Task<FlyStatus> FlySetAsync(bool? enable, double? speed, int? preset, bool? noclip, CancellationToken ct = default) => throw new NotImplementedException();
    public virtual Task<FlyStatus> FlyGetStateAsync(CancellationToken ct = default) => throw new NotImplementedException();
    public virtual Task<SeeThroughStatus> SeeThroughSetAsync(bool? enable, int? count, CancellationToken ct = default) => throw new NotImplementedException();
    public virtual Task<SeeThroughStatus> SeeThroughGetStateAsync(CancellationToken ct = default) => throw new NotImplementedException();
    public virtual Task<ForceFieldResult> ForceFieldAsync(string className, string fieldName, string kind, double value = 0, bool on = false, CancellationToken ct = default) => throw new NotImplementedException();
    public virtual Task<int> ResetFieldAsync(string className, string fieldName, CancellationToken ct = default) => throw new NotImplementedException();
    public virtual Task<int> ResetAllFieldsAsync(CancellationToken ct = default) => throw new NotImplementedException();
    public virtual Task<IReadOnlyList<ForcedFieldInfo>> GetForcedFieldsAsync(CancellationToken ct = default) => throw new NotImplementedException();
    public virtual Task<IReadOnlyList<StealthCandidate>> FindStealthMeterAsync(int max = 8, CancellationToken ct = default) => throw new NotImplementedException();
    public virtual Task<DataTableWalkResult> WalkDataTableRowsAsync(string addr, int offset = 0, int limit = 64, CancellationToken ct = default) => throw new NotImplementedException();
    public virtual Task<TeleportPose> TeleportGetPoseAsync(CancellationToken ct = default) => throw new NotImplementedException();
    public virtual Task<TeleportPose> TeleportSaveMarkerAsync(int slot, CancellationToken ct = default) => throw new NotImplementedException();
    public virtual Task<TeleportResult> TeleportRecallMarkerAsync(int slot, bool force, CancellationToken ct = default) => throw new NotImplementedException();
    public virtual Task<TeleportResult> TeleportRecallExplicitAsync(double x, double y, double z, double? pitch = null, double? yaw = null, double? roll = null, CancellationToken ct = default) => throw new NotImplementedException();
    public virtual Task<TeleportResult> TeleportToCursorAsync(double zOffset, int channel, bool fallbackCenter, CancellationToken ct = default) => throw new NotImplementedException();
    public virtual Task<List<TeleportMarker>> TeleportGetMarkersAsync(CancellationToken ct = default) => throw new NotImplementedException();
    public virtual Task<int> TeleportClearMarkerAsync(int slot, CancellationToken ct = default) => throw new NotImplementedException();
    public virtual Task<TeleportResult> TeleportRecallLastAsync(CancellationToken ct = default) => throw new NotImplementedException();
    public virtual Task<TeleportPov> TeleportGetPovAsync(CancellationToken ct = default) => throw new NotImplementedException();
    public virtual Task<TeleportPose> TeleportRelativeAsync(double distance, bool horizontal, CancellationToken ct = default) => throw new NotImplementedException();
    public virtual Task<int> SetMouseCursorAsync(bool show, CancellationToken ct = default) => throw new NotImplementedException();
    public virtual Task<int> GetMouseCursorAsync(CancellationToken ct = default) => throw new NotImplementedException();
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

        var csx = await CsxExportService.GenerateCsxAsync(_dump, "TestStruct", fields, ct: TestContext.Current.CancellationToken);

        Assert.Contains("Vartype=\"4 Bytes\"", csx);
        Assert.Contains("Bytesize=\"4\"", csx);
        Assert.Contains("Offset=\"288\"", csx); // 0x120 = 288
        Assert.Contains("OffsetHex=\"00000120\"", csx);
        Assert.Contains("Description=\"Health\"", csx);
        Assert.Contains("DisplayMethod=\"unsigned integer\"", csx);
    }

    [Fact]
    public async Task GenerateCsx_StrProperty_EmitsPointerWithUnicodeStringChild()
    {
        var fields = new List<LiveFieldValue>
        {
            new() { Name = "DisplayName", TypeName = "StrProperty", Offset = 0x10, Size = 16 }
        };

        var csx = await CsxExportService.GenerateCsxAsync(_dump, "TestStruct", fields, ct: TestContext.Current.CancellationToken);

        // FString (wchar_t*) → Pointer + wide "Unicode String" child element
        Assert.Contains("Vartype=\"Pointer\"", csx);
        Assert.Contains("Vartype=\"Unicode String\"", csx);
    }

    [Fact]
    public async Task GenerateCsx_Utf8StrProperty_EmitsPointerWithByteStringChild()
    {
        var fields = new List<LiveFieldValue>
        {
            new() { Name = "Utf8Name", TypeName = "Utf8StrProperty", Offset = 0x10, Size = 16 }
        };

        var csx = await CsxExportService.GenerateCsxAsync(_dump, "TestStruct", fields, ct: TestContext.Current.CancellationToken);

        // FUtf8String (1-byte) → Pointer + non-wide "String" child (CSX has no CodePage option)
        Assert.Contains("Vartype=\"Pointer\"", csx);
        Assert.Contains("Vartype=\"String\"", csx);
        Assert.DoesNotContain("Vartype=\"Unicode String\"", csx);
    }

    [Fact]
    public async Task GenerateCsx_AnsiStrProperty_EmitsPointerWithByteStringChild()
    {
        var fields = new List<LiveFieldValue>
        {
            new() { Name = "AnsiName", TypeName = "AnsiStrProperty", Offset = 0x10, Size = 16 }
        };

        var csx = await CsxExportService.GenerateCsxAsync(_dump, "TestStruct", fields, ct: TestContext.Current.CancellationToken);

        // FAnsiString (1-byte) → Pointer + non-wide "String" child
        Assert.Contains("Vartype=\"Pointer\"", csx);
        Assert.Contains("Vartype=\"String\"", csx);
        Assert.DoesNotContain("Vartype=\"Unicode String\"", csx);
    }

    [Fact]
    public async Task GenerateCsx_FloatProperty_EmitsFloat()
    {
        var fields = new List<LiveFieldValue>
        {
            new() { Name = "Speed", TypeName = "FloatProperty", Offset = 0x50, Size = 4 }
        };

        var csx = await CsxExportService.GenerateCsxAsync(_dump, "TestStruct", fields, ct: TestContext.Current.CancellationToken);

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

        var csx = await CsxExportService.GenerateCsxAsync(_dump, "TestStruct", fields, ct: TestContext.Current.CancellationToken);

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

        var csx = await CsxExportService.GenerateCsxAsync(_dump, "TestStruct", fields, ct: TestContext.Current.CancellationToken);

        Assert.Contains("Description=\"bIsVisible (bit 5, mask 0x20)\"", csx);
        Assert.Contains("Description=\"bIsLightingScenario (bit 0, mask 0x01)\"", csx);
        // Both at same offset
        Assert.Contains("OffsetHex=\"00000240\"", csx);
        // Default (Pre-7.7) must NOT emit a Binary bit-switch element
        Assert.DoesNotContain("Vartype=\"Binary\"", csx);
    }

    [Fact]
    public async Task GenerateCsx_BoolProperty_Ce77Plus_EmitsBinaryBitSwitch()
    {
        // Reproduce sample.CSX line 760 exactly: AActor::bCanBeDamaged at byte 0x5A=90, bit 2.
        var fields = new List<LiveFieldValue>
        {
            new() { Name = "bCanBeDamaged", TypeName = "BoolProperty", Offset = 0x5A, Size = 1,
                     BoolBitIndex = 2, BoolFieldMask = 0x04, BoolByteOffset = 0 },
        };

        var csx = await CsxExportService.GenerateCsxAsync(
            _dump, "TestStruct", fields, format: CsxFormat.Ce77Plus, ct: TestContext.Current.CancellationToken);

        // Byte-identical to the CE 7.7+ sample element (attribute order + values)
        Assert.Contains(
            "<Element Offset=\"90\" BitSize=\"1\" Vartype=\"Binary\" BitStart=\"2\" Bytesize=\"1\" OffsetHex=\"0000005A\" Description=\"bCanBeDamaged\" DisplayMethod=\"unsigned integer\"/>",
            csx);
        // The Pre-7.7 "(bit N, mask)" suffix and the Byte type must be gone in Binary mode
        Assert.DoesNotContain("(bit ", csx);
        Assert.DoesNotContain("Vartype=\"Byte\"", csx);
    }

    [Fact]
    public async Task GenerateCsx_BoolProperty_Ce77Plus_AddsBoolByteOffsetToAddress()
    {
        // A bit packed into a later byte of the property's storage: byte = Offset + ByteOffset.
        var fields = new List<LiveFieldValue>
        {
            new() { Name = "bPacked", TypeName = "BoolProperty", Offset = 0x10, Size = 1,
                     BoolBitIndex = 4, BoolFieldMask = 0x10, BoolByteOffset = 3 },
        };

        var csx = await CsxExportService.GenerateCsxAsync(
            _dump, "TestStruct", fields, format: CsxFormat.Ce77Plus, ct: TestContext.Current.CancellationToken);

        // byteOffset = 0x10 + 3 = 0x13 = 19 for BOTH decimal Offset and hex OffsetHex
        Assert.Contains("Offset=\"19\"", csx);
        Assert.Contains("OffsetHex=\"00000013\"", csx);
        Assert.Contains("BitStart=\"4\"", csx);
        Assert.Contains("Vartype=\"Binary\"", csx);
    }

    [Fact]
    public async Task GenerateCsx_BoolProperty_Ce77Plus_NestedStruct_UsesAbsoluteByteOffset()
    {
        // Regression guard: a bit-field bool inside a flattened StructProperty must use the
        // ABSOLUTE byte (parent.Offset + inner.Offset + BoolByteOffset), not the struct-relative
        // inner.Offset. Using field.Offset here would emit the wrong byte.
        _dump.RegisterStruct("0x1000", new InstanceWalkResult
        {
            Fields = new List<LiveFieldValue>
            {
                new() { Name = "bInnerFlag", TypeName = "BoolProperty", Offset = 0x4, Size = 1,
                         BoolBitIndex = 4, BoolFieldMask = 0x10, BoolByteOffset = 3 },
            }
        });

        var fields = new List<LiveFieldValue>
        {
            new() { Name = "State", TypeName = "StructProperty", Offset = 0x100, Size = 8,
                     StructTypeName = "FState", StructDataAddr = "0x1000", StructClassAddr = "0x2000" },
        };

        var csx = await CsxExportService.GenerateCsxAsync(
            _dump, "TestStruct", fields, format: CsxFormat.Ce77Plus, ct: TestContext.Current.CancellationToken);

        // Absolute byte = 0x100 + 0x4 + 3 = 0x107 = 263
        Assert.Contains("Offset=\"263\"", csx);
        Assert.Contains("OffsetHex=\"00000107\"", csx);
        Assert.Contains("BitStart=\"4\"", csx);
        Assert.Contains("Vartype=\"Binary\"", csx);
        Assert.Contains("Description=\"FState / bInnerFlag\"", csx);
    }

    [Fact]
    public async Task GenerateCsx_BoolProperty_Ce77Plus_NonBitfield_StaysByte()
    {
        // Whole-byte bool (BoolBitIndex == -1, mask 0xFF) is not a bit switch — stays Byte even in 7.7+.
        var fields = new List<LiveFieldValue>
        {
            new() { Name = "bWholeByte", TypeName = "BoolProperty", Offset = 0x20, Size = 1,
                     BoolBitIndex = -1, BoolFieldMask = 0xFF },
        };

        var csx = await CsxExportService.GenerateCsxAsync(
            _dump, "TestStruct", fields, format: CsxFormat.Ce77Plus, ct: TestContext.Current.CancellationToken);

        Assert.Contains("Vartype=\"Byte\"", csx);
        Assert.DoesNotContain("Vartype=\"Binary\"", csx);
    }

    [Fact]
    public async Task GenerateCsx_StrProperty_EmitsPointerWithUnicodeChild()
    {
        var fields = new List<LiveFieldValue>
        {
            new() { Name = "PlayerName", TypeName = "StrProperty", Offset = 0x30, Size = 8,
                     HexValue = "0000018AF21C3E20" }
        };

        var csx = await CsxExportService.GenerateCsxAsync(_dump, "TestStruct", fields, ct: TestContext.Current.CancellationToken);

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

        var csx = await CsxExportService.GenerateCsxAsync(_dump, "TestStruct", fields, ct: TestContext.Current.CancellationToken);

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

        var csx = await CsxExportService.GenerateCsxAsync(_dump, "TestStruct", fields, ct: TestContext.Current.CancellationToken);

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

        var csx = await CsxExportService.GenerateCsxAsync(_dump, "PlayerData_123", fields, ct: TestContext.Current.CancellationToken);

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

        var csx = await CsxExportService.GenerateCsxAsync(_dump, "TestStruct", fields, ct: TestContext.Current.CancellationToken);

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

        var csx = await CsxExportService.GenerateCsxAsync(_dump, "TestStruct", fields, ct: TestContext.Current.CancellationToken);

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

        var csx = await CsxExportService.GenerateCsxAsync(_dump, "TestStruct", fields, drilldownDepth: 0, ct: TestContext.Current.CancellationToken);

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

        var csx = await CsxExportService.GenerateCsxAsync(_dump, "TestStruct", fields, drilldownDepth: 1, ct: TestContext.Current.CancellationToken);

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

        var csx = await CsxExportService.GenerateCsxAsync(_dump, "TestStruct", fields, drilldownDepth: 1, ct: TestContext.Current.CancellationToken);

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

        var csx = await CsxExportService.GenerateCsxAsync(_dump, "TestStruct", fields, drilldownDepth: 1, ct: TestContext.Current.CancellationToken);

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

        var csx = await CsxExportService.GenerateCsxAsync(_dump, "TestStruct", fields, drilldownDepth: 1, ct: TestContext.Current.CancellationToken);

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

        var csx = await CsxExportService.GenerateCsxAsync(_dump, "TestStruct", fields, drilldownDepth: 2, ct: TestContext.Current.CancellationToken);

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
        var csx = await CsxExportService.GenerateCsxAsync(_dump, "TestStruct", fields, drilldownDepth: 3, ct: TestContext.Current.CancellationToken);

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

        var csx = await CsxExportService.GenerateCsxAsync(_dump, "PlayerData", fields, drilldownDepth: 1, ct: TestContext.Current.CancellationToken);

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

        var csx = await CsxExportService.GenerateCsxAsync(_dump, "PlayerData", fields, drilldownDepth: 2, ct: TestContext.Current.CancellationToken);

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

        var csx = await CsxExportService.GenerateCsxAsync(_dump, "TestStruct", fields, drilldownDepth: 1, ct: TestContext.Current.CancellationToken);

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

        var csx = await CsxExportService.GenerateCsxAsync(_dump, "TestStruct", fields, drilldownDepth: 0, ct: TestContext.Current.CancellationToken);

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

        var csx = await CsxExportService.GenerateCsxAsync(_dump, "TestStruct", fields, drilldownDepth: 1, ct: TestContext.Current.CancellationToken);

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

        var csx = await CsxExportService.GenerateCsxAsync(_dump, "TestStruct", fields, drilldownDepth: 1, ct: TestContext.Current.CancellationToken);

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

        var csx = await CsxExportService.GenerateCsxAsync(_dump, "TestStruct", fields, drilldownDepth: 1, ct: TestContext.Current.CancellationToken);

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

        var csx = await CsxExportService.GenerateCsxAsync(_dump, "TestStruct", fields, drilldownDepth: 2, ct: TestContext.Current.CancellationToken);

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

        var csx = await CsxExportService.GenerateCsxAsync(_dump, "TestStruct", fields, drilldownDepth: 1, ct: TestContext.Current.CancellationToken);

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

        var csx = await CsxExportService.GenerateCsxAsync(_dump, "TestStruct", fields, drilldownDepth: 2, ct: TestContext.Current.CancellationToken);

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

    // --- DataTable CSX tests ---

    [Fact]
    public async Task GenerateCsx_DataTableRows_DrilldownOne_ShowsRowsAsPointers()
    {
        // DataTable RowMap with 2 rows. Each row is a uint8* pointer to struct data.
        // With drilldown=1, container expands to show rows as ObjectProperty pointers.
        // stride=24, fnameSize=8:
        //   Row 0: offset = 0*24+8 = 8
        //   Row 1: offset = 1*24+8 = 32
        // Note: depth 1 expands the container but doesn't resolve row pointers (needs depth 2).

        var fields = new List<LiveFieldValue>
        {
            new() { Name = "RowMap", TypeName = "DataTableRows", Offset = 0xB0, Size = 8,
                     DataTableRowCount = 2, DataTableStructName = "RecipeRow",
                     DataTableFNameSize = 8, DataTableStride = 24,
                     DataTableRowStructAddr = "0xCLASS_RECIPE",
                     DataTableRowData = new List<DataTableRowInfo>
                     {
                         new() { SparseIndex = 0, RowName = "Recipe_Sword", DataAddr = "0x5000" },
                         new() { SparseIndex = 1, RowName = "Recipe_Shield", DataAddr = "0x6000" },
                     }
            }
        };

        var csx = await CsxExportService.GenerateCsxAsync(_dump, "DataTable_Recipes", fields, drilldownDepth: 1, ct: TestContext.Current.CancellationToken);

        // Child structure for the RowMap container
        Assert.Contains("Name=\"RowMap\"", csx);
        // Row entries with descriptive names
        Assert.Contains("Description=\"[0] Recipe_Sword\"", csx);
        Assert.Contains("Description=\"[1] Recipe_Shield\"", csx);

        // Offset calculation: sparseIndex * stride + fnameSize
        // Row 0: 0*24+8 = 8
        Assert.Contains("Offset=\"8\"", csx);
        // Row 1: 1*24+8 = 32
        Assert.Contains("Offset=\"32\"", csx);
    }

    [Fact]
    public async Task GenerateCsx_DataTableRows_DrilldownTwo_ResolvesRowFields()
    {
        // With drilldown=2, container expands (depth 1) AND row pointers resolve (depth 2).
        // Register row instance for drilldown resolution.
        _dump.RegisterStruct("0x5000", new InstanceWalkResult
        {
            ClassName = "RecipeRow",
            Fields = new List<LiveFieldValue>
            {
                new() { Name = "Damage", TypeName = "FloatProperty", Offset = 0, Size = 4 },
                new() { Name = "CraftTime", TypeName = "IntProperty", Offset = 4, Size = 4 },
            }
        });

        var fields = new List<LiveFieldValue>
        {
            new() { Name = "RowMap", TypeName = "DataTableRows", Offset = 0xB0, Size = 8,
                     DataTableRowCount = 1, DataTableStructName = "RecipeRow",
                     DataTableFNameSize = 8, DataTableStride = 24,
                     DataTableRowStructAddr = "0xCLASS_RECIPE",
                     DataTableRowData = new List<DataTableRowInfo>
                     {
                         new() { SparseIndex = 0, RowName = "Recipe_Sword", DataAddr = "0x5000" },
                     }
            }
        };

        var csx = await CsxExportService.GenerateCsxAsync(_dump, "DataTable_Recipes", fields, drilldownDepth: 2, ct: TestContext.Current.CancellationToken);

        // Resolved child structure for the row pointer target
        Assert.Contains("Name=\"RecipeRow\"", csx);
        Assert.Contains("Description=\"Damage\"", csx);
        Assert.Contains("Description=\"CraftTime\"", csx);
    }

    [Fact]
    public async Task GenerateCsx_DataTableRows_NoDrilldown_EmitsPointerNoExpansion()
    {
        // With drilldown=0, DataTable RowMap is emitted as a bare Pointer entry
        // without container expansion or child structures.
        var fields = new List<LiveFieldValue>
        {
            new() { Name = "RowMap", TypeName = "DataTableRows", Offset = 0xB0, Size = 8,
                     DataTableRowCount = 1, DataTableStructName = "ItemRow",
                     DataTableFNameSize = 8, DataTableStride = 24,
                     DataTableRowStructAddr = "0xCLASS_ITEM",
                     DataTableRowData = new List<DataTableRowInfo>
                     {
                         new() { SparseIndex = 0, RowName = "Item_Potion", DataAddr = "0x7000" },
                     }
            }
        };

        var csx = await CsxExportService.GenerateCsxAsync(_dump, "DataTable_Items", fields, drilldownDepth: 0, ct: TestContext.Current.CancellationToken);

        // Should emit as Pointer type
        Assert.Contains("Vartype=\"Pointer\"", csx);
        Assert.Contains("Description=\"RowMap\"", csx);
        // At depth 0, no container expansion — row names not visible
        Assert.DoesNotContain("Item_Potion", csx);
        Assert.DoesNotContain("Name=\"ItemRow\"", csx);
    }

    // --- Phase G/H/I: Soft/Lazy/Interface array drilldown tests ---

    [Fact]
    public async Task GenerateCsx_ArrayProperty_SoftObjectInner_DrilldownOne_ShowsAssetPaths()
    {
        // TArray<TSoftObjectPtr<UDataAsset>> — 0x28 stride, asset path display values
        var fields = new List<LiveFieldValue>
        {
            new() { Name = "AssetRefs", TypeName = "ArrayProperty", Offset = 0x100, Size = 16,
                     ArrayCount = 2, ArrayInnerType = "SoftObjectProperty", ArrayElemSize = 0x28,
                     ArrayDataAddr = "0x9000",
                     ArrayElements = new List<ArrayElementValue>
                     {
                         new() { Index = 0, Value = "/Game/Items/IT_Potion.IT_Potion" },
                         new() { Index = 1, Value = "/Game/Items/IT_Sword.IT_Sword" },
                     }
            }
        };

        var csx = await CsxExportService.GenerateCsxAsync(_dump, "TestStruct", fields, drilldownDepth: 1, ct: TestContext.Current.CancellationToken);

        // Child structure for the soft array
        Assert.Contains("Name=\"AssetRefs\"", csx);
        // Element offsets: index * 0x28 (40)
        Assert.Contains("Offset=\"0\"", csx);    // [0]: 0*40 = 0
        Assert.Contains("Offset=\"40\"", csx);   // [1]: 1*40 = 40
        // The Vartype="Pointer" in this output comes from the OUTER ArrayProperty
        // element, not from the soft leaves — so Contains("Pointer") alone passes
        // whatever the inner mapping is, and cannot fail if W9 is reverted. Audit #5
        // W9: TSoftObjectPtr holds an FSoftObjectPath, not an address, so its elements
        // must be watchable 8-byte hex leaves. Assert BOTH, mirroring the Delegate
        // sibling test below.
        Assert.Contains("Vartype=\"Pointer\"", csx);    // the ArrayProperty parent
        Assert.Contains("Vartype=\"8 Bytes\"", csx);    // the SoftObjectProperty leaves
    }

    [Fact]
    public async Task GenerateCsx_ArrayProperty_LazyObjectInner_DrilldownOne_ShowsGuids()
    {
        // TArray<TLazyObjectPtr<AActor>> — 0x20 stride, GUID display values
        var fields = new List<LiveFieldValue>
        {
            new() { Name = "LazyRefs", TypeName = "ArrayProperty", Offset = 0x40, Size = 16,
                     ArrayCount = 2, ArrayInnerType = "LazyObjectProperty", ArrayElemSize = 0x20,
                     ArrayDataAddr = "0xA000",
                     ArrayElements = new List<ArrayElementValue>
                     {
                         new() { Index = 0, Value = "{12345678-9ABCDEF0-AABBCCDD-EEFF0011}" },
                         new() { Index = 1, Value = "{00000000-00000000-00000000-00000000}" },
                     }
            }
        };

        var csx = await CsxExportService.GenerateCsxAsync(_dump, "TestStruct", fields, drilldownDepth: 1, ct: TestContext.Current.CancellationToken);

        Assert.Contains("Name=\"LazyRefs\"", csx);
        // Sequential offsets: index * 0x20 (32)
        Assert.Contains("Offset=\"0\"", csx);    // [0]: 0*32 = 0
        Assert.Contains("Offset=\"32\"", csx);   // [1]: 1*32 = 32
        // Same trap as the SoftObject sibling above: the Pointer comes from the OUTER
        // ArrayProperty element. Audit #5 W9 — TLazyObjectPtr's first 8 bytes are an
        // FWeakObjectPtr { int32 ObjectIndex; int32 SerialNumber }, not an address.
        Assert.Contains("Vartype=\"Pointer\"", csx);    // the ArrayProperty parent
        Assert.Contains("Vartype=\"8 Bytes\"", csx);    // the LazyObjectProperty leaves
    }

    [Fact]
    public async Task GenerateCsx_ArrayProperty_DelegateInner_DrilldownOne_ShowsTargets()
    {
        // TArray<FScriptDelegate> — each element binds a UObject* + FName.
        // Pointer-style conversion propagates PtrAddress so target drilldown works.
        var fields = new List<LiveFieldValue>
        {
            new() { Name = "Handlers", TypeName = "ArrayProperty", Offset = 0xA0, Size = 16,
                     ArrayCount = 2, ArrayInnerType = "DelegateProperty", ArrayElemSize = 16,
                     ArrayDataAddr = "0xC000",
                     ArrayElements = new List<ArrayElementValue>
                     {
                         new() { Index = 0, PtrAddress = "0xE01", PtrName = "PlayerActor",
                                 PtrClassName = "BP_Player_C" },
                         new() { Index = 1, PtrAddress = "0xE02", PtrName = "EnemyActor",
                                 PtrClassName = "BP_Enemy_C" },
                     }
            }
        };

        var csx = await CsxExportService.GenerateCsxAsync(_dump, "TestStruct", fields, drilldownDepth: 1, ct: TestContext.Current.CancellationToken);

        Assert.Contains("Name=\"Handlers\"", csx);
        // Resolved target names appear in element descriptions (pointer-style)
        Assert.Contains("Description=\"[0] PlayerActor\"", csx);
        Assert.Contains("Description=\"[1] EnemyActor\"", csx);
        // Sequential offsets: index * 16
        Assert.Contains("Offset=\"0\"", csx);
        Assert.Contains("Offset=\"16\"", csx);
        // DelegateProperty maps to 8 Bytes hex in CSX (Vartype="8 Bytes")
        Assert.Contains("Vartype=\"8 Bytes\"", csx);
    }

    [Fact]
    public async Task GenerateCsx_ArrayProperty_DelegateInnerCasePreserving_StrideIs20()
    {
        // With CasePreservingName, stride is 20 (8 + sizeof(FName) 12; FScriptDelegate is alignof 4)
        var fields = new List<LiveFieldValue>
        {
            new() { Name = "Handlers", TypeName = "ArrayProperty", Offset = 0x40, Size = 16,
                     ArrayCount = 2, ArrayInnerType = "DelegateProperty", ArrayElemSize = 20,
                     ArrayDataAddr = "0xD000",
                     ArrayElements = new List<ArrayElementValue>
                     {
                         new() { Index = 0, PtrAddress = "0xE10", PtrName = "Actor1", PtrClassName = "BP_A_C" },
                         new() { Index = 1, PtrAddress = "0xE11", PtrName = "Actor2", PtrClassName = "BP_A_C" },
                     }
            }
        };

        var csx = await CsxExportService.GenerateCsxAsync(_dump, "TestStruct", fields, drilldownDepth: 1, ct: TestContext.Current.CancellationToken);

        // Stride 20: [0] at 0, [1] at 20
        Assert.Contains("Offset=\"0\"", csx);
        Assert.Contains("Offset=\"20\"", csx);
    }

    [Fact]
    public async Task GenerateCsx_ArrayProperty_MulticastDelegateInner_DrilldownOne_ScalarStyle()
    {
        // TArray<FMulticastScriptDelegate> — scalar-style emission (no per-element pointer drill).
        // Display preview text appears in element name when short enough.
        var fields = new List<LiveFieldValue>
        {
            new() { Name = "Events", TypeName = "ArrayProperty", Offset = 0x60, Size = 16,
                     ArrayCount = 2, ArrayInnerType = "MulticastInlineDelegateProperty",
                     ArrayElemSize = 16,
                     ArrayDataAddr = "0xE000",
                     ArrayElements = new List<ArrayElementValue>
                     {
                         new() { Index = 0, Value = "(0 bindings)" },
                         new() { Index = 1, Value = "(1 binding)" },
                     }
            }
        };

        var csx = await CsxExportService.GenerateCsxAsync(_dump, "TestStruct", fields, drilldownDepth: 1, ct: TestContext.Current.CancellationToken);

        Assert.Contains("Name=\"Events\"", csx);
        // Sequential offsets: index * 16
        Assert.Contains("Offset=\"0\"", csx);
        Assert.Contains("Offset=\"16\"", csx);
        // MulticastInlineDelegateProperty maps to "Array of byte" in CSX
        Assert.Contains("Vartype=\"Array of byte\"", csx);
    }

    [Fact]
    public async Task GenerateCsx_ArrayProperty_InterfaceInner_DrilldownOne_ShowsPointerElements()
    {
        // TArray<TScriptInterface<I>> — 16-byte stride, UObject* drives drilldown
        var fields = new List<LiveFieldValue>
        {
            new() { Name = "DamageHandlers", TypeName = "ArrayProperty", Offset = 0x60, Size = 16,
                     ArrayCount = 2, ArrayInnerType = "InterfaceProperty", ArrayElemSize = 16,
                     ArrayDataAddr = "0xB000",
                     ArrayElements = new List<ArrayElementValue>
                     {
                         new() { Index = 0, PtrAddress = "0xD01", PtrName = "PlayerActor", PtrClassName = "BP_Player_C" },
                         new() { Index = 1, PtrAddress = "0xD02", PtrName = "Enemy_01", PtrClassName = "BP_Enemy_C" },
                     }
            }
        };

        var csx = await CsxExportService.GenerateCsxAsync(_dump, "TestStruct", fields, drilldownDepth: 1, ct: TestContext.Current.CancellationToken);

        Assert.Contains("Name=\"DamageHandlers\"", csx);
        // Resolved names appear in element descriptions
        Assert.Contains("Description=\"[0] PlayerActor\"", csx);
        Assert.Contains("Description=\"[1] Enemy_01\"", csx);
        // Sequential offsets: index * 16
        Assert.Contains("Offset=\"0\"", csx);    // [0]: 0*16 = 0
        Assert.Contains("Offset=\"16\"", csx);   // [1]: 1*16 = 16
        // InterfaceProperty maps to Pointer
        Assert.Contains("Vartype=\"Pointer\"", csx);
    }

    // --- Phase B: container element STRUCT value expansion ---

    [Fact]
    public async Task GenerateCsx_MapProperty_StructValue_DrilldownOne_FlattensValueFields()
    {
        // Map<Name, Struct> (the MissionInfoList shape) — value struct fields flatten inline
        // instead of staying a raw byte blob. stride = AlignUp(valOffset+valueSize, 4) + 8
        //   = AlignUp(8 + 16, 4) + 8 = 32; element value at index*32 + valOffset(8).
        //   [0] value @ 0*32+8 = 8  -> StructDataAddr = 0x10000 + 8  = 0x10008
        //   [1] value @ 1*32+8 = 40 -> StructDataAddr = 0x10000 + 40 = 0x10028
        var valueFields = new List<LiveFieldValue>
        {
            new() { Name = "Progress", TypeName = "IntProperty", Offset = 0, Size = 4 },
            new() { Name = "bDone", TypeName = "BoolProperty", Offset = 4, Size = 1 },
        };
        _dump.RegisterStruct("0x10008", new InstanceWalkResult { Fields = valueFields });
        _dump.RegisterStruct("0x10028", new InstanceWalkResult { Fields = valueFields });

        var fields = new List<LiveFieldValue>
        {
            new() { Name = "MissionInfoList", TypeName = "MapProperty", Offset = 0x40, Size = 8,
                     MapCount = 2, MapKeyType = "NameProperty", MapValueType = "StructProperty",
                     MapKeySize = 8, MapValueSize = 16, MapValueOffset = 8,
                     MapValueStructAddr = "0xMISSIONSTRUCT", MapValueStructType = "FMissionInfo",
                     MapDataAddr = "0x10000",
                     MapElements = new List<ContainerElementValue>
                     {
                         new() { Index = 0, Key = "Mission_01" },
                         new() { Index = 1, Key = "Mission_02" },
                     }
            }
        };

        var csx = await CsxExportService.GenerateCsxAsync(_dump, "SaveData", fields, drilldownDepth: 1, ct: TestContext.Current.CancellationToken);

        // Map child structure exists
        Assert.Contains("Name=\"MissionInfoList\"", csx);
        // Value struct sub-fields flatten with "[idx] key / SubField" naming
        Assert.Contains("Description=\"[0] Mission_01 / Progress\"", csx);
        Assert.Contains("Description=\"[0] Mission_01 / bDone\"", csx);
        Assert.Contains("Description=\"[1] Mission_02 / Progress\"", csx);
        // Offsets: element start (index*stride + valOffset) + sub.Offset
        Assert.Contains("Offset=\"8\"", csx);    // [0] Progress: 8 + 0
        Assert.Contains("Offset=\"40\"", csx);   // [1] Progress: 40 + 0
        Assert.Contains("Offset=\"44\"", csx);   // [1] bDone: 40 + 4
        // Proper type mapping for the flattened sub-fields
        Assert.Contains("Vartype=\"4 Bytes\"", csx);  // IntProperty
        Assert.Contains("Vartype=\"Byte\"", csx);     // BoolProperty
        // The value struct is NOT emitted as a raw byte blob
        Assert.DoesNotContain("Vartype=\"Array of byte\"", csx);
    }

    [Fact]
    public async Task GenerateCsx_SetProperty_StructElem_DrilldownOne_FlattensElementFields()
    {
        // Set<Struct> — element struct fields flatten inline (mirrors Map<…,Struct>).
        // stride = AlignUp(elemSize, 4) + 8 = AlignUp(12, 4) + 8 = 20; element @ index*20.
        //   [0] @ 0  -> StructDataAddr = 0x20000 + 0  = 0x20000
        //   [1] @ 20 -> StructDataAddr = 0x20000 + 20 = 0x20014
        var elemFields = new List<LiveFieldValue>
        {
            new() { Name = "Id", TypeName = "IntProperty", Offset = 0, Size = 4 },
            new() { Name = "Weight", TypeName = "FloatProperty", Offset = 4, Size = 4 },
        };
        _dump.RegisterStruct("0x20000", new InstanceWalkResult { Fields = elemFields });
        _dump.RegisterStruct("0x20014", new InstanceWalkResult { Fields = elemFields });

        var fields = new List<LiveFieldValue>
        {
            new() { Name = "TagSet", TypeName = "SetProperty", Offset = 0x60, Size = 8,
                     SetCount = 2, SetElemType = "StructProperty", SetElemSize = 12,
                     SetElemStructAddr = "0xTAGSTRUCT", SetElemStructType = "FTag",
                     SetDataAddr = "0x20000",
                     SetElements = new List<ContainerElementValue>
                     {
                         new() { Index = 0, Key = "Alpha" },
                         new() { Index = 1, Key = "Beta" },
                     }
            }
        };

        var csx = await CsxExportService.GenerateCsxAsync(_dump, "TestStruct", fields, drilldownDepth: 1, ct: TestContext.Current.CancellationToken);

        Assert.Contains("Name=\"TagSet\"", csx);
        Assert.Contains("Description=\"[0] Alpha / Id\"", csx);
        Assert.Contains("Description=\"[0] Alpha / Weight\"", csx);
        Assert.Contains("Description=\"[1] Beta / Id\"", csx);
        // Offsets: [1] Id = 20 + 0; [1] Weight = 20 + 4
        Assert.Contains("Offset=\"20\"", csx);
        Assert.Contains("Offset=\"24\"", csx);
        Assert.Contains("Vartype=\"Float\"", csx);   // FloatProperty
    }

    [Fact]
    public async Task GenerateCsx_MapProperty_StructValue_DepthZero_StaysFlatPointer()
    {
        // Depth-from-current-view: at D=0 a Map<…,Struct> does NOT expand — it stays a
        // bare Pointer, even though the value struct could be resolved.
        _dump.RegisterStruct("0x10008", new InstanceWalkResult
        {
            Fields = new List<LiveFieldValue>
            {
                new() { Name = "Progress", TypeName = "IntProperty", Offset = 0, Size = 4 },
            }
        });

        var fields = new List<LiveFieldValue>
        {
            new() { Name = "MissionInfoList", TypeName = "MapProperty", Offset = 0x40, Size = 8,
                     MapCount = 1, MapKeyType = "NameProperty", MapValueType = "StructProperty",
                     MapKeySize = 8, MapValueSize = 16, MapValueOffset = 8,
                     MapValueStructAddr = "0xMISSIONSTRUCT", MapValueStructType = "FMissionInfo",
                     MapDataAddr = "0x10000",
                     MapElements = new List<ContainerElementValue> { new() { Index = 0, Key = "Mission_01" } }
            }
        };

        var csx = await CsxExportService.GenerateCsxAsync(_dump, "SaveData", fields, drilldownDepth: 0, ct: TestContext.Current.CancellationToken);

        // No child structure, no flattened value fields — just the pointer leaf.
        Assert.DoesNotContain("Name=\"MissionInfoList\"", csx);
        Assert.DoesNotContain("Progress", csx);
        Assert.Contains("Description=\"MissionInfoList\"", csx);
        Assert.Contains("Vartype=\"Pointer\"", csx);
    }

    [Fact]
    public async Task GenerateCsx_MapValueStruct_NestedMap_DepthMeasuredFromCurrentView()
    {
        // Locks the "Drill Depth measured from the current view; each container level costs
        // one" semantics for a Map<Name, Struct> whose value struct itself holds a nested
        // Map<Name, Struct>. D=1 expands ONE level (outer value struct fields show, the inner
        // map stays a bare pointer); D=2 expands the inner map too.
        //
        // Outer: stride = AlignUp(8+16,4)+8 = 32; [0] value @ 8 -> 0x4008.
        // Inner (a field of the value struct): stride = AlignUp(8+8,4)+8 = 24; [0] value @ 8 -> 0x9008.
        _dump.RegisterStruct("0x4008", new InstanceWalkResult
        {
            Fields = new List<LiveFieldValue>
            {
                new() { Name = "Inner", TypeName = "MapProperty", Offset = 0, Size = 8,
                         MapCount = 1, MapKeyType = "NameProperty", MapValueType = "StructProperty",
                         MapKeySize = 8, MapValueSize = 8, MapValueOffset = 8,
                         MapValueStructAddr = "0xINNERSTRUCT", MapValueStructType = "FInner",
                         MapDataAddr = "0x9000",
                         MapElements = new List<ContainerElementValue> { new() { Index = 0, Key = "ik" } } },
            }
        });
        _dump.RegisterStruct("0x9008", new InstanceWalkResult
        {
            Fields = new List<LiveFieldValue>
            {
                new() { Name = "Leaf", TypeName = "IntProperty", Offset = 0, Size = 4 },
            }
        });

        var fields = new List<LiveFieldValue>
        {
            new() { Name = "Outer", TypeName = "MapProperty", Offset = 0x40, Size = 8,
                     MapCount = 1, MapKeyType = "NameProperty", MapValueType = "StructProperty",
                     MapKeySize = 8, MapValueSize = 16, MapValueOffset = 8,
                     MapValueStructAddr = "0xOUTERSTRUCT", MapValueStructType = "FOuter",
                     MapDataAddr = "0x4000",
                     MapElements = new List<ContainerElementValue> { new() { Index = 0, Key = "ok" } }
            }
        };

        // D=1: outer value struct flattens (Inner field appears), but the inner map does NOT
        // expand — no "Inner" child structure, no Leaf.
        var csx1 = await CsxExportService.GenerateCsxAsync(_dump, "Save", fields, drilldownDepth: 1, ct: TestContext.Current.CancellationToken);
        Assert.Contains("Name=\"Outer\"", csx1);
        Assert.Contains("Description=\"[0] ok / Inner\"", csx1);  // value struct flattened one level
        Assert.DoesNotContain("Name=\"Inner\"", csx1);            // inner map NOT expanded at D=1
        Assert.DoesNotContain("Leaf", csx1);

        // D=2: the inner map expands too — "Inner" child structure with the Leaf field.
        var csx2 = await CsxExportService.GenerateCsxAsync(_dump, "Save", fields, drilldownDepth: 2, ct: TestContext.Current.CancellationToken);
        Assert.Contains("Name=\"Inner\"", csx2);
        Assert.Contains("Leaf", csx2);
    }

    // ========================================
    // GenerateCsxAsync cancellation (Export CSX abort)
    // ========================================

    /// <summary>A StubDumpService whose WalkInstanceAsync always throws — proves the CSX
    /// resolver propagates a cancellation (abort) but still swallows ordinary pipe/target
    /// failures (leaf fallback).</summary>
    private sealed class ThrowingWalkStub : StubDumpService
    {
        private readonly Func<Exception> _make;
        public ThrowingWalkStub(Func<Exception> make) => _make = make;
        public override Task<InstanceWalkResult> WalkInstanceAsync(string addr, string? classAddr = null,
            int arrayLimit = 64, int previewLimit = 2, bool fillGaps = false, bool lean = false,
            CancellationToken ct = default)
            => throw _make();
    }

    [Fact]
    public async Task GenerateCsx_CancelledToken_ThrowsOperationCanceled()
    {
        // A struct field forces a WalkInstance during ResolveDrilldown; a token cancelled
        // up front aborts at the resolver's entry guard.
        _dump.RegisterStruct("0xDATA", new InstanceWalkResult
        {
            Fields = new List<LiveFieldValue> { new() { Name = "Leaf", TypeName = "IntProperty", Offset = 0, Size = 4 } }
        });
        var fields = new List<LiveFieldValue>
        {
            new() { Name = "S", TypeName = "StructProperty", Offset = 0,
                    StructClassAddr = "0xCLS", StructDataAddr = "0xDATA" },
        };
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            CsxExportService.GenerateCsxAsync(_dump, "TestStruct", fields, drilldownDepth: 1, ct: cts.Token));
    }

    [Fact]
    public async Task GenerateCsx_PointerWalkCancelled_Propagates()
    {
        // A cancel surfacing mid-walk (OCE from a pointer WalkInstance) must NOT be eaten
        // by the CSX ResolvePointerInstancesAsync pipe-error catch — it aborts the export.
        var dump = new ThrowingWalkStub(() => new OperationCanceledException());
        var fields = new List<LiveFieldValue>
        {
            new() { Name = "Ptr", TypeName = "ObjectProperty", Offset = 0, PtrAddress = "0xAAA" },
        };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            CsxExportService.GenerateCsxAsync(dump, "TestStruct", fields, drilldownDepth: 1,
                ct: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GenerateCsx_OrdinaryWalkError_StillSwallowed()
    {
        // Positive control: a normal pipe/target failure (not a cancel) is tolerated — the
        // pointer just gets no child structure and the export completes without throwing.
        var dump = new ThrowingWalkStub(() => new InvalidOperationException("pipe boom"));
        var fields = new List<LiveFieldValue>
        {
            new() { Name = "Ptr", TypeName = "ObjectProperty", Offset = 0, PtrAddress = "0xAAA" },
        };

        var csx = await CsxExportService.GenerateCsxAsync(dump, "TestStruct", fields, drilldownDepth: 1,
            ct: TestContext.Current.CancellationToken);

        Assert.Contains("<Structures>", csx);   // produced output despite the walk failure
    }

    // ============================================================
    // Audit #5 W9 — the CSX twin of W5's CE-XML guard.
    //
    // W5 stopped the CE-XML emitter dereferencing slots that hold no address; the same
    // defect lived in CSX (Vartype="Pointer" + a child <Structure>) and that commit did
    // not touch this file. These two theories are deliberately PAIRED: the negative one
    // pins the four weak-like types as watchable hex leaves, the positive one pins the
    // three real pointer slots as still drillable, so a predicate tightened too far
    // fails just as loudly as one left too broad. Mirrors
    // CeXmlExportServiceTests.DrillDown_NonPointerSlot_IsNotDereferenced / _RealPointerSlot_.
    //
    // The pre-existing array tests above could not do this job: their
    // Assert.Contains("Vartype=\"Pointer\"") is satisfied by the ArrayProperty PARENT,
    // so they passed identically before and after W9.
    // ============================================================
    private static async Task<string> DrillCsxFor(string typeName)
    {
        var dump = new StubDumpService();
        dump.RegisterStruct("0xB100", new InstanceWalkResult
        {
            Fields = new List<LiveFieldValue>
            {
                new() { Name = "Health", TypeName = "FloatProperty", Offset = 0x10, Size = 4 },
            }
        });

        var fields = new List<LiveFieldValue>
        {
            new() { Name = "Target", TypeName = typeName, Offset = 0x28, Size = 8,
                    PtrAddress = "0xB100", PtrName = "SomeActor", PtrClassName = "BP_Actor_C" },
        };

        return await CsxExportService.GenerateCsxAsync(
            dump, "TestStruct", fields, drilldownDepth: 1, ct: TestContext.Current.CancellationToken);
    }

    [Theory]
    [InlineData("WeakObjectProperty")]   // FWeakObjectPtr { int32 ObjectIndex; int32 SerialNumber }
    [InlineData("SoftObjectProperty")]   // FSoftObjectPath
    [InlineData("SoftClassProperty")]    // FSoftObjectPath
    [InlineData("LazyObjectProperty")]   // FWeakObjectPtr at +0, FGuid after it
    public async Task Csx_DrillDown_NonPointerSlot_IsNotDereferenced(string typeName)
    {
        var csx = await DrillCsxFor(typeName);

        // The resolved child must NOT appear: emitting it under a Vartype=Pointer
        // element is what told CE to dereference a slot that holds no address.
        Assert.DoesNotContain("Health", csx);
        Assert.DoesNotContain("Vartype=\"Pointer\"", csx);
        // ...and the field itself must still be present as a watchable hex leaf.
        // CSX carries the field name in Description, not Name (Name is the Structure).
        Assert.Contains("Target", csx);
        Assert.Contains("Vartype=\"8 Bytes\"", csx);
        Assert.Contains("DisplayMethod=\"hexadecimal\"", csx);
    }

    [Theory]
    [InlineData("ObjectProperty")]
    [InlineData("ClassProperty")]
    // FScriptInterface is { UObject* +0x00; void* +0x08 }, so its first 8 bytes ARE an
    // object pointer and the drill has always been correct for it. Pinned so a future
    // tightening of the predicate cannot silently drop a working case.
    [InlineData("InterfaceProperty")]
    public async Task Csx_DrillDown_RealPointerSlot_StillDereferences(string typeName)
    {
        var csx = await DrillCsxFor(typeName);

        Assert.Contains("Health", csx);
        Assert.Contains("Vartype=\"Pointer\"", csx);
    }
}
