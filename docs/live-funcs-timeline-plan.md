# Live Funcs — call timeline and stack snapshots `[LIVEFUNCS-TIMELINE-2026-10-04]`

**Status: STEP 1 (the timeline) BUILT, build 3633, 2026-10-07; STEP 2 (parameter snapshots, following functions by
name) BUILT, builds 3639-3640, 2026-10-08 -- checked live on DumperTest58 and Avowed, see "Step 2 built" at the end
(the ledger [live-funcs-step2-items.md](live-funcs-step2-items.md)); STEP 3 (native stack) BUILT in build 3641 (2026-10-08),
proven live on DumperTest58 -- see "Step 3 design" and "Step 3 built" at the end ([live-funcs-step3-design.md](live-funcs-step3-design.md), the ledger
[live-funcs-step3-items.md](live-funcs-step3-items.md)).** Step 1 was reviewed and checked live on DumperTest 5.4 and DumperTest58 (UE 5.8) — see "Step 1
built" at the end. **Decided 2026-10-06 and 2026-10-07:** T1 (a ring buffer, 32–512 MB), T3, T4, T5, T6, T7 and T8
— see "Decisions" — after a design review whose findings are TR1–TR7 below.
Written 2026-10-04 from a reading of the code; the sizes and rates in the sections before "Step 1 built" are
arithmetic or examples, and "Measure before building" now says which have been measured.

⚠ **This is our own feature, not part of external PRs 539 / 540.** The idea came up while discussing PR 540's
fetch limit, but nothing here comes from those PRs. Its commits do **not** carry the
`Co-authored-by: fireundubh` trailer that [ext-pr-539-540-plan.md](ext-pr-539-540-plan.md) asks for
(maintainer, 2026-10-04).

-----

## What exists today

- `Stark.cpp` hooks `ProcessEvent` (MinHook). `HookedProcessEvent(thisObj, ufunc, params)` sees every call with all
  three arguments, and stamps a millisecond time (`NowMs`) for its responsiveness check.
- Linie (`Linie.h` / `.cpp`) records, per UFunction, only a fire count, the call-stream position of its first fire
  (`firstSeq`) and the inter-arrival cadence. It keeps no per-call record, so the order of calls, who called whom,
  and the arguments are gone once counted.
- `Linie::RecordCall` takes a mutex and touches an `unordered_map` on every call while recording. Fine for a
  table; a per-call log should not add a second lock on this path.
- Already available for later steps: `Grimoire::UFUNCTION_FUNC` and `Aura::GetFunctionCodeAddr` (a UFunction's
  native entry point), `ParmsSize` / `NumParms` / `ReturnValueOffset` offsets, the function-parameter walk, and
  the AOBMaker CE bridge's `NavigateDisassembler` / `NavigateHexView` ([aobmaker-integration.md](aobmaker-integration.md)).

## The buffer — a ring, 32 to 512 MB (T1, changed 2026-10-06 and 2026-10-07)

The first proposal (2026-10-04) was fill-then-stop: append until full, then stop the log. **Changed 2026-10-06 to
a ring** (TR1): when the buffer is full, the newest call overwrites the oldest, so Stop always keeps the calls just
before it. The usual use is Start, go and do the action, Stop; the calls that fire every frame (Tick, animation,
camera) can fill a fill-then-stop buffer before the action happens, and the action is lost.

- Appending is one atomic `fetch_add` on the write index; the slot is that index modulo the capacity. No lock,
  and the same cost as the fill-then-stop shape.
- **The size is a cap the user picks, never computed from a time target.** How many seconds the ring kept is the
  result, reported after Stop ("kept the last 21 s"), not a setting: a seconds target on a busy game could ask for
  a gigabyte inside the game process (maintainer, 2026-10-06). Beside the slider the UI shows an **estimate** of
  the seconds it would keep, from the last Live Funcs recording's calls per second; how much memory the game can
  spare is the user's call (T1, 2026-10-07).
- Every record carries its sequence number, so after Stop the oldest kept call is known, and a call whose entry
  record was overwritten is marked as entered before the window.
- Allocate at Start and free on Reset / client disconnect, as Linie's table already is: 512 MB sitting in the
  game process when nobody is recording is not acceptable. Freeing it safely needs TR2. A failed allocation
  refuses the Start with a message; the trace never records without its buffer.
- The buffer size is a slider, persisted with the trace's other settings — see "Decisions" below.

## 1. Call timeline — feasible, recommended first

**Two records per call, about 32–40 bytes each (TR3, 2026-10-06):** an entry record — a high-resolution timestamp
(the current `nowMs` is too coarse for a timeline; this costs one more clock read per call), the UFunction, the
calling object (`thisObj`), the thread id — and a return record (timestamp, thread id) written after the original
returns. The first plan wrote the duration back into the entry record; a separate return record never writes into
a slot the ring has already handed to a newer call. 64 MB holds close to 1 million calls, 128 MB about 2 million.
How long that lasts depends on how often the game calls `ProcessEvent`, which has not been measured: as an example
only, at 50,000 calls per second 64 MB keeps about the last 20 seconds.

**Nesting depth is the part worth having.** `ProcessEvent` is re-entered from inside UFunctions, so one thread's
entry and return records nest, and pairing them after Stop turns the flat log into a call tree: which UFunction
ran inside which, and for how long. That is the "fuller call stack" asked for on PR 540. It is computed after
Stop, not kept in a `thread_local` counter on the hot path (TR3): a counter that an exception unwinds past stays
wrong for the rest of the recording, while a missing return record marks only that one call as "did not return".

**Names are resolved after Stop**, once per distinct function and object, never on the hot path. Objects can be
destroyed between the call and Stop, so a name resolved later is "what is at that address now"; validate it
against the object array and mark the ones that no longer resolve.

**Output, in order of cost:** export first (`.jsonl`, and CSV for spreadsheets), then a view in the UI — an
indented call tree with durations, filterable by function. The view is most of the work; the DLL side is small.

## 2. Parameter snapshots — feasible, only for chosen functions

At the hook the only meaningful "registers" are the three arguments (rcx = object, rdx = UFunction,
r8 = parameter block). The rest of the register file holds whatever the caller left there, so a full register
dump is mostly noise. What is worth keeping is the **parameter block**:

- Copy `ParmsSize` bytes on entry (inputs), and again after the original returns (out parameters and the return
  value). Decode later with the parameter walk we already have.
- Limit: an `FString`, `TArray` or object parameter is stored as a pointer. Following it at call time costs time
  and risks reading freed memory; following it at Stop reads what is there then. Start with pointers only.
- Doing this for every call would fill the buffer many times faster. **Only for functions the user ticks** in the
  Live Funcs table. Snapshots go to a buffer of their own, and the call's entry record holds their index (TR6).

### Also built with step 2: the Call Trace list and detail pane (maintainer, 2026-10-07)

Found by the maintainer on build 3638, and folded into step 2 rather than fixed on their own:

- **The list's columns cannot be resized.** The list is a virtualized ListBox (rows are made only for the ones on
  screen), and its header and every row share fixed widths: Time 96, Duration 88 and Thread 64 px, Function the rest,
  Object 260 px. A long object name is cut with an ellipsis and has no tooltip. Not sorting is right (the list is a
  timeline), but the widths should be draggable from the header, shared by every row and remembered. The detail
  pane (fixed at 380 px) should be draggable too, and the Object column gets a tooltip.
- **"UFunction: 0x…" reads like a call address.** It is the UFunction object's address (data on the heap), and the
  Object line's is the object's. Both are absolute addresses in that run of the game, not RVAs; pasted into CE's
  disassembler they show garbage. The detail pane will say that they are object addresses, and gain the function's
  native entry (`UFunction->Func`): a native function's `execXxx` thunk as a CE address,
  `"Game-Win64-Shipping.exe"+1A2B3C`; a Blueprint function marked as running as script, whose `Func` is the shared
  `ProcessInternal`. This is the first piece of view D (step 3), brought forward.
- **The detail pane always prints a `0x` prefix**, ignoring the UI's Address setting ("Hex (no prefix)"). It will
  follow the setting.

## 3. Native stack snapshots — feasible, chosen functions only

`RtlCaptureStackBackTrace` (or `RtlVirtualUnwind` over `.pdata`) gives the native return addresses above the
hook. It costs far more than a timeline record, so again **only for ticked functions**, with a fixed depth
(16 frames = 128 bytes) and a cap on how many are taken (TR7).

### Several views, like Cheat Engine — or just a dump?

Recommendation: **give the views that only we can give, and hand everything else to Cheat Engine.**

| View | What it shows | Why we can do it better than a raw dump |
|---|---|---|
| **A. Call stack** | One row per frame: return address as `module+RVA`, plus the function it falls in | `.pdata` (`RtlLookupFunctionEntry`) gives each frame's exact function start without symbols. Match that start against every UFunction's native entry (`UFUNCTION_FUNC`) and the addresses Himmel already resolves (`ProcessEvent` and others), and a frame reads `Game.exe+1A2B3C  in  execTakeDamage (Character)`. A shipping game has no PDB, so CE would show only the offset. |
| **B. Raw stack, annotated** | A copy of N bytes at the stack pointer (e.g. 512 B, clamped to the thread's stack base), one qword per row, each shown as hex / int / float, with a guess of what it is: code (`module+RVA`), a live UObject (name and class), an FName, other heap | Same as CE's stack view, plus the UE meaning of a value, which CE cannot know. The stack is gone once the call returns, so this must be a copy taken at call time; the annotation is done after Stop and carries the same "may be stale" mark as above. |
| **C. Parameters** | The parameter block from section 2, decoded by name and type | CE has no idea where a UFunction's parameters are. |
| **D. To Cheat Engine** | "Open in CE disassembler" on a frame, and "copy as CE address" (`"Game-Win64-Shipping.exe"+1A2B3C`, quoted because of the hyphens) | Through the existing AOBMaker bridge (`NavigateDisassembler`). Disassembly, breakpoints and tracing stay CE's job; we do not rebuild them. |

Not planned: our own disassembler view, register views beyond the three arguments (T4), and Blueprint's script stack
(`FFrame`), whose layout changes between UE versions — the nesting depth from section 1 already gives the
UFunction-level stack.

## Recommended order

1. **Timeline** with nesting depth and duration, the ring buffer (T1) and the recording scope (T5: the
   ticked-function scope first, then leaving out per-frame functions), export to `.jsonl` / CSV. Then the call-tree
   view in the UI.
2. **Parameter snapshots** for ticked functions (view C).
3. **Native stack** for ticked functions: view A first, view D next to it, view B last.

## Measure before building

- How many `ProcessEvent` calls per second a busy game makes, to size the buffer and the default.
- What the extra clock read and record write cost per call, with and without recording (frame time on a fixture).
- What one stack capture costs, to decide how many ticked functions are reasonable.
- What a **128 MB** buffer costs after Stop, which decides whether 128 stays on the slider: moving it out of
  the game (TR5), resolving its names, and loading about 2 million calls into the UI. The hot-path
  cost per call does not depend on the buffer size; what grows is the memory held in the game process and
  everything that happens after Stop.
- **Measured 2026-10-07, with step 1** (details under "Step 1 built"): the cost per call (129 ns traced, 21 ns
  outside a ticked scope, on the machine below), a fixture's call rate (121 calls/s on DumperTest 5.4 at 15 fps; 1,040–1,670 on
  DumperTest58 uncapped), and a traced Start's cost (5–7 ms at 32 MB, 17–26 at 128, 67–150 at 512).
  **Avowed, the same day** (a busy UE 5.3 game; "Step 1 built", "Avowed"): 9,755–14,454 calls/s, so 128 MB keeps
  about 2 minutes and 512 MB about 8. After Stop, a full 128 MB ring (1.68 million calls) loads in the UI in 11.5 s
  and a full 512 MB one (6.71 million) in 44 s, almost all of it the read; the UI's memory peaks at 1.3 and 3.25 GB.
  **Not measured:** what one stack capture costs (step 3).
- **The machine every figure here was measured on** (the maintainer's CPU-Z / GPU-Z, 2026-10-07): **AMD Ryzen 9
  9955HX3D** (Fire Range, 16 cores / 32 threads, a laptop part at 55 W, L3 96 + 32 MB), **DDR5-5600 64 GB** (CL46),
  NVIDIA RTX 5090 Laptop 24 GB. The per-call costs are CPU and cache figures; the GPU sets only the games' frame
  rate (Avowed ran at about 48-53 fps). A laptop's power plan and boost move them by a few percent from run to run
  (the table's bare cost read 31.8 and 33.8 ns in two runs). The project's other PC differs, so a figure without its
  machine is not comparable; dll_core_test's benchmarks print the CPU since 2026-10-07.

## Design review, 2026-10-06 (TR1–TR7)

A reading of this plan against `Stark.cpp` and `Linie.cpp`, not a measurement. TR1 and TR4 became decisions (T1,
T5); the others are requirements for building.

- **TR1 — fill-then-stop loses the action.** → T1, a ring.
- **TR2 — freeing the buffer under a call in flight crashes the game.** A `ProcessEvent` call can nest deep and
  run long. If Stop, Reset or a disconnect frees the buffer while a call is still inside the original, its return
  record lands in freed memory, on the game's thread. Linie's table is safe because every touch is under its
  mutex; a lock-free buffer needs a guard of its own: a generation number and an in-flight count around each short
  write (never held across the call to the original), and the buffer freed only at a count of zero.
- **TR3 — a separate return record, not a write-back.** See section 1. It also means the hook does work after
  the original returns. Today `HookedProcessEvent` calls the original outside its guard on purpose (Stark.cpp), so
  that post-call work needs a guard of its own.
- **TR4 — filter at record time, not only with a bigger buffer.** → T5. For the ticked-function scope: a fixed
  set of ticked UFunction addresses, read-only during the recording, one lookup per call. The scope is marked
  with the stack pointer at the ticked call's entry, and its return clears the mark; if an exception unwinds past
  the ticked call, the first call made from higher up the stack clears it instead, so the scope cannot stay open.
- **TR5 — the volume after Stop.** 64 MB is about 1 million calls; as JSON over the pipe that is roughly 100 MB
  of text, and `pe_profile_get` moved 144 KB in about 15 ms, so this would take on the order of ten seconds. Move
  it as binary pages with a separate name table, and have the UI load a time range or one subtree, never a tree
  of a million nodes.
- **TR6 — snapshots in a buffer of their own.** Parameter blocks and stack copies vary in size; in the ring they
  would push calls out faster and make slots variable. The entry record holds the snapshot's index.
- **TR7 — cap the native stack captures.** One walk costs microseconds (an estimate). A ticked function that
  runs on every actor every frame would be tens of thousands a second and drop frames. Cap it per function (the
  first N calls) or per second.

### Cost on and off — arithmetic, not measured

| What is on | Extra per `ProcessEvent` call | Expected effect |
|---|---|---|
| Trace off, or the experimental tabs off | nothing beyond today's one relaxed atomic load and branch | none |
| Timeline (entry and return records) | about 20–50 ns: a clock read, a `fetch_add`, two small writes | at 50,000 calls a second, about 0.1–0.25% of one core |
| Parameter snapshots, ticked functions only | two copies of the parameter block, tens of ns | negligible |
| Native stack, ticked functions only | 1–10 µs a capture | drops frames on a busy function without TR7's cap |
| Every register on every call (not planned, T4) | about 700 B a record | about 20 times the buffer use: 64 MB would last under two seconds |

## Decisions

| # | Item | Decision |
|---|---|---|
| T1 | **Buffer** (maintainer, 2026-10-04; changed 2026-10-06 and **2026-10-07**) | A **ring** that keeps the calls just before Stop (TR1), not fill-then-stop. A **power-of-two slider: 32 / 64 / 128 / 256 / 512 MB, default 64**; whether the game can spare the memory is the user's call. Beside it, an **estimate of the seconds it keeps**, from the last Live Funcs recording's calls per second and the bytes per call; no estimate before a recording. The maintainer offered a time-first choice (pick seconds, the UI works out the memory) and a memory-first slider; built as the slider with the time shown beside it, because the call rate changes with what the game is doing and a time target could ask for any amount of memory. The size is never computed from a seconds target. How many seconds it kept is reported after Stop. A failed allocation refuses the Start. Persisted across UI sessions, and disabled while recording, like the Live Funcs sliders. Was: 64 / 128 MB (2026-10-06); fill-then-stop, 32 / 64 / 128 MB, default 32 (2026-10-04). |
| T2 | **Recording** (maintainer's direction 2026-10-04; details ⚠ to confirm) | **The trace rides on the Live Funcs recording; it has no Start / Stop of its own.** Live Funcs' toolbar gains a "Trace" checkbox and the buffer slider; ticked, the one Start records the count table and the trace over the same window. With Live Funcs not recording, the trace cannot record either. Replaces the first proposal of a separate tab with its own Start. |
| T3 | **Viewing and choosing functions** (proposal 2026-10-04; **decided 2026-10-07**) | The trace is **viewed** in its own top-level tab (working name "Call Trace"). Functions are **ticked in the Live Funcs table** from an earlier recording; the next Start traces only inside them (T5 (a)). The snapshots of steps 2 and 3 are chosen in a column of their
own (T9; was "the same tick snapshots them", changed 2026-10-07). **With nothing ticked, the trace records every call** — the first proposal — after a confirmation (T7). |
| T4 | **Registers** (maintainer, 2026-10-06) | **Kept as planned: no register capture.** The maintainer first asked for GPR / XMM / YMM snapshots. At the hook only rcx, rdx and r8 (object, UFunction, parameter block) mean anything: the rest of the register file is the dispatcher's, and `ProcessEvent` takes no float arguments, so XMM / YMM hold leftovers. A UFunction's float and vector arguments are in the parameter block, decoded by name in view C. Registers matter inside the native implementation, which is Cheat Engine's debugger; view D hands the entry point over. A faithful capture would also need a MASM entry stub, because the C++ detour can change volatile registers before it reads them. |
| T5 | **Recording scope** (maintainer, 2026-10-06) | Two opt-in filters applied at record time (TR4), **(a) built first**. **(a) Ticked-function scope:** applies whenever functions are ticked (T3) — record only the ticked functions' calls and everything nested inside them on the same thread; tick OnJump, get the call tree under OnJump. **(b) Leave out per-frame functions:** a checkbox; the previous recording's per-frame list (`per_frame_funcs`, build 3630 on), passed at Start. Nothing ticked and (b) off records everything. The tick column is (a)'s alone: the snapshots have their own (T9, 2026-10-07; was one column for both). |
| T6 | **Experimental only** (maintainer, 2026-10-06) | The Trace checkbox, the buffer slider, the scope options, the tick column and the Call Trace tab show only while the experimental tabs are enabled (the System tab's checkbox, `ExperimentalGate`). The DLL never sees that flag: it allocates the buffer and records only when Start asks for the trace, so with the trace off its hot path is exactly today's. |
| T7 | **Nothing ticked** (maintainer, 2026-10-07) | A Start with Trace on and no function ticked asks first: nothing is ticked, so the trace records every call — a wide range, and more load on the game. Confirmed once, it does not ask again in the same UI session; a second Start with nothing ticked runs at once. Cancel leaves everything as it was and starts nothing, and does not count as asked. **It asks only when there is something to tick**: the first recording, or any Start while the Live Funcs table is empty, has no rows to tick from, so it records every call without asking (maintainer, 2026-10-07). |
| T9 | **Snapshots only for chosen functions** (maintainer, 2026-10-07) | Steps 2 (parameter snapshots) and 3 (native stack) capture **only for the functions the user chooses**, never for every call -- unlike the trace, there is no "nothing chosen = everything" (T3, T7). **Decided the same day:** what is limited is the **call rate**, not how many functions are chosen; the proposal in "How much may be chosen" below is taken as written (an estimate with a warning, per-frame functions marked and asked for stacks, the DLL's budget as the guarantee, a bulk tick through the same estimate); and the snapshot choice is **a column of its own**, apart from the trace's scope tick. |
| T8 | **The ticks seen from the Call Trace tab** (maintainer, 2026-10-07) | One tick state, set in the Live Funcs table. The Call Trace tab shows a copy of the trace settings and of the ticked functions, grayed out and read-only, with a hint that they are changed in Live Funcs. The top of the tab says it works with Live Funcs and cannot record on its own: Start, Stop and the settings are in Live Funcs, and the tab shows what the last recording traced (maintainer, 2026-10-07). |
| T10 | **Functions that reload at a new address** (maintainer, 2026-10-07) | **Matched by name** (class and function) for both the snapshot choices and the trace's ticks. A widget's functions unload when it closes and come back at new addresses when it opens again (D1 counted 304 on Avowed's inventory), so a choice or a tick made by address could not reach them. A choice or tick is no longer refused or dropped because its function is unloaded at Start: it waits for its function to load. The first call at a new address with a matching name arms it, and its parameter layout is read in the background while the function is alive; if that fails, its snapshots keep raw hex only. This replaces D1's "an unloaded row cannot be ticked". The critic's HIGH of the step-2 design review. |
| T11 | **A chosen call the trace would not record** (maintainer, 2026-10-07) | **Recorded alone**: its own entry and return records, flagged lone, opening no scope, so the calls it makes are not traced. With nothing trace-ticked and something chosen, the trace records only the chosen functions' calls, and T7 is not asked (the trace no longer records every call). A lone call over the budget writes nothing and is counted. |
| T12 | **The snapshot buffer** (maintainer, 2026-10-07) | **A slider of its own**, powers of two from 8 to 128 MB, default 32, remembered like the trace's; shown while something is chosen, and counted in D3's game figure. Game memory held from Start until the UI has read it, as the trace's ring is. |
| T13 | **When the snapshot estimate warns** (maintainer, 2026-10-07) | **Orange when the busiest chosen function keeps less time than the trace buffer**: its ring's calls at its rate from the last recording, against the seconds the trace buffer is estimated to keep. Derived, no new constant; a warning, never a refusal (T9). A grey note says separately how many calls a second the budget will skip. |
| T14 | **The Snapshot column** (maintainer, 2026-10-07) | **One checkbox** in step 2, apart from the Trace tick (T9). Step 3 decides whether native stacks get a column of their own or the checkbox becomes a kind. |
| T15 | **Stacks get their own "Stack?" column** (step-3 design D1, 2026-10-08; **confirmed by the maintainer 2026-10-08, as recommended**) | Who can be chosen differs (every function has a stack, not every one parameters), T9's ask-once and bulk rule apply to stacks only, and a walk costs about 100 times a parameter copy. |
| T16 | **The minimum lands before T9.1's estimate line and T9.2's ask-once** (D2; **confirmed by the maintainer 2026-10-08, as recommended**) | The time box; the DLL's budget is the guarantee meanwhile, and the column stays behind the experimental gate. They are the first items after the minimum. |
| T17 | **Stack budget 100 a second per function, 200 in all; depth 16 frames** (D3; **confirmed by the maintainer 2026-10-08, as recommended**) | Provisional, re-weighed from the measured µs per capture: total = 2,000 µs / mean µs. The review (L10) suggests a total of 100 until it is measured. |
| T18 | **Stack rings share the snapshot buffer and its K** (D4; **confirmed by the maintainer 2026-10-08, as recommended**) | One allocation, one memory figure, one release; no second slider. |
| T19 | **`pe_snap_get` with `"kind":"stack"`, no new command** (D5; **confirmed by the maintainer 2026-10-08, as recommended**) | It reuses the paging, gen and bulk-lane logic; the pipe count stays 104. Cheap to flip. |
| T20 | **A Low stack budget, and a warning in the UI** (the maintainer, 2026-10-08) | T17's defaults are not measured on any machine: they come from the plan's 10 µs upper estimate (200 × 10 µs = T9's 2 ms a second), and the test exe measured 1.4-2.6 µs a capture on the maintainer's PC. A slower machine, or a game whose .pdata pages are cold, pays more, so the UI offers **Low: 50 a second per function, 100 in all** (half of T17) beside Standard. The UI also says, wherever stacks are chosen, what the capture is and what it can cost: a software walk inside the hooked call (not a debugger capture like Cheat Engine's), so slower, and its time is added to the game's frame (many in one frame show as stutter); guarded against the cases the design foresaw, but run on the game's own thread, so an unforeseen one -- stopping mid-capture included -- could still stall or crash the game. Built as S3-U8. |

### Why the trace rides on Live Funcs (T2, T3)

The usual way to use it is one workflow: record in Live Funcs, find the candidates, then look at the calls
in order. A trace started on its own would have nothing to point at.

- **It is a design choice, not a technical limit.** Both the count table and the trace hang off Stark's
  `ProcessEvent` hook, and neither needs the other to record. Tying them is chosen for the workflow.
- **One recording, one lock.** One Start / Stop means one "recording" state: the fetch limit, Min calls, Save
  .jsonl, the Trace checkbox, the buffer slider and the ticks are all disabled together while it runs. Two
  Start buttons would each need their own rules.
### How much may be chosen (T9, raised and decided 2026-10-07)

The maintainer's question: can the number of chosen functions be capped, or warned about -- a user may select
all -- and the choosing itself needs designing. Below: the analysis, and the proposal the maintainer took as
written ("limit the call rate; do the proposal; two separate tick columns", 2026-10-07).

**The count is the wrong thing to cap; the call rate is the right one.** What a snapshot costs is paid per CALL,
not per chosen function. Ticking all 500 functions that fired once each costs 500 captures; ticking one Tick that
runs on 80 actors at 60 fps costs 4,800 captures a second. Arithmetic, not measured:

| Choice | Captures / s | Parameter snapshots (two copies, tens of ns; bytes = 2 x ParmsSize + a header) | Native stack (1-10 µs each, TR7) |
|---|---|---|---|
| 500 functions, each fired a few times | a few hundred | negligible | well under 1 ms a second |
| one per-frame function on 80 actors, 60 fps | 4,800 | about 0.1 ms a second; fills a 64 MB buffer of 64-byte blocks in minutes | **5-48 ms a second: frames drop** |
| "all" on Avowed (9.8-14.5k calls/s measured) | ~14,000 | under 1 ms a second; a 64 MB buffer in about a minute | **14-140 ms a second: unusable** |

So a cap of "N functions" both blocks harmless choices (many rare functions -- exactly the action recording's
target) and lets through the harmful one (a single per-frame function).

**Decided -- the proposal:**
1. **No cap on the count. An estimate from the previous recording, like D3's memory line.** The Live Funcs table
   already has each function's calls over a known window, and whether it is per-frame. Beside the choice: "chosen:
   N functions, about X calls/s last time -> about Y ms of the game's time a second for stacks, Z MB a minute of
   snapshots". Above a threshold (stacks: say 2 ms a second -- an eighth of one 60 fps frame, if it all landed in one) the line turns orange;
   like D3, a warning, not a refusal.
2. **A per-frame function is marked**, and choosing one for a native-stack snapshot asks once (T7's pattern): it is
   the one choice that drops frames by itself.
3. **The DLL enforces a budget regardless (TR7)**, because the last recording's rates do not bind the next one: at
   most N captures per function per second and a total per second, the first calls of each second kept, the rest
   counted as skipped and reported with the trace ("1,234 stack captures skipped: over the budget"). The budget is
   the guarantee; the UI's estimate is the advice. Defaults to measure in step 3.
4. **"Select all"** -- there is none today (one row at a time). If a bulk tick is added (tick the filtered rows),
   it goes through the same estimate, and per-frame rows are left out of a bulk tick for stacks unless asked.

**Decided: two columns.** One column would couple the trace's scope and the snapshots: ticking the opener to see
its call tree would also snapshot it, and snapshotting a function nested under the opener would make it a scope
root too. So the Live Funcs table keeps its Trace tick (T5 (a)) and gains a Snapshot column for steps 2 and 3. Left
to settle when step 2 is built, and settled on 2026-10-07: one tick (T14); the warning's threshold (T13); the
budget's defaults start provisional, measured live with step 2 and again for stacks in step 3.

- **Same window, so the two views can point at each other.** A row in the count table can jump to its calls in
  the trace, and a call in the trace back to its row, because both cover exactly the same calls.
- **This settles the earlier open question:** the count table and the trace do record at the same time,
  whenever Trace is ticked. Measure the cost of both together ("Measure before building").
- **Two rounds for snapshots:** the first recording finds the functions; tick them; the next Start captures
  their parameters and stacks. Ticks persist, so the second round needs no setup.
- **Viewing in its own tab, not inside Live Funcs:** the call tree and the per-call views need the room, and
  the UI already gives related tools sibling tabs (Snapshot, SPC Query, Class Pivot). No panel has a tab
  control inside it today. After Stop with Trace ticked, Live Funcs offers "Open in Call Trace".
- **Layout of the Call Trace tab:** the call tree with durations as the main area, export on top, and a
  detail pane for the selected call with **its own tabs for views A, B and C** (call stack / stack copy /
  parameters). The tabs belong in the detail pane, where they switch between views of one call.
- **Experimental at first:** the Trace checkbox and the Call Trace tab show only when the experimental tabs
  are enabled, until the hot-path cost is measured on real games.
- **Not mixed with PR 540:** the Live Funcs controls from PR 540 are built first and unchanged; the Trace
  checkbox, buffer slider and tick column are added later as this feature, without the co-author trailer.

-----

## Step 1 built (build 3633, 2026-10-07)

**What was built.** Linie's ring (entry and return records, 40 bytes each), the ticked scope (T5 (a)) and the
per-frame exclusion (T5 (b)) on Stark's hook; `pe_profile_start`'s `trace` option, `window_ms` on `pe_profile_get`,
and `pe_trace_get` / `pe_trace_names` / `pe_trace_release` ([pipe-protocol.md](pipe-protocol.md), "The call
trace"); in the UI, the Trace row and tick column in Live Funcs (T1, T3, T6, T7) and the Call Trace tab (T8): a
flat virtualized tree, a space = AND filter, the callers of any call, JSONL / CSV export.

**One deviation from TR5, recorded rather than hidden.** TR5 asked the UI to load a time range or one subtree. Step 1
loads the whole kept window — but as columns (about 70 bytes per call) under a row list that makes a row object only
for the rows on screen, never a node object per call. What that costs for a full 512 MB ring is the open measurement
above.

**Review.** Three reviewers (the DLL; the UI's data and logic; the UI's integration, AOT and the docs), each finding
put to a skeptic: 36 findings, one refuted. Fixed one per commit, red before green where testable: a reader's release
or names tied to its recording (DLL-1); the quiesce state machine — an RAII in-flight count, nothing the hook reads
changed while it may be inside, a Start meanwhile refused as Busy, a later wait clearing the give-up (DLL-2, -3); a
refused Start, an unarmed trace and a failed stop reported as what they are (DLL-4, F8, F9); an empty ring released
at Stop (DLL-5); the distinct pass in sets, not a vector per record (DLL-6); rows from an earlier connection not
tickable (F1, F3, MED); a new process's first trace read though its generation repeats (F2, MED); a tick by name
covering every class of that name; the time base from the earliest clock reading (F4); the load's lifecycle (F5, F6,
CT-RELEASE-CANCELLED); export with its own flag; an older trace on screen marked (F7); comments and wording.

**Live checks.**

| Fixture | What held |
|---|---|
| DumperTest 5.4 Shipping, 15 fps | The rig (`tools/verify/livefuncs_trace_live.py`): 15 of 16 — records exactly twice the table's calls (2,400 for 1,200), paging, names, release, the per-frame exclusion, a bad size refused. The 16th, the ticked scope, had nothing to tick: the stock template's ProcessEvent traffic is flat. The UI walkthrough: T7 not asking on the first run, asking with rows to tick, Cancel not counted, once per session; a ticked recording; the filter, Show in tree, the detail pane, CSV export. Found there: a long function name ran into the Object column (fixed). |
| DumperTest58 Shipping, uncapped | After adding a nested ProcessEvent chain to the fixture (`TraceNest_*`, tools/ue-sample/README.md): **18 of 18**. Ticking `TraceNest_Outer` gave exactly 20 roots and 60 entries for 20 rounds — three nested calls each — against 16,772 calls in the unscoped trace. The UI showed Outer ▾ Inner ▾ Leaf with nesting durations and the caller chain, and the long-name clip. |

⚠ **Found while building the fixture:** a BlueprintNativeEvent called from C++ does NOT go through ProcessEvent when
the owning class is native — UHT's thunk calls `_Implementation` directly. The first package's chain reached the
hook only at the delegate's binding. The fixture now dispatches by name through ProcessEvent.

### Avowed (UE 5.3 Shipping, the dxgi proxy at 3633), 2026-10-07

The maintainer launched the game (our own Steam launch had not started it, and Steam could not be inspected) and
loaded a save; the rig and the UI ran in gameplay, and during the recordings the maintainer opened the inventory
(`I`) and closed it again. Evidence: `out/livefuncs-trace/avowed-*.json`, the UI's `pipe-0.log` / `view-0.log`.

| Run | Calls / s | Kept | After Stop | Checks |
|---|---|---|---|---|
| Rig, 32 MB, 10 s | 9,755 | 225,522 records (not lapped) | read 0.36 s (12.0 MB on the pipe); 125 functions, 280 objects | 18 / 18; ticked scope 517 roots, 1,034 entries against 112,761 unscoped; per-frame exclusion 61 / 61; a traced Start 7 / 30 / 129 ms at 32 / 128 / 512 MB |
| Rig, 128 MB, 200 s | 14,454 | 3,355,443 records = 1,677,721 calls, about 116 s (lapped: 5,769,056 written) | read 5.0 s (179 MB, 36 MB/s); names 0.04 s (71 functions, 230 objects) | 11 / 11 (`--traced-only`) |
| Rig, 512 MB, 720 s | 14,450 | 13,421,772 records = 6,710,886 calls (lapped: 20,406,536 written) | read 22.8 s (716 MB, 31 MB/s); names 0.55 s (567 functions, 2,937 objects) | 10 / 11: **79 of the 567 functions no longer resolved** (below) |
| Rig, 512 MB, 600 s, again | 14,001 | 13,421,772 records = 6,710,886 calls over 548 s | read 37.8 s (716 MB, 19 MB/s: the same pipe, a busier game); names 1.0 s (796 functions, 5,891 objects) | 10 / 11: 184 of 796 functions no longer resolved, 5,387 of their calls |
| UI, 128 MB | 14,003 | 1,677,721 calls over 119.8 s | **11.5 s** from opening the tab to the tree: read 10.9 s, then names and the build 0.5 s; working set 328 MB → 1,286 MB | a two-term filter (18,957 matches) and Expand all at once |
| UI, 512 MB | 13,869 | 6,710,886 calls over 484.2 s | **44.3 s**: read 42.5 s, then names and the build 1.6 s; working set 313 MB → **3,249 MB**, still there a minute later | a two-term filter in under 3 s (capped at 50,000 matches) |

The slider's estimate, bytes / (rate × 80), matched the windows the rings kept: 120 s against 119.8 s at 128 MB,
8.1 min against 484.2 s at 512 MB (each estimate from that same recording's rate, read after its Stop).

**Found** — and the maintainer's decisions the same day (below the list):

1. **A function unloaded before Stop has no name** `[TRACE-UNLOADED-NAMES]`. Names are resolved after Stop (above,
   "Names are resolved after Stop"), and a UFunction freed by then no longer resolves: the Call Trace shows its calls
   as a bare address, and the Live Funcs table leaves the function out (`pe_profile_get` drops what does not
   resolve). On Avowed a 12-minute recording saw 568 distinct functions, where every other window saw 70–71, and
   79 of 567 (rig) and 125 of 570 (UI) no longer resolved at Stop. A second 512 MB run counted them: 184 of 796
   functions, but only 5,387 of 6,710,886 calls (0.08%), each last firing at its own moment between 162 s and 524 s
   of a 548 s window. A last call says the function was alive then, not when it was unloaded, so the spread does
   not count unloads: the inventory opened and closed during the recordings (above) fits it — its functions fire
   while it is open, each last at its own moment, and go when it closes. Rare calls are what an action recording
   is after, so the share of calls understates the loss. The plan named the risk for objects only. A way
   out: resolve a function's name when Linie first sees it — once per distinct function, on the calling thread,
   while it is certainly alive — and fall back to that name, flagged as unloaded.
2. **The UI's load holds about six times the ring** `[TRACE-UI-LOAD-MEMORY]`. What the trace keeps is about
   73 bytes per call (about 490 MB for 6.7 million calls); the rest of the 3.25 GB is the load's garbage — the
   whole window as `TraceRecord[]` (40 bytes per record) and, per page, a JSON reply of about 14 MB, its base64
   bytes and a decoded array — and nothing makes the GC run after it. Decoding each page into the window array
   and one compacting collection after the build would cut the peak and give the rest back.
3. **The UI reads at about half the rig's speed** `[TRACE-UI-READ-SPEED]`: 16–17 MB/s against the rig's 31–36 MB/s
   on the same pipe and game (19 MB/s in the rig's second 512 MB run, the game busier), and the read is 95% of the
   UI's 44 s. The page's JSON parse and base64 decode are the suspects, not measured.
4. Wording: a lapped ring nearly always has one call that began before the kept part, and the status then says
   "1 calls began" (`str.CT.Status.BeforeWindow`).

**Decided 2026-10-07 (the maintainer), to build next:**

- **D1 (1):** name a function **when Linie first sees it** — once per distinct function, while it is certainly
  alive — and use that name when it no longer resolves, marked as unloaded, in both the Call Trace tab and the Live
  Funcs table (which stops dropping such functions silently). What still has no name is shown as a share of the calls
  and functions after the load.
- **D2 (2):** fix the load first — each page decoded straight into the window, one compacting collection after the
  build — then measure the factor, and only then show beside the slider what the recording costs: the game's N MB
  from Start, and the UI's peak while it loads.
- **D3:** an estimate above the computer's available physical memory is a **warning, never a refusal** to Start (T1:
  the user decides).
- **D4:** **512 MB stays** on the slider.
- (3) is measured again after D2; (4) is fixed with them.
- **D1's live check (the maintainer's recipe):** launch with `steam.exe -applaunch <appid>` and **load a save**; record
  with Trace ticked, open the inventory (`I`) and close it, then Stop. Red (3633): its functions are bare addresses
  in the Call Trace tab and missing from the Live Funcs table. Green: named, marked unloaded, and the share still
  without a name shown.

-----

## D1–D4 built (builds 3634–3638, 2026-10-07)

Everything below was measured on the machine above, on Avowed with a save loaded. The DLL was 3634 throughout: no DLL
code changed after it.

**What was built.**

- **D1** `[TRACE-UNLOADED-NAMES]`. Linie reads a function's identity (its FName, its class's FName, flags, parameter
  size) when it first sees the function, on the calling thread while the function is certainly alive, with up to
  three tries. After that it checks only a key on each call, the FName and the Outer. A key that changes means another
  function took the address: it is read again and marked **reused**. After Stop each function is classified Live,
  Unloaded (its object slot no longer holds it), Recycled (the slot holds another function) or Unnamed (never read).
  - `pe_profile_get` keeps unloaded rows when asked (`include_unloaded`) and counts them; `pe_trace_names` carries the
    marks.
  - A traced Start keeps only the ticks that still hold the function they named, says how many it left out
    (`ticked_dropped`), and refuses when none is left.
  - In the UI an unloaded row shows "(unloaded)" with no tick and no ASM, so its dead address never leaves the panel.
    The Call Trace summary always says what share of the calls has no name (`<0.01%` and `>99.99%` at the edges,
    never a false 0% or 100%). The JSONL / CSV export carries `func_unloaded` / `func_reused`.
- **D2** `[TRACE-UI-LOAD-MEMORY]`. Each page decodes straight into the load's window, and pages are 32,768 records.
  The trace on screen is let go of, and collected, before a new read. A non-compacting collection runs at least every
  16 pages, and more often as the free memory runs low (3638: the pages' garbage, about 5 MB a page, may take 1/32 of
  the free physical memory, read after every page). One compacting collection follows the build. The load's log
  line gives the memory at its start, the peak working set, its collections and their time, and what is left after.
- **D3**. Beside the slider: "Memory for a buffer that fills: the game N MB from Start; this UI up to 2.25 × N + 45 MB
  while it loads the trace, about N MB after". Since 3638 the line, its warning and its tooltip say the UI's figures
  are a reference measured on the developer's PC (the maintainer: another user's machine will differ). The free
  physical memory is read again when the slider moves, when Trace is ticked, when the tab is shown, when the
  experimental tabs change and at Start. Above it the line turns orange and Start's status warns; Start still runs.
- **D4**: 512 MB stays. **(4)**: a single call that began before the kept part has its own sentence.

**Review.** 16 findings, each put to a skeptic; 4 refuted (DLL-2, DLL-4, DLL-5, UI-2). The other 12 were fixed one
per commit, red before green, each test mutation-checked:
- the classifier compares the class's FName too;
- an address another function takes during the recording is read again and marked reused, also when the new
  occupant's class reloads;
- a traced Start checks its ticks;
- ShareText clamps after rounding;
- the free memory is read again on entering the tab and when the experimental tabs change;
- Ctrl+C on a Live Funcs row copies its template columns, the function's name included;
- the notes' wording ("no longer loaded when the trace was read");
- the measurements name their machine.

**Live: D1** (DLL and UI 3634; the maintainer's recipe: a save loaded, the inventory opened and closed).

| Where | What held |
|---|---|
| The rig, the inventory opened and closed twice | 11 / 11. 697 functions, **all named**: 393 live, 304 unloaded since they fired, carrying 5,748 calls (0.70%) |
| Live Funcs | "(unloaded)" on those rows, with no tick and no ASM; the status "unloaded since firing: 491"; Ctrl+C on a row copies its class and function |
| Call Trace, 512 MB | "345 of 689 functions were no longer loaded when the trace was read, and are named from their first call (0.48% of the calls). 0% of the calls have no name (0 of 689 functions)." |
| A Start with only an unloaded function ticked | Refused; the DLL logged "pe_profile_start: all 1 ticked functions are unloaded; refused" |

At 3633 the same game left 184 of 796 functions without a name (above, "Avowed").

**Live: the load's memory.** The peak is the working set over the load's start, sampled after each page. "After" is
measured after the compacting collection.

| Build | Ring | Load | Peak over the load's start | After |
|---|---|---|---|---|
| 3633 | 128 MB | 11.5 s | +958 MB | nothing given back |
| 3634 | 128 MB | 6.5 s | +940 MB | working set 727 MB |
| 3635 | 128 MB | 9.6 s | +555 MB | heap +432 MB |
| 3636 | 128 MB | 10.4 s | **+303 MB** | heap +156 MB, working set +109 MB |
| 3633 | 512 MB | 44.3 s | +2,936 MB | nothing given back (3,249 MB a minute later) |
| 3634 | 512 MB, the 128 MB trace still shown | 33.4 s | 3.77 GB working set at the peak | working set 1.09 GB |
| 3636 | 512 MB | 38.3 s | **+1,054 MB** (480 → 1,534 MB) | heap +517 MB, working set +333 MB |

D2's first cut (3634) left three causes, each found by measuring:
1. Every page's pipe line and parsed document stayed uncollected through a load of hundreds of pages. Fix: a
   collection every few pages (3635).
2. The trace on screen stayed alive beside the new load's window and columns, about 0.4 GB of 3634's 3.77 GB. Fix:
   let go of it and collect it before the read (3635).
3. StreamReader keeps its pooled line buffers per thread. 13 lines of 14 million chars (a page of 262,144 records as
   base64) kept 205 MB after a full collection; 104 lines of 1.75 million chars kept nothing. Fix: pages of 32,768
   records (3636).

A probe: the view model alone keeps 124 MB for 1.68 million calls (73 bytes a call), and a ListBox over 1.68 million
rows keeps nothing of its own. After a 512 MB load the heap keeps about 517 MB for 6.7 million calls, which is the
trace itself.

**The estimate.** At 3634, 2 × N + 45 described the load's structure, but measured from the load's start it fell 2 MB
short at 128 MB (301 against 303). 3637 uses 2.25 × N + 45: 189 MB at 64, 333 at 128 and 1,197 at 512, which is
10–14% above the two measured loads. From a freshly started UI the 512 MB run came to 1,206 MB. The 152 MB the UI
already held before that load (328 → 480 MB) is not the trace's. The figures are this machine's, and since 3638 the
UI says so.

**(3) The read's speed** `[TRACE-UI-READ-SPEED]` is still open. The 512 MB load took 38.3 s on 3636 (44.3 s on
3633, and 33.4 s on 3634 with pages eight times larger): about 19 MB/s for 716 MB, against the rig's 31–36 MB/s.

**Not yet seen live:** 3638's collection period that follows the free memory. This machine always had more than
2.5 GB free, so every load so far ran the calibrated 16 pages. 3638's load log line also gives the collections' time
in total; the next trace load reports both.

-----

## Step 2 design (decided 2026-10-07)

**Status: designed, not built.** It covers three things:
- section 2, parameter snapshots and view C;
- following a function by name (T10);
- the Call Trace list and detail-pane fixes ("Also built with step 2").

The maintainer's decisions are T10 to T14 in "Decisions". This section is what they settle. A "Step 2 built" section will follow it, as step 1's did. The build, item by item with each red test and
mutation, is the ledger [live-funcs-step2-items.md](live-funcs-step2-items.md).

### What is decided, and why

The maintainer's:
- **T10. Ticks and choices follow a function by name** (its class and function). A function unloaded at Start is never refused or dropped: it waits for its first call.
- **T11. A chosen call the trace would not record is recorded alone.** It is flagged lone and opens no scope. With nothing ticked and something chosen, the trace records only the chosen functions' calls, and T7 is not asked.
- **T12. The snapshot buffer has its own slider:** 8 to 128 MB, default 32, remembered. It is shown while something is chosen and counted in the game's memory figure.
- **T13. The estimate turns orange** when the busiest chosen function keeps less time than the trace buffer. A grey note says how many calls a second the budget will skip.
- **T14. One Snapshot checkbox.** Step 3 decides about stacks.

Derived from the code and the plan, and not asked:
- **Snapshots ride on the trace.** The same Start, Stop and release apply. Every snapshot points at a call in the ring.
- **One ring per chosen function, all in one allocation, each with the same number of slots K.** A per-frame function can lap only its own ring, never a rare function's.
- **A budget per function and in total, per second.** The first calls of each second are kept and the rest are counted as skipped. The defaults, 1,000 and 10,000 a second, are provisional and get measured live.
- **The DLL decodes, after Stop.** The raw bytes travel beside every value. Each value says whether it is exact, what an address holds now, gone, missing, a header only, or raw.
- **Choices are kept for the UI session**, by class and function, like ticks. They are cleared on disconnect and never saved.
- **A bulk "Snapshot shown rows" includes per-frame rows.** T9 left those out only for stacks. The rings and the budget contain them.
- **Any traced Start gives up the previous trace**, refused Starts included.

### How a function is followed by name

- **The key is four numbers:** the function's FName and its class's FName, each an index and a number. Live Funcs rows carry it. Unloaded rows get it from the function's first call. The numbers stay valid for the life of the game process.
- **At Start, the DLL checks each key against the names the UI shows.** A key that does not decode to them is refused, and refusing every key refuses the Start. Being unloaded is never a reason.
- **During the recording**, the table already reads each function at its first call. When the names match a choice or a tick, that address is armed, and the very call that armed it is traced.
- **A reload at a new address** is a new arm, with its own parameter layout. An address taken by another function is not armed.
- **Armed addresses get a stricter check on every call.** It adds the class's name, which costs two more reads, so a class swapped at the same addresses is caught. Other calls cost what they cost in step 1.
- **The parameter layout is read in the background** by a worker thread of the recording, which looks every
  50 ms, while the function is alive. The read checks the function before and after. Stop waits up to 2 s for any
  layout not read yet, then seals the rest as raw only. Not the pipe's thread, and not its disconnect monitor: the
  first read of an enum's names can walk every object for seconds.
- **A layout read once is reused** only for the same address with the same five numbers (the address's Outer and the
  four name numbers). A struct or enum the layout reads from a cache is checked against its own names first: under
  reload churn a freed struct's address can hold another.
- **A layout that could not be read leaves its calls as raw hex**, with the reason: unloaded first, replaced, or not read before Stop.
- **The report says "not called", never "not loaded".** The DLL sees calls, not loads.

### Data flow

1. **Live Funcs.** The rows give the key, the parameter size, the flags and per-frame. Choose and tick. The estimate line shows MB a minute, how many calls each ring keeps, and the orange warning.
2. **Start.** The request carries the trace bytes, the ticks by name, and the snapshots (by name, with sizes, buffer and budgets). The DLL frees the previous trace and checks the keys. It allocates the ring and the snapshot rings and touches their pages. Then the recording starts.
3. **Each call.** The table reads it, or checks its key, and hands the trace a small hint: armed, ticked, which ring. The entry record is written, then the entry copy. The game's call runs. The return record is written, then the after copy.
4. **Every 50 ms.** The worker reads the layouts of new arms.
5. **Stop.** The trace stops, then the recording. The DLL drains the layouts and seals them. The reply says which names were never called.
6. **Call Trace load.** It reads the pages, then the names with each native entry, then the layouts by arm, then the snapshots per ring. Then it releases. The game's memory goes back at load, as in step 1.

### Deviations, recorded as step 1 recorded TR5's

- **TR6** ("the entry record holds the snapshot's index"). The 40-byte record is the wire format and has no free field. Instead:
  - the entry record gains flag bits: taken, lone, over the budget, kept from the exclusion list;
  - each snapshot slot carries its call's entry sequence number, the same link a return record uses.
- **Copy size** (section 2 says "copy ParmsSize bytes").
  - ParmsSize is copied, capped at 2,048 bytes.
  - When it reads 0 (an offset not decided), 256 bytes are copied, and the decoder marks what lies past the copy.
- **The after-return copy** is taken only when the function has out parameters (the engine's flag for that, whose name is FUNC_HasOutParms), or when its flags could not be read. It always goes into a new slot, never a write-back.

### Limits

| Limit | Value |
|---|---|
| Snapshot buffer | 8 to 128 MB, a power of two |
| Copy per call | ≤ 2,048 bytes; 256 when the size is unknown |
| Slots per ring | K ≥ 8 (fewer refuses the Start; the estimate says so first) |
| Functions chosen | no cap on the count (T9) |
| Addresses armed per recording | 16,384 (about 1.3 MB); more are counted, not armed |
| Struct depth / leaves per function | 4 / 256 |
| Layout read | a worker thread per recording with choices, every 50 ms; Stop waits up to 2 s, then seals |
| Game memory at most | 512 MB ring + 128 MB snapshots + the arm log |

### Not captured, or not matched

- The tool's own drained invokes. A call that a game exception unwinds has no after copy.
- A function called from native code without ProcessEvent, such as a BlueprintNativeEvent of a native class.
- **Calls in the microseconds between the trace starting and the recording starting.** Nothing matches them by name.
- A function whose identity could not be read in three tries.
- A reload whose class or function name comes back with another number. It reports as "not called".
- **The per-frame exclusion is still by address**, so a per-frame function that reloads escapes it.
- Strings and containers are headers only, and their data is not followed. FText shows only whether it is empty.
- **Object names are "what is at that address now".**
- **A torn slot.** A writer stalled while its own ring laps: at least K/2 admitted calls, so about 4 ms at K = 8. This is the same accepted class as the ring.

### Live checks

Same rules as step 1:
- one injected game at a time;
- the binary under test rebuilt;
- a stale proxy refreshed;
- the rig committed before it runs.

**DumperTest58 Shipping, rebuilt with new probes:**
- one function with every parameter kind and an out, an in-out and a return;
- a return-only function;
- a host that calls it in scope, plus a timer that calls it alone;
- a per-frame probe dispatched through ProcessEvent;
- a function first called only after a later invoke.

The TraceNest chain stays as recorded.

**The rig (`livefuncs_snap_live.py`) checks:**
- the key and per-frame on rows;
- ticks by name with no address: roots equal rounds;
- a never-called tick accepted, and listed at Stop;
- an altered key refused, and the previous trace freed;
- every entry decoding to its round's values;
- the outs and the return after the call;
- the late function's layout read in the background, with its delay logged;
- the budget skipping as configured;
- the native entry;
- the release freeing everything.

Also re-run `livefuncs_trace_live.py`.

**UI walkthrough, on the AOT build (`-Mode Publish`):**
- the column hidden with the experimental tabs off;
- the per-frame mark;
- the estimate orange after a bulk choice;
- Start and Stop;
- markers, "Only calls with parameters", the Parameters tab, raw hex;
- columns and pane dragged and remembered;
- the Address setting followed;
- JSONL and params CSV.

**The fixture cannot reload a class**: its classes are native, and native classes never unload. The reload path
is unit-tested with fake readers, and Avowed is its live proof.

**Avowed, the acceptance case** (`steam.exe -applaunch <appid>`, load a save):
1. From the table only, while the rows show "(unloaded)", tick and choose inventory functions with parameters, plus one per-frame function.
2. Start. Open and close the inventory twice. Stop.
3. Pass if:
   - each chosen name was armed, its layouts were read and its calls decode;
   - whether its addresses changed between openings is recorded, not required: it depends on when the game's
     garbage collection unloads the widget;
   - skipped calls match the rate against the budget;
   - calls/s and fps match a run without snapshots.
4. Record:
   - how long each layout waited to be read;
   - the Start and load times;
   - the UI's memory;
   - the budget defaults, measured again;
   - the machine.

**UE4: not checked live** (the maintainer, 2026-10-07). DumperTest and DumperTest58 come first, then Avowed. UE4's
UObject headers -- 4.11 to 4.27, the standard one and both case-preserving shapes -- and its UProperty and FField
property models are covered by dll_core_test, since after the UE5 live checks what differs is the offsets. A
UE4 live check, if one is ever needed, is UE 4.27.

### Build order

Each item is red first, one per commit, with its test mutation-checked. Fern and Stark are compiled by no test target, so their items are proven by building the DLL target and by the rig.

1. **Linie:**
   - the name key and the arm rules;
   - arming at first sight;
   - arm upkeep (reload, reuse, the stricter check);
   - capacity;
   - layouts' handover, and freeing with the trace;
   - the scope by name;
   - then the snapshot rings, the entry copy, the after copy, lone and excluded calls, the budgets, faults and TR2, and the windows.
2. **Ubel:**
   - parameter kinds;
   - the name-key check;
   - the layout read (the UE4 slot included);
   - its enrichment;
   - the checked background read;
   - the two decoder items.
3. **Fixture and rig**, then **Fern and Stark:**
   - the row key;
   - the Start by name;
   - the snapshot Start;
   - Stark's hint;
   - the background pass and Stop;
   - the two new commands (the pipe count goes from 102 to 104);
   - the native entry.
4. **UI:**
   - models;
   - the bulk lane;
   - the tick set;
   - ticks by name;
   - the choice;
   - the estimate;
   - the bulk choice;
   - the view;
   - what the summaries say;
   - the list's columns;
   - the detail's addresses;
   - the snapshot load;
   - view C;
   - export.
5. **Docs, then the AOT publish and the live checks.**

## Step 2 built (build 3640, 2026-10-08)

Every item of the ledger [live-funcs-step2-items.md](live-funcs-step2-items.md) is closed, each with its red, its
green and the mutations its tests had to kill. Built in one unattended run, 2026-10-07 22:40 to 2026-10-08; U10 / U11
and U14 were built in separate worktrees and merged.

### What changed from the design, found while building and live

- **The copy after the call also covers a lone return value.** The design took it for out parameters only
  (FUNC_HasOutParms) or flags never read. Measured on DumperTest58: `SnapProbe_RetOnly`, an `int32` return and
  nothing else, has FunctionFlags `0x20401` -- no FUNC_HasOutParms -- so its return value was never copied. The
  identity read at a function's first call now takes `UFunction::ReturnValueOffset` beside ParmsSize, and the copy
  after the call is taken for out parameters, a return value, or flags never read (99647a1b). The UI's estimate cannot
  tell a lone return from flags, so its MB figure counts two slots a call for every function (an upper bound) and its
  "calls kept" half the slots (a lower bound).
- **UHT's parameter flags, measured:** a `const TArray<int32>&` carries CPF_OutParm | CPF_ConstParm (kind
  `const_ref`); a `const FString&` carries neither and is a plain input (`in`). A const-reference-only function
  (`SnapProbe_ConstRefOnly`) takes no copy after the call.
- **FName casing:** the name pool keeps the casing it saw first, so `SnapProbe_Call`'s `Round` renders `round`
  on DumperTest58. Names are matched without case wherever they are compared to source names (the rig).
- **A soft pointer to an actor** is its level's asset path plus a sub-path (`:PersistentLevel.<actor>`) held in an
  FString whose text is on the heap, not in the copy. Decoded without it, the actor's pointer read as its level's;
  the value now says the sub-path's length and that its text was not copied, marked Header (ce9e3c1d).
- **A delegate's label in a copy** goes through the one label function `[R7-B-04]` (b94b7609): B7 had called the
  ladder directly, and the C# pin that guards that rule had failed unnoticed until the suite was run.
- **The arms' layouts are read by a worker thread** per traced Start with choices (F4), as the second critic asked,
  not by MonitorLoop. Stop now stops the trace before the table, gives the worker 2 s, runs the last passes itself,
  then seals. A worker past the deadline is joined at the next Start, a release, the last disconnect or shutdown.
- **A DLL that predates names** answers a by-name Start without `trace.names`; the UI then stops that recording and
  releases its trace rather than trace something other than what was asked.

### Live, DumperTest58 Shipping (UE 5.8), 2026-10-07

The fixture's probes (README, "DumperTest58", parameter snapshots) were packaged at 22:54. `livefuncs_snap_live.py`:

| DLL | Checks | What it shows |
|---|---|---|
| 3638 (before step 2) | 7 / 11 | no `fname_key` on any row: the rest cannot run -- the rig's own red |
| F1-F3 built, Stark unchanged | K1's 3 fail | a tick by name opens nothing (0 roots), no snapshot taken |
| ce9e3c1d | **33 / 33** | below |

- **K1:** 16 roots in an 8 s recording, every one `SnapNest_Outer`; one in-scope `SnapProbe_Call` per root and 16 lone
  ones, every entry flagged taken.
- **F5:** all 32 entries decode to their round's values (numbers, bool, the enum by name, the FName with its Number,
  the string's and the array's headers with the right Num, the Anchor live, the vector and the struct's packed bits);
  all 32 after copies give `OutTwice = 2R`, `InOut = 100 + R`, `ReturnValue = 3R`, the In parameters blank; no
  orphans; no slot without its block.
- **F4:** a function first called right before Stop (`SnapLate_Call`, invoked through the pipe) had its layout read
  1 ms after its arm; Stop returned in 0.00-0.01 s. A choice never called is `not_called` in the Stop reply after its
  empty trace was released.
- **F3:** K computed by the rig with the UI's formula equals the DLL's (97,541 for 5 rings in 32 MB); 512 choices of
  2,048 B into 8 MB are refused naming the count; a 4 MB buffer is refused; nothing stays allocated after a refusal.
- **Budget:** at 30/s per ring the per-frame probe kept 270 lone calls and dropped 1,443 over the recording.
- **F6:** `SnapNest_Outer`'s native entry lies inside the game's module. A script function's empty entry is not
  checkable on a C++-only fixture.
- Step 1's `livefuncs_trace_live.py` on the same DLL: 18 / 18.

### Live, the UI (AOT build 3639, then 3640), DumperTest58 Shipping, 2026-10-08

Driven through the UI, the rig not attached:

- **Live Funcs:**
  - "(per frame)" shows on the per-frame rows.
  - The Params? box appears only on rows with parameters.
  - Ticking `SnapNest_Outer` and choosing two functions turns the estimate line orange (T13: the busiest choice
    keeps less time than the scoped trace).
  - The game figure reads 96 MB (the 64 MB trace and the 32 MB snapshot buffer).
  - The Stop note says "1634 copies kept; 0 calls over the budget had none".
  - (One choice landed on `AnimInstance::BlueprintThreadSafeUpdateAnimation`, a per-frame Blueprint event with a
    parameter, which only made the run wider.)
- **Call Trace:**
  - The summary reads "Traced inside 1 ticked function(s), followed by name … Parameters: 1600 calls with a copy,
    from 2 load(s)".
  - Calls with a copy carry the `(p)` marker.
  - "Only calls with parameters" lists the 1,600 calls with the filter box empty.
- **The Parameters tab:**
  - A lone call says it was recorded for its parameters only.
  - Every `SnapProbe_Call` input decodes, the struct's members nested.
  - The after-call column shows `OutTwice -1 ≠ 480`, `InOut 100 ≠ 340`, `ReturnValue — ≠ 720`, with the raw
    copies below.
- **The Call tab:**
  - It follows the Address setting, without and then with `0x`.
  - It labels the UFunction object's address as data, not code.
  - It gives the native entry as `"DumperTest58-Win64-Shipping.exe"+4804C50`.
  - It marks the Blueprint event as a script function: the case the C++-only fixture cannot give the rig.
- **Exports:**
  - The parameters CSV has 2,076 rows, its cells armoured and its marks written as words.
  - The JSONL header has `scoped`, `snapshots_only` and the budget counts, followed by one `snapshot_layout` line
    per arm.
- **Found and fixed (3640):** the column and pane drag handles had no template. Fluent 12.1.3 themes a Thumb only
  inside a ScrollBar or a Slider, so a bare Thumb drew nothing. Styled in the panel, a column was dragged live on
  3640, and its width came back after a restart. The drags tried on 3639 had missed the 5-pixel handles at the
  screen's scale, so they say nothing either way about hit-testing.

### Live, Avowed (UE 5.3 Shipping, the dxgi proxy refreshed to 3640), 2026-10-08

Launched with `steam.exe -applaunch`, the save continued, and 272,472 objects scanned. The inventory was opened and
closed (`I`, held: an instant press is missed) during both recordings of
`livefuncs_snap_live.py --choose Inventory Item Equip --plain-s 25 --record-s 35`: **7 / 7.**

- **The choice:** the plain recording found 64 inventory functions with parameters and a key (widgets such as
  `WBP_InventoryViewerListButton_C`, `InventoryItemStatWidget`, `WBP_TooltipBreakdownEntry_C`). A snapshots-only
  Start chose all 64 by name: none refused, Start 52 ms. K was 11,065 per ring in 32 MB.
- **The layouts:** all 64 arms were read while their functions were alive, 13 / 40 / 50 ms after arming (min /
  median / max). Stop took 0.01 s, and every followed name was called.
- **The decode:** 844 slots, all of them decoded, none raw. **32 of the 64 functions were unloaded by the time the
  trace was read, and their 369 copies decode**. This is T10's case: the inventory closed, and its calls stay named
  and readable.
- **A second run** (`--plain-s 25 --record-s 100 --per-ring 30`, the inventory opened twice inside the window,
  about a minute apart): **9 / 9.**
  - **A reload is a second arm:** 34 of the 64 names were armed twice, at new addresses. All 98 layouts were read
    2 / 17 / 48 ms after arming, and each arm's slots decode with its own layout.
  - 6,830 copies decoded, none raw. 742 of them belong to the 68 functions unloaded by read time.
  - **The budget:** `AlabamaLightingFixtureInterface::GetRadiusValue`, about 2,198 calls a second, kept about 26 a
    second against a budget of 30 and dropped 257,468 lone calls.
- **A third run, the cost** (`--choose "" --plain-s 20 --record-s 30`: the 60 busiest functions with parameters,
  the default budgets): **7 / 7.**
  - The game ran 9,224 calls/s with the snapshots against 9,260 without (-0.4%), and the fps overlay read 136-140
    against 136.
  - 148,210 copies in 30 s, all decoded.
  - The busiest choice (3,150 calls/s) kept about 1,021 a second against its budget of 1,000 and dropped 63,366.
  - Still owed (verification register): the UI's memory while it loads snapshots, and the default budgets re-weighed
    on a game busier than this one (the total of 10,000 a second was not reached).

## Step 3 design (2026-10-08)

**Status: designed, not built.** It covers native stack snapshots for chosen functions: view A (the call stack, each
frame as `module+RVA` with the function it falls in) and view D (to Cheat Engine) ship first; view B (the raw stack
copy) is deferred. The design is [live-funcs-step3-design.md](live-funcs-step3-design.md): three code maps, two
designs (reuse-first and risk-first), a merge and an adversarial critic, all from one design workflow. The build
ledger is [live-funcs-step3-items.md](live-funcs-step3-items.md).

In short:
- **Capture:** inside `Linie::TraceEnter`, at entry, through an injected capturer, the way `BytesCopier` is
  injected. The capturer is `Macht::CaptureCallerStack`:
  - it bound-checks the hook's return slot against the thread's stack and keeps 32 KB of headroom;
  - it walks with `RtlCaptureStackBackTrace` under `__try`;
  - it trims our own frames by searching for the game's return address.
  - Stark.cpp does not change.
- **Storage:**
  - stack rings sit after the parameter rings in step 2's one allocation (same K), with their own index space,
    budget words and entry flags (32 taken, 64 over the budget);
  - every step-2 reader stays parameters-only;
  - each capture is timed (the slot's ticks, per-ring time, the maximum).
- **Resolution, in the DLL after Stop:** module, RVA, the function start from `.pdata` at ret−1, unwind data present,
  our own frames and ProcessEvent labelled. The UI matches function starts against the trace's native entries.
- **Pipe:** `pe_snap_get` with `"kind":"stack"`; no new command.
- **UI:** a "Stack?" column, a "Call stack" tab in the Call Trace detail, and per frame "Copy CE address" and "Open in
  CE disassembler".
- **Decisions:** T15-T19 confirmed by the maintainer 2026-10-08 as recommended; T20 (a Low stack budget and the UI's warning) added the same day.
- **The critic's HIGH:** the build does not fit one hour; the ledger's order and the mutations to keep are revised in
  its review.

## Step 3 built (build 3641, 2026-10-08)

**Status: built; proven live on DumperTest58 Shipping (the stacks rig 31/31 with 7 recorded, the step-2 and step-1
rigs unchanged, the AOT walkthrough).** The ledger is [live-funcs-step3-items.md](live-funcs-step3-items.md), with
the per-item mutations, the builders' deviations and the live results ("8.0 Results").

- **Deviations from the design:**
  - FollowChain (the review's M4) was built before the live check, as S3-M3, so S3's `known` half is a check.
  - T20 (the maintainer): a Standard / Low budget and the warning in the UI (S3-U8).
  - The UI's deviations are listed at the ledger's end ("The UI items as built").
- **The cost (S6), the number T17 waited for:** about 20-28 µs a capture in the fixture's ~162 MB image on a Ryzen 9
  9955HX3D (median 20-22 µs, the rig's mean 28, max 179 µs), against 1.4-2.6 µs in the test exe. Depth barely matters.
  D3's rule gives a total of **50 a second**; the defaults stay at T17's 100 / 200 and T20's Low 50 / 100 until the
  maintainer decides (put to them 2026-10-08), and Avowed's measurement is a register row.
- **Not reached live:** the register's "Live Funcs step 3" batch (Avowed's cost, the once-a-second hitch, the rig's
  real-DLL mutations, the one 8.1 ms capture, T9.1 / T9.2).
- **Next:** S3-U6 (the per-frame ask-once), S3-U7 (the estimate line), S3-A1 (the native-entry index), S3-R2 (the PDB
  check); deferred: view B, stack export, `.pdata` prewarm, own-frame calibration.

