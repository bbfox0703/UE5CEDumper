"""Ask a RUNNING Cheat Engine, through the AOBMaker plugin's pipe, what it sees of a process -- no clicking.

    py tools/verify/ce_bridge_probe.py <pid> <result file>

WHY. Several [PATH-SHAPE-2026-09-25] live checks are questions about CE's own view: does CE load our proxy's exports
from a folder whose path the ANSI code page cannot hold ([PATH-ES2-CE-SYMBOLS] -- CE's symbol handler goes through
GetModuleFileNameExA + dbghelp with the ANSI path), and what does CE call the modules. Driving CE's windows for that is
slow and fragile; the AOBMaker plugin (\\\\.\\pipe\\AOBMakerCEBridge, docs in the AOBMaker repo's API-CEPlugin.md) can add an
Auto Assembler record and enable it, and a {$lua} block in it runs in CE.

WHAT THE RECORD DOES: openProcess(<pid>), reinitializeSymbolhandler(true), waitForExports() (all three in CE's
celua.txt), then writes key=value lines to <result file>: the pid CE attached to, getAddressSafe('UE5_Init') and
getAddressSafe('version.UE5_Init'), and for every module whose name holds 'version' or 'shipping' its name and path as
HEX of the raw bytes CE's Lua hands out (the ANSI bytes -- decoded here, nothing is guessed). The record is left ticked:
the session kills CE afterwards. The result path should be ASCII (CE's io.open takes it as bytes).

Needs: CE running with the AOBMaker plugin (a build of 2026-09-08 or later: json.dumps' spacing). Not a gate.
"""
import ctypes
import json
import pathlib
import struct
import sys
import time

sys.stdout.reconfigure(encoding="utf-8", errors="replace")
PIPE = r"\\.\pipe\AOBMakerCEBridge"


def call(req):
    with open(PIPE, "r+b", buffering=0) as p:
        b = json.dumps(req).encode("utf-8")
        p.write(struct.pack("<I", len(b)) + b)
        n = struct.unpack("<I", p.read(4))[0]
        return json.loads(p.read(n))


LUA = r'''[ENABLE]
{$lua}
if syntaxcheck then return end
local out = {}
local function add(k, v) out[#out + 1] = k .. "=" .. tostring(v) end
local function hex(s) return ((s or ""):gsub(".", function(c) return string.format("%02X", c:byte()) end)) end
local function addr(sym) local a = getAddressSafe(sym) return a and string.format("%X", a) or "nil" end
local ok, err = pcall(function()
  openProcess(%PID%)
  reinitializeSymbolhandler(true)
  waitForExports()
  add("pid", getOpenedProcessID())
  add("UE5_Init", addr("UE5_Init"))
  add("version.UE5_Init", addr("version.UE5_Init"))
  for _, m in ipairs(enumModules() or {}) do
    local n = (m.Name or ""):lower()
    if n:find("version", 1, true) or n:find("shipping", 1, true) then
      add("module", hex(m.Name) .. " " .. string.format("%X", m.Address or 0) .. " " .. hex(m.PathToFile))
    end
  end
end)
add("ok", ok)
if not ok then add("err", err) end
local f = io.open([[%OUT%]], "w")
if f then f:write(table.concat(out, "\n") .. "\n") f:close() end
{$asm}
[DISABLE]
'''


def decode(h: str) -> str:
    raw = bytes.fromhex(h) if h else b""
    acp = ctypes.windll.kernel32.GetACP()
    return f"{raw.decode(f'cp{acp}', 'replace')!r} (bytes {raw.hex(' ') if len(raw) < 48 else raw[:48].hex(' ') + ' ...'})"


def main(argv):
    if len(argv) != 2:
        raise SystemExit(__doc__)
    pid, result = int(argv[0]), pathlib.Path(argv[1]).resolve()
    if not all(ord(c) < 0x80 for c in str(result)):
        raise SystemExit("the result path must be ASCII (CE's io.open takes it as bytes)")
    if result.exists():
        result.unlink()
    script = LUA.replace("%PID%", str(pid)).replace("%OUT%", str(result))
    r = call({"type": "CreateAAScript", "description": f"ue5-probe pid {pid}", "script": script, "autoActivate": True})
    print("CreateAAScript:", r)
    deadline = time.time() + 90
    while time.time() < deadline and not result.exists():
        time.sleep(0.5)
    if not result.exists():
        raise SystemExit("no result file within 90 s -- CE did not run the record (see its Lua Engine window)")
    time.sleep(0.5)
    for line in result.read_text(encoding="latin-1").splitlines():
        k, _, v = line.partition("=")
        if k == "module":
            name, base, path = (v.split(" ") + ["", "", ""])[:3]
            print(f"  module {base}: name {decode(name)}\n         path {decode(path)}")
        else:
            print(f"  {k} = {v}")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
