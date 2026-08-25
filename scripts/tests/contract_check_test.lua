--[[
  contract_check_test.lua
  UE5CEDumper -- the mailbox CONTRACT CHECK must fire BEFORE the first write.

  WHY THE ORDERING IS THE WHOLE POINT
    `Mimic.h` publishes a contract RANGE and every generated script bakes the version
    it was built against. The check has to happen **before anything is written to the
    mailbox**, because the thing in question IS the layout: if the script's idea of
    the field offsets is wrong, a write placed first lands somewhere unintended. The
    register states it directly -- "the contract check must fire FIRST ... and the
    record must untick itself -- no `writeByte` may have run".

  WHAT IT RUNS
    The real [ENABLE] block the shipping UI emitted, captured to
    out/slotsym/get_gameengine.lua.txt (see working-lessons 2.8), executed over
    stubbed CE globals. Every mailbox write is RECORDED, so "no write happened" is
    measured rather than assumed.

  THE FOUR REFUSALS, each of which must untick and write nothing
    1  g_mailboxContract does not resolve at all
    2  it resolves but the magic is wrong        (a stale address)
    3  the DLL is older than the script          (script > current)
    4  the script is older than the DLL          (script < minimum)
    ...plus a POSITIVE control: with a valid contract the script proceeds and DOES
    write. Without that control, a test that "no write happened" would pass equally
    well against a script that never writes at all.

  RUNNING IT
      lua scripts/tests/contract_check_test.lua
]]

local SRC = (arg and arg[0] or ''):gsub('[^/\\]*$', '') .. '../../out/slotsym/get_gameengine.lua.txt'

local fails, checks = 0, 0
local function check(name, cond, got)
  checks = checks + 1
  if cond then print(string.format("  ok    %s", name))
  else fails = fails + 1; print(string.format("  FAIL  %s   got: %s", name, tostring(got))) end
end

local fh = io.open(SRC, "rb")
if not fh then
  print("FAIL: " .. SRC .. " not found -- capture the emitted script first (working-lessons 2.8)")
  os.exit(1)
end
local raw = fh:read("a"):gsub("\r\n", "\n"); fh:close()
local enableSrc = raw:match("%[ENABLE%]%s*{%$lua}%s*(.-)%s*{%$asm}")
if not enableSrc then print("FAIL: could not split the [ENABLE] block"); os.exit(1) end

local MAGIC = 1127564629          -- MAILBOX_CONTRACT_MAGIC
local MB, CV = 0x10000, 0x20000

-- contract: what the fake DLL reports.  nil CV = symbol does not resolve at all.
local function runWith(opts)
  local writes, msgs = {}, {}
  local env = setmetatable({}, {__index = _G})
  env.syntaxcheck = false
  env.memrec = {Active = true}
  env.UE5_DEBUG = 0
  env.print = function() end
  env.showMessage = function(m) msgs[#msgs+1] = tostring(m) end
  env.getAddressSafe = function(n)
    if n == 'g_invokeMailbox' then return MB end
    if n == 'g_mailboxContract' then return opts.cv end
    return nil
  end
  env.registerSymbol = function() end
  env.unregisterSymbol = function() end
  env.allocateMemory = function() return 0x50000 end
  env.deAlloc = function() end
  env.reinitializeSymbolhandler = function() end
  env.sleep = function() end
  env.processMessages = function() end
  env.processMessagesPaintOnly = function() end
  env.getTickCount = function() return 0 end
  env.synchronize = function() end
  env.getLuaEngine = function() return {Close = function() end} end
  env.readInteger = function(a)
    if opts.cv then
      if a == opts.cv + 0x00 then return opts.magic end
      if a == opts.cv + 0x04 then return opts.cur end
      if a == opts.cv + 0x08 then return opts.min end
    end
    if a == MB + 0x00 then return 0 end
    if a == MB + 0x04 then return 1 end
    if a == MB + 0x08 then return 0 end
    return 0
  end
  env.readQword = function(a) if a == MB + 0x328 then return 0x7FF600001234 end return 0 end
  env.writeInteger = function(a, v) writes[#writes+1] = {"writeInteger", a, v} end
  env.writeQword = function(a, v) writes[#writes+1] = {"writeQword", a, v} end
  env.writeByte = function(a, v) writes[#writes+1] = {"writeByte", a, v} end
  local fn, err = load(enableSrc, "ENABLE", "t", env)
  if not fn then print("  FAIL  compile: " .. tostring(err)); fails = fails + 1; return end
  local ok, e = pcall(fn)
  if not ok then print("  FAIL  raised: " .. tostring(e)); fails = fails + 1 end
  return writes, msgs, env
end

local function mailboxWrites(writes)
  local n = 0
  for _, w in ipairs(writes) do
    if w[2] >= MB and w[2] < MB + 0x1000 then n = n + 1 end
  end
  return n
end

local cases = {
  {name = "1. contract symbol does not resolve", opts = {cv = nil},
   needle = "g_mailboxContract"},
  {name = "2. wrong magic (stale address)",      opts = {cv = CV, magic = 12345, cur = 3, min = 1},
   needle = "wrong memory"},
  {name = "3. DLL older than the script",        opts = {cv = CV, magic = MAGIC, cur = 2, min = 1},
   needle = "older than this script"},
  {name = "4. script older than the DLL",        opts = {cv = CV, magic = MAGIC, cur = 9, min = 5},
   needle = "too old for the DLL"},
}

print("-- the four refusals: each must untick and write NOTHING --")
for _, c in ipairs(cases) do
  local writes, msgs, env = runWith(c.opts)
  if writes then
    local all = table.concat(msgs, " | ")
    check(c.name .. " -- unticks the record", env.memrec.Active == false, tostring(env.memrec.Active))
    check(c.name .. " -- explains why (" .. c.needle .. ")",
          all:find(c.needle, 1, true) ~= nil, all:sub(1, 120))
    check(c.name .. " -- NO mailbox write happened", mailboxWrites(writes) == 0,
          tostring(mailboxWrites(writes)) .. " write(s)")
  end
end

print("\n-- POSITIVE control: a VALID contract must proceed and DO write --")
do
  local writes, msgs, env = runWith({cv = CV, magic = MAGIC, cur = 3, min = 1})
  check("record stays ticked", env.memrec.Active == true, tostring(env.memrec.Active))
  check("no refusal message", #msgs == 0, table.concat(msgs, " | "):sub(1, 120))
  check("the mailbox IS written (otherwise 'no write' above is vacuous)",
        mailboxWrites(writes) > 0, tostring(mailboxWrites(writes)))
end

print(string.format("\n%d checks, %d failure(s)", checks, fails))
os.exit(fails == 0 and 0 or 1)
