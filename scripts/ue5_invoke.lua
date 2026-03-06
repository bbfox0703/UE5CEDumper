-- ============================================================
-- ue5_invoke.lua — CE Lua UFunction invoker via shared memory mailbox
--
-- Communicates with the UE5Dumper DLL through a shared memory mailbox
-- (exported global "g_invokeMailbox"). All operations use
-- readQword/writeQword (ReadProcessMemory/WriteProcessMemory) so
-- no remote thread injection is needed.
--
-- ============================================================
-- API REFERENCE
-- ============================================================
--
-- invoke.init()
--     Locates the g_invokeMailbox exported symbol. Must be called
--     once before any other function.
--     Returns: mailbox base address (number)
--
-- invoke.findInstance(className)
--     Finds a live (non-CDO) UObject instance by class name.
--     Returns: UObject* address (number)
--     Example: local player = invoke.findInstance("BP_Player_C")
--
-- invoke.findFunction(instanceAddr, funcName)
--     Resolves a UFunction on the instance's UClass hierarchy.
--     Returns: table {addr, parmsSize, numParms, flags}
--     Example: local fn = invoke.findFunction(player, "GetHealth")
--
-- invoke.invoke(instanceAddr, ufuncAddr [, params])
--     Calls ProcessEvent on the game thread via the mailbox.
--     params is optional: array of {type=szDword|szFloat|..., value=N}
--     Returns: result code (0 = success)
--     Example: invoke.invoke(player, fn.addr)
--
-- invoke.invokeByName(className, funcName [, params])
--     All-in-one: find instance + find function + invoke.
--     Returns: result code (0 = success)
--     Example: invoke.invokeByName("BP_Player_C", "addMoney",
--                  { {type=szDword, value=1000} })
--
-- invoke.readReturnValue(offset, valueType)
--     Reads a return value from the params buffer after invoke.
--     valueType: "int32" (default), "float", "double", "bool",
--                "byte", "uint64", "qword", "int16", "word"
--     Example: local hp = invoke.readReturnValue(0, "float")
--
-- invoke.getMailboxAddr()
--     Returns the mailbox base address for manual memory access.
--
-- invoke.getParamsAddr()
--     Returns the params_data buffer address (mb + 0x328).
--
-- ============================================================
-- FUNCTION DISCOVERY
-- ============================================================
--
-- invoke.listFunctions(classOrAddr [, page])
--     Lists all UFunctions on a class, paginated (15 per page).
--     classOrAddr: class name (string) or UObject* address (number)
--     page: 0-based page index (default 0)
--     Returns: {functions={...}, total=N, page=P, totalPages=TP,
--              instanceAddr=addr}
--     Each function entry contains:
--       name, addr, parmsSize, numParms, flags,
--       isBlueprintCallable, isBlueprintPure, isConst, isNative,
--       isSafeGetter (heuristic: pure/const with <=1 param)
--
-- invoke.discover(classOrAddr)
--     Auto-discovers and categorizes ALL functions on a class.
--     Fetches all pages and prints a categorized report:
--
--       1. SAFE GETTERS  — Pure/Const functions with 0 input params.
--          These are ideal for testing: no side effects.
--       2. BLUEPRINT CALLABLE — Functions that can be called from
--          Blueprints. May have side effects.
--       3. OTHER — Internal/Native functions (count shown, not listed).
--
--     Also prints a ready-to-copy QUICK TEST snippet for the first
--     safe getter found.
--
--     Returns: array of all function entries
--
--     Example:
--       local invoke = require("ue5_invoke")
--       invoke.init()
--       invoke.discover("BP_Player_C")
--       -- Prints categorized function list to CE console
--
-- ============================================================
-- FLAG CONSTANTS (exported on the module table)
-- ============================================================
--
-- invoke.FUNC_Native            = 0x00000400
-- invoke.FUNC_Event             = 0x00000800
-- invoke.FUNC_Static            = 0x00002000
-- invoke.FUNC_Public            = 0x00020000
-- invoke.FUNC_HasOutParms       = 0x00400000
-- invoke.FUNC_BlueprintCallable = 0x04000000
-- invoke.FUNC_BlueprintEvent    = 0x08000000
-- invoke.FUNC_BlueprintPure     = 0x10000000
-- invoke.FUNC_Const             = 0x40000000
--
-- ============================================================
-- QUICK START
-- ============================================================
--
--   local invoke = require("ue5_invoke")
--   invoke.init()
--
--   -- 1) Discover what functions are available
--   invoke.discover("BP_PlayerCharacter_C")
--
--   -- 2) Call a safe getter
--   local player = invoke.findInstance("BP_PlayerCharacter_C")
--   local fn = invoke.findFunction(player, "GetHealth")
--   invoke.invoke(player, fn.addr)
--   local hp = invoke.readReturnValue(0, "float")
--   print("Health: " .. hp)
--
--   -- 3) Call a function with parameters
--   local result = invoke.invokeByName("BP_PlayerCharacter_C", "AddMoney",
--       { {type=szDword, value=1000, offset=0} })
--
-- ============================================================

local M = {}

-- Mailbox offsets (must match Mailbox.h MailboxData struct)
local OFF_CMD            = 0x000   -- int32: command (write LAST)
local OFF_STATUS         = 0x004   -- int32: status (poll this)
local OFF_RESULT         = 0x008   -- int32: result code
local OFF_INSTANCE_ADDR  = 0x010   -- uint64: UObject*
local OFF_UFUNC_ADDR     = 0x018   -- uint64: UFunction*
local OFF_PARMS_SIZE     = 0x020   -- uint16: ParmsSize
local OFF_NUM_PARMS      = 0x022   -- uint16: NumParms
local OFF_FUNC_FLAGS     = 0x024   -- uint32: FunctionFlags
local OFF_CLASS_NAME     = 0x028   -- char[256]: class name
local OFF_FUNC_NAME      = 0x128   -- char[256]: function name
local OFF_ERROR_MSG      = 0x228   -- char[256]: error message
local OFF_PARAMS_DATA    = 0x328   -- uint8[1024]: inline params buffer

-- Commands
local CMD_IDLE            = 0
local CMD_INVOKE          = 1
local CMD_FIND_INSTANCE   = 2
local CMD_FIND_FUNCTION   = 3
local CMD_INVOKE_BY_NAME  = 4
local CMD_LIST_FUNCTIONS  = 5

-- Status
local STATUS_DONE         = 1

-- Cached mailbox address
local mb = nil

-- Default timeout in milliseconds
local DEFAULT_TIMEOUT_MS = 10000

--- Initialize the invoker by finding the mailbox symbol.
--- Must be called before any other function.
function M.init()
    mb = getAddress("g_invokeMailbox")
    if mb == nil or mb == 0 then
        -- Try with DLL name prefix
        mb = getAddress("UE5Dumper.g_invokeMailbox")
    end
    if mb == nil or mb == 0 then
        -- Try via function export
        local fnAddr = getAddress("UE5_GetMailboxAddr")
        if fnAddr and fnAddr ~= 0 then
            -- Cannot call the function (that's the whole point of mailbox)
            -- but the symbol tells us the DLL is loaded
            error("[UE5Invoke] DLL loaded but g_invokeMailbox symbol not found. "
                  .. "Try: mb = readQword(getAddress('UE5_GetMailboxAddr') + offset)")
        end
        error("[UE5Invoke] Cannot find g_invokeMailbox symbol. Is UE5Dumper.dll injected?")
    end
    print(string.format("[UE5Invoke] Mailbox at 0x%X", mb))
    return mb
end

--- Get the mailbox base address (for reading params_data manually).
function M.getMailboxAddr()
    return mb
end

--- Get the params_data buffer address (for reading return values).
function M.getParamsAddr()
    return mb + OFF_PARAMS_DATA
end

--- Internal: wait for mailbox status to become DONE.
local function waitForDone(timeoutMs)
    timeoutMs = timeoutMs or DEFAULT_TIMEOUT_MS
    local elapsed = 0
    while readInteger(mb + OFF_STATUS) ~= STATUS_DONE do
        sleep(1)
        elapsed = elapsed + 1
        if elapsed >= timeoutMs then
            error("[UE5Invoke] Timeout waiting for mailbox response ("
                  .. timeoutMs .. "ms)")
        end
    end
end

--- Internal: read error message from mailbox.
local function readErrorMsg()
    local msg = readString(mb + OFF_ERROR_MSG, 256)
    if msg and #msg > 0 then
        return msg
    end
    return "Unknown error"
end

--- Internal: write a null-terminated string to mailbox buffer.
local function writeMailboxString(offset, str)
    local bytes = {}
    for i = 1, math.min(#str, 255) do
        bytes[#bytes + 1] = string.byte(str, i)
    end
    bytes[#bytes + 1] = 0  -- null terminator
    writeBytes(mb + offset, bytes)
end

--- Internal: write parameter table to params_data buffer.
--- Format: array of {type=szDword|szByte|szFloat|..., value=number}
local function writeParams(params)
    if not params or #params == 0 then
        -- Zero out first 64 bytes of params buffer
        for i = 0, 63 do
            writeByte(mb + OFF_PARAMS_DATA + i, 0)
        end
        return
    end

    local offset = 0
    for _, p in ipairs(params) do
        local t = p.type or szDword
        local v = p.value or 0
        local paramOffset = p.offset  -- explicit offset override

        if paramOffset then
            offset = paramOffset
        end

        if t == szByte then
            writeByte(mb + OFF_PARAMS_DATA + offset, math.floor(v) % 256)
            offset = offset + 1
        elseif t == szWord then
            writeSmallInteger(mb + OFF_PARAMS_DATA + offset, math.floor(v))
            offset = offset + 2
        elseif t == szDword then
            writeInteger(mb + OFF_PARAMS_DATA + offset, math.floor(v))
            offset = offset + 4
        elseif t == szQword then
            writeQword(mb + OFF_PARAMS_DATA + offset, v)
            offset = offset + 8
        elseif t == szFloat then
            writeFloat(mb + OFF_PARAMS_DATA + offset, v)
            offset = offset + 4
        elseif t == szDouble then
            writeDouble(mb + OFF_PARAMS_DATA + offset, v)
            offset = offset + 8
        else
            -- Default: 4 bytes
            writeInteger(mb + OFF_PARAMS_DATA + offset, math.floor(v))
            offset = offset + 4
        end
    end
end

--- Find a non-CDO instance of a class by name.
--- @param className string  The UClass name (e.g. "BP_Player_C")
--- @return number           The UObject* address
function M.findInstance(className)
    if not mb then error("[UE5Invoke] Not initialized. Call init() first.") end

    -- Write class name
    writeMailboxString(OFF_CLASS_NAME, className)

    -- Clear status, then set command (trigger)
    writeInteger(mb + OFF_STATUS, 0)
    writeInteger(mb + OFF_CMD, CMD_FIND_INSTANCE)

    -- Wait for result
    waitForDone()

    local result = readInteger(mb + OFF_RESULT)
    if result ~= 0 then
        error(string.format("[UE5Invoke] findInstance('%s') failed: %s",
              className, readErrorMsg()))
    end

    local addr = readQword(mb + OFF_INSTANCE_ADDR)
    print(string.format("[UE5Invoke] Found %s at 0x%X", className, addr))
    return addr
end

--- Find a UFunction by name on an instance's class.
--- @param instanceAddr number  The UObject* address
--- @param funcName string      The function name (e.g. "addMoney")
--- @return table               {addr, parmsSize, numParms, flags}
function M.findFunction(instanceAddr, funcName)
    if not mb then error("[UE5Invoke] Not initialized. Call init() first.") end

    -- Write instance + function name
    writeQword(mb + OFF_INSTANCE_ADDR, instanceAddr)
    writeMailboxString(OFF_FUNC_NAME, funcName)

    -- Clear status, then trigger
    writeInteger(mb + OFF_STATUS, 0)
    writeInteger(mb + OFF_CMD, CMD_FIND_FUNCTION)

    -- Wait
    waitForDone()

    local result = readInteger(mb + OFF_RESULT)
    if result ~= 0 then
        error(string.format("[UE5Invoke] findFunction('%s') failed: %s",
              funcName, readErrorMsg()))
    end

    local info = {
        addr = readQword(mb + OFF_UFUNC_ADDR),
        parmsSize = readSmallInteger(mb + OFF_PARMS_SIZE),
        numParms = readSmallInteger(mb + OFF_NUM_PARMS),
        flags = readInteger(mb + OFF_FUNC_FLAGS),
    }

    print(string.format("[UE5Invoke] Found %s at 0x%X (parmsSize=%d, numParms=%d)",
          funcName, info.addr, info.parmsSize, info.numParms))
    return info
end

--- Invoke ProcessEvent on an instance with a UFunction.
--- @param instanceAddr number       The UObject* address
--- @param ufuncAddr number          The UFunction* address
--- @param params table|nil          Optional: parameter table (see writeParams)
--- @return number                   Result code (0 = success)
function M.invoke(instanceAddr, ufuncAddr, params)
    if not mb then error("[UE5Invoke] Not initialized. Call init() first.") end

    -- Write instance + function addresses
    writeQword(mb + OFF_INSTANCE_ADDR, instanceAddr)
    writeQword(mb + OFF_UFUNC_ADDR, ufuncAddr)

    -- Write parameters to inline buffer
    writeParams(params)

    -- Clear status, then trigger
    writeInteger(mb + OFF_STATUS, 0)
    writeInteger(mb + OFF_CMD, CMD_INVOKE)

    -- Wait (invoke may take up to 5s for game-thread dispatch)
    waitForDone(DEFAULT_TIMEOUT_MS)

    local result = readInteger(mb + OFF_RESULT)
    if result ~= 0 then
        local msg = readErrorMsg()
        print(string.format("[UE5Invoke] invoke result=%d: %s", result, msg))
    else
        print("[UE5Invoke] invoke OK")
    end

    return result
end

--- All-in-one: find instance + find function + invoke.
--- @param className string     The UClass name
--- @param funcName string      The function name
--- @param params table|nil     Optional: parameter table
--- @return number              Result code (0 = success)
function M.invokeByName(className, funcName, params)
    if not mb then error("[UE5Invoke] Not initialized. Call init() first.") end

    -- Write class name + function name
    writeMailboxString(OFF_CLASS_NAME, className)
    writeMailboxString(OFF_FUNC_NAME, funcName)

    -- Write parameters
    writeParams(params)

    -- Clear status, then trigger
    writeInteger(mb + OFF_STATUS, 0)
    writeInteger(mb + OFF_CMD, CMD_INVOKE_BY_NAME)

    -- Wait
    waitForDone(DEFAULT_TIMEOUT_MS)

    local result = readInteger(mb + OFF_RESULT)
    local instanceAddr = readQword(mb + OFF_INSTANCE_ADDR)
    local ufuncAddr = readQword(mb + OFF_UFUNC_ADDR)

    if result ~= 0 then
        local msg = readErrorMsg()
        print(string.format("[UE5Invoke] invokeByName('%s::%s') failed: %s",
              className, funcName, msg))
    else
        print(string.format("[UE5Invoke] invokeByName('%s::%s') OK (inst=0x%X func=0x%X)",
              className, funcName, instanceAddr, ufuncAddr))
    end

    return result
end

-- ============================================================
-- Function Discovery
-- ============================================================

-- UE EFunctionFlags constants (for filtering)
M.FUNC_Native            = 0x00000400
M.FUNC_Event             = 0x00000800
M.FUNC_Static            = 0x00002000
M.FUNC_Public            = 0x00020000
M.FUNC_HasOutParms       = 0x00400000
M.FUNC_BlueprintCallable = 0x04000000
M.FUNC_BlueprintEvent    = 0x08000000
M.FUNC_BlueprintPure     = 0x10000000
M.FUNC_Const             = 0x40000000

--- List all UFunctions on an instance's class (paginated).
--- @param classOrAddr string|number  Class name or UObject* address
--- @param page number|nil            Page index (0-based, default 0)
--- @return table  {functions={...}, total=N, page=P, totalPages=TP}
function M.listFunctions(classOrAddr, page)
    if not mb then error("[UE5Invoke] Not initialized. Call init() first.") end

    page = page or 0

    -- Set up input
    if type(classOrAddr) == "string" then
        writeMailboxString(OFF_CLASS_NAME, classOrAddr)
        writeQword(mb + OFF_INSTANCE_ADDR, 0)  -- let DLL find instance
    else
        writeQword(mb + OFF_INSTANCE_ADDR, classOrAddr)
    end

    -- Write page index to params_data[0..3]
    writeInteger(mb + OFF_PARAMS_DATA, page)

    -- Clear status, then trigger
    writeInteger(mb + OFF_STATUS, 0)
    writeInteger(mb + OFF_CMD, CMD_LIST_FUNCTIONS)

    -- Wait
    waitForDone()

    local result = readInteger(mb + OFF_RESULT)
    if result ~= 0 then
        error(string.format("[UE5Invoke] listFunctions failed: %s", readErrorMsg()))
    end

    -- Read metadata from header fields
    local totalCount = readSmallInteger(mb + OFF_PARMS_SIZE)
    local returnedCount = readSmallInteger(mb + OFF_NUM_PARMS)
    local totalPages = readInteger(mb + OFF_FUNC_FLAGS)

    -- Parse entries (each 64 bytes)
    local ENTRY_SIZE = 64
    local funcs = {}
    for i = 0, returnedCount - 1 do
        local base = mb + OFF_PARAMS_DATA + (i * ENTRY_SIZE)
        local entry = {
            addr = readQword(base + 0),
            parmsSize = readSmallInteger(base + 8),
            numParms = readSmallInteger(base + 10),
            flags = readInteger(base + 12),
            name = readString(base + 16, 48),
        }

        -- Add convenience flags
        local f = entry.flags
        entry.isBlueprintCallable = (f % (0x04000000 * 2)) >= 0x04000000
        entry.isBlueprintPure = (f % (0x10000000 * 2)) >= 0x10000000
        entry.isConst = (f % (0x40000000 * 2)) >= 0x40000000
        entry.isNative = (f % (0x00000400 * 2)) >= 0x00000400

        -- Heuristic: safe to test = pure getter (no input params, only return value)
        -- numParms includes the return param, so numParms<=1 means 0 inputs + 0-1 return
        entry.isSafeGetter = (entry.numParms <= 1) and
            (entry.isBlueprintPure or entry.isConst)

        funcs[#funcs + 1] = entry
    end

    return {
        functions = funcs,
        total = totalCount,
        page = page,
        totalPages = totalPages,
        instanceAddr = readQword(mb + OFF_INSTANCE_ADDR),
    }
end

--- Internal: check if a flag bit is set (handles Lua 5.3 integers)
local function hasFlag(flags, bit)
    return (flags % (bit * 2)) >= bit
end

--- Discover all UFunctions on a class and print categorized results.
--- Highlights safe-to-test getters first, then other callables.
--- @param classOrAddr string|number  Class name or UObject* address
function M.discover(classOrAddr)
    if not mb then error("[UE5Invoke] Not initialized. Call init() first.") end

    local label = type(classOrAddr) == "string" and classOrAddr
                  or string.format("0x%X", classOrAddr)

    print(string.format("\n[UE5Invoke] === Discovering functions on %s ===\n", label))

    -- Collect all pages
    local allFuncs = {}
    local page = 0
    local total = 0
    local instAddr = 0

    while true do
        local r = M.listFunctions(classOrAddr, page)
        total = r.total
        instAddr = r.instanceAddr

        for _, f in ipairs(r.functions) do
            allFuncs[#allFuncs + 1] = f
        end

        if page + 1 >= r.totalPages then break end
        page = page + 1

        -- After first call, use instanceAddr directly (faster)
        classOrAddr = instAddr
    end

    print(string.format("Instance: 0x%X  |  Total functions: %d\n", instAddr, total))

    -- Categorize
    local safeGetters = {}
    local callables = {}
    local others = {}

    for _, f in ipairs(allFuncs) do
        if f.isSafeGetter then
            safeGetters[#safeGetters + 1] = f
        elseif f.isBlueprintCallable then
            callables[#callables + 1] = f
        else
            others[#others + 1] = f
        end
    end

    -- Print safe getters first (best test candidates)
    if #safeGetters > 0 then
        print("--- SAFE GETTERS (0 input params, Pure/Const) ---")
        print("    These are ideal for testing: no side effects, just read a value.")
        print("")
        for _, f in ipairs(safeGetters) do
            print(string.format(
                "  [TEST] %-36s  addr=0x%X  parmsSize=%d  numParms=%d",
                f.name, f.addr, f.parmsSize, f.numParms))
        end
        print("")
    end

    -- Print other callables
    if #callables > 0 then
        print("--- BLUEPRINT CALLABLE (may have side effects) ---")
        print("")
        for _, f in ipairs(callables) do
            local tags = ""
            if f.isConst then tags = tags .. " [Const]" end
            if f.isBlueprintPure then tags = tags .. " [Pure]" end
            if hasFlag(f.flags, 0x00400000) then tags = tags .. " [HasOut]" end
            print(string.format(
                "  %-36s  addr=0x%X  parmsSize=%d  numParms=%d%s",
                f.name, f.addr, f.parmsSize, f.numParms, tags))
        end
        print("")
    end

    -- Print others (Native/Event internals, usually not worth testing)
    if #others > 0 then
        print(string.format("--- OTHER (%d internal/native functions, not shown) ---", #others))
        print("")
    end

    -- Print quick-test suggestion
    if #safeGetters > 0 then
        local best = safeGetters[1]
        print("=== QUICK TEST ===")
        print(string.format("  local inst = 0x%X", instAddr))
        print(string.format("  local func = invoke.findFunction(inst, \"%s\")", best.name))
        print(string.format("  invoke.invoke(inst, func.addr)"))
        if best.parmsSize > 0 then
            print(string.format("  -- Return value at: invoke.getParamsAddr()"))
            print(string.format("  -- parmsSize=%d (check type: float/int32/bool)", best.parmsSize))
        else
            print("  -- Void function (no return value)")
        end
    end

    print("")
    return allFuncs
end

--- Read a return value from the params buffer after invoke.
--- @param offset number     Byte offset within params_data
--- @param valueType string  "int32", "float", "bool", "uint64", "byte"
--- @return number|boolean
function M.readReturnValue(offset, valueType)
    if not mb then error("[UE5Invoke] Not initialized.") end

    local addr = mb + OFF_PARAMS_DATA + (offset or 0)

    if valueType == "float" then
        return readFloat(addr)
    elseif valueType == "double" then
        return readDouble(addr)
    elseif valueType == "bool" or valueType == "byte" then
        return readByte(addr)
    elseif valueType == "uint64" or valueType == "qword" then
        return readQword(addr)
    elseif valueType == "int16" or valueType == "word" then
        return readSmallInteger(addr)
    else
        -- Default: int32
        return readInteger(addr)
    end
end

return M
