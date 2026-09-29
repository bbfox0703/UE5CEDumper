--[[
  dissect_test.lua
  UE5CEDumper -- executable tests for scripts/ue5_dissect.lua

  WHY THIS EXISTS
    Audit #5 filed AA4-AA7 against this file and rated them all "not re-derived
    by hand". Two of the four premises turned out to be wrong, and the one that
    mattered most was wrong in the direction that would have made a "fix" delete
    working code. The C# suite can only assert on this file's SOURCE TEXT
    (CeExecuteCodeExArityTests.cs) -- it cannot run it -- so a change could
    satisfy every string assertion and still behave wrongly.

    This harness stubs the Cheat Engine globals the script touches over plain Lua
    tables, then exercises the real functions and checks what actually happened.

  RUNNING IT
      lua scripts/tests/dissect_test.lua
    Exit 0 = all pass, 1 = a failure (with the case named).

  TWO LOAD-ORDER TRAPS, both measured rather than guessed
    1. The vt* constants MUST be defined BEFORE the chunk runs. TYPE_MAP is a
       file-scope table literal, so it captures whatever vtDword et al. are at
       LOAD time. Stub them afterwards and the mapped types silently get
       Vartype = nil -- while EnumProperty, the unknown-type fallback and the
       UObject header rows still resolve correctly, because those read the
       globals at CALL time. A partly-correct structure is harder to diagnose
       than a uniformly broken one.
    2. ue5_dissect.lua RETURNS its public table and defines NO globals, unlike
       ue5_freeze_helper.lua. `chunk()` alone (what the freeze rig does) yields
       nothing to test against -- the return value has to be captured.

  DELIBERATELY NOT WIRED INTO build.ps1 OR CI
    Same reasoning as freeze_helper_test.lua: a standalone `lua` is not a
    declared dependency of this repo, and a test step that silently skips when
    its tool is missing is the defect audit #5's AD1/AD2 just fixed in the C++
    test phase. Run it whenever you touch ue5_dissect.lua.
]]

local HELPER = (arg and arg[0] or ''):gsub('[^/\\]*$', '') .. '../ue5_dissect.lua'

-- ============================================================
-- CE constants -- MUST exist before the chunk loads (trap 1 above)
-- ============================================================
vtByte, vtWord, vtDword, vtSingle, vtDouble, vtQword, vtPointer = 0, 1, 3, 4, 5, 8, 12
vtBinary = 9   -- CE's real value (defines.lua); a single bit uses vtBinary + BitStart/BitSize

-- ============================================================
-- Cheat Engine stubs
-- ============================================================
local SYMBOLS      -- export name -> address (absent = unresolved, CE returns 0)
local CALLS        -- ordered log of every executeCodeEx dispatch
local RESULTS      -- export name -> function(callIndex, ...) -> ret, why
local MEM          -- address -> value written by the fake DLL
local PRINTS       -- captured print() lines
local ALLOCS       -- live allocateMemory blocks (address -> size)
local ALLOC_NEXT   -- bump allocator cursor
local ALLOC_FAIL   -- when true, allocateMemory returns nil
local STRUCTS      -- every createStructure() handed out, in creation order
local GLOBAL_LIST  -- structures registered via addToGlobalStructureList()
local REGISTERED   -- active structure-callback registrations (id-counted, see below)
local REGISTER_FAIL -- when true, registerStructureDissectOverride raises
local TIMERS       -- every createTimer() handed out, in creation order
local UET          -- the fake UETools' call counters and failure switches (installUETools)
local RECORDS      -- the fake address list: memory-record ID -> record (absent = deleted)
local FREED_READS  -- reads and writes of a deleted record's userdata (see deleteRecord)

local function resetWorld()
  SYMBOLS, CALLS, RESULTS, MEM, PRINTS = {}, {}, {}, {}, {}
  ALLOCS, ALLOC_NEXT, ALLOC_FAIL = {}, 0x10000, false
  STRUCTS, GLOBAL_LIST = {}, {}
  REGISTERED = { override = nil, nameLookup = nil, overrideCount = 0, nameLookupCount = 0 }
  REGISTER_FAIL, TIMERS, UET = false, {}, nil
  RECORDS, FREED_READS = {}, 0
  -- CE 7.7's UETools is absent unless a case installs it (see installUETools).
  UEngine, UEngineStructNameLookup, UEngineStructDissect = nil, nil, nil
  registerUEngineStructureLookupCallbacks, unregisterUEngineStructureLookupCallbacks = nil, nil
  UE5_DEBUG = nil
end

function allocateMemory(size)
  if ALLOC_FAIL then return nil end
  local a = ALLOC_NEXT
  ALLOC_NEXT = ALLOC_NEXT + size + 0x100
  ALLOCS[a] = size
  return a
end
function deAlloc(a) ALLOCS[a] = nil end

function readString(a)  return MEM[a] end
function readInteger(a) return MEM[a] end
function readQword(a)   return MEM[a] end

-- CE returns 0 for an unresolved symbol under its DEFAULT configuration --
-- getAddressFromNameL only raises when ExceptionOnLuaLookup is set, and
-- TSymhandler.create sets it FALSE (symbolhandler.pas:5076-5087, :6688).
-- Modelling it as 0 rather than a raise is what makes the "DLL not injected"
-- case testable at all.
function getAddress(name)     return SYMBOLS[name] or 0 end
function getAddressSafe(name) return SYMBOLS[name] end

-- The fake DLL. Each export gets a handler that may write to the out-param
-- buffers and returns (ret, why) exactly as CE's executeCodeEx does.
function executeCodeEx(callmethod, timeout, fn, ...)
  local name = fn and fn.export or '?'
  CALLS[#CALLS + 1] = { name = name, args = { ... }, callmethod = callmethod, timeout = timeout }
  local h = RESULTS[name]
  if not h then return 1 end          -- default: succeed, write nothing
  return h(#CALLS, ...)
end

local realPrint = print
function print(s) PRINTS[#PRINTS + 1] = tostring(s) end

-- A CE structure. Element is 0-BASED (ue5_dissect.lua's addFieldsToStruct and fillGaps both iterate
-- `for i = 0, ceStruct.Count - 1`). removeFromGlobalStructureList/Destroy are
-- called with COLON syntax in dissect.clearAll while addToGlobalStructureList uses DOT
-- in dissect.createFromClass, so these are plain closures that ignore any extra self argument.
function createStructure(name)
  local s = { Name = name, Count = 0, Element = {}, _begin = 0, _end = 0, _destroyed = false }
  s.addElement = function()
    local e = { Offset = 0, Name = '', Vartype = nil, Bytesize = 0, ChildStructStart = nil }
    s.Element[s.Count] = e
    s.Count = s.Count + 1
    return e
  end
  s.beginUpdate = function() s._begin = s._begin + 1 end
  s.endUpdate   = function() s._end   = s._end   + 1 end
  s.addToGlobalStructureList      = function() GLOBAL_LIST[#GLOBAL_LIST + 1] = s end
  s.removeFromGlobalStructureList = function()
    for i, v in ipairs(GLOBAL_LIST) do if v == s then table.remove(GLOBAL_LIST, i); break end end
  end
  s.Destroy = function() s._destroyed = true end
  STRUCTS[#STRUCTS + 1] = s
  return s
end
function createStructureForm() end
function inputQuery() return nil end

-- CE tracks EACH registration separately and hands back a distinct id; a leaked
-- second registration (AA22) survives one unregister. The single-slot model this
-- replaced could not show that: register overwrote the slot and any unregister
-- cleared it. Here `override`/`nameLookup` keep the LATEST fn (so callable, as the
-- AA4 cases need) while the *Count fields track how many registrations are live.
function registerStructureDissectOverride(f)
  if REGISTER_FAIL then error('registerStructureDissectOverride refused') end
  REGISTERED.override = f
  REGISTERED.overrideCount = REGISTERED.overrideCount + 1
  return REGISTERED.overrideCount               -- a distinct id per active registration
end
function registerStructureNameLookup(f)
  REGISTERED.nameLookup = f
  REGISTERED.nameLookupCount = REGISTERED.nameLookupCount + 1
  return 100 + REGISTERED.nameLookupCount
end
function unregisterStructureDissectOverride(_)
  REGISTERED.overrideCount = math.max(0, REGISTERED.overrideCount - 1)
  if REGISTERED.overrideCount == 0 then REGISTERED.override = nil end
end
function unregisterStructureNameLookup(_)
  REGISTERED.nameLookupCount = math.max(0, REGISTERED.nameLookupCount - 1)
  if REGISTERED.nameLookupCount == 0 then REGISTERED.nameLookup = nil end
end

-- CE's createTimer(owner, enabled): a TTimer starts with Interval 1000 and, given
-- enabled=false, disabled. A case fires one with tick(); a destroyed or disabled
-- timer does not fire, as in CE.
function createTimer(owner, enabled)
  local t = { Interval = 1000, OnTimer = nil, Enabled = (enabled ~= false), owner = owner }
  t.destroy = function() t.destroyed = true; t.Enabled = false end
  TIMERS[#TIMERS + 1] = t
  return t
end
local function tick(t)
  if t and not t.destroyed and t.Enabled and type(t.OnTimer) == 'function' then t.OnTimer(t) end
end
local function liveTimers()
  local n = 0
  for _, t in ipairs(TIMERS) do if not t.destroyed then n = n + 1 end end
  return n
end
-- The module's live watch timer, found among the timers CE handed out rather
-- than through a state key: a case pins what runs, not what it is called.
local function theWatch()
  local live = nil
  for _, t in ipairs(TIMERS) do if not t.destroyed then live = t end end
  return live
end

-- CE's address list, shaped on the older CE clone's source (LuaAddresslist.pas,
-- addresslist.pas): getAddressList() returns MainForm's list, and
-- getMemoryRecordByID takes the id from its LAST argument (lua_tointeger(L,-1)),
-- whichever call syntax put it there, and hands back nil for an ID no record has
-- (getRecordWithID returns nil, and luaclass_newClass pushes nil for it).
function getAddressList()
  return {
    getMemoryRecordByID = function(...)
      local n = select('#', ...)
      if n == 0 then return nil end
      return RECORDS[(select(n, ...))]
    end,
  }
end

-- A memory record as an [ENABLE] block's memrec hands it in. ID is CE's unique
-- id; Active is fActive, which CE sets only AFTER the [ENABLE] block returned.
local function newRecord(id)
  local r = { ID = id, Active = false }
  RECORDS[id] = r
  return r
end
-- The record's [ENABLE] returned: CE marks it ticked.
local function ceTicks(r) r.Active = true end
-- CE opened another process and the user agreed to disable the table's entries:
-- disableAllWithoutExecute clears fActive and runs no [DISABLE].
local function ceUnticksWithoutDisable(r) r.Active = false end
-- The user deletes the record while it is ticked: TMemoryRecord.destroy runs no
-- [DISABLE], and a userdata still held for it points at freed memory, so any
-- read or write of it is counted and raises.
local function deleteRecord(r)
  RECORDS[r.ID] = nil
  for k in pairs(r) do r[k] = nil end
  setmetatable(r, {
    __index = function() FREED_READS = FREED_READS + 1; error('read of a freed TMemoryRecord') end,
    __newindex = function() FREED_READS = FREED_READS + 1; error('write to a freed TMemoryRecord') end,
  })
end

-- ============================================================
-- A fake of CE 7.7's Extensions\UETools, shaped on its own source
-- (UEInfoStructureDissect.LUA and UEInfoScanner.LUA, CE 7.7):
--   * two globals hold its registrations, and its unregister function clears
--     both, in order: the name lookup is unregistered and set to nil BEFORE the
--     dissect override is touched, so a raise in the second call leaves UETools
--     half suspended;
--   * its scanner, once it recognizes the game, registers them and creates the
--     "Use when dissecting structures" item, AutoCheck and Checked, in one
--     synchronize() call;
--   * on a process change it rebuilds UEngine, dropping its dissect override but
--     NOT its name lookup, and creates the item again only when the new scan
--     finishes.
-- The item is a plain table: assigning Checked runs nothing, as LCL's
-- TMenuItem.SetChecked never calls Click (only a user's click fires OnClick).
-- The fake register does not call unregister first, so UET.unreg counts
-- suspensions and UET.reg counts registrations, nothing else.
-- ============================================================
local function uetScanCompletes()
  UEngineStructNameLookup, UEngineStructDissect = 2, 1
  UEngine.GUI.miStructureDissectCallbackStatus = { Checked = true }
end

-- state: 'live' (scan done), 'scanning' (installed, game not recognized yet) or
-- 'stale' (rebuilt after a process change, the old name lookup still set).
local function installUETools(state)
  UET = { reg = 0, unreg = 0, failRegister = false, failUnregister = false }
  -- failUnregister = 'first' raises in its first call, before anything is
  -- cleared; any other true value raises in the second, as a rejected
  -- unregisterStructureDissectOverride2 would, after the name lookup is gone.
  function unregisterUEngineStructureLookupCallbacks()
    if UET.failUnregister == 'first' then error('unregisterStructureNameLookup: invalid id') end
    UEngineStructNameLookup = nil
    if UET.failUnregister then error('unregisterStructureDissectOverride2: invalid id') end
    UET.unreg = UET.unreg + 1
    UEngineStructDissect = nil
  end
  function registerUEngineStructureLookupCallbacks()
    if UET.failRegister then error('registerStructureDissectOverride2 failed') end
    UET.reg = UET.reg + 1
    UEngineStructNameLookup, UEngineStructDissect = 2, 1
  end
  UEngine = { GUI = {} }
  if state == 'live' then uetScanCompletes() end
  if state == 'stale' then UEngineStructNameLookup = 2 end
end

-- UEInfoScanner starting over, as its OnProcessOpened makes it. A NEW process
-- rebuilds UEngine and drops the dissect override but not the name lookup; a
-- re-scan of the same process keeps UEngine. Either way createUEMenu() replaces
-- UEngine.GUI, so there is no item until uetScanCompletes() makes a NEW one.
local function uetStartsOver(newProcess)
  if newProcess then
    UEngineStructDissect = nil
    UEngine = { GUI = {} }
  else
    UEngine.GUI = {}
  end
end

-- The user clicks the item: LCL's AutoCheck flips Checked, then UETools' OnClick.
local function userClicksItem()
  local item = UEngine.GUI.miStructureDissectCallbackStatus
  item.Checked = not item.Checked
  if item.Checked then registerUEngineStructureLookupCallbacks()
  else unregisterUEngineStructureLookupCallbacks() end
end

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

local function contains(hay, needle, label)
  check(type(hay) == 'string' and hay:find(needle, 1, true) ~= nil, label,
        string.format('%q does not contain %q', tostring(hay), needle))
end

local function case(name) realPrint('- ' .. name) end

-- ============================================================
-- Load the module under test (trap 2 above: capture the return)
-- ============================================================
resetWorld()
local chunk, err = loadfile(HELPER)
if not chunk then realPrint('cannot load ue5_dissect.lua: ' .. tostring(err)); os.exit(1) end
local dissect = chunk()
if type(dissect) ~= 'table' then
  realPrint('ue5_dissect.lua did not return its public table'); os.exit(1)
end

-- Every export the script can reach for, resolved to a distinct fake address.
local EXPORTS = {
  'UE5_WalkClassBegin', 'UE5_WalkClassGetField', 'UE5_WalkClassEnd',
  'UE5_GetFieldStructClass', 'UE5_GetFieldBoolMask', 'UE5_GetClassPropsSize',
  'UE5_GetObjectName', 'UE5_GetObjectClass', 'UE5_FindObject', 'UE5_FindClass',
  'UE5_GetObjectOuter',
}
local function injectDll()
  for _, n in ipairs(EXPORTS) do SYMBOLS[n] = { export = n } end
end

-- Drive a successful class walk of `fields`, each {name=, typeName=, offset=, size=}.
-- Mirrors what the real UE5_WalkClassGetField does: it writes the out-param
-- buffers and returns true (1); on failure it returns false WITHOUT touching
-- them (Frieren.cpp), which is what makes a mishandled failure look
-- like the previous field.
local function serveWalk(fields, failIndex, failMode)
  RESULTS['UE5_WalkClassBegin'] = function() return #fields end
  RESULTS['UE5_WalkClassEnd']   = function() return 1 end
  RESULTS['UE5_GetClassPropsSize'] = function() return 0x40 end
  RESULTS['UE5_GetObjectName'] = function(_, obj, buf) MEM[buf] = 'FakeClass'; return 1 end
  -- Default: a root-ish object with no Outer. detectOuterOffset must then fall
  -- back to 0x20 rather than treat "no evidence" as evidence. Individual cases
  -- override this to model a case-preserving-FName build.
  RESULTS['UE5_GetObjectOuter'] = function() return 0 end
  RESULTS['UE5_WalkClassGetField'] = function(_, i, addrBuf, nameBuf, _n, typeBuf, _t, offBuf, sizeBuf)
    if failIndex ~= nil and i == failIndex then
      -- Leave every buffer exactly as the previous field left it.
      if failMode == 'nil' then return nil, 'Execution timeout' end
      return 0                                   -- DLL returned false
    end
    local f = fields[i + 1]
    MEM[nameBuf] = f.name
    MEM[typeBuf] = f.typeName
    MEM[offBuf]  = f.offset
    MEM[sizeBuf] = f.size
    MEM[addrBuf] = 0xF000 + i
    return 1
  end
end

local THREE = {
  { name = 'Health', typeName = 'IntProperty',   offset = 0x30, size = 4 },
  { name = 'Mana',   typeName = 'FloatProperty', offset = 0x34, size = 4 },
  { name = 'Speed',  typeName = 'FloatProperty', offset = 0x38, size = 4 },
}

-- ============================================================
-- Baseline -- the happy path must still work
-- ============================================================

case('happy path: three fields become three elements plus the UObject header')
do
  resetWorld(); dissect.clearAll(); injectDll(); serveWalk(THREE)
  local s = dissect.createFromClass(0xC1A55, 'Baseline')
  check(s ~= nil, 'a structure was created')
  eq(#GLOBAL_LIST, 1, 'registered in CE\'s global structure list')
  eq(s.Count, 3 + 6, 'three walked fields + six UObject header rows')
  eq(s.Element[0].Name, 'Health', 'first element is the first field')
  eq(s.Element[0].Offset, 0x30, 'first element keeps its offset')
  eq(s.Element[0].Vartype, vtDword, 'IntProperty maps to vtDword')
  eq(s.Element[1].Vartype, vtSingle, 'FloatProperty maps to vtSingle')
  eq(s._begin, s._end, 'beginUpdate and endUpdate are balanced')
end

-- ============================================================
-- AA4 -- an unresolved export, and what must NOT escape into CE
-- ============================================================

case('AA4: an unresolved export is named in the error (getAddress returns 0, it does not raise)')
do
  -- CE's default is ExceptionOnLuaLookup = FALSE (symbolhandler.pas:6688), so
  -- getAddress hands back 0 rather than raising. The audit called this branch
  -- dead code; it is the ONLY thing that turns "DLL not injected" into a named
  -- diagnostic instead of a call to address 0.
  resetWorld(); dissect.clearAll()   -- no injectDll()
  local ok, e = pcall(dissect.createFromClass, 0xC1A55, 'NoDll')
  eq(ok, false, 'the call fails rather than building something')
  contains(e, 'UE5_WalkClassBegin', 'the message names the export that could not be found')
  eq(#GLOBAL_LIST, 0, 'nothing was registered with CE')
end

case('AA4: a failure inside the dissect override does NOT escape into CE')
do
  -- CE re-raises a Lua error from this callback as a PASCAL exception
  -- (LuaCaller.pas:1229-1232) into a dispatch loop with no handler
  -- (StructuresFrm2.pas:1451-1458), which skips CE's own autoGuessStruct
  -- fallback at :1460. One raise here breaks Structure Dissect for EVERY
  -- address -- UObject or not -- for the rest of the CE session, because
  -- nothing unregisters the callback and CE never rebuilds its Lua state.
  resetWorld(); dissect.clearAll()
  dissect.enableAutoCallback()
  check(REGISTERED.override ~= nil, 'the override is registered')
  local ok, ret = pcall(REGISTERED.override, createStructure('CEsOwn'), 0xBEEF)
  eq(ok, true, 'the callback returned instead of raising')
  check(ret == false or ret == nil, 'it declined, so CE falls through to autoGuessStruct')
  dissect.disableAutoCallback()
end

case('AA4: a failure inside the name-lookup callback does NOT escape into CE')
do
  resetWorld(); dissect.clearAll()
  dissect.enableAutoCallback()
  local ok, ret = pcall(REGISTERED.nameLookup, 0xBEEF)
  eq(ok, true, 'the callback returned instead of raising')
  eq(ret, nil, 'it declined, so CE uses its own naming')
  dissect.disableAutoCallback()
end

case('AA4: the override still works normally when the DLL IS there')
do
  resetWorld(); dissect.clearAll(); injectDll(); serveWalk(THREE)
  RESULTS['UE5_GetObjectClass'] = function() return 0xC1A55 end
  dissect.enableAutoCallback()
  local s = createStructure('CEsOwn')
  local ok, ret = pcall(REGISTERED.override, s, 0xBEEF)
  eq(ok, true, 'no raise')
  eq(ret, true, 'the override accepted')
  eq(s.Count, 3 + 6, 'CE\'s structure was populated')
  dissect.disableAutoCallback()
end

-- ============================================================
-- AA5 -- executeCodeEx failing must not be reported as a value
-- ============================================================

case('AA5: a nil from executeCodeEx fails loudly, carrying CE\'s OWN reason')
do
  resetWorld(); dissect.clearAll(); injectDll(); serveWalk(THREE)
  RESULTS['UE5_WalkClassBegin'] = function() return nil, 'Execution timeout' end
  local ok, e = pcall(dissect.createFromClass, 0xC1A55, 'Timeout')
  eq(ok, false, 'the call fails')
  contains(e, 'Execution timeout', 'CE\'s reason is reported, not a guessed one')
  contains(e, 'UE5_WalkClassBegin', 'and the export that failed is named')
  eq(#GLOBAL_LIST, 0, 'nothing registered')
end

case('AA5: the failure does not surface as "attempt to compare nil with number"')
do
  -- The pre-fix shape. createFromClass:360-362 probes with UE5_WalkClassBegin
  -- and then tests `testCount <= 0`, so a nil return raised a Lua type error
  -- naming a line number instead of the DLL call that actually failed.
  resetWorld(); dissect.clearAll(); injectDll(); serveWalk(THREE)
  RESULTS['UE5_WalkClassBegin'] = function() return nil, 'Failure launching thread' end
  local _, e = pcall(dissect.createFromClass, 0xC1A55, 'Compare')
  check(type(e) == 'string' and e:find('compare nil', 1, true) == nil,
        'the message is about the DLL call, not a Lua comparison', e)
end

-- ============================================================
-- AA6 -- a failed field read must never become a field
-- ============================================================

case('AA6: a mid-walk field failure does NOT record a duplicate of the previous field')
do
  -- UE5_WalkClassGetField leaves the out-params untouched when it fails
  -- (Frieren.cpp), so `success ~= 0` being true for nil made the
  -- previous field's buffers read a second time.
  resetWorld(); dissect.clearAll(); injectDll(); serveWalk(THREE, 1, 'nil')
  local ok = pcall(dissect.createFromClass, 0xC1A55, 'MidWalk')
  eq(ok, false, 'the walk fails rather than inventing a field')
  -- Counted into ONE assertion rather than one per element: a per-element loop
  -- makes the suite's own check count depend on how far the build got, so the
  -- fix quietly shrinks the coverage it is being judged by.
  local healthRows = 0
  for _, s in ipairs(STRUCTS) do
    for i = 0, s.Count - 1 do
      if s.Element[i].Name == 'Health' then healthRows = healthRows + 1 end
    end
  end
  check(healthRows <= 1, 'the previous field was not re-recorded under its own name',
        healthRows .. ' rows named Health')
end

case('AA6: a TOTAL DLL failure must not register a structure that is silently garbage')
do
  -- The worst shape, and the one the fix is judged against: with every
  -- GetField call failing, the old code built a full-size structure whose every
  -- walked field had an empty Name and Offset 0 (`name or ""`, `offset or 0`),
  -- registered it with CE, cached it, and logged "Struct created".
  resetWorld(); dissect.clearAll(); injectDll()
  local many = {}
  for i = 1, 40 do many[i] = { name = 'F' .. i, typeName = 'IntProperty', offset = i * 4, size = 4 } end
  serveWalk(many)
  RESULTS['UE5_WalkClassGetField'] = function() return nil, 'Execution timeout' end
  local ok = pcall(dissect.createFromClass, 0xC1A55, 'AllFail')
  eq(ok, false, 'the build fails')
  eq(#GLOBAL_LIST, 0, 'NOTHING was registered with CE')
  for _, s in ipairs(STRUCTS) do
    check(s._destroyed or s.Count == 0, 'no half-built structure was left behind', s.Name)
  end
end

case('AA6: a total failure reports ONCE, not once per field')
do
  -- callDLL used to warn() -- ungated, by design -- on every nil return, and
  -- the walk calls it once per FIELD. 40 fields meant 40 lines over CE's Lua
  -- Engine window, which is the hygiene rule's whole concern.
  resetWorld(); dissect.clearAll(); injectDll()
  local many = {}
  for i = 1, 40 do many[i] = { name = 'F' .. i, typeName = 'IntProperty', offset = i * 4, size = 4 } end
  serveWalk(many)
  RESULTS['UE5_WalkClassGetField'] = function() return nil, 'Execution timeout' end
  pcall(dissect.createFromClass, 0xC1A55, 'Flood')
  check(#PRINTS <= 1, 'at most one line printed, not one per field', #PRINTS .. ' line(s)')
end

case('AA6: the DLL returning false (0) is a skip, not a failure -- and not a duplicate')
do
  -- Distinct from the nil case: false means "this index has no field", which is
  -- a legitimate answer the walk should absorb without inventing a row.
  resetWorld(); dissect.clearAll(); injectDll(); serveWalk(THREE, 1, 'false')
  local s = dissect.createFromClass(0xC1A55, 'SkipOne')
  check(s ~= nil, 'the structure is still built')
  eq(s.Count, 2 + 6, 'the skipped field is absent, not duplicated')
  eq(s.Element[0].Name, 'Health', 'field 0 kept')
  eq(s.Element[1].Name, 'Speed', 'field 2 kept, field 1 skipped')
end

-- ============================================================
-- Cleanup invariants a raising callDLL makes reachable
-- ============================================================

case('a mid-walk failure leaves no orphaned CE structure and no poisoned cache')
do
  resetWorld(); dissect.clearAll(); injectDll(); serveWalk(THREE, 1, 'nil')
  pcall(dissect.createFromClass, 0xC1A55, 'Orphan')
  for _, s in ipairs(STRUCTS) do
    eq(s._begin, s._end, 'beginUpdate/endUpdate balanced even on the failure path')
  end
  -- A second attempt must not be served a cached half-built structure.
  serveWalk(THREE)
  local s2 = dissect.createFromClass(0xC1A55, 'Orphan')
  check(s2 ~= nil, 'a retry after a failure succeeds')
  eq(s2.Count, 3 + 6, 'and builds the real structure, not a cached ruin')
end

case('every target-process allocation is released on the failure path')
do
  resetWorld(); dissect.clearAll(); injectDll(); serveWalk(THREE, 1, 'nil')
  pcall(dissect.createFromClass, 0xC1A55, 'Leak')
  local live = 0
  for _ in pairs(ALLOCS) do live = live + 1 end
  eq(live, 0, 'no buffer left allocated in the target process')
end

case('a failed allocateMemory is refused, not passed on as an address')
do
  resetWorld(); dissect.clearAll(); injectDll(); serveWalk(THREE)
  ALLOC_FAIL = true
  local ok = pcall(dissect.createFromClass, 0xC1A55, 'NoMem')
  eq(ok, false, 'the build fails rather than reading from a nil buffer')
  eq(#GLOBAL_LIST, 0, 'nothing registered')
end


-- ============================================================
-- AA8: UObject::OuterPrivate is NOT at a fixed 0x20.
--
-- On a WITH_CASE_PRESERVING_NAME build FName is 12 bytes rather than 8, so
-- OuterPrivate pads out to +0x28. The DLL detects that (DynOff::UOBJECT_OUTER);
-- this script used to assert a flat 0x20, which labels the FName's
-- DisplayIndex/Number pair as an 8-byte "Outer" pointer and omits the real one.
--
-- The probe asks the DLL for THIS object's Outer and finds which slot agrees,
-- so the two can never drift the way a second copy of the detection would.
-- ============================================================

local function outerAt(slotOffset, outerValue)
  -- Model an object whose Outer really lives at `slotOffset`.
  RESULTS['UE5_GetObjectOuter'] = function() return outerValue end
  MEM[0xC1A55 + slotOffset] = outerValue
end

local function outerRow(st)
  for i = 0, st.Count - 1 do
    if st.Element[i].Name == 'Outer' then return st.Element[i] end
  end
  return nil
end

case('AA8: Outer is placed at 0x20 on an ordinary build')
do
  resetWorld(); dissect.clearAll(); injectDll(); serveWalk(THREE)
  outerAt(0x20, 0xDEAD0000)
  local s = dissect.createFromClass(0xC1A55, 'Normal')
  local e = outerRow(s)
  check(e ~= nil, 'an Outer row exists')
  eq(e.Offset, 0x20, 'ordinary FName layout keeps Outer at 0x20')
end

case('AA8: Outer follows the DLL to 0x28 on a case-preserving-FName build')
do
  resetWorld(); dissect.clearAll(); injectDll(); serveWalk(THREE)
  outerAt(0x28, 0xBEEF0000)
  local s = dissect.createFromClass(0xC1A55, 'CasePreserving')
  local e = outerRow(s)
  check(e ~= nil, 'an Outer row exists')
  eq(e.Offset, 0x28, 'the 12-byte FName pushes Outer to 0x28')
end

case('AA8: a null Outer proves nothing and must fall back, not guess')
do
  -- A root package legitimately has no Outer. Treating 0 as a reading would let
  -- every such object "detect" whichever slot happened to hold 0.
  resetWorld(); dissect.clearAll(); injectDll(); serveWalk(THREE)
  RESULTS['UE5_GetObjectOuter'] = function() return 0 end
  MEM[0xC1A55 + 0x20] = 0
  MEM[0xC1A55 + 0x28] = 0
  local s = dissect.createFromClass(0xC1A55, 'RootPackage')
  eq(outerRow(s).Offset, 0x20, 'no evidence => the common layout')
end

case('AA8: an ambiguous read prefers 0x20 rather than the rarer layout')
do
  resetWorld(); dissect.clearAll(); injectDll(); serveWalk(THREE)
  RESULTS['UE5_GetObjectOuter'] = function() return 0x1234 end
  MEM[0xC1A55 + 0x20] = 0x1234
  MEM[0xC1A55 + 0x28] = 0x1234   -- both agree: not evidence for the rare one
  local s = dissect.createFromClass(0xC1A55, 'Ambiguous')
  eq(outerRow(s).Offset, 0x20, 'ties go to the common layout')
end

case('AA8: the detection is NOT memoised across structures in one Lua state')
do
  -- CE keeps ONE Lua state for the whole session and never rebuilds it, so a
  -- cached 0x28 would follow the user onto the next, non-case-preserving game
  -- and corrupt every structure built there. This is the regression guard for
  -- the "just cache it" optimisation.
  resetWorld(); dissect.clearAll(); injectDll(); serveWalk(THREE)
  outerAt(0x28, 0xBEEF0000)
  eq(outerRow(dissect.createFromClass(0xC1A55, 'GameA')).Offset, 0x28, 'first game detects 0x28')

  MEM[0xC1A55 + 0x28] = nil
  outerAt(0x20, 0xDEAD0000)
  eq(outerRow(dissect.createFromClass(0xC1A55, 'GameB')).Offset, 0x20,
     'the next structure re-detects instead of reusing 0x28')
end

case('AA8: an existing element already covering the slot still wins')
do
  -- addIfMissing's contract: a walked field at that offset is authoritative.
  -- The detection must not smuggle a duplicate row in behind it.
  resetWorld(); dissect.clearAll(); injectDll()
  serveWalk({ { name = 'Owner', typeName = 'ObjectProperty', offset = 0x28, size = 8 } })
  outerAt(0x28, 0xBEEF0000)
  local s = dissect.createFromClass(0xC1A55, 'Covered')
  local n = 0
  for i = 0, s.Count - 1 do if s.Element[i].Offset == 0x28 then n = n + 1 end end
  eq(n, 1, 'exactly one element at 0x28')
  eq(outerRow(s), nil, 'the walked field kept the slot; no duplicate Outer row')
end

-- ============================================================
-- AA37: addUObjectHeader must NOT be stapled onto a UScriptStruct.
--
-- createFromPath -> UE5_FindObject resolves ANY UObject by path, a UScriptStruct
-- included. A UScriptStruct is itself a UObject, so GetObjectClass(addr) ~= 0 does
-- not tell it from a UClass -- the META-class name does (UClass family ends in
-- "Class"; the struct's is "ScriptStruct").
-- ============================================================

case('AA37: createFromPath on a UScriptStruct gets NO UObject header')
do
  resetWorld(); dissect.clearAll(); injectDll()
  local VEC, META_SS = 0x5EC0, 0x55AA
  local VFIELDS = {
    { name = 'X', typeName = 'DoubleProperty', offset = 0x00, size = 8 },
    { name = 'Y', typeName = 'DoubleProperty', offset = 0x08, size = 8 },
    { name = 'Z', typeName = 'DoubleProperty', offset = 0x10, size = 8 },
  }
  serveWalk(VFIELDS)
  RESULTS['UE5_FindObject']     = function() return VEC end
  RESULTS['UE5_GetObjectClass'] = function(_, obj) return (obj == VEC) and META_SS or 0 end
  RESULTS['UE5_GetObjectName']  = function(_, obj, buf)
    MEM[buf] = (obj == META_SS) and 'ScriptStruct' or 'Vector'; return 1
  end
  local s = dissect.createFromPath('/Script/CoreUObject.Vector')
  check(s ~= nil, 'the struct dissect was created')
  eq(s.Count, 3, 'exactly the three real members -- no UObject header stapled on')
  for i = 0, s.Count - 1 do
    local n = s.Element[i].Name
    check(n ~= 'VTable' and n ~= 'Outer' and n ~= 'ObjectFlags' and n ~= 'ObjectIndex'
          and n ~= 'Class' and n ~= 'FNameIndex',
          'no UObject header row over the struct members', tostring(n))
  end
end

case('AA37: a real UClass (meta ends in "Class") STILL gets the header')
do
  -- The regression guard: the fix must keep the header for BlueprintGeneratedClass
  -- and every other UClass-family meta, not just literal "Class".
  resetWorld(); dissect.clearAll(); injectDll()
  local CLS, META_BP = 0xC1A55, 0xB9C1
  serveWalk(THREE)
  RESULTS['UE5_GetObjectClass'] = function(_, obj) return (obj == CLS) and META_BP or 0 end
  RESULTS['UE5_GetObjectName']  = function(_, obj, buf)
    MEM[buf] = (obj == META_BP) and 'BlueprintGeneratedClass' or 'BP_Player_C'; return 1
  end
  local s = dissect.createFromClass(CLS)   -- no structName -> resolves 'BP_Player_C'
  eq(s.Count, 3 + 6, 'a BlueprintGeneratedClass instance keeps the UObject header')
end

-- ============================================================
-- AA26: a packed bitfield bool must be shown as its single bit.
-- ============================================================

case('AA26: a packed bool is shown as one bit (vtBinary + BitStart/BitSize), not ChildStructStart')
do
  resetWorld(); dissect.clearAll(); injectDll()
  serveWalk({ { name = 'bFoo', typeName = 'BoolProperty', offset = 0x40, size = 1 } })
  RESULTS['UE5_GetFieldBoolMask'] = function() return 0x04 end   -- bit 2
  local s = dissect.createFromClass(0xC1A55, 'Packed')
  local e = s.Element[0]
  eq(e.Vartype, vtBinary, 'a packed bool uses vtBinary, not vtByte')
  eq(e.BitStart, 2, 'BitStart is the mask bit index')
  eq(e.BitSize, 1, 'BitSize is 1')
  eq(e.ChildStructStart, nil, 'the mask is NOT stuffed into ChildStructStart')
  contains(e.Name, 'bit 2', 'the name annotates the bit')
end

case('AA26: a native bool (0xFF marker) stays a whole byte and is not mislabelled')
do
  resetWorld(); dissect.clearAll(); injectDll()
  serveWalk({ { name = 'bNative', typeName = 'BoolProperty', offset = 0x40, size = 1 } })
  RESULTS['UE5_GetFieldBoolMask'] = function() return 0xFF end
  local s = dissect.createFromClass(0xC1A55, 'Native')
  local e = s.Element[0]
  eq(e.Vartype, vtByte, '0xFF is the native-bool marker, not a bit mask -> whole byte')
  eq(e.BitStart, nil, 'no bit fields for a native bool')
  eq(e.ChildStructStart, nil, 'and ChildStructStart is not abused')
  check(not e.Name:find('bit', 1, true), 'a native bool is not mislabelled with a bit index', e.Name)
end

case('AA26: mask 0 (no packed mask) also stays a whole byte')
do
  resetWorld(); dissect.clearAll(); injectDll()
  serveWalk({ { name = 'bPlain', typeName = 'BoolProperty', offset = 0x40, size = 1 } })
  RESULTS['UE5_GetFieldBoolMask'] = function() return 0 end
  local s = dissect.createFromClass(0xC1A55, 'Plain')
  eq(s.Element[0].Vartype, vtByte, 'no mask -> whole byte')
end

-- ============================================================
-- AA23 / AA24: nested StructProperty flattening -- depth-cap marker, and no
-- per-field GetObjectName round-trip (dead work that also leaked).
-- ============================================================

case('AA23: a nested struct beyond the depth cap leaves a visible marker, not silence')
do
  resetWorld(); dissect.clearAll(); injectDll()
  local SELF = 0x5E1F
  -- One StructProperty 'Next' whose inner class is SELF again -> infinite nesting,
  -- capped by maxDepth. GetObjectClass = 0 keeps the header out of this case.
  RESULTS['UE5_WalkClassBegin']      = function() return 1 end
  RESULTS['UE5_WalkClassEnd']        = function() return 1 end
  RESULTS['UE5_GetClassPropsSize']   = function() return 0x10 end
  RESULTS['UE5_GetObjectName']       = function(_, _o, buf) MEM[buf] = 'Recursive'; return 1 end
  RESULTS['UE5_GetObjectClass']      = function() return 0 end
  RESULTS['UE5_GetObjectOuter']      = function() return 0 end
  RESULTS['UE5_GetFieldStructClass'] = function() return SELF end
  RESULTS['UE5_WalkClassGetField']   = function(_, i, addrBuf, nameBuf, _n, typeBuf, _t, offBuf, sizeBuf)
    MEM[nameBuf] = 'Next'; MEM[typeBuf] = 'StructProperty'
    MEM[offBuf] = 0x00; MEM[sizeBuf] = 0x10; MEM[addrBuf] = 0xF000 + i
    return 1
  end
  local s = dissect.createFromClass(SELF, 'Recursive', 2)   -- maxDepth = 2
  check(s ~= nil, 'a structure was still created')
  local marker = nil
  for i = 0, s.Count - 1 do
    local n = s.Element[i].Name
    if type(n) == 'string' and n:find('omitted', 1, true) then marker = n; break end
  end
  check(marker ~= nil, 'AA23: a depth-cap marker row is present rather than a silent drop', marker)
end

case('AA24: a StructProperty walk does no per-field GetObjectName round-trip, and leaks nothing')
do
  resetWorld(); dissect.clearAll(); injectDll()
  local OUTER, INNER = 0x00017E5, 0x14EE5
  local lastBegun = nil
  RESULTS['UE5_WalkClassBegin'] = function(_, addr)
    lastBegun = addr
    if addr == OUTER then return 1 elseif addr == INNER then return 2 else return 0 end
  end
  RESULTS['UE5_WalkClassEnd']        = function() return 1 end
  RESULTS['UE5_GetClassPropsSize']   = function() return 0x20 end
  RESULTS['UE5_GetFieldStructClass'] = function() return INNER end
  RESULTS['UE5_GetObjectClass']      = function() return 0 end       -- keep the header out of it
  RESULTS['UE5_GetObjectOuter']      = function() return 0 end
  RESULTS['UE5_GetObjectName']       = function(_, _o, buf) MEM[buf] = 'N'; return 1 end
  RESULTS['UE5_WalkClassGetField']   = function(_, i, addrBuf, nameBuf, _n, typeBuf, _t, offBuf, sizeBuf)
    if lastBegun == OUTER then
      MEM[nameBuf] = 'Pos'; MEM[typeBuf] = 'StructProperty'; MEM[offBuf] = 0x00; MEM[sizeBuf] = 0x10
    else
      MEM[nameBuf] = 'F' .. i; MEM[typeBuf] = 'FloatProperty'; MEM[offBuf] = i * 4; MEM[sizeBuf] = 4
    end
    MEM[addrBuf] = 0xF000 + i
    return 1
  end
  local s = dissect.createFromClass(OUTER, 'Outer')   -- structName given -> no name-resolve call
  eq(s.Count, 2, 'the inner struct fields were flattened in')
  local innerNameCalls = 0
  for _, c in ipairs(CALLS) do
    if c.name == 'UE5_GetObjectName' and c.args[1] == INNER then innerNameCalls = innerNameCalls + 1 end
  end
  eq(innerNameCalls, 0, 'AA24: no per-struct-field GetObjectName round-trip')
  local live = 0; for _ in pairs(ALLOCS) do live = live + 1 end
  eq(live, 0, 'AA24: no target-process buffer leaked')
end

-- ============================================================
-- AA21 / AA22: module state is CE-GLOBAL, so a re-load (Table -> Add File again in
-- one CE session) reuses it rather than duplicating structures / registrations.
-- ============================================================

case('AA21: a second load reuses the global cache -- no duplicate structure')
do
  resetWorld(); injectDll(); serveWalk(THREE)
  dissect.clearAll()
  local s1 = dissect.createFromClass(0xC1A55, 'Shared')
  eq(#GLOBAL_LIST, 1, 'one structure registered')
  local dissect2 = assert(loadfile(HELPER))()      -- re-add the same file
  local s2 = dissect2.createFromClass(0xC1A55, 'Shared')
  eq(s2, s1, 'AA21: the re-loaded module returns the cached structure')
  eq(#GLOBAL_LIST, 1, 'AA21: still one -- no duplicate registered')
end

case('AA22: a second load does not double-register the dissect callbacks')
do
  resetWorld(); dissect.clearAll()
  dissect.enableAutoCallback()
  check(REGISTERED.override ~= nil, 'the override is registered')
  eq(REGISTERED.overrideCount, 1, 'exactly one override registration')
  local dissect2 = assert(loadfile(HELPER))()      -- re-add the same file
  dissect2.enableAutoCallback()
  eq(REGISTERED.overrideCount, 1, 'AA22: the re-load did not register a SECOND override')
  dissect2.disableAutoCallback()                    -- one disable must fully unregister
  eq(REGISTERED.overrideCount, 0, 'AA22: one disable fully unregisters (no orphan)')
  eq(REGISTERED.override, nil, 'AA22: and the override slot is clear')
end

case('AA27: the override auto-unregisters after consecutive failures (DLL gone)')
do
  resetWorld(); dissect.clearAll(); injectDll()
  dissect.disableAutoCallback()   -- clean slate: ST is global, so isolate from prior cases
  dissect.enableAutoCallback()
  eq(REGISTERED.overrideCount, 1, 'the override is registered once')
  -- The DLL goes away: getAddress now returns 0 for every export, so callDLL raises,
  -- callbackBarrier catches it, and after 3 consecutive failures the callbacks
  -- unregister THEMSELVES so CE's own autoGuessStruct comes back for the session.
  SYMBOLS = {}
  for _ = 1, 3 do pcall(REGISTERED.override, createStructure('x'), 0xBEEF) end
  eq(REGISTERED.overrideCount, 0, 'AA27: the override unregistered itself after 3 failures')
  eq(REGISTERED.nameLookupCount, 0, 'AA27: and so did the name-lookup callback')
end

case('AA27: a single failure does NOT unregister -- the control')
do
  resetWorld(); dissect.clearAll(); injectDll()
  dissect.disableAutoCallback()   -- clean slate: ST is global, so isolate from prior cases
  dissect.enableAutoCallback()
  SYMBOLS = {}
  pcall(REGISTERED.override, createStructure('x'), 0xBEEF)   -- one failure only
  eq(REGISTERED.overrideCount, 1, 'AA27: one failure leaves the override registered')
  dissect.disableAutoCallback()
end

-- ============================================================
-- [AOBM-DISSECT-UETOOLS] CE 7.7's own UE dissector answers Define new structure
-- before ours is asked, so while auto dissect is enabled its two hooks are
-- suspended through its OWN functions, and disable puts back what was taken.
-- Measured on DumperTest, CE 7.7: with UETools' hooks on, our override was never
-- called.
-- ============================================================

local ST_ = function() return _ue5_dissect_state end

local function uetCase(name, state)
  case(name)
  resetWorld(); dissect.clearAll()
  dissect.disableAutoCallback()    -- ST is CE-global: isolate from the cases before
  resetWorld()
  if state then installUETools(state) end
end

local function warnedWith(needle)
  for _, p in ipairs(PRINTS) do
    if p:find('[UE5Dissect WARN]', 1, true) and p:find(needle, 1, true) then return true end
  end
  return false
end

uetCase('UETOOLS: live at enable -> its hooks are suspended and its menu item unchecked', 'live')
do
  local item = UEngine.GUI.miStructureDissectCallbackStatus
  local ok, r = pcall(dissect.enableAutoCallback)
  eq(ok, true, 'enable does not raise')
  eq(REGISTERED.overrideCount, 1, 'ours is registered')
  eq(UET.unreg, 1, "UETools' own unregister function was called once")
  eq(UEngineStructNameLookup, nil, "UETools' name lookup is off")
  eq(UEngineStructDissect, nil, "UETools' dissect override is off")
  eq(item.Checked, false, "its 'Use when dissecting structures' item is unchecked")
  check(ST_().suspendedUETools ~= nil, 'the suspension is remembered in the CE-global state')
  local t = ST_().uetoolsWatch
  check(t ~= nil and t.Enabled == true, 'the watch runs anyway: UETools can start over while ours is on')
  eq(r, true, 'a clean enable says so')
  eq(#PRINTS, 0, 'quiet with UE5_DEBUG unset')
  dissect.disableAutoCallback()
end

uetCase('UETOOLS: disable restores exactly what enable suspended', 'live')
do
  local item = UEngine.GUI.miStructureDissectCallbackStatus
  dissect.enableAutoCallback()
  local ok, r = pcall(dissect.disableAutoCallback)
  eq(ok, true, 'disable does not raise')
  eq(REGISTERED.overrideCount, 0, 'ours is unregistered')
  eq(UET.reg, 1, "UETools' own register function was called once")
  check(UEngineStructNameLookup ~= nil and UEngineStructDissect ~= nil, "UETools' hooks are live again")
  eq(item.Checked, true, 'its menu item is checked again')
  eq(ST_().suspendedUETools, nil, 'the suspension is forgotten')
  eq(r, true, 'a clean disable says so')
  eq(#PRINTS, 0, 'quiet with UE5_DEBUG unset')
end

uetCase('UETOOLS: untick, then tick again on the same UETools -> suspended again', 'live')
do
  local item = UEngine.GUI.miStructureDissectCallbackStatus
  dissect.enableAutoCallback()
  dissect.disableAutoCallback()
  eq(item.Checked, true, 'set-up: restored by the untick')
  dissect.enableAutoCallback()
  eq(UET.unreg, 2, 'the second enable suspends it again: the first one\'s dealings end with its disable')
  eq(item.Checked, false, 'and unchecks its item')
  dissect.disableAutoCallback()
  eq(item.Checked, true, 'and the second untick puts it back')
end

uetCase('UETOOLS: absent (older CE, or the extension not loaded) -> nothing touched, no error')
do
  local ok1, r1 = pcall(dissect.enableAutoCallback)
  eq(ok1, true, 'enable does not raise')
  eq(REGISTERED.overrideCount, 1, 'ours is registered')
  eq(#TIMERS, 0, 'no watch timer is created without UETools')
  eq(UEngine, nil, 'no UETools global is created')
  local ok2, r2 = pcall(dissect.disableAutoCallback)
  eq(ok2, true, 'disable does not raise')
  eq(REGISTERED.overrideCount, 0, 'ours is unregistered')
  check(r1 ~= false and r2 ~= false, 'neither end reports a problem')
  eq(#PRINTS, 0, 'and nothing is printed')
end

uetCase('UETOOLS: registering AFTER our enable -> the watch suspends it once, then leaves the user alone', 'scanning')
do
  dissect.enableAutoCallback()
  eq(UET.unreg, 0, 'nothing to suspend while UETools has not recognized the game')
  local t = ST_().uetoolsWatch
  check(t ~= nil and t.Enabled == true and type(t.OnTimer) == 'function', 'a watch timer is running')
  check(t ~= nil and t.Interval >= 1000 and t.Interval <= 5000, 'it ticks every few seconds',
        t and t.Interval)
  tick(t)
  eq(UET.unreg, 0, 'a tick before UETools registers does nothing')

  uetScanCompletes()
  local item = UEngine.GUI.miStructureDissectCallbackStatus
  tick(t)
  eq(UET.unreg, 1, 'the next tick suspends it')
  eq(item.Checked, false, 'and unchecks its item')
  check(ST_().suspendedUETools ~= nil, 'the suspension is remembered')
  check(t ~= nil and not t.destroyed and ST_().uetoolsWatch == t,
        'the watch keeps running: UETools can still start over')

  -- The user turns UETools back on from its menu: a deliberate choice, on the
  -- SAME item. A new item comes with a new scan, not with a click.
  userClicksItem()
  tick(t)
  eq(UET.unreg, 1, "the user's re-check is not fought")
  check(UEngineStructDissect ~= nil, "UETools' hooks stay on")

  local regBefore = UET.reg
  dissect.disableAutoCallback()
  eq(UET.reg, regBefore, 'disable does not register hooks that are already live')
  eq(ST_().suspendedUETools, nil, 'the flag is cleared anyway')
end

uetCase("UETOOLS: a hand re-check after an enable-time suspension is left alone, and not registered twice", 'live')
do
  local item = UEngine.GUI.miStructureDissectCallbackStatus
  dissect.enableAutoCallback()
  eq(item.Checked, false, 'suspended at enable')
  userClicksItem()
  eq(UET.reg, 1, "the user's click registered UETools' hooks")
  local r = dissect.disableAutoCallback()
  eq(UET.reg, 1, 'disable does not register them a second time')
  eq(item.Checked, true, 'the item keeps the state the user gave it')
  eq(r, true, 'nothing to report')
end

uetCase("UETOOLS: the stale name lookup a process change leaves behind is not spent as the one suspension", 'stale')
do
  dissect.enableAutoCallback()
  eq(UET.unreg, 0, 'no item yet: UETools has not finished scanning this game, so nothing is suspended')
  local t = ST_().uetoolsWatch
  check(t ~= nil, 'the watch runs')
  uetScanCompletes()
  tick(t)
  eq(UET.unreg, 1, 'the real registration is suspended when it arrives')
  eq(UEngine.GUI.miStructureDissectCallbackStatus.Checked, false, 'and its item unchecked')
  dissect.disableAutoCallback()
  eq(UEngine.GUI.miStructureDissectCallbackStatus.Checked, true, 'then restored')
end

uetCase('UETOOLS: restore is skipped, without error, when UETools rebuilt itself meanwhile', 'live')
do
  dissect.enableAutoCallback()
  -- The user opened another process: UEInfoScanner destroys its menu and starts over.
  UEngine = { GUI = {} }
  local ok, r = pcall(dissect.disableAutoCallback)
  eq(ok, true, 'no error')
  eq(r, true, 'nothing to report: UETools registers itself when it recognizes the game')
  eq(UET.reg, 0, 'no registration against an engine UETools has not scanned')
  eq(ST_().suspendedUETools, nil, 'the flag is cleared')
  eq(#PRINTS, 0, 'quiet')
end

uetCase('UETOOLS: restore is skipped, without error, when its menu item is gone', 'live')
do
  dissect.enableAutoCallback()
  UEngine.GUI.miStructureDissectCallbackStatus = nil
  local ok = pcall(dissect.disableAutoCallback)
  eq(ok, true, 'no error')
  eq(UET.reg, 0, 'nothing registered')
  eq(ST_().suspendedUETools, nil, 'the flag is cleared')
end

uetCase('UETOOLS: a restore that fails is reported UNGATED and returned to the caller', 'live')
do
  local item = UEngine.GUI.miStructureDissectCallbackStatus
  dissect.enableAutoCallback()
  UET.failRegister = true
  local ok, r, why = pcall(dissect.disableAutoCallback)
  eq(ok, true, 'it does not raise: our own callbacks ARE unregistered')
  eq(REGISTERED.overrideCount, 0, 'ours is unregistered')
  eq(r, false, 'it tells the caller the restore failed, so the record keeps its window open')
  contains(why, 'registerStructureDissectOverride2 failed', "the reason carries UETools' own error")
  check(warnedWith('registerStructureDissectOverride2 failed'), 'printed with UE5_DEBUG unset',
        table.concat(PRINTS, ' | '))
  eq(item.Checked, false, 'the item is not re-checked over hooks that are not registered')
  eq(ST_().suspendedUETools, nil, 'the flag is cleared in every case')
end

uetCase('UETOOLS: a suspension that fails is reported UNGATED, and ours stays registered', 'live')
do
  local item = UEngine.GUI.miStructureDissectCallbackStatus
  UET.failUnregister = 'first'
  local ok, r, why = pcall(dissect.enableAutoCallback)
  eq(ok, true, 'it does not raise: our callbacks are registered')
  eq(REGISTERED.overrideCount, 1, 'ours is registered')
  eq(r, false, 'it tells the caller, so the record keeps its window open without unticking')
  contains(why, 'unregisterStructureNameLookup: invalid id', "the reason carries UETools' own error")
  check(warnedWith('unregisterStructureNameLookup: invalid id'), 'printed with UE5_DEBUG unset',
        table.concat(PRINTS, ' | '))
  eq(item.Checked, true, 'the item is left alone: its hooks may still be live')
  local printed = #PRINTS
  tick(ST_().uetoolsWatch); tick(ST_().uetoolsWatch)
  eq(#PRINTS, printed, 'the watch does not retry it, and warn again, on every tick')
  UET.failUnregister = false
  dissect.disableAutoCallback()
  eq(UET.reg, 0, 'hooks still live at disable are not registered twice')
end

uetCase('UETOOLS: a suspension that fails HALFWAY is put back whole on disable', 'live')
do
  local item = UEngine.GUI.miStructureDissectCallbackStatus
  UET.failUnregister = true   -- raises after its name lookup is already gone
  local r, why = dissect.enableAutoCallback()
  eq(r, false, 'the enable reports it')
  contains(why, 'unregisterStructureDissectOverride2: invalid id', "the reason carries UETools' own error")
  eq(UEngineStructNameLookup, nil, 'half suspended: its name lookup is gone')
  check(UEngineStructDissect ~= nil, 'and its dissect override is still registered')
  UET.failUnregister = false
  local r2 = dissect.disableAutoCallback()
  eq(UET.reg, 1, "disable registers UETools' hooks again: half on is not 'already back on'")
  check(UEngineStructNameLookup ~= nil and UEngineStructDissect ~= nil, 'both of its hooks are live again')
  eq(item.Checked, true, 'its item keeps its tick')
  eq(r2, true, 'a clean disable')
end

uetCase('UETOOLS: if our own registration fails, UETools is left alone', 'live')
do
  local item = UEngine.GUI.miStructureDissectCallbackStatus
  REGISTER_FAIL = true
  local ok = pcall(dissect.enableAutoCallback)
  REGISTER_FAIL = false
  eq(ok, false, 'the enable fails')
  eq(UET.unreg, 0, 'UETools is not suspended in favour of nothing')
  eq(item.Checked, true, 'its item is untouched')
end

uetCase('UETOOLS: disable destroys the watch timer', 'scanning')
do
  dissect.enableAutoCallback()
  local t = ST_().uetoolsWatch
  check(t ~= nil, 'a watch was started')
  dissect.disableAutoCallback()
  check(t ~= nil and t.destroyed == true, 'the timer is destroyed')
  eq(ST_().uetoolsWatch, nil, 'and dropped from the state')
end

uetCase('UETOOLS: a re-load of the module re-uses the running watch instead of starting a second', 'scanning')
do
  dissect.enableAutoCallback()
  local t = ST_().uetoolsWatch
  local firstTick = t and t.OnTimer
  local dissect2 = assert(loadfile(HELPER))()      -- re-add the same file
  -- The already-registered path reaches the watch too (a re-tick after CE's
  -- process-change untick), so this is where a second timer would come from.
  dissect2.enableAutoCallback()
  eq(#TIMERS, 1, 'one watch timer across the re-load')
  eq(ST_().uetoolsWatch, t, 'the same one')
  check(t ~= nil and t.OnTimer ~= firstTick, "it now runs the re-loaded module's tick")
  uetScanCompletes()
  tick(t)
  eq(UET.unreg, 1, 'and still suspends a registration that arrives')
  dissect2.disableAutoCallback()
  eq(liveTimers(), 0, "the re-loaded module's disable destroys it")
end

uetCase('UETOOLS: an item that was already unchecked over live hooks stays unchecked after restore', 'live')
do
  local item = UEngine.GUI.miStructureDissectCallbackStatus
  userClicksItem()                              -- the user turns UETools off ...
  registerUEngineStructureLookupCallbacks()     -- ... and its hooks come back from CE's Lua console
  eq(item.Checked, false, 'set-up: live hooks under an unchecked item')
  dissect.enableAutoCallback()
  eq(UET.unreg, 2, 'the live hooks are suspended')
  dissect.disableAutoCallback()
  eq(UET.reg, 2, 'and registered again on disable')
  eq(item.Checked, false, 'the item is not re-checked: auto mode never unchecked it')
end

uetCase('UETOOLS: restore is skipped when UEngine was rebuilt, even if the item object is the same one', 'live')
do
  local item = UEngine.GUI.miStructureDissectCallbackStatus
  dissect.enableAutoCallback()
  -- A new engine that happens to hand back the same item object: the item alone
  -- cannot tell, so the engine has to.
  UEngine = { GUI = { miStructureDissectCallbackStatus = item } }
  local r = dissect.disableAutoCallback()
  eq(r, true, 'nothing to report')
  eq(UET.reg, 0, 'no registration against an engine UETools has not scanned')
end

uetCase('UETOOLS: the watch treats a rebuilt UEngine as UETools starting over, even with the same item object', 'live')
do
  local item = UEngine.GUI.miStructureDissectCallbackStatus
  dissect.enableAutoCallback()
  UEngine = { GUI = { miStructureDissectCallbackStatus = item } }
  UEngineStructNameLookup, UEngineStructDissect = 2, 1   -- its scan of the new process registered
  item.Checked = true
  tick(ST_().uetoolsWatch)
  eq(UET.unreg, 2, 'the new engine\'s registration is suspended')
  eq(UEngineStructDissect, nil, "UETools' dissect override is off")
  dissect.disableAutoCallback()
end

uetCase("UETOOLS: when our override gives up (DLL gone), UETools' is put back", 'live')
do
  local item = UEngine.GUI.miStructureDissectCallbackStatus
  injectDll()
  dissect.enableAutoCallback()
  SYMBOLS = {}
  for _ = 1, 3 do pcall(REGISTERED.override, createStructure('x'), 0xBEEF) end
  eq(REGISTERED.overrideCount, 0, 'ours unregistered itself')
  eq(UET.reg, 1, "UETools' hooks are registered again")
  eq(item.Checked, true, 'and its item re-checked')
end

-- A suspension is spent per UETools REGISTRATION, not per enable. UETools starts
-- over on every process open (UEInfoScanner's OnProcessOpened), and each finished
-- scan registers again under a NEW item; a user's click re-uses the item it is on.

uetCase('UETOOLS: a re-scan of the same process while ours is on is suspended again; the user re-checking it is not', 'live')
do
  dissect.enableAutoCallback()
  eq(UET.unreg, 1, 'suspended at enable')
  local t = ST_().uetoolsWatch
  check(t ~= nil, 'the watch runs')
  uetStartsOver(false)
  tick(t)
  eq(UET.unreg, 1, 'nothing to suspend while it scans')
  uetScanCompletes()
  local item = UEngine.GUI.miStructureDissectCallbackStatus
  tick(t)
  eq(UET.unreg, 2, "the watch suspends the re-scan's registration")
  eq(UEngineStructDissect, nil, "UETools' dissect override is off again")
  eq(item.Checked, false, 'and the NEW item is unchecked')
  eq(ST_().suspendedUETools and ST_().suspendedUETools.item, item, 'the record names the newest item')
  userClicksItem()
  tick(t)
  eq(UET.unreg, 2, "the user's re-check of that item is left alone")
  check(UEngineStructDissect ~= nil, "UETools' hooks stay on")
  dissect.disableAutoCallback()
end

uetCase('UETOOLS: an item the user turned off BEFORE our enable is theirs: re-checking it later is left alone', 'live')
do
  local item = UEngine.GUI.miStructureDissectCallbackStatus
  userClicksItem()   -- the user turns UETools off (working-lessons 4.5's "to isolate ours")
  eq(UET.unreg, 1, 'their click unregistered it')
  local r = dissect.enableAutoCallback()
  eq(r, true, 'a clean enable')
  eq(UET.unreg, 1, 'nothing to suspend')
  local t = ST_().uetoolsWatch
  check(t ~= nil, 'the watch runs for a later scan')
  userClicksItem()   -- and back on, deliberately, to compare with ours
  tick(t)
  eq(UET.unreg, 1, "the user's re-check of the item they had turned off is not fought")
  eq(item.Checked, true, 'the item keeps the tick they gave it')
  check(UEngineStructDissect ~= nil, "UETools' hooks stay on")
  -- A real late registration still comes with a new item, and is suspended.
  uetStartsOver(false)
  uetScanCompletes()
  tick(t)
  eq(UET.unreg, 2, 'a new scan is suspended')
  eq(UEngine.GUI.miStructureDissectCallbackStatus.Checked, false, 'its new item unchecked')
  dissect.disableAutoCallback()
end

uetCase('UETOOLS: the game restarts with ours still ticked -> the new process\'s UETools is suspended too', 'live')
do
  dissect.enableAutoCallback()
  local t = ST_().uetoolsWatch
  -- CE kept the record ticked (the user answered No to disabling the table's
  -- entries), and UETools scans the restarted game.
  uetStartsOver(true)
  tick(t)
  uetScanCompletes()
  local item = UEngine.GUI.miStructureDissectCallbackStatus
  tick(t)
  eq(UET.unreg, 2, 'suspended again after the restart')
  eq(UEngineStructNameLookup, nil, "UETools' name lookup is off")
  eq(UEngineStructDissect, nil, "UETools' dissect override is off")
  eq(item.Checked, false, 'its new item is unchecked')
  local r = dissect.disableAutoCallback()
  eq(r, true, 'a clean disable')
  eq(UET.reg, 1, "disable puts the new process's UETools back")
  eq(item.Checked, true, 'and re-checks its item')
end

uetCase('UETOOLS: re-ticking after CE unticked the record without [DISABLE] suspends the restarted UETools', 'live')
do
  dissect.enableAutoCallback()
  -- CE opened the restarted game and ran disableAllWithoutExecute: the record is
  -- unticked, [DISABLE] never ran, so ours are still registered. UETools rescans.
  uetStartsOver(true)
  uetScanCompletes()
  local item = UEngine.GUI.miStructureDissectCallbackStatus
  -- The user ticks the record again: [ENABLE] re-loads the module and enables.
  local dissect2 = assert(loadfile(HELPER))()
  local ok, r = pcall(dissect2.enableAutoCallback)
  eq(ok, true, 'enable does not raise')
  eq(r, true, 'a clean enable')
  eq(REGISTERED.overrideCount, 1, 'ours stays registered once')
  eq(UET.unreg, 2, 'the restarted UETools is suspended by the enable itself')
  eq(UEngineStructDissect, nil, "UETools' dissect override is off")
  eq(item.Checked, false, 'its new item is unchecked')
  eq(liveTimers(), 1, 'one watch runs')
  dissect2.disableAutoCallback()
  eq(UET.reg, 1, 'disable puts it back')
  eq(item.Checked, true, 'and re-checks its item')
  eq(liveTimers(), 0, 'and stops the watch')
end

-- ============================================================
-- [AOBM-DISSECT-UETOOLS] The record that turned auto mode on. CE unticks its
-- ticked auto-assembler records WITHOUT running their [DISABLE] when it opens
-- another process and the user agrees to disable the table's entries
-- (disableAllWithoutExecute), and a record deleted while ticked is freed without
-- it too. Ours then stayed registered, and UETools suspended, under a record
-- showing auto mode off. The record hands the module its memrec, and the watch
-- turns auto mode off -- ours unregistered, UETools put back -- once that record
-- is gone, or unticked after the watch has seen it ticked.
-- ============================================================

uetCase('OWNER: the record unticked without [DISABLE] -> the watch turns auto dissect off and puts UETools back', 'live')
do
  local item = UEngine.GUI.miStructureDissectCallbackStatus
  local rec = newRecord(7)
  local r = dissect.enableAutoCallback(rec)
  eq(r, true, 'a clean enable')
  ceTicks(rec)
  local t = theWatch()
  check(t ~= nil, 'a watch runs')
  tick(t)
  eq(REGISTERED.overrideCount, 1, 'a tick with the record ticked changes nothing')
  eq(item.Checked, false, 'UETools stays suspended')
  ceUnticksWithoutDisable(rec)
  tick(t)
  eq(REGISTERED.overrideCount, 0, 'our dissect override is unregistered')
  eq(REGISTERED.nameLookupCount, 0, 'and our name lookup')
  eq(UET.reg, 1, "UETools' hooks are registered again")
  check(UEngineStructNameLookup ~= nil and UEngineStructDissect ~= nil, 'both of its hooks are live')
  eq(item.Checked, true, 'its item is checked again')
  eq(ST_().suspendedUETools, nil, 'the suspension is forgotten')
  eq(liveTimers(), 0, 'the watch stops')
  eq(ST_().ownerRecordId, nil, 'the record is let go')
  eq(#PRINTS, 0, 'quiet with UE5_DEBUG unset')
end

uetCase('OWNER: turning auto dissect off from the watch is logged with UE5_DEBUG on', 'live')
do
  local rec = newRecord(7)
  dissect.enableAutoCallback(rec)
  ceTicks(rec)
  local t = theWatch()
  tick(t)
  UE5_DEBUG = 1
  local before = #PRINTS
  ceUnticksWithoutDisable(rec)
  tick(t)
  local said = false
  for i = before + 1, #PRINTS do
    if PRINTS[i]:find('[UE5Dissect]', 1, true) and PRINTS[i]:find('without its [DISABLE]', 1, true) then said = true end
  end
  check(said, 'a gated line says why auto dissect went off', table.concat(PRINTS, ' | '))
  eq(REGISTERED.overrideCount, 0, 'and it did')
end

uetCase('OWNER: the record deleted while ticked -> the same, and the freed record is never read', 'live')
do
  local item = UEngine.GUI.miStructureDissectCallbackStatus
  local rec = newRecord(7)
  dissect.enableAutoCallback(rec)
  ceTicks(rec)
  local t = theWatch()
  tick(t)
  deleteRecord(rec)
  tick(t)
  eq(REGISTERED.overrideCount, 0, 'ours is unregistered')
  eq(REGISTERED.nameLookupCount, 0, 'both of ours')
  eq(UET.reg, 1, "UETools' hooks are registered again")
  eq(item.Checked, true, 'its item is checked again')
  eq(liveTimers(), 0, 'the watch stops')
  eq(FREED_READS, 0, "the module kept the record's ID, not the record")
  eq(#PRINTS, 0, 'quiet with UE5_DEBUG unset')
end

uetCase('OWNER: a record deleted before the watch saw it ticked still turns auto dissect off', 'live')
do
  local rec = newRecord(7)
  dissect.enableAutoCallback(rec)
  deleteRecord(rec)
  tick(theWatch())
  eq(REGISTERED.overrideCount, 0, 'gone is gone, ticked or not')
  eq(UET.reg, 1, 'UETools is put back')
  eq(FREED_READS, 0, 'the freed record is never read')
end

uetCase('OWNER: a tick before CE marks the record ticked changes nothing', 'live')
do
  local item = UEngine.GUI.miStructureDissectCallbackStatus
  local rec = newRecord(7)
  dissect.enableAutoCallback(rec)
  -- CE sets Active only after the [ENABLE] block returned; a tick can land first.
  tick(theWatch()); tick(theWatch())
  eq(REGISTERED.overrideCount, 1, 'ours stays registered')
  eq(item.Checked, false, 'UETools stays suspended')
  check(theWatch() ~= nil, 'the watch keeps running')
  ceTicks(rec); tick(theWatch())
  ceUnticksWithoutDisable(rec); tick(theWatch())
  eq(REGISTERED.overrideCount, 0, 'once seen ticked, an untick without [DISABLE] is acted on')
end

uetCase('OWNER: re-ticked before the watch noticed the untick -> the record is followed again from scratch', 'live')
do
  local item = UEngine.GUI.miStructureDissectCallbackStatus
  local rec = newRecord(7)
  dissect.enableAutoCallback(rec)
  ceTicks(rec)
  local t = theWatch()
  tick(t)
  ceUnticksWithoutDisable(rec)
  -- The user ticks it again before the next tick: its [ENABLE] lands on the
  -- already-registered path, and CE marks it ticked only after that returns.
  local r = dissect.enableAutoCallback(rec)
  eq(r, true, 'a clean enable')
  eq(REGISTERED.overrideCount, 1, 'ours stays registered once')
  tick(t)
  eq(REGISTERED.overrideCount, 1, 'the re-tick reset the seen-ticked guard: a tick before CE marks it does nothing')
  eq(item.Checked, false, 'UETools stays suspended')
  ceTicks(rec); tick(t)
  ceUnticksWithoutDisable(rec); tick(t)
  eq(REGISTERED.overrideCount, 0, 'an untick after CE marked it again is acted on')
  eq(liveTimers(), 0, 'and the watch stops')
end

uetCase('OWNER: an enable from another record makes that record the one followed', 'live')
do
  local a, b = newRecord(7), newRecord(9)
  dissect.enableAutoCallback(a)
  ceTicks(a)
  local t = theWatch()
  tick(t)
  dissect.enableAutoCallback(b)
  ceTicks(b)
  tick(t)
  deleteRecord(a)
  tick(t)
  eq(REGISTERED.overrideCount, 1, 'the earlier record no longer decides')
  eq(FREED_READS, 0, 'and its freed userdata is never read')
  ceUnticksWithoutDisable(b)
  tick(t)
  eq(REGISTERED.overrideCount, 0, 'the record that enabled last does')
end

uetCase('OWNER: none (an older record, or the Lua console) -> the watch never turns auto dissect off', 'live')
do
  local rec = newRecord(7)   -- in the list, but not handed in
  local r = dissect.enableAutoCallback()
  eq(r, true, 'a clean enable')
  ceTicks(rec)
  local t = theWatch()
  check(t ~= nil, 'the watch runs for UETools')
  tick(t)
  ceUnticksWithoutDisable(rec); tick(t)
  deleteRecord(rec); tick(t)
  eq(REGISTERED.overrideCount, 1, 'ours stays registered, as before a record was handed in')
  eq(UEngine.GUI.miStructureDissectCallbackStatus.Checked, false, 'UETools stays suspended')
  check(theWatch() ~= nil, 'the watch still runs')
  eq(FREED_READS, 0, 'no record is read')
end

uetCase('OWNER: an enable with no record after one with a record follows no record', 'live')
do
  local rec = newRecord(7)
  dissect.enableAutoCallback(rec)
  ceTicks(rec)
  tick(theWatch())
  dissect.enableAutoCallback()      -- CE's Lua console, say
  ceUnticksWithoutDisable(rec)
  tick(theWatch())
  eq(REGISTERED.overrideCount, 1, 'the latest enable named no record, so none decides')
end

uetCase('OWNER: no UETools at all -> the watch still runs for the record, and turns ours off on its untick')
do
  local rec = newRecord(7)
  local r = dissect.enableAutoCallback(rec)
  eq(r, true, 'a clean enable')
  local t = theWatch()
  check(t ~= nil and t.Enabled == true, 'a watch runs for the record, with no UETools to stand in for')
  ceTicks(rec); tick(t)
  eq(REGISTERED.overrideCount, 1, 'a tick with the record ticked changes nothing')
  ceUnticksWithoutDisable(rec); tick(t)
  eq(REGISTERED.overrideCount, 0, 'ours is unregistered by the tick')
  eq(REGISTERED.nameLookupCount, 0, 'both of ours')
  eq(liveTimers(), 0, 'the watch stops')
  eq(UEngine, nil, 'no UETools global is created')
  eq(#PRINTS, 0, 'quiet with UE5_DEBUG unset')
end

uetCase('OWNER: no UETools and no record -> still no watch timer')
do
  dissect.enableAutoCallback(nil)
  eq(#TIMERS, 0, 'nothing to watch, so no timer')
  dissect.disableAutoCallback()
end

uetCase("OWNER: when our override gives up (DLL gone), UETools is put back and the record let go", 'live')
do
  local item = UEngine.GUI.miStructureDissectCallbackStatus
  local rec = newRecord(7)
  injectDll()
  dissect.enableAutoCallback(rec)
  ceTicks(rec)
  SYMBOLS = {}
  for _ = 1, 3 do pcall(REGISTERED.override, createStructure('x'), 0xBEEF) end
  eq(REGISTERED.overrideCount, 0, 'ours unregistered itself')
  eq(UET.reg, 1, "UETools' hooks are registered again")
  eq(item.Checked, true, 'and its item re-checked')
  eq(liveTimers(), 0, 'the watch stops')
  eq(ST_().ownerRecordId, nil, 'the record is let go')
end

uetCase('OWNER: a restore that fails when the watch turns auto dissect off is still reported ungated', 'live')
do
  local item = UEngine.GUI.miStructureDissectCallbackStatus
  local rec = newRecord(7)
  dissect.enableAutoCallback(rec)
  ceTicks(rec)
  tick(theWatch())
  UET.failRegister = true
  ceUnticksWithoutDisable(rec)
  tick(theWatch())
  eq(REGISTERED.overrideCount, 0, 'ours is unregistered')
  check(warnedWith('registerStructureDissectOverride2 failed'), 'the failed restore is printed with UE5_DEBUG unset',
        table.concat(PRINTS, ' | '))
  eq(item.Checked, false, 'the item is not re-checked over hooks that are not registered')
  eq(liveTimers(), 0, 'the watch stops anyway')
end

uetCase('OWNER: [DISABLE] lets the record go', 'live')
do
  local rec = newRecord(7)
  dissect.enableAutoCallback(rec)
  ceTicks(rec)
  dissect.disableAutoCallback()
  eq(ST_().ownerRecordId, nil, 'no record is followed after a disable')
  eq(liveTimers(), 0, 'and no watch runs')
end

uetCase('OWNER: a re-load of the module keeps one watch, following the record its enable was handed', 'live')
do
  local rec = newRecord(7)
  dissect.enableAutoCallback(rec)
  ceTicks(rec)
  local dissect2 = assert(loadfile(HELPER))()      -- the record re-loads the module on every [ENABLE]
  dissect2.enableAutoCallback(rec)
  eq(#TIMERS, 1, 'one watch timer across the re-load')
  ceTicks(rec); tick(theWatch())
  ceUnticksWithoutDisable(rec); tick(theWatch())
  eq(REGISTERED.overrideCount, 0, "the re-loaded module's watch turns ours off")
  eq(liveTimers(), 0, 'and stops')
end

uetCase('OWNER: a game restart answered Yes -> ours goes off, the restarted UETools is left to itself; a re-tick suspends it', 'live')
do
  local rec = newRecord(7)
  dissect.enableAutoCallback(rec)
  ceTicks(rec)
  local t = theWatch()
  tick(t)
  -- CE opens the restarted game: UETools starts over, and the user's Yes unticks
  -- the record without [DISABLE].
  uetStartsOver(true)
  ceUnticksWithoutDisable(rec)
  tick(t)
  eq(REGISTERED.overrideCount, 0, 'ours is unregistered')
  eq(UET.reg, 0, 'nothing is registered against an engine UETools has not scanned')
  eq(liveTimers(), 0, 'the watch stops')
  uetScanCompletes()
  local item = UEngine.GUI.miStructureDissectCallbackStatus
  eq(item.Checked, true, "the restarted UETools' item stays checked")
  check(UEngineStructDissect ~= nil, 'and its hooks answer Define new structure')
  local r = dissect.enableAutoCallback(rec)    -- the user ticks the record again
  eq(r, true, 'a clean enable')
  eq(REGISTERED.overrideCount, 1, 'ours is registered again')
  eq(item.Checked, false, 'the re-tick suspends the restarted UETools')
  eq(liveTimers(), 1, 'one watch')
  dissect.disableAutoCallback()
  eq(item.Checked, true, 'and the untick puts it back')
end

uetCase('OWNER: an address list that cannot be read decides nothing', 'live')
do
  local rec = newRecord(7)
  dissect.enableAutoCallback(rec)
  ceTicks(rec)
  tick(theWatch())
  local saved = getAddressList
  getAddressList = function() error('no address list') end
  ceUnticksWithoutDisable(rec)
  tick(theWatch())
  getAddressList = saved
  eq(REGISTERED.overrideCount, 1, 'ours stays registered: nothing was learnt about the record')
  check(theWatch() ~= nil, 'the watch keeps running')
  eq(#PRINTS, 0, 'quiet with UE5_DEBUG unset')
end

uetCase('OWNER: a record whose ID cannot be read -> followed by nobody, and the enable is still clean', 'live')
do
  local odd = setmetatable({}, { __index = function() error('no ID here') end })
  local ok, r = pcall(dissect.enableAutoCallback, odd)
  eq(ok, true, 'enable does not raise')
  eq(r, true, 'a clean enable')
  eq(REGISTERED.overrideCount, 1, 'ours is registered')
  eq(ST_().ownerRecordId, nil, 'no record is followed')
  tick(theWatch())
  eq(REGISTERED.overrideCount, 1, 'and the watch does not turn ours off')
end

-- ============================================================
-- D4b -- a delegate row's WIDTH depends on the BUILD, not on the property
-- ============================================================
-- UE 5.3 gave TScriptDelegate / TMulticastScriptDelegate a TDelegateAccessHandlerBase base
-- whose DO_CHECK specialization holds one std::atomic<uint64>, so sizeof is 8 bytes larger in
-- Debug/Development/DebugGame than in Shipping/Test. This file baked
--   MulticastInlineDelegateProperty = { size = 16 }
-- and getTypeInfo returned it unconditionally, so on a checked build the row read the detector
-- plus half the TArray header. The engine's own ElementSize is already on the wire as f.size;
-- prefer it, exactly as the EnumProperty branch beside it already does.
do
  local gti = dissect._getTypeInfo
  check(gti ~= nil, 'D4b: getTypeInfo is reachable from the test')

  local _, n16 = gti('MulticastInlineDelegateProperty', 16)
  eq(n16, 16, 'D4b: Shipping multicast keeps 16')
  local _, n24 = gti('MulticastInlineDelegateProperty', 24)
  eq(n24, 24, 'D4b: checked-build multicast takes the reported 24')

  local _, u8 = gti('DelegateProperty', 8)
  eq(u8, 8, 'D4b: Shipping unicast keeps 8')
  local _, u16 = gti('DelegateProperty', 16)
  eq(u16, 16, 'D4b: checked-build unicast takes the reported 16')

  -- ⭐ THE CONTROLS. The wire value is preferred only when it is one of the TWO widths the
  -- layout can actually have; anything else falls back to the baked size rather than widening
  -- a CE row by whatever number came off a garbage ElementSize.
  local _, bad = gti('MulticastInlineDelegateProperty', 1073742336)
  eq(bad, 16, 'D4b control: a garbage ElementSize falls back, it does not widen the row')
  local _, zero = gti('MulticastInlineDelegateProperty', 0)
  eq(zero, 16, 'D4b control: a missing size falls back')
  local _, odd = gti('MulticastInlineDelegateProperty', 20)
  eq(odd, 16, 'D4b control: a width the layout cannot have falls back')

  -- Untouched neighbours: the branch must not leak into other types.
  local _, f = gti('FloatProperty', 24)
  eq(f, 4, 'D4b control: a non-delegate ignores the reported size as before')
  local _, sp = gti('MulticastSparseDelegateProperty', 1)
  eq(sp, 16, 'D4b: sparse is deliberately NOT re-sized here -- see the ⛔ note in TYPE_MAP')
end

-- ============================================================

realPrint(string.format('\n%d checks, %d failure(s)', checks, failures))
os.exit(failures == 0 and 0 or 1)
