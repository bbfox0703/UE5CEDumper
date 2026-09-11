using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UE5DumpUI.Models;
using UE5DumpUI.Services;
using UE5DumpUI.ViewModels;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// [A4-PARENT-CRUMB-VTABLE] The Parent (Outer) crumb claimed <c>[child + 0]</c> with a dereference.
///
/// <para>An Outer is reached by a BACK-reference: no forward offset leads from a child to it. The
/// Parent crumb was pushed as <c>FieldOffset = 0, IsPointerDeref = true</c>, so a CE export through
/// a Parent hop emitted <c>[E + 0]</c> — E's vtable — and applied every parent record to it.
/// PathStepToBreadcrumbs already stamped the offset-less-hop marker (<c>-1</c>) on its LevelActor /
/// WorldLevel steps, and e88190ba gave it to the GWorld actor list; <c>GoToParentAsync</c> was the
/// third producer of such a hop, and was missed.</para>
///
/// <para>⛔ The finding's own fix — stamp <c>-1</c> unconditionally — breaks the commonest Parent use:
/// Actor › RootComponent › Parent. Both XML commands re-anchor at the last <c>-1</c> hop BEFORE the
/// cycle collapse, so a restart-stable GWorld chain would become a session-only address. The recorded
/// safe shape (a): when the Outer is already on the spine, Parent IS a breadcrumb jump to it; only
/// otherwise push the <c>-1</c> hop. Both scenarios are pinned here, as the record asks.</para>
/// </summary>
public class LiveWalkerParentCrumbTests
{
    private static LiveWalkerViewModel MakeVm(StubDumpService dump)
        => new(dump, new MockLoggingService(), new MockPlatformService(Path.GetTempPath()));

    private static LiveFieldValue Ptr(string name, int off, string target) => new()
    {
        Name = name, TypeName = "ObjectProperty", Offset = off, Size = 8,
        PtrAddress = target, PtrName = "Obj" + target, PtrClassName = "BP_X_C",
    };

    private static LiveFieldValue Int(string name, int off) => new()
    {
        Name = name, TypeName = "IntProperty", Offset = off, Size = 4, TypedValue = "1",
    };

    [Fact]
    public async Task Parent_OfAnObjectWhoseOuterIsNotOnTheSpine_IsAnOffsetLessHop()
    {
        const string E = "0x1000", P = "0x5000";
        var dump = new StubDumpService();
        dump.RegisterStruct(E, new InstanceWalkResult
        {
            Address = E, Name = "Enemy", ClassName = "BP_Enemy_C",
            OuterAddr = P, OuterName = "PersistentLevel", OuterClassName = "Level",
            Fields = new List<LiveFieldValue> { Int("Health", 0x10) },
        });
        dump.RegisterStruct(P, new InstanceWalkResult
        {
            Address = P, Name = "PersistentLevel", ClassName = "Level",
            Fields = new List<LiveFieldValue> { Int("Count", 0x20) },
        });
        var vm = MakeVm(dump);
        await vm.NavigateToAddressCommand.ExecuteAsync(E);   // an Instance-Finder handoff re-roots here

        await vm.GoToParentCommand.ExecuteAsync(null);

        Assert.Equal(P, vm.CurrentAddress);                  // Parent still navigates
        var outer = vm.Breadcrumbs[^1];
        Assert.Equal(P, outer.Address);
        // Never a claim of [E + 0], which is E's vtable: -1 is the offset-less-hop marker.
        Assert.Equal(-1, outer.FieldOffset);
        // So the CE export re-roots at the parent's own address instead of dereferencing E + 0.
        var anchored = CeXmlExportService.AnchorAtLastUnchainableHop(vm.Breadcrumbs.ToList());
        Assert.Single(anchored);
        Assert.Equal(P, anchored[0].Address);
    }

    [Fact]
    public async Task Parent_WhenTheOuterIsAlreadyOnTheSpine_JumpsBackToIt_KeepingARestartStableChain()
    {
        // GWorld › PersistentLevel › Hero › RootComponent, then Parent. The component's Outer IS the
        // Hero crumb: the commonest Parent gesture, and the one the unconditional -1 broke.
        var dump = new StubDumpService();   // default world: 0xA8B0, PersistentLevel 0x4500 at +0x30
        dump.RegisterStruct("0x4500", new InstanceWalkResult
        {
            Address = "0x4500", Name = "PersistentLevel", ClassName = "Level",
            Fields = new List<LiveFieldValue> { Ptr("Hero", 0x98, "0x6000") },
        });
        dump.RegisterStruct("0x6000", new InstanceWalkResult
        {
            Address = "0x6000", Name = "Hero", ClassName = "BP_Hero_C",
            OuterAddr = "0x4500", OuterName = "PersistentLevel", OuterClassName = "Level",
            Fields = new List<LiveFieldValue> { Ptr("RootComponent", 0x130, "0x7000") },
        });
        dump.RegisterStruct("0x7000", new InstanceWalkResult
        {
            Address = "0x7000", Name = "Root", ClassName = "SceneComponent",
            OuterAddr = "0x6000", OuterName = "Hero", OuterClassName = "BP_Hero_C",
            Fields = new List<LiveFieldValue> { Int("Mobility", 0x40) },
        });
        var vm = MakeVm(dump);
        await vm.StartFromWorldCommand.ExecuteAsync(null);
        await vm.NavigateToFieldCommand.ExecuteAsync(vm.Fields.Single(f => f.Name == "PersistentLevel"));
        await vm.NavigateToFieldCommand.ExecuteAsync(vm.Fields.Single(f => f.Name == "Hero"));
        await vm.NavigateToFieldCommand.ExecuteAsync(vm.Fields.Single(f => f.Name == "RootComponent"));
        Assert.Equal(new[] { "GWorld", "PersistentLevel", "Hero", "RootComponent" },
                     vm.Breadcrumbs.Select(b => b.FieldName));

        await vm.GoToParentCommand.ExecuteAsync(null);

        // Exactly what clicking the Hero crumb does: truncate to it — no Outer crumb.
        Assert.Equal(new[] { "GWorld", "PersistentLevel", "Hero" }, vm.Breadcrumbs.Select(b => b.FieldName));
        Assert.Equal("0x6000", vm.CurrentAddress);
        Assert.True(vm.CanGoForward);                        // RootComponent is on the forward history, as a jump leaves it
        // Every hop below the root is a real forward offset, so the chain stays GWorld-rooted and
        // restart-stable: the export does not re-anchor on a session-only address.
        Assert.All(vm.Breadcrumbs.Skip(1), b => Assert.True(b.FieldOffset >= 0));
        var spine = vm.Breadcrumbs.ToList();
        Assert.Same(spine, CeXmlExportService.AnchorAtLastUnchainableHop(spine));

        // And Forward returns to RootComponent, exactly as after a breadcrumb jump.
        await vm.GoForwardCommand.ExecuteAsync(null);
        Assert.Equal(new[] { "GWorld", "PersistentLevel", "Hero", "RootComponent" },
                     vm.Breadcrumbs.Select(b => b.FieldName));
        Assert.Equal("0x7000", vm.CurrentAddress);
        Assert.False(vm.CanGoForward);
    }

    // ── From the batch's adversarial review ──────────────────────────────────────

    /// <summary>The Outer is the UWorld, and the only crumb at its address is the synthetic GWorld
    /// ROOT, whose view is the cached actor list, not the UWorld object. Jumping there swapped
    /// "walk the Outer as an instance" for the actor list (IsGWorldActorListRoot's doc calls that
    /// swap a defect), and lost the UWorld's own fields and its further Parent. So Parent from
    /// PersistentLevel pushes the offset-less hop and walks the UWorld.</summary>
    [Fact]
    public async Task Parent_WhoseOuterIsTheGWorldRoot_WalksTheUWorld_NotTheActorList()
    {
        var dump = new StubDumpService();   // default world 0xA8B0, PersistentLevel 0x4500 at +0x30
        dump.RegisterStruct("0x4500", new InstanceWalkResult
        {
            Address = "0x4500", Name = "PersistentLevel", ClassName = "Level",
            OuterAddr = "0xA8B0", OuterName = "TestWorld", OuterClassName = "World",
            Fields = new List<LiveFieldValue> { Ptr("Hero", 0x98, "0x6000") },
        });
        dump.RegisterStruct("0xA8B0", new InstanceWalkResult
        {
            Address = "0xA8B0", Name = "TestWorld", ClassName = "World",
            Fields = new List<LiveFieldValue> { Ptr("GameState", 0x120, "0xB000") },
        });
        var vm = MakeVm(dump);
        await vm.StartFromWorldCommand.ExecuteAsync(null);
        await vm.NavigateToFieldCommand.ExecuteAsync(vm.Fields.Single(f => f.Name == "PersistentLevel"));

        await vm.GoToParentCommand.ExecuteAsync(null);

        var outer = vm.Breadcrumbs[^1];
        Assert.Equal("Outer", outer.FieldName);
        Assert.Equal(-1, outer.FieldOffset);
        Assert.Equal("0xA8B0", outer.Address);
        Assert.Equal("World", vm.CurrentClassName);          // the instance walk, not PopulateFromWorld's "UWorld"
        Assert.Contains(vm.Fields, f => f.Name == "GameState");
    }

    /// <summary>A Parent DETOUR: from Hero's ASC, Parent goes to its Outer, the PlayerState, which is
    /// off the spine (a -1 hop), and PlayerState.PawnPrivate drills straight back to Hero. The spine
    /// now holds a cycle Hero…Hero. The XML exports anchored at the last -1 hop BEFORE collapsing
    /// cycles, so the collapse never saw it and a restart-stable GWorld chain became a session-only
    /// address — the harm the record called PARTLY HARMFUL, reached by a detour. Now: clean, then
    /// anchor (the record's option (b)).</summary>
    [Fact]
    public async Task ParentDetour_ThatReturnsToTheSpine_ExportStaysGWorldRooted()
    {
        var dump = new StubDumpService();
        dump.RegisterStruct("0x4500", new InstanceWalkResult
        {
            Address = "0x4500", Name = "PersistentLevel", ClassName = "Level",
            Fields = new List<LiveFieldValue> { Ptr("Hero", 0x98, "0x6000") },
        });
        dump.RegisterStruct("0x6000", new InstanceWalkResult
        {
            Address = "0x6000", Name = "Hero", ClassName = "BP_Hero_C",
            Fields = new List<LiveFieldValue> { Ptr("AbilitySystemComponent", 0x6A0, "0x7100"), Int("Level", 0x40) },
        });
        dump.RegisterStruct("0x7100", new InstanceWalkResult
        {
            Address = "0x7100", Name = "ASC", ClassName = "AbilitySystemComponent",
            OuterAddr = "0x9900", OuterName = "PlayerState", OuterClassName = "PlayerState",
            Fields = new List<LiveFieldValue> { Int("Stack", 0x10) },
        });
        dump.RegisterStruct("0x9900", new InstanceWalkResult
        {
            Address = "0x9900", Name = "PlayerState", ClassName = "PlayerState",
            Fields = new List<LiveFieldValue> { Ptr("PawnPrivate", 0x308, "0x6000") },
        });
        var platform = new MockPlatformService(Path.GetTempPath());
        var log = new MockLoggingService();
        var vm = new LiveWalkerViewModel(dump, log, platform);
        await vm.StartFromWorldCommand.ExecuteAsync(null);
        await vm.NavigateToFieldCommand.ExecuteAsync(vm.Fields.Single(f => f.Name == "PersistentLevel"));
        await vm.NavigateToFieldCommand.ExecuteAsync(vm.Fields.Single(f => f.Name == "Hero"));
        await vm.NavigateToFieldCommand.ExecuteAsync(vm.Fields.Single(f => f.Name == "AbilitySystemComponent"));
        await vm.GoToParentCommand.ExecuteAsync(null);                    // PlayerState: off the spine, a -1 hop
        Assert.Equal(-1, vm.Breadcrumbs[^1].FieldOffset);
        await vm.NavigateToFieldCommand.ExecuteAsync(vm.Fields.Single(f => f.Name == "PawnPrivate"));
        Assert.Equal("0x6000", vm.CurrentAddress);                        // back on Hero: a cycle

        await vm.ExportCeXmlCommand.ExecuteAsync(null);

        Assert.True(platform.LastClipboard != null, $"no XML produced - status='{vm.StatusText}' error='{vm.ErrorMessage}'");
        // The cycle collapses to GWorld > PersistentLevel > Hero, a restart-stable chain: nothing
        // needs re-anchoring, and the user is not told "no pointer chain from GWorld exists".
        Assert.DoesNotContain(log.Messages, m => m.Contains("re-anchored", System.StringComparison.Ordinal));
    }

    /// <summary>Pins the container-view skip in LastObjectCrumbAt. A container crumb carries its
    /// OWNER's address; without the skip, Parent from an array element whose Outer is that owner
    /// landed on the element list instead of the owner object.</summary>
    [Fact]
    public async Task Parent_FromAnArrayElement_LandsOnTheOwnerObject_NotTheContainerView()
    {
        var dump = new StubDumpService();
        dump.RegisterStruct("0x4500", new InstanceWalkResult
        {
            Address = "0x4500", Name = "PersistentLevel", ClassName = "Level",
            Fields = new List<LiveFieldValue> { Ptr("Hero", 0x98, "0x6000") },
        });
        dump.RegisterStruct("0x6000", new InstanceWalkResult
        {
            Address = "0x6000", Name = "Hero", ClassName = "BP_Hero_C",
            Fields = new List<LiveFieldValue>
            {
                new()
                {
                    Name = "Components", TypeName = "ArrayProperty", Offset = 0x140, Size = 16,
                    ArrayCount = 1, ArrayInnerType = "ObjectProperty", ArrayElemSize = 8, ArrayDataAddr = "0x9500",
                    ArrayElements = new() { new() { Index = 0, PtrAddress = "0x7200", PtrName = "Comp", PtrClassName = "SceneComponent" } },
                },
            },
        });
        dump.RegisterStruct("0x7200", new InstanceWalkResult
        {
            Address = "0x7200", Name = "Comp", ClassName = "SceneComponent",
            OuterAddr = "0x6000", OuterName = "Hero", OuterClassName = "BP_Hero_C",
            Fields = new List<LiveFieldValue> { Int("Mobility", 0x40) },
        });
        var vm = MakeVm(dump);
        await vm.StartFromWorldCommand.ExecuteAsync(null);
        await vm.NavigateToFieldCommand.ExecuteAsync(vm.Fields.Single(f => f.Name == "PersistentLevel"));
        await vm.NavigateToFieldCommand.ExecuteAsync(vm.Fields.Single(f => f.Name == "Hero"));
        await vm.NavigateToContainerCommand.ExecuteAsync(vm.Fields.Single(f => f.Name == "Components"));
        await vm.NavigateToFieldCommand.ExecuteAsync(vm.Fields.Single(f => f.Name == "[0]"));

        await vm.GoToParentCommand.ExecuteAsync(null);

        Assert.Equal(new[] { "GWorld", "PersistentLevel", "Hero" }, vm.Breadcrumbs.Select(b => b.FieldName));
        Assert.False(vm.Breadcrumbs[^1].IsContainerView);
        Assert.Equal("0x6000", vm.CurrentAddress);
    }

    /// <summary>Pins LAST-match in LastObjectCrumbAt: when the Outer appears twice on the spine
    /// (Hero › Controller › Pawn = Hero again), Parent jumps to the NEAREST occurrence, so the strip
    /// keeps Controller › Pawn and Forward returns to where Parent was pressed.</summary>
    [Fact]
    public async Task Parent_WhenTheOuterIsOnTheSpineTwice_JumpsToTheNearestOne()
    {
        var dump = new StubDumpService();
        dump.RegisterStruct("0x4500", new InstanceWalkResult
        {
            Address = "0x4500", Name = "PersistentLevel", ClassName = "Level",
            Fields = new List<LiveFieldValue> { Ptr("Hero", 0x98, "0x6000") },
        });
        dump.RegisterStruct("0x6000", new InstanceWalkResult
        {
            Address = "0x6000", Name = "Hero", ClassName = "BP_Hero_C",
            Fields = new List<LiveFieldValue> { Ptr("RootComponent", 0x130, "0x7000"), Ptr("Controller", 0x2A0, "0x8000") },
        });
        dump.RegisterStruct("0x8000", new InstanceWalkResult
        {
            Address = "0x8000", Name = "PC", ClassName = "PlayerController",
            Fields = new List<LiveFieldValue> { Ptr("Pawn", 0x2B0, "0x6000") },
        });
        dump.RegisterStruct("0x7000", new InstanceWalkResult
        {
            Address = "0x7000", Name = "Root", ClassName = "SceneComponent",
            OuterAddr = "0x6000", OuterName = "Hero", OuterClassName = "BP_Hero_C",
            Fields = new List<LiveFieldValue> { Int("Mobility", 0x40) },
        });
        var vm = MakeVm(dump);
        await vm.StartFromWorldCommand.ExecuteAsync(null);
        foreach (var hop in new[] { "PersistentLevel", "Hero", "Controller", "Pawn", "RootComponent" })
            await vm.NavigateToFieldCommand.ExecuteAsync(vm.Fields.Single(f => f.Name == hop));

        await vm.GoToParentCommand.ExecuteAsync(null);

        Assert.Equal(new[] { "GWorld", "PersistentLevel", "Hero", "Controller", "Pawn" },
                     vm.Breadcrumbs.Select(b => b.FieldName));
        Assert.Equal("0x6000", vm.CurrentAddress);
        await vm.GoForwardCommand.ExecuteAsync(null);
        Assert.Equal("RootComponent", vm.Breadcrumbs[^1].FieldName);
        Assert.False(vm.CanGoForward);
    }
}
