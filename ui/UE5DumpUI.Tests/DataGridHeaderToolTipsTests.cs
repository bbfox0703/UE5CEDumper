using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Avalonia.Controls;
using UE5DumpUI.Helpers;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// [PROXY-HEADER-TIP-DEAD] Found in the S5 live pass on build 3558 (4K at 225 %): the Proxy Deploy grid's "Loaded?" and
/// "Suggested proxy" headers carry a <c>ToolTip.Tip</c> on their header TextBlock -- the tooltips the sixth, seventh
/// and eighth reviews added to explain the "shared · " / "stale · " marks and Recommend's order -- and neither ever
/// shows. Measured on screen with a staged build: other tooltips on the panel show (Force Overwrite, Scan Drives); a
/// tip set by a Style on <c>DataGridColumnHeader</c> shows on every header EXCEPT those two, because Avalonia's
/// ToolTipService walks up from the hit element and stops at the first control carrying a tip -- the header content --
/// whose own tooltip never opens. So the tip must live on the header cell, not on its content.
/// </summary>
public class DataGridHeaderToolTipsTests
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

    [Fact]
    public void ATipOnTheHeaderContent_MovesToTheHeaderCell()
    {
        var text = new TextBlock { Text = "Loaded?" };
        ToolTip.SetTip(text, "explains the marks");
        var header = new DataGridColumnHeader { Content = text };

        Assert.True(DataGridHeaderToolTips.MoveContentTip(header));
        Assert.Equal("explains the marks", ToolTip.GetTip(header));
        // Left on the content as well, the lookup would stop there again and show nothing.
        Assert.Null(ToolTip.GetTip(text));
        // Idempotent: a second pass finds nothing to move and keeps the header's tip.
        Assert.False(DataGridHeaderToolTips.MoveContentTip(header));
        Assert.Equal("explains the marks", ToolTip.GetTip(header));
    }

    [Fact]
    public void AHeaderWithoutAContentTip_IsLeftAlone()
    {
        var plainString = new DataGridColumnHeader { Content = "Status" };
        Assert.False(DataGridHeaderToolTips.MoveContentTip(plainString));
        Assert.Null(ToolTip.GetTip(plainString));

        var plainText = new DataGridColumnHeader { Content = new TextBlock { Text = "Name" } };
        Assert.False(DataGridHeaderToolTips.MoveContentTip(plainText));
        Assert.Null(ToolTip.GetTip(plainText));
    }

    [Fact]
    public void TheProxyDeployGrid_HoistsItsHeaderTips()
    {
        var doc = XDocument.Load(RepoFile("ui/UE5DumpUI/Views/ProxyDeployPanel.axaml"));
        var grid = Assert.Single(doc.Descendants(), e => e.Name.LocalName == "DataGrid"
                                                       && e.Attribute(X + "Name")?.Value == "GamesGrid");
        // The grid still has header controls carrying a tip -- otherwise this pin guards nothing.
        Assert.Contains(grid.Descendants(), e => e.Name.LocalName == "TextBlock"
                                                 && e.Parent?.Name.LocalName.EndsWith("Column.Header", StringComparison.Ordinal) == true
                                                 && e.Attribute("ToolTip.Tip") is not null);

        var code = File.ReadAllText(RepoFile("ui/UE5DumpUI/Views/ProxyDeployPanel.axaml.cs"));
        Assert.Matches(@"DataGridHeaderToolTips\.Hoist\(\s*[^)]*GamesGrid", code);
    }
}
