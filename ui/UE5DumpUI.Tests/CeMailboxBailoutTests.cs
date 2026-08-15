using System;
using System.Collections.Generic;
using System.Linq;
using UE5DumpUI.Models;
using UE5DumpUI.Services;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// One rule, asserted once for every generator that talks to the Mimic mailbox —
/// deliberately NOT six near-identical per-generator tests, because six copies of the
/// rule is exactly how the defect got in.
///
/// <para><b>The rule.</b> An <c>[ENABLE]</c> block that returns without applying anything
/// MUST untick the record. Cheat Engine leaves the row ticked otherwise, so the user is
/// told a cheat is active when nothing was set. Every generator got this right on the
/// "mailbox not found" path and every one got it wrong on the "timeout" path and on the
/// "the DLL answered with an error" path.</para>
///
/// <para>Two more properties are pinned here because they were wrong in all seven
/// hand-rolled copies of the wait loop:</para>
/// <list type="bullet">
/// <item>the timeout must be a REAL deadline. The loop used to count <c>sleep(1)</c>
/// iterations and bail at 10000, which is 10 s only if <c>sleep(1)</c> sleeps 1 ms —
/// measured in CE's Lua Engine on 2026-08-06 it sleeps <b>15.47 ms</b>, so the real
/// timeout was ~155 s of frozen Lua Engine.</item>
/// <item>the message must READ the status rather than guess. "(DLL not responding?)" was
/// a guess; a timeout with <c>status == 0</c> means the DLL never saw the command at all
/// (measured on ES2 with the DLL's poller demonstrably alive at 1 ms).</item>
/// </list>
/// </summary>
public class CeMailboxBailoutTests
{
    /// <summary>Every mailbox-driven ENABLE/DISABLE script, by name so a failure says which.</summary>
    public static IEnumerable<object[]> MailboxScripts() => new List<object[]>
    {
        new object[] { "Movement.WalkSpeed",  MovementScriptGenerator.Generate(MovementScriptGenerator.Knob.WalkSpeed, 150) },
        new object[] { "Movement.Gravity",    MovementScriptGenerator.Generate(MovementScriptGenerator.Knob.Gravity, 50) },
        new object[] { "Movement.GravityDir", MovementScriptGenerator.GenerateGravityDirection(0, 0, -1) },
        new object[] { "Protection",          ProtectionScriptGenerator.Generate() },
        new object[] { "SeeThrough",          SeeThroughScriptGenerator.Generate() },
        new object[] { "Foreground",          ForegroundScriptGenerator.Generate() },
        new object[] { "DebugCamera",         DebugCameraScriptGenerator.Generate() },
        new object[] { "Fly.Enabled",         FlyScriptGenerator.Generate(FlyScriptGenerator.FlyToggle.Enabled, 0) },
        new object[] { "Fly.Noclip",          FlyScriptGenerator.Generate(FlyScriptGenerator.FlyToggle.Noclip) },
        new object[] { "TimeDilation.Global", TimeDilationScriptGenerator.Generate(TimeDilationScriptGenerator.Target.Global, 0.5) },
        new object[] { "TimeDilation.Pawn",   TimeDilationScriptGenerator.Generate(TimeDilationScriptGenerator.Target.Pawn, 2.0) },
    };

    /// <summary>The [ENABLE] half only — [DISABLE] has no cheat to leave falsely ticked.</summary>
    private static string EnableBlock(string script)
    {
        int a = script.IndexOf("[ENABLE]", StringComparison.Ordinal);
        Assert.True(a >= 0, "no [ENABLE] block");
        int b = script.IndexOf("[DISABLE]", a, StringComparison.Ordinal);
        return b < 0 ? script[a..] : script[a..b];
    }

    // ── The rule ────────────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(MailboxScripts))]
    public void EveryEnableBailout_UnticksTheRecord(string name, string script)
    {
        var lines = EnableBlock(script).Split('\n');

        // Counting messages does NOT work — the shared timeout branch has three
        // alternative messages (idle / processing / unexpected) sharing one untick, so a
        // count says "3 bail-outs, 1 untick" and is wrong. Scan structurally instead.
        bool sawBailout = false;

        for (int i = 0; i < lines.Length; i++)
        {
            // (a) Every error message must reach an untick before control leaves its
            // branch. The window covers an if/elseif/else trio plus its `end`.
            if (lines[i].Contains("showMessage(", StringComparison.Ordinal))
            {
                sawBailout = true;
                Assert.True(Within(lines, i + 1, 8, "memrec.Active = false"),
                    $"{name}: line {i + 1} reports a failure but no untick follows it — " +
                    $"the CE row stays ticked with nothing applied:\n  {lines[i].Trim()}");
            }

            // (b) Every early return must already have unticked. The syntaxcheck guard is
            // not a bail-out — it runs before anything is attempted.
            string t = lines[i].Trim();
            if (t == "return" && !lines[i].Contains("syntaxcheck", StringComparison.Ordinal))
            {
                Assert.True(Back(lines, i - 1, 4, "memrec.Active = false"),
                    $"{name}: the return on line {i + 1} leaves the block without unticking");
            }
        }

        Assert.True(sawBailout,
            $"{name}: no ENABLE bail-out at all — the harness is not reaching the code it claims to check");
    }

    private static bool Within(string[] lines, int from, int span, string needle)
    {
        for (int i = from; i < Math.Min(lines.Length, from + span); i++)
            if (lines[i].Contains(needle, StringComparison.Ordinal)) return true;
        return false;
    }

    private static bool Back(string[] lines, int from, int span, string needle)
    {
        for (int i = from; i >= Math.Max(0, from - span); i--)
            if (lines[i].Contains(needle, StringComparison.Ordinal)) return true;
        return false;
    }

    [Theory]
    [MemberData(nameof(MailboxScripts))]
    public void TheOldGuessingTimeoutMessage_IsGone(string name, string script)
    {
        // It blamed the DLL for the far more common "the DLL never saw it" case and sent
        // at least one real user to inspect a healthy DLL.
        Assert.False(script.Contains("(DLL not responding?)", StringComparison.Ordinal),
            $"{name}: still emits the guess that blamed a healthy DLL");
    }

    [Theory]
    [MemberData(nameof(MailboxScripts))]
    public void TheTimeoutReadsTheStatus_InsteadOfGuessing(string name, string script)
    {
        string enable = EnableBlock(script);
        // status 0 = never picked up; 0xFF = picked up and wedged. Both must be named,
        // because they send the user to completely different places.
        Assert.True(enable.Contains("never saw this command", StringComparison.Ordinal),
            $"{name}: does not name the status-0 case (the DLL never picked it up)");
        Assert.True(enable.Contains("never finished it", StringComparison.Ordinal),
            $"{name}: does not name the status-PROCESSING case (the DLL wedged)");
        Assert.Contains($"_st == {CeMailboxLayout.StatusProcessing}", enable, StringComparison.Ordinal);

        // nil is the THIRD case and it is not a status at all: readInteger returns nil once
        // the target process is gone. It must be named first, because `nil == 0` is false in
        // Lua so it would otherwise fall into the catch-all and tell the user its status was
        // "nil". It must also short-circuit the deadline — waiting 10 s to report a process
        // that already exited helps nobody. Found on Elliot 2026-08-07 by closing the game.
        Assert.Contains("if _st == nil then", enable, StringComparison.Ordinal);
        Assert.Contains("the mailbox could not be read", enable, StringComparison.Ordinal);
        Assert.Contains("local _over = _st == nil or", enable, StringComparison.Ordinal);

        // The contract check sits AHEAD of both wait loops, so its own unreadable case is
        // the first thing a user hits — and it had the same defect: `nil ~= MAGIC` is true,
        // so it reported "stale address" for a mailbox it simply could not read. Reading
        // once into _cmagic is what lets the two be told apart.
        Assert.Contains("local _cmagic = readInteger(_cv + ", enable, StringComparison.Ordinal);
        Assert.Contains("if _cmagic == nil then", enable, StringComparison.Ordinal);
        Assert.Contains("could not be READ", enable, StringComparison.Ordinal);
        // _want < _min against a nil raises instead of diagnosing.
        Assert.Contains("if _cur == nil or _min == nil then", enable, StringComparison.Ordinal);

        // Both waits must keep Cheat Engine's window alive. CE's Lua sleep is a bare Win32
        // Sleep, so a 10 s timeout froze CE for 10 s -- reported from a live session. The
        // pump is FEATURE-TESTED, not version-gated: processMessagesPaintOnly is absent from
        // the whole 7.5 source tree yet present in the 7.7 binary, so the introducing
        // version is unknown, and CE's own docs prefer it because it ignores mouse and
        // keyboard and therefore cannot re-enter the script through a second click.
        Assert.Contains("type(processMessagesPaintOnly) == 'function'", enable,
            StringComparison.Ordinal);
        // Only _pump here: the idle wait is emitted by the helper-shaped generators and by
        // Teleport, not by the flat toggles this theory covers.
        Assert.Contains("_pump()", enable, StringComparison.Ordinal);

        // The symbol-lookup branch sits above all of those and had the same defect in its
        // strongest form: getAddressSafe returns nil for EVERY reason a symbol can fail to
        // resolve, and the message asserted the rarest one. Verified 2026-08-07 against a
        // DLL that exports g_mailboxContract (dumpbin ordinal 64) while CE sat on a stale
        // PID -- the user was told to rebuild a DLL that was already correct.
        Assert.Contains("could not resolve g_mailboxContract", enable, StringComparison.Ordinal);
        // The self-heal is the point, not the message: CE snapshots the module list when it
        // OPENS a process, and "open the process, then inject" is the NORMAL flow (the
        // UE5CEDumper (DLL) record injects from inside CE), so the symbol table is stale by
        // construction. Retrying after reinitializeSymbolhandler() fixes the common case
        // instead of reporting it.
        Assert.Contains("reinitializeSymbolhandler()", enable, StringComparison.Ordinal);
        Assert.Contains("even after re-reading the module list", enable, StringComparison.Ordinal);

        // The result code is a SIGNED int32 and CE's readInteger defaults to UNSIGNED, so
        // every `if state < 0 then` error branch in these scripts was DEAD CODE: -1 reads
        // back as 4294967295 and a DLL-side failure was reported as success. Surfaced by a
        // Coordinate Library teleport printing "code 4294967295" (2026-08-07); the display
        // was the visible half, the dead branch was the dangerous one.
        Assert.DoesNotContain($"readInteger(mb + {CeMailboxLayout.OffResult})", enable,
            StringComparison.Ordinal);
        // Negative control: the claim that was false must be gone, not merely reworded.
        Assert.DoesNotContain("is older than this script (no contract symbol)", enable,
            StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(MailboxScripts))]
    public void TheTimeoutIsARealDeadline_NotAnIterationCount(string name, string script)
    {
        // getTickCount() was probed in CE's Lua Engine on 2026-08-06 and returns ms.
        Assert.True(script.Contains("getTickCount", StringComparison.Ordinal),
            $"{name}: timeout is not measured against a real clock");
        Assert.Contains($"_t0 >= {CeMailboxLayout.MailboxPollTimeoutMs}", script, StringComparison.Ordinal);

        // The pre-fix shape must not survive anywhere: `elapsed` counted sleep(1) calls
        // and compared them to a millisecond constant.
        Assert.False(script.Contains("elapsed >= ", StringComparison.Ordinal),
            $"{name}: the pre-fix iteration counter survived");
    }

    [Theory]
    [MemberData(nameof(MailboxScripts))]
    public void TheAutoCloseStaysUnreachableOnEveryBailout(string name, string script)
    {
        // "A timeout is an error, so return (not break)" — the close must never be
        // reachable from a bail-out. Proxy: the wait loop bails with `return`.
        string enable = EnableBlock(script);
        int loop = enable.IndexOf("while _st ~=", StringComparison.Ordinal);
        Assert.True(loop >= 0, $"{name}: no shared wait loop — did this generator stop using CeLuaHygiene?");
        int close = enable.IndexOf(CeLuaHygiene.CloseCall, StringComparison.Ordinal);
        if (close >= 0)
            Assert.True(close > loop, $"{name}: the success-close is emitted before the wait loop");
        Assert.DoesNotContain("then break end", enable, StringComparison.Ordinal);
    }

    // ── The contract check ──────────────────────────────────────────────────
    //
    // Versioned on the CONTRACT (mailbox offsets / Cmd / per-command ops / status
    // meanings), never on the build number — a .CT saved months ago has to keep working
    // against a newer DLL that changed nothing it depends on. tools/check_mailbox_contract.py
    // is what stops the version going stale; these pin the SCRIPT half.

    [Theory]
    [MemberData(nameof(MailboxScripts))]
    public void EveryScriptChecksTheContractBeforeWritingAnything(string name, string script)
        => AssertChecksTheContractBeforeWriting(name, script);

    [Theory]
    [MemberData(nameof(MailboxScripts))]
    public void TheContractCheckNamesBothFailureDirections(string name, string script)
        => AssertNamesBothFailureDirections(name, script);

    [Theory]
    [MemberData(nameof(MailboxScripts))]
    public void AFailedContractCheckUnticksTheRecord(string name, string script)
        => AssertAFailedCheckUnticksTheRecord(name, script);

    // The three rules live in ONE place each, because the shapes below assert exactly the
    // same three things about differently-shaped scripts and two copies of a rule is how
    // this file's own header says the original defect got in.

    private static void AssertChecksTheContractBeforeWriting(string name, string script)
    {
        string enable = EnableBlock(script);

        int check = enable.IndexOf(CeMailboxLayout.ContractSymbol, StringComparison.Ordinal);
        Assert.True(check >= 0, $"{name}: never reads the contract symbol");

        // Ordering is the whole point. If the layout moved, a write lands on whatever now
        // occupies those offsets — so the check has to come before the FIRST write, not
        // merely somewhere in the block.
        //
        // Matched on the actual write CALLS, not on the substring "write": these scripts
        // carry header comments that use the word (e.g. "cmd (write LAST to trigger)"),
        // and the first version of this test tripped on those instead of on real writes.
        //
        // TEXTUAL position, deliberately, even though what matters is execution order. A
        // write inside a `local function` does not run when the definition is emitted —
        // but a check placed after that definition is one refactor away from being after
        // the CALL too, and the text is the only thing a test can see. Emitting the check
        // above every write, helper or not, keeps the two orders the same thing.
        int w = new[] { "writeQword(", "writeInteger(", "writeDouble(", "writeBytes(", "writeByte(" }
            .Select(fn => enable.IndexOf(fn, StringComparison.Ordinal))
            .Where(i => i >= 0)
            .DefaultIfEmpty(-1)
            .Min();
        Assert.True(w < 0 || check < w,
            $"{name}: writes to the mailbox at offset {w} before checking the contract at {check}");
    }

    private static void AssertNamesBothFailureDirections(string name, string script)
    {
        string enable = EnableBlock(script);
        // The two directions need OPPOSITE advice, and today the second one is silent
        // corruption: a new .CT against an old DLL just writes nonsense.
        Assert.True(enable.Contains("too old for the DLL", StringComparison.Ordinal),
            $"{name}: does not tell the user to regenerate a too-old script");
        Assert.True(enable.Contains("DLL is older than this script", StringComparison.Ordinal),
            $"{name}: does not tell the user to update a too-old DLL");
        // And the stale-symbol case, which is a measured failure mode rather than a
        // theoretical one (2026-08-06: CE held a mailbox address the DLL no longer owned).
        Assert.Contains("stale address", enable, StringComparison.Ordinal);
    }

    private static void AssertAFailedCheckUnticksTheRecord(string name, string script)
    {
        // Even for momentary scripts, whose deferred untick timer does not exist yet at
        // this point in the block — a bare return there would strand the row ticked.
        string enable = EnableBlock(script);
        int check = enable.IndexOf(CeMailboxLayout.ContractSymbol, StringComparison.Ordinal);
        int untick = enable.IndexOf("memrec.Active = false", check, StringComparison.Ordinal);
        Assert.True(untick > check, $"{name}: a failed contract check leaves the record ticked");
    }

    [Fact]
    public void TheBakedContractVersionMatchesTheDll()
    {
        // The C# constant is what actually gets emitted, so it — not the C++ one — is what
        // a script claims. tools/check_mailbox_contract.py enforces the other half of this
        // (that the DLL agrees); here we only pin that the emitted number IS the constant.
        string script = ProtectionScriptGenerator.Generate();
        Assert.Contains($"local _want = {CeMailboxLayout.ContractVersion}", script, StringComparison.Ordinal);
    }

    // ── A THIRD shape: the round-trip lives inside a `local function` ────────
    //
    // PointerQuery's query(), CoordLibrary's call()/teleport(), Invoke's writeMbStr /
    // waitDone. That is the whole reason build 2743's conversion skipped these three:
    // the sweep looked for a wait loop in the block's own control flow and found none.
    // The contract check has no such excuse — it runs at CHUNK level in all three,
    // before the helper is even defined — so the same three rules apply verbatim.
    //
    // They are NOT in MailboxScripts because the BAIL-OUT theories above assume a flat
    // toggle and these legitimately end differently: the coordinate picker keeps its
    // window open after refusing a cross-map jump (nothing to untick — the record is
    // the window), and the invoke form unticks from OnClose rather than inline. Folding
    // them into MailboxScripts would mean weakening those theories for every generator
    // to accommodate three, which is the opposite trade.
    public static IEnumerable<object[]> HelperShapedScripts()
    {
        var coords = new[]
        {
            new CoordEntry
            {
                Uid = "u1", Label = "Chest 1", Group = "Chests", Map = "Map01",
                X = 1, Y = 2, Z = 3,
            },
        };

        return new List<object[]>
        {
            new object[] { "PointerQuery.GWorld",
                PointerQueryScriptGenerator.Generate(PointerQueryScriptGenerator.Target.GWorld) },
            new object[] { "PointerQuery.GameEngine",
                PointerQueryScriptGenerator.Generate(PointerQueryScriptGenerator.Target.GameEngine) },
            new object[] { "CoordLibrary.Dll",
                CoordLibraryScriptGenerator.Generate(
                    coords, CoordLibraryScriptGenerator.Flavour.Dll, out _) },
            new object[] { "Invoke.NoParams",
                InvokeScriptGenerator.Generate("Shop_C", "OpenShop",
                    new FunctionInfoModel { Name = "OpenShop", Params = new() }) },
            new object[] { "Invoke.WithParams",
                InvokeScriptGenerator.Generate("Shop_C", "Buy", new FunctionInfoModel
                {
                    Name = "Buy",
                    ParmsSize = 4,
                    Params = new List<FunctionParamModel>
                    {
                        new() { Name = "ItemId", TypeName = "IntProperty", Size = 4, Offset = 0 },
                    },
                }) },
        };
    }

    [Theory]
    [MemberData(nameof(HelperShapedScripts))]
    public void EveryHelperShapedScriptChecksTheContractBeforeWritingAnything(
        string name, string script)
        => AssertChecksTheContractBeforeWriting(name, script);

    [Theory]
    [MemberData(nameof(HelperShapedScripts))]
    public void TheHelperShapedContractCheckNamesBothFailureDirections(
        string name, string script)
        => AssertNamesBothFailureDirections(name, script);

    [Theory]
    [MemberData(nameof(HelperShapedScripts))]
    public void AFailedHelperShapedContractCheckUnticksTheRecord(string name, string script)
        => AssertAFailedCheckUnticksTheRecord(name, script);

    [Fact]
    public void TheCheckRunsBeforeTheHelperThatWritesIsEvenDefined()
    {
        // The distinction the three above cannot make on text position alone. In these
        // scripts the writes sit inside a closure that runs LATER — from a button click,
        // or a second query — so "before the first write" only means anything if the
        // check precedes the enclosing `local function`. Pinned per generator because
        // moving the check inside the helper would still satisfy the ordering assertion
        // for a script whose helper happens to be defined after it.
        foreach (var (name, script, helper) in new[]
        {
            ("PointerQuery", PointerQueryScriptGenerator.Generate(
                PointerQueryScriptGenerator.Target.GameEngine), "local function query("),
            ("CoordLibrary", CoordLibraryScriptGenerator.Generate(
                Array.Empty<CoordEntry>(), CoordLibraryScriptGenerator.Flavour.Dll, out _),
                "local function call("),
            ("Invoke", InvokeScriptGenerator.Generate("C", "F",
                new FunctionInfoModel { Name = "F", Params = new() }),
                "local function writeMbStr("),
        })
        {
            string enable = EnableBlock(script);
            int check = enable.IndexOf(CeMailboxLayout.ContractSymbol, StringComparison.Ordinal);
            int def = enable.IndexOf(helper, StringComparison.Ordinal);
            Assert.True(def >= 0, $"{name}: `{helper}` is gone — has the shape changed?");
            Assert.True(check >= 0 && check < def,
                $"{name}: the contract check does not precede `{helper}`");
        }
    }

    [Fact]
    public void TheNoDllCoordinatePickerHasNoContractCheck()
    {
        // Not an oversight, and the reason is worth pinning: the no-DLL flavour teleports
        // by raw memory write through the standalone trainer's baked offsets and never
        // touches g_invokeMailbox. There is no mailbox layout in question, so a contract
        // check there could only ever refuse to open a picker that would have worked.
        string script = CoordLibraryScriptGenerator.Generate(
            Array.Empty<CoordEntry>(), CoordLibraryScriptGenerator.Flavour.NoDll, out _);

        Assert.DoesNotContain(CeMailboxLayout.ContractSymbol, script, StringComparison.Ordinal);
        Assert.DoesNotContain("g_invokeMailbox", script, StringComparison.Ordinal);
    }

    // ── The OTHER script shape: momentary actions ───────────────────────────
    //
    // Teleport's rows fire once and self-untick from a deferred timer that also
    // suppresses the success-close. Applying the toggles' fix here would BREAK it: an
    // early `return` would skip that timer and strand the record ticked — which is
    // exactly what B15's comment in the generator warns about. So the shared emitter has
    // a second mode, and these tests pin the difference rather than assuming it.

    public static IEnumerable<object[]> MomentaryScripts() => new List<object[]>
    {
        new object[] { "Teleport.Save",   TeleportScriptGenerator.Generate(TeleportScriptGenerator.Action.Save, 0) },
        new object[] { "Teleport.Recall", TeleportScriptGenerator.Generate(TeleportScriptGenerator.Action.Recall, 1) },
    };

    [Theory]
    [MemberData(nameof(MomentaryScripts))]
    public void AMomentaryTimeout_FlagsAndBreaks_SoTheDeferredUntickStillRuns(string name, string script)
    {
        string enable = EnableBlock(script);

        int loop = enable.IndexOf("while _st ~=", StringComparison.Ordinal);
        Assert.True(loop >= 0, $"{name}: not using the shared wait loop");

        // Flag + break, NOT untick + return.
        int bail = enable.IndexOf("hadError = true", loop, StringComparison.Ordinal);
        Assert.True(bail > loop, $"{name}: the timeout does not set hadError");
        Assert.True(enable.IndexOf("break", bail, StringComparison.Ordinal) > bail,
            $"{name}: the timeout does not break out of the wait");

        // The deferred timer is what unticks; it must still be reached, and it must
        // suppress the auto-close when hadError.
        Assert.Contains("if memrec then memrec.Active = false end", enable, StringComparison.Ordinal);
        Assert.Contains($"if DEBUG == 0 and not hadError then {CeLuaHygiene.CloseCall} end",
                        enable, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(MomentaryScripts))]
    public void MomentaryScriptsGotTheSameTimeoutFixes(string name, string script)
    {
        Assert.False(script.Contains("elapsed >= ", StringComparison.Ordinal),
            $"{name}: still counts sleep(1) iterations against a millisecond constant");
        Assert.True(script.Contains("getTickCount", StringComparison.Ordinal),
            $"{name}: timeout is not measured against a real clock");
        Assert.Contains("never saw this command", script, StringComparison.Ordinal);
    }

    // ── The emitter is shared, so the text cannot drift again ────────────────

    [Fact]
    public void AllGeneratorsEmitByteIdenticalWaitLoops_ApartFromTheirTag()
    {
        // The whole point of moving this into CeLuaHygiene. Strip the bracketed tag and
        // every generator's loop must be the same text.
        var loops = MailboxScripts()
            .Select(row => (Name: (string)row[0], Loop: ExtractLoop((string)row[1])))
            .ToList();

        var normalised = loops
            .Select(x => (x.Name, Text: System.Text.RegularExpressions.Regex.Replace(x.Loop, @"\[[^\]]+\]", "[TAG]")))
            .ToList();

        var first = normalised[0];
        foreach (var other in normalised.Skip(1))
            Assert.True(first.Text == other.Text,
                $"{other.Name} drifted from {first.Name}:\n---{first.Name}---\n{first.Text}\n---{other.Name}---\n{other.Text}");
    }

    private static string ExtractLoop(string script)
    {
        string enable = EnableBlock(script);
        int a = enable.IndexOf("local _tick", StringComparison.Ordinal);
        Assert.True(a >= 0, "no shared wait loop found");
        // Up to and including the loop's closing `end`, which is the last line the
        // emitter writes.
        int b = enable.IndexOf("\nend\n", a, StringComparison.Ordinal);
        Assert.True(b >= 0, "wait loop is not terminated");
        return enable[a..(b + 5)];
    }
    /// <summary>A function WITH an input param, so the generator emits the picker-form
    /// path — where <c>waitDone</c> is called from inside <c>btnFire.OnClick</c> rather
    /// than from the chunk. That second frame is the whole reason this generator raises
    /// instead of returning.</summary>
    private static UE5DumpUI.Models.FunctionInfoModel IntParamFunc() => new()
    {
        Name = "AddMoney",
        ParmsSize = 4,
        Params = new List<UE5DumpUI.Models.FunctionParamModel>
        {
            new() { Name = "Amount", TypeName = "IntProperty", Size = 4, Offset = 0 },
        },
    };

    [Theory]
    [MemberData(nameof(HelperShapedScripts))]
    public void HelperShapedWaits_UseTheSharedLoop(string name, string script)
    {
        Assert.True(script.Contains("while _st ~= " + CeMailboxLayout.StatusDone, StringComparison.Ordinal),
            $"{name}: not using CeLuaHygiene.AppendMailboxWait — this is the file that hand-rolled it");
    }

    [Theory]
    [MemberData(nameof(HelperShapedScripts))]
    public void HelperShapedWaits_MeasureARealDeadline(string name, string script)
    {
        Assert.True(script.Contains("getTickCount", StringComparison.Ordinal),
            $"{name}: timeout is not measured against a real clock");
        Assert.Contains($"_t0 >= {CeMailboxLayout.MailboxPollTimeoutMs}", script, StringComparison.Ordinal);

        // The pre-fix shape, in each of the three spellings these files used. sleep(1)
        // costs 15.47 ms, so every one of them was a ~155 s timeout wearing a 10 s label.
        Assert.False(script.Contains("elapsed >= ", StringComparison.Ordinal),
            $"{name}: InvokeScriptGenerator's iteration counter survived");
        Assert.False(script.Contains("waited >= ", StringComparison.Ordinal),
            $"{name}: CoordLibrary/PointerQuery's iteration counter survived");
        Assert.False(script.Contains("idleWaited >= ", StringComparison.Ordinal),
            $"{name}: the idle wait's iteration counter survived");
    }

    [Theory]
    [MemberData(nameof(HelperShapedScripts))]
    public void HelperShapedWaits_ReadTheStatus_InsteadOfGuessing(string name, string script)
    {
        Assert.True(script.Contains("never saw this command", StringComparison.Ordinal),
            $"{name}: does not name the status-0 case (the DLL never picked it up)");
        Assert.True(script.Contains("never finished it", StringComparison.Ordinal),
            $"{name}: does not name the status-PROCESSING case (the DLL wedged)");
        Assert.False(script.Contains("(DLL not responding?)", StringComparison.Ordinal),
            $"{name}: still emits the guess that blamed a healthy DLL");
        // Invoke's own flavour of the same mistake: on a TIMEOUT the DLL never wrote
        // errorMsg, so that string is empty or stale from the previous command.
        Assert.False(script.Contains("Mailbox timeout: ' .. err", StringComparison.Ordinal),
            $"{name}: still reports a timeout from the mailbox's stale errorMsg string");
    }

    [Theory]
    [MemberData(nameof(HelperShapedScripts))]
    public void HelperShapedWaits_DoNotUntickFromInsideTheHelper(string name, string script)
    {
        // The defining property of this shape. PointerQuery's GameEngine record calls
        // query() for the &GEngine slot and falls back to a snapshot when that fails —
        // an untick inside the helper would kill the record on an attempt that is
        // ALLOWED to fail, and CoordLibrary's would kill it while the picker form is
        // still open. The callers own the untick, and they already do it.
        string region = string.Join('\n', ExtractBailStatements(script));
        Assert.False(region.Contains("memrec.Active = false", StringComparison.Ordinal),
            $"{name}: the helper unticks the record itself — its caller owns that decision:\n{region}");
    }

    /// <summary>
    /// The statements the timeout runs after building <c>_msg</c>, up to and including
    /// the one that leaves the wait. Walked line by line rather than sliced by a
    /// hard-coded <c>"\n  end\n"</c>: generators emit this block at three different
    /// indents, and an indent-sensitive slice silently ran past the end of Invoke's
    /// helper and into the NEXT bail-out's untick — reporting a violation in code the
    /// timeout never reaches.
    /// </summary>
    private static List<string> ExtractBailStatements(string script)
    {
        var lines = script.Split('\n');
        int i = Array.FindIndex(lines,
            l => l.Contains("_msg = 'timed out and the DLL never saw", StringComparison.Ordinal));
        Assert.True(i > 0, "no shared timeout reason found");

        // Walk to the last arm of the reason table, then past its closing `end`.
        while (i < lines.Length && !lines[i].Contains("tostring(_st)", StringComparison.Ordinal)) i++;
        Assert.True(i + 2 < lines.Length, "timeout-reason block is not terminated");
        i += 2;

        var bail = new List<string>();
        for (int j = i; j < lines.Length && bail.Count < 4; j++)
        {
            bail.Add(lines[j]);
            string t = lines[j].Trim();
            if (t is "return" or "break" || t.StartsWith("return nil", StringComparison.Ordinal)
                || t.StartsWith("error(", StringComparison.Ordinal)) break;
        }
        return bail;
    }

    /// <summary>
    /// Negative control for the test above. If <see cref="ExtractBailStatements"/> ever
    /// stops finding the real bail — mis-anchored, or fooled by an indent change — the
    /// "does not untick" assertion would pass over an empty or wrong region and prove
    /// nothing. The toggles DO untick in exactly that region, so this is the sample that
    /// makes a green "no untick" mean something.
    /// </summary>
    [Theory]
    [MemberData(nameof(MailboxScripts))]
    public void TheBailExtractorFindsTheUntickWhenThereIsOne(string name, string script)
    {
        string region = string.Join('\n', ExtractBailStatements(EnableBlock(script)));
        Assert.True(region.Contains("memrec.Active = false", StringComparison.Ordinal),
            $"{name}: the extractor missed a bail-out that DOES untick — every 'does not " +
            $"untick' assertion built on it is therefore vacuous:\n{region}");
    }

    [Theory]
    [MemberData(nameof(HelperShapedScripts))]
    public void HelperShapedWaits_CarryTheDiagnosisOutToTheCaller(string name, string script)
    {
        // Two carriers, and they are not interchangeable — see the MailboxTimeout docs.
        bool byValue = script.Contains("return nil, _msg", StringComparison.Ordinal);
        bool byRaise = script.Contains("error('[Invoke] ' .. _msg)", StringComparison.Ordinal);
        Assert.True(byValue || byRaise,
            $"{name}: the timeout diagnosis never reaches the caller — it is computed and dropped");
    }

    /// <summary>
    /// The reason table is ONE emitter for every shape. It used to be three inline
    /// <c>showMessage</c> literals, which meant a by-value or by-raise mode would have
    /// needed its own copy of "status 0 means X, 0xFF means Y" — and a second copy of
    /// that rule is precisely how the original defect reached seven files at once.
    /// </summary>
    [Fact]
    public void TheTimeoutReasonIsByteIdenticalAcrossEveryShape()
    {
        var all = MailboxScripts().Concat(MomentaryScripts()).Concat(HelperShapedScripts())
            .Select(row => (Name: (string)row[0], Reason: ExtractReason((string)row[1])))
            .ToList();

        var first = all[0];
        foreach (var other in all.Skip(1))
            Assert.True(first.Reason == other.Reason,
                $"{other.Name} drifted from {first.Name}:\n---{first.Name}---\n{first.Reason}\n---{other.Name}---\n{other.Reason}");
    }

    /// <summary>The <c>local _msg … end</c> block, de-indented so generators that emit it
    /// at different nesting depths still compare equal.</summary>
    private static string ExtractReason(string script)
    {
        int a = script.IndexOf("local _msg\n", StringComparison.Ordinal);
        Assert.True(a >= 0, "no shared timeout-reason block found");
        int b = script.IndexOf("tostring(_st)\n", a, StringComparison.Ordinal);
        Assert.True(b >= 0, "timeout-reason block is not terminated");
        return string.Join('\n', script[a..(b + 14)].Split('\n').Select(l => l.TrimStart()));
    }

    private static int Count(string haystack, string needle)
    {
        int n = 0, i = 0;
        while ((i = haystack.IndexOf(needle, i, StringComparison.Ordinal)) >= 0) { n++; i += needle.Length; }
        return n;
    }

    // ── The THIRD script shape: helper-shaped, back-to-back round-trips ──────
    //
    // InvokeScriptGenerator is not one flat round-trip. It emits Lua helper functions and
    // drives THREE round-trips through them back to back — CMD_FIND_INSTANCE(2),
    // CMD_FIND_FUNCTION(3), CMD_INVOKE(1) — which is exactly the shape that trips the
    // DLL's publication order. `SetDone`/`SetError` write `status = STATUS_DONE` BEFORE
    // clearing `cmd` (Mimic.cpp, deliberately, with the comment saying so). A script that
    // polls status, sees DONE, and immediately writes `status = 0` + `cmd = N` can have
    // that command WIPED by the DLL's trailing `cmd = CMD_IDLE`. The DLL then never
    // dispatches it, `status` stays 0, and the wait times out reporting "the DLL never saw
    // this command" — technically true, and it sends the user to re-inject a healthy DLL.
    //
    // The R3 bounded idle wait reached TeleportScriptGenerator, CoordLibraryScriptGenerator
    // and PointerQueryScriptGenerator and never reached Invoke, which was the only mailbox
    // generator writing `cmd` straight off a status poll.

    private static FunctionInfoModel NoParams() => new()
    {
        Name = "OpenShop",
        Params = new List<FunctionParamModel>(),
    };

    /// <summary>A string param is deliberately in here: it makes the generator inline its
    /// FString builder, whose body writes to a CE-allocated buffer of its own. That is not
    /// a mailbox write and must not be mistaken for one — see <see cref="IsMailboxWrite"/>.</summary>
    private static FunctionInfoModel WithParams() => new()
    {
        Name = "AddMoney",
        NumParms = 2,
        ParmsSize = 20,
        Params = new List<FunctionParamModel>
        {
            new() { Name = "Amount", TypeName = "IntProperty", Size = 4,  Offset = 0 },
            new() { Name = "Reason", TypeName = "StrProperty", Size = 16, Offset = 4 },
        },
    };

    /// <summary>Both Invoke flavours: no-params goes down the direct-invoke path, params
    /// goes down the FIRE-form path whose third round-trip lives in a closure.</summary>
    public static IEnumerable<object[]> InvokeShapedScripts() => new List<object[]>
    {
        new object[] { "Invoke.NoParams",   InvokeScriptGenerator.Generate("ShopKeeper_C", "OpenShop", NoParams()) },
        new object[] { "Invoke.WithParams", InvokeScriptGenerator.Generate("Player_C", "AddMoney", WithParams()) },
    };

    /// <summary>
    /// The rule: nothing goes into the mailbox until the previous command has been
    /// observed cleared. Asserted as a WINDOW scan rather than "a waitIdle() exists
    /// somewhere", because the defect is purely one of ORDER — a write emitted before its
    /// wait is exactly as lost as a write with no wait at all.
    /// </summary>
    [Theory]
    [MemberData(nameof(InvokeShapedScripts))]
    public void NoMailboxWriteEscapesTheIdleWait(string name, string script)
    {
        var lines = EnableBlock(script).Split('\n');

        // Scan from the mailbox lookup. Everything above it is helper DEFINITIONS, and
        // one of those bodies (`writeMbStr`) does write to `mb + offset` — but only when
        // it is called, which is always inside a guarded window below.
        // Anchored on the SYMBOL, not on the lookup function: the generator moved from
        // getAddress to getAddressSafe (audit #5 Y8, since the bare form raises on the very
        // case the block handles), and an anchor spelling one of them silently loses the
        // window it is supposed to scan. This scan is about ORDER, so it must survive a change
        // of lookup function.
        int start = Array.FindIndex(lines,
            l => l.Contains("g_invokeMailbox')", StringComparison.Ordinal));
        Assert.True(start >= 0,
            $"{name}: no mailbox lookup — the anchor this scan starts from is gone");

        bool guarded = false;
        int guards = 0, triggers = 0;

        // Skip `local function` BODIES. They are definitions: their writes happen when the
        // helper is CALLED, and every call site is inside a guarded window below. This used
        // to be handled by the anchor alone — everything above the mailbox lookup was a
        // definition — but the contract check has to precede every write, so the lookup
        // moved ABOVE the helpers and `writeMbStr`'s body came into range.
        int defIndent = -1;

        for (int i = start; i < lines.Length; i++)
        {
            string raw = lines[i];
            int indent = raw.Length - raw.TrimStart().Length;

            if (defIndent >= 0)
            {
                if (raw.Trim() == "end" && indent <= defIndent) defIndent = -1;
                continue;
            }
            if (raw.Contains("local function ", StringComparison.Ordinal))
            {
                defIndent = indent;
                continue;
            }

            if (lines[i].Contains("waitIdle()", StringComparison.Ordinal))
            {
                guarded = true;
                guards++;
                continue;
            }
            if (!IsMailboxWrite(lines[i])) continue;

            Assert.True(guarded,
                $"{name}: line {i + 1} writes into the mailbox with no idle wait since the " +
                $"last command — the DLL's trailing `cmd = CMD_IDLE` can wipe it:\n  {lines[i].Trim()}");

            // The cmd store is the trigger, and it closes the window: whatever comes next
            // is a new round-trip and needs its own wait.
            if (lines[i].Contains($"writeInteger(mb + {CeMailboxLayout.OffCmd},", StringComparison.Ordinal))
            {
                triggers++;
                guarded = false;
            }
        }

        Assert.Equal(3, triggers);        // FIND_INSTANCE(2), FIND_FUNCTION(3), INVOKE(1)
        Assert.Equal(triggers, guards);   // one wait per round-trip — no fewer, and no spares
    }

    /// <summary>Writes that land INSIDE the mailbox: the <c>mb + off</c> and
    /// <c>PD + off</c> forms, plus the emitted <c>writeMbStr</c> (which takes an offset,
    /// not an address). Deliberately not a bare "write" match — the inlined FString
    /// builder does <c>writeQword(addr, buf)</c> into its own CE allocation, which is not
    /// the mailbox and would make this a test of nothing if it counted.</summary>
    private static bool IsMailboxWrite(string line) =>
        line.Contains("writeMbStr(", StringComparison.Ordinal)
        || (line.Contains("write", StringComparison.Ordinal)
            && (line.Contains("(mb + ", StringComparison.Ordinal)
                || line.Contains("(PD + ", StringComparison.Ordinal)));

    /// <summary>
    /// The three TOP-LEVEL guards bail like every other ENABLE bail-out in this file:
    /// say why, untick, return. A busy mailbox there means nothing was sent at all, so a
    /// row left ticked tells the user a cheat is active when it is not.
    /// </summary>
    [Fact]
    public void ATopLevelBusyMailboxBail_UnticksTheRecordAndSaysWhy()
    {
        // The no-params flavour: all three of its round-trips are at block level.
        var lines = EnableBlock((string)InvokeShapedScripts().First()[1]).Split('\n');

        int found = 0;
        for (int i = 0; i < lines.Length; i++)
        {
            if (!lines[i].Contains("if not waitIdle() then", StringComparison.Ordinal)) continue;
            found++;
            Assert.True(Within(lines, i + 1, 8, "memrec.Active = false"),
                $"the busy-mailbox bail on line {i + 1} leaves the CE row ticked with nothing applied");
            Assert.True(Within(lines, i + 1, 8, "showMessage("),
                $"the busy-mailbox bail on line {i + 1} is silent — it reads exactly like success");
        }
        Assert.Equal(3, found);
    }

    /// <summary>
    /// The fourth guard sits inside <c>btnFire.OnClick</c>, so its <c>return</c> leaves
    /// only the click handler. That is the correct behaviour — the form stays open and the
    /// user can press FIRE again — and it is precisely why this one must SAY something and
    /// must NOT untick: the record's lifetime belongs to the form, and
    /// <c>frm.OnClose</c> owns the untick.
    /// </summary>
    [Fact]
    public void TheClosureBusyMailboxBail_SaysSoAndLeavesTheFormAlone()
    {
        string enable = EnableBlock((string)InvokeShapedScripts().Last()[1]);

        int click = enable.IndexOf("btnFire.OnClick = function()", StringComparison.Ordinal);
        Assert.True(click >= 0, "the FIRE handler is gone");

        int guard = enable.IndexOf("if not waitIdle() then", click, StringComparison.Ordinal);
        Assert.True(guard > click, "the FIRE handler writes to the mailbox with no idle wait");

        // Ordering again: before the params buffer is touched, not after.
        int firstWrite = enable.IndexOf("(PD + ", click, StringComparison.Ordinal);
        Assert.True(firstWrite > guard,
            "the FIRE handler's idle wait comes after its first write into the params buffer");

        // The bail body is everything up to the `local PD` that follows it.
        int bailEnd = enable.IndexOf("local PD", guard, StringComparison.Ordinal);
        string bail = enable[guard..bailEnd];

        Assert.Contains("nothing was sent", bail, StringComparison.Ordinal);
        Assert.Contains("return", bail, StringComparison.Ordinal);
        // A cleanup timer here would untick while the form is still on screen.
        Assert.DoesNotContain("memrec", bail, StringComparison.Ordinal);
        Assert.DoesNotContain("createTimer", bail, StringComparison.Ordinal);
    }

    /// <summary>
    /// The wait BODY is the shared emitter's, verbatim. That is the whole reason it lives
    /// in <see cref="CeLuaHygiene"/>: three separate properties of the sibling wait loop
    /// were wrong in all seven of its hand-rolled copies, and fixing them per-file only
    /// drifts again.
    /// </summary>
    [Fact]
    public void TheIdleWaitBodyIsTheSharedEmittersVerbatim()
    {
        var expected = new System.Text.StringBuilder();
        CeLuaHygiene.AppendIdleWait(expected, "mb", "return false", "    ");

        foreach (var row in InvokeShapedScripts())
            Assert.Contains(expected.ToString(), (string)row[1], StringComparison.Ordinal);
    }

    /// <summary>ONE emitted helper, not one copy per call site. Four hand-placed copies of
    /// a rule is the failure mode this area keeps hitting — R3 itself only reached three of
    /// the four generators that needed it.</summary>
    [Fact]
    public void TheIdleWaitIsEmittedOnce_NotOncePerCallSite()
    {
        foreach (var row in InvokeShapedScripts())
            // Anchored on the SURVIVING emitter's text. Two branches fixed this area in
            // parallel and shipped different locals; the one that landed is the
            // _idle-prefixed set, because three generators emit both waits into one Lua
            // scope and an unprefixed `waited` would collide with the status poll's.
            Assert.Equal(1, Count((string)row[1], "local _idleT0, _idleIters = "));
    }

    // ── The FOURTH shape: a helper that RETURNS its reason ───────────────────
    //
    // PointerQuery factors its round-trip into `query(op)` and returns `nil, reason` so the
    // caller can judge it — the GameEngine path is SUPPOSED to fall through from the
    // &GEngine slot to a UEngine* snapshot when no GEngine AOB validated, so a dialog
    // inside the wait would fire on the success path. That is MailboxTimeout.ReturnReason.
    //
    // Until 2026-08-07 both of its loops were hand-rolled, and the status poll carried all
    // three defects the shared emitter exists to fix: `elapsed >= 10000` counted sleep(1)
    // iterations against a millisecond constant (~155 s of frozen Lua Engine), the message
    // was the "(DLL not responding?)" guess, and the two statuses were never told apart.

    public static IEnumerable<object[]> HelperReturningScripts() => new List<object[]>
    {
        new object[] { "PointerQuery.GWorld",     PointerQueryScriptGenerator.Generate(PointerQueryScriptGenerator.Target.GWorld) },
        new object[] { "PointerQuery.GameEngine", PointerQueryScriptGenerator.Generate(PointerQueryScriptGenerator.Target.GameEngine) },
    };

    /// <summary>
    /// Both waits are the shared emitter's output verbatim. Asserted against
    /// <see cref="CeLuaHygiene"/> ITSELF rather than against a sibling generator, which is
    /// strictly stronger than
    /// <see cref="AllGeneratorsEmitByteIdenticalWaitLoops_ApartFromTheirTag"/>: that one
    /// would still pass if every generator drifted together.
    /// </summary>
    [Theory]
    [MemberData(nameof(HelperReturningScripts))]
    public void BothWaitsAreTheSharedEmittersVerbatim(string name, string script)
    {
        var idle = new System.Text.StringBuilder();
        CeLuaHygiene.AppendIdleWait(idle, "mb",
            // Must match what PointerQuery and CoordLibrary actually pass, verbatim — this
            // theory's whole point is that the emitted text is the shared emitter's output
            // and not a re-typed copy.
            "return nil, 'the DLL mailbox is busy -- try again in a moment'", "  ",
            "return nil, 'the mailbox could not be read -- the game process has most likely exited (re-inject UE5Dumper.dll if it is still running)'");
        Assert.True(script.Contains(idle.ToString(), StringComparison.Ordinal),
            $"{name}: the idle wait is not CeLuaHygiene.AppendIdleWait's output verbatim");

        // The tag is unused in ReturnReason mode (the caller prefixes its own), so any tag
        // reproduces the same bytes — which is itself worth pinning.
        var wait = new System.Text.StringBuilder();
        CeLuaHygiene.AppendMailboxWait(wait, "IgnoredTag", MailboxTimeout.ReturnReason, "  ");
        Assert.True(script.Contains(wait.ToString(), StringComparison.Ordinal),
            $"{name}: the status poll is not CeLuaHygiene.AppendMailboxWait's output verbatim");
    }

    /// <summary>The three fixes the hand-rolled poll was missing, asserted on the emitted
    /// text rather than on "it calls the emitter" — a caller can always stop calling.</summary>
    [Theory]
    [MemberData(nameof(HelperReturningScripts))]
    public void TheReturnedReasonCarriesTheSameThreeFixes(string name, string script)
    {
        string enable = EnableBlock(script);

        // 1. A real deadline, not an iteration count.
        Assert.Contains("getTickCount", enable, StringComparison.Ordinal);
        Assert.Contains($"_t0 >= {CeMailboxLayout.MailboxPollTimeoutMs}", enable, StringComparison.Ordinal);
        Assert.False(script.Contains("elapsed >= ", StringComparison.Ordinal),
            $"{name}: the pre-fix iteration counter survived");

        // 2. The status is read, not guessed at.
        Assert.Contains("never saw this command", enable, StringComparison.Ordinal);
        Assert.Contains("never finished it", enable, StringComparison.Ordinal);
        Assert.Contains($"_st == {CeMailboxLayout.StatusProcessing}", enable, StringComparison.Ordinal);
        Assert.False(script.Contains("(DLL not responding?)", StringComparison.Ordinal),
            $"{name}: still emits the guess that blamed a healthy DLL");

        // 3. Handed back, not shown — the whole reason this mode exists. A showMessage
        // inside `query` would fire on the GameEngine slot→snapshot fall-through, which is
        // a SUCCESS path.
        int helper = enable.IndexOf("local function query(op)", StringComparison.Ordinal);
        int helperEnd = enable.IndexOf("\nend\n", enable.IndexOf("return a", helper, StringComparison.Ordinal),
                                       StringComparison.Ordinal);
        string body = enable[helper..helperEnd];
        Assert.DoesNotContain("showMessage", body, StringComparison.Ordinal);
        Assert.DoesNotContain("memrec", body, StringComparison.Ordinal);
    }

    /// <summary>The idle wait still has to come before the <c>cmd</c> store, same rule as
    /// everywhere else — the ordering is the defect, not the presence of a wait.</summary>
    [Theory]
    [MemberData(nameof(HelperReturningScripts))]
    public void TheIdleWaitPrecedesTheCmdStore(string name, string script)
    {
        string enable = EnableBlock(script);
        int idle = enable.IndexOf("local _idleT0, _idleIters = ", StringComparison.Ordinal);
        int cmd = enable.IndexOf($"writeInteger(mb + {CeMailboxLayout.OffCmd},", StringComparison.Ordinal);
        Assert.True(idle >= 0, $"{name}: no idle wait at all");
        Assert.True(cmd > idle, $"{name}: writes cmd at {cmd} before waiting for IDLE at {idle}");
    }

    /// <summary>
    /// The contract check this generator was missing entirely. Same MUST rule as every
    /// other mailbox script: read the DLL's published range BEFORE the first write, because
    /// if the layout moved then a write lands on whatever now occupies those offsets.
    /// </summary>
    [Theory]
    [MemberData(nameof(HelperReturningScripts))]
    public void TheContractIsCheckedBeforeTheFirstWrite(string name, string script)
    {
        string enable = EnableBlock(script);

        int check = enable.IndexOf(CeMailboxLayout.ContractSymbol, StringComparison.Ordinal);
        Assert.True(check >= 0, $"{name}: never reads the contract symbol");

        int w = new[] { "writeQword(", "writeInteger(", "writeBytes(", "writeByte(" }
            .Select(fn => enable.IndexOf(fn, StringComparison.Ordinal))
            .Where(i => i >= 0)
            .DefaultIfEmpty(-1)
            .Min();
        Assert.True(w < 0 || check < w,
            $"{name}: writes to the mailbox at offset {w} before checking the contract at {check}");

        // Both directions, the stale-symbol case, and an untick on failure.
        Assert.Contains("too old for the DLL", enable, StringComparison.Ordinal);
        Assert.Contains("DLL is older than this script", enable, StringComparison.Ordinal);
        Assert.Contains("stale address", enable, StringComparison.Ordinal);
        Assert.True(enable.IndexOf("memrec.Active = false", check, StringComparison.Ordinal) > check,
            $"{name}: a failed contract check leaves the record ticked");
    }

    /// <summary>Every ENABLE bail-out unticks — the file's original rule, now applied to
    /// this generator too. Its <c>query</c> helper is exempt by construction: it returns
    /// <c>nil, reason</c> (never a bare <c>return</c>) precisely so it cannot untick.</summary>
    [Theory]
    [MemberData(nameof(HelperReturningScripts))]
    public void EveryEnableBailout_UnticksTheRecord_HelperReturningToo(string name, string script)
    {
        var lines = EnableBlock(script).Split('\n');
        bool sawBailout = false;

        for (int i = 0; i < lines.Length; i++)
        {
            if (lines[i].Contains("showMessage(", StringComparison.Ordinal))
            {
                sawBailout = true;
                Assert.True(Within(lines, i + 1, 8, "memrec.Active = false"),
                    $"{name}: line {i + 1} reports a failure but no untick follows it:\n  {lines[i].Trim()}");
            }

            string t = lines[i].Trim();
            if (t == "return" && !lines[i].Contains("syntaxcheck", StringComparison.Ordinal))
                Assert.True(Back(lines, i - 1, 4, "memrec.Active = false"),
                    $"{name}: the return on line {i + 1} leaves the block without unticking");
        }

        Assert.True(sawBailout, $"{name}: no ENABLE bail-out at all — the harness is not reaching the code it checks");
    }

    /// <summary>The success-close must stay unreachable from the wait's bail-out.</summary>
    [Theory]
    [MemberData(nameof(HelperReturningScripts))]
    public void TheAutoCloseStaysAfterTheWait(string name, string script)
    {
        string enable = EnableBlock(script);
        int loop = enable.IndexOf("while _st ~=", StringComparison.Ordinal);
        Assert.True(loop >= 0, $"{name}: no shared wait loop — did this generator stop using CeLuaHygiene?");
        int close = enable.IndexOf(CeLuaHygiene.CloseCall, StringComparison.Ordinal);
        Assert.True(close > loop, $"{name}: the success-close is emitted before the wait loop");
        Assert.DoesNotContain("then break end", enable, StringComparison.Ordinal);
    }
}
