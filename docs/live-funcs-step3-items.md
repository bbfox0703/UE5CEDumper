# Live Funcs step 3 — the build ledger `[LIVEFUNCS-TIMELINE-2026-10-04]` `[LIVEFUNCS-STEP3]`

The items that build step 3, in order, one red commit and one green commit each, every test mutation-checked. The design is the plan's "Step 3 design" ([live-funcs-timeline-plan.md](live-funcs-timeline-plan.md)). Written 2026-10-08 from a design workflow (three code maps, two designs, a merge). **Update an item's status in the same commit that closes it.** Two items may share a red and a green commit when they are built in one cycle (step 2's U5-U8 precedent); each keeps its own mutations.

Fern.cpp and Stark.cpp are compiled by no test target. Their items are proven by building the DLL target (`py tools/verify/build_dll.py --targets UE5Dumper`) and by the rig. Stark.cpp does not change in step 3. New dll_core_test blocks go right after the S7 block (dll_core_test.cpp:8392-8541) as "LIVEFUNCS-STEP3: …" and call no `Serie::InitUE4`; re-read the block-order rule at :1094-1100 first. Anchors (`file:line`) were right at `03c10fcb` and drift; re-read before relying on one.

⚠ **Read the design review first** -- [live-funcs-step3-design.md](live-funcs-step3-design.md), "Design review":
- **H1, the time box:** build S3-L1 and S3-L2 as one red / green cycle. Run the safety mutations only, and mark the
  rest "owed". The UI goes to helpers in a worktree. R1's red is by construction (3640 sends no `trace.stack`).
- **M1-M5 change these items:**
  - M1 (U4): the stack load pages like step 2's.
  - M2 (U5 case 5): `LastAsm` is `"140001234"`.
  - M3 (U1 / U2): `names.stacks` is `int?`, and the old-DLL guard uses it.
  - M4 (M2): FollowChain moves into M2, or `known` is "recorded, not failed".
  - M5 (M1): `noinline` on the capturers, and recursion that works after its call.
- **The LOW items L1-L11** are notes on the items they name.

**Status 2026-10-08 12:15: the DLL's capture, storage and budget are built (S3-M1, S3-M2, S3-L1, S3-L2); the pipe,
the rig and the UI are under way.** The design workflow (three
code maps, two designs, a merge, a critic) took an hour of the unattended window. S3-M1 went first because it needs
nothing else, it is the safety-critical part, and its bench gives T17 a number: **about 1.4 µs per 16-frame capture
from 20 deep** (Release, this PC). The next session starts at S3-L1 + S3-L2 as one cycle (H1), then S3-M1's case 8.

| # | Layer | Item | Depends on | Status |
|---|---|---|---|---|
| S3-X0 | docs | The plan's "Step 3 design" (decisions D1-D17; T15-T19 for the maintainer) and this ledger, with its docs/README.md row | — | ✅ this commit |
| S3-R1 | pipe | Rig `tools/verify/livefuncs_snap_live.py --stacks` (paths as arguments; committed before it runs) | S3-X0 | open (red: 3640 has no `trace.stack`) |
| S3-L1 | DLL | Linie: a stack choice armed by name (`stackRing` in ArmSpec / ArmHint, still 24 B / ArmSummary; the merge; ArmLocked before the parameters-only return); stack rings after the param rings in one allocation (own index space, same K); TraceConfig stack fields and constants; StackWrite through the injected capturer; TraceEnter's stack gate, lone and excluded for stack choices, flag 32; StackRings / CopyStacks; step-2 readers param rings only; TraceInfo.stack | — | ✅ red 4f7644b0 (with L2 and M1 case 8), green after it; 8 / 8 mutants killed |
| S3-L2 | DLL | Linie: the stack budget (own per-ring word, own total window), D10's drop rule, flag 64, skipped / dropped by kind, captures / spent / max ticks, the slot's ticks | S3-L1 | ✅ one cycle with S3-L1; 5 / 5 mutants killed |
| S3-M1 | DLL | Macht: CaptureCallerStack(Ex) — stack bounds and 32 KB headroom, the walk under `__try`, the anchor trim (AnchorIndex), Partial / Fault / More / BadSp / LowStack; through TraceEnter; bench line | S3-L1 | ✅ cases 1-7 + the capture bench (red 49dde439; 7 / 7 mutants killed); case 8 and the TraceEnter bench with S3-L1 (red 4f7644b0) |
| S3-M2 | DLL | Macht: DescribeCode (module base and UTF-8 leaf, function start at ret−1, unwind, own by `__ImageBase`) | — | ✅ red 9463e34a, green after it; 3 / 3 mutants killed. FollowChain landed as S3-M3 |
| S3-F1 | pipe | Fern: `trace.snapshots.stacks` at Start (keys, depth, budgets, capturer), refusal and snapOnly count stacks, `names.stacks`, `trace.stack` in TraceInfoToJson, `names[].stack` at Stop | S3-L2, S3-M1, S3-R1 | ◐ built (the DLL target builds; red by construction, H1); green is the rig's S0, live |
| S3-F2 | pipe | Fern: `pe_snap_get` `kind:"stack"` (CopyStacks under the lock; `rings`, items, per-page `sites` with module / rva / fn / unwind / own / known outside it; page cap; Tot poll); pipe-protocol.md subsection and flag rows 32 / 64; pipe count unchanged (104) | S3-F1, S3-M2 | ◐ built with S3-F1 (and L7: a 32K path, LeafUtf8, the sites counted in the page); green is the rig's S1-S6, live |
| S3-U1 | UI | Models and DumpService: `stacks` in the Start request (unchanged bytes without), TraceInfo.Stack and names.stacks parsed, PeStackGetAsync (`kind:"stack"`) with frames resolved to sites and CeModule by the UI's code page | §3 (wire frozen) | open |
| S3-U2 | UI | Live Funcs: the stack choice (key and Trace on, no parameters rule), Start options, T7 with stacks, the old-DLL guard, status and Stop notes, TraceStartKey's count, TraceGameMb, the K estimate with stack rings, refresh and disconnect | S3-U1 | open |
| S3-U3 | UI | Live Funcs view: Stack? column, the visibility loop, the snapshot row shown for either choice, the count, strings | S3-U2 | open |
| S3-U4 | UI | Call Trace: the stack load before the release, CallTraceStacks joined by entrySeq, no params load when `snap.rings == 0`, the Params(i) and export NotChosen fixes, the `(s)` marker, the Summary sentence | S3-U1 | open |
| S3-U5 | UI | Call Trace: the Call stack tab (view A rows and notes), Copy CE address and Open in CE disassembler (view D), ASM refused for an earlier connection | S3-U4 | open |
| S3-X1 | docs | Plan "Step 3 built" (deviations, measured µs and max per capture, the defaults re-weighed by D3's rule), the dev-log entry, verification-register rows (Avowed stack cost; T9.1 / T9.2 owed per D2; M3), todo rows for §6's deferred items and for folding Genau's module helpers into Macht; comment pass (`comment_impact.py --staged`) | S3-F2, S3-U5 | open |
| S3-X2 | docs | `-Mode Publish` (AOT, size and SHA, the build bumped), proxy refresh, S3-R1 green on DumperTest58 Shipping, the step-2 rigs re-run, the UI walkthrough; µs per capture, max, calls/s and fps with and without stacks, the machine recorded | S3-X1, S3-R1 | open |
| S3-U6 | UI | (next) The per-frame ask-once for stacks (T9.2): `ConfirmStackPerFrame`, `_stackPerFrameConfirmed`, an async ToggleStack that re-raises `IsStackChosen` when refused | S3-U2 | next |
| S3-U7 | UI | (next) The stack estimate line (T9.1): pure `EstimateStacks(rates, perFunc, total, usPerCapture)` → ms/s; orange above 2.0; µs measured from the last Stop, else 10 "assumed" | S3-U2, S3-X2 | next |
| S3-A1 | DLL | (next) The native-entry index: one GObjects pass (class-pointer memo, Function / DelegateFunction / SparseDelegateFunction), `Func → ufunc` sorted, cached per gen; sites gain `ufunc` / `class` / `func` / `shared` | S3-F2 | next |
| S3-M3 | DLL | Chained unwind: a pure `FollowChain(imageBase, begin, unwindData)` tested on a synthetic UNWIND_INFO; DescribeCode names a fragment's primary function | S3-M2 | ✅ red b8681485, green after it; 4 / 4 mutants killed (built before the live check: the review's M4) |
| S3-R2 | pipe | (next) Rig `--pdb`: dbghelp through ctypes names each `fn_rva` against the fixture's shipped PDB, checking displacement 0 | S3-R1 | next |
| S3-B1 / S3-E1 / S3-P1 / S3-O1 | DLL / UI | (deferred) View B; stack export; `.pdata` prewarm; own-frame calibration | S3-X2 | deferred |

-----

## S3-X0 — The plan's "Step 3 design" and this ledger

- **Red:** none (docs).
- **Note:**
  - Committed first, so every later commit cites an item. Main may commit S3-L1's red before it lands, because the IDs are fixed here.
  - The plan section carries D1-D17, §4.3's list, and the time box's minimum / next / deferred split.
  - The Decisions table gains T15-T19 (D1-D5), marked "built as proposed, to confirm".
- **Files:** `docs/live-funcs-timeline-plan.md`, `docs/live-funcs-step3-items.md`, `docs/README.md`

## S3-R1 — Rig: livefuncs_snap_live.py --stacks

- **Red:** run against the deployed 3640 DLL on DumperTest58 Shipping. No `trace.stack` in the Start reply, so S0 fails and S1-S6 cannot run (the rig's own red).
- **Note:**
  - Paths are arguments (the gate rule). The PDB is not required (S3-R2).
  - New options: `--stacks`, `--stack-per-ring` (default 30), `--stack-total` (default 200).
  - K is computed with §2.2's formula, stack terms included.
  - The checks are §8's S0-S7.
- **Mutation:** n/a (rig). Its asserts are proven by the 3640 red, and by S3-F1 / S3-F2's mutation runs.
- **Files:** `tools/verify/livefuncs_snap_live.py`

## S3-L1 — Linie: a stack choice armed by name, and its ring's slot taken at entry

- **Red:** a new block "LIVEFUNCS-STEP3: a stack choice armed by name, its ring after the parameters', its slot at entry". It uses the stub reader of the N1 block (dll_core_test.cpp:7385). `StubCap(sp, out, max, fl)` writes `out[j] = sp + j` for `j < min(3, max)`, sets `fl = 0`, records `max` in a global, and returns the count.
  1. ArmState {key {0xC,0,7,0}, stackRing 0}, log capacity 0, gen 5, then StartRecording. `RecordCall(0xC, t, &h)` gives `h.gen == 5`, `h.stackRing == 0`, `h.ring == -1`, arm-log size 0, and `ArmsSummary()[0].stackRing == 0`.
  2. One key given as tick, as params (ring 0, cap 64) and as a stack (stackRing 0) merges into one spec: the hint has kArmTick, `ring == 0`, `stackRing == 0`.
  3. Arm-log capacity 1, used by another key: the both-chosen key still has `stackRing == 0`, with `armsFull == 1`.
  4. `sizeof(ArmHint) == 24`; `StackRingCap(0) == 8`, `(16) == 128`, `(100) == 496`.
  5. cfg: 1 param ring (cap 16), `stackRings = 1`, `stackDepth = 4`, `snapBytes = 128 + 8 × 96`, `stackCapturer = StubCap`. GetTraceInfo gives: `snap.rings == 1`, `snap.slotsPerRing == 8`, `snap.allocated`, `stack.rings == 1`, `stack.depth == 4`.
  6. In scope, `TraceEnter(0xF0, sp 900, hint{gen, ring 0, stackRing 0, copy 16}, params = &buf)`:
     - the record has `2|32`;
     - `CopySnaps(0)` gives 1 slot;
     - `CopyStacks(0)` gives 1 slot: entrySeq == the record's seq, `len == 24`, frames {900, 901, 902}, flags 0;
     - the stub saw `max == 4`;
     - `SnapRings` lists 1 ring (cap 16); `StackRings` lists 1 (index 0, cap 32); `CopySnaps(1)` is false.
  7. Scoped, no ticks, `hint{stackRing 0, ring -1}`: the record has `Lone|StackTaken`; a nested 0xF2 at sp 800 is not recorded.
  8. `exclude = {0xF0}` inside an open scope, stack-only: `Excluded|StackTaken`.
  9. `hint.stackRing` 5 with 1 stack ring: no stack slot and no 32. Lone, it writes no record.
  10. `stackCapturer = nullptr` gives a slot with len 0, flags 0x8000. A capturer returning 0 with flag 8 gives len 0, flags 8.
  11. A capturer that throws: no visible slot, a record without 32, the in-flight count back at 0, StopTrace quiesces. This is the throwing-copier pattern of dll_core_test.cpp:6972-6990.
  12. With no stacks chosen, the S2 pinned step-1 record script (the dll_core_test.cpp:7928 block) is unchanged.
- **Mutation:**
  - Set stackRing after `if (it->ring < 0) return;` (case 1 fails).
  - Index `snapRings[sring]` instead of `[snapCount + sring]` (case 6: the param ring takes the frames).
  - Set `snapCount = P + S` (case 6: SnapRings lists 2).
  - Gate `lone` on `ring >= 0` alone (case 7 fails).
  - Pass `r.cap` as max (case 6: the stub sees 32).
- **Note:**
  - `kTraceStackTaken` is set only after StackWrite returns, so a capturer that throws leaves no claim.
  - The 4/8 flag comments are updated.
- **Files:** `dll/src/Linie.h`, `dll/src/Linie.cpp`, `dll/tests/dll_core_test.cpp`

- **Done 2026-10-08 (one cycle with S3-L2, red 4f7644b0):** cases 1-11 pass; case 12 is a guard (the step-1 and
  step-2 blocks run unchanged). Mutants, all killed: stackRing after the parameters-only return; the stack ring
  indexed without `snapCount +`; SnapRings over P+S rings (L3's replacement for "snapCount = P+S"); lone gated on a
  parameter ring alone; the slot's bytes as `max`; flag 32 before the walk returned; the slot's number written first;
  ArmsSummary without the stack ring.

## S3-L2 — Linie: the stack budget apart from the parameters', and its cost

- **Red:** the S5 test-clock pattern (the dll_core_test.cpp:8129 block), with a clock advancing 7 ticks per read, all inside one second. `stackPerRingPerSec` 2, `stackTotalPerSec` 3, param budgets 1000 (case 4: 1).
  1. Five in-scope calls of a stack-chosen key: 2 records with 32 and 3 with 64, none with 16. `stack.skippedBudget == 3`, `stack.droppedBudget == 0`, `snap.skippedBudget == 0`.
  2. A lone stack-only call over the budget: no record; `stack.droppedBudget == 1`.
  3. Lone, chosen for both, params admitted and the stack refused: the record is `4|2|64`, the param slot is written, stack skipped +1 and not dropped.
  4. Lone, chosen for both, with the param budget at 1 and spent: the params are refused, the stack is taken. The record is `4|16|32`, `snap.skippedBudget` +1 and not dropped.
  5. A second stack ring with room of its own is refused by the total of 3.
  6. A clock jump of `QpcFreq()` admits again.
  7. `stack.captures == 3` (the stack rings' `next`), `stack.spentTicks == 7 × 3`, `stack.maxTicks == 7`, and `CopyStacks(0)[k].ticks == 7`.
- **Mutation:**
  - StackAdmit uses `snapPerRing` (case 1 gives 5 slots).
  - StackAdmit shares `snapTotalWindow` (case 5 is admitted).
  - Drop the record when the params were refused even though the stack was taken (case 4 fails).
  - Sum the stack rings into `snap` in InfoLocked (case 1's `snap.skippedBudget == 0` fails).
- **Files:** `dll/src/Linie.h`, `dll/src/Linie.cpp`, `dll/tests/dll_core_test.cpp`
- **Done 2026-10-08:** cases 1-7 pass. Mutants, all killed: StackAdmit with the parameters' per-ring budget;
  StackAdmit with `snapTotalWindow` / `snapTotal` (L4's wording); D10 dropping a call whose stack was taken; the stack
  rings summed into `snap`; the dearest capture never recorded.

## S3-M1 — Macht: the caller's stack from the hook's return slot, safely

- **Red:** a new block "LIVEFUNCS-STEP3: a native stack from the hook's return slot", after S3-L2's.
  1. `AnchorIndex({5,6,7,8}, 4, 7, 3) == 2`; `(…, 8, 3) == 4` (outside the window); `(…, 9, 13) == 4`.
  2. A `__declspec(noinline)` `Outer` calls a noinline `CaptureFromHere`, which passes `(uintptr_t)_AddressOfReturnAddress()` to `CaptureCallerStack(sp, out, 16, fl)`:
     - `out[0] == (uint64_t)_ReturnAddress()` taken in CaptureFromHere;
     - `out[1] == Outer's _ReturnAddress()`;
     - `fl == 0`, `n >= 2`.
     Both helpers use the result after the call, so there is no tail call.
  3. A 40-deep noinline recursion with max 16: `n == 16` and More. Max 62 from the test's shallow depth: no More.
  4. sp = 1000, sp = a stack address + 1, and sp = an address below the capturer's frame: BadSp, n 0.
  5. `CaptureCallerStackEx(..., headroom = 1ull << 40, &RtlCaptureStackBackTrace)`: LowStack, n 0.
  6. A walker stub that returns frames without the anchor, with sp = the address of a local holding 0x1234: n 1, `out[0] == 0x1234`, Partial.
  7. A walker stub that writes through nullptr: Fault, n 0, and the process is alive.
  8. Through TraceEnter (needs S3-L1):
     - `stackCapturer = &Macht::CaptureCallerStack`, depth 4, scoped stack-only;
     - a noinline helper 20 deep calls `TraceEnter(..., sp = _AddressOfReturnAddress(), hint{stackRing 0})`;
     - `CopyStacks(0)[0].frames[0] == helper's _ReturnAddress()`, `len == 32`, flag More.
  9. Bench (printed, not asserted; Release), under the CPU line (dll_core_test.cpp:7033-7046):
     - 65,536 captures of 16 frames from 20 deep, in ns per capture;
     - TraceEnter with and without a stack choice, budgets at the 24-bit max.
- **Mutation:**
  - Copy from `raw[0]` (case 2: `out[0]` lands inside the capturer).
  - Drop the More test (case 3).
  - Drop the bound test (case 4 reads 1000 inside the `__try`: Fault, not BadSp).
  - Drop the `__try` (case 7 kills the test run).
- **Note:**
  - `static_assert(Linie::kStackMaxDepth + Macht::kStackOwnSlack + 1 <= Macht::kStackRawFrames)` goes in the block, and again in Fern.cpp.
  - CaptureCallerStackEx holds no C++ object (C2712 under the test's /EHsc).
- **Files:** `dll/src/Macht.h`, `dll/src/Macht.cpp`, `dll/tests/dll_core_test.cpp`
- **Done 2026-10-08 (cases 1-7):**
  - Bench: **1,413 ns per 16-frame capture from 20 deep** (65,536 runs, Release). At T17's proposed 200 a second in
    all, that is about 0.3 ms of one core a second; the per-function 100 a second is about 0.14 ms.
  - Mutants, all killed: copy from `raw[0]`; no More; no bound test; the `__try` that does not catch (the run dies,
    0xC0000005); no headroom test; the anchor window ignored; Partial keeps nothing.
  - ⚠ **The red's helpers had become tail calls**, which is the review's M5 in the test itself. `return n + 0 * x` and
    `n + (x & 0)` fold to `return n`, so `S3Outer` compiled to a `jmp`, its frame was gone, and case 2 saw `main`'s
    caller where `S3Outer`'s return address should be. The helpers now store the callee's result to a volatile after
    the call. Plain arithmetic is never a guard against a tail call; a store or a call is.
  - Owed: case 8 (through TraceEnter) and the TraceEnter bench, both after S3-L1; the `kStackMaxDepth` static_assert
    lands with S3-L1's constant.
- **Done 2026-10-08 (case 8, with S3-L1):** through TraceEnter with Macht's capturer and a 20-deep caller, the first
  frame is the hooked frame's caller, four kept, flag More. The TraceEnter bench, measured while the UI helpers'
  `dotnet test` runs loaded the machine: about 3.3 µs per hooked call with a 16-frame stack against about 0.12 µs
  without, and 2.6 µs per bare capture (1.4 µs on the idle machine at 06:26). The live check (S6) gives the number
  that counts.

## S3-M2 — Macht: what a return address is

- **Red:** in S3-M1's block (shared red and green commits).
  1. For case 2's return address into `Outer`:
     - `moduleBase == (uintptr_t)GetModuleHandleW(nullptr)`;
     - the leaf equals the test exe's file name, ignoring case;
     - `own` and `unwind`;
     - `fnBegin` == Outer's start: `(uintptr_t)&Outer`, following a leading `E9 rel32` when the build is incremental.
  2. `DescribeCode(start of Outer).fnBegin != start of Outer` (the ret−1 rule; the byte before is padding or the previous function).
  3. `GetProcAddress(ntdll, "RtlCaptureStackBackTrace") + 1`: leaf `ntdll.dll` (ignoring case), `own == false`.
  4. A heap address and 0x1000: false, module "", `unwind == false`, no fault.
- **Mutation:**
  - Drop the −1 (case 2 fails).
  - Compute `own` by the leaf name "UE5Dumper.dll" (case 1 fails: the test exe is `__ImageBase`).
- **Files:** `dll/src/Macht.h`, `dll/src/Macht.cpp`, `dll/tests/dll_core_test.cpp`
- **Done 2026-10-08:** cases 1-4 pass (1026 checks). Mutants, all killed: drop the −1; `own` by the leaf name; the
  full path instead of the leaf (cases 1 and 3 then name `...\dll_core_test.exe` and `C:\WINDOWS\SYSTEM32\ntdll.dll`).
  - ~~Owed: FollowChain~~ (the review's M4): built as S3-M3 before the live check, so DescribeCode names a chained
    fragment's primary function and S3's `known: "process_event"` half is a real check.

## S3-M3 — Macht: a chained fragment's primary function

- **Red:** in S3-M1's block, on a synthetic image: a fragment whose 3 unwind codes are padded to 4 names its primary
  (0x800); two links; an indirect entry (UnwindData bit 0); an entry that chains nowhere and a chain that loops are
  guards (the loop ends after `kChainMaxHops`).
- **Green:** `FollowChain`; DescribeCode takes its function start from `PrimaryFunctionStart` (the .pdata lookup, then
  the chain, under `__try`: a dynamic function table hands back memory nobody vouches for).
- **Mutation, all killed:** the codes not padded (the run dies on a wild read); UNW_FLAG_CHAININFO ignored; an
  indirect entry read as unwind info; and S3-M2's ret−1 again, now through `PrimaryFunctionStart`.
- **Files:** `dll/src/Macht.h`, `dll/src/Macht.cpp`, `dll/tests/dll_core_test.cpp`

## S3-F1 — Fern: stacks at Start; trace.stack; names.stacks; names[].stack

- **Red:** S3-R1's S0 on 3640 (no `trace.stack`).
- **Green:** the DLL target builds, and S0 passes on this build:
  - a stacks-only Start (no ticks, `funcs: []`) is accepted with `snap_only` true;
  - `names.stacks == 2`, `stack.rings == 2`, `depth == 16`, the budgets as sent and clamped;
  - `snap.rings == 0`, and `slots_per_ring` equals the rig's K;
  - an altered stack key alone is refused, naming it;
  - the Stop reply's `names[]` carry `stack: true`.
- **Mutation (rig; run if the clock allows, else marked owed):** leave `stackOk` out of the refusal sum. The stacks-only Start is then refused and S0 fails.
- **Files:** `dll/src/Fern.cpp`

## S3-F2 — Fern: pe_snap_get kind stack; pipe-protocol

- **Red:** S3-R1's S1-S5 on 3640 (they cannot run there).
- **Green:**
  - the DLL target builds;
  - S1-S6 pass on this build;
  - `py tools/check_derived_counts.py` stays OK at 104;
  - `py tools/check_all.py` is green (derive the gate count from its own "N gate(s) run" line).
- **Mutation (rig; owed if late):**
  - Ignore `kind`: the reply carries parameter items without `frames`, and S1 fails.
  - Compare `known` with the trampoline instead of `Stark::HookedAddress()`: S3 fails.
- **Files:** `dll/src/Fern.cpp`, `docs/pipe-protocol.md`

## S3-U1 — Models and DumpService

- **Red:** DumpServiceTests (MockPipeClient):
  1. A Start with stacks serializes `snapshots.stacks {funcs [{class, func, keys}], depth 16, per_ring_per_s, total_per_s}`, with no `parms_size` in a stack item, and `snapshots.funcs: []` for stacks only.
  2. A Start without stacks is byte for byte today's (:1910-1929).
  3. `trace.stack` and `names.stacks` parse; their absence reads null / 0.
  4. `PeStackGetAsync` sends `kind:"stack"` with gen / ring / from / max. It maps `frames` indices to `sites` (own, known, unwind, the absent `fn`, the empty module), and `CeModule` comes from a fake code page that marks its output.
- **Mutation:**
  - Omit `kind` from the request (case 4).
  - Index frames by item instead of by site (case 4).
  - `CeModule = Module` (case 4).
- **Files:** `ui/UE5DumpUI/Models/ParamSnapshotModels.cs`, `ui/UE5DumpUI/Models/CallTraceModels.cs`, `ui/UE5DumpUI/Core/IDumpService.cs`, `ui/UE5DumpUI/Services/DumpService.cs`, `ui/UE5DumpUI.Tests/DumpServiceTests.cs`

## S3-U2 — Live Funcs: the stack choice

- **Red:** LiveFuncsSnapshotTests. Its FakeDumpService (:24-52) echoes `Stack` and `Names.Stacks` from `trace.Snapshots.Stacks`, with a switch for "an old DLL".
  1. A keyed row with NumParms 0 and nonzero flags can be chosen for a stack but not for parameters.
  2. ToggleStack is refused with Trace off, while recording, on keyless rows, and on rows from an earlier connection.
  3. A stack-only Start does not ask T7, and sends `Snapshots{Funcs = [], Stacks}`.
  4. A reply without `Stack` stops, releases, and says `str.LF.Stack.NotArmed`.
  5. The status carries the stack sentence, and the start key counts the distinct names.
  6. The Stop note has the stack sentence with µs from `spent_ticks / captures / qpc_freq`.
  7. TraceGameMb counts the buffer for stacks only.
  8. `EstimateSnapshots(…, stackRings: 1)` for 1 param ring of 64 B in 32 MB gives K == 139,809, the DLL's formula.
  9. A disconnect clears the choices.
  10. The Linie pin (the :163-171 pattern) reads `kStackDefaultDepth` 16, `kStackDefaultPerRingPerSec` 100 and `kStackDefaultTotalPerSec` 200 from Linie.h.
- **Mutation:**
  - Leave `_stackChosen` out of T7's condition (:692) (case 3 asks).
  - Apply `CanChooseSnapshot` to stacks (case 1).
  - Build `Snapshots` only when `_snapChosen` is non-empty (case 3).
- **Files:** `ui/UE5DumpUI/Models/PeProfileResult.cs`, `ui/UE5DumpUI/ViewModels/LiveFuncsViewModel.cs`, `ui/UE5DumpUI/Resources/Strings/en.axaml`, `ui/UE5DumpUI.Tests/LiveFuncsSnapshotTests.cs`

## S3-U3 — Live Funcs view

- **Red:** a source pin (the LiveFuncsSnapshotTests :307-321 pattern):
  - the Stack? header is in `ApplyTickColumnVisibility`;
  - the column has `CanUserSort="False"`, no SortMemberPath, `IsVisible="False"`, and binds `IsStackChosen` OneWay, `ToggleStackCommand`, `CanSnapshot` and `CanChooseStack`;
  - the snapshot row's visibility includes `HasStackChoices`.
- **Mutation:** leave the header out of the loop.
- **Files:** `ui/UE5DumpUI/Views/LiveFuncsPanel.axaml`, `ui/UE5DumpUI/Views/LiveFuncsPanel.axaml.cs`, `ui/UE5DumpUI/Resources/Strings/en.axaml`, `ui/UE5DumpUI.Tests/LiveFuncsSnapshotTests.cs`

## S3-U4 — Call Trace: load, join, and the NotChosen fixes

- **Red:** CallTraceViewModelTests. A `DumpWithSnapshots` variant (:191-205) adds `Stack` info. The fake's `PeStackGetAsync` serves `rings` and one slot a page.
  1. A stack slot is joined by entrySeq into `Stacks.StackOf`, and not into `Snapshots.Has` / `CallsWithParams`; the row shows `(s)`.
  2. A stacks-only trace (`snap.rings == 0`, no arms) loads its stacks and never calls `PeSnapLayoutsAsync`.
  3. A stale page, or a gen that changed mid-load, gives Changed.
  4. A lone stack-only call's Params tab says NotChosen, not Overwritten, and its export has no `snapshot` field.
  5. Summary carries the stack sentence.
- **Mutation:**
  - Route stack slots into CallTraceSnapshots (case 1).
  - Keep `Lone` in the Params "chosen" test (case 4).
  - Keep `SnapLoneFlag` in the export gate (case 4).
- **Files:** `ui/UE5DumpUI/Models/CallTraceSnapshots.cs`, `ui/UE5DumpUI/Models/CallTrace.cs`, `ui/UE5DumpUI/ViewModels/CallTraceViewModel.cs`, `ui/UE5DumpUI/Helpers/CallTraceExport.cs`, `ui/UE5DumpUI/Views/CallTracePanel.axaml`, `ui/UE5DumpUI/Resources/Strings/en.axaml`, `ui/UE5DumpUI.Tests/CallTraceViewModelTests.cs`

## S3-U5 — Call stack tab (view A) and to Cheat Engine (view D)

- **Red:** DetailDump / DetailVm (:889-930, "Game.exe" at 0x140000000) with a stack slot:
  1. A game frame in ModuleOffset reads `"Game.exe"+1234` through `CeModule`. An `ntdll.dll` frame reads `"ntdll.dll"+50`. A frame with no module, and the hex settings, read absolute hex.
  2. Labels follow the precedence: own → hook; process_event; `fn ==` a traced native's `CodeAddr` → "native entry of Class::Func +0x…" ("and 1 others" with two); "into"; no unwind.
  3. Notes for 64, 32 without a slot, Lone, Partial, More, LowStack, the µs cost, and "may be wrong" after an `unwind:false` frame.
  4. Copy puts `"Game.exe"+1234` on `MockPlatformService.LastClipboard` (AobUsageServiceTests.cs:13) under every Address setting.
  5. ASM through `ScriptedAobMakerBridge` (AobMakerActionsTests.cs:441, as `new AobMakerStatus(bridge)` to MakeVm's LiveFuncs, :161-166) gives `LastAsm == "0x140001234"`.
  6. After `ClearOnDisconnect`, ASM is refused with `str.CT.Stack.AsmOldTrace` and the bridge is not called.
- **Mutation:**
  - Format with `Module` instead of `CeModule` (case 1).
  - Skip the earlier-connection guard (case 6).
  - Copy follows the Address setting (case 4).
- **Files:** `ui/UE5DumpUI/ViewModels/CallTraceViewModel.cs`, `ui/UE5DumpUI/Views/CallTracePanel.axaml`, `ui/UE5DumpUI/Resources/Strings/en.axaml`, `ui/UE5DumpUI.Tests/CallTraceViewModelTests.cs`

## S3-X1 — Plan 'Step 3 built', dev-log, register and todo rows, comment pass

- **Red:** none (docs).
- **Note:**
  - Deviations found while building.
  - The measured mean and max µs per capture, and the defaults re-weighed by D3's rule (or why not).
  - The dev-log entry for the published build.
  - Register rows: Avowed stack cost; T9.1 / T9.2 owed (D2); M3's hitch watch.
  - Todo rows for every deferred item, and for folding Genau's module helpers into Macht.
  - `py tools/verify/comment_impact.py --staged`.
- **Files:** `docs/live-funcs-timeline-plan.md`, `docs/dev-log.md`, `docs/verification-register.md`, `docs/todo.md`, `docs/live-funcs-step3-items.md`

## S3-X2 — AOT publish and live checks

- **Red:** the walkthrough's first check (a Stack? column) fails on 3640. S3-R1 is red on 3640.
- **Green:**
  - `build.ps1 -Mode Publish`: the exe is about 54 MB, and the SHA is recorded.
  - `proxy_refresh.py refresh` on DumperTest58.
  - S3-R1 S0-S7 green on that build's DLL.
  - The UI walkthrough (§8) in the same game session.
  - One injected game; the game, CE and the UI are killed afterwards.
- **Mutation:** hand over the non-trimmed build (the size / SHA check fails).
- **Files:** `docs/live-funcs-timeline-plan.md`, `docs/verification-register.md`, `docs/dev-log.md`, `docs/live-funcs-step3-items.md`

(S3-U6, S3-U7, S3-A1, S3-M3, S3-R2 are sectioned when they are started. Each has its red / mutation in the table's item text and in §6.)

---

## 8. Live checks

### 8.1 DumperTest58 Shipping (UE 5.8): rig additions, no repackage

The fixture's paths (DumperTest58Actor.cpp:224-233) give two kinds of call:
- **In scope:** the timer → `SnapNest_Fire` → `TraceNest_Dispatch` → ProcessEvent(`SnapNest_Outer`) [outer hook] → `UFunction::Invoke` → `execSnapNest_Outer` → `SnapProbe_Dispatch` → ProcessEvent(`SnapProbe_Call`).
- **Lone:** `SnapNest_Fire` → `SnapProbe_Dispatch` → ProcessEvent(`SnapProbe_Call`), with no ProcessEvent above.

**The run:** `livefuncs_snap_live.py --stacks --stack-per-ring 30`:
1. the plain recording;
2. a Start with `ticked_names: SnapNest_Outer`, `snapshots {bytes 32 MB, funcs: [SnapProbe_Call], stacks {funcs: [SnapProbe_Call, SnapProbe_PerFrame], depth 16, per_ring_per_s 30, total_per_s 200}}`;
3. record 8 s, Stop, read the trace, the names, the params and the stacks, then release;
4. then a second, stacks-only Start (no ticks, `funcs: []`, SnapProbe_Call) for S0's second half.

| Check | Pass condition |
|---|---|
| S0 | **Echo and refusals.** `names.stacks == 2`, `stack.rings == 2`, `depth 16`, the budgets echoed; K equals the formula with stack terms. The stacks-only Start is accepted with `snap_only`, `snap.rings == 0`, and records only lone calls flagged `4\|32`. An altered key alone is refused. Red on 3640. |
| S1 | **Slots.** Every SnapProbe_Call entry flagged 32 has exactly one stack slot joined by `entry_seq`; `orphans == 0`. No slot has flags 1 / 2 / 8 / 16 / 0x8000. Slots have ≥ 3 frames. |
| S2 | **Frame 0.** `frames[0].module == "DumperTest58-Win64-Shipping.exe"`, inside `module_range(pid)` (rig :244-269), with `rva` < the module size. `module_base + rva == addr`. The rig's `"DumperTest58-Win64-Shipping.exe"+RVA` adds back to `addr`. |
| S3 | **Nesting.** In-scope stacks hold a `known:"process_event"` site and, after it, an `own` site. Lone stacks hold neither within 16 frames. |
| S4 | **Recorded, not failed:** whether an in-scope site's `fn` equals SnapNest_Outer's `code_addr` (pe_trace_names) before the ProcessEvent frame. A miss is the tail-call case (§4.3) and goes into "Step 3 built". |
| S5 | **Budget.** SnapProbe_PerFrame (lone, every frame) keeps about 30/s × 8 s (±30) and `stack.dropped_budget > 0`. The params counters are unchanged by the stack budget. |
| S6 | **Cost, recorded.** Mean µs per capture (`spent_ticks / captures / qpc_freq`), max µs, captures a second, calls/s with and without stacks, the machine (Ryzen 9 9955HX3D). This sets D3's re-weigh. |
| S7 | The release frees everything. Re-run the default `livefuncs_snap_live.py` (33/33) and `livefuncs_trace_live.py` (18/18) on the same DLL (cut 4). |

### 8.2 UI walkthrough (the AOT `-Mode Publish` exe, same game session)

1. The Stack? column is hidden with the experimental tabs off. With them on, it shows on rows without parameters (where Params? does not).
2. Choose SnapProbe_Call for a stack and SnapNest_Outer as the Trace tick. Start: the status names the stack choice. Stop: the note says "N stacks kept; … µs a capture".
3. Open in Call Trace. The Summary has the stack sentence, and in-scope `SnapProbe_Call` rows carry `(s)`.
4. Select an in-scope call. The Call stack tab shows:
   - `"DumperTest58-Win64-Shipping.exe"+…` rows;
   - a "UObject::ProcessEvent" row;
   - "the dumper's hook" row (its module is the proxy's name);
   - the Address setting changes the address column, not the Copy text.
5. Copy on a frame puts the quoted CE form on the clipboard. With CE and the AOBMaker plugin up, Open in CE moves the disassembler to the absolute address (CE is cleared for standing use; cut 5 makes this a register row).
6. A stack-only lone call's Parameters tab says "not chosen".
7. Kill the game, CE and the UI.

### 8.3 Avowed (UE 5.3 Shipping, dxgi proxy): optional, after the box

- `livefuncs_snap_live.py --stacks --choose "" --plain-s 20 --record-s 30` chooses the 60 busiest named functions for stacks, at the default budgets.
- **Record:** calls/s and the fps overlay with and without stacks, mean and max µs, captures a second, skipped and dropped, and whether any hitch shows once a second (M3).
- **Expected:** at most 200 captures a second, so at most about 2 ms/s at 10 µs.
- If it is not run in this session, it is the register row "Avowed stack cost" (S3-X1), and the D3 defaults stay provisional.
