"""Register [PIPE-PER-PROCESS-2026-11-06] item 5's last part: the CT's INIT_FAILED message from the DLL's OWN INIT_FAILED.

    py tools/verify/initfailed_ct.py --pid P [--ct dist/UE5CEDumper.CT] [--delay-ms 400]

WHAT. The .CT's "Inject DLL + Start Pipe Server" record injects the DLL and polls its mailbox's initState; on
INIT_FAILED (3) it shows a message naming the two real causes. The DLL publishes INIT_FAILED when UE5_Init aborted -- a
shutdown landed mid-scan -- so this ticks that record in Cheat Engine and fires UE5_Shutdown into the game while the
DLL's scan runs.

HOW, AND WHY THIS WAY. The record's tick holds CE's main thread through its whole poll (it pumps paint messages only),
so nothing scheduled on that thread can fire the shutdown, and a remote thread started from python.exe is what the AV
flags (working-lessons.md 3.8). So:
  * the tick runs from a CE timer (the bridge call returns first), with showMessage and print captured to a file;
  * a CE createThread worker -- it runs while the tick sleeps between polls -- waits for this file's plan (the
    mailbox's and UE5_Shutdown's addresses, which Python works out from the module list once the record has injected
    the DLL), waits for initState RUNNING, waits --delay-ms, and starts UE5_Shutdown with CE's createRemoteThread (a
    plain CreateRemoteThread at the export: no assembled stub, nothing that needs CE's main thread). It records every
    initState it sees.

WHICH WAY THE RACE GOES. UE5_Shutdown ends by writing initState IDLE; the aborted scan writes FAILED when it notices.
The message appears only if FAILED is the last write (or the poll catches it in between). The DLL's init log says
which happened ("UE5_Init ABORTED ... initState=3"); a run that missed says so and can be repeated.

PRECONDITIONS: game P with no UE5Dumper loaded; ONE Cheat Engine with the AOBMaker plugin (ce_lua_eval.py's bridge).
PASS (exit 0): the capture holds the CT's INIT_FAILED message, and the DLL's init log has the aborted init with
initState=3. The game is left for the caller to kill (it holds a half-started DLL).
"""
from __future__ import annotations

import argparse
import os
import pathlib
import re
import sys
import time

import pefile

HERE = pathlib.Path(__file__).resolve().parent
ROOT = HERE.parents[1]
sys.path.insert(0, str(HERE))
import ce_lua_eval                        # noqa: E402
from oldname_join import module_of, export_rva   # noqa: E402
from pipe_client import LOGROOT           # noqa: E402

sys.stdout.reconfigure(encoding="utf-8", errors="replace")
OUT = ROOT / "out" / "initfailed"
RECORD = "Inject DLL + Start Pipe Server"
FAILED_TEXT = "The DLL loaded and initialised, but the Pipe Server failed to start."


def lua(code: str, timeout: float = 60.0) -> str:
    res = ce_lua_eval.run(code, timeout)
    if res is None:
        raise SystemExit("FAIL: Cheat Engine did not run the chunk")
    ok, body = res
    if not ok:
        raise SystemExit(f"FAIL: the chunk raised: {body}")
    return body


def fwd(p: pathlib.Path) -> str:
    return str(p).replace("\\", "/")


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    ap.add_argument("--pid", type=int, required=True)
    ap.add_argument("--ct", default=str(ROOT / "dist" / "UE5CEDumper.CT"))
    ap.add_argument("--dll", default=str(ROOT / "dist" / "UE5Dumper.dll"))
    ap.add_argument("--delay-ms", type=int, default=400)
    a = ap.parse_args()
    pid, dll = a.pid, pathlib.Path(a.dll)
    if module_of(pid, dll):
        raise SystemExit(f"FAIL: {dll} is already loaded in {pid}; the record must inject it")
    OUT.mkdir(parents=True, exist_ok=True)
    plan, trace, cap = OUT / "plan.txt", OUT / "trace.txt", OUT / "capture.txt"
    for f in (plan, trace, cap):
        f.unlink(missing_ok=True)
    pe = pefile.PE(str(dll))
    since = time.strftime("%Y-%m-%d %H:%M:%S")

    print("CE:", lua(f"openProcess({pid}); loadTable([[{fwd(pathlib.Path(a.ct))}]], false); "
                     f"local r = getAddressList().getMemoryRecordByDescription([[{RECORD}]]); "
                     "return 'pid ' .. getOpenedProcessID() .. ', record ' .. tostring(r ~= nil)"), flush=True)

    # The worker first, then the tick. Both chunks return at once; the tick runs from a timer on CE's main thread.
    lua(f"""
createThread(function(th)
  local function w(s) local f = io.open([[{fwd(trace)}]], 'ab') if f then f:write(s .. string.char(10)) f:close() end end
  local t0 = getTickCount()
  local mb, sd
  while getTickCount() - t0 < 60000 do
    local f = io.open([[{fwd(plan)}]], 'rb')
    if f then local s = f:read('*a') f:close() local x, y = s:match('(%x+) (%x+)') if x then mb, sd = tonumber(x, 16), tonumber(y, 16) break end end
    sleep(20)
  end
  if not mb then w('no plan') return end
  w(string.format('plan mb=%X sd=%X at %d', mb, sd, getTickCount() - t0))
  local last, fired = -99, false
  local tRun
  while getTickCount() - t0 < 60000 do
    local ok, v = pcall(readInteger, mb + 12)
    v = ok and v or -1
    if v ~= last then w(string.format('%d initState=%s', getTickCount() - t0, tostring(v))) last = v end
    if v == 1 and not tRun then tRun = getTickCount() end
    if tRun and not fired and getTickCount() - tRun >= {a.delay_ms} then
      fired = true
      local rt = createRemoteThread(sd, 0)
      w(string.format('%d UE5_Shutdown remote thread started: %s', getTickCount() - t0, tostring(rt ~= nil)))
      if rt then rt.waitForThread(10000) w(string.format('%d UE5_Shutdown returned', getTickCount() - t0)) end
    end
    if fired and getTickCount() - tRun > 30000 then break end
    sleep(5)
  end
  w('done')
end)
return 'worker started'
""")
    print("CE:", lua(f"""
local t = createTimer(nil, false)
t.Interval = 100
t.OnTimer = function(s)
  s.destroy()
  local L, oldMsg, oldPrint = {{}}, showMessage, print
  showMessage = function(m) L[#L+1] = 'MSG<<' .. tostring(m) .. '>>' end
  print = function(...) local p = {{}} for i = 1, select('#', ...) do p[#p+1] = tostring((select(i, ...))) end L[#L+1] = 'PRINT<<' .. table.concat(p, ' | ') .. '>>' end
  UE5_DEBUG = 1
  local r = getAddressList().getMemoryRecordByDescription([[{RECORD}]])
  local ok, err = false, 'no record'
  if r then ok, err = pcall(function() r.Active = true end) end
  showMessage, print, UE5_DEBUG = oldMsg, oldPrint, nil
  L[#L+1] = 'pcall=' .. tostring(ok) .. ' err=' .. tostring(err) .. ' active_after=' .. tostring(r and r.Active)
  local f = io.open([[{fwd(cap)}]], 'wb') if f then f:write(table.concat(L, string.char(10))) f:close() end
end
t.Enabled = true
return 'tick scheduled'
"""), flush=True)

    deadline = time.time() + 30
    mod = None
    while time.time() < deadline and not mod:
        mod = module_of(pid, dll)
        time.sleep(0.05)
    if not mod:
        raise SystemExit("FAIL: the record did not inject the DLL within 30 s")
    mb = mod[0] + export_rva(pe, "g_invokeMailbox")
    sd = mod[0] + export_rva(pe, "UE5_Shutdown")
    plan.write_text(f"{mb:X} {sd:X}", encoding="ascii")
    print(f"DLL at 0x{mod[0]:X}; plan written (mailbox 0x{mb:X}, UE5_Shutdown 0x{sd:X})", flush=True)

    deadline = time.time() + 60
    while time.time() < deadline and not cap.exists():
        time.sleep(0.5)
    time.sleep(1)
    capture = cap.read_text(encoding="utf-8", errors="replace") if cap.exists() else "(no capture: the tick did not end)"
    deadline = time.time() + 40
    while time.time() < deadline and "done" not in (trace.read_text(encoding="utf-8") if trace.exists() else ""):
        time.sleep(0.5)
    print("\nthe worker's trace:\n" + (trace.read_text(encoding="utf-8") if trace.exists() else "(none)"))
    print("the record's capture:\n" + capture)

    lines = []
    for path in LOGROOT.glob("*/init-0.log"):
        try:
            text = path.read_text(encoding="utf-8", errors="replace")
        except OSError:
            continue
        lines += [ln for ln in text.splitlines() if ln.startswith("[") and ln[1:20] >= since
                  and re.search(r"UE5_Init ABORTED|UE5_AutoStart: pipe server|UE5_Shutdown: Cleaning", ln)]
    print("\nthe DLL's init log since the run's start:")
    for ln in sorted(lines):
        print("  " + ln[:240])
    shown = FAILED_TEXT in capture
    failed = any("init ABORTED -> initState=3" in ln for ln in lines)
    print()
    print(f"  {'PASS' if failed else 'FAIL'}  the DLL published INIT_FAILED itself (init ABORTED -> initState=3)")
    print(f"  {'PASS' if shown else 'FAIL'}  the CT showed its INIT_FAILED message")
    if failed and not shown:
        print("  (the shutdown's closing IDLE probably overwrote FAILED before the poll read it: repeat)")
    ok = shown and failed
    print("PASS" if ok else "FAIL")
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main())
