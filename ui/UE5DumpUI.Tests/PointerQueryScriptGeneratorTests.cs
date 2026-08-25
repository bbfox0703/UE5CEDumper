using UE5DumpUI.Services;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// Locks the "Get GWorld" / "Get GameEngine" CE records: a stateful toggle that
/// resolves an address via the mailbox (CMD_QUERY_PTR=13), then publishes a CE
/// symbol. GWorld registers the symbol DIRECTLY to the &amp;GWorld slot (no buffer,
/// auto-follows level changes). GameEngine now prefers the same shape — the
/// &amp;GEngine SLOT via op 2 — and only falls back to an allocateMemory snapshot of
/// the live UEngine* (op 1) when no GEngine AOB validated. Mirrors the mailbox
/// contract in dll/src/Mimic.h (QueryPtrOp) + the CE Lua registerSymbol pattern.
/// </summary>
public class PointerQueryScriptGeneratorTests
{
    [Fact]
    public void Generate_is_lf_only()
    {
        var s = PointerQueryScriptGenerator.Generate(PointerQueryScriptGenerator.Target.GWorld);
        Assert.DoesNotContain("\r", s);
    }

    [Fact]
    public void Both_blocks_present()
    {
        var s = PointerQueryScriptGenerator.Generate(PointerQueryScriptGenerator.Target.GameEngine);
        Assert.Contains("[ENABLE]", s);
        Assert.Contains("[DISABLE]", s);
    }

    [Fact]
    public void GWorld_uses_op_0_reads_slot_and_registers_it_directly()
    {
        var s = PointerQueryScriptGenerator.Generate(PointerQueryScriptGenerator.Target.GWorld);
        Assert.Contains("query(0)", s);                    // op QUERY_OP_GWORLD
        Assert.Contains("writeInteger(mb + 0x00, 13)", s); // CMD_QUERY_PTR (write LAST)
        // The &GWorld slot is at paramsData[0..7] = mb + 0x328.
        Assert.Contains("readQword(mb + 0x328)", s);
        // Registered DIRECTLY to the slot address — no buffer, no dealloc.
        Assert.Contains("registerSymbol('UE_GWorld', addr)", s);
        // GWorld can never take a buffer path, so the script must not even MENTION
        // allocation — a reader has to be able to see at a glance that this record
        // cannot free a game address.
        Assert.DoesNotContain("allocateMemory", s);
        Assert.DoesNotContain("deAlloc", s);
    }

    [Fact]
    public void GameEngine_prefers_the_slot_op_2_and_registers_it_directly()
    {
        var s = PointerQueryScriptGenerator.Generate(PointerQueryScriptGenerator.Target.GameEngine);
        Assert.Contains("writeInteger(mb + 0x00, 13)", s); // CMD_QUERY_PTR
        // Slot first (QUERY_OP_GENGINE_SLOT), instance second (QUERY_OP_GAME_ENGINE).
        Assert.Contains("query(2)", s);
        Assert.Contains("query(1)", s);
        Assert.True(s.IndexOf("query(2)", System.StringComparison.Ordinal)
                  < s.IndexOf("query(1)", System.StringComparison.Ordinal),
            "the slot must be attempted BEFORE the snapshot");
        // On the slot path the symbol binds straight to the returned address.
        Assert.Contains("registerSymbol('UE_GameEngine', addr)", s);
    }

    [Fact]
    public void GameEngine_falls_back_to_a_buffered_snapshot()
    {
        var s = PointerQueryScriptGenerator.Generate(PointerQueryScriptGenerator.Target.GameEngine);
        Assert.Contains("local mem = allocateMemory(8)", s);
        Assert.Contains("writeQword(mem, addr)", s);
        Assert.Contains("registerSymbol('UE_GameEngine', mem)", s);
        // The marker is what lets [DISABLE] tell "we allocated this" from "this is a
        // game address" — without it, deAlloc could be called on the slot.
        Assert.Contains("registerSymbol('UE_GameEngine_buf', mem)", s);
    }

    [Fact]
    public void GameEngine_disable_frees_only_via_the_buffer_marker()
    {
        var s = PointerQueryScriptGenerator.Generate(PointerQueryScriptGenerator.Target.GameEngine);
        var disable = s.Substring(s.IndexOf("[DISABLE]", System.StringComparison.Ordinal));
        Assert.Contains("unregisterSymbol('UE_GameEngine')", disable);
        Assert.Contains("deAlloc(mem)", disable);
        // The dealloc must be reached through the marker, never through the symbol
        // itself — on the slot path that symbol IS a game address.
        Assert.Contains("getAddressSafe('UE_GameEngine_buf')", disable);
        Assert.DoesNotContain("deAlloc(getAddressSafe('UE_GameEngine'))", disable);
    }

    [Fact]
    public void Query_waits_for_idle_rather_than_sampling_cmd_once()
    {
        // The DLL writes status=DONE before it clears cmd, so a second back-to-back
        // query can observe the previous command for an instant. A single sample would
        // report "busy" and silently abandon the GEngine-slot -> snapshot fallback.
        //
        // The loop is CeLuaHygiene.AppendIdleWait's output now, not hand-rolled — hence
        // `idleWaited`, the shared emitter's counter, where this used to say `waited`.
        // CeMailboxBailoutTests.BothWaitsAreTheSharedEmittersVerbatim pins that it really
        // is the emitter's bytes; this one stays a behavioural check.
        var s = PointerQueryScriptGenerator.Generate(PointerQueryScriptGenerator.Target.GameEngine);
        Assert.Contains("local _idleCmd = readInteger(mb + 0x00)", s);
        Assert.Contains("while _idleCmd ~= 0 do", s);
        // nil is the absence of a status, not a busy mailbox: readInteger returns nil
        // once the process is gone, and `nil ~= 0` is true, so without this guard the
        // loop spun to its deadline and blamed the wrong thing.
        Assert.Contains("if _idleCmd == nil then", s);
        Assert.Contains("_idleIters = _idleIters + 1", s);
        Assert.DoesNotContain("if readInteger(mb + 0x00) ~= 0 then return nil", s);
    }

    [Fact]
    public void GameEngine_enable_clears_a_stale_buffer_before_republishing()
    {
        // An enable that now takes the slot path must not orphan the allocation a
        // previous snapshot-path enable made.
        var s = PointerQueryScriptGenerator.Generate(PointerQueryScriptGenerator.Target.GameEngine);
        var enable = s.Substring(0, s.IndexOf("[DISABLE]", System.StringComparison.Ordinal));
        Assert.Contains("local stale = getAddressSafe('UE_GameEngine_buf')", enable);
        Assert.Contains("deAlloc(stale)", enable);
    }

    [Fact]
    public void GWorld_disable_unregisters_only_no_dealloc()
    {
        var s = PointerQueryScriptGenerator.Generate(PointerQueryScriptGenerator.Target.GWorld);
        var disable = s.Substring(s.IndexOf("[DISABLE]", System.StringComparison.Ordinal));
        Assert.Contains("unregisterSymbol('UE_GWorld')", disable);
        // The slot is a game address — must never be deAlloc'd.
        Assert.DoesNotContain("deAlloc", disable);
    }

    // ── SLOTSYM: the slot [DISABLE] must ACTUALLY unregister, and say so honestly ──
    //
    // The bug: on the &GEngine SLOT path the record took the mayFallBack DISABLE branch,
    // where unregisterSymbol was nested inside the buffer-only `cur == mem` guard. With no
    // buffer, `mem` was nil, both arms were skipped, the symbol survived — and a trailing
    // UNCONDITIONAL dbg claimed it had been "unregistered" anyway. That leaves a stale
    // UE_GameEngine across a game restart (it resolves into the dead process's module).

    [Fact]
    public void GameEngine_slot_disable_actually_releases_the_symbol()
    {
        var s = PointerQueryScriptGenerator.Generate(PointerQueryScriptGenerator.Target.GameEngine);
        var disable = s.Substring(s.IndexOf("[DISABLE]", System.StringComparison.Ordinal));

        // The buffer guard is still there (the two other arms) …
        Assert.Contains("if mem and mem ~= 0 and cur == mem then", disable);
        Assert.Contains("elseif mem and mem ~= 0 then", disable);
        // … but now there is an ELSE (mem == nil = slot path) that reference-counts and
        // actually unregisters, instead of falling through doing nothing.
        Assert.Contains("UE5_slotSymRefcount['UE_GameEngine']", disable);
        Assert.Contains("while getAddressSafe('UE_GameEngine') and _tries < 8 do", disable);
        Assert.Contains("unregisterSymbol('UE_GameEngine')", disable);
    }

    [Fact]
    public void GWorld_disable_is_reference_counted_and_actually_releases()
    {
        var s = PointerQueryScriptGenerator.Generate(PointerQueryScriptGenerator.Target.GWorld);
        var disable = s.Substring(s.IndexOf("[DISABLE]", System.StringComparison.Ordinal));
        Assert.Contains("UE5_slotSymRefcount['UE_GWorld']", disable);
        Assert.Contains("while getAddressSafe('UE_GWorld') and _tries < 8 do", disable);
        Assert.Contains("unregisterSymbol('UE_GWorld')", disable);
    }

    [Theory]
    [InlineData(PointerQueryScriptGenerator.Target.GWorld, "UE_GWorld", "GWorld")]
    [InlineData(PointerQueryScriptGenerator.Target.GameEngine, "UE_GameEngine", "GameEngine")]
    public void Slot_disable_message_follows_the_fact_not_the_intent(
        PointerQueryScriptGenerator.Target target, string sym, string tag)
    {
        var s = PointerQueryScriptGenerator.Generate(target);
        var disable = s.Substring(s.IndexOf("[DISABLE]", System.StringComparison.Ordinal));

        // The success message is re-checked against reality: it only fires in the ELSE of
        // an `if getAddressSafe(sym) then <failure> else <success>` verify.
        Assert.Contains($"could NOT be unregistered", disable);
        Assert.Contains($"dbg('[{tag}] {sym} unregistered')", disable);

        // Negative control: the pre-fix UNCONDITIONAL success line (emitted at column 0,
        // so preceded by a bare newline) must be gone. If it came back, the message would
        // once again claim success on a path that unregistered nothing.
        Assert.DoesNotContain($"\ndbg('[{tag}] {sym} unregistered')", disable);
    }

    [Theory]
    [InlineData(PointerQueryScriptGenerator.Target.GWorld, "UE_GWorld")]
    [InlineData(PointerQueryScriptGenerator.Target.GameEngine, "UE_GameEngine")]
    public void Slot_disable_leaves_the_symbol_for_a_second_live_record(
        PointerQueryScriptGenerator.Target target, string sym)
    {
        var s = PointerQueryScriptGenerator.Generate(target);
        var disable = s.Substring(s.IndexOf("[DISABLE]", System.StringComparison.Ordinal));
        // Two records that resolve the SAME slot register the identical address, so only a
        // reference count (not an address marker) can keep the symbol for the survivor.
        Assert.Contains($"UE5_slotSymRefcount['{sym}'] = _rc", disable);
        Assert.Contains("if _rc > 0 then", disable);
        Assert.Contains("still held by", disable);
    }

    [Theory]
    [InlineData(PointerQueryScriptGenerator.Target.GWorld, "UE_GWorld")]
    [InlineData(PointerQueryScriptGenerator.Target.GameEngine, "UE_GameEngine")]
    public void Slot_enable_bumps_the_reference_count_before_registering(
        PointerQueryScriptGenerator.Target target, string sym)
    {
        var s = PointerQueryScriptGenerator.Generate(target);
        var enable = s.Substring(0, s.IndexOf("[DISABLE]", System.StringComparison.Ordinal));
        Assert.Contains($"UE5_slotSymRefcount['{sym}'] = (UE5_slotSymRefcount['{sym}'] or 0) + 1", enable);
        Assert.Contains($"registerSymbol('{sym}', addr)", enable);
        // The increment must precede the register so the count and the registration move
        // together.
        Assert.True(
            enable.IndexOf($"UE5_slotSymRefcount['{sym}'] = (", System.StringComparison.Ordinal)
            < enable.IndexOf($"registerSymbol('{sym}', addr)", System.StringComparison.Ordinal),
            "the refcount bump must come before the registerSymbol");
    }

    [Fact]
    public void Contract_check_is_emitted_exactly_once()
    {
        // Regression guard: the ENABLE used to call AppendContractCheck twice (a
        // copy/paste), emitting the whole ~40-line block redundantly.
        var s = PointerQueryScriptGenerator.Generate(PointerQueryScriptGenerator.Target.GameEngine);
        int n = 0;
        for (int i = s.IndexOf("local _want = ", System.StringComparison.Ordinal); i >= 0;
             i = s.IndexOf("local _want = ", i + 1, System.StringComparison.Ordinal))
            n++;
        Assert.Equal(1, n);
    }

    [Fact]
    public void Enable_closes_lua_engine_on_clean_success()
    {
        var s = PointerQueryScriptGenerator.Generate(PointerQueryScriptGenerator.Target.GWorld);
        Assert.Contains("if DEBUG == 0 then", s);
    }

    [Fact]
    public void SymbolName_maps_targets()
    {
        Assert.Equal("UE_GWorld",
            PointerQueryScriptGenerator.SymbolName(PointerQueryScriptGenerator.Target.GWorld));
        Assert.Equal("UE_GameEngine",
            PointerQueryScriptGenerator.SymbolName(PointerQueryScriptGenerator.Target.GameEngine));
    }

    [Fact]
    public void Resolves_mailbox_symbol_with_module_fallback()
    {
        var s = PointerQueryScriptGenerator.Generate(PointerQueryScriptGenerator.Target.GameEngine);
        Assert.Contains("getAddressSafe('g_invokeMailbox')", s);
        Assert.Contains("getAddressSafe('UE5Dumper.g_invokeMailbox')", s);
    }

    // --- Clipboard fallback: the AA body must be wrapped as paste-able CE XML ---
    // (a bare [ENABLE]/[DISABLE] body can't be pasted into a CE memory record).

    [Fact]
    public void WrapAaScriptXml_is_a_pasteable_cheatentry()
    {
        var script = PointerQueryScriptGenerator.Generate(PointerQueryScriptGenerator.Target.GWorld);
        var xml = CheatTableBuilder.WrapAaScriptXml("Get GWorld → symbol UE_GWorld", script);

        Assert.StartsWith("<?xml", xml);
        Assert.Contains("<CheatTable>", xml);
        Assert.Contains("<CheatEntries>", xml);
        Assert.Contains("<CheatEntry>", xml);
        Assert.Contains("<VariableType>Auto Assembler Script</VariableType>", xml);
        Assert.Contains("<AssemblerScript>", xml);
        Assert.Contains("</AssemblerScript>", xml);
    }

    [Fact]
    public void WrapAaScriptXml_escapes_the_script_body()
    {
        // The AA body contains XML-hostile chars (e.g. '>' in the "_tick() - _t0 >= N"
        // timeout deadline); they must be entity-escaped so the CE XML parser reads a
        // single well-formed <AssemblerScript> text node.
        var script = PointerQueryScriptGenerator.Generate(PointerQueryScriptGenerator.Target.GameEngine);
        var xml = CheatTableBuilder.WrapAaScriptXml("Get GameEngine → symbol UE_GameEngine", script);

        // Anchored on the shared emitter's actual text, not on a shape this generator no
        // longer emits: the old assertion named the hand-rolled `elapsed >= N` counter,
        // so once that was replaced BOTH halves passed for the wrong reason — nothing
        // was left to escape and nothing was left to find.
        // Assert the construct is REALLY in the body first, or the escape check below is
        // a test of nothing: the old assertion named `elapsed >= N` from the hand-rolled
        // poll, and folding that onto CeLuaHygiene.AppendMailboxWait deleted the string,
        // so the DoesNotContain went vacuously green with its comment still describing it.
        Assert.Contains("_t0 >= ", script);
        Assert.Contains($"_t0 &gt;= {CeMailboxLayout.MailboxPollTimeoutMs}", xml);
        Assert.DoesNotContain($"_t0 >= {CeMailboxLayout.MailboxPollTimeoutMs}", xml);
    }
}
