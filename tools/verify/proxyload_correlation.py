"""PROXYLOAD: measure the static-import BYPASS heuristic against real load evidence.

    py proxyload_correlation.py

THE CLAIM UNDER TEST (`[PROXYLOAD-2026-08-17]`). A title that **statically imports** the
proxy's base name is said to get the loader satisfying that import from an
already-mapped copy (an overlay/launcher maps it early), so it never searches the app
dir and **our proxy is silently ignored**. The row records the correlation as "3 for 3"
and is explicit that the mechanism **FITS but is UNTESTED**, and that `KnownDLLs` was
already refuted as the explanation.

WHAT THIS DOES. For every installed UE title it reads the exe's import table
(`tools/pe/pe_imports_exports.py`, the same reader the row names) for all four proxy
base names, then cross-references two independent facts already on disk:

  * which proxy flavour is actually DEPLOYED next to the exe, and
  * whether the DLL ever LOADED there -- i.e. whether
    `%LOCALAPPDATA%\\UE5CEDumper\\Logs\\<exe-base-name>` exists, which is exactly the
    join the panel's new "Loaded?" column uses (`ProcessLogFolderName`, mirroring
    `Sein.cpp InitProcessMirror`).

⚠ WHAT THIS CAN AND CANNOT SETTLE. It is **observational**, not a controlled
experiment: nobody deployed each flavour to each title on purpose. A title that imports
its deployed flavour AND has a log folder is a **counter-example** to the heuristic and
is real evidence; a title with no log folder may simply never have been launched, so
absence is NOT evidence of bypass. The rig labels those separately rather than counting
them, because "not observed" and "bypassed" are the two things this row exists to stop
people conflating.

The row already names one counter-example — OCTOPATH imports winmm yet its winmm proxy
WORKS — so the heuristic is known to false-positive. The useful output here is the
BREADTH of that: how often "imports it" coincides with "loaded anyway".
"""
import pathlib
import subprocess
import sys

ROOT = pathlib.Path(__file__).resolve().parents[2]
LOGROOT = pathlib.Path.home() / "AppData/Local/UE5CEDumper/Logs"
LIBS = [pathlib.Path(r"C:\Program Files (x86)\Steam\steamapps\common"),
        pathlib.Path(r"D:\SteamLibrary\steamapps\common")]
PROXIES = ("version", "dxgi", "winmm", "dinput8")


def say(s):
    enc = sys.stdout.encoding or "utf-8"
    sys.stdout.write(s.encode(enc, "replace").decode(enc, "replace") + "\n")


def imports_of(exe):
    """Set of proxy base names the exe statically imports."""
    r = subprocess.run([sys.executable, str(ROOT / "tools/pe/pe_imports_exports.py"),
                        "imports", str(exe)],
                       capture_output=True, text=True, errors="replace")
    if r.returncode != 0:
        return None                      # unreadable -> report, never treat as "none"
    low = r.stdout.lower()
    return {p for p in PROXIES if f"{p}.dll" in low}


def main():
    rows = []
    for lib in LIBS:
        if not lib.is_dir():
            continue
        for d in sorted(lib.iterdir()):
            if not d.is_dir():
                continue
            for exe in list(d.rglob("*-Win64-Shipping.exe"))[:1]:
                deployed = sorted(q.name[:-4].lower() for q in exe.parent.glob("*.dll")
                                  if q.name[:-4].lower() in PROXIES)
                if not deployed:
                    continue             # only titles we have actually deployed to
                imps = imports_of(exe)
                logdir = LOGROOT / exe.stem
                rows.append((d.name, exe.stem, deployed, imps, logdir.is_dir()))

    say(f"{'title':<30}{'deployed':<10}{'imports that name?':<20}{'log folder':<12} verdict")
    say("-" * 104)
    counter = same = never = unread = 0
    for title, stem, dep, imps, loaded in rows:
        flavour = dep[0]
        if imps is None:
            verdict, unread = "exe unreadable", unread + 1
            imp_s = "?"
        else:
            imp_s = "YES" if flavour in imps else "no"
            if flavour in imps and loaded:
                verdict = "*** COUNTER-EXAMPLE: imported AND loaded ***"
                counter += 1
            elif flavour in imps and not loaded:
                verdict = "consistent w/ bypass (or never launched)"
                never += 1
            else:
                verdict = "not imported -> ours expected to win"
                same += 1
        say(f"{title[:29]:<30}{flavour:<10}{imp_s:<20}{str(loaded):<12} {verdict}")

    say("")
    say(f"titles with a deployed proxy : {len(rows)}")
    say(f"  imported AND loaded anyway : {counter}   <- direct counter-examples to the heuristic")
    say(f"  imported, no log folder    : {never}   <- CONSISTENT with bypass, but 'never launched'")
    say(f"                                       explains it equally well. NOT evidence.")
    say(f"  not imported               : {same}")
    if unread:
        say(f"  unreadable exes            : {unread}")
    say("")
    say("⇒ The heuristic must stay worded as a heuristic: an import is not a prediction of")
    say("  bypass. The 'Loaded?' signal, not the import table, is the per-game source of truth.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
