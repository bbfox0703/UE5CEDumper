# MED re-derivation — 2026-08-16 (pre-vetted, UNFIXED)

**What this file is.** Six open MEDs from
[audit-2026-08-13-early-code-findings.md](audit-2026-08-13-early-code-findings.md), each re-derived
from source and then put through a **refute-mandated skeptic** instructed to default to "refuted"
when uncertain. **All six survived; none is fixed.** This exists so the next fix session does not
repeat the derivation — it was ~1.5M tokens of agent work and lived only in a temp folder until it
was written here.

**Read this before using any of it:**

- **Five of the six came back BIGGER than filed** (`V3` came back *different*). That is the normal
  outcome on this repo — see [working-lessons.md](working-lessons.md) §2.1–2.5.
- **A verdict here is evidence a defect exists. It is NOT authority on the repair.** The audit's own
  history has a case (AB4) where the diagnosis was right and the prescribed fix could never have
  worked. Each entry carries `fix_shape` **and** `residual_risk`; the risk section has repeatedly
  been the one that mattered.
- **Line numbers drift.** Several filed numbers were already stale when re-derived (`U3` by ~200
  lines). Grep the identifier; never trust the number.
- This is **agent-authored text preserved verbatim** under each heading, so nothing is lost to
  paraphrase. It has survived a skeptic; it has **not** survived a compiler or a live game.

## Suggested order, and why

| # | finding | why here |
|---|---|---|
| ~~1~~ ✅ **DONE** | ~~**PX1**~~ | **Shipped build 3166** (`9ea249b8` dinput8, `ceaff6ad` version, `b0ccae6c` the CI check). Verified offline exactly as predicted — all four proxies diff clean against real System32, zero missing names, zero ordinal mismatches, nothing added to the live backlog. **Two of the filed premises were wrong in the direction that matters**: link.exe assigns unpinned exports from *(highest pinned + 1)*, not "alphabetically from 1" — so the obvious minimal fix (pin only the missing export) would have moved the five *correct* forwards off their ordinals, i.e. **worse than the bug** — and `/DEF:` merges with `__declspec(dllexport)` rather than suppressing it, so the missing name needed an *implementation*, not a `.def` line. Permanently re-derived by [`tools/check_proxy_exports.py`](../tools/check_proxy_exports.py). |
| ~~1~~ ✅ **DONE** | ~~**AF3**~~ | **Shipped build 3167.** Closed exactly as predicted — pure C#, 8 new tests, five source mutations each observed to fail, nothing added to the live backlog. Confirmed the entry's own warning: reusing `ResultOf` would have made the tests pass while testing nothing, so a separate `TruncatedResultOf` fixture was mandatory. One defect beyond the write-up: `SetBaseline`'s `GroupBy(...).First().Count` made the captured baseline depend on the Earliest-first **view toggle**, because `ApplyDiffAndFilter` re-sorts `_allEntries` in place. Half 2 (a `sort` param on `pe_profile_get`) deliberately deferred, per this entry's own "do NOT do half 2 alone". |
| 1 ⭐ TAKE FIRST | **A3** | S/low effort and a *user-visible* scan gap (GAS `Health`/`MaxHealth`/`Mana` share one `UScriptStruct`, so only the first is indexed). No target compiles `Aura.cpp`, so budget a live check — the first of the remaining four that does. |
| 4 | **U3** | Real and bigger than filed, but `Ubel.cpp` has no test target and the payload is a *display* preview — misleading rather than corrupting. |
| 5 — SHIP WITH V4 | **V3** | Same root cause as V4, one method apart. Fixing either alone leaves the identical hole next door. |
| 5 — SHIP WITH V3 | **V4** | ⚠ **Read `residual_risk` before writing a line.** The obvious fix (a `required` expected-parent parameter) would SILENTLY KILL the Go box, Find Refs and every cross-tab 'Open in Live Walker' handoff — two of `NavigateToAsync`'s three callers `Breadcrumbs.Clear()` first and legitimately have no parent. |

**Do not batch these into one commit.** Negative-control ONE change at a time — the 2026-08-16
session's own build-time guard shipped green while checking nothing, and only reverting the thing it
guarded exposed it (working-lessons §2.2).

---

## PX1 — BIGGER_THAN_FILED — ✅ FIXED build 3166 (2026-08-17)

> **Closed.** `9ea249b8` (dinput8: `Proxy_GetdfDIJoystick` + pin `@1..@6`), `ceaff6ad` (version: pin
> `@1..@17`, shipped separately per `residual_risk` #1 so the default proxy has one suspect),
> `b0ccae6c` ([`tools/check_proxy_exports.py`](../tools/check_proxy_exports.py) + baseline + CI ×2).
> Every prediction below held: verification WAS offline, the fix WAS additive, nothing was added to
> the live-verification backlog, and `residual_risk` #2 — "both ordinal maps are machine-local facts
> baked into the tree with nothing to re-derive them" — is answered by the committed baseline plus
> `--verify-system`. The two `## Fix shape` warnings both proved real: pinning only `@6` would have
> moved the five correct forwards off their ordinals, and a `.def` line alone would not have linked.
> The analysis below is preserved verbatim as filed.

### Premises of the filed finding that are WRONG

- 'Because the .def pins no ordinals, MSVC assigns ordinals alphabetically' — the OUTCOME is right but the RULE is wrong, and the wrong rule would mislead the fix. link.exe assigns unpinned exports in name-sorted order starting at (highest PINNED ordinal + 1), not always at 1. Measured: dxgi pins @1-@20 -> UE5_AutoStart @21; winmm pins @3-@182 -> UE5_AutoStart @183; dinput8/version pin nothing -> @6/@10. This is precisely why pinning @1..@6 fixes the collision with no other edit.

- 'A by-name static import of GetdfDIJoystick fails process creation' — true for a LOAD-TIME import, but the finding presents it as the primary path when the repo's own code says it is the rare one. ProxyImportAnalyzer.cs:283-285 records that dinput8 is normally reached by a run-time LoadLibrary + GetProcAddress (where the failure is a NULL return, not a launch failure) and that only 1 of 21 measured games statically imports dinput8 at all.

- 'an ordinal-#6 import calls UE5_AutoStart which STARTS THE AOB SCAN and returns a bool' — the caller is not stalled by the scan. Frieren.cpp:895-899 CreateThreads the work and :917 returns `true` immediately. The damage is the returned 1 being dereferenced as an LPCDIDATAFORMAT (instant AV) plus an unrequested background scan — not a multi-second hang.

- The finding scopes the defect to dinput8 alone. ProxyVersion.def:16-32 has the identical unpinned-ordinal defect on the DEFAULT proxy, aliasing EIGHT real exports (VerFindFileA/W, VerInstallFileA/W, VerLanguageNameA/W, VerQueryValueA/W at @10..@17) onto UE5_* functions. Nine collisions across two proxies, not one.

- Implicit in the filed framing (and stated outright by ProxyDinput8.def:8-9, which the finding treats as the forwarding surface): '/DEF: suppresses default exports.' FALSE. The .def lists 36 names; the shipped dinput8 proxy exports 66. __declspec(dllexport) symbols are merged in by the linker (Frieren.h:113, Mimic.h:405 are in the binary and not in the .def). Consequence for the fix: a .def line alone exports nothing — GetdfDIJoystick needs an implementation.


### What the code actually does

The filed mechanism is real and I re-measured it against the shipped artifacts rather than the source, with an inline PE export-table parser (the loader's own view).

REAL `C:\Windows\System32\dinput8.dll` exports SIX named functions, ordinal base 1:
`@1 DirectInput8Create · @2 DllCanUnloadNow · @3 DllGetClassObject · @4 DllRegisterServer · @5 DllUnregisterServer · @6 GetdfDIJoystick`.

OUR `D:\Github\UE5CEDumper\build\dll\dinput8.dll` and `dist\proxy\dinput8.dll` (byte-identical export tables, 66 exports) give:
`@1..@5` = the same five real names, then `@6 = UE5_AutoStart`, `@7 = UE5_CallProcessEvent`, … `@65 g_invokeMailbox`, `@66 g_mailboxContract`. `GetdfDIJoystick` is absent from the export table entirely. A set-difference over real-vs-proxy for all four proxies returns exactly `['GetdfDIJoystick']` for dinput8.

So both halves of the filed defect are MEASURED, not argued: (a) a name the real DLL exports is missing, and (b) the ordinal that name occupies is occupied by a different function of an incompatible signature.

THE ORDINAL RULE THE FINDING STATES IS IMPRECISE, AND THE IMPRECISION IS LOAD-BEARING FOR THE FIX. link.exe does not simply "assign alphabetically from 1". It assigns unpinned exports in name-sorted order starting at (highest pinned ordinal + 1); when nothing is pinned, that is 1. Measured across all four proxies: dxgi pins @1..@20 → `@21 = UE5_AutoStart`; winmm pins @3..@182 → `@183 = UE5_AutoStart`; dinput8 and version pin nothing → `UE5_AutoStart` lands at @6 and @10 respectively. This is why pinning @1..@6 in `ProxyDinput8.def` is a complete fix for the collision half — it pushes the whole `UE5_*` block to @7+ automatically.

WHAT THE FINDING MISSED — THE SAME DEFECT IS ON THE DEFAULT PROXY, EIGHT TIMES. `ProxyVersion.def` also pins no ordinals. Its name coverage IS complete (all 17 real names present), so the by-name failure does not apply — but eight real ordinals are aliased to our functions:
`@10 VerFindFileA→UE5_AutoStart · @11 VerFindFileW→UE5_CallProcessEvent · @12 VerInstallFileA→UE5_CallProcessEventDirect · @13 VerInstallFileW→UE5_EnsureGameThreadHook · @14 VerLanguageNameA→UE5_FindClass · @15 VerLanguageNameW→UE5_FindFunctionByName · @16 VerQueryValueA→UE5_FindInstanceOfClass · @17 VerQueryValueW→UE5_FindObject`.
version.dll is the UI's default proxy (`ProxyImportAnalyzer.Recommend` step 3, and docs/todo.md:540 "the UI default is back to version.dll"). The finding names one collision on the least-used proxy; the defect class is nine collisions across two, one of them the default.

The contrast the finding draws with winmm is instructive in the other direction: real winmm has one NONAME @2 export that the proxy skips, and because winmm pins ordinals, our @1/@2 are NULL EAT slots. An ordinal-2 import there fails loudly (`STATUS_ORDINAL_NOT_FOUND`) instead of silently calling the wrong function. That is the failure mode pinning buys.

REACHABILITY IS NARROWER THAN THE FILED TEXT IMPLIES, IN A WAY THE FINDING DID NOT CHECK. I parsed the Windows SDK import library `…\Windows Kits\10\Lib\10.0.26100.0\um\x64\dinput8.lib`: it contains exactly ONE short-import record (`DirectInput8Create`), and `GetdfDIJoystick` is a DEFINED symbol (section 7, storage class 2 EXTERNAL) inside a static archive member alongside `c_dfDIJoystick`. Anything linked with the modern Windows SDK therefore neither name-imports nor ordinal-imports it — the System32 export exists for legacy DirectX-SDK-era consumers and for third-party dinput8 wrappers. A scan of every PE in `C:\Windows\System32` found zero importers of dinput8 at all.

WHAT ACTUALLY HAPPENS ON AN ORDINAL-6 HIT, precisely. `UE5_AutoStart` no longer blocks for the scan: it `CreateThread`s the work and returns `true` immediately (`Frieren.cpp:895-917`). So the caller is not stalled for seconds — it gets `1` in AL and dereferences address 1 as an `LPCDIDATAFORMAT`. Immediate AV, plus a full AOB scan now running in a game that never asked for one. The filed "kicks off the whole AOB scan and returns a bool in RAX" is right in substance; the "whole scan" part is now off-thread.

ROOT CAUSE: the forwarding surface was hand-enumerated from a wrong count and the wrong number was then written into three comments that made it look verified. `ProxyDinput8.def:4` "Maps the 5 dinput8 exports", `Lugner_Dinput8.cpp:5` "forwards all 5 exports", `Lugner_Dinput8.cpp:12` "Only 5 exports vs version.dll's 17", `Lugner_Dinput8.cpp:54` "Forwarded exports (5 total)". `scripts/gen_proxy_forwarders.py` was written later precisely to stop this ("with 180 exports, winmm must NOT be hand-written") and dinput8 was never migrated onto it.

ONE PREMISE UNDERNEATH THE .def IS FALSE AND MATTERS FOR THE FIX. `ProxyDinput8.def:8-9` claims "/DEF: suppresses default exports, so UE5_* entries must be listed explicitly here." The shipped binary exports 66 names while the .def lists 36 — `UE5_EnsureGameThreadHook`, `UE5_SetFlyPreset`, `g_mailboxContract` etc. are in the binary and NOT in the .def. They are exported by `__declspec(dllexport)` (`Frieren.h:113`, `:229`, `Mimic.h:405`), which link.exe merges with the .def. This is also why audit #4's "ProxyDinput8.def missing UE5_CallProcessEventDirect" was correctly refuted (`@8 = UE5_CallProcessEventDirect` in the shipped file). The consequence: adding a name to the .def is NOT what exports it — GetdfDIJoystick needs an implementation, not just a line.

`ProxyImportAnalyzer` cannot warn, exactly as filed: `Analyze` reads only `IMAGE_IMPORT_DESCRIPTOR.Name` (offset +12) and never walks the ILT/IAT thunks, and `Classify` stores four bools.

### Evidence

- MEASURED (inline PE export parser, read-only) — C:\Windows\System32\dinput8.dll: 6 exports, base 1: '@1 DirectInput8Create / @2 DllCanUnloadNow / @3 DllGetClassObject / @4 DllRegisterServer / @5 DllUnregisterServer / @6 GetdfDIJoystick'

- MEASURED — D:\Github\UE5CEDumper\dist\proxy\dinput8.dll AND build\dll\dinput8.dll (identical): 66 exports: '@1 DirectInput8Create ... @5 DllUnregisterServer / @6 UE5_AutoStart / @7 UE5_CallProcessEvent / @8 UE5_CallProcessEventDirect ... @65 g_invokeMailbox / @66 g_mailboxContract'. Set-difference real-minus-proxy = ['GetdfDIJoystick'].

- dll/src/ProxyDinput8.def:14-19 — '    ; --- Windows dinput8.dll forwarding ---' / '    DirectInput8Create  = Proxy_DirectInput8Create' / '    DllCanUnloadNow     = Proxy_DllCanUnloadNow' / '    DllGetClassObject   = Proxy_DllGetClassObject' / '    DllRegisterServer   = Proxy_DllRegisterServer' / '    DllUnregisterServer = Proxy_DllUnregisterServer'  — five entries, no '@N' on any line, no GetdfDIJoystick.

- dll/src/ProxyDinput8.def:4 — '; Maps the 5 dinput8 exports to internal Proxy_* implementations,'  (the wrong count, written as fact)

- dll/src/ProxyDinput8.def:8-9 — '; The /DEF: linker flag suppresses default exports, so UE5_*' / '; entries must be listed explicitly here.'  — FALSE: the .def lists 36 names, the shipped binary exports 66.

- dll/src/Lugner_Dinput8.cpp:5 — '// dinput8.dll from System32 and forwards all 5 exports.'  and :12 — '// Only 5 exports vs version.dll's 17 -> minimal forwarding code.'  and :54 — '// -- Forwarded exports (5 total) ---'

- dll/src/ProxyDxgi.def:14-15 — '; sync with kDxgiExports[] (Lugner_Dxgi.cpp) and the .asm thunk order.' preceded at :13-14 by '; Lugner_Dxgi.cpp::DxgiProxy_EnsureResolved (NOT in DllMain -- see that file for why). The @ordinal values match real dxgi.dll so ordinal imports resolve too.'  — and dxgi.def:30-49 pins '@1'..'@20'.

- MEASURED — build\dll\dxgi.dll: '@19 DXGIGetDebugInterface1 / @20 DXGIReportAdapterConfiguration / @21 UE5_AutoStart'. build\dll\winmm.dll: '@182 waveOutWrite / @183 UE5_AutoStart', ordinal range 3..243, @1/@2 NULL. Establishes the real linker rule: unpinned exports start at (max pinned + 1), name-sorted.

- MEASURED (sibling defect, NOT in the filed claim) — real version.dll vs build\dll\version.dll: 0 missing names but 8 ORDINAL COLLISIONS: '@10 real=VerFindFileA ours=UE5_AutoStart / @11 real=VerFindFileW ours=UE5_CallProcessEvent / @12 real=VerInstallFileA ours=UE5_CallProcessEventDirect / @13 real=VerInstallFileW ours=UE5_EnsureGameThreadHook / @14 real=VerLanguageNameA ours=UE5_FindClass / @15 real=VerLanguageNameW ours=UE5_FindFunctionByName / @16 real=VerQueryValueA ours=UE5_FindInstanceOfClass / @17 real=VerQueryValueW ours=UE5_FindObject'.

- dll/src/ProxyVersion.def:16-32 — 'GetFileVersionInfoA         = Proxy_GetFileVersionInfoA' ... 'VerLanguageNameW            = Proxy_VerLanguageNameW'  — seventeen entries, not one '@N'. Same unpinned-ordinal defect as dinput8, on the default proxy.

- dll/src/Frieren.h:28 — '__declspec(dllexport) bool     UE5_AutoStart();'  (returns bool; caller of ordinal #6 expects LPCDIDATAFORMAT)

- dll/src/Frieren.cpp:895-899 + :917 — 'HANDLE h = CreateThread(nullptr, 0, [](LPVOID) -> DWORD {' / '        Routine::RunThreadGuarded("UE5_AutoStart", [] { UE5_AutoStartBlocking(); });' ... '    return true;'  — the scan is spawned off-thread and `true` (=1) is returned immediately, so the caller derefs address 1.

- dll/src/Frieren.h:113 — '__declspec(dllexport) bool      UE5_EnsureGameThreadHook();'  and dll/src/Mimic.h:405 — 'extern "C" __declspec(dllexport) extern Mimic::MailboxContract g_mailboxContract;'  — both exported by the shipped dinput8 proxy (@9, @66) yet absent from ProxyDinput8.def. Proves the .def's 'suppresses default exports' comment wrong.

- ui/UE5DumpUI/Services/ProxyImportAnalyzer.cs:172-176 — 'uint nameRva = ReadU32(pe, d + 12);   // IMAGE_IMPORT_DESCRIPTOR.Name' / 'uint firstThunk = ReadU32(pe, d + 16);' / 'if (nameRva == 0 && firstThunk == 0) break; // null terminator' / 'if (nameRva != 0 && RvaToOffset(nameRva) is long nOff)' / '    Classify(ReadAsciiZ(pe, nOff, len));'  — the ILT/IAT is never walked; only the DLL name is read.

- ui/UE5DumpUI/Services/ProxyImportAnalyzer.cs:156-159 — 'if (name.Equals("version.dll", ...)) ver = true;' ... 'else if (name.Equals("dinput8.dll", ...)) di8 = true;'  — four bools, no function list. Confirms 'Proxy Deploy cannot warn'.

- ui/UE5DumpUI/Services/ProxyImportAnalyzer.cs:283-287 — 'dinput8.dll is grouped with it on the MECHANISM (middleware reaches DirectInput through a run-time LoadLibrary("dinput8.dll") + GetProcAddress, which is why mod loaders ship that name) and has no local measurement -- only 1 of the 21 games imports it'  — the repo's own reachability measurement for the catastrophic (static-import) path.

- MEASURED — C:\Program Files (x86)\Windows Kits\10\Lib\10.0.26100.0\um\x64\dinput8.lib archive parse: exactly ONE short-import member (sym=DirectInput8Create dll=DINPUT8.dll). Member 5 COFF symbol table: 'c_dfDIJoystick val=0 sec=3 class=2 DEF' and 'GetdfDIJoystick val=0 sec=7 class=2 DEF' -- GetdfDIJoystick is STATICALLY DEFINED, not imported, by the modern SDK.

- scripts/gen_proxy_forwarders.py:3-5 — 'The 4th-proxy evaluation in docs/todo.md set the rule this implements: with 180 exports, winmm must NOT be hand-written.'  and :28-30 — 'ORDINAL-ONLY exports are skipped and reported: a game that imports the proxied DLL by ordinal rather than by name would miss them.'  The generator emits Lugner_<Cap>.cpp / .asm / Proxy<Cap>.def, so `dinput8` maps onto exactly the three existing filenames.

- dll/CMakeLists.txt:369-372 — '    target_link_options(UE5Dumper_ProxyDinput8 PRIVATE' / '        /DELAYLOAD:dinput8.dll' / '        /DEF:${CMAKE_CURRENT_SOURCE_DIR}/src/ProxyDinput8.def'  — the .def is the only export map for this target; there is no second /EXPORT source.

- dll/CMakeLists.txt:539-543 — 'add_executable(dll_helpers_test' / '    ${CMAKE_CURRENT_SOURCE_DIR}/tests/dll_helpers_test.cpp' / '    ${CMAKE_CURRENT_SOURCE_DIR}/src/Radar.cpp' / '    ${CMAKE_CURRENT_SOURCE_DIR}/src/Denken.cpp'  — the only other C++ test target is utf8_helpers_test; nothing compiles Lugner_Dinput8.cpp, and a .def is not compilable at all.

- git log --diff-filter=A -- dll/src/ProxyDinput8.def -> '71a42673 2026-05-08 feat(proxy): add dinput8.dll proxy DLL alternative + passive-mode mutex' -- single commit, never revised; the wrong count has been in the tree since day one.


### Fix shape

The finding files no explicit prescription, only an implied "do what ProxyDxgi.def does". That implication is correct in direction but incomplete in two ways, and one of them would produce a build that does not link.

WHAT WOULD BREAK IF DONE NAIVELY: adding `GetdfDIJoystick @6` to ProxyDinput8.def and nothing else fails at link time — there is no symbol to bind. And pinning only `@6` while leaving @1..@5 unpinned is worse than the status quo: unpinned exports would then start at @7, so the five real forwards would move OFF their correct ordinals.

MINIMAL CORRECT FIX (S effort, matches the filed severity):
1. `Lugner_Dinput8.cpp` — add a sixth forwarder beside the existing five. Its prototype is known and trivial (no args, pointer return), so the C-forwarder shape used by the other five is safe here; it does not need the asm-thunk machinery dxgi/winmm use for undocumented internals:
   `extern "C" void* WINAPI Proxy_GetdfDIJoystick() { using Fn = void*(WINAPI*)(); static auto fn = reinterpret_cast<Fn>(RealProc("GetdfDIJoystick")); return fn ? fn() : nullptr; }`
2. `ProxyDinput8.def` — pin ALL SIX, not just the new one:
   `DirectInput8Create = Proxy_DirectInput8Create @1` … `DllUnregisterServer = Proxy_DllUnregisterServer @5` … `GetdfDIJoystick = Proxy_GetdfDIJoystick @6`.
   The UE5_* block needs no edit: pinning @6 pushes it to @7+ automatically (verified against dxgi @21 and winmm @183).
3. Fix the four comments that made the wrong count look verified: ProxyDinput8.def:4, Lugner_Dinput8.cpp:5, :12, :54.

DO THE SIBLING IN THE SAME COMMIT: `ProxyVersion.def:16-32` needs `@1`..`@17` pinned in the REAL DLL's ordinal order — `GetFileVersionInfoA @1, GetFileVersionInfoByHandle @2, GetFileVersionInfoExA @3, GetFileVersionInfoExW @4, GetFileVersionInfoSizeA @5, GetFileVersionInfoSizeExA @6, GetFileVersionInfoSizeExW @7, GetFileVersionInfoSizeW @8, GetFileVersionInfoW @9, VerFindFileA @10, VerFindFileW @11, VerInstallFileA @12, VerInstallFileW @13, VerLanguageNameA @14, VerLanguageNameW @15, VerQueryValueA @16, VerQueryValueW @17`. Note the .def's current source order is NOT the ordinal order — copy the measured ordinals, do not renumber the existing lines. No new code is needed there; version's name coverage is already complete.

THE ALTERNATIVE — regenerate — IS NOT "S" AND SHOULD BE REFUSED FOR NOW. `py scripts/gen_proxy_forwarders.py dinput8` emits exactly the three existing filenames (Lugner_Dinput8.cpp / .asm / ProxyDinput8.def) in the dxgi/winmm shape with correct pinned ordinals, which is the drift-proof answer. But it replaces the hand-written Lugner_Dinput8.cpp with an asm-thunk variant, which requires `enable_language(ASM_MASM)` plus an isolated thunks object library in the `BUILD_PROXY_DINPUT8` block (dll/CMakeLists.txt:351-378) mirroring the dxgi one at :384-400 — a build-system change on the injected artifact, med risk, and it bakes THIS machine's System32 ordinal map into the tree. Worth doing eventually; not in the same commit as a 2-line safety fix.

DO NOT extend `ProxyImportAnalyzer.Analyze` to collect imported function names as part of this. It would let Proxy Deploy warn, but it is a separate feature on a different layer, and once the export exists there is nothing left to warn about.

### Testability

NO C++ TEST CAN PIN THIS, AND NOT FOR THE USUAL REASON. The usual reason (no target compiles the .cpp) holds — `dll/CMakeLists.txt:539-543` shows `dll_helpers_test` = `tests/dll_helpers_test.cpp` + `src/Radar.cpp` + `src/Denken.cpp`, and the only other target is `utf8_helpers_test`; nothing compiles `Lugner_Dinput8.cpp`. But the header-inline escape hatch that rescued MA2 does NOT apply here either: the defect lives in a `.def` file, which is linker input, not compilable code. There is no predicate to move into a header. `tests/dll_helpers_test.cpp` already includes Macht.h/Himmel.h/Grimoire.h/Solitar.h/Lineal.h/Neu.h/GraphPath.h/VersionNeedleScan.h — none of that helps.

C# CAN OBSERVE IT BUT SHOULD NOT OWN IT. `ProxyImportAnalyzer.ReadExportNames(Stream)` is `internal`, already unit-tested against a synthetic PE in `ui/UE5DumpUI.Tests/ProxyImportAnalyzerTests.cs`, and would happily assert `names.Contains("GetdfDIJoystick")` on a built proxy. But that makes the unit-test project depend on a build artifact it does not produce, and `ReadExportNames` reads the NAME table only — it cannot see the ordinal collision at all, which is half the defect.

THE RIGHT HOME IS A CI DERIVED-FACT CHECK, and this repo already has that family. `.github/workflows/ci.yml:126` (`check_derived_counts.py`), `:137` (`check_ue_sample_values.py`), `:145` (`check_audit_register.py`) are all stdlib-only, sub-second, and exist for exactly this failure mode — a fact stated by hand that nothing re-derives. A `tools/check_proxy_exports.py` in the same shape would parse each `dll/src/Proxy*.def` and the matching `%SystemRoot%\System32\<name>.dll` export table and assert two things a .def can express on its own: (a) every named export of the real DLL has a `.def` entry, and (b) every such entry pins the real ordinal. Both fail today on dinput8 (one missing name, no pins) and on version (no pins), and both pass on dxgi and winmm — so the check has a demonstrated ability to FAIL and a demonstrated negative control, which is what §1 of working-lessons.md demands.

Two traps that check must handle, or it will be worse than nothing: the runner's Windows build may export a different set than the maintainer's, so "a name in the .def that the local DLL lacks" must be tolerated while "a name the local DLL has that the .def lacks" fails; and it must run under 64-bit Python or WOW64 silently redirects System32 to SysWOW64 (`gen_proxy_forwarders.py:56-68` already documents this trap and refuses a PE32 source — reuse that guard verbatim).

`py scripts/gen_proxy_forwarders.py dinput8 --check` is NOT that check. It compares whole generated FILES, so it reports STALE for the hand-written trio no matter what — it is a regeneration tool, not a lint.

LIVE VERIFICATION IS EFFECTIVELY UNAVAILABLE and should not be added to the backlog. `ProxyImportAnalyzer.cs:285` records that only 1 of 21 measured games statically imports dinput8, none has a dinput8 proxy deployed, and the modern Windows SDK statically defines `GetdfDIJoystick` rather than importing it — so there is probably no game on the maintainer's machine that can exercise the failure. The honest verification is the CI check plus a post-build export-table diff, not a game session.

### Blast radius

I swept all four proxies by set-differencing the real System32 export table against the built artifact, which is the direct sibling grep for this defect class.

1. `dll/src/ProxyDinput8.def` — FILED. 1 missing name (`GetdfDIJoystick`), 1 ordinal collision (@6 → `UE5_AutoStart`).

2. `dll/src/ProxyVersion.def` — NOT FILED, AND IT IS THE DEFAULT PROXY. 0 missing names, 8 ordinal collisions (@10..@17: VerFindFileA/W, VerInstallFileA/W, VerLanguageNameA/W, VerQueryValueA/W → UE5_AutoStart, UE5_CallProcessEvent, UE5_CallProcessEventDirect, UE5_EnsureGameThreadHook, UE5_FindClass, UE5_FindFunctionByName, UE5_FindInstanceOfClass, UE5_FindObject). Same root cause (hand-written .def, no `@N` anywhere), 8× the count, on the flavour `ProxyImportAnalyzer.Recommend` hands out by default and that docs/todo.md:540 confirms is the UI default. Any fix commit that touches dinput8 and not this one has fixed the less important half.

3. `dll/src/ProxyWinmm.def` — CLEAN, and it is the negative control that proves pinning works. 0 ordinal collisions; the one real NONAME @2 export is skipped, leaving @1/@2 as NULL EAT slots, so an ordinal-2 import fails loudly rather than misbinding. Generated, and `gen_proxy_forwarders.py:28-30` documents the skip.

4. `dll/src/ProxyDxgi.def` — CLEAN. 0 missing, 0 collisions, @1..@20 pinned. Generated.

The pattern is exact: the two GENERATED .def files are correct, the two HAND-WRITTEN ones are both wrong, in the same way, for the same reason.

FILES THE FIX TOUCHES: `dll/src/ProxyDinput8.def` (add entry + pin @1..@6), `dll/src/Lugner_Dinput8.cpp` (add the sixth forwarder; correct the counts at :5, :12, :54), `dll/src/ProxyVersion.def` (pin @1..@17). No CMake change for the minimal fix — `dll/CMakeLists.txt:308-313` already lists `src/Lugner_Dinput8.cpp` in `PROXY_SPECIFIC_SOURCES` for every proxy target, gated by `#ifdef UE5_PROXY_DINPUT8_BUILD`, so a new function inside that guard compiles into the dinput8 target only.

WHAT THE FIX CANNOT BREAK, checked: the UE5_* ordinals shift (dinput8 @6+→@7+, version @10+→@18+), and nothing consumes them. CE Lua resolves by name (`getAddress("g_invokeMailbox")`, `Mimic.h:335-337`), the pipe protocol is JSON by name, `Frieren.h` exports are `__declspec(dllexport)` and unaffected by .def ordering, and `ProxyImportAnalyzer.FoundingExportNames` / `HasExportQuorum` (the leftover-proxy ownership probe) match on NAMES only (`ProxyImportAnalyzer.cs:457-484`) — so the Proxy Deploy cleanup feature is ordinal-blind and stays correct.

ONE THING TO RE-MEASURE AFTER BUILDING: re-run the real-vs-proxy export diff on all four artifacts. The current expected result is `[]` for dxgi and version-names, `['GetdfDIJoystick']` for dinput8, `['<ord2>']` for winmm; after the fix all four should be `[]` except winmm's documented NONAME, with zero ordinal mismatches anywhere.

### ⚠ Residual risk — what could still be wrong, or could delete working code

Nothing in the fix DELETES code — it is additive (one forwarder, ordinal annotations, four comment corrections), so the usual "the fix removes a live guard" risk does not apply here. What remains:

1. THE REAL RISK IS THE VERSION.DLL HALF, AND IT IS A RISK OF SCOPE, NOT OF CORRECTNESS. Pinning `@1..@17` in `ProxyVersion.def` rewrites the export layout of the proxy that is the UI default (`Recommend` step 3) and that 11 of 21 measured games load dynamically. It is additive and should be harmless, but "should be harmless" on the default proxy is exactly the class of claim this repo cannot verify without a game session. It buys a path I measured as unreachable (all version.lib imports are by NAME). If it ships at all, it must ship separately from the dinput8 fix so a regression has one suspect.

2. BOTH ORDINAL MAPS ARE MACHINE-LOCAL FACTS BAKED INTO THE TREE. I measured them on Windows 11 26200. Both real DLLs happen to be alphabetically ordered (dinput8 @1..@6, version @1..@17), which is what makes them stable across builds — but that is an observation, not a guarantee, and nothing in the repo re-derives it. Without the proposed CI check, the pinned numbers become another hand-written fact that looks verified, which is the exact root cause the finding identifies (`ProxyDinput8.def:4` "Maps the 5 dinput8 exports" repeated into three more comments).

3. THE C-FORWARDER PATTERN DOES NOT GENERALIZE. It is correct for `GetdfDIJoystick` only because I disassembled it and it is `lea rax,[rip+X]; ret`. `gen_proxy_forwarders.py:14-19` states the repo's own rule against C forwarders for unknown prototypes. If a later change adds more dinput8 forwards by copying this shape without measuring, it reintroduces the problem the asm thunks exist to prevent.

4. THE FIX CANNOT BE PROVEN BY ANY TEST THIS REPO CAN RUN. No C++ target compiles the file; the artefact is linker input; live verification is effectively unavailable (modern SDK `dinput8.lib` has exactly ONE short-import record, `DirectInput8Create`, and both input libraries vendored in-tree — GLFW `win32_joystick.c:84` "our clone of c_dfDIJoystick" and SDL3's `SDL_c_dfDIJoystick2` — deliberately define their own data format rather than call the export). So the honest evidence after the fix is a post-build export-table diff plus the CI check, and the fix should NOT be added to the live-verification register as if a game session could settle it.

5. If only `@6` were pinned and `@1..@5` left unpinned, the five real forwards would move OFF their correct ordinals — strictly worse than today. The fix is only safe as "pin all six".

---

## A3 — BIGGER_THAN_FILED

### Premises of the filed finding that are WRONG

- Filed line `Aura.cpp:6170` is stale and points at unrelated code — 6170 is inside `struct ScanField` (`int32_t structInnerOffset = 0;`). The defect is at 6372, the set is constructed at 6686. The comparison target `CollectSchemaLeaves` is at 4113 (4109 is a comment line), and its erase is at 4203.

- "the 2nd and 3rd field of a repeated struct TYPE are dropped" understates it twice over: it is the entire SUBTREE of every repeat that is dropped, and the guard is CROSS-BRANCH — a struct type first entered deep inside one branch silently deletes an unrelated top-level field elsewhere in the same class. Inside one `FTransform`, `FVector Translation` already blocks `FVector Scale3D`.

- "Hits GAS directly" is true but is the wrong headline. The dominant real-world hit is FVector/FRotator repeats on ordinary actors (`Location` found, `Velocity`/`Scale3D`/`Extent` not) under the Float / Double / NumericAll scans that are this tool's most common query. Framing it as a GAS issue would let a reviewer scope the fix or its verification to attribute sets.

- The finding does not say WHICH scans are affected. Vector data types (FVector/FRotator) are NOT affected — `acceptedStructNames` is non-empty there and the recursion at 6654 is skipped, so the guard never fires. Every other dt is affected. A verification run using an FVector scan would show nothing wrong.

- The finding names only `CollectSchemaLeaves` as the correct sibling. `CollectGroupLeaves` (Aura.cpp:8060-8102, Group Scan) is a SECOND correct implementation. That matters: Group Scan and Property-Search-Deep both surface `MaxHealth` while single-value Value Search does not, which is the observable tell and a distinct scanner-side cause for the working-lessons §5 report family (not the same as AB4).

- The filed effort/risk "S / low" is only true of the erase itself. `expandFields` has no output cap, unlike BOTH correct siblings — so the one-line fix as filed removes the only bound besides depth<=4 on a tree walk whose fan-out is (fields per struct)^4. The risk is not low unless the cap lands with it.

- Severity MED understates the reach (the flagship scan path, silently, on nearly every class), though it is defensible as a silent under-scan with working alternatives (Group Scan / Snapshot). Worth re-ranking when it is scheduled, not asserting here.


### What the code actually does

CONFIRMED at the mechanism level, but the filed blast radius and the filed fix are both too small.

`ScanForValue` builds its per-class scan index with a recursive lambda `expandFields` (dll/src/Aura.cpp:6363-6666). Its cycle guard is `if (!visited.insert(structAddr).second) return;` at Aura.cpp:6372 and there is NO matching `erase` anywhere in the function — I grepped every occurrence of `visited` in Aura.cpp and the only ones inside `ScanForValue` are the comment at 6359, the parameter at 6368, the insert at 6372, the recursive pass-through at 6660, and the construction+call at 6686-6688. The set is constructed ONCE per `buildClassIndex(classAddr)` call (Aura.cpp:6686) and threaded by reference through the whole tree walk, so it is a WHOLE-WALK "have I ever seen this UScriptStruct in this class" set, not a "am I currently inside it" path set.

Consequence: for any class, only the FIRST field of a given `UScriptStruct` type (in `ci.Fields` order) contributes leaves. Every later field of that same struct type is skipped entirely — its whole subtree, not just its first leaf.

Where the finding UNDERSTATES it, and this is the important part:

(1) The loss is CROSS-BRANCH, not "sibling fields of a repeated struct". Once `Vector` has been entered anywhere in the walk, every other `FVector` in that class at any depth is dropped. Inside a single `FTransform` the walk enters `FVector Translation` and then silently drops `FVector Scale3D`. On an ordinary actor a Float/Double/NumericAll scan gets `Location` but not `Velocity`, not `Extent`, not `Scale3D` — one FVector per class, ever. That is not a GAS corner case; it is the most common Value Search this tool runs. GAS is one instance of a general defect, not the scope of it.

(2) The affected surface is every non-vector single-value scan. `Radar::VectorStructNames` (Radar.cpp:315) returns `kEmpty` for every non-FVector/FRotator dt, and the struct recursion at Aura.cpp:6654 is gated on `acceptedStructNames.empty()`. So all numeric, Bool, FString, FName and FText scans take the recursion and are hit. (Vector scans never recurse, so they are unaffected — the guard is inert there.)

(3) Nothing else rescues the dropped leaves. `needsDeepWalk`/`WalkContainerLeaves` emits only container-reachable leaves at depth >= 1 (Aura.cpp:2188 comment), not object-body struct fields. The Native-C pass cannot help either: `Ubel::ComputeClassHoles` (Ubel.h:541) marks each top-level field's whole `Offset..Offset+Size*ArrayDim` footprint as occupied, so `MaxHealth`'s bytes are covered reflected territory and are never a hole. The loss is total and silent for the session.

(4) `git log -L` shows the guard was introduced by commit `da9865dd` "fix(value-search): recurse StructProperty so GAS / nested-struct leafs are reachable (build 740)" — the commit that added GAS support shipped with the bug that half-defeats it, which is why it never looked broken (Health was found; only the siblings were missing).

(5) The two correct siblings named in the finding and one it did not name confirm the intended contract: `CollectSchemaLeaves` erases at Aura.cpp:4203 with an explicit comment "Only guard along the active path so two sibling fields of the same struct type are both visited" (4126-4128), and `CollectGroupLeaves` (Group Scan) push/pops a vector at 8066/8102. So Group Scan and Property-Search-Deep both find `MaxHealth`; only single-value Value Search does not. That asymmetry is a distinct in-the-scanner cause for the "Value Search can't find field X" report family in working-lessons §5, separate from AB4.

### Evidence

- dll/src/Aura.cpp:6363-6372 (the defect) — `auto expandFields = [&](auto& self,` ... `int depth) -> void {` / `constexpr int kMaxDepth = 4;` / `if (depth > kMaxDepth) return;` / `if (!visited.insert(structAddr).second) return;  // cycle`  — no matching erase exists anywhere in the lambda; the closing brace is at 6666.

- dll/src/Aura.cpp:6686-6688 (the set's lifetime) — `std::unordered_set<uintptr_t> visited;` / `expandFields(expandFields, classAddr, /*baseOffset=*/0,` / `/*namePrefix=*/"", sci.fields, visited, /*depth=*/0);`  — one set for the entire class walk.

- dll/src/Aura.cpp:6654-6660 (the recursion that re-enters a repeated struct) — `if (acceptedStructNames.empty()` / `&& f.TypeName == "StructProperty" && f.Address) {` ... `self(self, nested, baseOffset + f.Offset, childPrefix, out, visited, depth + 1);`

- dll/src/Aura.cpp:4126-4129 + 4203 (the CORRECT sibling, and its stated intent) — `// Cycle guard: a self-referential struct (FFoo holds a TArray<FFoo>) would` / `// otherwise recurse forever. Only guard along the active path so two` / `// sibling fields of the same struct type are both visited.` / `if (!pathStructs.insert(structAddr).second) return;` ... and at 4203 `pathStructs.erase(structAddr);`

- dll/src/Aura.cpp:8065-8066 + 8102 (second correct sibling — Group Scan) — `for (uintptr_t v : visited) if (v == structAddr) return;  // cycle guard` / `visited.push_back(structAddr);` ... `visited.pop_back();`

- dll/src/Aura.cpp:4111 (the cap the correct sibling pairs with path-scoping) — `static constexpr size_t kMaxSchemaLeavesPerClass = 4000;`  — enforced at 4125 `if (out.size() >= kMaxSchemaLeavesPerClass) return;` and 4133 `if (out.size() >= kMaxSchemaLeavesPerClass) break;`. `expandFields` has NO equivalent: an awk sweep of Aura.cpp:6314-6690 finds only `kMaxStructDepth` / `kMaxDepth`, no `out.size()` check at all.

- dll/src/Radar.cpp:315 + the default arm (`static const std::vector<std::string> kEmpty;`) — `VectorStructNames` returns empty for every non-FVector/FRotator DataType, so `acceptedStructNames.empty()` is TRUE for all numeric/bool/string scans and the recursion at Aura.cpp:6654 is taken.

- dll/src/Ubel.h:541-556 (`inline std::vector<Interval> ComputeClassHoles(const ClassInfo& ci, ...)`) — `occupied.push_back({ f.Offset, static_cast<int32_t>(f.Offset + span) });` over `ci.Fields`, i.e. the whole StructProperty footprint is occupied; a dropped nested leaf is never a native hole, so the Native-C pass cannot rescue it.

- git log -L 6363,6375:dll/src/Aura.cpp → `da9865dd fix(value-search): recurse StructProperty so GAS / nested-struct leafs are reachable (build 740)` introduces `if (!visited.insert(structAddr).second) return;  // cycle` in the same hunk that adds the lambda.

- dll/tests/dll_helpers_test.cpp:37-38 — `#include "../src/Ubel.h"` and `#include "../src/Aura.h"` are ALREADY in the test file (the brief's include list was incomplete), so a header-inline pure guard placed in either header is compiled by dll_helpers_test today.


### Fix shape

The filed prescription ("make it path-scoped, like CollectSchemaLeaves") is RIGHT in diagnosis but INCOMPLETE as a repair — copying only half of the sibling it cites.

What the fix must do, both halves together:

1. Path-scope the guard. `expandFields` has multiple `return` paths (`depth > kMaxDepth` returns BEFORE the insert, which is fine, but the body has none — it falls out of the field loop), so a bare `visited.erase(structAddr);` before the closing brace at Aura.cpp:6666 is actually sufficient today. It is fragile to the next early-return added, so prefer an RAII scope guard over a manual erase.

2. Add the output cap the sibling pairs with path-scoping. `CollectSchemaLeaves` guards with `kMaxSchemaLeavesPerClass = 4000` checked at both entry (4125) and per-field (4133); `CollectGroupLeaves` takes `leafCap` and checks at 8064/8070. `expandFields` has NEITHER, and `visited` was doing that bounding job by accident. Erasing without adding a cap converts a silent under-scan into an unbounded per-class `sci.fields` vector: depth<=4 with wide struct fan-out, built per worker thread, then iterated per instance in the hot scan loop. Do not ship half of this.

3. Do NOT touch the same-named `seen` sets in linear chain walks — Aura.cpp:5519 (`CountClassParams` FProperty chain) and Ubel.cpp:636/707/1343/1372/1432 are single-path walks where a never-erased set is CORRECT. A sed over "insert(...).second) return/break" would break them.

Preferred shape, because it is the only one this repo can test (see testability): lift the rule into a header-inline pure RAII type rather than open-coding the erase a third time. Something like `Aura::ScopedStructVisit` in Aura.h (already included by dll_helpers_test.cpp) whose contract is exactly "refuse re-entry along the CURRENT path, allow re-entry on a DIFFERENT branch", plus a header-inline leaf-cap predicate. Then convert all three walkers (`expandFields`, `CollectSchemaLeaves`, `CollectGroupLeaves`) to it, so the three copies of this rule cannot drift apart again — which is what happened here.

Expected behavioural side effect to state in the commit, not a regression: candidate counts and first-scan time will RISE for classes with repeated struct types. `sci.fields` also feeds the batch-read plan (`batchMin`/`batchSpan`/`bodyFieldCount`, Aura.cpp:6696-6717), so widening the field set changes the density gate (`kBatchBytesPerField = 512`) verdict for some classes — expect batch-mode membership to shift.

### Testability

Testable, but only if the fix is shaped to be — the naive one-line erase is NOT pinnable.

Facts: no C++ target compiles `Aura.cpp` (dll/CMakeLists.txt:539-543 — `dll_helpers_test` = `tests/dll_helpers_test.cpp` + `src/Radar.cpp` + `src/Denken.cpp` only), and `expandFields` is a lambda local to `ScanForValue`, so as written it is unreachable from any test. It also reads live process memory via `Macht::ReadSafe` / `Ubel::WalkClassEx`, so the walker itself is not pure.

But the brief's include list is incomplete: `dll/tests/dll_helpers_test.cpp:37-38` ALREADY has `#include "../src/Ubel.h"` and `#include "../src/Aura.h"`. So the MA2 move applies directly — a header-inline, pure RAII visit-guard placed in `Aura.h` is compiled and testable today, with no CMake change. The test that would have caught this is a three-line negative control:
  - A -> B, unwind, A -> C -> B  => the second B MUST be admitted (this is the assertion that fails on today's code)
  - A -> B -> A                  => MUST be refused (cycle still guarded)
  - guard destructor must not erase when its own insert was refused (otherwise the outer frame's entry is removed on unwind)
Plus a cap test: N admissions then refusal.

Without that extraction there is no unit route at all, and the fix joins the shipped-but-unverified backlog. The live-verification fallback is weaker and needs a game: run a Float Value Search on a class with two FVector members (any actor: `Location` + `Velocity`) or a GAS `UAttributeSet`, and check the second one appears. Note there is no seam that reports index size today — `LOG_INFO("Radar: First Scan dt=%s ... over %d objects", ...)` (Aura.cpp:6257) prints object count only, so a temporary per-class field-count log would have to be added to observe the before/after directly rather than inferring from candidate counts.

No C# involvement; `ui/UE5DumpUI.Tests/` is not a route for this.

### Blast radius

Defect site — exactly one:
- `dll/src/Aura.cpp:6363-6666` (`expandFields`) + its single construction site at 6686-6688 inside `buildClassIndex`. `buildClassIndex` is local to `ScanForValue`; `ScanForValue` has one caller, `dll/src/Fern.cpp:3002` (the `value_scan` begin handler). `refine` / `query_candidates` reuse the session's already-built candidate set (`Radar::SessionManager::Instance().Begin(...)`, Fern.cpp:3007), so there is no second entry point and no rebuild that could mask or re-expose the loss mid-session.

Sibling grep — I checked every recursive struct-descent site in the DLL (`grep -rn FSTRUCTPROP_STRUCT dll/src/`) and every set-based guard (`grep -rn "insert(.*).second)" dll/src/*.cpp`). NO other site shares the defect:
- `CollectSchemaLeaves` (Aura.cpp:4113, Property Search deep) — path-scoped (erase at 4203) + capped. Correct.
- `CollectGroupLeaves` (Aura.cpp:8060, Group Scan) — path-scoped (pop_back at 8102) + `leafCap`. Correct.
- `CollectGroupLeavesCrossObject` (Aura.cpp:~8141) — `subVisited.clear()` before each sub-object walk (8177); its `seen` is an OBJECT dedup, which is intentional and correct.
- `CollectContainersRecursive` (Aura.cpp:~2127), `EmitStructDirectLeaves` (Aura.cpp:~2230), `collectStructArrayInner` (Aura.cpp:6314) — depth cap only, no visited set at all, so no false drop. (`collectStructArrayInner` also has no output cap; same latent unboundedness as the fixed `expandFields` would have, worth capping in the same commit but it is not causing a drop today.)

Do-not-touch (would be broken by a mechanical sed): the linear chain walks where a never-erased set is CORRECT — `Aura.cpp:5519` (`CountClassParams`), `Ubel.cpp:636`, `707`, `1343`, `1372`, `1432`.

Downstream of the fix inside the same function: `sci.fields` also drives the batch-read plan (Aura.cpp:6696-6717 → `batchMin`/`batchSpan`/`bodyFieldCount`) and the per-instance read loop, so a class that regains fields costs (new fields x instances) more reads and may cross the `kMinBatchFields`/`kBatchBytesPerField` density gate in either direction. Also relevant to `docs/todo.md`'s verification register and `docs/working-lessons.md` §5: this is a THIRD in-the-scanner cause for "Value Search can't find field X", distinct from AB4 — its tell is that Group Scan and Property Search Deep DO find the field while single-value Value Search does not, and that the loss is by STRUCT TYPE REPEAT, not by object and not by width.

### ⚠ Residual risk — what could still be wrong, or could delete working code

The DIAGNOSIS is safe; the PRESCRIBED FIX contains one step I would not take, plus four sharp edges.

1. DO NOT do the three-walker RAII unification as part of this fix. `CollectSchemaLeaves` (4113-4204) and `CollectGroupLeaves` (8056-8103) are CORRECT today and live in Aura.cpp, which NO test target compiles (dll/CMakeLists.txt:539-543). Converting two working walkers to a new shared type in an untestable translation unit is the shape of change that regresses Property-Search-Deep and Group Scan with no unit route to catch it, in a repo already carrying 31 unverified batches. Ship the minimal change in `expandFields` only; treat unification as a separate, optional, separately-verified commit.

2. The header-inline RAII type IS unit-testable (Aura.h is included by dll_helpers_test.cpp:37) but the test pins the TYPE, not the CALL SITE. Nothing compiled by any target proves `expandFields` actually uses it — a correct guard sitting next to an unchanged walker passes every test and fixes nothing. This is a partial pin; the fix still needs live verification (Float scan on a class with two FVector members, e.g. USceneComponent's RelativeLocation + ComponentVelocity, or a GAS UAttributeSet's Health + MaxHealth).

3. The cap is not a nice-to-have and it is not free. Adding a mid-walk cap truncates by field-enumeration order, and because `ci.Fields` is the flattened super chain with INHERITED fields spliced in at `begin()` (Ubel.cpp:884), the leaf-most class's own fields sit LAST. A tight cap would therefore preferentially cut exactly the game-specific sibling this fix exists to restore — trading a silent drop for a different silent drop. Use a generous ceiling (the 4000 of kMaxSchemaLeavesPerClass) and make truncation observable, or the fix is unfalsifiable in the field.

4. Two user-visible effects that will read as regressions if not stated in the commit: `sci.fields` is iterated per instance across the entire GObjects pool, so a class whose field count multiplies slows first scan proportionally; and a wider candidate set hits `maxResults` earlier, so a scan that previously returned N rows may now truncate and DROP rows the user saw before. Also `sci.batchMin`/`batchSpan`/`bodyFieldCount` (6696-6715) all move, changing the `batchSpan <= bodyFieldCount * kBatchBytesPerField` density verdict — batch-read membership will shift for some classes.

5. Scope discipline: vector scans (`dt` = FVector/FRotator) never take the 6654 recursion, so their behaviour MUST be unchanged. If a vector scan's candidate count moves after this fix, something else was touched. And do not sed the same-named never-erased sets elsewhere — Aura.cpp:5519 and the Ubel.cpp chain walks are single-path walks where no erase is correct.

---

## AF3 — BIGGER_THAN_FILED — ✅ FIXED build 3167 (2026-08-17)

> **Closed.** Every prediction in this entry held, including the fixture warning: reusing `ResultOf`
> would have produced tests that pass while asserting nothing, so `TruncatedResultOf` was added
> alongside it. Half 1 shipped in full; half 2 (a `sort` parameter on `pe_profile_get` so the UI can
> request the low-count tail) is deliberately NOT done, per this entry's own "do NOT do half 2
> alone" — the false-NEW manufacture is client-side and survives any DLL change. One defect beyond
> the write-up: `SetBaseline` captured through `GroupBy(...).First().Count`, which made the baseline
> value depend on the Earliest-first VIEW toggle. Analysis below preserved verbatim.

### What the code actually does

Every clause of the filed claim is literally true, and the line number is (unusually) still accurate. But the filed defect is the SMALL half of what the truncation does.

**The mechanism as filed (confirmed).** `LiveFuncsViewModel.FetchAndPopulateAsync` (ui/UE5DumpUI/ViewModels/LiveFuncsViewModel.cs:208-233) calls `_dump.PeProfileGetAsync(FetchLimit)` with `FetchLimit = 300` (:31). DLL side, `Fern.cpp`'s `CMD_PE_PROFILE_GET` sorts the whole snapshot by fire count descending and then emits only the first `limit` rows (Fern.cpp:3920-3921, :3937), while `data["distinct_funcs"]` is `snap.size()` — the FULL pre-cap distinct count (Fern.cpp:3983; pipe-protocol.md:513 says "pre-cap" explicitly). The VM then stores the truncated page as `_allEntries` (:211), counts `newCount`/`increased` over that page, and prints them "of {result.DistinctFuncs:N0}" (:222-225). `SetBaseline` builds `_baseline` from the same `_allEntries` (:121). Any Class::Func that was in the idle window but ranked below #300 is absent from the baseline, so on the action fetch `_baseline.TryGetValue` misses → `e.IsNew = true` (:252) → it sorts to the very top (`OrderByDescending(e => e.IsNew)`, :266) and survives the default `NewChangedOnly` filter (:304). False NEW rows, exactly as filed.

**Why it is bigger — 1: the cap deletes the tool's own target, not just the baseline.** The DLL's selection policy is count DESC. This panel's stated purpose, in its own dev-log, is to find the function with a *low* count: "The action-specific function is near the top with a low count (a handful of calls); per-frame Tick/Update noise has huge counts." Count-desc + cap removes precisely the low-count tail. On any game where a Start→action→Stop window dispatches more than 300 distinct UFunctions, `OpenShop` (count 1-3) is not in the page at all — it is not a mis-ranked row, it is an absent row, and the panel gives no signal of that. `Linie::StartRecording` does `g_stats.reserve(4096)` (Linie.cpp:66), which is the author's own estimate of the table size; nothing in Linie caps it. This inverts the feature: the more the game dispatches, the more certain it is that the answer is the part that got cut. A user filtering for a function that fired 2× gets an empty grid and concludes it never fired.

**Why it is bigger — 2: the surviving tail is arbitrary and differs between the two windows.** `std::sort` is not stable, and the input vector order is `unordered_map` iteration order (Linie.cpp:92). Every count-1 function ties. `StartRecording` clears and rebuilds `g_stats` between the baseline and the action window (Linie.cpp:65), so the *set* of tied low-count functions admitted by the 300-cap is effectively arbitrary and unrelated between the two recordings. The false-NEW rate is therefore concentrated in the count-1 tail — the same tail where genuine NEW rows live — so false NEWs are indistinguishable from true ones by inspection. The status line then tells the user "The action's function is almost certainly among the NEW rows at the top" (:225).

**Why it is bigger — 3: `Entries.Count` is not even `min(limit, DistinctFuncs)`.** Rows whose `Ubel::ResolveFunctionInfo` fails are silently `continue`d (Fern.cpp:3940) and the cooperative `Tot::Requested()` abort can `break` mid-loop (Fern.cpp:3938). `emitted` is logged (Fern.cpp:3975-3979) but never put on the wire, so the UI cannot distinguish "capped" from "aborted" from "stale pointers dropped". It can only observe `Entries.Count < DistinctFuncs`, which it never checks.

Minor, same function, not filed: `SetBaseline`'s `GroupBy(Key).ToDictionary(g => g.Key, g => g.First().Count)` (:121) collapses duplicate Class::Func keys to the largest count (the list is already count-desc), so a same-named sibling always computes a negative Delta and is hidden by `NewChangedOnly`. Rare, low impact.

Not defective: the non-diff status string "{DistinctFuncs} distinct functions, {TotalCalls} total calls" (:229) is a true statement about the recording — its fault is omission (no "showing top N"), not a wrong number.

### Evidence

- ui/UE5DumpUI/ViewModels/LiveFuncsViewModel.cs:30-31 — `/// <summary>How many top rows to fetch from the DLL (ranked by fire count).</summary>` / `private const int FetchLimit = 300;`

- ui/UE5DumpUI/ViewModels/LiveFuncsViewModel.cs:210-211 — `var result = await _dump.PeProfileGetAsync(FetchLimit);` / `_allEntries = result.Entries;`

- ui/UE5DumpUI/ViewModels/LiveFuncsViewModel.cs:222-225 — `int newCount = _allEntries.Count(e => e.IsNew);` / `int increased = _allEntries.Count(e => !e.IsNew && e.Delta > 0);` / `StatusText = $"vs baseline: {newCount} NEW + {increased} increased (of {result.DistinctFuncs:N0}). " + "The action's function is almost certainly among the NEW rows at the top.";`

- ui/UE5DumpUI/ViewModels/LiveFuncsViewModel.cs:121 — `_baseline = _allEntries.GroupBy(Key).ToDictionary(g => g.Key, g => g.First().Count);`

- ui/UE5DumpUI/ViewModels/LiveFuncsViewModel.cs:247-252 — `if (_baseline.TryGetValue(Key(e), out var baseCount)) { e.IsNew = false; e.Delta = e.Count - baseCount; }` / `else { e.IsNew = true; e.Delta = e.Count; }`

- ui/UE5DumpUI/ViewModels/LiveFuncsViewModel.cs:266-269 — `_allEntries = _allEntries.OrderByDescending(e => e.IsNew).ThenByDescending(e => e.Delta).ThenBy(e => e.Count).ToList();`

- ui/UE5DumpUI/ViewModels/LiveFuncsViewModel.cs:304 — `if (diffNewOnly && !(e.IsNew || e.Delta > 0)) continue;`

- dll/src/Fern.cpp:3917-3921 — `// Sort by fire count desc; resolve only the capped set (name resolution` / `// is the cost, so we pay it after the sort + cap, not per stored entry).` / `std::sort(snap.begin(), snap.end(), [](const Linie::FuncStat& a, const Linie::FuncStat& b) { return a.count > b.count; });`

- dll/src/Fern.cpp:3937-3940 — `for (size_t i = 0; i < snap.size() && emitted < limit; ++i) {` / `if ((i & 0xFFF) == 0 && Tot::Requested()) break;  // cooperative abort` / `if (!Ubel::ResolveFunctionInfo(snap[i].func, fi)) continue;  // drop stale/recycled`

- dll/src/Fern.cpp:3983 — `data["distinct_funcs"] = static_cast<int>(snap.size());`  (no `emitted` / `truncated` field is ever emitted)

- dll/src/Fern.cpp:3975-3979 — `Sein::Info("PIPE:profile", "pe_profile_get: %d distinct funcs, %llu total calls, %d emitted (limit %d); " ... static_cast<int>(snap.size()), (unsigned long long)totalCalls, emitted, limit, ...)`  — the DLL logs the exact distinct-vs-emitted pair the wire omits

- dll/src/Linie.cpp:63-67 — `void StartRecording() { std::lock_guard<std::mutex> lk(g_mu); g_stats.clear(); g_stats.reserve(4096);  // bound rehash churn over the recording window` / `g_seq = 0;`  — no cap on the table; 4096 is the author's own size estimate

- dll/src/Linie.cpp:88-92 — `void Snapshot(std::vector<FuncStat>& out) { ... for (const auto& kv : g_stats) {`  — the vector handed to the unstable `std::sort` is in `unordered_map` iteration order, so ties among equal counts resolve arbitrarily and differently per recording

- docs/pipe-protocol.md:513 — `"distinct_funcs": 214,     // distinct UFunctions seen (pre-cap)` and `// ... ranked by count desc, capped at `limit``

- docs/archive/dev-log-2026-07-pre-build-2200.md (build 2109 entry) — `The action-specific function is near the top with a low count (a handful of calls); per-frame Tick/Update noise has huge counts.`  — the feature's own statement that the target lives in the tail the count-desc cap removes

- ui/UE5DumpUI.Tests/LiveFuncsViewModelTests.cs:66-73 — `private static PeProfileResult ResultOf(params PeProfileEntry[] entries) => new() { ... DistinctFuncs = entries.Length, ... Entries = entries.ToList(), };`  — every existing test aliases DistinctFuncs to the page size, so no test can express truncation

- ui/UE5DumpUI/ViewModels/SnapshotViewModel.cs:1329 — `var trunc = diff.Truncated ? $" (capped at {filter.MaxRows:N0})" : "";`  and ui/UE5DumpUI/ViewModels/SpcQueryViewModel.cs:607 — `var trunc = res.Truncated ? $" (capped at {query.MaxRows:N0})" : "";`  — the house convention for surfacing a cap, which this panel does not follow


### Fix shape

No fix was prescribed in the register row, so there is nothing filed to refute — but the obvious instinct ("raise `FetchLimit` from 300") is wrong on its own and should be called out before anyone reaches for it: it trades a silent correctness bug for an unmeasured latency regression on the pipe thread, because the per-row cost the cap exists to avoid is stated at Fern.cpp:3917-3918 and is real (`Ubel::ResolveFunctionInfo` + `Ubel::GetName(GetOuter(...))` + `Aura::ClassDerivesFromAny` super-chain walk, per emitted row). It also leaves the panel still silently lying whenever the new number is exceeded.

Split the repair into a mandatory C#-only half and an optional DLL half.

**Half 1 — C#-only, S effort, fully unit-testable, do this first.**
1. In `FetchAndPopulateAsync`, derive `bool truncated = result.Entries.Count < result.DistinctFuncs;` (this is conservative and correct: it is true when the cap bit, when `ResolveFunctionInfo` dropped stale rows, and when the `Tot` abort fired — all three mean "not everything is shown"). Append the existing house string to BOTH status branches, matching `SnapshotViewModel.cs:1329` / `SpcQueryViewModel.cs:607`: e.g. `" — showing top {Entries.Count:N0} of {DistinctFuncs:N0} by count"`.
2. In the diff branch, stop presenting page-scoped counts against a table-scoped denominator. Either drop the `(of {DistinctFuncs})` clause or change it to `(of {Entries.Count:N0} shown; {DistinctFuncs:N0} recorded)`.
3. In `SetBaseline`, **refuse** to capture from a truncated page (or capture and mark the baseline partial). A partial baseline is worse than no baseline: it converts unseen idle functions into top-ranked NEW rows under the default `NewChangedOnly`, i.e. it actively fabricates the answer the user is told to trust. Store the `truncated` flag alongside `_baseline` and put it in `BaselineStatus`.

**Half 2 — DLL, M effort, needs live measurement.** The count-desc cap is the wrong *selection policy* for this panel, not just the wrong *size*. Correct shapes, in preference order: (a) add a `sort` parameter to `pe_profile_get` ("count" | "first_seq" | "rarest") so the UI can request the low-count tail, which is what the workflow actually wants; or (b) accept an uncapped fetch only for the baseline capture, after measuring the resolve cost with the existing `pe_profile_get` log line; or (c) at minimum emit `emitted` and a `truncated` bool on the wire so the client stops inferring truncation from a subtraction. Note (b) still needs half 1, because the action fetch stays capped.

Do NOT do half 2 alone — the false-NEW manufacture is a client-side bug and stays reachable through any DLL change.

### Testability

Unusually good for this repo — the whole filed defect is pinnable by a plain xUnit test with zero DLL involvement, which is the argument for fixing it now rather than adding it to the live-verification backlog.

`ui/UE5DumpUI.Tests/LiveFuncsViewModelTests.cs` already exists (~30 tests) with `FakeDumpService.NextGet` as a settable `PeProfileResult` and `PeProfileGetAsync` returning it verbatim (:36-40). A test can construct `new PeProfileResult { DistinctFuncs = 900, TotalCalls = ..., Entries = <the 300 highest-count rows> }` and assert (a) the status text names the truncation, (b) `SetBaseline` refuses or flags a partial baseline, (c) a function present in the idle *recording* but absent from the idle *page* does not come back as `IsNew` in the action fetch. `LiveFuncsViewModel`'s ctor takes `(IDumpService, ILoggingService, IPlatformService? = null)` and nothing in the diff path touches Avalonia, so no UI thread is required.

⚠ The reason no existing test caught this is a fixture that silently aliases the two numbers: `ResultOf(...)` sets `DistinctFuncs = entries.Length` (:70), so every one of the ~15 tests that use it is structurally incapable of expressing a truncated page — it always asserts against the degenerate `truncated == false` case. Any fix MUST add a second helper (e.g. `TruncatedResultOf(distinct, entries)`) rather than reusing `ResultOf`, or the new test will silently test nothing. This is the working-lessons §2.3 "fixture that reports coverage it does not have" trap, verbatim.

Free passive verification that the cap actually bites on a real game (no new session needed): the DLL already logs the exact pair, so grep any existing `%LOCALAPPDATA%\UE5CEDumper\Logs\<pid>\pipe-*.log` by FORMAT STRING for `pe_profile_get:` and compare `%d distinct funcs` against `%d emitted (limit %d)` (Fern.cpp:3975-3979; category `PIPE:profile` → prefix `PIPE` → `LF_Pipe`, Sein.cpp:75). `emitted == 300 < distinct` on any recorded session is proof; note that archived logs are LZX-compressed in place so `rg` still reads them.

The DLL half (Fern.cpp / Linie.cpp) is NOT unit-testable — no C++ test target compiles `Fern.cpp`, and `Linie.cpp` is not in `dll_helpers_test`'s source list either. That asymmetry is another reason to land half 1 first.

### Blast radius

**Direct call sites: one.** `PeProfileGetAsync` has exactly one production caller, `LiveFuncsViewModel.cs:210` (the other hits are the `IDumpService` declaration at Core/IDumpService.cs:394 and two test stubs). `FetchAndPopulateAsync` itself has two callers, `StopAsync` (:175) and `RefreshAsync` (:197) — both are affected identically and neither needs changing.

**Sibling grep for the same defect pattern (a client-side diff built from a capped page): clean.** Grepping `_baseline`/`Baseline` across `ui/UE5DumpUI/ViewModels` returns only `LiveFuncsViewModel.cs` and `SpcQueryViewModel.cs`; the SPC one is a snapshot-*pick* concept (`RecomputeBaselines` at :531 marks the oldest checked snapshot), not a count diff over a truncated fetch. So the false-NEW manufacture is confined to this one file.

**Sibling grep for the wider pattern (a cap reported against a full-population number): two known, one already filed.**
- `DetectStatsViewModel.cs` — audit register row **AF2** (already ✅ closed) is the same family: probing stops after 30 classes and unprobed rows render as probed. Same root cause, different panel. Worth reading its fix as the precedent for the status-string wording.
- `GroupMatch.cs:302` — audit **AF13** (open, LOW): per-slot cap truncates at 256 with no signal at all. Same family, C# side.
- `Denken.h:37` — audit **AF7** (open, LOW): `budgetHit` computed, logged, then dropped before the wire. Same family, and structurally identical to Fern's un-emitted `emitted` counter here.

**Counter-examples that define the house convention the fix should copy:** `SnapshotViewModel.cs:1329` and `SpcQueryViewModel.cs:607` both do `res.Truncated ? $" (capped at {MaxRows:N0})" : ""`. `InstanceFinderViewModel` goes further with a user-configurable `InstanceSearchCap`. So the repo already decided caps must be visible; Live Funcs is the outlier, not the norm.

**If half 2 is attempted:** changing `pe_profile_get`'s response shape (adding `emitted`/`truncated`, or a `sort` parameter) is an ADDITIVE pipe change — it does not touch `Mimic::MAILBOX_CONTRACT` (no CE Lua script consumes `pe_profile_get`; it is pipe-only), but `docs/pipe-protocol.md:508-525` and the command count in `CLAUDE.md` must be re-derived, and `tools/check_derived_counts.py` will catch a stale count.

**Not in scope but adjacent:** `FetchLimit = 300` is a bare literal in the VM rather than in `Constants.cs`, which brushes against CLAUDE.md's magic-words rule. Move it if the fix touches it; not a defect on its own.

### ⚠ Residual risk — what could still be wrong, or could delete working code

THE PRESCRIBED FIX IS BROKEN AS WRITTEN — half 1 item 3. It derives `truncated = result.Entries.Count < result.DistinctFuncs`, calls it "conservative and correct", and then uses it to REFUSE SetBaseline. That predicate also fires when Fern.cpp:3940's `if (!Ubel::ResolveFunctionInfo(snap[i].func, fi)) continue;` drops a recycled UFunction* — a benign, expected outcome after a GC/level-load (Ubel.cpp:1308-1314 rejects anything whose meta-class name != "Function"). A single dropped stale pointer on an entirely uncapped table would hard-block the panel's primary documented workflow (str.Tip.LF.SetBaseline: "Start → stand still → Stop → Set Baseline") with no override. That is a fix that deletes working behaviour. An EXACT client-side predicate already exists and was not used: `Entries.Count >= FetchLimit` ⟺ the cap bit (the loop cannot emit more than `limit`; the only false positive is the benign exact-fit case), and it does not false-fire on stale drops. Prefer that, and prefer flag-and-warn over refuse (the re-derivation offers "or capture and mark the baseline partial" parenthetically — that parenthetical should be the requirement, not the refusal).

MAGNITUDE IS UNMEASURED. Whether the 300-cap ever bites is not established anywhere. I grepped all 10 folders under %LOCALAPPDATA%\UE5CEDumper\Logs for both `pe_profile` and `PIPE:profile`: ZERO hits, so the proposed "free passive verification" returns nothing on this machine and the claim stays unverified in both directions. pipe-protocol.md:513's `"distinct_funcs": 214` is a synthetic example (AShopVendor::OpenShop, func_addr 0x1B2C3D40) and is BELOW 300, i.e. the only concrete number in the repo points AWAY from the cap biting. The only other signal is Linie.cpp:66 `g_stats.reserve(4096)`. On a native-heavy title the finding is dormant; on a Blueprint-heavy one (Satisfactory/FactoryGame is in the tested set) it is near-certain. Per working-lessons §1, the register row should record the impact as CONDITIONAL on distinct > FetchLimit, not as established.

FRAMING ERROR: "the false-NEW manufacture is a client-side bug and stays reachable through any DLL change" understates the cause. The manufacture comes from the SERVER returning a differently-tie-broken sample per fetch (std::sort is unstable over unordered_map iteration order, Linie.cpp:92, and StartRecording clears/rebuilds between windows, :65). The client can only warn or refuse. Half 1 makes the panel HONEST; it does not make the diff CORRECT. Do not mark AF3 closed on half 1 alone.

FIX-TIME SIBLING GREP: the same page-vs-table confusion may exist wherever a capped fetch feeds a client-side denominator. The house convention to match is SnapshotViewModel.cs:1329 / SpcQueryViewModel.cs:607 (`diff.Truncated ? $" (capped at {filter.MaxRows:N0})" : ""`) — note both of those get a real `Truncated` bool from the DLL, which is precisely what pe_profile_get does not emit.

---

## U3 — BIGGER_THAN_FILED

### Premises of the filed finding that are WRONG

- FILED LOCATION IS STALE. `Ubel.cpp:1662` is not the branch. `InterpretValue` is defined at `Ubel.cpp:1773`; the StructProperty arm is `:1863-1894`. (`:1662` is inside `GetInnerStructAddr`/`GetMapPairLayout`, unrelated.)

- "(USTRUCTs generally have no vtable)" IS RIGHT IN AGGREGATE BUT WRONG FOR THE STRUCT THE CODE WAS WRITTEN FOR, and wrong in the direction that makes a fix delete working behaviour. `FGameplayAttributeData` — named first in the branch's own comment at `:1861` — has a virtual destructor in GameplayAbilities, so it DOES carry a vtable at +0 with `BaseValue`/`CurrentValue` at +8/+0xC. The 8-skip is CORRECT there. Deleting the skip, which is what the finding's parenthetical invites, regresses every GAS attribute preview.

- "reads UE5 LWC doubles as floats" IS IMPRECISE ABOUT THE MECHANISM AND THEREFORE ABOUT THE OUTPUT. It does not narrow a double to a float; it splits each 8-byte double into TWO 4-byte halves, so the COUNT of printed numbers is wrong too — a 3-component LWC FVector prints four values (`f:[0.0000, -4.1638, 0.0000, 3.3516]` for (1234.5, -678.25, 90.0)). Anyone verifying a fix by counting components would otherwise be looking for the wrong thing.

- THE LWC CASE IS THE SECONDARY SYMPTOM, NOT THE PRIMARY ONE. The only live-confirmed failure (`docs/todo.md:2357-2359`) is a plain 12-byte 3-float `FVector3f` inside a TMap, where the 8-skip prints exactly one genuine number — the LAST component — with nothing to signal that two were dropped. That is worse than garbage because it looks correct, and the finding does not claim it at all.

- "preview" UNDERSTATES REACH. This is not one preview path. Six confirmed call sites carry `"StructProperty"` into the branch (`:4420`/`:4448`, `:4560`/`:4584`, `:4686`/`:4763`, `:5701`, `:5995`, `:6280`) spanning Live Walker struct fields, TMap keys AND values, TSet elements, the Property Search preview column and DataTable rows — plus a latent seventh through the ungated `read_array_elements` pipe handler at `Fern.cpp:2334`.

- THE FINDING FRAMES THIS AS A BAD CONSTANT; IT IS A TYPE-BLIND DECODER USED WHERE THE TYPE IS KNOWN. Every reachable site already holds the struct identity and throws it away: `:5995` has `m.structType` and uses it only as a post-hoc fallback at `:6003`; `:4448` has `fv.mapValueStructAddr` (`:4396`); `:4686` has `fv.setElemStructAddr` (`:4659`); `:6280` has `fi.structType` (`:6258`). No amount of tuning the `8` fixes that.

- A CASE THE FINDING MISSES ENTIRELY: `FLinearColor` (16 B, 4 floats) silently loses R and G — the skip lands past them and only B and A print. Same for any 16-byte 4-float struct. This is a third distinct failure mode alongside truncation-to-last and double-splitting.

- MY OWN MID-DERIVATION ASSUMPTION WAS WRONG AND I RECORD IT SO IT IS NOT RE-RAISED: `ReadStructArrayElements` (`:2563`) and the TOptional tail (`:5410`) do NOT reach the struct branch — both short-circuit StructProperty first, at `:2475-2476` and `:5392-5398` respectively.


### What the code actually does

The code is real but the filed line is stale by ~200 lines: the branch is `dll/src/Ubel.cpp:1863-1894` (function `InterpretValue` starts at `:1773`), not `:1662`.

WHAT IT ACTUALLY DOES. For `typeName == "StructProperty"` it ignores the struct's identity entirely and reinterprets the raw bytes as an array of 4-byte floats after an unconditional 8-byte skip: `int floatStart = (size > 8) ? 8 : 0; int floatCount = (size - floatStart) / 4;` then prints each with `%.4f` as `f:[a, b, c]`. It never sees a UScriptStruct*, a field list, or a member width. Because `floatCount <= 16` is required, only structs of 9..72 bytes (and 4..8 bytes via the `floatStart = 0` arm) produce a hint at all; larger ones return "" and callers fall back to hex.

THE FILED CLAIM UNDERSTATES THE DOMINANT SYMPTOM. The finding leads with LWC doubles. The case actually observed on a live game is the non-LWC one, and it is worse because it looks correct: a UE4/`FVector3f` 12-byte 3-float struct gives `floatStart=8, floatCount=1`, so the panel prints ONE number — the LAST component — as if it were the whole struct. `docs/todo.md:2357-2359` records exactly this (`Map_IntToVec3f` → `f:[6203.0000]`, "one float, the **last** one", raw hex holds all three). So the defect is a SILENT DROP of components, not only a garbled read. Same shape on `FLinearColor` (16 B, 4 floats): R and G are dropped, only B and A print — a case the finding does not mention at all.

THE LWC CLAUSE IS TRUE BUT IS NOT "reads a double as a float". For a UE5 24-byte `FVector` (3 doubles) the skip eats the whole X double, then splits the Y and Z doubles into FOUR float halves. Worked example, location (1234.5, -678.25, 90.0): Y = 0xC0853A0000000000 → low half 0.0f, high half -4.1638f; Z = 0x4056800000000000 → low half 0.0f, high half 3.3516f. Output `f:[0.0000, -4.1638, 0.0000, 3.3516]` — four numbers for a three-component vector, none of them a value in the struct. The `anyMeaningful` gate (`v != 0 && v == v && -1e12 < v < 1e12`, `:1874`) passes on the high halves, so garbage is published rather than suppressed.

REACHABILITY IS SIX PATHS, NOT ONE "preview". Confirmed live call sites that can carry `"StructProperty"`: `:4448` and `:4584` (TMap value, FProperty + UProperty twins), `:4420`/`:4560` (TMap key), `:4686`/`:4763` (TSet element), `:5995` (`ResolvePropertyPreviews` — the Property Search preview column), `:6280` (`WalkDataTableRows` row fields), and `:5701` (WalkInstance's generic tail, reached by the explicit fall-through at `:4916` whenever the UScriptStruct* did not resolve or `previewLimit == 0`). A seventh is latent: `Fern.cpp:2334-2346` dispatches `read_array_elements` with a client-supplied `inner_type` and NO `IsScalarArrayType` gate, so a request naming `StructProperty` reaches `:2022`.

TWO SITES I INITIALLY EXPECTED ARE NOT REACHABLE, and I correct my own reasoning: `ReadStructArrayElements` short-circuits at `:2475-2476` (`sf.value = "{" + cf.nestedTypeName + "}"`), and the TOptional path short-circuits at `:5392-5398`.

THE REPO ALREADY KNOWS. Two in-tree comments name this function as the wrong answer — `:4830` ("Much more accurate than InterpretValue's \"interpret all bytes as floats\"") and `:5395` ("rather than rendering garbage via InterpretValue(\"StructProperty\", ...)") — and the correct field-driven decoder already exists 3,000 lines later at `:4832-4899`. Meanwhile `Radar.h:185-198`, `Wirbel.cpp:94`, `Laufen.cpp:97`, `Hemmung.cpp:75`, `Solide.cpp:92`, `Schlacht.cpp:69`, `Dunste.cpp:119` all state and apply the repo rule that width comes from the reflected ElementSize and never from a guess. `InterpretValue` is the lone violator.

WHERE THE FINDING'S RATIONALE IS WRONG IN THE DANGEROUS DIRECTION. The parenthetical "USTRUCTs generally have no vtable" is true in aggregate but false for the FIRST struct the code's own comment names: `FGameplayAttributeData` (GAS) declares a virtual destructor, so it genuinely carries a vtable at +0 with `BaseValue`/`CurrentValue` at +8/+0xC — the 8-skip is CORRECT there and is why the heuristic was written and why it survived. Acting on the finding as written ("remove the bogus skip") would regress every GAS attribute preview into `f:[<vtable low>, <vtable high>, BaseValue, CurrentValue]`.

### Evidence

- dll/src/Ubel.cpp:1863-1867 — the branch itself: `if (typeName == "StructProperty" && size >= 4) {` / `        // Skip the first 8 bytes if size > 8 — often a vtable/pointer preamble.` / `        // For structs <= 8 bytes, interpret from byte 0.` / `        int floatStart = (size > 8) ? 8 : 0;` / `        int floatCount = (size - floatStart) / 4;`

- dll/src/Ubel.cpp:1860-1862 — the comment that applies ONE rule to two families needing opposite rules: `    // StructProperty: for small structs, show inline float hints` / `    // Many gameplay structs (FGameplayAttributeData, FVector, FRotator, etc.)` / `    // contain float fields. Show a summary like "f:[100.0, 100.0]" for quick analysis.`

- dll/src/Ubel.cpp:1871-1877 — the garbage gate that lets split-double halves through: `                float v;` / `                memcpy(&v, bytes + floatStart + i * 4, 4);` / `                if (v != 0.0f && v == v && v > -1e12f && v < 1e12f) { // not zero, not NaN, reasonable range` / `                    anyMeaningful = true;`

- dll/src/Ubel.cpp:1881-1888 — every member printed as a 4-byte float regardless of declared width: `                for (int i = 0; i < floatCount; ++i) {` / `                    float v;` / `                    memcpy(&v, bytes + floatStart + i * 4, 4);` / `                    snprintf(buf, sizeof(buf), "%.4f", v);`

- dll/src/Ubel.cpp:5990-5995 + 6002-6005 — Property Search preview HAS the struct identity and uses it only after the garbage hint wins: `        // --- StructProperty: try float hint, fallback to type name ---` / `                    std::string hint = InterpretValue(t, buf.data(), sz);` / `                    if (!hint.empty()) { m.preview = hint; continue; }` … `            // Fallback: show struct type name` / `            if (!m.structType.empty()) { m.preview = "{" + m.structType + "}"; }`

- dll/src/Ubel.cpp:4448 — TMap value (the path live-observed as broken): `                                    ce.value = InterpretValue(valueTypeName, valBuf.data(), fv.mapValueSize);`  — and the UScriptStruct* is already resolved four lines earlier at :4396 as `fv.mapValueStructAddr`

- dll/src/Ubel.cpp:4686 — TSet element, with `fv.setElemStructAddr` / `fv.setElemStructType` already resolved at :4659-4660: `                                ce.key = InterpretValue(elemTypeName, elemBuf.data(), fv.setElemSize);`

- dll/src/Ubel.cpp:6280 — DataTable row fields, with `fi.structType` in hand at :6258 and :6298: `                fv.typedValue = InterpretValue(fi.TypeName, p, readSize);`

- dll/src/Ubel.cpp:4916 + 5675 + 5701 — the WalkInstance fall-through that carries StructProperty into the generic tail: `            // Fall through to generic scalar handler if struct not resolved` … `        // Scalar or struct: read raw bytes and interpret.` … `                fv.typedValue = InterpretValue(fi.TypeName, buf.data(), readSize);`

- dll/src/Ubel.cpp:4829-4830 — in-tree admission #1, sitting directly above the CORRECT implementation: `            // Generate field-based preview using cached WalkClass data.` / `            // Much more accurate than InterpretValue's "interpret all bytes as floats".`

- dll/src/Ubel.cpp:5392-5398 — in-tree admission #2, a path deliberately routed around this function: `            } else if (isStructInner) {` / `                // Struct inner but no scalar sub-fields previewable — at` / `                // least confirm the wrapper type rather than rendering` / `                // garbage via InterpretValue("StructProperty", ...).`

- dll/src/Ubel.cpp:2475-2476 — NOT a call site (corrects an assumption I made mid-derivation): `            } else if (cf.typeName == "StructProperty") {` / `                sf.value = cf.nestedTypeName.empty() ? "{Struct}" : "{" + cf.nestedTypeName + "}";`

- dll/src/Radar.h:190-198 — the repo-wide rule this function violates: `// 12 bytes, so the STRUCT NAME does not determine the width and neither does` / `// the engine version: one UE5 game holds fields of both widths.` … `constexpr int32_t VECTOR_WIDTH_FLOAT  = 12;   // 3 x float  (UE4, FVector3f)` / `constexpr int32_t VECTOR_WIDTH_DOUBLE = 24;   // 3 x double (UE5 LWC FVector)`

- docs/todo.md:2357-2359 — the live observation, non-LWC, which the filed claim does not describe: `> **Incidental — D1/U3 CONFIRMED LIVE as still broken (not yet fixed).** \`Map_IntToVec3f\` renders as` / `> \`f:[6203.0000]\`: one float, the **last** one. The raw hex holds all three correct values, so the` / `> loss is in \`InterpretValue\`'s 8-byte "vtable preamble" skip — 12-byte struct − 8 = one float.`

- dll/src/Fern.cpp:2334-2346 — the ungated pipe boundary (latent seventh path): `            if (innerType.empty() || elemSize <= 0)` / `                return Renge::MakeError(id, "missing inner_type or invalid elem_size").dump();` … `            auto result = Ubel::ReadArrayElements(` / `                addr, fieldOffset, innerAddr, innerType, elemSize, offset, limit);`  — no `IsScalarArrayType` check anywhere in the handler

- dll/tests/dll_helpers_test.cpp:36 — Ubel.h is ALREADY in the test's include set: `#include "../src/Ubel.h"        // Native-C scan P0: ComputeHoles / ComputeClassHoles / NormalizeGuessedTypeToProperty (inline, pure)`

- dll/CMakeLists.txt:539-543 — and Ubel.cpp is NOT compiled into it: `    add_executable(dll_helpers_test` / `        ${CMAKE_CURRENT_SOURCE_DIR}/tests/dll_helpers_test.cpp` / `        ${CMAKE_CURRENT_SOURCE_DIR}/src/Radar.cpp` / `        ${CMAKE_CURRENT_SOURCE_DIR}/src/Denken.cpp` / `    )`


### Fix shape

THE FILED PRESCRIPTION IS EFFECTIVELY "the 8-byte skip is bogus and the doubles are read as floats", and taken literally BOTH halves produce a broken repair.

Why "drop the 8-byte skip" is broken: it regresses `FGameplayAttributeData` (real vtable, `BaseValue`/`CurrentValue` at +8/+0xC), which is the case the heuristic exists for and arguably the most valuable struct in the list. Negative control before touching anything: a GAS attribute must still read `f:[<BaseValue>, <CurrentValue>]`.

Why "read them as doubles" is also broken: `InterpretValue` receives only `(typeName, bytes, size)`. Size does not determine member width — `Radar.h:190-191` states this explicitly ("the STRUCT NAME does not determine the width and neither does the engine version: one UE5 game holds fields of both widths"). A 24-byte struct is 3 doubles, or 6 floats, or 4 floats + a pointer. Swapping one guess for another is the same defect with different numbers.

THE ACTUAL REPAIR — stop guessing, use the layout the callers already hold. This is the repo's own rule and its own existing implementation:

1. Give the decoder the field list. Add a pure overload beside the existing one, e.g. `InterpretStructValue(const uint8_t* bytes, int32_t size, const std::vector<PreviewField>& fields)` where `PreviewField = {offset, size, typeName}`. Decode each member at its OWN offset with its OWN reflected width — 4-byte `FloatProperty`, 8-byte `DoubleProperty`, `IntProperty`, `BoolProperty` — exactly as `Ubel.cpp:4868-4886` already does. This automatically handles the vtable (there is no member at +0, so nothing prints from there) and LWC (width comes from the property), and it needs no magic constant at all.

2. Wire the four sites that already hold the identity: `:5995` pass `WalkClass(m.structClassAddr)`'s fields (the site already knows `m.structType`, so plumb the addr alongside it); `:4448`/`:4420` use `fv.mapValueStructAddr`/`fv.mapKeyStructAddr`; `:4686` uses `fv.setElemStructAddr`; `:6280` uses the row struct's nested `UScriptStruct*`. All are `WalkClass()` calls, which is cached — `:4833` notes "cached — just hash lookup".

3. Keep a fallback, but make it HONEST. When no layout resolves (`:5701`'s fall-through, an unresolvable UScriptStruct*), do NOT emit `f:[...]`. Emit `{StructName}` or nothing and let the caller show hex — which is precisely what `:5392-5398` and `:6002-6004` already do. A number the code cannot justify should not be printed at all; the whole cost of this defect is that a wrong number is indistinguishable from a right one.

4. If a byte-blind path must survive for a genuinely unknown struct, gate the 8-skip on evidence rather than on `size > 8`: the first 8 bytes read as a plausible in-module code pointer (`Grimoire::IsUserspacePointer` + module-range test, the same test `Aura.cpp:1911-1914` uses). Only then is "vtable preamble" a claim rather than a guess. I would rather see step 3 than step 4.

5. Optional but cheap: since `f:[` is parsed nowhere (see blast radius), the fixed output can be labelled — `{X=1.0, Y=2.0, Z=3.0}` — which makes a dropped component visible instead of silent. That alone would have caught this years earlier.

DO NOT also "fix" `Ubel.cpp:436` (`kScanBegin = 0x08; // skip the vtable at +0x00`). That one is legitimate: `FTextData` is a real polymorphic object.

### Testability

YES — testable today with zero new infrastructure, and this is the MA2 pattern already used in this repo.

CURRENT STATE. `dll/CMakeLists.txt:539-543` compiles only `tests/dll_helpers_test.cpp` + `src/Radar.cpp` + `src/Denken.cpp`, so `Ubel.cpp` is not linked and `InterpretValue` as it stands cannot be called from any test (it would fail at link, not compile). BUT `dll/tests/dll_helpers_test.cpp:36` ALREADY has `#include "../src/Ubel.h"`, and `Ubel.h` already hosts pure `inline` helpers that this test pins — `IsSanePropertiesSize` (`:421`), `ShouldPublishClassWalk` (`:441`), `ShouldPublishEnumTable` (`:455`), `ComputeHoles` (`:502`), `ComputeClassHoles` (`:541`), `NormalizeGuessedTypeToProperty` (`:564`). The seam is already open.

WHAT TO MOVE. The struct-preview decode is 100% pure: `(bytes, size, field list) -> string`, no `Macht::Read*`, no globals, no DynOff. Put the new `InterpretStructValue` (fix step 1) in `Ubel.h` as `inline`, exactly beside `ComputeHoles`. `dll_helpers_test` then covers it with synthetic byte buffers and no game process.

THE FOUR ASSERTIONS, EACH A NEGATIVE CONTROL THAT THE CURRENT CODE FAILS DIFFERENTLY — this matters because a fix aimed at only one of them regresses another:
- UE4 `FVector3f`, 12 B, 3 floats {1.0, 2.0, 6203.0} → must yield all THREE. Today: `f:[6203.0000]` (this is the live-observed failure, todo.md:2358).
- UE5 LWC `FVector`, 24 B, 3 doubles {1234.5, -678.25, 90.0} → must yield all THREE at their real magnitudes. Today: `f:[0.0000, -4.1638, 0.0000, 3.3516]` — four values, none correct.
- `FGameplayAttributeData`, 16 B = 8 B pointer-shaped preamble + BaseValue 100.0f + CurrentValue 75.0f → must yield exactly {100, 75} and must NOT surface the preamble halves. Today: correct — so this is the REGRESSION GUARD against "just delete the 8".
- `FLinearColor`, 16 B, 4 floats {0.1, 0.2, 0.3, 1.0} → must yield all FOUR. Today: only B and A.
Add a fifth for the honest-fallback rule: empty/unresolvable field list must return "" (or `{Name}`), never an `f:[...]`.

WHAT STAYS UNTESTABLE. The layout LOOKUP half — resolving the `UScriptStruct*` and `WalkClass()` — touches target memory and cannot be unit-tested here. That half is not new code though: it is the already-shipped path at `Ubel.cpp:4832-4899`, and the fix only routes more callers into it.

LIVE CHECK (one session, cheap, and there is a known-good vehicle): the todo.md entry names `Map_IntToVec3f` as a field that reproduces. Expand it in the Live Walker and confirm three components instead of one, cross-checked against the `hexValue` on the same row, which already holds the correct bytes. Then a UE5 LWC title for the 24-byte arm, and any GAS title for the regression guard.

C# SIDE: none needed and none possible. `ui/UE5DumpUI.Tests/` cannot reach this — a repo-wide grep for `f:\[` returns only `dll/src/Ubel.cpp:1862`/`:1880` and the todo.md note, so the UI never parses the string.

### Blast radius

SIBLING GREPS RUN, AND THE RESULT IS THE GOOD NEWS HERE.

1. `f:\[` across the whole repo returns exactly three hits: `dll/src/Ubel.cpp:1862` (the comment), `dll/src/Ubel.cpp:1880` (`std::string hint = "f:[";`), and `docs/todo.md:2358`. There is NO copy-pasted twin of this heuristic anywhere — not in `Radar.cpp` (whose `:982` only references `InterpretValue` in a comment), not in the C# UI. Blast radius is one function.

2. Because nothing parses `f:[...]`, the OUTPUT FORMAT IS FREE TO CHANGE. No UI code, no export path (`CeXmlExportService`, `CsxExport`, CT generators), and no test depends on the shape. Emitting `{X=1.0, Y=2.0, Z=3.0}` instead is safe and is what makes a dropped component visible.

3. CALL SITES THE FIX MUST VISIT (each already holds the struct identity it currently discards):
   - `dll/src/Ubel.cpp:4420` / `:4448` — FMapProperty key/value, FProperty path. Has `fv.mapKeyStructAddr` / `fv.mapValueStructAddr` (set at `:4394`/`:4396`). THIS IS THE LIVE-OBSERVED ONE.
   - `dll/src/Ubel.cpp:4560` / `:4584` — the UProperty (UE4 <4.25) twin of the same map code. Fixing only the FProperty arm leaves every pre-4.25 game broken; this pairing is a documented recurring miss in this file (see U1's "+UProperty twins" note in the audit register).
   - `dll/src/Ubel.cpp:4686` / `:4763` — FSetProperty element, both twins. Has `fv.setElemStructAddr` (`:4659`).
   - `dll/src/Ubel.cpp:5701` — WalkInstance generic tail, reached via the explicit fall-through at `:4916` when the UScriptStruct* did not resolve, and ALSO whenever `previewLimit == 0` (the gate is `if (found && fv.structClassAddr && previewLimit > 0)` at `:4832`; `previewLimit` is client-controlled — `Fern.cpp:2252` reads it from the request with default 2, clamped [0,6] at `:3591-3592`). A client asking for `preview_limit: 0` gets the garbage path, not "no preview".
   - `dll/src/Ubel.cpp:5995` — `ResolvePropertyPreviews`, the Property Search preview column. Highest user visibility; already has `m.structType` and uses it only as a fallback at `:6003`.
   - `dll/src/Ubel.cpp:6280` — `WalkDataTableRows` row fields. Has `fi.structType` (`:6258`, `:6298`).

4. LATENT SEVENTH PATH, worth closing in the same commit: `dll/src/Fern.cpp:2334-2346` dispatches `read_array_elements` with a client-supplied `inner_type` and validates only `!empty` and `elem_size <= 256`. There is no `IsScalarArrayType` gate, even though that predicate exists at `Ubel.cpp:1905` precisely to route struct inners to `ReadStructArrayElements`. A request naming `StructProperty` reaches `Ubel.cpp:2022`. The in-process callers (`:4026`, `:4215`) DO gate; only the pipe boundary does not.

5. SITES THAT LOOK LIKE THE DEFECT AND ARE NOT — do not "fix" these:
   - `dll/src/Ubel.cpp:2563` (`ReadStructArrayElements`): StructProperty is pre-branched at `:2475-2476`, never reaches InterpretValue.
   - `dll/src/Ubel.cpp:5410` (TOptional inner): pre-branched at `:5392-5398`.
   - `dll/src/Ubel.cpp:436` (`kScanBegin = 0x08; // skip the vtable at +0x00`): a DIFFERENT and correct case — `FTextData` is a genuinely polymorphic object. Superficially the same "skip 8" and it will show up in any grep for this defect.

6. SIDE EFFECT WORTH ANTICIPATING: routing `:4448`/`:4686`/`:6280` through `WalkClass()` adds entries to `s_walkClassCache`, which audit finding U5 (`Ubel.cpp:683`) already flags as unbounded and never evicted. This fix makes U5 marginally worse — worth noting in the commit, not worth blocking on, since `:4833` already calls `WalkClass` on the same struct addresses from the WalkInstance path.

### ⚠ Residual risk — what could still be wrong, or could delete working code

FIVE RISKS, ONE OF THEM A REAL UNDER-COST IN THE PROPOSED FIX.

1. STEP 2 IS NOT A PLUMB AT THE MOST VALUABLE CALL SITE. The fix says ":5995 pass WalkClass(m.structClassAddr)'s fields (the site already knows m.structType, so plumb the addr alongside it)". I read the struct: `Aura.h:498-541` `struct PropertyMatch` has `std::string structType;   // StructProperty -> inner struct name` at `:509` and NO address for it — `classAddr`/`definingClassAddr` are the CLASS, not the UScriptStruct. `Ubel.h:28` `std::string structType;` on `FieldInfo` is likewise a name only. So there is no addr to plumb: closing `:5995` needs either (a) a new field on `Aura::PropertyMatch` populated in `Aura.cpp`, which NO test target compiles, or (b) a re-probe of `FStructProperty::Struct` off `FieldInfo::Address` the way `Ubel.cpp:4800-4827` does, including its `DynOff::FSTRUCTPROP_STRUCT +/- 16` search and `if (!found)` failure log. Both are materially more work and more risk than the fix text implies. The map/set sites (`:4448`, `:4686`) genuinely are one-liners — the estimate is right for those and wrong for Property Search.

2. THE PURE HEADER-INLINE CORE CANNOT BE THE WHOLE DECODER, so the "two code paths drift" root cause is only half closed. The existing correct loop at `:4868-4889` handles `NameProperty` via `DecodeFNameBytes` and `ObjectProperty`/`ClassProperty` via `GetName(ptr)` — both touch target memory / FNamePool and cannot live in a pure `Ubel.h` inline. A testable `InterpretStructValue` therefore covers numerics/bool only and the impure arms stay at the call site. That is exactly audit #4's 4a shape ("the report and the reality are computed by different code paths"). Mitigation: have the impure sites CALL the pure core for numeric members rather than re-implement them, and write that intent down.

3. DELETING THE BYTE-BLIND ARM IS A BEHAVIOUR CHANGE ON AN UNTESTED PATH. Step 3 ("do NOT emit f:[...]" when no layout resolves) is the right call, but `:4824-4827` logs that `FStructProperty::Struct` resolution genuinely fails on some titles, and `:4832` additionally requires `previewLimit > 0`. On those, previews that today read plausibly — and for a float-struct whose members start at +8, correctly — become `{Name}` or hex. Honest, but a visible downgrade the maintainer should be told about before it lands.

4. VERIFICATION ASYMMETRY. The pure decoder is unit-testable today: `dll/tests/dll_helpers_test.cpp:36` already has `#include "../src/Ubel.h"` and `Ubel.h` already hosts pure inline helpers (`IsSanePropertiesSize :421`, `ComputeHoles :502`, `ComputeClassHoles :541`, `NormalizeGuessedTypeToProperty :564`). But `dll/CMakeLists.txt:539-543` compiles only `tests/dll_helpers_test.cpp` + `src/Radar.cpp` + `src/Denken.cpp`, and the WIRING lives in `Ubel.cpp`/`Aura.cpp`, which nothing compiles. A green `-Target Test` after this fix proves the decoder and NOTHING about whether any caller reaches it. Given the 31-batch verification backlog, it must ship with the live check attached: `Map_IntToVec3f` in DumperTest showing three components cross-checked against the row's own `hexValue`, plus a GAS title for the regression guard, plus a UE5 LWC title for the 24-byte arm.

5. TWO NUMBERS WERE REASONED, NOT RUN. The reachable size window is 9..75 bytes, not "9..72" (`floatCount = (size-8)/4 <= 16`), and the worked float half is about -4.1631 rather than -4.1638. Neither changes the verdict, but any test asserting exact expected strings must be produced by running the code, not hand-computed.

NOT A RISK BUT MUST SURVIVE INTO THE COMMIT: the fix text's own warning is correct — do NOT touch `Ubel.cpp:436` `kScanBegin = 0x08`, which is a legitimate vtable skip on the genuinely polymorphic `FTextData`.

---

## V3 — CONFIRMED_BUT_DIFFERENT

### Premises of the filed finding that are WRONG

- "Every other long round-trip snapshots CurrentAddress + Breadcrumbs.Count before its await" — FALSE. Of ~31 `await _dump.` call sites in LiveWalkerViewModel.cs, exactly TWO carry the guard: RefreshAsync (:4734-4735) and TryLoadDataTableRowsAsync (:6059/:6091). The idiom is a minority precedent, not a house rule. Most other sites are navigation commands that set the state themselves, so they are a different (last-writer-wins) case, not unguarded peers.

- "runs on the bulk lane precisely so the user can keep navigating" — MISATTRIBUTED. LaneRoutingPipeClient.cs:32-34 states find_refs_to_uobject is MUST-bulk because it builds the Aura class-metadata caches and must not run concurrently with another such command; a single serial bulk lane guarantees that. Interactive-lane responsiveness is the lane split's general property (class remarks, :15-17), not this command's reason for being there. The race conclusion survives either way.

- The filing implies the fix is a CurrentAddress comparison. That is WRONG on its own: a container drill (PopulateArrayContainerFields :1263) changes CurrentObjectName and pushes a breadcrumb while leaving CurrentAddress unchanged (CurrentAddress is assigned only at :856, :5890, :5910). An address-only guard — including the tempting `result.QueryAddress != CurrentAddress`, which is free because Fern.cpp:4124 echoes it and DumpService.cs:986 already parses it — would let the "References to Items" mislabel through unchanged.

- The filing does not mention the shared-flag half of the defect: `finally { IsLoading = false; }` (:4417), `ClearStatus()` (:2377) and `StatusText = ...` (:2399/:2406) are cross-command state that this method writes unconditionally after the await, clobbering a concurrent navigation's progress bar, status line, and the MainWindow.axaml.cs:659 auto-repeat interlock.

- The filing frames the bug as "A's list under B's header", implying the list itself is harmless. It is not purely cosmetic: the rows stay live, and OpenReferenceOwnerAsync (:2437) pre-arms `_pendingScrollFieldName` from the referring field and re-roots the walker (Breadcrumbs.Clear() at :2513) — so the user gets a real navigation into an object that references A while believing it references B.


### What the code actually does

The headline defect is real and the mechanism is exactly as described, but two of the filed supporting premises are wrong, and the correction matters because it invalidates the obvious fix.

WHAT IS TRUE. `FindReferencesAsync` (ui/UE5DumpUI/ViewModels/LiveWalkerViewModel.cs:2371-2419) takes `CurrentAddress` at :2381, awaits, and then at :2383-2386 clears and refills the `References` collection, sets `HasReferences`, and at :2398/:2404 composes `ReferencesHeader` from `CurrentObjectName` read AFTER the await. Nothing is snapshotted and nothing is compared. There is no CancellationToken passed either, although `IDumpService.FindReferencesToUObjectAsync` takes one (ui/UE5DumpUI/Core/IDumpService.cs:140-141).

THE RACE IS REAL AND WIDE. `find_refs_to_uobject` is in `LaneRoutingPipeClient.BulkCommands` (ui/UE5DumpUI/Services/LaneRoutingPipeClient.cs:46), while `walk_instance` / `read_array_elements` are not — so navigation runs on the *interactive* lane with no ordering relationship to the in-flight scan (`LaneRoutingPipeClient.SendAsync`, :119-124). The DLL-side scan has a 30-second deadline (dll/src/Aura.cpp:3215), so the window is up to 30 s. Nothing gates the UI during it: `ui/UE5DumpUI/Views/LiveWalkerPanel.axaml:510` binds `IsLoading` only to a `ProgressBar`'s `IsVisible`; there is no `IsEnabled="{Binding !IsLoading}"` anywhere in that panel. The only `IsLoading` interlock is the keyboard/mouse auto-repeat guard at ui/UE5DumpUI/Views/MainWindow.axaml.cs:659, which does not cover double-click drill-down, breadcrumb jumps, or the Parent button.

RESULT. Drill from A into B during the scan; the scan lands, `References` fills with A's referrers, and the header reads "References to B (N)". Clicking Open on a row then runs `OpenReferenceOwnerAsync` (:2437), which pre-arms `_pendingScrollFieldName` from a field that points at A and re-roots the walker there — a navigation the user believes is about B. This directly violates the design intent already written into this file at :2514-2518 ("references are about the now-current UObject, not the new one").

WHAT IS WRONG IN THE FILING. (1) "Every other long round-trip snapshots CurrentAddress + Breadcrumbs.Count" is false. Of ~31 `await _dump.` sites in this file, exactly two carry that guard: `RefreshAsync` (:4734-4735, checked at :4769/:4785/:4810/:4829) and `TryLoadDataTableRowsAsync` (:6059, :6091). The precedent exists and is even labelled "audit #7" at :6081 — but it is a minority pattern, not a house rule this one method broke. (2) The bulk-lane motivation is misstated: LaneRoutingPipeClient.cs:32-34 puts find_refs on bulk as MUST-bulk for Aura class-metadata-cache mutual exclusion, not "precisely so the user can keep navigating"; concurrency with the interactive lane is a consequence, not the reason.

WHAT THE FILING MISSED. (a) `CurrentAddress` alone is not a sufficient guard. A container drill (`NavigateToArrayContainerAsync`, :1080-1137) pushes a breadcrumb and sets `CurrentObjectName = sourceField.Name` (:1263) while leaving `CurrentAddress` untouched — `CurrentAddress` is assigned in only three places (:856, :5890, :5910), none of them the container path. So the natural one-liner `if (result.QueryAddress != CurrentAddress) return;` — tempting because `QueryAddress` is already echoed verbatim by the DLL (dll/src/Fern.cpp:4124) and already parsed (Services/DumpService.cs:986) — is a BROKEN fix: it lets the exact "References to Items" mislabel through. (b) `IsLoading` is a single shared flag: this method's `finally { IsLoading = false; }` (:4417) fires while an unrelated navigation is still in flight, hiding its progress bar and re-opening the MainWindow.axaml.cs:659 auto-repeat gate. `ClearStatus()` at :2377 and `StatusText = ...` at :2399/:2406 likewise stomp a concurrent navigation's status line. Secondary, but part of the same fix.

CONTRAST WORTH COPYING. `RelatedObjectsViewModel.LoadAsync` (ui/UE5DumpUI/ViewModels/RelatedObjectsViewModel.cs:72-110) is the same shape on the same bulk lane and is structurally immune: it snapshots `addr` at :74 and derives its header (`QueryClassName`) from the RESULT payload's "Self" row at :93-94, never from post-await VM state.

### Evidence

- ui/UE5DumpUI/ViewModels/LiveWalkerViewModel.cs:2381 — `            var result = await _dump.FindReferencesToUObjectAsync(CurrentAddress);` (no ct, no snapshot taken)

- ui/UE5DumpUI/ViewModels/LiveWalkerViewModel.cs:2383-2386 — `            References.Clear();` / `            foreach (var r in result.References)` / `                References.Add(r);` / `            HasReferences = References.Count > 0;` (unconditional refill after the await)

- ui/UE5DumpUI/ViewModels/LiveWalkerViewModel.cs:2398 — `                ReferencesHeader = $"References to {CurrentObjectName} ({References.Count})" + scanSuffix;` (post-await read of live VM state)

- ui/UE5DumpUI/ViewModels/LiveWalkerViewModel.cs:2404 — `                ReferencesHeader = $"References to {CurrentObjectName} (none found)" + scanSuffix;`

- ui/UE5DumpUI/Services/LaneRoutingPipeClient.cs:46 — `        "find_instances", "find_by_address", "find_refs_to_uobject", "find_path_from_gworld",` (bulk lane; walk_instance is absent from the set)

- ui/UE5DumpUI/Services/LaneRoutingPipeClient.cs:32-34 — `    // MUST-bulk (build the Aura class-metadata caches → must never run` / `    // concurrently with another such command, which a single serial bulk lane` / `    // guarantees): find_by_address, find_path_from_gworld, find_refs_to_uobject,` (the real reason it is on bulk — refutes the filed motivation)

- ui/UE5DumpUI/Services/LaneRoutingPipeClient.cs:122 — `        var lane = (cmd != null && BulkCommands.Contains(cmd)) ? _bulk : _interactive;`

- dll/src/Aura.cpp:3215 — `    constexpr int kDeadlineMs = 30000;` in `FindReferencesToUObject` (dll/src/Aura.cpp:3196) — the race window is up to 30 s

- ui/UE5DumpUI/Views/LiveWalkerPanel.axaml:510 — `                 IsVisible="{Binding IsLoading}" Height="2" Margin="0,0,0,4"/>` — IsLoading drives a ProgressBar only; grep for `IsEnabled` in that file returns no `!IsLoading` binding, so navigation is not gated

- ui/UE5DumpUI/Views/MainWindow.axaml.cs:659 — `        if (walker.IsLoading) return true;` — the only IsLoading interlock, and it covers Alt+←/→ and mouse 4/5 only, not drill-down clicks

- ui/UE5DumpUI/ViewModels/LiveWalkerViewModel.cs:2514-2518 — `            // Stale references panel from a previous lookup target shouldn't` / `            // hang around when we navigate elsewhere — references are` / `            // about the now-current UObject, not the new one.` / `            References.Clear();` / `            HasReferences = false;` — the design intent this defect violates

- ui/UE5DumpUI/ViewModels/LiveWalkerViewModel.cs:4732-4735 — `        // Snapshot address before async call — if user navigates while we're awaiting,` / `        // CurrentAddress will differ and we discard the stale result.` / `        var addressAtStart = CurrentAddress;` / `        var breadcrumbCountAtStart = Breadcrumbs.Count;` — the in-repo idiom (one of only two sites)

- ui/UE5DumpUI/ViewModels/LiveWalkerViewModel.cs:6091 — `                if (CurrentAddress != dataTableAddr || Breadcrumbs.Count != bcAtStart)` — the second and only other site (labelled "audit #7" at :6081); refutes "every other long round-trip"

- ui/UE5DumpUI/ViewModels/LiveWalkerViewModel.cs:1263 — `        CurrentObjectName = sourceField.Name;` in `PopulateArrayContainerFields` — a container drill renames the view WITHOUT touching CurrentAddress (assigned only at :856, :5890, :5910), so an address-only guard cannot see it

- ui/UE5DumpUI/ViewModels/LiveWalkerViewModel.cs:1115-1124 — `        Breadcrumbs.Add(new BreadcrumbItem` / `            Address = parentAddr,` … `            IsContainerView = true,` — the container drill DOES change Breadcrumbs.Count, which is why the two-part guard is required

- dll/src/Fern.cpp:4124 — `            data["query_addr"] = addrStr;` (echoed verbatim) and ui/UE5DumpUI/Services/DumpService.cs:986 — `            QueryAddress = res["query_addr"]?.GetValue<string>() ?? addr,` — a free but INSUFFICIENT guard, already parsed and never read

- ui/UE5DumpUI/ViewModels/RelatedObjectsViewModel.cs:74 / :93-94 — `        var addr = (TargetAddress ?? "").Trim();` … `                if (QueryClassName.Length == 0 && r.Relation == "Self")` / `                    QueryClassName = r.ClassName;` — the same-lane sibling that gets it right by labelling from the payload


### Fix shape

The filed diagnosis is right; the fix implied by it (an address comparison) is BROKEN — see premises_corrected. Correct repair:

1. In `FindReferencesAsync`, before the try block, snapshot both components exactly as `RefreshAsync` does at :4734-4735:
   `var addressAtStart = CurrentAddress; var breadcrumbCountAtStart = Breadcrumbs.Count;`
   Pass `addressAtStart` (not `CurrentAddress`) to `_dump.FindReferencesToUObjectAsync`.

2. Immediately after the await, before `References.Clear()` at :2383:
   `if (CurrentAddress != addressAtStart || Breadcrumbs.Count != breadcrumbCountAtStart) { _log.Info($"FindReferences superseded for {addressAtStart} (now {CurrentAddress}, bc {Breadcrumbs.Count}/{breadcrumbCountAtStart}) — dropped"); return; }`
   Dropping (rather than relabelling with a snapshotted name) is the choice consistent with the intent already written at :2514-2518. Because the discarded work can be up to 30 s (Aura.cpp:3215), do NOT drop silently to the log only — the `finally` will clear `IsLoading` and the user will see nothing happen. Set a one-line StatusText saying the scan for the previous object was discarded because the view moved.

3. Same-method secondary: the `finally { IsLoading = false; }` at :4417 should not clear a flag it may not own. The minimal honest change is to only clear it when the guard did not trip AND no newer operation set it; the cleaner change is to stop routing this decoration command through the shared `IsLoading`/`StatusText` at all and give the References panel its own busy flag (mirroring `RelatedObjectsViewModel.IsBusy`). Either is acceptable; leaving it as-is means the fix closes the label race but leaves the progress-bar/auto-repeat clobber.

Note on a stronger form: the two-part guard has a residual hole shared with the two existing sites — Back, then drill into a DIFFERENT sibling container at the same depth from the same parent, yields the same `CurrentAddress` and the same `Breadcrumbs.Count` with a different `CurrentObjectName`. A monotonic `_navEpoch` bumped wherever the spine or display node changes, compared after every await, would close it here AND tighten :4769/:4785/:4810/:4829 and :6091. That is a larger change; recommend it as a follow-up rather than folding it in, so this fix stays reviewable against the existing idiom.

Do NOT "fix" this by clearing `References` on every navigation. Only three sites clear it today (:815, :2517, :2821 — all re-roots); ordinary drill/Back leaves a stale-but-correctly-self-labelled panel up, which is a separate, pre-existing, lower-severity UX question and a behaviour change, not part of this defect.

### Testability

Testable in C#, in `ui/UE5DumpUI.Tests`, with one word of test-infra change. `LiveWalkerViewModel` is already driven directly by six existing suites (LiveWalkerForwardNavTests.cs, LiveWalkerMultiSelectTests.cs, LiveWalkerSearchNavTests.cs, LiveWalkerSearchHighlightTests.cs, LiveWalkerExportCancelTests.cs, LiveWalkerFocusTests.cs) using the pattern at LiveWalkerForwardNavTests.cs:25-27: `new LiveWalkerViewModel(new StubDumpService(), new MockLoggingService(), new MockPlatformService(Path.GetTempPath()))`. `CurrentObjectName` / `CurrentAddress` / `IsLoading` are public settable `[ObservableProperty]` (LiveWalkerViewModel.cs:94, :102, :104) and `Breadcrumbs` is a public ObservableCollection, so the race is drivable from a test with no game and no pipe.

The one blocker: `StubDumpService.FindReferencesToUObjectAsync` at ui/UE5DumpUI.Tests/CsxExportServiceTests.cs:102 is declared non-virtual and throws NotImplementedException — `public Task<FindReferencesResult> FindReferencesToUObjectAsync(string addr, int maxResults = 32, CancellationToken ct = default) => throw new NotImplementedException();`. Many neighbours in the same stub (`WalkWorldAsync` :91, `FindInstancesAsync` :97, `GetRelatedObjectsAsync` :103) are already `virtual`; adding `virtual` here matches the file's own convention and unblocks the test.

Shape of the pinning test (a deterministic race, no timing):
  - derived stub overrides `FindReferencesToUObjectAsync` to return a `TaskCompletionSource<FindReferencesResult>.Task` the test controls;
  - seed `vm.CurrentAddress = "0xA"`, one breadcrumb, `vm.CurrentObjectName = "ObjectA"`;
  - `var t = vm.FindReferencesCommand.ExecuteAsync(null);`
  - simulate the drill: `vm.Breadcrumbs.Add(...); vm.CurrentObjectName = "Items";` (container case — deliberately leave `CurrentAddress` unchanged, which is what makes this the negative control that an address-only fix FAILS);
  - `tcs.SetResult(new FindReferencesResult { QueryAddress = "0xA", References = { one match } }); await t;`
  - assert `vm.References.Count == 0` and `vm.ReferencesHeader` does not contain "Items".
  Add the mirror case (`CurrentAddress` changed to "0xB", breadcrumb count changed) and a pass-through case (nothing changed → panel fills) so the guard is shown able to fail in both directions.

No C++ involvement: dll/src/Aura.cpp is only the source of the 30 s window, and no test target compiles Aura.cpp anyway. Nothing here needs the DLL, a game, or the live-verification backlog — this fix can be fully pinned by `dotnet test ui/UE5DumpUI.Tests/UE5DumpUI.Tests.csproj`, which is the argument for doing it rather than adding a 26th unverified item.

### Blast radius

Grepped `await _dump\.` across LiveWalkerViewModel.cs (31 sites) and checked each for the post-await guard.

DIRECT FIX SCOPE — one method, `FindReferencesAsync` (LiveWalkerViewModel.cs:2371-2419). No callers to update: it is only reachable as `FindReferencesCommand` from LiveWalkerPanel.axaml:62. `FindReferencesToUObjectAsync` has exactly one production call site (this one) plus the interface decl (Core/IDumpService.cs:140) and the stub (Tests/CsxExportServiceTests.cs:102).

SAME-DEFECT SIBLING, LOWER CONFIDENCE — `LoadFunctionsAsync` (LiveWalkerViewModel.cs:6107-6131), fired-and-forgotten from `UpdateDisplay` at :6045 with no snapshot and no guard, writing `_allFunctions` / `HasFunctions` / `Functions` after its await. It is the structural twin of `TryLoadDataTableRowsAsync`, which sits two lines away at :6052 and DOES guard. It is largely rescued by ordering rather than by design: `walk_functions` is NOT in `BulkCommands`, so it shares the interactive lane with `walk_instance` and completes FIFO relative to the next navigation's walk. I did not find a construction that breaks it, so I am not filing it — but it is the site to re-check if the `_navEpoch` follow-up is ever done, since it would be covered for free.

EXISTING GUARDS THAT SHARE THE RESIDUAL HOLE — `RefreshAsync` checks at :4769, :4785, :4810, :4829 and `TryLoadDataTableRowsAsync` at :6091 all use `address + Breadcrumbs.Count`, so all three are blind to Back-then-drill-into-a-different-sibling-container-at-the-same-depth. Not a regression introduced here; it is the reason the `_navEpoch` follow-up is worth recording.

CHECKED AND CLEAN — `RelatedObjectsViewModel.LoadAsync` (RelatedObjectsViewModel.cs:72-110) is the same shape on the same bulk lane (`get_related_objects` is in BulkCommands, LaneRoutingPipeClient.cs:50) and is immune: it snapshots `addr` at :74 and derives its header from the result payload's "Self" row at :93-94. `DetectCurrentTargetAsync` (:177) is in the same VM and follows the same discipline.

DELIBERATELY OUT OF SCOPE — `References` is cleared at only three sites (:815 StartFromGameEngine, :2517 NavigateToAddressAsync, :2821 BuildBreadcrumbSpineFromPath), all re-roots. Ordinary drill-down / Back / Parent / breadcrumb-jump leave the panel up. That is a pre-existing staleness with a CORRECT self-label (the header was baked with the old object's name at scan time), materially different from this race, and touching it is a UX change. The V3 fix must not be widened into it.

### ⚠ Residual risk — what could still be wrong, or could delete working code

1. SCOPE CREEP IS THE MAIN RISK. The container-view mislabel I found is real but is a second defect sharing the same two lines. Folding an owner-name capture into four container-drill methods (NavigateToArrayContainerAsync :1080, NavigateToMapContainer :1139, NavigateToSetContainer :1160, NavigateToDataTableContainer :1180) plus BreadcrumbItem is a much wider diff than the two-line guard and touches paths covered by the Back/Forward/refresh logic at :4761-4800 (RefreshAsync re-enters RepopulateContainerView). If it is folded in, it must be its own commit with its own tests, or the reviewable-against-the-idiom argument for the guard is lost.

2. THE GUARD ITSELF CAN STILL BE WRONG IN THE PERMISSIVE DIRECTION. (address, Breadcrumbs.Count) does not identify what is displayed — proven by the container case, where both are unchanged while the view is a different thing. Back-then-drill-into-a-different-sibling at the same depth also slips through. So the fix reduces the failure rate; it does not close the class. Do not write a commit message or a register entry claiming the stale-panel class is closed.

3. DROP-SILENTLY IS A REGRESSION IF THE STATUS LINE IS OMITTED. The finally at :2417 clears IsLoading unconditionally, so an early return with only a _log.Info gives the user a 30-second scan that ends with the progress bar vanishing and nothing on screen — indistinguishable from the "no references found" path they would otherwise have got. StatusText is also stomped by concurrent flows (ClearStatus() at :2377, and every navigation opens with ClearStatus()), so even the status line may not survive; verify on the real UI rather than assuming the message lands.

4. NOTHING HERE DELETES WORKING CODE, but the one thing that could is over-correcting: do NOT clear References on every navigation. Only three sites clear it today (:815, :2517, :2822 — all re-roots), and ordinary drill/Back deliberately leaves a correctly-self-labelled panel up. Clearing it everywhere is a behaviour change the design comment at :2514-2518 does not authorise.

5. TEST-INFRA CHANGE HAS A SMALL BLAST RADIUS. Making CsxExportServiceTests.StubDumpService.FindReferencesToUObjectAsync virtual is safe (the base still throws NotImplementedException, so any existing test that reached it already failed), but StubDumpService is shared by many suites in the project — run the whole ui/UE5DumpUI.Tests suite, not just the new file.

6. UNVERIFIED BY ME: I did not run the build or the test suite (read-only task), and I did not confirm empirically that a user click can be delivered mid-await — that is inferred from the Avalonia message loop plus the absence of any IsEnabled gate, not measured. A negative control (revert the guard, watch the new test fail) is required before this is called verified, per working-lessons §1.

---

## V4 — BIGGER_THAN_FILED

### Premises of the filed finding that are WRONG

- The filed line number is stale and points at unrelated code. `LiveWalkerViewModel.cs:5575` is `ScrollToFirstSearchMatch?.Invoke();` inside `ApplySearch`. `NavigateToAsync` is at :5615 (Add at :5621); `GoBackAsync` is at :1986 (truncation at :2091-2092).

- "no shared re-entrancy gate" is not literally true. `Views/MainWindow.axaml.cs:659` already gates the Alt+left/right and mouse-button-4/5 Back/Forward route on `walker.IsLoading`. The hole is specifically the Back BUTTON, which has no `IsEnabled` binding (LiveWalkerPanel.axaml:37-40).

- "separate AsyncRelayCommands" is the right observation for the wrong reason if read as "AsyncRelayCommand blocks nothing". On 8.4.2 the default is `AllowConcurrentExecutions=false`, so `CanExecute` goes false while running and a bound Button self-disables — drill-vs-drill and Back-vs-Back are ALREADY blocked. Only cross-command pairs race. The repo measured this in build 3038 (ClassStructViewModelConcurrencyTests.cs:25-28).

- "a drill-down that started first" covers only one of two orderings. Back clicked FIRST corrupts synchronously — :972, :976 and :986-987 all read/write `Breadcrumbs[^1]` at click time, after Back has already popped it — with no await involved in the drill at all. A post-await guard does not fix that half.

- "leaving a leaf whose FieldOffset is relative to a parent no longer in the list" understates the outcome. Because both walks ride one FIFO lane and the drill was issued first, Back's `UpdateDisplay` lands LAST: `CurrentAddress`/`Fields` describe the BACK target while `Breadcrumbs[^1]` describes the DRILL target. The chain is not merely mis-based, it is a chain to one object carrying another object's field table.

- "NavigateToAsync" is not the only affected site. The struct branch of `NavigateToFieldAsync` (:1007, after awaits at :997-998) and `NavigateToArrayContainerAsync` (:1115, after the await at :1104, with `parentAddr = CurrentAddress` captured at :1088) have the identical shape. Conversely `NavigateToMapContainer` (:1145), `NavigateToSetContainer` (:1165), `NavigateToDataTableContainer` (:1184), `GoToParentAsync` (:2315) and the breadcrumb jump (:1782-1787) all mutate synchronously and are NOT affected — a blanket gate would change their currently-correct behaviour for nothing.

- "a silently wrong CE pointer chain" is right for `ExportCeXmlAsync` (:3920, no landing check) but wrong for the AA-script path: `BuildAaScript` already requires `AddressesEqual(spine[^1].Address, CurrentAddress)` at :4587 and degrades to the hardcoded-absolute fallback with the note "GWorld path not forward-walkable".

- The filing misses two consequences entirely: the late `Breadcrumbs.Add` trips the CollectionChanged hook (:676-679) and silently wipes the forward step Back just pushed at :2093; and the corrupt spine is persisted by `ToPersisted` (:3876-3881) into `Bookmarks\bookmarks.<hash>.json`, so it survives a restart.


### What the code actually does

The core mechanism is real and I reproduced it by reading, but the filed text describes only one of two orderings, names only one of three affected call sites, and understates the damage.

WHAT IS TRUE. `GoBackAsync` (`ui/UE5DumpUI/ViewModels/LiveWalkerViewModel.cs:1986`) truncates the spine SYNCHRONOUSLY, before its first await: `CaptureCrumbViewState(); var removed = Breadcrumbs[^1]; Breadcrumbs.RemoveAt(...)` at :2090-2092, and only then awaits `_dump.WalkInstanceAsync` at :2130. `NavigateToAsync` (`:5615`, NOT :5575) does the opposite: it awaits at :5617-5618 and appends at :5621-5629 with `FieldOffset = fieldOffset`, where `fieldOffset` was computed by the caller against `Breadcrumbs[^1]` as it stood BEFORE the await (`NavigateToFieldAsync:986-987`). So a drill whose walk is in flight resumes onto whatever spine exists at that moment and appends a leaf whose offset is relative to a parent that is no longer `Breadcrumbs[^2]`. Nothing serializes the two: they are distinct generated `AsyncRelayCommand` instances, the Back button carries no `IsEnabled` binding at all (`Views/LiveWalkerPanel.axaml:37-40`), and the panel's only `IsLoading` consumer is a 2px ProgressBar (`:509-510`).

WHERE THE FILING IS TOO NARROW.

(1) THE REVERSE ORDERING NEEDS NO INTERLEAVE. If Back is clicked FIRST, its truncation is already committed when the user clicks a still-rendered `→` button (the grid is not disabled and rows stay realised — the file says so itself at :1920-1921). `NavigateToFieldAsync` then writes `Breadcrumbs[^1].ScrollHintFieldName = field.Name` (:972), calls `CaptureCrumbViewState(field)` (:976) and computes `navOffset` from `Breadcrumbs[^1]` (:986-987) — all SYNCHRONOUSLY, all against the wrong (already-popped-to) crumb. A guard placed only after `NavigateToAsync`'s await does not touch this half.

(2) THE GRID AND THE SPINE DESYNC, WHICH IS WORSE THAN A WRONG OFFSET. Both walks ride one FIFO pipe lane (`Services/PipeClient.cs:21` `_pending` id map; `_writeLock` at :23 only serializes the WRITE, so two requests are genuinely in flight). The drill's request is issued first, so it is answered first — Back's `UpdateDisplay` at :2132 lands LAST and sets `CurrentAddress = result.Address` (:5910) plus `Fields` to the BACK target, while `Breadcrumbs[^1]` is the DRILL target. `ExportCeXmlAsync` (:3920) then mixes `Breadcrumbs` (ending at C) with `Fields` (A's layout) and has no landing check — that is the wrong chain the filing predicts, but with a second object's field table hung off it. `BuildAaScript` is partly self-protected (`AddressesEqual(spine[^1].Address, CurrentAddress)` at :4587) and merely degrades to the hardcoded-address note.

(3) IT ALSO WIPES THE FORWARD HISTORY. The late `Breadcrumbs.Add` reaches the CollectionChanged hook (:662-680) with `_replayingHistory == false`, so `ClearForwardStack()` fires and destroys the forward step `PushForward(removed)` (:2093) had just created. Forward greys out immediately after Back — a user-visible tell of the corruption.

(4) THE CORRUPT SPINE IS PERSISTED TO DISK. `ToPersisted` (:3868-3886) copies `FieldOffset` into `Bookmarks\bookmarks.<hash>.json`; `HydrateSlot` (:3895) reads it back. A spine corrupted by one mis-timed click outlives the session if the user saves a bookmark on it.

WHAT THE FILING GETS SLIGHTLY WRONG ABOUT THE GATE. There IS a partial cross-command gate, on the keyboard/mouse route only: `Views/MainWindow.axaml.cs:659` `if (walker.IsLoading) return true;` before dispatching `GoBackCommand`/`GoForwardCommand`. Alt+left/right and mouse-4/5 are already immune; the Back BUTTON is the hole. And the toolkit nuance matters: this repo already MEASURED (build 3038, recorded verbatim in `ui/UE5DumpUI.Tests/ClassStructViewModelConcurrencyTests.cs:25-28`) that CommunityToolkit.Mvvm 8.4.2's default `AllowConcurrentExecutions=false` makes a bound Button self-disable but does not block `ExecuteAsync`. Consequence: drill-vs-drill and Back-vs-Back are already button-blocked; only CROSS-command pairs race. This is the same root cause as audit #5 AE2/AE3, already fixed in `ClassStructViewModel` with a gesture-time ticket.

### Evidence

- ui/UE5DumpUI/ViewModels/LiveWalkerViewModel.cs:2090-2094 — Back truncates BEFORE any await: `CaptureCrumbViewState();` / `var removed = Breadcrumbs[^1];` / `Breadcrumbs.RemoveAt(Breadcrumbs.Count - 1);` / `PushForward(removed);` / `var prev = Breadcrumbs[^1];`

- ui/UE5DumpUI/ViewModels/LiveWalkerViewModel.cs:2130 — Back's first await comes only afterwards: `var result = await _dump.WalkInstanceAsync(prev.Address, classAddr, arrayLimit: ArrayLimit, previewLimit: PreviewLimit, fillGaps: FillGaps);`

- ui/UE5DumpUI/ViewModels/LiveWalkerViewModel.cs:5615-5629 — the drill appends AFTER two awaits with no re-check of the spine: `private async Task NavigateToAsync(string addr, string label, int fieldOffset, string fieldName, bool isPointer)` / `var result = await _dump.WalkInstanceAsync(addr, ...);` / `result = await AutoFillGapsRetryAsync(result, addr);` / … / `Breadcrumbs.Add(new BreadcrumbItem` / `Address = addr,` / … / `FieldOffset = fieldOffset,`

- ui/UE5DumpUI/ViewModels/LiveWalkerViewModel.cs:986-987 — the offset's base is read at click time, from a crumb Back may pop before the Add: `int navOffset = field.Offset + MapValueDrillOffset(` / `Breadcrumbs.Count > 0 ? Breadcrumbs[^1] : null);`

- ui/UE5DumpUI/ViewModels/LiveWalkerViewModel.cs:971-976 — the SYNCHRONOUS half (reverse ordering, needs no interleave): `if (Breadcrumbs.Count > 0)` / `Breadcrumbs[^1].ScrollHintFieldName = field.Name;` / … / `CaptureCrumbViewState(field);`

- ui/UE5DumpUI/Views/LiveWalkerPanel.axaml:37-40 — the Back button is unconditionally clickable: `<Button Content="{StaticResource str.LiveWalker.Back}"` / `Command="{Binding GoBackCommand}"` / `ToolTip.Tip="{StaticResource str.Tip.LiveWalker.Back}"` / `Padding="8,4"/>` — no IsEnabled at all

- ui/UE5DumpUI/Views/LiveWalkerPanel.axaml:509-510 — IsLoading's ONLY use in this panel: `<ProgressBar DockPanel.Dock="Top" IsIndeterminate="True"` / `IsVisible="{Binding IsLoading}" Height="2" Margin="0,0,0,4"/>`

- ui/UE5DumpUI/ViewModels/LiveWalkerViewModel.cs:1920-1921 — the file itself states the grid stays interactive during a load: `/// repopulated. (IsLoading is safe to have flipped: it only shows a 2px` / `/// ProgressBar, the DataGrid rows stay realised.)`

- ui/UE5DumpUI/Views/MainWindow.axaml.cs:659-662 — a partial gate that covers ONLY the keyboard/mouse route: `if (walker.IsLoading) return true;` / `var command = action == NavShortcut.Back ? walker.GoBackCommand : walker.GoForwardCommand;` / `if (command.CanExecute(null)) command.Execute(null);`

- ui/UE5DumpUI.Tests/ClassStructViewModelConcurrencyTests.cs:25-28 — the toolkit fact this repo already measured: `///   * <c>AsyncRelayCommand</c> does NOT block re-entrancy: <c>CanExecute</c> goes` / `///     false while running, so a bound Button self-disables, but <c>ExecuteAsync</c>` / `///     runs anyway. Measured on 8.4.2 in build 3038`

- ui/UE5DumpUI/ViewModels/LiveWalkerViewModel.cs:676-679 — the late Add also wipes the forward step Back just pushed: `if (_replayingHistory) return;` / `if (e.Action is NotifyCollectionChangedAction.Add` / `             or NotifyCollectionChangedAction.Reset)` / `ClearForwardStack();`

- ui/UE5DumpUI/ViewModels/LiveWalkerViewModel.cs:4769 — the guard idiom that already exists in this very file (RefreshAsync): `if (CurrentAddress != addressAtStart || Breadcrumbs.Count != breadcrumbCountAtStart) return;`

- ui/UE5DumpUI/ViewModels/LiveWalkerViewModel.cs:4586-4587 — the AA-script path is partly self-protected, so it degrades rather than emitting the bad chain: `&& spine.Skip(1).All(bc => bc.FieldOffset >= 0)` / `&& AddressesEqual(spine[^1].Address, CurrentAddress);`

- ui/UE5DumpUI/ViewModels/LiveWalkerViewModel.cs:1088 + 1115-1119 — a second append-after-await site the filing does not name: `var parentAddr = CurrentAddress;` … `var result = await _dump.ReadArrayElementsAsync(` … `Breadcrumbs.Add(new BreadcrumbItem` / `Address = parentAddr,` / … / `FieldOffset = field.Offset,`

- ui/UE5DumpUI/ViewModels/LiveWalkerViewModel.cs:997-1012 — a third: the struct branch appends after two awaits: `var result = await _dump.WalkInstanceAsync(field.StructDataAddr, ...);` / `result = await AutoFillGapsRetryAsync(result, field.StructDataAddr, field.StructClassAddr);` … `Breadcrumbs.Add(new BreadcrumbItem` … `FieldOffset = navOffset,`

- ui/UE5DumpUI/ViewModels/LiveWalkerViewModel.cs:3876-3881 — the corrupt spine is persistable to disk: `Breadcrumbs = slot.SavedBreadcrumbs.Select(bc => new PersistedCrumb` / … / `FieldOffset = bc.FieldOffset,`

- ui/UE5DumpUI/Services/PipeClient.cs:21-23 — two walks really can be in flight (id-keyed pending map; the lock covers only the write): `private readonly ConcurrentDictionary<int, TaskCompletionSource<JsonObject>> _pending = new();` / … / `private readonly SemaphoreSlim _writeLock = new(1, 1);`


### Fix shape

The right idiom already exists in this file, but the obvious application of it is BROKEN in three ways. Say all three out loud before anyone writes the patch.

THE IDIOM. `RefreshAsync` guards every long round-trip with `if (CurrentAddress != addressAtStart || Breadcrumbs.Count != breadcrumbCountAtStart) return;` (:4769, :4785, :4810, :4829, and again at :6091).

WHY COPYING IT LITERALLY IS WRONG.

(a) `Breadcrumbs.Count` IS NOT AN IDENTITY. Back-then-Forward, or Back-then-a-different-drill, restores the count while changing the parent — the guard passes and the corruption lands anyway. Capture the parent crumb OBJECT and compare by reference: `var parentAtStart = Breadcrumbs.LastOrDefault();` before the await, then `if (!ReferenceEquals(Breadcrumbs.LastOrDefault(), parentAtStart)) return;` before the Add. Crumbs are re-used by identity across Back/Forward (LiveWalkerForwardNavTests asserts `Assert.Same(leaf, vm.Breadcrumbs[1])`), so reference equality is exactly the right test here.

(b) A POST-AWAIT GUARD ONLY FIXES HALF THE DEFECT. The reverse ordering (Back first) commits its damage synchronously at :972, :976 and :986-987. The parent must be captured at GESTURE ENTRY in `NavigateToFieldAsync` / `NavigateToContainerAsync` and threaded into `NavigateToAsync` — which is precisely the lesson audit #5 AE2 already paid for and wrote down in dev-log.md: "The ticket is claimed at GESTURE time in `OnObjectSelected`, not inside `LoadClassAsync`. Claimed in the command it would invert the fix." Do not re-learn it here.

(c) A SILENT `return` LOSES THE USER'S CLICK WITH NO EXPLANATION. This panel's house style is honest degrade (`GoForwardAsync:2250` sets `StatusText = "Forward: object changed — selection not restored"`). Set a status line, and remember that `ErrorMessage` is not bound by this panel (that is finding V7) — it must be `StatusText`.

ALSO FIX, IN THE SAME PASS. Two commands share one `IsLoading` flag and each clears it in its own `finally` (:1028, :2142), so whichever finishes first turns the spinner off while the other is still running. Whatever guard lands should not leave that.

CHEAP BELT, NOT A SUBSTITUTE. Adding `IsEnabled="{Binding !IsLoading}"` to the Back / Forward / Parent buttons (LiveWalkerPanel.axaml:37, :43, :48) mirrors what `MainWindow.RunNavShortcut` already does for the keyboard route and closes the exact named pair for one line. It is NOT sufficient alone — the Go box, bookmark load, and every cross-tab "Open in Live Walker" handoff also re-root through `NavigateToAddressAsync` (:2513 Clear, :2530 async Add) — so ship it alongside the identity guard, not instead of it.

DO NOT introduce a single global nav mutex. `GoToParentAsync` and `NavigateToBreadcrumbAsync` mutate synchronously and are correct today; serializing them buys nothing and changes behaviour that is not broken.

### Testability

Fully and deterministically testable in C#, with no prerequisite change — unlike AE2, which had to add `virtual` to a stub method first. No C++ involvement, so none of the `dll_helpers_test` / `Aura.cpp`-is-uncompiled constraints apply.

THE RIG ALREADY EXISTS, TWICE OVER.
- `ui/UE5DumpUI.Tests/CsxExportServiceTests.cs:27` already declares `public virtual Task<InstanceWalkResult> WalkInstanceAsync(...)` on `StubDumpService`, so a sub-stub can gate it on a `TaskCompletionSource` today.
- `ui/UE5DumpUI.Tests/ClassStructViewModelConcurrencyTests.cs` is the direct template for the gating pattern, including the trap it documents at :64-67: `TaskCreationOptions.RunContinuationsAsynchronously` is REQUIRED, because without it `SetResult` resumes the continuation inline on the releasing thread and serialises the very interleaving the test exists to produce.
- `ui/UE5DumpUI.Tests/LiveWalkerForwardNavTests.cs:25-39` already builds the VM from three mocks (`StubDumpService`, `MockLoggingService`, `MockPlatformService`) with no dispatcher, and already drives `GoBackCommand.ExecuteAsync(null)` directly. `AsyncRelayCommand.ExecuteAsync` bypasses `CanExecute`, so a test can force both orderings that a real button would partly block.

THE TWO TESTS TO WRITE.
1. Drill-first: gate the child walk, fire `_ = vm.NavigateToFieldCommand.ExecuteAsync(field)`, `await vm.GoBackCommand.ExecuteAsync(null)`, release the gate, await both. Assert `Breadcrumbs` did not gain a crumb whose `FieldOffset` is based on the popped parent — and assert `CanGoForward` is still true, which pins consequence (3) as well.
2. Back-first: gate BACK's walk, fire `_ = vm.GoBackCommand.ExecuteAsync(null)`, then `await vm.NavigateToFieldCommand.ExecuteAsync(field)` while it is in flight, and assert `ScrollHintFieldName` / the captured view state did not land on the wrong crumb. This one goes red against a fix that only guards the post-await Add — it is the negative control that proves the gesture-time half was actually done.

NEGATIVE CONTROL IS AVAILABLE AND MANDATORY per working-lessons §1: revert the identity guard alone and test 1 must go red; revert the gesture-time capture alone and test 2 must go red. Gating must be OPT-IN by address (the `ClassStructViewModelConcurrencyTests` note at :41-47 says why: an ungated address resolves instantly so the unfixed run fails on a clean assertion instead of hanging on a gate nobody releases).

CAVEAT: `Breadcrumbs` is an `ObservableCollection` and the test harness has no `SynchronizationContext`, so continuations land on the thread pool. That is fine for a VM-only test with no bindings — the existing forward-nav tests already rely on it — but the test must not assert from a second thread concurrently.

### Blast radius

SIBLING GREP RUN, NOT GUESSED. `grep -n "Breadcrumbs.Add" ui/UE5DumpUI/ViewModels/LiveWalkerViewModel.cs` yields 10 sites; the affected set is exactly those that follow an `await` inside the same method.

AFFECTED — same defect, must be fixed together:
- `:5621` `NavigateToAsync` — the filed site. Three callers inherit an in-method fix: `NavigateToFieldAsync:992`, `StartFromGameEngineAsync:817` (preceded by `Breadcrumbs.Clear()` at :814, so the window is open across the walk), `NavigateToAddressAsync:2530` (preceded by `Breadcrumbs.Clear()` at :2513). Each caller ALSO needs the gesture-time capture separately.
- `:1007` `NavigateToFieldAsync` struct branch — appends after the awaits at :997-998, with `navOffset` from :986-987.
- `:1115` `NavigateToArrayContainerAsync` — appends after the await at :1104, and additionally stamps `Address = parentAddr` captured pre-await at :1088, so BOTH the address and the offset can be stale.

NOT AFFECTED — verified synchronous, do not touch:
- `:1145` `NavigateToMapContainer`, `:1165` `NavigateToSetContainer`, `:1184` `NavigateToDataTableContainer` — no await between read and Add.
- `:2315` `GoToParentAsync` — Adds BEFORE its await at :2324.
- `:1782-1787` `NavigateToBreadcrumbAsync` — truncates before its awaits.
- `:1875` (`ReplaceSpine`) and `:2201` (`GoForwardAsync`) — history replays, guarded by `_replayingHistory`.

DOWNSTREAM CONSUMERS OF A CORRUPTED SPINE (the reason severity is not LOW):
- `ExportCeXmlAsync:3920` — no landing check; emits the wrong chain. This is the one that actually ships bad output.
- `BuildAaScript:4574` — partly self-guarded at :4587; degrades to the hardcoded-address note instead.
- `CeXmlExportService.GenerateGWorldWalkedSymbolXml:1685` and `CsxExportService` — both consume `FieldOffset` per hop.
- `ToPersisted:3868` → `Bookmarks\bookmarks.<hash>.json` → `HydrateSlot:3895`. The corruption is persistable and survives a restart; that is what makes this more than a transient UI glitch.

ADJACENT REGISTER ITEM TO FIX IN THE SAME PASS: **V3** (`FindReferencesAsync`) is the same root cause with a different payload, and its filed text asserts the guard idiom already exists in this file — which I confirmed at :4769/:4785/:4810/:4829/:6091. Fixing V4 without V3 leaves the identical hole one method away.

CROSS-VIEWMODEL: audit #5 **AE2/AE3** already fixed this exact class of bug in `ClassStructViewModel` using a gesture-time monotonic ticket (`_loadGen` / `_fieldLoadId` / `_classLoadId` spelling; `InstanceFinderViewModel.LoadInstanceFieldsAsync` is cited as the one site that guards all four points). Reuse that spelling here rather than inventing a fifth. Worth one grep across `ui/UE5DumpUI/ViewModels/` for other `await` → mutate-shared-nav-state pairs before closing the group; this is now the third VM in the same family.

### ⚠ Residual risk — what could still be wrong, or could delete working code

The fix, as prescribed, can DELETE WORKING CODE in a way the re-derivation does not name. NavigateToAsync has three callers, and only ONE of them has a parent to compare against: NavigateToFieldAsync:992 (the real drill), StartFromGameEngineAsync:817, and NavigateToAddressAsync:2530 (the Go box / Find Refs owner drill / every cross-tab "Open in Live Walker" handoff). Both of the latter are RE-ROOTS that call `Breadcrumbs.Clear()` immediately before invoking it (:814 and :2513), so at the moment of the Add the collection is EMPTY by design. If the new expected-parent parameter is made `required` (per working-lessons §2.2) and threaded with a gesture-time `Breadcrumbs.LastOrDefault()`, those two callers capture the OLD spine leaf, find an empty collection at the Add, mismatch, and silently `return` — killing the Go box, Find Refs, and every cross-tab handoff outright. A `Breadcrumbs.Count` guard fails there identically. They must pass an explicit "no check" (null) sentinel, and a test must pin that. Second: ReferenceEquals is STRICTER than the count guard in a direction that over-rejects. Crumb instances are preserved across Back/Forward (PushForward:1849 stores the instance; ReplaceSpine:1873-1875 re-Adds the same objects), which is why :2276 can already use it — but HydrateSlot:3903 and the PathStepToBreadcrumbs/synthetic-container paths CONSTRUCT NEW BreadcrumbItem objects. If a bookmark load, a Locate-in-GWorld, or a spine rebuild lands while a legitimate drill is in flight, the reference test fails and the user's valid click is dropped. That fails safe (drop, not corrupt) but is exactly why the honest-degrade StatusText line is load-bearing rather than cosmetic — and note the re-derivation's reason for choosing StatusText over ErrorMessage is itself stale, since ErrorMessage is now bound at :519-522. Third: the prescribed scope is incomplete — NavigateToBreadcrumbAsync (:1782-1787 sync truncation, :1821 first await) is the same shape as Back and is a racing partner the fix section explicitly waves off as "correct today". Fourth: the two-commands-share-one-IsLoading cleanup the re-derivation bolts on (:1028 vs :2142 each clearing in their own finally) is a real second defect but is NOT covered by the identity guard; bundling it risks a negative control that cannot isolate which change fixed which test, against working-lessons §2.2's "negative-control ONE change at a time".

---
