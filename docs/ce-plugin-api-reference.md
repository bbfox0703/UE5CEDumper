# Cheat Engine Plugin SDK — C++ API Reference

> **Why this doc is in this repo.** UE5CEDumper does not build a CE plugin today — it is an
> injected C++ DLL plus an Avalonia UI that *generates* CE artifacts (AA scripts with `{$lua}`
> blocks, `.CT` cheat tables, CE XML pointer chains, Structure Dissect files) and talks to the
> **AOBMaker CE plugin** over a named pipe (see [aobmaker-integration.md](aobmaker-integration.md)).
> This reference is kept here because that pipe partner *is* a CE plugin, because several facts
> pinned down below already govern what this repo emits (CE's real `TVariableType` ordering, the
> `pluginsync` threading model, the disassembler/symbol output formats), and because shipping our
> own plugin is a plausible future. **This is a mirror, not the master.**
>
> **Master copy:** `<private-ce-repo>/docs/CE-Plugin-API-Reference.md` — edit there
> first, then mirror here.
>
> ⚠ **The numbers in this document are DERIVED from an EXTERNAL tree, and this repo's CI cannot
> guard them.** `tools/check_derived_counts.py` only derives from the UE5CEDumper tree; every count
> and every `file:line` here comes from `D:\Github\cheat-engine`. They must be re-derived by hand
> with the commands given in the Appendix.

Comprehensive API reference for developing Cheat Engine plugins in C/C++.
Derived from CE source code (`plugin.pas`, `pluginexports.pas`, `cepluginsdk.h`).

> **SDK Version:** `CESDK_VERSION = 6` (`cepluginsdk.h:16`) — unchanged in 7.7
> **Source read for this reference:** CE source tree `D:\Github\cheat-engine` (Delphi/FPC), tag
> **`7.5-195`**, HEAD `4178e037`, level with `upstream/master`
> **Binaries cross-checked:** `C:\Program Files\Cheat Engine\` — CE **7.7.0.10568** (ProductVersion 7.7)
> **7.5 vs 7.7:** the source read is 7.5 while the shipping binary is 7.7, and the plugin SDK did not
> change between them. `plugin/cepluginsdk.pas` is byte-identical; `cepluginsdk.h` differs on exactly
> one line — the `PLUGINTYPE0_RECORD.valuetype` comment (§2 Type 0, §11). Every `ExportedFunctions`
> member, every typedef and every `*_INIT` struct in this document is identical in both, so one
> plugin binary works on both. See the Appendix for how to regenerate that claim.
> **Companion doc:** [ce-plugin-sdk-notes.md](ce-plugin-sdk-notes.md) — pitfalls and known traps

---

## Table of Contents

1. [Plugin Lifecycle](#1-plugin-lifecycle)
2. [Plugin Types (0–8)](#2-plugin-types-08)
3. [ExportedFunctions Struct Layout](#3-exportedfunctions-struct-layout)
4. [Core Functions (Version 1)](#4-core-functions-version-1)
5. [Symbol & Module Functions (Version 2–3)](#5-symbol--module-functions-version-23)
6. [Memory Record Functions (Version 4)](#6-memory-record-functions-version-4)
7. [Debug Functions (Version 4)](#7-debug-functions-version-4)
8. [UI Creation Functions (Version 4)](#8-ui-creation-functions-version-4)
9. [Advanced Functions (Version 5)](#9-advanced-functions-version-5)
10. [Windows API Pointers](#10-windows-api-pointers)
11. [Enums & Constants](#11-enums--constants)
12. [Data Structures](#12-data-structures)
13. [Thread Safety Model](#13-thread-safety-model)
14. [Quick-Start Template](#14-quick-start-template)
15. [Common Pitfalls Checklist](#15-common-pitfalls-checklist)

---

## 1. Plugin Lifecycle

### 1.1 Required DLL Exports

Every plugin DLL must export exactly 3 functions (prefer `CEPlugin_` prefix):

```cpp
// Called first — CE validates version and gets plugin name
BOOL __stdcall CEPlugin_GetVersion(PPluginVersion pv, int sizeofpluginversion);

// Called when plugin is enabled — receive exported functions and register callbacks
BOOL __stdcall CEPlugin_InitializePlugin(PExportedFunctions ef, int pluginid);

// Called when plugin is disabled — unregister all callbacks
BOOL __stdcall CEPlugin_DisablePlugin(void);
```

### 1.2 Loading Sequence (CE internals)

`TPluginHandler.LoadPlugin` (`plugin.pas:1588`):

```
1. LoadLibrary(plugindll)   (retried once via AddDllDirectory on failure)  [:1624, :1626-1630]
2. GetProcAddress("CEPlugin_GetVersion")       — fallback: "GetVersion"    [:1638, :1640]
3. Call GetVersion(pv, sizeof(PluginVersion))                              [:1644]
4. Reject if pv->version > CurrentPluginVersion (6)                        [:1646]
5. Store plugin name from pv->pluginname                                   [:1658]
6. GetProcAddress("CEPlugin_InitializePlugin") — fallback: "InitializePlugin" [:1665-1667]
7. GetProcAddress("CEPlugin_DisablePlugin")    — fallback: "DisablePlugin"    [:1669-1671]
8. nextid = 1; raise if either export is missing                           [:1674, :1676-1677]
```

`sizeof(TPluginVersion)` is a `dword` + a `pchar` (`plugin.pas:22-25`, `cepluginsdk.h:21-25`), so
16 bytes on x64 and 8 on x86 — **derived** from those two declarations plus standard alignment, not
measured.

> Note the order: in `LoadPlugin` your `GetVersion` is called **before** CE checks that the other two
> exports exist (`plugin.pas:1644` vs `:1676-1677`, where the two `raise`s live). Only the name-probe
> path below checks all three first (`GetPluginName`, `plugin.pas:1519-1520`).

> **TRAP — CE opens your DLL twice, and throws the first pass away.** When the user picks a plugin
> with the Settings "Add" button, CE first runs `TPluginHandler.GetPluginName`
> (`formsettingsunit.pas:2156` → `plugin.pas:1480`): LoadLibrary (`:1497`) → resolve the three
> exports (`:1511`, `:1519`, `:1520`) → `GetVersion(PluginVersion, sizeof(TPluginVersion))`
> (`:1522`) → **FreeLibrary** (`:1525`). The real `LoadPlugin` only runs later, when the settings are
> applied (`formsettingsunit.pas:1130`). So your `DllMain(PROCESS_ATTACH)` and `GetVersion` run once
> against a module that is immediately unloaded. Never initialise global state in `GetVersion` — do
> it in `InitializePlugin`.

### 1.3 Enable Sequence

```
1. CE copies its ExportedFunctions to a local variable
2. Sets sizeofExportedFunctions based on plugin's declared version:
     Version 1   → sizeof(TExportedFunctions1)
     Version 2   → sizeof(TExportedFunctions2)
     Version 3   → sizeof(TExportedFunctions3)
     Version 4   → sizeof(TExportedFunctions4)
     Version 5-6 → sizeof(TExportedFunctions5)
3. Calls InitializePlugin(&exportedFunctions, pluginid)
4. Plugin should copy ExportedFunctions and register callbacks
```

The mapping is the `case plugins[pluginid].pluginversion of` at `plugin.pas:1372-1388`; anything
other than 1–5 (i.e. version 6) falls into the `else` and gets `sizeof(TExportedFunctions)`, which is
`TExportedFunctions5` by the alias at `plugin.pas:230`.

> **Note:** `TExportedFunctions1` is **not** a clean prefix of the full struct. It carries a trailing
> `previousOpcode: pointer` after `memorybrowser` (`plugin.pas:708-710`), so a version-1 plugin is
> handed a `sizeofExportedFunctions` one pointer past the end of the version-1 region — overlapping
> the slot the live struct uses for `sym_nameToAddress`. Member counts are **derived** — v1 = 85,
> v2 = 87, v3 = 95, v4 = 155, v5/v6 = 159. Regenerate rather than hand-edit: the five records start at
> `plugin.pas:47` (v5), `:233` (v4), `:410` (v3), `:522` (v2), `:620` (v1); count field declarations
> in each with `awk 'NR>a && NR<b' plugin.pas | grep -cE '^\s+[A-Za-z_][A-Za-z0-9_]*\s*:'`.
> On the byte figures: 1 `int` + N−1 pointers gives 1272 bytes on x64 / 636 on x86 for v5/v6, derived
> by applying standard alignment to those counts — not measured by compiling.

### 1.4 Disable Sequence

```
1. CE calls DisablePlugin()
2. If it returns FALSE, CE raises "Error disabling <dll>" and STOPS —
   the plugin stays marked enabled and NOTHING is unregistered
3. If it returns TRUE: CE calls unregisterfunction for every remaining
   registered function, types 0 through 8
4. Menu items freed, handler objects destroyed
```

> **TRAP:** `DisablePlugin` must return TRUE. Returning FALSE (`plugin.pas:1433`, message
> `rsErrorDisabling` at `:922`) raises out of `TPluginHandler.DisablePlugin` before
> `enabled:=false` (`:1434`) — so every callback stays registered (the nine per-type
> `unregisterfunction` loops at `:1439-1464` are never reached), every menu item stays live, and CE
> still believes the plugin is enabled. The exception also propagates out of `UnloadPlugin`
> (`plugin.pas:1413`) *before* its `FreeLibrary` at `:1417`, so the DLL is never unloaded either; at
> shutdown `TPluginHandler.Destroy` swallows it in a bare `try…except` (`plugin.pas:1840-1843`) and
> moves on to the next plugin. Net effect: a FALSE return wedges that plugin permanently — nothing
> is torn down and nothing is reported beyond one dialog.

### 1.5 GetVersion Implementation

```cpp
static char g_PluginName[] = "My Plugin";  // MUST be static/global

extern "C" __declspec(dllexport)
BOOL __stdcall CEPlugin_GetVersion(PPluginVersion pv, int sizeofpluginversion) {
    pv->version = CESDK_VERSION;       // 6
    pv->pluginname = g_PluginName;     // MUST NOT be stack variable
    return TRUE;
}
```

> **TRAP:** `sizeofpluginversion` is an `int` value, NOT a pointer. See [ce-plugin-sdk-notes.md](ce-plugin-sdk-notes.md) §1.1.

### 1.6 InitializePlugin Implementation

```cpp
static ExportedFunctions g_Exported;
static int g_PluginId;

extern "C" __declspec(dllexport)
BOOL __stdcall CEPlugin_InitializePlugin(PExportedFunctions ef, int pluginid) {
    // Validate the struct size BEFORE copying (same order as the §14 template)
    if (ef->sizeofExportedFunctions < (int)sizeof(ExportedFunctions))
        return FALSE;  // Header mismatch

    g_Exported = *ef;      // MANDATORY: copy the struct, do not keep `ef`
    g_PluginId = pluginid;

    // Register callbacks here (see §2)
    return TRUE;
}
```

> **TRAP:** `ef` points at a **stack local** of CE's `TPluginHandler.EnablePlugin` — `var e:
> texportedfunctions;` filled by `e:=exportedfunctions;` (`plugin.pas:1365`, `:1369`, comment
> *"save it to prevent plugins from fucking it up"*) and passed by reference at `:1398`
> (`TInitializePlugin=function(var ExportedFunctions: TExportedFunctions; pluginid: dword):BOOL`,
> `plugin.pas:720`). It is deliberately a throwaway so a misbehaving plugin cannot corrupt CE's
> globals, and it is dead the moment `EnablePlugin` returns — storing the `PExportedFunctions` and
> using it later is a dangling-pointer read. The copy is a shallow struct copy; the pointers inside
> stay owned by CE, which is exactly what you want. Never free anything reached through it.

### 1.7 RegisterFunction / UnregisterFunction

```cpp
// Register a callback — returns functionid (≥1), or -1 on error
int id = g_Exported.RegisterFunction(g_PluginId, pluginType, &initStruct);

// Unregister a callback
g_Exported.UnregisterFunction(g_PluginId, id);
```

CE assigns sequential IDs starting from 1 per plugin. Store returned IDs for cleanup.

---

## 2. Plugin Types (0–8)

### Type 0 — Address List Right-Click Menu

Adds a menu item to the address list right-click context menu.

```cpp
typedef BOOL (__stdcall *CEP_PLUGINTYPE0)(PPLUGINTYPE0_RECORD SelectedRecord);

typedef struct {
    char* name;                        // Menu item text (static string)
    CEP_PLUGINTYPE0 callbackroutine;
} PLUGINTYPE0_INIT;
```

**Callback receives:** Selected address record (readable/writable fields).
**Return:** TRUE if changes were made — CE copies your edits back **only** on the TRUE path
(`MainUnit.pas:9755`). See §12 for the full record.

```cpp
BOOL __stdcall OnAddressList(PPLUGINTYPE0_RECORD rec) {
    // rec->interpretedaddress — editable, 255 bytes INCLUDING the NUL (max 254 chars)
    // rec->address            — read-only (resolved numeric)
    // rec->description        — editable, 255 bytes INCLUDING the NUL (max 254 chars)
    // rec->valuetype          — CE TVariableType ordinal, NOT the legend in the 7.5 header:
    //   0=Byte 1=Word(2B) 2=Dword(4B) 3=Qword(8B) 4=Float(single) 5=Double
    //   6=String 7=UnicodeString 8=ArrayOfByte 9=Binary(bit) 10=All
    //   11=AutoAssembler 12=Pointer 13=Custom 14=Grouped 15=ByteArrays 16=CodePageString
    return FALSE;
}
```

> **TRAP — the 7.5 SDK header's `valuetype` legend is wrong from index 3 up.** `cepluginsdk.h:35` in
> the 7.5 source tree reads
> `//0=byte, 1=word, 2=dword, 3=float, 4=double, 5=bit, 6=int64, 7=string`. **CE fixed that comment
> in 7.7** — it is the one and only line on which the two copies of `cepluginsdk.h` differ. The field
> is a raw `TVariableType` ordinal (`commontypedefs.pas:15`), filled at `MainUnit.pas:9743` and
> written straight back into the memory record when your callback returns TRUE (`:9767-9768`).
> Against a 7.5-era header, writing `3` intending "float" silently retypes the cheat-table entry as
> an 8-byte **Qword**, and `5` intending "bit" gives **Double**. Full list in §11 — and note the SDK
> declares no value-type enum in C at all, so you have to write it yourself.

> **TRAP — the two string buffers are 255 bytes *including* the NUL, i.e. 254 usable characters.**
> Both pointers are `@interpretableaddress[1]` / `@description[1]` (`MainUnit.pas:9729`, `:9741`)
> into `string[255]` shortstring **locals** of `TMainForm.plugintype0click`
> (`MainUnit.pas:9708-9709`). Writing 255 characters plus a terminator writes one byte past the
> shortstring, onto CE's stack frame. CE force-terminates at index 255 anyway (`:9758-9759`) before
> measuring with `StrLen` (`:9761-9762`), so an over-long string is silently truncated even when it
> does not corrupt the stack.

> **Note — CE leaks on every Type 0 invocation, and leaks extra when you return FALSE.**
> `TMainForm.plugintype0click` (`MainUnit.pas:9703-9781`) allocates the record with
> `getmem(selectedrecord, sizeof(TPlugin0_SelectedRecord))` (`:9724`) and never frees it, and its
> `freememandnil(offsets)` (`:9771`) sits *inside* the `if x.callback(selectedrecord) then` block
> opened at `:9755` — so `getmem(offsets, selectedrecord^.countoffsets * 4)` at `:9735` leaks whenever
> the callback returns FALSE. Neither leak is fixable from the plugin side, but returning FALSE costs
> an extra `countoffsets*4` bytes per click, so do not use a Type 0 item as a polling hook.

### Type 1 — Memory Browser Menu

Adds a menu item to the Memory Browser's "Plugins" menu.

```cpp
typedef BOOL (__stdcall *CEP_PLUGINTYPE1)(
    UINT_PTR *disassembleraddress,
    UINT_PTR *selected_disassembler_address,
    UINT_PTR *hexviewaddress);

typedef struct {
    char* name;
    CEP_PLUGINTYPE1 callbackroutine;
    char* shortcut;                    // e.g. "Ctrl+Shift+M" or NULL
                                       // read only when pv->version > 1 [plugin.pas:1029]
} PLUGINTYPE1_INIT;
```

**Callback receives:** Pointers to the current addresses — `disassembleraddress` = the disassembler's
**top** (first visible) line, `selected_disassembler_address` = the highlighted line,
`hexviewaddress` = the hex view's address (`MemoryBrowserFormUnit.pas:4569-4571`).
**Return:** **`TRUE` to apply your writes.** CE only copies the three values back into the view when
the callback returns TRUE (gate at `MemoryBrowserFormUnit.pas:4572`, write-back at `:4574-4576`) —
returning FALSE silently discards them.

> **Version note:** the `shortcut` field is only read when the plugin reports version > 1
> (`plugin.pas:1029`, assignment at `:1033`). Types 5 and 6 read it unconditionally
> (`plugin.pas:1104`, `:1139` — same `try/except`, no version guard), so a version-1 plugin gets a
> shortcut on those menus but not on this one.

### Type 2 — Debug Event Handler

Called on every debug event when the debugger is active.

```cpp
typedef int (__stdcall *CEP_PLUGINTYPE2)(LPDEBUG_EVENT DebugEvent);

typedef struct {
    CEP_PLUGINTYPE2 callbackroutine;
} PLUGINTYPE2_INIT;
```

**Return:** `0` = let CE handle normally, `1` = event consumed (you must call ContinueDebugEvent yourself).

> **Thread safety:** Called from debugger thread, NOT the main thread.

### Type 3 — Process Watcher

Called by CE's **kernel Process Watcher** when a process is created or terminated system-wide. This
is *not* CE attaching to a target.

```cpp
typedef void (__stdcall *CEP_PLUGINTYPE3)(
    ULONG processid,
    ULONG peprocess,       // see TRAP — actually a 64-bit EPROCESS on x64
    BOOL Created);         // TRUE=process created, FALSE=process terminated

typedef struct {
    CEP_PLUGINTYPE3 callbackroutine;
} PLUGINTYPE3_INIT;
```

> **Version note:** SDK version 1 plugins receive only `(processid, peprocess)` — no `Created` parameter
> (`plugin.pas:1803-1804`).

> **TRAP — it usually never fires.** The dispatch loop lives in `tprocesswatchthread`
> (`frmProcessWatcherUnit.pas:279`, dispatching at `:324`), which is created only by
> `TfrmProcessWatcher.FormCreate` (`:357-372`, thread started at `:366-367`). CE constructs that form
> in exactly three places, all user-gated: the Settings "Process watcher" checkbox at apply time
> (`formsettingsunit.pas:544-547`), the same checkbox re-read at startup (`MainUnit2.pas:1066-1068`),
> and the Process List window's process-watch button
> (`ProcessWindowUnit.pas:1064-1067`, `TProcessWindow.btnProcessWatchClick`). On top of that
> `FormCreate` calls `loaddbk32` (`:360`) and raises `rsFailedStartingTheProcessWatcher` if
> `StartProcessWatch` fails (`:370`), so the DBK kernel driver must actually start. Absent all of
> that, a registered Type 3 callback receives nothing.

> **TRAP — 64-bit truncation.** CE passes `peprocess` as `ptruint`
> (`plugin.pas:776 TPluginFunction3=function(processid: dword; peprocess:ptruint; created: BOOL)`,
> source field `PEProcess:UINT64` at `frmProcessWatcherUnit.pas:283`). The SDK header's `ULONG`
> (`cepluginsdk.h:43`) keeps only the low 32 bits on x64. Re-declare the parameter as `UINT_PTR`.

> **Thread safety:** runs on the process-watcher worker thread, not the main thread — same
> constraint as Type 2. See §13.

### Type 4 — Function Pointer Change

Called when CE's internal API function pointers are reassigned (e.g., driver loaded).

```cpp
typedef void (__stdcall *CEP_PLUGINTYPE4)(int section);  // which pointer group changed

typedef struct {
    CEP_PLUGINTYPE4 callbackroutine;
} PLUGINTYPE4_INIT;
```

> The parameter is named `reserved` in `cepluginsdk.h:44`, but CE calls it `section`
> (`plugin.pas:787` `TPluginFunction4=function(section: integer):boolean; stdcall;`) and passes a
> meaningful group id — `0` and `3`–`10` are all in use
> (`NewKernelHandler.pas:1964, 1989, 2059, 2085, 2129, 2151, 2170, 2197, 2230, 2278`). Treat it as an
> opaque "which group of API pointers was just reassigned" tag; re-read the pointers you care about
> out of your saved `ExportedFunctions` copy rather than switching on it.

### Type 5 — Main Form Menu

Adds a menu item to the main CE window's "Plugins" menu.

```cpp
typedef void (__stdcall *CEP_PLUGINTYPE5)(void);

typedef struct {
    char* name;
    CEP_PLUGINTYPE5 callbackroutine;
    char* shortcut;                    // e.g. "Ctrl+Alt+P" or NULL
} PLUGINTYPE5_INIT;
```

### Type 6 — Disassembler Context Menu

Adds a menu item to the disassembler right-click context menu. Has two callbacks: one for display (popup) and one for click.

```cpp
// Called when right-click menu opens — decide whether to show the item
typedef BOOL (__stdcall *CEP_PLUGINTYPE6ONPOPUP)(
    UINT_PTR selectedAddress,      // Value, not pointer
    char **addressofname,          // Pointer to menu item name (can modify)
    BOOL *show);                   // Set to FALSE to hide this item

// Called when user clicks the menu item
typedef BOOL (__stdcall *CEP_PLUGINTYPE6)(
    UINT_PTR *selectedAddress);    // Pointer — read/write

typedef struct {
    char* name;
    CEP_PLUGINTYPE6 callbackroutine;
    CEP_PLUGINTYPE6ONPOPUP callbackroutineOnPopup;  // Can be NULL
    char* shortcut;                // e.g. "Ctrl+Shift+A" or NULL
} PLUGINTYPE6_INIT;
```

> **Version note:** SDK version ≤5 popup callback has no `show` parameter
> (`plugin.pas:1751-1752`):
> `typedef BOOL (__stdcall *)(UINT_PTR selectedAddress, char **addressofname);`

> **Both return values are ignored.** For the click callback, CE writes `*selectedAddress` back into
> the disassembler **unconditionally** (`MemoryBrowserFormUnit.pas:4554-4556`) — unlike Type 1, there
> is no "return TRUE to apply". For the popup callback, CE applies `*addressofname` to the menu
> caption and `*show` to its visibility regardless of the result (`plugin.pas:1757-1758`).
> `*show` is pre-initialised to TRUE by CE (`plugin.pas:1748`), and `*addressofname` initially points
> into a Delphi-managed local string (`:1746-1747`) — replace the pointer with your own static buffer
> rather than writing through it.

### Type 7 — Disassembler Line Renderer

Called for every line rendered in the disassembler view. Can modify display strings and colors.

```cpp
typedef void (__stdcall *CEP_PLUGINTYPE7)(
    UINT_PTR address,
    char **addressStringPointer,    // Pointer to address column string
    char **bytestringpointer,       // Pointer to bytes column string
    char **opcodestringpointer,     // Pointer to opcode column string
    char **specialstringpointer,    // Pointer to comment column string
    ULONG *textcolor);              // Pointer to text color (TColor)

typedef struct {
    CEP_PLUGINTYPE7 callbackroutine;
} PLUGINTYPE7_INIT;
```

> **Performance critical:** Called per-line during rendering. Keep callback fast.
> **String pointers:** Each `char**` points at a `PChar` that may be **NULL**. CE nils all four and
> then only sets those whose column string is non-empty
> (`disassemblerviewlinesunit.pas:931-946`, dispatched at `:952`) — the comment column
> (`specialstringpointer`) is empty on most lines, so `*specialstringpointer == NULL` is the common
> case. Always null-check before `strlen`/`printf`. You can replace the pointer to change the
> display, but don't free the original — it points into a Delphi-managed string.

### Type 8 — AutoAssembler Hook

Called during AA script processing phases.

```cpp
typedef void (__stdcall *CEP_PLUGINTYPE8)(
    char **line,                   // Pointer to current line string (modifiable)
    AutoAssemblerPhase phase,      // Current phase (see §11)
    int id);                       // Script execution ID

typedef struct {
    CEP_PLUGINTYPE8 callbackroutine;
} PLUGINTYPE8_INIT;
```

> **Version note:** SDK version ≤5 callback has no `id` parameter (`plugin.pas:1724-1725`).
> **Phases:** Called for **all four** phases — `aaInitialize` (0), `aaPhase1` (1), `aaPhase2` (2),
> `aaFinalize` (3). All four call sites are inside `autoassemble2`: CE uses phase 0 to announce that
> a script is about to be executed (`autoassembler.pas:1855`) and phase 3 to tell plugins to free
> their per-script data (`:4448`); phases 1 and 2 are the per-line passes (`:2061`, `:3572`).
> **TRAP:** `*line` is only valid during phases 1 and 2. On `aaInitialize` it is **NULL**
> (`currentlinep: pchar=nil`, `autoassembler.pas:1537` — never assigned before the phase-0 call at
> `:1855`), and on `aaFinalize` it is either still NULL or a dangling pointer into `autoassemble2`'s
> local `currentline` for whatever line was processed last (the only assignments are `:2059` and
> `:3571`, so a script whose lines were all empty never sets it at all). Treat `*line` as valid only
> when `phase == aaPhase1 || phase == aaPhase2`.
> **Thread safety:** multiple AA scripts can run concurrently; `id` is a per-script
> `InterLockedIncrement` counter (`autoassembler.pas:1854`), so key any per-script state on `id` and
> do your own locking — CE says so itself at `autoassembler.pas:3575`: *"note that this can be called
> in a multithreaded situation, so the plugin must hld storage containers on a threadid base and
> handle the locking itself"*.

---

## 3. ExportedFunctions Struct Layout

Complete struct with all fields in exact order. **Do NOT reorder, add, or remove any field.**

```cpp
typedef struct _ExportedFunctions
{
    //=== Meta ===
    int sizeofExportedFunctions;

    //=== Version 1: Core ===
    CEP_SHOWMESSAGE              ShowMessage;            // void(char*)
    CEP_REGISTERFUNCTION         RegisterFunction;       // int(pluginid, type, init*)
    CEP_UNREGISTERFUNCTION       UnregisterFunction;     // BOOL(pluginid, funcid)
    PULONG                       OpenedProcessID;        // *DWORD (dereference to read)
    PHANDLE                      OpenedProcessHandle;    // *HANDLE (dereference to read)

    CEP_GETMAINWINDOWHANDLE      GetMainWindowHandle;
    CEP_AUTOASSEMBLE             AutoAssemble;
    CEP_ASSEMBLER                Assembler;
    CEP_DISASSEMBLER             Disassembler;
    CEP_CHANGEREGATADDRESS       ChangeRegistersAtAddress;
    CEP_INJECTDLL                InjectDLL;
    CEP_FREEZEMEM                FreezeMem;
    CEP_UNFREEZEMEM              UnfreezeMem;
    CEP_FIXMEM                   FixMem;                 // ALWAYS NULL (obsolete)
    CEP_PROCESSLIST              ProcessList;
    CEP_RELOADSETTINGS           ReloadSettings;
    CEP_GETADDRESSFROMPOINTER    GetAddressFromPointer;

    //=== Version 1: Windows API / kernel block (MIXED — see note below) ===
    CEP_READPROCESSMEMORY        ReadProcessMemory;      // ** (double pointer!)
    PVOID WriteProcessMemory;       // ** (double pointer)
    PVOID GetThreadContext;         // **
    PVOID SetThreadContext;         // **
    PVOID SuspendThread;            // **
    PVOID ResumeThread;             // **
    PVOID OpenProcess;              // **
    PVOID WaitForDebugEvent;        // **
    PVOID ContinueDebugEvent;       // **
    PVOID DebugActiveProcess;       // **
    PVOID StopDebugging;            // ALWAYS NULL
    PVOID StopRegisterChange;       // ALWAYS NULL
    PVOID VirtualProtect;           // **
    PVOID VirtualProtectEx;         // **
    PVOID VirtualQueryEx;           // **
    PVOID VirtualAllocEx;           // **
    PVOID CreateRemoteThread;       // **
    PVOID OpenThread;               // **
    PVOID GetPEProcess;
    PVOID GetPEThread;
    PVOID GetThreadsProcessOffset;
    PVOID GetThreadListEntryOffset;
    PVOID GetProcessnameOffset;     // ALWAYS NULL (obsolete)
    PVOID GetDebugportOffset;
    PVOID GetPhysicalAddress;
    PVOID ProtectMe;                // ALWAYS NULL
    PVOID GetCR4;
    PVOID GetCR3;
    PVOID SetCR3;                   // ALWAYS NULL
    PVOID GetSDT;
    PVOID GetSDTShadow;
    PVOID setAlternateDebugMethod;  // ALWAYS NULL
    PVOID getAlternateDebugMethod;  // ALWAYS NULL
    PVOID DebugProcess;             // ALWAYS NULL
    PVOID ChangeRegOnBP;            // ALWAYS NULL
    PVOID RetrieveDebugData;        // ALWAYS NULL
    PVOID StartProcessWatch;
    PVOID WaitForProcessListData;
    PVOID GetProcessNameFromID;
    PVOID GetProcessNameFromPEProcess;
    PVOID KernelOpenProcess;
    PVOID KernelReadProcessMemory;
    PVOID KernelWriteProcessMemory;
    PVOID KernelVirtualAllocEx;
    PVOID IsValidHandle;
    PVOID GetIDTCurrentThread;
    PVOID GetIDTs;
    PVOID MakeWritable;
    PVOID GetLoadedState;
    PVOID DBKSuspendThread;
    PVOID DBKResumeThread;
    PVOID DBKSuspendProcess;
    PVOID DBKResumeProcess;
    PVOID KernelAlloc;
    PVOID GetKProcAddress;
    PVOID CreateToolhelp32Snapshot;  // **
    PVOID Process32First;            // **
    PVOID Process32Next;             // **
    PVOID Thread32First;             // **
    PVOID Thread32Next;              // **
    PVOID Module32First;             // **
    PVOID Module32Next;              // **
    PVOID Heap32ListFirst;           // **
    PVOID Heap32ListNext;            // **

    //=== Version 1: Delphi object pointers ===
    PVOID mainform;                 // TMainForm**  — pointer to CE's global MainForm VARIABLE
    PVOID memorybrowser;            // TMemoryBrowser** — pointer to the global MemoryBrowser VARIABLE

    //=== Version 2: Symbol functions ===
    CEP_NAMETOADDRESS            sym_nameToAddress;
    CEP_ADDRESSTONAME            sym_addressToName;
    CEP_GENERATEAPIHOOKSCRIPT    sym_generateAPIHookScript;

    //=== Version 3: Navigation & AA commands ===
    CEP_LOADDBK32                loadDBK32;
    CEP_LOADDBVMIFNEEDED         loaddbvmifneeded;
    CEP_PREVIOUSOPCODE           previousOpcode;         // Returns ADDRESS, not delta — but see TRAP
    CEP_NEXTOPCODE               nextOpcode;             // Returns ADDRESS, not delta — but see TRAP
    CEP_DISASSEMBLEEX            disassembleEx;
    CEP_LOADMODULE               loadModule;
    CEP_AA_ADDCOMMAND            aa_AddExtraCommand;
    CEP_AA_DELCOMMAND            aa_RemoveExtraCommand;

    //=== Version 4: Memory records ===
    CEP_CREATETABLEENTRY         createTableEntry;
    CEP_GETTABLEENTRY            getTableEntry;
    CEP_MEMREC_SETDESCRIPTION    memrec_setDescription;
    CEP_MEMREC_GETDESCRIPTION    memrec_getDescription;
    CEP_MEMREC_GETADDRESS        memrec_getAddress;
    CEP_MEMREC_SETADDRESS        memrec_setAddress;
    CEP_MEMREC_GETTYPE           memrec_getType;
    CEP_MEMREC_SETTYPE           memrec_setType;
    CEP_MEMREC_GETVALUETYPE      memrec_getValue;
    CEP_MEMREC_SETVALUETYPE      memrec_setValue;
    CEP_MEMREC_GETSCRIPT         memrec_getScript;
    CEP_MEMREC_SETSCRIPT         memrec_setScript;
    CEP_MEMREC_ISFROZEN          memrec_isfrozen;
    CEP_MEMREC_FREEZE            memrec_freeze;
    CEP_MEMREC_UNFREEZE          memrec_unfreeze;
    CEP_MEMREC_SETCOLOR          memrec_setColor;
    CEP_MEMREC_APPENDTOENTRY     memrec_appendtoentry;
    CEP_MEMREC_DELETE            memrec_delete;

    //=== Version 4: Process & debug ===
    CEP_GETPROCESSIDFROMPROCESSNAME getProcessIDFromProcessName;
    CEP_OPENPROCESS              openProcessEx;
    CEP_DEBUGPROCESS             debugProcessEx;
    CEP_PAUSE                    pause;
    CEP_UNPAUSE                  unpause;
    CEP_DEBUG_SETBREAKPOINT      debug_setBreakpoint;
    CEP_DEBUG_REMOVEBREAKPOINT   debug_removeBreakpoint;
    CEP_DEBUG_CONTINUEFROMBREAKPOINT debug_continueFromBreakpoint;

    //=== Version 4: Window management ===
    CEP_CLOSECE                  closeCE;
    CEP_HIDEALLCEWINDOWS         hideAllCEWindows;
    CEP_UNHIDEMAINCEWINDOW       unhideMainCEwindow;

    //=== Version 4: UI creation ===
    CEP_CREATEFORM               createForm;
    CEP_FORM_CENTERSCREEN        form_centerScreen;
    CEP_FORM_HIDE                form_hide;
    CEP_FORM_SHOW                form_show;
    CEP_FORM_ONCLOSE             form_onClose;
    CEP_CREATEPANEL              createPanel;
    CEP_CREATEGROUPBOX           createGroupBox;
    CEP_CREATEBUTTON             createButton;
    CEP_CREATEIMAGE              createImage;
    CEP_IMAGE_LOADIMAGEFROMFILE  image_loadImageFromFile;
    CEP_IMAGE_TRANSPARENT        image_transparent;
    CEP_IMAGE_STRETCH            image_stretch;
    CEP_CREATELABEL              createLabel;
    CEP_CREATEEDIT               createEdit;
    CEP_CREATEMEMO               createMemo;
    CEP_CREATETIMER              createTimer;
    CEP_TIMER_SETINTERVAL        timer_setInterval;
    CEP_TIMER_ONTIMER            timer_onTimer;
    CEP_CONTROL_SETCAPTION       control_setCaption;
    CEP_CONTROL_GETCAPTION       control_getCaption;
    CEP_CONTROL_SETPOSITION      control_setPosition;
    CEP_CONTROL_GETX             control_getX;
    CEP_CONTROL_GETY             control_getY;
    CEP_CONTROL_SETSIZE          control_setSize;
    CEP_CONTROL_GETWIDTH         control_getWidth;
    CEP_CONTROL_GETHEIGHT        control_getHeight;
    CEP_CONTROL_SETALIGN         control_setAlign;
    CEP_CONTROL_ONCLICK          control_onClick;
    CEP_OBJECT_DESTROY           object_destroy;
    CEP_MESSAGEDIALOG            messageDialog;
    CEP_SPEEDHACK_SETSPEED       speedhack_setSpeed;

    //=== Version 5 ===
    VOID *ExecuteKernelCode;        // Windows kernel driver only
    VOID *UserdefinedInterruptHook; // Windows kernel driver only
    CEP_GETLUASTATE              GetLuaState;
    VOID *MainThreadCall;           // Alias for pluginsync()

} ExportedFunctions, *PExportedFunctions;
```

The 159 members above are **derived** from the source, not hand-maintained. Regenerate the list with
`sed -n '277,456p' "<ce-src>/Cheat Engine/plugin/cepluginsdk.h"`. The Pascal side of the same struct
is `TExportedFunctions5` (`plugin.pas:47-228`) and `TExportedFunctions` (`cepluginsdk.pas:352-536`);
all three are 159 members in the same order, with only two cosmetic Pascal-side renames —
`sizeofTExportedFunctions` and `ce_generateAPIHookScript`.

> **TRAP — eleven slots are permanently NULL.** `FixMem`, `StopDebugging`, `StopRegisterChange`,
> `GetProcessnameOffset`, `ProtectMe`, `SetCR3`, `setAlternateDebugMethod`, `getAlternateDebugMethod`,
> `DebugProcess`, `ChangeRegOnBP` and `RetrieveDebugData` are assigned `nil` in
> `TPluginHandler.Create` (`plugin.pas:1872, 1890, 1891, 1902, 1905, 1908, 1911-1915` — several
> marked `//obsolete`) and are never reassigned anywhere in the unit. Calling any of them
> null-derefs. This is **not** the "needs `loadDBK32()`" case in §10 — loading the driver does not
> populate them. Regenerate the list with
> `grep -n "exportedfunctions\..*:=nil" "<ce-src>/Cheat Engine/plugin.pas"`.

> **The `ReadProcessMemory`…`Heap32ListNext` block is not uniform** — the old
> "Windows API double-pointers" banner over-generalised. Of its 64 members
> (`plugin.pas:1880-1944`, the whole block inside `{$ifdef windows}` at `:1879`):
> - **25 are pointer-to-function-pointer** (assigned `@@…`) — exactly the ones marked `// **` above:
>   `ReadProcessMemory`…`DebugActiveProcess` (`:1880-1889`), `VirtualProtect`…`OpenThread`
>   (`:1892-1897`), and `CreateToolhelp32Snapshot`…`Heap32ListNext` (`:1936-1944`). Dereference once
>   to call, or overwrite `*field` to hook the API CE itself uses.
> - **29 are plain function pointers** (assigned `@…`) — `GetPEProcess`…`GetKProcAddress`, excluding
>   the NULL ones. Call them directly; do **not** dereference.
> - **10 are permanently NULL** — the block's share of the eleven listed above; the eleventh,
>   `FixMem`, sits in the Version 1 core group rather than in this block.
>
> 25 + 29 + 10 = 64. Getting this wrong is a wild pointer, not a compile error: everything is
> declared `PVOID`. The three counts are **derived** — regenerate with
> `awk 'NR>=1880 && NR<=1944' plugin.pas | grep -c ':=@@'`, then the same with `':=@[^@]'` and
> `':=nil'`.

> **Do not resolve this from `cepluginsdk.pas`.** That header declares *all 64* members of the block
> as `ppointer` (`cepluginsdk.pas:376-439`) — `GetPEProcess` (`:394`), `GetCR4` (`:402`),
> `StartProcessWatch` (`:412`), `KernelOpenProcess` (`:416`) and `GetKProcAddress` (`:430`)
> included — with a single exception, `GetLoadedState: TGetLoadedState` (`:424`). For 28 of them
> that is simply wrong: `plugin.pas` assigns a plain `@` there. `plugin.pas:1880-1944` is the only
> authority on which slot is single and which is double.

> **TRAP — `mainform` and `memorybrowser` are DOUBLE pointers**, like most of the Windows-API block
> above them. CE assigns `@mainform` — the address of its global object-reference *variable*, not the
> object — deliberately, because the forms may not be constructed yet when the plugin handler is
> created (`plugin.pas:1949-1951`, comment *"give the address of the variable since there is a change
> they arn't initialized just yet"*). The globals are `MainUnit.pas:1103 MainForm: TMainForm;` and
> `MemoryBrowserFormUnit.pas:741 MemoryBrowser: TMemoryBrowser;`. Dereference once to get the live
> object — `TObject* frm = *(TObject**)ef.mainform;` — and re-read it each time, since the variable
> can change.

> **TRAP — `previousOpcode` / `nextOpcode` truncate on x64.** CE implements both as `ptrUint` returns
> (`pluginexports.pas:833-836` and `:838-843`), but `cepluginsdk.h:185-186` types them
> `DWORD (__stdcall *)(UINT_PTR address)`. On x64 the SDK typedef reads only `EAX`, so any address
> above 4 GB comes back truncated. Re-declare them yourself as `UINT_PTR (__stdcall *)(UINT_PTR)`
> before use. The "returns an ADDRESS, not a delta" half of the comment is correct:
> `ce_nextOpcode` looks like it returns its input, but `TDisassembler.disassemble` takes `offset` as
> a `var` parameter (`disassembler.pas:181`) and advances it past the instruction first.

---

## 4. Core Functions (Version 1)

### ShowMessage

```cpp
void __stdcall ShowMessage(char* message);
```

Shows a message dialog. Thread-safe (internally uses `pluginsync`).

> `char*` is non-const — use `const_cast<char*>("text")` for string literals.

### AutoAssemble

```cpp
BOOL __stdcall AutoAssemble(char* script);
```

Executes an AA script string.

> **The return value is not a success indicator.** `ce_AutoAssemble` throws away the boolean that
> `autoassemble()` returns and hardcodes `result := true`, downgrading to FALSE only if an
> exception escapes (`pluginexports.pas:738-751` — `result:=true;` at `:741` *precedes* the
> `autoassemble(script,false);` call at `:745`; only the `except` at `:746` lowers it).
> `autoassembler.pas:55` confirms there is a boolean being discarded. A script that fails to
> assemble — bad opcode, unresolved symbol, failed alloc — still returns TRUE. Verify the effect
> yourself (e.g. read back the bytes you expected to be written).
>
> Note also it is called with `popupmessages = false`, so CE shows no error dialog either, and it
> does **not** route through `pluginsync` — call it from the main thread only.

### Assembler

```cpp
BOOL __stdcall Assembler(
    UINT_PTR address,        // Target address for relative calculations
    char* instruction,       // e.g. "mov rax,[rsp+48]"
    BYTE* output,            // Output buffer for machine code
    int maxlength,           // Buffer size
    int* returnedsize);      // Actual bytes written
```

Assembles a single instruction.

> **The return value is not a success indicator, and a bad instruction can throw.** `ce_assembler`
> discards the boolean that `Assemble()` returns (`pluginexports.pas:774`, `Assemblerunit.pas:4742`)
> and hardcodes `result := true` (`pluginexports.pas:790`); the only FALSE is the `maxlength`
> overflow path (`:775-780`, which sets `ERROR_NOT_ENOUGH_MEMORY`). **Check `*returnedsize`
> instead** — an instruction CE could not encode comes back as TRUE with `*returnedsize == 0` and
> an untouched buffer. (CE's own assembler caller does check the boolean:
> `autoassembler.pas:3908`.)
> Separately, the function has **no exception handler** — contrast `ce_InjectDLL`
> (`pluginexports.pas:623-641`), which wraps everything in `try..except`. Malformed operands raise
> `EAssemblerException` inside CE (`Assemblerunit.pas:3647`, `:4056`, `:4061`, `:4203`, `:4618`; an
> over-large offset raises the `EAssemblerExceptionOffsetTooBig` subclass at `:8419`) and the
> exception unwinds across the `__stdcall` boundary into your plugin. Wrap the call in
> `__try/__except` (or validate the mnemonic first) if you pass user-supplied text.
> `returnedsize` is also dereferenced unconditionally (`:785`) — never pass NULL.

### Disassembler

```cpp
BOOL __stdcall Disassembler(
    UINT_PTR address,        // Address to disassemble
    char* output,            // Output buffer for mnemonic
    int maxsize);            // Buffer size
```

Disassembles one instruction. Returns mnemonic only (no module/symbol names).

> **CE internals:** Uses a plugin-private disassembler instance created once at startup with
> `showsymbols=false`, `showmodules=false`, `showsections=false` and `isdefault=false`
> (`pluginexports.pas:2638-2643`).

### previousOpcode / nextOpcode

```cpp
UINT_PTR __stdcall previousOpcode(UINT_PTR address);  // SDK header says DWORD — WRONG
UINT_PTR __stdcall nextOpcode(UINT_PTR address);       // SDK header says DWORD — WRONG
```

Return the **address** of the previous/next instruction. NOT a delta or size.

> **CRITICAL BUG in SDK header:** Return type declared as `DWORD` (32-bit), but CE actually returns `ptrUint` (64-bit). On x64, high 32 bits are truncated if you use the SDK header as-is. Fix in your header:
> ```cpp
> typedef UINT_PTR (__stdcall *CEP_PREVIOUSOPCODE)(UINT_PTR address);
> typedef UINT_PTR (__stdcall *CEP_NEXTOPCODE)(UINT_PTR address);
> ```

**Correct iteration pattern:**
```cpp
// Collect N instructions before address
UINT_PTR prev = targetAddr;
for (int i = 0; i < N; i++) {
    UINT_PTR p = g_Exported.previousOpcode(prev);
    if (p == 0 || p >= prev) break;  // Defensive only — see note below
    prev = p;
}

// Collect N instructions after address
UINT_PTR next = targetAddr;
for (int i = 0; i < N; i++) {
    UINT_PTR n = g_Exported.nextOpcode(next);
    if (n == 0 || n <= next) break;  // Guard: invalid or didn't advance
    next = n;
}

// Instruction length = nextOpcode(addr) - addr
```

> **`previousOpcode` never reports failure.** x86 has no way to decode backwards, so CE guesses:
> it looks for an instruction stream that lands exactly on `address` starting 80, then 40, then 20,
> then 10 bytes back (`disassembler.pas:15805`, `:15810`, `:15815`, `:15819`). If all four fail it
> seeds `result := address - 1` (`:15823`) and brute-forces `address-1 .. address-20` for any single
> instruction that ends exactly on `address`, keeping `address - 1` if even that fails
> (`:15824-15833`). The result is therefore always < `address` and never 0, so the guard above can
> only fire on a wrap at `address == 0`. **A returned value is a heuristic, not a verified
> instruction boundary** — in data, padding, or mid-instruction jump targets it will be wrong,
> silently. `nextOpcode` has no such problem: forward decoding is exact.

### disassembleEx

```cpp
// The SDK header (cepluginsdk.h:188) declares the first parameter as UINT_PTR. That is WRONG.
// CE fills this slot with ce_disassemble, whose first parameter is a POINTER to the address.
typedef BOOL (__stdcall *CEP_DISASSEMBLEEX)(UINT_PTR* address, char* output, int maxsize);
```

Identical to `Disassembler` except the address is passed **by pointer**, and CE **advances it**:
it reads `*address`, disassembles there, and writes back the address of the **next** instruction
(`pluginexports.pas:817`, `:826`; `plugindisassembler.disassemble` takes its offset as a `var`
parameter — `disassembler.pas:181` — and steps it past the instruction). That makes
`disassembleEx` a combined disassemble + `nextOpcode`: `ce_nextOpcode` is literally the same call
with the string thrown away (`pluginexports.pas:838-843`). It produces no extra info over
`Disassembler` — both call `plugindisassembler.disassemble(addr, extra)` and discard `extra`.

> **CRITICAL — do not use the SDK header typedef.** Calling `disassembleEx(0x7FF6C0001000, buf, 256)`
> makes CE execute `address^` on `0x7FF6C0001000`, dereferencing your *address* as if it were a
> *pointer to* an address. You disassemble whatever bytes happen to live at that location, or crash.
> The Pascal SDK gets this right — `cepluginsdk.pas:265`
> `TDisassembleEx=function(address: pptruint; output: pchar; maxsize: integer): BOOL; stdcall;` —
> and `plugin.pas:1965` fills the slot with `@ce_disassemble`.
>
> ```cpp
> UINT_PTR a = targetAddr;
> g_Exported.disassembleEx(&a, buf, sizeof(buf));   // correct; a now points at the NEXT instruction
> ```

### FreezeMem / UnfreezeMem

```cpp
int  __stdcall FreezeMem(UINT_PTR address, int size);  // Freeze ID (>=1), or -1 on failure
BOOL __stdcall UnfreezeMem(int freezeID);              // FALSE if that ID is not in the list
```

> Check for `-1`, not `0` — IDs are allocated from 1 upward (`pluginexports.pas:509` sets the
> failure value, `:521` seeds `maxid:=1`, `:525` bumps it, `:538` returns it).
> Failure means the initial `ReadProcessMemory` of `size` bytes at `address` failed (`:519`).
> CE marks this API obsolete in-source — `pluginexports.pas:503` carries the comment
> *"should be obsolete as it has no interpretable address support"*; a frozen memory record via
> `createTableEntry` + `memrec_freeze` is the maintained path.

### GetAddressFromPointer

```cpp
// SDK header (cepluginsdk.h:178) says the return is UINT_PTR. That is WRONG — CE returns DWORD.
DWORD __stdcall GetAddressFromPointer(
    UINT_PTR baseaddress,
    int offsetcount,
    DWORD* offsets);          // PDwordArray in CE — DWORD elements, not int
```

Resolves a pointer chain. For `offsetcount = N` it performs **N** reads:
`a = base; repeat N times { a = [a]; a += offsets[i]; }`. So `N=1` yields `[base]+off0`,
`N=2` yields `[[base]+off0]+off1`. The final offset is added, not dereferenced.
Returns 0 if any read fails. Each read is `processhandler.pointersize` bytes
(`pluginexports.pas:489-497`), so it handles a 32-bit target correctly.

> **CRITICAL — 64-bit truncation.** CE computes the address as `ptrUint` (`pluginexports.pas:482`
> `var a,b: ptrUint;` … `:499 result:=a;`) but the function is declared `:dword`
> (`pluginexports.pas:481`, mirrored at `cepluginsdk.pas:240`), so only the low 32 bits reach you.
> On an x64 target any address above 4 GB comes back mangled. **Do not use this for 64-bit
> targets** — walk the chain yourself with `ReadProcessMemory`, or use `sym_nameToAddress`, which
> correctly returns `UINT_PTR` via an out-parameter.

### ProcessList

```cpp
BOOL __stdcall ProcessList(char* listbuffer, int listsize);
```

Fills the buffer with the process list. Format is `%08X-ProcessName` per record — the PID is
**8-digit HEX**, not decimal — with records separated by **CRLF** (`\r\n`), and the whole buffer
NUL-terminated (`pluginexports.pas:592`, `:595`, `:607-610`).

Example: `00001A2C-game.exe\r\n000004D0-explorer.exe\0`

Parse with `sscanf(rec, "%8X-", &pid)` — `%d` silently yields the wrong PID. CE's own reader does
the same thing: `pluginexports.pas:1387 result:=strtoint('$'+copy(plist[i],1,j-1));` (the `$`
prefix is Pascal for hex).
Returns FALSE if `listsize` was too small (`:599-600`); the buffer then holds a truncated but still
NUL-terminated list.

### InjectDLL

```cpp
BOOL __stdcall InjectDLL(char* dllname, char* functiontocall);
```

Injects a DLL into the opened process and calls the specified export.

### ChangeRegistersAtAddress

```cpp
BOOL __stdcall ChangeRegistersAtAddress(
    UINT_PTR address,
    PREGISTERMODIFICATIONINFO changereg);
```

Sets a breakpoint that modifies registers when hit. See §12 for struct layout.

### ReadProcessMemory (Double Pointer!)

```cpp
// Correct call pattern — note double dereference:
(*g_Exported.ReadProcessMemory)(
    *g_Exported.OpenedProcessHandle,   // Dereference PHANDLE
    (LPCVOID)address,
    buffer,
    size,
    &bytesRead);
```

> **TRAP:** `ReadProcessMemory`, `WriteProcessMemory`, `GetThreadContext`, etc. are **pointer-to-function-pointer** (double indirection). CE uses this design to allow plugins to hook/replace these functions. See [ce-plugin-sdk-notes.md](ce-plugin-sdk-notes.md) §5.

### OpenedProcessID / OpenedProcessHandle

```cpp
DWORD  pid    = *g_Exported.OpenedProcessID;      // Dereference PULONG
HANDLE handle = *g_Exported.OpenedProcessHandle;   // Dereference PHANDLE
```

Both are pointers to the actual values. Always dereference before use.

---

## 5. Symbol & Module Functions (Version 2–3)

### sym_nameToAddress

```cpp
BOOL __stdcall sym_nameToAddress(char* name, UINT_PTR* address);
```

Resolves a symbol name (e.g., `"game.exe+1234"`) to an absolute address.

### sym_addressToName

```cpp
BOOL __stdcall sym_addressToName(UINT_PTR address, char* name, int maxnamesize);
```

Converts an address to `"module+offset"` format (e.g., `"game.exe+1501190"`).

### sym_generateAPIHookScript

```cpp
BOOL __stdcall sym_generateAPIHookScript(
    char* address,                    // Address to hook
    char* addresstojumpto,            // Jump target
    char* addresstogetnewcalladdress, // Where to store original
    char* script,                     // Output AA script buffer
    int maxscriptsize);
```

### loadModule

```cpp
BOOL __stdcall loadModule(char* modulepath, char* exportlist, int* maxsize);
```

Loads a module and retrieves its export list. Windows only.

### loadDBK32 / loaddbvmifneeded

```cpp
void __stdcall loadDBK32(void);
BOOL __stdcall loaddbvmifneeded(void);
```

Load the kernel driver (DBK32) or DBVM hypervisor if needed.

### aa_AddExtraCommand / aa_RemoveExtraCommand

```cpp
// The SDK header (cepluginsdk.h:189-190) marks these __stdcall. CE's functions are NOT stdcall:
// SynHighlighterAA.pas:527,539 declare them with no calling-convention directive, so they use
// FPC's default (`register`). The Pascal SDK agrees with the implementation (cepluginsdk.pas:266-267).
typedef VOID (__stdcall *CEP_AA_ADDCOMMAND)(char* command);   // correct on x64 ONLY — see note
typedef VOID (__stdcall *CEP_AA_DELCOMMAND)(char* command);
```

These add/remove a keyword in the **Auto Assembler syntax highlighter only**. They do NOT make a
new AA command work — the command itself must be registered separately
(`RegisterAutoAssemblerCommand`, which calls `aa_AddExtraCommand` as a side effect,
`autoassembler.pas:287`). Calling `aa_AddExtraCommand` alone just colours a word in the editor.
The list is case-insensitive and duplicate-ignoring (`SynHighlighterAA.pas:529-536`), and the
backing string list is freed once the last entry is removed (`:544-545`).

> **Calling convention (32-bit CE only).** On **x64** FPC's `register` and `__stdcall` are both the
> Microsoft x64 ABI, so the SDK header's declaration is correct and these are safe to call.
> On **32-bit CE** they differ and there is **no MSVC convention that matches**: FPC `register`
> passes `command` in **EAX** and cleans no stack, while MSVC `__stdcall` pushes the argument
> (callee pops 4), `__cdecl` pushes it (caller pops), and `__fastcall` uses ECX. Any of them both
> feeds the callee garbage and mismatches the cleanup. **Do not call these from a 32-bit plugin**
> unless you hand-write the EAX setup in asm — they only affect editor colouring.

---

## 6. Memory Record Functions (Version 4)

All memory record functions operate on `PVOID memrec` (opaque pointer to `TMemoryRecord`).

### Table Entry Management

```cpp
PVOID __stdcall createTableEntry(void);
// Creates a new memory record in the cheat table. Returns TMemoryRecord*.

PVOID __stdcall getTableEntry(char* description);
// Finds an existing entry by description text. Returns NULL if not found.
```

### Description

```cpp
BOOL  __stdcall memrec_setDescription(PVOID memrec, char* description);
PCHAR __stdcall memrec_getDescription(PVOID memrec);
// getDescription returns a pointer to an internal string — read-only, do not free.
```

### Address & Offsets

```cpp
BOOL __stdcall memrec_getAddress(
    PVOID memrec,
    UINT_PTR* address,        // Output: resolved base address (may be NULL)
    DWORD* offsets,           // Output: offset array (may be NULL)
    int maxoffsets,           // MUST equal the record's real offsetCount — NOT your buffer size
    int* neededOffsets);      // Output: actual offset count (may be NULL)

BOOL __stdcall memrec_setAddress(
    PVOID memrec,
    char* address,            // Address string (e.g. "game.exe+1234")
    DWORD* offsets,           // Offset array (NULL if not a pointer)
    int offsetcount);         // Number of offsets (0 if not a pointer)
```

> **TRAP — `maxoffsets` is not a cap, it is an exact count.** CE loops `for i := 0 to maxoffsets-1`
> with no bound against the record's own `offsetCount` (`pluginexports.pas:975-979`). Past the end,
> `m.offsets[i]` returns nil (`MemoryRecordUnit.pas:2149-2155`) and the nil dereference raises an AV
> that the `try..except` (`:985`) swallows — but `result := true` is at `:983`, *after* the loop, so
> **the call returns FALSE and the offsets array is only partially filled.** Passing your buffer
> capacity fails for every record with fewer offsets than that, i.e. the normal case.
>
> **Always make two calls:**
> ```cpp
> int needed = 0;
> g_Exported.memrec_getAddress(rec, &base, NULL, 0, &needed);          // probe
> std::vector<DWORD> offs(needed);
> g_Exported.memrec_getAddress(rec, &base, needed ? offs.data() : NULL,
>                              needed, NULL);                          // exact
> ```
> `neededOffsets^` (`:970`) and `address^` (`:973`) are written *before* the loop, so both are valid
> even when the call returns FALSE.
>
> `memrec_setAddress` has no such defect: it sizes the array first
> (`pluginexports.pas:1007 p.memrec.offsetCount:=p.offsetcount;`) and then fills it (`:1010-1011`).

### Type & Value

```cpp
int  __stdcall memrec_getType(PVOID memrec);
// Returns a CE TVariableType ordinal (commontypedefs.pas:15), or -1 if memrec is not a
// TMemoryRecord (pluginexports.pas:1037,1041). See §11 for the full enum.
//  0 = vtByte           6 = vtString          12 = vtPointer
//  1 = vtWord           7 = vtUnicodeString   13 = vtCustom
//  2 = vtDword          8 = vtByteArray       14 = vtGrouped
//  3 = vtQword          9 = vtBinary          15 = vtByteArrays
//  4 = vtSingle (float) 10 = vtAll            16 = vtCodePageString
//  5 = vtDouble        11 = vtAutoAssembler

BOOL __stdcall memrec_setType(PVOID memrec, int vtype);
// Same TVariableType numbering. pluginexports.pas:1061 does
// `p.memrec.VarType:=Tvariabletype(p.vtype);` with NO range check — an out-of-range value is
// cast straight into the enum.

BOOL __stdcall memrec_getValue(PVOID memrec, char* value, int maxsize);
// Gets the current value as a string. Writes at most maxsize bytes including the NUL.
// Returns TRUE even for a non-memrec pointer (CE bug, pluginexports.pas:1103) — the BOOL only
// goes FALSE on an internal exception. Zero your buffer first and treat an empty result as failure.

BOOL __stdcall memrec_setValue(PVOID memrec, char* value);
// Sets value from string representation
```

> **The old `3=float, 4=double, 5=bit, 6=int64, 7=string` numbering was never right for
> `memrec_getType`/`memrec_setType`.** It was copied from the trailing `//` comment on
> `PLUGINTYPE0_RECORD.valuetype` in the
> **7.5-era** `cepluginsdk.h:35`, and that comment was simply stale: the field has always carried a
> raw `TVariableType` ordinal (`MainUnit.pas:9743`
> `selectedrecord^.valuetype := integer(addresslist.selectedRecord.VarType);`). **CE fixed the
> comment in 7.7**, and the fixed text now agrees with the table above for 0–9. So there is one
> value-type numbering in this SDK, not two — see §11 for the full enum and the header diff.
>
> `createTableEntry` returns a record already set to `vtDword` (`pluginexports.pas:894`).

### Script

```cpp
char* __stdcall memrec_getScript(PVOID memrec);
BOOL  __stdcall memrec_setScript(PVOID memrec, char* script);
```

> **Both require `VarType == vtAutoAssembler` (11).** Outside that type `getScript` returns NULL
> and `setScript` returns FALSE, with no other signal (`pluginexports.pas:1133`, `:1148`).
> `createTableEntry` hands back a **vtDword** record (`pluginexports.pas:894`), so you must call
> `memrec_setType(rec, 11)` *first*:
> ```cpp
> PVOID rec = g_Exported.createTableEntry();
> g_Exported.memrec_setType(rec, 11);            // vtAutoAssembler — required
> g_Exported.memrec_setScript(rec, script);
> ```
>
> **`memrec_getScript` returns a DANGLING pointer.** It hands you
> `pchar(m.AutoAssemblerData.script.text)` (`pluginexports.pas:1134`), and `TStrings.Text` is a
> *function* (`GetTextStr`) that builds a temporary string which is released when
> `ce_memrec_getScript` returns. **Copy the bytes immediately** — before any other CE call — and
> never hold the pointer. (`memrec_getDescription` is genuinely safe by contrast: it returns a
> pointer into the record's own `fDescription` field, `pluginexports.pas:953` /
> `MemoryRecordUnit.pas:382`.)

### Freeze/Unfreeze

```cpp
BOOL __stdcall memrec_isfrozen(PVOID memrec);

BOOL __stdcall memrec_freeze(PVOID memrec, int direction);
// direction: 0 = normal freeze, 1 = allow increase only, 2 = allow decrease only

BOOL __stdcall memrec_unfreeze(PVOID memrec);
```

### Display & Hierarchy

```cpp
BOOL __stdcall memrec_setColor(PVOID memrec, DWORD color);
// color: TColor value (e.g., 0x0000FF for red in BGR format)

BOOL __stdcall memrec_appendtoentry(PVOID memrec1, PVOID memrec2);
// Makes memrec1 a CHILD of memrec2 — note the order: the FIRST argument is the one that moves.
// (pluginexports.pas:1309 -> m1.treenode.MoveTo(m2.treenode, naAddChild))

BOOL __stdcall memrec_delete(PVOID memrec);
// ALWAYS returns FALSE, even on success — do not test the result.
```

> **`memrec_appendtoentry`'s argument order reads backwards.** Think of it as "append memrec1 **to**
> memrec2", matching the function name. `TTreeNode.MoveTo(Destination, naAddChild)` moves the
> *calling* node under `Destination`, so `m1` becomes a child of `m2`; the follow-up
> `m2.SetVisibleChildrenState` (`pluginexports.pas:1312`) independently confirms `m2` is the parent.
> Passing them the other way round builds an inverted tree — the naming invites `(parent, child)`
> but CE implements `(child, parent)`.

> **CE bug: `memrec_delete`'s return value is stuck at FALSE.** `ce_memrec_delete2` sets
> `result:=nil` and never assigns a success value (`pluginexports.pas:1331-1341`), so
> `pluginsync(...)<>nil` at `:1345` is always false. Every sibling helper *does* set it —
> `result:=pointer(1)` appears at `:1064` (setType2), `:1209` (freeze2), `:1237` (unfreeze2),
> `:1271` (setColor2), `:1310` (appendToEntry2). The delete itself works. After it returns,
> `memrec` is freed — **the pointer is dangling; drop it.**

---

## 7. Debug Functions (Version 4)

### Process Control

```cpp
DWORD __stdcall getProcessIDFromProcessName(char* name);
// Returns 0 if not found

BOOL __stdcall openProcessEx(DWORD pid);
// SDK header says DWORD; CE returns BOOL (cepluginsdk.pas:291, pluginexports.pas:1479).
// Binary-compatible (Delphi BOOL = LongBool, 4 bytes) — but the value is 0/1, NOT a handle or PID.
// Read the resulting handle from *g_Exported.OpenedProcessHandle instead.

BOOL __stdcall debugProcessEx(int debuggerinterface);
// Same: header says DWORD, CE returns BOOL (cepluginsdk.pas:292, pluginexports.pas:1527).
// Starts debugging with the currently opened process.
// debuggerinterface: 0 = leave CE's configured setting alone (whatever Settings has)
//                    1 = Windows debug API   2 = VEH debugger   3 = Kernel debugger (DBK)
// Any other value behaves like 0 (the case has no else, pluginexports.pas:1515-1519).
// NOTE: 1/2/3 TICK the corresponding checkbox on CE's live Settings form, a user-visible change
// that outlives the call (and is written to the registry if the user later applies Settings) —
// not a per-call option. Returns honestly: TRUE only if startdebuggerifneeded succeeded
// (pluginexports.pas:1521-1524).

void __stdcall pause(void);
void __stdcall unpause(void);
```

### Breakpoints

```cpp
BOOL __stdcall debug_setBreakpoint(
    UINT_PTR address,
    int size,                          // Watch size in bytes (1/2/4/8) — IGNORED when trigger=0 (bptExecute)
    int trigger);                      // TBreakpointTrigger enum value
// trigger: 0=bptExecute, 1=bptAccess, 2=bptWrite

BOOL __stdcall debug_removeBreakpoint(UINT_PTR address);

BOOL __stdcall debug_continueFromBreakpoint(int continueoption);
// continueoption: 0=co_run, 1=co_stepinto, 2=co_stepover, 3=co_runtill
```

> **`debug_setBreakpoint`'s return value is not a success indicator.** `ce_debug_setBreakpoint2`
> returns success unconditionally — the guard is `pluginexports.pas:1544`
> `if startdebuggerifneeded(false) then`, its block closes at `:1554`, and `result:=pointer(1);`
> sits outside it at `:1556`. If CE could not attach a debugger, you still get TRUE and no
> breakpoint. Confirm by other means — `debug_removeBreakpoint` *does* report honestly: it sets
> `result:=true` (`:1580`) only when `debuggerthread.isBreakpoint(address)` actually found one
> (`pluginexports.pas:1568-1586`).

> **`size` reaches CE only for `bptAccess` and `bptWrite`.** The execute path calls
> `SetOnExecuteBreakpoint(address)`, an overload with no size parameter at all
> (`pluginexports.pas:1546-1550`, dropped at `:1549`; `debughelper.pas:2759`). Pass 1 for execute
> breakpoints — the value is discarded either way.

### Window Management

```cpp
void __stdcall closeCE(void);              // Graceful close
void __stdcall hideAllCEWindows(void);     // Hide all CE windows
void __stdcall unhideMainCEwindow(void);   // Show main window
```

---

## 8. UI Creation Functions (Version 4)

All UI functions create Delphi/LCL components. Owner controls lifetime — destroying the owner destroys all children.

> **All three CE callbacks take a `sender` argument** — `void __stdcall cb(PVOID sender)`. The
> stored callback type is `TNotifyCall2 = procedure(sender: TObject); stdcall` (`pluginexports.pas:148`,
> slots at `:156-158`), and CE invokes all three **with** the argument: `onclick(sender)` at `:231`,
> `onClose(sender)` at `:239`, `onTimer(sender)` at `:254`. The SDK header only types these slots as
> `PVOID`, so nothing catches a wrong declaration at compile time. Declaring `void __stdcall cb(void)`
> still "works" on x64 (the argument sits unread in RCX) but on **32-bit CE it corrupts the stack** —
> the caller pushes 4 bytes and a zero-argument `stdcall` callee pops none, leaking 4 bytes per
> click/tick. Take the parameter even if you ignore it; it is also the only way to share one handler
> across several controls.

> **A plugin form frees itself when closed.** `createForm` installs an OnClose handler that sets
> `Action := caFree` (`pluginexports.pas:1676`, handler body `:246-249`), and registering your own
> handler with `form_onClose` does not override it — CE runs your callback and *then* forces
> `caFree` anyway (`pluginexports.pas:236-243`). So after the user clicks the X, **your `PVOID` form
> handle and every child control handle are dangling.** Null them in your `form_onClose` callback,
> and never call `object_destroy` on a form you have already seen close.

### Form

```cpp
// The SDK header (cepluginsdk.h:224) declares this as taking void. That is WRONG.
// CE's function takes one parameter and reads it (pluginexports.pas:1662-1681).
// The Pascal SDK is correct: cepluginsdk.pas:303 -> function(visible: boolean): pointer; stdcall
typedef PVOID (__stdcall *CEP_CREATEFORM)(BOOL visible);

PVOID form = ((CEP_CREATEFORM)g_Exported.createForm)(FALSE);   // create hidden, then form_show()

void __stdcall form_centerScreen(PVOID form);
void __stdcall form_hide(PVOID form);
void __stdcall form_show(PVOID form);
void __stdcall form_onClose(PVOID form, PVOID function);
// function: void __stdcall callback(PVOID sender)
//   sender = the control/form/timer that fired (CE passes it; TNotifyCall2, pluginexports.pas:148)
```

> **Do NOT call `createForm` with no arguments.** CE dereferences the parameter
> (`if visible^ then f.show`, `pluginexports.pas:1671`; the address is handed on at `:1681`
> `result:=pluginsync(ce_createForm2,@visible);`), so on **x64** it reads whatever junk was in RCX
> and the form shows or hides at random. On **32-bit CE** it is worse: the callee is `stdcall` with
> one argument, so it executes `ret 4` and pops 4 bytes the caller never pushed — **stack
> corruption on every call.** Always pass an explicit `TRUE`/`FALSE`. (Delphi `boolean` is 1 byte,
> but a C `BOOL` of 0/1 has the right low byte, so `BOOL` is safe to declare.)

### Controls

```cpp
PVOID __stdcall createPanel(PVOID owner);
PVOID __stdcall createGroupBox(PVOID owner);
PVOID __stdcall createButton(PVOID owner);
PVOID __stdcall createImage(PVOID owner);
PVOID __stdcall createLabel(PVOID owner);
PVOID __stdcall createEdit(PVOID owner);
PVOID __stdcall createMemo(PVOID owner);
PVOID __stdcall createTimer(PVOID owner);
// owner: used as BOTH owner (lifetime) AND parent (visual containment) — CE assigns
//        control.parent := TWinControl(owner) for ALL SEVEN visual controls:
//        Panel 1730, GroupBox 1744, Button 1758, Image 1772, Label 1865, Edit 1879, Memo 1893
//        (pluginexports.pas).
//        It MUST therefore be a WINDOWED control: a form, panel, or groupbox.
//        Passing a TLabel or TImage (non-windowed TGraphicControl) is an unchecked bad cast.
//        Passing NULL leaves the control parentless and invisible.
// createTimer is the exception: owner is used for lifetime only, never as a parent
//        (pluginexports.pas:1903-1910) — TTimer is non-visual.
```

> The "seven visual controls" count and the line numbers above are **derived** — regenerate with
> `grep -n "parent:=twincontrol(params)" "Cheat Engine/pluginexports.pas"` rather than hand-editing
> them. A new `create*` control in a future CE adds a row to that grep.

### Image

```cpp
BOOL __stdcall image_loadImageFromFile(PVOID image, char* filename);
void __stdcall image_transparent(PVOID image, BOOL transparent);
void __stdcall image_stretch(PVOID image, BOOL stretch);
```

### Timer

```cpp
void __stdcall timer_setInterval(PVOID timer, int interval);  // Milliseconds
void __stdcall timer_onTimer(PVOID timer, PVOID function);
// function: void __stdcall callback(PVOID sender)
//   sender = the control/form/timer that fired (CE passes it; TNotifyCall2, pluginexports.pas:148)
```

> **`timer_onTimer` starts the timer.** Assigning the handler also sets `Enabled := true`
> (`pluginexports.pas:291`), so the callback begins firing immediately at the current interval —
> call `timer_setInterval` **first**. The SDK exports no enable/disable entry
> (`cepluginsdk.h:242,244,245` are the only timer functions), so the only way to stop a plugin
> timer is `object_destroy(timer)`, which Frees it (`pluginexports.pas:2195`). Register the handler
> last, and make it re-entrancy-safe: it runs on CE's main thread while your form is alive.

### Control Properties

```cpp
void __stdcall control_setCaption(PVOID control, char* caption);
BOOL __stdcall control_getCaption(PVOID control, char* caption, int maxsize);

void __stdcall control_setPosition(PVOID control, int x, int y);
int  __stdcall control_getX(PVOID control);
int  __stdcall control_getY(PVOID control);

void __stdcall control_setSize(PVOID control, int width, int height);
int  __stdcall control_getWidth(PVOID control);
int  __stdcall control_getHeight(PVOID control);

void __stdcall control_setAlign(PVOID control, int align);
// align: 0=alNone, 1=alTop, 2=alBottom, 3=alLeft, 4=alRight, 5=alClient

void __stdcall control_onClick(PVOID control, PVOID function);
// function: void __stdcall callback(PVOID sender)
//   sender = the control/form/timer that fired (CE passes it; TNotifyCall2, pluginexports.pas:148)
```

### Cleanup & Dialogs

```cpp
void __stdcall object_destroy(PVOID object);
// Controls, timers, panels: Free()d immediately.
// FORMS: Close()d, not Freed (pluginexports.pas:2192-2195). The form is destroyed only because
// its OnClose handler sets Action=caFree — so destruction is deferred to the message loop, and
// an OnClose handler of yours runs first. Do not touch the pointer after this call either way.
// It is a procedure with no result (pluginexports.pas:2200): there is no success signal.

int __stdcall messageDialog(char* message, int messagetype, int buttoncombination);
// messagetype:       0=Warning  1=Error  2=Information  3=Confirmation
//                    (anything else -> Information; pluginexports.pas:2226-2233)
// buttoncombination: 0=OK  1=YesNo  2=YesNoCancel  3=OKCancel
//                    VALUES >= 4 ARE NOT HANDLED — see warning below
// Returns a Delphi modal result: 1=mrOk 2=mrCancel 3=mrAbort 4=mrRetry 5=mrIgnore 6=mrYes 7=mrNo
```

> **`buttoncombination` >= 4 passes uninitialised stack to CE.** The `case` has no `else` branch and
> `p` is a local record, so the button set is never assigned and `MessageDlg` receives garbage
> (`pluginexports.pas:2218-2222` declares it, `:2235-2240` is the unguarded `case`, `:2214` is the
> `MessageDlg` call) — an arbitrary or empty button combination, i.e. a dialog the user may not be
> able to dismiss. **Clamp to 0–3.** There is no AbortRetryIgnore or RetryCancel option, and no
> `4=Custom` message type. Only the *return* mapping above is as previously documented.

### Speed Hack

```cpp
BOOL __stdcall speedhack_setSpeed(float speed);
// 1.0 = normal speed, 2.0 = 2x, 0.5 = half speed
```

---

## 9. Advanced Functions (Version 5)

### GetLuaState

```cpp
// The SDK header (cepluginsdk.h:265) marks this __fastcall. That is WRONG.
// CE's function is __stdcall like every other export:
//   pluginexports.pas:2632  function plugin_getluastate: Plua_State; stdcall;
//   cepluginsdk.pas:341     type TGetLuaState=function: pointer; stdcall;
typedef lua_State* (__stdcall *CEP_GETLUASTATE)(void);
```

Returns CE's internal Lua state. Allows registering Lua functions from C++.

> **Calling convention:** `__stdcall`. The `__fastcall` in the C SDK header is a header bug, not a
> real deviation — nothing in the SDK actually uses fastcall (`plugin.pas:2044` assigns
> `@plugin_GetLuaState`, which is declared `stdcall`). It is harmless in practice because the
> function takes no arguments: on x64 MSVC ignores both attributes (one ABI), and on x86 a
> zero-argument `__fastcall` and `__stdcall` both compile to a plain `ret`. Declare it `__stdcall`
> so it matches CE and stays correct if the signature ever changes.

```cpp
// Example: Register a Lua function
lua_State* L = g_Exported.GetLuaState();
lua_register(L, "myPluginFunc", my_lua_function);
```

### MainThreadCall

```cpp
// Typed as a bare PVOID in the SDK header (cepluginsdk.h:456). CE assigns pluginsync to it
// (plugin.pas:2045). Real signature, from cepluginsdk.pas:343-344 / pluginexports.pas:15,880:

typedef PVOID (*CEP_PLUGINFUNC)(PVOID parameters);   // NOT __stdcall — see warning
typedef PVOID (__stdcall *CEP_MAINTHREADCALL)(CEP_PLUGINFUNC func, PVOID parameters);
```

Runs `func(parameters)` on CE's main GUI thread and returns whatever `func` returned. If you are
already on the main thread it calls `func` directly; otherwise it blocks on
`SendMessage(mainform.handle, wm_pluginsync, ...)` until the main thread runs it
(`pluginexports.pas:886-889`). Every other `ce_*` UI/table function is built on this, which is why
they are all thread-safe.

> **The callback is NOT `__stdcall`.** `TPluginFunc` carries no calling-convention directive, so it
> uses FPC's default `register`. The Pascal SDK flags this explicitly —
> `cepluginsdk.pas:343` reads `type TPluginFunc=function(parameters: pointer): pointer;`
> with the trailing comment *note, no stdcall. It's a "pascal" calling convention*.
> - On **x64**, FPC `register` IS the Microsoft ABI, so declaring the callback `__stdcall` (or with
>   no attribute at all) is correct and this all works.
> - On **32-bit CE** it is unusable from MSVC: `register` passes `parameters` in **EAX**, which no
>   MSVC convention produces (`__cdecl` = stack, `__stdcall` = stack, `__fastcall` = ECX). A
>   32-bit plugin must either read EAX in inline asm or avoid `MainThreadCall` entirely and rely on
>   the `ce_*` wrappers, which already marshal to the main thread for you.
>
> **`MainThreadCall` itself IS `__stdcall`** (`cepluginsdk.pas:344`) — only the callback differs.
>
> Because the outer call blocks via `SendMessage`, never invoke it from a thread that the main
> thread is itself waiting on, and never from `CEPlugin_DisablePlugin` teardown paths.

---

## 10. Windows API Pointers

The `ExportedFunctions` struct contains **64** Windows/kernel function pointers
(`cepluginsdk.h:299-362`; the count is **derived** — regenerate with
`awk 'NR>=299 && NR<=362' plugin/cepluginsdk.h | grep -c ';'` — do not hand-edit it).
The SDK header's banner comment at `cepluginsdk.h:297-298` claims they are all "pointers to the
address that contains the pointers to the functions" — **that is only true for 25 of them.**
CE fills the block three different ways (`plugin.pas:1880-1944`) — all inside `{$ifdef windows}`
(`plugin.pas:1879`/`:1945`); on non-Windows CE builds none of the 64 are assigned:

| Shape | Count | How to call | Which fields |
|-------|-------|-------------|--------------|
| `@@X` — pointer to CE's function *variable* | 25 | **double** dereference: `(*ef.X)(...)` | `ReadProcessMemory`, `WriteProcessMemory`, `Get/SetThreadContext`, `Suspend/ResumeThread`, `OpenProcess`, `WaitForDebugEvent`, `ContinueDebugEvent`, `DebugActiveProcess`, `VirtualProtect`, `VirtualProtectEx`, `VirtualQueryEx`, `VirtualAllocEx`, `CreateRemoteThread`, `OpenThread`, `CreateToolhelp32Snapshot`, `Process32First/Next`, `Thread32First/Next`, `Module32First/Next`, `Heap32ListFirst/Next` |
| `@X` — the function address itself | 29 | **single**: `((fn_t)ef.X)(...)` | all `GetPE*`/`Get*Offset`/`GetCR*`/`GetSDT*`/`GetPhysicalAddress`, `StartProcessWatch`, `WaitForProcessListData`, `GetProcessNameFrom*`, `Kernel*`, `IsValidHandle`, `GetIDT*`, `MakeWritable`, `GetLoadedState`, `DBKSuspend/Resume*`, `KernelAlloc`, `GetKProcAddress` |
| `nil` | 10 | never callable | see "Permanently NULL" below |

Dereferencing a single-`@` field twice reads whatever the function's first bytes decode to as a
pointer and calls it — an immediate crash. Only the 25 hookable APIs take the `(*ef.X)` form the
examples below use.

### Commonly Used

| Field | Actual Type | Usage |
|-------|------------|-------|
| `ReadProcessMemory` | `BOOL(**)(HANDLE,LPCVOID,LPVOID,SIZE_T,SIZE_T*)` | Read target memory |
| `WriteProcessMemory` | `BOOL(**)(HANDLE,LPVOID,LPCVOID,SIZE_T,SIZE_T*)` | Write target memory |
| `VirtualQueryEx` | `DWORD(**)(HANDLE,LPCVOID,PMEMORY_BASIC_INFORMATION,DWORD)` | Query memory regions — **returns DWORD, not SIZE_T** (`NewKernelHandler.pas:590`) |
| `VirtualAllocEx` | `LPVOID(**)(HANDLE,LPVOID,DWORD,DWORD,DWORD)` | Allocate in target — **`dwSize` is DWORD** (`NewKernelHandler.pas:592`); a size >4 GB is truncated |
| `VirtualProtectEx` | `BOOL(**)(HANDLE,LPVOID,DWORD,DWORD,PDWORD)` | Change protection — **`dwSize` is DWORD** (`NewKernelHandler.pas:589`) |

> `ReadProcessMemory`, `WriteProcessMemory` and `VirtualQueryEx` point at CE's `*Actual` variables,
> not at CE's same-named wrappers (`plugin.pas:1880`, `:1881`,
> `:1894`: `@@ReadProcessMemoryActual`, `@@WriteProcessMemoryActual`, `@@VirtualQueryExActual`). Two
> consequences: (1) you automatically follow CE's mode switches — file-as-memory, physical memory,
> DBVM — because those re-point the same variables (`NewKernelHandler.pas:1982-1984, 2053-2055`);
> (2) you **skip** CE's `verifyAddress` guard and CR3 translation, which live in the wrapper
> `ReadProcessMemory` at `NewKernelHandler.pas:1467` (guard at `:1471-1475`, CR3/DBVM branch at
> `:1479-1494`). Validate addresses yourself.

### Correct Call Pattern (Double Dereference)

```cpp
// ReadProcessMemory — note the (*...) dereference
BOOL ok = (*g_Exported.ReadProcessMemory)(
    *g_Exported.OpenedProcessHandle,  // Also dereference PHANDLE
    (LPCVOID)address,
    buffer,
    size,
    &bytesRead
);

// VirtualQueryEx
MEMORY_BASIC_INFORMATION mbi;
// NewKernelHandler.pas:590 — DWORD return, DWORD length. Do NOT widen these to SIZE_T:
// on x64 the upper half of RAX is architecturally undefined for a 32-bit return,
// so a failed (0) query is not guaranteed to read back as 0.
typedef DWORD (__stdcall *VQEx_t)(HANDLE, LPCVOID, PMEMORY_BASIC_INFORMATION, DWORD);
VQEx_t vqex = *(VQEx_t*)g_Exported.VirtualQueryEx;
vqex(*g_Exported.OpenedProcessHandle, (LPCVOID)addr, &mbi, sizeof(mbi));
```

### Kernel/Driver Functions

Kernel pointers (`KernelOpenProcess`, `KernelReadProcessMemory`, `GetPEProcess`, `GetCR3`, …) are
**always non-NULL** — CE assigns them real wrapper addresses unconditionally (`plugin.pas:1898-1934`).
Without `loadDBK32()` they are callable but fail at run time; a NULL check will not protect you, so
check `GetLoadedState` instead.

**Permanently NULL — never callable, driver or not** (`plugin.pas:1890, 1891, 1902, 1905, 1908,
1911-1915`, several marked `//obsolete`): `StopDebugging`, `StopRegisterChange`,
`GetProcessnameOffset`, `ProtectMe`, `SetCR3`, `setAlternateDebugMethod`, `getAlternateDebugMethod`,
`DebugProcess`, `ChangeRegOnBP`, `RetrieveDebugData`. `FixMem` is likewise `nil`
(`plugin.pas:1872`, `//obsolete`).

> **`KernelVirtualAllocEx` is mis-wired in CE 7.5 — do not call it.** `plugin.pas:1923` assigns
> `@VQE`, which is DBK32's *VirtualQueryEx* (`dbk32/DBK32functions.pas:287`,
> `function {VirtualQueryEx}VQE(hProcess; address; var mbi; bufsize): dword`), not VirtualAllocEx.
> Calling it as an allocator makes the callee write a `MEMORY_BASIC_INFORMATION` through your size
> argument. Use `KernelAlloc` (`plugin.pas:1933`) for kernel allocation.

---

## 11. Enums & Constants

### Plugin Types

```cpp
typedef enum {
    ptAddressList         = 0,   // Right-click address list
    ptMemoryView          = 1,   // Memory Browser menu
    ptOnDebugEvent        = 2,   // Debug event callback
    ptProcesswatcherEvent = 3,   // Process watch callback
    ptFunctionPointerchange = 4, // API pointer change
    ptMainMenu            = 5,   // Main form menu
    ptDisassemblerContext = 6,   // Disassembler right-click
    ptDisassemblerRenderLine = 7,// Disassembler line render
    ptAutoAssembler       = 8    // AA phase hook
} PluginType;
```

### AutoAssembler Phases

```cpp
typedef enum {
    aaInitialize = 0,   // Script is about to be assembled — dispatched ONCE per script
    aaPhase1     = 1,   // Per line, 1st pass (parsing)   — dispatched
    aaPhase2     = 2,   // Per line, 2nd pass (execution) — dispatched
    aaFinalize   = 3    // Script finished — dispatched ONCE, in the `finally` block
} AutoAssemblerPhase;
```

> **All four phases are dispatched to Type 8 plugins.** `TPluginHandler.handleAutoAssemblerPlugin`
> (`plugin.pas:1716-1733`) has no phase filter — it forwards every phase to every registered Type 8
> callback (the only branch is the version test at `:1724`). Phase 0 fires once before parsing
> (`autoassembler.pas:1855`) and phase 3 fires once from the `finally` block
> (`autoassembler.pas:4448`, commented *"tell the plugins to free their data"*), so phase 3 runs
> **even when the script failed**. Use 0/3 as your per-script alloc/free pair — a plugin that
> ignores phase 3 leaks its per-script state on every AA run.

> **The `line` parameter is only valid in phases 1 and 2.** CE passes `@currentlinep` in all four
> phases, but `currentlinep` is `nil` at phase 0 (`currentlinep: pchar=nil`,
> `autoassembler.pas:1537` — it is not assigned until the first phase-1 line at `:2059`) and at
> phase 3 it is either still `nil` (a script whose lines were all empty never reaches
> `:2059`/`:3571`) or holds the pointer the last phase-2 line left behind (`:3571`), which may
> already dangle. In phases 0 and 3 read the `phase` and `id` arguments only — **never dereference
> `*line`**.

### Breakpoint Triggers

```cpp
typedef enum {
    bptExecute = 0,     // Break on execution
    bptAccess  = 1,     // Break on read/write access
    bptWrite   = 2      // Break on write only
} TBreakpointTrigger;
```

### Continue Options

```cpp
typedef enum {
    co_run      = 0,    // Continue running
    co_stepinto = 1,    // Single step (into calls)
    co_stepover = 2,    // Step over (skip calls)
    co_runtill  = 3     // Run until specific address
} TContinueOption;
```

### Value Types (for memory records)

> **WARNING:** `cepluginsdk.h` declares **no** value-type enum. There is no `enum MemRecValueType`
> anywhere in the header — the only two enums it declares are `PluginType` and `AutoAssemblerPhase`
> (`cepluginsdk.h:18-19`). The only value-type mapping the SDK gives you is a trailing `//` comment
> on the `PLUGINTYPE0_RECORD.valuetype` field, and **that comment was wrong from index 3 onward in
> the 7.5-era header. CE fixed it in 7.7** — it is the single line on which the 7.5 and 7.7 copies
> of `cepluginsdk.h` differ:
>
> ```c
> // cepluginsdk.h:35 — CE 7.5 source tree (WRONG from index 3 on):
>   char valuetype; //0=byte, 1=word, 2=dword, 3=float, 4=double, 5=bit, 6=int64, 7=string
>
> // cepluginsdk.h:35 — CE 7.7 install (fixed; agrees with CE's TVariableType):
>   char valuetype; //0=byte, 1=word, 2=dword, 3=int64, 4=float, 5=double, 6=string, 7=widestring, 8=bytearray, 9=binary
> ```
>
> The field carries a raw CE `TVariableType` ordinal — `MainUnit.pas:9743`
> `selectedrecord^.valuetype := integer(addresslist.selectedRecord.VarType);`, written back at
> `MainUnit.pas:9767-9768` — declared in `commontypedefs.pas:15` and reachable from a plugin through
> `ce_memrec_getType` (`pluginexports.pas:1041`) / `ce_memrec_setType` (`:1061`, which does
> `p.memrec.VarType:=Tvariabletype(p.vtype);` with **no range check**).
> See [ce-plugin-sdk-notes.md](ce-plugin-sdk-notes.md) §10 for details.

The list below is a transcription of that Pascal type. **The SDK will not give you this enum — you
have to declare it yourself**, and the name is yours to pick; CE's own name is `TVariableType`.

```cpp
// Hand-written from CE's Pascal TVariableType (commontypedefs.pas:15).
// NOT present in cepluginsdk.h in any form — do not grep for it there.
enum TVariableType {
    vtByte            = 0,
    vtWord            = 1,    // 2 bytes
    vtDword           = 2,    // 4 bytes
    vtQword           = 3,    // 8 bytes (int64/uint64)
    vtSingle          = 4,    // Float 32-bit
    vtDouble          = 5,    // Double 64-bit
    vtString          = 6,
    vtUnicodeString   = 7,
    vtByteArray       = 8,
    vtBinary          = 9,
    vtAll             = 10,
    vtAutoAssembler   = 11,   // AA script entries
    vtPointer         = 12,
    vtCustom          = 13,
    vtGrouped         = 14,
    vtByteArrays      = 15,   // "MultiByteArray" — CE 7.x
    vtCodePageString  = 16    // CE 7.x
};
```

### Control Align

```cpp
enum TAlign {
    alNone   = 0,
    alTop    = 1,
    alBottom = 2,
    alLeft   = 3,
    alRight  = 4,
    alClient = 5        // Fill remaining space
};
```

### Message Dialog Types & Buttons

These are **not** the Delphi `TMsgDlgType`/`TMsgDlgButtons` orderings. `ce_messageDialog` re-maps
the integer you pass through its own `case` statements, and that mapping is what a plugin sees.

```cpp
// messagetype parameter (ce_messageDialog, pluginexports.pas:2226):
enum TMsgDlgType {
    mtWarning      = 0,
    mtError        = 1,
    mtInformation  = 2,
    mtConfirmation = 3
    // anything else (incl. 4) falls through CE's `else` branch to mtInformation.
    // mtCustom is NOT reachable from a plugin.
};

// buttoncombination parameter (ce_messageDialog, pluginexports.pas:2235):
enum TMsgDlgButtons {
    mbOK          = 0,   // [mbOK]
    mbYesNo       = 1,   // [mbYes, mbNo]
    mbYesNoCancel = 2,   // [mbYes, mbNo, mbCancel]
    mbOKCancel    = 3    // [mbOK, mbCancel]
};
```

> **Never pass a value outside 0..3.** CE's `case` (`pluginexports.pas:2235-2240`) has no `else`
> branch, so `p.buttons` keeps the uninitialised stack contents of the local record declared at
> `pluginexports.pas:2218-2222` and hands that garbage set straight to `MessageDlg`
> (`pluginexports.pas:2214`). There is no mbAbortRetryIgnore or mbRetryCancel combination
> available to a plugin.

```cpp
// Return values (modal result):
enum TModalResult {
    mrOK     = 1,
    mrCancel = 2,
    mrAbort  = 3,
    mrRetry  = 4,
    mrIgnore = 5,
    mrYes    = 6,
    mrNo     = 7
};
```

> These are **LCL** `TModalResult` constants, not CE's. CE performs no translation — it returns
> whatever `MessageDlg` gives it (`pluginexports.pas:2214`, forwarded through `pluginsync` at
> `:2242`), so the authority is Lazarus `Controls.pas`, which is not part of the CE repo; these
> values were not verifiable against the CE tree. Every CE call site compares symbolically
> (e.g. `Assemblerunit.pas:8471`, `MainUnit.pas:3698`, both `=mrYes`). What you can rely on:
> a `buttoncombination` of 1 or 2 (the Yes/No family) answers with `mrYes`/`mrNo`, never 0/1.

---

## 12. Data Structures

### PluginVersion

```cpp
typedef struct _PluginVersion {
    unsigned int version;    // CESDK_VERSION (currently 6)
    char* pluginname;        // 0-terminated, MUST be static/global memory
} PluginVersion, *PPluginVersion;
```

### PLUGINTYPE0_RECORD (Address List Item)

```cpp
typedef struct _PLUGINTYPE0_RECORD {
    char* interpretedaddress;   // editable — 255-byte buffer, 254 chars + NUL usable (see below)
    UINT_PTR address;           // Read-only (resolved numeric address)
    BOOL ispointer;             // Read-only
    int countoffsets;           // Read-only
    ULONG* offsets;             // Read-only array [0..countoffsets-1]
    char* description;          // editable — 255-byte buffer, 254 chars + NUL usable (see below)
    char valuetype;             // Raw CE TVariableType ordinal — see §11
    char size;                  // Read-only. BYTE SIZE of the value, from TMemoryRecord.getByteSize
                                // (MemoryRecordUnit.pas:2787, reached via MainUnit.pas:9744):
                                //   byte=1  word=2  dword/single=4  qword/double=8
                                //   string  = Extra.stringData.length (x2 if unicode)
                                //   bytearray = extra.byteData.bytelength
                                //   binary  = 1+(extra.bitData.Bit + extra.bitData.bitlength div 8)
                                //   custom  = customtype.bytesize (0 if no custom type resolved)
                                // Every OTHER type falls off the end of the case and yields 0 —
                                // including vtUnicodeString, vtPointer, vtAll, vtAutoAssembler,
                                // vtGrouped. A 0 here does NOT mean "unknown length".
                                // NOTE: an `integer` narrowed into a byte — values >255 wrap.
} PLUGINTYPE0_RECORD, *PPLUGINTYPE0_RECORD;
```

> **Edits only apply if your callback returns TRUE.** `plugintype0click` (`MainUnit.pas:9755`) copies
> `interpretedaddress`, `description` and `valuetype` back into the memory record **inside**
> `if x.callback(selectedrecord) then` (block runs to `:9773`). Return FALSE and everything you wrote
> is silently dropped. `address`, `ispointer`, `countoffsets`, `offsets` and `size` are never
> written back at all.
>
> **Write at most 254 chars + NUL into the two string buffers.** They are `string[255]` locals
> (`MainUnit.pas:9708-9709`) handed over as `@buf[1]` (`:9729`), and CE stamps `buf[255] := #0`
> before measuring with `StrLen` (`:9758-9759`) — a full 255-char write loses its last character.

### REGISTERMODIFICATIONINFO

```cpp
typedef struct _REGISTERMODIFICATIONINFO {
    UINT_PTR address;           // OUTPUT, not input — CE overwrites this with the `address`
                                // argument you passed to ChangeRegistersAtAddress
                                // (pluginexports.pas:729). The struct must be writable memory.

    // Change flags — set TRUE to modify the register
    BOOL change_eax, change_ebx, change_ecx, change_edx;
    BOOL change_esi, change_edi, change_ebp, change_esp, change_eip;
#ifdef _AMD64_
    BOOL change_r8, change_r9, change_r10, change_r11;
    BOOL change_r12, change_r13, change_r14, change_r15;
#endif
    BOOL change_cf, change_pf, change_af, change_zf, change_sf, change_of;

    // New register values (only applied if corresponding change flag is TRUE)
    UINT_PTR new_eax, new_ebx, new_ecx, new_edx;
    UINT_PTR new_esi, new_edi, new_ebp, new_esp, new_eip;
#ifdef _AMD64_
    UINT_PTR new_r8, new_r9, new_r10, new_r11;
    UINT_PTR new_r12, new_r13, new_r14, new_r15;
#endif
    BOOL new_cf, new_pf, new_af, new_zf, new_sf, new_of;
} REGISTERMODIFICATIONINFO, *PREGISTERMODIFICATIONINFO;
```

> **IMPORTANT:** This struct has different sizes on x86 vs x64. x64 includes R8-R15 fields and uses 8-byte `UINT_PTR` instead of 4-byte.

---

## 13. Thread Safety Model

### Main Thread Synchronization

CE wraps **most, not all**, exported functions in `pluginsync()` — 58 of the 91 `ce_*` entry points
in `pluginexports.pas`. The rule of thumb: functions that **mutate CE state or touch the GUI** are
wrapped; **read-only queries are not**.

> These three counts are **derived**, not hand-maintained. Regenerate them by parsing
> `pluginexports.pas` below its `implementation` line (`:133`) for `function|procedure ce_… stdcall;`
> headers, splitting each body at the next header, and testing each body for the string
> `pluginsync`. Against the 7.5 tree that yields 91 / 58 / 33.

The mechanism, for the 58 that are wrapped (`pluginexports.pas:880-890`):

```
1. Check GetCurrentThreadId() == MainThreadID
2. If same thread → call func(parameters) directly
3. If different → SendMessage(mainform.handle, wm_pluginsync, func, parameters)
     wm_pluginsync = WM_USER + 3   (MainUnit.pas:62)
     Both are passed BY VALUE. (CE writes `@func`, but in `{$MODE Delphi}` `@` on a
     procedural variable yields the function address, not the variable's address —
     the handler casts wparam straight back to a callable: MainUnit.pas:2162.)
     The handler runs func(params) and returns its result as m.Result.
     SendMessage blocks until the main thread pumps the message.
```

**Consequence:** the 58 wrapped functions are thread-safe but **block** on the main thread. The other
33 run on *your* thread with no protection. Notable unwrapped entries that nevertheless touch the
GUI or CE state, and are therefore **unsafe off the main thread**:

| Function | What it does unsynchronized |
|----------|------------------------------|
| `RegisterFunction` / `UnregisterFunction` | creates/frees `TMenuItem` on `mainform`/`memorybrowser` (`plugin.pas:999-1002`, `1229`) |
| `ChangeRegistersAtAddress` | constructs `tfrmModifyRegisters` and clicks its button (`pluginexports.pas:651`, `732`) |
| `AutoAssemble`, `InjectDLL`, `reloadsettings`, `freezemem`/`unfreezemem` | mutate CE state directly |
| `Disassembler`, `disassembleEx`, `nextOpcode` | run on the caller's thread **and share one global `TDisassembler`** (`pluginexports.pas:145`, created once at `:2638`, used unlocked at `:798`, `:818`, `:841`) — two threads calling these concurrently corrupt each other's state |
| `sym_nameToAddress`, `sym_addressToName`, `previousOpcode`, `ProcessList`, `getaddressfrompointer`, all `memrec_get*`, `memrec_setValue`, `memrec_setScript` | read-only-ish; run on the caller's thread |

Call the GUI-touching ones from the main thread only — or route them through `MainThreadCall`.

### Plugin Callback Threading

| Plugin Type | Thread | Notes |
|-------------|--------|-------|
| Type 0 (Address List) | Main thread | Menu click handler |
| Type 1 (Memory Browser) | Main thread | Menu click handler |
| Type 2 (Debug Event) | **Debugger thread** | Must be thread-safe! |
| Type 3 (Process Watcher) | **Process-watcher worker thread** | Not synchronized — same hazard as Type 2 |
| Type 4 (Pointer Change) | Caller's thread (usually main) | Not synchronized (`plugin.pas:1813-1831`; the callback fires inline at `:1827`); the `int` argument is a **section id** — CE passes 0 and 3..10 (`NewKernelHandler.pas:1964, 1989, 2059, 2085, 2129, 2151, 2170, 2197, 2230, 2278`), not "reserved" |
| Type 5 (Main Menu) | Main thread | Menu click handler |
| Type 6 (Disasm Context) | Main thread | Menu handler |
| Type 7 (Disasm Render) | Main thread | GUI render cycle |
| Type 8 (AutoAssembler) | **Whatever thread ran the AA script** | NOT synchronized — can run concurrently on several threads |

### Rules

1. **Type 2 callback:** Runs on the debugger thread — be careful with exported functions that sync to the main thread. `pluginsync` uses `SendMessage(mainform.handle, wm_pluginsync, …)` (`pluginexports.pas:889`), which blocks only until the main thread pumps; it **deadlocks when the main thread is itself waiting on the debugger thread** (e.g. `debuggerthread.WaitFor`, `MainUnit.pas:7780`). That is the concrete case behind Pitfall #12's "potential deadlock".
2. **Type 3 callback:** Runs on `tprocesswatchthread` (`frmProcessWatcherUnit.pas:279`), dispatched *outside* the surrounding `synchronize` (`:322` vs `:324`) — `TPluginHandler.handlenewprocessplugins` only takes `pluginCS` and does not marshal. Treat it exactly like Type 2.
3. **Type 7 callback:** Keep fast — called per-line during rendering.
4. **Type 8 callback:** CE's own comment (`autoassembler.pas:3575`) — *"note that this can be called in a multithreaded situation, so the plugin must hld storage containers on a threadid base and handle the locking itself"* (CE's typo, quoted verbatim). Two AA scripts can be in flight at once; key your per-script state on the `id` argument (`aaid:=InterLockedIncrement(nextaaid)`, `autoassembler.pas:1854`), not on a global.
5. **Types 0, 1, 5, 6, 7:** run on the main thread — any exported function is safe. **Type 4** is not marshalled either, but every call site in CE 7.5 is a main-thread settings/driver path; do not *rely* on it. **Types 2, 3, 8** run off the main thread: the 58 `pluginsync`-wrapped functions will marshal (and can deadlock, see rule 1); the 33 unwrapped ones will not, and the GUI-touching subset of those is unsafe. Prefer `MainThreadCall` from these three.

---

## 14. Quick-Start Template

```cpp
#include "cepluginsdk.h"
#include <cstring>

static ExportedFunctions g_Exported;
static int g_PluginId = -1;
static int g_DisasmContextId = -1;
static char g_PluginName[] = "My Plugin";

// --- Type 6 callback: Disassembler context menu ---
BOOL __stdcall OnDisasmContextPopup(
    UINT_PTR selectedAddress, char** addressofname, BOOL* show)
{
    *show = TRUE;  // Always show
    return TRUE;
}

BOOL __stdcall OnDisasmContextClick(UINT_PTR* selectedAddress)
{
    // Read instruction bytes at selected address
    BYTE buf[16] = {};
    SIZE_T bytesRead = 0;
    (*g_Exported.ReadProcessMemory)(
        *g_Exported.OpenedProcessHandle,
        (LPCVOID)*selectedAddress,
        buf, sizeof(buf), &bytesRead);

    // Get module+offset name
    char name[256] = {};
    g_Exported.sym_addressToName(*selectedAddress, name, sizeof(name));

    // Disassemble
    char mnemonic[256] = {};
    g_Exported.Disassembler(*selectedAddress, mnemonic, sizeof(mnemonic));

    // Navigate forward
    // NOTE: cepluginsdk.h declares CEP_PREVIOUSOPCODE/CEP_NEXTOPCODE as returning DWORD
    // (cepluginsdk.h:185-186), but CE returns ptrUint (pluginexports.pas:833/838).
    // Patch the header to UINT_PTR, or cast through the correct type as below,
    // or every address above 4 GB is truncated. See Pitfall #6.
    typedef UINT_PTR (__stdcall *NextOpcode_t)(UINT_PTR);
    UINT_PTR next = ((NextOpcode_t)g_Exported.nextOpcode)(*selectedAddress);
    // next = address of the instruction after selectedAddress

    return TRUE;
}

// --- Required exports ---
extern "C" __declspec(dllexport)
BOOL __stdcall CEPlugin_GetVersion(PPluginVersion pv, int sizeofpluginversion)
{
    pv->version = CESDK_VERSION;
    pv->pluginname = g_PluginName;
    return TRUE;
}

extern "C" __declspec(dllexport)
BOOL __stdcall CEPlugin_InitializePlugin(PExportedFunctions ef, int pluginid)
{
    // Hygiene: validate before copying. (Against CE 7.5 the copy is safe either way —
    // TPluginHandler.EnablePlugin, plugin.pas:1364, always passes a complete
    // texportedfunctions record and merely rewrites sizeofExportedFunctions downward
    // to match the version you declared in CEPlugin_GetVersion, plugin.pas:1372-1388.)
    if (ef->sizeofExportedFunctions < (int)sizeof(ExportedFunctions))
        return FALSE;

    g_Exported = *ef;
    g_PluginId = pluginid;

    // Register Type 6 (Disassembler context menu)
    DISASSEMBLERCONTEXT_INIT init = {};
    init.name = const_cast<char*>("My Plugin Action");
    init.callbackroutine = OnDisasmContextClick;
    init.callbackroutineOnPopup = OnDisasmContextPopup;
    init.shortcut = const_cast<char*>("Ctrl+Shift+A");

    g_DisasmContextId = g_Exported.RegisterFunction(
        pluginid, ptDisassemblerContext, &init);

    return (g_DisasmContextId != -1) ? TRUE : FALSE;
}

extern "C" __declspec(dllexport)
BOOL __stdcall CEPlugin_DisablePlugin(void)
{
    // Optional: CE unregisters everything itself right after this returns
    // (plugin.pas:1439-1464), and it is already holding pluginCS (entered at :1429),
    // so doing it here re-enters that lock on the same thread. pluginCS is a
    // TCriticalSection (plugin.pas:1855), which on Windows is recursive, so this is safe.
    // Always return TRUE — returning FALSE makes CE raise and leaves the plugin
    // enabled with its functions still registered (plugin.pas:1433).
    if (g_DisasmContextId != -1) {
        g_Exported.UnregisterFunction(g_PluginId, g_DisasmContextId);
        g_DisasmContextId = -1;
    }
    return TRUE;
}

BOOL APIENTRY DllMain(HMODULE hModule, DWORD reason, LPVOID reserved)
{
    return TRUE;
}
```

### CMakeLists.txt

```cmake
cmake_minimum_required(VERSION 3.20)
project(MyPlugin LANGUAGES CXX)

add_library(MyPlugin SHARED
    plugin.cpp
    cepluginsdk.h
)

target_compile_definitions(MyPlugin PRIVATE _AMD64_)
set_target_properties(MyPlugin PROPERTIES
    OUTPUT_NAME "MyPlugin"
    SUFFIX ".dll"
)

# Static link CRT (avoid vcruntime dependency)
set(CMAKE_MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")

# UTF-8 source files
target_compile_options(MyPlugin PRIVATE /utf-8)
```

---

## 15. Common Pitfalls Checklist

| # | Pitfall | Fix |
|---|---------|-----|
| 1 | `GetVersion` 2nd param treated as pointer | It's `int`, not `int*` |
| 2 | `pluginname` on stack | Use `static char[]` or `global` |
| 3 | ExportedFunctions struct layout wrong | Use exact official SDK header — never modify field **order or size**. Return-type corrections (row 6) are safe: they do not move any field. |
| 4 | `ptDisassemblerContext` = 5 | It's `6`, not 5. Value 5 = `ptMainMenu` |
| 5 | `previousOpcode`/`nextOpcode` return delta | They return **address** — use `prev = previousOpcode(prev)` |
| 6 | `previousOpcode`/`nextOpcode` return `DWORD` | CE returns `UINT_PTR` — fix SDK header for x64 |
| 7 | `ReadProcessMemory` single dereference | Double pointer: `(*ef->ReadProcessMemory)(...)` |
| 8 | `OpenedProcessHandle` used directly | It's `PHANDLE` — dereference: `*ef->OpenedProcessHandle` |
| 9 | `char*` params with string literals | SDK uses non-const `char*` — use `const_cast<char*>()` |
| 10 | Type 6 popup callback version mismatch | SDK ≤5 has no `show` param — check version |
| 11 | Type 8 callback version mismatch | SDK ≤5 has no `id` param — check version |
| 12 | Calling exported functions from Type 2 callback | Type 2 runs on the debugger thread. The 58 `pluginsync`-wrapped functions block on the main thread → potential deadlock (concretely: the main thread doing `debuggerthread.WaitFor`, `MainUnit.pas:7780`). The other 33 do not sync at all and run unprotected. See §13 rule 1. |
| 13 | Type 7 callback doing heavy work | Called per-line during render — keep < 1ms |
| 14 | `memrec_freeze` direction semantics | 0=normal, 1=allow increase only, 2=allow decrease only |
| 15 | `GetLuaState` calling convention | The header's `__fastcall` (`cepluginsdk.h:265`, the only one in the header) is a **header bug**. CE's function is `__stdcall` like every other export (`pluginexports.pas:2632`, `cepluginsdk.pas:341`) — declare it `__stdcall`. Harmless either way because it takes no arguments. See §9. |
| 16 | Lua header dependencies | `cepluginsdk.h` includes `lua.h` — provide stubs if not using Lua |
| 17 | Missing `_AMD64_` define | `REGISTERMODIFICATIONINFO` layout changes with/without it |
| 18 | `form_onClose`/`control_onClick`/`timer_onTimer` callback type | It is `void __stdcall callback(PVOID sender)` — **one** parameter (`TNotifyCall2`, `pluginexports.pas:148`; fields at `:156-158`; invoked with the arg at `:231`/`:239`/`:254`). The SDK header never declares this signature — it just says `PVOID function` (`cepluginsdk.h:228`/`:245`/`:259`). Declaring `(void)` under `__stdcall` corrupts the stack on x86 and loses `sender` on x64. |
| 19 | Changing a control's `Tag` | CE indexes its callback table by `TControl(sender).tag` (assigned at `pluginexports.pas:222`, read at `:231`/`:239`/`:254`). Overwrite the Tag and the next event dereferences the wrong slot. |
| 20 | Expecting a form to survive its close handler | `TComponentFunctionHandlerClass.OnClose` sets `action := caFree` unconditionally, after your handler returns (`pluginexports.pas:243`) — the form is destroyed. |
| 21 | Registering `timer_onTimer` before you're ready | `setOnTimer` does `control.enabled := true` (`pluginexports.pas:291`) — the timer starts firing the moment you attach the handler. |
| 22 | Assuming every exported function is thread-synchronized | Only 58 of 91 are wrapped in `pluginsync` — see §13. |
| 23 | Calling `KernelVirtualAllocEx` as an allocator | It is wired to DBK32's *VirtualQueryEx* (`plugin.pas:1923`) — see §10. |
| 24 | Passing `buttoncombination` 4 or 5 to `messageDialog` | CE's `case` has no `else`; the button set is uninitialised stack — see §11. |

---

## Appendix: Version Compatibility Matrix

| Feature | V1 | V2 | V3 | V4 | V5 | V6 |
|---------|----|----|----|----|----|----|
| Core functions | Y | Y | Y | Y | Y | Y |
| sym_nameToAddress/addressToName | - | Y | Y | Y | Y | Y |
| previousOpcode | see note | - | Y | Y | Y | Y |
| nextOpcode | - | - | Y | Y | Y | Y |
| aa_AddExtraCommand | - | - | Y | Y | Y | Y |
| Memory record functions | - | - | - | Y | Y | Y |
| Debug breakpoint functions | - | - | - | Y | Y | Y |
| UI creation functions | - | - | - | Y | Y | Y |
| GetLuaState | - | - | - | - | Y | Y |
| MainThreadCall | - | - | - | - | Y | Y |
| ExecuteKernelCode / UserdefinedInterruptHook | - | - | - | - | Y | Y |
| Type 6 popup `show` param | - | - | - | - | - | Y |
| Type 8 callback `id` param | - | - | - | - | - | Y |

> **Note on `previousOpcode` in V1.** `TExportedFunctions1` really does end with
> `previousOpcode` (`plugin.pas:710`) — a V1-only historical quirk that V2 dropped
> (`plugin.pas:522-617`, whose tail is the `//version 2 extension` block at `:613-616`) and V3
> re-added under `//version 3 extension` (`plugin.pas:506-514`). **Do not use it.** CE always
> passes the modern struct and merely shrinks `sizeofExportedFunctions` (`plugin.pas:1372-1388`),
> and the leading fields are identical across V1 and V5, so V1's `previousOpcode` slot now holds
> `sym_nameToAddress` (`cepluginsdk.h:370`, right after `memorybrowser` at `:367`). A version-1
> plugin calling `previousOpcode` therefore calls `ce_sym_nameToAddress` with the wrong signature.
> Declare version 3 or higher.

> **The V5 row is four fields, not one.** The tail of `TExportedFunctions5` is
> `plugin.pas:221-225` — `//version 5`, then `ExecuteKernelCode`, `UserdefinedInterruptHook`,
> `GetLuaState`, `MainThreadCall` — mirrored in `cepluginsdk.h:452-456`
> (`//V5: Todo, implement function declarations`). `MainThreadCall` is documented in §9 but had no
> matrix row, which read as "present since V1".

> **V6 is a callback-ABI bump, not a struct bump.** `plugin.pas:230` declares
> `TExportedFunctions = TExportedFunctions5; //<----adjust on new version`, so `ExportedFunctions`
> is byte-identical between V5 and V6 and the two rows above are the *only* differences. CE branches
> on the version you report from `CEPlugin_GetVersion` at exactly two places — `plugin.pas:1751`
> (Type 6 popup gains `show`) and `plugin.pas:1724` (Type 8 gains `id`). Declaring 6 while using the
> 5-argument callback shapes mismatches the stack; declaring 5 while using the 6-shapes reads
> uninitialised arguments. CE rejects anything above 6 outright
> (`if PluginVersion.version>currentpluginversion then raise`, `plugin.pas:1646`, with
> `CurrentPluginVersion=6` at `plugin.pas:17`).

> **CE 7.5 → 7.7: the C ABI did not move.** `plugin/cepluginsdk.pas` is byte-identical between the
> 7.5 source tree and the 7.7 install, and `cepluginsdk.h` differs on **exactly one line** — the
> `PLUGINTYPE0_RECORD.valuetype` comment (§11). Every `ExportedFunctions` member, every typedef and
> every `*_INIT` struct is unchanged, and `CESDK_VERSION` is still `6` (`cepluginsdk.h:16`). One
> binary built against this header runs on both. Regenerate the claim with
> `diff "<ce-src>/Cheat Engine/plugin/cepluginsdk.h" "C:\Program Files\Cheat Engine\plugins\cepluginsdk.h"`.
