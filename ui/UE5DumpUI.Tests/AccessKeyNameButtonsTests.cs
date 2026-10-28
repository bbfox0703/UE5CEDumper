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
/// </summary>
public class AccessKeyNameButtonsTests
{
    /// <summary>App-owned labels and glyphs: they never hold a game name, a user label or a volume label.</summary>
    private static readonly string[] AppLabels =
    {
        "ExportCeFieldButtonLabel", "DeleteOrphansLabel", "Glyph", "CaptureSettingsToggleText", "CaptureButtonText",
    };

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
        var bound = new Regex(@"<(Button|ToggleButton|RadioButton|CheckBox)\b[^>]*?\bContent=""\{Binding(?:\s+([\w.]+))?\}""",
                              RegexOptions.Singleline);
        var offenders = Directory.EnumerateFiles(RepoDir(@"ui\UE5DumpUI\Views"), "*.axaml")
            .SelectMany(f => bound.Matches(File.ReadAllText(f))
                .Where(m => !AppLabels.Contains(m.Groups[2].Value))
                .Select(m => $"{Path.GetFileName(f)}: {m.Groups[1].Value} Content={{Binding {m.Groups[2].Value}}}"))
            .ToList();

        Assert.True(offenders.Count == 0, string.Join(Environment.NewLine, offenders));
    }
}
