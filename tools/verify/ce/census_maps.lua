-- Run inside Cheat Engine's Lua engine attached to the game, after setting the global below; it returns one line.
-- Census of one class's TArrays over the flat UE 4.15 GUObjectArray (24-byte items, object pointer at +0).
-- UE5_CENSUS = { base=<Objects>, num=<Num>, cls=<UClass*>, offs={<TArray offset>, ...} }. A subclass counts too: the
-- object's class chain is followed through UStruct::SuperStruct (+0x30 on 4.15, checked against UObject's class).
-- Returns the instance count, how many hold any Num > 0, and up to 20 of those as addr(class):num,num,...
local C = UE5_CENSUS
local function isA(k)
  for _ = 1, 32 do
    if not k or k == 0 then return false end
    if k == C.cls then return true end
    k = readQword(k + 0x30)
  end
  return false
end
local n, nz, L = 0, 0, {}
for i = 0, C.num - 1 do
  local obj = readQword(C.base + i * 24)
  if obj and obj ~= 0 then
    local k = readQword(obj + 0x10)
    if k and isA(k) then
      n = n + 1
      local counts, any = {}, false
      for _, off in ipairs(C.offs) do
        local cnt = readInteger(obj + off + 8) or -1
        counts[#counts + 1] = tostring(cnt)
        if cnt ~= 0 then any = true end
      end
      if any then
        nz = nz + 1
        if #L < 20 then L[#L + 1] = string.format('%X(%X):%s', obj, k, table.concat(counts, ',')) end
      end
    end
  end
end
return string.format('instances=%d nonzero=%d %s', n, nz, table.concat(L, ' '))
