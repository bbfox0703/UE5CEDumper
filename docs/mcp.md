# MCP server: let an AI read a running game

`UE5DumpUI.exe --mcp` turns the UI's exe into a [Model Context Protocol](https://modelcontextprotocol.io) server. An
AI client such as Claude Code or Claude Desktop starts it and talks to it over stdin / stdout. The AI can then look
through the game the way you do in the UI: find objects, read their fields, follow pointers, and run value scans. It
can also turn the gameplay features on and off, teleport the player, write memory and call a game function. Claude Code
asks you first: once per game run for the gameplay toggles and once for movement, and before every memory write,
function call and forced field. Any other AI client only reads, unless you let it write.

Started with `--mcp`, the exe opens no window. The AI client starts it and closes it.

> ℹ️ **What the AI can change.** The gameplay toggles (god mode, movement, gravity direction, time dilation, debug
> camera, mouse cursor, and the experimental Fly, Keep Foreground, See-through and force field), a teleport to
> explicit coordinates, data in memory (never code or read-only memory), and a call to a UFunction on the game thread
> (some kinds are refused). See [Changing the game](#changing-the-game).

## What it needs

- A game running the UE5Dumper DLL: deployed as a proxy DLL from the UI's **Proxy Deploy** tab, injected from the UI,
  or injected through the bundled Cheat Engine table.
- `UE5DumpUI.exe` from the same release as the DLL. An older DLL answers newer commands with "Unknown command", and
  `read_status` says when the two builds differ.

Every release carries `MCP-Setup.html` beside the exe (source: `scripts/MCP-Setup.html`): the setup steps a user
needs, including using the server over SSH. This page adds the tool list and the developer notes.

## Register it

Use the full path to `UE5DumpUI.exe`.

**Claude Code** (every project of yours):

```bash
claude mcp add --scope user ue5cedumper -- "C:\Tools\UE5CEDumper\UE5DumpUI.exe" --mcp
```

**Claude Desktop**: add it to `%APPDATA%\Claude\claude_desktop_config.json`, then restart Claude Desktop.

```json
{
  "mcpServers": {
    "ue5cedumper": {
      "command": "C:\\Tools\\UE5CEDumper\\UE5DumpUI.exe",
      "args": ["--mcp"]
    }
  }
}
```

**Other AI clients only read, unless you say so.** The server lists its write tools only to Claude Code 2.1.246 or
later, whose prompt is how you approve a grant. Claude Desktop and every other client get the `read_*` tools and
`scan_start`. To let one of them write, add `--writes=any-host` to that client's own registration:

```json
"args": ["--mcp", "--writes=any-host"]
```

> ⚠️ **On a client that never asks, or that you set to always allow, the AI then grants itself.** The grant tools and
> every write run with no prompt. Use `--writes=any-host` only with a client that asks before each tool call.

`--writes=off` turns every write off, for Claude Code too. Any other `--writes=` value, or any other argument, stops
the server at once, and the client reports that it failed to start. An application built on the Claude Agent SDK can
introduce itself as Claude Code and approve tools in code: treat one as a client that never asks.

**Skip the prompts for reads (optional).** Every tool that only reads has a name that starts with `read_`. In Claude
Code, one allow rule in `settings.json` admits all of them. In the default permission mode anything else still asks
first. Auto mode decides on its own and may run `scan_start` without asking (measured).

```json
{ "permissions": { "allow": ["mcp__ue5cedumper__read_*"] } }
```

**Skip the prompts for granted writes (optional, default permission mode only).** In the default mode Claude Code also
asks before each write a grant already covers, because it asks before any tool it has no rule for. This rule stops
that. The server still refuses every write no grant covers, and the grant tools, `write_memory`, `write_invoke` and
`write_force_field` keep asking, since an allow rule never skips their prompt.

```json
{ "permissions": { "allow": ["mcp__ue5cedumper__read_*", "mcp__ue5cedumper__write_*",
  "mcp__ue5cedumper__revoke_grants"] } }
```

Leave it out in auto mode for now: there, auto mode's own check of each write against what you asked for is one more
look. To be asked before every write again, in every mode, add `"ask": ["mcp__ue5cedumper__write_*"]`.

**Keep subagents from changing the game (recommended in Claude Code).** A subagent or workflow agent the AI starts
calls the same server and holds the grants you gave the main conversation, and its grant prompt looks just like the
main one's. This hook in your user-level `~/.claude/settings.json` refuses their writes and grants, and leaves their
reads alone; the exe itself is the hook (`--mcp-agent-guard`), so nothing else needs installing. Use your own path:

```json
{
  "hooks": {
    "PreToolUse": [
      {
        "matcher": "^mcp__ue5cedumper__(write|grant)_.*$",
        "hooks": [{ "type": "command", "command": "\"C:/Tools/UE5CEDumper/UE5DumpUI.exe\" --mcp-agent-guard" }]
      }
    ]
  }
}
```

Claude Code reads hooks when a session starts. If the exe is not at that path, the hook fails to run and the call goes
on without it, so update the path when you move the exe.

## Using it beside the UI

The server and the UI are separate processes and can run together. Each AI session starts its own server.

- **Separate connections.** Each server holds one connection to the game; the UI holds two. The DLL serves 6 at
  once.
- **Separate value scans.** A value scan belongs to the connection that started it, so the AI's scans never end or
  limit yours, and yours never end or limit the AI's.
- **Your scans keep their speed.** The AI's heavy walks (value scans, Find References, searches) run in the
  background: each takes at most half of the threads that are free. A value scan or snapshot you start in the UI
  while one runs still gets the other half. This needs the DLL from the same release.
- **Nothing shared is changed.** The server leaves out every command that changes state another client uses. It
  starts a scan only on a game whose scan has never run.
- **Your holds stay yours.** A feature you turned on in the UI or in a Cheat Engine record is never replaced or
  turned off by the AI; see [Changing the game](#changing-the-game).
- **The UI does not see the AI's work.** Results the AI reads appear only in its own conversation.
- **Stop the AI from the window.** Tick **Block AI writes** at the top of the Teleport tab: every AI session is then
  refused every change to the game (it can still turn off or reset what it holds), and every grant it was given ends.
  Then use a card's OFF or Reset to take back what an AI holds; your own changes always win over the AI's. The switch
  is per Windows user (`%LOCALAPPDATA%\UE5CEDumper\ai-writes.json`): a server started over SSH under another account
  does not see it. If that file cannot be read, the servers refuse every change too.

## The tools

| Tool | What it does |
|---|---|
| `read_games` | The games running the DLL (pid, exe, window title). Pass `pid` to the other tools when there are several. |
| `read_status` | Engine globals, UE version, object count, DLL build, and whether the object scan is done. Start here. |
| `scan_start` | Starts the object scan on a game whose scan has not run (a proxy-loaded game waits for one). Asks first. |
| `read_search_objects` | Keyword search over object and class names. |
| `read_find_object` | A full object path or name to its address. |
| `read_find_instances` | Objects of a class (templates included), with a class histogram. |
| `read_find_live_instance` | One live object of a class or subclass. |
| `read_object` | Name, path, class and outer of an address. |
| `read_find_by_address` | The object that contains an address, and the offset into it. |
| `read_list_classes` | Classes with their path, super class and a game-first score; `name_filter` narrows them. |
| `read_class` | A class or struct layout: fields, types, offsets. |
| `read_functions` | A class's UFunctions with their parameters. |
| `read_instance` | An object's live field values. |
| `read_array` | A page of one array field's elements. |
| `read_memory` | Raw bytes at an address (up to 4 KB). |
| `read_world` | The actors in the world's persistent level, with their components. |
| `read_find_refs` | Objects holding a pointer to an object. |
| `read_path_from_root` | The pointer chain from GWorld (or the engine) to an object. |
| `read_related` | An object's owned sub-objects, controller or pawn, class and outer. |
| `read_current_target` | Ranked guesses at the player's current target. |
| `read_search_properties` | Classes with a property of a given name. |
| `read_property_xrefs` | Blueprint functions that read or write a property. |
| `read_function_props` | The properties one function touches. |
| `read_function_code` | A native function's code address. |
| `read_functions_by_class` | Functions that take or return a class. |
| `read_list_functions` | Every UFunction (heavy; pass a `name_filter`). |
| `read_list_enums` | Every enum with its entries. |
| `read_ce_pointer` | A Cheat Engine pointer (module + offsets) to an object or a field. |
| `read_datatable_rows` | A page of a DataTable's rows. |
| `read_value_scan_begin` | Cheat Engine-style first scan over UPROPERTY fields; opens a session. |
| `read_value_scan_refine` | Next scan on a session (exact, changed, increased...). |
| `read_value_scan_results` | A page of a session's results, filtered or sorted. |
| `read_value_scan_end` | Ends a session. |
| `read_pipe_clients` | The programs connected to the game's pipe. |
| `read_offsets` | The engine layout offsets the DLL measured. |
| `read_holds` | Each gameplay hold (god mode, movement knobs, time dilation, fly, force-field jobs...): in force or not, and who holds it. |
| `read_pose` | The player pawn's location, rotation and map. |
| `read_movement` | Walk speed, gravity scale, jump velocity and gravity direction: value, base, multiplier, held or not. |
| `read_time_dilation` | World and pawn time dilation: value, natural value, held or not. |
| `read_forced_fields` | The force-field jobs. |

Addresses go in and come out as hex strings (`"0x7FF6A1B2C3D0"`). Lists come in small pages. A reply that would be
too long for the AI is cut by whole entries, and a note says how much was left out.

## Changing the game

| Tool | What it does |
|---|---|
| `write_god_mode` | God mode on or off (holds the player's `bCanBeDamaged` at false). |
| `write_movement` | Holds walk speed, gravity scale or jump at the base times a multiplier, or resets it. |
| `write_gravity_direction` | Holds the gravity direction at a vector (UE 5.3+), or resets it. |
| `write_time_dilation` | Holds world or pawn time dilation at a value, or resets it. |
| `write_debug_camera` | The engine's debug camera on or off (needs a CheatManager). |
| `write_mouse_cursor` | Shows or hides the game's mouse cursor once; nothing holds it. |
| `write_fly` | Experimental. Fly on or off, its speed, key preset and noclip. |
| `write_foreground_lock` | Experimental. Keep Foreground on or off. |
| `write_see_through` | Experimental. See-through on or off. |
| `write_force_field` | Experimental. Holds a bool or numeric field on every live instance of a class. |
| `write_force_field_reset` | Experimental. Releases one force-field job the AI holds. |
| `write_teleport` | Moves the player to explicit coordinates and returns the pose it left. |
| `write_memory` | Writes up to 4 KB of data at an address, if it still holds the bytes the AI read; returns before and after. |
| `write_invoke` | Calls a UFunction on a live object on the game thread, parameters by name; returns its return value and out parameters. |
| `write_invoke_granted_function` | Calls a UFunction an invoke grant names, as `write_invoke` does, without asking again (build 3717+). |
| `write_field` | Writes the one field a field grant names on a live instance of its class or a subclass, without asking again; checks the bytes it read first. |
| `grant_god_mode_camera_cursor_time_see_through_foreground` | Asks you once per game run to let the AI turn god mode, the debug camera, the mouse cursor, time dilation, See-through and Keep Foreground on and off. |
| `grant_movement_fly_teleport_may_kill_or_lose_pawn` | Asks you once per game run to let the AI change movement, gravity direction and Fly, and teleport the player. This may kill or lose the pawn. |
| `grant_invoke_one_function_any_object_and_args` | Asks you once per game run to let the AI call one UFunction, named by its function and class, on any object with any arguments (build 3717+). |
| `grant_write_field_one_property_every_instance` | Asks you once per game run to let the AI write one number, bool or enum field of one class, on every live instance of it and its subclasses (NPCs included), with any value its type allows. |
| `read_grants` | This session's grants: the game each covers, the function or field a grant names, and how many writes each let through. |
| `revoke_grants` | Ends the AI's grants: one kind, one function's or one field's grant, or all. It releases no hold. |

- **Asked once per grant, in Claude Code.** The gameplay toggles (god mode, debug camera, mouse cursor, time dilation,
  See-through, Keep Foreground) need the toggles grant, `grant_god_mode_camera_cursor_time_see_through_foreground`.
  Movement, gravity direction, Fly and teleport need the movement grant,
  `grant_movement_fly_teleport_may_kill_or_lose_pawn`. Claude Code asks you before each grant, in every permission
  mode, and shows the grant's fixed sentence: what it covers and how long it lasts. The AI is told to ask for one only
  after you asked for such a change.
- **What ends a grant.** The game exiting or restarting, the AI session ending or its server reconnecting (`/mcp`), or
  `revoke_grants`. A dropped pipe connection to the same game keeps it: the AI's holds are released then, and it may
  take them again without asking. `read_grants` shows the grants and how many writes each let through.
- **Asked every call.** `write_memory`, `write_invoke` and `write_force_field` take no grant and ask before each call.
- **One function, asked once** (build 3717+). `grant_invoke_one_function_any_object_and_args` names one UFunction by its
  function and its class's full path, as `read_functions` shows it in `full` (`//Script/Engine/Actor` and
  `SetActorHiddenInGame`). After you approve it, `write_invoke_granted_function` calls that function on any object, with
  any arguments, until the game run or the session ends. A Blueprint override or another class's function of the same
  name needs its own grant. The server refuses the grant for a function the DLL refuses the AI (the list below), one
  whose arguments reach other functions or properties by name (a timer by name, a delegate, a latent call, a parameter
  named for a function, an event or a property), one taking an FText, a network RPC and a Blueprint event graph; the
  DLL checks the same at every call, and refuses a call under a grant of a non-static function on a class default or a
  template. `write_invoke` still asks every call, for anything else.
- **No grant to end its own holds.** Turning off or resetting what the AI itself turned on needs no grant, so it can
  always clean up; the server checks whose hold it is right before sending. Turning off what the AI did not turn on (the
  game's own invulnerability, another program's change) is a change and needs the grant, as does an OFF that also
  changes a setting (Fly's speed, preset or noclip). The AI's See-through OFF ends only its own claim, so See-through
  stays on while you or a Cheat Engine record also have it on (DLL 3714 or later; an older DLL refuses it).
- **The server refuses what no grant covers**, naming the grant to ask for, so a write nobody approved never reaches
  the game. It also refuses a value outside the UI's own range rather than letting the DLL clamp it: time dilation up
  to 3 (world) and 10 (pawn), walk speed and gravity 0.1 to 10 times, jump 10% to 30 times the height, Fly 50 to
  20000, See-through 1 to 10.
- **Only Claude Code reads the prompt marker.** Another client only reads unless its registration adds
  `--writes=any-host`, and it then asks or not by its own settings (see [Register it](#register-it)).
- **Never over you.** When the UI, a Cheat Engine record or another AI session holds a feature, the AI's change is
  refused and the reply names the holder. The DLL decides this under the feature's own lock, so a change you make at
  the same moment cannot be overwritten. The AI never releases every forced field at once, and See-through stays on
  while you also have it on.
- **Released when the AI session ends.** The features the AI holds are turned off when its server exits, and what you
  turned on stays. If the game is frozen at that moment (a long load, a debugger), the AI's debug camera is turned off
  once the game runs again, unless you take the camera first. Not undone: Fly's speed, preset and noclip settings, a
  teleport, and whatever a `write_invoke` or `write_memory` changed.
- **Your teleport markers are yours.** `write_teleport` goes only to coordinates it is given: it never reads, saves or
  clears a marker slot, and **Recall last** in the UI still returns you to where you were before your own last
  teleport.
- **The experimental features follow the UI's switch.** Fly, Keep Foreground, See-through and the force field refuse
  while the UI's experimental features are off. The switch is read on every call.
- **Memory writes are data only.** The DLL refuses code, read-only, guard and uncommitted memory for every program
  (the UI's field edits included) and never changes a page's protection. The AI sends the bytes it read; if you, the
  game or another program changed them since, nothing is written. A field one of the holds above keeps is put back by
  it.
- **Calls never jump the queue.** Each call the game thread runs is recorded with the program that sent it. The AI's
  call is refused while one from the UI, a Cheat Engine record or another AI session is still queued or running, a
  call that timed out included; yours always queue. The same holds for the game-thread calls the gameplay tools make on
  their way (the debug camera's toggle, Fly's collision restore). A call the game thread did not run in time stays
  queued and runs later, unless the AI session ends first: it is then dropped. The AI never sends it again, never calls
  off the game thread, and never changes the call timeout.
- **Calls the AI may not make**, refused by the DLL: network RPCs, console commands, quitting or travelling to another
  level, destroying actors or removing a player, writing or deleting save slots, the debug camera (it goes through
  `write_debug_camera`), and since 2026-11-16 the calls that reach those effects by name or leave the machine without
  being network RPCs: timers and property setters that take a name (`K2_SetTimer*`, `Set*PropertyByName`), a
  Blueprint's event-graph entry (`ExecuteUbergraph_*`), and achievement, leaderboard and in-app purchase calls. Build
  3717 refuses a latent call too (a `LatentActionInfo` parameter names the function the engine calls when it finishes;
  `Delay`, `MoveComponentTo`, `LoadAsset`), and adds what a scan of the UE 4.18-5.8 sources found: UE4's `WriteAchievementProgress`, an async action's
  `Activate` (the online and save actions), `RemoteEvent`, `SetBind`, the timeline's `AddInterp*`,
  `EditTextPropertySourceString`, a streaming level's or level instance's load and unload (and the cheat manager's
  `StreamLevelIn` / `StreamLevelOut` / `OnlyLoadLevel`), and the save slot's local-player twins.
  The list goes by name; a game function that does the same under another name is not caught, and the prompt is then
  your check. The server itself refuses a function taking an FText, a value that does not parse as its parameter's
  type, and an object parameter that is not a live object.
- **The DLL must be build 3705 or later** for the gameplay tools, and **3706 or later** for `write_memory`,
  `write_invoke` and `write_debug_camera`. An older DLL cannot keep the AI's change off yours, so the tool refuses it and
  says to update the DLL.

## When something goes wrong

| The AI reports | What to do |
|---|---|
| "No game is running the UE5Dumper DLL" | Start the game with the proxy deployed, or inject the DLL. |
| "The game is not scanned yet" | Load into the game world; the AI can then call `scan_start`. |
| "Several games run the UE5Dumper DLL: pass pid on every call" | The AI picks one from `read_games` and names it on every call (an address belongs to one game); you can tell it which. |
| "The game's object scan is still running" | Wait: the scan validates its offsets before object tools answer. |
| "The game's scan finished without finding the object pool" | Run **Extra Scan** in the UE5DumpUI window; scanning again from the AI finds nothing more. |
| "Only an old UE5Dumper DLL's pipe is up" | Close the game, run **Update All** on the Proxy Deploy tab, start it again. |
| "Every connection to game N's pipe is taken" | Close a program holding one; the message names them. |
| "Windows refused the connection to game N: the game runs as administrator" | Start the AI client elevated too, or start the game normally. A server started over SSH runs elevated for an administrator account. |
| "Windows refused the connection to game N although this server runs elevated" | Another process probably created that game's pipe name first. Close it, or restart the game. |
| "... did not answer within N s" | The game is paused, loading or very large; the next call reconnects. |
| "Not changed: no grant covers this for this game. Call grant_..." | The AI asks you for that grant first; Claude Code shows you what it covers. |
| "Not sent: the connection now reaches another game run ..." | The game restarted between the AI's check and its write; a grant for the old run does not carry over. |
| "'...' must be from X to Y" | The value is outside the UI's own range for it. |
| The AI has no `write_*` tools | Its client is not Claude Code 2.1.246 or later. Update Claude Code, or add `--writes=any-host` to that client's registration (read the warning there first). |
| "Not changed: ... is held by ..." | You (or the named program) hold that feature. Release it where it was set if you want the AI to take it. |
| "... is an experimental feature, and the UE5DumpUI window's experimental features are off" | Turn them on in the UI if you want the AI to use them. |
| "The game's DLL is build N, older than 3705" (or 3706) | Run **Update All** on the Proxy Deploy tab and restart the game, or inject the new DLL. |
| "Not written: ... changed since it was read" | You, the game or another program changed the memory after the AI read it; the reply shows what is there now. |
| "Not invoked: ... has an invoke still queued or running" | A call you made has not run yet; the AI can call again once it has (`read_holds` lists outstanding calls). |
| "Not invoked: this server's previous invoke ... is still queued" | The game thread has not run the AI's last call; it runs when the game responds, or is dropped when the session ends. |

The server writes its log to `%LOCALAPPDATA%\UE5CEDumper\Logs\UE5DumpUI\mcp-<date>-<time>-<pid>.log`, one file per
server; files older than 21 days are deleted.

## For developers

- The code is `ui/UE5DumpUI/Mcp/`. The protocol is hand-written JSON-RPC 2.0 for the 2025-11-25 revision, with no SDK
  added to the AOT exe. A client on the 2026-07-28 revision first sends `server/discover`; the server answers with an
  error, and the client falls back to the handshake.
- ⚠ **Register a copy, not `dist\UE5DumpUI.exe`, on a machine that builds this repository.** Claude Code keeps
  every registered server running for the whole session, and a running exe in `dist\` makes the next publish fail
  to replace it. In `--mcp` mode the exe needs none of the DLLs beside it, so copying the exe alone is enough.
- `py tools/verify/mcp_client.py probe --exe dist/UE5DumpUI.exe` checks the protocol and which client gets writes,
  with no game running. Add `live --pid N --suspend-name DumperTest` to call every tool against a running fixture.
- **The write grants** ([MCP-WRITE-GRANTS-2026-11-14]): `McpGrants.cs` decides at `initialize` which client writes,
  classes every DLL command and keeps the grants, keyed on the game run (pid and process start time). Every send
  passes its check inside `McpGameLink`, so a tool that forgets to ask for admission fails closed. The design, the
  measurements behind it and what was rejected are in [mcp-write-approval-eval.md](mcp-write-approval-eval.md).
- The design decisions, and what comes next, are in [todo.md](todo.md) under `[PIPE-PER-PROCESS-2026-11-06]` Stage D.
