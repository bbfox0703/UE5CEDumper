# Requests to AOBMaker, from UE5CEDumper

**Filed 2026-09-29 against AOBMaker `dev` at `2528712`.** The work belongs to the AOBMaker repository. The list
lives here because UE5CEDumper is the client that needs it. The evaluation behind every item is
[aobmaker-integration-eval.md](aobmaker-integration-eval.md). The UE5CEDumper rows each request unblocks are in
[todo.md](todo.md) under `[AOBMAKER-EVAL-2026-09-29]`.

**AOBMaker replied in `9431370` (2026-09-29):** AOBMaker `docs/UE5CEDumper-Requests-Reply.md`. Its §1 gives each
request's status, and its §3 lists the rules a client must follow today. AOBMaker updates §1 and §3 in the commit that
ships an item. The full source check is its `docs/UE5CEDumper-Requests-Evaluation.md` (Traditional Chinese). The
defects we reported, re-measured there, are rows `EXT-2`–`EXT-15`; the ones it found itself while checking our
list are `EV-1`–`EV-5` (its `docs/Bug-Leak-Audit-Control-Table.md` §5).
The reply answers R1–R16. R17 was filed after it and is not answered. Each request below now carries an
**AOBMaker's answer** line.

**Updated for AOBMaker release `v20260930` (build 157), 2026-09-30** (reply at AOBMaker `dev` `f8cad7d`): the first shipped
fixes. Shipped in full: R3, R4, R15 (bulk nodes and import), R16 (a)–(e) and (g); in part: R1 (bulk `features[]`),
R2, R7 (steps 1–2), R8 and R9. The reply's §0 names the builds: **155** has the first batch, **156** adds import
fidelity and the `FindMostAccessed` replies, and both first ship in v20260930; "build 153 and earlier" is every
release up to `v20260925`. Its §3 is the client contract now, rule by build -- read rules **8** (`activated`,
`symbolRegistered`), **16** (read `features` before sending a new node member) and **20** (scripts verbatim when
the DLL lists `bulk.scriptVerbatim`) before any new bridge call. A rule that is lifted on 155+ still applies to a
user on an older build, and no command reports the DLL's build except `features`.

**When a request ships:**
1. Write the AOBMaker commit into its row in the summary table.
2. Flip the UE5CEDumper todo row it unblocks.

AOBMaker file paths below are relative to the AOBMaker repository. Line numbers in the requests are pinned to
`2528712`; line numbers in the **AOBMaker's answer** lines are at `9431370`.
Everything was read from source; nothing was run. Where a claim is inferred, it says so.

## Ground rules for every request

- **Backward compatible.** Add optional request fields and new reply fields; never change the meaning of an
  existing one. UE5CEDumper keeps talking to older plugins.
- **Detectable (R1).** Today an older plugin **silently ignores** a field it does not know. That is how `group` and
  `showAsHex` rolled out (see [aobmaker-integration.md](aobmaker-integration.md)), and a client cannot tell
  "applied" from "ignored". Every new field below should appear in R1's `features` list.
- **No new modal dialogs.** The bridge has one worker and no handler timeout, so a dialog blocks every client. A
  request that makes CE do more work must not add a path that can raise one. UE5CEDumper broke this rule itself
  with the standalone trainer's Setup, pushed ticked; since `1f9cf2ef` it is pushed unticked
  (`[AOBM-TRAINER-SETUP-MODAL]` in [todo.md](todo.md); AOBMaker reply §2.4).

## Summary

"Asked" is the priority we filed. The AOBMaker column is its reply §1: verdict, its own priority, status.

| ID | Asked | Kind | Request | AOBMaker (reply §1) | Unblocks in UE5CEDumper | Shipped in |
|---|---|---|---|---|---|---|
| R1 | P1 | new command | Capability discovery on the CE bridge | Accepted, narrowed · P1 · bulk `features[]` shipped: 4 names on build 155, 8 on 156 | every row below — feature gating | `d34abac` (155), `3f09138` `00a3ec0` `db934b6` (156) · v20260930 -- bulk only |
| R2 | P1 | defect / schema | Import keeps bit fields, dropdowns, colours, string flags; reports what it drops | Partly shipped: bits, child options (155); symbolic pointer offsets, `ByteLength`, `Async`, header type, verbatim scripts (156) · not started: `dropped`, dropdown, colour, `codePage` | `[AOBM-CEXML-PUSH]` | `d34abac` `f383dd1` (155), `3f09138` `e69a8fb` `00a3ec0` `db934b6` (156) · v20260930 |
| R3 | P1 | defect | An address-less `GroupHeader` imports as a plain folder | Shipped (`EXT-2`); import needs AOBMaker.UI + DLL of the same build, 155+ (156 for symbolic offsets, `ByteLength`, `Async`; reply §3 rule 15) | `[AOBM-CT-PUSH]` | `d34abac` · v20260930 |
| R4 | P1 | defect | `autoActivate` reports whether CE actually enabled the script | Shipped (`EXT-4`): `activated` tri-state + CE's reason in `message`; `symbolRegistered` on `CreateSymbolScript` (reply §3 rule 8) | `[AOBM-ACTIVATE-RESULT]` | `ad247a2` `f383dd1` · v20260930 |
| R5 | P1 | new commands | Record IDs in replies; find, delete, and skip-or-replace on create | IDs accepted under other names · P1; `FindRecords` scoped · P2; delete and `replace` on hold · not started | `[AOBM-DEDUP-TABLE]` | |
| R6 | P2 | schema | `CreateMemoryRecord` carries bits, dropdowns, string flags, offsets, colour | Not extended: R2's node fields in a one-node batch · P3 · not started | `[AOBM-PLUSCE-FIDELITY]` | |
| R7 | P2 | defect / schema | `group` matches headers only; nested group paths; colour on AA scripts | Partly shipped: steps 1–2 (a failed call cleans up; plain `group` matches root-level headers only) · `groupPath`, `color` not started | `[AOBM-CT-PUSH]` | `d28a4d0` · v20260930 -- steps 1–2 |
| R8 | P2 | defect / limit | Imports over 10 MiB; honest partial-failure counts; a reply to oversize requests | Partly shipped: the count fix, an oversize reply on both pipes, a chunk's `created` on a Lua error · streaming import not started | `[AOBM-CEXML-PUSH]` | `f5c57da` `f2730bb` `7e26152` `89c8edd` · v20260930 |
| R9 | P2 | defect / schema | `FindMostAccessed`: every hit, read vs write, size, the right instruction, no long hold | Partly shipped: 30 s clamp, own-hit filter, failure replies (155); an exited process and a failed attach get their own replies, navigation on CE's main thread (156) · extension not started | `[AOBM-FIND-ACCESS]` | `5276da8` (155), `1bbcac6` (156) · v20260930 |
| R10 | P3 | schema | A record-level hotkey at creation — only if proven reliable | Not accepted for now: the creating Lua call is unverified · on hold | `[AOBM-RECORD-HOTKEYS]` | |
| R11 | P3 | new command | Attach CE to a process id | Accepted · P3 · not started | `[AOBM-ATTACH-CHECK]` (its one-click fix) | |
| R12 | P3 | new command | Create a Structure Dissect structure from CSX XML | Declined as specified; the workaround is ours | `[AOBM-DISSECT-INJECT]` (the CSX half — now our own work) | |
| R13 | P3 | new command | An injection AA script from an address, over the UI pipe | Accepted in principle, design first · P3 · not started | `[DENKEN-WRITE-SITES]` | |
| R14 | P3 | new command | Find the instructions that reference an address | Accepted, C# only · P2 · not started | `[AOBM-EXPORT-GWORLD-AOB]` | |
| R15 | P3 | schema | `customType` on records | Shipped for bulk nodes and import (`EXT-15`); `CreateMemoryRecord` has none -- use a one-node batch | `[AOBM-CUSTOMTYPE]` | `d34abac` · v20260930 |
| R16 | P2 | defects / docs | Protocol hygiene: whitespace, key order, number types, nulls, missing replies, doc drift | Shipped (a)–(e), (g); (f) closed as docs (AOBMaker's own colours are correct) | any client of the bridge | `3ba4729` `9b3f522` `7e26152` `b9af932` · v20260930 |
| R17 | P3 | schema | `CreateSymbolScript` adds a constant after the RIP resolution | Not in the reply: filed after it | `[AOBM-GWORLD-GENAOB]` (adjusted signatures) | |

---

## R1 — Capability discovery (P1, new command)

- **Why:** UE5CEDumper cannot tell which plugin build it is talking to.
  - The UI pipe answers `Ping` with `version: "2.0"`. The CE bridge has no equivalent at all.
  - Unknown request fields are ignored without a trace. So "the plugin does not support `group`" and "the group
    was applied" look identical to the client.
- **Request:** `{ "type": "GetCapabilities" }`.
- **Reply, as AOBMaker plans it (reply §2.1):** `commands[]` and `features[]` (flat string arrays, such as
  `CreateAAScript.activated` or `bulk.bitFields`), plus the plugin build number.
  - We had asked for a per-command `fields` map. The plugin's JSON builder writes only strings, ints, bools and string
    arrays, so it cannot write one.
  - There is no `protocolVersion`. The bridge has none; `"2.0"` is the AOBMaker.UI pipe's `Pong` (AOBMaker `EV-5`,
    whose `API-CEPlugin.md` Versioning fix is in `e75562d`; `PipeProtocol.cs:70`).
- **Compatibility:** an older plugin answers `Unknown type`. The client reads that as "baseline 2.0, features unknown".
- **Limit:** R1 covers the CE bridge only. For the AOBMaker.UI pipe (R13, R14) the plan is `features[]` on `Pong`
  (batch A). An older AOBMaker.UI never replies to a type it does not know, so probe it with a read timeout.
- **AOBMaker's answer:** accepted, narrowed; P1, batch B; not started.
- **Acceptance:** the reply lists every dispatched command, and a feature string for each new field from R2–R15 once
  it ships.

## R2 — Import fidelity: bit fields, dropdowns, colours, string flags (P1, defect / schema)

- **Today:**
  - `src/AOBMaker.Core/Services/CheatTableXmlImporter.cs` reads only these entry fields: `Description`,
    `VariableType`, `AssemblerScript`, `Address`, `Offsets`, `GroupHeader`, `ShowAsHex`, `ShowAsSigned`, `Length`,
    `Unicode`, and `Options@moHideChildren`.
  - Everything else is dropped without a warning. `docs/Known-Scope-Limits.md` records this as a scope decision:
    "structural tree rebuild … not byte-perfect display-metadata fidelity".
- **Why that decision does not hold for bit fields:** `<BitStart>` and `<BitLength>` are **not** display metadata.
  They select which bit a `Binary` record reads. UE5CEDumper emits a bit-field bool exactly that way. Imported without
  them, the record reads CE's default bit instead of its own, so it can show the wrong value.
- **Also needed:**

  | Element | What UE5CEDumper uses it for |
  |---|---|
  | `<DropDownList DisplayValueAsItem="1">`, `<DropDownListLink>` | Enum and FName names |
  | `<Color>` | Alternating row colours |
  | `<CodePage>`, `<ZeroTerminate>` | String display (`<ZeroTerminate>` cannot be carried; see AOBMaker's answer) |
- **Request:**
  - Add these fields to `BulkRecordNode`, the importer, and the plugin's node Lua:
    - `bitStart` and `bitLength`;
    - `dropDown`, with `list`, `displayValueAsItem`, `readOnly` and `descriptionOnly`;
    - `dropDownLink`;
    - `color`, as CE's own TColor value (see R16);
    - `codePage`. (`zeroTerminate` was declined.)
  - Add `dropped: { "<Element>": count }` to the `ImportCheatTableXml` reply, so nothing is lost silently.
- **AOBMaker's answer (reply §2.2):** split; not started.
  - Bit fields (`EXT-3`) and `dropped`: accepted, P1. Its `Known-Scope-Limits.md` no longer calls bit parameters
    display metadata (`e75562d`, `Known-Scope-Limits.md:118-124`).
  - Dropdown, colour and `codePage`: later, P2–P3. Dropdown only after R3.
  - **`zeroTerminate`: declined.** CE 7.5 and 7.7 Lua expose only `Size`, `Unicode` and `Codepage` on a record's
    `String`, so setting `ZeroTerminate` is a no-op (AOBMaker row `G3-MED-11`, measured on CE's `lua53-64.dll`).
  - Also dropped today, and missing from our list:
    - `moActivateChildrenAsWell`, and `moDeactivateChildrenAsWell` without `moHideChildren`. The plugin writes
      `[moHideChildren,moDeactivateChildrenAsWell]` on every collapsed node (`EV-1`).
    - `ByteLength`, `ShowAsBinary`, `Hotkeys`, and `VariableType=Custom` (`EXT-15`).
  - Until R1 ships, `dropped` cannot see what an older plugin ignores.
- **Acceptance:** on the DumperTest fixture, import Live Walker's "Copy CE XML" output of a class that has a
  bit-field bool, an enum and a string. The CE tree must equal the one the same XML produces through a paste:
  same bits, same dropdowns, same colours.

## R3 — An address-less `GroupHeader` imports as a plain folder (P1, defect)

- **Today:**
  - `BulkRecordValidator.ValidateAddress` adds "address is empty" for every non-script node with no address.
  - `WindowsPipeServer` aborts the whole import on any validation error.
  - A bulk node with `group` becomes `IsAddressGroupHeader`, a header that carries an address and a value column.
- **Why it is a defect, not a format choice:**
  - A plain folder header with no address is CE's own shape.
  - AOBMaker's `samples/cheat_tables` hold **573** address-less `GroupHeader=1` entries across **18** of the 19
    tables; `4_elements_ii` has none. We first counted 19 tables; AOBMaker re-measured it with its own importer and
    validator (reply §2.3, `EXT-2`).
  - UE5CEDumper's `CheatTableBuilder` writes its root and category folders that way too, so every generated `.CT` is
    rejected whole.
- **Request:** a `GroupHeader` entry without `<Address>` becomes an `IsGroupHeader` folder. So does a bulk node with
  `group: true` and an empty `addr`. Keep today's behaviour for a header that does carry an address.
- **AOBMaker's answer (reply §2.3):** accepted, its own defect; P1, in two stages; not started.
  - Stage 1 (batch A, C# only): the validator accepts a group with no address. The current plugin then builds an
    address group header (`IsAddressGroupHeader`) with no address.
  - Stage 2 (batch B, plugin): those nodes become `IsGroupHeader` folders.
  - Stage 1 ships together with a loud reject (or a `dropped` report) of the records the importer cannot carry, such
    as Binary and Custom records.
  - `CheatTableBuilder` output imports after stage 1. Its folders look like address headers until stage 2.
- **Acceptance:** Interesting Properties' "Generate CT" output imports with the same tree as CE's File → Load of
  that `.CT`.

## R4 — Report whether `autoActivate` enabled the script (P1, defect)

- **Today:**
  - `plugins/CEPlugin/src/pipe_server.cpp` 1262–1265 (`CreateAAScript`) runs `if ok then mr.Active = true end` and
    returns `ok`. `ok` was computed **before** activation, and `Active` is never read back.
  - `CreateSymbolScript` has the same shape (1579–1582). A script CE failed to enable still replies `success:true`.
- **Risk:** activation runs on CE's main thread while the bridge's single worker waits. AOBMaker's own live test saw
  an `Active=true` enable raise CE's "Nearby allocation error" Yes/No dialog, and its driver stopped there
  (`docs/Live-Verification-Multi-Apply.md`). That driver enabled the record from CE Lua directly, not through the
  bridge (AOBMaker eval R4).
- **Request:**
  - After setting `Active`, read it back and reply with `activated` next to `success`. `success` keeps its
    meaning: the record was created.
  - ~~Before activating, run CE's `autoAssembleCheck`.~~ **Withdrawn; AOBMaker declined it, rightly.** Both our
    consumers begin `{$lua}` + `if syntaxcheck then return end`, so the check sees an empty script. CE also runs
    unguarded `{$lua}` blocks during the check, so their side effects would run twice. And the check can return a
    bare `false` with no message.
- **AOBMaker's answer (reply §2.4, `EXT-4`):** accepted with changes; P1, batch B; not started.
  - `activated` is tri-state: `true`, `false`, or absent. Absent means unknown: an older plugin, or no
    `autoActivate`. A client timeout is also "unknown".
  - The error text goes in the existing `message` field, not in a new `error` field.
  - `CreateSymbolScript` will also report whether the symbol resolves after activation. Today a failed scan only
    prints `[SymbolScanner] WARNING: AOB scan failed`, so `Active` alone reads `true`.
  - A third activation site we missed: `CreateAAScriptWithRecords` activates unconditionally, before its own check.
    An exception there replies `success:false` although the records exist.
- **Consumers in UE5CEDumper:**
  - the Pointer panel's symbol buttons: Register GWorld, Register &GEngine, and SYM on GObjects / GNames;
  - the standalone trainer's Setup row, until `1f9cf2ef` pushed it unticked.

  The symbol buttons use `autoActivate:true` and report success whatever happened. The trainer Setup did the same
  until `1f9cf2ef`, and its own failure path also raised a modal over the bridge: our defect,
  `[AOBM-TRAINER-SETUP-MODAL]` in [todo.md](todo.md), fixed in source by leaving Setup for the user to tick.
- **Acceptance:** send a script with a deliberate syntax error and `autoActivate:true`. The reply is
  `success:true, activated:false`, with the reason in `message`. With no pre-check, nothing guarantees that no dialog
  appears; whether a failed activation raises one is not measured yet (AOBMaker eval §6).

## R5 — Record identity: IDs, find, delete, skip-or-replace (P1, new commands)

- **Today the bridge is write-only.**
  - No create reply carries the new record's ID: they return `type` and `success`.
  - No command lists, finds, updates or deletes a record. The only ID-based command, `GetRecordValues`, reads values.
- **Why UE5CEDumper needs it:**
  - It cannot tell whether a record is already in the table. `TeleportViewModel.PushGameThreadReminderAsync` says so
    in as many words.
  - Pressing Teleport's "Add actions" twice duplicates every record.
  - Duplicates are not only clutter. Audit #4 B26
    ([audit-2026-08-04-findings.md](audit-2026-08-04-findings.md)) found that an older duplicate's `[DISABLE]`
    freed the newer one's buffer. The workaround remembers pushes in UI memory, and that memory is empty again after
    a restart.
- **Request** (field names as AOBMaker corrected them):
  - **Every create reply carries the new ID.**
    - `id` on `CreateMemoryRecord`, `CreateAAScript` and `CreateSymbolScript`.
    - `parentId` plus `childIds` on `CreateAAScriptWithRecords`. The reply does not address these names; check them
      against `PipeMessage`'s existing JSON names first (AOBMaker eval §5).
    - **`recordIds`**, an array in node order, on `CreateRecordTreeChunk`. We asked for `ids` keyed by node id.
      That would break every older AOBMaker.UI build: `PipeMessage.Ids` is a `List<int>`, so an object there fails
      the chunk (measured).
  - **`FindRecords { description, group, valueType? }`**, plus a result limit → each match's `id`, `description`,
    `type`, `parentId` and `active`. The plugin's JSON writer cannot write an array of objects (reply §2.9), so
    expect parallel arrays.
    - The filter is `valueType`, not `type`: the plugin's JSON reader is flat, and a second `type` key takes over
      dispatch.
    - It needs a group and a result limit. Lua reads about 0.1 ms per record on CE's main thread, so a
      60,000-record table takes about 6 s, over the 5 s client timeout.
  - **`DeleteRecords { ids }`** → `deleted`. **On hold** at AOBMaker.
  - **`ifExists: "create" | "skip" | "replace"`** on every create command.
    - It matches on description, within `group` when one is given.
    - The default is `"create"`, today's behaviour.
    - `"skip"` replies with the existing ID. It comes after R7. On several matches it returns all their IDs and
      creates nothing.
    - `"replace"` is **on hold** at AOBMaker.
- **AOBMaker's answer (reply §2.5):** IDs accepted under the names above, P1; `FindRecords` scoped, P2;
  `DeleteRecords` and `"replace"` on hold, because nobody knows whether CE runs `[DISABLE]` when Lua destroys an
  active script (our B26 case), and deactivation can raise a dialog. Not started.
- **Acceptance:** push Teleport's actions twice with `ifExists:"skip"`. The table holds one copy, and `FindRecords`
  (scoped to that group) returns those IDs. The `DeleteRecords` step waits until AOBMaker lifts its hold.

## R6 — `CreateMemoryRecord` fidelity (P2, schema)

- **Today:** `CreateMemoryRecord` takes only `description`, `address`, `valueType`, `isSigned` and `showAsHex`.
  UE5CEDumper's per-field `+CE` therefore has to degrade:
  - a bit-field bool becomes the containing byte;
  - an enum or FName becomes a bare number;
  - an FString becomes an 8-byte pointer;
  - every record uses an absolute address, because that is what UE5CEDumper sends. The plugin passes `address`
    straight to `mr.Address`, and CE accepts expressions such as `[x]+off` there. That is unproven through the bridge
    (AOBMaker eval R6).
- **Request:** optional `bitStart`/`bitLength`, `dropDown`/`dropDownLink`, `length`/`unicode`/`codePage`,
  `offsets[]` and `color`. (`zeroTerminate` was declined in R2.)

  Alternatively, implement the single-call small tree that `docs/Bulk-Memory-Records-API-Design.md` planned:
  "Small trees … may use a single self-contained `CreateRecordTree { rootAddress, nodes[] }` call". Then extend
  `BulkRecordNode` as in R2.
- **AOBMaker's answer (reply §2.6):** `CreateMemoryRecord` is not extended. R2's new node fields, sent as a one-node
  `Begin` / `Chunk` / `End`, will carry bits, offsets, length and colour. P3, batch C; not started. A single-call wrapper
  comes only if the three round trips prove a real problem. Today a one-node batch already carries `offsets`, `length`
  and `unicode`.
- **Acceptance:** push one field of each kind from Live Walker. The CE record matches the one "Copy CE Field"
  produces.

## R7 — Group targeting (P2, defect / schema)

- **Today:** `group` attaches to the **first** record anywhere in the list whose description matches and whose type
  is not 11 (`pipe_server.cpp` 1194–1217). That can be a value record, not a header. `group` names one group, matched
  at any depth; a missing group is created at the root. There is no way to name a nested path.
- **Request:**
  - Match `IsGroupHeader` / `IsAddressGroupHeader` records only.
  - Add `groupPath: ["UE5CEDumper", "Teleport"]`, creating any missing level.
  - Add `color` on `CreateAAScript`.
- **AOBMaker's answer (reply §2.7):** accepted, P2, batch B; the same defect as its `K7-13`; not started. Planned order:
  1. `K7-14`: clean up a group created by a call that then fails.
  2. Plain `group` matches root-level group headers only. This changes what `group` does today; detect it through
     R1's `features`.
  3. `groupPath`.
  4. `color` on `CreateAAScript`.

  `CreateSymbolScript` and `CreateMemoryRecord` take no `group`; file a request if we need one. **Workaround until
  then:** never give a value record the same description as a group we push to.
- **Acceptance:** with a value record named "Teleport" already in the table, `group:"Teleport"` still creates or uses
  a header. `groupPath` reproduces `CheatTableBuilder`'s root → category nesting.

## R8 — Large imports and honest failure counts (P2, defect / limit)

- **Today:**
  - `ImportCheatTableXml` is one message of at most 10 MiB, JSON-escaped. UE5CEDumper's CE XML export was capped at
    60,000 entries; since 2026-10-01 its only stop is a 256 Mi-character crash guard (`CeXmlExportService.MaxEmitChars`,
    `[CEXML-CAP-60K]`), and a large export exceeds 10 MiB: DumperTest's NestedBag is 98,890 entries, 30.35 M characters.
  - When a chunk fails, CE keeps the records already made, because nothing rolls back. The reply still says
    `totalCreated: 0`: `WindowsCEPluginClient` sets `TotalCreated` only on the End success path. **Root cause, per
    AOBMaker (`EXT-5`):** the plugin's recovery `End` does report the real count; AOBMaker.UI's
    `SendRecoveryEndAsync` throws it away.
  - An oversize request gets no reply at all, on **both pipes**: the CE bridge (`pipe_server.cpp:140-141`) and the
    AOBMaker.UI pipe (`WindowsPipeServer.cs:784-788`, then `:283`), lines at `9431370`. UE5CEDumper already has a
    client-side pre-flight check open for that in [todo.md](todo.md); AOBMaker asks us to keep it.
  - Splitting one export into several `ImportCheatTableXml` calls does not help: each call builds a new tree at the
    root, and no call attaches under a node an earlier call made.
- **Request:**
  - A chunked XML upload, or a documented way to stream a larger import.
  - `totalCreated` counts what exists after a failure.
  - Both pipes answer an oversize request with `success:false` and a size message.
- **AOBMaker's answer (reply §2.8):** not started.
  - Count fix: accepted, P1 (batch A, in AOBMaker.UI). The plugin's chunk-failure reply will carry the count too
    (batch B).
  - Oversize reply: accepted, P2.
  - Streaming: later, P3. The plan is XML text in `Begin` / `Chunk` / `End` on the UI pipe, reassembled by
    AOBMaker.UI and fed to the current importer.
  - Budget: AOBMaker's largest sample table (10,264 entries) is 7.29 MB after .NET's default JSON encoder, about
    710 bytes per entry, so about 14,700 entries fit in 10 MiB.
- **Acceptance:** a 20 MiB export imports whole. A forced failure in chunk 2 reports chunk 1's count.

## R9 — `FindMostAccessed` (P2, defect / schema)

- **Today:**
  - AOBMaker itself never calls it: `FindMostAccessedAsync` has no caller, and there is no live-verification record.
  - It keeps only the most frequent hit, and discards the hit map.
  - It sets a size-4 **access** breakpoint (`debug_setBreakpoint(addr, 4, 1)`), so reads and writes are not told apart.
  - It **resets** any `durationMs` over 30,000 to 5,000, where a clamp to 30,000 was presumably meant.
  - It records `ExceptionAddress` (`plugins/CEPlugin/src/plugin.cpp` `OnDebugEvent`). x86 data breakpoints are
    trap-type, so that is normally the instruction **after** the access. CE's own "Find out what accesses"
    compensates; this does not. **Inferred from the architecture, not measured. Check it live first.**
  - It holds the single-instance bridge for the whole window.
  - Found by AOBMaker while checking this list:
    - Hit counts are unfiltered: every single-step exception in the window counts, CE's own single steps and other
      hardware breakpoints included (`EV-2`).
    - The "debugger not attached" branch never runs, because CE's breakpoint export always reports success. A failed
      attach sleeps the whole window and replies "No accesses detected" (`EV-3`). At the end the plugin also scrolls
      CE's disassembler, from the pipe thread.
  - ⚠ **It attaches a debugger without asking.** Before it sets the breakpoint, the plugin calls
    `startdebuggerifneeded(false)`, which attaches CE's debugger to the game. That matters for a game with anti-debug
    checks.
- **Request** (as AOBMaker corrected it):
  - Every hit with its count, sorted by count, as **parallel arrays**: the plugin's JSON writer cannot write an array
    of objects.
  - `kind: "access" | "write"` only. x86 debug registers have no read-only mode; "read" means two runs subtracted.
    Our `size: 1 | 2 | 4 | 8` is not answered.
  - Keep the raw trap address, and add a separate, best-effort instruction address. A blanket "previous instruction"
    fix is wrong for `call` / `jmp [mem]` / `ret` (the trap lands at the branch target) and for REP string
    instructions (RIP still points at the instruction).
  - Clamp `durationMs` instead of resetting it.
  - A shape that does not hold the worker for the whole window: Begin / Poll / End, or a background collection
    answered by a later call.
- **AOBMaker's answer (reply §2.9):** on hold until a live check, P3; the `durationMs` clamp is P2 (`EXT-6`). It is
  shipped API, so every change stays additive: `address` and `hitCount` keep their meaning. The live check uses
  AOBMaker's fixture programs. Two of its answers decide whether the command is worth extending: does the game stay
  paused after the first hit, and does a failed debugger attach really go unreported.
- **Acceptance:** on a fixture whose writer instruction is known, a write-kind run returns that instruction's own
  address as the top hit.

## R10 — A record-level hotkey at creation (P3, schema — only if proven reliable)

- **Caution first:** UE5CEDumper's `docs/lessons-learned.md` records that hotkeys created from table Lua with the
  global `createHotkey` "worked on some games' CE sessions and silently did nothing on others". The app's own OS-level
  hotkeys are its primary path by decision. User-bound CE record hotkeys remain "a fine secondary path".
- **Request:** an optional `hotkeys` field on `CreateAAScript`. It must create the same record-level hotkey that CE's
  "Set/Change hotkeys" dialog creates. Confirm the Lua call against CE's `celua.txt` first.
- **Acceptance:** a pushed Teleport record's hotkey fires in two different games' CE sessions. Only then is it worth
  wiring. AOBMaker widens it: CE 7.5, 7.6 and 7.7, keys off the numpad, and "fires once per press".
- **AOBMaker's answer (reply §2.10):** not accepted for now; on hold. No repo uses the Lua call that creates a
  record-level hotkey, and AOBMaker does not build on unused CE APIs. Our lesson is about the global `createHotkey`,
  and says nothing either way about record hotkeys.
- **Risk:** UE5DumpUI also registers Teleport's keys as OS hotkeys (`RegisterHotKey`). Nobody knows whether CE still
  sees a key that `RegisterHotKey` already took: the same key may fire twice, or never reach CE.

## R11 — Attach CE to a process id (P3, new command)

- **Why:** UE5CEDumper plans to warn when CE is attached to a different process from the game it is connected to
  (`[AOBM-ATTACH-CHECK]`, which uses today's `GetAttachedProcess`). With this, the warning could offer the fix.
- **Request:** `AttachProcess { processId }`, which calls CE's `openProcess`. The reply is `success` plus the attached
  name.
- **AOBMaker's answer (reply §2.11):** accepted, P3, batch C; not started. Compare PIDs, not names: CE gives the name
  in ANSI, and it can arrive as invalid UTF-8. Open questions: does CE show a prompt when a plugin opens a process
  (that would break the no-dialog rule), and what happens to the table's active records, whose code was injected into
  the previous process.

## R12 — Create a Structure Dissect structure (P3, new command)

- **Why it is low priority:** UE5CEDumper's `scripts/ue5_dissect.lua` already builds structures inside CE, live from
  the DLL. It uses `createStructure`, `addElement` and `addToGlobalStructureList`. `[AOBM-DISSECT-INJECT]` delivers it
  with today's `InjectTableFile`.
- **What this adds:** the one thing that script cannot do is push the UI's own `.CSX` export (Live Walker's resolved
  view) in one click.
- **Request:** `CreateStructure { csx }`, which builds and registers the structures a CSX document describes.
- **AOBMaker's answer (reply §2.12): declined as specified.** The plugin has no XML parser, and CE's process is the
  wrong place to parse untrusted XML.
  - **Workaround, with no AOBMaker change:** generate `createStructure` / `addElement` Lua from our own CSX model
    (`CsxExportService`) and push it as `{$lua}` through `CreateAAScript` with `autoActivate`. It inherits R4's gap.
    So the CSX half of `[AOBM-DISSECT-INJECT]` is now our own work.
  - If still needed later: a command that takes a validated JSON element list (P3).

## R13 — An injection AA script from an address, over the UI pipe (P3, new command)

- **Today:** AOBMaker's injection-script generator (`TemplateEngine`) is not reachable from either pipe.
  `SendDisassembly` only fills AOBMaker.UI's input box.
- **Request:** `GenerateInjectionScript { address, module?, processId?, … }` → `script`, `aob`, `injectionOffset`,
  `warnings` and the safety verdict, with the same caller check as `GenerateAob`.
  - We asked for a `template?` field. It maps to none of AOBMaker's generator options (mask mode, Mode D, inject
    name, far jump, script and feature options); the option fields are to be agreed in AOBMaker's design step.
  - The warnings and the safety verdict must come back to the client, because the generator still produces an
    unsafe script and only warns about it (AOBMaker eval R13).
- **AOBMaker's answer (reply §2.13):** accepted in principle, P3; design first; not started. `GenerateAob`'s context
  has no instructions before or after the target, and the script generator needs them. Script generation must first
  move out of AOBMaker.UI's view model.
- **Pairs with:** UE5CEDumper's `[DENKEN-WRITE-SITES]`, which would supply native write sites found statically,
  with no debugger.

## R14 — Find the instructions that reference an address (P3, new command)

- **Why:** when UE5CEDumper resolves GWorld through an exported symbol (Satisfactory), there is no referencing
  instruction to seed `GenerateAob`.
- **Request:** `FindReferences { module, address, max? }` → the RIP-relative instructions in that module that address
  it.
- **Alternative:** the same search could be built in UE5CEDumper's Genau instead. `[AOBM-EXPORT-GWORLD-AOB]` records
  both routes.
- **AOBMaker's answer (reply §2.14):** accepted, **P2**, batch A (C# only; it reuses AOBMaker's `RipReferenceScanner`
  from its RIP Scan window); not started. It goes behind the `GenerateAob` caller check.
  - It finds RIP-relative references **inside the scanned module only**. Other modules reach an exported global
    through their import table, so for an exported GWorld only the defining module's own references show. Whether
    that is enough for Satisfactory needs a live check.
  - A large module can hold about 700,000 references, so the reply carries a cap, a truncated flag and totals.
  - It takes seconds, and the UI pipe serves one request at a time: use a long read timeout.
  - Chain on x64: `FindReferences` → `GenerateAob` (returns `pos` / `aoblen`) → `CreateSymbolScript`. An x86
    absolute-address hit gets no `pos` / `aoblen`.

## R15 — `customType` on records (P3, schema)

- **Why:** UE5CEDumper's todo wants FName shown live through a "UE FName to String" custom type, under
  "CE export drilldown — remaining gaps". Registering the type can already ride a `{$lua}` `CreateAAScript`.
  Setting it on records cannot: `BulkRecordNode` and `CreateMemoryRecord` have no such field, and the importer drops
  `<CustomType>`.
- **Request:** `customType` on `BulkRecordNode`, `CreateMemoryRecord` and the importer.
- **AOBMaker's answer (reply §2.15, `EXT-15`):** accepted after a CE-source check, P3; not started. Two warnings for
  any client:
  - Reading `NumericalValue` from a record whose custom type is not registered yet crashes CE with an access
    violation, and `pcall` cannot catch it.
  - `showAsHex` on a custom-type record silently breaks `Value` in both directions. Never push `showAsHex` together
    with a custom type.

## R16 — Protocol hygiene and doc drift (P2, defects / docs)

Each item was found by reading the source, and items (a)–(f) were re-checked by hand. Any one of them costs an
external client a debugging session.

**AOBMaker's answer (reply §2.16):** accepted item by item: (a) P1, (b) P2, (c) P3, (d) P3, (e) P1, (f) docs only,
(g) docs fixed and the TTL refresh P2. Until each ships, its reply §3 rules 1–7 are the contract; the notes below
say which part is already fixed.

- **(a) Whitespace in array keys.** `pipe_server.cpp` finds `"nodes":[`, `"offsets":[`, `"records":[` and `"ids":[`
  by literal search (1703, 1745, 2266, 2318, 2377, 2537).
  - With `"nodes": [`, a chunk replies success with `created:0`.
  - A spaced `offsets` or `records` key silently drops the offsets or the child records.

  `docs/API-CEPlugin.md` said whitespace is fine. **Fixed in the doc** (AOBMaker `e75562d`, `API-CEPlugin.md:41-52`):
  whitespace after the colon is fine for string, int and bool values on plugins built 2026-09-08 or later; after the
  colon of an array key, or before any colon, it still breaks. The code fix is open (`EXT-7`, P1).
- **(b) Key order.** `JsonParse::ExtractString` returns the first string-valued `"type"` in the document. So the
  envelope `type` must come before any node's `type`, or the request dispatches as, say, `"Float"`.
  - A key-sorting serializer (Go maps, Python `sort_keys`) breaks it.
  - The same shape, found by AOBMaker: `records[]` placed before the envelope gives the parent record the first
    child's `description`, and the self-check still passes (`EXT-8`, P2).
- **(c) Numeric types.** A bulk node's `type` is read as a string only, so `"type": 2` is ignored and the record gets
  CE's default type. `docs/Bulk-Records-Protocol-Spec.md` used to allow "a bare numeric CE vt". **Fixed in the doc**
  (`e75562d`): a numeric vt must be sent as a string (spec §5; `API-CEPlugin.md:211-213`). The plan is to accept both
  (`EXT-9`, P3).
- **(d) `"parent": null`.** It counts as present (`o.find("\"parent\":")`). The node then attaches under node 0 **only
  when a node with id 0 exists** in the batch; otherwise it stays at the root (`EXT-10`, partly true as filed). The
  spec says to omit the key; a null should mean "root" (planned, P3).
- **(e) The UI pipe's replies.**
  - A failure reply omits `success`, because `PipeMessage.Success` is `JsonIgnore(WhenWritingDefault)`.
  - An unknown `type` gets no reply at all; `WindowsPipeServer` only logs it.
  - Found by AOBMaker: malformed JSON, a length mismatch and a JSON `null` body get no reply either (`EXT-11`).

  Always write `success`, and answer an unknown type with an error, as the CE bridge already does. AOBMaker's plan
  (P1): every reply carries `success`, and an unknown type gets `type:"Error"`, with the echoed type cut to 64
  characters.
- **(f) `color` format.** `CreateAAScriptWithRecords` writes `child.Color = 0x<hex>` raw (`pipe_server.cpp` 1914). CE
  reads that as a BGR TColor. `docs/API-CEPlugin.md` and `PipeMessage` said "RRGGBB". **Fixed in the docs**
  (`e75562d`): both now say BGR `TColor` (`API-CEPlugin.md:186-187`). The plugin will not convert, because that would
  change every colour an existing client has pushed (`EXT-12`).
- **(g) Doc drift, reported by the research pass and checked in code.**
  - ✅ `maxBackwardBytes` omitted means a 256-byte default capped at 4096, not "uncapped". **Fixed in the doc**
    (`e75562d`, `EXT-13`): `API-CEPlugin.md:82-84` now gives the 256 default, the 4096 clamp and the 512 count clamp.
  - ✅ A second `CreateRecordTreeEnd` on a finished batch replies `success:true, totalCreated:0`, not the failure
    the spec described. **Settled in the doc:** the spec now documents that reply, and it stays
    (`Bulk-Records-Protocol-Spec.md:44-47`); a failure reply would break AOBMaker's own recovery path.
  - A batch's timestamp is set only at `Begin`. So another client's `Begin` more than 10 minutes into a long import
    sweeps the live batch. **Still open:** AOBMaker plans a per-chunk refresh (batch B, `EXT-14`). Until then, keep
    each import under 10 minutes from its `Begin`, and send `End` once.

## R17 — `CreateSymbolScript` with an offset (P3, schema)

- **Why:** found while implementing Eval A8 (UE5CEDumper `90c732e5`). Nine of UE5CEDumper's signatures (eight
  GObjects, one GNames) resolve to the RIP target PLUS a constant (−0x14 to +0x0C), because the instruction
  addresses a member of the structure, not its start. `CreateSymbolScript` registers `aob + aoblen + disp` and
  nothing else, so for those games UE5CEDumper refuses to register `gobjects_addr` / `gnames_addr` rather than
  register a wrong one.
- **Request:** an optional `offset` (signed integer, default 0) added to the resolved address before
  `registersymbol`. Omitted, the script is byte-identical to today's.
- **Alternative:** UE5CEDumper can emit its own symbol script through `CreateAAScript`. It would then duplicate the
  plugin's generator, including its module-name fallback, which is why this is filed.
- **Not in AOBMaker's reply.** The reply answers R1–R16 as filed at our `e070c166`; R17 came later, in `217ecf70`.
  It touches the same template R4 will change: the plugin's `BuildSymbolScanScript` and its C# twin
  `SymbolScannerScriptGenerator`, which AOBMaker's `SymbolScanTemplateTwinTests` pins together.

## Not requested

- **More pipe instances.** AOBMaker measured it and rejected it: there is only one worker, so a second instance would
  just queue. UE5CEDumper already reports a busy pipe as busy (`[W1-PIPEBUSY-STATUS]`).
- **Arbitrary Lua execution.** It is reachable today through `CreateAAScript` + `{$lua}` + `autoActivate`, and a
  dedicated command would widen an unauthenticated pipe for no feature this list needs.
