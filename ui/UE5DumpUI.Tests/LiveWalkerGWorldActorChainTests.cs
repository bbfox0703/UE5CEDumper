using UE5DumpUI.Models;
using UE5DumpUI.Services;
using UE5DumpUI.ViewModels;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// The GWorld actor list is NOT a list of fields, and a CE pointer chain must not pretend
/// it is.
///
/// <para>Reported on P3R (build 3358): "Start from GWorld" listed KernelActor / TaskActor /
/// RecastNavMesh-Enemy, the user drilled one and hit Copy CE XML, and CE resolved
/// <c>base P-&gt;603BB0A0</c> then <c>KernelActor (0) P-&gt;144AF6408</c> — an address inside
/// the executable image. <c>144AF6408</c> is the value at <c>UWorld + 0</c>, i.e. the world's
/// VTABLE POINTER, because the actor row was published with <c>Offset = 0</c> and exported as
/// if that were a real field offset.</para>
///
/// <para>The rows come from <c>Aura::FindActorsInLevel</c>, which reconstructs the list from
/// each actor's <c>Outer</c> — <c>ULevel::Actors</c> carries no UPROPERTY (audit #5 F8/F9), so
/// there is no offset AND no element index from the world to an actor. The Locate-in-GWorld
/// path already knew this (<c>PathStepToBreadcrumbs</c> stamps <c>FieldOffset = -1</c> for a
/// "LevelActor" step); the actor list was the copy that never got the marker.</para>
/// </summary>
public class LiveWalkerGWorldActorChainTests
{
    private const string WorldAddr = "0x603BB0A0";
    private const string ActorAddr = "0x64AF68C0";

    private sealed class WorldStub : StubDumpService
    {
        public override Task<WorldWalkResult> WalkWorldAsync(int actorLimit = 200, int arrayLimit = 64,
            CancellationToken ct = default)
            => Task.FromResult(new WorldWalkResult
            {
                WorldAddr = WorldAddr, WorldName = "LV_Xrd777_P",
                LevelAddr = "0x4DCEFA80", LevelName = "PersistentLevel", LevelOffset = 0x30,
                ActorCount = 1, ActorTotal = 1,
                Actors = new List<ActorInfo>
                {
                    new()
                    {
                        Address = ActorAddr, Name = "KernelActor", ClassName = "KernelActor", Index = 1,
                        Components = new List<ComponentInfo>
                        {
                            new() { Address = "0x64AF7000", Name = "SceneComp", ClassName = "SceneComponent" },
                        },
                    },
                },
            });
    }

    private static (LiveWalkerViewModel vm, MockPlatformService platform) MakeVm()
    {
        var dump = new WorldStub();
        // The object the user drills into: one plain scalar field at a real offset, so the
        // export has a leaf to emit under whatever root it ends up choosing.
        dump.RegisterStruct(ActorAddr, new InstanceWalkResult
        {
            Address = ActorAddr,
            Name = "KernelActor_2147481774",
            ClassName = "KernelActor",
            Fields = new List<LiveFieldValue>
            {
                new() { Name = "TickInterval", TypeName = "FloatProperty", Offset = 0x28, Size = 4 },
            },
        });
        var platform = new MockPlatformService(Path.GetTempPath());
        var vm = new LiveWalkerViewModel(dump, new MockLoggingService(), platform);
        return (vm, platform);
    }

    /// <summary>
    /// An actor row published by walk_world must declare that it has no parent offset.
    /// Offset itself stays 0 (bookmarks and same-layout row reuse key on it); the flag is
    /// what stops every downstream consumer from reading that 0 as "+0".
    /// </summary>
    [Fact]
    public async Task WorldActorRows_DeclareNoParentOffset()
    {
        var (vm, _) = MakeVm();
        await vm.StartFromWorldCommand.ExecuteAsync(null);

        var pLevel = Assert.Single(vm.Fields, f => f.Name == "PersistentLevel");
        var actor = Assert.Single(vm.Fields, f => f.Name == "KernelActor");
        var comp = Assert.Single(vm.Fields, f => f.Name.Trim() == "KernelActor.SceneComp");

        // PersistentLevel IS a real reflected field of UWorld and must stay chainable — if
        // this ever flips, the fix has over-reached and killed the one honest hop here.
        Assert.False(pLevel.HasNoParentOffset);
        Assert.Equal(0x30, pLevel.Offset);
        Assert.Equal("0x30", pLevel.OffsetDisplay);

        Assert.True(actor.HasNoParentOffset);
        Assert.True(comp.HasNoParentOffset);
        // Displayed as an em dash, not "0x0" — "0x0" is what made it look like a field.
        Assert.Equal("—", actor.OffsetDisplay);
        Assert.Equal("—", comp.OffsetDisplay);
    }

    /// <summary>
    /// Navigating into an actor row stamps the -1 sentinel already used by
    /// <c>PathStepToBreadcrumbs</c> for the same hop, so every consumer that tests
    /// <c>FieldOffset &gt;= 0</c> (the AA-script gate) sees it.
    /// </summary>
    [Fact]
    public async Task NavigatingIntoAWorldActor_StampsTheNoOffsetSentinel()
    {
        var (vm, _) = MakeVm();
        await vm.StartFromWorldCommand.ExecuteAsync(null);
        var actor = Assert.Single(vm.Fields, f => f.Name == "KernelActor");

        await vm.NavigateToFieldCommand.ExecuteAsync(actor);

        Assert.Equal(2, vm.Breadcrumbs.Count);
        Assert.Equal(0, vm.Breadcrumbs[0].FieldOffset);       // the GWorld anchor itself
        Assert.True(vm.Breadcrumbs[1].FieldOffset < 0);
    }

    /// <summary>
    /// The end-to-end claim: the copied XML must not contain a <c>[GWorld] + 0</c> hop, and
    /// must be rooted on the actor's own address.
    /// </summary>
    [Fact]
    public async Task CopyCeXml_FromAWorldActor_RootsOnTheActorNotOnGWorldPlusZero()
    {
        var (vm, platform) = MakeVm();
        await vm.StartFromWorldCommand.ExecuteAsync(null);
        await vm.NavigateToFieldCommand.ExecuteAsync(
            Assert.Single(vm.Fields, f => f.Name == "KernelActor"));

        await vm.ExportCeXmlCommand.ExecuteAsync(null);

        var xml = platform.LastClipboard;
        // Name the holder rather than asserting a bare non-null: an export that bailed and
        // an export that threw look identical through Assert.NotNull.
        Assert.True(xml != null,
            $"export copied nothing - status='{vm.StatusText}' error='{vm.ErrorMessage}'");

        // Rooted on the actor. The world address must not appear at all — if it does, the
        // chain still starts at GWorld and still has to invent a hop to reach the actor.
        Assert.Contains("64AF68C0", xml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("603BB0A0", xml, StringComparison.OrdinalIgnoreCase);

        // The leaf offset survives the re-anchor: it is relative to the actor, which is now
        // the root, so it is unchanged.
        Assert.Contains("+28", xml);

        // And the user is told the chain is session-only rather than silently handed one.
        Assert.Contains("re-rooted", vm.StatusText, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The second victim of the same root cause. "Copy CE AA Script" emits a RESTART-STABLE
    /// script that walks GWorld -> ... -> the object at enable time, gated on every hop
    /// below the root having a real offset (<c>FieldOffset &gt;= 0</c>). The gate was
    /// already correct — the actor crumb simply lied to it with a 0, so the script walked
    /// <c>[GWorld] + 0</c> into the world's vtable and registered a symbol there.
    /// </summary>
    [Fact]
    public async Task CeAaScript_FromAWorldActor_RefusesTheGWorldWalk()
    {
        var (vm, platform) = MakeVm();
        // A resolvable GWorld base, so the "walk from GWorld" branch is genuinely available
        // — without it the fallback would be taken for an unrelated reason and the test
        // would pass on the broken code too.
        vm.SetEngineState(new EngineState { GWorldAddr = "0x4C8003" });

        await vm.StartFromWorldCommand.ExecuteAsync(null);
        await vm.NavigateToFieldCommand.ExecuteAsync(
            Assert.Single(vm.Fields, f => f.Name == "KernelActor"));

        await vm.GenerateCeAAScriptCommand.ExecuteAsync(null);

        Assert.Contains("not forward-walkable", vm.StatusText);
        var xml = platform.LastClipboard;
        Assert.True(xml != null, $"no script produced - status='{vm.StatusText}' error='{vm.ErrorMessage}'");
        Assert.DoesNotContain("4C8003", xml, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("64AF68C0", xml, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A spine with no offset-less hop is untouched, and the note stays empty — the
    /// re-anchor must not fire on ordinary navigation.
    /// </summary>
    [Fact]
    public void OrdinarySpine_IsNotReanchoredAndSaysNothing()
    {
        var spine = new List<BreadcrumbItem>
        {
            new() { Address = "0x1000", Label = "GWorld", FieldName = "GWorld", FieldOffset = 0 },
            new() { Address = "0x2000", Label = "GameState", FieldName = "GameState", FieldOffset = 0x120,
                    IsPointerDeref = true },
            new() { Address = "0x3000", Label = "Pawn", FieldName = "Pawn", FieldOffset = 0x40,
                    IsPointerDeref = true },
        };

        var after = CeXmlExportService.AnchorAtLastUnchainableHop(spine);

        Assert.Same(spine, after);
        Assert.Equal("", LiveWalkerViewModel.ReanchorNote(spine, after));
    }

    /// <summary>
    /// Re-anchoring keeps the DEEPEST offset-less hop, not the first: a spine that leaves the
    /// actor list and re-enters it (GWorld → actor → its world → another actor) must root on
    /// the second actor, or the chain re-acquires an unwalkable hop below its own root.
    /// </summary>
    [Fact]
    public void Reanchor_KeepsTheDeepestOffsetLessHop()
    {
        var spine = new List<BreadcrumbItem>
        {
            new() { Address = "0x1000", Label = "GWorld", FieldName = "GWorld",      FieldOffset = 0 },
            new() { Address = "0x2000", Label = "ActorA", FieldName = "ActorA",      FieldOffset = -1 },
            new() { Address = "0x1000", Label = "World",  FieldName = "OwningWorld", FieldOffset = 0x18,
                    IsPointerDeref = true },
            new() { Address = "0x4000", Label = "ActorB", FieldName = "ActorB",      FieldOffset = -1 },
            new() { Address = "0x5000", Label = "Mesh",   FieldName = "Mesh",        FieldOffset = 0x88,
                    IsPointerDeref = true },
        };

        var after = CeXmlExportService.AnchorAtLastUnchainableHop(spine);

        Assert.Equal(2, after.Count);
        Assert.Equal("ActorB", after[0].Label);
        Assert.Equal("Mesh", after[1].Label);
        Assert.True(after[1].FieldOffset >= 0);

        // Idempotent: the surviving root's own -1 is never re-examined.
        Assert.Same(after, CeXmlExportService.AnchorAtLastUnchainableHop(after));
    }

    /// <summary>
    /// The generator refuses an un-anchored spine instead of formatting -1 as "+FFFFFFFF".
    /// This is the invariant behind the re-anchor: a future export path that forgets to call
    /// it fails loudly rather than handing the user a table that walks into the image.
    /// </summary>
    [Fact]
    public void Generator_RefusesAnUnanchoredSpine()
    {
        var spine = new List<BreadcrumbItem>
        {
            new() { Address = "0x1000", Label = "GWorld", FieldName = "GWorld", FieldOffset = 0 },
            new() { Address = "0x2000", Label = "KernelActor", FieldName = "KernelActor", FieldOffset = -1 },
        };

        var ex = Assert.Throws<InvalidOperationException>(() =>
            CeXmlExportService.GenerateHierarchicalXml(
                "0x1000", "GWorld", spine,
                new List<LiveFieldValue> { new() { Name = "X", TypeName = "IntProperty", Offset = 4, Size = 4 } }));

        Assert.Contains("KernelActor", ex.Message);
        Assert.DoesNotContain("FFFFFFFF", ex.Message);
    }
}
