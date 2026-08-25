"""AB17: idle value-scan sessions are reaped, and an active one is protected.

    py ab17_session_reap.py reap      # phase 1: an idle session is swept by a later Begin
    py ab17_session_reap.py protect   # phase 2: Refine protects its OWN session

THE POLICY (`Radar.h:19`, constant at `:837`). Reaping is **activity-triggered, not a
wall clock**: every `Begin` / `RefineWith` / `End` sweeps sessions idle past
`kScanSessionIdleExpiry` = **300 s**, and a `Refine`/`End` protects its OWN session
first. The read/query path is deliberately NOT swept, because it is hit on every page.

That design is exactly why this needs a live check and cannot be unit-tested: the
sweep TRIGGER and the protect-mine-first ORDERING are both wall-clock behaviours.

TWO PHASES, and the second is the one that could regress silently:
  * `reap`    — open A, idle it past 300 s, then open B. B's Begin must sweep A, so a
                query against A must FAIL while B still works.
  * `protect` — open C, idle it past 300 s, then REFINE C. C must survive, because a
                Refine protects its own session before sweeping others. If the
                ordering were wrong, C would reap itself and the refine would fail.

⚠ A query against a reaped session must FAIL LOUDLY for this to mean anything. If the
DLL answered a dead session id with an empty-but-ok reply, "0 candidates" would be
indistinguishable from "reaped", so the rig checks `ok`/`error`, not the row count.
"""
import sys
import pathlib
import time

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
from pipe_client import PipeClient                        # noqa: E402

IDLE = 300          # kScanSessionIdleExpiry
WAIT = IDLE + 20    # comfortably past it


def begin(c, tag):
    r = c.request("begin_value_scan", data_type="Int32", scan_type="Exact", value="1",
                  game_only=True, max_results=20000, deadline_ms=15000)
    if not r.get("ok"):
        raise SystemExit(f"ab17: begin ({tag}) failed: {r}")
    print(f"  {tag}: session {r['session_id']}  total={r.get('total')}")
    return r["session_id"]


def probe(c, sid, tag):
    """True if the session still answers."""
    r = c.request("query_candidates", session_id=sid, offset=0, limit=1)
    alive = bool(r.get("ok")) and not r.get("error")
    print(f"  probe {tag} (session {sid}): ok={r.get('ok')} err={str(r.get('error'))[:60]!r}"
          f"  -> {'ALIVE' if alive else 'GONE'}")
    return alive


def phase_reap():
    print(f"=== AB17 phase 1: an idle session must be reaped by a later Begin "
          f"(idle expiry {IDLE}s) ===")
    with PipeClient(timeout=900.0) as c:
        c.assert_build()
        a = begin(c, "A")
        print(f"  idling {WAIT}s ...")
        time.sleep(WAIT)
        b = begin(c, "B")           # this Begin should sweep A
        a_alive = probe(c, a, "A (idled past expiry)")
        b_alive = probe(c, b, "B (just created)")
        c.request("end_value_scan", session_id=b)
        ok = (not a_alive) and b_alive
        print(f"\nphase 1: {'PASS' if ok else 'FAIL'} "
              f"(A must be GONE, B must be ALIVE)")
        return 0 if ok else 1


def phase_protect():
    print(f"=== AB17 phase 2: Refine must PROTECT its own session (idle {IDLE}s) ===")
    with PipeClient(timeout=900.0) as c:
        c.assert_build()
        s = begin(c, "C")
        print(f"  idling {WAIT}s ...")
        time.sleep(WAIT)
        r = c.request("refine_value_scan", session_id=s, scan_type="Exact", value="1")
        print(f"  refine C: ok={r.get('ok')} remaining={r.get('total')} "
              f"err={str(r.get('error'))[:60]!r}")
        alive = probe(c, s, "C (refined at expiry)")
        if alive:
            c.request("end_value_scan", session_id=s)
        ok = bool(r.get("ok")) and alive
        print(f"\nphase 2: {'PASS' if ok else 'FAIL'} "
              f"(C must survive its own Refine)")
        return 0 if ok else 1


if __name__ == "__main__":
    what = sys.argv[1] if len(sys.argv) > 1 else "reap"
    sys.exit(phase_reap() if what == "reap" else phase_protect())
