"""Frieren's subclass-slot getters, called live the way Cheat Engine calls them.

    py tools/verify/frieren_getters_live.py --pid <game pid> --build <N> --label <name>
    py tools/verify/frieren_getters_live.py --pid <game pid> --build <N> --label <name> --slot 0x78   # UProperty

WHY THIS EXISTS. `UE5_GetFieldStructClass` and `UE5_GetFieldPropertyClass` are C-ABI exports reached
only from CE Lua (`ue5_dissect.lua` flattens a StructProperty through the first), so no pipe rig had
ever called them. [STRUCTPROBE-ANY-NAME] gave both a kind check: before build 3595 each took the first
NAMED object near the subclass slot, so the struct getter answered an object property with its UClass
and the PropertyClass getter (which delegated to it) answered a StructProperty with its UScriptStruct.

HOW. Three steps; the pipe and CE can overlap, because the copy of the DLL that CE loads as a plugin
starts no pipe server (its DllMain refuses when the host is CE).
  1. Pipe: every StructProperty and object-family field of `DumperTestActor`, `DumperTestSubsystem` and
     every `/Game/` class. The pointer in each field's subclass slot is read with ReadProcessMemory --
     NOT through the DLL -- and named with `get_object`.
  2. CE, through the AOBMaker bridge (`\\\\.\\pipe\\AOBMakerCEBridge`, see ce_bridge_probe.py): an Auto
     Assembler record whose {$lua} block attaches to the game, resolves both exports and calls each on
     every field with `executeCodeEx(1, ...)` -- ue5_dissect.lua's own call -- writing the full 64-bit
     answers to a file.
  3. Compare. A StructProperty: the struct getter returns the slot's struct, the class getter 0. An
     object property: the class getter returns the slot's class, the struct getter 0.

Needs the game injected with the build under test (nothing else first) and ONE Cheat Engine running
with the AOBMaker plugin (working-lessons 3.wa: never a second one). The record is left ticked; kill CE
afterwards. On an FProperty engine the slot is derived as PropertyFamilyFor derives it; a UProperty engine (UE4 before
4.25) needs `--slot`, the `SubclassStart=` of the session's offsets-log SUMMARY line -- the pipe does not report it.
Exit 0 = every call answered as above, 1 = at least one did not, 2 = the rig could not run.
Output: out/frieren_getters/<label>_*.txt|json (gitignored).
"""
import argparse
import collections
import ctypes
import ctypes.wintypes as wt
import json
import pathlib
import struct
import sys
import time

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
from pipe_client import PipeClient           # noqa: E402

sys.stdout.reconfigure(encoding="utf-8", errors="replace")
ROOT = pathlib.Path(__file__).resolve().parents[2]
OUT_DIR = ROOT / "out" / "frieren_getters"
BRIDGE = "\\\\.\\pipe\\AOBMakerCEBridge"
OBJFAM = {"ObjectProperty", "ClassProperty", "WeakObjectProperty", "SoftObjectProperty",
          "SoftClassProperty", "InterfaceProperty", "LazyObjectProperty"}
NATIVE_CLASSES = {"DumperTestActor", "DumperTestSubsystem"}

k32 = ctypes.WinDLL("kernel32", use_last_error=True)
k32.OpenProcess.restype = wt.HANDLE
k32.ReadProcessMemory.argtypes = [wt.HANDLE, ctypes.c_void_p, ctypes.c_void_p, ctypes.c_size_t,
                                  ctypes.POINTER(ctypes.c_size_t)]


def read_ptr(h, addr):
    buf, got = ctypes.create_string_buffer(8), ctypes.c_size_t(0)
    if not k32.ReadProcessMemory(h, ctypes.c_void_p(addr), buf, 8, ctypes.byref(got)) or got.value != 8:
        return None
    return struct.unpack("<Q", buf.raw)[0]


LUA = r'''[ENABLE]
{$lua}
if syntaxcheck then return end
local out = {}
local ok, err = pcall(function()
  openProcess(%PID%)
  reinitializeSymbolhandler(true)
  waitForExports()
  local fs = getAddressSafe("UE5Dumper.UE5_GetFieldStructClass") or getAddressSafe("UE5_GetFieldStructClass")
  local fp = getAddressSafe("UE5Dumper.UE5_GetFieldPropertyClass") or getAddressSafe("UE5_GetFieldPropertyClass")
  out[#out + 1] = string.format("pid=%d", getOpenedProcessID())
  out[#out + 1] = string.format("sym=%X %X", fs or 0, fp or 0)
  if not fs or not fp or fs == 0 or fp == 0 then error("exports not resolved") end
  for line in io.lines([[%IN%]]) do
    local a = tonumber(line, 16)
    if a then
      local s, ws = executeCodeEx(1, 5000, fs, a)
      local p, wp = executeCodeEx(1, 5000, fp, a)
      out[#out + 1] = string.format("%X %s %s", a,
        s and string.format("%X", s) or ("ERR:" .. tostring(ws)),
        p and string.format("%X", p) or ("ERR:" .. tostring(wp)))
    end
  end
end)
out[#out + 1] = "ok=" .. tostring(ok)
if not ok then out[#out + 1] = "err=" .. tostring(err) end
local f = io.open([[%OUT%]], "w")
if f then f:write(table.concat(out, "\n") .. "\n") f:close() end
{$asm}
[DISABLE]
'''


def bridge_call(req, tries=20):
    last = None
    for _ in range(tries):
        try:
            with open(BRIDGE, "r+b", buffering=0) as p:
                b = json.dumps(req).encode("utf-8")
                p.write(struct.pack("<I", len(b)) + b)
                n = struct.unpack("<I", p.read(4))[0]
                return json.loads(p.read(n))
        except OSError as e:   # errno 22 = busy between two instances, not absent (working-lessons 3.wa)
            last = e
            time.sleep(0.5)
    raise SystemExit("frieren: the AOBMaker bridge did not answer: %s -- is CE running with the plugin?" % last)


def collect(c, h, slot_arg, all_classes=False):
    off = c.request("get_offsets")
    if off.get("use_fproperty"):
        slot = (off["fproperty_offset"] + (0x34 if off.get("case_preserving") else 0x2C) + 7) & ~7
        if slot_arg is not None and slot_arg != slot:
            raise SystemExit("frieren: --slot 0x%X disagrees with the derived FProperty slot 0x%X" % (slot_arg, slot))
    elif slot_arg is None:
        raise SystemExit("frieren: a UProperty engine -- pass --slot, the SubclassStart= of the offsets-log SUMMARY")
    else:
        slot = slot_arg
    classes = c.request("list_classes", game_only=False, limit=50000)["classes"]
    pick = [x for x in classes if all_classes or x.get("class_name") in NATIVE_CLASSES
            or "/Game/" in (x.get("class_path") or "")]
    # Keyed by the FProperty's ADDRESS: walk_class lists inherited fields too, so a Blueprint base's
    # `UberGraphFrame` came back once per subclass -- 22,342 rows for 7,506 properties on EVERSPACE 2.
    by_addr = {}
    for i in range(0, len(pick), 200):
        r = c.request("walk_class_batch", addrs=[x["class_addr"] for x in pick[i:i + 200]])
        for j, ci in enumerate(r.get("classes", [])):
            owner = pick[i + j]["class_name"]
            for f in ci.get("fields", []):
                if f.get("type") == "StructProperty" or f.get("type") in OBJFAM:
                    addr = int(f["addr"], 16)
                    if addr in by_addr:
                        by_addr[addr]["listed_by"] += 1
                        continue
                    by_addr[addr] = {"owner": owner, "name": f["name"], "type": f["type"], "addr": addr,
                                     "want_name": f.get("struct_type") or f.get("obj_class") or "", "listed_by": 1}
    fields = list(by_addr.values())
    names = {}
    for f in fields:
        raw = read_ptr(h, f["addr"] + slot)
        f["slot_ptr"] = raw or 0
        if raw and raw not in names:
            o = c.request("get_object", addr="0x%X" % raw)
            names[raw] = (o.get("name", ""), o.get("class", ""))
        f["slot_name"], f["slot_meta"] = names.get(raw, ("", ""))
    return slot, len(pick), fields


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--pid", type=int, required=True)
    ap.add_argument("--build", required=True, help="the build injected into the game (assert_build)")
    ap.add_argument("--label", required=True)
    ap.add_argument("--all-classes", action="store_true",
                    help="every class list_classes returns, not only the fixture's own and the /Game/ ones")
    ap.add_argument("--slot", type=lambda v: int(v, 0), default=None,
                    help="the subclass slot, e.g. 0x78 -- required on a UProperty engine")
    a = ap.parse_args()
    OUT_DIR.mkdir(parents=True, exist_ok=True)
    fin, fres = OUT_DIR / ("%s_fields.txt" % a.label), OUT_DIR / ("%s_ce.txt" % a.label)
    if not all(ord(ch) < 0x80 for ch in str(fres)):
        raise SystemExit("frieren: the output path must be ASCII (CE's io.open takes bytes)")

    h = k32.OpenProcess(0x0010 | 0x0400, False, a.pid)
    if not h:
        raise SystemExit("frieren: OpenProcess(%d) failed" % a.pid)
    with PipeClient(timeout=300.0) as c:
        print("build:", c.assert_build(a.build))
        slot, nclasses, fields = collect(c, h, a.slot, a.all_classes)
    print("slot +0x%X; %d classes list %d rows = %d distinct properties (%s)" % (
        slot, nclasses, sum(f["listed_by"] for f in fields), len(fields),
        dict(collections.Counter(f["type"] for f in fields))))
    fin.write_text("".join("%X\n" % f["addr"] for f in fields), encoding="ascii")

    if fres.exists():
        fres.unlink()
    script = LUA.replace("%PID%", str(a.pid)).replace("%IN%", str(fin)).replace("%OUT%", str(fres))
    print("CreateAAScript:", bridge_call({"type": "CreateAAScript", "description": "frieren getters %s" % a.label,
                                          "script": script, "autoActivate": True}))
    deadline = time.time() + 600
    while time.time() < deadline and not (fres.exists() and "ok=" in fres.read_text(encoding="latin-1")):
        time.sleep(1.0)
    if not fres.exists():
        print("frieren: no result file within 600 s -- CE did not run the record")
        return 2
    lines = fres.read_text(encoding="latin-1").splitlines()
    meta = [ln for ln in lines if "=" in ln]
    print("CE:", meta)
    if "ok=true" not in meta:
        return 2
    got = {}
    for ln in lines:
        p = ln.split(" ")
        if len(p) == 3 and "=" not in ln:
            got[int(p[0], 16)] = (p[1], p[2])

    tally, bad = collections.Counter(), []
    for f in fields:
        s, p = got.get(f["addr"], ("MISSING", "MISSING"))
        is_struct = f["type"] == "StructProperty"
        right, cross = (s, p) if is_struct else (p, s)
        want = "%X" % f["slot_ptr"]
        ok_right = right == want and f["slot_ptr"] != 0
        ok_cross = cross == "0"
        kind = "%s/%s" % (f["type"], f["slot_meta"] or "-")
        tally[(kind, ok_right, ok_cross)] += 1
        if not (ok_right and ok_cross):
            bad.append((f["owner"], f["name"], f["type"], f["want_name"], f["slot_name"], want, right, cross))
    for (kind, r_ok, x_ok), n in sorted(tally.items()):
        print("  %-48s right-kind %-5s cross-kind 0 %-5s  x%d" % (kind, r_ok, x_ok, n))
    for b in bad[:15]:
        print("   MISMATCH %s.%s (%s, wants %s; slot %s=0x%s) own-kind getter 0x%s, other getter 0x%s" % b)
    json.dump({"build": a.build, "slot": slot, "fields": fields, "ce": {"%X" % k: v for k, v in got.items()}},
              open(OUT_DIR / ("%s.json" % a.label), "w", encoding="utf-8"))
    print("VERDICT: %d of %d distinct properties answered right by both getters" % (len(fields) - len(bad), len(fields)))
    return 1 if bad else 0


if __name__ == "__main__":
    sys.exit(main())
