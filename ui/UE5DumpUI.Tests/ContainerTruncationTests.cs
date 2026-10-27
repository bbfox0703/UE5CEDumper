using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UE5DumpUI.Core;
using UE5DumpUI.Models;
using UE5DumpUI.ViewModels;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// Finding <c>[CONTAINERCAP-2026-08-18]</c>: a container drill-down was capped at the Array Limit
/// (default 128) but the panel said nothing, so a 199-entry TSet drilled to a grid whose last row
/// was <c>[128]</c> looked identical to a complete 128-entry set. The DLL already sends BOTH the
/// true total (<c>set_count</c>/<c>map_count</c>/array <c>count</c>) and the capped element list, so
/// the fix is client-only: when the received element count is below the true total, badge the
/// breadcrumb + header and surface a status line pointing at the Array Limit control.
///
/// <para><b>Part A</b> tests the pure <see cref="ContainerTruncation"/> formatter. <b>Part B</b>
/// drives the real Live Walker container-drill command for each container kind (Array / Map / Set),
/// truncated and full, so the badge is proven to reach the breadcrumb, header, and status line —
/// the seam an isolated helper test cannot see.</para>
/// </summary>
public class ContainerTruncationTests
{
    // ── Part A: pure formatter ──────────────────────────────────────────────

    [Theory]
    [InlineData(128, 199, true)]   // capped: fewer shown than held
    [InlineData(1, 2, true)]       // minimal truncation
    [InlineData(199, 199, false)]  // complete read — no badge
    [InlineData(5, 3, false)]      // received > total (defensive) — never badge
    [InlineData(0, 199, false)]    // 0 received = read failure, not a cap; caller surfaces it
    [InlineData(0, 0, false)]      // empty container
    [InlineData(3, 0, false)]      // total unknown/zero
    public void IsTruncated_MatchesExpected(int received, int total, bool expected)
    {
        Assert.Equal(expected, ContainerTruncation.IsTruncated(received, total));
    }

    [Fact]
    public void BadgeSuffix_Truncated_ReadsShowingNofM()
    {
        Assert.Equal("  ⚠ showing 128 of 199", ContainerTruncation.BadgeSuffix(128, 199));
    }

    [Fact]
    public void BadgeSuffix_Full_IsEmpty()
    {
        Assert.Equal("", ContainerTruncation.BadgeSuffix(199, 199));
    }

    [Fact]
    public void BadgeSuffix_ReadFailureZeroReceived_IsEmpty()
    {
        // received == 0 is a read failure, not a cap; the badge must stay silent so it does not
        // read as "showing 0 of 199".
        Assert.Equal("", ContainerTruncation.BadgeSuffix(0, 199));
    }

    [Fact]
    public void BadgeSuffix_LargeTotals_UseGroupSeparators()
    {
        // The count formatting must match the GWorld actor-list disclosure (:N0), so a big
        // inventory reads "1,234" not "1234". Compare against the same format to stay
        // culture-independent.
        var suffix = ContainerTruncation.BadgeSuffix(128, 12345);
        Assert.Contains((128).ToString("N0"), suffix);
        Assert.Contains((12345).ToString("N0"), suffix);
    }

    [Fact]
    public void StatusLine_Truncated_PointsAtArrayLimitControl()
    {
        var line = ContainerTruncation.StatusLine(128, 199);
        Assert.Contains("Array Limit", line);
        Assert.Contains("128", line);
        Assert.Contains("199", line);
    }

    [Fact]
    public void StatusLine_Full_IsEmpty()
    {
        Assert.Equal("", ContainerTruncation.StatusLine(199, 199));
    }

    // ── Part B: real Live Walker drill, per container kind ───────────────────

    private static LiveWalkerViewModel MakeVm(LiveFieldValue field)
    {
        // A real drill always has a walked parent instance; give it one so the container crumb
        // carries a genuine address (the log helper FormatBreadcrumbTrace reads Address[^4..]).
        // Since [P4-CONTAINER-BASE] the drill also RE-READS that parent before opening the
        // container, so the stub answers that walk with the same row.
        var dump = new StubDumpService();
        dump.RegisterStruct("0x10000000", new InstanceWalkResult
        {
            Address = "0x10000000",
            Fields = new List<LiveFieldValue> { field },
        });
        var vm = new LiveWalkerViewModel(dump, new MockLoggingService(),
                                         new MockPlatformService(Path.GetTempPath()));
        vm.CurrentAddress = "0x10000000";
        return vm;
    }

    private static List<ContainerElementValue> ContainerElems(int n) =>
        Enumerable.Range(0, n)
            .Select(i => new ContainerElementValue { Index = i, Key = i.ToString(), Value = i.ToString() })
            .ToList();

    private static List<ArrayElementValue> PtrArrayElems(int n) =>
        Enumerable.Range(0, n)
            .Select(i => new ArrayElementValue { Index = i, PtrAddress = $"0x{1000 + i:X}", PtrName = $"Obj{i}" })
            .ToList();

    private static LiveFieldValue SetField(int total, int loaded) => new()
    {
        Name = "Set_Big",
        TypeName = "SetProperty",
        SetCount = total,
        SetElemType = "IntProperty",
        SetElemSize = 4,
        SetElements = ContainerElems(loaded),
    };

    private static LiveFieldValue MapField(int total, int loaded) => new()
    {
        Name = "Map_Big",
        TypeName = "MapProperty",
        MapCount = total,
        MapKeyType = "IntProperty",
        MapValueType = "IntProperty",
        MapKeySize = 4,
        MapValueSize = 4,
        MapElements = ContainerElems(loaded),
    };

    private static LiveFieldValue PtrArrayField(int total, int loaded) => new()
    {
        Name = "Actors",
        TypeName = "ArrayProperty",
        ArrayCount = total,
        ArrayInnerType = "ObjectProperty",   // pointer array => uses the capped inline preview
        ArrayElemSize = 8,
        ArrayElements = PtrArrayElems(loaded),
    };

    [Fact]
    public async Task Drill_TruncatedSet_BadgesBreadcrumbHeaderAndStatus()
    {
        var field = SetField(total: 199, loaded: 128);
        var vm = MakeVm(field);
        await vm.NavigateToContainerCommand.ExecuteAsync(field);

        Assert.Contains("showing 128 of 199", vm.Breadcrumbs[^1].Label);
        Assert.Contains("showing 128 of 199", vm.CurrentObjectName);
        Assert.Contains("Array Limit", vm.StatusText);
    }

    [Fact]
    public async Task Drill_FullSet_NoBadge()
    {
        var field = SetField(total: 3, loaded: 3);
        var vm = MakeVm(field);
        await vm.NavigateToContainerCommand.ExecuteAsync(field);

        Assert.DoesNotContain("showing", vm.Breadcrumbs[^1].Label);
        Assert.DoesNotContain("showing", vm.CurrentObjectName);
        Assert.Equal("", vm.StatusText);
    }

    [Fact]
    public async Task Drill_TruncatedMap_BadgesBreadcrumbHeaderAndStatus()
    {
        var field = MapField(total: 500, loaded: 128);
        var vm = MakeVm(field);
        await vm.NavigateToContainerCommand.ExecuteAsync(field);

        Assert.Contains("showing 128 of 500", vm.Breadcrumbs[^1].Label);
        Assert.Contains("showing 128 of 500", vm.CurrentObjectName);
        Assert.Contains("Array Limit", vm.StatusText);
    }

    [Fact]
    public async Task Drill_FullMap_NoBadge()
    {
        var field = MapField(total: 2, loaded: 2);
        var vm = MakeVm(field);
        await vm.NavigateToContainerCommand.ExecuteAsync(field);

        Assert.DoesNotContain("showing", vm.Breadcrumbs[^1].Label);
        Assert.DoesNotContain("showing", vm.CurrentObjectName);
        Assert.Equal("", vm.StatusText);
    }

    [Fact]
    public async Task Drill_TruncatedPointerArray_BadgesBreadcrumbHeaderAndStatus()
    {
        var field = PtrArrayField(total: 199, loaded: 128);
        var vm = MakeVm(field);
        await vm.NavigateToContainerCommand.ExecuteAsync(field);

        Assert.Contains("showing 128 of 199", vm.Breadcrumbs[^1].Label);
        Assert.Contains("showing 128 of 199", vm.CurrentObjectName);
        Assert.Contains("Array Limit", vm.StatusText);
    }

    // ---- [A3-CONTAINER-4096-ADVICE] a DLL-capped reply is not a slider-capped one ----
    //
    // The scalar re-fetch asks for the FULL count, and the DLL clamps every request (4,096 today). The status then
    // advised "raise the Array Limit slider", which cannot raise that cap. Derived from the reply, never a hardcoded 4096.

    private sealed class ClampingArrayStub : StubDumpService
    {
        public int Clamp { get; init; } = 4096;
        public override Task<ArrayElementsResult> ReadArrayElementsAsync(string addr, int fieldOffset, string innerAddr,
            string innerType, int elemSize, int offset = 0, int limit = 64, CancellationToken ct = default)
        {
            int n = Math.Min(limit, Clamp);
            return Task.FromResult(new ArrayElementsResult
            {
                TotalCount = limit, ReadCount = n, InnerType = innerType, ElemSize = elemSize,
                Elements = Enumerable.Range(0, n).Select(i => new ArrayElementValue { Index = i }).ToList(),
            });
        }
    }

    [Fact]
    public async Task Drill_ScalarArray_ClampedByTheDll_DoesNotBlameTheSlider()
    {
        var field = new LiveFieldValue
        {
            Name = "Samples", TypeName = "ArrayProperty", ArrayCount = 10_000,
            ArrayInnerType = "FloatProperty", ArrayElemSize = 4, ArrayInnerAddr = "0x7FF600001000",
        };
        var dump = new ClampingArrayStub();
        dump.RegisterStruct("0x10000000", new InstanceWalkResult
        {
            Address = "0x10000000", Fields = new List<LiveFieldValue> { field },
        });
        var vm = new LiveWalkerViewModel(dump, new MockLoggingService(), new MockPlatformService(Path.GetTempPath()));
        vm.CurrentAddress = "0x10000000";

        await vm.NavigateToContainerCommand.ExecuteAsync(field);

        Assert.Contains("showing 4,096 of 10,000", vm.Breadcrumbs[^1].Label);
        Assert.DoesNotContain("Array Limit", vm.StatusText);
        Assert.Contains("capped at 4,096 per fetch", vm.StatusText);
    }

    [Fact]
    public async Task Drill_PointerArray_OnLastRefreshValues_DoesNotClaimTheDllCap()
    {
        // [R7-D-07] The inline preview is only known to be walked at the slider's CURRENT value when the drill's re-read
        // succeeded. Here the re-read fails (nothing answers the parent walk) and the slider was raised to 1024 after
        // the 128-element preview was walked: 128 < min(1024, 199) is the SLIDER's old value, not the DLL's cap, so the
        // status must not say "capped at 128 per fetch" -- raising the slider (and refreshing) shows more.
        var field = PtrArrayField(total: 199, loaded: 128);
        var dump = new StubDumpService();   // no parent registered: the re-read cannot land
        var vm = new LiveWalkerViewModel(dump, new MockLoggingService(), new MockPlatformService(Path.GetTempPath()))
        {
            ArrayLimit = 1024,              // set before CurrentAddress, so it triggers no refresh
        };
        vm.CurrentAddress = "0x10000000";

        await vm.NavigateToContainerCommand.ExecuteAsync(field);

        Assert.Contains("Could not re-read", vm.StatusText);    // the precondition: last-refresh values
        Assert.DoesNotContain("per fetch", vm.StatusText);
        // [R7-S10] ...and not the slider either: which bound shortened a last-refresh preview is unknown (see below).
        // A re-open re-reads at the current value, and then the reply says which.
        Assert.DoesNotContain("Array Limit", vm.StatusText);
        Assert.Contains("re-open", vm.StatusText);
    }

    [Fact]
    public async Task Drill_PointerArray_OnLastRefreshValues_AtTheDllCap_DoesNotSendTheUserToTheSlider()
    {
        // [R7-S10] The mirror case R7-D-07 opened: 10,000 elements, the slider at 16384, the preview DLL-capped at 4,096,
        // and the re-read failed (plausibly a timeout -- a walk at 16384 is slow). `requested = elements.Count` made the
        // status say "raise the Array Limit slider", which cannot raise a per-fetch cap: [A3-CONTAINER-4096-ADVICE].
        var field = PtrArrayField(total: 10_000, loaded: 4_096);
        var dump = new StubDumpService();   // no parent registered: the re-read cannot land
        var vm = new LiveWalkerViewModel(dump, new MockLoggingService(), new MockPlatformService(Path.GetTempPath()))
        {
            ArrayLimit = 16384,
        };
        vm.CurrentAddress = "0x10000000";

        await vm.NavigateToContainerCommand.ExecuteAsync(field);

        Assert.Contains("Could not re-read", vm.StatusText);
        Assert.Contains("4,096 of 10,000", vm.StatusText);
        Assert.DoesNotContain("Array Limit", vm.StatusText);
    }

    [Fact]
    public async Task Drill_FullPointerArray_NoBadge()
    {
        var field = PtrArrayField(total: 4, loaded: 4);
        var vm = MakeVm(field);
        await vm.NavigateToContainerCommand.ExecuteAsync(field);

        Assert.DoesNotContain("showing", vm.Breadcrumbs[^1].Label);
        Assert.DoesNotContain("showing", vm.CurrentObjectName);
        Assert.Equal("", vm.StatusText);
    }

    // ---- [UE4-ASSETPTR-EXPORTS] a TArray<TAssetPtr> keeps the walk's paths ----
    //
    // The DLL fills a UE 4.11-4.17 asset-pointer array's inline elements with each path ([UE4-ASSETPTR-PROPS]);
    // read_array_elements is scalar-only and answers raw bytes for that inner. The drill took the scalar re-read for
    // any inner IsPointerOrStructArrayType did not name, so a capped preview was replaced by rows of hex.

    /// <summary>Answers an element read as read_array_elements does for an inner it cannot decode: raw bytes only.</summary>
    private sealed class RawElementReadStub : StubDumpService
    {
        public int Reads { get; private set; }
        public override Task<ArrayElementsResult> ReadArrayElementsAsync(string addr, int fieldOffset, string innerAddr,
            string innerType, int elemSize, int offset = 0, int limit = 64, CancellationToken ct = default)
        {
            Reads++;
            return Task.FromResult(new ArrayElementsResult
            {
                TotalCount = limit, ReadCount = limit, InnerType = innerType, ElemSize = elemSize,
                Elements = Enumerable.Range(0, limit)
                    .Select(i => new ArrayElementValue { Index = i, Hex = new string('0', elemSize * 2) }).ToList(),
            });
        }
    }

    [Theory]
    [InlineData("AssetObjectProperty")]
    [InlineData("AssetClassProperty")]
    public async Task Drill_UE4AssetPointerArray_ShowsTheWalksPaths_NotARawReRead(string innerType)
    {
        var field = new LiveFieldValue
        {
            Name = "ZonesToUse", TypeName = "ArrayProperty", ArrayCount = 200,
            ArrayInnerType = innerType, ArrayElemSize = 0x20, ArrayInnerAddr = "0x7FF600001000",
            ArrayElements = Enumerable.Range(0, 64)
                .Select(i => new ArrayElementValue { Index = i, Value = $"/Game/Maps/Zones/Zone_{i}.Zone_{i}" })
                .ToList(),
        };
        var dump = new RawElementReadStub();
        dump.RegisterStruct("0x10000000", new InstanceWalkResult
        {
            Address = "0x10000000", Fields = new List<LiveFieldValue> { field },
        });
        var vm = new LiveWalkerViewModel(dump, new MockLoggingService(), new MockPlatformService(Path.GetTempPath()));
        vm.CurrentAddress = "0x10000000";

        await vm.NavigateToContainerCommand.ExecuteAsync(field);

        Assert.Equal(0, dump.Reads);                                              // the walk's elements, as a pointer array's
        Assert.Equal(64, vm.Fields.Count);
        Assert.Equal("/Game/Maps/Zones/Zone_0.Zone_0", vm.Fields[0].TypedValue);
        Assert.Contains("showing 64 of 200", vm.Breadcrumbs[^1].Label);
    }

    // ---- [WALK-REFRESH-SCALAR-SHRINK] a Refresh inside a container view agrees with the drill ----
    //
    // Refresh's container branch re-walked the PARENT at the Array Limit and re-rendered the walk's inline preview: it
    // never re-fetched a scalar array the way the drill does, and it re-derived neither the status line nor the crumb.
    // Measured on DumperTest Shipping 5.4: Arr_Churn at 5,004 elements, drilled at Array Limit 16384, showed the first
    // 4,096 ("capped at 4,096 per fetch"); moving the slider to 64 re-rendered 64 rows and cleared the status line, and
    // the crumb still read "showing 4,096 of 5,004".

    /// <summary>Answers every walk with a FRESH row whose inline preview is cut where the DLL cuts it -- min(Array Limit,
    /// 4,096) -- and every element read at the same 4,096 per request. <see cref="Count"/> is the live element count.</summary>
    private sealed class RewalkStub : StubDumpService
    {
        public const string Parent = "0x10000000";
        public const int DllCap = 4096;
        public int Count { get; set; }
        public required Func<int, int, LiveFieldValue> Row { get; init; }   // (count, inline) -> a fresh row
        public int ElementReads { get; private set; }
        /// <summary>When set, every walk answers this instead: the parent object is gone.</summary>
        public InstanceWalkResult? Gone { get; set; }
        /// <summary>When set, the count the element READ sees: the array changed between the walk and the read.</summary>
        public int? ReadSees { get; set; }

        public override Task<InstanceWalkResult> WalkInstanceAsync(string addr, string? classAddr = null,
            int arrayLimit = 64, int previewLimit = 2, bool fillGaps = false, bool lean = false,
            CancellationToken ct = default)
            => Task.FromResult(Gone ?? new InstanceWalkResult
            {
                Address = Parent,
                Fields = new List<LiveFieldValue> { Row(Count, Math.Min(Count, Math.Min(arrayLimit, DllCap))) },
            });

        public override Task<ArrayElementsResult> ReadArrayElementsAsync(string addr, int fieldOffset, string innerAddr,
            string innerType, int elemSize, int offset = 0, int limit = 64, CancellationToken ct = default)
        {
            ElementReads++;
            int live = ReadSees ?? Count;
            int n = Math.Max(0, Math.Min(Math.Min(limit, DllCap), live - offset));
            return Task.FromResult(new ArrayElementsResult
            {
                TotalCount = live, ReadCount = n, InnerType = innerType, ElemSize = elemSize,
                Elements = Enumerable.Range(offset, n).Select(i => new ArrayElementValue { Index = i, Value = $"{i}" }).ToList(),
            });
        }
    }

    private static LiveFieldValue ScalarRow(int count, int inline) => new()
    {
        Name = "Arr_Churn", TypeName = "ArrayProperty", Offset = 0x40, Size = 16,
        ArrayCount = count, ArrayInnerType = "IntProperty", ArrayElemSize = 4,
        ArrayInnerAddr = "0x7FF600001000", ArrayDataAddr = "0x20000000",
        ArrayElements = Enumerable.Range(0, inline).Select(i => new ArrayElementValue { Index = i, Value = $"{i}" }).ToList(),
    };

    private static async Task<(LiveWalkerViewModel Vm, RewalkStub Dump)> DrillLiveAsync(
        Func<int, int, LiveFieldValue> row, int count, int arrayLimit)
    {
        var dump = new RewalkStub { Row = row, Count = count };
        var vm = new LiveWalkerViewModel(dump, new MockLoggingService(), new MockPlatformService(Path.GetTempPath()))
        {
            ArrayLimit = arrayLimit,          // set before CurrentAddress, so it triggers no refresh
        };
        vm.CurrentAddress = RewalkStub.Parent;
        var parentRow = (await dump.WalkInstanceAsync(RewalkStub.Parent, arrayLimit: arrayLimit)).Fields[0];
        await vm.NavigateToContainerCommand.ExecuteAsync(parentRow);
        return (vm, dump);
    }

    /// <summary>The toolbar slider: its setter runs Refresh itself.</summary>
    private static async Task MoveArrayLimitAsync(LiveWalkerViewModel vm, int value)
    {
        vm.ArrayLimit = value;
        if (vm.RefreshCommand.ExecutionTask is { } running) await running;
    }

    [Fact]
    public async Task Refresh_ScalarArrayOverTheArrayLimit_KeepsTheDrillsRows_StatusAndCrumb()
    {
        var (vm, _) = await DrillLiveAsync(ScalarRow, count: 5_004, arrayLimit: 16384);
        Assert.Equal(4_096, vm.Fields.Count);                                // the precondition: the drill's view
        Assert.Contains("capped at 4,096 per fetch", vm.StatusText);

        await MoveArrayLimitAsync(vm, 64);                                   // the measured path

        Assert.Equal(4_096, vm.Fields.Count);
        Assert.Contains("showing 4,096 of 5,004", vm.CurrentObjectName);
        Assert.Contains("showing 4,096 of 5,004", vm.Breadcrumbs[^1].Label);
        Assert.Contains("capped at 4,096 per fetch", vm.StatusText);
        Assert.DoesNotContain("Array Limit", vm.StatusText);

        await vm.RefreshCommand.ExecuteAsync(null);                          // ...and a Refresh at an unchanged limit

        Assert.Equal(4_096, vm.Fields.Count);
        Assert.Contains("capped at 4,096 per fetch", vm.StatusText);
    }

    [Fact]
    public async Task Refresh_ScalarArrayBelowTheDllCap_ShowsEveryElement_AsTheDrillDid()
    {
        // 200 elements at Array Limit 64: the drill re-fetches all 200, and an element edit at [N >= 64] refreshes.
        // Rendering the 64-element preview made the edited row vanish right after "Written: ...".
        var (vm, dump) = await DrillLiveAsync(ScalarRow, count: 200, arrayLimit: 64);
        Assert.Equal(200, vm.Fields.Count);

        await vm.RefreshCommand.ExecuteAsync(null);

        Assert.Equal(200, vm.Fields.Count);
        Assert.DoesNotContain("showing", vm.CurrentObjectName);
        Assert.Equal("", vm.StatusText);
        Assert.Equal(2, dump.ElementReads);                                  // the drill's read, and the refresh's
    }

    [Fact]
    public async Task Refresh_ScalarArrayGrew_TheCrumbTakesTheNewCount_AndTheNewReading()
    {
        var (vm, dump) = await DrillLiveAsync(ScalarRow, count: 5_004, arrayLimit: 16384);
        dump.Count = 5_010;

        await vm.RefreshCommand.ExecuteAsync(null);

        var crumb = vm.Breadcrumbs[^1];
        Assert.Contains("showing 4,096 of 5,010", vm.CurrentObjectName);
        Assert.Contains("showing 4,096 of 5,010", crumb.Label);
        Assert.Contains("[5010 x IntProperty]", crumb.Label);
        // The crumb's ContainerField is what Back re-renders and what the exporters read: it must be THIS reading.
        Assert.Equal(5_010, crumb.ContainerField!.ArrayCount);
        Assert.Equal(4_096, crumb.ContainerField.ArrayElements!.Count);
    }

    [Fact]
    public async Task Refresh_TruncatedPointerArray_KeepsItsStatusLine_AndTheCrumbFollowsTheSlider()
    {
        var (vm, _) = await DrillLiveAsync(PtrArrayField, count: 200, arrayLimit: 128);
        Assert.Contains("showing 128 of 200", vm.Breadcrumbs[^1].Label);
        Assert.Contains("Array Limit", vm.StatusText);

        await vm.RefreshCommand.ExecuteAsync(null);

        Assert.Equal(128, vm.Fields.Count);
        Assert.Contains("Array Limit", vm.StatusText);

        // The slider DOES govern a pointer array (no per-element re-fetch can resolve names), so the rows follow it --
        // and the crumb must say what the header says.
        await MoveArrayLimitAsync(vm, 64);

        Assert.Equal(64, vm.Fields.Count);
        Assert.Contains("showing 64 of 200", vm.CurrentObjectName);
        Assert.Contains("showing 64 of 200", vm.Breadcrumbs[^1].Label);
        Assert.Contains("Array Limit", vm.StatusText);
    }

    [Fact]
    public async Task Refresh_TruncatedSet_KeepsItsStatusLine_AndItsBadge()
    {
        var (vm, dump) = await DrillLiveAsync(SetField, count: 199, arrayLimit: 128);
        Assert.Contains("Array Limit", vm.StatusText);
        dump.Count = 205;

        await vm.RefreshCommand.ExecuteAsync(null);

        Assert.Contains("showing 128 of 205", vm.CurrentObjectName);
        Assert.Contains("showing 128 of 205", vm.Breadcrumbs[^1].Label);
        Assert.Contains("Array Limit", vm.StatusText);
    }

    // [WALK-REFRESH-SCALAR-SHRINK] review F2: the element read reports the array's count AT READ TIME (TotalCount), and
    // the view took its total and its "asked for" from the WALK's count instead. An array that shrank between the two
    // reads came back complete but short of the walk's count, and the view said "capped at N per fetch" over it.

    [Fact]
    public async Task Drill_ScalarArrayThatShrankBeforeTheRead_IsShownWhole_NotCalledCapped()
    {
        var dump = new RewalkStub { Row = ScalarRow, Count = 5_004, ReadSees = 300 };
        var vm = new LiveWalkerViewModel(dump, new MockLoggingService(), new MockPlatformService(Path.GetTempPath()))
        {
            ArrayLimit = 64,
        };
        vm.CurrentAddress = RewalkStub.Parent;
        var parentRow = (await dump.WalkInstanceAsync(RewalkStub.Parent, arrayLimit: 64,
                                                      ct: TestContext.Current.CancellationToken)).Fields[0];

        await vm.NavigateToContainerCommand.ExecuteAsync(parentRow);

        Assert.Equal(300, vm.Fields.Count);
        Assert.DoesNotContain("per fetch", vm.StatusText);
        Assert.Contains("[300 x IntProperty]", vm.Breadcrumbs[^1].Label);
        Assert.DoesNotContain("showing", vm.Breadcrumbs[^1].Label);
        Assert.DoesNotContain("showing", vm.CurrentObjectName);
    }

    [Fact]
    public async Task Refresh_ScalarArrayThatShrankBeforeTheRead_IsShownWhole_NotCalledCapped()
    {
        var (vm, dump) = await DrillLiveAsync(ScalarRow, count: 5_004, arrayLimit: 64);
        dump.ReadSees = 300;                                                 // the walk still says 5,004

        await vm.RefreshCommand.ExecuteAsync(null);

        Assert.Equal(300, vm.Fields.Count);
        Assert.DoesNotContain("per fetch", vm.StatusText);
        Assert.DoesNotContain("showing", vm.CurrentObjectName);
        Assert.Contains("[300 x IntProperty]", vm.Breadcrumbs[^1].Label);
        Assert.DoesNotContain("showing", vm.Breadcrumbs[^1].Label);
        Assert.Equal(300, vm.Breadcrumbs[^1].ContainerField!.ArrayCount);   // what Back and the exporters read
    }

    [Fact]
    public async Task Refresh_ScalarArrayThatShrankButIsStillOverTheDllCap_IsCappedAtTheLiveCount()
    {
        // Fewer back than both the request and the live count: that IS the DLL's per-fetch cap, of the LIVE total.
        var (vm, dump) = await DrillLiveAsync(ScalarRow, count: 10_000, arrayLimit: 64);
        dump.ReadSees = 5_000;

        await vm.RefreshCommand.ExecuteAsync(null);

        Assert.Equal(4_096, vm.Fields.Count);
        Assert.Contains("showing 4,096 of 5,000", vm.CurrentObjectName);
        Assert.Contains("showing 4,096 of 5,000", vm.Breadcrumbs[^1].Label);
        Assert.Contains("capped at 4,096 per fetch", vm.StatusText);
    }

    [Fact]
    public async Task Refresh_ScalarArrayThatGrewBeforeTheRead_SaysSo_WithoutBlamingACapOrTheSlider()
    {
        // The read asked for the walk's 200 and the array now holds 210: neither the DLL's cap nor the Array Limit
        // (which does not govern a scalar view) bounded it, the walk's count did -- a re-read asks for the new one.
        var (vm, dump) = await DrillLiveAsync(ScalarRow, count: 200, arrayLimit: 64);
        dump.ReadSees = 210;

        await vm.RefreshCommand.ExecuteAsync(null);

        Assert.Equal(200, vm.Fields.Count);
        Assert.Contains("showing 200 of 210", vm.Breadcrumbs[^1].Label);
        Assert.DoesNotContain("per fetch", vm.StatusText);
        Assert.DoesNotContain("Array Limit", vm.StatusText);
        Assert.Contains("re-open", vm.StatusText);
    }

    // [LW-EMPTIED-CONTAINER-REFRESH] RepopulateContainerView, and Refresh's re-derivation of the crumb, keyed the
    // container kind on a count above zero: a container emptied since the drill matched no kind, so a Refresh left the
    // old rows on screen (editable, at addresses the container no longer owns) under the old label.

    [Theory]
    [InlineData("array", "[0 x IntProperty]")]
    [InlineData("set", "{Set: 0, IntProperty}")]
    [InlineData("map", "{Map: 0, IntProperty")]
    public async Task Refresh_OfAContainerEmptiedSinceTheDrill_ShowsItEmpty(string kind, string label)
    {
        Func<int, int, LiveFieldValue> row = kind switch
        {
            "array" => ScalarRow,
            "set" => SetField,
            _ => MapField,
        };
        var (vm, dump) = await DrillLiveAsync(row, count: 200, arrayLimit: 64);
        Assert.NotEmpty(vm.Fields);                                          // the precondition: the drill's rows
        dump.Count = 0;

        await vm.RefreshCommand.ExecuteAsync(null);

        Assert.Empty(vm.Fields);
        Assert.Contains(label, vm.Breadcrumbs[^1].Label);
        Assert.DoesNotContain("showing", vm.Breadcrumbs[^1].Label);
        Assert.DoesNotContain("showing", vm.CurrentObjectName);
        Assert.Equal("", vm.StatusText);                                     // empty is complete, not unreadable
    }

    // [WALK-NULLCLASS-SILENT] review F1: the container branch acted only when the parent walk found the field again. A
    // parent that is gone (unreadable, no UObject header, stale) has no fields, so Refresh returned SILENTLY: a blank
    // status over the old element rows, still editable, each FieldAddress pointing into memory the game had freed.

    [Theory]
    [InlineData("unreadable", "no longer readable")]
    [InlineData("no_header", "No UObject header")]
    [InlineData("stale", "freed/recycled")]
    public async Task Refresh_WhenTheParentIsGone_SaysSo_AndLeavesNoRowsIntoItsMemory(string verdict, string says)
    {
        var (vm, dump) = await DrillLiveAsync(ScalarRow, count: 200, arrayLimit: 64);
        Assert.Equal(200, vm.Fields.Count);                                  // the precondition: the drill's view
        dump.Gone = new InstanceWalkResult
        {
            Address = RewalkStub.Parent,
            IsUnreadable = verdict == "unreadable", IsNoHeader = verdict == "no_header", IsStale = verdict == "stale",
        };

        await vm.RefreshCommand.ExecuteAsync(null);

        Assert.Contains(says, vm.StatusText);                                // what UpdateDisplay says for the verdict
        Assert.Empty(vm.Fields);
    }
}
