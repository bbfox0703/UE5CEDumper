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
/// a Parent hop emitted <c>[E + 0]</c> — E's vtable — and applied every parent record to it. e88190ba
/// gave the offset-less-hop marker (<c>-1</c>) to the two other producers of such a hop (the GWorld
/// actor list and PathStepToBreadcrumbs); <c>GoToParentAsync</c> was the third, and was missed.</para>
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
    }
}
