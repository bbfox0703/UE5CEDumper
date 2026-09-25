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

print(string.format("\n%d check(s), %d failure(s)", checks, fails))
os.exit(fails == 0 and 0 or 1)
