"""Suspend / resume a process by name -- V7's prescribed way to force a dead refresh.

    py suspend.py suspend DumperTest
    py suspend.py resume  DumperTest

The register says to force V7's failure by suspending the GAME PROCESS rather
than by destroying an actor: destroying one changes what the walk should return,
while suspending leaves the object graph intact and only stops the DLL answering,
which is exactly the "refresh could not complete" case the salmon line is for.

Always prints the resulting suspend state so "I called it" is never mistaken for
"it happened", and resume is idempotent -- NtResumeProcess is safe to over-call.
"""
import ctypes
import subprocess
import sys

ntdll = ctypes.WinDLL("ntdll")
k32 = ctypes.WinDLL("kernel32", use_last_error=True)
PROCESS_SUSPEND_RESUME = 0x0800
PROCESS_QUERY_LIMITED_INFORMATION = 0x1000


def pids(match):
    out = subprocess.run(["tasklist", "/FO", "CSV", "/NH"],
                         capture_output=True, text=True, errors="replace").stdout
    found = []
    for line in out.splitlines():
        parts = [p.strip('"') for p in line.split('","')]
        if len(parts) >= 2 and match.lower() in parts[0].lower():
            found.append((int(parts[1]), parts[0]))
    return found


def act(match, resume):
    targets = pids(match)
    if not targets:
        print(f"no process matching {match!r}")
        return 1
    for pid, name in targets:
        h = k32.OpenProcess(PROCESS_SUSPEND_RESUME | PROCESS_QUERY_LIMITED_INFORMATION,
                            False, pid)
        if not h:
            print(f"  {name} ({pid}): OpenProcess FAILED err={ctypes.get_last_error()}")
            continue
        st = ntdll.NtResumeProcess(h) if resume else ntdll.NtSuspendProcess(h)
        k32.CloseHandle(h)
        print(f"  {name} ({pid}): {'RESUMED' if resume else 'SUSPENDED'} (nt=0x{st & 0xFFFFFFFF:X})")
    return 0


if __name__ == "__main__":
    sys.stdout.reconfigure(encoding="utf-8")
    cmd = sys.argv[1] if len(sys.argv) > 1 else ""
    name = sys.argv[2] if len(sys.argv) > 2 else ""
    if cmd in ("suspend", "resume") and name:
        sys.exit(act(name, cmd == "resume"))
    print(__doc__)
