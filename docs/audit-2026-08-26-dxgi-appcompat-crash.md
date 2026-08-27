# The dxgi proxy vs. the AppCompat shim engine — root cause, and the fix

**Status: CLOSED.** Root cause confirmed at instruction level (2026-08-26); fix shipped in builds
**3363 + 3365** and **verified in-game on OCTOPATH TRAVELER, 2026-08-27, build 3366** — the game
starts, the dumper attaches, 406,060 objects. See §9.
⚠ 3363 alone turned the crash into a **hang**; §8.6 is the second half, and the reason it was
needed is that this document had recorded that half as "deliberately out of scope".
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
| **Octopath Traveler** | **dxgi** | **HIGHDPIAWARE** | ❌ would not start ≤3362 · ⏸ hung on 3363 · ✅ 20/20 from **3365** |
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

## 8. The fix — SHIPPED builds 3363 + 3365

Applies to **all four flavours**, not just dxgi: they are one binary with one DllMain, and the
`ResolveAll` bodies are identical except for the callee name.

### 8.1 A CRT-ready latch, and a pre-CRT gate on every resolver

`Lugner::g_crtReady` (`dll/src/Lugner.h`) is a plain `volatile LONG` with a **constant**
initialiser, so it lives in zero-filled `.data` with no dynamic initialiser and is readable
before the CRT exists. `Heiter.cpp`'s `DLL_PROCESS_ATTACH` sets it with `Lugner::MarkCrtReady()`
as its **first statement** — reaching DllMain means `__acrt_initialize` has already run.

Every resolver now opens with:

```cpp
if (!Lugner::CrtReady()) { Lugner::NotePreCrtCall(); return; }
```

in `DxgiProxy_EnsureResolved`, `WinmmProxy_EnsureResolved`, `LoadRealVersion` and
`LoadRealDinput8`. Nothing is latched, so the host's own first call resolves normally.

⚠ **This deviates from the original plan, deliberately.** The plan said a pre-CRT resolver "may
use only `GetSystemDirectoryW` + `LoadLibraryW` + `GetProcAddress` (pure kernel32, zero
allocation)". That is allocation-safe but **not loader-lock-safe**: the pre-CRT caller is the
loader itself, so a `LoadLibraryW` there is the recursive-load hazard that the eager-resolve
version of this proxy already crashed on once. Refusing outright is what ReShade does, and it is
strictly safer.

### 8.2 The deferred report — the only thing that keeps the refusal visible

`Lugner::g_preCrtCalls` is an `InterlockedIncrement` counter (no allocation, no CRT). `DllMain`
reports it once, after `Sein::InitProcessMirror`, as a `LOG_WARN`. Without it the refusal is
completely invisible — no log, no crash — and the next person asking "why did the compat shim not
apply?" has nothing. This is the counter's only consumer.

### 8.3 The thunks distinguish the two reasons a slot is null

`Lugner_Dxgi.asm` / `Lugner_Winmm.asm` gained a second null test plus a new data symbol
`mResolveAttempted`. This is the part that needed care, because a naive "return 0 on null" would
have silently reversed a decision recorded in `Lugner_Winmm.asm` (B44/B48):

| state | meaning | answer |
|---|---|---|
| `mResolveAttempted == 0` | resolver **refused** — CRT not up | `xor eax,eax / ret` — answer 0, do not forward |
| `mResolveAttempted == 1` | resolver ran, the **name is genuinely absent** from the host DLL | fall through to `jmp rax` with `rax == 0` — a loud crash, ON PURPOSE |

The second row must stay: for winmm `0 == TIMERR_NOERROR`, so a stub returning 0 for a missing
`timeBeginPeriod` would silently no-op the 1 ms tick instead of saying anything. ⚠ Note the
consequence for the first row: a winmm caller inside the pre-CRT window *does* get
`TIMERR_NOERROR` without the tick being set. That is accepted — the window is transient, nothing
is latched, and the alternative is the crash being fixed.

### 8.4 The magic statics had to go with it

`Lugner.cpp` (17 sites) and `Lugner_Dinput8.cpp` (6) cached with
`static auto fn = reinterpret_cast<Fn>(RealProc("..."));`. **A magic static latches its value
exactly once** — so the gate above would have latched `nullptr` on a pre-CRT first call and left
the export dead for the life of the process, which is worse than the crash it removes. All 23 are
now `static Fn fn = nullptr; if (!fn) fn = ...`, which also drops the CRT's
`_Init_thread_header` machinery off a path that must not need the CRT.

### 8.5 The winmm generator was already stale, and is now fixed

`Lugner_Winmm.{cpp,asm,def}` are generated by `scripts/gen_proxy_forwarders.py`, and `--check`
reported **`Lugner_Winmm.cpp STALE`** before this work: the AD18 `SystemDllPath` fix had been
hand-applied to the .cpp and never back-ported, so anyone re-running the generator would have
silently reintroduced the drive-root-relative-path defect. That check is **not in CI**. AD18 and
the changes above are now both in the generator; `--check` is clean again.

### 8.6 ⚠ The deadlock that fix exposed — and the deferral that caused it (build 3365)

**This section used to say the dxgi resolver's SRWLOCK was "NOT done — deliberately out of scope".
That deferral was wrong, and it cost a whole extra round trip.** Build 3363 removed the crash;
the game then **hung with no window**, its log stopping at `DllMain: auto-start thread created OK`.

That last line is diagnostic on its own: a thread created inside DllMain cannot run until the
loader lock is released, so the auto-start thread never printing its own first line means **the
loader lock was never released**.

`tools/verify/hang_dump.py` dumped the wedge (`out/hang-…-24836.dmp`). Main thread stack,
innermost first:

```
ntdll+0x6019B                  <- RtlAcquireSRWLockExclusive's wait (ZwWaitForAlertByThreadId)
dxgi.dll+0x2ACC90              <- our own .data: the SRWLOCK object itself
dxgi.dll+0x18993F              <- our thunk, SECOND pass
AcGenral+0x5D78/5C00/22E2/5420, apphelp+0x1E696/1F81F     <- this block appears TWICE
dxgi.dll+0x1889CC              <- SetAppCompatStringPointer thunk, FIRST pass
AcGenral+0x5C338 ("SetAppCompatStringPointer"), +0x56360 (L"dxgi.dll")
```

All three threads we create in DllMain were parked in `ZwWaitForSingleObject`, waiting for the
loader lock the main thread held.

**The mechanism.** Our resolver calls `LoadLibraryW`. Loading the real `dxgi.dll` makes the loader
raise `apphelp!SE_DllLoaded`; `AcGenral`'s DXGICompat hookset then does
`GetModuleHandleW(L"dxgi.dll")` — **which resolves back to US**, because we are the module
registered under that name — `GetProcAddress`, and calls our thunk again **on the same thread**
while the resolver is still inside `LoadLibraryW`. SRWLOCK is documented **non-recursive**, so the
second `AcquireSRWLockExclusive` deadlocked.

This is audit #4 **B43**, which removed that lock from the winmm twin for the sibling lock-order
reason and left dxgi alone, noting the dxgi original's safety argument was *"explicitly
CONDITIONAL on RHI init being the only entry point"*. It is not. The shim engine is a second entry
point, and it arrives under the loader lock.

**The fix (build 3365):**

- **The SRWLOCK is gone.** Correctness without one is B43's argument verbatim: `mProcs[]` stores
  are aligned and pointer-sized so they cannot tear, racing resolvers write identical values, and
  "nobody observes a half-populated table" is preserved by the thunk's own null test. The log line
  is claimed once with `InterlockedCompareExchange`, after the work — the winmm shape.
- **`Lugner::ResolveReentry`** — a same-thread guard, in all four flavours. A nested call returns
  at once; its thunk then sees an unresolved slot with `mResolveAttempted` still 0 and answers 0
  **without forwarding**, which is exactly what ReShade's compat exports do. The **outer** call
  completes the resolve and forwards for real.
  ⚠ Deliberately **not** a "first caller wins" mutual exclusion: a loser returning with its slot
  still null would hand the *game* a null `CreateDXGIFactory1`. Only same-thread re-entry bails; a
  cross-thread race falls through and resolves too, which is safe because the work is idempotent.
  **Removing the lock is the cure; the guard is defence.**

## 9. Verification

### 🟢 In-game: CONFIRMED on OCTOPATH TRAVELER, 2026-08-27, build 3366

The game starts, and `init-0.log` contains every predicted line, in the predicted order:

```
10:17:32.450 [WARN]  Proxy: 1 forwarded call(s) arrived BEFORE our CRT was initialised …
10:17:32.452 [INFO]  DllMain: auto-start thread created OK
10:17:32.456 [INFO]  dxgi proxy: lazily forwarded 20/20 exports to real System32 dxgi.dll
10:17:32.475 [INFO]  DllMain ProxyStart: proxy DLL mode — starting pipe server only (no scan)
10:17:32.674 [INFO]  Sein::RunRetentionSweep: retention sweep done in 203 ms (off the loader lock)
10:17:32.984 [INFO]  DllMain ProxyStart: pipe server started
10:18:13.670 [SUMMARY] GObjects=0x7FF659775C10 GNames=0x20440C60010 GWorld=0x7FF6598590F8 Objects=406060
```

Two details worth reading rather than skimming:

- ⭐ **`1 forwarded call(s) arrived BEFORE our CRT was initialised` is still there, and that is the
  point.** It is the AppCompat shim engine's direct fingerprint — the root-cause analysis of §2/§3
  is not inference any more, the shipped build reports the mechanism happening.
- ⭐ **`lazily forwarded 20/20` lands at `.456` — 4 ms after the auto-start thread was created and
  *before* `proxy DLL mode` at `.475`.** That ordering is the §8.6 fix working: the resolve
  completed on the **loader thread**, inside the shim's outer call, and only then did the loader
  release and let our threads run. Under the 3363 build that resolve never returned.

The rest of the stack is up: mailbox poller, pipe server on `\\.\pipe\UE5DumpBfx`, UI attached,
UE 4.18 detected, GObjects/GNames/GWorld resolved, 406,060 objects.

### Offline, and how it was shown able to fail

`tools/verify/proxy_precrt_gate.py` maps a proxy with
`LoadLibraryExW(..., DONT_RESOLVE_DLL_REFERENCES)` — image mapped, exports callable, **DllMain not
called** — and calls a forwarding thunk from a child process, so a fault is an exit code rather
than a dead script.

```
$ py tools/verify/proxy_precrt_gate.py --compare dist/proxy/dxgi.dll \
      out/proxy-backups/Avowed.dxgi.dll.20260823-212124.bak SetAppCompatStringPointer

[NEGATIVE CONTROL] ...Avowed.dxgi.dll.20260823-212124.bak
      SetAppCompatStringPointer -> +0x187A2E
      => FAULTED 0xC0000005 (ACCESS VIOLATION)
[FIXED]            dist/proxy/dxgi.dll
      SetAppCompatStringPointer -> +0x1889CC
      => RETURNED CLEANLY
```

⭐ **`+0x187A2E` is the same RVA that is on the faulting stack of both Octopath minidumps** (§3.3).
The rig calls the exact instruction the dump named, and it dies there. The same comparison passes
for winmm against `OCTOPATH_TRAVELER.winmm.dll.20260824-123841.bak`. All four fixed proxies return
cleanly — dxgi/winmm/version `0`, dinput8 `0x80004005` (`E_FAIL`, its forwarder's documented
failure value, confirming the gate made `RealProc` return null rather than the thunk being
skipped).

Confirmed in the shipped machine code, not just the source — the thunk's two-null-cases guard:

```
1889e9  cmp  qword ptr [rip + 0x12dbcf], 0     ; mResolveAttempted
1889f1  jne  0x1889f6
1889f3  xor  eax, eax
1889f5  ret
1889f6  jmp  rax
```

and `DxgiProxy_EnsureResolved`'s imported calls after the §8.6 fix — `GetCurrentThreadId`,
`GetSystemDirectoryW`, `LoadLibraryW`, `GetProcAddress`, `GetLastError`, with **no SRWLOCK
acquisition anywhere on the path**.

13/13 gates, `py tools/check_proxy_exports.py --artifacts`, and
`python scripts/gen_proxy_forwarders.py winmm --check` all pass.

### ⚠ What is still NOT proven

- **The rig cannot discriminate for `version` / `dinput8`.** Their pre-fix binaries also return
  cleanly under it — those are plain C forwarders, not asm thunks, and their pre-fix path happens
  not to fault in this harness. The tool says so out loud (`FAIL: the negative control ALSO
  returned cleanly ... it proves nothing about the fix`) rather than printing a green tick. Their
  gate and re-entry guard are verified by construction and by the single-mode clean return only.
- **Neither flavour has had an in-game regression run since build 3363.** The dxgi run exercises
  the shared `Lugner.h` code path, but not their own forwarders.
- **The offline rig is not a byte-for-byte replay of the game crash.** Under
  `DONT_RESOLVE_DLL_REFERENCES` the IAT is not snapped either, so a pre-fix binary faults on its
  first imported call rather than on `HeapAlloc(NULL, ...)`. Same place in our code, earlier
  instruction.

## 10. Artifacts and traps

**Machine-local, volatile** (WER prunes its queue; `out/` is gitignored):

- `%LOCALAPPDATA%\CrashDumps\Octopath_Traveler-Win64-Shipping.exe.{23324,6604}.dmp` — 2026-08-23 09:22
- `C:\ProgramData\Microsoft\Windows\WER\ReportQueue\AppCrash_Octopath_Travele_…` — `Report.wer`
  + `…appcompat.txt` (the file that proved the loaded dxgi.dll was ours) + a third copy of the dump
- `out/proxy-backups/Avowed.dxgi.dll.20260823-212124.bak` — the crashing binary

**Traps hit during this investigation, worth not repeating.** These are also recorded — with the
reasoning, and with what each one cost — as
[working-lessons.md §3.6a](working-lessons.md), which is the copy to keep current; this list is the
short form:

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
