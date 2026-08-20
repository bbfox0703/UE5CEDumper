--[[
  slotsym_gworld_test.lua
  UE5CEDumper -- `[SLOTSYM]` step 3, THE NON-REGRESSION: the GWorld record must
  still release its symbol.

  WHY A SEPARATE FILE FROM slotsym_release_test.lua
    That rig covers the record the defect was IN -- "Get GameEngine" on the
    `&GEngine` SLOT path, where `unregisterSymbol` sat inside a buffer-only guard
    and both arms were skipped. GWorld always unregistered correctly, so step 3
    asks the opposite question: did fixing the broken one break the working one?
    Both ends now go through the SAME shared emitters
    (`CeLuaHygiene.AppendSlotSymbolRegister` / `AppendSlotSymbolRelease`), which is
    exactly why the working end has to be re-checked -- a shared emitter turns one
    regression into two.

  WHAT IT RUNS
    The real script the shipping UI emitted today, captured from
    Teleport -> Global Pointers -> **Get GWorld** with AOBMaker offline (which
    copies the CE XML), `<AssemblerScript>` extracted to
    out/slotsym/get_gworld.lua.txt. Cheat Engine's globals are stubbed over plain
    Lua tables. working-lessons 2.5: run the emitted text, do not assert about it.

  THE THREE CASES  (mirroring the GameEngine rig, so a divergence is visible)
    1  enable -> disable            symbol GONE after ONE disable, and says so.
    2  enable, enable -> disable    a second still-ticked record KEEPS it;
                                    the second disable releases it.
    3  unregisterSymbol NEUTERED    must report "could NOT be unregistered",
                                    must NOT claim success, and the retry loop
                                    must be BOUNDED (8 attempts).

  RUNNING IT
      lua scripts/tests/slotsym_gworld_test.lua
]]

local SRC = (arg and arg[0] or ''):gsub('[^/\\]*$', '') .. '../../out/slotsym/get_gworld.lua.txt'
local SYM = 'UE_GWorld'

local fails, checks = 0, 0
local function check(name, cond, got)
  checks = checks + 1
  if cond then print(string.format("  ok    %s", name))
  else fails = fails + 1; print(string.format("  FAIL  %s   got: %s", name, tostring(got))) end
end

local fh = io.open(SRC, "rb")
if not fh then
  print("FAIL: " .. SRC .. " not found.")
  print("      Capture it: UI -> Teleport -> Global Pointers -> Get GWorld (AOBMaker offline")
  print("      copies the CE XML), then extract <AssemblerScript>.")
  os.exit(1)
end
local raw = fh:read("a"):gsub("\r\n", "\n"); fh:close()
local enableSrc  = raw:match("%[ENABLE%]%s*{%$lua}%s*(.-)%s*{%$asm}")
local disableSrc = raw:match("%[DISABLE%]%s*{%$lua}%s*(.-)%s*{%$asm}")
if not enableSrc or not disableSrc then
  print("FAIL: could not split [ENABLE]/[DISABLE] {$lua} blocks out of the emitted script")
  os.exit(1)
end
print(string.format("loaded the UI-emitted GWorld script: ENABLE %d chars, DISABLE %d chars",
                    #enableSrc, #disableSrc))

-- ============================================================
-- Cheat Engine stubs
-- ============================================================
local MB, CV = 0x10000, 0x20000            -- fake mailbox / contract addresses
local SLOT_ADDR = 0x7FF600009999           -- what the query returns for &GWorld

local function newEnv(opts)
  opts = opts or {}
  local syms, mem, allocNext = {}, {}, 0x50000
  local logged = {}
  local env = setmetatable({}, {__index = _G})
  env.syntaxcheck = false
  env.memrec = {Active = true}
  env.UE5_DEBUG = 1                         -- keep dbg() output readable
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
  env.synchronize = function() end
  env.getLuaEngine = function() return {Close = function() end} end
  env.readInteger = function(a)
    if a == CV + 0x00 then return 1127564629 end        -- MAILBOX_CONTRACT_MAGIC
    if a == CV + 0x04 then return 3 end                 -- current contract
    if a == CV + 0x08 then return 1 end                 -- minimum contract
    if a == MB + 0x00 then return 0 end                 -- cmd: idle
    if a == MB + 0x04 then return 1 end                 -- status: DONE
    if a == MB + 0x08 then return 0 end                 -- result: ok
    return mem[a] or 0
  end
  env.readQword = function(a)
    if a == MB + 0x328 then return SLOT_ADDR end        -- the &GWorld slot
    return mem[a] or 0
  end
  env.writeInteger = function(a, v) mem[a] = v end
  env.writeQword = function(a, v) mem[a] = v end
  env.writeByte = function(a, v) mem[a] = v end
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

local function joined(l) return table.concat(l, "\n") end

-- ============================================================
print("\n-- case 1: enable -> disable, the symbol must be GONE --")
do
  local env, syms, logged = newEnv()
  run(enableSrc, env, "ENABLE")
  check("ENABLE registered " .. SYM, syms[SYM] ~= nil, tostring(syms[SYM]))
  check("refcount incremented to 1", (env.UE5_slotSymRefcount or {})[SYM] == 1,
        tostring((env.UE5_slotSymRefcount or {})[SYM]))
  run(disableSrc, env, "DISABLE")
  check("symbol is GONE after ONE disable", syms[SYM] == nil, tostring(syms[SYM]))
  check("and it reported success", joined(logged):find(SYM .. " unregistered", 1, true) ~= nil,
        joined(logged))
end

print("\n-- case 2: two ticked records -- the first disable must NOT release --")
do
  local env, syms, logged = newEnv()
  run(enableSrc, env, "ENABLE#1")
  run(enableSrc, env, "ENABLE#2")
  check("refcount reached 2", (env.UE5_slotSymRefcount or {})[SYM] == 2,
        tostring((env.UE5_slotSymRefcount or {})[SYM]))
  run(disableSrc, env, "DISABLE#1")
  check("symbol SURVIVES while another record holds it", syms[SYM] ~= nil, tostring(syms[SYM]))
  check("and it says so rather than claiming success",
        joined(logged):find("still held by 1 other record", 1, true) ~= nil, joined(logged))
  run(disableSrc, env, "DISABLE#2")
  check("last holder releases it", syms[SYM] == nil, tostring(syms[SYM]))
end

print("\n-- case 3 (HONESTY): unregister neutered -- it must NOT claim success --")
do
  local env, syms, logged = newEnv({neuterUnregister = true})
  run(enableSrc, env, "ENABLE")
  run(disableSrc, env, "DISABLE")
  local out = joined(logged)
  check("symbol still resolves (the stub refused to remove it)", syms[SYM] ~= nil, tostring(syms[SYM]))
  check("reports 'could NOT be unregistered'", out:find("could NOT be unregistered", 1, true) ~= nil, out)
  check("does NOT falsely claim '" .. SYM .. " unregistered'",
        out:find(SYM .. " unregistered", 1, true) == nil, out)
  check("the retry loop is BOUNDED (reported 8 attempts, did not spin)",
        out:find("after 8 attempt", 1, true) ~= nil, out)
end

print(string.format("\n%d checks, %d failure(s)", checks, fails))
os.exit(fails == 0 and 0 or 1)
