--[[
  dll_size_text_test.lua
  UE5CEDumper -- executable test for the `[STALEDLL]` (b) size readout in UE5CEDumper.CT

  WHAT IT CHECKS
    `ue5_dllFileSize` / `ue5_dllSizeText` are what stop a stale UE5Dumper.dll being
    resolved SILENTLY. The build stamp is not a C ABI export, the DLL is not injected
    yet when the path is reported, and CE Lua has no stat-by-path API -- so file SIZE
    is the whole signal. If it is wrong or unreadable, the row's premise collapses.

    Step 2 of that row is the real test and it needs two files that genuinely differ:
      * Cheat Engine's install folder holds a ~0.5 MB February build
      * dist/ holds the current ~2.75 MB one
    Both are read here, and the test FAILS if they are not distinguishable.

  WHY IT EXTRACTS FROM THE .CT RATHER THAN RE-IMPLEMENTING
    working-lessons 2.5: a rig that RUNS the shipped script beats any number of
    assertions about its text. The two functions are lifted verbatim out of
    dist/UE5CEDumper.CT and executed, so this cannot pass against a .CT that no
    longer contains them.

  RUNNING IT
      lua scripts/tests/dll_size_text_test.lua
    Exit 0 = all pass, 1 = a failure (with the case named).

  Same standing as the sibling rigs: a manual tool, deliberately not in build.ps1/CI,
  because a standalone `lua` is not a declared dependency and a step that silently
  skips is worse than one that is run on purpose.
]]

local CT = (arg and arg[0] or ''):gsub('[^/\\]*$', '') .. '../../dist/UE5CEDumper.CT'

local fails, checks = 0, 0
local function check(name, cond, got)
  checks = checks + 1
  if cond then
    print(string.format("  ok    %s", name))
  else
    fails = fails + 1
    print(string.format("  FAIL  %s   got: %s", name, tostring(got)))
  end
end

-- ---- lift the two functions out of the shipped .CT -------------------------
local f = assert(io.open(CT, "rb"), "cannot open " .. CT)
local ct = f:read("a"); f:close()

local src = ct:match("(function%s+ue5_dllFileSize.-\nend)")
local src2 = ct:match("(function%s+ue5_dllSizeText.-\nend)")
if not src or not src2 then
  print("FAIL: could not find ue5_dllFileSize / ue5_dllSizeText in " .. CT)
  print("      The .CT no longer carries the [STALEDLL] size readout.")
  os.exit(1)
end
assert(load(src))()
assert(load(src2))()
print("loaded ue5_dllFileSize + ue5_dllSizeText verbatim from the shipped .CT")

-- ---- the two real DLLs ------------------------------------------------------
local STALE = [[C:\Program Files\Cheat Engine\UE5Dumper.dll]]   -- the Feb build
local FRESH = [[D:\Github\UE5CEDumper\dist\UE5Dumper.dll]]      -- current

local function sizeOf(p) local h = io.open(p, "rb"); if not h then return nil end
  local n = h:seek("end"); h:close(); return n end

local sStale, sFresh = sizeOf(STALE), sizeOf(FRESH)
print(string.format("on disk: stale=%s  fresh=%s", tostring(sStale), tostring(sFresh)))

print("\n-- step 1: the readout's shape --")
local tFresh = ue5_dllSizeText(FRESH)
print("  dist   -> " .. tFresh)
check("reports 'N bytes (X.X MB)'", tFresh:match("^%d+ bytes %(%d+%.%d MB%)$") ~= nil, tFresh)
check("byte count matches the real file size",
      sFresh ~= nil and tFresh:match("^(%d+)") == tostring(sFresh), tFresh)

print("\n-- step 2: a stale build must read DIFFERENTLY --")
if not sStale then
  print("  SKIP: no DLL in Cheat Engine's install folder -- step 2 needs the stale one")
else
  local tStale = ue5_dllSizeText(STALE)
  print("  CE dir -> " .. tStale)
  check("stale reports its own size", tStale:match("^(%d+)") == tostring(sStale), tStale)
  check("the two are DISTINGUISHABLE", tStale ~= tFresh, tStale .. " vs " .. tFresh)
  local mbS = tonumber(tStale:match("%((%d+%.%d) MB%)"))
  local mbF = tonumber(tFresh:match("%((%d+%.%d) MB%)"))
  check("the MB figures differ by more than 1 MB (0.5 vs 2.7 class)",
        mbS and mbF and (mbF - mbS) > 1.0, string.format("%s vs %s", tostring(mbS), tostring(mbF)))
end

print("\n-- negative controls: it must never throw --")
check("missing file -> the 'unknown' sentinel",
      ue5_dllSizeText([[Z:\no\such\file.dll]]) == "unknown (could not read the file)",
      ue5_dllSizeText([[Z:\no\such\file.dll]]))
check("nil path -> the 'unknown' sentinel",
      ue5_dllSizeText(nil) == "unknown (could not read the file)", ue5_dllSizeText(nil))
check("empty path -> the 'unknown' sentinel",
      ue5_dllSizeText("") == "unknown (could not read the file)", ue5_dllSizeText(""))
check("ue5_dllFileSize(nil) is nil, not 0 (0 would read as a real empty file)",
      ue5_dllFileSize(nil) == nil, tostring(ue5_dllFileSize(nil)))

print(string.format("\n%d checks, %d failure(s)", checks, fails))
os.exit(fails == 0 and 0 or 1)
