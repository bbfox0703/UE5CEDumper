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
