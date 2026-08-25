#!/usr/bin/env python3
"""M1-M5 step 1 -- See-through's disable arms, with TWO independent detectors.

    py tools/verify/seethrough_arms.py probe   # what the DLL says right now
    py tools/verify/seethrough_arms.py run     # the whole arm in ONE connection
    py tools/verify/seethrough_arms.py after   # detector (2) only, after an arm that
                                               # already dropped the connection (arm c/d)

WHY TWO DETECTORS. The row says it outright: `seethrough_get_state`'s `hidden_count`
is the DLL's own bookkeeping, so a tick where `SetActorHiddenInGame` was invoked but
did not take looks exactly like a tick where it worked. Auditing the hide with the
hider's own tally is letting the accused be the witness.

  detector (1)  seethrough_get_state -> hidden_count == 0
  detector (2)  re-read each actor's OWN bHidden bit via walk_instance

* Detector (2) needs to know WHICH actors, and until 2026-08-22 the DLL reported only
a count. That gap was not academic: on DumperTest the count read 1 while not one of 33
independently reachable candidate actors had bHidden set -- and there was no way to
tell "my candidate set is wrong" from "the hide silently failed", which is precisely
the defect this row exists to catch. `hidden_actors` was added to the reply for that
reason ([SEETHRUSET-2026-08-22]); this rig is its consumer.

ANTI-VACUITY, enforced rather than documented:
  * `run` FAILS if hidden_count never rises -- nothing to hide from this pose means
    every assertion after it is empty.
  * `run` FAILS if the DLL names actors whose own bHidden bit is NOT set while
    hiding. That is the positive control for detector (2): a detector never shown to
    fire cannot certify anything when it stays quiet.

The UI must be DISCONNECTED: it holds 2 of the 3 pipe slots.
The DLL disables See-through when the pipe client disconnects, so `run` does
everything in one connection. Splitting it across invocations measures the
disabled state and calls it a pass.
"""
from __future__ import annotations

import json
import pathlib
import sys
import time

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
from pipe_client import PipeClient  # noqa: E402

OUT = pathlib.Path(__file__).resolve().parents[2] / "out"
LAST = OUT / "seethru_last_hidden.json"


def _state(c: PipeClient) -> dict:
    return c.request("seethrough_get_state")


def _hidden_bit(c: PipeClient, addr: str):
    """The actor's OWN bHidden value, read straight off the instance."""
    r = c.request("walk_instance", addr=addr)
    for f in r.get("fields") or []:
        if f.get("name") == "bHidden":
            return str(f.get("value"))
    return None


def _truthy(v) -> bool:
    # walk_instance renders a bit-field bool as `true (bit 7, mask 0x80)`, so an
    # equality test against "true" reads a HIDDEN actor as not hidden. Caught by the
    # positive control refusing to pass -- a detector has to be right about the
    # format of the thing it reads, not just about where to read it.
    return str(v).strip().lower().split(" ", 1)[0] in ("true", "1")


def _report(c: PipeClient, addrs, want: bool, label: str) -> bool:
    ok = True
    for a in addrs:
        v = _hidden_bit(c, a)
        good = (v is not None) and (_truthy(v) == want)
        ok = ok and good
        print("    %-16s bHidden=%-6s  %s" % (a, v, "ok" if good else "<-- WRONG"))
    print("  %s: %s" % (label, "PASS" if ok else "FAIL"))
    return ok


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

        if what == "after":
            # For arms whose whole point is that the connection went away (c: pull the
            # UI link, d: close the game). The DLL is expected to have restored
            # everything already; this checks the actors named by the LAST run.
            assert LAST.exists(), "no recorded hidden set -- run `run` first"
            addrs = json.loads(LAST.read_text(encoding="utf-8"))
            assert addrs, "EMPTY recorded set -- nothing to check, refusing to pass"
            s = _state(c)
            print("detector (1) DLL tally : active=%s hidden_count=%s"
                  % (s.get("active"), s.get("hidden_count")))
            ok1 = (s.get("hidden_count") or 0) == 0
            print("  (1) hidden_count == 0        : %s" % ("PASS" if ok1 else "FAIL"))
            print("detector (2) the %d actor(s) the DLL had hidden:" % len(addrs))
            ok2 = _report(c, addrs, False, "  (2) every one restored     ")
            if ok1 != ok2:
                print("  !! THE DETECTORS DISAGREE -- that is the finding.")
            return 0 if (ok1 and ok2) else 1

        if what == "run":
            pierce = int(sys.argv[2]) if len(sys.argv) > 2 else 1
            s0 = _state(c)
            assert not s0.get("active"), "already active -- start from off"

            c.request("seethrough_set", enable=True, count=pierce)
            hc, addrs = 0, []
            for _ in range(40):
                time.sleep(0.5)          # the worker ticks at ~10 Hz on the GAME thread
                s = _state(c)
                hc = s.get("hidden_count") or 0
                addrs = list(s.get("hidden_actors") or [])
                if hc:
                    break
            print("enabled            : hidden_count=%d  hidden_actors=%s" % (hc, addrs))
            assert hc > 0, ("hidden_count stayed 0 -- nothing to hide from this pose; "
                            "every assertion below would be vacuous")
            assert len(addrs) == hc, (
                "the DLL's count (%d) and its own list (%d) disagree -- that is a "
                "defect in the report itself" % (hc, len(addrs)))
            LAST.write_text(json.dumps(addrs), encoding="utf-8")

            print("POSITIVE CONTROL   : each named actor's own bHidden must be TRUE")
            fired = _report(c, addrs, True, "  detector (2) can FIRE     ")
            assert fired, ("the DLL named actor(s) it says it hid, and their own bHidden "
                           "bit is NOT set. Either the hide never took, or bHidden is not "
                           "the bit SetActorHiddenInGame writes here. Do not read any "
                           "'restored' result below as a pass until this is settled.")

            c.request("seethrough_set", enable=False, count=1)
            time.sleep(1.0)
            s2 = _state(c)
            print()
            print("after disable      : active=%s hidden_count=%s"
                  % (s2.get("active"), s2.get("hidden_count")))
            ok1 = (s2.get("hidden_count") or 0) == 0
            print("  (1) hidden_count == 0        : %s" % ("PASS" if ok1 else "FAIL"))
            print("detector (2) the same %d actor(s):" % len(addrs))
            ok2 = _report(c, addrs, False, "  (2) every one restored     ")
            if ok1 != ok2:
                print("  !! THE DETECTORS DISAGREE -- that is the finding this row exists for.")
            return 0 if (ok1 and ok2) else 1

    print("unknown verb %r" % what)
    return 2


if __name__ == "__main__":
    raise SystemExit(main())
