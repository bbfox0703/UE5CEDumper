# Live-game verification register

**Everything shipped but not yet proven against a running game.** One row per check, each with its
own acceptance test. This is a standing queue, not a todo — the work is already *written*; what is
owed is the *proof*.

> Split out of [`todo.md`](todo.md) on 2026-09-03 at build 3369, where it had grown to **10,506 of
> 13,143 lines — 80% of a file whose stated charter is "Open work only"**. todo.md now answers
> "what should I build next"; this file answers "what have we not confirmed yet". Sections were
> moved **byte-identical**; nothing was rewritten.

-----

## ⭐ Why this register exists at all — read this before proposing to delete it

A row here is not bureaucracy, and it is not something a manual play-test can replace. The reason,
in the maintainer's words (2026-09-03):

> The problem we kept hitting is that **only this way can you tell which side is at fault, the DLL
> or the UI.** Testing by hand only shows you the UI — and the UI does not necessarily reflect
> reality, because it might receive the DLL's message and not act on it, or the DLL might never
> have sent the data in the first place.

That is the whole justification, and it is structural rather than a matter of thoroughness:

- **A green UI proves nothing on its own.** The panel showing a value is consistent with the DLL
  being right, and equally consistent with the UI rendering a stale cache while the DLL returns
  nothing. Both look identical to a human watching the screen.
- **A silent UI is equally ambiguous.** An empty grid can mean the DLL sent no rows, or that it sent
  rows the UI dropped. This project's own history has both: `walk_world` returned
  `actor_count: 0` for months (audit #5 F8/F9 — `ULevel::Actors` carries no `UPROPERTY`, so the
  reflected lookup could never match), and separately a sorted DataGrid rendered stale rows from a
  recycled container (`[GRIDRECYCLE-2026-08-21]`).
- **The rows disambiguate by construction** because each one names the observable on BOTH sides —
  the pipe payload or DLL log line, *and* what the UI must then display. That pairing is what a
  play-test cannot produce.

⚠ So the failure mode to guard against is not "this register is too long". It is **closing a row on
UI evidence alone**. If a row's acceptance test names only what the screen shows, it is
under-specified — fix the row, do not pass it.

ℹ️ Whether **Auto + Computer Use** can drain rows unattended is a separate question, deliberately
left open here (2026-09-03) and to be analysed once the docs settle. `docs/log-verification-checklist.md`
already covers the cheaper posture — sweeping a real session's logs opportunistically — and
`docs/pending-verification_zh-TW.md` holds the residue that genuinely needs a human.

-----

## How to enumerate this register

```
awk '/^## Pending live-game verification/,0' docs/verification-register.md   | awk '/^## /&&!/^## Pending live-game/{exit}1' | grep -c '^### ⬜'
```

⚠ **Scope the count to the register's own `##` section — a whole-file `grep -c '^### ⬜'` is WRONG
and answers 11, not 9.** The extra two live in the 繁中 step sections further down, which are a
different queue. I introduced that wrong one-liner in this file's first draft (2026-09-03) and it
disagreed with the correct command 190 lines below it — two self-enumeration commands in one file
giving different answers is exactly how the count in `todo.md` drifted to a stale 43 / 36 / 40 / 30
in turn. Derive it, never hand-edit it, and keep ONE command.

⚠⚠ **A heading here is NOT evidence** — the single most expensive property of this material, and it
travelled with it. Closures are recorded under their own `✅ … [TAG-YYYY-MM-DD]` block, often
thousands of lines from the row they close, and the original `⬜`/`🟡` heading is **not** updated as
a matter of course. Before planning off any row, `grep` its finding ids across the whole of `docs/`.
Tags have always resolved across files, so the split changed nothing about the lookup.

⚠ `tools/check_live_verification.py` reads THIS file: every `(key: X)` carried by an
"In-game verification pending" caveat in `roadmap.md` must appear somewhere below.

-----

## Pending live-game verification (verify only — no code)

> 📦 **84 closed sections (6,247 lines) were archived 2026-08-23** to
> [archive/todo-closed-2026-08-23-build-3337.md](archive/todo-closed-2026-08-23-build-3337.md) —
> moved verbatim, nothing rewritten. ⭐ A `✅` heading was **not** sufficient: 13 `✅` sections
> whose bodies still said *"still owed"* / *"still open"* were **kept here**, so this register
> remains the complete list of what is not finished. See that file's header for the exact rule.

> **Session evidence tag `[ELLIOT-2026-08-16]`.** Three launches of **Elliot** (`Elliot-Win64-Shipping`,
> UE5.4 runtime-reconciled, PE `6A577F4E1D91B000`, 482,390,784-byte image) on 2026-08-16 — 20:12
> (`scan #1`, cold), 20:26 (`scan #4`) and 20:49 (`scan #7`, build **3127**). **This is the most
> productive single evening on this register**, because Elliot is the *stripped-PE-version* title
> that DSA structurally could not stand in for, and because three launches of one binary give
> cold-vs-warm pairs with **two unhinted targets acting as a built-in negative control**. It closes
> G10 steps 1 and 3, G8/G9 steps 1 and 2, G11 step 1, and G2 step 1 — and it produced one honest
> non-result: G2's speed is real but **2.4 s, not the predicted sub-second** (step 2, with a lead).
>
> **Session evidence tag `[DSA-2026-08-16]`.** A real session on **DragonSword Awakening**
> (`DSClient-Win64-Shipping`, UE5.4, PE `691B0D9809EB2000`) under **build 3122** settled a few steps
> below; each is ticked in place and tagged, so grep `DSA-2026-08-16` for everything it covered.
> **Read what it did NOT reach as carefully as what it did** — the session detected its version from
> the intact **PE VERSIONINFO**, so the whole memory-string tier ladder (G2's 29 s sweep, G8/G9/G11's
> tier rules) was never entered, and it resolved offsets via `Guid`, so G12's actual repaired branch
> was never entered either. A green session is not the same as an exercised code path.

> ### ✅ DONE 2026-08-22 — `[PARAMSSORT-2026-08-22]` click-through on the TRIMMED build
>
> Driven end to end on **`dist/UE5DumpUI.exe` v1.0.0.3313, the 54.7 MB Native-AOT binary**, against
> DumperTest (UE504, 25,179 objects, DLL 3313). Every header clicked twice.
>
> | panel | column | ascending | descending | does it DISCRIMINATE numeric from string? |
> |---|---|---|---|---|
> | Interesting Funcs | Params | `0 (0B)` ×8 | **19, 19, 19, 17, 17, 16, 15, 15** | ⭐ **YES** |
> | Console | Params | `0 (0B)` ×6 | `6, 4, 2, 2, 2, 2` | no — nothing exceeds 9 params here |
> | Live Funcs | Params | blank, blank, 1, 1, 3, 7 | 7, 3, 1, 1, blank, blank | no — same reason |
> | Live Funcs | **Period** | 17, 17, 33, 33, 33, 33 | exactly reversed | no — see below |
> | Detect Stats | **Offset** | **`0x28, 0x2C, 0x2C, 0x30, 0x30, 0x64`** | — | ⭐ **YES** |
> | Detect Stats | Result | all `· guess` | all `✓ confirmed` | n/a — bool |
> | Live Walker | Params | **not run** | | — |
>
> ⭐ **Two of the seven genuinely discriminate, and they are what makes the rest mean anything.**
> Interesting Funcs descending tops with `19 (224B)` — `FunctionalTestUtilityLibrary::
> TraceChannelTestUtil`, the 19-parameter function this register already named — where an ordinal
> comparer would top with `9 (…)`, because `'9' > '1'`. Detect Stats' Offset ascending starts at
> `0x28`; ordinal would start at `0x174`, since `'1' < '2'`. Both are unambiguous.
>
> ⚠ **The other four columns do NOT discriminate and must not be written up as though they do.**
> Console's parameter counts top out at 6, Live Funcs' at 7, and every Period value on a
> fixed-frame-rate host renders `17 ms` or `33 ms`. Single-digit and equal-width values order
> identically under both comparers. What those four rows DO establish is the other half of the row's
> ask, and it is the half that only the trimmed binary can answer: **the header is live, it sorts,
> and a second click reverses.** That is the AF20 failure mode — a header that animates and does
> nothing — and it cannot be reproduced in a JIT test host.
>
> ⚠ **Live Walker's Params was not re-run**: its function grid's entry point was not found from the
> panel within a reasonable time (not under Options, not under Find Refs). It is the one column of
> the four the row names that was **already correct before this fix** and already verified by AF20,
> so nothing is riding on it — but it is untested *today* and this says so rather than implying a
> clean sweep.
>
> ✅ **Two things fell out of the same session:**
>
> 1. **`[CADENCEGAP-2026-08-22]` confirmed in the shipped UI**, which no unit test reaches. Live
>    Funcs showed `CameraModifier::BlueprintModifyCamera` at **496 calls / 17 ms** beside
>    `ABP_Manny_C::BlueprintUpdateAnimation` at **248 calls / 33 ms** — twice the calls, half the
>    period, arithmetically consistent. Before the fix both read **33 ms** while their call counts
>    differed 2×.
> 2. **The Detect Stats header really does read `Result`.** The 繁中 row called it "✓"; ✓ is cell
>    content. Corrected in the mirror, now confirmed on screen.
>
> ℹ️ No duplicate-record rows appeared after repeated sorting on any grid (the `supportsRecycling`
> addendum). These are XAML `{Binding}` cells, which should be structurally immune; observing it is
> weak evidence, and a failure would have been a new finding.

> ### 🔄 MIRROR RECONCILED 2026-08-19 — `pending-verification_zh-TW.md` pruned 46 → 40
>
> The maintainer worked the 繁中 checklist off a NAS copy and ticked **18 individual step rows** across
> four items; those ticks were folded in above (`AC1`, `AE4–AE7`, `A6`, `AD4`). A second, independent
> sweep then compared **every** 繁中 item against this register. **Six sections were removed from the
> mirror; three more were kept after the "closed" claim was refuted.** Recorded here because the
> mirror carries no history of its own — deletions there are only auditable from this side.
>
> | 繁中 item removed | ground | evidence in this register |
> |---|---|---|
> | `AC1` | ticked **6/6** *and* already closed | `✅ ALL SEVEN STEPS CLOSED 2026-08-17 [AC1-UI-2026-08-17]` |
> | `W2 / W3` | drift | `✅ UI HALF NOW CLOSED 2026-08-17 [SDKHDR-UI-2026-08-17] — all three checks pass`; that WAS the mirror's only remaining step |
> | `D2` (Group Scan scalar) | drift | `✅ Step 4 SETTLED 2026-08-17 [D2-UI-2026-08-17] — but its PREMISE was wrong`; step 4 was the mirror's only remaining step |
> | `AB1 / AB2` | drift | `✅ 5-of-5 CLOSED 2026-08-18`. Its APC sub-step is **not** outstanding — it is `⛔ CANNOT BE RUN ON A PUBLIC CHEAT ENGINE`, now carried as a D_MANUAL row in the session plan |
> | `D2`（樣本心跳） | drift | `✅ D2 (樣本心跳) PASS 2026-08-17 [GRP4-UI-2026-08-17]` + `✅ VERIFIED 2026-08-12` (all five HUD lines in the Shipping package) |
> | `G8 / G9` | drift | the mirror's single remaining step is this batch's **step 3**, `✅ PASS [DQ7R-PIPE-2026-08-17]` with the log quoted. ⚠ **The mirror also prescribed the WRONG HOST** — it said Elliot, which step 3's own correction shows can never emit a Tier 1 line |
>
> ⚠ **Two stale blockers died with those rows and must not be re-copied anywhere.** Both `W2/W3` and
> `D2` carried *"卡在 UE5DumpUI 無法授權給 computer-use"*. That is false since the all-users Start-Menu
> shortcut landed — the SDK-header export and the `Leaves/slot` control were both driven on the AOT
> `dist` binary. Any row still citing that blocker is out of date.
>
> ⚠ **Three sections were KEPT because the sweep's "closed" verdict did not survive checking** —
> logged because the cost of getting this wrong is deleting verification nobody has done:
> * **`U16`** — ✅ **now CLOSED 2026-08-22** (`[U16-ENUM65-2026-08-22]`); keeping it was the right
>   call, and this is what it was still owed. The parent is `✅ DONE 2026-08-18`, but step 5 inside
>   it was explicitly **🟡 PARTIAL**:
>   *"the largest table seen is 26 entries"* and *"the CE DropDownList half was not checked"*. Those
>   are precisely the mirror's remaining step 1. Only its step 2 (the `walk-0.log` grep) is discharged.
> * **`U3 / U17`** — the `✅ CLOSED 2026-08-17` covers steps 1–2, which the mirror had **already**
>   dropped. What it still lists is steps 3 (a UE5 **LWC** 24-byte `FVector` title) and 4 (the **GAS**
>   control); the closure ran on `Map_IntToVec3f`, a 12-byte float vector, so it cannot stand for either.
> * **`D2`（顯示配對） — the closest call.** `✅ VERIFIED in-game 2026-08-05` does cover the filter
>   pairing and `All fields` open/collapse, but the mirror's **step 1** (a non-zero default pair) was
>   *generated by* that session as a complaint and fixed **after** it — the write-up records the fix,
>   not a re-run — and its **step 4** (Live / Addr / Pivot / Locate off each leaf) appears only as a
>   design claim (*"act on it unchanged"*), never as an observation.
>
> **The mirror's own count table was re-derived, not edited by hand**: 第1步 1 · 第2步 16 · 第3步 8 ·
> 第4步 13 · 第5步 2 = **40**. CLAUDE.md's row said *63* and the session plan said *64 rows*; both were
> stale and are corrected in this commit.
>
> ⚠ **That `40` is a snapshot of that reconciliation, not a running total.** Audit **L10** added five
> items the same day and the four `[TAG]` items below were mirrored on 2026-08-19, so the mirror is
> larger now. **Never read a count out of this block — derive it**:
> `grep -c '^### ' docs/pending-verification_zh-TW.md` minus the two `###` under 「怎麼用這份清單」.

### ⛔ PRECONDITION FOR EVERY GAME ROW — as of 2026-08-19, ALL NINE deployed proxies are STALE

Measured with `tools/verify/proxy_refresh.py report` (build 3263, `dist/proxy` = dinput8 2,875,904 /
dxgi 2,876,928 / version 2,882,560 / winmm 2,889,216):

| game folder | proxy | deployed size | |
|---|---|---|---|
| EVERSPACE 2 · EVERSPACE · DQ7R · Lushfoil · Manor Lords · The Artisan of Glimmith | `version.dll` | 2,860,544 | stale |
| OCTOPATH TRAVELER | `winmm.dll` | 2,867,712 | stale |
| Avowed · Elliot | `dxgi.dll` | 2,855,936 | stale |

**9 deployed, 9 stale.** This is not cosmetic. A proxy auto-loads at game start and **owns the
pipe**, so injecting the current DLL afterwards is a no-op — the second instance logs
`pipe already exists (another UE5Dumper instance running) — skipping auto-start` and
`LoadLibraryW` merely bumps a refcount. Everything measured is then the OLD binary, silently.
`PipeClient.assert_build()` does catch it, but only after the launch has been spent.

⇒ **Refresh before measuring**: `py tools/verify/proxy_refresh.py refresh "<folder substring>"`,
which backs the old file up to `out/proxy-backups/` with size + SHA-256 verified before it
overwrites, refuses while the game is running, and refuses a needless write when already current.

⚠ **Correction to an earlier note: Avowed IS installed** (`…\common\Avowed\…\Avowed-Win64-Shipping.exe`).
It is simply absent from the Start menu, so `request_access` cannot resolve it and it is not
grantable for computer-use — but it is perfectly usable for any headless pipe/log row, which
matters for `A8` (flat-array CE pointer info) and `AA38` step 4's neighbourhood.

### ▶ HOW TO ENUMERATE THIS REGISTER — one invariant, and it is grep-able

**Every item's ID must appear in a `^###` (or `^####`) heading of this section.** A heading-level
scan is how anyone picks the next thing to run, so an item whose ID lives only in body prose is an
item that gets double-run or forgotten. Enumerate with:

`grep -n '^#\{3,4\} ' docs/verification-register.md` — then keep the lines that fall inside this section.

⚠ **`> ###` lines do NOT count, deliberately.** A blockquoted heading is an *evidence* sub-block — a
session result, a trap, a refutation — hanging under a real item, and `grep '^### '` cannot see it.
There are many and that is fine; what must never happen is an ITEM being introduced by one.

**Two blocks were violating this until 2026-08-19** and are the reason the rule is written down: the
build-2830 container group (**MG2 / TSet+UDataTable / U2**) sat under the `[SDKHDR-2026-08-18]`
heading, and the whole **"Shipped + unit-tests-pass but unproven on real games"** long tail sat under
`[STALEDLL-2026-08-18]`. No heading anywhere named a single one of their checks. Both now have their
own headings, and the headings that owned un-named items (the fourteen-MED batch, audit #4 ① and ②,
audit L10) carry their IDs. **Measured, not asserted:** cross-checking every ID that owns a 繁中
section against the register's `^###` headings went from **40 un-findable to 0**.

⚠ **Re-checked 2026-08-19 (closing sweep) and it had already sprung two small leaks**, both of the
same shape the rule forbids: two `### ⬜ Original checklist (kept for the steps)` blocks named no ID
at all, so a heading-level scan could not tell you *whose* checklist they were. They now read
`### ⬜ AE2 / AE3 — original checklist …` and `### ⬜ Y9 — original checklist …`, matching the
`U3 + U17` block that already had it right. **Re-derive with the two commands below and expect
`14` and `0`** — and as of 2026-09-03 this IS the machine check it asked to be:
`tools/check_derived_counts.py` carries `open_verification_batches`, so the number below and
`todo.md`'s copy of it now fail the build together if either drifts. It had drifted a third time
(this line still said `40`) and the gate caught it in the commit that added it:

```
awk '/^## Pending live-game verification/,0' docs/verification-register.md | awk '/^## /&&!/^## Pending live-game/{exit}1' | grep '^### ' | grep -c ⬜
```

⚠ **Two of those 40 hang under a parent that is already `🟡` or `✅`** (the two just renamed). They
are kept `⬜` deliberately — losing a live check is worse than over-counting by two — but a
`🔲`-marked sibling (`U3 + U17`) shows the other convention exists, and `🔲` is **not** counted by
the command above. Reconciling the three is a maintainer call, not an agent one.

⚠ **Un-mirrored `[TAG]` items — a known, tracked gap.** `PIPEBUSY` / `CLASSTOTAL` / `PROXYLOAD` /
`SLOTSYM` were mirrored into 繁中 on 2026-08-19. Still un-mirrored: `STALEDLL` (b), `FREEZESTUCK`,
`PASTECRASH`, `FREEZESCOPE`, `PEHOOK`, `PEHOOKONCE`, `SDKHDR`, `CONTAINERCAP`. They are **not**
exempt — `AUTOREFRESH` is already a full 繁中 section — they are simply behind. Mirror each as it is
picked up.





### ✅ FIXED + VERIFIED 2026-08-21 `[VOLUMEROOT-2026-08-19]` — and its "unverifiable" premise was wrong

**All three sites fixed, and the mount-point behaviour is DEMONSTRATED, not argued.** The row said
"only a real mount point can verify it", which is why it sat deferred. That was false, and the
correction is the more useful half of this entry.

**⭐ A cross-volume DIRECTORY JUNCTION separates the two answers exactly as a mount point does**, and
`mklink /J` needs no elevation and no spare volume. Measured on this machine:

```
junction  D:\...\out\xvol_junction  ->  C:\Windows\Fonts
Path.GetPathRoot equivalent : D:\        free 493.0 GB / total 1863.0 GB
GetVolumePathNameW          : C:\        free 836.3 GB / total 1881.4 GB
```

Two volumes, two genuinely different sizes, one path. `GetVolumePathNameW` resolves **through** the
junction; `Path.GetPathRoot` only ever reads the leading drive letter of the string it was handed.
A junction is not literally a mounted volume — but the code under test has no branch that could
tell them apart (it asks Win32 one question and uses the answer), so it is not a distinction the
fix can be wrong about.

**The three sites, all now on one resolver.**

| site | was | now |
|---|---|---|
| `WindowsPlatformService.GetFreeDiskSpaceBytes` | `Path.GetPathRoot` + `DriveInfo.AvailableFreeSpace` | `VolumeRoot.Resolve` + `GetDiskFreeSpaceExW` |
| `WindowsPlatformService.GetTotalDiskSpaceBytes` | `Path.GetPathRoot` + `DriveInfo.TotalSize` | same, `lpTotalNumberOfBytes` |
| `WindowsLogCompressionService.IsSupported` | `Path.GetPathRoot` + `DriveInfo.DriveFormat` | `VolumeRoot.Resolve` + `GetVolumeInformationW` |

`VolumeHasRecycleBin` (audit #5 **AC17**, already correct) was moved onto the same helper rather than
left as a fourth private copy — **the reason this recurred is that every site rolled its own
resolution**, and one shared `VolumeRoot` is the only version of the fix that stops a fifth.

⚠ **`DriveInfo` is the same trap wearing a different hat, and that is the non-obvious half.** Its
constructor normalizes through `Path.GetPathRoot`, so handing it a *correct* mount root converts it
straight back into the wrong one — which is exactly how AC17's original pre-filter defeated the
`GetVolumePathName` call sitting three lines above it. Pass a resolved root to **Win32**, never back
through a BCL type.

⚠ **`Resolve` returns `null`, never a guess.** A `Path.GetPathRoot` fallback would look defensive
and would silently restore the defect on precisely the paths where resolution is hardest. Each
caller maps null onto its own fail-open sentinel, and those sentinels are **deliberately
asymmetric**: free → `long.MaxValue` (don't block), total → `0` (collapse the percentage term),
NTFS → `false`. Swapping any pair makes an unmeasurable volume look full.

⚠ **A trap the rewrite introduced and the tests removed**: Win32 reports `ULONGLONG`, the callers
are `long`, and a plain cast **wraps negative** — a negative free-space reading does not read as
"unknown" to the guard, it reads as catastrophically full, so it would refuse to write on the
*largest* volumes. `VolumeRoot.ClampToInt64` saturates; `ClampToInt64_TheNaivePlainCastIsShownToBeWrong`
pins that the naive cast really does produce `-1` for `ulong.MaxValue`.

**13 tests in `VolumeRootTests`** (4605 → 4618 overall). ⭐ **Shown able to fail, which mattered here
more than usual**: the junction test has two silent early-return paths (no second volume, `mklink`
refused), either of which would let it pass while asserting nothing. Restoring the old
`Path.GetPathRoot` body failed **exactly one** test — the junction one — and left the other twelve
green. Its cleanup is deliberately a **non-recursive** `Directory.Delete` behind a `ReparsePoint`
check: the junction targets another volume's **root**, so a recursive delete would erase that
volume.

⚠ **Still owed, and small**: nothing here exercises a genuine `mountvol` mount point, only a
junction. If one ever exists on a dev machine, re-run `VolumeRootTests` on it — no code change is
expected.

### ✅ ALL FIVE ROWS CLOSED 2026-08-26 — `[GWORLDACTORCHAIN-2026-08-26]` CE chains through the GWorld actor list

*Reported from a real P3R session (build 3358, `UE427`, 65,158 objects; screenshots + logs supplied).
Fixed in build 3359 — see [dev-log.md](dev-log.md). The **logic** is pinned by 8 unit checks
(`ui/UE5DumpUI.Tests/LiveWalkerGWorldActorChainTests.cs`) with five negative controls. Rows 1–4 were
then driven end to end on a live DumperTest — evidence block below the table. **Only row 5 is still
open**: it needs a World-Partition / streaming title, which DumperTest is not.*

**What was wrong.** `PopulateFromWorld` published every level actor and component in the *Start from
GWorld* list as a field at `Offset = 0`. The actors are reconstructed from their `Outer` — audit #5
F8/F9 — so no such offset exists, and CE resolved `KernelActor (0)` to `P->144AF6408`, the value at
`UWorld + 0`, i.e. the world's **vtable pointer**. The real actor was at `0x64AF68C0`.

⚠ **The tell the user saw first was cosmetic and is worth remembering**: "many fields with offset
`0x0` appeared after the fix". They were right that the two were connected — the F9 fix is what
first *populated* that list, and every row it added carried the fabricated zero.

| # | cat | what to do | expected |
|---|---|---|---|
| 1 | **B** | P3R (or any game with a populated actor list). Live Walker → **Start from GWorld**. Look at the Offset column for the actor / `Actor.Component` rows. | `—`, not `0x0`. `PersistentLevel` still shows its real offset (`0x30`-ish). |
| 2 | **B** | Drill into one actor (e.g. `KernelActor`), pick a scalar leaf, **Copy CE XML**, paste into CE. | The table's root is the **actor's own address** (`0x64AF68C0` in the report), NOT a `GWorld → base → Actor (0)` chain. Every leaf resolves to the address the Live Walker's own Address column shows. The status line says **"⚠ Chain re-rooted at … (absolute address, session-only)"**. |
| 3 | **B** | Same spine, **Copy CE AA Script**. | Status says **"hardcoded address (GWorld path not forward-walkable)"** — NOT "GWorld AOB walk" / "GWorld hardcoded-base walk". The script registers the actor's absolute address. |
| 4 | **B** | A control that must NOT change: navigate GWorld → **PersistentLevel** → some reflected object field, then Copy CE XML. | A normal GWorld-rooted chain with real offsets, AOB wrapper included when the checkbox is on, and **no** re-rooted note. If this one re-roots, the fix over-reached. |
| 5 | **B** | Locate-in-GWorld on an object that resolves via the World-Partition recovery path (status `ok_via_level`), then Copy CE XML. | No `+FFFFFFFF` anywhere in the XML (that was the latent third defect); the chain re-roots at the recovered actor instead. |

⚠ **Row 5 needs a streaming / World-Partition title, not P3R.** If no such game is at hand, close
rows 1–4 and leave 5 open rather than marking the section done.

> #### ✅ ROWS 1–4 PASS 2026-08-26 `[GWORLDACTORCHAIN-2026-08-26]` — DumperTest Shipping, build 3360
>
> ⭐ **Run on DumperTest, not on P3R, and that is not a shortcut.** The defect is in
> `PopulateFromWorld`, which consumes the DLL's `walk_world` reply and never looks at the engine
> version — P3R (UE4.27) and DumperTest (UE5.4) exercise the identical code. DumperTest was already
> granted for computer use, so this cost no new grant and did not disturb a purchased title.
> ⭐ **`clipboardRead` made Cheat Engine unnecessary**: the emitted table was read back directly, so
> every claim below is about the actual bytes copied, not about a screenshot of CE.
>
> Host: `DumperTest-Win64-Shipping`, injected `dist/UE5Dumper.dll`, **UE504, 24,479 objects**
> (a booted engine, not the coherent-zeros trap), UI `v1.0.0.3360` / DLL 3360, GWorld resolved by
> AOB `GWLD_TQ_1` → `&GWorld = 0x7FF6D8457C70` — so row 3's gate had a real GWorld base to refuse,
> which is what makes it discriminating.
>
> | # | result |
> |---|---|
> | 1 | **PASS.** `PersistentLevel` shows `0x30`; `DirectionalLight`, `PlayerStart`, `SkyLight`, `StaticMeshActor`, `PlayerStart.CollisionComponent` … all show `—`. |
> | 2 | **PASS, decisively.** Drilled `PlayerStart0` (`0x1872FEDBE00`), Copy CE XML. Of **382** `<Address>` entries the copied table has **exactly one** absolute address — `1872FEDBE00`, the actor's own, as the root. The UWorld address (`1872C79A4F0`) appears **0** times, the string `GWorld` **0** times, `+FFFFFFFF` **0** times. Arithmetic checks: root `+30` = `1872FEDBE30` = `PrimaryActorTick(0x28) ▸ TickGroup(0x8)`, which is what the Live Walker's own Address column shows. Status: *"⚠ Chain re-rooted at PlayerStart_0 (absolute address, session-only)…"* |
> | 3 | **PASS.** Copy CE AA Script → *"hardcoded address (GWorld path not forward-walkable)"* — the precise note, not the "not a GWorld-rooted path" one. Script body is `define(PlayerStart,1872FEDBE00)` + `registersymbol`, with no GWorld walk. |
> | 4 | **PASS — the control did not move.** `GWorld → PersistentLevel → LevelScriptActor` still emits the AOB-wrapped restart-stable chain: root `gworld_addr_43BDD1`, then `PersistentLevel (30) ▸ LevelScriptActor (F0)`, then real leaf offsets. No re-root note, no `+FFFFFFFF`, and the ONLY non-`+` address is the AOB symbol name. |
>
> **The UI log is the compact proof**, and it is worth reading against the P3R report:
>
> | | P3R, build 3358 (broken) | DumperTest, build 3360 (fixed) |
> |---|---|---|
> | nav | `NAV→ KernelActor … off=0x0 ptr=True` | `NAV→ PlayerStart … off=none ptr=True` |
> | spine | `BC=GWorld(P,0x0,B0A0) > KernelActor(P,0x0,68C0)` | `BC=GWorld(P,0x0,A4C0) > PlayerStart(P,none,BE00)` |
> | export | `CEXML export: … bcCount=2` | `CEXML export: … bcCount=1` (re-rooted) |
>
> ⚠ `off=none` rather than `off=0xFFFFFFFF`: the first live run (build 3359) printed the sentinel
> through `0x{-1:X}`, which is meaningless as an offset **and** confusable with the `+FFFFFFFF`
> defect itself — a reader grepping the logs for `FFFFFFFF` would have hit a correctly-marked hop
> and read it as the bug. `LiveWalkerViewModel.FormatCrumbOffset` now prints what it means, at all
> six log sites.
>
> ⭐ **All four rows were then re-run on build 3360**, i.e. on the exact binary being handed over
> rather than on the one that happened to be built when the bug was fixed —
> `dist/UE5DumpUI.exe` sha `5b79406f`, byte-identical to the Native-AOT output. Identical outcomes
> (new session, so new addresses): root `10C5E66C0C0`, 382 `<Address>` entries, **1** absolute,
> UWorld `10C5D859BD0` **0** times; AA script `define(PlayerStart,10C5E66C0C0)`; control
> `bcCount=3` with root `gworld_addr_359220` and `PersistentLevel (30) ▸ LevelScriptActor (F0)`.
>
> **Row 5: CLOSED 2026-08-26 on Titan Quest II (build 3361).** DumperTest has no streaming, so the
> `ok_via_level` path could not be entered there. ⭐ **The reproducer was found by AUDITING THE LOG
> TREE, not by guessing a title**: `Logs\TQ2-Win64-Shipping\ui-view-20260823-091415.log` already held
> the pre-fix line, from a session five days earlier —
> `[2026-08-23 09:11:37.634] LocateInGWorld: reach mode, 2 hop(s) | BC=GWorld(P,0x0,68E0) > (world
> level)(S,0xFFFFFFFF,A960) > (level actor)(S,0xFFFFFFFF,FA60)`. Two `0xFFFFFFFF` hops in a real
> spine; nobody had ever pressed Copy CE XML on one, which is exactly why the third defect stayed
> latent.
>
> Re-run on TQ2 (UE507, **279,587 objects** — identical to the 2026-08-23 session; Instances →
> class `Actor`, exact → **3 results**, identical → 🌍 Locate on `0x2B8B783A8D0`):
>
> | | 2026-08-23, pre-fix | 2026-08-26, build 3361 |
> |---|---|---|
> | locate spine | `(world level)(S,**0xFFFFFFFF**,A960) > (level actor)(S,**0xFFFFFFFF**,FA60)` | `(world level)(S,**none**,91C0) > (level actor)(S,**none**,A8D0)` |
> | Copy CE XML from it | never pressed, on any build | `CEXML re-anchored: dropped **2** offset-less hop(s); root is now (level actor) @ 0x2B8B783A8D0` |
>
> **The emitted table, measured**: 777 `<Address>` entries, **exactly one** absolute — `2B8B783A8D0`,
> the level actor's own address, as the root (`<Description>"Actor_0"</Description>`). `FFFFFFFF`
> appears **0** times anywhere in the file; `GWorld` 0; the world address (`2B8B7892320`) 0; the
> level address 0. AA Script on the same spine: *"hardcoded address (GWorld path not
> forward-walkable)"*, body `define(Actor,2B8B783A8D0)`.
>
> ⚠ **Environment note, recorded rather than hidden**: TQ2 ran the **3360** proxy DLL against the
> **3361** UI, and the UI's own stale-build banner said so (`⚠ DLL build 3360 ≠ UI 3361 — stale,
> redeploy`). That is acceptable *here specifically*, and the reason is checkable rather than
> assumed: `git diff --stat e88190ba HEAD -- dll/` is **empty**, so the DLL is functionally
> identical, and every mechanism row 5 exercises (`AnchorAtLastUnchainableHop`, `FormatCrumbOffset`,
> `LogReanchor`) is UI-side. ⛔ Do not generalise this — for any row that turns on DLL behaviour the
> banner is a stop, not a note.

### Running the 2026-09-05 vendor-audit batch (A1 / A2 / A3 / A7) on the verification PC

*Added 2026-09-05 with the four rows below. The batch is unusual in that its highest-value row
(A2 / EVERSPACE 2) is also its cheapest, and one row is gated on a game that may not be installed.*

**One game at a time, sequential, never parallel.** Injecting a second title while the first holds `\\.\pipe\UE5DumpBfx` is how a session measures the wrong process.

#### Step 0 — before any game (offline, ~20 min)

1. `git -C D:\Github\UE5CEDumper status -sb` — expect ` M docs/handover-2026-08-22.md`, `ahead 14`. Apply the §4(a) corrections, then commit.
2. **Fix D1 and D2** (`Ubel.cpp:2900` → `elemAddr + softPathOff`; `Ubel.cpp:4079` → `LazyGuidOffset(fi.Size)`, and correct the stale comment at `:4074`). Fix D3 while there.
3. `build.ps1 -Target DLL`, then **`build.ps1 -Mode Publish`** — hand-over rule. Verify `dist\UE5DumpUI.exe` is ~54.7 MiB (57,398,784 B), **not** ~107 MB, and record the sha.
4. `py tools/check_all.py` — 12 gates green, including `check_derived_counts` at its new number.
5. **Census the fixtures on THIS machine** — the Steam layout may differ from the primary PC. Parse `libraryfolders.vdf` and check for: Lushfoil, Satisfactory, EVERSPACE 2, OCTOPATH, Solarpunk, and **Star Wars Jedi: Fallen Order** (the A3 gate — on the primary PC it is a ghost, only `steam_appid.txt`). Record what is present before planning further.
6. Grant list: `list_granted_applications` **first**, then request only what is missing — grants outlive sessions here (`docs/handover-2026-08-22.md:75-99`); the plan doc's §3 claim that they do not is refuted and is being corrected. Include `Cheat Engine (64-bit SSE4-AVX2)` and `steamwebhelper.exe`, both absent from `auto-verification-session-plan.md` §3.

#### Step 1 — EVERSPACE 2 (Row 2, A2) — ~20 min, highest information per minute

The one row whose answer is genuinely unknown and cheap.

1. **Refresh the deployed proxy first.** The `version.dll` in `…\ES2\Binaries\Win64\` is dated 2026-08-27 (build 3367) and owns the pipe; a fresh injection does not displace it. Then `assert_build()`.
2. Inject → `trigger_scan` → **one invoke** (`Add_IntInt(3,4)`) or `pe_profile_start`. Detection is lazy; connect+scan+walk emits no `DetectProcessEvent` line at all.
3. **Capture**: `init-0.log` (the `match at vtable+0x…` line, the `offset resolved … via the pattern scan` line, and the *absence* of `(fallback)` and `VALIDATION FAILED`); `scan-0.log`'s `DetectVersion: … -> 506`; the status bar `Connected — UE506`; Live Funcs `HookActive` / `HookFireCount`; the invoke's returned value.
4. Record whether the slot is `0x260` (table corroborated) or `0x278` (506 row wrong for this title — a result, not a failure).

#### Step 2 — Lushfoil UE5.6 (Row 1, A1 — primary) — ~40 min

1. Inject the fixed DLL. Live Walker → walk any actor with an asset reference.
2. **Capture**: both `payload envelope measured` lines from `offsets-0.log` (soft **and** lazy); a Live Walker Value cell showing a path that **begins with `/`**.
3. Property Search for an `ArrayProperty` whose inner is `SoftObjectProperty`; if one exists, Copy CE XML and confirm `<Address>+8</Address>` on the `PackageName` leaf and that the DropDownList keys now match the labels (that is D1's fix, visible only here).
4. If no such array exists, record the CE leg as **not exercised** and close the DLL + Live Walker legs independently. Do not synthesise one.

#### Step 3 — Satisfactory v1.1.3.1 UE5.3 (Row 1, A1 — second era) — ~25 min

Same captures. `5.3` is the first version where the envelope moved, so this is the boundary case. ⚠ modular build: the launcher is `Engine\Binaries\Win64\FactoryGameSteam-Win64-Shipping.exe`.

#### Step 4 — OCTOPATH TRAVELER UE4.18 (Row 1 negative control + A6 negative control) — ~20 min

1. A1 control: pre-5.3 title must land on the tagged `0x10` envelope; Live Walker soft-path display unchanged.
2. A6 control, free in the same session: `offsets-0.log` must read `UBoolProperty::FieldSize derived at +0x70 (Offset_Internal +0x44, UE=418)` — byte-identical to the old default.
3. ⚠ Proxy: **`winmm.dll`**, not `version.dll` (`docs/test-games.md:14`). `dxgi` crashes here.

#### Step 5 — Row 3 (A7), ride-along — ~15 min, no dedicated launch

Attach to whichever of Steps 2–4 is still running. `walk_class` over the pipe on an `EmptyPayload` child, then a full SDK export, then grep. Both negative controls come out of the same export.

#### Step 6 — DQ III HD-2D (A1 falsification probe) — ~20 min, optional

Not a control. Run it, log the `payload envelope measured` line, report the value. A `+0x08` here on a title whose row records the UE 5.0 field layout under a 505 badge is the ambiguity `Grimoire.h:255-262` warns about, and it is worth knowing before a user hits it.

#### Step 7 — Jedi Fallen Order (Row 4, A3) — ONLY if the Step-0 census found it installed

CE injection only (EA app blocks the proxies). Check `scan-0.log` for `UE Version = 421` **before** anything else; if it says 422, close the row. Then the `set_ue_version_override` 421→422→421 A/B on the same binary.

#### Not scheduled

**DQ XI S (A6's only UI-visible host) and NEKOPALIVE** — exe backups exist under `D:\UE_Analyze_data\Game Binary backup\` but a backup exe cannot be launched. The A6 row stays open, blocked on a host, and its amended text should say so.

---

#### What NOT to verify from this batch

1. **Do not build a UE Test configuration, and do not re-propose "upgrade DumperTest to 5.7".** Both installed engines refuse it at target validation (§2). A5's 40-byte half has no producible fixture on *either* PC — record it in `reference-builds.md` and stop re-deriving it.
2. **Do not open a row for A5's tie-break or its new warning.** `PreferStride` is a provable no-op over `{16, 24, 32, 20, 40}` (every divisor pair already has the smaller candidate earlier in the list), and the high-null warning is an anomaly detector whose silence on a healthy pool is the expected result. Both belong in the checklist's §1/§3, not the register.
3. **Do not open a row for A9.** Every branch is gated on `bCasePreservingName`, whose only two writers are `Genau.cpp:3243/3247` inside a live 20-object vote — no config, preset, or UI can force it true — and 12 titles have measured false with zero CPN. Every non-CPN arm is arithmetically identical to the pre-fix code. A row would be unfalsifiable. File the `Ubel.cpp` follow-up in `todo.md` instead.
4. **Do not open a row for the nlohmann bump, and do not repeat its stated rationale.** The commit's *"the EINTR fix is blocked by json.hpp's own errno reset"* is wrong — that reset exists in **both** versions. The real reasons there is no victim: EINTR is not a Windows/UCRT phenomenon, MSVC's `strtod` sets only `ERANGE` (which the new code excludes and the old code already excluded via a round-trip guard), and every integer on our wire is small (addresses travel as **strings**, `Fern.cpp:1983` etc.), so a float fall-through has no consequence. Also stop citing the C++ suite as coverage: `.dump()` and `json::parse` each occur **0** times in `dll/tests/`.
5. **Do not add a "verify DumperTest's fallback path" row.** It requires two DLL builds plus a new script, and the existing rig's polarity is **inverted** for A2 — `tools/verify/pehook_3b_refusal.py:136/:147` reports FAIL when there is no `VALIDATION FAILED` and no `-3`, which is exactly the outcome a *fixed* fallback produces. The variant DLL it names also no longer exists on disk and was built pre-A2 anyway. If the fallback is ever checked, budget three builds and three launches and write a new oracle.
6. **Do not treat `docs/verification-register.md:3774-3775` (PEHOOK steps 7/8) as an open A2 check.** The row header at `:3426` lists steps 7 and 8 as **already verified**; only step 3c remains, and it is structurally unreachable. Those steps ran on the pre-A2 DLL, which is *why* the ES2 row exists — but they are not an open slot to fill.
7. **Do not use the Self-Test advice strings as a positive UI observable anywhere.** All five (`en.axaml:435-439`) are keyed `Fail.*` and render only on the failure path. "The advice did not appear" is equally true of a UI that never connected.
8. **Do not grep `walk-0.log` for A1's DLL observable.** It lives in `Ubel.cpp` but its category is `DYNO:PersistPtr`, which routes to **offsets**. The natural grep returns nothing and reads as a failure.
9. **Do not use DQ III or DQ I&II as A1's negative control** (§3, Row 1) — they cannot discriminate and would be scored as a regression against a title the register already records as version-misdetecting.
10. **Do not "simplify" the ProcessEvent table into a `>=` ladder**, do not narrow Ubel's `{base, ±4, +8, −8}` bool probe spread now that the base is derived, and do not enable the `Bookmarks\` age sweep. All three are deliberate; the first two are the exact bugs A2 and A6 fixed.
11. **Do not plan a batch off `docs/auto-verification-session-plan.md` §5 or §10.** Its own top banner still points at §10 as the live authority and its §3 still asserts "grants do not survive a session", which `handover:75-99` measured false. Use it for §3 grant mechanics and §4 authorised writes only — and read §4.1 as spent (its Light Maze target is not installed in any of this machine's four Steam libraries).

-----

### ⬜ FIXED 2026-09-05, NEEDS A LIVE CHECK — audit A1: soft/lazy object-pointer offsets are now measured

*`TSoftObjectPtr`'s `FSoftObjectPath` was read at a hardcoded `+0x10`. That is right only to UE 5.2: 5.3 deleted `TPersistentObjectPtr::TagAtLastTest` and the path moved to `+0x08`. `TLazyObjectPtr`'s `FGuid` was read at `+0x10` too, and `FUniqueObjectGuid` is a bare `FGuid` (alignof 4) — so the tagged envelope is `0x0C` and there is **no era** in which `0x10` was right there. Both are now derived from the property's own `ElementSize` (`Grimoire.h:263-266`, `:268-280`; wrappers `Ubel.cpp:410-433`), and `CeXmlExportService.cs:3100` no longer bakes a literal `"+10"`.*

*⚠ Six installed titles are ≥ 5.3 and were reading one field late: Satisfactory v1.1.3.1 UE5.3 (`docs/test-games.md:28`), Manor Lords UE5.5 (`:27`), Lushfoil UE5.6 (`:26`), Satisfactory v1.2.3.1 UE5.6 (`:29`), EverSpace 2 (now 5.6, see the A2 row), Titan Quest II UE5.7 (`:13`).*

**Why the suite cannot close it.** `dll/CMakeLists.txt:539-543` compiles only `dll_helpers_test.cpp` + `Radar.cpp` + `Denken.cpp` — `Ubel.cpp` is **not** in any test target. `dll_helpers_test.cpp:5491-5530` pins `FSoftObjectPathSizeFor` / `PersistentPtrEnvelopeFor` arithmetic; nothing exercises the latch, the log line, the wire field, or — the load-bearing one — **whether `fi.Size` is a trustworthy `ElementSize` at all**. `Ubel.cpp:2523` and `:5878` already document `FPROPERTY_ELEMSIZE` as returning garbage (e.g. `1073742336`) for members of certain `UScriptStruct` layouts, and the entire fix is `elemSize - payloadSize`. No C# test covers `SoftArrayPathOffset` either.

**Acceptance test** — any UE ≥5.3 title; **Lushfoil UE5.6** (`docs/test-games.md:26`, installed at `E:\SteamLibrary`) is the named exemplar, Satisfactory v1.1.3.1 UE5.3 (`H:\SteamLibrary`) the second era. Three paired observables:

| side | where | expect |
|---|---|---|
| DLL | `offsets-0.log` | `TSoftObjectPtr payload envelope measured: +0x08 (ElementSize 0x28 - payload 0x20, UEver=506)` — the format is `Ubel.cpp:399-403`, and category `DYNO:*` routes to **offsets**, not walk, even though the line lives in `Ubel.cpp` (`Sein.cpp:80` `{ "DYNO", 4, LF_Offsets }`; `Ubel.cpp:8` `#define LOG_CAT "WALK"` binds only the `LOG_*` macros) |
| DLL | `offsets-0.log` | the sibling `TLazyObjectPtr payload envelope measured: +0x08 (ElementSize 0x18 - payload 0x10, …)`. **Required, not optional** — the lazy half was wrong in every era and has no other observable |
| UI | Live Walker, Value column | a `SoftObjectProperty` renders a **package path beginning with `/`**. Pre-fix the read started at `AssetName`, so it never began with `/`; post-fix it is a package path and always does. One-glance pass/fail. `Ubel.cpp:4043` sets `fv.typedValue = assetPath` and `LiveFieldValue.cs:395-401` gives `TypedValue` top precedence, so a **loaded** asset still shows the path |
| UI | CE XML export of a `TArray<TSoftObjectPtr>` | `<Address>+8</Address>` on the leaf described `PackageName` (was `+10`). ⚠ There is no `<Offsets>` element here — `EmitOffsets` (`CeXmlExportService.cs:3781-3790`) emits one only for a non-null array and both soft leaves pass `null`. This is the **stale-UI separator**: `DumpService.cs:1449` defaults `soft_path_offset` to `?? 0x10`, so an old DLL behind a correct UI emits `+10` while the log says `+0x08` |

⚠ **Precondition on the DLL legs.** The measurement fires only when a walk **touches** a soft/lazy property (callers `Ubel.cpp:2762/2765`, `:4024`, `:4306/4469`, `:6141/6167`) and only **once per distinct envelope per process** (guard `Ubel.cpp:394-395`, `measured && latched != envelope`). Absence after walking an object with no such property proves nothing. The unaccountable-`ElementSize` case is silent here and loud elsewhere: `Invalid SoftObject elemSize=` in **walk**-`0.log` (`Ubel.cpp:2854`, category `WALK:ArrayG`).

⚠ **Scope the CE leg or it cannot be closed.** `soft_path_offset` reaches the wire only inside the array block gated on `softArrayFNameSize > 0` (`Fern.cpp:1482-1489`), and the exporter's path-leaf branch requires `ArrayInnerType == "SoftObjectProperty"/"SoftClassProperty"` (`CeXmlExportService.cs:2722-2727`). A **scalar** soft property exports as a bare `8 Bytes` hex leaf (`:3952-3953`) with no path leaves at all. So the CE leg needs a discovery step first — Property Search for an `ArrayProperty` whose inner is `SoftObjectProperty`. The DLL and Live Walker legs need no array and close independently.

**Negative control**: **Hogwarts Legacy 4.27** (`docs/test-games.md:21`) or **OCTOPATH 4.18** (`:14`, installed at `E:\SteamLibrary`) — a pre-5.3 title must still land on the tagged `0x10` envelope, and the Live Walker path must be unchanged from the pre-fix build. ⛔ **Do NOT use DQ III HD-2D as the control.** `docs/test-games.md:18` records the SE HD-2D fork as reporting **UE505** while using the UE 5.0 field layout, and both `ReadSoftObjectPath` (`Ubel.cpp:335`) and `SoftObjectPathPayloadSize` (`:410-418`) discriminate on `g_cachedUEVersion >= 501` — so on a 5.0-layout title mis-badged 505 the payload is computed `0x20` against a real `ElementSize 0x28`, the candidate is `0x08`, and `PersistentPtrEnvelopeFor` **accepts and latches it** (`Grimoire.h:255-262`; `dll_helpers_test.cpp:5511-5519` asserts exactly that bogus latch). DQ III would log `+0x08` and be scored a regression against a control that cannot discriminate. It is a plausible **new victim**, not a control — run it separately, with no pre-declared pass value, and report what it logs.

-----

### ✅ FIXED + LIVE-VERIFIED 2026-09-05 `[A2-ES2-506-2026-09-05]` — audit A2: the 506 row is CORROBORATED at `0x260` on a retail UE 5.6.1 build

> **CLOSED on the verification PC, build 3371, 2026-09-05 21:00.** Rig:
> [`tools/verify/a2_es2_pehook.py`](../tools/verify/a2_es2_pehook.py) (written for this row; it
> re-runs from one command). Every observable this row asked for was captured in one session, and
> **both absence checks held** — which is the half a play-test cannot produce:
>
> | observable | expected | measured |
> |---|---|---|
> | `scan-0.log` | `DetectVersion: … -> 506` | `DetectVersion: PE VERSIONINFO -> UE 5.6 -> 506` |
> | `init-0.log` | pattern match | `DetectProcessEvent (pattern): match at vtable+0x260 -> 0x7FF6E6297080` |
> | `init-0.log` | what was **installed** | `ProcessEvent: offset resolved to vtable+0x260 via the pattern scan (detection run 0/8)` |
> | `init-0.log` | `(fallback)` **absent** | **0 lines** |
> | `init-0.log` | `VALIDATION FAILED` **absent** | **0 lines** |
> | invoke | `Add_IntInt(3,4) == 7` | **7** — a computed value, not a plausible one |
> | diagnostics | hook live | `hook_active=True`, `hook_fire_count=52` |
> | pipe | right binary | `build 3371`, `ue_version 506`, `79,831` objects, `load_mode proxy:version.dll` |
>
> **Verdict: `0x260`.** The 506 row now has a second independent live witness (Lushfoil was the
> first, the UVTD oracle aside). The title's twice-measured `0x278` stays what it always was — a
> live witness for the **5.5** row, taken on the pre-patch binary — so the table's deliberate
> non-monotonicity is confirmed from both sides by the same game. ⛔ Still do **not** collapse the
> table into a `>=` ladder; that is the exact bug A2 fixed.
>
> ⚠ **The build gate below was real and had to be paid twice.** The deployed `version.dll` was
> dated 2026-08-29 (not 2026-08-27 as written below) and owned the pipe; `proxy_refresh.py` fixed
> that. The run was then thrown away and repeated anyway, because build 3370 could not be read —
> see `[SEINSHARE-2026-09-05]`.

<details><summary>original row (kept — its reasoning is what made the row cheap to close)</summary>

**FIXED 2026-09-05, NEEDED A LIVE CHECK — audit A2: EVERSPACE 2 patched to UE 5.6 and crossed the table's own boundary**

⚠ Deliberately **not** a `###` heading and deliberately carrying no ⬜: `check_derived_counts`
counts `^### .*⬜` inside this section, so leaving either here would keep the row counted as open.

*A2 replaced an unreachable `>= 550` band (every UE5 game silently took `0x220`, wrong by `0x28-0x58`) with a measured per-version table, `DynOff::ProcessEventVTableSlotFor`, `dll/src/Grimoire.h:321-341`. The table is deliberately **non-monotonic**: `case 505: return 0x278` (`:336`) then `case 506: case 507: return 0x260` (`:337`). It is **fallback only** — `Frieren.cpp:1693-1701` runs the pattern scan across up to 12 candidate vtables and reaches `DetectProcessEventVTableOffsetByVersion` (`:1601`) at `:1703` only after all miss.*

*⭐ **The open EVERSPACE 2 question is settled, and settled in a way that helps.** The `0x278` measurement is a **live retail witness for the 5.5 row**, not a contradiction of the 5.6 row: it was taken twice (2026-05-11, `docs/lessons-learned.md:140`; 2026-08-20 `[PEHOOK-6-2026-08-20]`, `docs/verification-register.md:3504`) and bracketed by in-process PE-resource detections of **505** on 2026-08-19 (`:4434`) and **505 (5.5)** on 2026-08-24 (`:9031`) — all of it on the exe that Steam replaced on **2026-09-01 22:05**. The installed binary now reads **`prod=5.6.1.0 → 506`** (`py tools/verify/pe_version_probe.py`, measured 2026-09-05) and the 2026-09-03 session logged it in-process: `scan-20260903-123758.log` `DetectVersion: PE VERSIONINFO -> UE 5.6 -> 506`, `init-0.log` `UE5_Init: Complete (UE506, …, Objects=1154897)`. So the maintainer's report is correct — and the title has moved from the `0x278` row to the `0x260` row with no slot ever measured on the new build.*

**Why the suite cannot close it.** `dll_helpers_test.cpp:5607-5672` pins every table row *and* its non-monotonicity — 20 assertions of pure header arithmetic. `DetectProcessEventVTableOffset` is `static` in `Frieren.cpp` and the test target links headers, so nothing exercises whether the value is ever reached, and nothing can say what a real 5.6 licensee build's vtable actually looks like.

**Acceptance test** — **EVERSPACE 2**, `D:\SteamLibrary\steamapps\common\EVERSPACE™ 2\ES2\Binaries\Win64\`. Detection is **lazy on the invoke path**: `RunPeDetection` (`Frieren.cpp:1804`) is reached only from `EnsureProcessEventReady` (`:2025`) and `UE5_EnsureGameThreadHook` (`:2188`), so a connect + scan + walk emits **nothing** — confirmed empirically, `grep -c DetectProcessEvent` over the whole 2026-09-03 ES2 log folder returns **0** in all 18 files. Sequence: refresh the proxy → inject → `trigger_scan` → **one invoke** (or `pe_profile_start`) → grep.

| side | where | expect |
|---|---|---|
| DLL | `init-0.log` | `DetectProcessEvent (pattern): match at vtable+0x260 -> 0x…` (`Frieren.cpp:1589`), and `ProcessEvent: offset resolved to vtable+0x260 via the pattern scan (detection run 0/8)` (`Frieren.cpp:1850` — the only line reporting what was actually **installed**; `primary=` is not necessarily the return value, `:1640-1654` sweeps ±8/±16) |
| DLL | `init-0.log` | **ABSENT**: `DetectProcessEvent (fallback)` (`Frieren.cpp:1623`) and `VALIDATION FAILED` (`Frieren.cpp:1980`). A fallback line means the table quoted itself and the run must be discarded |
| DLL | `scan-0.log` | `DetectVersion: PE VERSIONINFO -> UE 5.6 -> 506` (`Genau.cpp:2741`, category `SCAN:Ver` → `Sein.cpp:74`) — the run must be on the 5.6 binary, not a rolled-back one |
| UI | status bar | `Connected — UE506 (…)` (`MainWindowViewModel.cs:734`, `:2763`) and Pointers panel showing **506** (`PointerPanel.axaml:42`, raw int) |
| UI | Live Funcs | `HookActive == true` **and** `HookFireCount > 0` (`Fern.cpp:4097-4098` → `DumpService.cs:2832-2833`) |

**Negative control, and it is what makes this a test rather than a screenshot**: the invoke must return a **computed** value — `Add_IntInt(3,4) = 7`, as `[PEHOOK-6]` did. A wrong slot returns 0/unchanged while `HookActive` can still read true. ⚠ Do **not** use the Self-Test advice strings as a positive: all five (`en.axaml:435-439`) are keyed `str.System.SelfTest.Fail.*` and `PointerPanelViewModel.cs:1739` documents `ClassifySelfTestFailureAsync` as running "only on the failure path" — nothing renders on a pass, so their absence is vacuously true for a UI that never connected.

⛔ **Build gate, and this is the trap `[B648-TWOENGINES]` already hit.** ES2's deployed `version.dll` is dated **2026-08-27** (`init-0.log` line 1: `build: 1.0.0.3367 344d9242-dirty`), i.e. it predates all fourteen commits and **owns the pipe** — a fresh injection does not displace it. Refresh the proxy from a `-Mode Publish` dist and `assert_build()` (`tools/verify/pipe_client.py:11-22`) **before** any number is taken.

**Outcome rule.** `0x260` → the table's 506 row is corroborated on a real retail 5.6 licensee build (Lushfoil and the UVTD oracle are currently its only support). Still `0x278` → the 506 row is wrong **for this title**; record it as a register note, and ⛔ do **not** collapse the table back into a `>=` ladder — the non-monotonicity is the bug A2 fixed.

</details>

-----

### ⬜ FIXED 2026-09-05, NEEDS A LIVE CHECK — audit A7: the SDK export's empty-base struct fix, end to end

*UE reports an empty native `USTRUCT`'s `PropertiesSize` as **1**, so the emitter's `Offset >= superPropsSize` split dropped a derived struct's offset-0 field and the trailing-pad pass wrote padding in its place. The floor is now lowered by a new wire field, and the empty base is emitted empty so EBO applies: `Ubel.cpp:1004-1007` captures `OwnPropertiesStart` after the own-chain walk and **before** the super chain is prepended at `:1015-1017`; `Ubel.h:72` defaults it `-1`; `Fern.cpp:2117` sends `own_props_start` from the serialiser shared by `walk_class` and `walk_class_batch`; `DumpService.cs:377` parses it with a load-bearing `?? -1`; `SdkExportService.cs:446` lowers the floor and `:495-496` suppresses the pad.*

**Why the suite cannot close it — and why the obvious observable is illegal here.** The five C# tests (`SdkExportServiceTests.cs:340, 371, 398, 423, 447`) hand-construct `ClassInfoModel { OwnPropertiesStart = 0 / -1 / 0x18 }` and pin the emitter arithmetic in **both** directions. There is **zero** DLL-side coverage — `grep -rn "OwnPropertiesStart|own_props_start" dll/tests/` returns nothing. But the generated header **alone cannot be the paired observable**: `DumpService.cs:377` defaults a missing field to `-1`, so a pre-fix DLL with a correct UI and a correct DLL with a stale UI produce a **byte-identical wrong header**. That is precisely the ambiguity the register charter exists to break, so the DLL leg must be read off the wire.

**Acceptance test** — rides along on any live session; no dedicated launch. Vehicle: `FEmptyPayload` (`vendor/UnrealEngine/.../Animation/AnimData/AnimDataNotifications.h:88`, Engine module, **outside** the file's only `#if WITH_EDITOR` at `:281-296`, byte-identical in the installed UE 5.4 tree) and a child such as `BracketPayload` (`FString Description`) or `AnimationTrackPayload` (`FName Name`).

| side | where | expect |
|---|---|---|
| DLL | `py tools/verify/pipe_client.py walk_class --args '{...}'` | on the derived struct: `own_props_start` **present and == 0**, `super_props_size == 1`, `props_size == 0x10`, `Description` at offset 0. On `EmptyPayload` itself: `props_size == 1`, no fields, `own_props_start == -1`. ⚠ There is **no log line** — `own_props_start` occurs once in `dll/src`, at `Fern.cpp:2117`, and `Fern` logs the request, never the reply body |
| UI | the generated `.h` | `struct BracketPayload : public EmptyPayload`, then `    FString Description; // 0x0000 (0x0010) StrProperty`, then `}; // Size: 0x0010` — **no** leading `Pad_0001[…]`. And the base emitted as `struct EmptyPayload` / `}; // Size: 0x0001` with **no** `Pad_0000[0x0001]` |

⚠ **Names carry no `F` prefix.** `SdkExportService.cs:380-386` appends `classInfo.Name` and `superName` raw, and a `UScriptStruct`'s FName has no `F`. Grep for `EmptyPayload`, never `FEmptyPayload` — the latter has zero hits in the packaged fixture.

**Negative controls, two, both free in the same export run**: (1) **the W2 guard** — a class with a **non-empty** super must still split at `superPropsSize` and must **not** re-emit the inherited chain (`SdkExportService.cs:443-447`; this is the failure the fix could *cause*, and is the load-bearing one); (2) **narrowness** — an opaque field-less struct with `PropertiesSize > 1` must still receive its `Pad_0000[…]`, proving the `emptyBase` suppression (`:495`, `own.Count == 0 && ownStart == 0 && propsSize == 1`) stayed narrow.

-----

### ⬜ FIXED 2026-09-05, NEEDS A LIVE CHECK — audit A3: `UFunction::FunctionFlags` on a real UE 4.21 title

*The pre-fix ladder read `>= 421 → 0x98`; the measured table is `4.08-4.21 = 0x88`, `4.22-4.24 = 0x98` (`Grimoire.h:396-403`). Exactly **one** producible version changes: **4.21**. On the 4.21 template `0x98` is `FirstPropertyToInit`, an `FProperty*` — and acceptance is only `funcFlags != 0` (`Ubel.cpp:1435`), so a non-null pointer's low dword latched and `NumParms` / `ParmsSize` / `ReturnValueOffset` were then read from `+0x04/+0x06/+0x08` off that wrong base (`Ubel.cpp:1450-1453`). The same commit deleted a dead `>= 550` band (unreachable: needles top out at 508, `VersionNeedleScan.h:61-62`; the pipe override is clamped 418..509, `Fern.cpp:1726`) and made both readers CPN-aware (no known CPN title).*

**⛔ HOST GATE — test this first, and close the row if it fails.** The corpus holds exactly one UE 4.21 title: **Star Wars Jedi: Fallen Order** (`docs/test-games.md:24`), an EA-launcher title where `version.dll`/`dinput8.dll` proxies do **not** load — it must be CE-injected after the game is running. On the primary PC it is a **ghost install** (`H:\SteamLibrary\steamapps\common\Jedi Fallen Order\` holds only `steam_appid.txt`; an exe backup exists at `D:\UE_Analyze_data\Game Binary backup\Jedi Fallen Order`, which cannot be launched). If it is not installed on the verification PC either, close this row as *"no reachable victim in the corpus"* rather than forcing it. Second gate, in `scan-0.log`: the run must log `FindAll: UE Version = 421`. `4.21.` is a real needle rung, but if the tag scan misses, detection defaults to 504 and the `TNameEntryArray` override rewrites it to 422 → primary `0x98` → **the fix changes nothing** and the row is moot.

**Why the suite cannot close it.** `dll_helpers_test.cpp:5539-5596` pins `FunctionFlagsOffsetFor` rows, the CPN `+8` invariant and the sweep contents — header arithmetic. `ReadFuncFlagsAndParams` lives in `Ubel.cpp`, which no test target compiles.

| side | where | expect |
|---|---|---|
| DLL | `pipe-0.log` | `Mailbox: FIND_FUNCTION '<name>' -> 0x… (parmsSize=%u numParms=%u flags=0x%X)` (`Mimic.cpp:577`, category `PIPE`) — the **only** DLL line printing all three, and CE is this title's injection route anyway. A param-less UFunction must report `numParms=0 parmsSize=0`, and `flags` must carry `FUNC_Native(0x400)` / `FUNC_Public(0x1)`-shaped bits, not a heap-pointer low dword |
| UI | Interesting Functions grid | `ParamsLabel` reads e.g. `2 (16B)` and not `2 (65413B)` (`AllFunctionsResult.cs:70`), and `ShortFlags` renders sane badges (`:38-51`, BC/BE/BP/Const/Exec/Native/Event/Static) |
| UI | Console panel | a **non-empty** `UFUNCTION(exec)` list (`IsExec`, `AllFunctionsResult.cs:67` → `ConsoleViewModel.cs:262`, `:612`) |

⚠ **Lead with `num_parms` / `parms_size`, not the badges** — `Ubel.cpp:1450-1453` reads them off `funcFlagsOff + 4/6/8`, so a wrong base corrupts them too, and `2 (65413B)` is self-evidently wrong where a garbage bitmask is not. ⚠ `walk-0.log`'s `WalkFunctions: %zu functions found at 0x%llX` (`Ubel.cpp:1659`) is a **liveness marker only** — count and address, no flags. Do not write the row against it.

**Negative control** — an in-session A/B on the **same binary**, which the pre-fix build could not give you: `set_ue_version_override` (`Fern.cpp:1715-1768`, accepts 418..509, sets `g_cachedUEVersion` immediately, no re-scan). Run at **421** (correct flags), override to **422** (the old garbage reappears — `0x98` on a 4.21 layout is `FirstPropertyToInit`), then back to 421. ⚠ Pass `persist:false`, or clear afterwards — `persist:true` writes into `UE5CEDumper.{Machine}.json` and the next session inherits it. Second control: **OCTOPATH 4.18** must be byte-identical (`0x88` before and after; a stock layout hits the primary so the reordered sweep never runs).

-----

### ⬜ FIXED 2026-09-05, NEEDS A LIVE CHECK — audit A6: UBoolProperty::FieldSize is now derived

*`DynOff::UBOOLPROP_FIELDSIZE` had **zero writers** repo-wide against nine readers. The
FProperty arm of `ValidateAndFixOffsets` derived the entire subclass-extension family and
simply had no `else`, so every UE4 <4.25 game kept the `0x70` default regardless of what its
`Offset_Internal` actually probed to. It is now derived from the probe.*

*⚠ The delta is **not** the flat `+0x2C` the audit prescribed. Measured across all 31 UVTD
templates: `0x28` for **4.11–4.17**, `0x2C` for **4.18+**, `+8` more under CasePreservingName.
The 4.17/4.18 step is structural — `Offset_Internal` and `RepNotifyFunc` swap order there.
4.11–4.17 is seven versions inside our floor, so the flat form would have been wrong on all
of them.*

**Failure shape being fixed** — DQ XI S (`docs/test-games.md:19`, UE4.22, UProperty mode, a
whole-layout **+0x10 shift**) puts the true `FieldSize` at `0x80`. The old `0x70` default plus
Ubel's `{base, ±4, +8, −8}` spread tops out at `0x78`, so **no probe could reach it**:
`boolFieldMask` stayed 0 and the reader fell back to `byteVal != 0`, which reports a native
C++ bitfield bool as **true whenever any sibling in its byte is set**.

**Acceptance test** — needs a **UProperty-mode** game (UE4 <4.25), ideally a shifted one. DQ XI S
is the named exemplar. Two paired observables, both required:

| side | where | expect |
|---|---|---|
| DLL | `offsets-0.log` | `UBoolProperty::FieldSize derived at +0x80 (Offset_Internal +0x54, UE=422)` — the numbers must be the game's own, not the defaults |
| UI  | ClassStructPanel | two **sibling bitfield bools on the same native class showing DIFFERENT values**. That is the whole point: with `boolFieldMask == 0` they were all reported true together, so identical values prove nothing |

**Negative control**: run a STOCK pre-4.25 title (OCTOPATH 4.18) and confirm the derived value is
**byte-identical to the old default** — `0x44 + 0x2C == 0x70`.

⛔ **CORRECTION 2026-09-05 — the original wording of this row was WRONG about 4.11, and in the
direction that would have manufactured a false pass.** It said 4.11 "should CHANGE, and the bools
should get better", because `0x50 + 0x28 = 0x78` is not the `0x70` default. But Ubel's probe
spread is `{base, base-4, base+4, base+8, base-8}`, so with the old `0x70` default it already
covered `{0x68, 0x6C, 0x70, 0x74, 0x78}` — and `0x78` is **base+8, inside that set**. The old
code therefore already landed on 4.11's true slot, which NEKOPALIVE's own live session
corroborates (`docs/test-games.md`, `Offset=+0x50`). NEKOPALIVE is a DLL-side-only control, not a
behaviour-change host. **DQ XI S remains the ONLY host that can show a UI-visible delta**, because
its shifted `0x54 + 0x2C = 0x80` is above the spread's `0x78` ceiling — and it is not installed,
so this row stays blocked on a host rather than on effort.

⚠ **Do not narrow Ubel's `{base, ±4, +8, −8}` spread now that the base is derived.** It is what
makes a misdetected version survivable: the two live deltas differ by exactly 4 and the
CasePreserving case by 8, both inside the spread. `Test_UBoolPropFieldSize` asserts those two
distances specifically so a future "cleanup" trips a test instead of removing the net silently.

*Also in the same commit: the `fieldSize` acceptance in `Frieren` and one site in `Ubel` accepted
`>= 1 && <= 8` where the other five copies required `== 1`. Tightened to match — the loose form
bought nothing and accepted `8`, which is a value the low byte of an 8-aligned pointer can present
when the probe lands off-field.*

-----

### ⬜ FIXED 2026-09-05, NEEDS A LIVE CHECK — audit A4: the UE 5.8 version marker

*The raise-only ladder in `UE5_Init` topped out at **507**, so a string-stripped UE 5.8 title
badged as UE 5.7 — the 507 predicate (`FUObjectItem` Object`@+0x08`) is satisfied by a 5.8
binary too, because that struct is byte-identical between the two. A 508 marker was added on
`~FFieldClass()` becoming virtual (5.7.4 `Field.h:100` non-virtual → 5.8.0 `:101` `virtual`,
unconditional; `FFieldClass` has no base and `FName Name` is its first member, so the vfptr
takes +0x00 and `Name` moves to +0x08).*

*The same commit widened the **507** marker's size set from `==24` to `{24, 32, 40}`. Object
`@+0x08` is the version signal; the SIZE varies with build configuration — 24 Shipping, 32
Development (STATS appends `TStatId`), 40 Test (UE 5.7's `Build.h` added
`ENABLE_STATNAMEDEVENTS_UOBJECT`, appending `TStatId` + `StatIDStringStorage`; audit A5). The
`==24` pin had silently excluded every stripped 5.7+ Development or Test build from the raise.*

**Why this cannot be closed by the C++ suite.** Both predicates are pinned by
`Test_VersionMarkers` (18 assertions, negative-controlled in both directions), but a predicate
returning the right answer for a given input proves nothing about whether the input is ever
produced. What is unverified is the *plumbing*: that a real stripped 5.8 game reaches
`UE5_Init` with `FFIELDCLASS_NAME` latched to 0x08, and that the raise then reaches the badge.

**Acceptance test** — needs a title that is UE 5.8 *and* string-stripped. No such game exists in
the corpus today (`docs/test-games.md` has no 5.8 row at all), so this is blocked on content,
not on effort. Two paired observables, and BOTH are required — a DLL-side raise with a stale UI
badge and a correct badge over a lucky RAW detection look identical on screen:

| side | where | expect |
|---|---|---|
| DLL | `init-0.log` | `structural marker (FFieldClass::Name@+0x08 = vfptr, UE5.8+) but version=507 — raising floor to 508` |
| DLL | `offsets-0.log` | `FFieldClass::Name=+0x08` on the same run (the latch the marker reads) |
| UI  | Pointers panel, "UE Version" card | the number reads **508**, not 507. ⚠ It is a RAW INT — `PointerPanel.axaml` binds `{Binding UeVersion}` with no converter, so it does **not** render as "UE 5.8". The only `"UE 5.8"` string in the app is the override ComboBox's static list, and that box reads **"Auto"** on an auto-detected run, so it is NOT a second observable. Status bar corroborates: `Connected — UE508`. |

**Negative control, and it is the one that matters**: run a genuine UE **5.7** title (Solarpunk,
`docs/test-games.md:57`) and confirm the 508 line does **NOT** appear and the badge stays 5.7.
Without it, a marker that fires unconditionally is indistinguishable from one that works.

⚠ **The badge is not independent corroboration of the probe.** A wrong `FFIELDCLASS_NAME` latch
has already broken the property walk by the time the marker runs, so a 5.8 badge on a run whose
walk also looks wrong means the probe was wrong — not that the game is 5.8. The log line says so
out loud, deliberately, because that is the sentence someone will read during triage.

⚠ `Genau::kVersionDetectLogicRev` is deliberately **not** bumped: `Frieren.cpp` documents this
ladder as runtime-only and re-applied on every init, so no cached detection needs invalidating.

-----

### ⬜ FIXED 2026-08-19, NEEDS A LIVE CHECK — audit L12 (INFO tier): MB3 / AC13 / AC14 / AC15 / AC17 / AE27 / AF25

*L12 closed **25 of the 26 INFO rows**; only these seven changed runtime behaviour. The other
eighteen need NO live check and are deliberately not listed: **AD23 / AB22 / AE28 / AF27 / Z17** are
comments only; **AD24 / AD25 / AD26 / AD27** are pinned by the C++ suite (258 utf8 + 1603 dll
assertions, AD25 negative-controlled); **AA35** is covered by the offline Lua rigs (83 / 154 / 91);
**AA36** touches only a CI checker and is proven by seven negative controls; **AE29** deletes a
method with zero callers; **AC16 / AB21 / AF24 / AF26 / AE26** are re-verified negative results with
no code change; **AB23** was not fixed (see its row). Categories are §10's A/B/C/D.*

⚠ **MB3 is the row to run FIRST.** It restructures the CE mailbox poller — the loop every `.CT`
command rides on, inside the game's own process — and **no test target compiles `Mimic.cpp`**, so
none of it has executed. Two changes: the dispatch `switch` now runs inside
`Routine::RunTickGuarded` so one throwing handler loses that command instead of ending the mailbox
for the session, and `CompoundOpGuard`'s destructor now detects unwinding
(`std::uncaught_exceptions()` vs the count at entry) and publishes `-11` instead of the stale
`result` — which for `HandleInvokeByName` was normally **0**, i.e. it reported SUCCESS for a command
that threw. **The regression risk is not the throw path (hard to trigger) but the ORDINARY path**:
if the lambda refactor broke plain dispatch, every CE command breaks at once. So the check that
matters is simply "do normal mailbox commands still work".

| # | ID | cat | what to do | expected |
|---|---|---|---|---|
| 1 | `MB3` | **B** | Inject, then run any two `.CT` rows that use the mailbox (Teleport save/recall, and an Invoke). Cheaper first step: `tools/verify/mailbox_addr.py` resolves `g_invokeMailbox` with **no CE**, so a scripted poke of one command is category **A**. | Both succeed exactly as before. `pipe-0.log` / `init-0.log` show no `Mailbox: tick threw` and no `result=-11`. A `-11` with a message means a handler really did throw — capture the log, that is a genuine find. |
| 2 | `MB3` | ✅ **CLOSED 2026-08-23** | The throw path itself. Needs a handler that actually throws — no way to force one on demand today. | If it ever fires: the mailbox keeps polling (subsequent commands still work) and the script reports `-11` + "the operation did NOT complete" rather than hanging at `status=PROCESSING`. |

> ### ✅ AE27 PASSES / 🟡 AC15 HALF 2026-08-21 `[AE27-AC15-2026-08-21]`
>
> **`AE27` — PASS, and the fixture really can falsify it.** DumperTest, Classes tab, **3,942
> classes**. The first look was misleading: the top of the list is all `//Script`, which would make
> a stale-cell bug invisible. Opening the Package box's autocomplete showed **three distinct
> packages** — `//Engine`, `//Game`, `//Script` — so the test has something to get wrong.
>
> Sorted by the Package column **both ways**, which moves rows between package groups:
>
> | direction | top of list | every cell vs its OWN row's Path |
> |---|---|---|
> | descending | `//Script` … | ✅ `GameEngine → //Script/Engine/GameEngine`, … |
> | ascending | `//Engine`, then `//Game` ×4, then `//Script` | ✅ `DmgTypeBP_Environmental → //Engine/EngineDamageTypes`, `ABP_Quinn_C → //Game/Characters/Mannequin`, … |
>
> Ordering is correct (`//Engine` < `//Game` < `//Script`), **no cell is blank, and every Package
> matches its own row's Path prefix across a full reorder** — which is exactly what a wrongly-keyed
> memo would break. Filtering by an exact package (`//Engine`) returned precisely the one class with
> that package.
>
> ⚠ The Package box is an **AutoCompleteBox with exact/prefix semantics, not substring**: typing
> `Script` matches nothing while the dropdown offers `//Script`. Worth knowing before anyone reads an
> empty grid as a bug — it is not the space=AND keyword-box contract, and arguably should not be,
> since it selects one package rather than searching text.
>
> **`AC15` — the Steam half passes; the drive scan was NOT run.**
> Proxy Deploy → **Scan Steam** → *"Found 18 UE game(s)"*, every row with a name, a real Binaries
> path, a deploy status and a suggested proxy. The `Version` column is **empty for 17 of 18**, and
> the one non-empty row is the only one whose status is `DeployedOutdated` — i.e. that is OUR
> deployed proxy's version, not a per-game `UeVersion`, which is what the row's "`UeVersion` was and
> remains null" asks for.
>
> ⚠ **Two honest limits.** The row's real assertion is a *before/after* comparison ("the same games
> are found with the same names/paths") and **no baseline exists here**, so what is shown is that the
> scan works and surfaces no per-game version — not that the set is unchanged. And the generic
> **drive scan was deliberately skipped**: it is a full walk of a 1.4 TB drive whose result has the
> same missing baseline, so it would cost minutes of disk for a conclusion no stronger than the
> Steam half's.
>
> 📌 Incidental, and NOT filed as a defect: the header keeps reading *"3,942 classes shown of 3,942
> total"* while a client-side Package or name filter narrows the grid to 1–2 rows. Both boxes behave
> the same way, and Property Search and Instance Finder report their query counts identically, so
> this is an app-wide convention (the line describes the QUERY, not the view) rather than a
> `[CLASSTOTAL]`-style honesty gap. Recorded because the word "shown" invites the other reading.
>
> ### ✅ MB3 — THE ORDINARY PATH PASSES 2026-08-19 `[MB3-POKE-2026-08-19]`, no Cheat Engine involved
>
> The row says the risk is **not** the throw path but plain dispatch: "if the lambda refactor broke
> plain dispatch, every CE command breaks at once". That is exactly what was tested, and it turned
> out to be **category A, not B** — `tools/verify/mailbox_poke.py` drives the mailbox from Python.
>
> **50 consecutive dispatches, 0 failures** (`--repeat 25`, alternating `CMD_QUERY_PTR`
> `QUERY_OP_GWORLD` / `QUERY_OP_GAME_ENGINE`; both are read-only and thread-agnostic, so they
> exercise the refactored dispatch `switch` without touching game state or needing the PE hook).
> `initState=2 (READY)`; round trips 5.4 ms / 6.7 ms.
> **Logs are clean: no `Mailbox: tick threw`, no `result=-11`, and 0 `[ERROR]` lines across all 8
> current log files.**
>
> ⭐ **Independently corroborated, not self-confirming**: the mailbox returned
> `&GWorld = 0x7FF6483188A0`, byte-identical to what `get_pointers` reports over the *pipe* — two
> different transports out of the same process agreeing. Its second output word
> (`UWorld* = 0x20144924B60`) also matches the address the F5 watcher dereferenced independently.
>
> ⚠ **Two rig bugs worth keeping, because both produced a confident WRONG answer first.**
> (1) `paramsData` is at **`0x328`**, not `0x030`; the wrong offset reads the tail of `funcName` and
> reports a silent all-zero output that looks like "the command returned nothing".
> (2) The DLL leaves `status` at `DONE` after a command, so a poller that only waits for `DONE`
> **returns instantly with the PREVIOUS result** — the second dispatch reported a bogus failure
> until the rig started writing `status = IDLE` before each trigger. Write `cmd` LAST; it is the
> trigger.
>
> **Step 2 (the throw path) remains open** — unchanged, there is still no way to force a handler to
> throw on demand. Step 1's remaining half (two real `.CT` rows through CE) is still worth doing in
> the CE batch, but it can no longer fail silently: plain dispatch is now known good.
| 3 | `AC14` | ✅ **PASS 2026-08-20** — connected the UI to DumperTest, closed it **while still connected**: `pipe-0.log` has **0** `Pipe: ReadLoop error` lines and ends with an orderly `Pipe lane dropped — tearing down both lanes for a clean reconnect` (once per lane). That entry used to be the NullReferenceException logged as if a fault. | | |
| 3b | `AC14` (original) | **B** | Connect the UI to an injected game, then close the UI **while still connected** (this is the `Dispose()` path that nulls `_reader` without awaiting the read loop). | `pipe-0.log` ends cleanly. **No `Pipe: ReadLoop error`** line — that entry was the NullReferenceException this fixed, logged as if an ordinary shutdown were a fault. |
| 4 | `AC13` | **B** | System tab → note the IPC figure. Then kill the game while the UI is mid-request so a write fails, and look again. | The IPC total now includes the failed request's transport time. Previously a write-path failure contributed exactly 0 ms, i.e. the figure flattered itself precisely when the pipe was misbehaving. |
> ### 🚫 AC13 IS NOT OBSERVABLE AS WRITTEN 2026-08-20 `[AC13-2026-08-20]` — do not spend a session on it
>
> **The reporting path works — that was checked first, so this is not a "couldn't get it to run".**
> DQ7R, a successful single Value Scan writes the figure the row asks you to note:
> ```
> PERF Value Scan (First): wall 298.7 ms · dispatcher busy 289.1 ms (96.8%) · 2 dispatches
>   · split dll 289.1 / ipc 6.3 / ui 3.3 ms (per call: dll 289.095 / ipc 6.304 / ui 3.345 ms)
> ```
> **`ipc 6.3 ms`** — that is "the IPC figure", and it lives in `view-0.log`, not on the System tab.
> (The System tab's *"Diagnostics — DLL dispatch cost"* card — `97 dispatches over 2,649.7s ·
> dispatcher busy 0.3%` — is the DLL-side dispatcher, a different number entirely.)
>
> **Why step 2 cannot work.** The figure is produced only by `DiagnosticsProbe`, and it is
> structurally silent on exactly the scenario the row prescribes:
> * `BeginAsync` swallows a failed opening `get_diagnostics` and leaves `before` **null**
>   ([DiagnosticsProbe.cs:76-80](ui/UE5DumpUI/Services/DiagnosticsProbe.cs:76)); `DisposeAsync` then
>   returns at `if (_dump == null || _before == null || _log == null) return;`.
> * If the pipe dies *mid*-operation, the closing `get_diagnostics` throws and `DisposeAsync` does
>   `catch { return; }` — commented *"disconnected mid-operation: nothing to report"*
>   ([:100-102](ui/UE5DumpUI/Services/DiagnosticsProbe.cs:100)).
>
> So **both** ways of making a write fail end with **no PERF line at all**, and the improved
> `PipeTransportStats` accounting has nowhere to appear. Killing the game to observe a figure that is
> only printed when the game is alive cannot succeed.
>
> **The fix itself is real and correctly placed** — `PipeClient.SendAsync` now wraps `SendCoreAsync`
> (write included) in the `try/finally` that calls `PipeTransportStats.Record`, where it used to wrap
> only `await tcs.Task` ([PipeClient.cs:195-203](ui/UE5DumpUI/Services/PipeClient.cs:195)). It is the
> *observation method* that is wrong, not the change.
>
> **What would actually check it** (cheap, and belongs in the test project rather than a live
> session): drive `SendAsync` against a writer that throws on `WriteLineAsync`, and assert
> `PipeTransportStats.Snapshot().Calls` incremented by 1 and `.Ms` by > 0 — plus the negative control
> the comment names, that a call refused by the **not-connected guard** adds **no** sample, since that
> guard deliberately sits above the timer.
| 5 | `AC15` | ✅ **PASS 2026-08-22** — both halves, each against an INDEPENDENT oracle, and the "no baseline exists" limit is now closed by proof rather than by observation. See `[AC15-ORACLE-2026-08-22]` below. | | |
> ### ✅ AC15 PASS 2026-08-22 `[AC15-ORACLE-2026-08-22]` — the drive half runs, and the missing baseline stops mattering
>
> Two earlier sessions closed as far as observation could reach and both recorded the same honest
> limit: *no pre-fix baseline exists on this machine*, so re-running the scan showed only that it
> still returns **something**. Re-reading the same list from the same code is not a second witness.
> This closes both gaps — the unrun drive half, and the limit itself.
>
> **1 — The set claim is now PROVED, not observed.** `git show 5374e662` on
> [ProxyDeployService.cs:417](ui/UE5DumpUI/Services/ProxyDeployService.cs:417) is the whole change:
>
> ```csharp
> // before:  try { var info = FileVersionInfo.GetVersionInfo(exePath); return null; } catch { return null; }
> // after:   private static string? TryDetectUeVersion(string exePath) => null;
> ```
>
> **Both forms return `null` on every path** — the `try` returned null, the `catch` returned null.
> The removed call could not influence the returned set at all, so "the same games with the same
> names and paths" holds by construction and needs no baseline. The dead load itself is gone from
> the scan path (the two surviving `FileVersionInfo` sites, :928 and :1395, read *proxy DLLs* via
> `IsOurProxyDll`, which is a different and intended use). ⚠ **No timing was measured** — "faster"
> rests on the removed work, not on a stopwatch.
>
> **2 — Both scans agree with an INDEPENDENT oracle** (`tools/verify/ac15_steam_oracle.py`,
> `ac15_drive_oracle.py` — the detector re-implemented in Python from the C# spec, no shared code).
> ⭐ Both were run and their answers written down **before** the UI was asked, which is the only
> ordering under which an oracle can disagree. UI = `dist\UE5DumpUI.exe` **v1.0.0.3315**, AOT.
>
> | mode | oracle | UI | agreement |
> |---|---|---|---|
> | **Scan Steam** | 18 games / 2 library folders, from 72 `steamapps\common` folders | `Found 18 UE game(s)` | **18/18, name for name** |
> | **Scan Drives, `D:`** | 22 games, `0` rows under a Steam root | `Found 22 UE game(s)` | **22/22, name and path** |
>
> ⭐ **The drive half's sharpest evidence is a NAME, not the count.** Ten of the 22 rows are called
> `Unreal Projects` rather than `DumperTest` / `StackOBot` / … — because prune-on-match fired at
> `D:\Unreal Projects` itself (Tier 3: a child holds `Binaries\Win64\*-Win64-Shipping.exe`) and
> `ScanGameFolder` then walked its children. The oracle produced the same ten identical names. A
> prune one level deeper yields **the same count of 22** with different names, so the count alone
> would not have caught it; the names pin the prune point.
>
> **3 — The path column is confirmed by CONTENT, not by reading it**
> (`tools/verify/ac15_path_witness.py`). The grid's `Status` / `Suggested proxy` cells are computed
> by opening files in the resolved folder, so they witness the path independently. All 18 Steam
> dirs exist; **11 carry one of our proxies and 7 carry none, and that split matches the Status
> column row for row** — the 2 `DeployedOutdated` rows are exactly the 2 holding a `dxgi.dll` of
> ours (the selected type), the 9 `DeployedOtherType` rows hold `version.dll` ×8 + `winmm.dll` ×1
> (OCTOPATH, swapped by `octopath_proxy_swap.py`), and the 7 `NotDeployed` rows hold nothing.
> A row pointing at the wrong directory could not produce that.
>
> **4 — `UeVersion` is bound by NOTHING.** The grid's `Version` column binds `InstalledVersion`
> ([ProxyDeployPanel.axaml:373](ui/UE5DumpUI/Views/ProxyDeployPanel.axaml:373)), and a whole-tree
> grep finds `DetectedGame.UeVersion` written at the two call sites and read only by
> `ProxyDeployTests.cs:526`'s `Assert.Null`. ⚠ So "the Version column is empty" — cited on
> 2026-08-20 — is **not** evidence about `UeVersion`; that column never showed it. (The 08-21 note
> already caught this; recorded here because the wrong reading is the natural one.)
>
> ⚠ **Still not covered:** no timing comparison. The 11 deployed proxies all predate
> `dist/proxy@3315`, which is the known post-republish `proxy_refresh.py` false alarm, **not** a
> finding.
>
> ⭐ **The 繁中 section is DELETED, and the reason is worth keeping.** Its three sub-steps are not
> three checks of AC15 — they are **one per item id in the heading**: sub-step 1 = `AC15`
> (this entry), sub-step 2 = `AE27` (Game Class Filter → Package column, ✅ `[AE27-AC15-2026-08-21]`,
> passed twice — DQ7R and DumperTest-on-AOT), sub-step 3 = `AF25` (✅ `[AF25-OPCODE-2026-08-22]`).
> All three ids are ✅ in the register, which is the checklist's own stated ground for deleting a
> section. Read as "three steps of AC15", sub-step 2 looks like outstanding work needing a game;
> it is not, and it had already been done. **A multi-id heading means the sub-steps may be
> independent items — check the register per id before scheduling one.** Bucket 第 2 步 11 → 10,
> total 33 → 32, re-derived from the file.
>
> ### ✅ AC15 PASS 2026-08-20 `[AC15-2026-08-20]` — both scanners still detect; ⚠ the two modes are NOT comparable
>
> | mode | result |
> |---|---|
> | **Steam** | `Found 18 UE game(s)`, from `Found 2 Steam library folder(s)` — the same 18 on **three** separate runs today (10:49, 12:06, 12:20), names and Binaries paths all populated |
> | **Scan Drives**, `D:` only | `Found 22 UE game(s)` — the reference builds under `D:\UE_Analyze_data\…` (`UE4.24`, `UE4.27.2`, `UE5.2.1`, `UE5.6.1`, `WindowsNoEditor`, …), names and paths populated, no errors |
>
> **`UeVersion` is null throughout**, as the row expects: the grid's Version column is empty for every
> detected game. The only rows carrying a version are ones where **our proxy** is deployed, and that is
> `InstalledVersion` (`1.0.0.3263`), a different field.
>
> ⚠ **Do not expect the two modes to agree — they are complementary by design.** The drive scanner's
> own tooltip says it: *"Scan the selected drives for non-Steam UE games … **Steam libraries and system
> folders are skipped**."* The D: drive scan therefore returns **none** of the 13 Steam titles that
> live on D:, and the Steam scan returns none of the 22 reference builds. Reading this row as "the
> same list twice" would report a defect where the design is working.
>
> ⚠ **Honest limit on the claim.** "The same games as before" cannot be checked here: no pre-fix
> baseline of either list exists on this machine. What *is* established is that removing the
> per-game VERSIONINFO load left both scanners detecting games, with names and paths intact, and with
> `UeVersion` null exactly as intended — i.e. the regression the row guards against is not present in
> anything observable today.
| 6 | `AE27` | ✅ **PASS 2026-08-21** — see `[AE27-AC15-2026-08-21]` below. | | |
> ### ✅ AE27 PASS 2026-08-20 `[AE27-DQ7R-2026-08-20]` — and the Path column cross-checks every memoized value
>
> **DQ7R**, Classes tab → Load → **4,393 classes shown of 4,393 total (scanned 149,408 objects)**.
>
> * **Package box:** `//Script` filters to rows whose Package cell reads `//Script` — populated, never
>   blank.
> * **Sort by the Package column:** ascending gives `//CriWare`, `//Engine`, `//Game`, `//Game`,
>   `//Game` with the sort arrow on the header. On the AOT `dist` build, which is where a
>   reflection-based DataGrid sort would fail if it were going to.
> * ⭐ **The strongest evidence is free and per-row:** the memoized `Package` agrees with the
>   independently-rendered `Path` on every visible row — `//CriWare` ↔ `//CriWare/AnimNotify_Pla…`,
>   `//Engine` ↔ `//Engine/EngineDamageTyp…`, `//Game` ↔ `//Game/UserInterface/Cap…`. A stale or blank
>   memo shows up instantly as a mismatch between two columns computed from different places.
>
> ⚠⚠ **The trap that almost produced a false defect report — read this before re-running.** The
> Package filter is a **prefix** match ([GameClassFilterViewModel.cs:209](ui/UE5DumpUI/ViewModels/GameClassFilterViewModel.cs:209)),
> and the values start with **two** slashes. Typing `Game`, `Script` or even `/Script` returns **zero
> rows**, while the header keeps reading `4,393 classes shown of 4,393 total` — because that line is
> the LOAD count, not the filtered count. The combination reads exactly like the blank-Package failure
> this row is about. It is not: `//Script` works. The `Package` column is also clipped in the default
> layout, so the leading `//` is easy to misread as `/`. Use the AutoCompleteBox suggestion (it offers
> `//Game`), not a hand-typed guess.
| 7 | `AF25` | ✅ **PASS 2026-08-20 `[AF25-CT-2026-08-20]`** — generated the real `.CT` from Teleport → CE Export → **Save .CT…** (34 rows, 281 KB) and read the emitted command numbers back. The file carries a section headed *"--- Teleport (17 rows) ---"*, and `writeInteger(mb + 0x00, 8)` appears **exactly 17 times** — the count matches the section header, so `CmdTeleport` is still **8** after the move to `CeMailboxLayout`. The other three agree three ways (DLL enum ↔ C# constant ↔ emitted script): **10** `CMD_MOVEMENT` ×8, **11** `CMD_FLY` ×11, **15** `CMD_TIME` ×4. `check_mailbox_contract.py` is green alongside. ⚠ "Run one" (an actual teleport) was **not** done — that needs CE plus a game with a controllable pawn. — ✅ **DONE 2026-08-22, and the premise was wrong too**: `[MB3-CT-2026-08-22]` ticked real `.CT` teleport records on **DumperTest**, which *does* have a controllable pawn (`ADumperTestCharacter : ACharacter`), with the pawn's pose as the witness. `[AF25-OPCODE-2026-08-22]` closed the opcode half. Struck from the B6 bucket 2026-08-24. | |
 Byte-identical script and working teleport. `CmdTeleport` moved to `CeMailboxLayout` but the value is unchanged (8), and the generator tests already assert the emitted text — this is belt-and-braces. |
| 8 | `AC17` | **C** | **Needs a real mount point.** Mount a fixed volume into a folder (`mountvol`, or Disk Management → Change Drive Letter and Paths → Add → empty NTFS folder), put a leftover proxy under it, then run Proxy Deploy → leftover cleanup → Execute. | The file goes to the Recycle Bin. Before this fix the fixed-drive pre-filter answered about the HOST volume (`DriveInfo` normalizes through `Path.GetPathRoot`), so it always said "Fixed" for mount-point paths and judged nothing. A removable volume mounted the same way should now be REFUSED. |


> ### ✅ MB3 STEP 1's CE HALF PASSES 2026-08-21 `[MB3-CE-2026-08-21]` — two real `.CT` row types, and the poller never threw
>
> The row wants "any two `.CT` rows that use the mailbox" driven through CE, with `pipe-0.log` /
> `init-0.log` showing **no `Mailbox: tick threw`** and **no `result=-11`**. Both command families
> have now been driven from real CE records:
>
> | mailbox family | driven by | count |
> |---|---|---|
> | `cmd=1` **CMD_INVOKE** | the baked-invoke record (`KismetMathLibrary::MakeTransform`, `[Y10-Y13-CE-2026-08-20]`) | **9** |
> | `cmd=6` **CMD_LIST_INSTANCES** | the class-wide Freeze records (`[AA2-STEP2-2026-08-21]`, `[FREEZESCOPE-CFG-2026-08-21]`) | **403** (221 + 182) |
> | `cmd=4` | incidental | 1 |
>
> * **`Mailbox: tick threw` — 0 occurrences in EVERY log on this machine**, not just today's.
> * **`result=-11` — 0 occurrences**, likewise.
> * `GameThreadDispatch: invoke completed result=0` on the invoke side; the poller logged
>   `polling thread started (poll=1ms)` and kept serving across hundreds of commands.
>
> ⚠ **Substitution named rather than hidden.** The row's example pair is "Teleport save/recall **and**
> an Invoke". Teleport was **not** the second family — Freeze/LIST_INSTANCES was. Teleport needs a
> **controllable pawn**, which DumperTest does not have, and that same requirement is what still
> blocks `AF25`'s "run one" below.
> ⛔ **BOTH HALVES OF THAT SENTENCE ARE NOW FALSE — corrected 2026-08-24, because it is a premise
> that keeps re-blocking teleport rows.** DumperTest **does** have a controllable pawn
> (`ADumperTestCharacter : ACharacter`; `tools/verify/seethrough_arm_a.py` drives it with
> `teleport_relative`), and `AF25`'s "run one" was **closed 2026-08-22** — `[MB3-CT-2026-08-22]`
> ticked real `.CT` teleport records with the pawn's pose as the witness (900 → 1000 → 900.000 /
> 1110.000 / 92.013), and `[AF25-OPCODE-2026-08-22]` closed the opcode half. Nothing below this line
> is still blocked on a pawn. What the row is actually asserting — that the restructured poller
> survives real `.CT` traffic without throwing — is tested by two *distinct* command families either
> way; a third would not add a new kind of evidence, only a third data point.
>
> **Step 2 remains ⛔ by construction**: it needs a handler that actually throws, and there is still no
> way to force one on demand.


### ⚠ Incidental: `errorMsg` is not cleared on success `[MBERRSTALE-2026-08-23]`

Every successful dispatch after the throw still carried the **previous** error text —
`GWORLD result=0 … err='command handler threw …'`, and after the revert
`GAME_ENGINE result=0 … err='Unknown command'`. `result` is authoritative and correct, so nothing is
broken, but a caller that reads `errorMsg` without checking `result` first sees a stale failure on a
successful command. Same family as audit #4's root cause (*the report and the reality are computed by
different code paths*). ✅ **FIXED 2026-08-23** (build 3338).

> **The fix is a PRE-clear, not a post-clear, and the distinction is the whole point.**
> Clearing `errorMsg` *after* a handler ran would wipe the message a handler had just written via
> `SetError`. It is cleared where the command is picked up — right after
> `status = STATUS_PROCESSING` — so `errorMsg` is empty on success and populated on failure, which
> is the only consistent pairing.
>
> Root cause, confirmed in both functions: **`SetError` writes `errorMsg`; `SetDone` writes only
> `result` and never touches it.** So a success inherited whatever the last failure left.
>
> **Verified live in both directions** on DumperTest / DLL 3338, using the exact observation that
> exposed the bug:
>
> | | before | after |
> |---|---|---|
> | a FAILING command | `cmd=16 → result=-1 err='Unknown command'` | `cmd=16 → result=-1 **err='Unknown command'**` |
> | a SUCCEEDING command | `GWORLD result=0 … err='command handler threw …'` | `GWORLD result=0 … **OK**` (no `err`) |
>
> ⭐ The failing row is the **built-in negative control**: real errors still surface, so the fix is
> not a blanket wipe. `check_mailbox_contract.py` green before and after (no layout change).
>
> ⚠ **A trap worth recording — the edit briefly wrote a NUL byte into a C++ source file.** The
> patch script was passed through a shell heredoc, which collapsed the source's `'\\0'` down to `'\0'`; Python then
> emitted a **literal NUL** rather than the two characters backslash-zero. The tell was the diff:
> `1483 insertions / 1470 deletions` on a 13-line edit, because git treats a file containing NUL as
> **binary** and reports the whole thing as changed. Line endings were never the problem (CRLF
> 1470 → 1483, exactly the 13 inserted lines). Restored from a byte snapshot and rebuilt the
> backslash numerically as `bytes([92])`. **A whole-file diff on a small edit means the file was
> corrupted, not reformatted — check for NUL before assuming line endings.**

### ⬜ DEFERRED, NOT A VERIFICATION ITEM — AB23: intern `GroupSlotMatch::ownerClass`

*Referenced from `dll/src/Radar.h` (the `kMaxGroupSessionLeaves` block) and from AB23's register row,
which stays **open** — this is unshipped work, not something awaiting a live check. Listed here so
those pointers resolve; it is counted by `check_audit_register.py`, not by the OPEN FIXES INDEX.*

`GroupSlotMatch` carries a by-value `std::string ownerClass` per LEAF, which is the per-record heap
string V3-A's interning was built to remove. `GroupSession` already has the machinery — `descriptors`
and `instances` pools, reached through `internDesc` / `internInstance` in `ScanForValueGroup` — so the
shape of the fix is settled: add an owner-class pool, replace the string with a `uint32_t` index, and
update the single reader (`Fern.cpp:377`, `lj["owner_class"]`). Six sites in total: four writes in
`Aura.cpp` (`:8427`, `:8774`, `:8899`, `:8965`), that one read, and the declaration.

**Why it was not done in L12:** no test target compiles `Aura.cpp` or `Fern.cpp`, so a refactor of the
group-scan hot record could only be verified in-game, and L12 ran unattended. What *was* done is the
half that could be made safe offline — the memory accounting the finding exposed. The budget's
justification read "~120 B per `GroupSlotMatch`, so 4,000,000 leaves is roughly half a GB", counting
the string OBJECT and not its heap block; UE class names routinely exceed the SSO buffer, so the real
ceiling is materially higher. The size is now derived (`kGroupSlotMatchBytes = sizeof(...)`) so it
cannot go stale, the under-count is stated, and a `static_assert` guards the premise by failing if
`kMaxGroupSessionLeaves * sizeof(GroupSlotMatch)` ever reaches 1 GB.

**Do the interning together with a raise of `kMaxGroupSessionLeaves`, not before it** — the cap is the
only thing that makes the per-leaf cost matter, and today's cap is far above any observed scan.

### ⬜ FIXED 2026-08-19, NEEDS A LIVE CHECK — audit L11 (U1/U4 + stragglers): V8 / V10 / V11 / W8 / Y10 / Y11 / Y12 / Y13 / F5

*L11 was the LAST LOW batch. **V9** (the Object Tree's Cancel button could not cancel a search) and
**Y14** (the baked export announced N params over values it had failed to parse) need NO live check —
both are driven end to end by the real ViewModel / real generator in `AuditL11HonestyTests`, and both
negative-controlled. **U18** is comments only. What follows is the rest.*

⚠ **F5 is the row to run FIRST and the one to worry about.** It is the only change in this batch that
touches the pipe every other feature rides on, in the game's own process: `MakeResponse` /
`MakeEvent` no longer splice their payload with nlohmann's `merge_patch` (per-key assignment
instead, which cannot delete an envelope key or be replaced wholesale by a non-object payload), and
`Fern::WriteLine` no longer materialises a `line + "\n"` copy — payload and terminator now go out as
two `WriteFile` calls under the same `writeMutex` on the byte-mode pipe. `Renge::ApplyPayload` is
pinned by 16 assertions in `dll_helpers_test` (the header IS compiled there), but **nothing compiles
`Fern.cpp`**, so the two-write split has never executed. If anything in this batch breaks a session,
it is this.

⚠ **Y10 / Y13 did NOT move the mailbox contract, and that is worth confirming rather than assuming.**
Both changes are script-side: a contract check placed before the first write, a pre-zero loop clamped
to the 1024-byte params region, and a wider Before/After dump window. Nothing about the LAYOUT
changed, `tools/check_mailbox_contract.py` passes unchanged, and the emitted script still bakes
contract **3** (min 1). A `.CT` saved before this batch stays valid.

> | # | cat | 做什麼 | 預期 |
> |---|-----|--------|------|
> | 1 | **A** | **F5.** With the UI **disconnected** (⛔ `kMaxPipeInstances=3` and the UI holds 2 — see `[PIPEBUSY]`), drive `tools/verify/pipe_client.py` against an injected game and send `snapshot_chunk`, `find_instances` (a class with thousands of instances) and `list_all_functions`. | Every reply parses as one JSON object per line and carries **all three** envelope keys `id` / `ok` / `game_thread_stalled` alongside its payload. The big ones matter most: they are the responses whose second copy the fix removed, and the two-`WriteFile` split is what could truncate or interleave them. |
> | 2 | **A** | **F5, the interleave control.** Same session: start a `watch` so the DLL pushes EVT_WATCH events on one connection while you issue ordinary commands on the other, for a minute. | No malformed line, ever. Both writes for a message happen under one `writeMutex`, so a watch event must never land in the middle of a response. A single garbled line here refutes the split and the change should be reverted to one `WriteFile`. |
> ### ✅ F5 STEPS 1 + 2 PASS 2026-08-19 `[F5-WIRE-2026-08-19]` — headless, DumperTest Development, dist 3263
>
> Rig: `tools/verify/f5_envelope.py`. ⚠ It does **not** use `PipeClient.request` to judge lines:
> that method silently `continue`s past any line it cannot parse, which is right for driving the
> DLL and **fatally wrong here**, where a malformed line is the entire subject — it would be
> dropped and the run would report a clean pass. The rig keeps every raw byte and judges the lines
> itself, distinguishing *truncated* from *two objects on one line* (they mean different bugs).
>
> * **Step 1 — PASS.** The big replies, the ones whose second copy the fix removed:
>   `list_all_functions` **961,873 B in 0.15 s**, `list_classes` 397,101 B, `find_instances` 48,102 B,
>   `begin_snapshot` + 3× `snapshot_chunk`. **Every reply carried all three envelope keys**
>   (`id` / `ok` / `game_thread_stalled`) and **9 of 9 wire lines were well-formed**.
>   (Incidentally re-confirms the `list_all_functions` timing note: 0.15 s, not minutes.)
> * **Step 2 — PASS, and the control is NOT vacuous.** 60 s, two connections: the main one issued
>   **17,205 commands**, the watch one received **187,553 lines including 1,179 real `watch`
>   events** — so a second writer genuinely competed for `writeMutex` throughout.
>   **204,758 lines total, ZERO malformed.** The two-`WriteFile` split never truncated a payload
>   and no event ever landed inside a response.
>   ⚠ Two traps this step nearly fell into, both now guarded in the rig: the parameter is **`addr`,
>   not `address`** (`Fern.cpp:4961`) — the wrong name is accepted as `addr=""`, i.e. a watch on
>   nothing; and the watched address must be one that **changes** (the `&GWorld` *slot* is static —
>   watch the `UWorld` it points at). With either wrong, "no malformed line" is trivially true of no
>   lines, so the rig now reports **INCONCLUSIVE** rather than PASS when 0 events were pushed.
> * **Step 3 (the UI regression control) — NOT RUN**, it needs the UI on screen. Deferred to the
>   UI batch; steps 1 and 2 are the ones that could only be done headless.
> ### ✅ F5 STEP 3 PASSES 2026-08-20 `[F5-ORDINARY-2026-08-20]` — 161,009 log lines, zero malformed
>
> The regression control does not need a dedicated session: **today already was one**. Between them,
> this day's runs drove DumperTest (Development *and* Shipping), Elliot and DQ7R through Object Tree
> loads, Live Walker drills and auto-refresh, single and **group** value scans, a full 10.5 MB Dump
> All, `list_all_functions`, `search_properties`, 80 × `walk_function_props`, `force_field`,
> Instances/Properties/Interesting Funcs/Props loads, Proxy Deploy scans and deploys, plus the
> headless `pipe_client` batches — i.e. far more envelope traffic than "a few minutes" of use.
>
> Sweeping **every** log file carrying a `2026-08-20` line:
> ```
> files with today's lines : 189
> lines dated 2026-08-20   : 161,009
> malformed / parse error  : 0
> ERROR-level lines        : 7
> ```
> ⚠ The sweep had to include **rotated archives**, not just `*-0.log`: the UI was restarted several
> times today for `AF10`/`AF11`, so its earlier logs are already archived and a `-0.log`-only glob
> reported a **falsely clean** 0 ERRORs.
>
> All 7 errors are accounted for and none is an envelope fault: 4 are the 10:05 DumperTest PE-hook
> `VALIDATION FAILED` + two 5 s invoke timeouts from a pre-existing session, 1 is Satisfactory's
> `FindGObjects: All patterns and fallback scan failed` at 07:27, and 2 are the **same** line — this
> session's own deliberate `[STAGELOCK]` test (`Deploy … failed: Access to the path is denied.`),
> which appears twice only because the UI mirrors its view log into the connected game's folder as
> `ui-view-*.log`. That mirror was checked rather than assumed to be misfiling.
>
> Not one malformed line in 161K, across three games and two write paths — which is what "the
> envelope change is invisible when it works" looks like when it is measured instead of asserted.

> ### ✅ W8 PASS 2026-08-20 `[W8-USMAP-2026-08-20]` — checked by COUNT, which needs no "before"
>
> The row asks for a comparison against "the same game before this build", and no such baseline
> exists here. But its assertion is **quantitative** — *"the struct count rises by roughly the number
> of `BlueprintGeneratedClass` objects in the game"* — so the same claim can be tested by asking
> whether those classes are in the map **at their full count**, which needs only one run.
>
> **DQ7R** (UE 4.27, 149,408 objects, Blueprint-heavy) → Export → **USMAP** →
> `out\DQ7R-Win64-Shipping.usmap`, **2,786,463 bytes**, magic `C430`, version 4, **compression byte
> 0** (uncompressed, so the name table is readable without a decoder).
>
> | measurement | value |
> |---|---|
> | distinct names ending `_C` in the .usmap | **507** |
> | `BlueprintGeneratedClass` **family** instances in the game | **513** |
>
> ⭐ **507 of 513 — 98.8 %.** Essentially every Blueprint-generated class in the pool has a name in
> the exported map, which is exactly the population the fix added. Sample entries:
> `ABP_LuckyPanelCard_C`, `ABP_NPC_Accessories_Phy_C`, `ABP_NPC_Bandana_Phy_C`.
>
> ⚠ **The family count is the one that matters, and Exact match hides it.** An Instances search for
> `BlueprintGeneratedClass` with **Exact match ON** returns just **89** — it excludes
> `AnimBlueprintGeneratedClass` / `WidgetBlueprintGeneratedClass`, which are subclasses and are most
> of the population here. Unticking Exact match gives 513. Comparing 507 against 89 would have looked
> like a wild over-count instead of a match.
>
> ℹ️ Scope: this shows the classes **are present at full count**, not the *delta* against a pre-fix
> export. If a genuine before/after is ever wanted, this file is the "after" for DQ7R.
> | 3 | **A** | **F5, the ordinary path.** Connect the UI normally and use it for a few minutes — Object Tree load, Live Walker drill, a value scan. | Everything behaves as before. This is the regression control; the envelope change is invisible when it works. |
> | 4 | **B** | **W8.** On a Blueprint-heavy shipped title, Tools → export the `.usmap`, and compare the "N structs" line against the same game before this build. | The struct count rises by roughly the number of `BlueprintGeneratedClass` objects in the game (thousands, not a handful), and a known `BP_*_C` / `WBP_*_C` name is now present. Load the file in FModel / CUE4Parse if it is installed — the `W1/W7` item already wants that parser. |
> | 5 | **B** | **V10.** On a title where the first scan leaves GObjects **or** GWorld unresolved, press **Extra Scan** and wait for it to finish. | The green "Found: GObjects: 0x…" result **stays on screen**. Before the fix it appeared and was blanked a few ms later by the pointer refresh the scan itself triggered. Then, mid-scan, change the **UE version** ComboBox: the Extra Scan button must stay disabled until the scan really ends. ⚠ Sample-blocked if every installed title resolves both pointers on the first pass. |
> | 6 | **B** | **V11.** With CE + the AOBMaker plugin connected, click **Register symbol** on the GWorld card, then again with **CE closed**. | Success prints a teal line naming `gworld_addr`; the failure prints a RED line naming it. Before the fix both produced *nothing at all* on screen. Repeat on the **&GEngine** card — it was the second site, found by the sibling grep. |
> ### ✅ 7a DETECTOR (i) PASSES 2026-08-21 `[L11-7A-UI-2026-08-21]` — decided from the UI, no CE
>
> Driven end to end through the app: Interesting Functions → "Game Only" off, "Show All" on → filter
> → **AA(B)** → tick **Verify return value** → **Copy AA Script**, then the clipboard asserted
> programmatically. Eight assertions over two functions, and the second is what makes the first mean
> anything:
>
> | | `ComposeTransforms` — ParmsSize **288**, ret ends 288 | `MakeTransform` — ParmsSize **176**, ret ends 176 |
> |---|---|---|
> | `local _DUMP_LEN =` | **256** ✅ | **176** ✅ |
> | return described | `(fstruct@192, size=96B)` ✅ | `(fstruct@80, size=96B)` ✅ |
> | `see After: dump above` | **absent** ✅ | **present** ✅ |
> | `past the … dump window above` | **present**, naming `+192` ✅ | **absent** ✅ |
>
> ⭐ **The control is the whole design.** Both rows come from the same dialog, the same defaults and
> the same Verify tick — the only difference is a function whose return ends inside the dump window
> instead of past it, and the two emit **opposite** phrasing. Without it, "the >256 case omits the
> phrase" would be equally consistent with the phrase having been deleted outright.
>
> ⚠ The Params cell reads `3 (288B` / `4 (176B` — the row's expected "2 (288B)" was a
> miscount of the parameters, not of the size; the **288B** is what matters and it matches.
>
> ⚠ **Do not conflate this with the pre-zero clamp.** 288 is far below 1024, so the clamp is still
> unexercised and remains its own open sub-item needing a `ParmsSize > 1024` function —
> `ToolMenuEntryExtensions::InitMenuEntry` (1104) is on the census shortlist for whoever takes it.
>
> Scripts kept at `out/y13b/composetransforms.lua.txt` and `out/y13b/maketransform.lua.txt`.
>
> ### ✅ 7a HAS FIXTURES — census 2026-08-21 `[L11-7A-CENSUS-2026-08-21]`
>
> The row's open half needs a UFunction whose **complex return ends past byte 256**, and the
> question "does one exist here" is a census, not a judgement. `py tools/verify/l11_7a_ret_census.py`
> on DumperTest / dist 3308, read-only, two stages:
>
> * stage 1 — **9,806** functions over **3,942** classes, `truncated=false aborted=false`. ⚠ That
>   pair is asserted, not reported: a capped walk can only support "none in the part I looked at",
>   which is not the claim. **185** functions have `parms_size > 256`, over 45 classes.
> * stage 2 — `walk_functions` on those 45 → **67 fixtures**.
>
> Best candidates for the UI step, all on DumperTest:
>
> | class | function | ParmsSize | return |
> |---|---|---|---|
> | `KismetAnimationLibrary` | `K2_LookAt` | 288 | `StructProperty@192` size 96 → ends **288** |
> | `ToolMenuSectionExtensions` | `GetLabel` | 312 | `TextProperty@296` size 16 → ends **312** |
> | `UserWidget` | `OnPreviewKeyDown` | 304 | `StructProperty@120` size 184 → ends **304** |
> | `ToolMenuEntryExtensions` | `InitMenuEntry` | 1104 | `StructProperty@80` size 1024 → ends **1104** |
>
> ⭐ `K2_LookAt`'s 288 / @192 / 96 is *exactly* the shape the plan predicted for
> `KismetMathLibrary::ComposeTransforms` — the same numbers on a different class, which is a useful
> independent confirmation that the shape is what 7a needs rather than a one-off.
>
> ⭐ **NEGATIVE CONTROL: 609 complex returns that stay INSIDE the buffer were correctly NOT
> flagged** — `MakeTransform`-shaped cases ending at 24, 64, 120, 136, 144. Without that count the
> classifier could be matching "complex" and ignoring the boundary entirely, and all 67 hits would
> be worthless. 67 flagged against 609 not-flagged is the discrimination.
>
> ▶ So 7a is **exercisable here**; what remains is the UI half (Batch 2.3), not a fixture hunt.
>
> ### 🟡 Y13 PASSES / Y10's CE HALF IS 3-OF-4 2026-08-20 `[Y10-Y13-CE-2026-08-20]` — and the miss is `[FREEZEUNTICK]` in a SECOND generator
>
> Driven end to end in CE for the first time. Subject: **`KismetMathLibrary::MakeTransform`**
> (`ParmsSize=176`, params `Location` off=0 / `Rotation` off=24 / `Scale` off=48), chosen because the
> row needs a **complex return whose slot sits past byte 32** — the DLL reports it as
> **`ReturnValue (fstruct@80, size=96B)`**, i.e. at byte **80**. Baked with Location `11/22/33`,
> Rotation `0/0/0`, Scale `1/1/1`, **Verify return** ticked, pushed to CE via AOBMaker
> (`AOBMaker: created AA script 'Invoke (baked): KismetMathLibrary::MakeTransform'`), with
> `ue5_invoke_helper.lua` injected first.
>
> **Y13 — the dump reaches the return slot. PASS.** Ticking the record printed:
> ```
> [Invoke] Before: 00 00 … (all zero, full buffer)
> [Invoke] After : 00 00 00 00 00 00 26 40 | 00 00 00 00 00 00 36 40 | 00 00 00 00 00 00 80 40 | …
>                  … F0 3F … 26 40 … 36 40 … 80 40 … F0 3F  F0 3F  F0 3F …
> [Invoke] OK: KismetMathLibrary::MakeTransform -> ReturnValue (fstruct@80, size=96B)
>              -- complex return; see After: dump above
> ```
> The head decodes as the inputs (`0x4026…`=11.0, `0x4036…`=22.0, `0x4040 8…`=33.0), and **the tail
> is the returned FTransform itself** — quaternion `(0,0,0,1)` (the lone `F0 3F` after a zero run),
> translation `11 / 22 / 33`, scale `1 / 1 / 1`. So the window not only *reaches* offset 80, the
> return value is **present, complete and correct** in it. The `see After: dump above` wording is
> therefore accurate here rather than a false promise, which is the half the row cares about.
>
> ⚠ The row's other clause — *"the line no longer says 'see After: dump above' when it cannot"* — was
> **not** exercised: with the window now sized to the full `ParmsSize`, no reachable function on this
> host produces a return the dump fails to cover. Recorded as unexercised, not as a pass.
>
> **Y10 — the contract check. 3 of 4.** Staged by attaching CE to a **sacrificial `python.exe`**
> instead of the game (a deliberate choice over the UI or the maintainer's notepad++, since the whole
> question is whether a stray `writeByte` runs). Re-ticking the record gave:
>
> | assertion | result |
> |---|---|
> | the contract check fires **FIRST** | ✅ — the only thing that happened was the check |
> | its message **names `g_mailboxContract`** | ✅ — `[Invoke] could not resolve g_mailboxContract, even after re-reading the module list.` …and it then lists three causes, the second being *"CE is attached to a different process, or to a stale PID from an earlier run (that looks identical to being attached)"* — i.e. it correctly describes the exact state staged |
> | **no `writeByte` may have run** | ✅ — the Lua Engine gained **no second `[Invoke] Before:` dump**. The successful run's Before/After pair is still the only one in the buffer, so the mailbox was never written |
> | the record must **untick itself** | ❌ 2026-08-20 → ✅ **CLOSED BY CONSTRUCTION 2026-08-24 `[Y10-UNTICK-BYCONSTRUCTION-2026-08-24]`**, see the block below |
>
> ⭐ **The miss is not a new defect — it is `[FREEZEUNTICK-2026-08-20]` in a SECOND generator.** Until
> now that defect was only ever seen in the Freeze script. Here the identical shape appears in the
> **baked-invoke** script: a bail-out that applied nothing leaves the record ACTIVE. Combined with
> `[FREEZESTUCK-CE-2026-08-20]`, which showed the **deferred** untick working in real CE, the picture
> is consistent across three scripts: **in-`[ENABLE]` `memrec.Active = false` never survives; a
> deferred one-shot timer always does.** The fix therefore belongs in the shared emitter, not in
> `FreezeScriptGenerator` alone — see the widened note on the defect's own block.
>
> #### ✅ CLOSED 2026-08-24 `[Y10-UNTICK-BYCONSTRUCTION-2026-08-24]` — and the test that was supposed to cover it could not have
>
> **No CE session was needed, and the row's own premise had already been fixed.** The shared-emitter
> fix (`[AA12-BAILOUT-2026-08-21]`) landed after this ❌ was recorded and was never re-checked here.
> Closed offline by a two-link chain, both links shown able to fail:
>
> | link | what it establishes |
> |---|---|
> | `scripts/tests/untick_bailout_test.lua` (10/10, run under real `lua`) | the emitted **deferred** shape actually unticks against CE's `setActive` lifecycle, modelled from CE's own `memoryrecordunit.pas:2573` — and the immediate shape provably does not |
> | `CeMailboxBailoutTests` (250 cases) | the baked-invoke script's contract bail-out emits **exactly** that shape, carries no immediate untick, and bails out before any mailbox write |
>
> ⭐⭐ **Getting there found a live gap and a non-discriminating assertion — the row was right to stay
> open, just not for the reason it said.**
>
> **(a) The fixture was half-wired.** `Baked.Invoke.Verify` was added to `EveryEnableScript()`
> specifically because *"the contract bail-out was covered by nothing at all"* (its own comment).
> But the three contract theories — including `AFailedContractCheckUnticksTheRecord`, whose entire
> subject is that bail-out — were left pointing at `MailboxScripts()`, which does not contain it. So
> the fixture reached the *shape* theories and missed the one it was created for. Replaced the
> hand-list with `ContractCheckingScripts()`, **derived** by looking for the contract symbol in the
> emitted text, so a generator that grows a contract check later is picked up without anyone
> remembering. `Baked.Invoke` (no `verifyReturn`, no contract check) is correctly excluded by the
> same derivation rather than by an exemption.
>
> **(b) Wiring it in alone would have changed nothing** — and this is the part worth keeping.
> The assertion was `IndexOf("memrec.Active = false", check)` with **no upper bound**, i.e. *"an
> untick exists somewhere after the contract symbol"*, which is not the claim being made. Measured:
> the baked script's contract bail-outs sit at emitted lines 54–81, and a completely unrelated
> untick sits at **line 132**, inside the verify-mode `OnTimer` body. Bounded the window at the
> **first `return` after the check**.
>
> ⚠ **Negative control, run twice, and the delta is the proof.** Breaking
> `CeLuaHygiene.DeferredUntickLua()` so every bail-out loses its untick:
>
> | | rows caught by `AFailedContractCheckUnticksTheRecord` |
> |---|---|
> | before | **11** — the MailboxScripts toggles only; `Baked.Invoke.Verify` **passed**, borrowing line 132 |
> | after | **12** — the twelfth is exactly `Baked.Invoke.Verify` |
>
> So both halves were necessary: (a) put the row in the theory, (b) make the theory able to fail for
> it. Control reverted; `CeLuaHygiene.cs` byte-identical to HEAD; 4,716 UI tests green, 13 gates 0.
>
> ⚠ **Honest limit.** This is offline. It does not prove CE **7.7** behaves as the model says — the
> model is derived from CE's published Pascal, which [CE-Bugs-Minesweeper.md](CE-Bugs-Minesweeper.md)
> records as *lagging the release*. What it does prove is that the **shape** that caused the original
> ❌ (an in-`[ENABLE]` immediate untick) is now excluded by two independent tests, and that the shape
> that replaced it is the one `[FREEZESTUCK-CE-2026-08-20]` already observed working in real CE.
> A live re-tick would add a third witness; it is no longer the only route.
>
> ℹ️ **Incidental but useful for the CRLF fix:** `Tools → Inject Helper into Current CE Table`
> reported **`Inject helper OK: ue5_invoke_helper.lua embedded (…)`** — no mismatch — in the same
> session where the *Freeze* helper reported `Stream size mismatch`. Two helpers, one injection path,
> only one complaining: that isolates `[FREEZEINJECT-CRLF]` to the **freeze helper file's line
> endings** rather than to the injection code, which is a cheaper thing to fix and a sharper thing to
> test.
>
> ⚠ **Rig traps, both of which cost time here.** (1) `Copy AA Script` **only writes the clipboard as a
> FALLBACK** — with AOBMaker available it calls `CreateAAScriptAsync` and touches no clipboard, so
> "the clipboard did not change" is *expected*, not a defect (a live control confirmed the clipboard
> read path itself works). (2) Three clicks produced nothing at all because **CE had no process
> attached**; `CreateAAScriptAsync` then fails and the result label reports it — but the label sits
> below the buttons and is pushed **off-screen when the dialog is maximized**, so the outcome is
> invisible in exactly the state a large param list tempts you into. Restore the dialog before
> judging whether a push worked.

> ### ✅ V11 PASSES ON BOTH CARDS, BOTH OUTCOMES 2026-08-20 `[V11-SYM-2026-08-20]`
>
> **The defect V11 was filed for is that the panel looked identical whether CE had registered the
> symbol or the bridge never reached CE at all** — both call sites branched the bool only to pick
> `_log.Info` vs `_log.Warn`, so the user's next action (rooting a CE record on that symbol) resolved
> to nothing with no hint why. All four combinations were driven on DumperTest Development (UE504,
> 25,179 objects) with the `dist` AOT UI:
>
> | card | CE + plugin | on screen |
> |---|---|---|
> | GWorld **SYM** | connected | **teal**: `Registered CE symbol 'gworld_addr' — it re-scans on enable, so it survives a game restart.` |
> | &GEngine **SYM** | connected | **teal**: `Registered CE symbol 'gengine_addr' — it re-scans on enable, so it survives a game restart.` |
> | GWorld **SYM** | CE killed | **RED**: `Could not register CE symbol 'gworld_addr'. AOBMaker accepted the request but CE did not create the script — check that Cheat Engine is still open and attached to this game, then try again.` |
> | &GEngine **SYM** | CE killed | **RED**: `Could not register CE symbol 'gengine_addr'. …` (same wording) |
>
> Every line **names its symbol**, success and failure are visually distinct (teal status vs the red
> error banner), and the second site — the `&GEngine` card the sibling grep turned up — behaves
> identically to the first, which is what `ReportSymbolRegistration` being shared is supposed to
> guarantee.
>
> **Second, independent detector: the log agrees four times out of four, at the right levels.**
> ```
> 21:38:29 [INFO] Created CE symbol script 'gworld_addr'  (AOB: 48 8B 1D ?? … , pos=3,  len=7)
> 21:39:03 [INFO] Created CE symbol script 'gengine_addr' (AOB: 48 83 EC 2? … , pos=10, len=14)
> 21:39:50 [WARN] Failed to create CE symbol script 'gworld_addr'  (…)
> 21:40:14 [WARN] Failed to create CE symbol script 'gengine_addr' (…)
> ```
> That matters because the *screen* is the thing V11 changed — the log split already existed — so
> agreement between them is what shows the new UI report is driven by the real outcome rather than
> being an optimistic message printed unconditionally.
>
> ⚠ **Navigation note for whoever re-runs this: the "Register symbol" control is the small `SYM`
> button on the GWorld / &GEngine cards of the *`System` tab*.** `str.Tab.Pointers` renders as
> **"System"**, so the panel the register lives on is not called Pointers anywhere on screen — and
> the Teleport tab's *Global Pointers → Cheat Engine symbols* card is a **different feature** (it
> publishes `UE_GWorld` / `UE_GameEngine`, build 1978). Clicking Teleport's `Get GWorld` and reading
> its teal line would look like a V11 pass while exercising none of V11's code.
>
> ℹ️ Observed, not filed: the toolbar badge still read **AOBMaker Connected** while both failures
> were produced, because it only re-probes on the ⟳ button or tab activation. The red message says
> "check that Cheat Engine is still open", so the user is not misled about what to do — but a badge
> and a banner disagreeing on screen at the same moment is worth a glance if the badge is ever made
> load-bearing.

> ### ✅ Y10's CONTRACT-BEFORE-WRITE HALF PASSES 15/15 2026-08-20 `[CONTRACT-ORDER-2026-08-20]`
>
> `scripts/tests/contract_check_test.lua` runs the **real `[ENABLE]` block the shipping UI emitted**
> (working-lessons §2.8) over stubbed CE globals, with **every mailbox write recorded** so "nothing
> was written" is measured rather than assumed.
>
> The ordering is the whole point: the contract check must happen **before the first write**, because
> the thing in question IS the layout — if the script's field offsets are wrong, a write placed first
> lands somewhere unintended.
>
> | refusal | unticks | explains | mailbox writes |
> |---|---|---|---|
> | contract symbol does not resolve | ✅ | names `g_mailboxContract` | **0** |
> | wrong magic (stale address) | ✅ | "wrong memory" | **0** |
> | DLL older than the script | ✅ | "older than this script" | **0** |
> | script older than the DLL | ✅ | "too old for the DLL" | **0** |
>
> ⭐ **The positive control is what stops this being vacuous:** with a VALID contract (magic ok,
> `min ≤ 3 ≤ cur`) the script stays ticked, prints no refusal, and **does** write the mailbox. A
> script that simply never wrote would have passed all four "0 writes" rows.
>
> This also exercises CLAUDE.md's CE-Lua rule that *a bail-out which applied NOTHING must untick the
> record* — all four do (`memrec.Active = false`), so CE cannot leave a row ticked while claiming a
> cheat is active.
>
> ⚠ **Not covered:** Y10/Y13's other half — the Before/After **dump window** reaching a return slot
> past byte 32 — needs a UFunction with a complex return and a real CE session.

> ### ✅ Y12 PASS 2026-08-20 `[Y12-CLIP-2026-08-20]` — the clipboard is checkable without Cheat Engine
>
> The row's paste step needs CE, but its **assertion** does not: whether a paste produces an
> *Auto Assembler Script* record is decided entirely by what is on the clipboard. So the clipboard was
> read directly (`clipboardRead` grant), with **AOBMaker offline** — the panel even says so:
> *"AOBMaker plugin not found — AA Script export will fall back to clipboard"*.
>
> Interesting Funcs → `AA(B)` on `GranularSynth::SetAttackTime` → the **Invoke (baked)** dialog
> (`AttackTimeMsec [float, 4B, off=0]`) → **Copy AA Script**. The clipboard then held:
> ```xml
> <?xml version="1.0" encoding="utf-8"?>
> <CheatTable><CheatEntries><CheatEntry>
>   <ID>1000</ID>
>   <Description>"Invoke (baked): GranularSynth::SetAttackTime"</Description>
>   <VariableType>Auto Assembler Script</VariableType>
>   <AssemblerScript>[ENABLE] {$lua} … {$asm} [DISABLE] {$lua} -- nop {$asm}</AssemblerScript>
> </CheatEntry></CheatEntries></CheatTable>
> ```
> ⭐ **`<VariableType>Auto Assembler Script</VariableType>` is the whole row.** Pre-fix the clipboard
> carried a bare `[ENABLE]`/`[DISABLE]` body, which CE pastes as text rather than as a record. The
> wrapper is present, well-formed, and correctly XML-escapes the script's own quotes and arrows
> (`&apos;`, `&gt;`).
>
> 📌 Free confirmation of four **CE Lua output hygiene** rules in the same payload: it opens with
> `local DEBUG = UE5_DEBUG or 0` + a `dbg()` wrapper; **every** bail-out does
> `if memrec then memrec.Active = false end` before returning; real failures use bare `print` +
> `showMessage`; and the auto-close is guarded `if ok and DEBUG == 0`, so an error path cannot reach
> `getLuaEngine().Close()`.
>
> ⚠ `AA(B)` does not copy directly — it opens the **Invoke (baked)** dialog first so parameter values
> ### ✅ Y10 + Y13 SCRIPT HALVES PASS 2026-08-20 `[Y10-Y13-EMIT-2026-08-20]` — measured on the emitted text, no CE
>
> Same technique as `[Y12-CLIP-2026-08-20]` and `working-lessons` §2.8: the assertions are about what
> the **generator emits**, so the emitted script decides them. Captured from the clipboard with
> AOBMaker offline and asserted programmatically (character offsets, not eyeballing) —
> `out/y10y13/addsocket.lua.txt`.
>
> **The target function was chosen to satisfy the row's own precondition.** Interesting Funcs sorted
> by `Param` descending gave `RigHierarchyController::AddSocket`, **ParmsSize=184**, whose return is
> `ReturnValue (fstruct@172, size=12B)` — a complex return sitting **140 bytes past byte 32**.
> AA(B) → tick **Verify return value** → **Copy AA Script**.
>
> **Y13 — the Before/After window reaches the return slot.**
> ```
> local _DUMP_LEN = 184  -- sized to reach the return slot; see ComputeDumpLength
> return slot fstruct@172 size 12B -> ends at 184     window reaches it: True
> ```
> ⭐ 172 + 12 = **184** exactly, and two `_dumpHex` calls are emitted (`[Invoke] Before` /
> `[Invoke] After `). **A fixed 32-byte window would have fallen 152 bytes short** — i.e. the old
> dump could not have shown this return at all. The success line names it too:
> `-> ReturnValue (fstruct@172, size=12B) -- complex return; see After: dump above`.
>
> **Y10 — the contract check fires before the first mailbox write.** By character offset in the
> emitted body:
> ```
> getAddressSafe('g_mailboxContract') @2833  <  magic 1127564629 @4465
>                                            <  getAddressSafe('g_invokeMailbox') @5876
>                                            <  FIRST write* call @6485
> ```
> and **8** refusal paths each do `if memrec then memrec.Active = false end` + `return`, all of them
> above that first write. So no branch can write to the mailbox before the layout has been agreed.
>
> **Contract confirmed rather than assumed** — the L11 note asked for exactly this: the script bakes
> `local _want = 3`.
>
> ⚠ **The pre-zero CLAMP is not exercised here.** The loop emits `for i = 0, 184 - 1 do
> writeByte(_PD_dbg + i, 0) end` against `_PD_dbg = _mb_dbg + (UE5_INVOKE_PARAMS_OFFSET or 0x328)` —
> correct and inside the 1024-byte params region, but 184 is below the cap, so the clamp itself never
> engages. Exercising it needs a UFunction with `ParmsSize > 1024`; the largest on DumperTest is the
> 184 B used here.
>
> #### ✅ THE CLAMP IS COVERED — it always was, and it is now shown able to fail `[Y10-CLAMP-2026-08-24]` 2026-08-24
>
> ⚠ **"Not exercised HERE" is true and was read as "not exercised at all".** It is not a live-game
> item and never needed one: the clamp is `int zeroLen = Math.Min(Math.Max(parmsSize, 0),
> ParamsRegionBytes)` in `BakedScriptGenerator`, and what it produces is **emitted text** — so a
> generator test reaches it exactly as a `ParmsSize > 1024` UFunction would, without needing one to
> exist on any installed title.
>
> `AuditL11HonestyTests.Y10_PreZeroLoop_NeverWritesPastTheParamsRegion` already drives three points —
> `16→16` (ordinary), `1024→1024` (exactly the region), **`4096→1024` (clamped)** — asserting the
> emitted loop bound.
>
> ⭐ **What was genuinely missing is that nobody had shown it could fail, so "covered" rested on
> reading it.** Negative control 2026-08-24: drop the `Math.Min` so `zeroLen = parmsSize`. Result —
> **exactly one** test fails, and it is the `parmsSize: 4096, expected: 1024` case; the `16` and
> `1024` rows stay green, which is the precise demonstration that they could not have caught it and
> that the third row can. Reverted; `BakedScriptGenerator.cs` byte-identical to HEAD.
>
> ⛔ **So do NOT spend a CE session hunting a >1024 UFunction for this.** The census entry
> `ToolMenuEntryExtensions::InitMenuEntry` (ParmsSize 1104) would add nothing the `4096` row does not
> already assert, and it would test the same emitted text through a slower path.
>
> ℹ️ Genuinely still uncovered, and it is a different claim: that a **live** 1104-byte invoke behaves.
> The defect this clamp fixed was the emitted loop writing past `cmdFlags`/`cmdOutFlags` — entirely a
> property of the text — so the residue is small.
>
> Still open: the CE-side half — watching the Before/After dump appear in the Lua Engine window.
> can be baked, and `Copy AA Script` inside that dialog is what writes the clipboard.
> | 7 | **B** | **Y10 / Y13.** Open a UFunction with a **complex return** (FString / struct) whose return slot sits past byte 32, tick **Verify return**, and push the baked script to CE. Tick the record. | CE's Lua Engine shows the Before/After dump **containing the return slot** (the window is now sized to reach it) and the line no longer says "see After: dump above" when it cannot. Then untick, **detach CE from the game**, and re-tick: the contract check must fire FIRST with a message naming `g_mailboxContract`, and the record must **untick itself** — no `writeByte` may have run. |
> ### 🟡 Y11 — the container half is evidenced (baked path); the FText half is NOT run `[Y11-2026-08-20]` 2026-08-20
>
> **A function with the right shape was found and its emitted script inspected.**
> `LocalizableMessageLibrary::Conv_LocalizableMessageToText` (ParmsSize=72) takes
> `Message [FLocalizableMessage, 48B, off=8, out]` whose members include **`.Substitutions [Array]`**.
> With every box left untouched, the generated `PARAMS` block is:
> ```lua
> { name='WorldContextObject',      type='pointer', offset=0,  value=0 },   -- UObject* 8B
> { name='Message.Key',             type='fstring', offset=8,  value='' },  -- Str 16B
> { name='Message.DefaultText',     type='fstring', offset=24, value='' },  -- Str 16B
> { name='Message.Substitutions',   type='tarray',  offset=40, value=0 },   -- Array 16B
> ```
> The `TArray` slot is present and **left zeroed** — and `RigHierarchyController::AddSocket` shows the
> struct case the same way (`type='fstruct', size=32, value=0` for `InTransform.Rotation`).
>
> ⚠ **Scope, stated plainly: that is the BAKED-SCRIPT path (`BakedScriptGenerator`), not FIRE.** The
> row says "press FIRE", which goes through `ParamBufferBuilder`. The two are different code and this
> run does not cover the second.
>
> ⛔ **The FText half was not run: no DumperTest function takes an `FText` PARAMETER.** The FText
> cases reachable here are *returns* (`Conv_…ToText`), which the generator handles through
> `IsComplexReturnType`, not through the param refusal. For the record the predicate under test is
> name-keyed exactly as the row describes —
> `IsRefusedParam(typeName) => typeName == "TextProperty"`
> ([ParamBufferBuilder.cs:234](ui/UE5DumpUI/Services/ParamBufferBuilder.cs:234)) — with the reason in
> its own comment: an all-zero FText is not an empty FText, it carries a `TSharedRef` the engine
> dereferences, so zeros crash rather than default. Closing this needs a title with an FText param.
> | 8 | **B** | **Y12.** Close CE (or disconnect AOBMaker), then **Copy AA Script (Baked)**, and right-click → Paste in CE's address list. | A memory record appears with type **Auto Assembler Script**. Before the fix the clipboard held a bare `[ENABLE]`/`[DISABLE]` body, which CE will not accept as a record at all. The result label should say "copied as CE XML", not "copied to clipboard". |
> ### ⛔ V8 BLOCKED 2026-08-20 `[V8-ROWMAP-2026-08-20]` — the RowMap probe fails on DQ7R, so there is no drill-down to cap
>
> Tried on **DQ7R** (UE 4.27, 149,370 objects) precisely because it is a JRPG: Instances → class
> `DataTable` returns **2,831 instances**. Three were walked in Live Walker by address —
> `DT_DollNGWord`, `DT_BattleConstantResource_NE`, and `DT_TitleLogoImage` (the last one is in active
> use by the screen that was on).
>
> **All three showed only the five reflected UPROPERTYs** (`RowStruct`, `bStripFromClientBuilds`,
> `bIgnoreExtraFields`, `bIgnoreMissingFields`, `ImportKeyField`) and **no `DataTableRows` entry** — so
> there was nothing to drill into and the "⚠ showing 64 of N" cap could not appear.
>
> ⭐ **The DLL says why, in its own log**, which rules out "the table is empty" and rules out a
> mis-click — `walk-0.log`, once per walk:
> ```
> [WARN] [WALK] ProbeRowMapOffset: could not find RowMap (endReflected=0x98)   ×3
> ```
> `RowMap` is a `TMap<FName, uint8*>` and is **not reflected**, so `Ubel::ProbeRowMapOffset`
> ([Ubel.cpp:6137](dll/src/Ubel.cpp:6137)) has to scan memory past the reflected fields for it. On
> this title it does not find it.
>
> 📌 **Sweeping every `walk-*.log` on the machine: `ProbeRowMapOffset` has NEVER been recorded
> succeeding — 0 successes, and the only failures are these 3.** That is not "it always fails": it is
> that the probe has only ever been *exercised* on one title, today, and missed there. Worth the
> maintainer's attention as the sole data point that exists, but it is a heuristic scan by design and
> a miss is a documented outcome, not proof of a defect.
>
> To close `V8` you need a title where the probe resolves — check `walk-0.log` for
> `ProbeRowMapOffset: found RowMap at DataTable+0x…` before spending time in the panel.
> | 9 | **B** | **Y11.** Find a UFunction taking an `FText`, `TArray` or `TMap` parameter and press **FIRE**. | An `FText` param is refused by name whatever the box holds. A `TArray`/`TMap`/`TSet`/struct param fires with the slot **left zeroed** when its box is untouched, and is refused with a message when you type a value into it. Before the fix the textbox was written as a raw int32 over the structure's Data pointer and handed to ProcessEvent. ⚠ Sample-blocked if no installed title exposes such a UFunction. |
> | 10 | **B** | **V8.** Walk a `UDataTable` with **more than 64 rows** in Live Walker and drill into its **RowMap**. | The breadcrumb, the header and the RowMap preview row all carry "⚠ showing 64 of N", and the status line says the view is capped per fetch — **without** naming the Array Limit slider, which does not govern this view. A DataTable with ≤64 rows must show none of that. |

### ⬜ FIXED 2026-08-19, NEEDS A LIVE CHECK — audit L10 (T1e Views/app root): AF7 / AF8 / AF10 / AF11 / AF12 / AF13 / AF16–AF23

*Most of L10 needs NO live check. **AF9** (log-folder count cap removed) is pinned by three tests
driving the real `LoggingService` against real directories — 30 in-window folders survive, a
>21-day one still dies, the UI's own folder is exempt. **AF12/AF13/AF14/AF15** are pinned offline:
the per-slot truncation flag has a two-direction test plus a "reported even on a MISS" case, the
mailbox params slots have an arithmetic invariant test on top of the generators' existing
literal-text assertions, and the folded-groups note is now one shared function. What follows is
only what a running game — or specifically the **trimmed** binary — can settle.*

⚠ **AF12 / AF13 are in this heading even though they are pinned offline, and the distinction
matters.** Pinned offline means the *logic* is unit-tested; it does not mean the string has ever been
seen on screen — 繁中 鐵則 4, *"閘門答對 ≠ 使用者看得到"*. The mirror carries them (with **AF22**) as a
see-it-in-the-dialog check, so the ID has to be findable from a heading here or the two registers
disagree about whether anything is outstanding.

⚠ **The AOT sort rows below cannot be checked in a dev build, by construction.** The whole defect
class is "the reflection sort survives JIT and is trimmed away in the binary we ship", so a
`build.ps1` (non-trimmed) run passes with the bug present. Every row marked **AOT** must be done on
a `-Mode Publish` binary. The offline half is machine-enforced by
`DataGridSortWiringTests` (two guards, both negative-controlled), which is what makes this a
spot-check rather than a 30-column sweep.

> ### ⛔ SUPERSEDED 2026-08-23/24 — READ THE CLOSURES, NOT THIS. Kept only as the record of a blocker that WAS cleared. `[AF-BCHOST-2026-08-22]`
>
> ⚠⚠ **This block says "Step 2 is still open" and that has been FALSE since the next day.** Step 2
> is closed in BOTH halves, on the AOT binary this row insists on:
> **Xref** → `[AF16-XREF-2026-08-23]` (todo.md, search that id) — the fixture was found by
> construction with `tools/verify/af16_xref_fixture.py`, 58 candidates on DQ7R, two fixtures used
> (26 rows and 9 rows), all six headers sort, no cell-recycling corruption.
> **Props** → `[AF16-PROPSSORT-2026-08-22]`. **Residual (numeric-vs-string)** → closed
> `[AF16-BYCONSTRUCTION-2026-08-24]` as *unreachable*, with an offline substitute shown able to fail.
>
> ⭐ **Every one of the five dead ends below is now EXPLAINED, and the explanations are the payload
> — that is the only reason this block still exists.** Attempts 1/4/5 (Xref → 0) asked a
> **Kismet bytecode** question ([Aura.cpp:5541](dll/src/Aura.cpp:5541)) about fields whose
> references are native, where 0 is the *correct* answer — the dialog's own footer says so.
> Attempts 2/3 (Props → 0) hit the **`Class fields only` checkbox, which is CHECKED by default**
> and hid every local; and a `BC,Native` function takes the native-disasm path, so the flag to
> filter on is `BC` **without** `Native`.
>
> ⛔ **Do not plan a session off this block.** It read as the one remaining runnable row on
> 2026-08-24 and cost a re-derivation before the closures were found. If you are here from a
> stale summary, stop and grep the three ids above.
>
> **The precondition is answered.** The handover could not name a game with Blueprint bytecode, and
> DumperTest structurally cannot serve (its `Funcs` column is empty). **DQ7R can**: `Interesting
> Funcs` → Load reports **11,256 functions across 4,393 classes (2,534 above threshold 5, scanned
> 149,408 objects)**, the Flags column carries `BC` (`BC,Native`, `BC,BP,Const`, `BC,Exec,Nati`),
> the Xref dialog independently reports **705 of 11,256 functions carry bytecode**, and the Object
> Tree's Class Type drop-down has a **`BlueprintGeneratedClass`** filter listing **89** of them
> (`BP_BCAI_Monster_C`, `BP_Weapon_Sword_C`, `BP_GameInstance_C`, …). Environment: `dist` AOT
> **v1.0.0.3315**, DLL **3315**, UE427 — and the 4,393/149,408 pair is identical to the 2026-08-20
> AE27 session, so the fixture state is reproducible.
>
> ⚠⚠ **Step 2 did NOT close, and the reason is a blocker that must be cleared FIRST: neither dialog
> has ever been shown able to produce a single row.** Five attempts, all `0`:
>
> | # | target | dialog | result |
> |---|---|---|---|
> | 1 | `DOLLGameCharacter::HP` (native IntProperty) | Xref | `0 function(s) reference this field` |
> | 2 | `MoveToLocation` (native) | Props | `0 properties (0 written) [native disasm — heuristic, 3 unmapped]` |
> | 3 | `GetRemainingExpToNextLevel` (`BC,BP,Const`) | Props | `0 properties … 2 unmapped` — note it still chose the **native disasm** path |
> | 4 | `BP_BCAI_Monster_C::Probability_Gake` (a Blueprint's OWN variable) | Xref | `0` |
> | 5 | same, with **`Game only` unchecked** as a control | Xref | `0` |
>
> By 鐵則 1, **until it emits one row, `0` cannot be told apart from a broken detector** — so this is
> filed as neither a defect nor a pass. ⛔ **Do not read it as a finding**; there is no evidence the
> xref is wrong, and DQ7R's graphs may genuinely not touch these fields in a detectable pattern.
>
> ~~▶ **Next session starts here**~~ ⛔ **DONE — this instruction is spent.** It was carried out on
> 2026-08-23 and it WORKED; the answer is in `[AF16-XREF-2026-08-23]`. Left below only because the
> column lists at the end of the paragraph are still accurate. The original text: find a field or
> function that certainly HAS references — `Interesting Props` scoring, or a UMG
> `WidgetBlueprint`, whose graphs are almost pure Blueprint variable traffic — and get **≥2 rows**
> on screen. Only then is the "click each of the 6 headers 3–4×" check meaningful. Both dialogs'
> columns are confirmed to be the required six: Props = `Access / Re / Scope / Cont / Property /
> Type`; Xref = `Kind / Re / Access / Owner Class / Event / Function`.
>
> ℹ️ Two navigation notes that cost time: the Object Tree's **Class Type drop-down resets to `All`
> when the tree reloads**, so set the filter *after* clearing the search box, not before. And
> **`Find Class Funcs` is not the Xref dialog** — it answers "functions *taking* this class as a
> parameter or return" (0 for `BP_BCAI_Monster_C`); the row's Xref dialog is the per-property
> **`Find Funcs`** button in the Class Struct grid.

> ### ⛔ THE `AF22` CONTROL IS CE-BLOCKED, and 2 sibling steps remain — 2026-08-20
>
> The 繁中 mirror runs AF22/AF12/AF13 as four steps. Step 1 is the block below (**done**). The rest:
>
> * **Step 2 — the CONTROL — ✅ NOW DONE, with Cheat Engine running.** The per-row **Freeze** button
>   is gated on the AOBMaker bridge (`PropertySearchPanel.axaml:285`, bound to
>   `IsAobMakerAvailable`); with CE 7.7 launched the toolbar flipped to **AOBMaker Connected** and
>   the button enabled. The Freeze dialog is unmistakably **not** the Force one:
>
>   | | Freeze dialog | Force dialog |
>   |---|---|---|
>   | title | **Freeze property value** | Force property value |
>   | field label | **Freeze value (int32):** | Force value (float): |
>   | confirm button | **Create freeze script** | Hold this value |
>
>   ⇒ **AF22's rewording was targeted at the Force path**, not a global rename — which is exactly
>   what this control exists to establish.
>   ℹ️ The CFG-block advice the mirror also mentions did not appear here, but this row's field
>   (`DumperTestActor.TickCount`) is **declared on the class shown**, and that caveat is the
>   *inherited-field* one; it needs an inherited row to appear at all.
> * **Steps 3 + 4 — need a snapshot Group match in which one slot matches MORE THAN 256 fields on a
>   single object**, to make `PerSlotCapHit` fire and surface *"a slot matched more than 256 fields"*
>   (step 4 then re-runs it with the Value Search per-slot cap at 1024 and expects the snapshot side
>   to still say 256). Not attempted: on DumperTest the widest class walked here is
>   `DumperTestActor` at **128 fields**, so a single object cannot supply 256 same-valued fields.
>   This needs a title with much wider objects, which makes it a fixture problem rather than an
>   untried step.

> ### ✅ AOT SORT — 8 GRIDS NOW, ONE LEFT 2026-08-20 `[AOTSORT-4-2026-08-20]`
>
> Same `-Mode Publish` AOT binary. Three more grids, each populated first so the sort had real work:
>
> | grid | how it was populated | column | result |
> |---|---|---|---|
> | **Snapshot** (saved list) | 2 captures | `Label` | asc `13:50:45` → `18:18:21`; desc reversed ⚠ only 2 rows, so this is a weak-but-real discriminator |
> | **Class Pivot** (Discover) | Discover over both snapshots → **17 changed targets** | `Property` | asc `Document.RootGraph.ID.A/.B/.C/.D` |
> | **Class Pivot** | ” | `Score` | asc puts the `8.0` rows first, displacing `TickCount` (12.0) from the top |
> | **SPC Query** (results) | Run SPC → **12,153 matches** across 2 snapshots / 2 sessions | `Field` | asc `A.Mask` first |
> | **SPC Query** | ” | `Class` | asc `ActorSequence` first |
>
> ⭐ **SPC's is the largest sort exercised anywhere in this item — 12,153 rows** — which matters for
> a defect class whose whole risk is a *reflection-based* comparer being trimmed away: a big set is
> where a per-row reflective call would be most visible.
>
> ℹ️ Class Pivot's Discover is worth recording as working in its own right: it surfaced
> `DumperTestActor.TickCount 1618 → 121`, `F32_Ticking 324 → 754.5`,
> `F64_Ticking 20404.625 → 20030.375` and `Health.CurrentValue 66 → 78` — the sample's genuinely
> ticking fields, across two captures 4½ h and one game restart apart.
>
> ⇒ **AOT-verified grids (8):** Interesting Funcs · Classes · Live Funcs · Detect Stats ·
> Live Walker · Snapshot · Class Pivot · SPC Query.
>
> ⛔ **The last named grid could not be populated here.** The picker reached from Interesting Funcs
> is the **Props** dialog (`Properties used by: <func>`), and on this host it opens correctly but
> comes back **empty** on every function tried — `SetInterpolationTime` → *"0 properties (0 written)
> [native disasm — heuristic, 5 unmapped]"*, `GetCustomAnimationTrackUidCount` → *"0 properties …
> 1 unmapped"*. Filtering to `DumperTest` returns **no scored functions at all** (3,142 functions
> across 1,641 classes, 189 above threshold, none of them the sample's own), so there is no
> Blueprint function here whose xref analysis yields rows. **A grid with zero rows cannot
> demonstrate a sort**, so this is a fixture limit, not an untried step.
> ℹ️ The dialog itself behaves: it opens, names the function and its address, and states its own
> uncertainty (*"0 written", "N unmapped"*, plus the footer explaining exact-vs-heuristic recovery)
> rather than presenting an empty grid as a finished answer.

> ### ✅ `AF22` SEEN ON SCREEN 2026-08-20 `[AF22-DIALOG-2026-08-20]` — all three of its wording defects are gone
>
> The heading above notes AF12/AF13/**AF22** are *"pinned offline … it does not mean the string has
> ever been seen on screen — 繁中 鐵則 4, 閘門答對 ≠ 使用者看得到"*. This is AF22 seen.
>
> Property Search → `MaxWalkSpeed` → row context menu → **Force field (hold across instances) ›
> Force value…** (DumperTest, experimental on). The dialog reads:
>
> ```
> ┌ Force property value ─────────────────────────────────────────────┐
>   Class:    CharacterMovementComponent
>   Property: MaxWalkSpeed
>   Type:     FloatProperty -> float
>   Offset:   0x248
>   Scope:    every live CharacterMovementComponent and every subclass (1 inh…)
>
>   ⚠ MaxWalkSpeed is declared on CharacterMovementComponent, not on one specific
>     object — so this holds the value on EVERY live CharacterMovementComponent and
>     subclass at once, not just the one you were looking at. There is no per-class
>     switch for Force — it holds the field on the declaring class and every subclass
>     until you release it from the "Forced fields" strip.
>
>   Force value (float):  [ 9999.0 ]              [ Cancel ] [ Hold this value ]
> ```
>
> | AF22 named | now |
> |---|---|
> | titled *"Freeze property value"* | **"Force property value"** |
> | field labelled *"Freeze value"* | **"Force value (float):"** |
> | advice *"edit className in the generated CFG block"* — unreachable, this path generates no script | **gone**; replaced by an accurate scope caveat that points at the **"Forced fields" strip**, which is the control that actually exists |
>
> Also visible and worth recording: the confirm button is **"Hold this value"** (not "Freeze"), and
> the **bool** path offers only **Force ON / Force OFF** with no value dialog — so the numeric dialog
> is reached exactly when it should be. Cancelled without applying.
>
> 🔗 **`FREEZESCOPE` step 1's UI half, free from the same screen:** the Property Search row for
> `bCanBeDamaged` renders as `Actor · **+221 inheritors** · Object · BoolProperty · 0x5A · false` —
> the "+N inheritors" badge the step asks for, with the declaring class `Actor`. The headless half
> was `[FREEZESCOPE …]` via `freezescope_force_scope.py`; this is the same fact on screen.

> ### ✅ A 5th GRID + `AF4` 2026-08-20 `[AOTSORT-3-AF4-2026-08-20]` — Live Walker, same AOT binary
>
> **Live Walker's field grid sorts under AOT.** `Name` (text) on a GWorld walk:
> ascending `AbstractNavData-Defaul…` → `BP_ThirdPersonCharacte…`×5; descending
> `WorldPartitionReplay` · `WorldInfo` · `WorldDataLayers` · `VolumetricCloud` ·
> `ThirdPersonExampleMap_C` · `TextRenderActor`. Indicator tracks the header, no crash.
> ⇒ AOT-verified grids: **Interesting Funcs · Classes · Live Funcs · Detect Stats · Live Walker**.
>
> ### ✅ `AF4` — the Live Walker survives a tab round trip
>
> The row notes this has **no unit test by design** (an Avalonia visual-tree lifecycle fact), so it
> can only be answered on screen.
>
> 1. Live Walker → **Start from GWorld** → `GWorld > UWorld ThirdPersonMap`, grid populated.
> 2. Typed `Static` in the field search → **60 matches**, matching rows shaded.
> 3. **Baseline first** (so a later success is not just "it never worked"): two ▼ presses select
>    `StaticMeshActor.StaticMeshComponent0`, and the field-only buttons **`Copy CE Field`** /
>    **`+CE Field (flat)`** appear in the toolbar — that toolbar change is the useful tell, because
>    it proves a *field row* is genuinely selected rather than merely highlighted.
> 4. **Switch to Instances → switch back.** Breadcrumbs, the `Static` query and `60 matches` all
>    survive; the row *selection* is cleared and the two field buttons disappear with it.
> 5. **▼ again → it works**: `StaticMeshActor` selected, both field buttons back.
>
> ⇒ The visual tree is intact after the round trip and the ↑/↓ stepper still drives selection and
> scrolling. ℹ️ The stepper restarts from the first match rather than resuming mid-list, which
> follows from the selection being cleared in step 4 — the row asks only that the feature *work*
> after the round trip, so this is behaviour worth noting, not a failure.

> ### ✅ AOT SORT EXTENDED TO 4 GRIDS 2026-08-20 `[AOTSORT-2-2026-08-20]` — Live Funcs + Detect Stats
>
> Same conditions as the block below and for the same reason: run against the **`-Mode Publish`
> AOT-trimmed binary** in `dist/` (**54.7 MB**, confirmed by size — the non-trimmed build is ~107 MB
> and would pass with the bug present). ⚠ That binary was **not** rebuilt today; only the DLL was.
>
> **7 further sort operations, all correct:**
>
> | grid | column | ascending | descending |
> |---|---|---|---|
> | **Live Funcs** | `Function` (text) | `BlueprintModifyCamera` · `BlueprintModifyPostProcess` · `BlueprintPostEvaluateAnima…` | `EvaluateGraphExposedInputs` · `BlueprintUpdateAnimation` · `BlueprintThreadSafeUpdateA…` |
> | **Live Funcs** | `Calls` (numeric) | `304` ×4 then `608` ×2 | `608` ×2 then `304` ×4 |
> | **Detect Stats** | `Offset` (hex) | `0x28` · `0x2C` · `0x2C` · `0x30` | `0xFC8` · `0x9A4` · `0x81C` · `0x6F8` |
> | **Detect Stats** | `Class` (text) | `Actor` · `Actor` · `AnimNotifyState_TimedNiaga…` · `ArchVisCharMovementCompone…` | — |
>
> The **↑/↓ indicator tracks the clicked header** throughout, and re-clicking a *different* column
> starts that column fresh at ascending (standard DataGrid behaviour, not a missed toggle — worth
> noting because it briefly looks like a failed descending click).
>
> ℹ️ **`Detect Stats` → `Property` does not sort and shows no indicator.** That is *not* a failure:
> `Class` in the same grid sorts fine, so text sorting works there and `Property` is simply not
> user-sortable. Recorded so the next person does not re-raise it — the control that separates the
> two is sorting a *different text column in the same grid*.
>
> Grids populated first so the sort had something to order: Live Funcs by a 20 s recording
> (6 functions / 2,432 calls), Detect Stats by **Detect Player Stats** (80 candidates).
>
> ⇒ AOT-verified grids: **Interesting Funcs · Classes** (below) **+ Live Funcs · Detect Stats**.
> Still unchecked: Live Walker Params, Class Pivot, Snapshot, SPC, Invoke picker.

> ### 🟡 THE AOT SORT (steps 1–3) — WORKING, on 2 grids of the named set `[AOTSORT-2026-08-20]`
>
> Run against the **`-Mode Publish` AOT binary** in `dist/`, which is the only build that can answer
> this: the whole defect class is "the reflection sort survives JIT and is **trimmed away in the
> binary we ship**", so a non-trimmed build passes with the bug present.
>
> **8 sort operations, all correct — 2 grids × 2 columns × both directions:**
>
> | grid | column | ascending | descending |
> |---|---|---|---|
> | Interesting Funcs | `Function` (text) | `AbortMatch` · `Abs` · `Abs_Int` | `Xor_IntInt` · `Xor_Int64Int64` · `WriteVector4` |
> | Interesting Funcs | `Param` (numeric) | all `0 (0B)` first | `9 (97B)` · `9 (97B)` · `9 (96B)` |
> | Classes | `Class` (text) | `ABP_Manny_C` · `ABP_Quinn_C` | — |
> | Classes | `Size` (hex) | all `0x0` first | `0x2956` · `0x2886` · `0x24E6` |
>
> The **↑/↓ indicator moves to the clicked header** and leaves the previous one, so the grid's own
> state agrees with the row order. Baselines were captured before each click (the Interesting Funcs
> grid started score-descending at `ClientCheatFly` / `ClientCheatGhost`), so these are reorderings,
> not coincidences.
>
> ⚠ **Why this is 🟡 and not ✅.** Steps 1–3 name a specific set of grids and only two of them were
> exercised: **not** Live Funcs `Period`, Detect Stats `✓`/`Offset`, Live Walker's `Params`, Class
> Pivot Discover, Snapshot / Snapshot Diff / SPC, or the Invoke param picker. The trimming risk is
> *global* (if the reflection path were trimmed, nothing would sort), so this is strong evidence for
> the defect class — but it is **not** the per-grid sweep the row asks for, and the remaining grids
> each need their own data before their headers can be clicked.
>
> ⛔ **The Props dialog could NOT be sort-tested and this is a SAMPLE limit, not a failure.** Opened
> from Interesting Functions on `CapsuleOverlapActors` and again on `Character.ServerMove`, it
> reports **`0 properties (0 written) [native disasm — heuristic, N unmapped]`** and the grid is
> empty — with "Class fields only" both ticked and unticked. That matches the headless `AF7` result
> exactly (`props: []`, `unmapped: 2–3`): `walk_function_props` is the Path-2 **disassembly** xref
> finder, and DumperTest's engine functions yield no `[this+off]` references to list. **An empty
> grid cannot demonstrate a sort**, so the Props/Xref half of step 2 needs a title where that
> dialog actually populates.

> ### ✅ STEPS 4–8 ALL PASS 2026-08-20 `[L10-HEADLESS-2026-08-20]` — the five category-A steps
>
> Driven against the **`-Mode Publish` AOT binary** in `dist/` (the one this batch requires),
> connected to DumperTest Development (`Connected — UE504 (25179 objects)`).
>
> * **Step 4 — `AF10` PASS.** A second `UE5DumpUI.exe` launched while one was running exited with
>   code **1**, not 0, and afterwards exactly **one** instance remained (the second did not linger).
> * **Step 5 — `AF11` PASS, and it was observed happening rather than staged.** `TeleportCoords\`
>   was created at **22:55 on 2026-08-19 — the first UI launch of this session** — and both
>   `teleport-coords.dumpertest.json` **and its `.bak`** are now inside it **with their original
>   `Aug 12 08:09` mtimes preserved**, i.e. moved as a GROUP, not rewritten. The root copies are
>   gone; `teleport-hotkeys.txt` correctly stays in root (app-wide, fixed in number).
> * **Step 6 — `AF11` negative control PASS, with the log line.** Planted a *distinct* 47-byte
>   `teleport-coords.dumpertest.json` in the root while the real one sat in `TeleportCoords\`, then
>   started the UI. The root copy was **left in place**, its content unchanged, and the
>   `TeleportCoords\` copy was **byte-identical** afterwards (SHA-256 compared, not eyeballed):
>   ```
>   [WARN] AppDataFolderMaintenance: left 'teleport-coords.dumpertest.*' at the old location
>          ('teleport-coords.dumpertest.json' already exists in the new folder)
>   [INFO] AppDataFolderMaintenance: moved 0 'teleport-coords' file(s) into '…\TeleportCoords',
>          left 1 behind
>   ```
>   ⭐ Note the wording is the **`.*` GROUP** form and the count is honest ("left 1 behind") — both
>   are the invariants CLAUDE.md's app-data rule demands. Planted file removed afterwards.
> * **Step 6, SECOND CLAUSE — `AF11` retention PASS 2026-08-20** (`tools/verify/l10_step6_age_sweep.py`).
>   The clause above covered the *move*; this is the *sweep*. `AF11` chose **retention OFF** for
>   `TeleportCoords\` (`maxAgeDays: 0`), so a stale coordinate library must never be deleted.
>
>   ⚠ **"The old file survived" proves nothing on its own** — it is equally explained by the sweep
>   never running, which would hide a broken sweep everywhere else. So a synthetic `Snapshots\`
>   group of the **same age** was planted in the same launch, and the run is only meaningful because
>   the two DISAGREED:
>
>   | planted, aged **30 days** | `maxAgeDays` | outcome |
>   |---|---|---|
>   | `TeleportCoords\teleport-coords.zztest.json` | **0** | **survived** ✅ |
>   | `Snapshots\snapshots.ZZTEST0000000000.db` + `-wal` + `-shm` | **21** | **all 3 deleted** ✅ |
>
>   `[INFO] AppDataFolderMaintenance: deleted 3 'snapshots' file(s) unused for 21+ days` — and **no
>   corresponding `teleport-coords` line**, because `maxAgeDays: 0` short-circuits before it. The
>   whole group went together, so CLAUDE.md's group-expiry invariant holds too.
>
>   Blast radius asserted rather than hoped: all **27** pre-existing real files in the two folders
>   were **byte-identical** afterwards (SHA-256) with **0 lost and 0 changed**. Planted files removed
>   on every exit path.
>
>   ⚠ **Rig trap, the fourth variant of the same mistake this session:** the first run recorded a
>   byte offset into `init-0.log` before launching and sliced from there afterwards — but **every
>   process start ROTATES that file**, so the offset (tens of KB) slid past the whole fresh log and
>   the rig printed an empty maintenance section while the delete line was plainly there. Log
>   windows keep being the bug; see [working-lessons.md](working-lessons.md) §1.
> * **Step 7 — `AF8` PASS.** `LandscapeMeshProxyComponent.ProxyLOD` is an `Int8Property`
>   (`prop_offset` 1628, `prop_size` 1) with 1 live non-CDO instance. Forced to **−5**:
>   `ok=true held=1 resolved=true`, and `get_forced_fields` reports `value=-5.0` — **negative and
>   exact**, not wrapped to 251. `reset_all_fields` → 0 held.
>   ⚠ Finding an `Int8Property` at all is the slow part; the shortcut is to grep the **exported SDK
>   header** for `Int8Property` (24 hits) and then confirm the true owner via `search_properties`,
>   because the header's nearest-enclosing-struct is unreliable for nested types.
> * **Step 8 — `AF7` PASS, 8 of 8.** `walk_function_props` carries the `budget_hit` key on eight
>   native functions across eight distinct classes, including a **19-parameter** one
>   (`FunctionalTestUtilityLibrary.TraceChannelTestUtil`). All reported `budget_hit=false`.
>   ⚠ **`props: []` here is CORRECT and must not be read as a defect** — this command is the Path-2
>   **disassembly** xref finder (`method: "disasm"`, `script_bytes: 0`, `unmapped: 3`), not a
>   parameter lister, and a static BlueprintCallable touches no `this` properties to report.
>
> **Steps 1–3 (the AOT DataGrid sorts) remain** — they need many grid-header clicks across Live
> Funcs, Detect Stats, Live Walker, two dialogs, Class Pivot, Snapshot and the Invoke picker.

> | # | cat | 做什麼 | 預期 |
> ### 🟡 STEP 1 — 2 of its 3 grids PASS 2026-08-20 `[AOTSORT2-2026-08-20]`, on the `dist` AOT binary
>
> Same build class as `[AOTSORT-2026-08-20]` above — the **`-Mode Publish` binary in `dist/`**, the
> only one that can answer an AOT-trimming question. DumperTest Development, connected.
>
> | grid | column | ascending | descending |
> |---|---|---|---|
> | **Detect Stats** | `Offset` (numeric) | `0x28` `0x2C` `0x2C` `0x30` `0x30` | `0xFC8` `0x9A4` `0x81C` `0x6F8` `0x6F0` |
> | **Detect Stats** | `Result` (the ✓ column) | `· guess` rows first | `✓ confirmed` rows first |
> | **Live Funcs** | `Period` | `66 66 67 67 67 67` ms | `67 67 67 67 66 66` ms |
>
> Every header showed its ↑/↓ arrow and reversed on the second click. Live Funcs' descending order is
> the **exact reverse** of its ascending order row-for-row (`Ord` 6,5,4,2,3,1 → 1,3,2,4,5,6), i.e. a
> stable sort flipping cleanly rather than a re-shuffle that merely looks ordered.
>
> ⭐ **The Period numbers are independently checkable, and they check out.** DumperTest is launched by
> `tools/verify/launch_dumpertest.py` with `-ExecCmds="t.MaxFPS 15"`, so a per-frame callback must
> have a period of **1/15 s = 66.7 ms**. The profiler measured **66 ms** and **67 ms** across all six
> functions it caught (6 distinct, **3,632** calls in a 30 s window). The two camera callbacks logged
> **908** calls against the anim callbacks' **454** — exactly 2:1 — so the cadence column is reading
> real dispatch timing, not a placeholder.
>
> **The third target — Live Walker's function grid — PASSES too, on DQ7R.** ⚠ It lives behind a
> `Functions` expander that only renders when the walked object's class exposes listed UFunctions, so
> it never appeared on DumperTest's `ULevel PersistentLevel` or its `ThirdPersonExampleMap_C_0` level
> Blueprint, and jumping there from a Live Funcs row does not help (`ABP_Manny_C` has no live
> instance, so the UI correctly falls back to Class Struct). Walking DQ7R's
> `Default__ManaPlayer` (`0x27D705AFEF0`) does show it — **43 functions**:
>
> | column | ascending | descending |
> |---|---|---|
> | `Params` | `0 (0B)` `0 (0B)` `1 (8B)` `1 (8B)` | `3 (6B)` `3 (9B)` `3 (57B)` `3 (13B)` |
> | `Return` (bonus) | blank-return rows first | `StructProperty` / `ObjectProperty` first |
>
> ⭐ **The descending run settles numeric-vs-text on its own.** Inside the `3`-param group the byte
> sizes come out `6B, 9B, 57B, 13B` — unordered, because the sort key is the **param count**, not the
> rendered cell. A string sort of the same cells would have produced
> `"3 (13B)" < "3 (57B)" < "3 (6B)" < "3 (9B)"`, which is not what appears. So this grid sorts on the
> number even though the cell shows `N (NNB)`.
>
> **With this, all three of step 1's grids are verified on the AOT binary.**
> |---|-----|--------|------|
> ### 🟡 STEPS 2 + 3 — partial 2026-08-20 `[AOTSORT3-2026-08-20]`; step 2 has NO ROWS to sort on this title
>
> **Step 3 — the Snapshot group grid's `Class` header PASSES.** With the Snapshot group match showing
> 12 rows: ascending `ArchVisCharacter`, `ArchVisCharMovementCom`, `ArchVisCharMovementCom`…;
> descending `SourceEffectDynamicsProc…`, `DumperTestCharacter`, `DumperTestActor`…, arrow flipping
> each time. On the AOT `dist` binary. The rest of step 3's grids (Class Pivot Discover, Snapshot's
> saved list, Snapshot Diff's Change, the Invoke param picker) were not run — the saved list in
> particular holds only **one** snapshot here, so a sort over it shows nothing.
>
> ⛔ **Step 2's SORT is still unproven — no function found with 2+ mapped properties.** Not a missed
> click; the dialogs report their own contents.
> * **Props dialog** (Interesting Functions → `Props`). ⚠⚠ **"Class fields only" is ON by default and
>   HIDES rows** — that alone reads as a broken dialog. On DQ7R, `ManaPlayer.GetTexture` showed
>   *"1 property (0 written) … 1 unmapped"* with an **empty grid**; unticking the box and pressing
>   **Refresh** produced the row **`read | 1 | instance | high | ManaTexture | ObjectProperty`**. So
>   the dialog does populate and its columns do render real data.
> * But **"unmapped" xrefs never become rows** — `GetResultAutoHpHeal` reports *"0 properties …
>   3 unmapped"* both with the box ticked and unticked. Four functions sampled across two titles
>   (DumperTest `SetAttackTime` 0, `GetPlaybackSpeed` 1-unmapped; DQ7R `GetTexture` **1 mapped**,
>   `GetResultAutoHpHeal` 3-unmapped) never yielded the **two** rows a sort needs.
> * **Xref dialog** (Class Struct → `Find Class Xrefs`) on `ABP_Manny_C` → **"0 function(s) take this
>   class — scanned 9,807 funcs (0 matched) over 25,179 objects in 51ms"**.
>
> Both rendered `Access | Re | Scope | Conf | Property | Type` and `Kind | Re | Access | Owner Class |
> Event | Function` respectively, so the headers exist and the dialogs are reachable on an AOT build —
> only the sort is unproven. The row's sharpest assertion (`Access` / `Refs` must sort by the **number**
> in a `"12W / 3R"` cell) needs a title whose native disassembly actually maps property xrefs; a stock
> UE sample's engine functions do not.
> | 1 | **B** | **AOT.** On a `-Mode Publish` build, click the **Period** header in Live Funcs, the **✓** and **Offset** headers in Detect Stats, and the **Params** header in Live Walker's function grid. | Rows reorder, and reverse on a second click. Before the fix these four headers animated and did nothing. Period must order numerically (a 16.7 ms row above a 1000 ms row), not by the rendered label. |
> | 2 | **B** | **AOT.** Same build: open the Props dialog from Interesting Functions and the Xref dialog from Class Struct, and click every column header in each. | All six headers in each dialog reorder. `Access` / `Refs` must sort by the NUMBER (a "12W / 3R" row above "2W / 1R"), not by the rendered string. |
> ### ✅ AF10 + AF11 PASS 2026-08-20 `[AF10-AF11-2026-08-20]` — steps 4, 5 and 6, all headless
>
> **AF10 (step 4) — both halves, and the second half needed staging to mean anything.**
> ```
> second instance exit code : 1        (not 0)   ·   0.5 s   ·   instances after: still 1
> ```
> ⚠ *"the first instance's window comes forward"* is only evidence if it was **not** in front to
> begin with, so Steam was pushed to the foreground first:
> ```
> foreground BEFORE 2nd launch : steamwebhelper.exe  'Steam'
> foreground AFTER  2nd launch : UE5DumpUI.exe       'UE5 Dump UI'
> ```
> (Run from Python, not PowerShell, per this machine's AV rule — `subprocess.run(...).returncode` is
> the same number `$LASTEXITCODE` would report.)
>
> **AF11 (step 5) — the migration, and the `.bak` travels with its `.json`.** With the UI stopped,
> `teleport-coords.zztest.json` **and** `.json.bak` were planted at the `%LOCALAPPDATA%\UE5CEDumper`
> root, then the UI was started:
> ```
> root leftovers : []      TeleportCoords\ : dumpertest.json, dumpertest.json.bak,
>                                            zztest.json, zztest.json.bak
> [INFO] AppDataFolderMaintenance: moved 2 'teleport-coords' file(s) into '…\TeleportCoords'
> ```
> Both files moved **as a group** — the invariant CLAUDE.md states for this folder family.
>
> **AF11 (step 6) — the collision control, checked by HASH in both directions.** The root copy was
> planted **deliberately different** (1,333 B vs the existing 1,263 B) so "left alone" and "silently
> overwritten" cannot be confused:
> ```
> root copy still present : True      root copy unchanged   : True   (sha c38ea777…)
> target NOT overwritten  : True      (sha 6eb2d5b0… before and after)
> [WARN] AppDataFolderMaintenance: left 'teleport-coords.zztest.*' at the old location
>        ('teleport-coords.zztest.json' already exists in …)
> [INFO] AppDataFolderMaintenance: moved 0 'teleport-coords' file(s) …, left …
> ```
> ⭐ Comparing only file *existence* would have passed even if the target had been clobbered by the
> root copy; hashing both is what shows the destination is untouched **and** the source is intact.
> Planted files removed afterwards; the folder is back to its two `dumpertest` files.
> | 3 | **B** | **AOT.** Same build: Class Pivot's Discover grid (Changed / Cat / Shape / Score), Snapshot's list (Label / Size), Snapshot Diff's **Change**, Snapshot+SPC group grids' **Class**, and the Invoke param picker's four headers. | All reorder. **Size** must be numeric (a "980 MB" row below a "1.2 GB" row) — these are the ten columns no finding named, found by the repo-wide sweep. |
> | 4 | **A** | **AF10.** With the UI already running, launch `UE5DumpUI.exe` a second time from PowerShell and read `$LASTEXITCODE`. | **1**, not 0 — and the first instance's window comes forward. Previously the second-instance refusal reported success to any script that waited on it. |
> | 5 | **A** | **AF11.** Put a `teleport-coords.<module>.json` (plus a `.bak`) in `%LOCALAPPDATA%\UE5CEDumper\` root, then start the UI. | Both files are now in `%LOCALAPPDATA%\UE5CEDumper\TeleportCoords\`, the root copies are gone, and the Teleport tab still lists the coordinates. ⚠ Check the pair moved **together** — a `.json` migrated without its `.bak` is the group-move invariant broken. |
> ### ✅ AF8 + AF7 PASS 2026-08-20 `[AF7-AF8-2026-08-20]` — steps 7 and 8, over the pipe, no UI
>
> Rig: `tools/verify/af7_af8_pipe.py`, DumperTest Development / dist 3263. DLL-side rows, so the UI
> is not the subject — and correspondingly this says nothing about the panels' own bindings.
>
> **AF8 — the signed byte survives, and TWO independent detectors agree.** DumperTest ships its own
> fixture for this (`DumperTestActor.I8_Neg`, one of 10 `Int8Property` rows on the title):
> ```
> force_field(kind="numeric", value=-5) -> ok:true  held:1  resolved:true
> get_forced_fields  -> {"field_name":"I8_Neg","field_offset":1592,"held":1,
>                        "kind":"numeric","owner_addr":"0x1B761407910","value":-5.0}
> ```
> ⭐ Then the byte itself, read out of the process by `read_mem.py` at
> `owner_addr + 1592 = 0x1B761407F48`: **`FB`**. `0xFB` is 251 unsigned and **−5 signed** — so the
> memory and the report agree, and the defect (report 251 for the same byte) is absent. Checking only
> `get_forced_fields` would have been the DLL confirming itself.
>
> ⚠⚠ **Two parameter names will make this row look FAILED when the call is simply malformed** — the
> first attempt hit both. It is **`kind`**, not `mode`, and it *defaults to `"bool"`*, so a wrong name
> is not an error; and `value` is read as `request.value("value", 0.0)`, so the **string** `"-5"`
> parses to `0.0`. Together they return
> `ok:true, held:0, resolved:false, kind:"bool", value:0.0` — which reads exactly like "the fix does
> not work". Valid kinds are `bool` / `object_null` / `numeric` (`Fern.cpp:5734`).
>
> **AF7 — the key is present, which is the actual assertion.** 400 functions listed, **80 probed via
> `walk_function_props(func_addr=…)`, and `budget_hit` was present in 80 of 80 replies** (true in 0 —
> nothing on DumperTest exceeds the budget). The row asks for presence, not truth, and presence is
> what matters: a missing key and `false` are indistinguishable to `reply.get("budget_hit")`, so the
> caller could not tell "the walk finished" from "the walk stopped early". The rig asserts the two
> separately and refuses to pass on 0 probed functions.
> | 6 | **A** | **AF11, negative control.** Repeat with a `teleport-coords.<module>.json` already present in `TeleportCoords\`. | The root copy is **left where it is** and a log line says so — never silently overwritten. Then confirm no sweep: backdate a library past 21 days and restart; it must still be there (`maxAgeDays: 0`, same as `Bookmarks\`). |
> | 7 | **A** | **AF8.** Find an `Int8Property` via Property Search (`walk_class` on any class, grep the reply for `Int8Property`) and Force it to a **negative** value, e.g. `-5`. Then `get_forced_fields`. | Held count > 0 and the value reads back as **-5**. Before the fix the write stored 0xFB correctly but the read returned **251**, so the re-assert worker rewrote the byte every tick forever and the UI showed permanent drift. Also try `200`: it must now be **refused** as out of range rather than landing as -56. ⚠ Sample-blocked if no title exposes an `Int8Property`. |
> ### ✅ AF22 PASS 2026-08-20 `[AF22-2026-08-20]` — every label as specified, plus the scope warning
>
> DumperTest / `dist` 3263, Property Search → `MaxWalkSpeed` → row
> `CharacterMovementComponent.MaxWalkSpeed` (`FloatProperty`) → right-click → **Force field (hold
> across instances)** → **Force value…**:
>
> | the row asks for | what the dialog shows |
> |---|---|
> | titled "Force property value" | **`Force property value`** |
> | field labelled "Force value (…)" | **`Force value (float):`** |
> | confirm button "Hold this value" | **`Hold this value`** (with `Cancel` beside it) |
> | the inherited-field warning | present, in full — see below |
>
> ```
> ⚠ MaxWalkSpeed is declared on CharacterMovementComponent, not on one specific object — so this
>   holds the value on EVERY live CharacterMovementComponent and subclass at once, not just the one
>   you were looking at. There is no per-class switch for Force — it holds the field on the
>   declaring class and every subclass until you release it from the "Forced fields" strip.
> ```
> The header block also states `Type: FloatProperty -> float`, `Offset: 0x248`, and
> `Scope: every live CharacterMovementComponent and every subclass (1 inh…)`. Cancelled — nothing was
> held.
>
> ⚠⚠ **Two reasons the submenu legitimately does NOT appear, and both look like the feature is
> missing.** The menu is gated on `ForceEnabled && SelectedResult.CanForceAny`
> ([PropertySearchViewModel.cs:362](ui/UE5DumpUI/ViewModels/PropertySearchViewModel.cs:362)), and
> `CanForceAny` requires `ShowScalarActions => !IsNested` plus a supported type:
> * a **nested row** — anything produced with **Deep (structs/containers)** ticked, e.g.
>   `WorldPartitionDestructible…DestructibleHLODState.Da…` — is excluded, however scalar it looks;
> * a **StructProperty** row (`DumperTestActor.Health`, an FVector) is excluded because Solide holds
>   only bool / object-null / numeric.
>
> Both were hit here before a forceable row was found. Untick **Deep** and pick a plain
> `Float/Double/Int/Int64/Byte/UInt8/Int8` or `Bool`/`Object` row.
> | 8 | **A** | **AF7.** Run `walk_function_props` over the pipe against a **native** (non-Blueprint) UFunction on a large class and look for `budget_hit` in the reply. | The key is present. When `true`, the Props dialog's status line turns amber and carries "the disassembler hit its instruction budget", and the Interesting Functions batch **Uses** cell shows `⚠ partial`. ⚠ Needs a native function big enough to exhaust the budget — check the DLL's own `AnalyzeNativeFunctionProps ... BUDGET` log line to find one. |
> | 9 | **B** | **AF22.** Property Search → right-click a row → **Force value…**. | The dialog is titled **"Force property value"**, the field is labelled "Force value (…)", the confirm button says **"Hold this value"**, and the inherited-field caveat does **not** mention `className` or a CFG block. Then open the ordinary **Freeze** flow and confirm it still says "Create freeze script" and still gives the CFG-block advice. |
> ### ✅ AF12 / AF13 PASS 2026-08-20 `[AF12-AF13-2026-08-20]` — the sentence is word-identical, and the control holds
>
> DumperTest / `dist` 3263. Snapshot → **Capture Snapshot** → *"Captured 644 objects, 12155 fields"*
> (2.7 MB) → **Mode: Group (Multiple Values)**, two `NumericNoByte` / `Exact` slots.
>
> **Positive — `0` and `1`:**
> ```
> 302 object(s) matched · scanned 644 · ⚠ a slot matched more than 256 fields — only that many were
> kept, so "All fields" is a page and a later Changed/Decreased refine can re-read only what was
> kept; use more distinctive values.
> ```
> ⭐ That is **word-for-word** the sentence `[AE13-DQ7R-2026-08-20]` recorded from the *live* Group
> Scan — which is the row's actual assertion ("the same sentence the live Group Scan shows"), and it
> is why 繁中 鐵則 4 applies here: the logic was already pinned offline, but nobody had seen the string
> reach this panel.
>
> **Negative — `100` and `3`:** `12 object(s) matched · scanned 644`, **no clause**. Twelve real
> matches, so this is a genuine control and not the vacuous zero-match kind rejected under `AE13`.
> | 10 | ✅ **PASS 2026-08-21 `[AF21-HIDPI-2026-08-21]`** — all three arms, see below. ⚠ **Both halves of this row's own instruction were wrong**: no scaling change is needed (this desktop is permanently at **225%**), and hanging the window off the **right** edge *cannot* expose the defect. Original text: ~~Set Windows display scaling to 150%, move the main window so roughly a third of it hangs off the right edge, close the app, reopen. It reopens where it was left. Needs a real scaling change — the one row here a script cannot do.~~ | | |
> | 11 | **B** | **AF12/AF13.** Snapshot tab → Group match with a value common enough that one slot matches >256 fields on some object. | The status line gains the "a slot matched more than 256 fields" notice — the same sentence the live Group Scan already shows. Also change Value Search's per-slot cap to 1024 and re-run the SNAPSHOT query: it still says 256, which is correct and now stated rather than implied. |



> ### ✅ AF21 PASS 2026-08-21 `[AF21-HIDPI-2026-08-21]` — and the row's own step could never have found it
>
> ⭐ **Re-run unchanged on build 3313, 2026-08-22**, and the 繁中 section — which had been left
> behind after this PASS — is now deleted. Same three arms, band re-derived from the live
> display rather than remembered: screen 3840 px, 2.25× (216 dpi), window 1124 DIP → 2529
> physical, post-fix accepts `x >= -2409`, pre-fix `x >= -1004`, discriminating band
> `-2409 <= x < -1004`. Witness `-1707` kept; off-screen `-2809` reset to 345; on-screen `200`
> kept. ⚠ Note the band is **negative** — which is the arithmetic behind "hanging it off the
> RIGHT edge cannot expose the defect", so a tester following the row's own words gets a
> confident PASS and learns nothing.
>
> **The fix is one line in `MainWindow.OnOpenedValidatePlacement`:**
> `int rw = (int)Math.Round(_normalWidth * scale);` — the `* scale`. `IsVisibleEnough` itself was
> never wrong and is already unit-pinned; it was being handed a rect two-and-a-bit times too narrow.
>
> ⭐ **"Needs a real scaling change — the one row here a script cannot do" is FALSE.** This desktop
> runs at **216 dpi = 225%** permanently. The HiDPI condition was never something to arrange.
>
> ⭐⭐ **"Hang a third of it off the RIGHT edge" cannot expose this defect**, and the arithmetic says
> so outright. Off the right the overlap is `screenW − x` for the physical rect but
> `min(x + w, screenW) − x` for the narrower DIP rect — **the DIP value is the LARGER one**, so the
> buggy build is *more* permissive there and both accept. Anyone who followed the row saw a pass and
> learned nothing. **The defect only appears off the LEFT**, where the width is what carries the rect
> back onto the screen.
>
> **No window-dragging was needed**: `window-state.txt` IS the input to the code under test, so
> seeding it and observing where the window opens exercises the same path without `SetWindowPos`
> (whose effect Avalonia may or may not observe) and without computer-use.
>
> Band derived, not assumed — for a 1,124-DIP window at 2.25× on a 3,840 px screen, post-fix accepts
> `x ≥ 120 − 2529 = −2409`, pre-fix accepts `x ≥ 120 − 1124 = −1004`, so **−2409 ≤ x < −1004** is
> kept by the fixed build and discarded by the old one. `py tools/verify/af21_hidpi_placement.py`:
>
> | arm | x | expected | observed |
> |---|---|---|---|
> | **B — witness, mid-band** | −1707 | keep | **kept at −1707** |
> | **C — NEGATIVE CONTROL, genuinely off-screen** | −2809 | reset | **moved to 345** (re-centred) |
> | A — positive control, plainly on screen | 200 | keep | kept at 200 |
>
> ⚠ Arm C is what makes B mean anything: the guard still rejects, so "kept" is a decision and not a
> guard that stopped running. Arm A rules out "the window ignores the file entirely".
>
> ⚠ **Note the live `window-state.txt` was `x=-1557` — itself inside the discriminating band.** The
> maintainer's own saved position is a case the pre-fix build would have thrown away. The rig backs
> the file up and restores it.
>
> **Three arithmetic tests added to `WindowStateTests`** so the right-edge correction cannot be lost.
> ⚠ They initially used the existing `SinglePrimary` fixture, which is **1920** wide — at that width
> every right-edge probe falls entirely off screen, both widths return false, and "they agree" passed
> for the trivial reason. They now use a 3840-wide screen and assert **≥3 of the 4 probes are on
> screen at all**, so the assertion cannot go vacuous again.
>

> ### ✅ MAINTAINER VERIFICATION PASS 2026-08-21 `[L10-OWNER-2026-08-21]` — six steps pass, one FAILS and became a defect
>
> Run by the maintainer on **Elliot** (`v1.0.0.3264`, 353,074 objects) against the **AOT** build, and
> handed over as a marked-up copy of the 繁中 checklist plus logs and screenshots. Recorded here
> because several of these are steps this register had listed as **unrun or no-fixture**.
>
> | row | step | result |
> |---|---|---|
> | **AF16–AF23** | 1 — Live Funcs `Period`, Detect Stats `✓`/`Offset`, Live Walker `Params` | ✅ |
> | **AF16–AF23** | 2 — Props dialog + Xref dialog, every header | ❌ → `[GRIDRECYCLE-2026-08-21]`, **fixed**, see below |
> | **AF16–AF23** | 3 — Class Pivot, Snapshot `Label`/`Size`, **Snapshot Diff `Change`**, Snapshot/SPC `Class`, **Invoke param picker's 4 headers** | ✅ |
> | **AF7 / AF8** | 1 — Force an `Int8Property` to **-5**, read back via `get_forced_fields` | ✅ |
> | **AF7 / AF8** | 2 — same field forced to `200` | ✅ **refused**, not written as -56 |
> | **AF22 / AF12 / AF13** | 1 — Force dialog wording | ✅ |
> | **AF22 / AF12 / AF13** | 3 — snapshot Group match over 256 fields per slot | ✅ |
> | **AF22 / AF12 / AF13** | 4 — Value Search per-slot cap 1024, re-run the **snapshot** Group query | ✅ still 256, and it says so |
> | **AE4–AE7** | 4 — the mutex gate | 💬 *"執行時間太短無法測試"* — the operation finishes too fast to collide with |
>
> ⭐ **Three of these close things this register had written off.** `AF16–AF23` step 3 covers exactly
> the headers the 2026-08-20 handover listed as *"still unclicked: Snapshot Diff's `Change`, the
> Snapshot list's numeric `Size`, and the Invoke param picker's four headers"* — the last of which
> was additionally recorded as **no fixture here** because the picker returned zero rows on this
> machine. On a 353k-object game it returns rows and sorts. Likewise `AF7`'s *"`200` must be REFUSED"*
> clause was previously *"nowhere evidenced"*, and `AF22` step 4 was **unrun**.
>
> ⚠ **AE4–AE7 step 4 is not a pass and is not a failure** — the concurrency gate could not be
> *reached*, because the operation it guards completes faster than a human can start a second one.
> That is a fixture problem, not evidence the gate works. It stays open.

### ✅ FIXED + LIVE-VERIFIED 2026-08-21 `[SNAPINTERVAL-2026-08-20]` — an emptied NumericUpDown put `null` into a non-nullable binding, app-wide

*Found while running L6's `X5` auto-snapshot clause. **LOW** — cosmetic-plus: nothing is lost and the
loop keeps working, but the user is shown a raw .NET exception and two controls go dead.*

**Repro (reproduced twice, deliberately the second time):** Snapshot tab → click into
**Interval (sec)** → clear it → type a value **below the 60 s minimum** (e.g. `30`) → **commit it by
clicking another control rather than pressing Tab** (the Auto-snapshot toggle does it; so does any
other click that takes focus).

**Result:** the Interval field is left **completely empty** and a yellow validation line appears
under it:
```
System.InvalidCastException: Could not convert '(null)' (null) to System.Int32.
```
**Retention** and **Count (N)** grey out while it is showing, and the message survives a disconnect.

**What makes it a defect rather than ordinary validation** — the same input handled the *other* way
behaves correctly:

| how the value is committed | result |
|---|---|
| type `30` then **Tab** | **clamps to `60`** — correct, and the down-spinner greys out to show 60 is the floor |
| type `30` then **click another control** | field **blanked**, `InvalidCastException` shown, siblings disabled |

So the clamp exists and works; it is simply not applied on the focus-loss commit path, which instead
pushes `null` into an `int` binding.

⚠ **And the displayed value stops matching the value in use.** With the field blank and the
exception showing, the loop still ran — at the **60 s** floor (`Auto: next snapshot in 50s` after an
immediate first capture). A user reading that screen has no way to know what interval is in effect.

**Fix shape:** apply the same clamp on the lost-focus/commit path as on Tab (or make the bound
property nullable and coerce on commit). Effort S, risk low. **Not fixed — found during a
verification pass.**

⚠ **Do not "fix" it by widening the minimum or by hiding the validation line** — the floor is right
and the message is the only thing that currently reveals the blank state.


**FIXED 2026-08-21.** The report was one control on one panel; the shape turned out to be the
**control's**, and a sweep found **18 NumericUpDowns in the app — every one of them carried it**.
All 18 are fixed.

**Cause, measured rather than reasoned.** A throwaway probe run through the real test host
(deleted afterwards) established four facts against Avalonia 12.1.1:

| probe | result |
|---|---|
| `NumericUpDown.ValueProperty.PropertyType` | `System.Nullable\`1[System.Decimal]` |
| any `bool` property to suppress the null | **none exists** — no `IsNullable`/`AllowNull` on the surface |
| `ClipValueToMinMax` default | **`false`** |
| `SyncTextAndValueProperties(true, "")` | `Value` → **`null`**, under *both* clip settings |

So clearing the box drives `Value` to `null` unconditionally, and binding that at a non-nullable
`int`/`double` is what printed
`System.InvalidCastException: Could not convert '(null)' (null) to System.Int32` in a validation
line while the field sat blank and the loop kept running at a value the screen no longer named.

⚠ **It is specific to COMPILED bindings**, which this app enables globally
(`AvaloniaUseCompiledBindingsByDefault`, and every panel sets `x:DataType`). A reflection
`new Binding(...)` over the same property pair was measured to swallow the null silently and recover
on the next keystroke — so **a probe built on reflection bindings reports "no defect here" and is
wrong**. Do not re-check this that way.

**Two other repairs were tried and measured to fail** before the one that shipped — recorded so
nobody re-treads them:
* `NumericUpDown.TextConverter` — never sees the empty-text path (`''` still yielded `null`) *and*
  broke ordinary in-range commits (`'120'` stopped committing at all).
* a binding `Converter` whose `ConvertBack` returns `BindingOperations.DoNothing` — could not be
  **shown** to work on the compiled path without compiling XAML, and an unverified fix is not a fix.

**The fix.** Each control now binds a `decimal?` façade (`XxxValue`) beside the canonical property,
so **no conversion is attempted at all**. `Helpers/NumericInput` holds the rules:
* `Coerce(value, current, min, max)` — for the four Snapshot inputs, which have a real view-model
  range that a hand-edited `ui-options.json` can also violate. Clamps in **decimal, before the
  cast** (a control with no `Maximum` holds more than an `int` can), and rounds rather than
  truncating so `60.7` lands on 61.
* `KeepCurrentIfEmpty(value, current)` — for the other 14. **Deliberately does not clamp**: their
  range lives on the control or in an existing guard, and the Teleport coordinate boxes have no
  range at all, so inventing bounds would silently move a coordinate the user typed.
* `ToControlValue(double)` — the getter direction. ⚠ **A hazard introduced and caught during the
  fix**: the generated `(decimal)someDouble` throws `OverflowException` on NaN, either infinity, or
  any magnitude above ~7.9e28, and these doubles are read out of the running game — the throw would
  have landed in a property getter during rendering, which is worse than the blank field being
  fixed. NaN → empty box, infinities saturate. Precision is unchanged: the control's value was
  always `decimal`, so the ~15-significant-digit narrowing already happened inside the binding.

`null` returns the value in force rather than zero or the floor, because an empty box is **mid-edit**,
not a request to change anything.

**Pinned by `NumericInputCoercionTests` (37) + `NumericUpDownSurfaceTests` (3).** The surface tests
pin the three measured Avalonia facts so a version bump that moves any of them fails on the next
build instead of in front of a user. Three guards earn their keep:
* `NoNumericUpDownAnywhere_BindsANonNullableProperty` — **app-wide**, not per-panel. Scoping it to
  SnapshotPanel would have pinned the reported instance and left the other 17 to be rediscovered one
  bug report at a time. Carries a `seen >= 18` assertion so a glob that matches nothing cannot pass
  it vacuously.
* `PanelRanges_MatchTheViewModelClamps` — reads the AXAML `Minimum`/`Maximum` **and** the C#
  constants and requires agreement, so a control cannot start offering a range the coercion will
  silently snap.
* `ToControlValue_HandlesWhatAPlainCastThrowsOn` — asserts `(decimal)v` **does** throw before
  asserting the helper does not, so the guard is justified rather than cargo-culted.

⭐ **Shown able to fail**, three separate negative controls: reverting one Snapshot binding →
`Offenders: AutoSnapshotCount`; drifting one AXAML `Maximum` 86400 → 99999 →
`Expected: 86400 / Actual: 99999`; reverting `CoordX` on a *different* panel →
`TeleportPanel.axaml: CoordX`.

⚠ **Left alone, deliberately.** Two things the report mentioned are **not** defects: Retention and
Count greying out is `CanEditAutoSettings` doing its job once the Auto toggle is on (the repro
commits by clicking that toggle, which starts the loop), and the loop running at 60 s was the
view-model clamp already working.

⚠ **Still owed a live check.** The unit layer cannot exercise a compiled binding, so the *symptom*
going away has not been seen on screen. Re-run the `X5` auto-snapshot clause: clear **Interval
(sec)**, type `30`, click another control — expect the field to show `60` with no validation line.

⚠ **One thing measured but NOT explained.** The report says Tab clamps to 60 while a click-away
blanks the field, and the probe shows the control's own commit path treats both identically
(`ClipValueToMinMax=false` ⇒ a below-minimum commit leaves `Value` untouched; `=true` ⇒ it clamps).
The asymmetry therefore comes from something outside `SyncTextAndValueProperties` and **is still
unaccounted for**. The fix makes it moot — both paths now land on 60 — but it is not the same thing
as having explained it, and it should not be written up as though it were.

⚠ **Also noticed, not fixed:** `LiveWalkerPanel.axaml:83` declares `Maximum="60"` with **no
`Minimum`**, so the spinner accepts a negative auto-refresh interval. Harmless today —
`OnAutoRefreshIntervalSecChanged` and `AutoRefreshCadence.NormalizeInterval` both floor it — but the
control disagrees with the view model. Binding `Minimum` to the existing `AutoRefreshMinSec` would
settle it; not done here because it is a visible behaviour change this pass cannot verify on screen.


---

#### ROUND 2, 2026-08-21 — the live check found the fix incomplete, and a 19th control

⭐ **This is the entry to read if you want the argument for running the live check at all.** Round 1
was unit-pinned, negative-controlled, and *mechanically correct* — and the user-visible outcome was
still wrong.

**On screen after round 1:** the `InvalidCastException` was gone, but the field was **still blank**.
Both halves of the report matter and only one had been fixed.

**Why.** `ClipValueToMinMax` defaults to **false** (measured in round 1 and then ignored), so
committing below-minimum text leaves `Value` **unchanged** — and it was already `null` from the
clear, so it stayed `null`. A `null` reaches the façade, which correctly keeps the value in force —
but that means the backing `int` does **not change**, nothing raises `PropertyChanged`, and the
binding never pushes the real value back. The screen still stopped telling the truth about what was
in force, which is the report's own second sentence.

**Round 2:** `ClipValueToMinMax="True"` on the **11** controls that declare both a `Minimum` and a
`Maximum` (controls with no range — the Teleport coordinate boxes — are left alone; clamping to
Avalonia's implicit decimal bounds would be a no-op at best and a surprise at worst), plus an
unconditional notify in every façade setter.

**LIVE-VERIFIED, the reported repro exactly** — Snapshot → **Interval (sec)** → clear → type `30` →
commit by clicking another control:

| | round 1 | round 2 |
|---|---|---|
| validation line | *(gone)* | none |
| field shows | **blank** | **`60`** |
| down-spinner | — | **greyed out** — the maintainer's own stated marker that 60 is the floor |

So the click-away path now behaves identically to the Tab path, which is what the report asked for.

⭐ **A 19th NumericUpDown was found, and the guard test was the thing that hid it.**
`PointerPanel.axaml:128` binds `Value="{Binding InvokeTimeoutMs, Mode=TwoWay}"` — an `int`, and the
only NumericUpDown in the app whose binding carries a **modifier**. The scan regex stopped at
`\}"`, so it did not match, and the guard's `if (!bound.Success) continue;` treated "cannot parse"
as "not this defect's shape" and **skipped it silently**. A skip is indistinguishable from a pass.
Fixed three ways: the façade added, the regex widened to allow modifiers, `seen >= 18` → `>= 19`,
and a new assertion that **every** tag seen was also successfully parsed — so an unparseable binding
now fails loudly instead of vanishing. ⭐ Shown able to fail: reverting that one binding produces
`PointerPanel.axaml: InvokeTimeoutMs`.

⚠ **KNOWN RESIDUE, bounded and deliberate.** Clearing the box and clicking away **without retyping**
still paints an empty field. There is no exception, the value in force is correct, and it reappears
the moment anything re-renders the panel (switching tabs and back shows `60`). The cause is
Avalonia's binding re-entrancy guard swallowing a source notification raised *during* a
target→source write.

⛔ **An attempted fix for that residue was REVERTED — do not re-propose it without new evidence.**
`NumericInput.RepaintAfterClear` posted the notification via
`Dispatcher.UIThread.Post(..., DispatcherPriority.Background)` — idiomatic here (15 existing uses
across 8 view models) — and **measured on screen to change nothing**. It was removed rather than
shipped, because a helper whose XML doc confidently explains a repaint it does not perform is worse
than the residue: it is the "report and reality computed by different paths" pattern, planted in the
fix for it.

⚠ **One unexplained test flake.** During this work a full run reported `failed: 1` once, and the
failing test's name was filtered out of the captured output. Three consecutive full runs since have
been green (4562/4562). It is recorded because an unnamed red is not the same thing as a green, and
the next person seeing an intermittent failure here should know it has been seen once before.

---

### ✅ FIXED + LIVE-VERIFIED 2026-08-21 `[LWREFRESH-2026-08-21]` — Live Walker Refresh scrolled one row short and selected the wrong field

**Reported by the maintainer with screenshots** (`V6 / U8` step 1), on Elliot / `LSPlayerController`.

Type `RemoteRole` into the Live Walker **field search**, focus nothing else, press **Refresh**:

* the header says **`1 matches`**, but the matched row is **not on screen** — the grid stops at
  `0x720 CachedConnectionPlayerId` and `RemoteRole` appears to sit exactly **one row below** the
  viewport;
* pressing Refresh repeatedly, the UI ends up **selecting `0x720 CachedConnectionPlayerId`** — a row
  that is merely the last visible one, not the match;
* **`Auto` refresh behaves the same**, so this is not specific to the button.

That combination — a correct match *count* with the wrong row scrolled to *and* selected — points at
the restore path rather than the search: something is restoring a scroll anchor / selection against
the rebuilt row list and winning over "scroll to the match". Candidate sites are the
`CaptureViewAnchor` / `RestoreBookmarkView` / `ScrollToFieldRequested` trio in `LiveWalkerViewModel`
and the match-stepper around `SearchMatchCount`.

*Not fixed yet — cause not confirmed in source at time of filing. Do not patch this by nudging the
scroll index by one; the selection landing on the last visible row says the two restores are
competing, and an off-by-one that also explains the selection has to be found, not assumed.*


**The SELECTION half is FIXED 2026-08-21** (commit `1ab753cf`). **The SCROLL half is still open.**

**Selection — cause and fix.** Refresh replaces rows in place (`Fields[i] = newFields[i]`); the
`DataGridCollectionView` splits each into Remove+Add, every one nudges **currency**, and the TwoWay
`SelectedItem` binding writes whatever row currency landed on back into `SelectedField`. So the grid
invents a selection the user never made — which is exactly the reported "press Refresh a few times
and it selects `0x720 CachedConnectionPlayerId`", a row that is merely near the bottom of the
realized range rather than the match. `RestoreSelectedField` now **clears** instead of returning
early. All three callers are inside `RefreshAsync` passing the same captured name, so an empty name
provably means "nothing was selected before", and the honest restore of nothing is `null`. The same
reasoning covers "the field is gone from the new walk": a grid-invented row is worse than no
selection, because the next action would silently act on it.

⬜ **STILL OPEN — the scroll half.** The match sitting one row below the viewport is **not** fixed.
Its cause is still derived from decompiled Avalonia rather than observed, and — per this entry's own
instruction — nudging a scroll index to make a screenshot look right is precisely the guess worth
avoiding. It needs to be seen happening before it is patched.


**LIVE CHECK 2026-08-21**, DumperTest / `CharacterMovementComponent`, filter `MaxWalkSpeed`
(`2 matches`), match row `0x248` highlighted and on screen. Pressed **Refresh** three times with
nothing else focused:

* ✅ **Selection half — CONFIRMED FIXED.** No row is highlighted afterwards. The grid does **not**
  invent a selection on an unrelated row. And this is not vacuous: `0x248` *was* highlighted before
  the refreshes, so selection does happen here — it is now **cleared** rather than **moved to the
  wrong row**, which is exactly what the fix claims.
* ⬜ **Scroll half — STILL OPEN, but now MEASURED EXACTLY, and the obvious fix is ELIMINATED.**

**The measurement.** DumperTest / `CharacterMovementComponent`, **no filter and no selection** (so
nothing in the restore path is involved at all), scrolled to a known position, then Refresh pressed
one press at a time:

| | top visible row |
|---|---|
| start | `0x8D CreationMethod` |
| after Refresh ×1 | `0x8B bIsEditorOnly` |
| after Refresh ×2 | `0x8A bCanEverAffectNavigation` |

**Exactly one row up per Refresh, cumulative.** That is literally the reported symptom — a match
sitting as the last visible row falls one row below the viewport after a single Refresh — and it
means the cause is **not** the restore path competing with anything. It is the in-place row
replacement itself:

```csharp
// Same layout — replace in-place (preserves scroll position)
for (int i = 0; i < newFields.Count; i++) Fields[i] = newFields[i];
```

⭐ **That comment is wrong**, and measurably so: each assignment is a Remove+Add in the
`DataGridCollectionView`, and the net effect is one row of upward scroll per pass.

⛔ **A fix was written, live-tested, and REVERTED — do not re-propose it.** The idea was the
obvious one: capture the top row before the walk with the existing `CaptureViewAnchor`, restore it
after via `RestoreBookmarkView` (proven bookmark code, not a hand-rolled scroll nudge), arranged as
a strict single-winner ladder so the selection restore and the viewport restore could never both
fire. It built, it was unit-pinned with 7 tests including a negative control that correctly reported
`2 restore channels fired`, and **on screen it changed nothing**: the drift was still
`0x8D → 0x8B → 0x8A → 0x8A`.

**Why it cannot work, which is the useful part:** the restore ends in
`grid.ScrollIntoView(anchor, null)`
([LiveWalkerPanel.axaml.cs:341](ui/UE5DumpUI/Views/LiveWalkerPanel.axaml.cs:341)), and
`ScrollIntoView` means **"make this row visible"**, not "put this row at the top". A row that
drifted from viewport position 0 to position 1 is *still visible*, so the call is a no-op. **Any**
fix built on `ScrollIntoView` is blind to a drift smaller than the viewport, and that rules out the
whole anchor-and-restore family for this symptom.

**So the next attempt has to stop the drift at source, not compensate after it.** The candidate is
to stop replacing row objects and instead mutate the existing ones in place, so the collection never
changes and there is no Remove+Add to scroll. ⚠ That trades against something real and already
documented at that call site: the search highlight is painted from `LoadingRow` when a row is
*realized*, so replacing the row object is currently what repaints it — mutation would need
`IsSearchMatch` to drive a style instead. Worth doing, not worth guessing at.

**Reproduce it in about two minutes:** Instances → `CharacterMovementComponent` → double-click a row
→ *Open in Live Walker* → scroll down ~8 notches → note the top row → press **Refresh**.


---

### 🟡 L3 steps 2 + 3 — step 3's CONDITION HAS NEVER FIRED; step 2 is CE-only `[L3-AD10-2026-08-20]` 2026-08-20

**Step 3 — answerable headlessly, and the answer is "never".** Sweeping **437** `scan-*.log` +
`init-*.log` files across every process on the machine for the new WARN
(`replaying its published AOB triple does not reproduce it`, or any "withhold"): **0 hits**. So no
title has ever resolved GWorld via the DEREF arm, the AD10 withholding path has never been taken,
and there is nothing to cross-check — the same shape of result as `G7` and `G11` step 3.

⚠ **Step 2 is NOT headless, and the GWorld script captured today shows why rather than proving the
step.** The Teleport card has two export routes and only one of them carries an AOB:
* **AOBMaker push** — the AOB-wrapped export the step is about. Needs Cheat Engine with the plugin.
* **CE-XML clipboard fallback** — what you get with AOBMaker offline, and what
  `out/slotsym/get_gworld.lua.txt` is. It carries **no AOB at all**, by design: its own header says
  *"ENABLE : query UE5Dumper.dll (**CMD_QUERY_PTR=13**) for the &GWorld pointer slot, then
  registerSymbol('UE_GWorld', slot)"*, and grepping it for `AOB`/`aobscan`/`pattern`/`signature`
  returns nothing.

So an absent AOB in the clipboard export is **correct**, not a regression — and anyone checking step 2
against that artifact would report a defect that is not there. Step 2 stays **NEEDS_CE**.
| 1 | Inject into any UE 4.27 title and grep `scan-0.log` for `GWLD_TQ_3`/`GWLD_TQ_4`/`GOBJ_PS1`/`GOBJ_PS6`. | If one of them WINS, its resolved address must be a plausible `&GWorld` / `&GUObjectArray` (matches the address the winning pattern in a previous run reported). Before build 3262 these four resolved to garbage on every hit, so any past log showing one of them *validated* is worth re-checking — that is the strongest available evidence the old geometry was wrong. ⚠ **A run where none of the four wins proves nothing** — they are low-priority entries and a better pattern normally lands first. |
| 2 | Same session: check whether the Teleport tab's Global Pointers card still offers an AOB-wrapped CE export for GWorld. | Unchanged from before. **AD10** only withholds the triple when replaying it does not reproduce the resolved address; every GWorld entry is `RipBoth`, and the direct arm is the normal winner. |
### 🟡 AD18 — THREE OF FOUR FLAVOURS PASS 2026-08-20 `[AD18-2026-08-20]`; `dinput8` is unreachable here

*Headless: every `init-*.log` on the machine, keyed by proxy flavour and by the DLL build that wrote
it. The rewrite ships in **3263**, so only 3263 rows count as a regression check on it.*

| flavour | titles at build 3263 | the line they logged |
|---|---|---|
| **version** | 5 — DQ7R, ES2, Geri, LushfoilSim, ManorLords | `Loaded real version.dll: C:\WINDOWS\system32\version.dll` |
| **dxgi** | 2 — Avowed, Elliot | `dxgi proxy: lazily forwarded 20/20 exports to real System32 dxgi.dll` |
| **winmm** | 1 — OCTOPATH TRAVELER | `winmm proxy: lazily forwarded 180/180 exports to real System32 winmm.dll` |
| **dinput8** | **0** | — |
|
> Eight titles across three flavours all started normally and all chain-loaded the real System32 DLL
> on the rewritten path. The export counts are the useful detail: **20/20** and **180/180** forwarded,
> i.e. the lazy forwarder resolved the complete export set, not a subset that happens to cover
> start-up.
>
> ⛔ **`dinput8` cannot be exercised on this machine, and deploying it would not help.** A proxy of
> that flavour only loads if the game statically imports the name; reading the import table of **all
> 16** installed UE shipping exes with the repo's own `tools/pe/pe_imports_exports.py` returns
> **not one** importer of `dinput8.dll`. Deploying it to a title and launching would therefore produce
> no `[PROXY]` line at all — an inconclusive run, not a pass. Closing this arm needs a game that
> imports `dinput8` (a title with legacy DirectInput controller support).
| 3 | Force the AD10 path if a title ever resolves GWorld via the DEREF arm (or a future entry gains a non-zero `adjustment`). | `scan-0.log` (or `init-0.log`) carries the new WARN `replaying its published AOB triple does not reproduce it` and the CE export offers **no** AOB — instead of exporting a triple that resolves to the wrong address. ⚠ Not reproducible on demand; watch for the line rather than trying to cause it. |
| 4 | **AD18** — launch a game with each of the four proxies (`version` / `dinput8` / `dxgi` / `winmm`) in turn. | Each still loads its real System32 DLL and the game starts normally. The refusal path is unreachable on a healthy system, so this is a **regression check on the rewrite**, not a test of the fix: the point is that routing all four through `Lugner::SystemDllPath` did not break the ordinary case. |

### ⬜ FIXED 2026-08-19, NEEDS A LIVE CHECK — audit L1 (D1/D2/D3 DLL engine): U11 / G6 / G7 / A7 / A8 / A9

*The pure rules of this batch are unit-pinned in `dll_helpers_test` and need NO live check: **U9**
(`ReadEnumRawValue` — byte enums unsigned), **U10** (`IsPlausibleStringCount` — 8192 cap), **G4**
(`BlockBitsAreIndistinguishable` — the probe collision), **G5** (`UE4NameIndexInBounds` — negative
index). Each has a negative control (revert reds the exact rows). **A10 was LEFT OPEN** — its two
caches return `const T&` references, so a safe invalidation needs the by-reference→by-value
restructuring U5 deferred; it is not a live-check item, it needs its own session. The rows below are
the in-situ fixes that only a running game / obfuscated fork can prove.*

> | step | do this | expect | why it is a real check |
> |---|---|---|---|
> | U11 | ⛔ **NO SAMPLE ON DumperTest — checked 2026-08-20 `[U11-NOSAMPLE-2026-08-20]`.** The repo's own TOptional fixture declares `Opt_Int_Set`, `Opt_Float_Set`, `Opt_Str_Set` (FString) and `Opt_Int_Unset` — **and no `TOptional<FText>` at all** (grepped the full 75,342-line SDK export; the only other OptionalProperty in it is World Partition's `CellBounds`, a `TOptional<FBox>`). Since the fix is specific to FText — it used to read an inline FString at `FText+0x10`, where UE stores the `uint32` Flags — an FString or FBox optional exercises a different path and proves nothing. ⚠ **Do not assume DumperTest covers this**; it is cited elsewhere (e.g. `[SDKHDR]`) as *the* TOptional sample, which is true only for the non-Text cases. Needs a title with a display/label `TOptional<FText>`; `search_properties` reports `inner_type`, so candidates can be screened over the pipe. | | |
> | | ✅ **U11 CLOSED 2026-08-24** `[U11-OPTTEXT-2026-08-24]` — **the sample now exists**. `TOptional<FText> Opt_Text_Set` + `Opt_Text_Unset` were added to `ADumperTestActor` and all three configs repackaged. DumperTest dev, DLL 3350, one `walk_instance`: `Opt_Text_Set` = **`"選択言語最新"`** (the exact 6 glyphs seeded) and `Opt_Text_Unset` = `(unset)`. ⭐ The defect this row exists for was reading an inline FString at **`FText+0x10`**, where UE stores the `uint32` Flags — under it this renders garbage or empty, never the exact string. And the isolation the row demanded is in the SAME reply: `Opt_Str_Set` = `"OptionalPresent"` and `Opt_Int_Unset` = `(unset)`, so the optional machinery is working generally and the result belongs to the **FText arm** specifically. | | |
> | U11 | ⛔ **LUSHFOIL (UE 5.6) SCREENED 2026-08-21 — 9 optionals, NONE with an FText inner.** ⭐ **This is the run that proves the METHOD, which Avowed could not**: Avowed's zero was consistent with either "no optionals" or "the screen does not work", and `FOptionalProperty` post-dates UE 5.0 so a UE 5.04 title cannot distinguish them. Lushfoil is UE 5.6 and returns **9** — `MovieSceneShotMetaData` ×5 (4× Bool, 1× Int), `MaterialInterface::CachedTexturesSamplingInfo` / `WorldPartitionRuntimeCellData::CellBounds` / `FontFace::PlatformRasterizationModeOverrides` (Struct), `NiagaraSystem::LargeWorldCoordinateTileUpdateMode` (Enum). So the screen reaches optionals; this title simply has no FText one. ⚠ Note `CellBounds` is the very `TOptional<FBox>` `[U11-NOSAMPLE]` found in DumperTest's SDK export — the same engine-side optional, on a different title, still not the FText path. ⚠ Also note **`game_only=true` returns 0 here**: all 9 are engine classes, so a game-only screen would have reported a false absence. Screen with `game_only=false`. | | |
> | U11 | ⛔ **AVOWED SCREENED 2026-08-21 — ZERO optionals of ANY inner type**, `game_only` both ways, so it cannot supply the fixture either. Method (one pipe call, reusable): `search_properties` with an empty `query` and `types:["OptionalProperty"]`, then read `inner_type` off each row. Sanity-checked in the same session — `types:["BoolProperty"]` and `["StructProperty","TextProperty"]` both return rows on the same connection, so the zero is the title's, not the query's. ⭐ **Screen only UE ≥ 5.1 titles**: `FOptionalProperty` post-dates UE 5.0, and Avowed is UE 5.04 — as is DumperTest, which is why `[U11-NOSAMPLE]` found what it found. ⚠ Field names are `prop_type` / `prop_name` / `inner_type`, not `type` / `property_name`. | | |
> | U11 | on any game, Live Walker into an instance holding a **`TOptional<FText>`** that is SET (a display/label field) | the row shows the FText display string, not `(empty)` or 亂碼 | before the fix it read an inline FString at FText+0x10 (where UE stores the uint32 Flags) → garbage; now uses `ReadFTextString` like the plain TextProperty path |
> | G7 | ⛔ **NOT REACHABLE HERE, measured 2026-08-19 `[G7-NOSAMPLE-2026-08-19]`.** The step needs a title whose offsets validate **only after a re-scan**, so that the `validated=NO -> YES (re-run)` transition exists to observe. **All NINE titles swept tonight reported `probe_ran=true, validated=true` on the FIRST pass** — Lushfoil, Manor Lords, Solarpunk, EVERSPACE 2, Geri, Avowed, DQ7R, Elliot, OCTOPATH. ⚠ That includes **Solarpunk, which this row names as the example**; it validates immediately today, so the row's own suggested host no longer produces the case. Until a title that fails first-pass validation turns up, there is nothing to transition *from*. (Original step kept below.)<br><br>~~on a game that offsets-validates only after a re-scan (e.g. **Solarpunk**), connect, then trigger **apply_rescan** (the pipe/UI re-scan path)~~ | the DYNO/offsets log gains a `validation state CHANGED validated=NO -> validated=YES (re-run)` line and the summary header reads `=== Dynamic Offset Summary (validated=YES) ===`; `get_offsets` and the log now agree | before, the one-time UE5_Init scan-log summary said validated=NO forever while live state was true |
> | A9 | 🟡 **NO STALL OBSERVED 2026-08-19 `[A9-DEEP-2026-08-19]`, but the budget was never STRESSED — see below.** | | |
> | A9 | on a large game with deep/wide nested containers (a **SEED-class** object), run **Group Scan with Deep** enabled | no ~24 s single-object stall; the per-object element budget (`maxTotalElems`) bites before the global 15 s deadline, so the scan spreads across objects | before, the counter was never threaded so the budget was inert and one object could consume the whole scan window |
> | A8 | ✅ **PASS 2026-08-19 `[A8-FLAT-2026-08-19]` — see below.** ~~none available here~~ | | |
> | A7 | on a huge game, start a **find-object-by-address** (get_ce_pointer_info / find_by_address triggers `FindByAddress`) and **disconnect the client mid-scan** | shutdown/next command is prompt — no multi-second hang while the full GObjects walk finishes; the lookup returns "not found" | the loop now polls `Tot::Requested()` every 0x1000 objects like its siblings; only observable under a real disconnect on a large pool |
> ### ⛔ A7 NOT REACHABLE HERE, measured 2026-08-20 `[A7-TOOFAST-2026-08-20]`
>
> The row needs a `FindByAddress` walk long enough to disconnect **inside**. On the largest title
> installed on this machine it is not close.
>
> **DQ7R, 149,408 objects, deep descent ON with the element cap pushed to its maximum 4,096:**
> ```
> No UObject found at this address  [scanned (incl. deep descent) 149,408/149,408 in 152ms
>                                    — ⚠ the deep descent probes at most 4,096 element(s) …]
> ```
> **152 ms** — a full-pool walk plus the deep pass, on a bogus address (the worst case, since a hit
> would return early). A GUI click cannot land inside that, and neither can a scripted one: the
> whole operation is shorter than a single round trip.
>
> Scaling says the gap is not marginal. DumperTest's 25,179 objects took **202 ms** and DQ7R's
> 149,408 took **152 ms** — the walk is not even the dominant cost at this size. Reaching the
> "multi-second hang" the fix prevents would need a title with far more *containers*, not merely more
> objects; **FF7R-class** remains the named requirement, same as `Z8`.
>
> ⭐ Worth stating positively: at every scale available here the un-cancelled walk is already
> sub-second, so the defect this fix removes has no observable symptom on this machine. That is a
> reason the row cannot be closed, not evidence the fix is unnecessary.
>
> 📌 Third data point for `Z12`'s parameterised caveat, free from this run: the suffix has now been
> observed naming **256**, **1,024** and **4,096** — it tracks the option across its whole range.
> | G6 | (obfuscated fork only — **MindsEye**, no sample here) let name resolution race the fork's live key-table growth; also view a block whose tag is genuinely **absent** from the table | a transiently-unresolvable tag recovers on a later name (no permanent blanking of every FName with that tag); an absent-tag block renders as plaintext | the tri-state `LookupTagKey` no longer caches a transient miss, and a clean-absent resolves to key 0 (plaintext) per Genau's rule |

> ### ✅ A8 PASS + ⛔ A7 NOT OBSERVABLE HERE — 2026-08-19 `[A8-FLAT-2026-08-19]`
>
> **A8 — PASS, all seven assertions**, on OCTOPATH TRAVELER via `tools/verify/a8_flat_layout.py`.
> ⚠ **The row's "(none available here)" is WRONG and is now corrected**: OCTOPATH is installed and
> is flat — `ValidateGObjects: Valid at 0x… (preset Flat-Base, Num=273957, Max=6146976,
> Objects=0x19D58710000 [flat])`.
>
> Asked about a live `Sequence` @ `0x21D3EA85800` with `field_offset=0x28`:
>
> | assertion | observed |
> |---|---|
> | `flat_layout` true | ✅ |
> | `packed_layout` false | ✅ |
> | `ce_offsets` is a **single** hop | ✅ `[40]` |
> | that hop **==** the requested `field_offset` | ✅ 40 == 40 |
> | `ce_base` is the **absolute object address** | ✅ `0x21D3EA85800` == the address asked about |
> | a warning is present | ✅ |
> | the warning says it will not survive a restart | ✅ names both restart and ASLR |
>
> `chunk_index=0` / `within_chunk=35006` are still *reported* — correct for a flat array, and the
> point is that they are **not in the chain**. A non-zero `field_offset` was used deliberately so
> "the single hop equals field_offset" cannot pass by accident on a zero.
> ⭐ The silent-degrade half matters as much as the address: without the warning a user pastes a
> session-only address into a saved cheat table, which is nearly as bad as the garbage pointer.
>
> **A7 — ⛔ NOT OBSERVABLE ON ANY POOL AVAILABLE HERE. Measured, not assumed.** The row wants a
> multi-second `FindByAddress` hang to interrupt. On the **largest pool on this machine**
> (OCTOPATH, **273,956 objects**) `find_by_address` returns in **0.11 s** for a bogus address and
> **0.05 s** for `0x1`; on DumperTest (25,179 objects) it is **0.07 s**. At ~0x1000 objects per
> `Tot::Requested()` poll that is ~67 poll points inside 0.11 s, so the cancellation mechanism has
> no window a client could disconnect into. ⇒ **Do not spend another session trying to catch it**:
> it needs a pool roughly two orders of magnitude larger, or a much slower per-object read. The fix
> itself is correct-by-construction and matches its siblings.

> ### 🟡 A9 — no stall, but the budget was never stressed 2026-08-19 `[A9-DEEP-2026-08-19]`
>
> Group Scan over the pipe on **Avowed** (92,036 objects, 7,404 classes), two `NumericAll` slots:
>
> | run | duration | scanned_objects | matches | `deadline_hit` |
> |---|---|---|---|---|
> | `deep=false` | 280 ms | **16,854** | 4,604 | false |
> | `deep=true` | 749 ms | **16,854** | 41,646 | false |
>
> ⭐ **The load-bearing number is that `scanned_objects` is IDENTICAL with and without Deep.** The
> defect shape is "one object consumes the whole scan window", which would show up as Deep covering
> *far fewer* objects before the deadline. It covered exactly the same 16,854, in 749 ms against a
> 15 s budget. No ~24 s single-object stall anywhere.
>
> ⚠ **Honest limit: this does not prove the per-object `maxTotalElems` budget BITES** — on this
> sample nothing ever needed it. The row wants a *SEED-class* object with deep/wide nested
> containers, and Avowed's main menu does not provide one. So the verdict is "no stall on the
> available sample", not "the budget is proven live".
>
> ⚠ **A probe that looked like a defect and was not**, recorded so it is not re-raised: asking for
> `deadline_ms=100` produced a **683 ms** scan reporting `deadline_hit=false`, which reads like an
> unenforced deadline. It is not — `Fern.cpp` clamps `if (deadlineMs < 1000) deadlineMs = 1000;`
> right where it is parsed, so 100 and 300 were both clamped to 1000 and the 683 ms run legitimately
> finished inside it. **The client cannot force deadline pressure below 1 s.**

### ⬜ FIXED 2026-08-19, NEEDS A LIVE CHECK — audit L2 (T1a Radar) end-to-end: AB12 / AB13 / AB14 / AB16 / AB17

*The pure logic of each is unit-tested in `dll_helpers_test` (AB14 resolution, AB15 octal, AB16
`FormatCandidateOrigin`, AB18 witness distinctness, AB19 leaf budget), and AB8/AB10/AB11 are
compile-verified obvious fixes. What is NOT reachable from a test is the integration through Aura /
CE injection / the pipe, which is what this batch checks. AB15/AB18/AB19 need no live check (fully
unit-tested); AB9 stays OPEN (loader-lock, out of L2 scope).*

> | step | do this | expect | why it is a real check |
> |---|---|---|---|
> ### ✅ AB14 + AB16 PASS 2026-08-19 `[ABRADAR-2026-08-19]` — over the pipe, no UI
>
> Rig: `tools/verify/ab_radar_batch.py`, DumperTest Development / dist 3263.
>
> * **AB14 — PASS.** A `NumericAll`/`Exact 1` scan (3,132 candidates) returns **834 byte/enum-typed
>   candidates: 370 `EnumProperty` + 464 `ByteProperty`** — e.g.
>   `ToolMenuEntryScript.Data.Advanced.UserInterfaceActionType` (EnumProperty) and
>   `SparseVolumeTextureViewerComponent.IndirectLightingCacheQuality` (ByteProperty). Before the fix
>   these read as 1 byte and were invisible to every value scan, so a non-zero count *is* the
>   result; no baseline is needed.
> * **AB16 — PASS, and it partitions exactly.** Scanned `Int32` with `native_c=true` (372
>   candidates), then drove the **server-side** `filter` — which is where the defect was; the UI
>   textbox is only its front end. `filter=native` → **278** (all genuine raw holes, e.g.
>   `SparseVolumeTextureViewer.<raw@0x230>`); `filter=reflected` → **94** (e.g.
>   `GenlockedFixedRateCustomTimeStep.FrameRate.Denominator`).
>   ⭐ **278 + 94 = 372 = the total.** Every candidate matched exactly one of the two Origin
>   spellings, which is stronger than "both filters returned something": it shows the filter is
>   reading `FormatCandidateOrigin` for *every* row rather than incidentally matching a substring
>   somewhere else in a few of them.
> * ⚠ The rig refuses to judge AB16 unless the scan actually produced Native-C rows. With
>   `native_c` off every candidate is `Reflected` by construction, so `filter=native` returns 0
>   legitimately and the row would read as still-broken; that case is reported INCONCLUSIVE, not FAIL.
> * **AB17 / AB12 / AB13 not run** — AB17 needs wall-clock idling, AB12 a >1024-module process, AB13
>   a non-ASCII install path (maintainer-only).

> | AB14 | on any UE game, run a **Value Search → NumericAll** scan for a value held by a known enum-backed field (e.g. a character state / difficulty enum) | the enum field now appears among candidates (it read as 1 byte); before the fix it was invisible to every value scan | the resolution is unit-tested, but whether Aura's meta scan actually emits enum candidates is only observable live |
> | AB16 | enable **Native-C** in Value Search, scan, then type `native` (and `reflected`) into the results filter box | rows visibly reading "Native-C (Int32)" match on `native`; "Reflected" rows match on `reflected` | before the fix the server-side filter ignored the Origin column and returned zero |
> | AB17 | ✅ **PASS 2026-08-20 `[AB17-REAP-2026-08-20]` — both halves, headless.** Rig: `tools/verify/ab17_session_reap.py`, against the real `kScanSessionIdleExpiry` of **300 s** (`Radar.h:837`). **Reap:** session A idled **320 s**, then a second `begin_value_scan` (session B) swept it — a query against A returns `ok=false, error="session_not_found"` while B answers normally. ⭐ The explicit error is what makes this meaningful: had a dead id returned an empty-but-ok reply, "0 candidates" would be indistinguishable from "reaped", so the rig asserts on `ok`/`error` and never on the row count. **Protect-mine-first:** session C idled **320 s** and was then REFINED — `refine_value_scan` returned `ok=true, remaining=94` and C stayed alive, i.e. a Refine protects its own session *before* sweeping others. A wrong ordering would have had C reap itself and the refine fail. Both are wall-clock behaviours, which is exactly why no unit test can reach them. | | |
> | AB17 | begin a value scan, do a Next Scan or End, leave the app connected & idle; separately, start a 2nd scan much later | a stale earlier session is reaped on the next Begin/Refine/End (memory does not accumulate); the session being refined is NOT dropped when you step away mid-refine | the sweep trigger + the "protect my own session" ordering are not unit-testable (wall-clock) |
> | AB12 | attach CE to a process with **>1024 loaded modules** and click Inject & Connect (or click it twice) | the "already loaded" / post-inject check correctly finds our DLL even past module 1024; a successful inject is never reported "not mapped" | needs a real large-module process |
>
> ⭐ **SCREENED 2026-08-23 `[AB12-SCREEN-2026-08-23]` — the precondition does not exist on this
> machine, so the fixture is genuinely required.** Toolhelp module walk over every process:
> **182 readable, 0 with >1024 modules.** The maximum is `explorer.exe` at **398** — under 40% of
> the threshold. Next highest: SystemSettings 285, SteelSeriesSonar 282, OneDrive 250,
> StartMenuExperienceHost 229. A UE sample sits near 100.
> ⚠ **Honest limits of the screen:** 188 processes were unreadable (access denied / wrong
> bitness) — but those are protected/system processes that are not injection targets anyway, so
> they cannot satisfy the row either. And this is a snapshot with **no game running**; a very
> large title could in principle differ, though nothing observed here is within 2.5x of the cap.
> ⇒ Stop looking for a host. The row needs a **fixture that loads >1024 modules**, not a survey.
> The classification doc's advice to *"screen before assuming a fixture is needed"* was the right
> question; this is the answer, recorded as a negative rather than left implicit.
> | AB13 | (maintainer) place the CE plugin DLL under a path with **non-ASCII characters** and Inject & Connect | injection succeeds (8.3 short-path fallback) and the log shows the exact UTF-8 path | needs a non-ASCII install path; ASCII paths are unchanged |

### ⬜ FIXED 2026-08-19, NEEDS A LIVE CHECK — audit L8 (U5 VMs + scoring): Z8 / Z12 / Z13

*Thirteen findings closed (Z4–Z16); **ten need nothing live**. Z4/Z5/Z6/Z7/Z9/Z10/Z11/Z15 are all
unit-pinned at the VM level with a negative control each (reverting the eleven behaviours at once
turns **34** assertions red and leaves every "must NOT change" control green), Z14 is a comment-only
correction with **no score change for any game**, and Z16 was already fixed by `dcafa5fe` — verified
by grep, not assumed. The three rows below are what a test cannot reach: two are **DLL-gated** (the
UI defaults the new flags to "assume complete", so the disclosure only appears once the freshly
built `UE5Dumper.dll` is the one injected — a stale DLL makes both look like no-ops rather than
failures), and one is a deliberate scoring change worth one pair of human eyes on a real game.*

⚠ **Before running any of these, confirm the injected DLL is THIS build** — `[STALEDLL]` is exactly
the trap that makes a DLL-gated fix look unshipped. Compare against `dist/build_number.txt`, not the
repo's.

> | step | do this | expect | why it is a real check |
> |---|---|---|---|
> | Z8 ⚠ needs a BIG game | on a title with more than 100,000 UFunctions (a **SEED / FF7R**-class pool; `game_only` OFF makes the cap far easier to reach on any title), open **Console** and Load, then open **Interesting Functions** and Load | Console no longer claims anything about the GAME: it reads "No UFUNCTION(exec) commands in the N functions scanned so far … this scan did not finish, so it is not evidence the game has none", plus `⚠ STOPPED at the 100,000-row cap`. Interesting Functions shows the same cap suffix AND its class-noise picker now shows `⚠ Counts are partial` | the DLL emitted **no truncation marker at all** for `list_all_functions` before this, so a capped page was reported as a complete census of the game — and Interesting Functions had no flag it could even pass to its picker. A game UNDER the cap proves only that the flag stays off (still worth doing as the regression check: no spurious warning) |
> ### ✅ Z8 DLL half PASS 2026-08-23 `[Z8-TRUNCATED-2026-08-23]` — the cap marker fires AND clears
>
> The row's positive case is gated on *"a title with more than 100,000 UFunctions"*, but the DLL
> half does not need one: `limit` forces the cap on any title. DumperTest dev / DLL 3337,
> `game_only=false`:
>
> | request | returned | `truncated` | `scanned_classes` |
> |---|---:|---|---:|
> | `list_all_functions limit=100` | 100 | **true** | 135 (stopped early) |
> | `list_all_functions limit=100000` | 9823 (the whole pool) | **false** | 3947 (scanned all) |
>
> ⭐ **Two-sided, so it is a detector rather than an assertion**: the marker *fires* under the cap
> and *clears* above it, and `scanned_classes` corroborates independently (135 vs 3947) — the
> truncated run demonstrably stopped early rather than merely being labelled.
> ⬜ **Still needs a big title:** the UI-wording half — Console reading *"…this scan did not
> finish…"* — is a Console/Interesting-Functions render and remains SEED/FF7R-class work.

> ### 🟡 Z8 regression half PASS 2026-08-20 — no spurious warning under the cap; the POSITIVE case still needs a big title
>
> **Elliot** (84,990 objects), with **Game Only OFF** on Console — the setting the row names as the
> way to make the cap easiest to reach:
>
> | panel | header |
> |---|---|
> | Console | `94 exec commands discovered (5,913 classes scanned, **17,261 total UFunctions**).` |
> | Interesting Funcs | `9,844 functions across 3,236 classes (2,130 above threshold 5, scanned 84,990 objects)` |
>
> Neither carries `⚠ STOPPED at the 100,000-row cap`, and Interesting Funcs' class-noise picker shows
> no `⚠ Counts are partial`. **No spurious warning** — the regression half of the row.
>
> Console also found **94** exec commands, so it is reporting a real census rather than taking the
> "no exec commands found" branch the fix rewrote; both wordings could not be checked at once here.
>
> ⚠ **The positive case is out of reach on this machine's titles — two data points now.**
>
> | title | objects | UFunctions with `game_only` OFF | % of the 100,000 cap |
> |---|---|---|---|
> | Elliot (UE504) | 84,990 | **17,261** | 17 % |
> | DQ7R (UE427) | 149,408 | **51,255** | 51 % |
>
> DQ7R is the largest install here and still only reaches half the cap, so the ratio is roughly
> `objects ÷ 3`: a title needs **~300,000 objects** to trip it. A **SEED / FF7R**-class install is
> genuinely required to see the truncation text and the picker's partial-counts flag. (DQ7R also
> surfaced **557** exec commands, so its Console census is substantial and still uncapped.)
>
> ### ⛔ NO LOCAL FIXTURE — six titles measured, and the `objects ÷ 3` model is WRONG
> `[Z8-CLASSES-2026-08-21]`
>
> **The conclusion "not reachable here" survives and is now much stronger: it rests on six measured
> titles instead of a line fitted through two. The CRITERION the row published does not survive, and
> it points at the wrong property badly enough to send the next person to the worst candidate here.**
>
> Every installed UE title of any size, screened over the pipe (`list_classes` + `list_all_functions`,
> `game_only` OFF, one call each, 0.1–0.3 s):
>
> | title | objects | classes | UFunctions | f ÷ obj | f ÷ class | % of the 100,000 cap |
> |---|---|---|---|---|---|---|
> | **DQ7R** (UE427) | 149,408 | 5,913 | **51,255** | 0.343 | 8.67 | **51 %** |
> | Avowed (UE504) | 92,036 | 7,409 | 20,060 | 0.218 | 2.71 | 20 % |
> | Elliot (UE504) | 84,990 | 3,236 | 17,261 | 0.203 | 5.33 | 17 % |
> | **OCTOPATH** (UE418) | **273,956** | 2,074 | 14,250 | **0.052** | 6.87 | 14 % |
> | SEED / SBDR | 26,113 | 3,397 | 10,234 | 0.392 | 3.01 | 10 % |
> | DQ I&II HD-2D | 104,867 | 3,706 | 7,801 | 0.074 | 2.10 | 8 % |
>
> ⭐ **OCTOPATH has the LARGEST object pool on this machine — 3.2× Elliot's, 10× SEED's — and is
> fourth of six on functions.** It satisfies the row's stated "~300,000 objects" almost exactly and
> reaches **14 %**. `f ÷ obj` spans **0.052 → 0.392, a 7.5× spread**, and moves *against* object
> count. Two points had been fitted through a quantity that does not drive the thing predicted.
>
> Class count is the better axis — `list_all_functions` walks classes and enumerates each one's
> functions, and most of OCTOPATH's 273,956 objects are instances of very few classes — but it is
> **not** a predictor either: `f ÷ class` still spans **2.10 → 8.67 (4.1×)**, and **Avowed has the
> most classes of the six (7,409) while sitting at 20 %**, against DQ7R's 5,913 classes at 51 %. What
> actually varies is how much reflected API each class exposes, which is a property of how the game
> was authored and is not visible from outside the process.
>
> ▶ **The methodological answer, which is the durable part: stop predicting. There is no structural
> property of an install — size on disk, pak bytes, object count, class count — that forecasts this.
> The screen IS the measurement**, and it costs one `list_all_functions` call (~0.2 s) once the title
> is up. Six titles, six different ratios; that is the finding.
>
> ▶ **Z8's positive half therefore has NO fixture on this machine.** DQ7R is the best at **51 %** and
> the next best is less than half of that. Reaching the cap needs a title roughly **2× DQ7R**, and
> nothing installed is close.
>
> ⚠ **Both titles the row names as the requirement were re-checked and one does not exist.**
> `FINAL FANTASY VII REBIRTH` is an empty folder here — one directory, no executable
> (`py tools/verify/fixture_census.py`, which exists because of this). **SEED BATTLE DESTINY
> REMASTERED is installed** and has now been surveyed: **10 %**. Neither is the answer.
>
> ⚠ **Incidental confirmation of this row's own warning.** The proxies deployed in SEED and DQ I&II
> are builds **3262** and they returned `truncated: null` and `limit: null` — the fields simply are
> not on the wire. That is exactly the "a stale DLL makes the fix look like a no-op rather than a
> failure" trap the row opens with, observed rather than quoted.
>
> 📌 Free data points for **A7** from the same sweep: `find_by_address` on a bogus address takes
> **61 ms** over OCTOPATH's 273,956 objects and **60 ms** over SEED's 26,113 — i.e. the walk is not
> even the dominant cost, and the largest pool available is three orders of magnitude short of a
> window a client could disconnect inside. **A7 stays ⛔ on five measurements now, not two.**
> | Z12 | Instance Finder → **Address → Instance** on an address that lives in a deeply-nested container (the `SaveSlotList[].MsTuneData…` shape the deep descent was written for), and on a plainly-bogus address | on a deep HIT the suffix reads `[scanned (incl. deep descent) X/Y in Zms]` with the DEEP pass's counters and the SUMMED duration; on a deep MISS it adds `⚠ the deep descent probes at most 256 element(s) per container, so this miss is not proof of absence` | before, a deep success reported the SHALLOW pass's numbers (describing a pass unrelated to the answer) and dropped the deep pass's deadline flag; a deep miss never mentioned the element cap at all. Change the Options element cap and re-run — the suffix must name the value you set, not a constant |
> ### 🟡 Z12 deep-MISS half PASS · Z13 NOT RUNNABLE on DumperTest — 2026-08-20 `[L8-Z12-Z13-2026-08-20]`
>
> **Z12, the deep-MISS caveat — PASS, including the part that could have been faked.** Instances →
> `Lookup` on a plainly-bogus `0x1234567800`, DumperTest / dist 3263:
> ```
> No UObject found at this address  [scanned (incl. deep descent) 25,179/25,179 in 202ms
>                                    — ⚠ the deep descent probes at most 256 element(s) …]
> ```
> Then **Options → "Deep container scan cap" 256 → 1024** and re-ran the identical lookup:
> ```
> …in 102ms — ⚠ the deep descent probes at most 1,024 el…
> ```
> ⭐ That second run is the real assertion. A hard-coded "256" would have read correctly on the first
> run and been indistinguishable from the fix; the suffix tracking the option to **1,024** shows it is
> reporting the cap actually used. The duration also moved (202 → 102 ms), so it re-scanned rather
> than re-rendering a cached line. The cap has been **restored to 256**.
>
> ### ✅ Z12's HIT path now has a FIXTURE SHORTLIST, mined offline 2026-08-21 `[Z12-MINE-2026-08-21]`
>
> The blocker was never the check — it was "which live object actually has the nested shape". That
> is answerable from the SDK export, which is the same walk the DLL does, already written down:
> **`py tools/verify/z12_mine_deep_containers.py`** → **428 triples over 277 owners** from
> DumperTest's 7,885 structs, in about a second, with no game running.
>
> Runtime-likely owners, in order — each is `owner.field` (a container) whose ELEMENT struct itself
> declares a container, which is exactly what forces the deep pass:
>
> | owner | container field | element struct's own container |
> |---|---|---|
> | `CollisionProfile` | `Profiles` / `EditProfiles` | `CollisionResponseTemplate::CustomResponses` / `CustomProfile::CustomResponses` |
> | `GameEngine` | `StatColorMappings` | `StatColorMapping::ColorMap` |
> | `InputMappingContext` | `Mappings` | `EnhancedActionKeyMapping::Triggers` |
> | `EnhancedPlayerInput` | `ActionInstanceData` | `InputActionInstance::Triggers` |
> | `World` | `LevelCollections` | `LevelCollection::Levels` (Set) |
> | `SkinnedMeshComponent` | `LODInfo` | `SkelMeshComponentLODInfo::HiddenMaterials` |
> | `PlayerCameraManager` | `PostProcessBlendCache` | `WeightedBlendables::Array` (depth 2) |
> | `PlayerController` | `ActiveForceFeedbackEffects` | `ActiveForceFeedbackEffect::ActiveDeviceProperties` (Set) |
>
> ⚠ **Depth matters more than expected: 155 of the 428 (36 %) are at nesting depth 2 or 3**, so a
> top-level-only scan would have missed over a third of the fixtures — including
> `PlayerCameraManager`, the one owner on this list that is guaranteed to exist in any running game.
>
> ⚠ **Three parsing traps are asserted by the tool, not trusted**, because each under-reports
> silently: 28.1 % of structs are BASE-LESS (a `^struct NAME ` pattern with a trailing space sees
> only 5,671 of 7,886); the header stores STRIPPED names (`Actor`, not `AActor`); and a TMap's KEY
> is a payload too. ⭐ The base-less control **failed on its first run for a fourth reason** — it was
> written `^struct NAME\s+`, and Python's `\s` matches the NEWLINE, so it matched base-less
> declarations too and discriminated nothing. A literal space fixes it. A control that cannot fail
> is not a control.
>
> ### ✅ Z12's DEEP-HIT HALF PASSES 2026-08-21 `[Z12-DEEPHIT-2026-08-21]` — the row is complete
>
> The blocker was staging an address that ONLY the deep descent can attribute. `tools/verify/`'s
> offline miner (0.2) named the shape and `z12_deep_hit.py` found it live:
> **`CollisionProfile::Profiles[0].CustomResponses`** — a `TArray<ResponseChannel>` living inside an
> element of a `TArray<CollisionResponseTemplate>`. Its buffer address is not a direct element of
> anything, so the shallow pass structurally cannot see it.
>
> **Phase A, over the pipe** (DumperTest dev, dist 3308):
>
> | | result |
> |---|---|
> | `container_depth=1` (NEGATIVE CONTROL) | **0 container matches** |
> | `container_depth=5` | **1 match**, `deep_scan=true`, `nested_chain` present, scanned 22,448/25,179 |
> | the log | ⭐ **`FindInContainersDeep: hit Default__CollisionProfile.Profiles (…, 2 hop(s) deep)`** |
>
> ⭐ **That `hit` line had never appeared in any log on this machine.** ⚠ But the recipe's phrasing —
> "`FindInContainersDeep` has never fired" — was **not right**, and the difference matters: it has
> RUN repeatedly (`maxDepth=5, maxElemProbe=256/1024`, DumperTest and DQ7R, 2026-08-20) and always
> found **0 matches**. It had never **HIT**. A rig watching for the wrong one of those two lines
> would have reported success on 2026-08-20.
>
> ⚠ **The negative control had to be keyed on `container_matches`, not on `found`.** For a heap
> buffer the OBJECT half of `find_by_address` answers `found=true, match_kind="nearest"` and names
> `Default__BlueprintExtension` at +0x60 — an object the buffer has nothing to do with — and it does
> so at **both** depths. Keying the control on `found` fails a correct run. Worth knowing generally:
> the "nearest" attribution is a heuristic and will confidently name an unrelated object for any
> heap address.
>
> **Phase B, in the UI** (the half no test and no pipe call can reach —
> `InstanceFinderViewModel.cs:647-651` passes `cs.DeepScan` and `anyContainerMatch`):
>
> | address | status suffix |
> |---|---|
> | `0x17979478230` — nested, deep-only | `[scanned (incl. deep descent) 20,400/25,179 in 102ms]` |
> | `0x1796E4A9D30` — a DIRECT element, shallow (CONTROL) | `[scanned 25,179/25,179 in 51ms]` |
>
> All three properties hold: the **deep phrasing** appears only for the deep case, the counters are
> the **deep pass's own** (20,400 of 25,179 — a partial, early-exit walk, not the shallow pass's
> completed 25,179), and **102 ms ≈ 51 + 51** is the SUMMED duration. Before Z12 a deep success
> reported the shallow pass's numbers. No element-cap caveat glyph appears, which is correct for a
> HIT. The panel also renders the nested path itself:
> `Default__CollisionProfile.Profiles[0].CustomResp… | CollisionProfile | StructProper… | 0x1796E492080`.
>
> ℹ️ The full status line is read from the UI's own log (`FindByAddress: '…' -> …`) because the
> panel's status text runs past the window edge at any width — the log carries it verbatim.
>
> 🟡 **Z12's HIT path renders correctly, but a DEEP hit is harder to stage than "pick a nested
> path" — retried on DQ7R 2026-08-20.** Two container addresses were taken straight out of a Deep
> Value Search and looked up:
>
> | address | container match reported | suffix |
> |---|---|---|
> | `0x27D8085BD90` | `Default__SoundWave.FrequenciesToAnalyze[0]`, inner `FloatProperty` | `[scanned 149,408/149,408 in 51ms]` |
> | `0x27D43FFEB30` | `Default__SplineComponent.SplineCurves.Position.P…`, inner `StructProperty` | `[scanned 149,408/149,408 in 51ms]` |
>
> Both are **hits**, both name the owning object and the `TArray` path, and in both the suffix is
> correct on its two conditional parts: **no "(incl. deep descent)"** (the deep pass never ran) and
> **no element-cap caveat** (right — the cap is only reported on a MISS, `anyContainerMatch` gating).
>
> ⭐ **The useful discovery: the SHALLOW pass already resolves a three-level path**
> (`SplineCurves` struct → `Position` struct → `Points[N]` array), so choosing a "nested" address is
> not enough to force the deep pass. A deep hit needs an address the shallow pass cannot reach at
> all — a container nested inside an **element** of another container. Neither DumperTest (no
> `SaveSlotList`-shaped fixture; `Tune` matches one plain float across 3,942 classes) nor DQ7R at the
> title screen produced one.
>
> ℹ️ **Z13 is not runnable on DumperTest** — no HP-named property to hover. Measured rather than
> assumed: Interesting Properties loaded **794 unique properties (threshold 4+: 530)** and filtering
> for `hp` returns only incidental substring hits — `MaxDepenetrationWithPawn`, `NavMeshProjection…`,
> `DynamicMeshProperties`, i.e. the "…hP…" inside *WithPawn* / *MeshProjection*. It was run on
> **Elliot** instead, below.

> ### ✅ Z13 PASS 2026-08-20 `[Z13-ELLIOT-2026-08-20]` — on a real RPG, with the contrast that proves it
>
> **The Adventures of Elliot** (proxy `dxgi.dll`, build 3263, 84,990 objects after Start Scan) —
> Interesting Properties → Load → filter `HP` → **2,356 unique properties (threshold 4+: 1,839)**, and
> unlike DumperTest it is full of genuine HP rows: `MaxHealthPoint` 20, `HPGaugeCount` 15,
> `HealthPoint` 15, `MinHealthPoint` 15, `bVisibilityHPGauge` 11, `m_IsShowHP` 10,
> `HPWidgetComponent` 10, `OnBelowHP` 9.
>
> Hovering the score of **`m_IsShowHP`** — a plain HP-token name — gives
> ```
> FinalScore=10 = keywords(1 hits) + classBonus=4 + structural=1
> ```
> ⭐ **`keywords(1 hits)`** is precisely the assertion. Before the fix `"HP"` and `"Hp"` both
> tokenised to `["hp"]`, so this row counted the same keyword twice and scored **15**.
>
> ⭐ **And the control that makes it meaningful:** `HealthPoint` on the same screen still reads
> **`keywords(2 hits)`** (`FinalScore=15 = keywords(2 hits) + classBonus+5 + …AttributeSet
> structural=1`). Two genuinely different keywords still count twice — the fix removed the *duplicate*
> without collapsing legitimate multi-keyword hits. A run that only showed `1 hits` somewhere could
> not tell those two apart.
>
> **Nothing went missing.** Every HP row above is still in the default (threshold-4) view; the lowest,
> `OnBelowHP`, sits at 9. The row's warning was about a threshold crossing, and none occurred here.
>
> 📌 Noted in passing, for `Z8`: this page's own header disclosed
> **`⚠ 3 of 87 keywords STOPPED at the 200-row cap (Max, Target, Time) — more matches exist`** — the
> per-keyword truncation disclosure working on a real title.
> | Z13 | on any game, open **Interesting Properties** and **Interesting Functions** and sort by Score; find an HP-named row and read its score tooltip | the tooltip reads `keywords(1 hits)` for a plain `HP`/`CurrentHP` name, not `keywords(2 hits)`, and that row scores **5 lower** than it did before | this is the one DELIBERATE score movement in the batch and it is not silent: `"HP"` and `"Hp"` both tokenised to `["hp"]`, so one keyword was counted twice. Nothing visible on HP alone becomes hidden (10 → 5, both thresholds ≤ 5), but an HP function on an `Anim*`/`Niagara*`/`Sound*`/`Particle*` class (−2 class penalty) goes 8 → 3 and correctly drops below the threshold. ⚠ **What to actually watch for: an HP row you EXPECTED that is now missing from the default view** — if one appears, it is a threshold crossing, and the fix is "Show all", not re-adding the duplicate |

### ✅ M1–M5 steps 2 / 4 / 5 PASS 2026-08-22 `[SOLIDEHOLD-2026-08-22]` — the 256 badge, the release, and the anti-zombie control

DumperTest (`dist` v1.0.0.3314), CE 7.7 attached alongside. Fixture found by measurement, not guess:
`ActorComponent::bIsEditorOnly` (BoolProperty, +0x8B, **+221 inheritors**) as the over-cap class and
`Actor::bIsEditorOnlyActor` (+0x5B) as the under-cap control — 519 component instances in the pool
against 58 actors, so one class crosses 256 and the other cannot.

**Step 4 — the cap badge, with its own control on screen at the same time:**

```
Forced fields:  [Clear all]
✕ ActorComponent · bIsEditorOnly      (256 held)  ⚠ capped
✕ Actor          · bIsEditorOnlyActor  (58 held)
```

Both rows visible together is the point — the badge is not merely present, it is **absent where it
should be**.

⚠ **The status line's clause could NOT be read on screen** — see `[FORCESTATUSCLIP-2026-08-22]`
below. The `⚠ capped` badge and that clause are both driven by `r.Truncated`, so the information
reaches the user, but the sentence the row names does not.

**Step 5 — release, verified as a CROSSOVER rather than a re-read:**

| row | before release | after releasing only the ActorComponent row |
|---|---|---|
| `ActorComponent::bIsEditorOnly` | `true` | **`false`** — restored |
| `Actor::bIsEditorOnlyActor` | `false` → `true` (held) | **still `true`** — untouched |

Both previews come from a fresh Property Search, i.e. a live re-read down a different path than the
one that wrote them. ⭐ The two rows **swapping in opposite directions in one refresh** is what rules
out a global stale-cache artefact: a re-read bug cannot flip one row down and hold the other up.

**Step 2 — hold survives a disconnect, AND is still writing.** The row is explicit that the listing
alone is not a pass ("殭屍 job 會照樣列出來但已經停止 re-assert"), so the witness is a **drift test
run from CE while the UI was disconnected**, on a real instance (`ChaosDebugDrawActor`
@ `0x1565475F240`, +0x5B):

```
UI DISCONNECTED
  before          = 0x23
  wrote 0      -> = 0x00
  +1.5s           = 0x02     <- the worker put ITS bit back
  +3.0s           = 0x02
```

⭐ Three things fall out of one measurement: the worker is alive with no UI attached; the mask is
`0x02`; and **only that bit came back** — `0x01` and `0x20` stayed clear, so Solide re-asserts its
own field and not the byte around it. Reconnecting then showed the strip still listing
`✕ Actor · bIsEditorOnlyActor (58 held)`, read back from the DLL by `get_forced_fields` over a
brand-new connection.

**And the negative control for that detector**, run immediately after releasing the hold:

```
AFTER RELEASE
  byte now        = 0x00      <- best-effort base restore put it back to false
  wrote 0, +2.0s  = 0x00      <- NOT re-asserted; the hold really stopped
  restored to     = 0x23
```

Same probe, same address, opposite answer. Without it, "0x02 came back" is only evidence that
*something* writes there.


> ### ⭐ MOVED HERE FROM THE 繁中 CHECKLIST 2026-08-24 — arm (a) re-scoped, and it is NOT human-only
>
> The 繁中 file's *M1–M5 步驟 1* section was deleted 2026-08-24 because arm (b) is closed exactly as
> written and arm (a) fails that file's one rule: **a row belongs there only if Auto + Computer Use
> cannot finish it.**
>
> ⚠ **But arm (a) is not the arm that closed.** `[SEETHRU-ARMS-AB-2026-08-23]` ran
> `tools/verify/seethrough_arm_a.py`, which tests *"moving RESTORES a hidden actor"*. The 繁中 arm (a)
> was *"close the game **while** moving"* — a different question, and still open.
>
> ▶ **It needs one rig that concatenates two that already work**: the movement loop
> (`seethrough_arm_a.py:97-99`, `teleport_relative` over the same pipe connection that owns the
> See-through session) and the posted close (`seethrough_arm_b.py:147-148`). No person required.
> Recorded here rather than dropped with the section.
ℹ️ ✅ **Step 1 (See-through's four disable arms) is CLOSED 2026-08-23** — arms (c)+(d) passed
2026-08-22, arms **(a)** and **(b)** on 2026-08-23, see `[SEETHRU-ARMS-AB-2026-08-23]` below.
✅ **Step 3 (close the game with a Solide hold live) is CLOSED 2026-08-23
`[SOLIDEHOLD-STEP3-2026-08-23]`** — DumperTest dev / DLL 3337: `force_field(numeric, 7777)` on
`DumperTestHolder::HolderValue` reported **held=80** with **8/8** sampled instances actually
reading 7777 (so the hold was demonstrably re-asserting, not merely registered), then a posted
`WM_CLOSE` exited the process in **1.5 s** with **0** `tick threw` / `[ERROR]` lines and **0**
crash dumps.
⚠ Same rule as arm (b): a `taskkill /F` does not test this — the DLL's shutdown path never runs.

⭐⭐ **M1–M5 is now complete: steps 1, 2, 3, 4 and 5 all closed.**

### 🟡 AC13 step 4 — PARTIALLY CLOSED 2026-08-23 `[AC13-STEP4-2026-08-23]`: option (2) was attempted, one half landed, and the other hit a wall worth writing down

**`PipeTransportStats` is no longer untested.** `ui/UE5DumpUI.Tests/PipeTransportStatsPlacementTests.cs`
adds two deterministic tests, **both negative-controlled against a deliberately broken build**:

| test | what it pins | control run |
|---|---|---|
| `ARefusalWithNothingSent_IsNotCountedAsTransport` | the not-connected guard sits **above** the timer, so a refusal that sent nothing logs no 0 ms sample | moved the timer above the guard → **only this test failed** |
| `Snapshot_IsMonotonicAndConvertsTicksToMilliseconds` | the accumulator never goes backwards, and ticks→ms is right (record exactly `Frequency/100`, expect 10 ms) | changed the factor 1000.0 → 500.0 → **only this test failed** |

Both controls reverted to an empty `git diff` and a green 2/2. **Zero production change** — the final
shape needs none.

⛔ **The positive half — "a request that dies in the write is still counted" — is still untested, and
the route was tried and abandoned. Recorded so nobody re-spends it.**

`SendAsync` refuses before the timer unless `IsConnected` is true and `_writer` is live, so the test
needs a real connected pipe. Two walls, in order:

1. **`Constants.PipeName` is a hardcoded `const`.** A test server would bind the name a running
   game's DLL also serves, and named pipes allow several server instances per name — so the test's
   client can reach the DLL, or the UI's client can reach the test. That is exactly the hazard behind
   CLAUDE.md's *"never run `pipe_client.py` while the UI is connected"*. An optional `pipeName` ctor
   parameter was prototyped, and **reverted** with the test rather than left in production to justify
   something that no longer exists.
2. ⭐ **With the name injectable, `PipeClient.ConnectAsync` reproducibly never completes against an
   in-process `NamedPipeServerStream`** — while a raw `NamedPipeClientStream` built with *identical*
   arguments connects in **0.15 s**. Measured across four variations, all timing out at the harness
   bound while the server's own `WaitForConnectionAsync` reported **completed**:
   `maxNumberOfServerInstances` **1** and **4**, and **on** and **off** the xUnit synchronization
   context (`Task.Run`). The dialled name was confirmed correct by capturing the client's own
   `Connecting to pipe: …` log line, and a `PipeClient` pointed at a name nobody serves throws at its
   5 s timeout as designed — so the injection worked and the timeout works; it is the
   live-server case that stalls. **Not diagnosed. It is not an AC13 defect** and cost more than it
   was worth.

⚠ **The first draft of these tests hung with no message** — an unbounded `await` in a helper — and
that is what turned a 10-minute suite into a killed test host reporting `4708 succeeded / error: 1`
with **zero failures listed**. Bounding every await (working-lessons §2.7, *a hang is not a test
result*) turned it into a 10 s failure naming the exact line, which is the only reason the wall above
could be characterised at all.

**Still open, unchanged:** the recommendation in the block above — surface the transport figure where
a disconnect cannot destroy the observable — remains the honest way to make the live row runnable.



### 🟡 G11 steps 3–4 ANSWERED 2026-08-22 `[G11-TIERS-2026-08-22]` — Tier 2 has never fired here, and Tier 3 has no subject

Both steps are greps, and both come back empty. What makes the emptiness *evidence* rather than a
non-result is the survey underneath it — every `DetectVersion` line across **every game's archived
scan log on this machine**:

| ladder line | occurrences |
|---|---|
| `Attempting to detect UE version...` | **66** |
| `PE VERSIONINFO -> UE 5.x` (happy path) | 26 |
| `PE VERSIONINFO … — unrecognised` | 33 |
| `PE resource failed, falling back to memory string scan` | 36 |
| `Tier 1 (ascii) '++UE4+Release-4.18' -> 418` | 3 |
| `Tier 1 (utf16) '++UE4+Release-4.27' -> 427` | 3 |
| **`Tier 2 Release prefix -> NNN`** | **0** |
| **Tier 3 / low confidence** | **0** |

⭐ **The channel is proven**: the ladder ran 66 times over **23 distinct hosts**, fell through to the
memory scan 36 times, and reached **Tier 1 on three separate games** — `DQ7R` and
`DQIandIIHD2DRemake` (utf16, 4.27) and `Octopath_Traveler` (ascii, 4.18). So "no Tier 2 line" is not
"nobody looked".

**Step 3 — answered: Tier 2 has never been reached on this corpus.** Nothing to record about its
accuracy, and the useful finding is the inverse of what the step expected: **the Tier 2 rung is
un-exercised in the field**, so nothing here can say whether it reports the right version.

**Step 4 — there is no subject.** It says "re-run a game that previously reported Tier 3 (low
confidence)". **Zero** Tier 3 lines exist, so no such game has ever been seen here. The step cannot
be run rather than has not been.

ℹ️ This also **corroborates `G2` step 2's blocker independently**. That row's `tier1_host_survey.py`
sweep concluded the only Tier-1-capable hosts installed are UE4; the log corpus agrees from the
other direction — all three games that actually produced a Tier 1 line are UE4 (4.18 / 4.27), so the
**UE5 branch still has no host**, measured twice by different means.

⚠ DumperTest cannot supply any of this: its version is **cached** (`FindAll: UE Version = 504
(cached, rev=5, detected=yes, lowConf=no) — skipped DetectVersion`), so the ladder does not run at
all, and it has intact VERSIONINFO so it would stop at the first rung anyway.

**What steps 1–2 still need**: a game not yet recorded (step 1 asks for at least one more beyond
Elliot and DragonSword Awakening) and an Avowed injection (step 2). Neither is a grep.



### ✅ AF25 (AC15/AE27/AF25 step 3) PASSES 2026-08-22 `[AF25-OPCODE-2026-08-22]` — teleport opcode still 8, now from the shared constant

Three halves, and the live one was already done earlier today:

1. **The script runs.** The MB3 batch (`[MB3-CT-2026-08-22]`) ticked real `.CT` teleport records
   through CE — `Save marker 1`, `TP facing direction`, `Recall marker 1` — with the pawn's pose as
   the witness (900 → 1000 → back to 900.000/1110.000/92.013 exactly). That is step 3's "實際跑一次".
2. **The content is byte-pinned.** `TeleportScriptGeneratorTests` asserts the emitted literal
   `writeInteger(mb + 0x00, 8)` — plus the per-action op — for Save / Recall / RecallLast / BugIt /
   BugItGo / GetPov / GetPose / ClearAll.
3. **The opcode comes from the shared constant**: `CeMailboxLayout.CmdTeleport = 8`, used at all
   three emit sites in `TeleportScriptGenerator` (the per-generator `private const int CmdTeleport`
   copies are gone).

⭐ **I suspected an unguarded hand-copy and the measurement refuted it.** `check_mailbox_contract.py`
only verifies `CeMailboxLayout.ContractVersion`, so on reading it looked as though a drifted C#
opcode would pass everything. Negative control — set `CmdTeleport = 9` while the DLL still says 8:

| | result |
|---|---|
| `check_mailbox_contract.py` | **CHECK OK** — it does not compare opcode values |
| the test suite | **6 failures**, all in `TeleportScriptGeneratorTests` |

Restored byte-exact. So both directions are covered, just by different mechanisms: a **C#-side**
drift is caught by the tests, and a **DLL-side** change moves the contract surface hash (`Cmd` is in
`CONTRACT_ENUMS`) and forces a bump. ⚠ Worth writing down because the natural conclusion from
reading the gate alone is the opposite one.

ℹ️ Steps 1 (Proxy Deploy: Steam scan vs drive scan must find identical games) and 2 (Game Class
Filter → Package column) are still open. ⭐ Step 1 needs **no game** — it is a UI-only row sitting in
the 第 2 步 bucket, so it can be run in any session that has the UI up.


### 🟡 第 3 步 CE batch — opened 2026-08-22 `[STEP3-BATCH-2026-08-22]`, three rows re-scoped before a single CE click

> **Where the batch stands at the end of 2026-08-22.** ✅ `MB3` **CLOSED** the same day (`[MB3-CT-2026-08-22]` — Save / TP-facing-dir / Recall plus a baked Invoke, all through real `.CT` records, with the pawn's pose as the witness). ✅ `AA12/AA13` and `Y10/Y13` also closed. ⛔ `.CT DLL discovery` is still blocked exactly as written below. ✅ `U16` **CLOSED 2026-08-22** (`[U16-ENUM65-2026-08-22]`). 🟡 `B18` and the `M1–M5` remainder are unchanged. The triage below is kept because its *reasoning* is what saved the CE session, not because every verdict is still current — check the per-row entries above before acting on any line of it.

Before setting up Cheat Engine, each of the eight rows was checked for what it *actually* still
needs. Three changed shape, and one is blocked by an item already on the index.

**⛔ `.CT DLL discovery` is BLOCKED by `[STALEDLL-2026-08-18]`(a), and the two rows were never linked.**
Its own step says *"first make sure there is no `UE5Dumper.dll` under CE's install folder, or the
cheaper slot answers first"*. Measured 2026-08-22: `C:\Program Files\Cheat Engine\UE5Dumper.dll`
is **still there — 536,064 bytes, dated 19 Feb 2026**. So the discovery step would report CE's own
folder, which is precisely the FAIL its warning describes. Deleting it needs elevation
(`%ProgramFiles%`), which is the maintainer-only half of STALEDLL. **Running this row before that
file is gone can only produce a false negative.**

**✅ `MB3` — CLOSED 2026-08-22, see `[MB3-CT-2026-08-22]`.** *(The paragraph below is the pre-run triage that scoped it; it was right — plain dispatch was good, and the CE run confirmed rather than discovered.)* Only step 1's CE half was left, and it could no longer fail silently. Steps 2 and 3 were
closed on 2026-08-19 by `[MB3-POKE-2026-08-19]` (50 consecutive dispatches through
`tools/verify/mailbox_poke.py`, zero failures, no `Mailbox: tick threw`, no `result=-11`). Step 4 —
the throw path — has no way to be triggered on demand and is a standing watch, not a runnable step.
What remains is two real `.CT` rows through CE, which is worth doing but is now a confirmation
rather than a discovery: plain dispatch is known good.

**✅ `U16` — CLOSED 2026-08-22 on DQ7R; see `[U16-ENUM65-2026-08-22]`.** ⚠ The paragraph below is kept as the reasoning of the day, but **its headline number is now known wrong** — DumperTest's ceiling is **113**, not 26, and 26 was an artefact of grepping one run's `walk-0.log`. Original text:


Walked `PhysicalMaterial` and the surrounding classes on DumperTest and grepped `walk-0.log`:

```
ResolveEnumValue lines            437
read N of M with N != M             0
GetEnumEntries: ... truncated read  0
largest table observed          26 of 26
```

So the correctness half is satisfied over 437 resolutions. The row's own caveat — *"the largest
table measured is only 26 members, so the 'large' half has not really been pressed"* — is confirmed
by measurement: **26 is the ceiling on this host**, and `PhysicalMaterial::SurfaceType` (the
`EPhysicalSurface` byte property, which in a project that defines them runs to 63 entries) does not
reach it here because a stock project defines only a handful.

▶ A cheap screen for anyone with another title injected: walk a few dozen instances, then
`grep -o 'read [0-9]* of [0-9]*' walk-0.log | sort -t' ' -k4 -n | tail -1`. A host whose ceiling is
still in the twenties cannot press this row either — and knowing that costs one command instead of
a CE session.


### 🟡 Genau RIP decode — GNames CLOSED 2026-08-22 `[GENAURIP-RECOVERY-2026-08-22]`, GObjects shown UNCLOSABLE on this host

Follows `[GENAURIP-AB-2026-08-19]`, which measured the win on notepad++ but could satisfy the
acceptance criterion for **GWorld only**: on a non-UE host GObjects and GNames never resolve, so
"the address did not move" was vacuous for two of the three the row names. A *game* could not close
it either — all five call sites are RECOVERY paths, and on a healthy title the AOB wins on the first
pattern so **none of them runs**. That was the deadlock.

⭐ **THE WAY OUT WAS THIS PROJECT'S OWN PRECEDENT.** The `PEHOOK` rows staged their TABLE arm by
temporarily removing signatures so DumperTest mis-detects. Same trick, two lines: force
`ScanForTarget`'s result to 0 in `FindGObjects` and `FindGNames`, and the recovery paths run on a
real UE host that actually *has* a GObjects and a GNames to find.
Rig: `tools/verify/genau_rip_recovery_ab.py`. Three DLLs from one tree in one session — `post` (AOB
forced off), `pre` (+ predicate reverted), and `dist`'s untouched DLL for the AOB baseline.

**✅ What closed.**

| | pre | post | AOB baseline |
|---|---|---|---|
| `GNames` | `0x7FF75A0568C0` | `0x7FF75A0568C0` | `0x7FF75A0568C0` |
| `GWorld` | `0x7FF75A3488A0` | `0x7FF75A3488A0` | `0x7FF75A3488A0` |

Both recovery paths **verifiably ran** on both sides (the fallback log lines are asserted, not
assumed), the module did **not** rebase (`code_base = 0x7FF74A311000` on both staged runs — checked,
because raw addresses are otherwise meaningless), and GNames is now byte-identical *and* agrees with
what the AOB finds. **That is the row's criterion, closed for GNames.**

**⭐ And the WIN is three orders of magnitude clearer than on notepad++.**

| host | candidates, pre → post | gap |
|---|---|---|
| notepad++ (2026-08-19) | 4,085 → 4,083 | −2 |
| **DumperTest (staged)** | **508,10x → 506,59x** | **≈ −1,510** |

Four independent runs per side: gaps **1511 / 1516 / 1513 / 1509**, against a measured run-to-run
variance of **±5** (live `.data` contents move the absolute count, not the gap). A second,
independent expression of the same win fell out of a bug in my own rig: **the pre side takes longer
to become ready — 15 s vs 9 s** — because it hands the scan ~1,500 more candidates and then
validates a bogus pool.

⚠⚠ **GOBJECTS IS NOT CLOSABLE THIS WAY, AND FINDING THAT OUT IS THE MOST USEFUL PART.**
Forced onto the data-scan fallback, DumperTest's `ValidateGObjects` accepts a **false positive**:

```
UE5_Init: Complete (UE504, GObjects=0x25339EB4E48, ..., Objects=2556928)   <- real count is 25,179
UE5_Init: Complete (UE504, GObjects=0x1D8663E4D70, ..., Objects=583)
```

and the answer is a **heap** address, so it moves every launch regardless. Which false positive wins
depends on live heap contents — measured: the **post** side picked instruction `0x7FF74B0BC264` on
all three runs, the **pre** side picked `0x7FF74B0CAC34` once and `0x7FF74B0BC3E4` twice. So the two
sides differ for a reason that is **not a regression**, and asserting on it would be reading noise
as signal. The rig therefore **reports GObjects and refuses to assert on it**.

⭐ **The lesson, and it generalises: STAGING A PATH MAKES IT RUN; IT DOES NOT MAKE IT MEANINGFUL.**
The staging was still worth doing — it closed GNames and produced a 750× better measurement of the
win — but "the code under test executed" and "the comparison means something" are two conditions,
and the second one has to be checked separately.

▶ **What would close GObjects**: a UE title whose GObjects AOB genuinely fails *and* whose data
section yields the real pool. The rig runs against any host — point it at one.

ℹ️ **Observed, filed here rather than as its own row because it is fallback-only and staged**:
`ValidateGObjects` accepted a pool with 583 objects and one with 2,556,928 on a host whose real
count is 25,179, and the DLL then reported `UE5_Init: Complete` with no warning. The path is only
reachable when *every* GObjects AOB pattern fails — an exotic or forked engine, which
[reversing-nonstandard-ue-games.md](reversing-nonstandard-ue-games.md) already treats as needing
bespoke work — and `s_gobjectsMethod` does record `data_scan`, so it is not wholly silent. But
"the fallback answered" is not the same as "the fallback answered correctly", and nothing downstream
distinguishes them.


### ✅ FOUND + FIXED 2026-08-22 `[PARAMSSORT-2026-08-22]` — three "Params" columns sorted the LABEL, and the audit that fixed the fourth could not see them

⭐ **The most useful thing here is WHY they survived**, and it is a reusable trap: **the sweep asked
the wrong question, so a column that worked perfectly and sorted wrongly passed.**

Audit #5 **AF20** asked *"is this header inert under trimming?"* — Avalonia resolves
`SortMemberPath` by reflection, and under Native-AOT that metadata survives only for a property some
compiled binding roots. Live Walker's "Params" gets its text from an element-syntax `<MultiBinding>`,
which roots nothing, so its header was **dead** in the shipped binary. It was found and fixed.

The three siblings — `ConsolePanel.axaml:226`, `InterestingFunctionsPanel.axaml:164`,
`LiveFuncsPanel.axaml:178` — are plain `DataGridTextColumn`s that **bind `ParamsLabel` and sort
`ParamsLabel`**. Binding and sort path agree, so the property is rooted, the header works, nothing is
inert. **They passed.** And `ParamsLabel` is `$"{NumParms} ({ParmsSize}B)"`, so they sorted the
string: `"11 (72B)"` ranks above `"2 (9B)"` because `'1' < '2'`.

`LiveFuncsPanel.axaml.cs`'s own comment said so out loud —
*"Class / Function / **Params** bind and sort on the same path, so they are rooted and need
nothing"* — which was **true, and beside the point**. ⭐ **Rooted is not the same as correct.**

**Reachable on a stock host, measured** (DumperTest, `list_all_functions`, 2026-08-22): 3,142
functions; the `NumParms` histogram is `0:238 1:964 2:1093 3:541 4:152 5:77 6:60 7:7 8:6 9:2 11:1
19:1`. Two functions have ≥10 parameters, so a real inverting pair exists — `"11 (72B)"` vs
`"2 (9B)"`. It needs no exotic game.

**The fix**: all three now declare `SortMemberPath="NumParms"` with a
`DataGridSortComparers.Number<T>` wired in the panel's code-behind, matching the Live Walker twin.
`ConsolePanel` had no comparer table at all and got one. `CanUserSort="True"` was added alongside —
belt-and-braces, not the fix: `CanUserSort` is what gates whether the click does anything, and the
2026-08-21 AOT run showed Avalonia's reflection sortability probe resolving in the shipped binary for
comparer-wired columns that omit it. **Do not read the attribute as load-bearing.**

**THE DURABLE PART IS THE GUARD, because the existing one structurally could not see this.**
`DataGridSortWiringTests.Every_user_sortable_XAML_column_is_binding_rooted_or_has_a_comparer` asks
"is a sort path rooted or wired?" — the AOT question. A new sibling in the same file,
`No_column_sorts_on_a_label_that_formats_a_number`, asks the correctness one: **no column may sort
on a computed `string` property whose expression interpolates a numeric member declared in the same
model file.**

⚠ **Its known imprecision is stated in the test rather than hidden.** Markup does not name the
grid's item type, so labels are matched by property NAME across all of `Models/`, and two models can
share one. `Display` is `$"{ClassName}  ({InstanceCount:N0})"` in `PivotModels` (numeric) and
`"Name : ClassName"` in `RelatedObject` (purely textual, ordinal is *correct*). Collisions go in an
exemption set **with the reason**, and **an exemption that stops being matched fails the test**, so
they cannot rot.

⭐ **The guard immediately found two sites I had not predicted**, and checking them is what the
exemption mechanism is for: Live Walker's and Instance Finder's "Value" column sorts
`LiveFieldValue.DisplayValue`, which is a heterogeneous fallback chain (FDateTime decode →
TypedValue → `"Name (Class)"` → `"{StructType}"` → array/map/set counts → DataTable row count → raw
hex). Only some branches carry a number and they are not the same number, so **no numeric key
exists** and `Ordinal` is wired deliberately. Exempted with that reason — a real judgement, not a
rubber stamp.

#### Two more, found by the same pass, both surviving adversarial refutation

⭐ **12 findings went to refuters; 4 survived (33%).** That ratio is the point of running them — the
eight that died included several that read convincingly. The three below are the survivors that were
actionable (the fourth is this entry's own subject).

**(a) `ObjectInstancePickerDialog` sorted addresses as TEXT while its sibling sorted them as
numbers.** `DataGridSortComparers.Hex<T>(ulong)` exists and had exactly **one** user in the whole
tree — `RelatedObjectsPanel.axaml.cs:22`, `Hex<RelatedObject>(r => r.AddressValue)`. The Invoke
param-picker's identically-named "Address" column used `Ordinal<InstanceResult>(r => r.Address)` on
the `"0x…"` string. Two panels, one column name, two answers, and one of them documented.

⚠ **It does NOT misbehave on this host and I am not going to pretend otherwise.** Equal-length
UPPERCASE hex compares identically ordinally and numerically; measured on DumperTest 2026-08-22, all
**137** `Object` instances are 13 characters. That is a property of this heap's layout, not evidence
the comparer is right — one 12-character address in the set (a static `FUObjectArray`, a `0x7FF…`
module-resident object) and the first character decides the order. Fixed anyway: `InstanceResult`
gained the same `ulong AddressValue` accessor `RelatedObject` already had, four lines, zero risk.

▶ A third guard, `No_address_column_is_sorted_with_a_string_comparer`, now refuses an `Ordinal`
comparer on any key named like an address. Negative control run: reverting the one line fails it by
name. Same anti-vacuity assertion as the others (43 comparer wirings across `Views/*.cs`; a scan
finding fewer than 30 fails rather than passing everything).

**(b) The snapshot cap notice was rendered in a `TextBlock` with neither `TextWrapping` nor a
`ToolTip`** (`SnapshotPanel.axaml:362`), inside a `WrapPanel`, while the live panel gives the
**identical shared string** both (`ValueSearchPanel.axaml:52-59`; both call
`PartialResultNotice.PerSlotWitnessCap`). The sentence is ~230 characters and the window's
`MinWidth` is 800 DIP. ⭐ **So the sentence whose entire job is to tell you results were truncated
was itself being truncated** — and it is the notice `[AF12/AF13]` step 3 is written to look for.
`ToolTip.Tip` is the half that certainly works; `TextWrapping` needs a bounded width inside a
`WrapPanel`, hence the `MaxWidth`. ⚠ **The wrap half is owed a visual check** and is noted as such in
the markup rather than claimed. — ✅ **DONE 2026-08-24, see `[PARAMSSORT-B-WRAP-2026-08-24]` below.**

> ### ✅ (b)'s WRAP HALF CLOSED 2026-08-24 `[PARAMSSORT-B-WRAP-2026-08-24]` — checked at the exact width the defect names
>
> The owed check was *"`TextWrapping` needs a bounded width inside a `WrapPanel`, hence the
> `MaxWidth` … visual confirmation is owed"*. Done on the AOT build (`dist/UE5DumpUI.exe`
> v1.0.0.3338), DumperTest **Development**, at the window's **true minimum width**.
>
> Both halves work, at 812 DIP:
>
> ```
> 423 object(s) matched · scanned 652 ⚠ a slot matched more than 256 fields — only that many were kept, so "All fields" is a
> page and a later Changed/Decreased refine can re-read only what was kept; use more distinctive values.
> ```
> * **wrap** — two lines, ends on its own full stop, **no ellipsis and nothing clipped** ✅
> * **tooltip** — hovering the notice shows the identical sentence in full ✅
> * bounded by construction too: `MaxWidth="760"` (`SnapshotPanel.axaml:371`) against the window's
>   `MinWidth="800"` (`MainWindow.axaml:10`), so the text can never be wider than the panel.
>
> ⚠⚠ **THE MEASUREMENT ALMOST DIDN'T MEAN ANYTHING, AND THE REASON GENERALISES TO EVERY FUTURE
> COMPUTER-USE RESIZE.** This monitor runs at **DPI 216 (225%)** — `GetDpiForWindow` says so. A
> `SetWindowPos` issued from a **DPI-unaware** process is *virtualised*: asking for 820 px returned a
> `GetWindowRect` of exactly `820 x 950`, which looked like a successful narrow resize and was not —
> the window was really ~1828 physical px. Calling **`SetProcessDPIAware()` first** is what made the
> numbers real, and the window then refuses to go below **1828 px = 812 DIP**, i.e. its
> `MinWidth="800"` plus non-client border. ▶ **Without the DPI call the run would have "verified" the
> wrap at a width nearly 2.2× wider than the one the defect is about** — a clipped-text check at the
> wrong width is precisely the vacuous pass this row existed to avoid.
>
> ⭐ **The fixture question was answered BEFORE the click, and it changed the plan.**
> `tools/verify/snapshot_cap_fixture.py` exists because *"an absence proves nothing until the channel
> is shown able to carry the thing"*, and here it earned that:
>
> | corpus | verdict |
> |---|---|
> | DumperTest **Shipping** (`0CAB57A7081C3000`, fresh capture: 628 obj / 11,344 fields) | ⛔ **largest group 125** — the notice CANNOT fire; a no-notice run here measures nothing |
> | DumperTest **Development** (`6A8AA8DF10F1F000`, fresh capture: 652 obj / 12,297 fields) | ✅ **264 fields at 0.0** on `TraceQueryTestResults` (gidx 22761) — above 256, below 1024, inside the discriminating window |
>
> So the run was moved to Development on the rig's say-so, not on a guess. A Group match on
> `0` + `0` then produced the notice on the first attempt.
>
> ⚠ **The archived corpus is stale and this is worth knowing before the next attempt.**
> `snapshots.6A7EA60310F17000.db` — the one the rig's 2026-08-21 note measured — belongs to a
> DumperTest binary that has since been **rebuilt**: Shipping now hashes `0CAB57A7081C3000` and
> Development `6A8AA8DF10F1F000`, so **neither current flavour loads it** and the Snapshot panel
> opens empty. A fresh capture is required, and Development reproduces the same 264-field group, so
> nothing is lost — but "the corpus is on disk" is not the same as "the corpus matches the game".
>
> ℹ️ The Shipping capture made while establishing this was deleted through the panel's **Delete
> Selected** (`Snapshot deleted.`, list empty afterwards), so the app-data folder is as it was found.

**(c) The checklist named a column header that does not exist.** Step 1 says to click Detect Stats'
**✓** header. `DetectStatsPanel.axaml:59` binds `str.Detect.ColConfirm`, which `en.axaml:47` defines
as **`Result`**; ✓ is *cell* content (`DetectedStat.cs:63-64`), and a Detect run with no confirmed
rows shows no ✓ anywhere at all. Corrected in the 繁中 file. ⚠ Small, but this is precisely the class
of thing that makes a re-runner either file "no fixture" or click a plausible-looking neighbour and
record a pass for the wrong column — the failure mode this whole re-run exists to avoid.

**Shown able to fail, twice** — both controls run and reverted:

| control | result |
|---|---|
| revert `LiveFuncsPanel` to `SortMemberPath="ParamsLabel"` | ❌ fails, naming file + header + path + both declaring models |
| break the label scan so it matches nothing | ❌ fails on `only 0 numeric-composite label(s) found` — **it refuses to pass vacuously** |

That second control is the one worth keeping. Without it a regex that quietly stopped matching would
turn the whole guard into a no-op that reports green forever.

**4,639 / 4,639 tests pass.**

⚠ **In-game click-through is still owed** and is genuinely D3: that the header is clickable in the
*trimmed* binary, that the second click reverses, and that no two rows show the same record after
sorting. A JIT test host cannot trim itself, which is the whole reason the AOT class of defect
exists. Added to `## Pending live-game verification`.


### 🟡 DOWNGRADED 2026-08-22 `[CADENCEBAND-2026-08-22]` — the "periodic timer" band assumes ≥25 FPS, and the only witness is our own harness

*Priority **low**, and **possibly not worth fixing at all** — read the refuted hypothesis before
picking it up. Effort **S** for the constant, **M** for the honest fix. Found as the residue of
`[CADENCEGAP-2026-08-22]` and deliberately filed apart from it.*

`Fern.cpp:4163` classifies a function as a periodic timer with

```cpp
if (snap[i].gapSamples >= 3 && snap[i].cv <= 0.25 &&
    snap[i].meanPeriodMs > 40.0 && snap[i].meanPeriodMs <= 30000.0)
```

`> 40.0` is meant to exclude per-frame (Tick) callbacks — its own comment says "out of the per-frame
(Tick) band". **40 ms is 25 FPS**, so the implication it encodes is *"period > 40 ms ⇒ not a plain
Tick"*, and that is false below the crossover:

| frame cap | frame period | plain Tick |
|---|---|---|
| 120 / 60 / 30 FPS | 8.33 / 16.67 / 33.33 ms | correctly excluded |
| **25 FPS** | **40.00 ms** | the exact crossover |
| 24 / 20 / 15 / 10 FPS | 41.7 / 50 / 66.7 / 100 ms | **every plain Tick is badged a TIMER** |

**Measured**, DumperTest, after the gap fix so the two effects are separate:

| | periodic-looking |
|---|---|
| `t.MaxFPS 60` | **0 of 6** ✅ |
| `t.MaxFPS 15` | **4 of 6** ❌ — the four once-per-frame animation callbacks |

⚠⚠ **THE ONLY WITNESS IS OUR OWN TEST HARNESS, AND THAT IS THE HEADLINE.**
`tools/verify/launch_dumpertest.py:38` caps DumperTest at `t.MaxFPS 15` as a deliberate house
setting (commit `9e141ec2`, so that no row can quietly launch unbounded and skew a timing
measurement). Every Live Funcs profile ever taken against DumperTest has therefore run at 15 FPS —
which is why the `6 periodic-looking` log line has been sitting there unremarked. **No real game has
been observed hitting this.**

⭐ **REFUTED HYPOTHESIS — do not re-form it.** The obvious realistic scenario is that you profile a
game while it is BACKGROUNDED (you are looking at our UI, not at the game), and UE throttles an
unfocused game below 25 FPS. Tested 2026-08-22 on DumperTest at `t.MaxFPS 60`, 15 s foreground vs
15 s minimised:

```
foreground   total_calls 7205   periods 8.33 / 16.67 ms   0 of 6 flagged
minimised    total_calls 7200   periods 8.33 / 16.67 ms   0 of 6 flagged
```

**It does not throttle at all** — 7205 vs 7200 calls is the same frame rate. The scenario does not
reproduce. ⚠ One sample on one machine: other titles do throttle when unfocused, so this refutes the
hypothesis *here*, not everywhere. But it is now evidence rather than a guess, and the guess was
mine.

⚠ **A `TickInterval`-throttled actor tick is NOT this defect.** It is frame-driven with an arbitrary
period and would be flagged — but it genuinely *is* periodic behaviour, and "find the callback
driving a cooldown/respawn" is exactly what the badge is for. Do not fold it in.

▶ **If it is fixed, not with a bigger constant** — any constant is wrong for some frame rate. The
band has to be relative to the *observed* frame period, which the profiler can estimate from its own
data. ⚠ **The obvious estimator is wrong**: the *minimum* observed period is not the frame period —
at 60 FPS the minimum was **8.33 ms**, because `CameraModifier::BlueprintModify*` fires twice per
frame. The **mode** of the period distribution is the frame period (4 of 6 at 16.67 ms; 4 of 6 at
66.66 ms in the 15 FPS run), and on a real game with hundreds of dispatched functions it would be
far more robust than on DumperTest's six. That choice, plus what to do when the window holds too few
functions to estimate from, and whether to publish the estimate so the UI can say "frame ≈ 16.7 ms",
is the design decision this is filed for.

ℹ️ **Clock precision, since it comes up**: the cadence timestamps are
`std::chrono::steady_clock` truncated to milliseconds (`Stark.cpp:103-107`), i.e. QPC-backed and
**not** the ~15.6 ms (1/64 s) Windows timer tick — `Fern.cpp:1157` already picks QPC over
`GetTickCount64` for that reason. Proven from the data rather than from what the STL ought to do:
were gaps quantised to 15.6 ms ticks, a 16.67 ms period would alternate 15.6 / 31.2 and read
**cv ≈ 0.24**; measured **cv = 0.028**, eight times smaller. What the residual 0.467 ms σ *is* is the
millisecond truncation itself — the difference of two independently-truncated stamps has
σ = √(2/12) = **0.408 ms**, so essentially all the observed jitter is the truncation floor and only
~0.01 of the cv is real frame-pacing noise.

That floor puts a lower bound on cv of `0.408 / period`, which crosses the classifier's `cv ≤ 0.25`
at about **1.6 ms** — so a perfectly regular callback faster than that can never be called periodic.
It is also far below the 40 ms band, so the two limits do not interact and nothing is lost. Rig:
`tools/verify/linie_cadence_gap.py`.

-----


> ### ✅ SEEN IN A REAL CE CONSOLE 2026-08-21 `[STALEDLL-B-CE-2026-08-21]` — both placements, and the ladder shows it did NOT hit the stale copy
>
> The offline rig already executed the two functions; what a CE session adds is that `ue5_log`
> actually reached the console. It does, in **both** the places the row names:
> ```
> [10:04:21] [UE5Dump] DLL_PATH = D:\Github\UE5CEDumper\dist\UE5Dumper.dll  (slot 2, size 2879488 bytes (2.7 MB))
> [10:04:23] [UE5Dump] DLL path: D:\Github\UE5CEDumper\dist\UE5Dumper.dll
> [10:04:23] [UE5Dump] DLL size: 2879488 bytes (2.7 MB)
> ```
> — the **startup replay** (first line) and **immediately after `DLL path:`** (last two). **2879488 is
> the real byte count** of `dist/UE5Dumper.dll`, independently hashed earlier today, so the readout is
> accurate rather than plausible.
>
> ⭐ **The resolution ladder printed alongside is the part worth keeping**, because it shows the
> feature working against the very hazard `[STALEDLL]`(a) describes:
> ```
> 2. [FOUND]       folder of CE's last File > Open   - D:\Github\UE5CEDumper\dist\
> 6. [not reached] Cheat Engine install folder       - C:\Program Files\Cheat Engine\
> ```
> Slot 6 is exactly where the ~0.5 MB **February** DLL lives. It was **not reached**, and the size
> beside the path is what would have made it obvious if it had been.
>
> ⚠ **Two things had to be staged, and both are worth knowing before re-running this.**
> * `ue5_log` echoes to the console **only** under `UE5_DEBUG` — `if (UE5_DEBUG or 0) ~= 0 then
>   print(msg) end` — while always writing the file log. So a default run prints **nothing** and looks
>   like the feature is missing. Set `UE5_DEBUG=1` in CE's Lua console first.
> * The `.CT` **short-circuits on an already-injected process**: *"UE5CEDumper is already loaded and
>   serving in this process as 'UE5Dumper.dll'. No injection needed"* — and returns **before** the
>   path/size report. A correct guard, but it means the row cannot be checked against a process that
>   was injected by `inject.py` first; it needs a **fresh, un-injected** game. That cost two relaunches
>   here.

### 🟡 ALL BUT TWO STEPS DONE 2026-08-20 `[PEHOOK-2026-08-17]` — a validation failure must ACT, and the advice must stop saying "re-deploy"

> **Verified: 1 · 1b · 2 · 3 (to its terminal 3/3) · 3b · 4 · 5 · 6 · 7 · 8** — steps 1b–8 headless
> across DumperTest (a shipping *and* a SIB-less build), Lushfoil and EVERSPACE 2; **step 1 on screen**
> (`[PEHOOK-1-UI-2026-08-20]`).
> **Only step 3c remains, and it is structurally unreachable here** — it needs a build that misses
> detection once and then hits, and no such build exists (see `[PEHOOK-3B-2026-08-20]`).

*Was: on **DumperTest** (UE 5.4 Development) the AOB pattern scan misses, the `UE=504` version-table
fallback picks `0x220`, the hook fires **0 times in 1500 ms** — and nothing acted on that verdict, so
every invoke silently timed out for the rest of the session while the UI advised a re-deploy that
cannot help. Now: a zero fire count on the **version-table** path soft-disables the hook and re-arms
detection (bounded at 3 failures, then terminal), and the Self-Test advice is chosen from the DLL's
own `get_diagnostics` hook state instead of asserting one cause.*

⚠ **The asymmetry is deliberate and step 5 is what protects it.** A zero fire count ALSO describes an
idle game thread (paused / loading / minimised). The pattern scan fingerprints ProcessEvent's own
body and has never been observed wrong, so a zero there is reported and the hook is **KEPT**; only
the version-table guess is acted on. Acting on every zero would disable a correct hook.

⚠ **Detector 2 alone proves nothing** — [working-lessons.md](working-lessons.md) §4.4: Kismet helpers
can no-op through ProcessEvent with a **correct** hook, producing the identical signature (args
written, return slot untouched). It is the fired-0-times validator that settles it, because it counts
the game's own traffic. **Do not read a `✗` as widening §4.4's population.**

⛔ **Steps 1–3 need a host whose pattern scan MISSES, and after the detection fix below there is no
longer one on this machine.** DumperTest was that host; the SIB alternates now match it, so it takes
the pattern path and the version-table branch these steps exercise cannot be entered there. Two
honest ways to run them, and **the second is preferred** (the X2 precedent — *step 4 proven by
LOWERING the cap, not by finding a host*):
> * against a **pre-2026-08-18 DLL** on DumperTest — records the old behaviour, not the new code; or
> * ⭐ **temporarily comment out the two `kPePat*Sib*` alternates in
>   [`Frieren.cpp`](../dll/src/Frieren.cpp) `DetectProcessEventVTableOffsetByPattern` and rebuild.**
>   That restores the miss on DumperTest and drives the real, current code down the version-table
>   path. Revert the edit afterwards.

> | step | do this | expect | why it is a real check |
> |---|---|---|---|
> ### ✅ STEPS 1b/2/3/5/7 PASS 2026-08-20 `[PEHOOK-LIVE-2026-08-20]` — via the row's own ⭐ preferred route
>
> Took the route this row recommends: **temporarily removed the two `kPePat*Sib*` alternates** in
> `Frieren.cpp`'s `DetectProcessEventVTableOffsetByPattern`, rebuilt, and drove the **real current
> code** down the version-table path. Source reverted and rebuilt afterwards; `git status` clean.
>
> **The contrast is the whole verification, and it is one variable:**
>
> | DLL | detection path | offset | validation failures |
> |---|---|---|---|
> | SIB alternates removed | `pattern scan missed, falling back to UE=504 version-table primary=0x220` | `vtable+0x220` *(a guess)* | **2** (`failure 1/3`, then `2/3`) |
> | restored (shipping) | `DetectProcessEvent (pattern): match at vtable+0x268` | **`vtable+0x268`** | **0** |
>
> ⭐ **The version table guessed `0x220`; the true offset is `0x268`.** So the slot really was
> mis-detected — the validator was not firing on a healthy hook, it caught a genuinely wrong virtual.
>
> * **Step 2 — PASS, verbatim.** The log carries every element the step names:
>   `GameThreadDispatch: VALIDATION FAILED — hook at 0x7FF69AB99FC0 (vtable+0x220) fired 0 times in
>   1500ms, and that offset came from the version TABLE guess, not the pattern scan. Reading this as
>   a MIS-DETECTED vtable slot (failure 1/3): disabling the hook, refusing the off-thread direct call
>   for the rest of this process (it would call a known-wrong virtual), and re-arming detection.`
>   The next line shows the re-arm actually happening (`pattern scan missed, falling back…` again).
> * **Step 3 — PASS.** The counter is bounded and advancing: `failure 1/3` then `failure 2/3`, never
>   unbounded.
> * **Step 1b — PASS.** `re-deploy` appears **twice in the whole log and both are the negation**:
>   `Re-deploying the DLL will NOT help — the binary is fine, the slot guess is wrong.` No advice
>   string recommends re-deploying. The message even keeps the honest alternative in view: *"(If the
>   game was merely idle, the next invoke re-detects and re-installs by itself…)"*.
> * **Step 5's asymmetry is respected** — with the pattern path restored the hook fired normally and
>   there were **0** validation failures, so nothing acted on a correct hook.
> * **Step 1 proper (the UI Self-Test text) not run** — that needs System → Run Self-Test on screen;
>   what is verified here is the DLL-side verdict and advice the panel now sources from
>   `get_diagnostics`.

> ### ✅ STEP 6 PASSES 2026-08-20 `[PEHOOK-6-2026-08-20]` — and it RETIRES the §4.4 Everspace 2 evidence
>
> The step allows two outcomes and says so: the BlueprintFastCall advice wording, **or** `✓ = 7`,
> which "is itself a result". It came back **7**.
>
> ```
> DetectProcessEvent (pattern): match at vtable+0x278 -> 0x7FF60152D940
> offset resolved to vtable+0x278 via the pattern scan (detection run 0/8)
> hook installed at 0x7FF60152D940, validator armed (1500ms)
> Add_IntInt(3,4) -> result_hex 03000000 04000000 07000000   ==>  ReturnValue 7
> hook_active=True   fire_count=160   VALIDATION FAILED: 0
> ```
>
> ⭐ **This is the result the step was really after.** [working-lessons.md](working-lessons.md) §4.4
> recorded that the Everspace 2 Kismet no-op was diagnosed *while the hook sat in the wrong vtable
> slot*, and that **the stub hypothesis had never been re-verified against a corrected hook**. It has
> now been, on the same title, and it does **not** reproduce: the return slot that "stayed 0" holds
> **7**. §4.4 has been updated — the Everspace 2 evidence for a BlueprintFastCall stub is retired,
> and the failing pattern that section was built on has **no surviving instance on this machine**.
> ⚠ That does not prove BlueprintFastCall never elides a helper; it narrows the claim to "not this
> title", and flips the inverse reading — a KismetMathLibrary failure should now be suspected of
> being a **bad slot** first.
>
> 🔗 **Third distinct slot, which reinforces the row's own warning against "fixing" the version
> table:** `0x260` Lushfoil (5.6) · `0x268` DumperTest (5.4) · **`0x278` Everspace 2**. Slot position
> is a build-flag property, not a version property — all three were found by the pattern scan, each
> on its first attempt (`detection run 0/8`) except Lushfoil's deliberate profiler-first run.

> ### ✅ STEP 4 PASSES 2026-08-20 `[PEHOOK-4-2026-08-20]` — the pattern path is untouched, on Lushfoil
>
> The non-regression check, headless (`tools/verify/lushfoil_pehook_batch.py`). ⚠ It is recorded
> separately from `[PEHOOKONCE-LIVE-2026-08-20]` step 4 on purpose: that run established
> `hook_active: true` and `vtable+0x260`, but **neither the invoke result nor the absence of
> `VALIDATION FAILED`**, which are the two things this step actually asserts.
>
> ```
> DetectProcessEvent (pattern): match at vtable+0x260 -> 0x7FF797AB1510
> ProcessEvent: offset resolved to vtable+0x260 via the pattern scan (detection run 1/8)
> GameThreadDispatch: hook installed at 0x7FF797AB1510, validator armed (1500ms)
> Add_IntInt(3,4) = 7        hook_active=True   fire_count=1223
> NEW 'VALIDATION FAILED' lines: 0
> ```
>
> ⭐ **Stronger than the row asks, because of what preceded it in the same process:** this ran
> *after* the deliberate profiler-before-scan sequence that used to poison the PE hook for the whole
> process. So the pattern path is shown untouched **and** shown to recover — the `detection run
> **1/8**` is the single re-arm, exactly the signature `[PEHOOKONCE-LIVE-2026-08-20]` predicted
> (a normal-order run resolves at run **0/8**).
>
> ⚠ Recorded as the **pipe** invoke, not the UI's Run Self-Test button; the panel wraps the same call
> but its advice text is a separate surface.

> ### ✅ STEP 1 PASSES 2026-08-20 `[PEHOOK-1-UI-2026-08-20]` — and the advice is shown to be STATE-DRIVEN
>
> The last non-UI-blocked step, run on screen: DumperTest + the SIB-less DLL + the real UI (build
> 3263, `DLL build 3263 ✓ matched`), connected (UE504, 25,179 objects), **System → Run Self-Test as
> the first invoke of the process**.
>
> **Click 1 — exactly what step 1 asks for:**
> ```
> ✗ Add_IntInt(3,4) expected 7, got 0
> The ProcessEvent hook is installed but has never fired. That is the signature of a
> MIS-DETECTED vtable slot — check init-*.log for "VALIDATION FAILED". Re-deploying the DLL
> will NOT help: the binary is current, the detected slot is wrong. The same reading also
> appears when the game thread is simply idle (paused, loading, minimised), so retry once
> while the game is actually running before concluding.
> Raw buffer: 030000000400000000000000
> ```
> The `✗`, the **mis-detected vtable slot**, and the `HookNeverFired` wording the step's
> order-dependency note names. `Raw buffer` shows A=3 and B=4 written correctly with the return slot
> **0** — the signature itself. Note it also volunteers the *alternative* explanation (idle game)
> rather than asserting one cause.
>
> ⭐ **Click 2, seconds later, returns a COMPLETELY DIFFERENT string** — because by then the validator
> had condemned the slot and the distrust guard was up:
> ```
> ✗ Add_IntInt(3,4) expected 7, got 0
> The DLL REFUSED this call — it never reached ProcessEvent, so the untouched return slot says
> nothing about the function. Check init-*.log: "VALIDATION FAILED" means a detected vtable slot
> was rejected because the hook never fired (re-deploying the DLL will NOT fix that), and
> "no UObject vtable available yet" means no scan has run in this process — run one and retry.
> ```
> **Two state-appropriate advices from the same button on the same host, minutes apart.** That is the
> row's headline claim demonstrated rather than asserted — *"the Self-Test advice is chosen from the
> DLL's own `get_diagnostics` hook state instead of asserting one cause"* — and the step's own note
> that a later click "correctly gets the `HookOff` wording instead" is confirmed as designed, not a
> failure.
>
> 🔗 **This is the UI face of `[PEHOOK-3B-2026-08-20]`.** The `-3` refusal measured over the pipe
> there surfaces here as **"The DLL REFUSED this call"** — the same state, reached the same way,
> observed through a different surface.
>
> **Step 1b re-confirmed in the UI, not just in the unit test:** across BOTH strings, the only
> mention of re-deploying is its negation (*"will NOT help"*, *"will NOT fix that"*).

> ### ✅ STEP 3b PASSES + STEP 3 NOW COMPLETE 2026-08-20 `[PEHOOK-3B-2026-08-20]`
>
> Rig: `tools/verify/pehook_3b_refusal.py`, on a purpose-built DLL with both `kPePat*Sib*` alternates
> gated behind `constexpr bool kSibAlternatesEnabled = false`. ⚠ **The variant was copied to the
> scratchpad and the source reverted and rebuilt in the same step**, so `dist/` never held it;
> `git status` clean and `build_number.txt` unchanged at 3263 (`-NoBumpBuildNumber` on both builds).
> The rig injects the variant by path (`inject.py --dll`), so nothing depends on `dist/` being wrong.
>
> **Staging confirmed before anything was concluded** — the host really did take the guessed path:
> `DetectProcessEvent (fallback): pattern scan missed, falling back to UE=504 version-table
> primary=0x220`, then `VALIDATION FAILED — hook at 0x7FF69AB99FC0 (vtable+0x220) fired 0 times in
> 1500ms, and that offset came from the version TABLE guess`.
>
> **Step 3b — PASS.** Four `direct_call` invokes across the condemn window:
> ```
> results in order: [0, -3, -3, -3]      -3 x3   |   -5 x0
> ```
> ⭐ **The `-5` count is the control that makes the `-3` mean "refused".** `-5` is the ordinary
> game-thread timeout; `-3` is produced *only* by the two `s_peOffsetDistrusted` guards. Zero `-5`s,
> so the direct path was genuinely refused rather than quietly queued — which is the over-correction
> the step exists to catch: re-arming without refusing would `call` a known-wrong virtual, where the
> pre-fix code merely timed out.
>
> **Step 3 — now COMPLETE, and the terminal state had never been observed before.** The earlier
> block recorded `failure 1/3` then `2/3` and honestly claimed only "bounded and advancing". Driving
> **non-direct** invokes into the same condemned process reached the end of the ladder:
> ```
> failure 1/3 : 1     failure 2/3 : 1     failure 3/3 : 1
> giving up on ProcessEvent for this process : 1
> pe_profile_start -> hook_active: false,
>   hook_detail: "ProcessEvent detection FAILED on this game — the vtable slot could not be
>                 determined, or a detected slot never fired and was rejected…"
> ```
> That is exactly the step's second clause — **the detection-FAILED detail, not the "not resolved
> yet" one**.
>
> ℹ️ **Why the non-direct route was needed, and it is a real property of the design:** once the
> distrust guard is up, every `direct_call` is refused at the door and therefore never re-arms a
> validator, so the failure counter cannot advance past 2 that way. A *queued* invoke still
> re-detects and re-installs (`-5`, then `-3` once the next verdict lands), which is what walks the
> ladder to 3/3. Anyone re-running step 3 with `direct_call` alone will stall at 2/3 and think the
> bound is wrong.
>
> ### ✅ STEP 3c CLOSED 2026-08-23 `[PEHOOK3C-STAGE-2026-08-23]` — reachable after all, and the blocker below aimed at the wrong mechanism
>
> ⭐⭐ **3c has nothing to do with the pattern.** The note below reasons about pattern hits and
> SIB alternates, but `this offset is TRUSTED again` is emitted by the **post-install validator**
> ([Frieren.cpp:1869](dll/src/Frieren.cpp:1869)) when a *previous* validation FAILED and a later one
> PASSES. And the failure path only runs when the offset's provenance is the **version table** —
> a zero-fire reading on a pattern-detected offset is deliberately ignored. So on DumperTest the
> cycle can never start, for a reason unrelated to which patterns match.
>
> ⭐ **The stage is therefore one line, and it cannot crash the host:** relabel the provenance
> (`fromTable = true`) at [Frieren.cpp:1773](dll/src/Frieren.cpp:1773). **The offset itself is still
> the pattern's**, so the hook is correct and fires normally — only the validator's interpretation of
> a zero-fire window changes. Nothing is mis-detected, which removes the crash risk a
> wrong-slot stage would carry.
>
> The rest is `suspend-tid`, the same instrument B8 and L8 used:
> ```
> [1] provenance: version TABLE (a guess — the post-install validator decides whether it is right)
> [2] VALIDATION FAILED — hook at 0x7FF7D1DD8CB0 (vtable+0x268) fired 0 times in 1500ms   <- thread frozen
> [3] resumed
> [4] this offset is TRUSTED again — the earlier zero-fire reading (1 consecutive) was an
>     idle game thread, not a mis-detected slot
> ```
> That is exactly the step's wording — *"after a condemn, let the game tick and invoke until the hook
> re-installs and validates"* — and it is the **over-correction** check too: the recovery attributes
> the zero-fire to an idle thread rather than a bad slot.
>
> ⚠ **The design objection below is right, and does not apply.** It rejects *"a runtime-togglable
> pattern set — a test hook in shipping code"*. A **staged build** ships nothing: the line existed
> for one build, was reverted with `git checkout`, and the rebuilt DLL logs
> `offset resolved to vtable+0x268 via the **pattern scan**` again — verified in the binary, since
> `dist/` is gitignored.
>
> ⛔ (superseded) **Step 3c remains unreachable, and not for want of trying.** It needs a condemn *followed by a
> successful re-detection* (`this offset is TRUSTED again`). The shipping DLL's pattern always
> matches on DumperTest, so it never condemns; the SIB-less variant can never re-validate, so it
> never recovers. No build available today misses once and then hits. Reaching it would need a
> runtime-togglable pattern set — a test hook in shipping code, which is a design decision, not a
> verification step.

> ### VERIFIED 2026-08-20 - STEPS 5 and 7 PASS, headlessly (`tools/verify/pehook_step5_idle.py`)
>
> ⚠ **The block above says "step 5's asymmetry is respected", and that was too generous.** With the
> pattern path restored the hook FIRED normally, so the validator's zero-fire branch was **never
> entered at all** — which shows the guard was not needed, not that it works. Step 5 asks for the
> opposite staging: the pattern branch actually *taken*, with a real 0.
>
> **How the idle window was staged with nobody present.** The step says "background/pause the game
> so PE traffic stops". **Suspending the UE game thread** is the same condition, scriptable, and
> strictly stronger — backgrounded, this build still ticks (~120 fires/s at `t.MaxFPS 15`); frozen,
> the count is *exactly* 0, so "0 fires" is a fact rather than a hope.
>
> **A FRESH process is required and the rig relaunches to get one:** the validator is armed once, at
> hook install, so reusing an already-validated process would pass vacuously. Order: launch → inject
> → scan → **confirm `hook_active == false`** → freeze → force the install with `pe_profile_start`
> (MinHook work on the calling thread, so it needs no game thread; an *invoke* would block on the
> thread just frozen) → wait out the window.
>
> ```
> after scan, BEFORE any invoke: hook_active=False fire_count=0     <- the window is enterable
> GameThreadDispatch: hook installed at 0x7FF69AF38CB0, validator armed (1500ms)
> after the window:              hook_active=True  fire_count=0     <- a REAL zero
> [WARN] ... fired 0 times in 1500ms, but the offset came from the PATTERN scan - the detector
>        that fingerprints ProcessEvent's own body. ... The hook is KEPT.
> 'VALIDATION FAILED' : 0
> after resume: Add_IntInt(3,4) = 7,  fire_count=368
> ```
>
> Every control the step needs is asserted rather than assumed: the hook was **absent** before the
> freeze, **installed** during it, the fire count **did not move** across the window, and the kept
> hook **still invokes** afterwards — "the hook is KEPT" is only worth something if the kept hook
> works. `WARN` (not `ERROR`), `hook_active` still `True`, zero `VALIDATION FAILED`.
>
> **Together with the block above this is now a two-armed test of one discriminator**, differing in
> exactly one variable — where the offset came from:
>
> | offset source | fires in 1500 ms | verdict | hook |
> |---|---|---|---|
> | version TABLE (SIB alternates removed) | 0 | `ERROR VALIDATION FAILED … failure 1/3` | **disabled**, detection re-armed |
> | PATTERN scan (shipping DLL, thread frozen) | 0 | `WARN … The hook is KEPT` | **kept**, invokes fine after resume |
>
> **Step 7 also passes on this same fresh launch** — it is the launch the step describes:
> `DetectProcessEvent (pattern): match at vtable+0x268`, and **zero** occurrences of
> `falling back to UE=…version-table` or `VALIDATION FAILED` anywhere in the run. So the detection
> fix is now witnessed *inside the running process*, not only file-verified, and the caveat below
> ("treat DumperTest as unproven for invoke-dependent rows") is lifted.
>
> **Step 8's invoke half holds** — `Add_IntInt(3,4) = 7` on this host. ⚠ Recorded honestly as the
> *pipe* invoke, not the UI's **Run Self-Test** button; the panel wraps the same call but its advice
> text is a separate surface and is not what was observed here.
>
> Steps 3b, 3c, 4 and 6 remain open (3b/3c need a condemned host, 4 needs Lushfoil, 6 EVERSPACE 2).


> | 1 | **DumperTest**, SIB alternates temporarily removed → System → **Run Self-Test**, as the **FIRST invoke of a freshly launched process** | `✗ Add_IntInt…`, and the advice names a **mis-detected vtable slot** | ⚠ order-dependent: `HookNeverFired` needs `hook_active == true`, and the validator soft-disables the hook 1500 ms after install. A later click sees the hook DOWN and correctly gets the `HookOff` wording instead — that is not a failure |
> | 1b | any Self-Test run | no advice string recommends re-deploying without ruling it out | `SelfTestAdviceTests.NoAdviceRecommendsRedeploying` pins the rule offline; this just confirms it reached the UI |
> | 2 | grep that run's `init-0.log` | `VALIDATION FAILED — … came from the version TABLE … (failure 1/3): … re-arming detection`, then `hook flag cleared` | the verdict is now acted on, and the log names the real cause |
> | 3 | force three CONSECUTIVE failing invoke attempts | the 3rd logs **"giving up on ProcessEvent for this process"**, and `pe_profile_start` then returns the **detection-FAILED** detail, not the "not resolved yet" one | proves the retry loop is bounded and lands in the honest terminal state. "Consecutive" is load-bearing — a validation that PASSES resets the counter |
> | 3b ⚠ SAFETY | after a condemn, issue an invoke within the next ~5 s (the install-retry cooldown, while the offset is usable but the hook is down) | the invoke returns **-3** and does **not** call through; the Self-Test says **the DLL REFUSED this call** | self-review found this: re-arming without the refusal made the mis-detected case WORSE than before, because the direct fallback would call a known-wrong virtual where the old code merely timed out |
> | 3c ⚠ THE RECOVERY, and it is the one that catches an over-correction | after a condemn, let the game tick and invoke until the hook re-installs and validates | `this offset is TRUSTED again` in the log, and direct calls (CE Lua `callFunction`, Run Self-Test) **work again** | review HIGH-1: a lifetime "have we ever failed" tally left the direct path dead for the rest of the process even after full recovery — `[PEHOOKONCE]` rebuilt one layer down. The refusal must be a STATE that lifts |
> | 4 ⚠ NON-REGRESSION | **Lushfoil** → Run Self-Test | `✓ Add_IntInt(3,4) = 7`, hook stays installed, **no** VALIDATION FAILED | the pattern path must be untouched |
> | 5 ⚠ THE FALSE-POSITIVE GUARD | on a pattern-detected title, background/pause the game so PE traffic stops, then force a first invoke | if 0 fires, the log is a **WARN** saying the offset came from the pattern scan and the hook is **KEPT**; invokes work once the game ticks again | a correct hook must survive an idle window — this is the regression the asymmetry exists to prevent |
> | 6 | on a title where a Kismet helper no-ops with a good hook (§4.4 — **EVERSPACE 2**), Run Self-Test | the advice is the **BlueprintFastCall** wording, not the wrong-slot one. ⚠ **`✓ = 7` is an equally valid outcome and is itself a result** | [working-lessons.md](working-lessons.md) §4.4 records that the EVERSPACE 2 no-op was diagnosed *while the hook was in the wrong slot* and was **never re-verified against a corrected hook**. A `✓` here narrows §4.4 again; it does not fail this step |

**The DETECTION half was also fixed, offline, from the binary's own bytes.** Root cause: the pattern
budgets two wildcards (ModRM + disp32 low byte), but when the compiler parks the `UFunction*` in an
**extended** register x64 makes a **SIB byte mandatory**, so the instruction is one byte longer and
the fixed `00`s land early. Measured at `ProcessEvent+0x36F` in the Development build:
> ### ℹ️ 2026-08-24 — the binary this was measured on is GONE, and the claim was RE-CONFIRMED rather than carried
>
> All three DumperTest configs were repackaged 2026-08-24, so the 08-23 Development exe this row was
> file-verified against **no longer exists anywhere** (searched `D:\UE_Analyze_data`). An impact sweep
> called that an irreversible loss. It is not — the measurement **reproduces on the current build**,
> which is the only thing that mattered:
>
> | config | `r12` form | `rdx` form |
> |---|---|---|
> | Development | **1** | 2 |
> | Shipping | **0** | 2 |
> | DebugGame | 1 | 2 |
>
> So *"Development uses `r12`, the Shipping build of the same project uses `rdx`"* still holds, on
> binaries that exist. ⚠ What must NOT be carried forward on assertion is the **`+0x268` vtable slot**:
> its ground truth came from the **paired PDB**, and Shipping ships **no PDB at all** (Development and
> DebugGame have 3 each), so that half is only re-derivable on the two debug-bearing configs.

`41 F7 84 24 B0 00 00 00 00 04 00 00` = `test dword ptr [r12+0xB0], 0x400`; the Shipping build of the
same project uses `rdx` and matches today. Ground truth for the slot came from the **paired PDB**:
`UObject::ProcessEvent` is vtable entry **77 = +0x268** in BOTH configs, and the fallback's `0x220` is
entry 68, `UObject::GetSubobjectsWithStableNamesForNetworking` — a replication callback that never
runs in a single-player sample, which is precisely "fired 0 times". SIB-tolerant alternates were
added, and the regression risk (a looser pattern matching an EARLIER slot) was **measured, not
argued**: over the **22 shipped UE games** in the local corpus plus both DumperTest configs, 60
candidate vtables each, **not one binary changed a first match it already had**; the only delta is
DumperTest Development going from no match at all to exactly one, at `0x268`.

> | step | do this | expect |
> |---|---|---|
> | 7 ⚠ THE DETECTION FIX | launch **DumperTest** (Development) with the new DLL, `init` → `trigger_scan` → one invoke, then grep `init-0.log` | `DetectProcessEvent (pattern): match at vtable+0x268`, **no** `falling back to UE=504 version-table`, and **no** `VALIDATION FAILED` |
> | 8 | Run Self-Test on DumperTest after step 7 | `✓ Add_IntInt(3,4) = 7` — the sample becomes usable for invoke-dependent rows |

⚠ **Until step 7 is observed, treat DumperTest as unproven for invoke-dependent rows and use
Lushfoil.** The slot is PDB-confirmed and the scan is file-verified, but nothing has yet watched the
DLL do it inside the running process.

⚠ **The pattern scan is still what has to work — it stays primary and the fire-count validator
stays the backstop.** But the two reasons this row gave for *not* fixing the version table were both
wrong, and the table was fixed on 2026-09-05 (audit A2). Recorded so the old wording is not cited
again:

- **"Lushfoil 5.6 → table `0x228`" is impossible.** The table read `>= 550 → 0x228 / >= 500 → 0x220`,
  and **550 is not a producible version** — versions are encoded `major*100+minor` and capped at 509
  (`Genau.cpp` `major == 5 && minor <= 9`; `Fern.cpp`'s 418..509 bound). The `0x228` arm was dead
  code. Lushfoil got `0x220`, exactly as DumperTest did, and so did every other UE5 title.
- **"Slot position is a build-flag property, not a version property" is refuted by these very two
  observations.** The measured non-editor slots are 5.4 = `0x268` and 5.6 = `0x260`
  (`vendor/RE-UE4SS/assets/VTableLayoutTemplates/`, cross-checked against six values this repo
  already held). So DumperTest sitting *later* than Lushfoil is precisely what the **version** table
  predicts — the two differ by engine version, not by build flags. The audit separately measured
  5.8 Shipping / Development / DebugGame all at `0x250` and 5.4 Shipping and Development both at
  `0x268`, i.e. build configuration does not move it. The Iris/replication attribution was a guess
  that happened to land on a real difference with the wrong cause.

⚠ Still unconfirmed **on a live game**: 5.0, 5.1, 5.2, 5.3 and 5.5. Their slots are measured from
the PDB oracle but no title at those versions has been run. 4.11–4.25 likewise.
⚠ The oracle is a **non-editor** dump. UE 5.8's `Object.h` declares 33 `WITH_EDITOR` virtuals
*before* `ProcessEvent`, so an editor process does not share these slots; the templates simply
contain none of those entries. Do not extend the table to an editor build.

-----

### 🔲 U3 + U17 — original checklist (kept for the steps)

*Needs a connected game. See dev-log builds 3169 and 3171. **The decode RULES are unit-pinned
(35 assertions, four negative controls); the LOOKUP half is not** — resolving a `UScriptStruct*` and
`WalkClass`-ing it touches target memory, and no target compiles `Ubel.cpp`.*

> **There is a known-good vehicle already on record.** This register names `Map_IntToVec3f` as the
> field that reproduced the original `f:[6203.0000]`, so the before/after is one row.
>
> | step | do this | expect | why it is a real check |
> |---|---|---|---|
> | 1 | Live Walker → expand a struct-valued `TMap`/`TSet` element | `{X=…, Y=…, Z=…}`, not `f:[…]` | U17: those callers now use the reflected layout |
> | 2 | cross-check against `hexValue` on the same row | all components present and correct | the hex always held them; that is how U3 was caught |
> | 3 | a UE5 **LWC** title (24-byte `FVector`) | three components at real magnitudes | the case the byte-blind path structurally cannot get right |
> | 4 ⚠ control | any **GAS** title — a `FGameplayAttributeData` preview | still `BaseValue` / `CurrentValue`, no pointer halves | **the regression guard**: GAS really does have a vtable, and "just delete the skip" would show four values here |
> | 5 | a struct with NO resolvable layout | still `f:[…]` | the byte-blind fallback is retained on purpose, not dead |

> ### ✅ **Step 5 REWRITTEN and CLOSED 2026-08-24** `[U17-STEP5-2026-08-24]` — DumperTest dev, DLL 3348
>
> ⚠⚠ **As written, step 5 CANNOT FAIL.** "A struct with no resolvable layout still renders
> `f:[…]`" passes identically on a build where the entire layout path is dead — if the decoder
> always returned nothing, *every* struct everywhere would render `f:[…]` and the step would pass
> with flying colours while the feature it guards was gone. Confirming a **fallback** still fires
> says nothing about the **primary** path being alive.
>
> ⭐ **Replaced with a three-way result on ONE build, then a red/green control.** Live
> `DumperTestActor` (not the CDO — the CDO's containers are empty and quietly answer nothing):
>
> | surface | clean DLL 3348 | staged: layout decoder killed |
> |---|---|---|
> | `walk_instance` StructProperty | `Health = {BaseValue=100, CurrentValue=70}` — **named** | **`None`** — no value at all |
> | `search_properties` preview | `f:[100.0000, 44.0000]` — unnamed floats | `f:[100.0000, 44.0000]` — **UNCHANGED** |
>
> All four StructPropertys behaved alike (`PrimaryActorTick`, `AttachmentReplication`,
> `ReplicatedMovement`, `Health`). Control: `InterpretStructByLayout` -> `return ""`, rebuild,
> reinject — the walk rows go to `None`; revert, rebuild — they come back named. Red, then green.
>
> ⛔ **Two corrections to the premise, both found BY the control rather than by reading.**
> 1. **`InterpretStructAt` is NOT the single point of failure.** Staging it alone left
>    `walk_instance` **fully alive**, because there are two independent entry points:
>    `Ubel.cpp:2005` reaches the decoder *through* `InterpretStructAt`, while `Ubel.cpp:4965` —
>    the `walk_instance` path — calls `InterpretStructByLayout` **directly**. Only the shared leaf
>    kills both.
> 2. **On `walk_instance` there is no `f:[…]` fallback at all** — the value simply disappears. The
>    `f:[…]` rendering lives on the *Property Search preview* surface, and that surface is
>    **byte-blind by construction**: its StructProperty branch (`Ubel.cpp:6060-6069`) calls
>    `InterpretValue` directly and never touches the layout, because it holds only a struct-type
>    NAME and no `UScriptStruct*`. Its output is therefore **unchanged by the staged build** — which
>    is the positive proof, not an assumption. ▶ **Running step 5 against the Property Search
>    preview column measures nothing on any build, forever.**
>
> ℹ️ **Step 4 is NOT discharged by this run, though the shape matches.** `Health` is
> `FDumperTestAttribute`, documented in `DumperTestTypes.h:28` as *"in the shape of a GAS
> `FGameplayAttributeData`"* — but it is a plain two-float USTRUCT with **no vtable**, and the vtable
> is precisely what gives step 4 its discriminating power ("GAS really does have a vtable, and 'just
> delete the skip' would show four values here"). Step 4 still needs a real GAS title.
> ℹ The live drift (`CurrentValue` read 52 / 37 / 44 / 70 across the session) is the fixture's own
> 1 Hz decrement (`DumperTestActor.h:242`), and it incidentally proves both surfaces are reading the
> same live memory rather than a cache.

### 🔲 A3 — one FVector per class was ever indexed (build 3168)

*Needs a connected game. See dev-log build 3168. **The guard's CONTRACT is unit-pinned
(`Test_Aura_StructPathGuard`, negative control 7 red); the WALK THAT USES IT is not** — no test target
compiles `Aura.cpp`, so `expandFields` calling the guard has never run against a real class.*

> **Why this is cheap: the before/after is a single scan and the expected delta is huge.** The guard
> was whole-walk instead of path-scoped, so only the FIRST field of a given `UScriptStruct` type in a
> class contributed leaves — `Location` was indexed, `Velocity` / `Scale3D` / `Extent` never were,
> subtree and all, across unrelated branches.
>
> | step | do this | expect | why it is a real check |
> |---|---|---|---|
> | 1 | Value Search, **Float** (or NumericAll), any value, on a class with a pawn/actor | rows whose field name ends in `.Velocity` / `.Scale3D`, not only `.Location` | before 3168 exactly one FVector per class could appear; this is the whole defect |
> | 2 ⚠ control | the same scan with data type **FVector** | unchanged vs before 3168 | `acceptedStructNames` is non-empty for vector scans, so the recursion is skipped and the guard never fired there — **if this changed, the fix reached somewhere it should not** |
> | 3 | Group Scan or Property Search **Deep** for the same field | already found it before 3168 too | those walkers were always path-scoped; confirms the asymmetry that made this diagnosable |
> | 4 | grep `scan-*.log` for `hit the 4000 scan-field cap` | absent on ordinary classes | the new cap is meant to be unreachable in practice; if it fires routinely the value is wrong |
>
> ⚠ **Do not verify with an FVector scan.** It is the one data type the defect never touched, so a
> green FVector run proves nothing — that is what step 2 is for, as a control rather than as evidence.

### 🟡 NEW 2026-08-17 — AA12 / AA13: the freeze script must stop lying about success (key: FreezeOutcome) — **the LYING is fixed and verified; STEP 5 CLOSED 2026-08-24, only step 4 is left**

*Needs a **real Cheat Engine** plus a connected game. See dev-log build 3125. The Lua rig stubs every
CE global, so what is unproven is precisely the CE-side behaviour: whether the window stays up and
whether the record ends ticked or unticked.*

> ### ✅ STEP 1 PASSES 2026-08-20 `[AA12-STEP1-2026-08-20]` — the happy path, with a release control
>
> **CE 7.7 + AOBMaker plugin + DumperTest**, the full chain the row describes.
>
> Property Search → `DumperTestActor.TickCount` (IntProperty `0x6A8`, climbing ~1 Hz) → **Freeze**
> → *"Freeze script created in CE: Freeze: DumperTestActor::TickCount = 9999"* → CE attached to
> `DumperTest.exe` → tick.
>
> **All three of the step's assertions hold:**
> * **the value holds** — `TickCount` read **9999** on two refreshes **10 s apart**, on a field that
>   had been counting up continuously beforehand;
> * **the Lua Engine window closes** — it never stayed up, which is the CE-Lua-hygiene rule's
>   auto-close-on-clean-success path;
> * **the record stays ticked** — `Active=true` (red ✗ in the checkbox).
>
> ⭐ **The release is the control, and it is what makes the 9999 mean something.** Unticking the
> record let the value resume immediately: **9999 → 10039 → 10048**. So the hold was caused by the
> freeze rather than by the field having stopped on its own, and the release path works too.
>
> ⚠ **Getting here required working around two defects filed from this same session** —
> `[FREEZEUNTICK-2026-08-20]` (the helper-missing bail-out leaves the record Active) and
> `[FREEZEINJECT-CRLF-2026-08-20]` (the setup step reports failure on a write that succeeded). Step 1
> passes *despite* them, because the injection really did work; a user following the on-screen
> messages would reasonably have concluded the feature was broken.
>
> Steps 4-5 remain: 4 needs a spawn, 5 needs a pre-1.2 helper. *(Step 5 CLOSED 2026-08-24 — the
> pre-1.2 helper was in git all along, `[AA12-STEP5-OLDHELPER-2026-08-24]` below.)*
>
> ### ✅ THE BAIL-OUT HALF IS CLOSED 2026-08-21 `[AA12-BAILOUT-2026-08-21]` — the row's actual title
>
> Step 1 above proved the **happy** path. This is the **failure** path — which is what
> "stop lying about success" is about, and the half that was still broken when step 1 ran (the note
> above says so: *"a user following the on-screen messages would reasonably have concluded the
> feature was broken"*).
>
> **CE 7.7 + DumperTest, on the REAL generated script** (5,495 chars, emitted through
> `FreezeScriptGenerator.Generate(PropertySearchViewModel.BuildFreezeParams(...))` for the same
> `DumperTestActor · TickCount · 0x6A8` row, preview **64** — the Freeze *button* is gated on the
> AOBMaker plugin, which was offline, so the script the button copies was produced directly rather
> than clicked). Loaded as a `vtAutoAssembler` record and enabled **without** the helper in the table,
> i.e. deliberately into the bail-out:
>
> | what the row asks | result |
> |---|---|
> | an accurate message instead of silent false success | ✅ `[Freeze] ue5_freeze_helper.lua not found in this table.` — verbatim |
> | the record must not claim to be active | ✅ `Freeze: DumperTestActor::TickCount = 9999  ->  Active=false` **(was `true`)** |
> | nothing may actually be applied | ✅ `TickCount` re-read at **1497**, still climbing from the 64 before the attempt — not held at 9999 |
>
> Read from CE's **Lua Engine**, never from the checkbox icon.
>
> ⚠ **Both defects the step-1 note said it had to work around are now fixed**:
> `[FREEZEUNTICK-2026-08-20]` (this run is its proof) and `[FREEZEINJECT-CRLF-2026-08-20]`.
>
> ### ✅ STEP 6 PASSES 2026-08-20 `[AA12-STEP6-2026-08-20]` — two freezes coexist and are independent
>
> Two freezes on the *same class*, deliberately of **different widths** so a shared-state bug could
> not hide:
> ```
> Freeze: DumperTestActor::F32_Ticking = 555.5    (FloatProperty  0x6B0, was 1000.5 and moving)
> Freeze: DumperTestActor::F64_Ticking = 777.75   (DoubleProperty 0x6B8, was 20024.375 and moving)
> ```
>
> | phase | F32_Ticking | F64_Ticking |
> |---|---|---|
> | both ticked, two reads 10 s apart | **555.5** · **555.5** | **777.75** · **777.75** |
> | **F32 unticked only**, two reads 10 s apart | **969.75 → 877.5** (resumed) | **777.75** · **777.75** (still held) |
>
> ⇒ Both hold simultaneously, and unticking one releases **only** that one — the row's assertion that
> "the keyed-handle table is untouched by this change" survives. The float/double pairing means a
> single shared write path or a type-confused handle would have shown up as one freeze clobbering
> the other, and neither happened.
>
> ⚠ CE attached to **`UE5DumpUI.exe`** on the first attempt because the process list reorders between
> openings and the row at the remembered y-coordinate had changed. Caught by reading the title bar
> (`0000A9AC-DumperTest.exe`) before ticking — worth doing every time, since a freeze against the
> wrong process fails for a reason that looks like a product defect.
>
> ### ✅ STEP 5 PASSES 2026-08-24 `[AA12-STEP5-OLDHELPER-2026-08-24]` — an OLD helper is reported as unknown, with a control that proves the close path works
>
> The last open step of AA12/AA13, and the only thing that had ever blocked it was *"needs a
> pre-1.2 helper"*. **An old version of our own artifact is never a missing fixture in a git repo**:
> `git show 04d40803^:scripts/ue5_freeze_helper.lua` is version **1.1** — a period artifact from
> commit `661c3925` (2026-08-16), not a reconstruction. Extracted to
> `out/aa12/ue5_freeze_helper_1_1.lua`, 683 lines, 29,535 bytes.
>
> ⭐ **Why 1.1 is the right one, mechanically:** the branch under test is `sok2 == nil`, and 1.1's
> `handle.start` ends on `handle._rescanTimer.Enabled = true` and **returns nothing**
> (`ue5_freeze_helper_1_1.lua:646-663`). So `pcall` yields `sok=true, sok2=nil` — neither success
> nor failure, the fourth state the generator refuses to guess at.
>
> **Environment.** DumperTest **Shipping** (24,478 objects, GObjects `0x7FF6D350BB50`, GWorld
> `0x7FF6D368BC70` — alive, not the dead-engine trap), DLL **3338**, CE **7.7.0.10568** with the
> AOBMaker plugin **Connected**, and the script produced by the real **Freeze** button
> (`DumperTestActor · TickCount · IntProperty · 0x698 · 9999`, scope *"every live DumperTestActor
> and every subclass"*). Not a generator fixture — the button, through `CreateAAScriptAsync`.
>
> ⚠ **The helper must be a TABLE FILE, not a global.** A first attempt loaded 1.1 with `dofile`,
> which defines `freezeProperty` perfectly well — and the script still refused, because
> `AppendHelperLoader` resolves it with **`findTableFile('ue5_freeze_helper.lua')`**
> ([FreezeScriptGenerator.cs:238](ui/UE5DumpUI/Services/FreezeScriptGenerator.cs:238)), a table-file
> lookup with *no filesystem fallback*. Embedded properly with the bridge's own documented
> incantation — `findTableFile` (delete-if-exists) + `createTableFile` +
> `Stream.copyFrom(createStringStream(...))` + a `Stream.Size` check
> ([IAobMakerBridge.cs:80-88](ui/UE5DumpUI/Core/IAobMakerBridge.cs:80)) — verified `29535 == 29535`.
>
> **THE ARM — all three of the step's assertions hold:**
>
> ```
> TABLEFILE_PRESENT=true
> ACTIVE_BEFORE=false
> [Freeze] this table has an older ue5_freeze_helper.lua: it cannot report whether anything
>          was frozen. Re-inject it via UE5DumpUI -> Tools -> Inject Freeze Helper into
>          Current CE Table.
> ACTIVE_IMMEDIATELY_AFTER=true
> ...
> RECORD_STILL_TICKED=true   (must be true)
> HELPER_VERSION_IN_STATE=1.1
> ```
> * the *"older … re-inject it"* line, **verbatim** ✅
> * the Lua Engine window was **left open** ✅
> * the record was **left ticked** ✅ — re-read long after the 50 ms deferred-untick timer, so this
>   is the settled state and not a snapshot taken before the timer could fire
>
> ⭐ **THE CONTROL, and it is what makes "the window stayed open" mean anything.** A window that
> stays open is equally consistent with an auto-close that never works at all. So the *same record
> and the same script* were re-ticked with only the table file swapped to the **current** helper
> (58,802 bytes vs 1.1's 29,535 — `versionLess('1.1','1.5')` makes the newer chunk redefine over the
> resident one, `ue5_freeze_helper.lua:273-294`):
>
> | | ARM — helper 1.1 | CONTROL — helper 1.5 |
> |---|---|---|
> | *"older … re-inject it"* line | ✅ printed | **absent** |
> | Lua Engine window | **left OPEN** | **CLOSED** by the script |
> | record | ticked | ticked |
> | `TickCount` | — | **held at 9999** across two reads 12 s apart, on a field that had been climbing (84 → 348 before the freeze) |
>
> So the close path provably fires in this very session, and the old-helper branch is what suppressed
> it. `sok2 == true and scount ~= 0 and not scapped` behaved on both sides of the gate.
>
> ⭐ **A third state came free and re-confirms `[AA12-BAILOUT-2026-08-21]`.** Before any table file
> existed, the same script showed `[Freeze] ue5_freeze_helper.lua not found in this table.` and
> **unticked itself** (`ACTIVE_IMMEDIATELY_AFTER=true` → unticked by the deferred timer). All three
> states — **missing / old / current** — are distinguishable and each behaved to spec.
>
> ℹ️ **A CE-side exception, and it is NOT ours — measured, not assumed.** Twice CE raised
> `Unhandled exception: [TCustomForm.SetFocus] frmLuaEngine:TfrmLuaEngine Can not focus
> (EInvalidOperation)` and auto-saved the table. Both times the tick had been driven **from inside
> the Lua Engine window**, i.e. our `synchronize(getLuaEngine().Close())` closing the form that was
> itself the active one. Unticking and re-ticking the record from the **address-list checkbox** — the
> normal user path — raised **nothing**, twice. Recorded so the next reader does not chase it as a
> freeze defect; it is an artifact of driving CE the way a verification session drives it.

>
> ### 🟡 STEP 3 ATTEMPTED 2026-08-20 — the fixture was WRONG, and finding out is itself a result
>
> Step 3 needs *"a class with **zero live instances** right now"*. `NiagaraComponent.WarmupTickCount`
> looked ideal: Property Search previews it as **`0 (CDO default)`**, which reads as "nothing live".
> Freezing it and ticking gave **no error and no untick** — and with `UE5_DEBUG=1` set in CE's Lua
> Engine, the reason showed:
> ```
> [Freeze] Started: NiagaraComponent::WarmupTickCount = 9999 (int32@0x624) on 2 instance(s)
> [Freeze] Stopped: NiagaraComponent::WarmupTickCount        (on untick)
> ```
> **Two live instances.** So this was never the empty case and step 3 is *not* satisfied — it needs a
> class with zero live instances **including subclasses**.
>
> ⭐ **Why the fixture looked right, and it is a finding in its own right — see
> `[CDOSCOPE-2026-08-20]` below:** the `(CDO default)` marker is decided on an **exact**
> `ClassPrivate` match (`Aura.cpp`'s preview walk skips any object whose class is not the row's class
> exactly), while Freeze and Force both scope **derived** (`FindInstancesDerivedFrom`). A row can
> therefore say "CDO default" and still have live instances the action will hit.
>
> ⚠ **And step 3's own assertion is currently NON-DISCRIMINATING.** It asks that the record *stays
> ticked*; `Active=true` was duly observed. But `[FREEZEUNTICK-2026-08-20]` means **nothing ever
> unticks**, so "stays ticked" would be seen whether or not the code intended it. Until that defect
> is fixed, this step cannot fail and therefore cannot pass either — the row's own warning ("if this
> unticks, the fix broke the feature") has no way to fire.
>
> ℹ️ **Free confirmation of the CE-Lua hygiene rule:** those `[Freeze] Started/Stopped` lines are
> **silent by default** and appeared only after `UE5_DEBUG=1`, which is exactly the
> `local DEBUG = UE5_DEBUG or 0` contract CLAUDE.md requires of every emitted script.

>
> ### 🟡 STEP 2 — THE MESSAGE HALF PASSES, THE UNTICK HALF FAILS 2026-08-20 `[AA12-STEP2-2026-08-20]`
>
> The row calls this *"the hard failure — the whole point"*, and it is now run properly: the freeze
> script and the helper were both created **while the DLL was injected**, then DumperTest was killed
> and **relaunched with the DLL deliberately NOT injected**, CE re-attached to the new process, and
> the same record ticked.
>
> | the row expects | result |
> |---|---|
> | a `showMessage` **naming the reason** | ✅ `[Freeze] nothing was frozen:` / `[ue5_freeze] g_invokeMailbox symbol not found -- is UE5Dumper.dll injected?` — accurate, names the real cause, and says outright that nothing happened |
> | the record **unticked by itself** | ⛔ **`getMemoryRecord(0).Active = true`**, read from CE's Lua Engine |
> | the Lua window **still open** | n/a — the bail-out happens before any Lua Engine output, so no window is opened to keep open |
>
> ⇒ **The fix is half-landed.** The behaviour the row was written against was *"it silently reported
> success, closed the window, and stayed ticked"*. The silent-false-success part is fixed and the
> message is good. **The staying-ticked part is not** — which is
> `[FREEZEUNTICK-2026-08-20]`, now confirmed on this batch's own scenario rather than only on the
> helper-missing one.
>
> ⚠ **The user-visible consequence is the one the rule exists to prevent:** an accurate dialog saying
> *"nothing was frozen"* dismisses to a row that still displays as active. Anyone who dismisses the
> dialog and glances at the table afterwards is told a cheat is running when none is.


1. **⚠ REGRESSION FIRST — a normal freeze still works and still closes.** Property Search → a numeric
   field on a class with live instances → Copy Freeze Script → paste into CE → tick. The value must
   hold, the Lua Engine window must **close**, and the record must stay ticked. Everything below
   changed this path.
2. **The hard failure — the whole point.** Tick the same script with **UE5Dumper.dll NOT injected**.
   Expect: a `showMessage` naming the reason, the record **unticked by itself**, and the Lua window
   **still open**. Before this it silently reported success, closed the window, and stayed ticked.
3. **⚠ The legitimate empty case must NOT untick.** Freeze a class with **zero live instances right
   now** (an enemy type not yet spawned). Expect: record **stays ticked**, window **stays open**, and
   one line — `[Freeze] armed: no live instances of X right now`. Then make one spawn and confirm the
   freeze takes hold within ~5 s. **If this unticks, the fix broke the feature and that is worse than
   the bug** — report it.
4. ✅ **CLOSED 2026-08-24 `[AA12-STEP4-TYPO-2026-08-24]` — offline, no CE.** *(original text: a
   misspelled class is indistinguishable from (3), by design. Edit `CFG.className` to nonsense and
   tick. It must behave exactly like step 3 — armed, 0. The DLL answers `SetDone(0)` for both, so
   claiming a typo would be a guess. Confirm it does not claim one.)*

   ⭐ **"Indistinguishable by design" is a STRUCTURAL property, so it is provable on the emitted
   text.** A typo and an empty class are the same input to the script — `HandleListInstances`
   answers `SetDone(0)` for both — so there is one `scount == 0` branch and both reach it. That
   makes every part of the step checkable without ticking anything:

   | assertion | how it is now held |
   |---|---|
   | armed, 0 — the message | `AnEmptyOrMisspelledClass_IsReportedAsArmed_AndNeverAsATypo` (NEW) |
   | **does not claim a typo** | same test — a 9-word vocabulary sweep, case-insensitive |
   | record stays ticked, window stays open | `Generate_ArmedButEmpty_DoesNotUntick_AndKeepsTheWindowOpen` — ⚠ **already existed**; only the typo half was unguarded |

   ⚠ **The gap was real and was worth closing.** `ue5_freeze_helper.lua` states the rule *at the
   implementation* — *"claiming a typo would be a guess, which is the thing CLAUDE.md's mailbox rule
   forbids"* — and **nothing enforced it**. A well-meaning *"class not found — check the spelling"*
   could be added to the armed message and every one of the 4,716 tests would have stayed green.

   ⚠ **Both directions negative-controlled**, because an absence check that cannot fail is not a
   check (working-lessons §2.10):

   | control | armed by | result |
   |---|---|---|
   | the guard bites | append *"or the class name has a typo — check the spelling"* to the armed message | the new test fails, naming the offending word |
   | the channel is real | delete the `elseif scount == 0 then` branch | the **channel** assertion fails first — so the vocabulary sweep can never pass over a script that simply has no zero branch |

   Both reverted; `FreezeScriptGenerator.cs` byte-identical to HEAD. Suite 4,717 green,
   `freeze_helper_test.lua` 159 checks / 0 failures.

   ℹ️ What is still not proven, stated plainly: that **CE** renders it this way. The script's text
   and the helper's behaviour are pinned; the pixels are not. Step 1's regression run already
   covers the rendering path for the non-empty case.
5. **An OLD helper is reported as unknown, not as a verdict.** Embed a **pre-1.2** `ue5_freeze_helper.lua`
   (any copy from before build 3125) and tick a newly generated script. Expect the "older
   ue5_freeze_helper.lua … re-inject it" line, the window left open, and the record **left ticked** —
   it must neither close over it nor untick a freeze that may well be running.
6. **Two freeze scripts still coexist.** Tick two different freezes at once, untick one: the other
   must keep working. The keyed-handle table is untouched by this change, and this is the check that
   proves it.

### ⬜ NEW 2026-08-17 — G12 / G3: the offset family, and the apply_rescan gate

*Needs the DLL injected. See dev-log builds 3119 / 3121. G12's invariant is unit-pinned; its WIRING
is not, because no test target compiles `Genau.cpp` or `Ubel.cpp`.*

1. **⚠ G12 REGRESSION — enums and TArray elements still read correctly.** Open Live Walker on a
   class with an **enum** field and a **TArray** field. The enum must show its member NAME (not a
   raw int) and the array must show its element type. All four writers of the family moved; this is
   the check that they still agree.
   **🟡 TArray half ✅, enum half still open `[DSA-2026-08-16]`.** The session walked arrays cleanly
   — `{"array_elem_size":8,"array_inner_addr":"0x1B59CB7FD80","array_inner_type":"ObjectProperty",
   …,"name":"ModelComponents","type":"ArrayProperty"}` — so `FARRAYPROP_INNER` is not 8 bytes off.
   **Zero `EnumProperty` appeared in the entire session**, so `FENUMPROP_ENUM` / `FBYTEPROP_ENUM`
   are untested. Pick a class with an enum field next time; that is the half that can still be wrong.
   **✅ BOTH HALVES NOW DONE `[G12-PIPE-2026-08-17]`** — DumperTest Development, build 3262, via
   `walk_instance` over the pipe (never `walk_class`, and without `lean`, so these are real per-object
   reads). **The enum half is covered by four fields across BOTH writers**, which is what makes it
   evidence rather than one lucky lookup:
   `Grade` → `EDumperTestGrade::Elite` and `UpdateOverlapsMethodDuringLevelStreaming` →
   `EActorUpdateOverlapsMethod::UseConfigDefault` exercise `FENUMPROP_ENUM`; `RemoteRole` →
   `ROLE_None` and `NetDormancy` → `DORM_Awake` exercise `FBYTEPROP_ENUM`. `Grade` is the
   discriminating one: the sample's `EDumperTestGrade` has a **hole at 3..6** (`Legend`=7), so a
   build that confused index with value could not land on `Elite` by accident — and `Elite` is the
   value `tools/ue-sample/README.md` documents in advance.
   TArray regression re-confirmed on the same reply: `Arr_Int` inner `IntProperty`/4,
   `Arr_Struct` inner `StructProperty`/**32** (FName 8 + int 4 + pad 4 + FText 16), `Tags` and
   `Layers` `NameProperty`/8.
2. **G12, the case it actually fixes.** Needs a title whose offset validation takes the **heuristic
   fallback** — `scan-0.log` / `offsets-0.log` shows `Cannot find Guid or Vector struct`. Solarpunk
   is the documented one (though a later build resolved via `Guid` instead, so it may not reproduce).
   On such a title, enum names and TArray inner types were previously read 8 bytes off. Confirm they
   are right now, and **record which branch the log shows** — a run that resolved via `Guid` did not
   exercise this.
   ✅ **CLOSED 2026-08-23 `[G12S2-STAGE-2026-08-23]` — the fallback was STAGED, and it reproduces
   the validated path exactly.** The row waits on *"a title whose offset validation takes the
   heuristic fallback"*, and no such title exists here — every one resolves via `Guid`. Staging the
   condition is what makes it runnable: one inserted line in `Genau.cpp`,
   `guidStruct = vectorStruct = 0;`, placed **after** the two `FindStructByName` calls so they still
   run.
   ⭐⭐ **That placement is the positive control, and it fired:**
   ```
   FindStructByName: Found 'Guid'   at 0x1DF1A5C9280 (index=4118)
   FindStructByName: Found 'Vector' at 0x1DF1A5C9100 (index=4124)
   ValidateAndFixOffsets: Cannot find Guid or Vector struct — trying heuristic fallback
   ```
   The structs **were** found and the fallback was taken anyway — so this is provably the staged
   branch, and every offset below came from the heuristic rather than from Guid probing.
   ⭐ **The oracle is pre-published ground truth, not self-consistency** — the same eight fields
   `[G12-PIPE-2026-08-17]` recorded from the *validated* path on build 3262. All eight match:
   | | expected | got |
   |---|---|---|
   | `Grade` (FENUMPROP) | `EDumperTestGrade::Elite` | ✅ |
   | `UpdateOverlapsMethodDuringLevelStreaming` | `EActorUpdateOverlapsMethod::UseConfigDefault` | ✅ |
   | `RemoteRole` / `NetDormancy` (FBYTEPROP) | `ROLE_None` / `DORM_Awake` | ✅ |
   | `Arr_Int` / `Arr_Struct` / `Tags` / `Layers` | `IntProperty`/4, `StructProperty`/**32**, `NameProperty`/8 ×2 | ✅ |
   `Grade` is the discriminating one: `EDumperTestGrade` has a **hole at 3..6** (`Legend`=7), so a
   build confusing index with value could not land on `Elite` by accident.
   ⚠ **Revert verified in the BINARY.** `Genau.cpp` is `i/lf **w/lf**`, so `git checkout` would have
   silently rewritten every line ending while `git status` stayed clean — reverted from a byte
   snapshot instead (identical, LF 5372, 0 NULs). After the rebuild the log reads
   `ValidateAndFixOffsets: **Using struct 'Guid'**` with no fallback line, which is the proof the
   stage is gone from what ships (`dist/` is gitignored, so a clean tree proves nothing).

   **⬜ (original) Branch recorded, and it is the WRONG one `[DSA-2026-08-16]`:** `FindStructByName: Found
   'Guid' at 0x1B5FB6840C0` → `ValidateAndFixOffsets: Using struct 'Guid'`, i.e. the validated path,
   with `FStructProp::Struct = +0x70` published from a real measurement. The Step 2.5 default block

> ### ⛔ NO FIXTURE EXISTS — swept ALL 19 HOSTS 2026-08-20 `[G12S2-SWEEP-2026-08-20]`
>
> The note above generalised from one host. Grepping every log folder on the machine settles it:
>
> | | |
> |---|---|
> | hosts with `ValidateAndFixOffsets: Using struct 'Guid'` | **19 of 19** |
> | hosts with `Cannot find Guid or Vector struct` (the heuristic branch) | **0** |
>
> Avowed · DQ7R · DQI&II HD2D · DSClient · DumperTest (×2) · ES2 · Echoes of Aincrad · Elliot ·
> **Satisfactory** · Geri · LightMaze · Lushfoil · Manor Lords · OCTOPATH · RSG · SEED · ST Voyager ·
> **Solarpunk** — every one takes the validated `Guid` path. Solarpunk is *the documented
> heuristic-fallback title* and it does not reproduce, which the note above had already found on its
> own; this extends that from a single observation to the whole installed corpus.
>
> ⚠ **The control is in the same data**: the string `Using struct '…'` is demonstrably logged (19
> times), so the zero for the fallback is about the branch, not about the grep.
>
> ⇒ **Step 2 is a 第 5 步 item — no sample exists anywhere**, not an untried one. The Step 2.5 default
> block G12 repaired cannot be entered by any title installed here, so the repair stays unexercised
> and there is no action that would change that short of acquiring a title whose `Guid` *and* `Vector`
> struct lookups both fail.
>
> 🔗 **Third item in this batch-family to land on "no fixture exists"** alongside `G11` step 4
> (Tier 3 never reached) and `G3` steps 3+4 (no unresolved-globals title). Worth reading together:
> several remaining register rows are waiting on engine states this corpus simply does not contain.

   G12 repaired was never entered. Still needs a heuristic-fallback title.
3. **⚠ G3 REGRESSION — Extra Scan → Apply still works.** Needs a game where something is missing to
   scan for (all 34 tested games resolve GWorld, so this may not be reachable). If it is: press
   Extra Scan, then Apply, and confirm `offsets-0.log` still contains exactly **one**
   `ValidateAndFixOffsets: Starting` line — the gate's whole purpose.
4. **⚠ G3 REGRESSION — GEngine still resolves after an Apply.** The GEngine second pass was hoisted
   out of the gated block precisely so it keeps running. If Apply is reachable, confirm
   `apply_rescan: Applied GEngine=0x…` still appears when GEngine was previously unresolved.

> ### ⛔ STEPS 3 + 4 ATTEMPTED 2026-08-20 AND THE RUN IS VOID `[G3-VOID-2026-08-20]` — the host never booted
>
> Satisfactory was chosen because a log-folder survey showed it as the **only** host on this machine
> with an unresolved global (`FactoryGameSteam-Win64-Shipping: UE506, GObjects=0x0, Objects=0`, where
> every other title resolves both). It was launched by running its shipping exe directly, injected,
> and driven headless with `tools/verify/g3_rescan_apply.py`.
>
> ⛔ **The game had put up a modal error dialog and never initialised its engine:**
> *"Failed to open descriptor file `../../../FactoryGameSteam/FactoryGameSteam.uproject`"*. UE
> resolves the `.uproject` **relative to the exe**, and this title's exe lives in
> `Engine\Binaries\Win64\`, so that path does not exist. **Satisfactory must be started through
> Steam.** Every number below is therefore about a dead engine and **none of it is evidence**:
> ```
> unresolved: ['gobjects', 'gengine']      GNames + GWorld DID resolve
> TrySymbolExport: Found '?GUObjectArray@@3VFUObjectArray@@A'   <- the symbol was found
> ValidateGObjects: Failed at 0x7FFCC7CE3620 (Num@+14=0, Num@+04=-1, Num@+1C=0)   <- array EMPTY
> ExtraScanGObjects: No valid FUObjectArray found (763 candidates tested)
> ```
>
> ⚠ **The contradiction that should have caught it was already in our own docs.**
> [test-games.md](test-games.md) records this exact title and engine (v1.2.3.1, UE 5.6) resolving
> **all three globals via symbol export, 217,602 objects**. A host that had "regressed" to zero
> deserved suspicion before belief. The tell in the log is that the symbol **resolved** and only the
> *counts* were empty — a wrong address gives garbage counts, not zeros. Full write-up:
> [working-lessons.md](working-lessons.md) §3.w.
>
> ⇒ **Steps 3 and 4 remain unrun**, and worse, the premise that picked the host is now doubtful: the
> pre-existing `GObjects=0x0` line that made Satisfactory look like the unresolved-globals title is
> plausibly the same failed-launch artefact from an earlier session. **Before re-running, launch it
> through Steam and confirm it reaches a menu with a non-zero object count**; if it resolves
> normally, then on current evidence there may be **no** unresolved-globals title on this machine and
> these two steps have no fixture at all.

> ### ✅ RE-RUN THROUGH STEAM 2026-08-20 — Satisfactory is EXONERATED, and G3 3+4 have NO fixture
>
> Relaunched with `steam.exe -applaunch 526870` (two processes, `FactoryGameSteam.exe` +
> `…-Win64-Shipping.exe` — the shape a correct Steam launch produces), left to reach a menu, then
> injected and scanned:
>
> ```
> ue=506   objects=137,425
>    gobjects = 0x7FFCC7CE3620      gnames  = 0x7FFCCC00D8C0
>    gworld   = 0x7FFCBB9CCB88      gengine = 0x7FFCBB9CF768
> ```
>
> ⭐ **`gobjects` is the EXACT address the void run's symbol export had already found and the
> validator rejected** (`ValidateGObjects: Failed at 0x7FFCC7CE3620 … Num@+04=-1`). Same address,
> same symbol, same session shape — the only difference is that the engine had actually initialised.
> That is as clean a positive control as this could have: it isolates "empty array" from "wrong
> address" by holding the address constant.
>
> ⇒ **Satisfactory resolves all four globals**, exactly as [test-games.md](test-games.md) says. It is
> **not** an unresolved-globals title, the log-folder survey that nominated it was reading a
> failed-launch artefact, and therefore:
>
> ### ✅ G3 STEPS 3 + 4 CLOSED 2026-08-23 `[G3-STAGE-2026-08-23]` — staged, because the "fixture" was a corpse
>
> ⛔ **First, the fixture claim in `tools/verify/g3_rescan_apply.py` is REFUTED.** It names
> Satisfactory as *"the only host with an unresolved global"*. Its `GObjects=0x0` readings are a
> **dead engine**, and the object count settles it — four recorded sessions of the same title:
> `07:27 GObjects=0x0 / Objects=0`, `07:34 resolved / Objects=137372`,
> `17:30 GObjects=0x0 / Objects=0`, `17:57 resolved / Objects=137425`. The shipping exe cannot be
> launched directly (a modal *"Failed to open descriptor file …uproject"* hides behind the window);
> `steam.exe -applaunch 526870` boots it and it resolves everything. `apply_rescan: Applied GEngine`
> has **never** appeared in any Satisfactory log. Following that paragraph reproduces
> `[G3-VOID-2026-08-20]`. The rig's docstring now says so at the top.
>
> ⭐ **Staged instead, and the stage is chosen to satisfy the row's own guard.** `apply_rescan` runs
> its GEngine second pass **only `if (g_cachedGEngine == 0)`** ([Fern.cpp:5159](dll/src/Fern.cpp:5159)),
> so the precondition is enforced in code. A one-shot skip in `Genau::FindGEngineSlot`, placed
> **after** the existing `bOffsetsProbeRan` deferred gate, forces the first *post-gate* resolve to
> miss: init's deferred call returns early without consuming it, init's `ResolveGEngineDeferred`
> misses, and `apply_rescan`'s call then succeeds.
>
> | | observed |
> |---|---|
> | precondition | `gengine=0x0`, `method=not_found` — genuinely unresolved at init |
> | **step 3** | `ValidateAndFixOffsets: Starting` = **1 before, 1 after** Extra Scan → Apply — Apply did not re-enter validation, which is the gate's whole purpose |
> | **step 4** | `apply_rescan: Applied GEngine=0x7FF7DFFFAAF0 (aob)`, and `get_pointers` then reports it resolved |
>
> ⚠ **Revert verified two ways.** `git checkout -- dll/src/Genau.cpp` returned the file
> **byte-identical** to the pre-stage snapshot (LF 5372, 0 CRLF) — the first revert this session
> where `git checkout` was safe to use, because `.gitattributes` now pins `eol=lf`. And the rebuilt
> DLL resolves `gengine=0x7FF7DFFFAAF0 (aob)` at **init**, proving the stage is gone from what ships
> (`dist/` is gitignored, so a clean tree proves nothing).
>
> ⛔ (superseded) **G3 steps 3 and 4 have no fixture on this machine.** Every installed title resolves everything,
> which is what the steps themselves predicted (*"all 34 tested games resolve GWorld, so this may not
> be reachable"*). They are a 第 5 步 item — no sample exists — not an untried one.

5. **✅ Free log check, no game needed beyond a normal session.** `walk-0.log` must show no burst of
   `Misaligned field … possible wrong FPROPERTY_OFFSET`. That line is the direct witness for a split
   or stale family.
   **PASS `[DSA-2026-08-16]`** — a 2.4 MB `walk-0.log` covering a full snapshot capture (35,891
   objects, **2,917,264 fields**) contains **zero** `Misaligned` lines and zero `[WARN]` lines of any
   kind. Conditions matter here, so record them: this is the *validated-`Guid`* branch (step 2), so
   it proves the family is coherent on the path that was already coherent — it is a regression check,
   not evidence for the repair.

### 🟡 GROUP 5 opened 2026-08-18 `[CE-2026-08-18]` — plugin bridge live, freeze record reaches CE

The AOBMaker CE plugin **is installed** (maintainer, 2026-08-18). With Cheat Engine 64-bit running:

* **AB1/AB2 — substantially verified.** `\\.\pipe\AOBMakerCEBridge` exists, UE5DumpUI's toolbar reads
  **`● AOBMaker Connected`** (green), and once CE attaches to a process an **`Unreal Engine` menu
  appears in CE's own menu bar** — three independent signs the plugin is loaded and talking.
* **Y9's remaining consumer — CLOSED.** With the bridge up the **Freeze button** is enabled (it is
  bound to `IsAobMakerAvailable`), opens the same dialog pre-filled `255`, rejects `9999` with
  `uint8 holds 0 to 255 — 9999 would be written as 15`, and on `200` reports
  **`Freeze script created in CE: Freeze: DumperTestActor::U8_Max = 200`**. CE's address list then
  holds that exact record as `<script>`. Y9 now has **both** consumers verified.
* **CE Lua hygiene — the bail-out shape is right.** Ticking the record before the helper was present
  produced a plain, actionable dialog — *"[Freeze] ue5_freeze_helper.lua not found in this table.
  Setup: UE5DumpUI → Tools → Inject Freeze Helper into Current CE Table"* — **and left the record
  UNTICKED**, which is CLAUDE.md's MUST for a bail-out that applied nothing. No Lua Engine window was
  left covering CE.
* **`Tools → Inject Freeze Helper into Current CE Table` works.** CE's `Table` menu then lists
  `ue5_freeze_helper.lua`, so the file really is attached to the table.

* **✅ The freeze ARMS, end to end.** The control was run: same flow on **Lushfoil**, CE re-attached
  to it, record ticked →
  ```
  [Freeze] armed: no live instances of PrimitiveComponent right now -- the freeze applies as they spawn.
  ```
  A success message that also states the *actual* state rather than implying a write happened. The
  dialog's **bool flavour** works too (`BoolProperty -> bool`, `0x272`, pre-filled `true`, hint
  *"Accepts: true / false / 1 / 0"*).

* **✅ And the earlier DumperTest failure was NOT a defect — the script diagnosed it correctly.**
  Opening CE's Lua Engine surfaced what the red ✗ meant:
  ```
  [ue5_freeze] DumperTestActor: 3 consecutive rescans failed -- freeze STOPPED writing
  (last error: the contract symbol resolved to the wrong memory (stale address) -- re-inject the DLL).
  Re-enable the record after fixing it.
  ```
  CE was still attached to a DumperTest process that had since been killed, so the registered symbol
  pointed at dead memory. **This is CLAUDE.md's "never report a mailbox failure by guessing" rule
  working**: it names the specific cause, stops writing after 3 consecutive failures instead of
  spinning, and tells the user what to do and to re-enable afterwards.
  ⚠ **Operational note that cost a diagnosis here:** the message is only visible with **CE's Lua
  Engine window open** — by design (DEBUG-gated hygiene), so a stopped freeze is silent until you
  open it. Open the Lua Engine *before* concluding anything about a record.
  ⚠⚠ **And read the checkbox correctly** (maintainer, 2026-08-18): in CE a **big red ✗ on a record's
  checkbox means ACTIVE, not failed** — an inactive record is an EMPTY box. So the red ✗ seen here
  was not a failure indicator at all; it was CE correctly reporting the record as still enabled while
  the freeze had stopped writing. That is what turned this observation into
  `[FREEZESTUCK-2026-08-18]` below.

### ✅ FIXED 2026-08-19 `[PIPEBUSY-2026-08-18]` / `[CLASSTOTAL-2026-08-18]` — both moved to "Pending live-game verification"

Both honesty defects were fixed 2026-08-19 (PIPEBUSY: at-capacity logs once, not 1 Hz forever;
CLASSTOTAL: `total_classes` now the real pool count past the row cap). The live-check writeups —
including the ⚠ "never run a second `pipe_client.py` alongside the UI" caveat, which the PIPEBUSY fix
makes non-spammy but does not repeal — are under those tags in **"Pending live-game verification"**.

### ✅ PART-FIXED 2026-08-19 `[PROXYLOAD-2026-08-17]` — screening + a real load signal (writeup moved to "Pending live-game verification")

Both **offline** halves shipped 2026-08-19: (1) import-table BYPASS screening at deploy time and in
the Suggested column, and (2) a per-game "Loaded?" column read from the log folder — so a
`DeployedCurrent` proxy that never ran is no longer silent. **The live check** (OCTOPATH warns +
shows "not observed"; DQ7R/DQ I&II confirm "loaded") is under
`[PROXYLOAD-2026-08-17]` in **"Pending live-game verification"**.

### 🟡 GROUP 7 SWEEP DONE 2026-08-19 `[SWEEP9-2026-08-19]` — nine titles, headless, one at a time

Rig: `tools/verify/title_sweep.py` (+ `proxy_refresh.py`). Every title: refresh the stale proxy →
**clear the hint entry so `DetectVersion` actually runs** → launch the shipping exe directly → wait
for the pipe → `trigger_scan` → pipe round-trip → grep `scan-0.log` → **kill and confirm dead** →
next. dist **3263** confirmed by `assert_build()` on every one.

| title | UE (detected) | tier | detect path | GObjects | GWorld | objects | classes | CPN |
|---|---|---|---|---|---|---|---|---|
| Lushfoil | 506 | 1 | PE resource | `GOBJ_ES53_1` | `GWLD_TQ_1` | 58,619 | 1,770 | false |
| Manor Lords | 505 | 1 | PE resource | `GOBJ_ES53_1` | `GWLD_SP57_1` | 80,013 | 2,919 | false |
| Solarpunk | 507 | 1 | PE resource | `GOBJ_V13` | `GWLD_SP57_1` | 120,862 | 2,706 | false |
| EVERSPACE 2 | 505 | 1 | PE resource | `GOBJ_V13` | `GWLD_ES2_1` | 79,012 | 3,052 | false |
| The Artisan of Glimmith (Geri) | 427 | 1 | PE resource | `GOBJ_ES53_1` | `GWLD_TQ_1` | 24,132 | 799 | false |
| Avowed | 503 (→504 raised) | 1 | PE resource | **`GOBJ_AV1`** | **`instance_scan_recovery`** | 92,037 | **5,102** | false |
| DQ7R | 427 | 1 | **Tier 1 (utf16)** | `GOBJ_ES53_1` | `GWLD_TQ_1` | 149,408 | 2,543 | false |
| Elliot | 427 *(fallback)* | **0** | **publisher-bias fallback** | `GOBJ_ES53_1` | `GWLD_TQ_1` | 84,990 | 3,236 | false |
| OCTOPATH TRAVELER | 418 | – | (see `[RELAUNCHPIPE]`) | `GOBJ_ES53_1` | `GWLD_TQ_1` | 273,957 | 699 | false |

**`U2` CPN screening — swept 9, ALL FALSE.** `case_preserving=false` on every title, `probe_ran=true`
on every title. That is the honest form of the null result, and it covers UE4 (418, 427) as well as
UE5 (503–507), which the row asks for. ⇒ The row's escalation ("only if the sweep returns all-false,
build UE from source with `WITH_CASE_PRESERVING_NAME=1`") is now *reached*, and it is hours of work,
so it stays a maintainer decision.

**`G11` step 3 / `G8`–`G9` — the tier ladder was entered, and Tier 2 still did not fire.**
`DQ7R` is the one that reaches it: `PE VERSIONINFO Product=1.1 File=1.1 — unrecognised` → memory
scan → `DetectVersion: Tier 1 (utf16) '++UE4+Release-4.27' -> 427 at 0x4BBC6D8`. **No
`Tier 2 Release prefix` line appeared on any of the nine.** That is the offline model's prediction
reproduced live — Tier 1 answering first and masking every Tier 2 hit — and it remains *not*
evidence that Tier 2 works.

**`G12` — the publisher-bias fallback branch is exercised, by Elliot.** Its resource is unusable
(`Product=1.2 — unrecognised`) *and* it carries no release tag, so it produces **no tier line at
all**, exactly as this register already predicted: `Could not detect UE version from PE or memory
(pre-UE4 markers 0/4, below the 2 needed)` → `UE detection failed — using publisher (SQUARE_ENIX)
bias fallback 427` → `UE Version = 427 (tier=0, detected=no, lowConfidence=yes)`.
⚠ **Elliot is really UE 5.04, so the bias picks the wrong number** — but it is honestly flagged
`detected=no, lowConfidence=yes`, and it is **harmless in practice**: the offset probe is empirical,
not version-driven, and reported `use_fproperty=true`, `item_size=24`, `validated=true`, with all
four pointers resolving. Worth knowing before anyone reads `427` on the UI's Elliot session as a bug.

**`X2` step 4 + `[CLASSTOTAL]` — the >5,000-class title exists and the wire reports it correctly.**
Avowed: `list_classes` → `total` (the page) **5000** = the default `limit`, `total_classes` **5102**,
`truncated` **true**. So the real pool total travels separately from the capped page, which is the
fix. ⚠ **Read `total_classes`, never `total`** — `total` is `results.size()` and equals the cap
exactly, which is precisely the misreading X2 is about. (This rig read `total` first and recorded
"5000 classes"; the number looked plausible and was wrong.)

**Avowed also re-confirms its documented shape**: `GOBJ_AV1`, **`item_size=20`** (the packed
`FUObjectItem`), and GWorld via **`instance_scan_recovery`** rather than a direct AOB.

⚠ **THREE DIFFERENT "UE VERSION" QUANTITIES, and confusing them manufactures a G11 false alarm.**
This cost two contradictory readings of Avowed before it was pinned down:
1. the **cached** `ueVersion` in `UE5CEDumper.<Machine>.json` — the *detected* value;
2. the **`FindAll: UE Version = N`** log line — also the detected value, and the right thing to
   compare a cache against;
3. **`get_pointers.ue_version`** — `g_cachedUEVersion`, which is the value **after any runtime
   raise**. Avowed detects 503 and then logs `property marker (CMC::GravityDirection) = UE5.4+ —
   raising version 503 -> 504`, exactly as DragonSword Awakening does, so 503 and 504 are *both
   right* for different questions.
⇒ **G11 step 1 must compare the cache against the LOG LINE.** On that basis: **6 of 8 IDENTICAL**
(Lushfoil 506, Manor Lords 505, Solarpunk 507, ES2 505, Geri 427, DQ7R 427 — and Solarpunk's is a
genuine cross-revision re-detect, its entry was still `rev=3`). The two that differ are Avowed
(cache 504 from an older run vs detected 503 + documented raise — **not** a regression) and Elliot
(504 → 427, the fallback change described above). No user override was destroyed by the clears —
every entry had `ueVersionUserOverrideAt` empty, checked before and after.

⚠ **Object counts drift by a few between runs** (Avowed 92,036 → 92,037) as the game loads; treat
small deltas as noise, not as findings.

⛔ **Two titles could NOT be swept**, recorded rather than silently skipped: **Star Trek Voyager**
exits immediately when its shipping exe is launched directly (Steam DRM wants the client), and
**EVERSPACE (RSG)** was not attempted. Both need a Steam-client launch.

> ### ✅ STVoyager SWEPT 2026-08-23 `[G11-STVOYAGER-2026-08-23]` — the DRM blocker is solved, and this is the cross-revision re-detect
>
> `"C:\Program Files (x86)\Steam\steam.exe" -applaunch 2643390` is the whole fix for *"exits
> immediately when its shipping exe is launched directly"*. ⚠ It boots slowly — well past a
> 2-minute wait — so wait on the **process**, not on a fixed timeout.
> `pe_hash 4720D6A80ABFA000`, **46,995 objects** (a genuinely booted engine, not the dead-engine
> trap), all three globals via AOB, `item_size=24`, `lowConfidence=no`.
>
> ⭐⭐ **Two runs, and the pair is the evidence** — the same title in one session, distinguished by
> the log line:
>
> | run | cache going in | `FindAll: UE Version` line | what it shows |
> |---|---|---|---|
> | 1 | `506 / rev 5` | `506 (cached, rev=5, detected=yes, lowConf=no) — **skipped DetectVersion**` | the cache-hit path |
> | 2 | **`0 / rev 1`** (primed by hand) | `506 (**tier=1**, detected=yes, lowConfidence=no, publisher=-)` | a **real re-detection** |
>
> After run 2 the cache was **rewritten `0/rev1 → 506/rev5`**, the log line and the cache **agree
> (506 = 506)** — which is the comparison this step demands — and `ueVersionUserOverrideAt` was
> empty before and after, so no user override was destroyed.
>
> ⚠ **A `trigger_scan` does NOT exercise this, and that cost a step.** Editing the on-disk cache and
> re-scanning left it at `0/rev1` untouched: the version is held in memory from process start, so
> `trigger_scan` re-scans pointers but never re-reads the version cache. **The stale-rev case needs
> a fresh process** — prime the cache, then relaunch.
>
> ### ✅ G11 step 2 PASS 2026-08-23 `[G11-AVOWED-2026-08-23]` — Avowed reports UE504, as documented, and it is NOT a defect
>
> Avowed = **appid 2457220** (from the Steam manifest; the handover table omits it). Launched via
> the Steam client, proxy refreshed first (below), `load_mode: proxy:dxgi.dll`.
>
> | field | value | matches the documented shape? |
> |---|---|---|
> | `ue_version` | **504** | ✅ exactly what this step says to expect |
> | `object_count` | **92,036** | ✅ the register's own figure (it notes 92,036 → 92,037 drift as noise) |
> | `item_size` | **20** | ✅ the packed `FUObjectItem` |
> | `gobjects_pattern_id` | **GOBJ_AV1** | ✅ |
> | `gworld_method` | **instance_scan_recovery** | ✅ not a direct AOB |
> | log line | `UE Version = 504 (cached, rev=5, detected=yes, lowConf=no)` | ✅ agrees with the cache |
>
> ⚠ **Proxy trap again, caught by the report rather than by a failure this time.** Avowed ships a
> **`dxgi.dll`** proxy (not `version` — its exe imports dxgi+winmm and not version, so dxgi is the
> deterministic choice here) and it was **STALE** at 2,891,264 B / sha `eb59beb768c3`. Refreshed to
> 3337 before launching, so the numbers above are the shipping build's. `proxy_refresh.py report`
> is the cheap pre-flight; running it *before* the launch saves the relaunch EVERSPACE cost.
>
> ### ✅ G1 + X3 screening — the NEGATIVE recorded 2026-08-23 `[G1X3-SCREEN-2026-08-23]`
>
> `get_offsets` on **Avowed**: **0** `unmeasured`, **0** `validated:false`, **1** `validated:true`.
> ⭐ Control: the token `validated` appears exactly **once** in the whole reply, so the search is
> able to find it — a zero from a broken grep would look identical otherwise.
> ⭐⭐ **Avowed is the strongest candidate on this machine** — packed 20-byte `FUObjectItem`,
> GWorld only via `instance_scan_recovery`, a licensee-shaped title. If any installed game were
> going to show a partial-offset failure it is this one, and it does not. That makes this the
> fourth sitting to find nothing; the banner's failure case still has no host here.

> ### ✅ EVERSPACE (RSG) SWEPT 2026-08-23 `[G11-RSG-2026-08-23]` — both un-swept titles are now done
>
> ⚠ **The appid was wrong in the note above.** `1128920` is **EVERSPACE 2** (already swept);
> the un-swept title is **EVERSPACE™ = 396750**. Both come from the Steam manifests
> (`appmanifest_*.acf` `"name"`), which is the reliable source — the handover's table lists only
> EVERSPACE 2.
>
> `pe_hash 5D8E2D5003601000`, **186,979 objects**, `item_size=24`, all three globals via AOB,
> `lowConfidence=no`. Cache `ueVersion=420, rev=5` vs log `FindAll: UE Version = 420 (cached,
> rev=5, detected=yes, lowConf=no)` — **agree (420 = 420)**, `ueVersionUserOverrideAt` empty.
>
> ⭐⭐ **A deployed proxy was serving the pipe and `assert_build` caught it** — the first attempt
> failed with *"the DLL answering the pipe reports build '3263', but dist is '3337'"*. EVERSPACE
> ships our `VERSION.dll` in `RSG\Binaries\Win64\`, it auto-loads and **owns the pipe**, so an
> `inject.py` of the current build would have been measuring a four-day-old DLL. `proxy_refresh.py
> refresh "EVERSPACE"` updated both EVERSPACE titles (backups taken); the relaunch then reported
> **`load_mode: proxy:version.dll`**, i.e. the refreshed proxy served the session.
> ℹ️ The backed-up proxy hashed **`418b8bb9f82d`** — the same sha the Lushfoil investigation found,
> confirming every deployed proxy on this machine was the one 3263 build.
> ⭐ It is also a live confirmation of the version-proxy path on a title whose exe **statically
> imports** `version.dll` (RSG is one of only four such titles here) — the deterministic case, as
> opposed to Lushfoil's run-time `LoadLibrary`.
>
> ### ⛔ Z8's positive case: EVERSPACE does NOT satisfy it, and object count is the wrong proxy
>
> With the biggest object pool on this machine (**186,979**), EVERSPACE returns only
> **11,197 UFunctions** (`game_only=false`; 7,233 with it on) — barely a tenth of Z8's
> >100,000 threshold, and `truncated=false` at both settings. **Object count is not a proxy for
> function count**, so "find a big game" is not the search: Z8's UI half still needs a genuinely
> SEED/FF7R-class *function* pool. Recorded as a negative so the next session does not re-try the
> largest-object-pool title.

### 🟡 STEPS 4+5 CLOSED 2026-08-18 — G2: the version sweep is ~29 s faster, and must still be RIGHT

*Needs the DLL injected. See dev-log builds 3086 / 3088. The 29 new C++ assertions pin the rewrite
against a naive oracle; what they cannot pin is that it still reads a REAL image correctly, because
no test target compiles `Genau.cpp`.*

1. **✅ THE ONLY CONTROL THAT MATTERS — same answer, not just a faster one.** On a title whose PE
   version resource is stripped (Elliot is the documented one; a game that detects from Tier 1 exits
   early and measures nothing), **first delete that game's record from
   `%LOCALAPPDATA%\UE5CEDumper\UE5CEDumper.{Machine}.json`** — otherwise the run takes the
   `"skipped DetectVersion"` branch. Note `ueVersion` / `versionDetected` / `lowConfidence` before
   deleting, then re-scan and confirm the values written back are **identical**. A fast-and-wrong
   detection passes step 2 and fails only this one.
   **PASS `[ELLIOT-2026-08-16]`** — the 20:12 run was `scan #1`, i.e. genuinely cold, and the
   rewritten sweep ran end to end: `PE VERSIONINFO Product=1.2 File=1.2 — unrecognised` →
   `PE resource failed, falling back to memory string scan` → `Could not detect UE version from PE
   or memory (pre-UE4 markers 0/4, below the 2 needed)` → `UE Version = 427 (tier=0, detected=no,
   lowConfidence=yes, publisher=SQUARE_ENIX)`, reconciled by `UE5_Init` to **UE504**. Identical to
   test-games.md's record. **This is also the first real evidence G2's rewritten sweep executes
   correctly on a live image** — it is the branch DSA never entered.
2. **🟡 The speed, with its conditions — MEASURED, and it does NOT meet this batch's own prediction.**
   In `Logs\<proc>\scan-0.log`, measure the timestamp delta from
   `"DetectVersion: PE resource failed, falling back to memory string scan"` to the next `SCAN:Ver`
   line. Expect sub-second where it was tens of seconds. **Record the game and its image size** — a
   duration without those is not a measurement.
   **`[ELLIOT-2026-08-16]`: 20:12:37.431 → 20:12:39.831 = 2.400 s.** Conditions:
   `Elliot-Win64-Shipping.exe`, **482,390,784 bytes (460 MB)**, build 3122, warm page cache (third
   launch of the evening). So the fix unquestionably took — this was *tens of seconds* — but 2.4 s is
   **~7× the 0.35 s the dev-log claims for G2**, and the batch predicted "sub-second".
   **LEAD, not yet a finding:** that interval does not contain only the version needle. The terminal
   line reports `pre-UE4 markers 0/4`, so `CountPreUE4Markers` — a *separate* whole-image sweep added
   by the pre-UE4 refusal work — is inside the same window and may not have been gated the way the
   version needle was. Before filing anything, split the measurement: add or find a `SCAN:Ver` line
   between the two sweeps, or re-measure on a title where the pre-UE4 check exits early. Do **not**
   record "G2 is slower than claimed" until the two are separated — the 0.35 s figure may simply have
   been measured on a much smaller image, which would make this no defect at all.

   **✅ LEAD RESOLVED `[DQ7R-PIPE-2026-08-17]` — NO DEFECT, and it took no instrumentation.** The
   lead's own second option was taken: re-measure on a title where the pre-UE4 check exits early.
   `CountPreUE4Markers` is reached **only** in `DetectVersionDetailed`'s terminal all-failed branch,
   so any title that produces a tier hit keeps that second sweep out of the window by construction.
   DQ7R is such a title (see G8/G9 step 3 for how it was found):

   | | Elliot | DQ7R |
   |---|---|---|
   | window `PE resource failed` → next `SCAN:Ver` | **2.400 s** | **0.316 s** |
   | contains `CountPreUE4Markers`? | **yes** (`markers 0/4`, no tier hit) | **no** (Tier 1 hit) |
   | image | 482,390,784 B (460 MiB) | 103,878,656 B (99 MiB) |
   | bytes the needle actually covered | full image, ×2 flavours | ascii full + utf16 to the hit at `0x4BBC6D8` = 79,415,000 B |
   | build | 3122 | 3262 |

   ⛔ **A first pass here concluded "the lead is REFUTED". That was over-claimed and is WITHDRAWN.**
   It rested on extrapolating DQ7R's per-byte rate onto Elliot's image, which assumed the rate is
   stable. **A second Tier-1 title was then measured and it is not:**

   | title | bytes the needle covered | window | implied rate |
   |---|---|---|---|
   | DQ7R | 79,415,000 (hit at `0x4BBC6D8`) = 75.7 MiB | 0.316 s | **240 MiB/s** |
   | DQ I&II HD-2D | 70,213,424 (hit at `0x42F5F30`) = 67.0 MiB | **0.114 s** | **587 MiB/s** |

   **2.4× apart on two images of the same order.** Extrapolated onto Elliot's 460 MiB that spans
   0.78 s – 1.9 s of the 2.400 s window, so the needle is somewhere between **33% and 80%** of it.
   The fast end makes `CountPreUE4Markers` the *dominant* term — i.e. it **supports** the lead the
   first pass claimed to refute. **Two points, two opposite conclusions: the extrapolation cannot
   decide this and must not be used to.**

   **What IS established, and it is worth having:**
   * The needle-only window is directly measurable, and it is **small** — 0.114 s and 0.316 s over
     ~67–76 MiB — on any title where a tier hit keeps the marker sweep out of the window by
     construction. That part of G2's rewrite demonstrably works on live images.
   * The dev-log's 0.35 s sits inside that measured range for ~100 MB-class images, so the figure is
     **image-size-specific rather than wrong**.
   * ⚠ **Per-byte scan rate on this machine varies by 2.4× run to run**, which is itself the finding:
     it makes *any* cross-title extrapolation of scan cost unsound, here and in future batches.

   **So step 2 stays 🟡 and the only decisive route is the lead's FIRST option: instrument.** Add one
   `SCAN:Ver` line between the version needle and `CountPreUE4Markers` and re-measure **Elliot
   itself** — nothing measured on a smaller title can settle what fraction of Elliot's 2.4 s belongs
   to which sweep. Do **not** file "G2 is slower than claimed" either; both directions are currently
   unsupported.

   ⚠ **Conditions:** single run per title, warm page cache, mixed builds (Elliot 3122 vs DQ7R/DQ I&II
   3262), and the Elliot row is quoted from `[ELLIOT-2026-08-16]` rather than re-measured. OCTOPATH
   would have been the third point but **cannot be measured at all** — its `version.dll` proxy never
   loads; see the silent-proxy finding below.
3. **⚠ REGRESSION — a Tier 1 game still detects from Tier 1.** ⛔ **"Any ordinary UE5 title" is
   WRONG and is what made this step look runnable** — an ordinary UE5 title resolves at Tier **0**
   and never reaches Tier 1. Screen candidates with `tools/verify/pe_version_probe.py` first; see
   `[G2-TIER0-SWEEP-2026-08-18]`. Confirm
   `scan-0.log` still shows `DetectVersion: Tier 1 (ascii|utf16) '++UEx+Release-N.N' -> NNN`. The log
   lines were kept byte-identical on purpose, so any wording change here is itself a defect.
4. **The three new cancel points actually fire.** Proxy mode: start a scan from the UI, close the UI
   mid-scan, and confirm `scan-0.log` carries one of the new `aborted (client gone / shutdown)` lines
   (`DataScanGObjectsCandidates` / `FindGObjectsStaticStruct` / `FindGNamesByStringRef`) rather than
   the sweep running to completion. These are **compiled but unexercised** — do not read a pass on
   steps 1–3 as covering them.
5. **⚠ REGRESSION — recovery still runs on a healthy game.** The polls honour the client-disconnect
   latch, so a stale one would abort recovery at offset 0. Connect the UI, disconnect it mid-command,
   reconnect, and confirm a fresh scan still resolves GObjects/GNames normally and that **no**
   `aborted` line appears. This is exactly what `Tot::ResetPerCommand()` in `AutoStartWork` is for.

> ### ✅ STEPS 4 + 5 CLOSED, STEP 3 PARTIAL `[G2-ELLIOT-2026-08-18]` — and step 4's PRESCRIBED VEHICLE IS REFUTED
>
> Elliot, dxgi proxy, **DLL build 1.0.0.3262**, proxy mode confirmed by
> `DllMain ProxyStart: proxy DLL mode — starting pipe server only (no scan)`.
>
> **Step 3 — 🟡 PARTIAL.** The wording is confirmed byte-exact against the source: the format string
> at `Genau.cpp:3047` is `"DetectVersion: Tier 1 (%s) '%s%s' -> %u at 0x%zX"`, and build-3262 logs
> carry `DetectVersion: Tier 1 (utf16) '++UE4+Release-4.27' -> 427 at 0x4BBC6D8` (DQ7R) and
> `... at 0x42F5F30` (DQ I&II). ⚠ **But a machine-wide sweep of every log found exactly TWO Tier-1
> lines, both `utf16` and both UE4** — the `ascii` flavour and the **UE5** branch of `'%s%s'` are
> still unwitnessed at 3262. The obvious host, **Lushfoil, cannot supply one without a cache drop**:
> it logs `UE Version = 506 (cached, rev=5, detected=yes, lowConf=no) — skipped DetectVersion`.
> `tools/verify/cold_detect.py drop 998ED2850957D000 --apply` is the one-line unblock (it was
> refused by this session's command classifier, not by anything in the repo).
> **The cache drop was then DONE, and it settles what step 3 actually needs — it is not Lushfoil.**
> `cold_detect.py drop 998ED2850957D000 --apply` worked, the cold sweep ran
> (`DetectVersion: Attempting to detect UE version...`), and Lushfoil resolved at the FIRST stage:
> `DetectVersion: PE VERSIONINFO -> UE 5.6 -> 506` -> `UE Version = 506 (tier=1, detected=yes,
> lowConfidence=no)`. Its PE version resource is **intact**, so it exits before the memory-string
> needle and **structurally cannot** emit `DetectVersion: Tier 1 (…) '++UE5+Release-5.6'`.
> ⇒ **Step 3's UE5 branch needs a UE5 title whose PE version resource is stripped or unrecognised**
> (Elliot is the documented stripped title but is UE4-era). Candidates to try, all UE5 and already in
> the cache: TQ2 (507), Solarpunk (507), Manor Lords (505), ES2 (505), STVoyager (506).
> *Incidentally this re-ran G2 step 1's control on a second title and it passed:* the record was
> rewritten **identically** (`ueVersion=506 versionDetected=True versionDetectRev=5`).
> ⚠ The maintainer has confirmed the whole cache file is **disposable** — it only speeds up a second
> load — so a cold-detect row may drop a record, or the file, without ceremony.
>
> ### ⛔ STEP 3 IS AS CLOSED AS IT CAN GET — the UE5 branch has NO HOST, and all five candidates are REFUTED
> `[G2-TIER0-SWEEP-2026-08-18]`
>
> Asked to run step 3 on **Solarpunk**. It cannot: its `VS_FIXEDFILEINFO.dwProductVersionMS` is
> **5.7.1.0**, so `Genau::DetectVersionFromPEResource`'s very first test (`major==5 && minor<=9`)
> returns **507** and the ladder exits at `PE VERSIONINFO` — the same mechanism that ruled out
> Lushfoil. ⚠ Its *string* `ProductVersion` is the placeholder `"UE5-CL-0"`, which **looks**
> unrecognisable and is why this title read as a candidate; the strings are only consulted at stage 3,
> long after the fixed-info block has already decided.
>
> **So the question was settled offline for every binary on this machine, not one title at a time.**
> ⚠ **CORRECTED — the first pass of this sweep was WRONG and the error is the interesting part.**
> It globbed a fixed depth (`common/*/*/Binaries/Win64/*-Win64-Shipping.exe`) and so silently
> skipped **Avowed**, **Echoes of Aincrad Demo** (nested one level deeper / installed mid-session)
> and **SEED BATTLE DESTINY REMASTERED** (its exe is not named `*-Win64-Shipping.exe` at all).
> **An absence claim built on a glob that can silently skip files is worthless** — the corrected
> tool, `tools/verify/tier1_host_survey.py`, walks every `Binaries\Win64` directory instead.
>
> It also reports **two independent facts**, because either alone misleads: whether the title falls
> THROUGH Tier 0, **and** whether the `++UEn+Release-N.N` needle actually exists in the image, in
> which encoding. Validated on a known positive first — DQ7R yields `utf16 ++UE4+Release-4.27`,
> exactly the line its log carries — so a "no needle" result is a real negative, not a dead detector.
>
> **18 installed binaries; only THREE can produce a Tier-1 line, and all three are UE4:**
>
> | title | ProductVersion | needle | note |
> |---|---|---|---|
> | DQ7R | 1.1.1.0 | `utf16` 4.27 | the already-witnessed line |
> | DQ I&II HD-2D | 1.0.2.0 | `utf16` 4.27 | same flavour |
> | **OCTOPATH TRAVELER** | 1.0.0.1 | **`ascii`** + utf16 4.18 | ⭐ **the only `ascii` host on this machine** |
>
> **Falls through but has NO needle → detects nothing:** Elliot (1.2.0.0) and Echoes of Aincrad Demo
> (1.0.1.27081). ⚠ Both are **UE 5.4**, not UE4 — an earlier revision of this block said "all UE4-era"
> and that was wrong. They are exactly the shape step 3 wants (UE5 + falls through Tier 0) and still
> cannot serve it, because the needle is absent from the image.
>
> **Every UE5 title either exits at Tier 0 or has no needle**, and the split is instructive:
> Light Maze (5.0), Lushfoil (5.6) and Manor Lords (5.5) **do** carry `++UE5+Release-` but resolve at
> Tier 0 and never look; Solarpunk, TQ2, ES2, STVoyager, Satisfactory, DSA and Avowed carry **no
> needle at all**. So the required host — falls through Tier 0 **and** carries a UE5 needle — does
> not exist here, and newer UE5 shipping builds appear to strip the string entirely.
> Our own packaged reference builds do not help either: UE writes the ENGINE version by default, so
> 5.3/5.4/5.7/5.8 and DumperTest all resolve at Tier 0. It is games that *override* it with a product
> version (1.x) that fall through — the opposite of the intuition that a stock-built sample would be
> generic.
>
> ⇒ **The UE5 branch is unverifiable on this inventory** and no supported switch skips Tier 0
> (`cold_detect.py drop` only clears the cache; it re-runs the same ladder).
> ⇒ **The `ascii` branch IS reachable — via OCTOPATH**, which was previously written off as
> "proxy never loads". The maintainer supplied the missing piece (2026-08-18): it needs the
> **`winmm.dll`** proxy; `version.dll` and `dxgi.dll` do not load in it. That is a concrete,
> runnable next step rather than a blocked one.
> Corroborated from the other side: a fresh sweep of every log still finds exactly **two** Tier-1
> lines, both `utf16`, both `++UE4+Release-4.27`.
>
> **The rest of step 3 stands as already recorded:** the wording is byte-exact against
> `Genau.cpp:3047`, and the regression it guards is witnessed on the UE4/`utf16` path.

> ### ✅ THE `ascii` BRANCH IS NOW WITNESSED `[OCTOPATH-G2T3-2026-08-18]` — and OCTOPATH is a working host again
>
> **OCTOPATH TRAVELER**, UE **4.18**, DLL **1.0.0.3262**, **`winmm.dll` proxy**. The offline survey
> predicted `ascii` for this title; the DLL then reported `ascii`. Prediction first, confirmation by
> an independent method second:
>
> ```
> DetectVersion: PE VERSIONINFO Product=1.0 File=1.0 — unrecognised
> DetectVersion: PE resource failed, falling back to memory string scan
> DetectVersion: Tier 1 (ascii) '++UE4+Release-4.18' -> 418 at 0x1C06AB0
> FindAll: UE Version = 418 (tier=1, detected=yes, lowConfidence=yes, publisher=SQUARE_ENIX)
> ```
>
> That is the **`ascii` flavour of `'%s%s'`**, unwitnessed since the format string shipped, and it
> also re-confirms step 3's regression (a Tier-1 game still detects from Tier 1) on a second engine
> generation. ⇒ **Of step 3's four combinations, three are now closed** (`utf16`+UE4, `ascii`+UE4,
> and the Tier-0 exit); only the **UE5** branch remains, still hostless for the reasons above.
>
> ### ⭐ THE PROXY FOR OCTOPATH IS `winmm.dll` — `version.dll` and `dxgi.dll` do NOT work
> Supplied by the maintainer and verified end-to-end. This retires a blocker recorded in three
> places (§"could not be swept at all", the G2 rate-sweep drop-out, and the deferred dxgi item):
> * `winmm proxy: lazily forwarded 180/180 exports to real System32 winmm.dll` → `pipe server started`
> * full scan clean: GObjects/GNames/GWorld/**GEngine** all `aob`, **273,956 objects**, and
>   **0 `[ERROR]` across all five log files**.
> ⚠ **The measured fact and the mechanism are separate, and only the first is established.** The exe
> **statically imports BOTH `VERSION.dll` and `WINMM.dll`** (verified by parsing its import table),
> yet the game-folder `winmm.dll` loads while the loaded `VERSION.dll` is **System32's**. A KnownDLLs
> explanation was tested and **refuted** — `version.dll` is not in the KnownDLLs list and no KnownDLL
> imports it. **So "why version.dll loses" is NOT established; do not publish a cause for it.**
> What is safe to rely on: for this title, use `winmm`.
> ⚠ The stale `version.dll` proxy was removed from the game folder (to the Recycle Bin, recoverable).
>
> ### ➕ A THIRD DATA POINT FOR STEP 2, free from the same run
> `20:04:35.123` (fallback begins) → `20:04:35.154` (Tier-1 hit at `0x1C06AB0`) = **31 ms** to reach
> ~28.0 MiB, i.e. **~900 MiB/s**. Against the two existing points — DQ7R 240 MiB/s, DQ I&II 587 MiB/s
> — the spread widens from 2.4× to **3.8×**. That **strengthens** step 2's existing conclusion rather
> than settling it: per-byte scan rate on this machine varies far too much for any cross-title
> extrapolation to decide what fraction of Elliot's 2.4 s belongs to which sweep. Same conditions
> caveat as before (single run, warm cache, early-exit path).
>
> **Step 4 — ✅ THE LINES FIRE, but NOT by the route this row prescribes.** Witnessed on 3262, in
> `Logs/Elliot-Win64-Shipping/scan-20260818-13*.log`: **`DataScanGObjectsCandidates: aborted (client
> gone / shutdown)`** (×1) and **`FindGNamesByStringRef: aborted (client gone / shutdown)`** (×3).
> `FindGObjectsStaticStruct` remains unwitnessed, so this is 2 of 3.
>
> ⛔ **"Close the UI mid-scan" cannot produce them, and this is structural, not luck:**
> * `Tot::RequestPerCommand()` has exactly one caller — `Fern::MonitorLoop`, which peeks **only**
>   connections whose `inFlight` flag is set (`Fern.cpp:804-865`, and the `inFlight` mark at `:1087`).
> * `trigger_scan` **returns immediately** and does the work on a detached `std::thread`
>   (`Fern.cpp:4983-5008` -> `RunScan` -> `UE5_Init`). No command is in flight while the scan runs, so
>   a client vanishing during it is never even peeked.
> * `rescan` is async too (`Fern.cpp:4840`) **and cannot reach these functions at all** —
>   `RunRescanBody` calls `Genau::ExtraScanGObjects/GWorld`, which are different functions.
> * `Genau::FindAll` has ONE caller in the whole tree (`Frieren.cpp:155`, `UE5_Init`).
>
> ▶ **What actually fired them is the SHUTDOWN half of the same flag**, and the logs say so directly:
> `13:34:46.459 UE5_Shutdown: Cleaning up...` -> `PipeServer: Stop entry (conns=2)` ->
> `13:34:47.313 UE5_Init: scan was cancelled (shutdown) — results are partial, NOT latching
> initialized so the next enable re-scans`. The cancel was **already latched when the scan began**,
> which the log shows unambiguously as `AOB scan CANCELLED after 0/7 batches` (GObjects) and
> `0/4 batches` (GNames) — every poll bailed on its first check, in the same millisecond.
> Incidentally this is Tot.h's stated purpose #1 working: `Stop watches+scan joins done (852 ms)`.
> **Rewrite the step to say "shut the DLL down while a scan is in flight" (disable the CE script /
> close the game), not "close the UI".**
>
> **Step 5 — ✅ PASS.** Staged with a client that kills itself mid-command, because the window cannot
> be hit by hand: a *second* process cannot be aimed (a tool round trip is seconds) and the obvious
> long commands are not long DLL-side — `list_all_functions` is **634 ms**, `search_properties`
> query="e" over 355,949 objects is **307 ms** server-side (its 2-minute wall clock was the Python
> client formatting 14,902 results), and `begin_value_scan` finished inside 0.5 s. Arming the kill
> **inside** the client on a 200 ms timer after the write produced it first try:
> `15:08:14.037 Received: begin_value_scan` -> `15:08:14.434 PipeServer: client gone mid-command
> (err=109) — aborting in-flight op` (109 = `ERROR_BROKEN_PIPE`) -> `Failed to write response`.
> ⚠ **The latch then cleared ITSELF 30 ms later** — `per-command cancel cleared — no connection that
> raised it is still live` — i.e. `ReevaluatePerCommandCancel` (audit #5 F2) retires it when the
> raising connection is removed, **without needing the reconnect this step assumes**. The UI stayed
> connected throughout and was unaffected. It was then disconnected and reconnected anyway
> (`Connected — UE504 (355949 objects)`) and a fresh scan run: **`grep -c aborted scan-0.log` = 0**,
> GObjects/GNames/GWorld all resolved (`GOBJ_ES53_1 -> 0x149BFC140`, `GNAM_V8 -> 0x149B18600`,
> `GWLD_TQ_1 -> 0x149D8BDA0`, 355,717 objects).

**Not covered by this batch:** version detection is still uncancellable (by design — see the block
comment in `DetectVersionDetailed`), and **MA1** — `Macht.cpp`'s AOB family has zero cancellation, so
once a scan enters `AOBScanAllModules` every poll added here is unreachable.

### 🟡 4-of-6 2026-08-17 `[AE23-UI-2026-08-17]` — AE2 / AE3: the Class/Struct panel under fast selection

Run on **Lushfoil Photography Sim** (UE 5.6, 58,093/58,618 objects), dist 3262.

* **1 — PASS.** Clicking tree nodes populates the panel and the header tracks: `MaterialExpression`
  → `//Script/Engine/MaterialExpression`, `Super Class Object`, `Properties Size 176`, full field
  list; then `Light` → `Super Class Actor`, `696`.
* **2 — PASS, on the transition the old failure actually needed.** ⚠ **Filter recorded, because the
  step says a homogeneous list proves nothing:** keyword **`SkyLight`**, **26 results**, genuinely
  interleaved — 5 `Class`, `Enum`, `ScriptStruct`, `Function`, then **six instances**
  (`Default__SkyLight`, `Default__SkyLightComponent`, `SkyLightComponent0`,
  `Default__DatasmithSkyLightComponentTemplate`, `Default__ARSkyLight`, `SkyLightComponent0`), then
  three more `Function` rows, then two instances. Fourteen rapid `Down` presses crossed the whole
  instance block and landed on the class-like `Function BndEvt__3Dmenu_SkyLightAO_…`, and the header
  read that function's full signature with `Properties Size 4` and its single `Value FloatProperty`.
  **Header matched the highlighted row**; it did not stay on the preceding instance's class.
  *(A held `Down` advanced only one row — key-repeat does not reach this list, so use `repeat`.)*
* **3 — BOTH HALVES NOW CLOSED.** *(a)* **PASS live:** no loading indicator stuck after the panel
  settled during that fast traversal. *(b)* **CLOSED offline 2026-08-25 `[AE23-SPINNER-2026-08-25]`**
  — see the block below.
* **6 — PASS.** Typing `Light` then `SkyLight` into the tree filter with a node selected left the
  Class/Struct panel **fully populated** on the previous class — it neither blanked nor flickered.
* **4 — not run** (needs a level travel to make a class address go stale; human-gated).
* **5 — not run** (the cross-tab handoff; nothing pushed a class into Class/Struct in this session).

> #### ✅ STEP 3'S SECOND HALF CLOSED 2026-08-25 `[AE23-SPINNER-2026-08-25]` — offline, because the observer was the problem
>
> The unverified half was *"the spinner does not vanish EARLY while a load is still running"*, and
> the recorded reason it stayed open is worth keeping: **on this machine a class load finishes
> faster than a screenshot can sample it.** Even `DOLLPlayerController` (Properties Size 2224, a
> long inherited field list) was fully drawn in a zero-wait capture, so the spinner was never *seen*
> at all — and a check that never observed the thing APPEAR cannot report on when it disappears.
>
> ⭐ **That is a limit of the OBSERVER, not a property of the code.** What the half claims is
> `IsLoading` staying true for the whole duration of a load, and a gated stub makes the load take
> exactly as long as the test wants. `ClassStructViewModelConcurrencyTests` already had the harness
> (`GatedDumpService.GateWalk`), so this cost two tests, not a rig.
>
> | test | claim |
> |---|---|
> | `IsLoading_StaysTrueForTheWholeLoad_NotJustAtTheStart` | the flag is false before, true while the walk is parked, **still true after the load has been pending a while**, false after — and the class actually loaded, so the run is not vacuous |
> | `ASupersededLoadFinishing_DoesNotClearTheSpinner_ViaTreeSelection` | the failure this row is actually about: under fast selection two loads overlap, and the OLDER one settling must not take the spinner down while the newer is still in flight |
>
> ⚠ **Negative controls, each isolating its own claim:**
>
> | armed by | result |
> |---|---|
> | `IsLoading = true` removed | **3** tests red, including *"the spinner is not up while the load is parked mid-flight"* |
> | the `if (gen == _loadId)` ownership guard removed from the `finally` | **exactly the 2** stale-load tests red; the whole-duration test stays green, correctly — it involves no stale load |
>
> ⚠⚠ **An overlap I did not spot by reading, and the control is what found it.** NC-1 reddened
> THREE tests where I expected two: `StaleWalk_DoesNotClearIsLoadingOfNewerLoad` already existed and
> asserts the same property as the second test above. The difference is real but narrow and is now
> written into the test itself: the existing one drives `LoadClassCommand` (the **cross-tab** entry),
> the new one drives `OnObjectSelected` (**tree selection**) — which is what AE2/AE3 is about. Both
> funnel into `LoadClassCoreAsync`, so the guard under test is the same.
>
> ℹ️ Still not proven, stated plainly: that the XAML binds the spinner to `IsLoading`. This is a
> ViewModel closure. 4,728 UI tests green; `ClassStructViewModel.cs` byte-identical to HEAD.

### (superseded) NEW 2026-08-17 — U4 / U16 / U6 / F3: the three never-erased caches in `Ubel`

*Needs the DLL injected. See dev-log builds 3052 / 3058 / 3065. The C++ suite pins all three predicates
(21 new assertions, 1073 → 1094); what it structurally cannot pin is the WIRING, because no test
target compiles `Ubel.cpp`. **Every step below is about the call sites, not the predicates.***

1. **⚠ REGRESSION FIRST — ordinary browsing is unchanged.** Object Tree loads, Live Walker drills
   into an actor and shows its fields, Property Search returns hits, an enum-typed field still shows
   its member NAME (not a raw int). All three caches are on this path; if anything here is worse,
   stop and read `walk-0.log`.
2. **U4 — a non-UStruct address no longer poisons the cache.** From CE Lua pick an address `A` that
   is not a class, call `UE5_WalkClassBegin(A)` then `UE5_WalkClassEnd()`, **twice**. `walk-0.log`
   must show **two** `WalkClass:` DEBUG lines for `0x<A>` (before the fix the second was served from
   the poisoned entry and logged nothing), plus a `WALK:safe` line naming `A`. **Record `A` and the
   `size=` the first line reported** — a number without its conditions is not a measurement.
3. **U4 — the honest half.** Confirm a legitimately field-less class (or an `FDateTime` /
   `FTimespan` struct, which `InjectIntrinsicStructFields` covers) still walks and still caches:
   exactly ONE cold-walk log line across repeated visits. The gate must reject garbage, not emptiness.

   ### ✅ **CLOSED 2026-08-24** `[U4-STEP3-2026-08-24]` — DumperTest dev, DLL 3345, **no CE, no fixture**
   `tools/verify/u4_step3_zerofield.py`. The predicate is `ShouldPublishClassWalk`
   (`Ubel.h:550`) = `propsSizeReadOk && IsSanePropertiesSize(...)`, whose own comment forbids
   gating on `Fields.empty()` or `Name.empty()`. So the question is exactly *"is emptiness still
   cached, while garbage is still refused?"*

   | | result |
   |---|---|
   | subject | `RigVMExtendedExecuteContext` — `props_size=560`, **0 reflected fields** |
   | walked 4x after the survey | **0** cold-walk lines, **0** `refusing to cache` -> every call hit the memo |
   | NEGATIVE CONTROL: an instance address walked 4x | **4** `refusing to cache` -> refused every time, never memoized |

   The observation cannot be faked: a COLD walk logs two lines (`Ubel.cpp:904` and `:980`) while a
   cache HIT logs neither, because `WalkClass` returns from the memo before reaching them — so
   *"walked 4x, logged 0"* **is** the memo, observed rather than asserted. And the control is what
   stops `0` reading as "the walk silently did nothing".

   ⭐ **THE SURVEY ANSWERED THE FIXTURE QUESTION, AND THE ANSWER IS "NO FIXTURE".** 500 `ScriptStruct`
   objects: **432 with fields, 68 with none, 67 usable** (named + `props_size>0`). The classification
   doc's drafted `USTRUCT() struct FDumperTestEmpty` (`auto-verification-classification-2026-08-23.md:352`)
   is **not needed** — which also removes one item from the C-bucket fixture list and its UE repackage.

   ⚠ **The survey is half the test, and reading it wrong manufactures a fixture out of nothing.**
   `walk_class` returns its payload **nested under `"class"`**; reading `fields` off the TOP level
   yields `[]` for every object. Measured while writing the rig: that mistake reported
   **500/500** "zero-field" structs — including `Vector`, `Guid` and `Box` — versus **68/500** read
   correctly. It would have picked a garbage subject while looking like a rich survey, which is why
   a candidate must be **named** and have **`props_size > 0`**: a struct with real storage and a
   resolved name cannot be a failed read masquerading as an empty one.
4. **U6/F3 — the in-session recycle, the point of the whole commit.** Bookmark an actor, travel to
   another level **while staying connected**, then re-walk the bookmark. It must show the new
   occupant's name or `""` — never the destroyed actor's name. This is the failure that previously
   needed a game restart to clear, and the reconnect-only fix (2819) could not reach it.
   *Deterministic alternative, no level change:* note an inert object's name and the 4 bytes at
   `+0x18`, write a different valid `ComparisonIndex` there from CE, refresh the same address — the
   new name must appear. Read the name off `get_object` / `walk_instance`'s own `name` field, **not**
   off a panel that renders a class-cache name; those are frozen copies and will look stale either
   way (see the open finding below).
5. **U16 — enums are unaffected in the normal case.** Open a class with a large enum field
   (`EPhysicalSurface` or any Blueprint enum) and confirm the CE DropDownList still lists every
   member. The truncation path is not stageable on demand; what this checks is that the new
   publish gate did not stop caching healthy tables. Grep `walk-0.log` for
   `ResolveEnumValue: UEnum` — the line now reports `read N of M`, and **N must equal M**.
   Any `GetEnumEntries: ... truncated read` line is a real find, so record it.

</details>

**Still open after this batch, deliberately** — do not read a pass here as closing them:
**U5** (nothing is freed; eviction is illegal while `WalkClassEx` returns a reference),
class-to-class recycling (a recycled address whose new occupant has a *sane* `PropertiesSize`),
**A10** (`Aura`'s two reference-returning caches), and names baked into `ClassInfo::Name` /
`FullPath` / `SuperName`, which are never witnessed.

### ✅ ALL SIX STEPS CLOSED (step 2 on 2026-08-24 `[AE4S2-BUSYBAR-2026-08-24]`; steps 3 + 4 on 2026-08-20 / 2026-08-24; step 4's gate arm is pinned offline, see below) — AE4–AE7: the Proxy Deploy panel, two buttons at once

*No game needed — just the UI and a folder with a couple of detected games. See dev-log build 3038.
Every step is a click sequence; the unit tests cover the logic, not what the panel looks like doing it.*

1. **Two operations no longer overlap.** Scan for games, tick a couple, press **Deploy** and then
   immediately **Remove**. The second must refuse with a line naming what is running
   (*"Busy: Deploy is running…"*) — not the old *"Wait for scan to finish"* when no scan is running,
   and not both operations writing over the same `Binaries` folder.
2. **The busy indicator finally appears for them.** The panel's progress bar is bound to
   `IsScanning`, which Deploy / Remove / Refresh / Update All never set — so they used to look like
   nothing was happening. Confirm the bar now runs during each of the four.
3. **⚠ REGRESSION — the three scans still work and still cancel.** Scan Steam, Scan drives (+ its
   Cancel button), Find leftovers (+ its Cancel). The gate took over `IsScanning` from all three, and
   `IsScanningDrives` / `IsScanningOrphans` still drive the two Cancel buttons independently — a
   ghost Cancel on the wrong card is the failure to watch for (it is what B45 fixed originally).
4. **⚠ REGRESSION — leftover removal is unaffected.** Find leftovers → tick one → Delete. It uses
   `IsRemovingOrphans`, which the gate now also tests; confirm a delete still blocks a scan and vice
   versa.
5. **The proxy-type radios.** Click through version → dinput8 → dxgi quickly. The grid's Status /
   Installed Version columns must end up showing the type the radio shows. Before this they could
   settle on a type nobody selected, with nothing to re-run it.
6. **The drive-selection reset.** Switch source to **Scan drives**, and while the drive list is
   loading switch back to Steam and to Drives again. Tick some drives. They must stay ticked — a
   second load used to `Clear()` the list and silently drop the selection.

> ### 🟡 4-of-6 CLOSED 2026-08-17 `[AE4-UI-2026-08-17]` — UI driven with computer-use, no game
>
> Build **1.0.0.3262** (the AOT `dist` binary), app `Disconnected` throughout. **Two steps are
> recorded NOT TESTED with a measured reason, not waved through.**
>
> | step | verdict | evidence |
> |---|---|---|
> | 1 | ✅ **PASS, via a stated substitution** | Deploy-then-Undeploy **cannot** be made to overlap here: a single 2.8 MB copy finishes faster than one input event, measured twice (`Deployed: 1 success, 0 failed` then `Removed: 1 success, 0 failed` — both ran). The **shared** gate was then exercised with a long first operation: with `Scan drives` running, pressing `Deploy` produced **`Busy: Scan drives is running — wait for it to finish`** — a line that *names what is running*, and not the old wrong *"Wait for scan to finish"*. Designed to risk nothing: **no rows were ticked**, so neither outcome could write a file |
> | 2 | ✅ **CLOSED 2026-08-24 `[AE4S2-BUSYBAR-2026-08-24]` — by a CI test, because the visual step is structurally unobservable** | Bar confirmed by eye during **Scan Steam** (`Checking deploy status...`), **Scan drives** and **Find leftovers**. The last is the one that counts: it runs on `IsScanningOrphans`, *not* `IsScanning`, so the bar is demonstrably no longer bound to `IsScanning` alone.<br>**The other four were re-attempted 2026-08-24 with a REAL workload and are still not observable**: Update All was run against **9 genuinely stale proxies** (~26 MB of copies, `Updated: 9, up-to-date: 2, failed: 0`, independently confirmed on disk — the earlier attempt may have been a no-op loop, this one was not) and it *still* finished inside one screenshot round-trip. So "photograph the bar" is not a procedure that can pass for Deploy / Undeploy / Refresh / Update All on this machine, and no amount of retrying changes that.<br>⭐ **What closed it instead — and the defect found on the way.** The chain is command → `TryBeginExclusive` → `IsScanning = true` (`ProxyDeployViewModel.cs:166`) → bar `IsVisible`/`IsIndeterminate` (`ProxyDeployPanel.axaml:238-239`), and `EveryLongOperation_HoldsTheGate_NotJustTheScans` was supposed to pin the middle link for all four. **It did not**: its mid-flight assertion was guarded by `if (!running.IsCompleted)`, and against the default harness Refresh and UpdateAll *complete synchronously* (`Ready()` deploys no proxy, so Update All hits `!File.Exists(targetDll)` and `continue`s past `DeployAsync` — the only awaited call). The guard was therefore false and the assertion **skipped**, leaving only `Assert.False(IsScanning)` afterwards, which a build that never sets the flag also passes. **The two commands AE5 was about were the two its own regression test silently exempted.**<br>Fixed by making each case reach an await inside the gate (`Ready(deployed: true)` for UpdateAll, `ParkRefreshes` for Refresh) and asserting unconditionally.<br>⚠ **Negative control, and it demonstrates the old test's blindness rather than asserting it.** With `IsScanning = true` commented out of `TryBeginExclusive`, on one identical broken build: **new shape 4 of 4 FAIL** (all at the `Assert.True(vm.IsScanning)` line) while the **old shape passes 2 of 2** — Refresh and UpdateAll both green on a build with no flag at all. Both temporaries reverted; product file byte-identical to HEAD, 11/11 green |
> | 3 | ✅ **PASS on 2 of 3 cancels** | Scan Steam runs; **Scan drives runs AND cancels** with an explicit `Scan cancelled` status. Find leftovers runs, its `Cancel` appears on the **correct card** and clears correctly — but the scan finished before the click **twice** (14 s then <3 s), so the orphan cancel itself is **NOT tested**. ⚠ The B45 failure was checked in **both** directions: no ghost `Cancel` ever appeared on the other card |
> | 4 | ✅ **PASS 2026-08-20 `[AE4S4-ORPHAN-2026-08-20]`** | Staged the synthetic leftover with `tools/verify/stage_synth.py create` — `ZZSynthOrphan\ZZOrphan\Binaries\Win64
ersion.dll`, our proxy with **no exe beside it**. **Find leftovers** → `Found 1 leftover proxy DLL(s) — nothing removed yet`; ticking the row enabled **`Delete checked (1)`** and the row spelled out the plan (`Recycle version.dll, then remove up to 4 folder(s) if it leaves empty, stopping below ZZSynthOrphan`). The confirm dialog listed the four folders **leaf→root, each only if left empty**, and named the boundary `Not touched: …\steamapps\common`. Result: **`Cleaned 1 of 1 leftover(s) — 1 file(s) recycled, 4 folder(s) removed`**.<br>**Verified independently of the panel's own report** (working-lessons §1.4): `ZZSynthOrphan` is gone from disk, `…\steamapps\common` still exists, and the file is genuinely in the bin and recoverable — exactly **2,882,560 bytes** at `D:\$Recycle.Bin\S-1-5-…\$RGOIP98.dll`.<br>⭐ **The negative control is the strongest part**: the sibling tree `ZZSynthProxyTest`, which holds our `dxgi.dll` **beside a real `-Shipping.exe`**, was left completely untouched (both files still present). So the scanner distinguished a true leftover from a proxy that belongs to a game, rather than deleting every proxy it found.<br>⚠ **The mutual-exclusion half of this step was NOT demonstrated** — "a delete blocks a scan and vice versa". The delete completes faster than one input event, the same measured reason step 1 could not be made to overlap. |
> | 5 | ✅ **PASS, both directions** | version→dinput8→**dxgi** clicked quickly: header becomes `Source: dxgi.dll v1.0.0.3262`, the nine `version.dll` titles flip to `DeployedOtherType` with the Version column **cleared**, and **Elliot flips to `DeployedCurrent` 1.0.0.3262** — because its real proxy *is* dxgi. Clicking back to `version.dll` flips both sets symmetrically. So Status **and** Installed Version follow the radio, with a positive and a negative case in one view |
> | 6 | ✅ **PASS** | D: ticked → Source toggled Steam→Scan Drives to force a **second** load of the drive list → D: **still ticked** |
>
> **State left as found, verified independently of the panel's own report** (working-lessons §1.4):
> TQ2 was the deploy target and its `Binaries\Win64` was re-listed from Python afterwards — no
> `version.dll` / `dxgi.dll` / `dinput8.dll` / `winmm.dll` present. `Force Overwrite` and the Source
> radio were both returned to their original values.
>
> **§3a re-confirmed by a second, independent route.** The panel reports `Found 16 UE game(s)` —
> exactly the 16 titles an offline enumeration found — and every deployed proxy reads **1.0.0.3262**:
> nine `version.dll` (ES2, DQ7R, DQ I&II, EVERSPACE, Lushfoil, Manor Lords, OCTOPATH, SEED, Geri) plus
> **Elliot** on `dxgi.dll · confirmed working`. Ten, matching §3a exactly.
>
> ⚠ **NEW, and §3a's inventory does not cover it: an ELEVENTH deployed proxy, and it is STALE.**
> A drive scan surfaces `D:\UE_Analyze_data\Game archive\Satisfactory\UE5.6.1\…` as
> **`DeployedOutdated 1.0.0.2498`**. It is in the reference-build corpus rather than a game, which is
> why the game-only inventory missed it. Two consequences: §3a should say *eleven*, and this row is a
> ready-made **`DeployedOutdated` fixture** — the only one on the machine — for exercising `Update All`
> (AE4 step 2) against something that actually needs updating.
>
> *Incidental lead, not filed as a finding:* launching a **second** instance of the UI writes an
> unhandled exception into `crash.log` — `System.InvalidOperationException: Cannot perform requested
> operation because the Dispatcher shut down` at `ClassicDesktopStyleApplicationLifetime.StartCore` —
> instead of exiting quietly. The first instance is unaffected and keeps running. Worth a look because
> `crash.log` is documented as *the* AOT startup diagnostic, and a benign duplicate launch pollutes it.
>
> ### ✅ STEP 3's ORPHAN CANCEL + STEP 4's MUTUAL EXCLUSION CLOSED 2026-08-24 `[AE4-TIMING-2026-08-24]` — the two "finished before the click" gaps, closed by a bigger fixture
>
> Both gaps had the **same** cause, recorded twice in the table above: *"the scan finished before the
> click **twice** (14 s then <3 s)"* and *"the delete completes faster than one input event"*. Neither
> is a property of the feature — both are a property of the **fixture being one tree**.
>
> ⭐ **600 leftover trees, staged in 1.7 s and costing ~0 disk.** `ae20_orphans.py create --count 600
> --link` hardlinks every proxy to a single staging copy: `st_nlink = 601` on the staged file, so all
> 600 entries share one set of extents — **2.9 MB instead of 1.7 GB**, which is what makes a count
> this large practical at all. ⚠ The links are made from a copy staged *under the Steam library*,
> **never from `dist\proxy\version.dll`** — a hardlink is not subordinate to its "original", every
> link is equal, and the Recycle Bin will move any of them; linking from the repo would put the
> shipped proxy one fixture-delete away. Verified after the run: `dist\proxy\version.dll` intact at
> `nlink = 1`.
>
> **Step 3 — the orphan scan's Cancel, on the correct card.** With 600 trees the scan lasts ~7 s, so
> the button is reachable:
>
> ```
> mid-scan:  Checking 140 folder(s) — 110 leftover(s) found      <- live progress
>            "Find leftovers" greyed; a Cancel appears ON THE LEFTOVER CARD
> after:     Scan cancelled
> ```
> * the Cancel is on the **leftover card**, not a ghost on the drive card ✅ (the B45 failure)
> * ⭐ **Two witnesses, and the second is the load-bearing one.** A completed scan always logs
>   `Orphan scan: N candidate folder(s) examined, M leftover(s) found` — present at **10:36:47**
>   (`640 examined, 600 found`) for the run that finished. The cancelled run logs only
>   `Found 2 Steam library folder(s)` and **no completion line at all**. So the scan demonstrably did
>   not run to the end, independently of the status text that claims it was cancelled.
>
> **Step 4 — "a delete blocks a scan and vice versa", both directions.**
>
> | direction | how | result |
> |---|---|---|
> | **a scan blocks a delete** | Find leftovers → tick 4 → **Scan Steam** → immediately **Delete checked (4)** | refused: `Wait for the current operation to finish`; the scan completed normally (`Found 19 UE game(s)`) and nothing was deleted |
> | **a delete blocks a scan** | tick 4 → Delete → confirm → immediately **Scan Steam** | **no scan started** — the log across the whole delete window (10:41:05.8 → 10:41:06.03) contains no scan-start line, only the delete's own per-row re-plan; the delete finished cleanly (`Cleaned 4 of 4 leftover(s) — 4 file(s) recycled, 16 folder(s) removed`) |
>
> ⚠ **What the second row does NOT show is the refusal TEXT.** `LastOperationResult` is overwritten by
> the delete's own summary ~300 ms later, and a 4-row delete cannot be made to outlast one screenshot
> round-trip (there is no select-all on the leftover card, so ticking 40+ rows is not reachable by
> clicking). The **blocking** is what step 4 asserts and it is measured; the message on that path is
> established from source, not from the screen, and is written up as the finding below.
>
> #### ✅ FIXED 2026-08-24 — `[ORPHANBUSYMSG-2026-08-24]` (LOW) — the leftover delete sat OUTSIDE the busy-naming scheme, in both directions
>
> **Fixed the same day it was found.** `DeleteSelectedOrphansAsync` now reports through
> `BusyMessage()` and takes `TryBeginExclusive("Delete leftovers")`, so **eight** commands share the
> gate where seven did. The scope is taken **after** the confirmation, not at the top of the method
> — holding it across the modal would make the panel report itself busy while the dialog is open,
> which `TheConfirmDialog_IsNotTheGate_AndThatIsDeliberate` explicitly pins against. That placement
> also shuts a window the pre-check alone left open: something invoked *while the dialog was up*
> used to find the delete setting `IsRemovingOrphans` unconditionally on the way out.
>
> ⚠⚠ **The fix shape below is quoted as found and its ⚠ caveat is WRONG — do not follow it.** It
> says `IsRemovingOrphans` is load-bearing because "the leftover card's Cancel binds to it". It does
> not. That Cancel binds to `IsScanningOrphans`, and the comment at `ProxyDeployPanel.axaml:152`
> says so in as many words: *"NOT to the shared IsScanning and NOT to IsRemovingOrphans"* — the very
> line the caveat cites. **Nothing in any `.axaml` binds `IsRemovingOrphans` at all** (`grep -rn
> IsRemovingOrphans ui/UE5DumpUI/Views/` returns only that comment). The flag was kept anyway, for
> the real reasons: it is half of `IsBusy` and the orphan tests assert on it. Working-lessons §2.4,
> exactly — re-derive the PREMISE, not just the location.
>
> ⭐ **A third half of the defect went unreported and is also fixed: the delete showed NO busy bar
> at all.** Blast radius checked rather than assumed — inside this panel `IsScanning` is bound only
> by the progress bar (`ProxyDeployPanel.axaml:238`) and read at `:576` to skip the drive-list
> auto-load. So the one visible change is that a leftover delete finally renders the bar.
>
> ⚠ **The existing tests could not have caught the naming half, and one of them PINNED the defect.**
> `AScan_IsRefused_WhileAnOrphanDeleteIsRunning` asserted only `Contains("Busy")` — which the
> `?? "another operation"` fallback also satisfies, so it was green on the broken wording.
> `AnOrphanDelete_IsRefused_WhileAScanIsRunning` asserted the generic
> `"Wait for the current operation"` **verbatim**, i.e. it asserted the defect. Both now assert the
> holder's own label and `DoesNotContain("another operation")`.
>
> **Two independent negative controls, each isolating one half:**
>
> | control | armed by | result |
> |---|---|---|
> | NC-A | top message → back to the generic string | **only** `AnOrphanDelete_IsRefused_WhileAScanIsRunning` fails |
> | NC-B | `TryBeginExclusive` removed, hand-rolled shape restored | **only** `AScan_IsRefused_WhileAnOrphanDeleteIsRunning` fails |
>
> Both reverted; **4,712 UI tests green, 0 failed**.
>
> <details><summary>the original finding, as recorded</summary>
>
> `TryBeginExclusive(what)` ([ProxyDeployViewModel.cs:163](ui/UE5DumpUI/ViewModels/ProxyDeployViewModel.cs:163))
> does three things: tests `IsScanning || IsRemovingOrphans`, sets **`_busyWith = what`**, sets
> `IsScanning = true`. **Seven** commands use it and report through
> `BusyMessage()` → `Busy: {_busyWith} is running — wait for it to finish`.
>
> `DeleteSelectedOrphansAsync` ([:959](ui/UE5DumpUI/ViewModels/ProxyDeployViewModel.cs:959)) does not.
> It hand-rolls the same predicate and then:
>
> * reports **`"Wait for the current operation to finish"`** — a generic line that names nothing, and
>   almost exactly the wording step 1 says the fix exists to replace (*"not the old 'Wait for scan to
>   finish'"*). **Measured on screen**, above;
> * never sets `_busyWith`, so while a delete is running the other seven fall through
>   `BusyMessage()`'s own `?? "another operation"` fallback — **`Busy: another operation is running`**.
>
> ⭐ So the leftover delete is the one operation in the panel that is unnamed **as the blocker and as
> the blocked**. The exclusion itself is correct in both directions — this is wording only, hence LOW.
>
> **Fix shape** (not applied — this session verifies): give the delete the same scope as everything
> else — `using var busy = TryBeginExclusive("Delete leftovers"); if (busy is null) { LastOperationResult
> = BusyMessage(); return; }` — and let `BusyScope.Dispose` clear it, instead of the hand-rolled
> `IsRemovingOrphans = true` in the `try`. ⚠ **`IsRemovingOrphans` is separately load-bearing** — the
> leftover card's Cancel binds to it (`ProxyDeployPanel.axaml:152-158`, deliberately *not* to the
> shared `IsScanning`), so it must keep being set, not be replaced by the scope.
>
> </details>
>
> ℹ️ The `Seven commands` count above was right when written and is now **eight** — derive it with
> `grep -c 'TryBeginExclusive("' ui/UE5DumpUI/ViewModels/ProxyDeployViewModel.cs`, never quote it.

> ### 🟡 STEPS 2 AND 3 CLOSED 2026-08-19 — by the maintainer, on their own machine
>
> The maintainer worked the 繁中 checklist from a NAS copy and ticked its rows **2, 3, 5 and 6**
> (that file's rows are 1:1 with the six numbered steps above). Two of those four were already closed
> here; the other two are exactly the two this block had recorded as **not** settled, and they are the
> reason this row moves from 4-of-6 to 5-of-6:
>
> | step | was | now | what changed |
> |---|---|---|---|
> | 2 | 🟡 PARTIAL — the bar was only observable on the three *scans*; Deploy / Remove / Refresh / Update All each finished inside one screenshot round-trip | ✅ | a human watching the panel live is not bound by the round-trip that blocked the automated pass — this is the measurement the machine could not take |
> | 3 | ✅ on **2 of 3** cancels — Find leftovers finished before the click, twice | ✅ **3 of 3** | the orphan-scan Cancel was finally caught mid-scan |
>
> Steps 5 and 6 were ticked too and were already ✅ above; the ticks corroborate, they do not add.
> **Step 1 was NOT ticked** and does not need to be — it is ✅ above via the stated substitution
> (a long first operation exercising the shared gate).
>
> ⚠ **What remains is step 4, and only half of it.** The *removal* half is closed (see
> "AE4 step 4 — removal half CLOSED" above: 1 file recycled, 4 folders removed, recoverable from the
> Recycle Bin). What is still unproven is the **`IsRemovingOrphans` gate arm** — that an in-flight
> delete refuses a scan and vice versa. The maintainer did not tick step 4, which is consistent:
> forcing that overlap needs a delete slow enough to click through, and nothing on this machine is.
>
> ⚠ **Evidence class, stated plainly:** these two ticks are the maintainer's own observation. No log
> line, screenshot or file hash from that run reached this repo, and nothing here was re-observed to
> confirm them. They are recorded as reported.

### 🟡 4-of-5 CLOSED 2026-08-19 — A6: Force now holds the class AND its subclasses

*Any game. See dev-log build 3036. This one changes what an already-shipped, in-game-verified
feature WRITES TO (the Stealth Meter card), so the regression half matters as much as the fix.*

1. **The capability that did not exist before.** Property Search a field on a base class — anything
   whose row shows an "inherited by N" badge (`bCanBeDamaged @ Actor` is the easy one) → right-click
   → Force. Before this it said *"0 live instances of Actor … — nothing held"*; it must now hold on
   a real, non-zero count. **That message is the whole finding — if it still appears, stop.**
2. **The held instances are the SUBCLASSES.** Property Search's "Forced fields (N held)" strip
   should show a count in the hundreds for a broad base, not 1. If the pool is capped the status
   line must say *"cap reached, more exist unheld"* — a broad base hits the 256 cap easily, so
   confirm the badge appears rather than a bare "on 256 instance(s)".
3. **Derivation, not substring.** Force a field on a class with a same-prefix sibling (`Enemy` vs
   `EnemyProjectile`, or any `Foo` / `FooComponent` pair in the game). The unrelated class must NOT
   be held — check the ForcedFields strip / the DLL log line `FindInstancesDerivedFrom base=…`,
   which reports the distinct class count it walked.
4. **⚠ REGRESSION — Stealth Meter still works.** Teleport tab → Stealth card → Detect → Hold @0 →
   Reset. It resolves a CONCRETE class, so subclass semantics should be additive — but this is the
   shipped, previously in-game-verified path that A6 deliberately changed, and it is the one thing
   here that could get *worse*. Confirm Hold reports a non-zero count and Reset restores.
5. **⚠ REGRESSION — no CDO is written.** After forcing a bool on a base class, the game must not
   show every future spawn already carrying the forced value in a way that survives
   `reset_all_fields` — that would mean a class-default object was written. (The CDO skip moved
   inside Aura's walk; the local skip in `Solide` stayed as the invariant.)

> ### 🟡 STEPS 1, 2 AND 4 CLOSED 2026-08-19 — by the maintainer, on their own machine
>
> Ticked in the 繁中 checklist, whose five rows are 1:1 with the five steps above.
>
> * **Step 1 ✅ — the capability that did not exist before.** Force on a base-class field held a
>   non-zero count. The pre-3036 output *"0 live instances of Actor … — nothing held"* is the whole
>   finding, and the checklist says to stop if it reappears; it did not.
> * **Step 2 ✅ — the held instances really are the subclasses.** The "Forced fields (N held)" strip
>   showed a broad-base count rather than 1, with the cap disclosed as *"cap reached, more exist
>   unheld"* rather than a bare "on 256 instance(s)".
> * **Step 4 ✅ — ⚠ REGRESSION CLEAR.** Teleport → Stealth card → Detect → Hold @0 → Reset still
>   reports a non-zero hold and restores on Reset. This is the one already-shipped, already
>   in-game-verified path A6 changed, so it is the step that could have gone *backwards*. It did not.
>
> ### ✅ STEP 3 CLOSED 2026-08-19 `[A6-DERIV-2026-08-19]` — derivation, not substring, PROVEN
>
> Headless over the pipe on DumperTest Development / dist 3263 (`tools/verify/a6_derivation.py`).
> The pair chosen is stronger than the row's `Enemy`/`EnemyProjectile` suggestion because **UE
> guarantees it**, so the result does not depend on one game's class tree:
> `CharacterMovementComponent` starts with `Character` and derives from `UActorComponent` — it is
> not a `Character` by any super-chain, but it is an exact prefix match.
>
> | walk | live instances |
> |---|---|
> | `FindInstancesDerivedFrom base='Character'` | **1** |
> | `FindInstancesDerivedFrom base='CharacterMovementComponent'` | **7** |
>
> Forcing `bCanBeDamaged` on `Character` held **1**. A prefix matcher would have held **8**.
>
> ⭐ **The reachability control is what makes this decisive, and it is the half that is easy to
> skip.** "The impostor was not held" proves nothing if the impostor is not in the pool: forcing
> `bAutoActivate` on `CharacterMovementComponent` itself held **7**, so those objects are live,
> reachable and holdable — their absence from the `Character` hold is a real **exclusion**, not an
> empty pool. Both walks report the same corpus (`scanned=25179, nonNull=25172, 3941 distinct
> classes`), so the difference is not a scoping artefact either.
>
> Game state restored: `reset_all_fields` → `get_forced_fields` re-read, **0** fields held.
> ⚠ Also worth knowing: **`find_instances` matches by NAME** and cheerfully returns
> `Default__CharacterMovementComponent` for the query `Character`. The two code paths are different,
> and confusing them is exactly the mistake this step exists to rule out.
>
> **Step 5 (no CDO is written) still remains** — it needs spawns after a reset, which a static pool
> cannot provide.

> ⚠ **STEP 5 REMAINS (step 3 closed above); it is the one that could still be wrong:**
> * **Step 3 — derivation, not substring.** Nothing above distinguishes a real super-chain test from
>   a name-prefix match; both hold "hundreds". It needs a same-prefix sibling pair (`Enemy` /
>   `EnemyProjectile`, any `Foo` / `FooComponent`) with the unrelated class confirmed **not** held,
>   read off the ForcedFields strip and the DLL's `FindInstancesDerivedFrom base=…` line. A6's whole
>   point is that this is a derivation test, and it is still unproven on a live pool.
> * **Step 5 — no CDO is written.** Force a bool on a base class, `reset_all_fields`, then watch
>   *newly spawned* objects. If they still carry the forced value, a class-default object was
>   written. Needs spawns after the reset, so it cannot be settled from a static pool.
>
> ⚠ **Evidence class:** the maintainer's ticks. No log line or screenshot reached this repo, and
> step 2's actual count and step 4's actual numbers were not recorded. Reported, not re-observed.

### 🟡 A5 + AE9 CLOSED, V6 corrected to a HALF-pass 2026-08-19 — the fourteen-MED batch, all UI-visible: A5 / V6 / AE9 / U8 / V7 / U7 / G1 / X3 / AB6 / AF4 / AF2 / AF6 / AE8 / AF1 (builds 3016-3031)

> ### ✅ U7 and X3 CLOSED 2026-08-24 — the batch's last two residues
>
> **U7 — `[U7-CJKCUT-2026-08-24]`.** The residue was the COMBINED case: a non-ASCII StrProperty whose
> preview also exceeds the 50-byte cut. Both halves passed separately on DQ7R, but no title here had
> both in one field, for a documented reason — localized games store display text as `FText`, so
> their `FString`s are short identifiers. Manufactured instead: `Str_Even22_TwoNull` (22 CJK chars =
> **66 UTF-8 bytes**) added to the DumperTest sample and all three configs repackaged.
>
> ```
> preview   "統一言語日本語テスト日本語テスト…"
> glyphs    16      the 50-byte cut backs up to a boundary at 48
> ellipsis  present, INSIDE the quotes
> U+FFFD    0       no split 3-byte sequence
> rows      1, and no `error` key
> ```
>
> ⭐ **The failure mode is not an ugly preview, it is ZERO ROWS**: a byte-naive `resize(50)` emits a
> split sequence and nlohmann's strict `dump()` turns the whole `search_properties` reply into
> `{"error":…}`. 50 mod 3 == 2, so with uniform 3-byte CJK the cut *always* lands mid-sequence.
>
> **X3 — `[G1-AMBER-2026-08-24]`.** X3 is the offset banner, not a CJK row; it closed with G1 the
> same day. ⚠ `auto-verification-classification-2026-08-23.md` mislabels X3 as the CJK item, which
> sent one triage down the wrong path.

Eight of the fourteen now carry a PASS block below (A5 · V6 · AE9 · V7 · AF4 · AB6 · AE8 · AF6) —
but **V6's is a HALF-pass and is corrected below**: its evidence covers the manual-Refresh half only,
and the auto-refresh half its own step prescribes was **not observed and could not have been**. The
rest are cheap to check because each has a *visible* pass/fail, and four of them only ever show up
when something ELSE goes wrong.

**Free from any ordinary session (just look):**

1. **A5 — Preview shows a LIVE value. ✅ VERIFIED 2026-08-19 — by the maintainer, on their own
   machine. I did not observe it.** Property Search a field you can change in-game (Health).
   The Preview column must read the value from a live instance, not the Blueprint default. A row
   whose class has no live instance must read `… (CDO default)` — the marker is the fix's honesty
   half, so confirm both.
   **Evidence: the maintainer's own run.** They re-ran the search after letting the value move
   in-game and reported the second Preview carrying the new number. That is *their report*, not an
   agent observation — there is no screenshot and no log line of mine behind it, and it is recorded
   to the same standard as the `[SKIA-ABI-2026-08-19]` close above. It corroborates the
   `[GRP4-UI-2026-08-17]` PASS block below, which was obtained by the same procedure.
   ⚠ **The Preview is a SNAPSHOT taken when the search runs; it does not update on its own, and it
   is not supposed to.** There is no timer and no live-cell binding in `PropertySearchViewModel` —
   `Preview` is a plain string on the result row, written once per search. So the check is
   **search → note the value → let the game move it → press Search AGAIN → the value must have
   moved**, which is exactly how the 2026-08-17 PASS below was actually obtained ("a re-search ~38 s
   later previewed 317"). Staring at the column waiting for it to tick is testing a feature that
   does not exist, and reads as a defect that is not there.
   📌 **Why this had to be run twice, and the lesson that is worth more than the close.** The
   2026-08-17 PASS was *also* obtained by re-searching — and the conditions that produced it were
   written into the **evidence block** ("a re-search ~38 s later previewed 317") but never into the
   **step**, which in this file *and* in the 繁中 mirror still told the reader to watch the Preview
   column. A wrong procedure therefore survived a PASS. On 2026-08-19 the maintainer followed it,
   saw nothing move, re-ran the same query three times in four seconds (12:47:22 / :24 / :26) and
   reported a defect that does not exist ("不知是否為 Issue: 再按一次 Search 才會刷新"). Cost: one
   round trip and one wasted run, for a feature that was correct the whole time. Generalised as the
   propagation half of [working-lessons.md](working-lessons.md) **§1.6** — writing a PASS's
   conditions beside the *number* is not enough when a second document owns the *procedure*.
2. **V6 — the search highlight survives a Refresh. 🟡 HALF-VERIFIED: manual PASS 2026-08-17, the
   auto-refresh half NOT OBSERVED and BLOCKED.** Live Walker → type a field-search keyword →
   press Refresh (and leave auto-refresh on for a few ticks). Highlights must stay, the ↑/↓ stepper
   must still land on highlighted rows, and **the grid must not jump to the top** — that last one is
   what the fix deliberately avoided by not re-using `ApplySearch`.
   ✅ **The manual-Refresh half PASSED 2026-08-17** — evidence in the `[GRP4-UI-2026-08-17]` block
   below, and it is a complete check of that half.
   ⛔ **The auto-refresh half is NOT OBSERVED, and the 2026-08-17 run could not have observed it.**
   That session ran on **dist 1.0.0.3262**, the build `[AUTOREFRESH-2026-08-19]` proves had a
   **frozen** Live Walker countdown: the countdown's only reset sat *past* `OnAutoRefreshTick`'s
   early-return guard, so one skipped tick pinned the label at `0s` forever while `Auto` still read
   ON — and the log analysis on that build measured **zero** auto-refreshes across a logged session
   ("no periodic cadence exists anywhere in the session"; every `walk_instance` in its 21-minute
   Elliot half maps 1:1 to a user action). So "leave auto-refresh on for a few ticks" was **physically
   unperformable** when the PASS was recorded, and the evidence block correspondingly says
   *"Pressed **Refresh**"* / *"Pressed the **▼ stepper**"* and never mentions Auto at all.
   ▶ **V6 stays OPEN for this half only**, blocked until `[AUTOREFRESH-2026-08-19]`'s fix reaches a
   **published** build (the other PC is still on 3262). Re-run: keyword → tick **Auto** → let it
   cycle 2-3 periods with no manual press → highlights, stepper target and scroll anchor must all
   survive a refresh the *timer* caused.
   📌 **This is why the record is corrected rather than deleted.** The PASS was not wrong about what
   it saw; it was wrong about what it *covered*. Same family as A5's 📌 above — there the conditions
   never reached the step, here a step's precondition was never checked against the build under test.
   **A procedure that names a behaviour the build cannot perform does not fail; it silently passes on
   the half that works.** Before recording a PASS, check that every clause of the step was runnable.
3. **AE9 — New Scan resets the Sort picker. ✅ VERIFIED 2026-08-17** (both halves; evidence in the
   `[GRP4-UI-2026-08-17]` block below, and its 繁中 step is deleted per close-then-delete).
   Value Search → First Scan → sort by Value → New Scan.
   The picker must read *"Scan order"*, and picking *"Value"* again must actually re-sort.
4. **U8 — `FName::Number` is back.** Live Walker a `NameProperty` whose value has a numeric suffix
   (`Slot_1`, `Slot_2`). Panel and Value Search must agree on the same 8 bytes. ⚠ Object/instance
   NAMES are a separate, unfixed lead — do not read a truncated instance name as a failure here.

> ### VERIFIED 2026-08-20 - U8 PASSES on a STAGED fixture, both halves
>
> **The sample has no fixture, so one was made and then unmade.** All 128 fields of
> `DumperTestActor` were walked: its only two `NameProperty` fields are `NetDriverName`
> (`GameNetDriver`) and `Name_Cjk`, and reading their raw 8 bytes with `ReadProcessMemory` shows
> **`Number == 0` on both**. Sweeping 42 live objects found no suffixed `NameProperty` value
> anywhere. Rather than record "no fixture", the rig writes `Number := 8` into `Name_Cjk`'s FName
> and restores the original 8 bytes afterwards:
>
> | `Name_Cjk` FName | panel (`walk_instance`) |
> |---|---|
> | `Number = 0` (as shipped) | `U+7D71 U+4E00` |
> | `Number = 8` (staged) | `U+7D71 U+4E00 U+005F U+0037` - i.e. **`_7` appended** |
> | restored to `Number = 0` | `U+7D71 U+4E00` again |
>
> The restore is the control: the change is shown to be **caused by the Number** and not by a
> re-read, a cache, or the walk itself.
>
> **The second half - "panel and Value Search must agree on the same 8 bytes" - is exact.** With
> the fixture staged, a `FName`/`Exact` value scan for the **suffixed** string returns exactly one
> candidate at **`0x1C0D51E7480`**, which *is* the field address (`instance 0x1C0D51E7120 + 0x360`).
>
> ⭐ **And the negative control landed better than asked for.** The same scan for the **bare**
> string also returns one candidate - but at a *different* address, `0x1C0D51764A0`, which is the
> **CDO**'s copy of the field (`0x1C0D5176140 + 0x360`), still sitting at `Number = 0`. So the two
> scans are disjoint and each matches exactly the object whose 8 bytes encode that rendering. The
> scanner is not string-matching loosely; it is decoding the same FName the panel decodes.
>
> `Ubel.cpp`'s own comment above `DecodeFNameBytes` names this finding and its cause (three sites
> open-coded `memcpy(&idx, p, 4); Serie::GetString(idx)`), which is what this run confirms is gone.


> ### ✅ A5 · AE9 PASS · 🟡 V6 HALF-PASS 2026-08-17 `[GRP4-UI-2026-08-17]` — DumperTest Development, 3262
>
> ⚠ **Corrected 2026-08-19: this block originally read "A5 · V6 · AE9 all PASS".** A5 and AE9 are
> unaffected. **V6's evidence covers the manual-Refresh half only** — the auto-refresh half its step
> prescribes was unperformable on 3262 (`[AUTOREFRESH-2026-08-19]`), so the ✅ was broader than what
> was seen. Nothing observed here is withdrawn; only the scope of the claim is.
>
> **A5 — the live half AND the honesty half, on one screen.** Property Search `TickCount`:
> `DumperTestActor.TickCount` (IntProperty, `0x6A8`) previewed **279**, and a re-search ~38 s later
> previewed **317**. The sample's HUD drives TickCount at 1 Hz, so +38 in 38 s is the value *moving*,
> not merely looking plausible — that is what makes it a live reading rather than a Blueprint default.
> In the same result set, `NiagaraComponent.WarmupTickCount` and `NiagaraSystem.WarmupTickCount` read
> **`0 (CDO default)`**, i.e. classes with no live instance are marked instead of silently presented
> as live. Both halves of the fix, together.
>
> ⚠ **Lead, not filed as a defect: DEEP rows get no preview at all.** A `CurrentValue` deep search
> returned 5 rows (`DumperTestActor.Health.CurrentValue` @ `0x698` among them) and **every Preview
> cell was empty** — not a value, not `(CDO default)`. Same for `NiagaraSimCache.CacheFrames[]…`.
> `Aura.cpp`'s `(CDO default)` marker is only appended `if (!m.preview.empty())`, so an empty preview
> is upstream of it, in `Ubel::ResolvePropertyPreviews` not resolving struct/container-nested paths.
> A5's own wording does not cover deep rows, so this is a gap to confirm, not a failure of A5.
>
> **AE9 — both halves.** Value Search → First Scan (`424242`, 2 candidates in 52 ms) → Sort picker set
> to `Value` → **New Scan** → the picker reads **`Scan order`** again and the session ends. Then, on a
> result set with *varied* values (`Bigger` 400000 → **14,813 candidates**), picking `Value` re-sorted
> the whole set ascending — first rows went from `225000000, 1023969488, 549755813888…` (scan order)
> to `424242, 424242, 480256×4, 524288…`. Note the `Exact` predicate returns identical values and
> therefore **cannot** test a re-sort; that is why `Bigger` was used.
>
> **V6 — all three claims, ACROSS A MANUAL REFRESH ONLY.** Live Walker on `DumperTestActor_0`
> (reached via a Value Search row's `Live` button, which correctly scrolled to and selected
> `FrozenInt`): typed `Flag` → `3 matches`, `bFlagA` highlighted. Pressed **Refresh** → the keyword
> and `3 matches` survive, and the grid stays at the same region (`0x478…0x658`) instead of jumping
> to the top. Pressed the **▼ stepper** → it scrolled to and selected `bFlagA` at `0x670`.
> Highlights, anchor and stepper all survive a **user-pressed** refresh.
>
> ⛔ **What this does NOT cover, added 2026-08-19.** Every action above is a button press — the word
> *Auto* appears nowhere in this evidence. V6's step also says *"leave auto-refresh on for a few
> ticks"*, and on this build (**3262**) that was impossible: `[AUTOREFRESH-2026-08-19]` proves the
> countdown froze at `0s` after a single skipped tick and that **zero** auto-refreshes were issued
> in a logged session on it ("no periodic cadence exists anywhere in the session"). ⚠ *That session
> was a different machine on the same build, not a re-run of this one — the transferable fact is the
> code reading, which the `[AUTOREFRESH-2026-08-19]` entry confirms was byte-identical here.* The
> timer-driven path therefore remains **unverified** — see V6's
> corrected entry above. The manual half stands exactly as recorded.
>
> **Dump Explorer cross-game identity gate — PASS, on the two DumperTest flavours.** §8 promoted this
> row on the grounds that the gate compares main-module names and the two packages differ; that is
> now confirmed end to end. Dumped from **Development** (`Export → Dump All Metadata`, 3,942 classes /
> 10.5 MB, meta line records `module: "DumperTest.exe"`, `pe_hash 6A7EA60310F17000`), then the game
> was swapped for **Shipping** (`UE504`, **24,445** objects vs Development's 25,179 — a genuinely
> different binary) and the dump loaded:
>
> ```
> UE 5.4 · DumperTest.exe · 3,942 classes · 68,637 props · 9,806 funcs · 2026-08-17T22:22:03Z
> Live match refused: this dump is from DumperTest.exe, but the connected game is DumperTest-Win64-Shipping.exe.
> ✅ In current game — (run Re-check live)        <- EMPTY
> ⚠ Not checked yet — showing 2,000 of 82,385    <- everything
> ```
>
> Three things make it a pass rather than a shrug: the refusal **names both modules**, the
> *In current game* list is **empty** so nothing is falsely claimed present, and the 82,385 rows are
> labelled **"Not checked yet"** rather than *"Not in current game"* — refusing to match is not the
> same claim as absence, and the panel keeps them apart. `Re-check live` stays enabled as the
> deliberate override.
>
> **AF4 — PASS, and this one has no unit test by design.** Live Walker on `DumperTestActor_0` →
> switch to **Instances** → switch back. The object, the scroll region and the selection all survive.
> Then the real check, chosen so a dead callback cannot hide: search `Text_` (**8 matches**, and the
> `Text_*` fields live at `0x2A0…0x310`, far *above* the `0x4C8+` region on screen) and press the
> **▼ stepper** — the grid scrolls all the way up and selects `0x2A0 Text_Even2_OneNull`. Before the
> fix all six callbacks were dead after one round trip **and nothing errored**; a stepper that only
> had to move a few rows could not have told the difference, which is why the keyword was picked to
> force a long scroll.
>
> ⭐ **Free corroboration of the SDK-header export, from a different code path.** Every offset the
> Live Walker shows on this actor matches the exported header exactly — `0x639 U8_Small`,
> `0x63C I16`, `0x640 I32`, `0x650 F32`, `0x658 F64`, `0x670 bFlagA/bFlagB/bFlagC` (bits 0/1/2, masks
> 0x01/0x02/0x04, byte `05`), `0x671 bPlainBool`, `0x672 Grade`, `0x694 Health`, `0x6A8 TickCount`,
> `0x6AC FrozenInt`. Two independent emitters agreeing on the whole layout is stronger evidence for
> W2/W3 than either alone. *(Incidental for AA1: the bitfield byte currently reads `0x05`, the
> pre-toggle state that check expects to become `0x07`.)*

**Needs a specific condition (worth doing when it arises):**

5. **G1 + X3 — the offset banner.** On a game where offset detection partially fails, the Pointers
   tab must show the amber *"Dynamic offsets only partially measured (unmeasured:…)"* banner naming
   the probe. The pair is only observable together. ⚠ On a game where everything measures cleanly
   the correct result is **no banner at all** — absence proves nothing unless `get_offsets` on the
   same process reports `validated: true`, so check that too before concluding.
   **Host screening `[DQ7R-PIPE-2026-08-17]`: DQ7R is the NEGATIVE-control branch, not the positive
   one.** `get_offsets` reports `validated: true`, `probe_ran: true`, and emits **no** `unmeasured`
   key at all, so the only thing DQ7R can establish here is that a clean game shows no banner. **The
   amber half still has no host** — it needs a title whose offset detection *partially* fails, and
   screening for one is a single `get_offsets` call per title, so fold it into any future sweep rather
   than launching for it.
> ### ✅ V7 · AF6 PASS 2026-08-17 `[GRP4-UI-2026-08-17]`
>
> **V7 — the salmon line appears, forced the way the plan prescribes.** Live Walker on GWorld
> (`UWorld ThirdPersonMap`), then the **game process was suspended** from Python (`NtSuspendProcess`)
> rather than an actor destroyed — suspending leaves the object graph intact and stops only the DLL
> answering, which is precisely the "refresh could not complete" case. Pressing **Refresh** produced,
> under the status line and in salmon:
> ```
> Refresh timed out after 10s — the target object may have been destroyed in-game.
> ```
> Ten-second deadline, visible failure, and the grid kept its previous contents rather than blanking.
> The process was resumed immediately afterwards. Before this fix a dead refresh looked exactly like a
> live one.
>
> **AB6 — PASS, on a deliberately leaf-heavy set.** Group Scan `Exact 0` on both slots →
> **1,655 matching objects in 196 ms**, with slots keeping many leaves (`(+63)`, `(+89)`, `(+90)`,
> `(+91)`). Sorting by **First value** reordered the rows so the *rendered* first-slot values ascend
> monotonically: `-1`, `-0.00000000000111392`, `…110971`, `…110953`, `…110926`, `…110204`, … The sort
> follows the leaf that is actually **on screen**, not some other leaf the slot kept — which is only a
> meaningful check because each slot is holding ~63 of them.
>
> **AE8 — PASS, and both halves are visible.** A First Scan with an empty Value box is **rejected
> with an inline `Value is required.`** (red, box outlined) rather than silently ignored — and the
> **`Diagnostics — DLL dispatch cost`** table went 38 → 40 dispatches across the attempt, the two new
> entries being `get_diagnostics` and `end_group_scan`, both of which the operator caused. **No scan
> command appears for the rejected click**: it never reached the pipe, so it was never measured.
>
> *Two things fell out of the same panel.* The header reads `40 dispatches over 860.4s — dispatcher
> busy **0.2%** of wall-clock`, consistent with [multipipe-eval](multipipe-eval.md) §10's measured
> finding that the dispatcher is mostly idle and there is no head-of-line blocking to remove. And the
> **Pipe Activity** tail independently corroborates **V7**: `07:20:58.618 B → walk_world #6` is sent
> with **no matching `←` reply** — the very refresh that timed out while the process was suspended.
>
> **AF6 — PASS, on the evidence Y9 already produced.** Its ask is "a huge integer into Force → an
> explicit refusal *naming the substitute*, not a silent nothing", and `9999` into Force on a
> `ByteProperty` answers **`uint8 holds 0 to 255 — 9999 would be written as 15`**. The substitute is
> named and the value is not written. No separate run needed.

6. **V7 — failures are visible.** Live Walker an object, then destroy/unload it in-game and press
   Refresh. Expect the salmon error line under the status line (10 s timeout). Before this fix a
   dead refresh looked exactly like a live one.
7. **U7 — a CJK string preview.** Property Search a `StrProperty` holding non-ASCII text longer than
   50 bytes on a localized game. Before the fix the whole search returned zero rows with an error;
   success = rows come back and the preview ends in `…`.
> ### 🟡 U7 — TWO HALVES PASS, THE COMBINED CASE HAS NO FIXTURE 2026-08-20 `[U7-DQ7R-2026-08-20]`
>
> **DQ7R (localized, UE 4.27), headless over the pipe.** The step wants a `StrProperty` holding
> non-ASCII text **longer than 50 bytes**; success = *rows come back and the preview ends in `…`*.
>
> * ✅ **The hard-failure half is decisively disproved.** Before the fix "the whole search returned
>   zero rows with an error". Across 16 keywords the search returned **1,808 property rows** with no
>   error. That is the regression the item is really about.
> * ✅ **Non-ASCII previews decode correctly.** `StatusGaugeWidget.NumString1` / `NumString10` /
>   `NumString100` are `StrProperty` and preview as `"９" (CDO default)` — **U+FF19**, a genuine
>   multi-byte UTF-8 character, rendered intact with no replacement characters.
> * ✅ **The >50-byte truncation marker is emitted.** 55-byte previews such as
>   `Engine.WireframeMaterialName` end with **U+2026** followed by the closing quote.
> * ⬜ **The two together — a non-ASCII string over 50 bytes — has no fixture on this host.** Every
>   CJK preview found is 19 bytes (well under the threshold) and every >50-byte preview is an ASCII
>   asset path. The precise hazard (truncating at a BYTE limit splitting a multi-byte character)
>   therefore remains unexercised. It needs a title whose long `StrProperty` values are localized.
>
> ⚠ **A literal reading of "the preview ends in `…`" reports a false failure.** The marker sits
> *inside* the quotes — the tail is `… "` (U+2026, U+0022) — so `preview.endswith("…")` is **False**
> even when truncation worked perfectly. Test with `"…" in preview`, or match `…"`.

8. **AB6 — group sort follows the visible column.** Group Scan with a filter that makes a slot keep
   many leaves, then sort by Value. The order must match the Value column on screen.

9. **AF4 — the Live Walker survives a tab round trip.** This one has NO unit test by design (it is
   an Avalonia visual-tree lifecycle fact). Open Live Walker on an object → switch to another tab →
   switch back → then use **🌍 Locate in GWorld**, a bookmark restore, or the ↑/↓ match stepper.
   The grid must still scroll. Before the fix all six callbacks were dead after one round trip and
   nothing errored — the buttons just stopped moving the view.
10. **AF2 — unchecked rows say so.** Experimental → Detect Player Stats on a game with more than 30
    candidate classes. Rows past the cap must read **"? not checked"** in amber, not "· guess", and
    the status line must say *"30 of N classes live-probed"*. On a small game with under 30 classes
    the correct result is that the suffix is ABSENT — check both, or you have only tested one branch.

**Trivially checkable, low value alone:** AF6 (type a huge integer into Force → expect an explicit
refusal naming the substitute, NOT a silent nothing), AE8 (a rejected scan click should no longer
appear in the diagnostics measurement list), AF1 (needs a malformed UEnum — not reproducible on
demand), U7's sibling paths.

### 🟡 STEPS 1-4, 7, 8, 9 DONE — `[FREEZESCOPE-2026-08-18]` — Freeze must hold the subclasses too (**8 CLOSED 2026-08-24**; **5 CLOSED 2026-08-24** `[FZ5-PAWNBIT-2026-08-24]` — headless, no damage needed; 6 has no fixture)

*Needs a game with a **player pawn** and any inherited `AActor` bool (`bCanBeDamaged`, `bHidden`,
`bReplicates`) — i.e. any UE game. Runs in the same sitting as `[FREEZESTUCK-2026-08-18]` above.*

> **What is already pinned offline and must NOT be re-checked here:** 11 executable scope cases in
> `scripts/tests/freeze_helper_test.lua` (derived is the default; `derived = false` is honoured; the
> 16-byte page stride, with a negative control that an 8-byte read would return a class pointer as
> an address; per-entry identity witnesses; a witness-less entry dropped rather than written blind;
> a missing `ClassPrivate` offset refused rather than degraded; a filter dropping address and
> witness in lockstep; cap reported, and a control that an uncapped pool does not claim to be
> capped; a contract-2 DLL refused). Plus `dll_helpers_test`'s page-geometry + contract-3 layout
> assertions and `FreezeValueDialogValidationTests`' scope-summary/warning pair.
> **What no offline test can reach** is whether a real `Aura::FindInstancesDerivedFrom` sweep
> reaches the user's pawn — that is step 4.
>
> ⚠ **DLL and UI must be from the same build.** This moved the mailbox contract to **3**; a
> contract-2 DLL is refused up front with *"update UE5Dumper.dll"*, which is a correct answer, not
> a defect. See `[STALEDLL-2026-08-18]` for the February DLL that can be picked up instead.
>
> ⚠ Read the checkbox correctly (red ✗ = ACTIVE) and open CE's Lua Engine first, as above.
>
> | step | do this | expect |
> ### ✅ FREEZESCOPE steps 1 + 9 PASS 2026-08-20 `[FZSCOPE-PIPE-2026-08-20]` — over the pipe, no CE
>
> Rig: `tools/verify/freezescope_force_scope.py`, DumperTest / dist 3263.
>
> **Step 1 — PASS.** `bCanBeDamaged` resolves to exactly one row: `class=Actor`,
> `defining_class=Actor`, `BoolProperty`, `bool_mask=4`, **`inherited_by_count=221`** — the number the
> "+N inheritors" badge renders.
>
> **Step 9 (the cross-feature control) — PASS.** Force on that same row:
> ```
> force_field(Actor.bCanBeDamaged, kind=bool) -> ok resolved held=58 truncated=false
> get_forced_fields                            -> Actor.bCanBeDamaged held=58
> after reset_field + reset_all_fields          -> 0 held
> ```
> ⭐ **58 is the number that matters, because the pre-fix value on this exact host was 1.** The row
> records the failure as `1/1` "in a 25,179-object level" — this is that level, and Force now sweeps
> derived. Force and Freeze are therefore not scoping oppositely, which is the thing that started the
> whole finding.
>
> ⚠⚠ **Do NOT use `find_instances(exact_match=false)` as the baseline — it measures something else.**
> It reports `total=252` for "Actor", but that is a **name-substring** match: 210 of the 252 are CDOs
> and the non-CDO remainder is largely `ActorElementAssetDataInterface`-style objects that **do not
> derive from `AActor` at all**. Solide uses `Aura::FindInstancesDerivedFrom` — a real super-chain
> test with a per-UClass verdict cache — and skips CDOs *inside* the walk. Comparing 58 against 252
> (or against 42) looks like a discrepancy and is not one; the two numbers answer different questions.
>
> ### ✅ SOLIDE `capped` AND FREEZESCOPE STEP 4 BOTH PASS 2026-08-20 `[DQ7R-CAP-2026-08-20]`
>
> **DQ7R (UE 4.27, 149,370 objects), proxy mode, headless — `tools/verify/dq7r_batch.py` plus a
> mailbox probe.** The note below is right that 58 instances cannot reach a cap of 256. The fix was
> not to find a bigger *level* but to notice that **Solide's pool is not restricted to Actors** —
> `FindInstancesDerivedFrom` works off any base, and an asset/component base is enormous even at a
> title screen.
>
> Derived pool sizes measured through `CMD_LIST_INSTANCES` (contract-3 derived flag) before forcing
> anything:
>
> | base | derived total | at the mailbox's own 1024 cap? |
> |---|---|---|
> | `Object` | **1024** | yes (`CAPPED`) |
> | `Widget` | **1024** | yes (`CAPPED`) |
> | `MaterialInterface` | **1024** | yes (`CAPPED`) |
> | `Texture2D` | **998** | no |
> | `ActorComponent` | **819** | no |
> | `SceneComponent` | 581 | no |
> | `Actor` | 44 | no |
>
> **Solide `capped` — PASS.** Forcing `ActorComponent.bAutoActivate` (BoolProperty, offset 137,
> mask 0x80) across its 819-instance derived pool:
> ```
> force_field       -> ok=true resolved=true  held=256  truncated=true
> get_forced_fields -> ActorComponent.bAutoActivate held=256 truncated=true
> after reset_all_fields -> 0 held
> ```
> `held == 256 AND truncated == true` — **the cap is ADMITTED, not silently applied**, and
> `get_forced_fields` reports the same pair the force did, so report and reality are not computed by
> different code paths (audit #4's named root cause is absent here).
> ℹ️ `bAutoActivate` was chosen deliberately: it is read at component spawn, so forcing it across 256
> already-live components changes nothing observable, and Solide restores the captured base on
> release. The run ends with 0 held.
>
> **FREEZESCOPE step 4 — PASS on the mechanism.** `pipe-0.log` carries `scope=derived` throughout,
> e.g. `LIST_INSTANCES returned 64/1024 (page 1/16) scope=derived CAPPED` and
> `returned 64/130 (page 1/3) scope=derived`. The step's own bar is *"a returned count in the
> hundreds/thousands, **not `1/1`**"* — 819 / 998 / 1024 clear it comfortably.
> ⚠ **Scoped honestly:** the step names `class='Actor'` specifically, and on DQ7R at its title screen
> the derived `Actor` pool is **44**, not hundreds — no gameplay level is loaded, and 149,370 objects
> are overwhelmingly classes and assets. 44 still refutes the `1/1` the defect produced, but a
> hundreds-scale *Actor* count needs a title actually in play.

> 🟡 **The Solide `capped` badge is only half-checked here.** `truncated=false` is *correct* at 58,
> but the cap is 256, so this host never reaches it and the "held==256 AND truncated==true" assertion
> is untested. That needs a title with **>256 live instances derived from one base** — DQ7R (149k
> objects) is the obvious candidate.
>
> Steps 2, 3, 6, 7, 8 are CE/dialog work; step 4 needs the `.CT` record (or a `CMD_LIST_INSTANCES`
> mailbox poke); step 5 needs a player pawn taking damage.
> |---|---|---|
> | 1 | Property Search `bCanBeDamaged` (or `bHidden`) — a field the Class column shows as **`Actor`** with an `+N inheritors` badge | the row exists |
> | 2 | Click **Freeze** and read the dialog before typing anything | a **Scope:** line reading `every live Actor and every subclass (N inherit this field)`, plus a ⚠ line saying the field is declared on `Actor` and how to narrow it. Neither existed before |
> | 3 | Create the script and read the generated CFG | it contains `derived            = true,` |
> | 4 ⚠ THE ONE THAT MATTERS | tick the record, then check `Logs\<Game>\pipe-0.log` | `LIST_INSTANCES class='Actor' page=0 scope=derived` and a **returned count in the hundreds/thousands**, not `1/1`. Before the fix this was `1/1` in a 25,179-object level |
> | 5 | ✅ **CLOSED 2026-08-24** `[FZ5-PAWNBIT-2026-08-24]` — NOT by taking damage. `py tools/verify/freezescope_step5_pawn.py` reads the pawn's OWN `bCanBeDamaged` bit with `ReadProcessMemory`. | `0x74 → 0x70 → 0x74` (mask `0x04`, offset +90): armed clears it, release restores it, the other 7 bits never move. Negative controls: `StaticMeshActor` **held=30** and `WorldSettings` held=1 both leave the pawn's bit SET, and `ChaosDebugDrawActor` — the very class the pre-fix freeze held — resolves to **held=0**. |
> | 6 ⚠ the honesty half | if the log line ends in `CAPPED`, read the Lua Engine | it printed `CAP REACHED, so that is a floor, not a total: more instances exist and are NOT held`, and the Lua Engine window **stayed open** instead of auto-closing over the notice |
> | 7 ⚠ control | edit the CFG to `derived = false`, re-tick | `scope=exact` in the log and the old narrow pool returns — the flag is a real switch, not decoration |
> | 8 ⚠ control, backward compatibility | tick an **older saved .CT** whose freeze script predates contract 3 | it still runs and still holds its exact-class pool. The flag defaults off and the handler clears it, so an old script must be unaffected |
> | 9 ⚠ control, cross-feature | on the same row, use **Force ON/OFF** (Solide) | it reports a comparable instance count to step 4 — Force and Freeze sit on one row and must not scope oppositely, which is what started this |


> ### ✅ STEPS 2, 3 AND 7 PASS 2026-08-21 `[FREEZESCOPE-CFG-2026-08-21]` — and step 7 is a clean A/B on one record
>
> **Step 2 — the dialog says the scope out loud.** `Actor::bCanBeDamaged` (`BoolProperty`, `0x5A`),
> Freeze, read before typing:
> ```
> Scope:  every live Actor and every subclass (221 inherit this field)
> ⚠ bCanBeDamaged is declared on Actor, not on one specific object — so this holds the value on
>   EVERY live Actor and subclass at once, not just the one you were looking at. To target a single
>   class, edit className in the generated CFG block (or set derived = false for that class only).
> ```
> Both the `Scope:` line and the ⚠ line are present, and the ⚠ line names the exact remedy step 7
> then exercises. **PASS**
>
> **Step 3 — the generated CFG.** Read back out of CE with
> `getAddressList().getMemoryRecord(i).Script`:
> ```lua
> local CFG = {
>   className        = 'Actor',
>   derived          = true,  -- also hold every SUBCLASS (set false for exact class only)
> ```
> `derived = true,` as specified. **PASS**
>
> **Step 7 ⭐ — the flag is a real switch.** Same record, same class, same session; the ONLY change
> was `derived true → false` in the CFG (`gsub`, `substitutions=1`, verified by reading the script
> back), then re-enabled:
>
> | `derived` | log line | returned | `classWitness` |
> |---|---|---|---|
> | `true` | `LIST_INSTANCES class='Actor' … scope=derived` | **58/58** | `0x0` |
> | `false` | `LIST_INSTANCES class='Actor' … scope=exact` | **1/1** | `0x1F4A82BB800` |
>
> One variable changed, three observables moved together — scope, pool size, and the witness. **PASS**,
> and it independently re-confirms `AA2/AA3` step 3's narrowed assertion: `classWitness=0x0` is
> *correct* on a derived listing and a real pointer on an exact one.
>
> ### ✅ STEP 6 FULLY CLOSED 2026-08-24 — the CE half by `[FZ6-ENABLE-2026-08-24]`, the DLL half by `[FZ6-CAP-2026-08-24]` below
>
> **The CE half needed no CE.** What the row asked for was: with the pool over the cap, tick the
> record and observe (a) the *"CAP REACHED, so that is a floor, not a total"* line firing and (b) the
> Lua Engine window **staying open** instead of auto-closing over it. Neither is a fact about Cheat
> Engine — (a) is an ungated `print()` on the `elseif scapped` arm and (b) is the **absence** of a
> call gated on `... and not scapped ...`. Both are decided by the emitted block's own Lua.
>
> ⭐⭐ **But a text assertion could not have closed it, and here is the proof rather than the claim.**
> `FreezeScriptGeneratorTests.cs:771` already asserts `Assert.Contains("CAP REACHED", enable)`.
> Replace `elseif scapped then` with `elseif false then` — the arm becomes **dead code** — and the
> string is *still in the file*, so that assertion **still passes**. Measured: `grep -c` returns 1.
> **A text assertion structurally cannot tell a reachable branch from dead code.**
>
> So `scripts/tests/freeze_enable_capped_test.lua` **loads and RUNS** the real emitted `[ENABLE]`
> block (`load(enableSrc, 'ENABLE', 't', env)`) over stubbed CE globals, with
> `getLuaEngine().Close` **recording** rather than ignoring the call. 19 checks, 0 failures.
>
> ⚠ **The trap it had to handle, and it is not obvious.** The `[ENABLE]` block loads the helper
> ITSELF via `findTableFile`, with an early `return` at `FreezeScriptGenerator.cs:243`. A rig that
> merely pre-defines `freezeProperty` in `_G` never reaches the capped arm: the block returns there
> having printed nothing and closed nothing — which reads as *"no CAP line, window stayed open"*,
> i.e. **a PASS on the capped case for entirely the wrong reason**. The rig stubs
> `findTableFile`/`createStringStream` to actually serve a helper, and asserts the bail-out as its
> own case so the state is named rather than mistaken for success.
>
> ⚠ **Three negative controls, on the FIXTURE, so each targets one claim:**
>
> | control | result |
> |---|---|
> | drop `not scapped` from the close gate | **`capped: the window is NOT closed` FAILS — `got: true`**. The rig watched the close actually fire |
> | `elseif scapped` → `elseif false` (dead arm) | the CAP-line checks fail — while `Assert.Contains` on the same file still passes |
> | fixture text ≠ generator output | `TheCheckedInFixture_IsStillWhatTheGeneratorEmits` fails, naming the regeneration step |
>
> ⭐ **The arming control is the load-bearing one**: the same run with `capped = false` must close
> the window (`closed == true`). Without it, *"the window stayed open"* is equally consistent with a
> close that never fires at all — which is exactly what the `findTableFile` bail-out produces.
>
> ℹ️ **The fixture is CHECKED IN**, at `scripts/tests/fixtures/freeze_enable.lua.txt`, not captured
> by hand into `out/`. `contract_check_test.lua` and the two `slotsym` rigs depend on manual captures
> in that gitignored scratch dir and are simply **unrunnable on a clean checkout**; this one is not.
>
> **The seam between the two halves is asserted, not assumed:** this rig owns everything after
> `start()`'s 5th return value, `freeze_helper_test.lua:785` owns everything before it (a real
> `LI_OUT_TRUNCATED` reply → `capped == true`, with an uncapped control), and the rig's anti-vacuity
> check pins the destructuring `local sok, sok2, serr, scount, scapped` so a change to `start()`'s
> arity fails loudly instead of silently splitting them.
>
> ### ✅ Step 6's DLL half CLOSED 2026-08-24 `[FZ6-CAP-2026-08-24]` — the fixture DOES exist now
>
> `Spawn_ManyComponents` ships in the packaged DumperTest (the repo's copy of the sample source is
> stale and does not show it — see `[C1-SPAWNER-EXISTS-2026-08-24]`). Driving it over the mailbox
> pushes the derived pool past the cap, which is exactly what the block below says could not be done
> here. DLL 3350, `list_instances(..., derived=True)`:
>
> | class | BEFORE | AFTER `Spawn_ManyComponents(1500)` |
> |---|---|---|
> | `ActorComponent` | total **286**, pages 5, `outflags=0` | total **1024**, pages 16, **`outflags=1`** |
> | `SceneComponent` | total **257**, pages 5, `outflags=0` | total **1024**, pages 16, **`outflags=1`** |
>
> The BEFORE row is the negative control: same command, same class, flag **clear** — so `outflags=1`
> is the cap firing and not a bit that is always set.
>
> ⚠ **The attribution caveat, which survives and must not be glossed.** `LIST_INSTANCES_MAX_DERIVED`
> is **1024** and `LIST_INSTANCES_MAX_PAGES` is **16** at 64 derived entries per page — so the DLL's
> cap and the helper's page ceiling land on the **same number** (`Mimic.h:414-421`, and the comment
> at :406 says so outright). Over the **CE Lua helper** path, no reachable pool size separates *"the
> DLL reported truncation"* from *"the helper ran out of pages"*. ▶ Over the **mailbox** path used
> here the helper is not in the loop at all, and `outflags` is the DLL's own `LI_OUT_TRUNCATED` bit,
> so this result is attributable — but it does **not** transfer to the helper's `CAPPED` message.
>
> ℹ️ Still owed, unchanged: the emitted script's honesty line firing **at runtime** and the
> stay-open behaviour. Those are the CE half, and they are where the ambiguity above bites.
> ℹ️ Rig hygiene: `list_instances` returns the raw page `blob`, and printing the whole dict buried
> the four numbers that matter under ~2 KB of hex. Print `total`/`pages`/`outflags` only.
>
> ### ~~⛔ Step 6 has no fixture on DumperTest — measured, not assumed~~ (superseded above)
>
> Step 6 needs the log line to end in `CAPPED`, which requires a derived pool larger than the DLL's
> **1024-entry derived cap** (`ue5_freeze_helper.lua`: *"two result caps … 2000 exact / 8-byte
> entries, 1024 derived / 16-byte"*). The pools actually measured on this title:
>
> | class | derived instances |
> |---|---|
> | `Actor` | 58 |
> | `PrimitiveComponent` | 145 |
> | `ActorComponent` | 819 *(Solide's earlier sweep, `[DQ7R-CAP-2026-08-20]` era)* |
>
> All below 1024. ⚠ `find_instances` cannot be used to look for a bigger one — it is a **name
> substring** match, not a derived walk (`class_name='ActorComponent'` returns a 3-class histogram of
> classes whose *names contain* the string, not the subclass pool).
>
> **What IS verified about step 6**: the honesty message exists in the emitted script and is an
> **ungated `print`**, so nothing suppresses it —
> `print('[Freeze] armed on ' .. tostring(scount) .. ' instance(s) of Actor or a subclass -- CAP
> REACHED, so that is a floor, not a total: more instances exist and are NOT held. Narrow className
> in CFG to cover the ones you want.')` — read out of the generated CFG in step 3. Its **runtime
> firing and the stay-open behaviour were not observed**, and cannot be here.
>
> ### ✅ STEP 8 PASSES 2026-08-24 `[FREEZESCOPE-8-OLDCONTRACT-2026-08-24]` — the pre-contract-3 artifact existed all along, in the HELPER
>
> ⛔ **This supersedes the "Step 8 has no fixture either" note below.** That note said *"no
> pre-contract-3 `.CT` exists on this machine … it would have to be reconstructed, which tests the
> reconstruction."* The reconstruction objection was right; the premise was not. **The contract is
> not carried by the `.CT`, and not by the generated script — it is carried by the HELPER**, as
> `local UE5_SCRIPT_CONTRACT = <n>`. Measured:
>
> ```
> grep -c CONTRACT scripts/UE5CEDumper.CT                  -> 0
> grep -n  Contract ui/.../FreezeScriptGenerator.cs         -> (nothing)
> git show 04d40803^:scripts/ue5_freeze_helper.lua          -> v1.1, UE5_SCRIPT_CONTRACT = 2
> ```
> That helper is a period artifact from commit `661c3925` (**2026-08-16**), and contract 3 landed in
> `2c2a950c` (**2026-08-19**) — so it genuinely predates it and nothing was reconstructed.
>
> **Fixture, and why this class and not the one the other freeze rows use.** Exact-vs-derived *is*
> the assertion, so the base needs a real subclass. `ADumperTestDerivedHolder : public
> ADumperTestHolder` exists for exactly that, and `ADumperTestHolderDecoy` **does not derive** while
> its class name *contains* the base's — the negative half, which catches a scope computed from a
> string test. `HolderValue` is seeded **1000 + i**, distinct per instance, so an unfrozen instance
> can never be mistaken for a frozen one. 30 + 30 + 8 live instances, DLL **3338**, CE **7.7**,
> DumperTest Shipping. Ticked from the **address-list checkbox** — the normal user path.
>
> | phase | embedded helper | `DumperTestHolder` | `DumperTestDerivedHolder` | `DumperTestHolderDecoy` |
> |---|---|---|---|---|
> | **arm** | 1.1, `UE5_SCRIPT_CONTRACT = 2` (29,535 B) | **30/30 @ 9999** | **0/30** — still 1030…1059 | 0/8 |
> | **control** | 1.5, contract 3 (58,802 B) | 30/30 @ 9999 | **30/30 @ 9999** | 0/8 |
>
> * **"it still runs"** — the base pool going 30/30 is the load-bearing half. A contract-2 script
>   against a contract-3 DLL passes the range check (`MIN 1 ≤ 2 ≤ 3`) and **actually writes**.
> * **"still holds its exact-class pool"** — derived untouched under 1.1, fully held under 1.5.
> * ⭐ **The control is what makes the empty derived pool mean "narrow scope" instead of "nothing
>   ran"** — and note the base pool alone already rules the second reading out, so the two legs are
>   independent rather than restatements.
> * **The decoy is 0/8 in BOTH phases** — the derived scope is reached by *derivation*, never by the
>   class name that contains the base's.
>
> ⚠ **What this does NOT cover, stated rather than glossed.** The generated freeze *script* was
> **current**, not period — only the helper is the old artifact. That is defensible because the
> script never touches the mailbox: it fills a `CFG` table and every mailbox write is the helper's,
> so the contract-bearing component is the one that was swapped. A current script setting
> `cfg.derived = true` is simply a key a 1.1 helper has no concept of, and the measured result is the
> exact-class pool the row predicts. What is untested is a *period-generated script text*, which
> would need a `git worktree` build of the old UI.
>
> ⚠ **CE has ONE Lua state, and 1.1 does not gate its definitions on version.** Loading 1.1 over a
> resident 1.5 leaves `UE5_FREEZE_HELPER_VERSION` at `'1.5'` (1.1's guard is
> `if not UE5_FREEZE_HELPER_VERSION`) while `freezeProperty` *does* become 1.1's — a mixed state that
> would have made the run unattributable. Both globals were cleared to `nil` before each phase, and
> the embedded size was read back each time (29,535 / 58,802) so which helper ran is a measurement,
> not an assumption.
>
> ▶ Rig: `tools/verify/freezescope_step8.py` (`spawn` / `read` / `verdict --phase arm|control`).
> Its `spawn` also spawns **8 decoys** — with zero of them, "no decoy was held" is trivially true and
> the decoy control is vacuous.
>
> **Remaining on this row: steps 5 and 6 only** — 5 needs damage taken on the player pawn, 6 has no
> fixture.
>
> ### ⛔ SUPERSEDED 2026-08-24 — "Step 8 has no fixture either" was wrong about WHERE the contract lives (see the PASS above)
>
> Every `.CT` on disk is current-era (`scripts/UE5CEDumper.CT`, `dist/UE5CEDumper.CT`,
> `out/teleport_rows.CT`). Backward compatibility against a script predating contract 3 cannot be
> tested without an old artifact; it would have to be reconstructed, which tests the reconstruction.
>
> **Remaining: step 5 only** — `bCanBeDamaged` frozen to `false` and then *taking damage on the player
> pawn*, which needs someone playing.
>
> ⚠ **Incidental — `[FREEZEUNTICK-2026-08-20]` reproduced a THIRD time**, on a fresh CE process and a
> fresh table: ticking with the helper absent raised the setup message and left the record at red ✗
> (Active). Three CE sessions, three reproductions.
>
> ℹ️ CE's known-harmless `TCustomForm.SetFocus … frmLuaEngine … Can not focus (EInvalidOperation)`
> fired when the script flipped record state from the Lua console; CE auto-saved the table to
> `ExceptionAutoSave_DumperTest.ct` **in the repo root**, which was deleted. Worth knowing: driving
> record state from Lua can litter the working tree.

### ℹ️ SUPERSEDED, NOT OPEN WORK — original AA1 steps, kept for the method

> Its successor **closed 2026-08-18** (the packed-bitfield freeze row just above). The ⬜ here was
> making a deliberately-archived section count as an open row in every sweep.

Sibling of the Y15 check below, same panel, same failure shape — a whole-byte write over a field that
does not own the whole byte. Freezing a `BoolProperty` now emits `boolMask` into the generated CFG and
the helper writes only that bit. 24 unit tests plus a negative control cover the C# and the helper
*source*, **but the DLL→UI half has never run against a real game**: nobody has seen a real packed
bool's `bool_mask` arrive on the `search_properties` wire.

**Needs a game with a `uint8 bFoo:1` bitfield bool** — extremely common on `AActor`
(`bHidden`, `bReplicates`, `bCanBeDamaged` are bitfields on many UE versions), so any UE game should do.

1. Property Search a bool on a live class. In the row, generate the freeze script.
2. **Read the generated CFG.** A packed bool must show `boolMask = 0xNN,` (one of 0x01…0x80). Its
   *absence* is the whole finding, so this line is the check — if it is missing, the mask is not
   reaching the UI and everything below is moot. A **native** bool correctly shows no `boolMask`;
   confirm you are looking at a packed one (Live Walker shows the mask in the field's tooltip/CSX
   description).
3. Note the **whole byte** at `prop_offset` in Live Walker / CE before enabling.
4. Enable the script, let it tick, and re-read that byte. Success = only the masked bit changed;
   **failure = the byte became `0x00` or `0x01`**, which is the pre-fix behaviour.
5. The nastiest half of the old bug: when the mask is **not** `0x01`, the intended bool was never set
   at all. So also confirm the target bool actually reads as the value you froze.

### ℹ️ SUPERSEDED, NOT OPEN WORK — original Y15 steps, kept for the method

> Its successor **closed 2026-08-18** (the 1-byte-enum freeze row just above). Same note as AA1.

Freezing an `EnumProperty` now picks its writer from the width the engine reported instead of always
using a 4-byte `writeInteger`. The mapping and both call sites are unit-tested with four negative
controls, **but nobody has watched a real 1-byte enum freeze leave the following bytes alone** —
which is the actual damage the finding is about.

Needs any connected game with an `enum class : uint8` field (Property Search → type filter
`EnumProperty`; almost every UE game has several — states, stances, difficulty, team).

1. **Property Search** → type filter `EnumProperty` → pick a row. **Live Walker** the owning class
   and write down the values of the **three fields immediately after** it (or read the raw bytes at
   `offset+1..offset+3`). This is the baseline; without it the rest proves nothing.
2. Back in Property Search, **Freeze** that row. The dialog's **Type** line must read
   `EnumProperty -> uint8`, *not* `-> int32`, and the value box must pre-fill **`255`**, not `9999`.
   Those two are the only places the fix is visible before the script runs.
3. Type `9999` → expect *"uint8 holds 0 to 255 — 9999 would be written as 15"* (Y9's check now
   reaching enums). Correct it to a valid enum value and generate.
4. Enable the script in CE, let it tick a few seconds, then **re-read the three neighbouring fields
   from step 1. They must be unchanged.** Before this build they were overwritten 20x/sec.
5. Confirm the CE script's CFG line reads `valueType = 'uint8'`.
6. If the game has a **4-byte** enum (rarer — a plain `enum`, not `enum class : uint8`), repeat: it
   must still map to `int32`. That is the no-regression half; 4 is the one width the old code was
   right about.

### ✅ ALL 5 CLOSED 2026-08-18 — AA(B) / FIRE on a class past the 5,000-row cap (audit #5 X2, build 2888)

The three handoffs that need a class address stopped re-deriving it from the capped `list_classes`
page and now use the address the row already carries. The pure logic is unit-tested with three
negative controls, **but the end-to-end path is not**: no test issues a real `walk_functions` against
an address sourced from `list_all_functions`.

Needs a game with **more than 5,000 classes** — any large UE title (DQ7R, Hogwarts Legacy, FF7R).

1. **Game Class Filter → Load.** Confirm the status line ends with
   *"⚠ STOPPED at the 5,000-row cap — more classes exist"*. If it does not, this game is too small
   and the rest of the check proves nothing — pick another title.
2. **Interesting Funcs → Load**, then pick a row whose class is **absent** from the Game Class Filter
   list (that is what "past the cap" means; filter by the class name there to confirm the absence).
3. Click **AA(B)** on that row. Success = the script generates / reaches CE. Before this build it
   aborted on *"Class X not found"*.
4. Repeat on the **Console** tab with an exec command taking parameters (**Run** → the FIRE dialog)
   and with its own **AA(B)** — those two twins were not named by the finding and share the fix.
5. Worth one negative case: a class that genuinely does not exist should still report plainly
   *"not found"*, not the "may still exist" caveat. The Live-handoff path (no live instance +
   an unknown class name) is where that text appears.

> ### ⛔ STEP 1 SAYS STOP — **DQ7R IS TOO SMALL**, and this row's own candidate list is wrong
> `[DQ7R-2026-08-18]`
>
> Classes tab → Load, on **DQ7R** (UE 427, 199,196 objects, DLL 3262), both ways:
> * `Game classes only` **on**  → `2888 classes (scanned 199,196 objects, 2888 total UClasses)`
> * `Game classes only` **off** → `4738 classes (scanned 199,196 objects, 4738 total UClasses)`
>
> **4,738 < 5,000, so the cap is never reached and the status line carries no
> `⚠ STOPPED at the 5,000-row cap` at all.** Step 1 is explicit that this makes the rest of the row
> meaningless here, so nothing further was attempted on this title.
>
> ▶ **Correct the row's candidate list.** It names "DQ7R, Hogwarts Legacy, FF7R"; DQ7R does **not**
> qualify. A host that plausibly does, and is already known-good for driving: **Elliot** — its own
> `list_all_functions` status line this session read *"20239 functions across **6612 classes**"*, and
> classes-with-functions is a **lower bound** on total UClasses, so Elliot is ≥6,612 > 5,000.
> (Object counts point the same way: Elliot 355,717 vs DQ7R 199,196.) ⚠ That is a lower bound from a
> *different* command — confirm with the Classes tab's own total before relying on it.

> ### ✅ STEPS 1-3 PASS ON ELLIOT `[ELLIOT-X2-2026-08-18]` — the lower-bound inference held
>
> Elliot, UE **504**, 355,679 objects, `dxgi.dll` proxy build **3262**, AOBMaker **Connected**.
>
> | step | verdict | evidence |
> |---|---|---|
> | 1 | ✅ **PASS** | Classes tab → Load → `5000 classes (scanned 355,679 objects, 5000 total UClasses)` **`⚠ STOPPED at the 5,000-row cap — more classes exist`** — verbatim. Confirms Elliot qualifies where DQ7R (4,738) does not |
> | 2 | ✅ **PASS** | Interesting Funcs → Load → `20235 functions across 6609 classes`. Took the top row's class **`BP_EnemyCharacter_C`** and filtered the *capped* Classes list by that exact name → **0 rows**. So it is in the function list and absent from the class page: past the cap by construction, not by assumption |
> | 3 | ✅ **PASS, end to end** | `AA(B)` on `BP_EnemyCharacter_C::SetBlockDispHPGauge` → the dialog **opened** (`ParmsSize=1`, `bBlock [bool, 1B, off=0]`) instead of aborting on *"Class … not found"*, and `Copy AA Script` **reached CE**: the record `Invoke (baked): BP_EnemyCharacter_C::SetBlockDispHPGauge` is in the address list. That is the whole finding — the handoff used the address the row carries rather than re-deriving it from the capped page |
> | 4 | ⬜ **NOT DECIDABLE ON ELLIOT — and the reason is measured** | The Console twin needs an `exec` command **on a past-cap class**. Elliot has **none**: with `Game Only` on, Console reports `No UFUNCTION(exec) commands found in this game (scanned 12,822 functions across 3,935 classes)`. All **94** exec commands it does find sit on engine classes (`CheatManager`, `AISystem`, `AbilitySystemCheatManagerExtension`), and `CheatManager` is **present** in the capped list (4 matching rows), i.e. inside the first 5,000. Running the twin here would pass **vacuously**. ▶ Needs a title with **>5,000 classes AND game-class exec commands** — check the Console tab's `Game Only` count before committing to a host |
> | 5 | ⬜ not run | The negative case (unknown class must say plainly *"not found"*, not the "may still exist" caveat) goes through the **Live handoff**, which was not staged this session |

> ### 🔁 SECOND SITTING `[ELLIOT-X2b-2026-08-18]` — step 4 CONFIRMED vacuous, step 5 is MIS-SPECIFIED
>
> Elliot, UE **504**, DLL **3262**, title screen (84,990 objects — smaller than the first sitting's
> 355,679, and it does not matter: `game_only=false` still returns **5,000 with `truncated=true`**,
> so the cap precondition holds).
>
> **Step 4 — ⛔ NOT DECIDABLE HERE, now confirmed by a SECOND, independent method.** The first
> sitting read the Console tab's counts; this one tested class membership over the pipe. Every
> exec-bearing class sits **inside** the capped page, by index:
> `CheatManager` **idx 2836**, `AISystem` **idx 1120**, `AbilitySystemCheatManagerExtension` **idx
> 3900** — all < 5,000. The controls agree in the other direction: `BP_EnemyCharacter_C` and
> `BP_PlayerCharacter_C` are **absent** from the page (that is why step 3 could use them).
> ⇒ The Console twin's fix only bites when the exec command's class is past the cap, so running it
> here would go **green while proving nothing**. Still needs a title with **>5,000 classes AND
> game-class exec commands**.
> ⚠ `list_all_functions` with `game_only=false` on this title **does not return** (killed at 10 min);
> the membership test above needs only `list_classes`, so prefer it.
>
> **Step 5 — ✅ the REACHABLE half passes, and the half the row asks for is UNREACHABLE BY DESIGN.**
> Staged exactly as the row describes: `WBP_DebugLevelJumpItem_C` is past the cap **and** has zero
> live instances (verified over the pipe first), so its `Live` button takes the no-live-instance
> path. The UI fell back to Class Struct and logged, verbatim:
> ```
> [WARN] InterestingFunctions navigate: WBP_DebugLevelJumpItem_C not in the class list
>        — it was CAPPED at 5,000 rows, so the class may still exist
> ```
> That is the **correct** answer — the class does exist, it is merely past the cap — and it is the
> branch build 2888 added. **But the row asks to see the OTHER branch, and two independent facts stop
> it:**
> 1. `ClassAddrLookup.MissReason` ([`Models/ClassListResult.cs:123`](../ui/UE5DumpUI/Models/ClassListResult.cs))
>    selects on **`Truncated`**, never on whether the class exists. On any game that hits the cap the
>    answer must be the caveat; asking for a plain *"not found"* there is asking the UI to claim
>    knowledge it does not have.
> 2. **A class that "genuinely does not exist" cannot reach this path at all.** All four call sites
>    (`MainWindowViewModel.cs` 1178 / 1232 / 1392 / 1775 — Interesting Funcs, Interesting Props,
>    Console/exec) take `className` from a **discovered row**, so the class always exists; and they
>    all call `ListClassesAsync(gameOnly: false)`, whose result on a **non**-truncating game is the
>    complete class list — in which that class is therefore present, so the miss branch does not fire
>    either. Dump Explorer, the one panel that knows about classes *not* in the game, hands off via
>    `NavigateToLiveWalker(addr)` / `NavigateToInstanceFinder(className)` and never touches
>    `FindClassAddr`.
> ⇒ `MissReason == "not found"` is reachable only in a narrow race (a class collected between the
> function-list and class-list calls). **It is not stageable deliberately, and the row's own note
> that the pure logic already has three unit-tested negative controls is the right place for it.**
> Rewrite step 5 as *"confirm the CAVEAT is what a past-cap miss reports"* — which is what passed here.

> ### ⛔ EVERSPACE 2 ALSO FAILS STEP 4 — and the reason looks STRUCTURAL `[ES2-X2-2026-08-18]`
>
> ES2 (UE **505**, **1,150,301** objects, in-game) is the best candidate yet on the first criterion:
> Interesting Funcs reads **`29805 functions across 6324 classes`** — comfortably over 5,000, while
> the Classes tab reads `5000 … ⚠ STOPPED`, which is `[CLASSTOTAL]` in one screenshot.
>
> **But its exec commands are on a class INSIDE the cap, so it decides nothing.** Console → Game Only
> finds **6** exec commands, all on **`ESGameInstance`** — and that class sits at **index 22** of the
> walk. Every exec-bearing class that actually exists is inside:
>
> | class | idx | inside cap |
> |---|---|---|
> | `ESGameInstance` | **22** | ✓ |
> | `ESPlayerController` | 25 | ✓ |
> | `PlayerController` / `Character` / `WorldSettings` | 64 / 68 / 71 | ✓ |
> | `AISystem` / `GameInstance` / `CheatManager` | 1034 / 2266 / 2745 | ✓ |
>
> ▶ **The structural claim this suggests, and it should be tested before another host is hunted:**
> `UFUNCTION(exec)` lives on **long-lived singletons constructed at startup**, which is exactly what
> puts them at the FRONT of GObjects — so they are inside the first 5,000 *by construction*. A
> past-cap class is by definition late-registered (a `BP_*_C` loaded with content), and Blueprint
> classes essentially never declare native exec commands. **Step 4 may therefore be near-unstageable
> on any title**, not merely on Elliot and ES2. If so, the honest resolution is to close it against
> the shared fix (below) rather than keep hunting.
>
> **What ES2 does establish, even vacuously:** the Console **AA(B)** twin resolves a class and opens
> its dialog — `Invoke: ESGameInstance::SetRichPresence`, `(ParmsSize=4)`,
> `PresenceId [int32, 4B, off=0]` — and **Copy AA Script** produced a complete, well-formed script
> (`AA Script ready: …`; AOBMaker offline so it went to the clipboard, read back and checked: correct
> `invokeUFunction('ESGameInstance','SetRichPresence', 4, PARAMS)`, the helper-file guard, and the
> untick-on-bail-out shape). The two twins share the class-address resolution the fix changed, so
> this is evidence the Console path works — it just cannot be *past-cap* evidence here.
> ⚠ Nothing was fired: these commands have real side effects (`SetAchievement`,
> `UnlockAllAchievements` touch the user's Steam account). The defect is in resolving the class
> **before** FIRE, so opening the dialog is the whole check.
>
> ### ⛔ AVOWED TOO — third title, richest exec surface anywhere, still ZERO past the cap `[AVOWED-X2-2026-08-18]`
>
> Avowed (UE **504**, **281,501** objects, save loaded) is the strongest candidate the machine has:
> **8,780 classes** and **281 exec functions across 22 classes** (193 of them on game classes — the
> figure the UI's Console reports, which is how the detector below was validated).
>
> **All 22 exec classes are INSIDE the cap. The highest is index 4929, seventy-one short of 5,000.**
>
> | idx | class | cmds | | idx | class | cmds |
> |---:|---|---:|---|---:|---|---:|
> | 5 | `AlabamaPlayerController` | 2 | | 2535 | `GameInstance` | 2 |
> | 36 | `DebugCameraController` | 2 | | 2657 | **`AlabamaCheatManager`** | **152** |
> | 55 | `AlabamaGameModeBase` | 6 | | 2831 | `PlayerInput` | 5 |
> | 59 | `PlayerController` | 14 | | 3094 | `CheatManager` | 50 |
> | 170–251 | `GameMode`/`GameHud`/`HudBase`/`HUD` | 13 | | 3301 | `ActivitiesSubsystem` | 5 |
> | 1092 | `AlabamaGameInstance` | 4 | | 4080–4102 | `AlabamaAutoPlayer`/`DevUtility`/`UiCheatManagerExtension` | 6 |
> | 1265–2050 | `AISystem`/`FogOfWarSubsystem` | 5 | | 4412–4929 | `HealthSnapshotBlueprintLibrary`/`UiCheatManagerExtension` | 12 |
>
> ⇒ **THE STRUCTURAL CLAIM IS NOW CONFIRMED ON THREE TITLES** (Elliot 0 game execs; ES2 6, all at
> idx 22; Avowed 281, all ≤ 4929) **and it should be refined**: it is not merely "startup singletons
> sit at the front". Every exec-bearing class is a **natively-declared C++ class**, registered while
> modules load — i.e. before content. The classes *past* the cap are the tail of the walk, which is
> content-loaded Blueprint assets (`BP_*_C`, `WBP_*_C`, `GA_*`), and a Blueprint cannot carry a
> native `UFUNCTION(exec)`. **A past-cap exec command is therefore close to a contradiction in terms.**
>
> ▶ **Recommendation: close step 4 against the shared fix rather than hunt a fourth host.** Step 3
> already proved the past-cap path end-to-end (`BP_EnemyCharacter_C::SetBlockDispHPGauge`, AA(B)
> dialog + script into CE), and the Console twin shares that same class-address resolution — ES2
> exercised it successfully, just not past the cap. Hunting further has a poor prior.
>
> ### ⚠ THE DETECTOR WAS WRONG TWICE, AND EACH TIME IT RETURNED A CLEAN ZERO
> Recorded because a zero from a broken detector is indistinguishable from a real absence — the exact
> failure this row keeps producing:
> 1. Read `flags` / `name`; the reply's fields are **`function_flags`** / **`func_name`** → every
>    lookup was `None` → "EXEC: 0 across 0 classes".
> 2. Fixed the field, then used **`FUNC_Exec = 0x100`** from memory. `0x100` is **`FUNC_NetRequest`**;
>    the real value is **`0x200`**, and this repo states it in
>    [`ConsoleViewModel.cs:15`](../ui/UE5DumpUI/ViewModels/ConsoleViewModel.cs) — still "EXEC: 0".
>
> **What made it safe in the end was a cross-check against a number computed by other code**:
> `game_only=true` must yield the UI's own `193`. It does, exactly, so the 281/22 figures are
> trustworthy. **Never accept a zero from a filter that has not been shown to fire.**
>
> ### ✅ STEP 4 CLOSED, NON-VACUOUSLY — by LOWERING the cap instead of hunting a host `[AVOWED-X2b-2026-08-18]`
>
> **The maintainer's idea, and it is the right one.** Three titles proved no game ships an exec
> command past 5,000 (above), so the row looked unstageable. But the code's only input is *"is this
> class absent from the page it was handed"* — **it never sees the cap's value**. Lowering the cap
> puts a real class outside it, which is the identical condition, reached without a fourth host.
>
> ⚠ Note the asymmetry with the warning further up: **raising** the cap would hide the defect
> forever; **lowering** it exposes the defect on demand. They are not the same act.
>
> **Setup.** `int limit = 5000` → `3000` in the two UI defaults
> ([`IDumpService.cs:242`](../ui/UE5DumpUI/Core/IDumpService.cs),
> [`DumpService.cs:2549`](../ui/UE5DumpUI/Services/DumpService.cs)) — every call site omits the
> argument, so two lines move all of them. **No DLL rebuild, no game restart.** Avowed, UE **504**,
> **289,018** objects, save loaded, game paused (which also stops the walk order drifting).
>
> | step | verdict | evidence |
> |---|---|---|
> | 1 | ✅ | Classes → Load → `3000 classes (scanned 289,018 objects, 3000 total UClasses) ⚠ STOPPED at the 3,000-row cap — more classes exist` — verbatim, and the message tracks the new limit |
> | 2 | ✅ | Filtering that page for `Activi` returns **0 rows** ⇒ `ActivitiesSubsystem` is absent from the capped page — the UI's *own* witness of absence, which is what the handoff will see |
> | 4a | ✅ **AA(B)** | `Invoke: ActivitiesSubsystem::EnableActivity`, `(ParmsSize=17)`, `activityID [FString, 16B, off=0]` + `Enabled [bool, 1B, off=16]`. **Copy AA Script** → `AA Script ready: ActivitiesSubsystem::EnableActivity` |
> | 4b | ✅ **Run** | The **FIRE dialog** opened with both parameter fields and `FIRE / Copy AA Script / Close / Cancel`. **Cancelled — nothing fired** (`exec EnableActivity cancelled`); these commands mutate a live save, and the defect is in resolving the class *before* FIRE, so opening the dialog IS the check |
>
> ⇒ **Both Console twins resolve a class the capped page does not contain.** Before build 2888 this
> aborted with *"Class … not found"*. Step 4 is closed on real behaviour, not by argument.
>
> ⚠ **What this does and does not prove.** It proves the handoff works when the class is *absent from
> the returned page* — the only input the code has. It does **not** separately exercise index >5000,
> and no claim is made that it does. Given the code never reads the limit, that is not a gap.
> ⚠ **The cap change was reverted and verified** (`grep` shows `5000` restored, `3000` gone,
> `build_number.txt` back to 3261, `dist` republished AOT).
>
> ### ⚠ TWO TRAPS THIS RUN, both of which produced convincing wrong answers
> * **The rig's `max_results` was silently ignored — the DLL reads `limit`.** So a "cap = 3000" query
>   quietly returned **5000 rows**, and the class indices taken from it disagreed with what the UI saw.
>   That is the **third** wrong-field-name of this sitting (`flags`→`function_flags`,
>   `name`→`func_name`, `max_results`→`limit`): the pipe silently ignores unknown keys, so a wrong
>   name is never an error, just a wrong answer. **Echo one known value back before trusting a query.**
> * **Walk position is NOT stable while the game streams.** `HealthSnapshotBlueprintLibrary` sat at
>   index 4412 in one query and 2582 in another minutes later. So "past the cap" must be re-checked
>   at the moment of the test — and the UI's own filter is the right witness, not a stored index.
>   Pausing the game stabilises it.
>
> ### ⛔ FOUND WHILE DOING THIS — `[PASTECRASH-2026-08-18]`: a clipboard paste can KILL the UI
> Typing a 19-character filter made computer-use paste rather than type, and the UI **died**:
> ```
> System.Runtime.InteropServices.COMException (0x8007000E): EnumFormatEtc failed
>    at Avalonia.Win32.ClipboardImpl.TryGetDataAsync()
>    at Avalonia.Controls.TextBox.Paste()
>    at System.Threading.Tasks.Task.<>c.<ThrowAsync>b__124_0(Object state)
> ```
> A failed clipboard READ inside `TextBox.Paste()` surfaces on the dispatcher as an unobserved async
> exception and terminates the process — **Ctrl+V into any textbox is a potential crash**, and the
> user loses a loaded session. Worth a dispatcher-level guard (`Dispatcher.UnhandledException`) that
> logs and swallows input-layer faults. Effort **S** · Risk **low**.
> ⚠ Second, smaller defect in the same evidence: `crash.log` labels it **"UE5DumpUI startup crash"**
> though it happened long after startup — the handler hard-codes that phrase.
> ➜ **BOTH HALVES FIXED 2026-08-18** (dispatcher input-fault guard + honest crash.log phase/uptime).
> The live check that is still owed is the batch tagged `[PASTECRASH-2026-08-18]` in
> `## Pending live-game verification` above — grep the tag.
>
> ### ⚠ MY OWN ERROR, recorded because it is the same shape as the trap this row keeps hitting
> I first read the class as **`ES2GameInstance`** off a 0.6-scale screenshot (the package is `ES2`,
> the class is `ESGameInstance`) and membership-tested *that*. It came back "not in the capped page"
> — **because it does not exist at all** — and I reported ES2 as qualifying. A nonexistent name is
> absent from every list, which is indistinguishable from "past the cap". **Always confirm the class
> EXISTS before concluding it is past the cap**; `find_instances` answers it in one call, and the
> corrected table above pairs `exists` with `inside cap` for exactly this reason.

### ✅ CLOSED 2026-08-17 `[W23-PIPE-2026-08-17]` + `[SDKHDR-UI-2026-08-17]` — SDK header layout: inherited-property boundary + packed bitfields (audit #5 W2/W3, build 2842)

**Both halves now pass — the headless boundary value AND the emitted header.** DumperTest
Development, build **1.0.0.3262**, headless via `tools/verify/pipe_client.py` and then through the
real UI. ⛔ **The export also surfaced a separate, unrelated defect that makes the header
uncompilable — see the block after the UI half. That one is still open.**

1. ✅ `walk_class` on `DumperTestActor` (`//Script/DumperTest/DumperTestActor`) reports
   `super_props_size: 672` against `props_size: 1760` — non-zero and smaller, as required.
2. ✅ **The real check.** Walking `super_addr` (`0x1DB6FDEAE00` = `//Script/Engine/Actor`) directly
   gives `props_size: 672` — *exactly* the child's `super_props_size`. This is the equality that
   would catch the offset being read off the wrong struct, and it holds. Corroborated independently
   by the layout itself: `DumperTestActor`'s own first field, `Text_Even2_OneNull`, sits at offset
   **672** — the derived data starts precisely at the boundary.
3. ✅ **Not an absence-shaped result** (§1.2). The lowest-offset field in `fields` is
   `PrimaryActorTick` at **40**, far below the 672 boundary, so the reply genuinely does carry
   inherited properties and the filter has something to do.

*Also visible in the same reply, though it is the emitter that W3 is about:* `AActor`'s replication
block is present in the packed form the header generator has to handle — `bNetTemporary`/
`bOnlyRelevantToOwner`/`bAlwaysRelevant`/… all at **offset 88** with `bool_mask` 1/4/8/16/…, and the
sample's own `bFlagA`/`bFlagB`/`bFlagC` at **1648** with masks 1/2/4 plus `bPlainBool` at 1649.

### 🟡 U2 only — container geometry on a real game (build 2830); MG2 is CLOSED

> **MG2 closed in full 2026-08-23** — step 1 + the TSet halves `[MG2-CONTAINER-2026-08-23]`, and the
> UDataTable half once `[DTROWMAP-2026-08-23]` / `[DTTEXT-2026-08-23]` were fixed. **Only U2 is left**,
> and it is D_ENVIRONMENT: `WITH_CASE_PRESERVING_NAME` needs a title that ships with it, and DumperTest
> cannot fake it — the flag is an `#ifndef` in `NameTypes.h:30`, overridable in principle, but this
> machine's UE 5.4 is an INSTALLED BINARY engine (`Engine/Build/InstalledBuild.txt` present) so a game
> module compiled with a 16-byte FName would ABI-mismatch the precompiled Core.

*Closed here already: **MG1 · MG3 · A2 · U1 · V1** (two sittings below — the DLL half 2026-08-14, the
UI half 2026-08-18). **Still open: three.** **MG2**'s rows-equal-count half (the count half passed;
the rows half is undecidable while the drill-down caps — `[CONTAINERCAP-2026-08-18]`), the
**`TSet<FName>` / `TSet<UObject*>` / `UDataTable` no-regression** check (DumperTest ships none of the
three, so it needs a real game), and **U2** (needs a `CasePreservingName: YES` title — twelve
confirmed non-CPN, zero CPN, so the absence is itself the signal and this stays LOW).*

> ### ⬜ 2026-08-14 — the UI half (build 2830) is NOT yet verified in-game
>
> The ✅ table below is **correct and narrower than it looks**: it verifies that the *DLL* reads map
> elements at the right stride. Audit #5 segment **U1/V2** then found the same formula in **three C#
> copies that `5ef4c2b` did not update**, so the key→value *text* in the grid was right (the DLL read
> it) while every map element **address the UI computed itself** was short by 4+ bytes past index 0.
> Alongside it, **U1/V1** (the audit's only surviving HIGH) had map rows publishing the element base —
> the **key** — as the address the inline editor writes to.
>
> **Fixed in build 2830**: the DLL now publishes `map_stride` / `set_stride`, `Core/ContainerGeometry.cs`
> is the single client-side consumer, all three mirrors are deleted, and a map row's type/address/size
> all describe the value. Unit-verified with a negative control (reverting the fix turns 5 tests red,
> including the seam test).
>
> **What still needs a live process** — the DLL half already has witnesses below; this is the **UI**
> half, which no headless pipe check can see because it is client-side arithmetic:
>
> | Check | On a `TMap<AActor*,float>`-shaped field (8-aligned key, 4-byte value) | Expect |
> |---|---|---|
> | Address column | drill into the map, read element **[1]**'s Address | `MapDataAddr + 24 + 8`, **not** `+20`, and **not** the element base |
> | Inline edit | edit element [1]'s value, then Refresh | the **value** changes; the key text is unchanged |
> | CE record | "+CE" on element [1] | CE shows the value, and freezing it does not corrupt the key |
>
> `DumperTest`'s `Map_I64ToI32` / `Map_StrToInt` already exercise the DLL side; the UI check wants a
> map whose pair alignment is 8 and whose pair size is NOT already a multiple of 8, since that is the
> only shape where the old and new strides differ.

> ### ✅ FIVE OF SIX VERIFIED IN-GAME 2026-08-14 — DumperTest, UE 5.4 Development package
>
> Driven **entirely headlessly**: launch the packaged sample → `scripts/inject-ue.ps1 -ProcessId` →
> a ~10-line PowerShell `NamedPipeClientStream` issuing `find_instances` + `walk_instance`. No UI.
> This is repeatable in one command; the witnesses were added to the sample the same day
> (commit `58ddf76`) precisely because none of the pre-existing containers could discriminate.
>
> | Fix | Verdict | Evidence from the live walk |
> |---|---|---|
> | **MG1** | ✅ | `Map_I64ToI32` all three elements correct (`600000000001..3` → `6001..3`). A stride of 20 makes elements 1–2 read from the previous element's tail; they are exact, so the stride is 24. |
> | **MG1** (2nd witness) | ✅ | `Map_StrToInt` `map_value_offset=16`, values `6101/6102/6103`. Different arithmetic from MG1's first witness, so one wrong assumption cannot satisfy both. |
> | **MG3** | ✅ | `Map_IntToVec3f` reports **`map_value_offset: 4`**. The old size guess yields **8**. This is `Ubel::GetStructAlignment` reading `MinAlignment=4` off a live `UScriptStruct`. Raw hex `00C8C145 00D0C145 00D8C145` decodes to 6201.0/6202.0/6203.0 — all three floats at the right offsets. |
> | **MG2** | ✅ | `Set_Big` `set_count=199` (200 added, 1 removed). Before the fix `NumFreeIndices` always read 0, so this reported **200**. |
> | **A2** | ✅ | `Set_Big` returns 199 elements with **9005 absent** and 9000 / 9004 / 9006 / 9199 all present. 9005 is index 5, i.e. its bit lives in the inline words the `TBitArray` froze when it spilled at 128 — the defect would still list it. |
> | **U2** | ⬜ | **No known vehicle.** See the box below — TQ2 is NOT CasePreservingName on the current build. |
>
> ### ⚠ 2026-08-14 — TQ2 is NOT CasePreservingName, contradicting `test-games.md`
>
> Injected into `TQ2-Win64-Shipping.exe` (PID 53412, Steam, save loaded) to verify U2.
> `get_offsets` returned **`case_preserving=false`**, and the DLL's own detection log is unambiguous:
>
> ```
> [DYNO] DetectCasePreservingName: votes standard=20, CPN=0 (tested 20 objects)
> [DYNO]   CasePreservingName: no
> [SUMMARY] DynOff: CPN=no FProp=yes TagFFV=yes Outer=+0x20 validated=yes
> [SCAN] FindAll: UE Version = 507 (tier=1, detected=yes, lowConfidence=no)
> [OARR] FUObjectItem size=24, object-ptr offset=+0x08 (UE5.7+ reordered item) — 200 named, 200 total, 0 bad
> ```
>
> A **20–0 sweep** is not a marginal or failed detection, and everything around it resolved cleanly
> (correct UE 5.7, correct reordered `FUObjectItem`, 200/200 named). So this is not the detector
> failing — this build genuinely has `WITH_CASE_PRESERVING_NAME` off.
>
> **`docs/test-games.md:13` says the opposite** ("CasePreservingName + DynOff. Stride 16."). One of
> two things is true and they need different responses: the game was **patched** since that row was
> written (then the row needs a date and a re-test note), or the row was **wrong from the start**
> (then every conclusion drawn from "TQ2 is our CPN title" needs re-checking — including the claim,
> made earlier today in commits `58ddf76` and `b281ca1`, that TQ2 is U2's verification vehicle).
> **Do not treat that row as evidence until this is settled.**
>
> **Solarpunk (UE5.7) — 2026-08-14: measurably NOT CasePreservingName.** A first sample 60 s after
> injection returned `case_preserving=false` with `probe_ran=false` — i.e. nothing, the probe had not
> run yet. **Re-queried later on the same still-running process: `case_preserving=false,
> validated=true, probe_ran=true`** — a real measurement. Solarpunk joins TQ2 as a confirmed non-CPN
> title. (The log is misleading here and that is filed as audit G7: its only `DynOff:` summary still
> says `validated=NO (DEFAULTS)` because it is never re-emitted after the later validation.)
>
> **Method note worth keeping:** the first sample was *real* but was not a *verdict*. `probe_ran` is
> the field that separates the two, and reading `case_preserving` without it produces a confident
> wrong answer in either direction.
>
> **U2 therefore has no known verification vehicle right now.** Three candidates are exhausted: TQ2
> is measurably not CPN, Solarpunk is indeterminate, and DumperTest cannot be (engine flag). Options:
> sweep other titles (`case_preserving` is one `get_offsets` call each, so this is cheap), or build UE
> from source with the flag on and repackage DumperTest. Until then U2 stands on the unit tests and
> code review only.
>
> **Sweep, title 3 of N — DQ7R is measurably NOT CPN `[DQ7R-PIPE-2026-08-17]`.** `get_offsets` on the
> live process returned **`case_preserving=false, probe_ran=true, validated=true`** — a verdict, not a
> sample, because `probe_ran` is set (the method note above). Population of confirmed non-CPN titles
> is now **TQ2 · Solarpunk · DQ7R**; still zero CPN titles found. Per the register's own priority
> rule, that growing absence is itself the signal and keeps U2 LOW.
>
> ### ⬜ Sweep completed to 9 titles 2026-08-17 `[SWEEP-2026-08-17]` — still ZERO CPN
>
> Six more titles, each a launch + one `get_offsets`, every one with `probe_ran=true` so every one is
> a verdict rather than a sample:
>
> | title | UE | detected via | objects | `case_preserving` | `validated` | GObj / GNames / GWorld |
> |---|---|---|---|---|---|---|
> | DQ7R | 427 | **memory Tier 1** | 149,497 | false | true | `ES53_1` / `V8` / `TQ_1` |
> | DQ I&II HD-2D | 427 | **memory Tier 1** | 104,867 | false | true | `ES53_1` / `V8` / `TQ_1` |
> | EVERSPACE™ | 420 | PE | 191,363 | false | true | **`G42_4` / `CT3`** / `TQ_1` |
> | The Artisan of Glimmith | 427 | PE | 24,132 | false | true | `ES53_1` / `V8` / `TQ_1` |
> | Lushfoil Photography Sim | 506 | PE | 58,617 | false | true | `ES53_1` / `V8` / `TQ_1` |
> | Manor Lords | 505 | PE | 80,013 | false | true | `ES53_1` / `V8` / **`SP57_1`** |
> | SEED BATTLE DESTINY REMASTERED | 427 | PE | 26,113 | false | true | `ES53_1` / `V8` / `TQ_1` |
> | DragonSword Awakening *(injected)* | 504 | **cached, `rev=5`** | 72,604 | false | true | `ES53_1` / `V8` / `TQ_1` |
> | Star Trek Voyager *(injected)* | 506 | PE | 46,994 | false | true | **`V13`** / `V8` / `TQ_1` |
> | Light Maze *(injected)* | 500 | PE | 14,958 | false | true | **`V13`** / `V8` / `TQ_1` |
>
> The last three carry **no proxy**, so they were injected through the panel's own *Inject into
> running game…* — which also exercises that button on three unrelated titles.
>
> **U2: TWELVE confirmed non-CPN titles, zero CPN** (TQ2 · Solarpunk · DQ7R · DQ I&II · EVERSPACE ·
> Geri · Lushfoil · Manor Lords · SEED · DSA · STVoyager · Light Maze), spanning **UE 4.20 · 4.27 ·
> 5.0 · 5.4 · 5.5 · 5.6 · 5.7**. The population is no longer thin enough to call unrepresentative,
> and the register's own rule — *no environment to test on ⇒ LOW* — now rests on a real sample rather
> than an assumption.
>
> **G8/G9 step 2 corroborated again, on DSA:** `UE Version = 504 (cached, rev=5, detected=yes,
> lowConf=no) — skipped DetectVersion`. The rev stamp is written back and honoured on a second title.
>
> **G1/X3 gains ten more clean hosts and still no amber one.** Every title returned
> `validated=true` with **no `unmeasured` key at all**. Twelve titles, twelve negative controls: the
> partial-offset-failure branch is looking genuinely rare rather than merely unvisited, and screening
> for it stays a single `get_offsets` per title.
>
> ⭐ **A control for the `lowConfidence` rule fell out for free.** Five of these are `tier=1,
> lowConfidence=**no**` with `DetectPublisher: no thumbprint match`, against DQ7R / DQ I&II at
> `tier=1, lowConfidence=**yes**` under `SQUARE_ENIX`. Same tier, opposite flag, publisher the only
> difference — which is exactly what the source says drives it, now observed rather than only read.
>
> **G12 discovery value — four distinct pattern families across twelve titles:** EVERSPACE (UE 4.20) is the only title using `GOBJ_G42_4` + `GNAM_CT3`, and
> Manor Lords the only one on `GWLD_SP57_1`, and STVoyager + Light Maze the only two on `GOBJ_V13`.
> **All twelve resolved all three pointers; no failures.**
>
> ⛔ **OCTOPATH TRAVELER could not be swept at all** — its `version.dll` proxy never loads. ✅ **RESOLVED
> 2026-08-18: use the `winmm.dll` proxy** (`[OCTOPATH-G2T3-2026-08-18]`) — 180/180 exports forwarded,
> full clean scan, 273,956 objects. See the
> `[PROXYLOAD-2026-08-17]` finding; it needs a different flavour or direct injection.
>
> **Incidental — D1/U3 CONFIRMED LIVE as still broken (not yet fixed).** `Map_IntToVec3f` renders as
> `f:[6203.0000]`: one float, the **last** one. The raw hex holds all three correct values, so the
> loss is in `InterpretValue`'s 8-byte "vtable preamble" skip — 12-byte struct − 8 = one float. U3
> moves from inferred to observed.

The remaining unchecked boxes below are superseded by the table above except where noted; U2 and the
`TSet`/`UDataTable` no-regression check still stand.

> ### ✅ THE UI HALF RAN 2026-08-18 — DumperTest Development, dist 3262, CE attached
>
> Every address below was checked against an **independent read of the process's own bytes**
> (`tools/verify/read_mem.py`), never against another number the UI computed — the map's data pointer
> comes from the `TMap` property's first 8 bytes, so the expected value cannot be derived from the
> observed one (working-lessons §1.10a).
>
> | row | verdict | evidence |
> |---|---|---|
> | **MG1** | ✅ | `Map_I64ToI32` property @`0x1F144477D88` → data ptr **`0x1F16D348980`** (ArrayNum 3, ArrayMax 4). UI element[1] Address = **`0x1F16D3489A0`** = ptr + 24 + 8. The old stride-20 arithmetic gives `0x1F16D34899C`, which is *not* what is shown. Element offsets 0x0/0x18/0x30 = stride 24. |
> | **MG3** | ✅ | `Map_IntToVec3f` property @`0x1F144477E28` → data ptr **`0x1F172A9E3E0`**. UI element[0] Address = **`0x1F172A9E3E4` = ptr + 4**, not +8. The 12 bytes there decode as **(6201.0, 6202.0, 6203.0)** — matching the row's `{X=6201, Y=6202, Z=…}`. This is the `MinAlignment` read. |
> | **U1 / V1** | ✅ | The two consumers of the element address both aim at the **value**. In-place edit of element[1] → status `Written: [1] 600000000002 = 7777`; memory at the element base then reads `02 70 C9 B2 8B 00 00 00 | 61 1E 00 00` — key **600000000002 intact**, value **7777** written at +8. `+CE` pushed `1F16D3489A0 / 4 Bytes / 7777` — the value address and the value's width, not the int64 key. |
> | **V1** (freeze control) | ✅ | Ticking that CE record and changing it to **1234** wrote `D2 04 00 00` at +8 while the key stayed `02 70 C9 B2 8B 00 00 00` across repeated freeze writes. The pre-fix bug wrote the user's 4 bytes over the key. |
> | **MG2** | 🟡 PARTIAL | `Set_Big` sparse array: **ArrayNum = 200**, UI header **`{Set: 199}`** — so `NumFreeIndices` is being subtracted and the count is no longer inflated. ⚠ The *rows-equal-count* half is **not decidable here** — see the cap finding below. |
> | **A2** | ✅ | This is the real A2 predicate and `Set_Big` satisfies it exactly. 200 slots > the 128 inline `TBitArray` bits, so the allocation **has spilled to the heap**; the freed slot is at **index 5**, i.e. inside the window the stale inline words used to cover. The walker shows `[4] 9004 → [6] 9006` — **[5] is absent**, and memory confirms slot 5 holds `FF FF FF FF FF FF FF FF 2D 00 00 00`, the sparse-array free-list links, not an element. Pre-fix this read as allocated and rendered a dead element. |
> | **TSet / UDataTable no-regression** | ⬜ **NOT TESTED — do not record as passing** | `TSet<int32>` (`Set_Int`, `Set_Big`) and `TSet<FStruct>` (`Set_Struct`) resolve, but the row asks for **`TSet<FName>` / `TSet<UObject*>` and a `UDataTable`**, and DumperTest ships **none of the three** (`Set_` filter returns exactly 3 matches, all covered above). Needs a real game. |
> | **U2** | ⬜ still open | needs a `CasePreservingName: YES` title; unchanged by this sitting. |

### ✅ CONTAINERCAP steps 1-3 CLOSED 2026-08-24 `[CONTAINERCAP-LIVE-2026-08-24]` — all three disclosures, on the AOT build

`dist/UE5DumpUI.exe` **v1.0.0.3338, the 54.7 MiB Native-AOT binary** (sha256 `1d510af3…`), DumperTest
Shipping (UE504, 24,478 objects), DLL 3338. Live Walker → `DumperTestActor0` → `Set_Big`
(`SetProperty {Set: 199, IntProperty}` @ `0x558`).

| step | what was done | result |
|---|---|---|
| **1** | drill into `Set_Big` at the default cap | breadcrumb `SetBig {Set: 199, IntProperty} ⚠ showing 128 of 199`; header `Set<IntProperty> Set_Big ⚠ showing 128 of 199`; status `Showing the first 128 of 199 entries — raise the "Array Limit" slider in the toolbar and re-open this container to read more.` ✅ all three |
| **2** | `Set_Int` (3 entries) as the clean control | breadcrumb `SetInt {Set: 3, IntProperty}`, header `Set<IntProperty> Set_Int`, **no status line at all** — every disclosure empty on the non-truncated case ✅ |
| **3a** | Array Limit **128 → 64**, re-open | `⚠ showing 64 of 199` in breadcrumb, header and status — **the count tracks the slider** ✅ |
| **3b** | Array Limit **64 → 256** (≥ 199), re-open | **badge absent everywhere**, no status line, and the grid runs to `[199] = 9199` ✅ |

⭐ **Incidental confirmation of the A2/M2 fixture:** the element list goes `[4] → [6]` — index **5**
(value 9005) is the low index the sample removes after the `TBitArray` spills past its 128-bit inline
buffer. Its absence is the walker reading the **heap** copy rather than the inline words frozen at
spill time, which is the thing `Set_Big` was built to catch.

⚠ **The step-3 wording is only half-reachable on this fixture.** *"The shown count rises (e.g.
`showing 256 of N`)"* cannot be seen with N=199, because the slider is exponential — 128 → 256 jumps
straight past it. The substance of that clause (the shown count follows the cap) was measured in the
**other** direction instead, 128 → 64, which is the same claim and is reachable.

#### ⚠ NEW FINDING `[CAPREFRESH-2026-08-24]` (LOW) — **Refresh** updates only ONE of the three truncation disclosures

Found while running step 3, by pressing **Refresh** instead of re-opening. Both directions measured
on the same container:

| sequence | breadcrumb | header | status line |
|---|---|---|---|
| navigate at 64, raise to 256, **Refresh** | **`⚠ showing 64 of 199`** (stale) | *(cleared)* | *(absent)* |
| navigate at 256, lower to 64, **Refresh** | **no badge** (stale) | `⚠ showing 64 of 199` | **absent** |

⭐ **The second row is the one that matters: the breadcrumb silently says the container is complete
while 135 of 199 entries are missing.** That is the exact defect `[CONTAINERCAP-2026-08-18]` was
written to remove — *"nothing distinguished a complete 128-entry set from the first 128 of 199"* —
reintroduced through the Refresh path.

**Mechanism, and it is systematic rather than a one-off** (`LiveWalkerViewModel.cs`, every
`ContainerTruncation` call site mapped to its method):

```
NavigateToArrayContainerAsync   BadgeSuffix(crumb) + StatusLine    <- runs ONCE, at navigation
NavigateToMapContainer          BadgeSuffix(crumb) + StatusLine
NavigateToSetContainer          BadgeSuffix(crumb) + StatusLine
NavigateToDataTableContainer    BadgeSuffix(crumb) + StatusLine
PopulateArrayContainerFields    BadgeSuffix(header) only           <- runs on navigation AND Refresh
PopulateMapContainerFields      BadgeSuffix(header) only
PopulateSetContainerFields      BadgeSuffix(header) only
PopulateDataTableRowFields      BadgeSuffix(header) only
```

The crumb label is **baked into the `BreadcrumbItem` at push time** and the status line is written
only from the `Navigate*` path, so a re-populate cannot move either. **No `Populate*` method calls
`StatusLine` at all.** Array, Map, Set and DataTable all share the shape.

**Fix shape** (not applied — this session verifies): move the badge + status computation into the
`Populate*` methods, or have Refresh update the *current* crumb's `Label` from the same helper the
header uses. The pure helper already exists (`ContainerTruncation`), so both disclosures can be
derived from one call rather than computed on two paths — which is audit #4's root cause verbatim,
*the report and the reality are computed by different code paths*.

**Severity LOW**: the header — the most prominent of the three — is always correct, and the user has
to change the Array Limit *and* press Refresh rather than re-open to reach it. It is filed because
the row's own acceptance treats all three disclosures as load-bearing ("Breadcrumb **AND** header …
**and** a status line"), and because the under-reporting direction is the one the original defect was
about.
- ⬜ **`TArray<FName>` / `TMap<FName,V>` on a CasePreservingName game (U2).** Needs a UE 5.5+/5.7
  title where `Genau` logs `CasePreservingName: YES` (e.g. Titan Quest II). Expand any actor's `Tags`.
  Before the fix `InferScalarSize` forced the stride to 8 against the engine's real 16, so every
  element but the first was read from the middle of its predecessor.
- 🟡 **PARTIAL `[DUMPERTEST-LOG-2026-08-17]` — A `TMap`/`TSet` whose ELEMSIZE reads garbage no longer
  wedges the walk (U1).** The **passive half PASSES**: every `KeySz=`/`ValSz=` in the DumperTest
  `walk-0.log` is plausible (8/4, 4/4, 8/4, 16/4, 4/12) — nothing like `1073742336`.
  ⚠ **The degraded branch itself is NOT TESTED and must not be recorded as passing.**

> ### ⛔ U1's PRESCRIBED PROBE CANNOT WORK 2026-08-23 `[U1-PROBE-2026-08-23]` — measured, and the reason is three layers of defence
>
> The row (and the A* classification) says: *"poke a bogus `ElementSize` into a live
> `FMapProperty`"* → expect `Cannot read map elements for '…'`. **Run on DumperTest / DLL 3337
> with `tools/verify/u1_map_elemsize.py`: the poke lands and nothing happens.**
>
> ```
> Map_NameToInt FMapProperty @ 0x2007F24C9A0
> pointer pairs whose ElementSizes are (8, 4): 1   ->  +0x70 KeyProp / ValueProp   (witnessed)
> before:  map_count=3 elements=3
> poke read-back: 0x40000200 (want 0x40000200)     <-- the write DID land
> with a bogus ElementSize: map_count=3 elements=3, oracle_warn=False
> after restore: map_count=3 elements=3
> ```
>
> ⭐ **The read-back is what makes this a finding instead of a defect report.** "The walker still
> worked" has two very different causes — it ignored the garbage, or **the write never landed** —
> and only a read-back separates them. Without it this rig would have filed a false defect.
>
> **Why it cannot work**, from `Ubel.cpp`'s `ResolveInnerSize` — **three** layers, in order:
> 1. `InferScalarSize(innerTn)` is **type-driven and first**. `IntProperty → 4`,
>    `NameProperty → 8` (`0x10` case-preserving), `ObjectProperty → 8`, … For a scalar value the
>    function **returns before `FPROPERTY_ELEMSIZE` is ever read**, so the poke is unobservable
>    *by design*.
> 2. Only if that yields 0 is the raw `ElementSize` read — and it is then passed through
>    **`ValidateArrayElemSize`**, so obvious garbage (`0x40000200`) is rejected rather than used.
> 3. If validation also fails and the type is `StructProperty`, the size is recovered from the
>    `UScriptStruct`'s `PropertiesSize`.
>
> ⇒ **A garbage ElementSize is defended three ways.** The degraded branch is guarded by
> `else if (fv.mapCount > 0 || sa.Data != 0)` ([Ubel.cpp:4573](dll/src/Ubel.cpp:4573)) and is
> reached only when the pair layout genuinely cannot be resolved.
>
> **What a future attempt must change** (do not repeat the above):
> * use a **StructProperty-valued** map — `Map_IntToVec3f` on `DumperTestActor` — so layer 1
>   returns 0 and the ElementSize is actually consulted;
> * poke a **plausible-but-wrong** size (e.g. 16 where 12 is right), not obvious garbage, so it
>   survives `ValidateArrayElemSize`;
> * and expect layer 3 to still recover the right size from the `UScriptStruct` — so reaching the
>   branch may require breaking the struct pointer too. **It is entirely possible this branch is
>   unreachable by data alone and needs a staged build**, which would move U1 out of "cheapest"
>   and into the staged tier.
>
> ℹ️ Reusable from this run regardless: the rig **witnesses** the ValueProp instead of assuming an
> offset. `get_offsets` does not publish `FSTRUCTPROP_STRUCT`, so it scans the FMapProperty for a
> pointer pair whose ElementSizes match what the field's *name* says they must be (FName=8,
> int32=4) and **refuses to run unless exactly one pair matches**. It found exactly one, at
> `+0x70`. Also: `fproperty_elemsize` is **52** at runtime here, not `Grimoire.h`'s `0x3C`
> default — read it from `get_offsets`. All five maps
  read cleanly (`Read 3/3 map entries … skipped 0 unallocated` on each), so
  `Cannot read map elements for '%s'` (`Ubel.cpp`, `Sein::Warn`) structurally cannot fire here — and
  this file already says the case is hard to force deliberately. The "no multi-second freeze" half is
  a UI-perceived claim and is likewise unmeasured from a log.
- ✅ **DONE `[DUMPERTEST-LOG-2026-08-17]` — `walk-0.log` `Stride=` values are correct.** Grepped
  `WALK:MapP` in `Logs\DumperTest\walk-0.log`; all five maps present with `ValOff=` and `Stride=`:

  | field | KeySz/ValSz | ValOff | Stride | the defect would have shown |
  |---|---|---|---|---|
  | `Map_I64ToI32` | 8 / 4 | 8 | **24** | 20 — the core MG1 witness |
  | `Map_StrToInt` | 16 / 4 | 16 | **32** | 28 — second witness, different arithmetic |
  | `Map_IntToVec3f` | 4 / 12 | **4** | 24 | value at +8; the only one wrong at element 0 (MG3) |
  | `Map_NameToInt` | 8 / 4 | 8 | 20 | unchanged by design |
  | `Map_IntToFloat` | 4 / 4 | 4 | 16 | unchanged by design |

  The two witnesses disagree in their arithmetic, so one wrong assumption cannot satisfy both, and
  `Map_IntToVec3f` exercises the `UScriptStruct::MinAlignment` read specifically. Log is from
  build `5ef4c2b` (1.0.0.2812) — the DLL-side commit for this fix, so it is in scope.
  **This is the log half only**; the UI-side arithmetic remains open in the rows above.

### 🔴 NEW 2026-08-11 — `executeCodeEx` finite timeout + reason capture (build 2792)

Shipped in [dev-log.md](dev-log.md) build 2792, **never run against a game**. Three call sites
changed: `scripts/ue5_dissect.lua`'s `callDLL` (was an INFINITE timeout, now 5000 ms),
`CeLuaHygiene.AppendCallDllHelper`, and `UE5CEDumper.CT`'s `ue5_callDLL`. CE-side model:
[ce-plugin-sdk-notes.md](ce-plugin-sdk-notes.md) §13.

**Free from any ordinary session** (no special setup — just use the tool once):

- ✅ **DONE 2026-08-18 — Dissect still builds a structure**, DumperTest Development, dist 3262.
  `dissect.createFromPath('/Script/DumperTest.DumperTestActor')` returned a CE structure named
  **`DumperTestActor` with 193 elements**.
  ⚠ The row warns that "a structure window appeared" is not a pass, so the check was **per field**:
  seven elements were looked up **by offset** and compared with what Live Walker independently
  reports, across five different type shapes — **matched 7, missed 0**.

  | offset | element | CE `Vartype` / `Bytesize` |
  |---|---|---|
  | `0x5E` | `UpdateOverlapsMethodDuringLevelStreaming` | 0 / 1 |
  | `0x17C` | `PhysicsReplicationMode` | 0 / 1 |
  | `0x478` | `Map_I64ToI32` | 12 / 8 (pointer stub) |
  | `0x518` | `Map_IntToVec3f` | 12 / 8 |
  | `0x568` | `Set_Big` | 12 / 8 |
  | `0x608` | `Opt_Int_Set` | 2 / 4 |
  | `0x671` | `bPlainBool` | 0 / 1 |

  Since a class walk runs the changed `callDLL` **once per field**, 193 successful elements is 193
  successful `executeCodeEx` round trips through the 5000 ms path.
  **This also closes AA7 step 1** (`createFromClass` succeeds and the structure appears in CE's list).
- ✅ **DONE 2026-08-18 — No stray warnings on a healthy run.** **Zero** `[UE5Dissect WARN]` lines in
  CE's Lua console across the whole dissect (and across a second run of the same class for the
  by-offset comparison). `warn()` is ungated, so this is a real absence, not a suppressed one.
- ✅ **DONE 2026-08-18 — `.CT` disable still tears down.** Evidence and timings are in the
  `Fern::Stop` block above: `init-0.log` shows `UE5_Shutdown: Cleaning up...` →
  `[Grausam] Foreground lock DISABLED` → `[SENSE] Diagnostics counters reset`, and CE's console
  printed `[UE5Dump] UE5 Dumper stopped.` So `ue5_callDLL` really reached `UE5_Shutdown` — not the
  audit #4 B1 shape where a clean teardown is reported and never happens.

**Needs deliberate action:**

- ✅ **DONE 2026-08-18 — 5000 ms is comfortably enough.** Dissected `/Script/Engine.Actor` on
  **Elliot** (85,068 objects, dist 3262, `dxgi` proxy) from CE's Lua Engine: **844 ms** end to end
  for `dofile` + `createFromPath`, producing `name=Actor elements=129`, with **no** `Execution
  timeout` and **no** `[UE5Dissect WARN]` line. That whole figure *includes* the `UE5_FindObject`
  GObjects scan this row names as the slow candidate, so the budget has ~6× headroom here.
  ⚠ Conditions: Elliot at its **main menu**, 85 K objects — not the 250 K+ the row asks for.
  DragonSword would still be a stronger sample, so treat this as "no sign of strain at 85 K"
  rather than a bound proven at 250 K.
- ✅ **The 5000 ms budget at ~250K objects — CLOSED 2026-08-24** `[EXECCODEEX-BUDGET-2026-08-24]`.
  **OCTOPATH TRAVELER**, launched through its deployed **winmm** proxy, **273,956 objects**, UE **418**,
  `GObjects=0x7FF62FA35C10 GNames=0x21676B30010`, DLL build **3343**, CE **7.7**.

  ```
  /Script/Engine.Actor                          1563 ms   115 elements    (first call: carries one-time init)
  /Script/Engine.SpringArmComponent              328 ms    83 elements   3.95 ms/element
  /Script/Engine.GameStateBase                   468 ms   124 elements   3.77 ms/element
  /Script/Engine.AudioComponent                  688 ms   170 elements   4.05 ms/element
  /Script/Engine.PlayerController                687 ms   185 elements   3.71 ms/element
  /Script/Engine.SkeletalMeshComponent          1454 ms   379 elements   3.84 ms/element
  /Script/Engine.CharacterMovementComponent     1765 ms   379 elements   4.66 ms/element   <- heaviest
  ```
  Every structure built, `ok=true`, **zero `[UE5Dissect WARN]`**, no `Execution timeout`. The heaviest
  class reaches **35 % of the 5000 ms budget**.

  ⚠ **THE ROW'S OWN CAVEAT WAS RIGHT, SO THE CHEAPNESS IS RECORDED RATHER THAN HIDDEN.** A big pool
  does *not* stress this path: the dissect costs **one `callDLL` per FIELD**, and OCTOPATH's 273,956
  objects sit behind only **705 classes** (measured; the earlier note said 699). So the *object count*
  is nearly irrelevant and the *element count* is the whole story. That is why seven classes were
  measured instead of one — a single `Actor` run would have "proven" a bound on 115 elements and read
  as a bound at 250 K objects.

  ⭐ **What the numbers actually license.** Steady state is a tight **3.7–4.7 ms/element** across six
  classes, so the budget is reached at roughly **1,070–1,350 elements** — about **3× the largest class
  observed here**. The claim this row can support is therefore *"no single `executeCodeEx` call comes
  near 5000 ms at this element scale"*, **not** *"5000 ms is sufficient for any UE class"*. A title
  with a >1,000-property Blueprint class would still be worth a look.

  ⚠ **`Actor`'s first run (13.6 ms/element) is an outlier and must not be averaged in** — it carries
  the one-time `dofile`, first `callDLL`, and offset detection. A *second* `Actor` call returned in
  **0 ms**, i.e. the structure was reused, so repeat timings on the same path are **not** independent
  samples. Measure a fresh path each time.

  ⚠ **The 379/379 coincidence was checked, not waved through.** `SkeletalMeshComponent` and
  `CharacterMovementComponent` both reported exactly 379 elements, which looks like structure reuse.
  Re-running three fresh classes with the struct **name** printed showed each path produces its own
  correctly-named structure (`struct=GameStateBase`, `struct=SpringArmComponent`,
  `struct=AudioComponent`), so the mechanism is one-structure-per-path and the match is coincidence —
  both are large components inheriting the same `USceneComponent`/`UActorComponent` chain.

  ⛔⛔ **OPERATIONAL, CONFIRMED BY THE MAINTAINER 2026-08-24: on OCTOPATH our `dxgi.dll` STOPS THE GAME
  FROM RUNNING — it must be `winmm`.** This sharpens `[OCTOPATH-G2T3-2026-08-18]`'s "dxgi instant-exits
  under the early loader lock" from a startup quirk into a hard per-game rule. ⚠ **And it compounds
  `[PROXYREFRESHOWNER-2026-08-24]`**: that bug did not merely destroy OCTOPATH's ReShade `dxgi.dll`, it
  *deployed our dxgi proxy in its place* — so a launch in that state would not have started at all.
  Restored to ReShade before this run (5,255,448 B, sha `b2945c29e7095491`), and the run used winmm.
  ⚠ Note the ownership gate added to `proxy_refresh.py` does **not** protect against this second
  hazard: if our dxgi is ever deployed to OCTOPATH deliberately, refresh will keep it current forever,
  because it *is* ours. A per-game "never deploy dxgi here" rule would be the fix; not attempted.
- ⛔ **The prescribed negative control DOES NOT WORK — attempted 2026-08-18, do not re-run as
  written.** Suspending the game process does **not** make `executeCodeEx` time out; it succeeds
  normally, so anyone following this row would see "no timeout" and wrongly conclude the path is
  fine while never having exercised it.

  | attempt | suspension | result |
  |---|---|---|
  | 1 | short (resume raced the call) | `elapsed=984 ms  ok=true` — confounded, discarded |
  | 2 | **held ~18 s**, call issued ~3 s in | **`elapsed=1422 ms  ok=true`**, structure built |

  Attempt 2 is decisive: the call started and finished entirely inside a suspension that provably
  outlasted it by an order of magnitude.

  **Why:** CE's `executeCodeEx` runs the target function on a **newly created remote thread**.
  `NtSuspendProcess` suspends the threads that *already exist*; a thread created afterwards runs.
  And the dissect's calls (`UE5_FindObject`, the walk exports) are pure memory work that needs no
  game thread, so nothing blocks.

  **This is the same trap the run plan already flags for `AA14–AA20` step 5** — *"needs the game
  thread only suspended; CE's whole-process pause hits the status-0 branch, not the 0xFF branch
  under test"*. Same cause, second row.

  **A working induction must make the call need something that is actually stopped**: suspend the
  **game thread specifically** and invoke through `Stark`'s ProcessEvent dispatch, which cannot
  complete without it. Rewrite the row that way before spending another session on it.

  ⛔ **THAT REWRITE WAS RUN 2026-08-24 AND IT DOES NOT WORK EITHER — do not follow this paragraph.**
  Suspending the game thread does not produce a timeout; it **hangs Cheat Engine indefinitely**,
  because the game thread holds the target's **loader lock** and `executeCodeEx`'s
  `CreateRemoteThread` blocks on it — *before* the wait CE's timeout governs. The DLL log is empty
  for the call: the remote thread never entered the export. Full evidence and what a third form would
  need: `[EXECCODEEX-NEGCTL-2026-08-24]` below.

> ### ⛔ THE REWRITTEN NEGATIVE CONTROL ALSO DOES NOT WORK 2026-08-24 `[EXECCODEEX-NEGCTL-2026-08-24]` — a SECOND non-viable form, for a different reason
>
> The row's prescribed control was refuted in 2026-08-18 (suspend the **process** — cannot fail,
> because `executeCodeEx` runs on a **newly created** remote thread that the suspension never froze).
> The register's replacement was *"freeze the **game thread** via `suspend.py`, not the process"*.
> **That was run today and it does not work either.** It does not produce a timeout — it **hangs
> Cheat Engine indefinitely**, and it hangs it in a place CE's timeout structurally cannot reach.
>
> **The run was armed properly first** — `tools/verify/execcodeex_negctl.py arm`, and both documented
> silent-pass traps were closed and *observed*, not assumed:
>
> ```
> [1] hook_active=True   fire_count=136   responsive=True        <- trap 1 closed: the export will QUEUE,
>                                                                   not take the direct synchronous path
> [2] set_invoke_timeout(60000) -> True                          <- trap 2 closed: Stark's default is 5000,
>                                                                   IDENTICAL to CE's, so at defaults they race
> [3] hook fired 192 times in 1.5 s                              <- the hook is genuinely live
> [4] tid=44084 SUSPENDED (suspend.py's own "main thread" marker)
>     WITNESSES while stalled: responsive=False  fire_count frozen at 600  (pipe still answering)
> ```
>
> **What happened.** `ue5_callDLL('UE5_CallProcessEvent', 'int', 0x2759BD24980, 0x275AE40AD00, 0)` —
> the shipped helper, extracted **verbatim** from `scripts/UE5CEDumper.CT:485-512` so the run
> exercised the real text rather than a reconstruction. CE resolved the export
> (`export resolved=true addr=140728754596288`) and then **never returned**:
>
> | elapsed | observation |
> |---|---|
> | 20 s | no `elapsed=` line; CE's 5000 ms deadline already blown 4× |
> | 50 s | still nothing |
> | 80 s | still nothing — **past Stark's raised 60 s deadline too** |
> | — | `IsHungAppWindow` = **True** for both the Cheat Engine and Lua Engine windows |
> | after `resume-tid` | game thread recovers (`responsive=True`, `fire_count` 600 → 856) but **CE stays hung**, still `True` 30 s later |
>
> ⭐ **THE DECIDING EVIDENCE — the DLL log is EMPTY for that call.** The last
> `UE5_CallProcessEvent: dispatching to game thread` is at **12:17:32.655**, which is the rig's own
> warm invoke, and it *completed* (`invoke completed result=0`). Nothing at all around 12:20 when CE
> called. **So CE's remote thread never entered our export.**
>
> **Why that kills the method, and it is not a CE defect:** `executeCodeEx` allocates in the target
> and calls `CreateRemoteThread`. Creating a thread in a target requires that process's **loader
> lock** (for the `DLL_THREAD_ATTACH` notifications). Suspending the UE game thread while it holds
> the loader lock leaves the lock held, so `CreateRemoteThread` blocks **in the kernel** — *before*
> the `WaitForSingleObject` that CE's timeout parameter governs. A timeout on the wait cannot bound a
> block that happens at creation.
>
> ⚠ **So do NOT record this as "the 5000 ms timeout is broken".** The measurement supports a narrower
> claim: with the game thread suspended, the call never reaches the wait, so this staging cannot
> exercise the timeout **either way**. Whether the timeout bounds a call that genuinely starts is
> still unmeasured.
>
> #### ▶ WHAT A THIRD FORM WOULD NEED
>
> The requirement is a target function that **starts and then blocks**, without touching the loader
> lock. Suspension of any thread is out — it is the loader lock that bites, not the game thread
> specifically. Candidates, cheapest first:
>
> 1. **Keep the game thread RUNNING but saturated**, so `Stark::EnqueueInvoke` waits on a queue that
>    drains too slowly rather than on a frozen thread. The remote thread then starts normally, enters
>    the export, and CE's wait is genuinely the shortest deadline. Needs a way to flood the dispatch
>    queue — a DumperTest UFUNCTION that sleeps on the game thread would do it in one line, and is a
>    **fixture addition**, which is why this is filed rather than run.
> 2. **A deliberately slow export** reachable from `ue5_callDLL` — same shape, no game required, but
>    it is a test-only export in shipping code, which the repo has avoided elsewhere.
>
> ⚠ **And the row's own value should be re-examined before anyone builds that.** Build 2792 shipped
> **two** things: a finite timeout *and* reason capture. The reason-capture half is already pinned
> offline — `ue5_callDLL` reports CE's own `why` string (`UE5CEDumper.CT:497-507`), and
> `docs/ce-plugin-sdk-notes.md` §13 documents the six reasons. What remains unproven is only that a
> *started* call is bounded at 5000 ms, which is CE's behaviour rather than ours.
>
> ℹ️ **Operational note: CE had to be killed.** It stayed `IsHungAppWindow=True` after the resume and
> did not recover. No table was open and nothing was saved, so nothing was lost — but a session that
> tries this form should expect to lose its CE instance, and should not have an unsaved `.CT` in it.

### 🟡 PARTIAL 2026-08-10 — GObjects layout fix (build 2782), DragonSword Awakening

> ### ✅ Bullet 2 CLOSED 2026-08-24 `[DSLAYOUT-GREP-2026-08-24]` — the cross-title grep finds NO regression
>
> Headless, no game launch. `Could not detect layout, using default` across **all 31 log folders**
> on this machine: **exactly 2 files**, and both are the same 2026-08-22 DumperTest session
> (`offsets-20260822-120815.log`, `offsets-20260822-123340.log`).
>
> ⭐ **Those two are the guard WORKING, not the defect.** Both records read:
>
> ```
> ObjectArray: GObjects@0x2479D7D49C8: +00:002704000001271B …
> ObjectArray: Strict validation failed for all presets, trying relaxed fallback...
> ObjectArray: Could not detect layout, using default
> ObjectArray: chunkTable@0x2704000001271B: +00:0000000000000000 +08:0000000000000000
> ```
>
> The GObjects anchor's first qword **is** `0x2704000001271B`, which is not a valid pointer, and the
> chunk table it points at is all zeros. The anchor itself was wrong in that session, so refusing to
> pick a layout is the correct answer. ⚠ Not a pass by absence: the string appears in **0** of the
> other 29 folders and in **0** of DumperTest's own later sessions, so it is session-specific rather
> than never-emitted.
>
> ℹ️ Bullet 1 is separately closed and its checkbox text is simply stale: it says to match an
> address suffix `…F8B0`, which ASLR moves between runs.

**Verified in-game on build 2786, same day** — see [test-games.md](test-games.md) for the log lines
and numbers. Strict tier accepted `preset Default` with `Max=10551296` (impossible under the old
8.4 M cap, so the ceiling fix is directly proven); live `NumElements` **266 614 → 274 900** within
one session; the original repro (`DsClientLocalPlayer.<raw@0xAC0>` = 9338, Native-C) now returns.

**What is still unverified**, and why it is not just pedantry: that run resolved the `ObjObjects`
anchor (`GOBJ_V13` → `0x7FF62529F8C0`), where even the OLD relaxed `A/C` row reads the correct
`NumElements`. The second half of the fix — **relaxed row B stealing a row-E layout at the
`FUObjectArray` base anchor** — was therefore never exercised. The winning pattern/anchor is not
stable across runs on this title (build 2780 got the base via `GOBJ_ES53_1`), so a later session
can still land there.

- ⬜ On any future DragonSword session that logs GObjects at a base anchor (address ending `…F8B0`
  rather than `…F8C0`), confirm the preset line reads **`UE5-Extended`**, not `relaxed B`.
- ⬜ Regression watch across the other tested titles, since the relaxed tier gained a
  chunk-consistent first pass: confirm nothing that resolved before now logs
  `Could not detect layout, using default`. Relaxed pass 2 is byte-identical to the old
  behaviour, so this should be a formality.

### 🔴 NEW 2026-08-05 — two defects the DumperTest sample found on its first real use

**D3 — `FUObjectItem` stride detected as HALF its real size on a Development build.** Effort **M** ·
Risk med. **This is the one to fix next: it is upstream of D2 and of every result on this config.**

The tell is arithmetic, not a guess. `UE5_Init` prints its name-sanity probes:

```
Shipping      Sanity obj[0] … obj[1] … obj[2]      -> 10/10 resolved
Development   Sanity obj[0] … obj[2] … obj[4]      ->  5/10 resolved
```

**Only even indices resolve.** The Object Tree agrees to the decimal: *"12,588 named objects of
25,175 total, **50.0%**"*. Reading with a stride of 16 where the real one is 32 makes every second
entry garbage, and 50.0% is what that looks like.

The detector already said so and nothing acted on it:
`ObjectArray: FUObjectItem size tentatively set to 16 bytes, object-ptr offset +0x00 (**only 27
items validated**)`. On a healthy game that validation count is in the thousands. **"Tentative" plus
27 was the warning; the scan continued as though it were an answer.**

**It also explains D2's newest symptom.** With half the pool garbage, the Group Scan's
`Game classes only` / `Skip Engine/System noise` filter rejects nearly everything —
`0 matching objects in 17 ms (**scanned 0 objects**, 13 classes)` where the same scan on Shipping
walked 1731. So the group-scan investigation must **re-run on Shipping, or after D3 is fixed**;
measurements taken on this config are measuring the stride, not the matcher.

> ### ✅ Fixed build 2673 — **32 was not in the candidate list**
>
> `Aura.cpp`'s sweep tried `{ 16, 24, 20 }`. A real 32-byte item was therefore not a near-miss, it
> was **undetectable** — and worse, undetectable in a way that looks like partial success: a stride
> that DIVIDES the real one still lands on a genuine object every k-th probe, so 16 validated half
> the pool and the sweep settled there "tentatively".
>
> Ordering does not decide the winner (`ProbeAllStrides` scores every candidate and takes the best),
> so adding 32 is enough: against the real stride it scores `named ≈ all / bad ≈ 0`, while the alias
> scores `named ≈ bad ≈ half`.
>
> **The "tentative" warning now states its cost.** Its old wording read as routine and the scan
> carried on as though it were an answer. When the validated count is under a quarter of the probe
> budget it now says so as an ERROR, names the denominator (200 — *"27 items validated"* means
> nothing without it), and points at the actual cause: *a multiple of this stride would validate all
> of them, and a round "N% named" in the object tree is that alias.*
>
> **Verify:** re-run the Development package. **PASS** = `FUObjectItem size detected as 32 bytes`,
> `Name sanity: 10/10`, and an Object Tree that reads 100% named rather than 50.0%.
>
> ### ✅ VERIFIED — the evidence was already on disk, filed 2026-08-06
>
> Six post-2673 runs of the Development package, `Logs\DumperTest\`, all identical:
>
> ```
> offsets-*.log   ObjectArray: FUObjectItem size detected as 32 bytes
>                 (200 items with valid names, 200 total valid, 0 bad)
> init-*.log      UE5_Init: Name sanity: 10/10 objects resolved
> ```
>
> `detected as` (not `tentatively set to`), **200/200 validated with 0 bad** against a probe budget
> of 200, and 10/10 name sanity where the broken run resolved only the even indices at 5/10. The
> third criterion — the object tree reading 100% rather than 50.0% — follows from the same run:
> D2's group scan verified on this package, and the stride alias is precisely what had made that
> scan walk **0** objects.
>
> **Nobody had to re-run anything** — the ⬜ outlived its own answer by a day because the check was
> filed as "re-run the Development package" rather than as a grep against logs the package had
> already written. Where a marker is passive, state the grep, not the run.
>
> ### ✅ RE-CONFIRMED ON THE CURRENT PACKAGE `[DUMPERTEST-LOG-2026-08-17]`
>
> The evidence above is from the package built **2026-08-05**, and the packages were **rebuilt
> 2026-08-14** to add the audit-#5 containers — so it no longer described the binary on disk. Re-run
> as greps against `Logs\DumperTest\`, all five log criteria still hold on the 2026-08-14 build:
>
> * `FUObjectItem size detected as 32 bytes (200 items with valid names, 200 total valid, 0 bad)` —
>   `detected as`, not `tentatively set to`; 200/200 against a 200 budget, 0 bad.
> * `UE5_Init: Name sanity: 10/10 objects resolved` — not the 5/10 that means a halved stride.
> * `[SCAN:GObj] Module anchor set to 'DumperTest.exe'`.
> * **D1 specifically:** all **15** `REFUSED` lines name `EOSSDK-Win64-Shipping.dll`, and GNames
>   still validates at `0x7FF63CD568C0` — the same `0x7FF63C…` image as GObjects at `0x7FF63CE43620`.
>   The anchor rule rejected the decoys *without* costing the real answer, which is the pairing that
>   makes this evidence rather than either half alone. One run resolved it by `aob` and another by
>   `pointer_scan`; both in-module.
> * `[SUMMARY] DynOff: CPN=no FProp=yes TagFFV=yes Outer=+0x20 validated=yes`, and **zero**
>   `does not deref to a UWorld` in the whole folder.
>
> ~~⚠ **Still not directly observed: the Object Tree header ratio.**~~ **✅ NOW READ OFF THE SCREEN
> 2026-08-17 `[GRP4-UI-2026-08-17]`.** UE5DumpUI 1.0.0.3262 connected to **DumperTest Development** —
> the configuration both defects lived in — shows:
> ```
> Object Tree   Objects: 25,179 (showing 5,000)
> Loaded 25,179 named objects (of 25,179 total, 100.0%)
> ```
> **25,179 / 25,179 = 100.0%.** This is the reading the step wanted and the one the log could not
> supply, since `ObjectTreeViewModel` builds the header from a different denominator.
>
> It discriminates because **both** defects would move this number and in opposite-looking ways: D3's
> halved `FUObjectItem` stride would walk roughly half the pool, and D1's GNames landing in
> `EOSSDK-Win64-Shipping.dll` would leave most entries unnamed. A ratio of exactly 100.0% on
> Development rules out both. *(An earlier capture during load read `25,172 / 25,179` — read the
> header only after the tree finishes loading, or the shortfall is just the progress bar.)*


Both came out of the config-only A/B (**same source, Shipping vs Development**) that this file has
called the highest-value first cell since 2026-07-29. It produced them on day one.

**D1 — GNames resolves into `EOSSDK-Win64-Shipping.dll` on a Development package.** ✅ **FIXED
build 2661** — see the fix note at the end of this item. Effort **M** · Risk med. On the Shipping build of the *same source* everything resolves cleanly
(`validated=yes`, GWorld fine). On Development:

```
[GNames] GNAM_SF_2: 1 match(es), none validated
AOBScanAllModules: 2 matches in '...\Engine\Binaries\Win64\EOSSDK-Win64-Shipping.dll'
[GNames] GNAM_SAT425_3: 2 matches (multi-module), validated -> 0x7FFCEF5F8FC0
```

GObjects is at `0x7FF67517D5A0` (inside the game exe); GNames lands at `0x7FFCEF5F8FC0`, **a
different module entirely**. On a monolithic build that cannot be right. Every in-exe GNames
pattern missed — the tables are Shipping-tuned — and the multi-module fallback then matched a
data pattern inside a **third-party SDK DLL** whose pointer happens to reach a plausible name pool,
so `ValidateGNames` accepted it.

**The whole failure chain is downstream of this one address:**
`Cannot find Guid or Vector struct` → `validated=NO (DEFAULTS)` → the FField/FProperty offsets stay
at defaults that are wrong for this build (`Next=+0x18/Name=+0x20` vs Shipping's `+0x20/+0x28`) →
`GWorld does not deref to a UWorld — recovery failed` → **Start-from-GWorld and Value Search both
fail.** One misresolution, four visible symptoms.

*Multi-module is deliberate and must stay* — modular builds put GNames in `CoreUObject`, which is
why the winning pattern is named `GNAM_SAT425` (Satisfactory 4.25). The fix is not to remove it but
to **rank same-module-as-GObjects first, and refuse an unrelated third-party DLL** (`EOSSDK`,
redistributables) when GObjects resolved inside the main executable.

> ### ✅ Fixed build 2661 — a module ANCHOR, not a denylist
>
> GObjects resolves first, so by the time GNames/GWorld/GEngine scan we already know which module
> the engine's globals live in. `Genau.cpp` now records that as `s_moduleAnchor` (set however
> GObjects was found — the data-scan fallback anchors as well as the AOB), and the multi-module
> pass uses it two ways: candidates in the anchor's module are tried **first**, and if the anchor is
> the **main executable** — i.e. the build is monolithic — a candidate resolving anywhere else is
> **refused outright**, naming the module it came from.
>
> **Deliberately not a list of SDK names.** This repo has been bitten three times by a fix verified
> against its own list rather than against the world (B34's three CE filenames, B14's seven thread
> procs, B47's session). *"The engine globals are all in one module unless the build is modular"* is
> structural, needs no maintenance, and cannot go stale as new redistributables appear.
>
> **Multi-module support is untouched for modular builds** — a real modular build puts GNames in
> `CoreUObject.dll`, which is precisely why the pattern that mis-won here is named `GNAM_SAT425`
> (Satisfactory 4.25). When the anchor is a DLL, the fix only reorders; it refuses nothing.
>
> **① Log-derivable, and the target is on disk.** Re-run the DumperTest **Development** package.
> **PASS** = `Module anchor set to 'DumperTest.exe'`, then GNames resolving to an address in the
> same `0x7FF6…` range as GObjects, `validated=yes` in the DynOff summary, and Start-from-GWorld +
> Value Search working. **FAIL, but informatively** = `REFUSED 0x… — it is in 'EOSSDK-Win64-Shipping.dll'`
> followed by no GNames at all, which would mean the in-executable patterns genuinely have no
> coverage for a UE 5.4 Development build and the answer is a new AOB, not a ranking rule.
>
> ### ✅ VERIFIED 2026-08-05 14:24 — first re-run, and it corrected itself in one pass
>
> ```
> Module anchor set to 'DumperTest.exe' — later targets must resolve there unless this build is modular
> [GNames] GNAM_SF_1: REFUSED 0x7FFCEF5F8FC0 — it is in 'EOSSDK-Win64-Shipping.dll' ...
> [GNames] GNAM_V1: 166 matches, validated -> 0x7FF675090840
> ```
>
> | | before | after |
> |---|---|---|
> | GNames | `0x7FFCEF5F8FC0` (EOSSDK) | **`0x7FF675090840`** — same module as GObjects |
> | DynOff | `validated=NO (DEFAULTS)` | **`validated=yes`** |
> | GWorld | `does not deref to a UWorld`, recovery failed | resolves, no warning |
>
> `FField Next=+0x18 / FProp Offset=+0x44` are now **validated**, which settles a second question:
> those are the genuine offsets for a Development build and differ from Shipping's `+0x20/+0x4C`
> legitimately. Five refused patterns later, `GNAM_V1` won in batch 4 with the correct address.
>
> **Two follow-ups the same run exposed, both fixed in build 2666:**
> - the refusal logged **once per match** — 8–11 identical lines per pattern, five patterns deep.
>   Now one line per pattern carrying the count.
> - **the UI reported "Connection Error / The operation has timed out" on a successful injection.**
>   The injected DLL scans BEFORE opening its pipe (the proxy path is the opposite), so the pipe
>   appeared **8.8 s** after injection — 1 s auto-start delay plus a 7.8 s scan that got *longer*
>   because refusing EOSSDK made it run all 31 patterns. The UI attempted the connect exactly once,
>   immediately. It now retries for 45 s, asking an `IsConnectedProbe` whether it worked instead of
>   assuming, and says which attempt it is on.

**D2 — Group Scan cannot see the object's own scalar UPROPERTYs.** ✅ **VERIFIED 2026-08-17
`[D2-PIPE-2026-08-17]`** — steps 1-3 of the operational checklist all pass on DumperTest Development,
build 3262, over the pipe. Effort **M** · Risk med.

> ### ✅ VERIFIED 2026-08-17 — the object's OWN fields are what matched
>
> **Step 1.** `begin_group_scan` with two `Exact` slots, `1234567` and `424242`, returns 2 candidates,
> and the live one's slots are **`I32` @ offset 1600** and **`FrozenInt` @ offset 1708** — the
> derived class's own scalars, which is precisely what the defect could not see. Not
> `PrimaryActorTick.*`, not `CustomTimeDilation`. Both offsets agree independently with the
> `walk_class` reply taken the same session (`I32` 1600, `FrozenInt` 1708), so two detectors concur.
> `match_count` is on the wire as documented.
>
> **Step 3 — the "groups need `Unchanged`" case, and it landed exactly as written.** A broad first
> scan (`Bigger 0` / `Exact 0`) gives **366** objects; a refine to `Changed` / `Unchanged` leaves
> **2**, one of which is `DumperTestActor_0` showing
> **`Health.CurrentValue=79`** and **`PrimaryActorTick.TickInterval=0`** — the row this checklist
> predicted in advance. `Health.CurrentValue` falls 1 Hz and `TickInterval` never moves, so the pair
> is the hard case rather than an accident.
>
> **Step 2 — the old `perSlotCap` of 8 is provably gone.** The first refine logged
> `leaves entered=` 2/3/4/8/**9**; a deliberately leaf-heavy scan (`Exact 0` on both slots, 432
> objects) pushed it to 14 and **20**. A hard cap at 8 cannot produce a 20, which is the discriminator
> — the raw magnitude is not, since `entered` is bounded by how many fields actually matched.
>
> ⚠ **Step 4 is NOT verified.** The `Leaves/slot` clamp (8–4096) is a client-side UI control and
> UE5DumpUI cannot currently be granted to computer-use. Its wire half does hold: none of these
> requests carried `per_slot_cap`, matching "absent unless the user moves the control".
>
> ### ✅ Step 4 SETTLED 2026-08-17 `[D2-UI-2026-08-17]` — but its PREMISE was wrong
>
> **The control is a `ComboBox`, not a NumericUpDown**
> ([ValueSearchPanel.axaml:550](../ui/UE5DumpUI/Views/ValueSearchPanel.axaml)), bound to
> `PerSlotCapChoices` — the ten powers of two from 8 to 4096
> ([ValueSearchViewModel.cs:79](../ui/UE5DumpUI/ViewModels/ValueSearchViewModel.cs)). **So an
> out-of-range value is unreachable from the UI and there is no clamp to test**; the `if (value <
> Min)` guard at `ValueSearchViewModel.cs:89-90` is a defensive backstop the interface cannot
> exercise. Confirmed by stepping the control: from `16`, four Downs lands on `256`
> (16→32→64→128→256), i.e. the enumeration is exactly as built. **Fix the step's wording, not the
> code.**
>
> **What the step should be checking, and it PASSES.** With `Leaves/slot` moved to **16**, a Group
> First Scan on DumperTest (`424242` + `100`) put it on the wire, and the *DLL's own* `pipe-0.log`
> recorded the request verbatim:
> ```json
> {"cmd":"begin_group_scan", … ,"deadline_ms":25000,"auto_skip_noise":true,"per_slot_cap":16,"id":2}
> ```
> At the 256 default it is omitted ([DumpService.cs:2244](../ui/UE5DumpUI/Services/DumpService.cs):
> `if (perSlotCap != Constants.GroupPerSlotCap)`), which is what the headless run above observed from
> the other side. **Both directions now have evidence.**
>
> ⭐ **The incidental result is the valuable one: a known AOT hazard does NOT bite here.** CLAUDE.md
> lists *"`ComboBox.SelectedItem` bound to a boxed value"* among the patterns that compile and run
> untrimmed and fail **only** after trimming. This control is exactly that —
> `SelectedItem="{Binding GroupPerSlotCap}"` over an `int` — and it was driven **on the AOT-trimmed
> `dist` binary** (256 → 16 → 256, selection honoured, value reaching the wire). One instance of the
> hazard class is therefore live-clear on 3262.
>
> *Also confirmed in passing:* the `(+N)` `match_count` annotation renders — both result rows read
> `FrozenInt=424242, NetUpdateFrequency=100 (+2)` / `(+3)`. Scan cost `83 ms` over 1,815 objects.

> ### ✅ D2 (樣本心跳) PASS 2026-08-17 `[GRP4-UI-2026-08-17]` — the DLL and the game agree to the digit
>
> The sample prints its own values on screen, so the panel can be checked against the *game's* opinion
> rather than against itself. Two readings **34 s apart** — Live Walker on `DumperTestActor_0` first,
> then the HUD:
>
> | field | DLL (Live Walker) | game HUD, +34 s | verdict |
> |---|---|---|---|
> | `FrozenInt` — *must NOT move* | **424242** (hex `32790600`) | **424242** | identical |
> | `Health.BaseValue` — *must NOT move* | **100** (hex `0000C842`) | **100** | identical |
> | `TickCount` — *climbs at 1 Hz* | **815** (hex `2F030000`) | **849** | +34 over a 34 s gap |
> | `F32_Ticking` — *falls 10.25/tick* | **600.75** (hex `00301644`) | **252.25** | 600.75 − 34×10.25 = **252.25 exactly** |
>
> **This is stronger than "the numbers look right".** The two frozen fields match to the digit, and
> the two moving fields match the sample's own documented rates **exactly** over the measured
> interval — including `F32_Ticking`, where an arithmetic slip of a single tick would show. Every hex
> column round-trips too (`0x32F` = 815, `0x00067932` = 424242, `0x44163000` = 600.75).
>
> *Conditions:* DumperTest Development, dist **3262**, windowed, ~34 s between the two captures, no
> wrap occurred in `F32_Ticking` during the interval (it falls from 600.75, and the wrap is far
> below).
>
> ⚠ **Only the first 5 candidates are logged**, by design (`[SCAN:grp]` debug, off the hot path), and
> only DROPPED ones appeared — the two survivors produced no `KEPT` line. Do not read the absence of
> a KEPT line as a failure.
On the Shipping package (where the pointers ARE correct), a Group First Scan over
`DumperTestActor_0` matched only **container elements and base-class fields**:

```
PrimaryActorTick.TickInterval=0, CustomTimeDilation=1     <- AActor's own
Set_Int[0][0]=1337   Map_NameToInt.Value[0][0]=111   Arr_Int[0][0]=10
```

Not one of `I32`(1234567), `FrozenInt`(424242), `TickCount`, `Health.*`, `Opt_*` — all plain
scalars declared on the derived class, all of which the **single-value** scan finds without trouble
(`Opt_Int_Set` @0x468, `Set_Int` @0x358). Because the only leaves recorded are ones that never
change, a follow-up `Changed`/`Decreased` refine returns **0**, which is what made this look like a
Mode-B problem for three rounds.

~~**Not a leaf cap:**~~ **It WAS a cap — just not the one I checked.** `Aura.cpp`'s `kLeafCap = 4096`
is fine; the one that bit is `Orden::MatchGroup`'s **`perSlotCap = 8`**.
**The sample is not at fault** — its on-screen heartbeat shows `frames=5971 TickCount=101` climbing
and `Health.CurrentValue` falling, so the values genuinely change.
**Sharpest repro, no timing involved:** Group First Scan, both slots `Exact` — `1234567` and
`424242`. Both are static UPROPERTYs on the same object.

> ### 🔬 NOT fixed — instrumented instead (build 2669), and the reason matters
>
> **Reading the code did not find it, and three hypotheses had already been written and abandoned
> against this bug's silence.** What the code says, all of it verified line by line:
> `CollectGroupLeaves` (`Aura.cpp:7686`) collects **every** direct and struct-nested numeric scalar —
> so `I32`/`TickCount`/`Health.*` *are* in the object block, and `CustomTimeDilation` appearing in
> the results proves that path runs. `emitGroupCandidate` (`:8175`) stores **all** leaves that
> satisfied each slot, not one representative, and seeds `prevValue` from the leaf bytes (`:8185`).
> `RefineGroupCandidates` (`:8367`) re-reads each stored leaf and compares prev-value predicates
> against its own `prevValue`. Every step is right on its own.
>
> So the refine now **counts why leaves die** instead of only saying "0 surviving", which is the
> same output for six different causes:
>
> ```
> RefineGroup cand[N]: DROPPED (a slot has no surviving leaf) | leaves entered=42 kept=0 |
>   dropped: unreadable=0 bad-width=0 no-target-for-width=0 predicate-said-no=42
> ```
>
> It also names the one cause that is invisible today — `GroupCandidateFeasible` rejecting a
> candidate because every slot matched **the same leaf**, so no *distinct* assignment exists. First
> 5 candidates only, `[SCAN:grp]` debug, off the hot path.
>
> **Next run answers it:** a Group First Scan then a `Changed` refine, then
> `grep "RefineGroup cand" pipe-0.log`. `predicate-said-no=<everything>` means the comparison is
> wrong; `entered=` far below the object's field count means the leaves were never stored; the
> DISTINCT-assignment verdict means the matcher, not the predicate. Those are three different fixes
> and the log now separates them.
> **✅ RUN 2026-08-17 `[D2-PIPE-2026-08-17]`** — see the block below; and ⚠ **that grep target is
> wrong**: the marker is emitted under `[SCAN:grp]`, so it lands in **`scan-0.log`**, not
> `pipe-0.log`. Grepping the file this line names returns nothing on a run that produced the lines.
>
> ### ✅ ANSWERED + FIXED build 2680 — the diagnostic named it on its first run
>
> ```
> RefineGroup cand[0]: DROPPED (a slot has no surviving leaf) | leaves entered=8 kept=0 |
>   dropped: unreadable=0 bad-width=0 no-target-for-width=0 predicate-said-no=8
> ```
>
> **`entered=8` IS the answer.** `Orden::MatchGroup` kept `perSlotCap = 8` satisfying leaves per
> slot — and leaves arrive in **field-declaration order, base class first**. On any `AActor` the
> first eight are `PrimaryActorTick.*`, `CustomTimeDilation` and friends, so a derived class's own
> fields — `I32`, `TickCount`, `Health.*`, `FrozenInt`, the ones a user actually searches for —
> **never made the list.** The kept list is also what the refine re-reads, so a `Changed` pass
> compared only never-changing engine fields and pruned all 618 candidates. The screen showed the
> same thing once the values were identical: *both* slots reporting `Set_Int[0][0]=1337`.
>
> **One list was serving two purposes.** The assignment check needs a handful; the refine needs
> everything. The cap now sizes for the refine (**256**), truncation is **reported** instead of
> silent, and it is an opt-in `per_slot_cap` on `begin_group_scan` (clamped 8–4096) so an object
> with unusually many numeric fields can be raised without a rebuild.
>
> Regression test `Test_Orden_PerSlotCap`: 40 satisfying leaves must all be kept, and an explicit
> small cap must both bound the list **and** set the truncation flag. 972 dll tests green.
>
> **UI control shipped (build 2690):** a `Leaves/slot:` NumericUpDown beside the Timeout slider,
> group-mode only, 8–4096 step 8, clamped in the VM and again in the DLL, attached to the wire only
> when moved off the default so existing captures stay byte-identical
> (`BeginGroupScanAsync_PerSlotCap_AttachedOnlyWhenMovedOffTheDefault`).
>
> ### ✅ VERIFIED 2026-08-05 — Mode B works
>
> `Changed` + `Unchanged` → **2 surviving objects**, and the row is the case the whole feature
> exists for: `DumperTestActor_0 — Health.CurrentValue=23, PrimaryActorTick.TickInterval=0`. One
> value moving, one holding still, in the same object.
>
> ### 🟡 Related, and NOT a scan bug: the row showed a leaf the filter had not matched
>
> Reported the same session: `FrozenInt=424242` never appeared in the list, and filtering for
> `424242` returned two rows that visibly contained no such value. Both are the same cause, and
> neither is a wrong result.
>
> The **filter** (`Radar.cpp` `BuildGroupOrderedView`) walks **every** leaf of every slot — class,
> defining class, field name and value. The **row renderer** (`Fern.cpp GroupCandidateToJson`)
> emitted `matches[0]`. So the filter was right, and the row was showing a *different leaf of the
> same candidate*. `FrozenInt` was in the kept set all along; `matches[0]` is base-class-first, so
> an `AActor` field always won the display slot.
>
> Audit #4's 4a root cause again — *the report and the reality computed by different code paths* —
> and this time the user was told to distrust a correct answer.
>
> **A second, worse form of the same thing (build 2695).** Each slot reported its own `matches[0]`,
> which is not an ASSIGNMENT: when two slots kept the same leaf first, the row read
> `PrimaryActorTick.TickInterval=0, PrimaryActorTick.TickInterval=0` — a value apparently paired
> with **itself**, which is exactly what `MatchGroup` forbids and `HasDistinctAssignment` had
> already proven impossible. The match was valid; the row was not showing it. Reported as *"找出來
> 的沒和其它數值配，是自己配自己"*. The renderer now claims leaves greedily across slots so the row
> is a real assignment.
>
> **This is also the answer to "Unchanged + Changed cannot find `Health.BaseValue` +
> `Health.CurrentValue`".** It can, and did — `BaseValue` was in the Unchanged slot's kept list all
> along, but `matches[0]` is base-class-first so `PrimaryActorTick.TickInterval` occupied the
> display. Not a design limitation, and nothing about the scan needed changing.
>
> **Fixed build 2690:** when a server-side filter is active the row reports the leaf that **matched
> it**, using the same helpers the filter uses (`GroupTextContainsCI` / `GroupSlotValueString`, now
> exported from `Radar` rather than duplicated). Each slot also carries `match_count`, so a row can
> no longer imply a candidate matched on one field when it matched on thirty.
>
> ### ✅ FIXED build 2719 — the FOURTH report of this shape, and the first fix that is not zero-sum
>
> Reported after 2701: *"`TickCount=NNN, FrozenInt=424242` 沒出現"*. It had matched. From that
> session's own `ui-pipe-0.log` (17:40:25, `query_group_candidates`):
>
> | slot | predicate | `match_count` | `matched_offsets` |
> |---|---|---|---|
> | 0 | Changed | 2 | `[1288, 1304]` |
> | 1 | Unchanged | 36 | `[52, 100, …, 1284, **1308**, 72]` |
>
> `1288` = `Health.CurrentValue` (named in the payload), `1304` = `0x518` = **TickCount**,
> `1308` = `0x51C` = **FrozenInt** — independently confirmed by the same session's
> `ScrollToFieldByOffset: offset 0x51C -> field 'FrozenInt'`. Two valid assignments, one row;
> 2701's same-struct preference gave the row to the `Health` pair.
>
> **Every earlier fix changed WHICH witness wins, which is zero-sum** — whichever pairing is
> promoted, the other reads as missing. This one makes the others *visible* instead:
>
> - **`query_group_slot_leaves`** (new pipe command) names every leaf one slot of one candidate
>   kept, on demand. Before it, the only trace of the other 35 was `matched_offsets`, and a raw
>   integer cannot tell anyone that 1308 is `FrozenInt`. Each leaf comes back as a full slot
>   match, so Live / Addr / Pivot / Locate act on it unchanged. UI: **All fields** in the
>   expanded row.
> - **`match_count` is finally parsed** (on the wire since 2690, read nowhere). The master row
>   now reads `Health.CurrentValue=19 (+1), Health.BaseValue=100 (+35)`, and the detail row
>   `… → 0x504 — 1 of 36 matching field(s)` instead of the old nameless
>   `= unchanged: 36 candidate offset(s)`. Counted through `MatchingFieldCount`, so Snapshot
>   Group / SPC Group / Class Pivot get the same annotation from their own offset lists rather
>   than repeating this as a separate report.
> - **The witness rule moved to `Radar::PickGroupWitnessAssignment`**, beside the filter it must
>   agree with. It lived in `Fern.cpp`'s JSON encoder, which no test target compiles — that is
>   *why* it kept drifting. Now covered by `Test_Radar_PickGroupWitnessAssignment` (sibling
>   preference, filter-by-name, filter-by-value, distinctness, empty slot, out-of-range
>   descriptor). Audit #4 root cause 4a, closed at the source: one encoder (`GroupLeafToJson`),
>   one decoder (`ParseGroupSlotLeaf`), one picker.
>
> **Four defects found while building it — and one of them was a fix that was itself wrong.**
> `deep` gives one candidate PER CONTAINER BLOCK sharing one instance address, so a lookup by
> `instance_addr` alone answers an expanded deep row with another block's fields — the request
> now carries an optional `leaf_addr` tie-breaker (a candidate *index* cannot work: a refine
> rebuilds the vector), and an unmatched hint with several candidates at that address returns
> `stale_leaf_addr` instead of guessing. `query_group_slot_leaves` was missing from
> `LaneRoutingPipeClient.BulkCommands`, which would have blocked Live Walker behind a running
> refine holding `GroupSessionManager::mu_`.
>
> A deep leaf's `offset` is 0 by construction, so `→ 0x0` was being printed as its location.
> **The first fix — fall back to the absolute `Addr` — was a new bug.** That holds on the live
> path (`addr` is `GroupSlotMatch::leafAddr`) but NOT on the Snapshot path, which cannot capture
> an array element's heap address and stores `AddrPlusOffset(objAddr, 0)` = the owning object's
> base. A Snapshot Deep row would have named the **UObject header** as the value's location — a
> plausible, copyable, wrong address, worse than the obviously-unknown `0x0`. Now the producer
> states it (`HasLeafAddress`, set only by the live decoder) and the row omits the arrow when
> nothing true can be said. **This is audit #4's 4b root cause — a cheap proxy signal standing in
> for a predicate a sibling computes correctly — committed while fixing 4a.** Found by the
> adversarial review, not by me.
>
> ### The follow-up that mattered more than the fix: **no rule can pick a specific pairing**
>
> Reported against 2715: *"要嘛以 TickCount 為主、要嘛以 FrozenInt 為主，可是畫面上沒有以這
> 二個值為主的 pair"* — and the sharper form, *"第二張截圖沒 filter … 那個 pair 根本不在
> result set"*. The pair **was** in the result set (both leaves kept — `TickCount`=183 ∈ 0..1000
> and `FrozenInt`=424242 ∈ 0..1000000, same object, cap 256 ≫ 38; the log proves the slot-0 half
> outright). What is true is the stronger claim underneath: **there is no automatic rule that
> produces that pairing**, because among slot 1's 36 unchanged fields nothing distinguishes
> `FrozenInt` from `I16` or `FixedArr`. 2 × 36 = 72 valid assignments; the scan cannot know which
> one was meant. Every "improve the heuristic" answer is therefore still zero-sum.
>
> **So the fix is to make it ASKABLE, and to make the unasked case findable:**
> - **The group filter is now space = AND** (`Radar::SplitFilterTerms`) — it was the last keyword
>   box in the repo treating its input as one substring, in violation of CLAUDE.md's own rule, and
>   that is exactly why a two-field request could not be expressed. `tickcount frozenint` keeps the
>   candidate (term-level AND, field-level OR) **and** the witness picker gives each term its own
>   slot, so the row becomes the requested pairing. Term order does not decide slot order.
> - **The leaf list is ordered object's-own-fields-first** (`Radar::OrderGroupSlotLeaves`). Leaves
>   are collected base-class-first, so `All fields` opened with `PrimaryActorTick.*`,
>   `InitialLifeSpan`, `CustomTimeDilation`, `AttachmentReplication.*` … and `FrozenInt` sat past
>   row 30 of a 220 px scrolling box. The tier comes from `definingClassName == className`, NOT
>   from the offset — a high offset correlates with "declared late" but is not that predicate, and
>   substituting a proxy for a predicate the data already carries is how this area regressed once
>   already this session.
> - **"All fields" toggles** — a second press collapses (locally, no round trip); re-opening
>   re-queries so a live scan never shows a stale snapshot.
>
> ### ✅ VERIFIED in-game 2026-08-05 — and it produced one more rule
>
> `tickcount frozenint` in the Filter turned the row into `TickCount=45 (+1),
> FrozenInt=424242 (+35)` — the requested pairing, on both the Development and the Shipping
> package. `All fields` lists and collapses.
>
> The maintainer then generalised the case they had *not* filtered: **"don't use a 0 as the
> default displayed pair — a 0 has little real meaning in a game."** Correct, and it was the
> default row's worst habit (`PrimaryActorTick.TickInterval=0, InitialLifeSpan=0` while the
> object's real fields had matched too). **Non-zero now wins inside every selection rule**
> (`Radar::IsZeroValueText`) — a tie-break within each rule, never a rule of its own: an
> all-zero slot still shows one leaf, and a zero the user explicitly filtered for still wins.
> The field column was also widened (`All fields` truncated
> `MinNetUpdateFrequency = unchanged -> 0x174 (FloatProper…`).
>
> **Separately, and not this feature's problem — the sample's Shipping heartbeat, now REWRITTEN:**
> `UEngine::AddOnScreenDebugMessage` is `#if !(UE_BUILD_SHIPPING || UE_BUILD_TEST)` in full
> (5.4 `UnrealEngine.cpp:11397`), so no flag could restore it. Replaced with `ADumperTestHUD`
> (`AHUD::DrawHUD` → `DrawText`, installed via `ClientSetHUD` from **Tick**, not the 1 Hz timer
> and not a GameMode asset). Whole chain read in the 5.4 source first — see the dev-log entry.
>
> ✅ **VERIFIED 2026-08-12 — and NO re-cook was needed.** The claim above ("needs a re-cook +
> re-package", "this environment cannot compile UE") was **wrong about the artifact, not just about
> the environment**: the Shipping package already on disk was built at 20:15 on 2026-08-05, five
> minutes *after* the HUD commit `b3d8593` (20:10:50), so it had carried `ADumperTestHUD` all along.
> Launching it (`-windowed -ResX=1280 -ResY=720`, no `-DumperTestNoHud`) puts **all five lines** on
> screen in the *Shipping* package.
>
> `TickCount` climbing is the actual assertion, and three independent counters agree on the same
> tick count over one 14.2 s window — which is what separates "numbers changed" from "the 1 Hz timer
> runs":
>
> | field | T0 | T1 (+14.2 s) | contract | |
> |---|---|---|---|---|
> | `frames` | 4593 | 5444 | must ALWAYS climb | ✅ |
> | **`TickCount`** | **78** | **93** | climbs **only** if the 1 Hz timer runs | ✅ **+15** |
> | `Health.CurrentValue` | 22 | 7 | must fall | ✅ |
> | `Health.BaseValue` / `FrozenInt` | 100 / 424242 | 100 / 424242 | must **NOT** move | ✅ |
> | `F32_Ticking` | 201.000 | 47.250 | −10.25 per tick | ✅ Δ153.75 = 10.25 **× 15** |
> | `F64_Ticking` | 20019.625 | 20023.375 | +0.25 per tick | ✅ Δ3.75 = 0.25 **× 15** |
> | `RawDouble_Ticking` (native) | 50039.500 | 50047.000 | non-UPROPERTY | ✅ Δ7.5 = 0.5 **× 15** |
>
> **Lesson worth keeping:** the item sat ⬜ for a week behind "this machine cannot compile UE" when
> the binary that settled it was already in `For Testing\`. Before accepting a build-environment
> blocker, check the artifact's mtime against the commit that was supposed to go into it.
>
> **Incidental, and it costs a session if you don't know it:** `-ExecCmds="t.MaxFPS 30"` is **silently
> ignored in Shipping**. `UE_ALLOW_EXEC_COMMANDS` is `UE_ALLOW_EXEC_COMMANDS_IN_SHIPPING` there
> (`Exec.h:13`) and 1 only otherwise, so `frames` climbed at ~60/s despite the cap. Use the
> **Development** package when a frame-rate cap actually matters.
>
> While verifying it, a **third** wrong Shipping assertion in the same file surfaced, pre-existing:
> `UE_LOG(..., Warning, ...)` does NOT survive Shipping (`Build.h:328` sets
> `NO_LOGGING = !USE_LOGGING_IN_SHIPPING`; `LogMacros.h:146-158` keeps only Fatal), so
> `[DumperTest] ADumperTestActor ready at 0x…` prints in Development only. All three misreads
> came from inferring a gate from a sibling instead of opening it.


**Z1 — zydis `a95bb71`: Path-2 native disassembly still resolves `[this+off]`.** ✅ **VERIFIED
2026-08-12** · Effort **S** · Risk low · **① log-verifiable**, one deliberate action.

> ### ✅ VERIFIED 2026-08-12 — DumperTest Development, DLL build 2794
>
> Property Search → `JumpZVelocity` on `CharacterMovementComponent` → **⇊ Funcs**. From
> `offsets-0.log`, eight Path-2 analyses:
>
> ```
>  8 instrs / 0 mapped     33 instrs / 0 mapped     17 instrs / 0 mapped
> 30 instrs / 0 mapped     31 instrs / 0 mapped     15 instrs / 0 mapped
> 27 instrs / 0 mapped      9 instrs / 1 mapped props   <- the one that resolved
> ```
>
> ### ✅ RE-CONFIRMED ON BUILD 3262 `[Z1-PIPE-2026-08-17]` — 497 analyses instead of 8
>
> Same sample, driven over the pipe instead of the UI, and at ~60× the volume: `walk_function_props`
> over every `UFunction` `find_instances` would return. **497 functions took the `disasm` path**
> (3 returned `none`, 0 took `bytecode`), `instrs` ran **min 7 / median 32 / max 98** — nowhere near
> zero — **zero decode errors**, and **8 functions mapped ≥1 property** (6×1, 1×2, 1×3).
>
> The mappings are *semantically* right, which is stronger than the bare N≥1 the criterion asks for:
> `GetPlaneConstraintNormal` → `PlaneConstraintNormal`, `GetPlaneConstraintOrigin` →
> `PlaneConstraintOrigin`, `IsActive` → `bAutoActivate`. A getter resolving to its own backing field
> is not something a mis-decoded `[this+off]` produces by chance.
>
> ⚠ **Two things worth knowing before re-running this.** (1) **`find_property_xrefs` does NOT
> exercise Path 2** — it is the bytecode path, and on this sample it reports
> `0 xrefs (scanned 9807 functions, 6 with script)` without emitting a single
> `AnalyzeNativeFunctionProps` line. Path 2 only runs via `walk_function_props` on a **script-less**
> UFunction. The checklist's "⇊ Funcs" step conflates them; they are separate commands.
> (2) `find_instances` capped at **500 with `truncated: true`**, so this is a SAMPLE of the UFunction
> pool. That is fine for an existence claim (N≥1 mapped) and would **not** have been fine for an
> absence claim.
>
> Against the criteria below: **zero decode errors** anywhere in the log folder, **at least one
> function with non-zero `mapped props`**, and `instrs` nowhere near 0. Path 1 ran too —
> `FindPropertyXrefs: 0 xrefs (scanned 9807 functions, 6 with script, 51ms)` — and 0 is expected on
> a stock template that has almost no Blueprint script, which the "NOT a failure" note below already
> covers.
>
> **One honest qualification:** the `instrs` distribution (8–33) skews **below** the v5 baseline of
> 17–65. That is the sample, not the decoder — the 9-instr function is precisely the one that
> **did** map a property, which is the opposite of a decoder bailing early, and a stock Third Person
> template's native getters are genuinely shorter than a commercial title's. If a future run shows
> the same skew *with* nothing mapping, that is a different result and worth chasing.
>
> ⚠ **Read the log LATE.** The first attempt in that session grepped ~20 s after the click, found
> nothing, and would have been recorded as a failure — the DLL had not flushed yet, and
> `offsets-0.log` grew from 6,048 to 7,885 bytes afterwards. Confirm the command was even sent
> (`grep find_property_xrefs ui-pipe-0.log`) before concluding anything from an empty grep.

The bump (`85d7518` → `a95bb71`, "Decoder patch for variable-position decoder-tree filters" #638)
is a decoder fix **plus a full table regen** — +34.9k/−45.7k lines. That is the same shape as the
v4→v5 bump, which was judged to warrant an in-game check for exactly this reason: the offline
tests decode byte sequences *we wrote*, and a table regen changes how *arbitrary game code*
decodes.

**What the offline evidence already covers** (so this check is not re-doing it): five
`Test_Denken_*` tests decode real x64 sequences through Zydis and all pass, including
`Test_Denken_ExcludesStackAndZeroDisp`, which exercises the `disp.size == 0` path the v5 migration
touched. 81 + 996 green, DLL builds clean.

**What it does NOT cover:** a real UE binary's compiler output.

**How to verify** — inject into any UE game, then run a Path-2 property xref (Interesting Funcs →
a native getter/setter, or Property Search's xref button) and grep **`offsets-0.log`** (category
`OARR` → `LF_Offsets`, `Sein.cpp` s_catMap):

```
AnalyzeNativeFunctionProps: 0x… exec=0x… -> N mapped props (U unmapped, I instrs, C calls)
FindPropertyXrefs: N xrefs (scanned … functions, … with script, …ms)
```

- **PASS** = `I instrs` is a plausible function length (the v5 baseline was 17–65 per function),
  `N mapped props` is non-zero on at least some functions, and there are **no decode errors**.
- **FAIL** = `instrs` collapses toward 0 (the decoder is bailing early) or every function reports
  `-> 0 mapped props` where the v5 run reported some.
- **NOT a failure:** mostly-empty results are Path 2's *nature*, not a regression — only native
  constant-`[this+off]` getters map at all; script-only properties have no machine code. The
  v5 verification run made the same point.

*Baseline to compare against: the 2026-06-23 v5 smoke test on SEED + TQ2 (both UE5) — 17–65
instrs/func, 1–5 `[this+off]` accesses, many `→ 1 mapped props`, TQ2 `2 xrefs`, zero decode
errors. See [[project-vendor-zydis-ue58-status]] in memory.*

> 🇹🇼 **繁體中文版：[pending-verification_zh-TW.md](pending-verification_zh-TW.md)** — a standalone
> translation of THIS section, reorganised by how much effort each check costs (seven of the ①
> items are free from any ordinary session). **This English section is canonical**: if the two
> disagree, this one is right, and edits land here first.
>
> **Procedure lives in [log-verification-checklist.md](log-verification-checklist.md)** — where to
> grep, which file each marker lands in, and which items need a deliberate in-game action versus
> which are free evidence from any ordinary session. THIS section is the status (⬜ / ✅); that one
> is the how. Two things worth knowing before you open a log: **there is no log level, nothing is
> filtered** (so `[DEBUG]` lines count), and **See-Through / Foreground-Lock evidence lands in
> `init-0.log`**, not `walk`/`pipe`, because their categories fall through `ResolveFile`.

### 🔎 Audit #4 items — split by HOW they can be verified

> **The rule, set 2026-08-04:** every audit-#4 fix is filed here classified into one of the two
> groups below **at the time it ships**. An item with no group is an item nobody can act on.
>
> **① Log-derivable** — provable by reading `%LOCALAPPDATA%\UE5CEDumper\Logs` after an ordinary
> session, or after one where a log line was *added for the purpose*. Prefer this: it needs no
> special skill and it leaves evidence. If an added line is heavy (per-object, per-tick), the commit
> that adds it must say so and mark it for removal once the item is ticked.
> Grep by **format string, never line number** — see
> [log-verification-checklist.md](log-verification-checklist.md).
>
> **② Manual-only** — needs a human at the keyboard doing something no log can cause (a click
> sequence, a specific game, a specific third-party install). Each of these carries its exact steps
> and the PASS/FAIL observation.
>
> **STATUS after the 2026-08-05 DumperTest sessions (builds 2622 → 2701):** the self-built sample
> closed **B28, V1a, V1c and NumericAll** — three of them ⬜ since builds 796/927/942 purely for
> want of a game containing the right UPROPERTY — and exposed **three dumper defects** nothing else
> had (D1/D2/D3 above, **all fixed and now all verified** — D3's ✅ filed 2026-08-06 from logs the
> package had already written). **13 ⬜ bullets remain.**
>
> **B4 (CE mailbox after the UI dies) is now the only open item that can produce silently wrong
> data** — the one it used to share that line with, the **drain straggler**, is no longer a
> verification item at all: four attempts deep, the phase instrumentation has proven it genuinely
> parked in `ReadFile` with both cancel APIs failing, so what remains is a structural code change,
> not a guess and not a check. **B8 is blocked** behind the PE-hook misdetection reproduced on
> stock UE 5.4.
>
> *Earlier line kept for the record:*
> **STATUS after five rounds of live testing (2026-08-04 → 08-05, builds 2622 → 2650):**
> **11 ✅ verified · 2 🟡 half (B8, Dump Explorer) · 14 ⬜ not yet exercised.**
> *(Dump Explorer's ⬜→🟡 came out of the "shipped but unproven" list below, not out of the 14 — an
> earlier revision of this line said 13 and was wrong. The 14 is the count of `- ⬜` bullets.)*
> Verified: B49, B31, B5(passive), B47, B35, B42, B36, **B34**, **B14+R5**, **B38**,
> the clean-scan report, and B8's main path.
>
> **The 2026-08-05 DQ7R pass moved three things and none of them were the three it aimed at:**
> the `Stop conn drain TIMEOUT` root cause fell out of a capture *already on disk* (see below —
> it needed no recurrence); **B47's earlier ✅ was found to be credited to a hand-injected session
> where the guard was not even compiled in**, and re-earned properly on that day's real proxy run;
> and **B28 was NOT tested** — the rows inspected were `StrProperty`, not FText. R8 was refuted
> outright by the maintainer (see [audit-2026-08-04-findings.md](audit-2026-08-04-findings.md)).
>
> **B14+R5 took three attempts, and the two failures are the most useful thing this audit
> produced.** Round 1: the guard was applied to an enumeration ("2 of 7 thread procs") that had
> counted wrong — a WER dump proved `std::terminate` on a thread no guard covered. Round 2: with
> guards on all ~15 entry points it crashed *again*, identically. That was the answer, not a
> setback — **there was never an exception.** `~std::thread()` on a joinable thread calls
> `std::terminate()` directly, and `UE5_Shutdown` is never called when a user closes a game, so
> every worker was still joinable at process exit. Fixed by making it a property of the TYPE
> (`Routine::SafeThread`) rather than a third list.
>
> **The lesson all of it shares, worth carrying into the remaining 14:** a fix verified against the
> *list* it was written from is not verified. B34 listed three CE filenames; B14 listed seven
> thread procs. Each was correct about every item on its list and wrong about the world. And when
> a fix does not take, re-read the EVIDENCE before adding more of the same fix — round 2 was
> effort spent on a mechanism that was never involved.
>
> ### ⚠ The three worth doing FIRST next session
>
> | # | Item | Why it leads |
> |---|---|---|
> | **1** | **B4** — CE mailbox survives a dead UI client | Fails **silently**: lookups answer 0 while reporting `scanned=<full pool>`, which reads as "the object isn't there". A CE-only session stays broken for its whole life. **Now the only open item that can produce wrong data.** |
> | **2** | **B16** — five dead coord-grid sort headers | Two minutes, and it needs nothing but the AOT build already in `dist`. Cheapest ⬜ on the list. |
> | **3** | ~~**B28** — CJK FText mojibake~~ | **✅ CLOSED 2026-08-05** on the DumperTest sample (8 FText fields, both directions). Only the STVoyager UTF-8 counter-check remains, and it is licensee-specific. |
> | — | ~~`Stop conn drain TIMEOUT`~~ | **ANSWERED, and no longer a verify item** — see the phase capture below. What is left is a structural fix. |
>
> The rest (B18, B19, B2, B25, B26, B13/B41 …) cannot produce wrong data or a crash, so they can wait.
>
> ### 🔍 `Stop conn drain TIMEOUT` — the invoke hypothesis is DEAD; do not "fix" it
>
> > **This entry briefly claimed the root cause was found. It was not, and the retraction is worth
> > more than the claim.** The reasoning was: `teleport_get_pose`/`teleport_get_pov` arrive at
> > 22:19:39.590/591, *"never answered"*, therefore the connections were inside a command. **The pipe
> > log has no response marker for ANY command** — 193 `Received`, zero `Sent` — so "no response
> > line" is not evidence of anything. 78 `teleport_get_pov` in that same file are equally
> > "unanswered" throughout a perfectly healthy session.
>
> **What the log DOES establish** (`pipe-20260804-221945.log`, build 2638):
> `Stop entry (conns=2)` → `cancels+wake done (0 ms)` → `conn drain TIMEOUT, 2 left (5000 ms)`.
> Two connection threads survived both `Tot::RequestShutdown()` and a `CancelIoEx` on every live
> connection handle (`Fern.cpp:481`, `:507-510`), then burned the full 5 s budget.
>
> **What reading the code eliminates — the invoke hypothesis, completely.**
> `UE5_Shutdown` (`Frieren.cpp:587`) calls **`Stark::Shutdown()` BEFORE `s_pipeServer.Stop()`**, and
> `Stark::Shutdown` drains the invoke queue setting every pending promise to `-7` (`Stark.cpp:328-340`).
> A pipe thread blocked in `EnqueueInvoke`'s `future.wait_for` is therefore **already released before
> `Stop()` is even entered** — the ordering exists for exactly this reason and the comment says so.
> So "make the Stark invoke wait observe `Tot::Requested()`" would be **a poll loop for a case that
> cannot occur on this path**. Considered and rejected 2026-08-05.
>
> > Rejected on its own merits too, for the record: honouring the full `Tot::Requested()` would let a
> > **latched `g_perCommand`** (set when one lane drops, cleared only on a fresh connect into an empty
> > registry) abort invokes on the *other* lane — manufacturing a new silent-failure bug of exactly
> > the B4 family. If it is ever wanted, it must key on `ShutdownRequested()` alone.
>
> **ANSWERED 2026-08-05 10:57 — the straggler line fired on the first proper repro, and it is the
> OTHER half.** Repro was exactly as filed (UI connected, untick the CE record):
>
> ```
> 10:57:00.157  Stop entry (conns=2)
> 10:57:00.157  Stop cancels+wake done (0 ms)
> 10:57:05.160  straggler: idle in ReadFile (the I/O cancel should have freed it), last cmd 'teleport_get_markers'
> 10:57:05.160  straggler: idle in ReadFile (the I/O cancel should have freed it), last cmd 'trigger_scan'
> 10:57:05.160  Stop conn drain TIMEOUT, 2 left (5002 ms)
> ```
>
> Both connections were **idle** (`inFlight == false`) — so nothing was stuck in a command, and the
> guess that started this whole thread was wrong in both directions. The cancel simply did not reach
> them.
>
> **Why a one-shot `CancelIoEx` misses.** `Fern::ReadLine` (`Fern.cpp:758-783`) reads **one byte per
> `ReadFile` call**, so a 40-byte command is 40 separate reads with 40 gaps between them. `Stop`
> fired `CancelIoEx` **once**, before the drain wait began. A thread sitting in a gap at that instant
> has no pending I/O to cancel (`ERROR_NOT_FOUND`) and then issues a **fresh** `ReadFile` that
> nothing will ever cancel — parked until the 5 s budget expires. With the Teleport panel polling
> twice a second on both lanes, landing in a gap is not a rare race: `Stop entry` came **146 ms**
> after the last command arrived.
>
> ### ✅ FIXED build 2650 — re-assert the cancel instead of firing it once
>
> `Fern::Stop` now slices its 5 s drain wait into `Grimoire::PIPE_STOP_CANCEL_REASSERT_MS` (100 ms)
> and re-issues `CancelIoEx` on every surviving connection each slice — the same *assert the state
> you want repeatedly* shape as the six re-assert workers, applied to teardown. Zero cost in the
> common case: with nothing left to drain the loop exits on its first wait with zero re-asserts.
> Safe under `m_connMutex` because a connection thread erases itself from `m_conns` **before**
> `CloseConnOnce` (`Fern.cpp:900-907`), so anything still in the registry has an open handle.
>
> A second line was added because the old log could say the threads were *"idle in ReadFile (the I/O
> cancel should have freed it)"* but **not whether the cancel had anything to free** — those are
> different bugs: `Stop cancel issued: N accepted, M had nothing pending`.
>
> ### ❌ That fix FAILED, and its own instrumentation said why — build 2651 has the real one
>
> Re-run 2026-08-05 12:55 (DumperTest, DLL build 2650), and the answer was in the line added for
> exactly this:
>
> ```
> Stop entry (conns=2)
> Stop cancel issued: 0 accepted, 2 had nothing pending
> straggler: idle in ReadFile ×2  (last cmd 'teleport_get_markers' / 'walk_world')
> Stop conn drain TIMEOUT, 2 left (5027 ms, 49 cancel re-asserts)
> ```
>
> **49 re-asserts, every one reporting nothing pending.** So it is not a missed window — my
> hypothesis is refuted by my own diagnostic. `CancelIoEx` cancels **asynchronous** requests; these
> pipe instances are created without `FILE_FLAG_OVERLAPPED`, so a thread parked in a blocking
> `ReadFile` has no pending IRP for it to find and it returns `ERROR_NOT_FOUND` every time, forever.
>
> **`CancelSynchronousIo` is the API for a synchronous operation blocking a known thread** — and it
> takes the **thread** handle, which only the serving thread can produce. Build 2651: each
> connection publishes a `DuplicateHandle` of its own thread, `Stop` calls `CancelSynchronousIo` on
> it alongside the (kept, harmless) `CancelIoEx`, and the handle is closed by the owner after it
> unregisters.
>
> **Same grep, same repro:** UI connected, untick the CE record → `grep "Stop conn drain"`.
> **PASS** = `satisfied, 0 left (… ms, N cancel re-asserts)`. **FAIL** = `TIMEOUT` again, which
> would mean the thread is not in `ReadFile` at all and the straggler line is wrong about it.
> ### ❌ 2651 FAILED TOO — stop guessing; build 2657 instruments instead
>
> Re-run 2026-08-05 13:25 on DLL build **2652** (which contains the CancelSynchronousIo fix, and
> no `could not duplicate serving-thread handle` warning, so the handles were published and the
> call was made):
>
> ```
> Stop cancel issued: 0 accepted, 2 had nothing pending
> straggler: idle in ReadFile x2   (last cmd 'teleport_get_markers' / 'refine_group_scan')
> Stop conn drain TIMEOUT, 2 left (5030 ms, 49 cancel re-asserts)
> ```
>
> **Three hypotheses, three refutations:** "stuck inside a command" (they are idle), "CancelIoEx
> missed the window" (49 re-asserts, all nothing-pending), "CancelSynchronousIo is the right API"
> (called, still timed out). Every one of them aimed at the same phrase — and that phrase is an
> **inference**. `inFlight` is set only around `DispatchCommand`, so a thread blocked in
> `WriteFile`, waiting on `writeMutex`, or **joining its watch threads in
> `StopWatchesForConnection`** is equally reported as "idle in ReadFile". A cancel does nothing for
> any of the latter.
>
> This is `feedback-fix-not-taking-reread-evidence` playing out verbatim: *when a fix does not take,
> re-read the evidence before adding more of the same fix.* Two were added.
>
> **Build 2657 replaces the label with an observation** — a per-connection `Phase`
> (Reading / Dispatching / Writing / StoppingWatches / Unregistering) stamped at every transition,
> reported with how long it has been there. `CancelIoEx` + `CancelSynchronousIo` are both kept:
> harmless, and correct for the case the phase may yet confirm.
>
> **Next run, same repro, one grep:** `grep "straggler" pipe-0.log`. It now names the real phase.
> `StoppingWatches` would mean the fix belongs in the watch-thread join, not in I/O cancellation at
> all — a different subsystem from the three already tried.
>
> *The re-assert loop is kept. It cost nothing (49 iterations of a failing syscall over 5 s) and it
> is what proved the diagnosis wrong quickly; a single shot would have looked like bad luck.*
>
> ### ✅ ANSWERED — the phase is `Reading`, three times over. **This is no longer a verify item.**
>
> Filed 2026-08-06 from captures already on disk. Three post-2657 runs, and the instrumentation
> said the same thing every time:
>
> ```
> 13:38:45  straggler: parked in ReadFile (waiting for the next command) for  73871 ms, last cmd 'get_object_list'
> 16:42:55  straggler: parked in ReadFile (waiting for the next command) for 264184 ms, last cmd 'walk_functions'
> 18:43:36  straggler: parked in ReadFile (waiting for the next command) for 145063 ms, last cmd 'query_group_slot_leaves'
>           Stop cancel issued: 0 accepted, 2 had nothing pending
>           Stop conn drain TIMEOUT, 2 left (5030 ms, 49 cancel re-asserts)
> ```
>
> **Phase `Reading`, parked 264 seconds.** Not `Dispatching`, not `Writing`, and **not
> `StoppingWatches`** — which was the hypothesis this instrumentation was added to test, and it is
> refuted too. The connection is genuinely blocked in a synchronous `ReadFile`.
>
> **Four attempts, each refuted by the diagnostic added for the previous one:**
>
> | # | Hypothesis | Refuted by |
> |---|---|---|
> | 1 | Stuck inside a command | `inFlight == false`; `Stark::Shutdown` already runs before `Stop()` |
> | 2 | `CancelIoEx` missed the window (2650: re-assert) | 49 re-asserts, every one `nothing pending` |
> | 3 | `CancelSynchronousIo` is the right API (2651) | called, handles published, still TIMEOUT |
> | 4 | "idle in ReadFile" is an inference, not an observation (2657: measure the phase) | the phase **is** `Reading` — so 1–3 were aimed at the right place and the wrong mechanism |
>
> **Root cause:** the pipe instances are created without `FILE_FLAG_OVERLAPPED`, so there is no
> pending IRP for `CancelIoEx` to find — `ERROR_NOT_FOUND`, forever, by construction.
>
> **What remains is a code change, not a verification.** Both remaining options are structural, and
> **neither is a fifth guess at the cancel API**:
> - close the connection handle from `Stop` so the blocking `ReadFile` returns an error, or
> - make the pipe overlapped.
>
> **When that fix ships**, the acceptance is unchanged and is one grep on the same repro (UI
> connected, untick the CE record): `grep "Stop conn drain" pipe-0.log` → **PASS** =
> `satisfied, 0 left (… ms, N cancel re-asserts)`.
>
> *Method note worth keeping: three consecutive fixes were written against the phrase "idle in
> ReadFile", which was a LABEL the code asserted, not something it had measured. Replacing the
> label with an observation cost one build and ended the thread.*
>
> ### ⚠ 2026-08-14 — audit #5/D5 says the ANSWER above is the wrong mechanism. Read this before fixing.
>
> The conclusion recorded above ("genuinely blocked in a synchronous `ReadFile`"; root cause = no
> `FILE_FLAG_OVERLAPPED`) accounts for attempt #2 failing but **not for attempt #3** —
> `CancelSynchronousIo` was called on a duplicated *thread* handle, which is exactly the API for a
> live thread blocked in synchronous I/O, and it *also* reported nothing-pending, 49 times.
>
> **A terminated thread explains both, and audit #5/D5's finding F1 shows the threads are terminated.**
> `Fern::Stop` has two logging call sites — `UE5_Shutdown` (`Frieren.cpp:588`, whose FIRST statement is
> `LOG_INFO("UE5_Shutdown: Cleaning up...")`) and `UE5_StopPipeServer`, which the shipped
> `scripts/UE5CEDumper.CT:772-780` only *probes* with `pcall(getAddress, …)` before calling
> `UE5_Shutdown` **alone**, deliberately. `grep -rn "Cleaning up"` over the whole
> `%LOCALAPPDATA%\UE5CEDumper\Logs` tree returns **zero**. So every `Stop entry` capture on disk —
> including the `conns=2` / `5029 ms` one this entry is built on — was reached from
> **`~Fern()` during `DLL_PROCESS_DETACH`**, i.e. after `ExitProcess` had already terminated the
> connection threads. A dead thread has no pending I/O for either cancel API to find, and it can never
> erase itself from `m_conns`, so the drain predicate is **unsatisfiable by construction** and the full
> 5 s budget burns every time.
>
> **Consequence for the two structural fixes proposed above: neither works on this path.** Closing the
> connection handle from `Stop` makes a *live* thread's `ReadFile` return an error — there is no live
> thread. Making the pipe overlapped has the same problem. The fix is not to run the drain at all when
> `Stop` is entered from the destructor: give `Stop` a `bool graceful`, skip the wait/joins/cancels on
> the DETACH path (the OS reclaims all of it — the reasoning `Heiter.cpp:288-301` already applies to its
> own DETACH body), and log which entry path was taken so future captures can be attributed.
>
> *This is attempt #5, and it is the first one aimed at a mechanism rather than at an API. Note what
> found it: not a new diagnostic, but reading the code that decides **who calls `Stop`** — a question
> none of the four earlier attempts asked, because the repro was assumed to be the CE untick it was
> written as, and no capture on disk is actually that repro.*
>
> ### ✅ There is now a ~30-second ON-DEMAND repro, with a negative control (2026-08-14, build 2812)
>
> Every capture in the four attempts above was **accidental**. This one is deliberate, headless, and
> takes half a minute on packaged `DumperTest` — use it as the acceptance test for whatever fix ships:
>
> 1. Launch `DumperTest.exe`, `scripts\inject-ue.ps1 -ProcessId <the -Win64-Shipping pid>`.
> 2. Connect a `NamedPipeClientStream` to `UE5DumpBfx` and send any command.
> 3. **Close the game with `CloseMainWindow()`** — `WM_CLOSE` → `ExitProcess` → `DLL_PROCESS_DETACH`.
>    **Not `Stop-Process -Force`**: `TerminateProcess` skips DETACH entirely, so a forced kill exits
>    fast and "proves" the bug is gone.
>
> | | Client at exit | `Stop entry` | Drain | Process exit |
> |---|---|---|---|---|
> | **B** | **held open** | `conns=1` | `TIMEOUT, 1 left (5030 ms, 49 cancel re-asserts)` | **6,046 ms** |
> | **A** | disconnected first | `conns=0` | `satisfied, 0 left (0 ms, 0 re-asserts)` | **1,105 ms** |
>
> One variable, 5.5× apart. **Run A as well as B** — without it, 6 s is indistinguishable from "how
> long a UE game takes to close", and A is also the regression guard: a fix that skips the drain must
> not make the already-correct `conns=0` path slower or noisier.
>
> **PASS for the fix** = case B reaches `Stopped` in well under a second, with the entry path named in
> the log so a future capture can be attributed to process-exit vs a CE Disable.
>
> ### ✅✅ FIXED build 2813 (2026-08-14) — attempt #5, and it passed its own acceptance test
>
> `Fern::Stop` takes `bool graceful = true`; `~Fern()` calls `Stop(false)`, which logs
> `Stop entry (process exit — skipping drain/joins, the OS reclaims this)` and returns before the
> cancel sweeps, the watch/scan joins and the 5 s drain. **Case B re-measured on the fixed build:
> 1,185 ms** (pre-fix 6,046 ms; pre-fix control A 1,105 ms) — a connection open at exit now costs
> nothing. The entry path is named in the log, so the attribution problem that made this take five
> attempts cannot recur.
>
> ### ✅ VERIFIED 2026-08-18 — the `graceful=true` path (CE Disable), DumperTest Development, dist 3262
>
> Unticked `Inject DLL + Start Pipe Server` with the UI **connected** (`conns=2`). All three PASS
> conditions met, and the whole shutdown took **174 ms**:
>
> ```
> 11:26:03.059 Mailbox: polling thread stopped
> 11:26:03.059 PipeServer: Stop entry (conns=2)          <- NOT "process exit"
> 11:26:03.059 PipeServer: Stop cancel issued: 2 accepted, 0 had nothing pending
> 11:26:03.060 PipeServer: AcceptLoop exiting
> 11:26:03.060 PipeServer: Stop cancels+wake done (0 ms)
> 11:26:03.060 PipeServer: Stop watches+scan joins done (0 ms)
> 11:26:03.060 PipeServer: Stop conn drain satisfied, 0 left (0 ms, 0 cancel re-asserts)
> 11:26:03.060 PipeServer: Stop accept join done (1 ms)
> 11:26:03.233 PipeServer: Stop monitor join done (174 ms)
> 11:26:03.233 PipeServer: Stopped
> ```
>
> `Stop entry` names `conns=2`, **not** `process exit` — so the destructor is not racing the explicit
> call, which was the FAIL signature. The drain says **satisfied** with **0 cancel re-asserts**, and
> `Stopped` follows. The 174 ms is entirely the monitor join (its poll is 200 ms, so this is one
> sleep), and every other phase is 0–1 ms.
>
> **This also closes the `executeCodeEx` basic-path step 3** ("untick the `.CT` record → `UE5_Shutdown`
> really runs, rather than the UI merely claiming unloaded"). `init-0.log` shows the DLL's own side:
>
> ```
> 11:26:03.057 [INIT]    UE5_Shutdown: Cleaning up...
> 11:26:03.059 [Grausam] Foreground lock DISABLED
> 11:26:03.060 [SENSE]   Diagnostics counters reset
> ```
>
> Real teardown of real state, two ms before the pipe server's own entry line — the ordering
> `UE5_Shutdown` → `Fern::Stop` that `Frieren.cpp:588` describes. CE's Lua console printed
> `[11:26:03] [UE5Dump] UE5 Dumper stopped.` and neither CE nor the game hung.
>
> ⬜ **B18's step 3 remains untestable on this sample** and is *not* claimed: it needs a title whose
> GObjects is **not** AOB-resolvable, so an Extra Scan is still running when the record is unticked.
> DumperTest resolves on the first pattern, so `Stop watches+scan joins done` had nothing to join —
> it reported `0 ms` because there was no scan, not because a long one was cancelled promptly.
>
> ⬜ does **not** mean "probably fine". It means nobody has looked. Most of the fourteen were
> simply not exercised (no wrapper installed, no UI killed mid-command, no Extra Scan).

#### ① Log-derivable — still open: B29 (log half) / B18 / B19 / B10 / B8 (🟡 deferred half)

- ✅ **`Fern::Stop` no longer waits for a client that may never come** (build 2569, B49) —
  **VERIFIED 2026-08-04 session logs (DQ7R / Elliot / CE), build 2622.** The CE session hit the exact wedge condition, `Stop entry (conns=0)`,
  which is the case the old `CloseHandle` on a synchronous listen handle blocked on forever:
  `cancels+wake done (0 ms)` → `conn drain satisfied, 0 left (3 ms)` → `accept join done (3 ms)`
  → `monitor join done (58 ms)` → `Stopped`. 59 ms end to end against a PASS bar of ~100 ms.
  *Original instructions kept below for the next build.*
  **Already instrumented** — the fix shipped with per-phase logging precisely so this needs no
  special run. Play normally with the UI connected, then disconnect the UI and untick the CE record.
  Grep `pipe-0.log` for `PipeServer: Stop entry` and the phase lines that follow it.
  **PASS** = `PipeServer: Stopped` appears within ~100 ms of `Stop entry`, and `Stop conn drain`
  says `satisfied`. **FAIL** = no `Stopped` line at all (the old unbounded hang), or a phase line
  showing seconds. The old behaviour logged *only* `Stopped`, so the presence of `Stop entry` also
  confirms you are on the new build.

- ⬜ **CE-plugin double-inject guard rejects a foreign wrapper** (build 2577, B29) — *log half.*
  Any session where CE's plugin menu is used: grep `init-0.log` for
  `is loaded but is not ours`. That line only exists in the new code, and it fires for the exact
  case that used to be misread. **PASS** = the line names the foreign module and injection proceeds.
  (The manual half — actually installing a wrapper — is in ② below.)

- ✅ **UI log rolls at 8 MB instead of stopping** (build 2585, B31) — **VERIFIED 2026-08-04 session logs (DQ7R / Elliot / CE), build 2622.**
  `Logs\UE5DumpUI\` holds `pipe-0.log` at **8,388,756 bytes** (the 8 MiB cap) *and*
  `pipe-0_001.log` at 4,055,182 bytes with a **newer** mtime (21:05 vs 20:53). The roll happened
  and writing continued into the new file — the silent-stop signature would have been the 8 MB
  file alone with a stale last line. *Original instructions below.*
  Free from any long session:
  `ls %LOCALAPPDATA%\UE5CEDumper\Logs\UE5DumpUI\`. **PASS** = files named `pipe-0_001.log` (or
  similar) exist alongside `pipe-0.log` once a category passes 8 MB, and the newest file's last line
  is recent. **FAIL** = a single `pipe-0.log` sitting at exactly ~8 MB with a stale last line — that
  is the silent-stop signature. Fastest way to reach it: Teleport → Auto refresh, left running.

- ✅ **Leftover-proxy reports land inside the app folder** (build 2585, B38) — **VERIFIED
  2026-08-04 22:49, build 2643.** `leftover-proxies-20260804-224903.txt` was written to
  `%LOCALAPPDATA%\UE5CEDumper\Reports\`, and the old `%LOCALAPPDATA%\Reports\` still holds only
  the pre-fix file from 2026-07-30. Log line: `Leftover report written: …\UE5CEDumper\Reports\…`
  *Original instructions below.*
  **Previously not exercised**
  — no Report has been run since the fix. Checked 2026-08-04 session logs (DQ7R / Elliot / CE), build 2622: `%LOCALAPPDATA%\Reports\` does
  hold `leftover-proxies-20260730-210903.txt`, but that is dated **2026-07-30**, i.e. before
  build 2585, so it is the documented pre-fix leftover and **not** evidence of failure.
  Run a proxy-cleanup Report. **PASS** = the file appears under `%LOCALAPPDATA%\UE5CEDumper\Reports\`. **FAIL** = it
  appears in `%LOCALAPPDATA%\Reports\`. (Files written before 2585 stay in the old place by design.)

- ✅ **A CLEAN scan still produces a report** (build 2637) — **VERIFIED 2026-08-04 22:49.**
  Raised by the maintainer: a scan that finds nothing must still leave an artifact, because
  "scanned everything and found nothing" and "never ran / looked in the wrong place / failed
  silently" are otherwise indistinguishable a week later. `BuildReport` had always handled the
  empty case; `CanWriteOrphanReport => Orphans.Count > 0` made that text unreachable and greyed
  the button out. Now gated on `OrphanScanRan`, and the empty report states the coverage:
  *"No leftover proxy DLLs were found. 67 folder(s) were examined."*

  > **~~Open UX question~~ — CLOSED 2026-08-05 by the maintainer: keep the current behaviour.**
  > *Find leftovers* shows its findings on screen; *Report…* writes the file. Writing a file stays
  > an explicit act. The discoverability half was already handled in build 2645 — the scan result
  > now names the button verbatim (*"press "Report…" to save this result as a file"*) and the clean
  > case states its coverage. **No auto-write. Do not re-open.**

- ✅ **The `UE5_Init` guard did not break ordinary init** (build 2592, B5) — *passive half* —
  **VERIFIED 2026-08-04 session logs (DQ7R / Elliot / CE), build 2622.** `Starting initialization...` and `Complete (UE…)` are one-for-one in
  all three games (DQ7R 5/5, Elliot 14/14, CE 1/1), and neither new line
  (`init already in progress`, `shutdown was requested during the scan`) appears anywhere.
  As stated below, that proves the guard is harmless, **not** that the race is fixed — the
  deliberate provocation is still open in ②. *Original instructions below.*
  *free from any session.* Grep `init-0.log` for `UE5_Init:`. **PASS** = `Starting initialization...` and
  `Complete (UE…)` alternate strictly one-for-one, and neither of the two new lines
  (`init already in progress`, `shutdown was requested during the scan`) appears. **FAIL** =
  a `Starting` with no matching `Complete` (the guard deadlocked — nothing should be able to cause
  this, which is why it is worth one grep per session), or two `Starting` lines in a row (still
  racing). Absence of the new lines proves only that the race did not *occur*; the deliberate
  provocation is in ② below.

- ✅ **Cheat Engine is never scanned as if it were the game** (build 2603, B34) — **VERIFIED
  build 2633**: `host process is 'cheatengine-x86_64-SSE4-AVX2.exe' — Cheat Engine is never a
  scan target`, and `scan-0.log` stayed at 121 bytes (header only) where the failing run left
  1.3 MB. *Earlier:* **FAILED 2026-08-04 session logs (DQ7R / Elliot / CE), build 2622, REFIXED build 2628, needs a re-test.**
  The capture shows `process: …\cheatengine-x86_64-SSE4-AVX2.exe` followed by
  `DllMain AutoStart: game process — calling UE5_AutoStart` — a 5.8 s AOB scan and the pipe
  opened **inside CE** (1.3 MB `scan-0.log` in that folder). Cause: the guard was an exact-name
  list and CE's real executable is the `-SSE4-AVX2` CPU-feature variant, which matched none of
  the three names. `g_isCEPlugin=0` too — the DLL was hand-injected, so the
  `CEPlugin_GetVersion` half could not help either. Now
  `Grimoire::IsCheatEngineExeName`, a case-insensitive **prefix** on the `cheatengine` stem
  (anchored at the start, so `MyCheatEngineClone.exe` is still allowed).
  **Re-test:** inject the DLL into CE by hand again. **PASS** = `host process is '…' — Cheat
  Engine is never a scan target` and **no** `scan-0.log` growth in that folder.
  Free from any
  session where the CE plugin is registered: grep `init-0.log` for `DllMain AutoStart:`.
  **PASS** = when the host is CE, either `CE plugin host — skipping auto-start` (the normal path,
  now reached because `CEPlugin_GetVersion` claims identity) or the new
  `host process is '…' — Cheat Engine is never a scan target`. **FAIL** = `game process — calling
  UE5_AutoStart` with `cheatengine-x86_64.exe` on the `UE5Dumper DLL loaded | … | process:` line
  two lines above. To provoke the original race: register the plugin but leave it **unticked**,
  then start CE.

- ⬜ **Extra Scan can be cancelled** (build 2603, B18). Needs a game where GObjects does NOT
  resolve by AOB, so Extra Scan actually runs long. Start it, then untick the CE record (or close
  the UI) while it is still going. **PASS** = `pipe-0.log` shows `PipeServer: Stop watches+scan
  joins done` within a second or so of `Stop entry`. **FAIL** = seconds of gap, or CE's window
  frozen until the sweep finishes — that is the unbounded join, and `UE5_Shutdown` runs on CE's own
  thread, which is why it freezes CE rather than just the game.

- ✅ **Log retention no longer dies at the first undeletable file** (build 2603, B19) --
  **CLOSED 2026-08-22** `[B19-LOCKED-2026-08-22]` (archive:3614), by
  `tools/verify/b19_locked_log.py`. **This bullet was STALE**; the ⬜ it carried until 2026-08-24
  was wrong.

  ⚠⚠ **CORRECTION 2026-08-24: I re-derived this row without triaging it first, and published a
  `[B19-BACKDATE-2026-08-24]` tag for it.** That tag is **withdrawn** -- there is one closure here
  and it is the 08-22 one. Two things make the mistake worth writing down rather than quietly
  deleting: I had spent the same day establishing that *this register is systematically stale and
  every row must be grepped before it is run* (B6 6-of-9, B7 2-of-5, A1 7-of-9) and then did not
  apply it to a row I found by accident; and **the existing rig is the better one** -- it stages
  **three** files (`b19a` unlocked BEFORE the lock, `b19b` locked, `b19c` unlocked AFTER), where my
  version had only the last two. `b19a` is what proves the sweep RAN, a control mine borrowed from a
  different verb instead of owning.

  ℹ️ The `b19` verb in `retention_backdate.py` is kept as a second, independent implementation
  (it reaches the same verdict from a different rig), but `b19_locked_log.py` is the rig of record.

  ```
  planted  ZZRET-aaa-held.log   (held open, sorts FIRST)
  planted  ZZRET-zzz-after.log  (deletable, sorts LAST)
    OK: the open handle really does make it undeletable
  held  file: present   <- correct, it is locked
  after file: GONE      <- the sweep continued past the lock
  ```

  ⭐ **Enumeration order is the whole test, which is why the names are `aaa-`/`zzz-`.** The defect
  was one shared `std::error_code` between the iteration and the per-file `fs::remove`, so a failed
  remove ended the loop -- and NTFS order being stable, it ended it at the same file every launch.
  If the deletable file were enumerated FIRST, the pre-fix code would have deleted it and the run
  would pass on a broken build. The rig also **asserts the lock works** (it tries `os.unlink` and
  requires an `OSError`) before drawing any conclusion, so the arm cannot be vacuous -- Python's
  `open()` on Windows does not pass `FILE_SHARE_DELETE`.

  ### ✅ The whole age sweep, verified the same way `[RETENTION-BACKDATE-2026-08-24]`

  The row above was the only live check the retention sweep had. Backdating turns the rest of it
  into a headless test too -- **a backdated mtime is not a simulation of the input, it IS the
  input**, since `PruneAgedLogs` / `PruneStaleProcessFolders` read nothing else.
  `retention_backdate.py plant` -> launch+inject -> `check`. **10 of 10 correct:**

  | case | age | want | got |
  |---|---|---|---|
  | folder `doomed` | newest -25d | die | gone |
  | folder `survivor` | newest -19d | live | present |
  | folder `edge-old` | newest **-21d-6h** | die | gone |
  | folder `edge-new` | newest **-21d+6h** | live | present |
  | folder `mixed` | oldest -25d, **newest -1d** | live | present |
  | folder `empty` | no files | die | gone |
  | file `old.log` | -25d | die | gone |
  | file `new.log` | -19d | live | present |
  | file `old.txt` | -25d | live | present |
  | `Bookmarks` `ancient.json` | **-400d** | live | present |

  ⭐ **What each control rules out, because "the file disappeared" on its own proves nothing.**
  `survivor` rules out *"it deleted everything"*. The **edge pair straddling 21 days by 6 hours**
  puts the boundary where `Grimoire.h:21` says it is, rather than merely "somewhere between 19 and
  25 days". `mixed` proves a folder's age is the **newest file inside** (`Sein.cpp:441-444`) and not
  the oldest, nor the folder's own mtime. `old.txt` -- 25 days old, in the same folder as a file
  that died -- proves the per-file sweep discriminates on the `.log` extension instead of nuking
  anything old. And the 400-day-old bookmarks file surviving confirms that store's retention is
  genuinely **0 = off** (CLAUDE.md: *"do not 'finish' it by enabling the sweep"*).

  ⚠ **The rig refuses to pass when the sweep never ran** -- measured, not just coded: planting and
  checking *without* launching a game reports `0 of the must-die cases died` and fails with
  *"NOTHING died -- the sweep did not run at all, so every 'LIVE' result above is vacuous"*. Without
  that guard, a rig that triggers nothing scores 6 LIVE out of 10 and looks half-right.

  ℹ️ Two shapes are exercised by different halves: the per-FILE sweep only touches the folder the
  running process OWNS (`Sein.cpp:628` passes `s_processDir`), so those cases live in the game's own
  log folder; the per-FOLDER sweep covers every OTHER folder and skips `keep` (`Sein.cpp:629`).
  Everything the rig writes carries a `ZZRET-` prefix and `clean` refuses to touch anything else.

  ### ✅ The C# store sweep too `[RETENTION-CSHARP-2026-08-24]` — a DIFFERENT sweep with a DIFFERENT trigger

  ⚠⚠ **CORRECTION TO THE RUN ABOVE: its `Bookmarks` case was VACUOUS, and I published it before
  noticing.** That case was triggered by launching the **game**, and the C++ sweep (`Sein`) never
  looks at `Bookmarks\` at all — so *"the 400-day-old bookmark survived"* was true of a sweep that
  was never pointed at it. **A no-sweep control is only worth anything when a must-die case in the
  SAME sweep dies beside it.** The C# sweep lives in `AppDataFolderMaintenance.PruneAged` and runs
  from each **store's constructor**, so it needs the **UI**, not a game.

  `py tools/verify/retention_backdate.py csharp` — **7 of 7 correct:**

  | file | age | want | why |
  |---|---|---|---|
  | `snapshots.ZZRETDOOM.db` | -25d | **die** | past `Constants.DataMaxAgeDays` = 21 |
  | `snapshots.ZZRETLIVE.db` | -19d | live | inside the window |
  | `snapshots.ZZRETGRP.db` | -25d | live | **old, but its GROUP has a fresh sibling** |
  | `snapshots.ZZRETGRP.db-wal` | -1d | live | the sibling that keeps the group alive |
  | `snapshots-ZZRETNOTOURS.db` | -400d | live | no dot after the prefix -> `GameKeyOf` refuses it |
  | `bookmarks.ZZRETBM.json` | -400d | live | `BookmarkStore` passes `maxAgeDays: 0` |
  | `teleport-coords.zzretcoord.json` | -400d | live | `CoordinateLibraryStore` passes `maxAgeDays: 0` |

  ⭐ **The group pair is the one worth having.** `SelectExpired` keys on `GameKeyOf` and expires a
  game's whole set on the **newest** member (`AppDataRetentionPolicy.cs:91-105`), which is the
  invariant CLAUDE.md states as *"a game's files move and expire as a GROUP"* — a `.db` migrated or
  expired without its `-wal` has silently dropped every transaction the WAL held. A 25-day-old `.db`
  surviving purely because its `-wal` is one day old is that rule, observed.

  ⭐ **Second, independent witness, scoped to the run.** `PruneAged` logs at
  `AppDataFolderMaintenance.cs:192`, and this run produced
  `[2026-08-24 16:39:01] AppDataFolderMaintenance: deleted 1 'snapshots' file(s) unused for 21+ days`
  — **count 1**, matching exactly one doomed group. ⚠ The first version of that check grepped
  *every* log and proudly printed lines from four days earlier: a witness that can never fail, i.e.
  the very shape this rig exists to avoid. It is now bounded to lines written after the fixtures were
  planted, and it **fails** if the count is not 1.

  ⚠ **CORRECTION: AF11 step 6's retention clause was NOT closed by this run -- it was already
  closed 2026-08-20** by `tools/verify/l10_step6_age_sweep.py`, credited at todo.md:4289. That rig
  had already planted the identical pair (a 30-day `TeleportCoords\` file that must survive against
  a 30-day `Snapshots\` group that must die) and states the same reason in the same words: *"the old
  file survived is equally well explained by the sweep never running at all"*. The claim that this
  run closed it is withdrawn.

  ℹ️ What IS new here, stated narrowly so it is not re-inflated later: the **mixed-age group** pair
  (`.db` at -25d kept alive by a `-wal` at -1d) -- `l10` plants a group of one uniform age, so the
  group-newest rule itself was untested -- and the `snapshots-…` prefix guard.

  ### ✅ The last-access hazard, which NEITHER existing rig could test `[RETENTION-ATIME-2026-08-24]`

  CLAUDE.md's app-data rule says the sweep must key on **`LastWriteTimeUtc`, stamped by the store on
  use — never last-access**, because NTFS last-access updates are **on by default**
  (`fsutil behavior query DisableLastAccess` = 2), so any AV / backup / indexer read would make every
  file look like today and the sweep would silently never fire. That is a severe, silent failure and
  it was **untested**.

  ⚠ **It was untested for a structural reason worth naming: every rig set both stamps together.**
  `l10_step6_age_sweep.py` and this rig's other cases all use `os.utime(p, (t, t))`, so a sweep
  reading the WRONG stamp passes them identically. `l10`'s own header calls that "fine" — and it is
  fine for what `l10` tests, but it means the hazard could not be caught there.

  The case that catches it: `snapshots.ZZRETATIME.db` with **mtime -30d and atime = NOW**. It
  **died**, so `LastWriteTime` is what is read. The rig now runs 8 cases and the log witness count is
  **derived** from the doomed set rather than hardcoded — it was a literal `deleted 1` until this
  case became the second doomed group, and it duly went red against a perfectly correct sweep. A
  stale expectation is the same defect class this rig hunts, so that was fixed rather than bumped.

  ### ✅ The THIRD sweep -- the UI's own `LoggingService` `[RETENTION-LOGSVC-2026-08-24]`

  `Logs\` is swept by **two independent subsystems**, not one, and only this one says what it did.
  `py tools/verify/retention_backdate.py logsvc` -- **11 of 11 correct**, UI only, **no game**:

  | case | contents | want | rules out |
  |---|---|---|---|
  | folder `lsstale` | `.log` -30d + `.txt` -30d | **die** | positive control |
  | folder `lsfresh` | `.log` -5d | live | "delete every non-UI folder" |
  | folder `lsmixed` | `.log` -30d + `.txt` **now** | live | folder judged by its own mtime |
  | ...its `.log` inside | -30d | **die** | ⭐ the file sweep never reaching non-UI folders |
  | ...its `.txt` inside | now | live | the `*.log` glob widening |
  | folder `lsempty` | no files, dir mtime -30d | **die** | -- |
  | folder `lsemptyfresh` | no files, dir mtime **now** | live | ⭐ "empty folders are always deleted" |
  | `UE5DumpUI\zzret-orphan.log` | -30d | **die** | the orphan sweep being dead |
  | `UE5DumpUI\zzret-recent.log` | -5d | live | it ignoring age |
  | `UE5DumpUI\zzret-keep.txt` | -30d | live | the glob |
  | `UE5DumpUI\zzret-0.log` | -30d | **die** | ⭐ that `-0.log` is protective |

  ⭐ **The `lsmixed` folder is three predicates in one launch**: the folder lived (so its age is
  the newest file inside, `NewestWriteUtc` globbing `"*"` so a `.txt` counts), the `.log` inside it
  died (so `PurgeOrphanedLogs` really does reach **non-UI** folders -- `LoggingService.cs:415` calls
  it per directory **with no live-name guard at all**), and the `.txt` beside it lived.

  ⭐ **`zzret-0.log` dying is the counter-intuitive one, and the two sweeps disagree by design.**
  `PruneAgedLogs` *does* skip anything ending `-0.log` (`LoggingService.cs:363`) -- but its glob is
  `{prefix}-*.log` for `prefix` in `{init,pipe,view}`, so it **never sees** this file. The orphan
  sweep globs `*.log` and skips only the live NAME LIST (`{init,pipe,view}-0.log`). ▶ **A `-0.log`
  suffix is not protective in general**; the three real live files are protected by being named, and
  a running game's own `-0.log` protects itself by having an mtime of *now* -- which is exactly what
  the call site's comment says.

  ⭐ **`lsempty` + `lsemptyfresh` is a pair because one alone proves nothing.** "The empty folder
  died" is equally explained by *"empty folders are always deleted"* -- which is precisely what the
  **C++** side does (`Sein.cpp:484`, the `!sawFile` branch removes them outright). Only the fresh
  empty folder surviving shows C# takes the `dir.LastWriteTimeUtc` fallback instead. **The two
  implementations genuinely differ here**, and now both are pinned.

  ⚠ **The rig refuses to run while a game is injected**, because `Sein` sweeps the same root and
  **logs nothing** -- a missing folder would have two possible authors. With the UI alone the
  witness is unambiguous, and this sweep does log:

  ```
  [2026-08-24 16:56:54] Deleted old log folder (>21d): ZZRET-lsempty
  [2026-08-24 16:56:54] Deleted old log folder (>21d): ZZRET-lsstale
  ```

  ℹ️ That line is younger than the feature: it used to dereference a null `_initLogger`, throw into
  the adjacent best-effort `catch`, and was **never once written** -- the folders were being deleted
  silently (`LoggingService.cs:101-104`). It is a witness precisely because that was fixed.

  ℹ️ Not exercised on disk: the UI-folder exemption (`LoggingService.cs:591-593`) would mean
  displacing the live `Logs\UE5DumpUI\`. It is pinned by `LoggingServiceRetentionTests.cs`; recorded
  as a deliberate omission rather than silently skipped.

  ### ~~⛔ STILL UNTESTED: there is a THIRD sweep~~ — SUPERSEDED by the section above, 2026-08-24

  Recon found that `Logs\` is swept by **two** independent subsystems, not one. Besides the C++
  `Sein` pair, the UI's **`LoggingService`** runs its own retention from its constructor
  (`App.axaml.cs:62`): `PruneAgedLogs` (`LoggingService.cs:83`, per category),
  `CleanupOldLogFolders` (`:105`, whole folders, `dir.Delete(true)`), and `PruneOrphanedLogs`, all at
  `Constants.LogMaxAgeDays`. **None of it is exercised above.**

  ⚠ **And it deletes folders under the same `Logs\` root while logging nothing**, so a rig that has
  both a game and the UI running cannot attribute a missing folder to either. The C++ arm above is
  safe only because it ran with **no UI**, and the C# arm only because its fixtures live in
  `Snapshots\` / `Bookmarks\` / `TeleportCoords\`, which `LoggingService` does not touch. ▶ A future
  `LoggingService` arm must run with **no game injected**, and should cover the cases recon names:
  the orphan sweep in the UI's own folder, the per-category sweep, the `*.log` glob guard, and
  `zzrig-0.log` at 30 days — which must **die**, because `-0.log` is a slot name and the live set is
  only `{init,pipe,view}-0.log`.

- ✅ **The proxy dedup guard says when it is not armed** (build 2603, B47) — **VERIFIED 2026-08-05,
  build 2645 — and the 2026-08-04 ✅ was credited to the WRONG SESSION.**
  > **The correction, because it is the same trap as B34 and B14.** The 08-04 note said *"DQ7R ran
  > through `version.dll` (a real proxy session, so the guard is compiled in)"*. It did not. That
  > line is inside `#ifdef UE5_PROXY_BUILD` (`Heiter.cpp:262-270`), and **not one 08-04 DQ7R session
  > logged `DllMain ProxyStart` or `Loaded real version.dll`** — every one was hand-injected, so the
  > guard was not in the loaded binary at all. Its absence proved nothing. *An absence is only
  > evidence once you have shown the producing code was present and running.*
  >
  > **The real evidence is the 2026-08-05 10:29:30 run**, which IS a proxy session —
  > `DllMain ProxyStart: proxy DLL mode — starting pipe server only (no scan)` →
  > `Loaded real version.dll: C:\WINDOWS\system32\version.dll` — and
  > `first-loaded-wins guard is NOT armed` is absent there. `Local\…_<PID>` succeeded where `Global\`
  > needed a privilege the game does not have. PASS, for the right reason this time.
  *Original instructions below.* Any proxy session:
  grep `init-0.log` for `first-loaded-wins guard is NOT armed`. **PASS** = the line is ABSENT
  (`Local\` + PID succeeds where `Global\` needed a privilege the game does not have). Its presence
  is not a failure of this fix — it is the fix reporting a condition that used to be silent — but
  it is worth investigating if it appears.

- ✅ **The PERF split no longer measures its own probe** (build 2610, B35) — **VERIFIED 2026-08-04 session logs (DQ7R / Elliot / CE), build 2622.**
  *This item had no verification entry when it shipped — a gap in the filing, found while
  sweeping these logs.* `grep 'PERF Snapshot capture'` gives
  `wall 5,256.2 ms … split dll 2,733.5 / ipc 692.4 / ui 1,830.3 ms`. The three parts sum to the
  wall time exactly, transport (dll+ipc = 3,425.9) is **less** than wall, and `ui` is a large
  non-zero. The pre-fix signature was the opposite: transport **exceeded** wall, so `ui` clamped
  to 0 and `ipc` absorbed the probe's own 93–125 ms round-trip. These are the numbers
  [multipipe-eval.md](multipipe-eval.md) reasons from.

- ✅ **CJK FText no longer renders as ASCII mojibake** (build 2599, B28) — **VERIFIED 2026-08-05 on
  the DumperTest sample, Shipping package, DLL build 2650.** All **eight** FText fields render as
  CJK in Live Walker, and every control holds:

  | field | rendered | role |
  |---|---|---|
  | `Text_Even2_OneNull` 統一 · `Text_Even2_TwoNull` 一言 · `Text_Even4_TwoNull` 統一言語 | correct | the trigger cases (even length, U+xx00) |
  | `Text_Odd3_OneNull` · `Text_Even6_NoNull` 日本語テスト | correct | length/parity controls |
  | `Text_Ascii` `DumperTest FText ASCII` | correct | **the other-direction control** — a fix that swung to always-UTF-16 would have broken this |
  | `Text_Localized` 統一言語 | correct | different `FTextHistory`, agrees with `Text_Even4_TwoNull` ⇒ the fault was never history traversal |
  | `Str_*` ×4, `Name_Cjk` | correct | FString + FNamePool paths, unaffected as expected |

  **This closes the one open item that could show the user WRONG DATA.** The counter-check on
  STVoyager's UTF-8 FText is a separate, licensee-specific case and stays open.

  > **Two observations from the same screen, neither of them B28:**
  > 1. `Text_Empty` renders as **`No`**. An `FText::GetEmpty()` should read as empty; `No` looks
  >    like a truncated `None` or a mis-typed render. Cheap to chase, cosmetic, but it is the empty
  >    display-string path and nothing else covers it. **NEW, unfiled.**
  > 2. The package under test was built from a **stale** `DumperTestActor.cpp` (退一步 where the
  >    repo had 走一步), so the odd-length control was not the documented one. It renders correctly
  >    either way, so B28's result stands — but see the identity-record note below; this is exactly
  >    what `capture_package_identity.py --project` now detects.

  *Original instructions below.*
  > **❌ NOT tested by the 2026-08-05 DQ7R pass, and the near-miss is worth recording so the next
  > attempt does not repeat it.** The rows inspected (`Name` / `DisplayName` / `ListName` = 忘名)
  > are **`StrProperty`** — FString, which goes through the UTF-16-only reader and **never had this
  > bug**. B28 lives in `ReadFTextString` alone. The hex confirms the FString path is fine and says
  > nothing about B28: `D8 5F | 0D 54 | 00 00 | 6F 00 | 78 00 | 00 00` = 忘(U+5FD8) 名(U+540D) NUL
  > 'o' 'x' NUL, `ArrayNum=6`, i.e. the game stores a fixed 6-TCHAR field with an **embedded NUL at
  > index 2**; the reader stops at the NUL and renders 忘名 — correct. Second miss: neither 忘
  > (U+5FD8) nor 名 (U+540D) has a **low byte of 0x00**, so this string could not have tripped the
  > trigger even as an FText.
  >
  > **What to do instead:** find a row whose Type column literally reads **`TextProperty`**. DQ7R's
  > 2026-08-05 walk logs contain **zero FText field reads** (the only `TextProperty` hits are the
  > class names `TextPropertyTestObject` / the `TextProperty` meta-class), so one has to be hunted:
  > Property Search for a TextProperty on a UI/dialogue/item-description class. Trigger characters
  > whose low byte IS 0x00, all common in JP/CN: **一** U+4E00 · **最** U+6700 · **言** U+8A00 ·
  > **退** U+9000 · **紀** U+7D00 — and the string must be an **even** number of characters.
  Affects **FText-typed values only** (`ReadFTextString`); FString goes
  through the UTF-16-only reader and never had the bug. **To test:** any game with Chinese/Japanese
  UI text — set the game to a CJK language, find an FText property in Live Walker or Property
  Search. **PASS** = the value reads as CJK. **FAIL** = short ASCII punctuation soup (`,{1`, `-N?e`)
  where CJK belongs. Worth checking specifically on a string with an **even** character count
  containing a `U+xx00` character (一, 第…一, 統一) — that is the exact trigger. Counter-check that
  the fix did not swing the other way: **Star Trek Voyager (UE5.6)** stores its FText as UTF-8, and
  its Chinese must still read correctly.

- ✅ **Fly/Noclip no longer leaves the pawn ghosted** (build 2596, B8) — **DEFERRED HALF CLOSED
  2026-08-23 `[B8-DEFERRED-2026-08-23]`**, DumperTest dev / DLL 3337:

  ```
  20:24:47.820  Fly: SetActorEnableCollision(0) invoked        <- collision OFF (noclip on)
  20:24:53.898  Fly: noclip = 0                                <- disable, game thread FROZEN
  20:24:53.912  Fly: worker stopped
  20:24:53.912  [WARN] Fly: DISABLED but the pawn's collision is still OFF (game thread
                       unresponsive) — waiting for it to resume to restore it
  20:24:53.913  Fly: waiting for the game thread to resume so the pawn's collision can be restored
  20:24:58.729  Fly: SetActorEnableCollision(1) invoked        <- the retry fires
  20:24:58.729  Fly: game thread resumed after 4750 ms — pawn collision restored
  ```

  All four elements of the fix observed: collision goes off; the disable **notices** the
  unresponsive game thread instead of optimistically committing; it says it is waiting; and on
  resume it **retries and applies** the restore. The pawn is not left ghosted.

  ⭐⭐ **The row's own framing is what kept this stuck, and it should not be re-used.** It says
  the deferred half *"needs a game that actually goes quiet when backgrounded"* — Elliot would
  not, and the hunt for a title that does was the blocker. But the code branches on
  **`Stark::IsGameThreadResponsive()`**, and `tools/verify/suspend.py suspend-tid` flips that
  **deterministically** on any title, DumperTest included. Backgrounding is a proxy for the
  condition; suspending the game thread *is* the condition. No `-DumperTestIdle` was needed.

  ⚠ **Grep for the right string.** The disable path logs `Fly: DISABLED but the pawn's collision
  is still OFF`; the *worker* tick logs `collision … deferred`. Searching for the worker's
  wording against the disable path produced a false FAIL on the first run even though the
  behaviour was already correct.

- 🟡 (original entry) **Fly/Noclip no longer leaves the pawn ghosted** (build 2596, B8) — **MAIN PATH VERIFIED**
  > **⚠ READ THIS BEFORE RE-TESTING — the deferred half is NOT reachable by closing the game.**
  > Closing a game never calls Fly's disable at all: `UE5_Shutdown` does not run on game close
  > (proven — zero `UE5_Shutdown: Cleaning up` lines in any session), so `Dunste::SetEnabled(false)`
  > never executes and `DISABLED but the pawn's collision is still OFF` can never be printed.
  > Confirmed in the 22:33 Elliot run: Fly was ON, the game was closed, and there is **no
  > `Fly: DISABLED` line at all**. That run is a B14 test, not a B8 test.
  >
  > The deferred half needs the **Disable button clicked while the game thread is quiet**. The
  > 22:01 Elliot run did click Disable — and `SetActorEnableCollision(1) invoked` proves the game
  > thread was still ticking, so Elliot does not appear to idle when unfocused. Alt-tab duration is
  > not the variable; whether the title honours `t.IdleWhenNotForeground` is.
  >
  > **So this needs a game that actually goes quiet when backgrounded.** If none is to hand it is
  > reasonable to close it as accepted-unverified: the code path is the same one Schlacht has been
  > running in production since build 2364, and the main path is verified.
  (Elliot, 2026-08-04, noclip ON). The log shows the fixed ordering exactly:
  `Fly: worker stopped` → `Fly: SetActorEnableCollision(1) invoked` → `Fly: DISABLED`. Join
  before restore, and the restore is committed from the invoke *actually running*. **The
  DEFERRED path is still ⬜** — the game thread stayed responsive, so
  `DISABLED but the pawn's collision is still OFF` was never reached. To finish it, alt-tab
  away for >500 ms before clicking Disable on a title that idles when unfocused.
  *Original instructions:* The whole answer is in the
  log, and the trigger is the *ordinary* way to turn Fly off on an idle-when-unfocused title.
  **To test:** Teleport tab → Fly ON + Noclip → fly through a wall → **alt-tab to the UI** (wait
  >500 ms so ProcessEvent goes quiet) → click Disable. Grep **`walk-0.log`** for `Fly:` —
  NOT `init-0.log`: `Dunste.cpp` sets `LOG_CAT "FLY"`, which `Sein.cpp`'s `s_catMap` routes to
  `LF_Walk`. Confirmed against real logs 2026-08-06.
  **PASS** = `Fly: DISABLED but the pawn's collision is still OFF (game thread unresponsive)`,
  then — after you click back into the game — `Fly: game thread resumed after N ms — pawn collision
  restored`. **FAIL** = the old shape: a plain `Fly: DISABLED` and nothing else, after which the
  pawn falls through the world. Corroborate in-game: walk into a wall, it should stop you.
  Second, cheaper check on any Fly session: `Fly: collision disable deferred` may appear, but it
  must not repeat — it is rate-limited to once per stall.

- ⬜ **`WalkClassEx` memo — the win is already instrumented** (build 2596, B10). **Blocked on a
  BASELINE**, not on instrumentation: the retained logs hold exactly one
  `PERF Snapshot capture` line (`wall 5,256.2 ms`, 2026-08-04, post-fix), so there is nothing
  pre-2596 to compare it against. Either keep this number as the new baseline and compare the
  next capture of the SAME snapshot on the same game, or settle the correctness half alone
  (struct types / enum names / bool masks still populate). Snapshot capture is
  wrapped in a `DiagnosticsProbe`, so no new logging is needed: grep
  **`Logs\UE5DumpUI\view-0.log`** (or the game folder's `ui-view-*.log`) for
  `PERF Snapshot capture` — it is a UI-side probe, NOT in `pipe-0.log`. Corrected 2026-08-06. **PASS** = `wall … ms` is materially lower than the same capture on a
  pre-2596 build (the memo removes a 100–300 × `FieldInfo` deep copy per struct-array *element*),
  and correctness is unchanged — property grids still show struct types, enum names and bool masks,
  which are exactly the fields `WalkClassEx` adds on top of `WalkClass`. **FAIL** = those columns go
  blank (the memo would be serving a pre-enrichment entry), or a crash under a parallel scan (a
  handed-out reference being invalidated — the reason `try_emplace` landed first).

- ℹ️ **Attempted 2026-08-18 on DumperTest Development first — do not retry there** (it passed later, on
  Elliot; see the ✅ row below).
  The single-call-that-blocks-for-seconds requirement is **unmeetable on that package**, measured
  rather than assumed. Its GObjects pool is 25,179 objects, and the two heaviest whole-pool commands
  the UI can issue finish far inside `MonitorLoop`'s 200 ms poll:

  | command (all classes, deep, native-C, no noise filter) | UI-reported duration |
  |---|---|
  | `begin_value_scan` `NumericNoByte` / `Bigger` / `0` — 50,000 candidates | **113 ms** |
  | `begin_value_scan` `FString` / `Contains` / `"a"` — 1,060 candidates | **52 ms** |

  Both appear in `pipe-0.log` as single `begin_value_scan` commands, so this is one call, not
  chunking — it is simply over before a poll can land. **Move B4 to a large title**; GROUP 6 already
  says Elliot's 482 MB image "is what makes the race windows real; the sample is too small", and that
  reasoning applies here exactly. Recorded as *not tested*, per the run plan's rule 4.

- ✅ **DONE 2026-08-18 `[ELLIOT-B4-2026-08-18]` — CE mailbox survives a dead UI client** (build 2592,
  B4). **The arming line has never been captured before**; this run has it, on Elliot (85,068
  objects), dist 3262, DLL injected by its deployed `dxgi` proxy.

  **Two vehicles were tried, and the first one FAILED for a reason worth keeping** (below). The one
  that works is a **single synchronous** pipe command: `begin_value_scan`, ~700 ms with *Parallel
  scan* and *Batch read* unticked. The kill was fired **from the log** rather than on a timer
  (`tools/verify/kill_on_marker.py`) — a fixed sleep fired before the command started on the first
  two attempts and proved nothing:

  ```
  12:06:38.041 Received: {"cmd":"begin_value_scan","data_type":"FString","scan_type":"Contains",…}
  12:06:38.665 PipeServer: Client disconnected
  12:06:38.818 [WARN]  PipeServer: client gone mid-command (err=109) — aborting in-flight op
  12:06:38.826 [ERROR] PipeServer: Failed to write response
  12:06:38.826 PipeServer: per-command cancel cleared — no connection that raised it is still live
  ```

  * **The ARMING line is present** — `client gone mid-command (err=109)` (109 = `ERROR_BROKEN_PIPE`),
    777 ms after the command arrived. Per this row's own rule, that is what makes everything below
    mean anything, and it is the line every previous attempt lacked.
  * **The in-flight op really was aborted** (`Failed to write response`).
  * **The follow-up command reports a NON-ZERO count**: a fresh UI, reconnected, ran Instance Finder
    on `Actor` → **`Found 2 instances (scanned 85,068, non-null 84,410, named 84,410 (100.0%))`**.
    The FAIL signature — `0` answered while `scanned` shows the whole pool — is excluded.
  * ⚠ **`per-command cancel is latched` did NOT appear on the next command, and that is a PASS, not
    a miss.** The DLL cleared the cancel at disconnect (*"no connection that raised it is still
    live"*), so it never survived to poison a later command at all. The row was written expecting
    the *next* command to hit the latch and clear it; the shipped behaviour is stronger. **Reword the
    row rather than re-running it.**

  ### ⛔ Add a THIRD trap to the list below: `trigger_scan` is ASYNC
  The obvious vehicle — the multi-second startup scan — **cannot arm the latch**, and this was
  measured, not reasoned. Killing the UI 900 ms into a 3.4 s scan produced **no** arming line, and
  the pipe log says why:
  ```
  12:03:18.127 Received: {"cmd":"trigger_scan","id":2}
  12:03:18.127 trigger_scan: Starting async engine scan...
  12:03:18.128 RunScan: started
  12:03:18.636 Received: {"cmd":"scan_status","id":8}
  12:03:19.421 ... repeated 2x: scan_status
  12:03:21.525 RunScan: finished
  ```
  `trigger_scan` returns immediately and the UI **polls `scan_status`**. So for those 3.4 s the
  connection had no long command in flight — it is the same "thousands of short commands with gaps"
  shape as Dump All and Snapshot capture, which this row already warns about. **The scan looks like
  the ideal vehicle and is a trap.**

- ⬜ *(original instructions kept for the method)* **CE mailbox survives a dead UI client** (build 2592, B4). The evidence line is **cold** — once
  per latch, so it costs nothing to leave in. Needs a deliberate sequence but the whole answer is in
  the log, so it lives here: connect the UI, start something long (Property Search deep, or a full
  Instance Finder scan), **kill the UI process while it runs**, then use any CE-side lookup — the
  `.CT`'s Find Instance, or a teleport/GodMode hotkey on a game that resolves through the class-scan
  fallback. Grep `pipe-0.log` for `per-command cancel is latched`.
  **PASS** = that WARN appears **and** the command that follows it reports a non-zero result count.
  **FAIL** = the old signature: no WARN, and a lookup answering `0` with `scanned=<full pool>` —
  the message that made this bug read like "the object isn't there".
  > **⚠ Task Manager's Processes tab does NOT kill it.** "End task" there sends `WM_CLOSE` first and
  > only escalates if the app stops responding — so a responsive UI closes GRACEFULLY and the latch
  > is never set. Use the **Details** tab → *End process*, or `taskkill /F /IM UE5DumpUI.exe`.
  > **Measured 2026-08-06** (SEED BATTLE DESTINY REMASTERED, build 2738) on a session that did
  > exactly this: the UI still wrote `UE5DumpUI shutting down...` — a line `TerminateProcess`
  > cannot produce — and the server logged `Stop entry (conns=0)` /
  > `Stop conn drain satisfied, 0 left (0 ms, 0 cancel re-asserts)`. `g_perCommand` was never
  > latched, so the run proved nothing. **Absence of the WARN is not a FAIL** — check those two
  > lines first to tell "the guard worked" apart from "the test no-opped".
  > The other half matters just as much: **something long has to be IN FLIGHT** when the UI dies.
  > That session's last pipe traffic was 40 s before the close, so there was no command for the
  > disconnect monitor to latch a cancel against.
  >
  > **In normal use this only triggers on a real UI crash** — every orderly exit disconnects
  > cleanly — which is why it has stayed unverified and why it is worth keeping the cold WARN in.
  > It is NOT hard to provoke, though: one `taskkill /F` during a Deep Property Search is the
  > whole test.
  >
  > ### ⚠ Check the ARMING line first — `client gone mid-command`
  >
  > The latch has its own WARN, emitted immediately before it
  > ([`Fern.cpp:769`](../dll/src/Fern.cpp:769)): `client gone mid-command (err=…) — aborting
  > in-flight op`. **Grep for that BEFORE grepping for the B4 line.** Absent ⇒ `g_perCommand` was
  > never latched ⇒ the B4 WARN was right to stay silent and the run proved nothing. Only when it
  > IS present does the absence of the B4 line mean anything.
  >
  > ### The axis is not "long" — it is "ONE call that blocks for seconds"
  >
  > `MonitorLoop` sleeps **200 ms** between polls ([`Fern.cpp:732`](../dll/src/Fern.cpp:732)) and
  > peeks only connections whose `inFlight` is set (`:743`), so a single command has to still be
  > running when a poll lands. **A CHUNKED operation never arms it, however many minutes it takes**
  > — it is thousands of short commands with gaps in between.
  >
  > Two that look like the obvious choice and are **both traps** (each cost a real run on
  > 2026-08-06):
  > - **Dump All Metadata** — `DumpAllService` is a `do/while` over
  >   `GetObjectListAsync(offset, pageSize)` ([`DumpAllService.cs:115-133`](../ui/UE5DumpUI/Services/DumpAllService.cs:115))
  >   plus `WalkClassesBatchAsync` in chunks of 200 (`:262`). **Measured: `get_object_list` pages
  >   50–80 ms apart** (19:45:16.124 → .201 → .249 → .323) — no poll ever caught one. The client's
  >   death surfaced through the connection's own write instead (`Failed to write response` →
  >   `Client disconnected`, same millisecond) and no latch was set.
  > - **Snapshot capture** — `Renge.h:161-165` says it outright: `begin_snapshot` + `snapshot_chunk`
  >   stream `[offset, offset+limit)` **"like get_object_list"**. Same shape, same no-op.
  >
  > Use one of the **single blocking scans** instead — all in `Aura.cpp`, which holds 30 of the
  > DLL's `Tot::Requested()` checks precisely because these are the ops expected to run long:
  >
  > | Command | UI | Why it is long |
  > |---|---|---|
  > | `begin_value_scan` | Value Search, first scan | every object × every property; heaviest by default |
  > | `find_path_from_gworld` | 🌍 Locate in GWorld | BFS, and the toolbar **depth slider** is a direct cost knob |
  > | `find_refs_to_uobject` | Live Walker → Find Refs | reverse-scans the whole pool incl. nested structs/containers |
  > | `find_instances` | Instance Finder | full-pool scan |
  >
  > On a small pool (SEED BATTLE: 69,688 objects) even these can finish fast — which is why the
  > arming line, not a stopwatch, is the thing to check. Locate-in-GWorld with the depth slider
  > raised is the only one with a knob you can turn until it is slow enough.

#### ② Manual-only — still open: B29 (third-party-wrapper case) / B25

- ✅ **Symbol-export GWorld no longer claims to have an AOB** (build 2581, audit #4 B2) —
  **VERIFIED 2026-08-12 on Satisfactory** (UE 5.6, 137,391 objects, DLL build 2798). Both halves.

  The precondition held live: `scan-0.log` shows
  `TrySymbolExport: Found '?GWorld@@3VUWorldProxy@@A' in module 'FactoryGameSteam-Engine-Win64-Shipping.dll'`
  → `GWLD_EXP … [WINNER]`. GObjects (`GOBJ_EXP`), GNames (`GNAM_EXP_TOSTR`) and GEngine (`GENG_EXP`)
  resolve the same way — this build is modular, so **all four** exercise the gate at once.

  | half | evidence |
  |---|---|
  | toggle greyed | `get_pointers` returns `gworld_aob: ""` → `IsAobSymbolAvailable=false` → `CanUseAobSymbol=false` → `IsEnabled` binding at [`LiveWalkerPanel.axaml:231`](../ui/UE5DumpUI/Views/LiveWalkerPanel.axaml:231). **Observed on screen**: the *AOB* item in Live Walker → Options renders dim while every sibling is white. |
  | export resolves | *Copy CE XML* on `GWorld → PersistentLevel`: 160,036 chars, **zero `??`**, **zero AOB markers** (`AOBScanModuleUE` / `aobscan` / the mangled name / `UE_GWorld`), root `<Address>1E4542EAEA0</Address>` literal with `+30` child offsets. |

  The mechanism is [`IsCeReplayableAob`](../dll/src/Himmel.h) suppressing the triple for
  `SymbolExport` / `SymbolCallFollow` / `CallFollow`, whose comment already cited this item.

  > ### 🔴 Found en route, and it was NOT cosmetic — fixed in build 2798
  >
  > The same payload reported `gworld_method: "aob"` next to `gworld_pattern_id: "GWLD_EXP"` and an
  > empty AOB triple: three fields disagreeing. [`Genau.cpp`](../dll/src/Genau.cpp) hardcoded
  > `"aob"` at all five sites whenever the scan returned non-zero, so every symbol-export and
  > CallFollow win was mislabelled, and `FindAll: Complete` printed `(aob)` for all four.
  >
  > **The trap:** the obvious fix — report the true mechanism — regresses the UI on its own.
  > `PointerPanelViewModel` asked `method != "aob"` to mean *"found via fallback"*, and
  > `ShowGWorldRecovered` asked it to mean *"found via a recovery path"*. Relabelling alone would
  > have raised a spurious **"found via fallback"** warning on all four pointers on Satisfactory
  > **and** badged its GWorld as **"recovered"** when nothing recovered anything. A symbol export is
  > the *strongest* result the scanner produces (priority 0, tried first, survives a recompile), not
  > a fallback.
  >
  > So both sides moved: the DLL reports `symbol` / `symbol_call_follow` / `call_follow` / `aob`
  > (`ScanMethodName`), and the panel asks a membership question (`IsDirectScan`) instead of an
  > equality one. 8 tests, including recovery paths still badging and an unknown future value
  > failing loud rather than silent. Measured before/after on the same game: all four went
  > `aob` → `symbol` / `symbol_call_follow`, with the AOB triple still empty.

- ⬜ **CE-plugin double-inject guard — the third-party-wrapper case** (build 2577, audit #4 B29).
  Ownership is now decided by PE ProductName, not file name. **Verified on real files here** (our 5
  binaries say `UE5CEDumper`; the 4 System32 counterparts say `Microsoft® …`), but the case that
  motivated the fix has no test material on this machine. **To test:** install ReShade (or drop any
  third-party `dxgi.dll`/`dinput8.dll` wrapper) into a UE game folder, attach CE, click
  *UE5CEDumper: Inject && Connect*. PASS = it injects normally, and the DLL log carries
  `'dxgi.dll' is loaded but is not ours`. FAIL = the old *"already loaded … no injection needed"*
  message, after which the UI cannot connect. Also worth eyeballing there: a game path with
  non-ASCII characters must now appear intact in that message (it used to render as `EVERSPACE? 2`).

- ✅ **Recycle-Bin refusal on a volume with no bin** (build 2621, B13/B41) — **VERIFIED 2026-08-12,
  end to end, both directions. It FAILED twice on the way and took THREE fixes** (builds 2799 + 2801):
  the detector could not see the condition it was named after, the refusal it then produced was
  silently dropped before reaching a row, and — found only because the negative control was run — the
  candidate folder was never examined at all. Post-fix re-measurement on the AOT build is at the
  bottom of this entry.

  The check never needed the UI: `VolumeHasRecycleBin` is upstream of every row, and it answered the
  question with `SHQueryRecycleBin(root) == S_OK` alone. That call reports on the bin's **contents**,
  not on its **policy**. Measured on two different fixed volumes with `NukeOnDelete=1` (a 10 GB iSCSI
  scratch volume, and the data drive with the bin switched off deliberately):

  | detector | result |
  |---|---|
  | registry | `HKCU\…\BitBucket\Volume\{guid}\NukeOnDelete = 1` |
  | **functional** — throwaway file, `SHFileOperation` + `FOF_ALLOWUNDO` (*exactly* what `MoveToRecycleBin` issues) | `rc=0`, `fAnyOperationsAborted=false`, bin item count **5 → 5**, **file gone** |
  | the shipped probe `SHQueryRecycleBinW(root)` | `hr=0x0`, `items=5` → **`VolumeHasRecycleBin` returned `true`** |

  So the shipped sequence was: probe says the bin works → `MoveToRecycleBin` proceeds → the shell
  returns success → the caller reports *"N files moved to the Recycle Bin"* → **the files were
  permanently destroyed.** That is verbatim the outcome
  [`WindowsPlatformService.cs`](../ui/UE5DumpUI/Services/WindowsPlatformService.cs)'s own comment
  says the refusal exists to prevent; the refusal simply never fired. `SHQueryRecycleBin` succeeds
  because the stale `$RECYCLE.BIN` folder and its leftover items are still on disk after the policy
  is turned off — emptiness and disabled-ness are different facts and it can only see the first.

  **Fix (build 2799):** the policy is now read from the registry *before* the shell is asked, via a
  pure [`RecycleBinPolicy`](../ui/UE5DumpUI/Core/RecycleBinPolicy.cs) that encodes Windows' real
  precedence — Group Policy `NoRecycleFiles` (machine, then user) → `UseGlobalSettings` +
  global `NukeOnDelete` → per-volume `NukeOnDelete`, with **absent ≠ 0** throughout. The
  `SHQueryRecycleBin` call is kept as a *second* gate (it still catches a volume the shell cannot
  service at all); both must pass. 18 unit tests cover every combination, including the two
  directions that are easy to get backwards under `UseGlobalSettings`.

  **Post-fix measurement, same machine, same session:** `T:` (`NukeOnDelete=1`) → `IsDisabled=true`
  → probe returns **false**, so the refusal fires. `C:` and `D:` (`NukeOnDelete=0`) → **true**.
  `D:`'s bin was **empty** at the time, which is the control that matters: an enabled-but-empty bin
  must still read as present, and any "fix" keyed off the item count would refuse every clean
  machine.

  ### ✅ The end-to-end half — DONE 2026-08-12. The prediction held, and the CONTROL found a second defect.

  **The blocker recorded in the previous handover was a miscount, not a bug.** `FakeGameT` *was*
  being detected the whole time — `Generic scan found 8 UE game(s)` already included it, and the
  panel showed the row. Nothing in `LooksLikeUeGameRoot` / `WalkDrive` / `IsExcludedBySteam` needed
  investigating. The fork written up for it (drop a `dummy.pak` and rescan) is moot; do not run it.

  **The rig, rebuilt with a real game** rather than a synthetic one — copy a small UE title wholesale
  instead of faking its shape, which removes every "is the detector confused?" question at once:

  ```
  T:  10 GB iSCSI volume, Fixed, $RECYCLE.BIN present, per-volume NukeOnDelete the ONLY variable
      T:\Light Maze\   <- Steam's "Light Maze" (215 MB, 27 files) copied whole from D:
  ```

  Deploy `version.dll` to it → delete everything Steam would own → `T:\Light Maze\LightMaze\
  Binaries\Win64\version.dll` is the sole survivor, which is exactly the leftover-after-uninstall
  shape. Re-scan drives so the game leaves the live list (it does, 9 → 8, so no `LiveGameFolder`
  veto can mask the result), then press *Find leftovers*.

  The VALID pair — same process, same bytes on disk, one registry DWORD between them. (An earlier
  pair, before the app was restarted, read **22 examined / 0 rows both ways** and is discarded: 22
  means the T: folder was not among the candidates, so those two runs measured nothing. See ② — that
  discrepancy is the whole finding.)

  | run | `T:` `NukeOnDelete` | folders examined | rows |
  |---|---|---|---|
  | 1 | **1** (bin off) | 23 | **0** — the refusal is computed, then dropped |
  | 2 | **0** (bin on) | 23 | **1** — `Recycle version.dll — folders left in place` |

  #### ① The predicted defect — now MEASURED, and fixed

  `PlanPrune` returns `NotOnFixedDrive`; the surface filter in
  [`ProxyDeployService.cs`](../ui/UE5DumpUI/Services/ProxyDeployService.cs) kept only
  `Deletable`/`FileOnly`, so the refusal never reached a row. The user was told **"No leftover proxy
  DLLs found (23 folder(s) examined)"** while our DLL sat on the one kind of volume where deleting it
  by hand cannot be undone. B13/B41's own PASS criterion was unobservable as shipped — exactly as
  predicted from code reading, and now watched happening.

  #### ② The defect the CONTROL found — and it is the one users hit daily

  In the FIRST pair (the discarded one above), flipping the bin back on was supposed to be a
  formality. It also returned 0, **which is what proved the bin-off run had measured nothing**: the
  T: folder was never a candidate. `CandidatesFromLogs` read our own `view-*.log` with
  `File.ReadLines`, which opens `FileShare.Read` and therefore **cannot open the live `view-0.log`
  that our own logger is holding**. The per-file `catch` swallowed the sharing violation, so the
  current session's entire deploy log contributed zero candidates; those 22 came from an *archived*
  `view-20260731-*.log`. Restarting the app rotated our deploy line into an archive too, the count
  went **22 → 23**, and only then was the folder actually examined. That single number is what
  separates "looked and found nothing" from "never looked" — without it, the discarded pair would
  have been recorded as a clean confirmation of the prediction.

  > **What this cost the user:** deploy a proxy, uninstall the game, press *Find leftovers* in the
  > same session → **nothing found**. It only appears after an app restart. `SteamShapeScan` hides
  > this for Steam titles (it sees them without the log), so it bites exactly the non-Steam
  > locations the log sources exist to cover.
  >
  > **Generalisable lesson 1:** `File.ReadLines`/`ReadAllLines` cannot read a file anything else
  > holds open for writing — including *our own* logs. Any future code that mines our logs must use
  > `ProxyDeployService.ReadLinesShared` (`FileShare.ReadWrite | FileShare.Delete`).
  >
  > **Generalisable lesson 2 — and it is the one that generalises furthest.** When the PASS criterion
  > is that something does **not** appear, the run that makes it appear is not optional, however much
  > it looks like a formality. **Absence is the cheapest result in the universe to produce by
  > accident**: a broken rig, a filter that never ran, a candidate list that never included the item —
  > all of them render as a clean-looking confirmation. Here the "formality" is the *only* thing that
  > distinguished a correct prediction from a measurement of nothing. The companion habit is to read
  > the **"examined N"** counter first: 22 → 23 was what separated *looked and found nothing* from
  > *never looked*, and no amount of staring at the 0 would have revealed it.
  >
  > This also overturns a lesson recorded on 2026-08-12 that read *"B13/B41 does not need the UI at
  > all — measure `VolumeHasRecycleBin` and you are done."* Measuring the gate proved only that the
  > gate answers correctly; **it said nothing about whether the user is ever shown anything**, and in
  > fact they were not. If the PASS criterion is a string on screen, the string has to be looked at.

  #### The fixes (build 2801)

  | # | change |
  |---|---|
  | ① | `OrphanVerdictRules.ShouldSurface` now keeps `NotOnFixedDrive`. The scan filter, the row's `IsActionable` and the removal re-check were three hand-written copies of two *different* predicates; they are now one pure pair in [`OrphanScanTypes.cs`](../ui/UE5DumpUI/Models/OrphanScanTypes.cs), so they cannot drift. |
  | ② | `ReadLinesShared` replaces `File.ReadLines` for the log sweep **and** for the Steam `.acf` read (same bug there: a manifest Steam holds open made `TryReadAcfInstallDir` report *unreadable*, which fails closed and silently refuses every Steam candidate — safe, but the feature just stops working). |
  | ③ | The recycler question moved **below** `ClassifyLeaf`, so a no-bin volume can no longer manufacture a refusal for a folder holding nothing of ours — and the refusal now carries the file list so the row can NAME the file. |
  | ④ | Honesty: a blocked row authorises nothing (`AuthorisedFiles` empty even if the verdict gate were relaxed), the report says *"NOT removable"* instead of *"to be recycled"*, and the status line counts blocked rows separately. |

  22 new tests, including the negative controls that make them mean something: the same folder with a
  working bin is still `Deletable`; a no-bin volume holding a *foreign* DLL is `ForeignFilePresent`,
  not a recycler refusal; `ShouldSurface` still drops all nine refusals that hold nothing of ours; and
  the `ReadLinesShared` test asserts `File.ReadLines` **throws** on the same handle first, or it would
  be asserting nothing.

  #### Post-fix re-measurement — build 2804, AOT/trimmed `dist\UE5DumpUI.exe` (54.3 MB), same rig

  Both directions, same process, one registry DWORD apart:

  | `T:` `NukeOnDelete` | examined | rows | the row says | checkbox |
  |---|---|---|---|---|
  | **1** (bin off) | 23 | 1 | *"This volume has no working Recycle Bin (removable/network, or the bin is disabled for it), so a delete here would be PERMANENT. Refused — remove the file by hand if that is what you want."* | **disabled** — clicking it does nothing, `Delete checked (0)` stays greyed |
  | **0** (bin on) | 23 | 1 | `Recycle version.dll — folders left in place` | **enabled** — ticks, `Delete checked (1)` goes live |

  The second row is the half that stops this being a probe that merely refuses everything: the SAME
  folder becomes actionable when, and only when, the bin is switched back on. Status line reads
  *"Found 1 leftover proxy DLL(s) — nothing removed yet. 1 cannot be removed from here — read the row
  for why."* Nothing was deleted at any point; the row was left unticked.

  **The rig is still on disk** (`T:\Light Maze\LightMaze\Binaries\Win64\version.dll`, T: back to
  `NukeOnDelete=1` as found) if this ever needs re-running. Rebuild it by copying any small UE game
  wholesale to a scratch volume — that is what made this tractable after the synthetic one wasted a
  session on a detection question that turned out not to exist.

- ✅ **VERIFIED 2026-08-19 `[B25-SYNTH-2026-08-19]` — the pre-4.11 refusal no longer fires on one
  PE field, and the UE3 refusal still does** (build 2621, checked on dist **1.0.0.3263**).
  **Both branches PASS**, on two purpose-built exes and no game at all. Rig:
  `tools/verify/b25_marker_exes.py` (compiles them through `cmd`+`vcvars64`, then asserts each
  artifact actually carries — and the other actually lacks — what its branch depends on, so a
  stripped literal cannot masquerade as a clean refusal-did-not-fire).
  - **Branch A — PASS.** `b25a_subfloor.exe`, a PE `PRODUCTVERSION 4,5,0,0` and nothing else:
    `DetectVersion: PE VERSIONINFO -> UE4.5 (treated as 400+minor)` then
    `DetectVersion: PE VERSIONINFO says UE 405, below the 411 floor — NOT accepting that on its own
    (it would refuse the whole scan). Corroborating against the memory string scan.`
    **No `SKIPPING the scan` anywhere**, and `FindAll: Complete — … UE=405` — the scan ran to the end.
  - **Branch B — PASS.** `b25b_ue3.exe`, no version resource + the literals `UnrealEngine3` and
    `SeqAct_Interp`: both markers hit (`0x16350`, `0x16360`),
    `PRE-UE4 engine POSITIVELY identified (2/4 markers, 2 needed) -> sentinel 300`, then
    `FindAll: PRE-UE4 engine (Unreal Engine 3) — SKIPPING the scan`.
  - ⭐ **The two branches are separated by a number, not just by a grep.** `scan-0.log` is
    **3,886 lines for A** and **10 lines for B** — A really did sweep the AOB tables and B really
    did refuse before starting. A pair of greps could both be satisfied by a scan that half-ran;
    the line counts cannot.
  - **Negative control, free and same-day**: a stock `python.exe` sleeper reaches the *identical*
    terminal branch and logs `pre-UE4 markers 0/4, below the 2 needed` (5,305-line scan log — it
    scans, like A). Same code path, markers absent ⇒ B's 2/4 is caused by the two literals and by
    nothing else about being a small synthetic exe.
  - ⚠ **Not covered, deliberately**: the UE-version-*override* route the original step offered as an
    alternative provocation. The PE-resource route exercises the same gate and needs no UI.

- ✅ **DONE 2026-08-18 — Duplicate GameEngine records no longer break each other** (build 2621, B26).
  Ran on DumperTest Development, dist 3262, AOBMaker bridge live (the row's precondition — verified
  first, so step 1 could not pass vacuously).
  - **Step 1 PASS.** First *Get GameEngine* → `Added 'Get GameEngine → symbol UE_GameEngine' to Cheat
    Engine via AOBMaker…`. Second click → *"'Get GameEngine → symbol UE_GameEngine' was already
    pushed to Cheat Engine this session — copied it as CE memory-record XML instead of adding a
    second record."* CE's list holds **exactly one** record under the `UE5CEDumper (DLL)` group.
  - **Step 2 PASS on its load-bearing assertion.** Pasted the XML to make a real second record,
    ticked both, unticked the **older** — the newer record's `UE_GameEngine` still resolved to
    **`0x7FF7AD323670`**, the `&GEngine` slot, *not* `??`. Enable also logs
    `[GameEngine] UE_GameEngine -> &GEngine slot 0x7FF7AD323670 (auto-follows)`, so the slot binding
    (rather than a snapshot buffer) is what this title takes.
  - ⚠ **The expected debug line is BRANCH-SPECIFIC and cannot appear here.**
    `Services/PointerQueryScriptGenerator.cs:238-250` emits *"another record owns … leaving it
    alone"* only under `mayFallBack` — the `allocateMemory` **buffer** flavour, where there is
    something to free. DumperTest resolves the `&GEngine` AOB, so its record takes the **slot**
    flavour (`:256-258`), whose `[DISABLE]` is two lines with no ownership guard because there is no
    buffer. **This checklist's wording should be scoped to the buffer flavour**; expecting the string
    on the slot flavour is a mis-specification, not a failure.
  - ✅ **The slot flavour's `[DISABLE]` was broken — FIXED 2026-08-19; see `[SLOTSYM-2026-08-18]` in "Pending live-game verification".**

### ✅ FIXED 2026-08-19 `[SLOTSYM-2026-08-18]` — the slot `[DISABLE]` now actually unregisters (writeup moved to "Pending live-game verification")

*Found while separating B26's two branches. The reproduction (untick the single record → still
`140701739398768`; one manual `unregisterSymbol` cleared it on the first call) is preserved for
history below, with one correction: the mechanism was NEITHER of the two this section originally
posited.*

**Mechanism (read from the code, not guessed).** The `:256-258` cite was the **GWorld** branch, which
already unregistered correctly. The record that reproduced the bug is the **GameEngine** target, which
takes the `mayFallBack` `[DISABLE]` branch — there, `unregisterSymbol('UE_GameEngine')` was nested
inside the buffer-only `if mem and mem ~= 0 and cur == mem` guard. On the slot sub-path there is no
buffer, so `mem = getAddressSafe('UE_GameEngine_buf')` is nil, both the `if` and `elseif` are skipped,
the symbol is never unregistered, and the trailing UNCONDITIONAL `dbg('… unregistered')` lies. So it
is a THIRD mechanism (unregister trapped in a buffer-only guard), closest to variant (a). **(b)
double-registration is refuted** — ENABLE does a single `registerSymbol` on op 2, which is why "one
manual `unregisterSymbol` sufficed".

**Fix (applied 2026-08-19).** Both slot ends (GWorld + the GameEngine slot sub-path) now go through
shared `CeLuaHygiene.AppendSlotSymbolRegister`/`AppendSlotSymbolRelease`: a per-symbol reference count
in a CE Lua global keeps the symbol for a second still-ticked record (an address marker can't — two
records resolve the IDENTICAL slot), the last holder unregisters in a bounded loop, and the message
re-reads `getAddressSafe` AFTER the unregister. Also removed an accidental duplicate
`AppendContractCheck`. Pinned by 6 new `PointerQueryScriptGeneratorTests` + a real-`lua` runtime
simulation. Live-check steps are under `[SLOTSYM-2026-08-18]` in "Pending live-game verification".

**Why it mattered.** A symbol that survives its record's disable is a **stale symbol across a game
restart**: `UE_GameEngine` kept resolving to the previous process's module base, and anything built
on it read dead memory.

- ✅ **The five dead coord-grid sort headers** (build 2610, B16) — **VERIFIED 2026-08-12**, on the
  AOT/trimmed `dist\UE5DumpUI.exe` (56.9 MB, build 2794) against the DumperTest Development package.
  All five reorder **and** reverse on the second click; Label (the non-regression control) still
  works. 10 of 10 observed orders matched the prediction made *before* clicking:

  | click | order (by row label) | | click | order |
  |---|---|---|---|---|
  | X ↑ | 3,4,1,5,2 | | X ↓ | 2,5,1,4,3 |
  | Y ↑ | 5,2,1,4,3 | | Y ↓ | 3,4,1,2,5 |
  | Z ↑ | 4,1,2,5,3 | | Z ↓ | 3,5,2,1,4 |
  | Yaw ↑ | 2,1,4,5,3 | | Yaw ↓ | 3,5,4,1,2 |
  | Dist ↑ | 1,4,5,3,2 | | Dist ↓ | 2,3,5,4,1 |

  > **The dataset was built so the test could fail.** Five rows were entered via *+ From fields* with
  > values chosen so that **X, Y, Z, Yaw, Dist and insertion order all induce six DIFFERENT
  > orderings**. With a lazier dataset — say monotonic coordinates — a sort that did nothing at all
  > would have reproduced insertion order and read as a pass on every column. Dist was cross-checked
  > independently: the grid's own values (0 / 4,205 / 3,734 / 891 / 3,590) matched hand-computed
  > distances from the live pose to the unit, so the column is genuinely computed, not a placeholder.
  >
  > **Not exercised: Group and Map.** *+ From fields* leaves Group empty and stamps every row with
  > the current map, so both columns held one value across all five rows and no ordering could be
  > observed. Label carried the load as the text-column control.
  >
  > ### CLOSED 2026-08-24 `[B16-GROUPMAP-2026-08-24]` -- both columns exercised, both directions
  >
  > DumperTest Development (UE504, 25,212 objects), `dist\UE5DumpUI.exe` **57,380,352 B = 54.7 MiB
  > AOT-trimmed** build 3343, DLL 3343. Rig: `tools/verify/b16_coord_sort.py`.
  >
  > **All six predicted orders matched exactly, read off the grid:**
  >
  > | sort state | predicted = observed |
  > |---|---|
  > | baseline (no click) | `B-two D-four A-one E-five C-three F-six` |
  > | X ascending | `E-five C-three F-six A-one D-four B-two` |
  > | **Group ascending** | `B-two D-four A-one E-five C-three F-six` |
  > | **Group descending** | `F-six C-three E-five A-one D-four B-two` |
  > | **Map ascending** | `C-three A-one E-five B-two F-six D-four` |
  > | **Map descending** | `D-four F-six B-two E-five A-one C-three` |
  >
  > ⚠⚠ **THE PROCEDURE THIS BULLET PRESCRIBED CANNOT FAIL, AND WAS NOT USED.** "Set distinct
  > groups (row editor → Group → Apply)" then clicking Group is **structurally vacuous on the
  > ascending click**: the VM's display order is ALREADY group-ascending -- `CompareForDisplay`
  > (`TeleportViewModel.cs:3468`) does `string.Compare(a.Group, b.Group, OrdinalIgnoreCase)` then
  > natural Label, and the wired header comparer (`TeleportPanel.axaml.cs:29`) is
  > `DataGridSortComparers.Ordinal(r => r.Group)`, which **despite its name IS `OrdinalIgnoreCase`**
  > (`DataGridSortComparers.cs:53`) -- the same comparison on the same field. A stable sort
  > reproduces the baseline, and so does a header that does nothing.
  > ▶ **What was run instead:** sort by **X first**, THEN click Group ascending. The order had to
  > *travel* from `E-five...` back to `B-two...`, which a dead header cannot do. That transition is
  > the actual evidence, not the final order.
  >
  > ⭐ **The dataset was designed so every state is identifiable from ROW 1 ALONE** (`B-two` /
  > `C-three` / `D-four` / `E-five` / `F-six`), so no reading depends on spotting a difference deep
  > in a six-row list. The rig asserts that no two predictions collide beyond the known
  > baseline/Group-asc pair.
  >
  > ⛔ **Staging is the whole job, and the shipped file cannot do it.** The live
  > `teleport-coords.dumpertest.json` had `group` **empty on all five rows** and `map` **identical on
  > all five** -- which is exactly *why* the 08-12 run left this half open. `Map` has **no row-editor
  > field at all**, so the JSON is the only way in; the rig keeps the original at
  > `.json.b16-original` and restores it.
  >
  > ⚠ **SCOPE CORRECTION -- this was never part of the B16 defect.** The fix's own comment
  > (`TeleportPanel.axaml.cs:22`) says *"Label/Group/Map worked"*: the five dead columns sorted on a
  > NESTED path (`Entry.X`) or a mismatched one (`Distance` vs `DistanceText`), while Group/Map bind
  > and sort the same direct path and were always live. So this row is a **completeness check on two
  > columns the fix says already worked**, not a test of the trimming defect. Worth the five minutes
  > -- wiring a custom comparer *replaced* Avalonia's default on these two -- but it must not be
  > recorded as "the B16 defect class is now fully verified".

- ✅ **Second launch raises the first window** (build 2610, B42) — **VERIFIED 2026-08-04 (maintainer).** Run `dist\UE5DumpUI.exe`, then run
  it again (double-click the exe, or the shortcut). **PASS** = the existing window comes to the
  front — including when it was minimized — and no second window appears. **FAIL** = nothing
  visibly happens, which is the old behaviour. Worth testing with the first instance **connected to
  a game**, since the window title carries the module name and a title-based search would miss
  exactly then.

- ✅ **Force submenu with nothing selected** (build 2610, B36) — **VERIFIED 2026-08-04 (maintainer).** Property Search → run a search →
  **right-click empty space below the rows**, or a row you have not left-clicked. **PASS** = no
  Force submenu. Left-click a BoolProperty row, right-click it: only Force ON / OFF. FAIL = all
  four actions at once. (Needs the Experimental toggle on for the submenu to exist at all.)

- ✅ **Close the game with a hold worker live** (build 2596, B14 + R5) — **VERIFIED build 2638**
  (DQ7R, bullet-time + See-through ON, closed from the game's own window: no event-log entry, no
  dump). Took THREE attempts and the first two failures are the whole lesson — see below.
  *Earlier:* **FAILED 2026-08-04 session logs (DQ7R / Elliot / CE), build 2622, SCOPE CORRECTED build 2628, needs a re-test.**
  DQ7R crashed at 21:05:06 on build 2622 (every fix present). The WER dump
  (`%LOCALAPPDATA%\CrashDumps\DQ7R-Win64-Shipping.exe.55564.dmp`) gives
  `0xC0000409` with **param[0] = 7 = FAST_FAIL_FATAL_APP_EXIT** — `abort()`/`std::terminate` —
  and the whole faulting stack inside `version.dll` + the CRT. **No `tick threw` line anywhere**,
  so no guard was even reached. Context: `pipe-0.log`'s last line is a `FindInstancesByClass`
  reporting `nonNull=35109` where the call 0.3 s earlier said `154964` — the game was freeing its
  object pool while we walked it.
  **The fix was right; its SCOPE was wrong.** The finding said "2 of 7 thread procs"; the DLL has
  ~15 places where a throw is fatal. Build 2628 adds `Routine::RunThreadGuarded` to all of them,
  the important one being `Stark::HookedProcessEvent` — it runs on the **game's own thread**,
  entered from game code with no handler for us, and allocates twice.
  **Re-test:** same steps below. **PASS** = no event-log entry. If it fires again, `init-0.log`
  now carries `UNCAUGHT exception … contained` naming the thread — that is what routing every
  entry point through one helper buys.
  *Note: the Elliot crash in the same event log is build **2567**, before B14 shipped — that one
  is the original bug, not a regression.*
  This is the exact repro that
  produced the live `0xC0000409` in build 2389, re-run against the loops that were still unguarded.
  **To test:** enable **two** holds whose workers were previously bare — Time Dilation (Hemmung) and
  Move Speed (Laufen) — plus See-through, then **disable See-through while the game is backgrounded**
  so its `PendingRestoreLoop` is actually waiting, and close the game from its own window.
  **PASS** = no crash, no WER minidump, nothing in the Windows Application event log. **FAIL** =
  exit code `0xc0000409` with a fault on a `version.dll` stack — that is an exception escaping a
  thread entry. If `init-0.log` carries `tick threw (…) — skipping (game tearing down?)`, the guard
  fired and did its job; its absence proves only that nothing threw this time.
  *Why it can't be tested here: the throw comes from reading a UFunction in a process that is
  actively freeing it — there is no way to stage that outside a real game shutdown.*

- ✅ **DONE 2026-08-18 `[ELLIOT-B5-2026-08-18]` — Provoke the concurrent `UE5_Init`** (build 2592, B5).
  Ran on **Elliot** launched through its deployed **`dxgi` proxy**, which is what makes the second
  caller reachable: `init-0.log` opens with
  `DllMain ProxyStart: proxy DLL mode — starting pipe server only (no scan)`, so the pipe is live
  with both cached pointers still 0 — the row's precondition, confirmed rather than assumed.

  **All four PASS conditions met, in one log window:**
  ```
  12:18:20.543 UE5_Init: Starting initialization...
  12:18:20.543 UE5_Init: init already in progress on another thread — tid=23592 is waiting (guard working, not an error)
  12:18:20.545 UE5_Init: init already in progress on another thread — tid=34088 is waiting (guard working, not an error)
  12:18:23.773 UE5_Init: Complete (UE504, GObjects=0x149BFC140, GNames=0x149B18600, Objects=85068)
  12:18:23.773 UE5_Init: tid=23592 resumed after waiting (first caller succeeded — returning its result, no second scan)
  12:18:23.774 UE5_Init: tid=34088 resumed after waiting (first caller succeeded — returning its result, no second scan)
  ```
  Exactly **one** `Starting initialization...`; both waiters named by tid; both resumed on the first
  caller's result with **no second scan**. And the callers themselves completed normally — all three
  CE threads returned `r=1` after **3234 ms**, i.e. they blocked for the scan and shared its result
  rather than erroring or re-scanning.

  ### ⚠ How this was made deterministic — the naive staging does NOT work
  Two earlier attempts on this same row failed to produce the handshake **even though the timing
  looked right**, and both failures are worth keeping:
  1. **UI Scan, then a CE call.** The CE call landed *after* the 3.3 s scan finished and returned in
     **16 ms** off the cached result. GUI round trips are 2–6 s; the window is 3.3 s.
  2. **A CE loop calling `UE5_Init` every 120 ms.** One Lua thread issues `executeCodeEx`
     **synchronously**, so call #1 simply blocked for the whole scan (`call 1: BLOCKED 3234 ms`) and
     calls 2–60 all ran afterwards, logging `Already initialized`. Sixty attempts, never two callers
     at once. *This still proved the FAIL condition absent — one `Starting` line across 61 calls —
     but it cannot produce the waiting handshake.*

  What works is **three `createThread` calls fired together**, so the second and third genuinely
  enter `UE5_Init` while the first holds it. **Concurrency had to be constructed; it does not arise
  from doing things quickly.**

  ℹ️ *The row describes the second caller as a CE mailbox command (`Mimic::EnsureInitialized`);
  here it is a direct `UE5_Init` from CE Lua. Same entry point, same guard — but the mailbox flavour
  specifically is still unexercised.*

  ### ✅ THE MAILBOX FLAVOUR IS CLOSED 2026-08-24 `[B5-MAILBOX-2026-08-24]` — `Mimic::EnsureInitialized` really is the second caller

  The note above ends *"the mailbox flavour specifically is still unexercised"*. It is exercised now,
  and **no Cheat Engine was involved** — which is also a correction to how the row was bucketed.

  ⭐ **NOT a CE row.** `mailbox_poke.py` drives the mailbox with `WriteProcessMemory` and nothing
  else, and `CommandRequiresInit` returns true for every command except `CMD_FOREGROUND`, so
  `CMD_QUERY_PTR` reaches `EnsureInitialized` with no CE anywhere. The classification listed this
  under *"Cheat Engine sitting"* and prescribed **3 CE `createThread`s** — that instrument is
  inherited from the DIRECT-export flavour and is the wrong one here: the mailbox is asynchronous by
  construction (the DLL's own poller thread dispatches it), so **one** poke inside the window is
  enough. Category A, not B.

  **Fixture:** `tools/verify/b5_mailbox_race.py` — a hardlinked copy of DumperTest Development at
  `D:\ZZProxyB5` with our real `version.dll` proxy dropped beside the exe (`DumperTest.exe` imports
  `VERSION.dll`, confirmed by reading its import table). Launching it gives the row's precondition
  for free, confirmed rather than assumed:

  ```
  DllMain ProxyStart: proxy DLL mode — starting pipe server only (no scan)
  PRE-RACE: initState=2 (READY)   objects=0   gobjects=0x0   gnames=0x0
  ```

  **All four PASS conditions, in one log window** (reproduced twice, 12:33 and 12:35):

  ```
  12:35:19.685 UE5_Init: Starting initialization...                      <- exactly ONE
  12:35:19.816 Mailbox: auto-initializing (UE5_Init)...                  <- EnsureInitialized, the 2nd caller
  12:35:19.816 UE5_Init: init already in progress on another thread — tid=39136 is waiting (guard working, not an error)
  12:35:21.253 UE5_Init: Complete (UE504, GObjects=0x7FF7DFDFAAA0, GNames=0x7FF7DFD0DD40, Objects=25212)
  12:35:21.254 UE5_Init: tid=39136 resumed after waiting (first caller succeeded — returning its result, no second scan)
  ```
  …and the mailbox command itself completed normally — `result=0` after **1443 ms**, i.e. it blocked
  for the scan and shared its result rather than erroring or re-scanning.

  ⚠⚠ **THE CONCURRENCY HAD TO BE CONSTRUCTED — the note above says so, and ignoring it cost two
  runs.** DumperTest's scan window is **1.57 s** (25,212 objects), less than half Elliot's 3.3 s at
  85,068. Spawning `mailbox_poke.py` as a PROCESS costs ~0.5–1.0 s of interpreter start **plus a
  nested spawn for `mailbox_addr`** — more than the whole window, and the first attempt duly landed
  after the scan and produced the documented no-lines-at-all shape. The fix is to resolve the mailbox
  address and open the process handle **before** anything starts, leaving only the
  `WriteProcessMemory` inside the window: the poke then began at **+140 ms** and blocked 1443 ms.

  ⚠ **`call_export.py` cannot be the first caller in proxy mode**, and this is a second rig
  limitation of the `[INJECTOWNER]` family: it looks for a module literally named `UE5Dumper.dll`,
  but in proxy mode our code **is** `VERSION.dll`, so it reports *"UE5Dumper.dll is not loaded --
  inject first"* on a process where our DLL is demonstrably running and serving a pipe. The pipe's
  `trigger_scan` was used instead, which is also closer to the row's own wording (*"connect the UI,
  click Scan, and while the scan is still running…"*). ⚠ `trigger_scan` returns **immediately**
  (`started: true`) and runs the scan on a worker — it is not itself the blocking call.

  ⛔ **A rig trap worth not re-paying: NEVER take a log byte-offset before a launch.** `<cat>-0.log`
  **rotates on process start**, so a pre-launch offset points into the *previous* run's file. If the
  new file is shorter the slice comes back empty; if it is longer the slice silently **drops this
  run's opening lines** — which is what happened here, reporting `0` "Starting initialization" lines
  while the raw log plainly had exactly one. The second shape is the dangerous one because it looks
  like a failed run rather than a broken reader. Mark **after** the process exists.

- ⬜ *(original instructions kept for the method)* **Provoke the concurrent `UE5_Init`** (build 2592, B5) — the active half of the passive check in
  ① above. Needs the **proxy** launch path, because that is what makes the second caller reachable:
  the proxy starts the pipe *without* scanning, so both cached pointers are 0 while the pipe is
  already live. **To test:** launch the game with a deployed proxy DLL, connect the UI, click Scan,
  and **while the scan is still running** trigger any CE-side mailbox command (tick the `.CT`, or a
  teleport hotkey) — that path calls `Mimic::EnsureInitialized`, which is the second `UE5_Init`.
  **PASS** = `init-0.log` shows `init already in progress on another thread — tid=… is waiting`
  followed by `resumed after waiting (first caller succeeded — returning its result, no second
  scan)`, exactly **one** `Starting initialization...`, and the CE command then works normally.
  **FAIL** = two `Starting` lines, or a `validated=yes` summary on a session where drill-down shows
  every property type unknown — that is the silent-corruption shape this fix exists to prevent.
  *Why it can't be tested here: it needs two real threads racing a multi-second scan inside a live
  game; the unit tests can only pin the flag semantics, not the timing.*

- ✅ **DONE 2026-08-18 — `.CT` DLL discovery survives a missing breadcrumb** (build 2576).
  Ran exactly as written: moved `%LOCALAPPDATA%\UE5CEDumper\dll-path.txt` aside, reloaded the `.CT`
  **from CE's Load Recent menu** (answering **No** to "save your last changes" — saying Yes would
  have written this session's test records into the repo's `scripts/UE5CEDumper.CT`), ticked `init`
  and then `Inject DLL + Start Pipe Server`.
  - **The discovery half PASSES.** With no breadcrumb file on disk, `UE5Dumper.dll` still loaded into
    `DumperTest.exe` and `\\.\pipe\UE5DumpBfx` came up. So the fallback chain reaches the DLL without
    the file the row is named after.
  - ⚠ **The `dll-path.txt` is recreated` clause is a MIS-SPECIFICATION — the `.CT` cannot do it.**
    `grep -c 'dll-path.txt", "w"' scripts/UE5CEDumper.CT` = **0**; the only occurrences are the path
    string and one `io.open(..., "r")`. The writer is the UI:
    `DumperDllPathStore.Record` (`Services/DumperDllPathStore.cs:80`, `File.WriteAllLines` at `:118`)
    with its single caller `App.axaml.cs:91`, i.e. **UI startup**. Verified end to end: the file was
    still absent after the successful `.CT` injection, and reappeared the moment the UI was
    restarted, containing `D:\Github\UE5CEDumper\dist`.
    ⇒ Rewrite the row's expectation as *"the DLL resolves without the breadcrumb; the breadcrumb
    returns on the UI's next start"*. **FAIL as written would have been reported against working
    code** — the same shape as working-lessons §2.4.
  - ⚠ Incidental: the rebuilt file has **one** entry where the original had two
    (`D:\tmp\UE5CEDumper_dist` is gone). `Record` seeds an MRU from the running exe's folder, so a
    deleted breadcrumb loses history rather than merging it. Harmless here, worth knowing before
    treating that file as a durable record.
  - ⛔ **The registry / recent-files half is STILL UNEXERCISED — a cheaper slot answered.** CE's
    console names the winner outright:
    ```
    [UE5Dump] DLL path: C:\Program Files\Cheat Engine\UE5Dumper.dll
    [UE5Dump] UE5CEDumper loaded as 'UE5Dumper.dll' but parked (initState=0) — restarting in place.
    ```
    So the chain resolved **CE's own install folder**, exactly the "only runs when every cheap slot
    misses" caveat this row already carried. Leave that half ⬜. See the finding below for what it
    resolved *to*.

### ⛔ NEW 2026-08-19 `[CACHEWIPE-DLL-2026-08-19]` — the DLL half of AC4/AC5 is still there (found while fixing the UI half)

> The C# side is fixed (audit L7, build 3262): a corrupt `UE5CEDumper.{Machine}.json` is now moved
> aside as `<name>.corrupt-<stamp>` before anything may write, and an Error names the file and the
> recovery step. **The DLL writes the same file and still does the old thing.** `Flamme.cpp:371`
> (and the identical `:519`, `:580`) parse with `allow_exceptions=false` and then
> `if (!root.is_object()) root = json::object();` — so a corrupt document is replaced in memory by
> an empty one and `WriteJsonAtomic` publishes a **one-game** file over it. Every other game's scan
> record, `ueVersionUserOverride`, `invokeTimeoutMs` and the DLL's own `versionDetectRev` stamp are
> gone, with nothing logged beyond the generic save line. It is the same defect, on the same file,
> in the process **more likely to hit it** (the game side writes on every scan).
>
> **Fix shape (small):** mirror the C# rule — on `is_discarded()` / not-an-object, rename the file
> to `<name>.corrupt-<stamp>` and `LOG_WARN` the path *before* building a fresh `root`; if the
> rename fails, **return without writing**. `Flamme` already has the pieces: `MakeTempPath` shows
> the naming idiom and `SweepOrphanTemps` shows the scoped, age-guarded directory walk. Keep the
> stamp format byte-identical to `AtomicFileHygiene.QuarantineNameFor` so one sweep can bound both
> sides' quarantine later. ⚠ No test target compiles `Flamme.cpp`, so the decision (`is this
> document a wipe candidate, and may I proceed without a quarantine?`) must go in `Flamme.h` beside
> `ShouldPublishAtomicWrite`, per the L4 precedent.
>
> Not folded into L7 because that batch is scoped to `ui/UE5DumpUI/Services/`, and a DLL change
> needs a `-Target DLL` build to mean anything.

### ⛔ NEW 2026-08-18 `[STALEDLL-2026-08-18]` — a 6-month-old `UE5Dumper.dll` sits in CE's install folder and the `.CT` will pick it

> **(b) FIXED 2026-08-19** — the `.CT` now reports the resolved DLL's SIZE beside its path, so a
> stale build no longer resolves silently; live-check under `[STALEDLL-2026-08-18]` in "Pending
> live-game verification". **(a) delete/refresh the stale file remains OPEN and maintainer-only.**

*Found only because deleting the breadcrumb for the B5 run pushed discovery one slot further down.*

| file | size | date |
|---|---|---|
| `C:\Program Files\Cheat Engine\UE5Dumper.dll` | **536,064 B** | **2026-02-19** |
| `D:\Github\UE5CEDumper\dist\UE5Dumper.dll` | 2,857,472 B | 2026-08-17 |

Different SHA-256, and a **5.3× size difference** — this is not a near-miss copy, it is a build from
six months and hundreds of builds ago, from before the mailbox contract moved.

**It did not actually load this time, and the reason matters.** A fresh `UE5Dumper.dll` was already
mapped into `DumperTest.exe` from the earlier injection, so the `.CT` took its *"already loaded but
parked — restarting in place"* branch. Proven, not assumed: `init-0.log` stamps **both** injections
(10:14 and 11:30) with `build=05a9af58-dirty`, and `git log 05a9af58` dates that commit **2026-08-17**
— the dist build. **Every result recorded this session therefore ran on the current DLL.**

**The hazard is a cleanly-launched game.** With no module already mapped and no `dll-path.txt` — the
state of any machine where the UI has not been run yet, which is precisely the state the breadcrumb
fallback exists to serve — the same resolution injects the **February** DLL. Symptoms would be a
contract-range refusal at best, and at worst the class of failure this session already saw twice
(`the contract symbol resolved to the wrong memory`), with nothing on screen naming a stale DLL as
the cause.

**Actions.** (a) **OPEN, maintainer-only:** delete or refresh
`C:\Program Files\Cheat Engine\UE5Dumper.dll` — machine-local, not something to do unattended.
(b) **DONE 2026-08-19:** the `.CT` now logs `DLL size: N bytes (X.X MB)` right after `DLL path:` (and
in the startup replay), via `ue5_dllSizeText`. The build stamp itself is not cheaply readable from the
`.CT` (not a C ABI export; DLL not injected yet at report time; CE Lua has no stat-by-path API), so
file SIZE is the honest signal that separates the ~0.5 MB Feb build from the ~2.7 MB current one.
Deferred idea: read the ACTUAL build stamp — would need a tiny data export (`g_buildNumber` /
`g_buildStamp`) or a `GetFileVersionInfo` PE-version-resource read; not worth a new export here.

### 🟡 FLAKY, not chased — `SnapshotViewModelTests.GroupMatch_MissingValue_ShowsErrorNoCandidates`

- **Flaky: `SnapshotViewModelTests.GroupMatch_MissingValue_ShowsErrorNoCandidates`** — failed ONCE
  in a full parallel run on 2026-07-23 (build 2318), then passed 25/25 three times in isolation and
  green on an immediate full re-run. Unrelated to the winmm/proxy work that was in flight. This test
  class has prior form for snapshot-DB concurrency flakes (see `feedback-ci-only-test-flakes`, and
  PR #451's concurrent-first-open fix), so the likeliest cause is another store-level race under
  parallel load rather than the assertion itself. **Not chased** — one observation is not a
  reproduction. If it recurs, capture whether `GroupCandidates` was non-empty or `GroupStatusText`
  empty, since those point at different halves. Effort **S** once reproducible.


### ⬜ Shipped + unit-tests-pass but unproven on real games — the long tail: Dump Explorer identity gate · Genau RIP decode b2544 · M1 / M2 / M3 / M4 / M5 · DLL LOW L1 / L5 / L8 / L10 / L12 · Solide L2 / L3 / L4 · V1a · NumericAll · V1c · b719 / b648 / b636 / b642 / b637 / b644 · FreezeOutcome
> ⚠ **`FreezeOutcome` is named here as a SECOND carrier on purpose.** `tools/check_live_verification.py` requires every `(key: X)` in roadmap.md to appear somewhere in this register, and until 2026-09-03 the only line carrying it was the `🟡 AA12 / AA13` heading — whose text reads *"the LYING is fixed and verified; STEP 5 CLOSED"*, i.e. it looks archivable. Archiving it would have turned `main` red with an error about a renamed heading, pointing nowhere near the cause. Two carriers means an archival pass can move either one safely.

*Every ID this heading names is a live check that lives in the bullets below and nowhere else. The
heading exists because this list spent months parented to whatever `###` happened to precede it —
most recently `[STALEDLL-2026-08-18]` — so a heading-level scan of the register found none of them.*

✅ **The `M1`–`M5` ID COLLISION is RESOLVED 2026-08-19 — `M`-numbers now mean exactly one thing.**
Until today two families shared these letters: **audit #3**'s Schlacht/Tot/shutdown-race fixes (here)
and **audit #5 D4a**'s map/set-stride findings. A register addressed by heading-level grep cannot
carry colliding IDs — sooner or later one family's close gets recorded against the other — so the
container-geometry family was **renamed `M1/M2/M3` → `MG1/MG2/MG3`** ("Macht geometry"):

| was | now | what it is |
|---|---|---|
| `M1` (D4a) | **`MG1`** | `ComputeSetElementStride` drops the `TPair`'s trailing padding (`Macht.h:314`) |
| `M2` (D4a) | **`MG2`** | `ReadTSparseArray` reads `NumFreeIndices` at `+0x3C` not `+0x34` (`Macht.h:293`) |
| `M3` (D4a) | **`MG3`** | `ComputeMapValueOffset` guesses alignment for struct values (`Macht.h:332`) |

**`M1`–`M5` therefore mean audit #3 and nothing else**, and `MG1`–`MG3` mean the container geometry.
**Why that family and not this one** — the choice was measured, not preferred: audit #3's IDs are
cited **4 times in `dev-log.md`** (append-only, must never dangle) and **23 times in `docs/archive/`**
(rewriting dated evidence would falsify history), plus 33 in-source `dll/src` comments and the five
`### M1`…`### M5` anchor headings in [audit-2026-07-14-findings.md](audit-2026-07-14-findings.md).
The D4a family has **zero** dev-log and **zero** archive references. `MG` is two letters + digits, so
it still matches `check_audit_register.py`'s `ROW_RE` (`[A-Z]{1,2}\d+`) and the three rows stay in the
register — a three-letter prefix would have silently dropped them.

✅ **The code-comment residual is CLOSED 2026-08-19 — all 9 sites renamed.** `141e8119` was scoped
docs-only, leaving 9 comments still saying `M1`/`M2`/`M3` for the D4a family:
`dll/tests/dll_helpers_test.cpp:3228,3255`, `tools/ue-sample/…/DumperTestActor.h` (6),
`tools/ue-sample/…/DumperTestTypes.h:57`. All now read `MG1`/`MG2`/`MG3`. The three families that
legitimately share those letters were left **untouched** and verified so by grep: audit #3's Schlacht
comments (`Schlacht.cpp:589,633,643,658`, `Dunste.cpp:677`, `Fern.cpp:1215`, `Frieren.cpp:612` — they
KEPT their IDs, so renaming them would re-create the collision in the opposite direction), the
statistics term in `Linie.cpp:31` (Welford's running `M2`), and the `Map="M2"` test data in
`CoordCsvCodecTests.cs:346` / `CoordLuaParserTests.cs:81`. `A2` on `DumperTestActor.h:139,164` is
audit #5 **D3**/Aura's, which was never renamed, so it stands.

- **`b648` — GameThreadDispatch hook validation on two more engines** (moved here from the 繁中

  ### ✅ **PASS 2026-08-24** `[B648-TWOENGINES-2026-08-24]` — both engines, DLL 3350

  | title | UE | objects | the line |
  |---|---|---|---|
  | EVERSPACE 2 | **505** (5.5) | 600,268 | `validation OK -- hook fired **2358** times in 1500ms` |
  | The Artisan of Glimmith (Geri) | **427** (4.27) | 24,226 | `validation OK -- hook fired **1170** times in 1500ms` |

  Both in `proxy:version.dll` mode, one game at a time. ⭐ **A second, independent witness in the
  same run**: `pe_profile_start` then `pe_profile_get` attributed the firings to **3 distinct
  UFunctions** on each title, so the hook is genuinely DISPATCHING and not merely "installed" -- the
  count in the log line and the profiler's attribution are computed by different code.

  ⚠⚠ **BOTH TITLES WOULD HAVE MEASURED A THREE-WEEK-OLD DLL.** ES2's deployed proxy answered the
  pipe at build **3337** while `dist` was 3350, and a proxy owns the pipe, so the fresh injection was
  a no-op -- `pipe_client`'s trap 1, caught by `assert_build()`. Geri's was stale too. Both were
  refreshed and the games relaunched before any number below was taken.
  ⚠ The stale proxy was **byte-size-identical** to the current one (2,898,432 B both), so a size
  comparison would have called it current. Only the hash caught it.
  checklist 2026-08-24, because it is **not human-only**). Do one instance invoke on **ES2**
  (UE5.5) and one on **Geri** (UE4.27). PASS = the log carries
  `GameThreadDispatch: validation OK — hook fired N times`, and instance invokes that previously
  timed out at `-5` now succeed. That is a **log grep**, not a judgement.
  ⚠ **The blocker the 繁中 file recorded was wrong**: it said the named title was not granted.
  Both are granted at full tier and both were injected and swept on this machine on 2026-08-23,
  so nothing environmental is in the way. Lower-priority extras: a UE 4.18–4.24 title (smaller
  vtable / lower slot) and a heavily-modified publisher fork.
- **Dump Explorer cross-game identity gate** (build 2538+; UI/C#-only, no DLL or pipe change).
  The live match joins on bare class NAMES, and every UE title has `Object` / `Actor` / `Pawn` /
  `PlayerController`, so loading game A's `.jsonl` against game B did not fail — it "succeeded",
  marked those rows **in current game**, and Jump opened B's object under A's label. Now two-tier:
  different `module` → refuse and name both sides; same module + different `pe_hash` → still match
  (a pre-patch dump of this game is the normal use) but say "Different build — offsets may have
  moved"; missing `pe_hash` → match but never claim identity was checked. Identity is read at match
  time via `GetPointersAsync`, deliberately NOT fanned into the VM — `SetConnected(true)` can fire
  before an `EngineState` exists, and that window is the wrong-game bleed in C2 above.
  **What offline already settled — do not spend live time on it:** all four arms plus the
  probe-throws path (`DumpExplorerTests` ×5, both directions), and the refusal was verified to FAIL
  when the module comparison is neutered.
  **What ONLY a real game can prove:** that `EngineState.ModuleName` and the dump's `meta.module`
  actually agree on the SAME game — they come from different producers (live DLL vs
  `DumpAllService` at export time), and if one carries a path or different casing the gate would
  refuse a legitimate same-game match. Acceptance: (1) export a Dump All from game X, keep X
  connected, Re-check → matches with NO caveat; (2) load that file with game Y connected → refused,
  status names X and Y, every row unmatched, Jump offers nothing; (3) load an OLD dump of X after
  an X patch → matches WITH the "Different build" caveat. Case (1) is the regression risk — a false
  refusal there breaks the feature for its main use. **No log marker** for the pass; the refusal
  logs `DumpExplorer live match refused: dump module '…' != live module '…'`.
  🟡 **Case (1) has evidence (2026-08-05, DQ7R).** The maintainer loaded a **different session's dump
  of the same game** and it matched; `DumpExplorer live match refused` appears **zero** times across
  every DQ7R log. That is the regression risk retired — `EngineState.ModuleName` and the dump's
  `meta.module` do agree on the same game despite coming from different producers. **Cases (2) and
  (3) are still ⬜**: (2) load that dump with a *different* game connected → must refuse and name both
  sides; (3) load a pre-patch dump of the same game → must match **with** the "Different build" caveat.

  ### CLOSED 2026-08-24 -- case (2) was already closed; case (3) was closed today `[DUMPGATE-C3-2026-08-24]`

  **Case (2) -- CLOSED 2026-08-17, uncredited here until today.** `[GRP4-UI-2026-08-17]`
  (todo.md:8517) dumped from DumperTest **Development** and loaded it with **Shipping** connected
  (24,445 objects vs 25,179 -- a genuinely different binary). The refusal **named both modules**, the
  *In current game* list was **empty**, and all 82,385 rows read *"Not checked yet"* rather than *"Not
  in current game"*. That is this bullet's case (2) end to end.

  **Case (3) -- CLOSED 2026-08-24.** DumperTest Development, UE504 / 25,212 objects, AOT-trimmed
  `dist\UE5DumpUI.exe` 54.7 MiB build 3343. Rig: `tools/verify/dumpgate_case3.py`.

  | dump loaded | status line, verbatim off the screen |
  |---|---|
  | the export itself (**control**) | `Live match: 3,947 class(es) in the current game (82,665 of 82,665 rows matched).` |
  | same file, `pe_hash` flipped **one hex digit** | **`Different build of the same game -- offsets may have moved. `**`Live match: 3,947 class(es) in the current game (82,665 of 82,665 rows matched).` |

  The two loads differ by **one character of input**, in one session against one game, so the gate is
  shown *choosing between branches* rather than merely rendering a literal. It still matched (3,947
  classes, 82,665 of 82,665, *Not in current game -- 0*), which is the deliberate design: `pe_hash` is
  per-build, so refusing here would reject a dump the user took of this very game last week.

  ⛔ **"Needs an actual DQ7R patch ... opportunistic, not schedulable" WAS WRONG, and the gate's own
  source says why.** `DumpExplorerViewModel.cs:396-403` is three **plain string comparisons** over
  `meta.module` and `meta.pe_hash` as read from the dump's first line; nothing re-hashes the running
  exe at match time. Flipping one hex digit in that line manufactures "a different build" exactly as
  far as the gate can tell -- a real patch would be a slower way to produce the same two strings. Cost
  was about a minute, against a wait of months.

  ⚠ **THE TRAP THAT WOULD HAVE FAKED THIS PASS, disarmed BEFORE the run rather than after.** The
  tier-2 branch picks this caveat only when **both** hashes are non-empty and differ; if **either**
  side is empty it falls through to *"Build identity unknown (no pe_hash) -- matched on module name
  only."* Both are amber caveats in the same label, so a run that never checked the live hash can
  photograph the **wrong branch** and file it as a pass. So the live hash was read from the pipe
  first: `pe_hash 6A8AA8DF10F1F000`, len 16, non-empty -- and the dump's meta line carried the
  **identical** value, which is also the case-(1) premise (the two producers -- live DLL
  `Fern.cpp:1322` vs `DumpAllService.cs:372` -- do agree) confirmed rather than assumed.

  ℹ️ Neither case's *refusal* text was re-read today; case (2)'s was read on screen on 08-17, and
  case (3) never refuses by design.

- **Solide pool-truncation badge — `⚠ capped` / "cap reached, more exist unheld"** (build 2531+;
  DLL `Solide`/`Fern` + Property Search + Teleport Stealth card). `Aura` already computed
  `rset.truncated` and `Solide` was dropping it, so "0 live instances matched" and "matched more
  than `SOLIDE_MAX_INSTANCES`=256 and discarded the rest" were indistinguishable. Now plumbed to
  both `force_field` and `get_forced_fields`, and the Stealth card **withdraws** its
  "you are minimal to detection" claim when the pool was capped (that claim is false for every
  instance past the cap).
  **What offline already settled — do not spend live time on it:** the wire parse both ways incl.
  the older-DLL missing-key default (`SolideTruncationWireTests`, 4 tests), both VM messages in
  both directions (`PropertySearchForceTests` ×3, `TeleportViewModelTests` ×1), and the prune-guard
  swap being an exact no-op (`!rset.truncated` ≡ the old size test on this path, since
  `FindInstancesByClass` is called with the default `buildHistogram=false`). All 8 were verified to
  FAIL when the implementation is reverted — three separate negative controls.
  **What ONLY a real game can prove:** that the flag ever fires. It needs a class with **>256 live
  instances** where a Force hold is meaningful — projectiles, crowd NPCs, destructible props are the
  likely candidates; most gameplay classes never reach the cap, which is exactly why this went
  unnoticed. Acceptance: hold a field on such a class → the strip row shows `⚠ capped` next to
  `(256 held)` and the status line ends "cap reached, more exist unheld"; hold on a small class →
  neither appears. **No grep-able log marker** — the DLL logs nothing on truncation; the evidence is
  the badge and the status text. Secondary check: with the pool capped, `RemoveForce` must still
  restore cleanly (the base-prune guard is skipped while truncated — L4), so verify no field is left
  stuck at the forced value after Reset.
  ### ✅ CLOSED 2026-08-24 — already verified THREE times, none of which reached this bullet

  Not a run, a bookkeeping correction. The badge has been observed on screen three separate times:

  | closure | tag | what it showed |
  |---|---|---|
  | **2026-08-22** ⭐ decisive | `[SOLIDEHOLD-2026-08-22]` (todo.md:5828) | DumperTest, dist 3314 — the positive case **and its negative control on one screen** |
  | 2026-08-20 | `[DQ7R-CAP-2026-08-20]` (todo.md:8904) | DQ7R, a real commercial title |
  | 2026-08-23 | `[SOLIDE-L3L4-2026-08-23]` (todo.md:1518) | closed alongside L3 + L4 |

  The 08-22 evidence is what the acceptance clause above asks for, verbatim and in one refresh
  (todo.md:5839) — including the "hold on a small class -> neither appears" half, which is the part
  a broken build passes:

  ```
  X ActorComponent . bIsEditorOnly      (256 held)  WARN capped
  X Actor          . bIsEditorOnlyActor  (58 held)
  ```

  Fixture chosen by measurement rather than guess: `ActorComponent::bIsEditorOnly` (+221 inheritors,
  519 instances -> over cap) against `Actor::bIsEditorOnlyActor` (58 instances -> under cap). The
  bullet's own worry — "most gameplay classes never reach the cap, which is exactly why this went
  unnoticed" — was solved by picking a base class with many inheritors instead of hunting for
  projectiles.

  Discriminating, and unusually well for this register: each way the feature can break produces a
  visibly different screen. The pre-fix defect this row exists for (Solide drops `rset.truncated`)
  leaves the ActorComponent row with no badge; a flag stuck true adds one to the Actor row too. Both
  rows rendered simultaneously is what excludes both.

  ⚠ **Two slivers deliberately NOT claimed** (recorded rather than reopened): the status-line clause
  "cap reached, more exist unheld" has never been read on screen — `[SOLIDEHOLD-2026-08-22]` says so
  itself at todo.md:5846 — and the secondary `RemoveForce`-while-capped check was closed under L4,
  not here.

- **Copy CE Field drills object-pointer arrays — leaf + GWorld-path spine + dup-crumb dedup — DONE +
  MERGED (PR #323, builds 1364-1379).** LEAF (`SpawnedAttributes[2]` → `CharacterAttributeSet` →
  `HealthPoint`), SPINE 2b (`PathStepToBreadcrumbs` splits a Locate-in-GWorld `PlayerArray[0]` hop into
  container + element), and DEDUP 2c (`DedupeConsecutiveBreadcrumbs` collapses a redundant consecutive
  container crumb in `ExportCeFieldXmlAsync` + `CleanBreadcrumbs`) all **LIVE-VERIFIED on Elliot AND the
  deeply-nested Gundam SEED chain** (nested + Collapse-chain). Unit-tested
  (`...ObjectArray_WithResolvedElement_DrillsElementGroup`, `...PathThroughObjectArrayElement_EmitsElementDerefNode`,
  `DedupeConsecutiveBreadcrumbs_*`, `..._DeepDistinctChain_Unchanged`). **(b) DONE + LIVE-VERIFIED
  (builds 1380-1388) — Back-nav onto a path-synthetic container crumb now re-hydrates the array element view.** The
  crumb's `ContainerField` is null (the `GWorldPathStep` carries no `ArrayDataAddr`/`ArrayCount`/element
  list), so Back-nav fell through to a parent re-walk and rendered the PARENT object grid (a silent
  mis-render — NOT a literal duplicate; the 2c dedup already covers the export-time crumb). "Give it a
  `ContainerField`" is infeasible (path step lacks the data) → `TryRepopulateSyntheticContainerAsync`
  LAZILY re-walks the parent + matches the field by name+offset + `RepopulateContainerView`, wired into
  all 4 re-display sites (NavigateToBreadcrumb, GoBack normal + pre-bookmark restore, LoadBookmark) +
  `RefreshAsync`'s container gate broadened. 7 new tests; C# 1648/0, AOT 46.5 MB. **(a) DONE +
  LIVE-VERIFIED (builds 1389-1390) — Map/Set (and interface-array) element hops in a GWorld-path spine
  now split into container + element crumbs.** The DLL `emit()` lambda was widened 6→8 args to thread `elemStride`
  (Map `pairStride` / Set `elemStride` / interface-array 16) + `elemValueOffset` (Map value's within-pair
  offset; 0 for set/key/interface) through `GraphEdge`/`GraphPathStep` → Fern `elem_stride`/`elem_value_offset`
  → C# `GWorldPathStep` → `PathStepToBreadcrumbs` (element crumb offset = `ElementIndex*stride + valueOffset`;
  container crumb strips the `.Key`/`.Value` suffix so Back-nav re-hydration matches). All emit callers
  updated (`GetRelatedObjects`/`AppendOwnedSubObjectLeaves`/test mock); object/class arrays keep the
  hardcoded-8 path. 6 new tests (5 C# + 1 dll round-trip); C++ 697/0, C# 1653/0, AOT 46.5 MB. Adversarial
  review confirmed Map/Set/Set offsets correct + reachable; accepted nits: struct-nested dotted base name
  doesn't re-hydrate (pre-existing, affects arrays too, CE math still correct) + int32 element-offset
  arithmetic (theoretical, `FieldOffset` is int by design).
- **Genau RIP decode: `Macht::IsRipRelativeModRM` (mod=00 half restored at 3 of 5 sites)**
  (build 2544+; DLL only). Three hand-rolled decode loops tested `(b & 0x07) == 0x05` and
  omitted the `mod == 00` half, so `mov rcx,[rbp-8]` / `lea rax,[rbp+0x20]` / `mov rax,rbp`
  were decoded as RIP-relative and the int32 read at `instr+3` was a disp8 plus the next
  instruction's bytes. All five sites now share one named predicate.
  **What offline already settled — do not spend live time on it:** the predicate itself
  (13 assertions incl. an exhaustive "exactly 8 of 256 ModR/M bytes qualify", verified to
  FAIL — 6 reds — when reverted to the r/m-only form). Also settled: this is **NOT** a
  wrong-answer bug at `ScanFunctionBodyForRipRef`, whose every caller is a GNames path gated
  by `ValidateGNamesAny` (it must decode the literal string `"None"` through a two-level
  pointer chain). Treat it as a correctness + scan-cost cleanup, not a fix.
  **What ONLY a real game can prove, and `sweep.sh` CANNOT:** `scan_patterns.java:137` skips
  every `Symbol*`/`CallFollow` signature (`GROUND-TRUTH.md` says so), and the two data scans
  are runtime-only and absent from the pattern harness — **a clean sweep diff here would mean
  "not measured", not "no regression".** The only evidence is the DLL's own scan log, same
  game, before vs after: the candidate/probe counts should go DOWN while **every resolved
  GObjects / GNames / GWorld address stays byte-identical**. The second half is the real
  acceptance criterion; a changed address is a regression, a lower count is the win.
  Passive — needs no special in-game action, just one injection each side.
  ✅ **VERIFIED 2026-08-19 `[GENAURIP-AB-2026-08-19]` — BOTH halves, on a non-game host.**
  Rig: `tools/verify/genau_rip_ab.py run notepad++`. Both DLLs were built **in the same
  session, from the same tree, by the same toolset**, differing ONLY in the two-line
  predicate — *not* "dist vs a checkout of build 2544", which would differ in ~700 builds of
  unrelated ways. Hint entry deleted before each side (a warm cache changes how many patterns
  are attempted — the quantity being compared).
  - **The win — candidate count went DOWN, deterministically.**
    `DataScanGObjectsCandidates: Found ` **4085** ` static pointers` (pre-fix) →
    **4083** (post-fix). **Reproduced exactly on 4 independent runs**, so −2 is signal, not
    variance. ⚠ The neighbouring `(N validation failures were suppressed)` counter is NOT
    stable run-to-run (3621/2777 across runs — it depends on live heap contents); its delta
    was a consistent −1, but **do not quote the absolute number as evidence**.
  - **The acceptance criterion — the resolved address did not move.** `GWorld` resolved to
    `0x7FF7480A03C8 (aob)` on **every one of the 4 runs, both sides**. Directly comparable
    because the host's module range was byte-identical across runs
    (`code=[0x7FF747B31000-0x7FF747F7837C]`), i.e. it was not rebased — checked rather than
    assumed, since the row warns that ASLR normally makes raw addresses meaningless.
  - ⚠ **THE HOST IS THE WHOLE EXPERIMENT, and the first choice was wrong.** All five call
    sites are RECOVERY paths (`DataScanGObjectsCandidates`, `FindGObjectsStaticStruct`,
    `ResolveSymbolExport`, `FindGNamesByStringRef` ×2), so **on a healthy game the AOB wins
    immediately and not one of them runs** — a game yields two identical logs for the worst
    possible reason. A `python.exe` sleeper fails every AOB and so drives all five, but
    **measured a flat null**: python.exe is a launcher stub whose main module has a code
    section of **0xE4C = 3,660 bytes** (the real code is in `python312.dll`, not the main
    module), so both sides returned an identical "Found 17 static pointers". That null is
    **manufactured by the host and is indistinguishable in the log from "the fix changed
    nothing"**. Notepad++ (~8.5 MB) is ~2,300× the code and is what produced the signal.
  - **Still not covered**: `GObjects`/`GNames` do not resolve on a non-UE host, so
    "addresses unchanged" is demonstrated for **GWorld only**. A UE title whose GObjects or
    GNames AOB *fails* (so recovery actually runs) would close that; DumperTest cannot,
    because all three resolve by AOB on the first pattern.

- **Audit #3 DLL fixes — M1–M5 + the DLL/Solide LOWs** ([audit-2026-07-14-findings.md](audit-2026-07-14-findings.md)).
  Shipped on `dev` (`408fd2d`, `7f3898f`, `3362636`); this section is their SINGLE owner — the audit
  doc and the Audit-#3 block above point here rather than each asserting a status of their own.
  Every one is a **race or a lifecycle-ordering fix**, which is precisely the class a unit test
  cannot reach: the bug needs a real game thread, a real disconnect, and real timing.
  - **M1 / M2 / M3 — Schlacht restore-set** (disable↔Tick race repopulating `hiddenActors`; disable
    while the game thread is stalled discarding the restore set; no un-hide on disconnect/shutdown).
    Acceptance: enable See-Through, then (a) toggle off during motion, (b) toggle off while the game
    is paused/stalled, (c) yank the UI connection and (d) close the game — in **all four** every
    hidden actor must become visible again. A single actor left invisible is the failure, and it is
    only visible on screen.

    ### ✅ **arms (a) (b) (c) PASS 2026-08-24** `[M123-RESTORESET-2026-08-24]` — headless; (d) is unobservable

    `py tools/verify/seethrough_restoreset.py`, DumperTest dev, DLL 3349.

    | arm | disturbance | captured actor's own `bHidden` after |
    |---|---|---|
    | **(a)** | disable issued **mid-motion** (the disable<->Tick race) | `false` |
    | **(b)** | disable issued while the **game thread is SUSPENDED** | `false` |
    | **(c)** | the socket **yanked** (abrupt handle close, not `__exit__`) | `false` |
    | (d) | close the game | ⛔ see below |

    ⭐ **"Only visible on screen" is out of date, and that is what makes this headless.**
    `seethrough_get_state` returns `hidden_actors` — the **addresses**, not just the tally — so each
    actor's own `AActor::bHidden` bit (**+88, mask 0x80**, resolved at runtime) is read straight out
    of the process with `ReadProcessMemory`. That is an independent witness: not the hider's count,
    not anything the DLL computed for the answer.

    ⭐ **The address set is captured BEFORE the disable, deliberately.** The worker re-picks
    occluders every tick, so *"`hidden_actors` is empty afterwards"* is worthless — an empty list is
    exactly what a worker that merely stopped choosing produces, un-hidden or not. The rig pins the
    actors that were hidden at the moment of the disable and re-reads **those**.

    ⭐ **Every arm carries its own negative control**: after the positive control, the rig waits
    2 s doing nothing and requires the bit to be **still set**. Without it, "the bit is clear" is
    equally well explained by the hide simply lapsing.

    ⚠ **Each arm runs on a FRESH GAME, and that was found the hard way.** A fresh DumperTest hides
    an occluder within a second, but after arm (a)'s 6x600-unit move **nothing is hideable any more**
    — the camera faces open space. Arms (b) and (c) duly reported *"nothing was ever hidden"* and
    failed for want of a SUBJECT, which reads exactly like a defect and is really a spent fixture.
    ⚠ **And recalling a saved marker does NOT fix it**: `teleport_save_marker` then
    `teleport_recall_marker` returns `code 0, tier 1` — a clean success — and the view is **still**
    not hideable. The marker restores where the pawn stands, not what the camera looks at. Only a
    relaunch is a state reset that actually works here.

    ⛔ **Arm (d) is NOT RUNNABLE, and not for want of scheduling.** Once the process exits there is
    no memory to read and *"is this actor visible"* has no referent. A rig that reported a pass there
    would be asserting something unobservable. Recorded as structurally impossible rather than left
    looking un-run.
  - ✅ **M4 — PASS 2026-08-23 `[M4-TOTZOMBIE-2026-08-23]`.** `tools/verify/m4_tot_latch_zombie.py`,
    DumperTest dev / DLL 3337, 60 live `DumperTestHolder`.
    | step | observed |
    |---|---|
    | hold applied | `force_field(numeric, 4242)` → **held=60**, `truncated=false`, 8/8 sampled read 4242 |
    | ⭐ detector proven FIRST | poke `-1` while healthy → **restored in 0.4 s** |
    | disconnect | `_f.close()` — an **abrupt** handle close, not `__exit__`; a clean teardown is the path that already works, and the latch is set by the monitor noticing a dropped socket |
    | reconnect, listed? | ✅ `('DumperTestHolder', 'HolderValue', 60)` |
    | ⭐⭐ **zombie check** | poke `-1` again → **restored in 0.4 s** — the worker is still re-asserting |
    The last row is the whole row: a zombie **lists** but stops re-asserting, so
    `get_forced_fields` alone cannot tell the two apart. And the detector was established
    *before* the disconnect, so "the value came back" cannot be confused with "nothing ever
    changed it".
  - ~~**M4 — Tot latch zombifying a Solide hold** during the disconnect window.~~ Acceptance: start a
    force-field hold, disconnect the UI mid-hold, reconnect → `get_forced_fields` must still list the
    hold AND the value must still be held (a zombie job lists but stops re-asserting, so checking the
    list alone is not enough — read the value in CE). ⬜
  - ✅ **M5 — `UE5_Shutdown` worker-join ordering — PASS 2026-08-24 `[M5-JOINORDER-2026-08-24]`.**
    `tools/verify/m5_shutdown_join.py` (control / run / baseline), DumperTest dev, DLL 3345.
    Hold active (`Actor.bCanBeDamaged`, **held=60**), **two** live pipe connections (the UI holds
    two of `kMaxPipeInstances=3`), closed with a **posted `WM_CLOSE`** — never `taskkill /F`, which
    skips the DLL's shutdown path entirely and makes the test vacuous.
    **No hang and no minidump in any of 5 closes.**

    ⚠⚠ **The acceptance said "evidence is the ABSENCE of a hang", so the detector had to be
    shown able to REPORT one — and on the first attempt it silently could not.** `control` suspends
    the game thread and posts `WM_CLOSE`: the process must then NOT exit. Attempt 1 reported a clean
    exit in 2.188 s, i.e. **a control that failed to arm** — `main_tid` was computed as `min(tid)`
    and suspend.py's header line reads *"DumperTest.exe (50348): **141 threads** — EARLIEST CREATED
    FIRST"*, so the parser returned **141**, the thread COUNT, and suspended nothing. Reading the row
    suspend.py already labels (`tid=30576 … <-- main thread (UE game thread)`) gives the real control:
    no exit in 5.0 s **and `IsHungAppWindow` False -> True**, so the hang is witnessed by the OS
    rather than inferred from a timeout. Only then does the clean `run` mean anything.

    ⭐ **The "sub-second exit" clause is WRONG and is corrected here, not quietly passed.** The
    first real run measured **1.970 s** — a FAIL against the wording. But UE tears down rendering,
    audio and the engine on any close, and none of that is ours. The only number that can accuse the
    DLL is the hold-vs-no-hold delta on the same host, so `baseline` was added and both were measured
    twice, alternating, each on a fresh process:

    | | exit time |
    |---|---|
    | baseline (no hold) | 1.717 s · 1.650 s |
    | hold active (held=60) | 1.612 s · 1.334 s |

    **A hold makes shutdown no slower — marginally faster, i.e. inside noise.** So ~1.5–1.7 s is
    UE's own teardown cost and the DLL contributes nothing measurable. Read the acceptance as *"no
    worse than the no-hold baseline"*; a literal sub-second reading would have filed a UE property as
    an M5 defect.

    ℹ️ **What this does NOT claim:** the ordering defect needs a mutator arriving inside a window of
    a few microseconds, and nothing here forces that. What is shown is that the ordinary
    shutdown-with-a-hold-active path is clean, repeatably, with the pipe still connected.
  - ✅ **DLL LOW L5 — PASS 2026-08-23 `[L5-CADENCE-2026-08-23]`.** Welford gap underflow.
    `tools/verify/linie_cadence_gap.py`, DumperTest dev / DLL 3337, 10 s window at 60 FPS.
    ⭐ **A real before/after, not an assertion.** The rig's own docstring records the PRE-fix
    measurement (2026-08-22, build 3309): the two twice-per-frame `CameraModifier::BlueprintModify*`
    rows read **ratio 1.99x with `gap_samples = count/2`**. This run reads **1.00x with
    `gap_samples = 1199 = count-1`** on the same two rows, same game, same rig. The four
    once-per-frame functions were 1.00x in both runs — a **free built-in control** that rules out a
    clock or window-measurement artifact, because no clock error can move exactly the two doubled
    rows. The guard is now `nowMs >= s.lastMs` ([Linie.cpp:46](dll/src/Linie.cpp:46)) with the
    reorder case (`nowMs < s.lastMs`, the unsigned-underflow input) still excluded, so the
    ~1.8e19-gap hazard L5 was filed for cannot occur.
    ⚠ Two commits, do not conflate: `7f3898ff` (the L1/L5/L8/L10/L12 batch) introduced the guard as
    `>`, and `06f01d27` `[CADENCEGAP-2026-08-22]` corrected it to `>=` after the `>` form was found
    to drop every same-millisecond sample. This row verifies the end state of both.
  - ✅ **DLL LOW L12 — PASS 2026-08-23 `[L12-STRLEAK-2026-08-23]`.** Fern `str_params` malloc leak on
    a mid-loop JSON `type_error`. `tools/verify/l12_strparams_leak.py`, DumperTest dev / DLL 3337.
    2000 invokes of `str_params = [ {16 KB string}, {"text": 12345} ]` — the good element allocates
    ~32 KB and is pushed to `strAllocs`, then `sp.value("text","")` throws `type_error.302`
    **after** it, which is the only input shape that can leak anything.
    Predicted if unfixed: **~63 MB**. Observed: **+0.9 MB** (idle drift is ~0.2 MB/read).
    ⭐ **Anti-vacuity, and it is the load-bearing check**: **2000 of 2000** requests returned an
    error reply, so the throw fired every time and the leak path was entered on every iteration. A
    silent success would have exercised the ordinary free-at-the-bottom path and proved nothing.
    ⚠ **The first control was too weak and the rig correctly FAILED itself.** Spawning 300 actors
    moved private bytes **+0.0 MB** — not because the probe is blind, but because ~600 KB is under
    the noise floor of a 3 GB process. Recalibrated to 3000 actors (**+3.3 MB**), which establishes
    the probe's resolution an order of magnitude below the 63 MB effect under test. Private bytes
    come from psapi `PROCESS_MEMORY_COUNTERS_EX.PrivateUsage` via ctypes — **psutil is not installed
    on this machine**, so the classification doc's "psutil private bytes" entry is not runnable as
    written.
  - ✅ **DLL LOW L8 — PASS 2026-08-23 `[L8-NOPUMP-2026-08-23]`.** Grausam's `GetWindowTextW`
    under `g_mutex`. `tools/verify/l8_fglock_nopump.py`, DumperTest dev / DLL 3337.
    **Offline half, on the shipped binary and stronger than reading source:** `GetWindowTextW`
    does not appear in `dist/UE5Dumper.dll`'s import table at all, while `SetWindowLongPtrW` —
    the subclassing call two lines away in the same function — does, so the detector fires. The
    only source occurrence is the explanatory comment ([Grausam.cpp:175](dll/src/Grausam.cpp:175)).
    **Live half:** freeze ONLY the UE game thread, then enable the lock so `SubclassEnumProc` runs
    over a window whose thread is not pumping → **0.260 s, `state=1`** (budget 3 s).
    ⚠⚠ **Three separate ways this test was vacuous before it was right — all found, none assumed.**
    (1) The rig sent `enabled=True`; the DLL reads **`enable`** ([Fern.cpp](dll/src/Fern.cpp)), so
    the unknown key defaulted to **false** and every call disabled an already-disabled lock. The
    reply still said `ok: true`. Caught only because the DLL log showed `set OFF` twice and no
    `set ON`. The rig now asserts the reply's `state == 1`.
    (2) A **re-enable skips the body**: [Grausam.cpp:167](dll/src/Grausam.cpp:167) is
    `if (::GetPropW(hwnd, kOrigProcProp)) return TRUE; // already subclassed by us`. So the
    enable must be the **first ever in that process** — which needs a fresh launch, and the rig
    refuses to run if `get_foreground_lock` is not 0. Proof the body ran: the `Subclassed window`
    log line count rises.
    (3) ⭐ **`game_thread_stalled == False` is NOT evidence of a healthy thread.**
    `Stark::IsGameThreadResponsive` opens with `if (!s_hookActive) return true;` — with no
    ProcessEvent hook installed it reports responsive **unconditionally**. The rig froze the
    correct thread (identified by CPU: 4906 ms vs 15 ms for its 141 siblings) and still read
    False. It now calls `pe_profile_start` (which forces the hook via
    `UE5_EnsureGameThreadHook`) and requires False→**True**→False around the freeze.
  - ✅ **DLL LOW L1 — PASS 2026-08-23 `[L1-GODRACE-2026-08-23]`.** Solitar worker start/stop under
    `s_workerMutex`. `tools/verify/l1_godmode_race.py`, DumperTest dev / DLL 3337.
    **1600 toggles across 2 pipe lanes in 2.8 s, with 1585 ON↔OFF transitions** — a serialised
    run would produce exactly **one**, so the lanes genuinely overlapped.
    Both of L1's failure modes checked **behaviourally**, because `get_god_mode` reports intent
    and cannot see whether the worker thread exists: poke `bCanBeDamaged` the wrong way and see
    whether anything restores it.
    | | |
    |---|---|
    | detector, god ON | poked bit **restored in 0.4 s** |
    | detector, god OFF | poke **stayed** for the full 4 s — so the detector separates the two states |
    | settled ON (no premature join) | restored ✅ |
    | settled OFF (no orphan) | not restored ✅ |
    | second, independent detector | `re-assert worker started` **794** / `stopped` **794**, net **0** |
    ⚠⚠ **Three defects in the rig itself, each of which would have produced a confident wrong
    answer.** They are recorded because two are traps in the *logging convention*, not in this row.
    (1) The first anti-vacuity metric counted `GodMode: set <ON|OFF> … (want=<0|1>)` lines where
    the argument and `want` disagree. That is **structurally always zero post-fix** — the fix holds
    `s_workerMutex` across the store *and* the log line, so nothing can be between them. A metric
    the fix makes impossible cannot measure concurrency. Replaced by counting ON↔OFF transitions
    with each lane sending a **constant** value.
    (2) ⭐ **`sorted(LOGDIR.glob("*.log"))` + slice-by-total-length reads a PREVIOUS session.**
    The folder keeps every archive, and name-sorting puts the live `walk-0.log` **before**
    `walk-2026…`, so the slice landed inside an archive and yielded a dead pawn address — four
    `read_mem returned nothing` results that read as "the worker is broken". Now snapshots
    per-file offsets over `*-0.log` only.
    (3) The `'<name>' @+0xNN mask=0xMM` line is emitted **only on the first class scan**, not on
    every toggle, so it is absent from a fresh OFF→ON window. The pawn address must come from the
    fresh window (it changes per process) but the bit layout must come from the whole live log.
  - ✅ **DLL LOW L10 — BOTH HALVES CLOSED.** Re-subclass half 2026-08-24
    `[L10-RESUBCLASS-2026-08-24]`, teardown half 2026-08-23 `[L10-TEARDOWN-2026-08-23]` below.

    **The re-subclass half needed no game.** *"Destroy and recreate the game window with a
    fullscreen toggle"* is a GAME PROCEDURE; the claim is Grausam's own state machine plus four
    ordinary Win32 calls over REAL HWNDs — a cached `std::atomic<HWND>`, the predicate
    `if (!gw || !::IsWindow(gw))`, a non-blocking `try_lock` re-subclass, and a `GetPropW`
    double-subclass guard. Nothing about Unreal, a GPU or a swapchain is asserted, and a
    fullscreen toggle is merely one way to make `IsWindow()` go false; `DestroyWindow` is another
    and it is the one a test can drive deterministically.

    New target **`grausam_window_test`** (`dll/tests/`, 22 checks): `#include`s `Grausam.cpp` to
    reach its anonymous namespace, creates real windows, and calls `HookedGetForegroundWindow`
    **directly with no MinHook hook installed** — so user32 is never patched in the test process.
    MinHook is still *linked* (`SetForegroundLock` references `MH_*` at external linkage, 17 call
    sites); `Sein::Info`/`Error` are satisfied by two stub definitions rather than by compiling
    `Sein.cpp` and dragging in the whole logging stack.

    ⭐ **The arming precondition is satisfied by CONSTRUCTION, not by luck.** The hook returns
    early when the real foreground window belongs to this process, so a test whose own window
    happened to be foreground would never reach the re-find and would pass asserting nothing.
    `g_origGetForegroundWindow` is pointed at a stub returning `nullptr`, so the path is always
    reached and the result does not depend on what else is on the desktop.

    ⚠ **Three negative controls, each isolating one mechanism:**

    | armed by | result |
    |---|---|
    | predicate → `if (!gw)` (no longer notices a dead window) | **3** failures, all in the re-find case |
    | double-subclass guard removed | `baseline: a second sweep does not re-save the proc` fails **by name**, then the process **crashes** — which is exactly the corruption `Grausam.cpp`'s own comment predicts (`SubclassProc` saved as "the original" → every message recurses) |
    | `case WM_ACTIVATEAPP: w = TRUE` removed | **exactly 2** failures, both about the rewrite |

    ⭐⭐ **The concurrency case is the leg a live session could NEVER observe**: four threads
    hammering the hook with `g_gameWindow` repeatedly nulled, asserting the saved prop never
    becomes `&SubclassProc`. That is the actual claim behind the `try_lock`, and no amount of
    toggling fullscreen can arrange it.

    ⚠⚠ **A trap worth more than the row: the FIRST run of these controls was VACUOUS.** Built by a
    bare `cmake --build` in a DevShell, the new object landed at **`#deps 0`** — Ninja recorded
    zero header deps because the console codepage did not match the `msvc_deps_prefix` CMake
    baked in — so editing `Grausam.cpp` did **not** rebuild it, and NC-1 silently re-ran the OLD
    binary and "passed". Caught by hashing the exe before and after each build. It is the same
    trap CLAUDE.md documents for `.h` edits, and it applies to an `#include`d `.cpp` identically.
    The target is now built through `build.ps1` (deps: **4**, with `../dll/src/Grausam.cpp` listed).

    ℹ️ The test is **unbuffered** (`setvbuf(stdout, nullptr, _IONBF, 0)`) because it can genuinely
    crash — see NC-2 — and `build.ps1:305` records the same "produced ZERO output" shape biting
    `dll_helpers_test` under CI.

  - 🟡 **teardown half PASS 2026-08-23 `[L10-TEARDOWN-2026-08-23]`.** DumperTest dev / DLL 3337,
    via `tools/verify/call_export.py`.
    ```
    20:19:13.383  [Grausam] Foreground lock ENABLED (fg-window=0x81029A)
    20:19:15.467  UE5_Shutdown: Cleaning up...
    20:19:15.473  [Grausam] Foreground lock DISABLED
    ```
    ⭐ **The ENABLED line is what makes this non-vacuous.** `Foreground lock DISABLED` sits in
    the unconditional soft-disable branch ([Grausam.cpp:271](dll/src/Grausam.cpp:271)), so on its
    own it does not show anything was turned off. Here the reply carried `state=1` **and** the log
    recorded the enable 2 s earlier, so the lock was demonstrably on when shutdown reached it.
    ⬜ **Still owed:** the *"re-subclasses on the GFW hook's rare window re-find"* half needs the
    game window destroyed and recreated (the fullscreen-toggle case), which is not reachable
    headlessly.
  - ✅ **M5 — `UE5_Shutdown` worker-join ordering — PASS 2026-08-23 `[M5-SHUTDOWN-2026-08-23]`.**
    Same sequence. With a GodMode hold **active** (`state=1`, `re-assert worker started` logged),
    `UE5_Shutdown` **returned in 0.173 s** (thread exit code 0), `re-assert worker stopped` was
    logged inside the shutdown window, the process stayed alive, and no new crash dump appeared.
    ⚠⚠ **The row's stated acceptance — "close the game while the UI is still connected" — CANNOT
    exercise this fix, and that is why it kept not getting run.** Closing a game never calls
    `UE5_Shutdown` (the same fact B8's block records), so the whole worker-join ordering under
    test is skipped. Substituting an explicit `UE5_Shutdown` is therefore a **fidelity
    improvement, not a shortcut** — it is the CE-Disable path, i.e. the only path that reaches
    the code.
    ⚠ **B8's block claims "zero `UE5_Shutdown: Cleaning up` lines in any session" — that is
    STALE.** 6 files in the log corpus contain it (grep control: `PipeServer` hits 334 files, so
    the search works). The path is real and reachable; it just is not reachable from a window
    close.
    ℹ️ **New reusable tool:** `tools/verify/call_export.py` calls any `UE5Dumper.dll` C-ABI export
    inside the injected game via `CreateRemoteThread`, resolving the RVA by loading our own DLL
    locally with `DONT_RESOLVE_DLL_REFERENCES` (no DllMain, no side effects). It bounds the wait
    and reports a timeout as a timeout, because a hang is not a test result.

    Welford gap underflow on out-of-order PE timestamps; Grausam `GetWindowTextW` under `g_mutex`
    hanging the pipe thread; Grausam post-enable windows + shutdown teardown; Fern `str_params`
    malloc leak on a mid-loop JSON `type_error`). L8 and L12 are the ones with a user-visible
    symptom (pipe stall / leak under repeated failed invokes). ⬜
  - ✅ **Solide LOW L2 — PASS 2026-08-21 `[SOLIDE-L2-2026-08-21]`.** `object_null` on
    `Actor::ParentComponent` (a `TWeakObjectPtr`, verified `WeakObjectProperty` @0x01C0) returns
    **`code=-12` (`FR_ERR_WEAK_PTR`), `held=0`, `resolved=false`**, persists **no** job, and starts
    **no** worker. `tools/verify/solide_l2_weakptr.py`, DumperTest / dist 3308.
    ⭐ **The reply is only a third of the claim** — a job can be absent from `get_forced_fields` and
    still have started a re-assert worker, which `get_forced_fields` structurally cannot show. So
    the rig reproduces the **PRE-FIX shape first**, on purpose: forcing a field that does not exist
    is accepted (`code=0 held=0`), persists a job, and drives `FindInstancesDerivedFrom base='Actor'`
    at **3.43/s** forever. Only after watching that counter move does its silence mean anything.
    ⚠ **The first version of this rig reported a FAIL against correct code**, asserting "zero new
    scan lines". The refusal legitimately performs **exactly one** `FindInstancesDerivedFrom`, 6 ms
    after the request, because it must resolve an instance to read the field's TYPE before it can
    refuse. The honest discriminator is the SUSTAINED rate: **0.00/s over 4 s** against the
    control's 3.43/s. L3 and L4 are untouched — annotate this bullet, do not tick it.
  - **Solide LOWs L3 / L4** (substring class + fuzzy field
    match tightened; per-instance restore bases instead of one representative). L4's prune guard was
    touched again in build 2531 — see the Solide pool-truncation entry below, verify them together. ⬜

- ✅ **Value Search `TSet<T>` / `TMap<K,V>` scan (key: V1a)** — **VERIFIED 2026-08-05 (DumperTest,
  build 2650), ⬜ since build 927.** Scanning `4242` returned `DumperTestActor.Set_Int[1]`
  (IntProperty, Reflected, offset `0x358`) on both the live actor and the CDO; scanning `222`
  returned `DumperTestActor.Map_NameToInt.Value[1]` at `0x3A8`. Both render with the element index,
  which is what the row format promised. The sparse-walk geometry hands back the slots we expect.
  *Not yet exercised: container reallocation between scans (the degrade-don't-lie case).*
- ✅ **Value Search `TOptional<T>` scan (key: V1c)** — **VERIFIED 2026-08-05, ⬜ since build 942.**
  `24680` returned `DumperTestActor.Opt_Int_Set` (IntProperty, `0x468`), and — the criterion that
  actually matters because it is negative — **a scan for `0` did NOT surface `Opt_Int_Unset`**, so
  the `bIsSet` gate holds and an unset optional is not being read as a zero.
- ✅ **Value Search `NumericAll` (byte families included)** — **VERIFIED 2026-08-05, ⬜ since build
  796.** `-5` (Int8Property) and `255` (ByteProperty) both returned results with NumericAll
  selected. *The remaining half is a UX judgement, not a defect: whether the result volume for a
  1-byte value is usable. The panel's own orange warning says it will flood, and this sample cannot
  settle "usable" — that needs a real game's object count.*
- **Value Search `TSet<T>` / `TMap<K,V>` scan — original instructions** (build 927). Scan a known value held
  in a `TSet<int>` / `TMap<K,int>` UPROPERTY → rows must render as `Set[idx]` / `Map.Key[idx]` /
  `Map.Value[idx]`, and a Next Scan must prune. The sparse-walk geometry
  (`Ubel::GetSetElementStride` / `GetMapPairLayout`) is shared with the container-aware Address
  Finder and unit-tested; what is NOT provable offline is that live sets/maps hand back the slots
  we expect. Specifically watch a **container reallocation between scans** — element addresses are
  raw, so refine degrades exactly like `TArray` (the SEH-safe read drops the candidate); confirm it
  degrades rather than reporting a wrong hit. ⬜ unverified.
- **Value Search `NumericAll` (byte families included) (key: NumericAll)** (build 796-797). Select
  NumericAll and scan a value that genuinely lives in an `Int8Property` / `ByteProperty` → confirm
  the byte field is found, and that the orange result-volume warning
  (`ValueSearchViewModel.DataTypeWarning`) appears. `BuildNumericTargets`' range gating is
  unit-tested (`300` → no Int8/UInt8; `-5` → Int8 yes / UInt8 no); the live question is whether the
  result volume for a small value (0/1/255) is *usable* or drowns the panel — that is a UX
  judgement no test can make. ⬜ unverified.
- **Value Search `TOptional<T>` scan (key: V1c)** (build 942). Scan a known value held in a
  `TOptional<int/float/FString>` UPROPERTY → confirm the row appears under the optional's
  field name and a Next Scan prunes; confirm an **unset** optional doesn't surface on a
  scan for `0` (the `bIsSet` gate). Layout helper is unit-tested; the field walk needs a
  live game with optional UPROPERTYs.
- **Property freeze (Route B)** on a respawning-NPC game (build 719). Watch: tick FPS
  impact (50ms × N instances), rescan cadence at respawn, vtable-liveness guard on level
  transition, AOBMaker gating UX, multi-script coexistence. First candidate: Geri (UE
  4.27).
- **Build-648 ProcessEvent fix** re-verify on ES2 (UE 5.5) + Geri (UE 4.27): look for
  `GameThreadDispatch: validation OK — hook fired N times`; previously-`-5`-timing-out
  instance invokes should now succeed. Lower-priority extras: a UE 4.18-4.24 game (smaller
  vtable / lower slot) + a heavily-modified publisher fork.
- ✅ **Static-native PE fast path** (build 636) — **BOTH halves now closed.** The "by accident"
  half closed 2026-08-23 `[B636-NOACCIDENT-2026-08-23]` offline; **the latency half closed
  2026-08-24 `[B636-FASTPATH-2026-08-24]`**, DumperTest dev, DLL **3349**,
  `py tools/verify/b636_latency.py`.

  ⭐ **A latency number was never the test, and that is why the 08-23 attempt was right to stop.**
  With a healthy game thread both routes are fast, so the measurement cannot tell a real bypass
  from a queue that happens to drain quickly. The discriminating experiment **suspends the game
  thread** and repeats:

  | | thread running | thread **SUSPENDED** |
  |---|---|---|
  | static-native `KismetMathLibrary::Sqrt` | 3.0 ms, returns 4.0 | **2.8 ms, returns 4.0** |
  | queued `DumperTestActor::V8_RemoveOneTableRow` | 58.1 ms | **TIMEOUT at 6.0 s** |

  A fast path that secretly queued would time out in the top-right cell. The bottom-right cell is
  what proves the suspend actually bit — without it the top row is unfalsifiable.

  ⭐ **The ambiguity that killed the last attempt is gone, and the raw bytes show it.** The buffer
  is pre-filled with `0xAA` and the input written over it; the reply reads

  ```
  00000000 00003040 | 00000000 00001040 | aaaaaaaa aaaaaaaa
     16.0 (input)         4.0 (return)      pre-fill, untouched
  ```

  Three independent ambiguities are excluded at once: it is **not `0xAA`** (so the slot really was
  written), **not `16.0`** (so it is not the input mirror that made `Abs(-3.5)` unpublishable), and
  **not `0`** (so a legitimately-zero return cannot be confused with a call that never ran). The
  surviving `0xAA` tail is what proves the pre-fill happened at all.

  ℹ️ Fixture chosen for exactly that: `Sqrt(16.0) = 4.0` — a result that is neither zero, nor the
  input, nor a value the buffer could hold by accident.

  ⭐ **"Don't fall into the fast path by accident" cannot happen, and the proof needs no game.**
  `direct_call` is **caller-supplied and defaults to false** — `Fern.cpp:5290`
  `bool directCall = request.value("direct_call", false);`, with the comment *"Caller is
  responsible for asserting safety."* Nothing in the DLL infers it. On the client side there is
  exactly **one** site in the whole UI that ever sets it:
  `PointerPanelViewModel.cs:1777` `directCall: true`, hardcoded to **`className: "KismetMathLibrary"`**
  in the self-test — a class that is static-native by definition, chosen explicitly rather than by
  a heuristic. `DumpService.cs:2953` states the rest: *"Default false preserves the existing
  behavior for LiveWalker's Pipe Invoke."* So a stateful UFunction has no route into the fast path.

  ⬜ **Latency half — attempted 2026-08-23 and deliberately NOT reported.** Two measurement
  attempts were discarded rather than published:
  1. The first benchmark passed `addr=` to `invoke_function`, which wants `instance_addr` /
     `class_name`. **Every one of the 80 timed calls was an error reply**, so the "0.223 ms vs
     0.184 ms" it produced was the latency of a *rejection*. Caught only because the return value
     was checked — the timings alone looked entirely plausible. (Same parameter-name class of bug
     that invalidated an L8 result the same day: `enabled` vs `enable`.)
  2. With the call shape fixed, `Abs(-3.5)` returned `ok:true, result:0` and a buffer of
     `-3.5, 0, -3.5` — the return slot **mirrors the input**, i.e. the function never executed.
     That is exactly the failure `PointerPanelViewModel`'s own docstring warns about: *"was
     indistinguishable from a call that ran and wrote nothing — the return slot is untouched
     either way."*

  **What a future attempt needs:** a KismetMathLibrary function whose return is *verified* to
  change the buffer (derive the return offset by finding the expected value, as attempt 2 tried),
  and only then time `direct_call` true vs false. Do not report a number without that check.
- ✅ **FPROPERTY_FLAGS offset fix** (build 642) — **PASS 2026-08-21 `[B642-RETFLAGS-2026-08-21]`,
  and the acceptance was RE-SCOPED, deliberately.**

  ⚠ **The original wording is not runnable as written**: "sweep the 12+ tested games' Class
  Structure Return columns". `ClassStructPanel.axaml` contains the string "Return" **zero** times —
  it has no Functions section and no Return column at all. The only Return column in the app is
  `LiveWalkerPanel.axaml:890`. A tester following this row would have gone looking for a control
  that does not exist and either given up or, worse, "checked" the wrong grid.

  Re-scoped to the wire, where the claim actually lives: `py tools/verify/b642_ret_flags.py`.

  | host | functions checked | skipped by the sanity gate | with out-params | violations |
  |---|---|---|---|---|
  | DumperTest **Shipping** | 3,708 | 184 (4.7 %) | 2,825 | **0** |
  | DumperTest **Development** | 3,721 | 185 (4.7 %) | 2,852 | **0** |

  ⭐ **The design is TWO INDEPENDENT READS.** `ret_offset` comes from `UFunction::ReturnValueOffset`;
  `params[].ret` comes from the `CPF_ReturnParm` bit off `PropertyFlags`. They must agree on every
  function, and a disagreement needs no reference build to detect. **7,429 functions, zero
  disagreements.**

  ⚠ **This is a cross-check, NOT a controlled experiment, and the row must say so.** No pre-fix
  binary exists to run here, so it was never shown able to fail on this machine. What can be said
  precisely: a pre-fix DLL, which never set the per-param flag, would violate assertion (b) on
  every one of the 2,393 non-void functions.

  ⭐ **Non-vacuity is measured, not assumed.** Both branches ran (2,393 non-void / 1,315 void), and
  **1,904 of the non-void returns sit at a NON-ZERO offset** — so the `offset == ret_offset`
  comparison is doing real work rather than trivially matching zero on everything.

  ⚠ Two guards exist because each would otherwise turn a broken probe into a clean sweep: a
  function with `num_parms==0 && parms_size==0 && ret_offset==0xFFFF` is SKIPPED, because that is
  also exactly what a failed `funcFlagsOff` probe looks like (4.7 % here — a high rate would be the
  finding); and zero out-params across a host is FLAGGED, because `CPF_OutParm` (0x100) is in the
  same low 32 bits (2,825 here, so it does not fire).

  ⚠ `get_offsets` is recorded for PROVENANCE only (`use_fproperty=true, flags=56, elemsize=52`).
  **Do not assert `flags == elemsize + 4`** — that holds by construction in all three writers, so
  it can never fail and would be a tautology wearing a check's clothes.

  ⚠⚠ **The rig's FIRST run reported 100+ confident violations that were all its own bug**:
  `int(p.get("offset") or -1)` turns a legitimate offset of **0** into −1, and offset 0 is the
  commonest case there is (every no-arg getter returns at 0). Python's falsy zero, in a comparison
  whose whole job is to compare offsets. Fixed and re-run.

  ▶ **The 繁中 mirror's row 4 carries the same unrunnable wording** and has been corrected there too.
- **Verify Return Value diagnostic** (build 637/644) -- **CLOSED 2026-08-24**
  `[B637-RETWIDTH-2026-08-24]`. The row asked for two things; one was already done and the
  other could not fail as written, so it was replaced.

  **FString-return "see After: dump above" hint -- CLOSED by citation.**
  `[L11-7A-UI-2026-08-21]` (todo.md:3677) already tested BOTH emit branches with a control:
  two functions from the same dialog, same defaults, same Verify tick, differing only in
  whether the return ends inside the dump window -- `see After: dump above` **absent** for
  `ComposeTransforms` and **present** for `MakeTransform` (table at todo.md:3688). The live
  half was read in CE on 08-20: `-> ReturnValue (fstruct@172, size=12B) -- complex return;
  see After: dump above` with `_DUMP_LEN = 184` = 172 + 12 exactly (todo.md:3921).
  `StrProperty` and `StructProperty` both resolve through `IsComplexReturnType` to the same
  literal, so the FString spelling adds no new path.

  ⚠⚠ **The pointer half as written CANNOT FAIL, so it was NOT run.** "The line shows a `0x`
  prefix" is a test of a **compile-time string literal** (`BakedScriptGenerator.cs:463`)
  that C# unit tests already pin -- they would go red long before a human reached CE. And it
  is **blind to the defect build 637 actually fixed**, which is a READ WIDTH, not a prefix:
  `readUFunctionReturn` has no `'pointer'` type, so the pre-fix spelling fell through to the
  signed-int32 default and read **4 bytes of an 8-byte pointer slot**. A `0x` prefix appears
  either way.

  ⭐ **What was run instead: `scripts/tests/return_read_test.lua`** -- the REAL
  `readUFunctionReturn` out of `scripts/ue5_invoke_helper.lua`, driven against
  **byte-accurate** stub memory, comparing the post-fix `'qword'` against the pre-fix
  fall-through on the *same bytes*. **15 checks, 0 failed**, no CE and no game:

  | case | `'qword'` (fix) | fall-through (pre-fix) |
  |---|---|---|
  | high pointer `0x7FF762E5AAA0` | `0x7FF762E5AAA0` | **`0x62E5AAA0`** -- a different address |
  | low dword's top bit set | exact | **NEGATIVE** (`-1562006880`) |
  | low pointer `0x400000` | `0x400000` | `0x400000` -- **agree** |
  | zero | 0 | 0 -- **agree** |

  ⭐ **Shown able to fail, which is the only reason the 15 greens mean anything.** Disabling
  the helper's `qword` branch (the pre-b637 behaviour) turns it red: **4 failures, exit 1**,
  printing exactly the historical symptom `0x62E5AAA0` instead of `0x7FF762E5AAA0`. Reverting
  returns 15/15. Note rows 3 and 4 keep PASSING while armed -- that is the rig documenting its
  own blind spot: **a low or zero pointer proves nothing**, which is presumably how the defect
  survived to build 637.

  ℹ️ **Why the memory model had to be rewritten rather than reused.** `invoke_helper_test.lua`
  keeps `I32` and `U64` in **separate** stores keyed by address. Correct for what it tests, but
  here it would have made the control a tautology -- a 4-byte read would return `nil` or an
  unrelated planted value instead of the **low half of the 8-byte value actually in memory**, so
  "the pre-fix path truncates" would be *modelled* rather than *measured*. `MEM` holds bytes and
  every reader assembles from them.

  The full chain is confirmed end to end: `ObjectProperty` -> `MapToHelperType`
  (`BakedScriptGenerator.cs:526-529`) -> `"pointer"` -> `readType = "qword"` (`:331`) ->
  `readUFunctionReturn` `'qword'` branch -> `readQword` -> 8 bytes -> `0x%X`.

  ### ⚠⚠ NEW DEFECT FOUND WHILE CLOSING THIS -- `[RETINT64-2026-08-24]` (MED)

  **The b637 fix special-cased only `"pointer"`, and `"int64"` has the identical bug, live
  today.** `readType = displayType == "pointer" ? "qword" : displayType` rewrites *only*
  `"pointer"`, so `"int64"` reaches the helper **verbatim** -- and `readUFunctionReturn` has
  **no `'int64'` branch** (its chain is float / double / bool / byte / uint64|qword / int16 /
  word|uint16 / uint32|dword / else-int32-SIGNED). So it falls through to the signed 4-byte
  default, exactly like the pre-b637 `'pointer'` spelling.

  Measured, case 5 of the rig: an 8-byte value `0x0000000123456789` reads back as
  **`591751049`** (`0x23456789`) instead of **`4886718345`**.

  Reached from `MapToHelperType` by **two** routes (`BakedScriptGenerator.cs:519,522`):
  `Int64Property` -> `"int64"`, and the size-8 signed-int case -> `"int64"`.

  ### ✅ **FIXED 2026-08-24** — `'int64'` joins the 8-byte branch

  `scripts/ue5_invoke_helper.lua`: `elseif valueType == 'uint64' or valueType == 'qword' or
  valueType == 'int64' then`, plus the `@param` list. One line of behaviour.

  ⭐ **The signedness worry recorded above turned out to be a NON-issue, and the reason is worth
  keeping.** `readQword` is indeed unsigned, but no sign folding is needed — **Lua integers are
  64-bit two's complement**, so the eight bytes CE returns *are* the signed value. Measured before
  writing the fix rather than assumed:

  | bytes | assembled in Lua 5.4 |
  |---|---|
  | `FF FF FF FF FF FF FF FF` | **-1** (`math.type` = integer) |
  | `89 67 45 23 01 00 00 00` | 4886718345 |
  | `00 00 00 00 00 00 00 80` | -9223372036854775808 |

  Contrast the 32-bit case, where CE widens 4 bytes into a *positive* Lua number and the `signed`
  flag genuinely changes the answer (that is AA20). At 64 bits `'int64'` and `'uint64'` read the
  **same bits**; only the caller's format specifier (`%d` vs `0x%X`) differs. ▶ So the warning above
  was right to demand a decision and wrong about which way it goes — a "fix" that widened the read
  and then re-applied an unsigned fold would have been the actual bug.

  **Tests** — `scripts/tests/return_read_test.lua` case 5, now 18 checks: the 8-byte read, agreement
  with `qword` on the same bytes, `-1` from all-`FF`, and the 4-byte default still truncating as the
  standing regression witness. **Shown able to fail**: removing `'int64'` from the branch turns 3 of
  them red; restoring it returns 18/18.
  ⚠ **`-1` is a BAD discriminator and the suite says so** — it passes even when armed, because a
  4-byte signed read of `FF FF FF FF` is *also* `-1`. The value that discriminates is one needing
  more than 32 bits. Same shape as the low-pointer case in this file's §3.

  ⛔ **A LUA-ONLY FIX DOES NOT SHIP UNTIL THE UI IS REPUBLISHED.** `scripts/ue5_invoke_helper.lua`
  is an **`EmbeddedResource`** of the UI (`UE5DumpUI.csproj:146-149`, logical name
  `UE5DumpUI.Resources.CE.ue5_invoke_helper.lua`), served to *Tools -> Export / Inject CE Helper Lua*
  through `GetManifestResourceStream`. It is **not** copied into `dist\` as a loose file, so editing
  `scripts/` alone changes nothing a user can run. Republished with `-Mode Publish` for this fix.

  ℹ️ **Not reachable from the local fixture**: `tools/ue-sample` has `int64 I64` as a UPROPERTY but
  **no int64-returning UFUNCTION**, so there is no end-to-end DumperTest repro. The C1 spawner draft's
  `int64 Spawn_LastRecycledAddr() const` would be the first one. The defect is nonetheless proven at
  the layer it lives in, by the Lua suite above.
- **`walk_functions_batch` follow-up** — Effort: **S**. Sister to `walk_class_batch`;
  DumpAll still does `WalkFunctions` single-call per class. Same byte-equivalence safety
  net. **Skip unless profiling shows it as the new bottleneck.**

-----


-----

## Verification steps migrated from the 繁中 checklist (2026-08-22)

These are the operational `做什麼 | 預期` tables for items that **do not need a human**:
Auto + computer-use can drive them end to end (UI clicking, the pipe, log greps, offline
tools). They lived in [`pending-verification_zh-TW.md`](pending-verification_zh-TW.md), whose
charter is *only what a human must verify* — carrying them there had turned that file into a
second copy of this register.

⚠ **Moved VERBATIM, including the ✅/🟡 status cells**, so no evidence was lost in the move.
Where a step is already marked done, it is done — this is not a fresh queue.

⭐ **These are still open verification work**; they are tracked by the item ids that already
appear elsewhere in this file. What changed is only where the STEPS live.

### ✅ AF16–AF23 step 2 — Xref half CLOSED 2026-08-23 `[AF16-XREF-2026-08-23]`; the fixture was FOUND, not guessed

The Props half passed on 2026-08-22. What remained was a field that **two or more Blueprint bytecode
functions touch**, so the Xref dialog returns ≥ 2 rows and its headers can be sorted. Five earlier
hunts picked fields by intuition and every one returned 0 or 1.

⭐⭐ **The fix was to stop guessing.** `tools/verify/af16_xref_fixture.py` inverts the mapping the DLL
already exposes — `walk_function_props` over every script-backed UFunction, then
`prop_addr → {functions}` — so any property with ≥ 2 **is** a fixture by construction. On DQ7R
(AOT `dist` v1.0.0.3315, DLL 3315, UE427, 149,408 objects, proxy `version.dll` already deployed):

```
functions: 5955 total, 916 script-backed (non-native)
probed 916; 705 took the exact bytecode path; 661 distinct instance props
properties touched by >= 2 functions: 58
```

**Two fixtures were used, because the first is homogeneous and the second discriminates.**

| # | fixture | rows | why it was needed |
|---|---|---|---|
| 1 | `DOLLActionSecondCheck :: CasterGameCharacter` @`0x340` | **26** | volume + a `Function` column with 26 distinct values |
| 2 | `MapStateBase :: StateData` @`0x250` | **9** | `Kind` has 2 values, `Re` varies (1/3/5/9), `Owner Class` has 2 — the only way to see a **reorder** at all |

⚠ Note both are keyed to the **declaring** class, not the Blueprint: the rig names
`BP_ActionSecondCheck_C`, Property Search lists `DOLLActionSecondCheck (+1 inheritor)`. Searching for
the BP class name finds nothing — this is the same inherited-field rule CLAUDE.md records for Solide.

**All six headers — `Kind | Re | Access | Owner Class | Event | Function` — sort.** Results:

* **`Function`** (26 rows): ascending is alphabetical on the full path, beginning `BurstPoint120_Up_F`;
  descending begins `ZonePoint_T` — a clean reversal.
* **`Re`** (9 rows): ascending `1,3,3,3,3,5,5,5,9`; descending `9,5,5,5,3,3,3,3,1`. Exact reversal.
* **`Kind`**: groups `instance` first, then all `ref`.
* **`Owner Class`**: groups `BP_MapManager_C` (×8) before `BP_MapStateLoading_C`.
* **`Access`, `Event`**: clickable, indicator moves, no crash — but constant/empty in both fixtures.

⭐ **No cell-recycling corruption across every reordering.** Each row's `Kind`/`Re`/`Owner Class`
stayed glued to its own `Function`: `MapChangeCutScene` kept `Re=9` in all four orderings,
`MapChangeField`/`Rura`/`Title` kept `5`, and `FactoryNextMapState` kept `1` **and** its unique
`instance` + `BP_MapStateLoading_C` pair. That mismatch is exactly what the `supportsRecycling` defect
produced, so this is the check that matters.

⭐ **Incidental and worth keeping: the sort is STABLE.** Sorting by `Kind` (2 values) left the eight
`ref` rows in their original scan order rather than scrambling them.

⚠ **Honest limit, same as the Props half's:** `Re` here is `1/3/5/9` — all single digit, where a string
sort and a numeric sort agree. So the headers demonstrably reorder, but **numeric-vs-string is still
not discriminated** on this dialog. It needs a field with a >=10 reference count; the rig can find one
(`--top`) if that is ever worth a session. `Access` was `read` throughout, so its ordering is untested.

### (original steps) AF16–AF23 —— DataGrid 欄位標題排序（**必須用 AOT 版**）

*優先度 **中** · ⚠ **一定要用 `build.ps1 -Mode Publish` 出來的 trimmed 版**。這個問題在一般 dev build
上不會出現，用 dev build 測等於沒測。*

⚠ **這一項還多一個必測點（2026-08-21 新增）：排序完不可以出現「兩列顯示同一筆資料」。**
維護者回報過這個畫面：Props 對話框標題連點幾次之後，兩列都顯示同一筆，但標題還是寫
「2 properties」。成因是 cell template 的 `supportsRecycling`，已修（17 處）。

> ⭐ **步驟 2 的宿主找到了：DQ7R**（`[AF-BCHOST-2026-08-22]`）。之前的交接文件說「找不到已確認有
> Blueprint bytecode 的遊戲」，這件事已經解決 —— DQ7R **11,256 個函式中有 705 個帶 bytecode**，
> 另有 **89 個 `BlueprintGeneratedClass`**（`BP_BCAI_Monster_C`、`BP_Weapon_Sword_C`、
> `BP_GameInstance_C` …，Class Type 下拉選單裡直接就有這個過濾器）。
>
> ⚠⚠ **但步驟 2 仍然沒完成,而且卡在一個必須先解決的前置問題:「那兩個對話框從來沒被證明會出現
> 任何一列」。** 2026-08-22 試了 **5 次全部回 0 列**:原生欄位 `DOLLGameCharacter::HP`、原生函式
> `MoveToLocation` 與 `GetRemainingExpToNextLevel`、Blueprint 自己的變數
> `BP_BCAI_Monster_C::Probability_Gake`,最後一次還把 `Game only` **取消勾選**當對照組,仍然 0。
> 依鐵則 1,**在讓它至少噴出一列之前,0 是「正確的空」還是「壞掉」分不出來** —— 所以這不算缺陷
> 回報,也不算通過。
>
> ▶ **下次接手的人請從這裡開始,不要重跑上面那 5 個死路**:先找一個**確定會有 refs** 的欄位／
> 函式讓對話框噴出 ≥2 列（可從 Interesting Props 的評分或 UMG WidgetBlueprint 這種純 BP 邏輯下手），
> 證明偵測器會 fire 之後，才有資格做「連點 6 個欄位標題」那件事。
> ℹ️ 兩個對話框的欄位都已確認是 6 個：Props 是 `Access / Re / Scope / Cont / Property / Type`，
> Xref 是 `Kind / Re / Access / Owner Class / Event / Function`。
> ℹ️ 這次的環境：DQ7R + `dist` AOT v1.0.0.3315、DLL 3315、UE427、**4,393 classes / 149,408
> objects**（和 2026-08-20 那次 AE27 完全相同，代表這個 fixture 狀態可重現）。

| # | 做什麼 | 預期 |
|---|--------|------|
| 1 | ✅ **2026-08-22 在 AOT/trimmed 版（v1.0.0.3313，54.7 MB）上實際點過**，DumperTest UE504。每個標題點兩下：Interesting Funcs 的 Params 降冪為 **19,19,19,17,17,16,15,15**（⭐ 有鑑別力——字串排序會把 `9 (…)` 放最上面）；Detect Stats 的 Offset 升冪為 **`0x28,0x2C,0x2C,0x30,0x30,0x64`**（⭐ 字串排序會從 `0x174` 開始）；Detect Stats 的 Result 升冪全 `· guess`、降冪全 `✓ confirmed`；Live Funcs 的 Period 升冪 17,17,33,33,33,33、降冪相反。⚠ **Console / Live Funcs 的 Params 和 Period 這幾欄不具鑑別力**（參數數量都是個位數、週期在固定幀率下只有兩種等寬值），它們證明的是另一半、也是只有 trimmed build 能回答的那一半：**標題是活的、會排、會反轉**。⚠ **Live Walker 的 Params 這次沒跑**（面板上找不到函式表入口），那一欄本來就是修正前就正確、且 AF20 已驗過的那一欄。ℹ️ 順帶在 UI 上確認了 `[CADENCEGAP-2026-08-22]`：`CameraModifier` 496 calls / 17 ms 對 `ABP_Manny_C` 248 calls / 33 ms —— 呼叫兩倍、週期一半，修正前兩者都顯示 33 ms。 點 Live Funcs 的 **Period**、Detect Stats 的 **Result**（⚠ 舊版這裡寫「✓」，但那是**儲存格內容**不是欄位標題；標題字串是 `str.Detect.ColConfirm` = `Result`，en.axaml:47。而且一次 Detect 若沒有任何 confirmed 列，畫面上連 ✓ 都不會出現）和 **Offset**、Live Walker 函式表的 **Params** 這四個欄位標題。 | 每個都會重新排序，再點一次反向。Period 要照**數值**排（16.7 ms 的列排在 1000 ms 之上），不是照顯示字串。 |
| 2 | **要一款有 Blueprint bytecode 的真實遊戲**（DumperTest 測不到，它的 `Funcs` 欄整欄是空的）。從 Interesting Functions 開 Props 對話框、從 Class Struct 開 Xref 對話框，挑**列數 ≥ 2** 的，每個欄位標題都連點三、四次。 | 兩邊各 6 個標題都會重排；`Access` / `Refs` 照**數字**排（「12W / 3R」排在「2W / 1R」之上）。**而且每一列的內容都不一樣** —— 尤其不可以出現兩列的 Class / Name 對不起來（那就是 cell 被回收後留著上一筆的字）。 |

> **步驟 3 已完成，整列刪除**（維護者驗過 Class Pivot / Snapshot / SPC group 那批；Invoke 參數挑選
> 視窗的 4 個標題 2026-08-21 也在 DumperTest 上驗過：253 列、四個標題共點 7 次，沒有重複列，
> Index 與 Address 全部相異，Class↔Name 也都對得起來）。

-----

## 第 2 步 — 要注入一個執行中的遊戲

任何一款 UE 遊戲都可以。

### ✅ A6 —— Force 是否對子類別一併生效 — **步驟 3、5 全部 CLOSED**（步驟 5 的「生成」那半 2026-08-23 在 DQ7R 上關閉，見 `[A6-SPAWN-DQ7R-2026-08-23]`）

*build 3036 · 優先度 **高** · 步驟 1、2、4 已於 2026-08-19 驗畢並刪除*

| # | 做什麼 | 預期 |
|---|---|---|
| 3 | ✅ **2026-08-22 通過**(`[A6-DERIVE-2026-08-22]`,`tools/verify/a6_prefix_siblings.py`)。用 `Actor::bIsEditorOnlyActor`(58 held)。⭐ **正向**:名字**不以 `Actor` 開頭**、但確實衍生自 `AActor` 的 `StaticMeshActor`,在 hold 期間 `bIsEditorOnlyActor = true` —— 字首比對到不了它。**反向**:33 個可 diff 的物件(含真正的同字首陌生類別 `ActorSequence`)逐欄位比對,**0 個被動到**。⚠ log 那行的 `over 3941 distinct class(es)` 是 `derivedCache.size()`(**評估過**的類別數,整個池),不是命中數 —— 拿它當命中數看會以為嚴重超抓。 | 不相關的同字首類別「沒有」被 hold<br>⚠ 前面步驟看到「hold 了數百筆」不能替代這步：字首比對也會 hold 數百筆，兩者長得一樣。 |
| 5 | 🟡 **2026-08-22:CDO 那半通過,生成那半在這個 fixture 上做不到**(`[A6-CDO-2026-08-22]`,`tools/verify/a6_cdo_and_spawn.py`)。`ActorComponent::bIsEditorOnly` hold 256 筆時,**CDO 全程維持 `false`**,而且抽樣的 12 個活體實例 **12/12 都真的被強制**(通道證明 —— 否則「CDO 乾淨」什麼都不代表)。⛔ 生成那半:debug camera 是**每個 process 一次性**(已被用掉,`state` 已是 1),off→on 循環 295→295 個物件、**0 新增**,而 `ConsoleCommand`／`RestartLevel` 不在這裡列得出的 3,142 個函式裡。⚠ 重開遊戲**不能替代** —— 那會從磁碟重新載入 CDO,正好偵測不到記憶體中的 CDO 被寫。要解鎖:換一款能觸發關卡重載／敵人重生的遊戲。 | 新生成物件不會仍帶著被強制的值（表示沒有寫到 CDO）<br>⚠ 一定要在 reset 之後真的生出新物件；看既有物件測不到這件事。 |

### ✅ V6 / U8 —— 兩個一開遊戲就能看的面板行為 — **CLOSED 2026-08-22**，證據見上一節 `[V6U8-FNAMEPAIR-2026-08-22]`

*build 3016-3031 · 優先度 **高** · 原步驟 1（A5 Preview）已於 2026-08-19 驗畢並刪除；原步驟 2（AE9 排序選單）已於 2026-08-17 驗畢並刪除*

| # | 做什麼 | 預期 |
|---|---|---|
| 1 | ✅ **2026-08-22 通過**（DumperTest，AOT 3315）。篩選字 `Name`、`6 matches`、高亮列 `Layers`、選取列 `Tags` 在 6 秒 × 約 3 拍 auto-refresh 後全部保留，捲軸沒有回到最上方，auto 跑完後按 ▼ 仍正確跳到下一個命中 `Name_Cjk`。⚠ **這次「不跳回最上方」才是有效檢查** —— 先把表格捲到 `0x1C8` 才開始量；同日稍早那次表格本來就在頂端，那條是空的。 | 高亮保留、↑/↓ 步進仍落在高亮列、表格不跳回最上方。<br>⚠⚠ **量具本身才是先前的阻礙**：Live Walker 工具列會**重排兩次**（載入物件後、AOBMaker 連上後），先前記下的按鈕座標會無聲失效。**每次點擊前重新從當下畫面讀座標。** |
| 2 | ✅ **2026-08-22 通過**，而且缺的樣本是**做出來的、不是等來的**。DumperTest 全機沒有帶數字尾碼的 NameProperty（三種方法量過），所以用 CE 對 `DumperTestActor_0::NetDriverName` @ `0x1A0039C7A58` 寫入 `Number=2` 再還原：CE 讀到 `1D 04 00 00 02 00 00 00`，Live Walker 顯示 **`GameNetDriver_1`**（`Number-1` 是 UE 的正確慣例），Value Search 掃 `GameNetDriver_1` **剛好 1 筆**、同位址同 offset。⭐ **負控制有開火**：掃 `GameNetDriver` 的命中數從 **267 掉到 266**，那一列消失 —— 證明 Value Search 的 FName 比對**真的讀 Number**，不是只比 ComparisonIndex。 | 面板與 Value Search 顯示同一組 8 bytes、尾碼數字一致。<br>⚠ 物件／實例「名稱」被截斷是另一條未修的線，不要當成這項失敗。（本次確實又看到：Instances 面板寫 `DumperTestActor`，Live Walker 與 Value Search 寫 `DumperTestActor_0`。） |

### 🟡 AE2 / AE3 —— Class/Struct 面板在快速切換選取下的同步（**步驟 1、2、6 通過;步驟 5 發現缺陷;步驟 3 半、步驟 4 前提做不出來**）

*優先度 **中** · 2026-08-22 於 DQ7R 實跑(`dist` AOT v1.0.0.3315／DLL 3315／UE427／149,370 objects)*

| # | 做什麼 | 預期 |
|---|---|---|
| 1 | ✅ **2026-08-22 通過**。過濾字串 `DOLLGameCharacter`,依序點 ScriptStruct → Function → instance → Class 四種列:標頭分別變成 `DOLLGameCharacterParameters`(72)／`MakeDOLLGameCharacterParameters`(72)／`DOLLGameCharacter`(1712)／`DOLLGameCharacterManager`(56),欄位都有載入。⭐ **四個 Properties Size 各不相同、欄位清單也不同**,所以是真的重新載入,不是看起來有換。 | 每次點擊 Class/Struct 標頭都跟著換，欄位有載入內容。 |
| 2 | ✅ **2026-08-22 通過**。**過濾字串 `DOLLGameCharacter`**(10 列:7 個 class-like + 3 個 instance,確實交錯,滿足那條 ⚠)。↓×9 再 ↑×3 共 12 次快速切換、中間跨過 instance 列,停在 class-like 列上:標頭 = `DOLLGameCharacterSeedCorrectParameters`(36),與反白列相符,欄位(Tikara/MaxHP/MaxMP…)都在。 | Class/Struct 標頭與反白的那一列相符。<br>⚠ 清單若只有單一種類（全 instance 或全 class-like）跑再多次都證明不了；要記錄用的過濾字串。 |
| 3 | ✅ **兩半都關了**。「穩定後不會卡住」2026-08-22 實機通過;「載入中不會提前消失」2026-08-25 離線關閉 `[AE23-SPINNER-2026-08-25]`——那半是 ViewModel 性質,不是畫面性質,截圖取樣不到不代表測不到。 | 見下方區塊 |
| 4 | 🟡 **真的切了關卡,但前提條件做不出來 —— 而且現在知道為什麼**(`[AE-LEVELRELOAD-2026-08-22]`)。授權重開後實際在 DQ7R 裡從標題畫面讀取存檔進到魚灣村,**關卡確實載入了**(物件池 **149,408 → 199,194**,+49,786,`view-0.log` 有兩行 `Loaded … named objects`)。但選中的節點在載入前後**walk 出完全一樣的結果**(`WBP_Common_TutorialTitleDeco_C`,54 fields,Properties Size 704,前後兩次都一樣),**沒有出現錯誤行,因為根本沒有失敗**。<br>⭐ **原因是這一步的前提本身有問題**:點 instance 節點走的是 `get_object` → **它的 UClass** → `walk_class`,而 UE **切關卡釋放的是 instance,不是 UClass**。Blueprint class 只要在轉換兩側都用得到就一直在,重掃後 `TutorialTitleDeco` 仍有 11 筆、class 活著、還多了幾個新 instance。所以「切關卡讓 class 位址失效」**對這種 class 行不通**。要做出來得找一個**只存在於轉換前**的 Blueprint、且它的 package 會被卸載。<br>⚠ 而且就算前提做出來了,步驟 5 已證明**再點一次已選中的列根本不發事件**,所以「再次點擊會重新嘗試載入」得改用「先選別列再選回來」才驗得到,原文的操作方式驗不出來。 | 出現錯誤訊息行，且再次點擊會重新嘗試載入（不是靜默忽略、停留在舊 class）。 |
| 5 | ✅ **2026-08-23 通過（缺陷已修，build 3322）**。原本是 ❌ 失敗並開出 `[TREERECLICK-2026-08-22]`；修法是 handoff 先清掉樹狀選取，因此樹不再宣稱一個畫面上沒有的選取，而且再點同一節點變成真正的變更、會重新載入。實測：選 `Actor`(544) → `Walk Class` 推 `DOLLSoubiState`(168)、**`Actor` 高亮消失** → 再點 `Actor` → 高亮回來且面板重載 544。wire 佐證 `ui-pipe-0.log` id=90 `walk_class` 與 id=88 同位址（缺陷時這一筆完全不存在）。詳見 `[TREERECLICK-2026-08-22]` 一節。 |
| 6 | ✅ **2026-08-22 通過,而且有線上證據**。選中節點後在樹狀 Filter 框連續打 `Def` → `ault`(兩段、中間停頓),樹縮成 `Filtered: 1 / 3`,**面板沒有被清空**(仍是 `DOLLPlayerController` 2224、欄位齊全)。⭐ 更強的證據在 `pipe-0.log`:打字期間**完全沒有新的 `walk_class`**,所以「不會重複重走 class」是量到的,不是看起來沒事。 | 不會重複重走 class，面板也不會被清空。 |

### 🟡 G2 —— 版本掃描加速後結果仍正確（步驟 1 **2026-08-23 答出**，見 `[UE3-GALGUN-2026-08-23]`；步驟 2 的 UE5 分支仍無宿主）

*優先度 **中** · 原步驟 3、4 已於 2026-08-18 驗畢並刪除*

| # | 做什麼 | 預期 |
|---|---|---|
| 1 | 把 `DetectVersion: PE resource failed, falling back to memory string scan` 到下一條 `SCAN:Ver` 之間的時間拆開量：加一條分隔 log，或改用一款 pre-UE4 檢查會提早結束的遊戲重測。同時記下遊戲名與 exe 位元組大小。 | 單獨的版本字串掃描本身在 1 秒以內。<br>⚠ 未拆分前不可記「G2 比宣稱慢」——目前量到的 2.4 s 內含 `CountPreUE4Markers` 另一次全檔掃描。 |
| 2 | ✅ **`ascii` 已於 2026-08-18 用 OCTOPATH 驗出**（`winmm.dll` proxy）：`DetectVersion: Tier 1 (ascii) '++UE4+Release-4.18' -> 418`。四種組合已收三種（`utf16`+UE4、`ascii`+UE4、Tier 0 直接結束），**只剩 UE5 分支**。 | ⛔ **UE5 分支本機無宿主，先別開遊戲**：全機 18 個已安裝 UE 執行檔用 `py tools/verify/tier1_host_survey.py` 離線掃過，只有 3 個能產生 Tier-1 行，全是 UE4。需要「**同時**穿過 Tier 0 **且**映像檔內含 `++UE5+Release-` needle」的遊戲 —— Light Maze/Lushfoil/Manor Lords 有 needle 但停在 Tier 0；Solarpunk/TQ2/ES2/STVoyager/Satisfactory/DSA/Avowed 連 needle 都沒有。<br>⚠ **裝新遊戲前先用該工具篩**，不要靠引擎版本猜。<br>✅ **2026-08-22 從另一個方向印證**(`[G11-TIERS-2026-08-22]`):全機 log 裡真正產生過 Tier 1 的三款遊戲(DQ7R／DQ I&II／OCTOPATH)**全是 UE4**(4.27／4.27／4.18),與離線掃描的結論一致 —— UE5 分支確實無宿主,兩種方法各自測到同一件事。 |

### (original steps) W1 / W7 —— 匯出的 .usmap 能被真實解析器讀出

*build 2853 · 優先度 **中***

⭐ **2026-08-22 阻礙解除(維護者指示):不必裝 FModel —— 把 CUE4Parse 當 NuGet package 加進來測。**
本機沒裝 FModel 也沒裝 CUE4Parse(查過),原本因此卡住。
⚠ **關鍵是別變成循環論證**:讀取器必須是**第三方**的 `UsmapParser`,不能拿我們自己的寫入邏輯
反過來當讀取器 —— 那只會證明我們跟自己一致。CUE4Parse 是外部實作,所以成立。
⚠ 這個 package 只給**驗證用的**一次性專案,不要進 `UE5DumpUI` 的相依(AOT/trimming)。

| # | 做什麼 | 預期 |
|---|---|---|
| 1 | 連上任一遊戲，Export → USMAP 匯出檔案 | 產出 .usmap 檔 |
| 2 | 在 FModel 用 Directory selector → Mappings file 載入該檔（或直接跑 CUE4Parse 的 UsmapParser） | 成功載入 |
| 3 | 查 AActor 的 bHidden / InitialLifeSpan | 屬性名稱與型別都正確列出<br>⚠ 「沒有報錯」不算通過；空表或亂碼視為失敗 |
| 4 | 順便查一個 Blueprint 類別（`*_C`） | ⚠ **原文已過期(2026-08-22 更正)**:W8 已於 2026-08-20 修好並驗證(`[W8-USMAP-2026-08-20]`),它的作用**就是把 BlueprintGeneratedClass 加進匯出**。所以現在 `*_C` **查得到才是預期**,查不到反而是缺陷。實測 DumperTest 有 5 個(`ABP_Quinn_C` 56 props、super `ABP_Manny_C`)。 |

### (original steps) D2（顯示配對） —— Group Scan 列上顯示的是真正的配對

*build 2715 / 2719 · 優先度 **中***

| # | 做什麼 | 預期 |
|---|---|---|
| 1 | 不下任何 filter，直接看 Group 結果的 master row | 預設顯示的一對值優先為非 0（不會是 PrimaryActorTick.TickInterval=0, InitialLifeSpan=0），且每個 slot 後面帶 (+N) 的 match count |
| 2 | Filter 輸入 tickcount frozenint（空白 = AND），再把兩個字順序對調重試 | 該列變成 TickCount=NN (+1), FrozenInt=424242 (+35)；字序對調結果相同 |
| 3 | 展開該列按 All fields，再按一次收合 | 列出該 slot 保留的所有 leaf，且物件自己的欄位排在最前面（FrozenInt 不必往下捲）；第二次按會收合，重開會重新查詢<br>⚠ 某個值「沒出現在列上」不代表沒 match — 先看 (+N) 與 All fields 再下結論 |
| 4 | 對 All fields 裡任一 leaf 依序按 Live / Addr / Pivot / Locate | 四個都能正常跳轉；deep 或 Snapshot 來源的列若取不到 leaf 位址則整個省略 → 0x… 箭頭，而不是印 → 0x0 或物件 base |

### 🟡 Genau RIP decode (b2544) —— **只剩 GObjects**;GNames / GWorld 已於 2026-08-22 關閉

*優先度 **低** · 需要：一款 GObjects AOB **真的掃不到**、而且 data-section scan 能找出**真正**
object pool 的 UE 遊戲。DumperTest 做不到 —— 原因見下,那才是重點。*

✅ **2026-08-22 `[GENAURIP-RECOVERY-2026-08-22]`。** 用本專案 `PEHOOK` 的老辦法把 recovery 路徑
逼出來(兩行:在 `FindGObjects` / `FindGNames` 把 `ScanForTarget` 的結果強制設 0),
rig：`py tools/verify/genau_rip_recovery_ab.py`。

| | pre | post | AOB 基準 |
|---|---|---|---|
| `GNames` | `0x7FF75A0568C0` | `0x7FF75A0568C0` | `0x7FF75A0568C0` |
| `GWorld` | `0x7FF75A3488A0` | `0x7FF75A3488A0` | `0x7FF75A3488A0` |

兩側的 fallback 都**確實跑了**(log 行是斷言出來的,不是假設),模組**沒有 rebase**
(`code_base` 兩側都是 `0x7FF74A311000`,有查),GNames 逐 byte 相同且與 AOB 一致。

⭐ **收益也比 notepad++ 那次清楚三個數量級**:候選數 **508,10x → 506,59x**,gap 約 **−1,510**
(四次獨立 run:1511/1516/1513/1509,run 間變異只有 ±5)。notepad++ 當時只量到 −2。
另外附帶一個獨立指標:pre 側**就緒時間 15 秒 vs post 9 秒**。

⚠⚠ **GObjects 這樣是驗不了的,而查出這件事才是最有價值的部分。** 被逼到 data-scan fallback 後,
DumperTest 的 `ValidateGObjects` 會接受**假陽性** —— `Objects=583`、`Objects=2556928`
(真實 25,179)—— 而且回的是 **heap 位址**,每次啟動都會變。挑中哪一個假陽性取決於當下的 heap:
post 側三次都選 `0x7FF74B0BC264`,pre 側選過 `0x7FF74B0CAC34` 和 `0x7FF74B0BC3E4`。
兩側不同**不是 regression**,拿它來斷言等於把雜訊當訊號。

⭐ **教訓:把路徑 stage 出來只讓它「有跑」,不會讓比較「有意義」。** 這是兩個條件,第二個要另外檢查。

| # | 做什麼 | 預期 |
|---|--------|------|
| 1 | 手上若有 GObjects AOB 真的掃不到的 UE 遊戲,對它跑 `py tools/verify/genau_rip_recovery_ab.py`(rig 對任何宿主都能跑)。 | 兩側 GObjects 逐 byte 相同。<br>⚠ **先確認它解出來的是真的**:比對 `Objects=` 和該遊戲實際物件數;數字離譜就跟 DumperTest 一樣是假陽性,那樣的相同或不同都不算數。 |

### ⬜ AC13 / AC14 —— Pipe 傳輸計時、關閉時的 reader

*優先度 **低***

| # | 做什麼 | 預期 |
|---|--------|------|
| 1 | ✅ **2026-08-22 通過**(`[AC13-2026-08-22]`)。AOT 版 v1.0.0.3315 連上 DumperTest 後送 `WM_CLOSE`:所有 log 裡 `ReadLoop` **0 次**,DLL 端只有兩行 `PipeServer: Client disconnected`(UI 用兩條 lane),整個 `pipe-0.log` 零 ERROR/WARN。⭐ 而且「沒有」不是空的:`ui-pipe-0.log` 在關閉前就停了,是 `ui-init-0.log` 的 `[16:24:15.146] UE5DumpUI shutting down...` 證明 logger 當下還活著並有寫入。 | 乾淨結束，**不可以**出現 `Pipe: ReadLoop error`。修正前那一行是關閉時的 NullReferenceException，把正常關機記成故障。 |
| 2 | ⛔ **2026-08-22:System 分頁上沒有 IPC 數字**。那裡有的是 *DLL dispatch cost*(每個指令的 Count／Total／Avg／Max／% busy —— DLL **派送端**的成本)與 *Pipe Activity* 的往返時間。AC13 修的那個傳輸計時在 `PipeTransportStats`,唯一的消費者是 `DiagnosticsProbe`,而它只包住三個操作(Copy CE XML／Copy CE Field／Snapshot capture)並寫一行 `PERF` 到 `view-0.log`。✅ **基準已從那條 PERF 行取得**(`[B10-2026-08-22]`):Snapshot capture 的 `split dll 189.7 / ipc 34.3 / ui 414.6 ms`,6 次 dispatch、每次 ipc 6.850 ms。 | 記下數值即可，這是下一步的基準。 |
| 3 | ⛔ **2026-08-22:這一步的觀測管道會被它自己的動作關掉**。`DiagnosticsProbe.DisposeAsync` 收尾時要再呼叫一次 `GetDiagnosticsAsync`,而 `catch { return; }` —— 遊戲一斷線,**那行 PERF 根本不會寫**,想讀的數字產不出來。要解鎖:(1) 把 `PipeTransportStats.Snapshot()`(單調、不需要 pipe)顯示在 System 分頁上,或 (2) 用真的 in-process `NamedPipeServerStream` 在寫入中途 dispose 來測計時器的位置。⚠ `PipeTransportStats` 目前**完全沒有測試**,而同一族的 `ClassifySendFailure` 有(它是純函式)。 |

-----

## 第 3 步 — 遊戲 ＋ Cheat Engine

還要開 CE 並載入 .CT。

### ✅ U16 —— 大型 enum 的成員清單 — **CLOSED 2026-08-22**，證據見上一節 `[U16-ENUM65-2026-08-22]`

*優先度 **中** · 需要：有 `EPhysicalSurface` 規模（數十個成員）enum 欄位的遊戲*

| # | 做什麼 | 預期 |
|---|---|---|
| 1 | ✅ **2026-08-22 在 DQ7R 上通過**：`DefaultPhysicalMaterial` → `SurfaceType`（`EPhysicalSurface`，**65 個成員**）。四個互相獨立的見證都一致：DLL log `read 65 of 65`、匯出的 CE XML 65 行且 index `0..64` 連續、CE 自己在 Lua Engine 裡回報 `n=65 offby0=0 dups=0`（**負控制有開火**：同一圈對 `i+1` 記分得到 `CTRLoffby1=65`）、以及 CE 畫出來的 dropdown 展開到底是 `64 : EPhysicalSurface_MAX`。 | 成員完整，沒有缺尾。<br>⚠⚠ **原本寫在這裡的「DumperTest 的天花板就是 26」是錯的 —— 它其實到 113**（`DumperTest/walk-0.log:212`）。26 從來不是宿主的性質，只是「那一次走訪剛好碰到哪些 class」。<br>▶ 連帶地，原本建議的篩選指令只 grep `walk-0.log`（**只有當次執行**），旁邊還躺著 127 個 `walk-*.log`。要篩就篩整個資料夾，指令見上一節。 |
| 2 | ✅ **2026-08-22 重新在整個 log corpus 上量過**（294 個 walk log、5 個宿主）：**4,919** 次 `ResolveEnumValue`，`N != M` **0 個**，`truncated read` **0 個**，最大的表 **212**（DQ7R）。 | `read N of M` 中 N 等於 M；出現任何 `GetEnumEntries: ... truncated read` 就是真的有問題，要記錄下來。 |

### ⛔ NO SAMPLE ON THIS MACHINE 2026-08-22 `[EXTRASCAN-NOSAMPLE-2026-08-22]` — B18 —— Extra Scan 跑到一半被取消要立刻收工（**Fern::Stop graceful 已完成，只剩這一步**）

*優先度 **中** · 需要：**GObjects 無法用 AOB 一次解出**的遊戲，否則 Extra Scan 根本不會跑久*

| # | 做什麼 | 預期 |
|---|---|---|
| 1 | 讓 Extra Scan 真的跑久，跑到一半取消 CE record 或關掉 UI。 | `PipeServer: Stop watches+scan joins done` 在 `Stop entry` 後約一秒內出現。FAIL = 中間隔了好幾秒，或 CE 視窗整個凍住直到掃完。 |

### ✅ U3 / U17 — BOTH halves closed: GAS 2026-08-23 `[U3U17-GAS-2026-08-23]`, **LWC 2026-08-24** `[U3U17-LWC-2026-08-24]`

> ### ✅ The LWC container arrived, and it was the only thing missing
>
> This header used to end *"the LWC host is FOUND, only its container is not"*. `TMap<int32, FVector>`
> and `TSet<FVector>` were added to `ADumperTestActor` and all three configs repackaged.
> DumperTest dev, DLL 3350, one `walk_instance`:
>
> | | |
> |---|---|
> | `Map_IntToVecLwc` | `{X=6401000.1234, Y=6402000.2345, Z=6403000.3456}` and `{X=6411000.1234, …}`, `map_stride` **40** |
> | `Set_VecLwc` | `set_elem_size` **24**, `set_elem_struct_type` `Vector`, `{X=6501000.1234, …}` |
>
> ⭐ **The wire bytes decode as DOUBLES, proven from the raw hex rather than from the rendered text.**
> `vh = 1DC9E507FA6A58414A0C020FF46B5841` unpacks little-endian as `X = 6401000.1234`,
> `Y = 6402000.2345`. Read as **float32** the same bytes give `3.457e-34, 13.5261, 6.41e-30, 13.5264` —
> so a 4-byte misread produces recognisable garbage, not a plausible number.
>
> ⭐ **The narrowing control is the reason the values look odd, and it bites**: all three tails CHANGE
> through float32 — `6403000.3456` becomes **`6403000.5`**. A round value such as `62010.5`
> round-trips through float32 exactly and would be blind to a silent narrow anywhere in
> decoder -> wire -> UI. `Map_IntToVec3f` (three 4-byte floats) renders through the same decoder in the
> same reply as the width control.

Run on **Elliot** (`Elliot-Win64-Shipping`, UE504, 85,079 objects, dxgi proxy) with the AOT `dist` UI
v1.0.0.3315, at the main menu — which the row explicitly permits (*"CDO 走訪即可，主選單就夠"*).

⚠ **Setup that must not be skipped, and it is this row's real trap.** Elliot's deployed proxy was the
**2026-08-19 build** (2,876,928 B) while `dist` is 3315 (2,891,264 B). A stale proxy serves the pipe
and *ignores* a fresh injection — `pipe_client`'s trap 1 — so every number below would have described
a three-week-old DLL. It was refreshed from `dist/proxy/dxgi.dll` (byte-identical to the TQ2 proxy
already proven at 3315) and the game then reported `build: 1.0.0.3315`. `assert_build()` is what makes
this checkable rather than a hope.

**The GAS half — ✅ PASS, and it is a REGRESSION GUARD, not a feature check.** The row's own wording:
*"GAS really does have a vtable, and 'just delete the skip' would show four values here."*

`Default__CharacterAttributeSet` @`0x7FF4DE8B77F8` — **30** `FGameplayAttributeData` fields, every one
previewing as exactly `{BaseValue=…, CurrentValue=…}`. Two members. No pointer halves, no `f:[…]`
byte-blind fallback. Magnitudes are real game defaults, not garbage: `BasicMoveSpeed = 1000`,
`BasicJumpZVelocity = 2100`, `MaxDivingOxygenPoint = 5`.

⭐ **The vtable was proven present from the raw bytes, so "the skip works" is measured, not inferred:**

| field | first 8 bytes | next 4 (BaseValue) | next 4 (CurrentValue) |
|---|---|---|---|
| `HealthPoint` | `F8924748 01000000` | `0000803F` = 1.0f | `0000803F` |
| `BasicMoveSpeed` | `F8924748 01000000` | `00007A44` = 1000.0f | `00007A44` |
| `BasicJumpZVelocity` | `F8924748 01000000` | `00400345` = 2100.0f | `00400345` |

The leading pointer `0x1484792F8` is **identical across all three** — exactly what one shared vtable
looks like — the declared size is **16** and the fields sit on a **16-byte stride** (0x30, 0x40, 0x50…),
and each decoded float matches its own hex exactly.

⭐⭐ **The expansion is the clincher, because the OFFSETS are the evidence.** Expanding `HealthPoint`
in Live Walker gives a two-row table:

```
0x8   BaseValue      FloatProperty   1    0000803F
0xC   CurrentValue   FloatProperty   1    0000803F
```

`0x8` and `0xC` — the first eight bytes are excluded. Delete the skip and members would also appear at
`0x0` and `0x4`, i.e. **four** values. ⚠ And the fourth would be *convincing*: the vtable's high half
reads `01000000` = **1**, which would sit in the grid looking like a perfectly ordinary attribute.
That is why this guard exists.

⭐ **Two independent witnesses.** The headless pipe pass was run **before** the UI was launched, and the
UI then showed the same values, the same hex and the same offsets. The UI is not the only observer.

**The LWC half — 🟡 host FOUND, vehicle still missing.** The sweep called this blocked for want of a
UE5 LWC title. **Elliot is one**, measured rather than assumed: on `Default__SceneComponent`,
`RelativeLocation` / `RelativeRotation` / `RelativeScale3D` / `ComponentVelocity` all report
**size = 24**, and `RelativeScale3D`'s hex is `000000000000F03F` ×3 — three IEEE-754 **doubles** of
1.0, i.e. three components at the right magnitude.

▶ So step 1 no longer needs a different game; it needs a **struct-valued `TMap`/`TSet` with a populated
element** inside Elliot. That distinction matters because the container-element path is a *different
caller* from the plain-field path, which is the whole point of U17. One search attempt was made and
failed for a tooling reason (`search_properties` rejects an empty `name`), not because none exists —
so this is "not yet looked for properly", not "not there".


#### 🟡 …and the LWC half's blocker is now MEASURED rather than assumed `[U3U17-LWC-BLOCKER-2026-08-23]`

Searched Elliot (UE504, LWC confirmed) for the one thing step 1 still needs: a **struct-valued
`TMap`/`TSet` whose element carries an `FVector`, with at least one entry**. It is not there **at the
main menu**, and that is now a measurement with a working detector behind it rather than a failed
look.

**What exists:** 1,442 distinct container properties; 44 of them populated (`{Map: 325, Name→Struct}`,
`{Map: 201, Int→Struct}`, `{Map: 102, Object→Struct}`, …). **11 containers whose element really is
vector-like** — `SubPoints` (`MapProperty`, element **Vector**) on `CalibrationPointComponent`,
`ProxyComponentCentersObjectSpace` (`Vector`) on `LandscapeMeshProxyComponent`,
`CachedBoneSpaceTransforms` / `CachedComponentSpaceTransforms` / `RestTransforms`
(`ArrayProperty` of **Transform**, and an `FTransform` holds two `FVector`s).

**Why none of them serves:** every vector-bearing container is empty here. The 11 sit on CDOs, and the
**29 live heap `SkeletalMeshComponent` instances** all have `CachedBoneSpaceTransforms` at zero
entries — at a title screen the meshes are not ticking, so nothing has filled the caches. ▶ **This row
needs Elliot in actual gameplay**, not a main-menu CDO walk. That is a sharper statement than
"no fixture on this machine": the host is right, the container kinds exist, only the *population* is
missing, and a loaded save should supply it.

⚠⚠ **A tooling trap that produced a confident zero, and the control is what caught it.** The obvious
approach — `search_properties` filtered to `MapProperty`/`SetProperty`, then read `struct_type` — sweeps
1,442 rows and reports **0** vector-like containers. That answer is meaningless: a control showed
`struct_type` is populated on **0 of 1,404** container rows while `StructProperty` rows carry
`struct_type='Vector'` quite happily. `search_properties`' row schema exposes only `inner_type`
(e.g. `"StructProperty"`) — *which* struct is never in the reply, so the question cannot even be asked
of that command.

⭐ **The data is on the wire, under different keys, from a different command.** `walk_instance` emits
`array_struct_type` / `map_key_struct_type` / `map_value_struct_type` / `set_elem_struct_type`
(`Fern.cpp:1477-1606`). Re-run through `walk_instance` over 500 live instances, **211 fields reported
an element struct type** — the detector demonstrably fires — and the 11 candidates above fell out
immediately. ▶ **To ask "which struct is inside this container", walk the instance; do not filter the
property search.** This is working-lessons §1.2 with a new failure mode: the rule was right, the *key*
was wrong, and a wrong key looks exactly like a true absence.


#### ⛔ …and gameplay does NOT supply it either — Elliot is RULED OUT 2026-08-23 `[U3U17-LWC-ELLIOT-OUT-2026-08-23]`

The previous note said the containers were empty only because nothing ticks at a title screen, and
that a loaded save should populate them. **Tested, and that prediction was wrong.**

Elliot was driven into real gameplay — autosave loaded, worlds `MainField_A2` / `BGE_FLD_A2` /
`EV_FLD_A2` live. Re-swept **900 live heap instances**: **748 populated containers of any kind**, and
**0** whose element is `Vector` / `Rotator` / `Transform` / `Quat`.

⭐ **That 748 is the whole point** — it is the negative control. An earlier pass of this same sweep
reported "0 vector-bearing" while ALSO finding 0 populated containers of any kind, which proved
nothing. With 748 the detector is demonstrably firing, so the 0 is a measurement.

▶ **Elliot is ruled out as the LWC vehicle**, and not for lack of trying: the two candidate shapes are
dead ends by design. `CalibrationPointComponent::SubPoints` (a `TMap` of `Vector`) is camera-calibration
tooling that never instantiates at runtime, and `SkeletalMeshComponent::CachedBoneSpaceTransforms` reads
`count: 0` on **all 127** live skeletal meshes — that cache is editor/debug-oriented and stays empty in
a shipping build. So step 1 needs a **different title**, not a different game state.

⚠⚠ **Third wrong-key trap in one row, and this one nearly produced a false negative.** For an
`ArrayProperty` the walk reply carries **`count`**, not a `value` string — arrays have no `value` key at
all. A sweep that parses `value` therefore scores every array as empty. Combined with the two earlier
traps (`search_properties` exposes only `inner_type`, never the element struct; the element struct types
live on `walk_instance` under `array_struct_type` / `map_value_struct_type` / `set_elem_struct_type`),
the rule for this codebase is blunt: **the field you want is often on the wire under a different key,
from a different command, and reading the wrong one looks exactly like a true absence.** Always show the
detector firing on something before believing a zero.

ℹ️ Operational note: Elliot renders **fullscreen-exclusive**, so computer-use screenshots capture the
desktop behind it and it cannot be driven by sight. `alt+Return` toggles it to windowed and makes it
visible — that is what unblocked the save load here. Its keystrokes DO land while exclusive; they just
cannot be verified visually, so use the pipe (world name, object count) as the feedback channel.


### 🟡 U3 / U17 —— struct 預覽的 LWC 寬度與 GAS 樣本（GAS 半 **CLOSED 2026-08-23**；LWC 半只差容器樣本）—— 證據見 `[U3U17-GAS-2026-08-23]` 一節

*build 3169 / 3171 · 優先度 **中** · 需要：一個 UE5 LWC（24-byte FVector）遊戲、一個使用 GAS 的遊戲*

| # | 做什麼 | 預期 |
|---|---|---|
| 1 | 在 UE5 LWC（24-byte FVector）遊戲上展開一個 struct-valued 的 TMap/TSet 元素。 | 三個分量都出現，數量級正確。 |
| 2 | 在使用 GAS 的遊戲上做同樣展開（CDO 走訪即可，主選單就夠）。 | 成員完整、寬度正確。 |

### 🟡 G1 / X3 / U7 / AF2 — step 3 CLOSED 2026-08-23 `[AF2-CLASSCAP-2026-08-23]`; step 2 reproduced on a 2nd host; step 1 has no fixture on the shipped build

**Step 3 (AF2, the class-probe cap) — ✅ CLOSED, both halves, which is what the row insists on.**

| half | host | status line | verdict |
|---|---|---|---|
| **> 30 candidate classes** | **TQ2** (UE507, 279,586 objects, 32 classes) | `80 candidates · 41 confirmed  ⚠ 30 of 32 classes live-probed — 2 shown as "? not checked"  ⚠ 6 keyword(s) hit the 200-row cap` | ✅ |
| **< 30 candidate classes** | **DQ7R** (UE427, 149,408 objects) | `80 candidates · 20 confirmed  ⚠ 5 keyword(s) hit the 200-row cap` — **no probe suffix at all** | ✅ |

⭐ **The negative half is provable, not merely observed.** `DetectStatsViewModel.cs:253-256` emits the
suffix only when `unprobedClasses = byClass.Count - classesProbed > 0`. Its absence on DQ7R therefore
*means* `byClass.Count ≤ classesProbed ≤ 30` — the row's "候選 class 少於 30" case — rather than
"I didn't notice a banner".

⭐ **Exactly 2 rows carry the badge, and they are visibly distinct from a guess.** Seen together in one
screenful on TQ2: `? not check[ed]` on `TQ2QuestSpawnOnDamaged::m_TargetDamage` and
`wbp_summons_hud_entry_C::T_Cooldown_Timer`, each annotated **"not live-probed (past the class cap)"**,
sitting among `· guess` rows (`m_ActiveWeaponSet`, `MaxUndilatedFrameTime`, `DemoRemainingTime`, …).
Count matches the status line's "2".

⭐ **The colour claim was checked in SOURCE rather than judged from a screenshot** — `DetectedStat.cs:66`
`ConfirmColor` is `#C08A3E` (amber) for not-checked vs `#808080` (grey) for guess and `#6A9955` for
confirmed. The code even states the intent: *"The third is not a weaker guess — it is the absence of
evidence, and calling it a guess is the AF2 defect."*

**Step 2 (X3, non-ASCII StrProperty) — 🟡 unchanged, but now reproduced on a SECOND host.** todo.md
already recorded the split from an earlier host; DQ7R independently gives the same one, which upgrades
"no fixture here" from one machine-state to two titles:

* ✅ **rows come back** — 169 distinct `StrProperty` fields, 6 with CJK previews (`"アルス"` …), no error
  and no 0-row result, which was the entire pre-fix failure mode.
* ✅ **the ellipsis marker is emitted** — previews cut at 50 chars and end `…`, e.g.
  `"../../../Engine/Content/EngineFonts/Faces/RobotoBo…"` and
  `"Creates a gradient of 0 near the camera to white a…"`.
* ⬜ **the two together still have no fixture.** Every CJK preview in DQ7R is a short name (≈6 bytes);
  every >50-char preview is ASCII engine-path text. A localized title stores display strings as
  **`FText`**, not `FString`, so CJK `StrProperty` tends to be short identifiers — that is *why* this
  combination is hard to find, and it is worth writing down rather than re-searching each time.

**Step 1 (G1, partial offsets) -- CLOSED 2026-08-24 `[G1-AMBER-2026-08-24]`, by manufacturing it.**

The blocker below was accurate and is kept for the record: `unmeasured:` appears in exactly 4 archived
files, all under `Logs\DumperTest\`, all from build **`91d09b94-dirty`** -- the same uncommitted
experimental build already flagged for bogus `data_scan` GObjects. No clean build on any host has ever
reported a partial offset measurement. ⛔ **That is precisely why the row's own procedure had to be
abandoned rather than run again.**

⚠⚠ **THE WRITTEN STEP CANNOT FAIL, AND THE REGISTER HAS "RUN" IT 13+ TIMES.** It says *"open the
Pointers tab on a game whose offset detection partially fails"*, with the caveat *"no banner is not a
pass; run `get_offsets` and confirm it reports `validated: true`"*. On every host available, detection
does **not** partially fail -- so the caveat branch is the one that always executes, and it passes by
confirming that **nothing happened**. A build where the banner is entirely broken passes it identically.

⭐ **What was run instead: a two-state pair, one variable.** Same game, same UI binary
(`dist\UE5DumpUI.exe` 54.7 MiB AOT-trimmed), same tab, two DLLs differing by one line:

| DLL | `get_offsets` on the wire | the System (Pointers) tab |
|---|---|---|
| **staged 3344** | `validated=False`, `probe_ran=True`, `fallback_reason='unmeasured:elemsize'` | amber banner **PRESENT** |
| **clean 3345** | `validated=True`, `fallback_reason=''` | banner **ABSENT**, layout closes up |

Banner text read off the screen, verbatim:

```
⚠ Dynamic offsets only partially measured (unmeasured:elemsize) - the rest are UE-version
defaults. Values below and every export derived from them may be wrong.
```

Amber border `#E6A817` on `#3A3323`, text `#E6C877` -- i.e. `PointerPanel.axaml:110-116`'s Border, not
a red refusal. The absence is measured too, not just eyeballed: with the clean DLL the *Invoke timeout*
row moves from y=286 to y=256 because the Border stops taking layout space.

**Both of the row's residues are now covered.** (a) a real `validated=false` with a probe-naming
`fallback_reason` travelled the wire and reached the VM; (b) the **rendered** Border was observed --
the axaml binding, which no unit test reaches.

ℹ️ **What the staging did and did NOT simulate, stated plainly.** The staged line is
`unmeasured |= UNMEASURED_PROP_ELEMSIZE;` immediately before `const bool allMeasured` in
`Genau::ValidateAndFixOffsets` -- so `validated`, the reason string, the wire encoding, the VM and the
Border are all the **real** code path, but the *probe failure itself* is simulated: every offset stayed
at its correctly measured value (`fproperty_elemsize` = **52**, confirmed on the wire). That was
deliberate, not lazy -- the genuine give-up branch keeps a **wrong** default, and an ElementSize
landing on PropertyFlags is the ~1 GiB per-element allocation `Ubel.cpp:4206` documents (audit #5 U1),
so corrupting it for real would risk the process while testing something else entirely. What remains
unproven is only that a *probe* can fail on some real game -- not that the flag or the banner work.

The staged source was reverted **before** the test ran (the staged DLL was copied to
`out/g1-staged/`), so the tree was clean throughout and no `[G1-STAGE]` marker can reach a commit.
ℹ️ Incidental: `PipeClient.assert_build()` correctly refused the staged DLL (*"reports build '3344',
but dist is '3345'"*) -- the stale-build guard doing its job on a deliberately mismatched build.


### 🟡 G1 / X3 / U7 / AF2 —— 三個要碰到特定遊戲才看得到的顯示（步驟 3 **CLOSED 2026-08-23**；步驟 2 第二個宿主複現同一半；步驟 1 出貨版無樣本）—— 見 `[AF2-CLASSCAP-2026-08-23]`

*build 3016-3031 · 優先度 **中** · 需要：三種樣本：offset 偵測只量到一部分的遊戲、含超過 50 bytes 非 ASCII StrProperty 的在地化遊戲、以及候選 class 超過 30 與少於 30 各一款的遊戲。*

| # | 做什麼 | 預期 |
|---|---|---|
| 1 | ✅ **CLOSED 2026-08-24** `[G1-AMBER-2026-08-24]` — 不是在真遊戲上等到，而是用 staged DLL 造出來。舊步驟（「在 offset 偵測部分失敗的遊戲上打開 Pointers tab」）**永遠不可能失敗** — 本機沒有任何遊戲會部分失敗，所以永遠跑到 caveat 分支，「什麼事都沒發生」也算過。 | staged 3344：`validated=False` / `fallback_reason='unmeasured:elemsize'`，琥珀色橫幅**出現**；clean 3345：`validated=True`，橫幅**消失**。詳見 todo.md `[G1-AMBER-2026-08-24]`。 |
| 2 | 在在地化遊戲用 Property Search 找一個超過 50 bytes 的非 ASCII（CJK）StrProperty。 | 有結果列回來，preview 以「…」結尾（修正前是整個搜尋 0 列並報錯）。 |
| 3 | Experimental → Detect Player Stats，先在候選 class 超過 30 的遊戲跑一次。 | 超過上限的列以琥珀色顯示「? not checked」（不是「· guess」），狀態列顯示「30 of N classes live-probed」。<br>⚠ 再到候選 class 少於 30 的遊戲跑一次，正確結果是完全沒有這個後綴——兩邊都做才算測完。 |

### 🟡 AE10 —— 🌍 要能用（步驟 1／2／4 **CLOSED 2026-08-23**，步驟 3 前提不成立）—— 證據見 `[AE10-LOCATE-2026-08-23]` 一節（grep 該 tag）；原標題「AOB 掃不到 &GWorld 的遊戲上」

*build 2961 · 優先度 **中** · 需要：AOB 掃不到 &GWorld 的遊戲（Pointers 面板沒有 GWorld 位址，或以 proxy 模式執行，例如 TQ2），外加一款 GWorld 正常解析的遊戲做回歸。*

| # | 做什麼 | 預期 |
|---|---|---|
| 1 | 在該遊戲檢查 Instance Finder、Interesting Functions、Interesting Properties、Detect Stats、Class Pivot、Snapshot（Diff + Group）、SPC Query 各列的 🌍 按鈕。 | 全部可點，不再是灰的。 |
| 2 | 點其中一個 🌍。 | 找到路徑，或顯示 DLL 明確的「no path」/「invalid」訊息。<br>⚠ 沒有任何訊息、靜默無反應就是失敗。 |
| 3 | 反向對照：在關卡尚未載入的主選單（確定沒有活的 UWorld）再點一次 🌍。 | 回報 DLL 的 invalid/no-path 狀態，不能看起來像成功。 |
| 4 | 回歸：在 GWorld 正常解析的遊戲上重跑幾個 🌍 交接。 | 行為與這次改動前完全相同。 |

### ✅ B25 —— pre-4.11 拒絕不再只憑一個 PE 欄位就擋掉 — **CLOSED 2026-08-23**，證據見 `[B25-RECHECK-2026-08-23]` 一節（grep 該 tag）

*優先度 **中** · 需要：PE ProductVersion 落在 4.0–4.10 的遊戲，或可用 UE 版本 override 硬造；反向對照另需一個真正的 UE3 binary。*

| # | 做什麼 | 預期 |
|---|---|---|
| 1 | 用 UE 版本 override，或找一款 PE ProductVersion 報 4.0–4.10 的遊戲，注入後 grep `scan-0.log` 的 `below the … floor — NOT accepting that on its own`。 | 該行出現，而且掃描照樣跑完（tier 3 → low confidence → gate 不啟動）。FAIL = 對一款其實能用的遊戲印出 `SKIPPING the scan`。 |
| 2 | 反向對照：拿一個真正的 UE3 binary 注入。 | 仍然被拒絕，`scan-0.log` 出現 `PRE-UE4 engine POSITIVELY identified`。<br>⚠ 沒跑反向對照就不算測完 — 只證明「不再擋」等於沒證明「該擋的還會擋」。 |

### ✅ B29 — steps 1 and 2 CLOSED 2026-08-23 `[B29-PRODUCTNAME-2026-08-23]` (offline discriminator) + `[B29-LIVE-2026-08-23]` (end-to-end); step 3 still lacks a non-ASCII path

The row needed a third-party `dxgi.dll`/`dinput8.dll` wrapper in a UE game folder. The 2026-08-23
sweep reported none on this machine. **That is no longer true** — the maintainer supplied one:

```
D:\SteamLibrary\steamapps\common\SEED BATTLE DESTINY REMASTERED\Game_SBDR\Binaries\Win64\dxgi.dll
  5,255,448 bytes, 2026-08-02 · ProductName "ReShade" · 0 hits for UE5CEDumper / UE5_Init
```
(installer kept at `%USERPROFILE%\Downloads\ReShade_Setup_6.8.0.exe`). SBDR is a UE title with its
own log folder, so it is a real host, not a synthetic one.

⭐⭐ **What actually discriminates the fixed build from the broken one — and it is NOT the message.**
`git show 229df1d8^:dll/src/Methode.cpp` shows the pre-fix rule was a **path** test:

```c
// Case 2: proxy DLL — only count if NOT loaded from System32/SysWOW64
if (IsProxyDllName(fileName)) {
    if (sysDirLen == 0 || _strnicmp(modPath, sysDir, sysDirLen) != 0) {
        outName = fileName;   // -> "already loaded, no injection needed"
        return true;
```

So the old code already ignored System32 **by path**. A wrapper in the **game folder** is not under
System32, so the old code returned `true` and told the user *"already loaded, no injection needed"* —
the pipe then never appeared. The new code replaces the path heuristic with an identity check.

⚠ **Therefore the six log lines already on disk do NOT close this row**, and it would have been easy
to think they did. `Logs\cheatengine-x86_64\init-20260818-*.log` (build **3262**) contains:

```
CEPlugin: 'dxgi.dll'    is loaded but is not ours (path=C:\WINDOWS\SYSTEM32\dxgi.dll) — not a UE5CEDumper proxy
CEPlugin: 'WINMM.dll'   is loaded but is not ours (path=C:\WINDOWS\SYSTEM32\WINMM.dll) — …
CEPlugin: 'VERSION.dll' is loaded but is not ours (path=C:\WINDOWS\SYSTEM32\VERSION.dll) — …
```
×2 (two Inject&&Connect clicks). Those prove the **message plumbing** — it fires, and it renders the
module name and full path correctly. They prove nothing about the fix, because **both** builds
exclude System32.

**✅ The deciding predicate is now verified, on the case that matters.** `IsOurModule` reads the PE
VERSIONINFO `ProductName` and requires `"UE5CEDumper"`, enumerating `\VarFileInfo\Translation` rather
than assuming the `040904B0` block. `tools/verify/b29_product_name.py` applies that exact rule through
the same Win32 API:

| case | `IsOurModule` | ProductName |
|---|---|---|
| our `dist/proxy/dxgi.dll` | **True** | `UE5CEDumper` |
| our `dist/UE5Dumper.dll` | **True** | `UE5CEDumper` |
| **ReShade wrapper (game folder)** | **False** | `ReShade` |
| Windows' real `dxgi.dll` | **False** | `Microsoft® Windows® Operating System` |

⭐ **Both controls are present, and the rig FAILS without them** — a predicate that answered "not ours"
for everything would pass the wrapper case vacuously, so the run asserts it saw at least one of each.

⭐ **The two detectors provably cannot disagree**, which the C++ comment claims and which was checked
rather than taken on trust: `DumperModuleDetector.IsOurs` is
`string.Equals(productName, Constants.ProxyProductName, OrdinalIgnoreCase)` with
`ProxyProductName = "UE5CEDumper"` — the same rule as `_wcsicmp(value, L"UE5CEDumper")`.

**▶ What is left is one end-to-end run**, and it is blocked on a *permission*, not on a fixture:
the CE plugin is **not installed**. `HKCU\Software\Cheat Engine\Plugins64` registers only
`AOBMaker_CEPlugin.dll` (enabled) and `CE-Handwire.dll` (disabled), and the copy at
`out\ce-plugin-test\UE5Dumper.dll` is the stale **3262** build. Installing it means copying the
current DLL into CE's plugin folder **and writing a registry key** — a persistent change to the
maintainer's Cheat Engine, so it is not something to do unasked. Once installed: attach CE to SBDR,
click *UE5CEDumper: Inject && Connect*, and expect
`'dxgi.dll' is loaded but is not ours (path=…\Game_SBDR\Binaries\Win64\dxgi.dll)` **plus a normal
injection** — FAIL being the old *"already loaded … no injection needed"*.

ℹ️ **Step 3's non-ASCII case has a cheaper subject than the row assumes.** It expects a game whose
**path** contains non-ASCII, and the historic symptom was `EVERSPACE? 2`. But EVERSPACE's install
folder is plain ASCII (`…\common\EVERSPACE`, verified byte-wise), so the `?` did not come from a path.
The likelier source is a **ProductName**: Windows' own `dxgi.dll` reports
`Microsoft® Windows® Operating System`, and `EVERSPACE® 2` carries the same `®`. Any run that logs the
System32 modules already exercises a non-ASCII string through this exact format — so step 3 may be
satisfiable from the same session rather than needing a specially-named install.


#### ✅ …and the end-to-end CE run PASSED the same day `[B29-LIVE-2026-08-23]`

Run with the maintainer's explicit permission to install the CE plugin and remove it afterwards.

**Host:** `SEED BATTLE DESTINY REMASTERED.exe` (PID 7772, UE427) with a **genuine ReShade install**
beside it — `dxgi.dll` 5,255,448 B plus `ReShade.ini`, `ReShade.log`, `reshade-shaders\`.

⚠ **Installing the plugin does NOT need elevation, and the obvious route is a dead end.** Copying into
`C:\Program Files\Cheat Engine\plugins\` requires UAC. Writing an absolute path straight into
`HKCU\Software\Cheat Engine\Plugins64` **looks** like it works — the key keeps the value — but CE
silently ignores it: its own plugin list still showed only AOBMaker and CE-Handwire. What does work is
**CE's own Settings → Plugins → Add new**, whose file dialog accepts a typed absolute path; the entry
then appears as `UE5Dumper.dll:UE5CEDumper` and can be ticked. ▶ Use that route.

⚠ **The plugin's log folder is named after the CE EXECUTABLE.** Running the AVX2 build creates
`Logs\cheatengine-x86_64-SSE4-AVX2\`, not `Logs\cheatengine-x86_64\`. Reading the old folder made it
look like the plugin had failed to load when it had loaded fine — check the folder list by mtime
before concluding anything.

**Step 2 — ✅ PASS, on the case that actually discriminates.**

```
CEPlugin: OnInjectAndConnect triggered
CEPlugin: 'dxgi.dll' is loaded but is not ours
   (path=D:\SteamLibrary\steamapps\common\SEED BATTLE DESTINY REMASTERED\Game_SBDR\Binaries\Win64\dxgi.dll)
   — not a UE5CEDumper proxy
CEPlugin: 'DINPUT8.dll' / 'VERSION.dll' / 'WINMM.dll' / 'dxgi.dll'  … (C:\WINDOWS\SYSTEM32\…)
CEPlugin: Injecting into PID=7772 | DLL=…\UE5Dumper.dll | fn=UE5_AutoStart
```

The **game-folder** wrapper is named explicitly with its full path — the case the pre-fix path rule
(`_strnicmp` against System32 only) would have mistaken for our own proxy — and the run **injected**
rather than reporting the FAIL string *"already loaded … no injection needed"*. The user-facing dialog
said *"DLL injected — GObjects/GNames scan started in the background"*.

⭐ **Injection genuinely succeeded, witnessed from the GAME side rather than from the plugin's own
claim** — `Logs\SEED BATTLE DESTINY REMASTERED\init-0.log`:

```
UE5Dumper DLL loaded | build: 1.0.0.3315 … | process: …\SEED BATTLE DESTINY REMASTERED.exe
UE5_Init: Name sanity: 10/10 objects resolved
UE5_Init: Complete (UE427, GObjects=0x7FF6E5B02550, GNames=0x7FF6E5AC6200, Objects=26113)
UE5_AutoStart: pipe server started, init complete -> initState=2
```

⚠⚠ **A line that looks like a defect and is not — do not re-raise it.** The plugin logs
`InjectDLL returned FALSE` **37 ms after** the DLL had already loaded, then
`post-inject module check: …\UE5Dumper.dll (ok=0)`. Both are correct and deliberate.
`Methode.cpp` documents that `ce_InjectDLL`'s BOOL *"is not the outcome of the injection — it is only
'did an exception escape'"*, and can be **true on a real failure and false while the DLL is loaded and
working**; so the plugin decides by **re-walking the module list** instead. In that log line `present`
prints the found path and `ok=` merely echoes the untrusted BOOL beside it, precisely so the
disagreement is visible. This is the repo's own "decide by looking, not by trusting the flag" rule
working in the field.

**Step 3 — still open, and its premise is probably misdirected.** SBDR's path is ASCII, so this run
did not exercise it. The historic symptom `EVERSPACE? 2` cannot have come from a folder name —
EVERSPACE's install folder is plain ASCII, verified byte-wise — so the `?` came from some other
string. Note the message logs `path=` only, no ProductName, so a repeat needs a game actually
installed under a non-ASCII path.

**Teardown, verified rather than assumed.** CE and the game were killed, the two `00000002 A/B`
values deleted, and the key then compared **programmatically** against the state recorded before the
change — `MATCHES the recorded 'before' state: True` (`AOBMaker` enabled, `CE-Handwire` disabled, and
nothing else). ⚠ CE rewrites `Plugins64` on exit, so the removal must happen **after** CE is closed;
doing it while CE runs would be undone.


### ⚠ NEW 2026-08-24 `[INJECTOWNER-2026-08-24]` (MED) — `inject.py` still uses the pre-fix rule B29 removed from the DLL

Found while staging `[B29-NONASCII-FIXTURE-2026-08-24]`: injecting into a game that has a
third-party ReShade `dxgi.dll` is **refused outright**.

```
STALE MODULE(S) ALREADY MAPPED:
   dxgi.dll  5,455,872 bytes  D:\測試\DumperTest\DumperTest\Binaries\Win64\dxgi.dll
inject.py: FAILED -- refusing to inject. LoadLibraryW would return the module listed above
instead of loading the file you asked for … Use a FRESH process, or pass --allow-stale …
```

**It is a false positive, and the rule is the defect B29 fixed, verbatim.**
`tools/verify/inject.py`'s `already_ours()` says so in its own comment:

```python
# Names that, if already mapped from somewhere other than System32, are ours.
PROXY_NAMES = ("dxgi.dll", "version.dll", "winmm.dll", "dinput8.dll")
```

Compare the pre-fix `Methode.cpp` that `git show 229df1d8^` preserves — *"Case 2: proxy DLL — only
count if NOT loaded from System32/SysWOW64 … → already loaded, no injection needed"*. The DLL was
moved to an **ownership** test (`IsOurModule(modPath)`, version-info based); the rig was not.

**Three things wrong with the outcome, in order of cost:**

1. **The advice cannot work.** *"Use a FRESH process"* is unfollowable — a wrapper in the game folder
   loads on **every** launch, so every future process has it too.
2. **`--allow-stale` is the wrong escape hatch and its message is wrong here.** It prints *"this is a
   refcount bump, NOT a load"*, which is false in this case: the mapped module is `dxgi.dll` and the
   file being injected is `UE5Dumper.dll`, so `LoadLibraryW` performs a **real load**. Verified —
   with `--allow-stale` the injection succeeded and the DLL logged a fresh
   `UE5Dumper DLL loaded | build: 1.0.0.3338`.
3. **It blocks real work on real machines.** SBDR carries the maintainer's ReShade today, and the
   B29 fixture reproduces it deliberately. Any verification run on such a title hits this first.

▶ **Fix shape**: mirror the DLL. Read the mapped module's version info and accept it as ours only if
it identifies as UE5CEDumper — the same predicate `Methode.cpp`'s `IsOurModule` and the C#
`DumperModuleDetector` already implement. Keep the System32 exclusion as a cheap pre-filter, not as
the decision. ⚠ **Keep the guard** — the case it was written for is real and still matters (a stale
`UE5Dumper.dll` mapped from CE's folder, `[STALEDLL-2026-08-18]`); it is the *ownership* test that is
missing, not the guard.

### ⚠⚠ NEW 2026-08-24 `[PROXYREFRESHOWNER-2026-08-24]` (MED-HIGH, **DESTRUCTIVE**) — `proxy_refresh.py` overwrote a third-party ReShade DLL, and I triggered it

Same root cause as `[INJECTOWNER-2026-08-24]` — *decide by filename, not by ownership* — but this one
is far worse, because `inject.py` merely **refused** to act while this one **destroys a file the user
installed**.

**Measured, not theorised: it happened during this session.** Running the row's own prescribed
precondition, `py tools/verify/proxy_refresh.py refresh "OCTOPATH"`:

```
backed up dxgi.dll (5,255,448 B, sha b2945c29e709) -> OCTOPATH_TRAVELER.dxgi.dll.20260824-123841.bak
refreshed OCTOPATH TRAVELER :: dxgi.dll  -> 2,893,824 B  (dist 3343)      <-- ReShade, DESTROYED
backed up winmm.dll (2,903,552 B, sha 681a4221b587) -> …winmm.dll.20260824-123841.bak
refreshed OCTOPATH TRAVELER :: winmm.dll -> 2,906,112 B  (dist 3343)      <-- ours, correct
```

⭐ **And I had verified the ProductName thirty seconds earlier**, which is what makes this a clean
demonstration rather than a near miss:

```
dxgi.dll   5255448 B  ProductName='ReShade'      OURS=False
winmm.dll  2903552 B  ProductName='UE5CEDumper'  OURS=True
```

The rig overwrote the `ReShade` file anyway, because `refresh()` selected on `q.name.lower()` against
`dist_map()` — a **filename** match — with no ownership test anywhere in the path.

**Restored byte-identically** from the rig's own backup: 5,255,448 B, sha `b2945c29e7095491`,
`ProductName='ReShade'`. ⚠ **That recovery was luck, not design** — it worked only because the rig
happens to back up before overwriting. Nothing in the flow *required* a recoverable copy to exist,
and a user with `out/proxy-backups` cleaned would have lost their ReShade install silently.

**Fix applied.** An ownership gate before the overwrite, using the shared helper rather than a
reimplementation, so `Methode.cpp`'s `IsOurModule`, `inject.py` and this rig cannot drift:

```
⛔ SKIPPED OCTOPATH TRAVELER: dxgi.dll is NOT ours (ProductName='ReShade') — a third-party wrapper, left untouched
   OCTOPATH TRAVELER: winmm.dll already current — nothing to do
```

Shown able to fail in the strongest possible way: **the pre-fix behaviour was measured on the real
file**, and the post-fix run leaves it byte-identical.

⚠ **THE HAZARD WAS LIVE, not hypothetical.** A survey of every deployed proxy on this machine:

| | |
|---|---|
| ours | **10** |
| **third-party** | **1** — `OCTOPATH TRAVELER\…\dxgi.dll`, ProductName `ReShade`, 5,255,448 B |

So an **unscoped** `proxy_refresh.py refresh` — or any future scoped run naming OCTOPATH — would have
destroyed it. It is also exactly the file `[B29-PRODUCTNAME-2026-08-23]` relies on as its
third-party fixture, and the same one `b29_nonascii_fixture.py` copies.

⭐ **The pattern, for the third time today.** `IsOurModule` was added to the DLL when `B29` showed a
name test cannot tell our proxy from a wrapper. The **rigs never followed**: `inject.py` still refused
on any wrapper (`[INJECTOWNER]`), `call_export.py` still cannot see our DLL in proxy mode because it
looks for the literal name `UE5Dumper.dll` (`[B5-MAILBOX-2026-08-24]`), and `proxy_refresh.py`
overwrote one. ▶ **When a predicate is fixed in the product, grep `tools/verify/` for the old rule
the same day** — three separate rigs carried it for months after the product stopped.

### ✅ GObjects layout fix (build 2782) — DragonSword — **CLOSED 2026-08-23**，證據見 `[DSLAYOUT-BASEANCHOR-2026-08-23]` 一節（grep 該 tag）；原 PARTIAL 剩餘項 —— base anchor 命中時要選到 UE5-Extended 而非 relaxed B

*優先度 **低** · 需要：DragonSword Awakening，且該次啟動剛好從 FUObjectArray base anchor（位址結尾 …F8B0）解出 GObjects；結尾 …F8C0 的那次不算數。*

| # | 做什麼 | 預期 |
|---|---|---|
| 1 | 啟動 DragonSword Awakening 並注入，於 scan-0.log／offsets-0.log 找出 GObjects 解析到的位址。 | 位址結尾是 …F8B0（base anchor）。若是 …F8C0 則本次不具驗證力，直接結束、下次再試。<br>⚠ 這個 anchor 每次啟動不固定，不能靠重跑同一次判定；沒命中 …F8B0 就不要記成通過。 |
| 2 | 確認同一份 log 中的 preset 行內容。 | 讀到 preset UE5-Extended，不是 relaxed B。 |
| 3 | 回歸檢查：對其他原本就能解析成功的測試遊戲各注入一次，grep log 中的 Could not detect layout, using default。 | 完全沒有這一行；原本能解出的 layout 仍照舊解出。 |

### ⬜ G12（heuristic 分支）—— 走 fallback 時 offset 仍正確

*build 3119 · 優先度 **低** · 需要：offset 驗證走 heuristic fallback 的遊戲：scan-0.log / offsets-0.log 出現 Cannot find Guid or Vector struct（Solarpunk 是紀錄中的案例，但後續 build 可能改走 Guid）。*

| # | 做什麼 | 預期 |
|---|---|---|
| 1 | 注入候選遊戲後 grep scan-0.log / offsets-0.log 的 Cannot find Guid or Vector struct 與 ValidateAndFixOffsets: Using struct。 | 確認走的是 heuristic fallback，而非 Using struct 'Guid'。<br>⚠ 走到 Guid 分支就等於沒測到，要把實際分支記下來。 |
| 2 | 在該遊戲上用 Live Walker 檢查 enum 名稱與 TArray inner type。 | 兩者皆正確，不再偏移 8 bytes。 |

### ⛔ NO SAMPLE ON THIS MACHINE 2026-08-22 `[EXTRASCAN-NOSAMPLE-2026-08-22]` — V10 —— Extra Scan 找到的結果不會被它自己觸發的 refresh 擦掉

*優先度 **中** · **需要**：一款第一次掃描後 GObjects 或 GWorld **仍未解出**的遊戲。都解得出來就是無樣本可測。*

| # | 做什麼 | 預期 |
|---|--------|------|
| 1 | 按 **Extra Scan**，等它跑完。 | 綠色的「Found: GObjects: 0x…」**留在畫面上**。<br>⚠ 修正前它會出現一瞬間，然後被掃描自己觸發的指標 refresh 擦掉，所以每次成功都看不到結果。 |
| 2 | 掃描**進行中**時去動 **UE version** 那個下拉選單。 | Extra Scan 按鈕在掃描真正結束前都保持 disabled。<br>⚠ 修正前那個下拉只被 `IsApplyingOverride` 擋，所以會在掃描中把 `IsScanning` 清掉，讓人可以再開第二個掃描。 |
| 3 | 對照組：斷線再重連。 | 掃描結果那一區被清空 —— 換一款遊戲不該看到上一款的結果。 |

-----

### (original steps) Y11 —— FIRE 對做不出來的參數型別要老實拒絕

*優先度 **中** · **需要**：一個參數含 `FText`、`TArray` 或 `TMap` 的 UFunction。找不到就是無樣本可測。*

| # | 做什麼 | 預期 |
|---|--------|------|
| 1 | 找一個吃 `FText` 參數的 UFunction，按 **FIRE**（欄位維持預設值 `0`）。 | **被拒絕**，訊息指名 FText。<br>⚠ 全零的 FText 不是空 FText —— 它含一個引擎會 deref 的 `TSharedRef`，送零會當掉。匯出腳本那邊（helper 的 `ftext` 分支）本來就是無條件拒絕，這步是讓 FIRE 給出同一個答案。 |
| 2 | 找一個吃 `TArray`／`TMap`／`TSet`／struct 參數的 UFunction，欄位**不要動**，按 FIRE。 | 正常送出，那個欄位維持**全零**（＝該型別的預設空值）。 |
| 3 | 同一個欄位打進一個值（例如 `42`），再按 FIRE。 | **被拒絕**並說明原因。<br>⚠ 修正前那串文字會被當成 int32 直接寫在結構的 Data 指標上，然後交給 ProcessEvent。 |
| 4 | 對照組：一般的 int／float／FString／指標參數照樣 FIRE。 | 全部照舊可用 —— 這步是確認閘門沒有把支援的型別一起擋掉。 |

-----

-----

### V8 — what the tests already pin, moved out of the 繁中 checklist (2026-08-22)

Migrated verbatim from `pending-verification_zh-TW.md`, whose charter is steps only. It is the
**evidence** for why V8's live check shrank to a single look, and it was the one piece of that
restructure that existed nowhere else — checked before the move (`V8_DataTableDrill_Truncated`,
`V8_ContainerTruncation_FixedCapStatusLine` and `WalkDataTableRows` each returned 0 hits here).

✅ **2026-08-22 重新分類。** 原本三個步驟的實質內容已經全部由測試釘住，不需要遊戲：

| 原步驟 | 由誰保證 |
|---|---|
| 1 麵包屑／標頭／下鑽前的預覽列三處都有「showing 64 of N」 | `AuditL11HonestyTests.V8_DataTableDrill_Truncated_BadgesCrumbHeaderAndStatus` ＋ `V8_SyntheticRowMapField_CarriesBadgeBeforeTheClick` |
| 2 狀態列講固定筆數，且**不**提 Array Limit 滑桿 | 同上的 `Assert.DoesNotContain("Array Limit", vm.StatusText)` ＋ `V8_ContainerTruncation_FixedCapStatusLine_DoesNotMentionTheSlider` |
| 3 對照組：≤64 列時上面那些字一個都不出現 | `V8_DataTableDrill_Complete_SaysNothing`（`Assert.Equal("", vm.StatusText)`） |
| 「64」是不是 DLL 真正的頁大小 | `dll/src/Ubel.h:888` —— `WalkDataTableRows(..., int32_t limit = 64)`。查證即可，不用跑。 |

⚠ **不能因此就刪掉這一列。** 那些測試斷言的是 **ViewModel 的字串**，不是畫面上的像素 —— 正是
「怎麼用這份清單」D0 那一格自己寫下的失效方式，也是同一天 `[PARAMSSORT-2026-08-22]` 撞到的：
快照那句提示 VM 字串完全正確，卻被放在沒有 `TextWrapping` 也沒有 `ToolTip` 的 `TextBlock` 裡，
自己被截斷。

-----

### ⛔ V10 and B18 have NO SAMPLE on this machine — measured 2026-08-22 `[EXTRASCAN-NOSAMPLE-2026-08-22]`

Both rows state their own precondition: V10 needs *"a game where GObjects or GWorld is still
unresolved after the first scan — if both resolve there is nothing to test"*, and B18 needs *"a game
where GObjects cannot be resolved by AOB in one go, otherwise Extra Scan never runs long"*. This
records that the precondition is **not satisfiable here**, rather than leaving them looking untried.

**Direct observation on DumperTest** (injected, UE504, 25,179 objects): the System tab resolves
**all five** — GObjects `GOBJ_ES53_1`, GNames `GNAM_V5`, GWorld `GWLD_TQ_1`, FSparseDelegateStorage,
and the `&GEngine` slot. ⭐ There is **no Extra Scan button at all**; the only thing present is a
dev-only *"Test Extra Scan — simulate scan progress UI (does not actually rescan)"*.

**Whole-machine sweep** of `UE5CEDumper.{Machine}.json` (every host ever scanned, 29 entries) for a
`not_found` resolve method. Four hit — and **none is a usable fixture**, which is why this is a
"no sample" verdict and not a "found one":

| host | gObjects | gWorld | why it does not count |
|---|---|---|---|
| `Solarpunk.exe` | `not_found` | `aob` | the **launcher shim**; the real `SolarpunkSteam-Win64-Shipping.exe` resolves both by AOB |
| `Game.exe` | `not_found` | `not_found` | likewise a shim/launcher, not an engine process |
| `b25a_subfloor.exe` | `not_found` | `not_found` | a **synthetic B25 fixture**, not a game |
| `python.exe` | `not_found` | `not_found` | a rig artefact — the DLL injected into Python |

Every real title resolves: 20 hosts by `aob`, Satisfactory by `symbol`, Avowed's GWorld by
`instance_scan_recovery`. ⇒ Extra Scan cannot be made to run long enough to cancel (B18) or to
produce a result that a refresh could erase (V10).

▶ **What would unblock them:** a genuinely hard title whose GObjects AOB misses — the same class of
game `AE10` and the `Genau RIP decode` row are waiting for. Until one is installed, these two are
*blocked on a fixture*, not on effort.

-----

-----

