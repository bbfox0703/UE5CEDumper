using System.Collections.Generic;
using System.IO;
using UE5DumpUI.Core;
using UE5DumpUI.Models;
using UE5DumpUI.ViewModels;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// TMap element geometry on the C# side.
///
/// <para><b>Stride</b>: the TSparseArray stride is
/// <c>Align(Align(elemSize, alignof(T)) + 8, alignof(T))</c>, and the alignment is an engine fact the
/// C# side cannot know — so the DLL publishes the stride it used and the C# consumes it, rather than
/// re-deriving it as <c>AlignUp(elemSize,4)+8</c>, which goes silently stale.</para>
///
/// <para><b>Value address</b>: a map element row is built with the VALUE's type, so it must get the
/// value's address, not the element BASE address — which is the key, since a TPair stores the key
/// first. The inline editor writes to that address, so editing a <c>TMap&lt;FName,int32&gt;</c> value
/// at the base would write over the FName key.</para>
///
/// These tests FAIL if either rule is broken — the fallback and the correct value are deliberately
/// different numbers, and the key address is asserted to be NOT the answer.
/// </summary>
public class ContainerGeometryTests
{
    // TMap<AActor*, float>: key 8 (8-aligned), value 4 => pair 12, pairAlign 8.
    // Engine strides Align(Align(12,8)+8,8) = 24. The 4-aligned fallback gives (12+3&~3)+8 = 20.
    private const int PairSize = 12;
    private const int TruePairStride = 24;
    private const int StalePairStride = 20;

    private static LiveFieldValue PointerKeyedMap(int dllStride) => new()
    {
        Name = "DamageMultipliers",
        TypeName = "MapProperty",
        MapCount = 3,
        MapKeyType = "ObjectProperty",
        MapValueType = "FloatProperty",
        MapKeySize = 8,
        MapValueSize = 4,
        MapValueOffset = 8,
        MapStride = dllStride,
        MapDataAddr = "0x2000",
    };

    // --- The stride must come from the DLL, not from a client-side guess ---

    [Fact]
    public void MapStrideOf_PrefersTheStrideTheDllActuallyUsed()
    {
        // The local formula and the DLL's stride differ by 4, so taking the local one fails here.
        Assert.Equal(StalePairStride, ContainerGeometry.FallbackStride(PairSize));
        Assert.Equal(TruePairStride, ContainerGeometry.MapStrideOf(PointerKeyedMap(TruePairStride)));
    }

    [Fact]
    public void MapStrideOf_FallsBackOnlyWhenTheDllSuppliedNothing()
    {
        // A field with no wire stride (older DLL / offline-reconstructed) still gets a best effort.
        Assert.Equal(StalePairStride, ContainerGeometry.MapStrideOf(PointerKeyedMap(0)));
    }

    [Fact]
    public void SetStrideOf_IsUnchangedByTheFix()
    {
        // TSet is the case the fallback formula gets right: a bare elemSize is already a multiple of
        // alignof(T), so the DLL's elemAlign default of 4 reproduces the fallback exactly.
        var set = new LiveFieldValue { SetCount = 2, SetElemType = "IntProperty", SetElemSize = 4 };
        Assert.Equal(ContainerGeometry.FallbackStride(4), ContainerGeometry.SetStrideOf(set));
    }

    // --- The value address, not the element base ---

    [Fact]
    public void MapValueAddress_TargetsTheValue_NotTheKey()
    {
        var map = PointerKeyedMap(TruePairStride);

        // element 1 = base + 1*24, value sits +8 into the pair
        ulong valueAddr = ContainerGeometry.MapValueAddress(map, 0x2000, 1);
        ulong keyAddr = ContainerGeometry.MapKeyAddress(map, 0x2000, 1);

        Assert.Equal(0x2000UL + 24 + 8, valueAddr);
        Assert.Equal(0x2000UL + 24, keyAddr);
        Assert.NotEqual(keyAddr, valueAddr);   // the whole point: the value is not the key
    }

    [Theory]
    [InlineData(0UL, 0)]   // no data base
    public void MapValueAddress_ReturnsZeroWhenGeometryIsUnusable(ulong dataBase, int index)
    {
        Assert.Equal(0UL, ContainerGeometry.MapValueAddress(PointerKeyedMap(TruePairStride), dataBase, index));
    }

    // --- The value address at the SEAM: the row the panel actually builds ---
    //
    // working-lessons 1.3: helpers that are individually correct prove nothing about the caller.
    // This drives the real populate path and inspects the row.

    private static LiveWalkerViewModel NewVm() =>
        new(new StubDumpService(), new MockLoggingService(), new MockPlatformService(Path.GetTempPath()));

    [Fact]
    public void PopulateMapContainerFields_RowAddressIsTheValue_SoAnInlineEditCannotHitTheKey()
    {
        var vm = NewVm();
        var map = PointerKeyedMap(TruePairStride);
        var elements = new List<ContainerElementValue>
        {
            new() { Index = 0, Key = "Actor_A", Value = "1.5" },
            new() { Index = 1, Key = "Actor_B", Value = "2.5" },
        };

        vm.PopulateMapContainerFields(elements, map);

        Assert.Equal(2, vm.Fields.Count);
        var row1 = vm.Fields[1];

        // The row describes the VALUE: type, address and size must all agree on that.
        Assert.Equal("FloatProperty", row1.TypeName);
        Assert.Equal($"0x{0x2000UL + 24 + 8:X}", row1.FieldAddress);
        Assert.Equal(4, row1.Size);

        // ...and must NOT be the element base, which is where the key lives.
        Assert.NotEqual($"0x{0x2000UL + 24:X}", row1.FieldAddress);

        // The row is editable, which is exactly why the address has to be right.
        Assert.True(row1.IsEditable);
    }

    [Fact]
    public void PopulateMapContainerFields_UsesTheDllStride_NotTheLocalFormula()
    {
        var vm = NewVm();
        vm.PopulateMapContainerFields(
            new List<ContainerElementValue> { new() { Index = 2, Key = "Actor_C", Value = "3.5" } },
            PointerKeyedMap(TruePairStride));

        // With the stale stride of 20 this would be 0x2000 + 40 + 8 = 0x2030.
        Assert.Equal($"0x{0x2000UL + 2 * 24 + 8:X}", vm.Fields[0].FieldAddress);
    }

    // --- The geometry must survive the multi-select filter ---

    [Fact]
    public void FilterContainerToElement_PreservesMapGeometry()
    {
        var map = PointerKeyedMap(TruePairStride);
        var full = new LiveFieldValue
        {
            Name = map.Name,
            TypeName = map.TypeName,
            MapCount = 3,
            MapKeyType = map.MapKeyType,
            MapValueType = map.MapValueType,
            MapKeySize = map.MapKeySize,
            MapValueSize = map.MapValueSize,
            MapValueOffset = map.MapValueOffset,
            MapStride = map.MapStride,
            MapDataAddr = map.MapDataAddr,
            MapElements = new List<ContainerElementValue>
            {
                new() { Index = 0, Key = "A", Value = "1" },
                new() { Index = 2, Key = "C", Value = "3" },
            },
        };

        var selected = new List<LiveFieldValue> { new() { Name = "[2] C" } };
        var filtered = LiveWalkerViewModel.FilterContainerToElement(full, selected);

        // Dropping either of these silently changes the exported layout.
        Assert.Equal(full.MapValueOffset, filtered.MapValueOffset);
        Assert.Equal(full.MapStride, filtered.MapStride);
        Assert.Equal(TruePairStride, ContainerGeometry.MapStrideOf(filtered));
        Assert.Single(filtered.MapElements!);
    }
}
