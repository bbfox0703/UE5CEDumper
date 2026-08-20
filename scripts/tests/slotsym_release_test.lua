--[[
  slotsym_release_test.lua
  UE5CEDumper -- executable test for `[SLOTSYM]`: the slot [DISABLE] must really
  unregister, and must only CLAIM success when the symbol is actually gone.

  WHAT IT RUNS
    Not a fixture and not a re-implementation: it executes the **real script the
    shipping UI emitted**, captured from the Teleport panel's
    "Global Pointers -> Get GameEngine" clipboard fallback into
        out/slotsym/get_gameengine.lua.txt
    (working-lessons 2.5 -- running the emitted text beats asserting about it).
    Cheat Engine's globals are stubbed over plain Lua tables, so the ENABLE path
    takes the SLOT branch (op 2 succeeds) exactly as it does on DumperTest.

  THE THREE CASES
    1  enable -> disable            the symbol must be GONE after one disable,
                                    with no manual unregisterSymbol. This is the
                                    defect: the old code skipped both arms when
                                    there was no buffer, left the symbol alive,
                                    and printed "unregistered" anyway.
    2  enable, enable -> disable    a second still-ticked record must KEEP the
                                    symbol (refcount), and say so.
       ...then disable again        now the last holder releases it.
    3  unregisterSymbol NEUTERED    the honesty half. With unregister made a
                                    no-op, the script must report "could NOT be
                                    unregistered" and must NOT claim success --
                                    it re-reads getAddressSafe AFTER the attempt.

  RUNNING IT
      lua scripts/tests/slotsym_release_test.lua
    Exit 0 = all pass, 1 = a failure. Manual tool, like its siblings: a standalone
    `lua` is not a declared dependency, so this is run on purpose rather than
    skipped quietly in CI.
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
  print("FAIL: " .. SRC .. " not found.")
  print("      Capture it first: UI -> Teleport -> Global Pointers -> Get GameEngine")
  print("      (with AOBMaker offline it copies the CE XML), then extract <AssemblerScript>.")
  os.exit(1)
end
local raw = fh:read("a"); fh:close()

-- split the two {$lua} blocks out of the AA script
-- CRLF-tolerant: the capture is written out by a Windows tool, so do not pin \n.
raw = raw:gsub("\r\n", "\n")
local enableSrc = raw:match("%[ENABLE%]%s*{%$lua}%s*(.-)%s*{%$asm}")
local disableSrc = raw:match("%[DISABLE%]%s*{%$lua}%s*(.-)%s*{%$asm}")
if not enableSrc or not disableSrc then
  print("FAIL: could not split [ENABLE]/[DISABLE] {$lua} blocks out of the emitted script")
  os.exit(1)
end
print(string.format("loaded the UI-emitted script: ENABLE %d chars, DISABLE %d chars",
                    #enableSrc, #disableSrc))

-- ============================================================
-- Cheat Engine stubs
-- ============================================================
local CE = {}
local MB, CV = 0x10000, 0x20000            -- fake mailbox / contract addresses
local SLOT_ADDR = 0x7FF600001234           -- what op 2 returns

local function newEnv(opts)
  opts = opts or {}
  local syms, mem, allocNext = {}, {}, 0x50000
  local logged = {}
  local env = setmetatable({}, {__index = _G})
  env.syntaxcheck = false
  env.memrec = {Active = true}
  env.UE5_DEBUG = 1                         -- keep dbg() output so we can read it
  env.print = function(...)
    local parts = {}
    for i = 1, select('#', ...) do parts[#parts+1] = tostring((select(i, ...))) end
    logged[#logged+1] = table.concat(parts, "\t")
  end
  env.showMessage = function(m) logged[#logged+1] = "SHOWMESSAGE: " .. tostring(m) end
  env.getAddressSafe = function(n) return syms[n] end
  env.registerSymbol = function(n, a) syms[n] = a end
  env.unregisterSymbol = function(n)
    if opts.neuterUnregister then return end   -- case 3
    syms[n] = nil
  end
  env.allocateMemory = function(n) local a = allocNext; allocNext = allocNext + n; return a end
  env.deAlloc = function() end
  env.reinitializeSymbolhandler = function() end
  env.sleep = function() end
  env.processMessages = function() end
  env.processMessagesPaintOnly = function() end
  env.getTickCount = function() return 0 end
  env.synchronize = function(f) end
  env.getLuaEngine = function() return {Close = function() end} end
  env.readInteger = function(a)
    if a == CV + 0x00 then return 1127564629 end        -- MAILBOX_CONTRACT_MAGIC
    if a == CV + 0x04 then return 3 end                 -- current
    if a == CV + 0x08 then return 1 end                 -- minimum
    if a == MB + 0x00 then return 0 end                 -- cmd: idle
    if a == MB + 0x04 then return 1 end                 -- status: DONE
    if a == MB + 0x08 then return 0 end                 -- result code: ok
    return mem[a] or 0
  end
  env.readQword = function(a)
    if a == MB + 0x328 then return SLOT_ADDR end        -- op 2 -> the &GEngine slot
    return mem[a] or 0
  end
  env.writeInteger = function(a, v) mem[a] = v end
  env.writeQword = function(a, v) mem[a] = v end
  -- the script resolves these two by name
  syms['g_invokeMailbox'] = MB
  syms['g_mailboxContract'] = CV
  return env, syms, logged
end

local function run(src, env, label)
  local fn, err = load(src, label, "t", env)
  if not fn then print("  FAIL  could not compile " .. label .. ": " .. tostring(err)); fails = fails + 1; return end
  local ok, e = pcall(fn)
  if not ok then print("  FAIL  " .. label .. " raised: " .. tostring(e)); fails = fails + 1 end
end

local function joined(logged) return table.concat(logged, "\n") end

-- ============================================================
print("\n-- case 1: enable -> disable, the symbol must be GONE --")
do
  local env, syms, logged = newEnv()
  run(enableSrc, env, "ENABLE")
  check("ENABLE took the SLOT path (registered the slot address, no buffer)",
        syms['UE_GameEngine'] == SLOT_ADDR and syms['UE_GameEngine_buf'] == nil,
        string.format("sym=%s buf=%s", tostring(syms['UE_GameEngine']), tostring(syms['UE_GameEngine_buf'])))
  check("refcount incremented to 1", (env.UE5_slotSymRefcount or {})['UE_GameEngine'] == 1,
        tostring((env.UE5_slotSymRefcount or {})['UE_GameEngine']))
  run(disableSrc, env, "DISABLE")
  check("symbol is GONE after ONE disable (the defect: it survived)",
        syms['UE_GameEngine'] == nil, tostring(syms['UE_GameEngine']))
  check("and it reported success", joined(logged):find("UE_GameEngine unregistered", 1, true) ~= nil,
        joined(logged))
end

print("\n-- case 2: two ticked records -- the first disable must NOT release --")
do
  local env, syms, logged = newEnv()
  run(enableSrc, env, "ENABLE#1")
  run(enableSrc, env, "ENABLE#2")
  check("refcount reached 2", (env.UE5_slotSymRefcount or {})['UE_GameEngine'] == 2,
        tostring((env.UE5_slotSymRefcount or {})['UE_GameEngine']))
  run(disableSrc, env, "DISABLE#1")
  check("symbol SURVIVES while another record holds it",
        syms['UE_GameEngine'] == SLOT_ADDR, tostring(syms['UE_GameEngine']))
  check("and it says so rather than claiming success",
        joined(logged):find("still held by 1 other record", 1, true) ~= nil, joined(logged))
  run(disableSrc, env, "DISABLE#2")
  check("last holder releases it", syms['UE_GameEngine'] == nil, tostring(syms['UE_GameEngine']))
end

print("\n-- case 3 (HONESTY): unregister neutered -- it must NOT claim success --")
do
  local env, syms, logged = newEnv({neuterUnregister = true})
  run(enableSrc, env, "ENABLE")
  run(disableSrc, env, "DISABLE")
  local out = joined(logged)
  check("symbol still resolves (the stub refused to remove it)",
        syms['UE_GameEngine'] == SLOT_ADDR, tostring(syms['UE_GameEngine']))
  check("reports 'could NOT be unregistered'", out:find("could NOT be unregistered", 1, true) ~= nil, out)
  check("does NOT falsely claim 'UE_GameEngine unregistered'",
        out:find("UE_GameEngine unregistered", 1, true) == nil, out)
  check("the retry loop is BOUNDED (reported 8 attempts, did not spin)",
        out:find("after 8 attempt", 1, true) ~= nil, out)
end

print(string.format("\n%d checks, %d failure(s)", checks, fails))
os.exit(fails == 0 and 0 or 1)
