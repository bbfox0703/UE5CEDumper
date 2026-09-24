#!/usr/bin/env python3
r"""L79 `[W4-HEXSORT]`: MANUFACTURE the inverting address pair this machine never produces, for Live Walker's grids.

    py tools/verify/l79_lowclone.py make    --state out\l79_clone.json [--funcs]   (pipe; UI CLOSED)
    py tools/verify/l79_lowclone.py predict --state out\l79_clone.json             (pipe; UI CLOSED)
    py tools/verify/l79_lowclone.py release --state out\l79_clone.json             (no pipe; teardown)

WHY MANUFACTURE IT. The fix sorts address columns numerically; the pre-fix grids sorted the hex TEXT. The two
orders differ only for a pair of different digit counts whose SHORTER address has the larger leading digit,
e.g. 0x7A00000000 (10 digits) against 0x278671D9A00 (11). Measured 2026-09-23 on DumperTest Shipping 5.4: every
address on every surface was 11 digits (4,100 DumperTestHolder instances, both 4,096-element pointer arrays, the
actor's pointer fields, its 34 UFunctions, its 181 FFields, the Find Refs owners), so no grid could fail on
natural data.

`make` copies the live DumperTestActor (`props_size` bytes) into memory it allocates at a fixed LOW address
(first free one of CANDIDATES, all 10 digits with a leading digit above 2), then overwrites ONE ObjectProperty
of the copy (`--poke`, default Table_Small) with the copy's own address. The game never references the copy, so
nothing in the engine reads or collects it, and the rig never writes the live actor. Walking the copy in Live
Walker (Go to <clone>) then shows a [Ptr] column holding five 11-digit pointers copied from the actor plus one
10-digit pointer: an inverting pair. `predict` walks the copy the way the grid does and prints the order the
[Ptr] column must show, numerically and as text, ascending and descending, and FAILS (exit 1) when the two orders
coincide, because a green run on such data proves nothing. Empty [Ptr] cells sort first in both orders (0
numerically, "" as text), so they lead ascending and trail descending; DESCENDING row 1 decides it at a glance.
It also prints the Hex column's text order (row step 2), which the fix leaves as text on purpose.

`make --funcs` does the same for Live Walker's Functions grid. Live Walker lists the functions of the walked
object's CLASS (walk_functions on its ClassPrivate), and the DLL follows that class's UStruct::Children chain.
So the rig also copies the actor's UClass (CLASS_BYTES) to clone+CLASS_AT and the head of its Children chain, a
UFunction (FUNC_BYTES), to clone+FUNC_AT. The class copy's Children then points at the function copy, whose
copied UField::Next carries on into the real chain. The actor copy's ClassPrivate points at the class copy.
The Functions grid then lists the same functions, one of them at a 10-digit address.

SAFETY. Everything is validated BEFORE the allocation: ClassPrivate against the pipe's class_addr (also the proof
that --process is the process the pipe answered from), every write offset against the copy's bounds, and under
--funcs a non-null Children head that walk_functions lists as a function. The state file is written as soon as
the region exists, and any failure after that frees it, so a failed `make` never leaves a region behind.
`release` frees only a region that still looks like ours: the recorded process, a private committed 64 KB
allocation at the recorded base, and the recorded ClassPrivate qword at +0x10.
⚠ WALK AND SORT ONLY. Never Invoke, Force or write a value through the copy. Its pointer fields alias the live
actor's subobjects, and ProcessEvent on it would run game code over a fake object. Navigate Live Walker away
(or close the UI) before `release`, or a later auto-refresh walks freed memory.
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

MEM_COMMIT, MEM_RESERVE, MEM_RELEASE, MEM_PRIVATE = 0x1000, 0x2000, 0x8000, 0x20000
PAGE_RW = 0x04
PROCESS_ACCESS = 0x0008 | 0x0020 | 0x0010 | 0x0400   # VM_OPERATION | VM_WRITE | VM_READ | QUERY_INFORMATION
CANDIDATES = (0x7A00000000, 0x7B00000000, 0x6A00000000, 0x5A00000000, 0x9A00000000, 0xAA00000000)
REGION = 0x10000                      # one allocation granule; every candidate is 64 KB aligned
UOBJECT_CLASS = 0x10                  # UObjectBase::ClassPrivate (Grimoire::OFF_UOBJECT_CLASS); verified first
# UClass is 0x200 on 5.3-5.6 (0x230 on 5.0/5.1, 0x220 on 5.2, 0x208/0x210 on 5.7/5.8) per vendor/RE-UE4SS
# MemberVariableLayout_5_0x templates; the DLL reads only UObject 0x10-0x27 and UStruct 0x40-0x5F of a class.
CLASS_AT, CLASS_BYTES = 0x2000, 0x240
FUNC_AT, FUNC_BYTES = 0x3000, 0x100   # UFunction is 0xE0 in every 5.x template

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


def q(b, off):
    return struct.unpack("<Q", b[off:off + 8])[0]


def make(a):
    import mailbox_poke as MP
    from pipe_client import PipeClient
    from ad4_contested import find_live_actor
    st_path = os.path.abspath(a.state)
    if os.path.exists(st_path) and not json.load(open(st_path)).get("released", False):
        raise SystemExit("REFUSED: %s names a region that was never released -- run `release` first" % st_path)
    os.makedirs(os.path.dirname(st_path), exist_ok=True)
    pid = MP.pid_of(a.process)
    with PipeClient() as c:
        print("build %s" % c.assert_build())
        c.ensure_scanned()
        act = find_live_actor(c)
        w = c.request("walk_instance", addr=act["addr"], array_limit=0)
        size = int(w.get("props_size") or 0)
        f = next((x for x in (w.get("fields") or []) if x.get("name") == a.poke), None)
        children_off = int(c.request("get_offsets").get("ustruct_children") or 0)
        class_addr = int(w.get("class_addr") or act["class_addr"], 16)
        func_addrs = {int(x["addr"], 16) for x in c.request("walk_functions", addr="0x%X" % class_addr)
                      .get("functions") or [] if x.get("addr")} if a.funcs else set()
    # ---- validate everything BEFORE allocating ----
    if not f or f.get("type") not in ("ObjectProperty", "ClassProperty"):
        raise SystemExit("FAIL: %s has no ObjectProperty %s" % (act["name"], a.poke))
    limit = CLASS_AT if a.funcs else REGION
    if not 0x30 <= size <= limit:
        raise SystemExit("FAIL: props_size %d does not fit below 0x%X" % (size, limit))
    off = int(f["offset"], 16) if isinstance(f["offset"], str) else int(f["offset"])
    if not (UOBJECT_CLASS + 8 <= off and off + 8 <= size):
        raise SystemExit("FAIL: %s offset 0x%X is outside the copy (0x%X bytes)" % (a.poke, off, size))
    src = int(act["addr"], 16)
    h = open_proc(pid)
    try:
        body = read(h, src, size)
        if q(body, UOBJECT_CLASS) != class_addr:
            raise SystemExit("FAIL: ClassPrivate@+0x%X = 0x%X but the pipe says class_addr 0x%X -- is --process the"
                             " game the pipe answered from?" % (UOBJECT_CLASS, q(body, UOBJECT_CLASS), class_addr))
        cls = fn = head = None
        if a.funcs:
            if not 0 < children_off <= CLASS_BYTES - 8:
                raise SystemExit("FAIL: ustruct_children %d outside the class copy" % children_off)
            head = q(read(h, class_addr + children_off, 8), 0)
            if not head or head not in func_addrs:
                raise SystemExit("FAIL: Children head 0x%X is not a function walk_functions lists" % head)
            cls, fn = read(h, class_addr, CLASS_BYTES), read(h, head, FUNC_BYTES)
        # ---- allocate, record, then write; any failure frees the region ----
        p = None
        for cand in CANDIDATES:
            got = k.VirtualAllocEx(h, ctypes.c_void_p(cand), REGION, MEM_COMMIT | MEM_RESERVE, PAGE_RW)
            if got:
                if got != cand:
                    k.VirtualFreeEx(h, ctypes.c_void_p(got), 0, MEM_RELEASE)
                    raise SystemExit("FAIL: asked for 0x%X, got 0x%X" % (cand, got))
                p = got
                break
            print("   0x%X taken (err=%d)" % (cand, ctypes.get_last_error()))
        if not p:
            raise SystemExit("FAIL: no free low candidate")
        stamp = p + CLASS_AT if a.funcs else class_addr
        st = {"pid": pid, "process": a.process, "clone": "0x%X" % p, "source": act["addr"], "size": size,
              "poke": a.poke, "poke_offset": off, "class_private": "0x%X" % stamp, "funcs": None, "released": False}
        json.dump(st, open(st_path, "w"), indent=1)
        try:
            write(h, p, body)
            write(h, p + off, struct.pack("<Q", p))
            if a.funcs:
                write(h, p + CLASS_AT, cls)
                write(h, p + FUNC_AT, fn)
                write(h, p + CLASS_AT + children_off, struct.pack("<Q", p + FUNC_AT))
                write(h, p + UOBJECT_CLASS, struct.pack("<Q", p + CLASS_AT))
                st["funcs"] = {"class_copy": "0x%X" % (p + CLASS_AT), "func_copy": "0x%X" % (p + FUNC_AT),
                               "class": "0x%X" % class_addr, "children_head": "0x%X" % head}
        except BaseException:
            k.VirtualFreeEx(h, ctypes.c_void_p(p), 0, MEM_RELEASE)
            st["released"] = True
            json.dump(st, open(st_path, "w"), indent=1)
            raise
        now = q(read(h, p + off, 8), 0)
    finally:
        k.CloseHandle(h)
    json.dump(st, open(st_path, "w"), indent=1)
    print("CLONE = 0x%X  (%d bytes of %s %s)" % (p, size, act["name"], act["addr"]))
    print("poked clone.%s @ +0x%X -> 0x%X (the clone itself)" % (a.poke, off, now))
    if st["funcs"]:
        print("class copy %(class_copy)s of %(class)s; its Children -> function copy %(func_copy)s of %(children_head)s;"
              " clone.ClassPrivate -> class copy" % st["funcs"])
    print("state -> %s" % st_path)
    return 0


def order(rows, key, reverse):
    return [r for r in sorted(rows, key=key, reverse=reverse)]


def inverts(rows):
    num = lambda x: int(x[1], 16) if x[1] else 0
    return [x[1] for x in order(rows, num, True)] != [x[1] for x in order(rows, lambda x: x[1], True)]


def predict(a):
    from pipe_client import PipeClient
    st = json.load(open(a.state))
    if st.get("released"):
        raise SystemExit("FAIL: %s was released" % st["clone"])
    with PipeClient() as c:
        c.assert_build()
        w = c.request("walk_instance", addr=st["clone"], array_limit=0)
        fl = c.request("walk_functions", addr=w["class_addr"]).get("functions") if st.get("funcs") else None
    bad = 0
    if fl is not None:
        rows = [(x.get("name"), x.get("addr") or "") for x in fl]
        short = [r for r in rows if len(r[1]) - 2 == 10]
        print("Functions of class_addr %s: %d rows, digits %s" % (w.get("class_addr"), len(rows),
                                                                  sorted({len(r[1]) - 2 for r in rows})))
        for label, key in (("NUMERIC (fixed)", lambda x: int(x[1], 16) if x[1] else 0),
                           ("TEXT (pre-fix)", lambda x: x[1])):
            print("%-16s Address desc rows 1-3: %s" % (label, ["%s %s" % x for x in order(rows, key, True)[:3]]))
            print("%-16s Address asc  rows 1-3: %s" % ("", ["%s %s" % x for x in order(rows, key, False)[:3]]))
        if len(short) != 1 or not inverts(rows):
            print("FAIL: Functions want exactly one 10-digit row and differing orders (got %d)" % len(short))
            bad = 1
    fields = w.get("fields") or []
    print("walk %s: %s fields, name=%s class=%s" % (st["clone"], len(fields), w.get("name"), w.get("class")))
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
    if not inverts(ptr):
        print("FAIL: [Ptr] text and numeric orders coincide -- no inverting pair, a green run would prove nothing")
        bad = 1
    hexs = [(f.get("name"), f.get("hex") or "") for f in fields]
    print("Hex (text order, row step 2): asc 1-3 %s" % ["%s=%r" % x for x in sorted(hexs, key=lambda x: x[1])[:3]])
    print("                              desc 1-3 %s" % ["%s=%r" % x for x in sorted(hexs, key=lambda x: x[1], reverse=True)[:3]])
    return bad


def release(a):
    import mailbox_poke as MP
    st = json.load(open(a.state))
    if st.get("released"):
        print("already released: %s" % st["clone"])
        return 0
    p = int(st["clone"], 16)
    if MP.pid_of(st["process"]) != st["pid"]:
        raise SystemExit("REFUSED: pid %d is no longer %s -- nothing of ours to free" % (st["pid"], st["process"]))
    h = open_proc(st["pid"])
    try:
        m = MBI()
        if not k.VirtualQueryEx(h, ctypes.c_void_p(p), ctypes.byref(m), ctypes.sizeof(m)):
            raise SystemExit("FAIL: VirtualQueryEx err=%d" % ctypes.get_last_error())
        if (m.AllocationBase or 0) != p or m.State != MEM_COMMIT or m.Type != MEM_PRIVATE or m.RegionSize < REGION:
            raise SystemExit("REFUSED: 0x%X is not our allocation any more (base 0x%X state 0x%X type 0x%X size 0x%X)"
                             % (p, m.AllocationBase or 0, m.State, m.Type, m.RegionSize))
        if q(read(h, p + UOBJECT_CLASS, 8), 0) != int(st["class_private"], 16):
            raise SystemExit("REFUSED: the qword at 0x%X+0x10 is not the ClassPrivate make wrote" % p)
        if not k.VirtualFreeEx(h, ctypes.c_void_p(p), 0, MEM_RELEASE):
            raise SystemExit("FAIL: VirtualFreeEx(MEM_RELEASE) err=%d" % ctypes.get_last_error())
    finally:
        k.CloseHandle(h)
    st["released"] = True
    json.dump(st, open(a.state, "w"), indent=1)
    print("released %s" % st["clone"])
    return 0


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("phase", choices=("make", "predict", "release"))
    ap.add_argument("--process", default="DumperTest-Win64-Shipping")
    ap.add_argument("--state", required=True, help="JSON file the phases share (put it under out\\)")
    ap.add_argument("--poke", default="Table_Small", help="ObjectProperty of the copy to point at the copy")
    ap.add_argument("--funcs", action="store_true", help="also copy the class + its first UFunction (Functions grid)")
    a = ap.parse_args()
    return {"make": make, "predict": predict, "release": release}[a.phase](a)


if __name__ == "__main__":
    sys.exit(main())
