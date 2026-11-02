using System.IO;
using System.Threading.Tasks;
using Avalonia.Input;
using UE5DumpUI.Helpers;
using UE5DumpUI.Models;
using UE5DumpUI.ViewModels;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// Browser-style forward history for the Live Walker.
///
/// Breadcrumbs is a PATH, not a history: Back and a breadcrumb jump truncate it.
/// The removed crumbs now land on a forward stack so they can be re-entered, and a
/// fresh navigation invalidates that stack — the same contract a browser has. The
/// invalidation is driven off Breadcrumbs.CollectionChanged rather than a call at
/// each navigation site, so these tests poke the collection directly to prove the
/// hook covers mutation sites the tests never name.
///
/// Pure VM state — the stub dump service answers every walk, no running game needed.
/// </summary>
public class LiveWalkerForwardNavTests
{
    private static LiveWalkerViewModel MakeVm(StubDumpService? dump = null)
        => new LiveWalkerViewModel(dump ?? new StubDumpService(), new MockLoggingService(),
                                   new MockPlatformService(Path.GetTempPath()));

    private static BreadcrumbItem Crumb(string name, string addr = "0x1000")
        => new BreadcrumbItem { Address = addr, Label = name, FieldName = name };

    /// <summary>A two-level spine: a root plus one child, the minimum Back needs.</summary>
    private static LiveWalkerViewModel WithSpine(out BreadcrumbItem root, out BreadcrumbItem leaf)
    {
        var vm = MakeVm();
        vm.Breadcrumbs.Add(root = Crumb("Root", "0x1000"));
        vm.Breadcrumbs.Add(leaf = Crumb("Leaf", "0x2000"));
        return vm;
    }

    // ── Stack mechanics ─────────────────────────────────────────────────────

    [Fact]
    public void FreshViewModel_HasNoForwardHistory()
    {
        var vm = MakeVm();
        Assert.False(vm.CanGoForward);
    }

    [Fact]
    public async Task Forward_WithEmptyHistory_IsNoOp()
    {
        var vm = WithSpine(out _, out _);
        await vm.GoForwardCommand.ExecuteAsync(null);

        Assert.Equal(2, vm.Breadcrumbs.Count);
        Assert.False(vm.CanGoForward);
    }

    [Fact]
    public async Task Back_PushesRemovedCrumbOntoForwardHistory()
    {
        var vm = WithSpine(out var root, out _);

        await vm.GoBackCommand.ExecuteAsync(null);

        Assert.Single(vm.Breadcrumbs);
        Assert.Same(root, vm.Breadcrumbs[0]);
        Assert.True(vm.CanGoForward);
    }

    [Fact]
    public async Task Forward_RestoresTheCrumbBackRemoved()
    {
        var vm = WithSpine(out _, out var leaf);

        await vm.GoBackCommand.ExecuteAsync(null);
        await vm.GoForwardCommand.ExecuteAsync(null);

        Assert.Equal(2, vm.Breadcrumbs.Count);
        // The SAME crumb object comes back, so everything needed to re-render the
        // level (container field, class addr, captured view state) survives intact.
        Assert.Same(leaf, vm.Breadcrumbs[1]);
        Assert.False(vm.CanGoForward);   // consumed
    }

    [Fact]
    public async Task ForwardHistory_SurvivesTheReplayPush_SoMultipleLevelsWalkBackDown()
    {
        var vm = MakeVm();
        vm.Breadcrumbs.Add(Crumb("Root", "0x1000"));
        vm.Breadcrumbs.Add(Crumb("Mid", "0x2000"));
        vm.Breadcrumbs.Add(Crumb("Leaf", "0x3000"));

        await vm.GoBackCommand.ExecuteAsync(null);   // drop Leaf
        await vm.GoBackCommand.ExecuteAsync(null);   // drop Mid
        Assert.True(vm.CanGoForward);

        // The re-push inside Forward must not be mistaken for a fresh navigation,
        // or the remaining history would be wiped and the second Forward would fail.
        await vm.GoForwardCommand.ExecuteAsync(null);
        Assert.Equal(new[] { "Root", "Mid" }, Names(vm));
        Assert.True(vm.CanGoForward);

        await vm.GoForwardCommand.ExecuteAsync(null);
        Assert.Equal(new[] { "Root", "Mid", "Leaf" }, Names(vm));
        Assert.False(vm.CanGoForward);
    }

    // ── Invalidation (driven off CollectionChanged, so any nav site is covered) ──

    [Fact]
    public async Task PushingANewCrumb_ClearsForwardHistory()
    {
        var vm = WithSpine(out _, out _);
        await vm.GoBackCommand.ExecuteAsync(null);
        Assert.True(vm.CanGoForward);

        // Stands in for every drill-down path (field, struct, container, Parent):
        // they all reach the forward stack only through this Add.
        vm.Breadcrumbs.Add(Crumb("Elsewhere", "0x9000"));

        Assert.False(vm.CanGoForward);
    }

    [Fact]
    public async Task ClearingTheSpine_ClearsForwardHistory()
    {
        var vm = WithSpine(out _, out _);
        await vm.GoBackCommand.ExecuteAsync(null);
        Assert.True(vm.CanGoForward);

        // Stands in for Start from GWorld / GameEngine / Locate / bookmark load.
        vm.Breadcrumbs.Clear();

        Assert.False(vm.CanGoForward);
    }

    [Fact]
    public async Task BreadcrumbJump_IsNBacks_AndForwardWalksBackDownInOrder()
    {
        var vm = MakeVm();
        vm.Breadcrumbs.Add(Crumb("Root", "0x1000"));
        vm.Breadcrumbs.Add(Crumb("Mid", "0x2000"));
        vm.Breadcrumbs.Add(Crumb("Leaf", "0x3000"));
        var root = vm.Breadcrumbs[0];

        await vm.NavigateToBreadcrumbCommand.ExecuteAsync(root);

        Assert.Single(vm.Breadcrumbs);
        Assert.True(vm.CanGoForward);

        // Nearest-removed sits on top, so Forward retraces the way we came.
        await vm.GoForwardCommand.ExecuteAsync(null);
        Assert.Equal(new[] { "Root", "Mid" }, Names(vm));

        await vm.GoForwardCommand.ExecuteAsync(null);
        Assert.Equal(new[] { "Root", "Mid", "Leaf" }, Names(vm));
        Assert.False(vm.CanGoForward);
    }

    // ── View state (selection + scroll anchor) ──────────────────────────────

    // [LW-BACK-SCROLL] Back must hand the View the row it drilled through. Replaying the saved
    // top row alone scrolled that row to the BOTTOM edge ("make visible" from the top of a
    // rebuilt grid), and the drilled row -- below it -- stayed off screen: measured on build
    // 3567, DumperTestActor, drill Children [] from mid-list, Back, and Children was not visible.
    [Fact]
    public async Task Back_HandsTheViewTheRowItDrilledThrough()
    {
        var vm = MakeVm();
        vm.Breadcrumbs.Add(Crumb("Root", "0x1000"));
        vm.Breadcrumbs.Add(new BreadcrumbItem { Address = "0x2000", Label = "Children", FieldName = "Children", FieldOffset = 0x190 });
        BookmarkFieldRef? drilled = null;
        vm.RestoreBookmarkView += (_, _, d) => drilled = d;

        await vm.GoBackCommand.ExecuteAsync(null);

        Assert.Equal(new BookmarkFieldRef("Children", 0x190), drilled);
    }

    [Fact]
    public async Task BreadcrumbJump_HandsTheViewTheTargetsChildOnTheOldSpine()
    {
        var vm = MakeVm();
        var root = Crumb("Root", "0x1000");
        vm.Breadcrumbs.Add(root);
        vm.Breadcrumbs.Add(new BreadcrumbItem { Address = "0x2000", Label = "Mid", FieldName = "Mid", FieldOffset = 0x40 });
        vm.Breadcrumbs.Add(new BreadcrumbItem { Address = "0x3000", Label = "Leaf", FieldName = "Leaf", FieldOffset = 0x8 });
        BookmarkFieldRef? drilled = null;
        vm.RestoreBookmarkView += (_, _, d) => drilled = d;

        await vm.NavigateToBreadcrumbCommand.ExecuteAsync(root);

        // Two levels up: the row to show in Root is the one that led to Mid, not to Leaf.
        Assert.Equal(new BookmarkFieldRef("Mid", 0x40), drilled);
    }


    [Fact]
    public async Task Back_CapturesTheSelectionOntoTheCrumbItLeaves()
    {
        var vm = WithSpine(out _, out var leaf);
        var row = new LiveFieldValue { Name = "Health", Offset = 0x40 };
        vm.Fields.Add(row);
        vm.SelectedField = row;

        await vm.GoBackCommand.ExecuteAsync(null);

        // Recorded on the crumb we left, so Forward can re-select it.
        Assert.NotNull(leaf.ViewSelectedFields);
        var saved = Assert.Single(leaf.ViewSelectedFields!);
        Assert.Equal("Health", saved.Name);
        Assert.Equal(0x40, saved.Offset);
    }

    [Fact]
    public async Task Back_WithNothingSelected_CapturesAnEmptySelection()
    {
        var vm = WithSpine(out _, out var leaf);
        vm.Fields.Add(new LiveFieldValue { Name = "Health", Offset = 0x40 });

        await vm.GoBackCommand.ExecuteAsync(null);

        // Not null-vs-empty pedantry: an EMPTY capture is the record that the user
        // had selected nothing, and the restore ladder must then leave the grid
        // unselected at the top rather than falling back to an older hint.
        Assert.NotNull(leaf.ViewSelectedFields);
        Assert.Empty(leaf.ViewSelectedFields!);
        Assert.Null(leaf.ViewTopRow);   // no View attached, so no scroll anchor
    }

    [Fact]
    public async Task MultiSelection_IsCapturedWholeAndBeatsTheSingleAnchor()
    {
        var vm = WithSpine(out _, out var leaf);
        var a = new LiveFieldValue { Name = "Health", Offset = 0x40 };
        var b = new LiveFieldValue { Name = "MaxHealth", Offset = 0x44 };
        var other = new LiveFieldValue { Name = "Mana", Offset = 0x48 };
        vm.SelectedField = other;                       // single anchor…
        vm.UpdateSelectedFields(new[] { a, b });        // …loses to the real selection

        await vm.GoBackCommand.ExecuteAsync(null);

        Assert.Equal(2, leaf.ViewSelectedFields!.Count);
        Assert.Equal("Health", leaf.ViewSelectedFields![0].Name);
        Assert.Equal("MaxHealth", leaf.ViewSelectedFields![1].Name);
    }

    [Fact]
    public async Task Forward_ReportsWhenTheObjectIsNoLongerTheOneWeLeft()
    {
        // Forward re-READS the level from the game, and UE can free / recycle a
        // UObject while the user is elsewhere. A class-name mismatch means the
        // address now holds something else, so the stale selection must not be
        // dressed up as a successful return.
        var dump = new StubDumpService();
        dump.RegisterStruct("0x2000", new InstanceWalkResult
        {
            ClassName = "USomethingElse",
            Fields = new List<LiveFieldValue>(),
        });

        var vm = MakeVm(dump);
        vm.Breadcrumbs.Add(Crumb("Root", "0x1000"));
        vm.Breadcrumbs.Add(new BreadcrumbItem
        {
            Address = "0x2000",
            Label = "Leaf",
            FieldName = "Leaf",
            TargetClassName = "UExpectedClass",
        });

        await vm.GoBackCommand.ExecuteAsync(null);
        await vm.GoForwardCommand.ExecuteAsync(null);

        Assert.Contains("object changed", vm.StatusText);
    }

    [Fact]
    public async Task Forward_OnAMatchingClass_ReportsNothing()
    {
        var dump = new StubDumpService();
        dump.RegisterStruct("0x2000", new InstanceWalkResult
        {
            ClassName = "UExpectedClass",
            Fields = new List<LiveFieldValue>(),
        });

        var vm = MakeVm(dump);
        vm.Breadcrumbs.Add(Crumb("Root", "0x1000"));
        vm.Breadcrumbs.Add(new BreadcrumbItem
        {
            Address = "0x2000",
            Label = "Leaf",
            FieldName = "Leaf",
            TargetClassName = "UExpectedClass",
        });

        await vm.GoBackCommand.ExecuteAsync(null);
        await vm.GoForwardCommand.ExecuteAsync(null);

        Assert.DoesNotContain("object changed", vm.StatusText);
    }

    [Fact]
    public async Task Forward_ThatFailsToWalk_LeavesTheUserWhereTheyWere()
    {
        // Back can afford to leave a truncated spine on failure — it is still
        // consistent. Forward cannot: an optimistic push that then fails to render
        // would strand a dead level on the spine. It must roll back, and the crumb
        // must stay retryable.
        var dump = new FailingDumpService { FailAddr = "0x2000" };
        var vm = MakeVm(dump);
        vm.Breadcrumbs.Add(Crumb("Root", "0x1000"));
        vm.Breadcrumbs.Add(Crumb("Leaf", "0x2000"));

        await vm.GoBackCommand.ExecuteAsync(null);
        await vm.GoForwardCommand.ExecuteAsync(null);

        Assert.Single(vm.Breadcrumbs);
        Assert.Equal("Root", vm.Breadcrumbs[0].FieldName);
        Assert.True(vm.CanGoForward);
    }

    // ── Re-rooting navigations: a spine REPLACED, not truncated ─────────────
    //
    // Two navigations throw the whole spine away instead of popping one level off it:
    // a bookmark load, and NavigateToAddressAsync (the Go box, the Find Refs owner
    // drill, every cross-tab "Open in Live Walker" handoff). A crumb on the forward
    // stack cannot express that — it re-ATTACHES to whatever is on screen — so these
    // push a whole-spine step instead, and Back reads the one-deep re-root slot.

    /// <summary>A spine plus the crumbs a bookmark would restore into it.</summary>
    private static LiveWalkerViewModel WithThreeLevelSpine(out BreadcrumbItem[] spine)
    {
        var vm = MakeVm();
        spine = new[] { Crumb("Root", "0x1000"), Crumb("Mid", "0x2000"), Crumb("Leaf", "0x3000") };
        foreach (var c in spine) vm.Breadcrumbs.Add(c);
        return vm;
    }

    [Fact]
    public async Task NavigateToAddress_LetsBackReturnToTheSpineItReplaced()
    {
        // Before this, the re-root nulled the slot and Back at the new root did
        // nothing at all — the Find Refs drill was a one-way door.
        var vm = WithThreeLevelSpine(out _);

        await vm.NavigateToAddressCommand.ExecuteAsync("0x5000");
        Assert.Single(vm.Breadcrumbs);                       // spine replaced

        await vm.GoBackCommand.ExecuteAsync(null);

        Assert.Equal(new[] { "Root", "Mid", "Leaf" }, Names(vm));
    }

    [Fact]
    public async Task NavigateToAddress_FromAnEmptyWalker_LeavesNothingToGoBackTo()
    {
        // Nothing was replaced, so Back must not invent a destination.
        var vm = MakeVm();

        await vm.NavigateToAddressCommand.ExecuteAsync("0x5000");
        var landed = Names(vm);
        await vm.GoBackCommand.ExecuteAsync(null);

        Assert.Equal(landed, Names(vm));
        Assert.False(vm.CanGoForward);
    }

    [Fact]
    public async Task SteppingOutOfARerootedSpine_KeepsTheForwardHistoryBuiltInsideIt()
    {
        // THE regression. Walking out of a re-rooted spine builds forward entries;
        // the Back that leaves the spine used to Clear+Add the collection, which
        // reached the invalidation hook as a fresh navigation and wiped every one of
        // them — greying Forward on the press that most obviously ought to be undoable.
        var vm = WithThreeLevelSpine(out _);
        await vm.NavigateToAddressCommand.ExecuteAsync("0x5000");

        // Drill twice inside the new spine, then walk back out of both.
        vm.Breadcrumbs.Add(Crumb("A", "0x6000"));
        vm.Breadcrumbs.Add(Crumb("B", "0x7000"));
        await vm.GoBackCommand.ExecuteAsync(null);   // drop B
        await vm.GoBackCommand.ExecuteAsync(null);   // drop A
        Assert.True(vm.CanGoForward);

        await vm.GoBackCommand.ExecuteAsync(null);   // step OUT of the re-rooted spine

        Assert.Equal(new[] { "Root", "Mid", "Leaf" }, Names(vm));

        // NOT merely CanGoForward: the spine step pushes one entry of its own, so the
        // flag stays true even when the wipe eats A and B. (Verified — with the
        // ReplaceSpine guard disabled, a CanGoForward-only assertion still passed.)
        // The entries built INSIDE the spine have to be reachable.
        await vm.GoForwardCommand.ExecuteAsync(null);   // back into the re-rooted spine
        await vm.GoForwardCommand.ExecuteAsync(null);   // and on to A
        Assert.Equal(new[] { "Custom", "A" }, Names(vm));
    }

    [Fact]
    public async Task SteppingOutOfARerootedSpine_IsItselfUndoable()
    {
        var vm = WithThreeLevelSpine(out _);
        await vm.NavigateToAddressCommand.ExecuteAsync("0x5000");

        await vm.GoBackCommand.ExecuteAsync(null);
        Assert.Equal(3, vm.Breadcrumbs.Count);
        Assert.True(vm.CanGoForward);

        await vm.GoForwardCommand.ExecuteAsync(null);

        // The whole spine is swapped back — NOT appended to the one we were on.
        Assert.Single(vm.Breadcrumbs);
        Assert.Equal("Custom", vm.Breadcrumbs[0].FieldName);
    }

    [Fact]
    public async Task ForwardIntoASpine_ReArmsTheBackOutOfIt()
    {
        // Symmetry: Forward must put what it left into the one-deep slot, or the pair
        // would ratchet — one Back out, and never able to get out again.
        var vm = WithThreeLevelSpine(out _);
        await vm.NavigateToAddressCommand.ExecuteAsync("0x5000");

        await vm.GoBackCommand.ExecuteAsync(null);      // out
        await vm.GoForwardCommand.ExecuteAsync(null);   // back in
        await vm.GoBackCommand.ExecuteAsync(null);      // out again

        Assert.Equal(new[] { "Root", "Mid", "Leaf" }, Names(vm));
    }

    [Fact]
    public async Task WalkOutOfASpine_ThenStepOut_ForwardRetracesEveryStep()
    {
        // The full interleave: crumb steps and a spine step on one stack, replayed in
        // the exact order the user pressed Back. This is why they share a stack — two
        // stacks could not order a spine step against the crumb steps around it.
        var vm = WithThreeLevelSpine(out _);
        await vm.NavigateToAddressCommand.ExecuteAsync("0x5000");
        vm.Breadcrumbs.Add(Crumb("A", "0x6000"));
        vm.Breadcrumbs.Add(Crumb("B", "0x7000"));

        await vm.GoBackCommand.ExecuteAsync(null);   // drop B      (crumb step)
        await vm.GoBackCommand.ExecuteAsync(null);   // drop A      (crumb step)
        await vm.GoBackCommand.ExecuteAsync(null);   // out of spine (spine step)
        Assert.Equal(new[] { "Root", "Mid", "Leaf" }, Names(vm));

        await vm.GoForwardCommand.ExecuteAsync(null);
        Assert.Equal(new[] { "Custom" }, Names(vm));
        await vm.GoForwardCommand.ExecuteAsync(null);
        Assert.Equal(new[] { "Custom", "A" }, Names(vm));
        await vm.GoForwardCommand.ExecuteAsync(null);
        Assert.Equal(new[] { "Custom", "A", "B" }, Names(vm));
        Assert.False(vm.CanGoForward);
    }

    [Fact]
    public async Task ARerootThatFollowsAnother_ReplacesTheOneBack_AndSaysSo()
    {
        // The slot is ONE deep by design (each entry pins a whole crumb list). Prove
        // the depth rather than leaving it as a comment: the SECOND re-root's Back
        // returns to the first re-root, not all the way to the original spine.
        var vm = WithThreeLevelSpine(out _);

        await vm.NavigateToAddressCommand.ExecuteAsync("0x5000");
        await vm.NavigateToAddressCommand.ExecuteAsync("0x8000");
        await vm.GoBackCommand.ExecuteAsync(null);

        Assert.Equal(new[] { "Custom" }, Names(vm));
        Assert.Equal("0x5000", vm.Breadcrumbs[0].Address);

        // And the original spine is genuinely gone, not merely one press further away.
        await vm.GoBackCommand.ExecuteAsync(null);
        Assert.Equal(new[] { "Custom" }, Names(vm));
    }

    [Fact]
    public async Task ARerootWithAGarbageAddress_StillLetsBackOut_ButOffersNoForwardIntoNothing()
    {
        // The capture happens before the address is validated, so a typo in the Go box
        // leaves an EMPTY walker with a live slot. Back out of that is the whole point;
        // a Forward back into the empty spine would walk the "" address.
        var vm = WithThreeLevelSpine(out _);

        await vm.NavigateToAddressCommand.ExecuteAsync("0xnothex");
        Assert.Empty(vm.Breadcrumbs);

        await vm.GoBackCommand.ExecuteAsync(null);

        Assert.Equal(new[] { "Root", "Mid", "Leaf" }, Names(vm));
        Assert.False(vm.CanGoForward);
    }

    [Fact]
    public async Task BookmarkLoad_StepsOutThroughTheSameOneBack()
    {
        // The bookmark path was the original owner of this slot; it now shares the
        // mechanism with the address re-root, so its Back must still behave.
        var vm = WithThreeLevelSpine(out _);
        var slot = new BookmarkSlot
        {
            SlotIndex = 0,
            IsOccupied = true,
            SavedAddress = "0x9000",
            SavedBreadcrumbs = new List<BreadcrumbItem> { Crumb("Marked", "0x9000") },
        };

        await vm.LoadBookmarkCommand.ExecuteAsync(slot);
        Assert.Equal(new[] { "Marked" }, Names(vm));

        // Walk one level deeper and back out, so there is an inner forward entry for
        // the step-out to preserve — the bookmark path is where this was first broken.
        vm.Breadcrumbs.Add(Crumb("Deeper", "0x9100"));
        await vm.GoBackCommand.ExecuteAsync(null);   // drop Deeper
        await vm.GoBackCommand.ExecuteAsync(null);   // step out of the bookmark spine

        Assert.Equal(new[] { "Root", "Mid", "Leaf" }, Names(vm));

        await vm.GoForwardCommand.ExecuteAsync(null);
        Assert.Equal(new[] { "Marked" }, Names(vm));
        await vm.GoForwardCommand.ExecuteAsync(null);
        Assert.Equal(new[] { "Marked", "Deeper" }, Names(vm));
    }

    // ── Telling the user the way back exists ────────────────────────────────

    [Fact]
    public async Task ARerootSaysHowToGetBack()
    {
        // The only affordance: Back is always clickable and the crumb strip shows the
        // NEW spine, so without a status line the return path is invisible.
        var vm = WithThreeLevelSpine(out _);

        await vm.NavigateToAddressCommand.ExecuteAsync("0x5000");

        Assert.Contains("Back returns to", vm.StatusText);
        Assert.Contains("Leaf", vm.StatusText);
    }

    [Fact]
    public async Task ARerootFromAnEmptyWalker_PromisesNoWayBack()
    {
        var vm = MakeVm();

        await vm.NavigateToAddressCommand.ExecuteAsync("0x5000");

        Assert.DoesNotContain("Back returns to", vm.StatusText);
    }

    [Fact]
    public async Task OpenReferenceOwner_ItsHintSurvivesTheNavigationThatUsedToWipeIt()
    {
        // The hint was assigned BEFORE calling NavigateToAddressAsync, whose first
        // statement is ClearStatus() — so it was written and wiped inside one click
        // and never reached the screen.
        var vm = WithThreeLevelSpine(out _);
        var match = new ReferenceMatch
        {
            OwnerAddress = "0x5000",
            OwnerName = "PlayerInventory",
            FieldName = "Items",
            ElementIndex = 3,
        };

        await vm.OpenReferenceOwnerCommand.ExecuteAsync(match);

        Assert.Contains("PlayerInventory", vm.StatusText);
        Assert.Contains("Items", vm.StatusText);
        Assert.Contains("[3]", vm.StatusText);
        Assert.Contains("Back returns to", vm.StatusText);
    }

    private static string[] Names(LiveWalkerViewModel vm)
        => vm.Breadcrumbs.Select(b => b.FieldName).ToArray();

    /// <summary>Stub that fails the walk for one address (a dead pointer / broken pipe).</summary>
    private sealed class FailingDumpService : StubDumpService
    {
        public string FailAddr = "";

        public override Task<InstanceWalkResult> WalkInstanceAsync(string addr, string? classAddr = null,
            int arrayLimit = 64, int previewLimit = 2, bool fillGaps = false, bool lean = false,
            System.Threading.CancellationToken ct = default)
            => addr == FailAddr
                ? throw new System.InvalidOperationException("walk failed")
                : base.WalkInstanceAsync(addr, classAddr, arrayLimit, previewLimit, fillGaps, lean, ct);
    }

    // ── A container view stands for its owner ── [LW-BACK-CONTAINER-HEADER-ADDR]
    //
    // A container drill renders without a walk, so the header's address and the Functions list stay the OWNER's,
    // inherited from the owner's walk. Back, a crumb jump, the Back out of a re-root and Refresh render the same view
    // without a walk too, and left both on whatever was walked last: measured live on build 3660, the header's Addr
    // chip copied the element's address after Back. The Functions list matters most -- PIPE fires the selected
    // function at the header's address, so the two must describe one object.

    private const string Owner = "0x10000", OwnerCls = "0xC100", Elem = "0x20000", ElemCls = "0xC200",
                         Other = "0x30000", OtherCls = "0xC300", Table = "0x40000", TableCls = "0xC400",
                         RowData = "0x48000", RowStruct = "0xC480";

    /// <summary>Walks an owner (an object array, an object-valued map and an object set, each holding
    /// <see cref="Elem"/>), a DataTable, one of its rows and an unrelated object, and lists one function for each
    /// UClass (the row struct has none). A gated class's list waits until the test releases it; its continuation runs
    /// inline, so the late landing has happened when SetResult or SetException returns.</summary>
    private sealed class OwnerStub : StubDumpService
    {
        private readonly Dictionary<string, TaskCompletionSource<List<FunctionInfoModel>>> _functionGates = new();
        private readonly Dictionary<string, TaskCompletionSource<InstanceWalkResult>> _walkGates = new();

        /// <summary>Hold the next walks of <paramref name="addr"/> until the test answers. Continuations run
        /// asynchronously, so the navigation that awaits the walk really is suspended meanwhile.</summary>
        public TaskCompletionSource<InstanceWalkResult> GateWalk(string addr)
            => _walkGates[addr] = new TaskCompletionSource<InstanceWalkResult>(TaskCreationOptions.RunContinuationsAsynchronously);

        public void UngateWalk(string addr) => _walkGates.Remove(addr);

        /// <summary>How many walks each address has had.</summary>
        public readonly Dictionary<string, int> Walks = new();

        public override Task<InstanceWalkResult> WalkInstanceAsync(string addr, string? classAddr = null,
            int arrayLimit = 64, int previewLimit = 2, bool fillGaps = false, bool lean = false,
            CancellationToken ct = default)
            => (Walks[addr] = Walks.GetValueOrDefault(addr) + 1) > 0 && _walkGates.TryGetValue(addr, out var gate)
                ? gate.Task
                : classAddr == null && _classlessWalks.TryGetValue(addr, out var classless)
                    ? Task.FromResult(classless)
                    : base.WalkInstanceAsync(addr, classAddr, arrayLimit, previewLimit, fillGaps, lean, ct);

        private readonly Dictionary<string, InstanceWalkResult> _classlessWalks = new();

        /// <summary>What a walk of <paramref name="addr"/> with no class answers: an inline struct's bytes read as a
        /// UObject, as the DLL reads them, find none of the struct's fields.</summary>
        public void RegisterClasslessWalk(string addr, InstanceWalkResult result) => _classlessWalks[addr] = result;

        public OwnerStub()
        {
            RegisterStruct(Owner, new InstanceWalkResult
            {
                Address = Owner, Name = "Owner_0", ClassName = "BP_Owner_C", ClassAddr = OwnerCls,
                Fields = new List<LiveFieldValue> { GearField(), LootField(), PalsField() },
            });
            RegisterStruct(Elem, Obj(Elem, "Elem_0", "BP_Elem_C", ElemCls));
            RegisterStruct(Other, Obj(Other, "Other_0", "BP_Other_C", OtherCls));
            RegisterStruct(Table, Obj(Table, "DT_Loot", "DataTable", TableCls));
            RegisterStruct(RowData, Obj(RowData, "Sword", "FLootRow", RowStruct));
        }

        public static LiveFieldValue GearField() => new()
        {
            Name = "Gear", TypeName = "ArrayProperty", Offset = 0x40, Size = 0x10,
            ArrayInnerType = "ObjectProperty", ArrayCount = 1, ArrayElemSize = 8, ArrayDataAddr = "0x50000",
            ArrayElements = new() { new() { Index = 0, PtrAddress = Elem, PtrName = "Elem_0", PtrClassName = "BP_Elem_C" } },
        };

        private static LiveFieldValue LootField() => new()
        {
            Name = "Loot", TypeName = "MapProperty", Offset = 0x50, Size = 0x50,
            MapCount = 1, MapKeyType = "IntProperty", MapValueType = "ObjectProperty", MapKeySize = 4, MapValueSize = 8,
            MapDataAddr = "0x60000", MapStride = 24, MapValueOffset = 8,
            MapElements = new()
            {
                new() { Index = 0, Key = "7", ValuePtrAddress = Elem, ValuePtrName = "Elem_0", ValuePtrClassName = "BP_Elem_C" },
            },
        };

        private static LiveFieldValue PalsField() => new()
        {
            Name = "Pals", TypeName = "SetProperty", Offset = 0xA0, Size = 0x50,
            SetCount = 1, SetElemType = "ObjectProperty", SetElemSize = 8, SetStride = 16, SetDataAddr = "0x70000",
            SetElements = new() { new() { Index = 0, KeyPtrAddress = Elem, KeyPtrName = "Elem_0", KeyPtrClassName = "BP_Elem_C" } },
        };

        public static DataTableWalkResult TableRows() => new()
        {
            RowCount = 1, RowMapOffset = 0x30, RowStructAddr = RowStruct, RowStructName = "FLootRow",
            FNameSize = 8, Stride = 24,
            Rows = new() { new() { SparseIndex = 0, RowName = "Sword", DataAddr = RowData } },
        };

        public static InstanceWalkResult Obj(string addr, string name, string cls, string clsAddr) => new()
        {
            Address = addr, Name = name, ClassName = cls, ClassAddr = clsAddr,
            Fields = new List<LiveFieldValue> { new() { Name = "X", TypeName = "IntProperty", Offset = 8, Size = 4 } },
        };

        public TaskCompletionSource<List<FunctionInfoModel>> GateFunctions(string classAddr)
            => _functionGates[classAddr] = new TaskCompletionSource<List<FunctionInfoModel>>();

        public override Task<List<FunctionInfoModel>> WalkFunctionsAsync(string addr, CancellationToken ct = default)
            => _functionGates.TryGetValue(addr, out var gate) ? gate.Task : Task.FromResult(FunctionsOf(addr));

        /// <summary>The address the last reference scan was asked about.</summary>
        public string? RefsScannedAt;

        public override Task<FindReferencesResult> FindReferencesToUObjectAsync(string addr, int maxResults = 32,
            CancellationToken ct = default)
        {
            RefsScannedAt = addr;
            return Task.FromResult(new FindReferencesResult());
        }

        public static List<FunctionInfoModel> FunctionsOf(string classAddr) => classAddr switch
        {
            OwnerCls => new() { new() { Name = "OwnerFn", Address = "0xF100" } },
            ElemCls => new() { new() { Name = "ElemFn", Address = "0xF200" } },
            OtherCls => new() { new() { Name = "OtherFn", Address = "0xF300" } },
            TableCls => new() { new() { Name = "TableFn", Address = "0xF400" } },
            _ => new(),
        };
    }

    private static (OwnerStub dump, LiveWalkerViewModel vm, MockPlatformService platform) OwnerWalker()
    {
        var dump = new OwnerStub();
        var platform = new MockPlatformService(Path.GetTempPath());
        return (dump, new LiveWalkerViewModel(dump, new MockLoggingService(), platform), platform);
    }

    private static string[] FunctionNames(LiveWalkerViewModel vm) => vm.Functions.Select(f => f.Name).ToArray();

    /// <summary>What the drill shows, and so what every other render of that container view must show.</summary>
    private static void AssertShowsTheOwner(LiveWalkerViewModel vm, string owner = Owner, string function = "OwnerFn")
    {
        Assert.Equal(owner, vm.CurrentAddress);
        Assert.Equal(new[] { function }, FunctionNames(vm));
    }

    private static async Task<LiveWalkerViewModel> OnOwnersContainer(LiveWalkerViewModel vm, string container)
    {
        await vm.NavigateToAddressCommand.ExecuteAsync(Owner);
        await vm.NavigateToContainerCommand.ExecuteAsync(vm.Fields.Single(f => f.Name == container));
        AssertShowsTheOwner(vm);   // the drill: the baseline every other render is held to
        return vm;
    }

    // ── A container view re-entered after its container moved ── [LEAD-LW-BACK-CACHED-ELEMENTS]
    //
    // Back, Forward and a crumb jump render a container view from the crumb's held reading, made at the drill or the
    // last refresh inside the view. A container that reallocated since then kept showing element addresses in the
    // freed allocation until Refresh. The owner is re-read once on re-entry; the view is re-rendered only when the
    // container's data address or count moved, so an unchanged view keeps the scroll it came back to.

    /// <summary>The owner's Gear array moved to a new allocation, now holding <see cref="Other"/>.</summary>
    private static void ReallocateGear(OwnerStub dump)
    {
        var gear = OwnerStub.GearField();
        dump.RegisterStruct(Owner, new InstanceWalkResult
        {
            Address = Owner, Name = "Owner_0", ClassName = "BP_Owner_C", ClassAddr = OwnerCls,
            Fields = new List<LiveFieldValue>
            {
                new()
                {
                    Name = gear.Name, TypeName = gear.TypeName, Offset = gear.Offset, Size = gear.Size,
                    ArrayInnerType = "ObjectProperty", ArrayCount = 1, ArrayElemSize = 8, ArrayDataAddr = "0x58000",
                    ArrayElements = new()
                    {
                        new() { Index = 0, PtrAddress = Other, PtrName = "Other_0", PtrClassName = "BP_Other_C" },
                    },
                },
            },
        });
    }

    private static void AssertGearShowsTheMovedAllocation(LiveWalkerViewModel vm)
    {
        Assert.StartsWith("Array<", vm.CurrentClassName);
        var row = vm.Fields.Single();
        Assert.Equal("0x58000", row.FieldAddress);
        Assert.Equal(Other, row.PtrAddress);
    }

    [Fact]
    public async Task Back_IntoAContainerThatMoved_ShowsItsNewAllocation()
    {
        var (dump, vm, _) = OwnerWalker();
        await OnOwnersContainer(vm, "Gear");
        await vm.NavigateToFieldCommand.ExecuteAsync(vm.Fields.Single());   // → [0]
        ReallocateGear(dump);

        await vm.GoBackCommand.ExecuteAsync(null);
        await vm.ReentryRecheck;

        AssertGearShowsTheMovedAllocation(vm);
        AssertShowsTheOwner(vm);
    }

    [Fact]
    public async Task Forward_IntoAContainerThatMoved_ShowsItsNewAllocation()
    {
        var (dump, vm, _) = OwnerWalker();
        await OnOwnersContainer(vm, "Gear");
        await vm.GoBackCommand.ExecuteAsync(null);   // to the owner: the view goes onto Forward holding the 0x50000 reading
        ReallocateGear(dump);

        await vm.GoForwardCommand.ExecuteAsync(null);
        await vm.ReentryRecheck;

        AssertGearShowsTheMovedAllocation(vm);
    }

    [Fact]
    public async Task ACrumbJump_IntoAContainerThatMoved_ShowsItsNewAllocation()
    {
        var (dump, vm, _) = OwnerWalker();
        await OnOwnersContainer(vm, "Gear");
        await vm.NavigateToFieldCommand.ExecuteAsync(vm.Fields.Single());   // → [0]
        ReallocateGear(dump);

        await vm.NavigateToBreadcrumbCommand.ExecuteAsync(vm.Breadcrumbs[1]);
        await vm.ReentryRecheck;

        AssertGearShowsTheMovedAllocation(vm);
    }

    [Fact]
    public void ContainerMoved_ReadsEachKindsOwnAddressAndCount()
    {
        LiveFieldValue Map(string data, int n) => new() { MapKeyType = "IntProperty", MapDataAddr = data, MapCount = n };
        LiveFieldValue Set(string data, int n) => new() { SetElemType = "IntProperty", SetDataAddr = data, SetCount = n };
        LiveFieldValue Arr(string data, int n) => new() { ArrayInnerType = "IntProperty", ArrayDataAddr = data, ArrayCount = n };

        Assert.False(LiveWalkerViewModel.ContainerMoved(Map("0x60000", 1), Map("0x60000", 1)));
        Assert.True(LiveWalkerViewModel.ContainerMoved(Map("0x60000", 1), Map("0x68000", 1)));
        Assert.True(LiveWalkerViewModel.ContainerMoved(Map("0x60000", 1), Map("0x60000", 2)));
        Assert.False(LiveWalkerViewModel.ContainerMoved(Set("0x70000", 3), Set("0x70000", 3)));
        Assert.True(LiveWalkerViewModel.ContainerMoved(Set("0x70000", 3), Set("0x78000", 3)));
        Assert.True(LiveWalkerViewModel.ContainerMoved(Arr("0x50000", 1), Arr("0x50000", 0)));
        Assert.False(LiveWalkerViewModel.ContainerMoved(Arr("0x50000", 1), Arr("0x50000", 1)));
    }

    // Review round 2: the re-check ran inside the navigation, so Back / Forward / a crumb jump stayed disabled for the
    // whole owner walk (up to the refresh timeout), and it re-rendered through RefreshAsync, which cleared the
    // navigation's own status line. It now runs after the navigation returns (ReentryRecheck), and renders the reading
    // it took.

    [Fact]
    public async Task Back_IntoAContainerView_ReturnsBeforeTheOwnerReRead()
    {
        var (dump, vm, _) = OwnerWalker();
        await OnOwnersContainer(vm, "Gear");
        await vm.NavigateToFieldCommand.ExecuteAsync(vm.Fields.Single());   // → [0]
        var gate = dump.GateWalk(Owner);

        var back = vm.GoBackCommand.ExecuteAsync(null);
        await Task.WhenAny(back, Task.Delay(2000, TestContext.Current.CancellationToken));

        Assert.True(back.IsCompleted, "Back waited for the owner re-read");
        Assert.False(vm.IsLoading);
        Assert.StartsWith("Array<", vm.CurrentClassName);   // the held view is up already
        dump.UngateWalk(Owner);
        gate.SetResult(OwnerStub.Obj(Owner, "Owner_0", "BP_Owner_C", OwnerCls));
        await vm.ReentryRecheck;
    }

    [Fact]
    public async Task BackOutOfAReRoot_IntoAContainerThatMoved_ShowsItsNewAllocation_AndKeepsItsStatus()
    {
        var (dump, vm, _) = OwnerWalker();
        await OnOwnersContainer(vm, "Gear");
        await vm.NavigateToAddressCommand.ExecuteAsync(Other);   // the Go box re-roots, keeping the spine for Back
        ReallocateGear(dump);

        await vm.GoBackCommand.ExecuteAsync(null);
        await vm.ReentryRecheck;

        AssertGearShowsTheMovedAllocation(vm);
        Assert.Contains("Returned to the previous view", vm.StatusText);
    }

    [Fact]
    public async Task AnInSessionBookmarkOfAContainerThatMoved_ShowsItsNewAllocation()
    {
        var (dump, vm, _) = OwnerWalker();
        await OnOwnersContainer(vm, "Gear");
        var slot = vm.BookmarkSlots[0];
        vm.IsBookmarkSaveMode = true;
        await vm.LoadBookmarkCommand.ExecuteAsync(slot);   // in save mode, a slot click saves
        await vm.NavigateToAddressCommand.ExecuteAsync(Other);
        ReallocateGear(dump);

        await vm.LoadBookmarkCommand.ExecuteAsync(slot);
        await vm.ReentryRecheck;

        AssertGearShowsTheMovedAllocation(vm);
    }

    // Review round 2 (MED): when the owner was gone, or no longer had the field, the re-check returned without a word,
    // and the held rows stayed on screen and editable, their addresses in memory the game may have freed. Refresh
    // inside the view already said so for a gone owner; the re-entry now does too, and both say so for a missing field.

    [Fact]
    public async Task Back_IntoAContainerWhoseOwnerIsGone_DropsTheRows_AndSaysWhy()
    {
        var (dump, vm, _) = OwnerWalker();
        await OnOwnersContainer(vm, "Gear");
        await vm.NavigateToFieldCommand.ExecuteAsync(vm.Fields.Single());   // → [0]
        dump.RegisterStruct(Owner, new InstanceWalkResult { Address = Owner, IsStale = true });

        await vm.GoBackCommand.ExecuteAsync(null);
        await vm.ReentryRecheck;

        Assert.Empty(vm.Fields);
        Assert.Equal(LiveWalkerViewModel.GoneObjectStatus(new InstanceWalkResult { IsStale = true }), vm.StatusText);
    }

    // Review round 2: the re-check matched the field by name and offset only, so another object now at the owner's
    // address -- another instance of the class (the bookmark-recycle case), or another class with a same-named field --
    // had its container rendered as this one's. The crumb knows its owner's class and, for a UObject owner, its name.

    private static void PutAnotherOwnerAt(OwnerStub dump, string name, string cls, string clsAddr)
    {
        var gear = OwnerStub.GearField();
        dump.RegisterStruct(Owner, new InstanceWalkResult
        {
            Address = Owner, Name = name, ClassName = cls, ClassAddr = clsAddr,
            Fields = new List<LiveFieldValue>
            {
                new()
                {
                    Name = gear.Name, TypeName = gear.TypeName, Offset = gear.Offset, Size = gear.Size,
                    ArrayInnerType = "ObjectProperty", ArrayCount = 1, ArrayElemSize = 8, ArrayDataAddr = "0x5C000",
                    ArrayElements = new() { new() { Index = 0, PtrAddress = Other, PtrName = "Other_0" } },
                },
            },
        });
    }

    [Theory]
    [InlineData("Owner_7", "BP_Owner_C", OwnerCls)]     // another instance of the class
    [InlineData("Other_9", "BP_Other_C", OtherCls)]     // another class with a same-named field
    public async Task Back_IntoAContainerWhoseOwnerAddressHoldsAnotherObject_DropsTheRows(string name, string cls,
                                                                                       string clsAddr)
    {
        var (dump, vm, _) = OwnerWalker();
        await OnOwnersContainer(vm, "Gear");
        await vm.NavigateToFieldCommand.ExecuteAsync(vm.Fields.Single());   // → [0]
        PutAnotherOwnerAt(dump, name, cls, clsAddr);

        await vm.GoBackCommand.ExecuteAsync(null);
        await vm.ReentryRecheck;

        Assert.Empty(vm.Fields);
        Assert.Contains(name, vm.StatusText);
    }

    [Fact]
    public async Task Back_IntoAContainerWhoseOwnerNoLongerHasIt_DropsTheRows_AndSaysWhy()
    {
        var (dump, vm, _) = OwnerWalker();
        await OnOwnersContainer(vm, "Gear");
        await vm.NavigateToFieldCommand.ExecuteAsync(vm.Fields.Single());   // → [0]
        dump.RegisterStruct(Owner, OwnerStub.Obj(Owner, "Owner_0", "BP_Owner_C", OwnerCls));   // no Gear any more

        await vm.GoBackCommand.ExecuteAsync(null);
        await vm.ReentryRecheck;

        Assert.Empty(vm.Fields);
        Assert.Contains("Gear", vm.StatusText);
    }

    [Fact]
    public async Task Back_IntoAContainerThatDidNotMove_ReadsTheOwnerOnce_AndKeepsTheView()
    {
        var (dump, vm, _) = OwnerWalker();
        await OnOwnersContainer(vm, "Gear");
        await vm.NavigateToFieldCommand.ExecuteAsync(vm.Fields.Single());   // → [0]
        var before = dump.Walks.GetValueOrDefault(Owner);
        var resets = 0;
        vm.Fields.CollectionChanged += (_, e) =>
        {
            if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Reset) resets++;
        };

        await vm.GoBackCommand.ExecuteAsync(null);
        await vm.ReentryRecheck;

        Assert.True(dump.Walks.GetValueOrDefault(Owner) <= before + 1);   // at most the re-check: no refresh's walk
        Assert.Equal(1, resets);                                           // the held render only
        var row = vm.Fields.Single();
        Assert.Equal("0x50000", row.FieldAddress);
        Assert.Equal(Elem, row.PtrAddress);
    }

    [Fact]
    public async Task Back_FromAnElement_PutsTheArrayViewBackOnItsOwner()
    {
        var (_, vm, platform) = OwnerWalker();
        await OnOwnersContainer(vm, "Gear");
        await vm.NavigateToFieldCommand.ExecuteAsync(vm.Fields.Single());   // → [0]
        Assert.Equal(Elem, vm.CurrentAddress);

        await vm.GoBackCommand.ExecuteAsync(null);

        Assert.StartsWith("Array<", vm.CurrentClassName);
        AssertShowsTheOwner(vm);
        await vm.CopyCurrentAddressCommand.ExecuteAsync(null);   // the header's Addr chip
        Assert.Contains("10000", platform.LastClipboard);
        Assert.DoesNotContain("20000", platform.LastClipboard);
    }

    [Fact]
    public async Task CrumbJump_FromAnElement_PutsTheMapViewBackOnItsOwner()
    {
        var (_, vm, _) = OwnerWalker();
        await OnOwnersContainer(vm, "Loot");
        var mapCrumb = vm.Breadcrumbs[^1];
        await vm.NavigateToFieldCommand.ExecuteAsync(vm.Fields.Single());   // → the value of [0]
        Assert.Equal(Elem, vm.CurrentAddress);

        await vm.NavigateToBreadcrumbCommand.ExecuteAsync(mapCrumb);

        Assert.StartsWith("Map<", vm.CurrentClassName);
        AssertShowsTheOwner(vm);
    }

    [Fact]
    public async Task BackOutOfAReRoot_IntoAContainerView_ShowsItsOwner_NotTheReRootTarget()
    {
        var (_, vm, _) = OwnerWalker();
        await OnOwnersContainer(vm, "Gear");
        await vm.NavigateToAddressCommand.ExecuteAsync(Other);   // the Go box re-roots, keeping the spine for Back
        Assert.Equal(Other, vm.CurrentAddress);

        await vm.GoBackCommand.ExecuteAsync(null);

        Assert.StartsWith("Array<", vm.CurrentClassName);
        AssertShowsTheOwner(vm);
    }

    [Fact]
    public async Task Back_FromARow_PutsTheDataTableViewBackOnTheTable()
    {
        var (_, vm, _) = OwnerWalker();
        await vm.NavigateToAddressCommand.ExecuteAsync(Table);
        vm.NavigateToDataTableContainer(new LiveFieldValue { Name = "RowMap", TypeName = "DataTableRows", Offset = 0x30 },
                                        OwnerStub.TableRows());
        AssertShowsTheOwner(vm, Table, "TableFn");
        await vm.NavigateToFieldCommand.ExecuteAsync(vm.Fields.Single());   // {} on the row
        Assert.Equal(RowData, vm.CurrentAddress);

        await vm.GoBackCommand.ExecuteAsync(null);

        Assert.StartsWith("DataTable<", vm.CurrentClassName);
        AssertShowsTheOwner(vm, Table, "TableFn");
    }

    [Fact]
    public async Task Refresh_OfAContainerViewThatKnowsOnlyItsOwnersAddress_LearnsTheRestFromTheOwnersWalk()
    {
        // The shape a bookmark file or a Locate spine rebuilds: the container crumb holds the owner's address and the
        // field's name and offset -- no container field, nothing about the owner's class.
        var (_, vm, _) = OwnerWalker();
        await vm.NavigateToAddressCommand.ExecuteAsync(Elem);   // whatever was walked before
        vm.Breadcrumbs.Clear();
        vm.Breadcrumbs.Add(new BreadcrumbItem { Address = Owner, Label = "Owner_0", FieldName = "Owner_0" });
        vm.Breadcrumbs.Add(SyntheticGearCrumb());

        await vm.RefreshCommand.ExecuteAsync(null);

        Assert.StartsWith("Array<", vm.CurrentClassName);
        AssertShowsTheOwner(vm);
    }

    [Fact]
    public async Task AContainerViewOpenedInsideAWalk_RemembersItsOwner_NotTheObjectBefore()
    {
        // Opening a reference's owner drills the matched container while that walk is still being applied. The drill
        // must take the owner's class, not the one the walk is replacing.
        var (_, vm, _) = OwnerWalker();
        await vm.NavigateToAddressCommand.ExecuteAsync(Other);
        await vm.OpenReferenceOwnerCommand.ExecuteAsync(
            new ReferenceMatch { OwnerAddress = Owner, FieldName = "Gear", ElementIndex = 0 });
        Assert.StartsWith("Array<", vm.CurrentClassName);   // the drill happened
        AssertShowsTheOwner(vm);

        await vm.NavigateToFieldCommand.ExecuteAsync(vm.Fields.Single());
        await vm.GoBackCommand.ExecuteAsync(null);

        AssertShowsTheOwner(vm);
    }

    [Fact]
    public async Task TheGWorldRoot_DoesNotListTheLastObjectsFunctions()
    {
        // The actor list is rendered without a walk too, and nothing names UWorld's class: an empty list is the
        // honest one. The last object's list would let PIPE fire that object's function at the world.
        var (_, vm, _) = OwnerWalker();
        await vm.NavigateToAddressCommand.ExecuteAsync(Elem);
        Assert.Equal(new[] { "ElemFn" }, FunctionNames(vm));

        await vm.StartFromWorldCommand.ExecuteAsync(null);

        Assert.Equal("UWorld", vm.CurrentClassName);
        Assert.Empty(vm.Functions);
        Assert.False(vm.HasFunctions);
    }

    [Fact]
    public async Task AFunctionListThatLandsLate_IsNotShownForTheObjectOnScreenNow()
    {
        var (dump, vm, _) = OwnerWalker();
        var ownersList = dump.GateFunctions(OwnerCls);
        await vm.NavigateToAddressCommand.ExecuteAsync(Owner);   // its list is still loading
        await vm.NavigateToContainerCommand.ExecuteAsync(vm.Fields.Single(f => f.Name == "Gear"));
        await vm.NavigateToFieldCommand.ExecuteAsync(vm.Fields.Single());
        Assert.Equal(new[] { "ElemFn" }, FunctionNames(vm));

        ownersList.SetResult(OwnerStub.FunctionsOf(OwnerCls));   // the owner's list lands now

        Assert.Equal(Elem, vm.CurrentAddress);
        Assert.Equal(new[] { "ElemFn" }, FunctionNames(vm));
    }

    [Fact]
    public async Task AFunctionListThatFailsLate_DoesNotEmptyTheListOfTheObjectOnScreenNow()
    {
        var (dump, vm, _) = OwnerWalker();
        var ownersList = dump.GateFunctions(OwnerCls);
        await vm.NavigateToAddressCommand.ExecuteAsync(Owner);
        await vm.NavigateToContainerCommand.ExecuteAsync(vm.Fields.Single(f => f.Name == "Gear"));
        await vm.NavigateToFieldCommand.ExecuteAsync(vm.Fields.Single());

        ownersList.SetException(new InvalidOperationException("pipe timeout"));   // the owner's walk fails now

        Assert.Equal(new[] { "ElemFn" }, FunctionNames(vm));
    }

    [Fact]
    public async Task Back_FromAnElement_PutsTheSetViewBackOnItsOwner()
    {
        var (_, vm, _) = OwnerWalker();
        await OnOwnersContainer(vm, "Pals");
        await vm.NavigateToFieldCommand.ExecuteAsync(vm.Fields.Single());
        Assert.Equal(Elem, vm.CurrentAddress);

        await vm.GoBackCommand.ExecuteAsync(null);

        Assert.StartsWith("Set<", vm.CurrentClassName);
        AssertShowsTheOwner(vm);
    }

    /// <summary>A container crumb as a Locate spine or a bookmark file builds it: no container field, so a render
    /// re-hydrates the view from a walk of the owner.</summary>
    private static BreadcrumbItem SyntheticGearCrumb() => new()
    {
        Address = Owner, Label = "Gear", FieldName = "Gear", FieldOffset = 0x40, IsContainerView = true,
    };

    [Fact]
    public async Task Back_IntoARehydratedContainerView_ShowsItsOwner()
    {
        var (_, vm, _) = OwnerWalker();
        await vm.NavigateToAddressCommand.ExecuteAsync(Elem);
        vm.Breadcrumbs.Clear();
        vm.Breadcrumbs.Add(new BreadcrumbItem { Address = Owner, Label = "Owner_0", FieldName = "Owner_0" });
        vm.Breadcrumbs.Add(SyntheticGearCrumb());
        vm.Breadcrumbs.Add(new BreadcrumbItem { Address = Elem, Label = "Elem_0", FieldName = "[0]", IsPointerDeref = true });

        await vm.GoBackCommand.ExecuteAsync(null);

        Assert.StartsWith("Array<", vm.CurrentClassName);
        AssertShowsTheOwner(vm);
    }

    [Fact]
    public async Task ABookmarkedContainerView_ReResolvedFromGWorld_ShowsItsOwner()
    {
        // Saved in an earlier game session: every address is dead, and the spine is walked again from the live GWorld.
        var (dump, vm, _) = OwnerWalker();
        dump.RegisterStruct("0xA8B0", new InstanceWalkResult   // the stub's live GWorld
        {
            Name = "World", ClassName = "World", Address = "0xA8B0",
            Fields = new() { new() { Name = "OwnerRef", Offset = 0x30, TypeName = "ObjectProperty", PtrAddress = Owner } },
        });
        var slot = vm.BookmarkSlots[0];
        slot.SavedClassName = "Array<ObjectProperty>";
        slot.SavedObjectName = "Gear";
        slot.SavedAddress = "0xDEAD0";
        slot.SavedBreadcrumbs = new List<BreadcrumbItem>
        {
            new() { Address = "0xDEADW", Label = "GWorld", FieldName = "GWorld", IsPointerDeref = true },
            new() { Address = "0xDEAD0", Label = "Owner_0", FieldName = "OwnerRef", FieldOffset = 0x30, IsPointerDeref = true,
                    TargetClassName = "BP_Owner_C" },
            new() { Address = "0xDEAD0", Label = "Gear", FieldName = "Gear", FieldOffset = 0x40, IsContainerView = true },
        };
        slot.IsOccupied = true;

        await vm.LoadBookmarkCommand.ExecuteAsync(slot);

        Assert.StartsWith("Array<", vm.CurrentClassName);
        AssertShowsTheOwner(vm);
    }

    [Fact]
    public async Task ARowMapLoadStartedOnATable_DoesNotLandInThatTablesRowViewRestoredAtTheSameDepth()
    {
        // Review of d206a4ad: a container render now puts the header back on its owner, so a RowMap row view restored
        // by the Back out of a re-root matches a row load started on the same table at the same depth by address AND
        // depth. The load would append a drillable RowMap row to the rows grid.
        var (dump, vm, _) = OwnerWalker();
        dump.RegisterStruct(Other, new InstanceWalkResult
        {
            Address = Other, Name = "Other_0", ClassName = "BP_Other_C", ClassAddr = OtherCls,
            Fields = new() { new() { Name = "ToTable", TypeName = "ObjectProperty", Offset = 0x18, Size = 8,
                                     PtrAddress = Table, PtrName = "DT_Loot", PtrClassName = "DataTable" } },
        });
        await vm.NavigateToAddressCommand.ExecuteAsync(Table);
        vm.NavigateToDataTableContainer(new LiveFieldValue { Name = "RowMap", TypeName = "DataTableRows", Offset = 0x30 },
                                        OwnerStub.TableRows());
        await vm.NavigateToAddressCommand.ExecuteAsync(Other);   // re-roots, keeping [Table, RowMap] for Back
        await vm.NavigateToFieldCommand.ExecuteAsync(vm.Fields.Single(f => f.Name == "ToTable"));
        var load = vm.LastRowMapLoad!;                           // [Other, Table]: the table's row load starts here
        Assert.Equal((Table, 2), (load.DataTableAddr, load.Depth));
        Assert.Same(vm.Breadcrumbs[^1], load.Crumb);
        Assert.True(vm.RowMapLoadStillCurrent(load));            // control: nothing has moved yet

        await vm.GoBackCommand.ExecuteAsync(null);               // [Other]
        await vm.GoBackCommand.ExecuteAsync(null);               // out of the re-root: [Table, RowMap]

        Assert.StartsWith("DataTable<", vm.CurrentClassName);
        Assert.Equal(Table, vm.CurrentAddress);                  // the address and the depth match again...
        Assert.Equal(2, vm.Breadcrumbs.Count);
        Assert.False(vm.RowMapLoadStillCurrent(load));           // ...and the load still must not land
    }

    [Fact]
    public async Task ARowMapLoad_DoesNotLandWhileTheTablesRowViewIsStillOnScreen()
    {
        // Review round 2: the trail can run ahead of the grid. Back pops the row view's crumb before the table's walk
        // returns, so the trail already says "the table" -- same address, depth and crumb as a load started on it --
        // while the grid still holds the row view's rows, which the load would append a RowMap row to.
        var (dump, vm, _) = OwnerWalker();
        await vm.NavigateToAddressCommand.ExecuteAsync(Table);
        vm.NavigateToDataTableContainer(new LiveFieldValue { Name = "RowMap", TypeName = "DataTableRows", Offset = 0x30 },
                                        OwnerStub.TableRows());
        await vm.GoBackCommand.ExecuteAsync(null);               // [Table], walked: its row load starts
        var load = vm.LastRowMapLoad!;
        await vm.GoForwardCommand.ExecuteAsync(null);            // [Table, RowMap]: the rows, rendered without a walk
        var tableWalk = dump.GateWalk(Table);

        var back = vm.GoBackCommand.ExecuteAsync(null);          // pops RowMap; the table's walk waits

        Assert.StartsWith("DataTable<", vm.CurrentClassName);   // the grid still shows the rows
        Assert.False(vm.RowMapLoadStillCurrent(load));
        dump.UngateWalk(Table);
        tableWalk.SetResult(OwnerStub.Obj(Table, "DT_Loot", "DataTable", TableCls));
        await back;
        Assert.True(vm.RowMapLoadStillCurrent(vm.LastRowMapLoad!));   // control: the table's own walk loads it again
    }

    // Review rounds of d206a4ad: the header moves to the new object at once, the Functions list only when its walk
    // returns. In between, PIPE would fire a row of the OLD list at the NEW address, and the DLL finds the function by
    // name on that object's class: a name both classes have runs the new class's function with arguments laid out for
    // the old one. Emptying the list for that time resized the Functions section -- and the field grid with it, under a
    // scroll restore. So the rows stay, and every row action refuses until the new class's list lands.

    /// <summary>On a list still loading for another class, every row action is refused under its button's label, and
    /// nothing is copied.</summary>
    private static async Task AssertRowActionsRefuse(LiveWalkerViewModel vm, MockPlatformService platform)
    {
        var row = vm.Functions.First();
        foreach (var (label, run) in new (string, Func<Task>)[]
        {
            ("INV", () => vm.GenerateInvokeScriptCommand.ExecuteAsync(row)),
            ("PIPE", () => vm.InvokeViaPipeCommand.ExecuteAsync(row)),
            ("AA(Baked)", () => vm.CopyBakedScriptCommand.ExecuteAsync(row)),
            ("ASM", () => vm.AsmFunctionCommand.ExecuteAsync(row)),
        })
        {
            await run();
            Assert.StartsWith($"⚠ {label}:", vm.StatusText);
            Assert.Contains("still loading", vm.StatusText);
        }
        Assert.Null(platform.LastClipboard);
    }

    /// <summary>The control: INV on a row of the class on screen runs instead of refusing, and copies its script. Only
    /// that: the class the script names is <see cref="AssertScriptsName"/>'s business.</summary>
    private static async Task AssertRowActionWorks(LiveWalkerViewModel vm, MockPlatformService platform, string function)
    {
        await vm.GenerateInvokeScriptCommand.ExecuteAsync(vm.Functions.Single(f => f.Name == function));
        Assert.Contains(function, platform.LastClipboard);
    }

    [Fact]
    public async Task Back_IntoAContainerView_RefusesTheOldRowsUntilItsOwnersListArrives()
    {
        var (dump, vm, platform) = OwnerWalker();
        await OnOwnersContainer(vm, "Gear");
        await vm.NavigateToFieldCommand.ExecuteAsync(vm.Fields.Single());
        var ownersList = dump.GateFunctions(OwnerCls);

        await vm.GoBackCommand.ExecuteAsync(null);

        Assert.Equal(Owner, vm.CurrentAddress);
        Assert.Equal(new[] { "ElemFn" }, FunctionNames(vm));   // kept: neither the section nor the field grid moves
        await AssertRowActionsRefuse(vm, platform);
        ownersList.SetResult(OwnerStub.FunctionsOf(OwnerCls));
        AssertShowsTheOwner(vm);
        await AssertRowActionWorks(vm, platform, "OwnerFn");
    }

    [Fact]
    public async Task AWalkToAnotherClass_RefusesTheOldRowsUntilItsListArrives()
    {
        var (dump, vm, platform) = OwnerWalker();
        await OnOwnersContainer(vm, "Gear");
        var elementsList = dump.GateFunctions(ElemCls);

        await vm.NavigateToFieldCommand.ExecuteAsync(vm.Fields.Single());

        Assert.Equal(Elem, vm.CurrentAddress);
        Assert.Equal(new[] { "OwnerFn" }, FunctionNames(vm));
        await AssertRowActionsRefuse(vm, platform);
        elementsList.SetResult(OwnerStub.FunctionsOf(ElemCls));
        Assert.Equal(new[] { "ElemFn" }, FunctionNames(vm));
        await AssertRowActionWorks(vm, platform, "ElemFn");
    }

    [Fact]
    public async Task TheRefusalLine_GoesWhenTheListLands_AndNoOtherLineDoes()
    {
        // Review round 4: "still loading" outlived the load, over rows that were usable by then.
        var (dump, vm, _) = OwnerWalker();
        await OnOwnersContainer(vm, "Gear");
        var elementsList = dump.GateFunctions(ElemCls);
        await vm.NavigateToFieldCommand.ExecuteAsync(vm.Fields.Single());
        await vm.InvokeViaPipeCommand.ExecuteAsync(vm.Functions.First());
        Assert.Contains("still loading", vm.StatusText);

        elementsList.SetResult(OwnerStub.FunctionsOf(ElemCls));

        Assert.DoesNotContain("still loading", vm.StatusText);

        // The control: a line put up after the refusal is not the refusal's to take down.
        var nextList = dump.GateFunctions(OwnerCls);
        await vm.GoBackCommand.ExecuteAsync(null);
        await vm.InvokeViaPipeCommand.ExecuteAsync(vm.Functions.First());
        vm.StatusText = "Something else happened";
        nextList.SetResult(OwnerStub.FunctionsOf(OwnerCls));
        Assert.Equal("Something else happened", vm.StatusText);
    }

    // Keyed on the CLASS: an address says nothing about which functions an object has.

    [Fact]
    public async Task ARefreshThatFindsAnotherClassAtTheSameAddress_RefusesTheOldRowsUntilItsListArrives()
    {
        // A freed slot reused by another class.
        var (dump, vm, platform) = OwnerWalker();
        await vm.NavigateToAddressCommand.ExecuteAsync(Other);
        dump.RegisterStruct(Other, OwnerStub.Obj(Other, "Elem_9", "BP_Elem_C", ElemCls));
        var elementsList = dump.GateFunctions(ElemCls);

        await vm.RefreshCommand.ExecuteAsync(null);

        Assert.Equal(new[] { "OtherFn" }, FunctionNames(vm));
        await AssertRowActionsRefuse(vm, platform);
        elementsList.SetResult(OwnerStub.FunctionsOf(ElemCls));
        Assert.Equal(new[] { "ElemFn" }, FunctionNames(vm));
    }

    [Fact]
    public async Task AWalkToAnotherObjectOfTheSameClass_KeepsTheRowsUsableWhileItsListReloads()
    {
        var (dump, vm, platform) = OwnerWalker();
        dump.RegisterStruct("0x21000", OwnerStub.Obj("0x21000", "Elem_1", "BP_Elem_C", ElemCls));
        await vm.NavigateToAddressCommand.ExecuteAsync(Elem);
        dump.GateFunctions(ElemCls);

        await vm.NavigateToAddressCommand.ExecuteAsync("0x21000");

        Assert.Equal("0x21000", vm.CurrentAddress);
        await AssertRowActionWorks(vm, platform, "ElemFn");
    }

    [Fact]
    public async Task ARefreshOfTheSameObject_KeepsTheRowsUsableWhileItsListReloads()
    {
        // The same class's rows are the right ones, and keeping them keeps the Functions grid's place on every Auto tick.
        var (dump, vm, platform) = OwnerWalker();
        await vm.NavigateToAddressCommand.ExecuteAsync(Owner);
        dump.GateFunctions(OwnerCls);

        await vm.RefreshCommand.ExecuteAsync(null);

        await AssertRowActionWorks(vm, platform, "OwnerFn");
    }

    [Fact]
    public async Task AContainerViewBookmarked_ThenReloadedAfterADisconnect_ListsItsOwnersFunctions()
    {
        // A disconnect empties the Functions list; the class it was for must not still count as listed.
        var (_, vm, _) = OwnerWalker();
        await OnOwnersContainer(vm, "Gear");
        var slot = vm.BookmarkSlots[0];
        vm.SaveBookmarkToSlotCommand.Execute(slot);
        vm.ClearOnDisconnect();
        Assert.Empty(vm.Functions);

        await vm.LoadBookmarkCommand.ExecuteAsync(slot);

        Assert.StartsWith("Array<", vm.CurrentClassName);
        AssertShowsTheOwner(vm);
    }

    // ── The actions that need a class NAME name the owner's ── [LW-CONTAINER-VIEW-CLASSNAME-ACTIONS]
    //
    // A container view lists its owner's functions and fires them at the owner's address, while its header names the
    // container: Array<ObjectProperty>, Map<…>, Set<…>, DataTable<FLootRow>. INV and AA(Baked) bake a class to find an
    // instance of, and so does the PIPE dialog's Copy AA Script; they took the header's name, which no class has, so the
    // script found no instance in CE.

    /// <summary>The CE record a script action copied when no AOBMaker is connected: its description and its script.</summary>
    private static (string Description, string Script) CopiedAaRecord(MockPlatformService platform)
    {
        var entry = System.Xml.Linq.XDocument.Parse(platform.LastClipboard!).Descendants("CheatEntry").Single();
        return ((string)entry.Element("Description")!, (string)entry.Element("AssemblerScript")!);
    }

    /// <summary>INV and AA(Baked) on <paramref name="function"/> bake <paramref name="cls"/> as the class to find an
    /// instance of, and PIPE would hand its dialog the same name.</summary>
    private static async Task AssertScriptsName(LiveWalkerViewModel vm, MockPlatformService platform, string cls,
                                                string function)
    {
        var row = vm.Functions.Single(f => f.Name == function);
        await vm.GenerateInvokeScriptCommand.ExecuteAsync(row);
        var (description, script) = CopiedAaRecord(platform);
        Assert.Contains($"local OWNER_CLASS  = '{cls}'", script);
        Assert.Equal($"\"Invoke: {cls}::{function}\"", description);
        await vm.CopyBakedScriptCommand.ExecuteAsync(row);   // no inputs and no return: baked at once, no dialog
        Assert.Contains($"invokeUFunction('{cls}', ", CopiedAaRecord(platform).Script);
        Assert.Equal(cls, vm.FunctionsClassName);   // PIPE's dialog title and its Copy AA Script (no window headless)
    }

    [Theory]
    [InlineData("Gear", "Array<")]
    [InlineData("Loot", "Map<")]
    [InlineData("Pals", "Set<")]
    public async Task OnAContainerView_TheScriptsNameTheOwnersClass_NotTheViewsLabel(string container, string label)
    {
        var (_, vm, platform) = OwnerWalker();
        await OnOwnersContainer(vm, container);

        Assert.StartsWith(label, vm.CurrentClassName);   // the header still names the container
        await AssertScriptsName(vm, platform, "BP_Owner_C", "OwnerFn");
    }

    [Fact]
    public async Task Back_IntoAContainerView_TheScriptsNameTheOwnersClass_NotTheElementJustLeft()
    {
        var (_, vm, platform) = OwnerWalker();
        await OnOwnersContainer(vm, "Gear");
        await vm.NavigateToFieldCommand.ExecuteAsync(vm.Fields.Single());
        await AssertScriptsName(vm, platform, "BP_Elem_C", "ElemFn");   // the element's own view names its own class

        await vm.GoBackCommand.ExecuteAsync(null);

        AssertShowsTheOwner(vm);
        await AssertScriptsName(vm, platform, "BP_Owner_C", "OwnerFn");
    }

    [Fact]
    public async Task OnADataTableRowsView_TheScriptsNameTheTablesClass()
    {
        var (_, vm, platform) = OwnerWalker();
        await vm.NavigateToAddressCommand.ExecuteAsync(Table);
        vm.NavigateToDataTableContainer(new LiveFieldValue { Name = "RowMap", TypeName = "DataTableRows", Offset = 0x30 },
                                        OwnerStub.TableRows());

        Assert.StartsWith("DataTable<", vm.CurrentClassName);
        await AssertScriptsName(vm, platform, "DataTable", "TableFn");
    }

    [Fact]
    public async Task AContainerViewThatLearnsItsOwnerFromAWalk_LearnsItsOwnersClassName_Too()
    {
        // A Locate spine or a bookmark file: the container crumb knows only the owner's address.
        var (_, vm, platform) = OwnerWalker();
        await vm.NavigateToAddressCommand.ExecuteAsync(Elem);
        vm.Breadcrumbs.Clear();
        vm.Breadcrumbs.Add(new BreadcrumbItem { Address = Owner, Label = "Owner_0", FieldName = "Owner_0" });
        vm.Breadcrumbs.Add(SyntheticGearCrumb());

        await vm.RefreshCommand.ExecuteAsync(null);

        AssertShowsTheOwner(vm);
        await AssertScriptsName(vm, platform, "BP_Owner_C", "OwnerFn");
    }

    [Fact]
    public async Task AContainerViewRehydratedOnBack_LearnsItsOwnersClassName_Too()
    {
        var (_, vm, platform) = OwnerWalker();
        await vm.NavigateToAddressCommand.ExecuteAsync(Elem);
        vm.Breadcrumbs.Clear();
        vm.Breadcrumbs.Add(new BreadcrumbItem { Address = Owner, Label = "Owner_0", FieldName = "Owner_0" });
        vm.Breadcrumbs.Add(SyntheticGearCrumb());
        vm.Breadcrumbs.Add(new BreadcrumbItem { Address = Elem, Label = "Elem_0", FieldName = "[0]", IsPointerDeref = true });

        await vm.GoBackCommand.ExecuteAsync(null);

        AssertShowsTheOwner(vm);
        await AssertScriptsName(vm, platform, "BP_Owner_C", "OwnerFn");
    }

    [Fact]
    public async Task ABookmarkedContainerView_ReResolvedFromGWorld_TheScriptsNameTheOwnersClass()
    {
        var (dump, vm, platform) = OwnerWalker();
        dump.RegisterStruct("0xA8B0", new InstanceWalkResult
        {
            Name = "World", ClassName = "World", Address = "0xA8B0",
            Fields = new() { new() { Name = "OwnerRef", Offset = 0x30, TypeName = "ObjectProperty", PtrAddress = Owner } },
        });
        var slot = vm.BookmarkSlots[0];
        slot.SavedClassName = "Array<ObjectProperty>";
        slot.SavedObjectName = "Gear";
        slot.SavedAddress = "0xDEAD0";
        slot.SavedBreadcrumbs = new List<BreadcrumbItem>
        {
            new() { Address = "0xDEADW", Label = "GWorld", FieldName = "GWorld", IsPointerDeref = true },
            new() { Address = "0xDEAD0", Label = "Owner_0", FieldName = "OwnerRef", FieldOffset = 0x30, IsPointerDeref = true,
                    TargetClassName = "BP_Owner_C" },
            new() { Address = "0xDEAD0", Label = "Gear", FieldName = "Gear", FieldOffset = 0x40, IsContainerView = true },
        };
        slot.IsOccupied = true;

        await vm.LoadBookmarkCommand.ExecuteAsync(slot);

        AssertShowsTheOwner(vm);
        await AssertScriptsName(vm, platform, "BP_Owner_C", "OwnerFn");
    }

    // Find Refs from a container view scans its owner -- a container is not a UObject, and only its owner points at it
    // -- but its header named the container: "References to Gear" over Owner_0's referrers.

    [Theory]
    [InlineData("Gear")]
    [InlineData("Loot")]
    [InlineData("Pals")]
    public async Task FindRefs_OnAContainerView_ScansTheOwner_AndSaysSo(string container)
    {
        var (dump, vm, _) = OwnerWalker();
        await OnOwnersContainer(vm, container);

        await vm.FindReferencesCommand.ExecuteAsync(null);

        Assert.Equal(Owner, dump.RefsScannedAt);
        Assert.Equal("References to Owner_0 (none found)", vm.ReferencesHeader);
    }

    [Fact]
    public async Task FindRefs_OnADataTableRowsView_ScansTheTable_AndSaysSo()
    {
        var (dump, vm, _) = OwnerWalker();
        await vm.NavigateToAddressCommand.ExecuteAsync(Table);
        vm.NavigateToDataTableContainer(new LiveFieldValue { Name = "RowMap", TypeName = "DataTableRows", Offset = 0x30 },
                                        OwnerStub.TableRows());

        await vm.FindReferencesCommand.ExecuteAsync(null);

        Assert.Equal(Table, dump.RefsScannedAt);
        Assert.Equal("References to DT_Loot (none found)", vm.ReferencesHeader);
    }

    [Fact]
    public async Task FindRefs_OnAContainerView_UnderALocateSpinesElementCrumb_NamesTheOwner_NotTheIndex()
    {
        // Review of 4159e0e7..f092ec12: a Locate spine labels the crumb of an object-array hop "[0]" (its address is
        // the object reached), so the crumb before a container drilled there names an index, not the owner.
        var (dump, vm, _) = OwnerWalker();
        await vm.NavigateToAddressCommand.ExecuteAsync(Other);
        vm.Breadcrumbs.Clear();
        vm.Breadcrumbs.Add(new BreadcrumbItem { Address = Other, Label = "Other_0", FieldName = "Other_0" });
        vm.Breadcrumbs.Add(new BreadcrumbItem { Address = Owner, Label = "[0]", FieldName = "[0]", IsPointerDeref = true });
        vm.Breadcrumbs.Add(new BreadcrumbItem { Address = Elem, Label = "Elem_0", FieldName = "Elem_0", IsPointerDeref = true });
        await vm.GoBackCommand.ExecuteAsync(null);       // walks the "[0]" crumb's object: the owner
        Assert.Equal(Owner, vm.CurrentAddress);
        Assert.Equal("Owner_0", vm.CurrentObjectName);   // the owner's own view names it
        await vm.NavigateToContainerCommand.ExecuteAsync(vm.Fields.Single(f => f.Name == "Gear"));

        await vm.FindReferencesCommand.ExecuteAsync(null);

        Assert.Equal(Owner, dump.RefsScannedAt);
        Assert.Equal("References to Owner_0 (none found)", vm.ReferencesHeader);
    }

    [Fact]
    public async Task FindRefs_OnAContainerInAnInlineStruct_KeepsTheStructViewsName_AfterARefresh()
    {
        // Review round 2: a walk of an inline struct names the struct's TYPE (or whatever FName its bytes decode to),
        // which the struct view's header replaces with its crumb's label. The owner name a Refresh re-learned from that
        // walk did not, so the scan's name changed on a Refresh.
        const string Holder = "0x80000", StatsAddr = "0x80020", StatsCls = "0xC800";
        var (dump, vm, _) = OwnerWalker();
        dump.RegisterStruct(Holder, new InstanceWalkResult
        {
            Address = Holder, Name = "Holder_0", ClassName = "BP_Holder_C", ClassAddr = "0xC700",
            Fields = new List<LiveFieldValue>
            {
                new() { Name = "Stats", TypeName = "StructProperty", Offset = 0x20, Size = 0x10,
                        StructTypeName = "FPlayerStats", StructClassAddr = StatsCls, StructDataAddr = StatsAddr },
            },
        });
        dump.RegisterStruct(StatsAddr, new InstanceWalkResult   // a raw struct's walk names its type
        {
            Address = StatsAddr, Name = "FPlayerStats", ClassName = "FPlayerStats", ClassAddr = StatsCls,
            Fields = new List<LiveFieldValue> { OwnerStub.GearField() },
        });
        await vm.NavigateToAddressCommand.ExecuteAsync(Holder);
        await vm.NavigateToFieldCommand.ExecuteAsync(vm.Fields.Single());   // {} Stats
        var structViewName = vm.CurrentObjectName;
        Assert.NotEqual("FPlayerStats", structViewName);                      // the header shows the crumb's label
        await vm.NavigateToContainerCommand.ExecuteAsync(vm.Fields.Single(f => f.Name == "Gear"));
        await vm.RefreshCommand.ExecuteAsync(null);

        await vm.FindReferencesCommand.ExecuteAsync(null);

        Assert.Equal($"References to {structViewName} (none found)", vm.ReferencesHeader);
    }

    // [LEAD-LW-SYNTH-CONTAINER-STRUCT-OWNER] A container crumb rebuilt from a bookmark file or a Locate spine has no
    // ContainerField, and is re-hydrated by walking its owner. It walked the owner with the CONTAINER crumb's class,
    // which a container crumb never has: an inline-struct owner's bytes were read as a UObject, the field was not found,
    // and the view fell through to a plain walk of that address. Refresh walks with the owner crumb's class.

    private const string Holder = "0x80000", StatsAddr = "0x80020", StatsCls = "0xC800";

    private static OwnerStub StructOwnerStub()
    {
        var dump = new OwnerStub();
        dump.RegisterStruct(Holder, new InstanceWalkResult
        {
            Address = Holder, Name = "Holder_0", ClassName = "BP_Holder_C", ClassAddr = "0xC700",
            Fields = new List<LiveFieldValue>
            {
                new() { Name = "Stats", TypeName = "StructProperty", Offset = 0x20, Size = 0x50,
                        StructTypeName = "FPlayerStats", StructClassAddr = StatsCls, StructDataAddr = StatsAddr },
            },
        });
        dump.RegisterStruct(StatsAddr, new InstanceWalkResult
        {
            Address = StatsAddr, Name = "FPlayerStats", ClassName = "FPlayerStats", ClassAddr = StatsCls,
            Fields = new List<LiveFieldValue> { OwnerStub.GearField() },
        });
        dump.RegisterClasslessWalk(StatsAddr, new InstanceWalkResult   // the struct's bytes read as a UObject
        {
            Address = StatsAddr, Name = "None", ClassName = "", ClassAddr = "",
            Fields = new List<LiveFieldValue> { new() { Name = "Unknown_0", TypeName = "IntProperty", Offset = 0x8, Size = 4 } },
        });
        return dump;
    }

    /// <summary>A bookmark FILE's spine: Holder, its inline Stats struct, and Stats' Gear array, saved addresses.</summary>
    private static async Task LoadStructOwnedGearBookmark(LiveWalkerViewModel vm)
    {
        var slot = vm.BookmarkSlots[0];
        slot.SavedClassName = "Array<ObjectProperty>";
        slot.SavedAddress = StatsAddr;
        slot.SavedBreadcrumbs = new List<BreadcrumbItem>
        {
            new() { Address = Holder, Label = "Holder_0", FieldName = Holder, IsPointerDeref = true },
            new() { Address = StatsAddr, Label = "Stats", FieldName = "Stats", FieldOffset = 0x20, ClassAddr = StatsCls },
            new() { Address = StatsAddr, Label = "Gear", FieldName = "Gear", FieldOffset = 0x40, IsContainerView = true },
        };
        slot.IsOccupied = true;
        await vm.LoadBookmarkCommand.ExecuteAsync(slot);
    }

    [Fact]
    public async Task ABookmarkedContainerInAnInlineStruct_RestoresTheContainersElements()
    {
        var dump = StructOwnerStub();
        var vm = new LiveWalkerViewModel(dump, new MockLoggingService(), new MockPlatformService(Path.GetTempPath()));

        await LoadStructOwnedGearBookmark(vm);

        Assert.Equal(new[] { "[0]" }, vm.Fields.Select(f => f.Name).ToArray());   // was the struct read as a UObject
        Assert.Equal(StatsCls, vm.Breadcrumbs[^1].OwnerClassAddr);
    }

    [Fact]
    public async Task BackIntoABookmarkedContainerInAnInlineStruct_ShowsTheContainersElements()
    {
        var dump = StructOwnerStub();
        var vm = new LiveWalkerViewModel(dump, new MockLoggingService(), new MockPlatformService(Path.GetTempPath()));
        await LoadStructOwnedGearBookmark(vm);
        await vm.NavigateToFieldCommand.ExecuteAsync(vm.Fields.Single());   // [0] -> Elem_0
        Assert.Equal(Elem, vm.CurrentAddress);

        await vm.GoBackCommand.ExecuteAsync(null);

        Assert.Equal(new[] { "[0]" }, vm.Fields.Select(f => f.Name).ToArray());
    }

    // [LEAD-DT-EXPORT-64ROWS] Copy CE Field on a container view exports the elements selected, by choice fewer than the
    // total: the export's limit warning reported that as the container being cut short.

    [Fact]
    public async Task CopyCeField_OfOneSelectedElement_DoesNotCallTheContainerCutShort()
    {
        const string Big = "0x90000";
        var (dump, vm, platform) = OwnerWalker();
        dump.RegisterStruct(Big, new InstanceWalkResult
        {
            Address = Big, Name = "Big_0", ClassName = "BP_Big_C", ClassAddr = "0xC900",
            Fields = new List<LiveFieldValue>
            {
                new() { Name = "Items", TypeName = "ArrayProperty", Offset = 0x40, Size = 0x10,
                        ArrayInnerType = "IntProperty", ArrayCount = 300, ArrayElemSize = 4, ArrayDataAddr = "0x91000",
                        ArrayElements = new() { new() { Index = 0, Value = "1" }, new() { Index = 1, Value = "2" } } },
            },
        });
        vm.ArrayLimit = 2;
        await vm.NavigateToAddressCommand.ExecuteAsync(Big);
        await vm.NavigateToContainerCommand.ExecuteAsync(vm.Fields.Single());
        vm.SelectedField = vm.Fields.Single(f => f.Name == "[1]");

        await vm.ExportCeFieldXmlCommand.ExecuteAsync(null);

        Assert.NotNull(platform.LastClipboard);
        Assert.StartsWith("Copied:", vm.StatusText);
        Assert.DoesNotContain("Container element limit", vm.StatusText);   // was "Items (Array: 300 total, 1 loaded)"
    }

    [Fact]
    public async Task CopyCeField_OfEveryLoadedElement_StillCallsTheContainerCutShort()
    {
        // Review round 1 of [LEAD-DT-EXPORT-64ROWS]: a selection of every loaded row is not a choice to leave the rest
        // out -- the rest were never read. Ctrl+A in a view of 2 of 300 lost the warning the export used to give.
        const string Big = "0x90000";
        var (dump, vm, platform) = OwnerWalker();
        dump.RegisterStruct(Big, new InstanceWalkResult
        {
            Address = Big, Name = "Big_0", ClassName = "BP_Big_C", ClassAddr = "0xC900",
            Fields = new List<LiveFieldValue>
            {
                new() { Name = "Items", TypeName = "ArrayProperty", Offset = 0x40, Size = 0x10,
                        ArrayInnerType = "IntProperty", ArrayCount = 300, ArrayElemSize = 4, ArrayDataAddr = "0x91000",
                        ArrayElements = new() { new() { Index = 0, Value = "1" }, new() { Index = 1, Value = "2" } } },
            },
        });
        vm.ArrayLimit = 2;
        await vm.NavigateToAddressCommand.ExecuteAsync(Big);
        await vm.NavigateToContainerCommand.ExecuteAsync(vm.Fields.Single());
        vm.UpdateSelectedFields(vm.Fields.ToList());   // Ctrl+A

        await vm.ExportCeFieldXmlCommand.ExecuteAsync(null);

        Assert.NotNull(platform.LastClipboard);
        Assert.Contains("Items (Array: 300 total, 2 loaded)", vm.StatusText);
    }

    // [CEXPORT-NESTED-TRUNC-NOTE] A container the export cut at the Array Limit inside a struct it walked.

    private static OwnerStub NestedArrayStub()
    {
        var dump = StructOwnerStub();
        dump.RegisterStruct(StatsAddr, new InstanceWalkResult
        {
            Address = StatsAddr, Name = "FPlayerStats", ClassName = "FPlayerStats", ClassAddr = StatsCls,
            Fields = new List<LiveFieldValue>
            {
                new() { Name = "Slots", TypeName = "ArrayProperty", Offset = 0x0, Size = 0x10,
                        ArrayInnerType = "IntProperty", ArrayCount = 300, ArrayElemSize = 4, ArrayDataAddr = "0x92000",
                        ArrayElements = new() { new() { Index = 0, Value = "1" }, new() { Index = 1, Value = "2" } } },
            },
        });
        return dump;
    }

    [Fact]
    public async Task CopyCeXml_SaysANestedContainerWasCutAtTheArrayLimit()
    {
        var dump = NestedArrayStub();
        var platform = new MockPlatformService(Path.GetTempPath());
        var vm = new LiveWalkerViewModel(dump, new MockLoggingService(), platform) { ArrayLimit = 2 };
        await vm.NavigateToAddressCommand.ExecuteAsync(Holder);

        await vm.ExportCeXmlCommand.ExecuteAsync(null);

        Assert.NotNull(platform.LastClipboard);
        Assert.Contains("Slots (Array: 300 total, 2 loaded, nested)", vm.StatusText);   // was no warning at all
    }

    [Fact]
    public async Task CopyCeField_SaysANestedContainerWasCutAtTheArrayLimit()
    {
        var dump = NestedArrayStub();
        var platform = new MockPlatformService(Path.GetTempPath());
        var vm = new LiveWalkerViewModel(dump, new MockLoggingService(), platform) { ArrayLimit = 2 };
        await vm.NavigateToAddressCommand.ExecuteAsync(Holder);
        vm.SelectedField = vm.Fields.Single(f => f.Name == "Stats");

        await vm.ExportCeFieldXmlCommand.ExecuteAsync(null);

        Assert.NotNull(platform.LastClipboard);
        Assert.Contains("Slots (Array: 300 total, 2 loaded, nested)", vm.StatusText);
    }

    [Fact]
    public async Task PushToCeStructureDissect_SaysANestedContainerWasCutAtTheArrayLimit()
    {
        var dump = NestedArrayStub();
        var bridge = new ScriptedAobMakerBridge { Available = true };
        var vm = new LiveWalkerViewModel(dump, new MockLoggingService(), new MockPlatformService(Path.GetTempPath()),
                                         bridge) { ArrayLimit = 2 };
        vm.ApplyAobMakerProbe(true);
        await vm.NavigateToAddressCommand.ExecuteAsync(Holder);

        await vm.PushCsxToCeCommand.ExecuteAsync(null);

        Assert.Single(bridge.AaScripts);
        Assert.Contains("Slots (Array: 300 total, 2 loaded, nested)", vm.StatusText);
    }

    [Fact]
    public async Task ExportCsx_SaysANestedContainerWasCutAtTheArrayLimit()
    {
        // Review round 1: Export CSX (to a file) passed the walked fields too, and no test held that wiring.
        var dump = NestedArrayStub();
        var file = Path.Combine(Path.GetTempPath(), $"UE5DumpNestedCsx_{Guid.NewGuid():N}.csx");
        var platform = new MockPlatformService(Path.GetTempPath()) { NextSavePath = file };
        var vm = new LiveWalkerViewModel(dump, new MockLoggingService(), platform) { ArrayLimit = 2 };
        await vm.NavigateToAddressCommand.ExecuteAsync(Holder);
        try
        {
            await vm.ExportCsx77Command.ExecuteAsync(null);

            Assert.True(File.Exists(file));
            Assert.Contains("Slots (Array: 300 total, 2 loaded, nested)", vm.StatusText);
        }
        finally { File.Delete(file); }
    }

    [Fact]
    public async Task CopyCeXml_SaysAContainerWasCutInAPointerTargetItWalked()
    {
        // Review round 1: the pointer-target half of the nested scan (the resolver's resolvedInstances) had no test.
        const string Ptr = "0x93000";
        var dump = NestedArrayStub();
        dump.RegisterStruct("0x94000", new InstanceWalkResult
        {
            Address = "0x94000", Name = "Holder2_0", ClassName = "BP_Holder2_C", ClassAddr = "0xCA00",
            Fields = new List<LiveFieldValue>
            {
                new() { Name = "Target", TypeName = "ObjectProperty", Offset = 0x28, Size = 8,
                        PtrAddress = Ptr, PtrName = "Pointee_0", PtrClassName = "BP_Pointee_C", PtrClassAddr = "0xCB00" },
            },
        });
        dump.RegisterStruct(Ptr, new InstanceWalkResult
        {
            Address = Ptr, Name = "Pointee_0", ClassName = "BP_Pointee_C", ClassAddr = "0xCB00",
            Fields = new List<LiveFieldValue>
            {
                new() { Name = "Bag", TypeName = "ArrayProperty", Offset = 0x30, Size = 0x10,
                        ArrayInnerType = "IntProperty", ArrayCount = 300, ArrayElemSize = 4, ArrayDataAddr = "0x95000",
                        ArrayElements = new() { new() { Index = 0, Value = "1" }, new() { Index = 1, Value = "2" } } },
            },
        });
        var platform = new MockPlatformService(Path.GetTempPath());
        var vm = new LiveWalkerViewModel(dump, new MockLoggingService(), platform) { ArrayLimit = 2, CsxDrilldownDepth = 1 };
        await vm.NavigateToAddressCommand.ExecuteAsync("0x94000");

        await vm.ExportCeXmlCommand.ExecuteAsync(null);

        Assert.NotNull(platform.LastClipboard);
        Assert.Contains("Bag (Array: 300 total, 2 loaded, nested)", vm.StatusText);
    }

    [Fact]
    public async Task Csx_HandsBackTheFieldsItWalked_SoTheCallerCanSayWhatWasCut()
    {
        var dump = NestedArrayStub();
        var walked = new List<LiveFieldValue>();
        var ct = TestContext.Current.CancellationToken;
        var holderFields = (await dump.WalkInstanceAsync(Holder, ct: ct)).Fields;

        await UE5DumpUI.Services.CsxExportService.GenerateCsxAsync(dump, "Holder", holderFields, arrayLimit: 2,
            resolvedFieldsOut: walked, ct: ct);

        Assert.Contains(walked, f => f.Name == "Slots" && f.ArrayCount == 300);
    }

    [Fact]
    public async Task ARefreshThatKeepsTheRows_CountsTheMatchesOnTheRowsShown()
    {
        // [LEAD-LW-INPLACE-SEARCHCOUNT] A refresh of the same object with the same layout copies the values onto the
        // rows shown and keeps their type label. The match count came from the fresh rows and the highlights from the
        // kept ones, so a guessed row whose label gained its "?" counted a match nothing showed.
        const string Obj = "0x96000";
        var (dump, vm, _) = OwnerWalker();
        InstanceWalkResult Walk(string type) => new()
        {
            Address = Obj, Name = "Obj_0", ClassName = "BP_Obj_C", ClassAddr = "0xCD00",
            Fields = new List<LiveFieldValue>
            {
                new() { Name = "?0x10_int", TypeName = type, Offset = 0x10, Size = 4, IsGuessed = true, TypedValue = "7" },
            },
        };
        dump.RegisterStruct(Obj, Walk("IntProperty"));
        await vm.NavigateToAddressCommand.ExecuteAsync(Obj);
        vm.SearchText = "Property?";
        Assert.Equal(0, vm.SearchMatchCount);

        dump.RegisterStruct(Obj, Walk("IntProperty?"));
        await vm.RefreshCommand.ExecuteAsync(null);

        Assert.Equal(vm.Fields.Count(f => f.IsSearchMatch), vm.SearchMatchCount);   // was 1 counted, 0 highlighted
    }

    [Fact]
    public async Task FindRefs_OnAnObject_StillNamesTheObject()
    {
        var (dump, vm, _) = OwnerWalker();
        await vm.NavigateToAddressCommand.ExecuteAsync(Owner);

        await vm.FindReferencesCommand.ExecuteAsync(null);

        Assert.Equal(Owner, dump.RefsScannedAt);
        Assert.Equal("References to Owner_0 (none found)", vm.ReferencesHeader);
    }

    // Copy Addr on a row with no address of its own fell back to the object shown + the row's Offset. A container row's
    // offset counts from the container's buffer, so that copied a slot inside the owner instead -- index 0 of an array
    // is the owner's vtable pointer -- and a DataTable row whose pointer reads null copied an address in the table's
    // own header.

    [Fact]
    public async Task CopyAddr_OnADataTableRowWhosePointerIsNull_CopiesNothing_AndSaysWhy()
    {
        var (_, vm, platform) = OwnerWalker();
        await vm.NavigateToAddressCommand.ExecuteAsync(Table);
        var rows = OwnerStub.TableRows();
        rows.Rows[0] = new() { SparseIndex = 0, RowName = "Sword (null)", DataAddr = "0x0" };   // the DLL's null row
        vm.NavigateToDataTableContainer(new LiveFieldValue { Name = "RowMap", TypeName = "DataTableRows", Offset = 0x30 },
                                        rows);

        await vm.CopyFieldAddressCommand.ExecuteAsync(vm.Fields.Single());

        Assert.Null(platform.LastClipboard);   // was 0x40008: the table + FNameSize
        Assert.StartsWith("⚠ Addr:", vm.StatusText);
    }

    [Fact]
    public async Task CopyAddr_OnARowsViewTheSpineHasLeft_CopiesNothing_AndSaysTheRowHasNoAddress()
    {
        // Review of 60fff318..f092ec12: Back pops the rows view's crumb before the table's walk returns, so the trail
        // says "the table" while the grid still shows its rows -- and the trail is not a container view, so the old
        // fallback copied the table + the row's offset again. Round 2: the row has no address at ANY level, so it is
        // told so; "wait and try again" would end in the same refusal.
        var (dump, vm, platform) = OwnerWalker();
        await vm.NavigateToAddressCommand.ExecuteAsync(Table);
        var rows = OwnerStub.TableRows();
        rows.Rows[0] = new() { SparseIndex = 0, RowName = "Sword (null)", DataAddr = "0x0" };
        vm.NavigateToDataTableContainer(new LiveFieldValue { Name = "RowMap", TypeName = "DataTableRows", Offset = 0x30 },
                                        rows);
        var nullRow = vm.Fields.Single();
        var tableWalk = dump.GateWalk(Table);
        var back = vm.GoBackCommand.ExecuteAsync(null);   // pops RowMap; the table's walk waits
        Assert.Same(nullRow, vm.Fields.Single());          // the grid still shows the rows

        await vm.CopyFieldAddressCommand.ExecuteAsync(nullRow);

        Assert.Null(platform.LastClipboard);               // was 0x40008 again
        Assert.StartsWith("⚠ Addr: nothing done — no address was read for this row", vm.StatusText);
        dump.UngateWalk(Table);
        tableWalk.SetResult(OwnerStub.Obj(Table, "DT_Loot", "DataTable", TableCls));
        await back;
    }

    [Fact]
    public async Task TheTablesRowMapRow_CarriesTheRowMapsAddress_SoCopyAddrCopiesIt()
    {
        // Review round 4: f4d19e45 refused every row without an address, and the RowMap row a table's own view appends
        // after its walk had none -- the removed fallback's table + RowMapOffset was its right address (the RowMap's
        // TSparseArray, where the DLL reads the rows). The row lands through the UI dispatcher, so its builder is
        // what is held here.
        var rowMap = LiveWalkerViewModel.SyntheticRowMapField(OwnerStub.TableRows(), Table);
        Assert.Equal("0x40030", rowMap.FieldAddress);   // the table + RowMapOffset 0x30: the Address column, Hex, +CE

        var (_, vm, platform) = OwnerWalker();
        await vm.NavigateToAddressCommand.ExecuteAsync(Table);
        await vm.CopyFieldAddressCommand.ExecuteAsync(rowMap);
        Assert.Contains("40030", platform.LastClipboard);
    }

    [Fact]
    public async Task CopyAddr_OnARowsGridLeftOverByAFailedReRoot_CopiesNothing()
    {
        // Review round 3: a re-root that fails after clearing the spine leaves the rows with no crumb at all, so neither
        // "the view is a container" nor "the spine has left it" could be told, and the fallback copied the table + the
        // row's offset. Every row a walk renders carries its address, so that fallback never had a right answer.
        var (_, vm, platform) = OwnerWalker();
        await vm.NavigateToAddressCommand.ExecuteAsync(Table);
        var rows = OwnerStub.TableRows();
        rows.Rows[0] = new() { SparseIndex = 0, RowName = "Sword (null)", DataAddr = "0x0" };
        vm.NavigateToDataTableContainer(new LiveFieldValue { Name = "RowMap", TypeName = "DataTableRows", Offset = 0x30 },
                                        rows);
        var nullRow = vm.Fields.Single();
        await vm.NavigateToAddressCommand.ExecuteAsync("not-an-address");   // fails after clearing the spine
        Assert.Empty(vm.Breadcrumbs);
        Assert.Same(nullRow, vm.Fields.Single());                           // the rows stay, rootless

        await vm.CopyFieldAddressCommand.ExecuteAsync(nullRow);

        Assert.Null(platform.LastClipboard);                                 // was 0x40008
        Assert.StartsWith("⚠ Addr: nothing done — no address was read for this row", vm.StatusText);
    }

    [Fact]
    public async Task CopyAddr_ThatCopies_TakesDownTheDefinitionViewsRefusal_Too()
    {
        // Review round 2: the definition-view refusal returned before _addrRefusal was set, so an Addr that copied once
        // the instance's walk landed left "this is a type definition" up over it.
        const string Definition = "0xDEF00";
        var (dump, vm, platform) = OwnerWalker();
        dump.RegisterStruct(Definition, new InstanceWalkResult
        {
            Address = Definition, Name = "FLootRow", ClassName = "ScriptStruct", IsDefinition = true,
            Fields = new List<LiveFieldValue> { new() { Name = "Damage", TypeName = "IntProperty", Offset = 0x10, Size = 4 } },
        });
        await vm.NavigateToAddressCommand.ExecuteAsync(Other);
        await vm.NavigateToAddressCommand.ExecuteAsync(Definition);   // re-roots, keeping the spine for Back
        var definitionRow = vm.Fields.Single();
        var otherWalk = dump.GateWalk(Other);
        var back = vm.GoBackCommand.ExecuteAsync(null);                // the definition's rows stay until it lands
        await vm.CopyFieldAddressCommand.ExecuteAsync(definitionRow);
        Assert.Equal(LiveWalkerViewModel.DefinitionNoDataText("Addr"), vm.StatusText);
        dump.UngateWalk(Other);
        otherWalk.SetResult(OwnerStub.Obj(Other, "Other_0", "BP_Other_C", OtherCls));
        await back;

        await vm.CopyFieldAddressCommand.ExecuteAsync(vm.Fields.Single());

        Assert.Contains("30008", platform.LastClipboard);
        Assert.DoesNotContain("type definition", vm.StatusText);
    }

    [Fact]
    public async Task CopyAddr_OnAnArrayRowWithoutAnAddress_CopiesNothing_AndSaysWhy()
    {
        // A guard: no live reply has been seen to send an array's elements without its data pointer.
        var (dump, vm, platform) = OwnerWalker();
        var gear = new LiveFieldValue   // GearField without its data pointer
        {
            Name = "Gear", TypeName = "ArrayProperty", Offset = 0x40, Size = 0x10,
            ArrayInnerType = "ObjectProperty", ArrayCount = 1, ArrayElemSize = 8, ArrayDataAddr = "",
            ArrayElements = new() { new() { Index = 0, PtrAddress = Elem, PtrName = "Elem_0", PtrClassName = "BP_Elem_C" } },
        };
        dump.RegisterStruct(Owner, new InstanceWalkResult
        {
            Address = Owner, Name = "Owner_0", ClassName = "BP_Owner_C", ClassAddr = OwnerCls,
            Fields = new List<LiveFieldValue> { gear },
        });
        await OnOwnersContainer(vm, "Gear");

        await vm.CopyFieldAddressCommand.ExecuteAsync(vm.Fields.Single());

        Assert.Null(platform.LastClipboard);   // was 0x10000: the owner's own first qword
        Assert.StartsWith("⚠ Addr:", vm.StatusText);
    }

    [Fact]
    public async Task CopyAddr_OnAnArrayRow_CopiesTheElementsAddress()
    {
        var (_, vm, platform) = OwnerWalker();
        await OnOwnersContainer(vm, "Gear");

        await vm.CopyFieldAddressCommand.ExecuteAsync(vm.Fields.Single());

        Assert.Contains("50000", platform.LastClipboard);   // TArray::Data + 0
    }

    [Fact]
    public async Task AContainerViewBookmarkedInSession_TheScriptsNameTheOwnersClass()
    {
        var (_, vm, platform) = OwnerWalker();
        await OnOwnersContainer(vm, "Gear");
        var slot = vm.BookmarkSlots[0];
        vm.SaveBookmarkToSlotCommand.Execute(slot);
        await vm.NavigateToAddressCommand.ExecuteAsync(Other);

        await vm.LoadBookmarkCommand.ExecuteAsync(slot);

        AssertShowsTheOwner(vm);
        await AssertScriptsName(vm, platform, "BP_Owner_C", "OwnerFn");
    }
}

/// <summary>
/// The browser-style shortcut policy. Split out from the window plumbing precisely
/// so it can be tested: a physical 4th mouse button and a real Alt+Arrow press can't
/// be produced headlessly, but the decision they feed into can.
/// </summary>
public class LiveWalkerNavShortcutTests
{
    private const bool OnTab = true, Active = true;

    [Theory]
    [InlineData(Key.Left, NavShortcut.Back)]
    [InlineData(Key.Right, NavShortcut.Forward)]
    public void AltArrow_Navigates(Key key, NavShortcut expected)
        => Assert.Equal(expected,
            LiveWalkerNavShortcuts.Resolve(key, KeyModifiers.Alt, OnTab, Active));

    [Theory]
    [InlineData(Key.Left)]
    [InlineData(Key.Right)]
    public void BareArrow_IsLeftToTheGrid(Key key)
        => Assert.Equal(NavShortcut.None,
            LiveWalkerNavShortcuts.Resolve(key, KeyModifiers.None, OnTab, Active));

    [Fact]
    public void CtrlAltArrow_IsNotOurs()
    {
        // Display drivers bind Ctrl+Alt+Arrow to screen rotation; an exact-modifier
        // match keeps us out of combos other software owns.
        Assert.Equal(NavShortcut.None, LiveWalkerNavShortcuts.Resolve(
            Key.Left, KeyModifiers.Control | KeyModifiers.Alt, OnTab, Active));
    }

    [Theory]
    [InlineData(Key.BrowserBack, NavShortcut.Back)]
    [InlineData(Key.BrowserForward, NavShortcut.Forward)]
    public void DedicatedBrowserKeys_WorkWithOrWithoutModifiers(Key key, NavShortcut expected)
    {
        Assert.Equal(expected, LiveWalkerNavShortcuts.Resolve(key, KeyModifiers.None, OnTab, Active));
        Assert.Equal(expected, LiveWalkerNavShortcuts.Resolve(key, KeyModifiers.Shift, OnTab, Active));
    }

    [Theory]
    [InlineData(PointerUpdateKind.XButton1Pressed, NavShortcut.Back)]
    [InlineData(PointerUpdateKind.XButton2Pressed, NavShortcut.Forward)]
    public void SideButtons_Navigate(PointerUpdateKind kind, NavShortcut expected)
        => Assert.Equal(expected, LiveWalkerNavShortcuts.Resolve(kind, OnTab, Active));

    [Theory]
    [InlineData(PointerUpdateKind.LeftButtonPressed)]
    [InlineData(PointerUpdateKind.RightButtonPressed)]
    [InlineData(PointerUpdateKind.MiddleButtonPressed)]
    [InlineData(PointerUpdateKind.Other)]
    public void OrdinaryClicks_PassStraightThrough(PointerUpdateKind kind)
        => Assert.Equal(NavShortcut.None, LiveWalkerNavShortcuts.Resolve(kind, OnTab, Active));

    [Fact]
    public void OffTheLiveWalkerTab_NothingFires()
    {
        Assert.Equal(NavShortcut.None,
            LiveWalkerNavShortcuts.Resolve(Key.Left, KeyModifiers.Alt, isLiveWalkerTab: false, Active));
        Assert.Equal(NavShortcut.None,
            LiveWalkerNavShortcuts.Resolve(Key.BrowserBack, KeyModifiers.None, isLiveWalkerTab: false, Active));
        Assert.Equal(NavShortcut.None,
            LiveWalkerNavShortcuts.Resolve(PointerUpdateKind.XButton1Pressed, isLiveWalkerTab: false, Active));
    }

    [Fact]
    public void WithTheWindowInTheBackground_NothingFires()
    {
        // Matters for the mouse route: clicking a side button on a background window
        // activates it first, so the press really does arrive.
        Assert.Equal(NavShortcut.None,
            LiveWalkerNavShortcuts.Resolve(Key.Left, KeyModifiers.Alt, OnTab, windowActive: false));
        Assert.Equal(NavShortcut.None,
            LiveWalkerNavShortcuts.Resolve(PointerUpdateKind.XButton1Pressed, OnTab, windowActive: false));
    }
}
