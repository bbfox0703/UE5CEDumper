#!/usr/bin/env python3
"""Prove that UE5_AutoStart returns BEFORE the work it starts has finished.

WHY THIS EXISTS
  Cheat Engine's injection stub does not wait for us. From CE's own source
  (read at tag 7.5, 2026-08-15):

    CEFuncProc.pas:1346-1360   createremotethread, then a wait loop of
                               `counter := 10000 div 10` x 10 ms — a HARD,
                               unconfigurable 10 second ceiling.
    CEFuncProc.pas:1332-1343   with Settings' `cbInjectDLLWithAPC` ticked there is
                               no wait at all: CreateRemoteAPC then `sleep(1000)`.
    CEFuncProc.pas:1379-1387   `finally ... virtualfreeex(processhandle,
                               injectionlocation, 0, MEM_RELEASE)` — UNCONDITIONAL
                               on both paths.

  So if UE5_AutoStart runs its multi-second AOB scan to completion on CE's remote
  thread, CE frees the page that thread is executing on, and the eventual `ret`
  crashes THE GAME. (audit #5 AB2. Note CE's own timeout text claims "Injection
  routine not freed", which that `finally` contradicts.)

  UE5_AutoStart therefore spawns and returns. That is a behaviour, not a shape, and
  no test target in this repo compiles Frieren.cpp — so it is measured here.

WHAT IT MEASURES
  How long the exported UE5_AutoStart takes to return, and what Mimic::InitState
  (mailbox + 0x0C) reads AT THAT MOMENT:

      InitState: 0 IDLE   1 RUNNING   2 READY   3 FAILED   4 SKIPPED

  Async  => returns in milliseconds AND initState is still 0 or 1 on return.
  Blocked => returns in seconds AND initState is already terminal (>= 2).

  This host is not a UE game, so where the scan ENDS is irrelevant and is not
  asserted. What matters is when the call returns relative to the work.

  Reference numbers on the dev machine (build 2932): fixed = 2.3 ms to return with
  ~3.6 s of work after it; the same build with the spawn reverted = 3486 ms to
  return, initState already 2. A real game's scan is 2-8 s, i.e. inside CE's
  ceiling and always past the APC path's 1 s.

MANUAL TOOL — deliberately not wired into build.ps1 or CI
  It LOADS THE DLL INTO THIS PROCESS, which starts real worker threads and opens
  the pipe, so it wants a throwaway process rather than a build step. Run it after
  touching UE5_AutoStart / the DllMain auto-start path.

Usage:
  py tools/probe_autostart_async.py [path-to-UE5Dumper.dll]
Exit 0 = async confirmed, 1 = still blocking, 2 = could not probe.
"""
import ctypes
import os
import sys
import time

OFF_INIT_STATE = 0x0C          # Mimic::MailboxData::initState
RETURN_BUDGET_MS = 500.0       # generous: the fixed path measures ~2 ms
STATE_NAMES = {0: "IDLE", 1: "RUNNING", 2: "READY", 3: "FAILED", 4: "SKIPPED"}


def main() -> int:
    root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    dll_path = sys.argv[1] if len(sys.argv) > 1 else os.path.join(
        root, "dist", "UE5Dumper.dll")

    if not os.path.exists(dll_path):
        print(f"PROBE FAILED: {dll_path} not found — build it first "
              f"(build.ps1 -Target DLL).")
        return 2

    try:
        lib = ctypes.CDLL(dll_path)
    except OSError as e:
        print(f"PROBE FAILED: could not load {dll_path}: {e}")
        return 2

    try:
        fn = lib.UE5_AutoStart
        fn.restype = ctypes.c_bool
        fn.argtypes = []
        base = ctypes.addressof(ctypes.c_char.in_dll(lib, "g_invokeMailbox"))
    except (AttributeError, ValueError) as e:
        print(f"PROBE FAILED: missing export ({e}) — is this a UE5CEDumper DLL?")
        return 2

    def state() -> int:
        return ctypes.c_int32.from_address(base + OFF_INIT_STATE).value

    def named(s: int) -> str:
        return f"{s} ({STATE_NAMES.get(s, '?')})"

    print(f"DLL                     : {dll_path}")
    print(f"initState before call   : {named(state())}")

    t0 = time.perf_counter()
    ret = fn()
    elapsed_ms = (time.perf_counter() - t0) * 1000.0
    at_return = state()

    print(f"UE5_AutoStart returned  : {ret}")
    print(f"elapsed until return    : {elapsed_ms:.1f} ms")
    print(f"initState AT RETURN     : {named(at_return)}"
          f"   <- terminal here means it BLOCKED")

    start = time.perf_counter()
    while time.perf_counter() - start < 30.0:
        time.sleep(0.2)
        if state() >= 2:
            break
    work_ms = (time.perf_counter() - start) * 1000.0

    print(f"initState after waiting : {named(state())}")
    print(f"work continued for      : ~{work_ms:.0f} ms AFTER the call returned")

    ok = elapsed_ms < RETURN_BUDGET_MS and at_return < 2
    print()
    if ok:
        print(f"VERDICT: ASYNC CONFIRMED — returned in {elapsed_ms:.1f} ms, well inside "
              f"CE's 10 s ceiling and its 1 s APC path.")
        return 0
    print("VERDICT: STILL BLOCKING — CE would free the remote stub out from under "
          "this call and the game would crash on the return.")
    return 1


if __name__ == "__main__":
    raise SystemExit(main())
