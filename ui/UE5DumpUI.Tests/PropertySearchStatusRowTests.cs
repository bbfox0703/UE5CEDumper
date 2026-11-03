using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// [PROPSEARCH-STATUS-CLIPPED] Property Search's status line gets a row of its own and wraps there.
///
/// <para>[FORCESTATUSCLIP-2026-08-22] made it the fill child of the search row, trimmed with an ellipsis and carrying
/// the full text as a tooltip. That made the tail recoverable, not readable: the search controls take most of the row,
/// and measured on build 3682 in a 1389-px window, a Force armed on nothing showed "... no live instance of CheatManager
/// or..." -- everything that says the hold is working as intended, and the templates-only note after it, sat behind a
/// hover nobody knows to make. Those tails are this panel's honesty clauses ("cap reached, more exist unheld" too).</para>
///
/// <para>These pin the layout; the acceptance is the screen, recorded in docs/todo.md.</para>
/// </summary>
public class PropertySearchStatusRowTests
{
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

    private static string? Attr(XElement e, string name) => e.Attribute(name)?.Value;

    private static XElement StatusLine()
    {
        var doc = XDocument.Load(RepoFile("ui/UE5DumpUI/Views/PropertySearchPanel.axaml"));
        return Assert.Single(doc.Descendants(),
            e => e.Name.LocalName == "TextBlock" && Attr(e, "Text") == "{Binding StatusText}");
    }

    [Fact]
    public void TheStatusLineWraps_OnARowOfItsOwn()
    {
        var status = StatusLine();
        Assert.Equal("Wrap", Attr(status, "TextWrapping"));
        Assert.Equal("Top", Attr(status, "DockPanel.Dock"));
        // A row of its own: a direct child of the panel's outer DockPanel, which the UserControl holds.
        var parent = status.Parent!;
        Assert.Equal("DockPanel", parent.Name.LocalName);
        Assert.Equal("UserControl", parent.Parent!.Name.LocalName);
    }

    [Fact]
    public void TheSearchRowNoLongerHoldsTheStatusLine()
    {
        // Wrapping beside the search controls would grow that row by a line per ~40 characters and push the grid down
        // on every status; the row of its own is what lets it wrap at the panel's full width.
        var status = StatusLine();
        var search = Assert.Single(status.Document!.Descendants(),
            e => e.Name.LocalName == "Button" && Attr(e, "Command") == "{Binding SearchCommand}");
        var searchRow = search.Ancestors().First(e => e.Name.LocalName == "DockPanel");
        Assert.NotSame(status.Parent, searchRow);
        Assert.DoesNotContain(status, searchRow.Descendants());
    }
}
