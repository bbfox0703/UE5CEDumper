# Live Funcs — call timeline and stack snapshots (feasibility) `[LIVEFUNCS-TIMELINE-2026-10-04]`

**Status: FEASIBILITY ONLY — nothing is built, and nothing here is decided until the maintainer says so.**
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

## The shape the maintainer proposed

One buffer of fixed size (32 MB or 64 MB), allocated at Start. Calls are appended until it is full; then
recording of the log stops (the count table can keep going). After Stop the UI shows it or exports it.

- Appending is one atomic `fetch_add` on the write index: no lock, and a full buffer is just "index ≥ capacity".
- Allocate at Start and free on Reset / client disconnect, as Linie's table already is: 64 MB sitting in the
  game process when nobody is recording is not acceptable.
- The buffer size is a slider, persisted with the trace's other settings — see "Decisions" below.

## 1. Call timeline — feasible, recommended first

**One record per call, about 40 bytes:** a high-resolution timestamp (the current `nowMs` is too coarse for a
timeline; this costs one more clock read per call), the UFunction, the calling object (`thisObj`), the thread id,
the **nesting depth**, and the duration, written back into the same record when the call returns (the same thread
writes both, so no lock). 64 MB holds about 1.6 million records, 32 MB about 0.8 million. How long that lasts depends on how often the
game calls `ProcessEvent`, which has not been measured: as an example only, at 50,000 calls per second 64 MB
lasts about half a minute.

**Nesting depth is the part worth having.** `ProcessEvent` is re-entered from inside UFunctions. A `thread_local`
counter raised before calling the original and lowered after it turns the flat log into a call tree: which
UFunction ran inside which. That is the "fuller call stack" asked for on PR 540, and it is cheap.

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
  Live Funcs table.

## 3. Native stack snapshots — feasible, chosen functions only

`RtlCaptureStackBackTrace` (or `RtlVirtualUnwind` over `.pdata`) gives the native return addresses above the
hook. It costs far more than a timeline record, so again **only for ticked functions**, with a fixed depth
(16 frames = 128 bytes).

### Several views, like Cheat Engine — or just a dump?

Recommendation: **give the views that only we can give, and hand everything else to Cheat Engine.**

| View | What it shows | Why we can do it better than a raw dump |
|---|---|---|
| **A. Call stack** | One row per frame: return address as `module+RVA`, plus the function it falls in | `.pdata` (`RtlLookupFunctionEntry`) gives each frame's exact function start without symbols. Match that start against every UFunction's native entry (`UFUNCTION_FUNC`) and the addresses Himmel already resolves (`ProcessEvent` and others), and a frame reads `Game.exe+1A2B3C  in  execTakeDamage (Character)`. A shipping game has no PDB, so CE would show only the offset. |
| **B. Raw stack, annotated** | A copy of N bytes at the stack pointer (e.g. 512 B, clamped to the thread's stack base), one qword per row, each shown as hex / int / float, with a guess of what it is: code (`module+RVA`), a live UObject (name and class), an FName, other heap | Same as CE's stack view, plus the UE meaning of a value, which CE cannot know. The stack is gone once the call returns, so this must be a copy taken at call time; the annotation is done after Stop and carries the same "may be stale" mark as above. |
| **C. Parameters** | The parameter block from section 2, decoded by name and type | CE has no idea where a UFunction's parameters are. |
| **D. To Cheat Engine** | "Open in CE disassembler" on a frame, and "copy as CE address" (`"Game-Win64-Shipping.exe"+1A2B3C`, quoted because of the hyphens) | Through the existing AOBMaker bridge (`NavigateDisassembler`). Disassembly, breakpoints and tracing stay CE's job; we do not rebuild them. |

Not planned: our own disassembler view, register views beyond the three arguments, and Blueprint's script stack
(`FFrame`), whose layout changes between UE versions — the nesting depth from section 1 already gives the
UFunction-level stack.

## Recommended order

1. **Timeline** with nesting depth and duration, fill-then-stop buffer, export to `.jsonl` / CSV. Then the call-tree
   view in the UI.
2. **Parameter snapshots** for ticked functions (view C).
3. **Native stack** for ticked functions: view A first, view D next to it, view B last.

## Measure before building

- How many `ProcessEvent` calls per second a busy game makes, to size the buffer and the default.
- What the extra clock read and record write cost per call, with and without recording (frame time on a fixture).
- What one stack capture costs, to decide how many ticked functions are reasonable.
- What a **128 MB** buffer costs after Stop, which decides whether 128 stays on the slider: moving it out of
  the game over the pipe, resolving its names, and loading about 3.3 million rows into the UI. The hot-path
  cost per call does not depend on the buffer size; what grows is the memory held in the game process and
  everything that happens after Stop.

## Decisions

| # | Item | Decision |
|---|---|---|
| T1 | **Buffer size** (maintainer, 2026-10-04) | A **slider: 32 / 64 / 128 MB, default 32**. Planned, not final: if measuring shows 128 MB is too heavy ("Measure before building"), the slider becomes **16 / 32 / 64 MB**, default still 32. Persisted across UI sessions, and disabled while a trace records, like the Live Funcs sliders. |
| T2 | **Where it lives** (proposal 2026-10-04, ⚠ awaiting the maintainer) | **Its own top-level tab** (working name "Call Trace"), not tabs nested inside Live Funcs. See below. |
| T3 | **How functions are chosen for snapshots** (proposal 2026-10-04, ⚠ awaiting the maintainer) | A **watch list in the Call Trace tab**. Live Funcs gets only a context-menu item "Add to Call Trace watch list"; the watch list can also take a function by name. No checkbox column in the Live Funcs table. |

### Why its own tab (T2, T3)

Choosing functions (sections 2 and 3) and viewing the result (section 1's tree, views A–C) are one workflow,
and it is not the Live Funcs workflow: Live Funcs ranks functions by count, the trace keeps every call in order.

- **The UI already splits related tools into sibling tabs** — Snapshot, SPC Query and Class Pivot each have
  one. No panel has a tab control inside it today, so tabs inside Live Funcs would be a new pattern and would
  hide the trace one level down.
- **Its own lifecycle:** its own Start / Stop, buffer slider, watch list and export. Inside Live Funcs, two
  Start buttons would need rules about which one the recording lock applies to.
- **Live Funcs stays as it is:** the PR 540 work (fetch limit, Min calls, Save .jsonl) is not mixed with this.
- **Proposed layout of the tab:** controls on top (buffer slider, Start / Stop, watch list, export); the call
  tree with durations as the main area; a detail pane for the selected call, with **its own tabs for views A,
  B and C** (call stack / stack copy / parameters). The tabs belong in the detail pane, where they switch
  between views of one call, not at the panel level.
- **Start it as an experimental tab**, shown only when the experimental tabs are enabled like Snapshot, until
  the hot-path cost is measured on real games.

Open: whether the count table and the trace may record at the same time. They share the hook, and recording
both costs both; to be settled with the measurements.
