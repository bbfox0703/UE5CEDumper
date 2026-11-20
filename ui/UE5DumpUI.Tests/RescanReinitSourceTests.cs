using System.Text.RegularExpressions;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// [RESCAN-DECOY-REINIT] Extra Scan replaces a published GObjects that reads no objects. The choice and the re-init's
/// order are unit-tested in dll_core_test / dll_helpers_test (Genau::PlanRescan, PlanRescanApply,
/// ChooseGObjectsCandidate, ReplaceGObjectsAndReprobe); Fern.cpp, Frieren.cpp, Routine.h's loop, Dunste, Schlacht and
/// Solitar reach no C++ test target, so their wiring is pinned here from the source.
/// </summary>
public sealed class RescanReinitSourceTests
{
    private static string DllSrc(string file) =>
        File.ReadAllText(NumericInputCoercionTests.RepoFile("dll/src/" + file)).Replace("\r\n", "\n");

    private static string Between(string src, string from, string to)
    {
        int a = src.IndexOf(from, StringComparison.Ordinal);
        Assert.True(a >= 0, $"'{from}' not found");
        int b = src.IndexOf(to, a + from.Length, StringComparison.Ordinal);
        Assert.True(b > a, $"'{to}' not found after '{from}'");
        return src[a..b];
    }

    private static void InOrder(string body, params string[] marks)
    {
        int at = -1;
        foreach (string m in marks)
        {
            int next = body.IndexOf(m, at + 1, StringComparison.Ordinal);
            Assert.True(next > at, $"'{m}' is missing or out of order");
            at = next;
        }
    }

    [Fact]
    public void Rescan_DecidesFromThePublishedPointers_UnderTheInitLock()
    {
        // The decision reads Aura's count, which an apply's scoring moves candidate by candidate: deciding and starting
        // under the lock the apply fence holds keeps a rescan from reading a candidate's count.
        var rescan = Between(DllSrc("Fern.cpp"), "if (cmd == Renge::CMD_RESCAN) {", "if (cmd == Renge::CMD_RESCAN_STATUS) {");
        InOrder(rescan, "if (m_scan.job.Running() || g_initInProgress.load(std::memory_order_acquire)) {",
                "FrierenInit::TryHoldInit()", "if (!initHold.held) {", "return Renge::MakeError(",
                "Genau::PlanRescan(g_cachedGObjects, g_cachedGNames, g_cachedGWorld, Aura::GetCount())",
                "m_rescan.job.TryStart(");
        Assert.Contains("RunRescanBody(plan.seekGObjects, plan.seekGWorld, plan.replace, plan.avoid, plan.maxCandidates)", rescan);
        Assert.Contains("data[\"replacing\"]", rescan);
    }

    [Fact]
    public void RescanJob_KeepsTheFirstHitForAFill_AndCollectsForAReplace()
    {
        // A fill calls ExtraScanGObjects itself: its result is unchanged, and the R7 staging recipes patch that function.
        var body = Between(DllSrc("Fern.cpp"), "void Fern::RunRescanBody(", "std::string Fern::StartWatch(");
        Assert.Contains("Genau::ExtraScanGObjects()", body);
        Assert.Contains("Genau::ExtraScanGObjectsCandidates(avoid, maxCandidates, candidates", body);
        Assert.Contains("m_rescan.result.gobjectsCandidates = candidates;", body);
    }

    [Fact]
    public void ApplyRescan_PlansUnderTheFence_ReplacesInsideIt_AndChecksGWorldAfter()
    {
        var fern = DllSrc("Fern.cpp");
        var apply = Between(fern, "if (cmd == Renge::CMD_APPLY_RESCAN) {", "if (cmd == Renge::CMD_TRIGGER_SCAN) {");
        // A client that drops mid-apply does not cut the re-init short.
        InOrder(apply, "Tot::CancelContextScope immune{Tot::CancelContext{nullptr, true}}", "FrierenInit::BeginApply()");
        // Planned under the fence, after a re-check that no rescan started on another connection since the first one.
        InOrder(apply, "FrierenInit::BeginApply()", "if (m_rescan.job.Running())", "m_rescan.Results()",
                "Genau::PlanRescanApply(", "m_rescan.MarkConsumed();", "FrierenInit::ReplaceDecoyGObjects(",
                "// the init fence ends here", "bool gworldRecovered = false;",
                "kind == Genau::RescanApply::Fill && Aura::GetCount() > 0",
                "FrierenInit::CheckAndRecoverGWorld(\"apply_rescan\")",
                "Flamme::UpdateGObjectsMethod(g_cachedPeHash, \"data_heuristic\")", "data[\"gobjects_outcome\"]");
        // A fill: an empty pool latched UFunction::Func and UEnum names, which its first probe re-detects.
        InOrder(apply, "Aura::Init(g_cachedGObjects);", "Aura::ResetUFunctionFuncDetection();");
        InOrder(apply, "if (!Genau::ValidateAndFixOffsets(g_cachedUEVersion)) {", "Genau::ResetUEnumNamesDetection();",
                "FrierenInit::CorrectVersionStructurally(");
        // A replace ran the probe and the GEngine second pass in its own order: the fill's blocks do not run again.
        Assert.Matches(@"kind != Genau::RescanApply::Replace && g_cachedGObjects && g_cachedGNames\s*&& !DynOff::bOffsetsProbeRan", apply);
        Assert.Contains("if (kind != Genau::RescanApply::Replace && g_cachedGObjects && g_cachedGNames) {", apply);
    }

    [Fact]
    public void ReplaceDecoyGObjects_PublishesAsInitsRecoveriesDo_AndClimbsFromThePreLadderVersion()
    {
        var frieren = DllSrc("Frieren.cpp");
        var body = Between(frieren, "Genau::ReplaceResult ReplaceDecoyGObjects(", "\n}\n");
        Assert.Contains("Genau::ReplaceGObjectsAndReprobe(", body);
        foreach (string clear in new[] { "g_cachedGObjectsPatternId = nullptr;", "g_cachedGObjectsExport = nullptr;",
                                         "g_cachedGObjectsAdjustment = 0;", "g_cachedGObjectsScanAddr = 0;",
                                         "g_cachedGObjectsMethod = \"data_heuristic\";" })
            Assert.Contains(clear, body);
        Assert.Contains("g_cachedIsLowConfidence = g_cachedPreLadderLowConfidence;", body);
        Assert.Contains("CorrectVersionStructurally(g_cachedPreLadderUEVersion, g_cachedIsUserOverride, \"apply_rescan\")", body);
        Assert.Contains("Genau::ResolveGEngineDeferred(", body);
    }

    [Fact]
    public void Init_RecordsThePreLadderVersion_AndSharesItsGWorldCheck()
    {
        var frieren = DllSrc("Frieren.cpp");
        var init = Between(frieren, "bool UE5_Init() {", "\n}\n");
        Assert.Contains("g_cachedPreLadderUEVersion = ptrs.UEVersion;", init);
        Assert.Contains("g_cachedPreLadderLowConfidence = ptrs.bLowConfidence;", init);
        // The lock is also held, briefly, by an apply_rescan and by pipe commands that read what init settles; the
        // wait says so, and keeps the words tools/verify/b5_mailbox_race.py and l54_init_fastpath.py count.
        Assert.Contains("init already in progress on another thread", init);
        Assert.Contains("an apply_rescan or a pipe command", init);
        Assert.Contains("resumed after waiting", init);
        InOrder(init, "FrierenInit::CorrectVersionStructurally(", "FrierenInit::CheckAndRecoverGWorld(\"UE5_Init\")",
                "ptrs.GWorld = g_cachedGWorld;");
        // One GWorld recovery, shared by init and apply_rescan.
        Assert.Single(Regex.Matches(frieren, @"Genau::RecoverGWorldViaEngine\(\)"));
    }

    [Fact]
    public void VersionWriters_HoldTheInitLock()
    {
        // An override set during a replace's re-init would be overwritten by its climb; a lazy refine computed from a
        // read taken before the replace would bring back the version it replaced.
        var fern = DllSrc("Fern.cpp");
        var over = Between(fern, "if (cmd == Renge::CMD_SET_UE_VERSION_OVERRIDE) {", "json data;\n            data[\"ue_version\"]");
        InOrder(over, "FrierenInit::TryHoldInit()", "if (!initHold.held) {", "return Renge::MakeError(",
                "Ubel::CheckVersionOverrideTail(");
        Assert.Equal(2, Regex.Matches(over, @"g_cachedPreLadderUEVersion\s*=").Count);
        var get = Between(DllSrc("Frieren.cpp"), "uint32_t UE5_GetVersion() {", "\n}\n");
        Assert.Contains("const uint32_t current = g_cachedUEVersion;", get);
        Assert.Contains("std::unique_lock<std::mutex> lk(s_initMutex, std::try_to_lock);", get);
    }

    [Fact]
    public void ProcessEventDetection_AndHookInstall_WaitOutAnInit()
    {
        // While an init or an apply moves Aura candidate by candidate, a detection would read a candidate's objects as
        // vtables and could latch the terminal failure; neither may spend its budget or stamp its cooldown then.
        var frieren = DllSrc("Frieren.cpp");
        var detect = Between(frieren, "static void RunPeDetection(bool force) {", "\n}\n");
        InOrder(detect, "g_initInProgress.load(", "s_lastPeDetectMs.store(");
        var install = Between(frieren, "static void TryInstallGameThreadHook(bool force = false) {", "\n}\n");
        InOrder(install, "g_initInProgress.load(", "s_hookAttempts.fetch_add(");
    }

    [Fact]
    public void ReassertWorkers_SkipATickWhileAnInitRuns()
    {
        var skip = new Regex(@"if \((::)?g_initInProgress\.load\(std::memory_order_acquire\)\) continue;");
        static string Code(string s) => Regex.Replace(s, @"//[^\n]*", "");
        Assert.Matches(skip, Code(Between(DllSrc("Routine.h"), "inline void ReassertLoop(", "\n}\n")));
        Assert.Matches(skip, Code(Between(DllSrc("Dunste.cpp"), "void WorkerLoop() {", "\n}\n")));
        Assert.Matches(skip, Code(Between(DllSrc("Schlacht.cpp"), "void WorkerLoop() {", "\n}\n")));
    }

    [Fact]
    public void AnEnumTableParsedAcrossAReset_IsNotCached()
    {
        // ResetUEnumNamesDetection rewrites the layout a reader may be parsing with; s_enumCache is never erased.
        var resolve = Between(DllSrc("Ubel.cpp"), "static std::string ResolveEnumValue(", "\n}\n");
        InOrder(resolve, "const uint32_t gen = DynOff::UENUM_DETECT_GEN.load(std::memory_order_acquire);",
                "bUEnumNamesDetected.load(", "ParseEnumTable(",
                "if (!tableComplete || DynOff::UENUM_DETECT_GEN.load(std::memory_order_acquire) != gen) {",
                "s_enumCache.emplace(");
        var reset = Between(DllSrc("Genau.cpp"), "void ResetUEnumNamesDetection() {", "\n}\n");
        InOrder(reset, "DynOff::bEnumNamesNewContainer = false;", "DynOff::UENUM_DETECT_GEN.fetch_add(1");
    }

    [Fact]
    public void GodModeTargets_AreKeyedByTheFamilyEpoch()
    {
        // A replace's probe re-measures the offsets the cached targets were read under; the epoch it retires is the key.
        var solitar = DllSrc("Solitar.cpp");
        var apply = Between(solitar, "int32_t ApplyGodNowLocked(", "\n}\n");
        InOrder(apply, "const uintptr_t targetsKey = DynOff::FamilyCacheKey(p.pawnClass);", "BuildTargets(p.pawnClass, verbose);",
                "s_targetsKey = targetsKey;");
        Assert.DoesNotContain("s_targetsClass", solitar);
    }
}
