"""Live pipe checks for the DLL side of the second half of the 2026-11-02 census pass (build 3672 on).

    py tools/verify/census_pass2_pipe_checks.py --pad 0      # DumperTest Shipping: no access detector
    py tools/verify/census_pass2_pipe_checks.py --pad 8      # DumperTest Development: an 8-byte detector

Headless, through the pipe (the UI must NOT be connected). Three checks:

1. [D4B-DELEGATEPAD-2026-09-09] element bytes. A delegate row's hex holds the whole value: on a checked build (pad 8)
   the 8-byte detector, a space, then the payload; on Shipping (pad 0) the bare payload, no space. Read on
   DumperTestActor's Del_Unicast row and each element of Arr_MulticastDelegates and Arr_Delegates.
2. [SOLIDE-RELATED-STOPS] find_stealth_meter's reply carries `stops`, the related-object search's stop causes.
3. [FLY-REPORTS-WISH] fly_get_state carries `collision`, read on the pawn: with Fly + noclip on it reads 0 (or 1 when
   the noclip did not take); with Fly off again it reads 1. Fly is switched off before the rig returns.
"""
import argparse
import pathlib
import sys
import time

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
from pipe_client import PipeClient  # noqa: E402


def data(reply):
    return reply.get("data", reply)


def hex_ok(h: str, size_hint: int, pad: int) -> bool:
    """`size_hint` is the payload's byte count; the whole value is pad + payload (+ any tail on a padded build)."""
    if not h or "?" in h:
        return False
    if pad == 0:
        return " " not in h and len(h) == size_hint * 2
    return h[16:17] == " " and h.count(" ") == 1 and len(h.replace(" ", "")) >= (size_hint + pad) * 2


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--pad", type=int, choices=(0, 8), required=True, help="the build's delegate detector pad")
    ap.add_argument("--class", dest="cls", default="DumperTestActor")
    a = ap.parse_args()
    bad = 0

    with PipeClient(timeout=180) as pc:
        print("build:", pc.assert_build())
        pc.ensure_scanned()

        # 1. delegate hex
        inst = data(pc.request("find_instances", class_name=a.cls, exact_match=True, limit=8)).get("instances") or []
        # The class default object is listed too, and its delegates are never bound.
        inst = [i for i in inst if not str(i.get("name", "")).startswith("Default__")]
        if not inst:
            print(f"BAD: no live {a.cls} instance")
            return 1
        print(f"   walking {inst[0].get('name')} @ {inst[0]['addr']}")
        walk = data(pc.request("walk_instance", addr=inst[0]["addr"], array_limit=8, preview_limit=2))
        fields = {f.get("name"): f for f in walk.get("fields", [])}
        rows = []
        uni = fields.get("Del_Unicast")
        if uni:
            rows.append(("Del_Unicast", uni.get("hex", ""), 16))
        for name, payload in (("Arr_MulticastDelegates", 16), ("Arr_Delegates", 16)):
            for e in (fields.get(name) or {}).get("elements", []):
                rows.append((f"{name}[{e.get('i')}]", e.get("h", ""), payload))
        if not rows:
            print("BAD: none of Del_Unicast / Arr_MulticastDelegates / Arr_Delegates carried a hex")
            bad += 1
        for label, h, payload in rows:
            ok = hex_ok(h, payload, a.pad)
            bad += not ok
            print(f"1. {label:28} hex {h!r} -> {'OK' if ok else 'BAD'}")

        # 2. stealth stops
        r = pc.request("find_stealth_meter", max=8)
        d = data(r)
        ok2 = bool(r.get("ok")) and "stops" in d
        print(f"2. find_stealth_meter: ok={r.get('ok')} code={d.get('code')} candidates="
              f"{len(d.get('candidates') or [])} stops={d.get('stops')!r} -> {'OK' if ok2 else 'BAD'}")
        bad += not ok2

        # 3. fly collision
        before = data(pc.request("fly_get_state"))
        print(f"3. fly before: active={before.get('active')} collision={before.get('collision')}")
        try:
            pc.request("fly_set", enable=True, noclip=True)
            on = {}
            for _ in range(20):
                time.sleep(0.25)
                on = data(pc.request("fly_get_state"))
                if on.get("active") and on.get("collision") in (0, 1):
                    break
            print(f"   fly on + noclip: active={on.get('active')} noclip={on.get('noclip')} collision={on.get('collision')}")
        finally:
            pc.request("fly_set", enable=False, noclip=False)
        off = {}
        for _ in range(20):
            time.sleep(0.25)
            off = data(pc.request("fly_get_state"))
            if not off.get("active") and off.get("collision") in (0, 1):
                break
        print(f"   fly off: active={off.get('active')} collision={off.get('collision')}")
        ok3 = "collision" in before and on.get("collision") in (0, 1) and off.get("collision") == 1
        print(f"   -> {'OK' if ok3 else 'BAD'}")
        bad += not ok3

    print("OK" if not bad else f"BAD: {bad} check(s)")
    return 0 if not bad else 1


if __name__ == "__main__":
    sys.exit(main())
