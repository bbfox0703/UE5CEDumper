using System.IO;
using UE5DumpUI.Models;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// <c>[V8PREVIEWCLIP-2026-08-23]</c> — the Live Walker's Value column is a fixed 200 px
/// <c>TextBlock</c> with no trimming ellipsis, so a longer value is invisible and nothing
/// on screen says so. The case that made it matter: a DataTable's pre-drill preview is
/// <c>{DataTable: 100 rows, &lt;struct&gt;}</c> with the <c>⚠ showing 64 of 100</c> badge
/// appended LAST. The prefix alone overflows, so the one disclosure warning that the grid
/// holds 64 of 100 rows could not be read at the default width — on any table, at any N.
///
/// Found by looking at the screen. Every relevant ViewModel test passed throughout,
/// because they assert strings and the strings were right. Same shape as
/// <c>[PARAMSSORT-2026-08-22]</c>.
/// </summary>
public class LiveFieldValueTooltipTests
{
    /// <summary>Same idiom as <c>ObjectTreeViewModelNavigationTests.FindRepoRoot</c>.</summary>
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
    public void ValueTooltip_CarriesTheWholeString_IncludingTheBadgeThatWasClipped()
    {
        // Exactly the shape LiveWalkerViewModel builds for the synthetic RowMap row:
        // DataTableFieldPreview(...) is written into TypedValue, and DisplayValue reads
        // TypedValue first — so the badge reaches the cell, and must reach the tooltip.
        var f = new LiveFieldValue
        {
            Name = "RowMap",
            TypeName = "DataTableRows",
            TypedValue = "{DataTable: 100 rows, DumperTestTableRow}  ⚠ showing 64 of 100",
            DataTableRowCount = 100,
            DataTableStructName = "DumperTestTableRow",
        };

        Assert.Equal(f.DisplayValue, f.ValueTooltip);
        Assert.Contains("showing 64 of 100", f.ValueTooltip);
        // The half that IS visible in 200px must not be all the tooltip has, or the
        // tooltip would be as useless as the cell.
        Assert.Contains("{DataTable: 100 rows, DumperTestTableRow}", f.ValueTooltip);
    }

    [Fact]
    public void ValueTooltip_IsNullWhenEmpty_SoABlankCellDoesNotPopAnEmptyBox()
    {
        // The whole reason this is a separate property rather than binding DisplayValue
        // straight to ToolTip.Tip: Avalonia shows a tooltip whenever Tip is non-null, so
        // "" would pop an empty box on every blank cell.
        var blank = new LiveFieldValue { Name = "x", TypeName = "IntProperty" };
        Assert.Equal("", blank.DisplayValue);
        Assert.Null(blank.ValueTooltip);

        var filled = new LiveFieldValue { Name = "x", TypeName = "IntProperty", TypedValue = "42" };
        Assert.Equal("42", filled.ValueTooltip);
    }

    [Fact]
    public void EveryDisplayValueNotification_HasAMatchingValueTooltipNotification()
    {
        // THIS is the test that stops the fix rotting. ValueTooltip is computed from
        // DisplayValue, so a source property that invalidates one must invalidate the
        // other. If they drift, the visible text updates while the tooltip keeps showing
        // the old value -- worse than the clipping it was added to fix, because a stale
        // tooltip reads as authoritative.
        var root = FindRepoRoot();
        Assert.NotNull(root);
        var path = Path.Combine(root!, "ui", "UE5DumpUI", "Models", "LiveFieldValue.cs");
        Assert.True(File.Exists(path), path);
        var src = File.ReadAllText(path);

        // Count ATTRIBUTE LINES, not substring hits. The first version of this test
        // used Split() over the whole file and immediately failed 10 vs 9 -- because
        // the tenth "occurrence" was the attribute's name written inside a <c>...</c>
        // in LiveFieldValue's own doc comment. A detector that cannot tell code from
        // prose about code will fail the moment someone documents the rule it enforces.
        static int CountAttributeLines(string text, string attr)
        {
            int n = 0;
            foreach (var line in text.Split('\n'))
                if (line.Trim() == attr) n++;
            return n;
        }

        int display = CountAttributeLines(src, "[NotifyPropertyChangedFor(nameof(DisplayValue))]");
        int tooltip = CountAttributeLines(src, "[NotifyPropertyChangedFor(nameof(ValueTooltip))]");

        Assert.True(display > 0, "expected DisplayValue to be invalidated by some source property");
        Assert.True(display == tooltip,
            $"{display} [NotifyPropertyChangedFor(nameof(DisplayValue))] but {tooltip} for "
            + "ValueTooltip. Every source property that invalidates DisplayValue must "
            + "invalidate ValueTooltip too, or the Value column's tooltip goes stale while "
            + "the cell text updates ([V8PREVIEWCLIP-2026-08-23]).");
    }

    [Fact]
    public void TypeTooltip_CarriesTheWholeTypeName_AndIsNullWhenEmpty()
    {
        // The Type cell is 115px and UE type names run long. "DataTableRows" rendering
        // as "DataTableRo" is what exposed it -- while hovering that very cell as the
        // negative control for the Value column's fix.
        var f = new LiveFieldValue { Name = "RowMap", TypeName = "DataTableRows" };
        Assert.Equal("DataTableRows", f.TypeTooltip);

        var longer = new LiveFieldValue { Name = "x", TypeName = "MulticastInlineDelegateProperty" };
        Assert.Equal("MulticastInlineDelegateProperty", longer.TypeTooltip);

        Assert.Null(new LiveFieldValue { Name = "x" }.TypeTooltip);
    }

    [Fact]
    public void TheTypeColumnActuallyBindsTheTooltip()
    {
        var root = FindRepoRoot();
        Assert.NotNull(root);
        var path = Path.Combine(root!, "ui", "UE5DumpUI", "Views", "LiveWalkerPanel.axaml");
        Assert.True(File.Exists(path), path);
        var src = File.ReadAllText(path);

        Assert.Contains("ToolTip.Tip=\"{Binding TypeTooltip}\"", src);
        int text = src.IndexOf("Text=\"{Binding TypeName}\"", System.StringComparison.Ordinal);
        int tip = src.IndexOf("ToolTip.Tip=\"{Binding TypeTooltip}\"", System.StringComparison.Ordinal);
        Assert.True(text >= 0 && tip > text && tip - text < 200,
            "ToolTip.Tip=\"{Binding TypeTooltip}\" must sit on the same TextBlock as "
            + "Text=\"{Binding TypeName}\" in the Type column's CellTemplate.");

        // Converting DataGridTextColumn -> DataGridTemplateColumn is what gave the cell
        // an element to hang the tooltip on. Sorting came free with the text column and
        // must not have been dropped in the conversion -- nothing else would notice.
        Assert.Contains("SortMemberPath=\"TypeName\"", src);
    }

    [Fact]
    public void TheValueColumnActuallyBindsTheTooltip()
    {
        // The property can be perfect and the column can still not use it -- which is
        // precisely the failure class this whole finding belongs to: correct code that
        // never reaches the pixels. Assert the XAML, since no test renders this grid.
        var root = FindRepoRoot();
        Assert.NotNull(root);
        var path = Path.Combine(root!, "ui", "UE5DumpUI", "Views", "LiveWalkerPanel.axaml");
        Assert.True(File.Exists(path), path);
        var src = File.ReadAllText(path);

        Assert.Contains("ToolTip.Tip=\"{Binding ValueTooltip}\"", src);
        // ...on the same element that shows DisplayValue, not somewhere else in the file.
        int text = src.IndexOf("Text=\"{Binding DisplayValue}\"", System.StringComparison.Ordinal);
        int tip = src.IndexOf("ToolTip.Tip=\"{Binding ValueTooltip}\"", System.StringComparison.Ordinal);
        Assert.True(text >= 0 && tip > text && tip - text < 200,
            "ToolTip.Tip=\"{Binding ValueTooltip}\" must sit on the same TextBlock as "
            + "Text=\"{Binding DisplayValue}\" in the Value column's CellTemplate.");
    }
}
