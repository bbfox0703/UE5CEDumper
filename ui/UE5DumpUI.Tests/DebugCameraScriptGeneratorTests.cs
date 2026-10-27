using UE5DumpUI.Services;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// Locks the Debug Camera memory-record AA Script shape: a STATEFUL toggle
/// (tick = setDebugCamera(1), untick = setDebugCamera(0)), both blocks loading
/// the embedded helper. The defining difference from BakedScriptGenerator is
/// that [DISABLE] is NOT a nop here — it actively turns the camera off.
/// </summary>
public class DebugCameraScriptGeneratorTests
{
    [Fact]
    public void Generate_emits_enable_and_disable_blocks()
    {
        var s = DebugCameraScriptGenerator.Generate();
        Assert.Contains("[ENABLE]", s);
        Assert.Contains("[DISABLE]", s);
    }

    [Fact]
    public void Generate_enables_on_tick_and_disables_on_untick()
    {
        var s = DebugCameraScriptGenerator.Generate();
        Assert.Contains("writeQword(mb + 0x10, 1)", s);   // [ENABLE] request = ON
        Assert.Contains("writeQword(mb + 0x10, 0)", s);   // [DISABLE] request = OFF
    }

    [Fact]
    public void Both_blocks_trigger_the_set_debug_camera_mailbox_command()
    {
        var s = DebugCameraScriptGenerator.Generate();
        var triggers = s.Split("writeInteger(mb + 0x00, 7)").Length - 1;
        Assert.Equal(2, triggers);   // CMD_SET_DEBUG_CAMERA=7, one per block
    }

    [Fact]
    public void Disable_block_is_not_a_nop()
    {
        var s = DebugCameraScriptGenerator.Generate();
        var disableIdx = s.IndexOf("[DISABLE]", System.StringComparison.Ordinal);
        Assert.True(disableIdx > 0);
        var disableBlock = s.Substring(disableIdx);
        // The whole point: unticking the record forces the camera OFF.
        Assert.Contains("writeQword(mb + 0x10, 0)", disableBlock);
        Assert.DoesNotContain("-- nop", disableBlock);
    }

    [Fact]
    public void Is_self_contained_no_helper_file_dependency()
    {
        // The fragile part of the old design was depending on the embedded
        // ue5_invoke_helper.lua (which could be stale). The mailbox round-trip
        // is inlined so the record works with just the DLL injected.
        var s = DebugCameraScriptGenerator.Generate();
        Assert.DoesNotContain("findTableFile", s);
        Assert.Contains("g_invokeMailbox", s);
    }

    [Fact]
    public void Generate_is_lf_only_for_ce_compatibility()
    {
        Assert.DoesNotContain("\r", DebugCameraScriptGenerator.Generate());
    }

    // [W2-CEGEN-MODAL] Unticking must not put a modal over a fullscreen game (MailboxTimeout.SilentReturn's own doc).
    // Both blocks came from one emitter, so every [ENABLE] bail -- the missing mailbox, the contract check (two
    // messages), the idle wait, the timeout, the result check -- was a showMessage in [DISABLE] too.
    [Fact]
    public void Disable_block_never_pops_a_modal()
    {
        var s = DebugCameraScriptGenerator.Generate();
        var disable = s[s.IndexOf("[DISABLE]", System.StringComparison.Ordinal)..];
        Assert.DoesNotContain("showMessage", disable);
        Assert.Contains("dbg('[DebugCamera]", disable);   // a DEBUG session still sees why it gave up
    }

    // The control: ticking keeps every announced, unticking bail -- the fix is [DISABLE]-only.
    [Fact]
    public void Enable_block_still_announces_and_unticks_its_bails()
    {
        var s = DebugCameraScriptGenerator.Generate();
        var enable = s[..s.IndexOf("[DISABLE]", System.StringComparison.Ordinal)];
        Assert.Contains("showMessage('[DebugCamera] g_invokeMailbox not found", enable);
        Assert.Contains("memrec.Active = false", enable);
    }

    // [W3-DEBUGCAM-QUEUED] -5: the toggle STAYS QUEUED and will run. Read as "state ~= 1", [ENABLE] said the game refused
    // and UNTICKED -- and a second tick queued a second toggle behind the first. EveryEnableBailout's 8-line window cannot
    // tell: the failure branch's untick sits right below the queued message. This pins the branch itself.
    // [DBGCAM-QUEUEDON-UNDO-TEXT] Its reason not to tick again must hold on every engine: what a second toggle does when it
    // drains depends on the engine, so "a second toggle would undo it" was not the reason.
    [Fact]
    public void A_queued_toggle_is_reported_and_never_unticks()
    {
        var s = DebugCameraScriptGenerator.Generate();
        int d = s.IndexOf("[DISABLE]", System.StringComparison.Ordinal);
        var enable = s[..d];
        int q = enable.IndexOf("if state == -5 then", System.StringComparison.Ordinal);
        Assert.True(q >= 0, "no queued branch in [ENABLE]");
        int f = enable.IndexOf("elseif state ~= 1 then", q, System.StringComparison.Ordinal);
        Assert.True(f > q, "the queued test must come BEFORE the failure test, or -5 reads as a failure");
        var queued = enable[q..enable.IndexOf("\nelseif", q, System.StringComparison.Ordinal)];
        Assert.Contains("showMessage('[DebugCamera] ON queued", queued);
        Assert.Contains("Do not tick again: the toggle is already queued.", queued);
        Assert.DoesNotContain("undo", queued);
        Assert.DoesNotContain("memrec.Active = false", queued);    // nothing to untick: the toggle WILL run
        Assert.DoesNotContain(CeLuaHygiene.CloseCall, queued);     // not a clean success either

        var disable = s[d..];
        Assert.Contains("if state == -5 then", disable);
    }

    // [DEBUGCAM-QUEUED-OFF-NOESCALATE] (c): a queued OFF drains as a toggle on the original controller's CheatManager,
    // which on stock UE 4.18-5.7 can only enable, so the camera stays ON. The untick used to dbg() "the toggle will run"
    // -- silent by default -- and end UNTICKED over a camera still on. It now says so ungated, and RAISES: CE's
    // TMemoryRecord.setActive sets fActive only when the script succeeds, so a raised [DISABLE] leaves the record ticked.
    [Fact]
    public void A_queued_OFF_says_so_and_leaves_the_record_ticked()
    {
        var s = DebugCameraScriptGenerator.Generate();
        var disable = s[s.IndexOf("[DISABLE]", System.StringComparison.Ordinal)..];
        int q = disable.IndexOf("if state == -5 then", System.StringComparison.Ordinal);
        Assert.True(q >= 0, "no queued branch in [DISABLE]");
        int f = disable.IndexOf("elseif state ~= 0 then", q, System.StringComparison.Ordinal);
        Assert.True(f > q, "the queued test must come BEFORE the failure test");
        var queued = disable[q..disable.IndexOf("\nelseif", q, System.StringComparison.Ordinal)];
        Assert.Contains("print('[DebugCamera] OFF did not complete", queued);   // ungated: a real problem
        Assert.DoesNotContain("dbg(", queued);
        Assert.DoesNotContain("will run", queued);
        Assert.Contains("error('[DebugCamera]", queued);                        // keeps the record ticked
        Assert.DoesNotContain("showMessage(", queued);                         // no modal on an untick
        Assert.DoesNotContain(CeLuaHygiene.CloseCall, queued);
    }

    // [DBGCAM-OFF-OVER-QUEUED-ON] review: -6 is a toggle the DLL did NOT send, because an earlier one of the other
    // direction is still queued. Read as "state ~= req", [DISABLE] unticked quietly while a queued ON still turns the
    // camera on, and [ENABLE] blamed a missing CheatManager. Each branch says what happened, before the failure test.
    [Fact]
    public void A_toggle_refused_over_an_opposite_queued_one_says_it_was_not_sent()
    {
        var s = DebugCameraScriptGenerator.Generate();
        int d = s.IndexOf("[DISABLE]", System.StringComparison.Ordinal);
        string Branch(string block, string failTest)
        {
            int b = block.IndexOf("elseif state == -6 then", System.StringComparison.Ordinal);
            Assert.True(b >= 0, "no -6 branch");
            Assert.True(b < block.IndexOf(failTest, System.StringComparison.Ordinal), "-6 must be tested before the failure");
            return block[b..block.IndexOf("\nelseif", b + 1, System.StringComparison.Ordinal)];
        }

        var on = Branch(s[..d], "elseif state ~= 1 then");
        Assert.Contains("showMessage('[DebugCamera] ON not sent -- an earlier OFF is still queued", on);
        Assert.Contains("memrec.Active = false", on);                          // nothing was applied: untick

        var off = Branch(s[d..], "elseif state ~= 0 then");
        Assert.Contains("print('[DebugCamera] OFF not sent -- an earlier ON is still queued", off);   // ungated
        Assert.Contains("error('[DebugCamera]", off);                          // keeps the record ticked
        Assert.DoesNotContain("showMessage(", off);
    }

    // [DEBUGCAM-QUEUED-OFF-NOESCALATE] (review): ue5_invoke_helper.lua's copy-me Debug Camera example kept the old
    // [DISABLE] -- pcall(setDebugCamera, 0), reporting only a failed pcall -- so a queued OFF unticked silently over a
    // camera that stays on. scripts/tests/invoke_helper_test.lua RUNS the block; this pins its shape where no CE Lua
    // host is built.
    [Fact]
    public void Invoke_helper_example_disable_says_a_queued_OFF_and_keeps_the_record_ticked()
    {
        var src = System.IO.File.ReadAllText(NumericInputCoercionTests.RepoFile("scripts/ue5_invoke_helper.lua"))
                                .Replace("\r\n", "\n");
        int at = src.IndexOf("pcall(setDebugCamera, 0)", System.StringComparison.Ordinal);
        Assert.True(at >= 0, "the Debug Camera example's [DISABLE] call is gone");
        var block = src.Substring(at, src.IndexOf("{$asm}", at, System.StringComparison.Ordinal) - at);
        int q = block.IndexOf("state == -5", System.StringComparison.Ordinal);
        Assert.True(q >= 0, "no queued branch in the example's [DISABLE]");
        int end = block.IndexOf("\n    end", q, System.StringComparison.Ordinal);
        Assert.True(end > q, "the queued branch has no end");
        var queued = block[q..end];
        Assert.Contains("print('[Debug Camera] OFF did not complete", queued);
        Assert.DoesNotContain("UE5_DEBUG", queued);                            // ungated: a real problem
        Assert.Contains("error('[Debug Camera]", queued);                      // keeps the record ticked
        Assert.DoesNotContain("showMessage(", queued);                         // no modal on an untick
    }

    // [DBGCAM-QUEUEDON-UNDO-TEXT] The copy-me example's [ENABLE] told the script author not to tick again because "a second
    // toggle would undo the first" -- not what a second toggle does on stock UE 4.18-5.7. The advice stays, with a reason
    // that holds on every engine.
    [Fact]
    public void Invoke_helper_example_enable_gives_a_reason_not_to_tick_again_that_holds_everywhere()
    {
        var src = System.IO.File.ReadAllText(NumericInputCoercionTests.RepoFile("scripts/ue5_invoke_helper.lua"))
                                .Replace("\r\n", "\n");
        int at = src.IndexOf("pcall(setDebugCamera, 1)", System.StringComparison.Ordinal);
        Assert.True(at >= 0, "the Debug Camera example's [ENABLE] call is gone");
        int q = src.IndexOf("state == -5", at, System.StringComparison.Ordinal);
        Assert.True(q >= 0, "no queued branch in the example's [ENABLE]");
        int end = src.IndexOf("\n    end", q, System.StringComparison.Ordinal);
        Assert.True(end > q, "the queued branch has no end");
        var queued = src[q..end];
        Assert.Contains("do not tick again -- the toggle is already queued.", queued);
        Assert.DoesNotContain("undo", queued);
    }

    // [DEBUGCAM-QUEUED-OFF-NOESCALATE] batch 2's review: the DLL's own -5 texts kept the old advice for both directions.
    // The mailbox writes one of them into errorMsg, which a CE script renders verbatim (MB3), so a script that sent OFF
    // read "it will run ... do not re-send" over a camera that stays on. Mimic.cpp is in no C++ test target.
    [Fact]
    public void The_DLLs_queued_texts_tell_a_queued_OFF_to_be_sent_again_and_never_claim_a_second_toggle_undoes_the_first()
    {
        string Src(string p) => System.IO.File.ReadAllText(NumericInputCoercionTests.RepoFile(p)).Replace("\r\n", "\n");
        var mimic = Src("dll/src/Mimic.cpp");
        int at = mimic.IndexOf("static void HandleSetDebugCamera() {", System.StringComparison.Ordinal);   // not the declaration
        Assert.True(at >= 0, "HandleSetDebugCamera is gone");
        var body = mimic.Substring(at, mimic.IndexOf("\n}\n", at, System.StringComparison.Ordinal) - at);
        Assert.Contains("Debug Camera: OFF did not complete", body);
        Assert.Contains("send OFF again once the game responds", body);
        Assert.Contains("Debug Camera: ON queued", body);                      // the ON keeps "do not re-send"

        foreach (var file in new[] { "dll/src/Frieren.h", "dll/src/Stark.h", "dll/src/Mimic.h", "docs/dll-spec.md" })
        {
            var text = Src(file);
            Assert.DoesNotContain("undoes it", text);
            Assert.DoesNotContain("never re-send", text);
        }
    }

    // [DBGCAM-OFF-OVER-QUEUED-ON] A Force OFF (or an untick) while a timed-out ON still sat in the game-thread queue read
    // the camera OFF -- the ON had not run -- returned "already OFF", reported a clean OFF, and the queued ON then turned
    // the camera on. UE5_SetDebugCamera must ask whether a queued ON has run BEFORE it reads the state, and record an ON
    // it queues. Frieren.cpp and Stark.cpp are in no C++ test target (Stark::QueuedInvoke itself is tested in C++).
    [Fact]
    public void SetDebugCamera_asks_whether_a_queued_ON_has_run_before_it_reads_the_state_and_records_one_it_queues()
    {
        string Src(string p) => System.IO.File.ReadAllText(NumericInputCoercionTests.RepoFile(p)).Replace("\r\n", "\n");
        var fr = Src("dll/src/Frieren.cpp");
        int at = fr.IndexOf("int32_t UE5_SetDebugCamera(int32_t enable) {", System.StringComparison.Ordinal);
        Assert.True(at >= 0, "UE5_SetDebugCamera is gone");
        var body = fr.Substring(at, fr.IndexOf("\n}\n", at, System.StringComparison.Ordinal) - at);
        int ask = body.IndexOf("s_dbgCamQueued.StillQueued()", System.StringComparison.Ordinal);
        int read = body.IndexOf("DbgCam_ReadState(", System.StringComparison.Ordinal);
        Assert.True(ask >= 0, "UE5_SetDebugCamera never asks whether a queued toggle has run");
        Assert.True(ask < read, "it asks only after reading a state the queued toggle will change");
        Assert.Contains("s_dbgCamQueued.Record(", body);
        // Review of the fix: the dispatch must hand the slot over, and EnqueueInvoke fill it -- a revert of either kept
        // the record empty with every other pin green.
        Assert.Contains("CallProcessEventRouted(cm, ufunc, 0, 0, false, &", body);
        Assert.Contains("if (queuedOut && !dropIfAbandoned) *queuedOut = future;", Src("dll/src/Stark.cpp"));
        // Both directions are tracked (a queued OFF does flip the camera on UE 5.8), and one being dispatched counts
        // too: the lock is no longer held across the invoke wait, which pushed a CE untick past its 10 s deadline.
        Assert.Contains("s_dbgCamInFlight", body);
        Assert.Contains("QueuedToggleAnswer(", body);
        Assert.DoesNotContain("s_dbgCamSetMutex", fr);
    }

    // [DBGCAM-OFF-OVER-QUEUED-ON] review: a Set refused over an opposite queued toggle sent nothing; the mailbox says so
    // in each direction, where a CE script shows it verbatim.
    [Fact]
    public void The_mailbox_says_a_toggle_refused_over_an_opposite_queued_one_was_not_sent()
    {
        string Src(string p) => System.IO.File.ReadAllText(NumericInputCoercionTests.RepoFile(p)).Replace("\r\n", "\n");
        var mimic = Src("dll/src/Mimic.cpp");
        int at = mimic.IndexOf("static void HandleSetDebugCamera() {", System.StringComparison.Ordinal);
        var body = mimic.Substring(at, mimic.IndexOf("\n}\n", at, System.StringComparison.Ordinal) - at);
        Assert.Contains("Stark::kToggleBlockedByQueued", body);
        Assert.Contains("Debug Camera: OFF not sent -- an earlier ON is still queued", body);
        Assert.Contains("Debug Camera: ON not sent -- an earlier OFF is still queued", body);
    }
}
