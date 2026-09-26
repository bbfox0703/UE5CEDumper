"""R7-B-02 / R7-B-04 / R7-S1: how the walker labels the Review 7 TOptional hosts on DumperTest 5.4.

    py tools/verify/r7_opt_walk.py --process DumperTest-Win64-Shipping --out out/r7live/opt/green

Needs the fixture repackaged on 2026-09-25 (tools/ue-sample/README.md, "Review 7 hosts") and, for the garbage and
stale arms, the `-DumperTestWeakGarbage` switch with at least ~75 s since launch (the first GC purge is ~61 s in).
Walks the live DumperTestActor --rounds times, --gap seconds apart, and prints every Opt_Weak_* / Opt_Soft* / Opt_Lazy
field next to WeakToGarbage / WeakToGarbageCount (the garbage twin to compare with). Opt_Soft's raw first 16 bytes are
read with ReadProcessMemory: its embedded weak cache must be all zero, which is why the pre-R7-S1 label was wrong.
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
WANT = ("Opt_Weak_", "Opt_Soft", "Opt_Lazy", "WeakToGarbage")


def main():
    ap = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    ap.add_argument("--process", required=True)
    ap.add_argument("--build", default=None)
    ap.add_argument("--rounds", type=int, default=3)
    ap.add_argument("--gap", type=float, default=2.0)
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
        c.ensure_scanned(timeout=300, poll=3)
        fi = c.request("find_instances", class_name="DumperTestActor", exact_match=True, limit=8)
        inst = [i for i in ((fi.get("data") or fi).get("instances") or [])
                if not (i.get("name") or "").startswith("Default__")]
        if not inst:
            raise SystemExit("no live DumperTestActor")
        A = inst[0]
        say(f"actor {A.get('name')} {A['addr']}")
        for r in range(a.rounds):
            if r:
                time.sleep(a.gap)
            wr = c.request("walk_instance", addr=A["addr"], array_limit=8)
            fl = [f for f in ((wr.get("data") or wr).get("fields") or []) if f.get("name", "").startswith(WANT)]
            say(f"--- round {r + 1}")
            for f in fl:
                raw = ""
                if f.get("name") == "Opt_Soft":
                    buf, got = (ctypes.c_ubyte * 16)(), ctypes.c_size_t()
                    if k32.ReadProcessMemory(h, ctypes.c_void_p(int(A["addr"], 16) + int(f["offset"])), buf, 16,
                                             ctypes.byref(got)):
                        raw = "  raw " + " ".join(f"{b:02X}" for b in buf[:got.value])
                say(f"  {f.get('name'):20} {f.get('type', '')[:22]:22} +0x{int(f['offset']):X}  {f.get('value')!r}{raw}")
    open(os.path.join(a.out, "opt_walk.txt"), "w", encoding="utf-8").write("\n".join(lines) + "\n")
    return 0


if __name__ == "__main__":
    sys.exit(main())
