"""[PIPE-PER-PROCESS-2026-11-06] Stage C, live: what a client turns on is its own, on a running game.

    py stagec_live.py                       # the one injected game; its logs under Logs/<exe name>
    py stagec_live.py --pid 1234
    py stagec_live.py --only seethrough     # one section: seethrough, sessions, watch, scan, persist

Pipe and mailbox only -- no UI and no CE window, so a CE window another session opens cannot get in the way. Run it
with the UI DISCONNECTED: section 1 needs to be the only pipe client, and every section opens its own connections.

WHAT EACH SECTION SHOWS, and what it read as before Stage C:
  1 seethrough  CE's mailbox record turns See-through on; a one-shot pipe client reads the state and leaves; the next
                client still finds it on, held by CE (before: the first client's departure, as the last pipe client,
                turned it off). A pipe client's own claim goes with it while CE's stays. CE's untick turns it off.
                Then pipe-only: client A, ANOTHER PROCESS, turns it on and leaves while B stays connected -- it
                goes off (before: it stayed on for nobody until B left too). An owner is a client process, so a
                second connection of A's own process keeps A's claim, as the UI's second lane keeps the first's.
  2 sessions    A begins a value scan; B can neither query nor end A's session (before: both worked); A still can;
                A's disconnect drops it, and the pipe log says so for A's connection.
  3 watch       A and B watch the same address and each keeps its own; B's unwatch stops nothing of A's (before:
                it stopped A's). A watch's events arrive with A's next command, carrying how many reads they stand
                for. An unwatch of a 10-minute watch returns at once (before: up to 10 minutes).
  4 scan        two clients send trigger_scan at the same instant: no error reply, and every reply is either the
                start or "in progress" (before: the loser got "Scan already in progress" as an error, or both
                started on one std::thread). The log's RunScan starts match the started replies. ⚠ On a game
                already initialized a scan ends in milliseconds, so the two usually run one after the other (two
                starts); the overlap itself is unit-tested (dll_core_test SINGLEFLIGHT).
  5 persist     set_invoke_timeout with persist answers `persisted` from the write and the cache file holds it; the
                game's previous value is put back afterwards.
"""
import argparse
import json
import os
import pathlib
import subprocess
import sys
import threading
import time

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
from pipe_client import LOGROOT, PipeClient, process_image   # noqa: E402
import mailbox_poke as mb                               # noqa: E402

CMD_SEETHROUGH = 14   # Mimic.h
RESULTS: list[tuple[str, bool, str]] = []


def check(name: str, ok: bool, detail: str = "") -> bool:
    RESULTS.append((name, ok, detail))
    print(f"  {'PASS' if ok else 'FAIL'}  {name}" + (f"   ({detail})" if detail else ""))
    return ok


def client(pid):
    return PipeClient(pid=pid, timeout=60.0).connect()


def log_text(logdir: str) -> str:
    p = LOGROOT / logdir / "pipe-0.log"
    return p.read_text(encoding="utf-8", errors="replace") if p.is_file() else ""


# ---------------------------------------------------------------------------------------------------------------- 1
def section_seethrough(pid):
    print("\n=== 1. See-through belongs to whoever turned it on ===")
    base = mb.mailbox_addr(str(pid))
    m = mb.Mem(pid)

    def ce(value):
        res, _params, _dt, err = mb.poke(m, base, 1, cmd=CMD_SEETHROUGH, value=value)
        return res, err

    def state():
        with client(pid) as c:
            return c.request("seethrough_get_state")

    def settle(done, timeout=3.0):
        # A one-shot reader is a connection of THIS process, which owns what an earlier connection of it turned on:
        # if it registers before that connection's teardown has run, the claim is kept until the reader leaves too.
        # So ask again until the state settles, instead of reading the first answer as the verdict.
        deadline = time.time() + timeout
        s = state()
        while not done(s) and time.time() < deadline:
            time.sleep(0.2)
            s = state()
        return s

    res, err = ce(1)
    if not check("CE's record turns See-through on", res == 1, f"result={res} {err}"):
        return
    s = state()   # a one-shot client: its departure is the old teardown's trigger
    check("a pipe client sees it on, held by CE", s.get("active") is True and s.get("ce_claimed") is True,
          f"active={s.get('active')} ce_claimed={s.get('ce_claimed')} claims={s.get('claims')}")
    s = state()
    check("after that client left, it is still on (CE holds it)", s.get("active") is True,
          f"active={s.get('active')} claims={s.get('claims')}")

    with client(pid) as y:
        r = y.request("seethrough_set", enable=True)
        check("a pipe client adds its own claim", r.get("claims") == 2, f"claims={r.get('claims')}")
    s = settle(lambda s: s.get("claims") == 1)
    check("that client left: its claim went, CE's stayed", s.get("active") is True and s.get("claims") == 1
          and s.get("ce_claimed") is True, f"active={s.get('active')} claims={s.get('claims')}")

    res, err = ce(0)
    s = state()
    check("CE's untick turns it off for everyone", res == 0 and s.get("active") is False and s.get("claims") == 0,
          f"result={res} active={s.get('active')} claims={s.get('claims')}")

    b = client(pid)
    try:
        # A in another process: an owner is a client process.
        r = subprocess.run([sys.executable, __file__, "--pid", str(pid), "--child-enable-seethrough"],
                           capture_output=True, text=True, timeout=120)
        check("pipe-only: client A (another process) turns it on and leaves", r.returncode == 0,
              (r.stdout + r.stderr).strip()[-120:])
        time.sleep(0.5)
        s = b.request("seethrough_get_state")
        check("A left while B stays connected: it goes off with A", s.get("active") is False
              and s.get("claims") == 0, f"active={s.get('active')} claims={s.get('claims')}")

        # A second connection of the SAME process keeps the claim: the UI's two lanes are one owner.
        with client(pid) as a2:
            a2.request("seethrough_set", enable=True)
        time.sleep(0.5)
        s = b.request("seethrough_get_state")
        check("a connection of B's own process left: B's process still holds it", s.get("active") is True
              and s.get("claims") == 1, f"active={s.get('active')} claims={s.get('claims')}")
    finally:
        b.close()
    s = settle(lambda s: s.get("active") is False)
    check("...until B, its last connection, leaves", s.get("active") is False, f"active={s.get('active')}")


# ---------------------------------------------------------------------------------------------------------------- 2
def section_sessions(pid, logdir):
    print("\n=== 2. A scan session is its connection's ===")
    a, b = client(pid), client(pid)
    try:
        r = a.request("begin_value_scan", data_type="Int32", scan_type="Exact", value="1",
                      game_only=True, max_results=2000, deadline_ms=15000, page_size=10)
        sid = r.get("session_id")
        if not check("A begins a value scan", bool(r.get("ok")) and bool(sid), f"session_id={sid} total={r.get('total')}"):
            return
        q = b.request("query_candidates", session_id=sid, offset=0, limit=5)
        check("B cannot query A's session", q.get("ok") is False and "session_not_found" in str(q.get("error", "")),
              str(q.get("error", q.get("total"))))
        e = b.request("end_value_scan", session_id=sid)
        check("B cannot end A's session", e.get("ended") is False, f"ended={e.get('ended')}")
        q = a.request("query_candidates", session_id=sid, offset=0, limit=5)
        check("A still can", q.get("ok") is True, f"total={q.get('total')}")
        me = [h for h in (a.request("pipe_clients").get("clients") or []) if h.get("self")]
        seq = me[0].get("seq") if me else None
    finally:
        a.close()
    time.sleep(0.8)
    try:
        line = f"connection #{seq} left -- 1 value / 0 group scan session(s) dropped"
        check("A's disconnect dropped its session (pipe log)", seq is not None and line in log_text(logdir),
              f"looked for: {line}")
    finally:
        b.close()


# ---------------------------------------------------------------------------------------------------------------- 3
def section_watch(pid):
    print("\n=== 3. A watch is its connection's, and its thread never touches the pipe ===")
    a, b = client(pid), client(pid)
    try:
        p = a.request("get_pointers")
        gw = int(p["gworld"], 16)
        raw = a.request("read_mem", addr=hex(gw), size=8)
        hexs = (raw.get("data") or raw.get("bytes") or "").replace(" ", "")[:16]
        target = int.from_bytes(bytes.fromhex(hexs), "little") if len(hexs) == 16 else gw
        addr = hex(target)
        ra = a.request("watch", addr=addr, size=8, interval_ms=250)
        rb = b.request("watch", addr=addr, size=8, interval_ms=250)
        check("A and B each watch the same address", ra.get("ok") is True and rb.get("ok") is True
              and ra.get("interval_ms") == 250, f"A={ra.get('ok')} B={rb.get('ok')} interval={ra.get('interval_ms')}")
        u = b.request("unwatch", addr=addr)
        check("B's unwatch stops B's own", u.get("stopped") is True, f"stopped={u.get('stopped')}")
        u = b.request("unwatch", addr=addr)
        check("...and a second one finds nothing of A's to stop", u.get("stopped") is False, f"stopped={u.get('stopped')}")

        a.events.clear()
        time.sleep(2.0)   # A sends nothing: its samples wait in the slot
        a.request("get_pointers")
        ev = [e for e in a.events if e.get("event") == "watch"]
        n = ev[-1].get("samples") if ev else None
        check("A's watch still delivers, with its next command", len(ev) >= 1, f"{len(ev)} event(s)")
        check("...one event standing for the reads in between", isinstance(n, int) and n >= 5, f"samples={n}")

        far = hex(target + 0x10)
        a.request("watch", addr=far, size=8, interval_ms=600000)
        t0 = time.time()
        u = a.request("unwatch", addr=far)
        dt = time.time() - t0
        check("an unwatch of a 10-minute watch returns at once", u.get("stopped") is True and dt < 1.0, f"{dt*1000:.0f} ms")
        u = a.request("unwatch", addr=addr)
        check("A's own unwatch stops A's", u.get("stopped") is True, f"stopped={u.get('stopped')}")
    finally:
        a.close()
        b.close()


# ---------------------------------------------------------------------------------------------------------------- 4
def section_scan(pid, logdir):
    print("\n=== 4. One scan job at a time, and a second asker hears 'in progress' ===")
    before = log_text(logdir).count("RunScan: started")
    c1, c2 = client(pid), client(pid)
    replies = [None, None]
    gate = threading.Barrier(2)

    def go(i, c):
        gate.wait()
        replies[i] = c.request("trigger_scan")

    try:
        ts = [threading.Thread(target=go, args=(i, c)) for i, c in enumerate((c1, c2))]
        for t in ts:
            t.start()
        for t in ts:
            t.join()
        okay = all(r and r.get("ok") is True for r in replies)
        shapes = [("started" if r.get("started") else ("in_progress" if r.get("in_progress") else "?")) for r in replies]
        check("two simultaneous trigger_scans: no error reply", okay, f"{shapes} {[r.get('error') for r in replies]}")
        check("...each is the start or 'in progress'", "?" not in shapes, str(shapes))
        deadline = time.time() + 120
        st = {}
        while time.time() < deadline:
            st = c1.request("scan_status")
            if not st.get("running"):
                break
            time.sleep(0.5)
        check("the scan ends initialized (scanned)", st.get("scanned") is True, f"phase={st.get('phase')}")
        started = sum(1 for r in replies if r.get("started"))
        time.sleep(0.3)
        after = log_text(logdir).count("RunScan: started")
        check("the log's RunScan starts match the started replies", after - before == started,
              f"log +{after - before}, replies started={started}"
              + ("" if "in_progress" in shapes else "; one after the other, no overlap this time"))
    finally:
        c1.close()
        c2.close()


# ---------------------------------------------------------------------------------------------------------------- 5
def cache_file() -> pathlib.Path:
    return pathlib.Path(os.environ["LOCALAPPDATA"]) / "UE5CEDumper" / f"UE5CEDumper.{os.environ.get('COMPUTERNAME', '')}.json"


def section_persist(pid):
    print("\n=== 5. `persisted` says what happened to the write ===")
    with client(pid) as c:
        pe = c.request("get_pointers").get("pe_hash") or ""
        f = cache_file()
        old = None
        if f.is_file():
            rec = json.loads(f.read_text(encoding="utf-8")).get("games", {}).get(pe, {})
            old = rec.get("invokeTimeoutMs")
        r = c.request("set_invoke_timeout", timeout_ms=7000, persist=True)
        check("set_invoke_timeout persist: persisted", r.get("persisted") is True, f"persisted={r.get('persisted')}")
        rec = json.loads(f.read_text(encoding="utf-8")).get("games", {}).get(pe, {}) if f.is_file() else {}
        check("...and the cache holds it", rec.get("invokeTimeoutMs") == 7000, f"invokeTimeoutMs={rec.get('invokeTimeoutMs')}")
        r = c.request("set_invoke_timeout", timeout_ms=int(old) if old else 0, persist=True)
        check("the game's previous value is back", r.get("persisted") is True, f"restored to {old or 'default'}")


def main():
    ap = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    ap.add_argument("--pid", type=int, default=None)
    ap.add_argument("--logdir", default=None, help="default: the game's exe name, as the DLL names its log folder")
    ap.add_argument("--only", choices=["seethrough", "sessions", "watch", "scan", "persist"])
    ap.add_argument("--child-enable-seethrough", action="store_true", help=argparse.SUPPRESS)
    a = ap.parse_args()
    if a.child_enable_seethrough:   # section 1's client A, run as its own process
        with client(a.pid) as c:
            r = c.request("seethrough_set", enable=True)
        print(f"child: active={r.get('active')} claims={r.get('claims')}")
        return 0 if r.get("active") is True else 1
    with client(a.pid) as c:
        pid = a.pid or int(c.pipe.rsplit("_", 1)[-1])
    if a.logdir is None:
        a.logdir = pathlib.Path(process_image(pid)).stem
    with client(pid) as c:
        others = [h for h in (c.request("pipe_clients").get("clients") or []) if not h.get("self")]
        b = c.request("get_pointers").get("build_number")
    print(f"game pid {pid}, DLL build {b}, other clients connected: {len(others)}")
    if others:
        print("  !! other clients hold connections (" + ", ".join(f"{h.get('exe')} {h.get('pid')}" for h in others)
              + ") -- section 1 needs to be the only pipe client")
    sections = {
        "seethrough": lambda: section_seethrough(pid),
        "sessions": lambda: section_sessions(pid, a.logdir),
        "watch": lambda: section_watch(pid),
        "scan": lambda: section_scan(pid, a.logdir),
        "persist": lambda: section_persist(pid),
    }
    for name, fn in sections.items():
        if a.only in (None, name):
            fn()
    bad = [r for r in RESULTS if not r[1]]
    print(f"\nSTAGE C LIVE: {len(RESULTS) - len(bad)}/{len(RESULTS)} checks passed -- {'PASS' if not bad else 'FAIL'}")
    return 0 if not bad else 1


if __name__ == "__main__":
    sys.exit(main())
