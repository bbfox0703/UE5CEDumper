using System.Text.RegularExpressions;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// [DUMPER-SECOND-COPY-AUTOSTART] Two copies of the dumper in one game process: the later copy takes over. Which copy
/// is retired, when a mailbox is served, what it says afterwards, and where an export call goes
/// are unit-tested in dll_helpers_test (Land.h). The wiring in Land.cpp, Frieren.cpp and Heiter.cpp reaches no C++
/// test target yet, so it is pinned here from the source; Mimic.cpp's -- the mailboxes the poller serves, what a stop
/// leaves in them -- is mimic_mailbox_test's LIFECYCLE and BOX blocks.
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
    /// that record never started (a record stops only what it started). The hidden-actor hand-over is answered from
    /// this copy's own record, for the copy it would be forwarded to.</summary>
    private static readonly string[] NotForwarded =
        { "UE5_YieldToLaterCopy", "UE5_Shutdown", "UE5_StopPipeServer", "UE5_HandOverHiddenActors" };

    /// <summary>The class walk's state is the copy's own, so a walk begun here finishes here.</summary>
    private static readonly string[] ForwardedUnlessWalkOpen = { "UE5_WalkClassGetField", "UE5_WalkClassEnd" };

    /// <summary>A copy that handed over passes every other C ABI call to the copy that took over, so a CE script that
    /// resolved the earlier copy's export reaches the live one. The first statement names the export EXACTLY: a prefix
    /// match would let LAND_FORWARD(UE5_AutoStartBlocking) pass inside a UE5_AutoStart that never forwards.</summary>
    [Fact]
    public void EveryExportForwardsOnceYielded_ButTheFourThatMustNot()
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
            else if (name == "UE5_WalkClassBegin")
            {
                // A forwarded Begin first closes a walk left open here.
                Assert.StartsWith("if (Land::Forwards(Land::State()))", first);
                InOrder(body, name, "s_walkCache = ClassInfo{}", "s_walkOpenHere.store(false",
                        "LAND_FORWARD(UE5_WalkClassBegin, uclassAddr)");
            }
            else if (ForwardedUnlessWalkOpen.Contains(name))
                Assert.Matches(@"^LAND_FORWARD_UNLESS\(s_walkOpenHere\.load\([^)]*\),\s*" + name + @"\s*[,)]", first);
            else
                Assert.Matches(@"^LAND_FORWARD\(" + name + @"\s*[,)]", first);
        }
    }

    /// <summary>The yielded copy's shutdown and pipe stop are no-ops; the hand-off's own shutdown leaves the mailbox
    /// whole and says SKIPPED until the later copy serves it, where an untick clears it and says IDLE. StopThread's own
    /// clear is mimic_mailbox_test's LIFECYCLE.</summary>
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
    }

    /// <summary>The later copy takes over inside its pipe start, after its own scan and before it asks who holds the
    /// pipe; a copy that is handing over refuses before, and a copy taken over DURING its pass refuses after it.
    /// Every server start re-checks under the start lock.</summary>
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
    /// the start lock, its pipe start after a scan a take-over interrupted, and the claim watcher.</summary>
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
                "UE5_Init()", "Land::RefusesStart(Land::State())", "UE5_StartPipeServer()",
                // ...and nothing published over a take-over that landed while the pipe started
                "Land::LockStart()", "Land::RefusesStart(Land::State())", "initState =");
        Assert.Matches(@"\}\s*if \(Land::RefusesStart\(Land::State\(\)\)\) \{[^}]*return false;\s*\}\s*bool ok = UE5_StartPipeServer\(\)",
                       FunctionBody(frieren, "static bool AutoStartWork() {"));
        Assert.Matches(@"bool ok = UE5_StartPipeServer\(\);\s*\{\s*const auto publishLock = Land::LockStart\(\);\s*" +
                       @"if \(Land::RefusesStart\(Land::State\(\)\)\)[^}]*return;\s*\}\s*g_invokeMailbox\.initState",
                       CodeOnly(DllSrc("Heiter.cpp")));
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

    /// <summary>Land.cpp ties the unit-tested decisions to the running DLL: only earlier copies,
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
        string take = FunctionBody(land,
            "void TakeMailbox(const CopySeen& seen, Mimic::MailboxData* box, Mimic::MailboxContract* contract, bool forwards) {");
        InOrder(take, "TakeMailbox", "if (!forwards)", "now != Mimic::INIT_IDLE", "return;",
                "CanServeMailbox(", "AdoptMailbox(", "PublishedState(false", "&contract->magic");
        // The verdict gates the adoption, with every argument in place (an order check alone passes `||` and swapped
        // arguments).
        Assert.Matches(@"CanServeMailbox\(seen\.current, seen\.minimum, Mimic::MAILBOX_CONTRACT,\s*" +
                       @"Mimic::MAILBOX_CONTRACT_MIN, Mimic::MAILBOX_LAYOUT_SINCE\)", take);
        Assert.Contains("if (layoutOk && Mimic::AdoptMailbox(box, forwards))", take);
        // An old copy found started again is left to itself: the whole condition, so a weakened one fails.
        Assert.Matches(@"if \(Macht::ReadSafe\(reinterpret_cast<uintptr_t>\(&box->initState\), now\) && " +
                       @"now != Mimic::INIT_IDLE\) \{\s*LOG_WARN\([^;]*\);\s*return;", take);
        Assert.Contains("TakeMailbox(call->seen, call->box, call->contract, call->yield != nullptr)", land);
        InOrder(FunctionBody(land, "YieldReply BeginYield(HMODULE successor) {"), "BeginYield",
                "IsEarlier(IndexOf(copies, g_hDllModule), IndexOf(copies, successor))", "s_startMutex", "ReplyToYield(State(), callerIsLater)");
        Assert.Contains("RouteCall(s, target != nullptr, t_forwardHops)", FunctionBody(land, "Routing RouteExport(const char* exportName) {"));
    }

    /// <summary>Every mailbox handler answers in the mailbox its command came from: mimic_mailbox_test's BOX sends
    /// each command to another mailbox while this copy's own holds a decoy, and WINDOW / LIFECYCLE drive the poller.
    /// What stays here, and why: the rule that only the export, the poller's own-mailbox steps, StopThread's clear and
    /// GetAddress name this copy's mailbox covers a handler added later before BOX has a row for it; the mirror is
    /// written before the serve within one pass, which no test can see apart; and s_box is per thread, which only two
    /// pollers racing would show.</summary>
    [Fact]
    public void MailboxHandlersAnswerThroughBox()
    {
        string mimic = CodeOnly(DllSrc("Mimic.cpp"));
        Assert.DoesNotMatch(@"\bg_invokeMailbox\.",
                            Regex.Replace(mimic, @"g_invokeMailbox\.initState\s*[,)]", "OWN_STATE)"));
        string poller = FunctionBody(mimic, "static void PollingThreadBody() {");
        // Each adopted mailbox says this copy's state -- strictly, for a copy that cannot forward -- before it is served.
        InOrder(poller, "the adopted mailboxes", "s_adopted[slot].load(", "box->initState = Land::MirroredState(g_invokeMailbox.initState,",
                "s_adoptedForwards[slot].load(", "ServeOne(box, warnedThrow)");
        // One poller's mailbox is not another's: a copy can run two (a stop that gave up, then a restart).
        Assert.Contains("static thread_local MailboxData* s_box", mimic);
    }

    /// <summary>The layout pin holds whatever MAILBOX_LAYOUT_SINCE says: moving it fails the build until the pin is
    /// restated for the new layout (a conditional pin would switch itself off).</summary>
    [Fact]
    public void TheMailboxLayoutPinIsUnconditional()
    {
        string h = CodeOnly(DllSrc("Mimic.h"));
        Assert.Matches(@"static_assert\(MAILBOX_LAYOUT_SINCE == 3 &&\s*\(?sizeof\(MailboxData\) == 0x730", h);
        Assert.DoesNotContain("MAILBOX_LAYOUT_SINCE != 3", h);
    }

    /// <summary>A copy whose magic a later copy cleared (it could not serve that mailbox) is named as a take-over, with
    /// advice that works: re-injecting or re-ticking resolves the same copy again. The branch comes before the
    /// stale-address one, in the generated check and in both Lua helpers.</summary>
    [Fact]
    public void TheContractChecksNameATakeOver()
    {
        var sb = new System.Text.StringBuilder();
        UE5DumpUI.Services.CeLuaHygiene.AppendContractCheck(sb, "T");
        string gen = sb.ToString();
        InOrder(gen, "the generated contract check", "if _cmagic == 0 then", "_tm >= 1 and _tc >= _tm",
                "another copy of UE5Dumper took over this game process",
                // Either copy can be the stale one: the later copy takes over even when it is older.
                "of the two copies is out of date", "the proxy DLL in the game folder or the DLL injected later",
                "if _cmagic ~= ");
        foreach (string helper in new[] { "ue5_invoke_helper.lua", "ue5_freeze_helper.lua" })
        {
            string lua = File.ReadAllText(NumericInputCoercionTests.RepoFile("scripts/" + helper)).Replace("\r\n", "\n");
            InOrder(lua, helper, "if magic == 0 then", "m >= 1 and c >= m", "another copy of UE5Dumper took over this game process",
                    "one of the two copies is out of date", "if magic ~= 1127564629 then");
        }
    }

    /// <summary>The yield is listed in all four proxy .def files. A proxy exports it either way -- every proxy links the
    /// shared objects, and /DEF merges with __declspec(dllexport) rather than replacing it (ProxyDxgi.def's header) -- so
    /// this is the list check_proxy_exports and CE's getAddress readers see, kept complete.</summary>
    [Fact]
    public void EveryProxyListsTheYield()
    {
        foreach (string def in new[] { "ProxyVersion.def", "ProxyDinput8.def", "ProxyDxgi.def", "ProxyWinmm.def" })
        {
            Assert.Matches(@"(?m)^\s*UE5_YieldToLaterCopy\s*$", DllSrc(def));
            Assert.Matches(@"(?m)^\s*UE5_HandOverHiddenActors\s*$", DllSrc(def));
        }
    }

    /// <summary>[SEETHRU-HANDOFF-LEFTOVER] An adoption trusts a deferred restore that is already running, so that
    /// restore's last pass must not decide it is done from its own snapshot -- actors adopted during the pass would be
    /// left with no restore waiting, while the card promises they reappear. The loop decides from the record, under the
    /// lock an adoption takes, and clears its running flag in that same section: an adoption lands before the decision
    /// (another pass takes it) or sees no restore running (and starts one). A new deferred restore clears an earlier
    /// "gave up".</summary>
    [Fact]
    public void ADeferredRestoreTakesWhatWasAdoptedDuringItsPass()
    {
        string schlacht = CodeOnly(DllSrc("Schlacht.cpp"));
        string loop = FunctionBody(schlacht, "void PendingRestoreLoop() {");
        Assert.DoesNotContain("done = kept.empty();", loop);
        // Each flag store INSIDE the section that decides the exit -- moved after its closing brace, or left to the store
        // at the loop's end, it races an adoption. One regex per section, braces included.
        Assert.Matches(@"std::lock_guard<std::mutex> lk\(s_mutex\);\s*nothingToDo = s_state\.active \|\| " +
                       @"s_state\.hiddenActors\.empty\(\);\s*if \(nothingToDo\) s_pendingRunning\.store\(false\); \}", loop);
        Assert.Matches(@"\{\s*std::lock_guard<std::mutex> lk\(s_mutex\);\s*RecordUnhideLeftoversLocked\(kept\);\s*" +
                       @"done = s_state\.active \|\| s_state\.hiddenActors\.empty\(\);\s*left = s_state\.hiddenActors\.size\(\);\s*" +
                       @"if \(done\) s_pendingRunning\.store\(false\);\s*\}", loop);
        // It gives up only on this pass's own failures; actors adopted while it ran were never tried.
        Assert.Contains("if (!done && !kept.empty() && waited >= Grimoire::PENDING_RESTORE_MAX_MS)", loop);
        InOrder(FunctionBody(schlacht, "bool StartPendingLocked() {"), "a new restore",
                "if (Tot::ShutdownRequested()) return false;", "s_state.restoreAbandoned = false;",
                "s_pendingWorker = std::thread(PendingRestoreLoop);");
    }

    /// <summary>[SEETHRU-HANDOFF-LEFTOVER] KeepAddedDuringTick is tested as a helper; this pins its call in Tick, since
    /// Schlacht.cpp is in no C++ test target. Under the write-back's lock, before the record is replaced: after the
    /// assignment it would merge the record with itself.</summary>
    [Fact]
    public void ATickKeepsWhatWasAdoptedWhileItRan()
    {
        InOrder(FunctionBody(CodeOnly(DllSrc("Schlacht.cpp")), "void Tick() {"), "the tick",
                "old = s_state.hiddenActors;", "std::lock_guard<std::mutex> lk(s_mutex);",
                "KeepAddedDuringTick(applied, s_state.hiddenActors, old);", "s_state.hiddenActors = std::move(applied);");
    }

    /// <summary>[WRITEMEM-ANY-COPY] The helper is tested elsewhere; this pins the call site, since Fern.cpp is in no C++
    /// test target. A background client's write_mem asks about every copy's image before it classifies the
    /// range, so a write into the earlier copy's adopted mailbox never reaches the classification.</summary>
    [Fact]
    public void AnAiWriteMemIsRefusedInsideAnyCopysImage()
    {
        string fern = CodeOnly(DllSrc("Fern.cpp"));
        int at = fern.IndexOf("if (conn->background.load() && Macht::RangeInAnyDumperImage(addr, bytes.size()))",
                              StringComparison.Ordinal);
        Assert.True(at > 0, "write_mem's any-copy refusal not found -- re-point this pin");
        int classify = fern.IndexOf("Macht::ClassifyWriteRange(addr, bytes.size())", at, StringComparison.Ordinal);
        Assert.True(classify > at, "the any-copy refusal must precede the classification");
        Assert.Contains("Grimoire::WRITE_MEM_OWN_IMAGE", fern[at..classify]);
        Assert.DoesNotContain("RangeInOwnImage(addr", fern);
    }

    /// <summary>[SEETHRU-HANDOFF-LEFTOVER] The hand-off's See-through OFF keeps on a stalled
    /// game thread what it could not un-hide, "for the next ON" -- which a copy that handed over never gets. The later
    /// copy takes that record after a hand-off it began, before anything else can take this copy over; the earlier copy
    /// answers only once it has handed over, from its own record.</summary>
    [Fact]
    public void TheLaterCopyTakesOverTheActorsSeeThroughLeftHidden()
    {
        string land = CodeOnly(DllSrc("Land.cpp"));
        // Asked after "already handing over" too -- that copy is still handing over, to a copy
        // this one took over -- and asked again until it has, bounded.
        string retire = FunctionBody(land, "DWORD WINAPI RetireThread(LPVOID param) {");
        InOrder(retire, "the hand-off thread", "reply == YIELD_NOT_LATER", "PollHiddenActors(call->hidden);",
                "RefusesStart(State())", "TakeMailbox(");
        Assert.DoesNotContain("reply == YIELD_DONE) AdoptHiddenActors", retire);
        Assert.Contains("call->hidden = GetProcAddress(h, Grimoire::DUMPER_HAND_OVER_HIDDEN_EXPORT);", land);
        // Under the start lock BeginYield takes, and only while this copy is not being taken over: a copy that hands
        // over never serves its record again, so it leaves the actors with the earlier copy for the copy taking over.
        InOrder(FunctionBody(land, "HiddenAsk AdoptHiddenActors(FARPROC handOver) {"), "the adoption",
                "LockStart()", "if (RefusesStart(State())) return HiddenAsk::Left;", "handOver)(",
                "Schlacht::AdoptLeftovers(");
        InOrder(FunctionBody(land, "void PollHiddenActors(FARPROC handOver) {"), "the poll",
                "AdoptHiddenActors(handOver) == HiddenAsk::NotYet", "Grimoire::LAND_HIDDEN_WAIT_MS",
                "Tot::ShutdownRequested()", "Sleep(Grimoire::LAND_HIDDEN_POLL_MS)");
        string frieren = CodeOnly(DllSrc("Frieren.cpp"));
        InOrder(FunctionBody(frieren, "int32_t UE5_HandOverHiddenActors(uint64_t* out, int32_t cap) {"), "the hand-over",
                "if (Land::State() != Land::YieldState::Yielded) return -1;", "Schlacht::TakeLeftovers(out, cap)");
        string schlacht = CodeOnly(DllSrc("Schlacht.cpp"));
        // Adopted while off, they get this copy's deferred restore, as after an OFF
        // that could not finish -- so the card's "they reappear" and "gave up" are true for them; "gave up" is set only
        // when no restore can wait (a shutdown), not for every adoption.
        InOrder(FunctionBody(schlacht, "void AdoptLeftovers(const uint64_t* actors, int32_t n) {"), "the adoption",
                "std::lock_guard<std::mutex> wk(s_workerMutex);", "s_state.hiddenActors.insert(",
                "if (!active && !StartPendingLocked())", "s_state.restoreAbandoned = true;");
        InOrder(FunctionBody(schlacht, "int32_t TakeLeftovers(uint64_t* out, int32_t cap) {"), "the record's move",
                "std::lock_guard<std::mutex> lk(s_mutex);", "if (s_state.active) return 0;",
                "TakeHiddenRecord(s_state.hiddenActors, s_state.unhideQueued, out, cap)");
    }
}
