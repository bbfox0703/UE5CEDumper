r"""SW8 — `Ubel::GetMapPairLayout`'s refusal must not fire SPURIOUSLY on a real title's TMaps.

    py tools/verify/sw8_map_geometry.py                  # any UE >= 4.25 title, injected
    py tools/verify/sw8_map_geometry.py --queries a,e,i,o,u --limit 50000

THE ROW. `[TMAPGEOM-2026-09-09]` made `GetMapPairLayout` REFUSE rather than derive a pair stride
from an `FStructProperty::Struct` pointer it could not read, because a silent 0 there flows into
`ResolveElementAlignment` -> `pairAlign` -> `pairStride` and mis-strides the WHOLE map. That fix is
✅ proven, but **offline by construction**: `dll_core_test` manufactures the partial read with a
page edge (`VirtualAlloc`, one page of two committed), since a wholly-unreadable property
short-circuits earlier and no live game can be made to unmap one page of a property.

⭐ What a live run adds is the OTHER direction, and it is the one a new refusal always owes:
**that it does not fire when nothing is wrong.** A refusal that blanks working data is a
regression even though every unit test of the refusal passes.

⛔ TRAP 1 — `walk_instance` IS NOT THIS FUNCTION. The instance walk carries its own inlined copy
of the map geometry (`Ubel.cpp:~4730`) and never calls `GetMapPairLayout`, so `map_stride` on the
wire is NOT evidence about it. The four real callers are all recursive collectors in `Aura.cpp`:
`CollectContainersRecursive`, `CollectRefMetaRecursive`, `CollectSchemaLeaves`, `ScanForValue`.
This rig drives `CollectSchemaLeaves` through `search_properties` with `deep: true`.

⛔ TRAP 2 — A UE < 4.25 TITLE MAKES THIS VACUOUS. `GetMapPairLayout` opens with
`if (!fieldAddr || !DynOff::bUseFProperty) return false;`, so on the UProperty path it refuses
EVERY map before reaching the branch under test: zero warnings, zero leaves, and a run that looks
clean while measuring nothing. Asserted below, with the reason.

⛔ TRAP 3 — "NO WARNING IN THE LOG" PROVES NOTHING BY ITSELF, twice over: the code path may never
have run, and the log channel may not carry the line. Both are closed here — the positive count
below shows the path ran, and the channel is checked for `[WARN] [WALK` traffic.

THE POSITIVE OBSERVABLE, and why it is exactly the right one. `CollectSchemaLeaves` recurses into
a map only inside `if (GetMapPairLayout(...))`, and then only into `layout.valueStructAddr` /
`keyStructAddr` — the very pointers the guarded block reads. So **one map-derived leaf is proof
that the guarded `ReadSafe` ran AND succeeded AND the layout was published**. A leaf tagged
`.Key` / `.Value` proves more: the code tags a side only when `bothStruct`, i.e. when BOTH
pointers came back non-zero.

ACCEPTANCE (both sides, per the register's charter):
  * DLL/wire side — N > 0 map-derived schema leaves come back from `search_properties deep`,
    which no code path can produce without a successful layout;
  * DLL/log side  — zero `"TMap geometry: FStructProperty::Struct faulted"` lines, on a walk log
    shown to carry `[WARN] [WALK` traffic.
"""
from __future__ import annotations

import argparse
import glob
import io
import json
import os
import re
import subprocess
import sys
import time
from pathlib import Path

HERE = Path(__file__).resolve().parent
ROOT = HERE.parent.parent
CLIENT = HERE / "pipe_client.py"

REFUSAL = "TMap geometry"          # Ubel.cpp: Sein::Warn("WALK", "TMap geometry: ...")


def call(cmd: str, args: dict) -> dict:
    r = subprocess.run([sys.executable, str(CLIENT), cmd, "--args", json.dumps(args)],
                       capture_output=True, text=True, encoding="utf-8", errors="replace",
                       cwd=str(ROOT))
    out = r.stdout or ""
    i = out.find("{")
    if i < 0:
        raise SystemExit("pipe_client gave no JSON for %s:\n%s\n%s" % (cmd, out, r.stderr))
    return json.loads(out[i:])


LOGROOT = Path(os.environ["LOCALAPPDATA"]) / "UE5CEDumper" / "Logs"


def injected_process(max_age_s: int = 600) -> str:
    """Which process is the DLL living in right now?

    ⛔ NOT via `tasklist /m`. That works for a hand-injected `UE5Dumper.dll`, but a DEPLOYED
    PROXY is named `version` / `dinput8` / `dxgi` / `winmm`, and matching those bare names is
    worse than useless -- every D3D title loads the SYSTEM `dxgi.dll`, so the probe would
    "find" the dumper in a process that has never heard of it. Measured on Titan Quest II,
    2026-09-09: our code was live via the deployed `dxgi.dll` proxy and the tasklist probe
    reported "inject first".

    The log stream settles it without guessing a module name: the DLL writes continuously, so
    the log folder touched most recently IS the process it is in. Recency is asserted, because
    a stale folder from a game that exited hours ago would otherwise be picked silently.
    """
    best, best_t = None, 0.0
    for d in LOGROOT.iterdir() if LOGROOT.is_dir() else []:
        if not d.is_dir():
            continue
        for f in d.glob("*.log"):
            t = f.stat().st_mtime
            if t > best_t:
                best, best_t = d.name, t
    if not best:
        raise SystemExit("no log folder under %s -- is the DLL loaded at all?" % LOGROOT)
    age = time.time() - best_t
    if age > max_age_s:
        raise SystemExit(
            "the newest log folder is %r, last written %.0f s ago -- that is a process that "
            "has already exited, not the one under test. Pass --process explicitly."
            % (best, age))
    return best


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--queries", default="a,e,i,o",
                    help="deep-search substrings; broad by design (default: 4 vowels)")
    ap.add_argument("--limit", type=int, default=50000)
    ap.add_argument("--process", default=None, help="log folder name (default: auto-detect)")
    a = ap.parse_args()

    proc = a.process or injected_process()
    print("process    : %s" % proc)

    # --- TRAP 2: the UProperty path refuses every map before the branch under test -----
    off = call("get_offsets", {})
    print("offsets    : use_fproperty=%r  ue=%r" % (off.get("use_fproperty"),
                                                    off.get("ue_version")))
    if not off.get("use_fproperty"):
        raise SystemExit(
            "REFUSING TO RUN: use_fproperty is false (UE < 4.25). GetMapPairLayout returns "
            "false at its first line on that path, so it never reaches the FStructProperty "
            "read this row is about. The run would show zero warnings and zero leaves and "
            "mean nothing. Use a UE 4.25+ title.")

    # --- every MapProperty this title declares, by class -------------------------------
    m = call("search_properties", {"query": "", "game_only": False, "limit": a.limit,
                                   "types": ["MapProperty"], "deep": False})
    bycls: dict[str, set[str]] = {}
    for r in m.get("results", []):
        bycls.setdefault(r.get("class_name", ""), set()).add(r.get("prop_name", ""))
    total_maps = len(m.get("results", []))
    print("MapProperty: %d field(s) across %d class(es)" % (total_maps, len(bycls)))
    if total_maps == 0:
        raise SystemExit("this title declares no TMap at all -- nothing for SW8 to measure")

    # --- drive CollectSchemaLeaves and keep the map-derived leaves ----------------------
    leaves: dict[tuple[str, str], dict] = {}
    for q in [s for s in a.queries.split(",") if s]:
        d = call("search_properties", {"query": q, "game_only": False, "limit": a.limit,
                                       "deep": True})
        res = d.get("results", [])
        n = 0
        for r in res:
            pn = r.get("prop_name") or ""
            if "[]" not in pn:
                continue          # shared with Array/Set -- the class check below decides
            if pn.split("[]")[0] in bycls.get(r.get("class_name", ""), ()):
                leaves[(r["class_name"], pn)] = r
                n += 1
        print("  query %-4r -> %6d result(s), %5d map-derived%s"
              % (q, len(res), n, "  (ABORTED)" if d.get("aborted") else ""))

    both = [p for (_c, p) in leaves if ".Key" in p or ".Value" in p]
    print("map-derived schema leaves : %d distinct" % len(leaves))
    print("  of which .Key/.Value    : %d  (only emitted when BOTH sides are structs, i.e. "
          "BOTH guarded reads returned non-zero)" % len(both))

    # --- the log side -------------------------------------------------------------------
    logdir = Path(os.environ["LOCALAPPDATA"]) / "UE5CEDumper" / "Logs" / proc
    files = sorted(glob.glob(str(logdir / "walk*.log")), key=os.path.getmtime, reverse=True)
    refusals: list[str] = []
    channel = 0
    for f in files:
        t = io.open(f, encoding="utf-8", errors="replace").read()
        channel += len(re.findall(r"\[WARN\] \[WALK", t))
        for ln in t.splitlines():
            if REFUSAL in ln:
                refusals.append("%s: %s" % (os.path.basename(f), ln.strip()))
    print("walk logs  : %d file(s) under %s" % (len(files), logdir))
    print("  [WARN] [WALK lines (channel proof) : %d" % channel)
    print("  %r refusals                        : %d" % (REFUSAL, len(refusals)))

    fails: list[str] = []
    if not files:
        fails.append("no walk*.log for %s -- an absent warning would prove nothing" % proc)
    if not leaves:
        fails.append(
            "ZERO map-derived schema leaves. GetMapPairLayout either never ran or returned "
            "false for every map, so 'no refusal in the log' measures nothing. Widen "
            "--queries, or check that this title's maps have a struct side at all.")
    if refusals:
        fails.append("the refusal FIRED %d time(s) on a healthy title -- that is the "
                     "spurious-refusal regression this row exists to rule out:\n      %s"
                     % (len(refusals), "\n      ".join(refusals[:5])))
    if channel == 0:
        print("  ⚠ no [WARN] [WALK line has ever been written for this process, so the log "
              "channel is UNPROVEN here; the zero above is weaker evidence on this title.")

    print()
    if fails:
        print("SW8: FAIL")
        for f in fails:
            print("  - %s" % f)
        return 1
    print("SW8: PASS -- %d map-derived schema leaves (%d of them both-struct) prove "
          "GetMapPairLayout laid out real TMaps on this title, and it refused none of them: "
          "0 %r lines across %d walk log(s) that carry %d WALK warning(s) overall."
          % (len(leaves), len(both), REFUSAL, len(files), channel))
    return 0


if __name__ == "__main__":
    sys.exit(main())
