--[[
  untick_bailout_test.lua
  UE5CEDumper -- executable proof for [FREEZEUNTICK-2026-08-20]

  WHY THIS EXISTS
    The C# suite can assert that a generated [ENABLE] block CONTAINS an untick, and for
    two years it did -- while every one of those unticks silently did nothing. Text
    assertions structurally cannot tell a working untick from a no-op, because the
    difference is in Cheat Engine's activation lifecycle, not in the script.

    This harness models that lifecycle from CE's own source and RUNS both shapes against
    it, so "the immediate one does not work" is demonstrated rather than argued.

  THE MODEL -- TMemoryRecord.setActive, Cheat Engine/memoryrecordunit.pas:2573
      if state = fActive then exit;                 -- (1) no-op when already that state
      if processingThread <> nil then exit;         -- (2) no-op while processing
      ...
      if autoassemble(script, ..., state, ...) then -- (3) the [ENABLE] block runs HERE
        fActive := state;                           -- (4) and only NOW does it flip
    While the script runs at (3), fActive is still false. An immediate
    `memrec.Active = false` therefore hits (1) -- state = fActive = false -- and returns
    having changed nothing. Then (4) sets it true regardless.

  RUNNING IT
      lua scripts/tests/untick_bailout_test.lua
    Exit 0 = all pass, 1 = a failure (with the case named).

  DELIBERATELY NOT WIRED INTO build.ps1 OR CI
    Same reasoning as freeze_helper_test.lua: a standalone `lua` is not a declared
    dependency, and a test step that silently skips when its tool is missing is worse
    than a documented manual one that fails loudly. The C# side
    (CeMailboxBailoutTests.EveryDeferredUntick_IsTheSharedEmittersText) pins that the
    generators emit exactly the line this file exercises, so the two cannot drift.
]]

local pass, fail = 0, 0

local function check(label, cond)
  if cond then pass = pass + 1
  else fail = fail + 1; print('  FAIL: ' .. label) end
end

-- ============================================================
-- Cheat Engine model
-- ============================================================

local TIMERS   -- every createTimer() handed out, in creation order

-- A memory record whose Active property behaves the way CE's setActive does.
local function newRecord()
  local state = { fActive = false, processing = false, setAttempts = 0, effective = 0 }
  return setmetatable({}, {
    __index = function(_, k)
      if k == 'Active' then return state.fActive end
      return nil
    end,
    __newindex = function(_, k, v)
      if k ~= 'Active' then rawset(state, k, v); return end
      state.setAttempts = state.setAttempts + 1
      if v == state.fActive then return end        -- (1) CE's early exit
      if state.processing then return end          -- (2) CE's early exit
      state.fActive = v
      state.effective = state.effective + 1
    end,
  }), state
end

local function createTimer(owner, enabled)
  local t = { Interval = 1000, OnTimer = nil, Enabled = (enabled ~= false), destroyed = false }
  function t.destroy() t.destroyed = true end
  TIMERS[#TIMERS + 1] = t
  return t
end

-- CE's message loop: fire every enabled timer that has a handler, once.
local function pumpTimers()
  for _, t in ipairs(TIMERS) do
    if t.Enabled and t.OnTimer and not t.destroyed then t.OnTimer(t) end
  end
end

-- autoassemble(): run the [ENABLE] chunk, then flip fActive -- exactly CE's order.
local function activate(rec, state, chunk)
  local env = { memrec = rec, createTimer = createTimer,
                showMessage = function() end, print = function() end }
  setmetatable(env, { __index = _G })
  TIMERS = {}
  local fn = assert(load(chunk, 'enable', 't', env))
  fn()                                   -- (3) the script runs
  state.fActive = true                   -- (4) CE ticks the record afterwards
  pumpTimers()                           -- ...and only now can a timer fire
end

-- ============================================================
-- The two shapes
-- ============================================================

local IMMEDIATE = [[
if memrec then memrec.Active = false end
return
]]

-- ⚠ MUST stay byte-identical to CeLuaHygiene.DeferredUntickLua(). The C# test
-- EveryDeferredUntick_IsTheSharedEmittersText asserts the generators emit exactly this,
-- and UntickRigMatchesTheEmitter asserts this file still contains it.
local DEFERRED = [[
if memrec then local _u=createTimer(nil,false) _u.Interval=50 _u.OnTimer=function(x) x.destroy() memrec.Active = false end _u.Enabled=true end  -- deferred: CE sets Active AFTER this block, so an immediate untick is a no-op
return
]]

-- ============================================================
-- Cases
-- ============================================================

print('untick_bailout_test.lua -- [FREEZEUNTICK-2026-08-20]')

-- 1. The defect, reproduced.
do
  local rec, state = newRecord()
  activate(rec, state, IMMEDIATE)
  check('immediate: the script DID attempt the untick', state.setAttempts == 1)
  check('immediate: ...but it changed nothing', state.effective == 0)
  check('immediate: record is left ACTIVE (the reported defect)', rec.Active == true)
end

-- 2. The fix.
do
  local rec, state = newRecord()
  activate(rec, state, DEFERRED)
  check('deferred: record ends UNTICKED', rec.Active == false)
  check('deferred: the untick actually took effect', state.effective == 1)
  check('deferred: the timer destroyed itself', TIMERS[1] and TIMERS[1].destroyed == true)
  check('deferred: fired on a real delay, not inline', TIMERS[1] and TIMERS[1].Interval == 50)
end

-- 3. Guard the model: if the model let ANY assignment through, case 1 would pass for the
--    wrong reason. Prove the record is writable once activation has finished.
do
  local rec, state = newRecord()
  activate(rec, state, 'return')
  check('control: record is ACTIVE after a clean enable', rec.Active == true)
  rec.Active = false
  check('control: an EXTERNAL untick works (so the model is not simply read-only)',
        rec.Active == false)
end

-- 4. Guard the model the other way: (2) must be reachable, or the model is understating
--    how many ways an in-script untick can be ignored.
do
  local rec, state = newRecord()
  state.processing = true
  state.fActive = true
  rec.Active = false
  check('control: processingThread <> nil also blocks an untick', rec.Active == true)
end

print(string.format('Pass: %d   Fail: %d', pass, fail))
os.exit(fail == 0 and 0 or 1)
