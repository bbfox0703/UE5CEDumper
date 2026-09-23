#!/usr/bin/env python3
r"""L79 `[W4-HEXSORT]`: MANUFACTURE the inverting address pair this machine never produces, for Live Walker's [Ptr].

    py tools/verify/l79_lowclone.py make    --state out\l79_clone.json   (pipe; UI CLOSED)
    py tools/verify/l79_lowclone.py predict --state out\l79_clone.json   (pipe; UI CLOSED)
    py tools/verify/l79_lowclone.py release --state out\l79_clone.json   (no pipe; teardown)

WHY MANUFACTURE IT. The fix sorts address columns numerically; the pre-fix grids sorted the hex TEXT. The two
orders differ only for a pair of different digit counts whose SHORTER address has the larger leading digit,
e.g. 0x7A00000000 (10 digits) against 0x278671D9A00 (11). Measured 2026-09-23 on DumperTest Shipping 5.4: every
address on every surface was 11 digits (4,100 DumperTestHolder instances, both 4,096-element pointer arrays, the
actor's pointer fields, its 34 UFunctions), so no grid could fail on natural data.

`make` copies the live DumperTestActor (`props_size` bytes) into memory it allocates at a fixed LOW address
(first free one of CANDIDATES, all 10 digits with a leading digit above 2), then overwrites ONE ObjectProperty
of the copy (`--poke`, default Table_Small) with the copy's own address. The game never references the copy, so
nothing in the engine reads or collects it, and the live actor is untouched. Walking the copy in Live Walker
(Go to <clone>) then shows a [Ptr] column holding five 11-digit pointers copied from the actor plus one 10-digit
pointer: an inverting pair. `predict` walks the copy the way the grid does and prints the order the [Ptr]
column must show, numerically and as text, ascending and descending. Empty [Ptr] cells sort as 0 in both
orders, so they lead ascending and trail descending; DESCENDING row 1 decides it at a glance. It also prints the
Hex column's text order (row step 2), which the fix leaves as text on purpose. `release` frees the allocation.
"""
import argparse
import ctypes
import json
import os
import struct
import sys
from ctypes import wintypes

sys.stdout.reconfigure(encoding="utf-8", errors="replace")
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

MEM_COMMIT, MEM_RESERVE, MEM_RELEASE = 0x1000, 0x2000, 0x8000
PAGE_RW = 0x04
PROCESS_ACCESS = 0x0008 | 0x0020 | 0x0010 | 0x0400   # VM_OPERATION | VM_WRITE | VM_READ | QUERY_INFORMATION
CANDIDATES = (0x7A00000000, 0x7B00000000, 0x6A00000000, 0x5A00000000, 0x9A00000000, 0xAA00000000)
REGION = 0x10000

k = ctypes.WinDLL("kernel32", use_last_error=True)
k.OpenProcess.restype = wintypes.HANDLE
k.VirtualAllocEx.restype = ctypes.c_void_p
k.VirtualAllocEx.argtypes = [wintypes.HANDLE, ctypes.c_void_p, ctypes.c_size_t, wintypes.DWORD, wintypes.DWORD]
k.VirtualFreeEx.argtypes = [wintypes.HANDLE, ctypes.c_void_p, ctypes.c_size_t, wintypes.DWORD]
k.WriteProcessMemory.argtypes = [wintypes.HANDLE, ctypes.c_void_p, ctypes.c_void_p, ctypes.c_size_t,
                                 ctypes.POINTER(ctypes.c_size_t)]
k.ReadProcessMemory.argtypes = [wintypes.HANDLE, ctypes.c_void_p, ctypes.c_void_p, ctypes.c_size_t,
                                ctypes.POINTER(ctypes.c_size_t)]


def open_proc(pid):
    h = k.OpenProcess(PROCESS_ACCESS, False, pid)
    if not h:
        raise SystemExit("OpenProcess(%d) failed err=%d" % (pid, ctypes.get_last_error()))
    return h


def write(h, addr, data):
    buf = (ctypes.c_ubyte * len(data)).from_buffer_copy(data)
    n = ctypes.c_size_t(0)
    if not k.WriteProcessMemory(h, ctypes.c_void_p(addr), buf, len(data), ctypes.byref(n)) or n.value != len(data):
        raise SystemExit("FAIL: WriteProcessMemory(0x%X) err=%d" % (addr, ctypes.get_last_error()))


def read(h, addr, size):
    buf = (ctypes.c_ubyte * size)()
    n = ctypes.c_size_t(0)
    if not k.ReadProcessMemory(h, ctypes.c_void_p(addr), buf, size, ctypes.byref(n)) or n.value != size:
        raise SystemExit("FAIL: ReadProcessMemory(0x%X, %d) err=%d" % (addr, size, ctypes.get_last_error()))
    return bytes(buf)


def make(a):
    import mailbox_poke as MP
    from pipe_client import PipeClient
    from ad4_contested import find_live_actor
    pid = MP.pid_of(a.process)
    with PipeClient() as c:
        print("build %s" % c.assert_build())
        c.ensure_scanned()
        act = find_live_actor(c)
        w = c.request("walk_instance", addr=act["addr"], array_limit=0)
        size = int(w.get("props_size") or 0)
        f = next((x for x in (w.get("fields") or []) if x.get("name") == a.poke), None)
    if not f or f.get("type") not in ("ObjectProperty", "ClassProperty"):
        raise SystemExit("FAIL: %s has no ObjectProperty %s" % (act["name"], a.poke))
    if not 0x30 <= size <= REGION:
        raise SystemExit("FAIL: props_size %d does not fit the 0x%X region" % (size, REGION))
    src = int(act["addr"], 16)
    h = open_proc(pid)
    try:
        body = read(h, src, size)
        p = None
        for cand in CANDIDATES:
            got = k.VirtualAllocEx(h, ctypes.c_void_p(cand), REGION, MEM_COMMIT | MEM_RESERVE, PAGE_RW)
            if got:
                p = got
                break
            print("   0x%X taken (err=%d)" % (cand, ctypes.get_last_error()))
        if not p:
            raise SystemExit("FAIL: no free low candidate")
        write(h, p, body)
        off = int(f["offset"], 16) if isinstance(f["offset"], str) else int(f["offset"])
        was = struct.unpack("<Q", read(h, p + off, 8))[0]
        write(h, p + off, struct.pack("<Q", p))
        now = struct.unpack("<Q", read(h, p + off, 8))[0]
    finally:
        k.CloseHandle(h)
    print("CLONE = 0x%X  (%d bytes of %s %s)" % (p, size, act["name"], act["addr"]))
    print("poked clone.%s @ +0x%X: 0x%X -> 0x%X (the clone itself)" % (a.poke, off, was, now))
    json.dump({"pid": pid, "clone": "0x%X" % p, "source": act["addr"], "size": size, "poke": a.poke,
               "poke_offset": off}, open(a.state, "w"), indent=1)
    print("state -> %s" % a.state)
    return 0


def order(rows, key, reverse):
    return [r for r in sorted(rows, key=key, reverse=reverse)]


def predict(a):
    from pipe_client import PipeClient
    st = json.load(open(a.state))
    with PipeClient() as c:
        c.assert_build()
        w = c.request("walk_instance", addr=st["clone"], array_limit=0)
    fields = w.get("fields") or []
    print("walk %s: %s fields, name=%s class=%s" % (st["clone"], len(fields), w.get("name"), w.get("class_name")))
    ptr = [(f.get("name"), f.get("ptr") or "") for f in fields]
    ptr = [(n, p if p and p != "0x0" else "") for n, p in ptr]
    nonempty = [x for x in ptr if x[1]]
    print("[Ptr] non-empty rows (%d of %d):" % (len(nonempty), len(ptr)))
    for n, p in nonempty:
        print("   %-24s %s (%d digits)" % (n, p, len(p) - 2))
    num = lambda x: int(x[1], 16) if x[1] else 0
    txt = lambda x: x[1]
    for label, key in (("NUMERIC (fixed)", num), ("TEXT (pre-fix)", txt)):
        d = order(ptr, key, True)[:3]
        asc = [x for x in order(ptr, key, False) if x[1]]
        print("%-16s desc rows 1-3: %s" % (label, ["%s %s" % x for x in d]))
        print("%-16s asc first non-empty: %s ; asc LAST: %s" % ("", "%s %s" % asc[0], "%s %s" % asc[-1]))
    hexs = [(f.get("name"), f.get("hex") or "") for f in fields]
    print("Hex (text order, row step 2): asc 1-3 %s" % ["%s=%r" % x for x in sorted(hexs, key=lambda x: x[1])[:3]])
    print("                              desc 1-3 %s" % ["%s=%r" % x for x in sorted(hexs, key=lambda x: x[1], reverse=True)[:3]])
    return 0


def release(a):
    st = json.load(open(a.state))
    h = open_proc(st["pid"])
    try:
        if not k.VirtualFreeEx(h, ctypes.c_void_p(int(st["clone"], 16)), 0, MEM_RELEASE):
            raise SystemExit("FAIL: VirtualFreeEx(MEM_RELEASE) err=%d" % ctypes.get_last_error())
    finally:
        k.CloseHandle(h)
    print("released %s" % st["clone"])
    return 0


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("phase", choices=("make", "predict", "release"))
    ap.add_argument("--process", default="DumperTest-Win64-Shipping")
    ap.add_argument("--state", required=True, help="JSON file the phases share (put it under out\\)")
    ap.add_argument("--poke", default="Table_Small", help="ObjectProperty of the copy to point at the copy")
    a = ap.parse_args()
    return {"make": make, "predict": predict, "release": release}[a.phase](a)


if __name__ == "__main__":
    sys.exit(main())
