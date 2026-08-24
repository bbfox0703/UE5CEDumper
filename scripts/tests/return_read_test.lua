--[[
  return_read_test.lua
  UE5CEDumper -- executable test for the b637 pointer-return read width.

  WHY THIS EXISTS
    The register row for b637/b644 asks a human to open Cheat Engine, invoke a
    UFunction with "Verify return value" ticked, and check that "the line shows a
    0x prefix". That check CANNOT FAIL: both observables are compile-time string
    literals baked into the generated script (BakedScriptGenerator.cs:463 for
    '0x%X', :345 for 'see After: dump above'), and both are already pinned by C#
    unit tests that would go red long before a human saw the CE window. So it is
    a test of a constant, not of behaviour.

    Worse, it is blind to the defect build 637 actually FIXED. That defect is a
    READ WIDTH: the helper's readUFunctionReturn has no 'pointer' type, so the
    pre-fix spelling fell through to the int32 default and read FOUR bytes of an
    EIGHT-byte pointer slot -- signed, at that. The fix is one line,
    BakedScriptGenerator.cs:331:

        var readType = displayType == "pointer" ? "qword" : displayType;

    A 0x prefix appears either way. The truncation is the whole bug and the
    prescribed check cannot see it.

  WHAT THIS FILE DOES INSTEAD
    Runs the REAL readUFunctionReturn out of scripts/ue5_invoke_helper.lua
    against BYTE-ACCURATE stub memory, and compares 'qword' (post-fix) with the
    fall-through (pre-fix) on the same bytes.

  WHY THE MEMORY MODEL HAD TO BE REWRITTEN, and it is the point of the file
    invoke_helper_test.lua keeps I32 and U64 in SEPARATE stores keyed by address,
    which is right for what it tests but would make this test a tautology: a
    4-byte read would return nil (or an unrelated planted value) rather than the
    LOW HALF of the 8-byte value actually in memory, so "the pre-fix path
    truncates" would be modelled rather than measured. Here MEM holds bytes and
    every reader assembles from them, so the truncation is arithmetic on the same
    bytes -- exactly what CE does.

  RUNNING IT
      lua scripts/tests/return_read_test.lua      (from the repo root)
    Exit 0 = all pass, 1 = a failure (with the case named).

  DELIBERATELY NOT WIRED INTO build.ps1 OR CI
    Same reasoning as its three siblings: a standalone `lua` is not a declared
    dependency, and a test step that silently skips when its tool is missing is
    the defect audit #5's AD1/AD2 fixed on the C++ side.
]]

local HELPER = 'scripts/ue5_invoke_helper.lua'
if not io.open(HELPER, 'r') then
  HELPER = (arg and arg[0] or ''):gsub('[^/]*$', '') .. '../ue5_invoke_helper.lua'
end

local MB             = 0x10000000
local CONTRACT       = 0x20000000
local CONTRACT_MAGIC = 1127564629
local OFF_PARAMS     = 0x328          -- ue5_invoke_helper.lua:111
local RET_OFF        = 0              -- return slot at params+0

-- ============================================================
-- Byte-accurate memory
-- ============================================================
local MEM = {}

local function poke(addr, value, nbytes)
  for i = 0, nbytes - 1 do
    MEM[addr + i] = (value >> (8 * i)) & 0xFF
  end
end

local function assemble(addr, nbytes)
  local v = 0
  for i = 0, nbytes - 1 do
    local b = MEM[addr + i]
    if b == nil then return nil end
    v = v | (b << (8 * i))
  end
  return v
end

-- CE's readers, modelled on the same bytes.
function readQword(a) return assemble(a, 8) end

function readInteger(a, signed)
  local v = assemble(a, 4)
  if v == nil then return nil end
  if signed and v >= 0x80000000 then return v - 0x100000000 end
  return v
end

function readSmallInteger(a, signed)
  local v = assemble(a, 2)
  if v == nil then return nil end
  if signed and v >= 0x8000 then return v - 0x10000 end
  return v
end

function readByte(a)   return assemble(a, 1) end
function readFloat(a)  return assemble(a, 4) end
function readDouble(a) return assemble(a, 8) end
function readString(a) return '' end
function readBytes(a)  return assemble(a, 1) end

local SYMBOLS = {}
function getAddress(name)     return SYMBOLS[name] end
function getAddressSafe(name) return SYMBOLS[name] end
function registerLuaFunctionHighlight(_) end
function writeInteger(a, v)   poke(a, v & 0xFFFFFFFF, 4) end
function writeQword(a, v)     poke(a, v, 8) end
function writeBytes(a, v)     poke(a, v, 1) end
function writeString(_, _) end
function allocateMemory(_)    return 0x50000 end
function deAlloc(_) end
function getTickCount()       return 0 end
function sleep(_) end
function print_(...) end

SYMBOLS['g_invokeMailbox']   = MB
SYMBOLS['g_mailboxContract'] = CONTRACT
poke(CONTRACT + 0x00, CONTRACT_MAGIC, 4)
poke(CONTRACT + 0x04, 2, 4)
poke(CONTRACT + 0x08, 1, 4)

local chunk, err = loadfile(HELPER)
if not chunk then
  io.write('FATAL: cannot load ' .. HELPER .. ': ' .. tostring(err) .. '\n')
  os.exit(1)
end
chunk()

if type(readUFunctionReturn) ~= 'function' then
  io.write('FATAL: the helper did not define readUFunctionReturn\n')
  os.exit(1)
end

-- ============================================================
-- Cases
-- ============================================================
local FAILED, RUN = 0, 0

local function check(name, got, want)
  RUN = RUN + 1
  if got == want then
    io.write(('  PASS  %-58s %s\n'):format(name, tostring(got)))
  else
    FAILED = FAILED + 1
    io.write(('  FAIL  %-58s got %s, want %s\n')
             :format(name, tostring(got), tostring(want)))
  end
end

local ADDR = MB + OFF_PARAMS + RET_OFF

-- The generator's own format specifier for pointer-shaped returns
-- (BakedScriptGenerator.cs:463).
local FMT = '0x%X'

local function readBoth(ptr)
  poke(ADDR, ptr, 8)
  -- 'qword' is what the fix sends on the wire; 'pointer' is the spelling the
  -- helper does NOT recognise, so it falls through to the signed int32 default.
  return readUFunctionReturn(RET_OFF, 'qword'),
         readUFunctionReturn(RET_OFF, 'pointer')
end

io.write('\n-- 1. A REAL high UObject pointer (the everyday case on x64) ------\n')
local HIGH = 0x00007FF762E5AAA0      -- measured GObjects addr from a live DumperTest
local q, p = readBoth(HIGH)
check('qword read returns the whole 8-byte pointer', q, HIGH)
check("qword formats with every digit", FMT:format(q), '0x7FF762E5AAA0')
check('fall-through TRUNCATES to the low 4 bytes', p, 0x62E5AAA0)
check('...so the printed line is a DIFFERENT address', FMT:format(p), '0x62E5AAA0')
check('the two disagree (this is the b637 defect)', q ~= p, true)

io.write('\n-- 2. Low dword has its top bit set -> the SIGNED default goes NEGATIVE\n')
local NEG = 0x00007FF7A2E5AAA0
q, p = readBoth(NEG)
check('qword still exact', q, NEG)
check('fall-through is NEGATIVE, not just short', p < 0, true)
check('  its value', p, 0xA2E5AAA0 - 0x100000000)

io.write('\n-- 3. A LOW pointer -- both agree, which is WHY this survived --------\n')
local LOW = 0x0000000000400000
q, p = readBoth(LOW)
check('qword', q, LOW)
check('fall-through agrees on a sub-2GiB address', p, LOW)
check('  so a test using a low pointer proves NOTHING', q == p, true)

io.write('\n-- 4. Zero -- indistinguishable, so it cannot be the fixture --------\n')
q, p = readBoth(0)
check('both read 0', q == 0 and p == 0, true)

io.write('\n-- 5. int64 -- the SAME defect, for a type the b637 fix did not cover\n')
-- MapToHelperType (BakedScriptGenerator.cs:522) maps Int64Property -> "int64", and
-- the size-8 signed-int case (:519) does too. readType only rewrites "pointer", so
-- 'int64' reaches the helper VERBATIM -- and readUFunctionReturn has NO 'int64'
-- branch, so it falls through to the signed int32 default, exactly like the pre-b637
-- 'pointer' spelling did. [RETINT64-2026-08-24]
local BIG = 0x0000000123456789      -- needs 33 bits
poke(ADDR, BIG, 8)
local as_i64   = readUFunctionReturn(RET_OFF, 'int64')
local as_qword = readUFunctionReturn(RET_OFF, 'qword')
check("'int64' is not a branch, so it TRUNCATES", as_i64, 0x23456789)
check('  while qword is exact', as_qword, BIG)
check('  i.e. an Int64Property return reads WRONG', as_i64 ~= as_qword, true)

io.write(('\n%d checks, %d failed\n'):format(RUN, FAILED))
os.exit(FAILED == 0 and 0 or 1)
