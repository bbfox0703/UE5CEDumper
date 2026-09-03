# Bug / Leak / Refactor Audit #4 — Findings & Fix Plan

> **Date:** 2026-08-04 · **Build:** 2554 · **Scope:** the 96 shipped source files / +15,372 lines changed
> since the [audit #3](audit-2026-07-14-findings.md) baseline (build 2168, commit `af2ce50`, 2026-07-15) —
> the Teleport Coordinate Library, leftover-proxy cleanup, the &GEngine slot + pre-UE4 refusal, the winmm
> proxy, the `Sense` diagnostics module, Live Walker Back/Forward, and the Avalonia 12.1.0 bump.
>
> **Method:** two passes. **4a** = 10 area agents (8 bug areas + 2 refactor areas) → a refute-mandated
> skeptic per area that re-checked every `file:line` → a second diverse lens on every surviving
> HIGH/MEDIUM → dedupe + rank. **4b** = a completeness critic mapped what 4a never read, then 6 more area
> agents closed those gaps through the same find → refute → second-lens pipeline. 48 agents total.
> Test baseline at audit time: **3110 green, 0 failed**.
>
> **Status:** all items are **REPORTED, NOT YET FIXED** unless a ✅ note says otherwise. Tick them off as
> they land (write up in [dev-log.md](dev-log.md), then delete the row here — this file is the working
> tracker, not history).

**Tally:** 3 HIGH · 14 MEDIUM · 32 LOW · 3 INFO — **52 items** (26 from 4a, 25 from 4b, 1 from in-game verification).
7 findings were adversarially **refuted and dropped** (listed at the bottom — do not re-raise them).

**Progress: 52 of 52 shipped.** Builds: 2560 · 2561 · 2569 · 2577 · 2581 · 2585 · 2592 · 2596 · 2599 ·
2603 (8 DLL + scripts) · 2610 (12 UI) · 2614 (refactor R1-R4/R6/R7) · 2617 (B39).
**ALL 52 SHIPPED — but 2 of them FAILED their first in-game check and were refixed.**
See [verification-register.md](verification-register.md) for the register.
Four rounds of live testing (2026-08-04, builds 2622 → 2643): **11 verified, 1 half, 14 not yet
exercised** — and B14+R5 needed THREE attempts, which produced the two most useful lessons here.
**B34** and **B14+R5** both failed the same way — a rule applied to an ENUMERATION that had
counted wrong (three CE filenames; seven thread procs). Two lessons, both earned the hard way:

1. *A fix verified against the list it was written from is not verified.* Each was correct
   about every item on its list and wrong about the world.
2. *When a fix does not take, re-read the EVIDENCE before adding more of the same fix.* B14's
   second round put guards on all ~15 thread entry points and crashed identically — because
   there was never an exception. `~std::thread()` on a joinable thread calls `std::terminate`
   directly, and `UE5_Shutdown` never runs on game close. Fixed as a property of the TYPE
   (`Routine::SafeThread`) rather than a third list.

 The last four landed after the maintainer's decisions (2026-08-04): B13/B41
*refuse and say why*, B21 *drop AllowThousands*, B25 *require corroboration*, B26 *both halves*.
Only R8 remains, filed "later" by the audit itself.

**What is NOT done: the verification.** Every fix is filed in
[the verification register](verification-register.md),
split into ① log-derivable and ② manual-only. None of it has been run on a real game.

---

## ▶ Continuation plan — read this first if you are picking the work back up

Written 2026-08-04 at build 2587, `dev` 13 commits ahead of `main`, tree clean, 3161 C# + 929 C++
green. The goal is **finish every open item**, then verify. Batches below are ordered so each one is a
coherent commit; the *within-batch* order matters where noted.

### ~~Batch A~~ — the two DLL concurrency defects ✅ **DONE, build 2592**
**B5** is the prerequisite for trusting any offset-related bug report, so it led.
- **B5** `Frieren.cpp:490` — dedicated mutex around the `UE5_Init` body + an in-progress flag so a
  second caller waits and returns the first result. Today the latch is set *after* a multi-second scan.
- **B4** `Mimic.cpp:194` — **do NOT just call `Tot::MarkBackgroundWorker()`.** That same flag is read by
  `Tot::IsBackgroundWorker()` at `Frieren.cpp:1579` to refuse (-8) the off-game-thread invoke fallback,
  a policy deliberately scoped to *repeating* workers; blanket-marking the poller would start refusing
  user one-shot CE invokes. Add a separate per-command-cancel-immunity flag, or mark only around the
  resolve calls.

### ~~Batch B~~ — Dunste + the worker-guard structure ✅ **DONE, build 2596**
- **B8** `Dunste.cpp:453/577` — set `collisionOff/collisionPawn` from the invoke's **actual result**;
  when the invoke is skipped, KEEP the record and start the Schlacht-style deferred restore
  (`Schlacht.cpp:635-658` + `PendingRestoreLoop` is the shipped precedent). Join the worker before
  snapshotting. **Out of scope:** the `Fern.cpp:779` disconnect half — holds persisting across
  disconnect is the deliberate family policy that sank audit #3's M6.
- **B14 + R5 together** (symptom + structure) — one `RunTickGuarded` helper wrapping the five unguarded
  thread procs (`Schlacht.cpp:507` `PendingRestoreLoop`, `Solide.cpp:330`, `Hemmung.cpp:284`,
  `Laufen.cpp:358`, `Solitar.cpp:312`), plus one `ReassertWorker` helper for the six hold modules and
  `Grimoire::WORKER_SLEEP_SLICE_MS = 25` replacing the 8 bare literals.
- **B10** `Ubel.cpp:885` — add `s_walkClassExCache`, return `const ClassInfo&`. **Prerequisite:**
  `Ubel.cpp:813` is `s_walkClassCache[addr] = info;` — an assign-over-existing. Two threads racing the
  same uncached class would reallocate a vector already handed out by reference (use-after-free).
  **Change it to `try_emplace` FIRST.** Fixing the four `// cached` comments is a zero-risk standalone step.

### ~~Batch C~~ — Utf8Helpers ✅ **DONE, build 2599**
- **B28** `Utf8Helpers.h:239` — the obvious ratio-scoring fix **does not work**: `第1章` / `中A文` both
  score 0 bad, and scoring can regress the STVoyager UTF-8 case whenever the adjacent heap tail happens
  to be zero. The discriminator must be **structural** (e.g. an interior-UTF-16-null check, preferring a
  clean UTF-16 decode when one exists). Add `"中文一二"` and `"第1章"` as regression buffers.

### ~~Batch D~~ — the small-fix sweep ✅ **DONE, builds 2603 + 2610**
Split by side: 2603 = the 8 DLL + scripts items (B11, B18, B19, B22, B24, B32, B33, B34, B44, B46,
B47, B48), 2610 = the 12 UI items (B7, B9, B12, B15, B16, B17, B20, B23, B35, B36, B42, B45).
Both corrections in the plan held up and were applied as written (B9 ADDED not moved; B15 sets
`hadError` rather than a bare `return`).

### ~~Batch E~~ — refactor ✅ **DONE, build 2614**
R1, R2, R3, R4, R6, R7 (R5 shipped with B14 in 2596; R8 remains "later").
**R4's "measure first" instruction paid off and inverted the recommendation** — the four-field split
the finding proposed as the ideal shape measured **2× slower** (55.1 ms vs 26.7 ms over 500K entries
× 3 terms), while a single pre-lowered haystack with `OrdinalIgnoreCase` was **not** a regression
(25.8 ms) and produced identical hit counts. Rejected on the number, recorded in the code.

### B39 ✅ **DONE, build 2617** — was missing from this plan entirely
Not in any batch above; the omission was in the plan, not the finding. Both writers (four in
`Flamme.cpp`, one in `AobUsageService`) now stage through a **per-process** `.tmp.<pid>`. The final
rename stays last-writer-wins — that is the accepted semantics — but the staging file must not be
shared, or one process truncates it while the other is mid-write and the loser's partial document
gets renamed over the real cache.

### ~~Needs a decision from the maintainer~~ ✅ ALL FOUR DECIDED AND SHIPPED (build 2621)
**B13/B41** (which volume-recycler API), **B21** (the `AllowThousands` tradeoff — removing it rejects
Excel's `"67,162.398"`), **B25** (should the version refusal ever fire on an uncorroborated signal),
**B26** (should duplicate CE records be deduped at push), ~~**B43**~~ ✅ **DONE build 2620** — was not actually a decision: the finding prescribed the fix
and ruled out the dangerous alternative.

### The DO-NOT list, carried forward
Do not raise the CE-side timeout. Do not add `CancelSynchronousIo` without re-measuring first. Do not
add a mailbox `shutdownState` field. Do not extract the 8-copy player chain *and* "fix" Schlacht's
missing fallback (its omission is deliberate — the fallback is a 486K full-pool scan on a 10 Hz timer).
Do not split `Aura.cpp` beyond steps 1–2. Do not build `MovementKnobCardViewModel`.

### Verification, in the two halves the maintainer asked for
Every fix lands with its verification classified **at the time it ships**, into
[the verification register](verification-register.md):
1. **Log-derivable** — provable from an ordinary session's logs, or from logs *added for the purpose*.
   Prefer this. If an added log is heavy (per-object, per-tick), say so in its commit and mark it for
   removal once the item is verified.
2. **Manual-only** — needs a human at the keyboard doing something a log cannot cause. These go in the
   manual section with the exact click sequence and the PASS/FAIL observation.
Never file an item without saying which half it is in.
**+1 found by in-game verification** (B49), which is why 51 became 52.

> ### ✅ Verification discipline
> Nothing here is a raw finder claim. Every item survived a skeptic whose mandated default stance was
> *"this is not a bug"*; HIGH/MEDIUM items additionally faced a second, independent lens judging
> reproducibility, observable consequence, and whether the proposed fix would break something deliberate.
> Two items were verified a third time by reading the code directly during synthesis (**B27**, **B31**),
> and **B31's** second lens did not reason at all — it built a scratch app against the exact production
> packages (Serilog 4.4.0 + Sinks.File 7.0.0) with `CreateFileLogger` copied verbatim and **measured** the
> sink dying at the size limit: 1 file created, frozen at 66,280 bytes, 69 of 500 events kept, post-limit
> Warning/Error markers absent.
>
> **A note on 4b's first run:** 5 of its 6 verifiers died mid-run on a usage limit, and the workflow's
> post-processing silently discarded their areas' findings as "no verdict returned". The run was resumed
> after the script was changed to *keep* such findings flagged `UNVERIFIED` instead of dropping them.
> The final numbers above come from the completed re-run (18 agents, 0 errors, 0 unverified). **Lesson
> worth keeping: a fan-out that treats "the checker died" and "the checker said no" identically will
> quietly report a hole as a clean bill of health.**

**Legend:** Effort **S**=hours · **M**=1 session · **L**=multi-session. Risk = chance the *fix* breaks
existing behaviour / perf.

---

## Summary table — bugs & leaks

| ID | Sev | Eff/Risk | Module | One-line defect |
|----|-----|----------|--------|-----------------|
| **B27** ✅ | 🔴 | S/low | App composition root | ~~11 positional args to a 12-param ctor ⇒ `CoordinateLibraryStore` binds `null` ⇒ the whole Coordinate Library never persists~~ **FIXED build 2560** |
| **B1** ✅ | 🔴 | M/med | CE teardown | CE Disable either never tears down (reported clean) **or** bricks the DLL for the session — exactly one is live, and they must be fixed together |
| **B49** ✅ | 🔴 | S/med | Fern | ~~`Fern::Stop` closes a SYNCHRONOUS listen handle to "unblock" the accept thread; the close instead BLOCKS until a client connects — under `m_connMutex` — so a disable with no UI connected wedges the teardown thread forever~~ **FIXED build 2569** |
| B2 ✅ | 🟠 | S/low | Genau | ~~SymbolExport winner published in the AOB field ⇒ CE table / trainer / symbol all dead on modular UE builds~~ **FIXED build 2581** |
| B3 ✅ | 🟠 | S/low | CeXmlExport | ~~`<Description>` never XML-escaped ⇒ one `&` in a game string voids the entire export~~ **FIXED build 2581** |
| B4 ✅ | 🟠 | M/med | Mimic | ~~Mailbox thread not a background worker ⇒ a latched per-command cancel empties every CE object lookup for the session~~ **FIXED build 2592** |
| B5 ✅ | 🟠 | M/med | Frieren | ~~`s_initialized` latched *after* the multi-second scan ⇒ a concurrent second full init corrupts DynOff silently~~ **FIXED build 2592** |
| B6 ✅ | 🟠 | S/low | Coord library | ~~Clear-all: no confirm, no pre-clear backup, `.bak` expires after 2 saves~~ **FIXED build 2560** |
| B7 ✅ | 🟠 | S/low | Coord library | ~~Uid is the one field skipping every ingress guard; duplicate uid + delete-by-uid wipes rows the user didn't select~~ **FIXED build 2610** |
| B8 ✅ | 🟠 | M/med | Dunste | ~~Collision state committed independently of the invoke ⇒ pawn left non-colliding, falls through the world~~ **FIXED build 2596** |
| B9 ✅ | 🟠 | S/low | MainWindowVM | ~~Wrong-game warning never runs on connect, never clears on disconnect~~ **FIXED build 2610** |
| B10 ✅ | 🟠 | M/med | Ubel | ~~`WalkClassEx` has no memo despite 4 call sites commented `// cached`; deep-copies under the global lock~~ **FIXED build 2596** |
| B28 ✅ | 🟠 | M/med | Utf8Helpers | ~~UTF-8-first gate accepts a UTF-16 CJK buffer whose byte at `n−1` is `0x00` ⇒ ASCII mojibake, UTF-16 branch unreachable~~ **FIXED build 2599** |
| B29 ✅ | 🟠 | S/low | Methode | ~~CE-plugin "already loaded" guard matches by **filename alone** ⇒ ReShade's `dxgi.dll` makes it refuse to inject~~ **FIXED build 2577** |
| B30 ✅ | 🟠 | S/low | UE5CEDumper.CT | ~~Every `ue5_inject()` bail-out leaves CE's record ticked ⇒ untick runs a real `UE5_Shutdown` against a proxy this script never injected~~ **FIXED build 2561** |
| B31 ✅ | 🟠 | S/low | LoggingService | ~~`fileSizeLimitBytes` without `rollOnFileSizeLimit:true` ⇒ the sink silently stops writing at 8 MB for the rest of the process~~ **FIXED build 2585** |
| B11 ✅ | 🟡 | S/low | Sein | ~~`fprintf` on a NULL `FILE*` after a failed rotation reopen ⇒ can terminate the game~~ **FIXED build 2603** |
| B12 ✅ | 🟡 | S/low | Proxy cleanup | ~~Confirm/status text asserts things the executed plan contradicts~~ **FIXED build 2610** |
| B13/B41 ✅ | 🟡 | M/low | Proxy cleanup | ~~"Recycle Bin" promise unverifiable; a drive-letter test is not a recycler test~~ **FIXED build 2621** |
| B14 ✅ | 🟡 | S/low | DLL workers | ~~Thread-proc exception guard rolled out to 2 of 7 thread procs~~ **FIXED build 2596** (with R5) |
| B15 ✅ | 🟡 | S/low | TeleportScriptGen | ~~Mailbox timeout `break`s into the auto-close ⇒ the CE window shuts on a failure~~ **FIXED build 2610** |
| B16 ✅ | 🟡 | S/low | TeleportPanel | ~~5 coord-grid columns sort on nested/mismatched paths ⇒ dead headers under AOT~~ **FIXED build 2610** |
| B17 ✅ | 🟡 | S/low | TeleportVM | ~~Pose not cleared on disconnect ⇒ the next game's library renders "0 of N"~~ **FIXED build 2610** |
| B18 ✅ | 🟡 | S/low | Genau | ~~Extra Scan ignores `Tot::Requested()` ⇒ CE UI freezes on the unbounded join~~ **FIXED build 2603** |
| B19 ✅ | 🟡 | S/low | Sein | ~~One shared `error_code` ⇒ the first undeletable entry aborts the whole retention sweep, forever~~ **FIXED build 2603** |
| B20 ✅ | 🟡 | S/low | TeleportVM | ~~Filter keystroke reverts an uncommitted edit; `_coordFilterMemory` never disposed~~ **FIXED build 2610** |
| B21 ✅ | 🟡 | S–M/low | Coord parsers | ~~Three independent import-parser holes (AllowThousands, quote-state, regex-in-literal)~~ **FIXED build 2621** |
| B22 ✅ | 🟡 | S/low | Laufen | ~~Base captured as 0 ⇒ the knob pins the CMC value at 0 against the game~~ **FIXED build 2603** |
| B23 ✅ | 🟡 | S/low | CE Lua | ~~Autorun binds DEBUG at CE start (its own instruction can't work); non-finite double emitted as bare `Infinity`~~ **FIXED build 2610** |
| B24 ✅ | 🟡 | S/low | Frieren | ~~Forced hook installs burn the automatic retry budget~~ **FIXED build 2603** |
| B25 ✅ | 🟡 | S/med | Genau | ~~Total scan refusal armed off an uncorroborated PE ProductVersion~~ **FIXED build 2621** |
| B26 ✅ | 🟡 | M/med | PointerQueryScriptGen | ~~Duplicate GameEngine records: the older record's DISABLE frees the newer one's buffer~~ **FIXED build 2621** |
| B32 ✅ | 🟡 | S/low | UE5CEDumper.CT | ~~The "very old DLL" fallback is unreachable ⇒ a modal **timeout error on a healthy inject**~~ **FIXED build 2603** |
| B33 ✅ | 🟡 | S/low | UE5CEDumper.CT + CeReadinessLua | ~~Readiness poll resolves only bare `g_invokeMailbox`, never `UE5Dumper.g_invokeMailbox`~~ **FIXED build 2603** |
| B34 ✅ | 🟡 | S/low | Heiter | ~~CE-plugin detection is a 1 s race ⇒ AOB scan + pipe server open **inside cheatengine-x86_64.exe**~~ **FIXED build 2603** |
| B35 ✅ | 🟡 | S/low | DiagnosticsProbe | ~~The probe's own closing round-trip falls inside the measured window ⇒ `transportMs > wallMs`, `ui` clamps to 0~~ **FIXED build 2610** |
| B36 ✅ | 🟡 | S/low | PropertySearchPanel | ~~No `FallbackValue` ⇒ all four mutually-exclusive Force actions render when nothing is selected~~ **FIXED build 2610** |
| B37 ✅ | 🟡 | S/low | LoggingService | ~~Count-based folder eviction ranks by **directory mtime** — the signal its own sibling documents as unusable~~ **FIXED build 2585** |
| B38 ✅ | 🟡 | S/low | ProxyDeployVM | ~~Leftover-proxy reports written to `%LOCALAPPDATA%\Reports`, not `…\UE5CEDumper\Reports`~~ **FIXED build 2585** |
| B39 ✅ | 🟡 | M/med | Flamme | ~~Four HintCache writers share one fixed `.tmp` path; the UI writes the byte-identical path from another process~~ **FIXED build 2617** |
| B40 ✅ | 🟡 | S/low | UE5CEDumper.CT | ~~`ue5_callDLL` uses bare `getAddress` and tests for nil — CE *throws*, aborting the disable block and leaking the log FILE handle~~ **FIXED build 2561** |
| B42 ✅ | 🟡 | S/low | App | ~~Second launch calls `Shutdown(1)` before the logger exists — no window, no dialog, no log line~~ **FIXED build 2610** |
| B43 ✅ | 🟡 | M/med | Lugner_Winmm | ~~Exclusive SRWLOCK held across `LoadLibraryW` + Sein file I/O; the dxgi safety precondition it copies does not transfer~~ **FIXED build 2620** |
| B44 ✅ | 🟡 | S/low | Lugner_Winmm.asm | ~~Thunk tests `mProcs[N]` before the resolver but not after ⇒ `jmp rax` with `rax==0` if a name never resolves~~ **FIXED build 2603** |
| B45 ✅ | 🟡 | S/low | ProxyDeployPanel | ~~Orphan-scan Cancel shown by the shared `IsScanning` flag but wired to a different command ⇒ a ghost Cancel on the wrong card~~ **FIXED build 2610** |
| B46 ✅ | 🟡 | S/low | Renge | ~~`HexToBytes` maps non-hex chars to `0x00`, drops an odd trailing nibble, cannot report failure — `write_mem` answers `ok:true`~~ **FIXED build 2603** |
| B47 ✅ | 🟡 | S/low | Heiter | ~~"First-proxy-wins" mutex is `Global\…` though the comment says per-process; without `SeCreateGlobalPrivilege` the dedup silently never fires~~ **FIXED build 2603** |
| B48 ✅ | 🟡 | S/low | gen_proxy_forwarders | ~~No PE-machine check ⇒ under 32-bit Python, WOW64 redirection feeds the **x86** winmm into an x64-only target~~ **FIXED build 2603** |

## Summary table — refactor & hygiene

| ID | Verdict | Eff/Risk | Item |
|----|---------|----------|------|
| R1 ✅ | done | S/low | `docs/naming-convention.md` — three module lists, the first two 8 modules stale |
| R2 ✅ | done | S/low | Delete 3 private Lua escapers + the private preamble/close copies; use `CeLuaHygiene` |
| R3 ✅ | done | S/low | `CeLuaHygiene.AppendIdleWait` at the **2** generators that sample `cmd` once (not 11) — **the scoping was wrong, see the note below the table** |
| R4 ✅ | done | S/low | `DumpExplorerViewModel` — the sole holdout of the space=AND keyword MUST rule |
| R5 ✅ | done | S–M/low | ~~One `ReassertWorker` helper for the six hold modules~~ **DONE build 2596** — new `Routine.h` (`ReassertLoop` / `RunTickGuarded` / `SleepSliced`) + `Grimoire::WORKER_SLEEP_SLICE_MS` |
| R6 ✅ | done | S/low | `en.axaml` — 24 inert keys, 2 shadowed by hardcoded C#; **zero dangling references** |
| R7 ✅ | done | S/low | `aob_specificity.py` docstring says "NOT WIRED INTO CI" 3 days after `6f594fa` wired it in as a blocking gate |
| ~~R8~~ | **refuted** | — | ~~`build.ps1` dist native payload never refreshed outside `-Clean`~~ — **the refresh half is false** (`build.ps1:514-516` re-copies every native with `-Force` each Publish; the old mtimes are the NuGet packages' own). Raised on an mtime proxy — the audit's own 4b root cause. Prune-only remainder is benign. Closed 2026-08-05 |
| — | later | — | `RunGuardedAsync` over TeleportVM's 55 busy/error blocks · LiveWalkerVM's hand-rolled debounce → `KeywordSearchMemory` · 135 hardcoded AXAML strings · the 12-generator mailbox emitter (after B15) · `ValidateAndFixOffsets` Step extraction · `Fern::DispatchCommand` handler table |
| — | **never as filed** | — | The 8-copy player-chain extraction *with* "fixing" Schlacht's omission (it is deliberate — the fallback is a 486K full-pool scan on a 10 Hz timer) · the `Aura.cpp` split beyond steps 1–2 · `MovementKnobCardViewModel` (33 binding paths, silent AOT failure, zero user gain) |

> **R3's scoping was wrong, and the way it was wrong is reusable.** The row bounded exposure by
> asking *"which generators read `cmd`?"* — 2 of 11 — and concluded the other 9 "cannot spuriously
> report busy". True, and beside the point: the hazard is not a bad READ, it is a `cmd` **write** that
> races the DLL's trailing `cmd = CMD_IDLE`. Enumerating the readers therefore *excluded* the worst
> case — `InvokeScriptGenerator`, which read `cmd` not once but **never**, and fires **three**
> back-to-back round-trips (`CMD_FIND_INSTANCE` → `CMD_FIND_FUNCTION` → `CMD_INVOKE`). A wiped
> command there surfaces as `waitDone` timing out with "the DLL never saw this command", which sends
> the user to re-inject a healthy DLL. Fixed 2026-08-07 (one emitted `waitIdle()` helper, 4 call
> sites); pinned by `CeMailboxBailoutTests.HelperShapedScripts`. The correct predicate is **"writes
> `cmd`"**.
>
> **`PointerQueryScriptGenerator` was a fourth site the same enumeration missed, and it was NOT
> cosmetic** — folding it in (2026-08-07) turned up two live defects in its *second* hand-rolled loop,
> the status poll, which had never been counted at all because R3 was scoped to idle waits: it
> counted `sleep(1)` iterations against a millisecond constant, so its stated 10 s deadline was
> **~155 s** of frozen Lua Engine, and it announced `'mailbox timeout (DLL not responding?)'` — the
> exact guess `CeMailboxBailoutTests.TheOldGuessingTimeoutMessage_IsGone` was written to forbid,
> surviving in a file that test never covered. Its `query()` helper returns `nil, reason` so the
> GameEngine path can fall through from the `&GEngine` slot to a snapshot, which no existing
> `MailboxTimeout` mode expressed; hence the new `ReturnReason` mode. It was also missing
> `AppendContractCheck` entirely. Pinned by `HelperReturningScripts`.
>
> Two method lessons, both cheap: **the guessing-message test existed and passed for 40+ builds while
> the string it forbids sat in an uncovered generator** — a `[Theory]` is only as wide as its
> `MemberData`, so a rule with a fixed script list is a rule with a silent exemption list. And
> **folding a hand-rolled copy onto a shared emitter invalidates the tests that pinned the copy**: one
> broke loudly (`waited` → `idleWaited`) and one went **vacuously green** — a `DoesNotContain("elapsed >= ")`
> that passed because the refactor deleted the construct, with its comment still describing it.
> `PointerQueryScriptGeneratorTests` now asserts the construct is present *before* asserting it is
> escaped.

---

## Cross-cutting root causes

The two passes found **two different** ones. Both are worth fixing as patterns, not site-by-site.

### 4a — the report and the reality are computed by different code paths

> A success message, an availability flag, or a persisted state is written by code that never observes
> whether the underlying operation ran.

B1 (`pcall` succeeded ⇒ "clean shutdown", for a call that made no remote thread) · B2 (a mangled symbol
in the `gworldAob` field ⇒ the UI's "AOB available" contract lies) · B8 (`collisionOff = wantOff` before
the invoke, then wiped when the invoke is skipped) · B12/B13 (the dialog promises the Recycle Bin /
"outside a Steam library" / "nothing removed" independent of the plan executed) · B16 (a sort affordance
enabled for a path that cannot resolve) · B21 (a silently wrong number reported as a parse success) ·
B30, B35.

### 4b — a cheap proxy signal substituted for a predicate the codebase already computes

> …and in almost every case a sibling **in this same repo** implements the real check correctly.

| Item | Proxy signal used | The real predicate — and where the repo already does it |
|---|---|---|
| B29 | module **filename** | export probe (`UE5CEDumper.CT:172`) / PE ProductName (`DumperModuleDetector.cs:57`) |
| B34 | a 1-second **sleep** | which export CE actually called / the host process name |
| B32 | `mb == nil` | a build stamp / a `UE5_GetVersion` probe |
| B37 | **directory mtime** | newest file inside — *in the sibling method 30 lines below* (`:558-571`) |
| B47 | a `Global\` **name** | the PID |
| B41 | `DriveType.Fixed` | the volume's `NukeOnDelete` / BitBucket policy |
| B48 | the **path** `System32` | the PE machine field |
| B33 | one symbol **spelling** | both spellings — 8 other call sites do it |
| B44 | a pre-resolve null test | a post-resolve null test |
| B36 | a binding **path** | an explicit `HasSelection` predicate (`CanForceAny` exists, unused) |

10 of 22. More actionable than 4a's, because the fix is usually *"call the function your sibling already
wrote"* rather than new design.

### Secondary (4b) — silent defaults at composition points

**B27** (an unpassed optional parameter defaults to `null`), **B31** (an unpassed Serilog parameter
defaults to `false`), **B38** (a path composed with a segment missing), **B45** (a shared flag used where
a dedicated one was needed). None fail at compile time; none produce an error at runtime. This is why
B27's *composition-root test* is worth more than its one line of production change.

### Secondary (4a) — per-site policy enforced by convention, not structure

A rule applied by hand-copying it to a list of sites lands at N−k: B4 + B14 (thread-hardening at 6/7 and
2/7), B9 + B17 (disconnect-reset field lists), B15 + R2 (the shared CE-Lua emitter rule), R4, R5.

---

## 🔴 HIGH

### B27 — Coordinate Library never persists: the store is constructed and never passed

> **✅ FIXED — build 2560, shipped with B6 in one commit.** Wiring moved out of `App` into
> `AppComposition.BuildMainWindowViewModel`, **whose parameters are all required**, so the compiler
> now enforces what optional parameters cannot; `App` and `CompositionRootWiringTests` call the same
> helper, closing the blind spot where a test that built the VM itself would only prove that *the
> test* passed the store. **Guard verified, not assumed:** dropping the argument again was re-tried
> on disk and the build failed with `CS7036 … required parameter 'coordLibrary'`. Three tests — the
> positive, a negative control (omit it ⇒ `internal HasCoordStore` is false, so the positive cannot
> pass for the wrong reason), and a structural one pinning `AppComposition`'s parameters as required
> with an arity matching the VM ctor. 3117 green.
> *Delete this row after the batch is merged to main.*

**🔴 HIGH** · Effort **S** · Risk **low** · *confirmed by 2 lenses + read directly during synthesis*
· Module: App composition root → MainWindowViewModel → TeleportViewModel

- **Defect:** `_coordLibraryStore` is constructed at `App.axaml.cs:65` and never read again. The
  `new MainWindowViewModel(...)` call passes **11 positional arguments** into a 12-parameter constructor
  whose 12th is `CoordinateLibraryStore? coordLibrary = null`, so it binds to its `null` default and is
  forwarded into `new TeleportViewModel(..., coordLibrary)`. C# optional-parameter binding makes the
  omission invisible at compile time.
- **Failure scenario:** User opts into experimental, connects, opens Teleport → Coordinate Library, saves
  40 labelled coordinates across several maps. `PersistCoordLibrary` returns at `_coordStore == null`
  before touching disk; `%LOCALAPPDATA%\UE5CEDumper\teleport-coords.<module>.json` is never created. On
  relaunch `LoadCoordLibrary` returns at the same guard. **The whole library is gone, silently, every
  restart.** A second independent guard exists (`_activeCoordKey` is only assigned *after* the null
  check), so there are two reasons the write never happens. `ApplyCoordImport` likewise evaluates
  `_coordStore?.SavePreImportBackup(...) ?? ""`, so a Replace-mode import overwrites with **no
  `.preimport.bak`** and silently drops the "backed up to…" clause.
- **Fix:** Pass `_coordLibraryStore` as the 12th argument. Add a composition-root test that constructs
  `MainWindowViewModel` exactly as `App` does and asserts Teleport's store is non-null — the only
  existing test that builds the VM (`MainWindowInjectHelperTests.cs:105`) passes 7 **named** args, so it
  can never catch this class of omission.
- **⚠️ Interaction with B6 — do not ship B27 alone.** B6 (Clear-all has no pre-clear backup) is
  *currently harmless precisely because nothing persists*. The moment this wiring lands, B6 becomes a
  live, unrecoverable data-loss path. **Same commit, B6 first or together.**
- **Severity note (honest):** both skeptic lenses calibrated this MEDIUM — the card is experimental-gated
  and the library is fully functional in-session. It is ranked HIGH here because the loss is total,
  silent, repeats every restart, defeats the feature's stated headline value (a per-game library keyed by
  module name *specifically so it survives a game patch*), and removes the documented import rollback.
  The fix order does not change under either reading.
- **Where:** [`ui/UE5DumpUI/App.axaml.cs:65`](../ui/UE5DumpUI/App.axaml.cs:65),
  [`ui/UE5DumpUI/App.axaml.cs:76`](../ui/UE5DumpUI/App.axaml.cs:76),
  [`ui/UE5DumpUI/ViewModels/MainWindowViewModel.cs:435`](../ui/UE5DumpUI/ViewModels/MainWindowViewModel.cs:435),
  [`ui/UE5DumpUI/ViewModels/MainWindowViewModel.cs:472`](../ui/UE5DumpUI/ViewModels/MainWindowViewModel.cs:472),
  [`ui/UE5DumpUI/ViewModels/TeleportViewModel.cs:3026`](../ui/UE5DumpUI/ViewModels/TeleportViewModel.cs:3026),
  [`ui/UE5DumpUI/ViewModels/TeleportViewModel.cs:3048`](../ui/UE5DumpUI/ViewModels/TeleportViewModel.cs:3048)

### B1 — CE Disable teardown: two coupled defects, exactly one is live today

> **✅ FIXED — build 2561, shipped with B30 + B40 in one commit.** Both halves together, as required.
> **(a)** all three emitters now go through one `CeLuaHygiene.AppendCallDllHelper`, which emits
> `pcall(executeCodeEx, 0, 5000, fn)` and — just as important — checks the *result* rather than
> `pcall`'s status, because a wrong-arity call returns `nil` without raising. The `.CT` wrapper and its
> misleading `:148` comment are fixed too. **(b)** `UE5_AutoStart` now calls `Tot::ResetShutdown()` +
> `Mimic::StartThread()` at the top (both no-ops on a first start; `StartThread` was already
> re-callable — it early-returns on `s_running` — nothing had ever called it outside `DllMain`).
> `Tot::ResetShutdown` had to move here rather than rely on `Fern::Start`, which runs *after*
> `UE5_Init`: a re-enable would otherwise rescan with `g_shutdown` still latched and every
> `StartWorker*` gate would refuse to spawn.
>
> The re-enable path is no longer "skip everything": all three scripts now read `initState` to tell
> **SERVING** (a proxy or another instance owns the pipe — not ours, untick so the disable can never
> tear it down) from **PARKED** (`UE5_Shutdown` left it at IDLE — revive in place via `UE5_AutoStart`,
> since the DLL is still mapped and re-injecting would double-map).
>
> **One deliberate invariant was narrowed, not quietly dropped.** `Enable_never_uses_executeCodeEx_in_code`
> forbade `executeCodeEx` anywhere in `[ENABLE]`, justified by *"start-up is exactly when games block
> CreateRemoteThread"*. That reason covers the **start-up path only**, and reviving a parked DLL
> genuinely requires a remote call — the mailbox poller has been joined, so no memory-write channel
> remains. The test is now two: no `executeCodeEx` from `injectDLL` onward (the region that runs during
> real start-up), and every remaining use must be the shared emitter's exact text. Same narrowing, same
> reasoning, in the autorun twin.
>
> Coverage added where the audit said there was none: `CeExecuteCodeExArityTests` pins the 3-argument
> form and the finite-non-zero timeout across both generators **and reads the shipped `.CT` from disk** —
> the first automated coverage that file has ever had. Verified by negative control: reverting the `.CT`
> to the 2-argument form fails `Shipped_cheat_table_passes_the_address_as_argument_three`. 3124 green.
>
> **✅ IN-GAME VERIFIED 2026-08-04 (Elliot, UE 5.04) — and the verification found a regression the fix
> itself created.** The cycle works: `UE5_Shutdown: Cleaning up...` logs, the UI's pipe drops, a re-tick
> logs `UE5_AutoStart: entry` and the UI reconnects. Twice. **But** making `UE5_Shutdown` run for the
> first time exposed a permanent hang inside `Fern::Stop()` that the broken call had been hiding —
> filed and fixed as **B49** (build 2569). B1 is not safe to ship without B49.
>
> *Delete this row after the batch is merged to main.*

**The evidence that settled it, kept for the record.** CE's own `celua.txt:589` gives the signature as
> **`executeCodeEx(callmethod, timeout, address, params...)`** — `callmethod` 0=stdcall/1=cdecl,
> `timeout` in ms (`nil`/`-1` = forever, **`0` = no wait and the call memory is never freed, i.e. a
> leak**), **address is argument 3**. So `executeCodeEx(0, fn)` binds `timeout = fn` and
> `address = nil`. Running the `.CT` teardown for real produced
> `[UE5Dump ERROR] executeCodeEx returned nil for UE5_StopPipeServer` and the same for
> `UE5_Shutdown`: **it returns `nil` without raising**, so no remote call happens and `UE5_Shutdown`
> has never run in the field. **Fixing the arity alone therefore WILL brick the session — (b) is now
> a certainty, not a hypothesis.**
>
> Three corrections to the finding text below, all of which change the fix:
> 1. Argument 2 is **`timeout`**, not a callback/`nil` separator. The proposed
>    `pcall(executeCodeEx, 0, nil, fn)` would be *valid* (nil = wait forever) but is the wrong choice
>    for a teardown call — it hangs CE's UI indefinitely on a stalled game thread. Emit a **finite**
>    timeout: `pcall(executeCodeEx, 0, 5000, fn)`. **Never `0`** — that is the documented leak.
> 2. `scripts/UE5CEDumper.CT:148`'s comment — *"executeCodeEx: retType 0=void, 1=integer"* — is a
>    misreading of `callmethod` (a calling convention) as a return type. That comment is how the bug
>    got written; fix it in the same commit or it will be written again.
> 3. **The generated scripts fail worse than the `.CT`.** `ue5_callDLL` checks for `nil` and logs an
>    error (which is how this was caught). The generators emit
>    `return (pcall(executeCodeEx, 0, fn))`, whose parentheses truncate to the **pcall status** — and
>    since the call returns `nil` rather than raising, `a` and `b` are both `true`, the `if not (a and
>    b)` branch is skipped, and the window **auto-closes reporting a clean shutdown**. A live,
>    measured instance of this audit's own 4a root cause.
>
> Remaining to confirm (cheap, closes the loop): no `UE5_Shutdown: Cleaning up...` in the game's
> `init-0.log` for that session.

**🔴 HIGH** · Effort **M** · Risk **med** · *(a) confirmed in-game; (b) proven latent*
· Module: CeInjectScriptGenerator + CeAutorunScriptGenerator + `UE5CEDumper.CT` + Frieren + Heiter

- **Defect (a):** all three CE paths call `executeCodeEx(0, fn)` — 2 args, so the address never
  arrives. `scripts/ue5_dissect.lua:44` (`executeCodeEx(1, nil, fn, ...)`) is the only call site in
  the repo that puts the address in slot 3. *(The claim that `docs/lessons-learned.md:10` documented
  the correct form was wrong — that line's own example was malformed too, and has been corrected.)*
- **Defect (b):** if the call *did* run, `UE5_Shutdown` latches `Tot::RequestShutdown()` and calls
  `Mimic::StopThread()` — and `Mimic::StartThread()` has exactly one caller, `Heiter.cpp:194`, inside
  `DllMain(DLL_PROCESS_ATTACH)`, which never re-runs. Both re-enable guards return before `UE5_AutoStart`.
- **The interaction:** every caller of `UE5_Shutdown` is one of those same CE Lua paths, so **(a) is
  currently masking (b)**. Fixing the arity alone converts a silent no-op into a session-long brick (no
  pipe, no mailbox, `g_shutdown` latched, recovery = restart the game). Conversely, if CE's binding
  tolerates the 2-arg form, (a) is a false alarm and (b) is already live. **One live test settles both:**
  untick the record and check whether the DLL log shows `UE5_Shutdown: Cleaning up...`.
- **Failure scenario:** user unticks "Inject DLL + Start Pipe Server", then re-ticks it. Today: teardown
  never happens, the window closes reporting clean, `ue5_shutdown()` returns true. Post-arity-fix-alone:
  pipe and mailbox gone, the dialog tells the user to connect to a pipe that no longer exists, every CE
  hotkey spins to timeout.
- **Fix:** land together — (a) emit `pcall(executeCodeEx, 0, 5000, fn)` in both generators **and** fix
  `ue5_callDLL` + its `:148` comment in the shipped `.CT`; (b) make `Mimic::StartThread()` re-callable
  (it already early-returns on `s_running`) and call it from `UE5_StartPipeServer`/`UE5_AutoStart`, and
  change the already-loaded branch of both `ue5_inject()` implementations from "skip everything" to
  "skip `injectDLL`, still call `UE5_AutoStart` and poll `initState`". Tighten the two tests to assert
  the full three-argument text, and add one asserting the timeout is neither `0` (leak) nor absent.
  Also make the generators stop reporting `pcall` status as success: check the call's own result, since
  a wrong-arity call returns `nil` without raising.
- **Note:** `CeInjectScriptGenerator.cs:170-171` already *claims* "UE5_AutoStart is idempotent and resets
  initState on the way through" — the guard returns before any such call. Documented intent vs behaviour.
- **Where:** [`ui/UE5DumpUI/Services/CeInjectScriptGenerator.cs:163`](../ui/UE5DumpUI/Services/CeInjectScriptGenerator.cs:163),
  [`ui/UE5DumpUI/Services/CeAutorunScriptGenerator.cs:152`](../ui/UE5DumpUI/Services/CeAutorunScriptGenerator.cs:152),
  [`scripts/UE5CEDumper.CT:207`](../scripts/UE5CEDumper.CT:207),
  [`dll/src/Frieren.cpp:550`](../dll/src/Frieren.cpp:550),
  [`dll/src/Heiter.cpp:194`](../dll/src/Heiter.cpp:194)

---

## 🟠 MEDIUM

### B49 — `Fern::Stop` waits for a client that may never come, holding `m_connMutex`

> **✅ FIXED — build 2569.** Found by in-game verification of B1, not by any audit pass: fixing B1 made
> `UE5_Shutdown` run for the first time, and that made this reachable. **It was unreachable before,
> which is why three audits missed it.**

**🔴 HIGH** · Effort **S** · Risk **med** · *measured in-game, root cause confirmed against the logs*
· Module: Fern

- **Defect:** `Fern::Stop()` called `CloseHandle(m_listenPipe)` under a comment describing it as a
  *"proven unblock"* for the accept thread's `ConnectNamedPipe`. The listen instance is a **synchronous**
  handle (`AcceptLoop`'s `CreateNamedPipeW` passes no `FILE_FLAG_OVERLAPPED`), so the close does not
  abort the parked call — it **blocks until that call completes**, i.e. until somebody connects. It did
  so while holding `m_connMutex`, which every connection thread needs in order to unregister itself and
  satisfy the drain wait.
- **Failure scenario (measured, Elliot UE 5.04, 2026-08-04):** two disables where the user happened to
  reconnect the UI took **9.4 s** and **13.3 s** — not a constant, so not a timeout. The third disable
  came after the UI had already disconnected: `pipe-0.log` shows `UE5_Shutdown: Cleaning up...`, then
  `Mailbox: polling thread stopped`, then **nothing — no `AcceptLoop exiting`, no `PipeServer: Stopped`,
  ever**. That teardown thread stays parked in the game process holding `m_connMutex` for the rest of
  the session, `UE5_Shutdown` never completes, and `s_initialized` / `initState` are never reset.
- **Fix:** send a **wake-connect** instead — `CreateFileW` on our own pipe name, immediately closed.
  `m_running` is already false, so `AcceptLoop` takes its `!m_running` branch and closes its **own**
  handle. This also repairs a latent leak: `Stop()` used to null `m_listenPipe`, so the accept thread's
  `if (m_listenPipe == pipe)` guard failed and nobody ever closed that instance. Gated on a listener
  actually being parked, so a second instrumented game (the pipe name is machine-global) does not see a
  phantom connect.
- **Three more defects found on the same path, all fixed here:**
  1. `Start()`'s only guard was `m_running`, which `Stop()` clears in its *first* statement — so the
     whole teardown window reads as "stopped", and starting there move-assigns onto a still-joinable
     `m_acceptThread`: a standard-mandated `std::terminate`, no log, no dump. New `m_stopping` flag.
  2. A duplicate `StopAllWatches()` sat *before* the cancel block — exactly the ordering the surviving
     call's own comment warns against. Deleted.
  3. The CE teardown called `UE5_StopPipeServer` then `UE5_Shutdown`, but the latter **is** that
     `Stop()` plus everything else, run deliberately *after* `Stark::Shutdown` so a pipe thread blocked
     in `EnqueueInvoke` gets its −7 and unwinds. Calling it first inverted that ordering, and since the
     CE call times out while the remote thread keeps running, it put two teardowns in the process
     concurrently. All three emitters now call `UE5_Shutdown` alone.
- **Instrumentation is part of the fix.** `Stop()` logged exactly one line, so a 5 s stall elsewhere and
  a 5 s expiry of the connection-drain wait were indistinguishable from outside — which is what made
  this expensive to diagnose. It now logs entry with the connection count, per-phase elapsed ms, and
  explicitly whether that wait was `satisfied` or `TIMEOUT` with how many connections remained.
- **Deliberately NOT done:** raising the CE-side timeout (no finite value bounds an unbounded wait);
  `CancelSynchronousIo` on the connection handles (this run never exercised `CancelIoEx` there — it is
  sequenced behind the blocking close and never executed — so there is **no evidence** it is broken;
  re-measure with the new phase logging first); a mailbox `shutdownState` field (`Mimic::StopThread`
  memsets the struct mid-teardown, so the flag would zero itself).
- **Still to verify in-game:** disconnect the UI *first*, then untick — a `PipeServer: Stopped` line
  should now appear at all, within ~100 ms.
- **Where:** [`dll/src/Fern.cpp:450`](../dll/src/Fern.cpp:450), [`dll/src/Fern.h:51`](../dll/src/Fern.h:51)

### B2 — SymbolExport winner published as an AOB pattern

> **✅ FIXED — build 2581.** New `IsCeReplayableAob(AobResolve)` in `Himmel.h` is the single place
> that answers "can CE replay this winner's (pattern, pos, len) triple?" — true only for
> `RipDirect`/`RipDeref`/`RipBoth`. Both publish sites (`Genau.cpp:4727` GWorld, `:4349`
> `PublishGEngineMetadata`) gate on it, so a symbol or call-follow winner now publishes nothing and
> the UI's existing "empty aob ⇒ toggle greys out" contract takes over. `CallFollow` is excluded
> alongside the two symbol forms, as the finding required: its pattern IS a byte string, but the
> address comes from following the CALL and scanning the callee, which no fixed offset can express.
> `Test_Sig_IsCeReplayableAob` pins the classification **and** sweeps the four shipped pattern tables
> to assert every non-replayable entry really does carry `instrOffset/opcodeLen/totalLen == 0`
> (the structural reason the gate is needed), plus that the tables still contain symbol and
> call-follow entries at all — a gate nothing exercises is a gate that silently stops guarding.
> **Not verified in-game:** needs a modular build such as Satisfactory, where GWorld resolves through
> `?GWorld@@3VUWorldProxy@@A`.

**🟠** · S/low · Genau. Both `Genau.cpp:4720-4723` (GWorld) and `:4347-4350` (`PublishGEngineMetadata`)
copy `ws->pattern` with no check on `ws->resolve`. `SIG_EXPORT` (`Himmel.h:1462`) stores an MSVC mangled
name there with `instrOffset/opcodeLen/totalLen = 0`.
**Failure:** Satisfactory v1.2.3.1 (GWorld via `?GWorld@@3VUWorldProxy@@A`). `IsAobSymbolAvailable` keys
on non-empty, so the checkbox enables — and auto-checks for anyone with the persisted preference. The
emitted script runs `AOBScanModuleUE(process, '?GWorld@@3VUWorldProxy@@A')`, never registers the symbol,
and every address in the exported table resolves to `??`. Same for `StandaloneTrainerScriptGenerator.cs:79`
and the &GEngine CE-symbol push.
**Fix:** gate both assignments on `resolve ∈ {RipDirect, RipDeref, RipBoth}` — `CallFollow`/
`SymbolCallFollow` also carry 0/0/0 and must be excluded. Reuses the existing "empty aob ⇒ toggle greys
out" contract.
**Where:** [`dll/src/Genau.cpp:4720`](../dll/src/Genau.cpp:4720), [`dll/src/Genau.cpp:4347`](../dll/src/Genau.cpp:4347)

### B3 — CE XML `<Description>` never escaped

> **✅ FIXED — build 2581.** All **eight** `<Description>` emissions now route through
> `EscapeXmlContent`, not just the four the finding named: escaping an already-safe string is a
> no-op, so "every Description is escaped" is a cheaper invariant to hold than a list of the risky
> ones. New `CeXmlEscapingTests` **parses the output with `XDocument`** rather than string-matching
> for `&amp;` — asserting on the entity would pass for output still malformed elsewhere. Five cases:
> the minimal `R&D` reproducer, the kitchen-sink string, a round-trip check that the text is
> *recoverable* and not merely encoded-away, `<` opening a phantom element, and the hierarchical
> emitter. **Verified by negative control:** removing the escaping again fails all five.

**🟠** · S/low · CeXmlExportService. Raw interpolation at `:3527` (also `:3567`, `:3591`, `:3641`) while
`EscapeXmlContent` (`:3729`) is called at only two sites, both `<DropDownListLink>`. The text is arbitrary
game memory: map keys, set elements, soft-path strings, DataTable row names.
**Failure:** a `TMap` key `"Bow & Arrow"` → invalid entity reference → CE rejects the whole document → a
multi-thousand-entry export imports as nothing, with no indication which record. `CheatTableBuilder.cs`
does escape; this file is the outlier. No test parses the output as XML.
**Fix:** route `description` through `EscapeXmlContent` inside the four emitters (covers ~40 `DecorateDesc`
callers untouched); add a golden test that `XDocument.Parse`s output containing `& < >`.
**Where:** [`ui/UE5DumpUI/Services/CeXmlExportService.cs:3527`](../ui/UE5DumpUI/Services/CeXmlExportService.cs:3527)

### B4 — Mailbox thread misses `Tot::MarkBackgroundWorker()`

> **✅ FIXED — build 2592, shipped with B5.** One `thread_local` was answering two different
> questions, so the mailbox poller could not have (a) *ignore a pipe client's disconnect* without
> also getting (b) *refuse the off-game-thread invoke fallback*. Split into `Tot::t_cancelImmune`
> (+ `MarkCancelImmune()`), read by `Requested()`; `MarkBackgroundWorker()` now sets **both**, so
> every existing worker call site keeps its exact behaviour. Mimic's poller marks itself
> cancel-immune only. A cold WARN — `cmd=%d runs while a pipe client's per-command cancel is
> latched` — fires once per latch so the state is provable from a log instead of on trust.
> 9 EXPECTs across three roles (unmarked / poller / worker); the poller block's
> *"is NOT a background worker"* assertion is the negative control for the tempting one-liner.
> Reverting `Requested()` to the old flag was confirmed to fail the test. 938 C++ green.
> *Delete this row after the batch is merged to main.*

**🟠** · M/med · Mimic. Verified absent at `Mimic.cpp:194`; the six markers live in Dunste/Hemmung/
Laufen/Schlacht/Solide/Solitar.
**Failure:** UI killed mid-scan → the disconnect monitor latches `g_perCommand` (`Fern.cpp:552`), cleared
only by `Fern::Start`/`AcceptLoop` firstConn. In a CE-only session it never clears, so
`Aura::FindInstancesByClass` breaks at `n==0` and every mailbox object lookup returns empty —
`CMD_FIND_INSTANCE`, `CMD_LIST_INSTANCES`, `CMD_INVOKE_BY_NAME`, **plus the class-scan fallback resolvers**
in Wirbel/Solitar/Laufen/Hemmung, so teleport/GodMode/speed hotkeys die too on games where the fallback is
the working path. The message reads `scanned=<full pool>`, making it more misleading.
**Fix — not the one-liner:** `t_backgroundWorker` is also read by `Tot::IsBackgroundWorker()` at
`Frieren.cpp:1579` to refuse (-8) the off-game-thread invoke fallback, a policy deliberately scoped to
*repeating* workers. Blanket-marking the poller would start refusing user one-shot CE invokes. Use a
separate per-command-cancel-immunity flag, or mark only around the resolve calls.
**Where:** [`dll/src/Mimic.cpp:194`](../dll/src/Mimic.cpp:194)

### B5 — `UE5_Init` latch set after the whole scan

> **✅ FIXED — build 2592, shipped with B4.** `s_initialized` is now `std::atomic<bool>` (the
> unlocked fast path was itself a data race) behind a dedicated `s_initMutex` around the body, with
> the latch re-tested **under** the lock — a second caller that waited must return the first
> caller's result, never re-scan. `try_to_lock` first so the wait itself logs (`init already in
> progress on another thread — tid=… is waiting`), which is the only externally observable proof
> the interleave was reachable.
>
> **One thing the finding did not cover, found while fixing it:** a CE Disable landing mid-scan
> clears the latch and tears the server down, but the scan thread would then set the latch to
> `true` on its way out — every cancellable loop having bailed early on `Tot::Requested()`. The next
> enable would short-circuit `UE5_Init` and run the whole session on those partial results. So the
> latch is now refused when `Tot::ShutdownRequested()` is set at that point (safe against a false
> positive: `UE5_AutoStart` calls `ResetShutdown()` before `UE5_Init`). `UE5_Shutdown` deliberately
> does **not** take `s_initMutex` — that would make a Disable block for the rest of the scan and
> re-create the wedged-teardown shape B49 just fixed; `RequestShutdown()` is the interlock instead.
> *Delete this row after the batch is merged to main.*

**🟠** · M/med · Frieren. `Frieren.cpp:490` (latch) vs `:112-115` (guard) — a plain `static bool`, no
mutex, multi-second body.
**Failure (proxy mode is the designed-in case):** `Heiter.cpp:81-90` starts the pipe without scanning, so
both cached pointers are 0 while the pipe is live. UI Scan → `Fern::RunScan` → `UE5_Init`; a CE hotkey
during it → `Mimic::EnsureInitialized` enters a **second full init**. Both write `DynOff::*`, Aura's array
descriptor, Serie's pool state, and `FindGEngineSlot` wholesale-resets `s_gengineReport`. Probes read back
what earlier probes latched, so an interleave can latch a mix — the documented "total but silent" failure
(`Grimoire.h:132-140`): every property types unknown, log still prints `validated=yes`.
**Fix:** a dedicated mutex for the body + an in-progress flag so a second caller waits and returns the
first result.
**Where:** [`dll/src/Frieren.cpp:490`](../dll/src/Frieren.cpp:490)

### B6 — Coord library Clear-all: no confirm, no pre-clear backup

> **✅ FIXED — build 2560, shipped with B27 in one commit** (the ordering constraint below was the
> reason). New `CoordinateLibraryStore.SavePreClearBackup` writes a `.preclear.bak` — distinct from
> both the rolling `.bak` and `.preimport.bak`, so a clear cannot eat an import's rollback copy or
> vice versa — written *before* anything is dropped, plus a `ConfirmDialog` and a status line naming
> the backup file. The `str.Tip.TP.LibClear` tooltip said *"There is no undo."*; it now says what is
> true, because leaving it would have been a fresh instance of the 4a root cause. 4 store tests
> (survives the delete it guards · distinct from the pre-import copy · outlives the saves that eat
> the rolling `.bak` · empty when there is no file). 3117 green.
> *Delete this row after the batch is merged to main.*

**🟠** · S/low · TeleportViewModel. Delete/Duplicate carry `IsEnabled="{Binding HasSelectedCoord}"`;
"Clear all" has no gate, so with no row selected it is the only live button of the three and sits next to
Delete. `ClearCoordLibrary` calls `_coordStore.Delete` with **no `SavePreImportBackup`** — unlike
`ApplyCoordImport`, which does and names the file.
**Failure:** one misclick on a 4000-entry library. The `.bak` survives exactly one further save
(`CoordinateLibraryStore.cs:198` returns early with no main file, `:199` then overwrites) — and
`OnCoordZToleranceChanged` persists on every spinner nudge, so two clicks of a NumericUpDown destroy the
last copy. The status says only "Cleared N entries."
**Fix:** `SavePreImportBackup` (or `.preclear.bak`) before `Delete`, surface the filename, and add the
`ConfirmDialog.ShowAsync` already used for the analogous wipe at `PointerPanelViewModel.cs:1056`. **The
backup is the load-bearing half.** See B27 — these two ship together.
**Where:** [`ui/UE5DumpUI/ViewModels/TeleportViewModel.cs:3368`](../ui/UE5DumpUI/ViewModels/TeleportViewModel.cs:3368),
[`ui/UE5DumpUI/Views/TeleportPanel.axaml:547`](../ui/UE5DumpUI/Views/TeleportPanel.axaml:547)

### B7 — Uid: the one text field that skips every ingress guard
**🟠** · S/low · *merge of coord-lib-1 + coord-lib-2.* Sites: `TeleportViewModel.cs:3604` and `:3616`
(re-mint only when empty) · `:3353` (`RemoveAll` by uid) · `CoordCsvCodec.cs:261` · `CoordLuaParser.cs:150`
(no `CoordText.Normalize`, no length cap).
**Failure:** duplicate a row in Excel, rename it, import (merge) → `FindMatch`'s `taken` set means row 2
gets no uid match, and a new label means no identity match either → committed as Added with the duplicate
uid. Delete one row → `RemoveAll` wipes both, persisted immediately, status names one. Selection-by-uid
can also bind the wrong row.
**Fix (one pass):** re-mint at commit when `_coordAll` already holds the uid; make `DeleteCoord` remove by
reference (`_coordAll.Remove(row.Entry)`) — identical when uids are unique, so zero behaviour change in
the intended state; route uid through `CoordText.Normalize` + a new `MaxUidLength` at both parsers.
**Where:** [`ui/UE5DumpUI/ViewModels/TeleportViewModel.cs:3616`](../ui/UE5DumpUI/ViewModels/TeleportViewModel.cs:3616)

### B8 — Fly/noclip collision state committed independently of the invoke

> **✅ FIXED — build 2596.** Three separate defects had to move together, because each one
> hid the next.
> 1. **The commit is now the invoke's.** `FlyTickLocked` no longer writes
>    `collisionOff/collisionPawn`; the worker does, after the call. The re-emit condition reads that
>    same record, so an un-committed state re-emits on the next tick — the retry the optimistic
>    commit had silently removed. The one path that deliberately does NOT commit is the deferred one
>    (game thread unresponsive), which is exactly the one that must retry. A missing
>    `SetActorEnableCollision` DOES commit: retrying cannot conjure a setter, and
>    `InvokeSetCollision` already says so once.
> 2. **Join before deciding.** `active` is cleared, the worker is joined, and only then is the
>    restore decided — the old order let an in-flight tick turn collision back off *after* the
>    restore. Same shape as Schlacht's M1.
> 3. **Keep the record, defer the restore.** Wiping it is what made the pawn fall through the world.
>    Now a new `PendingRestoreLoop` (Schlacht's shipped precedent) polls for the game thread and
>    restores the instant the user clicks back into the game.
>
> One thing the finding did not say, and it inverts the common case: on an idle-when-unfocused
> title the *click that turns Fly off is in our own window*, which is what backgrounds the game and
> stops ProcessEvent. `IsGameThreadResponsive` is therefore false at precisely the moment the
> restore is needed — the skip is the normal path, not the edge.
>
> Also restores on the pawn the collision was **actually disabled on**, not a freshly re-resolved
> one: after a respawn those differ, and re-enabling collision on the new pawn leaves the original
> permanently ghosted.
> *Delete this row after the batch is merged to main.*

**🟠** · M/med · Dunste. *Merge of DWP-1 + DWP-2.* `Dunste.cpp:453-454` (optimistic commit) · `:577-582`
(wipe-then-skip). The collision record is written whether or not `InvokeSetCollision` ran or succeeded, in
both directions; the re-emit condition reads the already-updated state, so nothing retries.
**Failure:** on an idle-when-unfocused title, Fly ON + Noclip, fly through a wall, alt-tab to the UI
(>500 ms, PE quiet), click Disable → MovementMode restored, velocity zeroed,
`SetActorEnableCollision(true)` **skipped**, record wiped. The pawn falls through the world. Re-enabling
Fly *without* Noclip does not restore it. Prerequisite: one foreground-time emit must have landed.
**Fix:** set `collisionOff/collisionPawn` from the invoke's actual result; when the invoke is skipped,
**keep** the record and start the Schlacht-style deferred restore (`Schlacht.cpp:635-658` +
`PendingRestoreLoop` is the shipped precedent — audit #3 M1/M2 was Schlacht-only). Join the worker before
snapshotting.
**Explicitly out of scope:** the `Fern.cpp:779` disconnect half — "holds persist across disconnect" is the
deliberate family policy that sank audit #3's M6.
**Where:** [`dll/src/Dunste.cpp:453`](../dll/src/Dunste.cpp:453), [`dll/src/Dunste.cpp:577`](../dll/src/Dunste.cpp:577)

### B9 — Competing-dumper-host warning: never wired to connect, never cleared
**🟠** · S/low · MainWindowViewModel. *Merge of livewalker-nav-1 + -2.* `CheckForCompetingDumperHostsAsync`
has exactly one call site, inside the `Pointers.RescanApplied` lambda — raised only by the UE-override
apply and Extra Scan. `ConnectAsync` and proxy `TriggerScanAsync` both funnel through `ApplyEngineState`,
where every other post-connect action lives, and it isn't called there.
**Failure:** two proxied games running; Connect lands on whichever pipe server is free; the tree fills with
the wrong game's data; the banner whose own comment says *"this can mean you are looking at the WRONG
GAME'S data, which nothing else on screen would reveal"* never appears. Then it never clears on disconnect
and pins a dead PID for the rest of the session.
**Fix — do NOT apply the wording literally.** `RescanApplied` is a *duplicated hand-rolled fan-out*
(`:665-704`) that never calls `ApplyEngineState`; **moving** the call deletes the one path that works.
**Add** `_ = CheckForCompetingDumperHostsAsync(state);` at `:2611` and **keep** `:694` (the check is
idempotent). Add `MultipleDumperHostsWarning = "";` at `:1997`, and capture `int epoch = _sessionEpoch;`
at entry, publishing only on match (same shape as `ShouldConfirmProxy`).
**Where:** [`ui/UE5DumpUI/ViewModels/MainWindowViewModel.cs:2611`](../ui/UE5DumpUI/ViewModels/MainWindowViewModel.cs:2611),
[`ui/UE5DumpUI/ViewModels/MainWindowViewModel.cs:1997`](../ui/UE5DumpUI/ViewModels/MainWindowViewModel.cs:1997)

### B10 — `Ubel::WalkClassEx` has no memo; four call sites claim otherwise

> **✅ FIXED — build 2596.** `s_walkClassExCache` added; `WalkClassEx` now returns
> `const ClassInfo&` and 23 call sites bind by reference instead of deep-copying. The four
> `// cached` comments now say `memoized (B10)`.
>
> **The prerequisite was done first and it was not optional.** `Ubel.cpp:813` was
> `s_walkClassCache[addr] = info;` — an assign-over-existing that destroys the entry's `Fields`
> vector. The moment a reference is handed out, that is a use-after-free for any thread still
> reading it. Both caches now `try_emplace`: first writer wins, the two results are equal anyway
> (the walk and the enrichment are pure functions of the same reads), and an existing entry is never
> touched. Node-based map, no `erase`/`clear` anywhere in `dll/src` ⇒ entries never move.
>
> Verified no caller mutates the result before switching the return type — a regex sweep for
> assignment/`push_back`/`clear`/`erase` on `ci.` / `eci.` / `rowCI.` came back empty, and the
> compiler then enforced it for good.
> *Delete this row after the batch is merged to main.*

**🟠** · M/med · Ubel. `Ubel.cpp:885-1023` has no lookup and no store; `:713-717` deep-copies on a cache
hit **inside** the lock. Per-object reach is wider than first filed: snapshot capture hits it at
`Aura.cpp:8583`, `:7605`, `:7564` (per struct-array element) and `:8420`; group scan at `:7698` **and
recursively** (`:7726`, `:7809`) plus `:8003`. `WalkClass`'s `Fields` is the flattened inheritance chain,
so an Actor subclass carries 100–300 `FieldInfo` × 14 `std::string` — copied per call, under the one mutex
every `ParallelGObjectsScan` worker contends on.
**Fix — with a prerequisite:** add `s_walkClassExCache` and return `const ClassInfo&`. The store at
`Ubel.cpp:813` is `s_walkClassCache[addr] = info;` — an **assign-over-existing**; two threads racing the
same uncached class would reallocate a vector already handed out by reference (use-after-free).
**Change it to `try_emplace` first.** Reference-return is otherwise safe: node-based map, zero
`erase`/`clear` in `dll/src`. Fixing the four `// cached` comments is a zero-risk standalone step.
**Where:** [`dll/src/Ubel.cpp:885`](../dll/src/Ubel.cpp:885), [`dll/src/Ubel.cpp:813`](../dll/src/Ubel.cpp:813)

### B28 — UTF-8-first gate returns mojibake for ordinary CJK

> **✅ FIXED — build 2599.** The audit was right that ratio scoring cannot do this. The
> discriminator that can is **strict UTF-8 well-formedness** (new `IsWellFormedUtf8`): the first
> `n` bytes of a UTF-16 CJK buffer contain a **lone continuation byte** — `中文一二`'s prefix is
> `2D 4E 87 65`, and `0x87` is a continuation byte with no lead. That is not a "how much of this
> looks like text" question, it is "this cannot be UTF-8". `Sanitize` had been turning it into
> `-N?e` — one bad character in four, comfortably inside any ratio.
>
> Well-formedness alone does not settle `第1章` → `,{1` or `中A文` → `-NA`: both prefixes are clean
> ASCII with zero replacements. Those needed the structural point the finding made, applied as a
> rule: **the two pieces of evidence are not equally strong.** The UTF-8 evidence (`buf[n-1]==0`)
> sits *inside* a UTF-16 string's own payload (`n-1 < 2n-2` for every `n>1`), so UTF-16 text
> produces it routinely. The UTF-16 evidence (a zero unit at byte `2n-2`) sits *outside* an n-byte
> UTF-8 string's payload, in unrelated heap, and can only be satisfied by chance. So both
> hypotheses are now evaluated before either is returned, and the stronger evidence wins.
>
> **The regression the audit predicted is closed by rule 2, not hoped away:** a well-formed UTF-8
> buffer containing a *multi-byte sequence* is decided as UTF-8 before the UTF-16 hypothesis is even
> considered. A UTF-16 prefix essentially cannot produce a valid multi-byte sequence, and the real
> shipped case (STVoyager's FText, 11 three-byte CJK characters) is exactly that. There is a test
> that hands it a zero heap tail at `[2n-2]` on purpose and asserts UTF-8 still wins.
>
> **Residual, stated rather than hidden:** an *ASCII-only* UTF-8 buffer whose heap tail is `00 00`
> at exactly `[2n-2]` **and** whose full 2n-byte reading still passes `LooksLikeDecodedText` would
> be read as UTF-16. Both must hold, and FUtf8String FText exists precisely for non-ASCII, so no
> ASCII-only case is known in the wild. Documented in the function header.
>
> 13 new EXPECTs. Negative control run: restoring "return the first hypothesis that passes" fails
> exactly the three CJK cases and nothing else. 81 utf8 + 938 dll green.
> *Delete this row after the batch is merged to main.*

**🟠** · M/med · Utf8Helpers. The UTF-8 hypothesis accepts on `buf[n-1]==0` plus an interior-null scan
that stops at `i+1 < n` — the terminator byte is deliberately excluded. The docstring's justification
reasons only about UTF-16 **high** bytes; **low** bytes are `0x00` for every U+xx00 codepoint.
**Failure:** `"中文一二"` (Num=5) → `2D 4E 87 65 00 4E 8C 4E 00 00`. `buf[4]=0x00` is the low byte of 一
(U+4E00). `Sanitize` yields `-N?e`; `LooksLikeDecodedText` tolerates up to ⅓ replacements (`3 < 4`) and
accepts; `:247` returns and the correct UTF-16 branch at `:251-267` is never reached. The second lens
found **shorter, more ordinary** triggers: `統一`/`第一`/`唯一`/`萬一` (any even-length CJK string with a
U+xx00 middle char), and `第1章` → `,{1` with **zero** replacement characters. Untested —
`utf8_helpers_test.cpp:371-401` covers only cases whose terminator byte is non-zero.
**Scope:** FText-typed values only (`ReadFTextString` → `Ubel.cpp:333`); FString goes through the
UTF-16-only reader and is unaffected.
**Fix:** do not return on the first hypothesis that merely passes. **Caveat that changes the fix:** scoring
both candidates by replacement ratio does *not* fix the `第1章`/`中A文` variants (both score 0 bad) and can
regress the STVoyager UTF-8 case whenever the adjacent heap tail happens to be zero at bytes
`[2n-2]/[2n-1]`. The discriminator must be **structural** — e.g. an interior-UTF-16-null check, preferring
a clean UTF-16 decode whenever one exists. Add the `"中文一二"` and `"第1章"` buffers as regression tests.
**Where:** [`dll/src/Utf8Helpers.h:239`](../dll/src/Utf8Helpers.h:239)

### B29 — CE-plugin "already loaded" decided by filename alone

> **✅ FIXED — build 2577.** One rewrite of `IsAlreadyLoadedInTarget`, covering both defects as the
> finding predicted. The file name is now only a cheap PRE-FILTER for the module walk; ownership is
> decided by **PE ProductName == "UE5CEDumper"**, read with `GetFileVersionInfoW`/`VerQueryValueW`
> over whichever language block the file actually has (no fixed `040904B0` guess). That is
> deliberately the SAME rule the C# side already uses (`DumperModuleDetector`), so the two detectors
> cannot disagree. The whole walk went wide — `GetModuleFileNameExW` + `wcsrchr`/`_wcsicmp`, with
> `Utf8Helpers::EncodeUtf16` only at the log/message boundary — which kills the `EVERSPACE? 2`
> mojibake. A same-named module that is not ours now logs a line naming it, since that is exactly the
> case that used to be silently misread. The System32 path test is **gone**, not kept alongside: a
> genuine Windows DLL fails the ProductName test anyway, so the special case was a second rule that
> could only drift from the first.
>
> **Rule verified on real files** (not just reasoned): all four shipped proxies + `UE5Dumper.dll`
> report `ProductName = UE5CEDumper`; all four System32 counterparts report
> `Microsoft® Windows® Operating System`. That also demonstrates the old defect directly — the old
> test was "path is not under System32", so a copy of `System32\dxgi.dll` placed in a game folder
> (which is what some passthrough wrappers ship) would have been claimed as ours.
>
> **Still to verify in-game:** no ReShade/ASI-Loader install exists on this machine to use as the
> positive negative-control, and this code path only runs inside Cheat Engine as a plugin.
> *Delete this row after the batch is merged to main.*

**🟠** · S/low · Methode. *Merge of ce-art-03 + dll-left-6 — both defects live in the same 40-line
function, `IsAlreadyLoadedInTarget`; one rewrite fixes both.*
**Defect (primary):** the function decides "UE5CEDumper is already present" from a module's **file name**
(`kProxyDllNames` = version/dinput8/dxgi/winmm) plus a not-under-System32 path test. `OnInjectAndConnect`
calls it at `:212` with no identity probe of any kind.
**Failure:** any UE game with ReShade installed has ReShade's `dxgi.dll` next to the EXE — a configuration
this repo documents three times (`docs/roadmap.md:15`, `DEPLOY_README.html:102/:188`). The user clicks
"UE5CEDumper: Inject && Connect"; the walk matches on name, the System32 prefix test passes, and the menu
returns at `:223` with *"UE5CEDumper is already loaded in this process as 'dxgi.dll' … No injection
needed"*. Nothing is injected, the pipe never exists, the UI's Connect fails with no diagnostic. No
override in the menu path. Same for Ultimate ASI Loader's `version.dll`.
**Defect (secondary, same function):** `:162` uses `GetModuleFileNameExA`, which replaces every character
the ANSI code page cannot represent with `?` — so `D:\Games\EVERSPACE™ 2\…` is displayed and logged as
`EVERSPACE? 2`, an unpasteable path. The sibling fixed exactly this in the same window
(`Heiter.cpp:177-185`, wide read + `Utf8Helpers::EncodeUtf16`). *Refuted sub-claim:* the `strrchr` DBCS
hazard cannot fire — `strrchr` returns the **last** `0x5C`, and a trail-byte `0x5C` only occurs inside a
directory component.
**Fix:** one rewrite covers both — `GetModuleFileNameExW` into a `wchar_t[MAX_PATH]`, wide comparisons,
`EncodeUtf16` only at the log/ShowMessage boundary, and after a name match **confirm identity** before
claiming ownership. Two siblings already do it correctly: `UE5CEDumper.CT:172-175` gates the whole walk on
`pcall(getAddress,'UE5_Init')`, and `DumperModuleDetector.cs:12-15,57` requires PE ProductName ==
`UE5CEDumper`. A false negative is already safe: `UE5_StartPipeServer` detects an existing pipe and
returns INIT_SKIPPED.
**Note:** the defect pre-existed for `version.dll`; recent commits (`ed4e337`, `edafcc9`) widened the blast
radius by adding dxgi/dinput8/winmm.
**Where:** [`dll/src/Methode.cpp:176`](../dll/src/Methode.cpp:176), [`dll/src/Methode.cpp:162`](../dll/src/Methode.cpp:162)

### B30 — `.CT` bail-outs leave the record ticked; untick tears down a foreign proxy
**🟠** · S/low · `scripts/UE5CEDumper.CT`. The child record is exactly `[ENABLE] ue5_inject()` /
`[DISABLE] ue5_shutdown()`. Five bail-outs inside `ue5_inject` (`:235` no-process, `:252` already-loaded,
`:262` injectDLL-failed, `:338` INIT_FAILED, `:345` timeout) end the chunk **without error and without
touching `memrec`** — grep for `memrec` over the whole `.CT` returns zero hits — so CE marks the record
active.
**Failure:** the user has any proxy deployed and the game running with the pipe up. They tick the .CT's
inject box; `ue5_isAlreadyLoaded()` returns true, the script says "No injection needed" and returns at
`:252` — but CE ticks the box. They untick it (it told them nothing was needed). `[DISABLE]` →
`ue5_shutdown()` → `getAddress` resolves against the **proxy's** DLL (all four `.def` files export both
symbols) → `UE5_Shutdown` runs the full teardown (`Frieren.cpp:542-567`). `Mimic::StartThread` has exactly
one caller (`Heiter.cpp:194`, DllMain), and re-ticking re-enters the same bail-out — **no in-.CT recovery;
only a game restart.**
**Fix:** set `memrec.Active = false` before every `return` in `ue5_inject`, and make `ue5_shutdown` a quiet
no-op when nothing was injected (probe `pcall(getAddress,'UE5_StopPipeServer')`). **The fix is broader
than the .CT:** `CeInjectScriptGenerator.cs:85-90` (the pushed-record already-loaded bail-out) *also*
returns without `memrec.Active = false`, and in that state its DISABLE probe **does** resolve — so the
pushed route has the identical destructive path and must be fixed too.
**Correction:** the `:82` DLL-not-found return is **not** one of the feeding bail-outs — it aborts the
parent chunk, so the child would raise `attempt to call a nil value` and never activate. Five bail-outs,
not six; only `:252` is destructive-against-a-foreign-DLL.
**Where:** [`scripts/UE5CEDumper.CT:398`](../scripts/UE5CEDumper.CT:398)

### B31 — UI log sink silently stops writing at 8 MB instead of rotating
**🟠** · S/low · LoggingService. *Confirmed by measurement, not inference.* The only `WriteTo.File` call
site in the UI passes `fileSizeLimitBytes: Constants.LogMaxSizeBytes` (8 MiB) with `rollOnFileSizeLimit`
left at Serilog's default **false** and `rollingInterval` at **Infinite**. There is no roll point: the
sink's `Emit` short-circuits once the limit is reached and drops every later event for the process
lifetime. No `SelfLog` is configured (zero hits repo-wide), so the drop is silent.
**Failure — two reachable sequences.** (1) *Continuous poll, no user action:* Teleport → Auto refresh
starts a 500 ms `DispatcherTimer` (`TeleportViewModel.cs:746-782`) issuing `teleport_get_pose` twice a
second, each producing a `Pipe TX`/`Pipe RX` Debug pair (`PipeClient.cs:172/249`) — a few MB/hour, so the
pipe category dies mid-session and the disconnect warning and ReadLoop errors that follow are never
written. (2) *The documented measurement procedure:* `UE5DUMP_PIPE_LOG_FULL=1` uncaps bodies, 8 MiB falls
within a handful of batched responses, and `scripts/analysis/walk_payload_audit.py:49-53` *tells the
reader* "Log rotation (4 × 8 MiB) then keeps the LAST ~32 MiB … an unbiased one" — reality is the
opposite: nothing rotates, the log freezes at the **first** 8 MiB, and the script computes per-key byte
shares from the export's opening prefix, reintroducing exactly the bias the flag exists to remove.
**Contract:** `docs/architecture.md:274` and the CLAUDE.md log MUST-rule both state the 8 MB cap archives
mid-session, and the DLL half genuinely does it (`Sein.cpp:260-280`). This is an omission, not a design
choice.
**Fix:** pass `rollOnFileSizeLimit: true` — **and `retainedFileCountLimit: null`**. Serilog defaults that
to 31 when rolling is enabled, and a *count* limit is precisely the policy this project deliberately
rejected in favour of age-based retention; leaving it defaulted reinstates generation-count eviction by
the back door. Rolled files are named `pipe-0_001.log`, which still matches `PruneAgedLogs`' `{prefix}-*.log`
glob and does not end in `-0.log`, so the live-file guard already handles them. Fix the false claim in
`walk_payload_audit.py:51` in the same change.
**Scope corrections carried forward:** the class docstring at `:18-23` does *not* itself promise
mid-session rotation (it says "5MB cap", stale against the 8 MiB constant); and the "mirror stops early"
note in the log-verification checklist is a **different** phenomenon that must not be cited as field
evidence here.
**Where:** [`ui/UE5DumpUI/Services/LoggingService.cs:264`](../ui/UE5DumpUI/Services/LoggingService.cs:264)

---

## 🟡 LOW

**B11 — `WriteToFile` fprintf's a NULL `FILE*`.** `Sein.cpp:316-321`; `RotateIfNeeded` `:260-280` sets
`file = nullptr` (265), guards its own banner (273), returns void. 8 MB rotation + a failing truncating
reopen (disk full — 21-day retention has no size cap — or a viewer holding the file) → UCRT's
invalid-parameter handler **terminates the injected game** with no message.
*Fix:* `RotateIfNeeded(fs_state); if (!fs_state.file) return;`. Optionally latch a disabled flag. S/low.
[`dll/src/Sein.cpp:318`](../dll/src/Sein.cpp:318)

**B12 — Orphan cleanup tells the user things the executed plan contradicts.** *Merge of proxy-orphan-2 +
-3.* `ProxyOrphanScanner.cs:444` — the already-gone branch precedes and discards `dirsRemoved`, yet
`ProxyDeployService.cs:1582` counts vanished files into `allFilesGone`, so up to four directories are
pruned while the row reads "Already gone — nothing left to remove." in success green.
`OrphanCleanupConfirmDialog.cs:143` prints "Outside a Steam library" for every `ChainDirs.Count == 0` row,
but `PlanPrune` returns `FileOnly` for six reasons and only one is that. The most common (`!prunableLeaf`
— a ReShade `dxgi.dll` beside our `version.dll`) happens *inside* a library, and its correct blocker is
printed two lines below. The dry-run report gets it right.
*Fix:* pass `dirsRemoved` into the already-gone branch (or move it below the dir branches); render the
`FileOnly` reason from `Blockers`. Regression test with `filesRecycled=0, filesAlreadyGone=1,
dirsRemoved=4`. S/low. [`ui/UE5DumpUI/Services/ProxyOrphanScanner.cs:444`](../ui/UE5DumpUI/Services/ProxyOrphanScanner.cs:444)

**B13 / B41 — "Moved to the Recycle Bin" is a promise the guard can't keep.** *Both passes found this
independently.* `FOF_ALLOWUNDO` is best-effort; with `FOF_NOCONFIRMATION` a volume whose recycler is
disabled (`NukeOnDelete=1`) hard-deletes and returns 0. The only precondition is a `Path.GetPathRoot`
**drive-letter** test, not a volume/recycler test, and `OrphanVerdict.NotOnFixedDrive`
(`Models/OrphanScanTypes.cs:65`) has **no producer** — the question is first asked inside the delete call.
Scoped honestly: the deleted item is our own proxy DLL, re-verified by `HasExportQuorum` immediately
before deletion, so it is redeployable; folder pruning is unaffected; the relative-path limb is
unreachable (the sole caller passes fully-qualified paths). An honest-messaging gap in a non-default
configuration.
*Fix:* `SHQueryRecycleBin` on the volume root from `GetVolumePathName`, or `IFileOperation` with
`FOFX_RECYCLEONDELETE | FOFX_EARLYFAILURE`; surface the refusal as `NotOnFixedDrive` at scan time so the
dialog never makes the promise. M/low.
[`ui/UE5DumpUI/Services/WindowsPlatformService.cs:736`](../ui/UE5DumpUI/Services/WindowsPlatformService.cs:736)

**B14 — Thread-proc exception guard at 2 of 7 sites.** ✅ **FIXED build 2596, with R5.** New
`Routine.h` (Frieren roster: ルティーネ, *"scheduled / periodic subroutine"*) holds `ReassertLoop`
(cancel-immunity + sliced sleep + guarded tick + a `ShutdownRequested` break the hand-copied loops
never had), `RunTickGuarded`, and `SleepSliced`. The four hold workers are now their tick and
nothing else; both `PendingRestoreLoop`s are wrapped. `Grimoire::WORKER_SLEEP_SLICE_MS` replaces the
8 bare `25`s. The per-module drift WARN wording is deliberately NOT templated — those strings are
individually worded and the log checklist greps them. *Original finding:* Guarded: `Schlacht.cpp:468`, `Dunste.cpp:480`.
Unguarded: `Schlacht.cpp:507` (`PendingRestoreLoop`, which calls the same `InvokeSetHidden` path),
`Solide.cpp:330`, `Hemmung.cpp:284`, `Laufen.cpp:358`, `Solitar.cpp:312`. Build 2389 added the guard for a
live-reproduced `0xC0000409` (bad_alloc escaping a thread entry). `Tot::ShutdownRequested()` is not set on
a plain game exit, so `PendingRestoreLoop` can still be walking reflection against a tearing-down process
for up to 5 minutes.
*Fix:* one `RunTickGuarded(fn, oneShotWarnFlag)` helper, wrap the five; give the four hold workers a
`ShutdownRequested` break. **Pairs with R5.** S/low. [`dll/src/Schlacht.cpp:507`](../dll/src/Schlacht.cpp:507)

**B15 — Teleport CE-Lua `break`s a mailbox timeout into the auto-close.** `TeleportScriptGenerator.cs:139`
and `:220`; closes at `:179`/`:231` (the latter has no `hadError` at all). CLAUDE.md: *"A timeout is an
error, so `return` (not `break`)"*. Ten of twelve generators comply; `CeLuaHygieneTests.cs:111` pins it —
for Movement only.
*Fix:* `then hadError = true; showMessage('[Teleport] mailbox timeout'); break end`; declare `hadError` in
`GenerateClearAll`. A bare `return` is **wrong** here — it would strand the record ticked. Promote the
`DoesNotContain("then break end")` assertion to a theory over every generator. S/low.
[`ui/UE5DumpUI/Services/TeleportScriptGenerator.cs:139`](../ui/UE5DumpUI/Services/TeleportScriptGenerator.cs:139)

**B16 — Coord grid: 5 dead sort columns under AOT.** `TeleportPanel.axaml:591-600` — X/Y/Z/Yaw sort on
`Entry.*` (nested, rooted by no column binding), Dist sorts on `Distance` against a `DistanceText`
binding; no `x:Name`, and `TeleportPanel.axaml.cs` makes no `WireSortComparers` call. Verbatim the trap
`Helpers/DataGridSortComparers.cs:10-27` documents. Label/Group/Map sort fine, so it reads as an
intermittent app bug.
*Fix:* `x:Name` + `CanUserSort` + reflection-free comparers, as PR #301 did for the other grids. S/low.
[`ui/UE5DumpUI/Views/TeleportPanel.axaml:592`](../ui/UE5DumpUI/Views/TeleportPanel.axaml:592)

**B17 — Pose not cleared on disconnect.** `TeleportViewModel.cs:686` (`ClearPovDisplay` only;
`ClearPoseDisplay` has one call site, `:826`) · `:661` (AutoRefresh forced false, never re-enabled) ·
`:3098` (the map filter reads a stale `PoseMap`). The next game's library loads and `ApplyCoordFilter`
drops every row: "Coordinate Library (0 of 340)". Self-heals on one manual Refresh pose. Same shape as
B9's missing clear — same disconnect branch, different VM.
*Fix:* call `ClearPoseDisplay()` at `:686`, and/or kick `RefreshPoseQuietAsync()` from `SetConnected(true)`.
S/low. [`ui/UE5DumpUI/ViewModels/TeleportViewModel.cs:686`](../ui/UE5DumpUI/ViewModels/TeleportViewModel.cs:686)

**B18 — Extra Scan is uncancellable under an unbounded join.** `Genau.cpp:3931`, `:4075` (and
`:517/555/693/714/1823/1999`) — zero `Tot::` references in the whole file vs 30 in `Aura.cpp`.
`Fern::Stop()` sets `Tot::RequestShutdown()` at `:447` *"so a long scan bails promptly"* then joins at
`:474-477` under a comment asserting `RunRescan` is a "bounded AOB scan" — it is not. `UE5_Shutdown` runs
on the CE Lua caller's thread, so CE's UI freezes for the remainder of the sweep.
*Fix:* `if ((n & 0xFFF) == 0 && Tot::Requested()) return 0;` in the address loops, matching Aura's idiom.
S/low. [`dll/src/Genau.cpp:3931`](../dll/src/Genau.cpp:3931)

**B19 — One shared `error_code` aborts the retention sweep permanently.** `Sein.cpp:238` (`if (ec) break;`
with `fs::remove` at 245) · `:343` (same, `remove_all` at 360/361; the only `ec.clear()` at 357 precedes
both). The first undeletable entry ends the sweep; enumeration order is stable, so the same entry
re-aborts it at the same point on **every launch** and the advertised 21-day retention silently stops
applying past it.
*Fix:* a fresh `error_code` per call; `continue`, don't `break`. S/low.
[`dll/src/Sein.cpp:238`](../dll/src/Sein.cpp:238)

**B20 — Coord editor: a keystroke reverts an uncommitted edit; filter memory leaked.**
`TeleportViewModel.cs:3113-3118` rebuilds `CoordRow`s and re-assigns `SelectedCoord` to a *new* reference,
so `OnSelectedCoordChanged` always fires and rewrites `EditCoordLabel/Group`. Reached per keystroke from
`:2991-2995`. `_coordFilterMemory` (`:79-80`) is never disposed in `Dispose()` (`:4501-4518`) though the
other five disposable VMs all dispose theirs.
*Fix:* a `_suppressCoordEditorSync` flag around the restore (same shape as `_suppressCoordPersist`); add
`_coordFilterMemory.Dispose();`. S/low.
[`ui/UE5DumpUI/ViewModels/TeleportViewModel.cs:3117`](../ui/UE5DumpUI/ViewModels/TeleportViewModel.cs:3117)

**B21 — Three coord-import parser holes (do in one sitting).**
· `CoordinateLibraryFile.cs:143` — `AllowThousands` makes `"67162,398"` parse as 67162398 with no issue
raised; the decimal-comma repair only fires for `;`-delimited files. **A genuine tradeoff, not a free
win:** removing the flag rejects Excel's `"67,162.398"`. Decide explicitly; document either way.
· `CoordCsvCodec.cs:349-353` vs `:396` — `SplitLines` flips quote state on *any* quote, `SplitCsvLine`
only at field start; one unpaired `"` merges every following record until EOF, dropped behind a single
diagnostic naming the start line. Needs an odd quote count and a hand-authored file; preview +
`.preimport.bak` blunt it.
· `CoordLuaParser.cs:233` — `RawNum`/`HasKey`/`ValueTextAfter` regex the whole entry with no literal
awareness, though `SkipString` is used two functions away; `label='Arena z=0 plane'` overrides the real Z.
Surfaces in the preview as a Changed row.
S–M/low. [`ui/UE5DumpUI/Models/CoordinateLibraryFile.cs:143`](../ui/UE5DumpUI/Models/CoordinateLibraryFile.cs:143)

**B22 — Movement knob captures a base of 0.** `Laufen.cpp:472-475`, `:293-296` (re-capture), target
`= base × multiplier` at `:478`/`:297`. A game holding `MaxWalkSpeed`/`GravityScale` at 0 (cutscene, swim,
mount) at Apply time makes the worker re-write 0 over the game's restored value every 250 ms while the
panel reads "300%, active".
*Fix:* refuse a non-positive/non-finite capture; keep the previous base on respawn re-capture. The same
guard belongs on `Hemmung.cpp:253-256/388-391`. S/low. [`dll/src/Laufen.cpp:472`](../dll/src/Laufen.cpp:472)

**B23 — Two CE-Lua emit defects.**
· `CeAutorunScriptGenerator.cs:67` — the preamble is emitted at **file scope**, so `local DEBUG` binds at
CE start-up and the file's own line-61 instruction ("Set UE5_DEBUG = 1 in the Lua console") can never
work. *Fix:* a late-bound `dbg` (`if (UE5_DEBUG or 0) ~= 0`) via a dedicated `CeLuaHygiene` overload.
· `BakedScriptGenerator.cs:495` — `double.TryParse` accepts `NaN`/`Infinity` and overflow, `ToString("R")`
emits the bare word, which is `nil` in Lua; `MarkUnparsed` can't fire because the parse "succeeded". Same
at `Views/FreezeValueDialog.cs:211-216`. Input is unvalidated (`InvokeParamDialog.cs:953`).
*Fix:* `if (!double.IsFinite(d)) return MarkUnparsed(t);` + reject in the freeze dialog. S/low.

**B24 — Forced hook installs burn the automatic retry budget.** `Frieren.cpp:1423` — `fetch_add` sits
after both `!force` guards; `UE5_EnsureGameThreadHook` consumes up to 2 per click. Corrected scope: only
two callers (`Fern.cpp:3230` Live Funcs, `Schlacht.cpp:586` see-through), and `force` still bypasses the
cap — so only the *automatic* retry at `:1512` goes quiet.
*Fix:* move the `fetch_add` inside `if (!force)`. S/low. [`dll/src/Frieren.cpp:1423`](../dll/src/Frieren.cpp:1423)

**B25 — Too-old gate armed off an uncorroborated PE ProductVersion.** `Genau.cpp:4618-4635` refuses all
scanning; the only sub-4.11 producer is `:2427-2430` (`major == 4 && minor <= 27` from `VS_FIXEDFILEINFO`,
zero corroboration) which `:2672-2673` classifies tier 1, so `bLowConfidence` is false and the `:4583`
softener is scoped to `>= MIN_SUPPORTED`. Every other version signal in the file demands context.
Unobserved in the 35-game corpus, and the UI string documents the override remedy — a latent robustness
gap, not a dead end.
*Fix:* return the sub-411 PE result as tier 3 unless the memory Tier 1/2 scan agrees. S/**med** (touches
the deliberate refusal design — read the `:4593-4617` comment first). [`dll/src/Genau.cpp:4618`](../dll/src/Genau.cpp:4618)

**B26 — Duplicate GameEngine records share one global buffer marker.**
`PointerQueryScriptGenerator.cs:67, 146-147, 179-180, 207-208`; duplicates are trivially produced
(`TeleportViewModel.cs:1691-1730` pushes per click, `AobMakerBridgeService.cs:202` has no dedup).
Unticking the older record deAllocs the newer one's live buffer and unregisters `UE_GameEngine` while it
is still ticked. *Scope correction:* the symbol itself is a fixed global by design, so the same "chain
goes to `??`" happens on the GWorld record with no buffer at all — a per-record marker alone wouldn't cure
it.
*Fix:* verify `getAddressSafe(sym)` equals the buffer before deAlloc/unregister; consider deduping the
push by description. M/med.
[`ui/UE5DumpUI/Services/PointerQueryScriptGenerator.cs:208`](../ui/UE5DumpUI/Services/PointerQueryScriptGenerator.cs:208)

**B32 — The "very old DLL" fallback is unreachable, and misreports a healthy inject as a timeout.**
`UE5CEDumper.CT:339` — `mb == nil` was chosen as the "very old DLL" signal, but `g_invokeMailbox` has been
exported for many builds and offset `0x0C` used to be `int32_t reserved` that **nothing ever wrote**
(verified against `ed4e337^`). An old DLL therefore resolves the symbol, reads 0 == INIT_IDLE forever,
burns the full 25 s, and lands in the *timeout error* branch with a modal on a **perfectly healthy
injection**.
*Fix:* distinguish "never left IDLE for the whole window" (unknown) from "observed RUNNING then stalled"
(a real timeout). S/low. [`scripts/UE5CEDumper.CT:339`](../scripts/UE5CEDumper.CT:339)

**B33 — Readiness poll resolves only one symbol spelling.** `UE5CEDumper.CT:297` + `CeReadinessLua.cs:65`
— a single spelling where `lessons-learned.md:110` mandates both (`g_invokeMailbox` **and**
`UE5Dumper.g_invokeMailbox`), and **eight** other sites in the repo obey the rule. Degrades to the
pre-commit blind 15 s wait on affected setups — graceful, but it defeats the commit's own purpose.
*Fix:* mirror `findMailbox` (`getAddressSafe` on both spellings) in both files so they stay byte-identical.
S/low. [`scripts/UE5CEDumper.CT:297`](../scripts/UE5CEDumper.CT:297)

**B34 — CE-plugin detection is a 1 s race the first install always loses.** `Heiter.cpp:112` —
`g_isCEPlugin` is set *only* in `CEPlugin_InitializePlugin`, which CE calls only on **enable**; a
registered-but-unticked plugin gets DllMain + `GetVersion` only. The fixed `Sleep(1000)` cannot be beaten
by a human ticking a checkbox, so `UE5_AutoStart()` AOB-scans and opens `\\.\pipe\UE5DumpBfx` **inside
cheatengine-x86_64.exe**. The non-proxy branch has no process-name guard at all.
*Fix:* set the flag in `CEPlugin_GetVersion` (called for enabled and disabled alike) + add a process-name
guard to the non-proxy path. *Corrected:* the game proxy does **not** report INIT_FAILED
(`UE5_StartPipeServer` returns TRUE on an existing pipe) — it publishes INIT_READY and skips serving,
reaching the same broken end state by the by-design path. S/low.
[`dll/src/Heiter.cpp:112`](../dll/src/Heiter.cpp:112)

**B35 — The PERF split measures its own probe.** `DiagnosticsProbe.cs:99-100` — field initializers run at
construction, *after* the opening `get_diagnostics`; only the **closing** call is inside the window, yet
the code subtracts 2 calls and 0 ms. With a probe call measured at 93–125 ms in this repo's own figures
and a 57.7 ms "Copy CE Field" wall time, `transportMs > wallMs`, `uiMs` clamps to 0, and `ipcMs` absorbs
the probe. **These lines are the evidence `docs/multipipe-eval.md` reasons from.**
*Fix:* snapshot before the closing call, subtract 1, clamp. S/low.
[`ui/UE5DumpUI/Services/DiagnosticsProbe.cs:99`](../ui/UE5DumpUI/Services/DiagnosticsProbe.cs:99)

**B36 — All four Force actions render with nothing selected.** `PropertySearchPanel.axaml:160/164/168/172`
— no `FallbackValue`; `ApplyResultFilter` nulls `SelectedResult` on every search *and* every filter
change, and Avalonia's DataGrid updates selection on left-press only, so right-clicking an unselected row
or empty space shows Force ON + OFF + →null + value… together. Harmless when clicked (all three commands
early-return). The model already exposes an unused `CanForceAny`. S/low.
[`ui/UE5DumpUI/Views/PropertySearchPanel.axaml:160`](../ui/UE5DumpUI/Views/PropertySearchPanel.axaml:160)

**B37 — Folder eviction ranks by the one signal its sibling calls unusable.**
`LoggingService.cs:504-533` ranks by `DirectoryInfo.LastWriteTimeUtc`, the signal the sibling at
`:558-563` documents as unusable, with no age test and no UI-folder guard; `docs/architecture.md:278-280`
states the invariant as doctrine. *Reachability is indirect* (a running game's folder mtime is normally
fresh; it only sinks past rank 20 when startup purges bump many stale folders above it). Impact is log
files only.
*Fix:* judge by the newest file inside, skip the UI folder, or drop the count cap entirely now that age
retention exists. S/low. [`ui/UE5DumpUI/Services/LoggingService.cs:511`](../ui/UE5DumpUI/Services/LoggingService.cs:511)

**B38 — Cleanup reports land outside the app data folder.** `ProxyDeployViewModel.cs:619` — the only one
of **nine** `GetAppDataPath()` consumers that omits `Constants.LogFolderName`. The written record of a
destructive cleanup lands in `%LOCALAPPDATA%\Reports`, invisible to the System-tab data wipe and to "send
me your app data folder". Cannot damage a co-tenant app (`PruneAgedReports` globs
`leftover-proxies-*.txt`). S/low.
[`ui/UE5DumpUI/ViewModels/ProxyDeployViewModel.cs:619`](../ui/UE5DumpUI/ViewModels/ProxyDeployViewModel.cs:619)

**B39 — HintCache writers share one fixed `.tmp` path across processes.** `Flamme.cpp:303/357/423/494` —
four writers, one fixed `.tmp` path, `std::ios::trunc`, no mutex in the module; `set_ue_version_override`
and `set_invoke_timeout` are not gated on `m_scan.running`. *The in-DLL lost-update window is small* (read
and write both live inside `SaveResults`) and a duplicate rename throws into the existing catch. **The
genuinely routine race is cross-process:** `AobUsageService.cs:129-135` writes the byte-identical path from
the UI on every scan completion, guarded only by an in-process semaphore, with its own comment at `:58`
documenting an ordering nothing enforces. Worst case: a lost `ueVersionUserOverride`. M/med.
[`dll/src/Flamme.cpp:303`](../dll/src/Flamme.cpp:303)

**B40 — `ue5_callDLL` uses bare `getAddress`.** `UE5CEDumper.CT:199-204` — bare `getAddress` + a nil test,
where `lessons-learned.md:110` records that it **throws**; lines 172 and 297 of the same file already use
`pcall`. A throw in `ue5_shutdown`'s first statement aborts `[DISABLE]` before `_logHandle:close()`.
**Only reachable via B30's tick-despite-bail chain**, so fix them in one commit; alone it costs a confusing
CE dialog + one leaked FILE handle. S/low. [`scripts/UE5CEDumper.CT:200`](../scripts/UE5CEDumper.CT:200)

**B42 — Second launch dies before the logger exists.** `App.axaml.cs:37-44` — `Shutdown(1)` before
`_logging` exists (`:53`): no window, no dialog, no log line, and `str.Error.AlreadyRunning` is one of
R6's 24 dead keys. The first instance is still on the taskbar, so the cost is a dead key + an unexplained
no-op relaunch.
*Fix:* show it (or activate the existing window) before shutting down, or delete the key. S/low.
[`ui/UE5DumpUI/App.axaml.cs:38`](../ui/UE5DumpUI/App.axaml.cs:38)

**B43 — Exclusive SRWLOCK held across `LoadLibraryW` in the winmm proxy.** `Lugner_Winmm.cpp:229-253` —
the lock spans `LoadLibraryW` **and** a Sein `LOG_INFO`; the dxgi original's safety argument is explicitly
conditional on `CreateDXGIFactory1`/RHI-init being the only entry point, and the generator emits the
*conclusion* as a constant. `timeGetTime`-family thunks are reachable from any thread including a foreign
`DllMain`. **Latent, not occurring:** the dev-log records the first forwarded call at T+1.2 s on both UE4
and UE5 test games, and two mitigations weaken the analogy (we deliberately don't link Winmm;
`Mimic.cpp:149-177` resolves from System32 by explicit path, usually leaving real winmm already mapped).
*Fix:* **remove** the SRWLOCK (aligned pointer stores can't tear; racing resolvers are idempotent), move
the logging out of the region, and fix the generator to derive the precondition per-DLL. **Do not take the
"spin until resolved" option** — a thread created from DllMain cannot start until the loader lock is
released, so spinning thunks would deadlock *deterministically*. M/med.
[`dll/src/Lugner_Winmm.cpp:239`](../dll/src/Lugner_Winmm.cpp:239)

**B44 — Thunk has no post-resolve null test.** `Lugner_Winmm.asm:76-83` — `jmp 0` for any name
`GetProcAddress` couldn't fill. **Knowingly accepted:** the dxgi original states the full tradeoff, and
`dll/CMakeLists.txt:480-484` records a deliberate rejection of the exact stub this finding proposes
(`return 0` == `TIMERR_NOERROR` would silently no-op the 1 ms tick). Residual real defect: the *generated
comment* drops the original's qualifier, making a null slot read as benign when it is an RIP=0 AV.
**Comment fix only.** S/low. [`dll/src/Lugner_Winmm.asm:82`](../dll/src/Lugner_Winmm.asm:82)

**B45 — Ghost Cancel button on the wrong card.** `ProxyDeployPanel.axaml:145` / `:43` — each Cancel is
wired to its own command but shown by the shared `IsScanning`. **Not a mis-fired cancel** — the generated
`*CancelCommand`'s `CanExecute` is the wrapped command's `CanBeCanceled`, so Avalonia renders a disabled
ghost button on the wrong card. (Note `ScanAsync` genuinely has no cancel affordance at all.) S/low.
[`ui/UE5DumpUI/Views/ProxyDeployPanel.axaml:145`](../ui/UE5DumpUI/Views/ProxyDeployPanel.axaml:145)

**B46 — `HexToBytes` cannot fail.** `Renge.h:226-233` — no character validation, an odd trailing nibble
dropped, no failure channel; `write_mem` checks only non-empty and ≤64 KiB. `"DE AD BE EF"` →
`{DE,0A,0D,BE,0E}` written and answered `ok:true`. *Refuted sub-claim:* `StrToAddr` is **no longer** the
lenient variant — it wraps `TryStrToAddr` and returns 0, so a bad address fails the write rather than
going wild. Reachability requires a hand-crafted third-party pipe client (our own producer uses
`Convert.ToHexString`).
*Fix:* `TryHexToBytes` + `MakeError`. S/low. [`dll/src/Renge.h:226`](../dll/src/Renge.h:226)

**B47 — "First-proxy-wins" mutex is machine-wide, and usually doesn't fire at all.**
`Heiter.cpp:155-161` — `Global\UE5CEDumper_PrimaryProxy` though the comment describes same-process scope;
`ERROR_ALREADY_EXISTS` returns TRUE before `Sein::Init`, `Mimic::StartThread` and auto-start, **with no
logging at all**. *Two corrections that flip the emphasis:* creating a `Global\` name needs
`SeCreateGlobalPrivilege`, which a non-elevated game lacks — so `CreateMutexW` returns NULL, the guard is
skipped, and the cross-process kill usually doesn't happen **but the intended same-process dedup silently
never works either**. And two simultaneously instrumented games are already unsupported (one fixed pipe
name).
*Fix:* `Local\…_<PID>` + log the passive decision. S/low. [`dll/src/Heiter.cpp:155`](../dll/src/Heiter.cpp:155)

**B48 — Proxy generator has no PE-machine check.** `gen_proxy_forwarders.py:332` — `%SystemRoot%\System32`
with no assertion. **Measured on this machine** with the script's own `read_exports`: System32 winmm = 180
named exports, SysWOW64 = 192; 12 x86-only names, and **174 shared names at different ordinals**. A
regeneration under 32-bit Python emits 12 permanently-null thunks (→ B44's `jmp 0`) and a wholesale-wrong
`@ordinal` map, while the build stays internally consistent and links clean.
*Fix:* assert `magic==0x20B` and machine `0x8664`, use `Sysnative` when `sys.maxsize < 2**32`, print the
detected architecture. S/low. [`scripts/gen_proxy_forwarders.py:332`](../scripts/gen_proxy_forwarders.py:332)

---

## 🔧 REFACTOR

### Worth doing NOW — each closes a live-defect class

| ID | Item | Why now |
|----|------|---------|
| **R1** | Collapse the three stale module lists in `docs/naming-convention.md` (head table `:26-54`, namespace block `:88-118`, roster `:262-320`); 8 modules missing from the first two | S/low. Exactly the head-vs-tail drift `Himmel.h:32-35` already diagnosed and repaired for itself. Three lists, not two. |
| **R2** | Delete the 3 private Lua escapers (`InvokeScriptGenerator.cs:555`, `BakedScriptGenerator.cs:582`, `FreezeScriptGenerator.cs:217`) and the private preamble/close copies (`CeXmlExportService.cs:1607`, `:1615`, `BakedScriptGenerator.cs:346`) in favour of `CeLuaHygiene` | S/low. Divergence is being maintained by hand (`FreezeScriptGenerator.cs:233`: *"keep them mirrors"*). Keep `EscapeLuaComment` (different algorithm) — move it into `CeLuaHygiene`. Add one full-text preamble assertion so drift fails loudly. |
| **R3** | Add `CeLuaHygiene.AppendIdleWait`, use it at the **2** sites that sample `cmd` once (`CoordLibraryScriptGenerator.cs:247`, `TeleportScriptGenerator.cs:212`) | ~6 lines, zero regression. The other 9 generators never read `cmd` and cannot spuriously report busy — exposure is 2 sites, not 11. |
| **R4** | `DumpExplorerViewModel.cs:468-470/487` → `SplitTerms`/`MatchesAllTerms` | S/low, the sole holdout of a MUST rule. **Measure first:** the current code lower-cases once at parse time and compares Ordinal; if per-field `OrdinalIgnoreCase` regresses on a large `.jsonl`, keep the parse-time lowercase but store the four fields separately. |
| **R5** | One `ReassertWorker` helper (thread + stop flag + control mutex + join discipline) for the six hold modules; `Grimoire::WORKER_SLEEP_SLICE_MS = 25` replacing the 8 bare literals | S–M. Do it **with B14** — B14 is the realised symptom, and audit #3's M5 guard is already byte-identical at 6 hand-copied sites. |
| **R6** | `en.axaml`: delete the 24 inert keys, wire the 3 live ones, add a CI grep gate mirroring `extract_patterns --check` | S/low. Reproduced independently: 1297 `x:Key` entries (no duplicates), 1224 markup refs + 50 code literals, `comm -23` yields exactly 24 orphans and `comm -13` is **empty** — no dangling references. `str.TP.LibHeader`/`str.TP.HkSet` are shadowed by `TeleportViewModel.cs:2958-2961/:4626`. Zero runtime impact; the cost is a dictionary that overstates what is localizable. *Scope correction: hardcoding VM-computed strings is systemic here, not a regression of the new card.* |
| **R7** | Delete the stale docstring in `tools/pe/aob_specificity.py:10-16` | S/low, **sharpest downside if left**. It says "NOT ACTUALLY WIRED INTO CI, AND NOTHING ELSE READS IT EITHER (audited 2026-08-01)" while `ci.yml:91-92` runs it with `--check` and throws on non-zero (added by `6f594fa`, the same day). A maintainer reading line 10 concludes the baseline is scratch and `--update-baseline`s past a golden-file gate whose whole purpose is forcing a human to notice a bound that **rose**. |

### LATER — real value, not this pass
- **`RunGuardedAsync`** over the 55 `IsBusy`/error/log blocks in `TeleportViewModel.cs` (vs ≤3 in any
  other VM). Mechanical, near-zero risk.
- **`LiveWalkerViewModel`'s hand-rolled debounce** (`:320`, `:5248-5253` with a literal `700`,
  `:5261-5280`) → `KeywordSearchMemory` — the helper that was extracted *from* this code and is already
  used for the other box in the same class.
- **135 hardcoded AXAML strings** (81 `Header=`, ~14 prose `Content=`, 20 `Text=`, 14 `ToolTip.Tip=`). Do
  `LiveWalkerPanel.axaml:820` immediately as a one-liner — it contradicts `:478` fifteen lines away.
  Decide once whether `"min"/"max"/"lo"/"hi"` count.
- **The 12-generator mailbox emitter extraction.** Only after B15 lands, so the extracted emitter starts
  correct.
- **`Genau::ValidateAndFixOffsets`** (`:3043-3862`) — reader-templated extraction of the nine Steps. L, but
  `DynOff::PickFFieldClassNameOffset` is shipped proof the pattern works, and `Grimoire.h:132-140` records
  that recovering one such constant cost a full RE session. Start with Step 8 and Step 3 Phase A/B.
- **`Fern::DispatchCommand`** (`Fern.cpp:1193-5401`, 4209 lines, 98 sequential string compares in one
  `try`). Step (a) alone — a `string_view → handler` table — forces each body out into a named function.
  Lift the duplicated 17-line exe-name block (`:1268-1284`, `:1324-1340`) regardless; that duplication is
  *how* the monolith reproduces itself.
- ~~**R8**~~ — **REFUTED 2026-08-05 by the maintainer, and the refutation is the more useful finding.**
  The claim was "the dist native payload is never refreshed outside `-Clean`", evidenced by mtimes
  (`libSkiaSharp.dll` Jul 14, `e_sqlite3.dll` Jul 5, `av_libglesv2.dll` Apr 20, next to an Aug 4 exe).
  **`build.ps1:514-516` copies EVERY `.exe`/`.dll` out of `$publishDir` with `-Force` on each Publish
  run**, so they are refreshed every time; the old dates are the **NuGet package's own timestamps**,
  preserved by `dotnet publish` → `Copy-Item`. Measured — every dist native is byte-for-byte the
  package currently referenced:

  | dist file | size | resolves to |
  |---|---|---|
  | `libSkiaSharp.dll` | 12,254,048 | `skiasharp.nativeassets.win32` **4.150.1** (newest in cache) |
  | `e_sqlite3.dll` | 1,978,880 | `sourcegear.sqlite3` **3.53.3** |
  | `av_libglesv2.dll` | 5,394,096 | `avalonia.angle.windows.natives` **2.1.27548.20260419** |
  | `libHarfBuzzSharp.dll` | 2,038,112 | `harfbuzzsharp.nativeassets.win32` **14.2.1.1** |

  **This finding used file mtime as a proxy for content freshness — which is verbatim the 4b root
  cause this same audit named** ("a cheap proxy signal substituted for a predicate the codebase
  already computes"). It shipped as a finding *about* that pattern while being an instance of it.
  Worth keeping visible: the audit's own method was not immune to the defect it was cataloguing.

  All that survives is **prune**: drop a native dependency and its DLL lingers in a *developer's local*
  dist until the next `-Clean`. It cannot reach a shipped artifact (CI passes `-Clean`; the non-AOT
  Release exe is a self-extracting single file carrying its own natives). Not worth code. **Closed.**

### NEVER as filed — the proposed fix is worse than the defect
- **Extracting the 8-copy player-resolution chain *and* "fixing" Schlacht's missing fallback.**
  `Dunste.cpp:215-217` states the rule: the one-shot enable passes `allowScan=true`, the worker passes
  `false`, because the fallback is a full-pool scan. `Schlacht::ResolveLocalPC`'s only caller is a 10 Hz
  worker, so its omission **matches** that rule. Adding the fallback puts a 486K-object scan on a timer.
  Its other "drift" (`ClassDerivesFromAny` vs a name substring) is strictly stronger. **Do only the
  sub-part:** fold the triplicated `ReadFloatAt`/`WriteFloatAt`, duplicated `ReadVec3At`/`WriteVec3At` and
  5 `ToLower` definitions into one header; if the chain is ever extracted, encode Schlacht's omission as an
  explicit `allowScan=false`, don't "fix" it.
- **The `Aura.cpp` split**, except steps 1–2 (move the `ParallelGObjectsScan`/`ConcatTruncate`/
  `ScanThreadCount` trio and `IsSnapshotNoiseClass` into an internal header, which retires the 2,659-line
  forward declaration at `:5817`). The graph-path block looks like the cheap seam and is not — it needs
  two find-refs-owned metadata caches exported first.
- **`MovementKnobCardViewModel`.** Rewrites 33 binding paths in the most in-game-verified panel; Avalonia
  resolves paths at runtime as strings, so a mis-rebind fails **silently** under the trimmed AOT publish,
  for zero user-visible gain. The "quadruplet" is really a pair plus two structurally different members.

---

## ❌ Adversarially refuted — do NOT re-raise

| Finding | Why it was killed |
|---|---|
| See-through enable joins away the deferred restorer before it knows the hook state (`Schlacht.cpp:572`) | The code shape is as cited, but the failure is unreachable and the stated consequence is wrong. |
| `ProxyDinput8.def` missing `UE5_CallProcessEventDirect` | The `.def` text difference is real (31 vs 32 entries, not the claimed 32 vs 33) but the described defect does not exist in the shipped binary. |
| Property Search `TabItem` has no `Tag`, so AOBMaker availability never refreshes | The static facts check out; the inference does not. |
| Blanket `NoWarn IL3053;IL2104` removes the only trim/AOT warnings | A deliberate, documented design decision with no traced failure. |
| The added `SkiaSharp.NativeAssets.Linux` pin | Inert on `win-x64` — the finder concedes it in its own text. |
| `dll/CMakeLists.txt:447` "`--check` verifies they are current" is asserted but nothing runs it | A misreading of the comment. |
| `blocktest` exits 0 when every recorded block's pattern id has gone | Documented deliberate design, and the visibility the finder said was missing is already implemented. |

Also inherited from earlier audits and **still not to be re-chased**: `LaneRoutingPipeClient` double-fire
on child death · `PipeClient.DisconnectAsync` double-firing `ConnectionStateChanged` · `Fern.cpp`
trigger_scan/extra_scan `ScanState` check-then-act · `Fern.cpp` `Tot::ResetPerCommand` first-connection
gating · Mimic `LIST_FUNCTIONS`/`LIST_INSTANCES` page-index overflow ·
`WindowsGlobalHotkeyService` ctor thread/MRE leak · "DLL unload would crash" (resident by design) ·
`Aura.cpp` batch-read buffer/offset/SEH-fallback (~4089-4504).

---

## Fix order

1. ~~**B6 + B27 together, one commit.**~~ **✅ DONE — build 2560.** (Kept here as the record of why the
   ordering mattered: wiring persistence without the backup would have converted a currently-harmless
   finding into live data loss. B6's backup half was the load-bearing part.)
2. ~~**B1, both halves in one commit.**~~ **✅ DONE — build 2561**, with B30 + B40. The live test came
   first and inverted the plan: `executeCodeEx` returned `nil`, so (a) was real and (b) was latent —
   which made "fix the arity alone" a certain brick rather than a suspected one.
3. ~~**B2, B3** — two small, low-risk, high-damage-avoided changes; both are "publish/emit correctly".~~ **✅ DONE — build 2581.**
4. ~~**B31 + B37 + B38**~~ **✅ DONE — build 2585**, including the
   `walk_payload_audit.py` docstring, which was wrong in BOTH halves it asserted.
5. ~~**B29** — one rewrite of `IsAlreadyLoadedInTarget`: wide path + identity probe.~~ **✅ DONE — build 2577.**
6. **B30 + B40** — one `.CT` commit: `memrec.Active = false` on all five bail-outs, quiet-if-absent
   `ue5_shutdown`, `pcall(getAddress)` in `ue5_callDLL`, **and the same guard in
   `CeInjectScriptGenerator.cs:89`**.
7. **B7, B5, B4** — B7 is user-data protection; B5 is the prerequisite for trusting any offset-related bug
   report; B4 needs the *separate flag*, not `MarkBackgroundWorker`.
8. **B8**, then **B14 + R5 together** (symptom + structure), then **B10** (with `try_emplace` first).
9. **B33 + B32** (one readiness-poll commit across both files) · **B34** · **B35** · **B36 + B45** ·
   **B9, B11, B12, B15–B20** — a single "small fixes" sweep; every one is S/low and independent.
10. **R1–R4, R6, R7** as the cleanup pass.

**Needs design thought before coding — do not rush:** **B28** (the obvious ratio-scoring fix does not work
and can regress the STVoyager case; needs a structural discriminator plus two new regression buffers) and
**B43** (remove the SRWLOCK rather than adding machinery; **reject** the spin-until-resolved option — it
deadlocks deterministically).

**Needs a decision from the maintainer before coding:** **B13/B41** (which volume-recycler API),
**B21** (the `AllowThousands` tradeoff), **B25** (should the version refusal ever fire on an uncorroborated
signal), **B26** (should duplicate CE records be deduped at push).

**Backlog, no urgency:** B39, B42, B44 (comment only), B46, B47, B48, R8.

### Prerequisites that are NOT in the finding text and will bite if skipped
- **B10** — `Ubel.cpp:813` must become insert-if-absent (`try_emplace`) *before* any reference return.
- **B9** — **add** the call, don't move it: `RescanApplied` never reaches `ApplyEngineState`.
- ~~**B27** — B6 ships first or together, never B27 alone.~~ ✅ done together, build 2560.
- **B31** — `retainedFileCountLimit: null`, or rolling reinstates generation-count eviction by the back door.
- **B43** — do not spin in a thunk; a DllMain-created thread cannot start until the loader lock releases.
