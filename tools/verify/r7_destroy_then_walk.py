"""Destroy a live actor through the pipe and watch how the walker labels what still points at it.

    py tools/verify/r7_destroy_then_walk.py --process DumperTest51-Win64-Shipping \
        --class BP_ThirdPersonCharacter_C --out out/r7live/b01/ship
    py tools/verify/r7_destroy_then_walk.py --process DumperTest-Win64-Shipping --class DumperTestActor \
        --follow "" --out out/r7live/b04/green

For Review 7 rows R7-B-01 (UE 5.0-5.3 PendingKill targets tagged [garbage]) and R7-B-04 (every delegate / weak
display site carries the tag). Destroying an actor marks it and its components garbage IMMEDIATELY, but the
memory survives until the next GC purge (up to ~61 s), so a walk inside that window reads bindings whose target
is a marked-but-live object -- exactly the state the tag is for.

What it does, teeing every reply to --out:
  1. find the first non-CDO instance P of --class (exact match, then a substring fallback);
  2. walk P, and the objects P's pointer fields named in --follow point at (default: CharacterMovement,
     CapsuleComponent -- taken from P's own walk, never by name: CDO subobjects share those names);
  3. read each object's ObjectFlags (UObjectBase+0x08) with ReadProcessMemory -- a witness that does not go
     through the DLL;
  4. invoke K2_DestroyActor on P;
  5. --rounds times, --gap seconds apart: re-walk every object and re-read its flags.
It prints, per object and round, the flags and every delegate / weak / optional field's display value.

Read-only apart from the destroy itself. Needs the DLL injected and scanned in --process.
"""
import argparse
import ctypes
import ctypes.wintypes as w
import json
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

INTERESTING = ("Delegate", "Weak", "Optional", "Soft", "Lazy")
RF_PENDINGKILL, RF_GARBAGE = 0x20000000, 0x40000000


def read_u32(h, addr):
    buf, got = ctypes.c_uint32(), ctypes.c_size_t()
    ok = k32.ReadProcessMemory(h, ctypes.c_void_p(addr), ctypes.byref(buf), 4, ctypes.byref(got))
    return buf.value if ok and got.value == 4 else None


def fields_of(reply):
    return (reply.get("data") or reply).get("fields") or []


def show_flags(v):
    if v is None:
        return "unreadable"
    tags = [n for n, b in (("PendingKill", RF_PENDINGKILL), ("Garbage", RF_GARBAGE)) if v & b]
    return f"0x{v:08X}" + (f" ({'+'.join(tags)})" if tags else "")


def main():
    ap = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    ap.add_argument("--process", required=True, help="image name (substring) of the injected game")
    ap.add_argument("--class", dest="cls", required=True)
    ap.add_argument("--follow", default="CharacterMovement,CapsuleComponent",
                    help="comma list of P's pointer fields to walk too ('' = P only)")
    ap.add_argument("--out", required=True)
    ap.add_argument("--build", default=None, help="assert this DLL build number")
    ap.add_argument("--rounds", type=int, default=3)
    ap.add_argument("--gap", type=float, default=2.0)
    ap.add_argument("--array-limit", type=int, default=8)
    a = ap.parse_args()
    os.makedirs(a.out, exist_ok=True)
    log = {"args": vars(a), "rounds": []}
    lines = []

    def say(s=""):
        print(s, flush=True)
        lines.append(s)

    pid, proc = pid_of(a.process)
    if not pid:
        raise SystemExit(f"no process matching {a.process!r}")
    h = k32.OpenProcess(0x0010 | 0x0400, False, pid)
    with PipeClient(timeout=300) as c:
        say(f"# {time.strftime('%Y-%m-%d %H:%M:%S')} {proc} pid {pid} build {c.assert_build(a.build)}")
        c.ensure_scanned(timeout=300, poll=3)
        p = c.request("get_pointers")
        pd = p.get("data") or p
        say(f"# ue {pd.get('ue_version')} objects {pd.get('object_count')} "
            f"sparse_delegates {pd.get('sparse_delegates') or pd.get('sparse_delegates_addr')}")
        log["pointers"] = pd

        inst = []
        for exact in (True, False):
            fi = c.request("find_instances", class_name=a.cls, exact_match=exact, limit=64)
            inst = [i for i in ((fi.get("data") or fi).get("instances") or [])
                    if not (i.get("name") or "").startswith("Default__")]
            if inst:
                break
        if not inst:
            raise SystemExit(f"no live instance of {a.cls}")
        P = inst[0]
        say(f"target P = {P.get('name')} {P['addr']} ({len(inst)} live candidate(s))")

        wp = c.request("walk_instance", addr=P["addr"], array_limit=a.array_limit)
        objs = [("P", P["addr"])]
        for name in [x for x in a.follow.split(",") if x]:
            f = next((f for f in fields_of(wp) if f.get("name") == name), None)
            if not f or not f.get("ptr") or f.get("ptr") in ("0x0", "0"):
                raise SystemExit(f"P has no non-null pointer field {name!r}")
            objs.append((name, f["ptr"]))
            say(f"  {name} -> {f['ptr']} {f.get('ptr_name')} ({f.get('ptr_class')})")

        def snapshot(label):
            rec = {"label": label, "t": time.time(), "objects": {}}
            say(f"--- {label}")
            for tag, addr in objs:
                flags = read_u32(h, int(addr, 16) + 0x08)
                try:
                    wr = c.request("walk_instance", addr=addr, array_limit=a.array_limit)
                    fl = fields_of(wr)
                    err = None
                except Exception as e:  # a purge inside the window fails the walk
                    fl, err = [], str(e)
                picked = [f for f in fl if any(k in (f.get("type") or "") for k in INTERESTING)
                          or "Delegate" in (f.get("name") or "")]   # delegate ARRAYS too (type ArrayProperty)
                rec["objects"][tag] = {"addr": addr, "flags": flags, "error": err, "fields": picked}
                say(f"  {tag:18} {addr}  flags {show_flags(flags)}" + (f"  WALK FAILED: {err[:80]}" if err else ""))
                for f in picked:
                    say(f"      {f.get('type', '')[:30]:30} {f.get('name', ''):30} {str(f.get('value'))[:120]}")
            log["rounds"].append(rec)

        snapshot("before destroy")
        r = c.request("invoke_function", instance_addr=P["addr"], func_name="K2_DestroyActor", parms_size=0)
        log["invoke"] = r
        say(f"--- invoke K2_DestroyActor: {json.dumps(r)[:200]}")
        for i in range(a.rounds):
            time.sleep(a.gap)
            snapshot(f"after destroy, round {i + 1}")
    k32.CloseHandle(h)
    json.dump(log, open(os.path.join(a.out, "destroy_then_walk.json"), "w", encoding="utf-8"), indent=1)
    open(os.path.join(a.out, "destroy_then_walk.txt"), "w", encoding="utf-8").write("\n".join(lines) + "\n")
    return 0


if __name__ == "__main__":
    sys.exit(main())
