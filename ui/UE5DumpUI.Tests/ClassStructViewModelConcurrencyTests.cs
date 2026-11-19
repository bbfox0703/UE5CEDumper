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
/// The Class/Struct panel must show the selected node's class, and a failed walk must not
/// pin another class there.
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
///     runs anyway — see the same note in <c>ProxyDeployConcurrencyTests</c>.
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
    // RegisterClass table. That matters for the negative controls — without the
    // guard under test the stale branch resolves instantly and the test fails on a
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

    [Fact]
    public async Task FailedInstancePick_DoesNotLeaveThePreviousClassUnderTheError()
    {
        // [CLASSSTRUCT-FAILED-KEEPS-OLD] An instance whose get_object fails never reaches the walk, so OnObjectSelected's
        // own catch shows the error -- and the class shown before must go with it, as it does when the walk fails.
        var dump = new GatedDumpService();
        var withField = Class("ClassP");
        withField.Fields.Add(new FieldInfoModel { Name = "Hp", TypeName = "IntProperty", Address = "0x1234" });
        dump.RegisterClass("0xP", withField);

        var vm = NewVm(dump);
        await vm.OnObjectSelected(ClassLike("0xP"));
        Assert.True(vm.HasClass);                    // precondition: something is painted
        Assert.NotEmpty(vm.Fields);
        vm.SelectedField = vm.Fields[0];

        dump.GateObject("0xA").SetException(new InvalidOperationException("get_object failed"));
        await vm.OnObjectSelected(Instance("0xA"));

        Assert.False(string.IsNullOrEmpty(vm.ErrorMessage));
        Assert.False(vm.HasClass);
        Assert.Empty(vm.Fields);
        Assert.Equal("", vm.ClassName);
        Assert.Equal("", vm.LoadedClassAddr);        // Find Class Funcs must not aim at the old class
        Assert.Null(vm.SelectedField);
        Assert.DoesNotContain("0xA", dump.WalkCalls);  // the failure came before any walk
    }

    private static async Task WaitForGate<T>(ConcurrentDictionary<string, TaskCompletionSource<T>> gates, string key)
    {
        for (int i = 0; i < 200 && !gates.ContainsKey(key); i++)
            await Task.Delay(5, TestContext.Current.CancellationToken);
    }

    // ── the spinner for the whole load, which a screenshot cannot sample ────────
    //
    // "The spinner does not vanish EARLY while a load is still running" cannot be checked
    // live: a class load can finish faster than a screenshot can sample it, even for a
    // class with a long inherited field list, so the spinner may never be SEEN at all — and
    // a check that never observed the thing appear cannot report on when it disappears.
    //
    // ⭐ That is a timing problem with the OBSERVER, not a property of the code. What the
    // claim actually is: `IsLoading` stays true for the whole duration of a load, and a
    // gated stub makes the load take exactly as long as the test wants.
    //
    // ⚠ And the interesting failure is NOT "is the flag true at some instant" — it is the
    // one fast selection changes produce: a SUPERSEDED load finishing and clearing the
    // flag out from under a newer load that is still running.
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
        // already asserts the same property — an older load must not retire a spinner it no
        // longer owns — but it drives LoadClassCommand, the CROSS-TAB entry. Fast selection
        // IN THE TREE enters through OnObjectSelected. Both funnel into LoadClassCoreAsync,
        // so the guard under test is the same one; what differs is that the tree's own path
        // is the one exercised.
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

        // Release the SUPERSEDED one first — the ordering that produces the race.
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

    // ── a stale selection must not overwrite a newer one ────────────────────

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

    // [CLASSSTRUCT-DISCONNECT-LOADING] ClearOnDisconnect supersedes the in-flight walk by bumping the ticket, and the
    // walk's finally clears the spinner only for the newest ticket -- so after a disconnect mid-walk no load owns the
    // flag, and unless the disconnect retires it the spinner stays up over the emptied panel until the next game's
    // first class load.
    [Fact]
    public async Task ADisconnectMidWalk_TakesTheSpinnerDown()
    {
        var dump = new GatedDumpService();
        var gate = dump.GateWalk("0xS1");
        var vm = NewVm(dump);
        var load = vm.OnObjectSelected(ClassLike("0xS1"));
        await WaitForGate(dump.WalkGates, "0xS1");
        Assert.True(vm.IsLoading);   // precondition: the walk is parked with the spinner up

        vm.ClearOnDisconnect();
        bool spinnerAfterDisconnect = vm.IsLoading;
        gate.SetResult(Class("ClassS1"));   // the superseded walk lands late, into the new session
        await load;

        Assert.False(spinnerAfterDisconnect, "the spinner is still up over the panel the disconnect emptied");
        Assert.False(vm.IsLoading, "the superseded walk's landing left the spinner up");
        Assert.Equal("", vm.ClassName);     // and it painted nothing
    }

    [Fact]
    public async Task AWalkThatFailsAfterTheDisconnect_LeavesNoSpinnerAndNoError()
    {
        // The realistic ending: the disconnect tears the pipe down and the parked walk fails. It is superseded, so it
        // must paint nothing -- and the spinner must not come back up or stay up either.
        var dump = new GatedDumpService();
        var gate = dump.GateWalk("0xS1");
        var vm = NewVm(dump);
        var load = vm.OnObjectSelected(ClassLike("0xS1"));
        await WaitForGate(dump.WalkGates, "0xS1");

        vm.ClearOnDisconnect();
        gate.SetException(new IOException("pipe broken"));
        await load;

        Assert.False(vm.IsLoading, "the superseded walk's failure left the spinner up");
        Assert.False(vm.HasClass);
        Assert.False(vm.HasNoFields);   // no "this class has no fields" banner over a panel with no class
        Assert.True(string.IsNullOrEmpty(vm.ErrorMessage), $"a superseded walk painted: '{vm.ErrorMessage}'");
    }

    // ── a failed walk must not pin the panel ────────────────────────────────

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
        // The pin needs a PRIOR SUCCESS: a dedupe guard of `key == addr && HasClass`
        // lets a cold retry through anyway — see ColdFailure_WasAlreadyRetryable below
        // for why that priming step is load-bearing and must not be trimmed.
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
    public async Task FailedWalk_DoesNotLeaveThePreviousClassUnderTheError()
    {
        // [CLASSSTRUCT-FAILED-KEEPS-OLD] After a level reload, re-selecting a freed class (ThirdPersonMap_C) fails with
        // "DLL error: Not a live class at ...", and that error must not sit ABOVE the same class's pre-reload header and
        // field grid -- fields whose addresses were freed, under a selection that has no class. Expected: an error
        // line, and not the old class's fields.
        var dump = new ThrowingWalkDumpService();
        // The prior class carries a field: with none, "no fields after the failure" holds before it too.
        var withField = Class("ClassP");
        withField.Fields.Add(new FieldInfoModel { Name = "Hp", TypeName = "IntProperty", Address = "0x1234" });
        dump.RegisterClass("0xP", withField);
        dump.RegisterClass("0xB", Class("ClassB"));

        var vm = NewVm(dump);
        await vm.OnObjectSelected(ClassLike("0xP"));
        Assert.True(vm.HasClass);                    // precondition: something is painted
        Assert.NotEmpty(vm.Fields);
        Assert.Equal("0xP", vm.LoadedClassAddr);
        vm.SelectedField = vm.Fields[0];

        dump.ThrowFor.Add("0xB");
        await vm.OnObjectSelected(ClassLike("0xB"));   // fails

        Assert.False(string.IsNullOrEmpty(vm.ErrorMessage));
        Assert.False(vm.HasClass);
        Assert.Empty(vm.Fields);
        Assert.Equal("", vm.ClassName);
        Assert.Equal("", vm.LoadedClassAddr);        // Find Class Funcs must not aim at the old class
        Assert.Null(vm.SelectedField);
    }

    [Fact]
    public async Task ColdFailure_WasAlreadyRetryable()
    {
        // A cold failure is retryable under either guard shape — the key released on
        // failure, or a dedupe that also requires `HasClass` — and this case is not
        // filler. Without it, a reviewer trims the priming step from the test above as
        // "setup noise" and gets a test that stays green against the `&& HasClass`
        // shape, because on a cold panel HasClass is false and that conjunct lets the
        // retry through. Naming this case is what makes that trap un-trimmable.
        //
        // ⚠ It DOES go red with neither: no `&& HasClass` and no key release. The two
        // are coupled: dropping the conjunct is only safe because the release covers
        // every failure, not just cold ones.
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
        // The third path: no failure, no concurrency, two ordinary clicks. A
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
        // On a COLD panel HasClass is still false while the first walk is in
        // flight, so a guard that requires HasClass falls through and a node ->
        // null -> node re-fire (ApplyFilter nulls SelectedNode on every filter
        // keystroke) issues a SECOND walk for the same class. Also discriminates this design from
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
        // Warm-path rail: the deliberate dedupe must survive the key release. Without
        // this, "release the key on failure" can quietly become "no dedupe".
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
        // production. This proves the overlap.
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

    /// <summary>
    /// [WALKCLASS-STALE-OK] A level reload frees a class (ThirdPersonMap_C), and walk_class on its old address must not
    /// answer ok with an EMPTY class -- no name, no fields -- or this panel paints a blank class with no error line, and
    /// its re-select dedupe (released only on an error) refuses the retry. The panel shows an error and releases the
    /// dedupe when the call throws; the DLL has to refuse. Fern.cpp is in no C++ test target, so the handler is pinned
    /// from the source. A class walked BEFORE the reload would keep answering its old fields -- WalkClassEx's memo is
    /// immortal -- so the refusal must come before the walk, not after an empty one.
    /// </summary>
    [Fact]
    public void WalkClassRefusesAnAddressThatIsNoLongerALiveObject()
    {
        string fern = File.ReadAllText(NumericInputCoercionTests.RepoFile("dll/src/Fern.cpp")).Replace("\r\n", "\n");
        int at = fern.IndexOf("if (cmd == Renge::CMD_WALK_CLASS) {", StringComparison.Ordinal);
        Assert.True(at > 0, "walk_class's handler not found -- re-point this pin");
        int end = fern.IndexOf("return Renge::MakeResponse(id, data).dump();", at, StringComparison.Ordinal);
        string handler = fern[at..end];
        // Refused BEFORE the walk: a stale address must neither reach the memo nor be answered from it.
        int live = handler.IndexOf("if (!IsLiveObject(addr))", StringComparison.Ordinal);
        int walk = handler.IndexOf("Ubel::WalkClassEx(addr)", StringComparison.Ordinal);
        Assert.True(live > 0 && walk > live, $"the liveness refusal must precede the walk (refusal at {live}, walk at {walk})");
        Assert.Contains("return Renge::MakeError(id, \"Not a live class at \" + addrStr", handler);
        // [WALKCLASS-NOT-STRUCT] Live is not enough -- an actor at a freed class's address
        // is live. The kind check sits between the liveness refusal and the walk.
        int kind = handler.IndexOf("if (!Ubel::IsStructKindObject(addr))", StringComparison.Ordinal);
        Assert.True(kind > live && walk > kind, $"the kind refusal must sit between liveness ({live}) and the walk ({walk}), at {kind}");
        Assert.Contains("\"Not a class or struct at \" + addrStr", handler);
        // The panel sends Enum rows and assets typed "...Class" here on purpose, and an AI can
        // pass an instance; none of them was freed by a level travel. The refusal says what walk_class reads instead.
        string kindRefusal = handler[kind..walk];
        Assert.DoesNotContain("level travel", kindRefusal);
        Assert.Contains("walk_class reads a class, a struct or a function", kindRefusal);
        // The liveness rule is the one pe_trace_names and the snapshot decoder use: its own index leads back to it.
        Assert.Matches(@"bool IsLiveObject\(uintptr_t \w+\) \{\s*const int32_t idx = Ubel::GetIndex\(\w+\);\s*return idx >= 0 && Aura::GetByIndex\(idx\) == \w+;", fern);
    }
}
