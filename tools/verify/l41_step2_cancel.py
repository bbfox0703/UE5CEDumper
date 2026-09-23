#!/usr/bin/env python3
r"""L41 step 2 `[P1-ENUMNAMES]`: a UEnum::Names search CANCELLED mid-flight must not latch FAILED, and the next
walk must detect the names -- on DumperTest58 (UE 5.8), where the default Legacy names format is WRONG.

    py tools/verify/l41_step2_cancel.py [--process DumperTest58-Win64-Shipping] [--hold 1.0]

PRECONDITIONS (the rig refuses or reports vacuity otherwise): a FRESH game process (the detection flags latch per
process), injected with a staged DLL that stalls the first DetectUEnumNames search 3 s --
`out\staged\l41-step2-first-search-stall` (HEAD's fix, green) or `out\staged\l41-step2-prefix-stall` (7e5a71fc
reversed + defcb56e's guards removed, red). NO UI connected: it would make the first enum lookup itself.

WHAT IT DOES. A PipeClient resolves the live DumperTest58Actor (find_instances touches no enum) and disconnects.
A RAW pipe connection (CreateFileW on \\.\pipe\UE5DumpBfx) then sends walk_instance on it -- the process's first
enum lookup, so detection starts and sits in the staged stall -- waits `--hold` seconds and closes the handle.
The server's monitor must log `client gone mid-command` in pipe-0.log (anti-vacuity: absent means the close was
never seen in flight and the run measured nothing). After the stall, a fresh PipeClient walks the same actor
again. The rig reports, from the lines each log gained during the run, and from that second walk:

  * offsets-0.log: `search cancelled ... not latching FAILED` (fixed) vs `DetectUEnumNames: FAILED` (pre-fix);
    `UEnum::Names detected at UEnum+0x40 (UE5.6+ FNameData ...)` after the reconnect walk (fixed);
  * walk-0.log: `has no cached table -- truncated read, retry pending` must be ABSENT;
  * the second walk's enum fields (Role / RemoteRole ...): a non-empty `enum_name` and `enum_entries`.
"""
import argparse
import ctypes
import json
import os
import sys
import time
from ctypes import wintypes

sys.stdout.reconfigure(encoding="utf-8", errors="replace")
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from pipe_client import PipeClient  # noqa: E402

PIPE = r"\\.\pipe\UE5DumpBfx"
GENERIC_RW = 0x80000000 | 0x40000000
OPEN_EXISTING = 3
INVALID = ctypes.c_void_p(-1).value

k = ctypes.WinDLL("kernel32", use_last_error=True)
k.CreateFileW.restype = wintypes.HANDLE
k.CreateFileW.argtypes = [wintypes.LPCWSTR, wintypes.DWORD, wintypes.DWORD, ctypes.c_void_p, wintypes.DWORD,
                          wintypes.DWORD, wintypes.HANDLE]
k.WriteFile.argtypes = [wintypes.HANDLE, ctypes.c_void_p, wintypes.DWORD, ctypes.POINTER(wintypes.DWORD),
                        ctypes.c_void_p]
k.CloseHandle.argtypes = [wintypes.HANDLE]


def say(s=""):
    print(s, flush=True)


def log_path(process, name):
    return os.path.join(os.environ["LOCALAPPDATA"], "UE5CEDumper", "Logs", process, name)


def size_of(p):
    try:
        return os.path.getsize(p)
    except OSError:
        return 0


def grown(p, start):
    try:
        with open(p, "rb") as f:
            f.seek(start)
            return f.read().decode("utf-8", "replace").splitlines()
    except OSError:
        return []


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--process", default="DumperTest58-Win64-Shipping")
    ap.add_argument("--hold", type=float, default=1.0, help="seconds the raw connection stays open mid-command")
    a = ap.parse_args()
    logs = {n: log_path(a.process, n) for n in ("pipe-0.log", "offsets-0.log", "walk-0.log")}
    mark = {n: size_of(p) for n, p in logs.items()}
    already = [l for l in grown(logs["offsets-0.log"], 0) if "DetectUEnumNames" in l or "UEnum::Names detected" in l]
    if already:
        raise SystemExit("REFUSED: offsets-0.log already has enum detection lines -- not a fresh process:\n  %s"
                         % already[-1][:160])
    with PipeClient() as c:
        say("build %s" % c.assert_build())
        c.ensure_scanned()
        r = c.request("find_instances", class_name="DumperTest58Actor", exact_match=True, limit=10)
        live = next((i for i in (r.get("instances") or []) if not i["name"].startswith("Default__")), None)
    if not live:
        raise SystemExit("FIXTURE: no live DumperTest58Actor")
    say("actor %s @ %s" % (live["name"], live["addr"]))

    h = k.CreateFileW(PIPE, GENERIC_RW, 0, None, OPEN_EXISTING, 0, None)
    if not h or h == INVALID:
        raise SystemExit("FAIL: CreateFileW(%s) err=%d" % (PIPE, ctypes.get_last_error()))
    line = (json.dumps({"cmd": "walk_instance", "addr": live["addr"], "id": 1}) + "\n").encode()
    n = wintypes.DWORD(0)
    ok = k.WriteFile(h, line, len(line), ctypes.byref(n), None)
    t0 = time.time()
    time.sleep(a.hold)
    k.CloseHandle(h)
    say("raw walk_instance sent (%s, %d bytes), handle closed after %.2f s" % (bool(ok), n.value, time.time() - t0))
    time.sleep(4.0)   # the staged stall is 3 s; let the dead walk run out

    pipe_new = grown(logs["pipe-0.log"], mark["pipe-0.log"])
    gone = [l for l in pipe_new if "client gone mid-command" in l]
    say("pipe-0.log 'client gone mid-command': %d" % len(gone))
    for l in gone[:2]:
        say("   " + l.strip()[:200])
    off1 = grown(logs["offsets-0.log"], mark["offsets-0.log"])
    for l in off1:
        if "DetectUEnumNames" in l or "UEnum::Names" in l:
            say("   offsets: " + l.strip()[:220])

    with PipeClient() as c:
        w = c.request("walk_instance", addr=live["addr"], array_limit=0)
    enums = [f for f in (w.get("fields") or []) if f.get("name") in ("Role", "RemoteRole")]
    for f in enums:
        say("second walk %-10s value=%r enum_name=%r entries=%d" % (f.get("name"), f.get("value"), f.get("enum_name"),
                                                                    len(f.get("enum_entries") or [])))
    time.sleep(0.5)
    off = grown(logs["offsets-0.log"], mark["offsets-0.log"])
    walk = grown(logs["walk-0.log"], mark["walk-0.log"])
    cancelled = [l for l in off if "search cancelled" in l]
    failed = [l for l in off if "DetectUEnumNames: FAILED" in l]
    detected = [l for l in off if "UEnum::Names detected" in l]
    trunc = [l for l in walk if "retry pending" in l]
    for l in detected[:1]:
        say("   offsets: " + l.strip()[:220])
    say("offsets-0.log: 'search cancelled' x%d, 'DetectUEnumNames: FAILED' x%d, 'UEnum::Names detected' x%d"
        % (len(cancelled), len(failed), len(detected)))
    say("walk-0.log: 'retry pending' x%d" % len(trunc))
    named = [f for f in enums if f.get("enum_name") and f.get("enum_entries")]
    if not gone:
        say("READING: VACUOUS -- the close was never seen in flight")
        return 1
    say("READING: %s" % ("cancel NOT latched, names detected on reconnect" if cancelled and not failed and detected
                         and named else "FAILED latched / names missing" if failed or not named else "mixed"))
    return 0


if __name__ == "__main__":
    sys.exit(main())
