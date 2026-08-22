#!/usr/bin/env python3
"""A6 step 3 — a Force must hold SUBCLASSES, never same-prefix strangers.

    py tools/verify/a6_prefix_siblings.py

The row is explicit that a big held count proves nothing here: a prefix match also
holds hundreds, and the two look identical from the outside. So this checks the one
thing a prefix match could not survive — that objects whose class NAME starts with
the forced class name, but which do not DERIVE from it, are left completely alone.

Fixture (DumperTest, found by measurement): force `Actor::bIsEditorOnlyActor`.
`ActorComponent`, `ActorChannel`, `ActorElementAssetDataInterface` … all begin with
"Actor" and none of them is an AActor. If the matcher were a name test they would be
written at AActor's +0x5B, which on a component is some unrelated field.

TWO detectors, because the DLL's own tally is not evidence about itself:
  (1) `FindInstancesDerivedFrom base='Actor'` in pipe-0.log — the held count and,
      more tellingly, "over N distinct class(es)"
  (2) a FULL field snapshot of the same-prefix strangers, before and after, diffed.
      This does not need to know which offset the write would land on: any write
      into one of those objects shows up as a changed field.

⚠ Anti-vacuity: the run FAILS if no same-prefix stranger was found, if the Force
held nothing, or if the strangers list is empty — a diff over nothing is not a pass.

⛔ The UI must be DISCONNECTED (it holds 2 of the 3 pipe slots).
"""
from __future__ import annotations

import os
import pathlib
import re
import sys
import time

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
from pipe_client import PipeClient  # noqa: E402

FORCE_CLASS = "Actor"
FORCE_FIELD = "bIsEditorOnlyActor"
LOGDIR = pathlib.Path(os.environ["LOCALAPPDATA"]) / "UE5CEDumper" / "Logs" / "DumperTest"


def _fields(c: PipeClient, addr: str) -> dict:
    r = c.request("walk_instance", addr=addr)
    return {f.get("name"): str(f.get("value")) for f in (r.get("fields") or [])}


def _derived_line() -> str | None:
    """The DLL's own account of the walk, newest first."""
    p = LOGDIR / "pipe-0.log"
    if not p.exists():
        return None
    hits = [ln for ln in p.read_text(encoding="utf-8", errors="replace").splitlines()
            if "FindInstancesDerivedFrom" in ln]
    return hits[-1] if hits else None


def main() -> int:
    with PipeClient().connect() as c:
        # ── find same-prefix strangers: name starts with FORCE_CLASS, class differs ──
        r = c.request("find_instances", class_name=FORCE_CLASS, limit=400, exact_match=False)
        strangers = []
        for inst in r.get("instances") or []:
            cls = inst.get("class") or ""
            name = inst.get("name") or ""
            if cls == FORCE_CLASS or not cls.startswith(FORCE_CLASS):
                continue
            if name.startswith("Default__"):        # CDOs are skipped by the walk anyway
                continue
            strangers.append((inst["addr"], cls, name))
        print("same-prefix strangers found: %d" % len(strangers))
        for a, cls, n in strangers[:8]:
            print("    %-16s %-34s %s" % (a, cls, n))
        assert strangers, ("no object whose class NAME starts with %r but is not %r — "
                           "there is nothing a prefix match could wrongly catch, so this "
                           "run would prove nothing" % (FORCE_CLASS, FORCE_CLASS))

        before = {a: _fields(c, a) for a, _, _ in strangers}
        assert all(before.values()), "a stranger walked to zero fields — refusing to diff nothing"

        # ── force ────────────────────────────────────────────────────────────
        c.request("reset_all_fields")
        fr = c.request("force_field", class_name=FORCE_CLASS, field_name=FORCE_FIELD,
                       kind="bool", on=True)
        held = fr.get("held", fr.get("count"))
        print()
        print("force_field -> held=%s truncated=%s" % (held, fr.get("truncated")))
        assert held and int(held) > 0, "the Force held nothing — the rest would be vacuous"
        time.sleep(1.5)                       # let the re-assert worker run at least once

        print("detector (1) the DLL's own walk:")
        line = _derived_line()
        print("    %s" % (line.strip() if line else "<no FindInstancesDerivedFrom line in pipe-0.log>"))
        classes = None
        if line:
            m = re.search(r"over (\d+) distinct class\(es\)", line)
            if m:
                classes = int(m.group(1))

        # ── detector (2): did anything land in a stranger? ───────────────────
        after = {a: _fields(c, a) for a, _, _ in strangers}
        touched = []
        for a, cls, n in strangers:
            diff = [k for k in before[a] if before[a][k] != after[a].get(k)]
            if diff:
                touched.append((a, cls, n, diff[:6]))

        print("detector (2) full field diff over %d stranger(s): %d touched"
              % (len(strangers), len(touched)))
        for a, cls, n, d in touched:
            print("    %-16s %-28s %s  fields: %s" % (a, cls, n, d))

        c.request("reset_all_fields")

        ok = not touched
        print()
        print("  (1) walk is derivation-scoped : %s"
              % ("PASS (%s distinct class(es))" % classes if classes is not None
                 else "no log line — inconclusive"))
        print("  (2) no same-prefix stranger written : %s" % ("PASS" if ok else "FAIL"))
        return 0 if ok else 1


if __name__ == "__main__":
    raise SystemExit(main())
