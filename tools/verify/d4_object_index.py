r"""D4 -- get_object_list's include_index on a running game: the index must BE the GObjects slot.

    py tools/verify/d4_object_index.py [--label NAME] [--pages 3] [--samples 200]
    (a host running and injected with a DLL from build 3625 or later; never while the UI holds the pipe)

WHY A SECOND READ. The object index writes each object's slot so two files can be matched by it (Dumper-7's
lesson). The handler skips null and unnamed slots, so the slot is NOT a row's position in the page -- which is
exactly what an index computed from the position would look like, and why the UI never guesses one. The rig
reads each sampled slot back by itself (`get_object_list offset=<index> limit=1`) and requires the same object.

ASSERTIONS
  (a) with include_index, every row carries an integer `index`; without it, none does (the lean reply is unchanged).
  (b) indexes rise strictly within a page and stay inside [offset, offset + scanned).
  (c) for --samples rows spread over --pages pages, `offset=<index> limit=1` returns that same address first.
  (d) anti-vacuity: at least one page held a skipped slot (a row whose index differs from offset + its position),
      or the run never met the case the index exists for; reported, and FAIL when no page had one.

Writes out/d4/<label>.json.
"""
import argparse
import json
import pathlib
import sys

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
sys.stdout.reconfigure(encoding="utf-8", errors="replace")

from pipe_client import PipeClient  # noqa: E402

PAGE = 5000
OUTDIR = pathlib.Path("out/d4")


def say(s):
    enc = sys.stdout.encoding or "utf-8"
    sys.stdout.write(str(s).encode(enc, "replace").decode(enc, "replace") + chr(10))
    sys.stdout.flush()


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--label", default="run")
    ap.add_argument("--pages", type=int, default=3)
    ap.add_argument("--samples", type=int, default=200)
    a = ap.parse_args()

    bad = dict(missing_index=0, lean_has_index=0, not_rising=0, outside_window=0, readback_mismatch=[])
    counts = dict(pages=0, rows=0, pages_with_skipped_slots=0, samples=0)

    with PipeClient() as c:
        build = c.assert_build()
        c.ensure_scanned()
        lean = c.request("get_object_list", offset=0, limit=50)
        bad["lean_has_index"] = sum(1 for o in lean.get("objects", []) if "index" in o)

        total = lean.get("total", 0)
        step = max(1, total // max(1, a.pages))
        rows = []
        for p in range(a.pages):
            offset = min(p * step, max(0, total - 1))
            r = c.request("get_object_list", offset=offset, limit=PAGE, include_index=True, include_path=True)
            objs = r.get("objects", [])
            scanned = r.get("scanned", 0)
            counts["pages"] += 1
            counts["rows"] += len(objs)
            prev = -1
            skipped = False
            for pos, o in enumerate(objs):
                idx = o.get("index")
                if not isinstance(idx, int):
                    bad["missing_index"] += 1
                    continue
                if idx <= prev:
                    bad["not_rising"] += 1
                if not (offset <= idx < offset + scanned):
                    bad["outside_window"] += 1
                if idx != offset + pos:
                    skipped = True
                prev = idx
                rows.append(o)
            if skipped:
                counts["pages_with_skipped_slots"] += 1

        stride = max(1, len(rows) // max(1, a.samples))
        for o in rows[::stride][: a.samples]:
            idx = o.get("index")
            if not isinstance(idx, int):
                continue
            back = c.request("get_object_list", offset=idx, limit=1).get("objects", [])
            counts["samples"] += 1
            if not back or back[0].get("addr") != o.get("addr"):
                bad["readback_mismatch"].append(
                    {"index": idx, "addr": o.get("addr"), "got": back[0].get("addr") if back else None})

    failures = {k: (len(v) if isinstance(v, list) else v) for k, v in bad.items() if v}
    vacuous = counts["pages_with_skipped_slots"] == 0
    report = dict(build=build, counts=counts, failures=failures,
                  readback_mismatch=bad["readback_mismatch"][:20], vacuous=vacuous,
                  verdict="FAIL" if failures or vacuous else "PASS")
    OUTDIR.mkdir(parents=True, exist_ok=True)
    out = OUTDIR / f"{a.label}.json"
    out.write_text(json.dumps(report, indent=1, ensure_ascii=False), encoding="utf-8")
    say(json.dumps(counts))
    for k, v in failures.items():
        say(f"FAIL {k}: {v}")
    if vacuous:
        say("FAIL vacuous: no page held a skipped slot, so position and index never differed")
    say(f"{report['verdict']} -> {out}")
    return 0 if report["verdict"] == "PASS" else 1


if __name__ == "__main__":
    sys.exit(main())
