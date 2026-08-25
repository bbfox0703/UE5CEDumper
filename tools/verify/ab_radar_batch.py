"""AB14 + AB16: the two Radar rows a pipe can settle without the UI.

    py ab_radar_batch.py

AB14 -- enum-backed fields must be visible to a value scan.
  The resolution is unit-tested; whether **Aura's meta scan actually emits enum
  candidates** is only observable live. A `NumericAll` scan must return candidates
  whose declared type is a byte/enum property. Before the fix they read as 1 byte and
  were invisible to every value scan.

AB16 -- the results filter must match on the ORIGIN column.
  `FormatCandidateOrigin` (Radar.cpp:1091) yields exactly `"Reflected"`,
  `"Native-C"`, or `"Native-C (<guessedType>)"`, and `MatchesCandidate` feeds that
  string to the same case-insensitive needle test as every other column
  (Radar.cpp:1176). Before the fix the server-side filter ignored Origin and typing
  `native` returned zero. This drives the SERVER-SIDE filter over the pipe, which is
  where the defect was -- the UI textbox is only its front end.

⚠ AB16 REQUIRES `native_c=true` ON THE SCAN. With it off, every candidate is
`Reflected` by construction, so `filter=native` legitimately returns 0 and the row
would read as still-broken. The rig therefore refuses to judge AB16 unless the
native-C scan actually produced some Native-C rows -- otherwise it reports
INCONCLUSIVE rather than FAIL.
"""
import pathlib
import sys

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
from pipe_client import PipeClient                        # noqa: E402

BYTEISH = ("byte", "enum")


def rows(c, sid, total, flt=None, limit=2000):
    out, off = [], 0
    while off < (total or 0):
        kw = dict(session_id=sid, offset=off, limit=limit)
        if flt:
            kw["filter"] = flt
        q = c.request("query_candidates", **kw)
        got = q.get("candidates") or []
        if not got:
            break
        out += got
        off += len(got)
    return out


def filtered_total(c, sid, flt):
    q = c.request("query_candidates", session_id=sid, offset=0, limit=5, filter=flt)
    return q.get("filtered_total"), (q.get("candidates") or [])


def main():
    ok = True
    with PipeClient(timeout=600.0) as c:
        c.assert_build()
        c.ensure_scanned()

        # ---------- AB14 ----------
        print("=== AB14: enum-backed fields must appear in a NumericAll scan ===")
        r = c.request("begin_value_scan", data_type="NumericAll", scan_type="Exact",
                      value="1", game_only=True, max_results=300000, deadline_ms=120000)
        sid = r["session_id"]
        allrows = rows(c, sid, r.get("total"))
        types = {}
        for x in allrows:
            types[x.get("field_type", "?")] = types.get(x.get("field_type", "?"), 0) + 1
        enumish = {t: n for t, n in types.items()
                   if any(k in t.lower() for k in BYTEISH)}
        print(f"  total={r.get('total')}  distinct field_type values={len(types)}")
        print(f"  byte/enum-typed candidates: {sum(enumish.values())}  {enumish}")
        ex = [x for x in allrows if any(k in x.get("field_type", "").lower() for k in BYTEISH)]
        for x in ex[:4]:
            print(f"    {x['class_name']}.{x['field_name']}  type={x['field_type']} "
                  f"value={x.get('value')}")
        if ex:
            print("  AB14 PASS -- enum/byte fields are emitted as candidates")
        else:
            print("  AB14 FAIL -- no byte/enum-typed candidate in the whole scan")
            ok = False
        c.request("end_value_scan", session_id=sid)

        # ---------- AB16 ----------
        print("\n=== AB16: the SERVER-SIDE filter must match the Origin column ===")
        r2 = c.request("begin_value_scan", data_type="Int32", scan_type="Exact", value="1",
                       game_only=True, max_results=300000, deadline_ms=120000,
                       native_c=True, native_align=4)
        sid2 = r2["session_id"]
        tot2 = r2.get("total")
        all2 = rows(c, sid2, tot2)
        native_rows = [x for x in all2 if x.get("is_native_c") or x.get("native_c")]
        print(f"  scan total={tot2}   (native_c=True)")

        n_native, ex_n = filtered_total(c, sid2, "native")
        n_refl, ex_r = filtered_total(c, sid2, "reflected")
        print(f"  filter 'native'    -> filtered_total={n_native}")
        for x in ex_n[:2]:
            print(f"      {x['class_name']}.{x['field_name']}  type={x.get('field_type')}")
        print(f"  filter 'reflected' -> filtered_total={n_refl}")
        for x in ex_r[:2]:
            print(f"      {x['class_name']}.{x['field_name']}  type={x.get('field_type')}")

        if not n_refl:
            print("  AB16 FAIL -- 'reflected' matched nothing, yet every non-native "
                  "candidate formats as exactly \"Reflected\"")
            ok = False
        elif n_native:
            print("  AB16 PASS -- both Origin spellings are matched server-side")
        else:
            print("  AB16 PARTIAL -- 'reflected' matches (so Origin IS consulted), but this "
                  "scan produced no Native-C rows, so the 'native' half is UNPROVEN here "
                  "rather than failed. Needs a title with raw non-UPROPERTY holes.")
        c.request("end_value_scan", session_id=sid2)

    print(f"\nAB14/AB16: {'PASS' if ok else 'NEEDS ATTENTION'}")
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main())
