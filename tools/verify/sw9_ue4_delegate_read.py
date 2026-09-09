r"""SW9 — a delegate's BINDINGS, read on UE4, off an actor the ENGINE spawned and bound.

    py tools/verify/sw9_ue4_delegate_read.py --engine 427
    py tools/verify/sw9_ue4_delegate_read.py --engine 423

THE ROW. `[D4B-DELEGATEPAD]` changed how every delegate reader derives its layout, and
`d4b_pad_survey.py` has run on UE 4.23 and 4.27 — but that rig walks CLASS FIELD TABLES, which a
UCLASS provides at module load without ever being spawned. So on UE4 the repo had measured
ElementSize RECOGNITION and nothing else: `walk_instance`, `find_instances` and `invoke_function`
appear in ZERO UE4 log. No UE4 run has ever READ A BINDING.

⭐ WHY A SPAWNED ACTOR, AND NOT A POKED ONE. SW7 manufactures a delegate state by writing memory;
that is the right tool there, because the STATE is what is under test. Here the READ is under
test, so the binding must be one the engine made: `ADelegatePadFixture::BeginPlay` calls
`AddDynamic` / `BindDynamic`, and the FNames that come back must be the engine's, not ours.
`UCheatManager::Summon(const FString&)` gives us that with no rebuild — it is `UFUNCTION(exec)`
in every installed UE4 engine, its body is unguarded, and UE4 calls `AddCheats()` unconditionally
from `APlayerController::PostInitializeComponents` with `CheatClass` defaulting to
`UCheatManager::StaticClass()`, gated only on `AllowCheats` (NM_Standalone || GIsEditor) — which a
standalone package satisfies.

⛔ TRAP 1 — THE CDO SPAWNS NOTHING AND REPORTS SUCCESS. `invoke_function`'s `class_name` shortcut
goes through `UE5_FindInstanceOfClass`, which falls back to the **Class Default Object** with only
a LOG_WARN (`Frieren.cpp:1038-1044`). On the CDO `GetOuterAPlayerController()` is null, so Summon
does nothing at all — while ProcessEvent still returns `result=0` and the response still says OK.
This rig therefore resolves a non-`Default__` CheatManager itself and passes `instance_addr`.

⛔ TRAP 2 — THE WRONG BINARY, SILENTLY. Pre-fixture UE4 builds share the exe stem with these, so
they share the `%LOCALAPPDATA%\UE5CEDumper\Logs\<stem>` folder, and their sizes are close. A run
against one of those finds no fixture for a reason that has nothing to do with the reader. The exe
is BYTE-SCANNED for the fixture symbols before it is launched, and the top-level
`WindowsNoEditor\<Project>.exe` (a ~145 KB BootstrapPackagedGame stub with zero hits) is rejected
by the same check.

⛔ TRAP 3 — THE STRIDE ARM CANNOT FAIL ON UE4, so it must not be reported as if it could. UE4 has
no `TDelegateAccessHandlerBase`, so ElementSize == base and the derived pad is 0 in EVERY UE4
configuration. What the array arm measures here is RECOGNITION (`array_elem_size` present and
== 16) and INDEXING (element [1] is reported at [1], not swallowed by [0]) — not the pad.
⛔ And `delegate_pad`'s ABSENCE is vacuous on UE4: Fern emits it only when non-zero, so "absent"
is what a correct UE4 run and a broken one both produce. It is printed, never asserted.

⛔ TRAP 4 — "(0 bindings)" ON BOTH ELEMENTS IS ALSO WHAT A BROKEN READ LOOKS LIKE. The row is
satisfied only by BOTH halves: element [0] genuinely empty AND element [1] naming the probe.

⚠ WHAT THIS RUN DOES **NOT** COVER, stated because the register's charter is coverage, not vibes:
the UE4 fixture declares no `TArray<FScriptDelegate>`, so `ReadDelegateArrayElements` — Phase J,
the reader its own comment calls "THE SIXTH D4b SITE" — is NEVER EXECUTED by this run. Nor is the
sparse walker. The readers this run does touch are printed at the end, keyed by the wire's
`array_inner_type`, so the row records what was measured rather than what was hoped.

ACCEPTANCE (both sides):
  * DLL/wire side — a non-CDO `DelegatePadFixture` exists AFTER the Summon and did not exist
    BEFORE it (a difference, not an assumption), and `walk_instance` on it names
    `DPad_OnPingProbe` on the scalar, the multicast and array element [1].
  * UI side — the Live Walker shows the same address with the same probe name (run separately;
    this rig prints the address and the expected strings for that arm).
"""
from __future__ import annotations

import argparse
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

PROBE = "DPad_OnPingProbe"
FIXTURE = "DelegatePadFixture"
ARCHIVE = Path(r"D:\UE_Analyze_data\For Testing")

# The packaged UE4 game binary -- NOT the WindowsNoEditor\<Project>.exe launcher stub.
ENGINES = {
    "423": ("UE423_Flying", "UE423_Flying"),
    "427": ("UE427_3rdPerson", "UE427_3rdPerson"),
}
DETACHED = 0x00000008 | 0x00000200


def call(cmd: str, args: dict | None = None) -> dict:
    r = subprocess.run([sys.executable, str(CLIENT), cmd, "--args", json.dumps(args or {})],
                       capture_output=True, text=True, encoding="utf-8", errors="replace",
                       cwd=str(ROOT))
    out = r.stdout or ""
    i = out.find("{")
    if i < 0:
        raise SystemExit("pipe_client gave no JSON for %s:\n%s\n%s" % (cmd, out, r.stderr))
    return json.loads(out[i:])


def running(image: str) -> list[int]:
    r = subprocess.run(["tasklist", "/FI", "IMAGENAME eq " + image, "/FO", "CSV", "/NH"],
                       capture_output=True, text=True, encoding="utf-8", errors="replace")
    pids = []
    for ln in (r.stdout or "").splitlines():
        m = re.match(r'^"([^"]+)","(\d+)"', ln.strip())
        if m and m.group(1).lower() == image.lower():
            pids.append(int(m.group(2)))
    return pids


def byte_scan(path: Path, needles: list[str]) -> dict:
    """⛔ TRAP 2's gate. Look for each needle as both ASCII and UTF-16LE."""
    blob = path.read_bytes()
    out = {}
    for n in needles:
        out[n] = (blob.count(n.encode("ascii")),
                  blob.count(n.encode("utf-16-le")))
    return out


def non_cdo(instances: list[dict]) -> list[dict]:
    return [i for i in instances if not (i.get("name") or "").startswith("Default__")]


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--engine", default="427", choices=sorted(ENGINES))
    ap.add_argument("--config", default="Development",
                    help="Development is REQUIRED for the CheatManager route")
    ap.add_argument("--wait", type=int, default=70, help="seconds to let the package load")
    ap.add_argument("--keep", action="store_true", help="leave the game running for the UI arm")
    a = ap.parse_args()

    proj, module = ENGINES[a.engine]
    stem = module if a.config == "Development" else "%s-Win64-%s" % (module, a.config)
    exe = (ARCHIVE / proj / a.config / "WindowsNoEditor" / proj / "Binaries" / "Win64"
           / (stem + ".exe"))
    print("engine     : UE %s.%s  %s" % (a.engine[0], a.engine[1:], a.config))
    print("exe        : %s" % exe)
    if not exe.is_file():
        raise SystemExit("no such packaged binary -- repackage first")
    size = exe.stat().st_size
    print("size       : %d bytes" % size)

    # ---- TRAP 2 --------------------------------------------------------------------
    if "Binaries" not in str(exe) or size < 4 * 1024 * 1024:
        raise SystemExit("that is the BootstrapPackagedGame launcher stub, not the game binary")
    scan = byte_scan(exe, [FIXTURE, PROBE])
    for k, (asc, wide) in scan.items():
        print("  byte-scan  %-22s ascii=%d utf16=%d" % (k, asc, wide))
    if not any(sum(v) for v in scan.values()) or sum(scan[FIXTURE]) == 0:
        raise SystemExit(
            "REFUSING TO LAUNCH: %r is not in this binary. Pre-fixture UE4 builds share the exe "
            "stem AND the log folder with the fixture builds, so a run against one of those "
            "would find no fixture for a reason that has nothing to do with the reader. "
            "Repackage with tools/ue-sample/repackage.py." % FIXTURE)

    # ---- one game at a time. launch_dumpertest.py's guard is BLIND to UE4 images ----
    for other in ("DumperTest.exe", "DumperTest-Win64-Shipping.exe", "DumperTest58.exe",
                  "DumperTest58-Win64-Shipping.exe", "UE423_Flying.exe", "UE427_3rdPerson.exe",
                  "UE423_Flying-Win64-Shipping.exe", "UE427_3rdPerson-Win64-Shipping.exe"):
        p = running(other)
        if p:
            raise SystemExit("REFUSING: %s is already running (pid %s). The pipe is "
                             "single-instance, so every command would go to THAT process."
                             % (other, p))

    proc = subprocess.Popen([str(exe), "-windowed", "-ResX=1280", "-ResY=720"],
                            cwd=str(exe.parent), creationflags=DETACHED)
    print("launched   : pid %d, waiting %ds" % (proc.pid, a.wait))
    time.sleep(a.wait)
    if not running(exe.name):
        raise SystemExit("the game exited during load")

    # ⭐ inject BY PID, not by name -- the pid we launched is the only one we may measure.
    r = subprocess.run([sys.executable, str(HERE / "inject.py"), "--pid", str(proc.pid)],
                       capture_output=True, text=True, encoding="utf-8", errors="replace",
                       cwd=str(ROOT))
    print((r.stdout or "").strip().splitlines()[-1] if r.stdout else r.stderr)
    if r.returncode != 0:
        raise SystemExit("inject failed: %s%s" % (r.stdout, r.stderr))

    init = call("init")
    if not init.get("ok"):
        raise SystemExit("init failed: %s" % init)
    call("trigger_scan")
    for _ in range(24):
        time.sleep(5)
        p = call("get_pointers")
        if p.get("gobjects", "0x0") != "0x0":
            break
    else:
        raise SystemExit("the offset scan never found GObjects")
    off = call("get_offsets")
    print("offsets    : use_fproperty=%r validated=%r  (4.23 -> False = the UProperty path)"
          % (off.get("use_fproperty"), off.get("validated")))

    fails: list[str] = []

    # ---- BEFORE: the fixture must exist as a CLASS but not as an INSTANCE -----------
    before = call("find_instances", {"class_name": FIXTURE, "limit": 16})
    binst = before.get("instances", [])
    print("before     : %d %s instance(s): %s"
          % (len(binst), FIXTURE, [i.get("name") for i in binst]))
    if not binst:
        raise SystemExit("the fixture CLASS is not even registered -- wrong binary after all")
    if non_cdo(binst):
        raise SystemExit("a non-CDO %s already exists BEFORE the Summon (%s). The AFTER state "
                         "would not be a difference." % (FIXTURE, non_cdo(binst)))

    # ---- TRAP 1: resolve a REAL CheatManager, never the class_name shortcut ---------
    cm = call("find_instances", {"class_name": "CheatManager", "limit": 16})
    live = non_cdo(cm.get("instances", []))
    print("cheatmgr   : %d total, %d non-CDO: %s"
          % (len(cm.get("instances", [])), len(live), [i.get("name") for i in live]))
    if not live:
        raise SystemExit(
            "no LIVE UCheatManager -- only the CDO. Summon on a CDO does nothing "
            "(GetOuterAPlayerController() is null) while still answering result=0, so this "
            "would be a guaranteed false pass. A CheatManager needs a PlayerController: is the "
            "package Development, and has the level actually started?")

    inv = call("invoke_function", {
        "instance_addr": live[0]["addr"],
        "func_name": "Summon",
        "parms_size": 16,                       # one FString {Data,Num,Max} at +0
        "str_params": [{"off": 0, "wide": True, "text": FIXTURE}],
        # direct_call stays FALSE: a spawn MUST run on the game thread.
    })
    print("summon     : %s" % json.dumps(inv)[:200])
    if not inv.get("ok"):
        fails.append("invoke_function(Summon) failed: %s" % inv)

    # ---- AFTER: the difference ------------------------------------------------------
    time.sleep(2)
    after = call("find_instances", {"class_name": FIXTURE, "limit": 16})
    spawned = non_cdo(after.get("instances", []))
    print("after      : %d %s instance(s), %d non-CDO: %s"
          % (len(after.get("instances", [])), FIXTURE, len(spawned),
             [i.get("name") for i in spawned]))
    if not spawned:
        print("\nSW9: FAIL -- Summon reported ok but no instance appeared.")
        return 1

    inst = spawned[0]
    print("spawned    : %s  %s" % (inst.get("name"), inst.get("addr")))

    w = call("walk_instance", {"addr": inst["addr"], "class_addr": inst["class_addr"],
                               "array_limit": 8})
    if not w.get("ok"):
        raise SystemExit("walk_instance failed: %s" % w)
    f = {x.get("name"): x for x in w.get("fields", [])}

    for n in ("Multicast_Inline", "Del_Unicast", "Arr_MulticastDelegates"):
        if n not in f:
            fails.append("%s missing from the walk" % n)
    if fails:
        print("\nSW9: FAIL")
        for x in fails:
            print("  - %s" % x)
        return 1

    mi = f["Multicast_Inline"].get("value", "")
    du = f["Del_Unicast"].get("value", "")
    arr = f["Arr_MulticastDelegates"]
    elems = [e.get("v", "") for e in arr.get("elements", [])]
    esz = arr.get("array_elem_size")
    print("Multicast_ : %r" % mi)
    print("Del_Unicast: %r" % du)
    print("Arr elems  : elem_size=%r  %r" % (esz, elems))
    print("delegate_pad on the wire: scalar=%r multicast=%r  (INFORMATIONAL -- Fern emits it "
          "only when non-zero, so absence is vacuous on UE4)"
          % (f["Del_Unicast"].get("delegate_pad"), f["Multicast_Inline"].get("delegate_pad")))

    if PROBE not in mi:
        fails.append("Multicast_Inline does not name %s -- got %r" % (PROBE, mi))
    if PROBE not in du:
        fails.append("Del_Unicast does not name %s -- got %r" % (PROBE, du))
    # TRAP 3: recognition + indexing, not the pad.
    if esz != 16:
        fails.append("array_elem_size is %r, expected 16 (UE4 has no access detector, so the "
                     "multicast element is a bare TArray header)" % esz)
    # TRAP 4: BOTH halves.
    if len(elems) != 2:
        fails.append("expected 2 array elements, got %d" % len(elems))
    else:
        if elems[0] != "(0 bindings)":
            fails.append("element [0] must be genuinely empty -- got %r" % elems[0])
        if PROBE not in elems[1]:
            fails.append("element [1] does not name %s -- got %r. A wrong stride reads [1] from "
                         "inside [0] and reports both empty." % (PROBE, elems[1]))

    print("\nreaders actually exercised by this run (by wire array_inner_type):")
    print("  Multicast_Inline       -> %s scalar handler" % f["Multicast_Inline"].get("type"))
    print("  Del_Unicast            -> %s scalar handler" % f["Del_Unicast"].get("type"))
    print("  Arr_MulticastDelegates -> ReadMulticastDelegateArrayElements (inner=%s)"
          % arr.get("array_inner_type"))
    print("  NOT exercised: ReadDelegateArrayElements (Phase J, the sixth D4b site) and the "
          "sparse walker -- this fixture declares no TArray<FScriptDelegate> and no sparse "
          "delegate.")

    print()
    if fails:
        print("SW9: FAIL")
        for x in fails:
            print("  - %s" % x)
        return 1
    print("SW9: PASS -- on UE %s.%s (use_fproperty=%r), an actor the ENGINE spawned and bound in "
          "BeginPlay reads back with %r on the scalar, the multicast and array element [1], "
          "while element [0] stays genuinely empty."
          % (a.engine[0], a.engine[1:], off.get("use_fproperty"), PROBE))
    print("\nUI ARM -- connect UE5DumpUI, Live Walker on %s, and require the delegate rows to "
          "name %s. The Value cell is a fixed 200px TextBlock with no ellipsis, so read the "
          "TOOLTIP, not the cell." % (inst["addr"], PROBE))
    if not a.keep:
        print("(pass --keep to leave the game up for that arm)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
