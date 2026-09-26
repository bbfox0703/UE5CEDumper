"""Does AOBMaker's SymbolScanner find a module BY NAME, or only through its main-exe fallback? Asked of a running CE.

    py tools/verify/ce_symbol_match_check.py <pid> <module name> <aob> <pos> <aoblen> <out dir>

WHY ([PATH-AOBMAKER-ANSI-MATCH], fixed in AOBMaker ce8247c). The plugin's generated AOBScanModuleUE compared only CE's
ANSI mod.Name with the UTF-8 module string, so a non-ASCII name the ACP holds (DumperTest51遊戲-… on cp950) never
matched BY NAME. It still "worked" for the main exe, because the script then falls back to `process` and prints
"[SymbolScanner] module <name> not found or no match; falling back to <process>". So the symbol's address alone cannot
tell the fix from the fallback; this rig also captures CE's print() output and reports whether the fallback ran.

STEPS (all through \\\\.\\pipe\\AOBMakerCEBridge, see the AOBMaker repo's API-CEPlugin.md):
  1. an AA record whose {$lua} hooks print() to <out dir>\\ce_print.txt, then openProcess(<pid>) and
     reinitializeSymbolhandler(true) (both in CE's celua.txt);
  2. CreateSymbolScript -- the request UE5DumpUI's "Register GWorld symbol" sends -- with the given module name (UTF-8);
  3. an AA record that writes getAddressSafe('ue5_symcheck') to <out dir>\\ce_symbol.txt.
<out dir> must be ASCII. The records are left ticked: the session kills CE afterwards. Not a gate.
"""
import json
import pathlib
import struct
import sys
import time

sys.stdout.reconfigure(encoding="utf-8", errors="replace")
PIPE = "\\\\.\\pipe\\AOBMakerCEBridge"
SYMBOL = "ue5_symcheck"


def call(req):
    with open(PIPE, "r+b", buffering=0) as p:
        b = json.dumps(req).encode("utf-8")
        p.write(struct.pack("<I", len(b)) + b)
        n = struct.unpack("<I", p.read(4))[0]
        return json.loads(p.read(n))


HOOK = r'''[ENABLE]
{$lua}
if syntaxcheck then return end
local path = [[%PRINT%]]
local realPrint = print
print = function(...)
  local t = {}
  for i = 1, select("#", ...) do t[#t + 1] = tostring(select(i, ...)) end
  local f = io.open(path, "a")
  if f then f:write(table.concat(t, "\t") .. "\n") f:close() end
  realPrint(...)
end
openProcess(%PID%)
reinitializeSymbolhandler(true)
print("[hook] attached " .. tostring(getOpenedProcessID()))
{$asm}
[DISABLE]
'''

QUERY = r'''[ENABLE]
{$lua}
if syntaxcheck then return end
local a = getAddressSafe("%SYM%")
local f = io.open([[%OUT%]], "w")
if f then f:write(a and string.format("%X", a) or "nil") f:close() end
{$asm}
[DISABLE]
'''


def wait_for(p: pathlib.Path, secs=60):
    end = time.time() + secs
    while time.time() < end and not p.exists():
        time.sleep(0.5)
    time.sleep(0.5)
    return p.exists()


def main(argv):
    if len(argv) != 6:
        raise SystemExit(__doc__)
    pid, module, aob, pos, aoblen, out = int(argv[0]), argv[1], argv[2], int(argv[3]), int(argv[4]), pathlib.Path(argv[5]).resolve()
    if not all(ord(c) < 0x80 for c in str(out)):
        raise SystemExit("<out dir> must be ASCII")
    out.mkdir(parents=True, exist_ok=True)
    printed, symfile = out / "ce_print.txt", out / "ce_symbol.txt"
    for f in (printed, symfile):
        if f.exists():
            f.unlink()
    print("hook:", call({"type": "CreateAAScript", "description": "ue5-symcheck hook", "autoActivate": True,
                         "script": HOOK.replace("%PRINT%", str(printed)).replace("%PID%", str(pid))}))
    if not wait_for(printed):
        raise SystemExit("the print hook never ran")
    print("symbol:", call({"type": "CreateSymbolScript", "name": "ue5-symcheck GWorld", "aob": aob, "pos": pos,
                           "aoblen": aoblen, "symbol": SYMBOL, "module": module, "autoActivate": True}))
    time.sleep(3)
    print("query:", call({"type": "CreateAAScript", "description": "ue5-symcheck query", "autoActivate": True,
                          "script": QUERY.replace("%SYM%", SYMBOL).replace("%OUT%", str(symfile))}))
    if not wait_for(symfile):
        raise SystemExit("the query never ran")
    lines = printed.read_text(encoding="utf-8", errors="replace").splitlines()
    fell_back = [ln for ln in lines if "falling back" in ln]
    print(f"\n  symbol {SYMBOL} = {symfile.read_text().strip()}")
    print(f"  fallback taken: {bool(fell_back)}")
    for ln in lines:
        print("   print>", ln)
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
