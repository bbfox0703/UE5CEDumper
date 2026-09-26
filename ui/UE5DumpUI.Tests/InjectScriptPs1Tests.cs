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

    [Fact]
    public void ResolveDll_ReadsPathsLiterally()
    {
        // [PATH-PS1-LITERALPATH] Test-Path / Resolve-Path bind a positional path to -Path, which expands wildcards:
        // a folder named 'UE5CEDumper [3554]' is the character class [345], so the DLL beside the script was
        // "not found". -LiteralPath takes the path as written.
        string s = Script();
        var fn = Regex.Match(s, @"function Resolve-Dll \{(?<body>.*?)\n\}", RegexOptions.Singleline);
        Assert.True(fn.Success, "Resolve-Dll is gone -- re-point this pin");
        string body = fn.Groups["body"].Value;
        Assert.DoesNotMatch(@"Test-Path\s+(?!-LiteralPath)", body);
        Assert.DoesNotMatch(@"Resolve-Path\s+(?!-LiteralPath)", body);
        Assert.Matches(@"Test-Path -LiteralPath \$Dll", body);
        Assert.Matches(@"Resolve-Path -LiteralPath \$c", body);
    }

    private static string RepoRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !File.Exists(Path.Combine(d.FullName, "build.ps1"))) d = d.Parent;
        return d?.FullName ?? throw new DirectoryNotFoundException("repo root");
    }
}
