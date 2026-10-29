"""[LW-CONTAINER-VIEW-CLASSNAME-ACTIONS] invoke_function's expect_class_addr on a live game.

    py tools/verify/invoke_expect_class.py                                   # DumperTestActor, K2_GetActorLocation
    py tools/verify/invoke_expect_class.py --class BP_Player_C --func K2_GetActorRotation

Headless, through the pipe (the UI must NOT be connected). A PIPE dialog stays open across FIREs with the address it
opened with, and the DLL finds the function by NAME on the class read at that address at FIRE time; since build 3666
the dialog sends the class its function list was read from, and the DLL refuses an object of another class there.
Freeing an object and having its slot reused cannot be arranged on demand, so this sends the mismatch directly: the
instance's own class (invoked), the class of its outer (refused, nothing dispatched), and no key at all (invoked, as
every caller that sends none). Pick a function that only reads: it is called twice.
"""
import argparse
import pathlib
import sys

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
from pipe_client import PipeClient  # noqa: E402


def data(reply):
    return reply.get("data", reply)


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--class", dest="cls", default="DumperTestActor", help="the class of the instance to invoke on")
    ap.add_argument("--func", default="K2_GetActorLocation", help="a function that only reads")
    a = ap.parse_args()

    with PipeClient() as pc:
        pc.connect()
        print("build:", pc.assert_build())
        pc.ensure_scanned()
        found = data(pc.request("find_instances", class_name=a.cls, exact_match=True, limit=8))
        inst = next((i for i in found.get("instances", []) if not i.get("name", "").startswith("Default__")), None)
        if inst is None:
            print(f"no live instance of {a.cls}")
            return 2
        addr = "0x" + inst["addr"].replace("0x", "")
        walked = data(pc.request("walk_instance", addr=addr, preview_limit=0))
        own_cls = "0x" + str(walked.get("class_addr", "")).replace("0x", "")
        outer = "0x" + str(walked.get("outer", "")).replace("0x", "")
        other_cls = "0x" + str(data(pc.request("walk_instance", addr=outer, preview_limit=0)).get("class_addr", "")) \
            .replace("0x", "")
        print(f"instance {inst['name']} @ {addr}, class {walked.get('class')} @ {own_cls}; "
              f"outer {walked.get('outer_name')} ({walked.get('outer_class')}), class @ {other_cls}")
        if own_cls == other_cls or own_cls == "0x" or other_cls == "0x":
            print("could not read two different classes")
            return 2

        cases = [
            ("its own class", own_cls, True),
            ("another class (its outer's)", other_cls, False),
            ("no expect_class_addr", None, True),
        ]
        wrong = 0
        for label, expect, should_run in cases:
            params = dict(func_name=a.func, instance_addr=addr)
            if expect is not None:
                params["expect_class_addr"] = expect
            r = pc.request("invoke_function", **params)
            ran = bool(r.get("ok"))
            ok = ran == should_run
            if not should_run:
                ok = ok and str(r.get("error", "")).startswith("Not invoked: the object at")
            wrong += not ok
            print(f"  {'OK ' if ok else 'BAD'} {label}: ok={r.get('ok')} "
                  f"{'result=' + str(data(r).get('result')) if ran else 'error=' + repr(r.get('error'))}")
    print(f"\n{len(cases) - wrong} of {len(cases)} as expected")
    return 0 if wrong == 0 else 1


if __name__ == "__main__":
    sys.exit(main())
