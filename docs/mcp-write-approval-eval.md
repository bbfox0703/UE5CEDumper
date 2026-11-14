# MCP write approval: grants instead of a prompt on every call — EVALUATED 2026-11-14, NOT STARTED

> **Status.** The maintainer asked for this evaluation first, written down, with no implementation yet (2026-11-14).
> Next step: the Phase 0 measurements in §6, which need no build. Then the maintainer decides the open points in §8,
> and Phase 1 can start. Tracked in [todo.md](todo.md) under `[PIPE-PER-PROCESS-2026-11-06]` Stage D as
> `[MCP-WRITE-GRANTS-2026-11-14]`.
>
> **Sources.** Two workflows on 2026-11-14, eight and seven agents:
> - `wf_17c2b19a-5d3`: how approval works today, Claude Code's documentation, the MCP specification and twelve other
>   hosts, with a skeptic re-checking every claim;
> - `wf_806dc258-f3b`: a risk inventory of every write tool, the Claude Code behaviours a design depends on, three
>   independent designs, a judge, and an adversarial review.
>
> Facts marked *(docs)* come from code.claude.com as read on 2026-11-14 and are not measured here. Facts marked
> *(measured)* cite the verification register.

## 1. Why every write asks today

- Every `write_*` tool carries `_meta["anthropic/requiresUserInteraction"]: true` (`McpServer.cs`, the tools/list
  entry). In Claude Code that prompts on **every** call in every permission mode, `bypassPermissions` included. It
  offers no "don't ask again", allow rules do not skip it, and a PreToolUse hook's `allow` does not either; `dontAsk`
  denies the call *(docs; measured for `-p` and an interactive desktop session, verification-register.md
  `[PIPE-PER-PROCESS-2026-11-06]` items 15 and 22)*.
- **It is our choice, not a Claude Code limit.** D2 put the marker on every write because an unmarked state-changing
  tool runs with no prompt in auto mode: `scan_start` did, under this machine's `defaultMode: "auto"` (item 15).
- Claude Code's documentation names the marker's intended use: "a consent or access-grant step where auto-approval
  would mean no human ever agreed". The marker is per tool; the server's other tools keep the normal rules *(docs)*.
  That is exactly the shape §4 uses: the marker moves from the writes to a few grant tools.

## 2. What approval is, and what it is not

- **The host enforces it, not the model.** Inside Claude Code a model on the MCP route cannot skip a marked prompt.
  But only Claude Code reads the `anthropic/` marker. The MCP specification calls annotations such as
  `destructiveHint` untrusted hints and a human in the loop only a SHOULD. Most other hosts have an always-allow or
  YOLO switch, and some let a classifier model decide by default (Cursor's Auto-review). A survey cited by
  agentpatterns.ai (arXiv 2611.02527) found that 32 of 41 open-source hosts and agent SDKs have no approval step by
  default, and that 39 of 41 ignore `destructiveHint`.
- **A same-user agent with a shell goes around MCP entirely.** `tools/verify/pipe_client.py` opens the pipe without
  `background` or `if_free`; `tools/verify/mailbox_poke.py` writes game memory with `OpenProcess` and
  `WriteProcessMemory`. On native Windows, Claude Code's Bash is unsandboxed *(docs)*, and in auto mode a classifier
  approves shell commands, not a human.
- **So approval guards against a careless model on the MCP route; it is not a security boundary.** The protections
  that hold on every host are the server's and the DLL's own:
  - `if_free`: never over a hold the UI, a CE record or another session has;
  - release of this server's holds when it leaves;
  - `keep_undo` on a teleport;
  - `write_mem` writes data pages only and needs `expect`;
  - the invoke deny list;
  - the experimental switch;
  - the DLL build floors.
- **The server can never learn that a human approved a call.** `tools/call` carries no such field, and nothing tells
  the server the client's permission mode *(docs, MCP spec)*.

## 3. The Cheat Engine comparison (the maintainer's point)

**The point.** A CE user writes memory and calls functions with no prompt at all, and MCP exists to let an AI
automate. **The counter-point, which the maintainer accepted:** an AI can do anything, flexibly, while a CE record was
fixed in advance by a human.

**Where the two differ.** In every no-prompt path this tool has, a human chose the **target**: CE records, the UI's
field edit and the UI cards.
- A baked CE invoke record fixes the class, the function and the arguments.
- CE's runtime invoke form fixes the function, and the human types the values each time.
- A CE address record fixes the address.
- The UI's field edit is one typed field.

The AI's raw tools take any address (up to 4 KB) and any function on any live object, chosen on the fly.

**Conclusion.** One grant per **target** fits CE; one grant per session for raw writes would exceed it. It is not full
parity either. In CE a human chooses the values, and a record's on/off state is always visible in the table. Under a
target grant the AI chooses the instance, the values and the frequency, and the UI shows nothing of it. So a target
grant is **delegation**: the human fixes what, the AI decides when and how much. The plan treats it that way (§4.4,
§4.7).

## 4. The plan

### 4.1 What asks and what does not

The prompt moves from every repeat of a write to every new kind of risk or new target. Only grant tools carry the
marker. The write tools they cover lose it, and the server refuses any covered write with no matching grant.

| Tool or group | Today | Plan |
|---|---|---|
| Holds: god mode, debug camera, Keep Foreground, See-through; also the mouse cursor (one-shot) and time dilation | asked every call | One **gameplay-toggles grant** per game run. OFF and reset need no grant. |
| Movement knobs, gravity direction, Fly, teleport | asked every call | One **movement grant** per game run, worded "may kill or lose the player's pawn; a teleport is not undone". |
| `write_invoke` | asked every call | One **invoke grant per function** (§4.4), only after M1 (§6). Until then it stays marked. |
| `write_memory` | asked every call | **Stays marked, asked every call.** Phase 2 adds a typed `write_field` under a class.field grant (§4.5). |
| `write_force_field` | asked every call | **Stays marked**: it reaches every instance of a class tree, and it is rare and experimental. |
| `write_force_field_reset` | asked every call | Unmarked, no grant: it only ends this server's own job, which needed a prompt to start. |
| `read_*`, `scan_start` | unchanged | Unchanged. Their commands form the gate's read list. |

**Why two gameplay grants and not one.** "Annoying" (god mode stays on, the game runs fast) must never quietly imply
"may kill or lose the pawn". God mode plus Fly then costs two prompts per game run instead of one.

**Bounds.** The server refuses a value outside the UI's own range rather than letting the DLL clamp it silently.
- The DLL already clamps the movement multipliers to 0.1–10 and fly speed to 50–20000, and it normalises the gravity
  vector (`Grimoire.h`, `Laufen.cpp`).
- Two DLL ceilings exceed the UI. Time dilation: the DLL allows 100, the UI's sliders stop at world 3 and pawn 10
  (`TeleportPanel.axaml`). Jump: the UI's 30x is height, a velocity of √30 ≈ 5.48 (`TeleportViewModel.cs`). The
  server refuses past those two.

### 4.2 The grant tools

- **Kind grants** (gameplay toggles, movement) take no scope argument except `pid`. Their meaning does not depend on
  whether the Code tab shows arguments (M1 unmeasured).
- **The name must carry the scope**, because the prompt may show only the name (M10). A name like `grant_toggles` hides
  that it covers time dilation and the cursor. Use names that state the scope, plus a required `scope_ack` argument the
  server checks against a fixed sentence (the covered tools and the lifetime). The final names follow M10 (§8).
- `readOnlyHint: false`, **`destructiveHint: true`**. They unlock destructive tools, and a host that auto-runs
  non-destructive tools would otherwise auto-approve the consent step.
- **Timing.** The server's instructions tell the model to state the scope and lifetime in chat and to call the grant
  only after the user asked for a change, never in advance. A refusal names the exact grant call to make.
- **Every grant reply** lists what is covered, the game, what ends the grant, and the session's other grants.
- **Two more tools.** `read_grants` is read-only, so a user's optional `read_*` allow rule admits it. `revoke_grants`
  is unmarked: it only narrows, and it never releases holds.

### 4.3 The gate

- **One choke point.** Every tool sends through `IMcpGame` (`McpGameLink.cs`). A gate wraps it and refuses every
  command that is not on the read list unless the call carries a grant capability. A forgotten check, or a future
  tool, then fails closed. A test pins the read list and pins that every `write_*` sends only read commands without a
  grant.
- **An explicit capability, not an `AsyncLocal` ticket.** `Admit` returns (grant id, pid, creation time, kind), and
  the state-changing send passes it. Inside `McpGameLink`'s lock the gate re-checks it against two things:
  - the game run recorded at connect, because `McpGameLink` silently reconnects to whichever game answers, and a write
    admitted for run A must not reach run B;
  - the live grant table, so a revoke that lands mid-call wins.
- **A release is classified from the request body, not the tool.** It is a `reset_*` command, or an ON/OFF command
  whose only state field is `enable: false`. `fly_set` applies speed, preset and noclip **before** `enable`, as global
  settings nobody releases (`Fern.cpp`), so `write_fly {enable:false, noclip:true, speed:20000}` is not a release. A
  test per command pins this.

### 4.4 Invoke grants (Phase 1b, after M1)

- **Key.** The exact resolved FullName, ordinal compare. Its real format is `Ubel::GetFullName`'s
  `//Script/Engine/Actor.SetActorHiddenInGame`: the function comes **last**, so a prompt that cuts long strings hides
  exactly the function. The grant therefore takes the short function name first and the declaring class second, and the
  server compares both with the resolved FullName. A subclass override has its own FullName and needs its own grant.
- **Free arguments and instance**, like CE's runtime invoke form. One function per grant, no lists, no wildcards.
- **Refused for grants** (the per-call marked route stays):
  - **Indirection functions.** These turn one name into every function or property: `K2_SetTimer` and its family
    (timer by function name), every `Set*PropertyByName` setter, and any function with a parameter that names a
    function, an event or a property.
    - Example: a grant on `K2_SetTimer` would let the model schedule `K2_DestroyActor` or `RestartLevel`, both on the
      deny list, by name.
    - A looping timer also outlives the session, outside the DLL's release.
  - **`ExecuteUbergraph_*`.** Its free `EntryPoint` enters the Blueprint's bytecode at any offset; the repo already says
    never to fire it.
  - **Online-service functions** (achievements, leaderboards, purchases). They are not RPCs, so `FUNC_Net` passes them,
    and their effects are permanent on the user's account. Exact names come from UE's OnlineSubsystemUtils and must be
    verified at implementation.
- **Under a grant, refuse a non-static function on a class default object.** Today the DLL only warns about that, and
  it can change the defaults of later spawns.
- **No use cap in Phase 1** (§8): each call is still a model turn, so a runaway loop is slow and visible. Every
  admitted call is counted, and the count shows in `read_grants` and the log.

### 4.5 `write_field` (Phase 2, after M1)

A typed twin of the UI's field edit: `write_field {instance_addr, field_name, value}` under a marked grant
`{class path, field_name}`.
- **Resolution.** The server resolves the property **by name** from an exact live object up its class chain. That
  avoids reverse-mapping an address (`find_by_address` answers exact, contains, backward or nearest).
- **What it writes.** Scalars only: bool, integer, float, enum, with ArrayDim 1.
  - Enum values are checked against the UEnum's entries.
  - A bitfield bool is written through its mask.
  - The current bytes are sent as `expect`.
- **Key.** The grant uses the class's full path, not its short name. Short names collide across packages.
- **Scope wording.** The grant says "every instance of this class and its subclasses, NPCs included". It also says the
  AI chooses values within the type's range: an index or a state value can still crash a game.
- **Raw `write_memory` stays per call.** Its CE equivalent is a human typing each address. A raw-range grant would ask a
  human to approve a hex range, which nobody can read meaningfully.

### 4.6 Lifetime

- **A grant ends** when the server process exits or the game run changes, or on `revoke_grants`.
  - The server exits on stdin EOF: the session closed, or `/mcp` reconnect or disable.
  - A game-run change is a different pid, or the same pid with a different `process_creation_time`.
    `get_pointers` already returns that time (`Fern.cpp`), and every write already sends `get_pointers` first.
- **A pipe drop and reconnect to the same game run keeps the grants.** The DLL drops the holds as today, and the AI may
  take them again without a new prompt. This is intended, and the docs must say it.
- **No timer in Phase 1.** Revisit if M9 shows a desktop session's server living for days.
- **Several AI sessions** each have their own server, grants and prompts. Grants never cross sessions, and holds keep
  the DLL's `if_free` arbitration between them.

### 4.7 Hosts and the version floor

- **The default is decided once, at initialize.** Writes and grants exist only for `clientInfo.name` `claude-code`
  with a parseable version at or above the floor (2.1.246, the repo's value). The docs name 2.1.199 for the marker
  itself; 2.1.246 is when "don't ask again" was removed from it.
- **Doubt falls back to today's behaviour, not to trust.** A Claude Code with a missing, unparseable or too-low version
  gets every write marked per call and no grant tools. Below the floor the marker may be ignored, and a single
  "don't ask again" on a grant would make every later grant silent.
- **Any other host is read-only by default.** `--writes=any-host` opens writes on that host. The user writes it into
  **that host's own registration**, so the opt-in is per host. The setup guide must warn that this flag plus the host's
  always-allow means the AI grants itself.
- **`--writes=off`** hides the write and grant tools everywhere. Any other value exits 1.
- **SDK hosts are untrusted until M8.** An Agent SDK application may introduce itself as `claude-code`, and its
  `canUseTool` callback can approve marked tools *(docs)*.
- **This is a behaviour change for Claude Desktop users.** Their writes become opt-in.

### 4.8 Claude Code, mode by mode

- **Recommended rule, Manual mode only until M3:** `allow: [mcp__ue5cedumper__read_*, mcp__ue5cedumper__write_*,
  mcp__ue5cedumper__revoke_grants]`.
  - It is safe because the server refuses ungranted writes.
  - It cannot weaken the still-marked tools: an allow rule never skips the marker (item 22).
- **Auto mode: hold the rule until M3.** With it, granted writes skip the classifier. The classifier is a free intent
  check against the user's own messages, so without the rule it may still block an unrequested teleport inside a
  grant. That is friction worth keeping until it is measured.
- **The way back.** A `permissions.ask` rule `mcp__ue5cedumper__write_*` restores a prompt on every call in every mode:
  ask beats allow, and ask rules prompt even in auto mode *(docs)*.
- **`dontAsk` and `claude -p`:** marked tools are denied, so no grants and no writes. That fails closed.
- **`bypassPermissions`:** unmarked writes run, but only inside grants.

### 4.9 What this still does not protect against

- **A shell-capable agent.** It can use the pipe or `WriteProcessMemory` directly, start its own
  `--mcp --writes=any-host`, or edit the host's settings.
- **Blind approval.** If the Code tab hides arguments, invoke and field grants do not ship (the M1 contingency).
- **Harm inside a granted scope:**
  - a teleport into the void in a game that does not respawn the pawn;
  - harmful values within the legal range;
  - a granted function called on NPCs, or repeatedly.
- **Subagents and workflow agents, if they share the session's server** (M6). They would inherit the human's grants and
  the allow rules, and the server cannot tell callers apart. Text the read tools return (FStrings, DataTable rows,
  object names) could also steer them. Answer before Phase 1:
  - if they share the server, ship a user-level PreToolUse hook that denies `mcp__ue5cedumper__write_.*` when the input
    carries `agent_id` (hooks fire inside subagents with `agent_id`);
  - the instructions say that game text is data, never a request, and that a declined grant means no, with no other
    route (`pipe_client.py`) to be tried.

## 5. Rejected, and why

| Idea | Why not |
|---|---|
| A session grant for raw `write_memory` or any `write_invoke` | No human ever sees a target; that exceeds CE rather than matching it. The deny list is name-only and weak by its own comment (`Voll.h`). |
| Drop the marker with no server gate | In auto mode the writes run unprompted (item 15), and Manual mode's only "remember" option is a permanent rule in `.claude/settings.local.json`. |
| Re-list tools via `notifications/tools/list_changed` | `listChanged:false` is pinned by tests. Whether Claude Code re-reads `_meta` after a refresh is undocumented, and the desktop app's refresh is reported as a no-op (anthropics/claude-code#88483, open). |
| MCP elicitation | A user's Elicitation hook can answer it with no dialog *(docs)*, so an answer does not prove a human agreed; the marker does. todo.md's "the Code tab declines it unseen" was never measured (M11). |
| A PermissionRequest hook with a `session` rule | Whether it can approve a marked tool is undocumented, and it works in Claude Code only; the server cannot see it. |
| Grants in the DLL's per-owner ledger | The `background` role it would key on is declared by the client itself; new pipe commands and a contract review buy nothing over an in-memory table that dies with the server. |
| The UE5DumpUI window as a source of grants | A same-user model with file tools could forge a grant file (`%LOCALAPPDATA%` is not a path Claude Code protects). The UI is kept for visibility and revoke only (Phase 3), where a forged file can only narrow. |
| Raw address-range grants on `write_memory` | Nobody can meaningfully read a hex range; it covers neighbouring vtables, headers and pointers. |
| One grant tool with a scope enum | The tool name would then say nothing, and the meaning would sit in arguments whose display is unmeasured. |
| A minutes argument, pinned invoke parameters, function lists or wildcards | Each adds something the human must read in a prompt whose argument display is unmeasured, or brings back rubber-stamping. |
| A global trusted-hosts file keyed on `clientInfo.name` | It would unlock every registration of any host that sends that name; a per-registration flag is explicit. |

## 6. Measure before implementing (Phase 0, no build needed)

Use the current 3709 dist in the desktop Code tab, and record the Claude Code version each time.

1. **M1, the deciding one.** Does a marked tool's prompt show its arguments in full? Use a `write_invoke` with a
   FullName longer than about 120 characters, read the prompt, decline, and record whether the end is cut. If it is
   hidden or cut, invoke and field grants do not ship, and today's per-call approval of `write_invoke` and
   `write_memory` is equally blind; record that too.
2. **M10.** Does the prompt show the tool's title or description, or only its name? This decides the grant names
   (§4.2).
3. **M6, before Phase 1.** Do a foreground subagent, a background subagent and a workflow agent share the session's
   server process (the pid in the `mcp-*.log` name, the count of initialize lines)? Does a marked prompt raised by a
   background subagent reach the main session?
4. **M3.** In an interactive Code-tab session in auto mode, does an allow rule run an unmarked MCP tool without the
   classifier? A community report says it was ignored (#77688). With no rule, does the classifier run it?
5. **M2.** In Manual mode with no rule (probe with `scan_start`), which options does an unmarked MCP tool's prompt
   offer, and which rule text and file does "don't ask again" write?
6. **M4 / M5.** In `bypassPermissions`, is a marked tool prompted as documented, or refused as "unavailable in
   unsupervised mode" (#58757)? In `dontAsk`, is it denied even under an allow rule?
7. **M7.** What `clientInfo.name` and `clientInfo.version` do the Code tab, the CLI, `-p`, Claude Desktop chat and an
   SDK session send? Only the name is logged today, so the version half needs a one-line logging change.
8. **M8.** In an SDK or workflow session inside the desktop app, does a human see a marked tool's prompt, or does
   `canUseTool` approve it automatically?
9. **M9.** Which of these end the server process: `/mcp` reconnect or disable, closing or archiving a session,
   quitting the app? How long does an idle session's server live?
10. **M11 (only before any future elicitation route).** Does the Code tab render `elicitation/create`?

## 7. Phases and what each touches

- **Phase 1: kind grants, the gate, the host policy.** UE5DumpUI only: no DLL change, no mailbox contract bump.
  - New `Mcp/McpGrants.cs` (the store, the capability, the gate) and `Mcp/McpGrantTools.cs`.
  - Gate calls in `McpWriteTools.cs`; the marker set and the flag in `McpTool.cs`, `McpServer.cs` (instructions per host,
    the initialize decision, list and call filtering), `McpHost.cs` and `Program.cs` (flag parsing), `Constants.cs`.
  - Tests:
    - `McpToolsTests`' marker pin becomes "the marked set is exactly the grants plus the per-call writes";
    - the choke-point and read-list tests;
    - release classification per command;
    - key mismatches, the reconnect case and the revoke race;
    - bounds at and past the edge;
    - the host matrix;
    - no instructions claim "asked before each one".
  - The rig: `tools/verify/mcp_client.py` starts with `--writes=any-host`, a write before its grant is refused, and the
    default host sees no write tools.
  - Docs: `docs/mcp.md`, `scripts/MCP-Setup.html` (the flags, the always-allow warning), the register's new live rows,
    working-lessons §6, architecture.md.
  - One AOT `-Mode Publish` build.
  - Estimate: about 450 lines of C# and 300 of tests, 1–2 sessions.
- **Phase 1b: invoke grants**, after M1, with the §4.4 exclusions.
- **Phase 2: `write_field`**, after M1. About one session.
- **Phase 3 (optional): visibility in the UI.** Per-session status files, "held by AI session pid N" on the existing
  cards, a revoke-only "Block AI writes" switch read on every call. The adversarial review argued for pulling a minimal
  "held by AI" display forward: once the prompts go, visibility is the only human check left (§8).

## 8. Decisions for the maintainer

Each has a recommendation.

| Question | Options | Recommendation |
|---|---|---|
| Measure first, or build the kind grants now? | measure first · build alongside | **Measure first** (about an hour, no build). The kind grants do not depend on M1, so they could go first if M1 is delayed. |
| One gameplay grant or two? | one · toggles + movement | **Two.** |
| `write_memory` | per call forever · per call plus `write_field` (Phase 2) · a grant on raw writes | **Per call plus `write_field`.** |
| `write_invoke` | per call forever · a per-function grant (Phase 1b) | **A per-function grant, after M1,** with the §4.4 exclusions. It answers the CE point for invoke without unmarking arbitrary calls. |
| Force field | per call · a class.field grant | **Per call**; revisit if it turns out to be used often. |
| Grant lifetime | session or game run, plus revoke · also an idle timer · a minutes argument | **Session or game run, plus revoke;** a timer only if M9 calls for one. |
| A use cap per invoke grant | none, counted and shown · a fixed cap such as 25 calls | **None, counted and shown:** a cap fights the automation the grant exists for, and each call is already a model turn. |
| Field grants | class-wide · one instance | **Class-wide**, worded so. |
| Other hosts | read-only unless `--writes=any-host` · today's behaviour | **Read-only by default.** |
| Recommend the `write_*` allow rule? | yes · Manual only until M3 · no | **Manual mode only until M3.** |
| "Held by AI" in the UI | with Phase 1 · Phase 3 | Open. The review argued for Phase 1; the judge for Phase 3. |

## 9. Corrections and side findings

- **The server's instructions are wrong in two places** (`McpServer.cs`):
  - "the user is asked before each one" is false on every host but Claude Code;
  - "Everything this server turns on is released when the session ends" is not quite true. Fly's speed, preset and
    noclip are global settings that stay, and an invoke can leave game-side state behind, such as a looping timer.

  todo.md `[MCP-INSTR-CLAIMS]`.
- **The invoke deny list misses indirection.** `K2_SetTimer` and its family, the `Set*PropertyByName` setters and
  `ExecuteUbergraph_*` reach deny-listed effects by name. `Voll.h`'s comment calls `FUNC_Net` the check for "a call that
  can leave the machine", but online-service functions are not RPCs. Today every `write_invoke` is still asked per call,
  so this is LOW until invoke grants exist. todo.md `[INVOKE-DENY-INDIRECTION]`.
- **Background `write_mem` can write the DLL's own data,** such as the exported `g_invokeMailbox`. Writing the mailbox
  hands an invoke to the CE-owned poller, past the background deny list. Each such write is still a per-call prompt; a
  DLL-side refusal of its own image is a small separate item. todo.md `[WRITEMEM-OWN-IMAGE]`.
- **todo.md's "Elicitation is not used: the desktop app's Code tab declines it unseen"** has no measurement behind it.
  Elicitation is still not used, for the reason in §5.
- **The marker floor.** The docs name Claude Code 2.1.199 for the marker. The repo's 2.1.246 is when the marker stopped
  offering "don't ask again" (anthropics/claude-code#89063). Keep 2.1.246 as the floor.
