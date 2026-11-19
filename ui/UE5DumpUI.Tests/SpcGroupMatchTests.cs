using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UE5DumpUI.Models;
using UE5DumpUI.Services;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// SPC GROUP query over a real temp SQLite store — the N-snapshot, object-aware
/// generalisation of Snapshot Group Match Mode B. Verifies the store loads the SPC
/// cross-snapshot intersection, groups candidate fields per object, and runs the SDR
/// matcher where each value-slot carries its OWN per-snapshot predicate CHAIN
/// (evaluated by SpcEngine). See docs/group-value-scan-spec.md §3.1.
/// </summary>
public class SpcGroupMatchTests : IDisposable
{
    private readonly string _tempDir;
    private readonly SnapshotStore _store;

    public SpcGroupMatchTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"UE5DumpSpcGroupTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _store = new SnapshotStore(new MockPlatformService(_tempDir));
        _store.SetActiveGame("SPCGROUP");
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); }
        catch { /* WAL files may linger briefly; best effort */ }
    }

    private static string IntHex(int v) => Convert.ToHexString(BitConverter.GetBytes(v)); // LE

    private static SnapshotCapturedObject Obj(int index, string cls,
        params (string name, string type, int offset, string hex)[] fields)
    {
        var o = new SnapshotCapturedObject
        {
            Index = index,
            Addr = $"0x{index * 0x1000:X}",
            Name = $"{cls}_{index}",
            ClassName = cls,
            OuterClassName = "World",
            Path = $"/Game/Map.Map:PersistentLevel.{cls}_{index}",
        };
        foreach (var (name, type, offset, hex) in fields)
            o.Fields.Add(new SnapshotCapturedField { Name = name, Type = type, Offset = offset, Hex = hex });
        return o;
    }

    // Object whose values live ONLY inside a nested leaf-container array (no inner
    // prop name) — the deep / struct-array case the SPC load includes for free.
    private static SnapshotCapturedObject ObjWithArray(int index, string cls, string arrayField,
        params (int elem, string hex)[] elems)
    {
        var o = new SnapshotCapturedObject
        {
            Index = index, Addr = $"0x{index * 0x1000:X}", Name = $"{cls}_{index}",
            ClassName = cls, OuterClassName = "World", Path = $"/Game/Map:{cls}_{index}",
        };
        var arr = new SnapshotCapturedArray { Field = arrayField };
        foreach (var (elem, hex) in elems)
            arr.Elements.Add(new SnapshotCapturedArrayElement
            {
                Index = elem,
                Fields = { new SnapshotCapturedField { Name = "", Type = "IntProperty", Hex = hex } },
            });
        o.Arrays.Add(arr);
        return o;
    }

    // Seed an ordered sequence of snapshots (oldest first), all in one game session so
    // the In-session join (gobjects_index) tracks each object across them.
    private async System.Threading.Tasks.Task<List<long>> SeedSeqAsync(
        System.Threading.CancellationToken ct, params SnapshotCapturedObject[][] perSnapshot)
    {
        var ids = new List<long>();
        foreach (var objs in perSnapshot)
        {
            long id = await _store.CreateSnapshotAsync(
                new SnapshotMeta { Label = $"s{ids.Count}", Scope = "NumericNoByte", GameSessionId = "S" }, ct);
            int rows = await _store.WriteChunkAsync(id, objs, ct);
            await _store.FinalizeSnapshotAsync(id, objs.Length, rows, ct);
            ids.Add(id);
        }
        return ids;
    }

    private static SpcGroupCell Cell(SpcPredicateKind p) => new() { Predicate = p };
    private static SpcGroupCell CellAbs(SpcPredicateKind p, SpcAbsoluteKind k, double lo = 0, double hi = 0) =>
        new() { Predicate = p, Absolute = new SpcAbsolutePredicate { Kind = k, Low = lo, High = hi } };

    private static SpcGroupSlot Slot(params SpcGroupCell[] cells) =>
        new() { Scope = ValueScanDataType.NumericNoByte, Chain = cells.ToList() };

    private static SpcGroupQuery Query(IReadOnlyList<long> ids, params SpcGroupSlot[] slots) => new()
    {
        SnapshotIds = ids.ToList(),
        Slots = slots.ToList(),
        JoinMode = SpcJoinMode.InSession,
    };

    [Fact]
    public async Task ThreeSnapshotChain_CurHpDownUp_MaxHpHeld_MatchesOnlyTheRightActor()
    {
        var ct = TestContext.Current.CancellationToken;
        // CurHP: 100 -> 80 (Decreased) -> 90 (Increased); MaxHP: 100 held throughout.
        // Actor 1 follows that pattern; Actor 2's MaxHP also moves (level-up at step 3).
        var ids = await SeedSeqAsync(ct,
            new[]
            {
                Obj(1, "BP_Player_C", ("CurHP", "IntProperty", 0x10, IntHex(100)), ("MaxHP", "IntProperty", 0x14, IntHex(100))),
                Obj(2, "BP_Player_C", ("CurHP", "IntProperty", 0x10, IntHex(100)), ("MaxHP", "IntProperty", 0x14, IntHex(100))),
            },
            new[]
            {
                Obj(1, "BP_Player_C", ("CurHP", "IntProperty", 0x10, IntHex(80)),  ("MaxHP", "IntProperty", 0x14, IntHex(100))),
                Obj(2, "BP_Player_C", ("CurHP", "IntProperty", 0x10, IntHex(80)),  ("MaxHP", "IntProperty", 0x14, IntHex(100))),
            },
            new[]
            {
                Obj(1, "BP_Player_C", ("CurHP", "IntProperty", 0x10, IntHex(90)),  ("MaxHP", "IntProperty", 0x14, IntHex(100))),  // healed
                Obj(2, "BP_Player_C", ("CurHP", "IntProperty", 0x10, IntHex(90)),  ("MaxHP", "IntProperty", 0x14, IntHex(130))),  // level-up
            });

        var q = Query(ids,
            Slot(Cell(SpcPredicateKind.Any), Cell(SpcPredicateKind.Decreased), Cell(SpcPredicateKind.Increased)),
            Slot(Cell(SpcPredicateKind.Any), Cell(SpcPredicateKind.Unchanged), Cell(SpcPredicateKind.Unchanged)));
        var res = await _store.SpcGroupQueryAsync(q, ct);

        Assert.Null(res.Error);
        var c = Assert.Single(res.Candidates);     // only actor 1 keeps MaxHP unchanged across both steps
        Assert.Equal(1, c.InstanceIndex);
        Assert.Equal(2, c.Slots.Count);
        Assert.Contains(c.Slots, s => s.FieldName == "CurHP" && s.FieldOffset == 0x10);
        Assert.Contains(c.Slots, s => s.FieldName == "MaxHP" && s.FieldOffset == 0x14);
    }

    [Fact]
    public async Task DistinctFields_TwoDecreasingSlots_NeedTwoDistinctLeaves()
    {
        var ct = TestContext.Current.CancellationToken;
        var ids = await SeedSeqAsync(ct,
            new[]
            {
                Obj(1, "BP_A_C", ("X", "IntProperty", 0x10, IntHex(100)), ("Y", "IntProperty", 0x14, IntHex(100))),
                Obj(2, "BP_A_C", ("X", "IntProperty", 0x10, IntHex(100)), ("Y", "IntProperty", 0x14, IntHex(100))),
            },
            new[]
            {
                Obj(1, "BP_A_C", ("X", "IntProperty", 0x10, IntHex(50)), ("Y", "IntProperty", 0x14, IntHex(40))),  // both down
                Obj(2, "BP_A_C", ("X", "IntProperty", 0x10, IntHex(50)), ("Y", "IntProperty", 0x14, IntHex(100))), // only X down
            });

        var q = Query(ids,
            Slot(Cell(SpcPredicateKind.Any), Cell(SpcPredicateKind.Decreased)),
            Slot(Cell(SpcPredicateKind.Any), Cell(SpcPredicateKind.Decreased)));
        var res = await _store.SpcGroupQueryAsync(q, ct);

        Assert.Null(res.Error);
        var c = Assert.Single(res.Candidates);      // only obj 1 has TWO distinct decreasing fields
        Assert.Equal(1, c.InstanceIndex);
    }

    [Fact]
    public async Task AbsoluteMatrix_WindowCutsOutOfRangeField()
    {
        var ct = TestContext.Current.CancellationToken;
        // Object 1: HP 300->120 and MP 200->150 — both Decreased AND inside 0..500.
        // Object 2: HP 300->120 (in window) but Width 1920->0 (a UI-noise drop OUT of
        // window) — so only ONE field fits the windowed Decreased, can't fill 2 slots.
        var ids = await SeedSeqAsync(ct,
            new[]
            {
                Obj(1, "BP_Unit_C", ("HP", "IntProperty", 0x10, IntHex(300)), ("MP", "IntProperty", 0x14, IntHex(200))),
                Obj(2, "BP_Unit_C", ("HP", "IntProperty", 0x10, IntHex(300)), ("Width", "IntProperty", 0x14, IntHex(1920))),
            },
            new[]
            {
                Obj(1, "BP_Unit_C", ("HP", "IntProperty", 0x10, IntHex(120)), ("MP", "IntProperty", 0x14, IntHex(150))),
                Obj(2, "BP_Unit_C", ("HP", "IntProperty", 0x10, IntHex(120)), ("Width", "IntProperty", 0x14, IntHex(0))),
            });

        var slot = Slot(
            CellAbs(SpcPredicateKind.Any,       SpcAbsoluteKind.Between, 0, 500),
            CellAbs(SpcPredicateKind.Decreased, SpcAbsoluteKind.Between, 0, 500));
        var q = Query(ids, slot, Slot(
            CellAbs(SpcPredicateKind.Any,       SpcAbsoluteKind.Between, 0, 500),
            CellAbs(SpcPredicateKind.Decreased, SpcAbsoluteKind.Between, 0, 500)));
        var res = await _store.SpcGroupQueryAsync(q, ct);

        Assert.Null(res.Error);
        var c = Assert.Single(res.Candidates);
        Assert.Equal(1, c.InstanceIndex);           // Width (1920) excluded → object 2 can't fill both slots
    }

    [Fact]
    public async Task ExcludedClasses_AreSkipped()
    {
        var ct = TestContext.Current.CancellationToken;
        var ids = await SeedSeqAsync(ct,
            new[]
            {
                Obj(1, "BP_Player_C",  ("A", "IntProperty", 0x10, IntHex(100)), ("B", "IntProperty", 0x14, IntHex(100))),
                Obj(2, "WidgetNoise_C", ("A", "IntProperty", 0x10, IntHex(100)), ("B", "IntProperty", 0x14, IntHex(100))),
            },
            new[]
            {
                Obj(1, "BP_Player_C",  ("A", "IntProperty", 0x10, IntHex(80)), ("B", "IntProperty", 0x14, IntHex(80))),
                Obj(2, "WidgetNoise_C", ("A", "IntProperty", 0x10, IntHex(80)), ("B", "IntProperty", 0x14, IntHex(80))),
            });

        var q = Query(ids,
            Slot(Cell(SpcPredicateKind.Any), Cell(SpcPredicateKind.Decreased)),
            Slot(Cell(SpcPredicateKind.Any), Cell(SpcPredicateKind.Decreased)));
        q.ExcludedClasses = new HashSet<string>(StringComparer.Ordinal) { "WidgetNoise_C" };
        var res = await _store.SpcGroupQueryAsync(q, ct);

        Assert.Null(res.Error);
        var c = Assert.Single(res.Candidates);
        Assert.Equal("BP_Player_C", c.ClassName);
    }

    [Fact]
    public async Task Deep_NestedArrayElements_AreGroupedUnderTheirOwner()
    {
        var ct = TestContext.Current.CancellationToken;
        // Tunes[0] held at 10; Tunes[2] dropped 20 -> 18. The values live ONLY in the
        // nested array — the SPC load includes array_field rows, so each element is a
        // candidate field grouped under BP_Save_C.
        var ids = await SeedSeqAsync(ct,
            new[] { ObjWithArray(1, "BP_Save_C", "SaveSlot[0].Tunes", (0, IntHex(10)), (2, IntHex(20))) },
            new[] { ObjWithArray(1, "BP_Save_C", "SaveSlot[0].Tunes", (0, IntHex(10)), (2, IntHex(18))) });

        var q = Query(ids,
            Slot(Cell(SpcPredicateKind.Any), Cell(SpcPredicateKind.Unchanged)),
            Slot(Cell(SpcPredicateKind.Any), Cell(SpcPredicateKind.Decreased)));
        var res = await _store.SpcGroupQueryAsync(q, ct);

        Assert.Null(res.Error);
        var c = Assert.Single(res.Candidates);
        Assert.Equal("BP_Save_C", c.ClassName);
        Assert.Contains(c.Slots, s => s.FieldName.Contains("Tunes[0]"));
        Assert.Contains(c.Slots, s => s.FieldName.Contains("Tunes[2]"));
    }

    [Fact]
    public async Task ThreeSnapshotChain_DeepNestedTunes_PerSlotChains_TheInGameSeedCase()
    {
        var ct = TestContext.Current.CancellationToken;
        // Mirrors the in-game SEED verification (BP_LifeSaveData_C, a deeply-nested Tunes[7]
        // int array). Across 3 snapshots:
        //   Tunes[1]: 15 -> 15 -> 150   (· Unchanged Increased)
        //   Tunes[2]: 20 -> 21 -> 21    (· Increased Unchanged)
        // plus a third all-Any slot riding along (like the in-game version_no field).
        const string path = "SaveSlotList[1].MsTuneData.MsTunes[0].WeaponTuneList[0].Tunes";
        var ids = await SeedSeqAsync(ct,
            new[] { ObjWithArray(1, "BP_LifeSaveData_C", path,
                (0, IntHex(10)), (1, IntHex(15)),  (2, IntHex(20)), (3, IntHex(0)), (4, IntHex(25)), (5, IntHex(3)), (6, IntHex(0))) },
            new[] { ObjWithArray(1, "BP_LifeSaveData_C", path,
                (0, IntHex(10)), (1, IntHex(15)),  (2, IntHex(21)), (3, IntHex(0)), (4, IntHex(25)), (5, IntHex(3)), (6, IntHex(0))) },
            new[] { ObjWithArray(1, "BP_LifeSaveData_C", path,
                (0, IntHex(10)), (1, IntHex(150)), (2, IntHex(21)), (3, IntHex(0)), (4, IntHex(25)), (5, IntHex(3)), (6, IntHex(0))) });

        var q = Query(ids,
            Slot(Cell(SpcPredicateKind.Any), Cell(SpcPredicateKind.Unchanged), Cell(SpcPredicateKind.Increased)),  // Tunes[1]
            Slot(Cell(SpcPredicateKind.Any), Cell(SpcPredicateKind.Increased), Cell(SpcPredicateKind.Unchanged)),  // Tunes[2]
            Slot(Cell(SpcPredicateKind.Any), Cell(SpcPredicateKind.Any),       Cell(SpcPredicateKind.Any)));       // ride-along
        var res = await _store.SpcGroupQueryAsync(q, ct);

        Assert.Null(res.Error);
        var c = Assert.Single(res.Candidates);
        Assert.Equal("BP_LifeSaveData_C", c.ClassName);
        Assert.Equal(3, c.Slots.Count);
        // The two chained slots resolve to the right elements with their newest values.
        Assert.Contains(c.Slots, s => s.FieldName.Contains("Tunes[1]") && s.LeafValue == "150");
        Assert.Contains(c.Slots, s => s.FieldName.Contains("Tunes[2]") && s.LeafValue == "21");
    }

    [Fact]
    public async Task ArrayElements_SlotMatchingTwo_IsNotFalselyLocked()
    {
        var ct = TestContext.Current.CancellationToken;
        // All values live in ONE array (offset 0 for every element). Tunes[0] + Tunes[1]
        // both decrease; Tunes[2] holds. The Decreased slot matches TWO elements, so it
        // must NOT report "locked" (a single resolved offset) — a dedup by offset alone
        // would collapse both offset-0 elements to one and falsely lock it.
        var ids = await SeedSeqAsync(ct,
            new[] { ObjWithArray(1, "BP_Save_C", "Tunes", (0, IntHex(10)), (1, IntHex(20)), (2, IntHex(5))) },
            new[] { ObjWithArray(1, "BP_Save_C", "Tunes", (0, IntHex(8)),  (1, IntHex(18)), (2, IntHex(5))) });

        var q = Query(ids,
            Slot(Cell(SpcPredicateKind.Any), Cell(SpcPredicateKind.Decreased)),
            Slot(Cell(SpcPredicateKind.Any), Cell(SpcPredicateKind.Unchanged)));
        var res = await _store.SpcGroupQueryAsync(q, ct);

        var c = Assert.Single(res.Candidates);
        var dec = c.Slots[0];   // Decreased — matched Tunes[0] AND Tunes[1]
        var unc = c.Slots[1];   // Unchanged — matched only Tunes[2]
        Assert.False(dec.Locked);
        Assert.Equal(2, dec.MatchedOffsets.Count);
        Assert.True(unc.Locked);
    }

    [Fact]
    public async Task TwoSlotsMatchingSameFields_GetDistinctRepresentatives()
    {
        var ct = TestContext.Current.CancellationToken;
        // X and Y both equal 100 and both hold. Two identical slots (Unchanged + Exact 100)
        // each match {X, Y}; the SDR must give them DISTINCT representatives — taking
        // hits[0] as the rep would render the SAME field (X) on both slots.
        var ids = await SeedSeqAsync(ct,
            new[] { Obj(1, "C", ("X", "IntProperty", 0x10, IntHex(100)), ("Y", "IntProperty", 0x14, IntHex(100))) },
            new[] { Obj(1, "C", ("X", "IntProperty", 0x10, IntHex(100)), ("Y", "IntProperty", 0x14, IntHex(100))) });

        SpcGroupSlot UnchangedExact100() => Slot(
            CellAbs(SpcPredicateKind.Any,       SpcAbsoluteKind.Exact, 100),
            CellAbs(SpcPredicateKind.Unchanged, SpcAbsoluteKind.Exact, 100));
        var res = await _store.SpcGroupQueryAsync(Query(ids, UnchangedExact100(), UnchangedExact100()), ct);

        var c = Assert.Single(res.Candidates);
        var names = c.Slots.Select(s => s.FieldName).ToHashSet();
        Assert.Equal(2, names.Count);     // distinct reps, not "X" twice
        Assert.Contains("X", names);
        Assert.Contains("Y", names);
    }

    [Fact]
    public async Task Validation_TooFewSlots_And_TooFewSnapshots_And_ChainLength()
    {
        var ct = TestContext.Current.CancellationToken;
        var ids = await SeedSeqAsync(ct,
            new[] { Obj(1, "C", ("X", "IntProperty", 0, IntHex(1)), ("Y", "IntProperty", 4, IntHex(2))) },
            new[] { Obj(1, "C", ("X", "IntProperty", 0, IntHex(1)), ("Y", "IntProperty", 4, IntHex(2))) });

        // < 2 slots
        var r1 = await _store.SpcGroupQueryAsync(Query(ids,
            Slot(Cell(SpcPredicateKind.Any), Cell(SpcPredicateKind.Unchanged))), ct);
        Assert.NotNull(r1.Error);

        // < 2 snapshots
        var r2 = await _store.SpcGroupQueryAsync(Query(new[] { ids[0] },
            Slot(Cell(SpcPredicateKind.Any)), Slot(Cell(SpcPredicateKind.Any))), ct);
        Assert.NotNull(r2.Error);

        // chain length (1) != snapshot count (2)
        var r3 = await _store.SpcGroupQueryAsync(Query(ids,
            Slot(Cell(SpcPredicateKind.Any)),
            Slot(Cell(SpcPredicateKind.Any))), ct);
        Assert.NotNull(r3.Error);
    }
}
