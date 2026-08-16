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

-- Set by installMailbox(): a hook the write stubs call so the fake DLL can answer
-- a command the moment the helper triggers it.
MAILBOX_ON_WRITE = nil

local function resetWorld()
  MEM, BYTES, WRITES, PRINTS, SYMBOLS, TIMERS = {}, {}, {}, {}, {}, {}
  MAILBOX_ON_WRITE = nil
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
local CMD_LIST_INSTANCES = 6

--- @param opts table  pages = { {addr,...}, ... } (one entry per page),
---                    result (rc, default 0), classPtr/classOff (witness),
---                    failOnPage = n  -> that page answers rc ~= 0 (AA11's shape),
---                    deadPage   = n  -> that page never answers (waitDone timeout)
local function installMailbox(opts)
  opts = opts or {}
  local pages = opts.pages or { {} }
  SYMBOLS['g_invokeMailbox']   = MB
  SYMBOLS['g_mailboxContract'] = CONTRACT_MB
  -- Contract block: magic, current, minimum. The helper bakes UE5_SCRIPT_CONTRACT=2.
  MEM[CONTRACT_MB + 0x00] = 1127564629
  MEM[CONTRACT_MB + 0x04] = opts.contractCur or 2
  MEM[CONTRACT_MB + 0x08] = opts.contractMin or 1

  MAILBOX_ON_WRITE = function(addr, value)
    if addr ~= MB + OFF_CMD or value ~= CMD_LIST_INSTANCES then return end
    -- The helper wrote the page index to paramsData[0] just before the trigger.
    local pageIndex = MEM[MB + OFF_PARAMS] or 0
    if opts.deadPage and pageIndex == opts.deadPage then
      return  -- status stays 0: the DLL never picked it up
    end
    if opts.failOnPage and pageIndex == opts.failOnPage then
      MEM[MB + OFF_RESULT] = -7
      MEM[MB + OFF_ERR]    = 'simulated page failure'
      MEM[MB + OFF_STATUS] = 1
      return
    end
    local page = pages[pageIndex + 1] or {}
    MEM[MB + OFF_RESULT]     = opts.result or 0
    MEM[MB + OFF_NUM_PARMS]  = #page
    MEM[MB + OFF_FUNC_FLAGS] = #pages
    for i = 1, #page do MEM[MB + OFF_PARAMS + ((i - 1) * 8)] = page[i] end
    MEM[MB + OFF_INSTANCE]   = opts.classPtr or 0xC1A55
    MEM[MB + OFF_UFUNC]      = opts.classOff or 0x10
    MEM[MB + OFF_STATUS]     = 1
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

realPrint(string.format('\n%d checks, %d failure(s)', checks, failures))
os.exit(failures == 0 and 0 or 1)
