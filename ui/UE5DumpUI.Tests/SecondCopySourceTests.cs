using System.Text.RegularExpressions;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// [DUMPER-SECOND-COPY-AUTOSTART] Two copies of the dumper in one game process: the later copy takes over (maintainer,
/// 2026-11-17). Which copy is retired, and what its mailbox says afterwards, is unit-tested in dll_helpers_test
/// (Land.h). The wiring sits in Frieren.cpp, Heiter.cpp and Mimic.cpp, which reach no C++ test target, so it is
/// pinned here from the source.
/// </summary>
public sealed class SecondCopySourceTests
{
    private static string DllSrc(string file) =>
        File.ReadAllText(NumericInputCoercionTests.RepoFile("dll/src/" + file)).Replace("\r\n", "\n");

    /// <summary>Comments and string literals removed: only code is left.</summary>
    private static string CodeOnly(string s)
    {
        s = Regex.Replace(s, @"/\*.*?\*/", " ", RegexOptions.Singleline);
        s = string.Join('\n', s.Split('\n').Select(l =>
        {
            bool inStr = false;
            for (int i = 0; i + 1 < l.Length; i++)
            {
                char ch = l[i];
                if (ch == '\\' && inStr) { i++; continue; }
                if (ch == '"') inStr = !inStr;
                else if (!inStr && ch == '/' && l[i + 1] == '/') return l[..i];
            }
            return l;
        }));
        return Regex.Replace(s, "\"(?:\\\\.|[^\"\\\\\\n])*\"", "\"\"");
    }

    private static string FunctionBody(string code, string signatureStart)
    {
        int def = code.IndexOf(signatureStart, StringComparison.Ordinal);
        Assert.True(def >= 0, $"{signatureStart} not found in the source -- re-point this pin");
        int open = code.IndexOf('{', def);
        int depth = 0, close = open;
        for (; close < code.Length; close++)
        {
            if (code[close] == '{') depth++;
            else if (code[close] == '}' && --depth == 0) break;
        }
        return code[open..(close + 1)];
    }

    private static IReadOnlyList<string> DeclaredExports() =>
        Regex.Matches(CodeOnly(DllSrc("Frieren.h")), @"__declspec\(dllexport\)[^;(]*?\b(UE5_\w+)\s*\(")
            .Select(m => m.Groups[1].Value).ToList();

    /// <summary>A copy that handed over passes every C ABI call to the copy that took over, so a CE script that
    /// resolved the earlier copy's export reaches the live one. A new export without the line would run in the dead
    /// copy; UE5_YieldToLaterCopy is the one that must not forward, since it is how the hand-off begins.</summary>
    [Fact]
    public void EveryExportForwardsOnceYielded_ButTheYieldItself()
    {
        string frieren = CodeOnly(DllSrc("Frieren.cpp"));
        var exports = DeclaredExports();
        Assert.True(exports.Count >= 60, $"Frieren.h declares {exports.Count} exports -- the pattern lost them");
        Assert.Contains("UE5_YieldToLaterCopy", exports);
        foreach (string name in exports)
        {
            var def = Regex.Match(frieren, @"^(?:extern """" )?[A-Za-z_][\w \*:]*?\b" + name +
                                           @"\s*\((?:[^()]|\([^()]*\))*\)\s*\{", RegexOptions.Multiline);
            Assert.True(def.Success, $"{name} has no definition in Frieren.cpp -- re-point this pin");
            string body = FunctionBody(frieren, def.Value);
            string first = body[1..].TrimStart();
            if (name == "UE5_YieldToLaterCopy")
                Assert.DoesNotContain("LAND_FORWARD", body);
            else
                Assert.True(first.StartsWith("LAND_FORWARD(" + name, StringComparison.Ordinal),
                            $"{name}'s first statement is not LAND_FORWARD({name}, ...)");
        }
    }

    /// <summary>The later copy retires the earlier one inside its pipe start, after its own scan (the earlier copy
    /// serves meanwhile) and BEFORE it asks who holds the pipe; a copy that is handing over refuses to serve.</summary>
    [Fact]
    public void ThePipeStartRetiresEarlierCopiesBeforeItsProbe()
    {
        string body = FunctionBody(CodeOnly(DllSrc("Frieren.cpp")), "bool UE5_StartPipeServer() {");
        int refuse = body.IndexOf("Land::RefusesStart(Land::State())", StringComparison.Ordinal);
        int retire = body.IndexOf("Land::RetireEarlierCopies()", StringComparison.Ordinal);
        int probe = body.IndexOf("CreateFileW(", StringComparison.Ordinal);
        Assert.True(refuse > 0, "UE5_StartPipeServer refuses to serve in a copy that is handing over");
        Assert.True(retire > refuse, "the retirement follows the refusal");
        Assert.True(probe > retire, "the earlier copies are retired before the pipe probe");
    }

    /// <summary>The paths that start something of this copy's own refuse once it is handing over: UE5_Init's scan
    /// (after its already-initialised returns, so the teardown's own calls still work), the auto-start, and the claim
    /// watcher's Start.</summary>
    [Fact]
    public void AYieldingCopyStartsNothing()
    {
        string frieren = CodeOnly(DllSrc("Frieren.cpp"));
        string init = FunctionBody(frieren, "bool UE5_Init() {");
        int lastAlready = init.LastIndexOf("return true;", init.IndexOf("Land::RefusesStart", StringComparison.Ordinal),
                                           StringComparison.Ordinal);
        Assert.True(init.Contains("Land::RefusesStart(Land::State())"), "UE5_Init refuses to scan");
        Assert.True(lastAlready > 0, "the refusal comes after the already-initialised return");
        Assert.Contains("Land::RefusesStart(Land::State())", FunctionBody(frieren, "bool UE5_AutoStartBlocking() {"));
        string watcher = FunctionBody(frieren, "static void WatchForPipeAndClaim(DWORD holderPid) {");
        int refuse = watcher.IndexOf("Land::RefusesStart(Land::State())", StringComparison.Ordinal);
        int start = watcher.IndexOf("s_pipeServer.Start()", StringComparison.Ordinal);
        Assert.True(refuse > 0 && start > refuse, "the claim watcher refuses before it starts the server");
    }

    /// <summary>A busy pipe held by another copy of the dumper here is not waited on: the later copy scans and
    /// takes over. Deferring stays for a busy pipe that is not ours.</summary>
    [Fact]
    public void TheInjectPathDoesNotDeferToAnEarlierCopy()
    {
        string heiter = CodeOnly(DllSrc("Heiter.cpp"));
        Assert.Matches(@"deferToHolder\s*&&\s*Land::AnyCopyToRetire\(\)", heiter);
        Assert.Matches(@"else\s+if\s*\(\s*deferToHolder\s*\)", heiter);
    }

    /// <summary>Every mailbox handler answers in the mailbox its command came from: only the export definition, the
    /// poller's own-mailbox steps, StopThread's clear and GetAddress name this copy's mailbox directly.</summary>
    [Fact]
    public void MailboxHandlersAnswerThroughBox()
    {
        string mimic = CodeOnly(DllSrc("Mimic.cpp"));
        Assert.DoesNotMatch(@"\bg_invokeMailbox\.", mimic.Replace("g_invokeMailbox.initState)", "OWN_STATE)"));
        Assert.Contains("static MailboxData& Box()", mimic);
        string poller = FunctionBody(mimic, "static void PollingThreadBody() {");
        Assert.Matches(@"ServeOne\(&g_invokeMailbox,\s*warnedThrow\)", poller);
        Assert.Matches(@"for\s*\(auto&\s*slot\s*:\s*s_adopted\)", poller);
    }

    /// <summary>A proxy build exports through its .def file, so the yield must be listed in all four: a stale proxy
    /// that cannot be asked to yield is shut down instead, and loses the hand-off of its exports.</summary>
    [Fact]
    public void EveryProxyExportsTheYield()
    {
        foreach (string def in new[] { "ProxyVersion.def", "ProxyDinput8.def", "ProxyDxgi.def", "ProxyWinmm.def" })
            Assert.Matches(@"(?m)^\s*UE5_YieldToLaterCopy\s*$", DllSrc(def));
    }
}
