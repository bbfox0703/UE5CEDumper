using System.IO;
using System.Text.Json.Nodes;
using UE5DumpUI.Core;
using UE5DumpUI.Models;
using UE5DumpUI.Services;
using UE5DumpUI.ViewModels;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// Tests for the Value Search feature (build 733+, port from
/// discrete Phase 27b shape):
///
/// - <see cref="DumpService"/>: JSON wire round-trips for the three
///   value-scan commands (begin / refine / end). Locks the field name
///   mapping so a DLL rename of a JSON key fails here at build time
///   rather than at user-runtime.
/// - <see cref="ValueSearchViewModel"/>: First-Scan / Next-Scan /
///   New-Scan workflow with a fake dump service. Verifies the
///   First-Scan-only / Prev-Value-only contract enforcement.
/// - Banner contract: the Native-C++-fields-unreachable banner in
///   ValueSearchPanel.axaml is locked in by literal-text assertion.
///   This is a project-memory UX rule (project_value_search_caveats).
/// </summary>
public class ValueSearchTests
{
    // ------------------------------------------------------------------
    // Service-level: DumpService → wire JSON → parsed model
    // ------------------------------------------------------------------

    private static DumpService MakeService(out MockPipeClient pipe)
    {
        pipe = new MockPipeClient();
        return new DumpService(pipe, new MockLoggingService());
    }

    [Fact]
    public async Task BeginValueScanAsync_BuildsCorrectRequest()
    {
        var svc = MakeService(out var pipe);
        JsonObject? captured = null;
        pipe.SetHandler(req =>
        {
            captured = (JsonObject)req.DeepClone();
            return new JsonObject
            {
                ["id"]              = req["id"]?.GetValue<int>() ?? 0,
                ["ok"]              = true,
                ["session_id"]      = 7UL,
                ["data_type"]       = "Int32",
                ["total"]           = 0,
                ["scanned_classes"] = 0,
                ["scanned_objects"] = 0,
                ["duration_ms"]     = 12L,
                ["deadline_hit"]    = false,
                ["candidates"]      = new JsonArray(),
            };
        });

        var res = await svc.BeginValueScanAsync(
            ValueScanDataType.Int32, ValueScanType.Between,
            value: "10", value2: "20",
            gameOnly: true, maxResults: 1234,
            ct: TestContext.Current.CancellationToken);

        Assert.NotNull(captured);
        Assert.Equal("begin_value_scan", captured!["cmd"]?.GetValue<string>());
        Assert.Equal("Int32",            captured["data_type"]?.GetValue<string>());
        Assert.Equal("Between",          captured["scan_type"]?.GetValue<string>());
        Assert.Equal("10",               captured["value"]?.GetValue<string>());
        Assert.Equal("20",               captured["value2"]?.GetValue<string>());
        Assert.Equal(true,               captured["game_only"]?.GetValue<bool>());
        Assert.Equal(1234,               captured["max_results"]?.GetValue<int>());
        // parallel defaults to true (the DLL default) → omitted to keep the wire tight.
        Assert.False(captured.ContainsKey("parallel"),
            "parallel must be omitted when left at its default (true)");

        Assert.Equal(7UL, res.SessionId);
        Assert.Equal("Int32", res.DataType);
        Assert.Equal(12L, res.DurationMs);
    }

    [Fact]
    public async Task BeginValueScanAsync_OmitsValue2WhenNotBetween()
    {
        var svc = MakeService(out var pipe);
        JsonObject? captured = null;
        pipe.SetHandler(req =>
        {
            captured = (JsonObject)req.DeepClone();
            return new JsonObject
            {
                ["id"]              = req["id"]?.GetValue<int>() ?? 0,
                ["ok"]              = true,
                ["session_id"]      = 1UL,
                ["data_type"]       = "Float",
                ["total"]           = 0,
                ["scanned_classes"] = 0,
                ["scanned_objects"] = 0,
                ["duration_ms"]     = 1L,
                ["deadline_hit"]    = false,
                ["candidates"]      = new JsonArray(),
            };
        });

        await svc.BeginValueScanAsync(
            ValueScanDataType.Float, ValueScanType.Exact,
            value: "3.14", value2: null,
            ct: TestContext.Current.CancellationToken);

        Assert.NotNull(captured);
        Assert.False(captured!.ContainsKey("value2"),
            "value2 must not be sent for non-Between scans");
    }

    [Fact]
    public async Task BeginValueScanAsync_AttachesParallelFalseWhenDisabled()
    {
        var svc = MakeService(out var pipe);
        JsonObject? captured = null;
        pipe.SetHandler(req =>
        {
            captured = (JsonObject)req.DeepClone();
            return new JsonObject
            {
                ["id"]              = req["id"]?.GetValue<int>() ?? 0,
                ["ok"]              = true,
                ["session_id"]      = 2UL,
                ["data_type"]       = "Int32",
                ["total"]           = 0,
                ["scanned_classes"] = 0,
                ["scanned_objects"] = 0,
                ["duration_ms"]     = 1L,
                ["deadline_hit"]    = false,
                ["candidates"]      = new JsonArray(),
            };
        });

        await svc.BeginValueScanAsync(
            ValueScanDataType.Int32, ValueScanType.Exact,
            value: "42", parallel: false,
            ct: TestContext.Current.CancellationToken);

        Assert.NotNull(captured);
        Assert.True(captured!.ContainsKey("parallel"),
            "parallel must be sent when the user disables it");
        Assert.False(captured["parallel"]?.GetValue<bool>(),
            "parallel:false forces a single-threaded DLL scan");
    }

    [Fact]
    public async Task BeginValueScanAsync_AttachesBatchReadFalseWhenDisabled()
    {
        var svc = MakeService(out var pipe);
        JsonObject? captured = null;
        pipe.SetHandler(req =>
        {
            captured = (JsonObject)req.DeepClone();
            return new JsonObject
            {
                ["id"]              = req["id"]?.GetValue<int>() ?? 0,
                ["ok"]              = true,
                ["session_id"]      = 3UL,
                ["data_type"]       = "Int32",
                ["total"]           = 0,
                ["scanned_classes"] = 0,
                ["scanned_objects"] = 0,
                ["duration_ms"]     = 1L,
                ["deadline_hit"]    = false,
                ["candidates"]      = new JsonArray(),
            };
        });

        // Default (batchRead:true) → omitted.
        await svc.BeginValueScanAsync(
            ValueScanDataType.Int32, ValueScanType.Exact, value: "1",
            ct: TestContext.Current.CancellationToken);
        Assert.False(captured!.ContainsKey("batch_read"),
            "batch_read must be omitted when left at its default (true)");

        // Disabled → batch_read:false on the wire.
        await svc.BeginValueScanAsync(
            ValueScanDataType.Int32, ValueScanType.Exact, value: "1", batchRead: false,
            ct: TestContext.Current.CancellationToken);
        Assert.True(captured!.ContainsKey("batch_read"));
        Assert.False(captured["batch_read"]?.GetValue<bool>(),
            "batch_read:false forces one SEH read per field");
    }

    [Fact]
    public async Task BeginValueScanAsync_ParsesCandidates()
    {
        var svc = MakeService(out var pipe);
        pipe.SetHandler(req => new JsonObject
        {
            ["id"]              = req["id"]?.GetValue<int>() ?? 0,
            ["ok"]              = true,
            ["session_id"]      = 42UL,
            ["data_type"]       = "Int32",
            ["total"]           = 1,
            ["scanned_classes"] = 100,
            ["scanned_objects"] = 1000,
            ["duration_ms"]     = 50L,
            ["deadline_hit"]    = false,
            ["candidates"]      = new JsonArray
            {
                new JsonObject
                {
                    ["addr"]                = "0x7FF601234560",
                    ["instance_addr"]       = "0x7FF601234540",
                    ["instance_index"]      = 12345,
                    ["field_offset"]        = 0x20,
                    ["instance_name"]       = "PlayerPawn_0",
                    ["class_name"]          = "BP_Player_C",
                    ["defining_class_name"] = "ACharacter",
                    ["field_name"]          = "Health",
                    ["field_type"]          = "FloatProperty",
                    ["bool_field_mask"]     = 255,
                    ["value"]               = "100",
                },
            },
        });

        var res = await svc.BeginValueScanAsync(
            ValueScanDataType.Int32, ValueScanType.Exact, "100",
            ct: TestContext.Current.CancellationToken);

        Assert.Single(res.Candidates);
        var c = res.Candidates[0];
        Assert.Equal("0x7FF601234560", c.Addr);
        Assert.Equal("PlayerPawn_0",   c.InstanceName);
        Assert.Equal("BP_Player_C",    c.ClassName);
        Assert.Equal("ACharacter",     c.DefiningClassName);
        Assert.Equal("Health",         c.FieldName);
        Assert.Equal(0x20,             c.FieldOffset);
        Assert.Equal("100",            c.Value);
        Assert.Equal("0x20",           c.OffsetHex);

        // LocationLabel surfaces inheritance when defining differs.
        Assert.Equal("BP_Player_C.Health  (ACharacter)", c.LocationLabel);
    }

    [Fact]
    public async Task RefineValueScanAsync_OmitsValueForPrevScanType()
    {
        var svc = MakeService(out var pipe);
        JsonObject? captured = null;
        pipe.SetHandler(req =>
        {
            captured = (JsonObject)req.DeepClone();
            return new JsonObject
            {
                ["id"]            = req["id"]?.GetValue<int>() ?? 0,
                ["ok"]            = true,
                ["session_id"]    = 1UL,
                ["data_type"]     = "Int32",
                ["scan_type"]     = "Changed",
                ["total"]         = 0,
                ["duration_ms"]   = 1L,
                ["candidates"]    = new JsonArray(),
            };
        });

        await svc.RefineValueScanAsync(
            sessionId: 1UL, scanType: ValueScanType.Changed,
            value: null, value2: null,
            ct: TestContext.Current.CancellationToken);

        Assert.NotNull(captured);
        Assert.Equal("refine_value_scan", captured!["cmd"]?.GetValue<string>());
        Assert.Equal(1UL,                 captured["session_id"]?.GetValue<ulong>());
        Assert.Equal("Changed",           captured["scan_type"]?.GetValue<string>());
        Assert.False(captured.ContainsKey("value"));
        Assert.False(captured.ContainsKey("value2"));
    }

    [Fact]
    public async Task BeginValueScanAsync_AttachesRoundingModeWhenNotRound()
    {
        var svc = MakeService(out var pipe);
        JsonObject? captured = null;
        pipe.SetHandler(req =>
        {
            captured = (JsonObject)req.DeepClone();
            return new JsonObject
            {
                ["id"]              = req["id"]?.GetValue<int>() ?? 0,
                ["ok"]              = true,
                ["session_id"]      = 1UL,
                ["data_type"]       = "Float",
                ["total"]           = 0,
                ["scanned_classes"] = 0,
                ["scanned_objects"] = 0,
                ["duration_ms"]     = 1L,
                ["deadline_hit"]    = false,
                ["candidates"]      = new JsonArray(),
            };
        });

        await svc.BeginValueScanAsync(
            ValueScanDataType.Float, ValueScanType.Exact, "338",
            roundMode: FloatRoundMode.Trunc,
            ct: TestContext.Current.CancellationToken);

        Assert.NotNull(captured);
        Assert.True(captured!.ContainsKey("rounding_mode"));
        Assert.Equal("Trunc", captured["rounding_mode"]?.GetValue<string>());
        // The old tolerance field is gone from the wire entirely.
        Assert.False(captured.ContainsKey("tolerance"));
    }

    [Fact]
    public async Task BeginValueScanAsync_OmitsRoundingModeWhenRound()
    {
        // Round is the DLL default → omitted from the wire so existing
        // exact-scan call sites stay byte-identical.
        var svc = MakeService(out var pipe);
        JsonObject? captured = null;
        pipe.SetHandler(req =>
        {
            captured = (JsonObject)req.DeepClone();
            return new JsonObject
            {
                ["id"]              = req["id"]?.GetValue<int>() ?? 0,
                ["ok"]              = true,
                ["session_id"]      = 1UL,
                ["data_type"]       = "Float",
                ["total"]           = 0,
                ["scanned_classes"] = 0,
                ["scanned_objects"] = 0,
                ["duration_ms"]     = 1L,
                ["deadline_hit"]    = false,
                ["candidates"]      = new JsonArray(),
            };
        });

        await svc.BeginValueScanAsync(
            ValueScanDataType.Float, ValueScanType.Exact, "100",
            roundMode: FloatRoundMode.Round,
            ct: TestContext.Current.CancellationToken);

        Assert.NotNull(captured);
        Assert.False(captured!.ContainsKey("rounding_mode"));
        Assert.False(captured.ContainsKey("tolerance"));
    }

    [Fact]
    public async Task RefineValueScanAsync_AttachesRoundingModeWhenNotRound()
    {
        var svc = MakeService(out var pipe);
        JsonObject? captured = null;
        pipe.SetHandler(req =>
        {
            captured = (JsonObject)req.DeepClone();
            return new JsonObject
            {
                ["id"]          = req["id"]?.GetValue<int>() ?? 0,
                ["ok"]          = true,
                ["session_id"]  = 1UL,
                ["data_type"]   = "Float",
                ["scan_type"]   = "Decreased",
                ["total"]       = 0,
                ["duration_ms"] = 1L,
                ["candidates"]  = new JsonArray(),
            };
        });

        await svc.RefineValueScanAsync(
            1UL, ValueScanType.Decreased,
            value: null, value2: null,
            roundMode: FloatRoundMode.Ceil,
            ct: TestContext.Current.CancellationToken);

        Assert.NotNull(captured);
        Assert.Equal("Ceil", captured!["rounding_mode"]?.GetValue<string>());
        Assert.False(captured.ContainsKey("tolerance"));
    }

    [Fact]
    public void ViewModel_SupportsRoundingMode_GatesByDataType()
    {
        // The rounding picker shows for every numeric + vector type; hidden
        // only for Bool and the 3 string types.
        var (vm, _) = MakeVm();
        vm.SelectedDataType = ValueScanDataType.Int32;
        Assert.True(vm.SupportsRoundingMode);
        vm.SelectedDataType = ValueScanDataType.Float;
        Assert.True(vm.SupportsRoundingMode);
        vm.SelectedDataType = ValueScanDataType.Double;
        Assert.True(vm.SupportsRoundingMode);
        vm.SelectedDataType = ValueScanDataType.Bool;
        Assert.False(vm.SupportsRoundingMode);
        vm.SelectedDataType = ValueScanDataType.FString;
        Assert.False(vm.SupportsRoundingMode);
    }

    [Fact]
    public async Task ViewModel_RoundingModePassesThroughForFloat()
    {
        var (vm, fake) = MakeVm();
        fake.NextBeginResult = new ValueScanBeginResult { SessionId = 1UL };
        vm.SelectedDataType = ValueScanDataType.Float;
        vm.SelectedScanType = ValueScanType.Exact;
        vm.Value = "338";
        vm.SelectedRoundingMode = FloatRoundMode.Trunc;

        await vm.FirstScanCommand.ExecuteAsync(null);

        Assert.Single(fake.Begins);
        var (_, _, _, _, _, _, mode, _) = fake.Begins[0];
        Assert.Equal(FloatRoundMode.Trunc, mode);
    }

    [Fact]
    public async Task ViewModel_RoundingModePassesThroughForIntegerType()
    {
        // The picker is shown for integers too (a fractional target gets
        // coerced via the mode), so the VM threads it through unchanged.
        var (vm, fake) = MakeVm();
        fake.NextBeginResult = new ValueScanBeginResult { SessionId = 1UL };
        vm.SelectedDataType = ValueScanDataType.Int32;
        vm.SelectedScanType = ValueScanType.Exact;
        vm.Value = "100";
        vm.SelectedRoundingMode = FloatRoundMode.Ceil;

        await vm.FirstScanCommand.ExecuteAsync(null);

        Assert.Single(fake.Begins);
        var (_, _, _, _, _, _, mode, _) = fake.Begins[0];
        Assert.Equal(FloatRoundMode.Ceil, mode);
    }

    [Fact]
    public async Task ViewModel_RoundingModeForcedRoundForStringType()
    {
        // A string scan hides the picker (SupportsRoundingMode=false), so the
        // VM forces Round to the service regardless of the stored mode.
        var (vm, fake) = MakeVm();
        fake.NextBeginResult = new ValueScanBeginResult { SessionId = 1UL };
        vm.SelectedRoundingMode = FloatRoundMode.Ceil;
        vm.SelectedDataType = ValueScanDataType.FString;
        vm.SelectedScanType = ValueScanType.Exact;
        vm.Value = "abc";

        await vm.FirstScanCommand.ExecuteAsync(null);

        Assert.Single(fake.Begins);
        var (_, _, _, _, _, _, mode, _) = fake.Begins[0];
        Assert.Equal(FloatRoundMode.Round, mode);
    }

    [Fact]
    public async Task ViewModel_ScanTimeout_ThreadsDeadlineMsToService()
    {
        // The Timeout slider (seconds) must reach the DLL as deadline_ms = seconds*1000
        // on both First Scan (single) and Group First Scan.
        var (vm, fake) = MakeVm();
        fake.NextBeginResult = new ValueScanBeginResult { SessionId = 1UL };
        vm.SelectedDataType = ValueScanDataType.Int32;
        vm.SelectedScanType = ValueScanType.Exact;
        vm.Value = "100";

        // Default 25s → 25000ms.
        await vm.FirstScanCommand.ExecuteAsync(null);
        Assert.Equal(25000, fake.LastDeadlineMs);

        // Slider moved to 45s → 45000ms.
        vm.ScanTimeoutSeconds = 45;
        await vm.FirstScanCommand.ExecuteAsync(null);
        Assert.Equal(45000, fake.LastDeadlineMs);

        // Group First Scan threads the same value.
        fake.NextGroupBeginResult = new GroupScanBeginResult { SessionId = 2UL };
        vm.IsGroupMode = true;
        vm.GroupInputs[0].Value = "1";
        vm.GroupInputs[1].Value = "2";
        await vm.GroupFirstScanCommand.ExecuteAsync(null);
        Assert.Equal(45000, fake.LastGroupDeadlineMs);
    }

    [Fact]
    public void ViewModel_ScanTimeout_ClampsToBand()
    {
        var (vm, _) = MakeVm();
        vm.ScanTimeoutSeconds = 3;    // below floor
        Assert.Equal(10, vm.ScanTimeoutSeconds);
        vm.ScanTimeoutSeconds = 999;  // above ceiling
        Assert.Equal(90, vm.ScanTimeoutSeconds);
        vm.ScanTimeoutSeconds = 30;   // in band
        Assert.Equal(30, vm.ScanTimeoutSeconds);
    }

    [Fact]
    public async Task EndValueScanAsync_SendsSessionId()
    {
        var svc = MakeService(out var pipe);
        JsonObject? captured = null;
        pipe.SetHandler(req =>
        {
            captured = (JsonObject)req.DeepClone();
            return new JsonObject
            {
                ["id"]         = req["id"]?.GetValue<int>() ?? 0,
                ["ok"]         = true,
                ["session_id"] = 99UL,
                ["ended"]      = true,
            };
        });

        await svc.EndValueScanAsync(99UL, TestContext.Current.CancellationToken);

        Assert.NotNull(captured);
        Assert.Equal("end_value_scan", captured!["cmd"]?.GetValue<string>());
        Assert.Equal(99UL,             captured["session_id"]?.GetValue<ulong>());
    }

    // ------------------------------------------------------------------
    // Scan-type partition predicates (mirror DLL-side contract)
    // ------------------------------------------------------------------

    [Theory]
    [InlineData(ValueScanType.Exact,      true,  false)]
    [InlineData(ValueScanType.Bigger,     true,  false)]
    [InlineData(ValueScanType.Smaller,    true,  false)]
    [InlineData(ValueScanType.Between,    true,  false)]
    [InlineData(ValueScanType.Changed,    false, true)]
    [InlineData(ValueScanType.Unchanged,  false, true)]
    [InlineData(ValueScanType.Increased,  false, true)]
    [InlineData(ValueScanType.Decreased,  false, true)]
    // Phase 2A: substring predicates are first-scan eligible (used as
    // narrowing predicates on the user's needle, like Exact).
    [InlineData(ValueScanType.Contains,   true,  false)]
    [InlineData(ValueScanType.StartsWith, true,  false)]
    [InlineData(ValueScanType.EndsWith,   true,  false)]
    public void ScanType_Partition_IsExhaustiveAndDisjoint(
        ValueScanType st, bool expectFirst, bool expectPrev)
    {
        Assert.Equal(expectFirst, ValueSearchViewModel.IsFirstScanType(st));
        Assert.Equal(expectPrev,  ValueSearchViewModel.IsPrevValueScanType(st));
        Assert.NotEqual(ValueSearchViewModel.IsFirstScanType(st),
                        ValueSearchViewModel.IsPrevValueScanType(st));
    }

    // ------------------------------------------------------------------
    // Phase 2: IsScanTypeValidFor partition (mirror of DLL contract).
    // String types: substring + Exact + Changed/Unchanged accept;
    //               numeric ordering predicates reject.
    // Vector / numeric types: substring predicates reject; ordering
    //                         predicates accept.
    // ------------------------------------------------------------------
    [Theory]
    // Numeric type accepts everything except substring predicates.
    [InlineData(ValueScanDataType.Int32, ValueScanType.Exact,      true)]
    [InlineData(ValueScanDataType.Int32, ValueScanType.Bigger,     true)]
    [InlineData(ValueScanDataType.Int32, ValueScanType.Smaller,    true)]
    [InlineData(ValueScanDataType.Int32, ValueScanType.Between,    true)]
    [InlineData(ValueScanDataType.Int32, ValueScanType.Changed,    true)]
    [InlineData(ValueScanDataType.Int32, ValueScanType.Increased,  true)]
    [InlineData(ValueScanDataType.Int32, ValueScanType.Contains,   false)]
    [InlineData(ValueScanDataType.Int32, ValueScanType.StartsWith, false)]
    [InlineData(ValueScanDataType.Int32, ValueScanType.EndsWith,   false)]
    [InlineData(ValueScanDataType.Float, ValueScanType.Contains,   false)]
    // String types: substring + Exact + Changed/Unchanged accept;
    // ordering rejects.
    [InlineData(ValueScanDataType.FString, ValueScanType.Exact,      true)]
    [InlineData(ValueScanDataType.FString, ValueScanType.Contains,   true)]
    [InlineData(ValueScanDataType.FString, ValueScanType.StartsWith, true)]
    [InlineData(ValueScanDataType.FString, ValueScanType.EndsWith,   true)]
    [InlineData(ValueScanDataType.FString, ValueScanType.Changed,    true)]
    [InlineData(ValueScanDataType.FString, ValueScanType.Unchanged,  true)]
    [InlineData(ValueScanDataType.FString, ValueScanType.Bigger,     false)]
    [InlineData(ValueScanDataType.FString, ValueScanType.Smaller,    false)]
    [InlineData(ValueScanDataType.FString, ValueScanType.Between,    false)]
    [InlineData(ValueScanDataType.FString, ValueScanType.Increased,  false)]
    [InlineData(ValueScanDataType.FString, ValueScanType.Decreased,  false)]
    [InlineData(ValueScanDataType.FName,   ValueScanType.Contains,   true)]
    [InlineData(ValueScanDataType.FName,   ValueScanType.Bigger,     false)]
    [InlineData(ValueScanDataType.FText,   ValueScanType.StartsWith, true)]
    // Vector types: ordering predicates accept; substring rejects.
    [InlineData(ValueScanDataType.FVector,  ValueScanType.Exact,      true)]
    [InlineData(ValueScanDataType.FVector,  ValueScanType.Bigger,     true)]
    [InlineData(ValueScanDataType.FVector,  ValueScanType.Between,    true)]
    [InlineData(ValueScanDataType.FVector,  ValueScanType.Changed,    true)]
    [InlineData(ValueScanDataType.FVector,  ValueScanType.Contains,   false)]
    [InlineData(ValueScanDataType.FRotator, ValueScanType.Smaller,    true)]
    [InlineData(ValueScanDataType.FRotator, ValueScanType.EndsWith,   false)]
    public void IsScanTypeValidFor_PartitionsCorrectlyPerDataType(
        ValueScanDataType dt, ValueScanType st, bool expected)
    {
        Assert.Equal(expected, ValueSearchViewModel.IsScanTypeValidFor(dt, st));
    }

    [Fact]
    public void IsStringDataType_OnlyMatchesStringFamily()
    {
        Assert.True(ValueSearchViewModel.IsStringDataType(ValueScanDataType.FString));
        Assert.True(ValueSearchViewModel.IsStringDataType(ValueScanDataType.FName));
        Assert.True(ValueSearchViewModel.IsStringDataType(ValueScanDataType.FText));
        Assert.False(ValueSearchViewModel.IsStringDataType(ValueScanDataType.Int32));
        Assert.False(ValueSearchViewModel.IsStringDataType(ValueScanDataType.FVector));
    }

    [Fact]
    public void IsVectorDataType_OnlyMatchesVectorFamily()
    {
        Assert.True(ValueSearchViewModel.IsVectorDataType(ValueScanDataType.FVector));
        Assert.True(ValueSearchViewModel.IsVectorDataType(ValueScanDataType.FRotator));
        Assert.True(ValueSearchViewModel.IsVectorDataType(ValueScanDataType.FTransform));
        Assert.False(ValueSearchViewModel.IsVectorDataType(ValueScanDataType.Int32));
        Assert.False(ValueSearchViewModel.IsVectorDataType(ValueScanDataType.FString));
    }

    [Theory]
    // Numeric DataTypes: dropdown excludes substring predicates.
    [InlineData(ValueScanDataType.Int32,   8 /* Exact..Decreased */)]
    [InlineData(ValueScanDataType.Float,   8)]
    // String DataTypes: 6 predicates (Exact, Contains, StartsWith,
    // EndsWith, Changed, Unchanged).
    [InlineData(ValueScanDataType.FString, 6)]
    [InlineData(ValueScanDataType.FName,   6)]
    [InlineData(ValueScanDataType.FText,   6)]
    // Vector DataTypes: same 8 as numerics.
    [InlineData(ValueScanDataType.FVector, 8)]
    [InlineData(ValueScanDataType.FRotator,8)]
    public void VisibleScanTypeOptions_ReflectsDataType(ValueScanDataType dt, int expectedCount)
    {
        var (vm, _) = MakeVm();
        vm.SelectedDataType = dt;
        Assert.Equal(expectedCount, vm.VisibleScanTypeOptions.Count);
    }

    [Fact]
    public void SelectedScanType_ResetsToExact_WhenSwitchingToIncompatibleDataType()
    {
        // User starts with Int32 + Bigger, then switches to FString.
        // Bigger is invalid for FString -> the VM must snap to Exact
        // so the dropdown stays in a consistent state.
        var (vm, _) = MakeVm();
        vm.SelectedDataType = ValueScanDataType.Int32;
        vm.SelectedScanType = ValueScanType.Bigger;
        vm.SelectedDataType = ValueScanDataType.FString;
        Assert.Equal(ValueScanType.Exact, vm.SelectedScanType);
    }

    [Fact]
    public void SupportsCaseSensitive_OnlyForStringTypes()
    {
        var (vm, _) = MakeVm();
        vm.SelectedDataType = ValueScanDataType.Int32;
        Assert.False(vm.SupportsCaseSensitive);
        vm.SelectedDataType = ValueScanDataType.FString;
        Assert.True(vm.SupportsCaseSensitive);
        vm.SelectedDataType = ValueScanDataType.FName;
        Assert.True(vm.SupportsCaseSensitive);
        vm.SelectedDataType = ValueScanDataType.FText;
        Assert.True(vm.SupportsCaseSensitive);
        vm.SelectedDataType = ValueScanDataType.FVector;
        Assert.False(vm.SupportsCaseSensitive);
    }

    [Fact]
    public void SupportsRoundingMode_AlsoForVectorTypes()
    {
        // The rounding picker is shown for Float/Double + Vector/Rotator/Transform,
        // and hidden for the string types.
        var (vm, _) = MakeVm();
        vm.SelectedDataType = ValueScanDataType.FVector;
        Assert.True(vm.SupportsRoundingMode);
        vm.SelectedDataType = ValueScanDataType.FRotator;
        Assert.True(vm.SupportsRoundingMode);
        vm.SelectedDataType = ValueScanDataType.FTransform;
        Assert.True(vm.SupportsRoundingMode);
        vm.SelectedDataType = ValueScanDataType.FString;
        Assert.False(vm.SupportsRoundingMode);
    }

    // ------------------------------------------------------------------
    // build 794 — multi-numeric (NumericNoByte) meta type
    // ------------------------------------------------------------------

    [Fact]
    public void NumericNoByte_IsOfferedInDropdown()
    {
        var (vm, _) = MakeVm();
        Assert.Contains(ValueScanDataType.NumericNoByte, vm.DataTypeOptions);
    }

    [Fact]
    public void NumericNoByte_IsNeitherStringNorVector()
    {
        // The meta type must classify as a plain numeric so the existing
        // numeric scan-type + (no) case-sensitive gating applies.
        Assert.False(ValueSearchViewModel.IsStringDataType(ValueScanDataType.NumericNoByte));
        Assert.False(ValueSearchViewModel.IsVectorDataType(ValueScanDataType.NumericNoByte));
    }

    [Fact]
    public void NumericNoByte_SupportsRoundingMode_ButNotCaseSensitive()
    {
        // Rounding is meaningful (float/double members); case-sensitive
        // is string-only so it must stay off.
        var (vm, _) = MakeVm();
        vm.SelectedDataType = ValueScanDataType.NumericNoByte;
        Assert.True(vm.SupportsRoundingMode);
        Assert.False(vm.SupportsCaseSensitive);
    }

    [Theory]
    // Behaves like a numeric: ordering predicates accept, substring reject.
    [InlineData(ValueScanType.Exact,      true)]
    [InlineData(ValueScanType.Bigger,     true)]
    [InlineData(ValueScanType.Smaller,    true)]
    [InlineData(ValueScanType.Between,    true)]
    [InlineData(ValueScanType.Changed,    true)]
    [InlineData(ValueScanType.Increased,  true)]
    [InlineData(ValueScanType.Contains,   false)]
    [InlineData(ValueScanType.StartsWith, false)]
    [InlineData(ValueScanType.EndsWith,   false)]
    public void NumericNoByte_ScanTypeValidity_MirrorsNumeric(ValueScanType st, bool expected)
    {
        Assert.Equal(expected,
            ValueSearchViewModel.IsScanTypeValidFor(ValueScanDataType.NumericNoByte, st));
    }

    [Fact]
    public void NumericNoByte_VisibleScanTypes_ExcludeSubstring()
    {
        // Same 8 ordering predicates as a single numeric type.
        var (vm, _) = MakeVm();
        vm.SelectedDataType = ValueScanDataType.NumericNoByte;
        Assert.Equal(8, vm.VisibleScanTypeOptions.Count);
        Assert.DoesNotContain(ValueScanType.Contains, vm.VisibleScanTypeOptions);
    }

    [Fact]
    public async Task BeginValueScanAsync_SendsNumericNoByteWireName_AndAttachesRoundingMode()
    {
        var svc = MakeService(out var pipe);
        JsonObject? captured = null;
        pipe.SetHandler(req =>
        {
            captured = (JsonObject)req.DeepClone();
            return new JsonObject
            {
                ["id"]              = req["id"]?.GetValue<int>() ?? 0,
                ["ok"]              = true,
                ["session_id"]      = 5UL,
                ["data_type"]       = "NumericNoByte",
                ["total"]           = 0,
                ["scanned_classes"] = 0,
                ["scanned_objects"] = 0,
                ["duration_ms"]     = 1L,
                ["deadline_hit"]    = false,
                ["candidates"]      = new JsonArray(),
            };
        });

        await svc.BeginValueScanAsync(
            ValueScanDataType.NumericNoByte, ValueScanType.Exact, "100",
            roundMode: FloatRoundMode.Trunc,
            ct: TestContext.Current.CancellationToken);

        Assert.NotNull(captured);
        Assert.Equal("NumericNoByte", captured!["data_type"]?.GetValue<string>());
        // Rounding mode rides along (it applies to the float/double members).
        Assert.True(captured.ContainsKey("rounding_mode"));
        Assert.Equal("Trunc", captured["rounding_mode"]?.GetValue<string>());
    }

    [Fact]
    public async Task BeginValueScanAsync_OmitsCaseSensitiveForNumericNoByte()
    {
        var svc = MakeService(out var pipe);
        JsonObject? captured = null;
        pipe.SetHandler(req =>
        {
            captured = (JsonObject)req.DeepClone();
            return new JsonObject
            {
                ["id"]              = req["id"]?.GetValue<int>() ?? 0,
                ["ok"]              = true,
                ["session_id"]      = 1UL,
                ["data_type"]       = "NumericNoByte",
                ["total"]           = 0,
                ["scanned_classes"] = 0,
                ["scanned_objects"] = 0,
                ["duration_ms"]     = 1L,
                ["deadline_hit"]    = false,
                ["candidates"]      = new JsonArray(),
            };
        });

        await svc.BeginValueScanAsync(
            ValueScanDataType.NumericNoByte, ValueScanType.Exact, "100",
            caseSensitive: true,   // user set it, but type isn't a string
            ct: TestContext.Current.CancellationToken);

        Assert.NotNull(captured);
        Assert.False(captured!.ContainsKey("case_sensitive"));
    }

    // ------------------------------------------------------------------
    // build 796 — multi-numeric with-byte variant (NumericAll) + warning
    // ------------------------------------------------------------------

    [Fact]
    public void NumericAll_IsOfferedInDropdown_AndClassifiedMultiNumeric()
    {
        var (vm, _) = MakeVm();
        Assert.Contains(ValueScanDataType.NumericAll, vm.DataTypeOptions);
        Assert.True(ValueSearchViewModel.IsMultiNumericDataType(ValueScanDataType.NumericAll));
        Assert.True(ValueSearchViewModel.IsMultiNumericDataType(ValueScanDataType.NumericNoByte));
        Assert.False(ValueSearchViewModel.IsMultiNumericDataType(ValueScanDataType.Int32));
        // Still a plain numeric for scan-type / case gating purposes.
        Assert.False(ValueSearchViewModel.IsStringDataType(ValueScanDataType.NumericAll));
        Assert.False(ValueSearchViewModel.IsVectorDataType(ValueScanDataType.NumericAll));
    }

    [Fact]
    public void NumericAll_SupportsRoundingMode_ButNotCaseSensitive()
    {
        var (vm, _) = MakeVm();
        vm.SelectedDataType = ValueScanDataType.NumericAll;
        Assert.True(vm.SupportsRoundingMode);
        Assert.False(vm.SupportsCaseSensitive);
    }

    [Fact]
    public void DataTypeWarning_OnlyShownForNumericAll()
    {
        // The result-volume caution fires for NumericAll (1-byte fields
        // flood on small values) and is empty for everything else.
        var (vm, _) = MakeVm();
        vm.SelectedDataType = ValueScanDataType.NumericAll;
        Assert.NotEmpty(vm.DataTypeWarning);
        Assert.Contains("1-byte", vm.DataTypeWarning);

        vm.SelectedDataType = ValueScanDataType.NumericNoByte;
        Assert.Empty(vm.DataTypeWarning);
        vm.SelectedDataType = ValueScanDataType.Int32;
        Assert.Empty(vm.DataTypeWarning);
        vm.SelectedDataType = ValueScanDataType.Float;
        Assert.Empty(vm.DataTypeWarning);
    }

    [Fact]
    public void DataTypeWarning_RaisesPropertyChanged_OnDataTypeSwitch()
    {
        var (vm, _) = MakeVm();
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);
        vm.SelectedDataType = ValueScanDataType.NumericAll;
        Assert.Contains(nameof(vm.DataTypeWarning), raised);
    }

    [Theory]
    [InlineData(ValueScanType.Exact,    true)]
    [InlineData(ValueScanType.Bigger,   true)]
    [InlineData(ValueScanType.Between,  true)]
    [InlineData(ValueScanType.Decreased,true)]
    [InlineData(ValueScanType.Contains, false)]
    public void NumericAll_ScanTypeValidity_MirrorsNumeric(ValueScanType st, bool expected)
    {
        Assert.Equal(expected,
            ValueSearchViewModel.IsScanTypeValidFor(ValueScanDataType.NumericAll, st));
    }

    [Fact]
    public async Task BeginValueScanAsync_SendsNumericAllWireName_AndAttachesRoundingMode()
    {
        var svc = MakeService(out var pipe);
        JsonObject? captured = null;
        pipe.SetHandler(req =>
        {
            captured = (JsonObject)req.DeepClone();
            return new JsonObject
            {
                ["id"]              = req["id"]?.GetValue<int>() ?? 0,
                ["ok"]              = true,
                ["session_id"]      = 8UL,
                ["data_type"]       = "NumericAll",
                ["total"]           = 0,
                ["scanned_classes"] = 0,
                ["scanned_objects"] = 0,
                ["duration_ms"]     = 1L,
                ["deadline_hit"]    = false,
                ["candidates"]      = new JsonArray(),
            };
        });

        await svc.BeginValueScanAsync(
            ValueScanDataType.NumericAll, ValueScanType.Exact, "100",
            roundMode: FloatRoundMode.Ceil,
            ct: TestContext.Current.CancellationToken);

        Assert.NotNull(captured);
        Assert.Equal("NumericAll", captured!["data_type"]?.GetValue<string>());
        Assert.True(captured.ContainsKey("rounding_mode"));
        Assert.Equal("Ceil", captured["rounding_mode"]?.GetValue<string>());
    }

    // ------------------------------------------------------------------
    // Phase 2 wire-shape locks for DumpService
    // ------------------------------------------------------------------

    [Fact]
    public async Task BeginValueScanAsync_AttachesCaseSensitiveForFString()
    {
        var svc = MakeService(out var pipe);
        JsonObject? captured = null;
        pipe.SetHandler(req =>
        {
            captured = (JsonObject)req.DeepClone();
            return new JsonObject
            {
                ["id"]              = req["id"]?.GetValue<int>() ?? 0,
                ["ok"]              = true,
                ["session_id"]      = 1UL,
                ["data_type"]       = "FString",
                ["total"]           = 0,
                ["scanned_classes"] = 0,
                ["scanned_objects"] = 0,
                ["duration_ms"]     = 1L,
                ["deadline_hit"]    = false,
                ["candidates"]      = new JsonArray(),
            };
        });

        await svc.BeginValueScanAsync(
            ValueScanDataType.FString, ValueScanType.Contains, "Player",
            caseSensitive: true,
            ct: TestContext.Current.CancellationToken);

        Assert.NotNull(captured);
        Assert.Equal("FString",   captured!["data_type"]?.GetValue<string>());
        Assert.Equal("Contains",  captured["scan_type"]?.GetValue<string>());
        Assert.Equal("Player",    captured["value"]?.GetValue<string>());
        Assert.True(captured.ContainsKey("case_sensitive"));
        Assert.True(captured["case_sensitive"]?.GetValue<bool>());
    }

    [Fact]
    public async Task BeginValueScanAsync_OmitsCaseSensitiveWhenFalse()
    {
        var svc = MakeService(out var pipe);
        JsonObject? captured = null;
        pipe.SetHandler(req =>
        {
            captured = (JsonObject)req.DeepClone();
            return new JsonObject
            {
                ["id"]              = req["id"]?.GetValue<int>() ?? 0,
                ["ok"]              = true,
                ["session_id"]      = 1UL,
                ["data_type"]       = "FString",
                ["total"]           = 0,
                ["scanned_classes"] = 0,
                ["scanned_objects"] = 0,
                ["duration_ms"]     = 1L,
                ["deadline_hit"]    = false,
                ["candidates"]      = new JsonArray(),
            };
        });

        // CE-style default is case-insensitive -- the wire should omit
        // the flag entirely so non-string sessions stay byte-identical
        // to the pre-Phase-2 wire shape.
        await svc.BeginValueScanAsync(
            ValueScanDataType.FString, ValueScanType.Exact, "Player",
            caseSensitive: false,
            ct: TestContext.Current.CancellationToken);

        Assert.NotNull(captured);
        Assert.False(captured!.ContainsKey("case_sensitive"));
    }

    [Theory]
    [InlineData(ValueScanDataType.Int32)]
    [InlineData(ValueScanDataType.Float)]
    [InlineData(ValueScanDataType.FVector)]
    public async Task BeginValueScanAsync_OmitsCaseSensitiveForNonStringTypes(ValueScanDataType dt)
    {
        // Even when the caller explicitly passes caseSensitive=true,
        // non-string DataTypes must NOT carry the flag on the wire --
        // the DLL ignores it for those sessions and omitting keeps the
        // wire shape minimal.
        var svc = MakeService(out var pipe);
        JsonObject? captured = null;
        pipe.SetHandler(req =>
        {
            captured = (JsonObject)req.DeepClone();
            return new JsonObject
            {
                ["id"]              = req["id"]?.GetValue<int>() ?? 0,
                ["ok"]              = true,
                ["session_id"]      = 1UL,
                ["data_type"]       = dt.ToString(),
                ["total"]           = 0,
                ["scanned_classes"] = 0,
                ["scanned_objects"] = 0,
                ["duration_ms"]     = 1L,
                ["deadline_hit"]    = false,
                ["candidates"]      = new JsonArray(),
            };
        });

        await svc.BeginValueScanAsync(
            dt, ValueScanType.Exact,
            dt == ValueScanDataType.FVector ? "0,0,0" : "0",
            caseSensitive: true,
            ct: TestContext.Current.CancellationToken);

        Assert.NotNull(captured);
        Assert.False(captured!.ContainsKey("case_sensitive"),
            $"case_sensitive must not appear on the wire for {dt}");
    }

    [Theory]
    [InlineData(ValueScanDataType.FVector)]
    [InlineData(ValueScanDataType.FRotator)]
    [InlineData(ValueScanDataType.FTransform)]
    public async Task BeginValueScanAsync_AttachesRoundingModeForVectorTypes(ValueScanDataType dt)
    {
        var svc = MakeService(out var pipe);
        JsonObject? captured = null;
        pipe.SetHandler(req =>
        {
            captured = (JsonObject)req.DeepClone();
            return new JsonObject
            {
                ["id"]              = req["id"]?.GetValue<int>() ?? 0,
                ["ok"]              = true,
                ["session_id"]      = 1UL,
                ["data_type"]       = dt.ToString(),
                ["total"]           = 0,
                ["scanned_classes"] = 0,
                ["scanned_objects"] = 0,
                ["duration_ms"]     = 1L,
                ["deadline_hit"]    = false,
                ["candidates"]      = new JsonArray(),
            };
        });

        await svc.BeginValueScanAsync(
            dt, ValueScanType.Exact, "100,200,300",
            roundMode: FloatRoundMode.Trunc,
            ct: TestContext.Current.CancellationToken);

        Assert.NotNull(captured);
        Assert.Equal("Trunc", captured!["rounding_mode"]?.GetValue<string>());
        Assert.False(captured.ContainsKey("tolerance"));
    }

    [Theory]
    [InlineData(ValueScanDataType.FString)]
    [InlineData(ValueScanDataType.FName)]
    [InlineData(ValueScanDataType.FText)]
    public async Task BeginValueScanAsync_NeverSendsTolerance(ValueScanDataType dt)
    {
        // The tolerance wire field is gone entirely. String types never carry it
        // (the VM forces Round for them anyway), and neither does any other type.
        var svc = MakeService(out var pipe);
        JsonObject? captured = null;
        pipe.SetHandler(req =>
        {
            captured = (JsonObject)req.DeepClone();
            return new JsonObject
            {
                ["id"]              = req["id"]?.GetValue<int>() ?? 0,
                ["ok"]              = true,
                ["session_id"]      = 1UL,
                ["data_type"]       = dt.ToString(),
                ["total"]           = 0,
                ["scanned_classes"] = 0,
                ["scanned_objects"] = 0,
                ["duration_ms"]     = 1L,
                ["deadline_hit"]    = false,
                ["candidates"]      = new JsonArray(),
            };
        });

        await svc.BeginValueScanAsync(
            dt, ValueScanType.Contains, "Player",
            ct: TestContext.Current.CancellationToken);

        Assert.NotNull(captured);
        Assert.False(captured!.ContainsKey("tolerance"),
            $"tolerance must not appear on the wire for {dt}");
    }

    [Fact]
    public async Task ViewModel_CaseSensitive_PassesThroughForFString()
    {
        var (vm, fake) = MakeVm();
        fake.NextBeginResult = new ValueScanBeginResult { SessionId = 1UL };
        vm.SelectedDataType = ValueScanDataType.FString;
        vm.SelectedScanType = ValueScanType.Contains;
        vm.Value = "Health";
        vm.CaseSensitive = true;

        await vm.FirstScanCommand.ExecuteAsync(null);

        Assert.Single(fake.Begins);
        var (_, _, _, _, _, _, _, cs) = fake.Begins[0];
        Assert.True(cs);
    }

    [Fact]
    public async Task ViewModel_CaseSensitive_IgnoredForNonStringTypes()
    {
        // The VM applies SupportsCaseSensitive gating before pushing
        // to the service -- even with CaseSensitive=true the
        // non-string scan must see false.
        var (vm, fake) = MakeVm();
        fake.NextBeginResult = new ValueScanBeginResult { SessionId = 1UL };
        vm.SelectedDataType = ValueScanDataType.Int32;
        vm.SelectedScanType = ValueScanType.Exact;
        vm.Value = "100";
        vm.CaseSensitive = true;   // user set it, but type is Int32

        await vm.FirstScanCommand.ExecuteAsync(null);

        Assert.Single(fake.Begins);
        var (_, _, _, _, _, _, _, cs) = fake.Begins[0];
        Assert.False(cs);
    }

    [Fact]
    public async Task ViewModel_ParallelScan_DefaultsTrue_AndPassesThrough()
    {
        var (vm, fake) = MakeVm();
        fake.NextBeginResult = new ValueScanBeginResult { SessionId = 1UL };
        vm.SelectedDataType = ValueScanDataType.Int32;
        vm.SelectedScanType = ValueScanType.Exact;
        vm.Value = "100";

        // Default is ON.
        Assert.True(vm.ParallelScan);
        await vm.FirstScanCommand.ExecuteAsync(null);
        Assert.Equal(true, fake.LastParallel);

        // Turning it off forces a single-threaded DLL scan.
        vm.ParallelScan = false;
        await vm.FirstScanCommand.ExecuteAsync(null);
        Assert.Equal(false, fake.LastParallel);
    }

    [Fact]
    public async Task ViewModel_BatchRead_DefaultsTrue_AndPassesThrough()
    {
        var (vm, fake) = MakeVm();
        fake.NextBeginResult = new ValueScanBeginResult { SessionId = 1UL };
        vm.SelectedDataType = ValueScanDataType.Int32;
        vm.SelectedScanType = ValueScanType.Exact;
        vm.Value = "100";

        Assert.True(vm.BatchRead);                    // default ON
        await vm.FirstScanCommand.ExecuteAsync(null);
        Assert.Equal(true, fake.LastBatchRead);

        vm.BatchRead = false;                         // force per-field reads
        await vm.FirstScanCommand.ExecuteAsync(null);
        Assert.Equal(false, fake.LastBatchRead);
    }

    [Fact]
    public async Task FirstScan_RejectsIncompatibleScanTypeForDataType()
    {
        // FString + Bigger is a legal-individually pair but illegal in
        // combination. The VM must catch it before hitting the DLL so
        // the user gets a clean error.
        var (vm, fake) = MakeVm();
        vm.SelectedDataType = ValueScanDataType.FString;
        // Bigger isn't in VisibleScanTypeOptions, but a misbehaving
        // caller could set it directly. Verify the FirstScan guard.
        vm.SelectedScanType = ValueScanType.Bigger;
        vm.Value = "anything";

        await vm.FirstScanCommand.ExecuteAsync(null);

        Assert.False(vm.HasSession);
        Assert.Empty(fake.Begins);
        Assert.Contains("not valid for", vm.ErrorMessage);
    }

    // ------------------------------------------------------------------
    // ViewModel-level: a fake IDumpService records calls and feeds
    // pre-baked results so we can verify the workflow contract.
    // ------------------------------------------------------------------

    private sealed class FakeDumpService : StubDumpService
    {
        public ValueScanBeginResult NextBeginResult { get; set; } = new();
        public ValueScanRefineResult NextRefineResult { get; set; } = new();
        public ValueScanWindowResult NextWindowResult { get; set; } = new();
        // (dataType, scanType, value, value2, gameOnly, maxResults, roundMode, caseSensitive)
        public List<(ValueScanDataType, ValueScanType, string, string?, bool, int, FloatRoundMode, bool)> Begins { get; } = new();
        // (sessionId, scanType, value, value2, roundMode, caseSensitive)
        public List<(ulong, ValueScanType, string?, string?, FloatRoundMode, bool)> Refines { get; } = new();
        // (sessionId, offset, limit, filter, sortKey, sortDesc)
        public List<(ulong, int, int, string?, string?, bool)> Queries { get; } = new();
        public List<ulong> Ends { get; } = new();

        // AE8: how many times DiagnosticsProbe reached the pipe. StubDumpService throws
        // NotImplementedException here and the probe swallows it, so counting is the only
        // way to see whether the probe was opened at all.
        public int DiagnosticsCalls { get; private set; }
        public override Task<DiagnosticsResult> GetDiagnosticsAsync(int limit = 25, CancellationToken ct = default)
        {
            DiagnosticsCalls++;
            return Task.FromResult(new DiagnosticsResult());
        }

        public bool? LastParallel { get; private set; }
        public bool? LastBatchRead { get; private set; }

        public bool? LastDeep { get; private set; }
        public bool? LastNativeC { get; private set; }
        public bool? LastNewestFirst { get; private set; }
        public int? LastDeadlineMs { get; private set; }
        public bool? LastAutoSkipNoise { get; private set; }

        public override Task<ValueScanBeginResult> BeginValueScanAsync(
            ValueScanDataType dataType, ValueScanType scanType,
            string value, string? value2 = null, bool gameOnly = true,
            int maxResults = 50000, FloatRoundMode roundMode = FloatRoundMode.Round,
            bool caseSensitive = false, bool parallel = true, bool batchRead = true,
            bool deep = false, bool nativeC = false, bool newestFirst = false,
            int pageSize = 1000, int deadlineMs = 15000, bool autoSkipNoise = false,
            CancellationToken ct = default)
        {
            Begins.Add((dataType, scanType, value, value2, gameOnly, maxResults, roundMode, caseSensitive));
            LastParallel = parallel;
            LastBatchRead = batchRead;
            LastDeep = deep;
            LastNativeC = nativeC;
            LastNewestFirst = newestFirst;
            LastDeadlineMs = deadlineMs;
            LastAutoSkipNoise = autoSkipNoise;
            return Task.FromResult(NextBeginResult);
        }

        public override Task<ValueScanRefineResult> RefineValueScanAsync(
            ulong sessionId, ValueScanType scanType,
            string? value = null, string? value2 = null,
            FloatRoundMode roundMode = FloatRoundMode.Round,
            bool caseSensitive = false, int pageSize = 1000,
            CancellationToken ct = default)
        {
            Refines.Add((sessionId, scanType, value, value2, roundMode, caseSensitive));
            return Task.FromResult(NextRefineResult);
        }

        /// <summary>Most recent exclude_classes passed to QueryCandidatesAsync
        /// (null when the caller passed none) — lets tests assert the class
        /// filter threads its excluded set into the server window query.</summary>
        public IReadOnlyList<string>? LastQueryExclude { get; private set; }

        public override Task<ValueScanWindowResult> QueryCandidatesAsync(
            ulong sessionId, int offset, int limit,
            string? filter = null, string? sortKey = null, bool sortDesc = false,
            IReadOnlyList<string>? excludeClasses = null,
            CancellationToken ct = default)
        {
            Queries.Add((sessionId, offset, limit, filter, sortKey, sortDesc));
            LastQueryExclude = excludeClasses;
            return Task.FromResult(NextWindowResult);
        }

        public override Task EndValueScanAsync(ulong sessionId, CancellationToken ct = default)
        {
            Ends.Add(sessionId);
            return Task.CompletedTask;
        }

        // --- group scan ---
        public GroupScanBeginResult NextGroupBeginResult { get; set; } = new();
        public GroupScanRefineResult NextGroupRefineResult { get; set; } = new();
        public GroupScanWindowResult NextGroupWindowResult { get; set; } = new();
        public List<(List<GroupSlotInput> slots, bool gameOnly, int maxResults, bool deep, bool crossObject, bool nativeC, bool newestFirst)> GroupBegins { get; } = new();
        public List<(ulong sessionId, List<GroupSlotInput> slots)> GroupRefines { get; } = new();
        public List<ulong> GroupEnds { get; } = new();
        public int? LastGroupDeadlineMs { get; private set; }
        public bool? LastGroupAutoSkipNoise { get; private set; }
        public FloatRoundMode? LastGroupBeginRoundMode { get; private set; }
        public FloatRoundMode? LastGroupRefineRoundMode { get; private set; }

        public override Task<GroupScanBeginResult> BeginGroupScanAsync(
            IReadOnlyList<GroupSlotInput> slots, bool gameOnly = true,
            int maxResults = 50000, bool deep = false, bool crossObject = false,
            bool nativeC = false, bool newestFirst = false, int pageSize = 1000,
            int deadlineMs = 15000, bool autoSkipNoise = false,
            FloatRoundMode roundMode = FloatRoundMode.Round, int perSlotCap = 256,
        CancellationToken ct = default)
        {
            GroupBegins.Add((slots.ToList(), gameOnly, maxResults, deep, crossObject, nativeC, newestFirst));
            LastGroupDeadlineMs = deadlineMs;
            LastGroupAutoSkipNoise = autoSkipNoise;
            LastGroupBeginRoundMode = roundMode;
            return Task.FromResult(NextGroupBeginResult);
        }

        public override Task<GroupScanRefineResult> RefineGroupScanAsync(
            ulong sessionId, IReadOnlyList<GroupSlotInput> slots, int pageSize = 1000,
            FloatRoundMode roundMode = FloatRoundMode.Round, CancellationToken ct = default)
        {
            GroupRefines.Add((sessionId, slots.ToList()));
            LastGroupRefineRoundMode = roundMode;
            return Task.FromResult(NextGroupRefineResult);
        }

        public IReadOnlyList<string>? LastGroupQueryExclude { get; private set; }

        public override Task<GroupScanWindowResult> QueryGroupCandidatesAsync(
            ulong sessionId, int offset, int limit,
            string? filter = null, string? sortKey = null, bool sortDesc = false,
            IReadOnlyList<string>? excludeClasses = null,
            CancellationToken ct = default)
        {
            LastGroupQueryExclude = excludeClasses;
            return Task.FromResult(NextGroupWindowResult);
        }

        /// <summary>Leaves handed back by <c>QueryGroupSlotLeavesAsync</c>, and a count of
        /// how many times it was actually called — the toggle must NOT re-query to collapse.</summary>
        public List<GroupSlotMatch> NextSlotLeaves { get; } = new();
        public int SlotLeafQueries { get; private set; }

        public override Task<IReadOnlyList<GroupSlotMatch>> QueryGroupSlotLeavesAsync(
            ulong sessionId, GroupSlotMatch slot, string instanceAddr, string className,
            int offset = 0, int limit = 0, CancellationToken ct = default)
        {
            SlotLeafQueries++;
            return Task.FromResult<IReadOnlyList<GroupSlotMatch>>(NextSlotLeaves.ToList());
        }

        public override Task EndGroupScanAsync(ulong sessionId, CancellationToken ct = default)
        {
            GroupEnds.Add(sessionId);
            return Task.CompletedTask;
        }
    }

    private static (ValueSearchViewModel vm, FakeDumpService fake) MakeVm()
    {
        var fake = new FakeDumpService();
        var vm = new ValueSearchViewModel(fake, new MockLoggingService());
        return (vm, fake);
    }

    // ------------------------------------------------------------------
    // Multiple values group scan (build 1276)
    // ------------------------------------------------------------------

    [Fact]
    public async Task BeginGroupScanAsync_BuildsValuesArray_AndParsesNestedSlots()
    {
        var svc = MakeService(out var pipe);
        JsonObject? captured = null;
        pipe.SetHandler(req =>
        {
            captured = (JsonObject)req.DeepClone();
            return new JsonObject
            {
                ["id"]              = req["id"]?.GetValue<int>() ?? 0,
                ["ok"]              = true,
                ["session_id"]      = 9UL,
                ["total"]           = 1,
                ["slot_count"]      = 2,
                ["scanned_classes"] = 3,
                ["scanned_objects"] = 100,
                ["duration_ms"]     = 5L,
                ["deadline_hit"]    = false,
                ["candidates"]      = new JsonArray
                {
                    new JsonObject
                    {
                        ["instance_addr"]       = "7FF600000000",
                        ["instance_index"]      = 42,
                        ["instance_name"]       = "BP_Player_C_0",
                        ["class_name"]          = "BP_Player_C",
                        ["defining_class_name"] = "ACharacter",
                        ["slots"]               = new JsonArray
                        {
                            new JsonObject
                            {
                                ["slot_index"]      = 0,
                                ["value"]           = "24",
                                ["field_name"]      = "Str",
                                ["field_offset"]    = 0x20,
                                ["field_type"]      = "IntProperty",
                                ["bool_field_mask"] = 0xFF,
                                ["leaf_value"]      = "24",
                                ["addr"]            = "7FF600000020",
                                ["matched_offsets"] = new JsonArray { 0x20 },
                                ["locked"]          = true,
                            },
                            new JsonObject
                            {
                                ["slot_index"]      = 1,
                                ["value"]           = "10",
                                ["field_name"]      = "Def",
                                ["field_offset"]    = 0x24,
                                ["field_type"]      = "IntProperty",
                                ["bool_field_mask"] = 0xFF,
                                ["leaf_value"]      = "10",
                                ["addr"]            = "7FF600000024",
                                ["matched_offsets"] = new JsonArray { 0x24, 0x40 },
                                ["locked"]          = false,
                            },
                        },
                    },
                },
            };
        });

        var slots = new List<GroupSlotInput>
        {
            new() { DataType = ValueScanDataType.NumericNoByte, Value = "24" },
            new() { DataType = ValueScanDataType.NumericAll,    Value = "10" },
        };
        var res = await svc.BeginGroupScanAsync(slots, gameOnly: true, maxResults: 1234,
            ct: TestContext.Current.CancellationToken);

        Assert.NotNull(captured);
        Assert.Equal("begin_group_scan", captured!["cmd"]?.GetValue<string>());
        var values = Assert.IsType<JsonArray>(captured["values"]);
        Assert.Equal(2, values.Count);
        Assert.Equal("24",            values[0]!["value"]?.GetValue<string>());
        Assert.Equal("NumericNoByte", values[0]!["data_type"]?.GetValue<string>());
        Assert.Equal("NumericAll",    values[1]!["data_type"]?.GetValue<string>());
        Assert.Equal("Exact",         values[0]!["scan_type"]?.GetValue<string>());  // per-slot predicate defaults to Exact

        Assert.Equal(9UL, res.SessionId);
        Assert.Equal(2, res.SlotCount);
        var gc = Assert.Single(res.Candidates);
        Assert.Equal("BP_Player_C", gc.ClassName);
        Assert.Equal(2, gc.Slots.Count);
        Assert.True(gc.Slots[0].Locked);
        Assert.Equal("Str", gc.Slots[0].FieldName);
        Assert.Equal(0x20, gc.Slots[0].FieldOffset);
        // Owner denormalized onto each slot so per-slot handoffs are self-contained.
        Assert.Equal("7FF600000000", gc.Slots[0].InstanceAddr);
        Assert.Equal("BP_Player_C",  gc.Slots[1].ClassName);
        Assert.False(gc.Slots[1].Locked);
        Assert.Equal(new[] { 0x24, 0x40 }, gc.Slots[1].MatchedOffsets);
    }

    [Fact]
    public async Task RefineGroupScanAsync_SendsValuesArray()
    {
        var svc = MakeService(out var pipe);
        JsonObject? captured = null;
        pipe.SetHandler(req =>
        {
            captured = (JsonObject)req.DeepClone();
            return new JsonObject
            {
                ["id"]          = req["id"]?.GetValue<int>() ?? 0,
                ["ok"]          = true,
                ["session_id"]  = 9UL,
                ["total"]       = 0,
                ["duration_ms"] = 1L,
                ["candidates"]  = new JsonArray(),
            };
        });

        await svc.RefineGroupScanAsync(9UL, new[]
        {
            new GroupSlotInput { Value = "24", ScanType = ValueScanType.Exact },
            new GroupSlotInput { Value = "",   ScanType = ValueScanType.Increased },  // prev-value: no value
        }, ct: TestContext.Current.CancellationToken);

        Assert.NotNull(captured);
        Assert.Equal("refine_group_scan", captured!["cmd"]?.GetValue<string>());
        Assert.Equal(9UL, captured["session_id"]?.GetValue<ulong>());
        var values = Assert.IsType<JsonArray>(captured["values"]);
        Assert.Equal(2, values.Count);
        Assert.Equal("24", values[0]!["value"]?.GetValue<string>());
        Assert.Equal("Exact", values[0]!["scan_type"]?.GetValue<string>());
        Assert.Equal("Increased", values[1]!["scan_type"]?.GetValue<string>());  // per-slot predicate (P2)
    }

    [Fact]
    public void GroupMode_TogglesSingleMode()
    {
        var (vm, _) = MakeVm();
        Assert.True(vm.IsSingleMode);
        Assert.False(vm.IsGroupMode);
        vm.IsGroupMode = true;
        Assert.False(vm.IsSingleMode);
    }

    [Fact]
    public void GroupRows_AddRemove_RespectTwoToFourBounds()
    {
        var (vm, _) = MakeVm();
        Assert.Equal(2, vm.GroupInputs.Count);   // starts with 2
        Assert.True(vm.CanAddGroupRow);
        Assert.False(vm.CanRemoveGroupRow);      // can't go below 2

        vm.AddGroupRowCommand.Execute(null);
        vm.AddGroupRowCommand.Execute(null);
        Assert.Equal(4, vm.GroupInputs.Count);
        Assert.False(vm.CanAddGroupRow);         // capped at 4

        vm.AddGroupRowCommand.Execute(null);     // no-op past 4
        Assert.Equal(4, vm.GroupInputs.Count);

        vm.RemoveGroupRowCommand.Execute(null);
        vm.RemoveGroupRowCommand.Execute(null);
        Assert.Equal(2, vm.GroupInputs.Count);
        Assert.False(vm.CanRemoveGroupRow);
    }

    [Fact]
    public async Task GroupFirstScan_PopulatesCandidates_AndOpensSession()
    {
        var (vm, fake) = MakeVm();
        vm.IsGroupMode = true;
        vm.GroupInputs[0].Value = "24";
        vm.GroupInputs[1].Value = "10";
        fake.NextGroupBeginResult = new GroupScanBeginResult
        {
            SessionId = 77UL,
            Total     = 1,
            SlotCount = 2,
            Candidates =
            {
                new GroupCandidate
                {
                    InstanceAddr = "7FF6AA",
                    ClassName    = "BP_Stats_C",
                    Slots        = { new GroupSlotMatch { Value = "24", FieldName = "Str", Locked = true } },
                },
            },
        };

        await vm.GroupFirstScanCommand.ExecuteAsync(null);

        Assert.True(vm.HasGroupSession);
        Assert.Equal(77UL, vm.GroupSessionId);
        Assert.Single(vm.GroupCandidates);
        Assert.Single(fake.GroupBegins);
        Assert.Equal(2, fake.GroupBegins[0].slots.Count);
    }

    [Fact]
    public async Task GroupFirstScan_RejectsEmptyValue()
    {
        var (vm, fake) = MakeVm();
        vm.IsGroupMode = true;
        vm.GroupInputs[0].Value = "24";
        vm.GroupInputs[1].Value = "";   // missing

        await vm.GroupFirstScanCommand.ExecuteAsync(null);

        Assert.Empty(fake.GroupBegins);
        Assert.False(string.IsNullOrEmpty(vm.ErrorMessage));
    }

    // ---- P2: per-slot prev-value/ordered scan types + locked-offset table ----

    [Fact]
    public void GroupSlotInput_RequiresValueInput_ReactsToScanType()
    {
        var slot = new GroupSlotInput();
        Assert.True(slot.RequiresValueInput);                 // Exact (default) needs a value

        var changes = new List<string>();
        slot.PropertyChanged += (_, e) => { if (e.PropertyName != null) changes.Add(e.PropertyName); };

        slot.ScanType = ValueScanType.Increased;              // prev-value: value box hides
        Assert.False(slot.RequiresValueInput);
        Assert.Contains(nameof(GroupSlotInput.RequiresValueInput), changes);

        slot.ScanType = ValueScanType.Bigger;                 // targeted again: value box returns
        Assert.True(slot.RequiresValueInput);
    }

    [Fact]
    public async Task GroupFirstScan_RejectsPrevValueScanType()
    {
        var (vm, fake) = MakeVm();
        vm.IsGroupMode = true;
        vm.GroupInputs[0].Value = "24";
        vm.GroupInputs[1].ScanType = ValueScanType.Increased;  // no baseline on a first scan

        await vm.GroupFirstScanCommand.ExecuteAsync(null);

        Assert.Empty(fake.GroupBegins);                        // never reached the DLL
        Assert.False(string.IsNullOrEmpty(vm.ErrorMessage));
    }

    [Fact]
    public async Task GroupNextScan_PassesPerSlotScanTypes()
    {
        var (vm, fake) = MakeVm();
        vm.IsGroupMode = true;
        vm.GroupInputs[0].Value = "24";
        vm.GroupInputs[1].Value = "10";
        fake.NextGroupBeginResult = new GroupScanBeginResult { SessionId = 5UL, Total = 1 };
        await vm.GroupFirstScanCommand.ExecuteAsync(null);     // open a session
        Assert.True(vm.HasGroupSession);

        // Now refine: slot 0 "Increased" (no value), slot 1 still Exact 10.
        vm.GroupInputs[0].ScanType = ValueScanType.Increased;
        vm.GroupInputs[0].Value = "";
        await vm.GroupNextScanCommand.ExecuteAsync(null);

        var refine = Assert.Single(fake.GroupRefines);
        Assert.Equal(5UL, refine.sessionId);
        Assert.Equal(ValueScanType.Increased, refine.slots[0].ScanType);
        Assert.Equal(ValueScanType.Exact,     refine.slots[1].ScanType);
    }

    [Fact]
    public void GroupCandidate_OffsetTable_OnlyWhenAllLocked()
    {
        var gc = new GroupCandidate
        {
            ClassName = "BP_Stats_C",
            Slots =
            {
                new GroupSlotMatch { FieldName = "Str", FieldOffset = 0x20, Locked = true },
                new GroupSlotMatch { FieldName = "Def", FieldOffset = 0x24, Locked = false },
            },
        };
        Assert.False(gc.HasOffsetTable);                       // a slot is still converging

        gc.Slots[1].Locked = true;
        Assert.True(gc.HasOffsetTable);
        Assert.Equal("Str@0x20, Def@0x24", gc.OffsetTable);
        Assert.Equal("🔒 BP_Stats_C — Str@0x20, Def@0x24", gc.OffsetTableLabel);
    }

    [Fact]
    public void GroupCandidate_SlotSummary_ShowsActualLeafValueNotQueryTarget()
    {
        // The "Matched values" master-row summary must show the ACTUAL current value
        // (LeafValue), not the query target / Between bound — a float that displays as
        // an integer (513.36) was misleadingly summarized as the searched "513" / the
        // lower bound "510". Parity with the SPC group display. (build 1678 fix.)
        var gc = new GroupCandidate
        {
            Slots =
            {
                // Exact 513 search → real value 513.36 (Value Search live path).
                new GroupSlotMatch { FieldName = "Health.BaseValue", Value = "513", LeafValue = "513.36" },
                // Between 510..514 → real value 513.36 (Snapshot path; bound must not show).
                new GroupSlotMatch { FieldName = "Health.CurrentValue", ScanType = "Between", Value = "510", Value2 = "514", LeafValue = "513.36" },
            },
        };
        Assert.Equal("Health.BaseValue=513.36, Health.CurrentValue=513.36", gc.SlotSummary);

        // Defensive fallback: when LeafValue is empty, the target is used.
        var noLeaf = new GroupCandidate { Slots = { new GroupSlotMatch { FieldName = "Str", Value = "24", LeafValue = "" } } };
        Assert.Equal("Str=24", noLeaf.SlotSummary);
    }

    [Fact]
    public void GroupCandidate_SlotSummary_AnnotatesTheFieldsItIsNotShowing()
    {
        // A row displays ONE assignment out of the many an object may satisfy. On
        // the DumperTest sample slot 0 kept {Health.CurrentValue, TickCount} and
        // slot 1 kept 36 leaves including FrozenInt; the row showed the Health pair
        // and the TickCount/FrozenInt pair was reported as a missed match. The
        // "(+N)" annotation is what stops a valid row reading as an exhaustive one.
        var gc = new GroupCandidate
        {
            Slots =
            {
                new GroupSlotMatch { FieldName = "Health.CurrentValue", LeafValue = "19",  MatchCount = 2  },
                new GroupSlotMatch { FieldName = "Health.BaseValue",    LeafValue = "100", MatchCount = 36 },
            },
        };
        Assert.Equal("Health.CurrentValue=19 (+1), Health.BaseValue=100 (+35)", gc.SlotSummary);

        // A slot that matched exactly one field says nothing extra.
        var single = new GroupCandidate
        {
            Slots = { new GroupSlotMatch { FieldName = "Str", LeafValue = "24", MatchCount = 1 },
                      new GroupSlotMatch { FieldName = "Def", LeafValue = "10", MatchedOffsets = { 0x24 } } },
        };
        Assert.Equal("Str=24, Def=10", single.SlotSummary);

        // The Snapshot / SPC shape: MatchCount stays 0 but MatchedOffsets carries one
        // entry per distinct matching field, so those panels get the SAME annotation.
        // Fixing this in Value Search alone is what turned the last round into a
        // second, separate report.
        var snapshotShape = new GroupCandidate
        {
            Slots = { new GroupSlotMatch { FieldName = "Hp",  LeafValue = "23",
                                           MatchedOffsets = { 0x40, 0x44 } },
                      new GroupSlotMatch { FieldName = "Max", LeafValue = "100",
                                           MatchedOffsets = { 0x44, 0x48, 0x4C } } },
        };
        Assert.Equal("Hp=23 (+1), Max=100 (+2)", snapshotShape.SlotSummary);
    }

    [Fact]
    public void GroupSlotMatch_DisplayLabel_ShowsAnAddressOnlyWhenItIsTheLeafsOwn()
    {
        // A deep / container leaf has no object-relative offset, so the DLL stores 0.
        // On the LIVE path `Addr` is that leaf's real address, so show it.
        var live = new GroupSlotMatch
        {
            FieldName = "Tunes[2]", FieldOffset = 0, FieldType = "IntProperty",
            ScanType = "Changed", Locked = true,
            Addr = "0x1F2A3B4C700", HasLeafAddress = true,
        };
        Assert.Equal("Tunes[2]  ≠ changed → 0x1F2A3B4C700  (IntProperty)", live.DisplayLabel);

        // On the SNAPSHOT path the element's heap address was never captured and
        // `Addr` is the OWNING OBJECT's base. Printing it would name the UObject
        // header as the place the value lives — a plausible, copyable, wrong address.
        // Say nothing instead. (Before the leaf-address flag existed this rendered
        // "→ 0x1F2A3B4C500", which is the defect this test exists to hold shut.)
        var snapshot = new GroupSlotMatch
        {
            FieldName = "Tunes[2]", FieldOffset = 0, FieldType = "IntProperty",
            ScanType = "Changed", Locked = true,
            Addr = "0x1F2A3B4C500", HasLeafAddress = false,
        };
        Assert.Equal("Tunes[2]  ≠ changed  (IntProperty)", snapshot.DisplayLabel);
        Assert.DoesNotContain("0x", snapshot.DisplayLabel);

        // A real offset always wins over either.
        var direct = new GroupSlotMatch
        {
            FieldName = "Hp", FieldOffset = 0x40, FieldType = "FloatProperty",
            ScanType = "Changed", Locked = true,
            Addr = "0x1F2A3B4C540", HasLeafAddress = true,
        };
        Assert.Equal("Hp  ≠ changed → 0x40  (FloatProperty)", direct.DisplayLabel);
    }

    [Fact]
    public void GroupSlotMatch_DisplayLabel_UnlockedStillNamesTheDisplayedField()
    {
        // Before: "= unchanged: 36 candidate offset(s)" — a count, no name. The
        // expanded row was therefore no help at all in finding out that one of
        // those 36 offsets was FrozenInt.
        var slot = new GroupSlotMatch
        {
            FieldName = "Health.BaseValue", FieldOffset = 0x504, FieldType = "FloatProperty",
            ScanType = "Unchanged", Locked = false,
            MatchedOffsets = { 52, 100, 1284, 1308 },
        };
        Assert.Equal("Health.BaseValue  = unchanged → 0x504  — 1 of 4 matching field(s)",
                     slot.DisplayLabel);
        Assert.Equal("×4", slot.LockLabel);
    }

    [Fact]
    public async Task QueryGroupCandidatesAsync_ReadsMatchCount()
    {
        var svc = MakeService(out var pipe);
        pipe.SetHandler(req => new JsonObject
        {
            ["id"] = req["id"]?.GetValue<int>() ?? 0, ["ok"] = true,
            ["session_id"] = 1UL, ["total"] = 1, ["filtered_total"] = 1,
            ["candidates"] = new JsonArray
            {
                new JsonObject
                {
                    ["instance_addr"] = "0x112FBCB4600",
                    ["class_name"]    = "DumperTestActor",
                    ["slots"] = new JsonArray
                    {
                        new JsonObject
                        {
                            ["slot_index"] = 0, ["scan_type"] = "Changed",
                            ["field_name"] = "Health.CurrentValue", ["leaf_value"] = "19",
                            ["match_count"] = 2,
                            ["matched_offsets"] = new JsonArray { 1288, 1304 },
                        },
                    },
                },
            },
        });

        var res = await svc.QueryGroupCandidatesAsync(1UL, 0, 100, ct: TestContext.Current.CancellationToken);

        var slot = Assert.Single(Assert.Single(res.Candidates).Slots);
        Assert.Equal(2, slot.MatchCount);
        Assert.True(slot.HasHiddenLeaves);
        Assert.Equal(" (+1)", slot.HiddenLeavesSuffix);
    }

    [Fact]
    public async Task LoadGroupSlotLeaves_SecondPressCollapses_WithoutAnotherRoundTrip()
    {
        // "All fields" is the only control for the list, so it has to close it too:
        // two open slots run to dozens of rows and there was no way back.
        var (vm, fake) = MakeVm();
        vm.GroupSessionId = 7;
        fake.NextSlotLeaves.Add(new GroupSlotMatch { FieldName = "TickCount", LeafValue = "219" });
        fake.NextSlotLeaves.Add(new GroupSlotMatch { FieldName = "FrozenInt", LeafValue = "424242" });
        var slot = new GroupSlotMatch
        {
            SlotIndex = 0, InstanceAddr = "0x17849EC4600", ClassName = "DumperTestActor",
        };

        await vm.LoadGroupSlotLeavesCommand.ExecuteAsync(slot);
        Assert.Equal(2, slot.Leaves.Count);
        Assert.Equal(1, fake.SlotLeafQueries);

        // Collapse is local — pressing again must not ask the DLL a second time.
        await vm.LoadGroupSlotLeavesCommand.ExecuteAsync(slot);
        Assert.Empty(slot.Leaves);
        Assert.Equal(1, fake.SlotLeafQueries);

        // Re-opening DOES re-query, so a live scan never shows a stale snapshot.
        await vm.LoadGroupSlotLeavesCommand.ExecuteAsync(slot);
        Assert.Equal(2, slot.Leaves.Count);
        Assert.Equal(2, fake.SlotLeafQueries);
    }

    [Fact]
    public async Task QueryGroupSlotLeavesAsync_AsksForTheSlot_AndCarriesItsIdentityOntoEveryLeaf()
    {
        var svc = MakeService(out var pipe);
        JsonObject? captured = null;
        pipe.SetHandler(req =>
        {
            captured = (JsonObject)req.DeepClone();
            return new JsonObject
            {
                ["id"] = req["id"]?.GetValue<int>() ?? 0, ["ok"] = true,
                ["total"] = 2, ["count"] = 2,
                ["leaves"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["field_name"] = "Health.BaseValue", ["field_offset"] = 1284,
                        ["field_type"] = "FloatProperty", ["leaf_value"] = "100",
                        ["addr"] = "0x112FBCB4B04", ["owner_addr"] = "0x112FBCB4600",
                        ["owner_class"] = "DumperTestActor",
                    },
                    new JsonObject
                    {
                        ["field_name"] = "FrozenInt", ["field_offset"] = 1308,
                        ["field_type"] = "IntProperty", ["leaf_value"] = "424242",
                        ["addr"] = "0x112FBCB4B1C", ["owner_addr"] = "0x112FBCB4600",
                        ["owner_class"] = "DumperTestActor",
                    },
                },
            };
        });

        var slot = new GroupSlotMatch
        {
            SlotIndex = 1, ScanType = "Unchanged", Value = "0",
            Addr = "0x112FBCB4B04",   // the displayed leaf — the deep-block tie-breaker
        };
        var leaves = await svc.QueryGroupSlotLeavesAsync(
            7UL, slot, "0x112FBCB4600", "DumperTestActor",
            ct: TestContext.Current.CancellationToken);

        Assert.Equal("query_group_slot_leaves", captured!["cmd"]?.GetValue<string>());
        Assert.Equal(7UL, captured["session_id"]?.GetValue<ulong>());
        Assert.Equal("0x112FBCB4600", captured["instance_addr"]?.GetValue<string>());
        Assert.Equal(1, captured["slot_index"]?.GetValue<int>());
        // Without this the server falls back to the first candidate sharing the
        // instance address — with `deep` that is a DIFFERENT container block, i.e.
        // another block's fields answering this row.
        Assert.Equal("0x112FBCB4B04", captured["leaf_addr"]?.GetValue<string>());
        // Paging is opt-in — an unpaged fetch must not pin a limit on the wire.
        Assert.False(captured.ContainsKey("offset"));
        Assert.False(captured.ContainsKey("limit"));

        Assert.Equal(2, leaves.Count);
        // The leaf a row could never show, now named — this is the whole point.
        Assert.Equal("FrozenInt", leaves[1].FieldName);
        Assert.Equal("424242", leaves[1].LeafValue);
        Assert.Equal(1308, leaves[1].FieldOffset);
        foreach (var leaf in leaves)
        {
            // Carries the SLOT's identity, so it renders and hands off like the
            // representative does...
            Assert.Equal(1, leaf.SlotIndex);
            Assert.Equal("Unchanged", leaf.ScanType);
            Assert.Equal("0x112FBCB4600", leaf.InstanceAddr);
            Assert.Equal("DumperTestActor", leaf.ClassName);
            // ...and a leaf IS one identified field, not a converging set.
            Assert.True(leaf.Locked);
        }
        Assert.Equal("FrozenInt  = unchanged → 0x51C  (IntProperty)", leaves[1].DisplayLabel);
    }

    [Fact]
    public void GroupSlotMatch_DisplayLabel_RendersPrevValueCriterion()
    {
        var targeted = new GroupSlotMatch
        {
            FieldName = "Str", FieldOffset = 0x20, FieldType = "IntProperty",
            Value = "24", ScanType = "Exact", Locked = true,
        };
        Assert.Equal("Str  24 → 0x20  (IntProperty)", targeted.DisplayLabel);

        var prev = new GroupSlotMatch
        {
            FieldName = "Hp", FieldOffset = 0x40, FieldType = "FloatProperty",
            Value = "", ScanType = "Increased", Locked = true,
        };
        Assert.Equal("Hp  ↑ increased → 0x40  (FloatProperty)", prev.DisplayLabel);

        var between = new GroupSlotMatch
        {
            FieldName = "Hp", FieldOffset = 0x40, FieldType = "IntProperty",
            Value = "1", Value2 = "100", ScanType = "Between", Locked = true,
        };
        Assert.Equal("Hp  1..100 → 0x40  (IntProperty)", between.DisplayLabel);
    }

    [Fact]
    public void GroupSlotInput_RequiresValue2Input_OnlyForBetween()
    {
        var slot = new GroupSlotInput();
        Assert.False(slot.RequiresValue2Input);               // Exact: no upper bound

        var changes = new List<string>();
        slot.PropertyChanged += (_, e) => { if (e.PropertyName != null) changes.Add(e.PropertyName); };

        slot.ScanType = ValueScanType.Between;
        Assert.True(slot.RequiresValue2Input);                // Between reveals the 2nd box
        Assert.True(slot.RequiresValueInput);                 // and still needs the low value
        Assert.Contains(nameof(GroupSlotInput.RequiresValue2Input), changes);

        slot.ScanType = ValueScanType.Exact;
        Assert.False(slot.RequiresValue2Input);
    }

    [Fact]
    public async Task BeginGroupScanAsync_SendsValue2_ForBetweenOnly()
    {
        var svc = MakeService(out var pipe);
        JsonObject? captured = null;
        pipe.SetHandler(req =>
        {
            captured = (JsonObject)req.DeepClone();
            return new JsonObject
            {
                ["id"] = req["id"]?.GetValue<int>() ?? 0, ["ok"] = true,
                ["session_id"] = 1UL, ["total"] = 0, ["candidates"] = new JsonArray(),
            };
        });
        var slots = new List<GroupSlotInput>
        {
            new() { Value = "1", Value2 = "100", ScanType = ValueScanType.Between },
            new() { Value = "5", ScanType = ValueScanType.Exact },
        };

        await svc.BeginGroupScanAsync(slots, ct: TestContext.Current.CancellationToken);

        var values = Assert.IsType<JsonArray>(captured!["values"]);
        Assert.Equal("Between", values[0]!["scan_type"]?.GetValue<string>());
        Assert.Equal("100", values[0]!["value2"]?.GetValue<string>());
        Assert.False(((JsonObject)values[1]!).ContainsKey("value2"), "value2 only for Between");
    }

    [Fact]
    public async Task GroupFirstScan_RejectsBetweenMissingUpperValue()
    {
        var (vm, fake) = MakeVm();
        vm.IsGroupMode = true;
        vm.GroupInputs[0].ScanType = ValueScanType.Between;
        vm.GroupInputs[0].Value = "1";        // low present, high (Value2) missing
        vm.GroupInputs[1].Value = "10";

        await vm.GroupFirstScanCommand.ExecuteAsync(null);

        Assert.Empty(fake.GroupBegins);                       // validation blocked it
        Assert.False(string.IsNullOrEmpty(vm.ErrorMessage));
    }

    // ---- P4: cross-object (owned sub-objects) ----

    [Fact]
    public async Task BeginGroupScanAsync_AttachesCrossObject_WhenEnabled_OmitsByDefault()
    {
        var svc = MakeService(out var pipe);
        JsonObject? captured = null;
        pipe.SetHandler(req =>
        {
            captured = (JsonObject)req.DeepClone();
            return new JsonObject
            {
                ["id"] = req["id"]?.GetValue<int>() ?? 0, ["ok"] = true,
                ["session_id"] = 1UL, ["total"] = 0, ["candidates"] = new JsonArray(),
            };
        });
        var slots = new List<GroupSlotInput> { new() { Value = "1" }, new() { Value = "2" } };

        await svc.BeginGroupScanAsync(slots, crossObject: false, ct: TestContext.Current.CancellationToken);
        Assert.False(captured!.ContainsKey("cross_object"), "cross_object omitted when off (wire-tight)");

        await svc.BeginGroupScanAsync(slots, crossObject: true, ct: TestContext.Current.CancellationToken);
        Assert.True(captured!["cross_object"]?.GetValue<bool>(), "cross_object attached when on");
    }

    [Fact]
    public async Task BeginGroupScanAsync_AttachesNativeC_WhenEnabled_OmitsByDefault()
    {
        var svc = MakeService(out var pipe);
        JsonObject? captured = null;
        pipe.SetHandler(req =>
        {
            captured = (JsonObject)req.DeepClone();
            return new JsonObject
            {
                ["id"] = req["id"]?.GetValue<int>() ?? 0, ["ok"] = true,
                ["session_id"] = 1UL, ["total"] = 0, ["candidates"] = new JsonArray(),
            };
        });
        var slots = new List<GroupSlotInput> { new() { Value = "1" }, new() { Value = "2" } };

        await svc.BeginGroupScanAsync(slots, nativeC: false, ct: TestContext.Current.CancellationToken);
        Assert.False(captured!.ContainsKey("native_c"), "native_c omitted when off (wire-tight)");

        await svc.BeginGroupScanAsync(slots, nativeC: true, ct: TestContext.Current.CancellationToken);
        Assert.True(captured!["native_c"]?.GetValue<bool>(), "native_c attached when on");
    }

    [Fact]
    public void GroupSlotMatch_Origin_ReflectsNativeFlag()
    {
        Assert.Equal("Reflected",
            new UE5DumpUI.Models.GroupSlotMatch { IsNativeField = false }.Origin);
        Assert.Equal("Native-C (Int32)",
            new UE5DumpUI.Models.GroupSlotMatch { IsNativeField = true, GuessedType = "Int32" }.Origin);
    }

    [Fact]
    public void GroupSlotMatch_HandoffAddr_PrefersOwnerOverActor()
    {
        var own = new GroupSlotMatch { InstanceAddr = "7FF6AA", OwnerAddr = "" };
        Assert.Equal("7FF6AA", own.HandoffAddr);    // own-block leaf -> handoff opens the actor

        var cross = new GroupSlotMatch { InstanceAddr = "7FF6AA", OwnerAddr = "1234BB" };
        Assert.Equal("1234BB", cross.HandoffAddr);  // cross-object leaf -> opens the owned sub-object
    }

    [Fact]
    public void GroupSlotMatch_PivotClassName_PrefersOwnerOverActor()
    {
        // Own-block leaf: the DLL emits owner_class == candidate class (or omits it on
        // an older payload) -> Pivot uses the candidate actor's class.
        var own = new GroupSlotMatch { ClassName = "BP_Player_C", OwnerClass = "" };
        Assert.Equal("BP_Player_C", own.PivotClassName);

        // Cross-object leaf: the owned sub-object's class -> Pivot lands on the class
        // that actually declares the field (e.g. the GAS UAttributeSet), not the actor.
        var cross = new GroupSlotMatch { ClassName = "BP_Player_C", OwnerClass = "BP_HealthSet_C" };
        Assert.Equal("BP_HealthSet_C", cross.PivotClassName);
    }

    [Fact]
    public void GroupPivot_UsesOwnerClassForCrossObjectSlot()
    {
        var (vm, _) = MakeVm();
        (string cls, string prop)? got = null;
        vm.NavigateToPivot += (c, p) => got = (c, p);

        // A cross-object slot: Pivot must target the owned sub-object's class.
        vm.PivotGroupSlotCommand.Execute(new GroupSlotMatch
        {
            ClassName = "BP_Player_C", OwnerClass = "BP_HealthSet_C", FieldName = "CurrentHealth.BaseValue",
        });
        Assert.Equal(("BP_HealthSet_C", "CurrentHealth.BaseValue"), got);

        // An own-block slot (no owner_class) falls back to the candidate actor's class.
        got = null;
        vm.PivotGroupSlotCommand.Execute(new GroupSlotMatch
        {
            ClassName = "BP_Player_C", OwnerClass = "", FieldName = "Gold",
        });
        Assert.Equal(("BP_Player_C", "Gold"), got);
    }

    [Fact]
    public async Task GroupFirstScan_PassesCrossObjectFlag()
    {
        var (vm, fake) = MakeVm();
        vm.IsGroupMode = true;
        vm.CrossObjectScan = true;
        vm.GroupInputs[0].Value = "10";
        vm.GroupInputs[1].Value = "20";
        fake.NextGroupBeginResult = new GroupScanBeginResult { SessionId = 3UL, Total = 0 };

        await vm.GroupFirstScanCommand.ExecuteAsync(null);

        var begin = Assert.Single(fake.GroupBegins);
        Assert.True(begin.crossObject);
    }

    // ---- Locate-in-GWorld handoff: the DLL decides, not the client ----
    // (Gating the button on a C# flag that read false on TQ2 — proxy mode — silently
    // disabled it even though GWorld was resolved; the DLL path search is the truth.)

    [Fact]
    public void GroupLocate_InvokesEvenWhenGWorldFlagFalse()
    {
        var (vm, _) = MakeVm();
        // No engine state set at all — and there is no longer a client-side GWorld
        // flag to consult, so the handoff cannot be pre-refused (audit #5 AE10).
        string? located = null;
        vm.LocateInGWorld += (addr, _, _) => located = addr;

        vm.LocateGroupSlotInGWorldCommand.Execute(new GroupSlotMatch { OwnerAddr = "0x1234" });

        Assert.Equal("0x1234", located);             // still fires — DLL decides GWorld availability
    }

    [Fact]
    public void GroupLocate_NoAddress_ReportsInsteadOfSilentNoOp()
    {
        var (vm, _) = MakeVm();
        bool fired = false;
        vm.LocateInGWorld += (_, _, _) => fired = true;

        vm.LocateGroupSlotInGWorldCommand.Execute(new GroupSlotMatch { OwnerAddr = "", InstanceAddr = "" });

        Assert.False(fired);                                  // no address -> no locate
        Assert.False(string.IsNullOrEmpty(vm.StatusText));    // but the user is told why (not a silent no-op)
    }

    [Fact]
    public async Task BeginGroupScanAsync_AttachesDeepWhenEnabled_OmitsByDefault()
    {
        var svc = MakeService(out var pipe);
        JsonObject? captured = null;
        pipe.SetHandler(req =>
        {
            captured = (JsonObject)req.DeepClone();
            return new JsonObject
            {
                ["id"] = req["id"]?.GetValue<int>() ?? 0, ["ok"] = true,
                ["session_id"] = 1UL, ["total"] = 0, ["candidates"] = new JsonArray(),
            };
        });
        var slots = new List<GroupSlotInput> { new() { Value = "1" }, new() { Value = "2" } };

        await svc.BeginGroupScanAsync(slots, deep: false, ct: TestContext.Current.CancellationToken);
        Assert.False(captured!.ContainsKey("deep"), "deep must be omitted when off (wire-tight)");

        await svc.BeginGroupScanAsync(slots, deep: true, ct: TestContext.Current.CancellationToken);
        Assert.True(captured!["deep"]?.GetValue<bool>(), "deep must be attached when on");
    }

    [Fact]
    public async Task BeginValueScanAsync_AttachesDeepWhenEnabled()
    {
        var svc = MakeService(out var pipe);
        JsonObject? captured = null;
        pipe.SetHandler(req =>
        {
            captured = (JsonObject)req.DeepClone();
            return new JsonObject
            {
                ["id"] = req["id"]?.GetValue<int>() ?? 0, ["ok"] = true,
                ["session_id"] = 1UL, ["data_type"] = "Int32", ["total"] = 0,
                ["scanned_classes"] = 0, ["scanned_objects"] = 0, ["duration_ms"] = 0L,
                ["deadline_hit"] = false, ["candidates"] = new JsonArray(),
            };
        });
        await svc.BeginValueScanAsync(ValueScanDataType.Int32, ValueScanType.Exact, "10",
            deep: true, ct: TestContext.Current.CancellationToken);
        Assert.True(captured!["deep"]?.GetValue<bool>());
    }

    [Fact]
    public async Task BeginValueScanAsync_AttachesNativeCAndNewestFirstOnlyWhenEnabled()
    {
        var svc = MakeService(out var pipe);
        JsonObject? captured = null;
        pipe.SetHandler(req =>
        {
            captured = (JsonObject)req.DeepClone();
            return new JsonObject
            {
                ["id"] = req["id"]?.GetValue<int>() ?? 0, ["ok"] = true,
                ["session_id"] = 1UL, ["data_type"] = "Int32", ["total"] = 0,
                ["scanned_classes"] = 0, ["scanned_objects"] = 0, ["duration_ms"] = 0L,
                ["deadline_hit"] = false, ["candidates"] = new JsonArray(),
            };
        });
        // Off by default → omitted (wire-tight, back-compat with old DLLs).
        await svc.BeginValueScanAsync(ValueScanDataType.Int32, ValueScanType.Exact, "10",
            ct: TestContext.Current.CancellationToken);
        Assert.False(captured!.ContainsKey("native_c"));
        Assert.False(captured!.ContainsKey("newest_first"));

        await svc.BeginValueScanAsync(ValueScanDataType.Int32, ValueScanType.Exact, "10",
            nativeC: true, newestFirst: true, ct: TestContext.Current.CancellationToken);
        Assert.True(captured!["native_c"]?.GetValue<bool>());
        Assert.True(captured!["newest_first"]?.GetValue<bool>());
    }

    [Fact]
    public void NativeCScan_Couples_NewestFirst()
    {
        var (vm, _) = MakeVm();
        Assert.False(vm.NativeCScan);
        Assert.False(vm.NewestFirst);

        // Enabling Native-C pre-checks Newest-first.
        vm.NativeCScan = true;
        Assert.True(vm.NewestFirst);

        // User may independently uncheck Newest-first while Native-C stays on.
        vm.NewestFirst = false;
        Assert.True(vm.NativeCScan);
        Assert.False(vm.NewestFirst);

        // Re-enabling re-checks it; disabling Native-C then clears it.
        vm.NewestFirst = true;
        vm.NativeCScan = false;
        Assert.False(vm.NewestFirst);
    }

    [Fact]
    public void ValueCandidate_Origin_ReflectsNativeFlag()
    {
        var reflected = new UE5DumpUI.Models.ValueCandidate { IsNativeField = false };
        Assert.Equal("Reflected", reflected.Origin);

        var native = new UE5DumpUI.Models.ValueCandidate
        { IsNativeField = true, GuessedType = "Int32" };
        Assert.Equal("Native-C (Int32)", native.Origin);
    }

    [Fact]
    public async Task FirstScan_PassesNativeCAndNewestFirst()
    {
        var (vm, fake) = MakeVm();
        fake.NextBeginResult = new ValueScanBeginResult { SessionId = 7UL, DataType = "Int32", Total = 0 };
        vm.NativeCScan = true;            // also auto-checks NewestFirst
        vm.SelectedDataType = ValueScanDataType.Int32;
        vm.Value = "100";

        await vm.FirstScanCommand.ExecuteAsync(null);

        Assert.True(fake.LastNativeC);
        Assert.True(fake.LastNewestFirst);
    }

    [Fact]
    public async Task GroupFirstScan_PassesDeepFlag()
    {
        var (vm, fake) = MakeVm();
        vm.IsGroupMode = true;
        vm.DeepScan = true;
        vm.GroupInputs[0].Value = "10";
        vm.GroupInputs[1].Value = "20";
        fake.NextGroupBeginResult = new GroupScanBeginResult { SessionId = 5UL, Total = 0 };

        await vm.GroupFirstScanCommand.ExecuteAsync(null);

        Assert.Single(fake.GroupBegins);
        Assert.True(fake.GroupBegins[0].deep);
    }

    [Fact]
    public async Task GroupFirstScan_PassesNativeCFlag()
    {
        var (vm, fake) = MakeVm();
        vm.IsGroupMode = true;
        vm.NativeCScan = true;            // shared toggle; group sends native_c
        vm.GroupInputs[0].Value = "777";
        vm.GroupInputs[1].Value = "1234";
        fake.NextGroupBeginResult = new GroupScanBeginResult { SessionId = 9UL, Total = 0 };

        await vm.GroupFirstScanCommand.ExecuteAsync(null);

        Assert.Single(fake.GroupBegins);
        Assert.True(fake.GroupBegins[0].nativeC);
        // Native-C couples Newest-first on → group passes it too (reaches high-index
        // UI/actor objects before a deadline truncation on a huge game).
        Assert.True(fake.GroupBegins[0].newestFirst);
    }

    [Fact]
    public async Task FirstScan_PopulatesCandidates_AndOpensSession()
    {
        var (vm, fake) = MakeVm();
        fake.NextBeginResult = new ValueScanBeginResult
        {
            SessionId = 42UL,
            DataType  = "Int32",
            Total     = 1,
            Candidates =
            {
                new ValueCandidate
                {
                    Addr = "0x1000", InstanceAddr = "0x2000",
                    ClassName = "BP_Player_C", FieldName = "Health",
                    FieldType = "IntProperty", Value = "100"
                }
            }
        };
        vm.SelectedDataType = ValueScanDataType.Int32;
        vm.SelectedScanType = ValueScanType.Exact;
        vm.Value = "100";

        await vm.FirstScanCommand.ExecuteAsync(null);

        Assert.Equal(42UL, vm.SessionId);
        Assert.True(vm.HasSession);
        Assert.Single(vm.Candidates);
        Assert.Single(fake.Begins);
        var (dt, st, val, val2, _, _, _, _) = fake.Begins[0];
        Assert.Equal(ValueScanDataType.Int32, dt);
        Assert.Equal(ValueScanType.Exact,     st);
        Assert.Equal("100",                   val);
        Assert.Null(val2);
    }

    // --- "inst" button: open the hit's owning class in the Instance Finder ---

    [Fact]
    public void OpenInInstanceFinder_RaisesNavigate_WithClassName()
    {
        var (vm, _) = MakeVm();
        string? got = null;
        vm.NavigateToInstanceFinder += c => got = c;

        vm.OpenInInstanceFinderCommand.Execute(
            new ValueCandidate { ClassName = "BP_Player_C", FieldName = "Health" });

        Assert.Equal("BP_Player_C", got);
    }

    [Fact]
    public void OpenInInstanceFinder_EmptyClass_DoesNothing()
    {
        var (vm, _) = MakeVm();
        bool fired = false;
        vm.NavigateToInstanceFinder += _ => fired = true;

        vm.OpenInInstanceFinderCommand.Execute(new ValueCandidate { ClassName = "" });

        Assert.False(fired);
    }

    // --- Column-header click → server-side sort (the grid is windowed, so a
    //     header click drives the DLL sort, not a client-side page sort) ---

    [Fact]
    public async Task ApplyColumnSort_DrivesServerSideSort_AndTogglesDirection()
    {
        var (vm, fake) = MakeVm();
        fake.NextBeginResult  = new ValueScanBeginResult  { SessionId = 1UL, Total = 5 };
        fake.NextWindowResult = new ValueScanWindowResult { SessionId = 1UL, Total = 5, FilteredTotal = 5 };
        vm.SelectedDataType = ValueScanDataType.Int32;
        vm.SelectedScanType = ValueScanType.Exact;
        vm.Value = "1";
        await vm.FirstScanCommand.ExecuteAsync(null);
        fake.Queries.Clear();

        // Click "Value" header → sort by value, ascending, over the full set.
        vm.ApplyColumnSort("value");
        Assert.Equal("value", vm.SelectedSortOption?.Key);
        Assert.False(vm.SortDescending);
        Assert.Contains(fake.Queries, q => q.Item5 == "value" && q.Item6 == false);

        // Click the same header again → flip to descending (same key).
        fake.Queries.Clear();
        vm.ApplyColumnSort("value");
        Assert.Equal("value", vm.SelectedSortOption?.Key);
        Assert.True(vm.SortDescending);
        Assert.Contains(fake.Queries, q => q.Item5 == "value" && q.Item6 == true);

        // Click a different header → new key, back to ascending, single query.
        fake.Queries.Clear();
        vm.ApplyColumnSort("offset");
        Assert.Equal("offset", vm.SelectedSortOption?.Key);
        Assert.False(vm.SortDescending);
        Assert.Single(fake.Queries);
        Assert.Contains(fake.Queries, q => q.Item5 == "offset" && q.Item6 == false);
    }

    [Fact]
    public void ApplyColumnSort_UnknownKey_IsIgnored()
    {
        var (vm, _) = MakeVm();
        var before = vm.SelectedSortOption;
        vm.ApplyColumnSort("not-a-real-key");
        Assert.Same(before, vm.SelectedSortOption);
    }

    /// <summary>
    /// New Scan must reset the BOUND sort picker, not just the private key — audit #5 AE9.
    ///
    /// Assigning only the private key left the combo showing "Value" while the next scan ran
    /// in scan order, and re-selecting the option the combo already displays raises no change
    /// notification, so the user could not get that sort back without picking a third option
    /// first. The second half of this test is what makes it a real check: it re-selects the
    /// SAME key afterwards and requires the query to come back.
    /// </summary>
    [Fact]
    public async Task NewScan_ResetsTheBoundSortPicker_SoTheSameSortCanBeChosenAgain()
    {
        var (vm, fake) = MakeVm();
        fake.NextBeginResult  = new ValueScanBeginResult  { SessionId = 1UL, Total = 5 };
        fake.NextWindowResult = new ValueScanWindowResult { SessionId = 1UL, Total = 5, FilteredTotal = 5 };
        vm.SelectedDataType = ValueScanDataType.Int32;
        vm.SelectedScanType = ValueScanType.Exact;
        vm.Value = "1";
        await vm.FirstScanCommand.ExecuteAsync(null);

        vm.ApplyColumnSort("value");
        vm.SortDescending = true;
        Assert.Equal("value", vm.SelectedSortOption?.Key);

        await vm.NewScanCommand.ExecuteAsync(null);

        Assert.Equal("scan", vm.SelectedSortOption?.Key);   // the picker, not just the key
        Assert.False(vm.SortDescending);

        // And the previously-chosen sort is reachable again: start a new session, pick
        // "value", and the server query must actually carry it.
        fake.NextBeginResult  = new ValueScanBeginResult  { SessionId = 2UL, Total = 5 };
        fake.NextWindowResult = new ValueScanWindowResult { SessionId = 2UL, Total = 5, FilteredTotal = 5 };
        await vm.FirstScanCommand.ExecuteAsync(null);
        fake.Queries.Clear();

        vm.SelectedSortOption = vm.SortOptions.First(o => o.Key == "value");

        Assert.Contains(fake.Queries, q => q.Item5 == "value");
    }

    /// <summary>
    /// A REJECTED scan click must not open the diagnostics probe — audit #5 AE8.
    ///
    /// The probe sat above four early returns, so every invalid click cost two
    /// get_diagnostics round-trips AND filed a "Value Scan (First)" measurement whose
    /// duration was the validation, not a scan. The probe exists to accumulate evidence
    /// about what heavy operations cost; samples from operations that never ran are worse
    /// than no samples.
    /// </summary>
    [Fact]
    public async Task RejectedFirstScan_DoesNotOpenTheDiagnosticsProbe()
    {
        var (vm, fake) = MakeVm();
        // Changed is a Next-Scan predicate — First Scan rejects it before doing any work.
        vm.SelectedDataType = ValueScanDataType.Int32;
        vm.SelectedScanType = ValueScanType.Changed;
        vm.Value = "1";

        await vm.FirstScanCommand.ExecuteAsync(null);

        Assert.NotEqual("", vm.ErrorMessage);      // it really was rejected...
        Assert.Empty(fake.Begins);                 // ...and no scan started...
        Assert.Equal(0, fake.DiagnosticsCalls);    // ...so nothing was measured.
    }

    [Fact]
    public async Task AcceptedFirstScan_StillOpensTheDiagnosticsProbe()
    {
        // The other half: moving the probe must not have disabled it.
        var (vm, fake) = MakeVm();
        fake.NextBeginResult  = new ValueScanBeginResult  { SessionId = 1UL, Total = 1 };
        fake.NextWindowResult = new ValueScanWindowResult { SessionId = 1UL, Total = 1, FilteredTotal = 1 };
        vm.SelectedDataType = ValueScanDataType.Int32;
        vm.SelectedScanType = ValueScanType.Exact;
        vm.Value = "1";

        await vm.FirstScanCommand.ExecuteAsync(null);

        Assert.Single(fake.Begins);
        Assert.True(fake.DiagnosticsCalls > 0);
    }

    [Fact]
    public async Task GroupNewScan_ResetsTheBoundGroupSortPicker()
    {
        // The group mode carried the identical defect; the finding named only the single
        // side, so this pins the twin found by grepping at fix time.
        var (vm, _) = MakeVm();
        vm.SelectedGroupSortOption = vm.GroupSortOptions.First(o => o.Key == "value");
        vm.GroupSortDescending = true;

        await vm.GroupNewScanCommand.ExecuteAsync(null);

        Assert.Equal("scan", vm.SelectedGroupSortOption?.Key);
        Assert.False(vm.GroupSortDescending);
    }

    // --- V3-C: server-side window / filter / sort / paging ---

    [Fact]
    public async Task QueryCandidatesAsync_BuildsRequest_AndParsesWindow()
    {
        var svc = MakeService(out var pipe);
        JsonObject? captured = null;
        pipe.SetHandler(req =>
        {
            captured = (JsonObject)req.DeepClone();
            return new JsonObject
            {
                ["id"]             = req["id"]?.GetValue<int>() ?? 0,
                ["ok"]             = true,
                ["session_id"]     = 7UL,
                ["data_type"]      = "Int32",
                ["total"]          = 5000,
                ["filtered_total"] = 1200,
                ["offset"]         = 1000,
                ["count"]          = 2,
                ["candidates"]     = new JsonArray
                {
                    new JsonObject { ["addr"] = "0x10", ["field_name"] = "HP", ["value"] = "50" },
                    new JsonObject { ["addr"] = "0x20", ["field_name"] = "MP", ["value"] = "30" },
                },
            };
        });

        var res = await svc.QueryCandidatesAsync(7UL, 1000, 1000, "hp", "value", sortDesc: true,
            ct: TestContext.Current.CancellationToken);

        Assert.NotNull(captured);
        Assert.Equal("query_candidates", captured!["cmd"]?.GetValue<string>());
        Assert.Equal(7UL,     captured["session_id"]?.GetValue<ulong>());
        Assert.Equal(1000,    captured["offset"]?.GetValue<int>());
        Assert.Equal(1000,    captured["limit"]?.GetValue<int>());
        Assert.Equal("hp",    captured["filter"]?.GetValue<string>());
        Assert.Equal("value", captured["sort_key"]?.GetValue<string>());
        Assert.True(captured["sort_desc"]?.GetValue<bool>());

        Assert.Equal(5000, res.Total);
        Assert.Equal(1200, res.FilteredTotal);
        Assert.Equal(1000, res.Offset);
        Assert.Equal(2,    res.Candidates.Count);
        Assert.Equal("HP", res.Candidates[0].FieldName);
    }

    [Fact]
    public async Task QueryCandidatesAsync_OmitsDefaultFilterAndSort()
    {
        var svc = MakeService(out var pipe);
        JsonObject? captured = null;
        pipe.SetHandler(req =>
        {
            captured = (JsonObject)req.DeepClone();
            return new JsonObject
            {
                ["id"] = req["id"]?.GetValue<int>() ?? 0,
                ["ok"] = true,
                ["candidates"] = new JsonArray(),
            };
        });

        await svc.QueryCandidatesAsync(1UL, 0, 1000, ct: TestContext.Current.CancellationToken);

        Assert.False(captured!.ContainsKey("filter"));
        Assert.False(captured.ContainsKey("sort_key"));
        Assert.False(captured.ContainsKey("sort_desc"));
        Assert.False(captured.ContainsKey("exclude_classes"));
    }

    // --- P2: class-noise filter (server-side histogram + exclude_classes) ---

    [Fact]
    public async Task QueryCandidatesAsync_AttachesExcludeClasses()
    {
        var svc = MakeService(out var pipe);
        JsonObject? captured = null;
        pipe.SetHandler(req =>
        {
            captured = (JsonObject)req.DeepClone();
            return new JsonObject { ["id"] = req["id"]?.GetValue<int>() ?? 0, ["ok"] = true, ["candidates"] = new JsonArray() };
        });

        await svc.QueryCandidatesAsync(1UL, 0, 1000,
            excludeClasses: new[] { "WidgetBlueprintGeneratedClass", "SoundCue" },
            ct: TestContext.Current.CancellationToken);

        var arr = captured!["exclude_classes"] as JsonArray;
        Assert.NotNull(arr);
        Assert.Equal(2, arr!.Count);
        Assert.Equal("WidgetBlueprintGeneratedClass", arr[0]?.GetValue<string>());
        Assert.Equal("SoundCue", arr[1]?.GetValue<string>());
    }

    [Fact]
    public async Task QueryCandidatesAsync_OmitsExcludeWhenEmpty()
    {
        var svc = MakeService(out var pipe);
        JsonObject? captured = null;
        pipe.SetHandler(req =>
        {
            captured = (JsonObject)req.DeepClone();
            return new JsonObject { ["id"] = req["id"]?.GetValue<int>() ?? 0, ["ok"] = true, ["candidates"] = new JsonArray() };
        });

        await svc.QueryCandidatesAsync(1UL, 0, 1000,
            excludeClasses: System.Array.Empty<string>(), ct: TestContext.Current.CancellationToken);

        Assert.False(captured!.ContainsKey("exclude_classes"));
    }

    [Fact]
    public async Task BeginValueScanAsync_ParsesClassHistogram()
    {
        var svc = MakeService(out var pipe);
        pipe.SetHandler(req => new JsonObject
        {
            ["id"]             = req["id"]?.GetValue<int>() ?? 0,
            ["ok"]             = true,
            ["session_id"]     = 3UL,
            ["data_type"]      = "Int32",
            ["total"]          = 100,
            ["class_distinct"] = 7,
            ["class_histogram"] = new JsonArray
            {
                new JsonObject { ["class_name"] = "WidgetBlueprintGeneratedClass", ["count"] = 60 },
                new JsonObject { ["class_name"] = "BP_Pawn_C", ["count"] = 40 },
            },
            ["candidates"] = new JsonArray(),
        });

        var res = await svc.BeginValueScanAsync(
            ValueScanDataType.Int32, ValueScanType.Exact, "1",
            ct: TestContext.Current.CancellationToken);

        Assert.Equal(7, res.ClassDistinct);
        Assert.Equal(2, res.ClassHistogram.Count);
        Assert.Equal("WidgetBlueprintGeneratedClass", res.ClassHistogram[0].ClassName);
        Assert.Equal(60, res.ClassHistogram[0].Count);
    }

    [Fact]
    public async Task FirstScan_PopulatesClassFilterFromHistogram()
    {
        var (vm, fake) = MakeVm();
        fake.NextBeginResult = new ValueScanBeginResult
        {
            SessionId = 1UL, DataType = "Int32", Total = 100, ClassDistinct = 2,
            ClassHistogram = { new ClassCount { ClassName = "Widget", Count = 60 },
                               new ClassCount { ClassName = "Pawn",   Count = 40 } },
            Candidates = { new ValueCandidate { Value = "1" } },
        };
        vm.SelectedDataType = ValueScanDataType.Int32;
        vm.SelectedScanType = ValueScanType.Exact;
        vm.Value = "1";

        await vm.FirstScanCommand.ExecuteAsync(null);

        Assert.True(vm.ClassFilter.HasFacets);
        Assert.Equal(2, vm.ClassFilter.Facets.Count);
        Assert.Equal("Widget", vm.ClassFilter.Facets[0].ClassName);
        Assert.Equal(60, vm.ClassFilter.Facets[0].HitCount);
    }

    [Fact]
    public async Task TogglingClassFilter_QueriesServer_WithExcludeClasses()
    {
        var (vm, fake) = MakeVm();
        fake.NextBeginResult = new ValueScanBeginResult
        {
            SessionId = 1UL, DataType = "Int32", Total = 100, ClassDistinct = 2,
            ClassHistogram = { new ClassCount { ClassName = "Widget", Count = 60 },
                               new ClassCount { ClassName = "Pawn",   Count = 40 } },
            Candidates = { new ValueCandidate { Value = "1" } },
        };
        fake.NextWindowResult = new ValueScanWindowResult { SessionId = 1UL, Total = 100, FilteredTotal = 40 };
        vm.SelectedDataType = ValueScanDataType.Int32;
        vm.SelectedScanType = ValueScanType.Exact;
        vm.Value = "1";
        await vm.FirstScanCommand.ExecuteAsync(null);

        // Tick "Widget" -> the helper re-runs the window query with the exclude set.
        vm.ClassFilter.Facets.Single(r => r.ClassName == "Widget").Picked = true;
        await Task.Yield();

        Assert.NotNull(fake.LastQueryExclude);
        Assert.Contains("Widget", fake.LastQueryExclude!);
    }

    [Fact]
    public async Task DetectNoiseClassesAsync_BuildsRequest_AndParses()
    {
        var svc = MakeService(out var pipe);
        JsonObject? captured = null;
        pipe.SetHandler(req =>
        {
            captured = (JsonObject)req.DeepClone();
            return new JsonObject
            {
                ["id"] = req["id"]?.GetValue<int>() ?? 0, ["ok"] = true,
                ["classes"] = new JsonArray
                {
                    new JsonObject { ["class_name"] = "Widget", ["is_noise"] = true, ["reason"] = "engine base class" },
                    new JsonObject { ["class_name"] = "BP_Enemy_C", ["is_noise"] = false, ["reason"] = "" },
                },
            };
        });

        var res = await svc.DetectNoiseClassesAsync(new[] { "Widget", "BP_Enemy_C" },
            TestContext.Current.CancellationToken);

        Assert.Equal("detect_noise_classes", captured!["cmd"]?.GetValue<string>());
        Assert.Equal(2, (captured["class_names"] as JsonArray)!.Count);
        Assert.Equal(2, res.Count);
        Assert.True(res[0].IsNoise);
        Assert.Equal("engine base class", res[0].Reason);
        Assert.False(res[1].IsNoise);
    }

    [Fact]
    public async Task DetectNoiseClassesAsync_EmptyInput_NoPipeCall()
    {
        var svc = MakeService(out var pipe);
        bool called = false;
        pipe.SetHandler(req => { called = true; return new JsonObject { ["id"] = req["id"]?.GetValue<int>() ?? 0, ["ok"] = true }; });

        var res = await svc.DetectNoiseClassesAsync(System.Array.Empty<string>(),
            TestContext.Current.CancellationToken);

        Assert.Empty(res);
        Assert.False(called);   // short-circuits, no pipe traffic
    }

    [Fact]
    public async Task FirstScan_AfterExcludingClass_ResetsFilterForNewSession()
    {
        var (vm, fake) = MakeVm();
        fake.NextBeginResult = new ValueScanBeginResult
        {
            SessionId = 1UL, DataType = "Int32", Total = 100, ClassDistinct = 2,
            ClassHistogram = { new ClassCount { ClassName = "Widget", Count = 60 },
                               new ClassCount { ClassName = "Pawn",   Count = 40 } },
            Candidates = { new ValueCandidate { Value = "1" } },
        };
        fake.NextWindowResult = new ValueScanWindowResult { SessionId = 1UL, Total = 100, FilteredTotal = 40 };
        vm.SelectedDataType = ValueScanDataType.Int32;
        vm.SelectedScanType = ValueScanType.Exact;
        vm.Value = "1";
        await vm.FirstScanCommand.ExecuteAsync(null);
        vm.ClassFilter.Facets.Single(r => r.ClassName == "Widget").Picked = true;
        await Task.Yield();
        Assert.True(vm.ClassFilter.AnyExcluded);
        int queriesBefore = fake.Queries.Count;

        // A fresh First Scan (NO New Scan) for a different value: the retired
        // session must NOT leak its exclusions into the new one.
        fake.NextBeginResult = new ValueScanBeginResult
        {
            SessionId = 2UL, DataType = "Int32", Total = 5, ClassDistinct = 1,
            ClassHistogram = { new ClassCount { ClassName = "Orc", Count = 5 } },
            Candidates = { new ValueCandidate { Value = "2" } },
        };
        vm.Value = "2";
        await vm.FirstScanCommand.ExecuteAsync(null);

        Assert.False(vm.ClassFilter.AnyExcluded);                       // stale exclusion cleared
        Assert.Equal(new[] { "Orc" }, vm.ClassFilter.Facets.Select(f => f.ClassName));
        Assert.Equal(queriesBefore, fake.Queries.Count);               // default view -> inline page, no stale exclude query
    }

    [Fact]
    public async Task GroupFirstScan_AfterExcludingClass_ResetsFilterForNewSession()
    {
        var (vm, fake) = MakeVm();
        fake.NextGroupBeginResult = new GroupScanBeginResult
        {
            SessionId = 1UL, Total = 10, ClassDistinct = 1,
            ClassHistogram = { new ClassCount { ClassName = "WidgetX", Count = 10 } },
        };
        fake.NextGroupWindowResult = new GroupScanWindowResult { SessionId = 1UL, Total = 10, FilteredTotal = 0 };
        vm.GroupInputs[0].Value = "24";
        vm.GroupInputs[1].Value = "10";
        await vm.GroupFirstScanCommand.ExecuteAsync(null);
        vm.GroupClassFilter.Facets.Single(r => r.ClassName == "WidgetX").Picked = true;
        await Task.Yield();
        Assert.True(vm.GroupClassFilter.AnyExcluded);

        fake.NextGroupBeginResult = new GroupScanBeginResult
        {
            SessionId = 2UL, Total = 3, ClassDistinct = 1,
            ClassHistogram = { new ClassCount { ClassName = "BP_Boss_C", Count = 3 } },
        };
        vm.GroupInputs[0].Value = "1";
        vm.GroupInputs[1].Value = "2";
        await vm.GroupFirstScanCommand.ExecuteAsync(null);

        Assert.False(vm.GroupClassFilter.AnyExcluded);
        Assert.Equal(new[] { "BP_Boss_C" }, vm.GroupClassFilter.Facets.Select(f => f.ClassName));
    }

    private static async Task<(ValueSearchViewModel vm, FakeDumpService fake)> StartSessionAsync(int total, int inlineCount = 1)
    {
        var (vm, fake) = MakeVm();
        var begin = new ValueScanBeginResult { SessionId = 1UL, DataType = "Int32", Total = total };
        for (int i = 0; i < inlineCount; i++)
            begin.Candidates.Add(new ValueCandidate { Addr = $"0x{i:X}", Value = i.ToString() });
        fake.NextBeginResult = begin;
        vm.SelectedDataType = ValueScanDataType.Int32;
        vm.SelectedScanType = ValueScanType.Exact;
        vm.Value = "1";
        await vm.FirstScanCommand.ExecuteAsync(null);
        return (vm, fake);
    }

    [Fact]
    public async Task FirstScan_DefaultView_UsesInlinePage_NoQuery()
    {
        var (vm, fake) = await StartSessionAsync(total: 3, inlineCount: 1);
        // Default view (no filter, scan order) shows the inline first page with
        // no extra round-trip.
        Assert.Empty(fake.Queries);
        Assert.Single(vm.Candidates);
        Assert.Equal(3, vm.Total);
        Assert.Equal(3, vm.FilteredTotal);
        Assert.True(vm.HasMore);   // 1 of 3 loaded
    }

    [Fact]
    public async Task SortChange_QueriesServer_WithSortKey()
    {
        var (vm, fake) = await StartSessionAsync(total: 2, inlineCount: 1);
        fake.NextWindowResult = new ValueScanWindowResult
        {
            SessionId = 1UL, Total = 2, FilteredTotal = 2,
            Candidates = { new ValueCandidate { Value = "1" }, new ValueCandidate { Value = "2" } },
        };

        vm.SelectedSortOption = vm.SortOptions[1];  // "value"
        await Task.Yield();

        Assert.NotEmpty(fake.Queries);
        Assert.Equal("value", fake.Queries[^1].Item5);  // sortKey
        Assert.False(vm.HasMore);                        // 2 of 2
        Assert.Equal(2, vm.Candidates.Count);
    }

    [Fact]
    public async Task SortDescending_QueriesServer_WithDescFlag()
    {
        var (vm, fake) = await StartSessionAsync(total: 2, inlineCount: 2);
        fake.NextWindowResult = new ValueScanWindowResult
        {
            SessionId = 1UL, Total = 2, FilteredTotal = 2,
            Candidates = { new ValueCandidate { Value = "2" }, new ValueCandidate { Value = "1" } },
        };

        vm.SortDescending = true;
        await Task.Yield();

        Assert.NotEmpty(fake.Queries);
        Assert.True(fake.Queries[^1].Item6);  // sortDesc
    }

    [Fact]
    public async Task LoadMore_AppendsNextWindow_AtLoadedOffset()
    {
        var (vm, fake) = await StartSessionAsync(total: 3000, inlineCount: 1);
        Assert.True(vm.HasMore);
        fake.NextWindowResult = new ValueScanWindowResult
        {
            SessionId = 1UL, Total = 3000, FilteredTotal = 3000, Offset = 1,
            Candidates = { new ValueCandidate { Value = "b" }, new ValueCandidate { Value = "c" } },
        };

        await vm.LoadMoreCommand.ExecuteAsync(null);

        Assert.Equal(3, vm.Candidates.Count);          // 1 inline + 2 appended
        Assert.Equal(1, fake.Queries[^1].Item2);       // offset = previously-loaded count
    }

    // Poll until a condition holds (or a generous timeout elapses) instead of sleeping a
    // fixed interval — deterministic under CI load. A fixed Task.Delay past the debounce
    // flaked when a loaded runner scheduled the debounce continuation late (blocked a merge).
    // The caller still asserts afterward, so a real failure (condition never true) surfaces
    // as a normal assertion failure rather than a hang.
    private static async Task WaitUntilAsync(Func<bool> condition, int timeoutMs = 5000, int pollMs = 10)
    {
        for (int waited = 0; waited < timeoutMs && !condition(); waited += pollMs)
            await Task.Delay(pollMs, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task FilterChange_DebouncedServerQuery()
    {
        var (vm, fake) = await StartSessionAsync(total: 2, inlineCount: 1);
        fake.Queries.Clear();
        fake.NextWindowResult = new ValueScanWindowResult
        {
            SessionId = 1UL, Total = 2, FilteredTotal = 1,
            Candidates = { new ValueCandidate { Value = "1" } },
        };

        vm.FilterText = "hp";
        // Wait for the debounced (250ms) server query to fire — poll, don't fixed-delay.
        await WaitUntilAsync(() => fake.Queries.Count > 0);

        Assert.NotEmpty(fake.Queries);
        Assert.Equal("hp", fake.Queries[^1].Item4);  // filter
        Assert.Equal(1, vm.FilteredTotal);
    }

    [Fact]
    public async Task NewScan_ClearsWindowState()
    {
        var (vm, fake) = await StartSessionAsync(total: 3, inlineCount: 1);
        await vm.NewScanCommand.ExecuteAsync(null);

        Assert.Empty(vm.Candidates);
        Assert.Equal(0, vm.Total);
        Assert.Equal(0, vm.FilteredTotal);
        Assert.False(vm.HasMore);
        Assert.False(vm.HasSession);
    }

    [Fact]
    public void OpenInLiveWalker_RaisesNavigateWithOffsetAndFieldName()
    {
        var (vm, _) = MakeVm();
        (string addr, int off, string name)? nav = null;
        vm.NavigateToInstance += (a, o, n) => nav = (a, o, n);

        // Container hit: offset is the owning property, name carries "[N]".
        vm.OpenInLiveWalkerCommand.Execute(new ValueCandidate
        {
            InstanceAddr = "0x2000",
            FieldOffset  = 0x1C,
            FieldName    = "AttributeAugmentLevels.Value[2]",
        });
        Assert.NotNull(nav);
        Assert.Equal("0x2000", nav!.Value.addr);
        Assert.Equal(0x1C,     nav.Value.off);
        Assert.Equal("AttributeAugmentLevels.Value[2]", nav.Value.name);

        // No instance address → no navigation.
        nav = null;
        vm.OpenInLiveWalkerCommand.Execute(new ValueCandidate { InstanceAddr = "" });
        Assert.Null(nav);
    }

    [Fact]
    public async Task FirstScan_RejectsPrevValueScanType()
    {
        var (vm, fake) = MakeVm();
        vm.SelectedScanType = ValueScanType.Decreased;
        vm.Value = "100";

        await vm.FirstScanCommand.ExecuteAsync(null);

        Assert.False(vm.HasSession);
        Assert.Empty(fake.Begins);
        Assert.Contains("First Scan supports targeted predicates only", vm.ErrorMessage);
    }

    [Fact]
    public async Task FirstScan_RejectsEmptyValue()
    {
        var (vm, fake) = MakeVm();
        vm.SelectedScanType = ValueScanType.Exact;
        vm.Value = "";

        await vm.FirstScanCommand.ExecuteAsync(null);

        Assert.Empty(fake.Begins);
        Assert.Contains("Value is required", vm.ErrorMessage);
    }

    [Fact]
    public async Task FirstScan_BetweenRequiresValue2()
    {
        var (vm, fake) = MakeVm();
        vm.SelectedScanType = ValueScanType.Between;
        vm.Value = "10";
        vm.Value2 = "";

        await vm.FirstScanCommand.ExecuteAsync(null);

        Assert.Empty(fake.Begins);
        Assert.Contains("Between requires", vm.ErrorMessage);
    }

    [Fact]
    public async Task NextScan_PrevValueType_SendsNullValue()
    {
        var (vm, fake) = MakeVm();
        fake.NextBeginResult = new ValueScanBeginResult { SessionId = 7UL };
        vm.SelectedScanType = ValueScanType.Exact;
        vm.Value = "100";
        await vm.FirstScanCommand.ExecuteAsync(null);
        Assert.True(vm.HasSession);

        fake.NextRefineResult = new ValueScanRefineResult { SessionId = 7UL, Total = 5 };
        vm.SelectedScanType = ValueScanType.Changed;
        // Value field intentionally left at "100" — Changed must ignore it.

        await vm.NextScanCommand.ExecuteAsync(null);

        Assert.Single(fake.Refines);
        var (sid, st, val, val2, _, _) = fake.Refines[0];
        Assert.Equal(7UL, sid);
        Assert.Equal(ValueScanType.Changed, st);
        Assert.Null(val);
        Assert.Null(val2);
    }

    [Fact]
    public async Task NewScan_EndsSession_AndClearsCandidates()
    {
        var (vm, fake) = MakeVm();
        fake.NextBeginResult = new ValueScanBeginResult
        {
            SessionId = 9UL,
            Candidates = { new ValueCandidate { Addr = "0x1000" } }
        };
        vm.SelectedScanType = ValueScanType.Exact;
        vm.Value = "1";
        await vm.FirstScanCommand.ExecuteAsync(null);
        Assert.Single(vm.Candidates);

        await vm.NewScanCommand.ExecuteAsync(null);

        Assert.False(vm.HasSession);
        Assert.Equal(0UL, vm.SessionId);
        Assert.Empty(vm.Candidates);
        Assert.Single(fake.Ends);
        Assert.Equal(9UL, fake.Ends[0]);
    }

    [Fact]
    public async Task FirstScan_AutoEndsExistingSession_BeforeNewBegin()
    {
        // If the user clicks First Scan again without explicitly ending
        // the prior session, the VM must end it to avoid the DLL
        // accumulating orphan sessions until 5-min idle expiry.
        var (vm, fake) = MakeVm();
        fake.NextBeginResult = new ValueScanBeginResult { SessionId = 1UL };
        vm.SelectedScanType = ValueScanType.Exact;
        vm.Value = "1";
        await vm.FirstScanCommand.ExecuteAsync(null);
        Assert.Equal(1UL, vm.SessionId);

        fake.NextBeginResult = new ValueScanBeginResult { SessionId = 2UL };
        vm.Value = "2";
        await vm.FirstScanCommand.ExecuteAsync(null);

        Assert.Equal(2UL, vm.SessionId);
        Assert.Single(fake.Ends);
        Assert.Equal(1UL, fake.Ends[0]);
    }

    // ------------------------------------------------------------------
    // UX rule: ValueSearchPanel.axaml MUST surface the native-C++-fields
    // limitation. This is locked in by reading the AXAML source file at
    // test time and asserting the literal English text is still there.
    //
    // The wording lives in en.axaml — the panel uses StaticResource
    // str.VS.Banner. We check BOTH files so a rename of the resource
    // key without updating the panel still fails.
    //
    // Why a literal-text test: this is a project-memory UX rule (memory:
    // project_value_search_caveats). Without the assertion a future
    // refactor could "tidy up" the banner and silently strip the
    // limitation disclosure -- the user wouldn't know their scan was
    // blind to native fields.
    // ------------------------------------------------------------------

    private const string BannerExpected =
        "Native C++ fields (non-UPROPERTY) cannot be found here";

    private static string ReadProjectFile(string relativePath)
    {
        // Walk up from the test-bin directory to the repo root.
        var dir = AppContext.BaseDirectory;
        for (int i = 0; i < 10 && dir != null; ++i)
        {
            var candidate = Path.Combine(dir, "ui", "UE5DumpUI", relativePath);
            if (File.Exists(candidate)) return File.ReadAllText(candidate);
            dir = Path.GetDirectoryName(dir);
        }
        throw new FileNotFoundException(
            $"Could not locate ui/UE5DumpUI/{relativePath} from {AppContext.BaseDirectory}");
    }

    [Fact]
    public void Banner_LiteralText_IsPresentInEnAxaml()
    {
        var en = ReadProjectFile(Path.Combine("Resources", "Strings", "en.axaml"));
        Assert.Contains(BannerExpected, en);
        Assert.Contains("Use Cheat Engine's raw memory scan", en);
    }

    [Fact]
    public void Banner_IsReferencedByValueSearchPanel()
    {
        var panel = ReadProjectFile(Path.Combine("Views", "ValueSearchPanel.axaml"));
        Assert.Contains("str.VS.Banner", panel);
    }
}
