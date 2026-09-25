--[[
  ct_ansi_path_test.lua
  UE5CEDumper -- [PATH-CT-INJECT-ANSI] UE5CEDumper.CT hands injectDLL the DLL path's ANSI bytes, or refuses.

  WHY
    CE's injectDLL copies its string into the game and calls LoadLibraryA; Lua's io.open is fopen. Both read the
    ANSI code page. The .CT's path sources are UTF-8 (the dll-path.txt breadcrumb, CE's dialogs, getCheatEngineDir),
    accepted by the fileExists fallback, and went to injectDLL unconverted: from a non-ASCII folder LoadLibraryA
    failed and CE manual-mapped the DLL instead. The size readout ([STALEDLL]) read "unknown", and the last-resort
    file picker dropped every non-ASCII pick.

  HOW
    The functions are lifted verbatim from scripts/UE5CEDumper.CT (XML entities unescaped) and run against stubs:
    a tiny fake ANSI code page (工具 <-> Big5 A4 75 A8 E3, best fit é -> e, ™ -> ?) stands in for CE's
    UTF8ToAnsi / ansiToUTF8, io.open opens only ANSI bytes, fileExists speaks UTF-8. Nothing outside the repo.

  RUNNING IT
      py tools/verify/ce_lua53_host.py            (runs every suite on CE's own lua53-64.dll)
      lua scripts/tests/ct_ansi_path_test.lua
    Exit 0 = all pass, 1 = a failure.
]]

local HERE = (arg and arg[0] or ''):gsub('[^/\\]*$', '')
local CT = HERE .. '../UE5CEDumper.CT'

local fails, checks = 0, 0
local function check(name, cond, got)
  checks = checks + 1
  if cond then print(string.format("  ok    %s", name))
  else fails = fails + 1; print(string.format("  FAIL  %s   got: %s", name, tostring(got))) end
end

local f = assert(io.open(CT, "rb"), "cannot open " .. CT)
local ct = f:read("a"); f:close()
local function unxml(s) return (s:gsub("&lt;", "<"):gsub("&gt;", ">"):gsub("&quot;", '"'):gsub("&amp;", "&")) end
local function lift(pattern, name)
  local src = ct:match(pattern)
  if not src then return nil, name .. " not found in the .CT" end
  local fn, err = load(unxml(src), name)
  if not fn then return nil, err end
  fn()
  return true
end

-- ---- the fake ANSI code page (CE's UTF8ToAnsi / ansiToUTF8 stand-ins) --------------------------------
local U_GONGJU, A_GONGJU = "\229\183\165\229\133\183", "\164u\168\227"      -- 工具 in UTF-8 / in Big5
local U_TM, U_EACUTE = "\226\132\162", "\195\169"                              -- ™, é in UTF-8
function UTF8ToAnsi(s)
  return (s:gsub(U_GONGJU, A_GONGJU):gsub(U_TM, "?"):gsub(U_EACUTE, "e"))
end
function ansiToUTF8(s) return (s:gsub(A_GONGJU, U_GONGJU)) end
ansiToUtf8, utf8ToAnsi = ansiToUTF8, UTF8ToAnsi

local P_UTF8 = "D:\\" .. U_GONGJU .. "\\UE5CEDumper\\UE5Dumper.dll"
local P_ANSI = "D:\\" .. A_GONGJU .. "\\UE5CEDumper\\UE5Dumper.dll"

-- io.open that opens only an ANSI path it knows (fopen reads the ANSI code page)
local realOpen = io.open
local function fakeOpen(p)
  if p == P_ANSI or p == "C:\\Tools\\UE5Dumper.dll" then
    return { seek = function() return 12345 end, close = function() end }
  end
  return nil, "No such file"
end

print("-- ue5_pathForms --")
local ok, err = lift("(function%s+ue5_pathForms.-\nend)", "ue5_pathForms")
check("ue5_pathForms is in the .CT", ok, err)
if ok then
  local a, u = ue5_pathForms("C:\\Tools\\UE5Dumper.dll", false)
  check("ASCII: both forms are the path", a == "C:\\Tools\\UE5Dumper.dll" and u == a, tostring(a))
  a, u = ue5_pathForms(P_UTF8, false)
  check("UTF-8, representable: ANSI = its exact bytes", a == P_ANSI, a)
  check("UTF-8, representable: the display form is the path", u == P_UTF8, u)
  a, u = ue5_pathForms("D:\\CE" .. U_TM .. "\\UE5Dumper.dll", false)
  check("UTF-8, not representable (TM): no ANSI form", a == nil, a)
  check("  ...and the display form survives", u == "D:\\CE" .. U_TM .. "\\UE5Dumper.dll", u)
  a = ue5_pathForms("D:\\Caf" .. U_EACUTE .. "\\UE5Dumper.dll", false)
  check("UTF-8, best fit (Cafe is not Cafe-acute's folder): no ANSI form", a == nil, a)
  a, u = ue5_pathForms(P_ANSI, true)
  check("found by io.open (ANSI): ANSI is the path", a == P_ANSI, a)
  check("found by io.open (ANSI): the display form is UTF-8", u == P_UTF8, u)
  a, u = ue5_pathForms(nil, false)
  check("nil in, nil out", a == nil and u == nil, tostring(a))
end

print("-- ue5_dllFileSize reads a UTF-8 non-ASCII path --")
ok, err = lift("(function%s+ue5_dllFileSize.-\nend)", "ue5_dllFileSize")
check("ue5_dllFileSize is in the .CT", ok, err)
if ok then
  io.open = fakeOpen
  local sz = ue5_dllFileSize(P_UTF8)
  io.open = realOpen
  check("size of a UTF-8 path is read through its ANSI form", sz == 12345, sz)
end

print("-- the last-resort file picker accepts a non-ASCII pick --")
ok, err = lift("(function%s+ue5_pickDllManually.-\nend)", "ue5_pickDllManually")
check("ue5_pickDllManually is in the .CT", ok, err)
if ok then
  local recorded
  function getMainForm() return {} end
  function createOpenDialog() return { Execute = function() return true end, FileName = P_UTF8,
                                        destroy = function() end } end
  function fileExists(p) return p == P_UTF8 end
  function extractFilePath(p) return (p:gsub("[^\\]*$", "")) end
  function ue5_recordDllDir(d) recorded = d end
  io.open = fakeOpen
  local picked = ue5_pickDllManually()
  io.open = realOpen
  check("a UTF-8 pick that fileExists confirms is taken", picked == P_UTF8, picked)
  check("  ...and its folder is recorded", recorded == "D:\\" .. U_GONGJU .. "\\UE5CEDumper\\", recorded)
end

print("-- the injection itself (the shipped text) --")
local body = unxml(ct)
check("injectDLL gets the ANSI form", body:find("injectDLL(DLL_PATH_ANSI)", 1, true) ~= nil)
check("injectDLL never gets the display form", body:find("injectDLL(DLL_PATH)", 1, true) == nil)
local refuse = body:find("if not DLL_PATH_ANSI then", 1, true)
local inject = body:find("injectDLL(DLL_PATH_ANSI)", 1, true)
check("a refusal comes before injectDLL", refuse ~= nil and inject ~= nil and refuse < inject, refuse)
if refuse and inject and refuse < inject then
  local block = body:sub(refuse, inject)
  check("  ...naming LoadLibraryA", block:find("LoadLibraryA", 1, true) ~= nil)
  check("  ...and the route that works", block:find("Inject into running game", 1, true) ~= nil)
  check("  ...and applying nothing (return false)", block:find("return false", 1, true) ~= nil)
end
check("every probe hit sets both forms", body:find("DLL_PATH_ANSI, DLL_PATH = ue5_pathForms(", 1, true) ~= nil)
check("a picked path gets its ANSI form",
      body:find("DLL_PATH_ANSI, DLL_PATH = ue5_pathForms(ue5_pickDllManually(), false)", 1, true) ~= nil)

-- ---- the skeptic review (wf_6ba4bc83-14d) ------------------------------------------------------------------

print("-- (T4) _dllAt says which probe found the file --")
local okA, errA = lift("(local function _dllAt.-\nend)", "_dllAt")
check("_dllAt is in the .CT", okA, errA)
-- _dllAt is a LOCAL in the .CT: re-lift it as a global to call it here.
local dllAtSrc = unxml(ct:match("(local function _dllAt.-\nend)") or ""):gsub("^local function _dllAt", "function _dllAt")
assert(load(dllAtSrc, "_dllAt-global"))()
function fileExists(p) return p == P_UTF8 end
io.open = fakeOpen
local hit, fromAnsi = _dllAt("D:\\" .. A_GONGJU .. "\\UE5CEDumper\\")
check("an io.open hit is the ANSI bytes, flagged fromAnsi", hit == P_ANSI and fromAnsi == true, tostring(fromAnsi))
hit, fromAnsi = _dllAt("D:\\" .. U_GONGJU .. "\\UE5CEDumper\\")
check("a fileExists hit is UTF-8, flagged not fromAnsi", hit == P_UTF8 and fromAnsi == false, tostring(fromAnsi))
io.open = realOpen

print("-- (T1) the probe loop itself sets both forms --")
local probeSrc = unxml(ct:match("(local function _probeSlots.-\nend)") or ""):gsub("^local function _probeSlots",
                                                                                  "function _probeSlots")
check("_probeSlots is in the .CT", probeSrc ~= "")
if probeSrc ~= "" then
  -- Its upvalues in the .CT (_slots, _dllAt, _dllFoundIn, _dllFoundLabel) resolve as globals here.
  assert(load(probeSrc, "_probeSlots"))()
  io.open = fakeOpen
  _slots = { { dir = "D:\\" .. U_GONGJU .. "\\UE5CEDumper\\", label = "breadcrumb" } }
  DLL_PATH, DLL_PATH_ANSI = nil, nil
  _probeSlots(1)
  check("a UTF-8 slot: DLL_PATH is the real path", DLL_PATH == P_UTF8, DLL_PATH)
  check("a UTF-8 slot: DLL_PATH_ANSI is its ANSI bytes", DLL_PATH_ANSI == P_ANSI, DLL_PATH_ANSI)
  _slots = { { dir = "D:\\" .. A_GONGJU .. "\\UE5CEDumper\\", label = "mru" } }
  DLL_PATH, DLL_PATH_ANSI = nil, nil
  _probeSlots(1)
  check("an ANSI slot: DLL_PATH is shown as UTF-8", DLL_PATH == P_UTF8, DLL_PATH)
  check("an ANSI slot: DLL_PATH_ANSI is the bytes", DLL_PATH_ANSI == P_ANSI, DLL_PATH_ANSI)
  _slots = { { dir = "C:\\Tools\\", label = "ascii" } }
  DLL_PATH, DLL_PATH_ANSI = nil, nil
  _probeSlots(1)
  check("an ASCII slot: both forms are the path", DLL_PATH == "C:\\Tools\\UE5Dumper.dll" and DLL_PATH_ANSI == DLL_PATH)
  io.open = realOpen
end

print("-- (CEINJ-1) the recent-files slot reads reg.exe as UTF-8 --")
-- Measured 2026-09-25 through a pipe: plain reg.exe gave the console code page with best fit (Cafe for Cafe-acute,
-- '?' for TM) -- lossy; after 'chcp 65001' every entry came back as exact UTF-8.
local popen = body:match("io%.popen%('([^']*)'%)")
check("the MRU popen switches the console to UTF-8 first", popen ~= nil and popen:find("chcp 65001", 1, true) ~= nil, popen)

print("-- (CEINJ-3) a refusal clears the path, so the next tick reaches the picker --")
local r0 = body:find("if not DLL_PATH_ANSI then", 1, true)
local r1 = r0 and body:find("return false", r0, true)
local block = (r0 and r1) and body:sub(r0, r1) or ""
check("DLL_PATH and DLL_PATH_ANSI are cleared before returning",
      block:find("DLL_PATH, DLL_PATH_ANSI = nil, nil", 1, true) ~= nil)
check("and the text says the picker comes next", block:find("file picker", 1, true) ~= nil)

print("-- (second review, T-MRU-UTF8-SELFHEAL) the recent-files probe, RUN on UTF-8 reg.exe output --")
local function liftLocal(name)
  local src = ct:match("(local function " .. name .. ".-\nend)")
  if not src then return false end
  assert(load((unxml(src):gsub("^local function " .. name, "function " .. name)), name))()
  return true
end
local okMru = ct:match("(function%s+ue5_probeRecentFiles.-\nend)") ~= nil
check("ue5_probeRecentFiles is in the .CT", okMru)
if okMru and liftLocal("_slot") and liftLocal("_dllAt") and liftLocal("_probeSlots") then
  -- its own dependency (pcall'd inside the probe: a missing one would just read as "no recent files")
  assert(load(unxml(ct:match("(function%s+ue5_splitRegMultiSz.-\nend)")), "ue5_splitRegMultiSz"))()
  assert(load(unxml(ct:match("(function%s+ue5_probeRecentFiles.-\nend)")), "ue5_probeRecentFiles"))()
  -- the .CT's chunk-level locals, as globals here
  _slots, _seen, _dllFoundIn, _dllFoundLabel = {}, {}, nil, ""
  DLL_PATH, DLL_PATH_ANSI = nil, nil
  local recorded
  function ue5_recordDllDir(d) recorded = d end
  function extractFileName(p) return (p:match("([^\\]*)$")) end
  function extractFilePath(p) return (p:gsub("[^\\]*$", "")) end
  function fileExists(p) return p == P_UTF8 end
  local mruLine = "    Recent Files    REG_MULTI_SZ    D:\\" .. U_GONGJU .. "\\UE5CEDumper\\UE5CEDumper.CT\\0E:\\other.CT\r\n"
  local realPopen = io.popen
  io.popen = function() return { read = function() return mruLine end, close = function() end } end
  io.open = fakeOpen
  ue5_probeRecentFiles()
  io.open, io.popen = realOpen, realPopen
  check("the MRU slot finds the DLL beside the table (UTF-8 path)", DLL_PATH == P_UTF8, DLL_PATH)
  check("  ...injectDLL gets its ANSI bytes", DLL_PATH_ANSI == P_ANSI, DLL_PATH_ANSI)
  check("  ...and the self-heal records the folder as UTF-8",
        recorded == "D:\\" .. U_GONGJU .. "\\UE5CEDumper\\", recorded)

  -- (third review, MRU-RELATIVE-SELF-MATCH-SHADOWS) A RELATIVE "UE5CEDumper.CT" entry (CE started with a relative
  -- table argument) is its own entry since MRU-REL-MERGE. Matched first, its folder is empty -- a later ABSOLUTE
  -- entry of the same name must still be the one probed.
  _slots, _seen, _dllFoundIn, _dllFoundLabel = {}, {}, nil, ""
  DLL_PATH, DLL_PATH_ANSI, recorded = nil, nil, nil
  mruLine = "    Recent Files    REG_MULTI_SZ    D:\\Games\\Other.CT\\0UE5CEDumper.CT\\0D:\\" .. U_GONGJU
            .. "\\UE5CEDumper\\UE5CEDumper.CT\r\n"
  io.popen = function() return { read = function() return mruLine end, close = function() end } end
  io.open = fakeOpen
  ue5_probeRecentFiles()
  io.open, io.popen = realOpen, realPopen
  check("a relative self-match does not shadow a later absolute one", DLL_PATH == P_UTF8, DLL_PATH)
end

print(string.format("\n%d check(s), %d failure(s)", checks, fails))
os.exit(fails == 0 and 0 or 1)
