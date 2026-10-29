using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// [LW-CRUMB-UNDERSCORE] A Button, ToggleButton, RadioButton or CheckBox whose Content is a bound STRING draws it
/// through an access-key presenter, which drops the first <c>_</c>: a crumb read "DumperTestActor0" for
/// <c>DumperTestActor_0</c>, and the noise-denylist chips did the same to class names like <c>BP_Enemy_C</c>, as did the
/// Discover snapshot picks (user labels) and Proxy Deploy's drives (volume labels). A control that shows such a name
/// puts a TextBlock in its content instead. Only bindings to the app's own labels, which carry no underscore, may stay.
/// <c>CrumbUnderscoreTests</c> (headless) shows the crumb drawn whole.
///
/// <para>The scan is wider than the four controls that went wrong (LW-PIN-NARROW, its review): every control whose
/// template may present a string through an access key, the slots that take one (<c>Content</c>, <c>Header</c>,
/// <c>OnContent</c>, <c>OffContent</c>), and every binding spelling -- <c>CompiledBinding</c>, <c>ReflectionBinding</c>,
/// <c>Path=</c>, trailing options. Over-matching costs an allowlist entry; under-matching is how a name loses its
/// underscore unseen.</para>
/// </summary>
public class AccessKeyNameButtonsTests
{
    /// <summary>App-owned labels and glyphs: they never hold a game name, a user label or a volume label.</summary>
    private static readonly string[] AppLabels =
    {
        "ExportCeFieldButtonLabel", "DeleteOrphansLabel", "Glyph", "CaptureSettingsToggleText", "CaptureButtonText",
    };

    private const string AccessKeyControls =
        "Button|ToggleButton|RadioButton|CheckBox|HyperlinkButton|DropDownButton|SplitButton|ToggleSplitButton|"
        + "RepeatButton|MenuItem|TabItem|ToggleSwitch|Label|GroupBox|HeaderedContentControl|Expander";

    private static readonly Regex Bound = new(
        @"<(" + AccessKeyControls + @")\b[^>]*?\b(Content|Header|OnContent|OffContent)="""
        + @"\{(?:Compiled|Reflection)?Binding(?:\s+(?:Path=)?([^\s,}]+))?(?:\s*,[^}]*)?\}""",
        RegexOptions.Singleline);

    private static string RepoDir(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (int i = 0; i < 8 && dir is not null; i++, dir = dir.Parent)
        {
            string candidate = Path.Combine(dir.FullName, relative);
            if (Directory.Exists(candidate)) return candidate;
        }
        throw new DirectoryNotFoundException($"could not find {relative} walking up from {AppContext.BaseDirectory}");
    }

    [Fact]
    public void No_control_binds_its_content_straight_to_a_name()
    {
        var offenders = Directory.EnumerateFiles(RepoDir(@"ui\UE5DumpUI\Views"), "*.axaml")
            .SelectMany(f => Bound.Matches(File.ReadAllText(f))
                .Where(m => !AppLabels.Contains(m.Groups[3].Value))
                .Select(m => $"{Path.GetFileName(f)}: {m.Groups[1].Value} {m.Groups[2].Value}={{Binding {m.Groups[3].Value}}}"))
            .ToList();

        Assert.True(offenders.Count == 0, string.Join(Environment.NewLine, offenders));
    }

    // Guard the guard: each spelling the scan claims to cover is matched, and a plain TextBlock is not.
    [Theory]
    [InlineData(@"<Button Content=""{Binding Label}""/>", "Label")]
    [InlineData(@"<MenuItem Header=""{CompiledBinding Name}""/>", "Name")]
    [InlineData(@"<TabItem Header=""{ReflectionBinding Path=Title}""/>", "Title")]
    [InlineData(@"<ToggleSwitch OnContent=""{Binding On, Mode=OneWay}""/>", "On")]
    [InlineData(@"<CheckBox IsChecked=""{Binding X}"" Content=""{Binding Drive.Label, FallbackValue=?}""/>", "Drive.Label")]
    public void The_scan_matches_every_binding_spelling(string axaml, string path)
    {
        var m = Bound.Match(axaml);
        Assert.True(m.Success, axaml);
        Assert.Equal(path, m.Groups[3].Value);
    }

    [Fact]
    public void The_scan_leaves_a_TextBlock_alone()
        => Assert.DoesNotMatch(Bound, @"<TextBlock Text=""{Binding Name}""/>");
}
