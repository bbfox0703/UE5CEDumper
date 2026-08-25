--[[
  freeze_enable_capped_test.lua
  UE5CEDumper -- FREEZESCOPE step 6, the half that was filed as "needs Cheat Engine".

  WHAT THE ROW ASKED FOR
    With the derived instance pool driven OVER the cap, tick the generated Freeze record
    from CE and observe (a) the honesty line firing -- "CAP REACHED, so that is a floor,
    not a total: more instances exist and are NOT held" -- and (b) the CE Lua Engine
    window STAYING OPEN instead of auto-closing over that notice.

  WHY THIS FILE EXISTS RATHER THAN A CE SESSION
    Neither (a) nor (b) is a fact about Cheat Engine. Both are decided by the [ENABLE]
    block's own Lua: (a) is an ungated print() on the `elseif scapped` arm, and (b) is
    the ABSENCE of a call, gated on `... and not scapped ...`. The C# suite pins that
    both strings are EMITTED -- but a text assertion structurally cannot tell a reachable
    branch from dead code, which is precisely the gap this closes: the block is loaded
    and RUN.

  THE SEAM, stated so the two halves cannot drift
    `handleOrErr.start()` returns (ok, err, count, capped) and the block destructures it
    as `local sok, sok2, serr, scount, scapped = pcall(...)`. This file owns everything
    AFTER that 5th value; freeze_helper_test.lua:785 owns everything before it (a real
    LI_OUT_TRUNCATED reply -> capped == true, with an uncapped control). The seam itself
    is asserted below, so a change to start()'s arity fails here rather than silently
    splitting the two.

  ⚠ THE TRAP THIS RIG HAD TO HANDLE, and it is not obvious
    The [ENABLE] block loads the helper ITSELF -- `local tf = findTableFile(...)` with an
    early `return` when tf is nil (FreezeScriptGenerator.cs:243). A rig that merely
    pre-defines freezeProperty in _G never reaches the capped arm at all: the block
    returns at that line having printed nothing and closed nothing, which reads as
    "no CAP line, window stayed open" -- i.e. a PASS on the capped case, for the wrong
    reason. So findTableFile/createStringStream are stubbed to actually SERVE a helper,
    and the arming control below is what catches it if they ever stop working.

  RUNNING IT
      lua scripts/tests/freeze_enable_capped_test.lua
    Exit 0 = all pass, 1 = a failure (with the case named).

  THE FIXTURE
    scripts/tests/fixtures/freeze_enable.lua.txt is the real generator output, checked in
    (unlike out/slotsym/*.txt, which are manual captures in gitignored scratch and leave
    contract_check_test.lua unrunnable on a clean tree).
    FreezeScriptGeneratorTests.TheCheckedInFixture_IsStillWhatTheGeneratorEmits fails if
    it goes stale, so the two cannot drift.

  DELIBERATELY NOT WIRED INTO build.ps1 OR CI
    Same reasoning as its siblings: a standalone `lua` is not a declared dependency, and
    a step that silently skips when its tool is missing is worse than a documented manual
    one that fails loudly.
]]

local pass, fail = 0, 0
local function check(label, cond, got)
  if cond then pass = pass + 1
  else fail = fail + 1; print(string.format("  FAIL  %s   got: %s", label, tostring(got))) end
end

-- ============================================================
-- The fixture: the REAL emitted [ENABLE] block
-- ============================================================

local here = (arg and arg[0] or ''):gsub('[^/\\]*$', '')
local SRC  = here .. '../../scripts/tests/fixtures/freeze_enable.lua.txt'
local fh = io.open(SRC, 'rb')
if not fh then
  print('FAIL: fixture not found: ' .. SRC)
  print('      regenerate it -- see FreezeScriptGeneratorTests, the fixture test.')
  os.exit(1)
end
local raw = fh:read('a'):gsub('\r\n', '\n'); fh:close()

local enableSrc = raw:match('%[ENABLE%]%s*{%$lua}%s*(.-)%s*{%$asm}')
if not enableSrc then print('FAIL: could not split the [ENABLE] block'); os.exit(1) end

-- ANTI-VACUITY. Everything below asserts what this block does; if the block were not the
-- one under test -- a renamed variable, a restructured arm -- the assertions would pass by
-- never reaching anything. These four are the exact surface the cases depend on.
check('fixture: destructures start() into FIVE values including scapped',
      enableSrc:find('local sok, sok2, serr, scount, scapped', 1, true) ~= nil)
check('fixture: has the capped arm',
      enableSrc:find('elseif scapped then', 1, true) ~= nil)
check('fixture: the close is gated on `not scapped`',
      enableSrc:find('not scapped', 1, true) ~= nil)
check('fixture: loads the helper through findTableFile',
      enableSrc:find("findTableFile('ue5_freeze_helper.lua')", 1, true) ~= nil)

-- ============================================================
-- Cheat Engine stubs
-- ============================================================

-- The "helper" the block loads through findTableFile. Deliberately MINIMAL: the real
-- helper's behaviour is freeze_helper_test.lua's job (159 checks, including a genuine
-- LI_OUT_TRUNCATED reply). What matters here is that the loader path RUNS and hands the
-- block a start() whose 4 returns this rig controls.
local function helperText(count, capped)
  return ([[
    function freezeProperty(cfg)
      return {
        start = function() return true, nil, %d, %s end,
        stop  = function() end,
        isTruncated = function() return %s end,
      }
    end
  ]]):format(count, tostring(capped), tostring(capped))
end

local function runBlock(opts)
  local prints, closed, messages = {}, false, {}

  local env = setmetatable({}, {__index = _G})
  env.syntaxcheck = false
  env.UE5_DEBUG   = 0
  env.memrec      = { Active = true }
  env.print       = function(...)
    local parts = {}
    for i = 1, select('#', ...) do parts[#parts + 1] = tostring((select(i, ...))) end
    prints[#prints + 1] = table.concat(parts, '\t')
  end
  env.showMessage = function(m) messages[#messages + 1] = tostring(m) end
  env.getLuaEngine = function() return { Close = function() closed = true end } end
  -- Every sibling rig stubs synchronize as "call it now"; the deferral is CE's, and the
  -- thing under test is WHETHER the call happens, not when.
  env.synchronize  = function(f) f() end
  env.createTimer  = function() return { Interval = 0, Enabled = false, destroy = function() end } end

  -- The helper loader. tf.Stream is only ever passed to ss.copyFrom, so it can be opaque.
  local text = helperText(opts.count, opts.capped)
  env.findTableFile = opts.noHelper and function() return nil end
                      or function() return { Stream = { Size = #text } } end
  env.createStringStream = function()
    return { copyFrom = function() end, DataString = text, destroy = function() end }
  end

  local fn, err = load(enableSrc, 'ENABLE', 't', env)
  if not fn then return nil, 'load error: ' .. tostring(err) end
  local ok, rerr = pcall(fn)
  if not ok then return nil, 'run error: ' .. tostring(rerr) end

  return { prints = table.concat(prints, '\n'), closed = closed,
           messages = table.concat(messages, '\n'), active = env.memrec.Active }
end

-- ============================================================
-- Cases
-- ============================================================

print('freeze_enable_capped_test.lua -- FREEZESCOPE step 6, CE half')

do -- THE CASE THE ROW IS ABOUT
  local r, e = runBlock{ count = 1024, capped = true }
  check('capped: the block ran at all', r ~= nil, e)
  if r then
    check('capped: the CAP REACHED honesty line is PRINTED',
          r.prints:find('CAP REACHED, so that is a floor, not a total', 1, true) ~= nil, r.prints)
    check('capped: it names the count as a floor, not a total',
          r.prints:find('1024 instance(s)', 1, true) ~= nil, r.prints)
    check('capped: the Lua Engine window is NOT closed over the notice', r.closed == false, r.closed)
    -- A capped freeze IS running, so the record must stay ticked. Unticking here would be
    -- the "worse than the bug" outcome step 3 of this row warns about.
    check('capped: the record stays ticked', r.active == true, r.active)
  end
end

do -- ⭐ THE ARMING CONTROL. Without it, "the window stayed open" is equally consistent
   -- with a close that never fires at all -- which is exactly what the findTableFile
   -- early-return produces.
  local r, e = runBlock{ count = 7, capped = false }
  check('control: the block ran at all', r ~= nil, e)
  if r then
    check('control: no CAP line when the pool was not capped',
          r.prints:find('CAP REACHED', 1, true) == nil, r.prints)
    check('control: the window IS closed on an ordinary success', r.closed == true, r.closed)
  end
end

do -- The third arm, for completeness: armed-but-empty must also keep the window open,
   -- and for a DIFFERENT conjunct (`scount ~= 0`). Two arms failing the same and-chain
   -- for different reasons is what shows the chain is read, not short-circuited by luck.
  local r, e = runBlock{ count = 0, capped = false }
  check('empty: the block ran at all', r ~= nil, e)
  if r then
    check('empty: says armed, no live instances',
          r.prints:find('armed: no live instances of', 1, true) ~= nil, r.prints)
    check('empty: the window stays open', r.closed == false, r.closed)
  end
end

do -- ⚠ THE TRAP, asserted rather than trusted: with no helper in the table the block must
   -- bail at findTableFile -- printing NOTHING and closing NOTHING. This is the state a
   -- careless rig sits in while appearing to pass the capped case.
  local r, e = runBlock{ count = 1024, capped = true, noHelper = true }
  check('trap: the block ran at all', r ~= nil, e)
  if r then
    check('trap: no CAP line -- it never reached the arm',
          r.prints:find('CAP REACHED', 1, true) == nil, r.prints)
    check('trap: it says WHY, via showMessage',
          r.messages:find('not found in this table', 1, true) ~= nil, r.messages)
    check('trap: and it closes nothing', r.closed == false, r.closed)
  end
end

print(string.format('\n%d checks, %d failure(s)', pass + fail, fail))
os.exit(fail == 0 and 0 or 1)
