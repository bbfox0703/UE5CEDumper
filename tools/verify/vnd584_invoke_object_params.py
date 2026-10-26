r"""[VND584-01] A picked object passed into a weak / soft parameter must reach the callee as that object.

    py tools/verify/vnd584_invoke_object_params.py                 # pipe + mailbox, the injected DumperTest
    py tools/verify/vnd584_invoke_object_params.py --no-mailbox    # pipe only
    py tools/verify/vnd584_invoke_object_params.py --any-build     # skip the dist build check (the red run)

Exits 1 while the defect stands, so it doubles as the regression test.

THE MECHANISM. Every invoke path wrote the picked object's ADDRESS into a TWeakObjectPtr / TSoftObjectPtr /
TSoftClassPtr parameter. The engine reads FWeakObjectPtr { int32 ObjectIndex, int32 SerialNumber } there (a soft
pointer leads with one), resolved it to null, and ProcessEvent reported OK. Since VND584-01 the DLL swaps the address
for the engine's own value (KismetSystemLibrary::Conv_ObjectToSoftObjectReference) before the call.

THE CASES. Each starred case asks the ENGINE what it received, so a pre-fix DLL fails it and a fixed one passes:
  * KismetSystemLibrary::Conv_SoftObjectReferenceToObject(picked actor) must return the actor, routed direct
    (static native) and through the game-thread queue;
  * KismetSystemLibrary::IsValidSoftObjectReference(picked actor) must return true -- the PATH, not only the weak half;
  * KismetSystemLibrary::Conv_SoftClassReferenceToClass(the actor's class) must return the class;
  * DumperTestActor::Vnd584_EchoWeakRef(picked actor) / Vnd584_EchoSoftSpec({Icon = picked actor}) must return the
    actor (fixtures packaged 2026-10-26 -- no engine library takes a weak parameter or a struct holding a soft one);
  * Vnd584_EchoWeak (BY VALUE): the slot must hold the engine's pair for the actor. Its return is not checked: before
    UE 5.8 the engine's own thunk loses a by-value weak pointer (CopyCompleteValueToScriptVM stores a raw UObject* in
    its TWeakObjectPtr local; fixed by 399f2949c408), so it returns null whatever the caller sends;
  * the same weak and soft cases through the CE mailbox (CMD_INVOKE), the path CE scripts use.
Controls, which must pass on every build: a null stays null, and a value that is no live object (an encoded pair the
caller built) goes through unrefused and unchanged.
"""
import argparse
import pathlib
import struct
import sys
import time

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
from pipe_client import PipeClient  # noqa: E402
import mailbox_poke as mb  # noqa: E402

CMD_INVOKE = 1
MAILBOX_PARAMS = 1024


def say(s):
    enc = sys.stdout.encoding or "utf-8"
    sys.stdout.write(str(s).encode(enc, "replace").decode(enc, "replace") + "\n")
    sys.stdout.flush()


def hexaddr(v):
    return "0x%X" % v


class Rig:
    def __init__(self, c):
        self.c = c
        self.results = []

    def record(self, label, ok, detail, starred=True):
        self.results.append((label, ok, starred))
        say(("  %s  %s%s   %s" % ("PASS" if ok else "FAIL", "* " if starred else "  ", label, detail)).rstrip())

    def instances(self, cls, exact=True):
        r = self.c.request("find_instances", class_name=cls, exact_match=exact, max_results=200, limit=200)
        return r.get("instances") or []

    def functions(self, class_addr):
        fl = self.c.request("walk_functions", addr=class_addr).get("functions") or []
        return {f["name"]: f for f in fl}

    @staticmethod
    def layout(fn):
        params = fn.get("params") or []
        end = max([p["offset"] + p["size"] for p in params if p.get("offset", -1) >= 0] + [fn.get("parms_size", 0)])
        ret = next((p for p in params if p.get("ret")), None)
        return params, max(end, 8), ret

    def invoke(self, inst, cls_name, fn, writes, direct):
        """writes: [(offset, bytes)]. Returns (reply, result bytes)."""
        params, size, _ = self.layout(fn)
        buf = bytearray(size)
        for off, data in writes:
            buf[off:off + len(data)] = data
        r = self.c.request("invoke_function", class_name=cls_name, func_name=fn["name"], instance_addr=hexaddr(inst),
                           params_hex=buf.hex().upper(), parms_size=size, direct_call=direct)
        out = bytes.fromhex(r.get("result_hex", "")) if r.get("result_hex") else b""
        return r, out


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--no-mailbox", action="store_true")
    ap.add_argument("--any-build", action="store_true", help="do not require the dist build (the pre-fix red run)")
    ap.add_argument("--process", default="DumperTest-Win64-Shipping")
    a = ap.parse_args()

    with PipeClient() as c:
        build = c.request("get_pointers").get("build_number")
        if not a.any_build:
            c.assert_build()
        c.ensure_scanned()
        say("DLL build %s" % build)
        rig = Rig(c)

        actors = [i for i in rig.instances("DumperTestActor") if not str(i.get("name", "")).startswith("Default__")]
        if not actors:
            say("no live DumperTestActor -- is DumperTest in its map?")
            return 2
        actor = actors[0]
        act = int(actor["addr"], 16)
        act_idx = int(actor.get("index", -1))
        act_cls = int(actor["class_addr"], 16)
        say("actor %s %s index %d class %s" % (actor["name"], hexaddr(act), act_idx, actor["class_addr"]))

        ksl = next((i for i in rig.instances("KismetSystemLibrary")
                    if str(i.get("name", "")).startswith("Default__KismetSystemLibrary")), None)
        if not ksl:
            say("Default__KismetSystemLibrary not found by find_instances")
            return 2
        ksl_addr = int(ksl["addr"], 16)
        kf = rig.functions(ksl["class_addr"])
        af = rig.functions(actor["class_addr"])
        q = lambda v: struct.pack("<Q", v)  # noqa: E731

        def first(fn, typ):
            return next(p for p in fn["params"] if p["type"] == typ and not p.get("ret"))

        def ret_u64(fn, out):
            _, _, ret = Rig.layout(fn)
            return struct.unpack_from("<Q", out, ret["offset"])[0] if out and ret else None

        # ---- pipe: KismetSystemLibrary ------------------------------------------------------------------
        conv = kf["Conv_SoftObjectReferenceToObject"]
        sp = first(conv, "SoftObjectProperty")
        for direct in (True, False):
            r, out = rig.invoke(ksl_addr, "KismetSystemLibrary", conv, [(sp["offset"], q(act))], direct)
            got = ret_u64(conv, out)
            rig.record("pipe Conv_SoftObjectReferenceToObject(actor) returns the actor (%s)"
                       % ("direct" if direct else "game thread"), got == act,
                       "result=%s ret=%s %s" % (r.get("result"), hexaddr(got) if got is not None else None,
                                                r.get("error", "")))
            if direct and out:
                idx, ser = struct.unpack_from("<ii", out, sp["offset"])
                rig.record("pipe the soft slot holds the engine's pair {actor index, serial != 0}",
                           idx == act_idx and ser != 0, "{index %d, serial %d}" % (idx, ser))

        valid = kf["IsValidSoftObjectReference"]
        vp = first(valid, "SoftObjectProperty")
        r, out = rig.invoke(ksl_addr, "KismetSystemLibrary", valid, [(vp["offset"], q(act))], True)
        _, _, vret = Rig.layout(valid)
        vb = out[vret["offset"]] if out and vret else None
        rig.record("pipe IsValidSoftObjectReference(actor) is true (the path, not only the weak half)", vb == 1,
                   "result=%s ret=%s %s" % (r.get("result"), vb, r.get("error", "")))

        cconv = kf["Conv_SoftClassReferenceToClass"]
        cp = first(cconv, "SoftClassProperty")
        r, out = rig.invoke(ksl_addr, "KismetSystemLibrary", cconv, [(cp["offset"], q(act_cls))], True)
        got = ret_u64(cconv, out)
        rig.record("pipe Conv_SoftClassReferenceToClass(actor's class) returns the class", got == act_cls,
                   "result=%s ret=%s %s" % (r.get("result"), hexaddr(got) if got is not None else None,
                                            r.get("error", "")))

        # Controls: null stays null; a value that is no live object goes through unrefused and unchanged.
        r, out = rig.invoke(ksl_addr, "KismetSystemLibrary", conv, [(sp["offset"], q(0))], True)
        rig.record("control: a null soft parameter stays null", r.get("result") == 0 and ret_u64(conv, out) == 0,
                   "result=%s %s" % (r.get("result"), r.get("error", "")), starred=False)
        pair = 0x0000000500001234
        r, out = rig.invoke(ksl_addr, "KismetSystemLibrary", conv, [(sp["offset"], q(pair))], True)
        kept = out and struct.unpack_from("<Q", out, sp["offset"])[0] == pair
        rig.record("control: an encoded pair (no live object) is sent unrefused and unchanged",
                   r.get("result") == 0 and bool(kept), "result=%s %s" % (r.get("result"), r.get("error", "")),
                   starred=False)

        # ---- pipe: the DumperTest fixtures ---------------------------------------------------------------
        weak_fn = af.get("Vnd584_EchoWeak")
        weak_ref_fn = af.get("Vnd584_EchoWeakRef")
        spec_fn = af.get("Vnd584_EchoSoftSpec")
        if not weak_fn or not weak_ref_fn or not spec_fn:
            say("  ----  the DumperTest fixtures are missing: package it with them (tools/ue-sample, 2026-10-26)")
            rig.results.append(("fixtures present", False, True))
        else:
            # By VALUE, the engine's own thunk loses the pointer before UE 5.8 (its TWeakObjectPtr local is filled
            # through CopyCompleteValueToScriptVM with a raw UObject*; fixed by 399f2949c408), so its return says
            # nothing about the invoke. What the invoke owes is the slot: the engine's pair for the actor.
            wp = first(weak_fn, "WeakObjectProperty")
            r, out = rig.invoke(act, "DumperTestActor", weak_fn, [(wp["offset"], q(act))], False)
            got = ret_u64(weak_fn, out)
            idx, ser = struct.unpack_from("<ii", out, wp["offset"]) if out else (None, None)
            rig.record("pipe Vnd584_EchoWeak: the by-value weak slot holds the engine's pair for the actor",
                       idx == act_idx and bool(ser), "{index %s, serial %s}; ret=%s (null before UE 5.8: engine)"
                       % (idx, ser, hexaddr(got) if got is not None else None))
            wrp = first(weak_ref_fn, "WeakObjectProperty")
            r, out = rig.invoke(act, "DumperTestActor", weak_ref_fn, [(wrp["offset"], q(act))], False)
            got = ret_u64(weak_ref_fn, out)
            rig.record("pipe Vnd584_EchoWeakRef(actor) returns the actor", got == act,
                       "result=%s ret=%s %s" % (r.get("result"), hexaddr(got) if got is not None else None,
                                                r.get("error", "")))
            sp2 = first(spec_fn, "StructProperty")
            icon = next((f for f in sp2.get("struct_fields") or [] if f["type"] == "SoftObjectProperty"), None)
            cnt = next((f for f in sp2.get("struct_fields") or [] if f["type"] == "IntProperty"), None)
            if not icon:
                rig.record("pipe Vnd584_EchoSoftSpec: the struct's soft member is listed", False,
                           str(sp2.get("struct_fields")))
            else:
                writes = [(sp2["offset"] + icon["offset"], q(act))]
                if cnt:
                    writes.append((sp2["offset"] + cnt["offset"], struct.pack("<i", 3)))
                r, out = rig.invoke(act, "DumperTestActor", spec_fn, writes, False)
                got = ret_u64(spec_fn, out)
                rig.record("pipe Vnd584_EchoSoftSpec({Icon = actor}) returns the actor", got == act,
                           "result=%s ret=%s %s" % (r.get("result"), hexaddr(got) if got is not None else None,
                                                    r.get("error", "")))

        # ---- mailbox: CMD_INVOKE, the CE scripts' path ---------------------------------------------------
        if not a.no_mailbox:
            pid = mb.pid_of(a.process)
            m = mb.Mem(pid)
            base = mb.mailbox_addr(a.process)

            def mailbox_invoke(inst, fn, writes, timeout=10.0):
                buf = bytearray(MAILBOX_PARAMS)
                for off, data in writes:
                    buf[off:off + len(data)] = data
                if m.i32(base + mb.OFF_STATUS) == mb.STATUS_PROCESSING:
                    raise SystemExit("mailbox busy")
                m.write(base + mb.OFF_STATUS, struct.pack("<i", mb.STATUS_IDLE))
                m.write(base + mb.OFF_INSTANCE, struct.pack("<Q", inst))
                m.write(base + mb.OFF_UFUNC, struct.pack("<Q", int(fn["addr"], 16)))
                m.write(base + mb.OFF_RESULT, struct.pack("<i", 0x7FFFFFFF))
                m.write(base + mb.OFF_PARAMS, bytes(buf))
                m.write(base + mb.OFF_CMD, struct.pack("<i", CMD_INVOKE))
                deadline = time.time() + timeout
                while time.time() < deadline:
                    if m.i32(base + mb.OFF_STATUS) == mb.STATUS_DONE:
                        break
                    time.sleep(0.02)
                else:
                    return None, b"", "timeout"
                res = m.i32(base + mb.OFF_RESULT)
                err = m.read(base + mb.OFF_ERRORMSG, 256).split(b"\0", 1)[0].decode("utf-8", "replace")
                return res, m.read(base + mb.OFF_PARAMS, MAILBOX_PARAMS), err

            res, out, err = mailbox_invoke(ksl_addr, conv, [(sp["offset"], q(act))])
            got = ret_u64(conv, out)
            rig.record("mailbox Conv_SoftObjectReferenceToObject(actor) returns the actor", got == act,
                       "result=%s ret=%s %s" % (res, hexaddr(got) if got is not None else None, err))
            if weak_ref_fn:
                wrp = first(weak_ref_fn, "WeakObjectProperty")
                res, out, err = mailbox_invoke(act, weak_ref_fn, [(wrp["offset"], q(act))])
                got = ret_u64(weak_ref_fn, out)
                rig.record("mailbox Vnd584_EchoWeakRef(actor) returns the actor", got == act,
                           "result=%s ret=%s %s" % (res, hexaddr(got) if got is not None else None, err))

        starred = [r for r in rig.results if r[2]]
        controls = [r for r in rig.results if not r[2]]
        say("")
        say("starred %d/%d pass, controls %d/%d pass" % (sum(1 for r in starred if r[1]), len(starred),
                                                       sum(1 for r in controls if r[1]), len(controls)))
        return 0 if all(r[1] for r in rig.results) else 1


if __name__ == "__main__":
    sys.exit(main())
