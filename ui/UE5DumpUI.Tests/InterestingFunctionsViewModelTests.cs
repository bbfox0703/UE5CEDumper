using System.Linq;
using UE5DumpUI.Core;
using UE5DumpUI.Models;
using UE5DumpUI.Services;
using UE5DumpUI.ViewModels;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// VM-level tests for the Interesting Functions Finder. Exercises:
/// - Load -> score -> sort pipeline
/// - Filter inputs (FilterText / CategoryFilter / ShowAll) and how
///   they interact with the InterestingThreshold cutoff
/// - Cross-tab navigation events fire with the right payload
///
/// Uses a hand-rolled stub IDumpService scoped to this test file.
/// </summary>
public class InterestingFunctionsViewModelTests
{
    // ------------------------------------------------------------------
    // Stub IDumpService -- subclass the existing StubDumpService and
    // override only ListAllFunctionsAsync so the rest of the surface
    // throws NotImplementedException (catches accidental calls cleanly).
    // ------------------------------------------------------------------

    private sealed class FakeDumpService : StubDumpService
    {
        public AllFunctionsResult NextResult { get; set; } = new();
        public bool LastGameOnly { get; private set; }
        public int CallCount { get; private set; }

        public override Task<AllFunctionsResult> ListAllFunctionsAsync(
            bool gameOnly = true, int limit = 100000, CancellationToken ct = default)
        {
            CallCount++;
            LastGameOnly = gameOnly;
            return Task.FromResult(NextResult);
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

    // ------------------------------------------------------------------
    // Helper: build a synthetic AllFunctionEntry list spanning all 5
    // categories + a few low-score noise entries so we can verify the
    // threshold filter independently of the categorisation logic.
    // ------------------------------------------------------------------

    // ClassAddr is populated because list_all_functions supplies it per row — the Copy AA
    // Script handoff carries it so the handler never re-derives an address out of the
    // CAPPED list_classes page (audit #5 X2).
    private static List<AllFunctionEntry> BuildSampleEntries() => new()
    {
        // High-score, multi-bucket: Inventory category, score ~10
        new() { ClassName="PlayerCharacter", ClassAddr="0x1000", FuncName="AddMoney",
                FunctionFlags=0x0400_0000, NumParms=2, ParmsSize=5 },
        // Stats
        new() { ClassName="PlayerState", ClassAddr="0x2000", FuncName="SetMaxHealth",
                FunctionFlags=0x0400_0000, NumParms=2, ParmsSize=4 },
        // Movement
        new() { ClassName="MyPawn", ClassAddr="0x3000", FuncName="TeleportPlayer",
                FunctionFlags=0x0400_0000, NumParms=4, ParmsSize=12 },
        // Combat
        new() { ClassName="WeaponComp", ClassAddr="0x4000", FuncName="FireWeapon",
                FunctionFlags=0x0400_0000, NumParms=1, ParmsSize=4 },
        // Utility
        new() { ClassName="GameMode", ClassAddr="0x5000", FuncName="SaveGame",
                FunctionFlags=0x0400_0000, NumParms=1, ParmsSize=4 },
        // Noise -- below threshold
        new() { ClassName="AnimNotify", ClassAddr="0x6000", FuncName="DoTick",
                FunctionFlags=0, NumParms=0, ParmsSize=0 },
        new() { ClassName="ParticleSystem", ClassAddr="0x7000", FuncName="UpdateInternal",
                FunctionFlags=0, NumParms=0, ParmsSize=0 },
    };

    private static AllFunctionsResult BuildResult(List<AllFunctionEntry> entries)
        => new()
        {
            Total          = entries.Count,
            ScannedObjects = 1000,
            ScannedClasses = 5,
            TotalFunctions = entries.Count,
            Functions      = entries,
        };

    private static (InterestingFunctionsViewModel vm, FakeDumpService dump) MakeVm(
        List<AllFunctionEntry>? entries = null)
    {
        var dump = new FakeDumpService
        {
            NextResult = BuildResult(entries ?? BuildSampleEntries()),
        };
        var vm = new InterestingFunctionsViewModel(dump, new NoopLogger());
        return (vm, dump);
    }

    /// <summary>Await the re-score the VM ITSELF started, with a real deadline.
    ///
    /// Calling RescoreAsync() from a test instead used to look equivalent, but the
    /// GameplayActions setter is fire-and-forget: a second call did not replace that
    /// task, it RACED it. Both end in ApplyFilter(), which Clear()s and refills
    /// Results, so the assertion could enumerate a half-rebuilt — or momentarily
    /// EMPTY — collection. Measured 50/4000 in-process iterations before this fix.
    ///
    /// Bounded rather than a bare await because a hang is not a test result: the
    /// 10s ceiling turns "the seam was never assigned" into a named failure instead
    /// of a suite that sits there until the CI job is killed.</summary>
    private static async Task DrainAsync(Task? pending, string what)
    {
        Assert.True(pending is not null, $"{what}: the VM never started it (seam unassigned)");
        var finished = await Task.WhenAny(
            pending!, Task.Delay(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
        Assert.True(ReferenceEquals(finished, pending), $"{what} did not finish within 10s");
        await pending!;   // observe any fault it captured
    }

    // ==================================================================
    // Load + scoring
    // ==================================================================

    [Fact]
    public async Task LoadAsync_PopulatesResultsSortedByScoreDescending()
    {
        var (vm, _) = MakeVm();
        await vm.LoadCommand.ExecuteAsync(null);

        Assert.NotEmpty(vm.Results);
        // First result should have the highest score (FilterText="" + CategoryFilter=null
        // so threshold cutoff applies but ShowAll=false -> only above-threshold).
        for (int i = 1; i < vm.Results.Count; i++)
        {
            Assert.True(vm.Results[i - 1].FinalScore >= vm.Results[i].FinalScore,
                $"Results not sorted by score descending: " +
                $"index {i - 1}={vm.Results[i - 1].FinalScore} vs index {i}={vm.Results[i].FinalScore}");
        }
    }

    [Fact]
    public async Task LoadAsync_DefaultThreshold_HidesNoiseEntries()
    {
        var (vm, _) = MakeVm();
        await vm.LoadCommand.ExecuteAsync(null);

        // The two noise entries (AnimNotify.DoTick + ParticleSystem.UpdateInternal)
        // should be hidden (negative scores due to class penalty).
        Assert.DoesNotContain(vm.Results, r => r.FuncName == "DoTick");
        Assert.DoesNotContain(vm.Results, r => r.FuncName == "UpdateInternal");
    }

    [Fact]
    public async Task LoadAsync_PassesGameOnlyToService()
    {
        var (vm, dump) = MakeVm();
        vm.GameOnly = false;
        await vm.LoadCommand.ExecuteAsync(null);
        Assert.False(dump.LastGameOnly);

        vm.GameOnly = true;
        await vm.LoadCommand.ExecuteAsync(null);
        Assert.True(dump.LastGameOnly);
    }

    // ==================================================================
    // Filter inputs
    // ==================================================================

    [Fact]
    public async Task FilterText_SubstringMatch_AppliesToFuncAndClassName()
    {
        var (vm, _) = MakeVm();
        await vm.LoadCommand.ExecuteAsync(null);

        // Match against function name
        vm.FilterText = "Money";
        Assert.Single(vm.Results);
        Assert.Equal("AddMoney", vm.Results[0].FuncName);

        // Match against class name
        vm.FilterText = "Pawn";
        Assert.Single(vm.Results);
        Assert.Equal("MyPawn", vm.Results[0].ClassName);

        // Case insensitive
        vm.FilterText = "MONEY";
        Assert.Single(vm.Results);
    }

    [Fact]
    public async Task CategoryFilter_HidesOtherCategories()
    {
        var (vm, _) = MakeVm();
        await vm.LoadCommand.ExecuteAsync(null);

        vm.CategoryFilter = FunctionCategory.Movement;
        Assert.All(vm.Results, r => Assert.Equal(FunctionCategory.Movement, r.Category));
        Assert.Contains(vm.Results, r => r.FuncName == "TeleportPlayer");

        vm.CategoryFilter = null; // back to All
        Assert.True(vm.Results.Count > 1);
    }

    [Fact]
    public async Task ShowAll_RevealsBelowThresholdRows()
    {
        var (vm, _) = MakeVm();
        await vm.LoadCommand.ExecuteAsync(null);
        int aboveThreshold = vm.Results.Count;

        vm.ShowAll = true;
        Assert.True(vm.Results.Count > aboveThreshold,
            "ShowAll should reveal additional rows below the threshold");
    }

    // ==================================================================
    // Gameplay-Action opt-in (default off) re-scores in place
    // ==================================================================

    [Fact]
    public async Task GameplayActions_Toggle_RescoresAndSurfacesActionVerbs()
    {
        // OpenShop scores 0 keyword points by default (Open/Shop are in the
        // opt-in pack only), so with just its BlueprintCallable flag (+2) it
        // sits below the threshold 5 and is hidden from the default view.
        var entries = new List<AllFunctionEntry>
        {
            new() { ClassName="ShopSubsystem", FuncName="OpenShop",
                    FunctionFlags=0x0400_0000, NumParms=1, ParmsSize=8 },
        };
        var (vm, _) = MakeVm(entries);
        await vm.LoadCommand.ExecuteAsync(null);

        Assert.DoesNotContain(vm.Results, r => r.FuncName == "OpenShop");

        // Enable the opt-in pack -> full re-score -> OpenShop now clears the
        // threshold as a GameplayAction row (Open + Shop = 10). Drain the
        // re-score the SETTER started; calling RescoreAsync() here instead
        // started a second one that raced it (see DrainAsync).
        vm.GameplayActions = true;
        await DrainAsync(vm.PendingToggleRescore, "toggle re-score");

        Assert.Contains(vm.Results, r => r.FuncName == "OpenShop");
        var row = vm.Results.First(r => r.FuncName == "OpenShop");
        Assert.Equal(FunctionCategory.GameplayAction, row.Category);
    }

    [Fact]
    public async Task GameplayActions_DefaultsOff()
    {
        var (vm, _) = MakeVm();
        Assert.False(vm.GameplayActions);
        await vm.LoadCommand.ExecuteAsync(null);
        // Default load must not assign any row the GameplayAction category.
        Assert.DoesNotContain(vm.Results, r => r.Category == FunctionCategory.GameplayAction);
    }

    [Fact]
    public async Task ClearFilters_ResetsAllInputs()
    {
        var (vm, _) = MakeVm();
        await vm.LoadCommand.ExecuteAsync(null);

        vm.FilterText = "Foo";
        vm.CategoryFilter = FunctionCategory.Stats;
        vm.ShowAll = true;
        vm.CallableOnly = true;

        vm.ClearFiltersCommand.Execute(null);

        Assert.Equal("", vm.FilterText);
        Assert.Null(vm.CategoryFilter);
        Assert.False(vm.ShowAll);
        Assert.False(vm.CallableOnly);
    }

    // ==================================================================
    // BlueprintCallable/Exec filter (default off)
    // ==================================================================

    [Fact]
    public async Task CallableOnly_HidesNonCallableNonExecRows()
    {
        // Two above-threshold Movement rows: one BlueprintCallable, one with
        // no flags at all. The keyword score alone (Teleport=5 + class bonus)
        // clears the threshold, so both show by default.
        var entries = new List<AllFunctionEntry>
        {
            new() { ClassName="MyPawn", FuncName="TeleportCallable",
                    FunctionFlags=0x0400_0000, NumParms=1, ParmsSize=4 }, // BlueprintCallable
            new() { ClassName="MyPawn", FuncName="TeleportNative",
                    FunctionFlags=0x0000_0400, NumParms=1, ParmsSize=4 }, // Native only (not BC/Exec)
        };
        var (vm, _) = MakeVm(entries);
        await vm.LoadCommand.ExecuteAsync(null);
        Assert.Contains(vm.Results, r => r.FuncName == "TeleportCallable");
        Assert.Contains(vm.Results, r => r.FuncName == "TeleportNative");

        // Enable BP/Exec-only -> the native-only row drops, the callable stays.
        vm.CallableOnly = true;
        Assert.Contains(vm.Results, r => r.FuncName == "TeleportCallable");
        Assert.DoesNotContain(vm.Results, r => r.FuncName == "TeleportNative");
    }

    [Fact]
    public async Task CallableOnly_KeepsExecRows()
    {
        // An Exec function with no keyword hits: needs Show All to clear the
        // threshold, but must survive the BP/Exec-only filter (Exec counts).
        var entries = new List<AllFunctionEntry>
        {
            new() { ClassName="MyCheatManager", FuncName="DoThing",
                    FunctionFlags=0x0000_0200, NumParms=0, ParmsSize=0 }, // Exec only
        };
        var (vm, _) = MakeVm(entries);
        await vm.LoadCommand.ExecuteAsync(null);
        vm.ShowAll = true;        // DoThing scores 0 -> only visible with Show All
        vm.CallableOnly = true;   // Exec flag satisfies the filter
        Assert.Contains(vm.Results, r => r.FuncName == "DoThing");
    }

    // ==================================================================
    // Cross-tab navigation events
    // ==================================================================

    [Fact]
    public async Task OpenInLiveWalker_FiresNavigateToFunctionWithClassAndFunc()
    {
        var (vm, _) = MakeVm();
        await vm.LoadCommand.ExecuteAsync(null);
        var addMoney = vm.Results.First(r => r.FuncName == "AddMoney");

        string? capturedClass = null;
        string? capturedFunc = null;
        vm.NavigateToFunction += (cls, fn) => { capturedClass = cls; capturedFunc = fn; };

        vm.OpenInLiveWalkerCommand.Execute(addMoney);

        Assert.Equal("PlayerCharacter", capturedClass);
        Assert.Equal("AddMoney", capturedFunc);
    }

    [Fact]
    public async Task CopyAaScript_FiresRequestCopyBakedScriptWithClassAndFunc()
    {
        var (vm, _) = MakeVm();
        await vm.LoadCommand.ExecuteAsync(null);
        var save = vm.Results.First(r => r.FuncName == "SaveGame");

        string? capturedClass = null;
        string? capturedFunc = null;
        string? capturedAddr = null;
        vm.RequestCopyBakedScript += (cls, fn, addr) =>
        {
            capturedClass = cls; capturedFunc = fn; capturedAddr = addr;
        };

        vm.CopyAaScriptCommand.Execute(save);

        Assert.Equal("GameMode", capturedClass);
        Assert.Equal("SaveGame", capturedFunc);
        // The address the row already carries — without it the handler falls back to the
        // capped list_classes page and aborts on "Class GameMode not found" (audit #5 X2).
        Assert.Equal("0x5000", capturedAddr);
    }

    [Fact]
    public async Task OpenInInstanceFinder_FiresNavigateToInstanceFinderWithClass()
    {
        var (vm, _) = MakeVm();
        await vm.LoadCommand.ExecuteAsync(null);
        var addMoney = vm.Results.First(r => r.FuncName == "AddMoney");

        string? capturedClass = null;
        vm.NavigateToInstanceFinder += cls => capturedClass = cls;

        vm.OpenInInstanceFinderCommand.Execute(addMoney);

        Assert.Equal("PlayerCharacter", capturedClass);
    }

    [Fact]
    public void OpenInInstanceFinder_NullRow_DoesNotFireEvent()
    {
        var (vm, _) = MakeVm();
        bool fired = false;
        vm.NavigateToInstanceFinder += _ => fired = true;

        vm.OpenInInstanceFinderCommand.Execute(null);

        Assert.False(fired);
    }

    [Fact]
    public void OpenInLiveWalker_NullRow_DoesNotFireEvent()
    {
        var (vm, _) = MakeVm();
        bool fired = false;
        vm.NavigateToFunction += (_, _) => fired = true;

        vm.OpenInLiveWalkerCommand.Execute(null);

        Assert.False(fired);
    }

    // ==================================================================
    // AOBMaker availability + Notes column
    // ==================================================================

    /// <summary>Stub bridge with a controllable IsAvailable for testing
    /// the VM's wiring without actually opening a pipe.</summary>
    private sealed class FakeAobMakerBridge : IAobMakerBridge
    {
        public bool IsAvailable { get; set; }
        public bool NextCheckResult { get; set; }
        public int CheckCallCount { get; private set; }

        /// <summary>When set, CheckAvailabilityAsync blocks on this instead of
        /// completing synchronously — a deterministic stand-in for the real
        /// bridge's 2s pipe-connect timeout when Cheat Engine isn't running.
        /// Lets a test occupy the window that LoadAsync used to spend with
        /// IsLoading still true.</summary>
        public TaskCompletionSource<bool>? Gate { get; set; }

        public async Task<bool> CheckAvailabilityAsync(CancellationToken ct = default)
        {
            CheckCallCount++;
            if (Gate != null) await Gate.Task;
            IsAvailable = NextCheckResult;
            return NextCheckResult;
        }

        public Task<bool> NavigateHexViewAsync(string hexAddress, CancellationToken ct = default)
            => Task.FromResult(false);
        public Task<bool> NavigateDisassemblerAsync(string hexAddress, CancellationToken ct = default)
            => Task.FromResult(false);
        public Task<bool> CreateAAScriptAsync(string description, string script,
            bool autoActivate = true, string? group = null, CancellationToken ct = default)
            => Task.FromResult(false);
        public Task<bool> CreateSymbolScriptAsync(string name, string aob, int pos, int aoblen,
            string symbol, string module, bool autoActivate = true, CancellationToken ct = default)
            => Task.FromResult(false);
        public Task<bool> CreateMemoryRecordAsync(string description, string address, int valueType,
            bool isSigned = false, bool showAsHex = false, CancellationToken ct = default)
            => Task.FromResult(false);
        public Task<(bool Ok, string? ErrorMessage)> InjectTableFileAsync(string fileName, string content,
            CancellationToken ct = default)
            => Task.FromResult<(bool, string?)>((false, null));
    }

    // ==================================================================
    // Audit #5 Z1 — a Gameplay-Actions toggle raised while Load is still
    // "in flight" must not be silently discarded.
    //
    // LoadAsync used to `await CheckAobMakerAsync()` as its last statement,
    // which pays a 2s pipe-connect timeout whenever CE isn't running, all of
    // it with IsLoading still true and the grid ALREADY painted. RescoreAsync
    // bails on `if (IsLoading ...) return;`, so a tick landing in that window
    // was dropped and nothing ever re-ran: the CheckBox read ON while the rows
    // stayed scored with the pack OFF, permanently. Recovery was untick+retick.
    // ==================================================================

    /// <summary>The load must not stay busy for the AOBMaker probe. The probe
    /// is fire-and-forget, so LoadAsync completes (and clears IsLoading) even
    /// while the bridge is still blocked.</summary>
    [Fact]
    public async Task LoadAsync_DoesNotStayBusyWaitingOnTheAobMakerProbe()
    {
        var bridge = new FakeAobMakerBridge
        {
            NextCheckResult = false,
            Gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously),
        };
        var vm = new InterestingFunctionsViewModel(
            new FakeDumpService { NextResult = BuildResult(BuildSampleEntries()) },
            new NoopLogger(), bridge);

        // The gate is never released — it stands in for "CE isn't running, so
        // the pipe connect burns its full 2s timeout".
        var load = vm.LoadCommand.ExecuteAsync(null);

        // Bounded wait, NOT a bare await: if LoadAsync goes back to awaiting the
        // probe this must FAIL, not hang the suite.
        var finished = await Task.WhenAny(load, Task.Delay(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));

        Assert.Same(load, finished);
        Assert.False(vm.IsLoading,
            "LoadAsync must not hold IsLoading true while the AOBMaker probe is outstanding");
        Assert.NotEmpty(vm.Results);
        Assert.Equal(1, bridge.CheckCallCount);

        bridge.Gate.SetResult(false);
    }

    /// <summary>A toggle raised inside the busy window survives it: once the
    /// load finishes, the rows are reconciled to the mode the CheckBox shows.</summary>
    [Fact]
    public async Task GameplayActions_ToggledWhileLoadIsBusy_IsNotSwallowed()
    {
        // OpenShop is below threshold with the pack off and above it with the
        // pack on — the same fixture the in-place re-score test uses.
        var entries = new List<AllFunctionEntry>
        {
            new() { ClassName="ShopSubsystem", FuncName="OpenShop",
                    FunctionFlags=0x0400_0000, NumParms=1, ParmsSize=8 },
        };
        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var bridge = new FakeAobMakerBridge { NextCheckResult = false, Gate = gate };
        var vm = new InterestingFunctionsViewModel(
            new FakeDumpService { NextResult = BuildResult(entries) },
            new NoopLogger(), bridge);

        var load = vm.LoadCommand.ExecuteAsync(null);

        // The user ticks the pack. Before the fix this landed while IsLoading
        // was still true and RescoreAsync's guard threw it away.
        vm.GameplayActions = true;

        gate.SetResult(false);
        var finished = await Task.WhenAny(load, Task.Delay(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
        Assert.Same(load, finished);

        // Drain ONLY the reconciliation the VM itself decided to run. Calling
        // RescoreAsync() from the test instead would re-score unconditionally
        // and pass even with the defect present — that seam is exactly why the
        // pre-existing coverage never caught this.
        Assert.NotNull(vm.PendingRescore);
        await vm.PendingRescore!;

        Assert.True(vm.GameplayActions);
        Assert.Contains(vm.Results, r => r.FuncName == "OpenShop");
    }

    /// <summary>Toggling twice inside the window lands back on the mode the
    /// rows were already scored with, so the reconciliation correctly does
    /// nothing — this is why it compares values instead of latching a bool.</summary>
    [Fact]
    public async Task GameplayActions_ToggledTwiceWhileBusy_LeavesRowsAlone()
    {
        var entries = new List<AllFunctionEntry>
        {
            new() { ClassName="ShopSubsystem", FuncName="OpenShop",
                    FunctionFlags=0x0400_0000, NumParms=1, ParmsSize=8 },
        };
        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var bridge = new FakeAobMakerBridge { NextCheckResult = false, Gate = gate };
        var vm = new InterestingFunctionsViewModel(
            new FakeDumpService { NextResult = BuildResult(entries) },
            new NoopLogger(), bridge);

        var load = vm.LoadCommand.ExecuteAsync(null);
        vm.GameplayActions = true;
        vm.GameplayActions = false;

        gate.SetResult(false);
        var finished = await Task.WhenAny(load, Task.Delay(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
        Assert.Same(load, finished);

        // Net-zero change, so there is nothing to reconcile and no re-score.
        Assert.Null(vm.PendingRescore);
        Assert.False(vm.GameplayActions);
        Assert.DoesNotContain(vm.Results, r => r.FuncName == "OpenShop");
    }

    [Fact]
    public async Task AobMakerNote_AvailabilityFlipsToFalse_NoteHasContent()
    {
        var bridge = new FakeAobMakerBridge { NextCheckResult = false };
        var vm = new InterestingFunctionsViewModel(
            new FakeDumpService { NextResult = BuildResult(BuildSampleEntries()) },
            new NoopLogger(), bridge);

        // Initial state: defaults to true so UI doesn't gray out before
        // first probe; AobMakerNote is empty.
        Assert.True(vm.IsAobMakerAvailable);
        Assert.Equal("", vm.AobMakerNote);

        await vm.CheckAobMakerAsync();

        Assert.False(vm.IsAobMakerAvailable);
        Assert.Contains("AOBMaker plugin not found", vm.AobMakerNote);
    }

    [Fact]
    public async Task AobMakerNote_AvailabilityRecovers_NoteClears()
    {
        var bridge = new FakeAobMakerBridge { NextCheckResult = false };
        var vm = new InterestingFunctionsViewModel(
            new FakeDumpService { NextResult = BuildResult(BuildSampleEntries()) },
            new NoopLogger(), bridge);

        await vm.CheckAobMakerAsync();
        Assert.False(vm.IsAobMakerAvailable);

        // CE starts up between checks
        bridge.NextCheckResult = true;
        await vm.CheckAobMakerAsync();

        Assert.True(vm.IsAobMakerAvailable);
        Assert.Equal("", vm.AobMakerNote);
    }

    [Fact]
    public void TryCheckAobMaker_RespectsCooldown()
    {
        var bridge = new FakeAobMakerBridge { NextCheckResult = true };
        var vm = new InterestingFunctionsViewModel(
            new FakeDumpService(), new NoopLogger(), bridge);

        // First call should fire (cooldown is from MinValue, always elapsed)
        vm.TryCheckAobMaker();
        // Drain any in-flight task
        SpinWait.SpinUntil(() => bridge.CheckCallCount >= 1, TimeSpan.FromSeconds(2));
        Assert.Equal(1, bridge.CheckCallCount);

        // Second call within cooldown window should be skipped silently
        vm.TryCheckAobMaker();
        Thread.Sleep(50); // Give any spurious task a chance to run
        Assert.Equal(1, bridge.CheckCallCount);
    }

    [Fact]
    public void TryCheckAobMaker_NoBridge_DoesNothing()
    {
        var vm = new InterestingFunctionsViewModel(
            new FakeDumpService(), new NoopLogger(), aobMaker: null);

        // No throw, no side effect
        vm.TryCheckAobMaker();
        Assert.True(vm.IsAobMakerAvailable); // unchanged from default
    }

    // ==================================================================
    // Audit #5 Z15 — a re-score must move the report with the rows.
    // ==================================================================

    /// <summary>
    /// Toggling Gameplay Actions re-scores every row, which by design moves how many
    /// clear the threshold — that is the entire purpose of the pack. The status line
    /// was written only by <c>LoadAsync</c>, so the panel kept reporting the PREVIOUS
    /// scoring's "above threshold: N" underneath a grid scored the other way.
    ///
    /// <para>Distinct from Z1 (the re-score not running); here the re-score runs and
    /// the report does not follow.</para>
    /// </summary>
    [Fact]
    public async Task Rescore_updates_the_above_threshold_count_in_the_status_line()
    {
        var dump = new FakeDumpService
        {
            NextResult = new AllFunctionsResult
            {
                Total = 2, ScannedClasses = 2, ScannedObjects = 100,
                Functions = new List<AllFunctionEntry>
                {
                    // Scores 0 without the pack, 5 with it (GameplayAction "Dash").
                    new() { ClassName = "AThing", FuncName = "Dash" },
                    // Never interesting either way — keeps the count honest at 0.
                    new() { ClassName = "AThing", FuncName = "Zzz" },
                },
            },
        };
        var vm = new InterestingFunctionsViewModel(dump, new NoopLogger());

        await vm.LoadCommand.ExecuteAsync(null);
        Assert.Contains("(0 above threshold", vm.StatusText);

        vm.GameplayActions = true;
        await DrainAsync(vm.PendingToggleRescore, "toggle re-score");

        // The grid moved...
        Assert.Single(vm.Results);
        // ...and so must the sentence describing it.
        Assert.Contains("(1 above threshold", vm.StatusText);
        // The scan facts it quotes are still the load's, not invented.
        Assert.Contains("2 functions across 2 classes", vm.StatusText);
    }

    // ==================================================================
    // Audit #5 Z8 — list_all_functions caps silently; say so.
    // ==================================================================

    [Fact]
    public void StatusLine_on_a_complete_scan_carries_no_warning()
    {
        var s = InterestingFunctionsViewModel.BuildStatusLine(
            new InterestingFunctionsViewModel.LoadScanFacts(
                50_000, 4_400, 1_000_000, Truncated: false, Aborted: false, Limit: 100_000),
            interesting: 900);

        Assert.Equal("50,000 functions across 4,400 classes  "
                   + $"(900 above threshold {KeywordScoringTable.InterestingThreshold}, "
                   + "scanned 1,000,000 objects)", s);
    }

    /// <summary>
    /// A capped walk makes the row count a page, not the pool. Before Z8 the DLL emitted
    /// no truncation marker at all for this command, so this sentence read as a complete
    /// census of the game.
    /// </summary>
    [Fact]
    public void StatusLine_on_a_capped_scan_names_the_cap_and_a_lever_this_panel_has()
    {
        var s = InterestingFunctionsViewModel.BuildStatusLine(
            new InterestingFunctionsViewModel.LoadScanFacts(
                100_000, 9_000, 1_400_000, Truncated: true, Aborted: false, Limit: 100_000),
            interesting: 1_500);

        Assert.Contains("STOPPED at the 100,000-row cap", s);
        Assert.Contains("more functions exist", s);
        Assert.Contains("Game classes only", s);   // a control this panel really owns
    }

    [Fact]
    public void StatusLine_on_an_aborted_scan_says_cancelled_not_capped()
    {
        var s = InterestingFunctionsViewModel.BuildStatusLine(
            new InterestingFunctionsViewModel.LoadScanFacts(
                12, 3, 400, Truncated: false, Aborted: true, Limit: 100_000),
            interesting: 1);

        Assert.Contains("SCAN CANCELLED", s);
        Assert.DoesNotContain("row cap", s);
    }

    /// <summary>
    /// The class-noise picker presents per-class hit counts as a census of the result.
    /// On a capped walk they are a lower bound, and until Z8 there was no flag to pass —
    /// so the en.axaml string "⚠ Counts are partial" could never appear on this panel.
    /// </summary>
    [Fact]
    public async Task Capped_load_marks_the_class_noise_picker_counts_as_partial()
    {
        var dump = new FakeDumpService
        {
            NextResult = new AllFunctionsResult
            {
                Total = 1, ScannedClasses = 1, ScannedObjects = 10,
                Truncated = true, Limit = 100_000,
                Functions = new List<AllFunctionEntry>
                {
                    new() { ClassName = "PlayerCharacter", FuncName = "AddMoney" },
                },
            },
        };
        var vm = new InterestingFunctionsViewModel(dump, new NoopLogger());

        await vm.LoadCommand.ExecuteAsync(null);

        Assert.True(vm.ClassFilter.CountsPartial);
    }

    [Fact]
    public async Task Complete_load_does_NOT_mark_the_picker_counts_as_partial()
    {
        var dump = new FakeDumpService
        {
            NextResult = new AllFunctionsResult
            {
                Total = 1, ScannedClasses = 1, ScannedObjects = 10,
                Functions = new List<AllFunctionEntry>
                {
                    new() { ClassName = "PlayerCharacter", FuncName = "AddMoney" },
                },
            },
        };
        var vm = new InterestingFunctionsViewModel(dump, new NoopLogger());

        await vm.LoadCommand.ExecuteAsync(null);

        Assert.False(vm.ClassFilter.CountsPartial);
    }
}
