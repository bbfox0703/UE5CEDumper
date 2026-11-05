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

    /// <summary>Each DLL opening the UI matches is the start of a string literal the DLL source actually sends: an edit to
    /// either side that drifts them apart turns the branch off without a word.</summary>
    [Theory]
    [InlineData(Constants.DllErrorUnknownCommand, "Fern.cpp", "\"Unknown command: \" + cmd")]
    [InlineData(Constants.DllErrorFunctionNotFound, "Renge.h", "\"Function not found: \" + funcName")]
    [InlineData(Constants.DllErrorNoInstanceFound, "Renge.h", "\"No instance found for class: \" + className")]
    public void The_DLL_sends_the_opening_the_UI_matches(string opening, string dllFile, string dllExpression)
    {
        Assert.StartsWith("\"" + opening, dllExpression, StringComparison.Ordinal);
        Assert.Contains(dllExpression, RepoFile("dll", "src", dllFile), StringComparison.Ordinal);
    }

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
