using System.IO;
using UE5DumpUI.Models;
using UE5DumpUI.ViewModels;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// Phase 3: the Object Tree per-instance drill-down context actions. Each command must
/// raise its navigation event with the selected row's ADDRESS (the per-hit handoff a
/// global instance search needs), and must no-op on a null node or an empty address so
/// a bad row never fires a navigation to a dead target.
/// </summary>
public class ObjectTreeViewModelNavigationTests
{
    private static ObjectTreeViewModel NewVm() =>
        new(new StubDumpService(), new MockLoggingService(), new MockPlatformService(Path.GetTempPath()));

    private static UObjectNode Node(string addr = "0x1400ABCD") =>
        new() { Address = addr, ClassName = "BP_Enemy_C", Name = "Enemy_0" };

    [Fact]
    public void OpenInLiveWalker_RaisesNavigateToLiveWalker_WithAddress()
    {
        var vm = NewVm();
        string? got = null;
        vm.NavigateToLiveWalker += a => got = a;

        vm.OpenInLiveWalkerCommand.Execute(Node("0x1400ABCD"));

        Assert.Equal("0x1400ABCD", got);
    }

    [Fact]
    public void LocateInGWorld_RaisesLocateInGWorld_WithAddress()
    {
        var vm = NewVm();
        string? got = null;
        vm.LocateInGWorld += a => got = a;

        vm.LocateSelectedInGWorldCommand.Execute(Node("0x1400BEEF"));

        Assert.Equal("0x1400BEEF", got);
    }

    [Fact]
    public void LocateInGameEngine_RaisesLocateInGameEngine_WithAddress()
    {
        var vm = NewVm();
        string? got = null;
        vm.LocateInGameEngine += a => got = a;

        vm.LocateSelectedInGameEngineCommand.Execute(Node("0x1400CAFE"));

        Assert.Equal("0x1400CAFE", got);
    }

    [Fact]
    public void ShowRelatedObjects_RaisesNavigateToRelatedObjects_WithAddress()
    {
        var vm = NewVm();
        string? got = null;
        vm.NavigateToRelatedObjects += a => got = a;

        vm.ShowRelatedObjectsCommand.Execute(Node("0x1400F00D"));

        Assert.Equal("0x1400F00D", got);
    }

    [Fact]
    public void DrillCommands_NullNode_DoNotFire()
    {
        var vm = NewVm();
        bool fired = false;
        vm.NavigateToLiveWalker += _ => fired = true;
        vm.LocateInGWorld += _ => fired = true;
        vm.LocateInGameEngine += _ => fired = true;
        vm.NavigateToRelatedObjects += _ => fired = true;

        vm.OpenInLiveWalkerCommand.Execute(null);
        vm.LocateSelectedInGWorldCommand.Execute(null);
        vm.LocateSelectedInGameEngineCommand.Execute(null);
        vm.ShowRelatedObjectsCommand.Execute(null);

        Assert.False(fired);
    }

    [Fact]
    public void DrillCommands_EmptyAddress_DoNotFire()
    {
        var vm = NewVm();
        bool fired = false;
        vm.NavigateToLiveWalker += _ => fired = true;
        vm.NavigateToRelatedObjects += _ => fired = true;

        vm.OpenInLiveWalkerCommand.Execute(Node(""));
        vm.ShowRelatedObjectsCommand.Execute(Node(""));

        Assert.False(fired);
    }

    // ── [TREERECLICK-2026-08-22] ────────────────────────────────────────────────
    //
    // A cross-tab handoff pushes class X into Class/Struct while the tree still
    // highlights node P. Clicking P then did nothing at all -- no pipe traffic --
    // because an Avalonia ListBox writes SelectedItem only when it CHANGES, so
    // SelectionChanged never fired. The fix is MainWindowViewModel's
    // ShowClassInClassStructAsync, which clears the highlight before loading, making
    // the next click on P a genuine change.
    //
    // MainWindowViewModel cannot be constructed in a unit test, so what is pinned here
    // is the MECHANISM the fix depends on, at the VM that owns it.
    //
    // Negative control: delete the `SelectedNode = null` line from the helper and
    // ReSelectingAfterAClear_FiresAgain stops being reachable in the app -- the raise
    // count in the "no clear" test below is what the defect looked like.

    [Fact]
    public void ReSelectingTheSameNode_DoesNotFireAgain_ThisIsTheDefect()
    {
        var vm = NewVm();
        var raised = new List<UObjectNode?>();
        vm.SelectionChanged += n => raised.Add(n);

        var p = Node("0xP");
        vm.SelectedNode = p;
        vm.SelectedNode = p;          // the re-click, with no clear in between

        Assert.Single(raised);        // second assignment is swallowed -> silence
        Assert.Same(p, raised[0]);
    }

    [Fact]
    public void ReSelectingAfterAClear_FiresAgain_ThisIsWhyTheHandoffClears()
    {
        var vm = NewVm();
        var raised = new List<UObjectNode?>();
        vm.SelectionChanged += n => raised.Add(n);

        var p = Node("0xP");
        vm.SelectedNode = p;
        vm.SelectedNode = null;       // what ShowClassInClassStructAsync does
        vm.SelectedNode = p;          // the same re-click now lands

        Assert.Equal(3, raised.Count);
        Assert.Same(p, raised[0]);
        Assert.Null(raised[1]);
        Assert.Same(p, raised[2]);
    }

    [Fact]
    public void ClearingTheSelectionTwice_RaisesOnlyOnce()
    {
        // The clear runs on every handoff, so it must be idempotent: a second null
        // must not re-enter the cross-VM cascade for nothing.
        var vm = NewVm();
        var raised = new List<UObjectNode?>();
        vm.SelectionChanged += n => raised.Add(n);

        vm.SelectedNode = Node("0xP");
        vm.SelectedNode = null;
        vm.SelectedNode = null;

        Assert.Equal(2, raised.Count);
    }

    /// <summary>Walk up from the test binary to the repo root (the folder holding build.ps1 + docs).
    /// Same idiom as <c>SdkHeaderDeclaratorTests.FindRepoRoot</c>.</summary>
    private static string? FindRepoRoot()
    {
        var dir = new System.IO.DirectoryInfo(System.AppContext.BaseDirectory);
        for (int i = 0; i < 10 && dir is not null; i++, dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "build.ps1"))
                && Directory.Exists(Path.Combine(dir.FullName, "docs")))
                return dir.FullName;
        }
        return null;
    }

    [Fact]
    public void EveryCrossTabClassHandoff_GoesThroughTheHelperThatClearsTheTree()
    {
        // THIS is the test that pins the FIX; the three above pin the MECHANISM and would
        // pass with or without it. The real regression is not the helper losing its clear
        // -- it is a SIXTH handoff site being added later that calls the command directly
        // and quietly reintroduces [TREERECLICK-2026-08-22] on that one path.
        //
        // MainWindowViewModel cannot be constructed here (it builds the whole app graph),
        // so this asserts on the source, the way SdkHeaderDeclaratorTests already does.
        var root = FindRepoRoot();
        Assert.NotNull(root);
        var path = Path.Combine(root!, "ui", "UE5DumpUI", "ViewModels", "MainWindowViewModel.cs");
        Assert.True(File.Exists(path), path);
        var src = File.ReadAllText(path);

        // Exactly one direct use of the command, and it is inside the helper.
        var direct = src.Split("ClassStruct.LoadClassCommand.ExecuteAsync").Length - 1;
        Assert.True(direct == 1,
            $"expected exactly 1 direct ClassStruct.LoadClassCommand.ExecuteAsync (inside "
            + $"ShowClassInClassStructAsync); found {direct}. A new cross-tab handoff must call "
            + "ShowClassInClassStructAsync so the Object Tree highlight is cleared first.");

        Assert.Contains("private async Task ShowClassInClassStructAsync(string classAddr)", src);
        // ...and the helper must still actually clear. Order matters: clear BEFORE the load.
        int clear = src.IndexOf("ObjectTree.SelectedNode = null;", System.StringComparison.Ordinal);
        int load  = src.IndexOf("ClassStruct.LoadClassCommand.ExecuteAsync", System.StringComparison.Ordinal);
        Assert.True(clear >= 0 && load > clear,
            "ShowClassInClassStructAsync must clear ObjectTree.SelectedNode before awaiting the load");
    }
}
