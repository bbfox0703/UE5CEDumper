using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UE5DumpUI.Models;
using UE5DumpUI.ViewModels;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// Live Walker field-search highlighting — audit #5 U1/V6.
///
/// The defect was not that the matcher was wrong; it was that a Refresh installed a
/// brand-new set of <see cref="LiveFieldValue"/> rows (whose <c>IsSearchMatch</c>
/// defaults to false) and never re-ran the matcher, so highlights vanished while
/// <c>SearchMatchCount</c> and the ↑/↓ stepper kept advertising the previous walk's
/// count. These tests pin the extracted matcher, including the property that makes
/// the refresh path correct: marking a DIFFERENT list than the one on screen.
/// </summary>
public class LiveWalkerSearchHighlightTests
{
    // DisplayValue is derived, not settable — TypedValue is the input it falls through to.
    private static List<LiveFieldValue> Rows() => new()
    {
        new LiveFieldValue { Name = "Health",    TypeName = "FloatProperty",  TypedValue = "37" },
        new LiveFieldValue { Name = "MaxHealth", TypeName = "FloatProperty",  TypedValue = "100" },
        new LiveFieldValue { Name = "Mana",      TypeName = "FloatProperty",  TypedValue = "12" },
        new LiveFieldValue { Name = "OwnerPawn", TypeName = "ObjectProperty",
                             PtrName = "BP_Player", PtrClassName = "BP_Player_C" },
    };

    [Fact]
    public void MarksMatchingRowsAndReturnsTheCount()
    {
        var rows = Rows();
        int n = LiveWalkerViewModel.MarkSearchMatches(rows, "health");

        Assert.Equal(2, n);
        Assert.True(rows[0].IsSearchMatch);
        Assert.True(rows[1].IsSearchMatch);
        Assert.False(rows[2].IsSearchMatch);
        Assert.False(rows[3].IsSearchMatch);
    }

    [Fact]
    public void SpaceSeparatedTermsAreAnded()
    {
        var rows = Rows();
        // "max health" must hit only MaxHealth — an OR would also take Health.
        Assert.Equal(1, LiveWalkerViewModel.MarkSearchMatches(rows, "max health"));
        Assert.True(rows[1].IsSearchMatch);
        Assert.False(rows[0].IsSearchMatch);
    }

    [Fact]
    public void MatchesAcrossFieldsNotJustName()
    {
        var rows = Rows();
        // Field-level OR: the term may land on the type, the value or the ptr class.
        Assert.Equal(1, LiveWalkerViewModel.MarkSearchMatches(rows, "BP_Player_C"));
        Assert.True(rows[3].IsSearchMatch);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("h")]      // below the 2-char floor — matches too broadly to be useful
    public void InactiveQueryClearsEveryFlagAndReturnsZero(string query)
    {
        var rows = Rows();
        foreach (var r in rows) r.IsSearchMatch = true;   // pretend a previous query matched

        Assert.Equal(0, LiveWalkerViewModel.MarkSearchMatches(rows, query));
        Assert.All(rows, r => Assert.False(r.IsSearchMatch));
    }

    [Fact]
    public void NonMatchingRowsAreClearedNotLeftStale()
    {
        var rows = Rows();
        LiveWalkerViewModel.MarkSearchMatches(rows, "health");
        Assert.True(rows[0].IsSearchMatch);

        // A second, disjoint query must not leave the first query's highlights behind.
        Assert.Equal(1, LiveWalkerViewModel.MarkSearchMatches(rows, "mana"));
        Assert.False(rows[0].IsSearchMatch);
        Assert.True(rows[2].IsSearchMatch);
    }

    /// <summary>
    /// The V6 property itself: a refresh hands the matcher the NEW rows, before they are
    /// installed into the bound collection. Marking a list the caller supplies — rather
    /// than an internal one — is what makes that possible, so pin it: the freshly walked
    /// rows come back marked, and the count matches, with the on-screen list untouched.
    /// </summary>
    [Fact]
    public void MarksTheSuppliedListSoARefreshCanMarkRowsBeforeInstallingThem()
    {
        var onScreen = Rows();
        LiveWalkerViewModel.MarkSearchMatches(onScreen, "health");

        var refreshed = Rows();   // a fresh walk: IsSearchMatch defaults to false
        Assert.All(refreshed, r => Assert.False(r.IsSearchMatch));

        int n = LiveWalkerViewModel.MarkSearchMatches(refreshed, "health");

        Assert.Equal(2, n);
        Assert.Equal(2, refreshed.Count(r => r.IsSearchMatch));
        // The old list is a separate object graph and was not disturbed.
        Assert.Equal(2, onScreen.Count(r => r.IsSearchMatch));
    }

    [Fact]
    public void EmptyFieldListIsZeroNotACrash()
    {
        Assert.Equal(0, LiveWalkerViewModel.MarkSearchMatches(new List<LiveFieldValue>(), "health"));
    }

    // ── The WIRING, which is what V6 actually broke ────────────────────────────
    //
    // The tests above pin the matcher; the defect was that Refresh never called it.
    // These drive the real RefreshCommand through a stub pipe, so reverting the
    // MarkSearchMatches call in UpdateDisplay turns them red.

    private static LiveWalkerViewModel VmWithWalk(string addr, params LiveFieldValue[] fields)
    {
        var dump = new StubDumpService();
        dump.RegisterStruct(addr, new InstanceWalkResult
        {
            Address = addr,
            Name = "PlayerState",
            ClassName = "BP_PlayerState_C",
            Fields = fields.ToList(),
        });
        var vm = new LiveWalkerViewModel(dump, new MockLoggingService(),
                                         new MockPlatformService(Path.GetTempPath()));
        vm.CurrentAddress = addr;
        return vm;
    }

    [Fact]
    public async Task Refresh_KeepsTheKeywordAndReMarksTheFreshRows()
    {
        var vm = VmWithWalk("0x2000", Rows().ToArray());
        vm.SearchText = "health";

        await vm.RefreshCommand.ExecuteAsync(null);

        // The keyword survives a refresh by design (clearFieldSearch: false)...
        Assert.Equal("health", vm.SearchText);
        // ...and so must the highlights it stands for. Before the fix these rows were
        // brand-new objects with IsSearchMatch == false while the count stayed at 2.
        Assert.Equal(2, vm.SearchMatchCount);
        Assert.True(vm.HasSearchResults);
        Assert.Equal(2, vm.Fields.Count(f => f.IsSearchMatch));
        Assert.Equal(vm.SearchMatchCount, vm.Fields.Count(f => f.IsSearchMatch));
    }

    [Fact]
    public async Task Refresh_WithNoKeyword_LeavesNothingHighlightedAndZeroCount()
    {
        var vm = VmWithWalk("0x2000", Rows().ToArray());

        await vm.RefreshCommand.ExecuteAsync(null);

        Assert.Equal(0, vm.SearchMatchCount);
        Assert.False(vm.HasSearchResults);
        Assert.DoesNotContain(vm.Fields, f => f.IsSearchMatch);
    }

    /// <summary>
    /// The count and the highlights are the two halves that drifted apart, so pin them
    /// against a refresh whose data CHANGED: a field that no longer matches must drop
    /// out of both at once.
    /// </summary>
    [Fact]
    public async Task Refresh_RecountsWhenTheRefreshedDataNoLongerMatches()
    {
        var vm = VmWithWalk("0x2000",
            new LiveFieldValue { Name = "Health",    TypeName = "FloatProperty", TypedValue = "37" },
            new LiveFieldValue { Name = "MaxHealth", TypeName = "FloatProperty", TypedValue = "100" });
        vm.SearchText = "37";
        await vm.RefreshCommand.ExecuteAsync(null);
        Assert.Equal(1, vm.SearchMatchCount);

        // Same object, new values — 37 is gone, so the match count must fall to 0
        // rather than keep advertising the previous walk's hit.
        var dump = new StubDumpService();
        dump.RegisterStruct("0x2000", new InstanceWalkResult
        {
            Address = "0x2000",
            Fields = new List<LiveFieldValue>
            {
                new LiveFieldValue { Name = "Health",    TypeName = "FloatProperty", TypedValue = "42" },
                new LiveFieldValue { Name = "MaxHealth", TypeName = "FloatProperty", TypedValue = "100" },
            },
        });
        var vm2 = new LiveWalkerViewModel(dump, new MockLoggingService(),
                                          new MockPlatformService(Path.GetTempPath()));
        vm2.CurrentAddress = "0x2000";
        vm2.SearchText = "37";
        await vm2.RefreshCommand.ExecuteAsync(null);

        Assert.Equal(0, vm2.SearchMatchCount);
        Assert.False(vm2.HasSearchResults);
        Assert.DoesNotContain(vm2.Fields, f => f.IsSearchMatch);
    }
}
