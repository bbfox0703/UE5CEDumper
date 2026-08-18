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
      instances of a given UE class -- and, by default, of every
      SUBCLASS of it (cfg.derived). It re-enumerates instances every
      few seconds via the DLL mailbox (CMD_LIST_INSTANCES), so newly
      spawned teammates / pickups / NPCs are picked up automatically
      and freed instances drop off.

    * If it ever gives up (the DLL was unloaded or re-injected and the
      mailbox stops answering), it UNTICKS its own CE record instead of
      leaving a checkbox that claims a cheat nothing is applying --
      provided the record was handed to it as cfg.memrec.

  Setup once per .CT:
    Option A (one-click, requires AOBMaker CE Plugin):
      UE5DumpUI -> Tools -> Inject Freeze Helper into Current CE Table
    Option B (manual):
      UE5DumpUI -> Tools -> Export Freeze Helper Lua File...
      then in Cheat Engine: Table -> Add File... -> select this file
    Save the .CT to bake the file into the table.

  Public API (re-declaration-safe, syntax-highlighted):
    handle = freezeProperty(cfg)
    ok, err, n, capped = handle.start()
                                  -- begin tick + rescan timers, and REPORT:
                                  --   false, err, 0  hard failure (no DLL /
                                  --                  contract mismatch) -- nothing
                                  --                  is frozen; tell the user
                                  --   true,  nil, 0  armed, no live instances YET
                                  --                  (normal -- spawns get picked
                                  --                  up on the next rescan)
                                  --   true,  nil, n  frozen on n instances
                                  -- `capped` (4th) is true when the DLL's result cap
                                  -- was reached: n is then a FLOOR, not a total, and
                                  -- more instances exist unheld. Common with
                                  -- derived=true off a broad base like 'Actor'.
    handle.stop()        -- cancel both timers cleanly

  cfg fields:
    className          (req) string  UE class name (e.g. 'BP_Teammate_C')
                                     -- case-insensitive; see `derived` for whether
                                     -- subclasses are included
    derived            opt   bool    default TRUE. Hold the property on instances of
                                     className AND OF EVERY SUBCLASS of it. This is
                                     what a Property Search row usually needs: an
                                     INHERITED field is keyed to the class that
                                     DECLARES it, so `bCanBeDamaged` arrives as
                                     'Actor' and an exact-class pool holds whichever
                                     stray AActor the level has -- never the pawn the
                                     user was looking at. Set false for exact-class
                                     only. Requires a DLL speaking mailbox contract 3.
    memrec             opt   object  the CE memory record that owns this freeze
                                     (pass `memrec` from the [ENABLE] block). When the
                                     freeze ABANDONS itself after MAX_FAIL_STREAK
                                     failed rescans it drives this record inactive, so
                                     the checkbox stops claiming a cheat is applied
                                     when nothing is being written. Omit it and the
                                     freeze still stops -- it just cannot untick.
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
    UE5_FREEZE_HELPER_VERSION = '1.3'   -- 1.1 added cfg.boolMask (packed bitfield bools)
                                        -- 1.2 start() returns (ok, err, count)
                                        -- 1.3 cfg.derived + cfg.memrec; start() also
                                        --     returns `capped`

  =========================================================================
  SAMPLES
  =========================================================================

  #########################################################################
  #  READ THIS BEFORE COPYING ANYTHING BELOW -- HANDLE LIFETIME           #
  #########################################################################
  #                                                                       #
  #  A handle kept in a `local` CANNOT BE STOPPED. Not "is awkward to      #
  #  stop" -- cannot, ever, for the life of the Cheat Engine process.      #
  #                                                                       #
  #  Cheat Engine compiles EACH {$lua} block as its own chunk             #
  #  (autoassembler.pas: the [ENABLE] block's text is handed to            #
  #  luaL_loadstring on its own) inside ONE shared Lua state. So a         #
  #  GLOBAL set in [ENABLE] is visible in [DISABLE]; a `local` is not.     #
  #  [ENABLE] and [DISABLE] are separate compilations besides.             #
  #                                                                       #
  #  handle.start() creates two timers owned by CE's main form and hands   #
  #  their callbacks to CE. Those callbacks keep the handle alive, but     #
  #  nothing you can NAME still refers to it -- so the freeze keeps        #
  #  writing ~20x/sec into the game, and re-enabling the script just adds  #
  #  another orphaned pair. Unticking the record does not stop it. Nor     #
  #  does deleting the record: the timers belong to the main form, not to  #
  #  the memory record. Only restarting Cheat Engine ends it.              #
  #                                                                       #
  #  So: PUT THE HANDLE IN A GLOBAL TABLE, KEYED. Never a bare global      #
  #  `h` either -- CE shares one Lua state across every table you have     #
  #  open, so a second script using `h` silently steals the first one's    #
  #  slot, and then the FIRST script's [DISABLE] stops the SECOND          #
  #  script's freeze. SAMPLE 1 is the shape to copy; it is the same shape  #
  #  UE5DumpUI's own generated scripts use.                               #
  #########################################################################

  ----- SAMPLE 1: THE COMPLETE SHAPE -- copy this whole thing -----
    Both blocks. The [DISABLE] half is not optional; it is the only way the
    freeze ever stops.

    [ENABLE]
    {$lua}
    if syntaxcheck then return end
    _ue5_freeze_handles = _ue5_freeze_handles or {}      -- GLOBAL: crosses blocks
    local KEY = 'Teammate_HP'                             -- unique per script

    -- Defensive stop: an AA Script reloaded while active would otherwise
    -- leak the previous handle exactly as a `local` does.
    if _ue5_freeze_handles[KEY] then
      pcall(_ue5_freeze_handles[KEY].stop)
      _ue5_freeze_handles[KEY] = nil
    end

    local h = freezeProperty({
      className  = 'BP_Teammate_C',
      derived    = true,      -- also every SUBCLASS of it (see cfg docs)
      propOffset = 0x4F8,
      valueType  = 'float',
      value      = 100.0,
      -- Hand the record over so an ABANDONED freeze can untick itself. Without
      -- this the box keeps claiming the cheat is on after writing has stopped.
      memrec     = memrec,
    })
    _ue5_freeze_handles[KEY] = h        -- <-- reachable from [DISABLE]

    local ok, err, n, capped = h.start()
    if not ok then                       -- hard failure: nothing is frozen
      pcall(h.stop)
      _ue5_freeze_handles[KEY] = nil
      showMessage('[Freeze] ' .. tostring(err))
      if memrec then memrec.Active = false end
      return
    end
    if n == 0 then
      -- Not an error: armed, and it applies as instances spawn.
      print('[Freeze] armed -- no live instances yet')
    elseif capped then
      -- n is a FLOOR: the DLL's cap was reached and more instances exist unheld.
      print('[Freeze] armed on ' .. n .. ' instance(s) -- cap reached, more exist unheld')
    end
    {$asm}

    [DISABLE]
    {$lua}
    if syntaxcheck then return end
    _ue5_freeze_handles = _ue5_freeze_handles or {}
    local KEY = 'Teammate_HP'                             -- re-declared: locals do NOT cross
    local h = _ue5_freeze_handles[KEY]
    if h then
      pcall(h.stop)
      _ue5_freeze_handles[KEY] = nil
    end
    {$asm}

  ----- SAMPLES 2-4: ONLY the cfg differs -----
    Keep SAMPLE 1's wrapper exactly as it is (both blocks, the keyed global,
    the defensive stop, the outcome check) and swap the table passed to
    freezeProperty. Give each script its OWN unique KEY.

  ----- SAMPLE 2: God mode (bool) -----
      className  = 'PlayerCharacter',
      propOffset = 0x328,
      valueType  = 'bool',
      value      = false,    -- e.g. bCanBeDamaged = false  (or 0)

  ----- SAMPLE 2b: PACKED bitfield bool (shares a byte with siblings) -----
    UE packs `uint8 bFoo:1` bools eight to a byte. Pass the FieldMask and only
    that bit is touched; omit it and the other seven get wiped.
      className  = 'PlayerCharacter',
      propOffset = 0x328,
      valueType  = 'bool',
      value      = true,
      boolMask   = 0x04,     -- this bool owns bit 2 of the byte at 0x328

  ----- SAMPLE 3: Filter -- only freeze teammates, NOT the local player -----
    localPawn is whatever pointer identifies "me" -- read it off a known
    LocalPlayer chain, or capture the address from CE before starting.
    Declare it ABOVE the freezeProperty call in the same [ENABLE] block; the
    filter closes over it, so a `local` is correct here (it never has to
    outlive the block).
      className  = 'BP_Teammate_C',
      propOffset = 0x4F8,
      valueType  = 'float',
      value      = 9999.0,
      filter     = function(addr) return addr ~= localPawn end,

  ----- SAMPLE 4: Two properties in one script (HP + MP) -----
    Two handles, so TWO keys -- and [DISABLE] must stop both. Everything else
    is SAMPLE 1 twice over:
      _ue5_freeze_handles['Teammate_HP'] = hp   -- propOffset 0x4F8
      _ue5_freeze_handles['Teammate_MP'] = mp   -- propOffset 0x4FC
    and in [DISABLE], look BOTH up from the global table and stop each.
    (Stopping them via the [ENABLE] block's locals cannot work -- see the
    lifetime box above.)

  ----- SAMPLE 5: Editing className / offset / value after generation -----
    Generated AA Scripts contain a `local CFG = { ... }` block near the top.
    Edit any field there and reactivate the script -- the new cfg is read
    fresh on every [ENABLE]. Use UE5DumpUI's PropertySearch panel to discover
    a new offset, copy the value into CFG, done.
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
-- 1.3: cfg.derived (hold every SUBCLASS too, the scope a Property Search row for an
-- inherited field actually needs) + cfg.memrec (an abandoned freeze unticks its own CE
-- record instead of leaving the checkbox claiming a cheat that stopped writing) +
-- start()'s 4th return `capped`. cfg.derived REQUIRES mailbox contract 3, so unlike
-- 1.1/1.2 this one is a compatibility gate: see UE5_SCRIPT_CONTRACT below.
-- (`[FREEZESCOPE-2026-08-18]` / `[FREEZESTUCK-2026-08-18]`)
if not UE5_FREEZE_HELPER_VERSION then
  UE5_FREEZE_HELPER_VERSION = '1.3'
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
local OFF_CMD_FLAGS  = 0x728  -- IN  (contract 3): per-command input flags
local OFF_OUT_FLAGS  = 0x72C  -- OUT (contract 3): per-command output flags

local CMD_LIST_INSTANCES = 6
local STATUS_DONE        = 1
local STATUS_IDLE        = 0    -- untouched: the DLL never picked the command up

-- CMD_LIST_INSTANCES flags (dll/src/Mimic.h ListInstancesFlag).
local LI_IN_DERIVED    = 1  -- cmdFlags:    include every SUBCLASS of className
local LI_OUT_TRUNCATED = 1  -- cmdOutFlags: the DLL's result cap was reached

-- Page geometry, and it is NOT one number: the entry stride depends on the scope
-- (dll/src/Mimic.h ListInstancesEntrySize). Exact scope ships bare UObject*s and one
-- class witness for the whole page; derived scope ships (UObject*, UClass*) pairs,
-- because a sweep across subclasses has no single class to witness with. Reading a
-- derived page at the exact stride does not fail loudly -- it reads the high half of
-- one pointer as the next object, and freezes an address that is not one.
local ENTRY_SIZE_EXACT   = 8
local ENTRY_SIZE_DERIVED = 16
local MAX_PAGES          = 16   -- matches Mimic::LIST_INSTANCES_MAX_PAGES

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
-- 3: CMD_LIST_INSTANCES takes an opt-in DERIVED scope (cmdFlags LI_IN_DERIVED) and
-- reports a capped pool (cmdOutFlags LI_OUT_TRUNCATED). Baked as this helper's
-- baseline rather than probed per-cfg, and deliberately so: the shipped generator
-- always asks for derived scope, because holding only the DECLARING class of an
-- inherited field is the defect (`[FREEZESCOPE-2026-08-18]`). Against a contract-2
-- DLL the flag would be ignored and the freeze would hold the wrong pool while
-- reporting success -- exactly the silent-wrong-scope failure being fixed -- so this
-- refuses up front and says to update the DLL.
local UE5_SCRIPT_CONTRACT = 3

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
-- Returns: page (or nil), totalPages, errMsg (nil on success), classPtr, classOff, capped
--
-- `page` is an array of { addr = <UObject*>, cls = <UClass*> }. `cls` is filled only
-- in DERIVED scope, where the DLL ships a per-entry witness because the sweep spans
-- many concrete classes; in exact scope it is nil and the page-wide classPtr covers
-- every entry.
--
-- classPtr/classOff are the contract-2 identity witness (see checkContract): the
-- UClass* every entry on this page belongs to, and the byte offset of
-- UObject::ClassPrivate. tick() re-reads that field before every write so a slot
-- recycled by a DIFFERENT class is refused. classPtr is 0 in derived scope BY
-- DESIGN (there is no single class); classOff is required in both, and the contract
-- check above makes a missing one unreachable.
--
-- `capped` is the DLL's own LI_OUT_TRUNCATED bit -- the walk that did the capping is
-- the only thing that knows, so it is read rather than re-derived here.
local function fetchInstancePage(className, pageIndex, derived)
  local mb, ferr = findMailbox()
  if not mb then return nil, 0, ferr end

  if _ue5_invoke_busy then
    -- Don't corrupt a concurrent invoke. Caller (rescan) treats this
    -- as "skip this cycle"; tick keeps writing the existing cache.
    return nil, 0, 'mailbox busy (concurrent invoke or rescan)'
  end

  -- The flag above is OURS. It says nothing about a command an EMITTED script
  -- left in flight -- those write the mailbox directly and never touch it, so
  -- the two guards are mutually blind (audit #5 AA10). A generated toggle that
  -- timed out bails while cmd is still non-zero and the DLL still owns the
  -- mailbox; without this, the next rescan tick (<=5 s away) overwrites a live
  -- command. Same class as AA19, which this repo already rated MED.
  --
  -- Placed BEFORE `_ue5_invoke_busy = true`, never after: returning with the
  -- flag already set latches it for the session and abandons the freeze, which
  -- is a worse failure than the one being prevented.
  local inFlight = readInteger(mb + OFF_CMD)
  if inFlight ~= nil and inFlight ~= 0 then
    -- Transient by contract: rescan() treats a nil return as "skip this cycle"
    -- and MAX_FAIL_STREAK bounds it, so a genuinely stuck mailbox still gives up.
    return nil, 0, 'mailbox busy (concurrent invoke or rescan)'
  end

  _ue5_invoke_busy = true
  local pok, page, totalPages, err, classPtr, classOff, capped = pcall(function()
    writeMbStr(mb, OFF_CLASS, className)
    -- Page index goes in paramsData[0..3].
    writeInteger(mb + OFF_PARAMS, pageIndex)
    -- Scope flag (contract 3). Written EVERY call, including the 0 case: the DLL
    -- clears it after reading, but writing it unconditionally means this helper
    -- never depends on that -- an exact-scope request can never inherit a derived
    -- one left behind by anything else sharing the mailbox.
    writeInteger(mb + OFF_CMD_FLAGS, derived and LI_IN_DERIVED or 0)
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
    if cOff < 8 or cOff > 0x200 then cOff = 0 end
    if derived then
      -- No page-wide class in derived scope, and that is not a degraded state --
      -- the witness travels per entry below. But WITHOUT the offset there is no
      -- witness at all, and writing unguarded is precisely the AA2 defect, so this
      -- is an error rather than a fallback.
      cPtr = 0
      if cOff == 0 then
        return nil, 0,
          'the DLL did not publish the ClassPrivate offset for a derived scan ' ..
          '-- refusing to freeze without an identity witness'
      end
    elseif cPtr == 0 then
      cOff = 0
    end

    local entrySize = derived and ENTRY_SIZE_DERIVED or ENTRY_SIZE_EXACT
    local out = {}
    for i = 0, returned - 1 do
      local base = mb + OFF_PARAMS + (i * entrySize)
      local a = readQword(base)
      if a and a ~= 0 then
        if derived then
          -- Per-entry witness: this object's own concrete UClass*. An entry the DLL
          -- could not resolve a class for is DROPPED rather than written unguarded.
          local c = readQword(base + 8)
          if c and c ~= 0 then out[#out + 1] = { addr = a, cls = c } end
        else
          out[#out + 1] = { addr = a }
        end
      end
    end

    -- Truncation is the DLL's verdict, not ours: only the walk knows whether it
    -- stopped at its cap. A broad derived base reaches it routinely.
    local outFlags = readInteger(mb + OFF_OUT_FLAGS) or 0
    local wasCapped = (outFlags % (LI_OUT_TRUNCATED * 2)) >= LI_OUT_TRUNCATED

    return out, totalPagesLocal, nil, cPtr, cOff, wasCapped
  end)
  _ue5_invoke_busy = false

  if not pok then
    -- Body raised; pcall captured the error in the first slot.
    return nil, 0, tostring(page)
  end
  return page, totalPages or 0, err, classPtr or 0, classOff or 0, capped or false
end

-- Full rescan: page through CMD_LIST_INSTANCES until all instances of the class
-- are collected. Bounded at MAX_PAGES, which is the same ceiling the DLL sizes its
-- two result caps against (2000 exact / 8-byte entries, 1024 derived / 16-byte
-- entries -- both 16 pages).
--
-- Returns: addrs, errMsg, classPtr, classOff, clsOf, capped
--   addrs  array of UObject* (the shape tick() and the tests use)
--   clsOf  parallel array of per-entry UClass*, or nil in exact scope where one
--          page-wide classPtr covers everything
local function rescanInstances(className, filter, derived)
  local all, clsOf = {}, (derived and {} or nil)
  local pageIndex = 0
  local firstErr = nil
  local classPtr, classOff = 0, 0
  local capped = false

  while pageIndex < MAX_PAGES do
    local page, totalPages, err, cPtr, cOff, wasCapped =
      fetchInstancePage(className, pageIndex, derived)
    if not page then
      if pageIndex == 0 then firstErr = err end
      break
    end
    for i = 1, #page do
      all[#all + 1] = page[i].addr
      if clsOf then clsOf[#clsOf + 1] = page[i].cls end
    end
    if wasCapped then capped = true end
    -- Exact scope: every page reports the same class, so the first non-zero witness
    -- is THE witness (taking the first rather than the last means a later empty page
    -- cannot erase it). Derived scope: cPtr is 0 by design and cOff is what matters,
    -- so latch that on its own.
    if classPtr == 0 and cPtr and cPtr ~= 0 then
      classPtr, classOff = cPtr, cOff
    elseif classOff == 0 and cOff and cOff ~= 0 then
      classOff = cOff
    end
    pageIndex = pageIndex + 1
    if totalPages <= pageIndex then break end
    -- Out of page budget with pages still unread. Only THIS is truncation-by-us:
    -- testing `pageIndex >= MAX_PAGES` after the loop instead would also fire for a
    -- pool that exactly fills the budget and was therefore complete, which is a
    -- caveat printed over a freeze that has nothing wrong with it.
    if pageIndex >= MAX_PAGES then capped = true; break end
  end

  if filter then
    local filtered, filteredCls = {}, (clsOf and {} or nil)
    for i = 1, #all do
      if filter(all[i]) then
        filtered[#filtered + 1] = all[i]
        -- The two arrays are indexed in lockstep by tick(), so a filter that drops
        -- an address MUST drop its witness with it. Filtering one and not the other
        -- silently pairs each surviving object with the NEXT one's class, and every
        -- write is then refused while the freeze reports itself healthy.
        if filteredCls then filteredCls[#filteredCls + 1] = clsOf[i] end
      end
    end
    all, clsOf = filtered, filteredCls
  end

  return all, firstErr, classPtr, classOff, clsOf, capped
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

    -- Scope DEFAULTS TO DERIVED. A Property Search row for an inherited field is
    -- keyed to the class that DECLARES it, so exact-class was the wrong answer for
    -- the most common way this helper is invoked, and a default that is wrong for
    -- the common case is the defect (`[FREEZESCOPE-2026-08-18]`). `derived = false`
    -- is honoured; nil means "not stated", not "off".
    local derived = (cfg.derived ~= false)

    local handle = {
      cfg          = cfg,
      _derived     = derived,
      _writer      = writer,
      _cache       = {},
      -- Per-entry UClass* witnesses, index-aligned with _cache. Populated only in
      -- derived scope; nil means "use the page-wide _classPtr" (exact scope).
      _cacheCls    = nil,
      _tickTimer   = nil,
      _rescanTimer = nil,
      _lastError   = nil,
      -- Identity witness from the last successful rescan (contract 2).
      _classPtr    = 0,
      _classOff    = 0,
      -- The DLL's result cap was reached on the last successful rescan: the cache
      -- is a PREFIX of the real pool and more instances exist unheld.
      _capped      = false,
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
      -- Derived scope: each entry carries its OWN concrete class, because the pool
      -- spans subclasses and one page-wide pointer would refuse every object that
      -- is not literally the base. nil here = exact scope, one witness for all.
      local clsOf  = handle._cacheCls
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
        local want = clsOf and clsOf[i] or cPtr
        if want and want ~= 0 and cOff ~= 0 then
          ok = readQword(addr + cOff) == want
        elseif clsOf then
          -- Derived scope with no witness for this entry: refuse. rescan drops
          -- witness-less entries, so reaching here means the cache was hand-built;
          -- writing unguarded is the AA2 defect and is never the safer default.
          ok = false
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
    --- Stop writing, then drive the owning CE record inactive.
    ---
    --- WHY THE RECORD AND NOT JUST A PRINT (`[FREEZESTUCK-2026-08-18]`). The print
    --- below lands in CE's Lua Engine window, which hygiene closes on a successful
    --- enable -- so an abandoned freeze was silent, and the user was left looking at
    --- a TICKED record (in CE a red X on the checkbox means ACTIVE, not failed) over
    --- a freeze that had permanently stopped writing. CLAUDE.md's rule for every
    --- stateful toggle is that a bail-out which applied nothing must untick, and a
    --- freeze that stopped writing is applying nothing. It also makes the existing
    --- advice -- "re-enable the record" -- possible to follow, which it was not while
    --- the record had never been disabled.
    ---
    --- WHY IT IS DEFERRED. Setting `memrec.Active = false` runs the record's
    --- [DISABLE] block synchronously, and that block calls handle.stop(), which
    --- DESTROYS the very timer whose OnTimer is on the stack right now. `showMessage`
    --- is modal and pumps messages, so it would let other timers re-enter for the
    --- same reason. Disabling both timers first stops all further writes
    --- immediately; the one-shot timer then does the rest after this callback has
    --- returned. Same shape (and the same reason) as the deferred self-untick the
    --- Teleport generator emits.
    ---
    --- WHY THE MESSAGE RIDES ALONG AT ALL. The generated [DISABLE] block ends with
    --- the hygiene auto-close, so unticking the record CLOSES the Lua Engine window —
    --- which means the print() above cannot survive its own fix, and a record that
    --- silently unticks itself is only half an answer. A modal survives. Same text as
    --- the print, from one variable, so the two channels cannot drift.
    ---
    --- WHY IT COMES AFTER THE UNTICK. `showMessage` blocks until the user dismisses
    --- it. Correcting the checkbox first means the record is honest whether or not
    --- anyone is at the keyboard; the other order leaves it ticked for as long as an
    --- unattended dialog sits there.
    local function abandonAndUntick(message)
      if handle._tickTimer   then handle._tickTimer.Enabled   = false end
      if handle._rescanTimer then handle._rescanTimer.Enabled = false end

      local rec = handle.cfg.memrec
      -- Scheduled with or WITHOUT a record: no record means the checkbox cannot be
      -- corrected, which makes telling the user more important, not less.
      local t = createTimer(getMainForm(), false)
      t.Interval = 50
      t.OnTimer = function(timer)
        timer.destroy()
        if rec then
          -- pcall: the user may have deleted the record while the freeze ran, and a
          -- freed record must not turn a diagnosed failure into a Lua traceback.
          pcall(function() rec.Active = false end)
        end
        if type(showMessage) == 'function' then pcall(showMessage, message) end
      end
      t.Enabled = true
    end

    local function rescan()
      local addrs, err, cPtr, cOff, clsOf, capped =
        rescanInstances(handle.cfg.className, handle.cfg.filter, handle._derived)
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
          handle._cacheCls = nil
          -- Nothing is held any more, so nothing is capped either. Leaving the last
          -- successful rescan's flag standing would have isTruncated() describe a
          -- pool that no longer exists.
          handle._capped = false
          -- ONE message, two channels. Built once so the print and the modal that
          -- follows the untick can never say different things.
          local msg = string.format(
            '[ue5_freeze] %s: %d consecutive rescans failed -- freeze STOPPED ' ..
            'writing (last error: %s). %s',
            tostring(handle.cfg.className), handle._failStreak, tostring(err),
            handle.cfg.memrec
              and 'This record has been unticked; re-enable it after fixing the cause.'
              or  'Untick and re-tick this record after fixing the cause.')
          -- Ungated on purpose: this is a real failure, and CE Lua hygiene
          -- keeps genuine failures unconditional. It is printed ONCE per
          -- abandonment, not per rescan.
          print(msg)
          -- Stops the timers now; unticks and re-states the message once this
          -- callback has returned.
          abandonAndUntick(msg)
        end
        return false, err, 0, handle._capped
      else
        handle._cache = addrs
        handle._cacheCls = clsOf
        handle._lastError = nil
        handle._failStreak = 0
        handle._abandoned = false
        handle._capped = capped and true or false
        -- Refresh the witness from the same enumeration that produced the
        -- cache. A rescan that returned instances but no witness would leave a
        -- stale class pointer paired with fresh addresses, so they move together.
        handle._classPtr = cPtr or 0
        handle._classOff = cOff or 0
        return true, nil, #addrs, handle._capped
      end
    end

    --- Last rescan error, or nil. `_lastError` had three writers and zero
    --- readers, so no failure ever reached anyone (audit #5 AA3).
    handle.lastError = function() return handle._lastError end

    --- True once consecutive rescan failures made the handle stop writing.
    --- The abandonment now also disables the timers and unticks cfg.memrec, so
    --- nothing has to POLL this to keep the checkbox honest -- it stays a readable
    --- state for tests and for a caller that wants to ask.
    handle.isAbandoned = function() return handle._abandoned end

    --- True when the last successful rescan hit the DLL's result cap: the frozen
    --- set is a PREFIX of the real pool and more instances exist unheld.
    handle.isTruncated = function() return handle._capped end

    --- True when this handle holds className AND every subclass of it.
    handle.isDerived = function() return handle._derived end

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
      local ok, err, count, capped = rescan()

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

      -- The initial rescan runs BEFORE the timers exist, so an abandonment on the
      -- very first pass (MAX_FAIL_STREAK is 3, so only reachable on a restarted
      -- handle) left them enabled. Re-apply the stop here rather than reordering
      -- start(): the timers have to exist for stop() to be reachable at all.
      if handle._abandoned then
        handle._tickTimer.Enabled   = false
        handle._rescanTimer.Enabled = false
      end

      return ok, err, count, capped
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
      handle._cacheCls = nil
    end

    return handle
  end

  registerLuaFunctionHighlight('freezeProperty')
end
