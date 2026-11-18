#!/usr/bin/env python3
r"""[WALKCLASS-NOT-STRUCT] walk_class walks a class, a struct or a function, and refuses any other live object.

    py tools/verify/walkclass_kind_live.py --pid <game pid>

Review wf_1b9e937b-a0d DLL-3: walk_class's liveness gate let ANY live object through, so a class freed by a level
travel whose address now holds an actor was walked as a class (an empty or made-up class, answered ok), and the
Class/Struct panel's Enum rows were walked as structs the same way. On DumperTest (Shipping by default):

  controls   a class (DumperTestActor's), a script struct (/Script/CoreUObject.Vector) and a UFunction of the class
             walk as before: ok, with the class's own name.
  refusals   a live DumperTestActor INSTANCE (what a freed class's address can hold) and an enum are refused with
             "Not a class or struct at <addr> -- it holds <name> (<class>)".
  liveness   an address that is no object at all still gets the older "Not a live class" refusal.

The game and its pid are arguments; nothing is written to the game.
"""
import argparse
import pathlib
import sys

sys.stdout.reconfigure(encoding="utf-8", errors="replace")
sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
from pipe_client import PipeClient   # noqa: E402

results: list[tuple[str, bool, str]] = []


def check(name: str, ok: bool, detail: str = "") -> bool:
    results.append((name, ok, detail))
    print(f"  [{'PASS' if ok else 'FAIL'}] {name}" + (f" -- {detail}" if detail else ""))
    return ok


def data(r: dict) -> dict:
    return r.get("data", r) if isinstance(r, dict) else {}


def walk(c, addr: str) -> dict:
    return c.request("walk_class", addr=addr)


def walked_ok(r: dict) -> tuple[bool, str]:
    cls = data(r).get("class") or {}
    return bool(r.get("ok")) and bool(cls.get("name")), str(cls.get("name") or r.get("error") or "")[:120]


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--pid", type=int, required=True)
    a = ap.parse_args()
    with PipeClient(pid=a.pid, timeout=240.0) as c:
        print("build", c.assert_build())
        c.ensure_scanned()

        r = c.request("find_instances", class_name="DumperTestActor", limit=100, exact_match=True)
        live = [i for i in data(r).get("instances", []) if not i["name"].startswith("Default__")]
        if not live:
            raise SystemExit("walkclass_kind: no live DumperTestActor")
        actor = live[0]["addr"]
        obj = data(c.request("get_object", addr=actor))
        cls = obj.get("class_addr")
        if not cls:
            raise SystemExit(f"walkclass_kind: no class_addr for {actor}: {obj}")
        vec = data(c.request("find_object", path="/Script/CoreUObject.Vector")).get("addr")
        funcs = data(c.request("walk_functions", addr=cls)).get("functions") or []
        fn = funcs[0]["addr"] if funcs else None
        enums = data(c.request("list_enums")).get("enums") or []
        enum = next((e for e in enums if "/Script/" in (e.get("full_path") or "")), enums[0] if enums else None)

        print("== controls: what walk_class walks ==")
        ok, why = walked_ok(walk(c, cls))
        check("a class walks", ok and why == "DumperTestActor", why)
        if vec:
            ok, why = walked_ok(walk(c, vec))
            check("a script struct (Vector) walks", ok and why == "Vector", why)
        else:
            check("a script struct (Vector) walks", False, "find_object found no /Script/CoreUObject.Vector")
        if fn:
            ok, why = walked_ok(walk(c, fn))
            check("a UFunction walks", ok, f"{funcs[0].get('name')}: {why}")
        else:
            check("a UFunction walks", False, "walk_functions listed none")

        print("== refusals: live objects that are no class ==")
        r = walk(c, actor)
        err = str(r.get("error") or "")
        check("an actor instance is refused as no class or struct",
              not r.get("ok") and err.startswith("Not a class or struct at") and "DumperTestActor" in err, err[:160])
        if enum:
            r = walk(c, enum["addr"])
            err = str(r.get("error") or "")
            check("an enum is refused as no class or struct",
                  not r.get("ok") and err.startswith("Not a class or struct at") and enum["name"] in err, err[:160])
        else:
            check("an enum is refused as no class or struct", False, "list_enums listed none")

        print("== liveness: no object at all ==")
        bogus = hex(int(actor, 16) + 0x8)   # inside the actor, not an object's start
        r = walk(c, bogus)
        err = str(r.get("error") or "")
        check("an address that is no object keeps the 'Not a live class' refusal",
              not r.get("ok") and err.startswith("Not a live class at"), err[:160])

    failed = [n for n, ok, _ in results if not ok]
    print(f"\n{len(results) - len(failed)}/{len(results)} passed")
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
