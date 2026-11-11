r"""Several heavy commands at once: how many threads does the game gain? [PIPE-PER-PROCESS-2026-11-06] Stage B

    py tools/verify/worker_budget_live.py --pid <game pid> [--clients 4] [--rounds 8]

WHY. Stage B lets the pipe take 6 clients, so several heavy commands can walk GObjects at once. Each walk
(Aura::ParallelGObjectsScan) used to spawn ScanThreadCount() - 1 workers with no limit across walks: k walks put k
times that many threads in the game. The process-wide WorkerBudget holds the workers of every walk together to what
one walk alone spawns.

WHAT IT DOES. `--clients` connections (each its own PipeClient, its own DLL thread) each send `find_refs_to_uobject`
on GWorld `--rounds` times, all at once -- a full GObjects walk per call. A sampler counts the game's threads every
10 ms (Toolhelp snapshot) before, during and after. It prints the baseline, the peak, and the bound the budget
allows:

    peak - baseline <= budget + clients

(budget = what one walk spawns beyond its own thread, from the DLL's log; each walk that got workers also runs one
cancel watcher, hence + clients; a walk's own first chunk runs on its connection's thread, which already exists).
It also counts the DLL's "ParallelGObjectsScan: N of M threads" lines written during the run: walks the budget cut.

THE CONTROL. Run it against a DLL from before Stage B (build 3695): the same load must exceed the bound, or the run
proves nothing about the budget. `--expect over` says that is the expected outcome.

Exit codes: 0 the expectation held; 1 it did not; 2 nothing measurable (no pipe, no GWorld, no budget in the log).
"""
from __future__ import annotations

import argparse
import ctypes
import pathlib
import re
import sys
import threading
import time
from ctypes import wintypes

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
from pipe_client import PipeClient  # noqa: E402

LOGROOT = pathlib.Path.home() / "AppData/Local/UE5CEDumper/Logs"
CUT = re.compile(r"ParallelGObjectsScan: (\d+) of (\d+) threads")

k32 = ctypes.WinDLL("kernel32", use_last_error=True)
k32.CreateToolhelp32Snapshot.restype = wintypes.HANDLE
k32.CreateToolhelp32Snapshot.argtypes = [wintypes.DWORD, wintypes.DWORD]
TH32CS_SNAPTHREAD = 0x00000004
INVALID = wintypes.HANDLE(-1).value


class THREADENTRY32(ctypes.Structure):
    _fields_ = [("dwSize", wintypes.DWORD), ("cntUsage", wintypes.DWORD), ("th32ThreadID", wintypes.DWORD),
                ("th32OwnerProcessID", wintypes.DWORD), ("tpBasePri", wintypes.LONG),
                ("tpDeltaPri", wintypes.LONG), ("dwFlags", wintypes.DWORD)]


k32.Thread32First.argtypes = [wintypes.HANDLE, ctypes.POINTER(THREADENTRY32)]
k32.Thread32Next.argtypes = [wintypes.HANDLE, ctypes.POINTER(THREADENTRY32)]
k32.CloseHandle.argtypes = [wintypes.HANDLE]


def thread_count_toolhelp(pid: int) -> int:
    snap = k32.CreateToolhelp32Snapshot(TH32CS_SNAPTHREAD, 0)
    if snap == INVALID:
        return -1
    try:
        te = THREADENTRY32()
        te.dwSize = ctypes.sizeof(THREADENTRY32)
        n = 0
        ok = k32.Thread32First(snap, ctypes.byref(te))
        while ok:
            if te.th32OwnerProcessID == pid:
                n += 1
            ok = k32.Thread32Next(snap, ctypes.byref(te))
        return n
    finally:
        k32.CloseHandle(snap)


# NtQuerySystemInformation(SystemProcessInformation): one call lists every process with its thread count, far faster
# than a Toolhelp thread snapshot (measured: 7 Toolhelp samples in 2.4 s). x64 SYSTEM_PROCESS_INFORMATION offsets:
# NextEntryOffset 0x00, NumberOfThreads 0x04, NumberOfThreadsHighWatermark 0x14, UniqueProcessId 0x50.
ntdll = ctypes.WinDLL("ntdll")
ntdll.NtQuerySystemInformation.argtypes = [ctypes.c_ulong, ctypes.c_void_p, ctypes.c_ulong,
                                           ctypes.POINTER(ctypes.c_ulong)]
_spi_buf = ctypes.create_string_buffer(8 * 1024 * 1024)


def thread_counts(pid: int) -> tuple[int, int]:
    """(threads now, the process's thread high-water mark), or (-1, -1)."""
    ret = ctypes.c_ulong(0)
    if ntdll.NtQuerySystemInformation(5, _spi_buf, len(_spi_buf), ctypes.byref(ret)) != 0:
        return -1, -1
    raw, off = _spi_buf.raw, 0
    while True:
        nxt, nthreads = int.from_bytes(raw[off:off + 4], "little"), int.from_bytes(raw[off + 4:off + 8], "little")
        if int.from_bytes(raw[off + 0x50:off + 0x58], "little") == pid:
            return nthreads, int.from_bytes(raw[off + 0x14:off + 0x18], "little")
        if nxt == 0:
            return -1, -1
        off += nxt


def thread_count(pid: int) -> int:
    return thread_counts(pid)[0]


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    ap.add_argument("--pid", type=int, required=True)
    ap.add_argument("--clients", type=int, default=4)
    ap.add_argument("--rounds", type=int, default=8)
    ap.add_argument("--budget", type=int, default=None,
                    help="workers one walk spawns beyond its own thread; default: ScanThreadCount's rule, min(16, cores-2)-1")
    ap.add_argument("--expect", choices=["within", "over"], default="within")
    a = ap.parse_args()

    import os
    cores = os.cpu_count() or 1
    with PipeClient(pid=a.pid, timeout=120) as c:
        p = c.request("get_pointers")
        gworld = p.get("gworld") or p.get("gworld_addr")
        if gworld and str(gworld) not in ("0x0", "0"):
            c.request("find_refs_to_uobject", addr=str(gworld), max_results=1)   # warm-up: the DLL logs its pick
    module = str(p.get("module_name", "")).rsplit(".", 1)[0]
    if not gworld or str(gworld) in ("0x0", "0"):
        print("worker_budget: NOT MEASURABLE -- no GWorld in get_pointers")
        return 2
    print(f"game pid {a.pid} ({module}), build {p.get('build_number')}, objects {p.get('object_count')}, GWorld {gworld}")

    logs = sorted((LOGROOT / module).glob("*-0.log")) if module else []
    # The DLL states its pick from the CPU's shape ([PIPE-PER-PROCESS-2026-11-06]: performance cores less 2); a DLL
    # from before that used logical processors less 2, capped at 16.
    stated = None
    for f in logs:
        for m in re.finditer(r"a GObjects walk uses up to (\d+) threads", f.read_text(encoding="utf-8", errors="replace")):
            stated = int(m.group(1))
    if a.budget is not None:
        budget, source = a.budget, "--budget"
    elif stated is not None:
        budget, source = stated - 1, "the DLL's log"
    else:
        budget, source = max(1, min(16, cores - 2 if cores >= 3 else 1)) - 1, "the old rule (no statement in the log)"
    print(f"one walk spawns up to {budget} workers beyond its own thread (from {source})")
    sizes = {f: f.stat().st_size for f in logs}

    samples, stop = [], threading.Event()

    def sampler():
        while not stop.is_set():
            samples.append(thread_count(a.pid))
            time.sleep(0.01)

    # The baseline is taken with the clients CONNECTED: each connection has its own DLL thread, which exists whether or
    # not a walk runs, so it belongs to the baseline, not to what the walks add.
    clients = [PipeClient(pid=a.pid, timeout=300).connect() for _ in range(a.clients)]
    time.sleep(0.5)
    base = min(thread_count(a.pid) for _ in range(20))
    # The process's own record of its most threads ever: a peak the sampler cannot miss, when the load raises it.
    hwm_before = thread_counts(a.pid)[1]
    errors, done = [], [0]
    go = threading.Barrier(a.clients + 1)

    def load(cl):
        go.wait()
        for _ in range(a.rounds):
            r = cl.request("find_refs_to_uobject", addr=str(gworld), max_results=32)
            if r.get("ok") is False or r.get("error"):
                errors.append(r.get("error"))
            done[0] += 1

    st = threading.Thread(target=sampler, daemon=True)
    st.start()
    workers = [threading.Thread(target=load, args=(cl,)) for cl in clients]
    for w in workers:
        w.start()
    t0 = time.time()
    go.wait()
    for w in workers:
        w.join()
    elapsed = time.time() - t0
    time.sleep(0.3)
    stop.set()
    st.join()
    for cl in clients:
        cl.close()

    cut = 0
    for f in logs:
        try:
            with open(f, "rb") as fh:
                fh.seek(sizes.get(f, 0))
                cut += len(CUT.findall(fh.read().decode("utf-8", "replace")))
        except OSError:
            pass
    good = [s for s in samples if s >= 0]
    peak = max(good) if good else -1
    hwm_after = thread_counts(a.pid)[1]
    if hwm_after > hwm_before:
        peak = max(peak, hwm_after)   # the load set a new record: that record is the true peak
    print(f"thread high-water mark: {hwm_before} before, {hwm_after} after"
          + ("" if hwm_after > hwm_before else " (not raised: the sampled peak stands)"))
    bound = budget + a.clients
    print(f"{done[0]} walks by {a.clients} clients in {elapsed:.1f} s, {len(errors)} error(s); "
          f"{len(good)} thread samples")
    print(f"threads: baseline {base}, peak {peak}, gained {peak - base}; bound budget + clients = {bound}")
    print(f"walks the budget cut (\"N of M threads\" lines): {cut}")
    if errors:
        print("errors:", errors[:3])
    within = peak - base <= bound
    ok = within if a.expect == "within" else not within
    print(f"RESULT: {'PASS' if ok else 'FAIL'} (expected {a.expect} the bound)")
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main())
