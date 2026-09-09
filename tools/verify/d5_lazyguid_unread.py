"""D5 — an UNREADABLE TLazyObjectPtr element must render "???", not a fabricated GUID.

    py tools/verify/d5_lazyguid_unread.py            # DumperTest must be running + injected

THE DEFECT (blind-spot sweep, fixed 2026-09-08, c45c7ce8). `ReadLazyObjectArrayElements`
made four unchecked `Macht::ReadSafe` calls for the FGuid and formatted whatever was left
in the locals. On a faulted read those stay zero, so the element was published as
`{00000000-00000000-00000000-00000000}` and counted in `readCount` — indistinguishable
from a genuinely unset TLazyObjectPtr, which is a REAL and legitimate all-zero FGuid.
That is why "print zeros differently" was never available: only the read's own answer
separates UNREAD from READ-AS-ZERO.

⛔ WHY THIS RIG WRITES TO THE GAME. On a healthy process every element reads fine, so the
fault arm cannot be observed by looking. `Macht::ReadTArray` validates only Count and Max
and NEVER probes Data (Macht.h:287-297), so a TArray whose Data has been pointed at
unmapped memory passes the gate intact and every element read then faults — exactly the
freed-buffer shape the defect describes. Manufacturing the case is the technique
working-lessons §1.aa prescribes, and the WRITE is its own negative control: the same
walk before and after must differ, and must come back.

⚠ The restore runs in a `finally`. Leaving `Arr_LazyPtr.Data` pointing at 0x1000 would be
a live corrupt pointer in the fixture; nothing in DumperTest reads that property per tick,
but a rig that can strand it is not one to run twice.

WHAT A PASS LOOKS LIKE
  1. baseline   3 elements, each a real GUID + a resolved DumperTestHolder name
  2. corrupted  3 elements, every one "???"   <- the fix. Pre-fix this said
                {00000000-00000000-00000000-00000000}
  3. restored   byte-identical to (1)
Any of the three failing is a FAIL: (2) proves the fix fires, (1)/(3) prove the rig did
not simply break the walk.
"""
from __future__ import annotations

import json
import subprocess
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
ROOT = HERE.parent.parent
CLIENT = HERE / "pipe_client.py"

FIELD = "Arr_LazyPtr"
DEAD_PTR = 0x1000          # never mapped in a Windows user process


def call(cmd: str, args: dict) -> dict:
    r = subprocess.run([sys.executable, str(CLIENT), cmd, "--args", json.dumps(args)],
                       capture_output=True, text=True, encoding="utf-8", errors="replace",
                       cwd=str(ROOT))
    out = r.stdout or ""
    i = out.find("{")
    if i < 0:
        raise SystemExit("pipe_client gave no JSON for %s:\n%s\n%s" % (cmd, out, r.stderr))
    return json.loads(out[i:])


def find_actor():
    r = call("find_instances", {"class_name": "DumperTestActor", "limit": 8})
    if not r.get("ok"):
        raise SystemExit("find_instances failed: %s" % r)
    for inst in r.get("instances", []):
        # Skip the CDO: its arrays are the defaults, not the spawned fixture data.
        if not inst.get("name", "").startswith("Default__"):
            return inst
    raise SystemExit("no non-CDO DumperTestActor found — is the level loaded?")


def walk(addr, class_addr):
    r = call("walk_instance", {"addr": addr, "class_addr": class_addr, "array_limit": 16})
    if not r.get("ok"):
        raise SystemExit("walk_instance failed: %s" % r)
    for f in r.get("fields", []):
        if f.get("name") == FIELD:
            return f
    raise SystemExit("%s not on this actor — wrong fixture build?" % FIELD)


def values(field):
    return [e.get("v", "") for e in field.get("elements", [])]


def main() -> int:
    inst = find_actor()
    addr, class_addr = inst["addr"], inst["class_addr"]
    print("actor      : %s  %s" % (inst["name"], addr))

    base = walk(addr, class_addr)
    hdr = int(addr, 16) + int(base["offset"])
    raw = call("read_mem", {"addr": hex(hdr), "size": 16})
    if not raw.get("ok"):
        raise SystemExit("read_mem of the TArray header failed: %s" % raw)
    header = raw["bytes"]
    data_le, rest = header[:16], header[16:]
    print("header     : Data=%s Count/Max=%s" % (data_le, rest))

    base_vals = values(base)
    print("baseline   : %d element(s)" % len(base_vals))
    for v in base_vals:
        print("             %s" % v)
    ok_base = bool(base_vals) and all(
        v.startswith("{") and v != "{00000000-00000000-00000000-00000000}" for v in base_vals)

    dead = DEAD_PTR.to_bytes(8, "little").hex().upper()
    corrupted_vals = []
    ok_corrupt = False
    try:
        w = call("write_mem", {"addr": hex(hdr), "bytes": dead})
        if not w.get("ok"):
            raise SystemExit("write_mem failed: %s" % w)
        corrupted_vals = values(walk(addr, class_addr))
        print("corrupted  : %d element(s)" % len(corrupted_vals))
        for v in corrupted_vals:
            print("             %s" % v)
        ok_corrupt = bool(corrupted_vals) and all(v == "???" for v in corrupted_vals)
    finally:
        back = call("write_mem", {"addr": hex(hdr), "bytes": data_le})
        print("restore    : %s" % ("ok" if back.get("ok") else "FAILED -- %s" % back))

    after_vals = values(walk(addr, class_addr))
    ok_after = after_vals == base_vals
    print("restored   : %s" % ("identical to baseline" if ok_after else after_vals))

    fails = []
    if not ok_base:
        fails.append("baseline did not show real GUIDs (the walk is broken, not the fix)")
    if not ok_corrupt:
        fails.append("an unreadable element did NOT render '???' — got %r" % (corrupted_vals,))
    if not ok_after:
        fails.append("the restore did not bring the baseline back")

    print()
    if fails:
        print("D5: FAIL")
        for f in fails:
            print("  - %s" % f)
        return 1
    print("D5: PASS — an unreadable TLazyObjectPtr renders '???'; a real one still renders "
          "its GUID; the write is reversible.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
