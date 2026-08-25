"""A3: prove more than ONE FVector per class now contributes scan leaves.

    py a3_struct_path.py

THE DEFECT (fixed build 3168). `expandFields`' recursion guard was WHOLE-WALK instead
of PATH-SCOPED, so only the FIRST field of a given `UScriptStruct` type in a class
contributed leaves. `RelativeLocation` was indexed; `RelativeScale3D`, `Velocity`,
`Extent` never were -- subtree and all, across unrelated branches.
The guard's CONTRACT is unit-pinned (`Test_Aura_StructPathGuard`, negative control 7
red). The WALK THAT USES IT is not: **no test target compiles `Aura.cpp`**, so
`expandFields` calling the guard has never run against a real class.

THE MEASUREMENT. A vector LEAF is a dotted candidate path ending `.X`/`.Y`/`.Z`; strip
the leaf and you have the struct field that produced it. Under the defect a class could
contribute AT MOST ONE distinct such parent, so the honest statistic is simply **how
many classes contribute two or more** -- 0 would be the defect, anything above 0 refutes
it, and no baseline capture is needed. Measured on DumperTest: **151 classes**, topped
by `TraceQueryTestResults` with 72 distinct vector fields and `DumperTestCharacter` with
19 (`AttachmentReplication.LocationOffset`, `AttachmentReplication.RelativeScale3D`,
`BasedMovement.Location`, ... -- several different branches of one class).

⚠ USE **Double**, NOT Float. The row's step 1 says "Float (or NumericAll)" and on a UE5
title that is simply wrong: LWC makes `FVector` a double-precision `FVector3d`, so a
Float scan can never see `RelativeLocation.X`. Measured side by side here -- Float/1.0
returned **0** `*Scale3D*` and **0** `*Location*`; Double/0 returned **177** and **114**.
Reading that Float run at face value would have condemned a working fix.

⚠ THE ROW'S OWN WARNING, ENFORCED HERE: **do not verify with an FVector scan.** For a
vector scan `acceptedStructNames` is non-empty, the recursion is skipped, and the guard
never fired -- so a green FVector run proves nothing. It is run below only as step 2's
CONTROL, where the expected outcome is "unchanged", and a CHANGE would mean the fix
reached somewhere it should not have.
"""
import json
import pathlib
import sys
import time

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
from pipe_client import PipeClient                     # noqa: E402

LOGDIR = pathlib.Path.home() / "AppData/Local/UE5CEDumper/Logs"


def all_rows(c, sid, total):
    """Every candidate row. `total` comes from begin_value_scan, NOT from a page.

    Paginating until a short page arrives is wrong here: the page size is server-side
    and a full last page would end the loop early, silently under-counting the very
    population being measured.
    """
    rows, off = [], 0
    while off < (total or 0):
        q = c.request("query_candidates", session_id=sid, offset=off, limit=2000)
        got = q.get("candidates") or []
        if not got:
            break
        rows += got
        off += len(got)
    if total and len(rows) != total:
        print(f"  ! pulled {len(rows)} of {total} -- pagination is short, treat counts "
              f"as a LOWER BOUND")
    return rows


def field_names(c, sid, total=None):
    return [r.get("field_name", "") for r in all_rows(c, sid, total or 100000)]


def scan(c, **kw):
    r = c.request("begin_value_scan", **kw)
    if not r.get("ok"):
        raise SystemExit(f"a3: begin_value_scan failed: {r}")
    sid = r.get("session_id")
    if not sid:
        raise SystemExit(f"a3: no session_id in reply: {json.dumps(r)[:400]}")
    return sid, r


def main():
    ok = True
    with PipeClient(timeout=300.0) as c:
        c.assert_build()
        c.ensure_scanned()

        # ---- step 1: the real check -----------------------------------------
        # ⚠ DOUBLE, not Float. The row says "Float (or NumericAll)", and on a UE5 title
        # that instruction is simply WRONG: LWC makes FVector a double-precision
        # FVector3d, so a Float scan can never see `RelativeLocation.X` and returns a
        # confident empty set. Measured here: Float/1.0 found 0 *Scale3D* and 0
        # *Location*; Double/0 found 177 and 114. Reading that Float run as a FAIL
        # would have condemned a working fix.
        print("=== A3 step 1: Double Exact 0 -- distinct vector fields PER CLASS ===")
        sid, meta = scan(c, data_type="Double", scan_type="Exact", value="0",
                         game_only=True, max_results=300000, deadline_ms=120000)
        total = meta.get("total")
        print(f"  session {sid}  total={total}  objs={meta.get('scanned_objects')}  "
              f"deadline_hit={meta.get('deadline_hit')}")
        rows = all_rows(c, sid, total)
        c.request("end_value_scan", session_id=sid)

        # A vector LEAF is a dotted path ending .X/.Y/.Z; its parent path is the struct
        # field. The defect meant at most ONE distinct such parent per class, so the
        # count of classes with TWO OR MORE is the direct measurement.
        per = {}
        for x in rows:
            fn = x.get("field_name", "")
            if "." in fn and fn.rsplit(".", 1)[-1] in ("X", "Y", "Z"):
                per.setdefault(x.get("class_name", "?"), set()).add(fn.rsplit(".", 1)[0])
        multi = {k: v for k, v in per.items() if len(v) > 1}
        print(f"  candidates pulled: {len(rows)}")
        print(f"  classes contributing >1 DISTINCT vector field: {len(multi)}"
              f"   (pre-3168 this had to be 0)")
        for k, v in sorted(multi.items(), key=lambda kv: -len(kv[1]))[:4]:
            print(f"    {k}: {len(v)} distinct, e.g. {sorted(v)[:3]}")
        if multi:
            print("  step 1 PASS -- multiple FVector fields in one class contribute leaves")
        else:
            print("  step 1 FAIL -- still at most one FVector per class")
            ok = False

        # ---- step 2: the FVector CONTROL ------------------------------------
        print("\n=== A3 step 2 (CONTROL): FVector scan must be UNCHANGED by the fix ===")
        sid2, meta2 = scan(c, data_type="FVector", scan_type="Exact", value="1,1,1",
                           game_only=True, max_results=50000, deadline_ms=60000)
        names2 = field_names(c, sid2, meta2.get("total"))
        c.request("end_value_scan", session_id=sid2)
        print(f"  session {sid2}  count={meta2.get('count')}  rows={len(names2)}")
        print(f"    sample: {names2[:4]}")
        print("  step 2 is a control: it has no pass/fail of its own here -- it is the "
              "baseline a FUTURE run compares against, and the row's point is that a\n"
              "  vector scan skips the recursion entirely, so the guard never fires.")

    # ---- step 4: the cap must not be firing ---------------------------------
    print("\n=== A3 step 4: the 4000 scan-field cap must be unreachable in practice ===")
    hits = []
    for f in LOGDIR.rglob("scan-*.log"):
        try:
            if "hit the 4000 scan-field cap" in f.read_text(encoding="utf-8", errors="replace"):
                hits.append(f)
        except OSError:
            pass
    print(f"  logs mentioning the cap: {len(hits)}")
    for h in hits[:5]:
        print("   ", h)
    if hits:
        print("  step 4 FAIL -- the cap is firing on ordinary classes, so its value is wrong")
        ok = False
    else:
        print("  step 4 PASS -- absent everywhere")

    print(f"\nA3: {'PASS' if ok else 'NEEDS ATTENTION'}")
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main())
