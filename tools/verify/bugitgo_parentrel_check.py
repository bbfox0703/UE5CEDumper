#!/usr/bin/env python3
r"""Live check for [TELEPORT-BUGITGO-PARENTREL] (build 3675 on): DumperTest, headless, no CE.

    py tools/verify/l78_parentrel_arm.py find                    # once per launch, then:
    py tools/verify/bugitgo_parentrel_check.py --expect refuse   # the fixed DLL
    py tools/verify/bugitgo_parentrel_check.py --expect jump     # the red arm: a DLL from before the fix

A parent-relative BugIt is staged with l78_parentrel_arm.py's verbs (attach, degrade, heal, detach, restore) and the
CE mailbox's CMD_TELEPORT is driven directly, the way mailbox_poke.py does it: inputs first, `cmd` last, a real
wall-clock deadline, a loud failure on any short read.

  B. degrade, then BUGIT_SAVE: pose flags bit0 must be 1 -- the stored BugIt is parent-relative. S = its numbers.
  C. heal, so a jump really runs through the world-space invoke. P0 = the pose, L0 = the "last" slot.
     |S - P0| must be at least 200 uu: with the parent near the origin a jump and a refusal look the same (MISS).
  D. BUGIT_GO.  --expect refuse: result -16, tier [177] 0, the pawn within 10 uu of P0, "last" unchanged.
                --expect jump:   result 0 and the pawn moved by at least 100 uu -- the defect, measured.
  E. control, the refusal is not sticky: BUGIT_SAVE on the healed pawn (flags 0), then BUGIT_GO answers 0.
Then detach and restore, whatever happened. Keep the UI closed (l78's rule: it takes pipe slots).
"""
from __future__ import annotations

import argparse
import math
import pathlib
import struct
import subprocess
import sys
import time

HERE = pathlib.Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
sys.stdout.reconfigure(encoding="utf-8", errors="replace")
from pipe_client import PipeClient  # noqa: E402
import mailbox_poke as MB  # noqa: E402

PROC = "DumperTest-Win64-Shipping"
CMD_TELEPORT = 8
TP_OP_BUGIT_SAVE, TP_OP_BUGIT_GO = 9, 10
TP_ERR_PARENT_RELATIVE = -16


def l78(verb: str) -> None:
    r = subprocess.run([sys.executable, str(HERE / "l78_parentrel_arm.py"), verb], capture_output=True, text=True,
                       encoding="utf-8", errors="replace")
    print(f"   [l78 {verb}] rc={r.returncode}")
    for line in (r.stdout + r.stderr).strip().splitlines()[-6:]:
        print("      " + line)
    if r.returncode != 0:
        raise SystemExit(f"l78 {verb} failed")


def teleport(m: MB.Mem, base: int, op: int, timeout: float = 15.0):
    """One CMD_TELEPORT round trip. Returns (result, six doubles, tier [177], flags [178], errorMsg)."""
    if m.i32(base + MB.OFF_STATUS) == MB.STATUS_PROCESSING:
        raise SystemExit("mailbox is PROCESSING before the command -- a previous one is wedged")
    m.write(base + MB.OFF_STATUS, struct.pack("<i", MB.STATUS_IDLE))
    m.write(base + MB.OFF_INSTANCE, struct.pack("<Q", op))
    m.write(base + MB.OFF_UFUNC, struct.pack("<Q", 0))
    m.write(base + MB.OFF_RESULT, struct.pack("<i", 0x7FFFFFFF))   # poison: the answer must be WRITTEN
    m.write(base + MB.OFF_PARAMS, b"\xAA" * 180)                      # so a zero tier / flag is real
    m.write(base + MB.OFF_CMD, struct.pack("<i", CMD_TELEPORT))      # cmd LAST
    t0 = time.time()
    while time.time() - t0 < timeout:
        if m.i32(base + MB.OFF_STATUS) == MB.STATUS_DONE:
            res = m.i32(base + MB.OFF_RESULT)
            params = m.read(base + MB.OFF_PARAMS, 180)
            err = m.read(base + MB.OFF_ERRORMSG, 256).split(b"\x00")[0].decode("utf-8", "replace")
            m.write(base + MB.OFF_CMD, struct.pack("<i", MB.CMD_IDLE))
            return res, struct.unpack_from("<6d", params, 0), params[177], params[178], err
        time.sleep(0.005)
    raise SystemExit(f"CMD_TELEPORT op {op}: TIMEOUT after {timeout}s, status={m.i32(base + MB.OFF_STATUS):#x}")


def data(reply):
    return reply.get("data", reply)


def pose(pc: PipeClient):
    d = data(pc.request("teleport_get_pose"))
    if d.get("code", 0) != 0:
        raise SystemExit(f"teleport_get_pose answered code {d.get('code')}: {d}")
    return (float(d["x"]), float(d["y"]), float(d["z"])), bool(d.get("parent_relative", False))


def last_slot(pc: PipeClient):
    for e in data(pc.request("teleport_get_markers")).get("markers", []):
        if e.get("slot") == -1:
            return e
    return {}


def dist(a, b) -> float:
    return math.dist(a[:3], b[:3])


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    ap.add_argument("--expect", choices=("refuse", "jump"), required=True)
    a = ap.parse_args()

    pid = MB.pid_of(PROC)
    base = MB.mailbox_addr(str(pid))
    m = MB.Mem(pid)
    if m.i32(base + MB.OFF_INITSTATE) != MB.INIT_READY:
        raise SystemExit("mailbox initState is not READY")
    bad = 0

    l78("attach")
    try:
        with PipeClient(timeout=60) as pc:
            print("build:", pc.assert_build(None) if a.expect == "refuse" else
                  data(pc.request("get_pointers")).get("build_number"))

            # B -- a parent-relative BugIt.
            l78("degrade")
            res, s, tier, flags, err = teleport(m, base, TP_OP_BUGIT_SAVE)
            print(f"B. BUGIT_SAVE degraded: result={res} flags={flags:#04x} S=({s[0]:.1f}, {s[1]:.1f}, {s[2]:.1f})")
            if res != 0 or not (flags & 1):
                print("BAD: the degraded BugIt is not flagged parent-relative -- nothing below would mean anything")
                return 1

            # C -- healthy again, so a jump runs through the world-space invoke.
            l78("heal")
            p0, p0rel = pose(pc)
            l0 = last_slot(pc)
            print(f"C. P0=({p0[0]:.1f}, {p0[1]:.1f}, {p0[2]:.1f}) parent_relative={p0rel}  "
                  f"last={'valid' if l0.get('valid') else 'empty'}")
            gap = dist(s, p0)
            if p0rel or gap < 200:
                print(f"MISS: |S - P0| = {gap:.1f} uu (parent_relative={p0rel}) -- a jump would land where the pawn "
                      "already is; re-stage with a parent farther from the origin")
                return 2

            # D -- the jump.
            res, _, tier, _, err = teleport(m, base, TP_OP_BUGIT_GO)
            time.sleep(0.5)
            p1, _ = pose(pc)
            l1 = last_slot(pc)
            moved = dist(p0, p1)
            print(f"D. BUGIT_GO: result={res} tier={tier} err={err!r}  moved {moved:.1f} uu  "
                  f"last {'unchanged' if l1 == l0 else 'CHANGED'}")
            if a.expect == "refuse":
                ok = res == TP_ERR_PARENT_RELATIVE and tier == 0 and moved < 10 and l1 == l0 and "-16" in err
            else:
                ok = res == 0 and moved >= 100
            print(f"   -> {'OK' if ok else 'BAD'} (expect {a.expect})")
            bad += not ok

            # E -- not sticky.
            if a.expect == "refuse":
                res, _, _, flags, _ = teleport(m, base, TP_OP_BUGIT_SAVE)
                res2, _, tier2, _, err2 = teleport(m, base, TP_OP_BUGIT_GO)
                ok = res == 0 and not (flags & 1) and res2 == 0
                print(f"E. healed BUGIT_SAVE flags={flags:#04x}, BUGIT_GO result={res2} tier={tier2} "
                      f"-> {'OK' if ok else 'BAD'}" + (f" err={err2!r}" if err2 else ""))
                bad += not ok
    finally:
        try:
            l78("heal")
        except SystemExit:
            pass
        l78("detach")
        l78("restore")

    print("RESULT:", "PASS" if bad == 0 else f"FAIL ({bad})")
    return 0 if bad == 0 else 1


if __name__ == "__main__":
    sys.exit(main())
