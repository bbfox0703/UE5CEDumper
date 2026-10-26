r"""[VND584-04] Interfaces: a by-name lookup reaches an interface's function, and an interface parameter gets its
interface half.

    py tools/verify/vnd584_interfaces.py                        # pipe + mailbox, the injected DumperTest
    py tools/verify/vnd584_interfaces.py --any-build            # skip the dist build check (the pre-fix red run)

Exits 1 while the defect stands, so it doubles as the regression test.

THE MECHANISM. Our by-name lookup climbed the class's SuperStructs only; UClass::FindFunctionByName also walks the
class's implemented interfaces (UClass::Interfaces). And every invoke path wrote an interface parameter's object
half only, so FScriptInterface::InterfacePointer stayed 0 and a native callee read the parameter as null. Both need
UClass::Interfaces, which the DLL now measures (DetectClassInterfaces in the init log).

THE CASES (DumperTest's VND584-04 fixtures; tools/ue-sample/README.md):
  * pipe: IDumperTestIface::Vnd584_IfaceMagic invoked BY NAME on Vnd584_IfaceActor returns 584 (pre-fix: not found);
  * pipe: Vnd584_CallIface(Target = Vnd584_IfaceActor) returns 584 (pre-fix: -1, the interface half was null);
  * mailbox: CMD_INVOKE of the interface's own UFunction on Vnd584_IfaceActor returns 584 (pre-fix: -15 since
    VND584-06, which did not yet count an implemented interface as the class's).
Control: Vnd584_CallIface(Target = the DumperTestActor, which implements nothing) is refused, never sent.
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


def say(s):
    enc = sys.stdout.encoding or "utf-8"
    sys.stdout.write(str(s).encode(enc, "replace").decode(enc, "replace") + "\n")
    sys.stdout.flush()


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--any-build", action="store_true")
    ap.add_argument("--process", default="DumperTest-Win64-Shipping")
    a = ap.parse_args()
    results = []

    def record(label, ok, detail, starred=True):
        results.append((ok, starred))
        say("  %s  %s%s   %s" % ("PASS" if ok else "FAIL", "* " if starred else "  ", label, detail))

    with PipeClient() as c:
        say("DLL build %s" % c.request("get_pointers").get("build_number"))
        if not a.any_build:
            c.assert_build()
        c.ensure_scanned()
        actor = next(i for i in c.request("find_instances", class_name="DumperTestActor", exact_match=True,
                                          limit=50).get("instances") if not i["name"].startswith("Default__"))
        ia = next((i for i in c.request("find_instances", class_name="DumperTestIfaceActor", exact_match=True,
                                        limit=50).get("instances") or [] if not i["name"].startswith("Default__")),
                  None)
        if not ia:
            say("no DumperTestIfaceActor -- package DumperTest with the VND584-04 fixtures")
            return 2
        iface_addr = int(ia["addr"], 16)
        say("iface actor %s %s" % (ia["name"], ia["addr"]))

        # 1. by name, on the implementer
        r = c.request("invoke_function", class_name="DumperTestIfaceActor", func_name="Vnd584_IfaceMagic",
                      instance_addr=ia["addr"], params_hex="00000000", parms_size=4, direct_call=False)
        out = bytes.fromhex(r.get("result_hex") or "")
        got = struct.unpack_from("<i", out, 0)[0] if len(out) >= 4 else None
        record("pipe Vnd584_IfaceMagic by name on the implementer returns 584", r.get("result") == 0 and got == 584,
               "result=%s ret=%s %s" % (r.get("result"), got, r.get("error", "")))

        # 2. an interface parameter
        fl = {f["name"]: f for f in c.request("walk_functions", addr=actor["class_addr"]).get("functions")}
        call = fl["Vnd584_CallIface"]
        tp = next(p for p in call["params"] if p["type"] == "InterfaceProperty")
        rp = next(p for p in call["params"] if p.get("ret"))
        size = max(p["offset"] + p["size"] for p in call["params"])

        def call_iface(target):
            buf = bytearray(size)
            buf[tp["offset"]:tp["offset"] + 8] = struct.pack("<Q", target)
            r = c.request("invoke_function", class_name="DumperTestActor", func_name="Vnd584_CallIface",
                          instance_addr=actor["addr"], params_hex=buf.hex().upper(), parms_size=size,
                          direct_call=False)
            out = bytes.fromhex(r.get("result_hex") or "")
            return r, (struct.unpack_from("<i", out, rp["offset"])[0] if len(out) >= rp["offset"] + 4 else None)

        r, got = call_iface(iface_addr)
        record("pipe Vnd584_CallIface(Vnd584_IfaceActor) returns 584 -- the interface half is filled",
               r.get("result") == 0 and got == 584, "result=%s ret=%s %s" % (r.get("result"), got, r.get("error", "")))
        r, got = call_iface(int(actor["addr"], 16))
        record("control: an object that implements nothing is refused, not sent", not r.get("ok", True)
               or "does not implement" in str(r.get("error", "")), "ok=%s error=%s ret=%s"
               % (r.get("ok"), r.get("error"), got), starred=False)

        # 3. the interface's own UFunction through the mailbox
        cls = c.request("find_object", path="/Script/DumperTest.DumperTestIface")
        ifl = {f["name"]: f for f in c.request("walk_functions", addr=cls["addr"]).get("functions") or []}
        fn = ifl.get("Vnd584_IfaceMagic")
        if not fn:
            record("the interface class lists Vnd584_IfaceMagic", False, str(cls))
        else:
            m = mb.Mem(mb.pid_of(a.process))
            base = mb.mailbox_addr(a.process)
            m.write(base + mb.OFF_STATUS, struct.pack("<i", mb.STATUS_IDLE))
            m.write(base + mb.OFF_INSTANCE, struct.pack("<Q", iface_addr))
            m.write(base + mb.OFF_UFUNC, struct.pack("<Q", int(fn["addr"], 16)))
            m.write(base + mb.OFF_RESULT, struct.pack("<i", 0x7FFFFFFF))
            m.write(base + mb.OFF_PARAMS, bytes(1024))
            m.write(base + mb.OFF_CMD, struct.pack("<i", CMD_INVOKE))
            t = time.time() + 10
            while time.time() < t and m.i32(base + mb.OFF_STATUS) != mb.STATUS_DONE:
                time.sleep(0.02)
            res = m.i32(base + mb.OFF_RESULT)
            err = m.read(base + mb.OFF_ERRORMSG, 256).split(b"\0", 1)[0].decode("utf-8", "replace")
            got = struct.unpack_from("<i", m.read(base + mb.OFF_PARAMS, 4), 0)[0]
            record("mailbox CMD_INVOKE of the interface's UFunction on the implementer returns 584",
                   res == 0 and got == 584, "result=%s ret=%s %s" % (res, got, err))

    starred = [r for r in results if r[1]]
    say("\nstarred %d/%d pass, controls %d/%d pass" % (sum(1 for r in starred if r[0]), len(starred),
                                                     sum(1 for r in results if not r[1] and r[0]),
                                                     sum(1 for r in results if not r[1])))
    return 0 if all(r[0] for r in results) else 1


if __name__ == "__main__":
    sys.exit(main())
