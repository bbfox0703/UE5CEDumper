#!/usr/bin/env python3
"""Write a minidump of a HUNG process and print every thread's stack by module.

Built for the build-3363 follow-up: with the dxgi proxy deployed, OCTOPATH TRAVELER stops
crashing but never shows a window, and its UE5CEDumper log stops at
`DllMain: auto-start thread created OK`. A thread created inside DllMain cannot start until
the loader lock is released, so that log line not being followed by the auto-start thread's
own first line means **the loader lock was never released**. This tool answers *which thread
is holding it and where*, which is the part reasoning cannot supply.

    py tools/verify/hang_dump.py --wait-for Octopath_Traveler-Win64-Shipping.exe --after 25

Waits for the process to appear, sleeps `--after` seconds so the hang is established, writes
`out/hang-<name>-<pid>.dmp`, then prints a per-thread module census of the stacks.

⚠ Reads only. It never suspends, injects into, or kills the target.
⚠ A dump of a hung process is a SNAPSHOT of a wedge, not a proof of causation: a thread
  sitting in NtWaitForSingleObject inside the loader tells you where it stopped, not who put
  it there. Read the OTHER threads before concluding.
"""
import argparse
import ctypes
import ctypes.wintypes as wt
import os
import sys
import time

PROCESS_QUERY_INFORMATION = 0x0400
PROCESS_VM_READ = 0x0010
MiniDumpWithFullMemory = 0x00000002
MiniDumpWithHandleData = 0x00000004
MiniDumpWithThreadInfo = 0x00001000

TH32CS_SNAPPROCESS = 0x00000002


class PROCESSENTRY32W(ctypes.Structure):
    _fields_ = [
        ("dwSize", wt.DWORD), ("cntUsage", wt.DWORD), ("th32ProcessID", wt.DWORD),
        ("th32DefaultHeapID", ctypes.POINTER(ctypes.c_ulong)), ("th32ModuleID", wt.DWORD),
        ("cntThreads", wt.DWORD), ("th32ParentProcessID", wt.DWORD),
        ("pcPriClassBase", ctypes.c_long), ("dwFlags", wt.DWORD),
        ("szExeFile", ctypes.c_wchar * 260),
    ]


def find_pid(name: str):
    k32 = ctypes.WinDLL("kernel32", use_last_error=True)
    k32.CreateToolhelp32Snapshot.restype = wt.HANDLE
    snap = k32.CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0)
    if snap == wt.HANDLE(-1).value:
        return None
    try:
        e = PROCESSENTRY32W()
        e.dwSize = ctypes.sizeof(e)
        ok = k32.Process32FirstW(snap, ctypes.byref(e))
        while ok:
            if e.szExeFile.lower() == name.lower():
                return e.th32ProcessID
            ok = k32.Process32NextW(snap, ctypes.byref(e))
    finally:
        k32.CloseHandle(snap)
    return None


def write_dump(pid: int, out_path: str) -> bool:
    k32 = ctypes.WinDLL("kernel32", use_last_error=True)
    dbg = ctypes.WinDLL("dbghelp", use_last_error=True)
    k32.OpenProcess.restype = wt.HANDLE
    k32.OpenProcess.argtypes = [wt.DWORD, wt.BOOL, wt.DWORD]
    h = k32.OpenProcess(PROCESS_QUERY_INFORMATION | PROCESS_VM_READ, False, pid)
    if not h:
        print(f"  OpenProcess({pid}) failed, err={ctypes.get_last_error()} "
              f"(an elevated game needs an elevated shell)")
        return False
    # CreateFileW, NOT open()+_get_osfhandle: the CRT helper's default ctypes restype is
    # c_int, which TRUNCATES a 64-bit HANDLE, and MiniDumpWriteDump then fails with
    # 0x80070006 ERROR_INVALID_HANDLE — a message that reads like a permissions problem.
    GENERIC_WRITE, CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL = 0x40000000, 2, 0x80
    k32.CreateFileW.restype = wt.HANDLE
    k32.CreateFileW.argtypes = [wt.LPCWSTR, wt.DWORD, wt.DWORD, ctypes.c_void_p,
                                wt.DWORD, wt.DWORD, wt.HANDLE]
    fh = k32.CreateFileW(out_path, GENERIC_WRITE, 0, None,
                         CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, None)
    if fh == wt.HANDLE(-1).value or not fh:
        print(f"  CreateFileW({out_path}) failed, err={ctypes.get_last_error()}")
        k32.CloseHandle(h)
        return False
    try:
        dbg.MiniDumpWriteDump.argtypes = [wt.HANDLE, wt.DWORD, wt.HANDLE, wt.DWORD,
                                          ctypes.c_void_p, ctypes.c_void_p, ctypes.c_void_p]
        ok = dbg.MiniDumpWriteDump(
            h, pid, fh,
            MiniDumpWithFullMemory | MiniDumpWithHandleData | MiniDumpWithThreadInfo,
            None, None, None)
        if not ok:
            print(f"  MiniDumpWriteDump failed, err={ctypes.get_last_error()}")
        return bool(ok)
    finally:
        k32.CloseHandle(fh)
        k32.CloseHandle(h)


def census(path: str) -> None:
    """Per-thread stack census. A hang has no exception record, so the existing
    tools/pe/minidump_triage.py (which walks the FAULTING thread) has nothing to walk."""
    import struct

    def u32(d, o):
        return struct.unpack_from("<I", d, o)[0]

    def u64(d, o):
        return struct.unpack_from("<Q", d, o)[0]

    d = open(path, "rb").read()
    if d[:4] != b"MDMP":
        print("  not a minidump")
        return
    streams = {}
    dirrva = u32(d, 12)
    for i in range(u32(d, 8)):
        b = dirrva + i * 12
        streams.setdefault(u32(d, b), (u32(d, b + 4), u32(d, b + 8)))

    mods = []
    _, rva = streams[4]
    for i in range(u32(d, rva)):
        b = rva + 4 + i * 108
        nr = u32(d, b + 20)
        n = u32(d, nr)
        nm = d[nr + 4:nr + 4 + n].decode("utf-16-le", "replace").split("\\")[-1]
        mods.append((u64(d, b), u32(d, b + 8), nm))

    def sym(a):
        for base, size, nm in mods:
            if base <= a < base + size:
                return nm, a - base
        return None

    ranges = []
    if 9 in streams:                                   # Memory64List (full-memory dump)
        _, r = streams[9]
        off = u64(d, r + 8)
        for i in range(u64(d, r)):
            b = r + 16 + i * 16
            ranges.append((u64(d, b), u64(d, b + 8), off))
            off += u64(d, b + 8)
    elif 5 in streams:                                 # MemoryList
        _, r = streams[5]
        for i in range(u32(d, r)):
            b = r + 4 + i * 16
            ranges.append((u64(d, b), u32(d, b + 8), u32(d, b + 12)))

    def read(addr, ln):
        for start, size, fo in ranges:
            if start <= addr < start + size:
                avail = min(ln, start + size - addr)
                return d[fo + (addr - start):fo + (addr - start) + avail]
        return None

    _, r = streams[3]
    nthreads = u32(d, r)
    print(f"\n== {nthreads} threads ==")
    for i in range(nthreads):
        b = r + 4 + i * 48
        # MINIDUMP_THREAD: ThreadId 0, SuspendCount 4, PriorityClass 8, Priority 12, Teb 16,
        # Stack{StartOfMemoryRange 24, DataSize 32, Rva 36}, ThreadContext{DataSize 40, Rva 44}.
        # ⚠ The stack descriptor starts at +24, NOT +16 — Teb sits in between. Reading it at
        # +16 yields a plausible-looking base with an absurd length (hundreds of MB) and an
        # RIP that is not in any module, which is what a wrong offset looks like here.
        tid = u32(d, b)
        stack_base, stack_size = u64(d, b + 24), u32(d, b + 32)
        ctx_rva = u32(d, b + 44)
        rip, rsp = u64(d, ctx_rva + 0xF8), u64(d, ctx_rva + 0x98)
        at = sym(rip)
        head = f"{at[0]}+0x{at[1]:X}" if at else f"0x{rip:016X}"
        mem = read(rsp, max(0, stack_base + stack_size - rsp)) if ranges else None
        seen, order = {}, []
        if mem:
            for o in range(0, len(mem) - 8, 8):
                s = sym(struct.unpack_from("<Q", mem, o)[0])
                if s:
                    if s[0] not in seen:
                        order.append(s[0])
                    seen[s[0]] = seen.get(s[0], 0) + 1
        trail = ", ".join(f"{m}x{seen[m]}" for m in order[:8]) or "(no stack memory)"
        print(f"  tid {tid:<7} rip={head:<34} stack: {trail}")


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--census", metavar="DUMP", help="analyse an existing dump and exit")
    ap.add_argument("--wait-for", help="process image name, e.g. Foo.exe")
    ap.add_argument("--after", type=float, default=25.0,
                    help="seconds to wait after the process appears before dumping")
    ap.add_argument("--timeout", type=float, default=300.0,
                    help="give up waiting for the process after this many seconds")
    ap.add_argument("--out", default=None)
    args = ap.parse_args()

    if args.census:
        census(args.census)
        return 0
    if not args.wait_for:
        ap.error("need --wait-for <Foo.exe> (or --census <dump>)")

    print(f"waiting for {args.wait_for} (timeout {args.timeout:.0f}s) ...")
    deadline = time.monotonic() + args.timeout
    pid = None
    while time.monotonic() < deadline:
        pid = find_pid(args.wait_for)
        if pid:
            break
        time.sleep(0.5)
    if not pid:
        print("  never appeared.")
        return 1
    print(f"  found PID {pid}; letting it wedge for {args.after:.0f}s ...")
    time.sleep(args.after)

    if find_pid(args.wait_for) != pid:
        print("  process exited before the dump — it did not hang this time.")
        return 1

    out = args.out or os.path.join("out", f"hang-{os.path.splitext(args.wait_for)[0]}-{pid}.dmp")
    os.makedirs(os.path.dirname(out), exist_ok=True)
    print(f"  writing {out} ...")
    if not write_dump(pid, out):
        return 1
    print(f"  wrote {out} ({os.path.getsize(out):,} bytes)")
    census(out)
    print(f"\nDump kept at {out} — re-analyse with:  py tools/verify/hang_dump.py --census {out}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
