using UE5DumpUI.Core;
using UE5DumpUI.Models;
using UE5DumpUI.ViewModels;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// VM-level tests for the experimental "Detect Player Stats" panel (P4). Verifies
/// the confirmation pipeline: scorer → per-class live-instance probe → live-value
/// sanity → confidence ranking, plus one-probe-per-class batching and graceful
/// snapshot-signal degradation. Uses a stub IDumpService scoped to this file.
/// </summary>
public class DetectStatsViewModelTests
{
    private sealed class FakeDump : StubDumpService
    {
        public List<PropertySearchMatch> Matches { get; } = new();
        public Dictionary<string, string> InstanceAddrByClass { get; } = new();
        public Dictionary<string, List<LiveFieldValue>> WalkByAddr { get; } = new();
        public int FindInstancesCalls { get; private set; }

        public override Task<PropertySearchBatchResult> SearchPropertiesBatchAsync(
            string[] queries, string[]? types = null, bool gameOnly = true,
            int limitPerQuery = 200, CancellationToken ct = default)
        {
            var env = new PropertySearchQueryEnvelope
            {
                Query = "seed",
                MatchCount = Matches.Count,
                Results = new List<PropertySearchMatch>(Matches),
            };
            return Task.FromResult(new PropertySearchBatchResult
            {
                PerQuery = new List<PropertySearchQueryEnvelope> { env },
                Total = Matches.Count,
            });
        }

        public override Task<FindInstancesResult> FindInstancesAsync(
            string className, bool exactMatch = false, int limit = 500, bool newestFirst = false,
            string nameFilter = "", IReadOnlyList<string>? excludeClasses = null, CancellationToken ct = default)
        {
            FindInstancesCalls++;
            var list = new List<InstanceResult>();
            if (InstanceAddrByClass.TryGetValue(className, out var addr))
                list.Add(new InstanceResult { Address = addr, Name = className + "_0", ClassName = className });
            return Task.FromResult(new FindInstancesResult { Instances = list });
        }

        public override Task<InstanceWalkResult> WalkInstanceAsync(
            string addr, string? classAddr = null, int arrayLimit = 64, int previewLimit = 2,
            bool fillGaps = false, bool lean = false, CancellationToken ct = default)
        {
            var fields = WalkByAddr.TryGetValue(addr, out var f) ? f : new List<LiveFieldValue>();
            return Task.FromResult(new InstanceWalkResult { Address = addr, Fields = fields });
        }
    }

    private sealed class NoopLogger : ILoggingService
    {
        public void Info(string message) { }
        public void Warn(string message) { }
        public void Error(string message) { }
        public void Error(string message, Exception ex) { }
        public void Debug(string message) { }
        public void Info(string category, string message) { }
        public void Warn(string category, string message) { }
        public void Error(string category, string message) { }
        public void Error(string category, string message, Exception ex) { }
        public void Debug(string category, string message) { }
        public void StartProcessMirror(string processName) { }
        public void StopProcessMirror() { }
    }

    private static PropertySearchMatch M(string name, string cls,
        string type = "FloatProperty", int off = 0x100)
        => new()
        {
            PropName = name, ClassName = cls, DefiningClassName = cls,
            PropType = type, PropOffset = off, PropSize = 4,
        };

    private static LiveFieldValue F(string name, int off, string val, string type = "FloatProperty")
        => new() { Name = name, Offset = off, TypeName = type, TypedValue = val };

    private static DetectStatsViewModel Vm(FakeDump dump, ISnapshotStore? store = null)
        => new(dump, new NoopLogger(), store);

    [Fact]
    public async Task Detect_LivePlausibleField_IsConfirmed()
    {
        var dump = new FakeDump();
        dump.Matches.Add(M("Health", "PlayerCharacter", "FloatProperty", 0x100));
        dump.InstanceAddrByClass["PlayerCharacter"] = "0xAAAA";
        dump.WalkByAddr["0xAAAA"] = new() { F("Health", 0x100, "100") };

        var vm = Vm(dump);
        await vm.DetectCommand.ExecuteAsync(null);

        var row = Assert.Single(vm.Results);
        Assert.Equal("Health", row.PropName);
        Assert.True(row.LiveInstanceExists);
        Assert.True(row.ValuePlausible);
        Assert.Equal("100", row.LiveValue);
        Assert.True(row.IsConfirmed);
    }

    [Fact]
    public async Task Detect_NoLiveInstance_NotConfirmed()
    {
        var dump = new FakeDump();
        dump.Matches.Add(M("Gold", "SomeDataAsset", "IntProperty", 0x40));
        // No InstanceAddrByClass entry → FindInstances returns empty.

        var vm = Vm(dump);
        await vm.DetectCommand.ExecuteAsync(null);

        var row = Assert.Single(vm.Results);
        Assert.False(row.LiveInstanceExists);
        Assert.False(row.IsConfirmed);
    }

    [Fact]
    public async Task Detect_HugeValue_NotPlausible()
    {
        // A pointer-sized mis-read (>= 1e9) must fail the plausibility gate.
        var dump = new FakeDump();
        dump.Matches.Add(M("Health", "PlayerCharacter", "IntProperty", 0x100));
        dump.InstanceAddrByClass["PlayerCharacter"] = "0xAAAA";
        dump.WalkByAddr["0xAAAA"] = new() { F("Health", 0x100, "140700000000000", "IntProperty") };

        var vm = Vm(dump);
        await vm.DetectCommand.ExecuteAsync(null);

        var row = Assert.Single(vm.Results);
        Assert.True(row.LiveInstanceExists);
        Assert.False(row.ValuePlausible);
        Assert.False(row.IsConfirmed);
    }

    [Fact]
    public async Task Detect_SameClassMultipleFields_ProbesClassOnce_AndPairs()
    {
        var dump = new FakeDump();
        dump.Matches.Add(M("Health", "PlayerCharacter", "FloatProperty", 0x100));
        dump.Matches.Add(M("MaxHealth", "PlayerCharacter", "FloatProperty", 0x104));
        dump.InstanceAddrByClass["PlayerCharacter"] = "0xAAAA";
        dump.WalkByAddr["0xAAAA"] = new()
        {
            F("Health", 0x100, "80"),
            F("MaxHealth", 0x104, "100"),
        };

        var vm = Vm(dump);
        await vm.DetectCommand.ExecuteAsync(null);

        Assert.Equal(2, vm.Results.Count);
        Assert.Equal(1, dump.FindInstancesCalls);          // one probe for the shared class
        Assert.All(vm.Results, r => Assert.True(r.HasMaxSibling)); // Health + MaxHealth family
        Assert.All(vm.Results, r => Assert.True(r.IsConfirmed));
    }

    /// <summary>
    /// Past the 30-class probe cap a row must say "not checked", not "guess" — audit #5 AF2.
    ///
    /// Every confirmation signal is false for an unprobed row for the same reason it is false
    /// for a row the probe examined and rejected, so the two rendered identically and a real
    /// stat at rank 31 was indistinguishable from a disproven one.
    /// </summary>
    [Fact]
    public async Task Detect_PastTheClassProbeCap_RowsSayNotChecked_NotGuess()
    {
        var dump = new FakeDump();
        // 40 distinct classes, one candidate each — comfortably past MaxClassesProbed (30).
        // None gets a live instance, so probing cannot itself change the verdict; the ONLY
        // difference between the two groups is whether the probe ran.
        for (int i = 0; i < 40; i++)
            dump.Matches.Add(M("Health", $"Class{i:D2}", "FloatProperty", 0x100));

        var vm = Vm(dump);
        await vm.DetectCommand.ExecuteAsync(null);

        Assert.Equal(40, vm.Results.Count);
        Assert.Equal(30, dump.FindInstancesCalls);          // the cap really did bite

        var probed   = vm.Results.Where(r => r.WasProbed).ToList();
        var unprobed = vm.Results.Where(r => !r.WasProbed).ToList();
        Assert.Equal(30, probed.Count);
        Assert.Equal(10, unprobed.Count);

        // Neither group is confirmed — and that is exactly why they used to be
        // indistinguishable. The badge is what has to separate them.
        Assert.All(vm.Results, r => Assert.False(r.IsConfirmed));
        Assert.All(probed,   r => Assert.Equal("· guess", r.ConfirmBadge));
        Assert.All(unprobed, r => Assert.Equal("? not checked", r.ConfirmBadge));
        Assert.All(unprobed, r => Assert.Contains("not live-probed", r.SignalSummary));
        Assert.All(probed,   r => Assert.DoesNotContain("not live-probed", r.SignalSummary));

        // ...and the status line admits the second truncation, which was silent.
        Assert.Contains("30 of 40 classes live-probed", vm.StatusText);
    }

    [Fact]
    public async Task Detect_WithinTheCap_SaysNothingAboutUnprobedClasses()
    {
        // The other direction: the new suffix must not appear when every class WAS probed,
        // or it becomes a permanent warning nobody reads.
        var dump = new FakeDump();
        dump.Matches.Add(M("Health", "PlayerCharacter", "FloatProperty", 0x100));

        var vm = Vm(dump);
        await vm.DetectCommand.ExecuteAsync(null);

        Assert.DoesNotContain("live-probed", vm.StatusText);
        Assert.All(vm.Results, r => Assert.True(r.WasProbed));
    }

    [Fact]
    public async Task Detect_RanksConfirmedFirst()
    {
        var dump = new FakeDump();
        dump.Matches.Add(M("Gold", "SomeDataAsset", "IntProperty", 0x40));         // no live → guess
        dump.Matches.Add(M("Health", "PlayerCharacter", "FloatProperty", 0x100));  // live+plausible → confirmed
        dump.InstanceAddrByClass["PlayerCharacter"] = "0xAAAA";
        dump.WalkByAddr["0xAAAA"] = new() { F("Health", 0x100, "100") };

        var vm = Vm(dump);
        await vm.DetectCommand.ExecuteAsync(null);

        Assert.Equal(2, vm.Results.Count);
        Assert.True(vm.Results[0].IsConfirmed);   // confirmed floats to the top
        Assert.Equal("Health", vm.Results[0].PropName);
    }

    [Fact]
    public async Task Detect_NoCandidates_EmptyResultsWithNote()
    {
        var dump = new FakeDump(); // no matches
        var vm = Vm(dump);
        await vm.DetectCommand.ExecuteAsync(null);

        Assert.Empty(vm.Results);
        Assert.Contains("No candidate", vm.StatusText);
    }

    [Fact]
    public async Task Detect_FilterText_NarrowsResults()
    {
        var dump = new FakeDump();
        dump.Matches.Add(M("Health", "PlayerCharacter", "FloatProperty", 0x100));
        dump.Matches.Add(M("Gold", "InventoryComponent", "IntProperty", 0x40));
        dump.InstanceAddrByClass["PlayerCharacter"] = "0xAAAA";
        dump.InstanceAddrByClass["InventoryComponent"] = "0xBBBB";
        dump.WalkByAddr["0xAAAA"] = new() { F("Health", 0x100, "100") };
        dump.WalkByAddr["0xBBBB"] = new() { F("Gold", 0x40, "500", "IntProperty") };

        var vm = Vm(dump);
        await vm.DetectCommand.ExecuteAsync(null);
        Assert.Equal(2, vm.Results.Count);

        vm.FilterText = "gold";               // substring over property/class/category
        Assert.Single(vm.Results);
        Assert.Equal("Gold", vm.Results[0].PropName);

        vm.FilterText = "";                   // cleared → full set restored
        Assert.Equal(2, vm.Results.Count);
    }

    [Fact]
    public async Task Detect_SnapshotSignalWithoutStore_DegradesGracefully()
    {
        var dump = new FakeDump();
        dump.Matches.Add(M("Health", "PlayerCharacter", "FloatProperty", 0x100));
        dump.InstanceAddrByClass["PlayerCharacter"] = "0xAAAA";
        dump.WalkByAddr["0xAAAA"] = new() { F("Health", 0x100, "100") };

        var vm = Vm(dump, store: null);
        vm.UseSnapshotSignal = true;
        await vm.DetectCommand.ExecuteAsync(null);

        Assert.Single(vm.Results);                       // still detects
        Assert.Contains("unavailable", vm.StatusText);   // snapshot note, no throw
    }
}
