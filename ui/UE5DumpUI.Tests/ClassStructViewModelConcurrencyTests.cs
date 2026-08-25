using System;
using System.Collections.Concurrent;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UE5DumpUI.Models;
using UE5DumpUI.ViewModels;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// Audit #5 AE2 / AE3 — the Class/Struct panel could show a class that is not the
/// selected node, and a failed walk could pin it there.
///
/// Two facts make these races real, and neither is visible at the call site:
///
///   * Nothing upstream serializes the handler. <c>ObjectTreeViewModel</c> raises
///     <c>SelectionChanged</c> as a bare <c>Action</c>, so MainWindowViewModel's
///     <c>async</c> subscriber returns to the message loop at its first await.
///     <see cref="SelectionChangedSubscriber_ReturnsBeforeHandlerCompletes"/> is the
///     rail that keeps that documented.
///   * <c>AsyncRelayCommand</c> does NOT block re-entrancy: <c>CanExecute</c> goes
///     false while running, so a bound Button self-disables, but <c>ExecuteAsync</c>
///     runs anyway. Measured on 8.4.2 in build 3038 — see the same note in
///     <c>ProxyDeployConcurrencyTests</c>.
///
/// The losing pair is specifically instance-then-class-like, and it loses by
/// ORDERING rather than timing: an instance needs a <c>get_object</c> hop before its
/// walk, a UClass does not, and both ride one strictly FIFO pipe lane.
/// </summary>
public class ClassStructViewModelConcurrencyTests
{
    // ── the fake ────────────────────────────────────────────────────────────
    //
    // Modelled on ClassPivotViewModelTests.GatedStore. Gating is OPT-IN: an
    // address nobody gated resolves immediately through the base stub's
    // RegisterClass table. That matters for the negative controls — against
    // UNFIXED code the stale branch resolves instantly and the test fails on a
    // clean assertion instead of hanging on a gate nobody releases.
    private class GatedDumpService : StubDumpService
    {
        public readonly ConcurrentDictionary<string, TaskCompletionSource<ClassInfoModel>> WalkGates = new();
        public readonly ConcurrentDictionary<string, TaskCompletionSource<ObjectDetail>> ObjectGates = new();
        public readonly ConcurrentDictionary<string, ObjectDetail> Objects = new();

        private readonly object _lock = new();
        private readonly List<string> _walkCalls = new();

        /// <summary>Addresses passed to WalkClassAsync, in order.</summary>
        public IReadOnlyList<string> WalkCalls
        {
            get { lock (_lock) return _walkCalls.ToList(); }
        }

        public int WalkCountFor(string addr)
        {
            lock (_lock) return _walkCalls.Count(a => a == addr);
        }

        // RunContinuationsAsynchronously: without it, SetResult resumes the awaiting
        // continuation inline on the releasing thread, which serialises the very
        // interleaving these tests exist to produce.
        public TaskCompletionSource<ClassInfoModel> GateWalk(string addr)
            => WalkGates[addr] = new TaskCompletionSource<ClassInfoModel>(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<ObjectDetail> GateObject(string addr)
            => ObjectGates[addr] = new TaskCompletionSource<ObjectDetail>(TaskCreationOptions.RunContinuationsAsynchronously);

        public override Task<ClassInfoModel> WalkClassAsync(string addr, CancellationToken ct = default)
        {
            lock (_lock) _walkCalls.Add(addr);
            if (WalkGates.TryGetValue(addr, out var tcs)) return tcs.Task;
            return base.WalkClassAsync(addr, ct);
        }

        public override Task<ObjectDetail> GetObjectAsync(string addr, CancellationToken ct = default)
        {
            if (ObjectGates.TryGetValue(addr, out var tcs)) return tcs.Task;
            if (Objects.TryGetValue(addr, out var d)) return Task.FromResult(d);
            return Task.FromResult(new ObjectDetail());
        }
    }

    private static ClassStructViewModel NewVm(GatedDumpService dump)
        => new(dump, new MockLoggingService(), new MockPlatformService(Path.GetTempPath()));

    // "Actor" does not end in "Class" and is not one of the struct/enum/function
    // names, so IsClassLikeNode is false -> the two-round-trip branch.
    private static UObjectNode Instance(string addr) =>
        new() { Address = addr, Name = "inst", ClassName = "Actor" };

    // Ends in "Class" -> IsClassLikeNode true -> the one-round-trip branch.
    private static UObjectNode ClassLike(string addr) =>
        new() { Address = addr, Name = "cls", ClassName = "BlueprintGeneratedClass" };

    private static ClassInfoModel Class(string name) =>
        new() { Name = name, FullPath = "/Script/X." + name, Fields = new List<FieldInfoModel>() };

    private static async Task WaitForGate<T>(ConcurrentDictionary<string, TaskCompletionSource<T>> gates, string key)
    {
        for (int i = 0; i < 200 && !gates.ContainsKey(key); i++)
            await Task.Delay(5, TestContext.Current.CancellationToken);
    }

    // ── AE2/AE3 step 3, the half a screenshot could not reach ────────────────
    //
    // The row's step 3 has two halves. "The spinner does not get STUCK after the panel
    // settles" passed live on 2026-08-22. The other half — "it does not vanish EARLY while
    // a load is still running" — was recorded as NOT verified, with the reason written
    // down: on this machine a class load finishes faster than a screenshot can sample it.
    // Even DOLLPlayerController (Properties Size 2224, a long inherited field list) was
    // fully drawn in a zero-wait capture, so the spinner was never SEEN at all — and a
    // check that never observed the thing appear cannot report on when it disappears.
    //
    // ⭐ That is a timing problem with the OBSERVER, not a property of the code. What the
    // half actually claims is `IsLoading` staying true for the whole duration of a load,
    // and a gated stub makes the load take exactly as long as the test wants.
    //
    // ⚠ And the interesting failure is NOT "is the flag true at some instant" — it is the
    // one this row's whole subject (fast selection changes) produces: a SUPERSEDED load
    // finishing and clearing the flag out from under a newer load that is still running.
    // That is what the `if (gen == _loadId)` guard in LoadClassCoreAsync's finally exists
    // for, and it is the second test below.

    [Fact]
    public async Task IsLoading_StaysTrueForTheWholeLoad_NotJustAtTheStart()
    {
        var dump = new GatedDumpService();
        dump.RegisterClass("0xS1", Class("ClassS1"));
        var gate = dump.GateWalk("0xS1");
        var vm = NewVm(dump);

        Assert.False(vm.IsLoading);                    // baseline — the flag is not just always on

        var load = vm.OnObjectSelected(ClassLike("0xS1"));

        // LoadClassCoreAsync sets IsLoading BEFORE its first await, so this is deterministic
        // rather than a race: by the time OnObjectSelected has returned its task, the walk is
        // parked on the gate and the flag must already be up.
        await WaitForGate(dump.WalkGates, "0xS1");
        Assert.True(vm.IsLoading, "the spinner is not up while the load is parked mid-flight");

        // Still up after the load has been pending a while — the "vanishes early" shape.
        await Task.Delay(30, TestContext.Current.CancellationToken);
        Assert.True(vm.IsLoading, "the spinner vanished while the load was still running");

        gate.SetResult(Class("ClassS1"));
        await load;
        Assert.False(vm.IsLoading, "the spinner is still up after the load finished");
        Assert.Equal("ClassS1", vm.ClassName);         // and it actually loaded, so this is not vacuous
    }

    [Fact]
    public async Task ASupersededLoadFinishing_DoesNotClearTheSpinner_ViaTreeSelection()
    {
        // ⚠ SAY WHAT THIS ADDS, because it is nearly a duplicate and pretending otherwise
        // would be the more expensive mistake. StaleWalk_DoesNotClearIsLoadingOfNewerLoad
        // above already asserts the same property — an older load must not retire a spinner
        // it no longer owns — but it drives LoadClassCommand, the CROSS-TAB entry. AE2/AE3 is
        // about FAST SELECTION IN THE TREE, which enters through OnObjectSelected. Both funnel
        // into LoadClassCoreAsync, so the guard under test is the same one; what differs is
        // that the row's own path is now the one exercised.
        //
        // (I found the overlap by running the negative control, not by reading: NC-1 reddened
        // three tests where I expected two.)
        var dump = new GatedDumpService();
        dump.RegisterClass("0xOLD", Class("ClassOld"));
        dump.RegisterClass("0xNEW", Class("ClassNew"));
        var oldGate = dump.GateWalk("0xOLD");
        var newGate = dump.GateWalk("0xNEW");
        var vm = NewVm(dump);

        var first  = vm.OnObjectSelected(ClassLike("0xOLD"));
        await WaitForGate(dump.WalkGates, "0xOLD");
        var second = vm.OnObjectSelected(ClassLike("0xNEW"));   // supersedes the first
        await WaitForGate(dump.WalkGates, "0xNEW");
        Assert.True(vm.IsLoading);

        // Release the SUPERSEDED one first — the ordering that produces the defect.
        oldGate.SetResult(Class("ClassOld"));
        await first;

        Assert.True(vm.IsLoading,
            "the superseded load cleared the spinner while the newer load was still running — "
            + "the panel now reads as settled while it is still filling in");
        Assert.NotEqual("ClassOld", vm.ClassName);   // and it did not paint the stale class either

        newGate.SetResult(Class("ClassNew"));
        await second;
        Assert.False(vm.IsLoading);
        Assert.Equal("ClassNew", vm.ClassName);
    }

    // ── AE2 ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task StaleInstanceSelection_DoesNotOverwriteNewerClassSelection()
    {
        // The headline race. A is an instance (get_object THEN walk); B is a UClass
        // (walk only). A is selected first, B second, but A's walk is issued last
        // and would settle last.
        var dump = new GatedDumpService();
        dump.Objects["0xA"] = new ObjectDetail { ClassAddr = "0xACLS" };
        dump.RegisterClass("0xACLS", Class("ClassA"));
        dump.RegisterClass("0xB", Class("ClassB"));

        var objGate = dump.GateObject("0xA");
        var vm = NewVm(dump);

        var t1 = vm.OnObjectSelected(Instance("0xA"));   // parks on get_object
        var t2 = vm.OnObjectSelected(ClassLike("0xB"));  // overtakes, walks, wins
        await t2;

        objGate.SetResult(new ObjectDetail { ClassAddr = "0xACLS" });
        await t1;

        Assert.Equal("ClassB", vm.ClassName);
        Assert.Equal("0xB", vm.LoadedClassAddr);
        // The strongest of the three: the superseded walk is never PUT ON THE WIRE,
        // not merely ignored after the fact.
        Assert.DoesNotContain("0xACLS", dump.WalkCalls);
    }

    [Fact]
    public async Task StaleWalk_DoesNotClobberNewerPanel()
    {
        // Isolates the post-walk guard from the post-get_object one: two cross-tab
        // loads, no OnObjectSelected involved, older released last.
        var dump = new GatedDumpService();
        dump.RegisterClass("0xQ", Class("ClassQ"));
        dump.RegisterClass("0xR", Class("ClassR"));
        var qGate = dump.GateWalk("0xQ");

        var vm = NewVm(dump);
        var t1 = vm.LoadClassCommand.ExecuteAsync("0xQ");
        await WaitForGate(dump.WalkGates, "0xQ");
        var t2 = vm.LoadClassCommand.ExecuteAsync("0xR");
        await t2;

        qGate.SetResult(Class("ClassQ"));
        await t1;

        Assert.Equal("ClassR", vm.ClassName);
    }

    [Fact]
    public async Task StaleWalk_DoesNotClearIsLoadingOfNewerLoad()
    {
        // The older load's `finally` must not retire a spinner it no longer owns.
        var dump = new GatedDumpService();
        var qGate = dump.GateWalk("0xQ");
        var rGate = dump.GateWalk("0xR");

        var vm = NewVm(dump);
        var t1 = vm.LoadClassCommand.ExecuteAsync("0xQ");
        await WaitForGate(dump.WalkGates, "0xQ");
        var t2 = vm.LoadClassCommand.ExecuteAsync("0xR");

        qGate.SetResult(Class("ClassQ"));   // older lands first
        await t1;

        Assert.True(vm.IsLoading);          // newer is still in flight

        rGate.SetResult(Class("ClassR"));
        await t2;
        Assert.False(vm.IsLoading);
    }

    [Fact]
    public async Task StaleGetObjectFailure_DoesNotPaintErrorOverNewerPanel()
    {
        // A stale FAILURE must not paint an error banner over a panel that loaded
        // fine. The throw is deliberate, not the base stub's NotImplementedException,
        // so this cannot pass for the wrong reason.
        var dump = new GatedDumpService();
        dump.RegisterClass("0xB", Class("ClassB"));
        var objGate = dump.GateObject("0xA");

        var vm = NewVm(dump);
        var t1 = vm.OnObjectSelected(Instance("0xA"));
        var t2 = vm.OnObjectSelected(ClassLike("0xB"));
        await t2;

        objGate.SetException(new InvalidOperationException("stale get_object failed"));
        await t1;

        Assert.True(string.IsNullOrEmpty(vm.ErrorMessage));
        Assert.Equal("ClassB", vm.ClassName);
    }

    [Fact]
    public async Task NoOpClassAddr_DoesNotStrandTheSpinner()
    {
        // Pins the ordering inside LoadClassAsync: the no-op check must come BEFORE
        // the ticket claim, or a request that does no work supersedes a live load
        // and nobody is left to retire IsLoading.
        var dump = new GatedDumpService();
        var qGate = dump.GateWalk("0xQ");

        var vm = NewVm(dump);
        var t1 = vm.LoadClassCommand.ExecuteAsync("0xQ");
        await WaitForGate(dump.WalkGates, "0xQ");

        await vm.LoadClassCommand.ExecuteAsync("0x0");   // no-op

        qGate.SetResult(Class("ClassQ"));
        await t1;

        Assert.False(vm.IsLoading);
    }

    // ── AE3 ─────────────────────────────────────────────────────────────────

    private sealed class ThrowingWalkDumpService : GatedDumpService
    {
        public readonly HashSet<string> ThrowFor = new();

        public override Task<ClassInfoModel> WalkClassAsync(string addr, CancellationToken ct = default)
        {
            var t = base.WalkClassAsync(addr, ct);   // still records the call
            if (ThrowFor.Contains(addr))
                return Task.FromException<ClassInfoModel>(new InvalidOperationException("walk failed"));
            return t;
        }
    }

    [Fact]
    public async Task FailedWalkAfterPriorSuccess_RetriesOnReselectingSameNode()
    {
        // AE3 proper. The pin needs a PRIOR SUCCESS, because the old guard was
        // `key == addr && HasClass` — see ColdFailure_WasAlreadyRetryable below for
        // why that priming step is load-bearing and must not be trimmed.
        var dump = new ThrowingWalkDumpService();
        dump.RegisterClass("0xP", Class("ClassP"));
        dump.RegisterClass("0xB", Class("ClassB"));

        var vm = NewVm(dump);
        await vm.OnObjectSelected(ClassLike("0xP"));
        Assert.True(vm.HasClass);            // precondition: the pin is now armed

        dump.ThrowFor.Add("0xB");
        await vm.OnObjectSelected(ClassLike("0xB"));   // fails

        dump.ThrowFor.Remove("0xB");
        await vm.OnObjectSelected(ClassLike("0xB"));   // the natural user gesture: click it again

        Assert.Equal(2, dump.WalkCountFor("0xB"));
        Assert.Equal("ClassB", vm.ClassName);
    }

    [Fact]
    public async Task ColdFailure_WasAlreadyRetryable()
    {
        // Green BEFORE and AFTER the AE3 fix — and it is not filler. Without it, a
        // reviewer trims the priming step from the test above as "setup noise" and
        // gets a test that is silently green against unfixed code, because on a cold
        // panel HasClass is false and the old `&& HasClass` conjunct let the retry
        // through. Naming this case is what makes that trap un-trimmable, and it is
        // the assertion that the finding's "with no way to retry" was too broad.
        //
        // ⚠ It DOES go red under a PARTIAL revert that removes the key release while
        // keeping the `&& HasClass` drop — measured. Those two changes are coupled:
        // dropping the conjunct is only safe because the release covers every
        // failure, not just cold ones. That combination never shipped; the honest
        // baseline is reverting the AE3 change as a whole, which reds exactly three
        // tests and leaves this one green.
        var dump = new ThrowingWalkDumpService();
        dump.RegisterClass("0xB", Class("ClassB"));
        dump.ThrowFor.Add("0xB");

        var vm = NewVm(dump);
        await vm.OnObjectSelected(ClassLike("0xB"));
        Assert.False(vm.HasClass);

        dump.ThrowFor.Remove("0xB");
        await vm.OnObjectSelected(ClassLike("0xB"));

        Assert.Equal("ClassB", vm.ClassName);
    }

    [Fact]
    public async Task CrossTabLoad_ReleasesTheTreeDedupeKey()
    {
        // AE3's third path: no failure, no concurrency, two ordinary clicks. A
        // cross-tab handoff shows a class no tree node selected, so it must CLEAR
        // the key rather than leave it naming the previously selected node.
        var dump = new GatedDumpService();
        dump.RegisterClass("0xP", Class("ClassP"));
        dump.RegisterClass("0xZ", Class("ClassZ"));

        var vm = NewVm(dump);
        await vm.OnObjectSelected(ClassLike("0xP"));
        await vm.LoadClassCommand.ExecuteAsync("0xZ");   // e.g. Interesting Funcs handoff
        Assert.Equal("ClassZ", vm.ClassName);

        await vm.OnObjectSelected(ClassLike("0xP"));     // click the same tree node again

        Assert.Equal(2, dump.WalkCountFor("0xP"));
        Assert.Equal("ClassP", vm.ClassName);
    }

    [Fact]
    public async Task DuplicateSelectionDuringFirstWalk_DoesNotIssueASecondWalk()
    {
        // Red today. On a COLD panel HasClass is still false while the first walk
        // is in flight, so the old guard fell through and a node -> null -> node
        // re-fire (ApplyFilter nulls SelectedNode on every filter keystroke) issued
        // a SECOND walk for the same class. Also discriminates this design from
        // "latch only on success", which would leave that window unguarded.
        var dump = new GatedDumpService();
        var gate = dump.GateWalk("0xP");

        var vm = NewVm(dump);
        var t1 = vm.OnObjectSelected(ClassLike("0xP"));
        await WaitForGate(dump.WalkGates, "0xP");
        await vm.OnObjectSelected(null);                 // filter keystroke
        var t2 = vm.OnObjectSelected(ClassLike("0xP"));  // same node, walk still in flight

        gate.SetResult(Class("ClassP"));
        await Task.WhenAll(t1, t2);

        Assert.Equal(1, dump.WalkCountFor("0xP"));
    }

    [Fact]
    public async Task RepeatedSelectionOfSameNode_WalksOnlyOnce()
    {
        // Warm-path rail: the deliberate dedupe must survive the AE3 fix. Without
        // this, "release the key on failure" can quietly regress into "no dedupe".
        var dump = new GatedDumpService();
        dump.RegisterClass("0xP", Class("ClassP"));

        var vm = NewVm(dump);
        await vm.OnObjectSelected(ClassLike("0xP"));
        await vm.OnObjectSelected(ClassLike("0xP"));

        Assert.Equal(1, dump.WalkCountFor("0xP"));
    }

    [Fact]
    public async Task SelectionChangedSubscriber_ReturnsBeforeHandlerCompletes()
    {
        // Reachability rail. Every test above drives OnObjectSelected directly,
        // which bypasses MainWindowViewModel's async-void lambda — so on their own
        // they prove the GUARD works, not that two selections can overlap in
        // production. This proves the overlap. Green before and after the fix.
        var tree = new ObjectTreeViewModel(new GatedDumpService(),
                                           new MockLoggingService(),
                                           new MockPlatformService(Path.GetTempPath()));
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finished = false;

        tree.SelectionChanged += async (_) =>
        {
            entered.TrySetResult();
            await release.Task;
            finished = true;
        };

        tree.SelectedNode = Instance("0xA");
        await entered.Task;

        Assert.False(finished);   // the setter returned while the handler is parked

        release.TrySetResult();
    }
}
