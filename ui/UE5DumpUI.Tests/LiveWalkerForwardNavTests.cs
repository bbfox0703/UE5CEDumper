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

        public override Task<InstanceWalkResult> WalkInstanceAsync(string addr, string? classAddr = null,
            int arrayLimit = 64, int previewLimit = 2, bool fillGaps = false, bool lean = false,
            CancellationToken ct = default)
            => _walkGates.TryGetValue(addr, out var gate)
                ? gate.Task
                : base.WalkInstanceAsync(addr, classAddr, arrayLimit, previewLimit, fillGaps, lean, ct);

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

    /// <summary>The control: INV on a row of the class on screen copies its script.</summary>
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
