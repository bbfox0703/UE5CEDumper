#!/usr/bin/env python3
r"""Live check for [MAINWINDOW-NAVIGATE-EXACT-NAME]: find_live_instance answers a live object by derivation, or none.

    py tools/verify/live_instance_lookup_live.py [--process DumperTest]

Written for the row [MAINWINDOW-NAVIGATE-EXACT-NAME] (docs/todo.md). Ten MainWindow handlers and Detect Stats' probe
looked for "a live instance" with find_instances(exact_match, limit 5) and skipped names starting Default__. This rig
puts that OLD pick beside the NEW one, find_live_instance, for the same class names, so each line shows what the
user's click used to land on and what it lands on now.

The template test is the rig's OWN, not the DLL's word: listinstances_templates_live.Rig.template_kind reads the
object's ObjectFlags with ReadProcessMemory and follows its outer chain through get_object (a class default object, a
default subobject of one, or an archetype is a template).

Checks (one line each, "<ID> OK|BAD  <detail>"; INFO lines are records, never counted):
  L1  "Character": find_live_instance picks a LIVE object whose class is a SUBCLASS (the third-person Blueprint's _C);
      INFO: the old exact-name pick (none expected -- every Character in the map is of a subclass)
  L2  "CharacterMovementComponent": find_live_instance picks a LIVE object; INFO: the old pick and its template kind
      (a class default object's default subobject is the defect the row describes)
  L3  "PlayerController": a live object
  L4  "CheatManager" (DumperTest Shipping has only its class default object): pick "none", no address
  L5  "KismetSystemLibrary" (a function library, never instantiated): pick "none"
  L6  a class that does not exist: pick "none"
  L7  no class_name: an error reply, not "none"
  L8  INFO: the round trip of each find_live_instance above (each one walks every object)

Prerequisites: DumperTest Shipping (launch_dumpertest.py shipping) on its third-person map, the DLL from dist\ injected
(pc.assert_build() refuses a stale one), the UI NOT connected (the rig owns the pipe).
Writes nothing to the game. Every reply is saved as it is read to out\MAINWINDOW-NAVIGATE-EXACT-NAME\<stamp>_replies.jsonl,
and the verdicts to <stamp>_summary.json. Exit 0 only when every check passed and at least one ran.
"""
from __future__ import annotations

import argparse
import json
import sys

import listinstances_templates_live as LT
import mailbox_poke as MB
from invoke_template_live import data, a2i
from pipe_client import PipeClient

TAG = "MAINWINDOW-NAVIGATE-EXACT-NAME"
NO_SUCH_CLASS = "NoSuchClass_NavigateExactName_7f3a"


def old_pick(rig, cls):
    """The handlers' old lookup, exactly: find_instances exact, 5 rows, the first whose name is not Default__*."""
    r = data(rig.ask("find_instances", class_name=cls, exact_match=True, limit=5))
    for inst in r.get("instances", []) or []:
        addr = a2i(inst.get("addr", "0") or "0")
        if addr and not (inst.get("name", "") or "").startswith("Default__"):
            return addr, inst.get("name", ""), inst.get("class", "")
    return 0, "", ""


def new_pick(rig, cls):
    reply = rig.ask("find_live_instance", class_name=cls)
    ms = rig.last_ms
    d = data(reply)
    return reply, d, a2i(d.get("addr", "0") or "0"), ms


def describe(rig, addr):
    if not addr:
        return "none", ""
    kind, owner = rig.template_kind(addr)
    return (kind or LT.LIVE), owner


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--process", default="DumperTest", help="game process name prefix, or a PID")
    a = ap.parse_args()

    pid = int(a.process) if a.process.isdigit() else MB.pid_of(a.process)
    m = MB.Mem(pid)
    ev = LT.Evidence(TAG)
    summary = {"tag": TAG, "stamp": ev.stamp, "process": a.process, "pid": pid, "replies": str(ev.replies)}
    rig = None
    timings = {}
    try:
        with PipeClient(timeout=180) as pc:
            rig = LT.Rig(pc, m, 0, ev)
            try:
                build = pc.assert_build()
                print("build:", build, flush=True)
                summary["build"] = build
                pc.ensure_scanned()
                rig.ask("get_pointers")

                # L1 / L2 / L3: a live object, by derivation, never a template.
                for cid, cls, want_subclass in (("L1", "Character", True),
                                                ("L2", "CharacterMovementComponent", False),
                                                ("L3", "PlayerController", False)):
                    o_addr, o_name, o_cls = old_pick(rig, cls)
                    o_kind, o_owner = describe(rig, o_addr)
                    rig.info(f"{cid}-old", f"find_instances exact '{cls}' first non-Default__: "
                             f"{hex(o_addr) if o_addr else 'none'} {o_name} ({o_cls}) -> {o_kind}"
                             + (f" of {o_owner}" if o_owner else ""),
                             old_addr=hex(o_addr), old_kind=o_kind)
                    _, d, addr, ms = new_pick(rig, cls)
                    timings[cls] = round(ms, 1)
                    kind, owner = describe(rig, addr)
                    obj_cls = d.get("class", "")
                    ok = (bool(addr) and d.get("pick") in ("live", "debug_camera_owned") and kind == LT.LIVE
                          and (obj_cls != cls if want_subclass else True))
                    rig.verdict(cid, ok,
                                f"find_live_instance '{cls}': pick={d.get('pick')} {hex(addr) if addr else '-'} "
                                f"{d.get('name', '')} (class {obj_cls}) -> rig says {kind}"
                                + (f" of {owner}" if owner else "")
                                + (" [want a subclass]" if want_subclass else ""),
                                addr=hex(addr), kind=kind, cls=obj_cls)

                # L4 / L5 / L6: nothing live answers none, with no address.
                for cid, cls in (("L4", "CheatManager"), ("L5", "KismetSystemLibrary"), ("L6", NO_SUCH_CLASS)):
                    reply, d, addr, ms = new_pick(rig, cls)
                    timings[cls] = round(ms, 1)
                    ok = reply.get("ok", True) is not False and d.get("pick") == "none" and not addr \
                        and "addr" not in d
                    rig.verdict(cid, ok, f"find_live_instance '{cls}': ok={reply.get('ok')} pick={d.get('pick')} "
                                f"addr={d.get('addr', '(absent)')}")

                # L7: a request with no class is an error, not "none".
                reply = rig.ask("find_live_instance")
                rig.verdict("L7", reply.get("ok") is False and bool(reply.get("error")),
                            f"no class_name: ok={reply.get('ok')} error={reply.get('error')!r}")

                rig.info("L8", "round trips (ms): " + ", ".join(f"{k} {v}" for k, v in timings.items()),
                         timings=timings)
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
