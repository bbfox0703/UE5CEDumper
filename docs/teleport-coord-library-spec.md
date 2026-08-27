# Teleport Coordinate Library — design spec

> **Status: P1-P5 SHIPPED** (builds 2257-2267, `dev`, 2777 tests green) —
> the **DLL-flavour picker is verified in-game**; the **standalone (no-DLL) flavour does NOT work
> on the tested title**. ⚠ This line said "not yet verified in-game" long after that stopped being
> true — [todo.md](todo.md) owns the status, this is a pointer to it. Designed 2026-07-22 across two adversarially
> verified multi-agent rounds; built 2026-07-23. Remaining verification work is in
> [todo.md](todo.md) under *"Teleport Coordinate Library"*; the shipping write-up
> is in [dev-log.md](dev-log.md).
>
> This document is the **design contract**, kept as written. Where the
> implementation had to correct it, the text says so inline (see §5.2 on
> precision) — nothing here is aspirational any more except the open questions
> in §10.
>
> Parent contract: [teleport-spec.md](teleport-spec.md) (Wirbel markers / POV /
> coord TP). This document covers the **UI-side unlimited coordinate list** and
> its CE-Lua + CSV export/import.

-----

## 1. The ask

Today the Teleport tab offers **3 DLL-side marker slots** (`Wirbel`, hotkey-driven,
survive a UI restart) plus a one-shot **"TP to coords"** numeric-entry card. That
is not enough for the real use case:

> A walkthrough author records **every chest coordinate** in a multi-map game.
> Coordinates from different maps can be numerically close. Teleporting *between*
> maps is risky (streaming / graphics cache / level load). The user needs to keep
> thousands of labelled positions, group them, filter them, pick one, confirm, and
> only then teleport.

| # | Requirement |
|---|---|
| R1 | Effectively unlimited coordinate sets (~4K is past the human limit) |
| R2 | Per-entry **Label** + **Group**; editable in the UI; sorted by group+label in Lua |
| R3 | Pick an entry, then **confirm**, then teleport (never one-click-fires) |
| R4 | UI: *Save current pos* / *Edit pos list*, a **filter**, in a **collapsible card, collapsed by default** |
| R5 | Export to Lua: a **needs-proxy-DLL** flavour and a **no-DLL** flavour |
| R6 | Export the editor's data as an **AA script via AOBMaker**; the Lua picks an entry and teleports; the Lua has its own filter |
| R7 | **Re-import** a previously generated AA script (copy-paste the whole thing) |
| R8 | **CSV export** (format documented with samples) and **CSV import** |

Explicitly **out of scope** (user decision): whether a saved coordinate is still
valid after the game is patched. The tool stores and replays coordinates; it makes
no claim about their continued validity.

-----

## 2. Verdict

**Feasible. The core needs ZERO DLL / pipe changes.**

* **Pipe** — `teleport_recall_marker` with `x/y/z` (+ optional `pitch/yaw/roll`)
  → [`DumpService.TeleportRecallExplicitAsync`](../ui/UE5DumpUI/Services/DumpService.cs) (`:3107`).
* **CE mailbox** — `CMD_TELEPORT = 8`, `op 13 = TP_OP_EXPLICIT` (`dll/src/Mimic.h:158-160`),
  already emitted by [`TeleportScriptGenerator`](../ui/UE5DumpUI/Services/TeleportScriptGenerator.cs).

So the feature is **a persisted UI-side list plus a new caller of an existing
primitive**. P1 is pure C#/AXAML.

-----

## 3. The load-bearing decisions

### D1 — Per-game file key: **exe module name, NOT PE hash**

`BookmarkStore` keys per-game files by `PeHash` (TimeDateStamp + SizeOfImage),
which changes on **every game patch**. Bookmarks *should* die on a patch — they
store offsets. A hand-curated 4 000-entry chest list must **not**.

Key the file by a sanitised `EngineState.ModuleName` (`EngineState.cs:33`):

```
%LOCALAPPDATA%\UE5CEDumper\teleport-coords.{sanitizedModuleName}.json
```

Per §1 the tool makes no validity claim after a patch — this is only about **not
losing the user's data**. Provide *"Import from file…"* for the renamed-exe case.

### D2 — `Map` is a first-class field

`teleport_get_pose` returns the `UWorld` name (`TeleportPose.Map`), and the CE
mailbox exposes it too: the `GET_POSE` output block puts a null-terminated map
name at `paramsData[48..175]` (`Mimic.h:42-45`) — i.e. `mailbox + 0x358`.

The explicit-coordinate teleport path performs **no map check** (only slot recall
returns `-7 MapMismatch`). Therefore:

* Capture `Map` at save time, store it, show it as a column.
* Default the list to **current map only**; other-map entries are flagged and
  require an explicit **Force** action.
* Compare map names **`OrdinalIgnoreCase`**, defined once. CSV import makes `Map`
  user-authored whether we like it or not (`map01` vs `Map01`); an ordinal
  comparison would flag every imported row cross-map and force 4 000 Force clicks.
* **Read the map AT CHECK TIME, never from a cache.** The panel's ~2 s pose poll is
  optional (the user can switch `Auto` off) and does not exist at all behind the
  generated Lua picker, so a cached value meant the guard could fire on a map the
  player had already left. The UI re-reads the pose on the teleport action; the Lua
  picker re-reads it inside its own `go()` — it previously captured the map once when
  the window opened and never again.
* Be honest in the UI: **the tool cannot send you to another map.** Cross-map
  safety is achieved by *filtering*, not by teleporting.

### D3 — Every entry carries a `uid`

An opaque string generated on Add, preserved by **all four** codecs (JSON, Lua,
CSV, and both importers). Without it, merge-import has to fall back to coordinate
equality — and Excel will have already rewritten the text, so nothing matches and
a 4 000-row merge produces 8 000 rows.

**It must NOT be called `id`.** AOBMaker's `CtIdRenumberService` classifies a
script as an "ID-check script" purely by `RxIdField = ((?<![A-Za-z0-9_])id\s*=\s*)(\d+)`
matching (`CtIdRenumberService.cs:216-224`), then rewrites those literals. A user
who saves the CE table and runs *renumber CT IDs* would get every coord `id = N`
silently rewritten. `uid` is safe — the `(?<![A-Za-z0-9_])` lookbehind is blocked
by the leading `u`. **Add a unit test asserting the generated script contains no
`id = <digits>`.**

Merge semantics: `uid` first → `(Label, Group, Map)` case-insensitive fallback →
else append. Three outcomes (matched / ambiguous / new), mirroring AOBMaker's
`CtRecordIndex` resolver.

### D4 — One canonical precision: **round at capture (3 dp), format shortest-round-trippable**

Two reviewers reached opposite conclusions here; the synthesis satisfies both.

* Coordinates are `double` end-to-end. A UE4 `float` widened to double is the
  normal case: `67162.3984375`.
* Every existing formatter in the teleport path is **lossy** — `"0.0###"`
  (`TeleportScriptGenerator.cs:337`, duplicated in `MovementScriptGenerator.cs:255`
  and `TimeDilationScriptGenerator.cs:172`) and `"0.000"` (`TeleportViewModel.cs:1246`).
  Both wire formats are *also round-trip sources*, so a lossy format degrades the
  library on every export→import cycle.
* Bit-exact `"R"` alone is not the answer either: on an unrounded double it emits
  `67162.39800000001`, the author "cleans" it, and a 4 000-row diff becomes noise.

**Round to 3 decimals at CAPTURE time, store the rounded double, then always
format with `"R"` / `CultureInfo.InvariantCulture`.** Verified: the stored value is
then the nearest double to a 3-decimal literal, so `"R"` emits exactly
`67162.398` — clean text *and* bit-identical round-trip, idempotent on the second
cycle. 0.001 uu is 10 µm; nothing in the teleport path is sensitive to it.

| raw (UE float→double) | stored (round 3dp) | emitted `"R"` | reparse == stored |
|---|---|---|---|
| `67162.3984375` | `67162.398` | `67162.398` | ✅ |
| `-20380.642578125` | `-20380.643` | `-20380.643` | ✅ |
| `35.79132080078125` | `35.791` | `35.791` | ✅ |
| `1.4210855e-14` (rotator noise) | `0.0` | `0.0` | ✅ |

Read back with `NumberStyles.Float` (**must** include `AllowExponent` — `"R"` goes
scientific below 1e-5). Normalise negative zero to `0` on capture. The `0.0###` /
`0.000` helpers **must not** be reused here.

### D4b — `Z tolerance`: applied at teleport, never stored

A saved position sits exactly on the floor plane it was captured on, and arriving on
that plane can drop the character straight through it. `Z tolerance` (uu) is added to
Z **at teleport time only** — the entry keeps its exact captured Z, so the value
stays a faithful record of where the user stood.

**Library-wide, not per-entry**: it describes how the *game* resolves an arrival, not
a property of any one place. That is also exactly why it is **in the Lua export but
not the CSV** — the generated picker teleports on its own and therefore needs the
value, whereas a CSV column would imply per-row meaning. It lives inside the fenced
block as `local Z_TOLERANCE = n`, so it round-trips with the coordinates.

A block written before the field existed parses to `null`, **not 0**, and the
importer then leaves the user's current setting alone rather than silently resetting
it. Default is 0 (off): silently moving someone's saved coordinates would be worse
than the problem it solves.

### D5 — Two Lua flavours (R5)

| | **Needs proxy DLL** (mailbox op 13) | **No DLL** (`StandaloneTrainerScriptGenerator`) |
|---|---|---|
| Teleport mechanism | engine invoke (clean, settles) | raw write to `RootComponent.RelativeLocation` |
| Rotation | ✅ `hasRot`, mailbox `+0x358` | needs the already-baked `ctrlRotOff` |
| Current map name | ✅ `GET_POSE` → `mailbox + 0x358` | ❌ → Map RadioGroup omitted, guard degrades to a label |
| Survives a game patch | ✅ | ❌ baked offsets go stale |
| Known caveat | — | `AppendTpWeakNote` (`:442-449`): coords change but the character may not visibly move |

**Recommend the DLL flavour as the default.**

**Delivery differs, and deliberately so.** The DLL flavour pushes via AOBMaker with
a clipboard `.CT`-XML fallback. The no-DLL flavour is **gated on AOBMaker with no
fallback at all**, matching `ExportTrainerCommand` ("Gated on AOBMaker — no
clipboard/disk fallback by design"). The reason is specific: the emitted script
calls `UE5T_pawn` / `UE5T_deref` / `UE5T_wrv` and reads `UE5T.rootOff`, all defined
by the Standalone Trainer's **Setup** record — which itself can only be delivered by
an AOBMaker push. A clipboard blob would hand the user a record that can never work,
failing with `attempt to call a nil value`, which reads as a bug in the script rather
than a missing prerequisite. The DLL flavour keeps its fallback because it depends
only on UE5Dumper.dll being injected, which the user can arrange independently.
The button is `IsEnabled="{Binding IsAobMakerAvailable}"`, again matching the trainer.

-----

## 4. Wire format: adapt AOBMaker's shape, own the namespace

### 4.1 Why this changed

The first draft used a **positional** Lua table (`{'Mountain','Fields','Map01',67162.398,…}`)
parsed by one regex per line. Reviewing AOBMaker's `@AOBMAKER:AA_TOGGLE v1` block
— which solves the identical problem and has a real, tested round-trip parser —
showed the positional design loses on every axis that matters. One agent ran
AOBMaker's shipped assembly as a probe harness; the tolerances below are
**empirically verified**, not read off the source.

| Axis | Positional (first draft) | Named-field (AA_TOGGLE) |
|---|---|---|
| Identify field 6 of 9 while hand-editing | comma-counting | `yaw=` is self-labelling |
| Reordered fields | **silently wrong data** | fine (independent per-key regex) |
| Field added in v2, parsed by a v1 build | **breaks** | ignored, rest still parses |
| Trailing / missing comma, entry split over lines | breaks (line-oriented) | fine (brace-balanced) |
| `--` comment line inserted between entries | dropped row | skipped correctly |
| Diff of two exports | column-shift noise | one key changes |
| Bytes @ 4 000 entries | ~74 B/entry ⇒ **296 KB** | ~120 B/entry ⇒ **480 KB** |

The only axis positional wins is size — and 480 KB is **4.6 %** of the verified
10 MiB pipe cap, nowhere near the binding 5 s timeout. **Robustness wins.**

### 4.2 What the coord library needs that AA_TOGGLE does not

The user's framing: *一個是拿來跑 script，一個是要用來產生 list + 跑 script.*

AA_TOGGLE's data is **iterated once** to toggle records. Ours must additionally
**drive a generated list UI** — a `ListView` plus two `RadioGroup`s (§6). That
means our block needs things AA_TOGGLE has no use for:

* **Facet fields** (`group`, `map`) that are grouped/counted at generation time to
  build the RadioGroups, not just displayed.
* **A search key** — every text field must be concatenable for the filter.
* **Numeric fields with a precision contract** (D4). AA_TOGGLE has only `id` +
  `desc`, so precision never arises.
* **A stable `uid`** (D3) — AA_TOGGLE re-resolves against live CE records each
  run, so it needs no identity of its own.
* **A second wire format** (CSV) that must round-trip to the same model.

Conversely AA_TOGGLE needs a `CONFIG` block (process/module name, step delay,
attach behaviour) that we do not — our runtime config is a handful of constants.

### 4.3 The format

```lua
-- @UE5CD:COORDS v1
-- Edit the rows below (or re-import this whole script into UE5CEDumper).
-- Field order does not matter. Unknown keys are ignored. uid links a row to the
-- library across edits -- keep it if you want an edit to UPDATE rather than ADD.
local COORDS = {
  { uid="k3f9", label="Mountain", group="Fields", map="Map01", x=67162.398, y=-20380.643, z=35.791, pitch=347.71, yaw=31.98, roll=0 },
  { uid="k3fa", label="Chest 1",  group="Chest",  map="Map01", x=87668.672, y=-24858.674, z=341.376, pitch=335.01, yaw=26.16, roll=0 },
  { uid="k3fb", label="Boss",     group="",       map="Map02", x=89828.133, y=-15534.243, z=850.328, pitch=346.88, yaw=0.93, roll=0 },
}
-- @UE5CD:END

---- GENERATED CODE (do not edit below) ----
```

**Namespace: `@UE5CD:`, never `@AOBMAKER:`.** The read relationship between the
two projects is **one-way**: UE5CEDumper only ever *writes* into CE
(`IAobMakerBridge` has no read/list operation), while AOBMaker *reads* `.CT` files
and will parse our `<AssemblerScript>` bodies. Sharing the namespace buys nothing
and costs a concrete failure: AOBMaker's end marker is the **feature-less, shared**
`-- @AOBMAKER:END`, matched by unanchored `IndexOf` (`AaToggleLuaGenerator.cs:22-24,167-172`).
A coord block pasted inside an AA Toggle script would make AOBMaker slice from
`AA_TOGGLE v1` to *our* `END`, parse zero entries, and **silently untick every
record in the tree**.

The `---- GENERATED CODE (do not edit below) ----` separator is copied **verbatim**
— it is inert (a valid Lua comment), never parsed, and it is already the author's
muscle memory across both tools.

### 4.4 Parser contract

Copy AA_TOGGLE's *mechanism* — brace-balanced scan + per-key anchored regex, **not**
a line-oriented regex and **not** a Lua evaluator:

1. Locate the fence with `^--\s*@UE5CD:COORDS\s+v(\d+)\s*$` (multiline, **version
   captured**) and `^--\s*@UE5CD:END\s*$`.
2. Branch on the captured integer: `1` → parse; `>1` → *"This block was written by
   a newer UE5CEDumper (format v{n}); this build understands v1."*
3. Locate `local COORDS = {`, brace-balance to the matching `}`, split into
   top-level `{ … }` groups, then pull each field with
   `(?<![A-Za-z0-9_])key\s*=\s*…`.

Four AOBMaker defects to **not** inherit:

| Defect | Evidence | What we do instead |
|---|---|---|
| Version baked into the marker literal → a `v2` block is indistinguishable from *no block*; the UI says "no markers found" | `AaToggleLuaGenerator.cs:167`; its `Version` const is dead code | capture `v(\d+)`, report unsupported-version distinctly |
| Markers matched anywhere, including inside a string literal | probe: marker inline in `print("…")` parsed | anchor to line start (`^--`, multiline) |
| **100 % silent failure** — no throw, no diagnostic, ever | `TryParseConfig` returns `null` or an empty list | per-row report (§5.3) |
| Single-quoted values silently ignored → defaults | probe: `name='x'` → `name` fell back to default | accept **both** quote styles; unparseable value = a *reported* row error, never a silent default |

### 4.5 Character policy — escape, don't block

> **Superseded 2026-07-22 (commit `002891e`).** An earlier draft hard-blocked the
> literal `]==]`. That commit made the shared escapers neutralise closing long
> brackets of **any** level (`BakedScriptGenerator.EscapeLua` emits `\093`;
> `EscapeLuaComment` pads). The repo's convention is **escape, not block** —
> "strip on import" silently mutates user data.

**Hard-block (reject at input, report on import) — only what cannot be escaped:**

| Input | Why |
|---|---|
| `NUL` (U+0000) | The CE plugin compiles with `luaL_dostring`, which takes no length → `strlen` truncates the chunk (`pipe_server.cpp:881`) |
| `CR` / `LF` | A raw newline inside a Lua single-quoted literal is a **compile error**. Reachable from CSV: Excel's Alt+Enter writes a quoted `"Chest 3\r\n(upper floor)"`, which an RFC-4180-correct reader accepts |
| Other C0 controls | Illegal in XML 1.0 with no entity form; `EscapeXml` passes them through unchanged |

**Everything else is escaped, not blocked** — including `]==]`, `'`, `"`, `\`,
`<`, `>`, `&`, and **CJK**. (The AOBMaker JSON encoder is `UnsafeRelaxedJsonEscaping`
precisely because CE's Lua parser can't decode `\uXXXX`; Crimson Desert ships a
6 508-entry zh-TW dropdown CE renders fine. The repo's ASCII-only rule covers
generated **comments**, not user data.)

**This table is a property of the `CoordEntry` model, enforced at EVERY ingress**
(UI edit, Lua import, CSV import) — not a property of one wire format.

**One shared escaper.** There are already four divergent private `EscapeLua`
implementations (`BakedScriptGenerator.cs:582`, `FreezeScriptGenerator.cs:217`,
`InvokeScriptGenerator.cs:555`, plus `EscapeLuaComment`), and §6 models the picker
form on `InvokeScriptGenerator` — whose copy is the **weakest** (no `\r`, no `\t`,
no `]` handling). Promote one `CeLuaHygiene.EscapeLuaString` + the fence constants
into `CeLuaHygiene`, which is already the single source of truth for
`CloseCall` / `AttributionUrl` / `AppendDebugPreamble`.

Length caps: `Label` ≤ 64, `Group` ≤ 32. Trim surrounding whitespace.

### 4.6 Size budget

Verified limits on the `CreateAAScript` path:

| Limit | Value | Source |
|---|---|---|
| Max JSON message | **10 MiB**, hard reject | `pipe_server.cpp:61`; `PipeProtocol.cs:69` |
| JSON escape expansion | **+2.3 … 4.6 %** measured | `UnsafeRelaxedJsonEscaping` — only `\n` expands |
| Server read deadline | **5 000 ms** total, 10 ms sleep per stall | `pipe_server.cpp:30,55,78` |
| Our response timeout | **5 000 ms** vs **15 000 ms** for `InjectTableFile` | `AobMakerBridgeService.cs:19,35` |

4 000 named-field entries ≈ **480 KB**, ~4.6 % of the cap. Corroborated:
`CrimsonDesert.CT` carries 6 508 entries × 3 lists = **714 KB** in one `.CT`.
Soft-warn above ~2 000 entries for CE's *editor* UX only; no hard cap. If a truly
huge dataset ever appears, the **CE Table File** channel already exists and is
already used by us for helper Lua (`InjectTableFileAsync`; it picks a safe
long-bracket level automatically and gets 15 s).

-----

## 5. CSV (R8)

### 5.1 Format

* **RFC 4180.** Comma-delimited. Quote any field containing the delimiter, `"`,
  or leading/trailing whitespace; escape `"` by doubling. **Mandatory** — §4.5
  permits `,` in a Label, and one unquoted comma shifts every subsequent column.
* **Header row is authoritative.** Columns are matched **by name**, so reordered
  and extra columns are handled and a v2 column is backward-compatible — the same
  property that made the named-field Lua win.
* **Split positionally with NO `RemoveEmptyEntries`**, then hard-validate the
  column count. The repo's only existing delimited reader, `BugItGoParser.cs:66-68`,
  uses `RemoveEmptyEntries | TrimEntries` — the obvious template, and wrong here:
  an empty `Group` (a real case — see the Boss sample) collapses 9 fields to 8 and
  shifts `Map`→`Group`, `X`→`Map`, silently.
* **Encoding: UTF-8 WITH BOM** — a deliberate, documented exception to the house
  rule (`DumpAllService.cs:99-103` pins `UTF8Encoding(false)`). Without a BOM,
  double-clicking a CJK export on a zh-TW machine hands Excel the ANSI codepage and
  every label is mojibake. Read with `detectEncodingFromByteOrderMarks: true`
  (`DumpJsonlReader.cs:36` precedent).
* **Write `\n`** (per `DumpAllService`); **accept** CRLF and LF, trimming a trailing
  `\r` before any ordinal header comparison — Excel writes CRLF.
* **Sniff the delimiter** from the header among `, ; \t`. When it is `;`, accept
  comma as the decimal separator (a de-DE/fr-FR Excel re-save produces
  `Boss;;Map02;89828,133;…`).
* Numbers per **D4**: `"R"` + `InvariantCulture` on write, `NumberStyles.Float`
  (with `AllowExponent`) on read.
* **Formula-injection armouring.** On export, prefix a single `'` to any
  `Label`/`Group` whose first character is `= + - @`; on import, strip exactly one
  leading `'` from those columns. Round-trip transparent. Without it, a label
  `=Boss Arena` displays as `#NAME?`, and Excel saves the *displayed* text — the
  label is destroyed with no error anywhere. The security variant
  (`=cmd|'/c calc'!A1`) is not mitigated by quoting.

### 5.2 Samples — including the hard cases

The three clean rows are not the interesting part; these are what an implementation
must actually survive.

```csv
uid,label,group,map,x,y,z,pitch,yaw,roll
k3f9,Mountain,Fields,Map01,67162.398,-20380.643,35.791,347.71,31.98,0
k3fa,Chest 1,Chest,Map01,87668.672,-24858.674,341.376,335.01,26.16,0
k3fb,Boss,,Map02,89828.133,-15534.243,850.328,346.88,0.93,0
k3fc,"Chest, big one",Chest,Map02,1.5,-2.25,0,0,0,0
k3fd,"He said ""hi""",Other,Map02,0,0,0,0,0,0
k3fe,寶箱 3（上層）,寶箱,Map03,-4096.5,8192.25,120,0,180,0
k3ff,'=Boss Arena,Other,Map03,10,20,30,0,0,0
k400,'1-2,Zones,Map03,0,0,100000,0,0,0
```

Row by row: an **empty group**; an **embedded comma** (quoted); an **embedded
quote** (doubled); a **CJK label with full-width parens**; a **formula-armoured**
label (`'` stripped on import → `=Boss Arena`); and a label Excel would eat
(`1-2` → a date).

Note the last row's X: an input of `1e-5` is **below** the 3-decimal precision, so
D4's capture rounding collapses it to `0`. The writer rounds too — defensively, so
`Write → Parse → Write` is byte-identical no matter how the entry was constructed,
not only for entries that came through a pose capture. In practice that also means
`"R"` never emits scientific notation for a realistic coordinate; the reader still
sets `AllowExponent`, which costs nothing and covers a hand-written file.

### 5.3 Import is a two-stage, consented operation

This is the single highest-value decision in the CSV design. **Never apply an
import in one shot.**

Some corruptions are **unfixable on our side**: Excel silently coerces `1-2` and
`3/4` to dates, `0012` to `12`, `1E5` to `100000`, and writes back the *displayed*
text. Quoting does not help. No validator can detect it, because the result is
perfectly valid CSV.

So the only real defence is to make changes **visible before they commit**:

* **Stage 1 — parse + report.** Scan the whole file with resync-on-error. Produce
  per-bad-line diagnostics (1-based file line, column name, raw text, reason) and
  a **diff against the current library**: added / changed / removed / mutated
  (trimmed, truncated, control-char stripped). Show cell-level changes:
  `row 812 Group: 1-2 → 2001/1/2`.
* **Stage 2 — commit or cancel.** *"Import 3 998, skip 2, update 14, add 27?"*
* Write `teleport-coords.{module}.preimport.bak` **before** the commit, and offer
  session-scoped Undo. A botched Replace is one click deep, and this is
  hand-curated data.

Partial success without a report is exactly the silent-drop bug the repo already
ships in `DumpJsonlReader.cs:38-53` — and the worst property of AOBMaker's parsers.
Do not reproduce it.

**Export the model, never the view.** Always the full library, never the filtered
grid and never the computed `Distance` column. A filtered export must be a
separate, explicitly labelled action that states the count — otherwise a user
filters to one map, exports, re-imports with Replace, and loses everything else.

### 5.4 "Samples embedded" — pinned reading

Sample rows live **in this spec** and in a copyable *"Format help"* block in the
export dialog, plus an **"Export sample CSV / template"** action for users starting
from scratch. The **exported data file carries only** the header + data rows.

Comment lines inside the file are rejected because they do not survive a spreadsheet:
the moment the author sorts by Label, `#` rows scatter into the middle of the data,
and a comment check that runs before quote handling would eat a legitimate label
like `#3 side room`.

-----

## 6. The generated Lua picker (R6)

### Verified CE Lua API surface

Project rule: **never invent a CE Lua API.**

*Already emitted by us* (`InvokeScriptGenerator.cs:235-305`): `createForm`,
`createLabel`, `createEdit`, `createButton`, `createTimer`.
**`createListBox` and `createComboBox` appear nowhere in this repo and are NOT verified.**

*Verified working in `CrimsonDesert.CT` "open item ID query GUI"* (CheatEntry 357,
`:9306-9650`) — the reference:

| Control | Verified members |
|---|---|
| `createForm` | `.Caption .Width .Height .Position('poScreenCenter') .ClientWidth .ActiveControl .Destroyed .show() .close() .BringToFront() .OnClose → caFree` |
| `createPanel` | `.Align('alTop'/'alBottom') .Height .BevelOuter('bvNone')` |
| `createLabel` | `.Caption .Left .Top .Align('alClient')` |
| `createEdit` | `.Left .Top .Width .Text .OnChange` |
| `createRadioGroup` | `.Caption .Top .Left .Width .Height .Columns .Items.add(str) .ItemIndex .OnClick` |
| `createButton` | `.Caption .Left .Top .Width .Height .OnClick` |
| `createListView` | `.Align .ViewStyle('vsReport') .RowSelect .ReadOnly .MultiSelect .SelCount .Selected .ItemIndex` · `.Columns.add().Caption`, `.Columns[i].Width` (0-based) · `.Items.beginUpdate()/.clear()/.add()/.endUpdate()/.Count/[i]` · row `.Caption`, `.SubItems.add(str)`, `.Selected` · `.OnDblClick`, `.OnKeyDown(sender,key,shift)` |
| globals | `synchronize(fn)`, `showMessage`, `writeToClipboard`, `caFree`, `syntaxcheck` |

**Build only from that set: `createListView` + `createRadioGroup`. No ListBox, no
ComboBox, no CheckBox.**

> ⚠ **This table is a whitelist of what we have SEEN work, not the CE API surface.**
> Absence from it is not evidence that something does not exist. `ListView.ItemIndex`
> was missing here and a later investigation concluded from that absence that it was
> an invented API and the root cause of a bug — wrong on both counts. It is
> documented (`celua.txt` "Listview Class", *"ItemIndex: integer — the currently
> selected index, -1 if nothing is selected"*) and explicitly registered in CE's own
> source (`LuaListview.pas`, `luaclass_addPropertyToTable(... 'ItemIndex' ...)`), so
> it returns an integer and can never raise "unknown property".
> **Before declaring an API invented, check `celua.txt` and CE's source — not this table.**

### Layout rules from the reference

* **Panel creation order is load-bearing**: `alTop` panels stack in creation order
  and the `alClient` control **must be created last** (`:9428,9519`). Order:
  `pnlTop` → `pnlStatus` → `pnlBottom` → `lv`.
* Re-open guard: `if frm ~= nil and not frm.Destroyed then frm.show(); frm.BringToFront(); return end`.
* A status label doubles as the live match counter.
* Display cap **2 000 rows** with *"Matched N (showing first 2000) — type more to
  narrow"* (`:9552`). Note this is the reference's **hardcoded guess**; no
  measurement of where a CE ListView stutters exists.

### Two facets, two RadioGroups

The reference's `sources` (EN/TW/JA) act like our **Group**, hard-capped at 1–3
(`:9354`). We have two facets:

* **RadioGroup A — Group.** `All` + the **top 8 groups by entry count**. Overflow
  groups are not lost: reachable via `All`, and the group name is in the search
  key. Warn at export time when groups were folded.
* **RadioGroup B — Map scope.** `Current map` / `All maps`. DLL flavour only.

### Filter

The reference uses a plain case-insensitive substring over `lower(id .. ' ' .. desc)`
with no debounce. **We mirror the app's MUST-rule instead: whitespace-separated
terms combined with AND**, over `lower(label .. ' ' .. group .. ' ' .. map)`. A few
lines of Lua, and app/script semantics stay identical.

Rows are **pre-sorted at generation time** (Group asc, then Label natural-sort so
`Chest 2` precedes `Chest 10`), so the Lua never sorts.

### Confirm-before-teleport (R3)

No CE yes/no dialog API is verified, so use two buttons — which also matches the
existing marker Force semantics:

* **`Teleport`** — refuses with `showMessage` when the entry's map ≠ current map
  (compared `OrdinalIgnoreCase`, D2), otherwise fires `CMD_TELEPORT` op 13.
* **`Force teleport`** — ignores the map guard.
* `OnDblClick` = `Teleport` (guarded), never Force.

### Hygiene

Interactive form → **not** the momentary auto-close path. Follow
`InvokeScriptGenerator`'s shape: untick + `CeLuaHygiene.CloseCall` in `frm.OnClose`,
and **every error path leaves the window open** (`CLAUDE.md` MUST-rule). Emit
`CeLuaHygiene.AppendDebugPreamble` in every `{$lua}` block that uses `DEBUG`/`dbg`.

-----

## 7. App-side UI (R2 / R4)

### Placement

A new card immediately **below the existing "TP to coords" card**
(`TeleportPanel.axaml:466-507`), which gains a **`＋ Add to library`** button.

The tab's right-click quick-jump menu is built **dynamically** from the visual
tree — no code-behind change needed, but three hard structural requirements:

1. Direct child of the `ContentRoot` StackPanel.
2. Must be a **`Border`** (other element types are skipped).
3. Label = the **first `FontWeight="SemiBold"` TextBlock descendant**.

Chrome verbatim: `Background="#252526" BorderBrush="#3E3E42" BorderThickness="1"
CornerRadius="4" Padding="10"`, inserted before the status strip.

⚠ **Verify at implementation time:** the card is a `Border` wrapping an `Expander`.
Confirm the SemiBold header in the `Expander.Header` is still reached by the
first-SemiBold-descendant walk, or the menu shows a wrong label.

### Collapsible, collapsed by default

VM-bound `IsExpanded` dialect (Snapshot / Spc / LiveWalker panels),
`[ObservableProperty] bool` defaulted to **false**. The VM may force it open after
a *Save current pos*. No panel persists `IsExpanded` across sessions today; if
wanted, that needs a new `UiOptionsSettings` field.

### Contents

* **Toolbar** — `Save current pos` · `Add` · `Edit` · `Duplicate` · `Delete` ·
  **`Teleport selected`** · `Export ▾` (Lua DLL / Lua no-DLL / CSV / sample CSV) ·
  `Import ▾` (Lua script / CSV) · `Clear all`
* **Filter row** — keyword `AutoCompleteBox` + Group combo + `Current map only`
* **Grid** — Label / Group / Map / X / Y / Z / Pitch / Yaw / Roll / Distance
* **Selection preview** — `Chest 2 — Map01 — 1,204 uu away`. Distance is free: the
  panel already polls `get_pose` every ~2 s.
* **Commit** — single click selects; `Teleport selected` fires; map mismatch raises
  a confirm. Reuse `TeleportToCoordsAsync`'s shape (`IsConnected` guard,
  `TeleportCodes.Describe`, `Tier == 2` raw-write warning). `Save current pos`
  reuses `FillCoordsFromCurrentAsync`'s `TeleportGetPoseAsync` + `ApplyPoseAndMovement`.

### ⚠ DataGrid virtualization

`TeleportPanel` has **no DataGrid today**, and `ContentRoot` sits in a vertically
unbounded `ScrollViewer` — **a DataGrid there will not virtualize**. With 4 000 rows
that is a hard perf problem. The grid **must** carry an explicit `MaxHeight`
(`LiveWalkerPanel.axaml:805` uses `280`). `ContentRoot` also swallows
`RequestBringIntoView`, so `BringIntoView()` will not work — scroll via
`Scroller.Offset`.

### Non-negotiable project rules

* **Keyword box** — space = AND via `ObjectTreeFilter.SplitTerms` +
  `MatchesAllTerms(terms, params string?[])`, **and** per-keyword memory via
  `KeywordSearchMemory` (field + `XxxHistory` + ctor probe + `Schedule(value)`).
  Client-side list ⇒ `Schedule`, not `Commit`. `AutoCompleteBox` with
  `PlaceholderText` and `Text="{Binding …, Mode=TwoWay}"`.
* **UI strings** — English only, new `str.TP.*` keys in `Resources/Strings/en.axaml`.
* **Grid sorting** — per-column `SortMemberPath` at the underlying model property.
* **AOT** — source-gen JSON only.

-----

## 8. Store

Structural clone of `BookmarkStore`: sync, `_ioLock`-guarded, source-gen JSON,
atomic temp+rename, swallow-and-log with empty defaults, `Delete()` as clear-all.

| Aspect | Decision |
|---|---|
| File key | **exe module name**, not PE hash (D1) |
| `DefaultIgnoreCondition` | **MUST NOT** be `WhenWritingDefault`. Follow the `BookmarkFile` / `UiOptionsSettings` dialect — a legitimately-saved coordinate of exactly `0.0` would otherwise be dropped and reload as 0 *by accident rather than by record* |
| Backups | `.bak` alongside the atomic rename, **plus** `.preimport.bak` before any import commits (§5.3) |
| Model | `public sealed class`, plain get/set POCOs, `Version` int (v1), `List<CoordEntry>`, each with `uid` (D3) |
| Context | `internal partial class CoordinateLibraryJsonContext : JsonSerializerContext` in `Models/` (never `Services/`) |
| Wiring | `App.axaml.cs` → trailing optional ctor param on `MainWindowViewModel` → trailing optional param on `TeleportViewModel` (which takes no store today) |

**Load hook.** `TeleportViewModel.SetEngineState` (`:226`) is a one-line stash that
does not capture identity. Mirror `LiveWalkerViewModel`: capture the key in
`SetEngineState`, but load from a **separate public `LoadCoordLibraryForGame(...)`**
called explicitly by `MainWindowViewModel` — so the load is not a hidden side effect.

⚠ `MainWindowViewModel` has **two** engine-state fan-out sites and they are not
symmetric: `:2502-2521` (`ApplyEngineState`) performs the bookmark load; `:613-623`
(connect path) does not. Wire deliberately.

**One codec seam.** Put the JSON, Lua, and CSV codecs behind one `CoordEntry` with a
**Generate → Parse → Generate byte-stability test for each**. AOBMaker wrote that
test only for `AUTO_REFRESH`, and `AA_TOGGLE` drifted out of idempotence as a
direct result.

-----

## 9. Phasing

| Phase | Content | Effort | Risk |
|---|---|---|---|
| **P1** | Model (+`uid`, D4 precision) + store + collapsible card + CRUD + `Save current pos` + `Teleport selected` with map guard + filter | **M** | low — UI only |
| **P2** | CSV export + two-stage import with diff preview (§5) | **M** | low-med — the preview is most of the work, and it is the point |
| **P3** | Lua export, DLL flavour: `@UE5CD:COORDS v1` block + generic picker form + AOBMaker push, clipboard `.CT` XML fallback | **S-M** | low-med |
| **P4** | Lua re-import (R7): fence + version capture + brace-balanced parser, sharing P2's report/preview | **S** | low |
| **P5** | No-DLL flavour via `StandaloneTrainerScriptGenerator` (degraded map guard) | **S** | med |

P2 before P3 is deliberate: CSV is the format users will actually curate 4 000 rows
in, and the import-report machinery it forces is reused by P4.

-----

## 10. Open questions for implementation time

1. **Quick-jump label** — does the first-SemiBold-descendant walk reach a TextBlock
   inside an `Expander.Header`? (§7)
2. **ListView throughput** — the reference's 2 000-row display cap is an unverified
   guess. Measure before treating it as meaningful.
3. **Which `MainWindowViewModel` fan-out sites are reachable** for the store load. (§8)
4. ~~**Experimental gating**~~ — **DECIDED: gated** (user call, 2026-07-23). The
   card carries `IsVisible="{Binding ExperimentalEnabled}"` like the other five.
   The draft's reasoning ("a coordinate bookmark list is not combat-affecting")
   was too narrow: it writes the pawn position live and emits CE scripts that do
   the same. Gating also gets the quick-jump menu right for free — the code-behind
   already skips a card that is not `IsEffectivelyVisible`.
   Two lifecycle consequences, both implemented: an un-applied import preview is
   cancelled when the gate goes off (it would otherwise sit behind a hidden card
   where the user can neither see nor cancel it), and — a pre-existing bug the
   gating work surfaced — it is also dropped when the active game changes, since
   the diff was computed against the previous game's library.
5. **CE Lua `readString`** — the DLL-flavour picker reads the map name from
   `mailbox + 0x358`. Confirm against CE's API reference before use.
6. **Client-side pre-flight size check** — `AobMakerBridgeService.WriteMessageAsync`
   (`:495-506`) has **no** send-side cap, and the plugin's oversize path
   (`pipe_server.cpp:61`) returns *without writing a response*, so an oversized push
   surfaces as a confusing *timeout*. Worth adding regardless of this feature.

-----

## 11. Cross-references

* [teleport-spec.md](teleport-spec.md) — Wirbel markers, POV, coord TP, the `CMD_TELEPORT` op table
* [aobmaker-integration.md](aobmaker-integration.md) — the CE-plugin pipe bridge
* [export-formats.md](export-formats.md) — CE XML / CSX export rules
* [ui-spec.md](ui-spec.md) — Avalonia stack + AOT constraints
* `CLAUDE.md` → *CE Lua output hygiene* and *Keyword search boxes* — MUST-rules this
  feature is bound by
* **Reference tables** (external, `D:\Github\Mydev-Cheat-Engine-Tables`):
  `Crimson Desert\CrimsonDesert.CT` CheatEntry 357 (the picker-form UI reference) and
  `Kyoto Xanadu\kyoto_xanadu.CT` lines 1-90 (the `@AOBMAKER:AA_TOGGLE v1` round-trip
  reference)
