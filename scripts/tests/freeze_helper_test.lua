--[[
  freeze_helper_test.lua
  UE5CEDumper -- executable tests for scripts/ue5_freeze_helper.lua

  WHY THIS EXISTS
    Audit #5 recorded that the early Lua scripts have no tests at all, and three
    HIGH findings landed in this one file (AA1 packed-bool writes, AA2 recycled-slot
    writes, AA3 stale cache kept forever). The C# suite can only assert on the
    helper's SOURCE TEXT -- it cannot run it -- so a fix could satisfy every string
    assertion and still behave wrongly.

    This harness stubs the handful of Cheat Engine globals the helper touches
    (memory reads/writes, timers, symbol lookup) over a plain Lua table, then
    exercises the real functions and checks what actually got written.

  RUNNING IT
      lua scripts/tests/freeze_helper_test.lua
    Exit 0 = all pass, 1 = a failure (with the case named).

  DELIBERATELY NOT WIRED INTO build.ps1 OR CI
    A standalone `lua` interpreter is not a declared dependency of this repo, and
    a test step that silently skips when its tool is missing is exactly the defect
    audit #5's AD1/AD2 just fixed in the C++ test phase. So this is a documented
    manual tool that fails loudly when run, rather than a build step that passes
    quietly when not. Run it whenever you touch ue5_freeze_helper.lua.
]]

local HELPER = (arg and arg[0] or ''):gsub('[^/\\]*$', '') .. '../ue5_freeze_helper.lua'

-- ============================================================
-- Cheat Engine stubs
-- ============================================================

local MEM      -- address -> qword value
local BYTES    -- address -> byte value
local WRITES   -- ordered log of {addr=, kind=, value=}
local PRINTS   -- captured print() lines
local SYMBOLS  -- symbol name -> address (0/absent = unresolved)
local TIMERS   -- every createTimer() handed out, in creation order

local function resetWorld()
  MEM, BYTES, WRITES, PRINTS, SYMBOLS, TIMERS = {}, {}, {}, {}, {}, {}
end

function readQword(a)        return MEM[a] end
function readInteger(a, _)   return MEM[a] end
function readSmallInteger(a) return MEM[a] end
function readByte(a)         return BYTES[a] end
function readString(a, _)    return MEM[a] end
function readBytes(a, n, t)  if t then return { BYTES[a] } end return BYTES[a] end

function writeByte(a, v)
  BYTES[a] = v
  WRITES[#WRITES + 1] = { addr = a, kind = 'byte', value = v }
end
local function logWrite(kind)
  return function(a, v) WRITES[#WRITES + 1] = { addr = a, kind = kind, value = v } end
end
writeInteger      = logWrite('int32')
writeSmallInteger = logWrite('int16')
writeQword        = logWrite('qword')
writeFloat        = logWrite('float')
writeDouble       = logWrite('double')
function writeBytes(a, b) WRITES[#WRITES + 1] = { addr = a, kind = 'bytes', value = b } end

function getAddressSafe(name) return SYMBOLS[name] or 0 end
-- Cosmetic CE API the helper calls at load time to colour its own names in the
-- Lua editor. No behaviour depends on it; stubbed so the chunk can run.
function registerLuaFunctionHighlight(_) end
function getMainForm() return {} end
function sleep(_) end
function processMessages() end
function processMessagesPaintOnly() end
function getTickCount() return 0 end

function createTimer(_, enabled)
  local t = { Interval = 0, OnTimer = nil, Enabled = enabled or false }
  t.destroy = function() t.destroyed = true end
  TIMERS[#TIMERS + 1] = t
  return t
end

local realPrint = print
function print(s) PRINTS[#PRINTS + 1] = tostring(s) end

-- ============================================================
-- Assertions
-- ============================================================

local failures, checks = 0, 0

local function check(cond, label, detail)
  checks = checks + 1
  if not cond then
    failures = failures + 1
    realPrint(string.format('  FAIL  %s%s', label,
      detail and ('\n        ' .. tostring(detail)) or ''))
  end
end

local function eq(got, want, label)
  check(got == want, label, string.format('got %s, want %s', tostring(got), tostring(want)))
end

local function case(name) realPrint('- ' .. name) end

-- Safe accessors: a failing case must report and let the run continue. Indexing
-- WRITES[n] directly raises when the fix under test wrote nothing, which aborts
-- the file and hides every later case -- the negative control found exactly that.
local function wroteAddr(n) local w = WRITES[n]; return w and w.addr end
local function wroteValue(n) local w = WRITES[n]; return w and w.value end

-- ============================================================
-- Load the helper under test
-- ============================================================

resetWorld()
local chunk, err = loadfile(HELPER)
if not chunk then realPrint('cannot load helper: ' .. tostring(err)); os.exit(1) end
chunk()

-- Build a handle without touching the mailbox: with no g_invokeMailbox symbol the
-- initial rescan fails, which is exactly the state several cases want anyway.
local function newHandle(cfg)
  local h = freezeProperty(cfg)
  h.start()
  return h
end

local function tickTimer()   return TIMERS[#TIMERS - 1] end   -- first of the pair
local function rescanTimer() return TIMERS[#TIMERS] end       -- second of the pair

-- ============================================================
-- AA1 -- packed bitfield bools
-- ============================================================

case('AA1: a native bool (no mask) writes the whole byte')
do
  resetWorld()
  local h = newHandle{ className = 'C', propOffset = 0x10, valueType = 'bool', value = true }
  h._cache, h._classPtr, h._classOff = { 0x1000 }, 0xC1A55, 0x10
  MEM[0x1000 + 0x10] = 0xC1A55
  BYTES[0x1010] = 0xFF
  tickTimer().OnTimer()
  eq(#WRITES, 1, 'one write')
  eq(wroteValue(1), 1, 'whole byte stamped to 1')
end

case('AA1: a packed bool sets ONLY its bit and preserves the siblings')
do
  resetWorld()
  local h = newHandle{ className = 'C', propOffset = 0x10, valueType = 'bool',
                       value = true, boolMask = 0x04 }
  h._cache, h._classPtr, h._classOff = { 0x1000 }, 0xC1A55, 0x10
  MEM[0x1000 + 0x10] = 0xC1A55
  BYTES[0x1010] = 0xA1        -- 1010 0001: siblings at bits 0, 5, 7; our bit 2 clear
  tickTimer().OnTimer()
  eq(#WRITES, 1, 'one write')
  eq(wroteValue(1), 0xA5, 'bit 2 set, every other bit untouched')
end

case('AA1: clearing a packed bool clears ONLY its bit')
do
  resetWorld()
  local h = newHandle{ className = 'C', propOffset = 0x10, valueType = 'bool',
                       value = false, boolMask = 0x04 }
  h._cache, h._classPtr, h._classOff = { 0x1000 }, 0xC1A55, 0x10
  MEM[0x1000 + 0x10] = 0xC1A55
  BYTES[0x1010] = 0xA5
  tickTimer().OnTimer()
  eq(wroteValue(1), 0xA1, 'bit 2 cleared, siblings intact')
end

case('AA1: a packed bool already at the wanted value is not rewritten (write-on-drift)')
do
  resetWorld()
  local h = newHandle{ className = 'C', propOffset = 0x10, valueType = 'bool',
                       value = true, boolMask = 0x04 }
  h._cache, h._classPtr, h._classOff = { 0x1000 }, 0xC1A55, 0x10
  MEM[0x1000 + 0x10] = 0xC1A55
  BYTES[0x1010] = 0x04
  tickTimer().OnTimer()
  eq(#WRITES, 0, 'no write when the bit already matches')
end

case('AA1: 0xFF is UE\'s native-bool marker, NOT a bit mask')
do
  resetWorld()
  local h = newHandle{ className = 'C', propOffset = 0x10, valueType = 'bool',
                       value = true, boolMask = 0xFF }
  h._cache, h._classPtr, h._classOff = { 0x1000 }, 0xC1A55, 0x10
  MEM[0x1000 + 0x10] = 0xC1A55
  BYTES[0x1010] = 0xA1
  tickTimer().OnTimer()
  eq(wroteValue(1), 1, 'falls back to the whole-byte write')
end

case('AA1: an unreadable byte skips the write rather than stamping the byte')
do
  resetWorld()
  local h = newHandle{ className = 'C', propOffset = 0x10, valueType = 'bool',
                       value = true, boolMask = 0x04 }
  h._cache, h._classPtr, h._classOff = { 0x1000 }, 0xC1A55, 0x10
  MEM[0x1000 + 0x10] = 0xC1A55
  BYTES[0x1010] = nil         -- read fails
  tickTimer().OnTimer()
  eq(#WRITES, 0, 'no write at all -- must NOT fall through to whole-byte')
end

-- ============================================================
-- AA2 -- the recycled-slot identity guard
-- ============================================================

case('AA2: a slot recycled by a DIFFERENT class is refused')
do
  resetWorld()
  local h = newHandle{ className = 'C', propOffset = 0x20, valueType = 'int32', value = 99 }
  h._cache, h._classPtr, h._classOff = { 0x2000, 0x3000 }, 0xC1A55, 0x10
  MEM[0x2000 + 0x10] = 0xC1A55   -- still our class
  MEM[0x3000 + 0x10] = 0xDEAD9   -- recycled by something else
  tickTimer().OnTimer()
  eq(#WRITES, 1, 'exactly one write')
  eq(wroteAddr(1), 0x2000 + 0x20, 'only the still-valid instance was written')
end

case('AA2: a slot recycled by the SAME class is allowed (the freeze is class-wide)')
do
  resetWorld()
  local h = newHandle{ className = 'C', propOffset = 0x20, valueType = 'int32', value = 99 }
  h._cache, h._classPtr, h._classOff = { 0x2000, 0x3000 }, 0xC1A55, 0x10
  MEM[0x2000 + 0x10] = 0xC1A55
  MEM[0x3000 + 0x10] = 0xC1A55
  tickTimer().OnTimer()
  eq(#WRITES, 2, 'both written -- a same-class reuse is a target too')
end

case('AA2: an unreadable class pointer is refused, not assumed live')
do
  resetWorld()
  local h = newHandle{ className = 'C', propOffset = 0x20, valueType = 'int32', value = 99 }
  h._cache, h._classPtr, h._classOff = { 0x4000 }, 0xC1A55, 0x10
  MEM[0x4000 + 0x10] = nil       -- decommitted page
  tickTimer().OnTimer()
  eq(#WRITES, 0, 'nothing written')
end

case('AA2: the OLD vtable-only guard would have passed a recycled slot')
do
  -- The negative control for the guard itself: a recycled block whose qword 0
  -- holds an allocator free-list link is non-zero, so the pre-fix test ("vtable
  -- non-zero") says live. Only the class check refuses it.
  resetWorld()
  local h = newHandle{ className = 'C', propOffset = 0x20, valueType = 'int32', value = 99 }
  h._cache, h._classPtr, h._classOff = { 0x5000 }, 0xC1A55, 0x10
  MEM[0x5000]        = 0x7FF700001234   -- non-zero: old guard would allow the write
  MEM[0x5000 + 0x10] = 0xDEAD9          -- but it is not our class any more
  tickTimer().OnTimer()
  eq(#WRITES, 0, 'the class check refuses what the vtable check would have allowed')
end

-- ============================================================
-- AA3 -- a persistently failing rescan must stop writing
-- ============================================================

case('AA3: one failed rescan keeps the cache (transient busy self-heals)')
do
  resetWorld()
  local h = newHandle{ className = 'C', propOffset = 0x20, valueType = 'int32', value = 99 }
  h._cache, h._classPtr, h._classOff = { 0x2000 }, 0xC1A55, 0x10
  MEM[0x2000 + 0x10] = 0xC1A55
  rescanTimer().OnTimer()                       -- fails: no mailbox symbol
  eq(h.isAbandoned(), false, 'not abandoned after one failure')
  eq(#h._cache, 1, 'cache kept')
  tickTimer().OnTimer()
  eq(#WRITES, 1, 'still writing')
end

case('AA3: consecutive failures drop the cache and STOP the writes')
do
  resetWorld()
  local h = newHandle{ className = 'C', propOffset = 0x20, valueType = 'int32', value = 99 }
  h._cache, h._classPtr, h._classOff = { 0x2000 }, 0xC1A55, 0x10
  MEM[0x2000 + 0x10] = 0xC1A55
  for _ = 1, 3 do rescanTimer().OnTimer() end
  eq(h.isAbandoned(), true, 'abandoned after 3 consecutive failures')
  eq(#h._cache, 0, 'cache dropped')
  WRITES = {}
  tickTimer().OnTimer()
  eq(#WRITES, 0, 'tick writes nothing after abandonment')
end

case('AA3: the failure is SURFACED, exactly once, and lastError is readable')
do
  resetWorld()
  local h = newHandle{ className = 'MyClass', propOffset = 0x20, valueType = 'int32', value = 99 }
  for _ = 1, 6 do rescanTimer().OnTimer() end
  eq(#PRINTS, 1, 'printed once, not once per failed rescan')
  check(PRINTS[1] and PRINTS[1]:find('MyClass', 1, true) ~= nil,
        'AA3: the message names the class', PRINTS[1])
  check(PRINTS[1] and PRINTS[1]:find('STOPPED', 1, true) ~= nil,
        'AA3: the message says writing stopped', PRINTS[1])
  check(h.lastError() ~= nil, 'AA3: lastError() is readable (it had zero readers before)')
end

-- ============================================================

realPrint(string.format('\n%d checks, %d failure(s)', checks, failures))
os.exit(failures == 0 and 0 or 1)
