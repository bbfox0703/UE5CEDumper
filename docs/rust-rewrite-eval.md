# Rewriting parts of the DLL in Rust, for stability — EVALUATED, NOT WORTH IT

> **Verdict (maintainer, 2026-11-14): no Rust rewrite, partial or whole.** The defect history says Rust's compiler
> and standard library would have prevented very little of what made the DLL unstable, and a port would give up a
> safety net the DLL relies on today. The cheap C++ hardening that covers most of what Rust would have caught is
> queued in [todo.md](todo.md) `[CPP-HARDENING-2026-11-14]`. Re-open this only with a new reason: a defect class that
> keeps recurring in our own data structures, which C++ tooling cannot catch.

The question: *would rewriting some of the DLL in Rust make it more stable?* Answered from the code and from every
DLL defect the repo has recorded, by workflow `wf_17c2b19a-5d3` (three researchers, then one skeptic who re-read the
cited entries and corrected several verdicts; the numbers below are the corrected ones).

## 1. What the defect history says

- **Sources:** [dev-log.md](dev-log.md) (builds 3263–3709), the eight `audit-*.md` files, [known-issues.md](known-issues.md),
  the DLL sections of [working-lessons.md](working-lessons.md), and the archived dev-logs for the older live crashes.
  About 213 distinct DLL defects after de-duplication by tag (the lists overlap: one class-cache design appears as
  audit #5 A10, U5 and B10-PREREQ).
- **Prevented outright by Rust: about 2–4%.** The file-static `ScanReport` written from two pipe threads (build 3423),
  the class-container cache read without its lock (latent, found by review: `multipipe-eval.md` §4), B10-PREREQ (assigning over a map entry
  whose reference was handed out), B11 (`fprintf` on a null `FILE*`), and the DQ7R game-exit `0xC0000409`: a joinable
  static `std::thread` destroyed at process exit calls `std::terminate` (commits `cfd84075`, `822c7bf6`). Rust never
  runs destructors of statics and dropping a `JoinHandle` detaches.
- **Partly: about 5–9%,** and most of those are idioms C++ has too: `#[must_use]` results against `[[nodiscard]]`,
  atomics, owned copies. Example: `[SEETHRUTALLY]` and the Fly `MovementMode` writes dropped a `bool` result; Rust warns
  only when the result is a `Result`, and C++ would warn with `[[nodiscard]]`, which dll/src uses nowhere today.
- **Not at all: about 89%.**

  | Class | Share | Why Rust does not help |
  |---|---|---|
  | UE layout or logic | ~37% | Wrong offsets, wrong version assumptions: the same logic in any language. |
  | Protocol, ownership by client, cancellation, reporting | ~31% | Correctly synchronised state with the wrong scope, e.g. `[MULTIPIPE-CANCEL-2026-09-07]`. |
  | MinHook, loader lock, CRT start-up order, ABI | ~11% | The dxgi SRWLOCK self-deadlock under the loader lock: Rust's `Mutex` is not re-entrant either. |
  | Game memory, game-thread affinity, wait ordering | the rest | Reads of foreign memory are `unsafe` in Rust too; `[SHUTDOWN-SEETHRU-WAIT]`'s wait cycle compiles in Rust. |

- **Weighted by live crashes and hangs** (about 16–18 incidents), Rust's share is higher, about 10–25%: about two
  prevented (the DQ7R exit terminate; F1's 5 s static teardown per game exit) and two or three partly (the pre-CRT
  allocator faults, since Rust's allocator calls `GetProcessHeap()` and never reads the UCRT heap). **All of them are
  already fixed in C++** (`SafeThread`, owned parameter copies, `try_emplace`, `RunThreadGuarded`).

## 2. What the code says

- 77,004 lines in 32 `.cpp` + 43 `.h`. About 768 `ReadSafe` call sites, 84% of them in Ubel, Aura and Genau: those
  modules are mostly guarded reads of the game's memory, which would be `unsafe` raw-pointer code in Rust as well.
- Per module, Rust's benefit is at best medium (Fern's own data and IPC; Stark's queue) and none for Macht, Himmel,
  Tot, Lugner/Heiter and Grausam/Linie. Radar and Orden are pure algorithms over byte buffers and the only easy fit.

## 3. What a port would give up or add

- **No native SEH.** The DLL builds with `/EHa`, and `catch(...)` in `RunThreadGuarded` has contained live access
  violations: the game survived (`[SCAN-EARLY-TRIGGER-CONTAINED]`). In Rust an access violation outside an explicit C
  `__try` shim (the `microseh` crate's approach) is a process crash; RFC 2945 leaves SEH through Rust frames undefined.
- **A panic across `extern "C"` aborts the process** since Rust 1.81, which here means the game exits. About 70 entry
  points (63 exports, the CE plugin exports, the detours, thread procedures, the window procedure) would each need
  `catch_unwind`. Allocation failure also aborts by default.
- **Cheat Engine loads and unloads the DLL repeatedly;** Rust's thread-local destructors carry documented caveats for
  a `cdylib` that is unloaded.
- **Build cost:** cargo and Corrosion inside `build.ps1` and CMake, `+crt-static` to match `/MT`, Rust's native
  libraries linked into the main DLL and all four proxies, a sixth test suite, `.rs` support in the gates
  (`check_derived_counts`, `comment_impact`, `check_comment_refs`, `check_text_integrity`), CI, and cargo build-script
  executables that Bitdefender may quarantine.

## 4. If Rust is ever tried

As a toolchain-cost pilot, not for stability: Radar or Orden behind a C ABI. They touch no Win32 and no game pointers,
and `dll_helpers_test` already exercises Radar in isolation, so a swap is testable.
