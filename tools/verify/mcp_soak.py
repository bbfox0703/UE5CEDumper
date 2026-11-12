"""Register [PIPE-PER-PROCESS-2026-11-06] item 14: one MCP session held for hours against a game.

    py tools/verify/mcp_soak.py --exe dist/UE5DumpUI.exe --pid P [--hours 2] [--interval 30]

One server process, one pipe connection, held for --hours. Every --interval seconds: read_status (timed) and
read_pipe_clients (the server's own connection: its seq must never change, i.e. no reconnect), and the server's and the
game's private bytes and handle counts. Every --heavy-every'th interval (10; 0 = never) also a heavier pair:
read_find_instances of Actor and a value scan (begin, then end). The sizes are printed every --report-every intervals.
At the end it closes the server and reads its exit code.

Report: per-hour read_status p50 / p95, the seqs seen, the growth of private bytes and handles, and the failures.
PASS (exit 0): no failed call; one seq for the whole run; the last hour's p95 at most twice the first hour's; the
server's private bytes grew by less than 50 MB and its handles by less than 50; the server exited 0.
"""
from __future__ import annotations

import argparse
import ctypes
import ctypes.wintypes as w
import pathlib
import statistics
import sys
import time

HERE = pathlib.Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
from mcp_client import McpClient, McpError, payload   # noqa: E402

sys.stdout.reconfigure(encoding="utf-8", errors="replace")

k32 = ctypes.WinDLL("kernel32", use_last_error=True)
psapi = ctypes.WinDLL("psapi", use_last_error=True)
k32.OpenProcess.restype = ctypes.c_void_p


class PMC(ctypes.Structure):
    _fields_ = [("cb", w.DWORD), ("PageFaultCount", w.DWORD), ("PeakWorkingSetSize", ctypes.c_size_t),
                ("WorkingSetSize", ctypes.c_size_t), ("QuotaPeakPagedPoolUsage", ctypes.c_size_t),
                ("QuotaPagedPoolUsage", ctypes.c_size_t), ("QuotaPeakNonPagedPoolUsage", ctypes.c_size_t),
                ("QuotaNonPagedPoolUsage", ctypes.c_size_t), ("PagefileUsage", ctypes.c_size_t),
                ("PeakPagefileUsage", ctypes.c_size_t), ("PrivateUsage", ctypes.c_size_t)]


def proc_stats(pid: int) -> tuple[int, int]:
    """(private bytes, handle count) or (-1, -1)."""
    h = k32.OpenProcess(0x1000 | 0x0010, False, pid)   # QUERY_LIMITED_INFORMATION | VM_READ
    if not h:
        return -1, -1
    h = ctypes.c_void_p(h)
    try:
        pmc = PMC()
        pmc.cb = ctypes.sizeof(PMC)
        priv = pmc.PrivateUsage if psapi.GetProcessMemoryInfo(h, ctypes.byref(pmc), pmc.cb) else -1
        n = w.DWORD()
        handles = n.value if k32.GetProcessHandleCount(h, ctypes.byref(n)) else -1
        return priv, handles
    finally:
        k32.CloseHandle(h)


def pct(xs: list[float], p: float) -> float:
    if not xs:
        return 0.0
    xs = sorted(xs)
    return xs[min(len(xs) - 1, int(round(p * (len(xs) - 1))))]


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    ap.add_argument("--exe", required=True)
    ap.add_argument("--pid", type=int, required=True)
    ap.add_argument("--hours", type=float, default=2.0)
    ap.add_argument("--interval", type=float, default=30.0)
    # 0 = light calls only. Varying this and --interval tells growth per call from growth per minute, and the heavy
    # pair's share from the light pair's.
    ap.add_argument("--heavy-every", type=int, default=10)
    ap.add_argument("--report-every", type=int, default=10)
    a = ap.parse_args()
    c = McpClient(str(pathlib.Path(a.exe).resolve()))
    c.initialize()
    srv = c.proc.pid
    fails: list[str] = []
    seqs: set = set()
    lat: dict[int, list[float]] = {}
    s0, g0 = proc_stats(srv), proc_stats(a.pid)
    t_start = time.monotonic()
    end = t_start + a.hours * 3600
    n = 0
    print(f"server pid {srv}, game pid {a.pid}; start: server {s0[0] / 1e6:.1f} MB / {s0[1]} handles, game "
          f"{g0[0] / 1e6:.1f} MB / {g0[1]} handles", flush=True)

    def call(tool: str, args: dict):
        try:
            r = c.call(tool, {"pid": a.pid, **args}, 300)
        except McpError as e:
            fails.append(f"{tool}: {e}")
            return None
        d, _ = payload(r)
        if r.get("isError"):
            fails.append(f"{tool}: {str(d)[:160]}")
            return None
        return d

    while time.monotonic() < end:
        n += 1
        hour = int((time.monotonic() - t_start) // 3600)
        t0 = time.monotonic()
        call("read_status", {})
        lat.setdefault(hour, []).append(time.monotonic() - t0)
        pc = call("read_pipe_clients", {}) or {}
        for x in pc.get("clients", []):
            if x.get("pid") == srv:
                seqs.add(x.get("seq"))
        if a.heavy_every and n % a.heavy_every == 0:
            call("read_find_instances", {"class_name": "Actor", "limit": 50})
            vs = call("read_value_scan_begin", {"data_type": "Int32", "value": "1", "page_size": 5})
            if isinstance(vs, dict) and vs.get("session_id") is not None:
                call("read_value_scan_end", {"session_id": vs["session_id"]})
        if n % a.report_every == 0:
            s, g = proc_stats(srv), proc_stats(a.pid)
            print(f"  {time.strftime('%H:%M:%S')} #{n}: server {s[0] / 1e6:.1f} MB / {s[1]} handles, game "
                  f"{g[0] / 1e6:.1f} MB / {g[1]} handles; seqs {sorted(seqs)}; failures {len(fails)}", flush=True)
        time.sleep(max(0.0, a.interval - (time.monotonic() - t0)))
    s1, g1 = proc_stats(srv), proc_stats(a.pid)
    code = c.close()
    print("\nread_status latency per hour (p50 / p95, ms):", flush=True)
    for h in sorted(lat):
        print(f"  hour {h}: {pct(lat[h], .5) * 1000:.1f} / {pct(lat[h], .95) * 1000:.1f} ({len(lat[h])} calls)")
    grow_mb, grow_h = (s1[0] - s0[0]) / 1e6, s1[1] - s0[1]
    print(f"server: {s0[0] / 1e6:.1f} -> {s1[0] / 1e6:.1f} MB ({grow_mb:+.1f}), handles {s0[1]} -> {s1[1]} ({grow_h:+d})")
    print(f"game:   {g0[0] / 1e6:.1f} -> {g1[0] / 1e6:.1f} MB, handles {g0[1]} -> {g1[1]}")
    print(f"seqs {sorted(seqs)}; server exit {code}; failures {len(fails)}")
    first, last = min(lat), max(lat)
    if len(seqs) != 1:
        fails.append(f"the connection changed: seqs {sorted(seqs)}")
    if last != first and pct(lat[last], .95) > 2 * pct(lat[first], .95):
        fails.append("the last hour's p95 is more than twice the first's")
    if grow_mb >= 50 or grow_h >= 50:
        fails.append(f"the server grew {grow_mb:+.1f} MB, {grow_h:+d} handles")
    if code != 0:
        fails.append(f"the server exited {code}")
    print(f"\n{'PASS' if not fails else 'FAIL'}: {len(fails)} failed" + "".join("\n  " + f for f in fails[:20]))
    return 0 if not fails else 1


if __name__ == "__main__":
    sys.exit(main())
