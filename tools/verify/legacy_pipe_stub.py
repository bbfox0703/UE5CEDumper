r"""Serve the LEGACY \\.\pipe\UE5DumpBfx for a while, as a game running an old UE5Dumper DLL would.

    py tools/verify/legacy_pipe_stub.py [--seconds 120]

[PIPE-PER-PROCESS-2026-11-06] Since the per-process name, a DLL serves \\.\pipe\UE5DumpBfx_<pid>, and a UI or rig that
finds only the legacy name must refuse it and say why: a game runs an old DLL (typically a proxy never refreshed).
Checking that needs the legacy name up without an old build in a game. This stub creates it and nothing else: it
answers no command, and any client that connects is disconnected at once. It also refuses to start when the name
already exists, so it never shares a name with a real old DLL.

Exit codes: 0 served for the whole time; 2 the name already exists or could not be created.
"""
from __future__ import annotations

import argparse
import ctypes
import sys
import threading
import time
from ctypes import wintypes

LEGACY = "\\\\.\\pipe\\UE5DumpBfx"
PIPE_ACCESS_DUPLEX = 0x00000003
FILE_FLAG_FIRST_PIPE_INSTANCE = 0x00080000
PIPE_REJECT_REMOTE_CLIENTS = 0x00000008
ERROR_ACCESS_DENIED = 5
INVALID = wintypes.HANDLE(-1).value

k32 = ctypes.WinDLL("kernel32", use_last_error=True)
k32.CreateNamedPipeW.restype = wintypes.HANDLE
k32.CreateNamedPipeW.argtypes = [wintypes.LPCWSTR, wintypes.DWORD, wintypes.DWORD, wintypes.DWORD, wintypes.DWORD,
                                 wintypes.DWORD, wintypes.DWORD, ctypes.c_void_p]
k32.ConnectNamedPipe.argtypes = [wintypes.HANDLE, ctypes.c_void_p]
k32.DisconnectNamedPipe.argtypes = [wintypes.HANDLE]
k32.CloseHandle.argtypes = [wintypes.HANDLE]


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    ap.add_argument("--seconds", type=float, default=120.0)
    a = ap.parse_args()

    # FIRST_PIPE_INSTANCE: fail rather than join a real old DLL's instances under the same name.
    h = k32.CreateNamedPipeW(LEGACY, PIPE_ACCESS_DUPLEX | FILE_FLAG_FIRST_PIPE_INSTANCE,
                             PIPE_REJECT_REMOTE_CLIENTS, 1, 4096, 4096, 0, None)
    if h == INVALID:
        err = ctypes.get_last_error()
        why = "the name already exists (a real old DLL?)" if err == ERROR_ACCESS_DENIED else f"err {err}"
        print(f"legacy_pipe_stub: could not create {LEGACY}: {why}", file=sys.stderr)
        return 2
    print(f"legacy_pipe_stub: serving {LEGACY} for {a.seconds:.0f} s (answers nothing)", flush=True)

    stop = threading.Event()

    def accept_loop():
        while not stop.is_set():
            k32.ConnectNamedPipe(h, None)      # blocks until a client connects (or the handle closes)
            if stop.is_set():
                return
            print("legacy_pipe_stub: a client connected -- disconnecting it", flush=True)
            k32.DisconnectNamedPipe(h)

    threading.Thread(target=accept_loop, daemon=True).start()
    time.sleep(a.seconds)
    stop.set()
    # No CloseHandle: the accept thread is parked in a synchronous ConnectNamedPipe on this handle, and closing it
    # from here can wait on that call. The process exit releases the handle, and the name leaves \\.\pipe\ with it.
    print("legacy_pipe_stub: done", flush=True)
    return 0


if __name__ == "__main__":
    sys.exit(main())
