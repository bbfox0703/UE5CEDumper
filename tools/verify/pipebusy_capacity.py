"""PIPEBUSY: at capacity the pipe log must say it ONCE, not once per second.

    py pipebusy_capacity.py

WAS: with all `kMaxPipeInstances = 3` instances in use, the accept loop's
`CreateNamedPipe` fails `ERROR_PIPE_BUSY` (231) every second and used to
`LOG_ERROR("PipeServer: CreateNamedPipe failed …")` each time -- **1,826 ERROR lines
in ~31.5 min measured on one Avowed session**, evicting real diagnostics as the 8 MB
log rotated, and naming the wrong thing (busy is not broken).

NOW: `Voll` (header-only policy) special-cases `ERROR_PIPE_BUSY` -- ONE info on the
transition INTO at-capacity, silence while it holds, ONE info on recovery. Any other
errno still ERRORs every time.

⚠ The register otherwise forbids running `pipe_client.py` beside the UI. **This rig
needs no UI**: it opens three raw connections itself, which fills the pool exactly the
same way and keeps the forbidden combination off the table entirely.

WHAT MAKES IT A REAL CHECK RATHER THAN A GREP
  * the at-capacity line must appear **exactly once**, not merely "at least once" --
    the defect was repetition, so a count of 1 is the whole assertion;
  * `CreateNamedPipe failed` must appear **zero** times while at capacity;
  * releasing one client must produce **exactly one** recovery line -- proving the
    latch resets rather than sticking;
  * and the non-regression half is free: every single-client session logged earlier
    tonight must contain NO at-capacity line at all. That is checked over the other
    game log folders, so "it only fires when actually at capacity" is measured across
    many sessions rather than asserted.
"""
import pathlib
import re
import sys
import time

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
from pipe_client import PipeClient                        # noqa: E402

LOGROOT = pathlib.Path.home() / "AppData/Local/UE5CEDumper/Logs"
AT_CAP = "pipe instances in use"
FREED = "slot freed, resuming accept"
OLD_SPAM = "CreateNamedPipe failed"


def count(txt, needle):
    return sum(1 for l in txt.splitlines() if needle in l)


def main(logdir="DumperTest", hold=75):
    log = LOGROOT / logdir / "pipe-0.log"
    if not log.is_file():
        print(f"pipebusy: FAILED -- {log} missing; inject into {logdir} first")
        return 1
    before = log.read_text(encoding="utf-8", errors="replace")
    base_cap, base_freed, base_spam = (count(before, AT_CAP), count(before, FREED),
                                       count(before, OLD_SPAM))
    print(f"baseline in pipe-0.log: at-capacity={base_cap} freed={base_freed} "
          f"CreateNamedPipe-failed={base_spam}")

    clients = []
    try:
        for i in range(3):
            c = PipeClient(timeout=60.0).connect()
            r = c.request("get_pointers")
            clients.append(c)
            print(f"  client {i+1}/3 connected (build {r.get('build_number')})")
        print(f"  pool is full; holding {hold}s ...")
        time.sleep(hold)

        txt = log.read_text(encoding="utf-8", errors="replace")
        cap = count(txt, AT_CAP) - base_cap
        spam = count(txt, OLD_SPAM) - base_spam
        print(f"\n--- step 1: while at capacity for {hold}s ---")
        for l in txt.splitlines():
            if AT_CAP in l:
                print("   " + l.strip()[:150])
        print(f"  at-capacity lines : {cap}   (expect EXACTLY 1)")
        print(f"  'CreateNamedPipe failed' lines: {spam}   (expect 0; the old bug was ~1/s, "
              f"so {hold}s would have produced ~{hold})")
        ok = (cap == 1 and spam == 0)
    finally:
        for c in clients:
            c.close()
    time.sleep(8)

    txt = log.read_text(encoding="utf-8", errors="replace")
    freed = count(txt, FREED) - base_freed
    print(f"\n--- step 2: after releasing the clients ---")
    for l in txt.splitlines():
        if FREED in l:
            print("   " + l.strip()[:150])
    print(f"  recovery lines: {freed}   (expect EXACTLY 1)")
    ok = ok and (freed == 1)

    print("\n--- step 3 (NON-REGRESSION): single-client sessions must never say it ---")
    bad = []
    checked = 0
    for d in sorted(LOGROOT.iterdir()):
        p = d / "pipe-0.log"
        if not p.is_dir() and p.is_file() and d.name != logdir:
            checked += 1
            if AT_CAP in p.read_text(encoding="utf-8", errors="replace"):
                bad.append(d.name)
    print(f"  other log folders checked: {checked}   with an at-capacity line: {len(bad)} {bad}")
    ok = ok and not bad

    print(f"\nPIPEBUSY: {'PASS' if ok else 'FAIL'}")
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main(*(sys.argv[1:2] or ["DumperTest"])))
