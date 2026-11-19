namespace UE5DumpUI.Services;

/// <summary>
/// Canonical Mimic mailbox layout tokens shared by every CE Lua generator that
/// talks to the UE5Dumper.dll mailbox directly (Invoke / DebugCamera / Movement /
/// Protection / Teleport). These string offsets and opcode / timeout ints are
/// emitted VERBATIM into the generated Lua, so they MUST match
/// <c>dll/src/Mimic.h</c> (MailboxData struct + Cmd enum).
///
/// <para>Offsets are kept as pre-formatted hex STRINGS so the emitted text is
/// byte-identical everywhere. The canonical form is compact hex with no zero padding
/// (e.g. <c>"0x10"</c>, not <c>"0x010"</c>). Lua parses both to the same number, so
/// the choice is cosmetic; one form keeps the emitted text identical across
/// generators, <see cref="InvokeScriptGenerator"/> included.</para>
/// </summary>
internal static class CeMailboxLayout
{
    // Mailbox field offsets (must match Mimic.h MailboxData) — emitted as
    // `mb + {Off...}` in the generated Lua.
    public const string OffCmd          = "0x00";   // cmd (write LAST to trigger)
    public const string OffStatus       = "0x04";   // status (poll == 1)
    public const string OffResult       = "0x08";   // result / observed state
    public const string OffInitState    = "0x0C";   // initState (DLL-written auto-start readiness)
    public const string OffInstanceAddr = "0x10";   // instanceAddr / op / knobId / request
    public const string OffUfuncAddr    = "0x18";   // ufuncAddr / value / slot / show flag
    public const string OffParamsData   = "0x328";  // params_data[0..]
    // params_data[1] / [2] — the 2nd and 3rd 8-byte operand slots. Named because an
    // emitter that writes the FIRST slot through OffParamsData and the others as bare
    // literals in the same statement group would, if params_data moved, silently split
    // an FVector across the old and new layouts.
    // ⚠ These are the SAME BYTES as 0x328 + 8 and + 0x10. They add no mailbox field,
    // so they are NOT a contract change — the emitted Lua is byte-identical to the
    // literal form, which CeMailboxLayoutOffsetTests pins.
    public const string OffParamsData1  = "0x330";  // params_data[1]
    public const string OffParamsData2  = "0x338";  // params_data[2]
    // The params slab's size: Mimic.h `uint8_t paramsData[1024]`. A zero-fill (or any write)
    // past it lands on whatever follows the struct. [A3-CEFORM-4X-STALESLAB]
    // InvokeScriptTests.ParamsDataBytes_MatchesMimicH reads Mimic.h's declaration back.
    public const int    ParamsDataBytes = 1024;

    // cmdOutFlags (contract 3+) and the template-warning bit published in it (Mimic.h InvokeOutFlag, contract 7+, which
    // says which commands set it). [INVOKE-NONSTATIC-ON-CDO]
    public const string OffOutFlags = "0x72C";
    public const int    InvOutNonStaticOnTemplate = 0x2;

    // Auto-start readiness values written to OffInitState (must match Mimic.h
    // InitState). Unlike every other field here this one is NOT part of a
    // command round-trip — the DLL publishes it once during start-up so a CE Lua
    // bootstrap can poll for readiness with a pure memory read instead of
    // sleeping a fixed budget (no executeCodeEx ⇒ no CreateRemoteThread, which
    // games block).
    public const int InitIdle    = 0;   // DLL mapped; auto-start has not begun
    public const int InitRunning = 1;   // AOB scan / pipe-server start in progress
    public const int InitReady   = 2;   // init finished AND the pipe server is up
    public const int InitFailed  = 3;   // start-up did not complete: init aborted (the server may
                                        // still be up), or the pipe server failed to start
    public const int InitSkipped = 4;   // deliberately skipped — CE plugin host, or
                                        // another instance already owns the pipe

    // Cmd opcodes (must match Mimic.h Cmd enum).
    public const int CmdSetDebugCamera = 7;   // CMD_SET_DEBUG_CAMERA
    // Teleport's opcode lives here like the rest (the class summary above names Teleport as
    // a consumer): a generator that keeps a private copy of it gives the most-used opcode
    // two places to update.
    public const int CmdTeleport       = 8;   // CMD_TELEPORT  (Wirbel — markers / POV / coord TP)
    public const int CmdProtect        = 9;   // CMD_PROTECT   (GodMode / Solitar)
    public const int CmdMovement       = 10;  // CMD_MOVEMENT  (Laufen knobs)
    public const int CmdFly            = 11;  // CMD_FLY       (Dunste — no-gravity 3D flight)
    public const int CmdForeground     = 12;  // CMD_FOREGROUND (Grausam — keep-foreground lock)
    public const int CmdQueryPtr       = 13;  // CMD_QUERY_PTR (resolve GWorld / GameEngine address)
    public const int CmdSeeThrough     = 14;  // CMD_SEETHROUGH (Schlacht — see-through occluders toggle)
    public const int CmdTime           = 15;  // CMD_TIME      (Hemmung — time dilation hold)
    // [W5-OFFSETS-MAILBOX] Answer: result 1 = the DynOff offsets were measured, 0 = not, with the reason
    // in paramsData. Contract 5. An older DLL does not know it and answers "Unknown command" (-1).
    public const int CmdOffsetsVerdict = 16;  // CMD_OFFSETS_VERDICT (are the offsets measured?)

    // CMD_TIME op codes (Mimic.h TimeOp): instanceAddr = op, ufuncAddr = target
    // (0 global / 1 pawn), paramsData[0..7] = double value (SET only).
    public const int TimeOpSet   = 0;
    public const int TimeOpReset = 1;

    // ── CE Lua ↔ DLL contract version (must match Mimic.h) ──────────────────
    //
    // NOT the build number. A .CT saved months ago stays valid against a newer DLL
    // as long as nothing it depends on moved, so what is versioned is the CONTRACT
    // — the mailbox offsets, the Cmd values, the per-command op values, and the
    // status/result meanings. Mimic.h holds the full rule for what does and does
    // not bump it; do not restate it here, because two copies of a rule drift apart.
    //
    // Emitted into every generated script, which then checks itself against what
    // the DLL publishes. The DLL answers with a RANGE, so the two failure
    // directions can be told apart: too-old script => regenerate the .CT, too-old
    // DLL => update the DLL. Without the check, the second case is silent corruption.
    public const int ContractVersion = 9;

    /// <summary>Exported symbol carrying the contract. Deliberately NOT a field of
    /// the mailbox struct: reading the layout version out of the struct whose layout
    /// is in question is circular, and the check has to run BEFORE any write.</summary>
    public const string ContractSymbol = "g_mailboxContract";

    // Field offsets within that symbol (Mimic::MailboxContract).
    public const string OffContractMagic = "0x00";
    public const string OffContractCur   = "0x04";
    public const string OffContractMin   = "0x08";

    /// <summary>'UE5C'. Proves the symbol resolved to the DLL's real data and not to
    /// a stale address from a previous injection, where CE holds a mailbox address the
    /// DLL no longer owns.</summary>
    public const long ContractMagic = 0x43354555L;

    // Mailbox status values (must match Mimic.h MailboxStatus). Emitted into the
    // generated Lua so a timed-out script can say WHICH fault it hit instead of
    // guessing: the DLL sets Processing the instant its poller picks a command up
    // (Mimic.cpp `PollingThreadBody`) and Done+cmd=IDLE when it finishes,
    // so on timeout the status still distinguishes "never seen" from "wedged".
    public const int StatusIdle       = 0;      // untouched — the DLL never saw it
    public const int StatusDone       = 1;      // finished
    public const int StatusProcessing = 0xFF;   // picked up, still working

    /// <summary>
    /// Upper bound of the <c>while status ~= 1</c> busy-wait in every emitted mailbox
    /// round-trip, in REAL milliseconds — measured with <c>getTickCount()</c>, not
    /// counted in loop iterations.
    ///
    /// <para><b>Why that distinction is load-bearing.</b> <c>sleep(1)</c> in CE's Lua
    /// Engine does not sleep 1 ms: <c>sleep(1)</c> through <c>sleep(10)</c> all cost
    /// ~15.5 ms — a hard floor at the default Windows tick — and longer sleeps quantise
    /// to multiples of ~15.6 ms. CE does not raise the timer resolution for itself, and
    /// the DLL's own <c>timeBeginPeriod(1)</c> is per-process so it does not leak in.
    /// A bound of 10000 <c>sleep(1)</c> iterations would be <b>~155 seconds</b> of a
    /// frozen Lua Engine window before a user with a stale mailbox address is told
    /// anything.</para>
    ///
    /// <para><b>That floor is the Windows scheduler tick (~64 Hz), NOT a per-machine
    /// constant</b> — which is what makes it safe to bake
    /// <see cref="MailboxPollTimeoutIters"/> from it.</para>
    /// </summary>
    public const int MailboxPollTimeoutMs = 10000;

    /// <summary>
    /// Fallback bound for a CE build with no <c>getTickCount</c>, counted in
    /// <c>sleep(1)</c> iterations. Derived from the tick above:
    /// 650 x 15.47 ms ~= 10.1 s, matching <see cref="MailboxPollTimeoutMs"/>.
    /// Only reached if the <c>getTickCount()</c> probe fails.
    /// </summary>
    public const int MailboxPollTimeoutIters = 650;

    /// <summary>How long an emitted script waits for the mailbox to go IDLE before
    /// declaring it busy, in REAL milliseconds — measured with <c>getTickCount()</c>,
    /// exactly like <see cref="MailboxPollTimeoutMs"/>.
    ///
    /// <para>This is NOT the same thing as <see cref="MailboxPollTimeoutMs"/>, and it
    /// exists because of an ordering detail: <c>SetDone</c>/<c>SetError</c> publish
    /// <c>status = DONE</c> BEFORE clearing <c>cmd</c> (deliberately — see Mimic.cpp).
    /// A script that issues two round-trips back to back can therefore exit its
    /// status-poll and still observe the PREVIOUS command in <c>cmd</c> for an instant.
    /// Sampling <c>cmd</c> once would report "busy" and abandon the second query, so
    /// the wait is bounded rather than a single read.</para>
    ///
    /// <para><b>Why 1500.</b> The wait covers not only the nanosecond publication gap but
    /// a previous command that is genuinely still in flight, and mailbox commands are
    /// dispatched onto the GAME thread — a frame at 30 fps is already 33 ms, and a
    /// loading screen or a stalled game thread is far more. Every bail-out on this path
    /// is a soft "busy, try again", so erring long costs a wait and erring short costs a
    /// spurious failure.</para></summary>
    public const int MailboxIdleWaitMs = 1500;

    /// <summary>
    /// Fallback bound for the idle wait on a CE build with no <c>getTickCount</c>,
    /// counted in <c>sleep(1)</c> iterations. 100 x 15.47 ms ~= 1.55 s, matching
    /// <see cref="MailboxIdleWaitMs"/>.
    /// </summary>
    public const int MailboxIdleWaitIters = 100;
}
