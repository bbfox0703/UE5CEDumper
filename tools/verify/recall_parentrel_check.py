#!/usr/bin/env python3
r"""Live check for [TELEPORT-RECALL-PARENTREL] (build 3680 on): DumperTest Shipping, headless, no CE.

    py tools/verify/l78_parentrel_arm.py find          # once per launch, then:
    py tools/verify/recall_parentrel_check.py

A parent-relative pose is staged with l78_parentrel_arm.py's verbs (attach, degrade, heal, detach, restore) and the
CE mailbox's CMD_TELEPORT is driven directly, as bugitgo_parentrel_check.py does: inputs first, `cmd` last, a real
wall-clock deadline, a loud failure on a short read. Keep the UI closed (l78's rule: it takes pipe slots).

  A. healthy: SAVE marker 1 (world), then EXPLICIT to where the pawn stands -- a jump that does not move it -- so the
     "last" slot holds that world pose, L0.
  B. degrade, SAVE marker 2: pose flags bit0 must be 1 -- the marker is parent-relative. R = its numbers.
  C. RECALL 2, RECALL_FORCE 2 and RELATIVE 100 uu: each -16, tier [177] 0, and GET_LAST still L0.
  D. option B: EXPLICIT to the pawn's world position while degraded. The jump runs (its invokes stay queued until the
     thread resumes, and land it where it stood), but the undo point must stay L0 -- not R, with no flag.
  E. heal: the pawn ends within 50 uu of where it stood; RECALL 1 (the world marker) answers 0 -- the refusal is not
     sticky.
Then detach and restore, whatever happened.
"""
from __future__ import annotations

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
TP_OP_SAVE, TP_OP_RECALL, TP_OP_RECALL_FORCE = 1, 2, 3
TP_OP_GET_LAST, TP_OP_RELATIVE, TP_OP_EXPLICIT = 8, 12, 13
TP_ERR_PARENT_RELATIVE = -16


def l78(verb: str) -> None:
    r = subprocess.run([sys.executable, str(HERE / "l78_parentrel_arm.py"), verb], capture_output=True, text=True,
                       encoding="utf-8", errors="replace")
    print(f"   [l78 {verb}] rc={r.returncode}")
    for line in (r.stdout + r.stderr).strip().splitlines()[-4:]:
        print("      " + line)
    if r.returncode != 0:
        raise SystemExit(f"l78 {verb} failed")


def teleport(m: MB.Mem, base: int, op: int, slot: int = 0, params: bytes = b"", timeout: float = 20.0):
    """One CMD_TELEPORT round trip. Returns (result, six doubles, tier [177], flags [178], errorMsg)."""
    if m.i32(base + MB.OFF_STATUS) == MB.STATUS_PROCESSING:
        raise SystemExit("mailbox is PROCESSING before the command -- a previous one is wedged")
    m.write(base + MB.OFF_STATUS, struct.pack("<i", MB.STATUS_IDLE))
    m.write(base + MB.OFF_INSTANCE, struct.pack("<Q", op))
    m.write(base + MB.OFF_UFUNC, struct.pack("<Q", slot))
    m.write(base + MB.OFF_RESULT, struct.pack("<i", 0x7FFFFFFF))   # poison: the answer must be WRITTEN
    m.write(base + MB.OFF_PARAMS, params + b"\xAA" * (180 - len(params)))
    m.write(base + MB.OFF_CMD, struct.pack("<i", CMD_TELEPORT))      # cmd LAST
    t0 = time.time()
    while time.time() - t0 < timeout:
        if m.i32(base + MB.OFF_STATUS) == MB.STATUS_DONE:
            res = m.i32(base + MB.OFF_RESULT)
            out = m.read(base + MB.OFF_PARAMS, 180)
            err = m.read(base + MB.OFF_ERRORMSG, 256).split(b"\x00")[0].decode("utf-8", "replace")
            m.write(base + MB.OFF_CMD, struct.pack("<i", MB.CMD_IDLE))
            return res, struct.unpack_from("<6d", out, 0), out[177], out[178], err
        time.sleep(0.005)
    raise SystemExit(f"CMD_TELEPORT op {op}: TIMEOUT after {timeout}s, status={m.i32(base + MB.OFF_STATUS):#x}")


def explicit(m, base, xyz):
    return teleport(m, base, TP_OP_EXPLICIT, params=struct.pack("<6d", *xyz, 0.0, 0.0, 0.0) + b"\x00")


def get_last(m, base):
    res, p, _, flags, _ = teleport(m, base, TP_OP_GET_LAST)
    return res, p[:3], flags


def data(reply):
    return reply.get("data", reply)


def pose(pc: PipeClient):
    d = data(pc.request("teleport_get_pose"))
    if d.get("code", 0) != 0:
        raise SystemExit(f"teleport_get_pose answered code {d.get('code')}: {d}")
    return (float(d["x"]), float(d["y"]), float(d["z"])), bool(d.get("parent_relative", False))


def fmt(v) -> str:
    return "(" + ", ".join(f"{c:.1f}" for c in v[:3]) + ")"


def main() -> int:
    pid = MB.pid_of(PROC)
    base = MB.mailbox_addr(str(pid))
    m = MB.Mem(pid)
    if m.i32(base + MB.OFF_INITSTATE) != MB.INIT_READY:
        raise SystemExit("mailbox initState is not READY")
    bad = 0

    def verdict(label, ok, detail):
        nonlocal bad
        bad += not ok
        print(f"{label:3} {'OK ' if ok else 'BAD'}  {detail}")

    l78("attach")
    try:
        with PipeClient(timeout=60) as pc:
            print("build:", pc.assert_build())

            # A -- a world marker, and a world undo point.
            w0, w0rel = pose(pc)
            res, _, _, flags, _ = teleport(m, base, TP_OP_SAVE, slot=0)
            verdict("A1", res == 0 and not w0rel and not (flags & 1), f"SAVE 1 healthy: result={res} flags={flags:#04x} W0={fmt(w0)}")
            res, _, tier, _, err = explicit(m, base, w0)
            r0, l0, l0flags = get_last(m, base)
            verdict("A2", res == 0 and r0 == 0 and math.dist(l0, w0) < 1 and not (l0flags & 1),
                    f"EXPLICIT in place: result={res} tier={tier}; last={fmt(l0)} flags={l0flags:#04x}")

            # B -- a parent-relative marker.
            l78("degrade")
            res, r, _, flags, _ = teleport(m, base, TP_OP_SAVE, slot=1)
            verdict("B", res == 0 and (flags & 1) == 1, f"SAVE 2 degraded: result={res} flags={flags:#04x} R={fmt(r)}")
            if res != 0 or not (flags & 1):
                print("the degraded marker is not flagged parent-relative -- nothing below would mean anything")
                return 1

            # C -- Recall, Force and TP facing refuse it, before the undo point.
            for label, op, slot, params in (("C1", TP_OP_RECALL, 1, b""), ("C2", TP_OP_RECALL_FORCE, 1, b""),
                                            ("C3", TP_OP_RELATIVE, 0, struct.pack("<d", 100.0) + b"\x00")):
                res, _, tier, _, err = teleport(m, base, op, slot=slot, params=params)
                rl, lp, _ = get_last(m, base)
                verdict(label, res == TP_ERR_PARENT_RELATIVE and tier == 0 and "-16" in err
                        and rl == 0 and math.dist(lp, l0) < 1,
                        f"op {op} slot {slot + 1}: result={res} tier={tier} err={err!r}; last {fmt(lp)}")

            # D -- option B: a jump whose pre-jump pose is parent-relative leaves the undo point alone.
            res, _, tier, _, err = explicit(m, base, w0)
            rl, lp, lflags = get_last(m, base)
            verdict("D", rl == 0 and math.dist(lp, l0) < 1 and not (lflags & 1),
                    f"EXPLICIT degraded: result={res} tier={tier}; last {fmt(lp)} flags={lflags:#04x} "
                    f"(L0 {fmt(l0)}, R {fmt(r)})")

            # E -- healed: the queued jump lands where the pawn stood, and a world marker still recalls.
            l78("heal")
            time.sleep(2.0)
            p1, p1rel = pose(pc)
            verdict("E1", not p1rel and math.dist(p1, w0) < 50, f"after heal: pose {fmt(p1)} (W0 {fmt(w0)})")
            res, _, tier, _, err = teleport(m, base, TP_OP_RECALL, slot=0)
            verdict("E2", res == 0, f"RECALL 1 (world) healed: result={res} tier={tier}" + (f" err={err!r}" if err else ""))
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
