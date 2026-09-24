#!/usr/bin/env python3
r"""L54 `[A3-MIMIC-INIT-FASTPATH]`: a mailbox command that lands while UE5_Init runs must WAIT for it, not
take the cached-globals fast path.

    py tools/verify/l54_init_fastpath.py --arm A --process DumperTest-Win64-Shipping --log-dir <dir> [--dll <path>]
    py tools/verify/l54_init_fastpath.py --arm B --process DumperTest-Win64-Shipping --log-dir <dir> [--dll <path>]

  ARM A (re-init, the WIDE window): UE5_Shutdown never clears the cached GObjects/GNames, and AutoStartWork
        restarts the mailbox poller BEFORE UE5_Init. So during a whole re-scan a pre-fix EnsureInitialized saw
        "cached globals present" and served the command at once. The rig calls UE5_Shutdown, then
        UE5_AutoStart, polls initState until it reads RUNNING (1), and pokes CMD_QUERY_PTR at that instant,
        then CMD_PROTECT op 2 (GET_STATE, read-only).
  ARM B (first init, the row's literal ask): the poke is fired the moment scan-0.log shows "FindAll: Complete --"
        (the globals are published AFTER FindAll; earlier windows do not discriminate). The process must
        be FRESH (just injected).

SCORING (b5's rule). PASS = exactly ONE "UE5_Init: Starting initialization" for this init, a
"...is waiting" + "...resumed after waiting" pair, and a successful reply. FAIL = two Starting lines, or a
reply served during RUNNING with no waiting line. MISS = the poke landed after init (no waiting line and
initState already READY when it was served): re-stage, never record.

⚠ mailbox_poke's CLI refuses unless READY, which is exactly NOT this window: it is used as a library.
⚠ `--dll` must be the DLL the process actually loaded (call_export resolves the export's RVA from it).
"""
import argparse
import os
import struct
import subprocess
import sys
import time

sys.stdout.reconfigure(encoding="utf-8", errors="replace")
HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import mailbox_poke as MP   # noqa: E402

CMD_PROTECT, PROTECT_OP_GET_STATE = 9, 2


def call_export(name, process, dll):
    r = subprocess.run([sys.executable, os.path.join(HERE, "call_export.py"), name, "--process", process,
                        "--dll", dll], capture_output=True, text=True, errors="replace")
    return r.returncode, (r.stdout + r.stderr).strip().splitlines()[-1:]


def tail(path, start):
    with open(path, "rb") as fh:
        fh.seek(start)
        return fh.read().decode("utf-8", "replace")


def size(path):
    return os.path.getsize(path) if os.path.exists(path) else 0


def score(label, init_txt, pipe_txt, served_state, reply_ok, elapsed):
    starting = init_txt.count("UE5_Init: Starting initialization")
    waiting = init_txt.count("init already in progress on another thread")
    resumed = init_txt.count("resumed after waiting")
    autoinit = pipe_txt.count("Mailbox: auto-initializing")
    print("   %s tally: Starting=%d waiting=%d resumed=%d auto-init=%d | initState when served=%s, "
          "reply ok=%s, round trip %.0f ms" % (label, starting, waiting, resumed, autoinit, served_state,
                                               reply_ok, elapsed * 1000))
    for ln in init_txt.splitlines():
        if "UE5_Init:" in ln and any(k in ln for k in ("Starting", "already in progress", "resumed", "Complete (", "Already initialized")):
            print("      " + ln.strip()[:170])
    if starting >= 2:
        return "FAIL", "two 'Starting initialization' lines -- the guard did not hold"
    if waiting == 0 and served_state == 1:
        return "FAIL", "served during RUNNING with no waiting line -- the fast path"
    if waiting == 0 or resumed == 0:
        return "MISS", "no waiting/resumed handshake -- the poke landed after init; re-stage, do not record"
    if not reply_ok:
        return "FAIL", "the waiting command did not succeed"
    return "PASS", "one Starting, a waiter that resumed, and a served reply"


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--arm", choices=("A", "B"), required=True)
    ap.add_argument("--process", required=True)
    ap.add_argument("--log-dir", required=True)
    ap.add_argument("--dll", default=os.path.join("dist", "UE5Dumper.dll"))
    a = ap.parse_args()
    logd = os.path.expandvars(a.log_dir)
    initlog, pipelog, scanlog = (os.path.join(logd, n) for n in ("init-0.log", "pipe-0.log", "scan-0.log"))

    t_start = time.time()
    pid = MP.pid_of(a.process)
    m = MP.Mem(pid)
    if a.arm == "B":
        # ⚠ ARMED BEFORE INJECTION: Python's own start-up is slower than the 190-445 ms window, so this arm is
        # launched first and waits for the module, resolving the mailbox from base + export RVA (no subprocess).
        import call_export as CX
        rva = CX.export_rva(os.path.abspath(a.dll), "g_invokeMailbox")
        base = None
        while time.time() - t_start < 120 and base is None:
            try:
                base = CX.module_base(pid, "UE5Dumper.dll") + rva
            except SystemExit:
                time.sleep(0.01)
        if base is None:
            print("MISS: UE5Dumper.dll never loaded")
            return 2
    else:
        base = MP.mailbox_addr(a.process)
    print("pid %d, mailbox 0x%X, initState %d, dll %s" % (pid, base, m.i32(base + MP.OFF_INITSTATE), a.dll))
    results = []

    if a.arm == "A":
        if m.i32(base + MP.OFF_INITSTATE) != MP.INIT_READY:
            print("REFUSED: arm A needs a READY process to re-init")
            return 2
        for label, cmd, op in (("QUERY_PTR", MP.CMD_QUERY_PTR, MP.QUERY_OP_GWORLD),
                               ("PROTECT GET_STATE", CMD_PROTECT, PROTECT_OP_GET_STATE)):
            print("\n== ARM A (%s): UE5_Shutdown -> UE5_AutoStart -> poke at initState RUNNING ==" % label)
            i0, p0 = size(initlog), size(pipelog)
            print("   UE5_Shutdown: %s" % (call_export("UE5_Shutdown", a.process, a.dll),))
            print("   UE5_AutoStart: %s" % (call_export("UE5_AutoStart", a.process, a.dll),))
            t0 = time.time()
            while time.time() - t0 < 15 and m.i32(base + MP.OFF_INITSTATE) != 1:
                time.sleep(0.002)
            st = m.i32(base + MP.OFF_INITSTATE)
            print("   initState %d after %.0f ms" % (st, (time.time() - t0) * 1000))
            res, params, el, err = MP.poke(m, base, op, timeout=90.0, cmd=cmd)
            served_state = st
            ok = res >= 0 and (cmd != CMD_PROTECT or params[2] == 1)
            print("   reply: result=%d params[0:4]=%s err=%r" % (res, params[:4].hex(), err[:80]))
            t1 = time.time()
            while time.time() - t1 < 60 and m.i32(base + MP.OFF_INITSTATE) != MP.INIT_READY:
                time.sleep(0.05)
            time.sleep(1.0)
            verdict = score(label, tail(initlog, i0), tail(pipelog, p0), served_state, ok, el)
            print("   ARM A %s: %s -- %s" % (label, verdict[0], verdict[1]))
            results.append(verdict[0])
            if cmd == CMD_PROTECT and verdict[0] == "PASS":
                r2 = MP.poke(m, base, op, timeout=10.0, cmd=cmd)
                print("   retry after READY: result=%d params[0:4]=%s (must match)" % (r2[0], r2[1][:4].hex()))
                if (r2[0], r2[1][2]) != (res, params[2]):
                    results.append("FAIL")
    else:
        print("\n== ARM B: poke the instant scan-0.log shows 'FindAll: Complete' ==")
        if m.i32(base + MP.OFF_INITSTATE) == MP.INIT_READY:
            print("MISS: the process is already READY -- arm B needs a freshly injected process")
            return 2
        # Only a line stamped AFTER this rig started counts: until the new DLL's logger rotates it, scan-0.log is
        # the PREVIOUS process's file, and it already contains "FindAll: Complete".
        stamp0 = time.strftime("%Y-%m-%d %H:%M:%S", time.localtime(t_start))
        t0 = time.time()
        seen = None
        while time.time() - t0 < 120 and not seen:
            txt = tail(scanlog, 0) if os.path.exists(scanlog) else ""
            for ln in txt.splitlines():
                if "FindAll: Complete" in ln and ln[1:20] >= stamp0:
                    seen = time.time()
                    break
            if not seen:
                time.sleep(0.003)
        if not seen:
            print("MISS: 'FindAll: Complete' never appeared")
            return 2
        st = m.i32(base + MP.OFF_INITSTATE)
        send = time.strftime("%H:%M:%S", time.localtime(seen)) + (".%03d" % int((seen % 1) * 1000))
        res, params, el, err = MP.poke(m, base, MP.QUERY_OP_GWORLD, timeout=90.0, cmd=MP.CMD_QUERY_PTR)
        print("   poke sent ~%s at initState %d; reply result=%d in %.0f ms" % (send, st, res, el * 1000))
        t1 = time.time()
        while time.time() - t1 < 60 and m.i32(base + MP.OFF_INITSTATE) != MP.INIT_READY:
            time.sleep(0.05)
        time.sleep(1.0)
        verdict = score("B", tail(initlog, 0), tail(pipelog, 0), st, res >= 0, el)
        print("   ARM B: %s -- %s" % verdict)
        results.append(verdict[0])
    print("\nL54 %s: %s" % (a.arm, "PASS" if results and all(r == "PASS" for r in results) else results))
    return 0 if results and all(r == "PASS" for r in results) else 1


if __name__ == "__main__":
    sys.exit(main())
