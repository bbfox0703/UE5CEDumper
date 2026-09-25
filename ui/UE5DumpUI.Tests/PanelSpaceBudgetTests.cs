using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// [UI-SPACE-2026-09-25] Vertical space on a 4K screen at 225 %. The maintainer's laptop is 3840x2400, so the window
/// has 1707x1067 DIP -- ~110 DIP less height and ~210 less width than FullHD at 100 %. Measured there on build 3557:
/// <list type="bullet">
/// <item>the main tab strip wrapped to THREE rows of Fluent's 48 DIP minimum (24 px headers), ~144 DIP on every tab;</item>
/// <item>Snapshot's diff grid was squeezed to its header row, and the saved list to about two rows;</item>
/// <item>Class Pivot's field picker and results grid were entirely below the window, under ~20 lines of explanation.</item>
/// </list>
/// These pin the layout decisions that gave the space back. They cannot prove the result looks right -- the screen is
/// the acceptance, recorded in docs/todo.md -- but they stop a later edit from quietly undoing one.
/// </summary>
public class PanelSpaceBudgetTests
{
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

    private static string RepoFile(string relative)
    {
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 8 && dir is not null; i++, dir = Path.GetDirectoryName(dir))
        {
            var candidate = Path.Combine(dir, relative.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(candidate)) return candidate;
        }
        throw new FileNotFoundException($"could not locate {relative} from {AppContext.BaseDirectory}");
    }

    private static XDocument Axaml(string name) => XDocument.Load(RepoFile($"ui/UE5DumpUI/Views/{name}"));

    private static string? Attr(XElement e, XName name) => e.Attribute(name)?.Value;

    private static string Res(string key) => "{StaticResource " + key + "}";

    /// <summary>The TextBlock that shows <paramref name="key"/>'s text; exactly one.</summary>
    private static XElement TextBlockOf(XDocument doc, string key) =>
        Assert.Single(doc.Descendants(), e => e.Name.LocalName == "TextBlock" && Attr(e, "Text") == Res(key));

    /// <summary>A row's MinHeight from the element form of Grid.RowDefinitions (the "Auto,*" shorthand has none).</summary>
    private static double RowMinHeight(XElement grid, int row)
    {
        var defs = grid.Elements().FirstOrDefault(e => e.Name.LocalName == "Grid.RowDefinitions");
        Assert.True(defs is not null, "the grid's rows are written as a shorthand string, which cannot carry a MinHeight");
        var rows = defs!.Elements().Where(e => e.Name.LocalName == "RowDefinition").ToList();
        Assert.True(row < rows.Count, $"row {row} does not exist ({rows.Count} rows)");
        var min = Attr(rows[row], "MinHeight");
        return min is null ? 0 : double.Parse(min, System.Globalization.CultureInfo.InvariantCulture);
    }

    [Fact]
    public void MainTabs_HeadersAreCompactEnoughForTwoRows()
    {
        var doc = Axaml("MainWindow.axaml");
        var tabs = Assert.Single(doc.Descendants(), e => e.Name.LocalName == "TabControl" && Attr(e, X + "Name") == "MainTabs");
        var res = tabs.Elements().FirstOrDefault(e => e.Name.LocalName == "TabControl.Resources");
        Assert.True(res is not null, "MainTabs overrides no Fluent tab resource: headers are 24 px in 48 DIP rows");

        double Value(string key)
        {
            var d = res!.Elements().FirstOrDefault(e => Attr(e, X + "Key") == key);
            Assert.True(d is not null, $"MainTabs does not override {key}");
            return double.Parse(d!.Value, System.Globalization.CultureInfo.InvariantCulture);
        }

        // 18 tabs, measured at 24 px: ~2770 DIP of headers against ~1340 DIP of strip at 1707 DIP wide -> 3 rows.
        // At 18 px the text shrinks by a quarter (the 12 DIP side padding does not) -> ~2180 DIP -> 2 rows.
        Assert.InRange(Value("TabItemHeaderFontSize"), 14, 18);
        Assert.InRange(Value("TabItemMinHeight"), 28, 36);
    }

    [Fact]
    public void Snapshot_GridsKeepAMinimumHeight_AndTheAutoHintIsATooltip()
    {
        var doc = Axaml("SnapshotPanel.axaml");
        var root = doc.Root!.Elements().First(e => e.Name.LocalName == "Grid");

        int RowOf(Func<XElement, bool> pick)
        {
            var el = Assert.Single(root.Elements(), e => pick(e));
            return int.Parse(Attr(el, "Grid.Row") ?? "0");
        }

        int savedRow = RowOf(e => e.Name.LocalName == "DataGrid" && Attr(e, X + "Name") == "SnapshotsGrid");
        int lowerRow = RowOf(e => e.Name.LocalName == "DockPanel"
                                  && e.Descendants().Any(d => Attr(d, X + "Name") == "DiffGrid"));

        // Saved list: header + ~3 rows. Lower area: the mode switch (~40) + the Compare expander (~185) + a diff grid of
        // header + ~4 rows. Without these, a 1067 DIP screen left the diff grid its header only.
        Assert.True(RowMinHeight(root, savedRow) >= 100, $"saved-snapshots row {savedRow} has no usable MinHeight");
        Assert.True(RowMinHeight(root, lowerRow) >= 340, $"diff/group row {lowerRow} has no usable MinHeight");

        // The auto-snapshot hint is a tooltip, not a line of its own.
        Assert.DoesNotContain(doc.Descendants(), e => e.Name.LocalName == "TextBlock"
                                                      && Attr(e, "Text") == Res("str.Snapshot.Auto.Hint"));
        Assert.Contains(doc.Descendants(), e => Attr(e, "ToolTip.Tip") == Res("str.Snapshot.Auto.Hint"));
    }

    /// <summary>
    /// (ninth review, R9-01, MED) The floors must never take space the Auto rows need. Measured by the reviewer on the
    /// committed layout at 225 % (grid 846.7 DIP): by default the rows sat 3 DIP above their floors, and opening the
    /// Noise picker (the last Auto row) overflowed by 299 DIP -- its grid and buttons below the window, and no
    /// collapse could free them. So each layout pass caps the floors at the room the Auto rows leave, both in
    /// proportion: full floors when there is room, less when the picker, a status line or a short window needs it.
    /// </summary>
    [Fact]
    public void Snapshot_RowFloors_NeverTakeTheAutoRowsSpace()
    {
        var full = UE5DumpUI.Views.SnapshotPanel.RowFloors(room: 10_000);
        Assert.Equal((0d, 0d), UE5DumpUI.Views.SnapshotPanel.RowFloors(room: 0));
        Assert.Equal((0d, 0d), UE5DumpUI.Views.SnapshotPanel.RowFloors(room: -50));   // Auto rows already overflow
        Assert.Equal(full, UE5DumpUI.Views.SnapshotPanel.RowFloors(room: full.Saved + full.Lower));
        var half = UE5DumpUI.Views.SnapshotPanel.RowFloors(room: (full.Saved + full.Lower) / 2);
        Assert.Equal(full.Saved / 2, half.Saved, 6);
        Assert.Equal(full.Lower / 2, half.Lower, 6);
        foreach (double room in new[] { 1d, 37d, 163.5d, 299d, 459.9d })
        {
            var f = UE5DumpUI.Views.SnapshotPanel.RowFloors(room);
            Assert.True(f.Saved + f.Lower <= room + 1e-9, $"room {room}: floors {f.Saved} + {f.Lower} take more than it");
        }

        // The full floors ARE the XAML's (one pair of numbers, two places, pinned together).
        var doc = Axaml("SnapshotPanel.axaml");
        var root = doc.Root!.Elements().First(e => e.Name.LocalName == "Grid");
        Assert.Equal("RootGrid", Attr(root, X + "Name"));
        int savedRow = int.Parse(Attr(Assert.Single(root.Elements(),
            e => Attr(e, X + "Name") == "SnapshotsGrid")!, "Grid.Row") ?? "0");
        int lowerRow = int.Parse(Attr(Assert.Single(root.Elements(),
            e => e.Name.LocalName == "DockPanel" && e.Descendants().Any(d => Attr(d, X + "Name") == "DiffGrid"))!,
            "Grid.Row") ?? "0");
        Assert.Equal(RowMinHeight(root, savedRow), full.Saved);
        Assert.Equal(RowMinHeight(root, lowerRow), full.Lower);

        // The code-behind re-applies them on every layout pass, from the room the Auto rows leave.
        var code = StripComments(File.ReadAllText(RepoFile("ui/UE5DumpUI/Views/SnapshotPanel.axaml.cs")));
        Assert.Contains("LayoutUpdated", code);
        Assert.Contains("DesiredSize.Height", code);
        Assert.Matches(@"const\s+int\s+SavedRow\s*=\s*" + savedRow + @"\s*;", code);
        Assert.Matches(@"const\s+int\s+LowerRow\s*=\s*" + lowerRow + @"\s*;", code);
        // (eleventh review, R11-03) The DATA FLOW: RowFloors' result is what both writes store -- a write of a fixed floor
        // (the R9-01 regression) would satisfy a bare "MinHeight =".
        Assert.Matches(@"RowDefinitions\[\s*SavedRow\s*\]\.MinHeight\s*=\s*saved\s*;", code);
        Assert.Matches(@"RowDefinitions\[\s*LowerRow\s*\]\.MinHeight\s*=\s*lower\s*;", code);
        // (tenth review, R10-04) The CALL, with its argument -- the method's own declaration also contains "RowFloors(".
        Assert.Matches(@"var\s*\(\s*saved\s*,\s*lower\s*\)\s*=\s*RowFloors\(\s*root\.Bounds\.Height\s*-\s*autos\s*\)", code);
    }

    /// <summary>Drop // and /* */ comments, so a pin cannot be satisfied by a commented-out line.</summary>
    private static string StripComments(string code) =>
        System.Text.RegularExpressions.Regex.Replace(code, @"/\*[\s\S]*?\*/|//[^\n]*", "");

    [Theory]
    [InlineData("str.Pivot.Intro")]
    [InlineData("str.Pivot.Discover.Intro")]
    [InlineData("str.Pivot.ResultsHint")]
    public void ClassPivot_AnExplanationIsOneLine_WithTheWholeTextOnHover(string key)
    {
        var tb = TextBlockOf(Axaml("ClassPivotPanel.axaml"), key);
        // One line: no wrapping of any kind (WrapWithOverflow wraps too -- ninth review, R9-04).
        Assert.True(Attr(tb, "TextWrapping") is null or "NoWrap", $"TextWrapping={Attr(tb, "TextWrapping")}");
        Assert.Equal("CharacterEllipsis", Attr(tb, "TextTrimming"));
        Assert.Equal(Res(key), Attr(tb, "ToolTip.Tip"));
        // Trimming needs a bounded width. (R9-04) Every ancestor, not only the parent: a WrapPanel or a horizontal
        // StackPanel hands its child the DESIRED width, and a ScrollViewer that may scroll horizontally hands it
        // infinity -- in each case the line never trims.
        foreach (var up in tb.Ancestors())
        {
            string n = up.Name.LocalName;
            Assert.NotEqual("WrapPanel", n);
            Assert.False(n == "StackPanel" && Attr(up, "Orientation") == "Horizontal", "inside a horizontal StackPanel");
            if (n == "ScrollViewer")
                Assert.Equal("Disabled", Attr(up, "HorizontalScrollBarVisibility"));
        }
    }

    [Fact]
    public void ClassPivot_TheLimitsAreOnDemand_AndTheSetupAreaIsCapped()
    {
        var doc = Axaml("ClassPivotPanel.axaml");

        var limits = TextBlockOf(doc, "str.Pivot.Discover.Limits");
        var box = limits.Ancestors().First(e => e.Name.LocalName == "Border");
        Assert.StartsWith("{Binding", Attr(box, "IsVisible") ?? "");

        // Discover + the pivot target scroll inside a viewer whose MaxHeight the code-behind ties to the panel's height,
        // so the field picker and the results always keep the rest.
        var scroller = Assert.Single(doc.Descendants(), e => e.Name.LocalName == "ScrollViewer"
                                                             && Attr(e, X + "Name") == "SetupScroller");
        Assert.Contains(scroller.Descendants(), e => Attr(e, X + "Name") == "DiscoverGrid");
        Assert.DoesNotContain(scroller.Descendants(), e => Attr(e, X + "Name") == "FieldPickGrid");

        // (R9-04) The actual assignment, comments stripped: two words near each other in the raw text also matched a
        // commented-out line or an unrelated MaxHeight.
        Assert.Equal("Disabled", Attr(scroller, "HorizontalScrollBarVisibility"));
        var code = StripComments(File.ReadAllText(RepoFile("ui/UE5DumpUI/Views/ClassPivotPanel.axaml.cs")));
        Assert.Matches(@"FindControl<ScrollViewer>\(\s*""SetupScroller""\s*\)\s*is\s*\{\s*\}\s*setup", code);
        Assert.Matches(@"SizeChanged\s*\+=\s*\(\s*_\s*,\s*e\s*\)\s*=>\s*setup\.MaxHeight\s*=\s*Math\.Max\(\s*SetupMinHeight\s*,\s*e\.NewSize\.Height\s*\*\s*SetupShare\s*\)", code);
    }
}
