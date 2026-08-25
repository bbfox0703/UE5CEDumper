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

Spawn source, selected with `--spawn`:

  debugcam  (default)  `set_debug_camera` on. UE instantiates `ADebugCameraController`
            and its components lazily on the first toggle, so this manufactures new
            UActorComponents without anyone playing the game. ⚠ It is ONE-SHOT per
            process: once those objects exist, cycling the camera off/on creates
            nothing, so a second run in the same session cannot spawn (measured on
            DumperTest 2026-08-22: 295 live objects before, 295 after).

  manual    Poll for new objects while a human — or computer-use — makes the game
            spawn them: walk into a battle, change map, reload a save. This is the
            mode for a real title (DQ7R et al.), where the game itself is a far better
            object factory than any debug lever. Nothing is written during the wait;
            the hold is already down by then, which is the whole point.

⚠ Whichever source is used, NEWNESS IS ESTABLISHED BY ADDRESS against a pre-spawn
snapshot — never by a count, and never by a name. An object that merely survived can
therefore not be mistaken for a new one, and neither can a recycled address that was
already in the `before` set.

⚠ Anti-vacuity, enforced: the run FAILS if the Force held nothing, if the CDO cannot
be found, or if the spawn produced no new object of the forced class — "no new
objects were wrong" is not a pass when there were no new objects.

⛔ The UI must be DISCONNECTED (it holds 2 of the 3 pipe slots).
"""
from __future__ import annotations

import argparse
import pathlib
import sys
import time

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
from pipe_client import PipeClient  # noqa: E402

# The docstring reaches argparse as --help text, and this console is cp950: without
# this, `--help` dies with UnicodeEncodeError on the first non-ASCII marker rather
# than printing anything. Same idiom as the other rigs here.
#
# line_buffering matters as much as the encoding here: in manual spawn mode this run
# blocks for MINUTES, and a redirected stdout is block-buffered, so the whole (A)
# section -- the CDO address, `held=N`, the live-sample channel proof -- can still be
# sitting in the buffer while the poll lines stream out. Measured 2026-08-23: a
# captured run began at the spawn banner with the entire mechanism half missing, i.e.
# the evidence existed and did not reach the log, which is the same as not having it.
sys.stdout.reconfigure(encoding="utf-8", errors="replace", line_buffering=True)
sys.stderr.reconfigure(encoding="utf-8", errors="replace", line_buffering=True)

BASE = "ActorComponent"
FIELD = "bIsEditorOnly"


def _fields(c: PipeClient, addr: str) -> dict:
    r = c.request("walk_instance", addr=addr)
    return {f.get("name"): str(f.get("value")) for f in (r.get("fields") or [])}


def _truthy(v) -> bool:
    return str(v).strip().lower().split(" ", 1)[0] in ("true", "1")


def _components(c: PipeClient, limit: int = 200000) -> dict:
    """addr -> (class, name) for live, non-CDO objects whose class contains 'Component'.

    ⚠ THE CAP IS LOAD-BEARING AND MUST NOT SILENTLY BITE. Newness is decided by
    diffing this set against itself across the spawn, so a truncated `before` makes
    every unsampled survivor look NEWLY SPAWNED — a false positive, in the direction
    that invents evidence rather than losing it. The old default was 400, which is
    fine on DumperTest (295 components) and wrong on any real game. `check_complete`
    turns a cap into a loud failure instead of a quiet sample.
    """
    out = {}
    r = c.request("find_instances", class_name="Component", limit=limit, exact_match=False)
    c.check_complete(r)
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


def _spawn_manual(c: PipeClient, before: dict, deadline_s: int, settle_s: float) -> dict:
    """Wait for the GAME to create objects. Returns the post-spawn map.

    Polls by ADDRESS diff, so it cannot be fooled by a count that happens to rise.
    Once the first genuinely-new object appears it waits `settle_s` more and re-reads,
    because a map change spawns in waves and the first wave is rarely the whole set.
    """
    print()
    print("  >> MANUAL SPAWN MODE — go make the game create objects now.")
    print("     (enter a battle, change map, reload a save …)  waiting up to %ds" % deadline_s)
    end = time.time() + deadline_s
    first_seen_at = None
    after = before
    while time.time() < end:
        time.sleep(3.0)
        after = _components(c)
        fresh = [a for a in after if a not in before]
        if fresh and first_seen_at is None:
            first_seen_at = time.time()
            print("     +%d new after %ds — settling %.0fs for the rest of the wave"
                  % (len(fresh), int(deadline_s - (end - time.time())), settle_s))
        if first_seen_at is not None and time.time() - first_seen_at >= settle_s:
            break
        if first_seen_at is None:
            print("     … still %d known, nothing new yet" % len(after))
    return _components(c)


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--spawn", choices=("debugcam", "manual"), default="debugcam",
                    help="how new objects are created (default: debugcam)")
    ap.add_argument("--wait", type=int, default=180,
                    help="manual mode: seconds to wait for a spawn (default: 180)")
    ap.add_argument("--settle", type=float, default=6.0,
                    help="manual mode: extra seconds after the first new object (default: 6)")
    ap.add_argument("--base", default=BASE, help="class to force (default: %s)" % BASE)
    ap.add_argument("--field", default=FIELD, help="bool field to force (default: %s)" % FIELD)
    args = ap.parse_args()
    globals()["BASE"], globals()["FIELD"] = args.base, args.field

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

        if args.spawn == "manual":
            after = _spawn_manual(c, before, args.wait, args.settle)
            how = "game-driven spawn"
        else:
            c.request("set_debug_camera", enable=True)
            time.sleep(2.5)
            after = _components(c)
            how = "debug camera on"
        fresh = [a for a in after if a not in before]
        print("%s -> %d component(s) that did NOT exist before" % (how, len(fresh)))
        for a in fresh[:8]:
            print("    %-16s %-34s %s" % (a, after[a][0], after[a][1]))
        assert fresh, ("nothing spawned — the row is explicit that looking at objects which "
                       "already existed cannot answer this, so this is NOT a pass")

        dirty = [(a, after[a][0], _fields(c, a).get(FIELD)) for a in fresh
                 if _truthy(_fields(c, a).get(FIELD))]
        print("  newly spawned carrying the forced value: %d" % len(dirty))
        for a, cls, v in dirty[:8]:
            print("    %-16s %-30s %s" % (a, cls, v))

        if args.spawn == "debugcam":
            c.request("set_debug_camera", enable=False)
        c.request("reset_all_fields")

        print()
        print("  (A) the CDO was never written        : %s" % ("PASS" if cdo_clean else "FAIL"))
        print("  (B) %d object(s) spawned after reset, none forced : %s"
              % (len(fresh), "PASS" if not dirty else "FAIL"))
        return 0 if (cdo_clean and not dirty) else 1


if __name__ == "__main__":
    raise SystemExit(main())
