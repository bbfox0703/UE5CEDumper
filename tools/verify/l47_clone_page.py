#!/usr/bin/env python3
r"""L47 `[P1-WALK-UNREADABLE]` `[A4-REROOT-STALE-WARNING]`: a MANUFACTURED freed object for Live Walker.

    py tools/verify/l47_clone_page.py make --process DumperTest-Win64-Shipping --state out\l47_page.json   (UI CLOSED)
    py tools/verify/l47_clone_page.py free --state out\l47_page.json                                      (UI may be open)
    py tools/verify/l47_clone_page.py release --state out\l47_page.json                                   (teardown)

WHY MANUFACTURE IT. The DLL reports `unreadable` only when the object's header fails Macht::IsAddrReadable:
VirtualQuery state != MEM_COMMIT, or no read permission (Ubel.cpp WalkInstance, Macht.cpp). A UObject the game
destroys stays in a binned page that remains COMMITTED, so it walks as a zombie, a recycled object or the stale
text -- never "no longer readable". So the freed object is built: `make` copies a real small UObject (the
DumperTestPayload that the live DumperTestActor's `Payload` points at, `props_size` bytes) into a fresh page the
game never references, and `free` DECOMMITS that page. dll_core_test's "reserved, uncommitted page" shape, live.

⚠ DECOMMIT, NEVER RELEASE, until the check is done (measured 2026-09-23). A MEM_RELEASE hands the 64 KB region
back, and a running game re-committed it within ~25 s: the next Refresh then walked a recycled ZERO page (header
all zeros, ClassPrivate 0), which reads as a silent blank 'None' walk -- not the freed-object branch under test.
MEM_DECOMMIT keeps the address RESERVED, so nothing else can be placed there, and the header reads MEM_RESERVE.
`release` frees the reservation afterwards.

`make` needs the pipe (find_instances + walk_instance + read_mem), so it runs BEFORE the UI connects: the UI
holds 2 of the 3 pipe instances. `free` is out of process (VirtualFreeEx + VirtualQuery) and never opens the
pipe, so it is safe while the UI is connected. The game never reads the page, so releasing it cannot fault it.
"""
import argparse
import ctypes
import json
import os
import sys
from ctypes import wintypes

sys.stdout.reconfigure(encoding="utf-8", errors="replace")
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

MEM_COMMIT, MEM_RESERVE, MEM_DECOMMIT, MEM_RELEASE = 0x1000, 0x2000, 0x4000, 0x8000
PAGE_RW = 0x04
PROCESS_ACCESS = 0x0008 | 0x0020 | 0x0010 | 0x0400   # VM_OPERATION | VM_WRITE | VM_READ | QUERY_INFORMATION
STATES = {0x1000: "MEM_COMMIT", 0x2000: "MEM_RESERVE", 0x10000: "MEM_FREE"}

k = ctypes.WinDLL("kernel32", use_last_error=True)
k.OpenProcess.restype = wintypes.HANDLE
k.VirtualAllocEx.restype = ctypes.c_void_p
k.VirtualAllocEx.argtypes = [wintypes.HANDLE, ctypes.c_void_p, ctypes.c_size_t, wintypes.DWORD, wintypes.DWORD]
k.VirtualFreeEx.argtypes = [wintypes.HANDLE, ctypes.c_void_p, ctypes.c_size_t, wintypes.DWORD]
k.WriteProcessMemory.argtypes = [wintypes.HANDLE, ctypes.c_void_p, ctypes.c_void_p, ctypes.c_size_t,
                                 ctypes.POINTER(ctypes.c_size_t)]
k.ReadProcessMemory.argtypes = [wintypes.HANDLE, ctypes.c_void_p, ctypes.c_void_p, ctypes.c_size_t,
                                ctypes.POINTER(ctypes.c_size_t)]


class MBI(ctypes.Structure):
    _fields_ = [("BaseAddress", ctypes.c_void_p), ("AllocationBase", ctypes.c_void_p),
                ("AllocationProtect", wintypes.DWORD), ("PartitionId", wintypes.WORD),
                ("RegionSize", ctypes.c_size_t), ("State", wintypes.DWORD), ("Protect", wintypes.DWORD),
                ("Type", wintypes.DWORD)]


k.VirtualQueryEx.argtypes = [wintypes.HANDLE, ctypes.c_void_p, ctypes.POINTER(MBI), ctypes.c_size_t]
k.VirtualQueryEx.restype = ctypes.c_size_t


def open_proc(pid):
    h = k.OpenProcess(PROCESS_ACCESS, False, pid)
    if not h:
        raise SystemExit("OpenProcess(%d) failed err=%d" % (pid, ctypes.get_last_error()))
    return h


def query(h, addr):
    m = MBI()
    if not k.VirtualQueryEx(h, ctypes.c_void_p(addr), ctypes.byref(m), ctypes.sizeof(m)):
        return "VirtualQueryEx failed err=%d" % ctypes.get_last_error()
    return "%s protect=0x%X base=0x%X size=0x%X" % (STATES.get(m.State, hex(m.State)), m.Protect,
                                                     m.BaseAddress or 0, m.RegionSize)


def pid_of(stem):
    import mailbox_poke as MP
    return MP.pid_of(stem)


def make(a):
    from pipe_client import PipeClient
    pid = pid_of(a.process)
    with PipeClient() as c:
        print("build %s" % c.assert_build())
        c.ensure_scanned()
        insts = c.request("find_instances", class_name="DumperTestActor", exact_match=True,
                          limit=10).get("instances") or []
        actor = next((i for i in insts if not i["name"].startswith("Default__")), None)
        if not actor:
            raise SystemExit("FAIL: no live DumperTestActor")
        w = c.request("walk_instance", addr=actor["addr"], array_limit=0)
        f = next((x for x in (w.get("fields") or []) if x.get("name") == "Payload"), None)
        if not f or not f.get("ptr") or int(f["ptr"], 16) == 0:
            raise SystemExit("FAIL: %s has no non-null Payload" % actor["name"])
        x = int(f["ptr"], 16)
        wx = c.request("walk_instance", addr=f["ptr"], array_limit=0)
        size = int(wx.get("props_size") or 0)
        print("actor %s @ %s; Payload -> 0x%X (%s / %s), props_size %d, %d fields"
              % (actor["name"], actor["addr"], x, f.get("ptr_name"), f.get("ptr_class"), size,
                 len(wx.get("fields") or [])))
    if not 0x30 <= size <= 0x1000:
        raise SystemExit("FAIL: props_size %d is not a small object that fits one page" % size)
    h = open_proc(pid)
    try:
        buf = (ctypes.c_ubyte * size)()
        got = ctypes.c_size_t(0)
        if not k.ReadProcessMemory(h, ctypes.c_void_p(x), buf, size, ctypes.byref(got)) or got.value != size:
            raise SystemExit("FAIL: ReadProcessMemory(0x%X, %d) err=%d" % (x, size, ctypes.get_last_error()))
        p = k.VirtualAllocEx(h, None, 0x1000, MEM_COMMIT | MEM_RESERVE, PAGE_RW)
        if not p:
            raise SystemExit("FAIL: VirtualAllocEx err=%d" % ctypes.get_last_error())
        wrote = ctypes.c_size_t(0)
        if not k.WriteProcessMemory(h, ctypes.c_void_p(p), buf, size, ctypes.byref(wrote)) or wrote.value != size:
            raise SystemExit("FAIL: WriteProcessMemory err=%d" % ctypes.get_last_error())
        print("CLONE P = 0x%X  (%d bytes of 0x%X)  %s" % (p, size, x, query(h, p)))
    finally:
        k.CloseHandle(h)
    json.dump({"pid": pid, "page": "0x%X" % p, "source": "0x%X" % x, "size": size,
               "actor": actor["addr"]}, open(a.state, "w"), indent=1)
    print("state -> %s" % a.state)
    return 0


def free(a):
    st = json.load(open(a.state))
    pid, p = st["pid"], int(st["page"], 16)
    h = open_proc(pid)
    try:
        print("before: 0x%X %s" % (p, query(h, p)))
        if not k.VirtualFreeEx(h, ctypes.c_void_p(p), 0x1000, MEM_DECOMMIT):
            raise SystemExit("FAIL: VirtualFreeEx(MEM_DECOMMIT) err=%d" % ctypes.get_last_error())
        after = query(h, p)
        print("after:  0x%X %s" % (p, after))
    finally:
        k.CloseHandle(h)
    if not after.startswith("MEM_RESERVE"):
        print("FAIL: want MEM_RESERVE (decommitted, address still held)")
        return 1
    print("DECOMMITTED (reserved)")
    return 0


def release(a):
    st = json.load(open(a.state))
    pid, p = st["pid"], int(st["page"], 16)
    h = open_proc(pid)
    try:
        if not k.VirtualFreeEx(h, ctypes.c_void_p(p), 0, MEM_RELEASE):
            raise SystemExit("FAIL: VirtualFreeEx(MEM_RELEASE) err=%d" % ctypes.get_last_error())
        print("released: 0x%X %s" % (p, query(h, p)))
    finally:
        k.CloseHandle(h)
    return 0


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("phase", choices=("make", "free", "release"))
    ap.add_argument("--process", default="DumperTest-Win64-Shipping")
    ap.add_argument("--state", required=True, help="JSON file the two phases share (put it under out\\)")
    a = ap.parse_args()
    return {"make": make, "free": free, "release": release}[a.phase](a)


if __name__ == "__main__":
    sys.exit(main())
