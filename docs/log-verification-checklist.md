# Log-sweep verification checklist

Companion to [the verification register](verification-register.md).
That section says **what** is unproven and what would count as proof. This says **where to look**
and **what to do first**, so one game session settles as many of them as possible.

> **Grep the FORMAT STRING, never a line number.** Every marker below is quoted verbatim from
> source, with its `printf` placeholders intact — search for the literal prefix. A survey of these
> same markers in 2026-08 came back with correct strings and **every Genau line number stale by
> 12–14**, purely from unrelated edits. Line numbers rot between sessions; the strings do not.

---

## 0. Before you grep

### There is no log level. Nothing is filtered.

`Sein::WriteLog` takes `level` only to interpolate it into the line
(`"[%s] [%s] [%s] %s"` — timestamp, level, category, message) and writes unconditionally. There is
no threshold variable, no `NDEBUG` guard on `LOG_DEBUG`, and no env var / ini / pipe command that
changes verbosity. The UI is the same (`MinimumLevel.Debug()`).

**So `[DEBUG]` lines are first-class evidence.** If a marker is missing, the cause is one of:
the code path did not run · the module logs nothing at all (§4) · the process never opened its
files. It is *never* "the level was too low".

### Which files

`%LOCALAPPDATA%\UE5CEDumper\Logs\<GameName>\` — `<cat>-0.log` is the **current** run; the previous
run is archived as `<cat>-YYYYMMDD-HHMMSS.log` stamped from *its own* mtime.

| File | Categories |
|---|---|
| `init-0.log` | `INIT`, `CEP`, **`SUMMARY`**, **+ every unmatched category** |
| `scan-0.log` | `SCAN`, `SCAN:GObj/GNam/GWld/Ver/Eng/Sparse`, `MEM` |
| `offsets-0.log` | `DYNO*`, `OARR`, `FNAM` |
| `pipe-0.log` | `PIPE`, `PIPE:*` |
| `walk-0.log` | `WALK*`, `FLY` |

> ⚠ **Four categories fall through to `init-0.log`.** `ResolveFile` ends in
> `return LF_Init;  // fallback: unknown categories go to init.log`, and `s_catMap` has no entry for
> `SEETHRU` (Schlacht / See-Through), `Grausam` (Foreground Lock), `SENSE`, or `PROXY`.
> **All See-Through and Foreground-Lock evidence is in `init-0.log`**, not `walk`/`pipe`.
> `Grausam` is also the only **mixed-case** category — grep case-sensitively or you will miss it.

UI logs live in `Logs\UE5DumpUI\{init,pipe,view}-0.log`. The `ui-*.log` files inside the *game*
folder are a **mirror that only starts at connect** — everything before that (startup, DLL deploy,
injection) exists only under `Logs\UE5DumpUI\`. The two folders can also be from different
sessions; check timestamps before correlating.

> ⚠ **The mirror also STOPS early — never read it as "the UI did nothing".** Measured on Elliot
> 2026-08-03: `ui-view-0.log` carried the connect-time lines (`Bookmarks loaded`, `SnapshotStore:
> active DB`) at 20:10:31 and then **nothing at all**, while `Logs\UE5DumpUI\view-0.log` held a
> full 40-second Live-Walker navigation trace from 20:10:41 to 20:11:07. Same for `ui-pipe-0.log`,
> which ended at the connect handshake while the DLL's own `pipe-0.log` went on receiving
> `walk_instance` commands. Reading only the game folder makes an active session look idle.
> **`Logs\UE5DumpUI\` is the source of truth for every UI-side claim** — the mirror is a
> convenience copy, not evidence of absence.

### ⚠ The classification below assumes CE / manual injection

Under **proxy-DLL deployment** the DLL starts the pipe server and does **not** scan:

```
DllMain ProxyStart: proxy DLL mode — starting pipe server only (no scan)
```

`Genau::FindAll` then runs only when you click **Start Scan**. So every "passive" startup marker in
§1 becomes deliberate-ish under proxy: *do one scan*. Grep that line first to know which mode the
session was in.

---

## 1. PASSIVE — free evidence, no special action

Do nothing unusual. Inject, let the scan finish, play normally, then grep.

| # | Grep for | File | Confirms |
|---|---|---|---|
| P1 | `FindAll: PE hash = ` | `scan` | Which build the rest of the log describes. Record it. |
| P2 | `FindAll: Complete — GObjects=` | `scan` | The resolved global pointers **and which method won**. The single highest-value line. |
| P3 | `=== ` … ` patterns tried, ` | `scan` | Per-target pattern sweep + winner. Numbers here are the before/after comparison for **[Genau ModR/M]**. |
| P4 | `Hint HIT: ` / `Hint MISS: ` | `scan` | Hint cache working (2nd+ launch of the same game). A **MISS** on a game that used to HIT = that signature has gone dead. |
| P5 | `[SUMMARY]` | `init` | Build / UE version / the three globals / DynOff validation / UStruct+FField+FProperty offsets. Six `LOG_SUMMARY` call sites, but two are the arms of one `if/else`, so a completed init emits **five** lines. No category field. Answers **[FPROPERTY_FLAGS]** on sight. |
| P5a | `FCName=` | `init` | ⚠ **The token is `FCName=`, not `FFieldClass::Name=`.** It rides the P5 FField line and is the probed `FFieldClass::Name` offset — `+0x00` up to UE 5.7, `+0x08` from 5.8 (5.8 made `~FFieldClass()` virtual), which is also the **UE 5.8 version marker**. The repo's other three sites spell it `FFieldClass::Name=` (`Genau.cpp`, `Ubel.cpp`), so the natural grep silently misses the one line that is actually in the log. ⚠ `+0x00` is **four-way ambiguous** — genuinely ≤5.7, or the probe gave up (two different ways), or UProperty mode — so read `fallback_reason` / the `validated` flag on the same summary before drawing a conclusion. |
| P6 | `DataScanGObjectsCandidates: ` | `scan` | **[Genau ModR/M]** candidate counts — see §5. |
| P7 | `FindGObjectsStaticStruct: ` | `scan` | Same, but ⚠ **only reached when `Aura::GetCount() == 0`** — a normal healthy session never runs it. Absence here is expected, not a finding. |

**[Genau ModR/M] acceptance** (needs *two* sessions, same game, same build — DLL before and after):
P3/P6 counts should go **DOWN**; P2's resolved addresses must be **byte-identical**. A changed
address is the regression; a lower count is the win. **Do not use `sweep.sh`** — it structurally
cannot see these three sites, so a clean sweep would read as "no regression" when it means "not
measured".

> ⚠ Do **not** grep for `if ((b2 & 0xC0) != 0x00)` to confirm the fix is applied — it was refactored
> into `Macht::IsRipRelativeModRM` and that literal has **zero hits**. Grep `IsRipRelativeModRM`
> (5 call sites in `Genau.cpp`).

---

## 2. DELIBERATE — do this in-game, then grep

> ⛔ **STATUS AUDIT 2026-09-06 — MOST OF THIS SECTION IS ALREADY CLOSED. READ THIS TABLE FIRST.**
> This file is a **method** doc: it says *how* to check a thing, and it deliberately carries no
> ⬜/✅ column. That is exactly why it reads, four months on, as if none of it had been done — it
> contained **zero** references to a closure tag. It is not a queue. Do not plan a session off it
> without checking the register.
>
> | § | subject | state |
> |---|---|---|
> | **A** | See-Through, audit M1/M2/M3 | ✅ closed. All four disable arms passed — (c)+(d) 2026-08-22, **(a)+(b) 2026-08-23** `[SEETHRU-ARMS-AB-2026-08-23]`. ⚠ The 繁中 file's *own* arm (a) — "close the game **while** moving" — is a different question and is still open, but is **not** human-gated. |
> | **B** | Solide hold / Tot latch zombie, audit M4 | ✅ closed `[SOLIDEHOLD-2026-08-22]` |
> | **C** | 256 pool-truncation badge (build 2531) | ✅ closed by the same tag, which covers "the 256 badge, the release, and the anti-zombie control" |
> | **D** | GodMode worker lifecycle, audit L1 | ✅ closed `[L1-GODRACE-2026-08-23]` — register:10075, *"DLL LOW L1 — PASS"* |
> | **E** | UFunction invoke / ProcessEvent | 🟡 **the only one with work left** — `[PEHOOK-LIVE-2026-08-20]` (steps 1b/2/3/5/7) and `[PEHOOK-6-2026-08-20]` closed; two steps remain under `[PEHOOK-2026-08-17]` |
> | **F** | Dump Explorer cross-game gate (2538) | ✅ closed `[DUMPXGAME-2026-08-22]` — the gate was already pinned by `DumpExplorerTests`; only its diagnostic was not |
> | **G** | Value Search `V1a` / `NumericAll` | ✅ **VERIFIED 2026-08-05 on DumperTest** — both had been ⬜ since builds 927 and 796 respectively, and the sample closed them along with B28 and V1c |
>
> ⭐ **Everything in §2 is closed except two steps of E.** M1–M5 as a whole is complete — the
> register states *"steps 1, 2, 3, 4 and 5 all closed"*.
>
> ⚠⚠ **AND HERE IS THE TRAP, COMMITTED WHILE WRITING THIS VERY TABLE:** D and F were first written
> up as *"no closure found"* because only `verification-register.md` had been grepped. Both are
> closed — D in `auto-verification-classification-2026-08-23.md`, F in `docs/archive/`. **A closure
> is recorded where the work happened, not where the row lives.** Always `grep -rn` the whole of
> `docs/`, archive included, before concluding anything here is open.

### A. See-Through / Schlacht — audit M1 / M2 / M3
**Do:** enable See-Through, then disable it **four different ways** — (a) while moving,
(b) while the game is paused / the game thread is stalled, (c) by yanking the UI connection,
(d) by closing the game.

**Then:** in **`init-0.log`** (not walk!) grep `SeeThrough: `.

- `SeeThrough: disabled but %zu actor(s) remain hidden` → **M1/M2 not fixed**
- `SeeThrough: gave up waiting for the game thread` → the stall path was exercised

**The real check is on screen:** after every one of the four, *every hidden actor must be visible
again*. One actor left invisible is the failure and no log line will say so.

### B. Solide force-field hold — audit M4 (Tot latch zombie)
**Do:** start a hold (Property Search ▸ Force), **disconnect the UI mid-hold**, reconnect.

**Then:** in `pipe-0.log` grep `FindInstancesByClass class='<your class>'`.

Solide's re-assert worker calls this every 300 ms, and it logs unconditionally — so this is a
**positive liveness detector**:

- line keeps appearing ~3×/s across the disconnect → hold survived, **M4 fixed**
- line stops → the job was zombified

⚠ `get_forced_fields` still **lists** a zombie job, so checking the list is not enough. Also read
the value in CE.

### C. Solide pool-truncation badge (build 2531)
**Do:** hold a field on a class with **>256 live instances** — projectiles, crowd NPCs,
destructible props. Most gameplay classes never reach the cap, which is why this went unnoticed.

**Then:** **no log marker exists** (confirmed: the flag is set with no `LOG_*` on that path). The
evidence is on screen — a `⚠ capped` badge beside `(256 held)` and a status line ending
`cap reached, more exist unheld`. Then hold on a *small* class and confirm neither appears.

Secondary: with the pool capped, **Reset** must still restore cleanly (the base-prune guard is
skipped while truncated) — no field left stuck at the forced value.

### D. GodMode worker lifecycle — audit L1
**Do:** toggle GodMode on/off rapidly, alternating the UI button and the CE mailbox.

**Then:** in `walk-0.log`, grep `GodMode: re-assert worker start` and `worker stopped`.
Both are logged from *inside* `WorkerLoop`, so they are per-thread-instance:
**two `started` with no `stopped` between them == the L1 orphan-worker bug.**

### E. UFunction invoke — [build-648 ProcessEvent] and [static-native fast path]
**Do:** invoke any UFunction from the Live Walker. (This is strictly deliberate — the validator
only runs from the invoke path / `UE5_EnsureGameThreadHook`, never on its own.)

**Then:**
- `GameThreadDispatch: validation OK — hook fired` in `walk-0.log` → the PE hook is on the right
  slot. Its **absence after an invoke** is the wrong-slot signal.
- `invoke_function: ` … `direct=` in `pipe-0.log` → `direct=1` is the **static-native fast path**,
  `direct=0` went through game-thread dispatch. Invoke one static-native and one instance method to
  see both.

### F. Dump Explorer cross-game gate (build 2538)
**Do:** three loads — (1) this game's own dump with this game connected, (2) *another* game's dump,
(3) an older dump of this game taken before a patch.

**Then:** `DumpExplorer live match refused: dump module ` in `Logs\UE5DumpUI\view-0.log` — must
appear for (2) **only**. Case (1) is the regression risk: a false refusal there breaks the feature's
main use, and it is silent in the log (no line = matched).

### G. Value Search — [V1a] TSet/TMap and [NumericAll]
**Do:** V1a — scan a value in a `TSet<int>` / `TMap<K,int>`; force a container **reallocation**
between scans and confirm it degrades rather than reporting a wrong hit.
NumericAll — scan a value that really lives in an `Int8`/`ByteProperty`.

**Then:** on-screen only — `Set[idx]` / `Map.Key[idx]` / `Map.Value[idx]` row shapes, and the orange
result-volume warning for NumericAll. `Radar.cpp` emits **zero** log calls, so there is no log
evidence for any Value Search behaviour.

---

## 3. Markers whose ABSENCE proves nothing

Traps. Do not read a missing line here as a negative result.

| Marker | Why absence is meaningless |
|---|---|
| `Experimental features: ` | Has exactly **one** caller in the DLL, deep inside `TryObfuscatedPool`, reached only after *both* stock FNamePool layouts are rejected. On a normal game it never runs — **with experimental fully enabled**. It also has nothing to do with the UI's experimental gate (See-Through / Force / Stealth), which is enforced C#-side. |
| `FindGObjectsStaticStruct: ` | Only reached when `Aura::GetCount() == 0`. A healthy session skips it. |
| Linie periodic summary (`~%.0fms cv=`) | Gated on `meanPeriodMs <= 30000`. Audit **L5**'s underflow bug produces ~1.8e19 ms, which is *filtered out* — so a poisoned function can never appear. This line cannot detect L5. |
| `SeeThrough: ` in `walk-0.log` | Wrong file — `SEETHRU` falls through to `init-0.log`. |

---

## 4. No log evidence exists — needs eyes, or a non-event

These modules emit **zero** log lines: `Linie`, `Radar`, `Denken`, `Tot`. (`Sense` emits one.)

| Item | What to do instead |
|---|---|
| **Solide truncation badge** | On-screen badge only (§2C). ✅ **closed** `[SOLIDEHOLD-2026-08-22]`. |
| **Audit M5 — `UE5_Shutdown` join order** | A **non-event**: with a hold active, close the game while connected → no hang, no crash. There is no positive line; "nothing bad happened" is the whole result. ✅ **closed 2026-08-24** `[M5-JOINORDER-2026-08-24]`. ⚠ Its recipe was wrong in one word: *"assert sub-second exit"* — UE's own teardown is ~1.5–1.7 s here with or without a hold, so a literal reading files a UE property as an M5 defect. What was measured is hold vs no-hold (1.612/1.334 vs 1.717/1.650 s — a hold is no slower). |
| **Audit L12 — Fern `str_params` leak** | Genuinely unreachable from the normal UI (needs a mid-loop JSON `type_error`). Not checkable in a play session — drive it from a crafted pipe client. ✅ **closed 2026-08-23** `[L12-STRLEAK-2026-08-23]`, which is exactly how it was done. |
| **[V1c] `TOptional<T>` scan** | ⛔ **STALE — do NOT go looking for a game.** ✅ **VERIFIED 2026-08-05 on DumperTest** (⬜ since build 942). The sample exists precisely so this is not a hunt: `Opt_Int_Set` / `Opt_Float_Set` / `Opt_Str_Set` / `Opt_Int_Unset` are declared in `DumperTestActor.h`, and UHT emits each as `FGenericPropertyParams` plus an `_Inner`. |
| **[Verify Return Value] diagnostic** | Read the invoke result grid on screen. |

---

## 5. Minimum session that settles the most

> ⛔ **AS OF 2026-09-06 THIS RECIPE SETTLES ALMOST NOTHING — steps 3–7 are all closed** (see the
> §2 status table). It is kept as the shape of a good session, not as a work list. The only piece
> with anything left in it is **step 3 / §2E**, and only two of that row's steps. If you want a
> session that still buys something, take it from `docs/todo.md` or the register, not from here.

1. Note the deployment mode (`proxy DLL mode` present or not). Under proxy, click **Start Scan**.
2. Let the scan finish → §1 P1–P6 are already in the bag (**Genau ModR/M**, **FPROPERTY_FLAGS**,
   hint cache, and a record of which signatures won on this build).
3. Invoke one UFunction → §2E (**ProcessEvent**, **fast path**).
4. Enable/disable See-Through the four ways → §2A (**M1/M2/M3**).
5. Force-field hold + disconnect/reconnect → §2B (**M4**).
6. GodMode on/off rapidly → §2D (**L1**).
7. Load three dumps in Dump Explorer → §2F.

Steps 1–2 are free. Everything after is a few minutes each. Update the ⬜ boxes in
[verification-register.md](verification-register.md) as you go — this file is the
procedure, that one is the status.
