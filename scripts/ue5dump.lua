-- ============================================================
-- ue5dump.lua — Main CE script: inject DLL + start pipe server
-- ============================================================

-- Load utilities
local scriptDir = extractFilePath(getMainForm().SaveDialog1.FileName) or ""
package.path = scriptDir .. "?.lua;" .. package.path

local ok, utils = pcall(require, "utils")
if not ok then
    -- Fallback: define inline if utils.lua not found
    utils = {
        callDLL = function(funcName, retType, ...)
            local fn = getAddress(funcName)
            assert(fn ~= 0, "[UE5Dump] Cannot find function: " .. funcName)
            local ret = executeCodeEx(1, fn, ...)
            if retType == "bool" then return ret ~= 0 end
            return ret
        end,
        log = function(fmt, ...) print(string.format("[UE5Dump] " .. fmt, ...)) end,
        logError = function(fmt, ...) print(string.format("[UE5Dump ERROR] " .. fmt, ...)) end,
        addrToHex = function(addr) return string.format("0x%X", addr) end,
    }
end

-- DLL path: place UE5Dumper.dll in the CE "ue5dumper" subfolder
local DLL_PATH = getCheatEngineDir() .. "ue5dumper\\UE5Dumper.dll"

local function main()
    utils.log("Starting UE5 Dumper...")
    utils.log("DLL path: %s", DLL_PATH)

    -- 1. Inject DLL into the target process
    local result = loadLibrary(DLL_PATH)
    if result == nil then
        utils.logError("Failed to load DLL: %s", DLL_PATH)
        utils.logError("Make sure the DLL exists and the process is open.")
        return
    end
    utils.log("DLL loaded successfully")

    -- Give DLL time to run DllMain
    sleep(500)

    -- 2. Initialize core (AOB scan for GObjects/GNames)
    local initOk = utils.callDLL("UE5_Init", "bool")
    if not initOk then
        utils.logError("Initialization failed! GObjects/GNames scan failed.")
        utils.logError("Check %APPDATA%\\UE5CEDumper\\Logs for details.")
        return
    end

    local version = utils.callDLL("UE5_GetVersion", "uint32")
    utils.log("UE version detected: %d (UE5.%d)", version, version % 100)

    -- Print pointer info
    local gobjects = utils.callDLL("UE5_GetGObjectsAddr", "uint64")
    local gnames   = utils.callDLL("UE5_GetGNamesAddr", "uint64")
    local objCount = utils.callDLL("UE5_GetObjectCount", "int32")

    utils.log("GObjects:     %s", utils.addrToHex(gobjects))
    utils.log("GNames:       %s", utils.addrToHex(gnames))
    utils.log("Object count: %d", objCount)

    -- 3. Start Pipe Server for external UI
    local pipeOk = utils.callDLL("UE5_StartPipeServer", "bool")
    if not pipeOk then
        utils.logError("Pipe Server failed to start!")
        return
    end

    utils.log("========================================")
    utils.log("Pipe Server started successfully!")
    utils.log("Pipe: \\\\.\\pipe\\UE5DumpBfx")
    utils.log("Launch UE5DumpUI.exe and click Connect.")
    utils.log("========================================")
end

-- Run with error handling
local success, err = pcall(main)
if not success then
    print("[UE5Dump FATAL] " .. tostring(err))
end
