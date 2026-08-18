"""Read raw bytes out of a live process -- the independent witness for UI-reported addresses.

    py read_mem.py DumperTest 0x1F144477D88 16        # 16 bytes at an address
    py read_mem.py DumperTest 0x1F144477D88 8 --ptr   # ...and decode as a pointer

Exists because several register rows are only decidable against bytes the UI did
NOT compute. Reading the map's data pointer out of the UI's own element list, for
instance, derives the expected value FROM the observed one and can only ever
agree with itself (working-lessons.md 1.10a). The same applies to the freeze rows:
AA1 asks whether the 7 neighbour BITS of a packed bool moved, and Y15 whether the
3 bytes after a 1-byte enum moved -- neither is visible in a panel that renders
one field at a time.

Read-only: PROCESS_VM_READ | PROCESS_QUERY_INFORMATION, no write path on purpose.
Prints the byte run, its little-endian u64, and a per-bit breakdown for byte runs
of length 1, since that is the form AA1 needs.
"""
import ctypes
import ctypes.wintypes as w
import subprocess
import sys

k32 = ctypes.WinDLL("kernel32", use_last_error=True)
PROCESS_VM_READ = 0x0010
PROCESS_QUERY_INFORMATION = 0x0400

k32.OpenProcess.restype = w.HANDLE
k32.ReadProcessMemory.argtypes = [w.HANDLE, w.LPCVOID, w.LPVOID,
                                  ctypes.c_size_t, ctypes.POINTER(ctypes.c_size_t)]


def pid_of(match):
    out = subprocess.run(["tasklist", "/FO", "CSV", "/NH"],
                         capture_output=True, text=True, errors="replace").stdout
    for line in out.splitlines():
        parts = [p.strip('"') for p in line.split('","')]
        if len(parts) >= 2 and match.lower() in parts[0].lower():
            return int(parts[1]), parts[0]
    return None, None


def main(argv):
    if len(argv) < 3:
        print(__doc__)
        return 2
    name, addr_s, n = argv[0], argv[1], int(argv[2])
    as_ptr = "--ptr" in argv

    pid, proc = pid_of(name)
    if not pid:
        print(f"no process matching {name!r}")
        return 1
    addr = int(addr_s, 16) if addr_s.lower().startswith("0x") else int(addr_s, 16)

    h = k32.OpenProcess(PROCESS_VM_READ | PROCESS_QUERY_INFORMATION, False, pid)
    if not h:
        print(f"OpenProcess({pid}) failed err={ctypes.get_last_error()}")
        return 1
    buf = (ctypes.c_ubyte * n)()
    got = ctypes.c_size_t(0)
    ok = k32.ReadProcessMemory(h, ctypes.c_void_p(addr), buf, n, ctypes.byref(got))
    k32.CloseHandle(h)
    if not ok:
        print(f"ReadProcessMemory(0x{addr:X}, {n}) FAILED err={ctypes.get_last_error()}")
        return 1

    raw = bytes(buf[:got.value])
    print(f"{proc} ({pid})  0x{addr:X}  {got.value} byte(s)")
    print("  bytes : " + " ".join(f"{b:02X}" for b in raw))
    if got.value >= 8:
        print(f"  u64   : 0x{int.from_bytes(raw[:8], 'little'):016X}")
    if as_ptr and got.value >= 8:
        print(f"  ptr   : 0x{int.from_bytes(raw[:8], 'little'):X}")
    if got.value == 1:
        b = raw[0]
        bits = " ".join(f"b{i}={(b >> i) & 1}" for i in range(8))
        print(f"  byte  : 0x{b:02X} = 0b{b:08b}   {bits}")
    return 0


if __name__ == "__main__":
    sys.stdout.reconfigure(encoding="utf-8")
    sys.exit(main(sys.argv[1:]))
