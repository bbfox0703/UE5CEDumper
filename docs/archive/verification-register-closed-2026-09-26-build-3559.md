# Archived verification-register units — closed 2026-09-26 (build 3559)

Lifted **byte-identical** out of [`../verification-register.md`](../verification-register.md)'s
`## Pending live-game verification` on 2026-09-26: **4 closed `###` sections** —
473 lines, 37,893 bytes. **Nothing was edited, only moved.**

⛔ **Kept byte-identical on purpose — do not repair anything in here**, including the relative links,
which were written relative to `docs/` and are therefore wrong from this folder (see
[`README.md`](README.md)). The only lines that were not moved are this header.

**How these were selected.** The register's own warning is why the rule is strict — *a heading here
is NOT evidence*: closures are recorded under their own `✅ … [TAG]` blocks, often far from the row
they close, and the original heading is not updated. A unit moved only if ALL of these hold:

1. its heading — or, for a table row, its own status cell — announces its own closure: `✅` with a
   dated `[TAG]` or a closure verb. **No `⬜` or `🟡` heading moved**, so the open-batch count
   `check_derived_counts` derives from them is unchanged;
2. read in context, nothing in its body says its own work is still owed. A line superseded inside
   the same unit (an early *"only row 5 is still open"*, a `PARTIAL` block, a first-sitting `⬜` cell)
   does not block when a later block of that unit closes that exact item; a line about a DIFFERENT
   item does not block when that item is itself closed (`[PASTECRASH]`, `[SDKHDR]`,
   `[CEPATHS-UNPADDED]`);
3. its body records no unexercised leg of its own acceptance that could still be run, no
   unexplained observation or *"not chased further"*, no unfiled lead, and no standing warning
   that lives nowhere else;
4. it is not a pointer stub, a precondition, a policy section or a live ledger header, and it is
   not a carrier of a `tools/check_live_verification.py` key (`FreezeOutcome`'s two carriers stay).

⚠ **No blockquoted evidence sub-block was cut out of an open section.** Most of the register's bytes
sit in `⬜` / `🟡` sections made of `> ### ✅ …` evidence blocks. Those blocks are the evidence
hanging under a still-open heading; moving them would leave that heading with nothing beside it,
which is how a closed check gets run twice. Whether such a batch is now fully closed is a call to
make on the heading, in the register, not here.

**Kept in the register, and why** — the `✅` units a marker-only rule would have taken:

| unit | why it stayed |
|---|---|
| `[VOLUMEROOT-2026-08-19]` | *"Still owed, and small"* — a genuine `mountvol` mount point was never exercised, only a junction |
| `[A1-ENVELOPE-2026-09-05]` | its CE leg is recorded as **not exercised** — a leg of its own acceptance that a host with a `TArray<TSoftObjectPtr>` could still run |
| `[A3-FUNCFLAGS-2026-09-05]` | *"does not close the CE `Mimic: FIND_FUNCTION` leg"* |
| `[SNAPINTERVAL-2026-08-20]` | a Tab-vs-click asymmetry *"still unaccounted for"*, and an unnamed test flake |
| `[LWREFRESH-2026-08-21]` | the scroll half is `⬜ STILL OPEN` |
| `[SOLIDEHOLD-2026-08-22]` | the 繁中 arm (a), *"close the game while moving"*, is recorded as still open |
| `[G2-TIER1-UE5-2026-09-07]` / `[AF25-OPCODE-2026-08-22]` | the same rows' other steps are still open |
| `[PARAMSSORT-2026-08-22]` | *"In-game click-through is still owed"*; its closure (kept too) says Live Walker's Params was not re-run |
| `[G12G3-CLOSE-2026-09-06]` | a log contradiction the code cannot produce, with an unproven hypothesis |
| `[AE4S2-BUSYBAR-2026-08-24]` | an incidental lead, *"not filed as a finding"* (a second UI instance writes `crash.log`) |
| `[CONTAINERCAP-LIVE-2026-08-24]` | `[CAPREFRESH-2026-08-24]`'s fix is *"not applied"*, and the U2 / U1 bullets under it are still `⬜` / `🟡` |
| `[SLOTSYM-2026-08-18]` | *"Leave that half ⬜"* — the registry / recent-files discovery half |
| `[PIPEBUSY]` / `[CLASSTOTAL]`, `[PROXYLOAD]` | pointer stubs (and `PROXYLOAD` is only PART-FIXED) |
| FP3 (row + `#### ✅ FP3 CLOSED`) | fixture deaths *"remain unattributed"*, and a *"worth a look"* left unfiled |
| SW1 · SW2 · SW3 · SW6 · SW7 · SW9 | an unexplained observation · 13 of 14 delivery sites not exercised · the DLL-side half still open · scalar arms unreachable behind the open `[CLASSCACHE-FRONTED-2026-09-09]` · a rig warning found nowhere else · the Phase J reader never executed |
| SW4 · SW5 · SW8 | closed in their own cells, but they sit under a still-`⬜` batch heading whose text names them (*"SW4 is second"*), and SW5's own fix (the red *"not reachable — nothing was pushed"* after CE is killed) was never run live; the batch is the maintainer's call |
| `[A4-508MARKER-2026-09-06]` (+ 507-rung, negative control) | its acceptance requires BOTH sides, and the UI badge leg (508 on the Pointers card and the status bar) is not recorded |
| `[A2-ES2-506-2026-09-05]` | its UI legs (status bar `UE506`, Pointers panel, Live Funcs) are not recorded; the pipe carries the same values, but the row asked for the screen too |
| `[A7-EMPTYBASE-2026-09-05]` | the second negative control of its own acceptance (narrowness: an opaque field-less struct keeps its `Pad_0000`) is not recorded |

The three `##` sections after the register — `## Verification steps migrated from the 繁中 checklist`,
`## 第 2 步` and `## 第 3 步` — are a different queue and were left alone: their `✅` headings are
pointer stubs under 12 lines, still carry an open half or a `🟡` cell, or (U3 / U17) sit beside a
`🟡` twin of the same row that still calls the LWC half open.

**Reviewed before it landed.** An adversarial verifier (workflow `wf_0cde9e50-c55`) re-read every unit a
first cut had selected and sent back six: A2, A4 and A7 (an unrecorded leg of their own acceptance) and
SW4 / SW5 / SW8 (see the table). The AA(B) / FIRE row moved only after its two rig traps, found nowhere
else, were copied to `working-lessons.md` (commit `9ed1958e`).

To find a closure, grep its `[TAG]` across `docs/` — tags have always resolved across files. ⚠
`verification-register.md:NNNN` citations elsewhere are stale from this date; re-grep the tag.

-----

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

### ✅ FIXED + LIVE-VERIFIED 2026-09-05 `[A6-BOOLFIELD-2026-09-05]` — audit A6: derived `0x80` on the shifted host, `0x70` on the stock control

> **CLOSED on the verification PC, build 3371.** Rig:
> [`tools/verify/a6_boolfieldsize.py`](../tools/verify/a6_boolfieldsize.py) — takes the log-folder
> name, so the same command runs both hosts.
>
> ⛔ **This row said "DQ XI S … is not installed, so this row stays blocked on a host." That is no
> longer true — the maintainer installed it on 2026-09-05, and it is on `D:\SteamLibrary`.** The
> row was blocked on *content*, and the content arrived. `tools/verify/fixture_census.py` did not
> see it because the install post-dated the census run, not because of anything about the tool.
>
> | host | `Offset_Internal` | derived `FieldSize` | reading |
> |---|---|---|---|
> | **DQ XI S** (`+0x10` whole-layout shift) | `+0x54` | **`0x80`** | ⭐ **OUTSIDE** the old probe spread `{0x68,0x6C,0x70,0x74,0x78}` — pre-fix **no** probe could reach it |
> | **OCTOPATH TRAVELER** (stock 4.18) | `+0x44` | **`0x70`** | byte-identical to the old default (`0x44 + 0x2C`) — the fix changed nothing where it must not |
>
> Both lines verbatim, `offsets-0.log`, category `DYNO`:
> `ValidateAndFixOffsets: UBoolProperty::FieldSize derived at +0x80 (Offset_Internal +0x54, UE=418)`
> `ValidateAndFixOffsets: UBoolProperty::FieldSize derived at +0x70 (Offset_Internal +0x44, UE=418)`
>
> ✅ **The row predicted `UE=422`; both hosts report `UE=418` — SETTLED 2026-09-06, and the DLL
> was right.** `DRAGON QUEST XI S.exe` carries `++UE4+Release-4.18` in `.rdata`, byte-identical
> to OCTOPATH's tag. `docs/test-games.md:19` ("UE4.22") was wrong and is corrected; `:14`,
> which calls DQ XI S "a known 4.18" and fingerprints OCTOPATH against it, was right all along.
> ⚠ The `+0x10` shifted layout and `UField::Next=+0x38` are a **licensee fork**, not a version
> signal — reading them as "must be newer than 4.18" is what produced the wrong number. The A6
> derivation therefore used the correct band, which strengthens this closure rather than
> qualifying it.
>
> ⛔ **THE ROW'S UI OBSERVABLE WAS IN THE WRONG PANEL, and following it would have produced a false
> FAIL.** It named `ClassStructPanel`, whose DataGrid has exactly five columns — Offset, Name, Type,
> Size, Address (`ClassStructPanel.axaml:108-125`). **It renders no value column at all**, so no
> bool value and no mask can ever appear there. The observable that exists is the **Live Walker**
> Value column: `Ubel.cpp:5162-5170` formats a bool as `"%s (bit %d, mask 0x%02X)"` when the probe
> landed and as a bare `"true"`/`"false"` when it did not — a direct readout, so **one** field
> settles it.
>
> **And the read side is stronger than the row asked for.** Rather than "two siblings with different
> values", both hosts produced a full single-byte **mask ladder** — DQ XI S: 8 distinct masks
> `0x01,0x02,0x04,0x08,0x10,0x20,0x40,0x80` across bits 0-7 on `Actor`; OCTOPATH: 6 on `Widget`,
> *with* differing values (`bIsVariable=true`, `bCreatedByConstructionScript=false`,
> `bIsEnabled=true`). A ladder like that is unreachable with `boolFieldMask == 0`, which is exactly
> the pre-fix state on DQ XI S.

<details><summary>original row (kept — its failure analysis is what made the host choice obvious)</summary>

**FIXED 2026-09-05, NEEDED A LIVE CHECK — audit A6: UBoolProperty::FieldSize is now derived**

⚠ Deliberately not a `###` heading and carrying no ⬜ — `check_derived_counts` counts `^### .*⬜`.

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

</details>

-----

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

