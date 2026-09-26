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
