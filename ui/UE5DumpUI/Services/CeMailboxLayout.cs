namespace UE5DumpUI.Services;

/// <summary>
/// Canonical Mimic mailbox layout tokens shared by every CE Lua generator that
/// talks to the UE5Dumper.dll mailbox directly (Invoke / DebugCamera / Movement /
/// Protection / Teleport). These string offsets and opcode / timeout ints are
/// emitted VERBATIM into the generated Lua, so they MUST match
/// <c>dll/src/Mimic.h</c> (MailboxData struct + Cmd enum).
///
/// <para>Offsets are kept as pre-formatted hex STRINGS so the emitted text is
/// byte-identical everywhere. The canonical form is the compact 2-digit hex
/// (e.g. <c>"0x10"</c>) historically emitted by DebugCamera / Movement / Protection /
/// Teleport (the majority). Lua parses <c>0x10</c> and <c>0x010</c> to the same
/// number, so <see cref="InvokeScriptGenerator"/> — which previously wrote the
/// zero-padded <c>0x010</c> — now emits this compact form (same address, cosmetic).</para>
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
    // params_data[1] / [2] — the 2nd and 3rd 8-byte operand slots. Named because five
    // emitters wrote them as bare literals while writing the FIRST one through
    // OffParamsData in the same statement group, so moving params_data would have
    // silently split an FVector across the old and new layouts (audit #5 AF14).
    // ⚠ These are the SAME BYTES the old literals named: 0x328 + 8 and + 0x10. Nothing
    // about the mailbox moved, so this is NOT a contract change — the emitted Lua is
    // byte-identical, which CeMailboxLayoutOffsetTests pins.
    public const string OffParamsData1  = "0x330";  // params_data[1]
    public const string OffParamsData2  = "0x338";  // params_data[2]

    // Auto-start readiness values written to OffInitState (must match Mimic.h
    // InitState). Unlike every other field here this one is NOT part of a
    // command round-trip — the DLL publishes it once during start-up so a CE Lua
    // bootstrap can poll for readiness with a pure memory read instead of
    // sleeping a fixed budget (no executeCodeEx ⇒ no CreateRemoteThread, which
    // games block).
    public const int InitIdle    = 0;   // DLL mapped; auto-start has not begun
    public const int InitRunning = 1;   // AOB scan / pipe-server start in progress
    public const int InitReady   = 2;   // init finished AND the pipe server is up
    public const int InitFailed  = 3;   // init finished but the pipe server failed
    public const int InitSkipped = 4;   // deliberately skipped — CE plugin host, or
                                        // another instance already owns the pipe

    // Cmd opcodes (must match Mimic.h Cmd enum).
    public const int CmdSetDebugCamera = 7;   // CMD_SET_DEBUG_CAMERA
    // audit #5 AF25 — 8 was the one gap in this table, even though the class summary above
    // names Teleport as a consumer. TeleportScriptGenerator and CoordLibraryScriptGenerator
    // each carried their own `private const int CmdTeleport = 8` while every other generator
    // referenced this class, so the single most-used opcode was the only one with no canonical
    // home and two places to update.
    public const int CmdTeleport       = 8;   // CMD_TELEPORT  (Wirbel — markers / POV / coord TP)
    public const int CmdProtect        = 9;   // CMD_PROTECT   (GodMode / Solitar)
    public const int CmdMovement       = 10;  // CMD_MOVEMENT  (Laufen knobs)
    public const int CmdFly            = 11;  // CMD_FLY       (Dunste — no-gravity 3D flight)
    public const int CmdForeground     = 12;  // CMD_FOREGROUND (Grausam — keep-foreground lock)
    public const int CmdQueryPtr       = 13;  // CMD_QUERY_PTR (resolve GWorld / GameEngine address)
    public const int CmdSeeThrough     = 14;  // CMD_SEETHROUGH (Schlacht — see-through occluders toggle)
    public const int CmdTime           = 15;  // CMD_TIME      (Hemmung — time dilation hold)

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
    // not bump it; do not restate it here, because two copies of a rule is how the
    // timeout defect got into seven files.
    //
    // Emitted into every generated script, which then checks itself against what
    // the DLL publishes. The DLL answers with a RANGE, so the two failure
    // directions can be told apart: too-old script => regenerate the .CT, too-old
    // DLL => update the DLL. Today the second case is silent corruption.
    public const int ContractVersion = 3;

    /// <summary>Exported symbol carrying the contract. Deliberately NOT a field of
    /// the mailbox struct: reading the layout version out of the struct whose layout
    /// is in question is circular, and the check has to run BEFORE any write.</summary>
    public const string ContractSymbol = "g_mailboxContract";

    // Field offsets within that symbol (Mimic::MailboxContract).
    public const string OffContractMagic = "0x00";
    public const string OffContractCur   = "0x04";
    public const string OffContractMin   = "0x08";

    /// <summary>'UE5C'. Proves the symbol resolved to the DLL's real data and not to
    /// a stale address from a previous injection — a failure mode measured on
    /// 2026-08-06, where CE held a mailbox address the DLL no longer owned.</summary>
    public const long ContractMagic = 0x43354555L;

    // Mailbox status values (must match Mimic.h MailboxStatus). Emitted into the
    // generated Lua so a timed-out script can say WHICH fault it hit instead of
    // guessing: the DLL sets Processing the instant its poller picks a command up
    // (Mimic.cpp:246) and Done+cmd=IDLE when it finishes (:1285-1286, :1296-1297),
    // so on timeout the status still distinguishes "never seen" from "wedged".
    public const int StatusIdle       = 0;      // untouched — the DLL never saw it
    public const int StatusDone       = 1;      // finished
    public const int StatusProcessing = 0xFF;   // picked up, still working

    /// <summary>
    /// Upper bound of the <c>while status ~= 1</c> busy-wait in every emitted mailbox
    /// round-trip, in REAL milliseconds — measured with <c>getTickCount()</c>, not
    /// counted in loop iterations.
    ///
    /// <para><b>Why that distinction is load-bearing.</b> The loop used to count one
    /// <c>sleep(1)</c> per iteration and bail at 10000 of them, on the assumption that
    /// <c>sleep(1)</c> sleeps 1 ms. **Measured in CE's Lua Engine on 2026-08-06
    /// (Windows 11 26200): it sleeps 15.47 ms.** <c>sleep(1)</c> through
    /// <c>sleep(10)</c> all cost the same ~15.47 ms — a hard floor at the default
    /// Windows tick — and <c>sleep(16)</c> jumps to 30.3 ms, i.e. it quantises to
    /// multiples of ~15.6 ms. CE does not raise the timer resolution for itself, and
    /// the DLL's own <c>timeBeginPeriod(1)</c> is per-process so it does not leak in.
    /// So "10000" was really <b>~155 seconds</b>, and a user with a stale mailbox
    /// address sat through two and a half minutes of a frozen Lua Engine window before
    /// being told anything.</para>
    ///
    /// <para><b>That floor is the Windows scheduler tick, NOT a per-machine constant</b>
    /// — which is what makes it safe to bake <see cref="MailboxPollTimeoutIters"/> from
    /// it. Measured on two deliberately different CPUs (a 9950X3D desktop and a 9955HX3D
    /// laptop, very different TDP): every value below the floor matched to three
    /// decimals — <c>sleep(1)</c> 15.470 on both, <c>sleep(5)</c> 15.630 on both. Only
    /// the 15/16 ms readings differed (18.59 vs 17.50, 30.31 vs 29.53), which is one
    /// tick of quantisation noise from a 15 ms timer measuring 15 ms events. The
    /// hypothesis that this was a performance-dependent per-PC offset is refuted: it is
    /// the ~64 Hz kernel tick, so EVERY user had the ~155 s timeout, not one machine.</para>
    /// </summary>
    public const int MailboxPollTimeoutMs = 10000;

    /// <summary>
    /// Fallback bound for a CE build with no <c>getTickCount</c>, counted in
    /// <c>sleep(1)</c> iterations. Derived from the measurement above:
    /// 650 x 15.47 ms ~= 10.1 s, matching <see cref="MailboxPollTimeoutMs"/>.
    /// Only reached if the probe fails — <c>getTickCount()</c> was verified present
    /// and returning ms in CE's Lua Engine on 2026-08-06.
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
    /// <para><b>Why 1500 and not the 100 this constant used to say.</b> The emitted loop
    /// counted <c>sleep(1)</c> iterations against this number, and <c>sleep(1)</c> costs
    /// 15.47 ms (see <see cref="MailboxPollTimeoutMs"/>), so the value that has actually
    /// SHIPPED since this wait was introduced is 100 x 15.47 ms ~= 1.55 s — never the
    /// 100 ms the comment claimed. Moving to a real deadline without moving the number
    /// would have been a silent 15x tightening of a live timeout, so the number moves to
    /// match the shipped behaviour instead. It is also the defensible one: the wait
    /// covers not only the nanosecond publication gap but a previous command that is
    /// genuinely still in flight, and mailbox commands are dispatched onto the GAME
    /// thread — a frame at 30 fps is already 33 ms, and a loading screen or a stalled
    /// game thread is far more. Every bail-out on this path is a soft "busy, try again",
    /// so erring long costs a wait and erring short costs a spurious failure.</para></summary>
    public const int MailboxIdleWaitMs = 1500;

    /// <summary>
    /// Fallback bound for the idle wait on a CE build with no <c>getTickCount</c>,
    /// counted in <c>sleep(1)</c> iterations. 100 x 15.47 ms ~= 1.55 s, matching
    /// <see cref="MailboxIdleWaitMs"/> — and, not by coincidence, the exact iteration
    /// count that shipped before the deadline became real.
    /// </summary>
    public const int MailboxIdleWaitIters = 100;
}
