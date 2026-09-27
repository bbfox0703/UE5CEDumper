using UE5DumpUI.Models;
using UE5DumpUI.ViewModels;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// [LW-SEARCH-CLEAR-KEEP] Clearing the Live Walker field-search keyword re-sets the grid's items
/// (ApplySearch swaps the collection so the highlight styles re-evaluate), which put the grid back
/// on its first row and dropped the selection: find a field at 0x1138 with ▲/▼, clear the keyword
/// to look for something else, and scroll all the way back down by hand. The maintainer's rule:
/// ONLY when the keyword goes from 2+ characters to empty in one edit (select all, Delete) —
/// <list type="bullet">
/// <item>the selection contains the match ▲/▼ landed on → the view stays where it is;</item>
/// <item>it does not → the first selected row (grid order) comes to the top.</item>
/// </list>
/// With nothing selected the view stays where it is too (the maintainer's follow-up). Typing,
/// pasting, shortening, a partial delete, and 1 → 0 characters keep today's behaviour.
/// The VM hands the View a restore (selection, top row, row to keep visible); these pin what it
/// hands over — the scrolling itself is the View's, proven live.
/// </summary>
public class LiveWalkerSearchClearKeepsViewTests
{
    private static LiveFieldValue Row(string name, int offset)
        => new() { Name = name, Offset = offset, TypeName = "IntProperty", TypedValue = "1" };

    private static LiveWalkerViewModel VmWithRows()
    {
        var vm = new LiveWalkerViewModel(new StubDumpService(), new MockLoggingService(),
                                         new MockPlatformService(Path.GetTempPath()));
        foreach (var r in new[] { Row("Alpha", 0x10), Row("I32", 0x630), Row("Beta", 0x700),
                                  Row("I32_Arr", 0x800), Row("Gamma", 0x1138) })
            vm.Fields.Add(r);
        return vm;
    }

    private sealed record Restore(IReadOnlyList<BookmarkFieldRef> Selected, BookmarkFieldRef? Top, BookmarkFieldRef? Keep);

    /// <summary>Stands in for the View: reports <paramref name="topRow"/> as the row at the top
    /// of the grid and records every restore the VM asks for.</summary>
    private static List<Restore> AttachView(LiveWalkerViewModel vm, BookmarkFieldRef? topRow)
    {
        var got = new List<Restore>();
        vm.CaptureViewAnchor += a => a.TopRow = topRow;
        vm.RestoreBookmarkView += (sel, top, keep) => got.Add(new Restore(sel, top, keep));
        return got;
    }

    private static LiveFieldValue Named(LiveWalkerViewModel vm, string name) => vm.Fields.First(f => f.Name == name);

    [Fact]
    public void Clearing_with_the_stepped_match_selected_keeps_the_view_where_it_is()
    {
        var vm = VmWithRows();
        var got = AttachView(vm, new BookmarkFieldRef("Beta", 0x700));
        vm.SearchText = "I32";
        vm.NextSearchMatchCommand.Execute(null);                    // lands on I32
        vm.UpdateSelectedFields(new[] { vm.SelectedField });         // what the grid reports

        vm.SearchText = "";                                          // select all, Delete

        var r = Assert.Single(got);
        Assert.Equal(new BookmarkFieldRef("Beta", 0x700), r.Top);    // the same top row: nothing moves
        Assert.Equal(new BookmarkFieldRef("I32", 0x630), r.Keep);
        Assert.Contains(new BookmarkFieldRef("I32", 0x630), r.Selected);
    }

    [Fact]
    public void Clearing_when_the_selection_excludes_the_match_brings_the_first_selected_row_up()
    {
        var vm = VmWithRows();
        var got = AttachView(vm, new BookmarkFieldRef("Beta", 0x700));
        vm.SearchText = "I32";
        vm.NextSearchMatchCommand.Execute(null);                    // lands on I32...
        vm.UpdateSelectedFields(new[] { Named(vm, "Gamma"), Named(vm, "Alpha") });   // ...then the user picks others

        vm.SearchText = "";

        var r = Assert.Single(got);
        var first = new BookmarkFieldRef("Alpha", 0x10);             // first in GRID order, not click order
        Assert.Equal(first, r.Top);
        Assert.Equal(first, r.Keep);
        Assert.Equal(2, r.Selected.Count);
    }

    [Theory]
    [InlineData("I32", "I3")]      // shortened
    [InlineData("I32", "I32_")]    // lengthened
    [InlineData("I32", "2")]       // a partial delete that leaves text
    [InlineData("I3", "I")]
    public void Other_edits_keep_todays_behaviour(string from, string to)
    {
        var vm = VmWithRows();
        var got = AttachView(vm, new BookmarkFieldRef("Beta", 0x700));
        vm.SearchText = from;
        vm.NextSearchMatchCommand.Execute(null);
        vm.UpdateSelectedFields(new[] { vm.SelectedField });

        vm.SearchText = to;

        Assert.Empty(got);
    }

    [Fact]
    public void One_character_to_empty_keeps_todays_behaviour()
    {
        // The maintainer's own limit: 1 -> 0 cannot be told apart from backspacing a keyword away.
        var vm = VmWithRows();
        var got = AttachView(vm, new BookmarkFieldRef("Beta", 0x700));
        vm.SearchText = "I";
        vm.UpdateSelectedFields(new[] { Named(vm, "I32") });

        vm.SearchText = "";

        Assert.Empty(got);
    }

    [Fact]
    public void Clearing_with_nothing_selected_stays_where_it_is()
    {
        // The maintainer's follow-up (2026-09-27): with nothing selected the view stays put too.
        var vm = VmWithRows();
        var got = AttachView(vm, new BookmarkFieldRef("Beta", 0x700));
        vm.SearchText = "I32";

        vm.SearchText = "";

        var r = Assert.Single(got);
        Assert.Empty(r.Selected);
        Assert.Equal(new BookmarkFieldRef("Beta", 0x700), r.Top);
        Assert.Null(r.Keep);
    }

    [Fact]
    public async Task A_navigation_clearing_the_keyword_is_not_the_user_clearing_it()
    {
        var vm = VmWithRows();
        vm.Breadcrumbs.Add(new BreadcrumbItem { Address = "0x1000", Label = "Root", FieldName = "Root" });
        vm.Breadcrumbs.Add(new BreadcrumbItem { Address = "0x2000", Label = "Leaf", FieldName = "Leaf", FieldOffset = 0x8 });
        var got = AttachView(vm, new BookmarkFieldRef("Beta", 0x700));
        vm.SearchText = "I32";
        vm.NextSearchMatchCommand.Execute(null);
        vm.UpdateSelectedFields(new[] { vm.SelectedField });

        await vm.GoBackCommand.ExecuteAsync(null);                   // clears the keyword itself

        // Back makes its own restore (the drilled row); none may carry the search's match.
        Assert.DoesNotContain(got, r => r.Keep == new BookmarkFieldRef("I32", 0x630));
    }
}
