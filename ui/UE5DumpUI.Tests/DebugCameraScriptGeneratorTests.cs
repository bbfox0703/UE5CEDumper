using UE5DumpUI.Services;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// Locks the Debug Camera memory-record AA Script shape: a STATEFUL toggle
/// (tick = camera ON, untick = camera OFF), both blocks writing
/// CMD_SET_DEBUG_CAMERA to the mailbox themselves, no helper. The defining difference from BakedScriptGenerator is
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
        // No dependency on the embedded ue5_invoke_helper.lua, which can be
        // stale: the mailbox round-trip is inlined so the record works with
        // just the DLL injected.
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
    // Both blocks come from one emitter, so an [ENABLE] bail's showMessage would reach [DISABLE] too unless the
    // [DISABLE] side passes its own mode.
    [Fact]
    public void Disable_block_never_pops_a_modal()
    {
        var s = DebugCameraScriptGenerator.Generate();
        var disable = s[s.IndexOf("[DISABLE]", System.StringComparison.Ordinal)..];
        Assert.DoesNotContain("showMessage", disable);
        Assert.Contains("dbg('[DebugCamera]", disable);   // a DEBUG session still sees why it gave up
    }

    // The control: ticking keeps every announced, unticking bail -- the silent mode is [DISABLE]-only.
    [Fact]
    public void Enable_block_still_announces_and_unticks_its_bails()
    {
        var s = DebugCameraScriptGenerator.Generate();
        var enable = s[..s.IndexOf("[DISABLE]", System.StringComparison.Ordinal)];
        Assert.Contains("showMessage('[DebugCamera] g_invokeMailbox not found", enable);
        Assert.Contains("memrec.Active = false", enable);
    }

    // [W3-DEBUGCAM-QUEUED] -5: the toggle STAYS QUEUED and will run. Read as "state ~= 1", [ENABLE] would say the game
    // refused and UNTICK -- and a second tick would queue a second toggle behind the first. EveryEnableBailout's 8-line
    // window cannot tell: the failure branch's untick sits right below the queued message. This pins the branch itself.
    // [DBGCAM-QUEUEDON-UNDO-TEXT] Its reason not to tick again must hold on every engine: what a second toggle does when it
    // drains depends on the engine, so "a second toggle would undo it" is not the reason.
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
    // which on stock UE 4.18-5.7 can only enable, so the camera stays ON. The untick must say so ungated (a dbg() line is
    // silent by default) and RAISE rather than end UNTICKED over a camera still on: CE's TMemoryRecord.setActive sets
    // fActive only when the script succeeds, so a raised [DISABLE] leaves the record ticked.
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

    // [DBGCAM-OFF-OVER-QUEUED-ON] -6 is a toggle the DLL did NOT send, because an earlier one of the other direction is
    // still queued. Read as "state ~= req", [DISABLE] would untick quietly while a queued ON still turns the camera on,
    // and [ENABLE] would blame a missing CheatManager. Each branch says what happened, before the failure test.
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

    // [DEBUGCAM-FORCEON-CDO] -17: the DLL refused the ON -- no CheatManager is live, and the toggle does nothing on its
    // class default object. The tick says so and unticks without [DISABLE] (nothing was applied, and [DISABLE] would
    // send an OFF). [DISABLE] never gets -17: an OFF is never refused. The generic failure does not name a missing
    // CheatManager, which has its own code.
    [Fact]
    public void A_refused_ON_says_there_is_no_live_CheatManager_and_unticks_without_DISABLE()
    {
        var s = DebugCameraScriptGenerator.Generate();
        int d = s.IndexOf("[DISABLE]", System.StringComparison.Ordinal);
        var enable = s[..d];
        int b = enable.IndexOf($"elseif state == {UE5DumpUI.Constants.DebugCameraNoLiveCheatManagerResult} then",
                               System.StringComparison.Ordinal);
        Assert.True(b >= 0, "no -17 branch");
        int fail = enable.IndexOf("elseif state ~= 1 then", System.StringComparison.Ordinal);
        Assert.True(b < fail, "-17 must be tested before the failure");
        var branch = enable[b..enable.IndexOf("\nelse", b + 1, System.StringComparison.Ordinal)];
        Assert.Contains("showMessage('[DebugCamera] ON not sent -- there is no live CheatManager", branch);
        Assert.Contains(CeLuaHygiene.DeferredUntickWithoutDisableLua("  "), branch);
        Assert.True(branch.IndexOf("showMessage(", System.StringComparison.Ordinal)
                    < branch.IndexOf("createTimer(", System.StringComparison.Ordinal), branch);
        Assert.DoesNotContain(CeLuaHygiene.CloseCall, branch);
        Assert.DoesNotContain("state == -17", s[d..]);

        var generic = enable[fail..enable.IndexOf("\nelse", fail + 1, System.StringComparison.Ordinal)];
        Assert.DoesNotContain("'no live CheatManager, or the game refused", generic);
    }

    // [DBGCAM-ENABLE-BLOCKED-UNTICK-REENTERS-DISABLE] A plain `memrec.Active = false` makes CE run [DISABLE], which sends
    // an OFF nobody asked for. Over a queued OFF (the -6 tick) that OFF answers -5, so [DISABLE] prints "OFF did not
    // complete" and raises -- the record stays ticked under a dialog that said "Tick again". So both branches that apply
    // nothing untick WITHOUT running [DISABLE].
    // Negative control: emit CeLuaHygiene.DeferredUntickLua in either branch and this fails.
    [Fact]
    public void A_tick_that_applied_nothing_unticks_without_running_DISABLE()
    {
        var s = DebugCameraScriptGenerator.Generate();
        var enable = s[..s.IndexOf("[DISABLE]", System.StringComparison.Ordinal)];
        string BranchAt(string head)
        {
            int b = enable.IndexOf(head, System.StringComparison.Ordinal);
            Assert.True(b >= 0, "no branch " + head);
            return enable[b..enable.IndexOf("\nelse", b + 1, System.StringComparison.Ordinal)];
        }

        foreach (var branch in new[] { BranchAt("elseif state == -6 then"), BranchAt("elseif state ~= 1 then") })
        {
            Assert.Contains(CeLuaHygiene.DeferredUntickWithoutDisableLua("  "), branch);
            Assert.Contains("disableWithoutExecute", branch);
            // The timer is armed after the dialog: a timer armed before it fires inside it, while CE has not ticked yet.
            Assert.True(branch.IndexOf("showMessage(", System.StringComparison.Ordinal)
                        < branch.IndexOf("createTimer(", System.StringComparison.Ordinal), branch);
        }
    }

    // [DEBUGCAM-QUEUED-OFF-NOESCALATE] ue5_invoke_helper.lua's copy-me Debug Camera example must treat a queued OFF as the
    // generator does: a [DISABLE] that reports only a failed pcall(setDebugCamera, 0) unticks silently over a camera that
    // stays on. scripts/tests/invoke_helper_test.lua RUNS the block; this pins its shape where no CE Lua host is built.
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

    // [DBGCAM-QUEUEDON-UNDO-TEXT] The copy-me example's [ENABLE] tells the script author not to tick again, and its reason
    // must hold on every engine: "a second toggle would undo the first" is not what a second toggle does on stock UE
    // 4.18-5.7.
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

    // [DEBUGCAM-QUEUED-OFF-NOESCALATE] The DLL's own -5 texts must give each direction its own advice. The mailbox writes
    // one of them into errorMsg, which a CE script renders verbatim, so one shared "it will run ... do not re-send" would
    // tell a script that sent OFF not to re-send over a camera that stays on. The mailbox's texts are
    // mimic_mailbox_test's DEBUGCAM; this keeps the headers and the spec from saying otherwise.
    [Fact]
    public void The_DLLs_queued_texts_tell_a_queued_OFF_to_be_sent_again_and_never_claim_a_second_toggle_undoes_the_first()
    {
        string Src(string p) => System.IO.File.ReadAllText(NumericInputCoercionTests.RepoFile(p)).Replace("\r\n", "\n");
        // The mailbox's own texts are mimic_mailbox_test's DEBUGCAM; the headers and the spec must not say otherwise.
        foreach (var file in new[] { "dll/src/Frieren.h", "dll/src/Stark.h", "dll/src/Mimic.h", "docs/dll-spec.md" })
        {
            var text = Src(file);
            Assert.DoesNotContain("undoes it", text);
            Assert.DoesNotContain("never re-send", text);
        }
    }

    // [DBGCAM-OFF-OVER-QUEUED-ON] A Force OFF (or an untick) while a timed-out ON still sits in the game-thread queue reads
    // the camera OFF -- the ON has not run -- so a check that reads first returns "already OFF", reports a clean OFF, and
    // the queued ON then turns the camera on. UE5_SetDebugCamera must ask whether a queued ON has run BEFORE it reads the
    // state, and record an ON it queues. Frieren.cpp and Stark.cpp are in no C++ test target (Stark::QueuedInvoke itself
    // is tested in C++).
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
        // The dispatch must hand the slot over, and EnqueueInvoke fill it -- without either, the record stays empty with
        // every other pin green.
        Assert.Contains("CallProcessEventRouted(cm, ufunc, 0, 0, false, &", body);
        Assert.Contains("if (queuedOut && !dropIfAbandoned) *queuedOut = future;", Src("dll/src/Stark.cpp"));
        // Both directions are tracked (a queued OFF does flip the camera on UE 5.8), and one being dispatched counts
        // too: no lock is held across the invoke wait, which would push a CE untick past its 10 s deadline.
        Assert.Contains("s_dbgCamInFlight", body);
        Assert.Contains("QueuedToggleAnswer(", body);
        Assert.DoesNotContain("s_dbgCamSetMutex", fr);
    }

}
