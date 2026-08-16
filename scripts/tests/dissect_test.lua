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

local function resetWorld()
  SYMBOLS, CALLS, RESULTS, MEM, PRINTS = {}, {}, {}, {}, {}
  ALLOCS, ALLOC_NEXT, ALLOC_FAIL = {}, 0x10000, false
  STRUCTS, GLOBAL_LIST = {}, {}
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

-- A CE structure. Element is 0-BASED (ue5_dissect.lua:294 / :324 both iterate
-- `for i = 0, ceStruct.Count - 1`). removeFromGlobalStructureList/Destroy are
-- called with COLON syntax at :544-545 while addToGlobalStructureList uses DOT
-- at :401, so these are plain closures that ignore any extra self argument.
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

local REGISTERED = {}
function registerStructureDissectOverride(f) REGISTERED.override = f; return 1 end
function registerStructureNameLookup(f)      REGISTERED.nameLookup = f; return 2 end
function unregisterStructureDissectOverride() REGISTERED.override = nil end
function unregisterStructureNameLookup()      REGISTERED.nameLookup = nil end

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
}
local function injectDll()
  for _, n in ipairs(EXPORTS) do SYMBOLS[n] = { export = n } end
end

-- Drive a successful class walk of `fields`, each {name=, typeName=, offset=, size=}.
-- Mirrors what the real UE5_WalkClassGetField does: it writes the out-param
-- buffers and returns true (1); on failure it returns false WITHOUT touching
-- them (Frieren.cpp:712-731), which is what makes a mishandled failure look
-- like the previous field.
local function serveWalk(fields, failIndex, failMode)
  RESULTS['UE5_WalkClassBegin'] = function() return #fields end
  RESULTS['UE5_WalkClassEnd']   = function() return 1 end
  RESULTS['UE5_GetClassPropsSize'] = function() return 0x40 end
  RESULTS['UE5_GetObjectName'] = function(_, obj, buf) MEM[buf] = 'FakeClass'; return 1 end
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
  -- (Frieren.cpp:712-731), so `success ~= 0` being true for nil made the
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

realPrint(string.format('\n%d checks, %d failure(s)', checks, failures))
os.exit(failures == 0 and 0 or 1)
