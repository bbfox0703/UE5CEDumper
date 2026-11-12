using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UE5DumpUI.Services;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// [CT-INITFAILED-HEADLINE] The DLL publishes INIT_FAILED for <c>!(ok &amp;&amp; inited)</c> (Frieren.cpp
/// AutoStartWork): the pipe server did not start, OR init aborted because a shutdown landed mid-scan, in which case
/// the server DID start. Every INIT_FAILED message used to open with only the first ("initialised, but the Pipe Server
/// failed to start"), contradicting its own causes sentence; measured live 2026-11-12 (verification-register.md
/// [PIPE-PER-PROCESS-2026-11-06] item 5) on exactly the aborted case. These pin that each copy names both outcomes and
/// never claims init finished. Only the message's string literals are read, so a comment quoting the old wording
/// cannot trip them (working-lessons 1.9).
/// </summary>
public class CeInitFailedMessageTests
{
    private const string Dll = @"D:\dist\UE5Dumper.dll";

    private static void AssertNamesBothOutcomes(string text, string where)
    {
        Assert.True(text.Contains("the scan was cut short", StringComparison.Ordinal),
            $"{where}: does not name the aborted scan:\n{text}");
        Assert.True(text.Contains("the Pipe Server did not start", StringComparison.Ordinal),
            $"{where}: does not name the pipe server:\n{text}");
        foreach (var claim in new[] { "initialised", "initialized", "init finished" })
            Assert.False(text.Contains(claim, StringComparison.OrdinalIgnoreCase),
                $"{where}: claims init finished (\"{claim}\"), which the aborted case contradicts:\n{text}");
    }

    /// <summary>The INIT_FAILED branch of an emitted or shipped script: from its <c>elseif</c> to its
    /// <c>return</c>, keeping only the quoted string literals.</summary>
    private static string BranchLiterals(string script)
    {
        var start = script.IndexOf("state == INIT_FAILED then", StringComparison.Ordinal);
        Assert.True(start >= 0, "no INIT_FAILED branch");
        var end = script.IndexOf("return", start, StringComparison.Ordinal);
        Assert.True(end > start, "the INIT_FAILED branch has no return");
        var body = script.Substring(start, end - start);
        var literals = Regex.Matches(body, "\"(?:[^\"\\\\]|\\\\.)*\"|'(?:[^'\\\\]|\\\\.)*'")
            .Select(m => m.Value);
        return string.Join("\n", literals);
    }

    [Fact]
    public void The_shared_message_names_both_outcomes()
        => AssertNamesBothOutcomes(CeReadinessLua.PipeFailedMessage, "CeReadinessLua");

    [Fact]
    public void The_generated_inject_record_names_both_outcomes()
        => AssertNamesBothOutcomes(BranchLiterals(CeInjectScriptGenerator.Generate(Dll)), "inject record");

    [Fact]
    public void The_generated_autorun_record_names_both_outcomes()
        => AssertNamesBothOutcomes(BranchLiterals(CeAutorunScriptGenerator.Generate(Dll)), "autorun record");

    [Fact]
    public void The_shipped_ct_names_both_outcomes()
    {
        var ct = File.ReadAllText(NumericInputCoercionTests.RepoFile("scripts/UE5CEDumper.CT"));
        AssertNamesBothOutcomes(BranchLiterals(ct), "scripts/UE5CEDumper.CT");
    }
}
