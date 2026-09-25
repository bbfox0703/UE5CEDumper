--[[
  ct_mru_split_test.lua
  UE5CEDumper -- [PATH-CT-MRU-ZERO] UE5CEDumper.CT splits reg.exe's REG_MULTI_SZ output correctly.

  WHY
    reg.exe prints a REG_MULTI_SZ with the LITERAL two characters \0 between entries. The .CT split on every "\0",
    which a path also contains wherever a folder name starts with the digit 0 (D:\0Games\...): measured on CE's VM,
    one entry became {'D:', 'Games\UE5CEDumper\UE5CEDumper.CT'} -- a relative path, and a drive-root probe.
    A separator is a "\0" that is followed by the next ABSOLUTE path (X:\ or \\) or by the end.

  HOW
    ue5_splitRegMultiSz is lifted verbatim from scripts/UE5CEDumper.CT (XML entities unescaped) and run. Stubs only.

  RUNNING IT
      py tools/check_lua_suites.py      (all suites, on CE's own lua53-64.dll)
      lua scripts/tests/ct_mru_split_test.lua
]]

local HERE = (arg and arg[0] or ''):gsub('[^/\\]*$', '')
local f = assert(io.open(HERE .. '../UE5CEDumper.CT', "rb")); local ct = f:read("a"); f:close()
local function unxml(s) return (s:gsub("&lt;", "<"):gsub("&gt;", ">"):gsub("&quot;", '"'):gsub("&amp;", "&")) end

local fails, checks = 0, 0
local function check(name, cond, got)
  checks = checks + 1
  if cond then print("  ok    " .. name)
  else fails = fails + 1; print("  FAIL  " .. name .. "   got: " .. tostring(got)) end
end

local src = ct:match("(function%s+ue5_splitRegMultiSz.-\nend)")
check("ue5_splitRegMultiSz is in the .CT", src ~= nil)
if src then
  assert(load(unxml(src), "ue5_splitRegMultiSz"))()
  local function same(t, want)
    if #t ~= #want then return false end
    for i = 1, #t do if t[i] ~= want[i] then return false end end
    return true
  end
  local function show(t) return "{" .. table.concat(t, " | ") .. "}" end
  local S = "\\0"   -- reg.exe's literal separator: a backslash and a zero

  local t = ue5_splitRegMultiSz("D:\\0Games\\UE5CEDumper\\UE5CEDumper.CT" .. S .. "D:\\Tables\\other.CT")
  check("a folder starting with 0 is not a separator",
        same(t, { "D:\\0Games\\UE5CEDumper\\UE5CEDumper.CT", "D:\\Tables\\other.CT" }), show(t))
  t = ue5_splitRegMultiSz("C:\\a.CT" .. S .. "\\\\server\\share\\0x\\b.CT" .. S .. "E:\\c.CT")
  check("a UNC entry and a 0-folder inside it",
        same(t, { "C:\\a.CT", "\\\\server\\share\\0x\\b.CT", "E:\\c.CT" }), show(t))
  t = ue5_splitRegMultiSz("D:\\only.CT")
  check("one entry", same(t, { "D:\\only.CT" }), show(t))
  t = ue5_splitRegMultiSz("D:\\a.CT" .. S)
  check("a trailing separator adds nothing", same(t, { "D:\\a.CT" }), show(t))
  t = ue5_splitRegMultiSz("  D:\\a b.CT  " .. S .. " D:\\c.CT")
  check("entries are trimmed, inner spaces kept", same(t, { "D:\\a b.CT", "D:\\c.CT" }), show(t))
  t = ue5_splitRegMultiSz("")
  check("empty in, nothing out", #t == 0, show(t))
  -- (second review, MRU-REL-MERGE) CE can store a RELATIVE entry (started with a relative table argument, then
  -- saved): it must not be glued onto the entry before it, which the old blind split kept whole.
  t = ue5_splitRegMultiSz("D:\\T\\UE5CEDumper.CT" .. S .. "rel.ct" .. S .. "E:\\y.CT")
  check("a relative entry after a table is its own entry",
        same(t, { "D:\\T\\UE5CEDumper.CT", "rel.ct", "E:\\y.CT" }), show(t))
  t = ue5_splitRegMultiSz("D:\\0Games\\x.CT")
  check("still: a 0-folder is not a separator", same(t, { "D:\\0Games\\x.CT" }), show(t))
end

local body = unxml(ct)
check("the recent-files reader uses it",
      body:find("ue5_splitRegMultiSz(data)", 1, true) ~= nil)
check("the old blind split is gone", body:find('gmatch("(.-)\\\\0")', 1, true) == nil)

print(string.format("\n%d check(s), %d failure(s)", checks, fails))
os.exit(fails == 0 and 0 or 1)
