"""Live pipe checks for the DLL side of the 2026-11-02 census pass (build 3671 on).

    py tools/verify/census_pass_pipe_checks.py                 # DumperTestActor, K2_GetActorLocation

Headless, through the pipe (the UI must NOT be connected). Three checks:

1. [LEAD-CONSOLE-PIN-EXPECT-CLASS] An invoke by class name names the class it read at the address it used
   (instance_class_addr), equal to that object's class_addr; sending it back as expect_class_addr with the address
   runs the call, as the Console's pinned run does. Pick a function that only reads: it is called twice.
2. [CEXPORT-ENUMNAMES-NOTE] A walk carries enum_addr on every field that has a UEnum. With enum names read (every
   fixture so far), each such field still carries its entries, so nothing an export shows changes; the reply carries
   no enum_names_failed. The latched case needs a build where UEnum::Names is not found, which no fixture is.
3. [LEAD-DT-EXPORT-64ROWS] Each live DataTable's row count against the rows one page returns, so a table larger than
   the page shows what the export warning now says.
"""
import argparse
import pathlib
import sys

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
from pipe_client import PipeClient  # noqa: E402


def data(reply):
    return reply.get("data", reply)


def hx(v) -> str:
    return "0x" + str(v or "").replace("0x", "").upper()


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--class", dest="cls", default="DumperTestActor")
    ap.add_argument("--func", default="K2_GetActorLocation", help="a function that only reads")
    a = ap.parse_args()
    bad = 0

    with PipeClient() as pc:
        pc.connect()
        print("build:", pc.assert_build())
        pc.ensure_scanned()

        # 1. the invoke reply's class
        r = data(pc.request("invoke_function", func_name=a.func, class_name=a.cls))
        inst, cls = hx(r.get("instance_addr")), hx(r.get("instance_class_addr"))
        walked = data(pc.request("walk_instance", addr=inst, preview_limit=0))
        own = hx(walked.get("class_addr"))
        ok1 = cls != "0x" and cls == own
        print(f"1. invoke by name: result={r.get('result')} instance {inst} instance_class_addr {cls}; "
              f"walk says class {walked.get('class')} @ {own} -> {'OK' if ok1 else 'BAD'}")
        r2 = pc.request("invoke_function", func_name=a.func, instance_addr=inst, expect_class_addr=cls)
        ok1b = bool(r2.get("ok")) and data(r2).get("result") == 0
        print(f"   pinned run with that class: ok={r2.get('ok')} result={data(r2).get('result')} "
              f"-> {'OK' if ok1b else 'BAD'}")
        bad += (not ok1) + (not ok1b)

        # 2. enum fields
        full = data(pc.request("walk_instance", addr=inst, preview_limit=2))
        with_addr = [f for f in full.get("fields", []) if f.get("enum_addr")]
        no_entries = [f["name"] for f in with_addr if not f.get("enum_entries")]
        latched = bool(full.get("enum_names_failed"))
        ok2 = not latched and not no_entries
        print(f"2. walk of {full.get('name')}: {len(with_addr)} field(s) carry enum_addr "
              f"({', '.join(f['name'] for f in with_addr[:6])}{'...' if len(with_addr) > 6 else ''}); "
              f"without entries: {no_entries or 'none'}; enum_names_failed={latched} -> {'OK' if ok2 else 'BAD'}")
        bad += not ok2

        # 3. DataTables
        tables = data(pc.request("find_instances", class_name="DataTable", exact_match=False, limit=200))
        rows_seen = 0
        for t in tables.get("instances", []):
            if t.get("name", "").startswith("Default__"):
                continue
            dt = pc.request("walk_datatable_rows", addr=hx(t["addr"]))
            if not dt.get("ok"):
                continue
            d = data(dt)
            total, got = d.get("row_count"), len(d.get("rows", []))
            rows_seen += 1
            print(f"3. {t.get('name')}: {total} rows, one page returns {got}"
                  f"{'  <- cut: the export warning names it' if isinstance(total, int) and total > got else ''}")
        if rows_seen == 0:
            print("3. no DataTable walked")

    print(f"\n{'all as expected' if bad == 0 else f'{bad} unexpected'}")
    return 0 if bad == 0 else 1


if __name__ == "__main__":
    sys.exit(main())
