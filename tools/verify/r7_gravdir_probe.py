"""Does this engine's CharacterMovementComponent READ GravityDirection? Flip it and watch the pawn.

    py tools/verify/r7_gravdir_probe.py --out out/r7live/x7/ue53
    py tools/verify/r7_gravdir_probe.py --out out/r7live/x7/ue54 --x 0 --y 0 --z 1

For the Review 7 skeptic's X4-adjacent note: the Gravity Direction card (Laufen) gates on the reflected
`GravityDirection` property and its text says "UE5.4+", but stock UE 5.3 reflects that property too (R7-X4). Whether
a 5.3 CMC ever reads it decides whether the card works there or reports a success that changes nothing.

It reads `get_movement_params` (the gravity_direction block) and the pawn's location (`teleport_get_pose`), sets the
direction with `set_gravity_direction`, samples the location every --gap seconds for --seconds, resets it with
`reset_gravity_direction`, and samples again. A honoured (0,0,1) sends a grounded pawn UP (Z rises by metres within a
second); an ignored one leaves Z where it was. Writes only the one field, and resets it. Needs the DLL injected.
"""
import argparse
import json
import os
import sys
import time

sys.stdout.reconfigure(encoding="utf-8", errors="replace")
HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
from pipe_client import PipeClient  # noqa: E402


def data(r):
    return r.get("data") or r


def main():
    ap = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    ap.add_argument("--out", required=True)
    ap.add_argument("--x", type=float, default=0.0)
    ap.add_argument("--y", type=float, default=0.0)
    ap.add_argument("--z", type=float, default=1.0)
    ap.add_argument("--seconds", type=float, default=4.0)
    ap.add_argument("--gap", type=float, default=0.5)
    a = ap.parse_args()
    os.makedirs(a.out, exist_ok=True)
    lines = []

    def say(s=""):
        print(s, flush=True)
        lines.append(s)

    with PipeClient(timeout=120) as c:
        p = data(c.request("get_pointers"))
        say(f"# {time.strftime('%Y-%m-%d %H:%M:%S')} build {p.get('build_number')} ue {p.get('ue_version')} "
            f"objects {p.get('object_count')}")
        mp = data(c.request("get_movement_params"))
        say("movement gravity_direction: " + json.dumps(mp.get("gravity_direction")))

        def pose(tag):
            d = data(c.request("teleport_get_pose"))
            loc = d.get("location") or {k: d.get(k) for k in ("x", "y", "z")}
            say(f"  {tag:14} loc {json.dumps(loc)}  pawn {d.get('pawn_addr') or d.get('pawn')}")
            return loc

        def sample(label):
            t0 = time.time()
            while time.time() - t0 < a.seconds:
                pose(f"{label} +{time.time() - t0:4.1f}s")
                time.sleep(a.gap)

        pose("before")
        r = c.request("set_gravity_direction", x=a.x, y=a.y, z=a.z)
        say("set_gravity_direction -> " + json.dumps(data(r))[:300])
        sample("flipped")
        r = c.request("reset_gravity_direction")
        say("reset_gravity_direction -> " + json.dumps(data(r))[:300])
        sample("reset")
        mp = data(c.request("get_movement_params"))
        say("movement gravity_direction after: " + json.dumps(mp.get("gravity_direction")))
    open(os.path.join(a.out, "gravdir_probe.txt"), "w", encoding="utf-8").write("\n".join(lines) + "\n")
    return 0


if __name__ == "__main__":
    sys.exit(main())
