using System.IO;
using System.Text.RegularExpressions;
using UE5DumpUI.Models;
using UE5DumpUI.Services;
using UE5DumpUI.ViewModels;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// C5 — the "Pivot this property" context-menu command on the source panels raises
/// NavigateToPivot with (className, propName, propOffset) so MainWindowViewModel can hand off to
/// the Class Pivot tab. The handoff stays inert when the row/class is empty.
///
/// <para>[PIVOT-HANDOFF-SHADOWED] The offset rides along because a class can carry two properties of one name at
/// different offsets, and Class Pivot lists each copy apart: a name alone ticked only the first copy, whichever one
/// the user had right-clicked.</para>
/// </summary>
public class PivotHandoffCommandTests
{
    [Fact]
    public void PropertySearch_PivotThis_RaisesNavigateToPivot()
    {
        var vm = new PropertySearchViewModel(new StubDumpService(), new MockLoggingService());
        (string cls, string prop, int off)? got = null;
        vm.NavigateToPivot += (c, p, o) => got = (c, p, o);

        vm.PivotThisCommand.Execute(new PropertySearchMatch { ClassName = "AMyActor", PropName = "Health", PropOffset = 0x128 });

        Assert.Equal(("AMyActor", "Health", 0x128), got);
    }

    [Fact]
    public void PropertySearch_PivotThis_NullRow_DoesNothing()
    {
        var vm = new PropertySearchViewModel(new StubDumpService(), new MockLoggingService());
        bool fired = false;
        vm.NavigateToPivot += (_, _, _) => fired = true;

        vm.PivotThisCommand.Execute(null);   // no SelectedResult either

        Assert.False(fired);
    }

    [Fact]
    public void InterestingProperties_PivotThis_CarriesTheOffset()
    {
        var vm = new InterestingPropertiesViewModel(new StubDumpService(), new MockLoggingService());
        (string cls, string prop, int off)? got = null;
        vm.NavigateToPivot += (c, p, o) => got = (c, p, o);

        vm.PivotThisCommand.Execute(new ScoredPropertyRow
        {
            Match = new PropertySearchMatch { ClassName = "AMyActor", PropName = "Health", PropOffset = 0xC4 },
            FinalScore = 1, Category = PropertyCategory.Other, KeywordHits = 0, ClassBonus = 0, IsUnusualLocation = false,
        });

        Assert.Equal(("AMyActor", "Health", 0xC4), got);
    }

    [Fact]
    public void LiveWalker_PivotThis_UsesCurrentClassAndFieldName()
    {
        var vm = new LiveWalkerViewModel(new StubDumpService(), new MockLoggingService(),
                                         new MockPlatformService(Path.GetTempPath()));
        vm.CurrentClassName = "APlayerState";
        (string cls, string prop, int off)? got = null;
        vm.NavigateToPivot += (c, p, o) => got = (c, p, o);

        vm.PivotThisCommand.Execute(new LiveFieldValue { Name = "Score", Offset = 0x40 });

        Assert.Equal(("APlayerState", "Score", 0x40), got);
    }

    [Fact]
    public void LiveWalker_PivotThis_NoClassContext_DoesNothing()
    {
        var vm = new LiveWalkerViewModel(new StubDumpService(), new MockLoggingService(),
                                         new MockPlatformService(Path.GetTempPath()));
        vm.CurrentClassName = "";   // synthetic/empty context
        bool fired = false;
        vm.NavigateToPivot += (_, _, _) => fired = true;

        vm.PivotThisCommand.Execute(new LiveFieldValue { Name = "Score" });

        Assert.False(fired);
    }

    // Value-locator -> pivot: a value-scan hit already carries class + field, so
    // "I can see this value" reaches a pivot in one click (same handoff contract).

    [Fact]
    public void ValueSearch_PivotThis_RaisesNavigateToPivot()
    {
        var vm = new ValueSearchViewModel(new StubDumpService(), new MockLoggingService());
        (string cls, string prop, int off)? got = null;
        vm.NavigateToPivot += (c, p, o) => got = (c, p, o);

        vm.PivotThisCommand.Execute(new ValueCandidate { ClassName = "APlayerState", FieldName = "Gold", FieldOffset = 0x2A8 });

        Assert.Equal(("APlayerState", "Gold", 0x2A8), got);
    }

    [Fact]
    public void ValueSearch_PivotThis_NoClass_DoesNothing()
    {
        var vm = new ValueSearchViewModel(new StubDumpService(), new MockLoggingService());
        bool fired = false;
        vm.NavigateToPivot += (_, _, _) => fired = true;

        vm.PivotThisCommand.Execute(new ValueCandidate { ClassName = "", FieldName = "Gold" });

        Assert.False(fired);
    }

    /// <summary>
    /// [PIVOT-HANDOFF-SHADOWED] MainWindowViewModel is where the offset could still be dropped: every source panel's
    /// handoff must forward it to <see cref="ClassPivotViewModel.PivotForAsync"/>. Pinned in source, as the handler is
    /// an async void reached only through the composition root.
    /// </summary>
    [Fact]
    public void MainWindow_ForwardsTheOffsetOfEveryPivotHandoff()
    {
        var source = File.ReadAllText(NumericInputCoercionTests.RepoFile("ui/UE5DumpUI/ViewModels/MainWindowViewModel.cs"));

        var wirings = Regex.Matches(source, @"\.NavigateToPivot\s*\+=\s*(.+);");
        Assert.Equal(4, wirings.Count);
        foreach (Match w in wirings)
            Assert.Matches(new Regex(@"^\(\s*(\w+)\s*,\s*(\w+)\s*,\s*(\w+)\s*\)\s*=>\s*HandlePivotHandoff\(\s*\1\s*,\s*\2\s*,\s*\3\s*\)$"),
                           w.Groups[1].Value.Trim());

        Assert.Matches(new Regex(@"HandlePivotHandoff\(string className, string\? propName, int propOffset\)"), source);
        Assert.Matches(new Regex(@"Pivot\.PivotForAsync\(className, propName, propOffset\)"), source);
    }
}
