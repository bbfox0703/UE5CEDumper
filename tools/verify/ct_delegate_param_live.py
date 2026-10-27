r"""[CT-DELEGATE-PARAM-PAD] Call Trace names a delegate parameter's bound object, past a checked build's detector.

    py tools/verify/ct_delegate_param_live.py --label dev-green
    py tools/verify/ct_delegate_param_live.py --label dev-red --any-build     # a DLL other than dist's (the red run)

Runs against the injected DumperTest (one game at a time, never while the UI holds the pipe). Exits 1 while the
defect stands, so it doubles as the regression check; 2 when the run could not set itself up.

THE CALL. DumperTestActor::InvokeGate_TakeDelegate takes FDumperTestUnicastSignature BY VALUE, and nothing in the
fixture calls it. A queued invoke runs through the trampoline, so the trace never sees it. A DIRECT invoke on an actor
calls AActor::ProcessEvent (the class's own override, which MinHook did not patch), and its Super::ProcessEvent is the
hooked entry, so that call is traced as the game's own are. Its Callback is the actor's own Del_Unicast copied byte for
byte (on a UE 5.3+ checked build: the access detector, then the weak pair and the FName), so the bound object is the
actor. A plain recording first gives the function's name key; a snapshots-only recording then copies the call.

THE WITNESSES, none of them the decoder under test:
  * the walk of Del_Unicast (walk_instance places it past the pad itself): its text, and its pair bytes, which must
    equal the actor's memory at the field + delegate_pad;
  * the slot's own bytes: the pair at Callback's offset + the pad equals that pair;
  * the engine: InvokeGate_LastDelegateBound reads 1 after the calls (IsBound() on the copy it received), and
    InvokeGate_DelegateCalls went up by two.
The starred check: the slot's decoded Callback is the walk's text, marked "now". The output, with the slot's bytes, is
written to out/ct_delegate_param/<label>.json.
"""
import argparse
import base64
import hashlib
import json
import pathlib
import sys
import time

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
from pipe_client import PipeClient, PipeError  # noqa: E402

ROOT = pathlib.Path(__file__).resolve().parents[2]
OUT_DIR = ROOT / "out" / "ct_delegate_param"
FIXTURE_CLASS = "DumperTestActor"
FUNC = "InvokeGate_TakeDelegate"
FIELD = "Del_Unicast"
MARK_NOW = 1   # Ubel::SnapMark::Now


def say(s):
    enc = sys.stdout.encoding or "utf-8"
    sys.stdout.write(str(s).encode(enc, "replace").decode(enc, "replace") + "\n")
    sys.stdout.flush()


def data_of(reply):
    return reply.get("data", reply)


class Checks:
    def __init__(self):
        self.items = []

    def __call__(self, name, cond, got="", starred=False):
        self.items.append({"name": name, "ok": bool(cond), "got": got, "starred": starred})
        say("  %s  %s%s   %s" % ("PASS" if cond else "FAIL", "* " if starred else "  ", name, got))
        return bool(cond)

    @property
    def failed(self):
        return sum(1 for i in self.items if not i["ok"])


def read_bytes(c, addr, n):
    r = c.request("read_mem", addr="0x%X" % addr, size=n)
    hexs = (r.get("bytes") or r.get("hex") or "").replace(" ", "").replace("0x", "")
    return bytes.fromhex(hexs) if hexs else None


def walk_fields(c, addr):
    return {f["name"]: f for f in (c.request("walk_instance", addr=addr).get("fields") or [])}


def int_value(field):
    try:
        return int(str(field.get("value", "")).split()[0])
    except (ValueError, IndexError):
        return None


def invoke_direct(c, actor_addr, fn, cb, payload):
    buf = bytearray(max(fn.get("parms_size", 0), cb["offset"] + len(payload)))
    buf[cb["offset"]:cb["offset"] + len(payload)] = payload
    return c.request("invoke_function", class_name=FIXTURE_CLASS, func_name=FUNC, instance_addr=actor_addr,
                     params_hex=buf.hex().upper(), parms_size=len(buf), direct_call=True)


def run(c, check, out, a):
    actors = [i for i in (c.request("find_instances", class_name=FIXTURE_CLASS, exact_match=True, max_results=50,
                                    limit=50).get("instances") or [])
              if not str(i.get("name", "")).startswith("Default__")]
    if not actors:
        say("BLOCKED: no live DumperTestActor -- is DumperTest in its map?")
        return 2
    actor = actors[0]
    act = int(actor["addr"], 16)
    fields = walk_fields(c, actor["addr"])
    dl = fields.get(FIELD)
    if not dl or "::" not in str(dl.get("value", "")):
        say("BLOCKED: the walk names no bound object for %s: %s" % (FIELD, dl))
        return 2
    pad, size = int(dl.get("delegate_pad", 0)), int(dl["size"])
    raw = read_bytes(c, act + int(dl["offset"]), size)
    out["actor"] = {"name": actor.get("name"), "addr": actor["addr"], "index": actor.get("index")}
    out["field"] = {"size": size, "delegate_pad": pad, "value": dl.get("value"), "hex": dl.get("hex"),
                    "memory": raw.hex().upper() if raw else None}
    say("%s at %s: %s, ElementSize %d, delegate_pad %d" % (FIELD, actor["addr"], dl.get("value"), size, pad))
    say("  memory %s" % (raw.hex(" ").upper() if raw else None))
    pair = raw[pad:pad + 8] if raw and len(raw) == size else b""
    if not check("the walk's pair is the actor's memory at the field + delegate_pad",
                 pair and str(dl.get("hex", "")).upper().startswith(pair.hex().upper()),
                 "memory %s, walk hex %s" % (pair.hex().upper(), dl.get("hex"))):
        return 1

    fl = {f["name"]: f for f in (c.request("walk_functions", addr=actor["class_addr"]).get("functions") or [])}
    fn = fl.get(FUNC)
    cb = next((p for p in (fn or {}).get("params", []) if p.get("type") == "DelegateProperty"), None)
    if not cb:
        say("BLOCKED: %s::%s has no DelegateProperty parameter (a package without the invoke gate?)" % (FIXTURE_CLASS, FUNC))
        return 2
    out["param"] = {"name": cb.get("name"), "offset": cb["offset"], "size": cb["size"],
                    "parms_size": fn.get("parms_size")}
    check("the parameter is the field's type, at its size", cb["size"] == size,
          "%s +0x%X, %d bytes" % (cb.get("name"), cb["offset"], cb["size"]))
    calls_before = int_value(fields.get("InvokeGate_DelegateCalls", {}))

    # The name key: a plain recording that holds the call (and shows the direct invoke reached the hook).
    plain = c.request("pe_profile_start")
    if "error" in plain:
        say("BLOCKED: pe_profile_start refused: %s" % plain.get("error"))
        return 2
    try:
        r1 = invoke_direct(c, actor["addr"], fn, cb, raw)
        time.sleep(a.settle_s)
    finally:
        c.request("pe_profile_stop")
    table = data_of(c.request("pe_profile_get", limit=32768, include_unloaded=True))
    row = next((f for f in table.get("functions", []) if f.get("class_name") == FIXTURE_CLASS
                and f.get("func_name") == FUNC and isinstance(f.get("fname_key"), list)), None)
    if not check("the direct invoke went through the hooked ProcessEvent", row is not None and row.get("count", 0) >= 1,
                 "result %s; row %s" % (r1.get("result"), row and {"count": row.get("count"), "key": row["fname_key"]})):
        return 1

    item = {"class": row["class_name"], "func": row["func_name"], "keys": [row["fname_key"]],
            "parms_size": row.get("parms_size", 0)}
    start = c.request("pe_profile_start", trace={"bytes": 32 << 20, "snapshots": {"funcs": [item], "bytes": 8 << 20}})
    if not check("the snapshots-only Start is accepted", "error" not in start, str(start.get("error", ""))[:120]):
        return 1
    try:
        r2 = invoke_direct(c, actor["addr"], fn, cb, raw)
        time.sleep(a.settle_s)
    finally:
        stop = data_of(c.request("pe_profile_stop"))
    gen = stop.get("trace", {}).get("gen", 0)
    lay = data_of(c.request("pe_snap_layouts", gen=gen, offset=0))
    arms = [x for x in lay.get("arms", []) if x.get("func_name") == FUNC]
    layouts = lay.get("layouts", [])
    slots = []
    for ring in sorted({x["ring"] for x in arms}):
        slots += data_of(c.request("pe_snap_get", gen=gen, ring=ring, **{"from": 0, "max": 64})).get("items", [])
    entry = next((s for s in slots if s.get("phase") == "entry" and s.get("values")), None)
    out["invoke"] = {"plain": r1.get("result"), "traced": r2.get("result")}
    if not check("the traced call left a decoded entry slot", entry is not None,
                 "%d arm(s), %d slot(s), invoke result %s" % (len(arms), len(slots), r2.get("result"))):
        return 1
    arm = next(x for x in arms if x.get("index") == entry.get("arm"))
    params = layouts[arm["layout"]].get("params", []) if "layout" in arm and arm["layout"] < len(layouts) else []
    k = next((i for i, p in enumerate(params) if p.get("name") == cb.get("name")), None)
    if not check("the arm's layout holds the parameter, at the walk's size", k is not None
                 and params[k].get("size") == size, json.dumps(params)[:200]):
        return 1
    text, mark = entry["values"][k][0], entry["values"][k][1]
    data = base64.b64decode(entry.get("data", ""))
    p_off = int(params[k]["offset"])
    slot_pair = data[p_off + pad:p_off + pad + 8]
    out["slot"] = {"value": text, "mark": mark, "bytes": data.hex().upper(), "param_offset": p_off}
    say("  slot bytes at Callback: %s" % data[p_off:p_off + size].hex(" ").upper())
    check("the slot's pair, at Callback + delegate_pad, is the actor's", slot_pair == pair,
          "slot %s, actor %s" % (slot_pair.hex().upper(), pair.hex().upper()))
    check("Call Trace decodes Callback as the walk names Del_Unicast, marked now",
          text == dl.get("value") and mark == MARK_NOW,
          "decoded %r mark %s, walk %r" % (text, mark, dl.get("value")), starred=True)

    after = walk_fields(c, actor["addr"])
    bound = int_value(after.get("InvokeGate_LastDelegateBound", {}))
    calls_after = int_value(after.get("InvokeGate_DelegateCalls", {}))
    out["engine"] = {"last_bound": bound, "calls_before": calls_before, "calls_after": calls_after}
    check("the engine received a bound delegate (InvokeGate_LastDelegateBound)", bound == 1, str(bound))
    check("...and was called twice", calls_before is not None and calls_after == calls_before + 2,
          "%s -> %s" % (calls_before, calls_after))
    return 0


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--label", required=True, help="names the output file")
    ap.add_argument("--any-build", action="store_true", help="do not require dist's build number")
    ap.add_argument("--dll", type=pathlib.Path, help="the injected DLL, recorded by its SHA-256")
    ap.add_argument("--settle-s", type=float, default=0.5)
    a = ap.parse_args(argv)

    check = Checks()
    out = {"label": a.label}
    if a.dll:
        out["dll"] = {"path": a.dll.name, "sha256": hashlib.sha256(a.dll.read_bytes()).hexdigest()}
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
        say("DLL build %s%s" % (out["build"], " sha256 %s" % out["dll"]["sha256"][:12] if a.dll else ""))
        rc = run(c, check, out, a)
    except PipeError as e:
        check("the run reached its end", False, str(e))
        rc = 1
    finally:
        try:
            c.request("pe_profile_stop")
            c.request("pe_trace_release")
        except PipeError:
            pass
        c.close()
    out["checks"] = check.items
    OUT_DIR.mkdir(parents=True, exist_ok=True)
    path = OUT_DIR / ("%s.json" % a.label)
    path.write_text(json.dumps(out, indent=1, default=str), encoding="utf-8")
    say("\n%d/%d checks hold; written to %s" % (len(check.items) - check.failed, len(check.items), path))
    return rc if rc else (1 if check.failed else 0)


if __name__ == "__main__":
    sys.exit(main())
