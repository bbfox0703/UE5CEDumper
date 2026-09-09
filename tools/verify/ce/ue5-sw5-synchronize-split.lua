-- SW5 harness — split the AOBMaker plugin's TWO pipe handlers deterministically.
--
-- Installed by tools/verify/sw5_push_branches.py into
--   <CheatEngineDir>\autorun\custom\ue5-sw5-synchronize-split.lua
--
-- WHY THIS EXISTS
-- ---------------
-- `PropertyXrefDialog.OnDisassembleClicked` awaits TWO independent Task<bool>s and branches on
-- the pair:
--     recorded && navigated -> GREEN   "Pushed <fn> -> CE disassembler @ <addr>"
--     recorded              -> AMBER   "Added <fn> ... but could not open the disassembler"
--     else                  -> RED     "CE refused the push ... nothing was added"
-- Only GREEN had ever been seen. The other two need CE to accept one call and refuse the other,
-- which is not something a user can arrange -- and killing Cheat Engine does NOT produce RED:
-- the button is gated on the cached `IsAvailable` (PropertyXrefDialog.cs:387) and the handler
-- returns early on it too, so with CE gone the click is a no-op. (That gap was itself a finding;
-- the handler now reports it, but that is a DIFFERENT message from the RED branch.)
--
-- THE MECHANISM, and why it is exact rather than a stand-in.
-- Both plugin handlers run their work through CE's plain Lua global `synchronize`:
--     record   -> pipe_server.cpp:1063-1076, run via LuaDoBuffer with chunkname "=AOBMakerCEBridge"
--     navigate -> pipe_server.cpp:912-918,  run via luaL_dostring, whose chunkname IS the script
--                 text -- which contains the literal "DisassemblerView"
-- `synchronize` is registered with `lua_register(L,'synchronize',...)` (LuaHandler.pas:16290), so
-- it is an ordinary overwritable global; and the plugin's Lua state is `lua_newthread(_luavm)`
-- (LuaHandler.pas:188), a coroutine that SHARES `_G` with this script. So a wrapper installed
-- here is seen by the plugin's pipe worker, and `debug.getinfo(2,"S").source` tells the two
-- callers apart. Nothing is faked: the REAL plugin does the real work, minus the one call we
-- refuse.
--
-- ⛔ INERT UNLESS THE JOB FILE SITS NEXT TO IT — same contract as the AOBMaker harnesses, and
-- for the same reason: this file lives in the user's real Cheat Engine install between runs. No
-- job file, no wrapper, no behaviour change of any kind.
--
-- Job file: <this script's folder>\ue5-sw5-job.txt
--   line 1 : mode -- "refuse-navigate" (produces AMBER) or "refuse-record" (produces RED)
--   line 2 : output directory (the log below is written there)
--
-- Output: <outdir>\sw5-ce.log, one line per synchronize() seen:
--     <mode> | caller=<record|navigate|other> | action=<passed|refused> | records=<N>
-- `records` is `getAddressList().Count` sampled AT THAT MOMENT, which is the other half of the
-- acceptance: the RED branch claims "nothing was added to the table", and this is CE itself
-- saying whether anything was.

local function scriptDir()
  local s = debug.getinfo(1, "S").source
  s = s:gsub("^@", "")
  return s:match("^(.*[\\/])") or ""
end

local dir = scriptDir()
local jobPath = dir .. "ue5-sw5-job.txt"

local job = io.open(jobPath, "r")
if not job then return end          -- ⛔ inert. This is the normal state.

local mode = (job:read("*l") or ""):gsub("%s+$", "")
local outdir = (job:read("*l") or ""):gsub("%s+$", "")
job:close()

if mode ~= "refuse-navigate" and mode ~= "refuse-record" then return end
if outdir == "" then return end
if outdir:sub(-1) ~= "\\" and outdir:sub(-1) ~= "/" then outdir = outdir .. "\\" end

local logPath = outdir .. "sw5-ce.log"

local function note(line)
  local f = io.open(logPath, "a")
  if f then f:write(line .. "\n") f:close() end
end

local function recordCount()
  local ok, n = pcall(function()
    local al = getAddressList()
    return al and al.Count or -1
  end)
  return ok and n or -1
end

note(("--- harness armed, mode=%s"):format(mode))

local realSynchronize = synchronize

-- luacheck: globals synchronize
synchronize = function(f, ...)
  -- Level 2 is whoever called synchronize -- i.e. the plugin's generated chunk.
  local info = debug.getinfo(2, "S")
  local src = (info and info.source) or ""

  -- The navigate chunk is passed to luaL_dostring, so its chunkname is the script text and
  -- carries "DisassemblerView". The record chunk is named "=AOBMakerCEBridge". Anything else
  -- is CE's own UI and must pass through untouched.
  local caller = "other"
  if src:find("DisassemblerView", 1, true) then
    caller = "navigate"
  elseif src:find("AOBMakerCEBridge", 1, true) or src:find("getAddressList", 1, true) then
    caller = "record"
  end

  local refuse = (mode == "refuse-navigate" and caller == "navigate")
              or (mode == "refuse-record"   and caller == "record")

  note(("%s | caller=%s | action=%s | records=%d")
       :format(mode, caller, refuse and "refused" or "passed", recordCount()))

  if refuse then
    -- The plugin turns a non-LUA_OK result into success:false, which is exactly the
    -- Task<bool> false the dialog branches on.
    error("SW5 harness: " .. caller .. " deliberately refused")
  end
  return realSynchronize(f, ...)
end

note("--- wrapper installed on the global synchronize")
