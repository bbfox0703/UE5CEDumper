r"""SW5 — the two push-result branches nothing could reach: AMBER and RED.

    py tools/verify/sw5_push_branches.py install --mode refuse-navigate   # -> AMBER
    <restart Cheat Engine, click "Disassemble in CE" in the Property Xref dialog>
    py tools/verify/sw5_push_branches.py read
    py tools/verify/sw5_push_branches.py uninstall

THE ROW. `PropertyXrefDialog.OnDisassembleClicked` awaits two independent `Task<bool>`s and
branches on the pair (`:501-519`):

    recorded && navigated -> GREEN   "Pushed <fn> → CE disassembler @ <addr>"
    recorded              -> AMBER   "Added <fn> to the CE table @ <addr>, but could not open
                                      the disassembler there — find the record in Cheat Engine
                                      and browse to it manually."
    else                  -> RED     "CE refused the push for <fn> — nothing was added to the
                                      table. Is Cheat Engine still open with the AOBMaker
                                      plugin loaded?"

Only GREEN had ever been seen; before the fix all three produced the same green label.

⛔ AND "JUST CLOSE CHEAT ENGINE" DOES NOT PRODUCE RED — verified in source, and it is why this
harness exists rather than a taskkill. `_btnDisasm` is enabled only while
`SharedAobMaker?.IsAvailable == true` (`:387`) and the handler re-reads that cached flag (`:471`),
so with CE gone the button is dead. (`IsAvailable` is refreshed only by user-triggered probes and
this dialog is MODAL, so a stale `true` is reachable — killing CE with the dialog open. That gap
was a finding of its own and the handler now REPORTS it, but with a different message: "Cheat
Engine is not reachable — nothing was pushed", which is NOT the RED branch under test.)

⭐ THE MECHANISM. Both plugin handlers run their work through CE's plain Lua global
`synchronize` — the record via `LuaDoBuffer` with chunkname `=AOBMakerCEBridge`
(`pipe_server.cpp:1063-1078`), the navigation via `luaL_dostring` whose chunkname IS the script
text and contains the literal `DisassemblerView` (`:912-918`). `synchronize` is registered with
`lua_register` (`LuaHandler.pas:16290`) so it is overwritable, and the plugin's Lua state is a
`lua_newthread` of the main VM (`LuaHandler.pas:188`) sharing `_G`. A wrapper installed from
autorun is therefore seen by the pipe worker, and `debug.getinfo(2,"S").source` tells the two
callers apart. **The real plugin does the real work, minus the one call we refuse** — this is not
a stand-in server replaying a transcript.

⭐ AND CE STAYS ALIVE, which is what makes the RED branch's own claim checkable. "Nothing was
added to the table" is only an assertion if something can count the table; the harness samples
`getAddressList().Count` on every `synchronize` and writes it to the log, so CE itself says
whether a record appeared.

⛔ THE HARNESS IS INERT WITHOUT ITS JOB FILE. It lives in the user's real Cheat Engine install;
`uninstall` removes both files. Always run it.
"""
from __future__ import annotations

import argparse
import io
import os
import re
import shutil
import subprocess
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
ROOT = HERE.parent.parent
SRC_LUA = HERE / "ce" / "ue5-sw5-synchronize-split.lua"
CE_DIR = Path(r"C:\Program Files\Cheat Engine")
AUTORUN = CE_DIR / "autorun" / "custom"
DST_LUA = AUTORUN / "ue5-sw5-synchronize-split.lua"
JOB = AUTORUN / "ue5-sw5-job.txt"
OUTDIR = ROOT / "out" / "sw5"
LOG = OUTDIR / "sw5-ce.log"

MODES = ("refuse-navigate", "refuse-record")
EXPECT = {"refuse-navigate": "AMBER", "refuse-record": "RED"}


def ce_running() -> list[str]:
    r = subprocess.run(["tasklist", "/FO", "CSV", "/NH"], capture_output=True, text=True,
                       encoding="utf-8", errors="replace")
    out = []
    for ln in (r.stdout or "").splitlines():
        m = re.match(r'^"([^"]+)"', ln.strip())
        if m and "cheatengine" in m.group(1).lower():
            out.append(m.group(1))
    return out


def install(a) -> int:
    if not SRC_LUA.is_file():
        raise SystemExit("missing %s" % SRC_LUA)
    if not AUTORUN.is_dir():
        raise SystemExit("no %s -- is Cheat Engine installed here?" % AUTORUN)
    OUTDIR.mkdir(parents=True, exist_ok=True)
    if LOG.exists():
        LOG.unlink()
    shutil.copy2(SRC_LUA, DST_LUA)
    io.open(JOB, "w", encoding="utf-8").write("%s\n%s\n" % (a.mode, str(OUTDIR)))
    print("installed  : %s" % DST_LUA)
    print("job        : mode=%s  outdir=%s" % (a.mode, OUTDIR))
    print("expect     : the %s branch" % EXPECT[a.mode])
    running = ce_running()
    if running:
        print("\n⚠ Cheat Engine is ALREADY RUNNING (%s). autorun scripts run at STARTUP, so "
              "this harness is not armed in that process -- close it and start it again."
              % ", ".join(running))
    print("\nNEXT: start Cheat Engine, then in UE5DumpUI open a Property Xref dialog "
          "(Class Struct ▸ Find Class Funcs), select a row, and click \"Disassemble in CE\".")
    print("THEN: py tools/verify/sw5_push_branches.py read")
    return 0


def read(a) -> int:
    if not JOB.is_file():
        raise SystemExit("no job file -- run `install` first")
    mode = io.open(JOB, encoding="utf-8").read().splitlines()[0].strip()
    if not LOG.is_file():
        print("SW5: FAIL")
        print("  - %s was never written. The harness did not run: autorun scripts are read at "
              "CE STARTUP, so Cheat Engine must be restarted AFTER install." % LOG)
        return 1

    lines = [l.rstrip("\n") for l in io.open(LOG, encoding="utf-8", errors="replace")]
    for l in lines:
        print("  %s" % l)

    fails: list[str] = []
    if not any("harness armed" in l for l in lines):
        fails.append("no 'harness armed' line -- the job file was not read, so nothing was "
                     "wrapped and any absence below means nothing")
    if not any("wrapper installed" in l for l in lines):
        fails.append("the wrapper was never installed on the global synchronize")

    calls = [l for l in lines if "caller=" in l]
    refused = [l for l in calls if "action=refused" in l]
    passed = [l for l in calls if "action=passed" in l]
    want = "navigate" if mode == "refuse-navigate" else "record"

    print("\nsynchronize calls seen : %d (%d passed, %d refused)"
          % (len(calls), len(passed), len(refused)))
    if not calls:
        fails.append("the plugin never called synchronize -- the click did not reach CE at "
                     "all, so neither branch was exercised")
    if not any(("caller=%s" % want) in l and "action=refused" in l for l in refused):
        fails.append("no REFUSED %s call. The branch under test was not produced." % want)
    other = "record" if want == "navigate" else "navigate"
    if not any(("caller=%s" % other) in l and "action=passed" in l for l in passed):
        fails.append("the %s call did not PASS. Both halves must run for the branch to be the "
                     "one under test -- if both failed this is the RED branch regardless of "
                     "mode." % other)

    # The RED branch's own claim, checked against CE rather than assumed.
    counts = [int(m.group(1)) for m in
              (re.search(r"records=(-?\d+)", l) for l in calls) if m]
    if counts:
        print("addresslist Count across the calls: %r" % counts)
        if mode == "refuse-record" and len(set(counts)) > 1:
            fails.append("the record count CHANGED (%r) while the record push was refused -- "
                         "the RED branch claims 'nothing was added to the table' and something "
                         "was" % counts)

    print()
    if fails:
        print("SW5 (%s -> %s): FAIL" % (mode, EXPECT[mode]))
        for f in fails:
            print("  - %s" % f)
        return 1
    print("SW5 (%s): PASS -- the %s call was refused inside CE's own Lua while the %s call "
          "passed, which is exactly the Task<bool> pair the %s branch requires. The real plugin "
          "did the real work; only the one call was refused."
          % (mode, want, other, EXPECT[mode]))
    print("⚠ The UI-side half is a SCREENSHOT of the dialog's status label -- record which "
          "string appeared, and check it against the branch above.")
    return 0


def uninstall(a) -> int:
    n = 0
    for p in (JOB, DST_LUA):
        if p.exists():
            p.unlink()
            print("removed    : %s" % p)
            n += 1
    if not n:
        print("nothing to remove")
    print("⚠ Cheat Engine keeps the wrapper until it is RESTARTED.")
    return 0


def main() -> int:
    ap = argparse.ArgumentParser()
    sub = ap.add_subparsers(dest="cmd", required=True)
    i = sub.add_parser("install")
    i.add_argument("--mode", required=True, choices=MODES)
    sub.add_parser("read")
    sub.add_parser("uninstall")
    a = ap.parse_args()
    return {"install": install, "read": read, "uninstall": uninstall}[a.cmd](a)


if __name__ == "__main__":
    sys.exit(main())
