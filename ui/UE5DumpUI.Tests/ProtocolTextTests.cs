using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// [I18N-READINESS-2026-11-04] P5. A few UI branches tell another program's replies apart by their TEXT: the DLL's
/// error texts carry no code, the AOBMaker plugin's "Unknown type", and Windows' sharing-violation message. That text
/// is the other side's, not the UI's: moved into en.axaml and translated, the match stops and the branch silently takes
/// its other arm (a stale proxy goes undetected, a Self-Test skip turns into a failure). So the openings live in one
/// place, Constants' protocol-text block, marked do-not-localize, and are pinned to what the DLL actually sends.
/// </summary>
public class ProtocolTextTests
{
    private static string RepoFile(params string[] parts)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            var c = Path.Combine(new[] { dir.FullName }.Concat(parts).ToArray());
            if (File.Exists(c)) return File.ReadAllText(c);
        }
        throw new FileNotFoundException(string.Join("/", parts) + " not found above " + AppContext.BaseDirectory);
    }

    /// <summary>The code of a C# file with its // comments removed, so a comment that quotes the text does not count.</summary>
    private static string CodeOnly(string s) => string.Join('\n', s.Split('\n').Select(l =>
    {
        int c = l.IndexOf("//", StringComparison.Ordinal);
        return c >= 0 ? l[..c] : l;
    }));

    [Theory]
    [InlineData("Services/DumpService.cs", "\"Unknown command\"")]
    [InlineData("ViewModels/PointerPanelViewModel.cs", "\"Function not found\"")]
    [InlineData("ViewModels/PointerPanelViewModel.cs", "\"No instance found\"")]
    [InlineData("Services/AobMakerBridgeService.cs", "\"Unknown type\"")]
    [InlineData("Services/ProxyDeployService.cs", "\"being used\"")]
    public void The_matcher_names_the_protocol_constant_not_a_literal(string file, string literal)
    {
        var code = CodeOnly(RepoFile(new[] { "ui", "UE5DumpUI" }.Concat(file.Split('/')).ToArray()));
        Assert.DoesNotContain(literal, code, StringComparison.Ordinal);
    }
}
