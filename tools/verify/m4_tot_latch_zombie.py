"""M4 - a client disconnect must not ZOMBIFY a Solide force-field hold.

    py tools/verify/m4_tot_latch_zombie.py

THE ROW's acceptance, verbatim: "start a force-field hold, disconnect the UI mid-hold,
reconnect -> `get_forced_fields` must still list the hold AND the value must still be held
(a zombie job lists but stops re-asserting, so checking the list alone is not enough)".

WHY THE LIST IS NOT ENOUGH, restated because it is the entire point of the rig: `Tot` is the
cancellation latch. A disconnect sets it so long-running scans abandon promptly. A hold's
re-assert WORKER must be exempt (`Solitar.cpp`/`Solide` mark themselves via
`Tot::MarkBackgroundWorker`), otherwise the disconnect silently stops the worker while the
JOB stays in the table - so `get_forced_fields` still lists it and the value quietly drifts.
That is the zombie. Listing proves bookkeeping; only a poke proves behaviour.

THE DISCONNECT IS ABRUPT ON PURPOSE. A clean shutdown is the path that already works; the
latch is set by the disconnect MONITOR noticing a dropped socket, so the rig closes the
underlying handle rather than asking politely.

⭐ THE DETECTOR IS ESTABLISHED BEFORE THE DISCONNECT. The rig pokes the held value while the
hold is known-good and requires it to be restored. Without that, "the value came back after
reconnect" is equally consistent with "nothing ever changed it", which is the vacuous
version of this test.
"""
from __future__ import annotations

import argparse
import pathlib
import sys
import time

HERE = pathlib.Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
from pipe_client import PipeClient           # noqa: E402
from ad4_contested import find_live_actor, invoke   # noqa: E402

CLS = "DumperTestHolder"
FIELD = "HolderValue"
FORCED = 4242.0


def i32(n):
    return int(n).to_bytes(4, "little", signed=True).hex()


def live(c):
    r = c.request("find_instances", class_name=CLS, limit=500, exact_match=True)
    return [i for i in r.get("instances", [])
            if i.get("class") == CLS and not i["name"].startswith("Default__")]


def field_of(c, addr):
    w = c.request("walk_instance", addr=addr, array_limit=1)
    for f in w.get("fields", []):
        if f.get("name") == FIELD:
            return f.get("value"), f.get("offset")
    return None, None


def held_frac(c, rows, n=8):
    got = [field_of(c, r["addr"])[0] for r in rows[:n]]
    hit = sum(1 for v in got if v is not None and abs(float(v) - FORCED) < 0.01)
    return hit, len(got)


def poke_and_watch(c, addr, off, secs=5.0):
    """Write a wrong value into the held field; report whether the worker restores it."""
    bad = (-1.0)
    import struct
    c.request("write_mem", addr="0x%X" % (int(addr, 16) + off),
              bytes=struct.pack("<f", bad).hex())
    t0 = time.time()
    while time.time() - t0 < secs:
        time.sleep(0.4)
        v, _ = field_of(c, addr)
        if v is not None and abs(float(v) - FORCED) < 0.01:
            return True, time.time() - t0
    return False, time.time() - t0


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--holders", type=int, default=60)
    a = ap.parse_args(argv)
    fails = []

    # ---------- lane A: set up the hold, then die abruptly ----------
    c = PipeClient(); c.__enter__()
    c.assert_build(); c.ensure_scanned()
    act = find_live_actor(c)
    fn = {f["name"]: f for f in c.request("walk_functions", addr=act["class_addr"])["functions"]}
    invoke(c, act["addr"], "Spawn_Holders",
           parms_size=fn["Spawn_Holders"]["parms_size"], params_hex=i32(a.holders) + "00")
    time.sleep(1.5)
    rows = live(c)
    print("[0] %d live %s" % (len(rows), CLS))
    if not rows:
        raise SystemExit("m4: no holders spawned")

    r = c.request("force_field", class_name=CLS, field_name=FIELD, kind="numeric",
                  on=True, value=FORCED)
    time.sleep(2.0)
    hit, tot = held_frac(c, rows)
    print("[1] force_field -> held=%s truncated=%s ; %d/%d sampled read %s"
          % (r.get("held"), r.get("truncated"), hit, tot, FORCED))
    if hit != tot or tot == 0:
        fails.append("1: the hold is not applying before the disconnect, so nothing below "
                     "measures the disconnect")

    addr = rows[0]["addr"]
    _, off = field_of(c, addr)
    ok, dt = poke_and_watch(c, addr, off)
    print("[2] DETECTOR (hold healthy): poked -1 -> restored=%s after %.1fs" % (ok, dt))
    if not ok:
        fails.append("2: the poke was not restored while the hold is healthy, so the detector "
                     "does not work and a later 'restored' would prove nothing")

    print("")
    print("[3] killing the socket abruptly (this is what sets the Tot latch)")
    # PipeClient holds the named-pipe handle as `_f` (opened "r+b"). Closing it directly is
    # the abrupt drop the disconnect monitor must notice; PipeClient.__exit__ would be a
    # clean teardown, which is the path that already works.
    c._f.close()                 # noqa: SLF001 - deliberate
    time.sleep(4.0)

    # ---------- lane B: reconnect and check BOTH halves ----------
    with PipeClient() as c2:
        c2.assert_build()
        forced = c2.request("get_forced_fields")
        jobs = forced.get("fields") or forced.get("forced") or forced.get("jobs") or []
        names = [(j.get("class_name"), j.get("field_name"), j.get("held")) for j in jobs]
        print("[4] after reconnect, get_forced_fields lists: %s" % (names or "NOTHING"))
        if not any(n[0] == CLS and n[1] == FIELD for n in names):
            fails.append("4: the hold is no longer listed after the disconnect -- the job was "
                         "dropped, not zombified")

        rows2 = live(c2)
        hit, tot = held_frac(c2, rows2)
        print("    %d/%d sampled still read %s" % (hit, tot, FORCED))
        _, off2 = field_of(c2, rows2[0]["addr"])
        ok2, dt2 = poke_and_watch(c2, rows2[0]["addr"], off2)
        print("[5] ZOMBIE CHECK: poked -1 after reconnect -> restored=%s after %.1fs" % (ok2, dt2))
        if not ok2:
            fails.append("5: the hold is LISTED but no longer re-asserting -- this is exactly "
                         "the zombie M4 is about (the Tot latch stopped the worker)")

        c2.request("reset_all_fields")

    print("")
    print("=" * 72)
    print("M4 Tot latch vs a Solide hold: %s" % ("PASS" if not fails else "FAIL"))
    for f in fails:
        print("   - %s" % f)
    return 1 if fails else 0


if __name__ == "__main__":
    sys.exit(main())
