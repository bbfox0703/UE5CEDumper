r"""Smoke-test the DLL against a DumperTest fixture, and diff its offset/scan verdicts against a baseline.

    py tools/verify/fixture_smoke.py shipping58 --out out\smoke_shipping58 --baseline out\baseline_582\DumperTest58-Win64-Shipping
    py tools/verify/fixture_smoke.py dev58 --class DumperTest58Actor --out out\smoke_dev58

WHY. A fixture that is REPACKAGED (an engine hotfix, a source change) is a new binary: its pe_hash,
its addresses and potentially its layouts move. "Does the DLL still read it the same way" is then a
question with an exact answer -- the DLL's own offset / object-array / FNamePool / AOB verdicts --
and this asks it the same way every time instead of by hand:

  1. launch the fixture (launch_dumpertest.py <flavour>, which refuses a second copy),
  2. inject the DLL (default dist\UE5Dumper.dll) and wait for `UE5_Init: Complete`,
  3. over the pipe: get_pointers, get_offsets, get_object_count, find_instances(<class>) and a
     walk_instance of the first non-CDO instance -- every reply tee'd to <out>\smoke.json,
  4. copy the DLL's own logs to <out>\,
  5. with --baseline <dir of an earlier run's logs>: diff the VERDICT lines of offsets-0.log and the
     AOB winners of scan-0.log after stripping timestamps, addresses and counts. A hotfix that
     changes nothing we depend on diffs empty; anything else is printed, line by line,
  6. kill the fixture (one game at a time; --keep leaves it up).

A process that exists is not a game that booted (handover 3): the object count is printed and a
count under 1000 is reported as a FAILED boot, not as a result.
"""
import argparse
import json
import os
import re
import shutil
import subprocess
import sys
import time

sys.stdout.reconfigure(encoding="utf-8", errors="replace")
HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(os.path.dirname(HERE))
sys.path.insert(0, HERE)
from pipe_client import PipeClient  # noqa: E402

LOGS = os.path.join(os.environ.get("LOCALAPPDATA", ""), "UE5CEDumper", "Logs")
PROC = {"shipping58": "DumperTest58-Win64-Shipping", "dev58": "DumperTest58",
        "shipping": "DumperTest-Win64-Shipping", "dev": "DumperTest"}
DEFAULT_CLASS = {"shipping58": "DumperTest58Actor", "dev58": "DumperTest58Actor",
                 "shipping": "DumperTestActor", "dev": "DumperTestActor"}

TS = re.compile(r"^\[[^\]]+\]\s*")
ADDR = re.compile(r"0x[0-9A-Fa-f]{6,}")
HEXWORD = re.compile(r"\+[0-9A-F]{2}:[0-9A-F]{16}")
NUMS = re.compile(r"\b(Num|Count|index|Max|count|objects?)=\d+")


def norm(line):
    s = TS.sub("", line.rstrip("\n"))
    s = ADDR.sub("0xADDR", s)
    s = HEXWORD.sub("+XX:WORD", s)
    return NUMS.sub(lambda m: m.group(1) + "=N", s)


def verdicts(path, cats=("[DYNO]", "[OARR]", "[FNAM]")):
    if not os.path.exists(path):
        return []
    out = []
    for ln in open(path, encoding="utf-8", errors="replace"):
        if "[DEBUG]" in ln or not any(c in ln for c in cats):
            continue
        out.append(norm(ln))
    return out


def winners(path):
    if not os.path.exists(path):
        return []
    return [norm(ln) for ln in open(path, encoding="utf-8", errors="replace")
            if "winner:" in ln or "FindAll: Complete" in ln]


def diff(label, old, new):
    so, sn = set(old), set(new)
    gone, added = [l for l in old if l not in sn], [l for l in new if l not in so]
    print("\n[diff] %s: %d baseline line(s), %d new line(s), %d only-in-baseline, %d only-in-new"
          % (label, len(old), len(new), len(gone), len(added)))
    for l in gone:
        print("   - " + l[:200])
    for l in added:
        print("   + " + l[:200])
    return len(gone) + len(added)


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("flavour", choices=sorted(PROC))
    ap.add_argument("--dll", default=os.path.join(REPO, "dist", "UE5Dumper.dll"))
    ap.add_argument("--class", dest="cls", default=None)
    ap.add_argument("--out", required=True)
    ap.add_argument("--baseline", default=None, help="folder holding an earlier run's offsets-0.log / scan-0.log")
    ap.add_argument("--keep", action="store_true", help="leave the fixture running")
    ap.add_argument("--init-timeout", type=int, default=180)
    a = ap.parse_args()
    os.makedirs(a.out, exist_ok=True)
    cls = a.cls or DEFAULT_CLASS[a.flavour]
    logdir = os.path.join(LOGS, PROC[a.flavour])
    py = sys.executable

    r = subprocess.run([py, os.path.join(HERE, "launch_dumpertest.py"), a.flavour], capture_output=True, text=True,
                       encoding="utf-8", errors="replace")
    print((r.stdout or "").strip().splitlines()[-1:] or r.stderr)
    if r.returncode != 0:
        raise SystemExit("launch failed:\n" + (r.stdout or "") + (r.stderr or ""))
    pid = int(open(os.path.join(REPO, "out", "host.pid")).read().strip())
    t_launch = time.time()
    r = subprocess.run([py, os.path.join(HERE, "inject.py"), "--pid", str(pid), "--dll", a.dll], capture_output=True,
                       text=True, encoding="utf-8", errors="replace")
    print("inject:", ((r.stdout or "").strip().splitlines() or ["?"])[-1])
    init = os.path.join(logdir, "init-0.log")
    deadline = time.time() + a.init_timeout
    while time.time() < deadline:
        if os.path.exists(init) and os.path.getmtime(init) >= t_launch - 5:
            txt = open(init, encoding="utf-8", errors="replace").read()
            if "UE5_Init: Complete" in txt or "FAILED" in txt:
                break
        time.sleep(2)
    else:
        print("!! no `UE5_Init: Complete` within %d s" % a.init_timeout)

    rep = {"flavour": a.flavour, "pid": pid, "dll": a.dll, "class": cls}
    try:
        with PipeClient(timeout=120) as c:
            rep["build"] = c.assert_build()
            for cmd in ("get_pointers", "get_offsets", "get_object_count"):
                rep[cmd] = c.request(cmd)
            fi = c.request("find_instances", class_name=cls, limit=8)
            rep["find_instances"] = fi
            addrs = [x.get("addr") for x in (fi.get("instances") or fi.get("results") or [])
                     if x.get("addr") and not str(x.get("name", "")).startswith("Default__")]
            if addrs:
                w = c.request("walk_instance", addr=addrs[0])
                fields = w.get("fields") or []
                rep["walk_instance"] = {"addr": addrs[0], "class": w.get("class_name") or w.get("class"),
                                        "field_count": len(fields),
                                        "first_fields": [(f.get("name"), f.get("offset"), f.get("type"))
                                                         for f in fields[:12]]}
    finally:
        json.dump(rep, open(os.path.join(a.out, "smoke.json"), "w", encoding="utf-8"), ensure_ascii=False, indent=1)
        for n in ("init-0.log", "scan-0.log", "offsets-0.log", "walk-0.log", "pipe-0.log"):
            p = os.path.join(logdir, n)
            if os.path.exists(p):
                shutil.copy2(p, os.path.join(a.out, n))
        if not a.keep:
            subprocess.run(["taskkill", "/F", "/PID", str(pid)], capture_output=True)
            print("fixture pid %d killed" % pid)

    p = rep.get("get_pointers", {})
    n = (rep.get("get_object_count") or {}).get("count", p.get("object_count"))
    print("\nbuild %s | ue_version %s | objects %s | gobjects %s | item %s/%s detect=%s"
          % (rep.get("build"), p.get("ue_version"), n, p.get("gobjects_method"), p.get("item_size"),
             p.get("item_layout_mode"), p.get("item_detect")))
    if "walk_instance" in rep:
        wi = rep["walk_instance"]
        print("walk %s (%s): %d field(s)" % (wi["addr"], wi["class"], wi["field_count"]))
    else:
        print("!! no non-CDO instance of %s to walk" % cls)
    booted = isinstance(n, int) and n >= 1000
    if not booted:
        print("!! FAILED BOOT: object count %r -- not a result (handover 3)" % n)

    changes = 0
    if a.baseline:
        changes += diff("offsets-0.log verdicts", verdicts(os.path.join(a.baseline, "offsets-0.log")),
                        verdicts(os.path.join(a.out, "offsets-0.log")))
        changes += diff("scan-0.log AOB winners", winners(os.path.join(a.baseline, "scan-0.log")),
                        winners(os.path.join(a.out, "scan-0.log")))
    print("\nSMOKE %s%s" % ("OK" if booted else "FAILED",
                            (" -- %d verdict line(s) differ from the baseline" % changes) if a.baseline else ""))
    return 0 if booted else 1


if __name__ == "__main__":
    sys.exit(main())
