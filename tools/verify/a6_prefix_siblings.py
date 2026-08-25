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
        seen, cand = set(), []
        for probe in (FORCE_CLASS, FORCE_CLASS + "Component", FORCE_CLASS + "Channel"):
            r = c.request("find_instances", class_name=probe, limit=400, exact_match=False)
            for inst in r.get("instances") or []:
                cls = inst.get("class") or ""
                name = inst.get("name") or ""
                addr = inst.get("addr")
                if not addr or addr in seen:
                    continue
                if cls == FORCE_CLASS or not cls.startswith(FORCE_CLASS):
                    continue
                if name.startswith("Default__"):    # CDOs are skipped by the walk anyway
                    continue
                seen.add(addr)
                cand.append((addr, cls, name))

        # ⚠ An object with NO reflected fields cannot be diffed, so it is dropped rather
        # than counted — a stranger that can never show a change would pad the sample and
        # make the result look stronger than it is. Interfaces are all of this kind here.
        strangers, blind = [], []
        before = {}
        for addr, cls, name in cand:
            f = _fields(c, addr)
            (strangers if f else blind).append((addr, cls, name))
            if f:
                before[addr] = f
        print("same-prefix strangers: %d diffable, %d field-less (dropped)"
              % (len(strangers), len(blind)))
        for a, cls, n in strangers[:8]:
            print("    %-16s %-34s %-28s %d fields" % (a, cls, n, len(before[a])))
        assert strangers, (
            "no diffable object whose class NAME starts with %r but which is not %r — "
            "nothing a prefix match could wrongly catch, so this run would prove nothing"
            % (FORCE_CLASS, FORCE_CLASS))
        prefix_n = len(strangers)

        # ⚠ DumperTest yields only a handful of same-prefix strangers, which is a thin
        # sample for "nothing else was written". So a broad set of NON-derived objects is
        # added on top: same assertion, much more of it. The prefix cases stay reported
        # separately because they are the ones the row is actually about — a name matcher
        # would hit those and nothing else.
        for probe in ("StaticMeshComponent", "SceneComponent", "Texture", "Material"):
            r = c.request("find_instances", class_name=probe, limit=60, exact_match=False)
            for inst in (r.get("instances") or [])[:20]:
                addr, cls = inst.get("addr"), inst.get("class") or ""
                name = inst.get("name") or ""
                if not addr or addr in seen or name.startswith("Default__"):
                    continue
                if cls.startswith(FORCE_CLASS):      # already counted as a prefix case
                    continue
                f = _fields(c, addr)
                if not f:
                    continue
                seen.add(addr)
                strangers.append((addr, cls, name))
                before[addr] = f
        print("plus a broader non-derived sample -> %d diffable object(s) total"
              % len(strangers))
        assert len(strangers) >= 10, "sample too small to mean anything"

        # ── force ────────────────────────────────────────────────────────────
        c.request("reset_all_fields")
        fr = c.request("force_field", class_name=FORCE_CLASS, field_name=FORCE_FIELD,
                       kind="bool", on=True)
        held = fr.get("held", fr.get("count"))
        print()
        print("force_field -> held=%s truncated=%s" % (held, fr.get("truncated")))
        assert held and int(held) > 0, "the Force held nothing — the rest would be vacuous"
        time.sleep(1.5)                       # let the re-assert worker run at least once

        # ── detector (1): a class a NAME matcher would MISS must be held ─────
        #
        # ⭐ The positive half, and the sharper of the two. `StaticMeshActor` derives
        # from AActor but its name does NOT start with "Actor", so a prefix matcher
        # could not reach it. If its bIsEditorOnlyActor is set while the hold is up,
        # the matcher is walking the super-chain.
        #
        # ⚠ The log line is printed for the record but is NOT the assertion: its
        # "over N distinct class(es)" is `derivedCache.size()`, i.e. how many classes
        # the derivation test was EVALUATED for (3941 = the whole pool), not how many
        # matched. Read as a match count it would look like a catastrophic over-hold.
        line = _derived_line()
        print("detector (1) the DLL's own walk (for the record, not the assertion):")
        print("    %s" % (line.strip() if line else "<no FindInstancesDerivedFrom line>"))

        smr = c.request("find_instances", class_name="StaticMeshActor", limit=40,
                        exact_match=True)
        sma = [i for i in (smr.get("instances") or [])
               if not str(i.get("name", "")).startswith("Default__")]
        assert sma, "no live StaticMeshActor — the positive control cannot run"
        probe = sma[0]
        assert not str(probe.get("class", "")).startswith(FORCE_CLASS),             "the control class must NOT share the prefix, or it proves nothing"
        pv = _fields(c, probe["addr"]).get(FORCE_FIELD)
        held_derived = str(pv).strip().lower().split(" ", 1)[0] in ("true", "1")
        print("    %s %s -> %s = %s" % (probe["addr"], probe.get("class"), FORCE_FIELD, pv))

        # ── detector (2): did anything land in a stranger? ───────────────────
        after = {a: _fields(c, a) for a, _, _ in strangers}
        touched = []
        for a, cls, n in strangers:
            diff = [k for k in before[a] if before[a][k] != after[a].get(k)]
            if diff:
                touched.append((a, cls, n, diff[:6]))

        print("detector (2) full field diff over %d object(s) (%d of them same-prefix): "
              "%d touched" % (len(strangers), prefix_n, len(touched)))
        for a, cls, n, d in touched:
            print("    %-16s %-28s %s  fields: %s" % (a, cls, n, d))

        c.request("reset_all_fields")

        ok = not touched
        print()
        print("  (1) a derived class a NAME match would MISS is held : %s"
              % ("PASS" if held_derived else "FAIL"))
        print("  (2) no non-derived object written (%d, %d same-prefix) : %s"
              % (len(strangers), prefix_n, "PASS" if ok else "FAIL"))
        return 0 if (ok and held_derived) else 1


if __name__ == "__main__":
    raise SystemExit(main())
