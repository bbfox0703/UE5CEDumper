"""R7-C-05 / R7-S9: does a CE mailbox command wait out apply_rescan's init fence, or run on the half-applied pool?

    py tools/verify/r7_apply_fence.py --mode c05 --process DumperTest-Win64-Shipping --out out/r7live/c05/green
    py tools/verify/r7_apply_fence.py --mode s9  --process DumperTest-Win64-Shipping --out out/r7live/s9/green

Needs a STAGED DLL (tools/verify/staging/r7-fence-green.json and its red twins) injected into a FRESH process:
init withholds the GObjects it found, so `apply_rescan` has something to publish, and two flag-gated sleeps widen
the windows -- the apply sleeps 2 s after Aura::Init (%TEMP%\\ue5dump_apply_widen.flag), and the mailbox's
post-UE5_Init path 1.5 s (%TEMP%\\ue5dump_ensure_widen.flag). This rig creates the flags and deletes them in a
`finally`. The apply is one-shot per process (GObjects publishes once), so every arm needs a fresh launch.

c05: rescan until GObjects is found, raise the apply flag, then send apply_rescan on one thread while another pokes
     CMD_QUERY_PTR in a loop from t_send+100 ms to t_reply+500 ms. GREEN: no poke is SERVED (result != -10) before
     t_reply - 50 ms. RED (no fence): pokes served in milliseconds inside the 2 s window.
s9:  raise both flags; poke once at t0 (GObjects is still 0, so it goes through UE5_Init and sleeps 1.5 s), send
     apply_rescan at t0+200 ms. GREEN: the poke is served at or after t_reply - 50 ms. RED: served ~0.7 s early.

Never calls ensure_scanned (it would send trigger_scan). Prints the timeline and the relevant log lines.
"""
import argparse
import json
import os
import pathlib
import sys
import threading
import time

sys.stdout.reconfigure(encoding="utf-8", errors="replace")
HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import mailbox_poke as MP  # noqa: E402
from pipe_client import PipeClient  # noqa: E402

TEMP = pathlib.Path(os.environ.get("TEMP", "."))
APPLY_FLAG, ENSURE_FLAG = TEMP / "ue5dump_apply_widen.flag", TEMP / "ue5dump_ensure_widen.flag"
LOGS = pathlib.Path(os.environ["LOCALAPPDATA"]) / "UE5CEDumper" / "Logs"


def main():
    ap = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    ap.add_argument("--mode", choices=("c05", "s9"), required=True)
    ap.add_argument("--process", required=True, help="image stem, e.g. DumperTest-Win64-Shipping")
    ap.add_argument("--out", required=True)
    a = ap.parse_args()
    os.makedirs(a.out, exist_ok=True)
    lines = []

    def say(s=""):
        print(s, flush=True)
        lines.append(s)

    logdir = LOGS / a.process
    init_log = (logdir / "init-0.log").read_text(encoding="utf-8", errors="replace")
    pre = [ln for ln in init_log.splitlines() if "UE5_Init: Complete" in ln]
    say(f"# {time.strftime('%Y-%m-%d %H:%M:%S')} mode {a.mode}  init: {pre[-1] if pre else 'NO UE5_Init: Complete'}")
    if not pre or "GObjects=0x0" not in pre[-1]:
        raise SystemExit("precondition failed: init must have completed with GObjects=0x0 (the staged DLL)")

    pid = MP.pid_of(a.process)
    m = MP.Mem(pid)
    base = MP.mailbox_addr(a.process)
    events, result = [], {}
    try:
        with PipeClient(timeout=120) as c:
            p = c.request("get_pointers")
            pd = p.get("data") or p
            say(f"get_pointers: gobjects {pd.get('gobjects')} gnames {pd.get('gnames')} objects {pd.get('object_count')}")
            if a.mode == "c05":
                res, _, el, err = MP.poke(m, base, MP.QUERY_OP_GWORLD, timeout=15.0)
                say(f"baseline QUERY_PTR: result {res} in {el * 1000:.0f} ms ({err!r})")
            c.request("rescan")
            t0 = time.time()
            while time.time() - t0 < 600:
                d = c.request("rescan_status")
                d = d.get("data") or d
                if not d.get("running"):
                    break
                time.sleep(1.0)
            say(f"rescan: found_gobjects {d.get('found_gobjects')} {d.get('gobjects_addr')} ({d.get('gobjects_method')})")
            if not d.get("found_gobjects"):
                raise SystemExit("precondition failed: the rescan did not find GObjects")

            APPLY_FLAG.write_text("r7")
            if a.mode == "s9":
                ENSURE_FLAG.write_text("r7")
            stop = threading.Event()
            times = {}

            def poker(start_at, once):
                while time.time() < start_at:
                    time.sleep(0.001)
                while not stop.is_set():
                    ti = time.time()
                    try:
                        res, _, el, err = MP.poke(m, base, MP.QUERY_OP_GWORLD, timeout=15.0)
                    except SystemExit as e:  # a 15 s timeout is the deadlock finding
                        events.append({"t_issue": ti, "t_done": time.time(), "result": None, "err": str(e)})
                        return
                    events.append({"t_issue": ti, "t_done": time.time(), "result": res, "err": err})
                    if once:
                        return

            if a.mode == "c05":
                times["t_send"] = time.time()
                th = threading.Thread(target=poker, args=(times["t_send"] + 0.1, False))
                th.start()
                r = c.request("apply_rescan")
                times["t_reply"] = time.time()
                time.sleep(0.5)
                stop.set()
                th.join(20)
            else:
                times["t0"] = time.time()
                th = threading.Thread(target=poker, args=(times["t0"], True))
                th.start()
                while time.time() < times["t0"] + 0.2:
                    time.sleep(0.001)
                times["t_send"] = time.time()
                r = c.request("apply_rescan")
                times["t_reply"] = time.time()
                th.join(20)
            result["apply"] = r.get("data") or r
            say(f"apply_rescan: {json.dumps(result['apply'])[:220]}")
    finally:
        for f in (APPLY_FLAG, ENSURE_FLAG):
            try:
                f.unlink()
            except FileNotFoundError:
                pass

    tb = times.get("t0", times["t_send"])
    say(f"timeline (s from {'t0' if 't0' in times else 't_send'}): t_send {times['t_send'] - tb:+.3f}  "
        f"t_reply {times['t_reply'] - tb:+.3f}")
    early = []
    for e in events:
        served = e["result"] is not None and e["result"] != -10
        say(f"  poke issued {e['t_issue'] - tb:+.3f} done {e['t_done'] - tb:+.3f} result {e['result']}"
            f"{'  SERVED' if served else ''}  {e['err'][:60]!r}")
        if served and e["t_done"] < times["t_reply"] - 0.05:
            early.append(e)
    for name in ("pipe-0.log", "init-0.log", "offsets-0.log", "scan-0.log"):
        txt = (logdir / name).read_text(encoding="utf-8", errors="replace") if (logdir / name).exists() else ""
        for ln in txt.splitlines():
            if any(k in ln for k in ("STAGING", "Mailbox: auto-initializing", "Already initialized",
                                     "apply_rescan: Applied", "ValidateAndFixOffsets: Starting")):
                say(f"  [{name}] {ln[:170]}")
    verdict = "RED (served on the half-applied pool)" if early else "GREEN (no poke served before the apply ended)"
    if not events:
        verdict = "NO POKES RECORDED"
    say(f"VERDICT: {verdict}; served-early pokes: {len(early)}")
    json.dump({"times": times, "events": events, "result": result}, open(os.path.join(a.out, "fence.json"), "w"),
              indent=1)
    open(os.path.join(a.out, "fence.txt"), "w", encoding="utf-8").write("\n".join(lines) + "\n")
    return 0


if __name__ == "__main__":
    sys.exit(main())
