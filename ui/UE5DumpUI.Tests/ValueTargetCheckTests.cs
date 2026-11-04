using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UE5DumpUI.Models;
using UE5DumpUI.Services;
using UE5DumpUI.ViewModels;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// [VALUESCAN-NONFINITE-TARGET] Infinity or 1e400 as a numeric target gave different answers live and in the snapshot
/// search: the DLL's stod throws on "1e400" and its float leaves take an infinity that GroupMatch's TargetFitsWidth
/// refuses, while .NET parses "1e400" as +Infinity. Nonsense input on both sides, so it is refused before either
/// matcher sees it, in every place a numeric target is typed.
/// </summary>
public class ValueTargetCheckTests
{
    [Theory]
    [InlineData("Infinity")]
    [InlineData("-Infinity")]
    [InlineData("1e400")]
    [InlineData("-1e400")]
    [InlineData("NaN")]
    [InlineData("∞")]
    public void ANonFiniteNumericTarget_IsRefused(string value)
    {
        foreach (var dt in new[] { ValueScanDataType.Float, ValueScanDataType.Double, ValueScanDataType.Int32,
                                   ValueScanDataType.NumericAll, ValueScanDataType.NumericNoByte })
            Assert.NotNull(ValueTargetCheck.NonFiniteError(dt, value));
    }

    // Review round 2: the DLL reads a target with std::stod, whose strtod family takes C99's spellings that .NET's
    // invariant parse does not -- "inf" reached the live matcher as +inf ("Smaller than inf" matched every leaf) while
    // the snapshot store answered "enter a numeric value". The check refuses what either side would read as non-finite.
    [Theory]
    [InlineData("inf")]
    [InlineData("-INF")]
    [InlineData("+Inf")]
    [InlineData("infinity")]
    [InlineData("nan")]
    [InlineData("NAN(ind)")]
    [InlineData("-nan(0x7ff8)")]
    public void AC99NonFiniteSpelling_IsRefused(string value)
        => Assert.NotNull(ValueTargetCheck.NonFiniteError(ValueScanDataType.NumericNoByte, value));

    [Theory]
    [InlineData("infra")]
    [InlineData("nano")]
    [InlineData("1inf")]
    public void AWordThatOnlyStartsLikeOne_IsNotTheChecksToRefuse(string value)
        => Assert.Null(ValueTargetCheck.NonFiniteError(ValueScanDataType.NumericNoByte, value));

    [Theory]
    [InlineData("1.5")]
    [InlineData("-3")]
    [InlineData("1e30")]
    [InlineData("100,000")]
    [InlineData("")]
    [InlineData(null)]
    public void AFiniteTargetOrNone_Passes(string? value)
        => Assert.Null(ValueTargetCheck.NonFiniteError(ValueScanDataType.Float, value));

    [Fact]
    public void AStringScan_TakesTheWordInfinity()
        => Assert.Null(ValueTargetCheck.NonFiniteError(ValueScanDataType.FString, "Infinity"));

    [Fact]
    public void TheSecondValue_IsCheckedToo()
        => Assert.NotNull(ValueTargetCheck.NonFiniteError(ValueScanDataType.Float, "1", "1e400"));

    // Review round 2: the check read every box, shown or not. Value2 is sent only for Between and a prev-value type
    // (Changed / Unchanged / Increased / Decreased) sends no Value, so a value left in a hidden box blocked a valid
    // scan and named a value the user could not see -- and the snapshot match checked Value on prev-value rows too.

    [Theory]
    [InlineData(ValueScanType.Bigger, "0", "Infinity")]
    [InlineData(ValueScanType.Exact, "1", "1e400")]
    [InlineData(ValueScanType.Changed, "1e400", "")]
    [InlineData(ValueScanType.Increased, "NaN", "NaN")]
    public void AHiddenBox_IsNotChecked(ValueScanType scanType, string value, string value2)
        => Assert.Null(ValueTargetCheck.NonFiniteTargetError(ValueScanDataType.Float, scanType, value, value2));

    [Theory]
    [InlineData(ValueScanType.Between, "0", "Infinity")]
    [InlineData(ValueScanType.Bigger, "1e400", "")]
    public void AShownBox_IsStillChecked(ValueScanType scanType, string value, string value2)
        => Assert.NotNull(ValueTargetCheck.NonFiniteTargetError(ValueScanDataType.Float, scanType, value, value2));

    [Fact]
    public async Task FirstScan_WithANonFiniteValueLeftInTheHiddenSecondBox_Runs()
    {
        var dump = new CountingScanDump();
        var vm = new ValueSearchViewModel(dump, new MockLoggingService())
        {
            SelectedDataType = ValueScanDataType.Float, SelectedScanType = ValueScanType.Bigger, Value = "0",
            Value2 = "Infinity",
        };

        await vm.FirstScanCommand.ExecuteAsync(null);

        Assert.Equal(1, dump.Begins);
    }

    [Fact]
    public async Task SnapshotGroupMatch_OfAChangedRow_IgnoresItsHiddenValue()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"UE5DumpNonFinite_{System.Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var vm = new SnapshotViewModel(new StubDumpService(), new SnapshotStore(new MockPlatformService(dir)),
                                           new MockLoggingService());
            vm.GroupSnapshot = new SnapshotMeta { Id = 1 };
            vm.GroupInputs[0].DataType = ValueScanDataType.Float;
            vm.GroupInputs[0].ScanType = ValueScanType.Changed;
            vm.GroupInputs[0].Value = "1e400";   // hidden on a Changed row, and never sent
            vm.GroupInputs[1].Value = "5";

            await vm.RunGroupMatchCommand.ExecuteAsync(null);

            Assert.DoesNotContain("1e400", vm.ErrorMessage ?? "");
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    private sealed class CountingScanDump : StubDumpService
    {
        public int Begins;
        public override Task<ValueScanBeginResult> BeginValueScanAsync(
            ValueScanDataType dataType, ValueScanType scanType, string value, string? value2 = null,
            bool gameOnly = true, int maxResults = 50000, FloatRoundMode roundMode = FloatRoundMode.Round,
            bool caseSensitive = false, bool parallel = true, bool batchRead = true, bool deep = false,
            bool nativeC = false, bool newestFirst = false, int pageSize = 1000, int deadlineMs = 15000,
            bool autoSkipNoise = false, CancellationToken ct = default)
        {
            Begins++;
            return Task.FromResult(new ValueScanBeginResult { SessionId = 1, Total = 0 });
        }

        public override Task EndValueScanAsync(ulong sessionId, CancellationToken ct = default) => Task.CompletedTask;

        public int GroupBegins, Refines, GroupRefines;

        public override Task<GroupScanBeginResult> BeginGroupScanAsync(System.Collections.Generic.IReadOnlyList<GroupSlotInput> slots,
            bool gameOnly = true, int maxResults = 50000, bool deep = false, bool crossObject = false, bool nativeC = false,
            bool newestFirst = false, int pageSize = 1000, int deadlineMs = 15000, bool autoSkipNoise = false,
            FloatRoundMode roundMode = FloatRoundMode.Round, int perSlotCap = 256, CancellationToken ct = default)
        {
            GroupBegins++;
            return Task.FromResult(new GroupScanBeginResult { SessionId = 2 });
        }

        public override Task<ValueScanRefineResult> RefineValueScanAsync(ulong sessionId, ValueScanType scanType,
            string? value = null, string? value2 = null, FloatRoundMode roundMode = FloatRoundMode.Round,
            bool caseSensitive = false, int pageSize = 1000, CancellationToken ct = default)
        {
            Refines++;
            return Task.FromResult(new ValueScanRefineResult());
        }

        public override Task<GroupScanRefineResult> RefineGroupScanAsync(ulong sessionId,
            System.Collections.Generic.IReadOnlyList<GroupSlotInput> slots, int pageSize = 1000,
            FloatRoundMode roundMode = FloatRoundMode.Round, CancellationToken ct = default)
        {
            GroupRefines++;
            return Task.FromResult(new GroupScanRefineResult());
        }

        public override Task EndGroupScanAsync(ulong sessionId, CancellationToken ct = default) => Task.CompletedTask;
    }

    // Review round 2: Next Scan and group Next Scan refuse too, and nothing held them -- removing either check left every
    // test green. Each asserts its session is live first, so a refusal cannot pass by the command simply returning.

    [Fact]
    public async Task NextScan_OfANonFiniteTarget_SendsNothing_AndSaysWhy()
    {
        var dump = new CountingScanDump();
        var vm = new ValueSearchViewModel(dump, new MockLoggingService())
        {
            SelectedDataType = ValueScanDataType.Float, SelectedScanType = ValueScanType.Exact, Value = "1",
        };
        await vm.FirstScanCommand.ExecuteAsync(null);
        Assert.True(vm.HasSession);

        vm.Value = "1e400";
        await vm.NextScanCommand.ExecuteAsync(null);

        Assert.Equal(0, dump.Refines);
        Assert.Contains("1e400", vm.ErrorMessage);
    }

    [Fact]
    public async Task GroupNextScan_OfANonFiniteRow_SendsNothing_AndSaysWhy()
    {
        var dump = new CountingScanDump();
        var vm = new ValueSearchViewModel(dump, new MockLoggingService());
        vm.GroupInputs[0].DataType = ValueScanDataType.Float;
        vm.GroupInputs[0].Value = "1";
        vm.GroupInputs[1].Value = "5";
        await vm.GroupFirstScanCommand.ExecuteAsync(null);
        Assert.True(vm.HasGroupSession);

        vm.GroupInputs[0].Value = "Infinity";
        await vm.GroupNextScanCommand.ExecuteAsync(null);

        Assert.Equal(0, dump.GroupRefines);
        Assert.Contains("Infinity", vm.ErrorMessage);
    }

    [Fact]
    public async Task FirstScan_OfANonFiniteTarget_SendsNothing_AndSaysWhy()
    {
        var dump = new CountingScanDump();
        var vm = new ValueSearchViewModel(dump, new MockLoggingService())
        {
            SelectedDataType = ValueScanDataType.Float, SelectedScanType = ValueScanType.Exact, Value = "1e400",
        };

        await vm.FirstScanCommand.ExecuteAsync(null);

        Assert.Equal(0, dump.Begins);
        Assert.Contains("1e400", vm.ErrorMessage);
    }

    [Fact]
    public async Task GroupFirstScan_OfANonFiniteRow_SendsNothing()
    {
        var dump = new CountingScanDump();
        var vm = new ValueSearchViewModel(dump, new MockLoggingService());
        vm.GroupInputs[0].DataType = ValueScanDataType.Float;
        vm.GroupInputs[0].Value = "Infinity";
        vm.GroupInputs[1].Value = "5";

        await vm.GroupFirstScanCommand.ExecuteAsync(null);

        Assert.Equal(0, dump.GroupBegins);
        Assert.Contains("Infinity", vm.ErrorMessage);
    }

    [Fact]
    public async Task SnapshotGroupMatch_OfANonFiniteRow_AsksTheStoreNothing()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"UE5DumpNonFinite_{System.Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var vm = new SnapshotViewModel(new StubDumpService(), new SnapshotStore(new MockPlatformService(dir)),
                                           new MockLoggingService());
            vm.GroupSnapshot = new SnapshotMeta { Id = 1 };
            vm.GroupInputs[0].DataType = ValueScanDataType.Float;
            vm.GroupInputs[0].Value = "1e400";
            vm.GroupInputs[1].Value = "5";

            await vm.RunGroupMatchCommand.ExecuteAsync(null);

            Assert.Contains("1e400", vm.ErrorMessage);
            Assert.Empty(vm.GroupCandidates);
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }
}
