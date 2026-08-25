# todo.md — closed rows, archived 2026-08-25 (build 3356)

> **What this is.** 37 `###` sections lifted **verbatim** out of [todo.md](../todo.md).
> Nothing was edited — the archive convention in this folder is *moved, not rewritten*.
>
> **Why.** `todo.md` had reached **1.19 MB / 15,801 lines**, which is not a plausible size for a
> list of things still to do. 28% of it was closed material.
>
> ⭐⭐ **The selection rule, and the two traps it exists for.**
> A `✅` heading does **NOT** mean a section is finished — the rule is *done-marked heading **AND**
> no "still owed" sentence anywhere in the body*. Applied to the 78 done-marked sections here,
> **30 of them (38%) still had outstanding work**: an unticked step in a table, a *"Fix shape (not
> applied)"* paragraph, an item moved in later that is open, a `▶` instruction to a future session.
> Each of those stayed. The previous pass found 13 such; this one found more, so the rule is not a
> formality.
>
> ⚠ **The second trap is structural and is new to this pass.** Eleven further sections were kept
> back by a guard rather than a judgement: a **bare header whose evidence lives in a neighbouring
> section that is not itself archivable** (several literally say *證據見上一節*), and one whose
> deeper children were not archivable. Lifting those would have left a 4-line husk pointing at
> nothing, or orphaned the evidence. The guard is mechanical: body under 12 lines with a
> non-archivable neighbour, or any unarchivable child.
>
> **Verified, not asserted.** The split was checked by reconstructing the original from the two
> halves and comparing **byte for byte** before anything was written. That check exists because a
> commit earlier in the same session destroyed 165 lines of `todo.md` by anchoring an edit on
> *"replace until the next heading"* — a whole-file operation is exactly where that failure is
> silent, since the result still looks like a plausible document.

-----

### ✅ AD4 step 4 CLOSED 2026-08-23 `[AD4-CONTESTED-2026-08-23]` — the `ON (contested)` state recorded, with three witnesses

The row's last open step wanted `ON (contested)` — the `(want=1, godmode=0, resolvable=true)` triple —
observed at least once, and it was explicit that **eyeballing the badge is the wrong instrument**:
the state is rare in real combat, so watching for it is both the easiest way to miss it and leaves
no evidence. It asked for a pipe poller instead.

Closed on **DumperTest Shipping** (UE 5.4, build 3322) with `tools/verify/ad4_contested.py`. The new
fixture replaces "go and get hit" with `AD4_SetDamageContention(true)`, which makes
`ADumperTestActor::Tick` write `SetCanBeDamaged(true)` on the player pawn every frame.

| phase | samples | result |
|---|---|---|
| **A** negative control — hold, contention **OFF** | **318** | `(1,1,true)` **100.0%**; contested triple **never**; `AD4_GetContestWrites` flat at **0** |
| **B** positive — contention **ON** | 315 | **`(1,0,true)` × 309 = 98.1%**; counter **31 → 513 (+482)** |
| **C** recovery — contention **OFF** again | 312 | `(1,1,true)` **100.0%**; contested triple gone |
| **D** independent witness | — | Solitar's own `re-asserted protection flag(s) (drift #N)` went **0 → 5** in `walk-0.log` |

Run twice; both runs agree.

⭐ **Three independent witnesses, which is why this is not one number agreeing with itself.** The
pipe poll is the DLL reading the pawn's bit; `AD4_GetContestWrites` is the **game** counting its own
writes from the other side; the drift warning is **Solitar** noticing independently of both. A flat
counter while the poll said contested would mean the poll was lying; a contested poll with no drift
line would mean the worker never saw it.

⭐ **The negative control ran FIRST and is the reason B means anything.** 318 consecutive
non-contested samples establish that the detector discriminates rather than being stuck on
"contested" — and phase C shows it goes back, so the transition is observed in both directions.

⚠ **Honest scope, and it is a real limitation.** A per-frame writer is a far harsher contest than
combat. The rig prints this with every result: what is proven is that the state is **real and
detectable**, not that it arises at any particular rate in ordinary play. In real combat the
re-assert worker usually wins, which is exactly why the row was reclassified away from eyeballing.

### ✅ FIXED 2026-08-23 `[DTROWMAP-2026-08-23]` — the DataTable drill-down now finds its OWN RowMap

Fix in `Ubel::ProbeRowMapOffset`. The scan is now bounded by the object and covers all of it:

* starts at `ci.SuperPropertiesSize` (where this class's own storage begins) instead of at the end
  of the reflected fields, so an offset **below** `endReflected` is reachable — which is where
  `RowMap` actually lives in a cooked build;
* ends at `ci.PropertiesSize - 0x38` (a `TSparseArray` is 56 bytes), so it can no longer read past
  the object into a neighbour;
* two passes — the holes between reflected fields first (a non-reflected member can only be in a
  hole), then the claimed bytes as a fallback so a wrong reflected `Size` cannot hide the target;
* the failure log now says it is deliberately **not** widening past the object, and why.

Validation logic untouched: it was never the problem, and it is what keeps a whole-object scan
honest.

**Measured before → after, same fixture, same flavour (DumperTest Shipping, UE 5.4):**

| table | before | after |
|---|---|---|
| `Table_Big` (100 rows) | `row_count 8`, `row_map_offset 240` — *the neighbour's rows* | **`row_count 100`, `row_map_offset 48`**, rows `Row_000…Row_099` |
| `Table_Small` (8 rows) | `"RowMap not found by probing"` | **`row_count 8`, `row_map_offset 48`**, rows `Row_000…Row_007` |

Both now resolve at **48**, the offset `ReadProcessMemory` said was right all along.

-----

### ✅ FOUND + FIXED 2026-08-23 `[DTTEXT-2026-08-23]` — a DataTable's FText column came back blank

Visible only once `[DTROWMAP]` was fixed and the walk was reading the right table. `Caption`
(`TextProperty`) was listed with its type and its raw hex and **no value and no `str_value` at all**:

```
{"hex":"40CA72F5A70100001200000000000000","name":"Caption","offset":24,"size":16,"type":"TextProperty"}
```

The pointer in that hex is valid. `WalkDataTableRows`'s per-field reader simply had no
`TextProperty` branch — it handles `StrProperty` / `Utf8StrProperty` / `AnsiStrProperty` / Object /
Struct / Enum. `WalkInstance` **does** handle it (`Ubel.cpp:5157-5160`), and on the same object in
the same build every one of the actor's eight `Text_*` fields rendered perfectly through
`walk_instance` while the DataTable path rendered none.

⭐ **Audit #4's root cause again, verbatim: *the report and the reality are computed by different
code paths*.** Two readers for one property type, and only one of them was ever exercised by a
DataTable. The fix mirrors `WalkInstance`'s branch line for line, including its `"(empty)"`
`typedValue`, and says in the comment that the two must agree.

⚠ **The failure mode is why this matters more than a missing field would.** The column was still
*listed*, with its type and its hex — so it read as "this row has no caption", not as "we cannot
decode FText here". A blank cell is a fact about the data; a missing decoder is a fact about us.

**After:** `Caption` decodes on all 100 rows — `走一步 0` … `走一步 99`. The string is CJK **by
construction**, so a mis-decode cannot produce it by accident, and it proves the whole chain: a
`\uXXXX`-escaped C++ literal → UHT → a cooked Shipping package → `FText` → `ReadFTextString` → JSON.

-----

### ✅ V1a step 1 CLOSED 2026-08-23 `[V1A-REALLOC-2026-08-23]` — and it found a hole on the way

`tools/verify/v1a_container_realloc.py`. The row predates audit #5's A11 re-anchor, so its wording
("the candidate is discarded") had to be re-derived before it could be judged.
`Radar::RefineContainerAnchor` now distinguishes drop / **repoint** / keep, and a growth realloc is
*supposed* to repoint — which satisfies the row's real requirement (no wrong address) more strongly
than dropping would. PASS was therefore defined as **dropped, or re-pointed to the correct new
address**; FAIL as kept at the old one.

| phase | ground truth (`ReadProcessMemory`, DLL out of the loop) | refine result |
|---|---|---|
| **1** grow by 400 | `Data 0x18B9D96BC40 → 0x18BA77009A0`, `Count 4 → 404` | live candidate **repointed to exactly the new base**; CDO control **survived unchanged** |
| **2** `Empty(0)` | `Data=0x0 Count=0` — buffer released | live candidate **dropped**; CDO control survived |

⭐ **The positive control is free and structural.** `Arr_Churn` is seeded in the **constructor**, so
the **CDO** carries element 0 too — and nothing reallocates a CDO. One scan therefore yields a
moving candidate and a stationary one, refined by the same code in the same call. Without it,
"the candidate is gone" cannot be told from "the refine is a shredder".

⭐⭐ **Phase 2's first run was INCONCLUSIVE, and fixing that is what found the defect.** With an
`Exact` refine, a vanished candidate might have been eliminated by the **value test** rather than by
the anchor policy — the freed block no longer read `7001`, so the run proved nothing about the
policy. The fix is to refine with a predicate that **cannot eliminate anything**:
`Between INT32_MIN..INT32_MAX` matches every `int32`. Under it the byte comparison is a tautology,
so the only thing that can remove a candidate is the anchor rule. No allocator behaviour is involved
and nothing has to be assumed about freed memory.

-----

### ✅ FOUND + FIXED 2026-08-23 `[V1AEMPTY-2026-08-23]` — an emptied container left its candidates on freed memory

`Radar::RefineContainerAnchor` checked, in this order:

```
2. if (elementIndex < 0 || numAtScan < 0 || !dataAtScan || !nowData) return KeepAddress;
3. if (elementIndex >= nowNum)  return Drop;
5. if (nowNum < numAtScan)      return Drop;
6. if (nowData != dataAtScan)   return Repoint;
```

`TArray::Empty(0)` releases the buffer and sets `Data` to `nullptr`. `Macht::ReadTArray` has no
check on `Data`, so `{0,0,0}` reads back **successfully** — and both call sites already `continue`
on `!hs.ok` before this function runs. So arriving with `nowData == 0` always means *"the header
read fine and the buffer is gone"* — **positive evidence**, not missing bookkeeping. Grouping it
with the guard at step 2 returned `KeepAddress` and jumped over **both** Drop rules that would have
caught it (step 3 with `nowNum == 0`, and step 5).

**Fix:** split `!nowData` out into its own `return Drop`, with the reasoning recorded at the site.

⭐ **Demonstrated in both directions, twice over.** Not reasoned about — watched:

* **Unit level.** Four new assertions in `dll_helpers_test.cpp`. With the fix reverted:
  `Pass: 1645  Fail: 4`, and the four are exactly the new ones. With it: `Pass: 1649  Fail: 0`.
  Nothing else moved.
* **Live level.** Same rig, same fixture, two builds. **Pre-fix**: after `Empty(0)` released the
  buffer, the candidate **SURVIVED a permissive refine at `0x288051E3740`** — a freed address.
  **Post-fix**: dropped, CDO control still surviving.

⭐ And the pre-fix run is the one worth keeping: the freed address read `85863968`, **not** the
scanned value — so under an ordinary `Exact` refine it would have been dropped *by luck* and the
defect would have stayed invisible. The permissive predicate is the whole reason it was seen.

-----

### ✅ V8 CLOSED 2026-08-23 `[V8-PAINTED-2026-08-23]` — the three strings are painted; a FOURTH site is clipped

The one thing the C# tests structurally could not answer — *are those strings actually on the
screen* — answered by looking, on the **AOT-trimmed** `dist/UE5DumpUI.exe` (v1.0.0.3332, DLL 3332)
against DumperTest Shipping. Trimmed on purpose: reflection-shaped binding code is exactly what
survives a plain build and fails a trimmed one.

**Live Walker → `DumperTestTable_Big_2` → RowMap. All three, verbatim and unclipped:**

| site | painted |
|---|---|
| breadcrumb | `DumperTestTableBig_2 › RowMap [100 x DumperTestTableRow] ⚠ showing 64 of 100 ›` |
| header | `DataTable<DumperTestTableRow>  RowMap ⚠ showing 64 of 100` |
| status line | `Showing the first 64 of 100 rows — this view is capped at 64 per fetch.` |

The status line does **not** mention the Array Limit slider (a DataTable's cap is fixed at 64 per
fetch, not the slider) — which is what
`V8_ContainerTruncation_FixedCapStatusLine_DoesNotMentionTheSlider` asserts, now confirmed in pixels.
The grid ends at `[63] Row_063`: 64 rows, matching the badge.

⭐ **The negative control was run, not assumed.** `DumperTestTable_Small_1` (8 rows), same session,
same panel: breadcrumb reads `RowMap [8 x DumperTestTableRow]` with **no ⚠**, the header carries no
badge, and there is **no status line at all**. So the badge is not always-on, and its presence on
the 100-row table means something. This is `V8_DataTableDrill_Complete_SaysNothing` on screen.

⭐ **`[DTTEXT-2026-08-23]` also confirmed to the pixels** — expanding `[63] Row_063` renders
`Index 63 / Label Row_063 / Value 163 / Caption 走一步 63`. The CJK reached the screen through
`\uXXXX` C++ literal → UHT → cooked Shipping package → `FText` → `ReadFTextString` → JSON →
Avalonia.

-----

### ✅ FIXED 2026-08-23 `[TYPECOLCLIP-2026-08-23]` — the Type column clipped too, and fixing it nearly broke its sort

The sibling of `[V8PREVIEWCLIP-2026-08-23]`, found *as that fix's negative control*: hovering the
Type cell to prove the Value tooltip was really the new binding showed a column that was equally
clipped (`DataTableRows` → `DataTableRo` at 115 px) and equally silent. Maintainer asked for it too.

**What shipped**

* `Models/LiveFieldValue.cs` — `public string? TypeTooltip`, same null-when-empty shape as
  `ValueTooltip`. ⚠ **No `[NotifyPropertyChangedFor]`, and that is a fact not an omission**:
  `TypeName` is `init`-only and structural (a refresh's same-layout branch checks it *before*
  deciding it may reuse rows), so it cannot change under a live row. `DisplayValue` is the opposite,
  which is why its twin needs nine.
* `Views/LiveWalkerPanel.axaml` — `DataGridTextColumn` → `DataGridTemplateColumn`, because a text
  column's `Binding=` is not an element and there is nothing to hang `ToolTip.Tip` on. Template
  deliberately identical to the Value column beside it so the two cannot drift visually.
* `Views/LiveWalkerPanel.axaml.cs` — **a sort comparer for `TypeName`**. See below.

### ✅ FIXED 2026-08-23 `[V8PREVIEWCLIP-2026-08-23]` — the Value column now offers the text it cannot show

Maintainer picked option **(1)**, the tooltip — the option that repairs the *class* of defect rather
than this one instance.

**What shipped**

* `Models/LiveFieldValue.cs` — new computed `public string? ValueTooltip`, returning
  `DisplayValue` or **`null`** when empty.
* `Views/LiveWalkerPanel.axaml:587-603` — `ToolTip.Tip="{Binding ValueTooltip}"` on the Value
  column's `TextBlock`, beside the existing `Text="{Binding DisplayValue}"`. The `DataTemplate`
  already carries `x:DataType="m:LiveFieldValue"`, so it is a **compiled** binding and AOT-safe;
  verified on the trimmed publish, not just in a Debug run.

⚠ **`null`, not `""`, and that is why it is a separate property.** Avalonia shows a tooltip whenever
`Tip` is non-null, so binding `DisplayValue` straight through would pop an **empty box on every
blank cell**. Matches how the other bound tooltips in this UI already behave (`XrefInfo`,
`ScoreTooltip`) — they are nullable and the binding simply goes quiet.

⚠ **All nine `[NotifyPropertyChangedFor(nameof(DisplayValue))]` now have a `ValueTooltip` twin.**
Without them the tooltip would go stale while the visible text updated — which is *worse* than the
clipping it fixes, because a tooltip reads as authoritative.

**Scope was checked, not assumed.** `ContainerTruncation.BadgeSuffix` has nine call sites; eight
build a **breadcrumb label**, which renders fine (confirmed on screen during
`[V8-PAINTED-2026-08-23]`). Only `DataTableFieldPreview` lands in a grid cell. So this was the one
affected badge site, and the fix needed no wider sweep — though it does now also rescue every other
long value in that column (Map/Set previews, struct previews, long strings).

⭐ **Four tests, and the drift guard is the one that matters.** `LiveFieldValueTooltipTests`:
the badge survives into the tooltip; empty ⇒ `null`; the XAML actually binds it *on the same
element* as `DisplayValue`; and the two notification sets are equal in count. The last is the only
one that stops the fix rotting — a tenth source property added later without its twin.

⭐ **Verified on the AOT-TRIMMED publish, not a Debug run** (v1.0.0.3334, DLL 3334, DumperTest
Shipping) — which is the whole point for a new XAML binding, since binding-shaped code is what
survives untrimmed and fails after trimming. Hovering the RowMap cell pops:

```
{DataTable: 100 rows, DumperTestTableRow} ⚠ showing 64 of 100
```

⭐ **And a negative control that rules out the boring explanation.** Hovering the **Type** cell of
the same row — which is *also* visibly clipped (`DataTableRo` for `DataTableRows`) — shows **no
tooltip at all**. So what appears over the Value cell is this binding, not some grid-wide tooltip
behaviour that was there all along.

ℹ️ The **Type** column clipped the same way. It was left alone here as outside the ask, then
fixed on request the same day — see `[TYPECOLCLIP-2026-08-23]` above, where the conversion turned
out to unroot the column's sort.

⭐ **Both the guard and the fix were shown able to fail.**

* Deleting one mirrored notification → `9 … but 8`, **1 failed / 4704 succeeded**. Restored → 4705.
* ⚠ And the guard **failed on its very first run, for the wrong reason** — `10 vs 9`. The tenth
  "occurrence" was the attribute name written inside a `<c>…</c>` in `LiveFieldValue`'s own doc
  comment, i.e. **the prose documenting the rule broke the check enforcing it**. Now it counts
  *attribute lines* (`line.Trim() == attr`) instead of substring hits. Same shape as the
  `MG2_RemoveOneMapEntry decl=2` miscount earlier the same day and as the `v8_datatable_cap.py`
  regex that matched another command's `limit` default: **a detector has to be right about WHERE it
  reads, not only about what it matches.** Three instances in one day is a pattern worth naming.

### ✅ AA2/AA3 step 4 CLOSED 2026-08-23 `[AA2-STEP4-CHURN-2026-08-23]` — the freeze re-acquires across churn, and touches nothing else

The step steps 2 and 3 structurally could not reach. Step 2 proved a class-wide freeze holds on a
**quiescent** 145-instance pool and said so; the guard being tested — *re-read `ClassPrivate` before
every write, refuse a foreign class* — only matters when a **slot is recycled**, and no commercial
game produces that on cue. The spawner does: `Spawn_DestroyHolders()` is
`Destroy()` + `Empty()` + **`ForceGarbageCollection(true)`**, so slots are genuinely freed and reused.

CE 7.7 + AOBMaker + DumperTest Shipping, build 3334. Freeze pushed the normal way (Property Search →
Freeze → *"every live DumperTestHolder and every subclass (1 inherit this…)"* → value 9999) and
ticked. `tools/verify/aa2_step4_churn.py` drives the churn.

| | |
|---|---|
| precondition | 12/12 sampled holders holding 9999 **before** the churn — otherwise a post-churn reading measures nothing |
| churn | destroy 100 + force GC → **0 live**; re-spawn 8 decoys, then 100 fresh holders |
| **re-acquire** | **4.1 s** (row allows ~5 s). Across five runs: 2.0 s, 3.1 s, 4.1 s, and 100/100 frozen in three further trials |
| nothing unrelated | decoys **8/8 untouched**; **176 unrelated fields** across 10 objects, **0 changed** |

⭐ **The intermediate readings are the evidence, not the endpoint.** Fresh instances are seeded
`1000+i`, so the poll shows them converting:

```
t=+1.0s  11/12 hold 9999   e.g. ['1107', '9999', '9999', '9999']
t=+4.1s  12/12 hold 9999   e.g. ['9999', '9999', '9999', '9999']
```

A post-churn 9999 therefore cannot be a value that was already there — the objects did not exist a
second earlier and were born at 1107.

⭐ **The decoy control is adversarial by construction.** `ADumperTestHolderDecoy` does **not** derive
from the frozen class, but its class NAME contains it and it carries a field of the **same name at
the same offset (`0x290`)**. A write by name, or a write into a recycled slot without re-checking
`ClassPrivate`, lands there. It never did.

⭐⭐ **Slot reuse was forced, not hoped for.** A dedicated run destroyed 100 frozen holders and then
spawned 24 decoys: **24 of 24 landed on addresses that had just held a frozen holder**, and **all 24
read `-1`**. That is the AA2 defect's exact scenario — a foreign class in a recycled, recently-frozen
slot — and the guard held.

⚠ **My own rig produced a vacuous control on its first run, and it is worth recording.** It reported
*"decoys: 0 checked, 0 changed"* and **scored that PASS** — because `Spawn_Decoys` adds decoys to the
same `SpawnedHolders` array that `Spawn_DestroyHolders` empties, so the churn destroyed the control
along with the subject. An empty set cannot fail. The rig now re-spawns the decoys **before** the
holders (so they are live for the whole re-acquisition window) and **refuses to score an empty
control**. Same trap I had been checking other people's work for all day.

### ✅ AA12/AA13 step 3 CLOSED 2026-08-23 `[AA12-STEP3-EMPTY-2026-08-23]` — the legitimate empty case, with a negative control

The row's hardest step, and the one the previous attempt could not stage. It needed *"a class with
**zero live instances right now**, including subclasses"*, then *"make one spawn and confirm the
freeze takes hold within ~5 s"*. `NiagaraComponent` looked right and turned out to have two live
instances, so the empty case was never actually exercised.

`UDumperTestLateSpawn` is that class by construction, and `Spawn_LateInstance()` is the second half
no commercial game gives you on cue. CE 7.7 + AOBMaker plugin + DumperTest Shipping, build 3334.

| the row asks | observed |
|---|---|
| zero live instances, incl. subclasses | pipe pre-check: `find_instances exact_match=true` → CDO only, **0 live** |
| record **stays ticked** | ✅ ticked (CE's red-X active marker) and still ticked afterwards |
| window **stays open** | ✅ Lua Engine window open |
| the line | ✅ **`[Freeze] armed: no live instances of DumperTestLateSpawn (or any subclass) right now -- the freeze applies as they spawn.`** — it names the subclass condition itself |
| then spawn one, freeze takes hold ≤5 s | ✅ **1.0 s** |

⭐⭐ **The discriminator is a number the game and the freeze disagree on.** `Spawn_LateInstance` seeds
`LateValue = 5000 + count`; the freeze forces **9999**. So "the freeze took hold" is not a reading
that could have been true anyway.

⭐ **Negative control run, not asserted.** Untick the record, spawn again:

```
0x17A78C40D60  LateValue = 5001   <- NEW, freeze off   (5000 + 1, the game's own value)
0x17A78C47810  LateValue = 9999   (from the frozen run)
```

The game's natural value is demonstrably not 9999, and the frozen instance kept its forced value.
That is what makes the 9999 attributable to the freeze rather than to the fixture.

ℹ️ **The armed-empty path also re-demonstrated the bail-out rule on the way in.** Ticking before the
helper was present produced `showMessage: [Freeze] ue5_freeze_helper.lua not found in this table.`
**and left the record unticked** — CLAUDE.md's *"a bail-out that applied NOTHING must untick the
record"*, observed. Step 3's requirement is the opposite (armed-but-empty must **stay** ticked), and
both behaviours were seen in the same session, which is the pair that makes either meaningful.

-----

### ✅ MB3's THROW PATH CLOSED 2026-08-23 `[MB3-THROW-2026-08-23]` — "no way to force one on demand" is overturned

The row above said *"Needs a handler that actually throws — no way to force one on demand today"*,
which is why it sat in bucket **C**. A staged, `#ifdef`-guarded `case 16:` that throws makes it
deterministic. DumperTest dev, staged DLL over build 3337, driven with the **existing**
`tools/verify/mailbox_poke.py --cmd 16` — no new rig.

| check | observed |
|---|---|
| the throw is reported | `cmd=16 -> result=-11`, `err='command handler threw — the operation did NOT complete'` |
| ⭐ **the mailbox keeps polling** | next dispatch `GWORLD result=0`, `GAME_ENGINE result=0` |
| repeatable, not one-shot luck | throw → `-11`, ordinary → `0`, again |
| ⭐ **second, independent oracle** | `[WARN] Mailbox: tick threw (UE5_TEST_THROW) — skipping` — it names the staged sentinel, so the throw is attributable to *this* case and not to anything else |

Both halves of the row's stated expectation therefore hold: the poller survives **and** the script
sees `-11` + "the operation did NOT complete".

`MB_ERR_HANDLER_THREW = -11` ([Mimic.cpp:119](dll/src/Mimic.cpp:119)). ⭐ Safe to stage **because it
was checked first**: `Routine::RunTickGuarded` ([Routine.h:96](dll/src/Routine.h:96)) catches
`const std::exception&` **and** `...`. An uncaught C++ throw would have `std::terminate`d the game.
⭐ `mailbox_poke` already poisons `OFF_RESULT` with `0x7FFFFFFF` before every round trip, so a `0`
must be genuinely **written** rather than read stale — the anti-vacuity was built in.

**The stage and its revert.** Six inserted lines in `Mimic.cpp` only: `#define UE5_TEST_THROW` +
`#include <stdexcept>` after the `<exception>` include, and an `#ifdef`-guarded
`case 16: throw std::runtime_error("UE5_TEST_THROW");` between `case CMD_TIME:`'s `break;` and the
`default:`. Cmd 16 is deliberately **NOT** added to `Mimic.h`, so `check_mailbox_contract.py` stayed
green before and after. Reverted from a **byte snapshot** (identical, 65,541 bytes), not
`git checkout`.

⚠⚠ **The revert was verified in the BINARY, not in `git status`.** `dist/` is gitignored, so a clean
tree says nothing about the shipped DLL. After rebuilding, `--cmd 16` returns
**`-1 / 'Unknown command'`** — the `default:` branch — instead of `-11`. That is the proof the staged
case is gone from what actually ships.

#### ✅ THE SCROLL HALF IS FIXED, 2026-08-21 — causation measured, two designs killed by measurement

**Causation, by A/B rather than argument.** Same fixture, same start row, same three Refreshes:

| the refresh loop | top row after ×1 / ×2 / ×3 |
|---|---|
| `Fields[i] = newFields[i]` (shipped) | `0x8B` → `0x8A` → `0x8A` — drifts one row up each time |
| **assignment disabled entirely** | `0x8D` → `0x8D` → `0x8D` — **zero drift** |

That is the whole diagnosis: the row *replacement* is the cause, not the restore path, not a
competing anchor. A second control agrees — at the very **top** of the list (offset 0, nothing to
scroll up into) three Refreshes move nothing.

⛔ **TWO designs were built or spiked and killed BY MEASUREMENT. Do not re-propose either.**
1. **Capture-and-restore the top row** (via the existing `CaptureViewAnchor` / `RestoreBookmarkView`
   pair). Built, unit-pinned with 7 tests and a working negative control, **and changed nothing on
   screen** — because the restore ends in `grid.ScrollIntoView`, which means *"make visible"*, not
   *"put at top"*, and is a no-op for a row still inside the viewport. This kills the entire
   anchor-and-restore family for any drift smaller than one screen.
2. **A single Reset** (`Clear` + re-`Add` on the same-layout branch). Spiked and measured: it
   **jumps the grid to the very top** (`0x8D` → `0x30`) and stays there — strictly worse than a
   one-row drift, and it would make auto-refresh unusable.

**The fix: the row objects survive the refresh.** `UpdateDisplay` now calls
`Fields[i].CopyLiveValuesFrom(newFields[i])` instead of assigning, so the collection raises nothing
and the grid never moves. `LiveFieldValue` became `ObservableObject` for **exactly** the members
that can differ between two walks of the same object — `HexValue`, `TypedValue`, `PtrAddress`,
`PtrName`, `PtrClassName`, `ArrayCount`, `MapCount`, `SetCount`, `EnumName`, `FieldAddress`,
`IsSearchMatch` — with `[NotifyPropertyChangedFor]` on the computed `DisplayValue`/`EditableValue`.
The structural members (`Name`, `Offset`, `TypeName`, the navigability flags) stay `init` **on
purpose**: they are precisely what the same-layout branch checks before deciding it may reuse rows
at all, so making them mutable would undermine the guard the fix rests on.

⚠ **The imperative row painter had to be DELETED, not kept as a fallback.**
`FieldGrid_LoadingRow` set `Row.Background`/`Foreground` on *realization*, which cannot see a
property change on a row that is never re-realized. It is replaced by a bound
`<Style Selector="DataGridRow" x:DataType="m:LiveFieldValue">` using a new `BoolToBrushConverter`.
The reason it could not simply be left in place is a priority rule worth remembering:
**`e.Row.Background = …` writes at LocalValue priority, which outranks a Style setter**, so even its
`Transparent` branch would have pinned every non-matching row and the binding would never have
shown at all.

⚠ **One guarantee was re-established by hand.** The editing latch was cleared by the `Replace`
notification this loop no longer raises, so the copy path clears `IsEditing` explicitly. It may now
be over-cautious rather than necessary — the latch was stranded because Avalonia tears an open
editor down when the *row object* is replaced, and it no longer is — but a stranded `true` vetoes
every auto-refresh tick for the rest of the session, so the old safe behaviour is kept rather than
betting on the new one. `RebuildingTheGridClearsAStrandedEditingLatch` caught this.

**LIVE-VERIFIED on the AOT build, four separate checks:**

| check | result |
|---|---|
| scroll drift | **5 Refreshes, top row unmoved** at `0x8D CreationMethod` |
| values still update | `TickCount` **274 → 300 → 307 → 314**, Hex tracking (`2C010000 → 33010000 → 3A010000`) |
| search highlight | `GravityScale` tinted, **survives two Refreshes** (the U1/V6 guarantee) |
| no stale highlight | keyword switched to `MaxWalkSpeed` + Refresh → the new row is tinted and `GravityScale` is **not** |

⭐ The value check is the one that matters most: reusing row objects is only correct if the cells
still repaint, and 274→314 with the Hex column tracking is that, on screen.

*(The selection half was fixed earlier the same day and is unchanged.)*


### ✅ VERIFIED 2026-08-20 `[AUTOREFRESH-2026-08-19]` — steps 1-7 all pass; — Live Walker auto-refresh: the countdown can no longer freeze, and it comes back after a reconnect

*Reported by the maintainer from their own session on the **other PC**, running dist **1.0.0.3262**:
"Live Walker `Auto` refresh 無效，秒數數到0後就停在那" — the countdown runs down to 0 and sits there
while nothing refreshes. **Classification: B** (needs the UI + a game on screen, no human judgement —
Auto + computer-use can drive it).*

> **⚠ Read the evidence split before trusting any of this.** The logs are from a **different machine
> running 3262** and carry **none** of 2026-08-19's commits, so they are evidence about 3262 only.
> The auto-refresh block was re-checked and is **byte-identical** between `021053d6` (the last
> 2026-08-18 commit) and the dev tree, so code reading of it does transfer; nothing else does.
>
> **Log-proven** (`Y:\UE5DumpUI\pipe-0_005/006.log`, `view-0.log`, one UI session 12:27:16–12:55:44):
> - Auto-refresh issued **zero** refreshes. Every `walk_instance` in the 21-minute Elliot half maps
>   1:1 to a user action in `view-0.log`; the gaps between repeats of the same address are 7 s, 11 s,
>   187 s, 6 s, 187 s, 1.6 s, 26 s — **no periodic cadence exists anywhere in the session**. (1.6 s is
>   below `MinAutoRefreshIntervalSec`=6, so those cannot be ticks.)
> - **Not a dead dispatcher and not a dead pipe.** The Teleport poll's own `DispatcherTimer` ran at a
>   flawless ~500 ms (117–119 `teleport_get_pov`/min) for every minute from 12:36 to 12:55. A
>   negative control we got for free.
> - **Zero ERROR and zero WARN** in the UI logs for the whole window. The panel did nothing and said
>   nothing.
> - A real disconnect DID happen mid-session (12:33:17.109 `Pipe: ReadLine returned null`), 58.8 s
>   before the reconnect to a **different game** at 12:34:15.9. It is logged **only** in the pipe log
>   — `init-0.log` and `view-0.log` jump straight from one game to the next with no disconnect line
>   at all.
>
> **Code-proven** (reading 3262's source, which is identical here): `_countdownRemaining` was reset in
> exactly ONE place — inside `OnAutoRefreshTick`, *past* its early-return guard. `OnCountdownTick`
> decremented and clamped at 0. So **any** condition that keeps skipping the tick pins the label at
> `sec · 0s` forever while the Auto toggle still reads ON. `RefreshAsync` catches `Exception`
> internally, so a *failing* refresh could not have caused this — only a *skipped* one.
>
> **Narrowed by evidence, NOT proven — the one thing a live run still has to settle.** The guard had
> four conditions and three are excluded: `_isAutoRefreshing_InProgress` would have rendered
> `sec · refreshing...` instead of a number; `!HasData` / empty `CurrentAddress` are contradicted by
> three manual refreshes at 12:51:33 / 12:51:34 / 12:52:01 that DID walk `0x3A0F60240` (`RefreshAsync`
> returns immediately on an empty address). That leaves **`_isEditing`**, a latch set from
> `DataGridBeginningEditEventArgs` and cleared **only** from `CellEditEnded` — which Avalonia does not
> raise when it tears an edit down because the rows were replaced (`CancelEdit(…, raiseEvents:false)`),
> i.e. exactly what a Refresh or a navigation does to the field grid. **How it actually got stuck is
> not established offline** and no log records it.

*Fixed by making the whole class of failure impossible rather than betting on that last inference —
three changes, each independently unit-pinned:*

1. **The countdown cannot freeze.** New pure `Helpers/AutoRefreshCadence.cs` owns the rule; the
   counter **re-arms at zero** because it displays the timer's PERIOD, which keeps elapsing whether or
   not the last tick did any work. The reset also moved into `OnAutoRefreshTick`'s `finally`, so a
   future throwing `RefreshAsync` cannot strand it either.
2. **A skipped tick says WHY.** `AutoRefreshSkip` is surfaced in the status text — `paused (editing)`
   / `paused (no data)` — so "suppressed on purpose" can no longer be mistaken for "broken". And the
   `_isEditing` latch is now cleared wherever the grid is rebuilt (`UpdateDisplay`,
   `ClearDisplayedNode`, `ClearOnDisconnect`), which is where it could get stranded.
3. **It comes back.** A stop caused by something outside the user's control — the pipe dropping
   (audit X5's `ClearOnDisconnect`) or switching away from the tab — is now *resumable*, and the panel
   re-arms from `UpdateDisplay` once it is rooted on data again. A **user untick** and every
   **navigation re-root** deliberately do not resume. ⚠ The pending flag is only written by a stop
   that actually stopped something, because `NavigateToAddressAsync` calls the non-resumable overload
   on its way in — writing it unconditionally would have eaten the resume in exactly the path the
   maintainer walked (disconnect → reconnect → navigate).

> Tests: `AutoRefreshCadenceTests` (13). Shown able to fail: reverting the three behaviours to 3262's
> and re-running the class **fails 7 of them**, and the two "must not change" controls (user untick,
> navigation re-root) stay green through both.
>
> | step | do this | expect | why it is a real check |
> |---|---|---|---|
> ### ✅ STEP 1 PASSES 2026-08-20 `[AUTOREFRESH-LIVE-2026-08-20]` — measured on the wire, not just on screen
>
> DumperTest Development + the AOT `dist` UI, Live Walker rooted on **GWorld** (`UWorld
> ThirdPersonMap`), **Auto** ticked, interval 10 s.
>
> * **The countdown cycles and re-arms.** Sampled every 4 s: `6s → 4s → 8s → 3s → 8s → 3s`. It
>   counts down and **wraps back up at least three times** in 20 s. The reported 3262 failure was
>   *"秒數數到0後就停在那"* — it stops at 0 forever. It demonstrably does not.
> * ⭐ **And the refreshes are REAL, measured to 0.1 s.** The UI's own `pipe-0.log` shows **13
>   `walk_world` requests**, and after the initial manual root (a 40.9 s gap) the gaps are:
>   `10.0, 10.0, 10.0, 10.0, 10.0, 10.0, 10.0, 10.0, 10.0, 10.0, 10.0` — **eleven consecutive ticks
>   at exactly the configured interval**, still running when the session ended.
>   This is the **exact inverse of the original diagnosis**, which was *"Auto-refresh issued **zero**
>   refreshes … no periodic cadence exists anywhere in the session"*. Same measurement, opposite
>   result.
>
> ⚠ **A trap worth carrying: the refresh command depends on the ROOT.** A GWorld-rooted view
> refreshes with **`walk_world`**, not `walk_instance`. Grepping for `walk_instance` (which is what
> the original investigation counted, correctly, for an *instance*-rooted view) returns **0** here
> and reads exactly like the bug. Check which command the current root actually issues before
> concluding an absence.
>
> Steps 2 and 4 are settled below; with 1/3/5/6/7 above, **all seven steps of this row are done**.

> ### ✅ STEPS 3, 5, 6, 7 PASS 2026-08-20 `[AUTOREFRESH-LIVE2-2026-08-20]` — every one measured on the wire
>
> Continues the run above on a **second** host and a **second** root type, so step 1 is now confirmed
> for an *instance*-rooted view too, not just GWorld. Throughout, the label was never trusted on its
> own: each verdict is the UI's `pipe-0.log` walk cadence.
>
> **Step 6 (non-regression) — first half PASS, and the second half's EXPECTATION IS WRONG.**
> * *user untick*: Auto ON → `sec · 6s`; untick → the button unlights and the label returns to plain
>   `sec`, still off 6 s later. **The untick sticks.** ✅
> * *drill into a child*: the row expects "stays OFF". It does **not** stay off — and it should not.
>   The `→` button is `NavigateToFieldCommand` → `NavigateToFieldAsync`, which is **not** one of the
>   six methods that call `StopAutoRefreshTimer` (`StartFromWorld`, `StartFromGameEngine`,
>   `NavigateToAddress`, `LocateInGWorld`, `LocateContainerInGWorld`, `LoadBookmark`). A field drill
>   never stops the timer, so "does not resume" never applies to it. What actually happens is the
>   useful thing, and it is correct on the wire — auto-refresh **re-targets to the new root**:
> ```
> 11:27:49.293  walk_instance 0x1C2444DBD80   <- the manual drill into PersistentLevel
> 11:27:54.857  walk_instance 0x1C2444DBD80   gap  5.6 s   (mid-cycle)
> 11:28:04.863 … 11:29:44.956                 gap 10.0 s × 12 consecutive
> ```
> It never walks the stale GWorld root again. ⚠ **Fix the row, not the code**: the fix note says a
> navigation re-root "does not *resume*", which is a statement about what happens after a stop — the
> step turned that into "must be off", which the code never did and the fix never claimed.
>
> **Step 5 (tab-leave / return) — PASS, and it genuinely stopped in between.** Auto ON, switch to
> Instances for 8 s, come back: the label reads `sec · 4s` and is running. The label alone would not
> settle it — a timer that never stopped looks identical. The wire does: ticks run at 10.0 s up to
> `11:30:04.971`, then **one 25.9 s gap** across the absence, then `11:30:30.838` and back to 10.0 s.
>
> **Step 7 (disconnect, empty panel) — PASS, and this is also X5's second row, auto-refresh half.**
> Game B killed at **11:31:04.943** with Auto ON and ticking. In the next 20 s the UI's pipe log
> gained **5 lines total** — the disconnect sequence — and **0 walk attempts**. No re-walk against a
> dead pipe, no spam. The Auto control itself is *hidden* while the panel has no data, so "the label
> reads `sec`" cannot be observed as written; the substantive half (nothing polls) is what was
> measured. Nothing polled for the **full 2 minutes** until a navigation.
>
> **Step 3 (THE MAINTAINER'S PATH) — PASS.** Killed game B (Shipping), started a **different** game A
> (Development), reconnected, and navigated. **Auto came back on by itself** — no click on the
> toggle:
> ```
> 11:31:04.9  game B killed, Auto was ON        -> 0 walks for 121 s
> 11:33:05.4  Start from GWorld (the navigate)  -> walk_world
> 11:33:15.5 … 11:34:05.5                       -> walk_world, gap 10.0 s × 6 consecutive
> ```
> ⭐ Both halves matter and they pull in opposite directions: it must **not** poll while
> disconnected *and* must **not** stay silently off afterwards. Pre-fix it stayed off for the rest of
> the session. Reconnecting also **reloaded game A's bookmark** into slot 1 (`ThirdPersonMap`) by
> itself, which is the same per-game store `X5` checked from the other side.
> ### ✅ STEP 2 PASSES 2026-08-20 — the suspected ORIGINAL trigger, and the pause is bounded
>
> This is the row that matters most in this block: `_isEditing` stranded by an editor torn down
> without a `CellEditEnded` is the mechanism the offline analysis narrowed to but **could not prove**.
> Driven exactly as written — open an editor, then navigate away **while it is still open**.
>
> 1. Rooted on `DirectionalLightComponent`, Auto ON and ticking at 10.0 s.
> 2. Double-clicked the `UCSSerializationIndex` (`IntProperty`, `-1`) value cell → the in-cell editor
>    opens and the label becomes **`sec · paused (editing)`**. ✅ The skip now *says why*, which is the
>    whole point of fix item 2 — before, a suppressed tick and a broken timer looked identical.
> 3. Clicked the **`GWorld` breadcrumb with the editor still open** — the teardown path Avalonia does
>    not raise `CellEditEnded` for.
> 4. The label is a **live countdown again (`sec · 9s`)**, not stuck on `paused (editing)`.
>
> ⭐ **And the pause is BOUNDED, measured on the wire — it did not merely look right on screen:**
> ```
> 11:35:45.565  walk_instance 0x1BCE29BA030   gap  2.3 s
> 11:35:55.568  walk_instance 0x1BCE29BA030   gap 10.0 s   <- last tick before the editor opened
> 11:36:25.574  walk_world    (root)          gap 30.0 s   <- exactly TWO skipped ticks, then resumed
> 11:36:35.575 … 11:37:05.592                 gap 10.0 s × 4
> ```
> Two things the label could not have given: the gap is **exactly 3 × the interval**, so precisely the
> ticks falling inside the editing window were skipped and no more; and `11:35:55.568 → 11:36:25.574`
> is **30.006 s**, i.e. the timer kept its phase straight through the pause. That is the re-arming
> counter of fix item 1 — it displays the *period*, which elapses whether or not the tick did work.
> The pre-fix failure was an **unbounded** pause; this one closed itself in two ticks.
>
> ⚠ No value was committed: the editor was abandoned by navigating, not by pressing Enter, so nothing
> was written to the game.

> ### ✅ STEP 4 PASSES 2026-08-20 — the proxy two-step, and the resume provably ignores the CONNECT
>
> Run on **Elliot** because it is a real proxy-mode title (`dxgi.dll` auto-loads, DLL starts the pipe
> and does **not** scan). Sequence: root Live Walker on Elliot's GWorld → **Auto ON** and ticking →
> `taskkill` Elliot → relaunch → **Connect** (`Connected — waiting for scan`) → **Start Scan**
> (84,990 objects) → **Start from GWorld**.
>
> ```
> 12:09:xx  Elliot killed with Auto ON  -> 0 walks for ~3 min, INCLUDING across
>                                          Connect and the whole Start Scan
> 12:12:29.863  Start from GWorld (the navigate)  -> walk_world
> 12:12:39.890 … 12:13:19.908                     -> walk_world, gap 10.0 s × 5
> ```
>
> ⭐ **The zero-walk stretch is the assertion, not the resume.** Step 3 already showed Auto comes
> back; what step 4 adds is that the two-step proxy path has *two* plausible wrong moments to resume
> at — the `Connected — waiting for scan` transition and the `Start Scan` completion — and it fired at
> **neither**. Auto re-armed only when the panel was re-rooted on data. That is exactly what the fix
> note claims ("the resume hangs off data being re-rooted, not off the connect event, precisely so
> the two-step proxy path behaves identically") and it is the reason the maintainer's
> disconnect→reconnect→navigate path works.
>
> **With this, steps 1–7 of `[AUTOREFRESH-2026-08-19]` are all verified.**

> | 1 ⚠ THE ONE THAT MATTERS | Live Walker → root on any live object → tick **Auto** → watch for 3 full intervals | the countdown cycles `10…1` and repeats, and the grid's values actually change | the reported failure is the counter reaching 0 and never moving again |
> | 2 | while Auto runs, double-click an editable scalar cell to open its editor, then click a breadcrumb to navigate away | the label reads `sec · paused (editing)` only while the editor is open, and auto-refresh resumes by itself afterwards — it must NOT stay paused | this is the suspected original trigger; a stranded latch used to kill Auto for the whole session |
> | 3 ⚠ THE RECONNECT, THE MAINTAINER'S PATH | with Auto ON, close the game (do not close the UI), start a **different** game, let it connect, then navigate to any object | Auto is off while disconnected (X5 — it must not walk a dead pipe or the old game's addresses) and **comes back on by itself** once the new object is showing | pre-fix it stayed off silently for the rest of the session |
> | 4 | repeat step 3 but go through **proxy mode** (`Connected (proxy mode — scan not yet triggered)` → `Connected:`) | same result | the resume hangs off data being re-rooted, not off the connect event, precisely so the two-step proxy path behaves identically — worth confirming rather than assuming |
> | 5 | switch to another tab with Auto on, then switch back | Auto is running again | tab-leave is now resumable too |
> | 6 ⚠ NON-REGRESSION | tick Auto, then **untick** it; separately, tick Auto then drill into a child object | stays OFF in both cases | only the pipe and the tab may re-arm it; a user's untick must stick |
> | 7 | disconnect with Auto ON and leave the panel empty (do not navigate) | label reads `sec` and nothing polls | resuming onto an empty panel would just re-arm a tick that skips |

-----

### ✅ CLOSED 2026-08-21 `[CLASSTOTAL-2026-08-18]` — the Classes tab reports the REAL class total, not the cap

*Was: `Aura::ListClasses` bounded its walk on the row cap AND counted `totalClasses` inside that loop,
so `totalClasses` could never exceed `maxResults` (5,000). The Classes tab rendered "5000 classes …
5000 total UClasses ⚠ STOPPED" — the two numbers identical exactly when the second was supposed to
add information — and the same capped value went out on the wire (`list_classes` → `total_classes`).
Now: the walk runs to the END of GObjects and increments `totalClasses` for every qualifying class;
only ROW materialization (the costly `WalkClassEx` + score + push) stops at the cap, so the extra work
past the cap is a handful of cheap reads per object (the same per-object cost `EnumerateAllFunctions`
already pays over the whole pool), not row building. `truncated` keeps its exact meaning (`rows >=
cap`). The status line now reads "5,000 classes shown of 6,609 total … ⚠ STOPPED at the 5,000-row
cap". `list_classes` is pipe-JSON, so no mailbox-contract implication. Pinned by
`ListClassesAsync_HonestTotalExceedsThePage` (UI) — the DLL walk itself has no test target
(`Aura.cpp` is compiled by none), so the class-count number is a live check.*

> Needs a game with **> 5,000 classes** (Elliot has ~6,609). A small game will not truncate.
>
> | step | do this | expect | why it is a real check |
> |---|---|---|---|
> ### ✅ ALL THREE ASSERTIONS HOLD ON THE WIRE 2026-08-19 `[CLASSTOTAL-WIRE-2026-08-19]`
>
> Checked over the pipe (`list_classes` / `list_all_functions`), so this covers the **numbers**; the
> UI *status-line wording* is the only part still owed.
>
> ⚠ **Elliot is NOT the >5,000-class title the row assumes.** At the main menu it reports **3,236**
> classes, `truncated=false`. **Avowed is** the truncating one.
>
> | title | `total` (page) | `total_classes` | `truncated` |
> |---|---|---|---|
> | Avowed, `game_only=true` | 5,000 | **5,102** | true |
> | Avowed, `game_only=false` | 5,000 | **7,409** | true |
> | Elliot | 3,236 | 3,236 | false |
> | OCTOPATH | 699 | 699 | false |
>
> * **Step 1 — PASS.** On Avowed the page (5,000 = the `limit` default) and `total_classes` (5,102 /
>   7,409) **differ**, with `truncated=true`. Before the fix both were 5,000, i.e. "total" answered
>   nothing.
> * **Step 2 — PASS, like-for-like.** `list_all_functions` reports `scanned_classes=`**5,102**,
>   which equals `list_classes(game_only=true).total_classes` **exactly**. ⚠ It does *not* equal the
>   `game_only=false` figure of 7,409 — comparing those two is an apples-to-oranges scope mismatch
>   that reads as a failure. Compare the same scope.
> * **Step 3 (NON-REGRESSION) — PASS on two titles.** Elliot 3,236 = 3,236 and OCTOPATH 699 = 699,
>   both `truncated=false`: a full walk reports one honest number and does not falsely flag
>   truncation.
> * ⚠ **Read `total_classes`, never `total`.** `total` is `results.size()` and equals the cap
>   exactly when truncated — the very misreading this row exists to correct.

> | 1 ⚠ THE ONE THAT MATTERS | ✅ **PASS 2026-08-21 on AVOWED** (⚠ not Elliot — Elliot is only 3,236 classes and never truncates; that was already corrected by the wire check above and the step text is left as filed so the correction stays visible). Classes tab, "Game classes only" **off**, Load → the status line reads, exactly:<br>`5,000 classes shown of 7,409 total (scanned 92,036 objects)  ⚠ STOPPED at the 5,000-row cap — filter to narrow, or raise the cap`<br>The two numbers **DIFFER**. Before the fix both were 5,000. With "Game classes only" **on** the same Load reads `5,000 … of 5,102 total`, matching the wire figure exactly. | | |
> | 2 ⚠ CROSS-CHECK | ✅ **PASS 2026-08-21 on AVOWED.** Interesting Funcs, "Game Only" off, Load → `20,060 functions across **7,409 classes** (4,965 above threshold 5, scanned 92,036 objects)`. The Classes tab's total for the SAME scope is **7,409**. The two panels agree.<br>⭐ This also strengthens the wire note above, which could only compare `list_all_functions` against `list_classes(game_only=true)` = 5,102: in the UI each panel has its own scope toggle, and the totals agree **at both scopes** (5,102 / 5,102 and 7,409 / 7,409). The apples-to-oranges warning is about mismatching the toggles, not about a real disagreement. | | |
> | 3 ⚠ NON-REGRESSION | ✅ **PASS 2026-08-20** — DumperTest, Classes tab, **"Game classes only" OFF**: the status line reads exactly `3,942 classes shown of 3,942 total (scanned 25,179 objects)` — the two numbers EQUAL and **no STOPPED note**. Corroborates the pipe reading (`scanned_classes` 3,942). | | |
> | 3 ⚠ NON-REGRESSION | on a small game (< 5,000 classes), Load | "N classes shown of N total" (equal), no STOPPED note | proves a full walk still reports one honest number and does not falsely flag truncation |

-----



### ✅ M1–M5 step 1 arms (a) + (b) PASS 2026-08-23 `[SEETHRU-ARMS-AB-2026-08-23]` — the row's "needs a human" was wrong for both

Both arms were classified human-only (*"needs a human moving the character / stalling the game"*).
Neither does. DumperTest dev / DLL 3337.

**Arm (a) — moving restores a hidden actor.** `tools/verify/seethrough_arm_a.py`.
`teleport_relative` is an ordinary pipe command and DumperTest has a real player pawn, so the
movement is driven over the **same connection** that owns the See-through session — which matters,
because the DLL disables See-through on client disconnect, so a two-process arm would measure the
disabled state and call it a pass.

| step | observed |
|---|---|
| positive control | `0x18C7F268F40` **own** `bHidden=true` — not the hider's tally |
| ⭐ **negative control** | held still 6 s → **still hidden**, `active=true`, `count=1` |
| the arm | 6 × `teleport_relative(900)` → `bHidden=**false**`, `hidden_count=0`, `actors=[]` |
| attribution | `active` **still true** at the end, so the restore is the move — not arm (c)/(d) |

The negative control is what makes it an arm rather than a coincidence: without it, "it became
visible after I moved" is equally consistent with the hide simply lapsing.

**Arm (b) — See-through active while the game is HUNG, then a graceful close.**
`tools/verify/seethrough_arm_b.py`, stalling the UE game thread with `suspend-tid`.

| witness | before stall | after stall |
|---|---|---|
| `IsHungAppWindow` (the OS's view) | False | **True** |
| `game_thread_stalled` (Stark's heartbeat) | False | **True** |

Two independent witnesses, both required to start healthy so they can be shown to flip. With the
game hung the DLL **still answered** (`active=true, hidden_count=1`), so the ~10 Hz worker did not
wedge the pipe. Then a posted `WM_CLOSE` exited the process in **1.0 s**, with **0** `tick threw`
and **0** crash dumps — which is the arm's real target, since `WorkerLoop`'s catch exists for the
`std::terminate` / `0xC0000409` "See-through then close the game" crash.

⚠ **A `taskkill /F` does not test this** (recorded during arms (c)/(d): the DLL's shutdown path
never runs, so the arm is vacuous). It must be a posted `WM_CLOSE`.

ℹ️ The arm-(a) rig legitimately refused a run on the second attempt — arm (a) had moved the pawn
5400 units, so nothing occluded from the new pose and `hidden_count` was 0. Relaunching restored a
valid pose. A rig that declines to score a vacuous setup is working.

-----

### ✅ CLOSED 2026-08-21 — A12: the same, in GROUP mode (build 3261)

*Needs a connected game and the same container as A11's check. **The rule and the anchor factories
are unit-pinned (17 assertions, two negative controls); the WIRING through three by-name hops is
not** — no target compiles `Aura.cpp`. Run this straight after A11's; it is the same in-game
actions with the panel in Group mode.*

> Grep by FORMAT STRING: `RefineGroup re-anchor:` (whole-pass summary) and `container-moved=`
> (the per-candidate drop tally).
>
> | step | do this | expect | why it is a real check |
> ### ✅ STEPS 2, 3, 4 ALL PASS 2026-08-21 `[A12-MUTATE-2026-08-21]` — the row is CLOSED
>
> DumperTest dev, **dist 3308**, over the pipe with no UI, using `tools/verify/mutate_guard.py`
> (capture → poke → witness by read-back → collateral check → restore, verified).
>
> | step | what was emulated | result |
> |---|---|---|
> | **2** buffer move | the whole `Arr_Int` slid **+4 bytes inside its own allocation**, `Count` left at 5 | **PASS** — both leaves repointed by exactly +4; `RefineGroup re-anchor: 2 … repointed, 0 dropped`; CDO row's slot addresses unchanged; zero `carries no ValueAnchor` |
> | **3** in-place shrink | tail down, `Num` 5→4, **`Max` left at 8** | **PASS** — candidate gone, and the tally attributes it: `container-moved=1`, `predicate-said-no=0`, `unreadable=0`; whole-pass `0 repointed, 1 dropped` |
> | **4** TMap growth | elements copied to a fresh page, header repointed, **source bytes wiped to zero** | **PASS** — leaves repointed to `scratch+4`/`scratch+8`; `2 … repointed, 0 dropped` |
> | **4** TMap removal | the candidate's own allocation bit cleared | **PASS** — row gone, leaf **still reads 6201**, `container-moved=1` |
>
> ⭐ **Step 2 slides +4 INSIDE the same allocation rather than moving to a new page, and that is
> the point.** A fresh page would also pass if the code simply re-derived each leaf from the
> container base — right answer, wrong reason. A 4-byte slide leaves the old addresses perfectly
> readable and full of plausible data, so "every slot moved by exactly +4" cannot come from a stale
> read or from luck. `Count` is deliberately unchanged too, so a count-only implementation cannot
> pass.
>
> ⭐ **Step 4's growth half WIPES THE SOURCE, and that is what makes it two-sided.** Without the
> wipe a candidate that was never repointed would still read `6201` at its stale address and
> survive — passing by doing nothing. With the source zeroed, survival is only possible if the
> leaves actually moved.
>
> ⭐ **The removal half has a control on BOTH sides**, and neither is optional. *Before*: clearing
> an UNRELATED element's bit must leave the row alive — otherwise the rule is "any sparse change
> kills everything" and the real result is unattributable. *After*: restoring the bit must bring
> the row BACK — otherwise the drop might have been "the container is now permanently unreadable"
> rather than "my slot was freed". Both held.
>
> ⚠ **`container-moved=1`, not 2** — the slot loop stops at the first emptied slot. Asserting ≥2
> would have failed a correct implementation.
>
> ⚠ Same synthetic-mutation limit as A11: these reproduce what the SCANNER OBSERVES, not the
> events. No allocator ran, no `TMap::Add/Remove` executed, nothing was freed. Step 3 emulates
> `RemoveAt` with `EAllowShrinking::No` only.
>
> ⚠ Steps 1 / 4a / 5 / 6 were **re-run** on 2026-08-21 after the log-path correction below, so
> their absence claims are now backed by a channel shown to carry `[SCAN:grp]`.
>
> ### ⚠ THE LOG-PATH CORRECTION, AND IT CORRECTS ME `[A12-LOGPATH-2026-08-21]`
>
> A11's sibling fix established that `Refine re-anchor:` lives in `offsets-0.log`, and I applied the
> same reasoning here and **broke this rig**. Both markers are in `Aura.cpp`, which declares
> `#define LOG_CAT "OARR"`:
>
> | marker | call form | tag | file |
> |---|---|---|---|
> | `Radar: Refine re-anchor:` (A11) | `LOG_INFO(...)` — uses the file's `LOG_CAT` | `[OARR]` | **offsets-*.log** |
> | `RefineGroup re-anchor:` (A12) | `Sein::Info("SCAN:grp", ...)` — **explicit category** | `[SCAN:grp]` | **scan-0.log** |
>
> An explicit category **overrides** the file default, and `"SCAN:grp"` matches `Sein.cpp`'s
> `{ "SCAN", 4, LF_Scan }` prefix rule. Measured across rotations: each file holds its own marker
> and neither holds the other's. So the ORIGINAL A12 rig and this row were right about `scan-0.log`
> all along.
>
> ▶ **The rule, corrected: `#define LOG_CAT` is a DEFAULT, not the answer — read the CALL.**
> `Aura.cpp` alone has **93** `LOG_*` calls and **22** explicit `Sein::` calls, so one source file
> routinely writes to two different logs. `Sein.cpp`'s table resolves a category; it cannot tell you
> which category a given line passes.
>
> ### ✅ A12 STEPS 1, 4a, 5, 6 PASS 2026-08-20 `[A12-PIPE-2026-08-20]` — over the pipe, no UI
>
> Rig: `tools/verify/a12_group_anchor.py`, DumperTest / dist 3263. Fixture read live first:
> `DumperTestActor.Map_IntToVec3f` is a `TMap<int32, DumperTestVec3f>` with
> `{1: X=6201 Y=6202 Z=6203}`, so **6201 and 6202 are two values inside ONE element** — step 1's
> requirement, on the TMap step 4 asks for.
>
> | step | result |
> |---|---|
> | **1** | 2 rows, slot fields **`['Map_IntToVec3f[0].X[0]', 'Map_IntToVec3f[0].Y[0]']`** — both carry the element index, and both name the *same* element |
> | **4a** | unchanged refine → **surviving 2 (was 2)**. The mass-drop trap does not fire |
> | **5** | plain pair (`NetUpdateFrequency`=100 + `FrozenInt`=424242, no container in either path) → 2 rows, unchanged refine → **2 survive**, and **0** `RefineGroup re-anchor` lines |
> | **6** | **0** occurrences of `carries no ValueAnchor` |
>
> Step 5 is non-vacuous in both directions: rows existed *and* survived, so the absent re-anchor line
> is a decision rather than an empty set.
>
> ⚠⚠ **Two things will make this row look broken when it is not, and both were hit here.**
> * **Group slots accept only `NumericNoByte` / `NumericAll`.** Passing `data_type: "Float"` is
>   rejected outright (`group slot data_type must be NumericNoByte or NumericAll: Float`), the reply
>   carries `session_id: null`, and a caller that reads the session as "no match" concludes the
>   fixture did not match.
> * **The obvious plain-pair control uses a MOVING value.** `Health.CurrentValue` ticks — the
>   DumperTest overlay says so outright (*"must fall, wraps to 100"*) — so a pair built from a value
>   read moments earlier matches **0** objects by the time the scan runs. Measured over 1.5 s, the
>   movers are `Health.CurrentValue`, `TickCount`, `F32_Ticking`, `F64_Ticking`; the stable fixtures
>   are `Health.BaseValue`=100 and `FrozenInt`=424242.
>
> ⛔ **Steps 2, 3 and the growth/removal half of 4 remain open** — all three need the container to
> change size in play, which nothing here does unattended.
> |---|---|---|---|
> | 1 | Value Search → **Group** mode, **Deep ON**, two slots whose values both live inside the same `TArray<FStruct>` element. First Scan. | a candidate row whose slot fields carry an `[i]` index | establishes the leaves really are container elements and Deep is on — without Deep, nothing here is exercised |
> | 2 ⚠ THE ONE THAT MATTERS | grow that container in game until it must realloc, then Next Scan | the row **SURVIVES**, and `scan-0.log` has `RefineGroup re-anchor: N … repointed` | before 3261 a realloc left every leaf address stale |
> | 3 | remove an element BEFORE the matched one, then Next Scan | the row is dropped, and the `RefineGroup cand[...]` line shows `container-moved=` non-zero | the shift-in-place case; the tally is what tells it apart from a predicate rejection |
> | 4 ⚠ THE UNIT TRAP, and it needs a TSet/TMap | run the same two steps against a `TMap` whose value struct holds both values | rows behave as in 2 and 3, and are **NOT** all dropped on the very first Next Scan | a mass drop with no in-game change is the `MaxCapacity`-vs-`MaxIndex` mismatch. This is the failure the two named factories exist to prevent and the ONLY way to observe it |
> | 5 ⚠ NON-REGRESSION | Group scan a plain (non-container) field pair, Next Scan with nothing changed | rows survive, and **no** `RefineGroup re-anchor` line at all | `Direct` leaves must not enter the new path |
> | 6 | check the log for `carries no ValueAnchor` | **absent** | it fires only if one of the three by-name hops dropped the stamp — the one thing no offline test can see |

⚠ **Depth ≥ 2 is deliberately NOT anchored** (`UnverifiableNested`), so a leaf nested two containers
deep behaves exactly as it did before 3261. Not a failure.

-----

### ✅ CLOSED 2026-08-21 — A11: a grown container must no longer lose its Value Search candidates (build 3253)

*Needs a connected game with a `TArray`/`TMap` UPROPERTY whose element count changes in play
(inventory, spawned-actor list, buff list). **The RULE is unit-pinned (15 assertions, two negative
controls); the WIRING is not** — no target compiles `Aura.cpp`.*

> **The cheapest decisive evidence is a log line the fix adds**, and it only appears when the
> re-anchor actually fired. Grep by FORMAT STRING: `Refine re-anchor:`.
>
> | step | do this | expect | why it is a real check |
> ### ✅ A11 STEPS 1 + 6 PASS 2026-08-20 `[A11-PIPE-2026-08-20]` — over the pipe; 2–5 need in-play mutation
>
> Rig: `tools/verify/a11_container_anchor.py`, DumperTest / dist 3263. The fixture is **read live
> before scanning** rather than assumed: `DumperTestActor.Arr_Int` = `[10, 20, 30, 40, 50]`, 5 of a
> capacity-8 buffer at `0x243AD7658E0`, 4-byte elements.
>
> **Step 1 — PASS.** Scanning `Int32 == 30` with deep on returns 5 candidates, **2 of them carrying
> the element index**:
> ```
> Arr_Int[2]  DumperTestActor  addr=0x243A7403E48  inst=0x24396AA6140   (the CDO)
> Arr_Int[2]  DumperTestActor  addr=0x243AD7658E8  inst=0x24402FE7910   (the live actor)
> ```
> ⭐ The addresses cross-check the index without trusting the label: `0x243AD7658E8` is exactly
> `array_data_addr 0x243AD7658E0 + 2 × 4B`, i.e. element **2** of the live array. So the `[2]` is
> describing the byte the scanner actually matched.
>
> **Step 6 — ⚠ THE 2026-08-20 PASS WAS VACUOUS. Re-run 2026-08-21, now genuine.**
> `[A11-LOGPATH-2026-08-21]`
>
> `Refine re-anchor:` is emitted from `Aura.cpp`, whose `#define LOG_CAT "OARR"` Sein maps to
> **`LF_Offsets`** (`Sein.cpp`: `{ "OARR", 4, LF_Offsets }`). It lands in **`offsets-0.log`**. Both
> the rig and **this row's own steps 2 and 3** named `scan-0.log`, where the line can never appear —
> so "expect NO re-anchor line" passed no matter what the code did.
>
> ⭐⭐ **The old write-up argued explicitly that it was non-vacuous**, on the grounds that the refine
> was shown to have run over 11 real candidates. That reasoning is sound and irrelevant: it
> controlled for the **stimulus** happening, not for the **detector** being able to see anything.
> A grep of the wrong file returns zero for both "the code did not do it" and "I am reading the
> wrong channel", and no amount of evidence about the stimulus separates them.
>
> The rig now **proves the channel first** — it aborts unless `offsets-0.log` actually carries an
> `[OARR]` line — and only then treats an absence as an absence:
> ```
> detector OK: offsets-0.log carries [OARR] traffic, so an absent marker is a real absence
> plain scan (deep OFF) session=2 candidates=11 -> refine ok=True remaining=11
> 'Refine re-anchor:' lines since the Direct scan began: 0   <-- must be 0
> ```
> **Step 6 PASS** (DumperTest, dist **3308**). Step 1 unchanged and re-confirmed: 5 candidates, 2
> carrying `Arr_Int[2]`.
>
> ▶ **Generalise this.** Any register step whose PASS is "X does not appear" needs the channel shown
> to carry X's traffic, not just the action shown to have run. `docs/log-verification-checklist.md`
> already says to grep by FORMAT STRING; the missing half is to grep the file the format string's
> CATEGORY actually routes to — `Sein.cpp`'s table is the authority, and four categories
> (`SEETHRU`/`Grausam`/`SENSE`/`PROXY`) fall through to `init-0.log` rather than anywhere obvious.
>
> ### 🔧 THE MUTATION HARNESS FOR STEPS 2–5 IS BUILT AND VALIDATED `[MUTATEGUARD-2026-08-21]`
>
> The blocker on A11 2–5 (and A12 2–4) was never the assertion — it is that nothing on DumperTest
> changes a container's size on its own and no operator is present. `write_mem` makes the change
> reachable, so `tools/verify/mutate_guard.py` now owns the risky half: capture → poke → **witness
> by read-back** → assert nothing else moved → **restore, verified by read-back**.
>
> ⚠ **A synthetic mutation is not the same EVENT as the game doing it.** Writing a new
> `{dataPtr,count}` reproduces what the SCANNER OBSERVES about a realloc, not a realloc — no
> allocator ran, the old buffer is still mapped, nothing was freed. That is enough for a re-anchor
> rule that reads the header and compares, and NOT enough for anything depending on the old memory
> becoming invalid. Each rig must say which it relies on.
>
> ⚠ **Restore is load-bearing, not tidiness**: a synthetic `TArray` header left installed makes the
> destructor call `FMemory::Free` on a pointer it does not own, and the crash lands minutes later
> looking like the game's fault. `__exit__` restores before anything else and verifies it; a failed
> restore prints an instruction to KILL the process rather than let it exit cleanly.
>
> **Validated against a live DumperTest** (`mutate_guard_selftest.py`, dist 3308) — and the two
> guards were each shown able to FAIL, which is the only reason the passes mean anything:
>
> | check | result |
> |---|---|
> | capture → poke → read-back witness | `28000000` → `5A5A5A5A`, witnessed |
> | collateral guard quiet when nothing else moved | no false alarm |
> | **collateral guard fires on a deliberate change** | `002EAFA0 → 22222222` caught, then restored |
> | restore, verified independently of the harness | back to `28000000` |
> | `assert_channel_carries(offsets-0.log, "[OARR]")` | accepted |
> | **same call on `scan-0.log`** | **refused** — the check discriminates |
>
> ▶ **The six per-step rigs are deliberately NOT written yet.** The plan had them authored up front;
> an unrunnable rig cannot be shown able to fail, and that exact shape has produced six wrong
> assertions in this session alone. Each will be written immediately before the session that runs
> it, on top of this harness.
>
> ### ✅ STEPS 2, 3, 4, 5 ALL PASS 2026-08-21 `[A11-MUTATE-2026-08-21]` — the row is CLOSED
>
> DumperTest dev, **dist 3308**, over the pipe with no UI. The blocker was never the assertion; it
> was that nothing here changes a container's size on its own. `write_mem` makes the change
> reachable, and `tools/verify/mutate_guard.py` makes it safe (capture → poke → **witness by
> read-back** → collateral check → **restore, verified**).
>
> | step | what was emulated | result |
> |---|---|---|
> | **2** growth realloc | fresh page via `VirtualAllocEx`, elements copied, header → `{buf2, 7, 16}` | **PASS** — survivor repointed to `buf2 + 2×4` **exactly**; log: `1 container element(s) repointed after a realloc, 0 dropped` |
> | **2 (ii)** separability | same move, but element[2] set to **31** in the same refine | **PASS** — repointed *and then* rejected on its merits; re-anchor and predicate are independent |
> | **3** in-place shrink | tail memmoved down, `Num` 5→4, `Max` left at 8 | **PASS** — both live candidates dropped, log: `0 repointed, 2 dropped` |
> | **4** sparse removal | allocation bit 1 cleared in the inline `TBitArray` word | **PASS** — dropped, log: `0 repointed, 1 dropped`, **and the address still read 4242** |
> | **5** append into slack | element[5]=60, `Num` 5→6, `Data`/`Max` untouched | **PASS** — survived **at the same address**, zero re-anchor lines |
>
> ⭐ **Every step carried the CDO as a paired control** — same value, same field, same code path, one
> difference. In steps 3 and 4 the CDO survived while the live row died, so the drops are targeted
> rather than a blanket wipe; in step 2 the CDO stayed at its ORIGINAL address while the live one
> moved, so the repoint is targeted rather than a blanket recompute. Without that sibling, "the row
> is gone" and "everything is gone" look identical.
>
> ⭐ **Step 4's real content is that the value was still readable.** `4242` was still sitting at the
> candidate's address when it was dropped — the freed slot is refilled at the identical address and
> `{dataPtr,count}` never moved, so the allocation bit is the only witness there is. A rule reading
> anything else would have kept it and shown a stale match.
>
> ⭐ **Step 5 is the one that could have failed.** Steps 2–4 all end in "moved or died", so the
> cheapest way to pass them is to drop a container candidate whenever the header changes at all.
> The slack append leaves `Data` alone and only bumps `Num`; the candidate survived **at the same
> address with zero re-anchor lines**, so the asymmetric rule is intact.
>
> ⚠ **What these do NOT prove, stated plainly.** The mutations are synthetic. They reproduce **what
> the scanner observes** — a new `Data`, a lower `Num`, a cleared bit — and not the events: no
> allocator ran, nothing was freed, the old buffer stays mapped. Specifically, step 3 emulates
> `RemoveAt(i, 1, EAllowShrinking::No)`; UE's default `RemoveAt` *allows* shrinking and may realloc,
> which is a different observable. Anything depending on the old memory becoming invalid is
> untested.
>
> ⚠ **deep=False throughout**, and it matters: a deep descriptor carries `ValueAnchor::Unknown`, so
> the container branch is never reached and the whole run would be vacuous. Measured — deep=False
> does return container rows here, which is what makes the choice available.
>
> ⚠ Fixture restored and re-read from the game's own view after every step:
> `count=5 values=['10','20','30','40','50']`. The slack slot's original content was `DDDDDDDD`,
> UE's uninitialised fill — incidental confirmation that it really was slack.
>
> ⚠ ~~**Steps 2–5 remain open and were not silently skipped.**~~ **SUPERSEDED 2026-08-21 — all four now PASS, see above.** Kept because its reasoning was right and its conclusion was only true until `write_mem` was used: All four require the container to
> **change size in play** — add entries until it reallocs, remove one before the candidate's index,
> remove the entry a TSet/TMap candidate points at, append into existing slack. Nothing on DumperTest
> grows a UPROPERTY container by itself and no operator is present, so they need either a game with a
> growing container or a fixture that mutates on a timer. (`Arr_Int` sitting at 5-of-8 capacity is
> exactly the slack step 5 wants — it just needs something to do the appending.)
>
> ℹ️ Key names for whoever repeats this: the element index is in **`field_name`** on a candidate row
> (`"Arr_Int[2]"`); there is no `path` or `class_field` key, and reaching for one returns `None` and
> reads as "the index is missing".
> |---|---|---|---|
> | 1 | Value Search a known value that lives in a container element (a `TArray<FStruct>` field, or a `TMap` value). First Scan. | the row appears with a `[i]` element index | establishes the candidate IS a container element, not a direct field |
> | 2 ⚠ THE ONE THAT MATTERS | in game, ADD entries to that container until it must grow (pick up items, spawn enemies), then Next Scan with the same value | the candidate **SURVIVES**, and `offsets-0.log` has `Refine re-anchor: N container element(s) repointed after a realloc` | before 3253 a growth realloc left every element address stale and they were lost outright. A surviving candidate with **no** re-anchor line means the buffer never moved — the container had slack, so this run did not test the repoint |
> | 3 | now REMOVE an element that sits BEFORE the candidate's index, then Next Scan | the candidate is **dropped**, and the log's `dropped` count goes up | this is the silent-wrong-value case: the tail shifts down one slot in place, so the old address reads cleanly and returns the neighbour's value |
> | 4 | for a `TSet`/`TMap`: remove the entry the candidate points AT, then Next Scan | dropped | the allocation bit is the only witness — a freed sparse slot is refilled at the identical address |
> | 5 ⚠ NON-REGRESSION, do not skip | scan a container value, then APPEND to that container without forcing a realloc (add one entry to a list that has slack), Next Scan | the candidate **survives** | the naive `{dataPtr,count}` rule drops these. If they vanish, the asymmetric rule was lost and this is a REGRESSION, not a fix |
> | 6 | repeat step 1 on a plain (non-container) field | unchanged behaviour, and **no** `Refine re-anchor` line at all | `Direct` candidates must not enter the new path |

**Known residuals, do not report as failures**: `TArray::Insert` at a low index shifts on a count
INCREASE and is not caught; balanced churn (remove one, add one back into the same slot) is
invisible; and the GROUP scan path is untouched (filed as **A12**), so a Group-mode refine still
behaves as it did before 3253.

-----

### ✅ AF1 CLOSED 2026-08-23 `[AF1-ENUMCOUNT-2026-08-23]` — "not reproducible on demand" is overturned: the malformed UEnum is a POKE

`tools/verify/af1_enum_count.py`, **StackOBot 5.8 Shipping** (staged build), DLL 3338. **No source
staging** — the input is data, so nothing had to be rebuilt.

```
NumValues before: 7 (0x00000007)     <- equals the baseline's 7 entries, so the offset is right
NumValues after poke: 0x80000000     <- read-back proves the poke landed
EInterpCurveMode = 0 entries         <- REFUSED
ETextGender      = 4 entries         <- untouched control, still at its baseline
```

⭐ **Four legs, each removing a way to be wrong:** the pre-poke read *equals the baseline entry
count* (so the address is the real `NumValues`, not a lucky offset); the read-back proves the write
landed; the target collapses to 0; and a second, **untouched** enum read in the *same* `list_enums`
call still returns its baseline — so the 0 is the guard firing, not a broken run.

⚠ **Only testable on UE 5.6+, and that is not incidental.** The bug lived only in the `FNameData`
branch — the Legacy branch's `num <= 0` test always caught the wrapped value, while this branch
tested `== 0`. **DumperTest at 5.4 can never reach it.** StackOBot 5.8's log confirms the layout:
`UEnum::Names detected at UEnum+0x40 (UE5.6+ FNameData, verified with 'ENetRole', count=5)`.
`UENUM_NAMES` is **not** published by `get_offsets` — read it from `offsets-0.log`.

⚠ **A fresh process is mandatory.** `s_enumCache` (`Ubel.cpp`) is a static map that is **never
cleared or erased** (verified: no `.clear()`/`.erase()` anywhere), so once an enum resolves the poke
is unobservable. Sequence: baseline in run 1 → **restart** → poke before anything resolves → read
once. `list_enums` has **no address filter** and caches all **2,371** at once — never call it before
poking.

⚠⚠ **The first attempt gave the right answer for the wrong reason, and the rig caught it.**
`read_mem` replies carry **`bytes`**, not `hex`; the rig read only `hex`, so the read-back was
silently empty. The target *did* collapse to 0 — but with no proof the poke had landed, that 0 was
unattributable and the rig refused to score it. **A PASS that cannot name its own cause is not a
PASS.**

ℹ️ **Fixture note for `reference-builds.md`:** the corpus copies under
`D:\UE_Analyze_data\Varies Version builds\5.8\…` carry **no `.pak` files** — they are AOB oracles, not
runnable games. The launchable 5.8 host is the staged build at
`D:\Unreal Projects\StackOBot\Saved\StagedBuilds\Windows\…` (161 MB, boots to **40,708 objects**,
`ue_version 508`).

### ✅ AE20 CLOSED 2026-08-24 `[ORPHANCANCEL-ONDISK-2026-08-24]` — re-run on disk; the FIRST run passed without discriminating, and finding that out is the result

`[ORPHANCANCEL-2026-08-20]`'s fix was pinned only at the view-model seam with a stubbed service —
the row's own caveat was *"Not re-run on disk … the DLL-recycling and folder-pruning behaviour
under a real cancel is unchanged-by-inspection rather than re-measured"*. Re-measured now, UI
**3338**, `tools/verify/ae20_orphans.py`, all trees synthetic under `ZZAe20Orphan###`.

**Run 1 — the row's own procedure (40 trees × 1 DLL, tick 4, cancel). Passed, and proves nothing.**

```
Recycled leftover proxy …ZZAe20Orphan000\…\version.dll   09:09:37.521
Recycled leftover proxy …ZZAe20Orphan001\…\version.dll   09:09:37.592
Recycled leftover proxy …ZZAe20Orphan002\…\version.dll   09:09:37.662
Cleanup cancelled after 3 of 4 leftover(s) — 3 file(s) recycled, 12 folder(s) removed   .673
```

3 log lines, tally 3 — they agree, the summary says *cancelled*, and row `…003` stayed listed,
**unticked**, reading `Interrupted — 0 file(s) recycled, 0 folder(s) removed; the rest was left in
place`. Every stated expectation met.

⭐ **And it is not evidence of the fix.** The cancel landed in the **11 ms gap between rows**, so the
interrupted row had recycled *nothing* — and a row that recycled nothing contributes zero under the
pre-fix arithmetic too. The fix moved `files += result.FilesRecycled` **out of `if (result.Success)`**;
a run only discriminates when a **non-successful row has already recycled something**. Run 1 never
created that state. Recording it as the closure would have been the "a number without its
conditions" failure in working-lessons §1, one clean-looking screenshot away.

**Run 2 — locking a DLL does NOT create that state either (a real finding about the rig, not the app).**

Two trees × `version.dll` + `dxgi.dll`, one `dxgi.dll` held open with `dwShareMode == 0` *after* the
scan. Expected `locked` → `Success=false` with `recycled=1`. Measured instead:

```
Cleaned 2 of 2 leftover(s) — 3 file(s) recycled, 4 folder(s) removed
```

no `is locked` warning anywhere in the log, and the DLL still on disk. **`RemoveOrphanProxyAsync`
re-plans from disk at delete time**, and the identity re-check cannot open a share-locked file, so it
drops out of `plan.FilesToRecycle` entirely — the row then succeeds at everything it is still asked
to do, and `ResolveRemovalOutcome`'s `locked` branch is **unreachable through a share lock**. Correct
behaviour, and a dead end for this measurement.

**Run 3 — read-only reaches the branch, deterministically, with no timing at all.**

A read-only file still *opens*, so it survives the re-plan and reaches the loop's own
`(fi.Attributes & FileAttributes.ReadOnly) != 0 → readOnly.Add(…); continue;`. Same fixture, bit set
**after** the scan on `…Orphan000\…\dxgi.dll` only (verified: the other three staged files
`ReadOnly=False`).

```
Recycled leftover proxy …ZZAe20Orphan000\…\version.dll    <- the UNSUCCESSFUL row's contribution
Recycled leftover proxy …ZZAe20Orphan001\…\dxgi.dll
Recycled leftover proxy …ZZAe20Orphan001\…\version.dll
Removed empty folder …ZZAe20Orphan001\…\Win64 / Binaries / ZZOrphan / ZZAe20Orphan001
Cleaned 1 of 2 leftover(s) — 3 file(s) recycled, 4 folder(s) removed; 1 still listed with the reason
```

* log `Recycled leftover proxy` lines = **3**; summary = **3 file(s) recycled** ✅ **they agree**
* row `…Orphan000` is **not successful** (`Cleaned 1 of 2`) yet **contributed 1 of those 3** — this is
  exactly the arithmetic the fix moved out of the success branch
* it stays listed, **unticked**, its future-tense plan replaced by
  `Read-only, left alone deliberately: dxgi.dll. Remove it by hand if you meant to protect it.`
* on disk: `version.dll` gone, the read-only `dxgi.dll` kept, **folder chain un-pruned** — the
  half-state is visible rather than hidden
* pre-fix this same run prints **`2 file(s) recycled`** against 3 log lines (`files += …` skipped for
  a `Success == false` row) — the 3-vs-2 shape of the original finding

⭐ **This is the `APartlyLockedRow_AlsoCountsWhatItRecycled` case on real disk** — the sibling hole
the finding never mentioned and the fix found by moving the tally: *a row that recycles one DLL and
then hits a lock was already going unreported, with no cancel involved.* It was pinned at the seam
and is now measured end to end through the real Recycle Bin. 16/16 `ProxyOrphanDeleteRefreshTests`
still green; 13/13 gates.

⚠ **The rig gained two verbs and one of them is a trap-marker**: `create --dlls version,dxgi` (a row
needs ≥2 files before "partly done" is even expressible) and `readonly <tree> <dll>`. `lock` is kept
**and documented as the one that does not work** for this purpose, so the next session does not spend
the run re-discovering the re-plan. `clean` now clears the read-only bit first — otherwise a failed
arm strands a tree that the next `create` refuses to run beside.

### ✅ X12 CLOSED 2026-08-24 `[X12-AUTORUNDENY-2026-08-24]` — the denied-write fallback, live; "maintainer-only" was wrong in both directions

Audit X12 (`MainWindowViewModel.InstallCeAutorunAsync`): when the auto-place into a running Cheat
Engine's `autorun\` is refused, the app must take the **manual save-dialog fallback**
(`FileWriteFault.IsPlacementDenied`) rather than report failure. The classifier was unit-tested; the
**live** denied-write path never was, and the row sat as *"a maintainer step (CE installed under
`%ProgramFiles%`, app run non-elevated), which no unattended session can stage"*.

⭐ **That blocker was false twice over, and both halves were measured, not argued.**

1. `TryFindCheatEngineDirAsync` resolves CE's folder from the **running `cheatengine*` process's own
   path** — not the registry, not `%ProgramFiles%`. So whichever CE we start *decides* the folder
   under test, and a copy we own is as real as the installed one.
2. The installed `C:\Program Files\Cheat Engine\autorun` is **writable non-elevated on this host**
   (probe file created and deleted). The prescribed setup would therefore have produced a *success*,
   not the denial — following the row exactly yields a confident false PASS, §2.13's sharpest case.

**Rig** `tools/verify/x12_ce_autorun_denied.py` — a 97 MB portable CE copy at `D:\ZZCePortable`
(`stage`), its `autorun\` emptied of CE's own extras, target toggled by `allow` / `deny`.
⚠ **No ACL edits.** The auto-place is a `File.WriteAllTextAsync` onto a fixed name, so a **read-only
file** raises the same `UnauthorizedAccessException` a permission denial raises — one attribute,
fully reversible, and nothing about the machine's security configuration changes.

**CONTROL first — writable, same CE, same folder:**

```
Installed CE autorun helper: D:\ZZCePortable\autorun\ue5_autorun.lua
  (9,309 chars, dll=D:\Github\UE5CEDumper\dist\UE5Dumper.dll, autoLocated=True)
```
No dialog. This is what makes the arm mean something: a save dialog *also* appears when CE is not
found at all (`ceDir == null`), and the two are only distinguishable by which sentence the status
bar shows. Here CE was demonstrably found — the log names our portable folder.

**ARM — same everything, target read-only:**

```
[WARN] CE autorun auto-place denied (Access to the path
       'D:\ZZCePortable\autorun\ue5_autorun.lua' is denied.); falling back to manual save dialog
Installed CE autorun helper: D:\Github\UE5CEDumper\out\ue5_autorun.lua
  (9,309 chars, …, autoLocated=False)
```

* the **save dialog appeared**, defaulting to `ue5_autorun.lua` with the `CE autorun Lua (*.lua)`
  filter — the name CE expects ✅
* `autoLocated=False`, and the status bar warned `⚠ Written to D:\Github\UE5CEDumper\out, which is
  …` (not CE's autorun folder) ✅
* the fallback file is **byte-identical** to what the auto-place wrote in the control
  (`sha256 6625bed7…`, 9,309 bytes both) — the fallback is not a degraded path ✅
* **the denied target was not touched**: still `-r--r--r--`, still the control's mtime and bytes ✅

**Nothing outside the copy was modified.** `C:\Program Files\Cheat Engine\autorun` still holds its
48 files and **no `ue5_autorun.lua`**, before and after. `D:\ZZCePortable` removed, CE closed.

⭐ **A carried capability belief died here and it had been shrinking the automatable set:** *"Avalonia
top-level menu items are not clickable by computer use — the header opens, the item click runs
nothing."* `Tools ▸ Install CE autorun Helper` fired on the **first attempt, twice**, and drove a
`SaveFileDialog`. Every row filed as human-only *because it lives behind a menu* is worth re-testing.
Recorded in working-lessons §2.13, together with `wmic` being absent on this Windows build (26200) —
it raises `WinError 2`, which reads like a broken script rather than a missing OS component.

### ✅ SPENT — 🌍 Locate-in-GWorld where the AOB scan does NOT resolve &GWorld (audit #5 AE10, build 2961)

> **Steps 1, 2 and 4 CLOSED 2026-08-23 `[AE10-LOCATE-2026-08-23]`; step 3's premise does not hold.**
> Nothing here is still owed.

The 🌍 buttons were gated on the client `IsGWorldAvailable` flag, which is really *"the AOB scan
produced a &GWorld slot address"* — not *"a live UWorld exists"*. The DLL has world-recovery
fallbacks that work when that scan did not, so the gate **disabled the button on games where locate
worked**. All 19 gates are gone and the flag is deleted; the DLL now decides.

**The payoff case is a game where GWorld did NOT resolve by AOB** — the Pointers panel shows no
GWorld address, or the game runs in proxy mode (TQ2 is the recorded example). Nothing in the test
suite can reach this.

1. On such a game, the per-row **🌍** buttons must now be **enabled** in Instance Finder, Interesting
   Functions, Interesting Properties, Detect Stats, Class Pivot, Snapshot (Diff + Group) and SPC
   Query. Before this build they were greyed out with no explanation.
2. Click one. **Success = a path is found, or a clear "no path"/"invalid" message** from the DLL.
   Silence is a failure — the whole point is that a click now says something either way.
3. **Then the negative case**: on a game with genuinely no live UWorld (main menu before a level
   loads), the click must report the DLL's invalid/no-path status rather than appearing to work.
4. Regression check on a normal game where GWorld *does* resolve: the 🌍 handoffs still behave as
   before — this change should be invisible there.

### ✅ SPENT — open the exported .usmap in a real consumer (audit #5 W1/W7, build 2853)

> **CLOSED 2026-08-22 `[W1W7-CUE4PARSE-2026-08-22]`** — a third-party parser reads the export. This
> heading covers only W1/W7, so it is fully discharged; kept for the reasoning below.

The `.usmap` export declared v3 and wrote the v0 body; it has been unopenable since the feature
shipped on 2026-03-01. Now fixed to v4 with a round-trip reader in the test suite that asserts the
stream is fully consumed at the widths the vendored canonical writers define.

**What the round-trip cannot prove is that a real parser agrees with our reading of the format.**
Both are derived from the same two sources (`vendor/RE-UE4SS/.../Generator.cpp`,
`vendor/Dumper-7/.../MappingGenerator.cpp`), so a shared misreading would satisfy both.

1. Export a `.usmap` from any connected game (**Export → USMAP**).
2. Open it in **FModel** (Directory selector → *Mappings file*), or run it through CUE4Parse's
   `UsmapParser` directly.
3. Success criterion is not "no error" — it is that **property names and types appear for a class you
   can independently verify**, e.g. `AActor`'s `bHidden` / `InitialLifeSpan`. A parser that accepted
   the header and produced an empty or garbage table has still failed.
4. Worth including a **Blueprint-generated** class in the check: `W8` (the bare `"Class"` filter that
   drops every `*_C`) is **still open**, so a BP class legitimately will not be there yet — confirm
   that is the reason rather than a parse failure.

> ### 🟡 STEP 1 DONE, STEPS 2-4 BLOCKED 2026-08-20 `[W17-USMAP-2026-08-20]` — no independent parser on this machine
>
> **Step 1 — done.** `out/DQ7R-Win64-Shipping.usmap`, **2,786,463 bytes**, exported 2026-08-20.
>
> **Header framing checked, and it is self-consistent at the v4 layout:**
> ```
> magic 0x30C4 · version 4 · bHasVersionInfo(int32) 0 · compression 0 (None)
> compressedSize = decompressedSize = 2,786,447 = filesize - 16
> ```
> The 16 comes from the **mandatory `int32 bHasVersionInfo`** that version >= 1 inserts after the
> version byte — the field whose absence was the original defect (declared v3, wrote a v0 body).
> ⚠ Reading this file with the *pre-v1* 12-byte header makes `compressedSize` land on the tail of
> that int32 and come out as **0**, which looks exactly like a corrupt export. It is not; the header
> is fine and the reader was misaligned. Worth writing down because the next person to eyeball these
> bytes will reach for the older layout first.
>
> ⛔ **Steps 2-4 cannot be run here and it is not for want of trying.** Neither **FModel** nor
> **CUE4Parse** is installed on this machine (searched `%LOCALAPPDATA%`, `Documents`,
> `%ProgramFiles%` and `D:\`), and obtaining one is an external download.
>
> ⚠ **Writing a third reader here would be worth nothing, and the row already says why:** our writer
> and our round-trip test both derive from the same two vendored sources
> (`RE-UE4SS/Generator.cpp`, `Dumper-7/MappingGenerator.cpp`), so *"a shared misreading would satisfy
> both"* — and a parser I wrote from those same sources would be a third copy of the same reading.
> The check needs a consumer with an **independent lineage**; that is the whole point of the item.
> What is verified above is framing only, which the round-trip test already covered.

### ✅ FIXED 2026-08-19 `[CONTAINERCAP-2026-08-18]` — container drill-down now discloses "showing N of M" when capped

**Was.** `Set_Big` drilled to a grid whose last row is `[128]` under breadcrumb
`SetBig {Set: 199, IntProperty}` — nothing distinguished a complete 128-entry set from the first 128
of 199, so a user who expanded a 500-entry `TMap`, missed an item, and concluded it wasn't there was
misled. `Constants.DefaultArrayLimit = 128` (surfaced as the toolbar **Array Limit** slider /
`ArrayLimitExponent`) caps the walk; the cap is correct — the *silence* was the defect.

**Fix (client-only, no protocol change).** The DLL already sends BOTH the true total
(`set_count` / `map_count` / array `count`, `Fern.cpp:1448-1602`) and the capped element list, so the
UI now compares them. New pure helper `ContainerTruncation` (`ui/UE5DumpUI/Core/ContainerTruncation.cs`)
drives three disclosures, ALL empty on the non-truncated common case (no noise): the drill breadcrumb
label and the panel header (`CurrentObjectName`) gain a `⚠ showing 128 of 199` suffix, and `StatusText`
points at the **Array Limit** slider. Wired into `NavigateTo{Array,Map,Set}Container` +
`Populate{Array,Map,Set}ContainerFields` in `LiveWalkerViewModel`. Scalar arrays are re-fetched in full
on drill, so they correctly show no badge; only `TSet` / `TMap` / pointer-`TArray` (inline preview)
truncate. Covered by `ui/UE5DumpUI.Tests/ContainerTruncationTests.cs` (pure helper + real-VM drill per
container kind, truncated & full).

**Verify in-game (verify only — no code):**

| # | 做什麼 | 預期 |
|---|---|---|
| 1 | Live Walker → drill into a `TSet` / `TMap` / `TArray<obj>` with **> 128** entries | Breadcrumb AND header read `⚠ showing 128 of N`; a status line names the **Array Limit** slider |
| 2 | Drill into a container with **≤ 128** entries | No `showing…` badge anywhere and no status line (common case stays clean) |
| 3 | Raise the toolbar **Array Limit** slider, re-open the same big container | The shown count rises (e.g. `showing 256 of N`); once the cap ≥ N the badge disappears entirely |

### ✅ AC11 step 2 CLOSED 2026-08-23 `[AC11-STAGELOCK-2026-08-23]` — and the RIG was reporting a false FAIL

B1 calls this an *"owed re-run"*: the fix shipped, nobody re-measured. Re-measured on build 3338
with the existing headless rig `tools/verify/ac11_locked_rename.py` — **no game and no UI needed**;
it runs entirely in `out/ac11/` and maps its own target with
`LOAD_LIBRARY_AS_IMAGE_RESOURCE|DONT_RESOLVE_DLL_REFERENCES`, so a real `SEC_IMAGE` section exists
without running our `DllMain`.

| | observed |
|---|---|
| **A — negative control** (nothing mapped) | both publish shapes **succeed**, no residue — so an "error 5" below cannot just mean a broken path |
| **B — target mapped as an image** | OLD direct `File.Copy` → `ERROR_SHARING_VIOLATION`; NEW staged copy+rename → `ERROR_ACCESS_DENIED` |
| the mapping | **both** reach the **LOCKED arm** via `IsTargetUnreplaceable` |
| residue | no `.ue5dump-stage` survived either failure |

⭐⭐ **The rig FAILED on first run, and the rig was wrong — not the code.** Its verdict still modelled
the **pre-fix** filter (`catch (IOException ex) when HResult == 0x80070020 || Message.Contains("being
used")`) and therefore failed whenever the two shapes produced *different* OS errors. They always do,
and always will: a direct copy opens the target (`SHARING_VIOLATION`) while a rename must first
**delete** it, and a file carrying an image section refuses deletion with `STATUS_CANNOT_DELETE` →
`ERROR_ACCESS_DENIED`. **That disagreement is an OS fact, not a defect** — and the fix accepted it
rather than trying to remove it: `ProxyDeployService` now catches
`Exception ex when (IsTargetUnreplaceable(ex))` ([:1188](ui/UE5DumpUI/Services/ProxyDeployService.cs:1188)),
mapping `UnauthorizedAccessException` **and** the sharing-violation `IOException` to the same arm.
⇒ The right question is no longer *"do the two shapes agree"* but *"is each shape's error one the
filter recognises"*. The rig now asserts that, and its docstring records why. Left as it was, it
would have reported a permanent false FAIL against already-fixed code.

ℹ️ The **code** half was already pinned: `IsTargetUnreplaceable` is `internal static` and pure, and
`ProxyDeployTests.IsTargetUnreplaceable_CatchesBothWriteShapes` covers it — **91 ProxyDeploy tests
green**. So this row needed only the OS-level half, which is why it never required the UI drive B1
prescribes.

### ✅ The `--top` residual is CLOSED 2026-08-24 `[AF16-BYCONSTRUCTION-2026-08-24]` — **it was unreachable, not merely unrun**

**There is no string sort path on this dialog to discriminate against**, so a >=10-reference field
would have confirmed arithmetic and nothing more. Verified by reading all four links in the chain:

| | |
|---|---|
| `PropertyXrefDialog.cs:40` | `["Occurrences"] = DataGridSortComparers.Number<PropertyXrefMatch>(r => r.Occurrences)` |
| `PropertyXrefDialog.cs:312` | `SortMemberPath = nameof(PropertyXrefMatch.Occurrences)` — the **property**, not a formatted label |
| `PropertyXref.cs:27` | `public int Occurrences { get; init; }` |
| `DataGridSortComparers.cs:32-37` | `Number<T>` is `getter(ta).CompareTo(getter(tb))` over `long` |

The failure mode trimming actually produces here is an **inert header** (no reorder at all), and that
was already shown by the existing 9-row fixture reversing exactly: `1,3,3,3,3,5,5,5,9` ->
`9,5,5,5,3,3,3,3,1` (todo.md:13077). The one route by which a numeric column *could* become a string
sort — a `SortMemberPath` aimed at a formatted label — is machine-enforced offline by
`No_column_sorts_on_a_label_that_formats_a_number` (`DataGridSortWiringTests.cs:216`).

⭐ **Closed with an offline substitute that is itself shown able to fail.** The digit boundary is now
pinned in `DataGridSortComparersTests.Number_OrdersByIntegerKey`:
`Assert.True(c.Compare(R(n: 9), R(n: 10)) < 0)` and its converse. Armed by swapping the comparer to
the string form (`Ordinal<Row>(r => r.N.ToString())`), the test goes **red on exactly those two new
assertions** and on no others — the pre-existing `1v2 / 5v2 / 3v3` all still pass under a string
compare, because none of them crosses a digit boundary. That is the precise demonstration that the
old assertions could not have caught this and the new ones can. Reverted; green again.

ℹ️ `Access` ordering remains untested, and stays that way — it is a two-value enum rendered as text,
with no numeric/string ambiguity to resolve.

⭐⭐ **The rig's confirm step earned itself immediately, and the result is a correction rather than a
defect.** Every candidate is re-asked through `find_property_xrefs` — what the dialog itself calls —
and only counted if that returns ≥ 2. The first run offered `DOLLCharacter::AutoPossessPlayer` with
**16** functions while `find_property_xrefs` returned **0**, with and without `game_only`, over the
identical 11,256-function / 705-bytecode corpus. Cause, measured rather than guessed: all 16 came from
`method="disasm"` — the native-disassembly **heuristic** (Path 2) — while `find_property_xrefs` is a
Kismet **bytecode** xref ([Aura.cpp:5541](dll/src/Aura.cpp:5541)). The two commands answer different
questions. **The dialog's own footer says so**: *"Native (C++) functions have no bytecode and cannot be
detected here — an empty result on an engine field is expected."* The rig now skips non-bytecode
replies and the confirm step went from 3-of-5 to **6-of-6 AGREE**.

▶ **Do not re-run the earlier dead ends.** `WBP_Battle_LvUpSkill_C::VerticalBox_0` returning 1 row is
now *explained*, not unlucky: that class owns exactly 2 UFunctions, one of which is an event stub.


### ✅ AF16–AF23 step 2 — Props half PASSES on the AOT build (Xref half CLOSED 2026-08-23, see `[AF16-XREF-2026-08-23]`) `[AF16-PROPSSORT-2026-08-22]`

Run on **DQ7R** (`dist` AOT **v1.0.0.3315** — the trimmed binary this row insists on, since the whole
defect class is "the reflection sort survives JIT and is trimmed out of what ships"), UE427,
149,370 objects.

⭐⭐ **THE UNLOCK, and it is the thing to read if this row is ever revisited: the Props dialog's
"Class fields only" checkbox is CHECKED by default and hides locals/temporaries.** That single
default is why five earlier attempts — and, on the evidence of its note, an earlier session — saw
`0 properties` and concluded the dialog produced nothing. The status line says so if you read it
closely: `0 properties (0 written) (1 locals/temporaries hidden)`. **Unchecking it** turned that
into a full table.

**The second half of the unlock: pick a function whose Flags are `BC` WITHOUT `Native`.** A
`BC,Native` function takes the native-disassembly path and reports `native disasm — heuristic, N
unmapped`; a pure Blueprint function (`BC,BE`, `BC,BE,Event`) takes the exact bytecode path. Filter
Interesting Funcs by `ExecuteUbergraph` on a `WBP_*` class to find them in bulk.

**Fixture used:** `ExecuteUbergraph_WBP_Battle_LvUpSkill` → **11 properties (6 written)**, which is
comfortably the "≥2 rows" the row asks for, and genuinely discriminating: Access values
`2W/4R`, `2W/3R`, `1W/2R`, `2W/1R`, `1W/1R`×2 and five `read`; Re values 1,2,2,2,2,2,3,3,3,5,6;
scopes `instance` and `local`; types Array/Int/Bool/Object.

**All headers clicked, and every one reorders** (Access ×2, Re ×2, Scope, Property, Type):

| header | ascending | descending |
|---|---|---|
| **Access** | five `read` first, then `1W/2R`, `1W/1R`, `1W/1R`, `2W/4R`, `2W/3R`, `2W/1R` | `2W/4R`, `2W/3R`, `2W/1R`, `1W/2R`, `1W/1R`, `1W/1R`, then the `read`s |
| **Re** | 1,2,2,2,2,2,3,3,3,5,6 | exact reverse |
| **Scope** | `instance` first, then all `local` | — |
| **Property** | alphabetical (`CallFunc_Add_IntInt` → `CallFunc_Array_Get_Item` → …) | — |
| **Type** | grouped: Array, Bool, Bool, Int, Int, … | — |

⭐ **Access is provably NOT a string sort**, which is what the row is really asking. Ascending puts
all five `read` rows **first**; a lexicographic sort would begin with `"1W / 1R"` and put `"read"`
last. It orders on the write count as a number (`read`=0W < 1W < 2W).
⚠ **Honest limit:** the row's sharpest example is *"12W / 3R above 2W / 1R"*, i.e. a two-digit
count. This table's maximum is `2W` and Re maxes at 6, so the **two-digit** case is still untested —
string and numeric agree on single digits. What is proven is the `read`-vs-`1W` ordering, which no
string sort produces.

⭐ **No cell-recycling corruption across 8 reorderings.** In every ordering each row's Access / Re /
Scope / Type stayed glued to its own Property — `CallFunc_GetAllChildren_ReturnValue` always
`2W/4R, 6, ArrayProperty`; `CallFunc_Less_IntInt_ReturnValue` always `1W/1R, 2, BoolProperty`;
`VerticalBox_0` always the only green `instance`. That is exactly what the `supportsRecycling` bug
would break, and all 11 property names stayed distinct.

⚠ **The row says "6 headers each"; this dialog has 5** — `Access | Re | Scope | Property | Type`.
A `Cont` column exists in other instances but was not present here, maximised. Not a defect; the
row's count is what is off.

**Xref half — 🟡 the dialog is PROVEN FUNCTIONAL but no ≥2-row instance was found.**
`WBP_Battle_LvUpSkill_C::VerticalBox_0` → **1 row** (`instance | 2 | read | WBP_Battle_LvUpSkill_C`),
which retires the earlier worry that the Xref dialog never returns anything. Seven other fields
returned 0, and the pattern is now understood rather than mysterious: **DQ7R's Blueprint bytecode
touches widget-internal fields, while its gameplay data is C++-driven.** `DOLLGameCharacter::MaxHP`,
`WBP_Common_GaugeHp_C::FrontBar`/`BackBar` and `BP_BCAI_Monster_C::Probability_Gake` all → 0,
correctly (the dialog's own note says a native field returning empty is expected).

▶ **To finish the Xref half**, find a field that **two or more Blueprint functions** touch — most
likely a variable of a BP with several named functions plus an ubergraph, not a widget whose parent
class is C++. Cross-checking with a Props dialog first (it names the fields a given BP function
touches) is the cheap way to find one, rather than guessing fields.

-----

### ✅ V6 / U8 CLOSED 2026-08-22 `[V6U8-FNAMEPAIR-2026-08-22]` — both steps pass; the missing fixture was MANUFACTURED, not waited for

Run on **DumperTest** (AOT `dist` v1.0.0.3315, DLL 3315, UE504, 24,445 objects), CE 7.7.0.10568
attached alongside as an independent reader.

---

**Step 1 — auto-refresh preserves highlight, selection, scroll and stepping. ✅ PASS.**

This step had been attempted twice and closed neither time. What was missing was not the feature but
the **measurement**, and both gaps are now shut:

| the row asks | result |
|---|---|
| filter text survives auto-refresh | ✅ still `Name` after ~3 ticks at 6 s |
| match count survives | ✅ still `6 matches` |
| highlight survives | ✅ `0x1B0 Layers` still amber |
| selection survives | ✅ still `0x1C8 Tags` |
| **table does not jump to top** | ✅ — and this time **the check was real**: the grid was first stepped down to row `0x1C8` of a long list, so "unchanged" had something to fail at. ⚠ The 2026-08-22 earlier attempt recorded this as vacuous *because the table was already at the top*; do not accept that shape again. |
| **↑/↓ stepping still lands on highlighted rows** | ✅ pressing ▼ **after** the auto ticks advanced from `Tags` to the next match, `0x350 Name_Cjk`, and scrolled it into view |

⚠⚠ **The measuring instrument was the actual blocker, and it is worth repeating why.** The Live
Walker toolbar **reflows twice**: once when an object loads (`Find Refs` / `Related` appear) and
again when AOBMaker connects (`Copy CE Field` / `+CE Field (flat)` appear). Coordinates noted before
either event land on the wrong control — that is how "stepping is broken" was recorded when nothing
had been clicked. **Re-read the ▲/▼ position from the current screenshot immediately before each
click**; this run did, and stepping worked every time.

---

**Step 2 — Live Walker and Value Search agree on the same 8 bytes, suffix included. ✅ PASS.**

⭐⭐ **The fixture gap was dissolved rather than deferred.** DumperTest has **no** NameProperty whose
value carries a numeric suffix — measured three ways, not assumed: a game-wide Property Search
(`Name`, 375 properties) shows every `NameProperty` preview as `None` / `GameNetDriver` / `Spatial` /
`Custom`; `DumperTestActor_0`'s own `Name_*` test fields are `統一`-class strings; and
`Map_NameToInt`'s three FName keys are `Alpha` / `Beta` / `Gamma`, all `Number=0`. The obvious move
was to go find another game. **The cheaper and stronger move was to CREATE the case with CE and put
it back**, which also turns the step into a controlled experiment instead of an observation.

`DumperTestActor_0::NetDriverName` @ `0x1A0039C7A58`, offset `0x148`:

| state | CE raw bytes (third-party reader) | Live Walker | Value Search `Exact` |
|---|---|---|---|
| **as found** | `1D 04 00 00 00 00 00 00` (`ComparisonIndex=0x41D`, `Number=0`) | `GameNetDriver`, hex `1D04000000000…` | `GameNetDriver` → **267** hits, incl. this address |
| **after `writeInteger(a+4, 2)`** | `1D 04 00 00 02 00 00 00` (`Number=2`) | **`GameNetDriver_1`**, hex `1D04000002000…` | `GameNetDriver` → **266** hits, **this address GONE**; `GameNetDriver_1` → **exactly 1** hit, same address, same offset, instance `DumperTestActor_0` |
| **restored** | `1D 04 00 00 00 00 00 00` | — | — |

⭐ **`Number=2` rendering as `_1` is the correct UE convention** (display suffix is `Number-1`), so
the panel is not merely echoing bytes — it is decoding them.

⭐ **The 267 → 266 drop is the negative control, and it is the most informative line in the table.**
It proves Value Search's FName matcher **reads the Number field** rather than comparing
`ComparisonIndex` alone: the moment the Number moved, the row stopped being an exact match for the
bare name. Had the count stayed 267, the "same 8 bytes" agreement would have been vacuous — two
panels can agree while both ignore the same half of the value.

⭐ **CE is what makes this more than self-agreement.** Comparing Live Walker against Value Search
compares two consumers of the same DLL; the raw `readBytes` is outside that path entirely.

ℹ️ **Incidental, and it belongs to the known open family, not to this row:** the **Instances** panel
listed the actor as `DumperTestActor` while **Live Walker** and **Value Search** both call it
`DumperTestActor_0`. That is the `Serie::GetString` dropped-`Number` family — same defect shape as
this row's subject, seen on an object name instead of a property value. Not raised as new.

ℹ️ Tooling note that cost a few minutes: Property Search's **Type filter** takes short tokens (its
own hint says `opt`, `weak`); `NameProperty` matches nothing and returns an empty grid that looks
like "no such fields exist". Search on the property **name** and read the Type column instead.

### ✅ U16 CLOSED 2026-08-22 `[U16-ENUM65-2026-08-22]` — a 65-member enum renders whole in CE, and this row's own ceiling claim was wrong

Run on **DQ7R** (`dist` AOT v1.0.0.3315, DLL 3315, UE427, 149,370 objects) against the target the row
itself names: `DefaultPhysicalMaterial` @ `0x22715637100` → `SurfaceType` (ByteProperty @`0x60`),
whose `EPhysicalSurface` table is **65 entries** — past the 63 the row asks for.

**Step 1 — four independent witnesses, and they agree.**

| witness | says |
|---|---|
| the DLL's own log | `ResolveEnumValue: UEnum 0x226D106A8E0 — read 65 of 65 entries (legacy)` |
| the exported CE XML (clipboard, counted programmatically) | 65 `<DropDownList>` lines, indices `0..64` **contiguous**, `0:SurfaceType_Default` … `64:EPhysicalSurface_MAX` |
| **CE's own parse**, asked in its Lua Engine | `n=65 offby0=0 dups=0 i32=32:SurfaceType32 i63=63:SurfaceType_Max` |
| **CE's rendered dropdown**, expanded on the live record | 0…64 all present, tail `63 : SurfaceType_Max` then `64 : EPhysicalSurface_MAX`, scrollbar at the end |

⭐ **The Lua witness carried a negative control, and it fired.** The same loop scored every entry
against `i+1` as well and returned `CTRLoffby1=65`. So `offby0=0` comes from a checker that
demonstrably *can* report mismatches, rather than from a loop that never fires. Without that, "no
gaps" would only have meant "nothing printed".

⭐ **Why CE's parse and CE's dropdown are counted separately.** They are the two halves this row
exists to separate: the first proves the XML we emit carries all 65 members, the second proves CE
*renders* them. Reading only the first would repeat the shape that made See-through a no-op — the
report and the reality computed by different paths.

⚠ Route note: `+CE Field (flat)` was greyed out because AOBMaker was **Offline**, so this went
through **`Copy CE Field`** → CE's address-list paste. That is the documented fallback and it
round-tripped intact. CE was attached to the game (`openProcess`, pid 11944) so the value resolved to
`0 : SurfaceType_Default` rather than `??`; the Change-value dialog was **cancelled**, nothing was
written to the running game.

**Step 2 — re-derived over the WHOLE log corpus instead of one run.** 294 walk logs across 5 hosts:

```
ResolveEnumValue resolutions      4,919
read N of M with N != M               0
GetEnumEntries: ... truncated read    0    (in any file in the folder)
largest table observed              212    (DQ7R)
```

⚠⚠ **Two corrections to what this row asserted about itself.**

1. **`DumperTest 的天花板就是 26` is wrong — it reaches 113.** `DumperTest/walk-0.log:212` reads
   `read 113 of 113 entries`, with 107 and 93 behind it. 26 was never a property of the host; it was
   a property of *which classes that one walk happened to touch*. The advice built on it — "a host
   whose ceiling is in the twenties cannot press this row" — would have retired a usable fixture.
2. **The row's own cheap screen inherits the same scope bug.** It greps `walk-0.log`, i.e. the
   CURRENT run only, while **127** archived `walk-*.log` sit beside it in that single folder. Screen
   the whole folder instead:

```bash
grep -rho "read [0-9]* of [0-9]*" "$LOCALAPPDATA/UE5CEDumper/Logs" | awk '{if($2!=$4)bad++; if($4+0>m)m=$4+0; n++} END{print n" resolutions, "bad+0" mismatched, largest "m}'
```

▶ The `🟡 PARTIAL` cell in `✅ DONE 2026-08-18 — U4 / U16 / U6 / F3` named exactly two gaps —
*"the largest table seen is 26 entries"* and *"the CE DropDownList half was not checked"*. **Both are
now closed**: the first by measurement (212 exists, and 65 was actually pushed through CE), the
second by the four witnesses above.

### ✅ `.CT` DLL discovery CLOSED 2026-08-22 `[CTDISC-SLOTS-2026-08-22]` — the MRU slot answers, and the comment above it was wrong

**Unblocked by the maintainer.** This row refused to run while a February `UE5Dumper.dll` sat in CE's
install folder — `[STALEDLL-2026-08-18]`(a). That file is **gone** (verified absent), so slot 6 can no
longer answer and the deferred recent-files slot finally gets its turn. ✅ **`[STALEDLL-2026-08-18]`(a)
is therefore CLOSED too** — its (b) half shipped 2026-08-19.

Run on **DumperTest** (alive, frames climbing — not the dead-engine trap), CE **7.7.0.10568**, with
every cheap slot deliberately emptied first: CE-folder DLL absent, no DLL in app-data, and
`dll-path.txt` **moved aside** (restored afterwards, machine left as found).

**Step 1 PASSES.** Loading the table from `File → Load Recent` and ticking `init`:

```
SLOT=8   DLL_PATH=D:\Github\UE5CEDumper\dist\UE5Dumper.dll
  1. [NOT SEARCHED] trainer folder                       - not launched as a trainer
  2. [NOT SEARCHED] folder of CE's last File > Open      - CE's Open dialog is empty
  3. [NOT SEARCHED] folder of CE's last File > Save      - CE's Save dialog is empty
  4. [no DLL]       folder of the running script         - "init <== ..." (id 102) :\
  5. [NOT SEARCHED] folder recorded by UE5DumpUI         - no folder recorded yet
  6. [no DLL]       Cheat Engine install folder          - C:\Program Files\Cheat Engine\
  7. [no DLL]       app data folder                      - ...\AppData\Local\UE5CEDumper\
  8. [FOUND]        folder of the most recent UE5CEDumper.CT in CE's recent-files list
                                                         - D:\Github\UE5CEDumper\dist\
  9. [not reached]  most recently opened cheat table     - same folder as slot 8
```

That is the row's expected sentence verbatim, and its warning case — *"若寫的是 CE 自己的資料夾，
代表這一步又沒測到"* — is now structurally impossible: slot 6 is listed `[no DLL]`.

⭐ **A second, independent witness came free.** The self-heal only fires for the **name-matched** MRU
slot, and it rewrote `dll-path.txt` with its **own** header — *"Written by UE5DumpUI at startup **and
by UE5CEDumper.CT after a manual pick**"* — where the file previously carried the UI-only header. So
the file on disk proves which slot answered, independently of the report that claims it.

⚠⚠ **The comment above slots 2/3 was FALSE, and measuring it is what made this run trustworthy.**
It claimed the dialogs are *"filled in by File > Open / File > Save, and **NOT** by double-clicking
the .CT or picking it from the recent-files menu, which is the whole bug."* Before/after on one
freshly launched CE:

```
BEFORE any load:   OpenDialog1=[]  SaveDialog1=[]
AFTER Load Recent: OpenDialog1=[D:\Github\UE5CEDumper\dist\UE5CEDumper.CT]  SaveDialog1=[same]
```

**`Load Recent` fills both.** The baseline is what makes that a measurement rather than a guess — the
first reading alone could have been state CE restored at startup, so CE was killed and relaunched to
get the empty side.

⭐ **Then why did the run above report them empty?** Because `openProcess()` **overwrites both with
the bare process name** — measured directly: `OpenDialog1=[DumperTest-Win64-Shipping]`, no path, no
extension. `extractFilePath` of a name with no separator is `""`, so slots 2/3 correctly fall
through. My own attach step is what emptied them, not the recent-files menu.

**So the order decides which slot answers, and both were run:**

| order | dialogs at chunk time | slot that answers |
|---|---|---|
| Load Recent → attach *(this run)* | bare process name | **8** — recent-files list |
| attach → Load Recent *(what the record itself instructs: "init ⇐ enable after process attached")* | the `.CT`'s full path | **2** — CE's last File > Open |

Both resolve to `D:\Github\UE5CEDumper\dist\`, so the chain is healthy either way and neither is a
defect. ⛔ **Do not "fix" one into the other.** The realistic user order is the second, which means
the MRU slot is rarely the one that fires — worth knowing before anyone concludes from a report that
the recent-files code is dead.

▶ **Fixed in `scripts/UE5CEDumper.CT`** (and the `dist/` copy refreshed, which `build.ps1:1004` also
does): the slot 2/3 comment now records the measurement, the CE build it was measured on, and the
ordering table, instead of the claim that measurement refuted.

### ✅ .CT DLL discovery —— 到底是哪一個 slot 答的 — **CLOSED 2026-08-22**，證據見上一節 `[CTDISC-SLOTS-2026-08-22]`

*優先度 **中** · ✅ **2026-08-22 已解除封鎖並跑完。**`C:\Program Files\Cheat Engine\UE5Dumper.dll` 已由維護者刪除（`[STALEDLL-2026-08-18]`(a) 一併關閉），較便宜的 slot 不再搶答，**slot 8（recent-files）如預期答出 `D:\Github\UE5CEDumper\dist\`**。*

| # | 做什麼 | 預期 |
|---|---|---|
| 1 | ✅ **2026-08-22 通過**（DumperTest + CE 7.7.0.10568）。`SLOT=8`，報告寫的正是「folder of the most recent UE5CEDumper.CT in CE's recent-files list」，且 slot 6（CE 自己的資料夾）列為 `[no DLL]`，這一列自己警告的 FAIL 情境已結構性不可能。⭐ 另有一個獨立見證：self-heal 只有**名稱吻合**的 slot 才會觸發，而它把 `dll-path.txt` 重寫成 **.CT 自己的** header。 | slot 報告寫「folder of the most recent UE5CEDumper.CT in CE's recent-files list」。<br>⚠⚠ **順序會決定是哪個 slot 答**（2026-08-22 兩種都量過，見上一節）：先接 process 再載入 .CT ⇒ dialog 帶著 .CT 路徑 ⇒ **slot 2** 答；先載入再接 process ⇒ `openProcess` 用**裸的行程名**覆蓋掉 dialog ⇒ 落到 **slot 8**。兩者都指向同一個正確資料夾，都不是缺陷。 |

### ✅ B29 —— 第三方 wrapper 存在時仍會正常注入 —— 步驟 1／2 **CLOSED 2026-08-23**（ReShade 實測，見 `[B29-PRODUCTNAME-2026-08-23]` 與 `[B29-LIVE-2026-08-23]`）；步驟 3（非 ASCII 路徑）仍缺樣本

*優先度 **中** · 需要：裝了第三方 dxgi.dll / dinput8.dll wrapper（例如 ReShade）的 UE 遊戲。*

| # | 做什麼 | 預期 |
|---|---|---|
| 1 | 把 ReShade 或任一第三方 `dxgi.dll` / `dinput8.dll` wrapper 放進 UE 遊戲資料夾。 | 遊戲資料夾內存在非我方的同名 DLL。 |
| 2 | 附加 CE，點 *UE5CEDumper: Inject && Connect*，並 grep `init-0.log` 的 `is loaded but is not ours`。 | 正常注入，且該行出現並指名那個外來模組。FAIL = 舊訊息 "already loaded … no injection needed"，之後 UI 連不上。 |
| 3 | 再用一款路徑含非 ASCII 字元的遊戲重做一次，看同一則訊息。 | 訊息裡的路徑完整顯示，不再變成 `EVERSPACE? 2` 這種問號。 |

> ### 🟡 STEP 3's FIXTURE BUILT 2026-08-24 `[B29-NONASCII-FIXTURE-2026-08-24]` — the sample exists now; the run needs a CE-plugin decision, and step 3 looks likely to FAIL
>
> The blocker was *"步驟 3（非 ASCII 路徑）仍缺樣本"* — no sample. `tools/verify/b29_nonascii_fixture.py`
> builds one: **`D:\測試\DumperTest`**, a real UE game folder, with the maintainer's ReShade
> `dxgi.dll` copied in beside the exe. Measured on the running process:
>
> ```
> QueryFullProcessImageNameW -> D:\測試\DumperTest\DumperTest\Binaries\Win64\DumperTest-Win64-Shipping.exe
> dxgi.dll                      D:\測試\DumperTest\DumperTest\Binaries\Win64\dxgi.dll   <-- third-party, from the game folder
> dxgi.dll                      C:\WINDOWS\system32\dxgi.dll                            <-- chain-loaded by ReShade
> ```
>
> ⛔ **A JUNCTION DOES NOT WORK, and it was the obvious first idea.** `mklink /J` is what closed
> `AC17`/`VOLUMEROOT` (a cross-volume junction standing in for a real mount point), so it was tried
> first here. Launched through the junction the process reports
> `D:\UE_Analyze_data\For Testing\…` — **Windows resolves the reparse point** and nothing non-ASCII
> ever reaches the module list. ▶ A rig built on a junction would have reported a confident PASS
> having tested an ASCII path. Recorded in the rig's header so it is not re-tried.
>
> ⭐ **A directory of HARDLINKS is a real directory.** The package is 767 MB; the fixture is 26
> hardlinks plus the one real `dxgi.dll` copy — `status` reports **2 files with `nlink == 1`**, i.e.
> ~5 MB of actual disk for a 767 MB game, and the non-ASCII path now survives. The wrapper is copied
> rather than linked so nothing here can reach the maintainer's own ReShade install.
>
> ⛔ **Why the row was NOT run to completion.** The message
> `CEPlugin: '%ls' is loaded but is not ours (path=%ls)` is emitted **only** from
> `OnInjectAndConnect` ([Methode.cpp:291](dll/src/Methode.cpp:291) / [:388](dll/src/Methode.cpp:388)),
> the CE-plugin Type-5 menu callback, and `Methode.cpp` is compiled into `UE5Dumper.dll` itself
> ([dll/CMakeLists.txt:256](dll/CMakeLists.txt:256)). Reading it requires **registering our DLL as a
> Cheat Engine plugin** — a persistent change to CE's configuration that re-creates the very
> `UE5Dumper.dll`-under-CE's-folder state `[STALEDLL-2026-08-18]`(a) was closed by *deleting*, and
> which blocked the `.CT DLL discovery` row until 2026-08-22. That is a maintainer call, not a side
> effect of a verification run.
>
> #### ⚠⚠ PREDICTED FAILURE, from source — `[NONASCIILS-2026-08-24]` (MED). File this BEFORE the run
>
> Step 3's expected result is *"訊息裡的路徑完整顯示，不再變成 `EVERSPACE? 2` 這種問號"*. Three
> measurements say it probably will not be:
>
> 1. **`Sein::WriteLog` formats with `vsnprintf`** ([Sein.cpp:499](dll/src/Sein.cpp:499)) — a
>    **narrow** formatter. `%ls` therefore converts the `wchar_t*` through the CRT's current locale.
> 2. **Nothing in `dll/src` ever calls `setlocale`** (grep: zero hits), so the conversion runs in
>    whatever locale the host left — the default `"C"` for most UE games, where any character above
>    127 cannot be represented.
> 3. **`Heiter.cpp` was already fixed for exactly this**, and its comment names the row's own example:
>    *"GetModuleFileNameA converts through the ANSI code page, which DESTROYS any character the code
>    page cannot represent: a real install at `…\EVERSPACE™ 2\…` logged as `EVERSPACE? 2`"*. Its fix
>    was to convert first — `Utf8Helpers::EncodeUtf16(...)` then `%s`
>    ([Heiter.cpp:341-343](dll/src/Heiter.cpp:341)) — **which is what `Methode.cpp:255` does not do.**
>
> ✅ **The `EncodeUtf16` route is confirmed working**, measured today on the fixture: with the DLL
> injected into the non-ASCII game, `init-0.log` reads
> `UE5Dumper DLL loaded | … | process: D:\測試\DumperTest\…` — **intact, no `?`**. So the logger's
> UTF-8 file writing is fine; it is the **`%ls` argument path** that is unconverted.
>
> ⚠ **And it is a FAMILY, not one line.** Six `%ls` sites log a PATH or a module name:
> `Methode.cpp:255`, `Genau.cpp:115`, `Lugner.cpp:50` + `:53`, `Lugner_Dinput8.cpp:55` + `:58`,
> `Macht.cpp:520`. The proxy ones matter most — `Lugner*` logs the real DLL it chain-loads, which on
> a non-ASCII install is precisely a path a user would need to read back.
>
> ▶ **Fix shape**: give each of those the `Heiter.cpp` treatment — `Utf8Helpers::EncodeUtf16(p,
> wcslen(p)).c_str()` with `%s`. ⚠ Do **not** "fix" it by calling `setlocale` in a DLL: that mutates
> CRT state for the whole host process.
>
> ℹ️ `PipeServer: Started on %ls` (`Fern.cpp:506`) is the one `%ls` that is safe by construction — its
> argument is the compile-time ASCII pipe name. Confirmed intact in the fixture's `pipe-0.log`; it is
> a positive control for "`%ls` works when the input is ASCII", not evidence about non-ASCII.
>
> ### ✅ STEP 3 CLOSED 2026-08-24 — first RUN AND FAILED (`[NONASCIILS-2026-08-24]`, MED), then FIXED and re-verified the same day. The prediction was confirmed and the symptom was worse than it said
>
> ⬆ **The block above is superseded.** It was filed as a prediction *before* the run, deliberately.
> The run has now happened: **UE5Dumper.dll registered as a CE plugin** (see below), CE 7.7 attached
> to the fixture, `Plugins ▸ UE5CEDumper: Inject & Connect` clicked. Step 3's expected result —
> *"訊息裡的路徑完整顯示，不再變成 `EVERSPACE? 2` 這種問號"* — **does not hold**.
>
> **What the log actually contains** (`Logs\cheatengine-x86_64\init-0.log`, build 3338):
>
> ```
> [11:27:18.675] [INFO] [CEP] CEPlugin: OnInjectAndConnect triggered
> [11:27:18.677] [INFO] [CEP]                                            <-- ENTIRELY EMPTY
> [11:27:18.678] [INFO] [CEP] CEPlugin: 'WINMM.dll' … (path=C:\WINDOWS\SYSTEM32\WINMM.dll) …
> [11:27:18.679] [INFO] [CEP] CEPlugin: 'VERSION.dll' … (path=C:\WINDOWS\SYSTEM32\VERSION.dll) …
> [11:27:18.683] [INFO] [CEP] CEPlugin: 'dxgi.dll' … (path=C:\WINDOWS\system32\dxgi.dll) …
> ```
>
> ⭐ **Not mojibake — a BLANK RECORD.** The empty line sits exactly where module index **24**, the
> third-party `dxgi.dll` at `D:\測試\…`, is processed (`EnumProcessModulesEx` order puts it *before*
> WINMM at 26 — verified by replaying the plugin's own enumeration). The arithmetic is exact: **4**
> proxy-named non-ours modules → **4** records → 1 empty (first) + 3 correct. The CJK bytes appear in
> the file in **no encoding at all** — UTF-8, UTF-16LE and CP950 all absent, and there is no `?`
> substitution either. Even the ASCII prefix `CEPlugin: 'dxgi.dll' is loaded but is not ours (path=`
> is gone. **Anyone grepping for a mangled path finds nothing whatsoever.**
>
> ⭐⭐ **THE CONTROL, and it is a single-variable one.** The *same* ReShade `dxgi.dll` (identical
> bytes, sha256 `b2945c29e7095491…`) was staged at an **ASCII** path, `D:\ZZAsciiCtl\DumperTest`, and
> the same menu item clicked against it:
>
> | | wrapper named? | records naming a module | EMPTY records |
> |---|---|---|---|
> | `D:\ZZAsciiCtl\…` (ASCII) | ✅ `'dxgi.dll' … (path=D:\ZZAsciiCtl\…\dxgi.dll)` | **8** | **0** |
> | `D:\測試\…` (non-ASCII) | ❌ never named | 6 | **2** |
>
> Same module, same code, same format string, same session — **only the path's characters differ**.
>
> **Mechanism, settled (`/MT` makes it deterministic, not merely likely):**
> `Sein::WriteLog` formats with narrow `vsnprintf` ([Sein.cpp:499](dll/src/Sein.cpp:499)). UCRT's
> `_wctomb_internal` returns `EILSEQ` for any wide value **> 0xFF** in the `"C"` locale; `printf`'s
> wide-string loop then sets `_characters_written = -1` and the tail writes `buffer[0] = 0`.
> `Sein.cpp:499` discards the return value, so an **empty** `msgBuf` is spliced in.
>
> #### ⚠ FOUR CORRECTIONS TO WHAT I WROTE IN THE PREDICTION — do not re-quote the earlier wording
>
> 1. **The symptom was wrong, in the worse direction.** I wrote "mangled or truncated". It is
>    **empty**. ⚠ With one exception that mangles *silently*: **U+0080–U+00FF** passes the C-locale
>    cast and emits the raw **Latin-1** byte, which is invalid UTF-8 inside a UTF-8 log (measured:
>    `EVERSPACE® 2` → `… 43 45 AE 20 32 …`). ⭐ And the irony: `Methode.cpp:199-203`'s own comment
>    cites **™ (U+2122)**, which is > 0xFF — so its own example produces a blank line, not the
>    `EVERSPACE? 2` it describes.
> 2. **My stated REASON was wrong, and the truth is firmer.** I wrote *"runs in whatever locale the
>    host left"*. Every DLL target sets `MSVC_RUNTIME_LIBRARY "MultiThreaded"` = **`/MT`**
>    ([dll/CMakeLists.txt:107](dll/CMakeLists.txt:107) and 6 more), a statically linked CRT with a
>    **private** locale the host cannot reach. `"C"` is **guaranteed**. Host locale,
>    `_configthreadlocale`, and the fact that this site runs inside CE (an FPC program) are all
>    irrelevant.
> 3. **The `Heiter.cpp` analogy is a FALSE PARALLEL.** Heiter's comment blames `GetModuleFileNameA` —
>    a Win32 **ANSI** conversion with a `'?'` default char, a different mechanism with a different
>    symptom. Heiter never had a wide string reaching a narrow formatter. Cite it as *"the correct
>    fix pattern"*, never as evidence about `%ls`.
> 4. **Blast radius is 4 of 13 `%ls` sites, not "six".** Corrected list in the fix section below —
>    and the named site is the **least** reachable of them.
>
> ⛔ **One agent-raised "anomaly" REFUTED before it could propagate.** The audit flagged that the run
> logs `InjectDLL returned FALSE` while the DLL demonstrably loaded 260 ms earlier. That is
> **documented, expected behaviour**: [Methode.cpp:374-380](dll/src/Methode.cpp:374) spells out that
> CE's BOOL "can be true on a real failure and false while the DLL is loaded and working", which is
> precisely why the code does its own module walk. Not a defect; not filed.
>
> ### ✅ FIXED + RE-VERIFIED 2026-08-24 — `[NONASCIILS-2026-08-24]` closed, and **B29 step 3 now PASSES**
>
> **The fix: convert before logging, at four sites.** `Utf8Helpers::EncodeUtf16(...)` + `%s`, which is
> what the DLL already does everywhere it gets this right.
>
> | site | why it needed it |
> |---|---|
> | [`Methode.cpp:255`](dll/src/Methode.cpp:255) | the row's own message. ⭐ `:260-261` already converted the *returned* values the same way — only the log line was left behind |
> | [`Macht.cpp:520`](dll/src/Macht.cpp:520) | ⭐ **the most exposed of the four.** Despite the field name and the `in '%ls'` wording, `m.name` is a **full install path** (`GetModuleFileNameW`), and it is reached from the ordinary **MA1 fallback** — no CE plugin, no third-party DLL, just a game installed under a non-ASCII folder. ⚠ needed a new `#include "Utf8Helpers.h"` |
> | [`Genau.cpp:115`](dll/src/Genau.cpp:115) | `TrySymbolExport` logs the module path |
> | [`Heiter.cpp:179`](dll/src/Heiter.cpp:179) | ⚠ **not ASCII-by-construction**, contrary to first reading: `IsCheatEngineExeName` anchors only the **first 11 characters**, so `cheatengine-測試.exe` would lose the entire warning explaining why auto-start was skipped |
>
> ⛔ **NOT changed, and the reasons are worth keeping** so nobody "finishes the job": `Genau.cpp:2645`
> is `swprintf_s` with a **wide** format string (`%ls` is the native case there, `Sein` is not
> involved); `Genau.cpp:2778`/`:2783` range over ASCII wide literals; `Fern.cpp:506` is the
> compile-time ASCII pipe name; `Lugner*.cpp` ×4 log `GetSystemDirectoryW` paths. **4 of 13 `%ls`
> sites**, not all of them.
>
> **THE RE-RUN — same fixture, same menu item, fixed DLL** (sha256 `0771fc02…`, 2,897,408 bytes,
> loaded into CE from `dist\` and confirmed by module walk):
>
> ```
> [11:56:46.289] [INFO] [CEP] CEPlugin: 'dxgi.dll' is loaded but is not ours
>                (path=D:\測試\DumperTest\DumperTest\Binaries\Win64\dxgi.dll) — not a UE5CEDumper proxy
> ```
>
> | | records naming a module | EMPTY records |
> |---|---|---|
> | before the fix, non-ASCII | 6 | **2** |
> | ASCII control (unchanged) | 8 | 0 |
> | **after the fix, non-ASCII** | **8** | **0** |
>
> The non-ASCII run now has exactly the ASCII control's shape. Step 3's expected result —
> *"訊息裡的路徑完整顯示，不再變成 `EVERSPACE? 2` 這種問號"* — **holds**.
>
> **A CI-runnable regression test, and it was SHOWN ABLE TO FAIL.**
> `dll/tests/utf8_helpers_test.cpp` gained `Test_NarrowVsnprintf_DropsWideNonAscii` (built by
> `build.ps1 -Target Test`, and that target is **`/MT` like the DLL**, so it is a faithful CRT
> replica). ⭐ It pins the **CRT behaviour the fix routes around**, not the fix's own output — which
> is what makes it a control rather than a restatement:
>
> * `snprintf(buf, n, "path=%ls", L"D:\\測試\\…")` returns **< 0** and leaves `buf[0] == '\0'`
>   (buffer poisoned with `0x7F` first, so "empty" is an observation, not a default);
> * the **same specifier and buffer** with an ASCII path succeeds and renders exactly;
> * ⚠ the silent half is pinned too: `L"EVERSPACE\u00ae 2"` **succeeds** and emits a bare `0xAE` —
>   invalid UTF-8 inside a UTF-8 log, with no error anywhere;
> * `EncodeUtf16` on the same wide path yields the expected UTF-8 bytes.
>
> ⭐ **Armed and observed failing**: flipping only the wide literal to ASCII fails **exactly 3** of the
> assertions — the two CRT-failure ones and the `EncodeUtf16` byte comparison — while the ASCII
> control and the Latin-1 case keep passing. Three failing rather than all seven is the point: a
> blanket failure would have meant the test was one assertion written seven times. Reverted
> byte-exactly (`git diff` = 52 insertions, 0 deletions); **265 pass / 0 fail**.
>
> ⚠ **`Macht.cpp` and `Genau.cpp` are closed BY CONSTRUCTION, not by live capture, and that is
> deliberate.** Neither has ever been observed with a non-ASCII argument in the whole log corpus, and
> forcing MA1/symbol-export on a non-ASCII-installed game is expensive and unreliable. They share one
> formatter with the site that *was* measured, and the unit test pins that formatter. Said here
> rather than implied, so nobody later reads this row as four live measurements.
>
> ℹ️ **The CE plugin was registered for this run and has been UNREGISTERED again** — CE's list is back
> to the two entries it had (`AOBMaker_CEPlugin.dll` enabled, `CE-Handwire.dll` disabled). Re-enable
> with `py tools/verify/ce_plugin_register.py register`. ⚠ It points at an **absolute path to
> `dist\UE5Dumper.dll`**, never a copy under CE's folder, so there is no second artifact to go stale
> (`[STALEDLL-2026-08-18]`); the trade is that **CE holds that file open while running**, so close CE
> before `build.ps1 -Target DLL`. That fails loudly with a link error rather than silently with a
> stale DLL, which is the right way round.

#### ✅ FIXED 2026-08-24 — and the predicate was already in the tree

`already_ours()` now imports **`b29_product_name.product_name`** rather than reimplementing the
check, so the rig and `Methode.cpp`'s `IsOurModule` cannot drift: PE VERSIONINFO `ProductName ==
"UE5CEDumper"`, enumerating `\VarFileInfo\Translation` instead of assuming a language block. The
name list survives as a **pre-filter only**, exactly as `nameLooksLikeOurs` is in the DLL. If the
helper cannot be imported the module is REPORTED rather than silently passed — an undecidable case
must not become a quiet yes.

⭐ **Both directions measured, on one DumperTest carrying a real ReShade `dxgi.dll`:**

| | before | after |
|---|---|---|
| game with a THIRD-PARTY `dxgi.dll` | ❌ refused, advice unfollowable | ✅ `loaded : HMODULE… LoadLibraryW returned non-NULL` |
| inject again with OUR `UE5Dumper.dll` mapped | ✅ refused | ✅ **still refuses**, naming `UE5Dumper.dll` |

The second row is the one that matters: it is easy to "fix" this by weakening the guard into
uselessness, and that would silently re-open `[STALEDLL]`. The guard is intact.

⭐ **Why this is worth a finding rather than a footnote:** it is `working-lessons` §2.x's shape — a
defect fixed in the product and left standing in the tool that verifies the product. A rig carrying
the bug it exists to detect will mask that bug's return.

### ✅ DragonSword GObjects layout CLOSED 2026-08-23 `[DSLAYOUT-BASEANCHOR-2026-08-23]` — the archive already held the proof, and the checkbox was looking for the wrong thing

The remaining PARTIAL asked: when the **FUObjectArray base** anchor hits, does the layout picker choose
`UE5-Extended` rather than the relaxed alternative? **It does, in all three archived DSClient sessions**,
and no game had to be launched to establish it.

`%LOCALAPPDATA%\UE5CEDumper\Logs\DSClient-Win64-Shipping\offsets-*.log`:

```
2026-08-16 19:48  ObjectArray: Layout 'UE5-Extended' detected (strict)   Num=278252  Max=10551296  NumChunks=5
2026-08-18 06:43  ObjectArray: Layout 'UE5-Extended' detected (strict)   Num=72604   Max=10551296  NumChunks=2
2026-08-18 16:01  ObjectArray: Layout 'UE5-Extended' detected (strict)   Num=275612  Max=10551296  NumChunks=5
```

⭐ **It really is the BASE anchor, proven from the bytes rather than from the address.** The DEBUG dump
`+00:000090E9000090EA +10:0000018C63E69D30 +20:0004349C00A10000 +28:00000005000000A1` decodes to
`Objects@+0x10 = 0x18C63E69D30`, `Max@+0x20 = 10,551,296`, `Num@+0x24 = 275,612`, `MaxChunks@+0x28 = 161`,
`NumChunks@+0x2C = 5` — byte-for-byte `{ "UE5-Extended", { 0x10, 0x20, 0x24, 0x28, 0x2C } }`
([Aura.cpp:313](dll/src/Aura.cpp:313)), and every decoded value equals what the log independently printed.
Had the anchor been `ObjObjects`, `Objects` would sit at `+0x00` and `"Default"` ([Aura.cpp:300](dll/src/Aura.cpp:300)) —
tried first — would have won instead.

⭐ **The counterfactual is what makes it a positive exercise rather than a lucky miss.** From the same bytes,
relaxed B reads `Num@+0x04 = 37,097` (the frozen disregard-for-GC count) with a valid `Objects@+0x10`, so it
**would** have matched; and `Max = 10,551,296` exceeds the old `0x800000` ceiling, so strict `UE5-Extended`
would have *failed* pre-2782. This host is exactly the shape the fix was written for.

⭐ **Two independent detectors, and a negative control that fires.** `Genau`'s validator — different file,
different log category — agrees in `scan-0.log`:
`ValidateGObjects: Valid at 0x7FF73D951870 (preset UE5-Extended, Num=275612, Max=10551296, ...)`, after
rejecting five decoys. And `Strict validation failed for all presets` occurs **0** times in DSClient while
firing in **10** files elsewhere in the same log tree, so the absence is meaningful rather than a bad grep.

⚠ **Why the row read open, and it is not a coverage gap:** its checkbox says *"address ending `…F8B0` rather
than `…F8C0`"*. Those literals came from the **2026-08-10 pre-patch binary**; run 3's address is
`0x7FF73D951870`, so a literal grep looks like a miss even though the substance passed.
`docs/test-games.md` had already recorded run 3 as *"accepted in the STRICT tier as preset `UE5-Extended`"* —
nobody connected it to this row. ▶ **Do not re-check by address suffix; check the layout name.**

ℹ️ **Forward-looking, recorded so the next session is not surprised:** DragonSword was **patched 2026-08-20**
(`appmanifest_4570720.acf` `lastupdated` = 2026-08-20 15:04), which is *after* the newest log. The cached
anchor is therefore stale — the machine JSON holds `peHash 691B0D9809EB2000` while the exe now computes
`71D66A9009EB1000` — so the next injection will re-scan from scratch rather than reuse `GOBJ_ES53_1`. That is
expected behaviour, not a defect, and it does not affect this closure: the row is about the DLL's layout
choice, not about the game build.

### ✅ B25 branch B RE-RUN on a REAL UE3 game, and G2 step 1 answered with it — 2026-08-23 `[UE3-GALGUN-2026-08-23]`

The maintainer installed **Gal\*Gun: Double Peace** (Steam appid 511740), which is a genuine
**Unreal Engine 3** title:
`D:\SteamLibrary\steamapps\common\GalGun Double Peace\Binaries\Win64\GG2Game.exe`, 53,640,704 bytes,
`CompanyName "Epic Games, Inc."`, `LegalCopyright "Copyright 1998-2012 Epic Games, Inc."`, with
`APEX_Clothing*` / PhysX-legacy DLLs beside it.

⚠ **This does NOT invalidate the earlier "no real UE3 binary exists here" measurement — it post-dates
it.** `appmanifest_511740.acf` records `lastupdated` = **2026-08-23 10:06:35**, and the 290-exe sweep
that found zero UE3 markers ran that morning, before the install. The absence claim was true when
made; the fixture was created afterwards. ▶ Worth remembering as the shape of a *correct* absence
result that later stops being true — the sweep was not wrong, the world changed.

**Branch B — ✅ PASSES on a commercial title, and more strongly than the synthetic did.**
`Logs\GG2Game\scan-0.log`, whole file **14 lines**:

```
DetectPublisher: no thumbprint match (Copyright='…1998-2012 Epic Games…', Company='Epic Games, Inc.')
DetectVersion: PE VERSIONINFO Product=1.0 File=1.0 — unrecognised
DetectVersion: PE resource failed, falling back to memory string scan
PreUE4: Epic copyright newest year 2012 (PRE-UE4) — 'Copyright 1998-2012 Epic Games, Inc. …'
PreUE4: marker 'SeqAct_ (UE3 Kismet)'      hit at 0x244DAF9
PreUE4: marker 'UnrealEngine3'             hit at 0x2729BD0
PreUE4: marker 'PhysXLoader64 (PhysX 2.8)' hit at 0x2AA61D0
DetectVersion: PRE-UE4 engine POSITIVELY identified (4/4 markers, 2 needed) -> sentinel 300.
FindAll: UE Version = 300 (tier=1, detected=yes, lowConfidence=no, publisher=-)
FindAll: PRE-UE4 engine (Unreal Engine 3) — SKIPPING the scan.
```

⭐ **4/4 markers, where `b25b_ue3.exe` could only manage 2/4** — the synthetic carried the two string
literals; a real UE3 build also trips the PhysX 2.8 loader and the ≤2013 Epic copyright. So the
marker table is now exercised across its whole width on a binary nobody constructed for it.

⭐ **14 lines vs branch A's 3,886 is still the cleanest discriminator** between *"scanned and accepted
with low confidence"* and *"refused before starting"*.

⭐ **An offline pre-check agreed with the DLL, independently.** Before injecting, a byte scan of the
exe found `UnrealEngine3` ×3 (ANSI) + ×1 (UTF-16LE), `SeqAct_` ×10, `PhysXLoader64` ×1 — and the
controls `UnrealEngine4` and `FUObjectArray` at **0**. The DLL then reported the same markers at
concrete addresses.

**G2 step 1 — ✅ ANSWERED, by the same run.** That row asked to separate the version-string scan from
`CountPreUE4Markers`' own full-file pass, and explicitly suggested *"a pre-UE4 game whose check ends
early"*. This is that game:

| span | measured |
|---|---|
| `PE resource failed, falling back to memory string scan` → next `[SCAN:Ver]` line | **277 ms** |
| the same start → `UE Version = 300` verdict (**includes** `CountPreUE4Markers`) | **316 ms** |

Bar was *"單獨的版本字串掃描本身在 1 秒以內"* — comfortably met. Game and exe size recorded as the row
asks: Gal\*Gun: Double Peace, `GG2Game.exe`, **53,640,704 bytes**.

⚠ **Honest bound:** the next `[SCAN:Ver]` line is itself emitted *from* `CountPreUE4Markers` (the
copyright check), so 277 ms is an **upper bound** on the string scan alone, not an isolated figure.
Since the bar is 1000 ms the conclusion holds a fortiori — but the row's original ask, a dedicated
divider log, would still be the way to get an exact number. ▶ The row's caveat about the earlier
**2.4 s** figure is now explained rather than merely suspected: here the *entire* fallback-to-verdict
window, both scans included, is 316 ms on a 53.6 MB image.

ℹ️ Injected through CE + `dist\UE5CEDumper.CT` (init → Inject DLL). No CE plugin was needed or
installed for this row, and CE's `Plugins64` was re-checked afterwards — still only AOBMaker
(enabled) and CE-Handwire (disabled).


### ✅ B25 CLOSED 2026-08-23 `[B25-RECHECK-2026-08-23]` — both branches already passed, and the row's own step-1 alternative is impossible

Bookkeeping, not work: the ⬜ was a copy artifact of the 2026-08-22 migration, whose preamble says the tables
were *"moved VERBATIM, including the ✅/🟡 status cells"*. The result has existed since
`[B25-SYNTH-2026-08-19]`. Re-read off disk today rather than trusted from the doc:

| branch | file | what it says |
|---|---|---|
| **A** sub-floor | `Logs\b25a_subfloor\scan-0.log` | `PE VERSIONINFO says UE 405, below the 411 floor — NOT accepting that on its own …` then `UE Version = 405 (tier=3, detected=yes, lowConfidence=yes)`. **3,886 lines**, `SKIPPING the scan` = **0** — it swept the tables. |
| **B** UE3 control | `Logs\b25b_ue3\scan-0.log` | `PRE-UE4 engine POSITIVELY identified (2/4 markers, 2 needed) -> sentinel 300` then `FindAll: … SKIPPING the scan … a refusal by design, not a scan failure`. **10 lines** total. |

⭐ **3,886 lines vs 10 is the whole point** — it separates *"scanned and accepted with low confidence"* from
*"refused before starting"* far more sharply than either log line does on its own.

⭐ **The evidence is NOT stale, and that was checked rather than assumed.** It was taken on dist 1.0.0.3263
(`10b00cf8`); dist is now 3315. But `git log 10b00cf8..HEAD -- dll/src/Genau.cpp dll/src/Genau.h
dll/src/Grimoire.h` returns **0 commits** and the diff is **empty** — the code under test is byte-identical.

⚠⚠ **Correction 1 — step 1's "用 UE 版本 override 硬造" alternative cannot work, by construction.** The refusal
gate is `if (UEVersion < MIN_SUPPORTED && bVersionDetected && !bLowConfidence && !bUserOverride)`, and the
override arm sets `bUserOverride = true` **without ever calling `DetectVersionDetailed`** — so an override
neither prints the floor line nor arms the refusal it is supposed to probe. The PE-resource route is the only
provocation, which is what the 2026-08-19 run used. ▶ Delete that clause rather than trying it.

⚠ **Correction 2 — no real UE3 binary exists on this machine, measured.** 290 executables byte-scanned for the
marker table (`UnrealEngine3` narrow + UTF-16LE, `SeqAct_`, `PhysXLoader64`): **0 hits**. Positive control by
the identical test: `b25b_ue3.exe` returns `['UnrealEngine3','SeqAct_']`, `b25a_subfloor.exe` returns `[]` —
so the strings and the test are right and the markers are simply absent. Honest limit: 23 exes >250 MB were
not scanned (modern UE4/UE5 titles by identity), and a packed section could hide the literals, so this is a
strong *"I could not find one"*, not a theorem.

▶ **A stronger branch-A re-run is available and has never been used**: `D:\UE_Analyze_data\Varies Version
builds\4.10\Shipping\UE4Game-Win64-Shipping.exe` — a genuine Epic-signed x64 binary, ProductVersion 4.10.4.0,
so `400+10 = 410 < 411`. Checked that it will not trip the UE3 gate by mistake (no pre-UE4 markers;
LegalCopyright 2015 > the 2013 marker cutoff; no `++UE4+Release-` needle → tier 3 → lowConfidence → gate stays
disarmed). Optional corroboration only; the row is closed either way.

⚠ **Re-run trap if branch A is repeated on the SAME exe**: `b25a_subfloor.exe` is now cached
(`ueVersion 405, lowConfidence true, versionDetectRev 5`, matching `Genau.h`'s `kVersionDetectLogicRev = 5`),
so a second run takes the cached arm, logs `— skipped DetectVersion`, and the floor line never reappears —
proving nothing. The PE hash is `TimeDateStamp + SizeOfImage`, so simply **rebuilding via
`tools/verify/b25_marker_exes.py` mints a new hash** and a fresh entry; no need to hand-edit the machine JSON.


### ✅ Y11 CLOSED — step 3 FIXED **and LIVE-VERIFIED** 2026-08-23 (build 3319); the other 3 steps passed 2026-08-22 `[Y11-OPAQUEDROP-2026-08-22]`

Run on **DQ7R** (`dist` AOT v1.0.0.3315, DLL 3315, UE427, 149,370 objects), on a live plain
`TextBlock` at `0x28F42DD4240` — DumperTest could not serve this row (no live `TextBlock`, and its
own classes declare zero UFUNCTIONs).

| step | verdict | evidence |
|---|---|---|
| **1** FText param, field left at `0`, FIRE | ✅ **PASS** | Refused, and the message names the type and the reason: *"InText: FText parameters cannot be invoked from this dialog — an FText holds a shared reference the engine allocates, and sending a zeroed one crashes the game. Invoke a wrapper that takes an FString instead."* ⭐ **The game did not crash**, which is the point of the gate. |
| **2** struct param untouched, FIRE | ✅ **PASS**, twice | `SetShadowOffset(FVector2D)` → `ProcessEvent OK`, post-call buffer `X=0, Y=0`. `SetStrikeBrush(FSlateBrush, 136 B)` → `ProcessEvent OK`, buffer all zeros across every member. |
| **3** type `42` into that field, FIRE | ❌ **FAILS AS WRITTEN** | Not refused. `ProcessEvent OK (result=0)` and the post-call buffer reads **`ImageSize=0x0`** — the typed value was **silently discarded**. Repeated on `.Margin` with the same result (`Margin=00-00-…-00`). |
| **4** control: ordinary params still fire | ✅ **PASS** | `SetShadowOffset` with `X=5, Y=7` → `ProcessEvent OK`, buffer `X=5, Y=7`, `raw: 0000A0400000E040` (= 5.0f / 7.0f little-endian). |

⭐ **Step 4 is what makes step 3 a finding rather than a shrug.** The dialog demonstrably *does* write
typed values into supported fields — 5 and 7 landed in the raw bytes. So the silence on the opaque
struct fields is specific to those, not a dialog that ignores input generally.

**What is actually fixed, and what is not.** The row's stated danger was that the typed text *"would
be written as an int32 straight onto the struct's Data pointer and handed to ProcessEvent"*. That is
**gone** — provably, because the post-call buffer is all zeros, so nothing was written. The safety
half of the fix is real.

What is missing is the **honesty** half the row asks for: the user types `42`, presses FIRE, and is
told `ProcessEvent OK` with no indication that their input was dropped. That is this repo's
recurring shape — the report and the reality computed by different paths — in its mildest form.

▶ **The fix is a message, not logic.** Either refuse the FIRE while an opaque `[struct]` field holds
a non-default value (what the row expects), or keep sending and say plainly that the field was
ignored because the type cannot be marshalled. ⛔ **Do not "fix" it by writing the value** — that is
precisely the behaviour that was removed.

**✅ FIXED 2026-08-23, build 3319 — and the root cause was narrower than 'a missing message'.**
The dialog *did* validate, via `TryValidateScalar`, but only in its **scalar** branch. Its **struct**
branch called `WriteStructParam` straight through, and that forwards every sub-field to `WriteParam`,
whose opaque-type guard is a **silent early return**. `WriteParam`'s own comment asserted the check
*"refuses ahead of this whenever the user actually typed something"* — true of a top-level param,
false of a sub-field, and that false comment is why the hole survived.

⭐ **The guard now lives with the write it protects**, as `ParamBufferBuilder.TryValidateStructSubFields`,
rather than being duplicated in a View — so the two cannot drift, and it is testable without an
Avalonia window. The dialog refuses and **names the member**:
`ERROR: NewBrush.ImageSize: struct parameters cannot be built from a textbox …`.

⚠⚠ **It closed a second hole that nobody had reported.** The same unvalidated path meant an
out-of-range **integer** sub-field masked to width — 9999 into a 1-byte member fired as 15. That is
the width family (W6/Y2/Y9/Y15/AE1) surviving in the one place its fix had not been applied.

⛔ **Not fixed by writing the value** — that is the original Y11 defect and a test now pins the
silence as *correct*: `Y11_SubField_TypedValueIsDroppedSilently_WhichIsWhyTheGateExists` asserts
`WriteStructParam` leaves the buffer zeroed, so a future 'helpful' write breaks a test.

⭐ **Four new tests, and the negative control was RUN, not asserted.** Neutering
`TryValidateStructSubFields` to `return true` made exactly the two refusal tests fail (2 failed /
2 passed) — the defaults-pass and silent-drop tests correctly do not depend on the guard. Restored,
all 21 Y11 tests pass; full suite green (C++ 1644/0, C# all); `dist` republished AOT-trimmed at
**54.7 MB**.

✅ **LIVE-VERIFIED the same day on DQ7R** (UE427, 149,370 objects, DLL **3319**, AOT `dist` **3319**,
proxy refreshed to match), on the original fixture — a live plain `TextBlock` (2 of them, as the
2026-08-22 note said) → `SetStrikeBrush (ParmsSize=136)`. **Three fires, and the middle one is the
one that matters:**

| # | input | result |
|---|---|---|
| 1 | `42` into `.ImageSize [struct]` | ❌ refused — `ERROR: InStrikeBrush.ImageSize: struct parameters cannot be built from a textbox — this is a multi-word structure whose contents must be allocated inside the game, and the value you typed would be dropped. Clear the box to send an empty/zeroed value instead.` **No `ProcessEvent OK`.** |
| 2 | ⭐ **control** — the same box back at `0` | ✅ `[#2] ProcessEvent OK (result=0)`, post-call buffer all zeros |
| 3 | `9999` into `.DrawAs [uint8]` | ❌ refused — `ERROR: InStrikeBrush.DrawAs: 9999 does not fit in this 1-byte parameter (range: -128 to 255)` |

⭐ **Fire #2 is what makes #1 and #3 mean something.** A gate that refused everything would satisfy
the row's wording while breaking step 2, which passed on 2026-08-22 — the zero-default path still
fires, so the refusal is specific to a value the user actually typed.

⭐ **Fire #3 is the second hole, seen in the field.** Before this change 9999 masked to **15** and was
sent silently; that is the width family (W6/Y2/Y9/Y15/AE1) in the one place its fix had never been
applied, and it was never reported by anyone — it fell out of putting the guard where the write is.

ℹ️ Fixture notes worth keeping: `TextBlock::SetText` is the FText candidate on any UMG title
(`ParmsSize=24` on UE 4.27, not 16). DQ7R has **3,159** live `LocalizeTextBlock` instances but that
subclass declares only 3 functions of its own — the Functions table lists a class's **own**
functions, not inherited ones, so walk a **plain `TextBlock`** (DQ7R had 2 live) to reach `SetText`.

-----

### ✅ SPENT — Y11's FIRE path `[Y11-FIREPATH-2026-08-22]`

> **Y11 CLOSED 2026-08-23** (build 3319) — step 3 fixed AND live-verified. The hunt recorded below is
> what made that possible; kept for the route, not as outstanding work.

Not executed yet, but the access problem that stalled it is solved and written down so the next
attempt does not repeat the hunt.

**FIRE lives in `InvokeParamDialog`, reached from Live Walker → the `Functions` expander → `PIPE`.**
It is not on Interesting Funcs rows and not on Instance Finder rows — two places I looked first.
(`⚙` on an Interesting Funcs row is *locate in GameEngine*; `Find Func` on an Instance row is the
*functions-taking-this-class* query. Neither invokes anything.)

⭐ **Why an earlier session recorded "面板上找不到函式表入口"** (AF16–AF23 step 1's note): the section is
`<Expander IsVisible="{Binding HasFunctions}">` at the bottom of `LiveWalkerPanel.axaml`, so it is
**invisible when the walked object's class has no UFunctions** — and `DumperTestActor` has **zero**.
Walking `PlayerController` (live instance `0x2431B6A4980`) makes it appear immediately: **162
functions**, each row carrying `INV` / `PIPE` / `AA(Baked)`. The entry point was never missing; the
object was the wrong one.

⚠ **Remaining fixture question for Y11 itself.** Step 1 needs a UFunction taking an **`FText`**
parameter *on a class with a live instance*. `TextBlock::SetText` (1 param, 16 B) is the obvious
candidate but DumperTest has **no live TextBlock** — the UI said so plainly: *"No live (non-CDO)
instance of TextBlock to locate in GameEngine."* So either find an FText-taking function among
`PlayerController`'s 162, or run Y11 on a UMG-heavy title.

