# Archived todo sections — closed 2026-09-03 (build 3369)

Sections lifted **byte-identical** out of [`../todo.md`](../todo.md) on 2026-09-03. Every one
carries a `[TAG-YYYY-MM-DD]` closure tag in its own heading and no open-work sentence in its body.

⛔ **Kept byte-identical on purpose — do not repair anything in here**, including the relative links,
which were written relative to `docs/` and are therefore wrong from this folder. Repairing them
would break the one property that makes an archived file trustworthy: that it is what was written.

**How these were selected** (the rule matters more than the list — a previous pass measured a 58%
false-positive rate for selecting on the heading marker alone):

1. heading starts `✅` or `⛔`, **and**
2. the heading itself announces the closure — a dated `[TAG]` or a closure verb, **and**
3. the body contains no still-open sentence (`still open` · `remains OPEN` · `only step N` ·
   `NEEDS A LIVE CHECK` · `尚未` · `仍未` …), **and**
4. it is not a pointer stub under 12 lines, **and**
5. it carries none of the keys `tools/check_live_verification.py` tracks.

That rule selected 12 of the 52 closed-marked sections. **40 were held back** — 21 for a still-owed
sentence, 12 as pointer stubs, 6 because the heading announced no closure (in this file `⛔` marks
live warnings as well as refutations — `⛔ PRECONDITION FOR EVERY GAME ROW` is a standing precondition,
not a closure), and 1 for carrying a gate key.

To find a closure, grep its `[TAG]` across `docs/` — tags have always resolved across files, so
nothing about the lookup changed when these moved.

-----

### ✅ BISECTION CLOSED 2026-07-29 — the edge is **5.3 → 5.4**

Stock UE 5.4.4 ThirdPerson (all three configs) settled it in **one** install, not the two this item
budgeted for. 5.3 Dev/DebugGame land `GNAM_ES53_1` with **15/15 patterns correct and zero** wasted
validations; 5.4 Dev/DebugGame drop to **1/6 correct, landing `GNAM_V1` after 2,240** — already the
full collapse, indistinguishable from 5.7.4/5.8.x. Both 5.4 configs report the identical 2,240,
consistent with UE building DebugGame's engine modules optimized like Development.

So **5.5 and 5.6 are no longer needed for this question.** Whatever they add is coverage, not
bisection. And if a fix pattern is ever mined, **5.3-vs-5.4 is the pair to mine it against** — the
smallest interval that contains the change, with a clean control on one side.

4.10 and 4.15 extend the healthy band downward, so this is a sharp UE5-era edge, not a slow drift.

**Not project-specific.** A second, unrelated 5.8.0 DebugGame project (Titan) matches StackOBot's
coverage *down to the individual pattern IDs* — same GObjects quartet, same GWorld n=13, same
GEngine quartet, GNames 0 with the same `{CT3, CT4, G42_1}` decoy. Build configuration is the only
remaining variable.

Root cause is a hardcoded destination register — the `GOBJ_V1`-on-DropIn failure mode one target
over. The twin-LEA lazy init is there (46 xrefs to `NamePoolData`), but the first LEA targets
**rbx (`48 8d 1d`) / r15 (`4c 8d 3d`)** and every GNames pattern pins rax/r8/rdx/rsi/rbp.

Decision needed, because it is genuinely marginal:
- **Do nothing** (default). Nobody attaches to a Development build of a template project, and the
  candidate fix `4? 8d ?? <d32> eb ?? 48 8d 0d <d32> e8` has only ~2 literal bytes at its head —
  in the band where `GWLD_G42_4` proves wildcarding backfires.
- **Or mine it**, and put it through the full 65-program gauntlet before it goes anywhere near the
  table. If it survives decoy-free it is also insurance for a shipped game whose register pressure
  happens to land the same way.

All four affected rows are in `sweep.sh` and swept, so the cost is visible as a ⚠️ with its wasted
count in the regression matrix instead of being invisible. **Leave them showing that** until fixed
— note they are ⚠️ (lands correct, expensively), not ❌; the only ❌ in the corpus is 4.10 GObjects.

**Second result from the same pass, and arguably the more important one — rule 5 just paid out.**
The sparse-delegate patterns that were kept purely as redundancy are the *only* thing holding that
target up on non-Shipping builds: on 5.8 `SPARSE_ES2_1` misses and **`X1`/`X2` alone** reach it,
and on **5.7.4 DebugGame those miss too and `SPARSE_MEL55_1` is the sole survivor (n=1)** — the
thinnest coverage anywhere in the corpus. Every one of the three was added against a Shipping
binary that already resolved, i.e. they looked like dead weight at the time. Had any been pruned,
a whole build configuration would have silently lost sparse-delegate support.

All three of the non-Shipping oracles this item was written against (5.7.4 DebugGame, 5.8.0
DebugGame, 5.8.0 Titan DebugGame) are imported `-noanalysis` and swept — the sweep reads raw bytes
and never needs auto-analyze, which is what had made a 300 MB Development project look
un-importable.

-----


-----

### ✅ FOUND + FIXED 2026-08-23 `[V8PREVIEWCLIP-2026-08-23]` — the FOURTH disclosure site was invisible at the default column width

Found by the very look this row exists for, and it is the failure mode the row **names**:

> *那些測試斷言的是 **ViewModel 的字串**，不是畫面上的像素 …… 同一天 `[PARAMSSORT-2026-08-22]` 撞到的：
> 快照那句提示 VM 字串完全正確，卻被放在沒有 `TextWrapping` 也沒有 `ToolTip` 的 `TextBlock` 裡，
> 自己被截斷。*

There are **four** sites, not three. `AuditL11HonestyTests.V8_SyntheticRowMapField_CarriesBadgeBeforeTheClick`
says so in its own comment — *"The preview row is what the user clicks to drill in, so the cap
belongs here too"* — and asserts the badge is in the pre-click preview string.

It is in the string. It is not on the screen:

```
LiveWalkerViewModel.DataTableFieldPreview(dt) =>
    $"{{DataTable: {dt.RowCount} rows, {dt.RowStructName}}}"
    + ContainerTruncation.BadgeSuffix(dt.Rows.Count, dt.RowCount);
```

so the full value is `{DataTable: 100 rows, DumperTestTableRow}  ⚠ showing 64 of 100` — badge
**last**. And the cell it lands in (`Views/LiveWalkerPanel.axaml:587-595`):

```xml
<DataGridTemplateColumn Header="{StaticResource str.LiveWalker.Value}" Width="200" MinWidth="100">
  <TextBlock Text="{Binding DisplayValue}" VerticalAlignment="Center" Margin="4,0"/>
```

Fixed **200 px**, a bare `TextBlock`, **no `ToolTip.Tip`**, no trimming ellipsis. Observed on screen:
`{DataTable: 100 rows, D` — cut mid-word, ~22 characters in. The prefix alone overflows the column,
so the badge is **structurally unreachable at the default width** on every table, at every N.

⚠ **Honest severity: LOW, and the reason matters.** The other three sites disclose correctly, so a
user who clicks through *is* told. The column is user-resizable (`CanUserResizeColumns="True"`), so
the text is recoverable, just not by default. Nothing is silently wrong — this is discoverability,
not a lie. That is why it is filed rather than hot-fixed.

⛔ **Not fixed, because the fix is a UX choice and it is the maintainer's, not mine.** Two defensible
one-line options:

1. **`ToolTip.Tip="{Binding DisplayValue}"` on that `TextBlock`.** Fixes every clipped value in the
   Live Walker grid, not just this badge — the general problem. Cost: a hover tooltip on *all* value
   cells, duplicating short values that were never clipped.
2. **Put the badge FIRST** in `DataTableFieldPreview` — `⚠ showing 64 of 100 {DataTable: …}`. Keeps
   the grid untouched and guarantees the important half is inside 200 px. Cost: the preview reads
   oddly when nothing is capped is not an issue (`BadgeSuffix` is empty then), but it inverts the
   established `…{value}{badge}` ordering the other three sites use.

I lean **(1)**: it repairs the class of defect rather than this instance, and `[PARAMSSORT]` was the
same shape in a different panel. But it changes behaviour across a whole grid, so it wants a
decision rather than a drive-by.


-----

### ✅ FOUND + FIXED 2026-08-23 `[DTROWMAP-2026-08-23]` — the DataTable drill-down read the NEIGHBOURING table's rows

**Found within minutes of the new DumperTest fixture going live, by V8's negative control** — the
8-row table that was only there to prove the ">64" banner *stays away*. It never got that far: the
100-row table reported **8 rows**, and the 8-row table reported **nothing at all**.

`Ubel::ProbeRowMapOffset` ([Ubel.cpp:6137](../dll/src/Ubel.cpp)) locates `UDataTable::RowMap` by
scanning memory, because `RowMap` is a protected non-`UPROPERTY` (`TMap<FName, uint8*>`) and there
is no reflection entry to look it up with. It scans **forward from the end of the reflected fields,
+0..+256 in 8-byte steps**, and accepts the first candidate whose `TSparseArray` validates (real
FName row name, userspace row pointer).

Measured on DumperTest Shipping (UE 5.4, build 3322), `UDataTable`:

| quantity | value | where from |
|---|---|---|
| `props_size` (object size) | **176** | `walk_class` on the `DataTable` UClass |
| `endReflected` = max(offset+size) | **152** | its 5 reflected fields; `ImportKeyField` @136 +16 |
| scan range | **152 … 408** | `endReflected + 0..256`, Ubel.cpp:6156 |
| real `RowMap` offset | **48** | raw process read, see below |

**Two defects, and they are independent:**

1. **The scan can never reach the target.** `RowMap` is declared immediately after `RowStruct`, so
   in a **cooked** build — where the `WITH_EDITORONLY_DATA` members between them are stripped — it
   lands at offset **48**, in the gap between `RowStruct` (40..48) and the bools (128). The scan
   starts at 152 and only goes *forward*. On any cooked `UDataTable` the true RowMap is
   **structurally unreachable**. This is not a tuning problem; +256 more bytes would not help.

2. **The scan is not bounded by the object.** The object ends at `props_size` = 176; the scan runs
   to 408, so **232 of its 257 candidate offsets are outside the object**. `PropertiesSize` was in
   hand the whole time — the probe's signature is
   `ProbeRowMapOffset(uintptr_t dataTableAddr, const ClassInfo& ci)` and `ci.PropertiesSize` is a
   field on that very struct ([Ubel.h](../dll/src/Ubel.h), `struct ClassInfo`).

⭐ **Together they produce a confident wrong answer, not an error.** DataTables are typically
allocated near each other, so the out-of-bounds scan lands in *another* `UDataTable` — and a real
RowMap validates perfectly. Proven by reading the process directly, with the DLL out of the loop
(`ReadProcessMemory`, no pipe involved):

```
Table_Big   @0x16D013D1780   +48  -> Data=0x16CFB13D580  ArrayNum=100   <- the real RowMap
Table_Big   @0x16D013D1780   +240 -> Data=0x16D046A0F40  ArrayNum=8
Table_Small @0x16D013D1840   +48  -> Data=0x16D046A0F40  ArrayNum=8     <- byte-identical
```

`0x16D013D1780 + 240 == 0x16D013D1840 + 48`. The two tables sit exactly 192 bytes apart, so the
probe walked 48 bytes past the end of `Table_Big` and read **`Table_Small`'s RowMap**. The DLL then
reported, for a 100-row table: `row_count: 8`, `row_map_offset: 240`, and eight rows named
`Row_000`…`Row_007` with correct-looking `Index`/`Label`/`Value` — *the other table's data,
rendered as this table's*.

The lone-table case gives the other failure: `Table_Small` has no DataTable at +240 and the probe
returns `"RowMap not found by probing"`, i.e. the feature is simply dead there.

⚠ **Two detectors, and the second is what makes this reportable.** The DLL's own reply is one
witness; `ReadProcessMemory` from outside is the other, and it is the one that establishes what the
right answer *was* (100). A row count alone could never have shown this — 8 is a perfectly
plausible number for a DataTable.

ℹ️ **`Caption` (the FText column) comes back `null`** in every row of that walk. Not chased yet, and
it may well be downstream of reading the wrong table. Re-check after the fix before treating it as
its own defect.

⚠ **This blocks `V8` as written, and MG2 step 2's "open any UDataTable" half.** V8 asks whether the
drill-down caps at 64 of N; that premise assumes it finds the right N. It does not. V8 cannot be
judged until this is fixed.

⚠ **No test compiles `Ubel.cpp`** (the C++ suite is two header-only test files — audit #5 §0), so
nothing was going to catch this offline. The fixture caught it in the first ten minutes of being
alive, which is the argument for building fixtures rather than hunting for games.

**Fix shape** (not yet applied): bound the scan by `ci.PropertiesSize`, and scan the *whole* object
rather than only the tail — the gaps between reflected fields are exactly where a non-reflected
member lives. Prefer scanning the reflected gaps first. Keep the existing validation; it is not the
problem, and it is what will stop a gap scan from accepting junk.


-----

### ✅ FIXED 2026-08-26 (build 3362) `[SANEPROPS-2026-08-26]` — `kMaxSanePropertiesSize = 1 MB` rejected legitimate classes

P3R has **two real classes over the limit**, and the same log shows both parsing perfectly:

```
[WALK] WalkClass: XRD777SaveGame (super=SaveGame, size=3671800) at 0x162C6940
[WALK] WalkClass: XRD777SaveGame — 2 fields
[WALK:safe] WalkClass: refusing to cache 0x162c6940 — PropertiesSize=3671800 (read ok);
            not a UStruct, or recycled memory
```

`AstreaSaveGame` (3,671,816) is the other. Both are byte-stable across twelve minutes of the
session, so "recycled memory" is simply wrong about them — a ~3.5 MB SaveGame class is ordinary.

⚠ **The cache refusal is the harmless half.** `Ubel.h:530 IsSanePropertiesSize` has a *second*
caller: `Ubel.cpp:3801`, in `WalkInstance`, which on a fail sets `result.isStale = true`,
`propsSize = 0` and **skips the field/gap walk entirely** — so walking an instance of one of these
classes would return nothing and the UI would report a live object as stale. That path was **not
entered in this session** (grep `implausible PropertiesSize` = 0), so this is latent, not observed.

**FIXED by splitting the one constant into two**, because it was answering two questions with
opposite risk profiles: `kMaxGapFillBytes` (1 MB, **unchanged** — the byte-sweep work cap that the
827 MB wedge exists to stop) and `kMaxPlausiblePropertiesSize` (**64 MB** — admission control for
caches that are never erased). `WalkInstance`'s stale gate and `ShouldPublishClassWalk` now ask the
plausibility question; `GuessGapTypes` and the gap-fill entry keep the work cap.

⭐ **The consequence was much worse than this row first recorded, and an adversarial design review
found it.** `Ubel.cpp`'s `WalkClassEx` gate does not merely refuse the CACHE — it refuses to
**RETURN**, handing back an empty `ClassInfo` (the signature is a reference into the map). Aura's
container and ref caches read `WalkClassEx(cls).Address != cls` as the refusal signal, so on P3R
both `USaveGame` classes were structurally invisible to **Value Search, Group Scan, snapshot
capture, CE export, Solitar and Solide** — with **no log line at all**. That refusal now logs.

⚠ **The 64 MB figure is a judgement call the maintainer may want to change.** It is an admission
bound, not a fact about UE (`UStruct::PropertiesSize` is an int32 with no engine cap). It sits ~17×
above the largest real sample (3,671,816, P3R) and ~13× below the observed garbage (867,763,776,
Elliot) — their geometric mean is ~56 MB. The samples are in the header comment so it can be
re-derived rather than re-guessed.

Also in the fix: a live-but-huge class now reports `gap_fill_skipped` on the wire and an ℹ status
line instead of the ⚠ "freed/recycled" one; the two WARN texts stopped asserting things they had
not measured; and `ProbeRowMapOffset`'s `O(PropertiesSize/8 × fields)` double loop gained the work
cap as a clamp, because the raised ceiling newly exposes it.

**Verification**: 9 new C++ assertions across `dll_helpers_test` (pure bound) and `dll_core_test`
(the instance path, driving `WalkInstance` against a fake class blob), with two negative controls —
reverting the ceiling to 1 MB turns **9 assertions red including "P3R's real 3.6 MB class is NOT
stale (THE bug)"**, and removing the skip flag turns the gap-pass assertion red.

⚠ **Writing that test reproduced A10 by accident**: one class blob reused across four cases had
every case after the first read the first one's memoised answer, because `s_walkClassExCache` is
keyed by address and nothing erases it. One blob per case; the comment says why.


-----

### ✅ FIXED 2026-08-26 (build 3362) `[FUNCDENOM-2026-08-26]` — "9760 entries from 2293 classes" counted classes that contributed nothing

`ListAllFunctions: total=9760 classes=2293 interesting=1848`. The `2293` was the number of classes
**scanned**, not the number that yielded a function; only ~889 actually contributed one. Read as
provenance — which it looks like — it overstated coverage by 2.6×. Same family as the `PERF`
denominator fixed in 3361: a number that invites an arithmetic reading it does not support.

⭐ **The wire field was NOT the problem and was deliberately left alone.** `scanned_classes` is
correctly named and correctly documented, and one of the three UI sentences (`"N classes scanned"`)
was already reading it correctly. What was wrong was every sentence phrased as PROVENANCE. So the
contributing count is **derived** — `AllFunctionsResult.ClassesWithFunctions`, a computed property
over the rows the panel is displaying — with **no new wire field**: a parsed field would be `0` in
every VM fixture (they all override `ListAllFunctionsAsync` and bypass the parser), and a `??`
fallback fires on *absent*, not on *zero*, so a DLL that shipped the key and missed the assignment
would render "9,760 functions from 0 of 2,293 classes". One number, one source, and that source is
the array being described.

The DLL keeps its own `CountContributingClasses` for its log line only, appended to the format
string (⚠ **append-only**: nothing in `dll/src` carries `_Printf_format_string_`, so MSVC diagnoses
no format/argument mismatch and a reordered trailing `%s%s` against an `int` is an access violation
on the pipe worker inside a shipping game).

**Verification**: 4 C++ assertions (including order-independence — entries are pushed grouped by
class today, so an adjacent-transition implementation would pass everything else), and 2 C# tests
including ⭐ **the over-swap control**: the "N classes scanned" sentence must KEEP quoting the
examined count while the two provenance sentences use the contributing one. Negative control:
reverting both provenance sentences turns 3 tests red.

⚠ **Siblings deliberately NOT folded in** — they are the same defect on different commands and need
their own change: `search_properties` (`Aura.cpp` counts the class then discards it at four separate
points), and the batch path that copies one batch-wide count into every query's result.


-----

### ✅ FIXED 2026-08-26 (build 3362) `[STALLDEFAULT-2026-08-26]` — `game_thread_stalled: false` before the hook existed was a DEFAULT, not a measurement

`game_thread_stalled` could not report a stall until the ProcessEvent hook was installed, so almost
every `false` in a session's logs was the field's initial value rather than an observation — the
exact shape of `[SEETHRUNOOP]` / `[SEETHRUTALLY]`, a success report computed by a path that never ran.

**Fixed by WITHHOLDING the key when it is not a measurement**, which turned out to be the cheapest
correct answer: absence is already a live wire state (`MakeError` and `MakeEvent` never carried it,
and the client already tolerated it), so it adds no new state and costs fewer bytes. `Stark` gained
a pure three-state classifier; `IsGameThreadResponsive` is now `IsResponsiveFromLiveness(...)` with
its **contract unchanged**, because eight in-DLL gates ask "should I attempt an invoke?" and
unknown must keep meaning yes.

⚠⚠ **The naive version of this fix is a REGRESSION, and the review caught it.** The hook can go
back DOWN mid-session (`Frieren.cpp`'s validation-failure path calls `Stark::RemoveHook()`), which
returns liveness to unknown. Today a banner raised by a real stall is cleared *by the lie* — the
next `false` clears it. Withhold the key without teaching the client that absence **withdraws** the
claim, and that banner sticks ON for the rest of the session. So the DLL half and the client half
had to land in one commit, and they did.

Rejected alternatives, each for a measured reason: a **tri-state string** would not degrade an old
client, it would **disconnect** it (`GetValue<bool>()` throws `InvalidOperationException`, which
`PipeClient`'s `catch (JsonException)` does not catch — the read loop exits and every in-flight
request fails). A **second additive bool** leaves the wrong value on the wire for anyone reading
only the first field.

Also fixed in the same commit: `get_diagnostics` had a **second report path** with the identical
defect (`gt["responsive"]`, rendered to the user as "Responsive" in the System tab). It gains
`liveness` beside it; `responsive` is kept unchanged so an older UI does not start rendering
"Stalled" on a healthy game.

Three verification rigs relied on the lying default and were **armed, not weakened**:
`f5_envelope.py` now installs the hook before probing and keeps its three-key assertion (dropping
the key only for a run where arming demonstrably failed, and saying so); `l8_fglock_nopump.py`'s
`is not False` now catches "detector never armed" and "already stalled" in one test, where the
former used to be indistinguishable from success; and ⚠ `seethrough_arm_b.py` read the key with
`[...]` **between `suspend-tid` and `resume-tid` with no try/finally** — a `KeyError` there would
have left the game thread SUSPENDED and the process needing a kill.

**Verification**: 9 C++ classifier assertions covering every branch (including the
connected-while-paused one that a "simplification" would delete, and the saturating guards), a
regression control asserting the gate predicate is **bit-for-bit** what it was before the split,
the envelope builders re-pinned per liveness, and 8 C# `PipeEnvelope` tests. Two negative controls:
restoring the unconditional stamp turns the omission assertions red, and making an absent key mean
"no report" turns the withdrawal test red.


-----

### ⛔ REFUTED 2026-08-21 `[PROXYDEPS-2026-08-19]` — the six `#deps 0` objects are EMPTY, not broken

**There is no defect. A `.h` edit does rebuild the four shipped proxy DLLs**, and it always did.
Recorded here rather than deleted because the finding was reasonable, the refutation is a
*measurement*, and the check that produced it now knows better.

**What the six actually are.** Every `Lugner*.cpp` is wrapped head-to-toe in
`#ifdef UE5_PROXY_<FLAVOUR>_BUILD` — `Lugner.cpp:25`, `Lugner_Dinput8.cpp:30`, `Lugner_Dxgi.cpp:40`,
`Lugner_Winmm.cpp:24` — **with its `#include`s inside the guard**, and CMake compiles all four into
all four proxy targets. In the three targets that do not define a given flavour the file
preprocesses to *nothing*, `/showIncludes` prints nothing, and ninja records zero deps. Correctly.

The shape is the giveaway once you look at the whole table instead of the six: it is exactly
**4×4 minus the diagonal**, one live TU per target and three empty ones.

| target | `#deps 2` (the live TU) | `#deps 0` |
|---|---|---|
| `UE5Dumper_Proxy` | `Lugner.cpp` | `Lugner_Dinput8.cpp` |
| `UE5Dumper_ProxyDinput8` | `Lugner_Dinput8.cpp` | `Lugner.cpp` |
| `UE5Dumper_ProxyDxgi` | `Lugner_Dxgi.cpp` | `Lugner.cpp`, `Lugner_Dinput8.cpp` |
| `UE5Dumper_ProxyWinmm` | `Lugner_Winmm.cpp` | `Lugner.cpp`, `Lugner_Dinput8.cpp` |

**Two independent measurements, either of which settles it.**

1. **Object size.** The six are **527–535 bytes** — a bare COFF header. The smallest object with
   real code is **10,985**. A 20× gap with nothing in between.
2. **The empirical predicate the row itself asked for** — touch a header, see what rebuilds.
   `touch dll/src/Lugner.h` then `ninja -n` on the four proxy targets queues **exactly 4** compiles,
   one per flavour's live TU, and relinks **all four** DLLs. Adding `Sein.h` brings in each target's
   `Heiter.cpp.obj` as well: 8 proxy objects over 4 targets, none missing.

⭐ **The row's own instinct was right and is worth keeping**: it refused to "fix" this by
re-configuring, on the grounds that all 17 `UE5DumperCommon` objects record deps correctly so a
whole-tree code-page mismatch was already refuted. That reasoning was sound — the remaining step was
to ask what *else* produces a zero, not to look harder for a breakage.

**What changed as a result.** `tools/verify/build_dll.py` classified `#deps 0` as broken outright and
therefore printed a permanent WARNING on every build. That is the worst of the three possible
states: a real breakage would have arrived looking exactly like the noise everybody had learned to
scroll past. `deps_health` now discriminates on the object's **content** rather than its dep count
(`EMPTY_TU_MAX_BYTES`), so the six are silent and anything genuinely dep-less is a **hard failure**
instead of a warning. ⚠ A **missing** object counts as bad, not empty — never built is not the same
as nothing to build. Shown able to fail: setting the threshold to 0 reports all six with their sizes
and exits 1; at 2048 the check is clean at 72 objects. CLAUDE.md's build section now names the empty
TU as the third legitimate exception beside `.rc.res` and `.asm.obj`.


-----

### ✅ FIXED + LIVE-CHECKED 2026-08-21 `[GRIDRECYCLE-2026-08-21]` — a sorted DataGrid rendered one row's data twice

**Reported by the maintainer with screenshots** (`AF16–AF23` step 2). Open Interesting Functions →
**Props**, click a column header a few times: the grid ends up showing **the same row twice**. The
screenshots make the shape unmistakable — the header still reads *"2 properties (1 written)"* while
**both** rows render `read / DropItemLaunchParams_OnDeath / MapProperty`, and the descending sort
one click earlier was **correct**. So the collection was intact and only the *rendering* was stale.

**Cause.** All six cell templates in that dialog are built in code as

```csharp
CellTemplate = new FuncDataTemplate<FunctionPropRef>(
    (x, _) => new TextBlock { Text = x?.AccessSummary ?? "", Foreground = /* from x */ },
    supportsRecycling: true),
```

`supportsRecycling: true` tells Avalonia the produced control may be reused for a **different** data
item *without re-running the factory* — but the factory **bakes the values in at construction** and
binds nothing. A recycled cell therefore keeps the previous item's text. Sorting reshuffles which
item lands in which row container, and the stale container renders a duplicate. Descending happened
to survive because that reshuffle reused containers in an order that masked it.

**Fix.** `supportsRecycling: false` on all **17** such templates, across the four code-built dialogs:
`FunctionPropsDialog` (6), `PropertyXrefDialog` (6), `ObjectInstancePickerDialog` (4),
`InvokeParamDialog` (1). These are small, bounded grids, so re-running a factory per row costs
nothing measurable.

⭐ **The correct pairing already existed in the tree** — `ProcessPickerWindow.cs` bakes its values
too and correctly passes `supportsRecycling: false`. The four dialogs had drifted from it, which is
why the fix is "match the sibling", not "invent a rule".

**Pinned by `DataTemplateRecyclingTests`** (new): scans those five files, fails on any
`supportsRecycling: true`, and separately asserts every `FuncDataTemplate` states the argument
explicitly so a silent default cannot creep back. It carries two guard-the-guard assertions (all
five files found, ≥15 templates seen) so it cannot pass vacuously if the code moves.
⭐ **Shown able to fail**: reintroducing one `true` produced
`failed … NoFuncDataTemplateClaimsRecycling … Offenders: FunctionPropsDialog.cs:206`.

**LIVE-CHECKED 2026-08-21** on the rebuilt AOT binary (v1.0.0.3283), DumperTest.

⚠ **On a SIBLING dialog, not the reported one — and the distinction matters.** The maintainer's
repro was `FunctionPropsDialog` on a real game. DumperTest **cannot** reach it: the Props and Xref
dialogs are driven by Blueprint bytecode xrefs, and DumperTest has essentially none — the
`Interesting Props` grid's **`Funcs` column is empty for every row**, `Find Funcs` on `MaxWalkSpeed`
returns *"0 function(s) reference this field — scanned 9,807 funcs with bytecode"* (both with and
without `Game only`), and the one Props dialog that did open showed a single property. So the
reported dialog is not testable on this fixture at all.

What WAS driven is `ObjectInstancePickerDialog` — **one of the same four dialogs, fixed in the same
commit, with 4 of the 17 recycling templates and `CanUserSortColumns = true`**. Reached via
Live Walker → `PlayerController` → Functions → `ClientSetViewTarget` → **PIPE** → the Invoke param
dialog's **Pick…** for its `UObject*` parameter, giving **253 instances** over Index / Address /
Class / Name.

**Seven header clicks across all four columns. No duplicate row appeared.** Two checks, because
"no duplicates" alone is weak:
* every row kept a **distinct Index and Address** — the repeated `WorldPartitionHLODSource ×3` under
  a descending Class sort carry indices 20026 / 20025 / 20024 and three different addresses, so they
  are genuinely three objects, not one rendered thrice;
* ⭐ every **Class↔Name pair stayed internally consistent** (`ContentBundleTypeFactory` /
  `ContentBundleTypeFactory`, `InterchangeFactoryBase` / `Default__InterchangeFactoryBase`, …). This
  is the stronger signal: a recycled cell keeps the *previous* item's baked text, so a stale grid
  shows one item's Name beside another item's Class. That mismatch is what could not be produced.

The dialog was cancelled rather than confirmed, so no UFunction was actually invoked and no game
state changed.

⚠ **What this does and does not establish.** It shows the fix works on a real, sortable, code-built
grid with 253 rows. It does **not** re-run the maintainer's exact `FunctionPropsDialog` case, which
needs a game with real Blueprint bytecode — `AF16–AF23` step 2 stays owed for that specific dialog,
and is worth ticking off the next time a real game is up.


-----

### ⛔ L3 step 1's CONDITION IS NEVER MET — swept 2026-08-20 `[AD-PATTERNS-2026-08-20]`, no game needed

*Fully headless: 170 `scan-*.log` files across **25** processes already on this machine, plus one
fresh UE 4.27 title (**DQ7R**, injected today).*

**None of the four patterns has ever WON, on anything.** The step reads "**if** one of them wins…",
and that antecedent is false everywhere. Extracting every `[WINNER]` line ever written here gives
**27 distinct winning patterns** — `GOBJ_ES53_1`, `GOBJ_V13`, `GOBJ_AV1`, `GOBJ_EXP`, `GOBJ_G42_4`,
`GOBJ_GH_4`, `GWLD_TQ_1`, `GWLD_V3`, `GWLD_SP57_1`, `GWLD_SP57_4`, `GWLD_ES2_1`, `GWLD_EXP`, … — and
`GWLD_TQ_3`, `GWLD_TQ_4`, `GOBJ_PS1`, `GOBJ_PS6` are **not among them**.

**Why a UE 4.27 title does not help.** Scanning is first-hit-wins in priority order, so the
low-priority entries are only reached when the primaries miss. DQ7R *is* UE 4.27 and logged just
**six** patterns tried in total — `GOBJ_ES53_1`, `GNAM_V8`, `GWLD_TQ_1`, `SPARSE_ES2_1`, `GENG_X1`,
`GENG_EXP` — every target satisfied on its first candidate. The four never ran.

**What the sweep DID establish, and it is not nothing.** Where they were tried:
```
FactoryGameSteam   [GObjects] GOBJ_PS1   hits=1   (not validated)
FactoryGameSteam   [GObjects] GOBJ_PS6   hits=1   (not validated)
Avowed / Game / b25a_subfloor / notepad++ / python   all four:  hits=0
```
⭐ `GOBJ_PS1` and `GOBJ_PS6` are **live** — they byte-match on a real UE title (Satisfactory) — and
with the corrected geometry the candidate they resolve is **rejected by the validator** rather than
accepted as a wrong winner; `GOBJ_EXP` went on to win there. That is the layered lookup behaving
correctly, and it is the only in-situ exercise of the corrected geometry available on this machine.
`hits=0` elsewhere means the geometry change cannot be judged from those runs at all.

**To close this row** you need a title where the higher-priority GWorld/GObjects patterns MISS and
one of these four wins — the same shape of requirement as `G7`. Nothing installed here does that.
|---|--------|------|

-----

### ✅ W1 / W7 PASS 2026-08-22 `[W1W7-CUE4PARSE-2026-08-22]` — a third-party parser reads our .usmap

Run end to end, and **not circularly**: the reader is **CUE4Parse `1.2.2.202608`** (owner
`GMatrixGames`) taken straight from nuget.org, unmodified, in a throwaway console project **outside
the repo**. Per the maintainer's 2026-08-22 instruction it is deliberately **not** added to
`UE5DumpUI`'s dependencies, which are AOT/trimming constrained.

| step | result |
|---|---|
| **1** export the `.usmap` | ✅ `Export → USMAP (.usmap)` on DumperTest (UE504, 25,179 objects, DLL 3315) → `out\DumperTest.usmap`, **1,889,952 bytes**, magic `c4 30` (`0x30C4`). Header reports `USMAP exported`. |
| **2** load it in a real parser | ✅ `UsmapParser` accepted it: version **`ExplicitEnumValues`**, compression **`None`**, **7,885 types**, **1,614 enums**. |
| **3** `Actor`'s `bHidden` / `InitialLifeSpan` | ✅ class `Actor`, super `Object`, **80 properties**. `bHidden` → **`BoolProperty`**, `InitialLifeSpan` → **`FloatProperty`** — both the correct UE types, not just present. The neighbouring rows are real too: `PrimaryActorTick` → `StructProperty` with `StructType=ActorTickFunction`, then `bNetTemporary` / `bOnlyRelevantToOwner` / … as `BoolProperty`. **Not an empty or garbled table**, which the row says is a FAIL even without an exception. |
| **4** a Blueprint class (`*_C`) | ⚠ **The row's expectation is STALE — see below.** |

⭐ **The negative control is what makes "it parsed" mean anything.** A parser that never rejects
anything would accept a garbage file too, so both failure modes were armed and both fired:

* `truncated.usmap` (first half of the bytes) → `System.IO.Stream.ReadExactly` throws;
* `badmagic.usmap` (first magic byte XOR 0xFF) → **`CUE4Parse.UE4.Exceptions.ParserException: Usmap has invalid magic`**.

**⚠ Step 4's premise is out of date, and the result is the opposite — correctly so.** The row says
*"查不到是預期的（W8 未修，`*_C` 被過濾）"*. But **W8 shipped and was verified on 2026-08-20**
(`[W8-USMAP-2026-08-20]`), and its whole assertion was *"the struct count rises by roughly the
number of `BlueprintGeneratedClass` objects in the game"* — i.e. W8 is precisely the change that
**adds** Blueprint classes to the export. So finding them is right, and their absence would now be
the defect.

Observed: **5 types ending in `_C`** — `DmgTypeBP_Environmental_C`, `ABP_Manny_C`, `ABP_Quinn_C`,
`BP_ThirdPersonCharacter_C`, `ThirdPersonMap_C` — with real inheritance and real tables
(`ABP_Quinn_C`: 56 props, super `ABP_Manny_C`; `ABP_Manny_C`: 55 props, super `AnimInstance`).

⭐ **That is a stronger confirmation of W8 than W8's own check.** W8 passed by *counting* structs;
here an independent parser resolves the Blueprint classes' names, super-chains and property tables.

▶ **Fix the row's step 4 wording** rather than the code: `*_C` present is now the expected result.

**Reproducing it costs about two minutes** (the project is throwaway by design, so only the recipe
is kept):

```
dotnet new console -o usmapcheck && cd usmapcheck
dotnet add package CUE4Parse            # 1.2.2.202608, nuget.org
```
```csharp
var u = new UsmapParser(path, Path.GetFileNameWithoutExtension(path), StringComparer.OrdinalIgnoreCase);
u.Mappings.Types["Actor"].Properties.Values      // -> PropertyInfo { Name, MappingType, Index, ArraySize }
```
⚠ Two API notes that cost time. `UsmapParser`'s useful members are **fields, not properties**
(`Mappings`, `Version`, `CompressionMethod`), so a property-only reflection dump shows nothing. And
`PropertyInfo.MappingType` is a `PropertyType` with **no `ToString()` override** — printing it
yields the class name `CUE4Parse.MappingsProvider.PropertyType`, which looks like a parse failure
and is not; read its `Type` / `StructType` fields.

-----


-----

### ✅ D2（顯示配對） PASSES 2026-08-22 `[D2-PAIRING-2026-08-22]` — all four steps, on DumperTest

Run on **DumperTest Development** (injected, `dist` AOT v1.0.0.3315, DLL 3315, UE504, 25,179
objects). Group scan: slot 1 `NumericNoByte / Between 200..100000`, slot 2
`NumericNoByte / Exact 424242`. ⚠ `Between` is the point — `TickCount` **moves** (measured
156 → 156 → 193 over ~16 s), so an `Exact` match on it races the game; a range brackets it.

| step | result |
|---|---|
| **1** default pair, no filter | ✅ `F32=513.36 (+3), FrozenInt=424242` — the displayed pair is **non-zero**, not the `TickInterval=0 / InitialLifeSpan=0` trap the row warns about, and the `(+N)` match counts are present. (`FrozenInt` shows no `(+N)` because its slot kept exactly one leaf.) |
| **2** filter `tickcount frozenint`, then reversed | ✅ The displayed leaf **changed** `F32=513.36` → **`TickCount=332 (+4)`**, i.e. the filter-matched leaf won the slot — precisely `PickGroupWitnessAssignment`. `frozenint tickcount` gave a **byte-identical row**, so it is order-independent. |
| **3** expand → `All fields` → again to collapse | ✅ Lists all 5 kept leaves by name (`F32` 0x650, `F64` 0x658, `TickCount` 0x6A8, `F32_Ticking` 0x6B0, `F64_Ticking` 0x6B8) and the status line says `Slot 1: 5 matching field(s) … Press "All fields" again to collapse`. Second press collapses. ⚠ `FrozenInt`'s slot has **no** `All fields` button — correct, it kept one leaf. |
| **4** `Live` / `Addr` / `Pivot` / `Locate` on a leaf | ✅ all four, on `F32_Ticking`. |

**Step 4 detail, because "it navigated" is not the same as "it navigated correctly":**

* **Live** → Live Walker on `DumperTestActor_0` with `F32_Ticking` **selected**, not just the object opened.
* **Addr** → ⭐ verified by **reading the clipboard**, not by assuming: `0x24319E37FC0`, which is
  **exactly** the address the Live Walker independently shows for `F32_Ticking`. It is neither `0x0`
  nor the object base (`0x24319E37918`) — the two failures the row names.
* **Pivot** → Class Pivot with `DumperTestActor (2)` selected as the target.
* **Locate 🌍** → Live Walker with the full breadcrumb `ThirdPersonMap > PersistentLevel >
  DumperTestActor0` and an honest status line: *"Located via GWorld — 2 hop(s) … the world→level hop
  is a back-reference, not a static pointer"*.

⚠ **One caveat, stated because it limits step 3.** All 5 leaves belong to `DumperTestActor` itself,
so the row's "the object's own fields sort first" clause was satisfied **trivially** — there were no
cross-object leaves to sort behind. That half needs a scan whose slot keeps leaves from an owned
component; it is not evidence from this run.

-----


-----

### ✅ AE10 — steps 1, 2, 4 CLOSED 2026-08-23 `[AE10-LOCATE-2026-08-23]`; step 3's premise is UNSATISFIABLE and the row's own fixture requirement was wrong

Run on **TQ2** (`TQ2-Win64-Shipping`, UE507, **279,587 objects**, dxgi proxy build 3315 — i.e. **proxy
mode**, the historic failure condition) with the AOT `dist` UI v1.0.0.3315.

⚠⚠ **The row's stated fixture — "a game where the AOB scan does NOT resolve &GWorld" — is not what
this defect was ever about, and no such host exists here.** todo.md's own long-form note records the
real symptom: on TQ2 *"the flag read false even though GWorld **was** resolved, so the button was
silently disabled"*. Confirmed again today: TQ2 logs `GWorld=0x7FF6A61BF788 (aob)`. Across all 24 host
log folders **every real game resolves &GWorld by AOB**; the only `GWorld=0x0` lines are cancelled or
pre-scan windows. ▶ **Fix the row's 需要 line** — it sends the next reader hunting a host that does not
exist and is not required.

**Step 1 — ✅ PASSED, and proven STRUCTURALLY rather than by clicking.** There is no mechanism left
that can grey these buttons:

| check | result |
|---|---|
| `IsGWorldAvailable` declaration or binding | **none** — the property is gone; only explanatory comments remain |
| `IsEnabled` on any 🌍 button (ClassPivot / DetectStats / InstanceFinder / InterestingFuncs / InterestingProps / Snapshot / SPC / ValueSearch) | **0** |
| `CanExecute` guard on any Locate command | **0** |
| *negative control* — can that grep fire at all? | **yes**, 19 `CanExecute` uses elsewhere (e.g. `DumpExplorerViewModel.cs:142`) |

⭐ That is stronger than observing seven panels: a proof that no gate exists covers every panel at
once, including ones added later. It was then **live-confirmed** on the historic problem host — the
`🌍 Locate` button in Instance Finder was enabled on TQ2 in proxy mode.

**Step 2 — ✅ PASSED, twice, with two independent detectors agreeing.**

| target | headless `find_path_from_gworld` | the UI's 🌍 |
|---|---|---|
| `Actor` @`0x17EB479F790` / `…FA60` (the 2 live actors) | `status=ok_via_level  found=True  visited=53855` | breadcrumb `lmain_menu > PersistentLevel > Actor0` + *"Located via GWorld — 2 hop(s) to Actor_0 (Actor). (via the world's level list — streaming/WP actor; the world→level hop is a back-reference, not a static pointer)"* |
| `Default__Actor` @`0x7FF47259D598` | — | `lmain_menu > gmmain_menu_C_2147481657 > HUD > Actor > Default__Actor`, *"Located via GWorld — 4 hop(s)"* |

⭐ **The pipe run was done BEFORE the UI was launched**, so the UI is not the only witness: the DLL
independently reported `ok_via_level`, and the panel's message says *"via the world's level list"* —
the same branch, in words. The row's FAIL condition (*"沒有任何訊息、靜默無反應"*) is decisively not met:
every click produced a breadcrumb **and** a sentence explaining how the path was reached.

⚠ The failure branch (`no_path` / `invalid`) was **not** exercised. Two attempts to manufacture it
failed for benign reasons: a `Package` row exposes no detail strip (nothing to walk, so no 🌍), and
CDOs turn out to be *reachable* through the class chain. Not a gap in the fix — a gap in this run.

**Step 3 — ⛔ THE PREMISE IS UNSATISFIABLE, and this is the row's real correction.** It asks for the
main menu *"確定沒有活的 UWorld"*. TQ2 sitting at its main menu has **5 live non-CDO UWorlds**:

```
0x17FBAB43A60  l_main_menu_scene_01     0x17B7E687480  World
0x17E484BAEA0  l_main_menu_scene_02     0x17B050B68E0  l_main_menu
0x17B500C0040  l_main_menu_scene_03
```

**In UE a main menu is itself a level.** The same was independently found for Avowed (its last session
was at `MainMenuPlayerController` with 92,036 objects and 6 non-CDO UWorlds). So "go to the main menu"
can never produce the no-UWorld state on a normal title. ▶ Either delete step 3, or restate it against
the condition the DLL actually has: `Fern.cpp` emits `status="no_gworld"` only when **neither** &GWorld
derefs **nor** any non-CDO UWorld exists in GObjects — which needs a process that is not a running UE
game, not a main menu.

**Step 4 — ✅ PASSED, with an honest caveat.** The regression control is "a game whose GWorld resolves
normally", and TQ2 **is** that game (`(aob)`), so the handoffs above are themselves the control: they
behaved exactly as designed on a normal-GWorld host. ⚠ The row imagined two different games — one
broken, one normal — but since the broken kind does not exist, TQ2 serves both roles: it is the
historic failure site (proxy mode) *and* a normal-GWorld host. A second title would add nothing this
run has not already shown.

> #### ✅ STEP 2'S RESIDUE CLOSED 2026-08-24 `[AE10-FAILBRANCH-2026-08-24]` — on the REPLY SPACE, not by finding an unreachable object
>
> The residue was *"exercise the FAILURE branch of 🌍 Locate-in-GWorld"*, and two attempts to
> manufacture one had failed for reasons unrelated to the claim (a Package row exposes no detail
> strip; CDOs turn out to be reachable through the class chain). A third attempt would have been a
> fourth search for a subject nobody has ever found.
>
> ⭐ **"Click something unreachable" is a GAME PROCEDURE, not the assertion.** The 繁中 row states
> the real FAIL condition as **沒有任何訊息、靜默無反應** — and that is a pure function of
> `GWorldPathResult.Status` through `LiveWalkerViewModel.GWorldPathFailureStatus`, which
> `LocateGWorldBannerTests`' `PathStub` already injects verbatim.
>
> ⚠⚠ **The row names two statuses that CANNOT OCCUR, which is why hand-hunting could never have
> worked.** `no_path` appears **nowhere** in the DLL or the UI — the real one is `not_reachable`.
> And `invalid` cannot reach this switch through this command at all: Fern answers the precondition
> failures as `no_gworld` / `no_engine` / `invalid_target` first. Both were being chased by name.
>
> **Added, ~60 lines, no DLL or pipe change:**
>
> | test | covers |
> |---|---|
> | `EveryKnownFailureStatus_RaisesTheBanner_WithItsOwnExplanation` | the four arms with no test — `deadline`, `visited_cap`, `no_gworld`, `invalid_target`. (`not_reachable` ×2, `no_engine` and the `cancelled` **non-banner control** were already covered.) |
> | ⭐ `AnUnknownFailureStatus_StillRaisesANonEmptyBanner_NeverASilentNoOp` | **the whole remaining reply space at once** |
>
> ⭐⭐ **The second one is what actually closes the row, because it does not enumerate.** Enumerating
> statuses can only keep pace with the switch — and a status added tomorrow, or a typo in one, is
> exactly the case that falls through, which is what the row fears. So it asserts the STRUCTURAL
> property instead: the default arm is `$"No {rootLabel} path found ({path.Status})."`, which cannot
> be empty for **any** input. Therefore every `Found=false` reply that is not `cancelled` raises a
> non-empty banner — including statuses that do not exist yet. Inputs chosen to be un-guessable
> rather than plausible: `no_path` (the row's own phantom), `invalid` (real in `Aura.cpp`,
> unreachable here), `reconstruct_error` (future-shaped), and `""` (a dropped field on the wire).
>
> ⚠ **Two negative controls, each isolating exactly what it should:**
>
> | control | armed by | result |
> |---|---|---|
> | the structural claim | default arm → `""` | **exactly the 4** unknown-status rows fail; the 4 known arms and all 7 pre-existing tests stay green |
> | each known arm | `deadline`'s text no longer says *"timed out"* | **exactly 1** row fails — so the arms are individually discriminating, not passing on a shared substring |
>
> Both reverted; `LiveWalkerViewModel.cs` byte-identical to HEAD. 15 tests in the file, 0 failed.
>
> ℹ️ **The one sliver left, stated plainly:** that the amber banner's PIXELS render. A VM test pins
> `HasLocateFailure` / `LocateFailureMessage`, not that the XAML binds them — though
> `LiveWalkerPanel.axaml:843` gates the Border solely on `HasLocateFailure` and its container Panel
> carries no visibility gate, so nothing else can suppress it. If a pixel witness is ever wanted it
> is **one screenshot** with the pipe forced to answer `invalid_target` via a bogus target address:
> no gameplay, no unreachable object, no CE, no fixture.

-----

# Second pass — the `## 🔎 Audit #3 fixes (build 2168) — **CLOSED, archived**` block

That `##` heading claimed to be archived since 2026-08; 694 lines of unrelated 2026-08-23 work had
accreted under it afterwards. Its closure writeups are below, verbatim. **Four subsections stayed
in `todo.md`** because their bodies still say work is owed, and **five migrated to
`tools/ue-sample/README.md`** because they are the fixture's design and were the only copy.

### ✅ MG2 step 1 + the TSet half of step 2 CLOSED 2026-08-23 `[MG2-CONTAINER-2026-08-23]`

Both remaining MG2 steps were parked on *"find a real game that happens to contain a `TSet<FName>`,
a `TSet<UObject*>` and a small enough `TMap`"*. The fixture supplies all three.
`tools/verify/mg2_container_count.py`, DumperTest Shipping, UE 5.4, build 3322.

| # | check | measured |
|---|---|---|
| **1a** | `TMap<int32,int32>` under the cap, remove one | `6/6` → `5/5`; remaining keys `[4001..4005]` = the old set **minus the lowest** |
| **1b** | `TSet<FName>` under the cap, remove one | `4/4` → `3/3`; exactly `Alpha` gone, `[Beta, Gamma, Delta]` left |
| **2** | **independence control** | `array_limit=64` → header **305**, rows **64** (disagree); `array_limit=1024` → **305/305** (agree) |
| **3** | `TSet<UObject*>` elements re-walked | all 4 addresses are real `DumperTestPayload` objects, `PayloadValue` = 909090 / 8100 / 8101 / 8102 |

⭐⭐ **Check 2 is the one that makes the row worth closing, and it is the part a commercial game
cannot give you.** The vacuity risk here is obvious once stated: if the header count were computed
from the rendered list, the two could only ever agree and "they agree" would be worthless. Settled
first in code —

```
Ubel.cpp:4410   fv.mapCount = sa.MaxIndex - sa.NumFreeIndices;   <- the TSparseArray HEADER
Ubel.cpp:3682   WalkInstance(..., int32_t arrayLimit, ...)        <- the LIST is a capped walk
```

— and then **demonstrated at runtime**: `V1a_GrowContainers(300)` pushed the map to 305 and the two
numbers separated to `305` vs `64`, then rejoined at `305/305` once the cap was raised. A check that
can be *made to fail on demand* is evidence; one only ever observed agreeing is not. You cannot ask
a shipped game to add 300 map entries.

⭐ **Step 1 is a DIFF, not a reading.** A single agreeing pair could agree by construction, so the
removal is the test: both numbers drop by exactly one *and* the surviving keys are the old set minus
the lowest — which is what `MG2_RemoveOneMapEntry` is specified to remove. A walker rendering a
stale inline copy would keep the low key and fail here.

⭐ **Step 2 counts nothing.** A broken decode still produces N rows, so the object set's elements
were **re-walked at the addresses the set reported** and each had to come back as a live object of
the class the set claimed, with a readable `PayloadValue`. All four did, and the values are the
seeded ones (the actor's own `Payload` at 909090 plus `SetPayload_0..2` at 8100-8102) — so the set
holds the right objects, not merely four valid ones.

⛔ **Step 2's "open any UDataTable" half stays OPEN, blocked by
[`[DTROWMAP-2026-08-23]`](#) — not skipped.** The drill-down can serve a *neighbouring* table's rows,
so a render check would be judging the wrong table's data. It becomes runnable the moment that is
fixed, and the fixture already holds both tables it needs.

ℹ️ The rig mutates the fixture and says so at the top: it shrinks `Set_Name` (four seeds — re-running
on one long-lived process eventually empties it and `[1b]` then correctly fails; relaunch rather
than "fix" that), and phase 2 deliberately leaves the containers ~300 larger for V1a.


-----

### ⚠ Found while doing it: `-ExecCmds=t.MaxFPS 15` is a NO-OP in Shipping

`AD4_GetContestWrites` was added to prove the contest was live. It also, unasked, measured the
sample's tick rate — **59.8 / 59.9 Hz in two runs**, against a launcher that documents a 15 FPS cap
as a house guarantee ("so an all-night batch does not load the machine — this PC also drives the
game under test").

The cause is the same Shipping gate that caught the CheatManager question earlier the same day.
UE 5.4 `Runtime/Core/Public/Misc/Exec.h:11-17`:

```cpp
#if UE_BUILD_SHIPPING && !WITH_EDITOR
    #define UE_ALLOW_EXEC_COMMANDS UE_ALLOW_EXEC_COMMANDS_IN_SHIPPING
#else
    #define UE_ALLOW_EXEC_COMMANDS 1
#endif
```

and UnrealBuildTool sets `UE_ALLOW_EXEC_COMMANDS_IN_SHIPPING=0` unless the Target opts in
(`UEBuildTarget.cs:5147/5151`). `GameEngine.cpp` wraps its exec handling in that macro
(`:1025-1102`, `:1398-1553`). So **`-ExecCmds` is silently discarded in a Shipping package**.

⛔ **CORRECTED the same day: "…and every past all-night Shipping batch ran uncapped" was WRONG,
and the 59.8 was the tell.** A genuinely uncapped UE sample on this GPU renders an empty level at
several hundred FPS, not at 59.8. **DumperTest caps itself**: `DumperTestSubsystem.cpp`
`ApplyMaxFPS` defaults `t.MaxFPS` to **60** and applies it **from C++** with `ECVF_SetByCode` —
which the Shipping restriction does not touch, because only `ProcessUserConsoleInput` refuses cheat
cvars. Past Shipping batches ran at the sample's own 60.

⭐ **And the sample already exposed the fix**: `-DumperTestMaxFPS=N`, same C++ path, works in every
flavour. `launch_dumpertest.py` now passes it. **Measured on Shipping after the change: 14.9 Hz.**
So the cap the launcher documents as a house guarantee is real again, on all three flavours.

⚠ The reusable lesson is the second miss, not the first: **when a measurement contradicts a
configured value, the next question is "what else sets this?", not "so it is unset".** Reading 59.8
as "uncapped" skipped straight past a suspiciously round number.

⭐ The irony is instructive and is the reusable part: the launcher's docstring contains a *careful*
decision about **which** capping mechanism to use — `t.MaxFPS` via `-ExecCmds` rather than
`-BENCHMARK -FPS=15`, because the latter switches UE to a fixed timestep and "silently changes what
every timing- or tick-sensitive row is measuring". The reasoning was right and the mechanism was
never checked for whether it *runs at all* in the flavour being launched. **Third Shipping-gate miss
in one day** (CheatManager, the log-verbosity comment, this).

⚠ **Consequence for other rows**: any Shipping-flavour measurement that assumed 15 FPS was taken at
~60. AD4's own numbers are unaffected because the rig now **measures** the rate and prints it
alongside a duty-cycle prediction the observation can disagree with — predicted 94.4% from 59.9 Hz
vs a 300 ms re-assert, observed 98.1%.

`launch_dumpertest.py` now says which flavours the cap applies to and warns at launch on Shipping.
Not "fixed" beyond that: a real Shipping cap needs either a Target.cs rebuild with
`bAllowExecCommandsInShipping` or a different mechanism, and that is a decision, not a typo.


-----

### ✅ MG2 step 2's DataTable half CLOSED 2026-08-23 — unblocked by the two fixes above

The half that was blocked an hour earlier. "Open any UDataTable — rows still parse correctly" is now
answered by the V8 run: 100 rows, each with correct `Index` / `Label` / `Value`, and an `FText`
`Caption` decoding to its seeded CJK. **MG2 is now closed in full.**


-----

### ✅ SUPERSEDED by the CLOSE below — the earlier blocked attempt `[AA12-STEP3-ATTEMPT-2026-08-23]`

> The blocker turned out to be an **input** limitation, not a broken feature: Avalonia menu items
> are not clickable by computer use. Kept for the ground it covered and for the traps it names.

The fixture half is done and the row is closer than it has ever been. It stopped on a Cheat Engine
plumbing step, and the ground already covered is written down so the next attempt starts here.


-----

### ✅ What is now established

| | |
|---|---|
| the fixture the row needed | `UDumperTestLateSpawn` — **0 live instances** (pipe-verified: `find_instances exact_match=true` → CDO only) and **no subclasses**. This is what the `NiagaraComponent` attempt lacked; that class turned out to have two live instances |
| Property Search finds it | one hit, `IntProperty @0x28`, Preview **`0 (CDO default)`** — ⭐ the *same marker* that misled the earlier attempt, except here it is genuinely true |
| the Freeze dialog | Scope line reads **"every live DumperTestLateSpawn and every subclass"**, value pre-filled 9999 |
| the push | **works** — two `Freeze: DumperTestLateSpawn::LateValue = 9999  <script>` records appeared in CE's table, unticked, via the AOBMaker plugin bridge |
| ⭐ the bail-out rule | ticking with the helper absent produced `showMessage`: **`[Freeze] ue5_freeze_helper.lua not found in this table.`** with the setup hint — and the record **did not stay ticked**. That is CLAUDE.md's "a bail-out that applied NOTHING must untick the record", observed working |


-----

### ⛔ Where it stopped

`Tools → Inject Freeze Helper into Current CE Table` **does not put the helper in the table.** Clicked
twice, position confirmed at full resolution; re-ticking still gives `not found in this table`.
`InjectFreezeHelperLuaAsync` (`MainWindowViewModel.cs:3362`) sets `StatusText` on **every** branch —
progress, success, three distinct failures — and `StatusText` **is** rendered
(`MainWindow.axaml:40-46`, top-left, `MaxWidth=360`, trimmed, with a tooltip). It stayed on
"AOBMaker plugin connected" throughout. So either the command never fired, or a periodic AOBMaker
re-check overwrites `StatusText` faster than the result can be read. **Not diagnosed — do not record
it as a defect until it is.**

**Next attempt should use the documented fallback first** (the handler names it itself): `Tools →
Export Freeze Helper Lua File…` to disk, then add it to the table through CE's own *Table Extras →
table files*. That sidesteps the plumbing entirely and gets the row to its actual question.


-----

### ⚠ Two operational traps that cost most of the time here

* ⭐⭐ **`open_application` on Cheat Engine LAUNCHES A NEW INSTANCE — it does not front the running
  one.** Three calls left **four** CE processes. The AOBMaker bridge stays bound to the *first*
  instance, so the freeze records went there while I was staring at a different instance's empty
  table and had begun writing it up as *"the UI reports success but CE has no record"*. **That would
  have been a fabricated defect.** Front CE with `py tools/verify/front_window.py front cheatengine`
  (the process name has no space, so `front "Cheat Engine"` finds nothing) and call
  `open_application` **once**, to start it.
* **Two different status lines.** The Properties tab's own status sits top-**right** of the panel;
  `MainWindowViewModel.StatusText` sits top-**left**. I read the panel's for several minutes while
  waiting for a MainWindow message. Both are `TextTrimming`-ellipsised.

ℹ️ Also seen, minor: the top toolbar's AOBMaker chip read **Connected** while Property Search's own
Freeze button stayed **disabled**, because the chip mirrors LiveWalker/Pointers availability
(`MainWindowViewModel.cs:698-706`) but Property Search probes lazily on **tab activation**. A tab
round-trip enabled it. Not filed as a defect — the panel does refresh, just not from the chip.


-----

### ✅ AA2 step 1 + AA3 step 5 CLOSED 2026-08-23 `[AA2-CONTRACT-AA3-STOP-2026-08-23]` — the row is now complete

The last two steps. With step 2 (2026-08-21), step 3 (2026-08-20) and step 4 (2026-08-23),
**AA2/AA3 is closed end to end.** CE 7.7 + AOBMaker + DumperTest Shipping.

⭐ **One CE table, one script, two DLLs.** The record was pushed once and CE was never restarted —
only the game was, with a different DLL each time. So the two results differ in exactly one variable.

-----


-----

### Step 1 — the contract refusal

| | |
|---|---|
| **positive** — contract-2 DLL | `[Freeze] nothing was frozen:`<br>`[ue5_freeze] the DLL is older than this script (script 3, DLL speaks 2) -- update UE5Dumper.dll`<br>record **left unticked** |
| **negative control** — contract-3 DLL, *same record* | arms and **ticks**, no dialog, and the freeze then holds |
| the other bail reasons, excluded | **60 holders were spawned first**, so "armed: no live instances" cannot explain it; the helper was in the table, so "helper not found" cannot; the DLL answered the pipe, so "no DLL" cannot |

⭐⭐ **The old DLL's contract was ESTABLISHED, not assumed — and the naive read was wrong.** The row's
staging needs a DLL older than the script, and `out/proxy-backups/` holds ten. They are dated
**2026-08-19**, and so is the commit that moved the contract to 3 (`2c2a950c`), so the date decides
nothing. Parsing each PE's export table for `g_mailboxContract` settles it:

```
dist/UE5Dumper.dll                       current=3  minimum=1
*.20260819-*.bak   (all ten)             current=2  minimum=1
DQ7R.version.dll.20260823-154401.bak     current=3  minimum=1
```

⚠ **Reading that export as a bare `int32` returns `1127564629` for every file** — that is
`0x43354555`, `MAILBOX_CONTRACT_MAGIC`. `g_mailboxContract` is a **12-byte struct**
`{u32 magic; i32 current; i32 minimum}` (`Mimic.h`), so the contract is at **+4**. A bare read makes
every DLL look identical and would have "proved" the backups were unusable. The magic doubles as the
check that the right field was read.

⭐ **No staged build was needed.** The alternative was to lower `MAILBOX_CONTRACT` in `Mimic.h`
temporarily — which would have turned `tools/check_mailbox_contract.py` (a CI gate) red and produced
a *synthetic* DLL. A real shipped contract-2 binary is better evidence and leaves nothing to restore.

⚠ **One confound removed before the run.** The backup is named `DQ7R.version.dll.….bak`, and the
helper resolves `UE5Dumper.g_invokeMailbox` by module name; a name-resolution failure bails with a
**different** message and would have been mis-read as the contract check firing. Copied to
`out/oldcontract/UE5Dumper.dll` first, so the module name is right and the only thing wrong is the
contract. **Recipe for next time: copy any `*.20260819-*.bak` to `UE5Dumper.dll` and
`inject.py --dll` it.**

-----


-----

### Step 5 (AA3) — permanent rescan failure must stop the writes

The real behaviour is richer than the row's paraphrase, and was derived from the helper rather than
quoted: `MAX_FAIL_STREAK = 3` (`ue5_freeze_helper.lua:880`) × a **5 s** rescan interval (`:85`,
`:1094`) — so the row's "~15 s" is a derived number. On abandonment the helper disables both timers,
clears the cache, then **defers** an untick plus a **modal** (a plain print would be destroyed by the
generator's auto-close — `[FREEZESTUCK-2026-08-18]`).

| # | observed |
|---|---|
| 1 | **positive detector first**: poke `-1.0f` into a frozen holder → **restored to 9999 in 1 s** |
| 2 | suspend the game process → the DLL never picks the command up, so rescans fail |
| 3 | within the 25 s window, a **modal**: `[ue5_freeze] DumperTestHolder: 3 consecutive rescans failed -- freeze STOPPED writing (last error: mailbox busy (concurrent invoke or rescan)). This record has been unticked; re-enable it after fixing the cause.` |
| 4 | the record **unticked itself** (empty checkbox) |
| 5 | **ONCE** — dismissing the modal produced no second one |
| 6 | **resume**, then the *same* poke → stayed `-1` for **10 s** |

⭐ **The symmetry is the evidence.** Identical probe, identical target: restored in 1 s before the
break, untouched for 10 s after — and step 6 runs on a **resumed, live** process, so "the value
stayed" is a statement about the freeze having stopped, not an artifact of a frozen game.

⚠ **Suspend rather than `FreeLibrary`, deliberately.** The row says "unload/re-inject the DLL", but
this DLL installs **MinHook trampolines on ProcessEvent** and **subclasses the game's WndProc**;
unloading it leaves dangling pointers and would very likely crash the process — and a dead game
trivially stops writes, proving nothing. `suspend.py`'s own docstring records that a whole-process
suspend "stops Fern and Mimic too, so the DLL never even picks the command up", which is precisely
the persistent rescan failure the step wants, and it is reversible. The substitution is a fidelity
*improvement*, not a shortcut.

⭐⭐ **And the substitution turned out to be forced, not merely preferable: the DLL PINS ITSELF.**
`Heiter.cpp` calls `GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_PIN | …_FROM_ADDRESS, &DllMain)`
on the inject path, precisely so *"a FreeLibrary of this DLL while the poller runs would unmap code
that is executing"*. So the row's literal *"unload the DLL"* is **not runnable at all** — it is not
a question of whether it would crash. (Deliberately **not** done on the CE branch, where no threads
are running.) The suspend is the only reversible way to produce the state the step describes.


-----

### ⚠ Two corrections to the step-5 record above, and one defect it exposed

Established from the helper's own code after the fact — the observations stand, one *interpretation*
did not.

1. ⚠ **The streak was NOT three 5 s timeouts, and the derivation above is wrong about the
   mechanism.** `waitDone`'s timeout path is `if not wok then return nil, 0, werr end` and does
   **not** clear `OFF_CMD` — deliberately, since the DLL may still write its reply later. So rescan
   #1 timed out with `cmd` left set, and rescans #2 and #3 short-circuited on the in-flight guard
   (`ue5_freeze_helper.lua:645-650`) in **microseconds**. `MAX_FAIL_STREAK × 5 s ≈ 15 s` lands near
   the truth only because the *rescan interval* is 5 s, not because three waits elapsed. The row's
   PASS is unaffected — the freeze stopped, unticked and printed once, all observed — but *"~15 s
   because 3 × 5 s"* should not be re-quoted.
2. ⭐ **That cascade is why the modal said `mailbox busy`, and it was a real defect.** `_lastError`
   was overwritten by every failure and the message reported the **last** one, so for the whole
   *"the DLL took the command and wedged"* family the modal was **guaranteed** to offer a transient
   concurrency cause for a permanent fault — in the one place a user reads it, and against
   CLAUDE.md's own *"never report a mailbox failure by guessing"*. **Fixed** as
   `[FREEZEFIRSTERR-2026-08-23]`: the abandon message now names the **first** error of the streak
   and appends a differing latest one. The live modal's text was reproduced exactly in
   `freeze_helper_test.lua` (AA31, shown failing before the fix) — so the earlier
   *"the last-error text records which failure mode was actually exercised"* was **backwards**: it
   recorded the consequence and discarded the cause, including its actionable hint.
3. ℹ️ **On step 1's staging**, two details worth keeping. `_freezeOutdated` is captured **once at
   helper load** (`:286`), so a resident chunk is not replaced by a re-`load()` of the same version
   — both arms must provably run the same chunk. They did: **one CE instance, one table, one script
   push, never restarted**; only the game changed. And the refusal must be matched on the
   **numbers** `(script 3, DLL speaks 2)`, not the phrase *"older than this script"* alone — `:477`
   (`no contract symbol`) shares the phrase. The recorded evidence quotes the numbers.


-----

### ⛔ CAPABILITY LIMIT FOUND — Avalonia top-level MENU items are not clickable by computer use

This is not a product defect (the maintainer's own clicks work) and it is not about this row, but it
**changes what "Auto + Computer Use" can verify** and belongs with the classification.

Measured on `Tools ▾` and `Export ▾`, four different items, both by coordinate click and by keyboard:

* clicking the menu **header** opens the popup, reliably;
* clicking an **item** dismisses the popup and **does nothing** — no command runs;
* six `Down` presses produce **no selection highlight** at all;
* the UI's own log shows **no trace** of the command, and `StatusText` (which every branch of
  `InjectFreezeHelperLuaAsync` sets, and which *is* rendered at `MainWindow.axaml:40-46`) never
  changes. So the command genuinely never fires — this is not a silent-failure product bug.

⚠ **It cost most of a session.** Two Tools actions (`Inject Freeze Helper`, `Export Freeze Helper`)
appeared to be broken features; they were unreachable input, not broken code. **Do not file an
Avalonia menu item as defective without first proving the command ran** — the UI log is the cheap
check.

⭐ **CE's own menus are Win32 and work fine** — `Table → Add file` opened, took a typed path, and
worked first try. So the workaround for any UI-menu-gated row is: get the artefact another way, then
use CE's side.

**The bypass used here, reusable:** the helper the Tools menu would have exported is a **file in this
repo** — `scripts/ue5_freeze_helper.lua`, embedded verbatim by
`UE5DumpUI.csproj:150-152` (`<EmbeddedResource Include="..\..\scripts\ue5_freeze_helper.lua">`). So
`Table → Add file` on that path gives byte-identical content with the menu removed from the loop.

ℹ️ Two smaller operational notes from the same run: the Windows IME must be switched to English
before typing a path into a Win32 dialog (click the taskbar language indicator; `Shift` does not
toggle it and `systemKeyCombos` is ungranted), and the taskbar indicator's x position moves, so
screenshot before clicking it — a miss landed on OneDrive and aborted the batch.


-----

### ✅ Solide L3 + L4 + the `⚠ capped` badge CLOSED 2026-08-23 `[SOLIDE-L3L4-2026-08-23]`

Three rows in one run, on the spawner fixture packaged today. `tools/verify/solide_l3_derivation.py`,
DumperTest Shipping, UE 5.4, build 3334.


-----

### ⭐ L3 — the hold follows DERIVATION, and this is the first time that was FALSIFIABLE here

`Aura::FindInstancesDerivedFrom` is specified to hold a class *and every subclass*. The plausible
wrong implementation — matching class **names** by substring — passes anything you can assemble from
a commercial game, because subclasses are conventionally named after their base.
`[A6-DERIVE-2026-08-22]` got as close as a shipped title allows and said so.

The fixture supplies an **inverted** pair, so the two rules cannot both be satisfied:

| class | name contains `DumperTestHolder` | derives from `ADumperTestHolder` |
|---|---|---|
| `ADumperTestHolder` | YES | YES |
| `ADumperTestDerivedHolder` | **NO** | **YES** |
| `ADumperTestHolderDecoy` | **YES** | **NO** |

⭐ **The disagreement was demonstrated before the test, not assumed.** `find_instances` matches by
substring, and asking it for `DumperTestHolder` returns
`['DumperTestHolder', 'DumperTestHolderDecoy']` — it catches the decoy and misses the derived. The
rig prints that first and FAILS if the inversion is not present, because without it the run proves
nothing.

⭐⭐ **The decisive evidence is one number.** With 100 base + 30 derived + 8 decoys and
`truncated=false`:

```
force_field DumperTestHolder.HolderValue = 777.0 -> held=130 resolved=True truncated=False
    held=130   derivation predicts 130   substring predicts 108
```

**130 = base + derived. 108 = base + decoys.** Same pool, two rules, two different numbers — so
`held` alone settles it, before any per-instance read. Then both reads agree: 12/12 sampled base and
**12/12 sampled DERIVED** carry the forced value, **0/8 decoys** do.

⚠ **The first run was NOT decidable and the rig now refuses it.** At 300 + 50 + 8 the walk hit the
256 cap (`held=256, truncated=true`), and under a cap *"the decoys were untouched"* is equally
explained by *"the walk never reached them"*. An absence is only evidence when the detector is known
to have looked. The rig now fails a truncated run for the negative half instead of scoring it.

> ### ℹ️ 2026-08-24 — the caveat above was UNREACHABLE on this machine, and a corroborating re-run
>
> Not a new closure; L3 and L4 were closed on 08-23 and re-running them was **my failure to triage**
> — the third time today (see also `[B19-BACKDATE]`, withdrawn). What the re-run did expose is a real
> defect in the rig of record: **`solide_l3_derivation.py` died with `UnicodeEncodeError` on a cp950
> console, on the very line that prints the truncation caveat.** The console here is cp950, every
> report line carries box-drawing and ⚠ glyphs, and the run aborted mid-report at
> *"truncated -- the cap fired, so 'decoys untouched' is NOT ..."*. A rig that crashes while
> delivering its own caveat is worse than one that never printed it. Fixed with
> `sys.stdout.reconfigure(encoding="utf-8", errors="replace")`.
> ⚠ Preserve the file's **BOM** when patching it — a `utf-8` read/write round trip turns it into a
> literal `﻿` in the source and the file stops parsing.
>
> The re-run itself, at counts chosen to stay under the cap (`--base 150 --derived 40 --decoys 30`),
> is the cleanest form of the evidence: **`held=190`, `truncated=false`**, against
> **derivation predicts 190 / substring predicts 180** — Holder 12/12 and DerivedHolder 12/12 carry
> the forced value, Decoy **0/12**, and `reset_field` returned 12/12 to their **own** prior value.
> With no cap in play, "the decoys were untouched" is decidable rather than merely observed.


-----

### L4 — each instance restored to its OWN base

`HolderValue` is seeded **1000 + global index**, distinct per instance by construction; the sample
showed 12 distinct values (1088, 1089, 1090, 1091 …). After `reset_field`, **12/12 were back at
their own prior value**, not at a single shared one. The defect this row exists for — one captured
base restored to every instance — is invisible with one instance and invisible to any count, and is
exactly what distinct seeding makes visible.


-----

### The `⚠ capped` badge

The 358-instance run produced `held=256, truncated=true` — the cap firing locally and
deterministically, which previously required finding a commercial title with a big enough class
pool. `Spawn_Holders`'s default of 300 exists for this.

-----

ℹ️ **Three tooling defects found on the way, all now fixed, all the same shape — a caller using a
name the callee never reads:**

* **`find_instances` takes `limit`, not `max_results`** (`Fern.cpp`, `request.value("limit", 500)`).
  Every rig passing `max_results` was silently running at the default 500 **and matching class names
  by SUBSTRING** (`exact_match` defaults false). Harmless while a pool is two objects; not harmless
  once the spawner puts 300 in it — it is what made the first run raise `TruncatedError`. Fixed in
  the shared `find_live_actor`, so every rig that imports it is fixed.
* **`force_field`'s `value` must be a JSON number.** `"777.0"` returns
  `type must be number, but is string` from `request.value("value", 0.0)`. The protocol doc only
  shows the **bool** form, so the numeric form's value type is written down nowhere.
* **My own package check looked in the wrong encoding.** Class names are stored **UTF-16**, function
  names ASCII, so an ASCII-only grep reported all five new classes missing from a package that
  contained them. Caught by controlling against `DumperTestPayload`, which has existed since the
  first build and "failed" the same check. **Third wrong-place detector in one day** — after the
  `.pak`-vs-IoStore map scan and the unscoped `limit` regex.


-----

### ✅ DumperTest spawner fixture — WRITTEN + PACKAGED 2026-08-23

Item ③ of the plan in
[auto-verification-classification-2026-08-23.md](auto-verification-classification-2026-08-23.md).
Written; **the maintainer packages on request.** Same arrangement as the first fixture extension.

**Why.** All eight existing mutators only **change a field on an object that lives for the whole
level**. Several rows need the other thing — objects being **created and destroyed while the dumper
watches** — and no commercial game does that on cue, which is why they sat unrunnable.


-----

### Rows this unlocks — CLAIMED 7, and here is what they actually turned out to be

| row | status 2026-08-23, after the package |
|---|---|
| Solide **L3** derivation vs substring | ✅ **CLOSED** `[SOLIDE-L3L4-2026-08-23]` |
| Solide **L4** per-instance restore bases | ✅ **CLOSED**, same run |
| Solide **`⚠ capped`** badge | ✅ **CLOSED**, same run (`held=256 truncated=true` at 358 instances) |
| **AA2/AA3 step 4** freeze across churn | ⬜ still needs **Cheat Engine**; the spawner supplies the churn but not the freeze |
| **AA12/AA13 step 3** the legitimately-empty case | ⬜ still needs **Cheat Engine**; `UDumperTestLateSpawn` is the fixture half and it is in place |
| **U4** class-to-class slot recycling | ⬜ a **deliberate deferral**, and the spawner only partly reaches it — see below |
| **AE4–AE7** the concurrency gate | ⛔ **MIS-CLAIMED. The spawner cannot help.** |

⛔ **The AE4–AE7 claim was wrong twice over, and it is worth writing down because it is the kind of
plan item that would have burned a whole session.** Its open step is **2, not 4** (step 4 closed
2026-08-20, `[ORPHANGATE-2026-08-22]`), and step 2's blocker is that **Deploy / Remove / Refresh /
Update All each finish inside one screenshot round-trip**, so the busy bar cannot be caught. The
proposal was to inflate GObjects with `Spawn_Holders(200000)` so a guarded op runs long enough — but
`ProxyDeployViewModel` makes **zero pipe calls** and `DeployAsync` is **file I/O on game folders**.
The size of the object pool is irrelevant to it. Verified by grep, not by reasoning.

⚠ **U4, honestly.** `s_walkClassExCache` is keyed by **UClass** address, and UClasses are created at
startup and rooted, so they are not what gets recycled. `Spawn_RecycleChurn` recycles **instance**
slots between two classes with sane `PropertiesSize`, which is the right shape — but the residual
concern the row defers is a **stale UI reference** silently rendering the new occupant as the old
one, and the walk itself re-reads the class pointer every time. A pipe-only run would show "the walk
reports the current class", which is true and does not address the deferral. Left deferred rather
than closed with a pass that means less than it looks.


-----

### Rows this unlocks (as originally claimed)

Solide **L3** (derivation vs substring) · Solide **L4** (per-instance restore bases) · Solide
**`⚠ capped`** badge · **AA2/AA3 step 4** (freeze across churn) · **AA12/AA13 step 3** (the
legitimately-empty case) · **U4** class-to-class slot recycling · **AE4–AE7 step 4** (the
concurrency gate, which currently reports 執行時間太短無法測試 because nothing runs long enough to
collide). **FREEZESCOPE step 6** becomes possible too if DQ7R is unavailable.


-----

### ✅ Packaged 2026-08-23 16:00–16:03, all three flavours

Verified present in each: the 9 `Spawn_*` UFunctions are reflected and all five new classes are
registered. ⚠ **Class names are stored UTF-16 and function names ASCII** — an ASCII-only grep
reports every class missing from a package that contains them. Control against `DumperTestPayload`
(present since the first build) before believing such a result.

First three rows closed the same hour — see `[SOLIDE-L3L4-2026-08-23]` above.

