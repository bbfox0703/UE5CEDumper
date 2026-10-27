r"""[DELEGATE-PAD-ALIGN] Find References sees a delegate binding past a checked build's access detector -- live.

    py tools/verify/findrefs_delegate_pad_live.py --label dev-green
    py tools/verify/findrefs_delegate_pad_live.py --label dev-red --any-build    # a DLL other than dist's

Runs against the injected DumperTest (one game at a time, never while the UI holds the pipe). Exits 1 while the
defect stands, 2 when the run could not set itself up.

THE SUBJECT. Every delegate on DumperTestActor is bound to the actor itself, and Find References suppresses
owner == target, so no binding the fixture makes can show the defect. The one other bound object is
UDumperTestSparseListener, which BeginPlay subscribes to the SPARSE OnActorBeginOverlap -- the pass that was already
pad-aware, so it is the CONTROL. The rig points Del_Unicast and Multicast_Inline's first binding at the listener for
the length of one Find References call, by writing the listener's weak pair over theirs, and restores both in a
`finally`. Nothing in the fixture fires either delegate, so the borrowed binding is never invoked.

THE CHECKS. The listener's pair is the one the sparse binding already holds (the walk shows it). Find References on
the listener must then list the actor three times: OnActorBeginOverlap (the control), Del_Unicast and
Multicast_Inline -- the starred two, which a DLL reading the detector at +0 drops under a complete scan. The original
bytes must read back afterwards. The output is written to out/findrefs_delegate_pad/<label>.json.
"""
import argparse
import json
import pathlib
import sys

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
from pipe_client import PipeClient, PipeError  # noqa: E402

ROOT = pathlib.Path(__file__).resolve().parents[2]
OUT_DIR = ROOT / "out" / "findrefs_delegate_pad"
ACTOR, LISTENER = "DumperTestActor", "DumperTestSparseListener"
SPARSE, UNICAST, MULTICAST = "OnActorBeginOverlap", "Del_Unicast", "Multicast_Inline"


def say(s):
    print(s, flush=True)


def instances(c, cls):
    r = c.request("find_instances", class_name=cls, exact_match=True, max_results=10, limit=10)
    return [i for i in (r.get("instances") or []) if not str(i.get("name", "")).startswith("Default__")]


def read_bytes(c, addr, n):
    r = c.request("read_mem", addr="0x%X" % addr, size=n)
    hexs = (r.get("bytes") or r.get("hex") or "").replace(" ", "").replace("0x", "")
    return bytes.fromhex(hexs) if hexs else None


def write_bytes(c, addr, data):
    r = c.request("write_mem", addr="0x%X" % addr, bytes=data.hex().upper())
    if r.get("ok") is False or "error" in r:
        raise PipeError("write_mem at 0x%X refused: %s" % (addr, r.get("error")))


def run(c, a, out, checks):
    actors, listeners = instances(c, ACTOR), instances(c, LISTENER)
    if not actors or not listeners:
        say("BLOCKED: need a live %s and its %s (is DumperTest in its map?)" % (ACTOR, LISTENER))
        return 2
    actor, listener = actors[0], listeners[0]
    act = int(actor["addr"], 16)
    w = c.request("walk_instance", addr=actor["addr"], class_addr=actor["class_addr"], array_limit=8)
    fields = {f.get("name"): f for f in w.get("fields", [])}
    sp, uni, mc = fields.get(SPARSE) or {}, fields.get(UNICAST) or {}, fields.get(MULTICAST) or {}
    elems = sp.get("elements") or []
    pair_hex = str(elems[0].get("h", ""))[:16] if elems else ""
    out["listener"] = {"name": listener.get("name"), "addr": listener["addr"], "index": listener.get("index"),
                       "pair": pair_hex, "sparse_value": sp.get("value")}
    if len(pair_hex) != 16 or listener.get("name", "") not in str(sp.get("value", "")):
        say("BLOCKED: the sparse binding does not show the listener's pair: %s" % json.dumps(sp)[:300])
        return 2
    pair = bytes.fromhex(pair_hex)
    upad, mpad = int(uni.get("delegate_pad", 0) or 0), int(mc.get("delegate_pad", 0) or 0)
    data = int(str(mc.get("array_data_addr", "0")), 16)
    if not uni or not mc or not data:
        say("BLOCKED: the walk lacks %s / %s's invocation list" % (UNICAST, MULTICAST))
        return 2
    sites = {UNICAST: act + int(uni["offset"]) + upad, MULTICAST: data}
    out["fields"] = {UNICAST: {"size": uni.get("size"), "delegate_pad": upad},
                     MULTICAST: {"size": mc.get("size"), "delegate_pad": mpad, "data": "0x%X" % data}}
    say("listener %s pair %s; %s pad %d, %s pad %d" % (listener.get("name"), pair_hex, UNICAST, upad, MULTICAST, mpad))

    saved = {}
    try:
        for n, addr in sites.items():
            saved[n] = read_bytes(c, addr, 8)
            if not saved[n] or len(saved[n]) != 8:
                say("BLOCKED: could not read %s's binding at 0x%X" % (n, addr))
                return 2
            write_bytes(c, addr, pair)
        refs = c.request("find_refs_to_uobject", addr=listener["addr"], max_results=256)
    finally:
        for n, b in saved.items():
            if b:
                write_bytes(c, sites[n], b)
        out["restored"] = {n: (read_bytes(c, sites[n], 8) == b) for n, b in saved.items()}
    rows = [r for r in (refs.get("refs") or refs.get("results") or refs.get("references") or [])
            if r.get("owner_addr") == actor["addr"]]
    names = [str(r.get("field_name", "")) for r in rows]
    out["scan"], out["actor_rows"] = refs.get("scan"), names
    say("Find References on the listener, the actor's rows: %s; scan %s" % (names, refs.get("scan")))
    checks("the original bindings read back", all(out["restored"].values()), str(out["restored"]))
    if not checks("control: the sparse binding is found (already pad-aware)", any(x.startswith(SPARSE) for x in names),
                  str(names)):
        return 2
    checks("Find References finds the DelegateProperty binding (%s, pad %d)" % (UNICAST, upad),
           any(x.startswith(UNICAST) for x in names), str(names), starred=True)
    checks("Find References finds the multicast binding (%s, pad %d)" % (MULTICAST, mpad),
           any(x.startswith(MULTICAST) for x in names), str(names), starred=True)
    return 0


class Checks:
    def __init__(self):
        self.items = []

    def __call__(self, name, cond, got="", starred=False):
        self.items.append({"name": name, "ok": bool(cond), "got": got, "starred": starred})
        say("  %s  %s%s   %s" % ("PASS" if cond else "FAIL", "* " if starred else "  ", name, got))
        return bool(cond)


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--label", required=True, help="names the output file")
    ap.add_argument("--any-build", action="store_true", help="do not require dist's build number")
    a = ap.parse_args(argv)
    checks, out = Checks(), {"label": a.label}
    try:
        c = PipeClient().connect()
    except PipeError as e:
        say("pipe not usable: %s" % e)
        return 2
    try:
        out["build"] = str(c.request("get_pointers").get("build_number"))
        if not a.any_build:
            c.assert_build()
        c.ensure_scanned()
        rc = run(c, a, out, checks)
    except (PipeError, KeyError, ValueError) as e:
        checks("the run reached its end", False, "%s: %s" % (type(e).__name__, e))
        rc = 2
    finally:
        c.close()
    out["checks"] = checks.items
    OUT_DIR.mkdir(parents=True, exist_ok=True)
    path = OUT_DIR / ("%s.json" % a.label)
    path.write_text(json.dumps(out, indent=1, default=str), encoding="utf-8")
    failed = [i for i in checks.items if not i["ok"]]
    say("\n%d/%d checks hold; written to %s" % (len(checks.items) - len(failed), len(checks.items), path))
    if any(i["starred"] for i in failed):
        return 1
    return rc if rc else (1 if failed else 0)


if __name__ == "__main__":
    sys.exit(main())
