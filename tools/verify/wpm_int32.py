r"""Read, or write and read back, ONE int32 in a running process -- out of process, no pipe slot.

    py tools/verify/wpm_int32.py <pid> <hexaddr>            # read only
    py tools/verify/wpm_int32.py <pid> <hexaddr> <value>    # write, then read back

Why it exists (L46 step 2-UI, 2026-09-23): the UI holds both pipe lanes while it is
connected, and ⛔ pipe_client.py must never run beside it. A reflection poke that the UI has
to RENDER -- e.g. an inner UProperty's ElementSize, whose address sw6_stride_refusal.py prints
as "ElementSize at 0x… reads N" -- therefore has to be written from outside the pipe. The
caller computes the address first (sw6 does it with the UI disconnected), connects the UI, pokes
here, clicks Refresh, and pokes the original value back. The read-back makes a write that did
not land fail loudly instead of reading as "the fix did nothing".
"""
import ctypes
import ctypes.wintypes as wt
import sys

k32 = ctypes.WinDLL("kernel32", use_last_error=True)
k32.OpenProcess.restype = wt.HANDLE
k32.OpenProcess.argtypes = [wt.DWORD, wt.BOOL, wt.DWORD]
k32.ReadProcessMemory.argtypes = [wt.HANDLE, ctypes.c_void_p, ctypes.c_void_p, ctypes.c_size_t,
                                  ctypes.POINTER(ctypes.c_size_t)]
k32.WriteProcessMemory.argtypes = [wt.HANDLE, ctypes.c_void_p, ctypes.c_void_p, ctypes.c_size_t,
                                   ctypes.POINTER(ctypes.c_size_t)]
k32.CloseHandle.argtypes = [wt.HANDLE]
PROCESS_VM_READ, PROCESS_VM_WRITE, PROCESS_VM_OPERATION = 0x10, 0x20, 0x08


def main():
    if len(sys.argv) not in (3, 4):
        raise SystemExit(__doc__)
    pid, addr = int(sys.argv[1]), int(sys.argv[2], 16)
    want = PROCESS_VM_READ | (PROCESS_VM_WRITE | PROCESS_VM_OPERATION if len(sys.argv) == 4 else 0)
    h = k32.OpenProcess(want, False, pid)
    if not h:
        raise SystemExit("OpenProcess(%d) failed: error %d" % (pid, ctypes.get_last_error()))
    try:
        buf, n = ctypes.c_int32(), ctypes.c_size_t()

        def rd():
            if not k32.ReadProcessMemory(h, addr, ctypes.byref(buf), 4, ctypes.byref(n)) or n.value != 4:
                raise SystemExit("ReadProcessMemory(0x%X) failed: error %d" % (addr, ctypes.get_last_error()))
            return buf.value

        print("0x%X before = %d" % (addr, rd()))
        if len(sys.argv) == 4:
            v = ctypes.c_int32(int(sys.argv[3], 0))
            if not k32.WriteProcessMemory(h, addr, ctypes.byref(v), 4, ctypes.byref(n)) or n.value != 4:
                raise SystemExit("WriteProcessMemory(0x%X) failed: error %d" % (addr, ctypes.get_last_error()))
            after = rd()
            print("0x%X after  = %d" % (addr, after))
            if after != v.value:
                raise SystemExit("read-back %d != written %d" % (after, v.value))
    finally:
        k32.CloseHandle(h)


if __name__ == "__main__":
    main()
