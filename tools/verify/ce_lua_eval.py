"""Run a Lua chunk inside a RUNNING Cheat Engine, through the AOBMaker bridge, and print what it returns.

    py tools/verify/ce_lua_eval.py "return getOpenedProcessID()"
    py tools/verify/ce_lua_eval.py --file probe.lua

WHY. A live check of anything we push into Cheat Engine is decided by CE's own state -- the address list, the hex and
disassembler views, a registered symbol. A screenshot of CE shows a little of that, inexactly; a Lua chunk reads all of
it exactly. ce_bridge_probe.py does this for one fixed question; this runs any chunk.

HOW. The chunk becomes the body of a function inside an Auto Assembler record's {$lua} block, pushed with CreateAAScript
and autoActivate, so CE runs it on its main thread. Its return value (tostring) goes to a result file, read back here.
The record is named "zz-probe <token>"; each run first deletes the earlier "zz-probe" records, so the address list keeps
at most one of them. The chunk never print()s: a print opens CE's Lua Engine window over CE.

Needs ONE Cheat Engine running with the AOBMaker plugin (working-lessons 3.wa). The request is compact JSON (AOBMaker's
reply rule 1). Exit 0 = the chunk ran and returned; 1 = it raised (the error is printed); 2 = no answer.
"""
import argparse
import json
import pathlib
import struct
import sys
import time
import uuid

sys.stdout.reconfigure(encoding="utf-8", errors="replace")
BRIDGE = "\\\\.\\pipe\\AOBMakerCEBridge"
OUT_DIR = pathlib.Path(__file__).resolve().parents[2] / "out" / "ce_lua_eval"

TEMPLATE = r'''[ENABLE]
{$lua}
if syntaxcheck then return end
local __ok, __res = pcall(function()
  local al = getAddressList()
  for i = al.Count - 1, 0, -1 do
    local r = al[i]
    if r.Description:sub(1, 9) == "zz-probe " and r.Description ~= "%DESC%" then r.destroy() end
  end
  local chunk = function()
%CODE%
  end
  return tostring(chunk())
end)
local __f = io.open([[%OUT%]], "w")
if __f then __f:write((__ok and "ok\n" or "err\n") .. tostring(__res)) __f:close() end
{$asm}
[DISABLE]
'''


# --state: the CE state a push is judged by -- the opened process, where the hex and disassembler views stand, and every
# record (depth, description, type, address, active). One line per fact, tab-separated.
STATE = r'''
  local lines = {}
  local function add(...) lines[#lines + 1] = table.concat({...}, "\t") end
  add("pid", getOpenedProcessID())
  local mv = getMemoryViewForm()
  add("memview.visible", tostring(mv.Visible))
  add("hex.top", string.format("%X", mv.HexadecimalView.TopAddress))
  add("disasm.selected", string.format("%X", mv.DisassemblerView.SelectedAddress))
  local al = getAddressList()
  for i = 0, al.Count - 1 do
    local r = al[i]
    local depth, p = 0, r.Parent
    while p do depth = depth + 1; p = p.Parent end
    if r.Description:sub(1, 9) ~= "zz-probe " then
      add("rec", depth, r.Description, tostring(r.Type), r.Address or "", string.format("%X", r.CurrentAddress or 0),
          tostring(r.Active), tostring(r.ShowAsHex))
    end
  end
  return table.concat(lines, "\n")
'''


def bridge_call(req, tries=20):
    last = None
    for _ in range(tries):
        try:
            with open(BRIDGE, "r+b", buffering=0) as p:
                b = json.dumps(req, separators=(",", ":")).encode("utf-8")
                p.write(struct.pack("<I", len(b)) + b)
                n = struct.unpack("<I", p.read(4))[0]
                return json.loads(p.read(n))
        except OSError as e:   # errno 22 = busy between two instances, not absent (working-lessons 3.wa)
            last = e
            time.sleep(0.5)
    raise SystemExit("ce_lua_eval: the AOBMaker bridge did not answer: %s -- is CE running with the plugin?" % last)


def run(code, timeout=60.0):
    OUT_DIR.mkdir(parents=True, exist_ok=True)
    token = uuid.uuid4().hex[:8]
    out = OUT_DIR / ("%s.txt" % token)
    if not all(ord(c) < 0x80 for c in str(out)):
        raise SystemExit("ce_lua_eval: the result path must be ASCII (CE's io.open takes bytes)")
    desc = "zz-probe " + token
    script = TEMPLATE.replace("%DESC%", desc).replace("%OUT%", str(out)).replace("%CODE%", code)
    r = bridge_call({"type": "CreateAAScript", "description": desc, "script": script, "autoActivate": True})
    if not r.get("success"):
        raise SystemExit("ce_lua_eval: CreateAAScript refused: %s" % r)
    deadline = time.time() + timeout
    while time.time() < deadline and not out.exists():
        time.sleep(0.2)
    if not out.exists():
        return None
    time.sleep(0.2)
    status, _, body = out.read_text(encoding="utf-8", errors="replace").partition("\n")
    return status == "ok", body


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("code", nargs="?")
    ap.add_argument("--file")
    ap.add_argument("--state", action="store_true", help="dump the opened process, the two views and every record")
    ap.add_argument("--timeout", type=float, default=60.0)
    a = ap.parse_args()
    code = STATE if a.state else (pathlib.Path(a.file).read_text(encoding="utf-8") if a.file else a.code)
    if not code:
        raise SystemExit(__doc__)
    res = run(code, a.timeout)
    if res is None:
        print("ce_lua_eval: no answer within %.0f s -- CE did not run the record" % a.timeout)
        return 2
    ok, body = res
    print(body)
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main())
