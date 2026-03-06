// ============================================================
// ExportAPI.cpp — C ABI export implementation
// ============================================================

#include "ExportAPI.h"
#define LOG_CAT "INIT"
#include "Logger.h"
#include "BuildInfo.h"
#include "Constants.h"
#include "Memory.h"
#include "OffsetFinder.h"
#include "ObjectArray.h"
#include "FNamePool.h"
#include "UStructWalker.h"
#include "PipeServer.h"
#include "GameThreadDispatch.h"
#include "Mailbox.h"

#include <string>
#include <cstring>
#include <mutex>
#include <algorithm>

// Global cached state (also accessed by PipeServer)
uintptr_t   g_cachedGObjects  = 0;
uintptr_t   g_cachedGNames    = 0;
uintptr_t   g_cachedGWorld    = 0;
uint32_t    g_cachedUEVersion = 0;
bool        g_cachedVersionDetected = true;  // false if UE version detection failed (PE + memory scan)
const char* g_cachedGObjectsMethod = "not_found";  // "aob", "data_scan", "not_found"
const char* g_cachedGNamesMethod   = "not_found";  // "aob", "string_ref", "pointer_scan", "not_found"
const char* g_cachedGWorldMethod   = "not_found";  // "aob", "not_found"

// AOB Usage Tracking: PE hash, winning pattern IDs, scan statistics
char        g_cachedPeHash[17] = {0};
const char* g_cachedGObjectsPatternId = nullptr;
const char* g_cachedGNamesPatternId   = nullptr;
const char* g_cachedGWorldPatternId   = nullptr;
int         g_cachedGObjectsTried = 0, g_cachedGObjectsHit = 0;
int         g_cachedGNamesTried   = 0, g_cachedGNamesHit   = 0;
int         g_cachedGWorldTried   = 0, g_cachedGWorldHit   = 0;
uintptr_t   g_cachedGObjectsScanAddr = 0;
uintptr_t   g_cachedGNamesScanAddr   = 0;
uintptr_t   g_cachedGWorldScanAddr   = 0;
const char* g_cachedGWorldAob    = nullptr;
int         g_cachedGWorldAobPos = 0;
int         g_cachedGWorldAobLen = 0;

static bool        s_initialized = false;
static PipeServer  s_pipeServer;
static std::mutex  s_walkMutex;
static ClassInfo   s_walkCache;

// Global scan progress for UI polling (updated by UE5_Init, read by scan_status)
namespace ScanProgress {
    std::atomic<int>  phase{0};
    std::string       statusText;
    std::mutex        statusMutex;

    void Set(int p, const char* text) {
        phase.store(p, std::memory_order_release);
        std::lock_guard<std::mutex> lock(statusMutex);
        statusText = text;
    }
    std::string GetStatusText() {
        std::lock_guard<std::mutex> lock(statusMutex);
        return statusText;
    }
}

// Helper: copy string to buffer safely
static bool CopyToBuffer(const std::string& src, char* buf, int32_t bufLen) {
    if (!buf || bufLen <= 0) return false;
    size_t copyLen = (std::min)(src.size(), static_cast<size_t>(bufLen - 1));
    memcpy(buf, src.c_str(), copyLen);
    buf[copyLen] = '\0';
    return true;
}

extern "C" {

bool UE5_Init() {
    if (s_initialized) {
        LOG_WARN("UE5_Init: Already initialized");
        return true;
    }

    LOG_INFO("UE5_Init: Starting initialization...");

    OffsetFinder::EnginePointers ptrs;
    OffsetFinder::FindAll(ptrs, [](int phase, const char* text) {
        ScanProgress::Set(phase, text);
    });

    g_cachedGObjects  = ptrs.GObjects;
    g_cachedGNames    = ptrs.GNames;
    g_cachedGWorld    = ptrs.GWorld;
    g_cachedUEVersion = ptrs.UEVersion;
    g_cachedVersionDetected = ptrs.bVersionDetected;
    g_cachedGObjectsMethod  = ptrs.gobjectsMethod;
    g_cachedGNamesMethod    = ptrs.gnamesMethod;
    g_cachedGWorldMethod    = ptrs.gworldMethod;

    // AOB Usage Tracking
    memcpy(g_cachedPeHash, ptrs.peHash, sizeof(g_cachedPeHash));
    g_cachedGObjectsPatternId = ptrs.gobjectsPatternId;
    g_cachedGNamesPatternId   = ptrs.gnamesPatternId;
    g_cachedGWorldPatternId   = ptrs.gworldPatternId;
    g_cachedGObjectsTried = ptrs.gobjectsPatternsTried;
    g_cachedGObjectsHit   = ptrs.gobjectsPatternsHit;
    g_cachedGNamesTried   = ptrs.gnamesPatternsTried;
    g_cachedGNamesHit     = ptrs.gnamesPatternsHit;
    g_cachedGWorldTried   = ptrs.gworldPatternsTried;
    g_cachedGWorldHit     = ptrs.gworldPatternsHit;
    g_cachedGObjectsScanAddr = ptrs.gobjectsScanAddr;
    g_cachedGNamesScanAddr   = ptrs.gnamesScanAddr;
    g_cachedGWorldScanAddr   = ptrs.gworldScanAddr;
    g_cachedGWorldAob    = ptrs.gworldAob;
    g_cachedGWorldAobPos = ptrs.gworldAobPos;
    g_cachedGWorldAobLen = ptrs.gworldAobLen;

    // Initialize subsystems — only when their pointer was found
    ScanProgress::Set(5, "Initializing subsystems...");
    if (ptrs.GNames) {
        if (ptrs.bUE4NameArray) {
            FNamePool::InitUE4(ptrs.GNames, ptrs.ue4StringOffset);
        } else {
            FNamePool::Init(ptrs.GNames, ptrs.fnameEntryHeaderOffset);
        }
    }
    if (ptrs.GObjects) {
        ObjectArray::Init(ptrs.GObjects);
    }

    // Sanity check + dynamic offset detection — only when BOTH are available
    ScanProgress::Set(6, "Validating offsets...");
    if (ptrs.GObjects && ptrs.GNames) {
        // Quick sanity check: verify name resolution works for a few objects
        {
            int verified = 0, tested = 0;
            for (int32_t i = 0; i < ObjectArray::GetCount() && tested < 10; ++i) {
                uintptr_t obj = ObjectArray::GetByIndex(i);
                if (!obj) continue;
                ++tested;
                uint32_t nameIdx = 0;
                if (Mem::ReadSafe(obj + Constants::OFF_UOBJECT_NAME, nameIdx)) {
                    std::string name = FNamePool::GetString(nameIdx);
                    if (!name.empty() && name != "None") {
                        ++verified;
                        if (verified <= 3) {
                            LOG_INFO("UE5_Init: Sanity obj[%d] name='%s' (idx=%u)", i, name.c_str(), nameIdx);
                        }
                    }
                }
            }
            LOG_INFO("UE5_Init: Name sanity: %d/%d objects resolved", verified, tested);
            if (verified == 0 && tested >= 5) {
                LOG_WARN("UE5_Init: WARNING — No objects resolved names! Check FUObjectItem size or FNamePool.");
            }
        }

        // Dynamically detect FField/FProperty/UStruct offsets
        // Must be called AFTER FNamePool + ObjectArray are initialized
        if (!OffsetFinder::ValidateAndFixOffsets(ptrs.UEVersion)) {
            LOG_WARN("UE5_Init: Offset validation failed — using default offsets (may be wrong for this UE version)");
        }

        // Post-DynOff version correction: UProperty mode definitively means UE4 pre-4.25.
        if (!DynOff::bUseFProperty && ptrs.UEVersion >= 500) {
            uint32_t corrected = ObjectArray::IsFlat() ? 418 : 424;
            LOG_WARN("UE5_Init: UProperty mode detected (no FProperty) but version=%u (>= 500). "
                     "Overriding to %u (flat=%s)", ptrs.UEVersion, corrected,
                     ObjectArray::IsFlat() ? "yes" : "no");
            ptrs.UEVersion = corrected;
            g_cachedUEVersion = ptrs.UEVersion;
        }
    } else {
        LOG_WARN("UE5_Init: Partial init — GObjects=%s GNames=%s — skipping offset validation",
                 ptrs.GObjects ? "OK" : "MISSING", ptrs.GNames ? "OK" : "MISSING");
    }

    s_initialized = true;
    LOG_INFO("UE5_Init: Complete (UE%u, GObjects=0x%llX, GNames=0x%llX, Objects=%d)",
             ptrs.UEVersion,
             static_cast<unsigned long long>(ptrs.GObjects),
             static_cast<unsigned long long>(ptrs.GNames),
             ObjectArray::GetCount());

    // Condensed summary for quick scan-log triage
    LOG_SUMMARY("build=%s config=%s UE=%u",
                BUILD_GIT_SHORT, BUILD_CONFIG, ptrs.UEVersion);
    LOG_SUMMARY("GObjects=0x%llX GNames=0x%llX GWorld=0x%llX Objects=%d",
                static_cast<unsigned long long>(ptrs.GObjects),
                static_cast<unsigned long long>(ptrs.GNames),
                static_cast<unsigned long long>(ptrs.GWorld),
                ObjectArray::GetCount());
    LOG_SUMMARY("DynOff: CPN=%s FProp=%s TagFFV=%s Outer=+0x%02X validated=%s",
                DynOff::bCasePreservingName ? "yes" : "no",
                DynOff::bUseFProperty ? "yes" : "no",
                DynOff::bTaggedFFieldVariant ? "yes" : "no",
                DynOff::UOBJECT_OUTER,
                DynOff::bOffsetsValidated.load(std::memory_order_acquire) ? "yes" : "no");
    LOG_SUMMARY("  UStruct: Super=+0x%02X Children=+0x%02X ChildProps=+0x%02X PropsSize=+0x%02X",
                DynOff::USTRUCT_SUPER, DynOff::USTRUCT_CHILDREN,
                DynOff::USTRUCT_CHILDPROPS, DynOff::USTRUCT_PROPSSIZE);
    if (DynOff::bUseFProperty) {
        LOG_SUMMARY("  FField: Next=+0x%02X Name=+0x%02X | FProp: Offset=+0x%02X ElemSize=+0x%02X StructProp=+0x%02X",
                    DynOff::FFIELD_NEXT, DynOff::FFIELD_NAME,
                    DynOff::FPROPERTY_OFFSET, DynOff::FPROPERTY_ELEMSIZE, DynOff::FSTRUCTPROP_STRUCT);
    } else {
        LOG_SUMMARY("  UProperty: Next=+0x%02X Offset=+0x%02X ElemSize=+0x%02X",
                    DynOff::UFIELD_NEXT, DynOff::UPROPERTY_OFFSET, DynOff::UPROPERTY_ELEMSIZE);
    }

    ScanProgress::Set(7, "Complete");

    // Switch to Pipe channel — all subsequent runtime logging goes to pipe file
    Logger::SetChannel(LogChannel::Pipe);

    return true;
}

void UE5_Shutdown() {
    LOG_INFO("UE5_Shutdown: Cleaning up...");
    Mailbox::StopThread();
    GameThreadDispatch::RemoveHook();
    s_pipeServer.Stop();
    s_initialized = false;
}

uint32_t UE5_GetVersion() {
    return g_cachedUEVersion;
}

uintptr_t UE5_GetGObjectsAddr() {
    return g_cachedGObjects;
}

uintptr_t UE5_GetGNamesAddr() {
    return g_cachedGNames;
}

void UE5_SetObjectDecryption(uintptr_t (*decryptFunc)(uintptr_t)) {
    ObjectArray::SetDecryptFunc(decryptFunc);
    LOG_INFO("UE5_SetObjectDecryption: %s",
             decryptFunc ? "Custom decryption set" : "Decryption cleared");
}

int32_t UE5_GetObjectCount() {
    return ObjectArray::GetCount();
}

uintptr_t UE5_GetObjectByIndex(int32_t index) {
    return ObjectArray::GetByIndex(index);
}

bool UE5_GetObjectName(uintptr_t obj, char* buf, int32_t bufLen) {
    std::string name = UStructWalker::GetName(obj);
    return CopyToBuffer(name, buf, bufLen);
}

bool UE5_GetObjectFullName(uintptr_t obj, char* buf, int32_t bufLen) {
    std::string name = UStructWalker::GetFullName(obj);
    return CopyToBuffer(name, buf, bufLen);
}

uintptr_t UE5_GetObjectClass(uintptr_t obj) {
    return UStructWalker::GetClass(obj);
}

uintptr_t UE5_GetObjectOuter(uintptr_t obj) {
    return UStructWalker::GetOuter(obj);
}

uintptr_t UE5_FindObject(const char* fullPath) {
    if (!fullPath) return 0;
    return ObjectArray::FindByName(fullPath);
}

uintptr_t UE5_FindClass(const char* className) {
    if (!className) return 0;

    uintptr_t result = 0;
    ObjectArray::ForEach([&](int32_t /*idx*/, uintptr_t obj) -> bool {
        uintptr_t cls = UStructWalker::GetClass(obj);
        if (!cls) return true;

        std::string clsName = UStructWalker::GetName(cls);
        if (clsName == "Class") {
            std::string objName = UStructWalker::GetName(obj);
            if (objName == className) {
                result = obj;
                return false;
            }
        }
        return true;
    });
    return result;
}

int32_t UE5_WalkClassBegin(uintptr_t uclassAddr) {
    std::lock_guard<std::mutex> lock(s_walkMutex);
    s_walkCache = UStructWalker::WalkClass(uclassAddr);
    return static_cast<int32_t>(s_walkCache.Fields.size());
}

bool UE5_WalkClassGetField(int32_t index,
                           uintptr_t* outAddr,
                           char* nameOut, int32_t nameBufLen,
                           char* typeOut, int32_t typeBufLen,
                           int32_t* offsetOut,
                           int32_t* sizeOut)
{
    std::lock_guard<std::mutex> lock(s_walkMutex);
    if (index < 0 || index >= static_cast<int32_t>(s_walkCache.Fields.size())) return false;

    const auto& field = s_walkCache.Fields[index];
    if (outAddr)   *outAddr   = field.Address;
    if (offsetOut) *offsetOut = field.Offset;
    if (sizeOut)   *sizeOut   = field.Size;

    CopyToBuffer(field.Name, nameOut, nameBufLen);
    CopyToBuffer(field.TypeName, typeOut, typeBufLen);

    return true;
}

void UE5_WalkClassEnd() {
    std::lock_guard<std::mutex> lock(s_walkMutex);
    s_walkCache = ClassInfo{};
}

bool UE5_ResolveFName(uint64_t fname, char* buf, int32_t bufLen) {
    int32_t compIndex = static_cast<int32_t>(fname & 0xFFFFFFFF);
    int32_t number    = static_cast<int32_t>((fname >> 32) & 0xFFFFFFFF);

    std::string name = FNamePool::GetString(compIndex, number);
    return CopyToBuffer(name, buf, bufLen);
}

bool UE5_AutoStart() {
    // Called by CEPlugin's InjectDLL after the DLL is loaded into the game.
    // Idempotent: UE5_Init checks s_initialized and skips if already done.
    LOG_INFO("UE5_AutoStart: entry");
    UE5_Init();  // Always succeeds (partial init is OK — Extra Scan can recover)
    bool ok = UE5_StartPipeServer();
    LOG_INFO("UE5_AutoStart: pipe server %s", ok ? "started" : "FAILED to start");
    return ok;
}

// === Property Detail Queries (for CE Lua dissect) ===

int32_t UE5_GetFieldBoolMask(uintptr_t fieldAddr) {
    if (!fieldAddr) return 0;
    // FBoolProperty: { FieldSize(1), ByteOffset(1), ByteMask(1), FieldMask(1) }
    // at DynOff::FBOOLPROP_FIELDSIZE. Probe nearby offsets for version variance.
    for (int tryOff : { DynOff::FBOOLPROP_FIELDSIZE, DynOff::FBOOLPROP_FIELDSIZE - 4,
                        DynOff::FBOOLPROP_FIELDSIZE + 4, DynOff::FBOOLPROP_FIELDSIZE + 8 }) {
        if (tryOff < 0) continue;
        uint8_t boolBytes[4] = {};
        if (Mem::ReadBytesSafe(fieldAddr + tryOff, boolBytes, 4)) {
            uint8_t fieldSize = boolBytes[0];
            uint8_t fieldMask = boolBytes[3];
            if (fieldSize >= 1 && fieldSize <= 8 && fieldMask != 0 && (fieldMask & (fieldMask - 1)) == 0) {
                return static_cast<int32_t>(fieldMask);
            }
        }
    }
    return 0;
}

uintptr_t UE5_GetFieldStructClass(uintptr_t fieldAddr) {
    if (!fieldAddr) return 0;
    // FStructProperty stores UScriptStruct* at DynOff::FSTRUCTPROP_STRUCT.
    constexpr int kDeltas[] = { 0, -8, 8, -16, 16, 4, -4, 12 };
    for (int delta : kDeltas) {
        int tryOff = DynOff::FSTRUCTPROP_STRUCT + delta;
        if (tryOff < 0) continue;
        uintptr_t structPtr = 0;
        if (Mem::ReadSafe(fieldAddr + tryOff, structPtr) && structPtr) {
            std::string sname = UStructWalker::GetName(structPtr);
            if (!sname.empty() && sname != "None") return structPtr;
        }
    }
    return 0;
}

uintptr_t UE5_GetFieldPropertyClass(uintptr_t fieldAddr) {
    // FObjectPropertyBase::PropertyClass sits at the same offset as
    // FStructProperty::Struct (DynOff::FSTRUCTPROP_STRUCT).
    // Delegate to the same probe logic — both store a UClass*/UScriptStruct*.
    return UE5_GetFieldStructClass(fieldAddr);
}

int32_t UE5_GetClassPropsSize(uintptr_t classAddr) {
    if (!classAddr) return 0;
    int32_t propsSize = 0;
    Mem::ReadSafe(classAddr + DynOff::USTRUCT_PROPSSIZE, propsSize);
    return propsSize;
}

// === UFunction Invocation ===

uintptr_t UE5_FindInstanceOfClass(const char* className) {
    if (!className || !className[0]) return 0;

    auto rset = ObjectArray::FindInstancesByClass(className, false, 100);

    // Prefer non-CDO instance
    for (const auto& r : rset.results) {
        if (r.addr && r.name.find("Default__") == std::string::npos) {
            LOG_INFO("UE5_FindInstanceOfClass: '%s' -> 0x%llX (%s)",
                     className, (unsigned long long)r.addr, r.name.c_str());
            return r.addr;
        }
    }

    // Fallback: return first result even if CDO
    if (!rset.results.empty() && rset.results[0].addr) {
        LOG_WARN("UE5_FindInstanceOfClass: '%s' -> only CDO: 0x%llX (%s)",
                 className, (unsigned long long)rset.results[0].addr,
                 rset.results[0].name.c_str());
        return rset.results[0].addr;
    }

    LOG_WARN("UE5_FindInstanceOfClass: '%s' -> not found (scanned=%d)",
             className, rset.scanned);
    return 0;
}

uintptr_t UE5_FindFunctionByName(uintptr_t classAddr, const char* funcName) {
    if (!classAddr || !funcName || !funcName[0]) return 0;

    auto funcs = UStructWalker::WalkFunctions(classAddr);

    // Exact match
    for (const auto& f : funcs) {
        if (f.name == funcName) {
            LOG_INFO("UE5_FindFunctionByName: '%s' -> 0x%llX (exact match)",
                     funcName, (unsigned long long)f.address);
            return f.address;
        }
    }

    // Case-insensitive fallback
    std::string lower(funcName);
    std::transform(lower.begin(), lower.end(), lower.begin(),
                   [](unsigned char c) { return static_cast<char>(std::tolower(c)); });
    for (const auto& f : funcs) {
        std::string fl = f.name;
        std::transform(fl.begin(), fl.end(), fl.begin(),
                       [](unsigned char c) { return static_cast<char>(std::tolower(c)); });
        if (fl == lower) {
            LOG_INFO("UE5_FindFunctionByName: '%s' -> 0x%llX (case-insensitive)",
                     funcName, (unsigned long long)f.address);
            return f.address;
        }
    }

    LOG_WARN("UE5_FindFunctionByName: '%s' not found (%d functions walked)",
             funcName, (int)funcs.size());
    return 0;
}

// ProcessEvent vtable detection — version-based with ±2 probing
static int DetectProcessEventVTableOffset() {
    // Empirical vtable byte offsets for UObject::ProcessEvent (Shipping builds, MSVC x64)
    //   UE 4.18-4.19: 0x208 (index 65)
    //   UE 4.20-4.24: 0x210 (index 66)
    //   UE 4.25-4.27: 0x218 (index 67)
    //   UE 5.0-5.4:   0x220 (index 68)
    //   UE 5.5+:      0x228 (index 69)
    int primary;
    if (g_cachedUEVersion >= 550)      primary = 0x228;
    else if (g_cachedUEVersion >= 500) primary = 0x220;
    else if (g_cachedUEVersion >= 425) primary = 0x218;
    else if (g_cachedUEVersion >= 420) primary = 0x210;
    else                               primary = 0x208;

    // Find any valid UObject to read vtable from
    uintptr_t testObj = 0;
    for (int i = 0; i < ObjectArray::GetCount() && i < 200; ++i) {
        testObj = ObjectArray::GetByIndex(i);
        if (testObj) break;
    }
    if (!testObj) {
        LOG_ERROR("DetectProcessEvent: no valid UObject in GObjects");
        return -1;
    }

    uintptr_t vtable = 0;
    if (!Mem::ReadSafe(testObj, vtable) || !vtable) {
        LOG_ERROR("DetectProcessEvent: cannot read vtable from obj 0x%llX",
                  (unsigned long long)testObj);
        return -1;
    }

    // Log nearby vtable entries for debugging
    LOG_INFO("DetectProcessEvent: UE=%u primary=0x%X vtable=0x%llX",
             g_cachedUEVersion, primary, (unsigned long long)vtable);
    for (int delta = -16; delta <= 16; delta += 8) {
        int off = primary + delta;
        if (off < 0) continue;
        uintptr_t addr = 0;
        Mem::ReadSafe(vtable + off, addr);
        LOG_INFO("  vtable+0x%03X = 0x%llX%s",
                 off, (unsigned long long)addr,
                 delta == 0 ? "  <-- primary" : "");
    }

    // Validate primary: must point to readable code
    uintptr_t funcAddr = 0;
    if (Mem::ReadSafe(vtable + primary, funcAddr) && funcAddr) {
        uint8_t test = 0;
        if (Mem::ReadBytesSafe(funcAddr, &test, 1)) {
            LOG_INFO("DetectProcessEvent: using primary 0x%X -> 0x%llX",
                     primary, (unsigned long long)funcAddr);
            return primary;
        }
    }

    // Probe ±8, ±16
    for (int d : { 8, -8, 16, -16 }) {
        int off = primary + d;
        if (off < 0) continue;
        funcAddr = 0;
        if (Mem::ReadSafe(vtable + off, funcAddr) && funcAddr) {
            uint8_t test = 0;
            if (Mem::ReadBytesSafe(funcAddr, &test, 1)) {
                LOG_WARN("DetectProcessEvent: primary 0x%X failed, using 0x%X -> 0x%llX",
                         primary, off, (unsigned long long)funcAddr);
                return off;
            }
        }
    }

    LOG_ERROR("DetectProcessEvent: all probes failed");
    return -1;
}

static int s_processEventOffset = -2;  // -2 = not yet detected

/// Resolve the actual ProcessEvent function address from any valid UObject's vtable.
/// Used both for direct calls and for installing the game-thread hook.
static uintptr_t ResolveProcessEventAddr() {
    if (s_processEventOffset == -2) {
        s_processEventOffset = DetectProcessEventVTableOffset();
    }
    if (s_processEventOffset < 0) return 0;

    // Find any valid UObject to read its vtable
    uintptr_t testObj = 0;
    for (int idx = 1; idx < 100; idx++) {
        auto* item = ObjectArray::GetItem(idx);
        if (item && item->Object) { testObj = item->Object; break; }
    }
    if (!testObj) return 0;

    uintptr_t vtable = 0;
    if (!Mem::ReadSafe(testObj, vtable) || !vtable) return 0;

    uintptr_t peAddr = 0;
    if (!Mem::ReadSafe(vtable + s_processEventOffset, peAddr) || !peAddr) return 0;

    return peAddr;
}

/// Try to install the game-thread ProcessEvent hook.
/// Called lazily on first UE5_CallProcessEvent invocation.
static void TryInstallGameThreadHook() {
    static bool s_hookAttempted = false;
    if (s_hookAttempted) return;
    s_hookAttempted = true;

    uintptr_t peAddr = ResolveProcessEventAddr();
    if (!peAddr) {
        LOG_WARN("GameThreadDispatch: cannot resolve ProcessEvent address for hooking");
        return;
    }

    if (GameThreadDispatch::InstallHook(peAddr)) {
        LOG_INFO("GameThreadDispatch: hook installed, invoke will use game-thread dispatch");
    } else {
        LOG_WARN("GameThreadDispatch: hook install failed, invoke will use direct call (unsafe)");
    }
}

int32_t UE5_CallProcessEvent(uintptr_t instance, uintptr_t ufunc, uintptr_t params) {
    if (!instance || !ufunc) return -1;

    // Lazy detection
    if (s_processEventOffset == -2) {
        s_processEventOffset = DetectProcessEventVTableOffset();
        TryInstallGameThreadHook();
    }
    if (s_processEventOffset < 0) return -3;

    // Prefer game-thread dispatch via hook
    if (GameThreadDispatch::IsHookActive()) {
        LOG_INFO("UE5_CallProcessEvent: dispatching to game thread inst=0x%llX func=0x%llX",
                 (unsigned long long)instance, (unsigned long long)ufunc);
        return GameThreadDispatch::EnqueueInvoke(instance, ufunc, params);
    }

    // Fallback: direct call from current thread (unsafe for state-changing functions)
    LOG_WARN("UE5_CallProcessEvent: hook not active, using direct call (unsafe)");

    // Read vtable from the target instance
    uintptr_t vtable = 0;
    if (!Mem::ReadSafe(instance, vtable) || !vtable) return -2;

    uintptr_t peAddr = 0;
    if (!Mem::ReadSafe(vtable + s_processEventOffset, peAddr) || !peAddr) return -3;

    typedef void (__fastcall *FnProcessEvent)(void*, void*, void*);
    auto pProcessEvent = reinterpret_cast<FnProcessEvent>(peAddr);

    LOG_INFO("UE5_CallProcessEvent: direct call inst=0x%llX func=0x%llX pe=0x%llX",
             (unsigned long long)instance, (unsigned long long)ufunc,
             (unsigned long long)peAddr);

    __try {
        pProcessEvent(reinterpret_cast<void*>(instance),
                      reinterpret_cast<void*>(ufunc),
                      reinterpret_cast<void*>(params));
    } __except(EXCEPTION_EXECUTE_HANDLER) {
        LOG_ERROR("UE5_CallProcessEvent: EXCEPTION during direct ProcessEvent call!");
        return -4;
    }

    LOG_INFO("UE5_CallProcessEvent: direct call success (warn: not game-thread)");
    return 0;
}

// === Mailbox ===

uintptr_t UE5_GetMailboxAddr() {
    return Mailbox::GetAddress();
}

// === Pipe Server ===

bool UE5_StartPipeServer() {
    // Guard: if another UE5Dumper instance (e.g., proxy DLL) already owns the pipe,
    // skip starting a competing pipe server to avoid connection failures.
    HANDLE testPipe = CreateFileW(
        Constants::PIPE_NAME,
        GENERIC_READ, 0, nullptr,
        OPEN_EXISTING, 0, nullptr);
    if (testPipe != INVALID_HANDLE_VALUE) {
        CloseHandle(testPipe);
        LOG_WARN("UE5_StartPipeServer: pipe already exists (another instance running) — skipping");
        return true;  // return true so CE Lua doesn't treat it as failure
    }
    return s_pipeServer.Start();
}

void UE5_StopPipeServer() {
    s_pipeServer.Stop();
}

bool UE5_IsPipeConnected() {
    return s_pipeServer.IsClientConnected();
}

} // extern "C"
