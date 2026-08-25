# UE performance counters in the UI — what an injected DLL can and cannot measure

> Moved out of [todo.md](todo.md) on 2026-08-25 for the same reason as its sibling `output-monitor-pin-eval.md`: a finished evaluation with a tiered verdict belongs beside the other `*-eval.md` docs, not in a list of things still to do. Nothing was edited, only moved.

-----

## UE performance counters in the UI — EVALUATED (2026-07-23), tiered

**Verdict: the literal ask — surfacing UE's own `stat` counters — is impossible from an injected
DLL. But the two cheapest tiers are worth more than the literal ask, because they measure the thing
[multipipe-eval.md](multipipe-eval.md) already blames for UI lag and currently has zero telemetry for.**

- **Tier 0 — WON'T DO: UE's `stat` system.** Shipping builds compile with `STATS=0` (even the *Test*
  configuration defines `STATS 0` by default), and the console is **removed from the binary** in
  Shipping, not hidden. Re-enabling needs `FORCE_USE_STATS` and an engine recompile. Unreachable from
  an injected DLL — record as WON'T-DO so it isn't re-litigated.

- **✅ Tier 1 — DONE (build 2308).** New `Sense` module + `get_diagnostics` pipe command + a
  System-tab card. Records per-command dispatch cost (count / total / max / last) at Fern's existing
  `inFlight` chokepoint — which is exactly the head-of-line window — and reports `busy_percent`, the
  fraction of wall-clock a dispatcher was occupied. **That is the number Phase 1 was missing.**
  Also carries game-thread health from Stark and the GObjects count. *Original note kept below for
  the rationale.*

- **Tier 1 — our own health. Zero new machinery, highest value.**
  [multipipe-eval.md](multipipe-eval.md) already names DLL-side **serial-dispatch head-of-line
  blocking** as the root cause of UI lag and game-thread CPU starvation as the CE-mailbox risk — yet
  neither is measured, so Phase 1 would be decided blind. Free to collect: per-command Fern handling
  time + queue depth; Stark invoke queue depth / timeout count (`invoke_timeout_ms` is already
  reported over the pipe); per-worker tick count + write-on-drift hit rate for Solide / Hemmung /
  Laufen / Solitar / Schlacht; Aura `NumElements` over time (GC/leak indicator). **Linie already
  computes frame-cadence statistics** (per-UFunction fire counts + Welford mean/cv) — it just isn't
  presented as performance. Effort **S-M** · Risk none.

- **✅ Tier 2 — DONE (build 2308).** Working set / private bytes / CPU% / thread + handle counts,
  in the same `get_diagnostics` payload. On demand only (thread count walks a system-wide snapshot).
  CPU% is `-1` until a second sample exists to difference against, and the UI renders that as an em
  dash — "0%" would read as *idle*, which is a different and wrong claim.

- **Tier 3 — real FPS / frame time: hook `IDXGISwapChain::Present`.** The only engine-version-
  independent, accurate source (true frametime, 1% low, pacing, present mode). **Shares its entire
  hook infrastructure with P2 of the output-monitor-pin evaluation above** — these two must be
  decided together and funded once, not twice. Effort **M-L** (joint) · Risk med (overlay-shaped
  behaviour; per-graphics-API work).

- **Tier 3.5 — `GAverageFPS` / `GAverageMS` via AOB.** These are plain engine globals
  (`GAverageFPS = 1000/GAverageMS`), **not** gated by `STATS`, so they survive Shipping, and Himmel's
  128-pattern infrastructure could carry a signature. But it is a per-version/per-compiler signature
  to maintain and yields the engine's *smoothed average*, strictly worse than the Present hook. Keep
  only as the fallback if we decide never to hook DXGI.

- **Tier 4 — reflected time values.** `AWorldSettings::TimeDilation` (Hemmung already reads it) and
  `UWorld::TimeSeconds` / `RealTimeSeconds` / `DeltaTimeSeconds` (not `UPROPERTY` — needs DynOff
  probing). Caveat: `DeltaTimeSeconds` is the **game-thread** delta only (no render/GPU) and is
  polluted by time dilation — usable as context, **not** as an FPS readout.

**Status: Tier 1 + Tier 2 SHIPPED (build 2308). Tier 3 still deferred to the monitor-pin P2
DXGI-hook decision; Tier 0 remains WON'T-DO.**

**Follow-on deliberately NOT built: per-worker tick counters** for Solide / Hemmung / Laufen /
Solitar / Schlacht (tick count + write-on-drift hit rate). That is five modules touched for a number
that does not bear on the dispatch question — and the dispatch question is the one that blocked a
decision. Worth doing if a re-assert worker is ever suspected of burning game-thread time. Effort
**S-M** · Risk low.

**✅ Automatic PERF records — DONE (build 2320).** `Services/DiagnosticsProbe.cs` brackets **Copy CE
XML / Copy CE Field / Value Scan (First & Next) / Snapshot capture** with two `get_diagnostics`
snapshots and logs the delta as a `PERF` line in the `view` log. Better than the manual measurement
session it replaces: a deliberate test only covers the scenario somebody thought of, and only if they
remembered to reset first — this accumulates evidence from real use.

**✅ ANSWERED (2026-07-23, build 2324) — and the answer is "don't build Phase 1".** Measured on
Elliot (UE 5.4) + SEED (UE 4.27), 24,178 dispatches across 5 real Copy CE XML / Copy CE Field runs.
Full table and reasoning in [multipipe-eval.md](multipipe-eval.md) §10.

- **Dispatcher busy 29.8%** — idle ~70% of wall-clock, and the ratio holds (22-31%) across
  operations from 2.6 ms to 5.4 s. Non-blocking dispatch can only recover a slice of the busy 30%,
  and only if something were queued behind it — in a single-user export nothing is.
- **Worst SINGLE dispatch: 14.3 ms** out of 24,178. Phase 1's premise is a long-blocking command
  holding the read loop; no such command exists here.
- Phase 1 was already **shipped and reverted once** (build 1840) and a correct version needs
  overlapped/async pipe I/O. Not a trade worth making for this.

**The real lever is CALL COUNT.** `walk_instance` is 100% of dispatcher cost in every row, and one
Copy CE XML issued **20,357** of them: **0.088 ms in the DLL vs 0.208 ms of round-trip overhead —
2.4x the work is overhead.** Batching it at the established ~200/call chunk (as
`search_properties_batch` / `walk_class_batch` already do) would collapse 24,178 round-trips to
~121. **✅ SHIPPED build 2329 — `walk_instance_batch`.** The measurement said dll 27-30% / **ipc 59-73%** /
ui 0-10%, i.e. per-call round-trip overhead roughly 2x the actual walk, so the calls were collapsed
(chunk ~200). Built to the `walk_class_batch` precedent with all three safety layers: a DLL handler
that is a trivial loop over the single-call path, a shared serialiser/deserialiser pair, and an
equivalence test comparing both paths field-for-field. The CE export now walks breadth-first per
level. A failed batch — or a short/long reply, which would otherwise mis-pair results with addresses
— replays the chunk as single calls.

**✅ DONE + MEASURED (build 2335): 1.71x faster.** Copy CE XML on SEED went **5,893 -> 3,437 ms**,
dispatches **22,522 -> 1,355**, IPC **3,532 -> 1,278 ms**. `top:` names `walk_instance_batch`.
(Build 2329 had batched the wrong loop - the calls come from the STRUCT tree, not the
object-pointer drilldown; fixed with a breadth-first `PrefetchStructTreeAsync` feeding the
unchanged depth-first emit, since that emit's order IS the exported field order.)

**The 2.4-3.5x projection was wrong, and usefully so - IPC is not purely per-round-trip.** At the
old 0.157 ms/call, 1,355 calls should have cost ~212 ms of IPC; they cost **1,278 ms**. So of the
original 3,532 ms, ~2,253 ms was fixed per-round-trip cost (removed) and **~1,066 ms is
payload-proportional** (untouchable by batching - the same bytes still cross). `ui` rose 610 -> 653
ms for the same reason. Full table in [multipipe-eval.md](multipipe-eval.md) section 10.5.

**Next lever, if anyone wants more: BYTES, not messages.** Remaining 3,437 ms = dll 1,506 (real
work) + ipc 1,278 (mostly payload) + ui 653 (parse). Trimming fields the CE export never reads would
hit the payload-proportional IPC *and* the parse cost together. Note also that raising the batch
chunk would achieve nothing: average batch size is ~16.6 (fan-out-limited), not near the 200 cap.

**✅ MEASURED (build 2339) — `scripts/analysis/walk_payload_audit.py`.** Byte-accounted a real
Copy CE XML on SEED against a key-by-key map of what the exporters read (full table in
[multipipe-eval.md](multipipe-eval.md) section 10.6):

- Per-field keys (52.7% of the sample): **60.9% used / 18.6% CSX-only / 16.7% unused.**
- Inline array elements (20.3%): **43.9% used / 44.6% unused** — `elem.h` (element raw hex) alone
  is 9.0% of the whole payload and no exporter reads it.
- The per-instance header (`name` / `class` / `outer_*` / `props_size` / even `addr`) is **99%
  dead** — the export touches `result.Fields` and nothing else.
- Verdict: **~24% of the payload-scaling bytes are droppable outright, ~38% if CSX opts out of
  `hex` too.** Biggest single items: `elem.h`, `field.hex` (CSX-only), `field.value`,
  `field.array_inner_addr`.

**✅ SHIPPED (build 2351) — `lean: true`.** `walk_instance` / `walk_instance_batch` take a `lean`
flag that omits exactly those keys (drop list in [pipe-protocol.md](pipe-protocol.md); design notes
in [multipipe-eval.md](multipipe-eval.md) section 10.7). Subtractive only, so an older DLL that
ignores it stays correct. Wired to the CE XML export path ONLY — CSX shares the same
`ResolveDrilldownAsync` and genuinely reads `hex` / `bool_mask` / `bool_byte_offset`, so the default
stays full-fat. `WalkInstanceLeanTests` proves lean and full payloads produce **byte-identical XML**
(mutation-checked: blanking a key the exporter does read fails it).

**✅ IN-GAME VERIFIED (build 2353, SEED).** Same object exported before (DLL 2338) and after
(DLL 2353): **payload 1,982,875 -> 1,168,944 bytes over the same 134 batch responses = -41.0%**,
matching section 10.6's prediction. The XML is unchanged — 149,621 lines / 14,326 leaves both
sides, 15 differing lines and every one a per-session value (root address + FName ComparisonIndex,
name half identical). DLL serialise time -20% (146.7 -> 116-119 ms), consistent across both runs.

**Still open — the wall-clock.** On that small export `ipc` did NOT move (207 -> 213-216 ms) even
though the bytes nearly halved: at ~15 KB/response over 134 calls, IPC is dominated by fixed
per-call cost.

A **bigger lean run exists** (2026-07-23 22:09, SEED `BP_LifeGameInstance_C`, depth 4, 13,845 structs
/ 54 pointers): wall **2,086.6 ms**, 302 dispatches, split **dll 832.4 (39.9%) / ipc 704.3 / ui
549.9 ms**, and **10.16 MB of lean payload** across 241 batch + 65 single responses (~39 KB per batch
response — 4x the small run). It has **no before-side**, so it measures where the time sits now
(DLL-bound) rather than what lean saved. Two cheap ways to close it:
(a) re-run the same export against the pre-lean DLL (build 2338) for a true A/B; or
(b) export the **same object as CSX**, which goes through the same `ResolveDrilldownAsync` with
`lean:false` — caveat: CSX additionally drills object-arrays / DataTable rows, so its walk set is a
SUPERSET and the comparison is an upper bound, not an equality.
While at it, re-run the payload audit with `UE5DUMP_PIPE_LOG_FULL=1` for an untruncated sample — the
1024-char body-log cap makes the whole-payload split read a flattering 39%.

*Parent: multipipe-eval.md Phase 1 (non-blocking dispatch) needs Tier 1 to be decidable; Linie
(dev-log build 2156) already holds the cadence half.*

-----
