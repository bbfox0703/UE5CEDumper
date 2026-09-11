using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UE5DumpUI.Models;
using UE5DumpUI.ViewModels;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// [A4-NAV-BACKFIRST-GRAFT] A row clicked while Back / a breadcrumb jump / Forward / Parent is
/// still walking grafts the OLD level's field onto the NEW parent.
///
/// <para>Those gestures change the spine FIRST (Back pops, a jump truncates, Forward and Parent
/// push) and then await the walk. The grid keeps showing the level just left until the walk lands.
/// A drill gesture on one of those rows captured <c>parentAtGesture = CurrentCrumb</c> — already
/// the NEW crumb — so V4's "captured at gesture time" check passed and the old level's
/// <c>field.Offset</c> was appended under the new parent. Copy CE XML, Copy CE Field, the AA script
/// and a saved bookmark then persist that wrong chain. This is the re-derivation's test #2, which
/// was never written.</para>
///
/// <para>The recorded fix: stamp WHICH crumb the rendered rows belong to, at every site that
/// repopulates the grid, and refuse a drill of a rendered row whose stamp is not the current
/// crumb. Each case below holds the navigation's walk on a gate, clicks a row of the level being
/// left, and asserts nothing was grafted.</para>
/// </summary>
public class LiveWalkerNavStampTests
{
    private const string A = "0x1000", B = "0x2000", C = "0x3000", D = "0x4000", P = "0x5000";

    /// <summary>Answers registered walks; a gated address waits until the test releases it.</summary>
    private sealed class GatedStub : StubDumpService
    {
        private readonly Dictionary<string, TaskCompletionSource<InstanceWalkResult>> _gates = new();

        // RunContinuationsAsynchronously: otherwise SetResult resumes the navigation INLINE and the
        // interleaving this exists to produce never happens.
        public TaskCompletionSource<InstanceWalkResult> Gate(string addr)
            => _gates[addr] = new TaskCompletionSource<InstanceWalkResult>(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Ungate(string addr) => _gates.Remove(addr);

        public override Task<InstanceWalkResult> WalkInstanceAsync(string addr, string? classAddr = null,
            int arrayLimit = 64, int previewLimit = 2, bool fillGaps = false, bool lean = false,
            CancellationToken ct = default)
            => _gates.TryGetValue(addr, out var g)
                ? g.Task
                : base.WalkInstanceAsync(addr, classAddr, arrayLimit, previewLimit, fillGaps, lean, ct);
    }

    private static LiveFieldValue Ptr(string name, int off, string target) => new()
    {
        Name = name, TypeName = "ObjectProperty", Offset = off, Size = 8,
        PtrAddress = target, PtrName = "Obj" + target, PtrClassName = "BP_X_C",
    };

    private static InstanceWalkResult Obj(string addr, string cls, params LiveFieldValue[] fields) => new()
    {
        Address = addr, Name = "Obj" + addr, ClassName = cls, ClassAddr = "0x9" + addr[2..],
        Fields = fields.ToList(),
    };

    /// <summary>A → ToB → B. B has a pointer (ToC), an inline int array (Items) and an Outer (P).
    /// A also has ToD, for the Forward case (its own pointer target must not be the gated one).</summary>
    private static (GatedStub dump, LiveWalkerViewModel vm) OnB() => OnBWith(new MockPlatformService(Path.GetTempPath()));

    private static async Task<(GatedStub dump, LiveWalkerViewModel vm)> WalkedToBWith(MockPlatformService platform)
    {
        var (dump, vm) = OnBWith(platform);
        await vm.NavigateToAddressCommand.ExecuteAsync(A);
        await vm.NavigateToFieldCommand.ExecuteAsync(vm.Fields.Single(f => f.Name == "ToB"));
        return (dump, vm);
    }

    private static (GatedStub dump, LiveWalkerViewModel vm) OnBWith(MockPlatformService platform)
    {
        var dump = new GatedStub();
        dump.RegisterStruct(A, Obj(A, "BP_A_C", Ptr("ToB", 0x10, B), Ptr("ToD", 0x18, D)));
        var b = Obj(B, "BP_B_C",
            Ptr("ToC", 0x20, C),
            new LiveFieldValue
            {
                Name = "Items", TypeName = "ArrayProperty", Offset = 0x30, Size = 16,
                ArrayCount = 2, ArrayInnerType = "IntProperty", ArrayElemSize = 4, ArrayDataAddr = "0x9000",
                ArrayElements = new() { new() { Index = 0, Value = "1" }, new() { Index = 1, Value = "2" } },
            },
            new LiveFieldValue
            {
                // A struct array: its element rows are drillable ({}), so a drill INSIDE the container
                // view proves the container views stamp the grid too.
                Name = "Parts", TypeName = "ArrayProperty", Offset = 0x40, Size = 16,
                ArrayCount = 2, ArrayInnerType = "StructProperty", ArrayElemSize = 8,
                ArrayStructType = "FPart", ArrayStructClassAddr = "0x9500", ArrayDataAddr = "0x9100",
                ArrayElements = new() { new() { Index = 0, Value = "{FPart}" }, new() { Index = 1, Value = "{FPart}" } },
            },
            new LiveFieldValue
            {
                // Struct values and struct elements: drillable rows inside the Map and Set views.
                Name = "Loot", TypeName = "MapProperty", Offset = 0x50, Size = 80,
                MapCount = 1, MapKeyType = "IntProperty", MapValueType = "StructProperty",
                MapKeySize = 4, MapValueSize = 16, MapValueStructAddr = "0x9800", MapValueStructType = "FLoot",
                MapDataAddr = "0x9200", MapStride = 32, MapValueOffset = 8,
                MapElements = new() { new() { Index = 0, Key = "7", Value = "{FLoot}" } },
            },
            new LiveFieldValue
            {
                Name = "Tags", TypeName = "SetProperty", Offset = 0x60, Size = 80,
                SetCount = 1, SetElemType = "StructProperty", SetElemSize = 8, SetStride = 16,
                SetElemStructAddr = "0x9900", SetElemStructType = "FTag", SetDataAddr = "0x9300",
                SetElements = new() { new() { Index = 0, Key = "{FTag}" } },
            });
        dump.RegisterStruct(B, new InstanceWalkResult
        {
            Address = b.Address, Name = b.Name, ClassName = b.ClassName, ClassAddr = b.ClassAddr,
            OuterAddr = P, OuterName = "Owner", OuterClassName = "BP_P_C", Fields = b.Fields,
        });
        dump.RegisterStruct(C, Obj(C, "BP_C_C", new LiveFieldValue { Name = "X", TypeName = "IntProperty", Offset = 8, Size = 4 }));
        dump.RegisterStruct(D, Obj(D, "BP_D_C", new LiveFieldValue { Name = "Y", TypeName = "IntProperty", Offset = 8, Size = 4 }));
        dump.RegisterStruct(P, Obj(P, "BP_P_C", new LiveFieldValue { Name = "Z", TypeName = "IntProperty", Offset = 8, Size = 4 }));
        var vm = new LiveWalkerViewModel(dump, new MockLoggingService(), platform);
        return (dump, vm);
    }

    private static async Task<(GatedStub dump, LiveWalkerViewModel vm)> WalkedToB()
    {
        var (dump, vm) = OnB();
        await vm.NavigateToAddressCommand.ExecuteAsync(A);
        await vm.NavigateToFieldCommand.ExecuteAsync(vm.Fields.Single(f => f.Name == "ToB"));
        Assert.Equal(B, vm.CurrentAddress);
        Assert.Equal(2, vm.Breadcrumbs.Count);
        return (dump, vm);
    }

    private static void AssertRefused(LiveWalkerViewModel vm, string rowName)
    {
        Assert.DoesNotContain(vm.Breadcrumbs, b => b.FieldName == rowName);
        Assert.Contains("belongs to a view you have navigated away from", vm.StatusText);
    }

    [Fact]
    public async Task Back_ARowOfTheLevelBeingLeft_IsNotGraftedOntoTheNewParent()
    {
        var (dump, vm) = await WalkedToB();
        var gate = dump.Gate(A);

        var back = vm.GoBackCommand.ExecuteAsync(null);             // pops B at once, then walks A
        var dest = vm.Breadcrumbs[^1];                               // A — where Back is headed
        var hintBefore = dest.ScrollHintFieldName;                   // "ToB", from the drill into B
        var selBefore = dest.ViewSelectedFields?.Select(s => s.Name).ToList();
        var stale = vm.Fields.Single(f => f.Name == "ToC");          // the grid still shows B
        await vm.NavigateToFieldCommand.ExecuteAsync(stale);
        AssertRefused(vm, "ToC");
        // "Before ANY write": the refused drill must not overwrite the view state Back will
        // restore on A, and must not end Back's loading state.
        Assert.Equal(hintBefore, dest.ScrollHintFieldName);
        Assert.Equal(selBefore, dest.ViewSelectedFields?.Select(s => s.Name).ToList());
        Assert.True(vm.IsLoading, "a refused drill must not end Back's loading state");

        dump.Ungate(A);
        gate.SetResult(Obj(A, "BP_A_C", Ptr("ToB", 0x10, B), Ptr("ToD", 0x18, D)));
        await back;
        Assert.Single(vm.Breadcrumbs);
        Assert.Equal(A, vm.CurrentAddress);
    }

    [Fact]
    public async Task Back_AContainerOfTheLevelBeingLeft_IsNotOpenedUnderTheNewParent()
    {
        var (dump, vm) = await WalkedToB();
        var gate = dump.Gate(A);

        var back = vm.GoBackCommand.ExecuteAsync(null);
        await vm.NavigateToContainerCommand.ExecuteAsync(vm.Fields.Single(f => f.Name == "Items"));
        AssertRefused(vm, "Items");

        dump.Ungate(A);
        gate.SetResult(Obj(A, "BP_A_C", Ptr("ToB", 0x10, B), Ptr("ToD", 0x18, D)));
        await back;
    }

    [Fact]
    public async Task BreadcrumbJump_ARowOfTheLevelBeingLeft_IsNotGrafted()
    {
        var (dump, vm) = await WalkedToB();
        var gate = dump.Gate(A);

        var jump = vm.NavigateToBreadcrumbCommand.ExecuteAsync(vm.Breadcrumbs[0]);   // truncates, then walks A
        await vm.NavigateToFieldCommand.ExecuteAsync(vm.Fields.Single(f => f.Name == "ToC"));
        AssertRefused(vm, "ToC");

        dump.Ungate(A);
        gate.SetResult(Obj(A, "BP_A_C", Ptr("ToB", 0x10, B), Ptr("ToD", 0x18, D)));
        await jump;
    }

    [Fact]
    public async Task Forward_ARowOfTheLevelBeingLeft_IsNotGrafted()
    {
        var (dump, vm) = await WalkedToB();
        await vm.GoBackCommand.ExecuteAsync(null);                  // on A, with B on the forward stack
        Assert.Equal(A, vm.CurrentAddress);
        var gate = dump.Gate(B);

        var forward = vm.GoForwardCommand.ExecuteAsync(null);       // pushes B's crumb, then walks B
        await vm.NavigateToFieldCommand.ExecuteAsync(vm.Fields.Single(f => f.Name == "ToD"));   // A's row
        AssertRefused(vm, "ToD");

        dump.Ungate(B);
        gate.SetResult(dump_B());
        await forward;

        static InstanceWalkResult dump_B() => new()
        {
            Address = B, Name = "Obj" + B, ClassName = "BP_B_C", ClassAddr = "0x92000",
            Fields = new List<LiveFieldValue> { Ptr("ToC", 0x20, C) },
        };
    }

    [Fact]
    public async Task Parent_ARowOfTheLevelBeingLeft_IsNotGrafted()
    {
        var (dump, vm) = await WalkedToB();
        var gate = dump.Gate(P);

        var parent = vm.GoToParentCommand.ExecuteAsync(null);       // pushes the Outer crumb, then walks P
        await vm.NavigateToFieldCommand.ExecuteAsync(vm.Fields.Single(f => f.Name == "ToC"));
        AssertRefused(vm, "ToC");

        dump.Ungate(P);
        gate.SetResult(Obj(P, "BP_P_C", new LiveFieldValue { Name = "Z", TypeName = "IntProperty", Offset = 8, Size = 4 }));
        await parent;
    }

    // ── The same shape, found by the batch's adversarial review ─────────────────

    /// <summary>Two navigations in flight: Back's walk lands AFTER a Forward pressed meanwhile. The
    /// stamp used to record whatever crumb was current at render time, so Back's rows (A) were
    /// installed and stamped as Forward's crumb (B) — and a drill of one of them grafted A's field
    /// under B. A navigation whose target is no longer current must drop its render.</summary>
    [Fact]
    public async Task Back_ThenForward_BeforeBacksWalkLands_BacksRenderIsDiscarded()
    {
        var (dump, vm) = await WalkedToB();
        var gate = dump.Gate(A);

        var back = vm.GoBackCommand.ExecuteAsync(null);             // pops B, walks A (held)
        await vm.GoForwardCommand.ExecuteAsync(null);                // pushes B back, renders B
        Assert.Equal(B, vm.CurrentAddress);

        dump.Ungate(A);
        gate.SetResult(Obj(A, "BP_A_C", Ptr("ToB", 0x10, B), Ptr("ToD", 0x18, D)));
        await back;                                                  // Back's late render

        Assert.Equal(B, vm.CurrentAddress);                          // still B, not A's rows
        Assert.Equal("ToB", vm.Breadcrumbs[^1].FieldName);
        Assert.DoesNotContain(vm.Fields, f => f.Name == "ToD");      // no A row to graft under B
        Assert.Contains(vm.Fields, f => f.Name == "ToC");
    }

    /// <summary>Back's walk FAILS: the spine is on A, the grid still shows B. A Refresh (manual, or
    /// an auto tick) re-walks what is on screen, B — and used to re-stamp B's rows as A's, reopening
    /// the graft. Refresh re-walks the rendered object, so it must keep the stamp it found.</summary>
    [Fact]
    public async Task Back_WhoseWalkFails_ThenRefresh_TheOldRowsStayRefused()
    {
        var (dump, vm) = await WalkedToB();
        var gate = dump.Gate(A);
        var back = vm.GoBackCommand.ExecuteAsync(null);
        gate.SetException(new System.InvalidOperationException("walk failed"));
        await back;
        Assert.Single(vm.Breadcrumbs);                               // the spine moved to A
        Assert.Equal(B, vm.CurrentAddress);                          // the grid did not

        await vm.RefreshCommand.ExecuteAsync(null);                  // re-walks B, the object on screen
        await vm.NavigateToFieldCommand.ExecuteAsync(vm.Fields.Single(f => f.Name == "ToC"));

        AssertRefused(vm, "ToC");
        Assert.Single(vm.Breadcrumbs);
    }

    /// <summary>A Refresh in flight across a spine swap that keeps the address AND the crumb count:
    /// a re-rooted Back (one crumb for one crumb) whose own walk has not landed. The refresh used to
    /// check only the address and the count, so its reply was installed and stamped as the restored
    /// spine's crumb.</summary>
    [Fact]
    public async Task Refresh_AcrossASameCountSpineSwap_IsDiscarded()
    {
        var (dump, vm) = OnB();
        await vm.NavigateToAddressCommand.ExecuteAsync(A);           // spine [A]
        await vm.NavigateToAddressCommand.ExecuteAsync(B);           // re-root: spine [B], A held for Back
        var gateB = dump.Gate(B);
        var gateA = dump.Gate(A);

        var refresh = vm.RefreshCommand.ExecuteAsync(null);          // walks B (held)
        var back = vm.GoBackCommand.ExecuteAsync(null);              // re-rooted Back: spine [A], walks A (held)
        dump.Ungate(B);
        gateB.SetResult(dump_B());                                   // the refresh's reply lands FIRST
        await refresh;

        await vm.NavigateToFieldCommand.ExecuteAsync(vm.Fields.Single(f => f.Name == "ToC"));
        AssertRefused(vm, "ToC");                                    // B's rows are not A's

        dump.Ungate(A);
        gateA.SetResult(Obj(A, "BP_A_C", Ptr("ToB", 0x10, B), Ptr("ToD", 0x18, D)));
        await back;
        Assert.Equal(A, vm.CurrentAddress);

        static InstanceWalkResult dump_B() => new()
        {
            Address = B, Name = "Obj" + B, ClassName = "BP_B_C", ClassAddr = "0x92000",
            Fields = new List<LiveFieldValue> { Ptr("ToC", 0x20, C) },
        };
    }

    /// <summary>A re-root that FAILS after clearing the spine (a Go-box typo, an address the DLL
    /// rejects) leaves the previous rows on screen with no crumb. That is no graft risk — a drill
    /// there re-roots at the pointee, as it always did — so it must not be refused forever.</summary>
    [Fact]
    public async Task AFailedReRoot_LeavesTheOldRowsDrillable()
    {
        var (_, vm) = OnB();
        await vm.NavigateToAddressCommand.ExecuteAsync(A);
        await vm.NavigateToAddressCommand.ExecuteAsync("0xZZ");     // invalid: spine cleared, rows kept
        Assert.Empty(vm.Breadcrumbs);

        await vm.NavigateToFieldCommand.ExecuteAsync(vm.Fields.Single(f => f.Name == "ToD"));

        Assert.Equal(D, vm.CurrentAddress);
        Assert.Equal("ToD", Assert.Single(vm.Breadcrumbs).FieldName);
    }

    /// <summary>The control for the case above: while a re-root's walk is still IN FLIGHT the old
    /// rows are refused — that window is the graft.</summary>
    [Fact]
    public async Task AReRootInFlight_StillRefusesTheOldRows()
    {
        var (dump, vm) = OnB();
        await vm.NavigateToAddressCommand.ExecuteAsync(A);
        var gate = dump.Gate(B);
        var reroot = vm.NavigateToAddressCommand.ExecuteAsync(B);

        await vm.NavigateToFieldCommand.ExecuteAsync(vm.Fields.Single(f => f.Name == "ToD"));
        AssertRefused(vm, "ToD");

        dump.Ungate(B);
        gate.SetResult(Obj(B, "BP_B_C", Ptr("ToC", 0x20, C)));
        await reroot;
    }

    /// <summary>The same window through the commands that combine the CURRENT spine with the grid:
    /// Copy CE XML (all rows), Copy CE Field (the selection), CSX, and Save Bookmark (the address,
    /// class, selection and anchor of the rendered level). Each used to emit or persist the new
    /// spine with the old level's rows.</summary>
    [Fact]
    public async Task InTheWindow_ExportsAndBookmarkSave_AreRefused()
    {
        var platform = new MockPlatformService(Path.GetTempPath());
        var (dump, vm) = await WalkedToBWith(platform);
        var gate = dump.Gate(A);
        var back = vm.GoBackCommand.ExecuteAsync(null);
        vm.SelectedField = vm.Fields.Single(f => f.Name == "ToC");   // a row of the level being left

        await vm.ExportCeFieldXmlCommand.ExecuteAsync(null);
        Assert.Contains("navigated away from", vm.StatusText);
        await vm.ExportCeXmlCommand.ExecuteAsync(null);
        Assert.Contains("navigated away from", vm.StatusText);
        await vm.ExportCsx77Command.ExecuteAsync(null);
        Assert.Contains("navigated away from", vm.StatusText);
        Assert.Null(platform.LastClipboard);
        var slot = vm.BookmarkSlots[0];
        vm.SaveBookmarkToSlotCommand.Execute(slot);
        Assert.False(slot.IsOccupied);

        dump.Ungate(A);
        gate.SetResult(Obj(A, "BP_A_C", Ptr("ToB", 0x10, B), Ptr("ToD", 0x18, D)));
        await back;

        // Control: once the view has landed, the same bookmark save goes through.
        vm.SaveBookmarkToSlotCommand.Execute(slot);
        Assert.True(slot.IsOccupied);
        Assert.Equal(A, slot.SavedAddress);
    }

    // ── Negative controls: a too-strict stamp breaks the panel for everyone ──────

    [Fact]
    public async Task NoInterleaving_ARowOfTheCurrentLevel_StillDrills()
    {
        var (_, vm) = await WalkedToB();
        await vm.NavigateToFieldCommand.ExecuteAsync(vm.Fields.Single(f => f.Name == "ToC"));
        Assert.Equal("ToC", vm.Breadcrumbs[^1].FieldName);
        Assert.Equal(C, vm.CurrentAddress);
    }

    [Fact]
    public async Task NoInterleaving_AContainerOfTheCurrentLevel_StillOpens()
    {
        var (_, vm) = await WalkedToB();
        await vm.NavigateToContainerCommand.ExecuteAsync(vm.Fields.Single(f => f.Name == "Items"));
        Assert.True(vm.Breadcrumbs[^1].IsContainerView);
        Assert.Equal("Items", vm.Breadcrumbs[^1].FieldName);
    }

    [Fact]
    public async Task AfterBackLands_TheNewLevelsRows_Drill()
    {
        // The stamp must follow the grid: once Back's walk lands, A's rows are current again.
        var (_, vm) = await WalkedToB();
        await vm.GoBackCommand.ExecuteAsync(null);
        await vm.NavigateToFieldCommand.ExecuteAsync(vm.Fields.Single(f => f.Name == "ToD"));
        Assert.Equal("ToD", vm.Breadcrumbs[^1].FieldName);
    }

    [Fact]
    public async Task InsideAContainerView_AnElementRowDrills()
    {
        // The container views repopulate the grid WITHOUT UpdateDisplay; if one forgot to stamp,
        // every drill from inside a container view would be refused.
        var (_, vm) = await WalkedToB();
        await vm.NavigateToContainerCommand.ExecuteAsync(vm.Fields.Single(f => f.Name == "Parts"));
        Assert.Equal("Parts", vm.Breadcrumbs[^1].FieldName);

        await vm.NavigateToFieldCommand.ExecuteAsync(vm.Fields.Single(f => f.Name == "[1]"));

        Assert.Equal("[1]", vm.Breadcrumbs[^1].FieldName);
        Assert.DoesNotContain("belongs to the view you just left", vm.StatusText ?? "");
    }

    // Every OTHER population site stamps too. Each pin below goes red if its site's stamp is
    // deleted: the drill would be refused on a perfectly current row.

    [Fact]
    public async Task InsideAMapView_AValueRowDrills()
    {
        var (_, vm) = await WalkedToB();
        await vm.NavigateToContainerCommand.ExecuteAsync(vm.Fields.Single(f => f.Name == "Loot"));
        await vm.NavigateToFieldCommand.ExecuteAsync(vm.Fields.Single(f => f.Name == "[0] 7"));
        Assert.Equal("[0] 7", vm.Breadcrumbs[^1].FieldName);
    }

    [Fact]
    public async Task InsideASetView_AnElementRowDrills()
    {
        var (_, vm) = await WalkedToB();
        await vm.NavigateToContainerCommand.ExecuteAsync(vm.Fields.Single(f => f.Name == "Tags"));
        await vm.NavigateToFieldCommand.ExecuteAsync(vm.Fields.Single(f => f.Name == "[0]"));
        Assert.Equal("[0]", vm.Breadcrumbs[^1].FieldName);
    }

    [Fact]
    public async Task InsideADataTableView_ARowDrills()
    {
        var (_, vm) = await WalkedToB();
        var rowMap = new LiveFieldValue
        {
            Name = "RowMap", TypeName = "DataTableRows", Offset = 0xB0,
            DataTableRowCount = 1, DataTableStructName = "FRecipe",
        };
        vm.NavigateToDataTableContainer(rowMap, new DataTableWalkResult
        {
            RowCount = 1, RowMapOffset = 0xB0, RowStructAddr = "0x9600", RowStructName = "FRecipe",
            FNameSize = 8, Stride = 24,
            Rows = new() { new DataTableRowInfo { SparseIndex = 0, RowName = "Sword", DataAddr = "0x9700" } },
        });

        await vm.NavigateToFieldCommand.ExecuteAsync(vm.Fields.Single(f => f.Name == "[0] Sword"));
        Assert.Equal("[0] Sword", vm.Breadcrumbs[^1].FieldName);
    }

    [Fact]
    public async Task FromTheGWorldRoot_ARowDrills()
    {
        var (_, vm) = OnB();
        await vm.StartFromWorldCommand.ExecuteAsync(null);     // PopulateFromWorld, not UpdateDisplay
        await vm.NavigateToFieldCommand.ExecuteAsync(vm.Fields.Single(f => f.Name == "PersistentLevel"));
        Assert.Equal("PersistentLevel", vm.Breadcrumbs[^1].FieldName);
    }

    [Fact]
    public async Task BackFromAContainerView_TheParentLevelsRowsDrill()
    {
        var (_, vm) = await WalkedToB();
        await vm.NavigateToContainerCommand.ExecuteAsync(vm.Fields.Single(f => f.Name == "Items"));
        await vm.GoBackCommand.ExecuteAsync(null);                  // back to B's grid, re-walked
        await vm.NavigateToFieldCommand.ExecuteAsync(vm.Fields.Single(f => f.Name == "ToC"));
        Assert.Equal("ToC", vm.Breadcrumbs[^1].FieldName);
        Assert.Equal(C, vm.CurrentAddress);
    }
}
