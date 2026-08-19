"""Resolve g_invokeMailbox in a live game by parsing the injected DLL's export table.

Read-only. Exists so the mailbox can be witnessed WITHOUT going through CE -- the
Y1 rows need a reader that is independent of the thing under test, and CE's own
getAddress() is part of the path being measured.

    py mailbox_addr.py Elliot-Win64-Shipping
    py mailbox_addr.py Elliot-Win64-Shipping --dump 0x328 16
"""
import sys, ctypes, subprocess, struct
from ctypes import wintypes

k32 = ctypes.WinDLL("kernel32", use_last_error=True)
psapi = ctypes.WinDLL("psapi", use_last_error=True)
k32.OpenProcess.restype = ctypes.c_void_p
k32.OpenProcess.argtypes = [wintypes.DWORD, wintypes.BOOL, wintypes.DWORD]
k32.ReadProcessMemory.argtypes = [ctypes.c_void_p, ctypes.c_void_p, ctypes.c_void_p,
                                  ctypes.c_size_t, ctypes.POINTER(ctypes.c_size_t)]
k32.CloseHandle.argtypes = [ctypes.c_void_p]

def pid_of(stem):
    out = subprocess.run(["tasklist", "/fo", "csv", "/nh"], capture_output=True, text=True).stdout
    hits = []
    for line in out.splitlines():
        parts = [p.strip('"') for p in line.split('","')]
        if parts and parts[0].lower().startswith(stem.lower()[:24]):
            hits.append((parts[0], int(parts[1])))
    if not hits:
        raise SystemExit(f"process {stem!r} not found")
    if len(hits) > 1:
        raise SystemExit(f"ambiguous: {hits}")   # never guess which process to read
    return hits[0][1]

class Reader:
    def __init__(self, pid):
        self.h = k32.OpenProcess(0x0010 | 0x0400, False, pid)
        if not self.h:
            raise SystemExit(f"OpenProcess failed err={ctypes.get_last_error()}")
    def read(self, addr, n):
        buf = (ctypes.c_ubyte * n)(); got = ctypes.c_size_t(0)
        ok = k32.ReadProcessMemory(self.h, ctypes.c_void_p(addr), buf, n, ctypes.byref(got))
        if not ok or got.value != n:
            raise SystemExit(f"read failed at {addr:#x} ({got.value}/{n})")
        return bytes(buf)
    def u32(self, a): return struct.unpack("<I", self.read(a, 4))[0]
    def u16(self, a): return struct.unpack("<H", self.read(a, 2))[0]

def modules(pid):
    h = k32.OpenProcess(0x0010 | 0x0400, False, pid)
    arr = (ctypes.c_ulonglong * 4096)(); need = wintypes.DWORD()
    psapi.EnumProcessModulesEx(ctypes.c_void_p(h), arr, ctypes.sizeof(arr),
                               ctypes.byref(need), 0x03)
    psapi.GetModuleFileNameExW.argtypes = [ctypes.c_void_p, ctypes.c_void_p,
                                           ctypes.c_wchar_p, wintypes.DWORD]
    out = []
    for i in range(need.value // 8):
        name = ctypes.create_unicode_buffer(512)
        psapi.GetModuleFileNameExW(ctypes.c_void_p(h), ctypes.c_void_p(arr[i]), name, 512)
        out.append((name.value, int(arr[i])))
    k32.CloseHandle(ctypes.c_void_p(h))
    return out

def find_export(r, base, want):
    e_lfanew = r.u32(base + 0x3C)
    nt = base + e_lfanew
    # optional header magic 0x20B = PE32+, export dir is the first data directory
    exp_rva = r.u32(nt + 0x18 + 0x70)
    if not exp_rva:
        return None
    ed = base + exp_rva
    n_names   = r.u32(ed + 0x18)
    a_funcs   = r.u32(ed + 0x1C)
    a_names   = r.u32(ed + 0x20)
    a_ords    = r.u32(ed + 0x24)
    for i in range(n_names):
        nrva = r.u32(base + a_names + 4 * i)
        s = b""
        a = base + nrva
        while True:
            chunk = r.read(a, 32)
            if b"\0" in chunk:
                s += chunk[:chunk.index(b"\0")]; break
            s += chunk; a += 32
        if s.decode("ascii", "replace") == want:
            o = r.u16(base + a_ords + 2 * i)
            return base + r.u32(base + a_funcs + 4 * o)
    return None

def main():
    stem = sys.argv[1]
    # An all-digits argument is a PID. Necessary because `pid_of` deliberately refuses
    # to guess between same-named processes, and the rigs that drive this ARE python --
    # so "python" is always ambiguous with the rig's own interpreter.
    pid = int(stem) if stem.isdigit() else pid_of(stem)
    r = Reader(pid)
    for path, base in modules(pid):
        if not base:
            continue
        low = path.lower()
        if not any(low.endswith(x) for x in ("dxgi.dll", "version.dll", "dinput8.dll",
                                             "winmm.dll", "ue5dumper.dll")):
            continue
        try:
            addr = find_export(r, base, "g_invokeMailbox")
        except SystemExit:
            continue
        if addr:
            print(f"# module {path} base={base:#x}")
            print(f"g_invokeMailbox = {addr:#x}")
            if "--dump" in sys.argv:
                i = sys.argv.index("--dump")
                off = int(sys.argv[i+1], 16); n = int(sys.argv[i+2])
                data = r.read(addr + off, n)
                print(f"+{off:#x} [{n}] {data.hex(' ')}")
                for k in range(0, n - 7, 8):
                    print(f"   +0x{off+k:03X}: {struct.unpack_from('<Q', data, k)[0]:#018x}")
            return
    raise SystemExit("g_invokeMailbox not found in any injected module")

main()
