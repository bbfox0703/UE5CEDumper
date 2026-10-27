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
        // (twelfth review, R12-02) ...and the rest of the handler's decisions, each a one-token regression back to R9-01:
        // the change guard (> 0.5; with < the writes never run and the XAML's fixed floors stay), the Auto-row SUM
        // (+=), and the filter (the two star rows out, invisible children out).
        Assert.Matches(@"Math\.Abs\(\s*root\.RowDefinitions\[\s*SavedRow\s*\]\.MinHeight\s*-\s*saved\s*\)\s*>\s*0\.5\s*\)", code);
        Assert.Matches(@"Math\.Abs\(\s*root\.RowDefinitions\[\s*LowerRow\s*\]\.MinHeight\s*-\s*lower\s*\)\s*>\s*0\.5\s*\)", code);
        Assert.Matches(@"autos\s*\+=\s*child\.DesiredSize\.Height\s*;", code);
        Assert.Matches(@"row\s*!=\s*SavedRow\s*&&\s*row\s*!=\s*LowerRow\s*&&\s*child\.IsVisible\s*\)", code);
        // (tenth review, R10-04) The CALL, with its argument -- the method's own declaration also contains "RowFloors(".
        Assert.Matches(@"var\s*\(\s*saved\s*,\s*lower\s*\)\s*=\s*RowFloors\(\s*root\.Bounds\.Height\s*-\s*autos\s*\)", code);
    }

    [Fact]
    public void Toolbar_AobMakerChip_IsNoWiderThanBefore()
    {
        // [AOBM-UI-INDICATOR] The maintainer's condition for the AOBMaker app's dot: the toolbar gets no wider. The old
        // chip read "AOBMaker" + "Connected" (17 characters) and changed width whenever the state flipped. The new one
        // keeps its state in two dots and their tooltips: "AOBMaker" + "DLL" + "/" + "UI".
        const int OldWidestText = 17;
        var chip = Axaml("MainWindow.axaml").Descendants()
            .Single(e => e.Name.LocalName == "StackPanel" && Attr(e, "IsVisible") == "{Binding IsAobMakerConfigured}");
        var en = XDocument.Load(RepoFile("ui/UE5DumpUI/Resources/Strings/en.axaml"));
        string Text(string? value)
        {
            if (value is null) return "";
            if (!value.StartsWith("{StaticResource ", StringComparison.Ordinal)) return value;
            var key = value["{StaticResource ".Length..^1];
            return en.Descendants().Single(e => Attr(e, X + "Key") == key).Value;
        }

        // Always shown: every TextBlock without a visibility binding of its own. The ⚠ is no longer in the chip at all
        // ([TOOLBAR-OTHER-MOVERS]), and it shows only with an attach warning.
        var shown = chip.Descendants().Where(e => e.Name.LocalName == "TextBlock" && Attr(e, "IsVisible") is null)
                        .Select(e => Text(Attr(e, "Text"))).ToList();
        Assert.Equal(new[] { "AOBMaker", "DLL", "/", "UI" }, shown);
        Assert.True(shown.Sum(s => s.Length) <= OldWidestText, string.Join("", shown));
        Assert.DoesNotContain("AobMakerConnected", chip.ToString());
        Assert.DoesNotContain("AobMakerOffline", chip.ToString());

        var dots = chip.Descendants().Where(e => e.Name.LocalName == "Ellipse").ToList();
        Assert.Equal(4, dots.Count);
        Assert.All(dots, d => Assert.True(Attr(d, "Width") == "9" && Attr(d, "Height") == "9"));
        Assert.Equal(2, dots.Count(d => Attr(d, "Fill") == "#4EC9B0"));
        Assert.Equal(2, dots.Count(d => Attr(d, "Fill") == "#888888"));

        // The state is in the tooltips now, so each dot's group must carry one.
        var groups = dots.Select(d => d.Parent!).Distinct().ToList();
        Assert.Equal(2, groups.Count);
        Assert.All(groups, g => Assert.StartsWith("{Binding ", Attr(g, "ToolTip.Tip") ?? ""));
    }

    private static double Num(string? value) =>
        double.Parse(value ?? throw new InvalidOperationException("no value"), System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Left + right of an Avalonia Thickness written as "u", "h,v" or "l,t,r,b"; 0 when absent.</summary>
    private static double Horizontal(string? thickness)
    {
        if (thickness is null) return 0;
        var p = thickness.Split(',').Select(Num).ToArray();
        return p.Length switch { 1 => 2 * p[0], 2 => 2 * p[0], 4 => p[0] + p[2], _ => throw new FormatException(thickness) };
    }

    /// <summary>The column definition an element sits in ("Auto", "*", ...).</summary>
    private static string ColumnOf(XElement grid, XElement child) =>
        (Attr(grid, "ColumnDefinitions") ?? "*").Split(',')[(int)Num(Attr(child, "Grid.Column") ?? "0")].Trim();

    /// <summary>A Text / Content value as the user reads it: a StaticResource resolved from en.axaml, else the literal.</summary>
    private static string EnText(string? value)
    {
        if (value is null) return "";
        if (!value.StartsWith("{StaticResource ", StringComparison.Ordinal)) return value;
        var key = value["{StaticResource ".Length..^1];
        var en = XDocument.Load(RepoFile("ui/UE5DumpUI/Resources/Strings/en.axaml"));
        return en.Descendants().Single(e => Attr(e, X + "Key") == key).Value;
    }

    private static XElement ToolbarStatusLine(XDocument doc) => Assert.Single(doc.Descendants(),
        e => e.Name.LocalName == "TextBlock" && Attr(e, "Text") == "{Binding StatusText}");

    private static XElement AobMakerRefresh(XDocument doc) => Assert.Single(doc.Descendants(),
        e => e.Name.LocalName == "Button" && Attr(e, "Command") == "{Binding RefreshAobMakerCommand}");

    /// <summary>The fixed-width Grid the toolbar status line sits in.</summary>
    private static XElement StatusSlot(XDocument doc)
    {
        var slot = ToolbarStatusLine(doc).Parent!;
        Assert.True(slot.Name.LocalName == "Grid",
            $"the status line sits in a {slot.Name.LocalName}, not in a fixed-width slot Grid");
        return slot;
    }

    [Fact]
    public void Toolbar_StatusLine_IsAFixedWidthSlot()
    {
        // The L64 run of [W1-PIPEBUSY-LOG], observation (b), 2026-09-22: the toolbar status line was only CAPPED, so it took
        // the width of its text. The ⟳ probe writes its "not connected" sentence there, the line widened, and every
        // control after it in the WrapPanel moved: ⟳ went from about x 444 to x 658, out from under the cursor, and the
        // next click opened the Address dropdown. A fixed Width keeps the controls after it still whatever the status says.
        // [L64-TOOLBAR-FIXED-SLOT] review: the fixed width is the slot's, a Grid the status fills; Start Scan and the
        // error line share it (Toolbar_ErrorLine_AndStartScan_ShareTheStatusSlot).
        var doc = Axaml("MainWindow.axaml");
        var status = ToolbarStatusLine(doc);
        var slot = StatusSlot(doc);
        Assert.True(slot.IsBefore(AobMakerRefresh(doc)), "⟳ no longer follows the status slot: re-check what this pin protects");

        Assert.True(Attr(slot, "Width") is not null, "the status slot has no fixed Width: its text moves every control after it");
        // 360 is the budget the status sentences are written for (about 50 characters before the ellipsis); a status with
        // nothing beside it gets exactly that.
        Assert.Equal(360d, Num(Attr(slot, "Width")) - Horizontal(Attr(status, "Margin")));
        Assert.Null(Attr(status, "Width"));
        Assert.Equal("*", ColumnOf(slot, status));   // it takes what the rest of the slot leaves
        Assert.Equal("CharacterEllipsis", Attr(status, "TextTrimming"));
        Assert.Equal("{Binding StatusText}", Attr(status, "ToolTip.Tip"));   // the whole sentence stays one hover away
    }

    [Fact]
    public void Toolbar_ErrorLine_AndStartScan_ShareTheStatusSlot()
    {
        // [L64-TOOLBAR-FIXED-SLOT] review + [TOOLBAR-OTHER-MOVERS]: the error line (capped at 360) and Start Scan sat beside
        // the fixed status line in a horizontal StackPanel. Either one appearing moved every control after it, ⟳ included,
        // and the StackPanel does not wrap: at the 800-DIP minimum window width a visible error ran past the window edge
        // and lost its ellipsis. In the slot's Auto columns they take their width from the status line instead.
        var doc = Axaml("MainWindow.axaml");
        var slot = StatusSlot(doc);
        var error = Assert.Single(doc.Descendants(),
            e => e.Name.LocalName == "TextBlock" && Attr(e, "Text") == "{Binding ErrorMessage}");
        var scan = Assert.Single(doc.Descendants(),
            e => e.Name.LocalName == "Button" && Attr(e, "Command") == "{Binding TriggerScanCommand}");

        Assert.Same(slot, error.Parent);
        Assert.Same(slot, scan.Parent);
        Assert.Equal("Auto", ColumnOf(slot, error));
        Assert.Equal("Auto", ColumnOf(slot, scan));
        Assert.Equal("CharacterEllipsis", Attr(error, "TextTrimming"));
        Assert.Equal("{Binding ErrorMessage}", Attr(error, "ToolTip.Tip"));
        // At most half the slot, so the status beside it stays readable; the whole error is one hover away.
        Assert.InRange(Num(Attr(error, "MaxWidth")), 120, Num(Attr(slot, "Width")) / 2);
    }

    [Fact]
    public void Toolbar_ConnectionGroup_FitsTheMinimumWindowWidth()
    {
        // [L64-TOOLBAR-FIXED-SLOT] review: the connection group is a horizontal StackPanel, which never wraps, so its widest
        // state must fit the window at its minimum width or the group runs past the edge -- where a visible error line
        // lost its ellipsis. Every child must have a bounded width for this to be checkable at all.
        var doc = Axaml("MainWindow.axaml");
        var group = ToolbarStatusLine(doc).Ancestors()
            .First(a => a.Name.LocalName == "StackPanel" && a.Parent?.Name.LocalName == "WrapPanel");
        Assert.Equal("Horizontal", Attr(group, "Orientation"));

        // A generous bound for a button: 10 DIP a character at the default 14 px font, its padding, a 1-DIP border.
        // A fixed Width is the element's real width, so it wins over the estimate ([TOOLBAR-CONNECT-WIDTH]).
        double ButtonBound(XElement b) => 10 * EnText(Attr(b, "Content")).Length + Horizontal(Attr(b, "Padding")) + 2;
        double Bound(XElement e) => (Attr(e, "Width") is { } width ? Num(width)
              : e.Name.LocalName == "Button" ? ButtonBound(e)
              : Num(Attr(e, "MaxWidth")
                    ?? throw new InvalidOperationException($"{e.Name.LocalName} in the connection group has no bounded width")))
            + Horizontal(Attr(e, "Margin"));

        // Connect and Disconnect show one at a time: the pair counts once, at the wider of the two.
        var pair = group.Elements().Where(e => Attr(e, "IsVisible") is "{Binding !IsConnected}" or "{Binding IsConnected}").ToList();
        Assert.Equal(2, pair.Count);
        var rest = group.Elements().Except(pair).ToList();
        double widest = pair.Max(Bound) + rest.Sum(Bound) + Num(Attr(group, "Spacing")) * rest.Count;

        var border = group.Parent!.Parent!;   // the toolbar's WrapPanel, then its Border
        double available = Num(Attr(doc.Root!, "MinWidth")) - Horizontal(Attr(border, "Padding"));
        Assert.True(widest <= available,
            $"the connection group can be {widest} DIP wide, but the toolbar has {available} at the minimum window width");
    }

    [Fact]
    public void Toolbar_ConnectAndDisconnect_OccupyOneWidth()
    {
        // [TOOLBAR-CONNECT-WIDTH] Connect and Disconnect show one at a time in the slot before the status line, and each
        // took its own label's width: about 20 DIP apart, so every control after them -- the AOBMaker ⟳ included --
        // moved once at connect and back at disconnect. One shared fixed Width keeps them still.
        var doc = Axaml("MainWindow.axaml");
        XElement ButtonFor(string command) => Assert.Single(doc.Descendants(),
            e => e.Name.LocalName == "Button" && Attr(e, "Command") == $"{{Binding {command}}}");
        var pair = new[] { ButtonFor("ConnectCommand"), ButtonFor("DisconnectCommand") };
        Assert.Same(pair[0].Parent, pair[1].Parent);
        Assert.True(pair[1].IsBefore(AobMakerRefresh(doc)), "⟳ no longer follows the pair: re-check what this pin protects");

        var width = Attr(pair[0], "Width");
        Assert.True(width is not null, "Connect has no fixed Width: its label's width moves every control after it");
        foreach (var b in pair)
        {
            var name = EnText(Attr(b, "Content"));
            Assert.Equal(width, Attr(b, "Width"));
            // Fluent's Button stretches its content (HorizontalContentAlignment defaults to Stretch, measured headless on
            // Avalonia 12.1.3), so in a Width wider than the label the text sits at the left edge unless it is centred.
            Assert.Equal("Center", Attr(b, "HorizontalContentAlignment"));
            // Either one would override the shared Width in Avalonia's layout.
            Assert.Null(Attr(b, "MinWidth"));
            Assert.Null(Attr(b, "MaxWidth"));
            // A margin sits outside the Width, so a different one would still move what follows.
            Assert.Equal(Horizontal(Attr(pair[0], "Margin")), Horizontal(Attr(b, "Margin")));
            // The label must fit, or a fixed Width cuts it. Measured in Segoe UI at the default 14 px, "Disconnect" is
            // 68 DIP; 8 DIP a character keeps a margin over that. The estimate holds only at the default font size.
            Assert.Null(Attr(b, "FontSize"));
            double needed = 8 * name.Length + Horizontal(Attr(b, "Padding")) + 2;
            Assert.True(Num(width) >= needed, $"'{name}' needs about {needed} DIP, but the shared Width is {width}");
        }
    }

    [Fact]
    public void Toolbar_AttachWarning_SitsAfterTheRefreshButton_OutsideItsChip()
    {
        // [TOOLBAR-OTHER-MOVERS]: the [AOBM-ATTACH-CHECK] warning sat inside the AOBMaker chip, BEFORE ⟳, so it appearing
        // pushed ⟳ right -- and widened the chip, which can wrap the whole chip to the next toolbar row. As its own toolbar
        // item after the chip it moves nothing before it.
        var doc = Axaml("MainWindow.axaml");
        var warn = TextBlockOf(doc, "str.Toolbar.AobMakerWrongProcess");
        var refresh = AobMakerRefresh(doc);
        var chip = refresh.Parent!;
        Assert.Equal("{Binding IsAobMakerConfigured}", Attr(chip, "IsVisible"));

        Assert.False(warn.Ancestors().Contains(chip), "the warning is inside the AOBMaker chip, which holds ⟳");
        Assert.True(refresh.IsBefore(warn), "the warning comes before ⟳, so it pushes ⟳ when it appears");
        Assert.Same(chip.Parent, warn.Parent);   // a toolbar item of its own
        Assert.Equal("{Binding AobMakerShared.HasAttachWarning}", Attr(warn, "IsVisible"));
        Assert.Equal("{Binding AobMakerShared.AttachWarning}", Attr(warn, "ToolTip.Tip"));
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
