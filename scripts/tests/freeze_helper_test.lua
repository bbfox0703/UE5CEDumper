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
local FAKE_TICK -- controllable clock in ms: sleep() advances it, getTickCount reads it
local TICK_STEP -- ms each sleep() adds (CE's sleep(1) really measures ~15.5 ms)

-- Set by installMailbox(): a hook the write stubs call so the fake DLL can answer
-- a command the moment the helper triggers it.
MAILBOX_ON_WRITE = nil

local function resetWorld()
  MEM, BYTES, WRITES, PRINTS, SYMBOLS, TIMERS = {}, {}, {}, {}, {}, {}
  MAILBOX_ON_WRITE = nil
  -- Non-zero default so a non-answering mailbox TIMES OUT via the tick arm rather
  -- than hanging the rig (AA29's fix makes the iteration fallback dormant when
  -- getTickCount is present). The AA29 case sets a sub-15ms step deliberately.
  FAKE_TICK, TICK_STEP = 0, 16
end

function readQword(a)        return MEM[a] end
function readInteger(a, _)   return MEM[a] end
-- Models CE's readSmallIntegerEx (LuaHandler.pas:1614): one argument means UNSIGNED, and
-- the value is pushed as `word(v)` so the range is 0..65535. The stub used to hand back
-- MEM[a] raw, which would have let a negative fixture value reach the helper and made the
-- dead sign-fixup AA35 removed look reachable. Same shape as invoke_helper_test.lua:93.
function readSmallInteger(a, signed)
  local v = MEM[a]
  if v == nil then return nil end
  if signed then
    if v >= 0x8000 then return v - 0x10000 end
    return v
  end
  if v < 0 then return v + 0x10000 end
  return v
end
function readByte(a)         return BYTES[a] end
function readString(a, _)    return MEM[a] end
function readBytes(a, n, t)  if t then return { BYTES[a] } end return BYTES[a] end

function writeByte(a, v)
  BYTES[a] = v
  WRITES[#WRITES + 1] = { addr = a, kind = 'byte', value = v }
end

-- A write must land in MEM, not only in the WRITES log. The original stubs only
-- logged, which silently made the whole mailbox SUCCESS path unreachable: waitDone
-- polls readInteger(mb + OFF_STATUS) and nothing could ever set it, so every case
-- in this file exercised the no-symbol failure path and nothing else. A stub that
-- is stricter or blinder than the real API hides exactly the defect under test
-- (working-lessons §2.3).
local function logWrite(kind)
  return function(a, v)
    MEM[a] = v
    WRITES[#WRITES + 1] = { addr = a, kind = kind, value = v }
    if MAILBOX_ON_WRITE then MAILBOX_ON_WRITE(a, v) end
  end
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
function sleep(_) FAKE_TICK = FAKE_TICK + TICK_STEP end
function processMessages() end
function processMessagesPaintOnly() end
function getTickCount() return FAKE_TICK end

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

-- Indexed from the FRONT, not the back: start() creates the tick+rescan pair first,
-- and an abandonment now creates a THIRD (one-shot, deferred untick) timer. Counting
-- backwards silently re-pointed both accessors the moment that landed.
local function tickTimer()     return TIMERS[1] end
local function rescanTimer()   return TIMERS[2] end
local function deferredTimer() return TIMERS[3] end

-- ============================================================
-- A fake DLL on the other end of the mailbox
-- ============================================================
-- Mirrors dll/src/Mimic.h's MailboxData layout and HandleListInstances' reply, so
-- the SUCCESS paths (and the "valid class, nothing alive" path that AA12 turns on)
-- are reachable from this rig at all. Offsets duplicated from the helper on
-- purpose: if the helper's copy drifts from Mimic.h, this rig must NOT drift with
-- it, or the two wrongs agree and the test proves nothing.
local MB          = 0x40000000
local CONTRACT_MB = 0x40100000
local OFF_CMD, OFF_STATUS, OFF_RESULT   = 0x000, 0x004, 0x008
local OFF_INSTANCE, OFF_UFUNC           = 0x010, 0x018
local OFF_NUM_PARMS, OFF_FUNC_FLAGS     = 0x022, 0x024
local OFF_ERR, OFF_PARAMS               = 0x228, 0x328
local OFF_CMD_FLAGS, OFF_OUT_FLAGS      = 0x728, 0x72C
local CMD_LIST_INSTANCES = 6
local LI_IN_DERIVED, LI_OUT_TRUNCATED = 1, 1

-- What scope the fake DLL was last ASKED for. The helper choosing the right stride
-- is only half the contract; it also has to send the flag, and a rig that only
-- checked the parsed addresses would pass while the DLL was still handed "exact".
local LAST_SCOPE = nil

--- @param opts table  pages = { {entry,...}, ... } (one list per page). An entry is
---                      an address, or {addr, cls} to give it its own class -- which
---                      is what a DERIVED page carries, since the pool spans
---                      subclasses and one page witness cannot describe it.
---                    result (rc, default 0), classPtr/classOff (witness),
---                    truncated = true -> the DLL reports LI_OUT_TRUNCATED,
---                    failOnPage = n  -> that page answers rc ~= 0 (AA11's shape),
---                    deadPage   = n  -> that page never answers (waitDone timeout)
local function installMailbox(opts)
  opts = opts or {}
  local pages = opts.pages or { {} }
  LAST_SCOPE = nil   -- reset here, not in resetWorld(): this local is declared later
  SYMBOLS['g_invokeMailbox']   = MB
  SYMBOLS['g_mailboxContract'] = CONTRACT_MB
  -- Contract block: magic, current, minimum. The helper bakes UE5_SCRIPT_CONTRACT=3
  -- (contract 3 = the LI_IN_DERIVED scope flag + LI_OUT_TRUNCATED).
  MEM[CONTRACT_MB + 0x00] = 1127564629
  MEM[CONTRACT_MB + 0x04] = opts.contractCur or 3
  MEM[CONTRACT_MB + 0x08] = opts.contractMin or 1

  MAILBOX_ON_WRITE = function(addr, value)
    if addr ~= MB + OFF_CMD or value ~= CMD_LIST_INSTANCES then return end
    -- The helper wrote the page index to paramsData[0] just before the trigger.
    local pageIndex = MEM[MB + OFF_PARAMS] or 0
    -- ...and the scope flag before that. The real handler reads it and CLEARS it,
    -- so a derived request cannot leak into the next caller's command; model that
    -- here or the rig would hide a helper that stopped writing the flag.
    local derived = ((MEM[MB + OFF_CMD_FLAGS] or 0) % 2) == LI_IN_DERIVED
    MEM[MB + OFF_CMD_FLAGS] = 0
    LAST_SCOPE = derived and 'derived' or 'exact'
    if opts.deadPage and pageIndex == opts.deadPage then
      return  -- status stays 0: the DLL never picked it up
    end
    if opts.failOnPage and pageIndex == opts.failOnPage then
      MEM[MB + OFF_RESULT] = -7
      MEM[MB + OFF_ERR]    = 'simulated page failure'
      MEM[MB + OFF_STATUS] = 1
      MEM[MB + OFF_CMD]    = 0
      return
    end
    local page = pages[pageIndex + 1] or {}
    MEM[MB + OFF_RESULT]     = opts.result or 0
    MEM[MB + OFF_NUM_PARMS]  = #page
    MEM[MB + OFF_FUNC_FLAGS] = #pages
    -- Stride follows the SCOPE, exactly as dll/src/Mimic.h's
    -- ListInstancesEntrySize does: 8 bytes exact, 16 bytes derived (addr + its
    -- own UClass*). A rig that always wrote 8 would let a stride bug pass.
    local entrySize = derived and 16 or 8
    for i = 1, #page do
      local e = page[i]
      local a = (type(e) == 'table') and e[1] or e
      local c = (type(e) == 'table') and e[2] or (opts.classPtr or 0xC1A55)
      MEM[MB + OFF_PARAMS + ((i - 1) * entrySize)] = a
      if derived then MEM[MB + OFF_PARAMS + ((i - 1) * entrySize) + 8] = c end
    end
    -- Derived scope publishes NO page-wide class (there is no single one); the
    -- ClassPrivate offset is published in both.
    MEM[MB + OFF_INSTANCE]   = derived and 0 or (opts.classPtr or 0xC1A55)
    MEM[MB + OFF_UFUNC]      = opts.classOff or 0x10
    MEM[MB + OFF_OUT_FLAGS]  = opts.truncated and LI_OUT_TRUNCATED or 0
    MEM[MB + OFF_STATUS]     = 1
    -- SetDone() clears `cmd` after publishing status, and the rig has to as well:
    -- fetchInstancePage refuses a mailbox whose cmd is still non-zero (AA10's
    -- in-flight guard). Without this the SECOND page request was always refused as
    -- busy, so every multi-page path -- pagination itself, and the page-budget
    -- truncation below -- was silently unreachable from this rig. A stub blinder
    -- than the real API hides exactly the code under test (working-lessons 2.3).
    MEM[MB + OFF_CMD]        = 0
  end
end

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
-- AA12 / AA13 -- start() must report an OUTCOME, and the three
-- outcomes must be told apart
-- ============================================================
-- The generated script's only signal was `pcall(handle.start)`, and pcall answers
-- "did Lua raise", never "did anything get frozen". Every mailbox error is caught
-- inside fetchInstancePage's own pcall, so start() cannot raise on the shipped path
-- and the pcall always succeeded -- over a record left ticked and a Lua window
-- auto-closed. start() now returns (ok, err, count).
--
-- The distinction that makes this non-trivial: `count == 0` is NOT a failure. A
-- class-wide freeze armed before its instances spawn is the helper's advertised
-- purpose (header :16-20), so a fix that unticks on zero would break the feature.

case('AA12: start() reports a HARD failure (no DLL) instead of returning nothing')
do
  resetWorld()
  local h = freezeProperty{ className = 'C', propOffset = 0x10, valueType = 'int32', value = 1 }
  local ok, err = h.start()
  eq(ok, false, 'ok is false')
  check(type(err) == 'string' and err:find('g_invokeMailbox', 1, true) ~= nil,
        'AA12: the error names the missing symbol', tostring(err))
end

case('AA12: start() reports SUCCESS with the instance count')
do
  resetWorld()
  installMailbox{ pages = { { 0x2000, 0x3000 } } }
  local h = freezeProperty{ className = 'C', propOffset = 0x10, valueType = 'int32', value = 1 }
  local ok, err, count = h.start()
  eq(ok, true, 'ok is true')
  eq(err, nil, 'no error')
  eq(count, 2, 'count is the number of live instances')
  eq(#h._cache, 2, 'and the cache agrees with the reported count')
end

case('AA12: a valid class with ZERO live instances is SUCCESS, not failure')
do
  -- The case the fix must NOT break: armed now, applies as instances spawn.
  resetWorld()
  installMailbox{ pages = { {} } }
  local h = freezeProperty{ className = 'C', propOffset = 0x10, valueType = 'int32', value = 1 }
  local ok, err, count = h.start()
  eq(ok, true, 'ok is true -- an empty world is not an error')
  eq(err, nil, 'no error')
  eq(count, 0, 'count is 0, and that is how the caller knows to keep the window open')
end

case('AA13: a hard failure is distinguishable from an armed-but-empty freeze')
do
  -- The whole point: before this, both produced pcall(start) == true and nothing else,
  -- so the generated script could not tell "froze nothing because the DLL is gone"
  -- from "froze nothing because nothing has spawned yet".
  resetWorld()
  local hFail = freezeProperty{ className = 'C', propOffset = 0x10, valueType = 'int32', value = 1 }
  local okFail, errFail, countFail = hFail.start()

  resetWorld()
  installMailbox{ pages = { {} } }
  local hEmpty = freezeProperty{ className = 'C', propOffset = 0x10, valueType = 'int32', value = 1 }
  local okEmpty, errEmpty, countEmpty = hEmpty.start()

  -- Assert the CONCRETE triple on both sides, not merely that they differ. A
  -- "they differ" check passes when the hard-failure path returns nothing at all
  -- (nil ~= true), which is the exact regression this case exists to catch --
  -- found by negative-controlling the two branches separately.
  eq(okFail, false, 'AA13: the hard failure reports ok=false (not nil)')
  check(type(errFail) == 'string', 'AA13: the hard failure carries a reason', tostring(errFail))
  eq(countFail, 0, 'AA13: the hard failure reports 0')
  eq(okEmpty, true, 'AA13: the armed case reports ok=true')
  eq(errEmpty, nil, 'AA13: the armed case carries no error')
  eq(countEmpty, 0, 'AA13: the armed case reports 0')
end

-- ============================================================
-- AA9 -- the header's own SAMPLE must actually work
-- ============================================================
-- The samples used to hold the handle in a chunk `local`, which CE makes
-- unreachable from [DISABLE]: each {$lua} block is compiled as its own chunk
-- (autoassembler.pas hands the block's text to luaL_loadstring) inside ONE
-- shared Lua state, so globals cross and locals do not. A handle parked in a
-- local can never be stopped -- its two timers belong to CE's main form, so
-- unticking and even deleting the record leave them writing ~20x/sec until CE
-- itself is restarted.
--
-- Asserting on the doc's TEXT would be tautological. Instead: EXTRACT the
-- sample from the header comment and RUN it, under a faithful model of CE's
-- two-chunk compilation. If the sample is wrong, this fails.

-- Line-based and EXACT, because that is what CE does: autoassembler uppercases
-- and trims each line and compares it to '{$LUA}' / '{$ASM}' whole. A substring
-- search is not the same thing, and the difference is not academic -- the first
-- version of this extractor used `src:gmatch('{%$lua}(.-){%$asm}')` and captured
-- the PROSE in the lifetime box above, which mentions `{$lua}` in a sentence.
-- Modelling CE faithfully is also what fixes it.
local function extractSampleBlocks(path)
  local blocks, body = {}, nil
  for line in io.lines(path) do
    local t = line:match('^%s*(.-)%s*$'):upper()
    if t == '{$LUA}' then
      body = {}
    elseif t == '{$ASM}' then
      if body then blocks[#blocks + 1] = table.concat(body, '\n') end
      body = nil
    elseif body then
      body[#body + 1] = line
    end
  end
  return blocks
end

--- Run one block the way CE does: its own chunk, in the shared global state,
--- with `syntaxcheck` and `memrec` injected as CHUNK LOCALS (autoassembler
--- prepends `local syntaxcheck,memrec=...` and passes them as varargs).
local function runAsCeChunk(body, memrec)
  local chunk, err = load('local syntaxcheck,memrec=...\n' .. body, 'ce-block')
  if not chunk then return false, 'compile: ' .. tostring(err) end
  return pcall(chunk, false, memrec)
end

function showMessage(s) PRINTS[#PRINTS + 1] = 'showMessage: ' .. tostring(s) end

case('AA9: the header SAMPLE compiles, starts, and is STOPPABLE from a second chunk')
do
  resetWorld()
  installMailbox{ pages = { { 0x2000, 0x3000 } } }

  local blocks = extractSampleBlocks(HELPER)
  check(#blocks >= 2, 'AA9: the header carries an [ENABLE] and a [DISABLE] block',
        string.format('found %d {$lua} block(s)', #blocks))

  if #blocks >= 2 then
    local memrec = { Active = true }
    local okE, errE = runAsCeChunk(blocks[1], memrec)
    check(okE, 'AA9: the [ENABLE] sample runs without error', errE)
    eq(#TIMERS, 2, 'AA9: it started the tick + rescan pair')
    eq(memrec.Active, true, 'AA9: a successful freeze leaves the record ticked')

    -- THE POINT: a SEPARATE chunk, sharing only globals -- exactly what CE does.
    local okD, errD = runAsCeChunk(blocks[2], memrec)
    check(okD, 'AA9: the [DISABLE] sample runs without error', errD)
    check(TIMERS[1] and TIMERS[1].destroyed == true,
          'AA9: the tick timer was actually destroyed by the second chunk')
    check(TIMERS[2] and TIMERS[2].destroyed == true,
          'AA9: the rescan timer was actually destroyed by the second chunk')
  end
end

case('AA9: the OLD sample shape (a chunk `local`) is unstoppable -- the control')
do
  -- The negative control for the doc itself. This is what SAMPLES 1-3 taught
  -- before the rewrite; it must fail, or the rewrite fixed nothing.
  resetWorld()
  installMailbox{ pages = { { 0x2000 } } }

  local okE = runAsCeChunk([[
    local h = freezeProperty({ className='C', propOffset=0x10,
                               valueType='float', value=1.0 })
    h.start()
  ]], { Active = true })
  eq(okE, true, 'the old ENABLE shape does run')
  eq(#TIMERS, 2, 'and it does start two timers')

  local okD, errD = runAsCeChunk([[ h.stop() ]], { Active = true })
  eq(okD, false, 'AA9 control: stopping via the local FAILS in a second chunk')
  check(type(errD) == 'string' and errD:find('nil value', 1, true) ~= nil,
        'AA9 control: and it fails as a nil global, which is the whole defect', tostring(errD))
  check(TIMERS[1] and TIMERS[1].destroyed ~= true,
        'AA9 control: the timers are STILL RUNNING -- unreachable forever')
end


-- ============================================================
-- AA10: the mailbox has TWO mutually-blind concurrency guards.
--
-- `_ue5_invoke_busy` is ours; the EMITTED scripts (Movement / GodMode / Fly /
-- TimeDilation / ...) write the mailbox directly and never touch it. So a
-- generated toggle that timed out bails while `cmd` is still non-zero and the
-- DLL still owns the mailbox -- and the next rescan tick would write over a
-- live command. Same class as AA19.
-- ============================================================

case('AA10: rescan refuses a mailbox another command still owns')
do
  resetWorld(); installMailbox{ pages = {{0xA1}} }
  local h = freezeProperty{ className = 'BP_Player_C', propOffset = 0x10, valueType = 'int32', value = 99 }
  h.start()

  -- An emitted script left a command in flight. Poke the raw cells so the
  -- write stubs / MAILBOX_ON_WRITE cannot answer it for us.
  MEM[MB + OFF_CMD]    = 8
  MEM[MB + OFF_STATUS] = 0
  MEM[MB + OFF_RESULT] = -7

  rescanTimer().OnTimer()

  eq(MEM[MB + OFF_CMD], 8, 'the in-flight command was NOT overwritten')
  eq(MEM[MB + OFF_RESULT], -7, 'and neither was its result')

  -- THE PLACEMENT TRAP. A guard written after `_ue5_invoke_busy = true` passes
  -- both assertions above and still latches the flag for the whole session,
  -- so every later rescan refuses itself and the freeze is silently abandoned.
  eq(_ue5_invoke_busy, false, 'the busy flag was not left latched')
end

case('AA10: the refusal is transient -- the next rescan still refreshes')
do
  resetWorld(); installMailbox{ pages = {{0xA1}} }
  local h = freezeProperty{ className = 'BP_Player_C', propOffset = 0x10, valueType = 'int32', value = 99 }
  h.start()

  MEM[MB + OFF_CMD] = 8
  rescanTimer().OnTimer()               -- refused
  MEM[MB + OFF_CMD] = 0                 -- the other command finished
  rescanTimer().OnTimer()               -- must recover

  check(not h.isAbandoned(), 'the freeze is still live after a refusal')
  eq(_ue5_invoke_busy, false, 'and the flag is clear again')
end

-- ============================================================
-- [FREEZESCOPE-2026-08-18]: the freeze must hold the class the user is
-- looking at, not only the class that DECLARES the field.
--
-- A Property Search row for an inherited field (bCanBeDamaged, bHidden,
-- bReplicates) is keyed to its DECLARING class, so the freeze was handed
-- 'Actor' and an exact-name pool returned whichever stray AActor the level
-- happened to hold -- one ChaosDebugDrawActor in the observed case -- while
-- the player's pawn and every other subclass went untouched. Solide already
-- walks subclasses for the Force submenu on the SAME ROW.
--
-- The wire consequence is the part a source-level test cannot reach: a derived
-- page ships (UObject*, UClass*) PAIRS, because a sweep across subclasses has
-- no single class to witness with.
-- ============================================================

case('SCOPE: derived is the DEFAULT -- the DLL is asked for subclasses')
do
  resetWorld(); installMailbox{ pages = { { 0x2000 } } }
  local h = freezeProperty{ className = 'Actor', propOffset = 0x58, valueType = 'bool', value = true }
  h.start()
  eq(LAST_SCOPE, 'derived', 'the mailbox request carried LI_IN_DERIVED')
  eq(h.isDerived(), true, 'and the handle agrees')
end

case('SCOPE: derived = false is honoured (exact class only)')
do
  resetWorld(); installMailbox{ pages = { { 0x2000 } } }
  local h = freezeProperty{ className = 'Actor', derived = false,
                            propOffset = 0x58, valueType = 'bool', value = true }
  h.start()
  eq(LAST_SCOPE, 'exact', 'the mailbox request did NOT carry LI_IN_DERIVED')
  eq(h.isDerived(), false, 'and the handle agrees')
  eq(#h._cache, 1, 'the 8-byte exact page still parses')
  eq(h._cache[1], 0x2000, 'and yields the address, not half of one')
  eq(h._classPtr, 0xC1A55, 'exact scope keeps the page-wide witness')
end

case('SCOPE: a derived page is read at the 16-byte stride, not the 8-byte one')
do
  -- The negative control for the stride itself. At an 8-byte stride the second
  -- "address" read would be entry 1's CLASS pointer, so the freeze would write
  -- into a UClass -- and nothing downstream would notice.
  resetWorld()
  installMailbox{ pages = { { {0x2000, 0xAAA}, {0x3000, 0xBBB} } } }
  local h = freezeProperty{ className = 'Actor', propOffset = 0x10, valueType = 'int32', value = 7 }
  local ok, _, count = h.start()
  eq(ok, true, 'armed')
  eq(count, 2, 'two instances, not four half-read ones')
  eq(h._cache[1], 0x2000, 'entry 1 address')
  eq(h._cache[2], 0x3000, 'entry 2 address -- 0xAAA here would be the stride bug')
  eq(h._cacheCls[1], 0xAAA, 'entry 1 witness')
  eq(h._cacheCls[2], 0xBBB, 'entry 2 witness')
end

case('SCOPE: each derived entry is guarded by ITS OWN class')
do
  -- The reason the witness had to go per-entry: two different subclasses in one
  -- pool. A single page-wide witness would refuse whichever one it was not.
  resetWorld()
  installMailbox{ pages = { { {0x2000, 0xAAA}, {0x3000, 0xBBB} } } }
  local h = freezeProperty{ className = 'Actor', propOffset = 0x20, valueType = 'int32', value = 99 }
  h.start()
  MEM[0x2000 + 0x10] = 0xAAA      -- still its own subclass
  MEM[0x3000 + 0x10] = 0xBBB      -- a DIFFERENT subclass, and equally a target
  WRITES = {}
  tickTimer().OnTimer()
  eq(#WRITES, 2, 'both subclasses written -- a page-wide witness would drop one')
end

case('SCOPE: a derived slot recycled by an UNRELATED class is still refused')
do
  resetWorld()
  installMailbox{ pages = { { {0x2000, 0xAAA}, {0x3000, 0xBBB} } } }
  local h = freezeProperty{ className = 'Actor', propOffset = 0x20, valueType = 'int32', value = 99 }
  h.start()
  MEM[0x2000 + 0x10] = 0xAAA
  MEM[0x3000 + 0x10] = 0xDEAD9    -- recycled by something that is not in the pool
  WRITES = {}
  tickTimer().OnTimer()
  eq(#WRITES, 1, 'exactly one write')
  eq(wroteAddr(1), 0x2000 + 0x20, 'and it is the surviving instance')
end

case('SCOPE: a derived entry the DLL could not class is DROPPED, not written blind')
do
  resetWorld()
  installMailbox{ pages = { { {0x2000, 0xAAA}, {0x3000, 0} } } }
  local h = freezeProperty{ className = 'Actor', propOffset = 0x20, valueType = 'int32', value = 99 }
  local _, _, count = h.start()
  eq(count, 1, 'the witness-less entry never entered the cache')
  eq(h._cache[1], 0x2000, 'and the survivor is the one that had a witness')
end

case('SCOPE: a derived scan with no ClassPrivate offset is an ERROR, not a blind freeze')
do
  -- Without the offset there is no witness at all, and degrading to an unguarded
  -- write is the AA2 defect -- so this must FAIL rather than fall back.
  resetWorld()
  installMailbox{ pages = { { {0x2000, 0xAAA} } }, classOff = 0 }
  local h = freezeProperty{ className = 'Actor', propOffset = 0x20, valueType = 'int32', value = 99 }
  local ok, err, count = h.start()
  eq(ok, false, 'reported as a hard failure')
  eq(count, 0, 'and nothing is frozen')
  check(type(err) == 'string' and err:find('identity witness', 1, true) ~= nil,
        'SCOPE: the error names the missing witness', tostring(err))
end

case('SCOPE: a filter drops the address AND its witness in lockstep')
do
  -- Filtering one array and not the other pairs every survivor with the NEXT
  -- entry's class, so every write is refused while the freeze reports itself
  -- healthy -- a silent no-op that looks exactly like success.
  resetWorld()
  installMailbox{ pages = { { {0x2000, 0xAAA}, {0x3000, 0xBBB} } } }
  local h = freezeProperty{ className = 'Actor', propOffset = 0x20, valueType = 'int32', value = 99,
                            filter = function(a) return a ~= 0x2000 end }
  local _, _, count = h.start()
  eq(count, 1, 'the filter removed one instance')
  eq(h._cache[1], 0x3000, 'the survivor is the expected address')
  eq(h._cacheCls[1], 0xBBB, 'and it kept ITS OWN witness, not the dropped one\'s')
  MEM[0x3000 + 0x10] = 0xBBB
  WRITES = {}
  tickTimer().OnTimer()
  eq(#WRITES, 1, 'and it is actually written')
end

case('SCOPE: a capped pool is reported, so "n instances" is not read as a total')
do
  resetWorld()
  installMailbox{ pages = { { {0x2000, 0xAAA} } }, truncated = true }
  local h = freezeProperty{ className = 'Actor', propOffset = 0x20, valueType = 'int32', value = 1 }
  local ok, _, count, capped = h.start()
  eq(ok, true, 'armed')
  eq(count, 1, 'one instance returned')
  eq(capped, true, 'start() reports the cap as its 4th value')
  eq(h.isTruncated(), true, 'and the handle exposes it')
end

case('SCOPE: an UNcapped pool does not claim to be capped -- the control')
do
  resetWorld()
  installMailbox{ pages = { { {0x2000, 0xAAA} } } }
  local h = freezeProperty{ className = 'Actor', propOffset = 0x20, valueType = 'int32', value = 1 }
  local _, _, _, capped = h.start()
  eq(capped, false, 'no cap reported')
  eq(h.isTruncated(), false, 'and the handle agrees')
end

case('SCOPE: running out of page budget also counts as capped')
do
  -- The other way the set can be a prefix: the DLL says there are more pages than
  -- MAX_PAGES lets us fetch. Reported the same way, because the user's question
  -- ("is this everything?") has the same answer.
  resetWorld()
  local pages = {}
  for i = 1, 17 do pages[i] = { {0x1000 + i, 0xAAA} } end
  installMailbox{ pages = pages }
  local h = freezeProperty{ className = 'Actor', propOffset = 0x20, valueType = 'int32', value = 1 }
  local ok, _, count, capped = h.start()
  eq(ok, true, 'armed')
  eq(count, 16, 'stopped at the 16-page budget')
  eq(capped, true, 'and said so')
end

case('SCOPE: a pool that EXACTLY fills the page budget is not capped -- the control')
do
  -- The off-by-one this guards: testing "did we reach MAX_PAGES" after the loop
  -- flags a complete 16-page set as truncated, printing a caveat over a freeze
  -- with nothing wrong with it. Only "pages remain AND budget spent" is truncation.
  resetWorld()
  local pages = {}
  for i = 1, 16 do pages[i] = { {0x1000 + i, 0xAAA} } end
  installMailbox{ pages = pages }
  local h = freezeProperty{ className = 'Actor', propOffset = 0x20, valueType = 'int32', value = 1 }
  local _, _, count, capped = h.start()
  eq(count, 16, 'every page was read')
  eq(capped, false, 'and nothing claimed to be missing')
end

case('SCOPE: abandonment clears the cap flag -- an empty freeze holds nothing to truncate')
do
  resetWorld()
  installMailbox{ pages = { { {0x2000, 0xAAA} } }, truncated = true }
  local h = freezeProperty{ className = 'Actor', propOffset = 0x20, valueType = 'int32', value = 1 }
  h.start()
  eq(h.isTruncated(), true, 'capped while it was running')
  SYMBOLS['g_invokeMailbox'] = 0
  for _ = 1, 3 do rescanTimer().OnTimer() end
  eq(h.isAbandoned(), true, 'abandoned')
  eq(h.isTruncated(), false, 'and no longer describes a pool it does not hold')
end

case('SCOPE: a contract-2 DLL is REFUSED, not silently given the old pool')
do
  -- The whole failure being fixed is a freeze that holds the wrong pool while
  -- reporting success. An older DLL ignores the scope flag, so it must be told
  -- apart from a working one BEFORE anything is written.
  resetWorld()
  installMailbox{ pages = { { 0x2000 } }, contractCur = 2 }
  local h = freezeProperty{ className = 'Actor', propOffset = 0x20, valueType = 'int32', value = 1 }
  local ok, err = h.start()
  eq(ok, false, 'refused')
  check(type(err) == 'string' and err:find('update UE5Dumper.dll', 1, true) ~= nil,
        'SCOPE: and it says which side to update', tostring(err))
end

-- ============================================================
-- [FREEZESTUCK-2026-08-18]: an abandoned freeze must untick its record.
--
-- The abandonment print lands in CE's Lua Engine window, which hygiene closes on
-- a successful enable -- so the user saw a TICKED record (in CE a red X on the
-- checkbox means ACTIVE, not failed) over a freeze that had permanently stopped
-- writing, and the message's own advice ("re-enable the record") could not be
-- followed because the record had never been disabled.
-- ============================================================

--- A stand-in for CE's TMemoryRecord: assigning Active = false runs the record's
--- [DISABLE] block, which is what CE actually does and what makes the reentrancy
--- hazard real (that block calls handle.stop(), which destroys the timers).
local function makeCeRecord(disableBody)
  local state = { Active = true, disableRuns = 0, disableErr = nil }
  local rec = setmetatable({}, {
    __index    = function(_, k) return state[k] end,
    __newindex = function(_, k, v)
      state[k] = v
      if k == 'Active' and v == false and disableBody then
        state.disableRuns = state.disableRuns + 1
        local ok, err = runAsCeChunk(disableBody, nil)
        if not ok then state.disableErr = err end
      end
    end,
  })
  return rec, state
end

case('STUCK: abandonment unticks the CE record')
do
  resetWorld()
  local rec, st = makeCeRecord(nil)
  local h = freezeProperty{ className = 'C', propOffset = 0x20, valueType = 'int32',
                            value = 99, memrec = rec }
  h.start()
  for _ = 1, 3 do rescanTimer().OnTimer() end
  eq(h.isAbandoned(), true, 'abandoned after 3 consecutive failures')

  -- The untick is DEFERRED: doing it inline would run [DISABLE] -> stop() ->
  -- destroy the very timer whose handler is on the stack.
  eq(st.Active, true, 'not unticked from INSIDE the rescan callback')
  eq(tickTimer().Enabled, false, 'but the tick timer already stopped')
  eq(rescanTimer().Enabled, false, 'and so did the rescan timer')
  check(tickTimer().destroyed ~= true, 'and nothing was destroyed from inside a callback')

  check(deferredTimer() ~= nil, 'a one-shot untick timer was scheduled')
  if deferredTimer() then
    deferredTimer().OnTimer(deferredTimer())
    eq(st.Active, false, 'THE FIX: the record is unticked once the callback has returned')
    check(deferredTimer().destroyed == true, 'and the one-shot timer cleaned itself up')
  end
end

case('STUCK: the untick runs [DISABLE], which stops the timers -- without reentering one')
do
  -- End to end against the header SAMPLE, under CE's real two-chunk model: the
  -- deferred untick fires [DISABLE], [DISABLE] calls stop(), stop() destroys the
  -- pair. If the untick had been done inline this is where it would destroy a
  -- timer from inside its own handler.
  resetWorld()
  installMailbox{ pages = { { {0x2000, 0xAAA} } } }
  local blocks = extractSampleBlocks(HELPER)
  check(#blocks >= 2, 'STUCK: the header still has both sample blocks')
  if #blocks >= 2 then
    local rec, st = makeCeRecord(blocks[2])
    local okE, errE = runAsCeChunk(blocks[1], rec)
    check(okE, 'STUCK: the [ENABLE] sample runs', errE)
    eq(st.Active, true, 'and a healthy freeze leaves the record ticked')

    -- Kill the mailbox the way a DLL re-injection does, then let it give up.
    SYMBOLS['g_invokeMailbox'] = 0
    for _ = 1, 3 do rescanTimer().OnTimer() end
    check(deferredTimer() ~= nil, 'STUCK: an untick was scheduled')
    if deferredTimer() then deferredTimer().OnTimer(deferredTimer()) end

    eq(st.Active, false, 'the record is inactive')
    eq(st.disableRuns, 1, 'and CE ran [DISABLE] exactly once')
    eq(st.disableErr, nil, 'with no error -- no timer was destroyed from its own handler')
    check(TIMERS[1] and TIMERS[1].destroyed == true, 'the tick timer was destroyed by [DISABLE]')
    check(TIMERS[2] and TIMERS[2].destroyed == true, 'the rescan timer too')
  end
end

case('STUCK: a TRANSIENT failure does NOT untick -- the control')
do
  -- The rule is "a bail-out that applied NOTHING unticks", not "any hiccup
  -- unticks". One failed rescan keeps the cache and keeps writing, so unticking
  -- there would be the opposite defect.
  resetWorld()
  local rec, st = makeCeRecord(nil)
  local h = freezeProperty{ className = 'C', propOffset = 0x20, valueType = 'int32',
                            value = 99, memrec = rec }
  h.start()
  h._cache, h._classPtr, h._classOff = { 0x2000 }, 0xC1A55, 0x10
  MEM[0x2000 + 0x10] = 0xC1A55
  rescanTimer().OnTimer()
  eq(h.isAbandoned(), false, 'not abandoned')
  eq(st.Active, true, 'record still ticked')
  eq(deferredTimer(), nil, 'and no untick was scheduled')
  WRITES = {}
  tickTimer().OnTimer()
  eq(#WRITES, 1, 'still writing')
end

case('STUCK: with no memrec it still stops, and says who has to untick')
do
  resetWorld()
  local h = freezeProperty{ className = 'MyClass', propOffset = 0x20, valueType = 'int32', value = 99 }
  h.start()
  for _ = 1, 3 do rescanTimer().OnTimer() end
  eq(h.isAbandoned(), true, 'abandoned')
  eq(tickTimer().Enabled, false, 'and it stopped writing anyway')
  eq(#PRINTS, 1, 'reported once')
  check(PRINTS[1] and PRINTS[1]:find('Untick and re-tick', 1, true) ~= nil,
        'STUCK: the advice matches what actually happened', PRINTS[1])

  -- The message still reaches the user even with no record to untick -- that case
  -- is worse, not better, because the checkbox cannot be corrected at all.
  check(deferredTimer() ~= nil, 'STUCK: a message was still scheduled')
  if deferredTimer() then
    deferredTimer().OnTimer(deferredTimer())
    check(PRINTS[2] and PRINTS[2]:find('showMessage', 1, true) == 1,
          'STUCK: and it arrives as a modal', tostring(PRINTS[2]))
  end
end

case('STUCK: the message survives its own untick -- [DISABLE] closes the Lua window')
do
  -- The trap this exists for: the fix ITSELF hides the diagnosis. Unticking runs
  -- [DISABLE], whose last line is the hygiene auto-close, so a print() into the Lua
  -- Engine window is gone the moment the record clears. Order and channel both
  -- matter -- the modal must come AFTER the untick and must not be a print.
  resetWorld()
  local rec, st = makeCeRecord(nil)
  local h = freezeProperty{ className = 'MyClass', propOffset = 0x20, valueType = 'int32',
                            value = 99, memrec = rec }
  h.start()
  for _ = 1, 3 do rescanTimer().OnTimer() end

  -- showMessage BLOCKS until dismissed, so the correction has to be done by the
  -- time it opens -- otherwise an unattended dialog leaves the record ticked for as
  -- long as nobody is at the keyboard. Sample the record from inside the modal;
  -- asserting afterwards cannot tell the two orders apart.
  local activeWhenShown = nil
  local realShow = showMessage
  showMessage = function(s) activeWhenShown = st.Active; realShow(s) end
  if deferredTimer() then deferredTimer().OnTimer(deferredTimer()) end
  showMessage = realShow

  eq(st.Active, false, 'unticked')
  eq(activeWhenShown, false, 'STUCK: and already unticked BEFORE the modal blocks')
  local modal = PRINTS[#PRINTS]
  check(modal and modal:find('showMessage', 1, true) == 1,
        'STUCK: the last thing the user gets is a modal, not a window-bound print',
        tostring(modal))
  check(modal and modal:find('STOPPED', 1, true) ~= nil,
        'STUCK: and it carries the same diagnosis as the print', tostring(modal))
  check(modal and modal:find('has been unticked', 1, true) ~= nil,
        'STUCK: including what was done about it', tostring(modal))
end

case('STUCK: with a memrec the message states the record WAS unticked')
do
  -- The old wording ("Re-enable the record after fixing it") described something
  -- the user could not do, because nothing had disabled the record.
  resetWorld()
  local rec = makeCeRecord(nil)
  local h = freezeProperty{ className = 'MyClass', propOffset = 0x20, valueType = 'int32',
                            value = 99, memrec = rec }
  h.start()
  for _ = 1, 3 do rescanTimer().OnTimer() end
  eq(#PRINTS, 1, 'reported once')
  check(PRINTS[1] and PRINTS[1]:find('has been unticked', 1, true) ~= nil,
        'STUCK: the message says the record was unticked', PRINTS[1])
end

case('STUCK: a deleted record cannot turn the diagnosis into a traceback')
do
  resetWorld()
  local exploding = setmetatable({}, {
    __index    = function() error('record freed') end,
    __newindex = function() error('record freed') end,
  })
  local h = freezeProperty{ className = 'C', propOffset = 0x20, valueType = 'int32',
                            value = 99, memrec = exploding }
  h.start()
  for _ = 1, 3 do rescanTimer().OnTimer() end
  check(deferredTimer() ~= nil, 'the untick was still scheduled')
  if deferredTimer() then
    local ok = pcall(deferredTimer().OnTimer, deferredTimer())
    eq(ok, true, 'and firing it does not raise out of the timer')
  end
end

-- ============================================================
-- AA11: a page failure after page 0 must NOT be reported as clean success.
-- ============================================================

case('AA11: a page failure after page 0 keeps the PRIOR cache, not a partial prefix')
do
  resetWorld()
  installMailbox{ pages = { { {0x2000, 0xAAA} }, { {0x3000, 0xBBB} } }, failOnPage = 1 }
  local h = freezeProperty{ className = 'Actor', propOffset = 0x10, valueType = 'int32', value = 99 }
  h.start()   -- initial rescan already fails on page 1; cache stays empty
  -- Model a PRIOR successful rescan's cache, then rescan again into the same failure.
  h._cache, h._cacheCls, h._failStreak = { 0x9000 }, { 0xAAA }, 0
  local ok, err = rescanTimer().OnTimer()
  eq(ok, false, 'AA11: the partial enumeration is reported as a FAILURE')
  check(err ~= nil, 'AA11: with an error string')
  eq(#h._cache, 1, 'AA11: the prior cache was kept, not replaced by the page-0 prefix')
  eq(h._cache[1], 0x9000, 'AA11: and it is the PRIOR entry (0x9000), not 0x2000 from page 0')
  check(h.lastError() ~= nil, 'AA11: lastError() is set')
end

case('AA11: an all-pages-OK multi-page rescan still succeeds -- the control')
do
  -- The fix must not turn a healthy multi-page enumeration into a failure.
  resetWorld()
  installMailbox{ pages = { { {0x2000, 0xAAA} }, { {0x3000, 0xBBB} } } }
  local h = freezeProperty{ className = 'Actor', propOffset = 0x10, valueType = 'int32', value = 99 }
  local ok, _, count = h.start()
  eq(ok, true, 'AA11: two good pages -> success')
  eq(count, 2, 'AA11: both pages collected')
  eq(h.lastError(), nil, 'AA11: no error on a clean rescan')
end

-- ============================================================
-- AA28: an unreadable contract symbol on a GONE process is not a "stale address".
-- ============================================================

case('AA28: an unreadable contract symbol is diagnosed as a gone process, not a stale address')
do
  resetWorld()
  SYMBOLS['g_invokeMailbox']   = MB           -- resolves...
  SYMBOLS['g_mailboxContract'] = CONTRACT_MB  -- ...but its magic is NEVER written to MEM
  local h = freezeProperty{ className = 'C', propOffset = 0x10, valueType = 'int32', value = 1 }
  local ok, err = h.start()
  eq(ok, false, 'AA28: the rescan fails')
  check(err and err:find('exited', 1, true) ~= nil, 'AA28: diagnosed as a gone process', err)
  check(err and err:find('stale address', 1, true) == nil,
        'AA28: NOT mislabelled a stale address', err)
end

case('AA28: a WRONG magic is still a stale address -- the control')
do
  resetWorld()
  SYMBOLS['g_invokeMailbox']   = MB
  SYMBOLS['g_mailboxContract'] = CONTRACT_MB
  MEM[CONTRACT_MB + 0x00] = 0xDEADBEEF        -- readable, but the wrong magic
  local h = freezeProperty{ className = 'C', propOffset = 0x10, valueType = 'int32', value = 1 }
  local ok, err = h.start()
  eq(ok, false, 'refused')
  check(err and err:find('stale address', 1, true) ~= nil,
        'AA28: a readable-but-wrong magic is still a stale address', err)
end

-- ============================================================
-- AA29: exactly ONE deadline governs waitDone, and the printed time is the arm
-- that fired -- not a dormant iteration fallback racing the tick deadline.
-- ============================================================

case('AA29: the real-ms deadline governs, not the ~15ms/iter fallback')
do
  resetWorld()
  TICK_STEP = 10                                   -- sleep(1) < 15 ms: the iter fallback
                                                   -- would fire FIRST if it were still live
  installMailbox{ pages = { {} }, deadPage = 0 }   -- page 0 never answers -> waitDone times out
  local h = freezeProperty{ className = 'C', propOffset = 0x10, valueType = 'int32', value = 1 }
  local ok, err = h.start()
  eq(ok, false, 'AA29: it times out')
  check(err and err:find('never picked this up', 1, true) ~= nil,
        'AA29: IDLE status is diagnosed as the DLL never taking the command', err)
  -- Post-fix: the tick arm governs, so the clock reaches ~limit (5000) before the
  -- iteration fallback (floor(5000/15)=333 iters * 10ms = 3330) could ever fire.
  -- Pre-fix: the live iter fallback fired at 3330 while the message still said 5000ms.
  check(getTickCount() >= 5000,
        'AA29: waited the full tick deadline, not the iteration fallback',
        'FAKE_TICK=' .. tostring(getTickCount()))
end

-- ============================================================
-- AA30: an UPDATED helper must take effect on re-load; a same/older one must not.
-- ============================================================
-- ⭐ The three versions below are DERIVED from the helper, never typed. They used to
-- be the literals '1.4'/'1.5', so bumping the helper to 1.5 (the [FREEZEFIRSTERR]
-- fix, which HAD to bump -- a same-version re-load is a no-op, so 1.4 residents in
-- the wild would never receive it) broke two of these cases and silently weakened
-- the third: with the file at 1.5, the "an OLDER file does not downgrade" case was
-- re-loading a SAME-version file and passing on the wrong branch entirely.
local CUR = UE5_FREEZE_HELPER_VERSION            -- whatever the file under test declares
local function bump(v, d)                        -- same major, minor +/- d
  local a, b = v:match('^(%d+)%.(%d+)$')
  return string.format('%s.%d', a, tonumber(b) + d)
end
local OLDER, NEWER = bump(CUR, -1), bump(CUR, 1)

case('AA30: a newer-version helper REPLACES a resident older one on re-load')
do
  resetWorld()
  UE5_FREEZE_HELPER_VERSION = OLDER          -- an OLD helper is resident
  local sentinel = function() return 'OLD' end
  freezeProperty = sentinel
  assert(loadfile(HELPER))()                 -- re-add the current file: CE recompiles the chunk
  check(freezeProperty ~= sentinel, 'AA30: the newer helper redefined freezeProperty')
  eq(UE5_FREEZE_HELPER_VERSION, CUR, 'AA30: and bumped the resident version')
end

case('AA30: the SAME version is a no-op -- shared state and the resident fn are kept')
do
  resetWorld()
  UE5_FREEZE_HELPER_VERSION = CUR
  local sentinel = function() return 'SAME' end
  freezeProperty = sentinel
  _ue5_invoke_busy = true                    -- in-flight state that must survive a re-load
  assert(loadfile(HELPER))()
  eq(freezeProperty, sentinel, 'AA30: same version does not redefine (state preserved)')
  eq(_ue5_invoke_busy, true, 'AA30: and the shared busy flag is untouched')
  _ue5_invoke_busy = false                   -- restore for any later cases
end

case('AA30: an OLDER file re-added does not downgrade a newer resident helper')
do
  resetWorld()
  UE5_FREEZE_HELPER_VERSION = NEWER
  local sentinel = function() return 'NEWER' end
  freezeProperty = sentinel
  assert(loadfile(HELPER))()
  eq(freezeProperty, sentinel, 'AA30: the newer resident is kept; the older file is a no-op')
  eq(UE5_FREEZE_HELPER_VERSION, NEWER, 'AA30: and the resident version is not downgraded')
  -- Restore a REAL freezeProperty so the process ends in a clean state.
  UE5_FREEZE_HELPER_VERSION = nil; assert(loadfile(HELPER))()
end

-- ============================================================
-- AA31: the abandon modal must name the FIRST failure of the streak, not the
-- busy-guard CONSEQUENCE of it.
-- ============================================================
-- Found closing AA3 step 5 on a live game (2026-08-23): the DLL was SUSPENDED, and
-- the modal reported `mailbox busy (concurrent invoke or rescan)` -- a TRANSIENT
-- concurrency cause offered for a PERMANENT fault, in the one place a user ever
-- reads it.
--
-- The cascade is structural, not a fluke. waitDone's timeout path is
-- `if not wok then return nil, 0, werr end` and does NOT clear OFF_CMD -- deliberately,
-- because the DLL may still write into the mailbox later. So rescan #1 times out with
-- cmd left set and rescans #2/#3 short-circuit on the in-flight guard in microseconds.
-- `_lastError` is overwritten by each, so the abandon message is GUARANTEED to blame the
-- busy guard whenever the first failure was a timeout -- which is precisely the "it took
-- the command and wedged" shape CLAUDE.md requires be told apart from "the DLL never
-- picked it up". It also means the streak is NOT three 5 s waits: it is one, then two
-- instant returns.

case('AA31: the abandon message names the FIRST error, not the busy-guard consequence')
do
  resetWorld()
  -- Hold the opts table so the fake DLL can be killed AFTER a healthy start -- which
  -- is the real scenario: the freeze was working, then the process wedged.
  local opts = { pages = { {} } }
  installMailbox(opts)
  local h = newHandle{ className = 'C', propOffset = 0x10, valueType = 'int32', value = 1 }
  eq(h.isAbandoned(), false, 'AA31: healthy at the start -- the mailbox answers')
  opts.deadPage = 0                                -- the DLL stops picking commands up
  for _ = 1, 3 do rescanTimer().OnTimer() end
  eq(h.isAbandoned(), true, 'AA31: abandoned after 3 consecutive failures')
  local msg = PRINTS[#PRINTS]
  check(msg and msg:find('never picked this up', 1, true) ~= nil,
        'AA31: the message names the TIMEOUT that actually caused the abandonment', msg)
end

case('AA31 control: an unchanging cause is still reported unchanged')
do
  resetWorld()
  local h = newHandle{ className = 'MyClass', propOffset = 0x20, valueType = 'int32', value = 99 }
  for _ = 1, 3 do rescanTimer().OnTimer() end
  eq(h.isAbandoned(), true, 'AA31 control: abandoned')
  local msg = PRINTS[#PRINTS]
  check(msg and msg:find('g_invokeMailbox', 1, true) ~= nil,
        'AA31 control: every failure identical -> that cause is what is reported', msg)
end

-- ============================================================

realPrint(string.format('\n%d checks, %d failure(s)', checks, failures))
os.exit(failures == 0 and 0 or 1)
