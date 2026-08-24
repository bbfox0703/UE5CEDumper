--[[
  ue5_invoke_helper.lua
  UE5CEDumper -- UFunction Invoker (mailbox protocol)

  This is the runtime helper required by AA Scripts produced via
  "Copy AA Script (Baked)" in UE5DumpUI. The generated AA Script
  uses findTableFile('ue5_invoke_helper.lua') to locate this code,
  so this file MUST be embedded in your CE table:

    Setup once per .CT:
      1. Save this file next to your .CT (e.g. via Tools ->
         Export CE Helper Lua File... in UE5DumpUI)
      2. In Cheat Engine: Table -> Add File... -> select this file
      3. Save the .CT to bake the file into the table

  All baked AA Scripts will then resolve the helper through
  findTableFile and call invokeUFunction(...).

  Public API (re-declaration-safe, syntax-highlighted):
    ok, err = invokeUFunction(className, funcName, parmsSize, params)
    value   = readUFunctionReturn(offset, valueType)
    freed   = freeInvokeStringBuffers() -- free FString INPUT-param buffers (UNSAFE unless read-only)
    state   = setDebugCamera(enable)   -- robust force on/off (1=on,0=off,-1=err)
    state   = getDebugCameraState()    -- 1=on, 0=off, -1=unknown

  String INPUT params: a param descriptor with type 'fstring' (wide, UE FString)
  or 'fstringn' (narrow, FUtf8String/FAnsiString) takes value = a Lua string; the
  helper allocates a char buffer in the target process and builds the by-value
  { Data, Num, Max } struct in place. See writeFStringInline for the lifetime note.

  Debug Camera memory-record example (one checkbox = camera on/off).
  Both blocks call the SAME DLL export, only the arg differs; the DLL
  reads state, toggles only when needed, and on a disable that the game's
  stripped ToggleDebugCamera can't honour, switches the local player's
  controller back to the original PlayerController:

    [ENABLE]
    {$lua}
    if syntaxcheck then return end
    -- setDebugCamera RAISES on a mailbox failure and returns -1 on a DLL-side error;
    -- either way nothing was applied, so untick the record (a stateful toggle must not
    -- leave a ticked box claiming a cheat that is not on) and report. (audit #5 AA31)
    local ok, state = pcall(setDebugCamera, 1)
    if not ok or state ~= 1 then
      if memrec then memrec.Active = false end
      local why = ok and ('returned state ' .. tostring(state)) or ('error: ' .. tostring(state))
      showMessage('[Debug Camera] could not enable -- ' .. why)
      return
    end
    {$asm}

    [DISABLE]
    {$lua}
    if syntaxcheck then return end
    -- On disable the record is going inactive regardless; still guard the call so a
    -- mailbox failure surfaces instead of raising out of the [DISABLE] block.
    local ok, err = pcall(setDebugCamera, 0)
    if not ok then showMessage('[Debug Camera] disable error: ' .. tostring(err)) end
    {$asm}

  Constants exposed:
    UE5_INVOKE_HELPER_VERSION  = '1.3'
    UE5_INVOKE_PARAMS_OFFSET   = 0x328  (params_data offset within mailbox)
]]

-- ============================================================
-- Version (callers can sanity-check after load)
-- ============================================================

if not UE5_INVOKE_HELPER_VERSION then
  UE5_INVOKE_HELPER_VERSION = '1.3'
end

-- ============================================================
-- Reentrancy guard
-- ============================================================
-- The DLL exposes a single-slot mailbox (g_invokeMailbox in
-- Mimic.cpp). If two CE-Lua callers write the className /
-- funcName / paramsData fields concurrently the in-flight call
-- gets corrupted -- both callers may observe status=DONE while
-- the DLL only executed one (Frankenstein) request. CE's Lua
-- `sleep(1)` does NOT pump Windows messages (CE's lua_sleep is a bare Sleep) --
-- the pump() next to it is what keeps timer / synchronize
-- callbacks CAN fire reentrantly inside waitDone's poll loop.
--
-- This flag serializes invokes within a single Lua engine. The
-- DLL side has no busy-rejection of its own; this is the only
-- guard. The `if nil` init pattern preserves the flag across
-- helper re-loads (multiple AA Scripts loading the helper file
-- in the same session) so a concurrent in-flight call isn't
-- silently cleared.
if _ue5_invoke_busy == nil then
  _ue5_invoke_busy = false
end

-- ============================================================
-- Mailbox layout (must match dll/src/Mimic.h MailboxData struct)
-- ============================================================

local OFF_CMD       = 0x000  -- int32: command (write LAST to trigger)
local OFF_STATUS    = 0x004  -- int32: status (poll for STATUS_DONE=1)
local OFF_RESULT    = 0x008  -- int32: result code (0 = success)
local OFF_INSTANCE  = 0x010  -- uint64: UObject*
local OFF_UFUNC     = 0x018  -- uint64: UFunction*
local OFF_PARMS_SZ  = 0x020  -- uint16: ParmsSize
local OFF_NUM_PARMS = 0x022  -- uint16: NumParms
local OFF_FLAGS     = 0x024  -- uint32: FunctionFlags
local OFF_CLASS     = 0x028  -- char[256]: class name
local OFF_FUNC      = 0x128  -- char[256]: function name
local OFF_ERR       = 0x228  -- char[256]: error message
local OFF_PARAMS    = 0x328  -- uint8[1024]: inline params buffer

local CMD_INVOKE_BY_NAME = 4
local STATUS_DONE        = 1
local STATUS_IDLE        = 0    -- untouched: the DLL never picked the command up
local CMD_IDLE           = 0    -- the DLL clears cmd back to this when it finishes

-- Default invoke timeout (ms). UE5DumpUI's per-game override only
-- affects the DLL side; this Lua-side timeout guards against the
-- mailbox poll loop hanging if the game thread stops responding.
local DEFAULT_TIMEOUT_MS = 10000

-- Exported so callers can do `local p = mb + UE5_INVOKE_PARAMS_OFFSET`
-- to read return values directly (advanced usage; prefer
-- readUFunctionReturn for typed reads).
if not UE5_INVOKE_PARAMS_OFFSET then
  UE5_INVOKE_PARAMS_OFFSET = OFF_PARAMS
end

-- ============================================================
-- Internal helpers (file-local -- no global pollution)
-- ============================================================

-- ============================================================
-- CE Lua <-> DLL contract check
-- ============================================================
-- Versioned on the CONTRACT (mailbox offsets, Cmd values, per-command ops,
-- status/result meanings), NOT on the build number: a .CT saved months ago stays
-- valid against a newer DLL as long as nothing it depends on moved. The DLL
-- publishes a RANGE so the two failure directions can be told apart -- too-old
-- script means regenerate, too-old DLL means update the DLL. See dll/src/Mimic.h.
local UE5_SCRIPT_CONTRACT = 1

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
  local mb = getAddressSafe('g_invokeMailbox')
  if not mb or mb == 0 then
    mb = getAddressSafe('UE5Dumper.g_invokeMailbox')
  end
  if not mb or mb == 0 then
    error('[ue5_invoke] g_invokeMailbox symbol not found -- ' ..
          'is UE5Dumper.dll injected? (Check the proxy DLL or CE -> ' ..
          'Add this process / Inject DLL.)')
  end
  -- Validated HERE because every path reaches the mailbox through this function,
  -- and it has to happen before the caller writes anything: if the layout moved,
  -- a write lands on whatever now occupies those offsets.
  local ok, why = checkContract()
  if not ok then error('[ue5_invoke] ' .. why) end
  return mb
end

local function writeMbStr(mb, off, str)
  local b = {}
  local len = math.min(#str, 255)
  for i = 1, len do b[#b + 1] = string.byte(str, i) end
  b[#b + 1] = 0  -- null terminator
  writeBytes(mb + off, b)
end

-- ============================================================
-- FString / FUtf8String / FAnsiString INPUT params (by value)
-- ============================================================
-- A UE string param is passed BY VALUE: the params buffer holds the whole
-- 16-byte struct { CharT* Data; int32 ArrayNum; int32 ArrayMax } INLINE, not a
-- pointer to it. So we allocate a Data buffer in the TARGET process, write the
-- characters + null terminator, and stamp the three struct fields.
--
-- LIFETIME: allocations are tracked in _ue5_invoke_str_bufs and are NOT freed
-- automatically. Freeing is unsafe if the callee kept the pointer, and the
-- buffer is CE-allocated (not UE's FMemory) so the game must NEVER free it.
-- Call freeInvokeStringBuffers() manually only when every such call merely READ
-- the string. Leaking a few small buffers is the safe default for one-shot cheats.
if _ue5_invoke_str_bufs == nil then
  _ue5_invoke_str_bufs = {}
end

-- Build a by-value UE string at (pd + off). wide=true -> UTF-16LE (FString);
-- wide=false -> raw bytes (FUtf8String is UTF-8, FAnsiString is ANSI).
local function writeFStringInline(pd, off, s, wide)
  s = tostring(s or '')
  local n = #s
  local bytes = {}
  local buf
  if wide then
    -- UTF-16LE: low byte + 0 high byte. ASCII / basic Latin only; multi-byte
    -- UTF-8 input is not transcoded here.
    for i = 1, n do
      bytes[#bytes + 1] = string.byte(s, i)
      bytes[#bytes + 1] = 0
    end
    bytes[#bytes + 1] = 0
    bytes[#bytes + 1] = 0                 -- L'\0'
    buf = allocateMemory((n + 1) * 2)
  else
    for i = 1, n do
      bytes[#bytes + 1] = string.byte(s, i)
    end
    bytes[#bytes + 1] = 0                 -- '\0'
    buf = allocateMemory(n + 1)
  end
  -- CE returns nil when the target-process allocation fails. Unchecked, the three
  -- writes below still ran: Data = 0 with ArrayNum = ArrayMax = n+1, i.e. an FString
  -- that PROMISES n+1 characters at address 0, handed straight to a live UFunction.
  -- The length must never be published for a buffer that does not exist, so this
  -- raises before any of them. (audit #5 AA14 / AA15)
  if buf == nil or buf == 0 then
    error(string.format(
      "[ue5_invoke] could not allocate %d bytes in the target process for the " ..
      "string param at +%d -- the invoke was NOT sent (no partial FString written)",
      #bytes, off))
  end
  writeBytes(buf, bytes)
  writeQword(pd + off, buf)               -- Data
  writeInteger(pd + off + 8,  n + 1)      -- ArrayNum (incl null)
  writeInteger(pd + off + 12, n + 1)      -- ArrayMax
  _ue5_invoke_str_bufs[#_ue5_invoke_str_bufs + 1] = buf
end

-- Stamp a list of baked params into the buffer at `base`. Each entry is
-- { name, type, offset, value, size? }. Split out of writeBakedParams so a
-- nested 'fstruct' member table can recurse (offsets RELATIVE to the struct
-- base) -- hence the explicit base + region size rather than assuming the
-- mailbox layout. Does NOT zero the buffer (writeBakedParams does that once).
local function writeParams(base, regionSize, params)
  if not params then return end

  -- Every write MUST stay inside the caller's region. A param whose offset+width runs
  -- past regionSize would scribble past the params buffer and, at the TOP level, past
  -- g_invokeMailbox itself -- writeParams took regionSize and never enforced it. Refuse
  -- rather than corrupt. (audit #5 AA33)
  local function bound(off, width, name)
    if off < 0 or off + width > regionSize then
      error(string.format(
        "[ue5_invoke] param '%s' at +%d (%d bytes) exceeds the %d-byte params region " ..
        "-- refusing to write past the mailbox", tostring(name), off, width, regionSize))
    end
  end
  -- Fixed byte-widths for the scalar / pointer types. fstring / fstringn / fstruct
  -- bound themselves below (16-byte struct / computed size); the container types
  -- write nothing but still keep their declared slot in-region.
  local WIDTHS = {
    bool = 1, byte = 1, int16 = 2, uint16 = 2, int32 = 4, uint32 = 4, enum = 4,
    int64 = 8, uint64 = 8, qword = 8, float = 4, double = 8,
    pointer = 8, object = 8, class = 8, name = 8, soft = 8, weak = 8, lazy = 8, interface = 8,
  }

  for i, p in ipairs(params) do
    local v    = p.value or 0
    local off  = p.offset or 0
    local t    = p.type or 'int32'
    local size = p.size            -- optional explicit byte size (any type)
    local w    = WIDTHS[t]
    if w then bound(off, w, p.name) end

    if t == 'bool' then
      writeBytes(base + off, { (v ~= 0 and v ~= false) and 1 or 0 })
    elseif t == 'byte' then
      writeBytes(base + off, { math.floor(v) % 256 })
    elseif t == 'int16' or t == 'uint16' then
      writeSmallInteger(base + off, math.floor(v))
    elseif t == 'int32' or t == 'uint32' or t == 'enum' then
      writeInteger(base + off, math.floor(v))
    elseif t == 'int64' or t == 'uint64' or t == 'qword' then
      writeQword(base + off, v)
    elseif t == 'float' then
      writeFloat(base + off, v)
    elseif t == 'double' then
      writeDouble(base + off, v)
    elseif t == 'pointer' or t == 'object' or t == 'class'
           or t == 'name' or t == 'soft' or t == 'weak'
           or t == 'lazy' or t == 'interface' then
      writeQword(base + off, v)
    elseif t == 'fstring' then
      -- Wide UE FString INPUT param (value = Lua string). Pass p.value, NOT v:
      -- `v = p.value or 0` turns a MISSING string into 0 -> the literal "0" written
      -- into the game. p.value (nil -> "" inside writeFStringInline) is an empty
      -- FString -- an honest default, not a fabricated value. (audit #5 AA34)
      bound(off, 16, p.name)                        -- {Data(8), Num(4), Max(4)} = 16 (AA33)
      writeFStringInline(base, off, p.value, true)
    elseif t == 'fstringn' then
      -- Narrow FUtf8String / FAnsiString INPUT param. p.value, not v -- see AA34 above.
      bound(off, 16, p.name)                        -- (AA33)
      writeFStringInline(base, off, p.value, false)
    elseif t == 'tarray' or t == 'tmap' or t == 'tset' or t == 'delegate' then
      bound(off, size or 0, p.name)   -- writes nothing, but keep the slot in-region (AA33)
      -- BakedScriptGenerator.MapToHelperType CAN emit these (a TArray/TMap/TSet or
      -- a delegate INPUT param), and writeParams accepted none of them -- so the
      -- error at the bottom of this chain aborted the WHOLE invoke, and such a
      -- UFunction could not be called at all from an exported script. (audit #5 AA16)
      --
      -- Nothing is written, deliberately: writeBakedParams zeroes the entire params
      -- buffer first, and all-zero IS the default-constructed empty value for each of
      -- these -- TArray/TSet/TMap { Data = nullptr, Num = 0, Max = 0 } and an unbound
      -- FScriptDelegate { null object, NAME_None }. A nested fstruct recursion zeroes
      -- its own sub-region too, so the same holds there.
      --
      -- A value the caller actually supplied cannot be honoured (it would need
      -- engine-allocated storage), so it is refused rather than silently dropped:
      -- passing the empty default when the caller asked for contents is a different
      -- wrong answer, not a better one.
      if v ~= 0 and v ~= nil and v ~= false then
        error(string.format(
          "[ue5_invoke] param '%s' is a %s -- an EMPTY one is passed by leaving " ..
          "value=0; a populated %s cannot be built from CE Lua (it needs " ..
          "engine-allocated storage)",
          tostring(p.name or '?'), t, t))
      end
    elseif t == 'ftext' then
      -- NOT grouped with the containers above. An all-zero FText is not an empty
      -- FText: it holds a TSharedRef the engine dereferences on use, so a zeroed one
      -- is a crash rather than a default. Refusing is the honest answer.
      error(string.format(
        "[ue5_invoke] param '%s' is an ftext -- an FText cannot be built from CE Lua " ..
        "(it holds a shared reference the engine allocates), and passing a zeroed one " ..
        "crashes the game. Invoke a wrapper that takes an FString instead.",
        tostring(p.name or '?')))
    elseif t == 'fstruct' then
      -- By-value UE struct param. Size resolution: explicit p.size wins
      -- (the generator now emits it); else infer from the next member's
      -- offset; else consume the rest of the region. value == a member
      -- table -> recurse and stamp fields; anything else -> zero-fill only.
      local structSize = size
      if not structSize then
        if i < #params then
          structSize = (params[i + 1].offset or 0) - off
        else
          structSize = regionSize - off
        end
      end
      if structSize < 0 then structSize = 0 end
      bound(off, structSize, p.name)   -- the struct region must fit the parent (AA33)
      -- Zero the struct region in one write. writeBakedParams already wiped
      -- the top-level buffer, but a nested recursion runs on a sub-region
      -- the caller did not pre-zero, so keep this local wipe.
      if structSize > 0 then
        local zeros = {}
        for j = 1, structSize do zeros[j] = 0 end
        writeBytes(base + off, zeros)
      end
      if type(v) == 'table' then
        writeParams(base + off, structSize, v)
      end
    else
      error(string.format(
        "[ue5_invoke] Unknown param type '%s' for '%s' -- " ..
        "supported: bool/byte/int16/uint16/int32/uint32/enum/int64/uint64/qword/" ..
        "float/double/pointer/object/class/name/soft/weak/lazy/interface/" ..
        "fstring/fstringn/fstruct/tarray/tmap/tset/delegate",
        tostring(t), tostring(p.name or '?')))
    end
  end
end

-- Zero the whole params buffer (clears stale data from the previous invoke --
-- the mailbox is a single shared slot reused across every call) then stamp
-- the baked params.
-- The whole 1024-byte region, not the caller's parmsSize. The DLL passes
-- sizeof(MailboxData::paramsData) -- a flat 1024 -- to UE5_CallProcessEventEx
-- (dll/src/Mimic.cpp), which copies all of it into the request it owns and hands
-- that to ProcessEvent. Zeroing only parmsSize left every byte past it holding the
-- PREVIOUS command's data, and whenever parmsSize understates the UFunction's real
-- ParmsSize -- stale metadata, or a generator that counted only the params it knew
-- about -- those bytes are read as live parameters. (audit #5 AA17)
--
-- One writeBytes rather than a per-byte loop: the old form cost one CE round trip
-- PER BYTE, so covering the full region this way is also faster than covering part
-- of it the old way.
--
-- writeParams still gets `parmsSize` as its region size: that is the CALLER's
-- declared extent and drives the fstruct size-inference fallback, which must not
-- silently grow to 1024.
local PARAMS_REGION_BYTES = 1024   -- sizeof(MailboxData::paramsData), dll/src/Mimic.h

local function writeBakedParams(mb, parmsSize, params)
  local PD = mb + OFF_PARAMS
  local zeros = {}
  for i = 1, PARAMS_REGION_BYTES do zeros[i] = 0 end
  writeBytes(PD, zeros)
  writeParams(PD, parmsSize, params)
end

-- Wait for the mailbox round-trip to finish. Mirrors ue5_freeze_helper.lua's waitDone
-- deliberately: the two helpers load independently, so the shape is duplicated rather
-- than shared, and they must not drift.
--
-- The limit is REAL milliseconds. It used to count sleep(1) calls and still print the
-- result as "%dms": sleep(1) measures 15.47 ms in CE -- the ~64 Hz Windows scheduler
-- tick, identical to three decimals on two very different CPUs -- so a 10000 "ms" limit
-- was really ~155 seconds, reported as 10000ms. getTickCount() was probed in CE's Lua
-- Engine on 2026-08-06 (present, returns ms); the iteration count survives only as a
-- fallback for a build without it, at ~15 ms per iteration.
--
-- STATUS_IDLE at the deadline means the DLL never picked the command up at all, which is
-- a different fault from a wedged one and is usually a stale g_invokeMailbox address.
-- The DLL's own error string is only meaningful once it HAS taken the command.
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
    --
    -- Exactly ONE deadline governs -- see ue5_freeze_helper.lua's waitDone (audit #5
    -- AA29). The old `st==nil or (tick and tickExpired) or itersExpired` kept the
    -- iteration fallback LIVE when getTickCount() was present, racing the real deadline
    -- and printing a "%dms" that was not the arm that fired. The two helpers duplicate
    -- this shape deliberately and must not drift, so the fix is applied to both.
    local over
    if st == nil then
      over = true
    elseif tick then
      over = (tick() - t0 >= limit)                  -- REAL-ms deadline
    else
      over = (iters >= math.floor(limit / 15))       -- fallback: no getTickCount
    end
    if st ~= STATUS_DONE and over then
      if st == nil then
        return false, 'the mailbox could not be read -- the game process has ' ..
          'most likely exited (if it is running, re-inject UE5Dumper.dll)'
      end
      -- Name the time that ACTUALLY elapsed when a clock is available, so the number
      -- reports the arm that fired rather than always echoing `limit`.
      local shownMs = tick and (tick() - t0) or limit
      if st == STATUS_IDLE then
        return false, string.format(
          'Mailbox timeout after %dms -- the DLL never picked this up ' ..
          '(stale g_invokeMailbox address? re-inject, or re-enable the table)', shownMs)
      end
      -- The DLL never clears errorMsg: its pickup sets status = PROCESSING and
      -- leaves the field alone (dll/src/Mimic.cpp), and only a FAILURE writes it.
      -- So this read returns the PREVIOUS command's message unless the caller wiped
      -- it -- which invokeUFunction now does before every send. Keep the empty-string
      -- arm anyway: `x or 'timeout'` does NOT fire for '' (an empty string is truthy
      -- in Lua), so the old fallback was unreachable. (audit #5 AA18)
      local err = readString(mb + OFF_ERR, 256)
      if err == nil or err == '' then err = 'no message from the DLL' end
      return false, string.format(
        'Mailbox timeout after %dms -- the DLL took the command but did not ' ..
        'finish it (status=%d, %s)', shownMs, st, err)
    end
  end
  return true
end

local function readErrMsg(mb)
  local s = readString(mb + OFF_ERR, 256)
  if s and #s > 0 then return s end
  return 'Unknown error'
end

-- ============================================================
-- Public API: invokeUFunction
-- ============================================================
-- Re-declaration guard so multiple AA scripts loading this helper
-- don't redefine functions and lose state.
if not invokeUFunction then

  --- Invoke a UFunction by class name + function name with baked params.
  ---
  --- Uses CMD_INVOKE_BY_NAME -- the DLL handles findInstance +
  --- findFunction in one mailbox round-trip.
  ---
  --- @param className string  e.g. 'PlayerCharacter' (must match a
  ---                          live, non-CDO instance's UClass name)
  --- @param funcName  string  e.g. 'AddMoney'
  --- @param parmsSize number  Total params buffer size in bytes
  ---                          (from the function metadata; zero-fill
  ---                          uses this to clear stale bytes)
  --- @param params    table   Array of param descriptors:
  ---                          { { name=..., type='int32',
  ---                              offset=0, value=1000 }, ... }
  ---                          See writeBakedParams for supported types.
  --- @return boolean ok       True on success
  --- @return string|nil err   Error message on failure (nil on success)
  function invokeUFunction(className, funcName, parmsSize, params)
    -- Input validation BEFORE the busy check so bad args from a
    -- concurrent caller surface as the real validation error
    -- instead of getting hidden by a busy state from someone else.
    if type(className) ~= 'string' or #className == 0 then
      return false, 'className must be a non-empty string'
    end
    if type(funcName) ~= 'string' or #funcName == 0 then
      return false, 'funcName must be a non-empty string'
    end
    parmsSize = parmsSize or 0
    if parmsSize < 0 or parmsSize > 1024 then
      return false, string.format(
        'parmsSize %d out of range (0..1024)', parmsSize)
    end

    -- Reentrancy guard: refuse to touch the mailbox if another
    -- invoke is mid-flight in this Lua engine. Returning a clean
    -- 'busy' error beats silently corrupting the in-flight call.
    -- A previous call that TIMED OUT left the flag set on purpose: the DLL had taken
    -- the command and not finished, so the mailbox was still its. Ask the DLL whether
    -- it has finished since, rather than latching this Lua-local boolean for the rest
    -- of the session -- it publishes status and cmd itself. (audit #5 AA19)
    if _ue5_invoke_busy and _ue5_invoke_stale_mb then
      local st  = readInteger(_ue5_invoke_stale_mb + OFF_STATUS)
      local cmd = readInteger(_ue5_invoke_stale_mb + OFF_CMD)
      if st == STATUS_DONE and cmd == CMD_IDLE then
        _ue5_invoke_busy, _ue5_invoke_stale_mb = false, nil
      end
    end

    if _ue5_invoke_busy then
      if _ue5_invoke_stale_mb then
        return false,
          '[ue5_invoke] the previous invoke timed out and the DLL is STILL holding ' ..
          'the mailbox -- sending now would overwrite the class/function/params of a ' ..
          'call that is mid-flight. Wait for the game thread to come back (this ' ..
          'clears itself once the DLL reports done), or re-inject if it never does.'
      end
      return false,
        '[ue5_invoke] busy -- another script is mid-call. ' ..
        'Serialize your AA Scripts or guard with synchronize().'
    end

    -- Set busy = true around the mailbox-touching body. The pcall
    -- wrapper ensures the flag is ALWAYS cleared, even if any
    -- write/read throws (e.g. mailbox address turned invalid).
    _ue5_invoke_busy = true
    local pok, ok_or_err, err_or_nil = pcall(function()
      -- Reclaim the PREVIOUS invoke's FString buffers before this one allocates more.
      -- The mailbox is synchronous, so by the time a NEW invoke starts (we are past the
      -- busy check, so the prior call finished) its ProcessEvent has long returned and
      -- any well-behaved callee that kept the string deep-copied it (FString assignment
      -- allocates its own storage). Freeing HERE -- not on completion -- gives maximum
      -- settle time and bounds the leak to a single invoke's worth: a one-shot cheat
      -- still leaks the same few small buffers (there is no next invoke), which was
      -- always the safe default, while a repeated invoke no longer accumulates
      -- unbounded. The opt-in freeInvokeStringBuffers() remains for the read-only fast
      -- path. NOT freed on the timeout/refusal path -- see the busy guard above: while
      -- the DLL still owns the mailbox its in-flight buffers must not be reclaimed.
      -- (audit #5 AA32)
      if _ue5_invoke_str_bufs then
        for _, a in ipairs(_ue5_invoke_str_bufs) do
          if a and a ~= 0 then deAlloc(a) end
        end
      end
      _ue5_invoke_str_bufs = {}

      local ok_mb, mb = pcall(findMailbox)
      if not ok_mb then
        return false, tostring(mb)
      end

      -- Marshal the request into the mailbox.
      writeMbStr(mb, OFF_CLASS, className)
      writeMbStr(mb, OFF_FUNC, funcName)
      local ok_p, err_p = pcall(writeBakedParams, mb, parmsSize, params)
      if not ok_p then
        return false, tostring(err_p)
      end

      -- Wipe the DLL's error field before sending. Nothing else does: the DLL's
      -- pickup sets status = PROCESSING and leaves errorMsg untouched, so a timeout
      -- would report whatever the LAST failure left there as THIS command's reason --
      -- the guessed diagnosis CLAUDE.md forbids. One NUL is enough; the DLL writes
      -- from byte 0 whenever it has something real to say. (audit #5 AA18)
      writeByte(mb + OFF_ERR, 0)

      -- Clear status, then write CMD last to trigger the DLL.
      writeInteger(mb + OFF_STATUS, 0)
      writeInteger(mb + OFF_CMD, CMD_INVOKE_BY_NAME)

      -- Poll until the DLL's mailbox handler reports done.
      local ok_w, err_w = waitDone(mb, DEFAULT_TIMEOUT_MS)
      if not ok_w then
        -- Remember that the mailbox may still be the DLL's. The flag was cleared
        -- unconditionally below, INCLUDING here -- so the next invoke overwrote
        -- className / funcName / params underneath an in-flight ProcessEvent and
        -- was reported OK though it never ran. (audit #5 AA19)
        _ue5_invoke_stale_mb = mb
        return false, err_w
      end

      local result = readInteger(mb + OFF_RESULT, true)   -- signed: rc is int32
      if result ~= 0 then
        return false, string.format(
          '%s::%s -> result=%d (%s)',
          className, funcName, result, readErrMsg(mb))
      end

      return true
    end)
    -- Released only when the mailbox is ours again. A timeout sets
    -- _ue5_invoke_stale_mb above and the guard stays up until the DLL publishes
    -- status=DONE / cmd=IDLE, which the entry check re-tests on the next call.
    if not _ue5_invoke_stale_mb then
      _ue5_invoke_busy = false
    end

    if not pok then
      -- Hard error inside the body (raised, not returned). pcall
      -- captured the message as ok_or_err.
      return false, tostring(ok_or_err)
    end
    -- Soft return path: inner function returned (ok, err)
    return ok_or_err, err_or_nil
  end

  registerLuaFunctionHighlight('invokeUFunction')
end

-- ============================================================
-- Public API: readUFunctionReturn
-- ============================================================
if not readUFunctionReturn then

  --- Read a return value (or out-param) from the params buffer
  --- after a successful invokeUFunction call.
  ---
  --- @param offset    number  Byte offset within params_data
  ---                          (typically the function's return-value
  ---                          offset from UFunction metadata)
  --- @param valueType string  One of: 'int32' (default, SIGNED), 'int16'
  ---                          (SIGNED), 'uint32'/'dword', 'uint16'/'word',
  ---                          'float', 'double', 'bool', 'byte',
  ---                          'uint64'/'qword', 'int64'.
  ---                          'int64' reads the same eight bytes as 'qword';
  ---                          Lua integers are 64-bit two's complement, so the
  ---                          value is already signed. It is spelled separately
  ---                          because the generator emits that word for
  ---                          Int64Property and for 8-byte EnumProperty, and an
  ---                          unrecognised spelling silently falls through to
  ---                          the 4-byte default. [RETINT64-2026-08-24]
  ---                          The signed spellings are the ones a UFunction
  ---                          return value normally wants: CE reads unsigned
  ---                          unless told otherwise, so -1 used to come back
  ---                          as 4294967295. The unsigned spellings are kept
  ---                          for callers that want the raw magnitude.
  --- @return number|nil       The decoded value, or nil if the
  ---                          mailbox cannot be located
  function readUFunctionReturn(offset, valueType)
    local ok_mb, mb = pcall(findMailbox)
    if not ok_mb then return nil end

    local addr = mb + OFF_PARAMS + (offset or 0)

    if valueType == 'float' then
      return readFloat(addr)
    elseif valueType == 'double' then
      return readDouble(addr)
    elseif valueType == 'bool' or valueType == 'byte' then
      return readByte(addr)
    elseif valueType == 'uint64' or valueType == 'qword' or valueType == 'int64' then
      -- 'int64' MUST be here and not in the int32 default below. It was missing, so an
      -- Int64Property return -- and an 8-byte EnumProperty -- fell through to the signed
      -- FOUR-byte read and came back truncated: 0x0000000123456789 read as 591751049.
      -- That is the same defect build 637 fixed for pointers; its one-line fix
      -- (BakedScriptGenerator.cs:331) rewrote only "pointer" -> 'qword' and left "int64"
      -- to reach here verbatim. UInt64Property was unaffected because it maps to
      -- "pointer". [RETINT64-2026-08-24]
      --
      -- No sign fixing is needed, and that is a property of the WIDTH, not an oversight:
      -- Lua integers are 64-bit two's complement, so the eight bytes CE hands back ARE
      -- the signed value already (0xFFFFFFFFFFFFFFFF reads as -1). Contrast the 32-bit
      -- case below, where CE widens 4 bytes into a positive Lua number and the `signed`
      -- flag genuinely changes the result. At 64 bits 'int64' and 'uint64' read the same
      -- bits; only the caller's format specifier (%d vs 0x%X) differs.
      return readQword(addr)
    elseif valueType == 'int16' then
      return readSmallInteger(addr, true)          -- SIGNED
    elseif valueType == 'word' or valueType == 'uint16' then
      return readSmallInteger(addr)                -- unsigned, as asked
    elseif valueType == 'uint32' or valueType == 'dword' then
      return readInteger(addr)                     -- unsigned, as asked
    else
      -- Default: int32, SIGNED.
      --
      -- CE's readInteger/readSmallInteger interpret the bytes as UNSIGNED unless the
      -- second argument is true, so a UFunction returning -1 read back as 4294967295
      -- (or 65535 for an int16) -- while this same file already passes the flag for
      -- the mailbox result code two functions up. The unsigned spellings above keep
      -- working for callers that genuinely want the raw magnitude. (audit #5 AA20)
      return readInteger(addr, true)
    end
  end

  registerLuaFunctionHighlight('readUFunctionReturn')
end

-- ============================================================
-- Public API: freeInvokeStringBuffers
-- ============================================================
if not freeInvokeStringBuffers then

  --- Free every target-process buffer allocated for FString/FUtf8String/
  --- FAnsiString INPUT params by prior invokeUFunction calls.
  ---
  --- UNSAFE if any invoked function retained the string pointer (use-after-
  --- free) -- only call when every such call merely READ the string. The
  --- default is to leak (safe); this is the opt-in cleanup.
  --- @return number freed  Count of buffers released.
  function freeInvokeStringBuffers()
    local freed = 0
    if _ue5_invoke_str_bufs then
      for _, a in ipairs(_ue5_invoke_str_bufs) do
        if a and a ~= 0 then deAlloc(a); freed = freed + 1 end
      end
    end
    _ue5_invoke_str_bufs = {}
    return freed
  end

  registerLuaFunctionHighlight('freeInvokeStringBuffers')
end

-- ============================================================
-- Public API: Debug Camera robust force on/off
-- ============================================================
-- Goes through the SAME single-slot mailbox as invokeUFunction (the
-- proven CE<->DLL channel) -- NOT executeCodeEx, which doesn't reliably
-- return the export's int result (observed: state=nil). The DLL handler
-- (CMD_SET_DEBUG_CAMERA=7) owns the whole toggle + controller-swap
-- fallback, so the UI (pipe) and CE Lua (here) share one implementation.
-- Returns the resulting state: 1 = ON, 0 = OFF, -1 = error/unknown.
if not setDebugCamera then

  local CMD_SET_DEBUG_CAMERA = 7

  -- req: 0 = OFF, 1 = ON, 2 = query (read state, no change).
  -- Reuses the file-local mailbox helpers + reentrancy guard.
  local function dbgCamMailbox(req)
    if _ue5_invoke_busy then
      error('[ue5_invoke] busy -- another mailbox call is mid-flight')
    end
    _ue5_invoke_busy = true
    local pok, res = pcall(function()
      local mb = findMailbox()
      writeQword(mb + OFF_INSTANCE, req)   -- 0x010: request (0/1/2)
      writeInteger(mb + OFF_STATUS, 0)     -- clear status
      writeInteger(mb + OFF_CMD, CMD_SET_DEBUG_CAMERA)  -- trigger (write LAST)
      local ok_w, err_w = waitDone(mb, DEFAULT_TIMEOUT_MS)
      if not ok_w then error(err_w) end
      return readInteger(mb + OFF_RESULT, true)  -- 0x008: resulting state (signed int32)
    end)
    _ue5_invoke_busy = false
    if not pok then error(tostring(res)) end
    return res
  end

  --- Force Debug Camera ON (enable ~= 0) or OFF. Idempotent.
  --- @param enable number|boolean
  --- @return number state  1=ON, 0=OFF, -1=error
  function setDebugCamera(enable)
    return dbgCamMailbox((enable and enable ~= 0) and 1 or 0)
  end
  registerLuaFunctionHighlight('setDebugCamera')

  --- Read the live Debug Camera state without changing it.
  --- @return number state  1=ON, 0=OFF, -1=unknown
  function getDebugCameraState()
    local ok, state = pcall(dbgCamMailbox, 2)
    return ok and state or -1
  end
  registerLuaFunctionHighlight('getDebugCameraState')

end

-- ============================================================
-- Sentinel (visible in CE Lua engine after first load)
-- ============================================================
-- Gated on UE5_DEBUG so loading the helper does not pop the Lua Engine window
-- over Cheat Engine. Set UE5_DEBUG=1 in CE's Lua console to see the load banner.
if (UE5_DEBUG or 0) ~= 0 then
  print(string.format('[*] ue5_invoke_helper.lua v%s loaded',
                      UE5_INVOKE_HELPER_VERSION))
end
