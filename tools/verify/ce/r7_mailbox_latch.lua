--[[
  r7_mailbox_latch.lua -- drive the CE-side shared mailbox latch from CE's Lua Engine.

  For Review 7 rows R7-C-01, R7-C-03 and R7-S3 (docs/review7-live-plan.md sessions 19 / 23): the freeze helper and
  the invoke helper share one mailbox and one latch (_ue5_invoke_busy / _ue5_invoke_stale_mb). This rig loads a
  given pair of helpers, reads the latch and the mailbox, and issues ONE freeze rescan or ONE invoke on demand, so a
  timeout can be provoked (suspend the game with tools/verify/suspend.py) and what happens next observed.

  In CE's Lua Engine, attached to the game:
      dofile([[D:\Github\UE5CEDumper\tools\verify\ce\r7_mailbox_latch.lua]])
      R7.load([[D:\Github\UE5CEDumper\scripts]])          -- or a pre-fix copy's folder
      R7.state('pre')   R7.freeze('WorldSettings')   R7.invoke()   R7.check()   R7.stop()

  Everything is appended to out\r7live\latch\lua.log with getTickCount(), and printed.

  ⚠ R7.freeze's filter DROPS every address before it is cached, so the handle never writes a byte: only its
  rescan (a LIST_INSTANCES mailbox command) runs. R7.invoke calls DumperTestActor::Spawn_CountHolders, which is
  const and read-only. ⚠ invokeUFunction is guarded against re-declaration, so a CE process that ever loaded one
  helper version keeps it: run a red arm in a FRESH CE process.
]]

R7 = R7 or {}
local LOG = [[D:\Github\UE5CEDumper\out\r7live\latch\lua.log]]

local function log(fmt, ...)
  local line = string.format('[%d] ', getTickCount()) .. string.format(fmt, ...)
  local f = io.open(LOG, 'a')
  if f then f:write(line, '\n'); f:close() end
  print(line)
end
R7.log = log

local function mailbox()
  local a = getAddressSafe('g_invokeMailbox')
  if not a or a == 0 then a = getAddressSafe('UE5Dumper.g_invokeMailbox') end
  return a
end

local function hex(v) return v and string.format('0x%X', v) or 'nil' end

function R7.load(dir)
  dofile(dir .. [[\ue5_invoke_helper.lua]])
  dofile(dir .. [[\ue5_freeze_helper.lua]])
  log('loaded %s: invoke helper %s, freeze helper %s', dir, tostring(UE5_INVOKE_HELPER_VERSION),
      tostring(UE5_FREEZE_HELPER_VERSION))
end

function R7.loadFreeze(dir)
  dofile(dir .. [[\ue5_freeze_helper.lua]])
  log('loaded freeze helper from %s: %s', dir, tostring(UE5_FREEZE_HELPER_VERSION))
end

function R7.state(tag)
  local m = mailbox()
  local cmd, st, cls, fn
  if m and m ~= 0 then
    cmd = readInteger(m)
    st = readInteger(m + 4)
    cls = readString(m + 0x28, 64)
    fn = readString(m + 0x128, 64)
  end
  log('state %s: busy=%s stale=%s mb=%s cmd=%s status=%s class=%s func=%s', tostring(tag),
      tostring(_ue5_invoke_busy), hex(_ue5_invoke_stale_mb), hex(m), tostring(cmd), tostring(st),
      tostring(cls), tostring(fn))
end

function R7.freeze(cls, keep)
  R7.n = 0
  local t0 = getTickCount()
  local ok, h = pcall(freezeProperty, {
    className = cls, derived = true, valueType = 'int32', propOffset = 0, value = 0,
    filter = function(a) R7.n = R7.n + 1; return false end,     -- drop everything: never write
    refreshIntervalSec = keep and 2 or 3600,
  })
  if not ok then log('freeze %s: freezeProperty raised %s', tostring(cls), tostring(h)); return false, h end
  R7.h = h
  local sok, err, count, capped = h.start()
  log('freeze %s start: ok=%s err=%s count=%s capped=%s filter_n=%d in %d ms', tostring(cls), tostring(sok),
      tostring(err), tostring(count), tostring(capped), R7.n, getTickCount() - t0)
  if not keep then h.stop(); R7.h = nil end
  return sok, err
end

function R7.invoke()
  local t0 = getTickCount()
  local ok, a, b = invokeUFunction('DumperTestActor', 'Spawn_CountHolders', 4, {})
  log('invoke Spawn_CountHolders: ok=%s r1=%s r2=%s in %d ms', tostring(ok), tostring(a), tostring(b),
      getTickCount() - t0)
  return ok, a, b
end

function R7.check()
  if not R7.h then log('check: no kept handle'); return end
  log('check: isAbandoned=%s lastError=%s filter_n=%d', tostring(R7.h.isAbandoned()), tostring(R7.h.lastError()),
      R7.n or -1)
end

function R7.stop()
  if R7.h then R7.h.stop(); R7.h = nil; log('stopped the kept handle') end
end

log('r7_mailbox_latch loaded')
