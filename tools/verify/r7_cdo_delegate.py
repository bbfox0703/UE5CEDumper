"""R7-B-02 (delegate half): a UE4 CDO's never-bound delegate reads "(unbound)", with its raw bytes as the witness.

    py tools/verify/r7_cdo_delegate.py --process UE427_3rdPerson-Win64-Shipping --out out/r7live/b02/427_green

UE4's FWeakObjectPtr::Reset writes {INDEX_NONE, 0}, so every default-constructed DelegateProperty on a UE4 native
CDO holds ObjectIndex -1 / Serial 0 -- a state the pre-fix labeller called "(stale)" (it treated only {0, 0} as
unbound). The fixture's Default__DelegatePadFixture.Del_Unicast is exactly that (it is bound only in BeginPlay, on a
spawned instance), and a spawned instance is the bound control.

Walks the CDO and every live DelegatePadFixture, prints each DelegateProperty's display value, and reads the CDO
field's first 16 bytes with ReadProcessMemory -- the value the label is about, read without the DLL.
"""
import argparse
import ctypes
import ctypes.wintypes as w
import os
import sys
import time

sys.stdout.reconfigure(encoding="utf-8", errors="replace")
HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
from pipe_client import PipeClient  # noqa: E402
from read_mem import pid_of  # noqa: E402

k32 = ctypes.WinDLL("kernel32", use_last_error=True)
k32.OpenProcess.restype = w.HANDLE
k32.ReadProcessMemory.argtypes = [w.HANDLE, w.LPCVOID, w.LPVOID, ctypes.c_size_t, ctypes.POINTER(ctypes.c_size_t)]


def main():
    ap = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    ap.add_argument("--process", required=True)
    ap.add_argument("--class", dest="cls", default="DelegatePadFixture")
    ap.add_argument("--build", default=None)
    ap.add_argument("--out", required=True)
    a = ap.parse_args()
    os.makedirs(a.out, exist_ok=True)
    lines = []

    def say(s=""):
        print(s, flush=True)
        lines.append(s)

    pid, proc = pid_of(a.process)
    h = k32.OpenProcess(0x0010 | 0x0400, False, pid)
    with PipeClient(timeout=300) as c:
        say(f"# {time.strftime('%Y-%m-%d %H:%M:%S')} {proc} pid {pid} build {c.assert_build(a.build)}")
        fi = c.request("find_instances", class_name=a.cls, exact_match=True, limit=16)
        inst = (fi.get("data") or fi).get("instances") or []
        say(f"{a.cls}: {[i.get('name') for i in inst]}")
        for i in inst:
            wr = c.request("walk_instance", addr=i["addr"], array_limit=8)
            for f in ((wr.get("data") or wr).get("fields") or []):
                if f.get("type") != "DelegateProperty":
                    continue
                raw = ""
                if (i.get("name") or "").startswith("Default__"):
                    buf, got = (ctypes.c_ubyte * 16)(), ctypes.c_size_t()
                    if k32.ReadProcessMemory(h, ctypes.c_void_p(int(i["addr"], 16) + int(f["offset"])), buf, 16,
                                             ctypes.byref(got)):
                        raw = "  raw " + " ".join(f"{b:02X}" for b in buf[:got.value])
                say(f"  {i.get('name'):36} {f.get('name'):16} +0x{int(f['offset']):X}  {f.get('value')!r}{raw}")
    open(os.path.join(a.out, "cdo_delegate.txt"), "w", encoding="utf-8").write("\n".join(lines) + "\n")
    return 0


if __name__ == "__main__":
    sys.exit(main())
