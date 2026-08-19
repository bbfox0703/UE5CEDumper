"""Call a no-argument exported function of our injected DLL in a live process.

    py call_export.py <pid> UE5_StartPipeServer
    py call_export.py <pid> UE5_StartPipeServer --module winmm.dll

WHY THIS EXISTS -- the OCTOPATH relauncher race (found 2026-08-19).
`UE5_StartPipeServer` guards with a one-shot `CreateFileW(PIPE_NAME, OPEN_EXISTING)`
and, if that succeeds, logs `pipe already exists (another instance running)` and never
starts a server. On a title that RELAUNCHES ITSELF that guard is a TOCTOU race:

    PID 28188  proxy loads, "pipe server started"            <- the launcher process
    PID 65684  proxy loads 3 s later, "pipe already exists"  <- the real game
    PID 28188  exits, taking its pipe server with it
    => the surviving game has our DLL mapped and NO pipe server at all

Reproduced three times on OCTOPATH TRAVELER, each run a single DLL load per process
(the two loads land in SEPARATE log files because each process start rotates
`init-0.log`, which is why it first looked like one instance contradicting itself).

Calling the export in the survivor is the workaround: the guard now finds no pipe,
because the launcher is gone, and starts the server for real.

MECHANISM: resolve the export from the module's PE export directory in the TARGET's
address space (same reader `mailbox_addr.py` uses), then `CreateRemoteThread` on it.
The function must be `__stdcall`/`__cdecl` with no arguments and an integer-ish return
-- `LPTHREAD_START_ROUTINE` passes one pointer argument, which such a function ignores.
The thread exit code is the return value truncated to 32 bits.
"""
import argparse
import ctypes
import pathlib
import subprocess
import sys
from ctypes import wintypes

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))

k32 = ctypes.WinDLL("kernel32", use_last_error=True)
k32.OpenProcess.restype = ctypes.c_void_p
k32.OpenProcess.argtypes = [wintypes.DWORD, wintypes.BOOL, wintypes.DWORD]
k32.CreateRemoteThread.restype = ctypes.c_void_p
k32.CreateRemoteThread.argtypes = [ctypes.c_void_p, ctypes.c_void_p, ctypes.c_size_t,
                                   ctypes.c_void_p, ctypes.c_void_p, wintypes.DWORD,
                                   ctypes.c_void_p]
k32.WaitForSingleObject.argtypes = [ctypes.c_void_p, wintypes.DWORD]
k32.GetExitCodeThread.argtypes = [ctypes.c_void_p, ctypes.POINTER(wintypes.DWORD)]
k32.CloseHandle.argtypes = [ctypes.c_void_p]

PROCESS_ALL_ACCESS = 0x1F0FFF


def resolve(pid, module_hint, export):
    """Address of `export` inside the target, via mailbox_addr's module/PE readers."""
    import mailbox_addr as ma
    r = ma.Reader(pid)
    for path, base in ma.modules(pid):
        if not base:
            continue
        low = path.lower()
        if module_hint:
            if not low.endswith(module_hint.lower()):
                continue
        elif not any(low.endswith(x) for x in ("dxgi.dll", "version.dll", "dinput8.dll",
                                               "winmm.dll", "ue5dumper.dll")):
            continue
        try:
            addr = ma.find_export(r, base, export)
        except SystemExit:
            continue
        if addr:
            return path, addr
    raise SystemExit(f"call_export: {export!r} not found in any of our modules in pid {pid}")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("pid", type=int)
    ap.add_argument("export")
    ap.add_argument("--module", default=None)
    ap.add_argument("--timeout", type=int, default=30000)
    a = ap.parse_args()

    path, addr = resolve(a.pid, a.module, a.export)
    print(f"module : {path}")
    print(f"export : {a.export} @ {addr:#x}")

    h = k32.OpenProcess(PROCESS_ALL_ACCESS, False, a.pid)
    if not h:
        raise SystemExit(f"call_export: OpenProcess({a.pid}) failed "
                         f"err={ctypes.get_last_error()}")
    try:
        th = k32.CreateRemoteThread(h, None, 0, ctypes.c_void_p(addr), None, 0, None)
        if not th:
            raise SystemExit(f"call_export: CreateRemoteThread failed "
                             f"err={ctypes.get_last_error()}")
        rc = k32.WaitForSingleObject(ctypes.c_void_p(th), a.timeout)
        if rc != 0:
            raise SystemExit(f"call_export: the remote call did not finish "
                             f"(WaitForSingleObject={rc}); do NOT read this as success")
        code = wintypes.DWORD(0)
        k32.GetExitCodeThread(ctypes.c_void_p(th), ctypes.byref(code))
        k32.CloseHandle(ctypes.c_void_p(th))
        print(f"returned: {code.value} (0x{code.value:X})")
    finally:
        k32.CloseHandle(ctypes.c_void_p(h))
    return 0


if __name__ == "__main__":
    sys.exit(main())
