"""Which titles' ProcessEvent hook actually validated? One grep per log folder.

    py pe_hook_survey.py

Answers the question the PEHOOK finding raised: the sample's hook never fired, so
which of the swept titles CAN carry an invoke-dependent row? The DLL logs its own
verdict, so this needs no relaunch -- every folder under Logs\\ already holds it.

⚠ DATED PREMISE, still a useful tool. The DumperTest miss was fixed on 2026-08-18
(SIB-tolerant pattern; true slot +0x268, not the version table's 0x220), so a
survey of logs written by a NEWER DLL should no longer report it. Logs predating
that build still will -- read the build stamp before drawing a conclusion.

Reads three independent markers rather than one, because they fail differently:
  * "pattern scan missed"  -> AOB detection fell back to a version-table guess
  * "VALIDATION FAILED"    -> the installed hook saw zero PE traffic in 1500 ms
  * "hook installed at"    -> a hook was installed at all
"""
import os
import pathlib
import re
import sys

LOGS = pathlib.Path(os.environ["LOCALAPPDATA"]) / "UE5CEDumper" / "Logs"


def main():
    rows = []
    for sub in sorted(LOGS.iterdir()):
        if not sub.is_dir():
            continue
        texts = []
        for f in sub.glob("init-*.log"):
            try:
                texts.append(f.read_text(encoding="utf-8", errors="replace"))
            except OSError:
                pass
        t = "\n".join(texts)
        if "GameThreadDispatch" not in t and "DetectProcessEvent" not in t:
            continue
        failed = "VALIDATION FAILED" in t
        missed = "pattern scan missed" in t
        installed = "hook installed at" in t
        m = re.search(r"DetectProcessEvent[^\n]*", t)
        detail = m.group(0).split("] ")[-1] if m else ""
        rows.append((sub.name, failed, missed, installed, detail))

    if not rows:
        print("no title logged a ProcessEvent hook at all")
        return 0

    print(f"{'log folder':36s} {'validator':10s} {'AOB':8s} detail")
    print("-" * 110)
    for name, failed, missed, installed, detail in rows:
        v = "FAILED" if failed else ("passed" if installed else "-")
        a = "MISSED" if missed else "hit"
        print(f"{name:36s} {v:10s} {a:8s} {detail[:60]}")

    bad = [r[0] for r in rows if r[1]]
    good = [r[0] for r in rows if not r[1] and r[3]]
    print()
    print(f"validator FAILED ({len(bad)}): {', '.join(bad) or 'none'}")
    print(f"hook installed, no failure ({len(good)}): {', '.join(good) or 'none'}")
    return 0


if __name__ == "__main__":
    sys.stdout.reconfigure(encoding="utf-8")
    sys.exit(main())
