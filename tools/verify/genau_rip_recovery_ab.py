r"""Genau RIP decode — the half `genau_rip_ab.py` could NOT reach: GObjects / GNames addresses.

    py tools/verify/genau_rip_recovery_ab.py

WHAT WAS ALREADY SETTLED (`[GENAURIP-AB-2026-08-19]`). On notepad++, the fix's WIN was measured
(candidates 4085 → 4083, reproduced on 4 runs) and the acceptance criterion was met **for GWorld
only** — because on a non-UE host GObjects and GNames never resolve at all, so "the address did not
move" was vacuous for two of the three the row names.

WHY A GAME COULDN'T CLOSE IT EITHER. All five call sites of `Macht::IsRipRelativeModRM` live in
RECOVERY paths (`DataScanGObjectsCandidates`, `FindGObjectsStaticStruct`, `ResolveSymbolExport`,
`FindGNamesByStringRef` ×2). On a healthy title the AOB wins on the first pattern and **not one of
them runs**, so a game hands you two identical logs for the worst possible reason: the code under
test never executed. That is the deadlock the row sat in.

⭐ THE WAY OUT IS THIS PROJECT'S OWN PRECEDENT. The `PEHOOK` rows staged their TABLE arm by
*temporarily removing signatures so DumperTest mis-detects*. Same trick: force
`ScanForTarget`'s result to 0 in `FindGObjects` and `FindGNames`, and the recovery paths run on a
real UE host that actually HAS a GObjects and a GNames to find.

THE 2×2 THIS BUILDS AND RUNS — three DLLs from one tree in one session, differing only in the lines
named:

    post : AOB forced to fail                            (current predicate)
    pre  : AOB forced to fail + predicate reverted        (drop the `mod == 00` half)

and `dist`'s untouched DLL supplies the AOB-resolved baseline.

WHAT IS ASSERTED — and this list was CORRECTED by the first run, which is the useful part:
  1. the recovery path really RAN on both sides (the fallback log line is present)  — else vacuous
  2. GNames resolves identically on both sides AND matches the AOB baseline         — the row's
     criterion, closed for GNames
  3. the candidate count drops by a margin far larger than its run-to-run variance  — the win
  4. GObjects is REPORTED, not asserted. See below.

⛔ GOBJECTS CANNOT BE AN ACCEPTANCE CRITERION HERE, AND THE STAGING IS WHAT REVEALED IT.
Forced onto the data-scan fallback, DumperTest's `ValidateGObjects` accepts a FALSE POSITIVE: the
run reports `UE5_Init: Complete` with an object count of 583 or 2,556,928 against a real 25,179,
and the resolved address is on the HEAP, so it moves every launch anyway. Which false positive wins
depends on live heap contents — measured: the post side picked the same instruction on all three
runs, the pre side picked two different ones. So the two sides differ for a reason that is **not a
regression**, and comparing the addresses would be reading noise as signal.

⭐ Staging a path makes it RUN; it does not make it MEANINGFUL. Closing the GObjects half still
needs a UE title whose GObjects AOB genuinely fails AND whose data scan then finds the real pool.

⚠ ASLR. Raw addresses are only comparable if the module did not rebase between runs, so the rig
parses the `code=[0x…-0x…]` range the data scan logs and REFUSES to compare unless the base is
identical across the runs it is comparing. That is checked, not assumed.

⚠ Every source edit is restored BYTE-EXACT in a `finally` and the tree is rebuilt, so `dist` is left
holding the real DLL (working-lessons §2.11).
"""
import pathlib
import re
import shutil
import subprocess
import sys
import time

HERE = pathlib.Path(__file__).resolve().parent
ROOT = HERE.parents[1]
sys.path.insert(0, str(HERE))
sys.stdout.reconfigure(encoding="utf-8", errors="replace")

from pipe_client import PipeClient  # noqa: E402

OUT = ROOT / "out" / "genau"
DIST = ROOT / "dist" / "UE5Dumper.dll"
GENAU = ROOT / "dll" / "src" / "Genau.cpp"
MACHT = ROOT / "dll" / "src" / "Macht.h"
LOGDIR = pathlib.Path.home() / "AppData/Local/UE5CEDumper/Logs/DumperTest"
EXE = (r"D:\UE_Analyze_data\for testing\DumperTest\Development\Windows"
       r"\DumperTest\Binaries\Win64\DumperTest.exe")
PS = ["powershell", "-NoProfile", "-ExecutionPolicy", "Bypass", "-File",
      str(ROOT / "build.ps1"), "-Target", "DLL", "-NoBumpBuildNumber"]

# ── the two source edits ───────────────────────────────────────────────────
FORCE_GOBJ = ("""    LogScanReport(report);

    if (result) {
        s_gobjectsMethod = ScanMethodFor(report);
    } else {
        // Fallback: exhaustive data-section pointer scan""",
              """    LogScanReport(report);

    result = 0;   // STAGING: force the recovery path (genau_rip_recovery_ab.py)
    if (result) {
        s_gobjectsMethod = ScanMethodFor(report);
    } else {
        // Fallback: exhaustive data-section pointer scan""")

FORCE_GNAM = ("""    if (result) {
        s_gnamesMethod = ScanMethodFor(report);
    } else {
        // Tier 2: string-reference fallback""",
              """    result = 0;   // STAGING: force the recovery path (genau_rip_recovery_ab.py)
    if (result) {
        s_gnamesMethod = ScanMethodFor(report);
    } else {
        // Tier 2: string-reference fallback""")

REVERT_PRED = ("""    return (modrm & 0x07) == 0x05     // r/m = 101
        && (modrm & 0xC0) == 0x00;    // mod = 00""",
               """    return (modrm & 0x07) == 0x05;    // PRE-FIX: r/m only, mod half omitted""")


def say(s=""):
    enc = sys.stdout.encoding or "utf-8"
    sys.stdout.write(str(s).encode(enc, "replace").decode(enc, "replace") + chr(10))
    sys.stdout.flush()


def sub(path, pair, label):
    LF, CRLF = chr(10), chr(13) + chr(10)
    raw = path.read_bytes()
    crlf = CRLF.encode() in raw
    t = raw.decode("utf-8").replace(CRLF, LF)
    old, new = pair
    n = t.count(old)
    assert n == 1, "%s: %s anchor x%d" % (path.name, label, n)
    path.write_bytes(t.replace(old, new).replace(LF, CRLF if crlf else LF).encode("utf-8"))


def build(label):
    r = subprocess.run(PS, capture_output=True, text=True, encoding="utf-8", errors="replace")
    say("   build %-22s %s" % (label, "OK" if r.returncode == 0 else "FAILED"))
    if r.returncode != 0:
        say((r.stdout or "")[-1500:])
        return False
    return True


def run_host(dll, label):
    """Launch DumperTest, inject `dll`, return (pointers, code_base, log_tail)."""
    subprocess.run(["taskkill", "/F", "/IM", "DumperTest.exe"], capture_output=True)
    time.sleep(1.5)
    subprocess.Popen([EXE, "-windowed", "-ResX=1024", "-ResY=576", "-ExecCmds=t.MaxFPS 60"])
    time.sleep(22)
    r = subprocess.run([sys.executable, str(HERE / "inject.py"), "--name", "DumperTest",
                        "--dll", str(dll)],
                       capture_output=True, text=True, encoding="utf-8", errors="replace")
    if r.returncode != 0:
        say("   inject FAILED: %s" % (r.stdout or r.stderr or "")[-200:])
        return None, None, ""
    # ⚠ POLL, do not sleep a constant. The PRE side is measurably SLOWER to become
    # ready — the broken predicate hands the data scan ~1,500 more candidates and then
    # validates a bogus 2.5M-object pool, so init runs long. A flat `sleep(2)` worked on
    # the post side and failed on the pre side twice in a row, which reads as "the DLL
    # crashed" and is not what happened. The wait is now a real deadline and the time is
    # REPORTED, because the difference is itself a measurement.
    t0 = time.time()
    p = None
    while time.time() - t0 < 180:
        try:
            with PipeClient() as c:
                c.assert_build()
                c.ensure_scanned()
                p = c.request("get_pointers")
            break
        except Exception:
            time.sleep(3)
    ready = time.time() - t0
    if p is None:
        say("   %-6s NEVER became ready within 180 s" % label)
        return None, None, ""
    say("   %-6s ready after %.0f s" % (label, ready))
    scan = LOGDIR / "scan-0.log"
    txt = scan.read_text(encoding="utf-8", errors="replace") if scan.is_file() else ""
    m = re.findall(r"code=\[0x([0-9A-Fa-f]+)-", txt)
    base = m[-1] if m else None
    say("   %-6s gobjects=%s gnames=%s gworld=%s  code_base=0x%s"
        % (label, p.get("gobjects"), p.get("gnames"), p.get("gworld"), base or "?"))
    return p, base, txt


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    # A host left over from an aborted run still HOLDS the staged DLL, and the copy then
    # fails with WinError 32 after both builds have already run. Clear the field first.
    subprocess.run(["taskkill", "/F", "/IM", "DumperTest.exe"], capture_output=True)
    time.sleep(1.5)
    gbak, mbak = GENAU.read_bytes(), MACHT.read_bytes()
    post_dll, pre_dll = OUT / "recovery_post.dll", OUT / "recovery_pre.dll"
    try:
        say("== building the two staged DLLs ==")
        sub(GENAU, FORCE_GOBJ, "gobj")
        sub(GENAU, FORCE_GNAM, "gnam")
        if not build("post (AOB forced off)"):
            return 2
        shutil.copy2(DIST, post_dll)

        sub(MACHT, REVERT_PRED, "predicate")
        if not build("pre  (+ predicate reverted)"):
            return 2
        shutil.copy2(DIST, pre_dll)
    finally:
        GENAU.write_bytes(gbak)
        MACHT.write_bytes(mbak)
        say("   sources restored byte-exact: Genau=%s Macht=%s"
            % (GENAU.read_bytes() == gbak, MACHT.read_bytes() == mbak))
        build("restored (dist is real again)")

    say("")
    say("== baseline: the untouched DLL, AOB path ==")
    base_p, base_code, _ = run_host(DIST, "base")

    say("")
    say("== side POST: recovery path, current predicate ==")
    post_p, post_code, post_txt = run_host(post_dll, "post")

    say("")
    say("== side PRE: recovery path, predicate reverted ==")
    pre_p, pre_code, pre_txt = run_host(pre_dll, "pre")

    subprocess.run(["taskkill", "/F", "/IM", "DumperTest.exe"], capture_output=True)

    if not (base_p and post_p and pre_p):
        say("")
        say("NOT_RUNNABLE: a side failed to produce pointers")
        return 2

    say("")
    say("== 1. did the recovery path actually RUN on both staged sides? ==")
    fails = []
    for label, txt in (("post", post_txt), ("pre", pre_txt)):
        gobj = "All patterns failed, trying data-section scan fallback" in txt
        gnam = "All patterns failed, trying string-ref fallback" in txt
        say("   %-5s GObjects fallback=%s  GNames fallback=%s" % (label, gobj, gnam))
        if not gobj:
            fails.append("%s: the GObjects recovery path never ran — nothing was measured" % label)
        if not gnam:
            fails.append("%s: the GNames recovery path never ran — nothing was measured" % label)

    say("")
    say("== 2. did they RESOLVE? ==")
    for k in ("gobjects", "gnames", "gworld"):
        say("   %-9s base=%-16s post=%-16s pre=%s"
            % (k, base_p.get(k), post_p.get(k), pre_p.get(k)))
        for label, p in (("post", post_p), ("pre", pre_p)):
            v = str(p.get(k) or "0")
            if v in ("0", "0x0", "", "None"):
                fails.append("%s: %s did not resolve, so 'unchanged' would be vacuous" % (label, k))

    say("")
    say("== 3. ASLR check — comparable only if the module did not rebase ==")
    say("   code_base  base=0x%s  post=0x%s  pre=0x%s" % (base_code, post_code, pre_code))
    comparable = post_code is not None and post_code == pre_code
    if not comparable:
        fails.append("the staged runs loaded at different bases (%s vs %s) — raw addresses are "
                     "not comparable and the rig will not pretend otherwise" % (post_code, pre_code))

    say("")
    say("== 4. THE ACCEPTANCE CRITERION — the two targets it can be applied to ==")
    if comparable:
        for k in ("gnames", "gworld"):
            same = str(post_p.get(k)) == str(pre_p.get(k))
            agrees = str(base_p.get(k)) == str(post_p.get(k))
            say("   %-9s pre==post: %-5s   ==AOB baseline: %-5s   (%s)"
                % (k, same, agrees, post_p.get(k)))
            if not same:
                fails.append("%s MOVED between the two predicates — that is the regression the row "
                             "exists to catch" % k)
            if not agrees:
                fails.append("%s: recovery disagrees with the AOB baseline — worth understanding "
                             "before trusting either" % k)

    say("")
    say("== 5. GObjects — REPORTED, not asserted ==")
    say("   pre=%s  post=%s  (baseline via AOB: %s)"
        % (pre_p.get("gobjects"), post_p.get("gobjects"), base_p.get("gobjects")))
    say("   ⛔ Not an acceptance criterion on this host: forced onto the data-scan fallback,")
    say("      ValidateGObjects accepts a FALSE POSITIVE (object counts of 583 / 2,556,928 against")
    say("      a real 25,179) and the answer is a HEAP address, so it moves every launch. Which")
    say("      false positive wins depends on live heap. Comparing these two would read noise as")
    say("      signal. Closing this half needs a UE title whose GObjects AOB genuinely fails.")

    say("")
    say("== 6. THE WIN — candidate count, and its own variance ==")
    def cands(txt):
        import re as _re
        return [int(x) for x in _re.findall(r"Found (\d+) static pointers", txt)]
    cpre, cpost = cands(pre_txt), cands(post_txt)
    say("   pre =%s   post=%s" % (cpre or "?", cpost or "?"))
    if cpre and cpost:
        gap = cpre[-1] - cpost[-1]
        say("   gap = %d" % gap)
        say("   ⚠ Run-to-run variance measured at +-5 over 3 runs per side (live .data contents),")
        say("      so a gap of this size is signal. A gap under ~50 would NOT be.")
        if gap <= 50:
            fails.append("the candidate gap is %d, inside the noise floor — the win is not "
                          "demonstrated by this run" % gap)

    say("")
    if fails:
        say("Genau RIP recovery A/B: FAIL")
        for f in fails:
            say("   - %s" % f)
        return 1
    say("Genau RIP recovery A/B: PASS — the recovery paths ran on both sides, GNames resolved")
    say("     through them to a byte-identical address that also matches the AOB baseline, and the")
    say("     candidate count dropped far beyond its own variance. GObjects is reported, not")
    say("     asserted, for the reason in section 5.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
