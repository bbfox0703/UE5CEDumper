# CE Disassembler Navigation from Plugin Code

> **Why this doc is in this repo.** UE5CEDumper does not build a CE plugin — but it emits CE Lua,
> and the second half of this document is the verified way to drive CE's Memory Viewer from Lua
> (`getMemoryViewForm().DisassemblerView.SelectedAddress`, backed by the real published Pascal
> property, not just by `celua.txt`). That is directly reusable by the `{$lua}` blocks this repo
> generates, and by the AOBMaker pipe integration
> ([aobmaker-integration.md](aobmaker-integration.md)). The plugin-callback half is kept for the
> same reason as [ce-plugin-api-reference.md](ce-plugin-api-reference.md). **This is a mirror, not
> the master.**
>
> **Master copy:** `<private-ce-repo>/docs/CE-Disassembler-Navigation.md` — edit there
> first, then mirror here.
>
> ⚠ Every `file:line` below is DERIVED from the EXTERNAL `D:\Github\cheat-engine` tree at tag
> `7.5-195`. This repo's `tools/check_derived_counts.py` gate does **not** cover them — it only
> derives from the UE5CEDumper tree. Re-derive by grepping the identifier, never by trusting the
> line number.

> **Version coordinates (the source of every claim below)**
>
> | What | Value |
> |------|-------|
> | CE source tree read | `D:\Github\cheat-engine`, tag **`7.5-195`**, HEAD `4178e037` (level with `upstream/master`) |
> | CE binaries checked against | `C:\Program Files\Cheat Engine\`, **7.7.0.10568** (ProductVersion 7.7) |
> | Plugin SDK version | `CESDK_VERSION 6` (`cepluginsdk.h:16`) — unchanged in 7.7 |
>
> So: **the source read was 7.5; the shipping binary is 7.7.** The plugin SDK C ABI is identical
> between them (`cepluginsdk.pas` is byte-identical; `cepluginsdk.h` differs on one comment line
> that has nothing to do with this document), so a plugin built against it works on both.

## Problem

CE Plugin SDK provides no direct `SetDisassemblerAddress()` function.
Navigating the disassembler view to a specific address requires understanding
when pointer writes work and when they don't.

## Two Approaches

### Approach 1: Direct Pointer Write (Type 6 Callback Only)

**Works inside** a Type 6 (Disassembler Context Menu) callback.

The Type 6 callback receives `UINT_PTR* selectedAddress`. CE writes the value back to the
disassembler view **unconditionally** — the callback's `BOOL` return value is *ignored*
(MemoryBrowserFormUnit.pas:4546-4558):

```pascal
selectedaddress := disassemblerview.SelectedAddress;
x.callback(@selectedaddress);                          // return value not captured
disassemblerview.SelectedAddress := selectedaddress;   // always applied
```

```cpp
// Type 6 callback — this works; the return value does not matter
BOOL __stdcall onContextMenu(UINT_PTR* selectedAddress) {
    *selectedAddress = 0x7FF600001234;   // CE navigates here after the callback returns
    return TRUE;                          // FALSE would navigate too
}
```

> **Corollary**: you cannot "cancel" a Type 6 navigation by returning FALSE. Leaving
> `*selectedAddress` alone gets you *close* — CE writes back the value it read, and
> `setSelectedAddress` skips the back-list push when the address is unchanged
> (`if (fSelectedAddress<>0) and (fselectedAddress<>address) and (not goingback) then backlist.Push(...)`,
> disassemblerviewunit.pas:346-347). It is **not** a total no-op, though: the setter still assigns
> `fSelectedAddress2 := address` (`:350`), collapsing any two-ended selection, and it re-seeds
> `fTopAddress` whenever the written address is not one of the currently rendered lines
> (`:366-378`) — and the in-range test at `:354` bounds the view at
> `disassemblerlines[fTotalvisibledisassemblerlines-2]`, i.e. it **excludes the bottom-most rendered
> line**. So a right-click on that last visible line scrolls the view even when you write the value
> straight back. Beyond that, the residual effect is the trailing `update` (`:397`).

**Does NOT work** from a form button / timer / any deferred context. The reason is not that
CE "only reads it back during the original callback return path" — it is that the pointer
aims at a **stack local of CE's own click handler**
(`var selectedaddress: ptrUint;` MemoryBrowserFormUnit.pas:4549, and the three locals at
4562-4564 for Type 1). That stack frame is gone the moment the handler returns, so a later
write corrupts unrelated stack memory.

### Approach 2: Lua Script (Works from Any Main-Thread Context)

CE's Lua engine exposes `getMemoryViewForm().DisassemblerView.SelectedAddress`.
Setting this property immediately navigates the disassembler view.

**Works from any context that runs on CE's main thread** — form button `OnClick`, timer
`OnTimer`, and every Type 0/1/5/6 menu callback qualify. CE creates these as ordinary LCL
objects inside CE's own process (`ce_createForm` → `TCEForm.CreateNew`, `ce_createTimer` →
`TCETimer.Create` where `TCETimer=class(Ttimer)`, ceguicomponents.pas:50), and LCL dispatches
their events from the thread pumping the message loop — CE's main thread. (The creation calls
themselves are also marshalled via `pluginsync`, pluginexports.pas:1681 / 1914, which is why
you may create them from a worker thread safely.)

> **Not safe from a raw plugin worker thread.** Two independent reasons:
> 1. `GetLuaState()` returns a **per-thread** `lua_State` created with `lua_newthread(_luavm)`
>    (LuaHandler.pas:188-208, `threadvar Thread_LuaVM` at :46-48), sharing one Lua global state.
>    CE holds no lock around Lua execution — the only `LuaCS.Enter` execution path in
>    LuaHandler.pas (line 1422) is inside a commented-out block (1417-1443). And
>    `plugin_getluastate` hands you a **raw pointer** with no `pluginsync` wrapper
>    (pluginexports.pas:2632-2635), so nothing downstream is marshalled for you.
> 2. `DisassemblerView.SelectedAddress`'s setter runs LCL painting
>    (`BeginUpdate` / `update` / `EndUpdate`, disassemblerviewunit.pas:382-393, plus the trailing
>    `update` at :397).
>
> From a worker thread, wrap the whole thing in `ExportedFunctions.MainThreadCall`
> (CE points it straight at `pluginsync`, plugin.pas:2045).

```cpp
bool navigateDisassembler(UINT_PTR address) {
    if (!g_Exported.GetLuaState)
        return false;

    lua_State* L = g_Exported.GetLuaState();
    if (!L)
        return false;

    char lua[128];
    snprintf(lua, sizeof(lua),
        "getMemoryViewForm().DisassemblerView.SelectedAddress = 0x%llX",
        static_cast<unsigned long long>(address));

    int err = luaL_dostring(L, lua);
    if (err != 0) {
        const char* errMsg = lua_tostring(L, -1);
        // handle error...
        lua_pop(L, 1);
        return false;
    }
    return true;
}
```

**Requirements:**
- Link against the **import library** `lua53-64.lib`, which ships in
  `<CE install>\plugins\` (verified on 7.7). From the CE **source tree** the same file is at
  `<ce-src>\Cheat Engine\plugin\lua53-64.lib` — note the directory is `plugin`, **singular**,
  and the source tree has no `plugins\` at all. A third copy lives in
  `<ce-src>\Cheat Engine\bin\lua_extra\`.
- The **DLL is not in `plugins\`** — `lua53-64.dll` sits at the CE **install root**, next to
  `cheatengine-x86_64.exe` (`lua/lua.pas:65  LUA_LIB_NAME = 'lua53-64.dll'`). Your plugin
  resolves against the module CE has already loaded, so there is nothing extra to deploy.
- Include `<lua.h>` / `<lauxlib.h>` — Lua **5.3.0** (`plugins\lua.h:19-25`; still 5.3 in 7.7,
  no 5.4 migration) — or CE-Handwire's `lua_stubs.h`.

## When to Use Which

| Context | Pointer Write | Lua |
|---------|:---:|:---:|
| Type 6 callback (disasm context menu) | OK — return value irrelevant | OK |
| Type 1 callback (memory view menu) | OK — **but only if the callback returns TRUE** | OK |
| Form button click | NO | OK |
| Timer callback | NO | OK |
| Any deferred/async context | NO | OK |
| Plugin worker thread | NO | **NO** — marshal via `MainThreadCall` first |

**Type 1 differs from Type 6 in two ways** (MemoryBrowserFormUnit.pas:4560-4579):

- It receives **three** pointers, and the first is `TopAddress`, not `SelectedAddress`:
  `callback(&disassemblerTopAddress, &selectedDisassemblerAddress, &hexViewAddress)`.
  Writing only `*selected_disassembler_address` is enough. CE re-applies all three in a fixed
  order — `TopAddress`, then `SelectedAddress`, then the hex view
  (MemoryBrowserFormUnit.pas:4574-4576). That order matters, because
  `setTopAddress` also overwrites `fSelectedAddress` (disassemblerviewunit.pas:324-330);
  since the selection is assigned *after* it, your value wins.
- CE guards the write-back: `if x.callback(...) then begin ... end;`.
  **Returning FALSE from a Type 1 callback silently discards every address you wrote.**

## CE-Handwire Usage

- **RegisterGoto**: Uses pointer write — works because `navigateTo()` is called
  directly from Type 6 callback (`*m_disasmAddr = address`).
- **MemoryBookmarks**: Uses Lua — "Go To" button is a form callback, not a
  Type 1/6 callback, so pointer write doesn't work.

## Related CE Lua Properties

```lua
-- Disassembler navigation
getMemoryViewForm().DisassemblerView.SelectedAddress = 0x12345678

-- Secondary selection (the other end of a multi-line selection)
getMemoryViewForm().DisassemblerView.SelectedAddress2 = 0x12345690

-- Hex view navigation
getMemoryViewForm().HexadecimalView.Address = 0x12345678

-- Read current disassembler address
local addr = getMemoryViewForm().DisassemblerView.SelectedAddress
```

**These are real published properties, not conventions.** The Pascal declarations are what makes
them reachable from Lua at all:

| Lua expression | Pascal declaration |
|---|---|
| `DisassemblerView.SelectedAddress` | `property SelectedAddress: ptrUint read fSelectedAddress write setSelectedAddress;` — disassemblerviewunit.pas:209 |
| `DisassemblerView.SelectedAddress2` | `property SelectedAddress2: ptrUint read fSelectedAddress2 write setSelectedAddress2;` — disassemblerviewunit.pas:210 |
| `HexadecimalView.Address` | `property Address: ptrUint read fAddress write setAddress;` — hexviewunit.pas:265 |

Two things follow from reading the setter rather than assuming a plain field write.
`setSelectedAddress` (disassemblerviewunit.pas:339) **pushes a history entry** when the address
actually changes (:346-347, skipped while `goingback`), and **re-centres the view** when the
target is outside the visible lines (:382-393). So this is navigation, not just state.

CE's own code uses exactly the pattern recommended above:

```pascal
// AdvancedOptionsUnit.pas:1304
memorybrowser.disassemblerview.SelectedAddress := symhandler.getAddressFromName(sn);
// accessedmemory.pas:220 does the same
```

`celua.txt` shipped with 7.7 documents the property at line 2715, and `DisassemblerView` /
`HexadecimalView` on the memoryview object at :2689-2690. Treat `celua.txt` as the user-facing
doc only — the Pascal property above is the verification.
