# Bug / Leak Audit #3 — Findings & Fix Plan

> **Date:** 2026-07-14 · **Build:** 2168 · **Scope:** the ~168 files / ~17k lines changed since the
> [audit #2 baseline](../CLAUDE.md) (build 1872, commit `88ee170`) — the Solide / Hemmung / Linie /
> Schlacht / Grausam DLL modules and the Auto-Snapshot / Dump-Explorer / Live-Funcs / Teleport-Stealth UI.
>
> **Method:** 5 parallel area agents (DLL hold-workers · DLL hook-profiler · DLL pipe/mailbox/core ·
> UI services · UI viewmodels) produced raw findings; every finding was then **re-verified against
> current code by a second 10-cluster pass** that corrected each `file:line` citation and attached a
> concrete fix shape + effort/risk. Test baseline at audit time: **2525 green**.
>
> **Status:** all items below are **REPORTED, NOT YET FIXED.** Tick them off as they land (write up in
> [dev-log.md](dev-log.md), then delete the row here — this file is the working tracker).

**Tally:** 1 HIGH · 10 MEDIUM · 20 LOW · 1 INFO — 29 confirmed, 3 partially-confirmed.

> ### ✅ Adversarial double-confirm (2026-07-14, second independent pass)
> Every HIGH/MEDIUM + the LOW batch was re-checked by skeptics **mandated to REFUTE** (default stance
> "not a bug"; the HIGH got 3 diverse-lens skeptics). Outcome:
> - **HIGH H1 — CONFIRMED (high, ×3 lenses agree).** All three refutations collapse: disconnect does **not**
>   cancel the capture ct (manual capture's `externalCt = default(None)`; neither the ConnectionStateChanged
>   handler nor `DisconnectAsync` touch the Snapshot `_cts`), the in-flight chunk gets a **bare** OCE (not the
>   safe `IOException`), the producer catch is unfiltered, and `CompleteSnapshotAsync` commits `is_usable=1`
>   on an uncancelled ct. Downstream consumers filter **for** `IsUsable` with no completeness field, so the
>   truncated snapshot reads as clean. **Fix first.**
> - **9 MEDIUM CONFIRMED (keep):** M1, M2, M3, M4, M5, M7, M8, M9, M10.
> - **M6 REFUTED → DROPPED.** "Unstoppable after UI crash" is **not a bug**: hold persistence across
>   disconnect is by-design (`Solide.cpp:38`) **and family-uniform** — Solitar/Laufen/Hemmung/Wirbel are
>   *also* absent from both cleanup paths, so the Linie contrast is a false equivalence (Linie is a passive
>   PE-hook accumulator that must reset to stay cold, not a chosen hold). An off-switch exists: reconnect →
>   `reset_all_fields`, or a game restart drops the DLL. The *real* disconnect defect in this area is **M4**
>   (the `Tot` latch silently zombifies the hold), which stands. Auto-releasing a hold on a UI hiccup would
>   be worse (God Mode dropping mid-fight).
> - **L6 REFUTED → DROPPED.** Welford `m2` is **provably non-negative** (`Linie.cpp:51` increment =
>   `delta²·(gaps−1)/gaps ≥ 0`, accumulates from 0), so `sqrt` never sees a negative → NaN is unreachable;
>   and downstream is tolerant anyway (`DumpService` parses `cv` via `JsonNode ?? 0.0`). No reachable defect.
> - **5 LOW DOWNGRADED to optional cleanup** (real but cosmetic / no incorrect outcome): **L7** (phantom row
>   wiped by next `StartRecording`), **L9** (deliberate clip-release tradeoff; niche third-party case),
>   **L11** (SEH-guarded + self-corrects next tick; no crash), **L18** (DetectStats has no
>   `OnSelectedResultChanged`, so the missing detach has zero functional effect — only the no-`ct` point is a
>   real usability gap), **L20** (missing cancellation, but output is correct). L19/L21 remain cosmetic/style.
>
> **Net scheduled: 1 HIGH + 9 MEDIUM + 13 LOW = 23 fixes** (down from 32: 2 refuted, 7 → optional/cosmetic).

**Legend:** Effort **S**=hours · **M**=1 session · **L**=multi-session. Risk = chance the *fix* breaks
existing behaviour / perf.

**Cross-cutting root cause:** 8 of the 11 HIGH/MEDIUM items are **disconnect / shutdown lifecycle** —
either a *bare* `OperationCanceledException` from `PipeClient` (DisconnectAsync/Dispose call
`TrySetCanceled()` with **no token**, so it is indistinguishable from a real cancel *by the exception*;
only the ambient `ct.IsCancellationRequested` tells them apart), or a DLL worker whose state is not
reset/restored when the last client goes away. Consider fixing the shared root (an `IsUserCancel(ct)`
helper + a single `OnLastClientGone()` reset registry) rather than each site in isolation.

---

## Summary table

| ID | Sev | Eff/Risk | Module | One-line defect |
|----|-----|----------|--------|-----------------|
| H1 | 🔴 | M/med | SnapshotViewModel | A deliberate pipe DisconnectAsync/Dispose during an in-flight snapshot chunk fetch throws a bare… |
| M1 | 🟠 | S/med | Schlacht | SetEnabled(false) snapshots+moves out s_state.hiddenActors and un-hides it BEFORE StopWorkerLocked() joins the… |
| M2 | 🟠 | S/low | Schlacht | restore = std::move(s_state.hiddenActors) at 457 is unconditional and hiddenActors is cleared, but the un-hide… |
| M3 ⚠️ | 🟠 | S/low | Schlacht | Schlacht.h:11 promises actors are 'un-hidden on disable / disconnect', but no disconnect or shutdown path… |
| M4 | 🟠 | M/med | Tot | Solide's re-assert worker resolves live instances only through Aura::FindInstancesByClass, which bails with an… |
| M5 | 🟠 | M/med | Frieren | UE5_Shutdown joins the re-assert workers (492-497) then drains Stark and stops the pipe (501-502), but… |
| M6 | 🟠 | S/low | Solide | Solide is a stateful write-path module reachable only via 5 pipe commands (no CE mailbox in Mimic.cpp, no… |
| M7 | 🟠 | S/low | SnapshotViewModel | The outer capture catch (844) lacks a `when (ct.IsCancellationRequested)` filter, so a disconnect-OCE from the… |
| M8 | 🟠 | S/low | MainWindowViewModel | The 20s LKG proxy-confirm timer's callback checks only the current IsConnected flag, not the session identity… |
| M9 | 🟠 | S/low | TeleportViewModel | OnExperimentalGateChanged's disable branch force-disables Keep-Foreground, Fly, and SeeThrough but never… |
| M10 | 🟠 | M/low | PropertySearchViewModel | The client-side ResultFilter box matches the whole trimmed filter string via one case-insensitive Contains per… |
| L1 | 🟡 | S/low | Solitar | SetGodMode mutates s_wantGod under s_mutex, releases it, then decides StartWorker()/StopWorker() on the `on`… |
| L2 | 🟡 | M/low | Solide | Solide::FR_ERR_WEAK_PTR (and FR_ERR_REFLECT/FR_ERR_WRITE) are never returned by AddForce — AddForce always… |
| L3 ⚠️ | 🟡 | S/med | Solide | ApplyJobLocked calls Aura::FindInstancesByClass(job.className, exactMatch=false, ...) (Solide.cpp:253) so the… |
| L4 | 🟡 | M/med | Solide | The restore base is captured exactly once from the first instance ever resolved (Solide.cpp:200 bool, 239… |
| L5 | 🟡 | S/low | Linie | nowMs is stamped in HookedProcessEvent before RecordCall acquires g_mu, so two threads dispatching PE for the… |
| L6 ⚠️ | 🟡 | S/low | Linie | Snapshot computes variance = m2/gaps without clamping to >=0 before std::sqrt, so a floating-point-negative m2… |
| L7 | 🟡 | S/low | Linie | RecordCall checks IsRecording() only via Stark's unlocked relaxed gate (Stark.cpp:152) and never re-checks… |
| L8 | 🟡 | S/low | Grausam | SubclassEnumProc calls the synchronous, timeout-less GetWindowTextW on a same-process window while… |
| L9 | 🟡 | S/low | Grausam | While locked and backgrounded, every game ClipCursor(rect) is converted to g_origClipCursor(nullptr), which… |
| L10 | 🟡 | M/med | Grausam | (A) SubclassAllGameWindows runs only inside the enable path (line 240); windows created after enable are never… |
| L11 | 🟡 | M/low | Schlacht | s_state.hiddenActors (345) stores raw uintptr_t actor pointers that the diff loop (396-397) later re-passes to… |
| L12 | 🟡 | S/low | Fern | strAllocs is a std::vector<void*> of raw malloc pointers whose ownership is only released by the explicit free… |
| L13 | 🟡 | S/low | TeleportViewModel | SetConnected(false) resets every other card badge… |
| L14 | 🟡 | S/low | PropertySearchViewModel +  | BatchFindFuncsAsync cancels then replaces _xrefBatchCts with a new CancellationTokenSource without disposing… |
| L15 | 🟡 | S/low | SnapshotViewModel | The size-estimate catch (1207) is a bare `catch (OperationCanceledException) { EstimateText = "Estimate… |
| L16 | 🟡 | S/low | LiveFuncsViewModel | IsRecording is never reset when the pipe disconnects, and AutoStopOnLeaveAsync (fire-and-forget from… |
| L17 | 🟡 | S/low | ObjectTreeViewModel | LoadAsync (:334) and SearchAsync (:418) set FilterText="" on navigation-to-new-data without calling… |
| L18 | 🟡 | M/low | DetectStatsViewModel | Both Results.Clear() sites — DetectAsync (:100) and ApplyFilter (:321) — clear the selection-bound grid without… |
| L19 | 🟡 | S/low | LiveFuncsViewModel | Clear() does Results.Clear() (:284) then SelectedResult=null (:285) — the inverse of ApplyFilter which detaches… |
| L20 | 🟡 | S/low | MainWindowViewModel | ExportDumpAllAsync calls DumpAllService.GenerateAsync(_dump, _engineState, fs, options, progress) at :3029… |
| L21 | ⚪ | S/low | DumpExplorerViewModel | ApplyFilter splits SearchText on space + lowercases each term (:395-397) then MatchesTerms (:443-448) does a… |

⚠️ = partially-confirmed (claim refined during verification — see the item's **Note**).

**Progress:** ✅ **ALL SCHEDULED DONE — 1 HIGH + 10 MEDIUM + 13 LOW.** UI LOWs L13–L17 (`8bd33f8`); Solide LOWs
L2/L3/L4 (`408fd2d`); DLL LOWs L1/L5/L8/L10/L12 (`7f3898f`); adversarial-verify followups (`3362636`: L4
prune-guard + L10 GFW-hook race). Two adversarial-verify passes (DLL M-cluster + DLL LOW batch) each caught a
real bug that was fixed. **Remaining = only the 7 optional/cosmetic downgrades (L6 dropped; L7/L9/L11/L18/L19/L20/L21).**
DLL fixes (M1–M5 + the DLL/Solide LOWs) await in-game verification. **Status lives in ONE place** —
[the verification register](verification-register.md),
which carries a per-item row with its acceptance test. Do not track a second status here: this
sentence said "await in-game verification" for 13 fixes at once, so nothing could ever be ticked off
individually and no one could tell which had actually been exercised.

---

## 🔴 HIGH

### H1 — Disconnect during snapshot capture silently truncates but saves as usable/Success

> **✅ FIXED — commit `452d3ff` (build 2182).** Producer catch now filters on
> `lct.IsCancellationRequested`; a bare disconnect-OCE faults the producer so `CompleteSnapshotAsync` is
> skipped and the outer catch deletes the partial. Regression test
> `Capture_DisconnectMidStream_DoesNotSaveUsablePartial`. **Verification note corrected:** `is_usable`
> defaults to **1** and `CreateSnapshotAsync` never sets it, so an un-finalised row is *usable*, not
> auto-cleaned — filtering the **outer** catch too would reroute the disconnect to the generic handler
> (which does not delete) and re-leave a usable partial, so it was deliberately left unfiltered (that is
> M7's separate concern).

**🔴 HIGH** · Effort **M** · Risk **med** · *confirmed* · Module: SnapshotViewModel (CaptureCoreAsync producer/consumer) + PipeClient

- **Defect:** A deliberate pipe DisconnectAsync/Dispose during an in-flight snapshot chunk fetch throws a bare OperationCanceledException (TrySetCanceled with no token) that the producer's unfiltered catch swallows, so the channel completes normally, Task.WhenAll passes, and the half-captured snapshot is finalized usable=true with outcome=Success.
- **Failure scenario:** User clicks Disconnect (or the app disposes the pipe) while the producer is awaiting _dump.SnapshotChunkAsync at line 692. PipeClient.DisconnectAsync (line 104) / Dispose (line 345) calls kvp.Value.TrySetCanceled() with CancellationToken.None; the in-flight `return await tcs.Task` (PipeClient.cs:198) throws a bare OCE. The capture's own ct is NOT cancelled (disconnect handler doesn't touch it), so lct.IsCancellationRequested is false, yet the producer catch (728) has no filter and swallows it. finally TryComplete() (729) completes the channel with no exception; consumer drains normally; Task.WhenAll (779) sees both tasks completed; driftDetected is false on a clean partial so CompleteSnapshotAsync(..., !driftDetected=true, ct) (786) commits it usable; outcome=Success (839). The partial snapshot later poisons SPC/Pivot/diff as if complete. Unexpected pipe DEATH is safe because ReadLoop faults pendings with IOException, not OCE.
- **Fix:** Discriminate disconnect-OCE from genuine cancel using the ambient token (the pipe's TrySetCanceled carries no token in BOTH cases, so filter on ct/lct, not the exception's token). Change the producer catch at 728 to `catch (OCE) when (lct.IsCancellationRequested)` so a disconnect-OCE propagates and faults the producer task; let Task.WhenAll rethrow it, and gate CompleteSnapshotAsync so it never runs (or marks unusable) when the producer aborted abnormally. Must preserve the intentional graceful-cap / low-disk partial-keep path (capReached/diskLowReached) which legitimately finalizes usable — key that on the flags, not on 'partial'. Ensure the un-finalized snapshot row is treated as unusable (auto-cleaned by DeleteUnusableSnapshotsAsync at capture start).
- **Note:** Citations exact as described (728/779/786/839, PipeClient 104/345). Verified BeginSnapshotAsync (DumpService.cs:2146) and SnapshotChunkAsync (2158) route through _pipe.SendAsync with no OCE translation. Confirmed the fix cannot rely on the OCE's own token: SendAsync's ct.Register callback (PipeClient.cs:151) also calls TrySetCanceled() with no token, so both real-cancel and disconnect yield CancellationToken.None — only the ambient ct.IsCancellationRequested distinguishes them. Interacts with M7's fix at line 844 (a propagated OCE must not be re-swallowed there).
- **Where:** [`ui/UE5DumpUI/ViewModels/SnapshotViewModel.cs:728`](../ui/UE5DumpUI/ViewModels/SnapshotViewModel.cs:728), [`ui/UE5DumpUI/ViewModels/SnapshotViewModel.cs:729`](../ui/UE5DumpUI/ViewModels/SnapshotViewModel.cs:729), [`ui/UE5DumpUI/ViewModels/SnapshotViewModel.cs:779`](../ui/UE5DumpUI/ViewModels/SnapshotViewModel.cs:779), [`ui/UE5DumpUI/ViewModels/SnapshotViewModel.cs:786`](../ui/UE5DumpUI/ViewModels/SnapshotViewModel.cs:786), [`ui/UE5DumpUI/ViewModels/SnapshotViewModel.cs:839`](../ui/UE5DumpUI/ViewModels/SnapshotViewModel.cs:839), [`ui/UE5DumpUI/Services/PipeClient.cs:104`](../ui/UE5DumpUI/Services/PipeClient.cs:104), [`ui/UE5DumpUI/Services/PipeClient.cs:345`](../ui/UE5DumpUI/Services/PipeClient.cs:345), [`ui/UE5DumpUI/Services/PipeClient.cs:198`](../ui/UE5DumpUI/Services/PipeClient.cs:198)

## 🟠 MEDIUM

### M1 — Schlacht disable<->Tick race repopulates hiddenActors after restore, leaking hidden actors

> **✅ FIXED — commit `0f6f6e0` (build 2188, with M2/M3, NEEDS IN-GAME VERIFY).** `SetEnabled(false)` now
> quiesces the worker (set `active=false` + `StopWorkerLocked`/join) **before** snapshotting + restoring the
> hidden set, so no in-flight `Tick` can repopulate `hiddenActors` after the restore.

**🟠 MEDIUM** · Effort **S** · Risk **med** · *confirmed* · Module: Schlacht (SeeThrough)

- **Defect:** SetEnabled(false) snapshots+moves out s_state.hiddenActors and un-hides it BEFORE StopWorkerLocked() joins the worker, while Tick() re-checks nothing after the active-gate — a mid-flight Tick can hide a fresh occluder and write it into hiddenActors after the restore already ran, leaving those actors hidden with no un-hide.
- **Failure scenario:** See-through active. User toggles off. Worker is inside Tick() (passed the active-gate at 411-412): it calls InvokeSetHidden(A,true) at 397 but has not yet written hiddenActors (400). Concurrently SetEnabled(false) locks s_mutex at 456, moves out the still-empty hiddenActors into restore, sets active=false, unlocks; the un-hide loop finds nothing to restore; then Tick locks s_mutex and writes hiddenActors={A}; StopWorkerLocked joins. A stays hidden. Next SetEnabled(true) clears hiddenActors at 444 without un-hiding, so A is invisible for the rest of the session.
- **Fix:** Reorder disable so the worker is quiesced before restore: set active=false, then StopWorkerLocked() (join, s_mutex released) FIRST, then snapshot+move+restore hiddenActors under s_mutex — once joined, no Tick can repopulate. Additionally have Tick re-check active/stop under s_mutex before writing hiddenActors at 399-403, and defensively un-hide any residual hiddenActors on the enable path before the clear at 444.
- **Note:** Reordering the join before the restore is safe re: the documented lock order (s_mutex must not be held during join, and it isn't). Line refs drifted slightly from the claim: the Tick write is at 399-403 and the active-gate at 411-412.
- **Where:** [`dll/src/Schlacht.cpp:454-463`](../dll/src/Schlacht.cpp:454), [`dll/src/Schlacht.cpp:464-465`](../dll/src/Schlacht.cpp:464), [`dll/src/Schlacht.cpp:466`](../dll/src/Schlacht.cpp:466), [`dll/src/Schlacht.cpp:411-414`](../dll/src/Schlacht.cpp:411), [`dll/src/Schlacht.cpp:394-403`](../dll/src/Schlacht.cpp:394), [`dll/src/Schlacht.cpp:444`](../dll/src/Schlacht.cpp:444)

### M2 — Schlacht disable while game thread stalled discards the restore set, actors never un-hidden

> **✅ FIXED — commit `0f6f6e0` (build 2188, with M1/M3); enable-path leak fixed in `61e1f7f`.** When the game
> thread is unresponsive at disable, `SetEnabled(false)` now **keeps** `hiddenActors` (no move/clear) + `WARN`s
> instead of discarding it. **Adversarial-verify follow-up (`61e1f7f`):** the enable-recovery must only pull
> the leftover out when the thread is responsive — otherwise it move/cleared the record but skipped the
> (responsive-gated) un-hide, orphaning the actors; now it leaves `hiddenActors` intact when unresponsive so
> the worker's first live tick self-heals on resume.

**🟠 MEDIUM** · Effort **S** · Risk **low** · *confirmed* · Module: Schlacht (SeeThrough)

- **Defect:** restore = std::move(s_state.hiddenActors) at 457 is unconditional and hiddenActors is cleared, but the un-hide loop at 464 is gated on Stark::IsGameThreadResponsive(); when the gate is false the moved-out local `restore` goes out of scope discarded — the hidden actors are neither restored nor retained, so they remain hidden with no record.
- **Failure scenario:** Game backgrounded/paused (exactly the case Grausam/see-through target). See-through had hidden wall actors. User disables see-through: hiddenActors is moved into `restore` and cleared; IsGameThreadResponsive() returns false so the un-hide loop is skipped; `restore` is destroyed at function exit. When the game resumes, the actors are still SetActorHiddenInGame(true) and s_state has no memory of them, so nothing ever un-hides them.
- **Fix:** Do not lose the set when the un-hide can't run: if !IsGameThreadResponsive(), leave hiddenActors populated in s_state (skip the move/clear) so a later SetEnabled/worker cycle can retry the un-hide, and log a warning that actors remain hidden. Alternatively keep the worker alive with a pending-restore flag to un-hide when the thread becomes responsive.
- **Note:** Confirmed exactly as described; move at 457, gate at 464, clear/state-reset at 459-462.
- **Where:** [`dll/src/Schlacht.cpp:457`](../dll/src/Schlacht.cpp:457), [`dll/src/Schlacht.cpp:459-462`](../dll/src/Schlacht.cpp:459), [`dll/src/Schlacht.cpp:464-465`](../dll/src/Schlacht.cpp:464)

### M3 — Schlacht never un-hides on disconnect or shutdown despite header contract

> **✅ FIXED — commit `0f6f6e0` (build 2188, with M1/M2).** `Schlacht::SetEnabled(false)` is now called from
> Fern's last-client cleanup and from `UE5_Shutdown` (before `Stark::Shutdown` so the un-hide invokes still
> dispatch); an early-out makes it a cheap no-op when see-through was never enabled, so it's safe to call
> blindly. (The CE-Lua Disable path already routed through `SetEnabled(false)` — the partially-confirmed note.)

**🟠 MEDIUM** · Effort **S** · Risk **low** · *partially-confirmed* · Module: Schlacht (SeeThrough) / Fern (PipeServer) / Frieren (Shutdown)

- **Defect:** Schlacht.h:11 promises actors are 'un-hidden on disable / disconnect', but no disconnect or shutdown path un-hides: Fern last-client cleanup (757-761) and PipeServer::Stop (497-500) reset only Radar/GroupSessionManager/Linie; UE5_Shutdown (Frieren.cpp:497) calls Schlacht::StopWorker() which only joins the worker (494-497) and never un-hides. StopWorker's own doc-comment (Schlacht.h:56-58) implies teardown but performs no restore.
- **Failure scenario:** User enables see-through (walls hidden), then closes/disconnects the UI. Fern's last-client block drops scan/PE sessions but never calls Schlacht::SetEnabled(false), so the worker keeps running with active=true, keeps hiding new occluders, and the already-hidden actors stay hidden. On process/DLL teardown, UE5_Shutdown joins the worker without un-hiding, so the game continues with those actors invisible.
- **Fix:** Call Schlacht::SetEnabled(false) (which already un-hides + joins) from Fern's last-client cleanup (757-761) and PipeServer::Stop (497-500), and un-hide before/inside teardown in UE5_Shutdown (e.g. SetEnabled(false) before Schlacht::StopWorker, or fold a RestoreAll into StopWorker). Interacts with M2 (stalled-thread un-hide may still no-op at shutdown).
- **Note (verification):** Core defect (no un-hide on disconnect OR shutdown, contract violated) is confirmed. The claim's 'or CE-Lua Disable' sub-clause is WRONG: the CE-Lua toggle at Mimic.cpp:955 routes disable through Schlacht::SetEnabled(false), which does un-hide. Hence partially-confirmed.
- **Where:** [`dll/src/Schlacht.h:11`](../dll/src/Schlacht.h:11), [`dll/src/Schlacht.cpp:494-497`](../dll/src/Schlacht.cpp:494), [`dll/src/Fern.cpp:757-761`](../dll/src/Fern.cpp:757), [`dll/src/Fern.cpp:497-500`](../dll/src/Fern.cpp:497), [`dll/src/Frieren.cpp:497`](../dll/src/Frieren.cpp:497), [`dll/src/Mimic.cpp:955`](../dll/src/Mimic.cpp:955)

### M4 — Shared per-command cancel latch zombifies Solide hold during disconnect window

> **✅ FIXED — commit `7edea28` (build 2187, NEEDS IN-GAME VERIFY).** Added `Tot::MarkBackgroundWorker()`
> (thread-local `t_backgroundWorker`) called once at the top of each re-assert worker loop
> (Solide/Hemmung/Laufen/Solitar/Dunste/Schlacht); `Tot::Requested()` returns `g_shutdown`-only on a marked
> thread, so a re-assert worker's `FindInstancesByClass` no longer bails on the per-command latch — while a
> pipe command calling the *same* `Resolve*` helper still honours it (thread-local, no signature churn).
> **Deliberately did NOT** reset `g_perCommand` on last-disconnect (the finding's secondary idea): an
> orphaned in-flight scan on the dropped connection must keep seeing the cancel until it unwinds (Tot's
> problem #2). Verify: force a field → disconnect the UI → the hold keeps asserting → reconnect → still held.

**🟠 MEDIUM** · Effort **M** · Risk **med** · *confirmed* · Module: Tot (Cancellation) ↔ Solide (ForceField) ↔ Fern/Aura

- **Defect:** Solide's re-assert worker resolves live instances only through Aura::FindInstancesByClass, which bails with an empty result on its very first loop iteration whenever the shared Tot::g_perCommand cancel flag is latched, so a hold silently does zero writes for the entire disconnected window.
- **Failure scenario:** A bulk-lane command is in flight when the UI drops; the monitor calls Tot::RequestPerCommand() (Fern.cpp:550). g_perCommand stays latched until a new session connects into an EMPTY registry (Fern.cpp:621 firstConn-only reset). Meanwhile every Solide worker tick (Solide.cpp:295 -> ApplyJobLocked -> FindInstancesByClass at :253) hits Tot::Requested() at n=0 (Aura.cpp:1343, (0 & 0xFFF)==0) and returns held=0 -> no re-assert writes -> the game re-writes the field and the 'survive UI reconnect' hold is dead for the whole window. Worse: if only the bulk lane drops while the interactive lane stays connected, firstConn=m_conns.empty() is false on reconnect, so ResetPerCommand never runs and the latch stays stuck while the UI looks fully connected.
- **Fix:** Stop background hold workers (Solide/Hemmung/Laufen/Solitar) from consulting the client-command cancel flag: give Aura::FindInstancesByClass an opt-out (e.g. an ignoreCancel/fromWorker param) that skips the Tot::Requested() poll for hold-worker calls, or route worker instance-resolution through a variant that only honours g_shutdown, not g_perCommand. Also fix the stale Tot.h:25/33 comment. Secondary hardening: reset g_perCommand when the connection registry empties (full disconnect) so a normal reconnect isn't the only cure.
- **Note:** Claim is accurate on every cited point. Tot::Requested composition (g_perCommand||g_shutdown), firstConn-only reset, the n=0 empty-bail, and Solide.cpp:253 being the sole instance source all confirmed. The Tot.h comment 'reset at the start of each command' is confirmed stale (reset happens in Start()/firstConn only). The single-lane-drop stuck-latch aggravation is real and would also silently kill all bulk-lane cancellable scans, not just Solide.
- **Where:** [`dll/src/Tot.h:34`](../dll/src/Tot.h:34), [`dll/src/Tot.h:44-47`](../dll/src/Tot.h:44), [`dll/src/Tot.h:25`](../dll/src/Tot.h:25), [`dll/src/Fern.cpp:548-551`](../dll/src/Fern.cpp:548), [`dll/src/Fern.cpp:621`](../dll/src/Fern.cpp:621), [`dll/src/Fern.cpp:422`](../dll/src/Fern.cpp:422), [`dll/src/Aura.cpp:1339-1346`](../dll/src/Aura.cpp:1339), [`dll/src/Solide.cpp:253`](../dll/src/Solide.cpp:253), [`dll/src/Solide.cpp:295`](../dll/src/Solide.cpp:295)

### M5 — UE5_Shutdown joins hold workers before stopping the pipe; a mutator in the window respawns an unjoined worker

> **✅ FIXED — commit `61e1f7f` (build 2189, NEEDS IN-GAME VERIFY).** `Tot::RequestShutdown()` now runs at the
> TOP of `UE5_Shutdown` (also speeds the joins — in-flight scans bail), and every module's `StartWorker*` (the
> single thread-spawn chokepoint) is gated on `Tot::ShutdownRequested()`, so no worker can (re)spawn during
> the join→pipe-stop window; cleared by `Fern::Start` on re-enable. **Adversarially verified** (5 lenses, no
> deadlock / lock-order / M3↔M5 or M4↔M5 regression): `EnqueueInvoke` gates on Stark's own hook flag (flipped
> only by the later `Stark::Shutdown`), so setting `g_shutdown` early does NOT break the M3 un-hide. The same
> pass also caught + fixed a leak in the M1/M2 enable-recovery (see M2).

**🟠 MEDIUM** · Effort **M** · Risk **med** · *confirmed* · Module: Frieren (ExportAPI/UE5_Shutdown) ↔ Solide/Hemmung/Laufen/Solitar

- **Defect:** UE5_Shutdown joins the re-assert workers (492-497) then drains Stark and stops the pipe (501-502), but Tot::RequestShutdown() is set only inside Fern::Stop (Fern.cpp:445) at the very end, so during the window m_running is still true, detached pipe handlers keep dispatching, and no worker-spawning mutator has a shutdown gate.
- **Failure scenario:** While the UI is connected, UE5_Shutdown runs. After Solide::StopWorker() joins the hold worker (Frieren.cpp:495), a detached pipe handler dispatches a queued force_field (Fern.cpp:4602 -> Solide::AddForce). AddForce gates only on g_cachedGWorld (Solide.cpp:324), which is never cleared at shutdown (only set at Frieren.cpp:117/426), so it proceeds to StartWorkerLocked() (Solide.cpp:349) and re-spawns the just-joined worker. UE5_Shutdown finishes (Stark::Shutdown -> pipe Stop -> return) without ever joining it. If the DLL is then unloaded the orphan thread runs unmapped code -> crash; even without unload it is an unstoppable leaked worker. The same holds for set_god_mode/set_movement_multiplier/set_time_dilation (Solitar/Laufen/Hemmung siblings).
- **Fix:** Close the window before joining: call Tot::RequestShutdown() (or a dedicated s_shuttingDown latch) at the TOP of UE5_Shutdown, and gate the four worker-spawning mutators (Solide::AddForce, Hemmung::SetDilation, Laufen::SetMultiplier, Solitar::SetGodMode) to return an error instead of calling StartWorkerLocked when that latch is set. Simpler alternative: reorder so the pipe server is stopped (handlers drained) before the hold-worker joins — but that must preserve the intentional Stark::Shutdown()-before-pipe-Stop() ordering (Frieren.cpp:498-500 comment) that unblocks pipe threads parked on EnqueueInvoke, so the top-of-shutdown gate is the lower-risk change.
- **Note:** Ordering and citations all confirmed. g_cachedGWorld is confirmed never cleared during shutdown. Realism caveat: the crash-on-unmapped-code worst case requires the DLL to actually be unloaded (FreeLibrary) after UE5_Shutdown; in the common CE case the module stays loaded and the concrete symptom is a leaked, unstoppable re-assert worker (which itself becomes a crash if the process later unloads or the game exits). Race window is non-trivial because Stark::Shutdown() (drain queue + MinHook uninit) runs inside it with m_running still true.
- **Where:** [`dll/src/Frieren.cpp:489-503`](../dll/src/Frieren.cpp:489), [`dll/src/Frieren.cpp:492-497`](../dll/src/Frieren.cpp:492), [`dll/src/Frieren.cpp:501-502`](../dll/src/Frieren.cpp:501), [`dll/src/Fern.cpp:439-445`](../dll/src/Fern.cpp:439), [`dll/src/Fern.cpp:4602`](../dll/src/Fern.cpp:4602), [`dll/src/Solide.cpp:321-349`](../dll/src/Solide.cpp:321), [`dll/src/Frieren.cpp:117`](../dll/src/Frieren.cpp:117)

> **⛔ DROPPED by double-confirm — not a bug (working-as-designed; see the double-confirm banner above).**

### M6 — Solide force-field hold has no disconnect ClearAll and no mailbox/export off-switch

**🟠 MEDIUM** · Effort **S** · Risk **low** · *confirmed* · Module: Solide (cleanup in Fern)

- **Defect:** Solide is a stateful write-path module reachable only via 5 pipe commands (no CE mailbox in Mimic.cpp, no Frieren C-ABI export — Frieren.cpp:495 only calls StopWorker at DLL shutdown), and neither Fern last-client cleanup path calls Solide::ClearAll(), so a UI crash leaves the 300ms re-assert worker writing game memory with no way to stop it short of reconnect or game restart.
- **Failure scenario:** User forces a field via Property Search 'Force' (a Solide job is added, worker starts writing every 300ms). The UI process crashes/is killed. Fern's disconnect handler (last==true, line 757-761) resets Radar + Linie but not Solide, so s_jobs persists and WorkerLoop keeps re-asserting the held value forever. There is no CE .CT / export control surface to disable it from a fresh CE session either.
- **Fix:** Reconcile the deliberate 'survive UI reconnect' comment (Solide.cpp:38) with unstoppable-after-crash: either add Solide::ClearAll() alongside Linie::Reset() in both last-client paths (Fern::Stop ~line 500 and the disconnect handler ~line 760), OR add a CE mailbox command / Frieren export off-switch so a reconnecting UI or CE .CT can call reset_all_fields. Preferred: clear on last-client-gone (matches Linie, and the pipe already exposes reset_all_fields for a reconnected UI).
- **Note:** Design tension: Solide.cpp:38 documents the persistence as intentional ('survive UI reconnect; live for the game process'), unlike Linie which resets. So the fix is a policy choice, not a clear bug-fix — but the finding's core point (no off-switch after a crash) is real. Clearing on last-client-disconnect changes documented behavior; a mailbox/export off-switch preserves reconnect-survival while fixing reachability. Mimic.cpp dispatch (cases at 193-208) has no Solide case; Frieren.cpp references Solide only for StopWorker.
- **Where:** [`dll/src/Fern.cpp:757-761`](../dll/src/Fern.cpp:757), [`dll/src/Fern.cpp:497-503`](../dll/src/Fern.cpp:497), [`dll/src/Solide.cpp:38`](../dll/src/Solide.cpp:38), [`dll/src/Solide.cpp:281-304`](../dll/src/Solide.cpp:281), [`dll/src/Frieren.cpp:495`](../dll/src/Frieren.cpp:495), [`dll/src/Renge.h:124-128`](../dll/src/Renge.h:124)

### M7 — Auto-snapshot loop wedges (stuck enabled, manual buttons disabled) on disconnect-OCE

> **✅ FIXED — commit `1b108a9` (build 2183).** The outer OCE catch now reports a disconnect (our token
> NOT cancelled) as `Failed` instead of `Cancelled`, so the auto-loop routes to `case Failed` →
> `StopAutoSnapshot()` and stops cleanly; `case Cancelled` also calls `StopAutoSnapshot()` defensively.
> **Also hardened the H1 family:** the partial delete+reclaim is factored into a `RemovePartialAsync` local
> and now runs on the generic `catch (Exception)` too, so a non-OCE mid-capture failure (unexpected pipe
> death → `IOException`, or a between-chunks disconnect → `InvalidOperationException`) can no longer leave a
> usable `is_usable=1` partial. Regression test `AutoSnapshot_DisconnectMidCapture_StopsLoopWithoutWedge`
> (new `internal AutoLoopTaskForTests` hook drives the real loop).

**🟠 MEDIUM** · Effort **S** · Risk **low** · *confirmed* · Module: SnapshotViewModel (CaptureCoreAsync outer catch + RunAutoLoopAsync)

- **Defect:** The outer capture catch (844) lacks a `when (ct.IsCancellationRequested)` filter, so a disconnect-OCE from the directly-awaited BeginSnapshotAsync sets outcome=Cancelled; RunAutoLoopAsync's `case Cancelled: return;` then exits the loop without calling StopAutoSnapshot(), leaving AutoSnapshotEnabled stuck true, _autoCts leaked, and the manual Capture/Estimate buttons disabled.
- **Failure scenario:** Auto-snapshot loop is running. Pipe disconnects while `int total = await _dump.BeginSnapshotAsync(dataType, ct)` (line 642) is in flight, before the producer/consumer start. The bare OCE (PipeClient TrySetCanceled) propagates directly to the outer catch (844); ct.IsCancellationRequested is false (disconnect doesn't cancel the capture ct) but the unfiltered catch treats it as a user cancel -> outcome=Cancelled (846). Back in RunAutoLoopAsync, line 1067 `if (ct.IsCancellationRequested) break;` does NOT fire (the auto-loop token isn't cancelled — the disconnect handler at MainWindowViewModel.cs:1919-1934 doesn't call Snapshot.StopAutoSnapshot() nor clear _engineState). Execution reaches `case CaptureOutcome.Cancelled: return;` (1080-1081), returning without StopAutoSnapshot(). Result: AutoSnapshotEnabled stays true (so CanManualCapture=false disables manual capture/estimate), _autoCts left non-null/un-cancelled/un-disposed, CanEditAutoSettings false. The loop is silently dead while the UI still shows auto ON. Only a manual toggle-off or a reconnect (SetEngineState calls StopAutoSnapshot) recovers it. A GENUINE user cancel is unaffected because Cancel() (line 962-968) calls StopAutoSnapshot() first, so line 1067 breaks before the switch — meaning the 1080 branch is only ever reached by the spurious disconnect-OCE.
- **Fix:** Add `when (ct.IsCancellationRequested)` to the outer catch at 844 so a disconnect-OCE falls through to the general `catch (Exception)` (867) -> outcome=Failed, which RunAutoLoopAsync's `case Failed` (1075) already handles by calling StopAutoSnapshot() and returning cleanly. (The `case Cancelled: return;` at 1080 is then reached only when the genuine break at 1067 was somehow skipped; optionally call StopAutoSnapshot() there too as belt-and-suspenders.) Same one-token discriminator as H1.
- **Note:** Citations exact (844/846/1080; BeginSnapshotAsync at 642; disconnect handler MainWindowViewModel.cs:1919). Confirmed the comment at line 1081 ('user cancelled the in-flight capture') is misleading — the genuine-cancel path breaks at 1067 first because Cancel() cancels the auto-loop token via StopAutoSnapshot(). This fix and H1's producer-catch fix should be applied together (a producer OCE that reaches WhenAll would otherwise be re-swallowed at 844).
- **Where:** [`ui/UE5DumpUI/ViewModels/SnapshotViewModel.cs:844`](../ui/UE5DumpUI/ViewModels/SnapshotViewModel.cs:844), [`ui/UE5DumpUI/ViewModels/SnapshotViewModel.cs:846`](../ui/UE5DumpUI/ViewModels/SnapshotViewModel.cs:846), [`ui/UE5DumpUI/ViewModels/SnapshotViewModel.cs:642`](../ui/UE5DumpUI/ViewModels/SnapshotViewModel.cs:642), [`ui/UE5DumpUI/ViewModels/SnapshotViewModel.cs:1080`](../ui/UE5DumpUI/ViewModels/SnapshotViewModel.cs:1080), [`ui/UE5DumpUI/ViewModels/MainWindowViewModel.cs:1919`](../ui/UE5DumpUI/ViewModels/MainWindowViewModel.cs:1919)

### M8 — LKG proxy-confirm timer records a crashed proxy on a later unrelated reconnect

> **✅ FIXED — commit `ad9a7e7` (build 2184).** Added `_sessionEpoch` (bumped on every disconnect); the
> dwell captures the epoch at schedule time and records only via the pure gate
> `ShouldConfirmProxy(IsConnected, scheduledEpoch, currentEpoch)` — same session still up + no disconnect
> since. Bumping only on disconnect avoids an ordering hazard with `ApplyEngineState`. Also disposes+nulls
> the timer BEFORE the early returns, on disconnect, and in `Dispose()`. Unit tests `MainWindowProxyConfirmTests`
> (4 cases).

**🟠 MEDIUM** · Effort **S** · Risk **low** · *confirmed* · Module: MainWindowViewModel (ScheduleProxyConfirmation — ProxyDeploy LKG gate)

- **Defect:** The 20s LKG proxy-confirm timer's callback checks only the current IsConnected flag, not the session identity that scheduled it, and the timer is never cancelled on disconnect nor on a non-proxy reconnect nor in Dispose(), so a proxy that crashed the game can still be recorded as confirmed-working.
- **Failure scenario:** Proxy loads game A and connects; the proxy crashes game A at t=5s (disconnect does not cancel the pending timer); user re-injects (or connects any other game via a non-proxy path) before t=20s. The reconnect hits the early return at line 2560 (non-proxy LoadMode) and never reaches the Dispose at line 2566, so the stale timer from A still fires; the callback sees IsConnected==true and calls ProxyDeploy.RecordConfirmedProxy(exeName, proxyDll) with A's captured values, marking the crashed proxy as confirmed-working for game A.
- **Fix:** Capture a session identity at schedule time (state.PeHash, or an int generation counter bumped on every connect/disconnect) and re-check it against the current session inside the callback before RecordConfirmedProxy (bail if it changed). Move _proxyConfirmTimer?.Dispose() above all early returns in ScheduleProxyConfirmation (line ~2556), and also cancel/null it in the ConnectionStateChanged !connected branch (lines 1928-1932). Add _proxyConfirmTimer?.Dispose() to Dispose() (lines 1951-1973) per the mandate comment.
- **Note:** All three sub-claims verified against current code; the cited line ranges (2554-2575 schedule, 2556-2564 early returns, 1951-1973 Dispose) are accurate as written. The closure does capture exeName/proxyDll per-schedule (so it never records the WRONG proxy name for the wrong exe), but it still records A's proxy for A even though A's proxy crashed — the IsConnected check is satisfied by any live connection, which is the actual defect. Disconnect handler at 1919-1934 also confirmed to not cancel the timer.
- **Where:** [`ui/UE5DumpUI/ViewModels/MainWindowViewModel.cs:2554`](../ui/UE5DumpUI/ViewModels/MainWindowViewModel.cs:2554), [`ui/UE5DumpUI/ViewModels/MainWindowViewModel.cs:2556`](../ui/UE5DumpUI/ViewModels/MainWindowViewModel.cs:2556), [`ui/UE5DumpUI/ViewModels/MainWindowViewModel.cs:2560`](../ui/UE5DumpUI/ViewModels/MainWindowViewModel.cs:2560), [`ui/UE5DumpUI/ViewModels/MainWindowViewModel.cs:2564`](../ui/UE5DumpUI/ViewModels/MainWindowViewModel.cs:2564), [`ui/UE5DumpUI/ViewModels/MainWindowViewModel.cs:2566`](../ui/UE5DumpUI/ViewModels/MainWindowViewModel.cs:2566), [`ui/UE5DumpUI/ViewModels/MainWindowViewModel.cs:2571`](../ui/UE5DumpUI/ViewModels/MainWindowViewModel.cs:2571), [`ui/UE5DumpUI/ViewModels/MainWindowViewModel.cs:1919`](../ui/UE5DumpUI/ViewModels/MainWindowViewModel.cs:1919), [`ui/UE5DumpUI/ViewModels/MainWindowViewModel.cs:1951`](../ui/UE5DumpUI/ViewModels/MainWindowViewModel.cs:1951)

### M9 — Experimental gate-off teardown skips active Stealth Hold @0 (Solide)

> **✅ FIXED — commit `1f46994` (build 2185).** The gate-off teardown now releases an active stealth hold:
> `if (StealthState == StealthHoldingState) _ = ResetStealthCommand.ExecuteAsync(null);` alongside the
> Foreground/Fly/SeeThrough force-offs. The `"Holding @0"` literal is factored into a shared const so the
> hold-set (`HoldStealthAsync`) and the teardown check can't drift. Tests
> `ExperimentalGateOff_releases_active_stealth_hold` + `..._no_stealth_hold_does_not_reset`.

**🟠 MEDIUM** · Effort **S** · Risk **low** · *confirmed* · Module: TeleportViewModel (Solide Stealth card)

- **Defect:** OnExperimentalGateChanged's disable branch force-disables Keep-Foreground, Fly, and SeeThrough but never releases an active Solide Stealth Hold @0, while its only control surface (the gated Stealth card) is hidden by IsVisible={Binding ExperimentalEnabled}.
- **Failure scenario:** User runs Teleport → Detect → Hold @0 (DLL re-assert worker forces the field to 0 across live instances), then unchecks the experimental gate. The else branch at 2909-2921 calls ForceForegroundLockOff/ResetFly/ResetSeeThrough but not ResetStealth; the card at TeleportPanel.axaml:586 collapses, so the DLL keeps writing 0 to the stealth field with no visible way to release it (Property Search 'Forced fields' strip never learns of the Teleport-created hold — M10/F6).
- **Fix:** In OnExperimentalGateChanged's IsConnected teardown block, add a stealth release: if the hold is active (StealthState == "Holding @0") invoke ResetStealthCommand.ExecuteAsync(null), matching the other three features' 'disable force-off first' pattern. A ResetStealthAsync command already exists at line 1519.
- **Note:** There is no _stealthActive bool; active-hold state is only reflected by StealthState == "Holding @0", so the guard must key off that string (or a new bool). Confirmed no other code path resets the stealth hold — a repo-wide search finds ResetStealthCommand referenced only at its definition.
- **Where:** [`ui/UE5DumpUI/ViewModels/TeleportViewModel.cs:2909-2921`](../ui/UE5DumpUI/ViewModels/TeleportViewModel.cs:2909), [`ui/UE5DumpUI/ViewModels/TeleportViewModel.cs:2914-2918`](../ui/UE5DumpUI/ViewModels/TeleportViewModel.cs:2914), [`ui/UE5DumpUI/ViewModels/TeleportViewModel.cs:1519-1539`](../ui/UE5DumpUI/ViewModels/TeleportViewModel.cs:1519), [`ui/UE5DumpUI/Views/TeleportPanel.axaml:586`](../ui/UE5DumpUI/Views/TeleportPanel.axaml:586)

### M10 — PropertySearch ResultFilter uses single whole-string Contains, no space=AND, no keyword memory

> **✅ FIXED — commit `8108ff2` (build 2186).** `ApplyResultFilter` now splits on `SplitTerms` and gates each
> row on `ObjectTreeFilter.MatchesAllTerms(terms, Class, Prop, Type, Super, Preview)` (term-AND / field-OR);
> `KeywordSearchMemory` wired (field + `ResultFilterHistory` + ctor probe + `Schedule` in the handler +
> `Dispose`); axaml `TextBox`→`AutoCompleteBox` (Text TwoWay, ItemsSource, FilterMode=Contains,
> PlaceholderText). Tests `PropertySearchFilterTests` (the old whole-string `Contains` found 0 for
> "max health"). Made `StubDumpService.SearchPropertiesAsync` virtual for the test.

**🟠 MEDIUM** · Effort **M** · Risk **low** · *confirmed* · Module: PropertySearchViewModel (client-side ResultFilter box)

- **Defect:** The client-side ResultFilter box matches the whole trimmed filter string via one case-insensitive Contains per field (MatchesFilter, 517-529) instead of ObjectTreeFilter.SplitTerms + MatchesAllTerms (space=AND), and OnResultFilterChanged (484-491) wires no KeywordSearchMemory; the control is a plain TextBox (axaml:54).
- **Failure scenario:** User types two space-separated terms (e.g. 'health max') into the Property Search result filter expecting term-level AND; MatchesFilter treats 'health max' as one literal substring, so rows where 'health' and 'max' live in different columns (or non-adjacent) never match, and typed keywords are never remembered/offered as autocomplete — violating the b2088 keyword-search MUST-rule this box was missed by.
- **Fix:** Rewrite MatchesFilter to split ResultFilter via ObjectTreeFilter.SplitTerms and call ObjectTreeFilter.MatchesAllTerms(terms, ClassName, PropName, PropType, SuperName, Preview). Wire KeywordSearchMemory per the MUST-rule (field + ResultFilterHistory property + ctor probe () => (ResultFilter, Results.Count > 0) + _mem.Schedule(value) in OnResultFilterChanged), and change axaml:54 from TextBox to AutoCompleteBox with PlaceholderText + Text TwoWay bound to ResultFilterHistory.
- **Note:** The AND-only fix alone is S; full MUST-rule compliance (KeywordSearchMemory + AutoCompleteBox wiring) makes it M. SearchQuery (axaml:12, server-side) and TypeFilter (axaml:18, comma-list AutoCompleteBox) are legitimately exempt as the finding states — ResultFilter is the only missed client-side box.
- **Where:** [`ui/UE5DumpUI/ViewModels/PropertySearchViewModel.cs:498-515`](../ui/UE5DumpUI/ViewModels/PropertySearchViewModel.cs:498), [`ui/UE5DumpUI/ViewModels/PropertySearchViewModel.cs:517-529`](../ui/UE5DumpUI/ViewModels/PropertySearchViewModel.cs:517), [`ui/UE5DumpUI/ViewModels/PropertySearchViewModel.cs:484-491`](../ui/UE5DumpUI/ViewModels/PropertySearchViewModel.cs:484), [`ui/UE5DumpUI/Views/PropertySearchPanel.axaml:52-56`](../ui/UE5DumpUI/Views/PropertySearchPanel.axaml:52)

## 🟡 LOW

### L1 — Split Solitar GodMode worker start/stop into *Locked cores under s_workerMutex

**🟡 LOW** · Effort **S** · Risk **low** · *confirmed* · Module: Solitar (GodMode)

- **Defect:** SetGodMode mutates s_wantGod under s_mutex, releases it, then decides StartWorker()/StopWorker() on the `on` call parameter outside any worker mutex, so the mutate and the worker start/stop decision are not jointly serialized against a concurrent call.
- **Failure scenario:** Concurrent SetGodMode(false) (Mimic CMD_PROTECT) then SetGodMode(true) (pipe): s_wantGod stores serialize to final=true, but the two threads then race their unsynchronized tail; if the true-caller's StartWorker() runs before the false-caller's StopWorker(), the worker is joined away while s_wantGod=true (badge/GetState reports want=1) so GodMode silently stops re-asserting across respawns; the mirror interleaving leaves an orphan worker idling with want=false.
- **Fix:** Mirror Laufen/Hemmung (their explicit 'audit #8' fix): add StartWorkerLocked()/StopWorkerLocked() cores that assume the caller already holds s_workerMutex; in SetGodMode take s_workerMutex (outer) across the whole mutate+decision, take s_mutex (inner) only for s_wantGod.store + ApplyGodNowLocked, then decide start vs stop on the final s_wantGod.load() (not the `on` parameter). Keep the public StopWorker() wrapper (DLL-unload path) taking s_workerMutex then calling StopWorkerLocked. Lock order s_workerMutex -> s_mutex; join runs with s_mutex released.
- **Note:** Claim accurate. Impact is benign in practice (worst case is GodMode silently not re-asserting, or a harmless idle worker that no-ops each tick because WorkerLoop checks s_wantGod) — no crash, no deadlock; hence LOW. The two siblings already implement the exact target pattern, so the fix is a near-mechanical port.
- **Where:** [`dll/src/Solitar.cpp:380-394`](../dll/src/Solitar.cpp:380), [`dll/src/Solitar.cpp:342-347`](../dll/src/Solitar.cpp:342), [`dll/src/Solitar.cpp:457-462`](../dll/src/Solitar.cpp:457), [`dll/src/Laufen.cpp:452-512`](../dll/src/Laufen.cpp:452), [`dll/src/Hemmung.cpp:370-423`](../dll/src/Hemmung.cpp:370)

### L2 — FR_ERR_WEAK_PTR is dead code; weak-ptr / type refusal is silent and leaves a futile job running

**🟡 LOW** · Effort **M** · Risk **low** · *confirmed* · Module: Solide

- **Defect:** Solide::FR_ERR_WEAK_PTR (and FR_ERR_REFLECT/FR_ERR_WRITE) are never returned by AddForce — AddForce always returns the non-negative held count. The GObjects[0] trap is avoided (Solide.cpp:223 returns false when TypeName != "ObjectProperty"), but that refusal only makes ApplyToInstance return false, so held stays 0 and the wire reports held=0/resolved=false/code=0 — indistinguishable from 'no live instances'. The job is still push_back'd into s_jobs (Solide.cpp:337) before ApplyJobLocked, and the worker is started (Solide.cpp:349), so a full-pool FindInstancesByClass scan runs every 300ms forever with zero effect.
- **Failure scenario:** User asks Force→null on a field that is a WeakObjectProperty (or any non-strong-ObjectProperty). AddForce adds the job, ApplyToInstance refuses every instance (line 223), returns held=0. Fern maps held<0?code:0 → code=0, resolved=false: the UI shows 'nothing matched' with no hint the type was refused, while the re-assert worker keeps scanning the whole GObjects pool indefinitely.
- **Fix:** In ApplyToInstance/AddForce distinguish 'field resolved but type refused' from 'class/field not found on any instance': have ApplyJobLocked surface a refusal signal, and have AddForce return FR_ERR_WEAK_PTR (object-null on non-strong ptr) or FR_ERR_REFLECT (wrong type) instead of 0 when the field resolved on >=1 instance but was refused everywhere. Also do not persist the job / start the worker in that case (early-return before push_back, or erase the just-added job when held==0 and a refusal was seen). Fern already forwards held<0 → code.
- **Note:** Only FR_ERR_NO_TARGET is ever returned by Solide, and only from FindStealthMeter (Solide.cpp:406,409), not AddForce. The FR_ERR_REFLECT hits in the grep are Dunste's separate enum. Changing AddForce's return semantics touches the held-count consumer in Fern (4602-4607) so verify the held vs error mapping stays correct.
- **Where:** [`dll/src/Solide.h:40`](../dll/src/Solide.h:40), [`dll/src/Solide.cpp:220-233`](../dll/src/Solide.cpp:220), [`dll/src/Solide.cpp:236`](../dll/src/Solide.cpp:236), [`dll/src/Solide.cpp:321-351`](../dll/src/Solide.cpp:321), [`dll/src/Fern.cpp:4602-4607`](../dll/src/Fern.cpp:4602)

### L3 — Substring class match + fuzzy contains field fallback can force unintended fields on unintended classes

**🟡 LOW** · Effort **S** · Risk **med** · *partially-confirmed* · Module: Solide

- **Defect:** ApplyJobLocked calls Aura::FindInstancesByClass(job.className, exactMatch=false, ...) (Solide.cpp:253) so the class name is substring/leaf-matched, and the numeric field lookup calls Ubel::FindField(cls, fieldName, fieldName, nullptr, nullptr) (Solide.cpp:215) which after an exact-case-insensitive miss takes the FIRST field whose name CONTAINS fieldName (Ubel.cpp:987-994). The bool path (Solitar::Get/SetActorBool → ResolveActorBoolBit at Solitar.cpp:434/450/204) is fuzzy on name too but restricted to typeFilter="BoolProperty".
- **Failure scenario:** Force class 'Enemy' field 'Health'=0 also matches EnemyProjectile/EnemySpawner instances (substring class). A class lacking an exact 'Health' but having numeric 'HealthRegenRate' gets 0 written into HealthRegenRate (fuzzy contains), inflating the 'N held' badge with wrong writes.
- **Fix:** Force exact matching: pass exactMatch=true to FindInstancesByClass (or gate on exact leaf class name), and change the numeric/bool FindField calls in ApplyToInstance to exact-only (contains=nullptr) so only an exactly-named field is forced. The forced field name originates from Property Search / the stealth finder which supply exact leaf names, so exact-only should not lose legitimate matches.
- **Note (verification):** Nuance vs the original claim: the numeric path passes NO typeFilter to FindField (accurate), but Solide.cpp:236 (IsNumericType) still gates the fuzzy match to numeric types downstream — so a non-numeric fuzzy hit is rejected; only a numeric same-prefix field like HealthRegenRate is at risk. Class-substring is documented in headers/CLAUDE.md; the fuzzy field fallback is not.
- **Where:** [`dll/src/Solide.cpp:253`](../dll/src/Solide.cpp:253), [`dll/src/Solide.cpp:214-218`](../dll/src/Solide.cpp:214), [`dll/src/Solide.cpp:236`](../dll/src/Solide.cpp:236), [`dll/src/Ubel.cpp:981-996`](../dll/src/Ubel.cpp:981), [`dll/src/Solitar.cpp:199-206`](../dll/src/Solitar.cpp:199)

### L4 — Representative single base is written to every instance on Reset (stale/foreign undo)

**🟡 LOW** · Effort **M** · Risk **med** · *confirmed* · Module: Solide

- **Defect:** The restore base is captured exactly once from the first instance ever resolved (Solide.cpp:200 bool, 239 numeric: if(!job.hasBase){job.baseValue=cur;job.hasBase=true;}) and never re-captured. RemoveForce (362) and ClearAll (376) call ApplyJobLocked(restore=true) which, via ApplyToInstance's restore branch (201 bool / 240 numeric), writes that single captured value to ALL currently-live instances. Unlike the single-owner siblings Hemmung (252-255) and Laufen (292-295) which re-capture the base on owner change, Solide's pool model has no per-instance base and no re-capture on turnover.
- **Failure scenario:** A numeric field is forced across a pool where instances legitimately hold different base values (e.g. instance A base=100 resolved first, instance B base=50). On Reset, both A and B get 100 written — B is clobbered with a wrong value. After a level change, entirely new instances receive the stale/foreign captured base rather than being left untouched.
- **Fix:** Track base per-instance (a bounded/expiring map keyed by owner addr, captured at first write) and on restore only rewrite instances present in that map to their own base, leaving never-captured instances untouched (missing rather than clobbered). A fully correct undo over a churning pool is inherently hard; the pragmatic goal is 'never write a foreign base'.
- **Note:** K_OBJECT_NULL is exempt — its restore is a no-op (Solide.cpp:225, original ptr not saved). The header (Solide.h:118-121) and CLAUDE.md already label this 'best-effort'/'representative', so it is a documented limitation; the gap is that the Teleport Stealth / Property Search UI presents Reset as a clean undo. Bool bases are 0/1 so lower-impact; numeric is the real concern.
- **Where:** [`dll/src/Solide.cpp:200`](../dll/src/Solide.cpp:200), [`dll/src/Solide.cpp:239-240`](../dll/src/Solide.cpp:239), [`dll/src/Solide.cpp:250-269`](../dll/src/Solide.cpp:250), [`dll/src/Solide.cpp:362`](../dll/src/Solide.cpp:362), [`dll/src/Solide.cpp:376`](../dll/src/Solide.cpp:376), [`dll/src/Hemmung.cpp:252-255`](../dll/src/Hemmung.cpp:252), [`dll/src/Laufen.cpp:292-295`](../dll/src/Laufen.cpp:292)

### L5 — Out-of-order PE timestamps underflow the Welford inter-arrival gap in RecordCall

**🟡 LOW** · Effort **S** · Risk **low** · *confirmed* · Module: Linie (LivePEProfiler)

- **Defect:** nowMs is stamped in HookedProcessEvent before RecordCall acquires g_mu, so two threads dispatching PE for the same UFunction can update s.lastMs out of timestamp order, making the uint64 subtraction nowMs - s.lastMs underflow to ~1.8e19 and poison that function's Welford mean/m2 for the rest of the window.
- **Failure scenario:** Thread A reads nowMs=100 at Stark.cpp:143, thread B reads nowMs=101; B wins the g_mu race and sets s.lastMs=101; A then acquires g_mu and computes gap = 100 - 101 = 18446744073709551615 ms -> mean_period_ms/cv for that UFunction become garbage. Linie explicitly claims multi-thread-PE safety (dedicated mutex, 'safe from any thread'), so this is a real gap in that guarantee.
- **Fix:** In RecordCall (Linie.cpp ~45-52) guard the Welford branch on nowMs > s.lastMs: only measure a gap when the new timestamp is strictly greater (skip/clamp the sample otherwise); optionally set s.lastMs = max(s.lastMs, nowMs) so a stale reorder can't lower the base.
- **Note:** Confirmed as described; citations accurate (minimal drift). Window is small (NowMs read at 143 to lock at 40) but non-zero and unbounded in effect once hit.
- **Where:** [`dll/src/Linie.cpp:47`](../dll/src/Linie.cpp:47), [`dll/src/Linie.cpp:39-55`](../dll/src/Linie.cpp:39), [`dll/src/Stark.cpp:143`](../dll/src/Stark.cpp:143), [`dll/src/Stark.cpp:152-153`](../dll/src/Stark.cpp:152)

> **⛔ DROPPED by double-confirm — NaN unreachable (Welford m2 provably ≥ 0).**

### L6 — Linie cv can be sqrt(negative)->NaN (variance not clamped); claimed response-failure impact is wrong

**🟡 LOW** · Effort **S** · Risk **low** · *partially-confirmed* · Module: Linie (LivePEProfiler) / DumpService

- **Defect:** Snapshot computes variance = m2/gaps without clamping to >=0 before std::sqrt, so a floating-point-negative m2 would yield a NaN cv that nlohmann serializes as JSON null.
- **Failure scenario:** For near-identical integer-ms gaps a catastrophic-cancellation rounding could drive Welford m2 slightly negative -> sqrt(negative) = NaN cv -> emitted as null in pe_profile_get.
- **Fix:** Clamp before the root in Linie.cpp:91: double variance = std::max(0.0, s.m2 / (double)s.gaps); (defensive hygiene).
- **Note (verification):** The downstream half of the claim is stale/incorrect: the UI does NOT source-gen-deserialize this DTO. DumpService.cs:2447-2448 parses via tolerant JsonNode -- obj["cv"]?.GetValue<double>() ?? 0.0 -- so a null cv coalesces to 0.0 with no exception and no response failure. Also Welford's incremental m2 (M2 += (x-oldMean)*(x-newMean), each term = (x-oldMean)^2*(n-1)/n >= 0) is numerically stable and stays non-negative for essentially all real inputs, so NaN is very unlikely. Net real harm ~= nil (a 0.0 cv even classifies the very-regular case as periodic, which is correct). Worth the cheap clamp for correctness only.
- **Where:** [`dll/src/Linie.cpp:90-93`](../dll/src/Linie.cpp:90), [`dll/src/Fern.cpp:3148-3149`](../dll/src/Fern.cpp:3148), [`ui/UE5DumpUI/Services/DumpService.cs:2447-2448`](../ui/UE5DumpUI/Services/DumpService.cs:2447), [`ui/UE5DumpUI/Models/PeProfileResult.cs:64-67`](../ui/UE5DumpUI/Models/PeProfileResult.cs:64)

> **⬇ DOWNGRADED — optional cleanup (phantom row wiped by next StartRecording).**

### L7 — RecordCall doesn't re-check the recording gate under g_mu; Reset can be repopulated with phantom rows

**🟡 LOW** · Effort **S** · Risk **low** · *confirmed* · Module: Linie (LivePEProfiler)

- **Defect:** RecordCall checks IsRecording() only via Stark's unlocked relaxed gate (Stark.cpp:152) and never re-checks g_recording after acquiring g_mu, so a fire that passed the gate can take g_mu after Reset()/StopRecording clears the map and insert into the just-cleared table.
- **Failure scenario:** PE hook thread passes IsRecording()==true at Stark.cpp:152; before it takes g_mu, the pipe thread runs Linie::Reset() on client disconnect (Fern.cpp:760) -> g_recording=false, g_stats.clear(), g_seq=0. The hook thread then acquires g_mu, default-constructs a Stat, sets firstSeq and ++g_seq -> 1-2 phantom rows survive with g_seq restarted; a pe_profile_get issued before a fresh Start returns them.
- **Fix:** At the top of RecordCall, after taking g_mu, add if (!g_recording.load(std::memory_order_relaxed)) return; (mirrors StartRecording's under-lock flip so the under-lock view is authoritative).
- **Note:** Confirmed and benign exactly as the finding states -- the next StartRecording clears g_stats under the lock. Citations accurate.
- **Where:** [`dll/src/Linie.cpp:39-55`](../dll/src/Linie.cpp:39), [`dll/src/Linie.cpp:75-80`](../dll/src/Linie.cpp:75), [`dll/src/Fern.cpp:760`](../dll/src/Fern.cpp:760), [`dll/src/Fern.cpp:500`](../dll/src/Fern.cpp:500)

### L8 — Grausam GetWindowTextW under g_mutex can hang the pipe/mailbox thread

**🟡 LOW** · Effort **S** · Risk **low** · *confirmed* · Module: Grausam (ForegroundLock)

- **Defect:** SubclassEnumProc calls the synchronous, timeout-less GetWindowTextW on a same-process window while SetForegroundLock holds g_mutex on a background (pipe/mailbox) thread, so a non-pumping game UI thread blocks the caller indefinitely.
- **Failure scenario:** Foreground lock is enabled via pipe set_foreground_lock (Fern.cpp:4371) or Mimic CMD_FOREGROUND (Mimic.cpp:935) while the game's message-pumping thread is stalled/paused (the documented game-thread-stall state this feature targets). GetWindowTextW(hwnd,...) at line 161 issues WM_GETTEXT via SendMessage to a window owned by that stalled thread; the pipe/mailbox thread parks forever holding g_mutex, wedging that IPC lane.
- **Fix:** Delete the GetWindowTextW call (lines 160-161) — the `title` buffer it fills is never referenced in the Sein::Info log at 163-166, so it is pure dead-code liability. If a title is ever wanted, use SendMessageTimeoutW(hwnd, WM_GETTEXT, ... , SMTO_ABORTIFHUNG, smallMs, ...).
- **Note:** Confirmed and slightly understated by the original claim: the retrieved title is entirely unused, so removing the call is behaviorally free (not merely 'logging-only'). Hang requires a non-pumping game UI thread — plausible but not the common backgrounded case, hence LOW.
- **Where:** [`dll/src/Grausam.cpp:160-161`](../dll/src/Grausam.cpp:160), [`dll/src/Grausam.cpp:146-168`](../dll/src/Grausam.cpp:146), [`dll/src/Grausam.cpp:177`](../dll/src/Grausam.cpp:177), [`dll/src/Grausam.cpp:240`](../dll/src/Grausam.cpp:240), [`dll/src/Fern.cpp:4371`](../dll/src/Fern.cpp:4371), [`dll/src/Mimic.cpp:935`](../dll/src/Mimic.cpp:935)

> **⬇ DOWNGRADED — optional (deliberate clip-release tradeoff; niche).**

### L9 — Grausam HookedClipCursor issues a per-frame global ClipCursor(nullptr) instead of swallowing

**🟡 LOW** · Effort **S** · Risk **low** · *confirmed* · Module: Grausam (ForegroundLock)

- **Defect:** While locked and backgrounded, every game ClipCursor(rect) is converted to g_origClipCursor(nullptr), which clears the desktop-global cursor clip each frame rather than merely swallowing the game's confinement attempt.
- **Failure scenario:** Foreground lock on, game backgrounded; the user switches to a third-party app (another game / remote-desktop client) that legitimately calls ClipCursor to confine the cursor. The still-ticking game re-clips each frame; HookedClipCursor line 113 responds with ClipCursor(nullptr), repeatedly wiping the other app's legitimate confinement.
- **Fix:** Change line 113 from `return g_origClipCursor(nullptr);` to `return TRUE;` (mirror HookedSetCursorPos) so the game's re-clip is swallowed without touching global clip state; the OS's kernel-side clip release on genuine foreground loss plus the one-shot release in the disable path (253-254) already cover the transition.
- **Note:** Confirmed. Low residual risk: the fix relies on the kernel releasing the clip on real deactivation independently of the WM_ACTIVATE messages we swallow via the subclass (kernel-side, so it holds) — worth a quick in-game confirm that the cursor isn't left clipped on first background.
- **Where:** [`dll/src/Grausam.cpp:107-115`](../dll/src/Grausam.cpp:107), [`dll/src/Grausam.cpp:112-113`](../dll/src/Grausam.cpp:112), [`dll/src/Grausam.cpp:253-254`](../dll/src/Grausam.cpp:253)

### L10 — Grausam misses post-enable windows and has no shutdown teardown (lock latched on with no off-switch)

**🟡 LOW** · Effort **M** · Risk **med** · *confirmed* · Module: Grausam (ForegroundLock) / Frieren (ExportAPI)

- **Defect:** (A) SubclassAllGameWindows runs only inside the enable path (line 240); windows created after enable are never subclassed, and there is no re-subclass timer. (B) UE5_Shutdown (Frieren.cpp:489-504) performs no Grausam teardown, so g_enabled stays true and the WndProc subclass (never removed anywhere) keeps rewriting activation messages; because Stark::Shutdown deliberately skips MH_Uninitialize (Stark.cpp:320-328), the GetForegroundWindow MinHook also stays physically active.
- **Failure scenario:** (A) User enables the lock, then the game toggles fullscreen/borderless and recreates its main window; the new HWND is unsubclassed, so WM_ACTIVATEAPP-driven pauses resume until re-enable (GFW hook still masks the polling path — partial degradation). (B) Lock is on when UE5_Shutdown runs (or a CE-Lua/trainer disable path that routes through shutdown); pipe (s_pipeServer.Stop) and Mimic thread are torn down while g_enabled==true and both the subclass and GFW hook remain live, so the game believes it is foreground forever with no remaining channel to clear the lock.
- **Fix:** (A) Re-run SubclassAllGameWindows periodically while enabled (piggyback an existing worker/poll tick, e.g. Mimic loop) or lazily subclass in the GFW/activation path when g_gameWindow changes. (B) Add a Grausam teardown that sets g_enabled=false, restores each subclassed window's WNDPROC via the saved kOrigProcProp (removing the prop), and disables the ClipCursor/SetCursorPos/GFW hooks, and call it from UE5_Shutdown BEFORE s_pipeServer.Stop(); guard the un-subclass against the same in-flight race that motivated leaving it installed.
- **Note:** Confirmed, and stronger than stated: the lock is doubly latched (subclass + still-active GFW MinHook, since Stark.cpp:320 skips MH_Uninitialize per audit fix #14), and if the DLL is later FreeLibrary'd the retained subclass/hook point at freed code (crash on next window message / GetForegroundWindow). The un-subclass restore is the risky part of the fix — must handle windows destroyed since enable and the WNDPROC-restore ordering race.
- **Where:** [`dll/src/Grausam.cpp:170-172`](../dll/src/Grausam.cpp:170), [`dll/src/Grausam.cpp:240`](../dll/src/Grausam.cpp:240), [`dll/src/Grausam.cpp:246-256`](../dll/src/Grausam.cpp:246), [`dll/src/Frieren.cpp:489-504`](../dll/src/Frieren.cpp:489), [`dll/src/Stark.cpp:300-328`](../dll/src/Stark.cpp:300)

> **⬇ DOWNGRADED — optional (SEH-guarded, self-corrects next tick; no crash).**

### L11 — Schlacht holds raw AActor* in hiddenActors across GC; best-effort guard passes on remapped memory

**🟡 LOW** · Effort **M** · Risk **low** · *confirmed* · Module: Schlacht (SeeThrough)

- **Defect:** s_state.hiddenActors (345) stores raw uintptr_t actor pointers that the diff loop (396-397) later re-passes to InvokeSetHidden; the liveness guard there (266 Ubel::GetClass + 268 Aura::ClassDerivesFromAny 'Actor') is structural only, so a freed slot remapped to another Actor-flavoured object passes and SetActorHiddenInGame runs on the recycled object.
- **Failure scenario:** An occluder wall actor gets GC'd/streamed out between ticks (or lingers across the M1/M2/M3 leak windows) and its memory is reused by a new Actor. On the next diff pass Ubel::GetClass returns a valid class that derives from Actor, the guard passes, and the wrong (recycled) actor is hidden or un-hidden.
- **Fix:** Residual risk is LOW: GetClass/ClassDerivesFromAny read through Macht SEH so a dangling pointer yields no crash (garbage class → guard fails → return false); the realistic residual is a wrong-but-live Actor being toggled visible, and occluders are typically static geometry that rarely GCs within one ~100ms tick. If hardened, store the ObjectIndex alongside the pointer and re-resolve via Aura::GetByIndex each tick (compare identity) or check GObjects membership before InvokeSetHidden. Low priority; fixing M1/M3 shrinks the exposure window most.
- **Note:** Confirmed as a genuine but best-effort-guarded residual. Not memory-unsafe (SEH-guarded reads); worst case is toggling visibility on a recycled valid Actor. Window is normally one tick but M1/M2/M3 can extend it to session-length.
- **Where:** [`dll/src/Schlacht.cpp:345`](../dll/src/Schlacht.cpp:345), [`dll/src/Schlacht.cpp:396-397`](../dll/src/Schlacht.cpp:396), [`dll/src/Schlacht.cpp:264-268`](../dll/src/Schlacht.cpp:264)

### L12 — Fern invoke_function str_params malloc buffers leak on a mid-loop JSON type_error

**🟡 LOW** · Effort **S** · Risk **low** · *confirmed* · Module: Fern (PipeServer)

- **Defect:** strAllocs is a std::vector<void*> of raw malloc pointers whose ownership is only released by the explicit free loop at 4282-4285; a json::type_error thrown by sp.value(...) mid-loop unwinds past that loop (the vector destructor frees only its pointer array, not the pointees) and is swallowed by the dispatch catch at 5075-5083, leaking every buffer from earlier successful iterations.
- **Failure scenario:** str_params = [ {"off":0,"text":"hi"}, "garbage" ]: iteration 1 mallocs a UTF-16 Data buffer and push_backs it; iteration 2 calls sp.value("off",-1) on a non-object element -> nlohmann type_error.306 (a wrongly-typed "off"/"wide"/"text" gives type_error.302). Exception propagates past the free loop, caught at 5075 which returns an error response; iteration 1's heap buffer is leaked in the game process.
- **Fix:** Give the buffers RAII ownership instead of raw void*: store each as a std::unique_ptr<void,decltype(&std::free)> (or std::vector<std::vector<uint8_t>> and pass .data()) so unwinding frees them; on the deliberate callResult==-5 path, release()/leak them intentionally as today. Alternatively wrap the str_params parse loop in try/catch that frees strAllocs before rethrowing. Confirm the -5 special-case is preserved either way.
- **Note:** Claim accurate including line regions and the note that the -5 (timeout) deliberate leak at 4282 is correct and must be preserved. Leak requires malformed str_params (the trusted UI does not normally emit non-object/mis-typed descriptors) and only leaks small per-string buffers on an error path, so LOW is right. Push_back is after the sp.value reads, so a throw leaks only prior-iteration buffers, never the current one — matches the finding's wording.
- **Where:** [`dll/src/Fern.cpp:4205`](../dll/src/Fern.cpp:4205), [`dll/src/Fern.cpp:4206-4250`](../dll/src/Fern.cpp:4206), [`dll/src/Fern.cpp:4208-4210`](../dll/src/Fern.cpp:4208), [`dll/src/Fern.cpp:4282-4285`](../dll/src/Fern.cpp:4282), [`dll/src/Fern.cpp:5075-5083`](../dll/src/Fern.cpp:5075)

### L13 — Stealth card state (candidate/badge/field) survives disconnect; stale hold on reconnect

**🟡 LOW** · Effort **S** · Risk **low** · *confirmed* · Module: TeleportViewModel (Solide Stealth card)

- **Defect:** SetConnected(false) resets every other card badge (DebugCamera/GodMode/ForegroundLock/MoveSpeed/TimeDilation/Gravity/SuperJump/Fly/SeeThrough/GravDir/MouseCursor + POV) but leaves _stealthCandidate, StealthState, and StealthFieldText untouched.
- **Failure scenario:** User detects/holds a stealth meter in game A, disconnects, then reconnects to a different game B. The card still shows 'Ready'/'Holding @0' with game A's class::field; pressing Hold @0 calls ForceFieldAsync(A.ClassName, A.FieldName, ...) against game B, where FindInstancesByClass leaf-name matching could bind an unrelated field in the new process.
- **Fix:** In SetConnected's disconnect (else) branch, reset the stealth card alongside the others: _stealthCandidate = null; StealthFieldText = "—"; (StealthState, StealthBadgeColor) = ("Off", "#999999").
- **Note:** Confirmed the disconnect branch (632-659) has no stealth reset. Low severity because it requires a disconnect-then-reconnect-to-different-game sequence plus a manual Hold press, but the stale-field write is real.
- **Where:** [`ui/UE5DumpUI/ViewModels/TeleportViewModel.cs:632-659`](../ui/UE5DumpUI/ViewModels/TeleportViewModel.cs:632), [`ui/UE5DumpUI/ViewModels/TeleportViewModel.cs:1445-1452`](../ui/UE5DumpUI/ViewModels/TeleportViewModel.cs:1445), [`ui/UE5DumpUI/ViewModels/TeleportViewModel.cs:1490-1517`](../ui/UE5DumpUI/ViewModels/TeleportViewModel.cs:1490)

### L14 — Batch-xref CTS replaced without Dispose + bare-OCE outer catch misreports disconnect as 'cancelled'

**🟡 LOW** · Effort **S** · Risk **low** · *confirmed* · Module: PropertySearchViewModel + InstanceFinderViewModel (BatchFindFuncs)

- **Defect:** BatchFindFuncsAsync cancels then replaces _xrefBatchCts with a new CancellationTokenSource without disposing the old one (only the final CTS is disposed in Dispose()), and the outer catch (OperationCanceledException) has no `when (ct.IsCancellationRequested)` filter, so a bare disconnect-OCE from the pipe is caught and reported as 'cancelled at N/M' rather than a failure.
- **Failure scenario:** User re-runs a batch Find Funcs several times (each run leaks the previous CTS), then disconnects mid-batch; PipeClient throws a bare OperationCanceledException (token=None) from FindPropertyXrefsAsync/FindFunctionsByClassAsync, the inner `catch (OperationCanceledException) { throw; }` rethrows it past the per-row error handling, and the outer catch reports a benign 'Find Funcs cancelled at N/M' instead of surfacing the connection loss.
- **Fix:** Before replacing _xrefBatchCts, dispose the old one (old?.Cancel(); old?.Dispose()); and add `when (ct.IsCancellationRequested)` to both outer catch blocks so a genuine user-cancel reports 'cancelled' while a bare disconnect-OCE falls through to a failure/error message. Apply identically in PropertySearchViewModel (615-617 / 645-648) and InstanceFinderViewModel (805-807 / 840-843).
- **Note:** The CTS 'leak' is minor: these CTSs only use .Token/.Cancel() (no CancelAfter/WaitHandle), so Dispose is nearly a no-op — the real defect is the bare-OCE misreport per feedback-pipeclient-bare-oce-cancel-guard. InstanceFinder line refs corrected from the finding's ~828/840: undispose is at 805-807, inner rethrow at 828, outer bare-OCE at 840; both Dispose() paths (PropertySearch 662-663, InstanceFinder 324-326) dispose only the final CTS.
- **Where:** [`ui/UE5DumpUI/ViewModels/PropertySearchViewModel.cs:615-617`](../ui/UE5DumpUI/ViewModels/PropertySearchViewModel.cs:615), [`ui/UE5DumpUI/ViewModels/PropertySearchViewModel.cs:633-648`](../ui/UE5DumpUI/ViewModels/PropertySearchViewModel.cs:633), [`ui/UE5DumpUI/ViewModels/InstanceFinderViewModel.cs:805-807`](../ui/UE5DumpUI/ViewModels/InstanceFinderViewModel.cs:805), [`ui/UE5DumpUI/ViewModels/InstanceFinderViewModel.cs:828-843`](../ui/UE5DumpUI/ViewModels/InstanceFinderViewModel.cs:828)

### L15 — EstimateSizeAsync mislabels a disconnect as 'Estimate cancelled.'

**🟡 LOW** · Effort **S** · Risk **low** · *confirmed* · Module: SnapshotViewModel (EstimateSizeAsync)

- **Defect:** The size-estimate catch (1207) is a bare `catch (OperationCanceledException) { EstimateText = "Estimate cancelled."; }` with no ct filter, so a pipe disconnect during a size estimate is shown to the user as a normal cancellation instead of an error.
- **Failure scenario:** User runs Estimate. Pipe disconnects while awaiting _dump.BeginSnapshotAsync (1175) or _dump.SnapshotChunkAsync (1185). PipeClient TrySetCanceled emits a bare OCE with ct.IsCancellationRequested false, but the unfiltered catch (1207) reports 'Estimate cancelled.' rather than 'Estimate failed.' Purely cosmetic — no data corruption or wedge.
- **Fix:** Add `when (ct.IsCancellationRequested)` to the catch at 1207 so a disconnect-OCE falls through to the general `catch (Exception)` (1208) which sets 'Estimate failed.'
- **Note:** Citation exact (1207). Cosmetic only; same bare-OCE root cause and same one-line filter fix as M7/H1.
- **Where:** [`ui/UE5DumpUI/ViewModels/SnapshotViewModel.cs:1207`](../ui/UE5DumpUI/ViewModels/SnapshotViewModel.cs:1207), [`ui/UE5DumpUI/ViewModels/SnapshotViewModel.cs:1175`](../ui/UE5DumpUI/ViewModels/SnapshotViewModel.cs:1175), [`ui/UE5DumpUI/ViewModels/SnapshotViewModel.cs:1185`](../ui/UE5DumpUI/ViewModels/SnapshotViewModel.cs:1185)

### L16 — LiveFuncs IsRecording not reset on disconnect; auto-stop clears it only post-round-trip

**🟡 LOW** · Effort **S** · Risk **low** · *confirmed* · Module: LiveFuncsViewModel

- **Defect:** IsRecording is never reset when the pipe disconnects, and AutoStopOnLeaveAsync (fire-and-forget from OnLeavingTab) flips IsRecording=false only AFTER the async PeProfileStopAsync round-trip, so StartAsync's `if (IsRecording) return;` guard can swallow a legitimate Start.
- **Failure scenario:** (a) Pipe dies while recording: nothing in ConnectionStateChanged (MainWindowViewModel.cs:1928 !connected branch) or DisconnectAsync's finally (2059) resets LiveFuncs.IsRecording, so the UI reads 'recording' across reconnect until a successful Stop. (b) User leaves the LiveFuncs tab (MainWindow.axaml.cs:640 → OnLeavingTab → _=AutoStopOnLeaveAsync), navigates back and clicks Start within the stop round-trip window; StartAsync returns at :142 because IsRecording is still true, then AutoStop completes and overwrites status with 'auto-stopped', silently discarding the click. Also StopAsync sets IsRecording=false only after the await (:174→:175), so a throwing PeProfileStopAsync leaves IsRecording stuck true.
- **Fix:** In AutoStopOnLeaveAsync set IsRecording=false optimistically before the await (UI state); in the ConnectionStateChanged !connected branch (or DisconnectAsync finally) reset LiveFuncs.IsRecording via a new LiveFuncs.ResetOnDisconnect(); and move IsRecording=false into a finally in StopAsync/AutoStopOnLeaveAsync so a throwing stop still clears UI state.
- **Note:** UI-state only; DLL side is safe (Linie resets its table on disconnect). All three sub-claims verified. Severity LOW is appropriate.
- **Where:** [`ui/UE5DumpUI/ViewModels/LiveFuncsViewModel.cs:142`](../ui/UE5DumpUI/ViewModels/LiveFuncsViewModel.cs:142), [`ui/UE5DumpUI/ViewModels/LiveFuncsViewModel.cs:346`](../ui/UE5DumpUI/ViewModels/LiveFuncsViewModel.cs:346), [`ui/UE5DumpUI/ViewModels/LiveFuncsViewModel.cs:350`](../ui/UE5DumpUI/ViewModels/LiveFuncsViewModel.cs:350), [`ui/UE5DumpUI/ViewModels/MainWindowViewModel.cs:1919`](../ui/UE5DumpUI/ViewModels/MainWindowViewModel.cs:1919), [`ui/UE5DumpUI/Views/MainWindow.axaml.cs:640`](../ui/UE5DumpUI/Views/MainWindow.axaml.cs:640)

### L17 — ObjectTree FilterText="" on Load/Search without Flush drops a just-typed keyword

**🟡 LOW** · Effort **S** · Risk **low** · *confirmed* · Module: ObjectTreeViewModel

- **Defect:** LoadAsync (:334) and SearchAsync (:418) set FilterText="" on navigation-to-new-data without calling _filterMemory.Flush() first; the resulting OnFilterTextChanged fires _filterMemory.Schedule("") which (KeywordSearchMemory.cs:74-76) disposes/cancels the pending debounce and schedules nothing, dropping a keyword typed <700ms earlier.
- **Failure scenario:** User types a keyword in the bottom Object-Tree filter box (matches appear), then within the 700ms quiet window clicks Load or the top Search; FilterText is reset to "", Schedule("") cancels the pending CommitSettled, and the keyword never lands in History even though it produced matches. The MUST-rule requires Flush() before clearing the box on tab-switch/navigation.
- **Fix:** Insert `_filterMemory.Flush();` before `FilterText = "";` in both LoadAsync (~:334) and SearchAsync (~:418). Placing it before `_allNodes.Clear()` (:333) ensures the probe (FilterText, FilteredNodes.Count>0) still sees the outgoing keyword's matches.
- **Note:** Confirmed: the debounce probe is client-side/synchronous so Schedule (not Commit) is correct on keystroke, but the navigation reset genuinely needs Flush. Same class as the other panels that already Flush on tab-leave.
- **Where:** [`ui/UE5DumpUI/ViewModels/ObjectTreeViewModel.cs:334`](../ui/UE5DumpUI/ViewModels/ObjectTreeViewModel.cs:334), [`ui/UE5DumpUI/ViewModels/ObjectTreeViewModel.cs:418`](../ui/UE5DumpUI/ViewModels/ObjectTreeViewModel.cs:418), [`ui/UE5DumpUI/ViewModels/ObjectTreeViewModel.cs:184`](../ui/UE5DumpUI/ViewModels/ObjectTreeViewModel.cs:184), [`ui/UE5DumpUI/Helpers/KeywordSearchMemory.cs:71`](../ui/UE5DumpUI/Helpers/KeywordSearchMemory.cs:71)

> **⬇ DOWNGRADED — missing detach has zero functional effect; only the no-ct usability point stands.**

### L18 — DetectStats Results.Clear() without SelectedResult detach; DetectAsync has no cancellation

**🟡 LOW** · Effort **M** · Risk **low** · *confirmed* · Module: DetectStatsViewModel

- **Defect:** Both Results.Clear() sites — DetectAsync (:100) and ApplyFilter (:321) — clear the selection-bound grid without first setting SelectedResult=null, deviating from the detach-before-clear convention. Separately, DetectAsync (:95-242) accepts/creates no CancellationToken, so its bounded-but-large fan-out of pipe round-trips keeps running after the user leaves the tab.
- **Failure scenario:** Detect fans out up to MaxClassesProbed=30 iterations, each doing FindInstancesAsync (:169) + WalkInstanceAsync (:176) — ~60 pipe round-trips with no cancellation; switching tabs mid-Detect cannot abort them. Independently, clearing Results while a row is selected without detaching first churns the selection model (same class as audit-#2 finding #6).
- **Fix:** Set SelectedResult=null immediately before each Results.Clear() (both :100 and :321). Add a CancellationTokenSource field (cancel+recreate at the top of DetectAsync, cancel on OnLeavingTab/Dispose) and thread the token into FindInstancesAsync/WalkInstanceAsync and the byClass loop with ct.ThrowIfCancellationRequested().
- **Note:** Both sub-claims confirmed. The fan-out IS bounded (MaxCandidates=80 / MaxClassesProbed=30) so it is a responsiveness/feature gap, not an unbounded leak; the detach ordering is cosmetic churn. Experimental-gated tab, so LOW is right.
- **Where:** [`ui/UE5DumpUI/ViewModels/DetectStatsViewModel.cs:100`](../ui/UE5DumpUI/ViewModels/DetectStatsViewModel.cs:100), [`ui/UE5DumpUI/ViewModels/DetectStatsViewModel.cs:321`](../ui/UE5DumpUI/ViewModels/DetectStatsViewModel.cs:321), [`ui/UE5DumpUI/ViewModels/DetectStatsViewModel.cs:95`](../ui/UE5DumpUI/ViewModels/DetectStatsViewModel.cs:95), [`ui/UE5DumpUI/ViewModels/DetectStatsViewModel.cs:169`](../ui/UE5DumpUI/ViewModels/DetectStatsViewModel.cs:169), [`ui/UE5DumpUI/ViewModels/DetectStatsViewModel.cs:176`](../ui/UE5DumpUI/ViewModels/DetectStatsViewModel.cs:176)

### L19 — LiveFuncs Clear() clears-then-detaches (inverted vs ApplyFilter)

**🟡 LOW** · Effort **S** · Risk **low** · *confirmed* · Module: LiveFuncsViewModel

- **Defect:** Clear() does Results.Clear() (:284) then SelectedResult=null (:285) — the inverse of ApplyFilter which detaches first (SelectedResult=null :294, then Results.Clear() :295).
- **Failure scenario:** User clicks Clear with a row selected. Note that Clear() first sets FilterText="" (:283); when FilterText was non-empty this triggers OnFilterTextChanged→ApplyFilter which already detaches-then-clears correctly, making :284-285 redundant no-ops. The inverted order only does real work (and causes minor selection-model churn) when FilterText was ALREADY empty so the setter doesn't fire.
- **Fix:** Swap the two lines in Clear() to SelectedResult=null; then Results.Clear();, matching ApplyFilter's detach-first order.
- **Note:** Confirmed but impact is minor/often nil: the preceding FilterText="" usually runs ApplyFilter (correct order) first. Cosmetic consistency fix.
- **Where:** [`ui/UE5DumpUI/ViewModels/LiveFuncsViewModel.cs:284`](../ui/UE5DumpUI/ViewModels/LiveFuncsViewModel.cs:284), [`ui/UE5DumpUI/ViewModels/LiveFuncsViewModel.cs:285`](../ui/UE5DumpUI/ViewModels/LiveFuncsViewModel.cs:285), [`ui/UE5DumpUI/ViewModels/LiveFuncsViewModel.cs:294`](../ui/UE5DumpUI/ViewModels/LiveFuncsViewModel.cs:294), [`ui/UE5DumpUI/ViewModels/LiveFuncsViewModel.cs:295`](../ui/UE5DumpUI/ViewModels/LiveFuncsViewModel.cs:295)

> **⬇ DOWNGRADED — optional (missing cancellation; output is correct).**

### L20 — Dump All export passes no CancellationToken

**🟡 LOW** · Effort **S** · Risk **low** · *confirmed* · Module: MainWindowViewModel

- **Defect:** ExportDumpAllAsync calls DumpAllService.GenerateAsync(_dump, _engineState, fs, options, progress) at :3029 without a ct, even though GenerateAsync's signature already accepts `CancellationToken ct = default` (DumpAllService.cs:95); ExportDumpAllAsync has no CancellationTokenSource at all.
- **Failure scenario:** On a large game a full-pool dump (GameOnly:false + functions + instance counts) walks the entire GObjects array and can run for minutes; the user has no way to cancel it — there is no cancel command and no token flows into the paging loop.
- **Fix:** Add a CancellationTokenSource to ExportDumpAllAsync (or a shared export CTS), expose a Cancel command/UI affordance, and pass its token to GenerateAsync(...); the service already threads ct through its GetObjectListAsync paging loop.
- **Note:** Confirmed. Disposal is safe (await using FileStream). Pure feature gap, not a leak.
- **Where:** [`ui/UE5DumpUI/ViewModels/MainWindowViewModel.cs:3029`](../ui/UE5DumpUI/ViewModels/MainWindowViewModel.cs:3029), [`ui/UE5DumpUI/Services/DumpAllService.cs:95`](../ui/UE5DumpUI/Services/DumpAllService.cs:95)

## ⚪ INFO

### L21 — DumpExplorer reimplements space=AND locally instead of ObjectTreeFilter.MatchesAllTerms

**⚪ INFO** · Effort **S** · Risk **low** · *confirmed* · Module: DumpExplorerViewModel

- **Defect:** ApplyFilter splits SearchText on space + lowercases each term (:395-397) then MatchesTerms (:443-448) does a per-term Ordinal Contains over a single pre-lowered space-joined Haystack (name+owner+type+path, built in DumpJsonlReader.BuildHaystack :174-179), instead of using the shared ObjectTreeFilter.SplitTerms + MatchesAllTerms.
- **Failure scenario:** No functional failure: terms are AND-combined and the space separators in Haystack make cross-field spurious matches implausible, so results match the shared helper for realistic inputs. It is a helper-reuse/consistency deviation from the keyword-search MUST-rule which explicitly says 'never a single .Contains/IndexOf over one concatenated string'.
- **Fix:** Replace the local Split/ToLowerInvariant/MatchesTerms with ObjectTreeFilter.SplitTerms(SearchText) + ObjectTreeFilter.MatchesAllTerms(terms, name, owner, type, path) per row; optionally keep the precomputed lowered fields for allocation. Drop the private MatchesTerms helper.
- **Note:** INFO. Technically violates the CLAUDE.md rule wording (single Contains over one concatenated string) but is semantically equivalent in practice; the rule's server-side/exempt clause does not apply since this is a client-side box.
- **Where:** [`ui/UE5DumpUI/ViewModels/DumpExplorerViewModel.cs:395`](../ui/UE5DumpUI/ViewModels/DumpExplorerViewModel.cs:395), [`ui/UE5DumpUI/ViewModels/DumpExplorerViewModel.cs:414`](../ui/UE5DumpUI/ViewModels/DumpExplorerViewModel.cs:414), [`ui/UE5DumpUI/ViewModels/DumpExplorerViewModel.cs:443`](../ui/UE5DumpUI/ViewModels/DumpExplorerViewModel.cs:443), [`ui/UE5DumpUI/Services/DumpJsonlReader.cs:174`](../ui/UE5DumpUI/Services/DumpJsonlReader.cs:174)

---

## Verified NOT a bug (do not re-chase)

- **StandaloneTrainer `createTimer(50, fn)` "leaked repeating timer"** — the two-argument CE
  `createTimer(delay, function)` form *"executes the given function, and then selfdestructs"* (confirmed
  against Cheat Engine's `celua.txt`): it is one-shot, not repeating. The momentary TP untick is correct.

## Regression check

**0 regressions.** All nine audit-#2 fixes still hold (SQLite pragma-normalize, Mimic `ownedParams`,
Stark `call_once` + `s_installMutex`, PipeClient orphan-TCS reap, Laufen lock-order). The audit-#8 lock
discipline is correctly implemented in Solide + Hemmung (L1 notes Solitar was never retrofitted). CE Lua
output-hygiene MUST-rule satisfied by all six generators. All new DataGrids use reflection-free
`CustomSortComparer` (AOT-safe).
