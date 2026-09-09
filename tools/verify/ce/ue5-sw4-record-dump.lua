-- SW4 harness — ask Cheat Engine what a pushed record's group ACTUALLY resolves to.
--
-- Installed by tools/verify/sw4_ce_delegate_pad.py into
--   <CheatEngineDir>\autorun\custom\ue5-sw4-record-dump.lua
--
-- WHY
-- ---
-- `[D4B-DELEGATEPAD]` put `delegate_pad` on the wire so `CeXmlExportService.CeOffset` can add it
-- before emitting a delegate leaf. Without it a CE record for a delegate on a CHECKED build
-- points at the 8-byte access detector instead of at `InvocationList::Data` -- i.e. at a pointer
-- that reads 0. The DLL half was fixed and measured on 2026-09-09; what had never happened is
-- CHEAT ENGINE resolving one of those records and someone reading the answer.
--
-- A screenshot of CE's address list cannot settle it: the Address column shows the record's
-- resolved address, but comparing it to "the InvocationList" by eye means trusting that the
-- reader knew which of two 8-byte-apart addresses was correct. So this dumps the number.
--
-- ⛔ INERT UNLESS THE JOB FILE SITS NEXT TO IT — this file lives in the user's real Cheat Engine
-- install. Same contract as the AOBMaker harnesses.
--
-- Job file: <this script's folder>\ue5-sw4-job.txt
--   line 1 : output directory
--   line 2 : poll interval in ms (optional, default 2000)
--
-- Output: <outdir>\sw4-records.txt, REWRITTEN on every poll so the file is always the CURRENT
-- table rather than a history. One line per record:
--     <index> | desc=<description> | addr=<resolved VA hex> | type=<vartype> | offsets=<a,b,c>
-- plus a header line with the record count and the attached PID.

local function scriptDir()
  local s = debug.getinfo(1, "S").source:gsub("^@", "")
  return s:match("^(.*[\\/])") or ""
end

local dir = scriptDir()
local job = io.open(dir .. "ue5-sw4-job.txt", "r")
if not job then return end          -- ⛔ inert. Normal state.

local outdir = (job:read("*l") or ""):gsub("%s+$", "")
local interval = tonumber((job:read("*l") or "")) or 2000
job:close()
if outdir == "" then return end
if outdir:sub(-1) ~= "\\" and outdir:sub(-1) ~= "/" then outdir = outdir .. "\\" end

local outPath = outdir .. "sw4-records.txt"

local function dump()
  local ok, err = pcall(function()
    local al = getAddressList()
    local n = al and al.Count or 0
    local lines = {}
    lines[#lines + 1] = ("# records=%d pid=%s"):format(n, tostring(getOpenedProcessID()))
    for i = 0, n - 1 do
      local mr = al.getMemoryRecord(i)
      if mr then
        -- getCurrentAddress() is what CE actually resolved the record to, pointer chain and
        -- all -- NOT the base address string the record was created with. That distinction is
        -- the whole question here.
        local addr = 0
        pcall(function() addr = mr.CurrentAddress or 0 end)
        local offs = {}
        pcall(function()
          local c = mr.OffsetCount or 0
          for k = 0, c - 1 do offs[#offs + 1] = string.format("%X", mr.Offset[k]) end
        end)
        lines[#lines + 1] = ("%d | desc=%s | addr=%X | type=%s | offsets=%s")
          :format(i, tostring(mr.Description), addr, tostring(mr.Type),
                  #offs > 0 and table.concat(offs, ",") or "-")
      end
    end
    local f = io.open(outPath, "w")
    if f then f:write(table.concat(lines, "\n") .. "\n") f:close() end
  end)
  if not ok then
    local f = io.open(outPath, "w")
    if f then f:write("# dump failed: " .. tostring(err) .. "\n") f:close() end
  end
end

dump()                               -- an immediate first write, so "the file exists" proves armed
local t = createTimer(nil, false)
t.Interval = interval
t.OnTimer = dump
t.Enabled = true
