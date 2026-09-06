# Cheat Engine Plugin SDK — Development Notes

**Why this doc is in this repo.** UE5CEDumper does not build a Cheat Engine plugin. It *emits* CE
artifacts — AA scripts with `{$lua}` blocks, `.CT` cheat tables, CE XML pointer chains, Structure
Dissect files — and it talks to the AOBMaker CE plugin over a named pipe. This is the pitfalls
companion to the CE plugin API reference: the traps you only learn by reading CE's Pascal instead of
its C header. Three of the facts pinned down here are load-bearing for what we emit — CE's real
`TVariableType` ordering (which the cheat-table generator encodes), the Lua-state threading rules,
and the disassembler/symbol output formats. Cross-reference
[docs/aobmaker-integration.md](aobmaker-integration.md) and
[docs/CE-Bugs-Minesweeper.md](CE-Bugs-Minesweeper.md).

Master copy: `<private-ce-repo>/docs/CE-Plugin-SDK-Notes.md` — edit there first, then
mirror here.

This document records the SDK details and known traps you must be aware of when developing a CE
plugin DLL in C++. Everything here comes from the observed behaviour of CE's own source code
(Delphi/Pascal), not merely from the declarations in the C SDK header.

> **Version coordinates (the source of every conclusion in this document)**
>
> | Item | Value |
> |------|-------|
> | CE source tree read | `D:\Github\cheat-engine`, tag **`7.5-195`**, HEAD `4178e037` (level with `upstream/master`) |
> | CE installed binaries cross-checked | `C:\Program Files\Cheat Engine\`, **7.7.0.10568** (ProductVersion 7.7) |
> | Plugin SDK version | `CESDK_VERSION = 6` (`cepluginsdk.h:16`) — unchanged in 7.7 |
>
> In other words: **the source read is 7.5, the shipping binary is 7.7**.
>
> **The 7.5 and 7.7 plugin SDKs are nearly identical**: `cepluginsdk.pas` is **byte-identical** on
> both sides; `cepluginsdk.h` differs by **exactly one comment line** (see §10). All 159
> `ExportedFunctions` members, every typedef and the C ABI of every INIT struct are identical in 7.5
> and 7.7 — a plugin built against this header runs on both, which is probably the most useful
> compatibility fact in this document.
>
> Main reference files: `plugin.pas`, `pluginexports.pas`, `disassembler.pas`,
> `commontypedefs.pas`, `LuaHandler.pas`, `MemoryBrowserFormUnit.pas`, `symbolhandler.pas`

---

## 1. The Three Plugin Export Functions

When CE loads the DLL it calls **only** `GetVersion`; the other two exports are just resolved with
`GetProcAddress` and stored away (all three prefer the `CEPlugin_`-prefixed name and only fall back
to the unprefixed one; plugin.pas:1636-1671).

| When | Export called | Source |
|------|---------------|--------|
| Loading the DLL (LoadPlugin) | `CEPlugin_GetVersion` | plugin.pas:1644 |
| User **enables** it (tick / autoload) | `CEPlugin_InitializePlugin` | plugin.pas:1398 (`EnablePlugin`) |
| User **disables** it / unload | `CEPlugin_DisablePlugin` | plugin.pas:1433 / 1413 |

> If `pv->version` is greater than CE's `CurrentPluginVersion` (7.5 = **6**, plugin.pas:17),
> CE refuses to load it outright (plugin.pas:1646-1647).

### 1.1 `CEPlugin_GetVersion`

```
Delphi signature:
  TGetVersion = function(var PluginVersion: TPluginVersion; TPluginVersionSize: integer): BOOL; stdcall;

TPluginVersion = record
  version: dword;        // Plugin SDK version (currently 6 at most)
  pluginname: pchar;     // points at a 0-terminated string (must not be on the stack)
end;
```

**Correct C++ implementation:**

```cpp
extern "C" __declspec(dllexport)
BOOL __stdcall CEPlugin_GetVersion(PPluginVersion pv, int sizeofpluginversion) {
    pv->version = CESDK_VERSION;  // = 6
    pv->pluginname = g_PluginName; // must be static/global, never a stack variable
    return TRUE;
}
```

> **TRAP:** the second parameter is the integer value of `sizeof(TPluginVersion)` (x64 = 16), **not a
> pointer**. Declaring it as `int*` and dereferencing writes to address 0x10 → Access Violation.
> That is exactly the root cause of the "tried to write to address 00000010" crash.

### 1.2 `CEPlugin_InitializePlugin`

```
Delphi signature:
  TInitializePlugin = function(var ExportedFunctions: TExportedFunctions; pluginid: dword): BOOL; stdcall;
```

**Correct C++ implementation:**

```cpp
extern "C" __declspec(dllexport)
BOOL __stdcall CEPlugin_InitializePlugin(PExportedFunctions ef, int pluginid) {
    g_Exported = *ef;  // copy the whole struct
    g_PluginId = pluginid;

    // Version check: the struct CE filled in must be at least as large as our header's
    if (g_Exported.sizeofExportedFunctions < (int)sizeof(ExportedFunctions))
        return FALSE;

    // Register features...
    return TRUE;
}
```

### 1.3 `CEPlugin_DisablePlugin`

```
Delphi signature:
  TDisablePlugin = function: BOOL; stdcall;
```

```cpp
extern "C" __declspec(dllexport)
BOOL __stdcall CEPlugin_DisablePlugin(void) {
    // Unregister everything that was registered
    if (g_DisasmContextId != -1) {
        g_Exported.UnregisterFunction(g_PluginId, g_DisasmContextId);
        g_DisasmContextId = -1;
    }
    return TRUE;
}
```

---

## 2. PluginType Enumeration Values

```
ptAddressList         = 0   // address list right-click
ptMemoryView          = 1   // Memory Viewer menu
ptOnDebugEvent        = 2   // Debug event callback
ptProcesswatcherEvent = 3   // Process watcher
ptFunctionPointerchange = 4 // Function pointer change notification
ptMainMenu            = 5   // CE main menu
ptDisassemblerContext = 6   // Disassembler right-click menu ★
ptDisassemblerRenderLine = 7 // Disassembler render line
ptAutoAssembler       = 8   // Auto Assembler phase callback
```

> **TRAP:** `ptDisassemblerContext` is **6**, not 5.
> Value 5 is `ptMainMenu`; pass the wrong one and CE interprets your init pointer with the wrong
> struct layout.

---

## 3. ExportedFunctions Struct Layout

The `ExportedFunctions` struct CE hands to the plugin is very large (**159 members, 1272 bytes on
x64**). **You must never trim or reorder the fields yourself** — it has to match
`TExportedFunctions5` in CE's source exactly.

### Field order (abridged)

```
offset  field
──────  ──────────────────────────
[0]     sizeofExportedFunctions (int)
[8]     ShowMessage
[16]    RegisterFunction
[24]    UnregisterFunction
[32]    OpenedProcessID (PULONG)
[40]    OpenedProcessHandle (PHANDLE)
[48]    GetMainWindowHandle
[56]    AutoAssemble
[64]    Assembler
[72]    Disassembler
[80]    ChangeRegistersAtAddress
[88]    InjectDLL
[96]    FreezeMem
[104]   UnfreezeMem
[112]   FixMem
[120]   ProcessList
[128]   ReloadSettings
[136]   GetAddressFromPointer
[144]   ReadProcessMemory          ← pointer-to-pointer
[152]   WriteProcessMemory         ← pointer-to-pointer
[160]   GetThreadContext            ← pointer-to-pointer
...     (`ReadProcessMemory`[144] ~ `Heap32ListNext`[648], **64** kernel/driver pointer-to-pointers in total)
...     mainform, memorybrowser
...     sym_nameToAddress          ← Version 2+
...     sym_addressToName
...     sym_generateAPIHookScript
...     loadDBK32                  ← Version 3+
...     loaddbvmifneeded
...     previousOpcode
...     nextOpcode
...     disassembleEx
...     loadModule
...     aa_AddExtraCommand
...     aa_RemoveExtraCommand
...     createTableEntry           ← Version 4+
...     (memrec_* family)
...     (debug_* family)
...     (UI creation family)
...     ExecuteKernelCode          ← Version 5+
...     UserdefinedInterruptHook
...     GetLuaState
...     MainThreadCall
```

> **TRAP:** if the number or order of fields in your header is wrong, then after `g_Exported = *ef`
> every function pointer points at the wrong slot and calling any of them crashes.

> **These two numbers (159 / 1272) are DERIVED — do not hand-edit them.**
> How to recompute: `TExportedFunctions5` = plugin.pas:47-226,
> `awk 'NR>=48 && NR<=226' plugin.pas | grep -cE '^\s+[A-Za-z_][A-Za-z0-9_]*\s*:'` = **159**;
> size = 1 `integer`(4) + padding(4) + 158 pointers × 8 = **1272**.
> `TExportedFunctions = TExportedFunctions5` (plugin.pas:230).
>
> This repo's CI gate `tools/check_derived_counts.py` does **not** cover these numbers, because it
> only derives from the UE5CEDumper tree and every number in this document comes from the external
> CE / AOBMaker trees — so they must be re-derived by hand with the commands given here.
>
> The **159 field names and their order in `TExportedFunctions5` match the C struct at
> `cepluginsdk.h:275-456` field for field** (a name-by-name diff came back empty), so the "must match
> `TExportedFunctions5` exactly" above is correct — what is actually wrong is only the **typedef
> signature of individual fields** (see the table in §9).

### How to validate

CE does **not** fill `sizeofExportedFunctions` from the size of its own struct; it fills it from
**the version your GetVersion reported**
(plugin.pas:1364-1389, `TPluginHandler.EnablePlugin`):

```pascal
e := exportedfunctions;   //save it to prevent plugins from fucking it up
case plugins[pluginid].pluginversion of
  1: e.sizeofExportedFunctions := sizeof(TExportedFunctions1);
  2: e.sizeofExportedFunctions := sizeof(TExportedFunctions2);
  3: e.sizeofExportedFunctions := sizeof(TExportedFunctions3);
  4: e.sizeofExportedFunctions := sizeof(TExportedFunctions4);
  5: e.sizeofExportedFunctions := sizeof(TExportedFunctions5);
else e.sizeofExportedFunctions := sizeof(TExportedFunctions);  //= TExportedFunctions5
end;
```

(The constructor's initial value `exportedfunctions.sizeofExportedFunctions := sizeof(TExportedFunctions);`
is at plugin.pas:1856, but the block above overwrites it, and only then does plugin.pas:1398 pass
`e` to you.)

Note that `TExportedFunctions = TExportedFunctions5` (plugin.pas:230), so **version 5 and 6 receive
the same value, 1272**. The memory itself is **always a complete TExportedFunctions5**; only the
reported size shrinks.

Therefore:

```cpp
// Our GetVersion reports CESDK_VERSION = 6 → CE always fills sizeof(TExportedFunctions5) = 1272
// At that point != and < are equivalent; CE's own official C sample uses != (plugin/example-c/example-c.c:126)
if (ef->sizeofExportedFunctions != sizeof(ExportedFunctions))
    return FALSE;   // header does not match CE's struct
```

> **The following is supplementary, not a correction**: for a plugin reporting `CESDK_VERSION = 6`
> (the case in this document), the `!=` here and the `<` in §1.2 **produce the same result and are
> both correct**. CE only deliberately fills a smaller value when you report version 1–4, and only
> then would `!=` misjudge a perfectly healthy CE as incompatible.
> If in doubt, use the `<` from §1.2 ("at least as large as our header"); it never falsely rejects.
> The two operators differing is just prose inconsistency — functionally there is no difference for v6.

---

## 4. previousOpcode / nextOpcode Semantics

### SDK header declaration (wrong)

```c
// original SDK:
typedef DWORD (__stdcall *CEP_PREVIOUSOPCODE)(UINT_PTR address);
typedef DWORD (__stdcall *CEP_NEXTOPCODE)(UINT_PTR address);
```

### CE's actual Delphi implementation

```pascal
// The return type is ptrUint (= UINT_PTR), not DWORD!
function ce_previousOpcode(address: ptrUint): ptrUint; stdcall;
begin
  result := previousopcode(address);  // returns the ADDRESS of the previous instruction
end;

function ce_nextOpcode(address: ptrUint): ptrUint; stdcall;
var x: string;
begin
  plugindisassembler.disassemble(address, x);  // address is advanced to the next instruction
  result := address;                           // returns the ADDRESS of the next instruction
end;
```

### Correct usage

```cpp
// previousOpcode: returns the FULL ADDRESS of the previous instruction, not a delta
UINT_PTR prevAddr = g_Exported.previousOpcode(currentAddr);
// prevAddr = start address of the previous instruction

// nextOpcode: returns the FULL ADDRESS of the next instruction, not a delta
UINT_PTR nextAddr = g_Exported.nextOpcode(currentAddr);
// nextAddr = start address of the next instruction

// Computing instruction length:
UINT_PTR instrLen = nextAddr - currentAddr;
```

> **TRAP 1:** the SDK declares the return type as `DWORD` (32-bit), but the actual return is a 64-bit
> address. On x64 it must be changed to `UINT_PTR`, otherwise the top 32 bits are truncated and the
> address is wrong.
>
> **TRAP 2:** the return value is an **address**, not a delta/size.
> Misusing it as `addr += nextOpcode(addr)` gives `addr + nextAddr` = a completely wrong location.

### Collecting instructions backwards

```cpp
UINT_PTR prev = selectedAddr;
for (int i = 0; i < N; i++) {
    UINT_PTR prevAddr = g_Exported.previousOpcode(prev);
    if (prevAddr == 0 || prevAddr >= prev) break;  // note: neither condition can ever hold, see below
    prev = prevAddr;
    addrs.push_back(prev);
}
std::reverse(addrs.begin(), addrs.end());  // far-to-near → reversed to near-to-far
```

> **TRAP 3 (`previousOpcode` has no failure return value):** that "guard" line above is in fact
> **dead code** — neither condition can ever be true.
> When `ce_previousOpcode` (pluginexports.pas:833-836) calls `previousopcode(address)`, `d` takes its
> default `nil`, and disassembler.pas:15790-15840 shows that when all four `previousOpcodeHelp`
> attempts (80/40/20/10 bytes) fail, it falls back to `result := address - 1;`
> (disassembler.pas:15823) and then does a 20-iteration nearest-match scan.
> **It never returns 0, and never returns a value ≥ `address`** — so "total failure" and "success"
> look exactly alike, and the loop will happily walk backwards one byte at a time into the middle of
> an instruction.
>
> **You have to validate it yourself**: after getting `prevAddr`, disassemble forward once
> (`nextOpcode(prevAddr)`) and require that it lands exactly back on `prev`; if it does not, treat it
> as a failure and stop.
>
> **TRAP 4 (`previousOpcode` mutates CE's shared disassembler):** these four functions do **not** all
> use the same object. `ce_nextOpcode` (pluginexports.pas:838-843), `ce_disassembler` (:793) and
> `ce_disassemble` (:811) all use the plugin-specific `plugindisassembler` (created at
> pluginexports.pas:2637-2643). `ce_previousOpcode` does not — it goes into `previousopcode`, where
> `if d=nil then d:=defaultDisassembler;` (disassembler.pas:15799-15800), and then **writes to CE's
> shared disassembler**: `aggressive:=d.aggressivealignment; d.aggressivealignment:=true;`
> (15802-15803), restoring it only on the way out. And `TDisassembler` **has no lock at all** — the
> critical section in its constructor is commented out
> (disassembler.pas:16555 `// cs:=TCriticalSection.Create;`).
> So calling `previousOpcode` from a plugin background thread races CE's own disassembly and can
> briefly flip aggressive alignment for the whole app. **Only call it on the main thread.**

### Collecting instructions forwards (correct)

```cpp
UINT_PTR next = selectedAddr;
for (int i = 0; i < N; i++) {
    UINT_PTR nextAddr = g_Exported.nextOpcode(next);
    if (nextAddr == 0 || nextAddr <= next) break;  // guard: invalid or did not advance
    next = nextAddr;
    lines.push_back(FormatLine(next));
}
```

### `disassembleEx` — the SDK header's parameter type is wrong

```c
// cepluginsdk.h:188 (wrong):
typedef BOOL (__stdcall *CEP_DISASSEMBLEEX)(UINT_PTR address, char *output, int maxsize);

// CE's actual implementation (pluginexports.pas:811, assigned by plugin.pas:1965):
// function ce_disassemble(address: pptrUint; output: pchar; maxsize: integer): BOOL; stdcall;
//   a := address^;  ...  address^ := a;   // both in and out are through the pointer
```

`cepluginsdk.pas:265`'s `TDisassembleEx=function(address: pptruint; …)` confirms it too: **the C
header is the one that is wrong**.

The correct C declaration and usage:

```cpp
typedef BOOL (__stdcall *CEP_DISASSEMBLEEX_FIXED)(UINT_PTR *address, char *output, int maxsize);

UINT_PTR a = currentAddr;
char buf[256];
((CEP_DISASSEMBLEEX_FIXED)g_Exported.disassembleEx)(&a, buf, sizeof(buf) - 1);
// a has been advanced to the next instruction; buf holds the same format as Disassembler in §8 (a whole line)
```

> **TRAP:** call it by value as the header says and CE will **dereference your address value as a
> pointer** → Access Violation.
> This is the one thing `disassembleEx` does better than `nextOpcode` (text plus the next address in
> one call), but you have to fix the header's declaration yourself.

---

## 5. ReadProcessMemory — Double Pointer

`ReadProcessMemory` in the CE SDK is a **pointer-to-function-pointer** (double pointer):

```c
// note the ** double asterisk
typedef BOOL(__stdcall **CEP_READPROCESSMEMORY)(
    HANDLE hProcess, LPCVOID lpBaseAddress,
    LPVOID lpBuffer, SIZE_T nSize, SIZE_T *lpNumberOfBytesRead);
```

The struct field `CEP_READPROCESSMEMORY ReadProcessMemory` is itself a pointer-to-pointer.

### Correct call form

```cpp
// dereference once to get the function pointer, then call
(*g_Exported.ReadProcessMemory)(
    *g_Exported.OpenedProcessHandle,  // HANDLE (also a dereference of a PHANDLE)
    (LPCVOID)address,
    buffer,
    size,
    &bytesRead);
```

> CE made it a double pointer so plugins can hook/replace ReadProcessMemory.
> `WriteProcessMemory`, `GetThreadContext` and friends follow the same double-pointer pattern (though
> the SDK header types them as PVOID).

---

## 6. Registering the Disassembler Right-Click Menu (Type 6)

### Init Struct

```c
typedef struct _PLUGINTYPE6_INIT {
    char* name;                          // menu item name
    CEP_PLUGINTYPE6 callbackroutine;     // click callback
    CEP_PLUGINTYPE6ONPOPUP callbackroutineOnPopup;  // callback before the popup is shown
    char* shortcut;                      // shortcut string (e.g. "Ctrl+Shift+A")
} PLUGINTYPE6_INIT, DISASSEMBLERCONTEXT_INIT;
```

### Callback Signatures

```c
// click callback:
typedef BOOL (__stdcall *CEP_PLUGINTYPE6)(UINT_PTR *selectedAddress);
// selectedAddress points at the address the user selected; readable and writable

// Popup callback (version 6+):
typedef BOOL (__stdcall *CEP_PLUGINTYPE6ONPOPUP)(
    UINT_PTR selectedAddress,  // passed by value
    char **addressofname,      // menu item name (modifiable)
    BOOL *show);               // set to FALSE to hide this menu item
```

### Registration

```cpp
DISASSEMBLERCONTEXT_INIT init = {};
init.name = const_cast<char*>("Send to AOBMaker");
init.callbackroutine = OnDisassemblerContextMenu;
init.callbackroutineOnPopup = OnDisassemblerContextPopup;
init.shortcut = const_cast<char*>("Ctrl+Shift+A");

int id = g_Exported.RegisterFunction(pluginid, ptDisassemblerContext, &init);
// id = -1 means registration failed
```

> **Note:** the `name` and `shortcut` fields are typed `char*` (non-const).
> String literals in C++ are `const char*`, so you need `const_cast<char*>()` or a static char array.

### CE's internal handling flow (plugin.pas)

```
1. RegisterFunction(pluginid, 6, &init)
2. CE reads init->name, init->callbackroutine, init->callbackroutineOnPopup
3. Creates a TMenuItem and adds it to debuggerpopup (the disassembler right-click menu)
4. Parses init->shortcut and sets the shortcut
5. Returns the functionid (for UnregisterFunction)

When the right-click menu opens:
6. CE calls callbackroutineOnPopup(selectedAddress, &name, &show)
7. If show=TRUE, the menu item is displayed

When the user clicks:
8. CE calls callbackroutine(&selectedAddress)
9. CE writes the (possibly modified) selectedAddress back into
   disassemblerview.SelectedAddress **unconditionally**
```

> **Step 9 is the core of the whole disassembler navigation mechanism** (see the sibling document
> `CE-Disassembler-Navigation.md`):
> ```pascal
> // MemoryBrowserFormUnit.pas:4554-4556
> selectedaddress := disassemblerview.SelectedAddress;
> x.callback(@selectedaddress);                        // the return value is not captured
> disassemblerview.SelectedAddress := selectedaddress; // applied unconditionally
> ```
> CE **completely ignores the callback's BOOL return value**, so there is no way to cancel the
> navigation with `return FALSE`.
> (Contrast Type 1: MemoryBrowserFormUnit.pas:4572 is `if x.callback(...) then`, and only its
> `begin...end` block at 4573-4577 writes the three addresses back; returning FALSE discards
> everything you wrote.)

> **Note 1 (version gate):** the 3-parameter version (with `show`) is only called when your
> `GetVersion` reports **version ≥ 6**. If you report 5 or lower, CE takes the
> `Tpluginfuntion6OnContextVersion5` path and passes only 2 parameters
> (plugin.pas:1751-1752), so `show` is never written → the menu item is always displayed.
>
> **Note 2 (`show` default):** CE sets `show := true` before the call (plugin.pas:1748),
> so "leave it alone" = displayed; only an explicit FALSE hides it.
>
> **Note 3 (`addressofname` lifetime):** what CE passes in is **the buffer of its own local Delphi
> string** (`s:=menuitem.Caption; addressofmenuitemstring:=@s[1]`, plugin.pas:1746-1747), and right
> after the call it does `menuitem.Caption := addressofmenuitemstring` (plugin.pas:1757).
> If you overwrite that pointer, the string it points at must be **static/global**; pointing it at a
> temporary buffer means reading freed memory.
>
> **Note 4:** CE **also completely ignores OnPopup's BOOL return value** (plugin.pas:1752-1754 does
> not capture a return value).

> **Note 5 (the C header and the Pascal use different field names — this is not an error):** the C
> struct above calls it `callbackroutineOnPopup`, which is the official name at `cepluginsdk.h:92`;
> on the Pascal side the same field is called `callbackroutineOnContext` (plugin.pas:955, used at
> 1130). Both refer to the same offset.

---

## 7. ShowMessage

```c
typedef void (__stdcall *CEP_SHOWMESSAGE)(char* message);
```

The parameter type is `char*` (non-const), so C++ needs a cast:

```cpp
g_Exported.ShowMessage(const_cast<char*>("Error message here"));
```

Internally CE calls `showmessage()` on the main thread via `pluginsync`, so it is safe to call from
any thread.

---

## 8. sym_addressToName / Disassembler

### sym_addressToName

```c
typedef BOOL (__stdcall *CEP_ADDRESSTONAME)(
    UINT_PTR address, char *name, int maxnamesize);
```

Return format: it tries, in order, **symbol name** → `"module+offset"` → a plain hex address.
(`ce_sym_addressToName` calls `symhandler.getNameFromAddress(address, true, true, false)`
 = symbols ON / modules ON / **sections OFF**; pluginexports.pas:453)

- With a symbol: `"CreateThing+1A"` (the raw symbol string + `+offset`, symbolhandler.pas:4978-4981)
- Module only: `"game.exe+1501190"`; when the address is exactly the module base it returns just
  `"game.exe"` (no `+0`, symbolhandler.pas:5042-5045)
- Nothing found: `"00007FF6C1501190"` (plain hex, symbolhandler.pas:5059)

> **TRAP:** this function **always returns TRUE** (unless an exception is thrown and swallowed by
> `except`), so you cannot use the return value to tell whether a symbol was resolved — you have to
> inspect the string yourself.
> It does truncate correctly (`min(maxnamesize-1, length(s))` then appends `#0`),
> but `maxnamesize` **must not be 0** (that computes `l = -1`).

### Disassembler

```c
typedef BOOL (__stdcall *CEP_DISASSEMBLER)(
    UINT_PTR address, char* output, int maxsize);
```

Return format: **a complete single line**, not just the mnemonic:
`"<hex address> - <bytes> - <opcode> <operands>"`
for example `"00007FF6C1501190 - 48 8B 05 A9 3E 2C 01 - mov rax,[00007FF6C27C5040]"`
(pluginexports.pas:798 → disassembler.pas:15671-15675)

> **TRAP (buffer size):** `ce_disassembler` only checks `if length(s) > maxsize then` before failing,
> and once past that it uses `StrCopy`, which copies the trailing `#0` as well → when
> `length(s) == maxsize` it actually writes `maxsize + 1` bytes, **overflowing by 1 byte**. Callers
> must pass `sizeof(buffer) - 1` as `maxsize`.
> And because what comes back is a whole line, a buffer that is too small (say only 64 bytes for a
> mnemonic) makes the function return FALSE and `SetLastError(ERROR_NOT_ENOUGH_MEMORY)`; ≥ 256 bytes
> is recommended.
> To get only the mnemonic you have to strip the first two `" - "`-separated fields yourself.

> **Note:** CE's internal plugin disassembler is configured as (pluginexports.pas:2638-2641):
> ```pascal
> plugindisassembler.showsymbols := false;
> plugindisassembler.showmodules := false;
> plugindisassembler.showsections := false;
> ```
> so the operands on that line are never replaced with module names or symbols (e.g.
> `[00007FF6C27C5040]` will not become `[game.exe+2C5040]`) — but **the address and bytes fields are
> still there**; the two things are independent.
> This configuration stays in effect because `isdefault := false` was set at creation time
> (pluginexports.pas:2643); disassembler.pas:1821-1825 only re-syncs these flags from symhandler when
> `isdefault` is true (`showsymbols:=symhandler.showsymbols;` at disassembler.pas:1823).
>
> This `plugindisassembler` is **a single global object shared by** `ce_disassembler` /
> `ce_disassemble` / `ce_nextOpcode`, and it has no lock — two plugin threads calling at once corrupt
> each other's state.
> `previousOpcode` does not use it at all (see §4 TRAP 4).

---

## 9. Compilation Notes

### You must use the correct SDK header

**Never** trim the `ExportedFunctions` struct yourself. It must contain every field (including the
PVOID fields you do not use), otherwise the struct size and offsets are wrong and every function
pointer reads from the wrong place.

> **TRAP:** the struct **layout** is correct (159 fields / 1272 bytes, verified field by field),
> but the **typedef signatures** of two individual fields do not match CE's implementation, and
> copying them verbatim will bite you:
>
> | Field | `cepluginsdk.h` declaration | CE's actual implementation | Consequence |
> |-------|-----------------------------|----------------------------|-------------|
> | `disassembleEx` | `(UINT_PTR address, char*, int)` (h:188) | `(address: pptrUint; …)` (pluginexports.pas:811) | the address is dereferenced as a pointer → **AV** |
> | `createForm` | `(void)` (h:224) | `(visible: boolean)` (pluginexports.pas:1679) | RCX is garbage → the form appears/does not appear at random |
>
> Both are corroborated by the mirror declarations on the Pascal side (`cepluginsdk.pas:265` /
> `:303`) — the C header is what is wrong, not CE.
> Correct declarations:
> ```cpp
> typedef BOOL  (__stdcall *CEP_DISASSEMBLEEX_FIXED)(UINT_PTR *address, char *output, int maxsize);
> typedef PVOID (__stdcall *CEP_CREATEFORM_FIXED)(BOOL visible);
> ```
> (The third case is `GetLuaState`'s `__fastcall`, but that one is harmless — see §11.)

### MSVC UTF-8

If the CE plugin's source code contains non-ASCII characters (e.g. Chinese comments), add:

```cmake
target_compile_options(MyPlugin PRIVATE /utf-8)
```

Otherwise MSVC emits a C4819 warning.

### Static Link CRT

Static-linking the MSVC runtime is recommended so users are not missing a vcruntime DLL:

```cmake
set(CMAKE_MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")
```

### Lua Integration

`cepluginsdk.h` includes `lua.h`, `lualib.h` and `lauxlib.h`.

**If you are not using any Lua functionality**, a minimal stub is enough:

```c
// lua.h
typedef struct lua_State lua_State;
typedef int (*lua_CFunction)(lua_State* L);
```

**If you do need Lua functionality** (e.g. `GetLuaState()` + `luaL_dostring()`), you must:

1. Use the complete Lua **5.3.0** headers shipped with the CE SDK — `lua.h`, `lauxlib.h`, `lualib.h`,
   `luaconf.h`, `lua.hpp`
   (the version is fixed by `lua.h:19-25`'s `LUA_VERSION_MAJOR "5"` / `MINOR "3"` / `RELEASE "0"`;
   both 7.5 and 7.7 are 5.3.0 — CE has not migrated to 5.4)
2. Link against the **import library** `lua53-64.lib`

**Where the files actually are** (measured on the 7.7 install):

| File | Location |
|------|----------|
| `lua53-64.lib` / `lua53-32.lib` (import lib) + all Lua headers | `<CE install dir>\plugins\` |
| `lua53-64.dll` / `lua53-32.dll` (the runtime binary) | `<CE install dir>\` **root**, next to `cheatengine-x86_64.exe` |

```cmake
target_link_libraries(MyPlugin PRIVATE "${CE_DIR}/plugins/lua53-64.lib")
```

> **Note:** the `.lib` is in `plugins\`, the `.dll` is in the **install root** — two different
> directories, do not mix them up.
> What CE loads at runtime is the one in the root (`lua/lua.pas:65 LUA_LIB_NAME = 'lua53-64.dll'`),
> so the plugin DLL resolves its symbols against a module CE has **already loaded**; nothing extra
> needs deploying.
>
> If you take the files from the CE **source tree** instead of an install, the paths are different —
> the directory is called `plugin` (**singular**): `<ce-src>/Cheat Engine/plugin/lua53-64.lib`;
> there is also a copy at `<ce-src>/Cheat Engine/bin/lua_extra/lua53-64.lib`.
> The source tree has **no** `plugins` (plural) directory.

> **Note:** `AutoAssemble()`'s `{$lua}` blocks give you no result when called from a plugin (see §11
> and §12).
> Use `GetLuaState()` + `MainThreadCall` + `luaL_dostring()` to run Lua scripts directly instead.

### x64 Calling Convention

On x64 Windows, `__stdcall`, `__cdecl` and `__fastcall` are all equivalent to the x64 calling
convention. Declaring `__stdcall` is only for readability and x86 compatibility; it does not affect
x64 behaviour.

---

## 10. TVariableType — the SDK header has no such enum

### There is **no** `MemRecValueType` enum in `cepluginsdk.h`

First, to clear one thing up: `grep -n "MemRecValueType" cepluginsdk.h` returns **0 hits**, and
`grep -n "vtByte"` returns 0 as well. The entire header contains only two `enum`s (`PluginType` and
`AutoAssemblerPhase`, both at h:18-19) — **no value-type enum of any kind**.

The "wrong enum in the SDK header" that used to get passed around is really just **a single `//`
comment** on a `_PLUGINTYPE0_RECORD` member:

```c
// cepluginsdk.h:35 — the CE 7.5 version; this mapping is out of date:
char valuetype; //0=byte, 1=word, 2=dword, 3=float, 4=double, 5=bit, 6=int64, 7=string

// cepluginsdk.h:35 — CE 7.7 has fixed it; consistent with CE's internal TVariableType:
char valuetype; //0=byte, 1=word, 2=dword, 3=int64, 4=float, 5=double, 6=string, 7=widestring, 8=bytearray, 9=binary
```

**That one comment line is the only difference between the 7.5 and 7.7 `cepluginsdk.h`**
(`cepluginsdk.pas` is byte-identical between the two versions).
So "the SDK header's value types are wrong" should be read as **the 7.5-era comment was wrong and 7.7
has corrected it** — not as a live defect. But **neither version has a C enum you can just
`#include`**: you have to write the table below out yourself inside your plugin.

What CE actually fills into `valuetype` is the ordinal of the internal `TVariableType`:
MainUnit.pas:9743 `selectedrecord^.valuetype := integer(addresslist.selectedRecord.VarType);`
`ce_memrec_getType` is the same: pluginexports.pas:1041 `result := integer(m.VarType);`

### CE's actual internal Delphi ordering (correct)

Source: `commontypedefs.pas:15` (a single-line enum, measured on 7.5-195; 7.7 is the same)

```
TVariableType = (
    vtByte            = 0,
    vtWord            = 1,
    vtDword           = 2,
    vtQword           = 3,    // 8 bytes (int64/uint64)
    vtSingle          = 4,    // Float (32-bit)
    vtDouble          = 5,    // Double (64-bit)
    vtString          = 6,
    vtUnicodeString   = 7,
    vtByteArray       = 8,
    vtBinary          = 9,
    vtAll             = 10,
    vtAutoAssembler   = 11,   // ★ AA script
    vtPointer         = 12,
    vtCustom          = 13,
    vtGrouped         = 14,
    vtByteArrays      = 15,   // MultiByteArray (special type)
    vtCodePageString  = 16
);
```

> **TRAP:** index 3 in the old comment is Float, but CE's internal index 3 is **Qword**;
> index 5 is **vtDouble**, not bit. When using the return value of `memrec_getType()` you must
> interpret it with CE's internal ordering. CE Lua's `mr.Type` property also uses CE's internal
> ordering.
>
> **Example impact:** in Lua, testing for an AA script requires `mr.Type == 11`, not whatever the old
> comment implied.
>
> These 17 members are **DERIVED from the source** — do not hand-edit them:
> `grep -n "type TVariableType" commontypedefs.pas` prints the whole enum on one line.

---

## 11. GetLuaState — Usage Notes

```cpp
// Get CE's Lua state (Version 5+)
lua_State* L = g_Exported.GetLuaState();
```

### Calling convention

The SDK header declares `GetLuaState` as the only `__fastcall`
(`typedef lua_State *(__fastcall *CEP_GETLUASTATE)();`, cepluginsdk.h:265 — the sole occurrence in
the whole file), but **CE actually implements it as `stdcall`**:
`function plugin_getluastate: Plua_State; stdcall;` (pluginexports.pas:2632),
assigned to `exportedfunctions.GetLuaState` at plugin.pas:2044.

Because the function **takes no parameters**, `__fastcall` and `__stdcall` have identical ABIs on
x86 (no parameters to place in registers, no stack to clean), and on x64 they are equivalent
outright — so this inconsistency is **harmless in practice**; writing it as the header does or
changing it to `__stdcall` both work.
(What is actually wrong is the header, not x86; "x86 builds must be careful here" was needless worry.)

### Thread safety ⚠

`GetLuaState()` does **not** return the main thread's lua_State, and **nothing will synchronise your
Lua calls onto the main thread for you**.

CE's `GetLuaState` (LuaHandler.pas:188-208) creates a thread-local `lua_State` for **every calling
thread** with `lua_newthread(_luavm)` (stored in `threadvar Thread_LuaVM`, LuaHandler.pas:46-48) and
returns it. Those states share **one and the same Lua global state** (globals / GC / registry).

`luaL_dostring()` is **your plugin calling the Lua library directly on its own thread**, and CE has
no idea it is happening — it does not go through `pluginsync`, and there is no synchronisation at
all.
`pluginsync` is used by only **some** of the `ce_*` exported functions (`ce_showmessage`,
`ce_createTableEntry`, `memrec_*`, `debug_*`, `ce_createForm`, `ce_createTimer` …;
pluginexports.pas:880-890).
`plugin_getluastate` itself, `ce_disassembler`, `ce_previousOpcode`, `ce_nextOpcode`,
`ce_sym_addressToName`, `ce_registerfunction` and `ce_assembler` **do not** go through pluginsync.
What `GetLuaState` returns is a **raw pointer**, and everything you do with it afterwards is outside
CE's control.

> **TRAP:** calling `luaL_dostring()` from a plugin background thread is a data race —
> Lua 5.3's global state is not thread-safe, and CE has no global Lua lock
> (the only `LuaCS.Enter` on a Lua execution path in LuaHandler.pas, at 1417-1443, is **commented-out**
> code).
>
> Note that the step where `GetLuaState()` creates the thread state on first call **is** protected by
> `_luacs` (LuaHandler.pas:192-202), but that critical section protects **only** the creation of the
> state, not any Lua **execution**: while another thread is running `lua_pcall`, your
> `lua_newthread(_luavm)` is still touching the same global state. `_LuaCS` appears in the whole file
> only at the decl (44), in GetLuaState (192-202), and in initialization/finalization (17353,
> 17382-17385).

**The right approach:** use ExportedFunctions' `MainThreadCall` (CE points it straight at
`pluginsync`, plugin.pas:2045) to run your Lua on the main thread:

```cpp
// TPluginFunc has no stdcall (cepluginsdk.pas:343); makes no difference on x64
static void* runLua(void* p) {
    lua_State* L = g_Exported.GetLuaState();
    if (!L) return nullptr;
    int err = luaL_dostring(L, static_cast<const char*>(p));
    if (err != LUA_OK)
        lua_pop(L, 1);          // on failure Lua pushes the error message onto the stack — it must be popped
    else
        lua_settop(L, 0);
    return nullptr;
}

typedef void* (__stdcall *MAINTHREADCALL)(void* (*)(void*), void*);
((MAINTHREADCALL)g_Exported.MainThreadCall)(runLua, script);
```

### Recommended usage (you must already be on the main thread)

```cpp
lua_State* L = g_Exported.GetLuaState();
if (!L) return; // CE has not initialised Lua yet

int top = lua_gettop(L);        // remember the original stack height
int err = luaL_dostring(L, "return getAddressList().Count");
if (err == LUA_OK) {
    int count = (int)lua_tointeger(L, -1);
    lua_pop(L, 1);
} else {
    const char* msg = lua_tostring(L, -1);
    // handle the error...
    lua_pop(L, 1);              // ← the failure path must pop too!
}
lua_settop(L, top);             // belt and braces: restore regardless
```

> **TRAP (a leak that accumulates):** when `luaL_dostring` fails it pushes the error object onto the
> stack. Code that pops only inside `if (err == LUA_OK)` permanently raises that state's stack by one
> per failure — and `GetLuaState()` hands you a **long-lived per-thread `lua_State`**
> (LuaHandler.pas:188-208 caches it in `threadvar Thread_LuaVM` and reuses it for the whole lifetime
> of the thread), so the leak does not go away when the call returns. The error path must always
> `lua_pop(L, 1)`, or wrap the whole thing with `lua_settop(L, top)` as above.

> **Not recommended:** using `AutoAssemble()`'s `{$lua}` blocks. A `{$LUA}` block's return value is
> only ever spliced back into the script **as assembly text** (autoassembler.pas:1449-1456) — there
> is **no channel back to the caller at all**; and `ce_AutoAssemble`'s return value does not indicate
> whether the script succeeded either (see §12).

---

## 12. Common-Error Quick Reference

| Symptom | Cause | Fix |
|---------|-------|-----|
| Access Violation at 0x10 | `GetVersion`'s second parameter dereferenced as a pointer | Use the `PPluginVersion pv, int size` signature |
| Crash calling any ExportedFunctions member | Struct layout does not match CE's | Use the complete official SDK header |
| Menu item appears in the wrong place | Wrong `ptDisassemblerContext` value | Confirm you are using `6`, not `5` |
| Collected instruction addresses are completely wrong | `previousOpcode`/`nextOpcode` return values treated as deltas | The return value is a full address; use `prev = prevAddr` |
| x64 address truncated (high bits lost) | The SDK declares the return type as `DWORD` | Change it to `UINT_PTR` |
| ReadProcessMemory crashes | Not double-dereferenced | Use `(*ef->ReadProcessMemory)(...)` |
| Compile error on string parameters | The SDK uses `char*` but C++ literals are `const char*` | `const_cast<char*>()` or a static array |
| CE crashes after the plugin loads | `pluginname` points at a temporary string on the stack | Use a static/global string |
| `memrec_getType()`'s return value is interpreted as the wrong type | Read against the stale `valuetype` comment in the 7.5 header (the header never had an enum) | Use CE's internal `TVariableType` ordering: vtQword=3, vtSingle=4, vtDouble=5 |
| Lua `mr.Type == 6` fails to detect an AA script | In CE Lua, vtAutoAssembler=11, not 6 | Use `mr.Type == 11` |
| `AutoAssemble("{$lua}...")`'s return value is meaningless | `ce_AutoAssemble` returns a Delphi `BOOL` (LongBool), and `autoassemble()`'s own success/failure is **discarded** — only a thrown exception yields FALSE (pluginexports.pas:738-751). A `{$LUA}` block's return value is only ever spliced back into the script as assembly text (autoassembler.pas:1449-1456), with **no channel back to the caller at all** | Use `GetLuaState()` + `MainThreadCall` + `luaL_dostring()` instead |
| `Disassembler()` output has an extra address and bytes | It returns **the whole line** `"addr - bytes - opcode operands"`, not a mnemonic | Strip the first two `" - "` fields yourself; pass `sizeof(buf)-1` as `maxsize`, with buf ≥ 256 |
| Access Violation after calling `disassembleEx()` | The SDK header declares `UINT_PTR address` (by value); CE implements `pptrUint` (a pointer) | Redeclare it as `UINT_PTR *address` and pass `&addr` |
| `createForm()` shows/does not show the form at random | The SDK header declares `(void)`; CE implements `(visible: boolean)` | Redeclare it as `PVOID (__stdcall*)(BOOL visible)` |
| Random crashes calling `luaL_dostring()` from a background thread | `GetLuaState()` returns **that thread's own** `lua_State`, and they share one global state; CE has no Lua lock | Use `MainThreadCall` (= `pluginsync`) to run the whole Lua block on the main thread |
| `previousOpcode()` walks back into the middle of an instruction | It **has no failure return value** — when every attempt fails it returns `address-1`, never 0 and always < address | Validate it yourself: `nextOpcode(prevAddr)` must land exactly on the original address |
| `writeInteger(addr, val)` does not write to plugin memory | `writeInteger` writes to the target process's memory | Use `writeIntegerLocal`, or switch to the `GetLuaState` approach |
| `executeCodeEx(1, nil, fn, ...)` hangs CE permanently, with no way to recover | A `nil` timeout is **not a default — it is INFINITE**, and the wait pumps no messages at all | Always pass a finite millisecond value (see §13) |
| `executeCodeEx` returns `nil` and you cannot tell why | CE's **second return value** is the reason string; checking only `~= nil` throws it away | `local r, why = executeCodeEx(...)` (see §13 for the five reasons) |
| Target-process memory keeps growing after repeated timeouts | `WAIT_TIMEOUT` also sets `dontfree`, so the stub / result / string allocations are never reclaimed | Deliberate by design; shorten the timeout, and use `freeExecuteCodeExStub` if you must reclaim (see §13) |

> **Addendum (about that `-1` from `AutoAssemble`):** the `-1` observed in early testing **does not
> mean failure**.
> `BOOL` in Delphi/FPC is `LongBool`, and the bit pattern for TRUE is compiler-determined (it may be
> 1, or all bits set = -1) — CE's source cannot settle it, so that `-1` is very likely "success".
> Either way, it must not be used to judge whether an AA script ran successfully.

---

## 13. `executeCodeEx` / `executeMethod` — the synchronous wait, timeout semantics, and leaks

> **Verification coordinate for this section:** everything below comes from CE source tag
> **`7.5-195`** (`git describe` = `7.5-195-g4178e037`). **The 7.7 binary was NOT probed.**
> Per this document's standing position — the public source lags the shipping release — the one
> item marked *suspected* below must not be used as a conclusion.
>
> This section is load-bearing for what this repo emits: every `callDLL` helper in
> `scripts/ue5_dissect.lua` and `Services/CeLuaHygiene.cs` goes through this path. The
> suspected handle leak is tracked in [docs/CE-Bugs-Minesweeper.md](CE-Bugs-Minesweeper.md).

### 13.1 The call chain

| Function | Location | Notes |
|----------|----------|-------|
| `executeCodeEx(callmethod, timeout, address, ...)` | LuaHandler.pas:11922 | **Just a shim.** Fewer than 3 parameters returns `nil, 'Not enough parameters. Minimum: callmethod, timeout, address'` (:11931-11935); otherwise it inserts `instance=nil` and tail-calls `executeMethod` |
| `executeMethod(callmethod, timeout, address, instance, ...)` | LuaHandler.pas:11417 | The real implementation — the wait lives here |
| `executeCode(address, parameter)` | LuaHandler.pas:11943 | **A separate implementation** carrying its own duplicate wait (:12056) — fixing one does not fix the other |
| `freeExecuteCodeExStub` | LuaHandler.pas:11412 | The companion reclaim API (see 13.4). Its preconditions are **not verified** here |

### 13.2 The wait is synchronous and pumps nothing ⚠

```pascal
thread := CreateRemoteThread(processhandle, nil, 0, pointer(stubaddress), nil, 0, y);  // :11847
...
wr := WaitForSingleObject(thread, timeout);                                            // :11861
```

**It blocks synchronously on the calling thread, and nothing anywhere on that path pumps messages.**

When called from an AA `{$lua}` block, the calling thread **is CE's GUI thread**, so:

- **The timeout value is the ceiling on GUI freeze time.** Pass 5000 and you have authorised a 5 s freeze.
- **A Lua-side `processMessagesPaintOnly()` cannot help.** It only gets to run once
  `executeCodeEx` returns; during the freeze it never executes. This is **structurally different**
  from a `sleep()` loop, which *can* pump itself.

### 13.3 Timeout semantics — `nil` is INFINITE, not "a default"

The source's header comment (:11425-11428) and its implementation (:11504-11507) agree:

| Value passed | Meaning |
|--------------|---------|
| `0` | Do not wait (no return value), **and deliberately do not reclaim memory** (see 13.4) |
| `nil` or `-1` | **INFINITE** — `if lua_isnil(L,2) then timeout:=INFINITE` |
| anything else | Milliseconds |

> **Trap:** `nil` reads like "unspecified, use the default"; it actually means **wait forever**.
> If the target process is suspended, the stub faults, or the remote thread never starts, CE's GUI
> is frozen permanently with no way to recover from the UI — the process has to be killed.
> **No script that reaches a user should ever pass `nil`.**

### 13.4 Reclaim: `dontfree` — a timeout does not reclaim either

The `finally` block (:11907) uses `if (dontfree=false)` to decide whether to `VirtualFreeEx` the stub
address, the result address, and every string allocation (**all of them in the target process**).
Two places set `dontfree`:

| Location | Condition | Consequence |
|----------|-----------|-------------|
| :11851 | `dontfree := timeout=0` | This is the **source-level mechanism** behind celua.txt's "timeout 0 leaks" claim |
| :11880 | the `WAIT_TIMEOUT` branch sets it unconditionally | **A timeout does not reclaim either** |

The second one is the easy miss: **every timeout leaves a permanent allocation in the target
process**, and repeated timeouts accumulate.

> This is **deliberate design, not a CE bug**: a timeout means the remote thread may still be
> running, and freeing the stub out from under it would crash the target outright.
> To reclaim, go through `freeExecuteCodeExStub` (:11412) — but you must establish for yourself
> that the thread has finished.

### 13.5 CE returns `nil, reason` on failure — do not throw the reason away

Every failure path in `executeMethod` is `lua_pushnil` + `lua_pushstring(<reason>)` + `exit(2)`, so
the Lua side receives **two** return values. The reasons point at completely different problems:

| Reason string | Location | Means |
|---------------|----------|-------|
| `'Not enough parameters. Minimum: callmethod, timeout, address'` | :11934 | Wrong argument count (caught by `executeCodeEx` itself) |
| `'Not enough parameters. Minimum: callmethod, timeout, address, instance'` | :11487 | Same, but `executeMethod` was called directly |
| `'Failure launching thread'` | :11898 | `CreateRemoteThread` failed |
| `'Execution timeout'` | :11882 | The stub ran but did not finish in time |
| `'Wait failure'` | :11888 | `WaitForSingleObject` itself errored |
| `'Failure reading the result address'` | :11873 | Execution completed, but the result could not be read back |

> **Trap:** checking only `result ~= nil` discards a diagnosis CE already computed, and substitutes
> a guessed message. These six reasons send you to six different places. The correct shape is
> `local r, why = executeCodeEx(...)`, surfacing `why` on failure.

### 13.6 Suspected: thread handle leak (**UNCONFIRMED**)

`closehandle(thread)` sits at :11893, but all four branches ahead of it (`WAIT_OBJECT_0` success /
result-read failure / `WAIT_TIMEOUT` / `Wait failure`) **`exit()` first**, and the `finally`
(:11901-11917) does not close it either. Read literally, **every call leaks one thread handle inside
cheatengine.exe**.

> ⚠ **Marked suspected — do not treat it as a conclusion.** This document already has the
> precedent: a defect present in the source had already been fixed in the 7.7 binary. This item
> **read only the 7.5-195 source and did not verify 7.7's behaviour.**
>
> Verifying takes a minute — attach to a target, then sample the handle count before and after N calls:
>
> ```powershell
> (Get-Process cheatengine).HandleCount
> ```
>
> A delta of ≈ N confirms the leak; ≈ 0 means 7.7 already fixed it, and this section should be
> rewritten as "present in 7.5, fixed in 7.7".
