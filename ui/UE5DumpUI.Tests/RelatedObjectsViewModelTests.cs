using UE5DumpUI.Core;
using UE5DumpUI.Models;
using UE5DumpUI.ViewModels;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// VM-level tests for the Related Objects panel's Phase 2 current-target
/// auto-detect (Edel). Verifies the auto-pick-and-load happy path and the
/// graceful-degradation path (weak / unresolved → no auto-load).
/// Uses a stub IDumpService scoped to this file.
/// </summary>
public class RelatedObjectsViewModelTests
{
    private sealed class FakeDumpService : StubDumpService
    {
        public CurrentTargetResult NextTarget { get; set; } = new();
        public string? LastRelatedAddr { get; private set; }
        public int RelatedCallCount { get; private set; }

        public override Task<CurrentTargetResult> DetectCurrentTargetAsync(
            int maxCandidates = 8, CancellationToken ct = default)
            => Task.FromResult(NextTarget);

        public override Task<RelatedObjectsResult> GetRelatedObjectsAsync(
            string addr, int maxResults = 128, CancellationToken ct = default)
        {
            RelatedCallCount++;
            LastRelatedAddr = addr;
            return Task.FromResult(new RelatedObjectsResult
            {
                QueryAddress = addr,
                Related = new List<RelatedObject>
                {
                    new() { Address = addr, Relation = "Self", ClassName = "BP_Enemy_C" },
                },
            });
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

    private static RelatedObjectsViewModel CreateVm(FakeDumpService dump)
        => new(dump, new NoopLogger(), new MockPlatformService(System.IO.Path.GetTempPath()));

    [Fact]
    public async Task DetectTarget_Resolved_AutoLoadsTopCandidate()
    {
        var dump = new FakeDumpService
        {
            NextTarget = new CurrentTargetResult
            {
                Resolved = true,
                PlayerPawn = "0x7FF6BB00",
                Note = "Detected target: Enemy_0 (BP_Enemy_C) — score 95 via CurrentTarget.",
                Candidates = new List<TargetCandidate>
                {
                    new() { Address = "0x7FF6CC00", Name = "Enemy_0", ClassName = "BP_Enemy_C",
                            Score = 95, FieldName = "CurrentTarget" },
                    new() { Address = "0x7FF6DD00", Name = "Ally_0", ClassName = "BP_Ally_C",
                            Score = 45, FieldName = "FocusActor" },
                },
            },
        };
        var vm = CreateVm(dump);

        await vm.DetectTargetCommand.ExecuteAsync(null);

        Assert.True(vm.HasCandidates);
        Assert.Equal(2, vm.TargetCandidates.Count);
        Assert.Same(vm.TargetCandidates[0], vm.SelectedCandidate);   // auto-pick = top
        // The top candidate was loaded into the related graph.
        Assert.Equal(1, dump.RelatedCallCount);
        Assert.Equal("0x7FF6CC00", dump.LastRelatedAddr);
        Assert.Equal("0x7FF6CC00", vm.TargetAddress);
        Assert.Single(vm.Related);
    }

    [Fact]
    public async Task DetectTarget_Unresolved_ShowsNoteAndDoesNotAutoLoad()
    {
        var dump = new FakeDumpService
        {
            NextTarget = new CurrentTargetResult
            {
                Resolved = false,
                Note = "Player resolved but no target-like reference found.",
                Candidates = new List<TargetCandidate>(),
            },
        };
        var vm = CreateVm(dump);

        await vm.DetectTargetCommand.ExecuteAsync(null);

        Assert.False(vm.HasCandidates);
        Assert.Empty(vm.TargetCandidates);
        Assert.Equal(0, dump.RelatedCallCount);          // nothing auto-loaded
        Assert.Equal("", vm.TargetAddress);
        Assert.StartsWith("Player resolved", vm.StatusText);
    }

    [Fact]
    public async Task DetectTarget_WeakCandidates_ShownButNotAutoLoaded()
    {
        var dump = new FakeDumpService
        {
            NextTarget = new CurrentTargetResult
            {
                Resolved = false,   // weak: candidates exist but top score not positive
                Note = "Only weak candidates (no clear target field). Showing best guesses — verify before trusting.",
                Candidates = new List<TargetCandidate>
                {
                    new() { Address = "0x7FF6EE00", Name = "Widget_0", ClassName = "UUserWidget",
                            Score = -10, FieldName = "TargetWidget" },
                },
            },
        };
        var vm = CreateVm(dump);

        await vm.DetectTargetCommand.ExecuteAsync(null);

        Assert.True(vm.HasCandidates);                   // shown for manual pick
        Assert.Single(vm.TargetCandidates);
        Assert.Equal(0, dump.RelatedCallCount);          // but NOT auto-loaded
        Assert.Null(vm.SelectedCandidate);
    }

    // ---- [W4-RELATED-RACE] two overlapping loads must not concatenate their graphs ----
    //
    // LoadAsync cleared before its await and appended after, with no generation ticket -- the only VM in
    // its cluster without one. A handoff or a candidate pick landing while a load was in flight made both
    // pass their Clear() and both Add(): object A's graph concatenated with B's under one header.
    // ⛔ `if (IsBusy) return;` is not the fix: DetectTargetAsync sets IsBusy, then awaits this very load.

    /// <summary>Each call parks on its own gate, so a test decides the order the loads finish in.</summary>
    private sealed class GatedDumpService : StubDumpService
    {
        public readonly Dictionary<string, TaskCompletionSource<RelatedObjectsResult>> Gates = new();
        public readonly HashSet<string> Instant = new();   // addresses whose load returns at once
        public readonly List<string> Calls = new();
        public TaskCompletionSource<CurrentTargetResult>? DetectGate;

        public override Task<RelatedObjectsResult> GetRelatedObjectsAsync(
            string addr, int maxResults = 128, CancellationToken ct = default)
        {
            Calls.Add(addr);
            if (Instant.Contains(addr)) return Task.FromResult(Graph(addr, "BP_Instant_C"));
            var tcs = new TaskCompletionSource<RelatedObjectsResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            Gates[addr] = tcs;
            return tcs.Task;
        }

        public override Task<CurrentTargetResult> DetectCurrentTargetAsync(
            int maxCandidates = 8, CancellationToken ct = default)
        {
            DetectGate = new TaskCompletionSource<CurrentTargetResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            return DetectGate.Task;
        }
    }

    private static RelatedObjectsResult Graph(string addr, string cls) => new()
    {
        QueryAddress = addr,
        Related = new List<RelatedObject>
        {
            new() { Address = addr,       Relation = "Self",      ClassName = cls },
            new() { Address = addr + "0", Relation = "Component", ClassName = cls + "Comp" },
        },
    };

    private static RelatedObjectsViewModel CreateVm(GatedDumpService dump)
        => new(dump, new NoopLogger(), new MockPlatformService(System.IO.Path.GetTempPath()));

    [Fact]
    public async Task OverlappingLoads_ShowOnlyTheNewestGraph()
    {
        var dump = new GatedDumpService();
        var vm = CreateVm(dump);

        var first  = vm.LoadForAddressAsync("0xA");   // parks on its gate
        var second = vm.LoadForAddressAsync("0xB");   // a handoff while the first is in flight
        dump.Gates["0xB"].SetResult(Graph("0xB", "BP_B_C"));
        await second;
        dump.Gates["0xA"].SetResult(Graph("0xA", "BP_A_C"));   // the stale one lands LAST
        await first;

        Assert.Equal(2, vm.Related.Count);
        Assert.All(vm.Related, r => Assert.StartsWith("0xB", r.Address));
        Assert.Equal("BP_B_C", vm.QueryClassName);
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task ASupersededLoad_DoesNotClearBusyUnderTheNewerOne()
    {
        var dump = new GatedDumpService();
        var vm = CreateVm(dump);

        var first  = vm.LoadForAddressAsync("0xA");
        var second = vm.LoadForAddressAsync("0xB");
        dump.Gates["0xA"].SetResult(Graph("0xA", "BP_A_C"));   // the stale one lands FIRST
        await first;
        Assert.True(vm.IsBusy);                                  // B is still in flight
        Assert.Empty(vm.Related);                                // and A drew nothing

        dump.Gates["0xB"].SetResult(Graph("0xB", "BP_B_C"));
        await second;
        Assert.False(vm.IsBusy);
        Assert.All(vm.Related, r => Assert.StartsWith("0xB", r.Address));
    }

    [Fact]
    public async Task ASupersededLoadThatFails_DoesNotOverwriteTheNewerStatus()
    {
        var dump = new GatedDumpService();
        var vm = CreateVm(dump);

        var first  = vm.LoadForAddressAsync("0xA");
        var second = vm.LoadForAddressAsync("0xB");
        dump.Gates["0xB"].SetResult(Graph("0xB", "BP_B_C"));
        await second;
        string newest = vm.StatusText;
        dump.Gates["0xA"].SetException(new InvalidOperationException("pipe gone"));
        await first;

        Assert.Equal(newest, vm.StatusText);
        Assert.DoesNotContain("Error", vm.StatusText);
    }

    [Fact]
    public async Task ALoadLandingAfterADisconnect_IsDropped()
    {
        // X5 promises a reconnect never shows the previous game's addresses; a load in flight at
        // disconnect repopulated the grid with them.
        var dump = new GatedDumpService();
        var vm = CreateVm(dump);

        var load = vm.LoadForAddressAsync("0xA");
        vm.ClearOnDisconnect();
        dump.Gates["0xA"].SetResult(Graph("0xA", "BP_A_C"));
        await load;

        Assert.Empty(vm.Related);
        Assert.Equal("", vm.QueryClassName);
    }

    // ---- review of c1c30d51: the busy flag, and the un-ticketed Detect ----

    private static CurrentTargetResult OneCandidate(bool resolved) => new()
    {
        Resolved = resolved, Note = "Detected target: X",
        Candidates = new List<TargetCandidate>
        {
            new() { Address = "0xX", Name = "X", ClassName = "BP_X_C", Score = resolved ? 90 : 0 },
        },
    };

    [Fact]
    public async Task ADisconnectDuringALoad_LeavesTheBusyFlagClear()
    {
        // ClearOnDisconnect bumped the generation but never took over IsBusy, and the superseded load's
        // finally skips it by design -- so a load in flight at disconnect left it stuck on.
        var dump = new GatedDumpService();
        var vm = CreateVm(dump);

        var load = vm.LoadForAddressAsync("0xA");
        vm.ClearOnDisconnect();
        dump.Gates["0xA"].SetResult(Graph("0xA", "BP_A_C"));
        await load;

        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task AHandoffDuringDetect_KeepsItsGraphAndItsBusyFlag()
    {
        var dump = new GatedDumpService();
        dump.Instant.Add("0xX");
        var vm = CreateVm(dump);

        var detect = vm.DetectTargetCommand.ExecuteAsync(null);   // parks on the detector
        var handoff = vm.LoadForAddressAsync("0xB");              // the user hands off B meanwhile
        dump.DetectGate!.SetResult(OneCandidate(resolved: true));
        await detect;                                              // the OLDER action lands first

        Assert.True(vm.IsBusy);                                    // B is still loading
        Assert.DoesNotContain("0xX", dump.Calls);                  // and Detect did not auto-load over it

        dump.Gates["0xB"].SetResult(Graph("0xB", "BP_B_C"));
        await handoff;
        Assert.False(vm.IsBusy);
        Assert.All(vm.Related, r => Assert.StartsWith("0xB", r.Address));
    }

    [Fact]
    public async Task ADetectLandingAfterADisconnect_IsDropped()
    {
        var dump = new GatedDumpService();
        var vm = CreateVm(dump);

        var detect = vm.DetectTargetCommand.ExecuteAsync(null);
        vm.ClearOnDisconnect();
        dump.DetectGate!.SetResult(OneCandidate(resolved: false));
        await detect;

        Assert.Empty(vm.TargetCandidates);
        Assert.False(vm.HasCandidates);
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public async Task ADetect_SupersedesAnEarlierLoad_EvenWhenItFindsNothing()
    {
        // The latest action owns the panel: a load handed off BEFORE Detect must not land over Detect's
        // result, nor clear the busy state Detect now owns.
        var dump = new GatedDumpService();
        var vm = CreateVm(dump);

        var load = vm.LoadForAddressAsync("0xA");
        var detect = vm.DetectTargetCommand.ExecuteAsync(null);
        dump.DetectGate!.SetResult(OneCandidate(resolved: false));   // nothing to auto-load
        await detect;
        dump.Gates["0xA"].SetResult(Graph("0xA", "BP_A_C"));         // the earlier load lands last
        await load;

        Assert.Empty(vm.Related);
        Assert.Single(vm.TargetCandidates);
        Assert.False(vm.IsBusy);
    }
}
