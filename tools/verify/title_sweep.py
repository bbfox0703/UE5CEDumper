"""GROUP 7: sweep one Steam title end to end, headless, and record what it answers.

    py title_sweep.py "Lushfoil"          # folder substring
    py title_sweep.py "Lushfoil" --keep   # leave the game running afterwards

ONE TITLE PER RUN, BY DESIGN. ⛔ The rule is not merely about CPU: with a host already
injected, the next one logs `pipe already exists (another UE5Dumper instance running)
— skipping auto-start` and NEVER SCANS (122-byte scan log), so every reading taken
from it is an absence the injection itself caused. This refuses to start if any of our
proxies is already mapped anywhere.

WHAT ONE SWEEP PAYS FOR (session plan, GROUP 7 — "advances 4 rows at once"):
  * `G12` heuristic-branch discovery and `G8`/`G9`/`G11` version detection — the
    `[SCAN:Ver]` lines, verbatim, plus which tier answered.
  * `U2` CPN screening — `get_offsets` polled until `probe_ran:true`. CPN exists in
    UE4 too, so every title is worth screening; expect all-false and record that as
    "swept N, all false", which is the honest form of a null result.
  * `G1`/`X3`/`U7` — class/offset totals from a real pool.

PROXY FRESHNESS IS CHECKED FIRST, not assumed: `assert_build()` compares the DLL
answering the pipe against `dist/build_number.txt`, and every deployed proxy on this
machine was stale on 2026-08-19. A stale proxy owns the pipe and answers happily.
"""
import argparse
import json
import pathlib
import subprocess
import sys
import time

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
from pipe_client import PipeClient, StaleBuildError        # noqa: E402

ROOT = pathlib.Path(__file__).resolve().parents[2]
LIBS = [pathlib.Path(r"C:\Program Files (x86)\Steam\steamapps\common"),
        pathlib.Path(r"D:\SteamLibrary\steamapps\common")]
LOGROOT = pathlib.Path.home() / "AppData/Local/UE5CEDumper/Logs"
OURS = {"dxgi.dll", "version.dll", "winmm.dll", "dinput8.dll"}
OUT = ROOT / "out" / "sweep"


def say(s):
    enc = sys.stdout.encoding or "utf-8"
    sys.stdout.write(s.encode(enc, "replace").decode(enc, "replace") + "\n")


def find_title(match):
    for lib in LIBS:
        if not lib.is_dir():
            continue
        for d in sorted(lib.iterdir()):
            if d.is_dir() and match.lower() in d.name.lower():
                exes = list(d.rglob("*-Win64-Shipping.exe"))
                if exes:
                    return d, exes[0]
    raise SystemExit(f"title_sweep: no game folder matching {match!r} with a shipping exe")


MACHINE_JSON = (pathlib.Path.home()
                / "AppData/Local/UE5CEDumper/UE5CEDumper.MSI-NB.json")


def hint_for(exe_name):
    """(peHash, entry) for `exe_name`, or (None, None)."""
    d = json.loads(MACHINE_JSON.read_text(encoding="utf-8"))
    for k, v in d["games"].items():
        if (v.get("gameName") or "").lower() == exe_name.lower():
            return k, v
    return None, None


def drop_hint(exe_name):
    """Delete the cached hint so DetectVersion actually RUNS.

    Without this the scan logs `FindAll: UE Version = NNN (cached, rev=5, detected=yes,
    lowConf=no) — skipped DetectVersion` and produces NO `[SCAN:Ver] DetectVersion`
    lines at all, so G8/G9/G11/G12 get no evidence whatsoever from the run. Measured on
    Lushfoil before this was added.

    The entry is READ FIRST and returned, because its `ueVersion` is the only "before"
    that exists for G11 step 1's regression comparison -- deleting it without capturing
    it destroys the comparison the step is asking for.
    """
    h, entry = hint_for(exe_name)
    if h is None:
        return None, None
    d = json.loads(MACHINE_JSON.read_text(encoding="utf-8"))
    del d["games"][h]
    MACHINE_JSON.write_text(json.dumps(d, indent=2, ensure_ascii=False), encoding="utf-8")
    if hint_for(exe_name)[0] is not None:
        raise SystemExit(f"title_sweep: FAILED -- hint for {exe_name} survived deletion")
    return h, entry


def anything_of_ours_running():
    o = subprocess.run(["tasklist", "/m", "/fo", "csv"],
                       capture_output=True, text=True, errors="replace").stdout.lower()
    return "ue5dumper.dll" in o


def wait_pointers(c, budget=240):
    """Poll until GObjects resolves, or give up honestly."""
    end = time.time() + budget
    last = {}
    while time.time() < end:
        last = c.request("get_pointers")
        if last.get("gobjects_method") not in (None, "", "not_found"):
            return last, True
        time.sleep(5)
    return last, False


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("title")
    ap.add_argument("--keep", action="store_true")
    ap.add_argument("--boot", type=int, default=60, help="seconds to let the game boot")
    a = ap.parse_args()

    if anything_of_ours_running():
        raise SystemExit("title_sweep: REFUSED -- UE5Dumper.dll is already mapped in some "
                         "process. A second host silently skips auto-start and never scans. "
                         "Kill the other host first.")

    d, exe = find_title(a.title)
    say(f"title  : {d.name}")
    say(f"exe    : {exe}")
    proxies = [q.name for q in exe.parent.glob("*.dll") if q.name.lower() in OURS]
    say(f"proxy  : {proxies or 'NONE (will inject directly)'}")

    # Capture the cached verdict, then clear it, so DetectVersion actually runs and
    # G11 step 1 has a genuine before/after rather than a re-read of the same cache.
    pehash, cached = drop_hint(exe.name)
    if cached:
        say(f"cached : PE={pehash} ueVersion={cached.get('ueVersion')} "
            f"detected={cached.get('versionDetected')} lowConf={cached.get('lowConfidence')} "
            f"rev={cached.get('versionDetectRev')} scans={cached.get('scanCount')}  -> CLEARED")
    else:
        say("cached : (no entry -- this is a first-ever scan of this binary)")

    proc = subprocess.Popen([str(exe)], cwd=str(exe.parent),
                            creationflags=0x00000008 | 0x00000200)
    say(f"launched pid {proc.pid}; booting for {a.boot}s ...")
    time.sleep(a.boot)

    # Is it actually still there? A Steam title started from its shipping exe can
    # exit immediately (DRM wants to be launched through Steam), or hand off to a
    # differently-named process. Detect that HERE and say so, rather than letting a
    # missing pipe surface later as a confusing PipeError that reads like a DLL fault.
    alive = subprocess.run(["tasklist", "/FI", f"IMAGENAME eq {exe.name}", "/FO", "CSV", "/NH"],
                           capture_output=True, text=True, errors="replace").stdout
    if exe.name.lower() not in alive.lower():
        say(f"NOT RUNNING after {a.boot}s -- {exe.name} exited or handed off. "
            f"Most likely Steam DRM refusing a direct launch; this title needs to be "
            f"started through the Steam client. Recording as NOT SWEPT.")
        result = {"title": d.name, "exe": exe.name, "proxy": proxies,
                  "swept": False, "reason": "exe did not stay running after direct launch",
                  "cached_before": cached, "pe_hash": pehash}
        OUT.mkdir(parents=True, exist_ok=True)
        (OUT / f"{exe.stem}.json").write_text(json.dumps(result, indent=1, ensure_ascii=False),
                                              encoding="utf-8")
        # Put the hint back: we cleared it and then learned nothing, so leaving it
        # deleted would silently cost the NEXT run its G11 "before".
        if cached is not None:
            dd = json.loads(MACHINE_JSON.read_text(encoding="utf-8"))
            dd["games"][pehash] = cached
            MACHINE_JSON.write_text(json.dumps(dd, indent=2, ensure_ascii=False),
                                    encoding="utf-8")
            say(f"restored the cleared hint entry for {exe.name}")
        return 2

    result = {"title": d.name, "exe": exe.name, "proxy": proxies,
              "cached_before": cached, "pe_hash": pehash}
    try:
        if not proxies:
            r = subprocess.run([sys.executable, str(ROOT / "tools/verify/inject.py"),
                                "--name", exe.stem], capture_output=True, text=True,
                               errors="replace")
            say(r.stdout.strip().splitlines()[-1] if r.returncode == 0
                else f"inject FAILED: {r.stdout}{r.stderr}")
            time.sleep(10)

        # Wait for the pipe rather than assuming the boot budget was enough. When it
        # never appears, print init-0.log's tail: it says WHY, and the reason is often
        # not "the game is slow". Measured on OCTOPATH, which relaunches itself --
        # instance A opened the pipe, instance B logged `UE5_StartPipeServer: pipe
        # already exists (another instance running) — skipping`, then A exited and took
        # the pipe with it, leaving a live game with no server at all.
        pipe_path = pathlib.Path(r"\\.\pipe\UE5DumpBfx")
        deadline = time.time() + 120
        while time.time() < deadline:
            try:
                with open(pipe_path, "r+b", buffering=0):
                    break
            except OSError:
                time.sleep(3)
        else:
            tail = (LOGROOT / exe.stem / "init-0.log")
            say(f"NO PIPE after boot+120s. init-0.log says:")
            if tail.is_file():
                for l in tail.read_text(encoding="utf-8", errors="replace").splitlines()[-6:]:
                    say("   " + l.strip()[:180])
            result.update(swept=False, reason="pipe never appeared")
            OUT.mkdir(parents=True, exist_ok=True)
            (OUT / f"{exe.stem}.json").write_text(
                json.dumps(result, indent=1, ensure_ascii=False), encoding="utf-8")
            return 3

        with PipeClient(timeout=300.0) as c:
            try:
                build = c.assert_build()
                say(f"build  : {build}  (matches dist)")
                result["build"] = build
            except StaleBuildError as e:
                say(f"*** STALE BUILD *** {e}")
                result["stale_build"] = str(e)
                return 1

            # Proxy mode starts the pipe only -- it does NOT scan.
            p = c.request("get_pointers")
            if p.get("gobjects_method") in (None, "", "not_found"):
                say("triggering scan (proxy mode does not scan on load) ...")
                c.request("trigger_scan")
            p, resolved = wait_pointers(c)
            result["pointers"] = {k: p.get(k) for k in (
                "gobjects", "gobjects_method", "gobjects_pattern_id",
                "gnames", "gnames_method", "gnames_pattern_id",
                "gworld", "gworld_method", "gworld_pattern_id",
                "gengine", "gengine_method", "ue_version")}
            say(f"pointers resolved: {resolved}")
            for k in ("gobjects", "gnames", "gworld", "gengine"):
                say(f"  {k:<9} {p.get(k)}  {p.get(k+'_method')}  {p.get(k+'_pattern_id','')}")

            n = c.request("get_object_count")
            result["object_count"] = n.get("count")
            say(f"objects: {n.get('count')}")

            # U2 screening -- poll until the probe actually ran.
            off = {}
            for _ in range(30):
                off = c.request("get_offsets")
                if off.get("probe_ran"):
                    break
                time.sleep(2)
            # NOTE the CPN key is `case_preserving`, not `cpn` -- asking for the wrong
            # name returns None, which reads as "screened, negative" when nothing was
            # screened at all. That is the U2 null result being faked.
            result["offsets"] = {k: off.get(k) for k in
                                 ("probe_ran", "validated", "case_preserving",
                                  "use_fproperty", "item_size", "item_layout_mode",
                                  "item_obj_offset", "uobject_outer")}
            say(f"offsets: probe_ran={off.get('probe_ran')} validated={off.get('validated')} "
                f"CPN(case_preserving)={off.get('case_preserving')!r} "
                f"item_size={off.get('item_size')} layout={off.get('item_layout_mode')}")

            cls = c.request("list_classes")
            result["class_total"] = cls.get("total") or cls.get("count")
            say(f"classes: {result['class_total']}")
    finally:
        if not a.keep:
            subprocess.run(["taskkill", "/F", "/IM", exe.name], capture_output=True)
            time.sleep(3)
            o = subprocess.run(["tasklist", "/FO", "CSV", "/NH"],
                               capture_output=True, text=True, errors="replace").stdout
            say(f"killed {exe.name}: gone={exe.name.lower() not in o.lower()}")

    # The version-detection lines are the G8/G9/G11/G12 payload.
    logdir = LOGROOT / exe.stem
    scan = logdir / "scan-0.log"
    vers = []
    if scan.is_file():
        vers = [l.strip() for l in scan.read_text(encoding="utf-8", errors="replace").splitlines()
                if "[SCAN:Ver]" in l or "DetectVersion" in l or "DetectPublisher" in l]
    result["version_lines"] = vers
    # G11 step 1: the cached verdict vs the freshly detected one.
    newv = (result.get("pointers") or {}).get("ue_version")
    if cached is not None:
        oldv = cached.get("ueVersion")
        same = (str(oldv) == str(newv))
        result["g11_version_before_after"] = {"before": oldv, "after": newv, "same": same}
        say("")
        say(f"G11 step 1: cached ueVersion={oldv} -> re-detected {newv}   "
            f"{'IDENTICAL (expected)' if same else '*** CHANGED -- a real finding ***'}")
    say("\n--- version detection ---")
    for l in vers[:12]:
        say("  " + l[:190])
    if not vers:
        say("  (none -- no scan log for this exe stem, or the scan never ran)")

    OUT.mkdir(parents=True, exist_ok=True)
    dest = OUT / f"{exe.stem}.json"
    dest.write_text(json.dumps(result, indent=1, ensure_ascii=False), encoding="utf-8")
    say(f"\nwrote {dest}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
