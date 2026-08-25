"""A8: on a FLAT FFixedUObjectArray title, the CE pointer chain must degrade honestly.

    py a8_flat_layout.py            # expects a flat-array game already injected & scanned

THE DEFECT (audit #5 A8). On a flat (non-chunked) `FFixedUObjectArray`, `Objects*`
points straight at one `FUObjectItem[]` with **no chunk pointer table**. The 4-hop
chunked chain would treat `Item[0].Object` as a chunk table at hop 3 and hand CE a
GARBAGE address **the user can then write to**. The fix degrades to the absolute
object address with a warning and a single hop.

⚠ THE ROW SAYS "the degrade is only observable on a flat title (none available here)".
**That is no longer true** — OCTOPATH TRAVELER is flat and installed:
`ValidateGObjects: Valid at 0x… (preset Flat-Base, Num=273957, Max=6146976,
Objects=0x… [flat])`. FF7R Intergrade / Extinction / NEKOPALIVE are the other named
candidates.

WHAT IS ASSERTED, and why each half matters:
  * `flat_layout` is true and `packed_layout` is false — the right branch was taken;
  * `ce_offsets` is EXACTLY ONE hop and equals the requested `field_offset` — the
    4-hop chunked chain is what produced the garbage address;
  * `ce_base` is the ABSOLUTE object address, i.e. the address asked about — not a
    GObjects-relative base;
  * a warning is present and says the address will not survive a restart. A silent
    degrade would be almost as bad as the bug: the user would paste a
    session-only address into a saved cheat table.

The rig also checks the CONTROL half: `chunk_index` / `within_chunk` may still be
reported, but they must not appear in `ce_offsets`.
"""
import json
import pathlib
import sys

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
from pipe_client import PipeClient                        # noqa: E402

FIELD_OFFSET = 0x28          # arbitrary but non-zero, so "single hop == field_offset"
                             # cannot pass by accident on a zero


def main():
    ok = True
    with PipeClient(timeout=300.0) as c:
        c.assert_build()
        p = c.request("get_pointers")
        print(f"host GObjects={p.get('gobjects')} ({p.get('gobjects_method')})  "
              f"UE={p.get('ue_version')}")

        # Any live non-CDO object will do; the branch depends on the ARRAY layout,
        # not on which object is asked about.
        fi = c.request("find_instances", class_name="Actor", max_results=25)
        insts = [i for i in (fi.get("instances") or [])
                 if not (i.get("name") or "").startswith("Default__")]
        if not insts:
            insts = fi.get("instances") or []
        if not insts:
            print("a8: no instances returned; cannot ask about an object")
            return 1
        obj = insts[0]
        addr = obj.get("address") or obj.get("addr")
        print(f"object : {obj.get('name')}  @ {addr}")

        r = c.request("get_ce_pointer_info", addr=addr, field_offset=FIELD_OFFSET)
        print(json.dumps({k: v for k, v in r.items()
                          if k not in ("id", "ok", "game_thread_stalled")},
                         indent=1)[:1400])

        flat = r.get("flat_layout")
        packed = r.get("packed_layout")
        offs = r.get("ce_offsets")
        base = r.get("ce_base")
        warn = r.get("warning") or ""

        def check(label, cond):
            nonlocal ok
            print(f"  {'ok  ' if cond else 'FAIL'} {label}")
            if not cond:
                ok = False

        print("\n--- A8 assertions ---")
        check("flat_layout is true", flat is True)
        check("packed_layout is false", packed is False)
        check(f"ce_offsets is a SINGLE hop (got {offs})",
              isinstance(offs, list) and len(offs) == 1)
        check(f"the single hop == field_offset {FIELD_OFFSET}",
              isinstance(offs, list) and len(offs) == 1 and int(offs[0]) == FIELD_OFFSET)
        check(f"ce_base is the ABSOLUTE object address ({base} vs {addr})",
              base and addr and int(str(base), 16) == int(str(addr), 16))
        check("a warning is present", bool(warn))
        check("the warning says it will not survive a restart",
              "restart" in warn.lower() or "aslr" in warn.lower())
        print(f"\n  (reported chunk_index={r.get('chunk_index')} "
              f"within_chunk={r.get('within_chunk')} -- fine to report, "
              f"must not be IN the chain)")

    print(f"\nA8: {'PASS' if ok else 'FAIL'}")
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main())
