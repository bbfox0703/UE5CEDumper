using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UE5DumpUI.Models;
using UE5DumpUI.ViewModels;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// The gate on Live Walker's in-place refresh branch. [P4-OTHER-INSTANCE] [P4-GUESS-SHIFT]
///
/// Since [LWREFRESH-2026-08-21] a refresh copies the fresh values ONTO the existing row objects
/// (<see cref="LiveFieldValue.CopyLiveValuesFrom"/>) instead of replacing them, which is what keeps
/// the grid from drifting one row per refresh. But the members that copy does NOT take are
/// <c>init</c>, so the branch is only correct when those members cannot have changed: the SAME
/// object, with the SAME row layout. The branch used to check only the field count and
/// <c>Fields[0].Name</c>, so:
/// <list type="bullet">
/// <item>opening instance B of a class already on screen kept instance A's rows, with A's
/// ABSOLUTE <c>StructDataAddr</c> — drilling B's struct walked A's memory, and an edit wrote into
/// A in the running game;</item>
/// <item>with Guess? on, guessed rows re-derived from the bytes could move while the count stayed
/// equal, and every row between two gaps showed its neighbour's value and address under its own
/// name, still editable.</item>
/// </list>
/// Pure VM state — the stub dump service answers every walk, no running game needed.
/// </summary>
public class LiveWalkerSameObjectGateTests
{
    private const string AddrA = "0x100000";
    private const string AddrB = "0x200000";

    private static LiveWalkerViewModel MakeVm(StubDumpService dump)
        => new(dump, new MockLoggingService(), new MockPlatformService(Path.GetTempPath()));

    /// <summary>An instance of BP_Enemy_C: a float and an inline struct whose data address is
    /// ABSOLUTE (instance + offset), exactly as the DLL reports it.</summary>
    private static InstanceWalkResult Enemy(string addr, ulong baseAddr, string health)
        => new()
        {
            Address = addr,
            Name = $"BP_Enemy_C_{addr}",
            ClassName = "BP_Enemy_C",
            ClassAddr = "0x900000",
            Fields = new List<LiveFieldValue>
            {
                new() { Name = "Health", TypeName = "FloatProperty", Offset = 0x10, Size = 4, TypedValue = health },
                new() { Name = "Stats", TypeName = "StructProperty", Offset = 0x20, Size = 16,
                        StructTypeName = "FEnemyStats", StructClassAddr = "0x950000",
                        StructDataAddr = $"0x{baseAddr + 0x20:X}" },
            },
        };

    [Fact]
    public async Task OpeningASecondInstanceOfTheSameClass_ShowsThatInstancesOwnRows()
    {
        var dump = new StubDumpService();
        dump.RegisterStruct(AddrA, Enemy(AddrA, 0x100000, "100"));
        dump.RegisterStruct(AddrB, Enemy(AddrB, 0x200000, "40"));
        var vm = MakeVm(dump);

        await vm.NavigateToAddressCommand.ExecuteAsync(AddrA);
        var rowsOfA = vm.Fields.ToList();
        Assert.Equal("0x100020", vm.Fields[1].StructDataAddr);

        // Same class, same count, same first name -- the old gate's three facts all match.
        await vm.NavigateToAddressCommand.ExecuteAsync(AddrB);

        Assert.Equal(AddrB, vm.CurrentAddress);
        Assert.Equal("40", vm.Fields[0].TypedValue);   // the copied member was right either way
        // The member the copy does NOT take. Before the fix it stayed A's, so drilling "B > Stats"
        // walked A's struct and an edit there wrote A's memory.
        Assert.Equal("0x200020", vm.Fields[1].StructDataAddr);
        // A different object gets its own rows, not A's rows repainted.
        for (int i = 0; i < rowsOfA.Count; i++)
            Assert.NotSame(rowsOfA[i], vm.Fields[i]);
    }

    [Fact]
    public async Task Refresh_WhenTheAddressNowHoldsAnotherClass_Rebuilds()
    {
        // The same address, a different class: an inline struct at offset 0 shares its owner's
        // address, and a freed object's slot can be reused by another class. The address alone
        // does not identify the object.
        var dump = new StubDumpService();
        dump.RegisterStruct(AddrA, Enemy(AddrA, 0x100000, "100"));
        var vm = MakeVm(dump);
        await vm.NavigateToAddressCommand.ExecuteAsync(AddrA);
        var before = vm.Fields.ToList();

        var other = Enemy(AddrA, 0x100000, "100");
        dump.RegisterStruct(AddrA, new InstanceWalkResult
        {
            Address = AddrA, Name = "Recycled", ClassName = "BP_Pickup_C", ClassAddr = "0x980000",
            Fields = other.Fields,   // an identical row layout, so only the class tells them apart
        });

        await vm.RefreshCommand.ExecuteAsync(null);

        Assert.Equal("BP_Pickup_C", vm.CurrentClassName);
        for (int i = 0; i < before.Count; i++)
            Assert.NotSame(before[i], vm.Fields[i]);
    }

    /// <summary>The cross-gap variant of [P4-GUESS-SHIFT]: one gap loses a guessed row and a later
    /// one gains one in the same walk, so the count and the first name both still match.</summary>
    [Fact]
    public async Task Refresh_WhenGuessedRowsMoved_EveryRowKeepsItsOwnAddressAndValue()
    {
        var dump = new StubDumpService();
        dump.RegisterStruct(AddrA, new InstanceWalkResult
        {
            Address = AddrA, Name = "Hero", ClassName = "BP_Hero_C", ClassAddr = "0x900000",
            Fields = new List<LiveFieldValue>
            {
                new() { Name = "Health",     TypeName = "FloatProperty", Offset = 0x10, Size = 4, TypedValue = "100" },
                new() { Name = "Guess_0x14", TypeName = "IntProperty",   Offset = 0x14, Size = 4, TypedValue = "7", IsGuessed = true },
                new() { Name = "Mana",       TypeName = "FloatProperty", Offset = 0x18, Size = 4, TypedValue = "50" },
                new() { Name = "Stamina",    TypeName = "FloatProperty", Offset = 0x20, Size = 4, TypedValue = "80" },
            },
        });
        var vm = MakeVm(dump);
        await vm.NavigateToAddressCommand.ExecuteAsync(AddrA);

        // Same object, next walk: the gap at 0x14 read as padding this time, and a new guessed row
        // appeared at 0x1C. Four rows, first name Health -- the old gate let this through.
        dump.RegisterStruct(AddrA, new InstanceWalkResult
        {
            Address = AddrA, Name = "Hero", ClassName = "BP_Hero_C", ClassAddr = "0x900000",
            Fields = new List<LiveFieldValue>
            {
                new() { Name = "Health",     TypeName = "FloatProperty", Offset = 0x10, Size = 4, TypedValue = "99" },
                new() { Name = "Mana",       TypeName = "FloatProperty", Offset = 0x18, Size = 4, TypedValue = "49" },
                new() { Name = "Guess_0x1C", TypeName = "IntProperty",   Offset = 0x1C, Size = 4, TypedValue = "3", IsGuessed = true },
                new() { Name = "Stamina",    TypeName = "FloatProperty", Offset = 0x20, Size = 4, TypedValue = "79" },
            },
        });

        await vm.RefreshCommand.ExecuteAsync(null);

        // Every row's address must be its OWN offset. Before the fix the row named Guess_0x14 held
        // Mana's address and value, and the row named Mana held the new guess's -- editable.
        foreach (var f in vm.Fields)
            Assert.Equal($"0x{0x100000UL + (ulong)f.Offset:X}", f.FieldAddress);
        var mana = Assert.Single(vm.Fields, f => f.Name == "Mana");
        Assert.Equal("49", mana.TypedValue);
        Assert.Equal(0x18, mana.Offset);
    }

    /// <summary>The control, and the [LWREFRESH-2026-08-21] guarantee the gate must not lose: a
    /// same-object, same-layout refresh keeps the row OBJECTS (so the grid does not move) and takes
    /// the new values.</summary>
    [Fact]
    public async Task Refresh_SameObjectSameLayout_KeepsTheRowObjects_AndTakesTheNewValues()
    {
        var dump = new StubDumpService();
        dump.RegisterStruct(AddrA, Enemy(AddrA, 0x100000, "100"));
        var vm = MakeVm(dump);
        await vm.NavigateToAddressCommand.ExecuteAsync(AddrA);
        var before = vm.Fields.ToList();

        dump.RegisterStruct(AddrA, Enemy(AddrA, 0x100000, "73"));
        await vm.RefreshCommand.ExecuteAsync(null);

        Assert.Equal(before.Count, vm.Fields.Count);
        for (int i = 0; i < before.Count; i++)
            Assert.Same(before[i], vm.Fields[i]);
        Assert.Equal("73", vm.Fields[0].TypedValue);
    }

    // ── Each check of the gate on its own: ONE fact changes per case ─────────────
    //
    // The tests above change several facts at once, so deleting any single check would leave them
    // green (working-lessons §1.2a: one negative control validates one axis). Every case below
    // changes exactly one fact, so each check is pinned by itself.

    private static async Task<(LiveWalkerViewModel vm, List<LiveFieldValue> before)> OnA(
        StubDumpService dump, InstanceWalkResult first)
    {
        dump.RegisterStruct(AddrA, first);
        var vm = MakeVm(dump);
        await vm.NavigateToAddressCommand.ExecuteAsync(AddrA);
        return (vm, vm.Fields.ToList());
    }

    private static InstanceWalkResult WithClass(InstanceWalkResult r, string className, string classAddr)
        => new() { Address = r.Address, Name = r.Name, ClassName = className, ClassAddr = classAddr, Fields = r.Fields };

    private static void AssertRebuilt(List<LiveFieldValue> before, LiveWalkerViewModel vm)
    {
        Assert.Equal(before.Count, vm.Fields.Count);
        for (int i = 0; i < before.Count; i++)
            Assert.NotSame(before[i], vm.Fields[i]);
    }

    private static void AssertKept(List<LiveFieldValue> before, LiveWalkerViewModel vm)
    {
        Assert.Equal(before.Count, vm.Fields.Count);
        for (int i = 0; i < before.Count; i++)
            Assert.Same(before[i], vm.Fields[i]);
    }

    [Fact]
    public async Task Refresh_SameClassName_DifferentClassAddr_Rebuilds()
    {
        var dump = new StubDumpService();
        var (vm, before) = await OnA(dump, Enemy(AddrA, 0x100000, "100"));
        dump.RegisterStruct(AddrA, WithClass(Enemy(AddrA, 0x100000, "100"), "BP_Enemy_C", "0x980000"));
        await vm.RefreshCommand.ExecuteAsync(null);
        AssertRebuilt(before, vm);
    }

    [Fact]
    public async Task Refresh_DifferentClassName_NoClassAddr_Rebuilds()
    {
        // No class address on the new walk: the address check is skipped, so the NAME must decide.
        var dump = new StubDumpService();
        var (vm, before) = await OnA(dump, Enemy(AddrA, 0x100000, "100"));
        dump.RegisterStruct(AddrA, WithClass(Enemy(AddrA, 0x100000, "100"), "BP_Pickup_C", ""));
        await vm.RefreshCommand.ExecuteAsync(null);
        AssertRebuilt(before, vm);
    }

    [Fact]
    public async Task Refresh_SameClassName_NoClassAddr_KeepsTheRows()
    {
        // The control for the lenient path: a missing class address alone must not force a rebuild.
        var dump = new StubDumpService();
        var (vm, before) = await OnA(dump, Enemy(AddrA, 0x100000, "100"));
        dump.RegisterStruct(AddrA, WithClass(Enemy(AddrA, 0x100000, "73"), "BP_Enemy_C", ""));
        await vm.RefreshCommand.ExecuteAsync(null);
        AssertKept(before, vm);
        Assert.Equal("73", vm.Fields[0].TypedValue);
    }

    [Theory]
    [InlineData("Name")]
    [InlineData("Offset")]
    [InlineData("TypeName")]
    [InlineData("Size")]
    [InlineData("IsGuessed")]
    public async Task Refresh_OneRowFactDiffers_Rebuilds(string fact)
    {
        static InstanceWalkResult Walk(string changed) => new()
        {
            Address = AddrA, Name = "Hero", ClassName = "BP_Hero_C", ClassAddr = "0x900000",
            Fields = new List<LiveFieldValue>
            {
                new() { Name = "Health", TypeName = "FloatProperty", Offset = 0x10, Size = 4, TypedValue = "100" },
                new()
                {
                    Name      = changed == "Name"      ? "Mana2"       : "Mana",
                    Offset    = changed == "Offset"    ? 0x1C          : 0x18,
                    TypeName  = changed == "TypeName"  ? "IntProperty" : "FloatProperty",
                    Size      = changed == "Size"      ? 8             : 4,
                    IsGuessed = changed == "IsGuessed",
                    TypedValue = "1",
                },
            },
        };

        var dump = new StubDumpService();
        var (vm, before) = await OnA(dump, Walk(""));
        dump.RegisterStruct(AddrA, Walk(fact));
        await vm.RefreshCommand.ExecuteAsync(null);
        AssertRebuilt(before, vm);
    }

    /// <summary>A guessed float or double carries a VALUE-driven confidence suffix
    /// (<c>Ubel.cpp</c> <c>IsLikelyFloat</c>: "Float" only for a clean .0/.5 at or below 1000,
    /// "Float?" otherwise), while its Name (<c>?0x14_float</c>), Offset and Size stay put. That is
    /// not a layout change: a stat draining from 100.0 to 87.3 must not jump the grid to the top on
    /// every Auto tick, which is the [LWREFRESH-2026-08-21] defect the in-place branch exists for.
    /// Found by the gate's own adversarial review.</summary>
    [Theory]
    [InlineData("Float", "Float?")]
    [InlineData("Float?", "Float")]
    [InlineData("Double", "Double?")]
    public async Task Refresh_GuessedRowOnlyChangesItsConfidenceLabel_KeepsTheRows(string before, string after)
    {
        static InstanceWalkResult Walk(string type, string value) => new()
        {
            Address = AddrA, Name = "Stats", ClassName = "FHeroStats", ClassAddr = "0x950000",
            Fields = new List<LiveFieldValue>
            {
                new() { Name = "Level", TypeName = "IntProperty", Offset = 0x0, Size = 4, TypedValue = "3" },
                new() { Name = "?0x10_float", TypeName = type, Offset = 0x10, Size = 4,
                        TypedValue = value, IsGuessed = true },
            },
        };

        var dump = new StubDumpService();
        var (vm, rows) = await OnA(dump, Walk(before, "100"));
        dump.RegisterStruct(AddrA, Walk(after, "87.3"));
        await vm.RefreshCommand.ExecuteAsync(null);

        AssertKept(rows, vm);
        Assert.Equal("87.3", vm.Fields[1].TypedValue);
    }
}
