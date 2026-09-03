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


