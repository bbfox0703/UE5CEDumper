#!/usr/bin/env python3
r"""Hold \\.\pipe\AOBMakerCEBridge BUSY -- a server with ONE instance, occupied by our own client.

    py tools/verify/aobmaker_pipe_busy.py --seconds 60

WHY THIS EXISTS. `[W1-PIPEBUSY-LOG]` (backlog L64): a BUSY pipe times out in .NET's
`NamedPipeClientStream.ConnectAsync` exactly like an ABSENT one -- it waits for a free instance until
the deadline -- so the UI used to log "no server ... (Cheat Engine not running ...)" while another
client held the AOBMaker bridge. The fix asks whether the pipe exists after the timeout.

⚠ THE ROW'S OWN STAGING DOES NOT PRODUCE A BUSY PIPE. "A second Cheat Engine holding the pipe" makes
the second plugin fail CreateNamedPipe with 231 and retry; it never connects as a CLIENT, so the first
CE's single instance stays idle-listening and the UI simply connects. Only a client OCCUPYING the one
instance makes ConnectAsync wait out its deadline. This rig manufactures exactly that, with no CE.

Sibling: `ac3_denied_pipe.py` creates the same name with a DENY DACL, which reaches the DIFFERENT
"connect ... failed (UnauthorizedAccessException)" branch -- a contrast, not this row's line.

⛔ Cheat Engine with the AOBMaker plugin must NOT be running: its plugin owns the name, and
CreateNamedPipeW here then fails. The rig says so and exits 2 rather than holding someone else's pipe.
"""
import argparse
import ctypes
import ctypes.wintypes as W
import os
import sys
import time

sys.stdout.reconfigure(encoding="utf-8", errors="replace")

NAME = "AOBMakerCEBridge"
PATH = "\\\\.\\pipe\\" + NAME
k = ctypes.WinDLL("kernel32", use_last_error=True)
k.CreateNamedPipeW.restype = W.HANDLE
k.CreateNamedPipeW.argtypes = [W.LPCWSTR, W.DWORD, W.DWORD, W.DWORD, W.DWORD, W.DWORD, W.DWORD, W.LPVOID]
k.CreateFileW.restype = W.HANDLE
k.CreateFileW.argtypes = [W.LPCWSTR, W.DWORD, W.DWORD, W.LPVOID, W.DWORD, W.DWORD, W.HANDLE]
k.ConnectNamedPipe.argtypes = [W.HANDLE, W.LPVOID]
k.CloseHandle.argtypes = [W.HANDLE]
INVALID = W.HANDLE(-1).value

PIPE_ACCESS_DUPLEX = 0x3
PIPE_TYPE_BYTE_WAIT = 0x0
GENERIC_RW = 0x80000000 | 0x40000000
OPEN_EXISTING = 3
ERROR_PIPE_CONNECTED = 535


def listed():
    """Independent witness: is the name in the pipe namespace -- what the fix's PipeExists enumerates."""
    try:
        return NAME.lower() in (n.lower() for n in os.listdir("\\\\.\\pipe\\"))
    except OSError as e:
        return "cannot list: %s" % e


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--seconds", type=float, default=60.0)
    a = ap.parse_args()

    print("before: %s listed in the pipe namespace: %s" % (PATH, listed()))
    srv = k.CreateNamedPipeW(PATH, PIPE_ACCESS_DUPLEX, PIPE_TYPE_BYTE_WAIT, 1, 65536, 65536, 0, None)
    if srv in (None, INVALID):
        print("REFUSED: CreateNamedPipeW failed (err %d) -- another server (Cheat Engine's AOBMaker plugin?) "
              "owns the name; close it first." % ctypes.get_last_error())
        return 2
    cli = None
    try:
        cli = k.CreateFileW(PATH, GENERIC_RW, 0, None, OPEN_EXISTING, 0, None)
        if cli in (None, INVALID):
            print("FAILED: our own client could not occupy the instance (err %d)" % ctypes.get_last_error())
            return 1
        ok = k.ConnectNamedPipe(srv, None)
        err = ctypes.get_last_error()
        state = "connected" if ok or err == ERROR_PIPE_CONNECTED else "ConnectNamedPipe err %d" % err
        print("HOLDING: 1 instance, occupied by our own client (%s); listed: %s; for %.0f s"
              % (state, listed(), a.seconds))
        sys.stdout.flush()
        time.sleep(a.seconds)
    finally:
        if cli not in (None, INVALID):
            k.CloseHandle(cli)
        k.CloseHandle(srv)
    print("released; listed: %s" % listed())
    return 0


if __name__ == "__main__":
    sys.exit(main())
