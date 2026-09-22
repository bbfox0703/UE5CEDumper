#!/usr/bin/env python3
r"""L49 `[A2-WALKCLASSEX-UNMAPPED]`: an unreadable class address is logged once and NOT memoized as empty.

    py tools/verify/l49_unmapped_class.py --pid <game pid> --log-dir "%LOCALAPPDATA%\UE5CEDumper\Logs\DumperTest-Win64-Shipping"

THE ROW SAYS "no live trigger" -- the 2026-09-22 survey found one. `walk_class` hands ANY address to
`Ubel::WalkClassEx`, which has no GObjects-membership check, so a page RESERVED (never committed) in
the game from outside is exactly the transient fault dll_core_test manufactures by decommitting a page:

  STEP A  walk_class P twice + walk_class_batch [P, P] on a reserved page: every reply has no fields,
          walk-0.log gains EXACTLY ONE "WalkClass: 0x<P> is not readable at +0x..." and ZERO
          "WalkClassEx: 0x<P> REFUSED".
  STEP B  commit P and copy a real UClass's first 0x400 bytes into it: walk_class P must now return
          that class's name and field list. ⭐ THIS is the discriminator -- pre-fix, WalkClassEx had
          memoized {P, PropertiesSize 0} forever and answered the empty class again.

⚠ MEM_RESERVE, not a committed zero page: a committed page is "readable, PropertiesSize 0", a
different path that is legitimately memoized as empty. Run with the UI closed (2 of 3 pipe slots).
P is released at the end; do not reuse the address (it stays memoized as the copied class).
"""
import argparse
import ctypes
import ctypes.wintypes as W
import os
import sys
import time

sys.stdout.reconfigure(encoding="utf-8", errors="replace")
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from pipe_client import PipeClient          # noqa: E402
from mutate_guard import assert_channel_carries   # noqa: E402

k = ctypes.WinDLL("kernel32", use_last_error=True)
k.OpenProcess.restype = W.HANDLE
k.VirtualAllocEx.restype = ctypes.c_void_p
k.VirtualAllocEx.argtypes = [W.HANDLE, ctypes.c_void_p, ctypes.c_size_t, W.DWORD, W.DWORD]
k.VirtualFreeEx.argtypes = [W.HANDLE, ctypes.c_void_p, ctypes.c_size_t, W.DWORD]
k.ReadProcessMemory.argtypes = [W.HANDLE, ctypes.c_void_p, ctypes.c_void_p, ctypes.c_size_t,
                                ctypes.POINTER(ctypes.c_size_t)]
k.WriteProcessMemory.argtypes = [W.HANDLE, ctypes.c_void_p, ctypes.c_void_p, ctypes.c_size_t,
                                 ctypes.POINTER(ctypes.c_size_t)]
MEM_COMMIT, MEM_RESERVE, MEM_RELEASE = 0x1000, 0x2000, 0x8000
PAGE_NOACCESS, PAGE_READWRITE = 0x01, 0x04
ACCESS = 0x0008 | 0x0010 | 0x0020 | 0x0400    # VM_OPERATION | VM_READ | VM_WRITE | QUERY_INFORMATION
COPY = 0x400


def fields_of(reply):
    d = reply.get("data", reply)
    c = d.get("class") or {}
    return c.get("name"), [(f.get("name"), f.get("offset"), f.get("type")) for f in (c.get("fields") or [])]


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--pid", type=int, required=True)
    ap.add_argument("--log-dir", required=True)
    a = ap.parse_args()
    walklog = os.path.join(os.path.expandvars(a.log_dir), "walk-0.log")
    if not assert_channel_carries(walklog, "[WALK", "a WalkClass line"):
        return 2
    fails = []
    h = k.OpenProcess(ACCESS, False, a.pid)
    if not h:
        print("FAIL: OpenProcess(%d) err %d" % (a.pid, ctypes.get_last_error()))
        return 2
    P = None
    try:
        with PipeClient() as c:
            print("build", c.assert_build())
            c.ensure_scanned()
            inst = next((i for i in (c.request("find_instances", class_name="DumperTestActor",
                                               max_results=5).get("instances") or [])
                         if i.get("name") == "DumperTestActor"), None)
            if not inst:
                print("FAIL: no live DumperTestActor")
                return 2
            C = inst.get("class_addr")
            base_name, base_fields = fields_of(c.request("walk_class", addr=C))
            print("baseline: class %s at %s -> %d fields" % (base_name, C, len(base_fields)))
            if not base_fields:
                print("FAIL: the baseline class walk is empty -- STEP B would be vacuous")
                return 2

            P = k.VirtualAllocEx(h, None, 0x10000, MEM_RESERVE, PAGE_NOACCESS)
            if not P:
                print("FAIL: VirtualAllocEx(MEM_RESERVE) err %d" % ctypes.get_last_error())
                return 2
            Ps = "0x%X" % P
            print("reserved (uncommitted) P = %s" % Ps)
            start = os.path.getsize(walklog)

            # ---- STEP A ----
            r1 = fields_of(c.request("walk_class", addr=Ps))
            r2 = fields_of(c.request("walk_class", addr=Ps))
            rb = c.request("walk_class_batch", addrs=[Ps, Ps])
            db = rb.get("data", rb)
            batch = db.get("classes") or db.get("results") or []
            batch_fields = [len((x.get("class", x) or {}).get("fields") or []) for x in batch]
            print("STEP A: walk_class P -> %d, %d fields; walk_class_batch [P,P] -> %s"
                  % (len(r1[1]), len(r2[1]), batch_fields))
            if r1[1] or r2[1] or any(batch_fields):
                fails.append("STEP A: a reserved page returned fields")
            time.sleep(1.0)
            with open(walklog, "rb") as fh:
                fh.seek(start)
                new = fh.read().decode("utf-8", "replace")
            key = ("0x%X" % P).lower()
            unread = [l for l in new.splitlines() if "is not readable at +0x" in l and key in l.lower()]
            refused = [l for l in new.splitlines() if "WalkClassEx:" in l and "REFUSED" in l and key in l.lower()]
            print("        walk-0.log since STEP A: %d 'is not readable' line(s) for P (want 1), "
                  "%d 'REFUSED' (want 0)" % (len(unread), len(refused)))
            for l in unread[:2]:
                print("          " + l.strip()[:170])
            if len(unread) != 1:
                fails.append("STEP A: %d 'not readable' lines for P, want exactly 1" % len(unread))
            if refused:
                fails.append("STEP A: a REFUSED line for P")

            # ---- STEP B ----
            if not k.VirtualAllocEx(h, ctypes.c_void_p(P), 0x1000, MEM_COMMIT, PAGE_READWRITE):
                fails.append("STEP B: commit failed err %d" % ctypes.get_last_error())
            else:
                buf = ctypes.create_string_buffer(COPY)
                got = ctypes.c_size_t(0)
                ok = k.ReadProcessMemory(h, ctypes.c_void_p(int(C, 16)), buf, COPY, ctypes.byref(got))
                wrote = ctypes.c_size_t(0)
                ok2 = k.WriteProcessMemory(h, ctypes.c_void_p(P), buf, got.value, ctypes.byref(wrote))
                print("STEP B: committed P, copied %d bytes of the real class (read ok=%s, write ok=%s)"
                      % (wrote.value, bool(ok), bool(ok2)))
                name_b, fields_b = fields_of(c.request("walk_class", addr=Ps))
                same = (name_b == base_name and fields_b == base_fields)
                print("        walk_class P -> class %s, %d fields; identical to the baseline: %s"
                      % (name_b, len(fields_b), same))
                if not same:
                    fails.append("STEP B: P answered %s/%d fields, not %s/%d -- memoized empty?"
                                 % (name_b, len(fields_b), base_name, len(base_fields)))
    finally:
        if P:
            k.VirtualFreeEx(h, ctypes.c_void_p(P), 0, MEM_RELEASE)
        k.CloseHandle(h)
    for f in fails:
        print("FAIL: " + f)
    print("L49 pipe arms: %s" % ("PASS" if not fails else "FAIL"))
    return 0 if not fails else 1


if __name__ == "__main__":
    sys.exit(main())
