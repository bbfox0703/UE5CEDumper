#!/usr/bin/env python3
r"""Live check for [FINDINSTANCE-SUBSTRING-USERFACING] (build 3675 on): DumperTest, headless, no CE.

    py tools/verify/findinstance_pick_live.py

The UI must NOT be connected. For each class name it prints the OLD pick beside the new one: the old rule is
recomputed from `find_instances exact_match=false limit=100` (the first non-CDO match, else the first match), which
is what the C ABI export, the pipe's invoke_function class_name and the mailbox's FIND_INSTANCE all did.

Pipe (invoke_function class_name):
  P1 "Actor" / K2_GetActorLocation      -> result 0, instance_pick "live", the class's super chain reaches Actor
  P2 "DumperTestActor" / K2_GetActorLocation -> "live", the chain reaches DumperTestActor
  P3 "DumperTestAct" (no class has it)  -> "partial_name", the same object the old rule took
  P4 "KismetSystemLibrary" / IsPackagedForDistribution, direct_call -> "class_default", Default__KismetSystemLibrary
  P5 "KismetSystemLib"                  -> "partial_name_default", the same function's call
  P6 "NoSuchClass_Xyz"                  -> ok:false, "No instance found for class: NoSuchClass_Xyz"
  P7 "Actor" / NoSuchFunc_Xyz           -> ok:false, "Function not found: NoSuchFunc_Xyz on <object> (class <class>)"
Mailbox (CMD_FIND_INSTANCE, driven like mailbox_poke.py):
  M1 g_mailboxContract reads current 6, min 1
  M2 "Actor" -> result 0 and the object P1 used (both lanes share one resolver)
  M3 "NoSuchClass_Xyz" -> -2, "No instance of 'NoSuchClass_Xyz' found (scanned=...)"
  M4 "DumperTestAct" -> the object P3 used
  M5 "CheatManager" -> reported only (a Shipping build usually has no live CheatManager); never an extension
"""
from __future__ import annotations

import pathlib
import struct
import sys
import time

HERE = pathlib.Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
sys.stdout.reconfigure(encoding="utf-8", errors="replace")
from pipe_client import PipeClient  # noqa: E402
import mailbox_poke as MB  # noqa: E402
import mailbox_addr as MA  # noqa: E402

PROC = "DumperTest-Win64-Shipping"
CMD_FIND_INSTANCE = 2
OFF_CLASSNAME = 0x028


def data(reply):
    return reply.get("data", reply)


def old_pick(pc, q):
    d = data(pc.request("find_instances", class_name=q, exact_match=False, limit=100))
    rows = d.get("instances") or []
    for r in rows:
        if "Default__" not in str(r.get("name", "")):
            return r.get("addr", ""), r.get("name", "")
    return (rows[0].get("addr", ""), rows[0].get("name", "")) if rows else ("", "")


def chain_names(pc, cls_addr):
    names = []
    for _ in range(24):
        if not cls_addr or cls_addr in ("0", "0x0"):
            break
        c = data(pc.request("walk_class", addr=cls_addr)).get("class") or {}
        if not c.get("name"):
            break
        names.append(c["name"])
        cls_addr = c.get("super_addr", "")
    return names


def same_addr(a, b):
    try:
        return int(str(a), 16) == int(str(b), 16)
    except ValueError:
        return False


def find_instance(m, base, name, timeout=60.0):
    """One CMD_FIND_INSTANCE round trip: (result, instanceAddr, errorMsg)."""
    if m.i32(base + MB.OFF_STATUS) == MB.STATUS_PROCESSING:
        raise SystemExit("mailbox is PROCESSING before the command -- a previous one is wedged")
    m.write(base + MB.OFF_STATUS, struct.pack("<i", MB.STATUS_IDLE))
    raw = name.encode("utf-8")[:255]
    m.write(base + OFF_CLASSNAME, raw + b"\x00" * (256 - len(raw)))
    m.write(base + MB.OFF_INSTANCE, struct.pack("<Q", 0xDEADBEEF))   # poison: the answer must be WRITTEN
    m.write(base + MB.OFF_RESULT, struct.pack("<i", 0x7FFFFFFF))
    m.write(base + MB.OFF_CMD, struct.pack("<i", CMD_FIND_INSTANCE))  # cmd LAST
    t0 = time.time()
    while time.time() - t0 < timeout:
        if m.i32(base + MB.OFF_STATUS) == MB.STATUS_DONE:
            res = m.i32(base + MB.OFF_RESULT)
            inst = m.u64(base + MB.OFF_INSTANCE)
            err = m.read(base + MB.OFF_ERRORMSG, 256).split(b"\x00")[0].decode("utf-8", "replace")
            m.write(base + MB.OFF_CMD, struct.pack("<i", MB.CMD_IDLE))
            return res, inst, err
        time.sleep(0.005)
    raise SystemExit(f"FIND_INSTANCE {name!r}: TIMEOUT after {timeout}s")


def main() -> int:
    bad = 0

    def verdict(label, ok, detail):
        nonlocal bad
        bad += not ok
        print(f"{label:4} {'OK ' if ok else 'BAD'}  {detail}")

    with PipeClient(timeout=180) as pc:
        print("build:", pc.assert_build())
        pc.ensure_scanned()

        def invoke(q, fn, direct=False):
            t0 = time.time()
            r = pc.request("invoke_function", class_name=q, func_name=fn, direct_call=direct)
            return r, (time.time() - t0) * 1000

        def show(q, r, ms):
            d = data(r)
            oa, on = old_pick(pc, q)
            print(f"     {q!r}: ok={r.get('ok')} result={d.get('result')} pick={d.get('instance_pick')!r} "
                  f"-> {d.get('instance_name')} ({d.get('instance_class_name')}) @ {d.get('instance_addr')} "
                  f"[{ms:.0f} ms]   old pick: {on} @ {oa}" + (f"   error={r.get('error')!r}" if r.get('error') else ""))
            return d, oa

        r, ms = invoke("Actor", "K2_GetActorLocation")
        d1, _ = show("Actor", r, ms)
        chain = chain_names(pc, d1.get("instance_class_addr"))
        verdict("P1", r.get("ok") and d1.get("result") == 0 and d1.get("instance_pick") == "live" and "Actor" in chain,
                f"chain {' > '.join(chain[:6])}")

        r, ms = invoke("DumperTestActor", "K2_GetActorLocation")
        d2, _ = show("DumperTestActor", r, ms)
        chain = chain_names(pc, d2.get("instance_class_addr"))
        verdict("P2", r.get("ok") and d2.get("instance_pick") == "live" and "DumperTestActor" in chain,
                f"chain {' > '.join(chain[:4])}")

        r, ms = invoke("DumperTestAct", "K2_GetActorLocation")
        d3, old3 = show("DumperTestAct", r, ms)
        verdict("P3", d3.get("instance_pick") == "partial_name" and same_addr(d3.get("instance_addr"), old3),
                "a partial name lands where it always did, and says so")

        r, ms = invoke("KismetSystemLibrary", "IsPackagedForDistribution", direct=True)
        d4, _ = show("KismetSystemLibrary", r, ms)
        verdict("P4", d4.get("instance_pick") == "class_default"
                and d4.get("instance_name") == "Default__KismetSystemLibrary", f"{ms:.0f} ms for a full walk")

        r, ms = invoke("KismetSystemLib", "IsPackagedForDistribution", direct=True)
        d5, _ = show("KismetSystemLib", r, ms)
        verdict("P5", d5.get("instance_pick") == "partial_name_default", "")

        r, ms = invoke("NoSuchClass_Xyz", "K2_GetActorLocation")
        verdict("P6", not r.get("ok") and str(r.get("error", "")) == "No instance found for class: NoSuchClass_Xyz",
                repr(r.get("error")))

        r, ms = invoke("Actor", "NoSuchFunc_Xyz")
        err = str(r.get("error", ""))
        verdict("P7", not r.get("ok") and err.startswith("Function not found: NoSuchFunc_Xyz on ") and "(class " in err,
                repr(err))

        pid = MB.pid_of(PROC)
        base = MB.mailbox_addr(str(pid))
        m = MB.Mem(pid)

        cv = None
        rd = MA.Reader(pid)
        for path, mbase in MA.modules(pid):
            if mbase and path.lower().endswith(("ue5dumper.dll", "version.dll", "dxgi.dll", "dinput8.dll", "winmm.dll")):
                try:
                    cv = MA.find_export(rd, mbase, "g_mailboxContract")
                except SystemExit:
                    cv = None
                if cv:
                    break
        if cv:
            magic, cur, lo = struct.unpack("<III", m.read(cv, 12))
            verdict("M1", cur == 6 and lo == 1, f"magic={magic:#x} current={cur} min={lo}")
        else:
            verdict("M1", False, "g_mailboxContract not found")

        res, inst, err = find_instance(m, base, "Actor")
        verdict("M2", res == 0 and same_addr(hex(inst), d1.get("instance_addr")), f"result={res} -> {inst:#x}")
        res, inst, err = find_instance(m, base, "NoSuchClass_Xyz")
        verdict("M3", res == -2 and err.startswith("No instance of 'NoSuchClass_Xyz' found"), f"result={res} {err!r}")
        res, inst, err = find_instance(m, base, "DumperTestAct")
        verdict("M4", res == 0 and same_addr(hex(inst), d3.get("instance_addr")), f"result={res} -> {inst:#x}")
        res, inst, err = find_instance(m, base, "CheatManager")
        name = data(pc.request("get_object", addr=hex(inst))).get("name", "") if res == 0 and inst else ""
        cls = data(pc.request("get_object", addr=hex(inst))).get("class", "") if res == 0 and inst else ""
        oa, on = old_pick(pc, "CheatManager")
        verdict("M5", res in (0, -2) and "Extension" not in cls,
                f"result={res} -> {name or '(none)'} ({cls})   old pick: {on or '(none)'}")

    print("RESULT:", "PASS" if bad == 0 else f"FAIL ({bad})")
    return 0 if bad == 0 else 1


if __name__ == "__main__":
    sys.exit(main())
