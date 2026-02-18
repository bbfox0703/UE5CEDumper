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

return M
