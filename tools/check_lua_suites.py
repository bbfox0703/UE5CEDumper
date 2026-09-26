#!/usr/bin/env python3
"""Run the self-contained Lua suites (scripts/tests/*.lua) on Cheat Engine's OWN Lua VM, when it is built here.

    py tools/check_lua_suites.py          # run them, or SKIP (exit 0) where the host is not built
    py tools/check_lua_suites.py --list   # print which suites run and which are excluded, and why

WHY (skeptic T11, [PATH-SHAPE-2026-09-25]). The suites are the only behavioural tests of the .CT and of the Lua the
UI emits, and no gate ran them: they ran only when someone remembered `py tools/verify/ce_lua53_host.py`. So a
revert of a .CT fix could pass every gate.

WHAT IT DOES NOT DO. It never BUILDS the host: that needs a local Cheat Engine install and MSVC, which is reaching
outside the repo (gates must not; CI has neither). Where out/ce_lua53/lua53ce.exe exists -- this machine, after
`py tools/verify/ce_lua53_host.py` -- the suites run and a failure fails the gate. Where it does not, the gate
reports SKIPPED and passes, and says how to enable it.

MACHINE-BOUND suites are excluded: they read real files outside the repo (a CE install, dist\\), which a gate must
not depend on. They stay runnable by hand through ce_lua53_host.py.
"""
from __future__ import annotations

import pathlib
import subprocess
import sys

sys.stdout.reconfigure(encoding="utf-8", errors="replace")

ROOT = pathlib.Path(__file__).resolve().parents[1]
HOST = ROOT / "out" / "ce_lua53" / "lua53ce.exe"
SUITES = ROOT / "scripts" / "tests"

# suite -> why it is not run by the gate
MACHINE_BOUND = {
    "dll_size_text_test.lua": "reads C:\\Program Files\\Cheat Engine\\UE5Dumper.dll and dist\\UE5Dumper.dll -- "
                              "real files outside the repo",
}


def suites():
    return sorted(p for p in SUITES.glob("*.lua") if p.name not in MACHINE_BOUND)


def main(argv):
    if "--list" in argv:
        for p in suites():
            print(f"  run   {p.name}")
        for n, why in sorted(MACHINE_BOUND.items()):
            print(f"  skip  {n}   ({why})")
        return 0
    if not HOST.exists():
        print(f"SKIPPED: CE's Lua host is not built here ({HOST.relative_to(ROOT)}). "
              "Build it with 'py tools/verify/ce_lua53_host.py' (needs a local Cheat Engine + MSVC).")
        return 0
    failed = []
    for p in suites():
        r = subprocess.run([str(HOST), str(p)], cwd=str(ROOT), capture_output=True, text=True,
                           encoding="utf-8", errors="replace", timeout=300)
        tail = (r.stdout.strip().splitlines() or ["(no output)"])[-1]
        print(f"  {'ok  ' if r.returncode == 0 else 'FAIL'}  {p.name:34} rc={r.returncode}  {tail}")
        if r.returncode != 0:
            failed.append(p.name)
    print(f"{len(suites()) - len(failed)}/{len(suites())} Lua suite(s) passed on CE's Lua VM"
          + (f"; excluded as machine-bound: {', '.join(sorted(MACHINE_BOUND))}" if MACHINE_BOUND else ""))
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
