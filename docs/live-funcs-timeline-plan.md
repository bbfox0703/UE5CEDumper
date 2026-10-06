# Live Funcs — call timeline and stack snapshots (feasibility) `[LIVEFUNCS-TIMELINE-2026-10-04]`

**Status: FEASIBILITY ONLY — nothing is built, and nothing here is decided until the maintainer says so.**
**Decided 2026-10-06 and 2026-10-07:** T1 (a ring buffer, 32–512 MB), T3, T4, T5, T6, T7 and T8 — see
"Decisions" — after a design review whose findings are TR1–TR7 below. **Step 1 (the timeline) is being built from
2026-10-07** (maintainer: "start"); the measurements below are taken as part of it.
Written 2026-10-04 from a reading of the code, not from a measurement: every size and rate below is arithmetic or
an example, and the section "Measure before building" lists what has to be measured first.

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
- Not measured yet: the maintainer deferred this list on 2026-10-06. Nothing is built before it is measured.

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
| T3 | **Viewing and choosing functions** (proposal 2026-10-04; **decided 2026-10-07**) | The trace is **viewed** in its own top-level tab (working name "Call Trace"). Functions are **ticked in the Live Funcs table** from an earlier recording; the next Start traces only inside them (T5 (a)) and, from step 2 on, snapshots them (sections 2 and 3). **With nothing ticked, the trace records every call** — the first proposal — after a confirmation (T7). |
| T4 | **Registers** (maintainer, 2026-10-06) | **Kept as planned: no register capture.** The maintainer first asked for GPR / XMM / YMM snapshots. At the hook only rcx, rdx and r8 (object, UFunction, parameter block) mean anything: the rest of the register file is the dispatcher's, and `ProcessEvent` takes no float arguments, so XMM / YMM hold leftovers. A UFunction's float and vector arguments are in the parameter block, decoded by name in view C. Registers matter inside the native implementation, which is Cheat Engine's debugger; view D hands the entry point over. A faithful capture would also need a MASM entry stub, because the C++ detour can change volatile registers before it reads them. |
| T5 | **Recording scope** (maintainer, 2026-10-06) | Two opt-in filters applied at record time (TR4), **(a) built first**. **(a) Ticked-function scope:** applies whenever functions are ticked (T3) — record only the ticked functions' calls and everything nested inside them on the same thread; tick OnJump, get the call tree under OnJump. **(b) Leave out per-frame functions:** a checkbox; the previous recording's per-frame list (`per_frame_funcs`, build 3630 on), passed at Start. Nothing ticked and (b) off records everything. One tick column serves (a) and the snapshots (maintainer, 2026-10-07). |
| T6 | **Experimental only** (maintainer, 2026-10-06) | The Trace checkbox, the buffer slider, the scope options, the tick column and the Call Trace tab show only while the experimental tabs are enabled (the System tab's checkbox, `ExperimentalGate`). The DLL never sees that flag: it allocates the buffer and records only when Start asks for the trace, so with the trace off its hot path is exactly today's. |
| T7 | **Nothing ticked** (maintainer, 2026-10-07) | A Start with Trace on and no function ticked asks first: nothing is ticked, so the trace records every call — a wide range, and more load on the game. Confirmed once, it does not ask again in the same UI session; a second Start with nothing ticked runs at once. Cancel leaves everything as it was and starts nothing. |
| T8 | **The ticks seen from the Call Trace tab** (maintainer, 2026-10-07) | One tick state, set in the Live Funcs table. The Call Trace tab shows a copy of the trace settings and of the ticked functions, grayed out and read-only, with a hint that they are changed in Live Funcs. |

### Why the trace rides on Live Funcs (T2, T3)

The usual way to use it is one workflow: record in Live Funcs, find the candidates, then look at the calls
in order. A trace started on its own would have nothing to point at.

- **It is a design choice, not a technical limit.** Both the count table and the trace hang off Stark's
  `ProcessEvent` hook, and neither needs the other to record. Tying them is chosen for the workflow.
- **One recording, one lock.** One Start / Stop means one "recording" state: the fetch limit, Min calls, Save
  .jsonl, the Trace checkbox, the buffer slider and the ticks are all disabled together while it runs. Two
  Start buttons would each need their own rules.
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
