"""AC10: kill the game while a Dump All is streaming, and prove the kill was MID-stream.

    py tools/verify/ac10_kill_midstream.py <partial-glob> <process.exe> [--min-bytes 200000]

WHY IT WATCHES THE .partial RATHER THAN SLEEPING
    "Kill it mid-stream" is the whole test, and a fixed sleep cannot establish it: fire
    early and the dump has not started (nothing is in flight, so the disconnect is an
    ordinary idle one); fire late and it already finished (the file is published and the
    test measured nothing). Both look like a pass in the UI.

    The dump streams to `<chosen name>.jsonl.partial` and is renamed to the real name
    only after the trailing summary line is written, so the .partial existing AND having
    grown past a threshold is direct evidence that bytes are moving. That is the trigger.

    It records the partial's size at the instant of the kill, which is what makes the
    result checkable afterwards: a non-trivial size proves in-flight, and the file's
    fate after the kill (deleted, or left as .partial, but NEVER published under the
    final name) is what the row is actually asserting.
"""
import pathlib
import subprocess
import sys
import time

ROOT = pathlib.Path(__file__).resolve().parents[2]


def say(s):
    enc = sys.stdout.encoding or "utf-8"
    sys.stdout.write(s.encode(enc, "replace").decode(enc, "replace") + "\n")
    sys.stdout.flush()


def main():
    if len(sys.argv) < 3:
        say(__doc__)
        return 2
    globpat, proc = sys.argv[1], sys.argv[2]
    min_bytes = 200_000
    if "--min-bytes" in sys.argv:
        min_bytes = int(sys.argv[sys.argv.index("--min-bytes") + 1])

    deadline = time.time() + 180
    hit = None
    say("watching %s for a .partial past %d bytes (kill target: %s)" % (globpat, min_bytes, proc))
    while time.time() < deadline:
        for p in ROOT.glob(globpat):
            try:
                n = p.stat().st_size
            except OSError:
                continue
            if n >= min_bytes:
                hit = (p, n)
                break
        if hit:
            break
        time.sleep(0.05)

    if not hit:
        say("TIMEOUT: no .partial reached %d bytes -- nothing was killed" % min_bytes)
        return 1

    p, n = hit
    say("MID-STREAM: %s is %d bytes -- killing %s NOW" % (p.name, n, proc))
    r = subprocess.run(["taskkill", "/F", "/IM", proc], capture_output=True, text=True,
                       errors="replace")
    say("taskkill rc=%d %s" % (r.returncode, (r.stdout or r.stderr).strip()))
    time.sleep(3)
    say("3 s after the kill:")
    say("  .partial still present : %s (%s bytes)"
        % (p.exists(), p.stat().st_size if p.exists() else "-"))
    final = pathlib.Path(str(p)[: -len(".partial")])
    say("  FINAL name published   : %s   <-- must be False" % final.exists())
    return 0


if __name__ == "__main__":
    sys.exit(main())
