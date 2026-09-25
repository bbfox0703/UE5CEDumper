using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// scripts/inject-ue.ps1 (copied to dist\ by the build) against the path shapes of [PATH-SHAPE-2026-09-25]. Static
/// pins: ad-hoc PowerShell is barred on this machine (the AV quarantines it), so the script cannot be run here; the
/// behaviour these pin is PowerShell's documented one, cited per test.
/// </summary>
public class InjectScriptPs1Tests
{
    private static string Script() => File.ReadAllText(Path.Combine(RepoRoot(), "scripts", "inject-ue.ps1"));

    [Fact]
    public void TheElevatedRelaunch_QuotesThePathsItPasses()
    {
        // [PATH-PS1-ELEVATE-QUOTE] Start-Process joins -ArgumentList with single spaces and quotes nothing (its docs:
        // a value with a space must carry escaped double quotes). So '-File C:\Program Files\...\inject-ue.ps1' --
        // or a profile with a space, or 'DragonSword  Awakening' -- split after the user had accepted UAC.
        string s = Script();
        Assert.Matches(@"'-File',\s*\('""\{0\}""'\s*-f\s*\$PSCommandPath\)", s);
        Assert.Matches(@"'-Dll',\s*\('""\{0\}""'\s*-f\s*\$dllPath\)", s);
        Assert.DoesNotMatch(@"'-File',\s*\$PSCommandPath", s);
        Assert.DoesNotMatch(@"'-Dll',\s*""\$dllPath""", s);
    }

    private static string RepoRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !File.Exists(Path.Combine(d.FullName, "build.ps1"))) d = d.Parent;
        return d?.FullName ?? throw new DirectoryNotFoundException("repo root");
    }
}
