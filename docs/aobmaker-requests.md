# Requests to AOBMaker, from UE5CEDumper

**Filed 2026-09-29 against AOBMaker `dev` at `2528712`.** The work belongs to the AOBMaker repository. The list
lives here because UE5CEDumper is the client that needs it. The evaluation behind every item is
[aobmaker-integration-eval.md](aobmaker-integration-eval.md). The UE5CEDumper rows each request unblocks are in
[todo.md](todo.md) under `[AOBMAKER-EVAL-2026-09-29]`.

**When a request ships:**
1. Write the AOBMaker commit into its row in the summary table.
2. Flip the UE5CEDumper todo row it unblocks.

AOBMaker file paths below are relative to the AOBMaker repository, and line numbers are pinned to `2528712`.
Everything was read from source; nothing was run. Where a claim is inferred, it says so.

## Ground rules for every request

- **Backward compatible.** Add optional request fields and new reply fields; never change the meaning of an
  existing one. UE5CEDumper keeps talking to older plugins.
- **Detectable (R1).** Today an older plugin **silently ignores** a field it does not know. That is how `group` and
  `showAsHex` rolled out (see [aobmaker-integration.md](aobmaker-integration.md)), and a client cannot tell
  "applied" from "ignored". Every new field below should appear in R1's capability reply.
- **No new modal dialogs.** The bridge has one worker and no handler timeout, so a dialog blocks every client. A
  request that makes CE do more work must not add a path that can raise one.

## Summary

| ID | Pri | Kind | Request | Unblocks in UE5CEDumper | Shipped in |
|---|---|---|---|---|---|
| R1 | P1 | new command | Capability discovery on the CE bridge | every row below — feature gating | |
| R2 | P1 | defect / schema | Import keeps bit fields, dropdowns, colours, string flags; reports what it drops | `[AOBM-CEXML-PUSH]` | |
| R3 | P1 | defect | An address-less `GroupHeader` imports as a plain folder | `[AOBM-CT-PUSH]` | |
| R4 | P1 | defect | `autoActivate` reports whether CE actually enabled the script | `[AOBM-ACTIVATE-RESULT]` | |
| R5 | P1 | new commands | Record IDs in replies; find, delete, and skip-or-replace on create | `[AOBM-DEDUP-TABLE]` | |
| R6 | P2 | schema | `CreateMemoryRecord` carries bits, dropdowns, string flags, offsets, colour | `[AOBM-PLUSCE-FIDELITY]` | |
| R7 | P2 | defect / schema | `group` matches headers only; nested group paths; colour on AA scripts | `[AOBM-CT-PUSH]` | |
| R8 | P2 | defect / limit | Imports over 10 MiB; honest partial-failure counts; a reply to oversize requests | `[AOBM-CEXML-PUSH]` | |
| R9 | P2 | defect / schema | `FindMostAccessed`: every hit, read vs write, size, the right instruction, no long hold | `[AOBM-FIND-ACCESS]` | |
| R10 | P3 | schema | A record-level hotkey at creation — only if proven reliable | `[AOBM-RECORD-HOTKEYS]` | |
| R11 | P3 | new command | Attach CE to a process id | `[AOBM-ATTACH-CHECK]` (its one-click fix) | |
| R12 | P3 | new command | Create a Structure Dissect structure from CSX XML | `[AOBM-DISSECT-INJECT]` (the CSX half) | |
| R13 | P3 | new command | An injection AA script from an address, over the UI pipe | `[DENKEN-WRITE-SITES]` | |
| R14 | P3 | new command | Find the instructions that reference an address | `[AOBM-EXPORT-GWORLD-AOB]` | |
| R15 | P3 | schema | `customType` on records | `[AOBM-CUSTOMTYPE]` | |
| R16 | P2 | defects / docs | Protocol hygiene: whitespace, key order, number types, nulls, missing replies, doc drift | any client of the bridge | |
| R17 | P3 | schema | `CreateSymbolScript` adds a constant after the RIP resolution | `[AOBM-GWORLD-GENAOB]` (adjusted signatures) | |

---

## R1 — Capability discovery (P1, new command)

- **Why:** UE5CEDumper cannot tell which plugin build it is talking to.
  - The UI pipe answers `Ping` with `version: "2.0"`. The CE bridge has no equivalent at all.
  - Unknown request fields are ignored without a trace. So "the plugin does not support `group`" and "the group
    was applied" look identical to the client.
- **Request:** `{ "type": "GetCapabilities" }`.
- **Reply:** `{ "type": "GetCapabilitiesResult", "success": true, "protocolVersion": "2.x", "pluginBuild": "<commit or date>",
  "commands": [ … ], "fields": { "CreateAAScript": ["description", "script", "autoActivate", "group", …], … } }`.
- **Compatibility:** an older plugin answers `Unknown type`. The client reads that as "baseline 2.0, fields unknown".
- **Acceptance:** the reply lists every dispatched command, and each new field from R2–R15 once it ships.

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
  | `<CodePage>`, `<ZeroTerminate>` | String display |
- **Request:**
  - Add these fields to `BulkRecordNode`, the importer, and the plugin's node Lua:
    - `bitStart` and `bitLength`;
    - `dropDown`, with `list`, `displayValueAsItem`, `readOnly` and `descriptionOnly`;
    - `dropDownLink`;
    - `color`, as CE's own TColor value (see R16);
    - `codePage` and `zeroTerminate`.
  - Add `dropped: { "<Element>": count }` to the `ImportCheatTableXml` reply, so nothing is lost silently.
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
  - AOBMaker's `samples/cheat_tables` hold **573** address-less non-script entries across 19 tables. That count
    comes from parsing those files, 2026-09-29.
  - UE5CEDumper's `CheatTableBuilder` writes its root and category folders that way too, so every generated `.CT` is
    rejected whole.
- **Request:** a `GroupHeader` entry without `<Address>` becomes an `IsGroupHeader` folder. So does a bulk node with
  `group: true` and an empty `addr`. Keep today's behaviour for a header that does carry an address.
- **Acceptance:** Interesting Properties' "Generate CT" output imports with the same tree as CE's File → Load of
  that `.CT`.

## R4 — Report whether `autoActivate` enabled the script (P1, defect)

- **Today:**
  - `plugins/CEPlugin/src/pipe_server.cpp` 1262–1265 (`CreateAAScript`) runs `if ok then mr.Active = true end` and
    returns `ok`. `ok` was computed **before** activation, and `Active` is never read back.
  - `CreateSymbolScript` has the same shape (1579–1582). A script CE failed to enable still replies `success:true`.
- **Risk:** activation runs on CE's main thread while the bridge's single worker waits. AOBMaker's own live test saw
  an `Active=true` enable raise CE's "Nearby allocation error" Yes/No dialog, and its driver stopped there
  (`docs/Live-Verification-Multi-Apply.md`).
- **Request:**
  - After setting `Active`, read it back and reply with `activated: true|false` next to `success`. `success` keeps its
    meaning: the record was created.
  - Before activating, run CE's `autoAssembleCheck` on the script. AOBMaker already uses it offline, in
    `docs/Bug-Leak-Audit-Control-Table.md` §6.3c. On failure, do not activate, and return its message as `error`.
    This keeps a syntax error from reaching CE's error dialog. It cannot catch a runtime failure such as the
    allocation error above.
- **Consumers in UE5CEDumper:**
  - the Pointer panel's Register GWorld and Register &GEngine symbol buttons;
  - the standalone trainer's Setup row.

  Both use `autoActivate:true`, and today both report success whatever happened.
- **Acceptance:** send a script with a deliberate syntax error and `autoActivate:true`. The reply is
  `success:true, activated:false, error:"…"`, and no dialog appears.

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
- **Request:**
  - **Every create reply carries the new ID.**
    - `id` on `CreateMemoryRecord`, `CreateAAScript` and `CreateSymbolScript`.
    - `parentId` plus `childIds` on `CreateAAScriptWithRecords`.
    - `ids`, keyed by node id, on `CreateRecordTreeChunk`.
  - **`FindRecords { description, group?, type? }`** → `records: [{ id, description, type, parentId, active }]`.
  - **`DeleteRecords { ids }`** → `deleted`.
  - **`ifExists: "create" | "skip" | "replace"`** on every create command.
    - It matches on description, within `group` when one is given.
    - The default is `"create"`, today's behaviour.
    - `"skip"` replies with the existing ID.
- **Acceptance:** push Teleport's actions twice with `ifExists:"skip"`. The table holds one copy.
  `FindRecords` returns those IDs, and `DeleteRecords` removes them.

## R6 — `CreateMemoryRecord` fidelity (P2, schema)

- **Today:** `CreateMemoryRecord` takes only `description`, `address`, `valueType`, `isSigned` and `showAsHex`.
  UE5CEDumper's per-field `+CE` therefore has to degrade:
  - a bit-field bool becomes the containing byte;
  - an enum or FName becomes a bare number;
  - an FString becomes an 8-byte pointer;
  - every record uses an absolute address.
- **Request:** optional `bitStart`/`bitLength`, `dropDown`/`dropDownLink`, `length`/`unicode`/`codePage`/`zeroTerminate`,
  `offsets[]` and `color`.

  Alternatively, implement the single-call small tree that `docs/Bulk-Memory-Records-API-Design.md` planned:
  "Small trees … may use a single self-contained `CreateRecordTree { rootAddress, nodes[] }` call". Then extend
  `BulkRecordNode` as in R2.
- **Acceptance:** push one field of each kind from Live Walker. The CE record matches the one "Copy CE Field"
  produces.

## R7 — Group targeting (P2, defect / schema)

- **Today:** `group` attaches to the **first** record anywhere in the list whose description matches and whose type
  is not 11 (`pipe_server.cpp` 1194–1217). That can be a value record, not a header. Only one level is supported.
- **Request:**
  - Match `IsGroupHeader` / `IsAddressGroupHeader` records only.
  - Add `groupPath: ["UE5CEDumper", "Teleport"]`, creating any missing level.
  - Add `color` on `CreateAAScript`.
- **Acceptance:** with a value record named "Teleport" already in the table, `group:"Teleport"` still creates or uses
  a header. `groupPath` reproduces `CheatTableBuilder`'s root → category nesting.

## R8 — Large imports and honest failure counts (P2, defect / limit)

- **Today:**
  - `ImportCheatTableXml` is one message of at most 10 MiB, JSON-escaped. UE5CEDumper's CE XML export is capped at
    60,000 entries (`CeXmlExportService.MaxEmitEntries`), and a large export can exceed 10 MiB.
  - When a chunk fails, CE keeps the records already made, because nothing rolls back. The reply still says
    `totalCreated: 0`: `WindowsCEPluginClient` sets `TotalCreated` only on the End success path.
  - An oversize request on the CE bridge gets no reply at all. UE5CEDumper already has a client-side pre-flight check
    open for that in [todo.md](todo.md).
- **Request:**
  - A chunked XML upload, or a documented way to stream a larger import.
  - `totalCreated` counts what exists after a failure.
  - Both pipes answer an oversize request with `success:false` and a size message.
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
- **Request:**
  - `hits: [{ address, count }]`, sorted by count.
  - `kind: "access" | "write"`, and `size: 1 | 2 | 4 | 8`.
  - The previous-instruction correction, if the live check confirms it is needed.
  - Clamp `durationMs` instead of resetting it.
  - A shape that does not hold the worker for the whole window: Begin / Poll / End, or a background collection
    answered by a later call.
- **Acceptance:** on a fixture whose writer instruction is known, a write-kind run returns that instruction's own
  address as the top hit.

## R10 — A record-level hotkey at creation (P3, schema — only if proven reliable)

- **Caution first:** UE5CEDumper's `docs/lessons-learned.md` records that hotkeys created from table Lua with the
  global `createHotkey` "worked on some games' CE sessions and silently did nothing on others". The app's own OS-level
  hotkeys are its primary path by decision. User-bound CE record hotkeys remain "a fine secondary path".
- **Request:** an optional `hotkeys` field on `CreateAAScript`. It must create the same record-level hotkey that CE's
  "Set/Change hotkeys" dialog creates. Confirm the Lua call against CE's `celua.txt` first.
- **Acceptance:** a pushed Teleport record's hotkey fires in two different games' CE sessions. Only then is it worth
  wiring.

## R11 — Attach CE to a process id (P3, new command)

- **Why:** UE5CEDumper plans to warn when CE is attached to a different process from the game it is connected to
  (`[AOBM-ATTACH-CHECK]`, which uses today's `GetAttachedProcess`). With this, the warning could offer the fix.
- **Request:** `AttachProcess { processId }`, which calls CE's `openProcess`. The reply is `success` plus the attached
  name.

## R12 — Create a Structure Dissect structure (P3, new command)

- **Why it is low priority:** UE5CEDumper's `scripts/ue5_dissect.lua` already builds structures inside CE, live from
  the DLL. It uses `createStructure`, `addElement` and `addToGlobalStructureList`. `[AOBM-DISSECT-INJECT]` delivers it
  with today's `InjectTableFile`.
- **What this adds:** the one thing that script cannot do is push the UI's own `.CSX` export (Live Walker's resolved
  view) in one click.
- **Request:** `CreateStructure { csx }`, which builds and registers the structures a CSX document describes.

## R13 — An injection AA script from an address, over the UI pipe (P3, new command)

- **Today:** AOBMaker's injection-script generator (`TemplateEngine`) is not reachable from either pipe.
  `SendDisassembly` only fills AOBMaker.UI's input box.
- **Request:** `GenerateInjectionScript { address, module?, processId?, template? }` → `script`, `aob`,
  `injectionOffset`, with the same caller check as `GenerateAob`.
- **Pairs with:** UE5CEDumper's `[DENKEN-WRITE-SITES]`, which would supply native write sites found statically,
  with no debugger.

## R14 — Find the instructions that reference an address (P3, new command)

- **Why:** when UE5CEDumper resolves GWorld through an exported symbol (Satisfactory), there is no referencing
  instruction to seed `GenerateAob`.
- **Request:** `FindReferences { module, address, max? }` → the RIP-relative instructions in that module that address
  it.
- **Alternative:** the same search could be built in UE5CEDumper's Genau instead. `[AOBM-EXPORT-GWORLD-AOB]` records
  both routes.

## R15 — `customType` on records (P3, schema)

- **Why:** UE5CEDumper's todo wants FName shown live through a "UE FName to String" custom type, under
  "CE export drilldown — remaining gaps". Registering the type can already ride a `{$lua}` `CreateAAScript`.
  Setting it on records cannot: `BulkRecordNode` and `CreateMemoryRecord` have no such field, and the importer drops
  `<CustomType>`.
- **Request:** `customType` on `BulkRecordNode`, `CreateMemoryRecord` and the importer.

## R16 — Protocol hygiene and doc drift (P2, defects / docs)

Each item was found by reading the source, and items (a)–(f) were re-checked by hand. Any one of them costs an
external client a debugging session.

- **(a) Whitespace in array keys.** `pipe_server.cpp` finds `"nodes":[`, `"offsets":[`, `"records":[` and `"ids":[`
  by literal search (1703, 1745, 2266, 2318, 2377, 2537).
  - With `"nodes": [`, a chunk replies success with `created:0`.
  - A spaced `offsets` or `records` key silently drops the offsets or the child records.

  `docs/API-CEPlugin.md` says whitespace is fine, which is true only for scalar fields. Tolerate it, or correct the
  doc.
- **(b) Key order.** `JsonParse::ExtractString` returns the first string-valued `"type"` in the document. So the
  envelope `type` must come before any node's `type`, or the request dispatches as, say, `"Float"`.
- **(c) Numeric types.** A bulk node's `type` is read as a string only, so `"type": 2` is ignored and the record gets
  CE's default type. `docs/Bulk-Records-Protocol-Spec.md` allows "a bare numeric CE vt".
- **(d) `"parent": null`.** It counts as present (`o.find("\"parent\":")`) and attaches the node under node 0. The
  spec says to omit the key; a null should mean "root".
- **(e) The UI pipe's replies.**
  - A failure reply omits `success`, because `PipeMessage.Success` is `JsonIgnore(WhenWritingDefault)`.
  - An unknown `type` gets no reply at all; `WindowsPipeServer` only logs it.

  Always write `success`, and answer an unknown type with an error, as the CE bridge already does.
- **(f) `color` format.** `CreateAAScriptWithRecords` writes `child.Color = 0x<hex>` raw (`pipe_server.cpp` 1914). CE
  reads that as a BGR TColor. `docs/API-CEPlugin.md` and `PipeMessage` say "RRGGBB". Fix the doc, or convert.
- **(g) Doc drift, reported by the research pass and checked in code.**
  - `maxBackwardBytes` omitted means a 256-byte default capped at 4096, not "uncapped" as `docs/API-CEPlugin.md`
    says.
  - A second `CreateRecordTreeEnd` on a finished batch replies `success:true, totalCreated:0`, not the failure
    `docs/Bulk-Records-Protocol-Spec.md` describes.
  - A batch's timestamp is set only at `Begin`. So another client's `Begin` more than 10 minutes into a long import
    sweeps the live batch.

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

## Not requested

- **More pipe instances.** AOBMaker measured it and rejected it: there is only one worker, so a second instance would
  just queue. UE5CEDumper already reports a busy pipe as busy (`[W1-PIPEBUSY-STATUS]`).
- **Arbitrary Lua execution.** It is reachable today through `CreateAAScript` + `{$lua}` + `autoActivate`, and a
  dedicated command would widen an unauthenticated pipe for no feature this list needs.
