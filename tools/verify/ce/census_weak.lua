-- Run inside Cheat Engine's Lua engine attached to the game, after setting the global below; it returns one line.
-- Census of a TAssetPtr field at OFF on every object whose class chain holds CLS (SuperStruct at +0x30 on 4.15), over
-- the flat GUObjectArray. UE5_WEAK = { base=, num=, cls=, off= }. A weak pair {ObjectIndex, SerialNumber} that is not
-- {-1, 0} and not {0, 0} has been resolved by TPersistentObjectPtr::Get(); report up to 20 such objects with the
-- pair, whether ObjectIndex is in range, and the object at that index.
local C = UE5_WEAK
local function isA(k)
  for _ = 1, 32 do
    if not k or k == 0 then return false end
    if k == C.cls then return true end
    k = readQword(k + 0x30)
  end
  return false
end
local n, hit, L = 0, 0, {}
for i = 0, C.num - 1 do
  local obj = readQword(C.base + i * 24)
  if obj and obj ~= 0 and isA(readQword(obj + 0x10)) then
    n = n + 1
    local idx, ser = readInteger(obj + C.off, true), readInteger(obj + C.off + 4, true)
    if idx and not ((idx == -1 or idx == 0) and ser == 0) then
      hit = hit + 1
      if #L < 20 then
        local target = (idx >= 0 and idx < C.num) and readQword(C.base + idx * 24) or 0
        L[#L + 1] = string.format('%X:{%d,%d}->%X', obj, idx, ser, target or 0)
      end
    end
  end
end
return string.format('instances=%d resolved=%d %s', n, hit, table.concat(L, ' '))
