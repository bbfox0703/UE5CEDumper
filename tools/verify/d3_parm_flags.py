r"""D3 -- walk_functions' `parm` flag on a running game: two reads of one fact must agree.

    py tools/verify/d3_parm_flags.py [--label NAME] [--max-classes 3000] [--min-locals 10]
    (a host running and injected with a DLL from build 3622 or later; never while the UI holds the pipe)

TWO READS. Since [EXTPR-539-540-2026-10-02] D3 every walk_functions entry carries `parm`, read from that
property's own CPF_Parm bit. UE records the same fact a second way, on the UFunction: NumParms is the number
of leading CPF_Parm properties and ParmsSize the end of the parameter block. A Blueprint function's chain
also holds its locals after the parameters (the 2026-08 Y1 trap), and those must come back `parm: false`.

ASSERTIONS
  (a) every entry carries a boolean `parm`. Missing = the DLL answering predates D3: a stale build, and
      every later count would be about the wrong binary.
  (b) num_parms == 0 and parms_size == 0 is also what a FAILED num_parms probe looks like, so such a
      function is not counted as agreeing; it goes to `ambiguous` when it has flagged entries. The rate is
      itself a finding.
  (c) for every other function, the number of `parm: true` entries equals num_parms.
  (d) every `parm: true` entry lies inside the parameter block: offset >= 0 and offset + size <= parms_size.
  (e) no `parm: true` entry follows a `parm: false` one. Dump All's fallback for an older DLL (the leading
      num_parms entries) rests on this ordering, so it is asserted, not assumed.
  (f) anti-vacuity: at least --min-locals functions with a `parm: false` entry. Without them the run never
      met a Blueprint local and proves nothing about the case D3 exists for.

Writes out/d3/<label>.json: the counts, up to 20 examples per violation, and five functions with locals.
"""
import argparse
import json
import pathlib
import sys

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
sys.stdout.reconfigure(encoding="utf-8", errors="replace")

from pipe_client import PipeClient  # noqa: E402

BP_META = ("BlueprintGeneratedClass", "WidgetBlueprintGeneratedClass", "AnimBlueprintGeneratedClass")
OUTDIR = pathlib.Path("out/d3")
PAGE = 2000


def say(s):
    enc = sys.stdout.encoding or "utf-8"
    sys.stdout.write(str(s).encode(enc, "replace").decode(enc, "replace") + chr(10))
    sys.stdout.flush()


def list_classes(c):
    """Every class-like object, Blueprint classes first (they are the ones with locals)."""
    bp, native = [], []
    offset = 0
    while True:
        r = c.request("get_object_list", offset=offset, limit=PAGE)
        objs = r.get("objects") or []
        for o in objs:
            meta = o.get("class", "")
            if meta in BP_META:
                bp.append(o)
            elif meta == "Class":
                native.append(o)
        step = r.get("scanned") or len(objs)
        total = r.get("total", 0)
        offset += step
        if step <= 0 or offset >= total:
            break
    return bp, native


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--label", default="run")
    ap.add_argument("--max-classes", type=int, default=3000)
    ap.add_argument("--min-locals", type=int, default=10)
    a = ap.parse_args()

    counts = dict(classes=0, bp_classes=0, functions=0, entries=0, ambiguous=0, agreeing=0,
                  with_locals=0, locals=0, locals_inside_block=0)
    bad = dict(missing_flag=[], count_mismatch=[], parm_outside_block=[], parm_after_local=[])
    examples = []

    with PipeClient() as c:
        build = c.assert_build()
        c.ensure_scanned()
        bp, native = list_classes(c)
        targets = (bp + native)[: a.max_classes]
        say(f"build {build}: {len(bp)} Blueprint classes, {len(native)} native; walking {len(targets)}")

        for o in targets:
            try:
                r = c.request("walk_functions", addr=o["addr"])
            except Exception as e:   # one unreadable class is not a verdict on the flag
                say(f"walk_functions failed on {o.get('name')}: {e}")
                continue
            counts["classes"] += 1
            if o.get("class") in BP_META:
                counts["bp_classes"] += 1
            for f in r.get("functions", []):
                counts["functions"] += 1
                ps = f.get("params", [])
                counts["entries"] += len(ps)
                who = f"{o.get('name')}::{f.get('name')}"
                if any(not isinstance(p.get("parm"), bool) for p in ps):
                    bad["missing_flag"].append(who)
                    continue
                n, size = f.get("num_parms", 0), f.get("parms_size", 0)
                flagged = [p for p in ps if p["parm"]]
                local = [p for p in ps if not p["parm"]]
                if local:
                    counts["with_locals"] += 1
                    counts["locals"] += len(local)
                    counts["locals_inside_block"] += sum(1 for p in local if p.get("offset", -1) < size)
                    if len(examples) < 5:
                        examples.append(dict(func=who, num_parms=n, parms_size=size,
                                             params=[[p["name"], p["parm"], p.get("offset"), p.get("size")]
                                                     for p in ps]))
                seen_local = False
                for p in ps:
                    if not p["parm"]:
                        seen_local = True
                    elif seen_local:
                        bad["parm_after_local"].append(f"{who}.{p['name']}")
                        break
                if n == 0 and size == 0:
                    if flagged:
                        counts["ambiguous"] += 1
                    continue
                if len(flagged) != n:
                    bad["count_mismatch"].append(f"{who}: {len(flagged)} flagged, num_parms {n}")
                    continue
                outside = [p["name"] for p in flagged
                           if p.get("offset", -1) < 0 or p.get("offset", 0) + p.get("size", 0) > size]
                if outside:
                    bad["parm_outside_block"].append(f"{who}: {outside} (parms_size {size})")
                    continue
                counts["agreeing"] += 1

    failures = {k: len(v) for k, v in bad.items() if v}
    vacuous = counts["with_locals"] < a.min_locals
    report = dict(build=build, counts=counts, failures=failures,
                  violations={k: v[:20] for k, v in bad.items() if v}, local_examples=examples,
                  vacuous=vacuous, verdict="FAIL" if failures or vacuous else "PASS")
    OUTDIR.mkdir(parents=True, exist_ok=True)
    out = OUTDIR / f"{a.label}.json"
    out.write_text(json.dumps(report, indent=1, ensure_ascii=False), encoding="utf-8")

    say(json.dumps(counts))
    for k, v in bad.items():
        if v:
            say(f"FAIL {k}: {len(v)}, e.g. {v[:3]}")
    if vacuous:
        say(f"FAIL vacuous: only {counts['with_locals']} functions with a local (need {a.min_locals})")
    say(f"{report['verdict']} -> {out}")
    return 0 if report["verdict"] == "PASS" else 1


if __name__ == "__main__":
    sys.exit(main())
