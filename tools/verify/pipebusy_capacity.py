"""PIPEBUSY: at capacity the pipe log must say it ONCE, not once per second.

    py pipebusy_capacity.py

WAS: with all `kMaxPipeInstances` instances in use (3 then), the accept loop's
`CreateNamedPipe` fails `ERROR_PIPE_BUSY` (231) every second and used to
`LOG_ERROR("PipeServer: CreateNamedPipe failed …")` each time -- **1,826 ERROR lines
in ~31.5 min measured on one Avowed session**, evicting real diagnostics as the 8 MB
log rotated, and naming the wrong thing (busy is not broken).

NOW: `Voll` (header-only policy) special-cases `ERROR_PIPE_BUSY` -- ONE info on the
transition INTO at-capacity, silence while it holds, ONE info on recovery. Any other
errno still ERRORs every time.

**This rig needs no UI**: it opens as many connections as the DLL takes, which fills the
pool exactly as a UI plus rigs would. How many is DERIVED, not assumed
([PIPE-PER-PROCESS-2026-11-06] Stage B raised 3 to 6, and a game with an older DLL keeps
its own): the DLL's log states the limit in force ("takes N clients at once", or the
start line's `maxInstances=N` before that line existed), and the rig then MEASURES it --
with N connected, one more open must find every instance taken (WaitNamedPipeW, which
does not connect).

WHAT MAKES IT A REAL CHECK RATHER THAN A GREP
  * the at-capacity line must appear **exactly once**, not merely "at least once" --
    the defect was repetition, so a count of 1 is the whole assertion;
  * `CreateNamedPipe failed` must appear **zero** times while at capacity;
  * releasing one client must produce **exactly one** recovery line -- proving the
    latch resets rather than sticking;
  * and the non-regression half is free: across the other game log folders, an
    at-capacity line may appear ONLY when the session really was full -- the last
    "Client connected (conns=K)" before it had K equal to the limit in force then. So "it
    only fires when actually at capacity" is measured across many sessions rather than
    asserted. (It first demanded no such line at all, which assumed every other session
    had one client; a deliberately filled pipe, as the Stage A checks did, is legitimate.)
"""
import pathlib
import re
import sys
import time

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
from pipe_client import PipeClient, pipe_is_busy          # noqa: E402

LOGROOT = pathlib.Path.home() / "AppData/Local/UE5CEDumper/Logs"
AT_CAP = "pipe instances in use"
FREED = "slot freed, resuming accept"
OLD_SPAM = "CreateNamedPipe failed"


def count(txt, needle):
    return sum(1 for l in txt.splitlines() if needle in l)


def limit_in_force(txt):
    """The most clients the DLL says its pipe takes, from its latest statement, or None when it states none."""
    stated = list(re.finditer(r"takes (\d+) clients at once", txt)) or list(re.finditer(r"maxInstances=(\d+)", txt))
    return int(stated[-1].group(1)) if stated else None


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

    n = limit_in_force(before)
    if n is None:
        print(f"pipebusy: FAILED -- {log} states no pipe limit; is the DLL injected?")
        return 1
    print(f"the DLL states its pipe takes {n} clients at once")

    clients = []
    try:
        for i in range(n):
            c = PipeClient(timeout=60.0).connect()
            r = c.request("get_pointers")
            clients.append(c)
            print(f"  client {i+1}/{n} connected (build {r.get('build_number')})")
        # The measurement behind the stated number: with n connected, one more open finds every instance taken.
        time.sleep(1.5)   # the accept loop retries its next create once a second
        full = pipe_is_busy(clients[0].pipe)
        print(f"  open #{n + 1}: every instance taken = {full}   (expect True)")
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
        ok = (cap == 1 and spam == 0 and full)
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

    print("\n--- step 3 (NON-REGRESSION): an at-capacity line only when the session was full ---")
    bad = []
    checked = full_lines = 0
    for d in sorted(LOGROOT.iterdir()):
        p = d / "pipe-0.log"
        if not p.is_dir() and p.is_file() and d.name != logdir:
            checked += 1
            limit = conns = None
            for line in p.read_text(encoding="utf-8", errors="replace").splitlines():
                m = re.search(r"maxInstances=(\d+)|takes (\d+) clients at once", line)
                if m:
                    limit = int(m.group(1) or m.group(2))
                m = re.search(r"Client connected \(conns=(\d+)\)", line)
                if m:
                    conns = int(m.group(1))
                if AT_CAP in line:
                    if limit is not None and conns == limit:
                        full_lines += 1
                    else:
                        bad.append(f"{d.name}: conns={conns} limit={limit}: {line.strip()[:90]}")
    print(f"  other log folders checked: {checked}; at-capacity lines in full sessions: {full_lines}; "
          f"anywhere else: {len(bad)}")
    for b in bad[:5]:
        print("   " + b)
    ok = ok and not bad

    print(f"\nPIPEBUSY: {'PASS' if ok else 'FAIL'}")
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main(*(sys.argv[1:2] or ["DumperTest"])))
