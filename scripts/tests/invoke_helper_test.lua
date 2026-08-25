--[[
  invoke_helper_test.lua
  UE5CEDumper -- executable tests for scripts/ue5_invoke_helper.lua

  WHY THIS EXISTS
    Audit #5 filed AA14-AA20 against this file, all on one path: the mailbox
    round-trip that CE Lua uses to call a UFunction in the game. Every one of them
    is a state the C# suite cannot reach -- it can only assert on this file's
    SOURCE TEXT -- and several are about what gets STAMPED INTO A LIVE GAME'S
    parameter buffer, which is the last place to be guessing.

    This harness models the mailbox as plain Lua tables, stubs the CE globals the
    helper touches, and installs a fake "DLL" the test drives: it can answer
    immediately, never pick the command up, take it and wedge, or fail with a
    message. The helper's real functions then run against it.

  RUNNING IT
      lua scripts/tests/invoke_helper_test.lua
    Exit 0 = all pass, 1 = a failure (with the case named).

  THE TRAP THAT SHAPES THIS FILE
    ue5_invoke_helper.lua defines GLOBALS behind `if not invokeUFunction then`
    re-declaration guards, so a second load is a NO-OP -- a rig cannot reload
    between cases to get a clean slate. State is reset by hand instead
    (resetWorld clears _ue5_invoke_busy and _ue5_invoke_str_bufs). It also means
    writeParams / writeFStringInline / writeBakedParams / waitDone are file-LOCALS
    and unreachable from here: everything is driven through invokeUFunction, which
    is what a user actually calls anyway.

  DELIBERATELY NOT WIRED INTO build.ps1 OR CI
    Same reasoning as its two siblings -- a standalone `lua` is not a declared
    dependency, and a test step that silently skips when its tool is missing is
    the defect audit #5's AD1/AD2 fixed in the C++ test phase.
]]

local HELPER = (arg and arg[0] or ''):gsub('[^/\\]*$', '') .. '../ue5_invoke_helper.lua'

-- ============================================================
-- Mailbox model
-- ============================================================
local MB        = 0x10000000        -- fake g_invokeMailbox base
local CONTRACT  = 0x20000000        -- fake g_mailboxContract base
local CONTRACT_MAGIC = 1127564629

local OFF_CMD, OFF_STATUS, OFF_RESULT = 0x000, 0x004, 0x008
local OFF_CLASS, OFF_FUNC, OFF_ERR, OFF_PARAMS = 0x028, 0x128, 0x228, 0x328
local PARAMS_BYTES = 1024           -- sizeof(MailboxData::paramsData), Mimic.h:276

local I32, U64, BYTES, STR   -- int32 / qword / byte / string stores, by address
local ALLOCS, ALLOC_NEXT, ALLOC_FAIL
local PRINTS, TICK
local DLL                   -- function(cmd) -> called when CE writes OFF_CMD

local function resetWorld()
  I32, U64, BYTES, STR = {}, {}, {}, {}
  ALLOCS, ALLOC_NEXT, ALLOC_FAIL = {}, 0x50000, false
  PRINTS, TICK = {}, 0
  -- The helper's own globals. The re-declaration guards mean the chunk cannot be
  -- reloaded, so these are reset by hand -- missing one leaks state between cases.
  _ue5_invoke_busy = false
  _ue5_invoke_str_bufs = {}
  -- A live DLL: takes the command and answers success immediately.
  DLL = function() I32[MB + OFF_RESULT] = 0; I32[MB + OFF_STATUS] = 1 end
  -- Contract block: magic, current, min.
  I32[CONTRACT + 0x00] = CONTRACT_MAGIC
  I32[CONTRACT + 0x04] = 2
  I32[CONTRACT + 0x08] = 1
end

-- ============================================================
-- Cheat Engine stubs
-- ============================================================
local SYMBOLS = {}

function getAddressSafe(name)
  return SYMBOLS[name]
end

function readInteger(a, signed)
  local v = I32[a]
  if v == nil then return nil end
  -- CE reads 4 bytes; `signed` selects the interpretation. Modelling BOTH arms is
  -- the point of AA20 -- a stub that always returned the signed value would make
  -- the unsigned bug invisible.
  if signed then
    if v >= 0x80000000 then return v - 0x100000000 end
    return v
  end
  if v < 0 then return v + 0x100000000 end
  return v
end

function readSmallInteger(a, signed)
  local v = I32[a]
  if v == nil then return nil end
  if signed then
    if v >= 0x8000 then return v - 0x10000 end
    return v
  end
  if v < 0 then return v + 0x10000 end
  return v
end

function readQword(a)  return U64[a] end
function readFloat(a)  return U64[a] end
function readDouble(a) return U64[a] end
function readByte(a)   return BYTES[a] end
function readString(a)
  -- CE reads bytes until a NUL. The helper clears the error field by writing ONE
  -- zero byte, so a stub that only consulted the string store would report a wiped
  -- message as still present -- and the AA18 fix would look broken.
  if BYTES[a] == 0 then return '' end
  return STR[a]
end

function writeInteger(a, v)
  I32[a] = v
  if a == MB + OFF_CMD and v ~= 0 and DLL then DLL(v) end
end
function writeSmallInteger(a, v) I32[a] = v end
function writeQword(a, v)        U64[a or 0] = v or 0 end
function writeFloat(a, v)        U64[a] = v end
function writeDouble(a, v)       U64[a] = v end
function writeByte(a, v)         BYTES[a] = v end

-- CE does NOT raise on a nil address. lua_toaddress falls through to
-- lua_tointeger for a non-string, and lua_tointeger(nil) is 0, so writeBytesEx
-- happily writes to address 0 (LuaHandler.pas). Modelling that as a raise -- the
-- first version of this rig did -- turns AA14's SILENT SUCCESS into a clean
-- failure and hides the defect the case exists to catch. Same for writeQword's
-- VALUE argument: an explicitly-passed nil occupies a stack slot, so the write
-- proceeds with 0 rather than being skipped.
function writeBytes(a, t)
  local base = a or 0
  for i = 1, #t do BYTES[base + i - 1] = t[i] end
end

function allocateMemory(size)
  if ALLOC_FAIL then return nil end
  local addr = ALLOC_NEXT
  ALLOC_NEXT = ALLOC_NEXT + size + 0x100
  ALLOCS[addr] = size
  return addr
end
function deAlloc(a) ALLOCS[a] = nil end

function sleep(_) TICK = TICK + 16 end   -- CE's sleep(1) really measures ~15.5 ms
function processMessages() end
function processMessagesPaintOnly() end
function getTickCount() return TICK end
function registerLuaFunctionHighlight(_) end
function synchronize(f) if type(f) == 'function' then f() end end

local realPrint = print
function print(s) PRINTS[#PRINTS + 1] = tostring(s) end

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
-- Load the helper (globals; no return value -- see the header)
-- ============================================================
resetWorld()
SYMBOLS['g_invokeMailbox']    = MB
SYMBOLS['g_mailboxContract']  = CONTRACT

local chunk, err = loadfile(HELPER)
if not chunk then realPrint('cannot load ue5_invoke_helper.lua: ' .. tostring(err)); os.exit(1) end
chunk()
if type(invokeUFunction) ~= 'function' then
  realPrint('ue5_invoke_helper.lua did not define invokeUFunction'); os.exit(1)
end

--- Count non-zero bytes in the params region, [from, to).
local function nonZeroParams(from, to)
  local n = 0
  for i = from, to - 1 do
    local b = BYTES[MB + OFF_PARAMS + i]
    if b ~= nil and b ~= 0 then n = n + 1 end
  end
  return n
end

--- Fill the params region with a recognisable pattern, as a previous command would.
local function dirtyParams()
  for i = 0, PARAMS_BYTES - 1 do BYTES[MB + OFF_PARAMS + i] = 0xAB end
end

-- ============================================================
-- Baseline
-- ============================================================

case('happy path: a simple int32 param invoke succeeds')
do
  resetWorld()
  local ok, e = invokeUFunction('PlayerCharacter', 'AddMoney', 8,
                                { { name = 'amount', type = 'int32', offset = 0, value = 1000 } })
  eq(ok, true, 'the invoke reports success')
  eq(e, nil, 'and no error')
  eq(I32[MB + OFF_PARAMS], 1000, 'the param landed at offset 0')
  eq(_ue5_invoke_busy, false, 'the busy flag is released')
end

-- ============================================================
-- AA14 / AA15 -- a failed allocateMemory must not ship a null FString
-- ============================================================

case('AA14: a failed allocateMemory refuses the invoke instead of shipping Data=nullptr')
do
  -- The params buffer holds the FString BY VALUE: { CharT* Data; int32 Num; int32 Max }.
  -- With allocateMemory returning nil and nothing checking it, the helper stamped
  -- Data = nil (0) while still writing Num = Max = #s+1 -- an FString claiming n+1
  -- characters at address 0, handed to a live UFunction.
  resetWorld()
  ALLOC_FAIL = true
  local ok, e = invokeUFunction('C', 'F', 16,
                                { { name = 's', type = 'fstring', offset = 0, value = 'hello' } })
  eq(ok, false, 'the invoke is refused')
  contains(e, 'allocate', 'and says the allocation failed')
  -- The decisive assertion: no length was published for a buffer that does not exist.
  eq(I32[MB + OFF_PARAMS + 8], nil, 'ArrayNum was never written')
  eq(U64[MB + OFF_PARAMS], nil, 'Data was never written')
  eq(_ue5_invoke_busy, false, 'and the busy flag is still released')
end

case('AA14: the same holds for the narrow (FUtf8String / FAnsiString) branch')
do
  resetWorld()
  ALLOC_FAIL = true
  local ok, e = invokeUFunction('C', 'F', 16,
                                { { name = 's', type = 'fstringn', offset = 0, value = 'hi' } })
  eq(ok, false, 'refused')
  contains(e, 'allocate', 'names the allocation')
  eq(I32[MB + OFF_PARAMS + 8], nil, 'ArrayNum was never written')
end

case('AA14: a SUCCESSFUL string param still writes Data / Num / Max')
do
  resetWorld()
  local ok = invokeUFunction('C', 'F', 16,
                             { { name = 's', type = 'fstring', offset = 0, value = 'hi' } })
  eq(ok, true, 'succeeds')
  check(U64[MB + OFF_PARAMS] ~= nil and U64[MB + OFF_PARAMS] ~= 0, 'Data points somewhere')
  eq(I32[MB + OFF_PARAMS + 8], 3, 'ArrayNum = #s + 1')
  eq(I32[MB + OFF_PARAMS + 12], 3, 'ArrayMax = #s + 1')
end

-- ============================================================
-- AA16 -- param-type tokens the C# generator can emit
-- ============================================================

case('AA16: the tokens BakedScriptGenerator emits do not abort the whole invoke')
do
  -- MapToHelperType can emit ftext / tarray / tmap / tset / delegate, and
  -- writeParams accepted none of them: the error propagated out of
  -- writeBakedParams and the ENTIRE invoke was refused, so a UFunction with (say)
  -- one TArray input could not be called at all from an exported script.
  for _, t in ipairs({ 'tarray', 'tmap', 'tset', 'delegate' }) do
    resetWorld()
    local ok, e = invokeUFunction('C', 'F', 32,
                                  { { name = 'p', type = t, offset = 0, size = 16, value = 0 },
                                    { name = 'n', type = 'int32', offset = 16, value = 7 } })
    eq(ok, true, t .. ': the invoke still runs')
    eq(e, nil, t .. ': with no error')
    eq(I32[MB + OFF_PARAMS + 16], 7, t .. ': the following scalar param still lands')
  end
end

case('AA16: an empty container param is left as all-zero bytes, which IS its empty value')
do
  resetWorld()
  dirtyParams()
  local ok = invokeUFunction('C', 'F', 16,
                             { { name = 'arr', type = 'tarray', offset = 0, size = 16, value = 0 } })
  eq(ok, true, 'runs')
  eq(nonZeroParams(0, 16), 0, 'the TArray region is zeroed: {Data=null, Num=0, Max=0}')
end

case('AA16: ftext is REFUSED, and says why')
do
  -- Deliberately not treated like the containers. An all-zero FText is not an
  -- empty FText -- it holds a TSharedRef the engine dereferences -- so passing one
  -- is a crash, and silently zeroing it would be worse than refusing.
  resetWorld()
  local ok, e = invokeUFunction('C', 'F', 24,
                                { { name = 't', type = 'ftext', offset = 0, size = 24, value = 0 } })
  eq(ok, false, 'refused')
  contains(e, 'ftext', 'the message names the type')
end

case('AA16: a genuinely unknown token is still refused')
do
  resetWorld()
  local ok, e = invokeUFunction('C', 'F', 8,
                                { { name = 'x', type = 'wat', offset = 0, value = 1 } })
  eq(ok, false, 'refused')
  contains(e, 'wat', 'the message names the token')
end

-- ============================================================
-- AA17 -- the params buffer must be zeroed to the DLL's view, not the caller's
-- ============================================================

case('AA17: the WHOLE 1024-byte params buffer is cleared, not just parmsSize')
do
  -- Mimic.cpp:585-588 passes sizeof(paramsData) == 1024 to UE5_CallProcessEventEx,
  -- so EnqueueInvoke copies all 1024 bytes into the request it owns. Zeroing only
  -- the caller's parmsSize left every byte past it holding the PREVIOUS command's
  -- data -- and if that parmsSize is smaller than the UFunction's real ParmsSize
  -- (stale metadata, or a generator that counted only the params it knew), those
  -- stale bytes are read as live parameters.
  resetWorld()
  dirtyParams()
  local ok = invokeUFunction('C', 'F', 8,
                             { { name = 'n', type = 'int32', offset = 0, value = 42 } })
  eq(ok, true, 'runs')
  eq(I32[MB + OFF_PARAMS], 42, 'the real param is there')
  eq(nonZeroParams(8, PARAMS_BYTES), 0, 'every byte past parmsSize is zero')
end

case('AA17: a zero-param invoke still clears the buffer')
do
  resetWorld()
  dirtyParams()
  local ok = invokeUFunction('C', 'F', 0, nil)
  eq(ok, true, 'runs')
  eq(nonZeroParams(0, PARAMS_BYTES), 0, 'nothing stale survives into a no-param call')
end

-- ============================================================
-- AA18 -- a timeout must not report an older command's error
-- ============================================================

case('AA18: a timeout does NOT report the previous command\'s error message')
do
  -- Mimic.cpp:253-255 sets status = PROCESSING on pickup and never clears errorMsg;
  -- nothing else zeroes it either. So the timeout branch read whatever the LAST
  -- failure left there and presented it as this command's reason -- the guessed
  -- diagnosis CLAUDE.md forbids.
  resetWorld()
  STR[MB + OFF_ERR] = 'Instance not found: OldClass'   -- left by an earlier command
  DLL = function() I32[MB + OFF_STATUS] = 2 end        -- takes it, then wedges
  local ok, e = invokeUFunction('C', 'F', 0, nil)
  eq(ok, false, 'the invoke times out')
  contains(e, 'timeout', 'and says so')
  check(not e:find('OldClass', 1, true), 'the stale message is NOT quoted as the reason', e)
end

case('AA18: a FRESH error from this command IS reported')
do
  resetWorld()
  DLL = function()
    STR[MB + OFF_ERR] = 'Function not found: Nope'
    I32[MB + OFF_RESULT] = -3
    I32[MB + OFF_STATUS] = 1
  end
  local ok, e = invokeUFunction('C', 'Nope', 0, nil)
  eq(ok, false, 'reports failure')
  contains(e, 'Nope', 'and carries the DLL\'s own message')
end

case('AA18: the never-picked-up case keeps its own distinct diagnosis')
do
  resetWorld()
  STR[MB + OFF_ERR] = 'stale junk'
  DLL = function() end                                  -- status stays IDLE
  local ok, e = invokeUFunction('C', 'F', 0, nil)
  eq(ok, false, 'times out')
  contains(e, 'never picked this up', 'status IDLE is diagnosed as a stale mailbox address')
end

-- ============================================================
-- AA19 -- the reentrancy flag after a timeout
-- ============================================================

case('AA19: after a TIMEOUT the next invoke is refused -- the DLL still owns the mailbox')
do
  -- The flag was cleared unconditionally, including on the path where the DLL had
  -- taken the command and not finished. The next invoke then overwrote className /
  -- funcName / params underneath an in-flight ProcessEvent.
  resetWorld()
  DLL = function() I32[MB + OFF_STATUS] = 2 end         -- taken, wedged
  local ok1 = invokeUFunction('C', 'F', 0, nil)
  eq(ok1, false, 'the first invoke times out')

  local ok2, e2 = invokeUFunction('C', 'G', 0, nil)
  eq(ok2, false, 'the second is refused rather than scribbling on the first')
  contains(e2, 'STILL holding the mailbox', 'and says the previous call is unfinished')
  -- The FIRST call stamped 'C'/'F'; the refusal must not have replaced 'F' with 'G'.
  eq(BYTES[MB + OFF_FUNC], string.byte('F'),
     'the in-flight function name was NOT overwritten by the refused call')
end

case('AA19: once the DLL finishes, the next invoke proceeds')
do
  -- The refusal must not be a permanent latch. The DLL publishes its own state, so
  -- the next call asks IT rather than trusting a Lua-local boolean forever.
  resetWorld()
  DLL = function() I32[MB + OFF_STATUS] = 2 end
  eq(invokeUFunction('C', 'F', 0, nil), false, 'first times out')

  -- The DLL completes late: status DONE, cmd back to IDLE.
  I32[MB + OFF_STATUS] = 1
  I32[MB + OFF_CMD] = 0
  DLL = function() I32[MB + OFF_RESULT] = 0; I32[MB + OFF_STATUS] = 1 end

  local ok2, e2 = invokeUFunction('C', 'G', 0, nil)
  eq(ok2, true, 'the next invoke runs')
  eq(e2, nil, 'with no error')
end

case('AA19: a normal SUCCESS still leaves the helper ready for the next call')
do
  resetWorld()
  eq(invokeUFunction('C', 'F', 0, nil), true, 'first succeeds')
  eq(invokeUFunction('C', 'G', 0, nil), true, 'second succeeds')
  eq(_ue5_invoke_busy, false, 'not latched')
end

-- ============================================================
-- AA20 -- signed return decoding
-- ============================================================

case('AA20: an int32 return of -1 reads as -1, not 4294967295')
do
  resetWorld()
  I32[MB + OFF_PARAMS + 8] = -1
  eq(readUFunctionReturn(8, 'int32'), -1, 'int32 is decoded SIGNED')
  eq(readUFunctionReturn(8), -1, 'and so is the default (no type given)')
end

case('AA20: an int16 return of -1 reads as -1')
do
  resetWorld()
  I32[MB + OFF_PARAMS + 4] = -1
  eq(readUFunctionReturn(4, 'int16'), -1, 'int16 is decoded SIGNED')
end

case('AA20: the UNSIGNED spellings still read unsigned')
do
  -- The fix must not make everything signed: 'uint32' / 'word' are distinct
  -- requests, and a caller asking for one wants the raw magnitude.
  resetWorld()
  I32[MB + OFF_PARAMS] = -1
  eq(readUFunctionReturn(0, 'uint32'), 4294967295, 'uint32 stays unsigned')
  eq(readUFunctionReturn(0, 'word'), 65535, 'word stays unsigned')
end

case('AA20: a positive value is unaffected either way')
do
  resetWorld()
  I32[MB + OFF_PARAMS] = 1234
  eq(readUFunctionReturn(0, 'int32'), 1234, 'positive int32')
  eq(readUFunctionReturn(0, 'uint32'), 1234, 'positive uint32')
end

-- ============================================================
-- AA31: the Debug Camera SAMPLE must untick the record on the error path.
--
-- CE compiles each {$lua} block as its own chunk in one shared state; extract the
-- header's sample blocks the way CE's autoassembler splits them (line-EXACT
-- {$LUA}/{$ASM} markers, not a substring search), then RUN the [ENABLE] block.
-- Asserting on the doc's text would be tautological.
-- ============================================================

local function extractSampleBlocks(path)
  local blocks, body = {}, nil
  for line in io.lines(path) do
    local t = line:match('^%s*(.-)%s*$'):upper()
    if t == '{$LUA}' then
      body = {}
    elseif t == '{$ASM}' then
      if body then blocks[#blocks + 1] = table.concat(body, '\n') end
      body = nil
    elseif body then
      body[#body + 1] = line
    end
  end
  return blocks
end

--- Run one block as CE does: its own chunk in the shared state, with syntaxcheck
--- and memrec injected as chunk locals (autoassembler prepends the local + varargs).
local function runAsCeChunk(body, memrec)
  local chunk, e = load('local syntaxcheck,memrec=...\n' .. body, 'ce-block')
  if not chunk then return false, 'compile: ' .. tostring(e) end
  return pcall(chunk, false, memrec)
end

function showMessage(s) PRINTS[#PRINTS + 1] = 'showMessage: ' .. tostring(s) end

case('AA31: the Debug Camera [ENABLE] sample unticks the record when the call returns -1')
do
  resetWorld()
  DLL = function() I32[MB + OFF_RESULT] = -1; I32[MB + OFF_STATUS] = 1 end   -- DLL reports error
  local blocks = extractSampleBlocks(HELPER)
  check(#blocks >= 2, 'the header carries the [ENABLE]/[DISABLE] sample', '#blocks=' .. #blocks)
  local memrec = { Active = true }
  local ok, err = runAsCeChunk(blocks[1], memrec)
  check(ok, 'AA31: the [ENABLE] sample runs without raising', err)
  eq(memrec.Active, false, 'AA31: a -1 result unticked the record')
end

case('AA31: the sample unticks when the call RAISES (mailbox gone)')
do
  resetWorld()
  SYMBOLS['g_invokeMailbox'] = nil          -- findMailbox raises -> setDebugCamera raises
  local blocks = extractSampleBlocks(HELPER)
  local memrec = { Active = true }
  local ok = runAsCeChunk(blocks[1], memrec)
  check(ok, 'AA31: the sample catches the raise rather than propagating it')
  eq(memrec.Active, false, 'AA31: a raised error also unticked the record')
  SYMBOLS['g_invokeMailbox'] = MB           -- restore for later cases
end

case('AA31: a SUCCESSFUL enable (state 1) leaves the record ticked -- the control')
do
  resetWorld()
  DLL = function() I32[MB + OFF_RESULT] = 1; I32[MB + OFF_STATUS] = 1 end     -- camera ON
  local blocks = extractSampleBlocks(HELPER)
  local memrec = { Active = true }
  local ok = runAsCeChunk(blocks[1], memrec)
  check(ok, 'the sample runs')
  eq(memrec.Active, true, 'AA31: a real success keeps the record ticked')
end

-- ============================================================
-- AA32: FString buffers are reclaimed on the next invoke (bounded, not unbounded).
-- ============================================================

local function liveAllocs()
  local n = 0; for _ in pairs(ALLOCS) do n = n + 1 end; return n
end

case('AA32: string-param buffers are reclaimed on the next invoke, not accumulated')
do
  resetWorld()
  invokeUFunction('C', 'F', 16, { { name = 's', type = 'fstring', offset = 0, value = 'hello' } })
  eq(liveAllocs(), 1, 'one string buffer live after the first invoke')
  invokeUFunction('C', 'F', 16, { { name = 's', type = 'fstring', offset = 0, value = 'world' } })
  eq(liveAllocs(), 1, 'still one -- the previous buffer was reclaimed, not accumulated')
  for _ = 1, 10 do
    invokeUFunction('C', 'F', 16, { { name = 's', type = 'fstring', offset = 0, value = 'x' } })
  end
  check(liveAllocs() <= 1, 'AA32: bounded across many invokes', liveAllocs())
end

case('AA32: a subsequent NON-string invoke also reclaims the prior string buffer')
do
  resetWorld()
  invokeUFunction('C', 'F', 16, { { name = 's', type = 'fstring', offset = 0, value = 'hi' } })
  eq(liveAllocs(), 1, 'one buffer after the string invoke')
  invokeUFunction('C', 'G', 8, { { name = 'n', type = 'int32', offset = 0, value = 1 } })
  eq(liveAllocs(), 0, 'AA32: the next invoke reclaimed it even though it had no strings')
end

case('AA32: the opt-in freeInvokeStringBuffers() still frees the current set')
do
  resetWorld()
  invokeUFunction('C', 'F', 16, { { name = 's', type = 'fstring', offset = 0, value = 'hi' } })
  eq(liveAllocs(), 1, 'one buffer live')
  eq(freeInvokeStringBuffers(), 1, 'AA32: the manual free still reports one released')
  eq(liveAllocs(), 0, 'and it is gone')
end

-- ============================================================
-- AA33: a param write must not run past the params region / mailbox.
-- ============================================================

case('AA33: an int32 whose offset+width overruns the region is refused')
do
  resetWorld()
  local ok, e = invokeUFunction('C', 'F', 16, { { name = 'n', type = 'int32', offset = 14, value = 7 } })
  eq(ok, false, 'AA33: refused (14 + 4 > 16)')
  contains(e, 'params region', 'AA33: and says why')
  eq(I32[MB + OFF_PARAMS + 14], nil, 'AA33: nothing was written out of bounds')
end

case('AA33: an in-bounds param at the exact region edge still writes -- the control')
do
  resetWorld()
  local ok = invokeUFunction('C', 'F', 16, { { name = 'n', type = 'int32', offset = 12, value = 7 } })
  eq(ok, true, 'AA33: 12 + 4 = 16 fits')
  eq(I32[MB + OFF_PARAMS + 12], 7, 'AA33: and it landed')
end

case('AA33: an fstring whose 16-byte struct would overrun is refused (past the mailbox)')
do
  resetWorld()
  local ok = invokeUFunction('C', 'F', 1024,
                             { { name = 's', type = 'fstring', offset = 1016, value = 'hi' } })
  eq(ok, false, 'AA33: 1016 + 16 = 1032 > 1024 -> refused before writing past OFF_PARAMS+1024')
end

-- ============================================================
-- AA34: a MISSING string param must not become the literal "0".
-- ============================================================

case('AA34: a missing fstring value is an empty FString, not the literal "0"')
do
  resetWorld()
  local ok = invokeUFunction('C', 'F', 16, { { name = 's', type = 'fstring', offset = 0 } })  -- no value
  eq(ok, true, 'runs')
  eq(I32[MB + OFF_PARAMS + 8], 1, 'AA34: ArrayNum = 1 (empty + null), NOT 2 ("0" + null)')
  local dataPtr = U64[MB + OFF_PARAMS]
  check(dataPtr ~= nil and dataPtr ~= 0, 'Data points at a buffer')
  eq(BYTES[dataPtr], 0, 'AA34: the first char is the null terminator, not "0" (0x30)')
end

case('AA34: a provided string is still written verbatim -- the control')
do
  resetWorld()
  local ok = invokeUFunction('C', 'F', 16, { { name = 's', type = 'fstring', offset = 0, value = 'Hi' } })
  eq(ok, true, 'runs')
  eq(I32[MB + OFF_PARAMS + 8], 3, 'ArrayNum = #"Hi" + 1')
  local dataPtr = U64[MB + OFF_PARAMS]
  eq(BYTES[dataPtr], string.byte('H'), 'AA34: the real first char survives')
end

-- ============================================================

realPrint(string.format('\n%d checks, %d failure(s)', checks, failures))
os.exit(failures == 0 and 0 or 1)
