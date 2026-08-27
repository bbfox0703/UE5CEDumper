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

── `--forward`: the complement ──────────────────────────────────────────────────────────────

    py tools/verify/proxy_precrt_gate.py --forward dist/proxy/dinput8.dll DllCanUnloadNow
    py tools/verify/proxy_precrt_gate.py --forward dist/proxy/dxgi.dll \
        DXGIDeclareAdapterRemovalSupport "dxgi proxy: lazily forwarded"

A NORMAL `LoadLibraryW`, so DllMain runs and the CRT comes up, and the export must then
actually **forward** to the real System32 DLL. This is what covers the 23 magic statics that
became `static Fn fn = nullptr; if (!fn) …`: a latched null leaves the export permanently
dead, and from the outside that is indistinguishable from success, because the forwarders
answer a *documented failure value* (`FALSE` / `0` / `E_FAIL`) rather than crashing.

So the verdict is **not** the return value — it is the proxy's own log line, written only
when `LoadReal*()` / `*Proxy_EnsureResolved()` actually loaded the real DLL, and only counted
when the LINE's own timestamp is newer than this run. Pass the marker explicitly for the
table resolvers (dxgi/winmm log `"<name> proxy: lazily forwarded N/N"`, the C forwarders log
`"Loaded real <dll>"`).

⚠ Two things this mode gets wrong if you copy the pre-CRT probe's habits: calling with
garbage arguments is unsafe (the *genuine* function dereferences them, so every export needs
an entry in `SAFE_CALLS`), and a differing second answer is **not** a failure — real exports
can be stateful, e.g. `DXGIDeclareAdapterRemovalSupport` answers `S_OK` then
`0x887A0036 DXGI_ERROR_ALREADY_EXISTS`, which is itself proof the call reached the real DLL.

⚠ It leaves a `%LOCALAPPDATA%\\UE5CEDumper\\Logs\\python\\` folder behind — the proxy logs
under the host process's name, and here the host is the test runner.
"""
import argparse
import ctypes
import os
import subprocess
import sys
import time

DONT_RESOLVE_DLL_REFERENCES = 0x00000001

# Exit codes from the child. Anything else is a fault (0xC0000005 etc. arrives as a huge
# unsigned value, or as a negative one depending on how Windows reports it).
EXIT_RETURNED = 0
EXIT_LOAD_FAILED = 10
EXIT_NO_EXPORT = 11


def _call_no_args(addr):
    return ctypes.CFUNCTYPE(ctypes.c_uint64)(addr)()


def _call_getfileversioninfosizew(addr):
    fn = ctypes.CFUNCTYPE(ctypes.c_uint32, ctypes.c_wchar_p,
                          ctypes.POINTER(ctypes.c_uint32))(addr)
    handle = ctypes.c_uint32(0)
    return fn(os.path.join(os.environ.get("SystemRoot", r"C:\Windows"),
                           "System32", "kernel32.dll"), ctypes.byref(handle))


def _call_timebeginperiod(addr):
    # 1 ms. The child exits immediately after, which releases it; and winmm's own
    # TIMERR_NOERROR is 0, the same value our stub answers with -- so for this one the return
    # value proves nothing and the marker check is the whole verdict.
    return ctypes.CFUNCTYPE(ctypes.c_uint32, ctypes.c_uint32)(addr)(1)


def _call_verlanguagenamew(addr):
    fn = ctypes.CFUNCTYPE(ctypes.c_uint32, ctypes.c_uint32, ctypes.c_wchar_p,
                          ctypes.c_uint32)(addr)
    buf = ctypes.create_unicode_buffer(128)
    return fn(0x0409, buf, 128)


# Known-safe invocations. Real arguments, real expected answers — a correct forward returns
# something non-trivial (a version-info byte count, a language-name length), a broken one
# returns the forwarder's documented failure value instead.
SAFE_CALLS = {
    "DllCanUnloadNow": _call_no_args,                        # dinput8: takes no arguments
    # dxgi: no arguments, and a genuinely STATEFUL one — S_OK the first time, then
    # 0x887A0036 DXGI_ERROR_ALREADY_EXISTS. That second answer is itself proof the call
    # reached the real System32 dxgi, since our stub would answer the same both times.
    # ⚠ Its log marker is NOT "Loaded real dxgi.dll" (the table resolvers log
    # "<name> proxy: lazily forwarded N/N"), so pass the marker explicitly.
    "DXGIDeclareAdapterRemovalSupport": _call_no_args,
    "timeBeginPeriod": _call_timebeginperiod,                  # winmm: marker is the verdict
    "GetFileVersionInfoSizeW": _call_getfileversioninfosizew,  # version: > 0 on success
    "VerLanguageNameW": _call_verlanguagenamew,                # version: > 0 on success
}


def child_forward(dll_path: str, export: str) -> int:
    """The complement of child(): a NORMAL load, so DllMain runs and the CRT comes up, and the
    export must then actually FORWARD to the real System32 DLL.

    This is what covers the 23 magic statics that became `static Fn fn = nullptr; if (!fn) …`.
    A latched null would make the export permanently dead, and returning a documented failure
    value looks identical to success from the outside — so the PASS condition is not the return
    value, it is the proxy's own `Loaded real <dll>` log line, which only LoadReal*() writes.
    """
    k32 = ctypes.WinDLL("kernel32", use_last_error=True)
    k32.LoadLibraryW.restype = ctypes.c_void_p
    k32.LoadLibraryW.argtypes = [ctypes.c_wchar_p]
    k32.GetProcAddress.restype = ctypes.c_void_p
    k32.GetProcAddress.argtypes = [ctypes.c_void_p, ctypes.c_char_p]

    h = k32.LoadLibraryW(dll_path)
    if not h:
        print(f"      LoadLibraryW failed, err={ctypes.get_last_error()}")
        return EXIT_LOAD_FAILED
    print(f"      loaded at 0x{h:016X}, DllMain HAS run")
    addr = k32.GetProcAddress(h, export.encode("ascii"))
    if not addr:
        print(f"      GetProcAddress({export}) -> NULL")
        return EXIT_NO_EXPORT

    # ⚠ Unlike the pre-CRT probe, this call REALLY FORWARDS, so the signature-agnostic
    # zero-argument trick is unsafe here: the genuine System32 function dereferences its
    # arguments. (Learned by doing — a bare GetFileVersionInfoSizeW() faulted the child.)
    # Every export tested here needs a known-safe invocation, and an unknown one is refused
    # rather than guessed at.
    call = SAFE_CALLS.get(export)
    if call is None:
        print(f"      no safe invocation known for {export} — refusing to call it with\n"
              f"      garbage arguments. Add it to SAFE_CALLS.")
        return EXIT_NO_EXPORT
    rv = call(addr)
    print(f"      {export}(...) returned 0x{rv:X}")
    # Twice: the retry-on-null cache is only exercised on the SECOND call. A latched null
    # would answer differently the second time round.
    rv2 = call(addr)
    note = "" if rv2 == rv else "   <-- differs (can be genuine state, see below)"
    print(f"      {export}(...) again  0x{rv2:X}{note}")
    # ⚠ A DIFFERING second answer is NOT a failure. Some real exports are stateful:
    # dxgi!DXGIDeclareAdapterRemovalSupport returns S_OK once and then
    # 0x887A0036 DXGI_ERROR_ALREADY_EXISTS -- which is itself proof the call reached the
    # GENUINE System32 function, because our own stub would answer identically both times.
    # The pass/fail verdict belongs to the marker check in the parent, not here.
    return EXIT_RETURNED


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
    ap.add_argument("--child-forward", action="store_true", help=argparse.SUPPRESS)
    ap.add_argument("--forward", action="store_true",
                    help="NORMAL load (DllMain runs); PASS = the proxy logged 'Loaded real <dll>'")
    ap.add_argument("--compare", action="store_true",
                    help="second positional is a pre-fix binary used as the NEGATIVE CONTROL")
    ap.add_argument("paths", nargs="+")
    args = ap.parse_args()

    if args.child:
        return child(args.paths[0], args.paths[1])
    if args.child_forward:
        return child_forward(args.paths[0], args.paths[1])

    if args.forward:
        if len(args.paths) not in (2, 3):
            ap.error("--forward needs: <proxy.dll> <ExportName> [log-marker]")
        dll, export = args.paths[0], args.paths[1]
        # ⚠ .lower() the WHOLE marker, not just the basename — it is compared against
        # line.lower(), so a capital "L" in the prefix makes the test never match and the rig
        # reports FAIL for a run that passed. It did exactly that first time round.
        # The dxgi/winmm flavours resolve a whole table and log "<name> proxy: lazily
        # forwarded N/N" instead of the C forwarders' "Loaded real <dll>", so the marker is
        # overridable rather than assumed.
        marker = (args.paths[2] if len(args.paths) == 3
                  else "Loaded real " + os.path.basename(dll)).lower()
        logs = os.path.join(os.environ.get("LOCALAPPDATA", ""), "UE5CEDumper", "Logs")
        started = time.time()
        print(f"[FORWARD] {dll}  export={export}")
        proc = subprocess.run(
            [sys.executable, "-u", os.path.abspath(__file__), "--child-forward", dll, export],
            capture_output=True, text=True)
        sys.stdout.write(proc.stdout)
        if proc.stderr.strip():
            sys.stdout.write("      stderr: " + proc.stderr.strip().splitlines()[-1] + "\n")
        code = proc.returncode & 0xFFFFFFFF
        if code != EXIT_RETURNED:
            print(f"      => {describe(code)}")
            return 1
        # The return value alone cannot tell "forwarded" from "answered a failure value", so
        # the verdict comes from the proxy's own log line. Only folders touched by THIS run
        # count -- an old folder would hand back a PASS earned by a previous build.
        # ⚠ The LINE's own timestamp must be newer than this run, not just the FILE's mtime.
        # A 5-second file window let a previous run of the same proxy satisfy the check — which
        # would mask exactly the regression this rig exists to catch (a proxy that stopped
        # forwarding would still "pass" on yesterday's line).
        hits = []
        for root, _dirs, files in os.walk(logs):
            for fn in files:
                p = os.path.join(root, fn)
                try:
                    if os.path.getmtime(p) < started - 5:
                        continue
                    with open(p, encoding="utf-8", errors="replace") as fh:
                        for line in fh:
                            if marker not in line.lower():
                                continue
                            stamp = line[1:24]
                            try:
                                t = time.mktime(time.strptime(stamp[:19], "%Y-%m-%d %H:%M:%S"))
                            except ValueError:
                                continue
                            if t + 1 >= started:
                                hits.append((p, line.strip()))
                except OSError:
                    pass
        if not hits:
            print(f"      => FAIL: no '{marker}' line written during this run.\n"
                  f"         The export returned, but nothing proves it FORWARDED -- a latched\n"
                  f"         null would look exactly like this.")
            return 1
        for p, line in hits[:2]:
            print(f"      log: {line}")
        print("      => PASS: the proxy loaded the real DLL and forwarded.")
        return 0

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
