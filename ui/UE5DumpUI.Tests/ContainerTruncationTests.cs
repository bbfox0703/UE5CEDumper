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

    private static LiveWalkerViewModel MakeVm()
    {
        var vm = new LiveWalkerViewModel(new StubDumpService(), new MockLoggingService(),
                                         new MockPlatformService(Path.GetTempPath()));
        // A real drill always has a walked parent instance; give it one so the container crumb
        // carries a genuine address (the log helper FormatBreadcrumbTrace reads Address[^4..]).
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
        var vm = MakeVm();
        await vm.NavigateToContainerCommand.ExecuteAsync(SetField(total: 199, loaded: 128));

        Assert.Contains("showing 128 of 199", vm.Breadcrumbs[^1].Label);
        Assert.Contains("showing 128 of 199", vm.CurrentObjectName);
        Assert.Contains("Array Limit", vm.StatusText);
    }

    [Fact]
    public async Task Drill_FullSet_NoBadge()
    {
        var vm = MakeVm();
        await vm.NavigateToContainerCommand.ExecuteAsync(SetField(total: 3, loaded: 3));

        Assert.DoesNotContain("showing", vm.Breadcrumbs[^1].Label);
        Assert.DoesNotContain("showing", vm.CurrentObjectName);
        Assert.Equal("", vm.StatusText);
    }

    [Fact]
    public async Task Drill_TruncatedMap_BadgesBreadcrumbHeaderAndStatus()
    {
        var vm = MakeVm();
        await vm.NavigateToContainerCommand.ExecuteAsync(MapField(total: 500, loaded: 128));

        Assert.Contains("showing 128 of 500", vm.Breadcrumbs[^1].Label);
        Assert.Contains("showing 128 of 500", vm.CurrentObjectName);
        Assert.Contains("Array Limit", vm.StatusText);
    }

    [Fact]
    public async Task Drill_FullMap_NoBadge()
    {
        var vm = MakeVm();
        await vm.NavigateToContainerCommand.ExecuteAsync(MapField(total: 2, loaded: 2));

        Assert.DoesNotContain("showing", vm.Breadcrumbs[^1].Label);
        Assert.DoesNotContain("showing", vm.CurrentObjectName);
        Assert.Equal("", vm.StatusText);
    }

    [Fact]
    public async Task Drill_TruncatedPointerArray_BadgesBreadcrumbHeaderAndStatus()
    {
        var vm = MakeVm();
        await vm.NavigateToContainerCommand.ExecuteAsync(PtrArrayField(total: 199, loaded: 128));

        Assert.Contains("showing 128 of 199", vm.Breadcrumbs[^1].Label);
        Assert.Contains("showing 128 of 199", vm.CurrentObjectName);
        Assert.Contains("Array Limit", vm.StatusText);
    }

    [Fact]
    public async Task Drill_FullPointerArray_NoBadge()
    {
        var vm = MakeVm();
        await vm.NavigateToContainerCommand.ExecuteAsync(PtrArrayField(total: 4, loaded: 4));

        Assert.DoesNotContain("showing", vm.Breadcrumbs[^1].Label);
        Assert.DoesNotContain("showing", vm.CurrentObjectName);
        Assert.Equal("", vm.StatusText);
    }
}
