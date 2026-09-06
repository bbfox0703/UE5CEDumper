"""A/B the Genau RIP-decode fix on one host: pre-fix DLL vs post-fix DLL.

    py genau_rip_ab.py run            # both sides, then the comparison
    py genau_rip_ab.py compare        # re-print the comparison from saved logs

WHAT THE ROW ASKS FOR (todo.md, "Genau RIP decode ... b2544"):
  * candidate / probe counts should go DOWN with the fix   <- the win
  * every resolved GObjects / GNames / GWorld address stays identical <- acceptance

AND WHAT IT WARNS: `sweep.sh`'s pattern diff CANNOT measure this -- `scan_patterns.java`
skips every `Symbol*`/`CallFollow` signature and the two data scans are runtime-only,
so a clean sweep diff would mean "not measured", not "no regression". The only
evidence is the DLL's own scan log, same host, before vs after.

WHY THE HOST IS A python.exe SLEEPER AND NOT A GAME. The predicate is used at five
sites, and all five live in RECOVERY paths:

    Genau.cpp:572   DataScanGObjectsCandidates
    Genau.cpp:755   FindGObjectsStaticStruct
    Genau.cpp:901   ResolveSymbolExport
    Genau.cpp:2323  FindGNamesByStringRef
    Genau.cpp:2348  FindGNamesByStringRef

On a healthy game the AOB wins immediately and NONE of them runs, so a game would
produce an identical-looking pair of logs for the worst possible reason -- the code
under test never executed. A non-UE host fails every AOB and therefore drives all
five at full stretch. The trade is stated honestly rather than hidden: on this host
the "addresses unchanged" half is trivially satisfied (nothing resolves), so this
rig measures the COUNT half. The address half needs the separate DumperTest run.

BOTH SIDES ARE BUILT IN THE SAME SESSION FROM THE SAME TREE by the same toolset,
differing ONLY in the two-line predicate -- not "current dist vs a checkout of
build 2544", which would differ in ~700 builds of unrelated ways.

The per-PE-hash hint entry is deleted before EACH side: a cached result changes how
many patterns are even attempted, which is precisely the quantity being compared.
"""
import os
import hashlib
import json
import pathlib
import re
import shutil
import subprocess
import sys
import time

ROOT = pathlib.Path(__file__).resolve().parents[2]
OUT = ROOT / "out" / "genau"
LOGROOT = pathlib.Path.home() / "AppData/Local/UE5CEDumper/Logs"
LOGS = LOGROOT / "python"             # rebound per host by run_side()
MACHINE_JSON = (pathlib.Path.home()
                / ("AppData/Local/UE5CEDumper/UE5CEDumper.%s.json"
                       % os.environ.get("COMPUTERNAME", "")))
SIDES = {"before": OUT / "before_buggy_UE5Dumper.dll",
         "after": OUT / "after_fixed_UE5Dumper.dll"}

# THE HOST MUST HAVE A LARGE MAIN MODULE, and this is not a preference -- it decides
# whether the measurement exists at all. MEASURED on a python.exe sleeper:
# DataScanGObjectsCandidates reported code=[...0x...1000-...0x...1E4C], i.e. a code
# section of 0xE4C = 3,660 BYTES, because python.exe is a launcher stub and all the
# real code lives in python312.dll, which is NOT the main module. The RIP decoder
# under test therefore barely executed, and both sides returned an identical
# "Found 17 static pointers in data section".
# That is a NULL RESULT MANUFACTURED BY THE HOST and it is indistinguishable, in the
# log, from "the fix changed nothing". Notepad++ is ~8.5 MB and Cheat Engine ~18.6 MB.
HOSTS = {
    "python": None,        # kept deliberately: the known null-result control
    "notepad++": r"C:\Program Files\Notepad++\notepad++.exe",
    "ce": r"C:\Program Files\Cheat Engine\cheatengine-x86_64-SSE4-AVX2.exe",
}


def drop_hint(exe_name):
    """Delete the per-PE-hash hint entry for `exe_name` -- matched by NAME, not hash.

    By name because the hash is per-binary and this rig is host-parameterised; a
    hardcoded hash would silently skip the delete for any other host and leave a warm
    cache, which changes how many patterns are even attempted -- the very quantity
    being compared.
    """
    d = json.loads(MACHINE_JSON.read_text(encoding="utf-8"))
    doomed = [k for k, v in d["games"].items()
              if (v.get("gameName") or "").lower() == exe_name.lower()]
    for k in doomed:
        del d["games"][k]
    if doomed:
        MACHINE_JSON.write_text(json.dumps(d, indent=2, ensure_ascii=False), encoding="utf-8")
    back = json.loads(MACHINE_JSON.read_text(encoding="utf-8"))
    if [k for k, v in back["games"].items()
            if (v.get("gameName") or "").lower() == exe_name.lower()]:
        raise SystemExit(f"genau_ab: FAILED -- hint entry for {exe_name} survived the "
                         f"delete; the run would not be a cold scan")
    return doomed


def _pid_by_name(exe_name):
    out = subprocess.run(["tasklist", "/FI", f"IMAGENAME eq {exe_name}", "/FO", "CSV", "/NH"],
                         capture_output=True, text=True, errors="replace").stdout
    pids = []
    for line in out.splitlines():
        parts = [x.strip('"') for x in line.split('","')]
        if len(parts) >= 2 and parts[0].lower() == exe_name.lower() and parts[1].isdigit():
            pids.append(int(parts[1]))
    return pids[-1] if pids else None


def run_side(side, host):
    global LOGS
    dll = SIDES[side]
    if not dll.is_file():
        raise SystemExit(f"genau_ab: FAILED -- {dll} missing; build both sides first")

    exe = HOSTS[host]
    if exe is None:
        argv = [sys.executable, "-c", "import time;time.sleep(900)"]
        exe_name, logdir = "python.exe", "python"
    else:
        if not pathlib.Path(exe).is_file():
            raise SystemExit(f"genau_ab: FAILED -- host not found: {exe}")
        argv = [exe]
        exe_name, logdir = pathlib.Path(exe).name, pathlib.Path(exe).stem
    drop_hint(exe_name)
    LOGS = LOGROOT / logdir

    p = subprocess.Popen(argv, creationflags=0x00000008 | 0x00000200)
    time.sleep(5)

    # Resolve the pid we will actually inject into BY IMAGE NAME, not from Popen.
    # Notepad++ (and many GUI apps) are single-instance relaunchers: the process we
    # spawned hands off and exits, so Popen's pid is already dead and Toolhelp fails
    # with a bare Win32 299 that reads like a permissions problem.
    pid = _pid_by_name(exe_name)
    if pid is None:
        raise SystemExit(f"genau_ab: FAILED -- no live {exe_name} five seconds after launch")
    if pid != p.pid:
        print(f"  note: launched pid {p.pid} handed off to {pid} (single-instance relaunch)")
    p = type("H", (), {"pid": pid})()
    try:
        r = subprocess.run([sys.executable, str(ROOT / "tools/verify/inject.py"),
                            "--pid", str(p.pid), "--dll", str(dll)],
                           capture_output=True, text=True, errors="replace")
        print(r.stdout.strip())
        if r.returncode != 0:
            raise SystemExit(f"genau_ab: FAILED -- inject: {r.stdout}{r.stderr}")
        # The scan is the slow part; wait for its own completion marker rather than
        # a fixed sleep, so a slower machine cannot silently truncate the log.
        deadline = time.time() + 180
        scan = LOGS / "scan-0.log"
        while time.time() < deadline:
            if scan.is_file() and "FindAll: Complete" in scan.read_text(
                    encoding="utf-8", errors="replace"):
                break
            time.sleep(2)
        else:
            raise SystemExit("genau_ab: FAILED -- no 'FindAll: Complete' within 180 s; "
                             "an incomplete log would compare as 'fewer probes'")
        shutil.copy2(scan, OUT / f"{side}.scan.log")
        print(f"  captured {OUT / f'{side}.scan.log'}")
    finally:
        subprocess.run(["taskkill", "/F", "/PID", str(p.pid)], capture_output=True)
        time.sleep(1)


def metrics(path):
    """Counts the row cares about, pulled out of one scan log."""
    txt = path.read_text(encoding="utf-8", errors="replace")
    m = {}
    m["log_lines"] = len(txt.splitlines())
    # Per-target "N patterns tried, M with hits"
    for tgt, tried, hits in re.findall(
            r"=== (\w+): (\d+) patterns tried, (\d+) with hits", txt):
        m[f"{tgt}.tried"] = int(tried)
        m[f"{tgt}.hits"] = int(hits)
    # The resolved addresses (module-relative comparison is done by the caller)
    fa = re.search(r"FindAll: Complete — GObjects=(\w+) \((\w+)\), GNames=(\w+) \((\w+)\), "
                   r"GWorld=(\w+) \((\w+)\)", txt)
    if fa:
        m["GObjects"] = f"{fa.group(1)} ({fa.group(2)})"
        m["GNames"] = f"{fa.group(3)} ({fa.group(4)})"
        m["GWorld"] = f"{fa.group(5)} ({fa.group(6)})"
    # The RIP-decode work itself: how many candidates the data scan produced, and
    # how many probes the string-ref path made. These are the numbers the fix moves.
    m["datascan_candidate_lines"] = len(re.findall(r"DataScan.*candidate", txt, re.I))
    m["ripref_lines"] = len(re.findall(r"rip|RIP", txt))
    m["aobscanall_lines"] = len(re.findall(r"AOBScanAll:", txt))
    return m


def compare():
    a = metrics(OUT / "before.scan.log")
    b = metrics(OUT / "after.scan.log")
    keys = sorted(set(a) | set(b))
    print(f"\n{'metric':<34}{'BEFORE (buggy)':>22}{'AFTER (fixed)':>22}   verdict")
    print("-" * 100)
    for k in keys:
        va, vb = a.get(k, "-"), b.get(k, "-")
        note = ""
        if isinstance(va, int) and isinstance(vb, int):
            note = "same" if va == vb else ("LOWER after" if vb < va else "HIGHER after")
        elif va != vb:
            note = "*** CHANGED ***"
        else:
            note = "same"
        print(f"{k:<34}{str(va):>22}{str(vb):>22}   {note}")


if __name__ == "__main__":
    cmd = sys.argv[1] if len(sys.argv) > 1 else "run"
    OUT.mkdir(parents=True, exist_ok=True)
    if cmd == "run":
        host = sys.argv[2] if len(sys.argv) > 2 else "notepad++"
        if host not in HOSTS:
            raise SystemExit(f"genau_ab: unknown host {host!r}; choose from {sorted(HOSTS)}")
        print(f"host: {host}  ({HOSTS[host] or 'python.exe sleeper'})")
        for side in ("before", "after"):
            print(f"=== {side} ===")
            run_side(side, host)
        compare()
    elif cmd == "compare":
        compare()
    else:
        raise SystemExit(__doc__)
