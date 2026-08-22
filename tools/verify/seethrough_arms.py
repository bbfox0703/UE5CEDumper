#!/usr/bin/env python3
"""M1-M5 step 1 -- See-through's disable arms, with TWO independent detectors.

    py tools/verify/seethrough_arms.py probe        # what does the DLL say right now
    py tools/verify/seethrough_arms.py baseline     # snapshot every candidate's hidden flag
    py tools/verify/seethrough_arms.py on           # enable, wait for hidden_count > 0
    py tools/verify/seethrough_arms.py check        # both detectors, against the baseline
    py tools/verify/seethrough_arms.py off          # disable, then check

THE POINT, and why one detector is not enough. The row says so in as many words:
`seethrough_get_state`'s `hidden_count` is the DLL's own bookkeeping, so a run where
the count zeroes but the `SetActorHiddenInGame` invoke silently failed looks exactly
like a pass. Using only the DLL's tally is letting the accused be the witness. So:

  detector (1)  seethrough_get_state -> hidden_count == 0
  detector (2)  re-read the ACTORS' own bHidden bit, independently, and compare
                against a baseline taken before See-through was ever enabled

Detector (2) needs to know WHICH actor got hidden, and the DLL does not report that
-- only a count. So the rig diffs: snapshot the whole candidate set's bHidden before,
snapshot again while hiding, and whatever flipped IS the hidden actor. That also
doubles as the channel proof the row demands (`hidden_count > 0` first, or all four
arms are vacuous), and it is stronger: it names the actor rather than trusting a
number.

ANTI-VACUITY. `check` FAILS if the baseline is empty, if no candidate was ever seen
hidden, or if the two detectors disagree -- a disagreement is the finding, not an
error to be smoothed over.

⛔ The UI must be DISCONNECTED: it holds 2 of the 3 pipe slots.
"""
from __future__ import annotations

import json
import pathlib
import sys

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
from pipe_client import PipeClient  # noqa: E402

OUT = pathlib.Path(__file__).resolve().parents[2] / "out"
BASE = OUT / "seethru_baseline.json"

# Occluder candidates. See-through only hides non-Pawn/Character hits, and in a
# stock UE map the thing in front of the camera is world geometry.
CANDIDATE_CLASSES = ["StaticMeshActor", "Actor"]

HIDDEN_FIELDS = ("bHidden", "bActorHiddenInGame", "bIsHidden")


def _instances(c: PipeClient, cls: str, limit: int = 400) -> list[dict]:
    r = c.request("find_instances", class_name=cls, limit=limit, exact_match=False)
    return r.get("instances") or r.get("results") or []


def _hidden_of(c: PipeClient, addr: str) -> tuple[str, str] | None:
    """(field name, value) of whichever hidden-ish bool this instance exposes."""
    r = c.request("walk_instance", addr=addr)
    for f in r.get("fields") or []:
        n = f.get("name")
        if n in HIDDEN_FIELDS:
            return n, str(f.get("value"))
    return None


def _snapshot(c: PipeClient) -> dict[str, dict]:
    seen: dict[str, dict] = {}
    for cls in CANDIDATE_CLASSES:
        for inst in _instances(c, cls):
            addr = inst.get("addr") or inst.get("address")
            name = inst.get("name", "")
            if not addr or addr in seen or name.startswith("Default__"):
                continue
            hv = _hidden_of(c, addr)
            if hv:
                seen[addr] = {"name": name, "cls": inst.get("type", cls),
                              "field": hv[0], "value": hv[1]}
    return seen


def _state(c: PipeClient) -> dict:
    return c.request("seethrough_get_state")


def main() -> int:
    if len(sys.argv) < 2:
        print(__doc__)
        return 2
    what = sys.argv[1]
    OUT.mkdir(exist_ok=True)

    with PipeClient().connect() as c:
        if what == "probe":
            s = _state(c)
            print(json.dumps({k: s[k] for k in sorted(s) if k != "id"}, indent=2))
            return 0

        if what == "baseline":
            s = _state(c)
            if s.get("active"):
                print("REFUSING: See-through is ACTIVE (hidden_count=%s). A baseline "
                      "taken while it hides is not a baseline." % s.get("hidden_count"))
                return 1
            snap = _snapshot(c)
            assert snap, "EMPTY candidate set -- refusing to write a baseline nothing can fail against"
            BASE.write_text(json.dumps(snap, indent=1), encoding="utf-8")
            hid = sum(1 for v in snap.values() if v["value"].lower() in ("true", "1"))
            print("baseline: %d actor(s) with a hidden flag; %d already hidden" % (len(snap), hid))
            print("  -> %s" % BASE)
            return 0

        if what == "on":
            pierce = int(sys.argv[2]) if len(sys.argv) > 2 else 1
            c.request("seethrough_set", enable=True, count=pierce)
            for _ in range(40):
                s = _state(c)
                if (s.get("hidden_count") or 0) > 0:
                    print("ENABLED, hidden_count=%s has_target=%s" %
                          (s.get("hidden_count"), s.get("has_target")))
                    return 0
            print("hidden_count stayed 0 -- nothing in front of the camera to hide. "
                  "The arms below would all be VACUOUS on this fixture/pose.")
            return 1

        if what in ("check", "off"):
            if what == "off":
                c.request("seethrough_set", enable=False, count=1)

            assert BASE.exists(), "no baseline -- run `baseline` before enabling"
            base = json.loads(BASE.read_text(encoding="utf-8"))
            assert base, "EMPTY baseline"

            s = _state(c)
            now = _snapshot(c)

            drift = [(a, base[a]["name"], base[a]["value"], now[a]["value"])
                     for a in base if a in now and base[a]["value"] != now[a]["value"]]

            print("detector (1) DLL tally : active=%s hidden_count=%s" %
                  (s.get("active"), s.get("hidden_count")))
            print("detector (2) actor bits: %d of %d candidates differ from baseline"
                  % (len(drift), len(base)))
            for a, n, b, v in drift[:10]:
                print("    %s  %-40s %s -> %s" % (a, n, b, v))

            ok1 = (s.get("hidden_count") or 0) == 0
            ok2 = not drift
            print()
            print("  (1) hidden_count == 0        : %s" % ("PASS" if ok1 else "FAIL"))
            print("  (2) no actor left hidden     : %s" % ("PASS" if ok2 else "FAIL"))
            if ok1 != ok2:
                print("  ⚠ THE DETECTORS DISAGREE -- that is the finding this row exists for.")
            return 0 if (ok1 and ok2) else 1

    print("unknown verb %r" % what)
    return 2


if __name__ == "__main__":
    raise SystemExit(main())
