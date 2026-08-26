# The dxgi proxy vs. the AppCompat shim engine — root cause + fix plan

**Status: root cause CONFIRMED at instruction level, 2026-08-26. Fix NOT yet implemented.**
Reported build 1.0.0.3315 (crash dumps) / 1.0.0.3362 (user's re-test) — the mechanism is
byte-for-byte identical in both, see §3.7.

> **The one-line version.** Windows' AppCompat shim engine calls
> `dxgi.dll!SetAppCompatStringPointer` **during loader initialisation, before our DLL's entry
> point has run**. Our export for that name is a lazy asm thunk whose resolver *logs*, and
> logging allocates from a CRT heap that does not exist yet. `RtlAllocateHeap(NULL, 0, 0x20)`
> → access violation → the process dies before the EXE entry point. The game never starts.

---

## 1. The question

> ReShade's `dxgi.dll` runs fine in OCTOPATH TRAVELER. Ours makes the game refuse to start.
> Our own `winmm.dll` proxy — *the same 2.9 MB binary* — works in the same game. Why?

Game: `D:\SteamLibrary\steamapps\common\OCTOPATH TRAVELER\Octopath_Traveler\Binaries\Win64\`
(UE 4.18, `Octopath_Traveler-Win64-Shipping.exe`, 45 MB, SQUARE ENIX 2020).

## 2. The answer

Octopath has a **Windows Application Compatibility layer applied**:

```
HKCU\Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers
  D:\SteamLibrary\...\Octopath_Traveler-Win64-Shipping.exe    REG_SZ    HIGHDPIAWARE
```

so `apphelp.dll` and `AcGenral.dll` (the shim engine) are loaded at module **[4]** and **[5]** —
ahead of `msvcrt`, and 32 modules ahead of our dxgi.

`AcGenral.dll` carries a shim named **`DXGICompat`** (`NS_DXGICompat::InitializeHooksMulti`). Its
gate, disassembled from `C:\Windows\System32\AcGenral.dll` (two identical copies, at `0x5d4f` and
`0x357b1`):

```asm
lea  rcx, [UTF-16 "dxgi.dll"]            ; 0x56360
call [GetModuleHandleW]
test rax, rax
je   0x5d9b                              ; not loaded -> skip
lea  rdx, [ASCII "SetAppCompatStringPointer"]   ; 0x5c338
call [GetProcAddress]
test rax, rax
je   0x5d9b                              ; not exported -> skip
call rax                                 ; <-- enters our proxy
```

The loader notifies the shim engine through `apphelp!SE_DllLoaded` (export RVA `0x01F790`) when a
module is **mapped** — which is *before* its initialisation routine runs. So our export is entered
with `_DllMainCRTStartup` not yet executed, i.e. **`__acrt_heap == NULL`**.

Our export for that name is the lazy thunk `f7` → `ResolveAll` → `DxgiProxy_EnsureResolved()`
([dll/src/Lugner_Dxgi.cpp:100](../dll/src/Lugner_Dxgi.cpp)), which calls `LOG_INFO` /`LOG_ERROR`
([:120](../dll/src/Lugner_Dxgi.cpp), [:122](../dll/src/Lugner_Dxgi.cpp)) → `Sein` →
`std::string` → `malloc` → `HeapAlloc(NULL, …)` → **AV**.

## 3. Evidence

Every item below was reproduced on this machine. Where a path is machine-local and volatile it is
flagged — the *derivation* is what matters, not the artifact surviving.

### 3.1 The fault, at the instruction

`ntdll+0x3DCB4` = `RtlAllocateHeap+0x54`, `0xC0000005`, read at `0x10`:

```asm
3dc8a  mov  rdi, rcx                 ; rdi = HeapHandle
3dc95  test rcx, rcx
3dc98  jne  0x3dcb4
3dcac  lea  ecx, [rdi + 0x13]        ; context Rcx = 0x13, with rdi = 0 -> matches
3dcaf  call 0x56d80                  ; null-handle report path, returns
3dcb4  cmp  dword ptr [rdi + 0x10], 0xddeeddee   ; <-- FAULT, rdi = 0
```

Registers: `Rdi = 0` (HeapHandle), `Rbx = 0` (flags), `R13 = 0x20` (size).
→ **`HeapAlloc(NULL, 0, 32)`**.

`grep -rn "HeapAlloc\|HeapCreate\|GetProcessHeap" dll/src/` returns **nothing**, and the proxy
imports no CRT DLL (`/MT`, `dll/CMakeLists.txt:107`), so this is our own statically-linked UCRT's
allocator with `__acrt_heap` still zero — i.e. **our CRT was never initialised**.

### 3.2 The caller is us

`RtlAllocateHeap`'s frame is 7 pushes + `sub rsp,0x140` = `0x178`. The value at `rsp+0x178` is
`dxgi.dll+0x1B43E4` — inside our own image.

### 3.3 The exact export, recovered from the exact binary

The crashing binary still exists: `out/proxy-backups/Avowed.dxgi.dll.20260823-212124.bak`
(2,891,264 bytes, `SizeOfImage 0x2CA000`) — an exact match for the module in the dumps. Resolving
RVAs against **that** binary rather than a current build:

```asm
187a2e  mov  rax, [rip+0x12da53]   ; mProcs[7]  -> SetAppCompatStringPointer thunk (@8)
187a35  test rax, rax
187a38  jne  0x187a46
187a3a  call 0x187930              ; ResolveAll
187a3f  mov  rax, [rip+0x12da42]   ; the instruction AFTER the call
187a46  jmp  rax
```

**Both `0x187A2E` and `0x187A3F` are on the faulting stack of both dumps**, with `AcGenral`
return addresses directly above them.

⚠ Do **not** resolve these RVAs against `dist/proxy/dxgi.dll` — the layout shifted between builds
(`SizeOfImage 0x2CA000` vs `0x2CB000`) and `0x1B43E4` lands mid-instruction there. That mistake
was made and caught during this investigation.

### 3.4 The 32 bytes are the timestamp string

`Sein::GetTimestamp` ([dll/src/Sein.cpp:142](../dll/src/Sein.cpp)) ends in
`return std::string(buf)`. A 23-character timestamp gives MSVC capacity `23 | 15 = 31` → a
**32-byte** block. That is the allocation that faults.

### 3.5 The deep stack

```
ntdll!RtlAllocateHeap+0x54                       <-- fault
dxgi.dll+0x1B43E4 … +0xB010 (GetFileAttributesExW = std::filesystem) … +0x187A3F  <-- our thunk
AcGenral.dll+0x55D9B / +0x5D78 / +0x5C00 / +0x1983
apphelp.dll+0x1F81F , apphelp.dll+0x1F790        <-- SE_DllLoaded (export RVA 0x1F790)
ntdll!Ldrp… (LdrpInitializeProcess region)
```

**No game code anywhere on the stack** — only EXE *data* pointers. The process died inside loader
initialisation.

### 3.6 Corroboration that DllMain never ran

- Only **4 threads** in the dump — `Mimic::StartThread` and the two `CreateThread` calls in
  [dll/src/Heiter.cpp:435-465](../dll/src/Heiter.cpp) never happened.
- `C:\Windows\System32\dxgi.dll` is **never mapped** — the resolver's `LoadLibraryW` never
  completed.
- **No log file exists for 2026-08-23 at all.** The Octopath log folder jumps straight from
  `init-20260821-194234.log` to `init-0.log` (2026-08-24, a winmm run). `Sein::Init` writes
  nothing before it allocates.

### 3.7 It is not a stale build

```bash
git log -1 --format='%h %ad %s' --date=short -- dll/src/Lugner_Dxgi.cpp dll/src/Lugner_Dxgi.asm dll/src/ProxyDxgi.def
```
→ `cda2f720 2026-08-19` — i.e. nothing in the mechanism changed between the dumped build 3315 and
the user's 3362 re-test.

### 3.8 The cross-game correlation

| Game | proxy flavour | AppCompat layer | Result |
|---|---|---|---|
| Avowed, Elliot, SEED, TQ2 | dxgi | none | ✅ `dxgi proxy: lazily forwarded 20/20` |
| **Octopath Traveler** | **dxgi** | **HIGHDPIAWARE** | ❌ will not start |
| Octopath Traveler | winmm | HIGHDPIAWARE | ✅ works |
| Geri | version | `~ HIGHDPIAWARE` | ✅ works (see §5) |

Derive the flavour column with:
```bash
grep -a -oh "dxgi proxy:\|winmm proxy:\|Loaded real version.dll" "$LOCALAPPDATA/UE5CEDumper/Logs"/*/*.log | sort -u
```

## 4. Why winmm survives and dxgi does not

Same binary. Same `Heiter.cpp` DllMain. Same import set. Same `/MT` CRT. Same lazy-thunk shape.
The asymmetry is **who calls one of our exports, and when**:

| | dxgi flavour | winmm flavour |
|---|---|---|
| First caller | `AcGenral!NS_DXGICompat`, inside `LdrInitializeThunk` | the game, during audio/RHI setup |
| When | **before `_DllMainCRTStartup`** → `__acrt_heap == 0` | ~2.0 s after DllMain |
| Result | `malloc` on a null heap → AV | logger fully up, resolver logs fine |

Measured from a real winmm session (`init-0.log`, 2026-08-24): DllMain at `12:56:53.038`,
`winmm proxy: lazily forwarded 180/180` at `12:56:55.017`.

`AcGenral.dll` contains **no** UTF-16 `"winmm.dll"` and **zero** occurrences of
`waveOutGetNumDevs` or `timeBeginPeriod` — the only two winmm names this EXE imports. Its only
winmm involvement is `NS_PrivacyMicrophone` installing an IAT hook on `waveInOpen` — a pointer
replacement, not a call into our code, on an API this EXE does not import.

Which DLLs AcGenral *does* name (UTF-16 string search):
`dxgi.dll` ✓ · `ninput.dll` ✓ · `d3d9.dll` ✓ · `winmm.dll` ✗ · `version.dll` ✗ · `dinput8.dll` ✗

## 5. `version.dll` — same class of defect, different trigger

⚠ **This section is reasoned from verified facts but was NOT put through the adversarial
verification the rest of this document was.** Treat §5's conclusion as an inference.

Verified facts:

1. `version.dll` is **not** a KnownDLL on this machine
   (`HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\KnownDLLs` has kernel32, combase, ole32,
   SHCORE, SHELL32, SHLWAPI, user32 — no version), so a `version.dll` in the game folder **is**
   loaded in preference to System32's.
2. `AcGenral.dll` **statically imports** `version.dll` — `GetFileVersionInfoSizeW`,
   `GetFileVersionInfoW`, `VerQueryValueW`.
3. In all three Octopath dumps the module list has `apphelp[4]`, `AcGenral[5]`, `shell32[20]`,
   `version.dll[21]` — stable across every dump.

Inference: on a shimmed game our `version.dll` proxy is pulled in as a **static dependency of
AcGenral**, and a static dependency is *initialised before its dependent*. So our DllMain runs,
the CRT comes up, and `shell32`/`ole32`/`user32` are already present. **No null-heap crash — the
game starts**, which matches both the user's report and the successful version-proxy runs on
`Geri-Win64-Shipping.exe` (also shimmed).

But the *latent* defect is the same: [`LoadRealVersion()`](../dll/src/Lugner.cpp) does
`LoadLibraryW` + `LOG_*` on the first forwarded call, and on a shimmed game that first call comes
from **AcGenral's shim init, under the loader lock, inside `LdrpInitializeProcess`**. It survives
today only because the CRT happens to be up by then. All four flavours share this shape — the
`ResolveAll` bodies in `Lugner_Dxgi.asm` and `Lugner_Winmm.asm` are identical except for the
callee name.

There is **no evidence** for the user's recollection that "version.dll used to work in this game
and later stopped": the Octopath log folder only goes back to 2026-08-18 and contains **zero**
version-proxy runs. Anything earlier was swept by the 21-day retention.

## 6. What ReShade does differently

**The rule is not "do less in DllMain".** ReShade does *more* under the loader lock than we do —
file I/O, 44+ hook installs — and boots fine. The rule is:

> **Do not present an export that something calls before your CRT is up.**

Concretely, from the deployed ReShade 6.8.0.2158 (`ReShade64.dll`, 500 funcs / 494 names):

- **It does not export `SetAppCompatStringPointer`.** Nor `ApplyCompatResolutionQuirking`,
  `UpdateHMDEmulationStatus`, or `PIXBeginCapture/EndCapture/GetCaptureState`. It exports only
  `CompatString` @11 and `CompatValue` @12. AcGenral's `GetProcAddress` returns NULL and the
  `je 0x5d9b` skips the whole path — **the shim engine never enters ReShade's code.**
- Its compat exports allocate nothing. `CompatString` is `call <ensure-loaded>` →
  `GetProcAddress` → `test/je → ret` → `jmp rax`. Zero heap on that path, and a **clean return**
  on failure rather than a jump through NULL.
- Its lazy-load helper is a no-op before its own DllMain: it early-outs on an empty path string
  that only DllMain fills in.
- Its ordinals are wrong on purpose (`CreateDXGIFactory` @13 vs the real @10) and it does not
  matter — both importers bind **by name**.
- Lazy-vs-eager is *not* the difference: `ReShade.log` shows
  `Registering hooks for 'C:\WINDOWS\system32\dxgi.dll' ... > Delayed until first call to an
  exported function`, with the real install happening later on the game's `CreateDXGIFactory1`.
  We defer exactly the same way.

## 7. Ruled out — do not re-raise

Each was tested and refuted during this investigation.

- **Missing or mis-ordinalled export.** `ProxyDxgi.def` maps all 20 real names to ordinals 1..20
  in order; ReShade matches 0 of 20 ordinals and works.
- **Loader dependency cycle.** 235-node static closure with apisets resolved: no path from
  `version/kernel32/user32/shell32/ole32` back to dxgi or winmm.
- **Import-descriptor position.** Wrong direction — winmm is descriptor #4, dxgi #17, and the
  *earlier* one is the one that works.
- **TLS callbacks / C++ static initialisers.** Both flavours have the identical 2 MSVC callbacks
  (both early-out on reason 1) and identical `__xc_a..__xc_z` (5 C / 48 C++).
- **Stack misalignment or missing unwind data in `ResolveAll`.** RSP is 16-aligned at the inner
  call; the 72-byte body is byte-identical between flavours except the call rel32.
- **First-loaded-wins passive-proxy race.** The crashing runs loaded System32's winmm — our winmm
  was not deployed. [Heiter.cpp:311-322](../dll/src/Heiter.cpp) never fired.
- **ReShade conflict.** No ReShade module in either dump; it had been renamed to `dxgi0.dll`,
  which nothing imports.
- **`d3d11`'s inbound static edge as the trigger.** A snapped IAT slot is a binding, not a call.

### Two claims made *during* this investigation that were wrong

Recorded so they are not repeated:

1. ❌ *"ReShade's dxgi.dll does not import version.dll."* It **does** — `version.dll` **and**
   `wininet.dll`. The original dump was truncated by a `tail`/`grep` pipeline. The import
   dependency tree is not the discriminator at all.
2. ❌ *"`d3d9.dll` and `d3d11.dll` both import dxgi."* Only `d3d11.dll` does, and only
   `CreateDXGIFactory2`. `d3d9.dll` imports nothing from dxgi.

## 8. The fix — planned, not yet implemented

Applies to **all four flavours**, not just dxgi.

### 8.1 Never allocate on the resolver path

`Lugner_Dxgi.cpp:120,122` · `Lugner_Winmm.cpp` · `Lugner.cpp` (`LoadRealVersion`) ·
`Lugner_Dinput8.cpp` (`LoadRealDinput8`)

Remove `LOG_*` from inside the resolvers. Record the outcome in POD statics
(`resolvedCount`, `lastError`) and emit the log line later, from DllMain or from the first call
that is known to be safe. `Sein` allocates on **every** path, including the "early buffer"
(`s_earlyBuffer.emplace_back` at [Sein.cpp:522](../dll/src/Sein.cpp)) — there is no safe logging
call before the CRT is up.

### 8.2 A CRT-ready gate

New shared helper in `Lugner.h`; the flag is set at the top of `DLL_PROCESS_ATTACH` in
`Heiter.cpp`. Each resolver checks it first. When not ready, the resolver may use only
`GetSystemDirectoryW` + `LoadLibraryW` + `GetProcAddress` (pure kernel32, zero allocation) and
must not touch `Sein` at all.

### 8.3 NULL-guard the thunks

`Lugner_Dxgi.asm:97` (`LAZY_THUNK`) and the winmm twin currently end in `jmp rax` — a failed
resolve jumps through NULL. Follow ReShade: `test rax, rax / jz → xor eax, eax / ret`. A shim
engine calling an export we cannot resolve should get a clean return, not a fault.

### 8.4 Optional, and only after the above

Consider not exporting `ApplyCompatResolutionQuirking` / `SetAppCompatStringPointer` /
`CompatString` / `CompatValue` at all — only the shim engine calls them, and dropping them is
exactly how ReShade avoids the window. This masks the symptom; 8.1–8.3 fix the cause. Note
`tools/check_proxy_exports.py` re-derives the export table against the real System32 one, so
dropping names is not a one-line change.

## 9. How to verify the fix

1. `build.ps1 -Target DLL`
2. Deploy `dist/proxy/dxgi.dll` to the Octopath `Binaries\Win64` folder (rename ReShade's aside).
3. The game must start, and `%LOCALAPPDATA%\UE5CEDumper\Logs\Octopath_Traveler-Win64-Shipping\init-0.log`
   must contain `dxgi proxy: lazily forwarded 20/20`.

**The negative control already exists** — `out/proxy-backups/Avowed.dxgi.dll.20260823-212124.bak`
is the binary that crashes. A fix that cannot be shown to differ from it has not been shown to
work.

A cheaper, game-free reproduction would be a test EXE with a `HIGHDPIAWARE` layer applied
(`reg add HKCU\...\AppCompatFlags\Layers`) that statically imports a dxgi-named proxy. Not built.

## 10. Artifacts and traps

**Machine-local, volatile** (WER prunes its queue; `out/` is gitignored):

- `%LOCALAPPDATA%\CrashDumps\Octopath_Traveler-Win64-Shipping.exe.{23324,6604}.dmp` — 2026-08-23 09:22
- `C:\ProgramData\Microsoft\Windows\WER\ReportQueue\AppCrash_Octopath_Travele_…` — `Report.wer`
  + `…appcompat.txt` (the file that proved the loaded dxgi.dll was ours) + a third copy of the dump
- `out/proxy-backups/Avowed.dxgi.dll.20260823-212124.bak` — the crashing binary

**Traps hit during this investigation, worth not repeating:**

- ⚠ **`UploadTime` is not `EventTime`.** The WER event surfaced on 2026-08-26 but
  `EventTime=134319217554888574` decodes to **2026-08-23 09:22**. Reading the wrong field led to
  the crash first being attributed to the wrong day and then to ReShade.
- ⚠ **Resolve dump RVAs against the dumped build, not the current one.** See §3.3.
- ⚠ **Minidump module order is stable in the head but not exactly.** `apphelp[4]`, `AcGenral[5]`,
  `shell32[20]`, `version.dll[21]` are identical across all three dumps, but `winmm` is `[31]` or
  `[32]` and `dxgi` `[37]` or `[38]`. Do not build a fine-grained ordering argument on one index.
- ⚠ **ntdll's exports are far too sparse for nearest-export symbolisation.** `RtlNtdllName+0x6F9C`
  is a `.rdata` data pointer, not a function. Use the `.pdata` function table to tell code from
  data.
- ⚠ In git-bash, `wevtutil qe Application /c:5 …` fails with "too many arguments" because MSYS
  rewrites `/c:5` as a path. Prefix with `MSYS2_ARG_CONV_EXCL='*' MSYS_NO_PATHCONV=1`.
- ⚠ Do not name a scratch script `dis.py` — it shadows the stdlib module `capstone` imports.

---

*Investigation: 6 parallel evidence lenses + 68 adversarial verifiers + synthesis
(75 agents, 0 errors). Every claim above survived a refutation pass except where §5 says
otherwise.*
