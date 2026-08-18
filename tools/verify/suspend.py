"""Suspend / resume a process by name -- V7's prescribed way to force a dead refresh.

    py suspend.py suspend DumperTest
    py suspend.py resume  DumperTest

The register says to force V7's failure by suspending the GAME PROCESS rather
than by destroying an actor: destroying one changes what the walk should return,
while suspending leaves the object graph intact and only stops the DLL answering,
which is exactly the "refresh could not complete" case the salmon line is for.

    py suspend.py threads     Elliot
    py suspend.py suspend-tid Elliot 12345
    py suspend.py resume-tid  Elliot 12345

Always prints the resulting suspend state so "I called it" is never mistaken for
"it happened", and resume is idempotent -- NtResumeProcess is safe to over-call.

THREAD verbs exist because a WHOLE-PROCESS suspend is the wrong instrument for
the AA19 mailbox row: it stops Fern and Mimic too, so the DLL never even picks
the command up and the Lua reports status=0 ("never picked this up") instead of
the status=0xFF ("took it and wedged") branch under test. Freezing ONLY the UE
game thread leaves the pipe and the mailbox poller answering while
ProcessEvent dispatch is stuck -- which is the state that row describes.

`threads` ranks by creation time because the UE game thread IS the process main
thread (WinMain -> GuardedMain -> FEngineLoop::PreInit sets GGameThreadId on the
thread already running). Do NOT pick by lowest TID -- thread ids come from a
recycled global pool and are only loosely ordered.

Confirm you froze the right one by EFFECT, not by assumption: the DLL's
`get_diagnostics` must show game_thread.hook_fire_count frozen and
responsive=false WHILE the pipe itself still answers. If the pipe stops
answering you suspended the wrong thread -- resume immediately.

SuspendThread returns the PREVIOUS suspend count, which is the real state, so it
is printed rather than a hardcoded label.
"""
import ctypes
import subprocess
import sys

ntdll = ctypes.WinDLL("ntdll")
k32 = ctypes.WinDLL("kernel32", use_last_error=True)
PROCESS_SUSPEND_RESUME = 0x0800
PROCESS_QUERY_LIMITED_INFORMATION = 0x1000
THREAD_SUSPEND_RESUME = 0x0002
THREAD_QUERY_LIMITED_INFORMATION = 0x0800
TH32CS_SNAPTHREAD = 0x0004

# HANDLE is 64-bit; without these restypes ctypes defaults to c_int and
# truncates/sign-extends the handle. Harmless while handle values stay small,
# which is exactly why it survives untested.
k32.OpenProcess.restype = ctypes.c_void_p
k32.OpenThread.restype = ctypes.c_void_p
k32.CreateToolhelp32Snapshot.restype = ctypes.c_void_p
k32.SuspendThread.restype = ctypes.c_ulong
k32.ResumeThread.restype = ctypes.c_ulong


class THREADENTRY32(ctypes.Structure):
    _fields_ = [("dwSize", ctypes.c_ulong), ("cntUsage", ctypes.c_ulong),
                ("th32ThreadID", ctypes.c_ulong), ("th32OwnerProcessID", ctypes.c_ulong),
                ("tpBasePri", ctypes.c_long), ("tpDeltaPri", ctypes.c_long),
                ("dwFlags", ctypes.c_ulong)]


class FILETIME(ctypes.Structure):
    _fields_ = [("dwLowDateTime", ctypes.c_ulong), ("dwHighDateTime", ctypes.c_ulong)]


def _u64(ft):
    return (ft.dwHighDateTime << 32) | ft.dwLowDateTime


def thread_ids(pid):
    """TH32CS_SNAPTHREAD snapshots threads SYSTEM-WIDE -- the pid argument to
    CreateToolhelp32Snapshot is ignored for it, so the owner filter is mandatory."""
    snap = k32.CreateToolhelp32Snapshot(TH32CS_SNAPTHREAD, 0)
    if not snap or snap == -1:
        return []
    te = THREADENTRY32()
    te.dwSize = ctypes.sizeof(te)
    out = []
    ok = k32.Thread32First(snap, ctypes.byref(te))
    while ok:
        if te.th32OwnerProcessID == pid:
            out.append(te.th32ThreadID)
        ok = k32.Thread32Next(snap, ctypes.byref(te))
    k32.CloseHandle(snap)
    return out


def thread_times(tid):
    """(created, cpu_ms) or None. GetThreadTimes needs QUERY_LIMITED_INFORMATION."""
    h = k32.OpenThread(THREAD_QUERY_LIMITED_INFORMATION, False, tid)
    if not h:
        return None
    c, e, kt, ut = FILETIME(), FILETIME(), FILETIME(), FILETIME()
    ok = k32.GetThreadTimes(ctypes.c_void_p(h), ctypes.byref(c), ctypes.byref(e),
                            ctypes.byref(kt), ctypes.byref(ut))
    k32.CloseHandle(ctypes.c_void_p(h))
    if not ok:
        return None
    return _u64(c), (_u64(kt) + _u64(ut)) / 10000.0


def list_threads(match):
    targets = pids(match)
    if not targets:
        print(f"no process matching {match!r}")
        return 1
    for pid, name in targets:
        tids = thread_ids(pid)
        print(f"{name} ({pid}): {len(tids)} threads -- EARLIEST CREATED FIRST")
        rows = []
        for tid in tids:
            t = thread_times(tid)
            rows.append((t[0] if t else 1 << 63, tid, t[1] if t else -1.0))
        rows.sort()
        for i, (created, tid, cpu) in enumerate(rows[:12]):
            tag = "  <-- main thread (UE game thread)" if i == 0 else ""
            print(f"  tid={tid:<8} cpu={cpu:10.1f} ms{tag}")
    return 0


def act_tid(match, tid, resume):
    targets = pids(match)
    if not targets:
        print(f"no process matching {match!r}")
        return 1
    pid = targets[0][0]
    if tid not in thread_ids(pid):
        print(f"tid {tid} does not belong to pid {pid} -- refusing")
        return 1
    h = k32.OpenThread(THREAD_SUSPEND_RESUME, False, tid)
    if not h:
        print(f"OpenThread({tid}) FAILED err={ctypes.get_last_error()}")
        return 1
    hv = ctypes.c_void_p(h)
    if resume:
        prev = k32.ResumeThread(hv)
        # ResumeThread is the only one safe to over-call; drain to zero.
        while prev not in (0, 1, 0xFFFFFFFF):
            prev = k32.ResumeThread(hv)
    else:
        prev = k32.SuspendThread(hv)
    k32.CloseHandle(hv)
    if prev == 0xFFFFFFFF:
        print(f"tid {tid}: {'Resume' if resume else 'Suspend'}Thread FAILED "
              f"err={ctypes.get_last_error()}")
        return 1
    print(f"tid {tid}: {'RESUMED' if resume else 'SUSPENDED'} "
          f"(previous suspend count = {prev})")
    return 0


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
    if cmd == "threads" and name:
        sys.exit(list_threads(name))
    if cmd in ("suspend-tid", "resume-tid") and name and len(sys.argv) > 3:
        sys.exit(act_tid(name, int(sys.argv[3]), cmd == "resume-tid"))
    print(__doc__)
