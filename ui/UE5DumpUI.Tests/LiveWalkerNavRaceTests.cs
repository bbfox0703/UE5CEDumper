using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UE5DumpUI.Core;
using UE5DumpUI.Models;
using UE5DumpUI.ViewModels;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// Audit V3 + V4 — Live Walker state written AFTER an await, against VM state that the
/// user may have changed in the meantime.
///
/// Both commands are long round-trips with nothing gating the panel while they run:
/// `IsLoading` is bound only to a ProgressBar's IsVisible, never to IsEnabled, and
/// `find_refs_to_uobject` rides the BULK pipe lane while `walk_instance` rides the
/// interactive one, so there is no ordering between them at all. The DLL-side reference
/// scan has a 30-second deadline, which is how wide the window gets.
///
/// V4 — a drill-down appends to `Breadcrumbs` after its walk returns. If the user went
/// Back meanwhile, the new crumb is grafted onto a DIFFERENT parent and its `FieldOffset`
/// describes a spine that no longer exists. That corruption is not confined to the panel:
/// it ships into CE XML and CSX exports and is PERSISTED into bookmarks.<hash>.json, so it
/// survives a restart.
///
/// V3 — the reference scan refills `References` and composes `ReferencesHeader` from
/// live VM state, so the rows for object A appear under "References to B". Not cosmetic:
/// Open on such a row pre-arms the scroll hint from the referring field and re-roots the
/// walker, giving a real navigation into an object that references something else.
///
/// ⚠ Two things these tests are built to prove, beyond "the guard exists":
///   * the guard keys on crumb IDENTITY, not Breadcrumbs.Count. Back-then-Forward restores
///     the count while changing the parent, so a count check passes and the corruption
///     lands anyway.
///   * the re-rooting callers (Go box / bookmark / cross-tab handoff, and Game Engine
///     start) legitimately have NO parent — they Breadcrumbs.Clear() first. A guard that
///     demanded one would silently kill every one of those paths, which is the single
///     biggest risk in this fix.
/// </summary>
public class LiveWalkerNavRaceTests
{
    // ── the fake ────────────────────────────────────────────────────────────
    //
    // Gating is OPT-IN by address, following ClassStructViewModelConcurrencyTests: an
    // address nobody gated resolves immediately. That matters for the negative controls —
    // against UNFIXED code the stale branch resolves instantly and the test fails on a
    // clean assertion instead of hanging on a gate nobody releases.
    private sealed class GatedDumpService : StubDumpService
    {
        public readonly ConcurrentDictionary<string, TaskCompletionSource<InstanceWalkResult>> WalkGates = new();
        public readonly ConcurrentDictionary<string, TaskCompletionSource<FindReferencesResult>> RefGates = new();

        // RunContinuationsAsynchronously: without it SetResult resumes the awaiting
        // continuation INLINE on the releasing thread, which serialises the very
        // interleaving these tests exist to produce.
        public TaskCompletionSource<InstanceWalkResult> GateWalk(string addr)
            => WalkGates[addr] = new TaskCompletionSource<InstanceWalkResult>(
                   TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<FindReferencesResult> GateRefs(string addr)
            => RefGates[addr] = new TaskCompletionSource<FindReferencesResult>(
                   TaskCreationOptions.RunContinuationsAsynchronously);

        public override Task<InstanceWalkResult> WalkInstanceAsync(
            string addr, string? classAddr = null, int arrayLimit = 64, int previewLimit = 2,
            bool fillGaps = false, bool lean = false, CancellationToken ct = default)
            => WalkGates.TryGetValue(addr, out var gate)
                ? gate.Task
                : Task.FromResult(WalkOf(addr));

        public override Task<FindReferencesResult> FindReferencesToUObjectAsync(
            string addr, int maxResults = 32, CancellationToken ct = default)
            => RefGates.TryGetValue(addr, out var gate)
                ? gate.Task
                : Task.FromResult(new FindReferencesResult());

        public static InstanceWalkResult WalkOf(string addr) => new()
        {
            Address   = addr,
            Name      = "Obj" + addr,
            ClassName = "UObject",
            Fields    = new(),
        };
    }

    private static LiveWalkerViewModel MakeVm(GatedDumpService dump)
        => new(dump, new MockLoggingService(), new MockPlatformService(Path.GetTempPath()));

    private static BreadcrumbItem Crumb(string name, string addr)
        => new() { Address = addr, Label = name, FieldName = name };

    /// <summary>A navigable pointer field pointing at <paramref name="target"/>.</summary>
    private static LiveFieldValue PtrField(string name, string target) => new()
    {
        Name       = name,
        TypeName   = "ObjectProperty",
        PtrAddress = target,
        Offset     = 0x10,
    };

    // ── V4: the drill-down append ───────────────────────────────────────────

    [Fact]
    public async Task V4_UserGoesBackDuringTheWalk_TheStaleCrumbIsDiscarded()
    {
        var dump = new GatedDumpService();
        var vm   = MakeVm(dump);
        vm.Breadcrumbs.Add(Crumb("Root", "0x1000"));
        vm.Breadcrumbs.Add(Crumb("Leaf", "0x2000"));

        var gate = dump.GateWalk("0x3000");
        var drill = vm.NavigateToFieldCommand.ExecuteAsync(PtrField("Child", "0x3000"));

        // The user goes Back while the walk is still in flight.
        await vm.GoBackCommand.ExecuteAsync(null);
        var parentAfterBack = vm.Breadcrumbs[^1];

        gate.SetResult(GatedDumpService.WalkOf("0x3000"));
        await drill;

        Assert.Same(parentAfterBack, vm.Breadcrumbs[^1]);        // nothing grafted on
        Assert.DoesNotContain(vm.Breadcrumbs, b => b.FieldName == "Child");
        Assert.Contains("superseded", vm.StatusText);            // honest degrade, not a silent drop
    }

    [Fact]
    public async Task V4_NoInterleaving_TheCrumbIsStillAppended()
    {
        // NEGATIVE CONTROL. If this goes red the guard is rejecting ordinary navigation,
        // which is the failure mode that matters most — a too-strict guard breaks the
        // panel for everyone while looking like a safety improvement.
        var dump = new GatedDumpService();
        var vm   = MakeVm(dump);
        vm.Breadcrumbs.Add(Crumb("Root", "0x1000"));

        await vm.NavigateToFieldCommand.ExecuteAsync(PtrField("Child", "0x3000"));

        Assert.Contains(vm.Breadcrumbs, b => b.FieldName == "Child");
        Assert.DoesNotContain("superseded", vm.StatusText);
    }

    [Fact]
    public async Task V4_BackThenForward_RestoresTheCountButNotTheParent()
    {
        // THE REASON THE GUARD IS IDENTITY AND NOT Breadcrumbs.Count. Here the user goes
        // Back and then drills somewhere ELSE, so the count returns to what it was at
        // gesture time while the parent is a different object. A count-based guard passes
        // and the corruption lands; a reference check catches it.
        var dump = new GatedDumpService();
        var vm   = MakeVm(dump);
        vm.Breadcrumbs.Add(Crumb("Root", "0x1000"));
        vm.Breadcrumbs.Add(Crumb("Leaf", "0x2000"));
        int countAtGesture = vm.Breadcrumbs.Count;

        var gate = dump.GateWalk("0x3000");
        var drill = vm.NavigateToFieldCommand.ExecuteAsync(PtrField("Child", "0x3000"));

        await vm.GoBackCommand.ExecuteAsync(null);
        vm.Breadcrumbs.Add(Crumb("Other", "0x9000"));   // a different second crumb
        Assert.Equal(countAtGesture, vm.Breadcrumbs.Count);   // the count guard would pass here

        gate.SetResult(GatedDumpService.WalkOf("0x3000"));
        await drill;

        Assert.DoesNotContain(vm.Breadcrumbs, b => b.FieldName == "Child");
        Assert.Equal("Other", vm.Breadcrumbs[^1].Label);
    }

    [Fact]
    public async Task V4_ReRootingNavigation_HasNoParentAndMustStillWork()
    {
        // ⚠ THE RESIDUAL-RISK GUARD. The Go box, bookmark load and every cross-tab
        // "Open in Live Walker" handoff re-root through NavigateToAddressAsync, which
        // calls Breadcrumbs.Clear() first — so there is no parent to compare against.
        // A guard that treated "no parent" as "stale" would break all of them silently.
        var dump = new GatedDumpService();
        var vm   = MakeVm(dump);
        vm.Breadcrumbs.Add(Crumb("Root", "0x1000"));
        vm.Breadcrumbs.Add(Crumb("Leaf", "0x2000"));

        await vm.NavigateToAddressCommand.ExecuteAsync("0x4000");

        Assert.NotEmpty(vm.Breadcrumbs);
        Assert.DoesNotContain("superseded", vm.StatusText);
    }

    // ── V3: the reference scan ──────────────────────────────────────────────

    [Fact]
    public async Task V3_NavigatingAwayDuringTheScan_DiscardsTheResults()
    {
        var dump = new GatedDumpService();
        var vm   = MakeVm(dump);
        vm.Breadcrumbs.Add(Crumb("Items", "0x1000"));
        await vm.NavigateToAddressCommand.ExecuteAsync("0x1000");

        var gate = dump.GateRefs(vm.CurrentAddress);
        var scan = vm.FindReferencesCommand.ExecuteAsync(null);

        // The user drills elsewhere while the 30-second bulk-lane scan is in flight.
        await vm.NavigateToFieldCommand.ExecuteAsync(PtrField("Other", "0x7000"));

        gate.SetResult(new FindReferencesResult
        {
            References = { new ReferenceMatch { OwnerAddress = "0xAAA", OwnerName = "Holder" } },
        });
        await scan;

        // The rows belong to the object that was scanned, not to where the user is now.
        Assert.Empty(vm.References);
        Assert.DoesNotContain("References to", vm.ReferencesHeader ?? "");
        Assert.Contains("navigated away", vm.StatusText);
    }

    [Fact]
    public async Task V3_ContainerDrillDuringTheScan_IsAlsoCaught()
    {
        // ⚠ THE CASE THAT KILLS THE OBVIOUS FIX. `if (result.QueryAddress != CurrentAddress)`
        // is tempting — the DLL already echoes QueryAddress and DumpService already parses
        // it — but a CONTAINER drill pushes a crumb and changes CurrentObjectName while
        // leaving CurrentAddress untouched. An address-only guard passes here and the
        // "References to Items" mislabel lands anyway, which is the whole filed symptom.
        //
        // This test was added because reverting the guard to address-only left all the
        // other V3 tests GREEN — the control found the hole, not review.
        var dump = new GatedDumpService();
        var vm   = MakeVm(dump);
        await vm.NavigateToAddressCommand.ExecuteAsync("0x1000");
        var addrBefore = vm.CurrentAddress;

        var gate = dump.GateRefs(addrBefore);
        var scan = vm.FindReferencesCommand.ExecuteAsync(null);

        // Container drill: same object address, new crumb, new displayed name.
        vm.Breadcrumbs.Add(Crumb("Items [12 x FItem]", addrBefore));
        vm.CurrentObjectName = "Items [12 x FItem]";
        Assert.Equal(addrBefore, vm.CurrentAddress);   // an address-only guard would pass

        gate.SetResult(new FindReferencesResult
        {
            References = { new ReferenceMatch { OwnerAddress = "0xAAA", OwnerName = "Holder" } },
        });
        await scan;

        Assert.Empty(vm.References);
        Assert.Contains("navigated away", vm.StatusText);
    }

    [Fact]
    public async Task V3_StayingPut_PublishesTheResultsUnderTheScannedName()
    {
        // NEGATIVE CONTROL for the test above, and it also pins the header source: the
        // name must come from the SNAPSHOT taken before the await, not from live VM state.
        var dump = new GatedDumpService();
        var vm   = MakeVm(dump);
        await vm.NavigateToAddressCommand.ExecuteAsync("0x1000");
        var scannedName = vm.CurrentObjectName;

        await vm.FindReferencesCommand.ExecuteAsync(null);

        Assert.Contains("References to", vm.ReferencesHeader ?? "");
        Assert.Contains(scannedName, vm.ReferencesHeader ?? "");
        Assert.DoesNotContain("navigated away", vm.StatusText);
    }
}
