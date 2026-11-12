r"""Serve the LEGACY \\.\pipe\UE5DumpBfx for a while, as a game running an old UE5Dumper DLL would.

    py tools/verify/legacy_pipe_stub.py [--seconds 120]
    py tools/verify/legacy_pipe_stub.py --pid P --instances 6 --answer [--seconds 120]

--pid P creates a GAME's per-process name, \\.\pipe\UE5DumpBfx_P, before the game's DLL can (register
[PIPE-PER-PROCESS-2026-11-06] item 1: another process holding a game's name); --answer then replies to init and to
get_pointers with this stub's own pid, so a client that checks who answered can tell it is not the game.

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
import json
import os
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
k32.ReadFile.argtypes = [wintypes.HANDLE, ctypes.c_void_p, wintypes.DWORD, ctypes.POINTER(wintypes.DWORD), ctypes.c_void_p]
k32.WriteFile.argtypes = [wintypes.HANDLE, ctypes.c_char_p, wintypes.DWORD, ctypes.POINTER(wintypes.DWORD),
                          ctypes.c_void_p]
k32.DisconnectNamedPipe.argtypes = [wintypes.HANDLE]
k32.CloseHandle.argtypes = [wintypes.HANDLE]


def reply_to(line: bytes) -> bytes:
    """--answer: what a foreign process holding a game's pipe name might say -- enough for a client to read its pid
    (get_pointers' "pid"), and so to tell it is not the game."""
    text = line.decode("utf-8", errors="replace").lstrip("﻿").strip()
    try:
        req = json.loads(text)
    except json.JSONDecodeError:
        return b""
    rid, cmd = req.get("id"), req.get("cmd")
    if cmd == "init":
        out = {"id": rid, "ok": True}
    elif cmd == "get_pointers":
        out = {"id": rid, "ok": True, "pid": os.getpid()}
    else:
        out = {"id": rid, "ok": False, "error": "stub"}
    return (json.dumps(out) + "\n").encode("utf-8")


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    ap.add_argument("--seconds", type=float, default=120.0)
    ap.add_argument("--pid", type=int, help="create a game's per-process name UE5DumpBfx_<pid> instead of the legacy one")
    ap.add_argument("--instances", type=int, default=1, help="instances served at once, one accept thread each")
    ap.add_argument("--answer", action="store_true",
                    help="answer init and get_pointers (its own pid) instead of disconnecting each client")
    a = ap.parse_args()
    name = f"{LEGACY}_{a.pid}" if a.pid else LEGACY

    # FIRST_PIPE_INSTANCE on the first create: fail rather than join a real DLL's instances under the same name.
    handles = []
    for i in range(max(1, a.instances)):
        flags = PIPE_ACCESS_DUPLEX | (FILE_FLAG_FIRST_PIPE_INSTANCE if i == 0 else 0)
        h = k32.CreateNamedPipeW(name, flags, PIPE_REJECT_REMOTE_CLIENTS, max(1, a.instances), 4096, 4096, 0, None)
        if h == INVALID:
            err = ctypes.get_last_error()
            why = "the name already exists (a real DLL?)" if err == ERROR_ACCESS_DENIED else f"err {err}"
            print(f"legacy_pipe_stub: could not create {name}: {why}", file=sys.stderr)
            return 2
        handles.append(h)
    print(f"legacy_pipe_stub: pid {os.getpid()} serving {name} x{len(handles)} for {a.seconds:.0f} s "
          f"({'answers init and get_pointers' if a.answer else 'answers nothing'})", flush=True)

    stop = threading.Event()

    def accept_loop(h, idx):
        buf = ctypes.create_string_buffer(4096)
        while not stop.is_set():
            k32.ConnectNamedPipe(h, None)      # blocks until a client connects (or the handle closes)
            if stop.is_set():
                return
            if not a.answer:
                print("legacy_pipe_stub: a client connected -- disconnecting it", flush=True)
                k32.DisconnectNamedPipe(h)
                continue
            print(f"legacy_pipe_stub: instance {idx}: a client connected -- answering it", flush=True)
            pending = b""
            while not stop.is_set():
                got = wintypes.DWORD()
                if not k32.ReadFile(h, buf, len(buf), ctypes.byref(got), None) or got.value == 0:
                    break
                pending += buf.raw[:got.value]
                while b"\n" in pending:
                    line, pending = pending.split(b"\n", 1)
                    out = reply_to(line)
                    if out:
                        print(f"legacy_pipe_stub: instance {idx}: {line[:80]!r} -> {out[:80]!r}", flush=True)
                        wrote = wintypes.DWORD()
                        k32.WriteFile(h, out, len(out), ctypes.byref(wrote), None)
            k32.DisconnectNamedPipe(h)

    for i, h in enumerate(handles):
        threading.Thread(target=accept_loop, args=(h, i), daemon=True).start()
    time.sleep(a.seconds)
    stop.set()
    # No CloseHandle: the accept thread is parked in a synchronous ConnectNamedPipe on this handle, and closing it
    # from here can wait on that call. The process exit releases the handle, and the name leaves \\.\pipe\ with it.
    print("legacy_pipe_stub: done", flush=True)
    return 0


if __name__ == "__main__":
    sys.exit(main())
