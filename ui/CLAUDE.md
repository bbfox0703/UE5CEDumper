# CLAUDE.md — ui/

Rules that apply to the C# UI under this folder; they load when you work here. The rules for the whole repository are in the [root CLAUDE.md](../CLAUDE.md).

## Rules

- **Single Instance**: UI app uses Mutex to ensure only one instance runs
- **Async everywhere**: All I/O, pipe operations, and alert actions must be async
- **Platform Abstraction**: Any system/OS-dependent call (P/Invoke, Registry, OS commands) MUST go through an interface in `Core` project. `Core` must NEVER contain direct platform-specific code
- **App-data layout**: the `%LOCALAPPDATA%\UE5CEDumper` root holds ONLY files that are **app-wide and fixed in number** (`Constants.cs` enumerates them). Anything **per-game** — one more set on every game patch, forever — gets its own PascalCase subfolder beside `Logs\` / `Reports\`: today `Snapshots\`, `Bookmarks\`, `TeleportCoords\`. A new per-game family MUST add a subfolder, not a root file, via `Services/AppDataFolderMaintenance.Prepare` called **from the store's constructor** — a composition-root call site is one reorder away from silently reading the folder before it is migrated. Three invariants, each with its measurement in `AppDataFolderMaintenance.cs` / `Constants.cs`: a game's files **move and expire as a GROUP**; **"unused" = `LastWriteTimeUtc`, STAMPED by the store on use** — never last-access, which every AV/backup/indexer read refreshes; and **retention is the STORE's call, not the folder's** (`Snapshots\` 21 days; `Bookmarks\` and `TeleportCoords\` pass `0` — sweep off deliberately, do not "finish" it by enabling it)
- **UI Strings**: English only. All UI strings in `Resources/Strings/en.axaml`, referenced via `StaticResource` bindings
- **Keyword search boxes (space = AND + per-keyword memory)**: Every client-side keyword/filter box in the UI MUST behave identically:
  - **space = AND**: split with `Helpers/ObjectTreeFilter.SplitTerms`, match with `MatchesAllTerms` (term-level AND, field-level OR) — never one `.Contains`/`IndexOf` over a concatenated string. Server-side matchers that can't AND client-side (ValueSearch, SPC query-time) are the only exemption.
  - **per-keyword memory**: `Helpers/KeywordSearchMemory` — remember only keywords the user typed *and that matched*. Its header carries the 4-line VM wiring and the `TextBox`→`AutoCompleteBox` AXAML swap verbatim; `Flush()` before clearing the box on tab-switch/navigation.
  - **an async / server-side count calls `Commit()`, never `Schedule()`** — a debounce probe races the async reload and reads a stale count.
- **AOT compatible**: All C# code must be Native AOT / trimming compatible. No reflection-based APIs — use source generators instead (e.g. `[JsonSerializable]` context for `System.Text.Json`, `[ObservableProperty]` for MVVM). The UI is published as a self-contained trimmed binary
