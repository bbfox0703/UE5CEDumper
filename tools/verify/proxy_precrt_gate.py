#!/usr/bin/env python3
"""Prove a proxy DLL survives having an export called BEFORE its DllMain has run.

This is the offline stand-in for the OCTOPATH TRAVELER failure in
docs/audit-2026-08-26-dxgi-appcompat-crash.md. There, Windows' AppCompat shim engine
(`apphelp!SE_DllLoaded` -> `AcGenral!NS_DXGICompat`) calls
`dxgi.dll!SetAppCompatStringPointer` after our module is MAPPED but before
`_DllMainCRTStartup` has run. The lazy thunk then entered a resolver that logs, logging
allocated, `__acrt_heap` was still NULL, and the process died in
`ntdll!RtlAllocateHeap+0x54` before the EXE entry point.

`LoadLibraryExW(..., DONT_RESOLVE_DLL_REFERENCES)` reproduces that state without a game,
without a compat layer and without the shim engine: the image is mapped, its exports are
callable, and **DllMain is not called**. Calling a forwarding thunk in that state is exactly
the window the fix closes.

    py tools/verify/proxy_precrt_gate.py dist/proxy/dxgi.dll SetAppCompatStringPointer
    py tools/verify/proxy_precrt_gate.py --compare dist/proxy/dxgi.dll \
        out/proxy-backups/Avowed.dxgi.dll.20260823-212124.bak SetAppCompatStringPointer

⚠ WHAT THIS DOES AND DOES NOT PROVE.

  PROVES: the thunk, entered before DllMain, returns instead of transferring control. That
  is the property that was missing, and it is checked here against a real mapped image and
  real machine code — not against source text.

  DOES NOT PROVE: that the *allocation* would have been the thing to fault. Under
  DONT_RESOLVE_DLL_REFERENCES the IAT is not snapped either, so a pre-fix binary faults on
  its first imported call (`AcquireSRWLockExclusive`) rather than on `HeapAlloc(NULL, ...)`.
  Same place in OUR code, earlier instruction. The negative control below therefore shows
  "the old binary dies inside the resolver, the new one never enters it" — which is the
  fix — and NOT a byte-for-byte replay of the game crash. Only the game proves that.

Each load+call runs in a CHILD PROCESS so that a fault is a reportable exit code rather than
the death of this script. That is the whole reason for the --child plumbing.
"""
import argparse
import ctypes
import os
import subprocess
import sys

DONT_RESOLVE_DLL_REFERENCES = 0x00000001

# Exit codes from the child. Anything else is a fault (0xC0000005 etc. arrives as a huge
# unsigned value, or as a negative one depending on how Windows reports it).
EXIT_RETURNED = 0
EXIT_LOAD_FAILED = 10
EXIT_NO_EXPORT = 11


def child(dll_path: str, export: str) -> int:
    k32 = ctypes.WinDLL("kernel32", use_last_error=True)
    k32.LoadLibraryExW.restype = ctypes.c_void_p
    k32.LoadLibraryExW.argtypes = [ctypes.c_wchar_p, ctypes.c_void_p, ctypes.c_uint32]
    k32.GetProcAddress.restype = ctypes.c_void_p
    k32.GetProcAddress.argtypes = [ctypes.c_void_p, ctypes.c_char_p]

    h = k32.LoadLibraryExW(dll_path, None, DONT_RESOLVE_DLL_REFERENCES)
    if not h:
        print(f"      LoadLibraryExW failed, err={ctypes.get_last_error()}")
        return EXIT_LOAD_FAILED
    print(f"      mapped at 0x{h:016X} with DllMain NOT called")

    addr = k32.GetProcAddress(h, export.encode("ascii"))
    if not addr:
        print(f"      GetProcAddress({export}) -> NULL, err={ctypes.get_last_error()}")
        return EXIT_NO_EXPORT
    print(f"      {export} -> 0x{addr:016X} (+0x{addr - h:X})")

    # Signature-agnostic on purpose: the thunk either tail-jumps (forwards) or does
    # `xor eax,eax; ret`. x64 is caller-cleans, so calling through a zero-arg prototype is
    # safe whatever the real export's arity would have been.
    proto = ctypes.CFUNCTYPE(ctypes.c_uint64)
    print(f"      calling {export} ...")
    rv = proto(addr)()
    print(f"      returned cleanly: 0x{rv:X}")
    return EXIT_RETURNED


def run_one(dll_path: str, export: str) -> int:
    """Spawn the child; return its exit code (a fault shows up as a non-zero/huge code)."""
    # -u is load-bearing: the interesting child DIES, and buffered stdout dies with it. Without
    # it the negative control prints nothing at all and you cannot tell "faulted while calling
    # the thunk" (the result we want) from "faulted at LoadLibraryExW" (a rig that proves
    # nothing). Ask how a passing result would look different from a broken rig.
    proc = subprocess.run(
        [sys.executable, "-u", os.path.abspath(__file__), "--child", dll_path, export],
        capture_output=True, text=True,
    )
    sys.stdout.write(proc.stdout)
    if proc.stderr.strip():
        sys.stdout.write("      stderr: " + proc.stderr.strip().splitlines()[-1] + "\n")
    return proc.returncode & 0xFFFFFFFF


def describe(code: int) -> str:
    if code == EXIT_RETURNED:
        return "RETURNED CLEANLY"
    if code == EXIT_LOAD_FAILED:
        return "could not map the DLL"
    if code == EXIT_NO_EXPORT:
        return "export not present"
    named = {
        0xC0000005: "ACCESS VIOLATION",
        0xC0000409: "STACK BUFFER OVERRUN",
        0xC00000FD: "STACK OVERFLOW",
        0xC0000374: "HEAP CORRUPTION",
    }
    return f"FAULTED 0x{code:08X} ({named.get(code, 'unknown')})"


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--child", action="store_true", help=argparse.SUPPRESS)
    ap.add_argument("--compare", action="store_true",
                    help="second positional is a pre-fix binary used as the NEGATIVE CONTROL")
    ap.add_argument("paths", nargs="+")
    args = ap.parse_args()

    if args.child:
        return child(args.paths[0], args.paths[1])

    if args.compare:
        if len(args.paths) != 3:
            ap.error("--compare needs: <fixed.dll> <prefix.dll> <ExportName>")
        fixed, prefix, export = args.paths
        print(f"export under test: {export}\n")
        print(f"[NEGATIVE CONTROL] {prefix}")
        bad = run_one(prefix, export)
        print(f"      => {describe(bad)}\n")
        print(f"[FIXED]            {fixed}")
        good = run_one(fixed, export)
        print(f"      => {describe(good)}\n")

        if good != EXIT_RETURNED:
            print("FAIL: the fixed binary did not return cleanly.")
            return 1
        if bad == EXIT_RETURNED:
            print("FAIL: the negative control ALSO returned cleanly — this rig cannot tell the\n"
                  "      two binaries apart, so it proves nothing about the fix. Check that the\n"
                  "      pre-fix binary is really pre-fix before trusting any PASS from it.")
            return 1
        print("PASS: pre-fix faults inside the resolver, fixed returns without entering it.")
        return 0

    if len(args.paths) != 2:
        ap.error("needs: <proxy.dll> <ExportName>")
    dll, export = args.paths
    print(f"[SINGLE] {dll}  export={export}")
    code = run_one(dll, export)
    print(f"      => {describe(code)}")
    return 0 if code == EXIT_RETURNED else 1


if __name__ == "__main__":
    sys.exit(main())
