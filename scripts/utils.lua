-- ============================================================
-- utils.lua — Helper utilities for CE Lua scripts
-- ============================================================

local M = {}

--- Call an exported DLL function by name
--- @param funcName string  The export function name (e.g. "UE5_Init")
--- @param retType  string  Return type: "bool", "uint32", "uint64", "int32", "void"
--- @param ...      any     Function arguments
--- @return any             The function's return value
function M.callDLL(funcName, retType, ...)
    local fn = getAddress(funcName)
    if fn == nil or fn == 0 then
        error("[UE5Dump] Cannot find function: " .. funcName)
    end

    -- Map return types to CE calling convention
    local ceRetType
    if retType == "bool" then
        ceRetType = 1  -- cdecl, return integer (interpret as bool)
    elseif retType == "uint32" or retType == "int32" then
        ceRetType = 1
    elseif retType == "uint64" then
        ceRetType = 1
    elseif retType == "void" then
        ceRetType = 0
    else
        ceRetType = 1
    end

    local result = executeCodeEx(ceRetType, fn, ...)

    if retType == "bool" then
        return result ~= 0
    end

    return result
end

--- Format an address as hex string
--- @param addr number  Address value
--- @return string      Hex formatted string "0x..."
function M.addrToHex(addr)
    return string.format("0x%X", addr)
end

--- Print a formatted log message
--- @param fmt string  Format string
--- @param ... any     Format arguments
function M.log(fmt, ...)
    print(string.format("[UE5Dump] " .. fmt, ...))
end

--- Print an error message
--- @param fmt string  Format string
--- @param ... any     Format arguments
function M.logError(fmt, ...)
    print(string.format("[UE5Dump ERROR] " .. fmt, ...))
end

--- Check whether UE5CEDumper is already loaded in the currently attached process.
--- Detects two cases:
---   1. Our proxy DLL (version.dll / winmm.dll) loaded from a non-System32 path
---      (i.e. hijacked from the game's binaries folder).
---   2. Our injected UE5Dumper.dll already present from a previous inject.
--- Returns: present (bool), moduleName (string|nil), modulePath (string|nil)
function M.isAlreadyLoaded()
    -- Fast path: any of our exports resolvable means *something* of ours is loaded.
    -- pcall guards against CE versions that error on unresolved symbols.
    local ok, addr = pcall(getAddress, "UE5_Init")
    if not (ok and addr and addr ~= 0) then
        return false, nil, nil
    end

    -- Confirmed loaded — figure out which module so we can show a useful message.
    local mods
    local okEnum
    okEnum, mods = pcall(enumModules)
    if okEnum and type(mods) == "table" then
        for _, mod in ipairs(mods) do
            local nm = string.lower(mod.Name or "")
            if nm == "ue5dumper.dll" then
                return true, mod.Name, mod.PathToFile
            end
            if nm == "version.dll" or nm == "winmm.dll" then
                local path = string.lower(mod.PathToFile or "")
                -- Skip System32/SysWOW64 — genuine Windows copy
                if path ~= "" and not path:find("\\system32\\", 1, true)
                              and not path:find("\\syswow64\\", 1, true) then
                    return true, mod.Name, mod.PathToFile
                end
            end
        end
    end

    -- Export resolved but module unknown — still treat as already loaded.
    return true, "(unknown module)", nil
end

return M
