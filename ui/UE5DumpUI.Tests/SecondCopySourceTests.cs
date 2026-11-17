using System.Text.RegularExpressions;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// [DUMPER-SECOND-COPY-AUTOSTART] Two copies of the dumper in one game process: the later copy takes over (maintainer,
/// 2026-11-17). Which copy is retired, when a mailbox is served, what it says afterwards, and where an export call goes
/// are unit-tested in dll_helpers_test (Land.h). The wiring sits in Land.cpp, Frieren.cpp, Heiter.cpp and Mimic.cpp,
/// which reach no C++ test target, so it is pinned here from the source.
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

    private static void InOrder(string body, string what, params string[] parts)
    {
        int at = -1;
        foreach (string p in parts)
        {
            int next = body.IndexOf(p, at + 1, StringComparison.Ordinal);
            Assert.True(next > at, $"{what}: '{p}' is missing or out of order");
            at = next;
        }
    }

    private static IReadOnlyList<string> DeclaredExports() =>
        Regex.Matches(CodeOnly(DllSrc("Frieren.h")), @"__declspec\(dllexport\)[^;(]*?\b(UE5_\w+)\s*\(")
            .Select(m => m.Groups[1].Value).ToList();

    /// <summary>The exports a copy that handed over does NOT forward: the yield itself begins the hand-off, and its
    /// shutdown and pipe stop do nothing there -- a CE untick reaching it must not stop the copy that took over, which
    /// that record never started (B30's rule; review wf_67396083-685 FWD-SHUTDOWN-B30).</summary>
    private static readonly string[] NotForwarded = { "UE5_YieldToLaterCopy", "UE5_Shutdown", "UE5_StopPipeServer" };

    /// <summary>The class walk's state is the copy's own, so a walk begun here finishes here (FWD-WALK-SPLIT).</summary>
    private static readonly string[] ForwardedUnlessWalkOpen = { "UE5_WalkClassGetField", "UE5_WalkClassEnd" };

    /// <summary>A copy that handed over passes every other C ABI call to the copy that took over, so a CE script that
    /// resolved the earlier copy's export reaches the live one. The first statement names the export EXACTLY: a prefix
    /// match let LAND_FORWARD(UE5_AutoStartBlocking) inside UE5_AutoStart pass, which never forwards
    /// (PIN-FWD-PREFIX).</summary>
    [Fact]
    public void EveryExportForwardsOnceYielded_ButTheThreeThatMustNot()
    {
        string frieren = CodeOnly(DllSrc("Frieren.cpp"));
        var exports = DeclaredExports();
        Assert.True(exports.Count >= 60, $"Frieren.h declares {exports.Count} exports -- the pattern lost them");
        foreach (string name in NotForwarded) Assert.Contains(name, exports);
        foreach (string name in exports)
        {
            var def = Regex.Match(frieren, @"^(?:extern """" )?[A-Za-z_][\w \*:]*?\b" + name +
                                           @"\s*\((?:[^()]|\([^()]*\))*\)\s*\{", RegexOptions.Multiline);
            Assert.True(def.Success, $"{name} has no definition in Frieren.cpp -- re-point this pin");
            string body = FunctionBody(frieren, def.Value);
            string first = body[1..].TrimStart();
            if (NotForwarded.Contains(name))
                Assert.DoesNotContain("LAND_FORWARD", body);
            else if (ForwardedUnlessWalkOpen.Contains(name))
                Assert.Matches(@"^LAND_FORWARD_UNLESS\(s_walkOpenHere\.load\([^)]*\),\s*" + name + @"\s*[,)]", first);
            else
                Assert.Matches(@"^LAND_FORWARD\(" + name + @"\s*[,)]", first);
        }
    }

    /// <summary>The yielded copy's shutdown and pipe stop are no-ops; the hand-off's own shutdown leaves the mailbox
    /// whole and says SKIPPED until the later copy serves it (MBX-5), where an untick clears it and says IDLE.</summary>
    [Fact]
    public void AHandedOverCopysShutdownStopsNothing_AndTheHandOffKeepsTheMailbox()
    {
        string frieren = CodeOnly(DllSrc("Frieren.cpp"));
        foreach (string sig in new[] { "void UE5_Shutdown() {", "void UE5_StopPipeServer() {" })
            Assert.StartsWith("if (Land::RefusesStart(Land::State()))", FunctionBody(frieren, sig)[1..].TrimStart());
        Assert.Contains("ShutdownBody(false)", FunctionBody(frieren, "void UE5_Shutdown() {"));
        InOrder(FunctionBody(frieren, "int32_t UE5_YieldToLaterCopy(HMODULE successor) {"), "the yield",
                "Land::BeginYield(successor)", "ShutdownBody(true)", "Land::FinishYield()");
        string body = FunctionBody(frieren, "static void ShutdownBody(bool handingOver) {");
        Assert.Contains("Mimic::StopThread(!handingOver)", body);
        Assert.Matches(@"initState\s*=\s*handingOver\s*\?\s*Mimic::INIT_SKIPPED\s*:\s*Mimic::INIT_IDLE", body);
        string mimic = CodeOnly(DllSrc("Mimic.cpp"));
        Assert.Matches(@"if\s*\(clearMailbox\)\s*memset\(&g_invokeMailbox", FunctionBody(mimic, "void StopThread(bool clearMailbox) {"));
    }

    /// <summary>The later copy takes over inside its pipe start, after its own scan and before it asks who holds the
    /// pipe; a copy that is handing over refuses before, and a copy taken over DURING its pass refuses after it
    /// (LAND-NO-RECHECK-AFTER-PASS). Every server start re-checks under the start lock.</summary>
    [Fact]
    public void ThePipeStartRetiresEarlierCopiesBeforeItsProbe()
    {
        string frieren = CodeOnly(DllSrc("Frieren.cpp"));
        string body = FunctionBody(frieren, "bool UE5_StartPipeServer() {");
        InOrder(body, "UE5_StartPipeServer", "Land::RefusesStart(Land::State())", "Land::RetireEarlierCopies()",
                "Land::RefusesStart(Land::State())", "CreateFileW(");
        Assert.DoesNotContain("s_pipeServer.Start()", body);
        InOrder(FunctionBody(frieren, "static bool StartServerUnlessHandedOver(const char* who) {"), "the server start",
                "Land::LockStart()", "Land::RefusesStart(Land::State())", "s_pipeServer.Start()");
        // The holder is the copy being taken over while its hand-off runs: claim the pipe when it lets go.
        Assert.Matches(@"case Voll::StartAction::AlreadyOurs:\s*if \(Land::RetireInFlight\(\)\)", body);
    }

    /// <summary>The paths that start something of this copy's own refuse once it is handing over: UE5_Init's scan
    /// (after its already-initialised returns, so the teardown's own calls still work), the auto-start's restarts under
    /// the start lock (MBX-6), its pipe start after a scan a take-over interrupted (AUTOSTART-AFTER-YIELD), and the
    /// claim watcher.</summary>
    [Fact]
    public void AYieldingCopyStartsNothing()
    {
        string frieren = CodeOnly(DllSrc("Frieren.cpp"));
        string init = FunctionBody(frieren, "bool UE5_Init() {");
        int refuse = init.IndexOf("Land::RefusesStart", StringComparison.Ordinal);
        Assert.True(refuse > 0, "UE5_Init refuses to scan");
        Assert.True(init.LastIndexOf("return true;", refuse, StringComparison.Ordinal) > 0,
                    "the refusal comes after the already-initialised return");
        Assert.Contains("Land::RefusesStart(Land::State())", FunctionBody(frieren, "bool UE5_AutoStartBlocking() {"));
        InOrder(FunctionBody(frieren, "static bool AutoStartWork() {"), "AutoStartWork",
                "Land::LockStart()", "Land::RefusesStart(Land::State())", "Tot::ResetShutdown()", "Mimic::StartThread()",
                "UE5_Init()", "Land::RefusesStart(Land::State())", "UE5_StartPipeServer()");
        string watcher = FunctionBody(frieren, "static void WatchForPipeAndClaim(DWORD holderPid) {");
        Assert.Contains("StartServerUnlessHandedOver(", watcher);
        Assert.DoesNotContain("s_pipeServer.Start()", watcher);
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

    /// <summary>Land.cpp ties the unit-tested decisions to the running DLL (PIN-LAND-CPP-UNPINNED): only earlier copies,
    /// latest first, never while a later copy has started; the pass stops once this copy is taken over; the mailbox is
    /// taken by the hand-off's own thread after the earlier copy's shutdown, with the layout-range rule, else SKIPPED
    /// and the magic cleared; the yield checks the order; an export call goes where RouteCall says.</summary>
    [Fact]
    public void LandWiresItsDecisions()
    {
        string land = CodeOnly(DllSrc("Land.cpp"));
        Assert.StartsWith("if (RefusesStart(State())) return", FunctionBody(land, "void RetireEarlierCopies() {")[1..].TrimStart());
        Assert.StartsWith("if (RefusesStart(State())) return", FunctionBody(land, "bool AnyCopyToRetire() {")[1..].TrimStart());
        string pass = FunctionBody(land, "void RetireEarlierCopies() {");
        InOrder(pass, "the pass", "const PassLock passLock;", "LaterCopyStarted(copies, own, later)",
                "for (int i = own - 1; i >= 0; --i)", "Judge(seen)", "CreateThread(", "WaitForSingleObject(",
                "if (RefusesStart(State()))");
        InOrder(FunctionBody(land, "DWORD WINAPI RetireThread(LPVOID param) {"), "the hand-off thread",
                "YIELD_NOT_LATER", "if (RefusesStart(State()))", "TakeMailbox(");
        string take = FunctionBody(land, "void TakeMailbox(const CopySeen& seen, Mimic::MailboxData* box, Mimic::MailboxContract* contract) {");
        InOrder(take, "TakeMailbox", "CanServeMailbox(seen.current, seen.minimum, Mimic::MAILBOX_CONTRACT",
                "Mimic::MAILBOX_LAYOUT_SINCE", "Mimic::AdoptMailbox(box)", "PublishedState(false", "&contract->magic");
        InOrder(FunctionBody(land, "YieldReply BeginYield(HMODULE successor) {"), "BeginYield",
                "IsEarlier(IndexOf(copies, g_hDllModule), IndexOf(copies, successor))", "s_startMutex", "ReplyToYield(State(), callerIsLater)");
        Assert.Contains("RouteCall(s, target != nullptr, t_forwardHops)", FunctionBody(land, "Routing RouteExport(const char* exportName) {"));
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

    /// <summary>The yield is listed in all four proxy .def files. A proxy exports it either way -- every proxy links the
    /// shared objects, and /DEF merges with __declspec(dllexport) rather than replacing it (ProxyDxgi.def's header) -- so
    /// this is the list check_proxy_exports and CE's getAddress readers see, kept complete (PIN-DEF-RATIONALE).</summary>
    [Fact]
    public void EveryProxyListsTheYield()
    {
        foreach (string def in new[] { "ProxyVersion.def", "ProxyDinput8.def", "ProxyDxgi.def", "ProxyWinmm.def" })
            Assert.Matches(@"(?m)^\s*UE5_YieldToLaterCopy\s*$", DllSrc(def));
    }
}
