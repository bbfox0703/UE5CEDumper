
# Live Funcs step 3: native stack snapshots — the design `[LIVEFUNCS-STEP3]`

This merges the "reuse" and "risk" designs, with three code maps behind them. I re-read every anchor below at `03c10fcb`. Anchors drift, so re-read one before relying on it.

**Two corrections to the brief:**
- **The pipe count is 104, not 102.** `grep -c 'constexpr const char* CMD' dll/src/Renge.h` = 104, and `dll/src/Fern.h:5` and CLAUDE.md:183 also say 104.
- **Himmel resolves no code address.** The only known code address is ProcessEvent, `Stark::HookedAddress()` (Stark.h:176, Stark.cpp:496).

## 0. Summary

**Capture**
- The stack is taken **inside `Linie::TraceEnter`**, at entry only, on calls chosen for a stack.
- The capturer is injected the way `BytesCopier` is (Linie.h:201): a function pointer installed by Fern.
- That capturer is `Macht::CaptureCallerStack`, which:
  1. bound-checks the hook's return slot against this thread's stack, and checks that enough stack is left;
  2. walks with `RtlCaptureStackBackTrace` under `__try`;
  3. trims our own frames by searching for the anchor, the game's return address `*retSlot`.
- **Stark.cpp does not change.** Stark.cpp:253 already passes `_AddressOfReturnAddress()` as TraceEnter's `sp` (:260).

**Storage**
- A stack ring is an ordinary snapshot ring in step 2's single allocation: same K, same 24-byte `SnapSlotHeader`, then `u64 frames[depth]`.
- Stack rings sit **after** the parameter rings (internal index `P + s`). `snapCount` keeps meaning P, so every step-2 reader stays parameters-only without a code change.
- Stacks have their own index space (0..S-1), their own budget words, entry flags 32 and 64, and their own readers (`StackRings` / `CopyStacks`).

**Resolution**
- Done **in the DLL after Stop**, on the pipe thread, per page. For each frame it gives:
  - module and module base;
  - RVA;
  - the function start from `.pdata` at ret-1;
  - whether unwind data exists;
  - `own` (our image, through `__ImageBase`);
  - `known` (ProcessEvent).
- The UI then does two things:
  - turns module names into CE names with its own code page;
  - matches function starts against the trace's own native entries (`code_addr`, step 2's F6).

**Pipe**
- **No new command.** `pe_snap_get` takes `"kind":"stack"`, so the pipe count stays **104**.
- No new `.cpp`/`.h` file and no new test file, so no pinned count moves.

**UI**
- A separate **"Stack?" column** in Live Funcs (this settles T14).
- A **"Call stack" tab** in the Call Trace detail pane (view A).
- Per-frame **Copy CE address** and **Open in CE disassembler** (view D).

**Ships now, end to end:**
- DLL capture, budget and counters;
- Start and Stop by name;
- `pe_snap_get kind:stack` with sites;
- the Stack? column;
- Call Trace views A and D;
- rig green on DumperTest58 Shipping (no repackage);
- an AOT UI walkthrough.

**Deferred:**
- the T9.1 estimate line and the T9.2 ask-once (both first in line after the minimum);
- the full native-UFunction index;
- chained-unwind follow;
- view B (raw stack copy plus annotation);
- stack export;
- `.pdata` prewarm;
- own-frame calibration;
- the PDB check in the rig;
- a time-based budget.

---

## 1. Decisions

The ones marked **confirm** go into the plan's Decisions table as T15-T19. They are built as written here (each is reversible), and the maintainer confirms or overrules them afterwards. Nothing in the run waits on an answer.

| # | Decision | Why | Maintainer |
|---|---|---|---|
| D1 (T15) | **T14 settled: stacks get a column of their own, "Stack?".** Params? stays a parameters-only checkbox, not a kind selector. A NameKey can carry a param ring, a stack ring, or both. | (1) **Who can be chosen differs.** `CanChooseSnapshot` (Models/PeProfileResult.cs:59) refuses a function with no parameters, but every function has a native stack. (2) **T9.2 (ask once) and T9.4 (bulk without per-frame rows) apply to stacks only.** (3) **The cost differs by about 100×**: tens of ns for a parameter copy, 1-10 µs for a walk. One box would make every parameter choice pay a walk, which is the coupling argument that split Trace from Snapshot. (4) **The column copies a tested pattern** (Params?, LiveFuncsPanel.axaml:282-296). | **confirm** (T14 handed it to step 3) |
| D2 (T16) | **The minimum lands before T9.1 (the estimate line) and T9.2 (the per-frame ask-once).** They are the first two items after it (S3-U6, S3-U7). | Time box. T9 calls the DLL budget "the guarantee" and the estimate "the advice". At D3's defaults the worst case is bounded at T9's own orange line (200/s × 10 µs = 2 ms/s). The column stays behind the experimental gate (T6). There is no bulk stack choice, so T9.4 holds trivially. | **confirm** (postpones two decided items) |
| D3 (T17) | **Budget 100 captures a second per function, 200 in total; depth 16 frames (128 B), fixed, no UI control; wire range 1-62.** After the live check the total is re-weighed: `total = floor(2000 µs / measured mean µs)`, rounded down to a multiple of 50. | T9.3: "defaults measured in step 3". The provisional total puts the plan's upper estimate (10 µs) exactly on the 2 ms/s line. | **confirm** (provisional, as step 2's 1,000 / 10,000 were) |
| D4 (T18) | **Stack rings share the snapshot buffer (T12's 8-128 MB slider) and its K.** No second slider. The slider row shows while anything is chosen for parameters or stacks. | One allocation, one memory figure (the D3 game figure), one release path. K just gains stack terms. A stack slot is 152 B at depth 16, against up to 2,072 B for a parameter slot. | **confirm** (T12's buffer now also holds stacks) |
| D5 (T19) | **Transport: `pe_snap_get` with `"kind":"stack"`**, not a new `pe_stack_get`. The pipe count stays 104. | It reuses the handler's gen / stale / max / page-cap / Tot logic (Fern.cpp:5233-5293) and its bulk-lane routing. It avoids 7 pinned-count edits (tools/check_derived_counts.py:117-134), a LaneRouting change and its test. An old DLL is never asked: the UI sends `kind:stack` only when the Start reply carried `trace.stack`. Cost: one command has two reply shapes, told apart by `kind` in the reply. | **confirm** (cheap to flip: +1 count in 7 places) |
| D6 | **Capture = `RtlCaptureStackBackTrace` + anchor search**, with stack-bound and headroom checks and a `__try`. Not rejected outright, but not used: a FramesToSkip count, a hand-rolled `RtlVirtualUnwind` loop, a context seeded from `sp`, a raw copy unwound offline, dbghelp `StackWalk64`. | (1) The anchor (`*retSlot`, a game address) is exact **whatever got inlined**. It cannot match among our frames, which are in our DLL. (2) A context seeded from `sp` carries our hook's RBP, so frame-pointer frames unwind wrong. (3) A manual loop is needed only for view B's per-frame RSP (deferred). (4) An exact offline unwind needs T4's MASM stub. (5) dbghelp takes a global lock. | no |
| D7 | **Resolution in the DLL, after Stop, per page.** The UI matches `fn` only against the trace's own native entries. The full GObjects native-entry index is **deferred** (S3-A1). | Only the game process can map an arbitrary address to its module (the UI knows only the main module's base, and no size: AddressHelper.cs:47-53), read `.pdata`, and know `__ImageBase` and ProcessEvent. The hot path pays nothing for it. The full index would name exec thunks reached without ProcessEvent, but it costs a GObjects pass. | no |
| D8 | **Entry flags: 32 `kTraceStackTaken`, 64 `kTraceStackBudget`.** 2 (Taken) and 16 (Budget) stay **parameters only**. 4 (Lone) and 8 (Excluded) mean "chosen for anything". | The UI and the export must tell a stack-only call from a parameter call. Adding bits is backward compatible: the UI decodes by bit. | no |
| D9 | **The DLL measures every capture.** Two trace-clock reads around the walk feed: the slot's header `arm` field (ticks, saturated u32; a stack slot has no arm), a per-ring `spentTicks`, and a global `maxTicks`. `captures` = the stack rings' `next`. | T9 wants "an estimate from the last recording". This turns "defaults measured in step 3" into a number the rig prints. Cost: about 40 ns per admitted capture. | no |
| D10 | A lone or excluded call is dropped (no record) **only when nothing it was chosen for was admitted**. Otherwise it is recorded, and each refused kind counts as *skipped*. Example: params refused, stack taken → `Lone \| 16 \| 32`, and params skipped +1. | Keeps step 2's meanings: skipped = recorded without the copy; dropped = not recorded at all. | no |
| D11 | **Stacks at entry only.** The capture time is **inside the call's measured duration** (the entry ticks are read before the walk). The tab says how many µs. | Plan §3 ("the return addresses above the hook"). | told |
| D12 | **Our hook's frame and ProcessEvent's frame are shown and labelled**, not hidden. | They are true frames of a nested call. Hiding them would make an in-scope stack look like a lone one. | no |
| D13 | **View D:** Copy always gives CE's module form `"module"+RVA` (quoted), or absolute hex outside any module, whatever the Address setting. The row's address column follows the Address setting (U11). **Open in CE** sends the absolute address and is refused once the trace's connection is gone (`_loadedGen == 0`, CallTraceViewModel.cs:857-859). | A copied CE address must survive a relaunch. An absolute address belongs to one process. | no |
| D14 | **The stack-ring index space is 0..S-1 on the wire and in `ArmHint.stackRing`.** Internally it is `snapRings[snapCount + s]`. `SnapRings` / `CopySnaps` / `InfoLocked` loop `snapCount` only, unchanged. | The UI pages every ring `pe_snap_layouts` lists and decodes it as parameters (CallTraceViewModel.cs:364-376). Stack rings must never appear there. | no |
| D15 | **UI storage: a separate `CallTraceStacks`** (in Models/CallTraceSnapshots.cs), stored as `CallTrace.Stacks`. It is not folded into `CallTraceSnapshots`. | Stack slots then cannot count in `Has` / `CallsWithParams` / `(p)`. A stacks-only trace, which has no arms, still loads its stacks. | no |
| D16 | **Headroom 32 KB.** The walk is skipped (slot flag LowStack) when less than 32 KB of stack reserve is left. | Our walk adds about 3 KB transiently: `raw[75]` (600 B), the walker's CONTEXT (about 1.2 KB), locals. 32 KB covers that plus the thread's stack guarantee, with 2× margin. It makes `STATUS_STACK_OVERFLOW` inside our `__try` unreachable. 64 KB would wrongly refuse small worker stacks. LowStack is counted and shown, so the live check sees it if it bites. | no |
| D17 | **Work split: every C++ edit in ONE tree, by the main session.** Helper A does the UI in a worktree. Helper B does the rig and the docs, no builds. | Two sessions building `dll_core_test` in one tree, mutations included, poison each other's red/green. A worktree has no configured build tree, and `build_dll.py` never configures. | no |
| M3 | **Open, after the live check:** if T9.3's "first calls of each second" shows a once-a-second hitch, use a 100 ms window with cap/10 for stacks only. | One AdmitWord parameter. It changes T9.3's wording. | ask later |

---

## 2. DLL

### 2.1 Linie.h

**ArmHint** (Linie.h:103-109) stays at **24 B**. Add `static_assert(sizeof(ArmHint) == 24)`.

| Offset | Field | Note |
|---|---|---|
| @0 | `u64 gen` | |
| @8 | `i32 ring` | parameter ring |
| @12 | `u32 arm` | |
| @16 | `u16 copy` | |
| @18 | `u8 flags` | |
| @19 | pad | |
| @20 | **`i32 stackRing = -1`** | NEW, last; existing aggregate inits still compile |

There is no `kArmStack` bit: a bit cannot say which ring.

**ArmSpec** (Linie.h:416-421) becomes 32 B. `NameKey key`@0 (16), `bool tick`@16, `i32 ring`@20, `u32 ringCap`@24, **`i32 stackRing = -1`@28** (last; the aggregate init at dll_core_test.cpp:8484 still compiles).

**ArmSummary** (Linie.h:506-513): add `int32_t stackRing = -1;` last.

**Entry flags**, next to `kTraceSnapBudget` (Linie.h:184):
```cpp
inline constexpr uint32_t kTraceStackTaken  = 1u << 5;   // a stack ring holds this call's native return addresses
inline constexpr uint32_t kTraceStackBudget = 1u << 6;   // chosen for a stack; the stack budget left it out
```
- The comments on `kTraceSnapLone` / `kTraceSnapExcluded` (:179-182) become "for its parameters or its stack".
- Run `py tools/verify/comment_impact.py --staged`.

**Stack constants**, next to the snapshot limits (Linie.h:385-391); the UI pins each:
```cpp
inline constexpr uint32_t kStackDefaultDepth         = 16;      // 128 B of return addresses (plan §3)
inline constexpr uint32_t kStackMaxDepth             = 62;
inline constexpr uint32_t kStackDefaultPerRingPerSec = 100;     // provisional (D3)
inline constexpr uint32_t kStackDefaultTotalPerSec   = 200;     // 200 x 10 us = T9's 2 ms/s line (D2, D3)
inline constexpr uint16_t kSnapNoCapturer            = 0x8000;  // Linie's own slot bit; bits 0-14 are the capturer's
inline uint32_t StackRingCap(uint32_t depth) { return (depth < 1 ? 1 : depth > kStackMaxDepth ? kStackMaxDepth : depth) * 8u; }
```

**Capturer type**, beside `BytesCopier` (Linie.h:201). Linie stays free of SEH; the pipe installs Macht's:
```cpp
using StackCapturer = uint32_t (*)(uintptr_t retSlot, uint64_t* out, uint32_t max, uint16_t& flags);
```

**TraceConfig** (Linie.h:222-245) additions:
```cpp
uint32_t      stackRings = 0;                                     // one per function chosen for a stack (D1)
uint32_t      stackDepth = kStackDefaultDepth;                    // every stack ring's; clamped 1..kStackMaxDepth
StackCapturer stackCapturer = nullptr;                            // nullptr: every stack slot empty, kSnapNoCapturer
uint32_t      stackPerRingPerSec = kStackDefaultPerRingPerSec;
uint32_t      stackTotalPerSec   = kStackDefaultTotalPerSec;
```

**TraceInfo** (Linie.h:282-306):
- `snap` is unchanged and stays parameters-only: `rings` = P, its budget counters from P rings.
- `snap.allocated` / `bytes` / `slotsPerRing` describe the whole block, which a stacks-only Start allocates too.
- Add:
```cpp
struct Stack { size_t rings = 0; uint32_t depth = 0, perRingPerSec = 0, totalPerSec = 0;
               uint64_t captures = 0, skippedBudget = 0, droppedBudget = 0, spentTicks = 0, maxTicks = 0; } stack;
```

**Readers**
- `SnapRingInfo` (Linie.h:309-316) gains `uint64_t spentTicks = 0;` last. The brace init in SnapRings (Linie.cpp:839) still compiles.
- New reader declarations:
```cpp
struct StackCopy { uint64_t index = 0, entrySeq = 0; uint16_t len = 0, flags = 0; uint32_t ticks = 0; std::vector<uint64_t> frames; };
bool StackRings(std::vector<SnapRingInfo>& out, uint64_t* gen = nullptr);   // index = s (0..S-1); stack rings only
bool CopyStacks(uint32_t s, uint64_t from, size_t maxSlots, std::vector<StackCopy>& out,
                uint64_t* next = nullptr, uint64_t* orphans = nullptr);    // CopySnaps' window and filters
```
- `StackCopy.len` is the header's value. `frames` comes from the payload clamped to the ring's cap.

### 2.2 Linie.cpp

- **BuildArmState** (Linie.cpp:55-73), in the merge branch (:59-63): `if (sp.stackRing >= 0) m.stackRing = sp.stackRing;`.
- **ArmLocked** (Linie.cpp:80-110): `s.arm.stackRing = it->stackRing;` goes right after `if (it->tick) ...` (:98), **before** `if (it->ring < 0) return;` (:99).
  - A stack-only choice then takes no arm-log record (log capacity 0) and starts no layout worker. Fern starts the worker only when `arms->capacity != 0` (Fern.cpp:4660).
  - A full arm log (`armsFull`) still arms the stack.
- **ArmsSummary** init (Linie.cpp:328): pass `sp.stackRing`.
- **SnapRing** (Linie.cpp:370-378, `alignas(64)`): add `std::atomic<uint64_t> spentTicks{0};` at @48. Used bytes become 56 of 64. Add `static_assert(sizeof(SnapRing) == 64)`.
- **TraceState** (Linie.cpp:380-405): add
```cpp
uint32_t stackCount = 0, stackDepth = 0, stackPerRing = 0, stackTotal = 0;
StackCapturer stackCapturer = nullptr;
std::atomic<uint64_t> stackTotalWindow{0}, stackMaxTicks{0};
```
  `FreeTraceLocked` (:457-484) resets them beside :467-471.
- **StartTrace** (Linie.cpp:568-643):
  - Build `std::vector<uint32_t> caps = cfg.snapRingCaps; caps.insert(caps.end(), cfg.stackRings, StackRingCap(cfg.stackDepth));` and use `caps` where the code uses `cfg.snapRingCaps` (:585-603). Nothing else in the allocation changes.
  - `snapCount = P`, `stackCount = S`, `stackDepth = clamp(1, 62)`.
  - Clamp the stack budgets as at :623-624 (1..0xFFFFFF). Reset `stackTotalWindow` and `stackMaxTicks` (as at :625).
  - **K = (snapBytes − 64·(P+S)) / (Σ_P(24+cap_p) + S·(24+8·depth))**, unchanged in form (:592-593):
    - 5 param rings of 64 B and 2 stack rings at depth 16 in 32 MB: (33,554,432 − 448) / 744 = **45,099**;
    - 1 param ring of 64 B and 1 stack ring: (33,554,432 − 128) / 240 = **139,809**.
- `static SnapRing& StackRingAt(int32_t s) { return g_trace.snapRings[g_trace.snapCount + s]; }`
- **StackAdmit**, beside SnapAdmit (:502-506), sharing AdmitWord (:489-498):
```cpp
bool StackAdmit(int32_t s, uint64_t ticks) {
    const uint64_t sec = ticks / QpcFreq();
    return AdmitWord(StackRingAt(s).window, sec, g_trace.stackPerRing) &&
           AdmitWord(g_trace.stackTotalWindow, sec, g_trace.stackTotal);
}
```
- **StackWrite**, beside SnapWrite (:511-529). Same discipline: the slot's number is written last.
```cpp
void StackWrite(int32_t s, uint64_t entrySeq, uintptr_t sp) {
    SnapRing& r = StackRingAt(s);
    const uint64_t k = r.next.fetch_add(1, std::memory_order_relaxed);
    uint8_t* slot = r.base + (k % g_trace.snapK) * r.slot;
    auto* hdr = reinterpret_cast<SnapSlotHeader*>(slot);
    hdr->seqKind = UINT64_MAX;
    hdr->entrySeq = entrySeq;
    const uint32_t max = r.cap / 8;                          // frames, never the slot's bytes
    uint16_t fl = 0; uint32_t n = 0;
    const uint64_t t0 = g_clock();
    if (g_trace.stackCapturer) n = g_trace.stackCapturer(sp, reinterpret_cast<uint64_t*>(slot + sizeof(SnapSlotHeader)), max, fl);
    else fl = kSnapNoCapturer;
    const uint64_t dt = g_clock() - t0;
    if (n > max) n = max;                                    // never trust a count past the slot
    hdr->len = static_cast<uint16_t>(n * 8);
    hdr->flags = fl;
    hdr->arm = dt > UINT32_MAX ? UINT32_MAX : static_cast<uint32_t>(dt);   // a stack slot has no arm: its cost (D9)
    r.spentTicks.fetch_add(dt, std::memory_order_relaxed);
    for (uint64_t m = g_trace.stackMaxTicks.load(std::memory_order_relaxed);
         dt > m && !g_trace.stackMaxTicks.compare_exchange_weak(m, dt, std::memory_order_relaxed);) {}
    hdr->seqKind = k;                                        // last: a torn or thrown write stays invisible (:812)
}
```
- **InfoLocked** (Linie.cpp:748-778):
  - `snap.*` is unchanged; its loop is already `r < snapCount` (:771).
  - When `stackCount != 0`, fill `i.stack`:
    - `captures` = Σ `next`;
    - `skippedBudget`, `droppedBudget`, `spentTicks` = sums over the stack rings;
    - `maxTicks`, `depth`, and the budgets.
- **CopyStacks / StackRings.**
  - Factor CopySnaps' body (:791-829) into a file-static `template <class Emit> void VisitSlotsLocked(const SnapRing&, uint64_t from, size_t max, uint64_t* next, uint64_t* orphans, Emit&&)`. It keeps the window, the out-of-turn test `(seqKind & ~kSnapAfterBit) != k` and the orphan test.
  - CopySnaps keeps its refusals and its bound `ring >= snapCount`. CopyStacks bounds `s >= stackCount`.
  - StackRings mirrors SnapRings (:831-844) over `[snapCount, snapCount + stackCount)`, with `index = s`.

### 2.3 Hot path: TraceEnter (Linie.cpp:655-725)

These are the changes, in order. TraceReturn (:727-745) does not change: there is no stack after the call.
```cpp
// beside :664
const int32_t sring = (named && hint.stackRing >= 0 &&
                       static_cast<uint32_t>(hint.stackRing) < g_trace.stackCount) ? hint.stackRing : -1;
// scope (:677):      } else if (ring >= 0 || sring >= 0) { lone = true; } else { return; }
// exclusion (:685):  if (ring < 0 && sring < 0) return; excluded = true;
const uint64_t ticks = g_clock();                                         // unchanged (:688)
bool taken      = ring  >= 0 && SnapAdmit(ring, ticks);
bool stackTaken = sring >= 0 && StackAdmit(sring, ticks);
if ((lone || excluded) && !taken && !stackTaken) {                         // D10
    if (ring  >= 0) g_trace.snapRings[ring].droppedBudget.fetch_add(1, std::memory_order_relaxed);
    if (sring >= 0) StackRingAt(sring).droppedBudget.fetch_add(1, std::memory_order_relaxed);
    return;
}
if (ring  >= 0 && !taken)      g_trace.snapRings[ring].skippedBudget.fetch_add(1, std::memory_order_relaxed);
if (sring >= 0 && !stackTaken) StackRingAt(sring).skippedBudget.fetch_add(1, std::memory_order_relaxed);
// record as today; flags add ((sring >= 0 && !stackTaken) ? kTraceStackBudget : 0)   (:709-710)
// after the param SnapWrite block (:717-724):
if (stackTaken) { StackWrite(sring, seq, sp); r.flags |= kTraceStackTaken; }        // flag only after it returned
```
- Parameters are copied first (tens of ns) and the stack last (µs).
- The record and the token are whole before either copy, which is step 2's rule for a faulting copy.

**Cost, arithmetic until S3-M1's bench and the rig measure it:**

| Call | Extra over step 2 |
|---|---|
| Not named | nothing |
| Named, no stack | one compare |
| Chosen for a stack, over the budget | 1-2 CAS and a relaxed add, about 20 ns |
| Admitted | about 60 ns (2 clock reads, the stack limits, atomics) plus the walk over about 5 own + 16 + 1 frames: **estimated 2-10 µs, unmeasured** |

RecordCall is unchanged. ArmLocked runs only at first sight or on a key change.

### 2.4 Macht: the capture (Macht.h / Macht.cpp; dll_core_test #includes Macht.cpp, dll_core_test.cpp:63)

```cpp
// Stack-slot flags: Macht owns bits 0-14 (Linie adds only kSnapNoCapturer 0x8000)
inline constexpr uint16_t kStackPartial = 1, kStackFault = 2, kStackMore = 4, kStackBadSp = 8, kStackLowStack = 16;
inline constexpr uint32_t kStackOwnSlack  = 12;          // our frames above the anchor at most (Release about 5; Debug about 7)
inline constexpr uint32_t kStackRawFrames = 75;          // kStackOwnSlack + 62 + 1
inline constexpr uintptr_t kStackHeadroom = 32 * 1024;   // D16
// First i < min(n, window) with raw[i] == ret; n when absent. Pure: tested without a stack.
inline uint32_t AnchorIndex(void* const* raw, uint32_t n, uintptr_t ret, uint32_t window);
using StackWalker = WORD (NTAPI*)(DWORD, DWORD, PVOID*, PDWORD);   // RtlCaptureStackBackTrace's shape
uint32_t CaptureCallerStackEx(uintptr_t retSlot, uint64_t* out, uint32_t max, uint16_t& flags,
                              uintptr_t headroom, StackWalker walk);
uint32_t CaptureCallerStack(uintptr_t retSlot, uint64_t* out, uint32_t max, uint16_t& flags);  // Ex(..., kStackHeadroom, &RtlCaptureStackBackTrace)
```

**CaptureCallerStackEx** is plain C with **no C++ objects**: dll_core_test builds with /EHsc (dll/CMakeLists.txt:667), so C2712 applies. This is the CallProcessEventSEH pattern (Stark.cpp:118).
1. `flags = 0`. If `max == 0`, return 0. Clamp `max` to `kStackRawFrames - kStackOwnSlack - 1` (62).
2. Call `GetCurrentThreadStackLimits(&low, &high)` (kernel32, Win8+, reads the TEB). Take `here = (uintptr_t)_AddressOfReturnAddress()`.
3. If `retSlot & 7 || retSlot <= here || retSlot + 8 > high`, set `flags = kStackBadSp` and return 0.
   - This covers a fiber or a switched stack: SwitchToFiber updates the TEB limits.
   - It also lets a test pass `sp = 900` safely.
4. If `here - low < headroom`, set `flags = kStackLowStack` and return 0.
5. Declare `void* raw[kStackRawFrames]`. Inside `__try`: `ret = *(const uint64_t*)retSlot; n = walk(0, kStackOwnSlack + max + 1, raw, nullptr);`.
   - On `__except(EXCEPTION_EXECUTE_HANDLER)`: `flags = kStackFault`, return 0.
   - The read sits inside the `__try` too, so a dropped bound check shows up as Fault, not as a crash.
6. `i = AnchorIndex(raw, n, ret, kStackOwnSlack + 1)`.
   - If `i == n`: `out[0] = ret`, `flags = kStackPartial`, return 1. The immediate caller is still exact.
7. `c = min(n - i, max)`. Copy `raw[i..i+c)`. If `n - i > max`, set `kStackMore` (hence the `+1`). Return c.

**Rules that hold the anchor:**
- `RtlCaptureStackBackTrace` is documented to MAXUSHORT frames. The "skip + capture < 63" limit is XP / Server 2003 only.
- Our frames carry `.pdata`, so the walk restores the game's nonvolatile registers (RBP included) before it reaches the game frames.
- `static_assert(Linie::kStackMaxDepth + Macht::kStackOwnSlack + 1 <= Macht::kStackRawFrames)` goes in Fern.cpp, where the capturer is installed, and in the test block.

**Rejected:** `NtCurrentTeb()` TIB reads (they work, but GetCurrentThreadStackLimits also gives the reserve's low end for the headroom test).

### 2.5 Macht: describing a code address (pipe thread only)

```cpp
struct CodeSite { uintptr_t moduleBase = 0; std::string moduleUtf8; uintptr_t fnBegin = 0; bool unwind = false; bool own = false; };
bool DescribeCode(uintptr_t retAddr, CodeSite& out);   // false (module "") when the address is in no module
```
- **Module:** `GetModuleHandleExW(FROM_ADDRESS | UNCHANGED_REFCOUNT)`, then `GetModuleFileNameW`, the leaf, then `Utf8Helpers::EncodeUtf16`.
  - This is the pattern of Genau.cpp:1149-1165, whose helpers are file-static.
  - Copy it into Macht. Folding Genau's statics into Macht is a todo row, not this run.
- **Function start:** `Macht::GetFunctionExtent(retAddr - 1, b, e)` (Macht.cpp:116-135).
  - The `-1` keeps a return address that follows a final noreturn call inside its own function.
  - `unwind = true` when it returns true.
  - It does **not** follow `UNW_FLAG_CHAININFO`: a PGO / hot-cold fragment reports its fragment's start (S3-M3, deferred).
- **own:** `moduleBase == (uintptr_t)&__ImageBase` (`extern "C" IMAGE_DOS_HEADER __ImageBase;`).
  - Never compare by name: the proxies are named dxgi / version / dinput8 / winmm.dll.
  - In dll_core_test, the test exe is `__ImageBase`.

### 2.6 TR2, TR6, TR7, and what can go wrong on the game thread

**TR2.**
- The walk runs inside TraceEnter's existing `InflightGuard` (Linie.cpp:435-440). StopTrace's 2 s quiesce (:444-454) covers a µs walk and a ms hard fault.
- Under /EHa (dll/CMakeLists.txt:186), anything that escapes the capturer meets RunThreadGuarded's `catch(...)` (Routine.h:128). The guard's destructor still runs; the slot stays invisible; the record has no 32.

**TR6.** The stack is in the snapshot buffer, linked by `entrySeq`, never in the 40-byte record. This is step 2's recorded deviation.

**TR7.** Per function per second and in total, the first calls of each second kept; skipped and dropped are counted and reported.

**Risks and answers:**

| # | Risk | Answer | Proven by |
|---|---|---|---|
| R1 | Stack nearly exhausted | the headroom test gives LowStack and no walk | S3-M1 case 5 |
| R2 | Fibers or a stale `sp` | the bound test gives BadSp and no read | S3-M1 case 4 |
| R3 | Code without unwind data (JIT, DRM) | unwound as a leaf; depth and stack limits bound it; site `unwind:false`, and the UI says "frames below #i may be wrong" | S3-M2 case 4, S3-U5 |
| R4 | A fault in the walk | `__try` gives Fault; RunThreadGuarded is the second net | S3-M1 case 7 |
| R5 | Locks | `RtlLookupFunctionEntry` takes the inverted-function-table SRW shared, as every C++ throw does. We hold no lock of ours and allocate nothing. | review |
| R6 | Cost | the budget, plus measured `spent` / `max` ticks | S3-L2, S3-M1 bench, rig S6 |
| R7 | Cold `.pdata` pages of a 100+ MB exe | detected by `max_ticks`, not prevented; prewarm deferred (S3-P1, triggered if max > 1 ms) | rig S6 |
| R8 | Once-a-second burst | M3, after the live check | Avowed later |
| R10 | Our frame count varies with inlining | the anchor search | S3-M1 cases 2, 8 |
| R11 | Deep recursion | the walk stops at `kStackOwnSlack + max + 1` ≤ 75 frames | S3-M1 case 3 |
| R12 | Unchosen calls pay | one compare when named; step-1/2 pinned records unchanged | S3-L1 case 12 |

---

## 3. Pipe protocol (Fern.cpp: no unit target; proven by the DLL build and the rig)

### 3.1 Start: `pe_profile_start`

Stacks ride inside `trace.snapshots`, so `snapsAsked` (Fern.cpp:4484) already opens the by-name block:
```json
"trace": { "bytes": 67108864, "ticked_names": [ ... ],
  "snapshots": { "bytes": 33554432, "per_ring_per_s": 1000, "total_per_s": 10000,
    "funcs":  [ {"class":"DumperTest58Actor","func":"SnapProbe_Call","keys":[[i,n,ci,cn]],"parms_size":96} ],
    "stacks": { "funcs": [ {"class":"DumperTest58Actor","func":"SnapProbe_Call","keys":[[i,n,ci,cn]]} ],
                "depth": 16, "per_ring_per_s": 100, "total_per_s": 200 } } }
```
- `funcs` may be `[]` (stacks only).
- A function in both lists gets a param ring and a stack ring; BuildArmState merges the two specs.

**Handler**, inside `if (snapsAsked)` (Fern.cpp:4527-4566), after the params loop:
1. Make the `budget` lambda (:4536-4541) take its JSON object as a parameter, so it serves both `s` and `s["stacks"]`.
2. If `s["stacks"]` is an object:
   - `cfg.stackDepth = clamp(st.value("depth", 16), 1, 62)`;
   - the stack budgets go through the lambda;
   - `cfg.stackCapturer = &Macht::CaptureCallerStack` (mirrors :4543).
3. For each item:
   - `++stackAsked`;
   - check the keys with `goodKeys` (:4491-4506); none left → `refuse` and continue;
   - `++stackOk`; `const int32_t sr = static_cast<int32_t>(cfg.stackRings++);`;
   - per key, push `ArmSpec{ key, tick = false, ring = -1, ringCap = 0, stackRing = sr }`.
4. **Refusal** (:4569): `tickOk + snapOk + stackOk == 0 || (tickAsked != 0 && tickOk == 0)`. The log line adds the stack counts.
5. `BuildArmState(specs, snapOk ? kArmLogCapacity : 0)` (:4579) is **unchanged**: stack-only choices take no log and start no worker.
6. `namesReply` (:4583) adds `{"stacks", stackOk}`. This is half of the UI's echo; `trace.stack` is the other half.
7. `cfg.snapOnly` (:4618) becomes `(!cfg.snapRingCaps.empty() || cfg.stackRings != 0) && cfg.tickedNames == 0 && cfg.ticked.empty()`.
8. The SnapTooSmall / SnapNoMemory warning (:4620-4625) counts P+S rings.

### 3.2 Replies

- **TraceInfoToJson** (Fern.cpp:1746-1778) adds, only when `i.stack.rings > 0`:
```json
"stack": {"rings":2, "depth":16, "per_ring_per_s":100, "total_per_s":200, "captures":812,
          "skipped_budget":0, "dropped_budget":1443, "spent_ticks":2412345, "max_ticks":51234}
```
  - Every pe_* reply carries it.
  - µs per capture = `spent_ticks / captures / qpc_freq × 1e6`; `qpc_freq` is already in the reply.
  - `snap` keeps `rings` = P. For a stacks-only Start, `snap.allocated` is true with `rings: 0`.
- **ArmsSummaryToJson** (Fern.cpp:1894-1911) adds `"stack": s.stackRing >= 0` on each `names[]` entry. The Stop reply's `snap_rings` (:4730-4741) stays parameters-only.
- **Old DLL:**
  - A 3640 DLL ignores `stacks`: no `trace.stack`, no `names.stacks`.
  - A stacks-only Start with no ticks is refused there ("None of the chosen functions…"). That is a stale-proxy case, and it is acceptable.

### 3.3 `pe_snap_get` with `"kind":"stack"`

The branch sits at the top of the handler (Fern.cpp:5233), after the shared preamble: ring / from / max clamp to 1..4096 (:5234-5238), the gen and stale tests (:5240-5245).

**Request:**
```json
{"cmd":"pe_snap_get","kind":"stack","gen":G,"ring":S,"from":F,"max":M}
```
`S` is a stack ring index, 0..`stack.rings`-1.

**Reply:** the root is TraceInfoToJson, as today.
```json
{ ..., "ring": 0, "kind": "stack",
  "rings": [ {"ring":0, "cap":128, "depth":16, "written":420, "first_valid":0,
              "skipped_budget":0, "dropped_budget":1443, "spent_ticks":1234567} ],
  "count": 2, "next": 2, "orphans": 0,
  "items": [ {"index":0, "entry_seq":1234, "flags":0, "ticks":412, "frames":[0,1,2]} ],
  "sites": [ {"addr":"0x7FF6A14804C9", "module":"DumperTest58-Win64-Shipping.exe", "module_base":"0x7FF6A0000000",
              "rva":4719817, "fn":"0x7FF6A1480440", "fn_rva":4719680, "unwind":true},
             {"addr":"0x7FF6A0F00123", "module":"DumperTest58-Win64-Shipping.exe", "module_base":"0x7FF6A0000000",
              "rva":15729955, "fn":"0x7FF6A0F00000", "fn_rva":15728640, "unwind":true, "known":"process_event"},
             {"addr":"0x7FFC12345678", "module":"dxgi.dll", "module_base":"0x7FFC12300000",
              "rva":284280, "fn":"0x7FFC12345000", "fn_rva":282624, "unwind":true, "own":true} ] }
```

**Fields:**
- `rings` is **always sent**, from `StackRings`, so the UI learns the windows without another call. `from` below `first_valid` is clamped by the DLL, as in CopySnaps (Linie.cpp:806-807).
- `items[].frames` are indices into this page's `sites`, de-duplicated per page with the `layoutIndex` pattern (Fern.cpp:5188-5218).
- `module` is `""` outside any module (JIT, heap, unloaded); `module_base` and `rva` are then absent.
- `fn` and `fn_rva` are absent when `unwind` is false.
- `own` and `known` appear only when true or matched. `known` is a token; the UI owns the text.
- `known:"process_event"` when `fnBegin == Stark::HookedAddress()`.
  - The trampoline is never on the stack: it jumps into ProcessEvent's body after the relocated prologue, so ProcessEvent's unwind info covers the frame.

**Rules copied from today's handler:**
- The copy is taken under the lock by `Linie::CopyStacks`; DescribeCode runs outside it, with a per-request memo `unordered_map<uint64_t addr, size_t site>`.
- The page is capped at `kSnapPageBytes` (1 MB, Fern.cpp:2016). `Tot::Requested()` is polled every 256 items.
- It is refused (count 0) while tracing, unquiesced, released or stale. Release is unchanged.

**Stack slot flags** (`items[].flags`, u16):

| Value | Name | Meaning |
|---|---|---|
| 1 | Partial | the anchor was not found: only the caller's return address |
| 2 | Fault | the walk faulted: 0 frames |
| 4 | More | deeper than the depth: the rest was not taken |
| 8 | BadSp | the return slot is not on this thread's current stack: no walk |
| 16 | LowStack | under 32 KB of stack reserve left: no walk |
| 0x8000 | NoCapturer | no capturer was installed |

**docs/pipe-protocol.md:**
- Add a "#### Native stack snapshots (step 3) `[LIVEFUNCS-STEP3]`" subsection after the step-2 section (:677-757): the request, the reply, the slot-flag table, and "what does not hold" (§4.3).
- Add rows 32 and 64 to the entry-flag table (:740-748).

---

## 4. Resolution: what is named, what is not

### 4.1 The DLL (§2.5, §3.3)

Per distinct address, once per page, the DLL gives:
- module, module base and RVA;
- the function start (`.pdata`, at ret-1) and whether unwind data exists;
- `own` (our image);
- `known` (ProcessEvent).

It is never done on the hot path.

### 4.2 The UI

- **CE module name per site:** `CeModule = _codePage.AnsiModuleName(module)` at parse time (precedent DumpService.cs:177). CE narrows names with the UI machine's code page (Methode.h:96-97), never the game's.
- **Naming a frame:** a dictionary `CodeAddr → TraceFuncName` is built once per trace from `CallTrace.Funcs` (CallTrace.cs:50) where `CodeAddr != 0`. These are pe_trace_names' `code_addr`: live natives only (step 2's F6). A frame whose `fn` equals an entry is labelled "native entry of Class::Func +0xOFF". If several traced functions share the address (identical-code folding), the label adds "and N others".
- **Formatting:** `AddressHelper.FormatAddress(hex, site.CeModule, site.ModuleBaseHex, fmt)` (AddressHelper.cs:100-118), with each site's **own** module and base. `TryGetModuleRva`'s 4 GiB guess (:55-78) is never used to attribute a frame.

### 4.3 Not captured, or not matched

This goes into the plan's "Step 3 design" and pipe-protocol.md.
- **Exec thunks reached without ProcessEvent.** Blueprint → native goes through ProcessInternal / CallFunction, so these thunks are not in `Funcs` and stay unnamed until S3-A1, the full native-entry index (one GObjects pass of Function / DelegateFunction / SparseDelegateFunction, `Func → ufunc`, `shared: N` for ProcessInternal).
- **Tail calls.** A thunk that tail-jumps to its implementation leaves no frame, so `fn` is the implementation's start and matches no `code_addr`. DumperTest58's `execSnapNest_Outer` may well be such a thunk: the rig records this, it does not fail on it.
- **Inlined functions** have no frame.
- **PGO / hot-cold fragments** report the fragment's start (S3-M3).
- **Code without unwind data** unwinds as a leaf, so the frames below it may be wrong. Depth and the stack bounds limit this.
- **Frames deeper than 16** are cut, with flag 4.
- **Absolute addresses** belong to one run. The copied `"module"+RVA` survives a relaunch.
- **Not planned:** registers, and Blueprint's FFrame script stack (T4, plan §3).

---

## 5. UI

### 5.1 Models and service (AOT: manual JsonNode only; no new file)

**Models/PeProfileResult.cs.** Add `[ObservableProperty] _isStackChosen` beside :23, and `public bool CanChooseStack => FnameKey != null;`. There is no parameters rule.

**Models/ParamSnapshotModels.cs**, appended:
- `StackStartOptions { Funcs (IReadOnlyList<NamedFunction>), Depth = 16, PerRingPerSec = 100, TotalPerSec = 200 }`.
- `SnapshotStartOptions.Stacks` (nullable).
- `StackInfo { Rings, Depth, PerRingPerSec, TotalPerSec, Captures, SkippedBudget, DroppedBudget, SpentTicks, MaxTicks }`.
- `StackSite { ulong Addr; string Module, CeModule; ulong ModuleBase; uint Rva; ulong Fn; uint FnRva; bool Unwind, Own; string Known; }`.
- `StackSlot { ulong Index, EntrySeq; int Flags; uint Ticks; IReadOnlyList<StackSite> Frames; }`, with constants Partial 1, Fault 2, More 4, BadSp 8, LowStack 16, NoCapturer 0x8000.
- `StackPage { TraceInfo Info; bool Stale; int Ring; IReadOnlyList<SnapRingInfo> Rings; int Count; ulong Next; ulong Orphans; IReadOnlyList<StackSlot> Items; }`. `SnapRingInfo` is reused; its `spent_ticks` is ignored, because the totals are in StackInfo.

**Models/CallTraceModels.cs.** `TraceInfo` (:39-73) gains `StackInfo? Stack`. The start-names model gains `Stacks` (int).

**Services/DumpService.cs:**
- **Start request.** Beside the snapshots object (:2862-2872), add `["stacks"] = new JsonObject { ["funcs"] = NamedFunctionsJson(st.Funcs, withSize: false), ["depth"], ["per_ring_per_s"], ["total_per_s"] }` only when `Stacks != null`. Every node goes through the `(JsonNode?)` cast (:2850-2853).
  - The snapshots object is built when either list is non-empty, with `funcs: []` for stacks only.
  - **Without stacks the request is byte-identical to today's** (pinned by DumpServiceTests.cs:1910-1929).
- **ParseTraceInfo** (:3038-3089) reads `stack` like `snap` (:3056-3066), and `names.stacks`. Absent → `null` / 0.
- **`PeStackGetAsync(ulong gen, int ring, ulong from, int max, CancellationToken)`** mirrors PeSnapGetAsync (:3186-3217):
  - it sends `pe_snap_get` with `"kind":"stack"`, already on the bulk lane;
  - it parses `rings` with ParseSnapRings (:3102);
  - it resolves `frames` indices to `sites` (an out-of-range index is dropped and logged);
  - it sets each site's `CeModule` through `_codePage.AnsiModuleName`.
- **Core/IDumpService.cs.** A default-throwing `PeStackGetAsync` beside :461-465.

### 5.2 Live Funcs: the choice (ViewModels/LiveFuncsViewModel.cs)

- **State.** `_stackChosen` (FunctionTickSet) beside `_snapChosen` (:341). Add `StackFunctions` / `HasStackChoices` / `StackCountText` beside :343-345, and `StackPerFuncPerSec = 100` / `StackTotalPerSec = 200` beside :357-358.
- **`[RelayCommand] ToggleStack(PeProfileEntry? row)`.** Synchronous in the minimum; a copy of `ToggleSnapshot` (:374-382) without the parameters rule.
  - Refused: Trace off (`CanSnapshot`, :347), while recording, keyless, rows from an earlier connection.
  - Reuse the snapshot refusal keys where their text fits; add a stack key only where a text names parameters.
- **Clearing.** `ClearSnapshots` (:384-391) also clears stacks. `RefreshSnapshotList` (:416-426) raises the stack properties.
- **TraceOptionsForStartAsync** (:677-717):
  - T7's condition (:692) adds `&& _stackChosen.Count == 0`.
  - `Snapshots` is built when `_snapChosen.Count > 0 || _stackChosen.Count > 0`, with `Stacks = _stackChosen.Count == 0 ? null : new StackStartOptions { Funcs = _stackChosen.Named(), ... }`.
- **`TraceStartKey`** (:721-724): its count is the distinct Class::Func across both lists, so a stacks-only Start no longer says "0".
- **StartAsync:**
  - **Old-DLL guard**, after the names guard (:859-867): stacks asked and `start.Trace?.Stack == null` → stop, release, `str.LF.Stack.NotArmed`.
  - **Status** (:886-890): the parameter sentence only when `Snapshots.Funcs.Count > 0`; a stack sentence (`str.LF.Stack.Recording`) when stacks were asked.
- **`TraceStopNote`** (:961-963):
  - the parameter sentence only when `Snap.Rings > 0`;
  - plus `str.LF.Stack.StopNote`: "N stacks kept; M over the budget (K not recorded); about X µs a capture, at most Y µs".
- **Memory and the K estimate.**
  - `TraceGameMb` (:306) counts the snapshot MB when either set is non-empty.
  - `EstimateSnapshots` (:461) gains `int stackRings = 0`. Its per-round term (:472) adds `stackRings × (24 + 8 × 16)`, and its 64 term counts P+S, so K and TooSmall keep matching the DLL's (§2.2).
- **Refresh** (:1054-1057) maps `_stackChosen` onto new rows. **Disconnect** (:1302-1305) clears it.

### 5.3 Live Funcs view

- **Views/LiveFuncsPanel.axaml.** A "Stack?" `DataGridTemplateColumn` copied from Params? (:282-296):
  - header `str.LF.Col.Stack`;
  - `CanUserSort="False"`, no SortMemberPath, `IsVisible="False"`;
  - `IsChecked IsStackChosen OneWay`, `ToggleStackCommand`;
  - `IsEnabled CanSnapshot`, `IsVisible CanChooseStack`;
  - tooltip `str.Tip.LF.Stack.Choose`.
- **Code-behind.** `ApplyTickColumnVisibility` (LiveFuncsPanel.axaml.cs:80-88) includes its header.
- **The snapshot row** (:131-159) shows while `HasSnapshotChoices || HasStackChoices`, with `StackCountText` beside the count.

### 5.4 Call Trace: load and join

- **Load** (ViewModels/CallTraceViewModel.cs, ReadAndBuildAsync):
  - The parameter block's test (:351) becomes `info.Snap is { Allocated: true, Rings: > 0 }`, so a stacks-only trace makes no layouts call.
  - After it (:376) and before the build, when `info.Stack is { Rings: > 0 }`, page each ring `s` in `0..Rings-1`:
    - start at `from = 0`;
    - `PeStackGetAsync(info.Gen, s, from, SnapPage, ct)`;
    - on stale, or a gen that changed, return `str.CT.Status.Changed` (the :358 / :370 pattern);
    - break on `Count == 0 || Next <= from`;
    - collect the slots and add the orphans;
    - status `str.CT.Status.ReadingStacks`.
- **Build** (:384-389): `if (stackSlots.Count > 0 || info.Stack != null) built.Stacks = CallTraceStacks.Join(built, stackSlots, orphans, info.Stack);`.
- **Models/CallTraceSnapshots.cs** gains `public sealed class CallTraceStacks`:
  - `Dictionary<int, StackSlot>`;
  - `StackOf(call)`, `Has(call)`, `Calls`, `Unjoined`, `Orphans`, `Info`;
  - joined through `trace.FindBySeq` (CallTrace.cs:72).
- **Models/CallTrace.cs.** Add `public CallTraceStacks? Stacks { get; internal set; }` beside `Snapshots` (:48).
- **Params(i) fix** (:558-605). Parameters are chosen only when `(f & (Taken | Budget)) != 0 || entry != null || after != null`; otherwise the tab says `str.CT.Param.NotChosen`.
  - Today a stack-only lone call (`4|32`) reads "Overwritten".
  - Step 2 is unchanged: its lone and excluded calls always carry 2, or were dropped.
  - `str.CT.Param.Lone` (en.axaml:1176) says "alone, for what it was chosen for".
- **Export fix** (Helpers/CallTraceExport.cs:108). The gate's `ChosenFlags` (:21) becomes `SnapTakenFlag | SnapBudgetFlag`, plus `Snapshots?.Has(i)`. A stack-only lone call then gets no `snapshot` field. Today it gets "overwritten", or "taken" when there is no params load (:185-189).
- **Marker and summary:**
  - `CallTraceRow.HasStack` and an `(s)` marker beside `(p)` (CallTraceViewModel.cs:895-896, :959; CallTracePanel.axaml:287-290);
  - the `Summary` sentence `str.CT.Summary.Stacks` (:462-464): calls with a stack, skipped, dropped, mean µs.

### 5.5 View A: the "Call stack" tab

- **Tab.** A TabItem `str.CT.Tab.Stack` is inserted after the Call tab (CallTracePanel.axaml:151-156). The order becomes Call | Call stack | Parameters; view B's tab goes later between the last two (plan: A, B, C).
- **Data.** `internal (IReadOnlyList<StackFrameRow> Rows, IReadOnlyList<string> Notes) Stack(int i)`:
  - filled in `OnSelectedIndexChanged` (:539-547) as `StackRows` / `StackNote`;
  - cleared in `DropShownTrace` (:407-419).
- **Row type.** `public sealed class StackFrameRow { int Index; string Address; string Where; string CopyText; ulong Abs; }`. It is public and in the ViewModels namespace, like ParamRow (:900-914).
- **Pure helpers** (internal static, unit-tested):
  - `FrameAddress(StackSite s, AddressFormat f)`: `AddressHelper.FormatAddress(hex, s.CeModule, s.ModuleBaseHex, f)` when `s.Module != ""`, otherwise hex in the chosen style.
  - `FrameCopyText(StackSite s)`: always `"{CeModule}"+{Rva:X}`, or `0x{Addr:X}` with no module.
  - `FrameWhere(StackSite s, codeIndex)`, in this precedence:
    1. `Own` → `str.CT.Stack.Hook`;
    2. `Known == "process_event"` → `str.CT.Stack.ProcessEvent`;
    3. `Fn` in codeIndex → `str.CT.Stack.Native` (Class::Func, +0xOFF, "and N others");
    4. `Fn != 0` → `str.CT.Stack.Into` (`+0x{Addr-Fn:X} into "{CeModule}"+{FnRva:X}`);
    5. otherwise `str.CT.Stack.NoUnwind` or `str.CT.Stack.NoModule`.
- **Notes,** from the entry flags and the slot:

| Condition | Key |
|---|---|
| no 32, no 64, no slot | `str.CT.Stack.NotChosen` |
| 64 | `str.CT.Stack.Budget` |
| 32 but no slot | `str.CT.Stack.Overwritten` |
| Lone or Excluded | `str.CT.Stack.Lone` |
| slot flags 1 / 2 / 4 / 8 / 16 / 0x8000 | `str.CT.Stack.Partial` / `.Fault` / `.More` / `.BadSp` / `.LowStack` / `.NoCapturer` |
| the capture's cost | `str.CT.Stack.Cost` ("taking it cost X µs, inside this call's duration") |
| the first frame with `unwind:false` | `str.CT.Stack.MayBeWrong` ("frames below #i may be wrong") |

### 5.6 View D: to Cheat Engine

- **`[RelayCommand] CopyFrameAsync(StackFrameRow r)`:** `ClipboardDelivery.TryAsync(_platform, r.CopyText)` (Helpers/ClipboardDelivery.cs:60). Its bool sets `str.CT.Stack.Copied` or `str.CT.Stack.CopyFailed`, as `check_clipboard_delivery` requires.
- **`[RelayCommand] AsmFrameAsync(StackFrameRow r)`:**
  - refused with `str.CT.Stack.AsmOldTrace` when `_loadedGen == 0`;
  - otherwise `AobMakerActions.AsmAsync(LiveFuncs.AobMaker, "0x" + r.Abs.ToString("X"), label, _log)` (Helpers/AobMakerActions.cs:57). That reaches `NavigateDisassemblerAsync` with a bare absolute hex (IAobMakerBridge.cs:63-68).
  - `LiveFuncs.AobMaker` is already the shared status (LiveFuncsViewModel.cs:254; MainWindowViewModel.cs:591-592).
- **Buttons** bind through the AOT-safe `$parent[UserControl].((vm:CallTraceViewModel)DataContext).…Command` route (CallTracePanel.axaml:277-282), with `CommandParameter={Binding}`. ASM's `IsEnabled` binds `LiveFuncs.AobMaker.IsAvailable`.
- **Gates.** Every status goes through Res keys (`check_vm_status_literals`). Every new en.axaml key is referenced (`check_axaml_strings`).

---

## 6. What ships now, and what is deferred

**SHIPS (the minimum):** S3-X0, S3-R1, S3-L1, S3-L2, S3-M1, S3-M2, S3-F1, S3-F2, S3-U1 to S3-U5, S3-X1, S3-X2. Together they give:
- the DLL capture, budget and counters;
- Start and Stop by name;
- `pe_snap_get kind:stack` with sites;
- the Stack? column;
- views A and D;
- the rig on DumperTest58;
- the AOT walkthrough.

**NEXT, in this order** (built in the same run if the clock allows, else first next session):
1. S3-U6, the per-frame ask-once (T9.2);
2. S3-U7, the estimate line (T9.1);
3. S3-A1, the native-entry index;
4. S3-M3, chained unwind;
5. S3-R2, the PDB check in the rig.

**DEFERRED** (ledger rows marked deferred):
- S3-B1: view B (a raw-copy ring kind `[sp+8, min(sp+8+N, StackBase))`, a per-frame RSP from a manual unwind, annotation after Stop);
- S3-E1: JSONL / CSV stack export (CallTraceTreeTests pins the columns; func_unloaded / func_reused stay last);
- S3-P1: `.pdata` prewarm;
- S3-O1: own-frame calibration;
- a "Stack shown rows" bulk choice that leaves per-frame rows out;
- "Only calls with a stack";
- "Copy whole stack";
- ASM at the function start;
- a time-based budget;
- M3's 100 ms window.

### Time plan (65 min wall clock)

| T+ | Main session (all C++, then build, publish, live) | Helper A (UI, worktree off `03c10fcb`, fakes only, §3 frozen) | Helper B (rig and docs, no builds, worktree) |
|---|---|---|---|
| 0-2 | brief the helpers with this design | start S3-U1 | start S3-X0 |
| 2-14 | S3-L1 (red build, green build, one combined mutation build) | S3-U1 (to 9), S3-U2 | S3-X0 committed (to 6); main merges it |
| 14-22 | S3-L2 | S3-U2 (to 17), S3-U3 (to 21) | S3-R1 code (to 16), committed before it runs |
| 22-31 | S3-M1 + S3-M2 (one red, one green, two mutation builds) | S3-U4 (to 30) | S3-R1 red on the deployed 3640 (DumperTest58; kill it after) |
| 31-42 | S3-F1 + S3-F2 code; `py tools/verify/build_dll.py --targets UE5Dumper dll_core_test`; the full dll_core_test run | S3-U5 (to 42) | pipe-protocol.md subsection; S3-X1 drafts (plan "Step 3 design" decisions T15-T19, register / todo rows) |
| 42-46 | merge A's and B's branches; `dotnet test` (count the tests run: zero is a failure) | full UI run done; hand over; then S3-U6 if under 50 | read-only review of the Fern diff against §3 |
| 46-53 | `build.ps1 -Mode Publish` (VS DevShell, bump kept); check ~54 MB and the SHA; `py tools/verify/proxy_refresh.py refresh` on DumperTest58 | (S3-U6 / S3-U7 in the worktree, merged only if green before 46) | prepare the rig and walkthrough commands |
| 53-61 | rig `--stacks` green → step-2 rig re-runs → the UI walkthrough in the same game session; kill the game, CE and the UI | — | — |
| 61-65 | S3-X1 / S3-X2 numbers; ledger statuses; commit | — | — |

**Build economy.**
- A combined mutation build is valid only when each mutation's own named check fails and no other does. Otherwise split it.
- Fern items share one DLL build and one rig run.

**Cut order if late:**
1. the Fern rig-mutation runs (marked "owed" in their status; unit mutations are never cut);
2. the bench lines;
3. the `(s)` marker and the Summary sentence;
4. the step-2 rig re-runs (a register row instead);
5. the CE half of the walkthrough (Copy is still checked through the clipboard; Open-in-CE becomes a register row).

**Never cut:**
- the bound, headroom and `__try`;
- the stack budget;
- `snapCount = P` (the readers' isolation);
- the Params / export NotChosen fixes;
- the old-DLL guard;
- the AOT publish before the walkthrough.

---

The build ledger is [live-funcs-step3-items.md](live-funcs-step3-items.md) (section 7 of this design, moved there).

---

## Design review (critic, 2026-10-08)

I checked the merged step-3 design against the code at 03c10fcb. Nothing in it can crash or stall the game thread beyond what any C++ exception thrown on that thread already risks, and TR2 holds. The design's arithmetic is right. What is wrong: the plan does not fit 65 minutes, four places where the UI or its tests would fail as specified, and a few test-mechanics traps.

## What I confirmed against the code

**Anchors**
- I re-read every file:line anchor the design relies on. Almost all are right; the drifted ones are listed under L15 below.

**Stark.cpp needs no change**
- The hook passes `_AddressOfReturnAddress()` as TraceEnter's `sp` (Stark.cpp:253 and :260).
- TraceEnter runs inside `RunThreadGuarded` (Routine.h:127-139).
- The armHint is copied whole into the hint (Linie.cpp:203), so a new `stackRing` field travels with it.

**Hook target and `known:"process_event"`**
- The hooked address is UObject::ProcessEvent: Frieren.cpp:1865-1885 reads the vtable of an object at index 1-99, never an actor.
- AActor::ProcessEvent calls its Super directly, so actor calls still reach the hook.
- So `known` = "function start == `Stark::HookedAddress()`" (Stark.cpp:496) is the right test.

**Fixture**
- SnapNest_Fire runs from a native C++ timer (DumperTest58Actor.cpp:95-98 and :224-231). Lone stacks therefore really have no ProcessEvent frame, as S3 expects.

**Sizes and ring-count arithmetic**
- ArmHint is 19 bytes padded to 24, so a 4-byte `stackRing` at offset 20 keeps it at 24.
- ArmSpec grows from 28 to 32 bytes. The aggregate init at dll_core_test.cpp:8484 still compiles.
- SnapRing uses 48 of its 64 bytes; `spentTicks` at offset 48 fits.
- The ring-count (K) examples are right: 45,099 and 139,809. L1 case 5 gives K = 8. `StackRingCap(100)` = 496.

**TR2**
- The walk runs inside InflightGuard, after the `g_tracing` check.
- Every reader takes `g_traceMu` and is refused while tracing.
- DescribeCode works only on integers copied out of the ring.
- No new free path is added.

**Windows API facts**
- The design's claim about how many frames RtlCaptureStackBackTrace can take is right. Microsoft's docs say "up to MAXUSHORT frames", and the "skip + capture < 63" limit applies to XP and Server 2003 only.
- The `StackWalker` typedef matches the winnt.h signature, and the function links from kernel32.lib.
- GetCurrentThreadStackLimits needs Windows 8 or later. dll/CMakeLists.txt:160-166 defines no `_WIN32_WINNT`, so the SDK default applies and the declaration is visible.
- RtlLookupFunctionEntry runs inside the game process, because the DLL is injected. Only the DLL can resolve frames; the UI cannot.

**Pipe and counts**
- The pipe count is 104, not the brief's 102. `py tools/check_derived_counts.py` reports "21 claim(s) across 10 file(s) match".
- Using `kind:"stack"` on `pe_snap_get` adds no command. `pe_snap_get` is already on the bulk lane (LaneRoutingPipeClient.cs:58).
- No new .cpp, .h or C# test file means no pinned count moves.

## HIGH

### H1. The plan does not fit 65 minutes

**Evidence**
- Step 2's commit log measures one DLL item, red to green with its mutations, at 7 to 17 minutes:

  | Item | Red | Green | Minutes |
  |---|---|---|---|
  | S7 | 20:05 | 20:13 | 8 |
  | R1 | 20:42 | 20:49 | 7 |
  | B6 | 22:08 | 22:25 | 17 |
  | B7 | 22:29 | 22:41 | 12 |

- Each named mutation is its own rebuild of dll_core_test, one `/bigobj` file that includes every tested source.
- A combined mutation build is not valid for S3-L1: its "snapCount = P+S" mutation also breaks the stack-ring indexing (see L3), so more than one check would fail.
- `-Mode Publish` runs every C++ test executable and both dotnet suites before the Native AOT compile (out/build-3615.log).
- The main session's serial path is L1 → L2 → M1/M2 → F1/F2, with about 13 mutation builds, then the publish and the live checks. That comes to roughly 85-110 minutes.

**Fix**
1. Build S3-L1 and S3-L2 as one cycle: one red and one green commit, each item keeping its own mutations (step 2's U5-U8 precedent).
2. Run only the mutations that guard safety:
   - `stackRing` set before the parameters-only return in ArmLocked;
   - the `snapCount +` offset in StackRingAt;
   - the parameter readers looping over the parameter rings only;
   - the lone gate including stack choices;
   - the stack budget in its own words;
   - D10's drop rule;
   - the stack bound test;
   - the `__try`;
   - the anchor (copy from the anchor, not from `raw[0]`);
   - the ret−1 rule.

   Mark the rest "owed".
3. Put both helpers on the UI:
   - Helper A: S3-U1, then U2, then U3.
   - Helper B: S3-R1, then U4 and U5, starting from A's U1 commit.
4. Docs:
   - S3-X0 becomes only the ledger file, written by the main session in about 3 minutes.
   - The plan's "Step 3 design" section and the pipe-protocol.md subsection move to S3-X1.
5. Skip R1's red run on build 3640. It is red by construction: S0 requires `trace.stack`, which TraceInfoToJson (Fern.cpp:1746-1778) never emits. That saves a game launch.
6. Take cuts 4 and 5 now, not when late.
7. The bench lines become optional.

Even with all of this, the publish and live checks (about 20 minutes) must start by T+45.

## MED

### M1. The UI's stack loading stops at the first page with no items
**Problem**
- The design's §5.4 loop breaks on `Count == 0`.
- CopySnaps returns `next = end` with zero items when every slot in the window is an orphan or out of turn (Linie.cpp:807-816).
- Orphans are exactly the oldest stack slots once the trace ring has lapped, which happens on any long recording.
- The load would then stop at the first all-orphan page and show no stacks at all.

**Fix:** copy step 2's loop (CallTraceViewModel.cs:364-375):
- `for (from = ring.FirstValid; from < ring.Written;)`;
- break only on `Next <= from`;
- take the ring windows from the first reply's `rings`.

### M2. S3-U5 case 5 expects the wrong value and fails on a correct build
**Problem**
- The test expects `LastAsm == "0x140001234"`.
- But MoveViewAsync strips the prefix before calling the bridge (AobMakerActions.cs:79-84).
- ScriptedAobMakerBridge records exactly what it receives (AobMakerActionsTests.cs:483-487).
- So LastAsm is `"140001234"`.

**Fix:** expect `"140001234"`.

### M3. The old-DLL guard misfires when every stack choice is refused
**Problem**
- Fern emits `trace.stack` only when the stack ring count is above 0.
- If every stack key is refused but ticks or parameters are accepted, the Start succeeds without `trace.stack`.
- The UI's guard, as specified (`start.Trace?.Stack == null`), then stops and releases a valid recording, saying the DLL is old.
- `StartNames.Stacks` as a plain int cannot tell "absent" from 0.

**Fix**
- Parse `names.stacks` as `int?`.
- Old DLL = stacks were asked and `Names.Stacks == null`.
- Refused = `Names.Stacks` < the number asked; the existing `Refused` list already names them.

### M4. Chained unwind is deferred, but S3's live check and the `known` label depend on it
**Problem**
- GetFunctionExtent (Macht.cpp:116-135) does not follow `UNW_FLAG_CHAININFO`.
- If ProcessEvent's call to Invoke sits in a chained (shrink-wrapped or hot/cold) fragment, the function start found is the fragment's, not ProcessEvent's.
- Then no frame is labelled `known:"process_event"`, S3 fails live, and view A shows "+0x… into …" instead of ProcessEvent.
- I have not verified whether the 5.8 fixture has such a fragment.

**Fix**
- Pull a FollowChain helper into S3-M2. It is about 12 lines:
  - read the UNWIND_INFO at `base + UnwindData`;
  - while `Flags & UNW_FLAG_CHAININFO`, take the RUNTIME_FUNCTION after the `(CountOfCodes + 1) & ~1` unwind codes;
  - also handle `UnwindData & 1` indirection.
- Test it on a synthetic buffer.
- At the least, demote S3's `known` half to "recorded, not failed", like S4.

### M5. Inlining can break S3-M1 case 2 in the test build
**Problem**
- dll_core_test includes Macht.cpp into one Release `/O2` file (dll_core_test.cpp:62, dll/CMakeLists.txt:677).
- Microsoft's `_ReturnAddress` docs say inlining changes the result; `_AddressOfReturnAddress` is affected the same way.
- If CaptureCallerStackEx is inlined through CaptureCallerStack into the noinline CaptureFromHere, `here` equals `retSlot`. The bound test then returns BadSp, and case 2 fails at green.
- Separately, cases 3 and 8 recurse 40 and 20 deep. If the recursive call is in tail position, the compiler turns it into a loop: case 3 then gets fewer than 16 frames and no More flag.

**Fix**
- Put `__declspec(noinline)` on CaptureCallerStackEx and on CaptureCallerStack.
- Make the recursion do work after the recursive call.

## LOW

**L1. Stack-overflow edge**
- The 32 KB headroom ignores SetThreadStackGuarantee.
- If EXCEPTION_STACK_OVERFLOW lands in our `__except`, the guard page is not re-armed, and the thread's next overflow ends the process silently.
- Fix: call `_resetstkoflw()` in the handler when the code is STATUS_STACK_OVERFLOW, or add the thread's queried guarantee to the headroom.
- Also add `here >= low` to the bound test. A stack switched without updating the TEB is harmless today (the walk returns about 0 frames, so Partial), but the check makes it explicit.

**L2. Locks:** frames outside any image send RtlLookupFunctionEntry through the dynamic-function-table path and may call a registered callback. This is the same exposure as any C++ exception thrown on that thread. Record it under "what does not hold".

**L3. The "snapCount = P+S" mutation is undefined behaviour:** StackRingAt then reads past the ring array, which corrupts the heap instead of failing cleanly. Use "SnapRings / InfoLocked loop over P+S rings" instead.

**L4. The S3-L2 mutation "StackAdmit shares snapTotalWindow" is ambiguous:** sharing the word with the stack cap refuses earlier, it does not admit. Spell it as "uses snapTotalWindow with snapTotal".

**L5. Comment gates and stale comments**
- The StackWrite snippet's comment cites `(:812)`, an in-repo line number, which `check_comment_refs` rejects.
- Macht.h:188-190 says "The four SEH sites are…"; there will be five.
- Fern.cpp:1764 says the `snap` block appears "only when parameters were chosen"; a stacks-only Start also allocates it.
- Under D10, flag 16 can now appear on lone and excluded calls, so these go stale:
  - the comment on `kTraceSnapBudget` (Linie.h:183-184);
  - the row for flag 16 in pipe-protocol.md (:748).
- Run `py tools/verify/comment_impact.py --staged`.

**L6. UI gaps missing from §5.2**
- These ignore `_stackChosen`:
  - TraceSecondsForComparison (LiveFuncsViewModel.cs:509): a stacks-only trace is scoped, but the estimate uses the unscoped call rate;
  - the "needs trace" note (:851).
- EstimateSnapshots returns early with no parameter choice (:463), so a stacks-only Start gets no K or TooSmall figure.
- The JSONL header writes a `snap` block for stacks-only, with 0 rings (CallTraceExport.cs:51).
- The export fix drops the `lone` key for stack-only lone calls, because that key is written inside AppendSnapshot (:153).

**L7. pe_snap_get kind:stack**
- The 1 MB page cap must count the `sites` JSON bytes, not only the items.
- Memoise DescribeCode by module base, so GetModuleFileNameW runs once per module.
- Use `Utf8Helpers::LeafUtf8` (Utf8Helpers.h:196) instead of copying Genau's file-static helpers.
- GetModuleFileNameW with `MAX_PATH` truncates long paths: use a 32K buffer.

**L8. AOT and pins**
- In the Call stack tab's grid, use compiled bindings with `x:DataType="vm:StackFrameRow"` and `CanUserSortColumns="False"`. The reflection-based DataGrid sort is a recorded AOT trap (CLAUDE.md).
- The Linie.h pin regex `(\d+)` (LiveFuncsSnapshotTests.cs:166) cannot read `0x8000`. Also pin entry flags 32 and 64 and the stack-slot flags, in decimal or with a widened regex.

**L9. Reading `depth` in Fern:** read it through the guarded integer lambda (Fern.cpp:4536-4540). `st.value("depth", 16)` throws on a non-integer.

**L10. The 2 ms/s claim is about counts, not time**
- The budget caps the number of captures, not their cost. 200/s gives 2 ms/s only if a capture costs 10 µs or less on average.
- Because the first calls of each second are kept, up to 200 captures can land in the first frame or two of each second.
- Consider a provisional total of 100 until the live check (S6) measures it, then apply M3 as planned.

**L11. Rig check S2:** compare module names ignoring case.

**L12. Register pin:** a new `### … ⬜` heading under "Pending live-game verification" changes `open_verification_batches` (check_derived_counts.py:102-115). Either add the step-3 rows under an existing batch, or update the claims in todo.md and verification-register.md in the same commit.

**L13. CopyStacks:** copy CopySnaps' roughly 20-line loop rather than refactoring step 2's tested CopySnaps into a template, given the time box.

**L14. Not every listed test is red:** S3-L1 case 12 and S3-U1 case 2 pass before the change. Label them as guards.

**L15. Anchor drift (harmless)**

| The design cites | The code is at |
|---|---|
| dll_core_test.cpp:6972-6990 as the throwing-copier pattern | that block is a throwing clock; the throwing copier is :8254-8372 (S6) |
| Linie.cpp:806-807 for CopySnaps' clamp | Linie.cpp:800-801 |
| dll/CMakeLists.txt:667 for `/EHsc` | dll/CMakeLists.txt:677 |
| dll_core_test.cpp:63 for the Macht.cpp include | dll_core_test.cpp:62 |
| Helpers/AddressHelper.cs | ui/UE5DumpUI/Core/AddressHelper.cs |
| IAobMakerBridge.cs:63-68 for NavigateDisassemblerAsync | IAobMakerBridge.cs:69 |

## Corrected ledger order

**Main session (all C++ in one tree)**

1. **S3-X0:** the ledger file and its docs/README.md row only. The plan section and the T15-T19 decisions move to S3-X1.
2. **S3-L1 + S3-L2:** one red and one green commit, with the mutations listed under H1. Apply L3, L4 and L13.
3. **S3-M1 + S3-M2:** includes FollowChain pulled in from S3-M3 (M4), `noinline` and tail-call-safe recursion (M5), and `_resetstkoflw` (L1).
4. **S3-F1 + S3-F2:** one DLL build (`build_dll.py --targets UE5Dumper dll_core_test`) and `check_all`. Their green is R1's S0-S6. Apply L7 and L9.
5. Merge both UI branches, then `dotnet test`. A run that executes zero tests counts as a failure.
6. **S3-X2:**
   - `-Mode Publish`, then check the size (about 54 MB) and the SHA;
   - `proxy_refresh.py refresh` on DumperTest58;
   - rig R1 green;
   - UI walkthrough steps 1-4 and 6, with Copy checked through the clipboard;
   - kill the game, CE and the UI.
7. **S3-X1:**
   - the plan's "Step 3 design" and "Step 3 built";
   - the pipe-protocol.md subsection, its flag rows, and the L5 fixes;
   - the dev-log entry;
   - register and todo rows (L12);
   - the comment pass.

**Helper A (UI):** S3-U1 first, with M3 applied; commit it so B can start. Then U2 (with L6), then U3.

**Helper B:** S3-R1, with no run on 3640 and L11 applied. Then S3-U4 (with M1) and S3-U5 (with M2 and L8), both on top of A's U1 commit.

**Dependency changes**
- R1 no longer depends on X0.
- F1's red is "by construction".
- S3-M3 is folded into S3-M2.

**Next, if time is left:** S3-U6, S3-U7, S3-A1, S3-R2.

**Deferred, unchanged:** S3-B1, S3-E1, S3-P1, S3-O1.
