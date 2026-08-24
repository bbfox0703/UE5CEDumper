r"""B5 mailbox flavour — make `Mimic::EnsureInitialized` the SECOND caller of UE5_Init.

    py tools/verify/b5_mailbox_race.py stage     # proxy-launched DumperTest (hardlinked)
    py tools/verify/b5_mailbox_race.py race      # run the race and score it
    py tools/verify/b5_mailbox_race.py clean

THE ROW. B5's main half is closed (`[ELLIOT-B5-2026-08-18]`): three CE `createThread`s hit
`UE5_Init` together and produced the full handshake. Its closing note says the row described the
second caller as a **CE mailbox command** (`Mimic::EnsureInitialized`) whereas the run used a direct
`UE5_Init` from CE Lua — *"same entry point, same guard — but the mailbox flavour specifically is
still unexercised."* This exercises that flavour.

⭐ NO CHEAT ENGINE. The classification filed this under "Cheat Engine sitting", but
`mailbox_poke.py` drives the mailbox with `WriteProcessMemory` and nothing else, and
`CommandRequiresInit` returns true for every command except `CMD_FOREGROUND` — so `CMD_QUERY_PTR`
reaches `EnsureInitialized` with no CE anywhere. The "3 CE createThreads" in the row text is
inherited from the DIRECT-export flavour's rig and is the wrong instrument here: the mailbox is
asynchronous by construction (the DLL's own poller thread dispatches it), so ONE poke inside the
window suffices.

⭐ WHY A PROXY LAUNCH IS THE WHOLE POINT. Only the proxy path leaves the pipe live while BOTH cached
pointers are still 0 — `DllMain ProxyStart: proxy DLL mode — starting pipe server only (no scan)`.
Inject-after-launch scans immediately, so there is no window to race into.

⚠⚠ THE THIRD OUTCOME, AND IT READS LIKE A PASS. If the poke lands AFTER the scan completes,
`EnsureInitialized`'s `if (g_cachedGObjects && g_cachedGNames) return true;` early-returns, UE5_Init
is never re-entered, and **no log lines appear at all**. That is a STAGING MISS, not a result. It has
already happened twice on this row (a GUI attempt returned in 16 ms off the cached result; a 61-call
loop produced one `Starting` line and no handshake). This rig therefore scores THREE outcomes, and
refuses to call an empty log a pass.

  PASS    exactly one "Starting initialization...", plus "init already in progress" and
          "resumed after waiting", and the mailbox command completes
  FAIL    two or more "Starting initialization..."  (the guard did not hold)
  MISS    no handshake lines at all -> re-stage, never record
"""
from __future__ import annotations

import os
import pathlib
import shutil
import subprocess
import sys
import time

HERE = pathlib.Path(__file__).resolve().parent
REPO = HERE.parents[1]
SRC = pathlib.Path(r"D:\UE_Analyze_data\for testing\DumperTest\Development\Windows")
GAME = pathlib.Path(r"D:\ZZProxyB5\DumperTest")
WIN64 = GAME / "DumperTest" / "Binaries" / "Win64"
EXE = WIN64 / "DumperTest.exe"
PROXY = REPO / "dist" / "proxy" / "version.dll"
LOGDIR = pathlib.Path(os.environ["LOCALAPPDATA"]) / "UE5CEDumper" / "Logs" / "DumperTest"


def say(s):
    enc = sys.stdout.encoding or "utf-8"
    sys.stdout.write(s.encode(enc, "replace").decode(enc, "replace") + "\n")


def stage():
    if not PROXY.is_file():
        say("MISSING %s -- build the proxies first (-Target DLL)" % PROXY)
        return 1
    if GAME.exists():
        say("REFUSING: %s exists -- clean first" % GAME)
        return 1
    n = 0
    for dirpath, _d, files in os.walk(SRC):
        rel = pathlib.Path(dirpath).relative_to(SRC)
        (GAME / rel).mkdir(parents=True, exist_ok=True)
        for fn in files:
            s, d = pathlib.Path(dirpath) / fn, GAME / rel / fn
            try:
                os.link(s, d)
            except OSError:
                shutil.copy2(s, d)
            n += 1
    shutil.copy2(PROXY, WIN64 / "version.dll")     # a REAL copy: it is the artifact under test
    say("staged %s (%d files hardlinked)" % (GAME, n))
    say("proxy: %s (%d bytes)" % (WIN64 / "version.dll", (WIN64 / "version.dll").stat().st_size))
    return 0


def _fresh_log_marker():
    """Byte offsets of the live logs, so 'since launch' is exact.

    ⚠ USELESS ACROSS A LAUNCH, and it cost a run: `<cat>-0.log` ROTATES on every process
    start (the previous run is archived and a fresh file begins), so an offset taken
    BEFORE launching is larger than the new file and `txt[mark:]` comes back EMPTY. That
    reads as "the proxy line is missing" when the proxy was fine. Mark AFTER the process
    exists, or read the whole fresh file.
    """
    return {str(f): f.stat().st_size for f in LOGDIR.glob("*-0.log")} if LOGDIR.is_dir() else {}


def _since(mark):
    out = []
    if LOGDIR.is_dir():
        for f in LOGDIR.glob("*-0.log"):
            try:
                txt = f.read_text(encoding="utf-8", errors="replace")
            except OSError:
                continue
            off = mark.get(str(f), 0)
            out.append(txt[off:] if off <= len(txt) else txt)   # rotated -> take it all
    return "\n".join(out)


def race():
    if not EXE.is_file():
        say("MISSING %s -- run stage first" % EXE)
        return 1
    fails = []

    p = subprocess.Popen([str(EXE), "-windowed", "-ResX=1280", "-ResY=720",
                          "-DumperTestMaxFPS=15"])
    say("[0] launched pid %d through the deployed proxy" % p.pid)
    pathlib.Path("out").mkdir(exist_ok=True)
    pathlib.Path("out/host.pid").write_text(str(p.pid))

    # Wait for the PROXY to publish its pipe — but NOT for any scan, because there is none.
    time.sleep(9.0)
    # ⚠ MARK ONLY NOW, NEVER BEFORE THE LAUNCH. `<cat>-0.log` rotates on process start, so a
    # pre-launch offset points into the PREVIOUS run's file: if the new file is shorter the
    # slice is empty, and if it is longer the slice silently DROPS this run's opening lines.
    # The second shape is the nastier one — it cost a run here, reporting 0 "Starting
    # initialization" lines while the raw log plainly had one. Marking after the process
    # exists means the file has already rotated and the offset is real.
    mark = {k: 0 for k in _fresh_log_marker()}      # this process's log IS the whole file
    txt = _since(mark)
    proxy_mode = "proxy DLL mode" in txt
    say("[1] proxy mode line present: %s" % proxy_mode)
    for ln in txt.splitlines():
        if "ProxyStart" in ln or "proxy DLL mode" in ln:
            say("    %s" % ln.strip()[:150])
    if not proxy_mode:
        fails.append("1: the game did not start in PROXY mode, so the pipe is not live with the "
                     "pointers still 0 and there is no window to race into")

    # ---- CONSTRUCT the race. All the slow parts happen BEFORE it ----
    #
    # ⭐ This is the row's own lesson, applied: "Concurrency had to be constructed; it does
    # not arise from doing things quickly." Two earlier attempts on the sibling row failed
    # because the second caller arrived AFTER the scan. Spawning `mailbox_poke.py` as a
    # PROCESS costs ~0.5-1.0 s of interpreter start plus a nested spawn for mailbox_addr —
    # more than DumperTest's whole scan window (measured 1.57 s at 25,212 objects, vs
    # Elliot's 3.3 s at 85,068). So resolve the mailbox HERE, before anything starts, and
    # leave only the WriteProcessMemory inside the window.
    #
    # ⚠ `call_export.py` cannot be the first caller in proxy mode: it looks for a module
    # named UE5Dumper.dll, and in proxy mode our code IS `VERSION.dll`. The pipe's
    # `trigger_scan` is both available and closer to the row's own wording ("connect the
    # UI, click Scan, and while the scan is still running…").
    sys.path.insert(0, str(HERE))
    import mailbox_poke as MP                       # noqa: E402
    from pipe_client import PipeClient              # noqa: E402

    pid = p.pid
    base = MP.mailbox_addr(str(pid))
    mem = MP.Mem(pid)
    init_state = mem.i32(base + MP.OFF_INITSTATE)
    with PipeClient() as c:
        gp = c.request("get_pointers")
    say("[2] PRE-RACE (the row's precondition, confirmed not assumed):")
    say("      initState=%s (%s)   objects=%s  gobjects=%s  gnames=%s"
        % (init_state, "READY" if init_state == MP.INIT_READY else "NOT READY",
           gp.get("object_count"), gp.get("gobjects"), gp.get("gnames")))
    if init_state != MP.INIT_READY:
        fails.append("2: initState is not READY, so the mailbox poller is not serving yet")
    if (gp.get("object_count") or 0) != 0:
        fails.append("2: the pool is already scanned -- there is no window left to race into. "
                     "This is the STAGING MISS shape; relaunch.")

    say("[3] racing: trigger_scan, then a mailbox poke %d ms later" % 120)
    t0 = time.time()
    with PipeClient() as c:
        r = c.request("trigger_scan")
        say("    trigger_scan -> %s (returns immediately; the scan runs on a worker)" % r.get("started"))
        time.sleep(0.12)                            # only the write is inside the window
        res, params, dt, err = MP.poke(mem, base, MP.QUERY_OP_GWORLD)
        say("    mailbox poke at +%.0f ms -> result=%s  %.0f ms%s"
            % ((time.time() - t0) * 1000, res, dt * 1000, ("  err=%r" % err) if err else ""))
    time.sleep(4.0)

    time.sleep(2.0)
    txt = _since(mark)
    starting = txt.count("UE5_Init: Starting initialization")
    waiting = txt.count("init already in progress on another thread")
    resumed = txt.count("resumed after waiting")
    say("")
    say("[3] handshake tally since launch:")
    say("      'Starting initialization...'        : %d   (must be exactly 1)" % starting)
    say("      'init already in progress ... waiting': %d" % waiting)
    say("      'resumed after waiting'             : %d" % resumed)
    for ln in txt.splitlines():
        if "UE5_Init:" in ln and any(k in ln for k in
                                     ("Starting", "already in progress", "resumed", "Complete")):
            say("      %s" % ln.strip()[:160])

    say("")
    say("=" * 72)
    if starting >= 2:
        say("B5 mailbox flavour: FAIL — %d 'Starting initialization' lines; the guard did not hold"
            % starting)
        return 1
    if waiting == 0 or resumed == 0:
        say("B5 mailbox flavour: ⚠ STAGING MISS, NOT A RESULT — no waiting/resumed handshake.")
        say("  The poke almost certainly landed after the scan, so EnsureInitialized early-returned")
        say("  on the cached pointers and UE5_Init was never re-entered. Re-stage; do not record.")
        return 2
    if fails:
        say("B5 mailbox flavour: FAIL")
        for f in fails:
            say("   - %s" % f)
        return 1
    say("B5 mailbox flavour: PASS — one 'Starting', %d waiter(s), %d resumed, mailbox served"
        % (waiting, resumed))
    return 0


def clean():
    if not GAME.exists():
        say("nothing to clean")
        return 0
    assert GAME.parent.name.startswith("ZZ"), "refusing %s" % GAME
    shutil.rmtree(GAME)
    say("removed %s" % GAME)
    try:
        GAME.parent.rmdir()
        say("removed %s" % GAME.parent)
    except OSError:
        pass
    return 0


if __name__ == "__main__":
    cmd = sys.argv[1] if len(sys.argv) > 1 else "race"
    sys.exit({"stage": stage, "race": race, "clean": clean}.get(cmd, race)())
