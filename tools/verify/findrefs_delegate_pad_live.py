r"""[DELEGATE-PAD-ALIGN] Find References sees a delegate binding past a checked build's access detector -- live.

    py tools/verify/findrefs_delegate_pad_live.py --label dev-green
    py tools/verify/findrefs_delegate_pad_live.py --label dev-red --any-build    # a DLL other than dist's

Runs against the injected DumperTest (one game at a time, never while the UI holds the pipe). Exits 1 while the
defect stands, 2 when the run could not set itself up.

THE SUBJECT. Every delegate on DumperTestActor is bound to the actor itself, and Find References suppresses
owner == target, so no binding the fixture makes can show the defect. The rig lends the bindings to the class
default object instead, for the length of one Find References call: it writes the actor's weak pair over the CDO's
Del_Unicast payload, and copies the actor's Multicast_Inline and Arr_Delegates TArray headers over the CDO's, so
the CDO's lists point at the actor's own invocation-list storage. All three are restored in a `finally`. Nothing
fires a CDO's delegates, so the borrowed bindings are never invoked.

THE CHECKS. Find References on the actor must then list the CDO three times: Arr_Delegates -- the CONTROL, a
TArray<FScriptDelegate> whose per-element pad Find Refs has read since D4b -- and Del_Unicast and Multicast_Inline,
the starred two, which a DLL reading the access detector at the field offset drops under a complete scan. The
CDO's original bytes must read back afterwards. The output is written to out/findrefs_delegate_pad/<label>.json.
"""
import argparse
import json
import pathlib
import sys

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
from pipe_client import PipeClient, PipeError  # noqa: E402

ROOT = pathlib.Path(__file__).resolve().parents[2]
OUT_DIR = ROOT / "out" / "findrefs_delegate_pad"
ACTOR = "DumperTestActor"
CONTROL, UNICAST, MULTICAST = "Arr_Delegates", "Del_Unicast", "Multicast_Inline"


def say(s):
    print(s, flush=True)


def read_bytes(c, addr, n):
    r = c.request("read_mem", addr="0x%X" % addr, size=n)
    hexs = (r.get("bytes") or r.get("hex") or "").replace(" ", "").replace("0x", "")
    return bytes.fromhex(hexs) if hexs else None


def write_bytes(c, addr, data):
    r = c.request("write_mem", addr="0x%X" % addr, bytes=data.hex().upper())
    if r.get("ok") is False or "error" in r:
        raise PipeError("write_mem at 0x%X refused: %s" % (addr, r.get("error")))


def walk(c, inst):
    w = c.request("walk_instance", addr=inst["addr"], class_addr=inst["class_addr"], array_limit=8)
    return {f.get("name"): f for f in w.get("fields", [])}


def run(c, out, checks):
    found = c.request("find_instances", class_name=ACTOR, exact_match=True, max_results=10, limit=10)
    rows = found.get("instances") or []
    actor = next((i for i in rows if not str(i.get("name", "")).startswith("Default__")), None)
    cdo = next((i for i in rows if str(i.get("name", "")).startswith("Default__")), None)
    if not actor or not cdo:
        say("BLOCKED: need a live %s and its class default object (is DumperTest in its map?)" % ACTOR)
        return 2
    act, dft = int(actor["addr"], 16), int(cdo["addr"], 16)
    fa = walk(c, actor)
    uni, mc, arr = fa.get(UNICAST) or {}, fa.get(MULTICAST) or {}, fa.get(CONTROL) or {}
    if not uni or not mc or not arr or "::" not in str(uni.get("value", "")):
        say("BLOCKED: the actor's walk lacks a bound %s / %s / %s" % (UNICAST, MULTICAST, CONTROL))
        return 2
    upad, mpad = int(uni.get("delegate_pad", 0) or 0), int(mc.get("delegate_pad", 0) or 0)
    pair = read_bytes(c, act + int(uni["offset"]) + upad, 8)
    # (where on the CDO, what to write there): the actor's own pair, and the actor's two TArray headers.
    lend = {
        UNICAST: (dft + int(uni["offset"]) + upad, pair),
        MULTICAST: (dft + int(mc["offset"]) + mpad, read_bytes(c, act + int(mc["offset"]) + mpad, 16)),
        CONTROL: (dft + int(arr["offset"]), read_bytes(c, act + int(arr["offset"]), 16)),
    }
    out["actor"] = {"name": actor.get("name"), "addr": actor["addr"], "pair": pair.hex().upper() if pair else None}
    out["cdo"] = {"name": cdo.get("name"), "addr": cdo["addr"]}
    out["pads"] = {UNICAST: upad, MULTICAST: mpad, "sizes": [uni.get("size"), mc.get("size")]}
    if any(not b or len(b) not in (8, 16) for _, b in lend.values()):
        say("BLOCKED: could not read the actor's bindings: %s" % {n: (b.hex() if b else None) for n, (_, b) in lend.items()})
        return 2
    say("actor %s pair %s; %s pad %d (ElementSize %s), %s pad %d (ElementSize %s)"
        % (actor.get("name"), pair.hex().upper(), UNICAST, upad, uni.get("size"), MULTICAST, mpad, mc.get("size")))

    saved = {}
    try:
        for n, (addr, data) in lend.items():
            saved[n] = read_bytes(c, addr, len(data))
            if not saved[n] or len(saved[n]) != len(data):
                say("BLOCKED: could not read the CDO's %s at 0x%X" % (n, addr))
                return 2
            write_bytes(c, addr, data)
        refs = c.request("find_refs_to_uobject", addr=actor["addr"], max_results=4096)
    finally:
        for n, b in saved.items():
            if b:
                write_bytes(c, lend[n][0], b)
        out["restored"] = {n: (read_bytes(c, lend[n][0], len(b)) == b) for n, b in saved.items()}
    hits = [r for r in (refs.get("references") or refs.get("refs") or []) if r.get("owner_addr") == cdo["addr"]]
    names = [str(r.get("field_name", "")) for r in hits]
    out["scan"], out["cdo_rows"] = refs.get("scan"), names
    say("Find References on the actor, the CDO's rows: %s; scan %s" % (names, refs.get("scan")))
    checks("the CDO's original bytes read back", all(out["restored"].values()), str(out["restored"]))
    if not checks("control: the TArray<FScriptDelegate> binding is found (pad-aware since D4b)",
                  any(x.startswith(CONTROL) for x in names), str(names)):
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
        rc = run(c, out, checks)
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
