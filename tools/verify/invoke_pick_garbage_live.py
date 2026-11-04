#!/usr/bin/env python3
r"""Live check for [INVOKE-PICK-GARBAGE]: an invoke by class name never lands on a destroyed object GC has not collected.

    py tools/verify/invoke_pick_garbage_live.py [--process DumperTest] [--class StaticMeshActor]

Written for the row [INVOKE-PICK-GARBAGE] (docs/todo.md). DestroyActor marks an actor garbage, and it stays in GObjects
until the next garbage collection (60 s by default), so the class-name pick -- which takes the first live object in
object order -- would land on a destroyed actor that sits lowest. This rig makes one: it destroys the object the pick
lands on, then asks again within the collection window, through every path that resolves a class name.

The garbage test is the rig's OWN, not the DLL's word: the destroyed object's ObjectFlags are read with
ReadProcessMemory (UE5 RF_MirroredGarbage, 0x40000000), and get_object shows it still in GObjects.

Checks (one line each, "<ID> OK|BAD  <detail>"; INFO lines are records, never counted):
  P0  before: invoke_function class_name=<class> K2_GetActorLocation lands on a live object X (pick "live")
  P1  invoke_function instance_addr=X K2_DestroyActor answers ok
  G1  X is still in GObjects (get_object names it) and its ObjectFlags carry RF_MirroredGarbage -- the case is real
  P2  invoke_function class_name=<class> K2_GetActorLocation again: ok, and NOT on X
  P3  find_live_instance <class>: an object, NOT X
  P4  the CE mailbox's FIND_INSTANCE <class>: an object, NOT X
  INFO the old navigate lookup (find_instances exact, the first not named Default__) still lists X -- what a garbage-blind
       pick met first; and the seconds since the destroy (the checks are only meaningful well inside the GC window)

Prerequisites: DumperTest Shipping (launch_dumpertest.py shipping) on its third-person map, the DLL from dist\ injected
(pc.assert_build() refuses a stale one), the UI NOT connected, no Cheat Engine. A build from 3685 on.
WRITES: destroys ONE actor of --class in the running fixture (by default a StaticMeshActor -- possibly the floor: close
the game afterwards). Every reply is saved to out\INVOKE-PICK-GARBAGE\<stamp>_replies.jsonl and the verdicts to
<stamp>_summary.json. Exit 0 only when every check passed and at least one ran.
"""
from __future__ import annotations

import argparse
import json
import struct
import sys
import time

import listinstances_templates_live as LT
import mailbox_poke as MB
from invoke_template_live import data, a2i, cmd, CMD_FIND_INSTANCE
from pipe_client import PipeClient

TAG = "INVOKE-PICK-GARBAGE"
RF_MIRRORED_GARBAGE = 0x40000000
GC_WINDOW_S = 60.0


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--process", default="DumperTest", help="game process name prefix, or a PID")
    ap.add_argument("--class", dest="cls", default="StaticMeshActor", help="the actor class to destroy one of")
    a = ap.parse_args()

    pid = int(a.process) if a.process.isdigit() else MB.pid_of(a.process)
    m = MB.Mem(pid)
    base = MB.mailbox_addr(str(pid))
    ev = LT.Evidence(TAG)
    summary = {"tag": TAG, "stamp": ev.stamp, "process": a.process, "pid": pid, "class": a.cls,
               "replies": str(ev.replies)}
    rig = None
    try:
        with PipeClient(timeout=180) as pc:
            rig = LT.Rig(pc, m, base, ev)
            try:
                build = pc.assert_build()
                print("build:", build, flush=True)
                summary["build"] = build
                pc.ensure_scanned()

                def by_name():
                    r = rig.ask("invoke_function", class_name=a.cls, func_name="K2_GetActorLocation")
                    d = data(r)
                    return r, d, a2i(d.get("instance_addr", "0") or "0")

                r, d, x = by_name()
                rig.verdict("P0", r.get("ok") and d.get("result") == 0 and d.get("instance_pick") == "live" and x,
                            f"by name: pick={d.get('instance_pick')} {d.get('instance_name')} @ {hex(x)}")
                if not x:
                    raise SystemExit("no live object to destroy")
                r = rig.ask("invoke_function", instance_addr=hex(x), func_name="K2_DestroyActor")
                t_destroy = time.time()
                rig.verdict("P1", bool(r.get("ok")) and data(r).get("result") == 0,
                            f"K2_DestroyActor on {hex(x)}: ok={r.get('ok')} result={data(r).get('result')} "
                            f"error={r.get('error')!r}")
                time.sleep(0.5)   # the destroy runs on the game thread; let it land before reading the flags

                g = data(rig.ask("get_object", addr=hex(x)))
                flags = struct.unpack("<I", m.read(x + LT.OFF_UOBJECT_FLAGS, 4))[0]
                ev.add("memory", {"read": "ObjectFlags", "addr": hex(x + LT.OFF_UOBJECT_FLAGS)}, {"flags": hex(flags)})
                rig.verdict("G1", bool(g.get("name")) and bool(flags & RF_MIRRORED_GARBAGE),
                            f"{hex(x)} still '{g.get('name')}' in GObjects, ObjectFlags={flags:#x} "
                            f"(RF_MirroredGarbage {'set' if flags & RF_MIRRORED_GARBAGE else 'NOT set'})")

                r, d, x2 = by_name()
                rig.verdict("P2", r.get("ok") and d.get("result") == 0 and x2 and x2 != x,
                            f"by name again: pick={d.get('instance_pick')} {d.get('instance_name')} @ {hex(x2)} "
                            f"(the destroyed one is {hex(x)})")

                fl = data(rig.ask("find_live_instance", class_name=a.cls))
                x3 = a2i(fl.get("addr", "0") or "0")
                rig.verdict("P3", x3 and x3 != x, f"find_live_instance: pick={fl.get('pick')} {fl.get('name')} @ {hex(x3)}")

                res, x4, _, out, err = cmd(m, base, CMD_FIND_INSTANCE, cls=a.cls)
                ev.add("mailbox", {"cmd": "FIND_INSTANCE", "class": a.cls},
                       {"result": res, "instance": hex(x4 or 0), "outflags": out, "error": err})
                rig.verdict("P4", res == 0 and x4 and x4 != x,
                            f"mailbox FIND_INSTANCE: result={res} instance={hex(x4 or 0)} outflags={out:#x} {err}")

                old = data(rig.ask("find_instances", class_name=a.cls, exact_match=True, limit=5))
                first = next((i for i in old.get("instances", []) or []
                              if i.get("addr") and not str(i.get("name", "")).startswith("Default__")), {})
                elapsed = time.time() - t_destroy
                rig.info("INFO", f"the old exact-name lookup's first: {first.get('name')} @ {first.get('addr')} "
                         f"(the destroyed one {'IS' if a2i(first.get('addr', '0') or '0') == x else 'is not'} it); "
                         f"{elapsed:.1f} s since the destroy (GC window ~{GC_WINDOW_S:.0f} s)",
                         seconds=round(elapsed, 1))
                if elapsed > GC_WINDOW_S * 0.8:
                    rig.verdict("T1", False, f"{elapsed:.1f} s since the destroy: too close to a collection to trust")
            except (Exception, SystemExit) as e:
                rig.verdict("ABORT", False, f"{type(e).__name__}: {e}")
    finally:
        if rig is not None:
            summary["checks"] = rig.checks
        ev.close()
        ev.summary.write_text(json.dumps(summary, indent=1, ensure_ascii=False, default=str), encoding="utf-8",
                              newline="\n")
        print(f"evidence: {ev.replies}\nsummary:  {ev.summary}", flush=True)

    counted = [c for c in (rig.checks if rig else []) if c["verdict"] in ("OK", "BAD")]
    bad = sum(c["verdict"] == "BAD" for c in counted)
    if not counted:
        print("RESULT: FAIL (no check ran)")
        return 1
    print("RESULT:", f"PASS ({len(counted)} checks)" if bad == 0 else f"FAIL ({bad} of {len(counted)})")
    return 0 if bad == 0 else 1


if __name__ == "__main__":
    sys.exit(main())
