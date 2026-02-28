-- ============================================================
-- ue5_dissect.lua — CE Structure Dissect builder via DLL
--
-- Creates Cheat Engine Structure Dissect entries from UE class
-- reflection data. Uses the injected UE5Dumper.dll exports for
-- object enumeration and property walking.
--
-- Features:
--   * Manual creation   — user inputs class address / UE path
--   * Auto callback     — registerStructureDissectOverride
--   * Full type mapping — 20+ UE property types
--   * StructProperty    — recursive flattening via UE5_GetFieldStructClass
--   * BoolProperty      — ChildStructStart bitmask via UE5_GetFieldBoolMask
--   * Array/Map/Set     — pointer child stubs
--   * Gap filling       — unused bytes shown as vtPointer
-- ============================================================

-- ----------------------------------------------------------------
-- Module state
-- ----------------------------------------------------------------
local dissect = {}            -- public API table
local structList = {}         -- saved CE structure references
local structCache = {}        -- name -> CE structure (avoids duplicates)
local callbackIdOverride = nil
local callbackIdNameLookup = nil
local MAX_STRUCT_DEPTH = 6    -- prevent infinite StructProperty recursion
local UOBJECT_VTABLE_SIZE = 8 -- 64-bit pointer

-- ----------------------------------------------------------------
-- Logging helpers
-- ----------------------------------------------------------------
local function log(fmt, ...)  print(string.format("[UE5Dissect] " .. fmt, ...)) end
local function warn(fmt, ...) print(string.format("[UE5Dissect WARN] " .. fmt, ...)) end

-- ----------------------------------------------------------------
-- DLL call helper
-- ----------------------------------------------------------------
local function callDLL(name, ...)
    local fn = getAddress(name)
    if fn == nil or fn == 0 then error("[UE5Dissect] DLL function not found: " .. name) end
    return executeCodeEx(1, nil, fn, ...)
end

-- ----------------------------------------------------------------
-- Allocate helper — allocates target-process buffer for DLL out-params
-- ----------------------------------------------------------------
local function withBuf(size, func)
    local buf = allocateMemory(size)
    if buf == nil or buf == 0 then error("[UE5Dissect] allocateMemory failed") end
    local ok, result = pcall(func, buf)
    deAlloc(buf)
    if not ok then error(result) end
    return result
end

-- ----------------------------------------------------------------
-- UE type → CE Vartype mapping
--
-- CE Lua constants (defined by CE):
--   vtByte=0, vtWord=1, vtDword=3, vtSingle=4, vtDouble=5,
--   vtQword=8, vtPointer=12
-- ----------------------------------------------------------------
local TYPE_MAP = {
    -- Integer types
    BoolProperty     = { vt = vtByte,    size = 1 },
    ByteProperty     = { vt = vtByte,    size = 1 },
    Int8Property     = { vt = vtByte,    size = 1 },
    Int16Property    = { vt = vtWord,    size = 2 },
    UInt16Property   = { vt = vtWord,    size = 2 },
    IntProperty      = { vt = vtDword,   size = 4 },
    UInt32Property   = { vt = vtDword,   size = 4 },
    Int64Property    = { vt = vtQword,   size = 8 },
    UInt64Property   = { vt = vtQword,   size = 8 },

    -- Float types
    FloatProperty    = { vt = vtSingle,  size = 4 },
    DoubleProperty   = { vt = vtDouble,  size = 8 },

    -- Name / Enum
    NameProperty     = { vt = vtQword,   size = 8 },
    EnumProperty     = { vt = vtByte,    size = 1 },  -- actual size from field

    -- Pointer types (dereferenceable in CE dissect)
    ObjectProperty   = { vt = vtPointer, size = 8 },
    ClassProperty    = { vt = vtPointer, size = 8 },
    StrProperty      = { vt = vtPointer, size = 8 },
    TextProperty     = { vt = vtPointer, size = 8 },
    ArrayProperty    = { vt = vtPointer, size = 8 },
    MapProperty      = { vt = vtPointer, size = 8 },
    SetProperty      = { vt = vtPointer, size = 8 },

    -- Soft / Lazy / Interface — treated as pointer
    SoftObjectProperty    = { vt = vtPointer, size = 8 },
    SoftClassProperty     = { vt = vtPointer, size = 8 },
    LazyObjectProperty    = { vt = vtPointer, size = 8 },
    InterfaceProperty     = { vt = vtPointer, size = 8 },
    WeakObjectProperty    = { vt = vtQword,   size = 8 },

    -- Delegates
    DelegateProperty                  = { vt = vtQword,   size = 8 },
    MulticastInlineDelegateProperty   = { vt = vtPointer, size = 16 },
    MulticastSparseDelegateProperty   = { vt = vtPointer, size = 16 },
}

-- Fallback for unknown property types
local function getTypeInfo(typeName, fieldSize)
    local entry = TYPE_MAP[typeName]
    if entry then
        -- For EnumProperty, prefer the actual field size
        if typeName == "EnumProperty" and fieldSize > 0 then
            if     fieldSize == 1 then return vtByte,   1
            elseif fieldSize == 2 then return vtWord,   2
            elseif fieldSize == 8 then return vtQword,  8
            else                       return vtDword,  4 end
        end
        return entry.vt, entry.size
    end
    -- Unknown type: show as raw dword block
    return vtDword, (fieldSize > 0 and fieldSize or 4)
end

-- ----------------------------------------------------------------
-- Walk a UClass via DLL and return a Lua field table
-- ----------------------------------------------------------------
local function walkClassFields(classAddr)
    -- Allocate buffers for output parameters (in target process memory)
    local NAME_BUF_SIZE = 256
    local TYPE_BUF_SIZE = 128
    local nameBuf   = allocateMemory(NAME_BUF_SIZE)
    local typeBuf   = allocateMemory(TYPE_BUF_SIZE)
    local addrBuf   = allocateMemory(8)
    local offsetBuf = allocateMemory(4)
    local sizeBuf   = allocateMemory(4)

    local function cleanup()
        deAlloc(nameBuf); deAlloc(typeBuf)
        deAlloc(addrBuf); deAlloc(offsetBuf); deAlloc(sizeBuf)
    end

    local ok, result = pcall(function()
        local count = callDLL("UE5_WalkClassBegin", classAddr)
        if count <= 0 then
            callDLL("UE5_WalkClassEnd")
            return {}
        end

        local fields = {}
        for i = 0, count - 1 do
            local success = callDLL("UE5_WalkClassGetField",
                i, addrBuf, nameBuf, NAME_BUF_SIZE, typeBuf, TYPE_BUF_SIZE, offsetBuf, sizeBuf)
            if success ~= 0 then
                local name     = readString(nameBuf, NAME_BUF_SIZE, false)
                local typeName = readString(typeBuf, TYPE_BUF_SIZE, false)
                local offset   = readInteger(offsetBuf)
                local size     = readInteger(sizeBuf)
                local faddr    = readQword(addrBuf)
                fields[#fields + 1] = {
                    name     = name or "",
                    typeName = typeName or "",
                    offset   = offset or 0,
                    size     = size or 0,
                    faddr    = faddr or 0,
                }
            end
        end
        callDLL("UE5_WalkClassEnd")
        return fields
    end)

    cleanup()
    if not ok then error(result) end
    return result
end

-- ----------------------------------------------------------------
-- Add fields to a CE Structure, with optional name prefix and
-- offset base (for StructProperty flattening).
-- depth: current recursion depth (guards infinite loops).
-- ----------------------------------------------------------------
local function addFieldsToStruct(ceStruct, classAddr, offsetBase, namePrefix, depth)
    depth = depth or 0
    offsetBase = offsetBase or 0
    namePrefix = namePrefix or ""

    if depth > MAX_STRUCT_DEPTH then return end

    local fields = walkClassFields(classAddr)

    for _, f in ipairs(fields) do
        local absOffset = offsetBase + f.offset
        local displayName = namePrefix .. f.name

        -- StructProperty: recurse into inner struct fields
        if f.typeName == "StructProperty" then
            local innerClass = callDLL("UE5_GetFieldStructClass", f.faddr)
            if innerClass ~= 0 then
                -- Resolve struct type name for display prefix
                local sBuf = allocateMemory(128)
                callDLL("UE5_GetObjectName", innerClass, sBuf, 128)
                local sName = readString(sBuf, 128, false) or "Struct"
                deAlloc(sBuf)
                addFieldsToStruct(ceStruct, innerClass, absOffset,
                    displayName .. "." , depth + 1)
            else
                -- Unresolved struct: emit as raw bytes
                local e = ceStruct.addElement()
                e.Offset  = absOffset
                e.Name    = displayName
                e.Vartype = vtDword
                if f.size > 4 then e.Bytesize = f.size end
            end

        -- BoolProperty: get bitmask and set ChildStructStart
        elseif f.typeName == "BoolProperty" then
            local e = ceStruct.addElement()
            e.Offset  = absOffset
            e.Name    = displayName
            e.Vartype = vtByte
            local mask = callDLL("UE5_GetFieldBoolMask", f.faddr)
            if mask ~= 0 then
                e.ChildStructStart = mask
                -- Compute bit index for display
                local bitIdx = 0
                local m = mask
                while m > 1 do m = math.floor(m / 2); bitIdx = bitIdx + 1 end
                e.Name = string.format("%s (bit %d, mask 0x%02X)", displayName, bitIdx, mask)
            end

        -- Array/Map/Set: pointer + size helpers
        elseif f.typeName == "ArrayProperty" or f.typeName == "MapProperty"
            or f.typeName == "SetProperty" then
            local e = ceStruct.addElement()
            e.Offset  = absOffset
            e.Name    = displayName
            e.Vartype = vtPointer
            -- Emit _count field (TArray::Num at pointer + 8)
            local e2 = ceStruct.addElement()
            e2.Offset  = absOffset + 8
            e2.Name    = displayName .. "_count"
            e2.Vartype = vtDword
            -- For Map: extra _capacity at +12
            if f.typeName == "MapProperty" or f.typeName == "SetProperty" then
                local e3 = ceStruct.addElement()
                e3.Offset  = absOffset + 12
                e3.Name    = displayName .. "_capacity"
                e3.Vartype = vtDword
            end

        -- Standard scalar / pointer types
        else
            local vt, vtSize = getTypeInfo(f.typeName, f.size)
            local e = ceStruct.addElement()
            e.Offset  = absOffset
            e.Name    = displayName
            e.Vartype = vt
            if vtSize ~= nil and vtSize > 8 then
                e.Bytesize = vtSize
            end
        end
    end
end

-- ----------------------------------------------------------------
-- Fill gaps: insert vtPointer every 4 bytes where no element exists
-- ----------------------------------------------------------------
local function fillGaps(ceStruct, totalSize)
    if ceStruct.Count < 1 then return end

    -- Collect existing offsets into a set
    local existing = {}
    for i = 0, ceStruct.Count - 1 do
        existing[ceStruct.Element[i].Offset] = true
    end

    -- Determine end boundary
    local lastElem = ceStruct.Element[ceStruct.Count - 1]
    local endBound = totalSize
    if endBound <= 0 then
        endBound = lastElem.Offset + (lastElem.Bytesize > 0 and lastElem.Bytesize or 4)
    end

    ceStruct.beginUpdate()
    for off = 0, endBound - 1, 4 do
        -- Check if this 4-byte slot is covered by any existing element
        if not existing[off] and not existing[off + 1]
            and not existing[off + 2] and not existing[off + 3] then
            local e = ceStruct.addElement()
            e.Offset  = off
            e.Vartype = vtPointer
        end
    end
    ceStruct.endUpdate()
end

-- ----------------------------------------------------------------
-- Add standard UObject header fields (VTable, index, class, name, outer)
-- ----------------------------------------------------------------
local function addUObjectHeader(ceStruct)
    -- Build a set of offsets already covered by existing elements
    local covered = {}
    for i = 0, ceStruct.Count - 1 do
        covered[ceStruct.Element[i].Offset] = true
    end
    local function addIfMissing(offset, name, vt)
        if not covered[offset] then
            local e = ceStruct.addElement()
            e.Offset  = offset
            e.Name    = name
            e.Vartype = vt
            covered[offset] = true
        end
    end
    addIfMissing(0x00, "VTable",      vtPointer)
    addIfMissing(0x08, "ObjectFlags", vtDword)
    addIfMissing(0x0C, "ObjectIndex", vtDword)
    addIfMissing(0x10, "Class",       vtPointer)
    addIfMissing(0x18, "FNameIndex",  vtDword)
    addIfMissing(0x20, "Outer",       vtPointer)
end

-- ----------------------------------------------------------------
-- PUBLIC: Create a CE Structure from a UClass address or object instance
--
-- Accepts either a UClass pointer or a UObject instance.
-- If the given address has no FField chain (i.e. it's an instance),
-- the function auto-resolves via UE5_GetObjectClass.
-- ----------------------------------------------------------------
function dissect.createFromClass(classAddr, structName)
    if not classAddr or classAddr == 0 then
        warn("createFromClass: invalid classAddr")
        return nil
    end

    -- Auto-detect: if no fields on this address, treat it as an instance
    -- and resolve its UClass instead.
    local testCount = callDLL("UE5_WalkClassBegin", classAddr)
    callDLL("UE5_WalkClassEnd")
    if testCount <= 0 then
        local realClass = callDLL("UE5_GetObjectClass", classAddr)
        if realClass ~= 0 and realClass ~= classAddr then
            log("0x%X is an instance (0 fields), using UClass 0x%X", classAddr, realClass)
            classAddr = realClass
        end
    end

    -- Resolve class name if not provided
    if not structName or structName == "" then
        structName = withBuf(256, function(buf)
            callDLL("UE5_GetObjectName", classAddr, buf, 256)
            return readString(buf, 256, false) or "Unknown"
        end)
    end

    -- Check cache — reuse if already built
    if structCache[structName] then
        log("Reusing cached struct: %s", structName)
        return structCache[structName]
    end

    log("Creating struct: %s (class=0x%X)", structName, classAddr)

    local ceStruct = createStructure(structName)
    ceStruct.beginUpdate()

    -- Walk class fields recursively
    addFieldsToStruct(ceStruct, classAddr, 0, "", 0)

    -- Add UObject base fields
    addUObjectHeader(ceStruct)

    ceStruct.endUpdate()

    -- Get PropertiesSize for struct size reporting
    local propsSize = callDLL("UE5_GetClassPropsSize", classAddr)

    -- Register globally
    ceStruct.addToGlobalStructureList()
    structList[#structList + 1] = ceStruct
    structCache[structName] = ceStruct

    log("Struct created: %s (%d elements, %d bytes)", structName, ceStruct.Count, propsSize)
    return ceStruct
end

-- ----------------------------------------------------------------
-- PUBLIC: Create struct by full UE object path (e.g., "/Script/Engine.Actor")
-- ----------------------------------------------------------------
function dissect.createFromPath(fullPath)
    local addr = callDLL("UE5_FindObject", fullPath)
    if addr == 0 then
        warn("Object not found: %s", fullPath)
        return nil
    end
    return dissect.createFromClass(addr)
end

-- ----------------------------------------------------------------
-- PUBLIC: Interactive creation — prompt user for class address/path
-- ----------------------------------------------------------------
function dissect.createInteractive()
    local input = inputQuery("UE5 Structure Dissect",
        "Enter UClass address (hex) or full UE path:",
        "/Script/Engine.Actor")
    if not input or input == "" then return nil end

    -- Try as hex address first
    local addr = getAddressSafe(input)
    if addr and addr ~= 0 then
        local ceStruct = dissect.createFromClass(addr)
        if ceStruct then
            createStructureForm(nil, nil, ceStruct.Name)
        end
        return ceStruct
    end

    -- Try as UE full path
    addr = callDLL("UE5_FindObject", input)
    if addr ~= 0 then
        local ceStruct = dissect.createFromClass(addr)
        if ceStruct then
            createStructureForm(nil, nil, ceStruct.Name)
        end
        return ceStruct
    end

    -- Try finding as a class name
    addr = callDLL("UE5_FindClass", input)
    if addr ~= 0 then
        local ceStruct = dissect.createFromClass(addr)
        if ceStruct then
            createStructureForm(nil, nil, ceStruct.Name)
        end
        return ceStruct
    end

    warn("Cannot resolve input: %s", input)
    return nil
end

-- ----------------------------------------------------------------
-- Callback: auto-fill Structure Dissect when CE opens it on an address
-- ----------------------------------------------------------------
local function dissectOverrideCallback(ceStruct, instanceAddr)
    if not instanceAddr or instanceAddr == 0 then return nil end

    -- Get the UClass of this instance
    local classAddr = callDLL("UE5_GetObjectClass", instanceAddr)
    if classAddr == 0 then return nil end  -- not a UObject, let CE default

    -- Get class name
    local className = withBuf(256, function(buf)
        callDLL("UE5_GetObjectName", classAddr, buf, 256)
        return readString(buf, 256, false) or ""
    end)
    if className == "" then return nil end

    log("Auto dissect: 0x%X -> %s", instanceAddr, className)

    -- Populate the provided structure
    ceStruct.beginUpdate()
    addFieldsToStruct(ceStruct, classAddr, 0, "", 0)
    addUObjectHeader(ceStruct)
    ceStruct.endUpdate()

    if ceStruct.Count > 1 then
        return true   -- override accepted
    end
    return false      -- empty struct, rejected
end

-- ----------------------------------------------------------------
-- Callback: resolve address to a UObject name for dissect title bar
-- ----------------------------------------------------------------
local function nameLookupCallback(address)
    if not address or address == 0 then return nil end

    local classAddr = callDLL("UE5_GetObjectClass", address)
    if classAddr == 0 then return nil end

    local name = withBuf(256, function(buf)
        callDLL("UE5_GetObjectName", address, buf, 256)
        return readString(buf, 256, false)
    end)
    if name and name ~= "" then
        return name, address
    end
    return nil
end

-- ----------------------------------------------------------------
-- PUBLIC: Enable/disable auto-dissect callbacks
-- ----------------------------------------------------------------
function dissect.enableAutoCallback()
    if callbackIdOverride then
        log("Auto callback already registered")
        return
    end
    callbackIdOverride    = registerStructureDissectOverride(dissectOverrideCallback)
    callbackIdNameLookup  = registerStructureNameLookup(nameLookupCallback)
    log("Auto dissect callbacks registered")
end

function dissect.disableAutoCallback()
    if callbackIdOverride then
        unregisterStructureDissectOverride(callbackIdOverride)
        callbackIdOverride = nil
    end
    if callbackIdNameLookup then
        unregisterStructureNameLookup(callbackIdNameLookup)
        callbackIdNameLookup = nil
    end
    log("Auto dissect callbacks unregistered")
end

-- ----------------------------------------------------------------
-- PUBLIC: Clear all created structures
-- ----------------------------------------------------------------
function dissect.clearAll()
    for _, s in ipairs(structList) do
        pcall(function() s:removeFromGlobalStructureList() end)
        pcall(function() s:Destroy() end)
    end
    structList = {}
    structCache = {}
    log("All structures cleared")
end

-- ----------------------------------------------------------------
-- Module return
-- ----------------------------------------------------------------
return dissect
