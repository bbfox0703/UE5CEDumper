#!/usr/bin/env python3
r"""L42 `[W5-CSX-DELEGATEPAD]` offline half: do a saved Live Walker .CSX's delegate Elements sit where the payload is?

    py tools/verify/l42_csx_check.py <file.CSX> --walk <walk_instance.json> [--flavour dev|shipping]

Read-only. `--walk` is a saved `pipe_client.py walk_instance` reply for the SAME actor (its `fields` carry each field's
`offset` and, on a checked build, `delegate_pad`). For the actor's top-level Structure it prints, per delegate field,
the Elements the CSX emitted and what CsxExportService should have emitted:
  * DelegateProperty (`Del_Unicast`)            -> one Element at offset + pad (the FWeakObjectPtr, past the detector)
  * Multicast with a drill (`Multicast_Inline`) -> the raw block at the FIELD offset, and
                                                   "<name> / InvocationList" at offset + pad, Vartype Pointer, with a
                                                   child <Structure>
On Shipping the pad is 0, so right and wrong emitters agree: `--flavour shipping` is a control, not a discriminator.
Exit 0 when every checked Element matches, 1 otherwise. It never writes.
"""
from __future__ import annotations

import argparse
import json
import sys
import xml.etree.ElementTree as ET

sys.stdout.reconfigure(encoding="utf-8", errors="replace")
FIELDS = ("Del_Unicast", "Multicast_Inline")


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("csx")
    ap.add_argument("--walk", required=True)
    ap.add_argument("--flavour", default="dev")
    a = ap.parse_args()
    # pipe_client.py prints a "# build: N" line before the JSON; accept its stdout saved as-is.
    raw = open(a.walk, encoding="utf-8").read().splitlines()
    walk = json.loads("\n".join(ln for ln in raw if not ln.startswith("#")))
    wf = {f.get("name"): f for f in walk.get("fields", [])}
    root = ET.parse(a.csx).getroot()
    top = root.find(".//Structure")
    elems = top.find("Elements") if top is not None else None
    if elems is None:
        print("no top-level <Structure><Elements> in %s" % a.csx)
        return 1
    ok = True
    for name in FIELDS:
        f = wf.get(name)
        if not f:
            print("%-18s not in the walk" % name)
            ok = False
            continue
        off, pad = int(f.get("offset", -1)), int(f.get("delegate_pad", 0) or 0)
        got = [(e.get("Description"), int(e.get("Offset")), e.get("Vartype"), e.find("Structure") is not None)
               for e in elems.findall("Element") if (e.get("Description") or "").startswith(name)]
        print("%-18s walk offset=%d (0x%X) delegate_pad=%d type=%s" % (name, off, off, pad, f.get("type")))
        for g in got:
            print("    CSX: %-34s Offset=%-5d Vartype=%-8s child=%s" % g)
        if name == "Del_Unicast":
            want = [(name, off + pad)]
        else:
            want = [(name, off), (name + " / InvocationList", off + pad)]
        for d, o in want:
            hit = [g for g in got if g[0] == d and g[1] == o]
            good = bool(hit)
            if d.endswith("/ InvocationList"):
                good = good and hit[0][2] == "Pointer" and hit[0][3]
            print("    want %-33s Offset=%-5d -> %s" % (d, o, "OK" if good else "MISMATCH"))
            ok = ok and good
    print("VERDICT (%s): %s" % (a.flavour, "PASS" if ok else "FAIL"))
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main())
