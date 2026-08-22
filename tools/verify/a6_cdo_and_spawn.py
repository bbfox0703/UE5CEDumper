#!/usr/bin/env python3
"""A6 step 5 — a Force must never reach the CDO, so objects spawned AFTER a reset are clean.

    py tools/verify/a6_cdo_and_spawn.py

The row's method is "reset, then make new objects and look at them", with an explicit
warning that inspecting objects that already existed cannot answer this. Both halves
are run here, because they answer different things:

  (A) the MECHANISM — read the class's own `Default__` CDO while the hold is UP. If
      the CDO was written, every later spawn inherits the forced value, and no amount
      of looking at live instances would say why.
  (B) the CONSEQUENCE the row asks for — spawn genuinely new objects AFTER
      `reset_all_fields` and read the field on them. Newness is established by
      ADDRESS: the set is diffed against a snapshot taken before the spawn, so an
      object that merely survived cannot be mistaken for a new one.

Spawn source: `set_debug_camera` on. UE instantiates `ADebugCameraController` and its
components lazily on the first toggle, so this manufactures new UActorComponents on
demand without a human playing the game.

⚠ Anti-vacuity, enforced: the run FAILS if the Force held nothing, if the CDO cannot
be found, or if the spawn produced no new object of the forced class — "no new
objects were wrong" is not a pass when there were no new objects.

⛔ The UI must be DISCONNECTED (it holds 2 of the 3 pipe slots).
"""
from __future__ import annotations

import pathlib
import sys
import time

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
from pipe_client import PipeClient  # noqa: E402

BASE = "ActorComponent"
FIELD = "bIsEditorOnly"


def _fields(c: PipeClient, addr: str) -> dict:
    r = c.request("walk_instance", addr=addr)
    return {f.get("name"): str(f.get("value")) for f in (r.get("fields") or [])}


def _truthy(v) -> bool:
    return str(v).strip().lower().split(" ", 1)[0] in ("true", "1")


def _components(c: PipeClient, limit: int = 400) -> dict:
    """addr -> (class, name) for live, non-CDO objects whose class contains 'Component'."""
    out = {}
    r = c.request("find_instances", class_name="Component", limit=limit, exact_match=False)
    for i in r.get("instances") or []:
        n = str(i.get("name", ""))
        if i.get("addr") and not n.startswith("Default__"):
            out[i["addr"]] = (i.get("class"), n)
    return out


def _cdo(c: PipeClient, cls: str) -> str | None:
    r = c.request("find_instances", class_name=cls, limit=50, exact_match=True)
    for i in r.get("instances") or []:
        if str(i.get("name", "")).startswith("Default__"):
            return i["addr"]
    return None


def main() -> int:
    with PipeClient().connect() as c:
        c.request("reset_all_fields")

        cdo = _cdo(c, BASE)
        assert cdo, "no Default__%s found — the CDO half cannot run" % BASE
        cdo_before = _fields(c, cdo).get(FIELD)
        print("CDO %s @ %s : %s = %s" % (BASE, cdo, FIELD, cdo_before))
        assert cdo_before is not None, "the CDO does not expose %s" % FIELD
        assert not _truthy(cdo_before), "the CDO already has the field set — start clean"

        # ── (A) the mechanism: hold up, CDO must stay untouched ──────────────
        fr = c.request("force_field", class_name=BASE, field_name=FIELD, kind="bool", on=True)
        held = int(fr.get("held") or 0)
        print()
        print("force_field -> held=%s truncated=%s" % (held, fr.get("truncated")))
        assert held > 0, "held nothing — everything below would be vacuous"
        time.sleep(1.0)

        live = _components(c)
        sample = [a for a in list(live)[:12]]
        on_live = sum(1 for a in sample if _truthy(_fields(c, a).get(FIELD)))
        cdo_during = _fields(c, cdo).get(FIELD)
        print("  live sample of %d: %d actually forced   (channel proof)" % (len(sample), on_live))
        print("  CDO during hold : %s = %s" % (FIELD, cdo_during))
        assert on_live > 0, ("not one sampled live component carries the forced value, so "
                             "'the CDO is clean' would prove nothing about the write path")
        cdo_clean = not _truthy(cdo_during)

        # ── (B) the consequence: reset, then SPAWN ───────────────────────────
        c.request("reset_all_fields")
        time.sleep(0.5)
        before = _components(c)
        print()
        print("after reset: %d live component(s) known" % len(before))

        c.request("set_debug_camera", enable=True)
        time.sleep(2.5)
        after = _components(c)
        fresh = [a for a in after if a not in before]
        print("debug camera on -> %d component(s) that did NOT exist before" % len(fresh))
        for a in fresh[:8]:
            print("    %-16s %-34s %s" % (a, after[a][0], after[a][1]))
        assert fresh, ("nothing spawned — the row is explicit that looking at objects which "
                       "already existed cannot answer this, so this is NOT a pass")

        dirty = [(a, after[a][0], _fields(c, a).get(FIELD)) for a in fresh
                 if _truthy(_fields(c, a).get(FIELD))]
        print("  newly spawned carrying the forced value: %d" % len(dirty))
        for a, cls, v in dirty[:8]:
            print("    %-16s %-30s %s" % (a, cls, v))

        c.request("set_debug_camera", enable=False)
        c.request("reset_all_fields")

        print()
        print("  (A) the CDO was never written        : %s" % ("PASS" if cdo_clean else "FAIL"))
        print("  (B) %d object(s) spawned after reset, none forced : %s"
              % (len(fresh), "PASS" if not dirty else "FAIL"))
        return 0 if (cdo_clean and not dirty) else 1


if __name__ == "__main__":
    raise SystemExit(main())
