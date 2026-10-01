using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// [AOBM-UI-FUNC-VISUAL] A control that needs the AOBMaker APP (AOBMaker.UI, the toolbar's "UI" dot) looks different
/// from one that needs only the CE plugin (the "DLL" dot), so a user can tell which program a failing button depends
/// on. The set is derived from the code -- the commands routed through <c>RegisterSymbolViaGenerateAobAsync</c>, the
/// one path that calls AOBMaker.UI -- so a new UI-backed button that is not marked fails here, and so does a mark on a
/// plugin-only one.
/// </summary>
public class AobMakerUiVisualTests
{
    private const string UiClass = "aobmUi";
    private const string AccentKey = "AobMakerUiAccent";

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

    /// <summary>Commands whose method hands its work to AOBMaker.UI's GenerateAob. <c>Always</c> is false for one that
    /// does so only when there is no export to anchor on ([AOBM-EXPORT-SYM-REST]): that button's mark has to follow the
    /// route it will take.</summary>
    private static (string Command, bool Always)[] UiBackedCommands()
    {
        var code = File.ReadAllText(RepoFile("ui/UE5DumpUI/ViewModels/PointerPanelViewModel.cs"));
        return Regex.Matches(code, @"Task\s+(\w+)Async\(\)\s*=>([^;]*);")
            .Where(m => m.Groups[2].Value.Contains("RegisterSymbolViaGenerateAobAsync("))
            .Select(m => (m.Groups[1].Value + "Command", !m.Groups[2].Value.Contains("RegisterSymbolFromExportAsync(")))
            .ToArray();
    }

    private static string? CommandOf(XElement button)
    {
        var m = Regex.Match(button.Attribute("Command")?.Value ?? "", @"\{Binding\s+(\w+)\}");
        return m.Success ? m.Groups[1].Value : null;
    }

    private static bool HasUiClass(XElement e)
        => (e.Attribute("Classes")?.Value ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains(UiClass);

    /// <summary>The property a conditional mark (<c>Classes.aobmUi="{Binding X}"</c>) binds to, or null.</summary>
    private static string? ConditionalMark(XElement e)
    {
        var m = Regex.Match(e.Attribute("Classes." + UiClass)?.Value ?? "", @"^\{Binding\s+(\w+UsesUi)\}$");
        return m.Success ? m.Groups[1].Value : null;
    }

    [Fact]
    public void Exactly_the_buttons_that_call_AOBMaker_UI_carry_its_mark()
    {
        var uiBacked = UiBackedCommands();
        Assert.NotEmpty(uiBacked);   // guard the guard: a regex that matches nothing must not pass silently

        var buttons = Axaml("PointerPanel.axaml").Descendants()
            .Where(e => e.Name.LocalName == "Button" && CommandOf(e) is not null)
            .ToList();

        foreach (var (cmd, always) in uiBacked)
        {
            var b = buttons.Single(x => CommandOf(x) == cmd);
            if (always)
                Assert.True(HasUiClass(b), $"{cmd} always calls AOBMaker.UI but its button does not carry Classes=\"{UiClass}\"");
            else
                Assert.True(!HasUiClass(b) && ConditionalMark(b) != null,
                    $"{cmd} calls AOBMaker.UI only without an export, so its mark must be Classes.{UiClass}=\"{{Binding ...UsesUi}}\"");
        }

        var wronglyMarked = buttons
            .Where(b => (HasUiClass(b) || ConditionalMark(b) != null) && !uiBacked.Any(u => u.Command == CommandOf(b)))
            .Select(CommandOf);
        Assert.Empty(wronglyMarked);
    }

    [Fact]
    public void A_conditionally_marked_button_shows_its_UI_tag_only_when_it_uses_the_UI()
    {
        // [AOBM-EXPORT-SYM-REST] The "UI" tag beside SYM says "this needs the AOBMaker app"; on a game whose GObjects
        // came from an export it does not, so the tag must hide with the mark.
        var conditional = Axaml("PointerPanel.axaml").Descendants()
            .Where(e => e.Name.LocalName == "Button" && ConditionalMark(e) != null).ToList();
        Assert.NotEmpty(conditional);
        foreach (var b in conditional)
        {
            var prop = ConditionalMark(b)!;
            var tags = b.Descendants().Where(e => e.Name.LocalName == "TextBlock"
                && e.Attribute("Text")?.Value == "{StaticResource str.Toolbar.AobMakerUi}").ToList();
            Assert.NotEmpty(tags);
            foreach (var t in tags)
                Assert.True(t.Ancestors().TakeWhile(a => a != b).Any(a => a.Attribute("IsVisible")?.Value == "{Binding " + prop + "}"),
                    $"{CommandOf(b)}'s UI tag is not inside an element whose IsVisible binds to {prop}");
        }
    }

    [Fact]
    public void The_mark_and_the_toolbar_UI_label_share_one_accent()
    {
        var app = XDocument.Load(RepoFile("ui/UE5DumpUI/App.axaml"));
        Assert.Contains(app.Descendants(), e => e.Name.LocalName == "SolidColorBrush"
            && e.Attributes().Any(a => a.Name.LocalName == "Key" && a.Value == AccentKey));

        var accentRef = "{StaticResource " + AccentKey + "}";
        var uiLabel = Axaml("MainWindow.axaml").Descendants()
            .Single(e => e.Name.LocalName == "TextBlock"
                && e.Attribute("Text")?.Value == "{StaticResource str.Toolbar.AobMakerUi}");
        Assert.Equal(accentRef, uiLabel.Attribute("Foreground")?.Value);

        var styleUsesAccent = Axaml("PointerPanel.axaml").Descendants()
            .Where(e => e.Name.LocalName == "Style" && (e.Attribute("Selector")?.Value ?? "").Contains("." + UiClass))
            .SelectMany(s => s.Descendants())
            .Any(e => e.Name.LocalName == "Setter" && e.Attribute("Value")?.Value == accentRef);
        Assert.True(styleUsesAccent, $"no Style for .{UiClass} in PointerPanel.axaml sets {accentRef}");
    }
}
