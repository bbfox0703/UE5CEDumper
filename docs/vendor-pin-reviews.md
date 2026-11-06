# Submodule pin reviews — minhook and zydis

`vendor/minhook` and `vendor/zydis` are compiled into every DLL we inject, and our git index pins them. A bump costs a
rebuild, the test suites, an AOT publish and a live hook check, so each vendor audit asks the same question: *do the
upstream commits since our pin change anything we ship?* Until 2026-11-06 every audit answered it again from scratch,
by reading the same commits.

This page is where the answer lives. For each submodule it records the last upstream commit reviewed, the decision, the
fact that decided it, how that fact was measured, and what would reopen the question. The reviewed SHAs are also in
[`tools/vendor_pin_reviews.json`](../tools/vendor_pin_reviews.json). `sync_tools.ps1` reads that file and prints, under
each submodule's behind-count, how many upstream commits are **newer than the last review**.

## How to run the next review

1. Run `.\sync_tools.ps1`. Find the line `N after the last review (<sha>, <date>: <verdict>)` under each submodule.
2. **0 means nothing is new since the review below.** The decision stands; do not re-read the older commits.
3. More than 0: read only `git -C vendor/<name> log <reviewed>..origin/master`.
   - For zydis, run the rig on the new tip:
     `py tools/verify/zydis_bump_diff/zydis_bump_diff.py <x64 game or fixture exe>... --candidate origin/master`.
   - For minhook, re-check the commits against the trampoline model (§ minhook).
4. Add a dated entry under the submodule below, newest first, and move the SHA in `tools/vendor_pin_reviews.json`.
   Do not edit an older entry: it is the record of what was true then.

---

## zydis

The pin is `a95bb71` (2026-07-27). Its header says version 5.0.0, but `git describe` says `v4.0.0-121` because upstream
never tagged a v5 (`sync_tools.ps1` prints the header version for this reason). Denken (native UFunction disassembly)
is the only Zydis user in the DLL.

### 2026-11-06 — reviewed up to `fcc63c635889` (origin/master, 2026-11-03): **HOLD**

**The deciding fact:** the real `dll/src/Denken.cpp` produces **byte-identical output** linked against `a95bb71` and
against `fcc63c6`. This was measured from every `.pdata` function start of three real UE x64 binaries: 867,714 starts
and 44,456,042 decoded instructions, compared by SHA-256. A positive control proves the rig sees a difference when one
exists.

Several of the 13 commits do fix wrong decoder output. Denken reads none of the fields they change. A bump would add
about 45 KB of decoder tables to every injected DLL and fix nothing Denken produces.

| Commit | What it changes | Does Denken see it? |
|---|---|---|
| `ff4f87e` | Sets `ZYDIS_ATTRIB_IS_PRIVILEGED` for every encoding (a fix) | No: Denken never reads `attributes` |
| `daaa71d` | Widens the internal ISA-set field. Register-form MOVSS (`F3 0F 10/11`) destination becomes read-write (a fix). Inserts EVEX element-size / disp8*N table rows | No: Denken tests only `actions & MASK_WRITE`, which is set in both. 0 displacement or size changes across 1.85M scaled-disp8 EVEX decodes |
| `0f44566` | 3DNow element types, and two mnemonic names (`pfsqrt`→`pfrsqrt`, `pfcpit1`→`pfrcpit1`) (a fix) | No: Denken compares only `ZYDIS_MNEMONIC_MOV`; operand sizes are unchanged |
| `efadec2`, `8a961be`, `b4ebc9a` | WBNOINVD, IBHF and PREFETCHRST become **default-on** decoder modes | Only `0F 18 /4` (reserved NOP → `prefetchrst2`) changes an operand size Denken records, and it occurs only at non-instruction offsets in the measured binaries |
| `3bca602` | Accepts VEX opcode maps 5 and 7. Rewrites the VEX decoder-tree index, which re-routes every VEX decode | Maps 1-3: 0 changes, checked exhaustively (VEX2 16.7M, VEX3 67.1M, XOP 12.6M decodes). Maps 5/7: bytes that failed now decode (new ISA only) |
| `289ddf3`, `304b090`, `3de879c`, `8ca5588`, `47ce083`, `fcc63c6` | New ISA: AVX10.2, AVX10_V2_AUX, VEX CMPccXADD, ACE + BSR0, AMX, USER_MSR / MSR_IMM | Only as FAIL→OK at non-instruction offsets. Shipped game code does not contain them |

Other facts, all checked:

- The nested zycore gitlink is `75a36c45` at both revisions.
- `ZYDIS_VERSION` and the CMakeLists diff are unchanged.
- Enum values shift mid-enum (`BSR0` before `FLAGS`, `USER_MSR` after `UNCOND_BR`). That is harmless: Denken uses only
  names and is compiled with the library, and `Denken.h` exposes no Zydis type.
- APX already decodes at the pin (APX mode is on by default there), so APX is not part of this delta.

**How it was verified.** Three readers, each followed by an adversarial verifier. The rig is
[`tools/verify/zydis_bump_diff/`](../tools/verify/zydis_bump_diff/zydis_bump_diff.py), committed so the next review
can rerun it.

- **End to end:** `denken_run` linked the real `Denken.cpp` against each revision. Output SHA-256 was identical on all
  three binaries:
  - the DumperTest58 Shipping fixture (UE 5.8, 508,094 starts);
  - UE427_3rdPerson Shipping (UE 4.27, 235,374 starts);
  - Nekopara.exe (a commercial UE game, 124,246 starts; read as a file, never run).
- **Positive control:** ahead of `mov [rcx+0x10],eax ; ret`, a VEX-map-7 `rdmsr` and a MOVRS stop Denken at the pin
  (0 instructions) and decode at the candidate (3 instructions, `@0x10` recorded).
- **Per instruction, real code:** a linear sweep of every `.pdata` range, 45.7M decodes, found 0 differences in any
  field Denken reads. The 13,390 raw differences are 13,379 MOVSS action changes, plus 11 bytes that fail at both
  revisions with a different status.
- **Per instruction, every byte offset:** 207M offsets of every executable section. Results: 0 OK→FAIL and 0 length
  changes. 440 Denken-visible differences, all at offsets that are not instruction starts: 430 new-ISA FAIL→OK and 10
  NOP→`prefetchrst2`.
- **Synthesized encodings (verifier):** about 470M decodes across the legacy, REX2, VEX2, VEX3, EVEX (X3 set
  included), XOP and 3DNow spaces. Results: 0 OK→FAIL, 0 length changes, and no Denken-visible change on any
  pre-existing ISA except the `0F 18 /4` row.
- **Compile and test:** `Denken.cpp` compiles against the candidate's headers with the repo's flags and 0 warnings.
  `dll_helpers_test` (Denken cases included), linked against a `/DNDEBUG` candidate build, passes 3754 / 0.

**Not verified:**

- The repo's own CMake/ninja DLL build against the candidate; the gitlink was never moved. The DLL size delta is
  estimated from the decoder library (+72,942 bytes) and a linked Denken exe (+45,056 bytes).
- `dll_core_test` against the candidate.
- A clang-cl-built game.
- A binary known to carry AVX-512/AVX10 dispatch paths.
- A live game's actual exec-thunk addresses. The sweep used every `.pdata` start, which is a superset of them.

**What would reopen it:**

- `sync_tools.ps1` shows commits after `fcc63c6`.
- A target game's native code uses AVX10.2, MOVRS, VEX CMPccXADD or MSR_IMM. Denken stops silently at the first
  instruction Zydis cannot decode, so such a game would show up only as short results, not as an error. Making Denken
  flag a decode-failure stop the way it flags `budgetHit` would make that visible.

**A bump, when it happens:**

- Keep zycore at zydis's own gitlink. `sync_tools.ps1` records the time a recursive update moved it to zycore master.
- Run `build.ps1 -Target DLL` and `-Target Test`, then this rig, then `-Mode Publish`.

---

## minhook

The pin is `d94c64d` (v1.3.4 + 14). Only `hde64.c`, `trampoline.c`, `hook.c` and `buffer.c` are compiled.
`dll/src/Lager.h` transcribes `buffer.c`'s block search (gate `check_lager_vendor_sync`).

The hook targets are:

- `UObject::ProcessEvent` (Stark), from a vtable slot.
- `user32!GetForegroundWindow`, `ClipCursor` and `SetCursorPos` (Grausam).
- The never-enabled holder on the game image's entry point, or an executable section start (Stark +
  Lager, [MH-PRERESERVE-PROXY]).

### 2026-11-06 — reviewed up to `8af6b4acae5a` (origin/master, 2026-09-08): **HOLD until the trampoline pre-flight ships**

Upstream's three commits are `72fca12` and `8af6b4a` (a rewrite of the HDE instruction-length decoder) and `7586d1a`
(an `IsExecutableAddress` VirtualQuery check). **No upstream release tag contains them**; the newest is v1.3.4.

**The deciding fact:** on every target we hook, the pinned and upstream trampolines are byte-identical, and Zydis
validates both. A plain bump changes nothing we ship, but it is **not** a strict improvement. Upstream rewrote the
decoder without updating `trampoline.c`, which still treats any `hs.opcode2` of `0x80-0x8F` as a conditional jump.
At `8af6b4a` the new decoder puts the VEX / EVEX / XOP / REX2-map-1 opcode in `opcode2`. So `vpmaskmovd/q`, AVX-512
`vexpand*` / `vcompress*` / `vpermb/w`, XOP `vpmacs*` and similar instructions are rewritten as a Jcc, and the
instruction is dropped from the trampoline. `xbegin` is copied with its rel32 unrelocated. The pin refuses all of these
safely (`MH_ERROR_UNSUPPORTED_FUNCTION`). Measured on real code: Avowed RVA `0x83EAFEC`, `C4 E2 5D 8C 01`, which is in
the middle of a function, not at any hook target.

The measured trade, through MinHook's real `CreateTrampolineFunction` with both decoders and Zydis as ground truth:

| | Pin `d94c64d` | Upstream `8af6b4a` |
|---|---|---|
| Our targets: 10 ProcessEvent prologues, 18 logged holder targets plus 23 entry points, Grausam's 3, all 2,760 user32/win32u exports | OK | Identical bytes, OK |
| 3,683,352 real function starts (6 DumperTest fixtures 4.23→5.8, 8 shipped games) | 1,656 refusals | 0 refusals |
| Of those refusals, caused by the instruction just **past** the 5-byte patch | 952 | — |
| Silently wrong trampoline, legacy forms (popcnt, `67`-addressing, `66`+REX.W imm, psadbw / maskmov, vmread / vmwrite) | 1,713 encodings | 0 |
| popcnt in mid-function code (103.9M instructions) | 281 corrupt | 0 |
| Silently wrong trampoline, VEX / EVEX / XOP / REX2 `0x80-0x8F`, `xbegin` | 0 (refused) | 62 encodings + `xbegin` |
| `67 8B 05 disp32` | Writes 1 byte below `trampoline.c`'s `instBuf[16]` (stack out-of-bounds) | OK |

`7586d1a` cannot be taken alone. It sits between the two decoder commits, and `72fca12` already carries the whole
62-encoding class. VirtualQuery fails only at or above `0x7FFFFFFF0000`, where the pin's `IsExecutableAddress` reads
a stale stack struct. The holder and user32 targets are validated before MinHook sees them; ProcessEvent was not.

**What was decided (2026-11-06, maintainer):** first add our own guard on the pin, then bump. The guard refuses any
hook whose bytes MinHook would copy wrongly, whichever decoder is linked:

1. Read every ProcessEvent and Grausam target the way the holder already is: VirtualQuery plus a SEH probe of the
   span MinHook decodes. MinHook reads with no SEH, inside its spin lock. This also gives `7586d1a`'s protection on
   the pin.
2. Decode the bytes MinHook will copy with Zydis, which is already linked. Refuse when `hde64` measures a different
   length, when MinHook's branch classification names a different encoding than Zydis decoded, or when a copied
   instruction carries a relative operand `trampoline.c` does not rewrite.

The bump then becomes a strict improvement: fewer refusals, and the new decoder's mis-relocation class turned into
a refusal.

**What the bump must also update:**

- `Lager.h`'s `kHolderReadSpan` derivation: the new decoder reads at most 15 bytes per instruction, so the reach is
  19 + 15. The 64-byte span itself stays sufficient.
- The read-span assertions in `InvokeScriptTests`.
- A live ProcessEvent and Grausam check.
- Nothing in `check_lager_vendor_sync`: its hash is `35ddbd82…b751` at both `d94c64d` and `8af6b4a`, because
  `7586d1a` touches only `IsExecutableAddress`, which it does not hash.

**How it was verified:**

- Two readers and one differential harness, each with an adversarial verifier.
- The harness linked `trampoline.c` with the decoder of each of `d94c64d`, `72fca12` and `8af6b4a` (symbol-renamed
  into one exe). Negative controls confirmed the builds decode differently.
- Corpus:
  - every `.pdata` start of 6 fixtures and 8 games;
  - entry points and executable section starts;
  - the user32 and win32u exports;
  - the ProcessEvent RVAs from the logs.
- 103.9M mid-function instructions were swept for the risky classes.
- The harness is scratch on the verification PC (`out/vendorpin/`). It is to be committed with the bump if one
  happens.

**Not verified:**

- Run-time bytes. Every byte was read from a file, so code a protector decrypts at run time (Extinction), and user32
  bytes an overlay may already have patched, were not covered.
- `hook.c`'s thread-IP fix-ups.
- The `IsExecutableAddress` change on a VirtualQuery that actually fails.
- 174 of the 270 logged holds come from `dll_core_test`, which loads `build\dll\VERSION.dll`. Real titles and fixtures
  account for 96.

**What would reopen it, amending audit #7's triggers:**

- Upstream tags a release containing `72fca12..8af6b4a`.
- A log shows `MH_ERROR_UNSUPPORTED_FUNCTION`.
- A new hook target has VEX / EVEX / 0F38 / 0F3A / tzcnt / lzcnt / popcnt in **any instruction MinHook decodes**. That
  includes the first instruction at offset ≥ 5, not just the first 5 bytes, because `trampoline.c` decodes that
  instruction before deciding it only needs a jump back to it.
