using System.Text;

namespace UE5DumpUI.Services;

/// <summary>
/// Generates a self-contained CE memory-record AA Script that asks the injected
/// UE5Dumper.dll for a global-pointer address and publishes it as a CE
/// <b>registered symbol</b> the user can reference directly — "Get GWorld" and
/// "Get GameEngine instance address".
///
/// <para>A STATEFUL toggle (like the GodMode record — NOT momentary). Both targets
/// resolve the address with one mailbox round-trip (<c>CMD_QUERY_PTR</c>), then
/// publish a symbol so <c>[UE_GWorld]+offset</c> / <c>[UE_GameEngine]+offset</c>
/// chains straight into the object. They differ in HOW the symbol is backed:</para>
/// <list type="bullet">
///   <item><b>GWorld</b> — registered DIRECTLY to the <c>&amp;GWorld</c> pointer
///   slot (a stable static/engine address). <c>[UE_GWorld]</c> dereferences the
///   slot to the CURRENT <c>UWorld*</c>, so it AUTO-FOLLOWS level transitions. No
///   buffer to free.</item>
///   <item><b>GameEngine</b> — prefers the same treatment: it asks for the
///   <c>&amp;GEngine</c> SLOT first (<c>QUERY_OP_GENGINE_SLOT</c>) and registers the
///   symbol directly to it, so <c>[UE_GameEngine]</c> auto-follows engine recreation.
///   Only when no GEngine AOB validated does it fall back to copying the live
///   <c>UEngine*</c> into an <c>allocateMemory(8)</c> buffer (a SNAPSHOT; re-tick to
///   refresh), which <c>[DISABLE]</c> then frees.</item>
/// </list>
///
/// <para>That choice is made at ENABLE time, not when this script is generated. A CE
/// record gets saved into a <c>.CT</c> and re-enabled in later sessions, where the AOB
/// may resolve even though it did not when the record was created (or vice versa), so
/// baking the decision in at generation time would make the artifact wrong later. The
/// snapshot path registers an extra <c>&lt;symbol&gt;_buf</c> marker purely so
/// <c>[DISABLE]</c> can tell the two apart — <c>deAlloc</c> must never be called on a
/// game address.</para>
///
/// <para>SELF-CONTAINED: talks to the mailbox directly (no
/// <c>ue5_invoke_helper.lua</c>). The mailbox is REQUIRED because CE Lua's
/// <c>executeCodeEx</c> can't reliably read an export's return value on protected
/// games (returns nil) — see docs/lessons-learned.md / docs/godmode-spec.md §10.</para>
/// </summary>
public static class PointerQueryScriptGenerator
{
    // Mailbox layout — see dll/src/Mimic.h (MailboxData + QueryPtrOp).
    private const int CmdQueryPtr = CeMailboxLayout.CmdQueryPtr;

    public enum Target
    {
        GWorld,      // QUERY_OP_GWORLD      = 0
        GameEngine,  // QUERY_OP_GAME_ENGINE = 1
    }

    /// <summary>The CE symbol name a given target publishes on enable
    /// (surfaced in the UI hint / record description so the user knows what to type).</summary>
    public static string SymbolName(Target target) =>
        target == Target.GWorld ? "UE_GWorld" : "UE_GameEngine";

    /// <summary>Build the [ENABLE]/[DISABLE] AA Script body for one query.</summary>
    public static string Generate(Target target)
    {
        // Short ASCII tag for comments / error messages (the script is transmitted
        // through CE / the AOBMaker JSON pipe).
        string tag = target == Target.GWorld ? "GWorld" : "GameEngine";
        string sym = SymbolName(target);
        // Marker symbol registered ONLY on the snapshot path, so [DISABLE] can tell a
        // buffer we allocated from a game address we merely pointed at. deAlloc on a
        // game address would be a very bad day.
        string bufSym = sym + "_buf";
        bool mayFallBack = target == Target.GameEngine;

        var sb = new StringBuilder(4096);

        // ── [ENABLE] ──
        Line(sb, "[ENABLE]");
        Line(sb, "{$lua}");
        Line(sb, "if syntaxcheck then return end");
        CeLuaHygiene.AppendDebugPreamble(sb);
        Line(sb, "-- ================================================================");
        Line(sb, $"-- Get {tag} -> CE symbol '{sym}' | {CeLuaHygiene.AttributionUrl}");
        if (mayFallBack)
        {
            Line(sb, $"-- ENABLE : ask UE5Dumper.dll (CMD_QUERY_PTR=13) for the &GEngine SLOT");
            Line(sb, $"--          (op 2) and registerSymbol('{sym}', slot) -- [{sym}]");
            Line(sb, "--          then derefs to the CURRENT UEngine, so it auto-follows");
            Line(sb, "--          engine recreation across a restart.");
            Line(sb, "--          If no GEngine AOB validated on this game, fall back to");
            Line(sb, "--          op 1 (live UEngine* found by walking GObjects), copy it");
            Line(sb, "--          into an 8-byte buffer and register THAT -- a SNAPSHOT,");
            Line(sb, "--          so re-tick to refresh.");
            Line(sb, $"-- DISABLE: unregisterSymbol('{sym}'), and deAlloc the buffer only if");
            Line(sb, $"--          the '{bufSym}' marker says we allocated one.");
        }
        else
        {
            Line(sb, $"-- ENABLE : query UE5Dumper.dll (CMD_QUERY_PTR=13) for the &GWorld");
            Line(sb, $"--          pointer slot, then registerSymbol('{sym}', slot).");
            Line(sb, $"--          [{sym}] derefs the slot to the CURRENT UWorld, so");
            Line(sb, $"--          [{sym}]+off auto-follows level transitions.");
            Line(sb, $"-- DISABLE: unregisterSymbol('{sym}') (nothing to free -- the slot");
            Line(sb, "--          is a game address, not ours).");
        }
        Line(sb, "-- Requires the DLL injected (version.dll proxy or CE inject).");
        Line(sb, "-- ================================================================");
        Line(sb);

        Line(sb, "local mb = getAddressSafe('g_invokeMailbox')");
        Line(sb, "if not mb or mb == 0 then mb = getAddressSafe('UE5Dumper.g_invokeMailbox') end");
        Line(sb, "if not mb or mb == 0 then");
        Line(sb, $"  showMessage('[{tag}] g_invokeMailbox not found -- is UE5Dumper.dll injected?')");
        Line(sb, "  if memrec then memrec.Active = false end");
        Line(sb, "  return");
        Line(sb, "end");
        // Contract check BEFORE the first write. It runs HERE, at chunk level, rather
        // than inside query(): every write this script makes lives in that helper, so
        // verifying the layout once before the helper exists covers all of them, and
        // covers them before the first one can land on offsets that moved. A stateful
        // toggle, so a failed check unticks the row instead of leaving it claiming to
        // have published a symbol.
        CeLuaHygiene.AppendContractCheck(sb, tag, MailboxTimeout.UntickAndReturn);
        Line(sb);

        // One mailbox round-trip, factored into a local so the GameEngine path can run it
        // twice (slot first, snapshot second). Returns addr, or nil + a reason string —
        // a failed op is NOT necessarily fatal here, so it must not untick by itself.
        Line(sb, "local function query(op)");
        CeLuaHygiene.AppendIdleWait(sb, "mb",
            "return nil, 'the DLL mailbox is busy -- try again in a moment'", "  ",
            "return nil, 'the mailbox could not be read -- the game process has most likely exited (re-inject UE5Dumper.dll if it is still running)'");
        Line(sb, $"  writeQword(mb + {CeMailboxLayout.OffInstanceAddr}, op)");
        Line(sb, $"  writeInteger(mb + {CeMailboxLayout.OffStatus}, 0)             -- clear status");
        Line(sb, $"  writeInteger(mb + {CeMailboxLayout.OffCmd}, {CmdQueryPtr})  -- CMD_QUERY_PTR (write LAST)");
        // The shared wait, in its by-value mode. This loop used to be hand-rolled and
        // carried all three of the defects build 2743 fixed in the other seven copies:
        // it counted sleep(1) iterations against a millisecond constant (so the "10 s"
        // timeout was ~155 s of frozen Lua Engine), and it reported "(DLL not
        // responding?)" -- a guess, when `status` already says whether the DLL never saw
        // the command or took it and wedged.
        //
        // ReturnReason rather than the toggles' UntickAndReturn because this helper is
        // called SPECULATIVELY: the GameEngine path tries the &GEngine slot and falls
        // back to a snapshot, so a failure here is not necessarily fatal and must not
        // untick by itself. The reason is untagged; the caller adds '[GWorld] '.
        CeLuaHygiene.AppendMailboxWait(sb, tag, MailboxTimeout.ReturnReason, indent: "  ");
        // Signed: rc is an int32 and readInteger defaults to unsigned, so a negative code
        // would print as a ten-digit number instead of, say, -1.
        Line(sb, $"  local code = readInteger(mb + {CeMailboxLayout.OffResult}, true)");
        // The two SOFT reasons carry their own "enter gameplay first?" hint, because the
        // caller can no longer append it blindly: it is right for "the DLL answered, it
        // just does not have this yet" and misleading on a timeout or a busy mailbox,
        // which is what a caller-side suffix used to staple onto every failure alike.
        Line(sb, "  if code ~= 0 then return nil, 'not resolved (code=' .. code .. ') -- enter gameplay first?' end");
        Line(sb, $"  local a = readQword(mb + {CeMailboxLayout.OffParamsData})");
        Line(sb, "  if not a or a == 0 then return nil, 'address is 0 -- not available yet; enter gameplay first?' end");
        Line(sb, "  return a");
        Line(sb, "end");
        Line(sb);

        if (mayFallBack)
        {
            // Clear a buffer left by a PREVIOUS enable before republishing — otherwise an
            // enable that now takes the slot path would leak the old allocation.
            Line(sb, $"local stale = getAddressSafe('{bufSym}')");
            Line(sb, $"if stale and stale ~= 0 then unregisterSymbol('{bufSym}'); deAlloc(stale) end");
        }
        Line(sb);

        if (mayFallBack)
        {
            Line(sb, "-- Prefer the SLOT (op 2). Only a game with no validated GEngine AOB");
            Line(sb, "-- falls through to the snapshot (op 1).");
            Line(sb, "local addr, err = query(2)");
            Line(sb, "local usedSlot = addr ~= nil");
            Line(sb, "if not usedSlot then");
            Line(sb, "  dbg('[" + tag + "] &GEngine slot unavailable (' .. tostring(err) .. ') -- using a UEngine* snapshot')");
            Line(sb, "  addr, err = query(1)");
            Line(sb, "end");
            Line(sb, "if not addr then");
            // The "enter gameplay first?" hint now lives on the two reasons it actually
            // fits (op not resolved / address still 0). Appending it to EVERY reason told a
            // user whose mailbox had timed out to go and play the game.
            Line(sb, $"  showMessage('[{tag}] ' .. tostring(err))");
            Line(sb, "  if memrec then memrec.Active = false end");
            Line(sb, "  return");
            Line(sb, "end");
            Line(sb);
            Line(sb, "if usedSlot then");
            // Slot path: register straight to the game slot, reference-counted so a second
            // live record keeps the symbol when the first is unticked (SLOTSYM).
            CeLuaHygiene.AppendSlotSymbolRegister(sb, sym, "addr", "  ");
            Line(sb, $"  dbg(string.format('[{tag}] {sym} -> &GEngine slot 0x%X (auto-follows)', addr))");
            Line(sb, "else");
            Line(sb, "  local mem = allocateMemory(8)");
            Line(sb, "  if not mem or mem == 0 then");
            Line(sb, $"    showMessage('[{tag}] allocateMemory failed')");
            Line(sb, "    if memrec then memrec.Active = false end");
            Line(sb, "    return");
            Line(sb, "  end");
            Line(sb, "  writeQword(mem, addr)");
            // Buffer path keeps its own marker-based ownership model (NOT the refcount) —
            // each snapshot record owns a DISTINCT allocation that DISABLE must free. Clear
            // any prior registration first (the generic pre-unregister that used to do this
            // moved into the slot emitter, so this branch does its own).
            Line(sb, $"  if getAddressSafe('{sym}') then unregisterSymbol('{sym}') end");
            Line(sb, $"  registerSymbol('{sym}', mem)");
            Line(sb, $"  registerSymbol('{bufSym}', mem)   -- marker: DISABLE must free this");
            Line(sb, $"  dbg(string.format('[{tag}] {sym} -> 0x%X (snapshot, buffer 0x%X)', addr, mem))");
            Line(sb, "end");
        }
        else
        {
            Line(sb, "local addr, err = query(0)");
            Line(sb, "if not addr then");
            // The "enter gameplay first?" hint now lives on the two reasons it actually
            // fits (op not resolved / address still 0). Appending it to EVERY reason told a
            // user whose mailbox had timed out to go and play the game.
            Line(sb, $"  showMessage('[{tag}] ' .. tostring(err))");
            Line(sb, "  if memrec then memrec.Active = false end");
            Line(sb, "  return");
            Line(sb, "end");
            // Reference-counted so a second live "Get GWorld" record keeps the symbol
            // when the first is unticked (SLOTSYM).
            CeLuaHygiene.AppendSlotSymbolRegister(sb, sym, "addr");
            Line(sb, $"dbg(string.format('[{tag}] {sym} -> &GWorld slot 0x%X', addr))");
        }
        Line(sb, $"if DEBUG == 0 then {CeLuaHygiene.CloseCall} end");
        Line(sb, "{$asm}");

        // ── [DISABLE] ──
        Line(sb, "[DISABLE]");
        Line(sb, "{$lua}");
        Line(sb, "if syntaxcheck then return end");
        CeLuaHygiene.AppendDebugPreamble(sb);
        if (mayFallBack)
        {
            Line(sb, $"-- Free the buffer ONLY when the '{bufSym}' marker says we allocated one;");
            Line(sb, "-- on the slot path the symbol points at a game address that is not ours.");
            Line(sb, "--");
            Line(sb, "-- AND only when it is still OUR buffer. Both symbols are global, so two of");
            Line(sb, "-- these records in one table share them: unticking the OLDER record used to");
            Line(sb, "-- deAlloc the NEWER one's live buffer and unregister the symbol while that");
            Line(sb, "-- record was still ticked, leaving its pointer chain resolving to ??. The");
            Line(sb, "-- newer ENABLE overwrote the marker, so a mismatch here means someone else");
            Line(sb, "-- owns it now — leave both alone and let the owner clean up. (B26)");
            Line(sb, $"local mem = getAddressSafe('{bufSym}')");
            Line(sb, $"local cur = getAddressSafe('{sym}')");
            Line(sb, "if mem and mem ~= 0 and cur == mem then");
            Line(sb, "  -- BUFFER path, still ours: unregister and free the allocation.");
            Line(sb, $"  unregisterSymbol('{bufSym}')");
            Line(sb, $"  unregisterSymbol('{sym}')");
            Line(sb, "  deAlloc(mem)");
            // Message follows the fact: re-read AFTER unregistering.
            Line(sb, $"  if getAddressSafe('{sym}') then");
            Line(sb, $"    dbg('[{tag}] {sym} could NOT be unregistered -- it still resolves')");
            Line(sb, "  else");
            Line(sb, $"    dbg('[{tag}] {sym} unregistered')");
            Line(sb, "  end");
            Line(sb, "elseif mem and mem ~= 0 then");
            Line(sb, $"  dbg('[{tag}] another record owns {sym} now -- leaving it alone')");
            Line(sb, "else");
            Line(sb, "  -- SLOT path (no buffer): reference-counted release, so a second live");
            Line(sb, "  -- record keeps the symbol. This branch is the SLOTSYM fix -- the old");
            Line(sb, $"  -- code left {sym} registered here while printing 'unregistered'.");
            CeLuaHygiene.AppendSlotSymbolRelease(sb, sym, tag, "  ");
            Line(sb, "end");
            Line(sb, $"if DEBUG == 0 then {CeLuaHygiene.CloseCall} end");
            Line(sb, "{$asm}");
            return sb.ToString();
        }

        Line(sb, $"-- Release the '{sym}' symbol (the slot is a game address, nothing to free).");
        Line(sb, "-- Reference-counted so a second live record keeps it, and the message");
        Line(sb, "-- follows the fact rather than the intent (SLOTSYM).");
        CeLuaHygiene.AppendSlotSymbolRelease(sb, sym, tag);
        Line(sb, $"if DEBUG == 0 then {CeLuaHygiene.CloseCall} end");
        Line(sb, "{$asm}");
        return sb.ToString();
    }

    private static void Line(StringBuilder sb, string text = "")
    {
        sb.Append(text);
        sb.Append('\n');
    }
}
