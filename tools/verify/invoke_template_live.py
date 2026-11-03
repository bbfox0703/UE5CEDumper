#!/usr/bin/env python3
r"""Live check for [INVOKE-NONSTATIC-ON-CDO] (build 3676 on): DumperTest, headless, no CE.

    py tools/verify/invoke_template_live.py [--process DumperTest] [--bp-class BP_ThirdPersonCharacter_C]

The UI must NOT be connected. Warn, never refuse: a user-facing invoke of a function that is not static on a TEMPLATE
(a class default object, or one of its default subobjects) is reported, never blocked. Only READ-ONLY functions are
called (K2_GetActorLocation reads RootComponent and returns zero when there is none; K2_GetComponentLocation reads the
component's transform; IsPackagedForDistribution is static). Never a CheatManager command on its class default object.

Pipe (invoke_function):
  T1 KismetSystemLibrary / IsPackagedForDistribution, direct_call -> class_default, NOT warned (a static function)
  T2 "Actor" / K2_GetActorLocation                    -> a live actor, not a template, not warned
  T3 Default__Actor by address / K2_GetActorLocation  -> result 0, pick address, class_default, WARNED
  T4 a Blueprint class default object by address      -> class_default, WARNED
  T5 that object's CapsuleComponent template / K2_GetComponentLocation -> "template", owned by it, WARNED
  T6 FIND_INSTANCE of component classes: none lands on a template any more (it did on 3675 -- measured)
Mailbox:
  M1 g_mailboxContract >= 7 / 1
  M2 FIND_FUNCTION + INVOKE K2_GetActorLocation on Default__Actor -> result 0, cmdOutFlags bit 0x2
  M3 the same on T2's live actor                                   -> cmdOutFlags 0
  M4 INVOKE_BY_NAME KismetSystemLibrary IsPackagedForDistribution   -> cmdOutFlags 0 (static)
  M5 after M2, a FIND_INSTANCE                                      -> cmdOutFlags 0 (cleared before every command)
"""
from __future__ import annotations

import argparse
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

CMD_INVOKE, CMD_FIND_INSTANCE, CMD_FIND_FUNCTION, CMD_INVOKE_BY_NAME = 1, 2, 3, 4
OFF_CLASSNAME, OFF_FUNCNAME, OFF_OUT_FLAGS = 0x028, 0x128, 0x72C
COMPONENTS = ("CharacterMovementComponent", "CapsuleComponent", "SkeletalMeshComponent", "SpringArmComponent",
              "CameraComponent")


def data(reply):
    return reply.get("data", reply)


def a2i(s) -> int:
    try:
        return int(str(s), 16)
    except ValueError:
        return 0


def cmd(m, base, c, inst=None, cls=None, fn=None, timeout=60.0):
    """One mailbox round trip: (result, instanceAddr, ufuncAddr, cmdOutFlags, errorMsg)."""
    if m.i32(base + MB.OFF_STATUS) == MB.STATUS_PROCESSING:
        raise SystemExit("mailbox is PROCESSING before the command -- a previous one is wedged")
    m.write(base + MB.OFF_STATUS, struct.pack("<i", MB.STATUS_IDLE))
    if inst is not None:
        m.write(base + MB.OFF_INSTANCE, struct.pack("<Q", inst))
    for off, s in ((OFF_CLASSNAME, cls), (OFF_FUNCNAME, fn)):
        if s is not None:
            raw = s.encode("utf-8")[:255]
            m.write(base + off, raw + b"\x00" * (256 - len(raw)))
    m.write(base + MB.OFF_PARAMS, b"\x00" * 64)
    m.write(base + OFF_OUT_FLAGS, struct.pack("<I", 0xDEAD0000))   # poison: the DLL must clear it
    m.write(base + MB.OFF_RESULT, struct.pack("<i", 0x7FFFFFFF))
    m.write(base + MB.OFF_CMD, struct.pack("<i", c))
    t0 = time.time()
    while time.time() - t0 < timeout:
        if m.i32(base + MB.OFF_STATUS) == MB.STATUS_DONE:
            res = m.i32(base + MB.OFF_RESULT)
            inst_o, ufunc = m.u64(base + MB.OFF_INSTANCE), m.u64(base + MB.OFF_UFUNC)
            out = struct.unpack("<I", m.read(base + OFF_OUT_FLAGS, 4))[0]
            err = m.read(base + MB.OFF_ERRORMSG, 256).split(b"\x00")[0].decode("utf-8", "replace")
            m.write(base + MB.OFF_CMD, struct.pack("<i", MB.CMD_IDLE))
            return res, inst_o, ufunc, out, err
        time.sleep(0.005)
    raise SystemExit(f"mailbox cmd {c}: TIMEOUT")


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    ap.add_argument("--process", default="DumperTest")
    ap.add_argument("--bp-class", default="BP_ThirdPersonCharacter_C")
    a = ap.parse_args()
    bad = 0

    def verdict(label, ok, detail):
        nonlocal bad
        bad += not ok
        print(f"{label:3} {'OK ' if ok else 'BAD'}  {detail}")

    with PipeClient(timeout=180) as pc:
        print("build:", pc.assert_build())
        pc.ensure_scanned()

        def inv(**kw):
            r = pc.request("invoke_function", **kw)
            d = data(r)
            return r, d, (f"ok={r.get('ok')} result={d.get('result')} pick={d.get('instance_pick')!r} "
                          f"on {d.get('instance_name')} ({d.get('instance_class_name')}) "
                          f"template={d.get('instance_template')!r} owner={d.get('template_owner_name')!r} "
                          f"warn={d.get('nonstatic_on_template')!r}" + (f" error={r.get('error')!r}" if r.get('error') else ""))

        def cdo_of(cls):
            rows = data(pc.request("find_instances", class_name=cls, exact_match=True, limit=20)).get("instances") or []
            return next((x for x in rows if str(x.get("name", "")) == f"Default__{cls}"), None)

        r, d, s = inv(class_name="KismetSystemLibrary", func_name="IsPackagedForDistribution", direct_call=True)
        verdict("T1", r.get("ok") and d.get("instance_template") == "class_default"
                and d.get("nonstatic_on_template") is False, s)
        r, d, s = inv(class_name="Actor", func_name="K2_GetActorLocation")
        live_actor = a2i(d.get("instance_addr", "0"))
        verdict("T2", r.get("ok") and d.get("instance_pick") == "live" and d.get("instance_template") == ""
                and d.get("nonstatic_on_template") is False, s)
        cdo_actor = cdo_of("Actor")
        r, d, s = inv(instance_addr=cdo_actor["addr"], func_name="K2_GetActorLocation") if cdo_actor else ({}, {}, "no Default__Actor")
        verdict("T3", r.get("ok") and d.get("result") == 0 and d.get("instance_pick") == "address"
                and d.get("instance_template") == "class_default" and d.get("nonstatic_on_template") is True, s)
        cdo_bp = cdo_of(a.bp_class)
        r, d, s = inv(instance_addr=cdo_bp["addr"], func_name="K2_GetActorLocation") if cdo_bp else ({}, {}, f"no Default__{a.bp_class}")
        verdict("T4", r.get("ok") and d.get("instance_template") == "class_default"
                and d.get("nonstatic_on_template") is True, s)
        capsule = 0
        if cdo_bp:
            w = data(pc.request("walk_instance", addr=cdo_bp["addr"], array_limit=0, preview_limit=0))
            capsule = next((a2i(f.get("ptr", "0")) for f in w.get("fields", []) if f.get("name") == "CapsuleComponent"), 0)
        r, d, s = inv(instance_addr=hex(capsule), func_name="K2_GetComponentLocation") if capsule else ({}, {}, "no capsule template")
        verdict("T5", r.get("ok") and d.get("instance_template") == "template"
                and d.get("template_owner_name") == f"Default__{a.bp_class}" and d.get("nonstatic_on_template") is True, s)

        pid = MB.pid_of(a.process)
        base = MB.mailbox_addr(str(pid))
        m = MB.Mem(pid)
        for comp in COMPONENTS:
            res, inst, _, _, _ = cmd(m, base, CMD_FIND_INSTANCE, cls=comp)
            if res != 0 or not inst:
                print(f"T6  --   {comp}: nothing ({res})")
                continue
            g = data(pc.request("get_object", addr=hex(inst)))
            flags = struct.unpack("<I", m.read(inst + 0x08, 4))[0]
            outer = data(pc.request("get_object", addr=g.get("outer_addr", "0x0"))).get("name", "")
            tpl = bool(flags & 0x30) or g.get("name", "").startswith("Default__") or outer.startswith("Default__")
            verdict("T6", not tpl, f"{comp}: {g.get('name')} flags={flags:#x} outer={outer}")

        rd = MA.Reader(pid)
        cv = 0
        for path, mbase in MA.modules(pid):
            if mbase and path.lower().endswith(("ue5dumper.dll", "version.dll", "dxgi.dll", "dinput8.dll", "winmm.dll")):
                try:
                    cv = MA.find_export(rd, mbase, "g_mailboxContract") or 0
                except SystemExit:
                    cv = 0
                if cv:
                    break
        magic, cur, lo = struct.unpack("<III", m.read(cv, 12)) if cv else (0, 0, 0)
        verdict("M1", cur >= 7 and lo == 1, f"current={cur} min={lo}")   # a floor: later bumps keep the warning

        def invoke_on(obj):
            res, _, ufunc, _, err = cmd(m, base, CMD_FIND_FUNCTION, inst=obj, fn="K2_GetActorLocation")
            if res != 0:
                return res, 0, f"FIND_FUNCTION {res} {err}"
            res, _, _, out, err = cmd(m, base, CMD_INVOKE, inst=obj)
            return res, out, err
        if cdo_actor:
            res, out, err = invoke_on(a2i(cdo_actor["addr"]))
            verdict("M2", res == 0 and out == 0x2, f"result={res} cmdOutFlags={out:#x} {err}")
            res, _, _, out, _ = cmd(m, base, CMD_FIND_INSTANCE, cls="Actor")
            verdict("M5", res == 0 and out == 0, f"FIND_INSTANCE after it: cmdOutFlags={out:#x}")
        if live_actor:
            res, out, err = invoke_on(live_actor)
            verdict("M3", res == 0 and out == 0, f"result={res} cmdOutFlags={out:#x} {err}")
        res, _, _, out, err = cmd(m, base, CMD_INVOKE_BY_NAME, cls="KismetSystemLibrary", fn="IsPackagedForDistribution")
        verdict("M4", res == 0 and out == 0, f"result={res} cmdOutFlags={out:#x} {err}")

    print("RESULT:", "PASS" if bad == 0 else f"FAIL ({bad})")
    return 0 if bad == 0 else 1


if __name__ == "__main__":
    sys.exit(main())
