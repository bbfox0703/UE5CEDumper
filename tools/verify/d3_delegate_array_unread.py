"""An UNREADABLE multicast-delegate array element must render "???", not "(0 bindings)".

    py tools/verify/d3_delegate_array_unread.py     # DumperTest must be running + injected

THE DEFECT. If `Ubel::ReadMulticastDelegateArrayElements` drops the `Macht::ReadSafe` returns for
the element's inner `TArray<FScriptDelegate>` header, a faulted read leaves `innerData`/`innerCount`
at 0, so the element is published as the AFFIRMATIVE `"(0 bindings)"` and counted in
`readCount` — an assertion that a delegate provably has NO subscribers, made over memory
nobody could read. `Macht::ReadTArray` (Macht.h) validates only Count and Max and never probes
Data, so a freed buffer reaches the element loop with a sane-looking header.

⚠ THE FIXTURE. `Arr_MulticastDelegates` exists for exactly this row (see
`tools/ue-sample/README.md`): a `DumperTestActor` ArrayProperty with a `Multicast*` inner.
Without it this reader has no host at all and its unread arm can only be reached by a
synthetic in-process TArray.

Same manufacture-and-restore technique as `d5_lazyguid_unread.py`, and the same reason: on a
healthy process every element reads fine, so the fault arm cannot be observed by looking.
The WRITE is its own negative control — the walk must change, and must come back.

WHAT A PASS LOOKS LIKE
  1. baseline   [0] "(0 bindings)", [1] bound   <- READ. [1] is bound on purpose: with both
                                                   elements empty, a 16-byte and a 24-byte
                                                   stride print the SAME string, so the
                                                   stride would be unfalsifiable here.
  2. corrupted  2 elements, both "???"            <- the fix. Without it: "(0 bindings)",
                                                     i.e. identical to (1) — which is the
                                                     whole point: the two states are
                                                     INDISTINGUISHABLE
  3. restored   identical to (1)
⭐ (1) is not decoration here. Unlike the lazy-GUID row, the defect renders the SAME string
as the healthy case, so a rig that only checked (2) could not tell a working fix from a
broken walk.
"""
from __future__ import annotations

import json
import subprocess
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
ROOT = HERE.parent.parent
CLIENT = HERE / "pipe_client.py"

FIELD = "Arr_MulticastDelegates"
# BeginPlay binds element [1] to this and leaves [0] empty -- see d4b_delegate_pad.py for why
# two DIFFERENT elements are what makes the stride observable at all.
PROBE = "D4b_OnPingProbe"
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
    raise SystemExit("%s not on this actor — repackage the fixture (it was added 2026-09-08)"
                     % FIELD)


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
    data_le = raw["bytes"][:16]
    print("header     : Data=%s Count/Max=%s" % (data_le, raw["bytes"][16:]))
    print("inner      : %s  elem_size=%s" % (base.get("array_inner_type"),
                                             base.get("array_elem_size")))

    base_vals = values(base)
    print("baseline   : %s" % base_vals)
    # ⛔ ASSERT THE SHAPE, NOT A FROZEN LITERAL. `all(v == "(0 bindings)")` holds only while
    # both fixture elements are empty, and element [1] is bound (to make the element STRIDE
    # observable, see d4b_delegate_pad.py). A fixture change can silently break a passing
    # verification and nothing catches it: no gate reads these rigs, and a rig is only run
    # when someone remembers the row. So assert what the fixture actually guarantees.
    ok_base = (len(base_vals) == 2
               and base_vals[0] == "(0 bindings)"
               and PROBE in base_vals[1])

    dead = DEAD_PTR.to_bytes(8, "little").hex().upper()
    corrupted_vals = []
    ok_corrupt = False
    try:
        w = call("write_mem", {"addr": hex(hdr), "bytes": dead})
        if not w.get("ok"):
            raise SystemExit("write_mem failed: %s" % w)
        corrupted_vals = values(walk(addr, class_addr))
        print("corrupted  : %s" % corrupted_vals)
        ok_corrupt = bool(corrupted_vals) and all(v == "???" for v in corrupted_vals)
    finally:
        back = call("write_mem", {"addr": hex(hdr), "bytes": data_le})
        print("restore    : %s" % ("ok" if back.get("ok") else "FAILED -- %s" % back))

    after_vals = values(walk(addr, class_addr))
    ok_after = after_vals == base_vals
    print("restored   : %s" % ("identical to baseline" if ok_after else after_vals))

    fails = []
    if not ok_base:
        fails.append("baseline is not the expected [empty, bound-to-%s] pair — got %r. "
                     "The fixture binds Arr_MulticastDelegates[1] and leaves [0] empty; if "
                     "[1] reads empty too, the element STRIDE is wrong (D3b), and if the "
                     "field is missing the package is stale."
                     % (PROBE, base_vals))
    if not ok_corrupt:
        fails.append("an unreadable element did NOT render '???' — got %r" % (corrupted_vals,))
    if not ok_after:
        fails.append("the restore did not bring the baseline back")

    print()
    if fails:
        print("D3: FAIL")
        for f in fails:
            print("  - %s" % f)
        return 1
    print("D3: PASS — an unreadable delegate element renders '???' where it used to render the "
          "same '(0 bindings)' a genuinely-empty one does; the write is reversible.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
