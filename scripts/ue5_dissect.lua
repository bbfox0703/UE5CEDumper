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
-- Debug gate: log() stays quiet unless UE5_DEBUG ~= 0 (set it in CE's Lua
-- console) so the Lua Engine window does not pop over Cheat Engine. warn()
-- always prints -- it flags a real problem the user needs to see.
local function log(fmt, ...)  if (UE5_DEBUG or 0) ~= 0 then print(string.format("[UE5Dissect] " .. fmt, ...)) end end
local function warn(fmt, ...) print(string.format("[UE5Dissect WARN] " .. fmt, ...)) end

-- ----------------------------------------------------------------
-- DLL call helper
-- ----------------------------------------------------------------
-- executeCodeEx blocks the CALLING thread in a WaitForSingleObject with nothing on
-- that path pumping messages (CE 7.5-195, LuaHandler.pas:11861), and the calling
-- thread here is CE's GUI thread -- so this timeout is the ceiling on how long CE
-- can sit frozen, and a Lua-side processMessagesPaintOnly() cannot reach it.
--
-- This used to be nil. A nil timeout is INFINITE, not "use a default"
-- (LuaHandler.pas:11504-11505), so a suspended target or a faulting stub froze CE
-- permanently with no UI-level recovery -- and a class walk calls this once per
-- FIELD, so the exposure multiplied. See docs/ce-plugin-sdk-notes.md 13.2-13.3.
local DLL_CALL_TIMEOUT_MS = 5000   -- keep in step with CeLuaHygiene.DllCallTimeoutMs

-- CONTRACT: callDLL either returns the DLL's value or RAISES. It never returns
-- nil, and no caller needs a nil check.
--
-- It used to return CE's nil straight through after a warn(), and not one of the
-- 19 call sites handled that. The two ways Lua treats nil are both wrong here and
-- in opposite directions: `count <= 0` RAISES ("attempt to compare nil with
-- number", naming a line rather than the DLL call that failed), while
-- `success ~= 0` is TRUE for nil, so a failed field read counted as a SUCCESS.
-- Since UE5_WalkClassGetField leaves its out-params untouched when it fails
-- (Frieren.cpp:712-731), that success then re-read the PREVIOUS field's buffers
-- and recorded it twice -- and with the DLL failing outright, the walk built a
-- full-size structure of empty-named, offset-0 rows, registered it with CE,
-- cached it and logged "Struct created". A total failure was reported to the
-- user as a successfully built structure. (audit #5 AA5 / AA6)
--
-- Raising is only safe because the two CE-REGISTERED callbacks now barrier it --
-- see dissectOverrideCallback. Do not remove one without the other.
local function callDLL(name, ...)
    -- Under CE's DEFAULT configuration getAddress RETURNS 0 for an unresolved
    -- symbol -- it does not raise. getAddressFromNameL gates its raise on
    -- ExceptionOnLuaLookup (symbolhandler.pas:5076-5087) and TSymhandler.create
    -- sets that FALSE (:6688); nothing in CE's Pascal ever sets it true, only the
    -- Lua-facing errorOnLookupFailure(). So this guard is LIVE, and it is the only
    -- thing that turns "the DLL was never injected" into a message naming the
    -- export instead of a call to address 0. Keep it.
    -- (CE's own celua.txt claims the default is true. The source disagrees; a
    -- user script CAN flip it, and then getAddress raises first with CE's message,
    -- which is equally fine -- both paths fail loudly. Do not "simplify" to one.)
    local fn = getAddress(name)
    if fn == nil or fn == 0 then error("[UE5Dissect] DLL function not found: " .. name) end
    -- On failure CE returns nil PLUS a reason string, and the six reasons point at
    -- six different problems ('Execution timeout', 'Failure launching thread',
    -- 'Failure reading the result address', ...). Report what CE already worked out
    -- instead of guessing -- docs/ce-plugin-sdk-notes.md 13.5.
    local ret, why = executeCodeEx(1, DLL_CALL_TIMEOUT_MS, fn, ...)
    if ret == nil then
        -- Level 0: no "file:line:" prefix. The useful coordinates are the export
        -- name and CE's reason, not a line of this script. Raising here also
        -- replaces the old per-call warn(), which fired once per FIELD -- 40
        -- fields meant 40 ungated lines over CE's Lua Engine window.
        error(string.format("[UE5Dissect] %s failed: %s", name, tostring(why)), 0)
    end
    return ret
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
local function addFieldsToStruct(ceStruct, classAddr, offsetBase, namePrefix, depth, maxDepth)
    depth = depth or 0
    offsetBase = offsetBase or 0
    namePrefix = namePrefix or ""
    maxDepth = maxDepth or MAX_STRUCT_DEPTH

    if depth > maxDepth then return end

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
                    displayName .. "." , depth + 1, maxDepth)
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
-- REMOVED 2026-08-17: fillGaps ("unused bytes shown as vtPointer").
--
-- It was defined, advertised in this header and in scripts/README.md +
-- DEPLOY_README.html, and never called from anywhere in the repo. It was also
-- wrong: it built its coverage set from element START offsets only
-- (`existing[Element[i].Offset] = true`) and then tested four consecutive
-- offsets, so an 8-byte field at 0x10 marked only 0x10 — 0x14 read as
-- uncovered and the loop would have emitted a vtPointer OVERLAPPING it. On a
-- real class it would add hundreds of unnamed rows, some of them overlaps.
--
-- Deleted rather than repaired (maintainer's call, audit #5 AA7): CE's own
-- autoGuessStruct already covers the no-override case, and a correct version
-- would have to track each field's real byte span while elements are added —
-- new, untested, visible behaviour in a shipped script that nobody asked for.
-- If it ever comes back, that is the shape to build: the DLL already hands us
-- f.size per field, so the coverage set must never be re-derived from CE's
-- element list.
-- ----------------------------------------------------------------

-- ----------------------------------------------------------------
-- Where UObject::OuterPrivate sits.
--
-- Every other header offset below is fixed across the UE versions we support,
-- but this one is NOT: on a WITH_CASE_PRESERVING_NAME build FName is 12 bytes
-- instead of 8, so OuterPrivate pads out to +0x28. The DLL already detects that
-- (DynOff::UOBJECT_OUTER, Genau) while this script asserted a flat 0x20 --
-- labelling the FName's DisplayIndex/Number pair as an 8-byte "Outer" pointer
-- and omitting the real one (audit #5 AA8).
--
-- Rather than mirror the DLL's detection (a second copy that can drift), ASK it
-- for the answer on this object and find which slot agrees:
--   * a null Outer proves nothing -- a root package legitimately has none, so
--     fall back rather than guess,
--   * and if BOTH slots happen to hold it, prefer 0x20: the common layout, and
--     an ambiguous read is not evidence for the rarer one.
--
-- Deliberately NOT memoised. CE keeps ONE Lua state for the whole session and
-- never rebuilds it (docs/CE-Bugs-Minesweeper.md 5), so a cached 0x28 would
-- follow the user onto the next, non-case-preserving game and corrupt every
-- structure built there. The probe is two reads and one export call.
--
-- No pcall: callDLL raises loudly by design (the AA5/AA6 fix), UE5_GetObjectOuter
-- predates the module-naming scheme so no DLL a user could plausibly pair with
-- this script lacks it, and swallowing here would re-introduce exactly the silent
-- failure that fix removed.
-- ----------------------------------------------------------------
local function detectOuterOffset(probeAddr)
    if not probeAddr or probeAddr == 0 then return 0x20 end
    local outer = callDLL("UE5_GetObjectOuter", probeAddr)
    if not outer or outer == 0 then return 0x20 end   -- root package: no evidence
    if readQword(probeAddr + 0x20) == outer then return 0x20 end
    if readQword(probeAddr + 0x28) == outer then return 0x28 end
    return 0x20
end

-- ----------------------------------------------------------------
-- Add standard UObject header fields (VTable, index, class, name, outer)
-- ----------------------------------------------------------------
local function addUObjectHeader(ceStruct, probeAddr)
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
    addIfMissing(detectOuterOffset(probeAddr), "Outer", vtPointer)
end

-- ----------------------------------------------------------------
-- PUBLIC: Create a CE Structure from a UClass address or object instance
--
-- Accepts either a UClass pointer or a UObject instance.
-- If the given address has no FField chain (i.e. it's an instance),
-- the function auto-resolves via UE5_GetObjectClass.
-- ----------------------------------------------------------------
function dissect.createFromClass(classAddr, structName, maxDepth)
    maxDepth = maxDepth or MAX_STRUCT_DEPTH  -- backward compatible default
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

    -- The walk raises on any DLL failure (see callDLL), so the build has to be
    -- unwound rather than left where it stopped. Without this, beginUpdate() had
    -- no matching endUpdate() and a half-walked structure was orphaned: never in
    -- structList, so clearAll() could never free it either. That was survivable
    -- while a failed call merely returned nil; it is the NORMAL failure path now.
    -- (audit #5 AA25, promoted from latent by the AA5/AA6 fix above.)
    local built, buildErr = pcall(function()
        addFieldsToStruct(ceStruct, classAddr, 0, "", 0, maxDepth)   -- walk class fields recursively
        addUObjectHeader(ceStruct, classAddr)                        -- add UObject base fields
    end)

    ceStruct.endUpdate()

    if not built then
        -- Never register or cache a partial structure. The whole point of AA6 is
        -- that a garbage structure presented as a success is worse than no
        -- structure: the user cannot tell it apart from a real one.
        pcall(function() ceStruct:Destroy() end)
        error(buildErr, 0)
    end

    -- Struct size, for the log line only -- a class whose PropertiesSize cannot be
    -- read is still a perfectly good dissect, so this must not undo the build.
    local okSize, propsSize = pcall(callDLL, "UE5_GetClassPropsSize", classAddr)
    if not okSize then propsSize = -1 end

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
local function dissectOverrideBody(ceStruct, instanceAddr)
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
    -- Probe the INSTANCE, not the class: both are UObjects, but the instance is
    -- the object this structure actually describes.
    addUObjectHeader(ceStruct, instanceAddr)
    ceStruct.endUpdate()

    if ceStruct.Count > 1 then
        return true   -- override accepted
    end
    return false      -- empty struct, rejected
end

-- Barrier for the two callbacks CE itself invokes.
--
-- A Lua error raised inside one of these does NOT stay in Lua.
-- TLuaCaller.StructureDissectEvent pcalls our function, and then re-raises the
-- failure as a PASCAL exception (LuaCaller.pas:1229-1232) into a dispatch loop
-- that has no handler at all (StructuresFrm2.pas:1451-1458). That skips the very
-- next line -- `if not UsedOverride then c.autoGuessStruct(...)` (:1460) -- so
-- CE's OWN structure guessing never runs either.
--
-- The blast radius is the whole session and every address: CE calls these for
-- ANY address the user opens Structure Dissect on, nothing auto-unregisters the
-- callback, and CE never rebuilds its Lua state. So an un-injected DLL (or a game
-- that exits) turns Structure Dissect into an error dialog for UObjects and plain
-- allocations alike until the user remembers dissect.disableAutoCallback().
--
-- Declining (false / nil) is the contract for "not mine" -- it is what lets CE
-- fall through -- so that is what a failure reports. Warned ONCE per session:
-- these fire per expanded node, and the hygiene rule exists so the Lua Engine
-- window never covers Cheat Engine. (audit #5 AA4)
local warnedCallbackFailure = false
local function callbackBarrier(what, fn, ...)
    local ok, a, b = pcall(fn, ...)
    if ok then return a, b end
    if not warnedCallbackFailure then
        warnedCallbackFailure = true
        warn("auto-dissect %s failed, letting CE handle it: %s\n" ..
             "         (reported once; run dissect.disableAutoCallback() to unregister)",
             what, tostring(a))
    end
    return nil
end

local function dissectOverrideCallback(ceStruct, instanceAddr)
    -- false, not nil: both make CE fall through, and false is what the success
    -- path already returns for "empty struct, rejected".
    return callbackBarrier("override", dissectOverrideBody, ceStruct, instanceAddr) or false
end

-- ----------------------------------------------------------------
-- Callback: resolve address to a UObject name for dissect title bar
-- ----------------------------------------------------------------
local function nameLookupBody(address)
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

local function nameLookupCallback(address)
    -- Same barrier, same reason -- see callbackBarrier. nil = "no name from me".
    return callbackBarrier("name lookup", nameLookupBody, address)
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
