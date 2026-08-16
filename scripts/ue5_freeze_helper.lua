--[[
  ue5_freeze_helper.lua
  UE5CEDumper -- Property Freeze Helper (class-wide horizontal freeze)

  Companion to ue5_invoke_helper.lua. Generated AA Scripts produced by
  UE5DumpUI's "Copy Freeze Script" button (PropertySearch row) depend
  on THIS file being embedded in your CE table.

  What this helper does, that a plain CE pointer-list freeze does NOT:

    * Plain CE freeze locks ONE address (one instance, one offset).
      If the game frees that instance (respawn, level reload, death),
      the address becomes a dangling pointer and the freeze silently
      breaks.

    * This helper locks a PROPERTY (offset + type) on ALL live
      instances of a given UE class. It re-enumerates instances every
      few seconds via the DLL mailbox (CMD_LIST_INSTANCES), so newly
      spawned teammates / pickups / NPCs are picked up automatically
      and freed instances drop off.

  Setup once per .CT:
    Option A (one-click, requires AOBMaker CE Plugin):
      UE5DumpUI -> Tools -> Inject Freeze Helper into Current CE Table
    Option B (manual):
      UE5DumpUI -> Tools -> Export Freeze Helper Lua File...
      then in Cheat Engine: Table -> Add File... -> select this file
    Save the .CT to bake the file into the table.

  Public API (re-declaration-safe, syntax-highlighted):
    handle = freezeProperty(cfg)
    ok, err, n = handle.start()   -- begin tick + rescan timers, and REPORT:
                                  --   false, err, 0  hard failure (no DLL /
                                  --                  contract mismatch) -- nothing
                                  --                  is frozen; tell the user
                                  --   true,  nil, 0  armed, no live instances YET
                                  --                  (normal -- spawns get picked
                                  --                  up on the next rescan)
                                  --   true,  nil, n  frozen on n instances
    handle.stop()        -- cancel both timers cleanly

  cfg fields:
    className          (req) string  exact UE class name (e.g. 'BP_Teammate_C')
                                     -- exact match, case-insensitive
    propOffset         (req) number  byte offset of property within instance
    valueType          (req) string  see TYPE_WRITERS table below
    value              (req) number  target value to write each tick
                                     -- for 'bool' accepts true/false or 0/1
    boolMask           opt   number  'bool' only: FBoolProperty FieldMask, one
                                     of 0x01/0x02/.../0x80. Set it for a PACKED
                                     bitfield bool (`uint8 bFoo:1`, up to 8 per
                                     byte) and only that bit is written. OMIT it
                                     for a native bool -- it owns its whole byte.
                                     UE5DumpUI fills this in automatically; the
                                     DLL reports the mask only for packed bools.
    tickIntervalMs     opt   number  default 50  -- 20 writes/sec per instance
    refreshIntervalSec opt   number  default 5   -- rescan instances every 5s
    filter             opt   fn      function(addr) -> bool
                                     -- return true to include, false to skip

  Constants exposed:
    UE5_FREEZE_HELPER_VERSION = '1.2'   -- 1.1 added cfg.boolMask (packed bitfield bools)
                                        -- 1.2 start() returns (ok, err, count)

  =========================================================================
  SAMPLES -- copy/paste into your AA Script's [ENABLE] block, modify in place.
  =========================================================================

  ----- SAMPLE 1: Basic teammate HP freeze -----
    local h = freezeProperty({
      className  = 'BP_Teammate_C',
      propOffset = 0x4F8,
      valueType  = 'float',
      value      = 100.0,
    })
    h.start()

  ----- SAMPLE 2: God mode (bool) -----
    local h = freezeProperty({
      className  = 'PlayerCharacter',
      propOffset = 0x328,
      valueType  = 'bool',
      value      = false,    -- e.g. bCanBeDamaged = false  (or 0)
    })
    h.start()

  ----- SAMPLE 2b: PACKED bitfield bool (shares a byte with siblings) -----
    -- UE packs `uint8 bFoo:1` bools eight to a byte. Pass the FieldMask and
    -- only that bit is touched; omit it and the other seven get wiped.
    local h = freezeProperty({
      className  = 'PlayerCharacter',
      propOffset = 0x328,
      valueType  = 'bool',
      value      = true,
      boolMask   = 0x04,     -- this bool owns bit 2 of the byte at 0x328
    })
    h.start()

  ----- SAMPLE 3: Filter -- only freeze teammates, NOT the local player -----
    -- localPawn is whatever pointer identifies "me". You'd discover
    -- this either by reading a known LocalPlayer chain or by capturing
    -- the address from CE before starting the freeze. Adjust the
    -- filter body to your game.
    local localPawn = 0x12345678  -- resolved elsewhere
    local h = freezeProperty({
      className  = 'BP_Teammate_C',
      propOffset = 0x4F8,
      valueType  = 'float',
      value      = 9999.0,
      filter     = function(addr) return addr ~= localPawn end,
    })
    h.start()

  ----- SAMPLE 4: Multi-property freeze in one script (HP + MP) -----
    local hp = freezeProperty({
      className='BP_Teammate_C', propOffset=0x4F8, valueType='float', value=100,
    })
    local mp = freezeProperty({
      className='BP_Teammate_C', propOffset=0x4FC, valueType='float', value=50,
    })
    hp.start(); mp.start()
    -- In [DISABLE]: hp.stop(); mp.stop()

  ----- SAMPLE 5: Editing className / offset / value after generation -----
    -- Generated AA Scripts contain a `local CFG = { ... }` block near
    -- the top. Edit any field there and reactivate the script -- the
    -- new cfg is read fresh on every [ENABLE]. Use UE5DumpUI's
    -- PropertySearch panel to discover a new offset, copy the value
    -- into CFG, done.
]]

-- 1.1: cfg.boolMask -- packed bitfield bools are written bit-wise instead of
-- stamping the whole byte. A 1.0 helper still loads every generated script
-- (it just ignores boolMask and keeps the old whole-byte behaviour), and a
-- 1.1 helper runs every 1.0 script unchanged -- so this is informational,
-- not a compatibility gate.
-- 1.2: handle.start() returns (ok, err, count) instead of nothing, so a caller can
-- tell a HARD failure (no DLL / contract mismatch) from a freeze that is armed with
-- no live instances yet. Generated scripts read it; a 1.1 helper returns nil there
-- and the generated script treats that as "cannot report" rather than as a verdict,
-- so an old helper still runs a new script -- it just cannot diagnose it. (AA12/AA13)
if not UE5_FREEZE_HELPER_VERSION then
  UE5_FREEZE_HELPER_VERSION = '1.2'
end

-- ============================================================
-- Mailbox layout (MUST match dll/src/Mimic.h MailboxData)
-- Duplicated from ue5_invoke_helper.lua so this helper is
-- loadable on its own (no cross-helper dependency at load time).
-- ============================================================

local OFF_CMD        = 0x000
local OFF_STATUS     = 0x004
local OFF_RESULT     = 0x008
local OFF_INSTANCE   = 0x010  -- LIST_INSTANCES output (contract 2): UClass* witness
local OFF_UFUNC      = 0x018  -- LIST_INSTANCES output (contract 2): ClassPrivate offset
local OFF_PARMS_SZ   = 0x020  -- uint16: total count (LIST_INSTANCES output)
local OFF_NUM_PARMS  = 0x022  -- uint16: returned this page
local OFF_FUNC_FLAGS = 0x024  -- uint32: total pages
local OFF_CLASS      = 0x028
local OFF_ERR        = 0x228
local OFF_PARAMS     = 0x328

local CMD_LIST_INSTANCES = 6
local STATUS_DONE        = 1
local STATUS_IDLE        = 0    -- untouched: the DLL never picked the command up

-- Mailbox round-trip is bounded: GObjects walk + memcpy of <=1024 bytes.
-- 5 s is generous for a 2000-instance cap.
local DEFAULT_TIMEOUT_MS = 5000

-- Shared reentrancy flag with ue5_invoke_helper.lua. Whichever helper
-- loads first initialises it; subsequent loads see it already defined
-- and keep its current value (preserving any in-flight call state).
if _ue5_invoke_busy == nil then
  _ue5_invoke_busy = false
end

-- ============================================================
-- Type writers
-- ============================================================
-- valueType -> function(addr, value). Aliases (byte, dword, etc.) are
-- normalised through TYPE_ALIASES before lookup. v1 supports numeric +
-- bool only; FString / FName / struct fields are out of scope.

-- Packed bitfield bools ARE supported (since the AA1 fix). UE stores a
-- bool as either:
--   * a native bool -- one whole byte, 0 or 1; or
--   * a packed bitfield (`uint8 bFoo:1`) -- up to 8 bools sharing one byte,
--     each owning a single bit named by the FProperty's FieldMask.
-- The generated CFG carries `boolMask` for the second kind ONLY, so the
-- absence of a mask is itself the signal that the whole byte is ours. The
-- DLL only reports a mask after reading FieldSize == 1, so the bit is always
-- inside the byte at propOffset -- there is no ByteOffset to apply here.
--
-- Before this, EVERY bool freeze wrote a whole byte. On a packed bool that
-- clobbered up to 7 sibling bools ~16x/sec, and whenever the mask was not
-- 0x01 it also never set the intended bool (writing 1 sets bit 0), so the
-- freeze silently did nothing while corrupting its neighbours. (audit #5 AA1)
-- The eight legal single-bit FieldMask values. An explicit set rather than a
-- power-of-two test because the domain IS these eight, and it excludes both
-- values that must never be treated as a bit mask: 0 (no mask reported) and
-- 0xFF (UE's own native-bool marker -- SetBoolSize writes FieldMask = 255 when
-- bIsNativeBool). Both of those mean "the whole byte is ours".
local BOOL_BIT_MASKS = {
  [1] = true, [2] = true, [4] = true, [8] = true,
  [16] = true, [32] = true, [64] = true, [128] = true,
}

local function isPackedBoolMask(mask)
  return type(mask) == 'number' and BOOL_BIT_MASKS[mask] == true
end

local function writeBool(addr, v, mask)
  local on = (v == true or v == 1)
  if isPackedBoolMask(mask) then
    -- Read-modify-write the single bit, leaving the siblings untouched.
    -- Same rule as the DLL's Solitar::ApplyBoolBit and the UI's
    -- FieldValueConverter.ApplyBoolMask -- three tiers, one rule.
    --
    -- Pure arithmetic, no bitwise operators: CE's Lua has no bAnd/bOr/bNot
    -- and this mirrors StandaloneTrainerScriptGenerator's UE5T_setbit, which
    -- solved the same problem here first. Writing only on drift is a bonus
    -- the arithmetic gives for free.
    local b = readByte(addr)
    -- A failed read must NOT fall through to the whole-byte write below:
    -- that is exactly the corruption this branch exists to prevent. Skip
    -- this tick; the address is re-validated on the next rescan.
    if not b then return end
    local isSet = math.floor(b / mask) % 2
    if on and isSet == 0 then
      writeByte(addr, b + mask)
    elseif (not on) and isSet == 1 then
      writeByte(addr, b - mask)
    end
    return
  end
  -- Native bool (no mask reported): the whole byte belongs to this property.
  writeByte(addr, on and 1 or 0)
end

local TYPE_WRITERS = {
  bool    = writeBool,
  int8    = function(addr, v) writeByte(addr, math.floor(v) % 256) end,
  uint8   = function(addr, v) writeByte(addr, math.floor(v) % 256) end,
  int16   = function(addr, v) writeSmallInteger(addr, math.floor(v)) end,
  uint16  = function(addr, v) writeSmallInteger(addr, math.floor(v)) end,
  int32   = function(addr, v) writeInteger(addr, math.floor(v)) end,
  uint32  = function(addr, v) writeInteger(addr, math.floor(v)) end,
  int64   = function(addr, v) writeQword(addr, v) end,
  uint64  = function(addr, v) writeQword(addr, v) end,
  float   = function(addr, v) writeFloat(addr, v) end,
  double  = function(addr, v) writeDouble(addr, v) end,
}

local TYPE_ALIASES = {
  byte        = 'uint8',
  sbyte       = 'int8',
  word        = 'int16',
  dword       = 'int32',
  qword       = 'uint64',
  int         = 'int32',
  long        = 'int64',
  boolean     = 'bool',
}

local function resolveWriter(valueType)
  if type(valueType) ~= 'string' then
    return nil, '[ue5_freeze] valueType must be a string'
  end
  local t = valueType:lower()
  t = TYPE_ALIASES[t] or t
  local w = TYPE_WRITERS[t]
  if not w then
    return nil, string.format(
      "[ue5_freeze] unsupported valueType '%s' -- supported: " ..
      'bool, int8/uint8(byte), int16/uint16(word), ' ..
      'int32/uint32(dword), int64/uint64(qword), float, double',
      valueType)
  end
  return w, nil
end

-- ============================================================
-- Internal: mailbox helpers
-- ============================================================

-- ============================================================
-- CE Lua <-> DLL contract check
-- ============================================================
-- Versioned on the CONTRACT (mailbox offsets, Cmd values, per-command ops,
-- status/result meanings), NOT on the build number: a .CT saved months ago stays
-- valid against a newer DLL as long as nothing it depends on moved. The DLL
-- publishes a RANGE so the two failure directions can be told apart -- too-old
-- script means regenerate, too-old DLL means update the DLL. See dll/src/Mimic.h.
-- 2: CMD_LIST_INSTANCES publishes the (UClass*, ClassPrivate offset) witness this
-- helper needs to refuse a write into a recycled slot (audit #5 AA2/AA3). Required,
-- not optional: without it the freeze tick has no way to tell a live instance from a
-- reused address, and degrading to that silently is the defect, not the fallback.
local UE5_SCRIPT_CONTRACT = 2

-- Returns true, or false + a message. Call BEFORE writing to the mailbox: if the
-- layout moved, writing first scribbles on whatever now lives at those offsets.
local function checkContract()
  local cv = getAddressSafe('g_mailboxContract')
  if not cv or cv == 0 then cv = getAddressSafe('UE5Dumper.g_mailboxContract') end
  if not cv or cv == 0 then
    return false, 'this UE5Dumper.dll is older than this script (no contract symbol) -- update the DLL'
  end
  if readInteger(cv + 0x00) ~= 1127564629 then
    return false, 'the contract symbol resolved to the wrong memory (stale address) -- re-inject the DLL'
  end
  local cur, min = readInteger(cv + 0x04), readInteger(cv + 0x08)
  if UE5_SCRIPT_CONTRACT < min then
    return false, string.format(
      'this script is too old for the DLL (script %d, DLL needs %d+) -- regenerate the table',
      UE5_SCRIPT_CONTRACT, min)
  end
  if UE5_SCRIPT_CONTRACT > cur then
    return false, string.format(
      'the DLL is older than this script (script %d, DLL speaks %d) -- update UE5Dumper.dll',
      UE5_SCRIPT_CONTRACT, cur)
  end
  return true
end

local function findMailbox()
  -- getAddressSafe (not getAddress) -- returns nil on missing symbol
  -- instead of raising. Either name is valid depending on whether the
  -- DLL exports its symbols module-qualified.
  local mb = getAddressSafe('g_invokeMailbox')
  if not mb or mb == 0 then
    mb = getAddressSafe('UE5Dumper.g_invokeMailbox')
  end
  if not mb or mb == 0 then
    return nil,
      '[ue5_freeze] g_invokeMailbox symbol not found -- is ' ..
      'UE5Dumper.dll injected?'
  end
  -- Validated HERE because every path reaches the mailbox through this function,
  -- and it has to happen before the caller writes anything: if the layout moved,
  -- a write lands on whatever now occupies those offsets. Reported through this
  -- function's own (nil, message) contract rather than raising.
  local ok, why = checkContract()
  if not ok then return nil, '[ue5_freeze] ' .. why end
  return mb, nil
end

local function writeMbStr(mb, off, str)
  local b = {}
  local len = math.min(#str, 255)
  for i = 1, len do b[#b + 1] = string.byte(str, i) end
  b[#b + 1] = 0  -- null terminator
  writeBytes(mb + off, b)
end

-- Wait for the mailbox round-trip to finish.
--
-- The limit is REAL milliseconds. It used to count sleep(1) calls and still print the
-- result as "%dms": sleep(1) measures 15.47 ms in CE -- the ~64 Hz Windows scheduler
-- tick, identical to three decimals on two very different CPUs -- so a 5000 "ms" limit
-- was really ~77 seconds, reported as 5000ms. getTickCount() was probed in CE's Lua
-- Engine on 2026-08-06 (present, returns ms); the iteration count survives only as a
-- fallback for a build without it, at ~15 ms per iteration.
--
-- The timeout message names WHICH fault, because the status already distinguishes them:
-- STATUS_IDLE means the DLL never picked the command up (a stale g_invokeMailbox address
-- is the usual cause), anything else means it took the command and did not finish.
local function waitDone(mb, timeoutMs)
  local limit = timeoutMs or DEFAULT_TIMEOUT_MS
  local tick  = (type(getTickCount) == 'function') and getTickCount or nil
  -- Keep CE's window alive: its Lua sleep is a bare Sleep and pumps nothing. Prefer
  -- processMessagesPaintOnly -- CE's own docs call processMessages "not recommended"
  -- and paint-only ignores mouse/keyboard, so it cannot re-enter us. Feature-tested
  -- rather than version-gated: it is absent from the 7.5 source and present in the
  -- 7.7 binary, so the introducing version is unknown, and an undefined global is
  -- nil in Lua rather than an error.
  local pump  = (type(processMessagesPaintOnly) == 'function')
                and processMessagesPaintOnly or processMessages
  local t0, iters = tick and tick() or 0, 0
  local st = readInteger(mb + OFF_STATUS)
  while st ~= STATUS_DONE do
    -- processMessages keeps CE's window alive while we block. CE's Lua sleep is a
    -- bare Win32 Sleep and does NOT pump messages, so without this the whole
    -- timeout is a frozen Cheat Engine.
    sleep(1); pump()
    iters = iters + 1
    st = readInteger(mb + OFF_STATUS)
    -- nil is not a status. readInteger returns nil once the process is gone and
    -- `nil ~= STATUS_DONE` is true, so without this the loop burns the whole
    -- deadline and then matches none of the branches below (status=nil).
    local over = st == nil
               or (tick and (tick() - t0 >= limit) or (iters >= math.floor(limit / 15)))
    if st ~= STATUS_DONE and over then
      if st == nil then
        return false, 'the mailbox could not be read -- the game process has ' ..
          'most likely exited (if it is running, re-inject UE5Dumper.dll)'
      end
      if st == STATUS_IDLE then
        return false, string.format(
          'mailbox timeout after %dms -- the DLL never picked this up ' ..
          '(stale g_invokeMailbox address? re-inject, or re-enable the table)', limit)
      end
      return false, string.format(
        'mailbox timeout after %dms -- the DLL took the command but did not ' ..
        'finish it (status=%d)', limit, st)
    end
  end
  return true
end

-- Pull one page of instance pointers via CMD_LIST_INSTANCES.
-- Returns: addrsArray (or nil), totalPages, errMsg (nil on success), classPtr, classOff
--
-- classPtr/classOff are the contract-2 identity witness (see checkContract): the
-- UClass* every entry on this page belongs to, and the byte offset of
-- UObject::ClassPrivate. tick() re-reads that field before every write so a slot
-- recycled by a DIFFERENT class is refused. Both are 0 when the DLL did not
-- publish them; the contract check above makes that unreachable, and the caller
-- still treats 0 as "no witness" rather than as a match.
local function fetchInstancePage(className, pageIndex)
  local mb, ferr = findMailbox()
  if not mb then return nil, 0, ferr end

  if _ue5_invoke_busy then
    -- Don't corrupt a concurrent invoke. Caller (rescan) treats this
    -- as "skip this cycle"; tick keeps writing the existing cache.
    return nil, 0, 'mailbox busy (concurrent invoke or rescan)'
  end

  _ue5_invoke_busy = true
  local pok, addrs, totalPages, err, classPtr, classOff = pcall(function()
    writeMbStr(mb, OFF_CLASS, className)
    -- Page index goes in paramsData[0..3].
    writeInteger(mb + OFF_PARAMS, pageIndex)
    -- Status cleared, THEN cmd written last as the trigger.
    writeInteger(mb + OFF_STATUS, 0)
    writeInteger(mb + OFF_CMD, CMD_LIST_INSTANCES)

    local wok, werr = waitDone(mb, DEFAULT_TIMEOUT_MS)
    if not wok then return nil, 0, werr end

    local result = readInteger(mb + OFF_RESULT, true)   -- signed: rc is int32
    if result ~= 0 then
      local em = readString(mb + OFF_ERR, 256) or ''
      return nil, 0, string.format(
        'CMD_LIST_INSTANCES result=%d (%s)', result, em)
    end

    local returned = readSmallInteger(mb + OFF_NUM_PARMS) or 0
    -- readSmallInteger returns signed; we packed an unsigned uint16.
    if returned < 0 then returned = returned + 65536 end
    local totalPagesLocal = readInteger(mb + OFF_FUNC_FLAGS) or 1

    local out = {}
    for i = 0, returned - 1 do
      local a = readQword(mb + OFF_PARAMS + (i * 8))
      if a and a ~= 0 then out[#out + 1] = a end
    end

    -- Contract-2 identity witness. Read AFTER result==0, because the DLL only
    -- fills these on a successful enumeration and clears them first, so a stale
    -- UObject*/UFunction* from an earlier command can never be mistaken for one.
    local cPtr = readQword(mb + OFF_INSTANCE) or 0
    local cOff = readQword(mb + OFF_UFUNC) or 0
    -- Plausibility gate, cheap and worth it: ClassPrivate sits a few bytes into
    -- UObject (0x10 today). Anything outside a small window is not an offset --
    -- most likely a leftover 64-bit address -- so drop the witness rather than
    -- compare against garbage, which would refuse EVERY write and make the
    -- freeze silently do nothing.
    if cOff < 8 or cOff > 0x200 or cPtr == 0 then
      cPtr, cOff = 0, 0
    end
    return out, totalPagesLocal, nil, cPtr, cOff
  end)
  _ue5_invoke_busy = false

  if not pok then
    -- Body raised; pcall captured the error in the first slot.
    return nil, 0, tostring(addrs)
  end
  return addrs, totalPages or 0, err, classPtr or 0, classOff or 0
end

-- Full rescan: page through CMD_LIST_INSTANCES until all instances
-- of the class are collected. Caps at 16 pages (16*128 = 2048
-- instances) to match the DLL's hard cap.
local function rescanInstances(className, filter)
  local all = {}
  local pageIndex = 0
  local maxPages = 16
  local firstErr = nil
  local classPtr, classOff = 0, 0

  while pageIndex < maxPages do
    local addrs, totalPages, err, cPtr, cOff = fetchInstancePage(className, pageIndex)
    if not addrs then
      if pageIndex == 0 then firstErr = err end
      break
    end
    for i = 1, #addrs do all[#all + 1] = addrs[i] end
    -- Every page reports the same class (the DLL enumerates one exact class), so
    -- the first non-zero witness is the witness. Taking the first rather than the
    -- last means a later empty page cannot erase it.
    if classPtr == 0 and cPtr and cPtr ~= 0 then
      classPtr, classOff = cPtr, cOff
    end
    pageIndex = pageIndex + 1
    if totalPages <= pageIndex then break end
  end

  if filter then
    local filtered = {}
    for i = 1, #all do
      if filter(all[i]) then filtered[#filtered + 1] = all[i] end
    end
    all = filtered
  end

  return all, firstErr, classPtr, classOff
end

-- ============================================================
-- Public API: freezeProperty
-- ============================================================

if not freezeProperty then

  --- Build a freeze handle for one (class, offset, type, value) tuple.
  ---
  --- @param cfg table  see header docs for fields
  --- @return table     handle with .start(), .stop(), and internals
  function freezeProperty(cfg)
    if type(cfg) ~= 'table' then
      error('[ue5_freeze] freezeProperty: cfg must be a table')
    end
    if type(cfg.className) ~= 'string' or #cfg.className == 0 then
      error('[ue5_freeze] cfg.className must be a non-empty string')
    end
    if type(cfg.propOffset) ~= 'number' then
      error('[ue5_freeze] cfg.propOffset must be a number')
    end
    if cfg.value == nil then
      error('[ue5_freeze] cfg.value must be provided')
    end

    local writer, werr = resolveWriter(cfg.valueType)
    if not writer then error(werr) end

    local handle = {
      cfg          = cfg,
      _writer      = writer,
      _cache       = {},
      _tickTimer   = nil,
      _rescanTimer = nil,
      _lastError   = nil,
      -- Identity witness from the last successful rescan (contract 2).
      _classPtr    = 0,
      _classOff    = 0,
      -- Consecutive failed rescans. Bounds how long a stale cache can be
      -- written to when the mailbox stops answering (see rescan()).
      _failStreak  = 0,
      _abandoned   = false,
    }

    -- How many consecutive failed rescans before the cache is dropped.
    -- At the default 5 s rescan interval that is ~15 s of writing to addresses
    -- nothing has re-confirmed. A transient 'mailbox busy' clears on the next
    -- cycle and never gets near it; a DLL that was unloaded, re-injected, or
    -- version-mismatched never recovers, and that is the case this bounds.
    local MAX_FAIL_STREAK = 3

    local function tick()
      local offset = handle.cfg.propOffset
      local value  = handle.cfg.value
      local w      = handle._writer
      local cache  = handle._cache
      -- Only writeBool reads a third argument; every other writer ignores it.
      -- nil here means "native bool / not a bool" -> whole-byte write.
      local mask   = handle.cfg.boolMask
      local cPtr   = handle._classPtr
      local cOff   = handle._classOff
      for i = 1, #cache do
        local addr = cache[i]
        -- Identity guard (audit #5 AA2). A cached pointer is NOT proof the
        -- object is still there: UE frees instances on respawn / level change
        -- and the allocator hands the same address to something else, so
        -- between two rescans this list can point at objects we never
        -- enumerated. Re-read ClassPrivate and refuse anything that is no
        -- longer the class being frozen -- that is the write that corrupts,
        -- because propOffset means something entirely different over there.
        --
        -- A slot reused by ANOTHER INSTANCE OF THE SAME CLASS is deliberately
        -- allowed through: this freeze is class-wide by design, so that object
        -- is a target too and the next rescan would list it anyway.
        --
        -- The old guard was `readQword(addr) ~= 0` -- the vtable slot. It
        -- almost never fired: a freed block keeps old bytes or an allocator
        -- free-list link in qword 0, both non-zero, so in practice it caught
        -- only fully decommitted pages, i.e. the game exiting.
        local ok
        if cPtr ~= 0 then
          ok = readQword(addr + cOff) == cPtr
        else
          -- No witness (contract check makes this unreachable today; kept so
          -- the loop has a defined answer rather than writing unconditionally).
          local vt = readQword(addr)
          ok = vt ~= nil and vt ~= 0
        end
        if ok then
          w(addr + offset, value, mask)
        end
      end
    end

    -- Returns ok, err, count -- an OUTCOME, not nothing.
    --
    -- Three results, and they are NOT two (audit #5 AA12/AA13):
    --   false, err, 0   a HARD failure: no DLL, contract mismatch, stale mailbox.
    --                   Nothing is frozen and nothing will be until it is fixed.
    --   true,  nil, 0   ARMED, nothing alive yet. A valid class with no live
    --                   instances is the helper's advertised purpose (header
    --                   :16-20 -- newly spawned NPCs get picked up), so this must
    --                   never be reported as a failure or the feature IS the bug.
    --   true,  nil, n   frozen on n instances.
    -- Before this, start() returned nothing at all, so the generated script's only
    -- signal was `pcall(start)` -- which answers "did Lua raise", and no mailbox
    -- error can raise (they are all caught in fetchInstancePage's own pcall). Every
    -- one of the three came out as success, over a ticked record and a Lua window
    -- the generator then auto-closed.
    --
    -- The DLL cannot distinguish a MISSPELLED class from a live-but-empty one --
    -- Mimic.cpp's HandleListInstances answers SetDone(0) for both -- so neither can
    -- this. "Armed, 0 right now" is the honest report; claiming a typo would be a
    -- guess, which is the thing CLAUDE.md's mailbox rule forbids.
    local function rescan()
      local addrs, err, cPtr, cOff =
        rescanInstances(handle.cfg.className, handle.cfg.filter)
      if err then
        handle._lastError = err
        handle._failStreak = handle._failStreak + 1
        -- One failure is usually a transient 'mailbox busy' (a concurrent
        -- invoke); keeping the cache is right there. A PERSISTENT failure is
        -- not transient and never self-heals -- DLL unloaded or re-injected so
        -- g_invokeMailbox no longer resolves, a contract mismatch after a DLL
        -- update, a wedged _ue5_invoke_busy. Before this, the cache was kept
        -- through all of them and tick wrote into it forever (audit #5 AA3).
        if handle._failStreak >= MAX_FAIL_STREAK and not handle._abandoned then
          handle._abandoned = true
          handle._cache = {}
          -- Ungated on purpose: this is a real failure, and CE Lua hygiene
          -- keeps genuine failures unconditional. It is printed ONCE per
          -- abandonment, not per rescan.
          print(string.format(
            '[ue5_freeze] %s: %d consecutive rescans failed -- freeze STOPPED ' ..
            'writing (last error: %s). Re-enable the record after fixing it.',
            tostring(handle.cfg.className), handle._failStreak, tostring(err)))
        end
        return false, err, 0
      else
        handle._cache = addrs
        handle._lastError = nil
        handle._failStreak = 0
        handle._abandoned = false
        -- Refresh the witness from the same enumeration that produced the
        -- cache. A rescan that returned instances but no witness would leave a
        -- stale class pointer paired with fresh addresses, so they move together.
        handle._classPtr = cPtr or 0
        handle._classOff = cOff or 0
        return true, nil, #addrs
      end
    end

    --- Last rescan error, or nil. `_lastError` had three writers and zero
    --- readers, so no failure ever reached anyone (audit #5 AA3).
    handle.lastError = function() return handle._lastError end

    --- True once consecutive rescan failures made the handle stop writing.
    handle.isAbandoned = function() return handle._abandoned end

    --- Begin the tick + rescan timers. Returns rescan()'s (ok, err, count) so the
    --- caller can tell a hard failure from an armed-but-empty freeze -- see rescan.
    ---
    --- The timers are started in ALL THREE cases, deliberately. A hard failure is
    --- still owned by the failure-streak logic, and a caller that wants to abandon
    --- calls handle.stop(). start() must not RAISE instead: the generated script
    --- stores the handle before calling start, and its failure branch nils that slot
    --- WITHOUT stopping -- so a raise thrown after the timers exist would strand two
    --- of them writing into the game with no reachable handle. Reporting by value is
    --- what keeps the cleanup path available.
    handle.start = function()
      -- Initial scan happens synchronously so tick has data on the
      -- very first fire.
      local ok, err, count = rescan()

      local tickMs   = handle.cfg.tickIntervalMs or 50
      local rescanMs = (handle.cfg.refreshIntervalSec or 5) * 1000

      handle._tickTimer = createTimer(getMainForm(), false)
      handle._tickTimer.Interval = tickMs
      handle._tickTimer.OnTimer  = tick
      handle._tickTimer.Enabled  = true

      handle._rescanTimer = createTimer(getMainForm(), false)
      handle._rescanTimer.Interval = rescanMs
      handle._rescanTimer.OnTimer  = rescan
      handle._rescanTimer.Enabled  = true

      return ok, err, count
    end

    handle.stop = function()
      if handle._tickTimer then
        handle._tickTimer.Enabled = false
        handle._tickTimer.destroy()
        handle._tickTimer = nil
      end
      if handle._rescanTimer then
        handle._rescanTimer.Enabled = false
        handle._rescanTimer.destroy()
        handle._rescanTimer = nil
      end
      handle._cache = {}
    end

    return handle
  end

  registerLuaFunctionHighlight('freezeProperty')
end
