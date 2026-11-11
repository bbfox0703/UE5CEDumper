# MCP server: let an AI read a running game

`UE5DumpUI.exe --mcp` turns the UI's exe into a [Model Context Protocol](https://modelcontextprotocol.io) server. An
AI client such as Claude Code or Claude Desktop starts it and talks to it over stdin / stdout. The AI can then look
through the game the way you do in the UI: find objects, read their fields, follow pointers, and run value scans.

Started with `--mcp`, the exe opens no window. The AI client starts it and closes it.

> ℹ️ **This version only reads.** It never writes game memory, calls a game function, or turns a feature on.
> Writing tools are planned for a later version, and each will ask you before it runs.

## What it needs

- A game running the UE5Dumper DLL: deployed as a proxy DLL from the UI's **Proxy Deploy** tab, injected from the UI,
  or injected through the bundled Cheat Engine table.
- `UE5DumpUI.exe` from the same release as the DLL. An older DLL answers newer commands with "Unknown command", and
  `read_status` says when the two builds differ.

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

**Skip the prompts for reads (optional).** Every tool that only reads has a name that starts with `read_`. In Claude
Code, one allow rule in `settings.json` admits all of them. In the default permission mode anything else still asks
first. Auto mode decides on its own and may run `scan_start` without asking (measured).

```json
{ "permissions": { "allow": ["mcp__ue5cedumper__read_*"] } }
```

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
- **The UI does not see the AI's work.** Results the AI reads appear only in its own conversation.

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

Addresses go in and come out as hex strings (`"0x7FF6A1B2C3D0"`). Lists come in small pages. A reply that would be
too long for the AI is cut by whole entries, and a note says how much was left out.

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
| "... did not answer within N s" | The game is paused, loading or very large; the next call reconnects. |

The server writes its log to `%LOCALAPPDATA%\UE5CEDumper\Logs\UE5DumpUI\mcp-<date>-<time>-<pid>.log`, one file per
server; files older than 21 days are deleted.

## For developers

- The code is `ui/UE5DumpUI/Mcp/`. The protocol is hand-written JSON-RPC 2.0 for the 2025-11-25 revision, with no SDK
  added to the AOT exe. A client on the 2026-07-28 revision first sends `server/discover`; the server answers with an
  error, and the client falls back to the handshake.
- ⚠ **Register a copy, not `dist\UE5DumpUI.exe`, on a machine that builds this repository.** Claude Code keeps
  every registered server running for the whole session, and a running exe in `dist\` makes the next publish fail
  to replace it. In `--mcp` mode the exe needs none of the DLLs beside it, so copying the exe alone is enough.
- `py tools/verify/mcp_client.py probe --exe dist/UE5DumpUI.exe` checks the protocol with no game running. Add
  `live --pid N --suspend-name DumperTest` to call every tool against a running fixture.
- The design decisions, and what comes next, are in [todo.md](todo.md) under `[PIPE-PER-PROCESS-2026-11-06]` Stage D.
