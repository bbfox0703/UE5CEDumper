# AOBMaker — what else UE5CEDumper could hand to it (evaluation)

**Status: EVALUATED 2026-09-29. Section A implemented the same day in `90c732e5`** (UI only). Built and published
AOT-trimmed on Windows as build 3599 (`21687f7c`); not yet checked on a running game —
[verification-register.md](verification-register.md) `[AOBMAKER-A1-A9-LIVE]`. Each A item's **Done** line says what
shipped and where it differs from the plan below. The evaluation itself was code reading only.
**AOBMaker replied in `9431370`** (its `docs/UE5CEDumper-Requests-Reply.md`). The B and C items below carry its
verdicts; [aobmaker-requests.md](aobmaker-requests.md) has them per request. **AOBMaker release `v20260930` (build 157)** ships
R3, R4, R15 and most of R16, and parts of R1, R2, R7, R8, R9; the per-request status is in aobmaker-requests.md,
and the B rows it unblocks are flipped in [todo.md](todo.md).
Baseline: UE5CEDumper `dev` at build 3598, AOBMaker `dev` at `2528712`. AOBMaker line numbers are
pinned to that commit, except in the notes taken from AOBMaker's answer, which cite `9431370`.
UE5CEDumper sites are named by `Type.Method`, which survives edits.

The question was where UE5CEDumper can still use AOBMaker. It covers three kinds of place:
- places that copy to the clipboard and never try AOBMaker;
- places that do not use AOBMaker and need no clipboard fallback;
- places AOBMaker cannot serve yet.

This document sorts every candidate by **what AOBMaker can do today**:

- **A — fully, with AOBMaker as it is.** UE5CEDumper-only work.
- **B — yes, but something is lost.** Usable now; faithful only after an AOBMaker revision.
- **C — no.** Needs a new AOBMaker interface.

Each item's **Today** line keeps the original question's split. The values are:
- `clipboard only`;
- `file only`;
- `not wired` (neither AOBMaker nor the clipboard);
- `AOBMaker, lossy`.

Related documents:
- [aobmaker-integration.md](aobmaker-integration.md) lists the commands in use and their UI entry points: seven on the
  CE bridge (`GetAttachedProcess` joined the first six in `90c732e5`), and `GenerateAob` on AOBMaker.UI's own pipe.
- [working-lessons.md](working-lessons.md) §6 holds the one related settled decision: *"Hierarchical Copy CE XML direct-push to CE — DEFERRED, not refused"*. B1 is about that item.
- **Follow-ups.** Every item is a row in [todo.md](todo.md) under `[AOBMAKER-EVAL-2026-09-29]`. What AOBMaker would
  have to change is filed as R1–R17 in [aobmaker-requests.md](aobmaker-requests.md). The B and C items below name
  their request.

---

## 0. The two pipes, and what we use

| Pipe | Hosted by | Commands | We use |
|---|---|---|---|
| `\\.\pipe\AOBMakerCEBridge` | the CE plugin, inside Cheat Engine | 15, listed after this table | the first six; since `90c732e5` also `GetAttachedProcess` |
| `\\.\pipe\AOBMaker` | AOBMaker.UI, a separate app | 5, listed after this table | none at evaluation time; since `90c732e5`, `GenerateAob` |

The 15 CE-bridge commands, in the order the "We use" column counts them:
- `NavigateHexView`, `NavigateDisassembler`, `CreateMemoryRecord`, `CreateAAScript`, `CreateSymbolScript`, `InjectTableFile`;
- `CreateAAScriptWithRecords`;
- `CreateRecordTreeBegin`, `CreateRecordTreeChunk`, `CreateRecordTreeEnd`;
- `FindMostAccessed`, `GetAttachedProcess`, `GetActiveScripts`, `GetRecordValues`, `DisassembleRange`.

The five AOBMaker.UI commands: `GenerateAob`, `ImportCheatTableXml`, `SendDisassembly`, `GetRelocationSelection`, `Ping`.

Sources: the bridge dispatches in the `if (type == …)` chain of `PipeServer::HandleClient`, in
`plugins/CEPlugin/src/pipe_server.cpp`. The UI-pipe dispatcher is in
`src/AOBMaker.Platform.Windows/Services/WindowsPipeServer.cs`. Both are AOBMaker paths.

A comment above that chain states that every bridge command is published third-party API: AOBMaker's release
ships `docs/API-CEPlugin.md`. So commands AOBMaker.UI never sends itself, such as `FindMostAccessed`, are still
meant for clients like us.

Four facts about the second pipe decide what is feasible:

- AOBMaker.UI must be running, **in addition to** CE with the plugin loaded.
- `GenerateAob` and `ImportCheatTableXml` accept a caller only if it is the same user at the same or a higher integrity level.
  An elevated AOBMaker therefore rejects an unelevated UE5DumpUI.
- A failure reply **omits** `success` (`PipeMessage.Success` is `JsonIgnore(WhenWritingDefault)`). A `Rejected:`
  reply omits it too, and `Pong` never carries it: detect `Pong` by its `type`.
- An unknown `type` gets **no reply at all**: the dispatcher only logs it. Neither do malformed JSON, a length
  mismatch, a JSON `null` body or an oversize request (AOBMaker `EXT-11`, `EXT-5`).
  A client must treat a missing field as false, and must bound every read with a timeout.

Both pipes are single-instance. The bridge is `CreateNamedPipeA` with max instances 1, it has one worker thread,
and it has no timeout on handler work. We already say "busy" when the pipe is held ([W1-PIPEBUSY-STATUS]).
Anything that holds the bridge longer blocks every other push, AOBMaker's own included. Two such cases are
below: B5, and a CE modal dialog (B4).

---

## A. Fully doable with AOBMaker as it is

UE5CEDumper-only work: every command named here exists and carries everything the item needs.
All nine shipped in `90c732e5`; the new buttons share one `Helpers/AobMakerStatus` (probed on tab switch, repainted
by the toolbar ⟳) and one set of actions, `Helpers/AobMakerActions`.

### A1 — Instance Finder "AA" pushes like Live Walker's
- **Today:** clipboard only.
  `InstanceFinderViewModel.GenerateCeAAScriptAsync` copies the output of `GenerateRegisterSymbolXml`.
- **Command:** `CreateAAScript` with `autoActivate:false`, keeping the clipboard fallback.
- **Template:** `LiveWalkerViewModel.GenerateCeAAScriptAsync` already does exactly this.
  It calls `ExtractAssemblerScript`, pushes the script, and copies the wrapped XML to the clipboard if the push fails.
- **Effort:** S.
- **Done** (`90c732e5`): as planned, through `AobMakerActions.PushAaScriptOrCopyAsync`. The record arrives unticked.

### A2 — "+CE" and "HEX" on value-result rows
- **Today:** clipboard only. The copy commands are:
  - `ValueSearchViewModel.CopyAddressAsync` and `CopyGroupSlotAddressAsync`;
  - `SnapshotViewModel.CopyDiffAddressAsync`;
  - `CopyGroupSlotAddressAsync` in the `SnapshotViewModel.Group` and `SpcQueryViewModel.Group` partials;
  - `SpcQueryViewModel.CopyAddressAsync`.

  Snapshot's and SPC's own doc comments call the copy "a quick handoff into CE".
- **Commands:** `CreateMemoryRecord` (typed) and `NavigateHexView`. Keep the Copy button.
- **Type mapping:** the rows already carry the type.
  - Snapshot and SPC rows have `DeclaredType`.
  - Value Search rows and group slots have `FieldType` plus `BoolFieldMask`.

  Map it through the UE→CE mapping that `CeXmlExportService` already owns. That needs a type-string entry point
  beside `MapFieldToCeRecordType(LiveFieldValue)`. It must also follow the enum element-size rule that
  `MapInnerTypeToCeField` documents.
- **Fidelity:** the same as Live Walker's +CE today. What +CE cannot express is B3.
- **Effort:** S–M.
- **Done** (`90c732e5`): HEX and +CE on all five, with the Copy button kept. The type entry point is
  `CeXmlExportService.MapTypeNameToCeRecordType`; an enum of unknown width goes to CE as one byte. Two refusals the
  plan did not foresee:
  - a snapshot row that names an array element (`Inventory[3]`, `Inventory[3].Count`) carries its OWNER's base, not
    an address of its own, so it is refused with that reason;
  - a Value Search group slot is sent only when the live decoder gave it a leaf address (`HasLeafAddress`).

  Snapshot and SPC use the same session gate as their Copy buttons.

### A3 — Instance Finder: +CE and HEX on fields, instances and container owners
- **Today:** clipboard only, and the panel has no AOBMaker path at all.
  The copy commands are `CopyFieldAddressAsync`, `CopyInstanceAddressAsync` and `CopyContainerAddressAsync`.
- **Why it is cheap:** fields are `LiveFieldValue`, so Live Walker's `MapFieldToCeRecordType` and
  `LiveFieldValue.PayloadAddress` apply unchanged. That includes the delegate-pad rule, [A4-PUSHCE-UNPADDED].
- **Effort:** S.
- **Done** (`90c732e5`): HEX and +CE on fields. Instances and container owners get HEX only: a +CE record of an
  object's base would show its vtable pointer.

### A4 — HEX on object-address rows
- **Today:** clipboard only.
  The copy commands are `ObjectTreeViewModel.CopyAddressAsync`, `ClassPivotViewModel.CopyAddressAsync` and
  `RelatedObjectsViewModel.CopyAddress`.
- **Command:** `NavigateHexView`. A +CE for an object base adds little.
- **Effort:** S.
- **Done** (`90c732e5`): Object Tree's context menu, Class Pivot's toolbar (same session gate as its Copy), and a
  per-row button in Related Objects.

### A5 — Pointer panel: the two missing ASM buttons
- **Today:** clipboard only for one row, nothing for the other.
  - The FSparseDelegateStorage row copies its scan hit but has no ASM button.
  - &GEngine has no scan-hit button at all, although `EngineState.GEngineScanAddr` is published.
- **Command:** `NavigateDisassembler`, exactly as on the GObjects, GNames and GWorld rows.
- **Effort:** XS.
- **Done** (`90c732e5`). **Correction:** the &GEngine scan-hit row already existed and showed the address; what it
  lacked was the buttons. It now has ASM and Copy, and the FSparseDelegateStorage row has ASM.

### A6 — Check which process CE is attached to before pushing
- **Today:** not wired.
- **Command:** `GetAttachedProcess` returns CE's `processId` and `processName`. Compare them with `EngineState.ProcessId`.
- **Why:** a push into a CE that is attached to nothing, or to another process, fails or lands in the wrong table,
  and no message says why.
  - [verification-register.md](verification-register.md) records three clicks that produced nothing for exactly this reason: CE had no process attached.
  - [todo.md](todo.md) CEB-2 closed "the bridge does not establish WHICH Cheat Engine it reached" as *"an enhancement request, not a defect"*. This item is that enhancement.
- **Fallback:** none. The result is a warning in the status line.
- **Effort:** S. It needs a seventh `IAobMakerBridge` method, one round trip. C4 would let the same check also fix the mismatch.
- **Done** (`90c732e5`), as a warning that never refuses: `IAobMakerBridge.GetAttachedProcessAsync` (a default
  interface method, so older doubles answer "cannot tell"). The toolbar shows "⚠ CE is not on this game" at connect
  and on ⟳, and a +CE that CE refuses appends the reason. It is not asked before every push: that would cost a
  round trip on each, and a push into a CE with nothing open is sometimes right (the DLL bootstrap).

### A7 — "Disassemble in CE" for native UFunctions everywhere
- **Today:** not wired, except in one dialog.
  `PropertyXrefDialog.OnDisassembleClicked` has it: `GetFunctionCodeAddrAsync`, then a ByteArray record, then `NavigateDisassembler`.
  These have none: Live Walker's function rows, Interesting Functions, Live Funcs, and `FunctionPropsDialog`.
  `FunctionPropsDialog` is the one that lists the fields a native function touches.
- **Done** (`90c732e5`): an ASM button on all four, through the shared `AobMakerActions.DisassembleFunctionAsync`.
  `PropertyXrefDialog` keeps its own copy, because it colours each outcome.
- **Bonus:** once CE shows the code, the plugin's own **Send to AOBMaker** menu item (Ctrl+Shift+A, which sends `SendDisassembly`)
  moves the selection into AOBMaker.UI. So "UE function → AOB or AA script" works with no new command.
  `SendDisassembly`'s `selectedAddress` is only logged, so the user still picks the injection point in AOBMaker
  (`API-AOBMaker-UI.md:65`, AOBMaker `EV-5`).
- **Effort:** S.

### A8 — GObjects and GNames symbols through `GenerateAob`
- **Today:** not wired. Genau publishes a CE-replayable AOB triple for GWorld and &GEngine only. Consequences:
  - there is no `gobjects_addr` or `gnames_addr` symbol;
  - there is no GWorld or GEngine symbol either when Genau withholds the triple. It does that when CE's replay
    would not land on the resolved address: see `CeReplayMatchesResolved`, and the RipBoth deref arm.
- **Route:**
  1. Feed `EngineState.<X>ScanAddr` (the referencing instruction) to AOBMaker.UI's `GenerateAob`.
  2. It returns a unique `aob` plus `pos` and `aoblen`.
  3. Pass those to `CreateSymbolScript`.
- **Limits:**
  - This works only when the scan-hit instruction is a RIP-relative reference to the target, because only then does `GenerateAob` return `pos` and `aoblen`.
  - For the deref arm, the symbol names the slot, and our export has to add one dereference.
  - AOBMaker.UI must be running at the same integrity level.
  - `GenerateAob` is fixed to mask Mode A with a 2-hit uniqueness scan (`MaxResults = 2`), with no options.
- **Use:** a CE-side GNames anchor is what the todo's "UE FName to String" custom type would need (C7).
- **Effort:** M. **Value:** medium.
- **Done** (`90c732e5`) for GObjects and GNames: a SYM button on each, and `Services/AobMakerUiClient` for
  AOBMaker.UI's own pipe. Implementing it turned up two things the route above missed:
  - **The scan hit is where the PATTERN matched, not the instruction.** Himmel's signatures put their RIP
    instruction up to 21 bytes in (`instrOffset`), and `GenerateAob` decodes the instruction at the address it is
    given. So the UI reads 64 bytes at the scan hit and seeds `GenerateAob` with the instruction whose
    `[rip+disp32]` lands exactly on the resolved address (`AobMakerActions.FindRipSeed`).
  - **Some signatures adjust the target** (`adjustment` −0x14 to +0x0C on eight GObjects entries and one GNames
    entry).
    `CreateSymbolScript` registers the raw RIP target, so those are refused before AOBMaker is asked. Supporting
    them needs AOBMaker R17, or a symbol script of our own through `CreateAAScript`.

  Nothing is pushed unless the returned AOB replays (`disp = [aob+pos]`, `aob + aoblen + disp`) to the address the
  DLL resolved. The GWorld / &GEngine withheld-triple case is not done: `[AOBM-GWORLD-GENAOB]` in todo.md.

### A9 — Deliver the live Structure Dissect builder into the open table
- **Today:** manual.
  - `scripts/ue5_dissect.lua` builds CE structures live from the DLL. Its auto mode registers
    `registerStructureDissectOverride`, so any UObject address dissects itself.
  - The user has to `dofile` it from disk. Unlike `ue5_invoke_helper.lua` and `ue5_freeze_helper.lua`, it is not
    embedded in UE5DumpUI.
- **Commands:** `InjectTableFile` for the script itself. Then `CreateAAScript` for a record whose `[ENABLE]` loads the
  script through `findTableFile` and turns auto mode on, and whose `[DISABLE]` turns it off.
  The baked-invoke and Freeze helpers already use this load pattern.
- **Rules:** embed the script as an `EmbeddedResource`, as the two helpers are. The record follows the CE Lua hygiene
  rules in CLAUDE.md (`CeLuaHygiene` emitters).
- **Why it is here:** this is the Structure Dissect path current AOBMaker can already serve. See C3 for what it cannot.
- **Effort:** M.
- **Done** (`90c732e5`): Tools → "Add Auto Structure Dissect to Current CE Table" (`DissectScriptGenerator`,
  `DissectLuaResource`). The record arrives unticked, checks that `UE5Dumper.dll` is loaded before anything else, and
  keeps the module in the Lua global `UE5Dissect`, so `UE5Dissect.createInteractive()` works from CE's Lua console
  while it is ticked.

---

## B. Doable today, but something is lost — AOBMaker must change

### B1 — Hierarchical CE XML straight into CE
- **Today:** clipboard only.
  - Live Walker: `ExportCeXmlAsync` ("Copy CE XML") and `ExportCeFieldXmlAsync` ("Copy CE Field").
  - Instance Finder: `ExportCeXmlAsync`.

  The flat `PushCeFieldToCeAsync` exists, and states that it does **not** reproduce the hierarchy.
- **Why it is now reachable:** §6 deferred this because we have no tree model and no bulk client.
  `ImportCheatTableXml` removes both blockers: it takes our XML as-is, and AOBMaker.UI chunks it into
  `CreateRecordTree*` itself.
- **Checked: our output passes AOBMaker's validator.**
  - Both group emitters (`EmitGroupOpen` and `EmitGroupPlaceholder`) always write `<Address>`.
  - Roots are either an AA script or an absolute address.
  - The `base` record under the AA-script root (a symbol address on a non-root node) raises a warning, not an error.
- **What is lost.** `CheatTableXmlImporter` reads only `Description`, `VariableType`, `AssemblerScript`, `Address`,
  `Offsets`, `GroupHeader`, `ShowAsHex`, `ShowAsSigned`, `Length`, `Unicode` and `Options@moHideChildren`. So:
  - **`<BitStart>` and `<BitLength>` are dropped.** A bit-field bool arrives as `Binary` with CE's default bit
    position instead of its own, so it can show the **wrong value**. That is a correctness loss, not a cosmetic one.
  - `<DropDownList>` and `<DropDownListLink>` are dropped, so enum and FName names are gone.
  - `<Color>` is dropped: the alternating row colours.
  - `<CodePage>` and `<ZeroTerminate>` are dropped, which changes how strings display. The `ZeroTerminate` loss is
    permanent: AOBMaker declined it, because CE's Lua cannot set it (R2).
  - The child-activation options are rewritten: `moActivateChildrenAsWell` is dropped, `moDeactivateChildrenAsWell`
    alone is dropped, and a header with only `moHideChildren` gains "deactivate children too". That changes what
    ticking a header does (AOBMaker `EV-1`: 71, 51 and 360 cases in its corpus).
  - `ByteLength`, `ShowAsBinary` and `Hotkeys` are dropped, and a `VariableType=Custom` record becomes CE's default
    4 Bytes (AOBMaker `EXT-15`).
  - `<ID>` values are renumbered. That is harmless here.
  - None of it is reported. The reply counts only absolute-address warnings.
- **Size limit:** one request holds at most 10 MiB of JSON-escaped XML. `CeXmlExportService.MaxEmitEntries` is 60,000,
  and a large export can exceed that. Going around the limit means talking `CreateRecordTree*` directly, which
  brings back the tree-model work §6 describes.
  - Splitting the export into several `ImportCheatTableXml` calls does not help: each call builds a new tree at the
    root (AOBMaker reply §2.8).
  - AOBMaker measured about 710 bytes per entry with .NET's default encoder, so about 14,700 entries fit in 10 MiB.
    The default encoder writes `<` and `>` as 6-byte escapes. Sent with `UnsafeRelaxedJsonEscaping` (our CE-bridge
    client's encoder; `AobMakerUiClient` uses the default one today), the XML should need fewer bytes per entry; not
    measured.
- **AOBMaker's side:** its `docs/Known-Scope-Limits.md` records these drops as a scope decision. The importer
  "targets structural tree rebuild … not byte-perfect display-metadata fidelity". Since `e75562d` it carries a
  correction: bit parameters and the child-activation options are not display metadata (`Known-Scope-Limits.md:118-124`).
  The drops themselves are unchanged.
- **AOBMaker change (R2, R8):**
  - carry `bitStart`/`bitLength`, `dropDownList` (with its link and `DisplayValueAsItem`), `color` and
    `codePage` through `BulkRecordNode`, the importer and the plugin's Lua;
  - report what it drops;
  - stream imports larger than 10 MiB.

  AOBMaker's answer: bits and `dropped` P1; dropdown after R3; colour and `codePage` P2–P3; streaming P3. Stage 1 of
  R3 also ships with a loud reject (or a `dropped` report) of the records the importer cannot carry. Our bit-field
  bools are Binary records, so they may be refused outright until R2's bits ship. Reply §4 keeps
  `[AOBM-CEXML-PUSH]` blocked on R3 too without saying why; that reject is our reading of it (reply §2.3).
- **Verdict:** hold the push until at least bits and dropdowns survive. A push that shows wrong bools is worse than a paste.

### B2 — Generated .CT files straight into CE
- **Today:** file only. roadmap.md: *"AOBMaker direct-inject of the generated CT is also v2"*. The sources are:
  - the Interesting Properties and Interesting Functions batch CT, via `MainWindowViewModel.SaveCheatTableAsync`;
  - Teleport's `SaveCtAsync`. Teleport also has the "Add actions" push.
- **`ImportCheatTableXml` rejects the whole file.** `CheatTableBuilder` writes the root group and the category
  groups without `<Address>`. `BulkRecordValidator.ValidateAddress` fails any non-script node with an empty address
  ("address is empty"), and one error aborts the whole import.

  This is a general AOBMaker defect, not our format. AOBMaker's own `samples/cheat_tables` hold **573**
  address-less `GroupHeader=1` entries across **18** of the 19 tables (AOBMaker's re-measurement, `EXT-2`).
  AOBMaker accepted R3 in two stages. Our `.CT` imports after stage 1, with its folders looking like address headers
  until stage 2.
- **What works today:** one `CreateAAScript` + `group` call per row. Every row `CheatTableBuilder.EmitRowEntry` writes is
  an Auto Assembler script, and Teleport's "Add actions" already works this way. It loses three things:
  - the two-level nesting (root → category), because `group` names one group, not a path: it matches that name at
    any depth, and creates a missing one at the root;
  - the colours, because `CreateAAScript` has no colour field;
  - certainty about the parent: `group` attaches to the **first** record, at any depth, of that description whose type is not 11, which may be a value record rather than a header.
- **AOBMaker change (R3, R7):**
  - accept an address-less `GroupHeader` as a plain folder (`IsGroupHeader`), both in the import and in bulk nodes;
  - support nested group paths;
  - make `group` match headers only;
  - add a colour field to `CreateAAScript`.

### B3 — What a +CE record can hold
Applies to the existing Live Walker `+CE` and `Push CE Field`, and to every A2 and A3 site.
`CreateMemoryRecord` carries only an address, a type, a signed flag and a hex flag. The losses:

| Field kind | What +CE produces | Where it comes from |
|---|---|---|
| Bit-field bool | The containing byte | Deliberate: `CeXmlExportService.KeywordToValueType` maps `Binary` to Byte |
| Enum, FName | A number, with no dropdown | `CreateMemoryRecord` has no dropdown field |
| FString | An 8-byte hex pointer | `MapCeField` has no `StrProperty` case. The XML path emits a string leaf with `Offsets [0]`, `Length` and `Unicode` |
| Any field | An absolute address, which does not survive a restart | The XML path is GWorld-rooted. AOBMaker notes the plugin passes `address` straight to `mr.Address`, and CE accepts expressions such as `[x]+off` there; unproven through the bridge |

- **Partial workaround today:** a one-node `CreateRecordTree*` batch carries offsets, length and unicode. It still cannot carry bits or dropdowns.
- **AOBMaker change:** R2's node fields, sent as a one-node `Begin` / `Chunk` / `End`. We asked (R6) to give
  `CreateMemoryRecord` `bitStart`/`bitLength`, `dropDownList`, `length`/`unicode`/`codePage` and `offsets`; AOBMaker
  keeps `CreateMemoryRecord` as it is and re-targets the item to that batch (reply §2.6, P3).

### B4 — `autoActivate` reports success whether or not CE enabled the script
- **The code:** with `autoActivate:true`, the plugin runs `if ok then mr.Active = true end` and never reads `Active` back.
  This is in `pipe_server.cpp` 1262–1265, and the same pattern is in `CreateSymbolScript`.
  A script that CE failed to enable still replies `success:true`.
- **A third site AOBMaker found:** `CreateAAScriptWithRecords` activates unconditionally, before its own check. An
  exception there replies `success:false` although the records exist.
- **Where we depend on it:**
  - the Pointer panel's Register GWorld and Register &GEngine symbol buttons, and since A8 its GObjects / GNames SYM;
  - the standalone trainer's Setup row, until `1f9cf2ef` pushed it unticked (`[AOBM-TRAINER-SETUP-MODAL]`).
- **Blocking risk:** activation runs on CE's main thread while the single worker waits.
  - AOBMaker's own live test saw an `Active=true` enable raise CE's "Nearby allocation error" Yes/No dialog,
    and its driver stopped at that dialog (`docs/Live-Verification-Multi-Apply.md`). That driver enabled the record
    from CE Lua directly, not through the bridge.
  - While a dialog is open, the bridge is busy for every client.
  - **Our own defect, found by AOBMaker (reply §2.4):** the trainer Setup's failure paths call `showMessage` (a modal)
    and untick 50 ms later. Over the bridge that modal holds the single worker, and an immediate read-back still sees
    `Active=true`. AOBMaker cannot fix it from the plugin: `[AOBM-TRAINER-SETUP-MODAL]` in [todo.md](todo.md), fixed
    in source in `1f9cf2ef` by pushing Setup unticked.
- **AOBMaker change (R4):** read `Active` back and return `activated`, with CE's text in the existing `message`.
  AOBMaker's answer (reply §2.4, `EXT-4`; P1, batch B, not started):
  - `activated` is tri-state: `true`, `false`, or absent = unknown (an older plugin, no `autoActivate`, or a client
    timeout);
  - no `autoAssembleCheck` pre-check: it sees an empty script in both our consumers, and runs unguarded `{$lua}`
    twice;
  - `CreateSymbolScript` will also say whether the symbol resolves after activation, since a failed scan only prints a
    warning and leaves `Active` true.

### B5 — `FindMostAccessed` ("what accesses this field")
- **Status:** it exists, but AOBMaker itself never calls it. `FindMostAccessedAsync` has no caller, and there is no live-verification record.
- **What is lost:**
  - Every hit except the most frequent one. The hit map is discarded.
  - Read versus write. It sets a size-4 *access* breakpoint (`debug_setBreakpoint(addr, 4, 1)`).
  - Windows longer than 30 s. Any `durationMs` above 30,000 is reset to 5,000, not clamped.
  - Probably the accessing instruction itself. It records `ExceptionAddress`, and x86 data breakpoints are traps,
    so that is normally the instruction **after** the access. CE's own "Find out what accesses" compensates for this;
    the plugin does not. **This is inferred, not measured. Check it live before building on it.**
  - Found by AOBMaker while checking our list:
    - hit counts are unfiltered: every single-step exception in the window counts (`EV-2`);
    - a failed debugger attach cannot be told apart: CE's breakpoint export always reports success, so the plugin
      sleeps the whole window and replies "No accesses detected" (`EV-3`). It also scrolls CE's disassembler from the
      pipe thread.
- **Blocking risk:** it holds the single-instance bridge for the whole window.
- ⚠ **It attaches a debugger without asking:** the plugin calls `startdebuggerifneeded(false)` before it sets the
  breakpoint. Keep it away from games with anti-debug checks.
- **Until it is fixed:** use +CE (A2, A3, B3), then CE's own "Find out what accesses". Live Walker's +CE tooltip already suggests that.
- **AOBMaker change (R9), as corrected by AOBMaker:**
  - return all hits with their counts, as parallel arrays;
  - `kind: access|write` only (x86 debug registers have no read-only mode); no answer on size;
  - keep the raw trap address and add a separate, best-effort instruction address (a blanket "previous instruction"
    is wrong for `call` / `jmp [mem]` / `ret` and REP string instructions);
  - stop blocking the bridge.

  **R9 is on hold** at AOBMaker until a live check (P3; the `durationMs` clamp P2).

---

## C. Cannot be done today — needs a new AOBMaker interface

### C1 — Find, update and delete records; return record IDs
- **Today the bridge is write-only.**
  - [teleport-coord-library-spec.md](teleport-coord-library-spec.md): *"`IAobMakerBridge` has no read/list operation"*.
  - No create reply carries the new record's ID: they return `type` and `success` only.
- **Evidence that we need it:**
  - `TeleportViewModel.PushGameThreadReminderAsync` documents *"we cannot read CE's table"*.
  - Pressing Teleport's "Add actions" twice duplicates every record it pushes.
  - Duplicates are not only clutter. Audit #4 [audit-2026-08-04-findings.md](audit-2026-08-04-findings.md) B26 found that an older duplicate's `[DISABLE]` freed the newer one's buffer.
    The fix made `[DISABLE]` check ownership. The Global Pointers buttons also remember their own pushes (`_pushedQuerySymbols`),
    but that memory cannot see CE's table. It is empty again after UE5DumpUI restarts, and a repeat click has to fall back
    to the clipboard in case the user deleted the record.
- **Proposal (R5), with AOBMaker's corrections:**
  - the record `id` in every single create reply, and `recordIds` (an array in node order) on bulk chunks (P1);
  - `FindRecords(description, group, valueType)` returning IDs, scoped to a group and with a result limit (P2);
  - an `ifExists: skip` option on every create, after R7;
  - `DeleteRecords(ids)` and `ifExists: replace`: **on hold** at AOBMaker.

### C2 — Record hotkeys on push (low priority)
- **Why it could matter:** `TeleportScriptGenerator`'s header calls a CE **record-level** hotkey the reliable way to
  fire a momentary teleport record, and the Teleport tab already stores the user's bindings.
- **Gap:** no command or field sets a record hotkey.
- **Why it is low priority:** [lessons-learned.md](lessons-learned.md) records that hotkeys created from table Lua
  with CE's `createHotkey` "worked on some games' CE sessions and silently did nothing on others". Since then the
  app's own OS-level hotkeys are the primary path, and user-bound CE record hotkeys are "a fine secondary path". A
  hotkey that AOBMaker sets would first have to be proven to behave like a user-bound one.
- **Request:** R10. **Not accepted for now** (AOBMaker reply §2.10): the Lua call that creates a record hotkey is
  unverified in both repositories. Risk: our own `RegisterHotKey` Teleport keys may fire twice, or never reach CE.

### C3 — Structure Dissect from the UI's own CSX
- **Today:** file only. `LiveWalkerViewModel.ExportCsxCoreAsync` writes a .CSX for CE's "Import from file".
  No plugin command creates a structure.
- **Why it is low priority:** A9 already puts live dissection in CE with today's commands. This item would add only
  a one-click push of Live Walker's *exported view* of a structure.
- **Request:** R12 (`CreateStructure` from CSX XML). **Declined** (AOBMaker reply §2.12): no XML parsing inside CE.
  The CSX half is now our own work: generate `createStructure` / `addElement` Lua from `CsxExportService`'s model,
  and push it as `{$lua}` through `CreateAAScript`. It inherits B4's gap.
- **README:** its AOBMaker paragraph claimed the plugin delivers "Structure Dissect data". That was corrected
  2026-09-29.

### C4 — Attach CE to the game's process
- **Gap:** there is no `openProcess` equivalent. `GetAttachedProcess` only reads.
- **Why:** with this, A6 could fix the mismatch it detects in one click.
- **Request:** R11.

### C5 — "UE field → the instruction that writes it → an injection AA script"
- **AOBMaker's side:** no pipe command exposes AOBMaker's injection-script generator (`TemplateEngine`).
  `SendDisassembly` only fills AOBMaker.UI's input box.
- **Needs:**
  - a `GenerateInjectionScript(address, …)` command (R13). AOBMaker accepted it in principle, P3, design first;
    our `template` maps to none of its generator options;
  - on our side, instruction addresses kept in Denken. `NativeFieldAccess` merges accesses by offset today.
    With the addresses, native write sites are found statically, with no debugger. That matters in games that fight one.
- **Assessment:** probably the most valuable combination of the two tools, and the most work.

### C6 — A GWorld AOB when GWorld came from an exported symbol
- **Gap:** when Genau resolves GWorld through an export (Satisfactory, `?GWorld@@3VUWorldProxy@@A`), there is no referencing instruction to seed `GenerateAob`.
- **Needs:** either a "find code that references address X" search in AOBMaker (R14), or an xref pass in Genau (our side).
  AOBMaker accepted R14 as P2, batch A. It finds RIP-relative references inside the scanned module only, so for an
  exported GWorld only the defining module's own references show. Whether that helps Satisfactory needs a live check.

### C7 — Custom-type records
- **Motivation:** the todo item "CE export drilldown — remaining gaps" wants FName shown live through a "UE FName to String" custom type.
- **Gap:** neither `CreateMemoryRecord` nor `BulkRecordNode` can carry `CustomType`, and the importer drops it.
  Registering the type is already possible through a `{$lua}` `CreateAAScript`; setting it on records is not.
- **Proposal:** a `customType` field (R15). AOBMaker accepted it after a CE-source check (P3), with two warnings:
  reading `NumericalValue` from a record whose custom type is not registered yet crashes CE (`pcall` cannot catch it),
  and `showAsHex` on a custom-type record silently breaks `Value` both ways, so never push the two together.

---

## D. Not candidates

These copies target a search box, a script or the game console, not CE:
- the name copies in Live Walker, Instance Finder and the Object Tree;
- the property- and function-name copies in Interesting Properties, Interesting Functions, Detect Stats, Live Funcs, `FunctionPropsDialog` and `PropertyXrefDialog`;
- `PropertySearchViewModel.CopyOffsetAsync`;
- `DumpExplorerViewModel.CopyPathAsync`;
- `TeleportViewModel.CopyAsBugItGoAsync`.

These already push through AOBMaker and are not re-evaluated here (B3 and B4 still apply to them):
- Live Walker: HEX, +CE, Push CE Field, AA, INV and the baked scripts;
- the Pointer panel's HEX, ASM and SYM;
- Property Search Freeze;
- every Teleport push;
- the Tools-menu injects;
- the invoke dialog.

---

## E. Suggested order

1. **UE5CEDumper only, existing commands:** A1, A2, A3, A6, A7. All small. A4 and A5 are optional polish.
   ✅ Done, together with A8 and A9, in `90c732e5`.
2. **Ask AOBMaker for the P1 requests** in [aobmaker-requests.md](aobmaker-requests.md):
   - R1, capability discovery, so every later field can be feature-gated;
   - R2, B1's bits and dropdowns;
   - R3, B2's address-less headers;
   - R4, B4's activation result;
   - R5, C1's record IDs. AOBMaker made only the IDs P1: `FindRecords` is P2, and delete is on hold.
3. **Then:** B1's push and B2 (A8 and A9 went ahead with step 1).
4. **Hold:** B5, until R9 lands and it is checked live.

**Where each item is tracked.** The todo rows are in [todo.md](todo.md) `[AOBMAKER-EVAL-2026-09-29]`; the requests
are in [aobmaker-requests.md](aobmaker-requests.md).

| Item | Todo row | AOBMaker request |
|---|---|---|
| A1 | `[AOBM-INSTFINDER-AA]` | — |
| A2 | `[AOBM-VALUE-ROWS-CE]` | — |
| A3 | `[AOBM-INSTFINDER-CE]` | — |
| A4 | `[AOBM-OBJECT-HEX]` | — |
| A5 | `[AOBM-PTR-SCANASM]` | — |
| A6 | `[AOBM-ATTACH-CHECK]` | R11, for a one-click fix |
| A7 | `[AOBM-FUNC-DISASM]` | — |
| A8 | `[AOBM-GNAMES-SYMBOL]`; the rest in `[AOBM-GWORLD-GENAOB]` | R17 (unanswered), for adjusted signatures |
| A9 | `[AOBM-DISSECT-INJECT]` | — |
| B1 | `[AOBM-CEXML-PUSH]` | R2, R8 |
| B2 | `[AOBM-CT-PUSH]` | R3, R7 |
| B3 | `[AOBM-PLUSCE-FIDELITY]` | R2 (one-node batch; R6 re-targeted) |
| B4 | `[AOBM-ACTIVATE-RESULT]` | R4 |
| B5 | `[AOBM-FIND-ACCESS]` | R9 |
| C1 | `[AOBM-DEDUP-TABLE]` | R5 |
| C2 | `[AOBM-RECORD-HOTKEYS]` | R10 |
| C3 | the CSX half of `[AOBM-DISSECT-INJECT]` | R12 declined — our side |
| C4 | `[AOBM-ATTACH-CHECK]` | R11 |
| C5 | `[DENKEN-WRITE-SITES]` (our half) | R13 |
| C6 | `[AOBM-EXPORT-GWORLD-AOB]` | R14 |
| C7 | `[AOBM-CUSTOMTYPE]` | R15 |

R1 (capability discovery) and R16 (protocol hygiene) serve every row rather than one.

## F. Protocol traps for a client that talks to the bridge directly

These matter for B1, B2 or B3's one-node workaround. Our serializer is compact JSON, but a hand-built string is not.
AOBMaker's reply §3 is the current client contract; the list below follows it.

- **Array keys must be compact.** The plugin finds `"nodes":[`, `"offsets":[`, `"records":[` and `"ids":[` by literal
  search, so a space after the colon is not tolerated:
  - with `"nodes": [`, a chunk replies success with `created:0`;
  - a spaced `offsets` or `records` key silently drops the offsets or the child records.

  AOBMaker's `docs/API-CEPlugin.md` used to claim whitespace is fine. Since `e75562d` it states the real rule
  (`API-CEPlugin.md:41-52`): whitespace after the colon is fine for string, int and bool values on plugins built
  2026-09-08 or later; after an array key's colon, or before any colon, it breaks.
- **The envelope `type` (and `description`) must come before any nested object.** The extractor takes the first
  occurrence of a key.
- **A numeric CE type in a node must be a string** (`"2"`, not `2`).
- **Omit `parent` for a root.** `"parent": null` counts as present. The node then attaches under node 0 when a node
  with id 0 exists, and otherwise stays at the root.
- **Oversized requests get no reply, on both pipes.** The CE bridge and the AOBMaker.UI pipe both drop them without a
  reply. The client-side 10 MiB pre-flight check is already an open item in [todo.md](todo.md).
- **Colours are CE `TColor` hex, BGR order**, at most 6 digits. The plugin writes them as-is.
- **One bulk import stays under 10 minutes from its `Begin`, and sends `End` once.** Chunks do not refresh the batch's
  age, and a repeated `End` replies `success:true, totalCreated:0`.
- **On the AOBMaker.UI pipe, a missing `success` means false.** Always use a read timeout there.
- **With `autoActivate`, `success` means "created", not "enabled".** On `CreateAAScriptWithRecords`, `success:false` can
  still leave the records in CE.
- **`totalCreated` after a failed import is not the real count.** AOBMaker.UI discards the plugin's count.
- **Expect one bridge client at a time.** One pipe instance, one worker.
- **`group` binds to the first same-named non-script record at any depth.**
- **`GenerateAob` takes instruction addresses only.** It does not check that the address is code.
- **Call `FindMostAccessed` only where a debugger on the game is acceptable.** It attaches CE's debugger without
  asking, and holds the single bridge worker for `durationMs`.
