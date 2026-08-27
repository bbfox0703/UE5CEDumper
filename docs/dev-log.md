# Dev Log

Append-only milestone history, newest first. Each entry references a
build number from `build_number.txt` so commits can be cross-referenced.
**Reading tip:** grep `^## ` for the index, then read the top (newest-first).
Entries for **builds ≤2168** are archived: builds 1800–2168 in
[archive/dev-log-2026-07-pre-build-2200.md](archive/dev-log-2026-07-pre-build-2200.md),
builds 1178–1799 in
[archive/dev-log-2026-06-pre-build-1800.md](archive/dev-log-2026-06-pre-build-1800.md),
builds 939–1177 in
[archive/dev-log-2026-06-pre-build-1180.md](archive/dev-log-2026-06-pre-build-1180.md),
builds 715–937 in
[archive/dev-log-2026-06-pre-build-940.md](archive/dev-log-2026-06-pre-build-940.md),
builds ≤696 in
[archive/dev-log-2026-05-pre-build-700.md](archive/dev-log-2026-05-pre-build-700.md).

> **Looking for current state?** See [roadmap.md](roadmap.md) for the
> capability matrix / per-game configuration / tested games, and
> [todo.md](todo.md) for the prioritized next-work list. This file
> records *what shipped* — the other two record *what works now* and
> *what's next*.

-----

## 2026-08-27 (later) - The crash fix turned into a hang, because a deferral said the second half was out of scope (build 3365, verified in-game 3366)

Build 3363 (previous entry) stopped OCTOPATH TRAVELER crashing with our `dxgi.dll` proxy. The game
then **hung with no window**, and its log stopped at `DllMain: auto-start thread created OK`.

That line being last is diagnostic by itself: a thread created inside DllMain cannot run until the
loader lock is released, so the auto-start thread never printing its own first line means **the
loader lock was never released**. `tools/pe/minidump_triage.py` walks the FAULTING thread and a
hang has none, so `tools/verify/hang_dump.py` was written to dump a live process and census every
thread's stack by module.

The dump named it exactly:

```
ntdll+0x6019B                  <- RtlAcquireSRWLockExclusive's wait (ZwWaitForAlertByThreadId)
dxgi.dll+0x2ACC90              <- our own .data: the SRWLOCK object itself
dxgi.dll+0x18993F              <- our thunk, SECOND pass
AcGenral+0x5D78/5C00/22E2/5420, apphelp+0x1E696/1F81F     <- this block appears TWICE
dxgi.dll+0x1889CC              <- SetAppCompatStringPointer thunk, FIRST pass
```

with all three DllMain-created threads parked in `ZwWaitForSingleObject` behind the loader lock.

**Our own `LoadLibraryW` re-enters us on the same thread.** Loading the real `dxgi.dll` makes the
loader raise `apphelp!SE_DllLoaded`; `AcGenral`'s DXGICompat hookset does
`GetModuleHandleW(L"dxgi.dll")` — which resolves back to **us**, because we are the module
registered under that name — and calls our thunk again while the resolver is still inside
`LoadLibraryW`. **SRWLOCK is documented non-recursive.** Self-deadlock.

### The part worth keeping

That lock is audit #4 **B43**, which removed it from the winmm twin for the sibling lock-order
reason and left dxgi alone, noting the dxgi original's safety argument was *"explicitly CONDITIONAL
on RHI init being the only entry point"*. Build 3363's own audit doc recorded finishing it as
**"NOT done — deliberately out of scope"**. It was not out of scope; it was the live defect, and
the deferral cost a full extra round trip through a real game. Logged as a new row in
[working-lessons.md §2.13](working-lessons.md) — *a deferral reason ages worse than the finding it
defers* — because the specific mistake was rating a **sibling's already-proven finding** as
theoretical in the flavour it had not been applied to.

### What shipped

- **The SRWLOCK is gone.** Correctness without one is B43's argument verbatim: `mProcs[]` stores
  are aligned and pointer-sized so they cannot tear, racing resolvers write identical values, and
  "nobody observes a half-populated table" is preserved by the thunk's own null test. The log line
  is claimed once with `InterlockedCompareExchange`, after the work — the winmm shape.
- **`Lugner::ResolveReentry`**, in all four flavours. A nested call returns at once; its thunk then
  sees an unresolved slot with `mResolveAttempted` still 0 and answers 0 **without forwarding** —
  exactly what ReShade's compat exports do. The **outer** call completes the resolve and forwards
  for real. ⚠ Deliberately not a "first caller wins" mutual exclusion: a loser returning with a
  null slot would hand the *game* a null `CreateDXGIFactory1`. Removing the lock is the cure; the
  guard is defence.

### Verified in-game (build 3366, 2026-08-27)

```
10:17:32.450 [WARN]  Proxy: 1 forwarded call(s) arrived BEFORE our CRT was initialised …
10:17:32.452 [INFO]  DllMain: auto-start thread created OK
10:17:32.456 [INFO]  dxgi proxy: lazily forwarded 20/20 exports to real System32 dxgi.dll
10:17:32.475 [INFO]  DllMain ProxyStart: proxy DLL mode — starting pipe server only (no scan)
10:17:32.984 [INFO]  DllMain ProxyStart: pipe server started
10:18:13.670 [SUMMARY] GObjects=0x7FF659775C10 GNames=0x20440C60010 GWorld=0x7FF6598590F8 Objects=406060
```

Two lines to actually read. The **pre-CRT warning is still there and that is the point** — it is
the shim engine's direct fingerprint, so the root-cause analysis is no longer inference. And
**`20/20` lands at `.456`, before `proxy DLL mode` at `.475`**: the resolve completed on the
*loader thread*, inside the shim's outer call, and only then did the loader release and let our
threads run. Under 3363 that resolve never returned.

⚠ `version.dll` / `dinput8.dll` have had no in-game regression run since 3363 — and they are also
the two flavours the offline rig provably cannot discriminate pre/post fix on. Queued in todo.md.

-----

## 2026-08-27 - The AppCompat shim calls our export before our CRT exists; four proxies fixed, and one "fix" that would have killed the export (build 3363)

OCTOPATH TRAVELER would not start with our `dxgi.dll` proxy deployed, while the **same 2.9 MB
binary** as `winmm.dll` worked in the same game. Root cause confirmed at instruction level from
two crash dumps and the shim engine's own disassembly; full dossier in
[audit-2026-08-26-dxgi-appcompat-crash.md](audit-2026-08-26-dxgi-appcompat-crash.md).

The game carries an AppCompat layer (`HKCU\...\AppCompatFlags\Layers` = `HIGHDPIAWARE`), so
`apphelp.dll` + `AcGenral.dll` load at module [4]/[5] — ahead of `msvcrt`. `AcGenral`'s
`NS_DXGICompat` shim does `GetModuleHandleW(L"dxgi.dll")` →
`GetProcAddress("SetAppCompatStringPointer")` → call, driven by `apphelp!SE_DllLoaded`, which the
loader raises when a module is **MAPPED** — before its init routine runs. Our export for that name
was a lazy asm thunk whose resolver logs; logging allocates; `__acrt_heap` was still NULL;
`HeapAlloc(NULL, 0, 0x20)` faulted in `ntdll!RtlAllocateHeap+0x54` and the process died before the
EXE entry point.

`winmm` survives because `AcGenral` names `dxgi.dll` / `ninput.dll` / `d3d9.dll` and **not**
`winmm.dll`: nothing enters our winmm thunks until the game's own code runs, ~2.0 s after DllMain.

### What shipped, across all four flavours

- **`Lugner::g_crtReady`** — a plain `volatile LONG` with a *constant* initialiser, so it lives in
  zero-filled `.data` with no dynamic initialiser and is readable before the CRT exists. Set by
  `Lugner::MarkCrtReady()` as the **first statement** of `DLL_PROCESS_ATTACH`.
- **A pre-CRT gate on every resolver** — `DxgiProxy_EnsureResolved`, `WinmmProxy_EnsureResolved`,
  `LoadRealVersion`, `LoadRealDinput8`. ⚠ It **refuses outright** rather than doing the
  kernel32-only resolve the plan called for: that would have been allocation-safe but *not*
  loader-lock-safe, and the pre-CRT caller is the loader itself. Nothing is latched, so the host's
  own first call resolves normally. This is what ReShade does.
- **`Lugner::g_preCrtCalls`** — an `InterlockedIncrement` counter reported once from DllMain as a
  `LOG_WARN`. Without it the refusal is completely invisible: no log, no crash, nothing.
- **The thunks now tell two null cases apart**, via a new `mResolveAttempted` data symbol. This is
  the part that needed care: the obvious "return 0 on null" would have silently reversed B44/B48's
  recorded decision, where a stub returning 0 is *worse* because winmm's `0 == TIMERR_NOERROR`
  would make a missing `timeBeginPeriod` silently no-op the 1 ms tick. `mResolveAttempted == 0`
  (resolver refused) → `xor eax,eax / ret`; `== 1` (name genuinely absent) → keep the deliberate
  loud `jmp rax` with `rax == 0`.

### The change the fix forced, which was not in the plan

`Lugner.cpp` (17 sites) and `Lugner_Dinput8.cpp` (6) cached with
`static auto fn = reinterpret_cast<Fn>(RealProc("..."));`. **A magic static evaluates its
initialiser exactly once and latches the result forever** — so adding the gate above without
touching them would have latched `nullptr` on a pre-CRT first call and left the export dead for
the life of the process. That is a *worse* failure than the crash being removed: silent, permanent
and invisible. All 23 became `static Fn fn = nullptr; if (!fn) fn = ...`, which also drops the
CRT's `_Init_thread_header` machinery off a path that must not need the CRT.

### An unrelated defect found on the way

`scripts/gen_proxy_forwarders.py --check` reported **`Lugner_Winmm.cpp STALE`** *before* any of
this work: the AD18 `SystemDllPath` fix had been hand-applied to the generated .cpp and never
back-ported, so re-running the generator would have silently reintroduced the
drive-root-relative-path defect — and that check is **not in CI**. AD18 and the new changes are
both in the generator now; `--check` is clean.

### Verification

`tools/verify/proxy_precrt_gate.py` maps a proxy with
`LoadLibraryExW(..., DONT_RESOLVE_DLL_REFERENCES)` — image mapped, exports callable, **DllMain not
called** — and calls a thunk from a child process so a fault is an exit code. The pre-fix
`out/proxy-backups/Avowed.dxgi.dll.20260823-212124.bak` faults `0xC0000005` at **`+0x187A2E`, the
same RVA on the faulting stack of both Octopath minidumps**; `dist/proxy/dxgi.dll` returns cleanly
at `+0x1889CC`. Same result for winmm. 13/13 gates and
`check_proxy_exports --artifacts` pass.

⚠ The rig provably **cannot** discriminate pre/post fix for `version` / `dinput8` (plain C
forwarders, not asm thunks — their pre-fix path does not fault in this harness), and it says so
rather than printing a green tick. Their gate is verified by construction only. **The in-game
Octopath run is still owed** — see todo.md.

-----

## 2026-08-26 (night) - The three log-audit findings, fixed; an adversarial design review changed the shape of two of them (build 3362)

The three open findings from the P3R log audit (previous entry). Each was designed, then faced a
safety lens and an honesty/scope lens before a line was written — and that was not ceremony: five
of six verdicts came back fatal, and two of the three fixes are **not** the shape I would have
shipped from my own analysis.

### `[SANEPROPS]` — one constant answering two questions

`kMaxSanePropertiesSize = 1 MB`, whose comment claimed *"even the largest generated Blueprint /
UClass layouts are at most tens of KB"*. P3R refutes it with `XRD777SaveGame` (3,671,800) and
`AstreaSaveGame` (3,671,816), both of which the same log shows walking their fields cleanly.

Split into `kMaxGapFillBytes` (1 MB, **unchanged** — the byte-sweep work cap the 827 MB Elliot
wedge exists to stop, and also a WIRE bound, since `GuessGapTypes` emits ~N/4 rows for an N-byte
gap) and `kMaxPlausiblePropertiesSize` (**64 MB** — admission control for caches that are never
erased). ⚠ 64 MB is an admission bound, not a fact about UE; it sits ~17× above the largest real
sample and ~13× below the observed garbage, and both samples are in the header so the next person
re-derives rather than re-guesses.

⭐ **My own analysis had two errors, and the review found both.** I concluded "nothing scales with
PropertiesSize after the gate" — wrong: gap-fill emits one row per 4-byte stride, so a 3.67 MB
class is ~918,000 rows serialised with no cap. (The split still holds, because that site uses the
work cap — but I knew that by luck, not by measurement.) And I recorded the cache refusal as
"only costs a re-walk". It does not: `WalkClassEx` refuses to **RETURN**, handing back an empty
`ClassInfo`, and Aura's caches read `WalkClassEx(cls).Address != cls` as the refusal signal — so
both `USaveGame` classes were invisible to **Value Search, Group Scan, snapshot capture, CE export,
Solitar and Solide**, with no log line at all. That refusal now logs.

⚠ **Writing the test reproduced A10 by accident.** One class blob reused across four cases had
every case after the first read the first one's memoised answer, because `s_walkClassExCache` is
keyed by address and nothing erases it. One blob per case, and the comment says why.

### `[FUNCDENOM]` — a denominator that counted classes contributing nothing

*The wire field was not the problem.* `scanned_classes` is correctly named, correctly documented,
and one of the three UI sentences ("N classes scanned") was already reading it correctly. What was
wrong was every sentence phrased as **provenance**.

So the contributing count is **derived** — a computed property over the rows the panel is
displaying — with **no new wire field**. Two reasons, both found independently by the reviewers: a
parsed field would be `0` in every VM fixture (they all override `ListAllFunctionsAsync` and bypass
the parser), and a `??` fallback fires on *absent*, not on *zero*, so a DLL that shipped the key and
missed the assignment would render *"9,760 functions from 0 of 2,293 classes"* — a new wrong report
at the one site the fix exists to protect.

⚠ The DLL's log line is **append-only** on its format string: nothing in `dll/src` carries
`_Printf_format_string_`, so MSVC diagnoses no format/argument mismatch anywhere here, and a
reordered trailing `%s%s` reading an `int` as a `char*` is an access violation on the pipe worker
inside a shipping game.

### `[STALLDEFAULT]` — a default posing as a measurement

Fixed by **withholding** `game_thread_stalled` when it is not a measurement — the cheapest correct
answer, not a compromise: absence is already a live wire state and it costs fewer bytes.

⚠⚠ **The naive version of this fix is a regression, and the review caught it.** The hook can go back
DOWN mid-session (`Frieren.cpp` → `Stark::RemoveHook()` on a validation failure). Today a banner
raised by a real stall is cleared **by the lie** — the next `false` clears it. Withhold the key
without teaching the client that absence *withdraws* the claim and that banner sticks ON for the
rest of the session. DLL half and client half therefore landed in one commit.

A tri-state **string** was rejected on a measured path, not a guess: `GetValue<bool>()` throws
`InvalidOperationException`, `PipeClient`'s read loop catches only `JsonException`, so the throw
escapes, the loop exits, and every in-flight request fails with "Pipe disconnected" — it would
**disconnect** an old client, not degrade it.

`get_diagnostics` had a **second report path** with the identical defect, rendered to the user as
"Responsive" in the System tab. It gains `liveness`; `responsive` stays unchanged so an older UI
does not start rendering "Stalled" on a healthy game.

Three rigs relied on the lying default and were **armed, not weakened**. ⚠ One of them,
`seethrough_arm_b.py`, read the key with `[...]` **between `suspend-tid` and `resume-tid` with no
try/finally** — a `KeyError` there would have left the game thread SUSPENDED and the process
needing a kill.

---

**Method note worth keeping.** The `PERF` denominator fixed in 3361 and `[SANEPROPS]` are the same
sentence in different files: *a fix that landed in one of its two copies*. In 3361 it was `busyMs`
corrected and `dispatches` two lines below it not; here it was `PathStepToBreadcrumbs` marked and
`PopulateFromWorld` not. Three instances in one day, in three unrelated modules. The grep that
finds them is aimed at the CONCEPT, not at the diff.

C++ suite green (265 + 1649 + 22 + 14 + 17), C# **4,750/4,750** with six negative controls across
the three fixes, gates **13/13**.

## 2026-08-26 (evening) - The P3R log confirmed the fix; auditing the same logs found two more reporting defects, and named the title that closes row 5 (build 3361)

The maintainer re-ran the reported game. `Logs\P3R` (build 3360, `e88190ba-dirty`, proxy DLL,
UE427, 65,021 objects) against the 3358 session on the **same actor in the same game**:

| | 3358 (broken) | 3360 (fixed) |
|---|---|---|
| nav | `NAV→ KernelActor addr=0x64AF68C0 off=0x0` | `NAV→ KernelActor addr=0x101646B80 off=none` |
| spine | `KernelActor(P,0x0,68C0)` | `KernelActor(P,none,6B80)` |
| export | `bcCount=2` | `bcCount=1` |
| AOB | `AOB=True` | `AOB=False` + `AOB requested but root is not GWorld` |

⭐ **`AOB=True → False` is the sharpest line in the whole affair**: build 3358 wrapped the bogus
chain in a **GWorld AOB script**, i.e. it made the wrong address *survive a restart*. And the
"before" artifact was on disk the whole time — `Y:\Copy CE XML.xml`, saved 09:47 — which measures
identically to the after: root `gworld_addr_9E30D6`, then `KernelActor (0)` at `+0`, and the
actor's real address `64AF68C0` appearing **0 times in 1,288 entries**. That is precisely what the
unit test asserts when the row marker is removed.

**Then the logs were audited rather than merely read** — five diverse lenses over both sessions, a
refute-mandated skeptic per finding (19 of 40 killed), and a completeness critic. It changed the
conclusion twice, in both directions.

⚠ **Two corrections to what this session had already claimed.**
1. "The two sessions are apples-to-apples" was **too strong**. They differ in injection mode
   (proxy vs auto-start), AOB hint-cache state (`scan #1` vs `#17`), `array_limit`, and whether
   AOBMaker was up. The narrow comparison — same PE `69CE343916376000`, same actor, same class,
   same gesture — holds, and that is what should have been said.
2. The P3R logs witness the **mechanism**, not the **bytes**: no export's XML content is ever
   logged, and a `bcCount=1 / AOB=False` pair is also what a **GameEngine-rooted** export would
   print — and that session rooted one on GameEngine at 10:50:13. Here the same line's `BC=` trace
   names a two-crumb GWorld spine, so it is disambiguated; but the signature alone is not.

**That second point was a real gap, and it is now closed in code.** The re-anchor announced itself
only through `StatusText`, which reaches no log and is overwritten by the next status. `LogReanchor`
now says it outright at both export sites:

```
CEXML re-anchored: dropped 2 offset-less hop(s); root is now (level actor) @ 0x2B8B783A8D0
                   (absolute, session-only — no pointer chain from GWorld exists)
```

**Two reporting defects fixed, and both are the same shape as the bug that started the day.**

⭐ `[PERFDENOM-2026-08-26]` — the `PERF` line printed three numbers over **two denominators**:
`11 dispatches` beside `busy 15.2 ms` beside `per call: dll 1.524`, where 15.2/11 = 1.38. `busyMs`
is summed from the per-command rows, which deliberately skip the probe's own `get_diagnostics` —
there is a comment saying so, added when a 57.7 ms operation reported "161.2%" because the probe
was measuring itself. `dispatches`, **two lines below it**, still read the global counter. The
divisor was always `dispatches − 1`, on two games and two builds. *The fix landed in one of its two
copies* — the same sentence as the GWorld defect, in a different file. The existing test
**pinned** it (`R(3, …, one call)` asserting `"3 dispatches"`), sitting directly beside the test
that made exactly this correction for `busyMs`.

`[PERFPEAK-2026-08-26]` — `max` was a running high-water mark, so it was the worst call *since the
last reset*, not the worst in the operation; two exports minutes apart printed a byte-identical
`max 3.5ms` while their totals moved. `TopDeltas`' own comment said *"let the label carry the
caveat"* and the label said `max`. It now reads `top (peak = session high-water, not this op)`.

⚠ Fixing that label broke `Split_never_goes_negative…`, which asserted **no `-` anywhere in the
line** as a proxy for *no negative number* — and `high-water` has a hyphen. The proxy was corrected
to the actual predicate rather than the wording bent to fit it.

**Row 5 is closed, and the log tree named the title.** Three lenses guessed "needs a
World-Partition game"; the critic found the reproducer already on disk —
`Logs\TQ2-Win64-Shipping\ui-view-20260823-091415.log` from five days earlier, showing
`(world level)(S,0xFFFFFFFF,A960) > (level actor)(S,0xFFFFFFFF,FA60)`. Two `0xFFFFFFFF` hops in a
real spine, and nobody had ever pressed Copy CE XML on one — which is why the third defect stayed
latent. Re-run on **Titan Quest II** (UE507, 279,587 objects; the same `Actor` → 3 results → 🌍
Locate): the hops now render `none`, the export re-roots (`dropped 2 offset-less hop(s)`), and the
emitted table has **777 `<Address>` entries, exactly one absolute** — the level actor's own — with
`FFFFFFFF` appearing **0** times anywhere in the file. AA Script: `define(Actor,2B8B783A8D0)`.

Three findings stay **open** and are recorded in [todo.md](todo.md): `[SANEPROPS-2026-08-26]`
(a 1 MB sanity ceiling rejects two legitimate ~3.5 MB P3R SaveGame classes, and the *instance*
walker uses the same predicate to declare a live object stale), `[FUNCDENOM-2026-08-26]`,
`[STALLDEFAULT-2026-08-26]`.

Suite **4,738/4,738**, gates **13/13**, `dist/` republished AOT-trimmed (54.7 MB, sha `25e65203`,
byte-identical to the native output).

## 2026-08-26 (later) - The GWorld actor-chain fix, driven on a live game; and the sentinel was logged as `0xFFFFFFFF` (build 3360)

**Live half of `[GWORLDACTORCHAIN-2026-08-26]`** (previous entry). Rows 1–4 of its verification
register were run end to end against a running **DumperTest Shipping** (UE504, **24,479 objects**,
`dist/UE5Dumper.dll` injected, GWorld resolved by AOB `GWLD_TQ_1`). Full evidence is in
[todo.md](todo.md); the short form:

- The Offset column shows `—` for every Outer-derived actor/component and `0x30` for
  `PersistentLevel`.
- Copy CE XML from `PlayerStart0`: of **382** `<Address>` entries the copied table carries **exactly
  one** absolute address — the actor's own, as the root. The UWorld address appears **0** times.
- Copy CE AA Script: *"hardcoded address (GWorld path not forward-walkable)"*, script body
  `define(PlayerStart,1872FEDBE00)`, no GWorld walk.
- Control: `GWorld → PersistentLevel → LevelScriptActor` still emits the AOB-wrapped restart-stable
  chain with real offsets and no re-root. The fix did not over-reach.

⭐ **Two things made this cheap, and both are reusable.** The defect lives in `PopulateFromWorld`,
which consumes the DLL's `walk_world` reply and never looks at the engine version — so the
**already-granted DumperTest fixture stands in for the reported P3R (UE4.27) session** and no new
computer-use grant or purchased title was needed. And `clipboardRead` is granted, so the emitted
table was **read back as bytes** instead of being eyeballed in Cheat Engine: every claim above is a
count over the actual XML, not a reading of a screenshot. Most "needs CE" export rows are like this.

⚠ **One real find from the live run, and it was in the LOG.** The first pass printed the no-offset
sentinel through `0x{-1:X}`:

```
NAV→ PlayerStart addr=0x1872FEDBE00 off=0xFFFFFFFF ptr=True
```

That is meaningless as an offset **and** confusable with `+FFFFFFFF` — the emitted-table defect the
sentinel exists to prevent. A future reader grepping the logs for `FFFFFFFF` would have hit a
**correctly marked** hop and read it as the bug still present. `FormatCrumbOffset` now prints
`off=none` at all six sites (nav, struct-nav, synthetic-container rehydrate, both export pre-checks,
and the breadcrumb trace). Logs are this project's primary evidence channel — a sentinel has to be
legible there too.

## 2026-08-26 - "Start from GWorld" published level actors as fields at offset 0; every CE chain through one walked into the world's vtable (build 3359)

**`[GWORLDACTORCHAIN-2026-08-26]`.** Reported on **P3R** (`UE427`, 65,158 objects, build 3358):
Copy CE XML from an object reached through the GWorld actor list produced *completely wrong*
addresses. CE resolved the emitted table to

```
base            P->603BB0A0   8 Bytes  0000000144AF6408
  KernelActor (0)  P->144AF6408
```

`0x144AF6408` is inside the executable image (base `0x140000000`) — it is the value at
`UWorld + 0`, i.e. the **world's vtable pointer**. The actor really lives at `0x64AF68C0`.

**Root cause — a fix that landed in one of its two copies.** Audit #5 F8/F9 established that
`ULevel::Actors` carries **no UPROPERTY**, so the actor list is reconstructed from each actor's
`Outer` (`Aura::FindActorsInLevel`): there is no offset from UWorld to an actor, and no element
index either. `LiveWalkerViewModel.PathStepToBreadcrumbs` **already knew this** — a `LevelActor`
path step is stamped `FieldOffset = -1`, `IsPointerDeref = false`, with a comment citing F8.
`PopulateFromWorld`, which builds the same hop for the *Start from GWorld* list, was never given
the marker: it published every actor and component as `Offset = 0` + `isPointer: true`, which is
a positive claim that the actor sits at `[UWorld + 0]`.

Three consumers believed it, and the third is the one that shows the shape of the bug best:

| | path | what it emitted |
|---|---|---|
| 1 | Copy CE XML / Copy CE Field | `[GWorld] + 0` → the vtable (the report) |
| 2 | Copy CE AA Script | `gworldWalkable` requires `spine.Skip(1).All(bc => bc.FieldOffset >= 0)` — **the gate was already correct**; the crumb lied to it with a `0`, so it emitted a restart-stable walk into the vtable |
| 3 | Locate-in-GWorld → export | that path *does* stamp `-1`, and nothing downstream handled it: `$"+{step.Offset:X}"` formatted it as **`+FFFFFFFF`** |

So the marker existed, the gate that consumes it existed, and the emitter that had to survive it
did not. Row 3 was latent the whole time.

**Fix.**

- `LiveFieldValue.HasNoParentOffset` — "this row is not reachable from the current object by a byte
  offset". `Offset` deliberately **stays 0** (bookmarks and the same-layout row-reuse path key on
  it); the flag is what stops it being read as `+0`. `PopulateFromWorld` sets it on actors and
  components; `PersistentLevel`, which IS a reflected field of UWorld, keeps its real offset.
- Navigation stamps the existing `-1` sentinel rather than inventing a second representation, and
  skips `MapValueDrillOffset` so the sentinel cannot be turned back into a number.
- `CeXmlExportService.AnchorAtLastUnchainableHop` — re-roots the spine at the **deepest** hop with
  no offset and drops everything above it. Idempotent (index 0 is never examined, because a root's
  offset is not applied by any emit path), so the VM and the generator can both call it. Both
  export commands do, and say so: *"⚠ Chain re-rooted at KernelActor (absolute address,
  session-only)"* — the trade is restart-stability for correctness, and the user is told.
- `ProjectBreadcrumb` now **throws** on a negative offset instead of formatting it. That is what
  kills row 3, and it is a reachable invariant, not dead code: with the re-anchor removed, the
  export fails with *"spine still contains an offset-less hop 'KernelActor' … was not applied"*.
- The Offset column renders `—` instead of `0x0` for such a row.

⚠ **The offset column's Binding change broke its sort, and `DataGridSortWiringTests` caught it in
the same run.** `SortMemberPath="Offset"` was rooted by the column's own `Binding`; pointing that
at `OffsetDisplay` un-roots it, and under trimming the header goes inert. This is the **third**
disguise of that trap recorded in `LiveWalkerPanel.axaml.cs` (after a template-column conversion
and an element-syntax `MultiBinding`) — and here sorting on the display string would have been
*worse* than inert: it orders hex text, putting `0x9` after `0x10`. Fixed with a wired
`DataGridSortComparers.Number` entry.

**Verification.** `ui/UE5DumpUI.Tests/LiveWalkerGWorldActorChainTests.cs` — 7 checks driving the
real production path (stubbed `walk_world` → row → breadcrumb → clipboard XML), with **five
negative controls, each isolating one half**: remove the row marker → 4 red (and the copied XML
contains **no trace of the actor's address**, which is the reported defect reproducing); remove the
export re-anchor → 1 red, via the generator guard firing in production; remove the generator guard
→ 1 red; revert the offset display → 1 red. Full suite **4,734/4,734**, gates **13/13**.

⛔ **Not yet confirmed on P3R itself** — see `[GWORLDACTORCHAIN-2026-08-26]` in
[todo.md](todo.md)'s live-verification register.

## 2026-08-24 (later) - The C1 spawner fixture already existed; the repo's copy of the sample source did not know (build 3349)

**`[C1-SPAWNER-EXISTS-2026-08-24]`.** Found by accident while picking a queued-route fixture for
b636: `list_all_functions` on a running DumperTest reports **17** `DumperTestActor` UFunctions, and
they include `Spawn_Holders`, `Spawn_Decoys`, `Spawn_DestroyHolders`, `Spawn_CountHolders`,
`Spawn_Generation`, `Spawn_LateInstance`, `Spawn_RecycleChurn`, `Spawn_LastRecycledAddr` and
`Spawn_ManyComponents` — the entire set the classification doc drafted as **bucket C1, the one
fixture addition it said would unlock seven rows**, plus one it never asked for.

It is not a declaration stub: three `Spawn_LateInstance` calls moved the live
`DumperTestLateSpawn` population **2 → 5**.

⚠ **Two sessions concluded the opposite, including this one.** The check both made was to grep
`tools/ue-sample/DumperTest/Source/DumperTest/DumperTestActor.h` — which is dated **2026-08-19** and
contains none of it, while the packaged `DumperTest.exe` used for every verification run is dated
**2026-08-23** and contains all of it. **The repo's copy of the sample source is stale relative to
the binary that is actually used.** So grepping `tools/ue-sample` is not a valid way to answer "does
this fixture exist"; ask the running game. Earlier today I corrected an agent for saying the spawner
had shipped — the agent was right and the correction was wrong.

ℹ️ Recorded separately so it is not mistaken for a fixture defect: invoking any **parameterised**
`DumperTestActor` UFunction over the pipe returns `ProcessEvent error code -4`, while every
**zero-parameter** one returns 0. The split is exact and hits the pre-existing `AD4_*` functions as
hard as the new `Spawn_*` getters, so it belonged to the parameterised-invoke path, not to this fixture. **DIAGNOSED AND FIXED 2026-08-24** (build 3350): `invoke_function` sized its param buffer from the caller's `parms_size`, which **defaults to 0**, so omitting the field handed ProcessEvent a zero-length heap buffer and it wrote the return value past the end. Half the fault was mine for not sending the field; the other half is that the DLL had `ufuncAddr` and could read `UFunction::ParmsSize` itself, and the protocol doc called the field *optional (default 0)*.

## 2026-08-24 - The b637 return-value fix had a sibling it never covered: int64 (build 3348)

**`[RETINT64-2026-08-24]`.** Found while closing the b637/b644 verification row, not by looking for
it. That row asks a human to confirm a pointer return "shows a `0x` prefix" — a compile-time string
literal already pinned by C# tests, and blind to what build 637 actually fixed, which is a **read
width**: `readUFunctionReturn` has no `'pointer'` type, so the pre-fix spelling fell through to the
signed int32 default and read **four bytes of an eight-byte slot**.

Replacing that check with one that measures the width (`scripts/tests/return_read_test.lua`, the real
helper driven against byte-accurate stub memory) immediately showed the fix was incomplete.
`BakedScriptGenerator.cs:331` is `readType = displayType == "pointer" ? "qword" : displayType` — it
rewrites **only** `"pointer"`. `MapToHelperType` also emits `"int64"`, from two routes
(`Int64Property` at :522 and the size-8 `EnumProperty` case at :519), and that word reached the
helper verbatim where **no `'int64'` branch existed**. Measured: `0x0000000123456789` read back as
**591751049**. `UInt64Property` was unaffected — it maps to `"pointer"` and so rode the fix.

Fix: `'int64'` joins the 8-byte branch. **No sign folding**, and that is a property of the width
rather than an oversight — Lua integers are 64-bit two's complement, so the bytes CE returns already
*are* the signed value (`FF FF FF FF FF FF FF FF` -> `-1`, measured). At 64 bits `'int64'` and
`'uint64'` read the same bits and only the caller's format specifier differs; the 32-bit case is
different precisely because CE widens 4 bytes into a positive Lua number, which is AA20.

Two things worth carrying forward. **`-1` is a bad discriminator** — it passes even with the fix
removed, because a 4-byte signed read of `FF FF FF FF` is also `-1`; the test needs a value wider
than 32 bits. And **a Lua-only fix does not ship until the UI is republished**:
`scripts/ue5_invoke_helper.lua` is an `EmbeddedResource` of the UI (`UE5DumpUI.csproj:146`), served
through `GetManifestResourceStream`, and is not copied into `dist\` as a loose file. Verified after
republishing by byte-searching the AOT exe for the new branch.

## 2026-08-23 (evening) - The proxy advisory hid winmm; found by reading import tables, not code (build 3337)

**`[PROXYALTWINMM-2026-08-23]`.** Working the A6 offline bucket — the "Lushfoil proxy did not load"
row — the useful move was to stop reading our code and parse the games' PE import tables with
`tools/pe/pe_imports_exports.py`. Over every UE shipping `.exe` installed on this machine:

```
16 shipping exes:  14 import winmm   ·   13 import dxgi   ·   4 import version   ·   0 import dinput8
```

`ProxyImportAnalyzer.DescribeImportable` built its `alt:` list from **dxgi and dinput8 only** — so it
recommended the flavour **nothing** imports and suppressed the one **almost everything** does, even
though the analyzer has parsed `ImportsWinmm` since 2026-07-27 (`a2c81a0c`, *"teach the analyzer
winmm"*), winmm is one of the four proxies we build, and the class's own remarks group `dxgi`/`winmm`
as the *"pure static-import hijacks"* — the deterministic pair. On the 13 games importing both, the
user saw `alt: dxgi` and was never told winmm was equally available.

⚠ **Root cause is `working-lessons.md` §2.3, verbatim.** `ImportsWinmm` was appended to the record
**with a default** — the only defaulted member — so all four `Recommend` tests construct three
positional arguments and silently assert the no-winmm case. `DescribeImportable` was edited *again*
on 2026-08-10 (`c28e3a78`), two weeks after winmm was taught, and still not updated. The test file's
comment still read *"none of OUR three"*.

⚠⚠ **And the structural guard I wrote for it was VACUOUS on its first draft.** It asserted
`Display.Contains("winmm")` — but the corrected empty-case sentence is `no dxgi/winmm/dinput8`,
which *contains* `winmm`, so with the fix removed the guard **still passed** while the two
hand-written cases failed. Only the negative control exposed it. It now matches inside the `alt:`
segment. Final control: dropping the winmm line fails **all three**; restoring returns 51/51.

Suite **4,712 / 0 failed**, **13/13** gates, `dist/` republished AOT-trimmed.

ℹ️ **What this did NOT settle.** The Lushfoil row itself stays open. Offline forensics established the
proxy is present, correctly placed beside `LushfoilSim-Win64-Shipping.exe`, the right flavour (78
`version.dll` exports) and dated 2026-08-19, and that the exe was **not** patched (2026-02-22) — but
the exe never imports `version.dll`, which `ProxyImportAnalyzer`'s own remarks already document as
**normal and non-diagnostic** for that flavour (Lushfoil is named there among 11 games running a
working version proxy without the import; the load is a run-time `LoadLibrary`). So the filesystem
cannot say why it failed on 2026-08-21. **Actionable instead:** Lushfoil statically imports both
`dxgi` and `winmm`, either of which loads deterministically rather than riding a run-time
`GetFileVersionInfo` call — which is exactly what the advisory now surfaces.

## 2026-08-23 (later still) - AC13's untested metric gets two tests, and the route that does not work is written down (no build change)

**No product change; `build_number.txt` stays 3335/3336** — this adds tests only, and the final shape
needed **zero** production edits.

`PipeTransportStats` appeared in **no test source at all** — the sibling of the same defect family
(`ClassifySendFailure`) is tested only because it was split out as a pure function, while AC13's fix
is a `try`/`finally` **placement**. `[AC13-2026-08-22]` had already found the live row unobservable,
so the metric had no coverage of any kind.

**Two deterministic tests, each negative-controlled against a deliberately broken build:**

* the not-connected guard sits **above** the timer, so a refusal that sent nothing logs no 0 ms
  sample — control: moved the timer above the guard, **only that test failed**;
* `Snapshot()` is monotonic and converts ticks→ms correctly (record exactly `Frequency/100`, expect
  10 ms) — control: factor 1000.0 → 500.0, **only that test failed**.

Both controls reverted to an empty diff and a green 2/2. Suite: **4,709 tests, 0 failed, 34.7 s.**

⛔ **The positive half is still uncovered, and the attempt is recorded rather than repeated.**
`SendAsync` needs a live connection to reach the timer at all. `Constants.PipeName` is a hardcoded
`const`, so a test server would bind the name a running game's DLL also serves — the hazard behind
*"never run `pipe_client.py` while the UI is connected"*. An injectable `pipeName` ctor parameter was
prototyped and then **reverted with the test**, rather than left in production justifying something
that no longer exists. With it in place, `PipeClient.ConnectAsync` **reproducibly never completes**
against an in-process `NamedPipeServerStream`, while a raw `NamedPipeClientStream` with identical
arguments connects in **0.15 s** — measured at `maxNumberOfServerInstances` 1 and 4, on and off the
xUnit sync context, always with the server's own `WaitForConnectionAsync` reporting completed. The
dialled name was confirmed from the client's own log line, and a `PipeClient` aimed at an unserved
name throws at its 5 s timeout as designed. Not diagnosed; not an AC13 defect.

⚠ **The lesson that cost the most.** The first draft had an unbounded `await` in a helper, so it
**hung with no message** — and that is what made the full suite report `4708 succeeded / error: 1`
with **zero failures listed** after the host was killed. Bounding every await (working-lessons §2.7,
*a hang is not a test result*) turned it into a 10 s failure naming the exact line, which is the only
reason the wall above could be characterised instead of guessed at. The clean suite now runs in
**34.7 s** — the earlier 9m46s was entirely the hang.

## 2026-08-23 (later) - A sibling clip found by sweep, and the sweep became gate 13 (build 3336)

**`[DUMPHDRCLIP-2026-08-23]`.** Closing the classification doc's A6 item *"`[FORCESTATUSCLIP]`
sibling `.axaml` sweep"*. `DumpExplorerPanel.axaml:28` carried `TextTrimming="CharacterEllipsis"` as
the last child of a **horizontal `StackPanel`, behind four fixed-width buttons** — the exact
structure fixed on 2026-08-22 elsewhere. A `StackPanel` hands each child its **desired** width, so
the trimming can never fire and the text is hard-cut with no ellipsis and no tooltip.

The tail is again the part that matters: `BuildHeader` emits
`UE {ver} · {module} · … · {DumpedAt}`, so the first thing lost is the **dump timestamp** — the field
that says whether the dump is stale. Fixed like the precedent: `DockPanel`, buttons `Left`-docked,
`HeaderText` as the fill child, plus `ToolTip.Tip`.

⭐ **The narrowing is the reusable part.** The naive query — bound `TextBlock` in a horizontal
`StackPanel`, no tooltip — returns **138 hits**, nearly all short scalars (`PoseX`, `ArrayLimit`).
That is `working-lessons.md` §2's "~52% wrong" shape in miniature: a real structural pattern with no
severity filter is noise. The discriminator is **the author's own `TextTrimming`** — its presence
says they expected clipping and asked for an ellipsis, and the layout makes it impossible. 138 → 1.

⚠ **A dropout dropped for the wrong reason.** `ValueSearchPanel.axaml:694` has `Width="520"` and is
genuinely fine. But `MainWindow.axaml:338/353` were excluded by a first draft that examined only
**direct** children; their tooltips sit on the wrapping `Border`. The correct rule is *a tooltip
anywhere up the ancestor chain*, and the draft would have hidden a real case nested one level
deeper. The shipped check walks ancestors for `ToolTip.Tip` **and** `Width`/`MaxWidth`.

**New gate:** `tools/check_inert_trimming.py`, wired into `check_all.py` — **13 gates now**. This
defect class has shipped four times (`FORCESTATUSCLIP`, `V8PREVIEWCLIP`, `TYPECOLCLIP`,
`DUMPHDRCLIP`), which is what makes a one-off sweep the wrong deliverable. Negative-controlled
against the pre-fix file via `git show HEAD:…`: it reports the hit there and none on the fixed tree.

⚠ **Stop quoting the gate count** — it has been 4, 12, and now 13. The handover row and the memory
index were changed from a hard number to *"derive it from `N gate(s) run`"*.

## 2026-08-23 - The freeze abandon modal blamed the wrong cause; `ue5_freeze_helper.lua` -> 1.5 (build 3335)

**`[FREEZEFIRSTERR-2026-08-23]` — found by a verification row, not by an audit.** Closing AA3 step 5
(*"a permanent rescan failure must stop the writes"*) on a live suspended DumperTest, the abandon
modal read:

```
[ue5_freeze] DumperTestHolder: 3 consecutive rescans failed -- freeze STOPPED writing
(last error: mailbox busy (concurrent invoke or rescan)). This record has been unticked...
```

The row still PASSED — the freeze did stop, untick and print exactly once. But the **reported cause
was a consequence**, and structurally so:

* `waitDone`'s timeout path is `if not wok then return nil, 0, werr end` and does **not** clear
  `OFF_CMD` — deliberately, because the DLL may still write its reply later;
* so rescan #1 times out with `cmd` left set, and rescans #2 and #3 short-circuit on the in-flight
  guard (`:645-650`) in microseconds;
* `_lastError` was overwritten by each, and the message reported the **last** one.

So for the entire *"the DLL took the command and wedged"* family — a suspended process, a hung game
thread — the modal was **guaranteed** to name a transient concurrency cause for a permanent fault,
in the one place a user ever reads it, and to discard the timeout's actionable hint
(*"stale g_invokeMailbox address? re-inject, or re-enable the table"*). That is the distinction
CLAUDE.md's *"never report a mailbox failure by guessing"* exists to preserve.

**Fix:** track `_firstError` (set only on the 0→1 transition, cleared with `_lastError` on any clean
rescan) and report it, appending a differing latest one:

```
... freeze STOPPED writing (first error: mailbox timeout after 5008ms -- the DLL never picked
this up (stale g_invokeMailbox address? re-inject, or re-enable the table); then: mailbox busy
(concurrent invoke or rescan)).
```

⭐ **The live modal was reproduced in the rig before anything was changed.** `freeze_helper_test.lua`
AA31 stages the real sequence — a healthy start, then the fake DLL is killed by mutating the captured
`installMailbox` opts — and printed the byte-identical `(last error: mailbox busy …)`, failing. Its
control (every failure identical) passed throughout, so the case discriminates.

**Helper version 1.4 → 1.5, and the bump is load-bearing**: `versionLess` makes a same-version
re-load a **no-op**, so a 1.4 chunk already resident in a user's CE table would never have received
this. Two copies of the version exist (the doc block at `:90` and `THIS_HELPER_VERSION` at `:273`);
both moved.

⚠ **The bump broke two AA30 cases and silently weakened a third**, which is the more reusable
lesson: they hard-coded `'1.4'`/`'1.5'`, so with the file at 1.5 the *"an OLDER file does not
downgrade a newer resident"* case was re-loading a **same-version** file and passing on the wrong
branch entirely. AA30 now **derives** `OLDER`/`CUR`/`NEWER` from whatever the helper declares.
Negative-controlled: forcing `OLDER = CUR` makes the replace case fail.

Suite: **159 checks, 0 failures**. Gates: **12/12**. `dist/` republished AOT-trimmed — the helper is
an `EmbeddedResource` (`UE5DumpUI.csproj:150-152`), so the shipped exe carries its own copy and
*"Export Freeze Helper Lua File…"* would otherwise have kept writing 1.4.

## 2026-08-22 (later) - Eleven more register rows, the twelve-gate discovery, and a new single-entry handover (no build change)

**No source change to the product.** `build_number.txt` stays **3315**; the only code added is a
tooling script and two verification rigs. The entry below covers the work done *after* the one above
was written.

### ⚠ The finding that matters most for anyone reading this next

**CI runs TWELVE doc/source gates before it builds, plus a thirteenth over the built proxies — and
this session ran FOUR of them all day**, because four is what the docs named. `tools/check_all.py`
now runs the twelve in CI's order (which is not alphabetical: `aob_specificity` reads the TSV that
`extract_patterns --check` writes) and reprints CI's own failure text, so a local failure reads like
the one that would have appeared on the PR.

⭐ **Its first run failed** — `CeLuaQuotingTests` carried a literal user-home path (`<user home>\O'Brien\UE5Dumper.dll`) — quoting it verbatim here trips the same gate, which is a fair demonstration —
and `check_no_local_paths` rejects a concrete user home in a tracked file. That test had been
committed hours earlier against a green four-gate run. The apostrophe was the point of the fixture;
the home directory never was. Now `D:\O'Brien Studios\…`, with a comment saying why.

### Register rows settled

| row | outcome |
|---|---|
| `A6` step 3 | ✅ the Force walks the **super-chain, not the name** — `StaticMeshActor` (derives from `AActor`, name does *not* start with "Actor") **is** held, while 33 diffable non-derived objects including the genuine same-prefix `ActorSequence` are **untouched**. The pair is the proof; either half alone is consistent with the wrong matcher |
| `A6` step 5 | 🟡 CDO half ✅ (CDO clean through a 256-instance hold, with 12/12 sampled live components **actually** forced as the channel proof); spawn half **blocked**, measured three ways — the debug camera is one-shot per process, cycling it gives 295 → 295 objects, and no `ConsoleCommand`/`RestartLevel` exists in the 3,142 functions listed here |
| `AC13` step 1 | ✅ closing a **connected** UI leaves no `Pipe: ReadLoop error`; non-vacuous because `ui-init-0.log` shows the logger alive and flushing at the exact moment (`UE5DumpUI shutting down...`) |
| `AC13` steps 2–3 | ⛔ **unobservable as written** — there is no IPC figure on the System tab, and step 3's own action destroys its observable: the probe's closing `GetDiagnosticsAsync` sits under `catch { return; }`, so closing the game mid-request suppresses the PERF line entirely |
| `B10` | ✅ CLOSED — capture 644 objects / 12,155 fields, `wall 638.6 ms` recorded as a **new baseline** (the only prior figure is a different target and not comparable), and struct / enum / bool all decode. ⭐ Discriminating because the grid prints the **raw byte beside the decoded name**: `03 → ROLE_Authority` and `01 → DORM_Awake` are two different enums each decoded right |
| `A3` | ✅ CLOSED by step 3, and the 2026-08-19 measurement **reproduced to the digit** across a different build (3,450 candidates · 72 · 34 · 19 · 19) |
| `G11` steps 3–4 | ✅ answered as a measured **negative**: over 66 detection attempts across 23 hosts, `Tier 2` has **never** fired and `Tier 3` has **no subject**. The channel is proven — the ladder reached Tier 1 six times on three games |
| `AF25` step 3 | ✅ teleport opcode still 8 and the mirror **is** guarded — a negative control (`CmdTeleport = 9`) leaves the contract gate `CHECK OK` but fails **6 tests** |
| `V6/U8` step 1 | 🟡 attempted, **not closed**, and deliberately no defect filed — see below |

The 繁中 checklist went **38 → 33**.

### ⚠ Two rows where the *instrument* was the problem, not the product

* `V6/U8` — the Live Walker toolbar **reflows** once an object loads (`Find Refs` / `Related`
  appear), so coordinates captured earlier in the same session go stale silently. Two "press ▼"
  clicks landed on the **"2 matches" label**. The claim "the stepper stopped working after
  auto-refresh" was one sentence from being filed; it is unverified because the actuator was never
  shown to fire in that state. `working-lessons.md` §2.5d.
* `A3` step 1 — following the 繁中 step literally (*Value Search, **Float***) gives **0** vector
  components, a clean-looking FAIL of a working fix, because under LWC an `FVector` is a
  double-precision `FVector3d`. The warning was already in `todo.md` from 2026-08-19. **Read the
  register entry before running the mirror's step.**

### Docs brought to current

* **`docs/handover-2026-08-22.md`** — a new single entry point, replacing both predecessors, which
  moved to `docs/archive/`. It carries what a fresh session needs in its first ten minutes: the exact
  **computer-use grant list** (20, with their `request_access` names), the **≤7 batching rule** and
  the correction that **grants persist across sessions** (measured — a *reboot* is the real
  invalidation), `systemKeyCombos` being ungranted, **which Steam process is which**
  (`steam.exe -applaunch` to launch · `steamwebhelper.exe` to see the library · the
  `*-Win64-Shipping.exe` to inject, with a measured shim table for all nine granted titles), the
  dead-engine trap, the hard rules, the build/test/gate commands with their timeouts, how to drive
  Cheat Engine, what is open with a derivation for every number, and how to close a row.
* `todo.md`'s header was stale in two measurable ways (build **3263**, "30 open batches" against a
  derived **15**) — the file that says *"read this when deciding what to do next"*. Fixed.
* The OPEN FIXES INDEX heading said **3** while the table needed a fourth row
  (`[FORCESTATUSCLIP]`); added, with the observation that **none of the four is a straightforward
  code fix**.
* CLAUDE.md's `Schlacht` capability row still described the pre-fix hit resolution and claimed a
  verification that `[SEETHRUNOOP]` had just invalidated. Rewritten.
* The 繁中 checklist's own derivation instruction said to subtract **the two** preamble headings; a
  third was added on 2026-08-22, so following it literally now yields 34 against a correct 33.
  Reworded to count them rather than name a number.

-----

## 2026-08-22 - Unattended verification session: 8 defects found and fixed while working the register (builds 3309 → 3315, 42 commits)

**Shipped**: `build_number.txt` **3315**, `dist/` republished as the Native-AOT trimmed binary
(54.7 MB, `sha 8CA03D81BAAB`) with the DLL rebuilt alongside. Not a feature session — every fix below
was found by *running a verification row*, not by looking for bugs.

### The defects, in the order they surfaced

- **`[CADENCEGAP]`** — `Linie` dropped every same-millisecond inter-arrival sample (`>` where `>=`
  belonged), so a 17 ms callback reported the same 33 ms period as a 33 ms one. Seen in the UI:
  `CameraModifier` 496 calls / 17 ms against `ABP_Manny_C` 248 calls / 33 ms — twice the calls, half
  the period, where both had read 33 ms.
- **`[PARAMSSORT]`** — three grids sorted the **label** (`"9 (144B)"`) instead of the number, so
  Params ordered lexicographically. Fixed at all three sites, plus an address column that sorted as
  text. Two new AOT-sort guards; the click-through on the trimmed binary showed columns that
  actually discriminate (Params `19,19,19,17,17,16,15,15`; Offset `0x28…0x64`).
- **`[FREEZECFGNAME]`** — the freeze script bakes the class name into its runtime messages while the
  freeze itself reads `CFG.className`, and the product *instructs* the user to edit that CFG. Editing
  it made every message name the class you had just replaced. ⭐ The defect had been **pinned by a
  test** that used the baked literal as a convenient anchor, which is why it looked deliberate.
- **`[INVOKEHINTQUOTE]`** — an unescaped apostrophe (`"read it in CE's memory viewer"`) interpolated
  into a single-quoted Lua literal made the **whole `[ENABLE]` block a syntax error**, and Cheat
  Engine reports that by leaving `Active` at `false` with no dialog and nothing in the log. Any
  invoke with a large by-value struct return was affected. New `CeLuaQuotingTests` runs **19
  generators** through a Lua quote scanner — behavioural, because the offending value came from a
  variable ten lines above its use and a grep of the emitting line found nothing.
- **`[FORCESTATUSCLIP]`** *(filed LOW, not fixed)* — the Force status line is right-clipped at ~30
  characters with no trimming or tooltip, and the clipped tail is the clause whose own code comment
  says it exists because "on 256 instance(s)" reads as "all of them" without it.
- **`[SEETHRUTALLY]`** — `InvokeSetHidden` returns `bool` and **both call sites discarded it**, so
  `hiddenActors` / `hidden_count` / the UI card / the log's `disabled (N restored)` all reported
  **intent**. `Tick()` now records only what was applied.
- **`[SEETHRUNOOP]`** — and what that honesty exposed: on **UE 5.4** the object read out of
  `FHitResult` is a `UStaticMeshComponent`, so `InvokeSetHidden`'s Actor guard rejected every hit and
  **See-through was a complete no-op while every channel said it was working**. Fixed by trying
  `Actor` → `HitObjectHandle` → `Component` and taking the first that resolves to an actor
  (`ResolveToActor` walks `Outer`, bounded). ⭐ Cannot regress a working build: the first two members
  are still tried first, and `ResolveToActor` is the identity at hop 0 for anything already an actor.
- **`[DISTCOPY]`** — `build.ps1` reported a successful publish over a copy that never happened. A
  locked destination left `$exitCode` at 0, `Remove-Item` then deleted the correctly-built binary, and
  the success line printed the **stale** file's size — which is 54.7 MB exactly like a good one. The
  copy is now verified by **SHA256** and `dist/publish/` is kept when it fails.

### Verification closed

`Y10/Y13` (all four steps) · `MB3` (the CE half, through real `.CT` records) · `AA12/AA13` ·
`AE4–AE7` · `M1–M5` steps 2/3/4/5 and step 1's arms (c)+(d) · `B19` · `AF7` · `AF22/AF12/AF13` ·
`AC17` · `AF21` · `A11` · `A12` · `V11` · `Y12` · `W8` · Genau RIP decode (GNames + GWorld).
The 繁中 checklist went **50 → 35** sections.

### Two capabilities added because a row could not be verified without them

- **`seethrough_get_state` now returns `hidden_actors`** — the count alone could not be audited from
  outside, and it cost a full round of guessing (33 candidate actors walked, none hidden, no way to
  tell a wrong candidate set from a failed hide) before the set was exposed and the answer appeared
  immediately.
- **`tools/verify/seethrough_arms.py`** — two independent detectors that refuse to report a pass when
  the count never rises or when the DLL names actors whose own `bHidden` is not set.

### What this session is really about

Six of the eight defects are the same shape, and it is the one audit #4 named: **the report and the
reality computed by different code paths**. `[SEETHRUTALLY]` is the extreme case — the "reality" path
did not exist at all, which is exactly why the feature could be dead for as long as it was. Every one
of them was caught by insisting on a **second, independent witness** for a claim the system makes
about itself.

-----

## 2026-08-20 (evening) - Fourth pass on 3263: the UI and Cheat-Engine modes, 3 more defects, AA12/AA13 opened up

**No source change.** `build_number.txt` is **3263** and the tree is clean. ⚠ The caveat from the
entry below still stands and is worth repeating because it changes what a build means today:
`dist/UE5Dumper.dll` **was rebuilt** for the PEHOOK step-3b experiment (identical source), so its
bytes differ from the handed-over 3263 — and **`proxy_refresh.py` therefore reports all nine
deployed proxies as `*** STALE ***`, which is a FALSE ALARM**. `dist/UE5DumpUI.exe` was **not**
rebuilt and is still the 54.7 MB AOT-trimmed binary, which is what made the AOT-sort work below
possible without a rebuild.

This pass changed **mode** twice rather than finding more headless rows: first driving the real UI
with computer-use, then driving **Cheat Engine**. Both were productive, and the CE half found three
defects in one sitting.

### The UI pass — one batch closed, several completed

* **`PEHOOK` step 1** — the last non-UI-blocked step. Self-Test on a SIB-less build gives
  `✗ Add_IntInt(3,4) expected 7, got 0` with advice naming a **MIS-DETECTED vtable slot**. ⭐ Clicking
  it a second time returns a **completely different** string — *"The DLL REFUSED this call"* — because
  the validator had condemned the slot in between. Two state-appropriate advices from one button
  minutes apart is the row's headline claim (*the advice is chosen from `get_diagnostics`, not
  asserted*) demonstrated rather than argued, and it is the UI face of the `-3` refusal measured
  headlessly in step 3b.
* **`PEHOOKONCE` step 5 — batch CLOSED.** On a fresh Lushfoil the UI genuinely shows
  *"Connected — waiting for scan"* with **Start Scan** unpressed, so the pre-scan window is real in
  the UI and not only over the pipe. Live Funcs → Start before any scan gives the actionable
  *"Run a scan, then Start again"*; after a scan, Start again **without restarting the game** records
  **67 distinct functions / 98,236 calls**. The order-swap that used to poison the PE hook for a whole
  process now recovers inside one process.
* **`PASTECRASH` 4b + 4c** — and they turn out to be **two separate handlers**. The Copy button logs
  *"Clipboard copy FAILED — nothing was copied, the app is unaffected"* while the
  `Input-layer fault swallowed (#N)` counter does **not** move. 4b's safety half passes; its predicted
  undo residue did **not** occur (6 typed characters took exactly 6 `Ctrl+Z`), so the row's
  explanation of that residue should be treated as unconfirmed.
* **AOT sort — 2 grids → 8**, all on the `-Mode Publish` binary because a non-trimmed build passes
  with the bug present: Live Funcs, Detect Stats, Live Walker, Snapshot, Class Pivot, SPC Query.
  ⭐ SPC's is the largest sort exercised anywhere in the item — **12,153 rows** — which is where a
  per-row reflective comparer would be most visible. The **Props/Invoke picker** is the only named
  grid left and has **no fixture here** (it returns zero rows on every function tried).
* **`AF4`**, **`AE2`/`AE3` steps 1-3**, **`AF22`**, and **`L6`'s `X5` auto-snapshot clause** all pass.
  AE2's filter is recorded as the row demands (`DumperTest` → 22 results, 10 class-like then 12
  instance rows) and the three headers it discriminates on are mutually unmistakable — 1760 / 12 /
  928 properties.

### The Cheat-Engine pass — `AA12`/`AA13` finally reachable, and three defects

Launching CE flips the UI to **AOBMaker Connected**, which is what *enables* the per-row **Freeze**
button (`PropertySearchPanel.axaml:285`). That single fact unblocked a batch that had **six steps and
zero evidence**.

* **`AA12`/`AA13` step 1 — PASS**, with the release as its control: `TickCount` held at **9999** across
  10 s on a field that had been climbing ~1 Hz, the Lua window auto-closed, the record stayed ticked,
  and unticking let it resume **9999 → 10039 → 10048**.
* **Step 6 — PASS.** A float and a double freeze coexist (`555.5` / `777.75`); unticking one released
  **only** that one. The differing widths mean a shared write path would have shown up as one
  clobbering the other.
* **Step 2 — HALF.** The message half is exactly right (*"nothing was frozen: g_invokeMailbox symbol
  not found -- is UE5Dumper.dll injected?"*); the untick half fails.
* **Step 3 — the fixture was wrong**, and finding that out was the result: `NiagaraComponent` previews
  as `0 (CDO default)` but the freeze reports **2 live instances**.

**Three defects, all with reproducers:**

* **`[FREEZEUNTICK-2026-08-20]`** — a bail-out that applies nothing leaves the record **`Active=true`**,
  on **both** the helper-missing and DLL-not-injected paths. The generator *does* emit
  `if memrec then memrec.Active = false end`, and assigning the same property externally works — so it
  is the in-ENABLE assignment that does not survive. This is precisely what CLAUDE.md's rule forbids,
  and it makes `AA12`/`AA13` step 3 non-discriminating until fixed.
* **`[FREEZEINJECT-CRLF-2026-08-20]`** — "Inject Freeze Helper" reports
  `wrote 58345, stream has 57208`. The arithmetic is exact: the helper is **58,345 bytes with 1,137
  CRLF endings**, and 58,345 − 1,137 = 57,208. CE stores it LF-normalised; the check compares a CRLF
  byte count to an LF stream. **The write succeeds** (`findTableFile` → FOUND) — only the verification
  is wrong, but it tells the user the setup step failed.
* **`[CDOSCOPE-2026-08-20]`** — the `(CDO default)` marker is decided on an **exact** `ClassPrivate`
  match while Force and Freeze on that same row scope **derived**. A row can read "nothing live" and
  the action then hit two instances. Same exact-vs-derived split as `FREEZESCOPE` / audit #5 `A6`, one
  layer up.

### Method notes worth keeping

* **Read `memrec.Active` from CE's Lua Engine, never from the checkbox** — measured this pass: red ✗
  is **ACTIVE**, empty box is inactive. Reading the ✗ as "failed" would have inverted every finding
  above.
* **Check CE's title bar before ticking.** The process list reorders between openings, and one attach
  landed on `UE5DumpUI.exe`; a freeze against the wrong process fails in a way that looks like a
  product defect.
* **The CE-Lua hygiene contract is confirmed live**: `[Freeze] Started/Stopped` lines are silent by
  default and appear only after `UE5_DEBUG=1`.

-----

## 2026-08-20 (later) - Third verification pass on 3263: ST1 and PEHOOK closed out, 1 new defect, a §4.4 retirement, and one void run caught

**No source change.** `build_number.txt` stays **3263** and the tree is clean.
⚠ **Correction to the entry below it: `dist/` is NOT untouched any more.** PEHOOK step 3b needs a
DLL whose SIB pattern alternates are disabled, and the row sanctions building one; that build and
the restoring rebuild both overwrite `dist/`. Source is identical (git clean, `-NoBumpBuildNumber`
on both), so the binaries differ only by build non-determinism — but they are **not** byte-identical
to the handed-over 3263, and `proxy_refresh.py` now reports all nine deployed proxies `*** STALE ***`
as a result. **That report is a false alarm; do not act on it** (it compares SHA-256, and the sizes
still match exactly).

**Hosts, one at a time, each killed when its rows were done:** DumperTest Development (shipping DLL
*and* a SIB-less variant injected by path), **DQ7R**, **Lushfoil**, **EVERSPACE 2**, **Echoes of
Aincrad Demo**, **Satisfactory**.

### Two batches closed

* **`ST1` — all six steps, headless.** Two techniques made a batch whose table repeatedly says
  *"a paused/menu game"* and *"ordinary gameplay for a few minutes"* runnable with nobody present:
  **suspending the UE game thread** is a scriptable and strictly *stronger* form of every idle-game
  precondition (frozen = exactly 0 ProcessEvent fires, where backgrounding still ticks ~120/s), and
  where a log line structurally could not decide a step, **an observable side effect in memory
  could** — steps 4 and 6 were settled by watching `AActor::bHidden` flip after a resume, not by an
  absent log line.
* **`PEHOOK` — 1b/2/3/3b/4/5/6/7/8.** Only the UI Self-Test *text* (step 1) and the structurally
  unreachable 3c remain. Step 3 reached its **terminal 3/3 + "giving up"** state for the first time;
  step 5's false-positive guard was exercised for the first time (previous runs only showed the
  branch was never entered).

### One new defect

**`[INVOKEINHERIT-2026-08-20]`** — `Ubel::WalkFunctions` never climbs `SuperStruct`, so
`UE5_FindFunctionByName` can only resolve a function a class **declares**. `AActor`'s 140 functions
are unreachable on every subclass; 11 of 42 live objects can invoke *nothing at all*; and
`UE5_SetDebugCamera` resolves `ToggleDebugCamera` off the live CheatManager's class, so the shipped
Debug Camera toggle fails on any game with a derived CheatManager. Reproducer committed
(`invoke_inherited_function.py`, exits 1 while the defect stands). **Not fixed** — and the fix shape
carries a warning: do *not* make `WalkFunctions` inherit, because it is also what LISTS a class's
functions; the change belongs in the resolvers.

### A documented rule retired, and a blast radius measured

* **`working-lessons` §4.4's Everspace 2 evidence is withdrawn.** It recorded a KismetMathLibrary
  no-op diagnosed *while the hook was in the wrong vtable slot*, with the stub hypothesis "never
  re-verified against the corrected hook". It has now been re-verified on that same title:
  `Add_IntInt(3,4)` returns **7** at `vtable+0x278`. The failing pattern the section rested on has no
  surviving instance here.
* **The open `Serie::GetString` dropped-`Number` lead now has a measured blast radius** (it was
  explicitly "not measured"): 40 of 42 live objects are named differently by two pipe commands, and
  6 of 6 are **unfindable by the name the DLL itself reports** — the bare name silently resolving a
  *different* object. 71 call sites, 4 of which pass a Number. Still "do not sed it": the
  discriminator is display/identity vs matching.

### One run caught and voided

**`G3` steps 3+4 on Satisfactory produced a full set of coherent readings from a game that had never
booted.** Launching its shipping exe directly puts up *"Failed to open descriptor file
`../../../FactoryGameSteam/FactoryGameSteam.uproject`"* — UE resolves the `.uproject` relative to the
exe, and this title's exe lives in `Engine\Binaries\Win64\`. Injection, the pipe, and the scan all
succeeded against a dead engine, and the numbers looked *specific*: GNames and GWorld resolved (symbol
exports work as soon as DLLs are mapped), `GObjects=0x0`, and `ExtraScanGObjects: No valid
FUObjectArray found (763 candidates tested)`. Relaunching via `steam.exe -applaunch 526870` resolves
**all four globals, 137,425 objects** — with `gobjects` at **the exact address the failed run had
found and rejected as empty**, which isolates "array not populated" from "wrong address" by holding
the address constant. Satisfactory is exonerated, `test-games.md` was right, and **G3 3+4 have no
fixture on this machine at all**. Written up as `working-lessons` §3.w.

### Also settled

`AA2/AA3` step 3 (mailbox driven from Python, witness checked against `ReadProcessMemory` across 29
classes — and the row's assertion narrowed, because a derived listing's `classWitness=0x0` is
*correct*) · `U8` on a staged fixture (`Number := 8` written and restored; Value Search matched the
same 8 bytes, the bare string matching the CDO instead) · `L4/MB1` both rows (the invoke ROUTE made
observable by freezing the game thread: 5 ms + success vs 5006 ms + timeout) · `L10` step 6's
retention clause (a 30-day `TeleportCoords` file survives while a 30-day `Snapshots` group is deleted
in the same launch — the control that makes it mean anything) · `G11` steps 3 and 4 (Tier 2 has now
failed to fire for three *different* reasons; an offline exe scan proves the silence is correct on a
tag-stripped title) · Solide `capped` and `FREEZESCOPE` step 4 (unlocked by noticing Solide's pool is
**not Actor-only**: an 819-instance `ActorComponent` sweep gives `held=256, truncated=true`) ·
`PEHOOKONCE` step 3 in its literal pre-scan form.

### Rig lessons worth more than the rows

`working-lessons` §1 now carries **four** variants of one mistake, all found in this batch: a log
window coarser than the events it separates *reports a confident wrong answer*. Line-count slicing
across several growing files; a one-second timestamp watermark between cells milliseconds apart
(this one printed `FAIL` on a run whose own `result=-5` proved the opposite); a counter read outside
the timed window; and a byte offset recorded before a process start that **rotates** the log. The
reliable primitive is a before/after **count**. Also §1.y: `find_instances` without `exact_match` is
a *name substring* match, so "the first live instance of `Actor`" was a `UActorSequence` — and a
wrong-type invoke left queued can drain later against a non-actor.

-----

## 2026-08-20 - Second live-verification pass on 3263: 26 register rows settled, 2 new defects, 4 rigs (no build change)

**No source changed.** Everything below is `docs/todo.md` evidence, `tools/verify/` rigs and the
gitignored run ledger; `dist/` and `build_number.txt` are untouched at **1.0.0.3263**. Both CI gates
(`check_live_verification.py`, `check_audit_register.py`) stayed green after every commit.

**Hosts used, one at a time and killed after each group** (`working-lessons` §3.9): DumperTest
Development and Shipping, **The Adventures of Elliot** (UE504, 84,990 objects, `dxgi` proxy) and
**DQ7R** (UE**4.27**, 149,408 objects, `version` proxy) — the first UE4 title driven end to end in
this programme, and the largest install on the machine.

**Two new defects, both reproducers committed.**

* **`[STAGELOCK-2026-08-20]`** — `AC11` step 2's premise is wrong. `CopyProxyStaged` publishes with
  `File.Move(overwrite:true)`, and a target carrying an **image section** refuses the replacing
  rename with `ERROR_ACCESS_DENIED` (5), not the `ERROR_SHARING_VIOLATION` (32) the old direct
  `File.Copy` raised. .NET turns 5 into `UnauthorizedAccessException`, which is not an `IOException`,
  so it misses both arms of `DeployAsync`'s filter and the user is told **"Access to the path is
  denied."** — a message naming no path — instead of "File locked (game running?)". Established three
  independent ways: an OS-level probe (`tools/verify/ac11_locked_rename.py`), a throwaway xunit test
  against the real `CopyProxyStaged`, and finally **a live game** (Elliot running, Force Overwrite →
  `[EROR] … Access to the path is denied.`, status `ErrorOther`). `UndeployAsync` and the orphan
  sweep already carry the `catch (UnauthorizedAccessException)` arm; `DeployAsync` is the only one of
  the three without it, and the only one whose write became a rename.
* **`[ORPHANCANCEL-2026-08-20]`** (LOW) — cancelling a leftover-proxy cleanup mid-row leaves that row
  **untallied, still ticked, and its folder chain half-pruned**: the token is passed *into*
  `RemoveOrphanProxyAsync`, so it recycles the file and *then* throws, skipping `files +=`, `ok++`,
  `row.IsSelected = false` and `DropOrphanRow`. The log showed **three** recycles under a summary
  saying two. Audit #4's root cause verbatim — the report and the reality computed by different code
  paths.

**Batches closed.** Audit **L9** (AE13 / AE20 / AE30) is fully verified and its heading flipped, so
the register's open count went **40 → 39**. **L6** is complete but for the CE-only `X8` and the
maintainer-only `X12`; **`[AUTOREFRESH-2026-08-19]` is complete — all seven steps**; **F5** is
complete (steps 1–3).

**Things that could only be learned by running them**, now recorded next to their rows:

* `mtime` **cannot** witness a re-deploy — `File.Copy` carries the source's timestamp through
  `File.Move`, so the deployed proxy is byte-*and*-timestamp identical after a second deploy. Use
  `ctime`.
* `AUTOREFRESH` step 6's "drill → stays OFF" **expectation is wrong**: a field drill never calls
  `StopAutoRefreshTimer`, and what actually happens (re-target to the new root, 12 consecutive 10.0 s
  ticks) is better.
* `AC13` is **not observable as written** — the IPC figure only exists via `DiagnosticsProbe`, which
  returns early on exactly the disconnect the row prescribes.
* `AE27`'s Package filter is a **prefix** match on values beginning `//`, so `Script` matches nothing
  and reads exactly like the blank-memo defect it is meant to catch.
* `AF8`'s `force_field` takes **`kind`** (defaulting to `"bool"`), not `mode`, and a numeric `value`
  — the string `"-5"` parses to `0.0`, giving `held:0` that looks like a failed fix.
* A game window **steals focus back**, so a computer-use click on the UI behind it silently
  re-activates instead of pressing; `tools/verify/front_window.py` first.

**Rows that cannot be closed on this machine, with the measurement that proves it** — `G7`-style
results rather than "not tried": `Z8` (DQ7R's whole pool is 51,255 UFunctions, 51 % of the cap;
ratio ≈ objects ÷ 3, so ~300k objects are needed), `A7` (a full 149,408-object `FindByAddress` with
the deep cap at its 4,096 maximum takes **152 ms** — no window to disconnect inside), L3 step 1
(**none** of `GWLD_TQ_3/TQ_4/GOBJ_PS1/PS6` has ever won across **170 scan logs / 25 processes**),
`AD18`'s `dinput8` arm (**no** installed title imports the name, all 16 exes read), `Z13`/`Z12`-deep
and `AE30`'s control.

**New rigs:** `ac11_locked_rename.py`, `ac10_kill_midstream.py`, `ae20_orphans.py`,
`af7_af8_pipe.py`.

-----

## 2026-08-19 (night) - The first live-verification pass on build 3263: 13 register items settled, 2 new defects found, 12 rigs added (no build change)

**Nothing shipped and no source changed** — `build_number.txt` stays **3263**, `dist/` is untouched
(54.7 MB AOT UI, 2,879,488-byte DLL). This entry records the *verification* programme the handover
asked for, run unattended against the shipping build.

### Register items settled

| item | outcome |
|---|---|
| `AA38` 1/2/3 | ✅ re-confirmed on **3263** (the ✅ was earned on 3262). Refusal names `atcuf64.dll` — Bitdefender's own filter, present in every run on this machine |
| `B25` | ✅ **both** branches, on two purpose-built marker exes. 3,886 vs 10 log lines separates "swept the tables" from "refused before starting" |
| `Genau RIP decode` | ✅ both halves — candidates **4085 → 4083**, reproduced exactly over 4 runs; GWorld unmoved |
| `F5` 1+2 | ✅ 204,758 wire lines across two concurrent connections, 1,179 interleaved watch events, **zero malformed** |
| `MB3` | ✅ ordinary dispatch: 50 consecutive mailbox commands, 0 failures, 0 `[ERROR]` lines |
| `A3` 1/2/4 | ✅ **151 classes** contribute >1 FVector field. ⚠ the row's "use Float" is wrong on UE5 — LWC makes FVector a double |
| `FL1`/`FL2` | ✅ stale staging file swept, **fresh one survived** — the age guard proven, not assumed |
| `SE1` | ✅ announcement *and* **597 `[SCAN*]` lines** rerouted into `init-0.log` |
| `MB2` | ✅ foreground toggle 0→1→1→0 on a host with all three pointers `not_found` |
| `AB14`/`AB16` | ✅ 834 enum/byte candidates; the Origin filter **partitions 278+94=372 exactly** |
| `A6` step 3 | ✅ derivation, not substring — `Character`=1 vs `CharacterMovementComponent`=7, with a reachability control |
| `A8` | ✅ 7/7 on OCTOPATH. ⚠ the row's "(none available here)" was **wrong** — OCTOPATH is flat and installed |
| `A7` | ⛔ **not observable here, measured**: 0.11 s over 273,956 objects. Needs a pool ~100× larger |
| GROUP 7 | 🟡 **nine titles swept.** `U2` CPN **all-false** across UE4+UE5; **no Tier 2 line anywhere**; `X2`'s >5,000-class title found (Avowed `total_classes` 5,102) |

### Two new defects, both found in passing

* **`[RELAUNCHPIPE-2026-08-19]`** — a game that **relaunches itself** ends up with our DLL mapped and
  **no pipe server at all**. `UE5_StartPipeServer`'s one-shot `CreateFileW(OPEN_EXISTING)` guard
  races the dying first process. Reproduced 3/3 on OCTOPATH and **proven by repair**.
* **`[PROXYDEPS-2026-08-19]`** — six proxy objects carry no recorded header dependencies, so a `.h`
  edit may not rebuild the four shipped proxy DLLs.

Plus a blocking machine-state fact: **all nine deployed game proxies were stale** (pre-3263).

### Rigs added under `tools/verify/` (Python only — ad-hoc PowerShell is blocked here)

`inject.py` (with a stale-module guard) · `launch_dumpertest.py` · `build_dll.py` · `call_export.py` ·
`proxy_refresh.py` · `title_sweep.py` · `b25_marker_exes.py` · `genau_rip_ab.py` · `f5_envelope.py` ·
`mailbox_poke.py` · `fl_staging_sweep.py` · `se1_log_reroute.py` · `ab_radar_batch.py` ·
`a3_struct_path.py` · `a6_derivation.py` · `a8_flat_layout.py`.

### Method notes that cost real time and are now written down

**`working-lessons.md` §3.8** — this machine has **two Visual Studios**; `build.ps1` takes the newer
(MSVC 14.51), so pointing a builder at 2022's `vcvars64` mixes toolsets and fails at LINK with
unresolved `__std_rotate` in a file you never edited. **§3.9** — "one game at a time" is a
**correctness** rule: a second injected host logs `pipe already exists … skipping auto-start` and
never scans, so every reading from it is an absence the injection itself caused.

⚠ **Three quantities are all called "the UE version"** — the cached `ueVersion`, the
`FindAll: UE Version = N` log line, and `get_pointers.ue_version` (which is **after** any runtime
raise). Avowed detects 503 then raises to 504; comparing the wrong pair manufactures a G11 false
alarm, and did.

-----

## 2026-08-19 - Audit #5 closed out (166 → 4 of 297) + the field-reported defect queue (12 → 1), in 32 commits (build 3263)

**One unattended programme, one entry.** This is the rollup for the 32 commits between `9062f08f`
(build 3261, the A12 fix) and `10b00cf8`. Two queues were emptied in parallel:

| queue | before | after | what is left, and why |
|---|---|---|---|
| audit #5 register (`check_audit_register.py`) | **166 open of 297** | **4 open of 297** — 0 HIGH · 0 MED · 3 LOW · 1 INFO | `AB9`, `A10`, `AA39`, `AB23` — each left open **deliberately**; reasons below |
| the field-found OPEN FIXES INDEX in [todo.md](todo.md) | **12** | **1** of the original twelve (`[STALEDLL]`(a), a maintainer-only file deletion) | the audit itself then surfaced **3 new deferred items**, so the index reads **4 rows** — see "the index is 4, not 1" below |

⚠ **NOTHING in this entry has been verified on a running game.** Every fix is unit-pinned,
negative-controlled, or rig-covered offline; the live checks are queued in todo.md's
`## Pending live-game verification`, which grew to **40 open batches** as a direct result. Treat the
whole programme as *shipped and unproven* until that register moves.

### The twelve audit layers (L1–L12)

Audit #5 scanned the **48,950 lines authored before 2026-06-01** that audits #3 and #4 structurally
could not reach. Closing it ran as twelve batches, one per segment, each ending in a negative control:

- **L1** `9270046c` — DLL engine (`Ubel`/`Serie`/`Genau`/`Aura`). Enum reads of size 1 stopped
  sign-extending the UHT `MAX=255` sentinel into a miss; the `FString` count cap went 256 → 8192
  after re-deriving that it bounds only a *garbage* count's allocation, not the hot path;
  `TOptional<FText>` decodes through `ReadFTextString` instead of reading `FText+0x10`, which is the
  `uint32` Flags.
- **L2** `ab0fd6a6` — `Radar` value scan. `EnumProperty` is now scannable (mapped to `UInt8` and
  added to the `NumericAll` union); integer parsing is base 10 unless `0x`-prefixed, so a leading
  zero no longer means octal-for-ints and decimal-for-floats inside one meta scan; the group witness
  assignment gained an augmenting-path repair so one leaf can no longer appear in two slots.
- **L3** `cda2f720` — headers / `Himmel`. **The durable one:** nothing validated a signature's
  `(instrOffset, opcodeLen, totalLen)` against its own pattern bytes. Now enforced twice — by
  `extract_patterns.py --check` *and* by a compile-time `ASSERT_RIP_GEOMETRY` over all five tables —
  and negative-controlled 6 for 6 against four historical defects plus two invented slips. Four real
  RIP-decode offsets were wrong and are corrected (PS1 23→21, PS6 14→12, TQ_3/TQ_4 3→0).
- **L4** `569a1d59` — `Mimic`/`Sein`/`Flamme`. `HandleInvoke` routed on `functionFlags`, a
  DLL-filled **output** field, so a second CE FIRE routed on whatever the previous command left
  behind. Fixed by re-reading the flags from the `UFunction` rather than by promoting the field to
  an input — the latter would have been a **meaning** change the contract hash cannot see.
- **L5** `e8893e5a` — the three standalone CE Lua scripts, all covered by the real `lua` 5.4.6 rigs
  in `scripts/tests/` (dissect 50→83, freeze 132→154, invoke 63→91 checks).
- **L6** `6fc00e4d` — `MainWindowViewModel`. Disconnect now resets **all** process-scoped panels
  (was 3 of ~15); the three long exports thread a real `CancellationToken`; the Dump All completion
  line is composed from the actual `DumpResult` counts instead of the file's byte length.
- **L7** `9ef5b8ca` — UI services: data durability, honest failure levels, VDF parsing.
- **L8** `a87706c7` — VMs + scoring. Four findings turned out to be one theme — *a truncation or
  deadline signal exists and is discarded before the user sees it* — so the wording lives in one new
  `Core/PartialResultNotice.cs` rather than in four new spellings.
- **L9** `d4fdd418` — ViewModels/Core/DTOs. Two rows were closed by **re-deriving against current
  code** and finding them already fixed, rather than by re-fixing them.
- **L10** `ec72d7c0` — Views / app root. **A user-sortable DataGrid column is AOT-safe only if its
  Binding path equals its `SortMemberPath`, or a comparer is wired.** Swept the whole tree instead of
  trusting the list: 34 grids / 162 sortable columns / **30 unsafe across 10 sites**, of which the
  findings named six. `DataGridSortWiringTests` now machine-enforces the pairing offline.
- **L11** `179d2f80` — the last LOW batch. One theme runs through all twelve rows: **the report and
  the thing it reports on were produced by different code.** The worst was the DataTable RowMap
  drill, whose crumb printed the DLL's true row total over a grid holding a fixed 64-row page.
- **L12** `5374e662` — 25 of the 26 INFO rows. See the gate work below.

### Mailbox contract 2 → 3 (`2c2a950c`) — additive, `MAILBOX_CONTRACT_MIN` stays 1

`[FREEZESCOPE]`: a Property Search row for an **inherited** field is keyed to the class that
*declares* it, so freezing a pawn's `bCanBeDamaged` submitted `Actor` and the exact-name pool
returned one incidental `ChaosDebugDrawActor` out of a 25,179-object level. `CMD_LIST_INSTANCES`
gained an opt-in `LI_IN_DERIVED` flag routed to `Aura::FindInstancesDerivedFrom`, plus
`LI_OUT_TRUNCATED` when the pool is capped.

**Why this is additive:** `MailboxData` grew only at its tail (`cmdFlags` in, `cmdOutFlags` out), the
flag defaults to `0` = the old exact match, the handler clears it after every use so it cannot be
inherited, and the 16-byte derived-page format is unreachable without it. A pre-contract-3 `.CT`
keeps the exact-match pool byte for byte. `check_mailbox_contract.py`'s golden block records why.

The same commit fixed `[FREEZESTUCK]`: an abandoned freeze reported its failure with a `print()` into
a Lua Engine window hygiene had already closed, while CE still showed a ticked record — so the user
was told a cheat was applied while nothing was written. The record now unticks from a one-shot timer
(deferred, because `Active = false` runs `[DISABLE]` synchronously and that block calls `stop()`).

### Gate work — the part that outlives the fixes

Five gates got stronger, and one had a hole big enough to matter:

- **`check_mailbox_contract.py` could not see five of the six copies of the layout** (`AA36`). It
  hashed `Mimic.h` and read `CeMailboxLayout.ContractVersion`, and was blind to both standalone CE
  helpers, the shipped `.CT`, and both offline Lua rigs. **A gate with a hole reads as covered.** It
  now compares **67 literals across those 5 mirrors** against offsets *computed* from the packed
  struct, and the registry is **closed** — an unregistered layout-shaped constant is a hard failure.
- **`check_audit_register.py` did not enforce ID uniqueness** (`69fa412a`). `AE11`/`AE12` each sat on
  two rows, so marking one closed closed both in every derived count.
- **`check_axaml_strings.py` was red for four commits** and was wrongly written off as pre-existing
  and a false positive. It was neither: `ec72d7c0` had resolved a `StaticResource` key by
  interpolation, so eight real keys became invisible to static inspection — **exactly the property
  the gate exists to defend.** Fixed at the call sites, not by exempting the checker (`a1bdd205`).
- **New rigs:** `tools/verify/compile_sdk_header.py` puts the real emitter's output in front of
  `cl.exe` (nothing in this repo had ever *compiled* a generated header — they were only read), and
  `tools/verify/pe_pattern_regression.py` pins the ProcessEvent pattern set across 22 shipped games.

### Field-reported defects (the OPEN FIXES INDEX)

`[PASTECRASH]` took three commits and is the one worth reading: a clipboard read that failed inside
Avalonia's `TextBox.Paste()` surfaced as an unobserved dispatcher exception and **terminated the UI
31 minutes into a connected session**. The guard swallows only what a pure, narrow classifier
positively identifies as a platform input-layer fault — and `4365a1eb` reverses part of its own
predecessor on a weighing, not a fact: a swallowed `Ctrl+X` orphans an undo snapshot, but that is a
no-op `Ctrl+Z` against a terminated process taking a loaded object tree with it.

Also closed: `[SDKHDR]` (the array extent was baked into the *type* string, so 5 of 75,342 emitted
lines were not valid C++), `[PEHOOK]`/`[PEHOOKONCE]` (a failed ProcessEvent **detection** was
permanent; three sentinels now distinguish re-armable from terminal), `[PIPEBUSY]` (at-capacity
logged an ERROR every second — 1,826 lines in 31.5 min, evicting real diagnostics as the log
rotated), `[CLASSTOTAL]`, `[CONTAINERCAP]`, `[SLOTSYM]`, `[STALEDLL]`(b), `[PROXYLOAD]` and
`[AUTOREFRESH]`.

**The index is 4 rows, not 1.** The original twelve went to one — `[STALEDLL]`(a), deleting a
6-month-old `UE5Dumper.dll` from CE's install folder, which is maintainer-only. But the audit work
itself surfaced three new **deferred** items that now sit in the same table: `[PROPSEARCHCAP]` (a
feature — Property Search has no Max control), `[VOLUMEROOT]` (three sites ask `DriveInfo` about a
mount point and answer about the host volume) and `[SCANIDENTITY]` (needs a product decision plus a
live game with mid-scan object churn). None is a regression from tonight.

### The four audit findings left open on purpose

`AB9` — DllMain does filesystem work under the loader lock; a half-fix is worse than the defect, so
it needs its own session. `A10` — needs the by-reference→by-value restructuring `U5` deferred; a
partial invalidation would dangle held references. `AA39` — its prescribed fix was **measured to be a
no-op**; do not re-raise. `AB23` — interning `GroupSlotMatch::ownerClass` touches `Aura.cpp` and
`Fern.cpp`, which **no test target compiles**, so it is in-game-only work.

### Method notes worth keeping

- **No test target compiles most of `dll/src`.** Every pure decision rule fixed in a `.cpp` this
  programme was *moved into a header* `dll_helpers_test` includes, and given a negative control.
  That is now the standard move, not an improvisation.
- **Refutations are results.** `bd9b6d1b` refutes "ninja records no header deps" — it was a
  measurement artifact (CMake emits rules into `CMakeFiles/rules.ninja`, so grepping `build.ninja`
  returns 0 and looks like breakage). `AA39`, `Z11`, `AD16` and halves of `MB2`/`SE1` were also
  refuted and are recorded as do-not-re-raise.
- **A PASS can carry a wrong procedure.** `141e8119` corrects verification records whose evidence
  never covered the step they closed — `V6`'s auto-refresh half could not have been performed on the
  build it was recorded against. Recorded as a half-pass in place, with the reason inline.
- **`9a8ddd24`**: two ViewModel tests set an `[ObservableProperty]` whose generated hook is
  fire-and-forget, then awaited a *second* call that raced the first. Measured in-process at 4,000
  iterations — 1.25% / 2.275% / 0.625% flake. Whole-process runs cannot sample this, which is why
  160 clean cold runs had proved nothing.

### Build

Native AOT publish clean at **build 3263**. ⚠ **3262 was deliberately skipped**: `dist/` already
carried a 3262 and so does the maintainer's second machine, and a plain `build.ps1` had since
overwritten local `dist/` with a **non-trimmed** 106.8 MB exe under that same number. Reusing it
would have made one build number mean three different binaries. Note that **every** `build.ps1`
invocation bumps `build_number.txt`, not just `-Mode Publish` (use `-NoBumpBuildNumber` to suppress).

-----

## 2026-08-17 - A12: the group scan re-anchors container elements too (build 3261)

**Audit #5 A12 closed — the register is now 0 HIGH / 0 MED.** A11 fixed the single-value scan
and left the group scan carrying the same defect: `RefineGroupCandidates` re-read a stored
ABSOLUTE address with no container re-anchor.

The RULE needed no work; this was the wiring, and the wiring is where the traps were. **A
pre-implementation adversarial review broke the first design twice**, and both breaks would have
been invisible offline:

1. **Deriving the scan-time buffer base from stride+intra** — what the single-value path does — is
   the strictly MORE dangerous of two equal-cost encodings here. The deep walk builds a leaf
   address through a recursion where the intra-element offset is easy to get wrong (the Map
   `.Value` side alone is off by `cfe.valueOffset`), and with a derived base a wrong intra makes
   `dataAtScan != nowData` on every pass — so the candidate is Repointed by that error,
   permanently, onto a real and plausible-looking neighbouring field. The base is now **stored**,
   and `RepointByBufferMove` needs neither stride nor intra: every leaf in a moved buffer shifts by
   the same delta. Two fields dropped from three hops as a side effect.
2. **The sparse count UNIT.** The walker's loop bound is `sa.MaxCapacity`; refine re-reads
   `sa.MaxIndex`, and `MaxCapacity >= MaxIndex` is enforced. Reaching for the local in hand would
   make `numAtScan` exceed `nowNum` for every TSet/TMap with a spare slot, so the shrink rule would
   **drop every sparse group candidate on the first Next Scan** — a fix that deletes valid results.
   Two named factories now stand between them; `maxIndex` being a parameter name is the defence.

Also corrected before shipping: the depth test was off by one as drafted (leaves are emitted with
`depth + 1`, so the rule is `leafDepth == 1`) and now lives in one place in a header the tests
compile; `LeafAnchor` is ONE sub-object placed before `int depth` rather than four loose scalars,
because `int -> int32_t` is an identity conversion and a partially-filled positional initialiser
would silently set `depth = 0`, which `deepVisitor`'s `if (lf.depth < 1) return;` turns into
"every deep group leaf dropped"; and **A4's compile-force claim turns out to be half false** — the
`int -> uintptr_t` narrowing half is a warning, not an error, because this repo builds `/W4` with
no `/WX`. Measured by compiling it.

`FieldDescriptor::anchor` is now documented single-value-sessions-only: `internDesc` interns group
descriptors per SHORT class name, so two distinct UClasses sharing one already share a descriptor —
harmless for display text, an address bug for pointer arithmetic. The anchor rides per-match instead.

17 assertions, two negative controls run separately. The `static_assert` had to be moved off the
controlled line — at depth 1 it turned the first control into a compile abort that could not show
its own must-stay-green half.

**1386 C++ / 4016 .NET assertions, 0 failed.** Live check owed.

-----

## 2026-08-17 - AA38 + F9 + A11: the three MED the morning's fixes created (builds 3245, 3247, 3253)

**Audit #5 AA38, F9 and A11 closed.** All three were spawned by the session that cleared the
original MED tier, and each was filed as "already re-derived and refute-passed". **A fresh
re-derivation plus a three-lens adversarial pass found all three fix shapes defective** — every one
would have produced a green commit with the defect intact. Two new rows filed: **A12** (the group
half of A11) and **AA39** (the honest Pass-1 residual of AA38).

### AA38 — an unanchored foreign-module candidate was published as a win (build 3245)

On a process where GObjects never validated at all, the Pass-2 multi-module fallback published a
GWorld out of an arbitrary loaded module. Seen on **both** corpus instances with `GObjects=0x0` —
`python.exe` and `Solarpunk` — i.e. on processes with no UE world to find.

**The filed fix was a measured no-op.** It prescribed tightening `ValidateGWorldBasic`'s
`world == 0` arm when the module is foreign. That validator is `bool(uintptr_t)` — a bare address,
no provenance — so the rule cannot live there without a hidden global; and on both repros the
winning pattern has `gworldAllowNull == false`, so `TryResolveMatch` rejects null **before** the
validator and the arm was never reached.

The question is where the ADDRESS came from, which the Pass-2 loop already knows. So the rule moved
there as a pure `constexpr Genau::AdmitMultiModuleCandidate(AnchorState, candIsMainExe,
producesAnchor)`. **`AnchorState` is an enum rather than two booleans deliberately**: the two-bool
shape has an unreachable combination whose parameter has to be documented *"meaningless here, pass
false"* — the exact "zero must never silently mean nobody said" trap the fix cites as its own design
principle. Three states, 12 legal rows, all asserted. `producesAnchor` threads through
`ScanForTarget` with no default, so all five targets state it and a sixth fails to compile.

`ValidateGWorldBasic` is untouched: `world == 0` is the only reason `GWLD_DI427_1/2` resolve at all.
The blunter alternative — skip `FindGWorld` entirely when GObjects failed — covers more but is not
strictly better: `ExtraScanGWorld` needs `Aura::GetCount() > 0`, so on a hard game it would delete
the `&GWorld` slot *and* every path that could recover it.

### F9 — walk_world enumerated a non-reflected field, twice over (build 3247)

`ULevel::Actors` has no `UPROPERTY`, so walk_world's reflected lookup could never match:
`actor_count: 0` on 2 of 2 games, always. **And the finding named only half of it.** The component
lookup 45 lines below has the identical shape — `AActor::OwnedComponents` is a private
`TSet<TObjectPtr<UActorComponent>>` with no `UPROPERTY`, asked for by name **and as an
`ArrayProperty`**, then read with `ReadTArray`. It was invisible because the outer bug meant that
loop had **never executed in production**; fixing only the actor half would have shipped a flat
actor list with no components.

Both halves are now derived structurally: actors = one GObjects pass for objects derived from AActor
whose **Outer is the level**; components = outgoing object-pointer edges deriving from
`ActorComponent` whose Outer is the actor. No offset detection, no latch, no constant — so all three
traps the finding warned about are structurally unreachable, and the expensive native-detector
option is rejected on the record.

Rather than copy a 70-line pool walk, `FindInstancesDerivedFrom` gained `outerFilter` + `totalOut`;
it already read and returned the Outer. The outer read is hoisted above the class read, so the
expensive half is paid only by the surviving minority of the pool. `truncated` now has one source
instead of two computed by different paths, and a cancelled pass reports `actor_total = -1` rather
than `0`.

### A11 — container-element candidates were refined at a stale address (build 3253)

**Three of the filed premises were wrong.** It was filed as deep/TSet-specific and A4-caused; it is
neither — the direct static path has emitted address-pinned TSet/TMap element candidates since
**V1a, build 927**, and a comment predating A4 says so. Its stated mechanism (rehash relocates
elements) is false in the engine source: `Rehash()` rebuilds the bucket table and relocates nothing.
And the half it dismissed — *"for TArray that is mostly fine"* — is where the real damage is:
`RemoveAtImpl` relocates the tail **in place**, so the pinned address stays mapped and returns the
**neighbour's value**. A sparse slot is likewise **reused** by the next Add. The three "stale = drop"
comments documented the benign outcome, which is why this went unnoticed for months.

Its prescribed fix would have fixed nothing (both producers end at the same `cand.addr = valueAddr`)
and would have required reverting A4's predicate to adopt.

Fixed with a pure `Radar::RefineContainerAnchor`. **The rule is index-aware, and that is the whole
design**: the obvious `{dataPtr, count}` stamp is a regression, because appending into slack
relocates nothing and every existing candidate is correct today. A buffer that moved is now
**repointed and kept** — those candidates were lost outright before. A freed sparse slot is caught
by its own allocation bit, the only exact witness when the address is byte-identical.

15 assertions and **two** negative controls: one deletes the alloc-bit arm, the other substitutes
the *plausible* rule and confirms the non-regression assertions fail.

**Totals:** 1369 C++ / 4016 .NET assertions, 0 failed. Live checks owed on all three.

-----

## 2026-08-17 - F8 + F2: ok_via_level had never fired, and a dropped lane latched the cancel forever (builds 3220, 3221)

**Audit #5 F8 (site 2 of 2) and F2 (MED) closed** — the last two of the original MED tier. F8's
first site is filed as **F9**.

### F8 — `ULevel::Actors` has no `UPROPERTY`

`RecoverViaWorldLevel` recovers a streaming / World-Partition actor through the
`ULevel::OwningWorld` back-reference, then confirmed membership by looking up `ULevel::Actors` and
scanning it. That lookup **cannot succeed**: UE declares
`TArray<TObjectPtr<AActor>> Actors;` with no `UPROPERTY` (verified in the vendored 5.8 tree), so
`FindFieldOffset` returns < 0 and the whole recovery bailed. The logs agree — **18 sessions with
`actor_count 0` and not one non-zero.** `ok_via_level` has never once fired.

Worse than a clean miss: the fuzzy name fallback can bind "Actors" to
`DestroyedReplicatedStaticActors`, which IS reflected — so the alternative outcome was scanning the
wrong array.

**The membership it was proving is guaranteed by construction.** The Outer climb exits ONLY with
`level = GetOuter(actor)`. Re-deriving that from a reflected array bought no information and added a
hard failure mode, so the lookup and the scan are deleted — no offset detection, no wrong-offset
risk. The element index goes with them: there is no reflected array to index into, so `-1` is the
honest answer, and it is the shape the UI already renders for the synthetic `WorldLevel` hop
directly above. The hop is typed `LevelActor` and handled beside it as a **navigation anchor, not a
pointer deref** — which stops CE export fabricating an offset for a hop that has none, and correctly
keeps the GWorld-walkable export gate closed.

⛔ The `GuessGapTypes` route the finding implies was rejected, and the rejection verified: it emits
only Padding/Pointer?/Float/Double/Int32?/Int16?/Byte?, and `NormalizeGuessedTypeToProperty` drops
the two labels a TArray header would produce.

The comment that made the code look right is corrected too — *"level → Actors → actor is a
reflected chain"* is precisely what is not true.

### F2 — emptiness was the wrong question

The per-command cancel cleared only on `firstConn`: a session connecting into an EMPTY registry.
Correct when there was one connection; **unreachable after the two-lane split** (PR #396). If the
BULK lane drops mid-scan while the LIGHT lane stays connected the registry is never empty, so
`g_perCommand` stays latched and **every subsequent scan on the surviving lane aborts instantly for
the life of the process** — silently; the UI just shows empty results.

Ownership replaces emptiness. Connections carry a monotonic `seq`, the server records which seqs
raised the cancel, and clears once none is still registered. Three details are load-bearing:

- **The seq is assigned under `m_connMutex` at accept**, never on the connection thread — or two
  accepts race to the same value.
- **A seq, not a pointer.** The allocator reuses addresses, so a pointer cannot answer *is that same
  connection still live*, and a new connection could inherit an old one's cancel.
- **Owners are recorded unconditionally, not only on the first raise.** If both lanes drop and only
  the first is recorded, clearing on its exit frees a cancel the second still needs. Only the
  `LOG_WARN` stays once-per-latch — noise, not correctness.

It cannot clear early, which is the axis `Tot.h` exists to protect: a connection is erased strictly
AFTER its handler returned, so an orphaned scan has unwound before its owner leaves the live set.
And the new rule **subsumes `firstConn` strictly** — an empty registry trivially has no live owner —
with a test for exactly that, because a fix handling only the new case would regress the path that
already worked.

The decision is a pure `Tot::PerCommandStillOwed`, unit-tested rather than inferred from Fern's
threading; Fern supplies both lists and holds no policy.

12 assertions across the two. Negative controls: dropping `LevelActor` from the UI's synthetic-hop
branch fails exactly F8's two new tests; reverting the predicate to the emptiness rule fails four of
F2's, the first being the exact two-lane scenario. 4016 passed / 0 failed.

⚠ Both owe a live check, and F8's is the point of the whole finding: `ok_via_level` firing **at
all**.

-----

## 2026-08-17 - A4: struct-sided TSet/TMap elements were covered by nothing (build 3219)

**Audit #5 A4 (MED) closed**, and it filed **A11** on the way out.

Value Search's deep pass skipped on `lf.depth < 2` alone — "depth 1 is already covered by the static
paths". That holds for **arrays only**: `collectStructArrayInner` has exactly one non-recursive call
site and it sits inside the `ArrayProperty` branch. A struct-sided `TSet<FStruct>` or
`TMap<K, FStruct>` element was therefore reached by **neither** index, and an everyday
`TMap<FName, FItemData>` inventory count was unfindable with Deep ON as well as OFF.

**The asymmetry is the evidence.** Three consumers share the same walker and only this one has the
wrong shape: the snapshot path tests `leafName.empty() && depth < 2` (whole-element-aware, i.e.
correct) and the group scan uses `depth < 1`.

**The rule moved to a pure header-inline predicate; the WIRING was closed by placement, not by a
test** — no target compiles `Aura.cpp`, so the fix's most likely failure was never the rule but a
leaf-init site left un-threaded. `ContainerKind` moves to `Aura.h` with **`Unknown` first**, and
`ContainerLeaf::kind` sits **immediately before `int depth`**, so a site that forgets it binds an
`int` to a scoped enum and fails to compile. Verified as a control: dropping it from one of the four
sites is `error C2440`.

⚠ **That inverts the advice the fix was drafted from, and the inversion is the whole safety
argument.** The draft warned against putting a new enumerator first, on the grounds that
`ContainerCacheEntry` is aggregate-initialised positionally — but those sites name their enumerators
explicitly, so order is unobservable there. The real hazard runs the other way: with `Array` at 0, a
missed wiring site value-initialises to *"statically covered"* and **silently reproduces A4**, with
no compile error and no failing test. Zero must mean "nobody said", and nobody-said is not covered.
(§2.2's *make a wired-through field required*, applied to an enum rather than a bool.)

**Two prerequisites shipped with it**, because without them the fix is wrong in a new way:

- `ScanClassInfo.fieldCapHit` now gates the predicate. It answers *does the static index reach this
  leaf*, which is meaningless if that index was truncated at `kMaxScanFieldsPerClass` — such a class
  now falls through to emitting rather than trusting coverage it may not have.
- `ensureDeepDescriptor` takes the leaf's real `boolMask` instead of hardcoding `0xFF`. A packed bool
  shares its byte with up to 7 siblings, so a whole-byte mask compares all of them — newly reachable
  now that struct-sided Set/Map leaves emit.

13 assertions. Negative control: restoring `depth < 2` fails exactly the two defect rows plus the
`Unknown` guard. 4013 passed / 0 failed.

⚠ **Known limit, filed as A11 rather than glossed over.** Deep candidates refine by stored ABSOLUTE
address, and the newly-reachable rows are overwhelmingly TSet/TMap sparse-container elements — which
UE rehashes and compacts on Add/Remove. So a refine after the user picks up an item can drop the very
candidate this fix made findable. That is pre-existing behaviour, but this multiplies its incidence
on precisely the inventory workflow the finding cites. **A4 makes the count findable; A11 is what
makes it stay findable.** Do not report the workflow as closed on A4 alone.

-----

## 2026-08-17 - U5: the class-walk cache is bounded, and "eviction is illegal" was half wrong (build 3218)

**Audit #5 U5 (MED) closed — Tier 0 + Tier 1.** The headline is not the bound; it is that the reason
this was refused four times does not hold for half the memory.

The register said, repeatedly, that eviction is **illegal** until `WalkClassEx` stops returning
`const ClassInfo&`, and that the real item is a return-type refactor across 25 call sites. That is
true of the **enriched** memo. It is **false of the plain `s_walkClassCache`**: `WalkClass` returns
`ClassInfo` **by value**, and all three touch points copy under the mutex — so eviction there cannot
invalidate anything a caller holds. **Bounding it was legal the whole time, with zero call-site
change.** Half the reclaimable bytes were sitting behind a blanket claim that covered both maps.

The supporting numbers were reproduced from the maintainer's own `walk-0.log`, not asserted: 10,046
distinct classes, 10,198 walks, 0 refusals.

**Tier 0 first, because it is what makes Tier 1 measurable.** `get_diagnostics` gains `class_cache`
(entries / max_entries / fields / approx_bytes) plus a pure header-inline
`Ubel::EstimateClassInfoBytes`. That replaces one game's log extrapolation with a per-game number.

**Tier 1** is an LRU on the plain cache with touch-on-hit at **both** lookups. The super-chain site is
the load-bearing half and is easy to miss: a base class is reused by every subclass, so the chain
walk **is** a use — without touching there, `Object`/`Actor` age out despite being the hottest
entries in the map.

Cap **2048, not the 512 first proposed.** 512 came from "~50× the deepest super chain", which is the
wrong denominator — the working set is the classes touched in one scan pass. The test asserts the cap
is neither 512 nor ≥ the reference working set, because that constant is the one most likely to be
re-tuned later off the wrong figure. Tune it with the Tier-0 counters, **never** with the 0.038 ms
average walk time: that average is dominated by cache HITS and says nothing about an eviction.

⛔ **Four things deliberately not done**, recorded so none reads as an oversight:

1. **Tier 2** (shrinking `FieldInfo`) — 197-419 mechanical edits across three files with no coverage,
   and its proposed handle cannot have both an implicit `const std::string&` conversion and a sole
   `operator==`. Its own finding if ever pursued.
2. **Publishing each super the chain loop walks.** The clean implementation is recursion through
   `WalkClass`, i.e. restructuring the B10 hot path; publishing a *partial* `ClassInfo` instead would
   be returned verbatim by a later lookup, which is a correctness regression rather than a cheaper
   version. And the cliff it defends against is already unreachable — touch-on-hit at the super site
   refreshes bases on every subclass walk, so they cannot go LRU-cold.
3. **"Stop publishing when the call came from `WalkClassEx`"** — same bytes, but it kills the B10
   super-chain reuse.
4. **An append-only arena as a BOUNDING measure** — it bounds nothing by construction. Fine as
   compaction, which is a different question.

Two follow-ups filed rather than folded in: **U18** (four `// cached — just hash lookup` comments
that are wrong — a hit is a hash lookup *plus a deep copy of the flattened chain*, and three of them
sit inside per-value loops) and **A10**, already open (`s_classContainerCache` is the same shape and
still blocked by a by-reference return, so whatever is decided here governs it).

4013 passed / 0 failed. Negative controls: dropping the estimator's fields term fails exactly the two
field-scaling assertions; restoring cap 512 fails exactly the two cap assertions.

-----

## 2026-08-17 - AD3: a zero-hit hint erased the pattern before the pass that could find it (build 3215)

**Audit #5 AD3 closed (MED→LOW)**, and it filed **AA38** on the way out.

The checked question first: **is this the same defect G10 already fixed?** No. G10 stopped the hint
erasing a pattern that MATCHED but failed validation. What remained is the `hintHits.empty()` arm,
and it is wrong for a different reason: `Macht::AOBScanAll(pattern)` passes no `moduleBase`, so it
defaults to `GetModuleBase(nullptr)` and answers **"no match in the MAIN module"** — while **Pass 2
exists precisely for patterns with zero main-module hits** and re-scans them across every loaded
module.

So erasing on an empty result removed the pattern **before the only pass that could still find it**.
A target living in another module was unresolvable on any run that had a hint, and resolvable on the
cold run that had none. Same G10 invariant — *the hint path must never be weaker than the scan that
produced the hint* — one axis further out.

**A second defect surfaced while fixing it.** The hint phase pushed its `PatternScanResult` on BOTH
miss paths while leaving the pattern in `sorted` for the batch passes to re-scan, so the same pattern
landed in `report.results` twice: "N patterns tried" was inflated and the per-pattern dump listed it
twice. That already affected the matched-but-unvalidated case, and deleting the erase would have
added the zero-hit case. The hint phase now records only when it WINS — the batch pass reports
otherwise, because its verdict is the final one.

⛔ **No test, deliberately.** The rule is a deletion, not a predicate, and no target compiles
`Genau.cpp`. A `Sig::ShouldDropHintPattern` header predicate was considered and rejected as oversold:
it is constant-false at all three reachable call sites, so it would pin a hypothetical branch while
the actual call-site edit stayed unverified — a green test there would be worse than no test. The
diff is kept to the minimum inspection can carry.

**AA38 filed rather than hidden.** Both corpus instances that exercise this had `GObjects … NONE
validated` and recovered a **foreign-module GWorld** that `ValidateGWorldBasic` accepted only because
`world == 0` passes outright — on `python.exe`, a process with no UE world at all. AD3 makes those
suspect resolves appear on *every* launch instead of every other launch; it did not create them and
does not make them worse per-run. The honest item is the one the review asked for: *a foreign-module
GWorld win on a run where GObjects never resolved should be refused, not published.*

Live check (log-only, no interaction): grep by format string — `Hint MISS: '`,
`(multi-module), validated`, `patterns tried,`. A hint-miss run should now show the pattern reaching
Pass 2 with real multi-module hits rather than `hits=0`.

4013 passed / 0 failed.

-----

## 2026-08-17 - Y16: a 1-byte enum param was written as 4 bytes over the next one (build 3214)

**Audit #5 Y16 (MED) closed** — the maintainer lifted the hold that had kept it recorded-not-fixed.
The register's own scope note was right and the row wrong: **three sites plus a cosmetic straggler**.

UE sizes an enum by its UNDERLYING type. The common `TEnumAsByte` / `enum class : uint8` is 1 byte,
so mapping every `EnumProperty` to `int32` writes 4 bytes and clobbers the next parameter in
`params_data`.

| # | site | was | now |
|---|---|---|---|
| 1 | `InvokeScriptGenerator.GetMailboxWriteStatement` — interactive CE form, WRITE | shared the `IntProperty` arm → `writeInteger` | falls to the existing `_ => size switch` |
| 2 | `BakedScriptGenerator.MapToHelperType` via `MapInputType` — Copy AA Script (Baked), WRITE | flat `'int32'` token | size-aware token |
| 3 | `CeInvokeReturn` — RETURN, READ | 1-byte enum reported from a 4-byte read | reads its real width |

Sites 1–2 corrupt memory; site 3 only misreports. **Fixing site 1 alone would have left the three
ways of invoking a UFunction from one dialog input still disagreeing**, which is the row's own
complaint.

**This is the family's recurring shape and it is worth naming: the finding is the DROPPED FIELD, not
the guess.** The size was in scope at all three sites the entire time — site 1's `_ => size switch`
fallback already mapped 1/2/8 correctly and `EnumProperty` short-circuited past it; site 2 held
`v.Size` and used it only for the `fstruct` arm; site 3's `size` was already a parameter. The same
was true of W6 (`CeWidthForSize` existing but unused) and Y15 (`MapToHelperType`'s "out of v1 scope"
comment). Here `BakedParamValue.Size`'s own doc comment stated the defective assumption outright.

Shape copied from **Y15's precedent**: a `(string, int)` overload plus a sizeless one behaving as
size 0, with **only `EnumProperty` consulting the size**. That restriction has its own test, and it
is the one that matters — letting `size` override types whose width is fixed by their NAME would turn
a mis-reported size into a wrong write for types that are currently correct, i.e. the fix would
become a bigger version of the bug.

**No Lua change was needed**: `writeBakedParams` already accepts the `byte` / `int16` / `int64`
tokens, so no user has to re-embed the helper. The cosmetic straggler is folded in —
`ShortTypeNameForComment` hardcoded `"enum(int32)"`, so the generated script's own trailing comment
would have gone on asserting the old width after the write was corrected.

42 tests. Site 1 is asserted on the **emitted Lua** rather than the mapping, because that is what
reaches the game — including that the neighbouring int param keeps its own width. Negative controls:
putting `EnumProperty` back in the int arm fails exactly the three emitted-width cases; flattening
the enum arm fails the mapping, `MapInputType` and cross-path-agreement cases. 4013 passed / 0.

**The enum-width family is now 4 findings across 7 sites in 4 subsystems, all closed** — W6, Y2,
Y15, Y16.

-----

## 2026-08-17 - AA10: seven of eleven mailbox emitters had no idle guard at all (build 3206)

**Audit #5 AA10 closed (MED→LOW).** The mailbox has two concurrency guards and they are mutually
blind: `_ue5_invoke_busy` is the Lua helpers' own flag, and the GENERATED scripts write the mailbox
directly and never touch it.

**The re-derivation's own reachability argument was too pessimistic**, and correcting it is what made
this shippable. It concluded the bug needs `processMessagesPaintOnly` to dispatch `WM_TIMER` —
unknowable offline. But a re-entrancy-free branch exists that needs no pump at all: every wait arm
bails on timeout while `cmd` is still non-zero and the DLL still owns the mailbox, so the next rescan
tick (≤5 s away) or the next user-ticked guardless generator writes straight over a live command.
That is AA19's shape, which this repo already rated MED and fixed.

And the scale is worse than the headline: **7 of the 11** mailbox emitters had no idle wait at all —
8 sites, counting Movement's two. The four that did have one are all helper-shaped
(`return nil, reason`), so there was no toggle-shaped precedent to copy.

**Two corrections to the prescribed fix, both load-bearing:**

- **Placement.** "Before each status clear" is still *after* the operand writes — and those land in
  the same mailbox, corrupting the in-flight command just as surely. It belongs above the FIRST
  write, exactly where `AppendContractCheck` already sits for the identical reason.
- **Shape.** A one-line `showMessage(...); if memrec then ... end; return` reads fine and **fails
  `CeMailboxBailoutTests`**, which scans line-by-line for an untick FOLLOWING each message. The test
  was right to reject it — the one-liner is also unreadable in CE's editor. Rather than hand-roll
  eight copies (build 2743's three defects reached all seven copies of the mailbox wait exactly that
  way), this adds the shared `CeLuaHygiene.AppendIdleWaitOrBail`.

Lua side: `fetchInstancePage` now refuses a non-zero `cmd`, **placed before `_ue5_invoke_busy =
true`**. After it, a return latches the flag for the session and abandons the freeze — a worse
failure than the one being prevented.

⛔ **Not taken:** emitting an `_ue5_invoke_busy` acquire/release into the generated scripts. Its only
added coverage is the nested-re-entry window whose reachability is unsettleable offline, against a
release that must fire on five wait arms plus every generator's own returns, where one miss latches
a session-global.

**Verified by parsing, not by asserting about text.** All **16** emitted `{$lua}` chunks across the
8 guarded sites were dumped and loaded by a real Lua 5.4 — 0 rejected. The guard adds two `if/end`
pairs and a `break` inside a while-loop to eight blocks at once, which is exactly the class of change
text assertions cannot vet; the parser check was itself negative-controlled with a bare `break`
outside a loop.

Negative controls: disabling the refusal lets the rescan overwrite the live command (cmd 8→6,
result -7→0); moving the guard after the flag set keeps **both** value assertions green while
latching the flag and abandoning the freeze — which is precisely why the third assertion exists.

3971 C# / 0 failed; freeze rig 55 checks / 0; dissect rig 50 / 0.

-----

## 2026-08-17 - ST1: our own "direct" calls re-entered our own detour (build 3205)

**Audit #5 ST1 (MED) closed**, and the AA10 / AA11 status conflict settled in the same pass.

**The diagnosis was right and both the location and the repair were wrong.** The finding reads as
"the drain lacks a thread check". One level up: three of our own call sites resolve
`UObject::ProcessEvent` out of an instance's vtable and call **the address MinHook patched**. So a
self-issued "direct" call lands in `HookedProcessEvent` — on a pipe lane, or on the Mimic polling
thread — and the drain there is gated only on *is the queue non-empty*.

What the drain then runs is the whole point. Requests are queued **because** a caller judged them
unsafe off the game thread: Mimic sends Native+Static helpers direct and routes FUNC_Net,
FUNC_Event, BlueprintEvent and stateful actor mutation to the queue. The concrete failure is
therefore UE object and world mutation on a non-game thread — exactly the hazard this module exists
to prevent.

**And the window is not a microsecond race.** A timed-out invoke *stays queued*, deliberately, with
its own owned parameter copy, expecting a later drain. After one timeout the window is open
indefinitely, and the next CE static-native invoke or UI self-test executes that abandoned stateful
UFunction on the wrong thread.

**Fixed by calling the trampoline** — the same thing `Grausam` already does for its own hooks — so
our calls never enter the detour at all.

⛔ **Explicitly not by checking thread identity, and this is the part to remember.** Nothing in this
tree resolves the game-thread id: the finding's five grep hits are three `printf` arguments and two
AOB comment lines, and `GIsGameThreadIdInitialized` is only *named* in a derivation note. Any gate
would be guessing — and **a gate that guesses wrong never drains**, which times out every
game-thread invoke. That is strictly worse than the defect it would be fixing.

Two pure predicates in `Stark.h`, each negative-controlled on its own:

- **`ShouldUseTrampoline`'s `resolvedPeAddr == hookedAddr` term is load-bearing.** A class that
  genuinely OVERRIDES ProcessEvent has a different slot, that slot was never patched, and calling the
  trampoline for it would silently run the BASE implementation instead of the override. When they
  differ we fail open to the caller's address, which is the correct one.
- **`ShouldDrainQueue` is CALLED by `HookedProcessEventBody`, not mirrored by it.** A mirrored copy
  proves only that the copy is right; no target compiles `Stark.cpp`, so the predicate under test has
  to be the shipped one.

⚠ **The `thread_local` marker is set ONLY inside the new `CallOriginalSEH`, never inside
`CallProcessEventSEH`.** That distinction is the correctness of the whole guard: the latter is what
the *legitimate* game-thread drain calls, and a UFunction executing there routinely dispatches
further ProcessEvent calls through the vtable. Marking it there would make the game's own nested
dispatches look like ours and suppress draining on the very thread that is supposed to drain.

**Four scope claims were cut back on review, all of them in the direction of over-claiming:** the
`UE5_CallProcessEventEx` fallback is **dead as a second vector** (`Stark::RemoveHook` has zero
callers, so `IsHookActive()==false` there means the address was never patched); the
validator-self-satisfaction consequence is a **race, not a guaranteed path**; the false-liveness one
is **bounded by the 500 ms** stall threshold and only fires on rare user-initiated one-shots; and the
filed *"reuse Linie's fix two lines above"* is a **category error** — it is eight lines away, in
another translation unit, and `Linie.cpp:46` is a timestamp-monotonicity tolerance that tests no
thread identity at all.

`Frieren.cpp`'s *"never goes through GameThreadDispatch"* comment was provably false and is true
again only because of this change.

10 assertions. Negative controls: dropping the address-match term fails exactly the
overridden-ProcessEvent case; reverting the drain gate to depth-only fails exactly the two re-entry
cases. 3971 passed / 0 failed. ⚠ Live check owed — the drain is only observable with a game attached.

**AA10 / AA11 re-tiered MED → LOW.** Their rows said MED while §3b's clump table said they were
downgraded when AA9/AA12/AA13 shipped; the rows are the authority, so the two disagreed and the
gate-derived headline was inflated by two. Re-derivation confirmed LOW for both and they are now
consistent. They remain **open** — this is a re-tier, not a close.

-----

## 2026-08-17 - AA8: the dissect script asserted an Outer offset the DLL already detects (build 3204)

**Audit #5 AA8 closed — re-tiered MED→LOW** (population today is **zero of 30+ tested games**, so it
is a correctness gap with no known live victim), and it filed **AA37** on the way out.

`ue5_dissect.lua`'s UObject header carried a flat `addIfMissing(0x20, "Outer")`. On a
`WITH_CASE_PRESERVING_NAME` build FName is 12 bytes instead of 8, so `OuterPrivate` pads out to
`+0x28` — which the DLL detects (`DynOff::UOBJECT_OUTER`, Genau) and the script did not. The emitted
structure then labels the FName's DisplayIndex/Number pair as an 8-byte "Outer" pointer and omits the
real field. Correctly narrowed on re-derivation: this is the **only** header row that can be wrong;
the other five match `Grimoire`'s constants.

**Fixed by asking the DLL, not by mirroring it.** A second copy of the detection is the drift this
repo keeps paying for, so `detectOuterOffset` calls `UE5_GetObjectOuter` for the object being
dissected and finds which slot holds that value — two reads and one export call.

Three judgement calls, each with its own test:

- **A null Outer proves nothing.** A root package legitimately has none, so it falls back rather
  than treat 0 as a reading — otherwise every such object "detects" whichever slot happens to hold 0.
- **An ambiguous read prefers 0x20.** A tie is not evidence for the rarer layout.
- **Deliberately NOT memoised.** CE keeps one Lua state for the whole session and never rebuilds it
  (CE-Bugs-Minesweeper §5), so a cached `0x28` would follow the user onto the next,
  non-case-preserving game and corrupt every structure built there. The "just cache it" optimisation
  has its own regression test.

**One filed rationale was over-claimed and dropped:** a `pcall` around the probe, for version skew.
`git log -S"UE5_GetObjectOuter"` finds only the module-rename commit, so the export predates the
naming scheme and no DLL a user could plausibly pair with this script lacks it — and swallowing there
would re-introduce exactly the silent failure the AA5/AA6 fix removed.

**The rig earned its keep twice**, which is the reusable part. `scripts/tests/dissect_test.lua` runs
the real script over stubbed CE globals: it failed on the first run because the change had quietly
introduced a **new DLL dependency**, and its 40 pre-existing checks then proved the rest of the
header path was untouched. Six new cases, 50 checks. Negative control: restoring the hardcoded
`0x20` fails 3 of them.

**AA37 filed rather than bundled.** AA8's re-derivation surfaced a second, independent defect:
`addUObjectHeader` runs **unconditionally**, without asking whether the walked `UStruct` is even
UObject-derived — so `createFromPath("/Script/CoreUObject.Vector")` staples six meaningless header
rows over real member offsets, and the `covered` set does not stop it because that set is built from
element START offsets only. The register is CI-gated per row, so folding two defects into one commit
would have left neither honestly markable.

3971 passed / 0 failed; the C# text assertions over this script (`CeExecuteCodeExArityTests`) still
pass.

-----

## 2026-08-17 - AD4: the God Mode badge collapsed three states into one (build 3203)

**Audit #5 AD4 closed — re-tiered MED→LOW.** Nothing mis-writes: the hold is correct and the
re-assert worker keeps it. What was wrong is a display that *could not* be accurate, plus ~25 lines
of shipped-but-unused DLL surface.

`get_protect_state` has been in the DLL since build 1251 carrying `want` / `live` / `resolvable`
separately, with **zero clients**. The UI read the collapsed `get_god_mode` tri-state, so three
genuinely different situations all rendered as a flat OFF or Unknown:

| real state | old badge | why that is wrong |
|---|---|---|
| `want=1`, unresolvable | "Unknown" | armed and waiting for a pawn — shown as a broken connection |
| `want=0`, `live=1` | "ON" | immune, but not by us — credits the tool for a state it isn't holding |
| `want=1`, `live=0`, resolvable | "OFF" | engaged, game won the drift race — shown as never-enabled |

**Wired up C#-only. Deleting the dead command was the alternative and is strictly MORE expensive:**
it trips three CI gates — `check_mailbox_contract.py` hashes `ProtectOp`, so removal means a contract
bump plus a golden-block justification, and `check_derived_counts.py` covers `pipe_commands` and
`c_abi_exports`, cascading into CLAUDE.md ×2, architecture.md, naming-convention.md and dll-spec.md.
Wiring moves none of them.

**Three traps, each of which would have produced a fix that looked finished:**

1. **The wire name for `Live` is `godmode`, not `live`.** The wrong key falls through to the `-1`
   default, which *means* "no pawn" — so the badge would sit at Unknown forever, looking exactly
   like a game with nothing spawned. Its negative control is the rename itself.
2. **The badge map needs THREE new cells, not two.** The obvious version leaves
   `want=1 && live=0 && resolvable` falling through to plain "OFF" — mirroring the very conflation
   the fix exists to remove, in the cell most likely to occur in practice. Omitting it would have
   reproduced the defect *inside its own repair*.
3. **The toggle path had to move too.** `ForceGodModeAsync` still routed through the int map, so a
   force-ON with no pawn yet reported "Unknown" — the reported symptom surviving on the commoner
   path. It passes `want` directly; it already knows it, so there is no extra round trip.

Connect-time read added, since `want` survives a UI reconnect and nothing queried it (AutoTick polls
pose + markers only). Deliberately **not** `RefreshGodModeAsync`: that sets `IsBusy`, which flickers
every `CanOperate`-bound button, and writes `StatusText`, which would overwrite the "Connected"
written moments earlier. Both properties are pinned by their own tests.

**Left open on purpose, and this is the important boundary:** making `Solitar::GetState.live` honest
on a pawn whose scan matched no canonical `bCanBeDamaged` — it falls back to the *desired* value
while `GetGodMode` returns `PR_ERR_REFLECT`. That needs `Solitar.cpp`, which no test target
compiles, so it is live-only; folding it in would have made these negative controls ambiguous.
`Solitar::IsGodModeWanted`'s zero callers are also left alone — its header comment is the only
written record of the reconnect-restore rationale `want` exists to serve.

One filed clause was overstated: "the docs assert the opposite contract in two places" is **one** —
`godmode-spec.md` §7 is plan text superseded by that file's own status banner, which names this
deviation explicitly. The repo's supersession convention held.

11 tests. Negative controls: renaming the wire key reddens the parse test; dropping the contested
cell reddens exactly that badge test. 3971 passed / 0 failed.

-----

## 2026-08-17 - AF5 + Z2: a ComboBox pick inside a tab re-ran that tab's activation (builds 3200, 3201)

**Audit #5 AF5 (MED) and Z2 (MED→LOW) closed.**

**AF5 is two independent defects**, and only together do they produce the reported symptom.

*The routing.* Avalonia's `SelectionChanged` **bubbles**. A ComboBox or ListBox inside the selected
tab raises it, it travels to the main TabControl, and `MainTabs_SelectionChanged` ran with
`sender == TabControl` and `SelectedItem.Tag` still naming the tab the user was already on — so an
ordinary in-tab pick re-ran the whole per-tab activation routine, cancelling in-flight heavy queries
and rebuilding lists.

**The premise was executed, not assumed.** In a headless harness against the pinned Avalonia: the
TabControl *is* a visual ancestor of the tab's content
(`StackPanel→ContentPresenter→Panel→DockPanel→Border→TabControl`); **ComboBox and ListBox** re-fire the
handler this way while **DataGrid and AutoCompleteBox do not**; and a genuine tab switch arrives
with `e.Source == TabControl`. Two things follow that are easy to get wrong: the discriminator is
**`e.Source`, not `sender`** (sender is the TabControl in every case — which is exactly why the old
code could not tell them apart), and **`e.Handled` is not an option**, because the TabControl is the
end of the route and setting it suppresses nothing.

Fixed with a pure `Helpers/TabActivation.ShouldRunActivation(e.Source, tabs)` — the
`LiveWalkerNavShortcuts` shape, `object?` parameters, testable without a toolkit.

*The reset.* `ClassPivotViewModel.RefreshAsync` rebuilt `Snapshots` and then **hard-set** the
selection to `Snapshots[0]` and re-ticked the two newest `DiscoverPicks`. That is what turned a
wasteful re-entry into a destructive one: the user picked a snapshot, the pick bubbled, activation
re-ran, refresh reset the pick. It now captures both before the rebuild and restores them **by Id** —
`UiCollection.Reset` repopulates with NEW `SnapshotMeta` instances, so a reference or index
comparison would pass for the wrong reason. The two-newest default still applies on a first build,
so it stays a starting point rather than something that reasserts itself, and a snapshot deleted
between refreshes falls through to the newest.

**The third proposed part was deliberately not taken.** Coalescing concurrent refreshes behind a
shared task targets an overlap that the source guard already removes, and the obvious implementation
is unsafe: hoisting `_refreshing` above the await also swallows `OnSelectedSnapshotChanged`'s class
load, which keys off the same flag. Separate concern, separate change.

Negative controls: degrading the guard to always-true fails 5 of 6 `TabActivation` cases including
the bubbled-child one; reverting the hard reset fails exactly the two preservation tests and nothing
else. AOT publish verified (54.4 MB).

**Z2 lands as CONSISTENCY, and the write-up matters more than the diff.** Two selection-bound lists
were rebuilt without first detaching the selection, where three sibling panels carry the detach
verbatim. The *severity story* around it is wrong in **both** directions, which is the part worth
keeping: the filed row asks to rank it by audit #3's L18/L19 downgrade, and that precedent **does
not transfer** — this site is `SelectionMode="Extended"` with `grid.SelectedItems` consumed by two
code-behind handlers, while both downgraded sites are single-select. But that difference is
**untouched by the one-liner**, which detaches `SelectedResult`, not `SelectedItems`. So the fix is
right and the elevated-risk story would have been wrong. No crash has ever been observed at any of
the four panels; the reason to land it is that four panels asserting one invariant three different
ways is how the next reader learns the wrong rule.

3960 passed / 0 failed.

-----

## 2026-08-17 - A1: at stride 20 the serial read was ClusterRootIndex (build 3194)

**Audit #5 A1 (MED) closed.** `Aura::GetSerialNumber` computed its offset inline as
`s_itemSize >= 24 ? 0x10 : 0x0C` — a two-way split covering only strides 16 and 24. The reachable
set is **{16, 20, 24, 32}**: the auto-probe tries `{16, 24, 32, 20}` and `UE5_InitWithExtendedLayout`
forces any of `{0x14, 0x18, 0x10, 0x20}`.

Stride 20 is Avowed's packed `FUObjectItem`, whose layout is
`{Object@+0x00, Flags@+0x08, ClusterRoot@+0x0C, Serial@+0x10}` — from the Ghidra decompilation of
`AllocateUObjectIndex` recorded in `docs/avowed-gobjects-fix.md`. The old expression returned
`0x0C` for it, which is **ClusterRootIndex**. `Ubel::ResolveWeakObjectPtr` then runs a bare
`if (actualSerial != serialNumber) return 0;` — no fallback, no retry, no log — so every weak
reference resolved to null.

**The question this had to settle before anything moved: is A1 the thrice-refused `SerialNumber`
witness?** It is not, and the distinction is worth keeping. `working-lessons.md` §4.3 and the
cluster-③ STOP-BLOCK refuse a **passive observer STORING `(index, serial)`** as a recycle witness,
because UE zeroes the serial in `FreeUObjectIndex` and allocates it lazily, so most objects carry 0
for life and a stale `(i, 0)` matches a recycled `(i, 0)`. A1 is UE's own
`FWeakObjectPtr::SerialNumbersMatch` input, read at the right **address**. Different question — and
it is the kind of surface similarity that would have got the fix refused on sight.

One filed over-claim corrected: *"silently nulls a whole feature family"* is total only for
`WeakObjectProperty` and the delegate family (single, multicast, sparse). The Soft and Lazy handlers
display the asset PATH first and lose only the resolved live object.

**How it became testable.** The rule moved to a pure header-inline
`Lineal::SerialOffsetForLayout`. No target compiles `Aura.cpp`, but `dll_helpers_test.cpp` already
includes `Lineal.h` — the MA2 / build-3135 pattern again: *ask what the test file already includes
before accepting that something cannot be pinned.* `Aura.h`'s declaration comment said "16-byte or
24-byte" and described the code exactly; comment and bug agreed, which is why neither looked wrong.

**Two things deliberately not done**, on the skeptic's call: deleting `Aura.h`'s 16-byte
`struct FUObjectItem` (nothing reads `.SerialNumber` off it, but removing an exported accessor is
scope creep on a one-line correctness fix — its comment was corrected instead), and pinning the
Packed57 serial offset, which is still *** UNVERIFIED *** and runtime-calibratable. It passes
through untouched, and there is a test asserting exactly that.

9 assertions across all four classic strides, both UE5.7+ modes, and the calibrated pass-through.
Negative control: restoring the old expression fails exactly one row — `classic 20 -> 0x10
(Avowed)`. 3950 passed / 0 failed.

⚠ Live check owed — Avowed, or any forced-stride-20 title, should now resolve weak references and
delegate targets instead of showing them uniformly null.

-----

## 2026-08-17 - AD5: an English FText over zeroed heap came back as CJK mojibake (build 3193)

**Audit #5 AD5 (MED) closed.** `DecodeFStringBuffer`'s own "KNOWN RESIDUAL" was real, reachable, and
**fires 100% of the time** in the case it describes — not, as the header argued, a near-impossible
coincidence.

**The finding names the wrong type.** `FUtf8String` goes through `Ubel::ReadFUtf8String`, which is
count-based and never calls this function. The live path is `Ubel::ReadFTextString`, which
blind-scans `FTextData+0x08..0x90` and offers every 16-byte candidate to `TryDecodeFStringAt`. So the
case is an **FText display string that a cooked build stored as UTF-8, holding ASCII text** — i.e.
ordinary English UI, not an exotic input.

Rule 2 needs a multi-byte sequence and pure ASCII has none, so step 3 falls through to the UTF-16
hypothesis. Production reads `Num*2` bytes, so half the window is adjacent heap; each ASCII byte
PAIR becomes one BMP unit and "Continue" returns as U+6F43 U+746E U+6E69 U+6575.

**The header's defence was false on both halves, and that sentence is why this stayed open.** It
claimed *"Both conditions must hold — LooksLikeDecodedText rejects a binary tail"*. The two
conditions are the SAME heap state: zero-filled slack (the ordinary allocator state) puts the
terminator pair at `[2n-2]` and supplies the textual tail at once. And the mojibake contains **zero**
replacement characters, so `LooksLikeDecodedText` accepts it — it rejects binary, and a run of valid
CJK code points is not binary. That block now says so.

**Fixed structurally, not heuristically.** An `FString` is a `TArray<TCHAR>` whose `Num` includes the
terminator, so exactly one null unit exists and it is the last; an interior null unit proves the
second half of the window is not part of the string.

**The re-derivation's own fix was too broad, and the skeptic caught it by compiling both.** Built
against the real header with MSVC over 20 vectors: an unconditional interior-zero-unit rule also
changes buffers that today decode as a truncated prefix — nothing to do with this defect — and one
of the prescribed tests would have locked that behaviour in. What shipped is gated on `utf8Ok`, so
it can only break the tie where both hypotheses succeed and the wrong one wins, and it is a guarded
block rather than an early `return ""`, because the function's tail is what delivers the correct
UTF-8 answer once UTF-16 steps aside.

Six tests: three real cases (OK / Continue / Press Start over zeroed slack) and three controls —
genuine UTF-16 unaffected, rule 2 still winning ahead of the tie-break, and an interior-zero-unit
UTF-16 buffer that pins the gate stays OUT. Negative control: disabling the tie-break fails exactly
the three new vectors and none of the 11 pre-existing ones. 3950 passed / 0 failed.

-----

## 2026-08-17 - AD6: the test that covered nothing, and the comment that made it load-bearing (build 3192)

**Audit #5 AD6 closed — re-derived as LOW, not MED:** a coverage lie, not a defect in shipping code.
The diagnosis needed no premise corrected, which is unusual for this register.

`Test_Mimic_PollLatency_OneMillisecond` asserted a fact about the **host** — that
`timeBeginPeriod(1)` makes 100×`Sleep(1)` finish under 300 ms. True, occasionally useful, and
invariant to every line in `dll/src`: **it passed with `Mimic.cpp` deleted from the tree.** None of
the four regressions its own banner named could redden it; `kPollIntervalMs` is a file-static in
`Mimic.cpp`, so the test's literal `Sleep(1)` cannot track it.

Worse than an empty test, because the false claim lived in **two** places and the second was
load-bearing: `dll/CMakeLists.txt` cited *"so it covers the real mechanism instead of a linked
import"* as the **justification for not linking winmm** into the target. Both rewritten rather than
merely deleted — leaving that comment behind would turn a deletion into a new lie.

**What replaces it is coverage that cannot exist anywhere else.** `Mimic.cpp` is compiled by no test
target, but `Mimic.h` is pure data, and that data is a published cross-language contract: every
offset is baked as a literal into `Services/CeMailboxLayout.cs` (whose comment reads *"must match
Mimic.h MailboxData"*) and into `scripts/UE5CEDumper.CT`. Nothing enforced it on either side. A
silent shift does not fail a build — it makes every saved `.CT` write to the wrong address. Offsets
are spelled as literals rather than derived from the struct: deriving them from the declaration
under test would assert only that C++ agrees with itself.

**Measured, not assumed, against the existing guard.** `tools/check_mailbox_contract.py` *does* catch
the control edit, via its surface hash — so the claim "the tool is blind" would have been wrong. But
it can only demand a *decision*: it never computes an offset, so a developer who bumps the version
sails through with the offsets silently moved. The two guards are complementary.

Negative control: `className[256]` → `[255]` fails exactly 5 assertions — the `funcName`,
`errorMsg` and `paramsData` offsets, the `className` size, and the struct total. Before this commit
that edit built and passed clean. 3950 passed / 0 failed.

-----

## 2026-08-17 - AC1: one checkbox, two policies, and only one of them is reversible (build 3191)

**Audit #5 AC1 (MED) closed.** Proxy Deploy's "Force Overwrite" armed two policies through a single
`bool force`, and they have nothing in common:

- `ProxyDeployService.cs:924` — redeploy over **our** proxy even at the same version. Benign,
  reversible, and by far the commoner reason anyone ticks the box.
- `:916` — replace a file that is provably **not ours**: ReShade, Special K, Ultimate ASI Loader,
  or a wrapper the game itself shipped. Irreversible.

The user ticks for the first. `UiOptionsSettings.cs:217` persists it and `MainWindowViewModel.cs:2434`
restores it on every launch, so that consent silently became **standing, cross-session, cross-game
authority for the second** — applied per game inside `DeploySelectedAsync`'s loop, over a Select All
that can be an entire Steam library, with no confirmation anywhere on the path (the VM's only
confirm delegate serves orphan cleanup). `:955` is a bare `File.Copy(overwrite: true)`: no backup, no
rename, and **no Recycle Bin**, against this repo's own rule that destructive operations go to the
bin. The evidence went with it — refresh computes `Other proxy: {ProductName}` at `:854-865`, and a
successful deploy returns `SetErrorMessage` default-true which blanks exactly that row, while `:956`
logged only the destination. So after the fact, nothing on screen or on disk said what had been
destroyed.

**The asymmetry was the tell, and it is one line.** `UpdateAllAsync` also passes `force: true`, but
is pre-gated at `ProxyDeployViewModel.cs:1355` on `IsOurProxyDll` and therefore never reaches a
foreign file. `DeploySelectedAsync` simply had no equivalent gate.

**Fixed by splitting the flag at the service boundary**, into a
`DeployOptions(ForceSameVersion, ForeignConsent)` record struct. Named members rather than two
adjacent positional bools on purpose: `DeployAsync(src, game, type, true, false, ct)` compiles, reads
plausibly, and destroys files if the pair is transposed. The persisted checkbox now feeds
`ForceSameVersion` **only**; a new **deliberately non-persisted** "Replace other tools' DLLs"
checkbox feeds `ForeignConsent` and resets to off every launch. The capability the tooltip has always
advertised is kept — this re-scopes the authority, it does not remove the feature — and the tooltip
now states which half does what and that it cannot be undone. The identity of a foreign DLL is
logged *before* it is replaced, because nothing else survives the operation.

Policy extracted to a pure `PlanDeploy`, matching the `PlanUndeploy` idiom already in this file:
ownership is decided by `FileVersionInfo.ProductName`, so fabricating a PE that carries a version
resource would test the fixture rather than the policy.

**Blast radius the finding did not name:** `Core/IProxyDeployService.cs` declares the signature and
**two test doubles** implement it. All three updated. `DeployOptions` lives in `Models`, not beside
the service, because `Core` names it and Core must not depend on Services.

15 policy tests. Negative control: re-conflating the two flags (`ForeignConsent || ForceSameVersion`)
fails exactly the three cases that describe the defect and nothing else. 3950 passed / 0 failed.

⚠ Live check owed — a real foreign `dxgi.dll` must refuse with Force Overwrite alone and proceed
with both boxes ticked.

-----

## 2026-08-17 - Z3: half the property scorer's vocabulary could never be fetched (build 3190)

**Audit #5 Z3 (MED) closed.** Interesting Properties (and Detect Player Stats) fetch on
`SeedQueries` and score on the six `*Keywords` tables. A property is only scored if it was fetched,
so a scored keyword no seed can reach is a **dead scoring arm** — the panel cannot show the field
however well it would rank. The finding named one case (`CurrentMP`). The real figure, measured
twice independently and agreeing exactly, is **58 of 123 keywords (47%)**, with Resources (20/29)
and Utility (10/15) hit far harder than the Stats example that was filed.

**Two filed premises were wrong**, both in the direction of over-claiming: `Duration` *is* seeded,
so 5 of the 6 build-678 Combat additions are affected rather than all six; and the table's "14/15
games" annotation covers only `Effect`/`Target` — `Ability`/`Modifier` are marked 7/15.

**The filed fix would have made its own flagship case worse, and this is the part worth keeping.**
"Cost of the fix is near zero — the DLL walks GObjects once" measures the wrong resource. CPU
genuinely is near-free (single walk, hoisted `ToLower`, and the per-query cap is tested *before* the
substring search, so a filled seed costs an integer compare). The scarce resource is the
**per-query 200-row envelope**, filled in GObjects walk order, first come first served. Measured
offline over **578,809 distinct identifiers from three shipped games** (DWORIGINS / ES2 /
CrimsonDesert — the `D:\UE_Analyze_data` PE corpus, no game running): a bare `"MP"` seed matches
**10,180** names of which **6%** carry `mp` as an actual token. It would spend its whole envelope on
`Component` / `Compression` / `Template`, never reach `CurrentMP`, and convert a bucket that returns
nothing today into 200 junk rows that also pollute the class histogram and permanently inflate the
"N of M keywords STOPPED at the 200-row cap" strip until that warning means nothing.

**Volume is not the test — precision is.** `Item` (3,050 matches) and `Velocity` (665) also blow
past the cap, but at 98% and 99% genuine the 200 rows they return are the right rows. Shipped 40
measured-safe seeds (58 → **8** unreachable) and recorded the 8 refusals — `MP`, `SP`, `XP`, `Exp`,
`Lv`, `Def`, `Load`, `Run` — in `DeliberatelyUnseededKeywords` **with their measurements**, so the
obvious-looking completion cannot be re-attempted blind. The honest repair for those is a
whole-token match on the DLL side (the client scorer already tokenises), not a broader substring.

**The drift was procedural, so the fix is structural.** The "Add a keyword" recipe at the top of
`PropertyScoringTable` lists three steps and never mentions `SeedQueries`, 460 lines below it in the
same file — that is how 47% of the vocabulary went dark without anyone doing anything wrong. Three
tests now assert the two vocabularies agree, that every exclusion is a real scored keyword, and that
no exclusion is already reachable.

**A test was deleted for being wrong in the dangerous direction.** The first version also asserted
"no seed is a substring of another seed", and it flagged `Timer`, `TimeDilation` and `Damaged` as
redundant with `Time`/`Damage`. That reads as obvious waste and is the opposite: each query owns its
OWN envelope, so a narrower seed is a **reservation** — `Time` caps routinely on any real game and
would fill with `Lifetime`/`TimeStamp`/`CastTime` long before reaching timer fields. Acting on the
check would have deleted three working paths to save work the cap already makes free. The reasoning
is now a do-not-re-add comment on the surviving exact-duplicate check. (§2.2 again: a guard you add
is code, and negative-controlling it is what exposed this — it failed on first run and the failure
was correct about two of its three claims and wrong about the conclusion.)

Negative controls: dropping the `Regen` seed re-orphans exactly `Stats.Regen` + `Stats.Regenerate`;
the reachability test also failed on first run with three real gaps (`Supplies`, `TickRate`,
`Cadence`) that the seed list had missed. 3935 passed / 0 failed.

-----

## 2026-08-17 - Z1: Interesting Functions looked finished 2 s before it was (build 3189)

**Audit #5 Z1 (MED) closed.** The Gameplay-Actions opt-in could latch ON while the grid stayed
scored with the pack OFF — permanently, with untick+retick the only recovery. The filed finding
named the guard that drops the toggle (`RescoreAsync`'s `if (IsLoading || ...) return;`) but not
the thing holding `IsLoading` true, and prescribed a fix that is wrong on a double-toggle.

**What re-derivation added.** `LoadAsync` ended with `await CheckAobMakerAsync()` — sitting directly
under a comment asserting it *"doesn't block the load"*. It is the **only awaited AOBMaker probe in
the UI**, against eight fire-and-forget siblings (one hit repo-wide), and the bridge pays a 2000 ms
pipe-connect timeout whenever Cheat Engine is not running, which is this panel's ordinary state. So
the swallow window is a **guaranteed 2 s, not a race**. Worse, nothing between `ApplyFilter()` and
the probe awaits, so the grid's first paint and the final status line land at the instant the window
opens: the panel looks *done* for the whole 2 s in which its main opt-in silently does nothing.

**Two halves, negative-controlled separately.** (a) `_ = CheckAobMakerAsync()` restores what the
comment already claimed and deletes 2 s of the window — but not the `Task.Run` remainder, so it is
not the repair. (b) The repair: record which mode `_allRows` was actually scored with, and reconcile
it against the live property in `LoadAsync`'s `finally`. Comparing *values* beats the filed
"pending-rescore latch" precisely on the case a boolean gets wrong — a user who toggles twice inside
the window is back on the mode already scored, and must trigger no re-score.

**The seam is the lesson, and it is §1.3 again.** The pre-existing test set the property and then
`await`ed `RescoreAsync()` *directly* — which re-scores unconditionally and therefore passes with
the defect fully present. The new tests await `PendingRescore`, the task the VM itself decided to
run, so they can distinguish "the VM reconciled" from "the test re-scored it". They also use bounded
`Task.WhenAny` waits rather than bare awaits, so re-introducing the `await` **fails** the suite
instead of hanging it — a hang is not a test result.

Negative controls: restoring the `await` alone fails only the busy-probe test; removing the
reconcile alone fails only the swallowed-toggle test. 3931 passed / 0 failed.

-----

## 2026-08-17 - the six-MED dossier is spent and archived; the register gate's --list now prints the open vetted tier

**No code change — docs routing + one tool.** The consolidation question ("should the scattered
audit docs merge into one bugs control table?") was evaluated and answered NO: the audit #5
register (§3c) already IS the single status owner, CI-gated, and every additional hand-maintained
copy of status is a copy that drifts. The evidence closed the argument by itself: the re-derivation
dossier — six rows, one day old — was already inconsistent three ways when checked (its title still
said UNFIXED, the V4 heading carried no marker despite shipping in build 3170, and the U3 heading
still called U17 open despite build 3171).

- **`docs/audit-2026-08-16-med-rederivation.md` → `docs/archive/`.** Its queue is fully consumed
  (PX1 3166 · AF3 3167 · A3 3168 · U3 3169 · V3+V4 3170 · U17 3171). Status markers and relative
  links were corrected at archive time; the derivation text is untouched — its archive README row
  flags this explicitly, since that folder's default convention is "nothing was edited, only moved".
- **`check_audit_register.py --list` now prints each open HIGH/MED row with the §2 segment it sits
  under** (HIGH first), derived from the same `ROW_RE` pass the gate already runs — zero new stored
  state, so the list can never disagree with the register it summarises. LOW/INFO stay unlisted on
  purpose: §3c warns they were never vetted to the audit's standard, and a flat listing would
  present 160+ unvetted leads as confirmed bugs.
- Routing text updated everywhere it pointed at the dossier: CLAUDE.md's docs index (dossier row
  removed, archive row extended, the audit-#5 row now says the fix queue is spent) and §3b's head
  block, whose stale "0 HIGH / 25 MED" is now the gate-derived 19. The "31 batches" figure next to
  it was re-derived (31 of 33 register headings carry an open marker) and stands as written.

First run of the new list surfaced a pre-existing row-vs-prose discrepancy to settle later: §3b's
clump table says AA10 + AA11 were downgraded to LOW on re-derivation, but their register rows still
carry MED, so both appear in the 19. The rows are the authority the count reads; if the downgrade
is real, the settle is re-tiering the rows (which moves the derived headline), not trusting the
prose.

Gates after the change: `check_audit_register` (including the new list), `check_live_verification`,
`check_derived_counts` — all green.

-----

## 2026-08-17 - U17: the correct struct decoder existed, and only one caller could reach it (build 3171)

**The layout half of U3, and the root cause was one level deeper than filed.** U17 was filed as "the
byte-blind decoder still reads 4-byte floats, so an LWC `FVector` decodes as six float halves". True —
but the reason was not that a correct decoder had to be written. **It already existed**, field-driven
and width-correct, and had done since build ~1200 — *inline inside `WalkInstance` and nowhere else*.
Every other surface (TMap keys and values, TSet elements, the Property Search preview column,
DataTable rows) fell through to `InterpretValue("StructProperty", …)` **while already holding the
`UScriptStruct*` a few lines above its own decode**. Two in-tree comments even name `InterpretValue`
as the wrong answer.

So the fix is extraction and routing, not new logic:

- **`Ubel::InterpretStructByLayout` / `InterpretStructAt`** — the decoder, lifted out of
  `WalkInstance` and made callable. `WalkInstance` now calls it too, so the two cannot drift; a second
  copy is precisely how the report and the reality end up computed by different code paths.
- **`PreferLayout`** routes the six container call sites: reflected layout when the struct address is
  in hand, `InterpretValue` only as the fallback for callers that genuinely have no layout.
- **The width handling is pure and moved to `Ubel.h`** — `PreviewScalarValue` reads each member at the
  property's OWN declared width, and `FormatPreviewNumber` was **moved** out of `Ubel.cpp` rather than
  copied. That matters twice: no target compiles `Ubel.cpp`, so this is the only way to pin it; and a
  copy would have re-created the drift this whole finding is about.

⚠ **The byte-blind path REMAINS, deliberately.** Callers with no `UScriptStruct*` still get
`f:[…]` from the U3 gate, and U3's test case 6 still asserts its six-float output. That assertion is
not a leftover — it pins the fallback's behaviour, which is unchanged and still cannot disambiguate
3 doubles from 6 floats. What changed is that the surfaces that *never needed* the fallback no longer
take it.

**Verification.** 246 + **1256** C++ assertions (+21) and 3928 C# tests green. Three negative controls,
each aimed at the width handling because that is the entire defect: `DoubleProperty` read as a float
→ **2** failures (the LWC case, both signs); `UInt32Property` sign-extended → **1** (the AB4 family —
`0xFFFFFFFF` must print 4294967295, not -1); the `size == 8` width check dropped → **1**. Restored →
green.

⚠ **Live check owed** and it is cheap: a struct-valued `TMap`/`TSet` element should now read
`{X=…, Y=…, Z=…}` instead of `f:[…]`, cross-checked against the `hexValue` on the same row.

-----

## 2026-08-17 - V3 + V4: Live Walker wrote post-await state onto whatever the user moved to (build 3170)

**Shipped together because they are one root cause, one method apart.** Both write VM state after a
long `await` without checking that the state is still the state they started from. Nothing gates the
panel meanwhile: `IsLoading` is bound only to a `ProgressBar`'s `IsVisible`, never to `IsEnabled`, and
`find_refs_to_uobject` rides the **bulk** pipe lane while `walk_instance` rides the **interactive**
one, so there is no ordering between them at all. The DLL-side reference scan has a 30-second
deadline — that is how wide the window gets.

**V4** — a drill-down appends to `Breadcrumbs` after its walk returns. Go Back meanwhile and the new
crumb is grafted onto a *different* parent, with a `FieldOffset` describing a spine that no longer
exists. Not confined to the panel: it ships into CE XML and CSX exports and is **persisted** into
`bookmarks.<hash>.json`, so it survives a restart.

**V3** — the reference scan refills `References` and composes `ReferencesHeader` from live VM state,
so A's referrers appear under "References to B". Not cosmetic: `Open` on such a row pre-arms the
scroll hint from the referring field and re-roots the walker, giving a real navigation into an object
that references something else entirely.

**The guard is crumb IDENTITY, never `Breadcrumbs.Count`.** Back-then-a-different-drill restores the
count while changing the parent. That is not a theoretical objection — swapping the shipped guard for
a count check was **measured to let 2 of the 7 new tests through**.

**Captured at GESTURE time**, not after the await, and threaded into `NavigateToAsync` — audit #5 AE2
already paid for this exact lesson ("the ticket is claimed at GESTURE time … claimed in the command it
would invert the fix"). All three post-await `Breadcrumbs.Add` sites are covered (`NavigateToAsync`,
the `NavigateToFieldAsync` struct branch, `NavigateToArrayContainerAsync`), each degrading with an
honest `StatusText` rather than a silent `return`.

⚠ **The residual risk was real, and it has its own test.** The expected-parent parameter is
**required but nullable**. Game Engine start, and the Go box / bookmark / cross-tab "Open in Live
Walker" handoff, all call `Breadcrumbs.Clear()` first and legitimately have **no** parent — a
`required` non-null parameter would have silently killed every one of those paths.
`V4_ReRootingNavigation_HasNoParentAndMustStillWork` exists to stop a future "tighten the guard"
change from doing it.

**The controls found a hole that review did not.** Reverting V3's guard to the *tempting* one-liner —
`result.QueryAddress != CurrentAddress`, free because the DLL echoes it and `DumpService` already
parses it — left **every V3 test green**. A container drill pushes a crumb and changes
`CurrentObjectName` while leaving `CurrentAddress` untouched, which is the exact "References to Items"
mislabel. `V3_ContainerDrillDuringTheScan_IsAlsoCaught` was written in response and now fails against
that variant. Two of the first four mutations were also useless — one did not compile, one was a
semantic no-op — which is worth recording: **a mutation that does not change behaviour proves
nothing, and reads exactly like a passing control.**

**Verification.** 246 + 1235 C++ and **3928** C# tests green (+7). Controls: the identity guard
downgraded to a count check → 2 failures; the `NavigateToAsync` guard neutralised → 2 failures; V3's
guard downgraded to address-only → 1 failure (the container-drill test). Restored → green.

**Not done, deliberately:** the shared-`IsLoading` flag (two commands, one flag, each clearing it in
its own `finally`) and the `IsEnabled="{Binding !IsLoading}"` belt on the Back/Forward/Parent buttons.
Both are named in the finding; neither is the corruption, and folding them in would have made the
negative controls ambiguous.

-----

## 2026-08-17 - U3: a struct preview that dropped leading members, silently (build 3169)

**`size > 8` is not evidence of a vtable.** `InterpretValue`'s StructProperty arm skipped the first 8
bytes whenever the struct was larger than 8, on the theory that structs begin with a vtable pointer,
then read the remainder as 4-byte floats. The result was a **silent drop of leading members**, which
is worse than garbage because it looks correct:

- **`FVector3f`** (12 B, 3 floats) printed **one** number — the *last* component. Live-confirmed:
  `Map_IntToVec3f` → `f:[6203.0000]`, while the raw hex on the same row held all three.
- **`FLinearColor`** (16 B, 4 floats) lost **R and G**; only B and A printed. Not in the filed finding.

**The filed rationale was wrong in the dangerous direction.** Its parenthetical — *"USTRUCTs generally
have no vtable"* — is true in aggregate and **false for the first struct the branch's own comment
names**. `FGameplayAttributeData` declares a virtual destructor, so GAS attributes really do carry a
vtable with `BaseValue`/`CurrentValue` at +8/+0xC. The 8-skip is *correct* there, and is why the
heuristic was written and why it survived. Acting on the finding as written — "remove the bogus skip"
— would have regressed every GAS attribute preview into `f:[<vtable low>, <vtable high>, 100, 75]`.

So the skip is now gated on **evidence** instead of on size: `Ubel::LooksLikeVtablePointer` requires
the first 8 bytes to be non-null, 8-byte aligned and inside the x64 user-mode canonical range. Two
floats (`0x400000003F800000`), a double (`0x40934A0000000000`) and an `FLinearColor`'s first two
components all fail it; a module address (`0x00007FF6...`) passes.

The whole decode moved into `Ubel.h` as pure `inline` code (`LooksLikeVtablePointer` /
`InterpretStructBytes`) beside `ComputeHoles`, because **no target compiles `Ubel.cpp`** and
`dll_helpers_test` already includes `Ubel.h` — the MA2 pattern.

⚠ **HALF THE DEFECT REMAINS, and it is asserted rather than papered over.** A 24-byte struct is three
doubles (UE5 LWC `FVector`) or six floats, and *the bytes cannot say which* — `Radar.h` states this
repo rule outright: the struct name does not determine the width and neither does the engine version.
So an LWC vector still decodes wrongly; the skip no longer eats its X, but the doubles are still split
into float halves. Only the reflected layout settles it, and swapping one guess for another would be
the same defect with different numbers. Test case 6 asserts the *current* six-value output precisely
so this cannot be mistaken for fixed. The layout-driven half — routing the four call sites that
already hold the `UScriptStruct*` (`fv.mapValueStructAddr`, `fv.setElemStructAddr`, `m.structType`,
`fi.structType`) into the field-driven decoder that already exists at `Ubel.cpp:4832-4899` — stays
open on the register.

**Verification.** 246 + **1235** C++ assertions (+14) and 3921 C# tests green. Negative control: the
gate reverted to the old `size > 8` produced **exactly three** failures — `FVector3f`, `FLinearColor`
and the LWC component count — while **the GAS regression guard kept passing**, which is what
demonstrates the suite tells this fix apart from "just delete the skip". Blast radius is one function:
`f:[` appears in source only in `Ubel.cpp`, nothing parses it, so the output format was free to change.

**Also:** `uint64_t small` does not compile in this test file — `rpcndr.h`, via `Windows.h`, does
`#define small char`. It fails as *"'uint64_t' followed by 'char' is illegal"*, which names neither
the macro nor the header.

-----

## 2026-08-17 - A3: Value Search indexed ONE FVector per class, ever (build 3168)

**A cycle guard that answered the wrong question.** `ScanForValue`'s per-class index builder
(`expandFields`, [Aura.cpp:6411](../dll/src/Aura.cpp)) threaded a single `unordered_set` through the
whole struct walk with `visited.insert(structAddr)` and **no matching erase anywhere**. One set per
`buildClassIndex` call, by reference, for the entire tree — so it answered *"have I ever seen this
`UScriptStruct` in this class?"* when the only safe question is *"am I currently inside it?"*.

Consequence: only the **first** field of a given struct type in a class contributed leaves, and every
later one was dropped **subtree and all**. And the loss is **cross-branch**, not "sibling fields of a
repeated struct" — once `Vector` had been entered anywhere, every other `FVector` in that class at any
depth was skipped. An ordinary actor indexed `Location` but never `Velocity`, `Scale3D` or `Extent`;
inside a single `FTransform`, `Translation` blocked `Scale3D`. Value Search then reported no match for
a field that was sitting right there.

**The filed framing ("hits GAS") was the wrong headline** and would have scoped the fix. The dominant
real-world hit is `FVector`/`FRotator` repeats on ordinary actors under Float / Double / NumericAll —
this tool's most common query. GAS is one instance, not the scope. Conversely **vector-typed scans are
unaffected**: `Radar::VectorStructNames` is non-empty there, so the `acceptedStructNames.empty()` gate
skips the recursion and the guard never fires — a verification run using an FVector scan would have
shown nothing wrong.

**Why it never looked broken:** `git log -L` puts the guard in `da9865dd`, *"recurse StructProperty so
GAS / nested-struct leafs are reachable (build 740)"* — the commit that added nested-struct support
shipped with the bug that half-defeats it. `Health` was found; only its siblings were missing.

- **`Aura::StructPathGuard`** (new, header-inline in [Aura.h](../dll/src/Aura.h)) — RAII, scoped to the
  active path. RAII rather than a bare `erase`, because the lambda has many early exits and the first
  one anybody forgets silently restores the bug.
- **`kMaxScanFieldsPerClass = 4000`** — path-scoping deliberately removes the accidental bound the
  whole-walk set was providing, leaving only `depth <= 4` on a tree whose fan-out is
  (fields per struct)^4. Mirrors `kMaxSchemaLeavesPerClass`, which its sibling pairs with the same
  path-scoped guard for exactly this reason. **The one-line fix as filed would have removed a bound
  without adding one.**
- **The cap logs when it bites.** A missing leaf is indistinguishable from a value that is not there —
  which is precisely how this stayed invisible for ~2400 builds.

**Two walkers already had it right**, which is the observable tell: `CollectSchemaLeaves`
(Property Search Deep, erases at `Aura.cpp:4251` under a comment that states the intent verbatim) and
`CollectGroupLeaves` (Group Scan, push/pop at 8114/8150). So Group Scan and Property-Search-Deep found
`MaxHealth` while single-value Value Search did not — **a distinct in-the-scanner cause for the
"Value Search can't find field X" family in [working-lessons.md](working-lessons.md) §5, separate from
AB4.**

**Verification.** No target compiles `Aura.cpp`, so the semantics were moved into a header-inline type
and pinned in `dll_helpers_test` (which already includes `Aura.h`): 246 + 1221 C++ assertions and 3921
C# tests green. Negative control — the guard's destructor emptied to reproduce the whole-walk
behaviour — produced **7 failures including both A3-labelled sibling assertions**, then green again on
restore. The test's third case is itself a control in the opposite direction: re-entry *along the
active path* must still be **refused**, or "siblings work" would be passing for the wrong reason and a
self-referential `USTRUCT` would recurse until the stack died.

⚠ **Still needs a live check.** The unit test pins the guard's contract, not the walk that uses it.
The in-game confirmation is cheap and specific: a Float scan on an actor class should now index
`Velocity` / `Scale3D`, not just `Location`.

-----

## 2026-08-17 - AF3: the Live Funcs panel was silent about a cap that removes its own target (build 3167)

**The cap keeps the highest counts; this panel exists to find a low one.** `pe_profile_get` sorts the
whole table by fire count desc and emits only the first `FetchLimit = 300` rows, while
`distinct_funcs` stays **pre-cap** ([Fern.cpp:3983](../dll/src/Fern.cpp), pipe-protocol.md says
"pre-cap" explicitly). The panel's own dev-log states the workflow: *"The action-specific function is
near the top with a **low** count (a handful of calls); per-frame Tick/Update noise has huge counts."*
Count-desc + cap deletes precisely that tail. On a game whose window dispatches more than 300 distinct
UFunctions, `OpenShop` (count 1-3) is not a mis-ranked row — it is an **absent** row, and nothing said
so. The busier the game, the more certain it was that the answer was the part that got cut.

**The compounding half: a baseline captured from a capped page fabricates the answer.**
`SetBaseline` built `_baseline` from the same truncated page, so any idle function below the cut
missed `_baseline.TryGetValue` on the action fetch → `IsNew = true` → sorted to the very top by
`OrderByDescending(e => e.IsNew)` → survived the default New/changed-only filter. The status line then
read *"The action's function is almost certainly among the NEW rows at the top."* Worse, `std::sort`
is not stable and the input is `unordered_map` iteration order ([Linie.cpp:92](../dll/src/Linie.cpp)),
so which count-1 functions survive the cap differs arbitrarily between the two recordings — false NEWs
land in the same tail as the true ones and are indistinguishable by inspection.

- **Truncation is derived, not guessed** — `Entries.Count < DistinctFuncs`. Conservative and correct:
  it is also true when `ResolveFunctionInfo` dropped stale pointers or the `Tot` abort cut the emit
  loop short, and all three mean the same thing to the user.
- **Both status lines are honest now.** Non-diff gains the house cap string
  (`SnapshotViewModel`/`SpcQueryViewModel` convention), spelled `(showing top N of M by count)`
  because *which* rows were cut is the point. The diff line stops reporting page-scoped counts against
  a table-scoped denominator — "1 NEW of 900" invited reading 900 as the population those rows were
  selected from when only 300 were examined — and **drops the certainty claim** when either page was
  capped, replacing it with what NEW actually means: *not in the idle top N*, not *did not fire*.
- **`SetBaseline` marks the baseline PARTIAL rather than refusing it.** Refusing would disable Diff on
  exactly the busy games it is for; the defect was never the capture, it was the silence.
- **AF28 — found while re-deriving, filed as its own LOW row rather than folded in silently:**
  `GroupBy(Key).ToDictionary(g => g.Key, g => g.First().Count)`
  made the captured baseline depend on a **view toggle**. `_allEntries` is re-sorted *in place* by
  `ApplyDiffAndFilter`, and Earliest-first orders it by `FirstSeq` — so `First()` took whichever
  duplicate-key row was on top of the current grid. Now `Max(x => x.Count)`, which is what `First()`
  was reaching for and is sort-independent.

**Tests: 8 new, and the fixture trap was real.** `ResultOf(...)` sets `DistinctFuncs = entries.Length`,
so all 13 existing uses are structurally incapable of expressing a truncated page — they always assert
the degenerate not-truncated case, which is why ~30 tests never caught this. A separate
`TruncatedResultOf(distinct, entries)` was added rather than reusing it (working-lessons §2.3, "a
fixture that reports coverage it does not have"). Four of the eight are **negative controls in their
own right**: the cap note must NOT appear on a whole fetch, and the original certainty sentence must
SURVIVE when nothing was capped — otherwise the fix has merely deleted a working hint.

**Verification.** 3921 tests green. Then five source mutations, each reverting one piece of the fix and
each observed to fail: dropping the cap note, dropping the PARTIAL marking (2 tests), `Max`→`First`,
forcing the diff line always-confident, and reporting diff counts against the table again. Restored
tree green again. **The harness itself needed a negative control first** — the initial run passed
`--nologo`, which Microsoft.Testing.Platform does not accept, so it printed help and ran **zero**
tests while reporting every mutation as "no test failed". Only checking the *restored* tree's exit
code exposed it. Half 2 of the fix shape (a `sort` parameter on `pe_profile_get` so the UI can ask for
the low-count tail) is DLL work and deliberately not done here — the false-NEW manufacture is a
client-side bug and stays reachable through any DLL change.

-----

## 2026-08-17 - PX1: two proxies handed our functions the real DLL's ordinals (build 3166)

**A proxy DLL is a NAME map AND an ORDINAL map.** PX1 was filed against `dinput8` for the first;
re-derivation found the second, on `version.dll` — the UI's default proxy — eight times over. Nine
collisions across two proxies, not one.

**The loud half** (dinput8 only): the real `dinput8.dll` exports six functions and
`ProxyDinput8.def` listed five. A by-name static import of `GetdfDIJoystick` against our proxy fails
process creation with `STATUS_ENTRYPOINT_NOT_FOUND` — before `DllMain`, before `Sein` has a log file
to say so.

**The quiet half, and the worse one** (both proxies): `link.exe` hands **unpinned** exports out in
name-sorted order starting at **(highest PINNED ordinal + 1)** — not at 1, and not
"alphabetically from 1" as the finding assumed. Neither hand-written `.def` pinned anything, so on
dinput8 the five real forwards landed on `@1..@5` by luck and our `UE5_*` block began exactly where
`GetdfDIJoystick` belonged; on version the nine `GetFileVersionInfo*` names took `@1..@9`
alphabetically and our block took `@10..@17`, which is where the real DLL keeps `VerFindFileA/W`,
`VerInstallFileA/W`, `VerLanguageNameA/W` and `VerQueryValueA/W`. An ordinal import of any of those
nine did not fail — it called one of ours, with an unrelated signature.

**Two filed premises were wrong, and both were load-bearing for the repair.** The ordinal rule above
is why pinning *only* the new export would have been **worse than the bug** (the five correct
forwards would have moved off their own ordinals to `@7..@11`). And `/DEF:` does **not** suppress
`__declspec(dllexport)` — it *merges* with it, which three `.def` headers claimed otherwise; the
shipped dinput8 proxy exported 66 names while its `.def` listed 36. So the missing export needed an
*implementation*, not just a line.

- **`Lugner_Dinput8.cpp`** — a sixth forwarder, `Proxy_GetdfDIJoystick`, declared `const void*` so
  `dinput.h` stays out of the build (no-args/pointer-return either way, so a plain C forwarder is
  exact and the asm jmp-thunk machinery dxgi/winmm need for undocumented internals is unnecessary).
  The four comments that made the wrong count *look* verified are corrected.
- **`ProxyDinput8.def`** pins `@1..@6`, **`ProxyVersion.def`** pins `@1..@17` — shipped as two
  separate commits so a regression on the default proxy has one suspect. version's source order is
  deliberately *not* renumbered into ordinal sequence, so it stays line-diffable against `Lugner.cpp`.
- **[`tools/check_proxy_exports.py`](../tools/check_proxy_exports.py)** re-derives it permanently
  against a committed System32 baseline (`tools/pe/proxy-export-baseline.tsv`) — a baseline rather
  than a live read because these tables vary by Windows build and a runner whose System32 differs
  from the maintainer's must not redden an unrelated PR. Five rules; rule 4 (*max pinned >= max
  real*) is the structural one that keeps the unpinned `UE5_*` block above the real range with no
  per-symbol bookkeeping. Wired into CI **twice**: the `.def` source before the build, the four
  linked DLLs after it — because PX1's own severity was misjudged from the source and it was the
  artifact that disagreed.
- **Negative controls** — 6 mutations run on scratch copies, all observed to fail (2/7/5/3/1/18
  errors); case 6 reverts version's pins and reproduces the shipped defect exactly. dxgi and winmm
  pass untouched, so the check distinguishes the two broken `.def`s from the two correct ones.

**Verified on the artifacts, not the source:** all four proxies now diff clean against real System32
— zero missing names, zero ordinal mismatches. dinput8 67 exports (ours start `@7`), version 78
(`@18`), dxgi 81 (`@21`), winmm 241 (`@183`). No live-game verification was added to the backlog:
`ProxyImportAnalyzer.cs:285` records only 1 of 21 measured games statically imports dinput8, and the
modern Windows SDK *statically defines* `GetdfDIJoystick` rather than importing it, so there is
probably no game that can exercise the original failure.

**Also shipped: a Startup-shortcut tool, in two languages.**
[`scripts/startup-shortcut.ps1`](../scripts/startup-shortcut.ps1) and
[`scripts/startup_shortcut.py`](../scripts/startup_shortcut.py) create / remove / inspect a per-user
Start Menu Startup shortcut for `UE5DumpUI.exe`, resolving the exe from their own folder; `build.ps1`
copies both into `dist\` beside it. Current user only by design (the folder comes from the shell, not
from a literal `%APPDATA%` path, which is localised and redirectable). No pipe, no network. Both
refuse to overwrite or delete a shortcut pointing at anything else without `-Force`, and both read the
shortcut back after writing — `Save()` reports success by not throwing, and a wrong Startup shortcut
gives no feedback until the next sign-in. **Why two:** Bitdefender's Advanced Threat Defense
quarantined the `.ps1` on its first run and took `build.ps1`,
`scripts/gen_proxy_forwarders.py`, `tools/check_proxy_exports.py` and `dist/UE5DumpUI.exe` with it —
see [working-lessons.md](working-lessons.md) §3.8.

**Verification split by host, deliberately.** The Python tool was swept automatically — 11 cases
including both refusals, all inside a temp folder via `--startup-dir`, real Startup folder never
touched. The `.ps1` was **not** re-run here (automated PowerShell is what tripped the AV); the
maintainer ran it by hand from `dist\` instead: `status` → `remove` on an empty folder → `install`
→ directory listing showing `UE5CEDumper.lnk` → `remove` → listing showing it gone. All six steps as
expected, with the two other programs' shortcuts in that folder untouched throughout. Two things
that cross-check: the shortcut is **989 bytes, identical to the one the Python twin writes**, so the
two implementations produce equivalent `.lnk` files; and the em dashes rendered correctly, confirming
the UTF-8 BOM fix (Windows PowerShell 5.1 reads a BOM-less `.ps1` as ANSI and had been printing them
as `??`).

-----

## 2026-08-16 - AU1: find-object-by-path never existed, on three APIs that advertised it (build 3157)

**Found by VERIFICATION, not by a finder.** Running todo.md's AA4-AA7 step 1 against a real Cheat
Engine on Elliot produced `[UE5Dissect WARN] Object not found: /Script/Engine.Actor`. The DLL exports
resolved fine, so this was not the AA4 error-reporting fix misbehaving - the DLL genuinely could not
find `AActor`, a class that is present in every UE process ever built.

**What was wrong.** Three APIs claimed a capability none of them had:

- `Frieren.cpp:691` - `UE5_FindObject(const char* fullPath)` handed `fullPath` straight to
  `Aura::FindByName`, which matches a **bare FName**. Every path-shaped argument returned 0.
- `Fern.cpp:1910` - the pipe's `find_object` did the same, on a variable literally named `path`.
- `Aura.cpp:1408` - `FindByFullName`, the only path-shaped API, was a **stub returning 0**
  (`(void)fullName; return 0;`) whose comment claimed it "is implemented after UStructWalker is
  available". It never was. It had **zero callers**, and `docs/dll-spec.md:218` listed it as real
  API - so nothing failed loudly and the gap survived indefinitely.

**Root cause is a format mismatch nobody ever compared.** `Ubel::GetFullName` emits
`//Script/Engine/Actor` - double leading slash, `/` between package and object. Every caller, doc,
`.CT` and Lua script writes UE's own `/Script/Engine.Actor`. Those two strings denote the same object
and compare unequal, which is why a path resolve found nothing *at all* rather than finding the wrong
thing. Measured before the fix: `find_object "Actor"` -> `0x7FF4DDE12068`;
`find_object "/Script/Engine.Actor"` -> `Object not found`.

**User-visible blast radius.** `ue5_dissect.lua`'s `createFromPath` is the documented entry point for
building a CE structure from a class path, and `createInteractive` **pre-fills its dialog with
`/Script/Engine.Actor`** - so the shipped default input failed 100% of the time. AA4-AA7 step 1, as
written, could never have passed on any game.

**The fix.** A pure canonicalizer trio header-inline in `Aura.h` -
`CanonicalizeObjectPath` / `LooksLikeObjectPath` / `PathLeafName` - reducing all three spellings
(`//Script/Engine/Actor`, `/Script/Engine.Actor`, `Class /Script/Engine.Actor`) to one form, with
`.` and `:` treated as separators and case preserved (every sibling name compare in `Aura` is
exact-cased). `FindByFullName` is now real: it gates the expensive `GetFullName` Outer-chain walk
behind a **cheap FName leaf pre-filter**, so it costs one FName read per object instead of a string
build over ~85K objects. One new entry point, `FindByNameOrPath`, is what both callers use.

**Path is tried FIRST when the query carries a separator, and that ordering is the design.**
`/Game/A.Foo` and `/Game/B.Foo` share the leaf `Foo`; answering either with whichever object the
GObjects walk reached first is a wrong answer that looks like a right one. Bare names skip the path
attempt entirely, so `FindByName`'s historical single-pass cost is unchanged.

**Testability, per the MA2 lesson.** No test target compiles `Aura.cpp` - but
`dll_helpers_test.cpp` **already includes `Aura.h`** (for `IsEnginePackage`), so the pure half is
directly pinnable. 16 new assertions; the negative control (removing the `.`/`:` rewrite) turns
**6** of them red, and notably NOT the `//Script/Engine/Actor` case, which needs only slash
collapsing - the assertions discriminate rather than failing as a block.

**Live verification (Elliot, UE 5.4, 84,990 objects, proxy dxgi build 3157).**
`/Script/Engine.Actor`, `Class /Script/Engine.Actor`, `//Script/Engine/Actor` and bare `Actor` all
resolve to the **same** `0x7FF4DDE12068`; `/Script/Engine.Pawn` resolves separately. The negative
control is the one that matters: **`/Script/NoSuchPkg.Actor` returns "not found"** even though the
leaf `Actor` exists - proving the package half is genuinely matched and not quietly ignored.
End-to-end, `createFromPath("/Script/Engine.Actor")` now builds a **129-field `Actor`** structure in
CE with `unnamed=0` rows and a correct header (`0:VTable | 8:ObjectFlags | 12:ObjectIndex |
16:Class | 24:FNameIndex | 32:Outer`), closing AA4-AA7 steps 1 and 5.

## 2026-08-17 - MA2: one predicate for "where can this pattern start" (build 3135)

**`ScanRegionBatch` computed `regionSize - pat.bytes.size()` as an unsigned max-start** while
guarding only against `minPatLen`, the batch's **shortest** pattern. A batch mixing a 60-byte and a
200-byte pattern therefore cleared a 100-byte region and then underflowed on the second: `100 - 200`
as `size_t` is ~1.8e19, and `for (pos = …; pos <= patMaxStart; ++pos)` walks the address space — in a
function with **no SEH**, so an access violation rather than a caught read failure.

**Latent, and the reachability claim was verified rather than repeated:** the sole caller
(`Genau.cpp:1312`) passes three arguments, so `AOBScanBatch`'s `moduleBase` defaults to 0 → the main
module, whose exec sections are ≥ 3,660 B against patterns ≤ ~60 B. It goes live the moment that
call site is given a small module.

### The correct predicate already existed twice in the same file

`ScanRegion` and `ScanRegionAll` each did `if (regionSize < patLen) return;` **before** computing
`maxStart`. The batch version had the same idea at the wrong granularity — per batch instead of per
pattern. So this did not need a new rule, it needed the existing one applied.

**Shipped as one shared `Macht::PatternScanRange(regionSize, patLen, maxStartOut)`** — header-inline
and pure — now feeding **all four** scan loops (both single-pattern scanners and both of the batch's
scalar loops), plus an early skip at the batch's classification step so a too-long pattern reaches
neither `simdEntries` nor `scalarOnlyIndices`. The underflow is now impossible by construction, not
guarded in three places out of four, and the next copy of this loop inherits the guard.

`patLen == 0` is refused rather than accepted: the old arithmetic gave `maxStart == regionSize`,
which reports a match one past the end. `ParsePattern` cannot currently produce an empty pattern, so
that is belt-and-braces — but it is the branch a future caller gets wrong.

### It is testable after all, which was worth checking before assuming

No test target compiles `Macht.cpp` and `ScanRegionBatch` is `static`, so the first read was "this
cannot be pinned". But `dll_helpers_test.cpp` **already includes `Macht.h`** (it exercises the real
`ParsePattern` for the same reason), so a header-inline predicate is directly reachable. 11 new
assertions (1182 → **1193**) including the exact-fit and one-byte-too-long boundaries and the full
batch scenario; negative control (predicate reverted to the bare arithmetic) **5 red**.

-----

## 2026-08-17 - AB4: a width gate that is right for Exact and wrong for ordering (build 3133)

**`BuildNumericTargets` asked "does the target FIT this width".** Correct for `Exact` — a value that
does not fit cannot equal a field of that width — and **wrong for `Smaller`/`Bigger`**, where the
question is whether *any* value of the width can satisfy the comparison. Every `Int16` field is
smaller than 70000, but 70000 has no int16 encoding, so no `Int16` entry was emitted and `Aura`'s
`multiResolve` skipped **every 2-byte field in the pool**. No error, no warning, no log line.

The four cases are **not symmetric**, and the old code was right in exactly two — which is why the
gate reads like a working optimisation:

| | target above the width's max | target below its min |
|---|---|---|
| **Smaller** | every value matches → **dropped (the bug)** | none match → dropped ✓ |
| **Bigger** | none match → dropped ✓ | every value matches → **dropped (the bug)** |

**The SIGN domain was the bigger leak and the finding never mentioned it.** A negative string
suppresses the whole unsigned parse, so `Bigger -5` dropped every `UInt16`/`UInt32`/`UInt64` field
although every unsigned value satisfies it.

### Clamping is the trap, and it looks like it works

`Smaller 500` clamped to Int8's 127 becomes `cur < 127` — `ApplyOrdered`'s `Smaller` is a **strict**
`<` — so it silently drops the field holding exactly 127. It restores ~99.6% of the missing rows and
re-introduces the same class of silent loss in a form far harder to find. **There is no int8 byte
pattern meaning `cur <= 127`**, so the answer cannot live in a fixed-width buffer: it has to be a
verdict. Hence `Fit::AlwaysTrue`, and `Find()` deliberately **hides** such entries — handing out
their zeroed buffer would compare against 0, wrong in a quieter way than the bug.

### The re-derivation's own fix shape was broken, and the skeptic caught it

It put the verdict on `Entry` while every consumer looks up through `Find()`, which returns
`const uint8_t*` — the flag could never have been seen. (Its own `wrong_obvious_fix` section argued
the repair "cannot live inside the fixed-width buffer" and then put it there.) Shipped instead as
`FindEntry()` plus an `Entry`-taking `ComparePredicate` overload, so **the verdict is honoured once,
in `Radar.cpp`** — the file `dll_helpers_test` compiles — and `Aura.cpp`, which no test target
compiles, gets a mechanical `Find` → `FindEntry` substitution with nothing to get wrong. That split
was the maintainer's call and it is the right general rule for this tree.

`Between` is **deliberately not fixed**: its two bounds are built by two *independent* calls at four
call sites, and `ApplyOrdered` normalises reversed bounds at compare time, so which is the lower
bound is not known at build time. A correct fix needs a joint builder — filed, not half-done.

16 new C++ assertions (1166 → **1182**), including the boundary case clamping would have dropped.
Negative control (verdict forced to `false`) **6 red**. ⚠ The Aura half is unverified — 7 steps in
todo.md, with step 4 as the control that the pruning half still prunes.

### AB7 was downgraded, not fixed — and its prescription refused for the third time

Its defect text ends *"no serial witness is stored"*, i.e. it prescribes storing one. That is the
prescription working-lessons §4.3 already refuses twice. **AB7 makes the trap sharper**:
`InstanceRecord::instanceIndex` is *already* stored, so "just add the serial next to it" is a
one-line change that would produce a validator passing on every recycled slot. Clause 1 ("raw
addresses used as identity across RPCs") is **refuted** — identity is the pool index. Clause 2 (the
index is captured and never validated) is verbatim true, but the harm chain over-claimed twice and
one downstream harm is already closed in-tree by `Ubel::WalkInstance`'s recycled-slot gate.
**MED → LOW, still open**, with the correct shape recorded for whoever takes it.

-----

## 2026-08-17 - CORRECTION: the Skia crash IS symbolizable, and it is path geometry (build 3131)

**Correcting build 3127's entry, which said "there is no PDB for `libSkiaSharp`, so the faulting
function is unknown".** That was wrong. `SkiaSharp.NativeAssets.Win32` **ships `libSkiaSharp.pdb`**
in the NuGet package — for 3.119.4, 4.150.1 and 4.151.1 alike — and `HarfBuzzSharp.NativeAssets.Win32`
ships one too. The maintainer checked the NuGet cache; I had assumed rather than looked.

Symbolizing `libSkiaSharp+0x102B8D` against the **4.151.1 win-x64** binary
(`llvm-symbolizer --obj=… --relative-address`, from
`VC\Tools\Llvm\**x64**\bin` — the recursive search finds the ARM64 copy first and it will not run):

```
skia_private::TArray<SkPathVerb,1>::size      include/private/SkTArray.h:419
  -> SkSpan<const SkPathVerb>::SkSpan         include/core/SkSpan.h:99
  -> SkPathBuilder::verbs                     include/core/SkPathBuilder.h:986
  -> SkPathBuilder::computeFiniteBounds       src/core/SkPathBuilder.cpp:1102
  -> SkPathPriv::Raw                          src/core/SkPathPriv.h:393
```

**Binary identity confirmed, not assumed:** the `libSkiaSharp.dll` in `dist/` at crash time was
12,272,440 bytes and the NuGet 4.151.1 win-x64 payload is 12,272,440 bytes — the same file.

**What this changes.** The out-of-bounds read is in **path geometry**, reading a `TArray<SkPathVerb>`'s
size through an `SkSpan` while computing a path's bounds. So **HarfBuzz is exonerated for this fault**
— it is not text shaping — and the ABI hypothesis gets materially stronger rather than staying a
guess: `SkPathBuilder` is exactly the area Skia restructured across this major, and a caller built
against the old layout reading a `TArray` header at the wrong offset yields a bogus `size`, after
which `SkSpan` walks off the end. That is the observed fault, not a story about it.

**Still not proven:** that Avalonia.Skia is the caller which supplied the mis-shaped path. Naming the
callee is not naming the caller. The next step, if it ever recurs, is a page-heap dump with the full
stack symbolized — now known to be possible.

**Method lesson recorded in working-lessons §3.6:** *check the package for a PDB before declaring a
native crash unsymbolizable*, and use the **x64** llvm-symbolizer.

-----

## 2026-08-17 - AA9: the helper's own samples taught a freeze that cannot be stopped (build 3129)

**`ue5_freeze_helper.lua`'s header told users to hold the handle in a `local`.** Cheat Engine
compiles **each `{$lua}` block as its own chunk** — verified in CE's source, not assumed:
`autoassembler.pas` matches a *trimmed, uppercased, whole* line against `{$LUA}`, prepends
`local syntaxcheck,memrec=...`, takes the one shared `GetLuaState`, and hands the block's text to
`luaL_loadstring` on its own. So globals cross between `[ENABLE]` and `[DISABLE]`; locals do not.

A handle parked in a `local` is therefore **unreachable forever**. Not awkward — unreachable:
`start()` gives two timers to CE's **main form**, so unticking the record does not stop them and
neither does deleting it, because the timers do not belong to the record. Re-enabling adds another
orphaned pair. Only restarting Cheat Engine ends it. SAMPLES 1–3 showed no stop at all, and SAMPLE 4
was worse than silent: `-- In [DISABLE]: hp.stop(); mp.stop()` is advice that **cannot work**.

Rewritten so SAMPLE 1 is the complete shape — keyed **global** table, defensive pre-stop, the
outcome check from build 3125, and the matching `[DISABLE]` half — with 2–4 reduced to cfg deltas so
the lifecycle is stated exactly once. This is the shape `FreezeScriptGenerator` already emits; the
header had been contradicting the shipped generator both by demonstration and by omission.

**A bare global `h` is NOT the fix** and is worse than the bug: CE shares one Lua state across every
open table, so a second script using `h` steals the first's slot — and then the *first* script's
`[DISABLE]` stops the *second* script's freeze. The lifetime box says so explicitly.

### The test RUNS the documentation

Asserting on a doc's text is tautological, so the rig now **extracts the sample from the header
comment and executes it** under a faithful model of CE's two-chunk compilation (`load()` per block,
`syntaxcheck`/`memrec` injected as chunk locals, only globals shared). It asserts the timers are
really destroyed by the *second* chunk — plus a control that runs the OLD shape and asserts it fails
as a nil global with the timers still live.

**The extractor's own first version was wrong, and the test caught it.** `gmatch('{%$lua}(.-){%$asm}')`
captured the prose of the new lifetime box, which mentions `{$lua}` in a sentence. The fix was to do
what CE does — exact whole-line match — so modelling the real thing faithfully also removed the bug.
Rig 38 → **50 checks**; negative control (sample stops publishing the handle) 2 red.

⚠ **Not verified in a real Cheat Engine.** The rig models CE's chunk rule; it is not CE.

-----

## 2026-08-17 - The UI's heap corruption was SkiaSharp, one major ahead of Avalonia (build 3127)

**The UI died twice in 14 minutes with `0xC0000374` STATUS_HEAP_CORRUPTION**, minutes after a
Copy CE XML on Elliot. Fixed by aligning `SkiaSharp` **4.151.1 → 3.119.4** and `HarfBuzzSharp`
**14.2.1.2 → 8.3.1.3** — the versions `Avalonia.Skia` / `Avalonia.HarfBuzz` 12.1.1 are built against.

### The method matters more than the fix: a heap-corruption dump names nothing

The first dump was useless and *had* to be. Heap corruption is detected at the **next heap
operation**, so its stack is the **detector, not the culprit** — ours showed ntdll's heap-error path
on the UI thread with Skia/DWrite/user32 frames below it, which is motive and opportunity, not the
act. Our own C# was ruled out on structure alone: no `AllowUnsafeBlocks`, no `Marshal.AllocHGlobal`
or `Marshal.Copy` anywhere in the UI, and the only `stackalloc`s are bounded `Span`s — a stack
overrun is `0xC000_00FD`, not `0xC000_0374`.

**Full page heap** (IFEO `GlobalFlag=0x02000000` + `PageHeapFlags=0x3`) converted the next
occurrence into an immediate `0xC0000005` at **`libSkiaSharp.dll+0x102B8D`** — WER event
`AutoVerifierV2`, `verifier.dll` on the stack, faulting address a guard page. Base cross-checked two
ways (`0x7FFEE53C2B8D − 0x102B8D` and `0x7FFEE5453602 − 0x193602` both give `0x7FFEE52C0000`).
**Re-run a heap-corruption crash under page heap; do not try to read the first dump.**

### Why nothing in the build caught it

`Avalonia.Skia` declares an **open-ended minimum** (`SkiaSharp >= 3.119.4`), so a major-version jump
*satisfies* the constraint: no NU1608, no NU1605, and `TreatWarningsAsErrors=true` had nothing to
fail on. NuGet's dependency model cannot express "and not a different major". Three routine
`chore(deps)` bumps had walked Skia 4.148 → 4.150.1 → 4.151.1 while Avalonia stayed on 3.x, and the
csproj carried **no comment** saying why those two were pinned above what Avalonia asked for — unlike
the `SQLitePCLRaw` pin two lines below, which has a full paragraph of rationale.

Both versions now live in **one** `PropertyGroup` (`$(SkiaSharpVersion)` / `$(HarfBuzzSharpVersion)`)
feeding all seven references, so a future bump cannot be applied to some of them and not the others,
with the recovery command and this incident recorded next to them.

### What is proven, and what is not

Proven: libSkiaSharp accessed out of bounds, caught red-handed. **Not** proven: that the version gap
*caused* it — there is no PDB for `libSkiaSharp`, so the faulting function is unknown. If crashes
continue at the aligned versions the hypothesis is refuted and the next step is a Skia bug, not
another dependency change. Verified so far only that the AOT publish is clean (54.4 MB trimmed, zero
NuGet warnings, full suite green) and that the native assets really swapped (`libSkiaSharp.dll`
11.7 → 11.1 MB, `libHarfBuzzSharp.dll` 1.9 → 1.7 MB). ⚠ **A pass here is several quiet sessions, not
one** — see todo.md, and note the broad rendering regression check: HarfBuzz went back *six* majors.

-----

## 2026-08-17 - AA12 + AA13: a freeze that applied NOTHING reported clean success (build 3125)

**`pcall` answers "did Lua raise", never "did anything get frozen"** — and on the shipped path no
mailbox failure *can* raise, because every one of them is caught inside `fetchInstancePage`'s own
`pcall`. So `pcall(handleOrErr.start)` was `true` for a DLL that was never injected, for a contract
mismatch, and for a stale mailbox alike; the generator then auto-closed the CE Lua window over a
record it left ticked. The user is told a freeze is active while nothing is being written.

### The two findings are one defect, and two neighbours moved

**AA13 has no independent content left.** Its unique clause — *"nothing reads `_lastError`"* — is
stale: AA3 (build 2926) added `handle.lastError()` / `handle.isAbandoned()`. What survives is one
level up — those accessors have **zero shipped callers**, and the C# test that "guards" them asserts
only their *source text*. AA3 moved the defect from *write-only field* to *accessor nobody calls*.
Its other clause is refuted outright: the persistent case is **not** silent (AA3's abandonment
`print` fires ~10 s in, and CE's `print2` re-opens the window the generator just closed).
**AA10 and AA11 were downgraded to LOW** on the same pass and are no longer part of this clump.

### The crux is a tension, and every obvious fix fails it

`count == 0` is **not** a failure. A class-wide freeze armed before its instances spawn is the
helper's advertised purpose, so unticking there converts the feature into the bug. And the DLL
cannot separate a *misspelled* class from a *live-but-empty* one — `HandleListInstances` answers
`SetDone(0)` for both — so neither can the Lua side. *"Armed, 0 right now"* is the only honest report.

Three refuted repairs, each at the source: unticking on `count == 0` (breaks the feature); unticking
whenever `lastError() ~= nil` (`_lastError` carries the transient `'mailbox busy'` on the same
channel as fatal errors — which is precisely why `MAX_FAIL_STREAK` exists); and putting the untick in
the **helper**, where `memrec` is a chunk *local* (`autoassembler.pas:1419`) and the helper is a
separately-`load`ed chunk — a guard that could never fire.

### What shipped

`rescan()` / `handle.start()` now return **`(ok, err, count)`** — three outcomes, not two. Helper
**1.1 → 1.2**. The timers still start in all three cases and `start()` deliberately does **not**
raise: the generated script stores the handle *before* calling start and its failure branch nils the
slot **without** stopping, so a raise thrown after `createTimer` would strand two timers writing into
the game with nothing able to reach them. Reporting by value is what keeps the cleanup reachable.

`FreezeScriptGenerator` reads the outcome instead of the `pcall` status: hard failure → `stop()` →
clear the slot → `showMessage` → untick → **return before the close**; `count == 0` → keep running,
print once, keep the window open; and a **fourth** state — an older embedded helper returns `nil`,
which is neither success nor failure, so it says exactly that rather than inventing a verdict.

### Verification

`scripts/tests/freeze_helper_test.lua` 23 → **38 checks**, written to fail first (**8 red**). Five new
C# tests, including two ORDERING assertions — a reorder that moved the close above the bail-out would
still pass every `Contains` check. **Four independent negative controls**: success branch 5 red,
hard-failure branch 5 red, close condition 2 red, `nil`-check order 1 red.

**Two things the controls found that the fix did not.** The hard-failure control left the AA13 case
GREEN: it asserted only that the two outcomes *differ*, and `nil ~= true` differs — so it tolerated
the very regression it existed to catch. Strengthened to the concrete triple; the control then went
2 → 5 red. And the rig itself was **blinder than the real API**: its write stubs only appended to a
log and never touched `MEM`, so `waitDone`'s status poll could never be satisfied and *every case in
the file* had been exercising the no-symbol failure path. Fixed with `MEM`-backed writes and an
`installMailbox()` that models `HandleListInstances`.

⚠ **Not verified in a real Cheat Engine** — the rig stubs CE. See todo.md's register.

-----

## 2026-08-17 - G12 + G3: one offset family, and a re-probe that should not happen (builds 3119 / 3121)

**The finding I set out to fix turned out to be a LOW; the one found while re-deriving it is the
real defect.** C++ 1137 → **1166**.

### G12 (MED, new) — the `sizeof(FProperty)` family was published SPLIT

`FSTRUCTPROP_STRUCT` / `FARRAYPROP_INNER` / `FBOOLPROP_FIELDSIZE` / `FBYTEPROP_ENUM` all name the
**same** slot — the first subclass field, `== sizeof(FProperty)` — and `FENUMPROP_ENUM` sits 8 bytes
later because `FEnumProperty` declares `FNumericProperty* UnderlyingProp` first. Five names, one
measurement, and **four independent writers**.

Genau's Step 2.5 default block set only **two** of them (`STRUCT`/`BOOLSIZE` → 0x70), leaving
`INNER`/`BYTEENUM` at 0x78 and `ENUMENUM` at 0x80. Three *"keeping defaults"* exit paths then shipped
that split **for the whole session**: TArray element descriptors and every enum-name read 8 bytes
off, while struct reads stayed correct — the "names resolve, values are garbage" shape.

Deterministic, first run, **no concurrency and no re-entry required**. And it has fired on a real
game: `docs/test-games.md` records Solarpunk (UE5.7) resolving through exactly that heuristic
fallback with `FProp Offset +0x44` — which *is* the Step 2.5 default, i.e. the branch that writes two
of five.

The one mechanism that could have harmonised them latches shut on precisely this case:
`Ubel::CorrectSubclassOffsets` only rewrites the other four when `delta != 0`, and here delta is 0
because `FSTRUCTPROP_STRUCT` is already right.

Fixed with a single `DynOff::PropertyFamilyFor` / `PropertyFamilyAtBase` / `ApplyPropertyFamily`
helper, with **all four** writers routed through it. **The fix-time sibling grep found the third and
fourth — the finding named neither**, and the third was coherent-but-hand-rolled, which is exactly
how it and Step 2.5 drifted apart. Deliberately *not* routed: Ubel's ArrayProperty self-heal probe,
which re-probes `FARRAYPROP_INNER` independently because UE5.7 puts `EArrayPropertyFlags` before
`Inner`; that exception is now commented at both ends.

30 new assertions pin the invariant across five plausible `Offset_Internal` values, both shipped
layouts (0x44 → base 0x70; TQ2's 0x48 → 0x74), agreement between the two spellings, and the
historical split asserted as a **shape that must be unreachable**. Controls: reproducing the split in
the helper → **16 red**; dropping the `FEnumProperty` +8 → 6 red.

### G3 — confirmed as a mechanism, and four premises wrong

| filed | measured |
|---|---|
| "for the **seconds** a re-run takes" | **1–3 ms**, all 16 `offsets*.log` on this machine |
| "the DynOff **set**" | **3 globals** — and **15 of 16** runs are byte-identical to the defaults |
| "`UFIELD_NEXT` reset 0x38 → 0x28" | requires UE4 **pre-4.25**; on UE5 it is never written |
| CE `[DISABLE]`/`[ENABLE]` "poller survives" | **false** — `Mimic::StopThread` joins. The real mechanism is restart-*before*-scan |

And reachability is zero: every one of the 16 logs contains exactly **one**
`ValidateAndFixOffsets: Starting`. On the CE path CE's single Lua thread is parked in
`processMessagesPaintOnly` for the entire window — documented as not handling keyboard/mouse — so no
CE-side writer can drive the poller anyway.

**So G3 is a LOW, not a MED, and emphatically not the filed L-effort fix.** Refuted as repairs:
build-then-publish (~200 lines, and naive staging is *wrong* — Step 2.5's defaults are probe **seeds**
read back by the heuristic phase); a reader seqlock (426 `DynOff::` sites, 265 outside Genau.cpp,
mostly inline in 486K-node loops); quiescing consumers (`Tot` means cancel, not pause).

What shipped is a 5-line gate removing the one genuinely exposed case: an `apply_rescan` re-probe
when init already probed.

> ⚠ The predicate is `!bOffsetsProbeRan`, **not** `applied`. `applied` is true on every reachable
> path — `CMD_RESCAN` only scans pointers already 0, and the UI only sends `apply_rescan` when
> something was found — so gating on it would be a **never-firing predicate**, the defect class
> recorded one commit earlier as G11's lesson.

GEngine's second pass is **hoisted out** of that block: it must still run when the offsets were
already probed, which is exactly the GWorld-only recovery the gate now skips, or the fix would trade
G3 for "GEngine reports AOB not found forever".

-----

## 2026-08-17 - G11: Tier 2 matches the bare needle, so it can finally fire (build 3112)

**Tier 2 had never fired on any binary this project owns.** The needle table's trailing `.` is a
*Tier 3* device — it forces a three-component `X.Y.Z` so a bare `5.4` cannot match `15.40`. Applying
it to Tier 2 as well meant Tier 2 could never match UE's own tag, which is **two-component**:
`++UE4+Release-4.27`. That is verbatim the defect `Genau.h`'s rev-2 note records fixing **for Tier 1**
via the dot-strip in `ScanVersionTier1` — Tier 2 never received it.

### Measured, before and after

A faithful model of the header's semantics (same table, bounds, window and anchor rules) run over the
**170 PE images** in the local analyze corpus. Conditions are part of the number: on-disk PE bytes,
whereas the DLL scans the *mapped* image — for unpacked titles the string content is the same.

| | Tier 2 fired |
|---|---|
| before | **0 / 170** |
| after | **6 / 170** |

And on **all six**, Tier 2's answer agrees exactly with the version **Tier 1 independently reports**
(418, 420, 418, 420, 420, 418) — two detectors cross-validating rather than one asserting.

Tier 1 returns first on all six, so **no effective verdict changed on any binary we own**. What is
gained is a Tier 2 that works as a *fallback* for images whose full `++UEx+Release-` tag is stripped
but a `Release-X.Y` fragment survives — exactly the population Tier 2/3 exists for, and none of our
170 is one.

### The two guards that make a shorter match safe

- **Whole-token**: the byte after the bare needle may not be a digit. That admits both real shapes —
  two-component `Release-4.27\0` and three-component `Release-5.4.2`, whose next byte is `.` — while
  rejecting `Release 5.40`, which is a game version, not an engine one.
- **Preceding digit/dot**, hoisted out of Tier 3 so Tier 2 gets it too. Tier 3 always had it; Tier 2
  never did, and needs it far more now that it matches the shorter form (`15.4` and `1.5.4` both
  contain `5.4`).

Tier 3 is untouched — it still demands the trailing dot *and* a digit after it, restated now that the
match above is on the bare form. Two rails assert exactly that.

`kVersionDetectLogicRev` 4 → **5**, mandatory. C++ 1130 → **1137**. Three controls, each reverted
alone: match the full needle again → 1 red; drop the whole-token guard → 1 red; un-hoist the
preceding-digit guard → 3 red.

-----

## 2026-08-17 - G8 + G9: Tier 2's context window, and the Tier 3 retire (build 3105)

Both were reproduced **deliberately** in build 3086 so the needle-gate rewrite could stay
equivalence-preserving. Fixed now, together, because they interact. C++ 1123 → **1130**;
`kVersionDetectLogicRev` 3 → **4** (mandatory under its own rule — tier rules changed).

### G8 — and the obvious repair is wrong in *both* directions

Three things disagreed and the code was the narrowest: the comment said *"within the preceding 16
bytes"*, the buffer was `char ctx[17]`, the `memcpy` copied **8**. The audit's literal patch
(`off >= 16` + `memcpy(…, 16)`) would have shipped two regressions, both measured first:

- **`strstr` stops at the first NUL in the copied bytes.** A neighbouring string's terminator inside
  the *wider* window truncates the search and **loses a match the narrow window found** — so a wider
  `strstr` is not a superset of a narrower one. (12.2% of the 14,823 `[Rr]elease` occurrences across
  the 33-binary corpus have a NUL in the preceding 8 bytes.)
- **`off >= 16` drops offsets 8–15 entirely**, which includes the *canonical* `Release-5.4.0`, whose
  needle sits at offset 8.

So it is a raw byte search over a **clamped** window, which has neither problem.

G8 also **adds the UE-anchor gate Tier 3 always had**. Tier 2 was the loosest predicate in the
system — no anchor, no preceding-digit check — so widening it without one manufactures a confident
`504` out of an ordinary `Release Notes 5.4.0` that the narrow form rejected outright. With the
anchor, the widening is a strict superset on every regression case and still returns nothing on
anchorless noise.

### G9 — the retire defeated the design's own stated promise

A Tier 3 candidate retired its pattern, so a later Tier 2 hit on the **same needle** was never seen.
The deferral comment says the design exists to stop a stray bundled `5.5.0` *"out-racing a real
'Release-4.27' string later in the module"* — that held across patterns and failed within one. Two
facts are now kept per pattern, and a pattern retires only once a Tier 2 is known.

There is now a test asserting that exact promised scenario. **It returned the wrong VERSION before
this commit — 505 instead of 427 — not merely a wrong confidence badge.**

### Measured scope, stated so this is not oversold

**Both fixes are no-ops on all 85 real PE images in the local corpus.** Tier 2 has never fired on any
of them — see **G11**: the needle table's trailing dot means Tier 2 demands a three-component
`Release-5.4.2` while UE's tag is the two-component `++UE4+Release-4.27`. Tier 1 got that dot-strip
at rev 2; Tier 2 never did. That, not the window, is why Tier 2 is dead.

### Controls

The naive oracle moves in the **same** commit (it encoded the old rules and would otherwise go red
for the wrong reason), and 8 new **absolute** cases pin the difference, since equivalence alone
cannot see a change both sides made. Four controls, each reverted alone: window back to 8 → 2 red;
the naive `strstr` repair → 1 red (the NUL case, which is the whole argument); drop the anchor gate →
6 red; restore the G9 retire → 1 red.

⚠ The retire control **passed first time** — my patch's `break` exited the *pattern* loop rather than
the offset loop and retired nothing. A broken control, not a passing one (working-lessons §4.3b).

-----

## 2026-08-17 - G10 + MA1: the hint fast path was destroying working scans, and AOB scanning can now be cancelled (builds 3091 / 3095)

Two commits. **The bigger one was not the finding I set out to fix** — it was found while
re-deriving it, is HIGH, and was live-reproducible from logs already on this machine.

### G10 (HIGH) — the hint fast path used a weaker test than the scan that made the hint

`Genau::ScanForTarget`'s hint phase called `Macht::AOBScan`, which returns the **first** match only.
The batch path it shortcuts hands Pass 1 **every** match and walks them until one validates. So a
pattern whose first match failed validation was logged `Hint MISS` and then **erased from the pattern
set** — hiding it from the one path that would have found the later, valid match.

Same binary (PE `6A7EA60310F17000`), five minutes apart, from `Logs/DumperTest/`:

```
13:29 (cold)  === GNames: 31 patterns tried, 10 with hits, winner: GNAM_V1 -> 0x7FF63CD568C0 ===
13:34 (hint)  [GNames] Hint MISS: 'GNAM_V1' (scan 1545 us) — falling back to full scan
              === GNames: 33 patterns tried, 12 with hits, NONE validated ===
```

GNames not found on a binary where it demonstrably resolves. `GNAM_V1` had **166** matches on that
image and the winner was not the first. GObjects showed the same shape in the same run and went
**0.66 s → 6.14 s**.

Three consequences, in order:

1. **The false "not found" is PERSISTED.** `Flamme` writes `method="not_found"` over a good
   `method="aob"` hint, and `ExtractHint` then refuses anything but `"aob"`. This week's dominant
   defect family — a failed operation memoized as a real answer — already shipping, with no
   cancellation involved.
2. **It oscillates**: fail → hint destroyed → next launch cold-scans successfully → hint saved →
   fail again.
3. **It is the largest contributor to the worst scan time in the corpus.** The 16.3 s `FindAll` that
   made MA1 look urgent is largely this, not `Macht`.

Fix: `AOBScanAll` walked with the **same cap and same first-validated-wins rule as Pass 1**, so the
two paths cannot disagree; `pr.hitCount` reports the real count (it was `matchAddr ? 1 : 0`, so a
166-match pattern logged `hits=1` — the report and the reality computed by different code paths,
which is what hid this); and the pattern is erased **only** when it produced zero matches.

### MA1 — confirmed, narrowed, and my own re-raise had a wrong premise

I wrote *"once control enters Macht, every poll G2 added to Genau is unreachable."* **Wrong.** None
of Genau's 7 polls was ever on `ScanForTarget`'s call path — `Macht` does not shadow them, the AOB
phase simply never had a poll at any level. Impact is also smaller than "larger than G2" implied: CE
gives up at its own 5000 ms ceiling, so the user gets a bounded freeze plus a leaked stub, not a hang.

**The poll alone would have been a net regression**, which is why it shipped as one commit with four
guards. Today nothing aborts, so a scan always completes and writes a real record; add a poll and
`*Method` stays `"not_found"`, `FindAll` still reaches `Flamme::SaveResults`, and the write is
unconditional — one impatient untick destroys the hint cache permanently.

- `ScanReport::cancelled`, recorded **at the bail** and never re-derived by calling
  `Tot::Requested()` again (`Fern::AcceptLoop` resets the per-command flag on firstConn, so a
  reconnect during unwind would make the run look complete).
- `FindSparseDelegateStorage` no longer latches on a cancelled run — `s_sparseDelegatesScanned` has
  three references in the whole tree and **nothing resets it**, so latching a cancelled scan would
  kill sparse drill-down for the game process, surviving a CE Disable/Enable.
- `EnginePointers::bScanCancelled`, OR-ed in the **one** block that already reads all four reports
  rather than at four call sites — C++ has no `required`, and a missed target reintroduces hint
  destruction for that target only.
- `UE5_Init`'s latch guard now also refuses on it. ⚠ **Not** widened to `Tot::Requested()`:
  `g_perCommand` stays latched until firstConn, so a stale flag would refuse the latch on a scan
  that completed fine and permanently disable the DLL for that process.

Polls sit at the **pattern boundary**, deliberately not inside `Macht`: the largest indivisible unit
below is one `AOBScanBatch` (max measured 0.64 s on a 213 MB `.text`) or one `AOBScanAllModules`
(max 2.34 s on a 593-module title), both inside CE's ceiling. `Macht.h` now records that reasoning —
plus the correction that the earlier refutation was wrong for a *stronger* reason than MA1 gave
(there is no `__try` in any scan core at all), and that a future poll there must discard partials,
since `ResolveNameKeyTable` reads the list as a **uniqueness** test.

### Also filed

**MA2 (LOW)** — `ScanRegionBatch` guards on `minPatLen` across the batch but computes
`regionSize - pat.bytes.size()` per pattern; for a pattern longer than the region that `size_t`
subtraction underflows and the loop walks off the end, in a function with no SEH. Unreachable today
(the sole call site passes no `moduleBase`), live the moment one is passed. `AOBScan`/`AOBScanAll`
already carry the correct guard.

-----

## 2026-08-17 - G2: gate the version needle sweep, and let the recovery sweeps be cancelled (builds 3086 / 3088)

**Audit #5 G2 — CONFIRMED, but its framing was wrong.** Filed as a *cancellation* defect; it is a
**cost** defect the file already knew how to fix, and the poll it prescribed would have been both
insufficient and, on the version sweep specifically, dangerous. Two commits. C++ 1094 → **1123**.

### Measured, not estimated

Conditions are part of the number — `Elliot-Win64-Shipping.exe`, 460.0 MiB = 482,344,960 bytes,
MSVC 2022 `/O2` x64:

| loop | naive | gated |
|---|---|---|
| Tier 1 (4 whole-image passes) | 4.348 s | 0.2245 s |
| Tier 2/3 (19 whole-image passes) | 24.465 s · **9,165,424,620** memcmp calls | 0.0957 s · 39,351 |
| `CountPreUE4Markers` | 0.489 s | — |
| **contiguous unpolled stretch** | **~29.3 s** | **~0.35 s** |

The audit's `~9.2e9` reproduces **exactly**; its `~14 s` implied 1.5 ns/iteration against a measured
2.67 ns, so 14 s was a floor nothing reaches. A poll would have left all 29.3 s in place.

> ⚠ **The measurement trap, recorded because it cost a rebuild:** benchmarking with a string
> *literal* needle lets the compiler constant-fold `strlen` and inline the `memcmp`, understating the
> cost by ~5×. The production loop takes `needleLen` from a table at runtime.

### Why the gate is safe rather than merely fast

Every needle begins `'4'` or `'5'` **and** has `'.'` at index 1; both Tier 1 prefixes begin `'+'` in
narrow and UTF-16LE. Skipping an offset whose first byte is not in that set cannot change any
`memcmp` result — exact by construction. `static_assert`s enforce it, so adding a `"6.0."` row
without extending the walked set **fails the build**.

Logic moved to `dll/src/VersionNeedleScan.h` (pure, header-only) for one reason: **no test target
compiles `Genau.cpp`**, so none of this had a build-time check. `dll_helpers_test` now carries naive
references transcribed from the pre-change loops and asserts agreement.

### Version detection stays UNCANCELLABLE — deliberately

Recorded in a block comment so the next session does not "finish G2" by pasting the `(B18)` idiom in.
The verdict is **persisted** by `Flamme` per PE hash and skipped on later launches, so a cancelled
sweep would be memoized as the fallback guess forever — the exact never-invalidated-cache shape this
audit removed from `Ubel` three times over (U4/U16/U6). `CountPreUE4Markers` is worse: a truncated
marker count is not a smaller answer but a **wrong** one, in the direction that refuses a supported
game.

### The three sweeps that DID get polls (build 3088)

`DataScanGObjectsCandidates`, `FindGObjectsStaticStruct`, `FindGNamesByStringRef` — Genau goes from
4 poll sites to 7. Each abort was verified benign **at the caller**: none publishes or memoizes a
not-found result. `Frieren::AutoStartWork` also gains `Tot::ResetPerCommand()`, which is load-bearing
rather than tidy — `Tot::Requested()` is true for the client-disconnect latch too, and that latch is
cleared only *after* this scan, so without it a stale latch would abort a healthy game's recovery at
offset 0.

### Three findings this pass, one of them a correction to the audit itself

- **MA1 (MED, re-raised)** — a prior refutation recorded the `Macht.cpp` AOB family as *"here the
  guards exist"*. **They do not**: `grep -c "Tot::" dll/src/Macht.cpp` = **0**; the `__try/__except`
  blocks are SEH *read* guards, not cancellation. `ScanForTarget`'s Pass 2 feeds every zero-hit
  pattern into `AOBScanAllModules` from five call sites. **That is what G2's word "multi-module"
  actually pointed at**, and once control enters `Macht` every poll added here is unreachable. A
  do-not-re-raise row is where a wrong refutation does the most damage.
- **G8 (LOW)** — Tier 2's context window: the comment says 16 bytes, the buffer is `char ctx[17]`,
  the `memcpy` copies **8**. Fails silently (falls to Tier 3, sets `bLowConfidence`).
- **G9 (LOW)** — the Tier 3 `break` retires a pattern before a later Tier 2 hit for the same needle
  can be seen. Both reproduced deliberately so the rewrite stayed equivalence-preserving.

### Two negative controls PASSED first time, and both were bugs in my tests

Per working-lessons §4.3b. Deleting the `'.'` gate stayed green because the perf buffer had no
`'4'`/`'5'` at all, so the first-byte gate alone satisfied it. Tightening the walk bound stayed green
**twice** — first because the bound-edge needles qualified as neither tier so the difference was
invisible, then because the anchor was planted 1080 bytes from the needle when the window is 256, so
it still qualified as nothing. Both now red (1 and 3).

-----

## 2026-08-17 - AE3: a dedupe key that names what the panel is showing OR loading (build 3068)

**Audit #5 queue ⑥, part 2.** One filed clause **REFUTED**, the rest materially narrowed. 5 new
tests (C# 3902 → **3907**); baseline control reds exactly 3.

`_lastLoadedNodeAddress` was written *before* the awaited load, so a walk that failed left the key
naming a node the panel never showed — and the guard then refused to reload it.

### What the finding got wrong, and it matters

- **REFUTED: "with no way to retry."** Selecting a *different* node overwrites the key and the
  original reloads fine. Only the **same-node** retry was blocked — which is still the defect, since
  clicking the same row again is the natural gesture after an error, but it is not a dead end.
- **NARROWED: it needs a PRIOR SUCCESSFUL LOAD.** The guard was
  `_lastLoadedNodeAddress == node.Address && HasClass`, and `HasClass = true` is the **only**
  assignment to that property in all of `ui/` — a one-way latch. On a cold panel `HasClass` is
  `false`, so the retry already worked. `ColdFailure_WasAlreadyRetryable` asserts exactly this and is
  green before and after; without it a reviewer trims the priming step from the real test and gets a
  silently-green test against broken code.
- **REFUTED, companion prose** (`§2` of the audit): *"the panel binds no ErrorMessage at all …
  completely silent."* `ClassStructPanel.axaml` binds it today, explicitly ungated by `HasClass`
  (audit #5 V7 already fixed that). **Do not re-raise it.**

### The fix

The field is renamed to `_shownNodeAddress` — deliberately, so every diff hunk re-reads the
invariant. The old name *was* the false premise: it is written when a load is **claimed**, so it
names an attempt, and reading it as "last loaded" invites the natural repair (move the write after
the walk) which would break the dedupe it exists for.

New contract: *the node whose class the panel is showing **or is loading**; `null` when the content
did not come from a tree selection, or when the newest tree-driven load failed.* One writer,
`BeginLoad(nodeAddr)`, which also takes the ticket — so "you cannot take a ticket without recording
whose panel this is" is true by construction. Cross-tab loads pass `null`, which is **AE3's third
path** and needs neither a failure nor any concurrency: two ordinary clicks pinned the panel.

`&& HasClass` is dropped. Its only job was keeping a cold failure retryable, which the release now
does for **every** failure. Keeping it preserved two defects: the permanent-`true` latch armed the
pin, and while the *first* walk is in flight it is still `false`, so a `node → null → node` re-fire
(`ApplyFilter` nulls `SelectedNode` on every filter keystroke) issued a **second** walk for the same
class.

### The control found a coupling worth recording

`ColdFailure_WasAlreadyRetryable` was predicted green under every control. It goes **red** under a
*partial* revert that removes the key release while keeping the `&& HasClass` drop — the two changes
are coupled, and that combination never shipped. The honest baseline is reverting the AE3 change as a
whole: exactly 3 red (`FailedWalkAfterPriorSuccess`, `CrossTabLoad`, `DuplicateSelectionDuringFirstWalk`),
with `ColdFailure` and `RepeatedSelectionOfSameNode` green.

-----

## 2026-08-17 - AE2: give the Class/Struct panel one owner per load (build 3067)

**Audit #5 queue ⑥, part 1.** Narrowed from the filed text. 6 new tests on a VM that had **zero**
coverage (C# 3896 → 3902); five negative controls, each reverted alone, each reding exactly its own
test.

Object-Tree selection could leave the panel showing a class that is not the selected node. Nothing
upstream serializes the handler — `ObjectTreeViewModel` raises `SelectionChanged` as a bare
`Action`, so MainWindowViewModel's `async` subscriber returns to the message loop at its first await
— and `AsyncRelayCommand` does not block re-entrancy either (`CanExecute` goes false so a bound
Button self-disables, but `ExecuteAsync` runs anyway; measured build 3038).

### Narrowings

- **Not "any two overlapping selections."** Only **instance-then-class-like** loses, and it loses by
  **ordering**, not timing: the two branches issue a different NUMBER of round-trips (an instance
  needs `get_object` before its walk, a UClass does not) over one strictly FIFO pipe lane, so the
  older gesture's walk is issued third and answered third — deterministic, not a flake. Equal-hop
  pairs settle in order and are safe.
- **"async-void handler" is inaccurate as filed.** `OnObjectSelected` is `async Task`; the async void
  is one level up, at the subscription. The mechanism is unaffected, but the label points a fixer at
  the wrong file.

### The fix, and the part an obvious implementation gets backwards

Reuses the four-guard idiom from `InstanceFinderViewModel.LoadInstanceFieldsAsync` — the one site in
this repo that guards all four points — and the existing counter spelling (`_loadGen` /
`_fieldLoadId` / `_classLoadId`) rather than inventing a fourth.

⚠ **The ticket is claimed at GESTURE time in `OnObjectSelected`, not inside `LoadClassAsync`.**
Claimed in the command it would *invert* the fix: the stale instance selection **enters the command
last**, because its `get_object` hop delays entry, so it would take the **highest** ticket and win
legitimately. Bailing right after `get_object` also means the stale walk is never put on the wire —
saving a hop on a contended lane rather than adding one.

`LoadClassAsync` keeps its exact signature (five cross-tab callers untouched) and delegates to a new
`LoadClassCoreAsync(classAddr, gen)`. The no-op `classAddr` check stays **before** the claim: a
request that does no work must not supersede a live load, or the spinner is left owned by a ticket
nobody retires.

### Prerequisite

`StubDumpService.GetObjectAsync` gains `virtual`. Without it the two-round-trip branch — the whole of
AE2 — is unreachable from a test, and its negative control reports green against broken code.
Additive; the ~13 subclassing test files inherit the unchanged throwing body.

-----

## 2026-08-17 - U6 + F3: witness the name cache on the bytes it decoded (build 3065)

**Audit #5 queue ⑤, part 3 — and the fix is NOT the one the audit prescribed.** Pinned by 7 new
assertions (C++ 1087 → **1094**); negative control 2 red.

`Ubel::GetName` memoizes per `UObject*` — an address the engine recycles — and served every hit
unvalidated. After a level change, every name-bearing response (Object Tree rows, `walk_instance`'s
own/outer name, every ObjectProperty target) returned the **destroyed** object's name for the rest of
the process, while the class was read fresh, so the two disagreed with no error anywhere. Only
restarting the game cleared it. F3's *reconnect* half shipped in build 2819; this is the in-session
half it deferred.

### Why not the prescribed `(InternalIndex, SerialNumber)` witness

The audit called that pair *"the same pair UE itself uses to detect a recycled slot"*. It is not, for
a passive observer: `FUObjectArray::FreeUObjectIndex` sets `SerialNumber = 0`, and
`AllocateSerialNumber` assigns only inside `if (!SerialNumber)` — essentially only from
`FWeakObjectPtr::operator=`. **Most objects carry serial 0 for life**, and the free list is LIFO, so
a stale witness of `(i, 0)` matches the new state `(i, 0)` — silent in exactly the recycle it exists
to catch. It would also have rested on `Aura::GetSerialNumber`, whose own comment marks the packed
`FUObjectItem` layout `*** UNVERIFIED ***`. **Do not re-propose it.**

### What shipped instead

Witness the **input bytes of the decode**, not the identity of the object. The cached string is a
pure function of `{ int32 ComparisonIndex; int32 Number }` at `UObject+0x18`, and FNamePool entries
are append-only for the life of the process — so if those two int32s still read the same, the cached
string is still the correct answer, whoever owns the address now. Total, not heuristic, and *cheaper*
than what it guards: two int32 reads against a pool walk. It also replaces the read `GetName` already
did rather than adding one.

`number` is load-bearing and has its own assertion: dropping it is exactly U8, which shipped once and
rendered `Slot_1`/`Slot_2`/`Slot_3` all as `Slot`. `NAME_None` is `{0,0}` and a **real** name, so a
witness treating zero as "unset" would never serve it from cache — also asserted.

Assign-over-stale is legal here and only here: this cache hands out **copies** (the hit returns by
value with the copy made under the lock), so no reference into it escapes. The two class caches hand
out references, which is why they get an insert-time gate instead (U4, build 3052).

`ClearNameCache` keeps all four call sites but its rationale changed — staleness is no longer one of
them. What is left is bounding growth, and covering a `Serie::Init` re-run, the one event that can
remap a `ComparisonIndex` and so make an unchanged witness decode to a different string.

-----

## 2026-08-17 - U16: a truncated UEnum table was cached forever, and the log said it was full (build 3058)

**New finding, hand-found while fixing U4** — same file, same never-erased-cache shape, in none of
queue ⑤'s four findings. Pinned by 7 new assertions (C++ 1080 → **1087**); two negative controls,
3 red and 1 red.

`ResolveEnumValue`'s entry loop `break`s on a mid-table `Neu::ReadEntry` failure, and the **partial**
vector was then published unconditionally into `s_enumCache`. Nothing in `dll/src` erases that cache,
so **one truncated read permanently splits a single UEnum**: values below the break point resolve,
values above render as raw integers — Live Walker, Property Grid and every CE export — for the rest
of the process, with no retry.

The log actively concealed it. `LOG_DEBUG` printed `layout.count`, the *intended* count, never
`entries.size()`, so a truncated table logged as a full one. That is audit #4's own root cause
verbatim — **the report and the reality computed by different code paths**.

### The fix, and the distinction it turns on

New `Ubel::ShouldPublishEnumTable(buildLayoutOk, intendedCount, readCount)`. The two failure modes
look alike and must **not** be treated alike, in either direction:

- **`BuildLayout` failed → publish.** That is a *complete* answer. `Neu.h:100`/`:116` reject
  `count == 0` / `num <= 0`, so a legitimately member-less UEnum and any address that is not a UEnum
  both land there; refusing to cache them would re-probe on every lookup. (This is also why U4's own
  filed enum claim was correctly refuted — do not re-raise it.)
- **The loop broke mid-table → do not publish.** Answer this call from the partial table, cache
  nothing, let the next call recover.

`GetEnumEntries` now warns when it finds no cached table, because post-fix that means exactly one
thing — a truncated read — and an empty CE DropDownList is otherwise indistinguishable from a
member-less UEnum.

-----

## 2026-08-17 - U4: refuse to memoize a class walk whose identity read failed (build 3052)

**Audit #5 queue ⑤, part 1.** The filed mechanism was **refuted and replaced** — see below. Pinned by
7 new assertions in `dll_helpers_test` (C++ 1073 → **1080**); two independent negative controls,
4 red and 2 red.

### The filed premise was wrong; the real one is worse

U4 said a zero-field `ClassInfo` gets cached "from a transient read failure or a not-yet-`Link`ed
UClass". Both halves are wrong: `Macht::ReadSafe` is an in-process SEH deref that fails only on an
access violation, and `UStruct::Link` *iterates* `ChildProperties` rather than creating it, so a
pre-`Link` walk yields every field with correct names and wrong offsets — never an empty list.

The real mechanism is deterministic and already shipped. `WalkInstance` calls `WalkClass(classAddr)`
(`Ubel.cpp:3561`) and only **then** applies its recycled-object gate `IsSanePropertiesSize`
(`:3576`), whose own comment reads *"an implausible value means classAddr points at recycled
memory"*. **The one code path that knows the address is garbage had already published it
permanently** — the class caches are keyed by a raw `UClass*` and nothing in `dll/src` erases them.
Reachable with no GC at all: `UE5_WalkClassBegin` (`Frieren.cpp:706`) takes a raw address with zero
validation, and `scripts/ue5_dissect.lua:378` uses it as its *is-this-an-instance?* probe — so the
shipped workflow keys `s_walkClassCache` by **instance** addresses by design. `UE5_WalkClassEnd`
clears only Frieren's own local copy.

### The fix

New `Ubel::ShouldPublishClassWalk(propsSizeReadOk, propertiesSize)` beside the `IsSanePropertiesSize`
it reuses — **the predicate already existed; only the read-ok term was missing.** `Macht::ReadSafe`
zeroes its out-param on fault and 0 is a *sane* PropertiesSize, so the value test alone cannot see an
unmapped address; `WalkClass` now carries that discarded `bool`.

Two gates, deliberately different, because the two conditions are not equally trustworthy:

- **Read fault → bail before the field walk.** `USTRUCT_PROPSSIZE` is a small in-object offset, so
  only an unmapped page faults — a verdict that is *offset-independent*, hence safe on a forked
  layout. Skips 4096 bounded-but-real FNamePool lookups down a garbage FField chain.
- **Implausible value → complete the walk, refuse to memoize.** `DynOff::USTRUCT_PROPSSIZE` is
  *derived* (`childPropsOff + 8`, `Genau.cpp:3348/4010/4016`), never independently probed, so a
  pre-walk bail on the value would turn "fields fine, size wrong" into "no fields at all" on a fork.
  Refusing to cache costs only a re-walk.

`WalkClassEx` gets the same gate — it is the more widely consulted cache (Property/Value Search,
snapshot capture, CE export, Solitar, Solide), so fixing only `WalkClass` would close the smaller
half. Placed **before** `CorrectSubclassOffsets` so a garbage class cannot calibrate the
process-wide `FSTRUCTPROP_STRUCT` offset off its own bogus fields.

### Corrected in passing

The B10 comment claimed *"WalkClassEx hands out a `const ClassInfo&` into this map"* about
`s_walkClassCache`. It does not — `WalkClassEx` copies, and both of that map's readers copy under
its mutex. The reference-return belongs to `s_walkClassExCache`. The rule (try_emplace, never
assign) is right for both and both now say why.

### Not fixed here

**U5 stays open** and its row stays unticked: not one byte is freed. Eviction is impossible while
`WalkClassEx` returns `const ClassInfo&` to 25 call sites, several of which re-enter it while
iterating `ci.Fields` on one thread — so the item is "change the return type, THEN bound the cache",
not "add an LRU". Class-to-class recycling (a recycled address whose new occupant has a *sane*
PropertiesSize) is also still open — that needs a layout fingerprint over an append-only arena.

-----

## 2026-08-17 - AA14-AA20: seven fixes on the CE Lua invoke path (build 3039)

**Audit #5 queue ④** — all seven on one path: the mailbox round-trip CE Lua uses to call a UFunction
in the game. Pinned by a new `scripts/tests/invoke_helper_test.lua` (63 checks), the third rig in
`scripts/tests/`. **Written to fail first: 23 failures against the unfixed file.**

### What was wrong

| | |
|---|---|
| **AA14/15** | `allocateMemory`'s nil return was unchecked, so a failed allocation still wrote `ArrayNum = ArrayMax = n+1` beside `Data = 0` — an FString **promising n+1 characters at address 0**, handed to a live UFunction. |
| **AA16** | `BakedScriptGenerator.MapToHelperType` can emit `ftext` / `tarray` / `tmap` / `tset` / `delegate`; `writeParams` accepted **none** of them, so the error aborted the WHOLE invoke before the DLL was ever triggered. |
| **AA17** | The params buffer was zeroed only to the CALLER's `parmsSize`, while the DLL passes `sizeof(paramsData)` — a flat **1024** — to `UE5_CallProcessEventEx`. |
| **AA18** | A timeout reported the STALE `errorMsg` from an earlier command. |
| **AA19** | The reentrancy flag was cleared unconditionally, including on the timeout path — exactly when the DLL still owns the mailbox. |
| **AA20** | `readUFunctionReturn` decoded int32/int16 UNSIGNED, so a UFunction returning `-1` read as `4294967295`. |

### Two things the measurement changed

- **AA14 is worse than filed, and the rig nearly hid it.** CE does *not* raise on a nil address —
  `lua_toaddress` falls through to `lua_tointeger`, and `lua_tointeger(nil)` is `0`, so
  `writeBytes(nil, …)` writes to address **0** and returns. The first version of the rig modelled it
  as a raise, which made three of AA14's five assertions pass for the wrong reason. With a
  CE-accurate stub the real behaviour appears: `ok = true, err = nil` — a **silent success** that
  sends the invoke. The finding described the bytes correctly and the *outcome* not at all.
- **AA16's most likely victim is `tarray`, not `ftext`.** `InvokeParamDialog.CollectBakedValues`
  skips OUT params only when they are STRING types, so the ubiquitous
  `GetAllActorsOfClass(…, TArray<AActor*>& OutActors)` shape is collected and aborts the export —
  a plain getter the user supplied nothing for.

### The repair

- **AA14/15**: raise before *any* of the three field writes. A length is never published for a
  buffer that does not exist.
- **AA16**: `tarray`/`tmap`/`tset`/`delegate` are accepted and **write nothing** — after AA17 the
  whole buffer is zeroed first, and all-zero *is* the default-constructed empty value for each
  (`{Data=nullptr, Num=0, Max=0}`; an unbound `FScriptDelegate`). A value the caller actually
  supplied is refused rather than silently dropped. **`ftext` stays refused, deliberately**: an
  all-zero FText is not an empty FText — it holds a `TSharedRef` the engine dereferences, so a
  zeroed one is a crash, not a default. The error message also listed 11 of the 23 tokens
  `writeParams` really accepts; it now lists them all.
- **AA17**: one `writeBytes` over the full 1024. Also *faster* than before — the old form was one CE
  round trip per byte, so covering the whole region this way beats covering part of it the old way.
  `writeParams` still gets `parmsSize` as its region size, so the `fstruct` size-inference fallback
  is unchanged.
- **AA18**: wipe `errorMsg` before sending. Nothing else does — the DLL's pickup sets
  `status = PROCESSING` and leaves the field alone. Incidental: the existing `or 'timeout'` fallback
  was **unreachable**, because `'' or x` is `''` in Lua.
- **AA19**: the guard is released only when the mailbox is ours again. A timeout records the mailbox
  address, and the next call asks the **DLL's own published state** (`status == DONE && cmd == IDLE`)
  rather than latching a Lua-local boolean for the session.
- **AA20**: `int32` (the default) and `int16` decode signed; `uint32`/`dword` and `uint16`/`word`
  are the unsigned spellings. The docblock and `scripts/README.md` both listed a token set that
  omitted them.

### Negative controls — one per finding, all discriminating

AA14/15 → **7 red** · AA16 → **13** · AA17 → **2** · AA18 → **1** · AA19 → **2** · AA20 → **2**.
All green on restore. C++ 246 + 1073, C# 3896 (the eleven source-text assertions on the embedded
helper still pass — it ships as a manifest resource, not a file).

`scripts/README.md` gains the third rig and, more usefully, a fourth trap for whoever writes the
next one: **a stub stricter than CE hides exactly the defects worth finding.**

-----

## 2026-08-17 - AE4-AE7: the Proxy Deploy panel gets a guard that is actually one (build 3038)

**Audit #5 queue ③.** `IsScanning`'s own declaration already called itself *"the mutual-exclusion
guard"* — it just was not one. Three of the eight long operations **set** it while six **tested**
it, so the guard was one-directional: a scan blocked a deploy, a deploy blocked nothing.

### What re-deriving changed about the fix

Two premises needed narrowing before any code was written, and one new defect turned up in the same
handler:

- **AE6's scope was too wide.** `AsyncRelayCommand` reports `CanExecute == false` while it runs and
  Avalonia's Button gates on that, so a command already could not re-enter **itself** from its own
  button. Measured against the resolved package (8.4.2) with a scratch probe rather than recalled:
  `ExecuteAsync` twice → both bodies entered, but `CanExecute` was `false` throughout. So
  Deploy-vs-Deploy was never reachable from the UI; the gap is strictly **cross-command** — two
  different buttons, plus the paths no button owns (a property-changed handler, a hotkey, a test).
- **AE4's mechanism was "the loser's proxy type wins".** Not derivable: each refresh captures its
  arguments at call time and applies its whole batch in one synchronous pass, so rows never mix.
  The true claim is **last writer wins, with nothing checking that the winner matches the radio.**
- **AE5's "six guards" is right but three of them are well-formed** self-exclusion. Only four
  readers never set it.
- **Found while verifying, in the same handler:** `OnScanDrivesModeChanged` fires
  `LoadDrivesCommand.Execute(null)`, and `ICommand.Execute` does not consult `CanExecute` either.
  `LoadDrivesAsync` sets no flag and its trigger condition (`Drives.Count == 0`) stays true until
  the load *completes*, so toggling the source radio off and on during it starts a second load
  whose `Drives.Clear()` discards the `DetectedDrive` instances the user has already ticked —
  **the drive selection silently resets.**

### The repair

- **One gate, `TryBeginExclusive`, held by all eight operations** via an `IDisposable` scope, so an
  early return or a throw cannot leave the panel wedged — which is how the flag came to be set by
  only three of the operations that test it. `IsScanning` now has exactly **one** writer.
  `ScanAsync` gains an entry guard it never had, which is what let it `Games.Clear()` under a
  running Update All. The refusal message names the operation actually running, instead of always
  saying *"Wait for scan to finish"* when no scan was running.
- **AE4: cancel the superseded refresh, then correct the grid if the radio moved on.** The CTS
  supersede is the idiom `InstanceFinderViewModel:242` already uses. The correction is the half the
  negative control forced: cancellation stops a refresh that is still *computing*, but the service
  writes the grid **after** its cancellable worker returns, so a cancel landing in that window is
  too late and the stale write goes through anyway.
- **AE7: snapshot `Games` before the loop, and catch.** `UpdateAllAsync` had no `catch` at all and
  is an `AsyncRelayCommand`, so a faulted task on the button path is rethrown onto the UI thread —
  a crash risk, not merely a tally that never appeared. Cancelling now reports what *did* get
  updated, because N folders are no longer uniform and the user needs to know.
- **`LoadDrivesAsync` gets a re-entry guard.**

### The negative controls did real work

Four controls, one per finding — and **two of them found bugs in the fix**:

1. **The AE4 control passed**, which meant the test was not pinning the correction at all: the stub
   completed refreshes synchronously, so two were never in flight. Rebuilt so the test parks both
   and releases them in **reverse** order — and the fix then failed, because the guard I had
   written (`!ct.IsCancellationRequested`) blocked the correction on precisely the call that needs
   it: the superseded one, whose token is by definition the cancelled one.
2. **The AE7 snapshot control passed** because the new `catch` masked it. Tightened to assert the
   *success* tally, which is the only wording that means the loop ran to the end.
3. The first gate control **deadlocked the suite** rather than failing — a refused command that
   stops being refused parks on the same test gate. The refusal awaits are now bounded, so a
   regression fails in 10 s with a clear message instead of hanging.

Final: reverting the gate → **3 red**; reverting AE4's correction → **1 red**; reverting AE7's
snapshot → **1 red**. All green on restore. C# **3896** (was 3885), C++ 246 + 1073, AOT publish
clean.

**Not in this change, deliberately:** `DeleteSelectedOrphansAsync` was already well-formed (it sets
`IsRemovingOrphans`, which the gate now tests), and the three other fire-and-forget
`_ = ApplyProxySuggestionsAsync()` sites plus their siblings in `InterestingFunctionsViewModel` and
`RelatedObjectsViewModel` are separate findings — `ApplyProxySuggestionsAsync` also has four
legitimately-awaited callers, so a generation guard there must supersede only the fire-and-forget
path or the awaited ones silently no-op.

-----

## 2026-08-17 - AA4-AA7: ue5_dissect.lua stops reporting failure as success (build 3037)

**Audit #5 queue ②**, and the first fix in this repo written against a Lua test rig that RUNS the
script (`scripts/tests/dissect_test.lua`, 40 checks). The C# suite can only assert on this file's
source TEXT, so every claim below was measured, including the pre-fix behaviour.

### Two of the four premises were wrong, and one was wrong dangerously

These were MED rows carrying the audit's own *"not re-derived by hand"* caveat. Re-deriving them
first was not ceremony:

- **AA4's premise is REFUTED.** It asserted, as "CE source-verified", that a bare `getAddress`
  raises on a missing symbol and that `ue5_dissect.lua:54`'s `if fn == nil or fn == 0` is therefore
  dead code. `TSymhandler.getAddressFromNameL` gates its raise on `ExceptionOnLuaLookup`
  (`symbolhandler.pas:5082`) and `TSymhandler.create` sets that **FALSE** (`:6688`) — nothing in
  CE's Pascal ever sets it true. **`getAddress` returns 0, and that guard is the only thing turning
  "the DLL was never injected" into a message naming the export.** Acting on the finding would have
  deleted it. CE's own `celua.txt` claims the opposite default; that contradiction is now
  [CE-Bugs-Minesweeper.md](CE-Bugs-Minesweeper.md) §6.
- **AA4's *consequence* is real, and is the actual defect.** A Lua error inside a registered dissect
  callback does not stay in Lua: `TLuaCaller.StructureDissectEvent` re-raises it as a **Pascal
  exception** (`LuaCaller.pas:1229-1232`) into a dispatch loop with no handler
  (`StructuresFrm2.pas:1451-1458`), which skips the next line — CE's own
  `autoGuessStruct` fallback (`:1460`). Nothing auto-unregisters the callback and CE never rebuilds
  its Lua state, so **one raise breaks Structure Dissect for every address, UObject or not, for the
  rest of the session.**
- **AA5/AA6 are real and worse than filed.** The audit said a failed field read is recorded as "a
  duplicate of the previous field". Measured: that is the *mild* case. With the DLL failing
  outright, `pcall` returned **ok=true** and the run built a 45-element structure whose every walked
  field had an empty name and offset 0, **registered it with CE, cached it, and logged "Struct
  created"**. A total failure was reported to the user as a successfully built structure.
- The audit's "14 call sites" is **19**, and the first `nil` comparison to blow up is
  `createFromClass:362`, not the `:164`/`:173` the findings cite — the instance-detection probe runs
  before the walk is ever entered.

### The repair

`callDLL` now has one contract: **it returns the DLL's value or it RAISES. It never returns nil, and
no caller needs a nil check.** The two ways Lua treats `nil` were both wrong and in opposite
directions — `count <= 0` raises with a Lua type error naming a line instead of the failed call,
while `success ~= 0` is **true** for nil, so a failed read counted as a success and re-read the
previous field's buffers (`UE5_WalkClassGetField` leaves its out-params untouched on failure,
`Frieren.cpp:712-731`).

Raising is only safe because of the other half, and the two must not be separated:

- **Both CE-registered callbacks are now barriered** (`callbackBarrier`). A failure declines —
  `false`/`nil`, the contract that lets CE fall through to its own dissect — and warns **once per
  session**, because CE calls these per expanded node.
- **`createFromClass` unwinds a failed build**: `endUpdate()` always runs, the partial structure is
  destroyed, and nothing is registered or cached. Previously `beginUpdate()` had no match and the
  orphan was never in `structList`, so `clearAll()` could not free it either — survivable while a
  failed call returned nil, but this fix makes raising the *normal* failure path, so leaving it
  would have shipped a regression (audit #5 AA25, promoted from latent by this change).
- The per-call `warn()` is gone: it fired once per FIELD, so a dead DLL printed **40 ungated lines**
  over CE's Lua Engine window — the exact thing CLAUDE.md's hygiene rule exists to prevent.

### Negative controls (two, one per half)

Reverting `callDLL`'s raise to the old `warn()` → **9 checks red**, including *"NOTHING was
registered with CE"* and *"at most one line printed, not one per field"* (40). Reverting the
callback barrier alone → **2 checks red**. Both green on restore. Against the unfixed file the rig
reported **13 failures**, so the defects are reproduced, not argued.

C++ 246 + 1073, C# 3885 (the source-text assertions in `CeExecuteCodeExArityTests.cs` still pass —
`local ret, why = executeCodeEx(` and the `DLL_CALL_TIMEOUT_MS` shape are unchanged),
`luac -p` clean.

**Checked and clean — do not re-raise:** the `.CT`'s sibling `ue5_callDLL`
(`scripts/UE5CEDumper.CT:448`) also returns nil on failure, but both of its call sites (`:619`,
`:781`) test `== nil` explicitly.

### AA7 — `fillGaps` deleted (maintainer's call)

Defined, advertised in three places (this file's header, `scripts/README.md`,
`scripts/DEPLOY_README.html`), and **never called from anywhere in the repo**. It was also wrong:
the coverage set was built from element START offsets only, so an 8-byte field at `0x10` marked
only `0x10` — `0x14` read as uncovered and the loop would have emitted a `vtPointer` **overlapping
a real field**. On a real class that is hundreds of unnamed rows, some of them overlaps.

Deleted rather than repaired. CE's own `autoGuessStruct` already covers the no-override case, and a
correct version would need to track each field's real byte span while elements are added — new,
untested, visible behaviour in a shipped script that nobody asked for. The three doc claims are
gone with it, and the removal comment records the shape to build if it ever returns (the DLL hands
us `f.size` per field, so the coverage set must never be re-derived from CE's element list).

-----

## 2026-08-17 - A6: Force holds the class AND its subclasses (build 3036)

**Audit #5 A6 — unparked by a maintainer decision, not by new evidence.** The finding was confirmed
and deliberately left unfixed because the repair was a product choice with no right default. The
maintainer chose **option (a)**: the defining class **plus every subclass**, and asked for the
Stealth Meter to move to the same semantics.

### The defect

Property Search reports the **defining** class for an inherited property — `Aura` sets
`match.className = definingName` so dedup can collapse ~4,800 `AActor` subclasses into one row
badged *"inherited by 4822"* instead of listing them. `Solide::ApplyJobLocked` then resolved the
pool with `FindInstancesByClass(name, exactMatch=true)`, so forcing an inherited field looked up
instances of `Actor` **itself** and found essentially none. The failure was at least honest —
*"0 live instances of Actor matched — nothing held."* — which is why it was a capability gap rather
than a corruption.

`exactMatch=false` was never the answer: it is a case-insensitive **substring** match on the class
NAME, so `"Actor"` would capture everything with "actor" anywhere in its name and still miss every
subclass that does not contain the word.

### The repair

New `Aura::FindInstancesDerivedFrom(baseClassName, maxResults)` — a **derivation** test that walks
the UClass super chain, with a **per-UClass verdict cache** so the walk is paid once per distinct
class rather than once per object (GObjects holds 10^5–10^6 objects over 10^3–10^4 classes, which
is what makes "AActor and everything under it" affordable). `Solide::ApplyJobLocked` calls it.
`"Enemy"` still excludes `"EnemyProjectile"` — the reason the old exact match existed — because
derivation is not substring.

**The non-obvious half: the CDO skip had to move INSIDE the walk, before the cap.** A base like
`AActor` is the ancestor of thousands of classes, every one contributing a `Default__` object, and
CDOs are constructed at class-load time so they occupy the LOW GObjects indices this walk reaches
first. Filtering them in the caller — which is what `Solide` did — would have handed it 256
class-default rows and not one live instance, turning the fix into a different zero. `Solide`'s own
skip stayed as the local invariant (`ApplyToInstance` must never write a class default).

`ApplyToInstance` already resolved the field against each instance's **own** class
(`Ubel::FindField(cls, …)`), so an inherited field resolves correctly across the subclasses and an
instance that genuinely lacks it is simply not counted as held.

**Stealth Meter is covered by the same change** — it goes through `ForceFieldAsync` → `force_field`
→ `AddForce` → `ApplyJobLocked`. It resolves a concrete class, so the semantics are additive there,
but it is the shipped, in-game-verified path this deliberately altered and is registered as a
regression check.

The cap (`SOLIDE_MAX_INSTANCES` = 256) is now reached routinely rather than exceptionally, so
`truncated` is load-bearing UI: both status lines already said it and still do.

### What is NOT in this change

- `ResolveLocalPC` and the other `FindInstancesByClass("PlayerController", false, …)` callers keep
  their substring match — a different concern (resolve ONE object, not a pool) in modules with their
  own verified behaviour. `Hemmung::ResolveWorldSettings` is the one worth revisiting: its comment
  calls the substring match "subclass-tolerant", which `FindInstancesDerivedFrom` would now do
  properly. **Left alone deliberately; not silently widened.**

### Negative control

The DLL half **cannot be unit-tested** — no test target compiles `Aura.cpp` or `Solide.cpp`
(`dll_helpers_test` compiles `Radar.cpp` + `Denken.cpp` only), so it is registered for live
verification with an explicit regression half. The UI half IS pinned: a new
`Force_zero_held_says_the_pool_included_subclasses` test; reverting the status string to the
class-only wording fails exactly that test (1 of 3885) and passes again when restored. C++ 246 +
1073, C# 3885, `-Target DLL` clean.

-----

## 2026-08-17 - AB3+AB5: the vector scan learns UE5's LWC width (build 3035)

**Audit #5 queue ①.** The first entry of §3b's ordered fix queue, and the highest-user-impact MED
left: *every UE5 game's FVector/FRotator value scan compared junk.*

### The defect

UE5's Large World Coordinates made the default `FVector` / `FRotator` **3 doubles (24 bytes)** while
the explicit float variants (`FVector3f` / `Rotator3f`) stayed **3 floats (12 bytes)**. The scan
accepted both spellings by NAME — `Radar::VectorStructNames` lists `"Vector"` and `"Vector3f"` in
one table — and then read a flat **12 bytes as three floats** at every stage:

- `Radar::SizeOf` answered a constant `12` for all three vector DataTypes (`Radar.cpp:27`).
- `Radar::CompareVectorPredicate` decoded `float c[3], a[3], b[3]` (`Radar.cpp:797`).
- `FormatVectorBytes12` rendered the same 12 bytes as floats — so the display and the compare were
  wrong *together*, which is why this never looked like a bug in the value column.
- Every read site in `Aura.cpp` passed a literal `12`.

On a 24-byte field that reads the low four bytes of X plus the low four bytes of Y as one "float" —
a bit pattern that can never equal what the user typed. The scan returned zero plausible hits and
looked like "the value isn't a UPROPERTY".

**The width was already in hand and thrown away.** `ScanField::size` captured the property's
reflected `ElementSize` at index-build time and was **written seven times and read nowhere**.

### The repair

The width is now read per FIELD from the property's own reflected size — which is the rule
[teleport-spec.md](teleport-spec.md) §5.3 already fixed for this repo (*"Never key this off a
version number"*) and which `Wirbel`/`Laufen`/`Dunste`/`Schlacht` each already applied locally.
A version-keyed fix would have been wrong anyway: **one UE5 game holds fields of both widths.**

- `Radar::SizeOf` returns **0** for the vector types — the same "variable" signal the string and
  multi-numeric families already use. A single constant cannot be true here, and pretending
  otherwise is what the defect was.
- New shared, tested statement of the rule: `Radar::IsSupportedVectorWidth` /
  `DecodeVectorBytes` / `StoreVectorCanonical` + `VECTOR_WIDTH_FLOAT` / `_DOUBLE` / `_CANON_BYTES`.
- `CompareVectorPredicate` now takes **decoded triples** (`const double*`) rather than raw bytes.
  Deliberate: the three buffers no longer share a width (a 24-byte field vs a target typed as text),
  so a byte-level predicate would need a width per argument and would silently mis-read if one drifted.
- One **canonical in-memory form**: 3 doubles. `Candidate::prevValue` (16 → 24 bytes) and every
  vector target buffer hold it, so the renderer, the server-side filter/sort, the refine compare and
  the wire can never disagree about the source width again.
- `FieldDescriptor::vectorWidth` carries the source width into the session, because **refine
  structurally cannot re-derive it** — `fieldType` is the bare string `"StructProperty"` for every
  vector (the multi-numeric family's re-resolve trick has no vector equivalent).
- The index builder resolves the width from the property that actually holds the triple, which is a
  *different* field per container shape: a leaf uses its own `ElementSize`; `TArray` its inner
  element size (`size` there is the 16-byte header); `TSet` its element size, **not** `elemStride`
  (the sparse-array slot is padded by hash bookkeeping); `TMap` the `keySize`/`valueSize` half;
  `TOptional` the *wrapped* type's size (`f.Size` includes the trailing `bIsSet` byte).
- **AB3's real repair**: a reflected size that is neither 12 nor 24 is now **refused** rather than
  read at a guessed width — that is what keeps an `FVector2D`/`FVector4`/game-namesake out of the
  candidate set. `"Vector"` stays accepted on both engines, because the name gate no longer implies
  a width.
- `ParseVectorBytes` parses through `std::stod` into the canonical form. Incidental: `std::stof`
  was already rounding a large coordinate before it reached the compare.

**Found while fixing** (the fix-time sibling grep): `GroupSlotValueString` and its sibling lambda
copied `sizeof(tmp.prevValue)` bytes out of a `GroupSlotMatch` — destination-bounded. Benign while
both arrays were 16, an **8-byte out-of-bounds read** the moment `Candidate` grew. Now
source-bounded with a `static_assert`.

### Negative control

`DecodeVectorBytes` reverted to the pre-fix "always three floats": **7 assertions red**, including
`LWC field Exact-matches the typed target`. Restored → green. C++ **246** + **1073** (was 1042;
+31), C# suite green, `-Target DLL` clean.

### Not verified on a game

This needs a **UE5** title and cannot be checked on UE4 — five steps are registered in
[todo.md](todo.md) `## Pending live-game verification`. **No wire or UI change**: vector targets and
values cross the pipe as text, so the width is entirely a DLL-side fact.

-----

## 2026-08-16 - Fourteen audit-#5 MEDs, fixed in cluster order (builds 3016-3031)

**Audit #5, MED tier.** The three named families were already closed, so this run followed §4's
*"clusters, in the order worth fixing"* instead of picking by segment. Fourteen findings, fourteen
commits, each with its own negative control. The open MED tier went **69 → 55**.

### What was fixed

| ID | Where | The defect |
|----|-------|-----------|
| **G1** | `Genau.cpp` | `bOffsetsValidated = true` stored unconditionally on the success tail, though four in-path probes give up and keep a version guess. `get_offsets` reported `{"validated": true, "fallback_reason": ""}` over unmeasured offsets. |
| **A5** | `Aura.cpp` | Property Search's Preview sampled the **CDO**, so the column showed the Blueprint default forever (Health = 100 while the player is at 37). |
| **U7** | `Ubel.cpp` | `s.resize(50)` split a UTF-8 sequence; nlohmann's strict `dump()` then threw and the ENTIRE `search_properties` response became `{"error":...}`. |
| **U8** | `Ubel.cpp` | Three open-coded FName decoders dropped `FName::Number`, so `Slot_1/2/3` all rendered as `Slot` — disagreeing with `ReadFNameAt` on the same 8 bytes. |
| **V7** | 6 × `*.axaml` | `ViewModelBase.SetError` wrote `ErrorMessage` and **six panels never bound it**. A dead Live Walker refresh — 10 s timeout included — was pixel-identical to a live one. |
| **V6** | `LiveWalkerViewModel.cs` | Refresh kept the field-search keyword but installed new row objects and never re-ran the matcher: highlights vanished while the count kept advertising N matches. |
| **X3** | `DumpService` + Pointers | The DLL's offset-validation verdict had **zero clients** — the string `get_offsets` did not appear in `ui/` at all. |
| **AB6** | `Radar.cpp` | Group-scan sort keys read `slotMatches[0][0]` while the row displays `slotMatches[0][picks[0]]` — the grid ordered by a leaf the user cannot see. |
| **AF1** | `Neu.h` | UEnum member count range-checked AFTER a signed cast, so the whole upper `uint32` half passed and `out.count` came back negative. |
| **AF6** | `PropertySearchPanel` | A REFUSED force value was reported as a cancel; and `double.TryParse` silently rounds a wide `Int64Property`, so Force would hold a number the user never typed. |
| **AE9** | `ValueSearchViewModel.cs` | New Scan reset the private sort key but not the bound picker, and re-selecting the option the combo already shows is a no-op. |
| **AE8** | 4 sites | `DiagnosticsProbe` opened BEFORE validation, filing measurements for operations that never ran — into the dataset the probe exists to collect. |
| **AF2** | `DetectStatsViewModel.cs` | Detect stops live-probing after 30 classes, and past the cap every signal is false for a DIFFERENT reason than on a rejected row — both rendered "· guess", so a real stat at rank 31 looked disproven. |
| **AF4** | `LiveWalkerPanel.axaml.cs` | All six VM callbacks were torn down on visual-tree detach and only ever re-subscribed from `DataContextChanged` — which a tab switch does not raise, so one round trip silently killed every scroll-to and the bookmark view restore. |

### Two things worth carrying forward

**G1 → X3 is a pair, and the order mattered.** G1 made `bOffsetsValidated` mean what `Grimoire.h:243`
says it means; X3 then gave that verdict its first client (an amber Pointers banner). Wiring X3
first would have rendered a banner driven by a flag that was itself lying.

**The fix-time sibling grep paid off in EIGHT of the fourteen** (and cleared two more: AF4's shape exists in exactly one view, and AE8's fourth site was already correct). V7 named one panel and six were
unbound; AE8 named one probe site and four had the shape; AE9, G1, U8 and AF6 each grew a second
site the finding never listed. In no case did the finding's own text mention the sibling.

### Deliberately NOT fixed

**A6** was re-derived and CONFIRMED — including that `PropertySearchViewModel`'s doc comment
asserted the opposite of what the code does (that comment is corrected). But the repair is a
product decision: Force on the defining class **plus every subclass** (semantically right, changes
the pool the shipped Stealth Meter card writes to) or on the one most-derived subclass observed
(arbitrary). Today's failure is at least honest — *"0 live instances of Actor matched"* — so it is a
capability gap, not a corruption. Parked for the maintainer.

One **new lead** was opened by U8's sibling grep and is the larger half of it: ~19 sites read an
instance's `FName` and drop `Number`, so the Instance Finder renders every instance of a class under
one name — and its name gate substring-matches against that same truncated string. Mechanism
verified; blast radius not measured. Full block in the audit doc.

### Verification

Every fix was proved able to fail by reverting it in the tree and watching the suite go red, then
restored. Every UI/binding change additionally ran `-Mode Publish` (Native AOT, trimmed) per
CLAUDE.md. Test counts moved **246/1029 → 246/1042** (C++) and **3847 → 3884** (C#).

**AF4 is deliberately NOT unit-tested** — the defect lives in Avalonia's visual-tree lifecycle, and
a test driving the private handlers would assert my model of when Avalonia raises them rather than
the behaviour. It is a live-verification step instead.

⚠ **Nothing here is verified on a running game.** See todo.md's pending-verification section.

-----

## 2026-08-15 - CE XML told CE to dereference slots that hold no pointer (build 2966)

**Audit #5 U2 finding W5.** First MED after both named families closed.

### The defect

`EmitDrilledPointer`'s scalar branch gated on `IsObjectPropertyType` — the whole pointer *family* —
and emitted `Offsets=[0]`, which instructs CE to dereference the 8 bytes at `+Offset`. Three of that
family hold no address at all:

| type | what is actually in the slot |
|---|---|
| `WeakObjectProperty` | `FWeakObjectPtr { int32 ObjectIndex; int32 SerialNumber; }` — two ints |
| `SoftObjectProperty` / `SoftClassProperty` | `FSoftObjectPath` — a string-ish struct |
| `LazyObjectProperty` | `FGuid` — four ints |

**What made it reachable and invisible is the same fact.** The DLL resolves every one of those to a
live `UObject*` and stamps it on `PtrAddress`, so the branch's `TryGetValue(field.PtrAddress, …)`
guard *succeeded* — a target genuinely had been resolved. It simply is not what lives in the slot.
The exported table then looks entirely healthy: a group header, a plausible class name, children at
plausible offsets — and CE follows an index+serial pair as if it were an address.

### The fix

A new `IsRawObjectPtrSlot` beside the existing predicates, gating the drill on "the field's own 8
bytes hold a `UObject*`". Weak/Soft/Lazy fall through to the 8-byte hex leaf they already had —
watchable, and honest about being a raw slot rather than a followed pointer.

**The correct distinction already existed 1,660 lines up in the same file** as
`IsRawObjectPtrArrayInner`, with a comment justifying each exclusion, because the ARRAY path had been
written correctly and given a regression test. Same file, same question, one path right — the audit's
double-check pass had already pinned that line, which is what made this a short fix.

**`InterfaceProperty` is deliberately INCLUDED** where the array predicate excludes it, and the
asymmetry is real: `FScriptInterface` is `{ UObject* +0x00, void* +0x08 }` — stated by the DLL at
`Ubel::IsInterfaceArrayType` — so its first 8 bytes *are* an object pointer and the scalar drill has
always been correct for it. It is absent from the array predicate because a `TScriptInterface`
element is **16 bytes** and gets its own DLL reader, not because it is not a pointer. Copying the
array predicate verbatim would have silently removed a working case. Both predicates now
cross-reference each other and state why they differ.

### Verified

3824 → **3831** tests, 0 failures; restoring the broad gate fails 4 (one per non-pointer type).

New tests were required rather than optional: the double-check had already established that the
look-alike test (`…EmitsLeafWith8BytesNotGroupHeader`) passes **no `resolvedInstances`**, so it never
entered the drill branch — the branch had no coverage at all.

-----

## 2026-08-15 - Root cause #4, closed: a struct-layout guard that a Service could not reach, and a GWorld gate that disabled a working button (build 2961)

**Audit #5 findings AC2 + AE10** — the second and last named family. Both were *the audit's own
earlier fixes applied at only some of their sites*.

### AC2 — a predicate that guarded a table, but did not live with the table

Y7 added `ResolveTrustedLayout`: use the hardcoded struct layout **only when it agrees with the size
the engine reported**, because the layout is a guess keyed on a *detected* UE version (LWC turns
FVector's floats into doubles and doubles every offset) while the engine's size is ground truth.

It was written as a private helper inside `InvokeParamDialog` — **a View**. The two other consumers
live in `StructReturnDecoder` — **a Service**, which cannot depend on a View. So the guard could not
spread even in principle, and the invoke dialog **refused a size-contradicted layout for its INPUT
boxes while accepting the very same layout for the RESULT grid**. Four call sites, one guarded.

Fixed by moving the rule to `KnownStructLayouts.GetTrustedLayout`, beside the table it guards, with
all four sites routed through it (`ResolveTrustedLayout` keeps its name and delegates, so callers and
tests do not churn). **A predicate that guards a table belongs with the table** — had Y7 put it there,
AC2 could not have existed.

### AE10 — a cheap proxy signal in front of a predicate the DLL already computes

Locate-in-GWorld was gated on the client-side `IsGWorldAvailable` flag at **19 sites across 7
ViewModels** (14 C# + 5 XAML `IsEnabled` bindings). Value Search had been decoupled from it; nothing
else was.

**The flag is not what its name says.** It comes from `EngineState.HasGWorld`, whose definition is
`GWorldAddr` non-empty and non-zero — i.e. *"the AOB scan produced a &GWorld **slot address**"*, not
*"a live UWorld exists"*. The DLL has world-recovery fallbacks that work when that scan did not, so
the gate **disabled the button on games where locate worked fine** (TQ2, proxy mode). Meanwhile
`find_path_from_gworld` returns an explicit invalid/no-path status when there really is no live
UWorld, and the locate flow surfaces it. This is audit #4's recorded root cause verbatim: *a cheap
proxy signal substituted for a predicate a sibling in this repo already computes correctly*.

All 19 gates removed. Then the flag itself **deleted from 9 ViewModels** — removing only the gates
would have left it write-only in nine places, which is the dead-flag shape this audit already flagged
in `LiveWalkerViewModel`, and a flag nobody reads is an invitation to re-gate on it. Deleting it makes
the mistake unavailable. `EngineState.HasGWorld` remains for anything that wants to *display* GWorld
status; what must not return is gating an **action** on it.

Each edit is easy to check because in every one of those files the **engine-rooted counterpart sits a
few lines below and was already un-gated**, with a comment explaining why — so each fix makes the
GWorld command match its own sibling.

### Two existing tests were pinning the defects — one of them argued for it

`CanDecode_KnownStruct_ReturnsTrue` asserted that a **12-byte** FVector param decodes with the
**24-byte** UE5 layout. `LocateResultInGWorld_RaisesEvent_OnlyWhenGWorldAvailable` asserted the gate.
And `InterestingPropertiesViewModelTests` carried a written justification: *"a property is a
class-level definition, so without a resolved GWorld there is nothing to locate against."*

That argument is sound in its conclusion and wrong in its premise — which is why it was checked
against `EngineState.HasGWorld`'s definition rather than overridden. **This is how both findings
survived: the sibling got fixed, and a green test kept saying the other site was fine.** When a fix
turns out to be under-applied, expect its siblings to have tests defending them, and read those tests
as evidence about the *belief*, not about the code.

All three were rewritten into regression tests that state the reasoning.

### Verified

3820 → **3824** tests, 0 failures. Negative control, one break at a time, each revert compiling:
the command guard restored → 2 failures; the `Can*` property gate restored → 1.

**Not verified in-game.** The payoff case is precisely a game where the AOB scan does not resolve
&GWorld but locate still works (TQ2, proxy mode) — the button should now be live there. Nothing here
can prove that without one.

-----

## 2026-08-15 - The width family, closed: out-of-range values are refused instead of silently masked (build 2950)

**Audit #5 T1c finding AE1**, plus the two double-check leads that survived re-derivation and one
sibling the tests exposed. First MED-tier batch, and it takes the width family from five open
occurrences to **one — the parked Y16**.

### The family

Nine occurrences across five subsystems, all the same mistake: **an out-of-range value masked down to
the field width, and the untruncated number reported as if it had been written.** W6, Y2, Y9 and Y15
were fixed in earlier builds; this closes the rest.

**AE1** — `FieldValueConverter.TryConvertEnum` wrote `(byte)(rawValue & 0xFF)` and returned success,
so typing `9999` for a 1-byte enum put **15** in the game while LiveWalker's status line said
`Written: Field = 9999`. Every sibling converter in that same file already refused out-of-range and
named the range (`TryConvertByte` → *"Invalid byte (range: 0 to 255)"*); the enum path was the one
that never adopted the idiom.

**The predicate now exists once**, as `FieldValueConverter.FitsInWidth(value, sizeBytes)` — five
hand-written range checks are five things that drift apart, which is how this family got to nine.
It is deliberately **signedness-tolerant**: N bytes accept the union `[-2^(8N-1), 2^(8N)-1]`, because
the engine reports a width but not always a signedness and both readings are things users
legitimately type (`-1` into a byte means `0xFF` — Y5's rule, which must not regress; `255` into a
signed byte is that bit pattern from the other direction). The union still catches what every
finding here was about: a value that fits **neither** reading.

### The three leads from the double-check, re-derived by hand first

They were single-agent finds with no skeptic pass, so §2's ~50% base rate applied and none was
treated as a finding until re-derived:

| lead | verdict |
|---|---|
| `InvokeParamDialog.DecodeParamValue` reads a 1-byte enum as 4 | **CONFIRMED** — `"EnumProperty"` sat in the `"IntProperty"` group behind `available >= 4`, so a 1-byte enum **mid-buffer** returned its own byte plus three belonging to the next param, while the same enum at the buffer's END decoded correctly (the guard failed and it fell through to the size switch). That asymmetry is what identified it. This is the READ side of the mistake Y2 fixed on the write side of the same file. Fixed with a shared `DecodeBySize`, the mirror of `WriteBySize`. |
| `SdkExportService` enum width vs the layout cursor | **REFUTED — do not re-raise.** The premise needs `InferEnumUnderlyingType` and the layout cursor to meet, and they never do: `GenerateEnumDefinition` / `InferEnumUnderlyingType` have **zero production callers** (grep across `ui/` returns only `SdkExportServiceTests`). The class layout goes through `MapCppType`, which emits the enum's NAME, not a width. |
| `ParamBufferBuilder` FIRE path masks with no validation | **CONFIRMED, and its caveat was the whole story.** `WriteBySize` masks at every width, so 9999 into a 1-byte param fired 15 silently. The masks are Y5's fix and are **kept**; the repair is a signedness-aware range check in front of them (`ParamBufferBuilder.TryValidateScalar`), surfaced through the dialog's existing red result label, refusing the invoke rather than calling a UFunction with one silently-wrong argument. |

### A new sibling, found by a test rather than by a finder — root cause #4's eighth occurrence

`ParseULong("-1")` returns **0**, because `ulong.TryParse` rejects the sign. So typing `-1` for a
`UInt16Property` / `UInt64Property` / pointer param fired **0** at the live game while *Copy AA
Script* baked `0xFFFF…` — one dialog, two opposite calls. **That is exactly the defect Y5 fixed**,
and Y5 fixed it in `ParseByteOrSByte` only, leaving every unsigned path with the original bug.

It surfaced because a new test asserts `EffectiveIntWidth` against how many bytes `WriteParam`
*actually touches* — i.e. it was caught by testing the **seam**, not the helper. That test also
exists to pin the two together permanently: `EffectiveIntWidth`'s table mirrors `WriteParam`'s
switch, which is a drift risk by construction.

### Verified by negative control — and the control was wrong the first time

3751 → **3820** tests, 0 failures. Each fix then reverted **alone**: AE1 → 6 failures, the enum-read
revert → 3, the FIRE range check → 8, `ParseULong` → 3.

⚠ **The first control run reported two of those as detected when nothing had been detected.** Those
two reverts did not compile, so no test ran, and the harness's `'error CS' in out` fallback counted
the compile error as a catch. A compile error is **inconclusive**, not detection. The reverts were
redone so they build, and the harness now says INCONCLUSIVE for that case. Worth carrying: *a
negative control needs a revert that BUILDS, or it measures the compiler instead of the tests* — and
it is the same defect class as AD1, in the tool being used to verify the fix for it.

-----

## 2026-08-15 - Cheat Engine freed the injection stub out from under our still-running scan, crashing the GAME (build 2932) — AUDIT #5 HAS NO OPEN HIGHs LEFT

**Audit #5 phase T1a, finding AB2** — the eleventh and last HIGH. Every HIGH this audit raised is now
fixed (AB1 2913 · AD1/AD2 2914 · AA1 2922 · AA2/AA3 2926 · AB2 2932).

### The defect

The CE plugin's *"Inject & Connect"* called `InjectDLL(dllPath, "UE5_AutoStart")`, so CE's remote
thread ran our **entire multi-second startup** — DllMain + `Sein::Init` + a 2-8 s AOB scan + pipe
start. CE does not wait that long. From its own source (read at tag `7.5`, 2026-08-15):

| `CEFuncProc.pas` | what it does |
|---|---|
| `:1346-1360` | `createremotethread`, then a wait loop of `counter := 10000 div 10` × 10 ms — a **hard, unconfigurable 10 s** ceiling, after which it raises |
| `:1332-1343` | with Settings' **`cbInjectDLLWithAPC`** ticked there is no wait at all: `CreateRemoteAPC` then a flat `sleep(1000)` |
| `:1379-1387` | `finally ... virtualfreeex(processhandle, injectionlocation, 0, MEM_RELEASE)` — **unconditional, on both paths** |

So the page our code is executing on gets released while it runs, and the eventual `ret` lands on
freed memory. **The victim is the game process, not CE** — and AB1's module pin does not help, because
that protects our image in our address space whereas this is CE's own `VirtualAllocEx` page in the
game. (CE's timeout string even claims *"Injection routine not freed"*, which that `finally`
contradicts.) The same shape reached us a second way: generated CE Lua calls
`callDLL('UE5_AutoStart')` → `executeCodeEx`, whose timeout path sets `dontfree := true` and leaks the
stub permanently (ce-plugin-sdk-notes.md §13).

### The fix

`UE5_AutoStart` **spawns and returns**. The work moved into `AutoStartWork()`, reached by two entry
points sharing a one-at-a-time latch:

- `UE5_AutoStart()` — exported, spawns a guarded thread, returns immediately.
- `UE5_AutoStartBlocking()` — **not exported**, runs inline, for `DllMain`'s auto-start thread which
  already owns a thread of its own.

Readiness was already published through `Mimic::InitState` and every emitted script already polls it
(`CeReadinessLua::AppendPollLoop`), so no caller lost information. Worth noting: the **Lua inject path
never passed a function name to `injectDLL` at all** — only our own plugin was taking the dangerous
route.

Two details the fix had to get right. **In a Cheat Engine host it runs inline, no thread** (AB1's
rule — CE `FreeLibrary`s plugin DLLs; and there is no remote stub to outrun in-process). And **the
latch is load-bearing, not theoretical**: in the ordinary plugin flow `LoadLibrary` spawns DllMain's
auto-start thread *and* CE's stub then calls the export, two full scans that were previously survived
only incidentally via `UE5_Init`'s `s_initialized` latch and a pipe-exists probe.

### A second defect, in the half the audit only called "misleading"

`ce_InjectDLL` (`pluginexports.pas:622-640`) returns false **only if an exception escapes**, and CE's
own handler swallows one of the three it can raise (`CEFuncProc.pas:1050-1051`, `1391-1396`):

| CE outcome | Exception class | `InjectDLL` returns |
|---|---|---|
| injection thread > 10 s | plain `Exception` | **false** |
| "Failed executing the function of the dll" | `EInjectDLLFunctionFailure` — a **sibling** of `EInjectError`, not a subclass, so `on e:EInjectError` misses it | **false** |
| "Failed injecting the DLL" | `EInjectError` → caught, falls back to `forceLoadModule` | **true** |

So the BOOL is **true on a real injection failure** and **false while the DLL is loaded and working**.
Our dialog was not merely worded badly — it was reading an inverted signal, and cheerfully told users
to check that the target is 64-bit when the DLL was in fact loaded and scanning.

`OnInjectAndConnect` now **decides by looking**: it re-runs the same target module-list walk it
already uses for the already-loaded check (with a short retry, since the APC path does not wait on the
loader) and reports what is actually mapped, naming CE's unreliable result when the two disagree.

### Verified by measurement + negative control

This is a behaviour, not a shape, and **no test target compiles `Frieren.cpp`**. So
[tools/probe_autostart_async.py](../tools/probe_autostart_async.py) (stdlib-only) loads the shipped
DLL, times the export, and reads `InitState` at the instant it returns:

| build | elapsed until return | `initState` at return | verdict |
|---|---|---|---|
| fixed | **2.3 ms** (1.0 ms on a re-run) | 0 IDLE — work not started | ASYNC CONFIRMED |
| spawn reverted | **3486.5 ms** | 2 READY — work already finished | STILL BLOCKING |

That 3.5 s is in a **Python host with no game at all**; a real UE AOB scan is 2-8 s, i.e. squarely
inside CE's 10 s ceiling and always past the APC path's 1 s. The export table was checked against the
**shipped artifact** rather than the source (64 exports; `UE5_AutoStart` present,
`UE5_AutoStartBlocking` correctly absent).

The probe is a manual tool, not a build step: it loads the DLL into the running process, starts real
workers and opens the pipe, so it wants a throwaway process — and a build step that skips when its
precondition is missing is the AD1 defect this session opened with.

**Not verified against a real Cheat Engine + game.** The measurement proves the export returns in
time; it cannot prove CE is happy. Folded into the existing AB1 CE-session entry in
[todo.md](todo.md)'s register, including the `cbInjectDLLWithAPC` case, which is the
near-certain-crash path and therefore the strongest single check.

-----

## 2026-08-15 - A running freeze could write into a recycled UObject slot, and a dead mailbox never stopped it (build 2926; mailbox contract 1 -> 2)

**Audit #5 segment S1, findings AA2 + AA3** — one defect at two sites, and the last two HIGHs in the
freeze helper. Only **AB2** remains open at HIGH.

### The defect

`ue5_freeze_helper.lua` caches instance pointers from `CMD_LIST_INSTANCES` and writes to them on a
CE `TTimer` (~16 writes/sec per address — `Interval = 50` quantised by the ~15.6 ms scheduler tick;
the file's own "20x/sec" comment was never measured), re-enumerating every 5 s.

**AA2:** between two rescans, UE can destroy an instance and the allocator can hand the same address
to something else. The only guard was:

```lua
local vt = readQword(addr)          -- "is the vtable slot non-zero?"
if vt and vt ~= 0 then ... end
```

which a recycled block passes trivially — a pooled free block keeps old bytes or an allocator
free-list link in qword 0, both non-zero. In practice it caught only fully decommitted pages, i.e.
the game exiting. So the freeze could sit there writing its value into an unrelated live object at an
offset that means something completely different there.

**AA3:** a failed rescan kept the previous cache and tried again in 5 s. Right for a transient
`mailbox busy`; wrong for the failures that never self-heal — DLL unloaded or re-injected so
`g_invokeMailbox` no longer resolves, a contract mismatch after a DLL update, a wedged
`_ue5_invoke_busy`. Those wrote into an unrefreshed cache indefinitely. And `_lastError` had **three
writers and zero readers repo-wide**, so no failure ever reached anyone.

### The fix, and why it is not the one the audit proposed

The register said this needs an identity witness (`InternalIndex`, `SerialNumber`) so the tick can
ask *"is this still the object I enumerated?"*. Re-deriving it against what the feature actually
promises says that is the wrong question:

- The freeze is **class-wide by design** — it locks a property on *all* live instances of a class and
  picks up newly spawned ones each rescan. A slot recycled by **another instance of the same class**
  is therefore not a hazard; it is a target. A serial-number check would refuse that write, i.e.
  refuse to do the feature's job for up to 5 s.
- The write that actually corrupts is into an object of a **different class**.

So the witness that matters is **class membership** — and it is far cheaper, because one `UClass*`
and one offset are constant across the whole enumeration. They ride in two previously-unused
**output** fields (`instanceAddr`, `ufuncAddr`) rather than widening every 8-byte entry, so the entry
stride, page size and 128-per-page cap are all unchanged. That makes the change **additive**:
`MAILBOX_CONTRACT` 1 → **2**, `MAILBOX_CONTRACT_MIN` stays **1**, so every `.CT` saved against
contract 1 keeps working.

| Change | Where |
|---|---|
| Publish `instanceAddr` = enumerated `UClass*`, `ufuncAddr` = `OFF_UOBJECT_CLASS` | [Mimic.cpp](../dll/src/Mimic.cpp) `HandleListInstances` |
| Contract 1 → 2 + why it is additive | [Mimic.h](../dll/src/Mimic.h), `CeMailboxLayout.cs`, `check_mailbox_contract.py` |
| tick re-reads `ClassPrivate`, refuses a foreign class | [ue5_freeze_helper.lua](../scripts/ue5_freeze_helper.lua) |
| Bounded failure streak (3) → drop the cache, stop writing, print once | same, `rescan()` |
| `handle.lastError()` / `handle.isAbandoned()` | same |

Both fields are **cleared before use**: an earlier command may have left a real `UObject*` /
`UFunction*` there and a caller must never mistake that for a witness. The Lua additionally
range-checks the offset (`8 ≤ off ≤ 0x200`) so a leftover 64-bit address cannot masquerade as one.
Getting this wrong fails *closed* — every write refused, i.e. a freeze that silently does nothing —
which is exactly why it is checked on both sides.

`check_mailbox_contract.py` refused the bump at first with *"MAILBOX_CONTRACT is 2 but the surface is
unchanged"*, and that is the gate working: its hash covers field **layout**, not field **meaning**, so
a command that starts using a field it never touched is invisible to it. The golden version was moved
deliberately with a comment recording that blind spot.

### The verification: the helper is now EXECUTED, not just grepped

A Lua 5.4 interpreter turned out to be available, so
**[scripts/tests/freeze_helper_test.lua](../scripts/tests/freeze_helper_test.lua)** stubs the CE
globals the helper touches (memory reads/writes, timers, symbol lookup) over a plain table, runs the
real `freezeProperty` / `tick` / `rescan`, and asserts on **what was actually written**. It is the
first executable test of any script in S1 — the segment the audit flagged as having none — and it
covers the AA1 bool fix from the previous build as well.

**23 checks, 0 failures.** Then each fix was reverted **one at a time**: AA1 → 4 failures, AA2 → 11,
AA3 → 6. All three detected. The first attempt broke all three at once and the output was
*uninterpretable* (AA2's break made an AA1 case fail for an unrelated reason) — one break at a time
is the only version that proves anything. That run also caught the harness aborting on its first
failure and hiding every later case; it now uses safe accessors.

**Deliberately NOT wired into `build.ps1` or CI.** `lua` is not a declared dependency of this repo,
and a test step that silently skips when its tool is missing is precisely the defect AD1/AD2 fixed
one build earlier. Three C# tests are the CI tripwire instead — they cannot prove the guard works,
only that nobody deleted it, and one pins the helper's own `UE5_SCRIPT_CONTRACT` to
`CeMailboxLayout.ContractVersion` so the hand-maintained copy cannot drift. 3748 → **3751**.

**Residual, stated plainly:** a slot freed and *not* yet reused can keep its old class pointer, so a
write can still land in dead memory. Nothing cheap sees that. What is removed is the write into a
*live object of another class*.

**Not verified in-game** — needs a class whose instances die and respawn with a freeze active. Filed
in [todo.md](todo.md)'s `## Pending live-game verification`.

-----

## 2026-08-15 - Freezing a packed bitfield bool stamped the whole byte and wiped its 7 siblings (build 2922)

**Audit #5 segment S1, finding AA1** — the fourth site of the dropped-field family behind W6, Y2,
Y9 and Y15, and the second one in the freeze pipeline in three builds.

### The defect

UE stores an `FBoolProperty` two ways: a **native bool** owning one whole byte, or a **packed
bitfield** (`uint8 bFoo:1`) where up to eight bools share a byte and each owns one bit named by the
property's `FieldMask`. `ue5_freeze_helper.lua`'s `writeBool` did:

```lua
writeByte(addr, (v == true or v == 1) and 1 or 0)
```

unconditionally. On a packed bool that is wrong twice over, ~16 times a second for as long as the
freeze is enabled:

1. **The seven siblings are wiped.** Writing `1` sets the byte to `0x01` — every other bool in it
   goes to false.
2. **The intended bool is never set** whenever its mask is not `0x01`. Writing `1` sets *bit 0*. So a
   bool at bit 2 stayed false while its neighbours were destroyed — the feature silently did nothing
   *and* corrupted the field around it. There is no error channel, so the user sees unrelated flags
   flipping and goes hunting in the game's logic.

The code's own comment stated the defect as a design limitation — *"We do NOT support packed bitfield
bools … generating a freeze script for one will overwrite the whole byte, clobbering sibling bools"* —
which is exactly why it survived. Audit #5's most reliable technique is grepping for a comment that
admits a limitation and then checking whether it is still acceptable; this is its sixth hit.

Meanwhile the DLL sibling reachable from the **same Property Search row** (Solide's Force ON/OFF, via
`Solitar::ApplyBoolBit`) does a masked read-modify-write correctly, and is exhaustively unit-tested.
One row, two actions, opposite correctness.

### The fix — a five-tier wire-through, not a Lua edit

The engine reported the `FieldMask` all along; every tier dropped it, so the mask had to be added
end-to-end:

| Tier | Change |
|---|---|
| DLL wire | [Fern.cpp](../dll/src/Fern.cpp) — both `search_properties` encoders emit `bool_mask` when non-zero (single-query **and** batch) |
| Model | `PropertySearchMatch.BoolFieldMask` + both `DumpService` parsers |
| Row | `ScoredPropertyRow.BoolFieldMask` — forwarded exactly like `PropSize` one line above |
| Params | `FreezeScriptParams.BoolFieldMask`, **`required`** |
| Script | [FreezeScriptGenerator.cs](../ui/UE5DumpUI/Services/FreezeScriptGenerator.cs) emits `boolMask = 0xNN` into CFG only for a genuinely packed bool |
| Lua | [ue5_freeze_helper.lua](../scripts/ue5_freeze_helper.lua) `writeBool(addr, v, mask)` does a masked read-modify-write; `tick` passes `cfg.boolMask`. Helper version 1.1 |

**No `ByteOffset` is needed, and that is verified rather than assumed.** The DLL sets
`boolFieldMask` only after reading `FieldSize == 1` (`Ubel.cpp:662` and `:1044` — both check it), and
a one-byte property has nowhere for a `ByteOffset` to point. So a row carrying a mask always has its
bit in the byte at `prop_offset`. Consistently, `PropertyMatch::boolByteOffset` is **declared and
never assigned anywhere in the tree** — dead, and now known to be harmlessly so.

**`0xFF` is not a bit mask.** UE's `SetBoolSize` writes `FieldMask = 255` for a native bool, so both
`0` (no mask reported, including from a pre-2922 DLL) and `0xFF` fall back to the whole-byte write.
The accept set is exactly `{0x01,0x02,0x04,0x08,0x10,0x20,0x40,0x80}`, encoded identically in C#
(`IsPackedBoolMask`) and Lua (`BOOL_BIT_MASKS`).

**Reused three existing correct implementations rather than inventing a fourth rule.** The bit rule
already lived in `Solitar::ApplyBoolBit` (C++), `FieldValueConverter.ApplyBoolMask` (C#, the Live
Walker edit path) and `UE5T_setbit` (Lua, the standalone trainer). The last also supplied the idiom:
**CE's Lua has no `bAnd`/`bOr`/`bNot`**, so the helper uses pure arithmetic
(`math.floor(b / mask) % 2`), which is version-proof and writes only on drift. `readByte` /
`writeByte` were checked against `celua.txt` before use, per CLAUDE.md.

**A failed read must not fall through.** If `readByte` returns nil the tick returns without writing —
falling through to the whole-byte write is the corruption the branch exists to prevent.

**`required` immediately earned its keep.** The build failed on `InterestingPropertiesViewModel`
because `ScoredPropertyRow` forwards `PropSize` but not the mask — the same dropped-field shape, one
row further down the same file. Left optional, that call site would have silently kept the bug on the
batch-CT path. This is Y15's own repair template working as intended.

### Verified by negative control

24 new tests, **3724 → 3748**, 0 failures. Then both halves of the fix were reverted — the CFG
emission and the mask argument in `tick` — and the packed-mask theories **failed**, naming the exact
eight masks; restored, green again.

Also asserted: a native bool emits **no** `boolMask`; `0xFF`, multi-bit and negative values emit
none; a non-bool type never emits one even carrying a stale mask; and the helper source no longer
contains *"We do NOT support packed bitfield bools"*.

**Not yet verified in-game** — the DLL→UI half (does a real packed bool's mask arrive on the
`search_properties` wire?) needs a running game. Tracked in
[todo.md](todo.md)'s `## Pending live-game verification`.

-----

## 2026-08-15 - A C++ test that failed to COMPILE was reported as "skip", and the build exited 0 (build 2914)

**Audit #5 phase T1b, findings AD1 + AD2** — the *meta* one, fixed first on purpose: while it stood,
every other fix's "tests green" was one compile error away from meaning nothing.

### The defect

The test phase derived its pass/fail signal from **"did an `.exe` path get assigned"**, not from
**"did the build succeed"**. Three unrelated outcomes therefore collapsed into one line:

```
Write-Info "dll_helpers_test.exe not available (skip — run -Target DLL or All first)"
```

`$exitCode` was never touched, so the script printed `Status: SUCCESS` and **exited 0**. The three
outcomes were: the target **failed to compile**; the build succeeded but **no `.exe` was found**;
and the **build dir was absent**. Only the third is benign, and even it isn't under `-Target Test`.

Rename a symbol, update the DLL caller and not the test, and the five shipping targets build clean,
`dll_helpers_test` fails to compile, and **~700 assertions across Radar / Orden / GraphPath / Lineal /
Denken / Solitar / Solide / Macht stop executing with no signal** — including the memory-corruption
class `Test_Solitar_ApplyBoolBit` exists to catch. CI inherited all of it: `ci.yml:142` and
`release.yml:70` are single `build.ps1` steps that assert nothing beyond its exit code.

Worse, the message actively misdirects. *"not available (skip — run -Target DLL or All first)"* tells
the operator they forgot a prerequisite, when in fact they ran the right target and a compile error
scrolled past earlier in the same transcript. Two in-repo comments asserted the opposite contract in
writing (`dll/CMakeLists.txt:503-504`, `dll/tests/utf8_helpers_test.cpp:8-9`) — both describe the
**run** path; the **build** path had no failure channel at all.

### The fix — one helper, not two edits

The finding asked for the same edit at two sites. Two hand-copied blocks *are* audit #5's root
cause #4 ("a fix applied at only some of its sites"), so instead both were collapsed into a single
`Invoke-CppSelfTest` helper in [build.ps1](../build.ps1), beside `Invoke-CmdInVsEnv`. Adding a third
C++ test target can no longer add a third copy of the logic. The call sites are now two lines of
`if (-not (Invoke-CppSelfTest …)) { $exitCode = 1 }`.

Each of the three outcomes now has its own `Write-Fail` and fails the build. **The benign-skip arm
was removed, not narrowed** — under `-Target Test` / `All` the C++ suite is expected to build and
run, so no absence of it is benign.

**A sibling was found and fixed at fix time** — the rule the audit says keeps being read at scan time
and not applied at fix time. The C# phase's `if (-not (Test-Path $TEST_PROJ)) { Write-Info "Test
project not found, skipping" }` is the same defect class: the csproj is checked into the repo, so its
absence means a broken tree, and it left a green exit code with **zero C# tests run**. Now
`Write-Fail` + `$exitCode = 1`.

**PowerShell trap worth remembering:** moving the runner into a function changes what
`& $exe.FullName` does — a native command's stdout joins the *pipeline*, so the test's own output
would have been captured into the function's return value and every call would have read as truthy.
It is piped to `Out-Host`; `$LASTEXITCODE` is unaffected. (The `Write-*` helpers were checked first —
all use `Write-Host`, so they don't pollute the return.)

### Verified by negative control, not by inspection

Mandatory here: the finding *is* "a check that cannot fail", so a green run proves nothing on its own.

1. **Positive control** — `-Target Test` on the clean tree: `[OK] utf8_helpers_test passed`,
   `[OK] dll_helpers_test passed` (1029 assertions), **3724** C# passed, `Status: SUCCESS`, exit **0**.
2. **Negative control** — a deliberate syntax error appended to `dll/tests/utf8_helpers_test.cpp`:
   `[FAIL] utf8_helpers_test FAILED TO COMPILE - the C++ suite did not run (this is a build failure,
   not a skip)`, `Status: FAILED`, exit **1**. The pre-fix code emitted `Write-Info "…(skip…)"` and
   exit **0** in exactly this state. `dll_helpers_test` still ran and passed in the same invocation —
   one broken target must not hide the other's result.
3. Source restored, re-ran: green, exit 0.

No workflow change was needed — CI already gates on `build.ps1`'s exit code. That was the problem:
the script was reporting success.

-----

## 2026-08-15 - Freezing a 1-byte enum wrote four bytes and destroyed its three neighbours (build 2904)

**Audit #5 segment U4 finding Y15** — the third site of a family this audit has now closed three
times.

### The defect

`FreezeScriptGenerator.MapToHelperType` mapped **`EnumProperty` → `int32` unconditionally**. The
freeze helper's `int32` writer is `writeInteger` — four bytes. UE's dominant enum shape is
`enum class E : uint8`, **one** byte. So freezing one wrote over the three bytes that follow it,
20 times a second, for as long as the freeze stayed enabled — and there is no error channel, so the
user sees three unrelated fields quietly changing and goes looking for the game's logic.

The mapping's own comment admitted the gap: *"if a future game has a 1-byte enum we'd want to
surface the size and pick uint8 instead. Out of v1 scope."* The engine had been reporting the real
width all along (`PropertySearchMatch.PropSize`, on the wire since the DLL emits `prop_size`) —
`FreezeScriptParams` simply carried no size field, so the generator **could not** know better. The
finding is the dropped field, not the guess.

### The fix is the one W6 and Y2 already established

Let the engine-reported size overrule the type-name guess. `HelperTypeForSize(int)` is the direct
sibling of `CeXmlExportService.CeWidthForSize` — 1 → `uint8`, 2 → `uint16`, 4 → `int32`,
8 → `int64`, anything else → the legacy `int32` default. `MapToHelperType` gained a
`(typeName, size)` overload; only `EnumProperty` consults the size, because it is the only type
whose width its name does not fix, and a test asserts every other type **ignores** the argument so a
bogus size from the wire can never turn a float into a byte.

Signedness follows what C++ actually produces at each width — `enum class : uint8`/`: uint16` are
unsigned, while a plain 4-byte `enum` has a signed `int` underlying type — and it only affects which
values the dialog accepts: the helper's signed and unsigned writers of one width are literally the
same function. 4 stays `int32`, which is also the only width the old mapping was ever right about,
so nothing that worked before changes.

### `PropertySize` is `required` on purpose

Making it optional would let the next call site silently re-create the bug. `required` makes the
compiler ask the question at every construction, which is the same reasoning as Y2's *"sharing the
implementation is the only repair that does not depend on a future editor remembering"* — here the
enforcement is the type system rather than a shared helper.

### Two new test seams, because the call sites had none

The mapping was already testable; the two places that *use* it were not. `FreezeValueDialog`'s
constructor needs an Avalonia runtime, and `PropertySearchViewModel`'s freeze command needs the
AOBMaker bridge plus a modal. So the width could have been dropped at either call site with **zero
test failures**. Extracted `FreezeValueDialog.HelperTypeFor(match)` and
`PropertySearchViewModel.BuildFreezeParams(match, literal)` — both `internal static`, matching the
existing `BuildRowsFromSelection` precedent — and added the end-to-end assertion that matters: **the
type the dialog validates the user's input against is the type the generated script writes with.**
That pairing is this audit's recurring root cause (audit 4a: *the report and the reality are
computed by different code paths*), and it is now pinned rather than assumed.

### Negative controls — four, each isolating one hop

Each was applied alone and the suite re-run:

| Control | Reverted | Red | Shape |
|---|---|---|---|
| A | the mapping → flat `"int32"` | **17** | every enum test at widths 1/2/8, across all four test files |
| B | dialog drops the size | **3** | only the chain test, only where the two sides now disagree |
| C | single-row params drop the size | **3** | same three |
| D | batch-CT params drop the size | **4** | the batch chain test at every width |

Control A's green half is the point: sizes **4 and 0 stayed passing** (`int32` *is* correct there)
and every non-enum type stayed passing, so the control demonstrates the tests are sensitive to the
defect and not merely to the edit. **B and C would have produced zero failures without the new
seams** — that is exactly the regression class the extraction was for.

3724 tests, 0 failed (3674 → 3724). `dist` is the 54.4 MB AOT-trimmed binary, launch-verified
(window up, clean `init-0.log`, no `crash.log`).

### Found while fixing it, recorded not fixed: Y16

`InvokeScriptGenerator.GetMailboxWriteStatement` (`:558`) groups `EnumProperty` with
`IntProperty`/`UInt32Property` → `writeInteger`, so the **interactive CE invoke form** writes 4 bytes
for a 1-byte enum param and clobbers the next param in the buffer. It is the same defect Y2 fixed in
`ParamBufferBuilder` (the FIRE path) surviving in a third path, and the repair is one line — the
method already takes `p.Size` and already has a `size switch` fallback that handles 1/2/8 correctly;
`EnumProperty` just short-circuits past it. Filed rather than folded in: it is the invoke subsystem,
with its own helper Lua and its own tests, and it deserves its own control.

⬜ **Not verified in-game.** The widths are unit-verified against the helper's writer table, but
nobody has frozen a real `enum class : uint8` and confirmed its neighbours survive. Queued in
[todo.md](todo.md#pending-live-game-verification-verify-only--no-code).

-----

## 2026-08-15 - We were crashing Cheat Engine, and the guard that would have stopped it had one call site (build 2913)

**Audit #5 phase T1a, finding AB1 — the audit's most consequential.**

### What we were doing to CE

`DllMain(DLL_PROCESS_ATTACH)` started a **1 ms-poll thread unconditionally** — `Heiter.cpp:274`
`Mimic::StartThread();`, whose comment reads *"Runs in both proxy and inject modes"* — plus the
auto-start thread. Neither was conditional on the host process, and **Cheat Engine loads this DLL as
a plugin and then unloads it**:

* `Settings → Plugins → Add` does LoadLibrary → `CEPlugin_GetVersion` → **FreeLibrary**
  (cheat-engine 7.5 `plugin.pas:1497 / :1522 / :1525`), so the refcount hits 0 microseconds after
  `DllMain` returns;
* **every CE exit** does `FormClose → pluginhandler.free → UnloadPlugin → FreeLibrary`
  (`plugin.pas:1417`, `MainUnit.pas:7832`) — and that runs **before** CE writes its settings, so the
  crash also loses the user's CE configuration for that session.

A thread that returns from `Sleep(1)` into an unmapped image is an access violation on a thread with
no handler. We were taking down the user's primary tool.

### Three things had to be simultaneously true, and they were

1. **The guard existed and was applied at the wrong place.** `IsCheatEngineExeName` had **exactly one
   call site in the entire DLL** — inside `AutoStartBody`, i.e. *inside the thread it should have
   prevented from being created*.
2. **`DLL_PROCESS_DETACH` could not rescue it.** `Heiter.cpp:190` declared
   `DllMain(HMODULE, DWORD, LPVOID /*reserved*/)` — **the parameter is commented out**, so DETACH
   structurally cannot distinguish a `FreeLibrary` unload from process exit. Its own comment claimed
   *"Only the implicit process-exit DETACH is a no-op"*, a distinction the signature makes impossible
   to draw.
3. **A comment asserted the case away.** `Fern.cpp:533-537`: *"The only case this gives up on is
   FreeLibrary of this DLL with the process still alive … **Nothing in this repo does that** … and
   Heiter.cpp's no-op DETACH already relies on the same fact."* Two modules resting on one unverified
   premise — and **this repo's own mirrored doc already recorded the load/free cycle** at
   `docs/ce-plugin-api-reference.md:95-102`.

### The fix

Two small guards, in `DllMain` where the decision belongs.

**Do not create threads in a CE host.** The host executable path was *already* read at `Heiter.cpp:239`
for logging, so the decision costs no new syscall and no new loader-lock exposure. The CE-plugin entry
points still work — they inject into the **game**, which is where the poller belongs.

**Pin the module when we do start threads.** `GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_PIN | …)`,
so a `FreeLibrary` can never unmap an image our threads live in. Deliberately **not** done in the CE
branch: with no threads running there is nothing to protect, and CE should stay free to unload us
cleanly. `grep GET_MODULE_HANDLE_EX_FLAG_PIN dll/src/` previously returned **0**.

**Threads are still not joined from DETACH** — the existing comment is right that that deadlocks under
the loader lock.

### The test is the durable part

`DllMain` is unreachable from any test, so a test of the guard alone would have proved nothing about
the call site — which is exactly how this shipped. The decision moved into
`Grimoire::HostAllowsBackgroundThreads(const wchar_t* hostExePath)`, a pure header-only function
taking **the full path as `GetModuleFileNameW(nullptr, …)` hands it over**, so the tested unit is the
one `DllMain` actually calls. It fails **open**: an unreadable path returns `true`, preserving shipped
behaviour for every non-CE host rather than silently disabling the DLL.

12 assertions: every CE variant as a full path (including `cheatengine-x86_64-SSE4-AVX2.exe`, the
build variant that defeated the original exact-name list), forward and mixed separators, a bare leaf
with no directory, and the other direction — a real game, **Cheat Engine in the DIRECTORY name with a
game as the leaf**, `MyCheatEngineClone.exe`, and both fail-open cases.

**Negative control:** reverting `HostAllowsBackgroundThreads` to `return true` (the pre-fix behaviour)
reds **exactly the 6 CE-refusal assertions** — `Pass: 1023  Fail: 6` — while all 6 allow/fail-open
assertions stay green, because those assert what the broken version does. Sensitive to the defect
rather than to the edit. Restored: **81 + 1029 C++ assertions, 3724 C# tests, 0 failed.** `dist` is
the 54.4 MB AOT-trimmed binary, launch-verified, no `crash.log`.

⚠ **Not verified against a real Cheat Engine.** The unload paths are read out of CE's published
7.5 source; nobody has yet installed the plugin into CE 7.7 and watched it survive
`Settings → Plugins → Add` and a clean exit. Queued in
[todo.md](todo.md#pending-live-game-verification-verify-only--no-code).

-----

## 2026-08-15 - The freeze dialog took 9999 for a byte and the game got 15 (build 2895)

**Audit #5 segment U4 finding Y9.**

### Why the dialog is the only place this can be caught

Everything downstream narrows in silence *and reports success*.
`ue5_freeze_helper.lua`'s byte writer is `writeByte(addr, math.floor(v) % 256)`;
`Solide::WriteNumeric` is `static_cast<uint8_t>(llround(value))`. Neither has a channel to say
"that did not fit" — the freeze is a background timer and the force-hold is a re-assert worker.
So `9999` on a `ByteProperty` became `15` in the game with nothing on screen, and the user ends up
debugging the game instead of the input.

### The check was asking the wrong question

`ValidateAndConvert` had only ever asked *does this fit a `long`/`ulong`*, which is the wrong
question for seven of its eight integer types. It now checks the **width**: `IntegerRange` is the
inclusive per-type table, and the error names the range *and* the value that would have landed —
*"uint8 holds 0 to 255 — 9999 would be written as 15"*. `WrapToRange` computes that number with the
same modular arithmetic the writers perform, and a test cross-checks it, so the number we quote
cannot drift from the number that lands.

Two more sites of the same defect were inside the same method: **`float` was validated as a
`double`**. The existing `NaN`/`Infinity` guard (B23) passed `1e300` straight through, and CE's
`writeFloat` / Solide's `WriteFloatAt` narrow it to `+inf`; `1e-300` collapses to `0`. Both are now
rejected for `float` and still accepted for `double`, asserted in both directions.

### The pre-fill is inseparable from the check

`SuggestedDefault` returned a flat `"9999"` for every integer type. Adding the range check alone
would have opened every `ByteProperty` dialog holding a value its own OK button rejects. It is now
derived from the same `IntegerRange` table (`min(9999, Max)`), and a test asserts every helper
type's suggestion survives `ValidateAndConvert` — a property that cannot drift, rather than a list
that can.

**One check covers two features**: `PropertySearchPanel.PromptForceValueAsync` reuses this dialog
for Solide's Force value precisely for its per-type validation, so Force gained the same guard.

**Negative controls, three, each isolating one claim.** Deleting the two integer range checks reds
exactly the 11 width tests, and the boundary-acceptance tests stay green — an inclusive bound must
not become an off-by-one that costs the top value of every field. Deleting the float narrowing check
reds exactly the 3 float tests. Reverting `SuggestedDefault` reds exactly the 4 default tests and
**only for `int8`/`uint8`**, which is the predicted shape since 9999 fits every wider type; that
control is also what demonstrates the interaction above. 3674 tests, 0 failed. `dist` is the 54.4 MB
AOT-trimmed binary, launch-verified, no `crash.log`.

**Found while fixing it, recorded not fixed:** `FreezeScriptGenerator.MapToHelperType` maps
`EnumProperty` → `int32` unconditionally, so freezing an `enum class : uint8` writes 4 bytes over 3
neighbours. That is the *third* site of a family this audit has already closed twice (W6 in the CE
XML export, Y2 in the invoke param buffer), and `FreezeScriptParams` carries no size for the
generator to consult — filed as **Y15**, M/low.

⬜ **Not verified in-game** — nobody has typed 9999 into a real `ByteProperty` freeze and watched the
new error appear instead of a 15.

-----

## 2026-08-15 - "Class not found" about the class you just clicked on (build 2888)

**Audit #5 segment U3 finding X2**, plus two twins the finding did not cite.

### The lookup should never have existed

Interesting Funcs and Console build their rows from `list_all_functions`, which supplies
`class_addr` **per row**. The three handlers that need a class address to call `walk_functions`
threw that away, issued `list_classes`, searched the returned page by NAME, and bailed with
`"Class {className} not found"` — about the class whose own row the user had just clicked.

`list_classes` returns at most `limit` rows (5,000) and `Aura::ListClasses` stops walking GObjects
the moment it has them, so on a large title a perfectly real class is simply not in the page. The
three events now carry `classAddr` and the common path issues **no pipe call at all** — it is both
the correct fix and one fewer full-GObjects walk per button click.

### The finding cited one site; there were three

`Console.RequestParameterInvoke` (the FIRE dialog for any exec taking parameters) and
`Console.RequestCopyBakedScript` are byte-for-byte the same body as the cited
`InterestingFunctions.RequestCopyBakedScript`, with the same message and the same hard `return`.
That is the fourth time this audit has found its own defect shape half-covered — §3b's
"grep for its siblings before closing a fix" rule is what caught it.

### The cap is detected at the source now

Per D5/F4's own lesson, the DLL is the only side that knows whether the walk reached the end.
`Aura::ClassListResult` gained `truncated` (set exactly as `SearchResultSet::truncated` is), and
`list_classes` emits it. The C# falls back to inferring it from a full page, so a **pre-2888 DLL
still produces the honest message** instead of silently degrading to "not found".

`ClassListResult.FindClassAddr` is now a pure, unit-testable lookup returning `ClassAddrLookup`.
Its `MissReason` is *"not in the class list — it was CAPPED at 5,000 rows, so the class may still
exist"* on a truncated walk, and plainly *"not found"* on a complete one — the second half matters
as much as the first, or the caveat becomes noise on every genuine miss.

Four further handoff sites (the ones whose wider "blast radius" framing this audit **refuted**) keep
their behaviour but were re-pointed at the same helper: their old text asserted *"Find Instances +
ListClasses both empty"*, which is a false statement about a capped list. **Game Class Filter** now
appends *"⚠ STOPPED at the 5,000-row cap — more classes exist"* — worth it because its
`total_classes` moves in lockstep with the results vector, so on a truncated walk that status line
printed the cap twice and read as a pool size.

**Negative controls, three, each isolating one claim.** Forcing `Truncated = false` in the parser
reds exactly the 2 truncation tests — and the "full walk is not truncated" test correctly stays
green, because it asserts an absence. Passing `""` instead of `row.ClassAddr` reds exactly the 3
address-carrying tests. Flattening `MissReason` to `"not found"` reds exactly the capped-wording
test. 3631 tests, 0 failed. `dist` is the 54.4 MB AOT-trimmed binary, launch-verified, no
`crash.log`.

⬜ **Not verified in-game** — nobody has clicked AA(B) on a class past the cap in a real title and
watched the script generate.

-----

## 2026-08-15 - Struct parameters: one path wrote four bytes of a vector, the other trusted a guessed layout (build 2881)

**Audit #5 segment U4 fixes Y6 and Y7.** Both are struct params, and both come down to the same
question — *what do you do when you cannot know the layout?* They answer it in opposite places, so
the fixes differ.

### Y6 — the CE form cannot edit a struct, so it must stop pretending to

The generated form has one edit box per param, and `StructProperty` has no scalar spelling. It fell
through `GetMailboxWriteStatement`'s size switch to `writeInteger`: **four bytes** of a 12-byte (UE4)
or 24-byte (UE5 LWC) `FVector`, taken from `math.floor(tonumber(text))`, with the remaining bytes left
zero. A garbage vector went into a live call and nothing said so.

The write is now skipped. The params buffer is already zero-filled, so the callee receives a
well-defined zeroed struct rather than a mangled one; the emitted script carries a
`-- <name>: struct (N B) left ZEROED` line; and the box keeps its place with its label changed to
`NOT EDITABLE - sent as zeroes`. Keeping the box is deliberate — `edits[i]` is indexed by param
position, so removing it would silently shift every later param's box.

### Y7 — the engine's size overrules the version guess

`KnownStructLayouts.GetLayout` is keyed on a **detected** UE version, and the dialog built its
sub-field editors from it without ever comparing it against the size the engine reported for that
param. When the two disagree the guess is wrong: a mis-detected version (LWC turns `FVector`'s floats
into doubles, doubling every offset) or a licensee fork with a modified struct — this project has met
both. Expanding on a wrong layout puts every sub-field editor at a wrong offset, so the numbers the
user types land in the wrong bytes of a live call with nothing on screen saying so.

The layout is now refused when it contradicts the engine, and the caller falls through to the
DLL-discovered fields, which came from the actual `UScriptStruct`.

**The rule was extracted to `ResolveTrustedLayout` so it could be tested at all.** It had been an
inline condition inside a View's control-building loop — unreachable from a test, which is the same
structural reason X1's missing field survived two builds. Five tests cover it now, including both
directions of the mismatch and the `engineSize <= 0` case, where nothing contradicts the guess and
keeping it is the right answer.

**Negative control:** reverting both turns 4 tests red — 2 for Y6 (one per struct size) and 2 for Y7
(one per direction of the mismatch). `Generate_StructParam_LabelsTheBoxAsInert` correctly stayed
green: the label and the write-skip are separate changes and only the latter was reverted.
3624 tests, 0 failed.

⬜ Not verified in-game: nobody has passed a struct param through either path against a live UFunction.

-----

## 2026-08-15 - The invoke script's mailbox lookup raised on exactly the case it was written to handle (build 2875)

**Audit #5 segment U4 fix Y8**, and the last bare `getAddress` the repo emitted.

`celua.txt` is unambiguous: `getAddress` *"returns the address of a symbol"*, while `getAddressSafe`
*"returns the address of a symbol, **or nil if not found**"*. The bare form **raises**. The block in
question exists precisely to handle a missing symbol — the DLL not being loaded — so the first call
aborted the whole chunk and took three things with it:

- the **module-prefixed fallback** on the very next line. Both spellings are mandatory, not a
  preference: which one resolves depends on how CE picked the module up (lessons-learned B33).
- the **diagnostic**, so instead of *"make sure UE5Dumper DLL is loaded (version.dll Proxy or CE
  inject)"* the user got CE's raw Lua error.
- the **cleanup timer**, so the memory record stayed **ticked** after a bail-out that applied
  nothing — against CLAUDE.md's rule that a bail-out which applied nothing must untick.

Both siblings already did this right: `BakedScriptGenerator` via `getAddressSafe`, `CeReadinessLua`
via `pcall(getAddress, …)`. Same sibling-divergence shape as W4/W6/X1.

### A pre-existing test broke, and it was the right kind of break

`CeMailboxBailoutTests.NoMailboxWriteEscapesTheIdleWait` uses the mailbox lookup as a *textual
anchor* to find where its window scan starts, and the rename lost it. The test had predicted this in
its own failure message — *"the anchor this scan starts from is gone"*. It now anchors on the
**symbol** instead of the lookup function, so it survives a change of lookup.

Worth distinguishing from the Y1 fix two commits ago, where an actual **assertion** had to change
because it pinned the defect. Here nothing about the test's subject — write ordering — moved; only
its anchor did.

**Negative control:** reverting to `getAddress` turns the two new tests red while
`CeMailboxBailoutTests` stays green, which is the proof the re-anchoring is genuinely
spelling-agnostic rather than retuned to the new spelling. One new test was hardened mid-flight for
the same reason: it had anchored on `getAddressSafe(`, so on revert it failed because `IndexOf`
returned −1 rather than because the untick had gone. 3615 tests, 0 failed.

-----

## 2026-08-15 - Two discovery panels reported a capped page as the whole pool (build 2870)

**Audit #5 segment U3 fix X1** — and it is the D5/F4 truncation fix finally reaching its second site.

The DLL has emitted per-query `truncated` and batch-level `aborted` since F4; the comment at
`Fern.cpp:2822-2825` names *"audit #5 D5/F4"* outright. The C# parsed them on the single-query path
and not on the batch twin, ~80 lines away in the same file, and `PropertySearchQueryEnvelope` had no
field to parse into.

The cost lands on the two panels that use the batch call. **Interesting Properties** sends all 51
`PropertyScoringTable.SeedQueries` at 200 rows each, so ordinary seeds — `Max`, `Count`, `Time`,
`Level`, `Hit` — cap routinely on any real game. The panel then reported *"N unique properties"* with
no caveat, the user filtered that page, did not find their field, and concluded it does not exist.
That is the exact report class the F4 fix was written to end. **Detect Player Stats** makes the same
call at the same limit and had the same blind spot.

Both now say so, mirroring the wording the single-query path has used since F4: *"⚠ N of 51 keywords
STOPPED at the 200-row cap (Max, Count, Time, …) — more matches exist"*, and a shorter form in Detect.

### The test hook is the durable part

The batch parse was inlined in an `async` method that needs a live pipe, so **nothing could reach
it** — which is precisely why a missing field survived two builds. It is now
`internal static ParseSearchPropertiesBatch(JsonObject)` plus a string-taking hook, so the wire
contract is testable without a pipe. Three tests: the per-query flag, the batch flag, and an older
DLL that omits both keys.

That third test matters more than it looks. It passes in **both** directions — before and after the
fix — because it asserts an *absence*: a pre-2818 DLL sends neither key and must not produce a
spurious cap warning on every scan. It is a guard against the fix, not a demonstration of it.

**Negative control:** removing the two parse lines turns the two flag tests red. 3613 tests, 0 failed.

⬜ Not verified in-game: nobody has run a real batch scan on a title where a seed keyword caps and
watched the strip appear.

-----

## 2026-08-15 - One dialog, two different calls: FIRE and Copy AA Script disagreed on what you typed (build 2866)

**Audit #5 segment U4 fixes Y2, Y3, Y4 and Y5.** One commit, because they are one defect wearing four
hats: `ParamBufferBuilder` — the FIRE path — re-implemented, more weakly, the parsing
`BakedScriptGenerator` already did correctly for the exported script.

| | typed | FIRE sent | exported script sent |
|---|---|---|---|
| **Y2** | `3` into a 1-byte enum | **nothing at all** (game got 0) | 3 |
| **Y3** | `true` | **0** | 1 |
| **Y4** | `1,5` | **15.0** | refused |
| **Y5** | `-1` into an `Int8` | **0** | −1 |

Y2 is the sharpest of the four: `EnumProperty` was grouped with `IntProperty` and the write gated on
`available >= 4`, so a 1-byte `enum class : uint8` param — the standard shape — failed the guard and
**was never written at all**. The game received whatever the zero-filled buffer held.

Y4 is the one with a paper trail: `TryParse(text, IFormatProvider, out _)` defaults to
`NumberStyles.Float | AllowThousands` **and accepts `NaN` / `Infinity`**. The baked path had already
discovered that, documented it (as B23) and guarded against it; the FIRE path had not, so `1,5` fired
as 15.0 and `NaN` reached the game as a real float.

### The fix is to share the parsers, not to write a fifth one

`BakedScriptGenerator.ParseBoolLiteral` and `TryParseHexOrDecimal` became `internal`, and the FIRE
path calls them. `EnumProperty` now routes through a new `WriteBySize` that the size-driven fallback
uses too, so those two cannot drift apart either.

Copying the logic across would have been the obvious move and the wrong one. This audit has now found
its *own* fixes applied to only some of the places that needed them three separate times — V2 (one
side of the wire), W4/W6 (some call sites of a helper), X1 (one of two single/batch twins). Sharing
the implementation is the only repair that does not depend on a future editor remembering.

**Negative control:** reverting `ParamBufferBuilder.cs` to its pre-fix state turns **11** tests red,
mapping to all four defects — 2 enum widths, 4 bool spellings (`true`/`TRUE`/`yes`/`on`; `1`/`0`/
`false`/`no` pass either way because the old byte parser handled digits), 4 float cases, 1 signed
byte. 3610 tests, 0 failed.

⬜ Not exercised against a live game: the bytes are unit-verified, but nobody has watched a UFunction
receive a 1-byte enum or a `true` from the FIRE button.

-----

## 2026-08-15 - The CE invoke form passed a null pointer for every address you typed into it (build 2862)

**Audit #5 segment U4 fix Y1 (HIGH).** The generated Cheat Engine invoke form detected a leading
`0x` and then handed the **still-prefixed** string to Lua's `tonumber(s, 16)`. Lua's base form
rejects any character that is not a digit of that base, so the `x` made it `nil` and the `or 0`
fallback wrote a **null pointer** into the params buffer. The DLL memcpys that straight into
`ProcessEvent`, so the UFunction was called with `nullptr` — an access violation for any callee that
dereferences it — while the script still reported `INVOKED OK`, or closed the Lua window silently
when `DEBUG == 0`.

Every `UObject*` / `FName` argument the user actually filled in was affected, because the app formats
every address as `0x` + uppercase hex (`Renge::AddrToStr`). Only the `'0x0'` default parsed
"correctly" — by arriving at the same 0 — which is why a smoke test with unmodified defaults always
passed and the capability never worked.

The irony is that the `else` branch was already right: plain `tonumber(s)` accepts `0x…` hex quite
happily. The special case written to handle hex was the only thing breaking hex.

### The fix

Strip the prefix before the base-16 parse, then fall back to decimal and finally to bare hex:

```lua
local s = edits[N].Text or ''; s = s:gsub('%s+','')
local h = s:match('^0[xX](%x+)$')
if h then return tonumber(h,16) or 0 end
return tonumber(s) or tonumber(s,16) or 0
```

**Decimal is tried before bare hex on purpose.** This branch also serves `NameProperty`, whose value
is an FName index a user may well type in decimal; reading `1234` as hex would silently change its
meaning. A test pins the ordering.

### Verified with three independent detectors

A claim about a runtime we do not control is not settled by reasoning about that runtime:

1. **Cheat Engine's own `lua53-64.dll`** (`_VERSION` = Lua 5.3), driven via ctypes with a stub
   `edits` table, evaluating the emitted expression verbatim. Before: `0x1F2A3B4C5D0` → **0**,
   `0X7FF6CD120000` → **0**, bare hex → **0**, and only a *decimal* address survived. After: every
   form resolves, `1234` stays 1234, junk → 0, and every result is a Lua **integer** (`math.type`),
   which `writeQword` requires.
2. **A standalone Lua 5.4.6 CLI** reproduced all ten inputs identically, which also shows the
   behaviour is stable across 5.3 → 5.4 — worth knowing if CE ever updates its bundled Lua.
3. **CE's bundled Lua source**, `Cheat Engine/lua53/lua53/src/lbaselib.c:48-65`, gives the mechanism:
   `int digit = isdigit(*s) ? *s - '0' : toupper(*s) - 'A' + 10; if (digit >= base) return NULL;` —
   for `'x'` that is `'X' - 'A' + 10 = 33`, and `33 >= 16`.

**A detail worth keeping:** the old code accidentally *worked* for padded input. `  0x40  ` made
`s:sub(1,2)` miss the prefix and fall into the working `else`, so the same field succeeded or
silently returned null depending on stray whitespace.

**Negative control:** reverting the fix turns all four new tests red. One of those tests was wrong on
the first attempt — it asserted the emitted script contains no `tonumber(s,16)` at all, which fails
the *correct* code, since the fix legitimately uses that form as the bare-hex fallback once the prefix
is known absent. Corrected to assert the old prefix-detection idiom (`s:sub(1,2)`) is gone, which is
the defect's precise signature. 3594 tests, 0 failed.

⬜ Not yet exercised inside Cheat Engine against a live game — filed in [todo.md](todo.md).

-----

## 2026-08-14 - A 1-byte enum array was exported as 4-byte CE records, so each one ate the next three elements (build 2857)

**Audit #5 segment U2 fix W6**, and with it the last of the segment's partial-application defects.

`MapInnerTypeToCeField` mapped `EnumProperty` to a hardcoded CE type of `"4 Bytes"`, but element
**addresses** are laid out with the DLL's real `ArrayElemSize` / `SetElemSize` — 1 for the standard
`enum class : uint8`. So a `TArray<ECharacterState>` produced records spaced one byte apart, each
reading four bytes: element 0's record swallowed elements 1–3, and every value in the pasted table
was wrong in a way that still looked like a plausible number.

### The fix is the signature, not the branch

The rule was already known — and already *written down* at the site that got it right:

> *"Enum width follows the sub-field's real byte size (a 1-byte enum must NOT be read as 4 bytes —
> that pulls in the next field's bytes)."*

It was applied at three of five call sites (struct sub-fields, map key, map value) via a caller-side
ternary, and the TArray and TSet element paths simply did not have it. Adding a fourth copy of the
ternary would have left the sixth call site free to forget it again, so instead
`MapInnerTypeToCeField` now **requires** the element size and applies the rule itself. All five sites
get it by construction, and the duplicated ternaries are gone.

Two mappings are deliberately **not** size-driven, and are now documented as such so a later reader
does not "fix" them: `NameProperty` stays 4 bytes because the record shows the FName
`ComparisonIndex` paired with a DropDownList of names rather than the whole 8- or 16-byte FName, and
the pointer flavours stay 8 regardless of stride.

### Negative control

Reverting the width to a hardcoded `"4 Bytes"` fails three tests: the two new byte-wide array/set
tests **and a pre-existing struct-sub-field test**. That last one is the useful part — it confirms
the refactor preserved the behaviour that test was already pinning, rather than merely satisfying
assertions written alongside the change. 3590 tests, 0 failed.

-----

## 2026-08-14 - The .usmap export declared one format and wrote another, and had never produced a readable file (build 2853)

**Audit #5 segment U2 fixes W1 (HIGH) and W7.** W7 came along because W1's deliverable does not work
without it: a name index past the end of the table corrupts the very file the version fix exists to
make openable.

This was never a regression. `git log -S` shows the `Version` constant has exactly one commit in the
file's history — its creation, `7f91295`, 2026-03-01 — and the string `bHasVersion` had never
appeared in any revision. The menu item shipped for 5½ months and could not once have written a
header a consumer would accept.

### The version now describes the bytes

The writer stamped `Version = 3` and emitted the version-0 body. Three independent desyncs followed,
and the first one lands before a single name is read:

- **`int32 bHasVersionInfo` was missing.** Any version ≥ `PackageVersioning` (1) must carry it. A
  reader consumes four bytes there, so it ate the low bytes of the payload size and threw.
- **Enum member counts were `uint8`** where ≥ `LargeEnums` (3) requires `uint16` — and the `uint8`
  also silently truncated any enum past 255 members.
- **ArrayDim was a hardcoded 2-byte `0`** where the format says one byte, so every struct's first
  property slid the stream by one, and a `0` additionally tells a reader to register no schema slots.

It now emits **v4 (ExplicitEnumValues)**, the version both vendored canonical writers produce, with
each member's `int64` value so an enum with gaps is no longer flattened to `0..N-1`. v4 rather than
"the cheapest v2" because the writer already emitted `uint16` name lengths (it can never go below 2)
and because the `uint16` count removes the 255-member truncation. The CEXT extensions block is
deliberately not written — Dumper-7 emits v4 without one, so it is optional.

A fourth fix, quieter but the same class: a struct's two counts are genuinely different numbers — the
first is the sum of every property's ArrayDim (a static array `Foo[4]` occupies four schema slots),
the second is how many property records follow. Both were `Fields.Count`.

### W7 — the name table can no longer be extended behind the file's back

The table's length is written once, up front, but the write pass resolved struct/enum references
through a `GetIndex` that fell through to `GetOrAdd` and **appended**, handing out an index past the
end of the table the file had already declared. `"None"` is now pre-registered, the write pass may
only use a non-appending `IndexOf`, and `NameTable.Seal()` makes any later `GetOrAdd` throw. The
invariant is enforced by the type instead of remembered by the caller.

### The real deliverable is the round-trip reader

`UsmapFile.Parse` in the tests reads the file the way a consumer does, at the widths the canonical
writers define, and asserts **the stream is fully consumed** and every name index is in range.

That is exactly what the old tests could not do. All five skipped a hardcoded 12-byte header and read
each field at the width the *writer* happened to use — so they encoded the bug rather than checking
it, and stayed green for 5½ months. Five were rewritten onto the reader and four added: a full
round-trip over every container shape, static-array slot counting, a 300-member enum, and an
unregistered struct name.

Negative controls were run one per sub-fix, each reverted alone: removing `bHasVersionInfo` fails 9
tests, restoring the `uint8` enum count fails 3, restoring the 2-byte ArrayDim fails 4, and
un-registering `"None"` fails 1. The reader is therefore checking the canonical layout rather than
mirroring the writer. 3587 tests, 0 failed.

⬜ Still unverified against a real consumer: the round-trip proves self-consistency at the vendored
writers' widths, but nobody has opened the output in FModel yet. Filed in [todo.md](todo.md).

-----

## 2026-08-14 - The SDK header re-declared everything it inherited, and gave each packed bool its own byte (build 2842)

**Audit #5 segment U2 fixes W2 (HIGH) and W3.** One commit: both live in the same emitter and the
same layout cursor, and W3's byte accounting is only observable once W2 stops flooding the struct
with inherited members.

### W2 — every derived class had the wrong `offsetof`

`Ubel::WalkClass` deliberately prepends the **entire** SuperStruct chain to its field list, and
nothing between the DLL and the SDK emitter filtered it out. The emitter's own comment said "skip
fields below the superclass boundary", but the code only moved the padding cursor — the loop still
emitted every inherited property. So `struct BP_Player_C : public AActor` re-declared all of AActor's
properties inside a struct that already inherits them. That compiles, which is why nobody noticed;
it just silently lays the struct out wrong from the first member onward.

The boundary is now **sent, not guessed**: `walk_class` gained `super_props_size`, read in
`Ubel::WalkClass` where `SuperClass` is already resolved. Nothing else in the reply implies it, so a
client can only heuristic its way there — the same situation as `map_stride` two builds ago, and the
same answer: *where the input is an engine fact the wire does not carry, send the number.* The old
first-field heuristic survives only as a fallback for an older DLL, and is now documented as one,
because it mis-splits silently when a derived class adds no properties of its own.

### W3 — N packed bools consumed N bytes instead of one

UE packs `uint8 bX:1` flags into a shared byte; they arrive at the same offset with Size 1. The
emitter wrote a whole `bool` for each and advanced the cursor by Size every time, so eight flags took
eight bytes instead of one. Padding could not compensate — it is only emitted when the next field's
offset is *ahead* of the cursor, and by then the cursor had overshot. Every later member, and the
trailing `// Size:` comment, was displaced.

They are now emitted as `uint8_t Name : 1` at their **true bit positions**, with unnamed fillers for
the bits UE left unused, so bit N in the game is bit N in the header. A native `bool` (FieldMask
`0xFF`) keeps its byte, and so does an unresolved mask (`0`) — an unknown must never be allowed to
rewrite the layout.

### Both duplicated loops are gone

The schema and live emitters carried byte-identical member-emit loops, and therefore carried both
defects. They now project into a single `SdkField` record and share one `EmitStructBody`. Fixing this
in two places is how it would have drifted apart again — the same lesson as the three stride mirrors.

### One pre-existing test was changed, deliberately

`GenerateClassHeader_BoolBitfield_EmitsComment` asserted `bool bHidden;` for a field with
`BoolFieldMask = 0x04` — a single-bit mask, i.e. precisely the packed bitfield W3 is about. Its name
and its second assertion show it was written for the *mask comment*; the declaration form was
incidental and pinned the defect. The mask assertion is untouched; the declaration assertion now
expects the bitfield. The arithmetic behind that call: with **one** bool the struct size is identical
either way, but the bit position was wrong (bit 0 instead of bit 2) — and with several bools the size
itself breaks, which the new eight-bool test demonstrates.

Negative controls were run **separately** so each fix is independently guarded: reverting only the
bitfield grouping turns exactly the two W3 tests red; reverting only the inherited-field filter turns
exactly the one W2 test red. Restored and re-confirmed: 3583 tests, 0 failed.

⬜ The unit tests drive the real emitters end-to-end, but the boundary *value* now comes from the DLL
and no headless check has yet read a real `super_props_size` off a live class — filed in
[todo.md](todo.md).

-----

## 2026-08-14 - One '&' in a game string could reject an entire pasted cheat table (build 2836)

**Audit #5 segment U2 fix W4.** `<Description>` text has been XML-escaped since audit #4 B3, because
a single `&` anywhere in a multi-thousand-entry export makes the document malformed and Cheat Engine
rejects **all** of it, with no indication which record was at fault. The `<DropDownList>` body — the
*other* place a game-derived string reaches the XML — was never covered.

Its content is built from live `FName` entries, enum member names and formatted container element
values, then interpolated raw. A stock `TArray<FName> Tags` holding a designer-typed `Bow & Arrow` is
enough. So is a `TMap<int32, FName>`, whose values are routed into a dropdown rather than a
description.

Escaping now happens inside `BuildDropDownContent`, which is the single choke point: all six call
sites — including the cached `_dropDownOwners` link path — build their body there, and both
`<DropDownList>` emit sites interpolate that body.

**The fix has two halves and the second is the one well-formedness cannot catch.** Metacharacters go
through the same `EscapeXmlContent` the Descriptions use. But the body is also **line-delimited**, so
a CR/LF inside a game string forges an extra dropdown row and shifts every following one *without*
making the document malformed. `CollapseLineBreaks` flattens those to spaces.

### Why five existing escaping tests did not catch it

`CeXmlEscapingTests` was written for B3 and every one of its five tests puts the game string in a map
**key** — which lands in `<Description>`. Nothing in the suite reached the dropdown path, so it passed
throughout. The four new tests go through a `TMap<int32,FName>` to reach it, and they live in that
same file on purpose: the file is the record of what "the export must survive arbitrary game text"
means, and it was incomplete.

Verified with a negative control rather than a green run: reverting the fix turns all four red, each
for its own reason — `&` gives *"error parsing EntityName"*, `<` gives *"Name cannot begin with ' '"*
(the parser started reading a tag), and the newline test fails **with no XmlException at all**, which
is precisely why that half needed its own handling. 3579 tests, 0 failed.

Still open in the same emitter and the same family: **W6**, where `CeWidthForSize` is bypassed by the
enum array/set path — the other partial-application defect U2 found.

-----

## 2026-08-14 - The map row that was editing its own key, and a formula the DLL had already fixed for itself (build 2830)

**Audit #5 segment U1 fixes V1 (the audit's only surviving HIGH), V2 and V5.** One commit, because
the three are one subsystem and not independently correct: V1's write address is computed *from*
V2's stride, so shipping V1 alone would have aimed a corrected offset off a wrong base.

### V1 — a TMap element row was inline-editable, and its address was the KEY

A `TPair` stores its key first, so a map element's base address *is* the key. `PopulateMapContainerFields`
built each row with `TypeName = MapValueType` — which makes any scalar-valued map pass
`FieldValueConverter.IsEditableType` — while setting `FieldAddress` to that element base. Every
consumer of `FieldAddress` on such a row acts on the row's declared type: the inline editor writes
there, "+CE" pushes it as a record typed from the value, the Hex button navigates there, the Address
column shows it. So editing a `TMap<FName,int32>` value wrote the user's four bytes over the FName
key — silently corrupting the map in a live game, and every later lookup of that entry missing.

The correction was already known to the file: `MapValueDrillOffset`'s doc comment states it outright,
and it was applied at exactly one call site (`NavigateToFieldAsync`'s `navOffset`) while the edit and
export consumers were not. Rows now carry the value's address.

**A second half the finding did not name:** the row also reported `Size = MapKeySize + MapValueSize`,
and `Size` reaches `TryConvert` as the **write length** — so a `TMap<int32, enum4>` would have written
8 bytes over a 4-byte value even once the address was right. A row now describes the value in all
three respects: type, address, size. `Offset` deliberately stays the element base, because
`MapValueDrillOffset` adds the value offset back when a drill-down builds a breadcrumb.

### V2 — three C# copies of a formula the DLL had already corrected

Build 2554's cluster ① fix (`5ef4c2b`) replaced `ComputeSetElementStride` with an alignment-aware
`Align(Align(elemSize, alignof(T)) + 8, alignof(T))` — **in the DLL only**. The same formula existed
in three C# files (`LiveWalkerViewModel`, `CeXmlExportService`, `CsxExportService`), each carrying a
doc comment claiming it mirrored the DLL. All three stayed on `Align(elemSize,4)+8`, so across five
map call sites the grid's key→value *text* was right (the DLL read it) while every map element
address the UI computed *itself* was 4+ bytes short past index 0: Address column, struct-drill target,
the breadcrumb offset feeding CE chains, and the CE-XML / CSX exports. TSet was unaffected — a bare
`elemSize` is already a multiple of `alignof(T)`, so the DLL's `elemAlign` default of 4 reproduces
the old behaviour exactly.

**The fix is not a fourth copy of the arithmetic.** The stride needs `alignof(Key)`/`alignof(Value)`,
which are engine facts that never cross the wire — a client can only guess. So the DLL now publishes
the stride it *actually used to read the elements* as additive wire fields `map_stride` / `set_stride`
(set at all four `Ubel.cpp` walk sites), and one new `Core/ContainerGeometry.cs` is the only
client-side consumer. All three mirrors are deleted; the old expression survives solely as
`ContainerGeometry.FallbackStride`, documented as correct only for `alignof(T) <= 4` and reached only
when the DLL supplied nothing. UI and DLL can no longer disagree, because there is one number.

### V5 — the multi-select clone dropped the geometry

`FilterContainerToElement` rebuilds a container field property-by-property for the "Copy CE Field(s)"
path and omitted `MapValueOffset`, so exporting *selected* map elements laid out differently from
exporting the same map whole. Fixed by carrying the geometry — which the V2 work made mandatory
anyway, since a dropped `MapStride` would have sent that one path back to the guess.

### Verified with a negative control, not just a green run

8 new tests in `ContainerGeometryTests` (3575 total, 0 failed). Both fixes were then reverted in
`ContainerGeometry.cs` and the suite re-run: **5 tests failed** — the helper, the seam
(`PopulateMapContainerFields`, made `internal` so a test can drive the real populate path) and the
clone — then the fix was restored and green re-confirmed. That ordering matters here: the pre-existing
`FieldValueConverterTests` passed in *both* directions, because the helper was never the broken part.
The bug lived in the caller that fed it an address, which is exactly the seam
[working-lessons.md](working-lessons.md) §1.3 warns about.

⬜ **In-game verification of the UI half is still owed** — see the entry in [todo.md](todo.md). The
DLL half already has `DumperTest` witnesses; what no headless pipe check can see is client-side
arithmetic, so the Address column / inline edit / CE record need a live look on a map whose pair
alignment is 8 and whose pair size is not already a multiple of 8.

-----

## 2026-08-14 - Two replies that reported work they had not done, and the feature that was empty all along (build 2818)

**Audit #5 D5 fixes F4 and F6 — and F8, which the F6 fix immediately exposed.**

### F4 — `search_properties` claimed a full sweep after stopping at the result cap

`Aura::SearchProperties` assigned `result.scannedObjects = GetCount()` **before** the walk, so a
search that stopped at the `maxResults` cap a few percent in still reported the whole pool. The panel
printed that as *"Found 200 properties in 3,412 classes (scanned 1,204,338 objects)"* — and a user
whose field was past the cap read it as proof the field does not exist. `PropertySearchResult` now
carries `truncated` (cap reached) and `aborted` (`Tot::Requested()` fired), `scannedObjects` is what
was actually walked, and the batch twin got the same treatment with **per-query** `truncated` — the
batch loop stops only when *every* query is full, so one seed keyword can be capped while another
swept everything. Measured on DumperTest: capped query `total 3 / classes 8 / scanned 105 /
truncated true`; full sweep `scanned 24445`, which is exactly what `get_object_count` reports.

> The first build had `walked = i` and a full sweep reported **24444** — one short. Caught by the
> same cross-check that made the original lie visible. A number compared only against itself is not
> checked at all.

### F6 — `walk_world` reported the page size as the level's actor count, and failed silently

`actor_total` and `truncated` are now emitted beside `actor_count`, and the two failure branches
(`Actors` unresolved; `ReadTArray` failed) set `data["error"]` instead of returning `actors: []` with
`ok:true` — which the handler already did for the two failures above them.

### F8 (new) — `ULevel` has no reflected `Actors` on this engine, so `walk_world` enumerates nothing

The F6 error string fired on DumperTest's stock ThirdPersonMap on its first run and named the branch:
`actorsOffset < 0`, not a failed read. Walking the live `ULevel` confirms it — **29 fields, 7
`ArrayProperty`, none named `Actors`** (`ModelComponents` @208, `NavDataChunks` @264,
`StreamingTextures` @320, `DestroyedReplicatedStaticActors` @768; the only `*Actor*` names are
`ActorCluster` and `LevelScriptActor`, both `ObjectProperty`). `walk_world` finds the actor array
purely by reflection, so on this engine "Load GWorld" renders a populated level as empty. `Actors` is
a real native member — the fix is a native-offset read, not more reflection. Filed, not fixed.

**Reproduced on a second, unrelated title within the hour.** Solarpunk (commercial, different engine
build), same day's session: `walk_world limit:500` → `{"actor_count":0,"actors":[],"ok":true,
"world_name":"MainLevel"}`. **2 of 2 games tested return nothing**, so this is not a DumperTest
artifact and may not be version-specific at all. Two captures is not a survey — what is established is
that it fails on both engines we have evidence for, not the size of the affected range.

> **This entry originally said "`walk_world` demonstrably works on other titles".** Nothing was checked
> before writing that; it was a conservative-sounding assumption, and the maintainer asking an adjacent
> question is what sent me to look at the one other capture on disk, which refutes it. **An "honest
> limit" written to sound cautious is still an unverified claim — and it draws less scrutiny than a
> bold one precisely because it sounds modest.**

> Worth keeping as method: **the cheapest new finding of the day came from making an existing silent
> failure speak**, not from another scan. When a reply cannot say what it failed to do, the defect
> underneath it is invisible too.

**Wire-only.** `PropertySearchPanel` does not yet bind `truncated`, and `LiveWalkerViewModel` does not
bind `actor_total` — the DLL stopped lying, the panels have not started telling. Separate pass.

-----

## 2026-08-14 - Every game exit paid 5 seconds to drain threads Windows had already killed (build 2813)

**Audit #5 segment D5 fixes F1 and F7 — the first two fixes from the D5 scan, both in `Fern`.**

### F1 — `~Fern()` ran the whole pipe teardown from `DLL_PROCESS_DETACH`

`Fern::Stop` now takes `bool graceful = true`, and `~Fern()` calls `Stop(false)`, which logs the entry
path and returns. Everything it skips — the cancel sweeps, the watch/scan joins, the **5-second**
connection drain, and the accept/monitor joins — is teardown that `Heiter.cpp:288-301` already refuses
to do at DETACH and `Routine.h:51-56` already documents as fatal for every *other* module's worker.
`Fern::Stop`'s explicit `join()` / `wait_for` calls were simply never added to that list, and
`s_pipeServer` being a namespace-scope static (`Frieren.cpp:92`) means the CRT runs them anyway.

Two things went wrong, and the second is worse than the first:

1. **`ExitProcess` has already terminated the connection threads**, so a dead thread can never erase
   itself from `m_conns` and the drain predicate is **unsatisfiable by construction** — the full 5 s
   budget burned on every exit that still had a client registered.
2. The body takes `m_connMutex`, Sein's log mutex and both Radar session mutexes **after their holders
   were killed**. MSDN is explicit that detach code taking a lock a terminated thread held deadlocks
   the process — i.e. a game that never closes.

**Measured on packaged DumperTest, one variable, graceful `WM_CLOSE` → `ExitProcess` → DETACH:**

| | Client at exit | Drain | Process exit |
|---|---|---|---|
| pre-fix, held open | `conns=1` | `TIMEOUT, 1 left (5030 ms, 49 re-asserts)` | 6,046 ms |
| pre-fix, disconnected first | `conns=0` | `satisfied, 0 left (0 ms)` | 1,105 ms |
| **post-fix, held open** | — | *skipped* | **1,185 ms** |

A connection open at exit now costs nothing. **`Stop-Process -Force` cannot see any of this** —
`TerminateProcess` skips DETACH entirely, so a forced kill exits fast and "proves" the bug is gone.

> **This also closes a question that had been answered wrongly four times.** todo.md's
> `Stop conn drain TIMEOUT` entry concluded the connection was *genuinely blocked in a synchronous
> `ReadFile`* (root cause: no `FILE_FLAG_OVERLAPPED`). That explains why `CancelIoEx` found nothing but
> **not** why `CancelSynchronousIo` — the correct API for a live thread blocked in synchronous I/O —
> also reported nothing-pending, 49 times. A terminated thread explains both. What found it was not a
> new diagnostic but asking **who calls `Stop`**: `UE5_Shutdown` logs `"Cleaning up..."` as its first
> statement, the shipped `.CT` only *probes* `UE5_StopPipeServer` before calling `UE5_Shutdown` alone,
> and `grep -rn "Cleaning up"` over the whole Logs tree returns **zero** — so no capture on disk was
> ever the CE-untick repro the entry was written around. Both structural fixes proposed there (close
> the handle from `Stop`; make the pipe overlapped) act on a *live* thread's `ReadFile` and would not
> have helped.

### F7 — an error string that named an execution which never happened

`invoke_function` rendered result `-7` as *"(hook not active, direct call used)"*. `-7` is produced
**only** by `Stark::EnqueueInvoke`'s inactive-hook guard or by `Stark::Shutdown` draining the queue —
neither reaches ProcessEvent by any route; the direct fallback lives on the other side of
`if (Stark::IsHookActive())` and returns 0/-2/-3/-4/-8, never -7. Now reads *"game-thread hook is down
— the invoke was never dispatched; re-enable the script and retry"*, and `-8` gained the mapping it
never had (it used to fall through as a bare number).

**Not verified: the graceful path.** It is unchanged by construction — the fix is an early return in
front of it — but reaching `Stop(graceful=true)` needs a CE Disable, so it is filed in todo.md's
pending register rather than claimed. Note also that `-Target Test` says nothing here: **no test target
compiles `Fern.cpp`.**

-----

## 2026-08-12 - The leftover-proxy refusal nobody could see, and the log we could not read (build 2801/2804)

Finished B13/B41's end-to-end half — *watch a leftover-proxy row actually carry the no-Recycle-Bin
refusal* — and it took two failed runs to get one honest measurement.

**The rig.** Previous attempts used a synthetic UE game on a scratch volume and stalled on "the scan
does not detect it". That turned out to be **a miscount, not a bug** — `Generic scan found 8 UE
game(s)` already included it and the row was on screen the whole time. Replacing it with a real game
copied wholesale (Steam's *Light Maze*, 215 MB) removed the entire question: `Found 9 UE game(s)`,
deploy `version.dll`, delete everything Steam would own, and the sole survivor is exactly the
leftover-after-uninstall shape.

**Defect ① — predicted, and confirmed.** `PlanPrune` computes `OrphanVerdict.NotOnFixedDrive` with a
carefully worded refusal, and the scan's surface filter then threw it away, keeping only
`Deletable`/`FileOnly`. The user was told *"No leftover proxy DLLs found (23 folder(s) examined)"*
while our DLL sat on a volume where deleting it by hand is unrecoverable. The feature's own PASS
criterion was unobservable as shipped.

**Defect ② — found only by running the negative control.** Flipping the bin back ON was supposed to
be a formality; it also returned 0 rows, which is what revealed that the first run had measured
nothing. `CandidatesFromLogs` read our own `view-*.log` with `File.ReadLines`, which opens
`FileShare.Read` and **cannot open the live `view-0.log` our own logger holds**. The per-file `catch`
swallowed the sharing violation, so the current session's whole deploy log contributed zero
candidates — the 22 examined came from an *archived* log. Practical cost: **deploy a proxy, uninstall
the game, press Find leftovers in the same session → nothing found**, until a restart rotates the
log. `SteamShapeScan` masks this for Steam titles, so it bit exactly the non-Steam locations the log
sources exist to cover.

**Fixes.** `OrphanVerdictRules.{IsActionable,ShouldSurface}` — one pure pair beside the enum,
replacing three hand-written copies of two *different* predicates, with `NotOnFixedDrive` now
surfaced but never actionable. `ReadLinesShared` (`FileShare.ReadWrite | FileShare.Delete`) for the
log sweep and for the Steam `.acf` read, where a manifest Steam holds open made
`TryReadAcfInstallDir` report unreadable and silently refuse every Steam candidate. The recycler
question moved **below** `ClassifyLeaf` so a no-bin volume cannot manufacture a refusal for a folder
holding nothing of ours, and it now carries the file list so the row can name the file. Plus honesty
work: a blocked row authorises nothing, the report says *"NOT removable"* rather than *"to be
recycled"*, and the status line counts blocked rows separately.

**Post-fix, on the AOT/trimmed build 2804:** bin OFF → 23 examined, 1 row carrying the refusal, its
checkbox disabled and `Delete checked (0)` greyed; bin ON → same folder becomes
`Recycle version.dll — folders left in place`, tickable, `Delete checked (1)`. One registry DWORD
apart, same process. 22 new tests, each with the negative control that makes it mean something —
including one asserting `File.ReadLines` **throws** on the same handle first, or it would be
asserting nothing. 3567 C# green.

-----

## 2026-08-11 - `executeCodeEx`'s wait cannot be pumped, and one call site asked it to wait forever (build 2792)

Started as a forum question about the mailbox `sleep()` loop — *"why not wrap the sleep in a
`createThread()` or use `createTimer()`, that would prevent CE's GUI from becoming unresponsive?"*
The answer for **that** loop is no change: it already pumps with `processMessagesPaintOnly` (~0.00 ms,
and deliberately re-entrancy-proof because it dispatches no mouse or keyboard), and it has to stay
synchronous because the AA script needs the return value before it can decide whether to untick the
record — going async would re-open the exact "the checkbox lied" defect the build-2743 sweep closed.

But reading CE `7.5-195` to answer it found that **`executeCodeEx` is a different animal**, and one
of our call sites was standing in the worst possible spot.

**The CE facts** (now owned by [ce-plugin-sdk-notes.md](ce-plugin-sdk-notes.md) §13):

- `executeCodeEx` (LuaHandler.pas:11922) is a shim over `executeMethod` (:11417), whose wait is
  `CreateRemoteThread` (:11847) then `WaitForSingleObject(thread, timeout)` (:11861) — **on the
  calling thread, with nothing on that path pumping messages.** From an AA `{$lua}` block the
  calling thread *is* CE's GUI thread, so the timeout is a ceiling on GUI-freeze time and a
  Lua-side pump structurally cannot reach it (it only runs after the call returns). `executeCode`
  (:11943) carries a duplicate wait at :12056.
- **A `nil` timeout means `INFINITE`** (:11504-11505), not "use a default".
- Failure returns **`nil` plus a reason string**, six distinct ones (`'Execution timeout'`,
  `'Failure launching thread'`, `'Wait failure'`, `'Failure reading the result address'`, two arity
  messages) that point at six different problems.
- A **timeout does not reclaim**: `dontfree := true` on the `WAIT_TIMEOUT` branch (:11880) makes the
  `finally` (:11907) skip `VirtualFreeEx` on the stub, the result address and every string
  allocation — permanently, in the *target* process. Deliberate (the remote thread may still be
  running), but it means "just shorten the timeout" is not a free move. The same flag on
  `timeout=0` (:11851) is the source-level mechanism behind celua.txt's leak note.

**What was wrong here.** [`scripts/ue5_dissect.lua`](../scripts/ue5_dissect.lua) passed
`executeCodeEx(1, nil, fn, ...)` — infinite, unpumped, **once per field** of a class walk. A
suspended target or a faulting stub froze CE with no UI-level recovery short of killing the
process. It was also the one `executeCodeEx` call site with no coverage in
`CeExecuteCodeExArityTests`, which is why it survived the audit #4 B1 sweep that fixed the others.

**And a defect we had already fixed once, at a new address.** All three call sites — the dissect
helper, `CeLuaHygiene.AppendCallDllHelper`, and `UE5CEDumper.CT`'s `ue5_callDLL` — checked only
`result ~= nil` and then printed a message they had guessed. The `.CT`'s was
`"executeCodeEx returned nil for %s — process alive?"`, and **four of the six reasons occur with a
perfectly healthy process**. This is the same shape as the mailbox-timeout defect build 2743 fixed
("the message blamed the wrong thing, and the mailbox already held the answer") — the diagnosis was
sitting in a return value nobody read.

**Shipped:** finite `DLL_CALL_TIMEOUT_MS = 5000` in the dissect helper; `local ret, why = …` at all
three sites with CE's reason surfaced; `AppendCallDllHelper` split into two branches because
`pcall`'s second return is the Lua error on a raise and the callee's RAX on a clean run — one
`if not okCall or ret == nil` could not tell them apart. `CeExecuteCodeExArityTests` gained a
dissect test (finite timeout + reason capture) and reason-capture assertions for the other two.
3520 C# tests green.

> **A note on the test that caught itself.** The new `.CT` assertion
> `DoesNotContain("process alive?")` failed on first run — against the *comment* explaining why that
> string was removed. It now checks code lines only. Worth remembering when pinning the absence of a
> string that the fix's own documentation will quote.

-----

## 2026-08-10 - A generous `gc.MaxObjectsInGame` made us enumerate 11.7% of GObjects and call it a full scan (build 2782)

Reported as "Value Search can't find a Native-C value": a live `DsClientLocalPlayer` field at
`+0xAC0` = 9338 was visible in Live Walker's Guess-What but no scan would return it. Native-C was
not at fault, and neither was any scan — **the object was never enumerated**, and neither was any
other runtime object in the process.

`find_by_address` settled it in one call: the live object resolved `index: -1, match_kind:
"backward"` (recovered by walking back to a UObject header) while its CDO resolved `index: 36968,
match_kind: "exact"`. Every instance any tool could find was a `Default__*`. `get_object_count`
returned **37,099** — the same 37,099 it returned 78 minutes and one map earlier. A live
`FUObjectArray` never holds still; that number was not a count of anything current.

Reading the array header confirmed it. `NumElements` (+0x24) was **317,810** and climbing (260,799
at scan time, `NumChunks` 4 → 5); 37,099 is **`ObjLastNonGCIndex`** (+0x04) — the frozen high-water
mark of the startup disregard-for-GC set, which is precisely why everything enumerated was a CDO or
a startup asset.

**Two defects, stacked, either one alone sufficient to cause it.**

1. **The MaxElements sanity cap was a live-object-count intuition applied to a capacity field.**
   `maxElements > 0x800000` (8,388,608) rejected the *correct* preset, because MaxElements is
   `MaxChunks * 64K` and tracks the title's `gc.MaxObjectsInGame` ceiling, not its population:
   161 chunks → **10,551,296**. Every strict preset then failed and both validators fell to their
   relaxed tier. Raised to 33.5M (`kMaxElementsCeiling` / `kStrictMaxCeiling`), still an order of
   magnitude under the 233M that MindsEye's pointer-half produced — which is what the bound is for.
2. **In the relaxed tier, row B and row E are indistinguishable on the only structural check.**
   Both read Objects from **+0x10**, so the pointer + cyclic-class-chain checks cannot separate
   them, and B is listed first. On a UE5-Extended array B's `numOff` (+0x04) lands on
   `ObjLastNonGCIndex` — in range, plausible, and wrong forever. The discriminator is B's own chunk
   counts (`MaxChunks@+0x08` / `NumChunks@+0x0C`): real on a genuine B layout, but on UE5-Extended
   +0x0C is `OpenForDisregardForGC`, a bool reading 0 once startup finishes. `Genau` now runs the
   relaxed table in two passes (chunk-consistent first, then the historical pass verbatim);
   `Aura`'s hand-written B block gained the same guard.

Both fixes are strictly **widening or re-ordering** — nothing that resolves today stops resolving.
Deliberately *not* a reorder of B and E: putting E first would let E's +0x20/+0x24 reads steal a
real Back4Blood layout, trading one silent misread for another.

**The shape worth remembering:** a validator that rejects the right answer does not fail loudly —
it falls through to a wrong answer that looks healthy. `relaxed B, Num=37099` was logged as a
success and copied into [test-games.md](test-games.md) as a normal result. The tell was never in
the validation log; it was that a live counter never moved.

**Verified in-game the same day (build 2786).** `ValidateGObjects: Valid at 0x7FF62529F8C0 (preset
Default, Num=266614, Max=10551296, …)` + `Layout 'Default' detected (strict, preset 1/5)` — the
**strict** tier accepting `Max=10551296` is impossible under the old 8.4M cap, so that line alone
proves the ceiling fix. Live count **266,614 at init → 274,900** later in the session, and the
original repro returns `DsClientLocalPlayer.<raw@0xAC0>` = 9338.

One honest gap, recorded in [todo.md](todo.md): that run resolved the **`ObjObjects`** anchor
(`GOBJ_V13` → `…F8C0`), not the `FUObjectArray` **base** (`GOBJ_ES53_1` → `…F8B0`) the broken run
used — and at the ObjObjects anchor even the old relaxed `A/C` row reads the right `NumElements`.
So defect 1 is proven and defect 2 is verified only by construction. That the same binary, at the
same module base, resolves a different pattern and a different anchor between two runs is itself
worth knowing: which preset row has to be correct is not a fixed property of a title.

-----

## 2026-08-10 - Proxy deploy stopped refusing the proxy it recommends, and stopped hiding why it failed (build 2779)

A newly-installed game (DragonSword Awakening) reported `Deployed: 0 success, 1 failed` with a row
reading **NotDeployed and a blank Error**. Nothing in the panel said why. Two independent defects,
one of which had been silently wrong since it was written.

**1. The import-table refusal was built on a false premise, and it was wrong.**
`DeployAsync` hard-refused any proxy flavour absent from the .exe import table, on the reasoning that
it "would never load". The refusal's own worked example was Octopath Traveler, described in the code
as importing winmm and dxgi *but not version.dll*. **Octopath's import directory names VERSION.dll**
— the premise was a misreading. (Octopath's real quirk is unrelated and already documented elsewhere
in the tree: it instant-exits under the *dxgi* proxy, because dxgi is imported early enough that the
game calls it before the CRT is initialised.)

Measured before changing anything — 21 Steam UE games on the maintainer's machine:

| | |
|---|---|
| games running a **working `version.dll` proxy with NO static import** | **11 of 21** |
| among them | DQ7R, P3R, Stray, Palworld, Manor Lords, Ghostwire Tokyo, both DQ HD-2D remakes, Lushfoil, Arms of God, The Artisan of Glimmith |
| strongest single case | **DQ7R** — the DLL itself reported the proxy load (the "confirmed working" suggestion) |
| games importing dxgi | 21 of 21 — which is why escalating to dxgi on "version not imported" is not a fix |
| our four names in `KnownDLLs` | **none** — the one condition that would make exe-directory proxying impossible |

The mechanism the refusal missed: `version.dll` / `dinput8.dll` arrive via a **run-time
`LoadLibrary`**, and the default search order reaches the .exe directory before System32. An import
proves a proxy WILL load; its absence proves nothing. The refusal therefore rejected `version.dll` —
the broadest-compatible flavour, and the one the Suggested column recommends — on most games,
telling the user to "try the Suggested column" that pointed straight back at it.

Now advisory-only (`ProxyImportAnalyzer.DescribeLoadRisk`). Nothing blocks a deploy. The real lesson
survives: a proxy that genuinely cannot load fails **silently and totally** (zero log, reads exactly
like "nothing happened"), so the one case still worth reporting — a static-import-only flavour
(dxgi/winmm) the game never names — rides along with the successful deploy as a note that names the
alternatives. `Recommend()` was deliberately **not** changed: with 21/21 importing dxgi, escalating
on that signal would trade a working default for Octopath's crash.

**2. A post-operation refresh erased the operation's own result.**
`DeploySelectedAsync` / `UndeploySelectedAsync` / `UpdateAllAsync` each ended with
`RefreshDeployStatusAsync`, which recomputes purely from disk. For a game the deploy just failed on,
disk says "file absent" → `ClassifyAbsentSelected` honestly returns `(NotDeployed, null)`, overwriting
both the failure status and its reason. That is the blank Error column: the reason existed only in
the log. This hid **every** deploy failure mode, not just this one. `RefreshDeployStatusAsync` now
takes a `preserveBinariesDirs` set (`ShouldApplyRefresh`, pure + tested, case-insensitive because it
compares Windows paths); a standalone Refresh still passes null and recomputes everything.

11 new tests (3519 C# green). All 11 were negative-controlled: reverting each fix fails exactly the
tests that cover it, and no others.

**In-game outcome, same day — and it cuts both ways.** On DragonSword Awakening itself the
`version.dll` proxy **does not load**: zero log folder, the silent-total-failure signature. `dxgi.dll`
works, and the game then runs perfectly (UE 5.4, GObjects/GNames/GWorld/Sparse/**&GEngine slot** all
resolved, ProcessEvent hook validated at 9 540 fires/1500 ms, no errors — see
[test-games.md](test-games.md)). So the old refusal's *verdict* on this one game was right, while its
*rule* was still wrong: applied consistently that rule also rejects the 11 games where version.dll is
the only thing that works, and it cannot tell the two groups apart, because the import table does not
distinguish them. Both groups import dxgi+winmm and not version. What separates them is whether
anything in the process ever calls `LoadLibrary("version.dll")` by bare name — invisible to static
analysis. Hence advisory, not refusal: the user tries, and **"no log folder appeared" is the
diagnostic**. That symptom is now documented in both READMEs.

-----

-----

## Older entries

Builds **2220–2747** were moved to
[`archive/dev-log-2026-08-pre-build-2779.md`](archive/dev-log-2026-08-pre-build-2779.md)
on 2026-08-25 (nothing edited, only moved). Everything before that is in the four older
archives that file links.
