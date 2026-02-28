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

#include <string>
#include <cstring>
#include <mutex>

// Global cached state (also accessed by PipeServer)
uintptr_t   g_cachedGObjects  = 0;
uintptr_t   g_cachedGNames    = 0;
uintptr_t   g_cachedGWorld    = 0;
uint32_t    g_cachedUEVersion = 0;
bool        g_cachedVersionDetected = true;  // false if UE version detection failed (PE + memory scan)
const char* g_cachedGObjectsMethod = "not_found";  // "aob", "data_scan", "not_found"
const char* g_cachedGNamesMethod   = "not_found";  // "aob", "string_ref", "pointer_scan", "not_found"
const char* g_cachedGWorldMethod   = "not_found";  // "aob", "not_found"

static bool        s_initialized = false;
static PipeServer  s_pipeServer;
static std::mutex  s_walkMutex;
static ClassInfo   s_walkCache;

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
    if (!OffsetFinder::FindAll(ptrs)) {
        LOG_ERROR("UE5_Init: OffsetFinder::FindAll failed");
        return false;
    }

    g_cachedGObjects  = ptrs.GObjects;
    g_cachedGNames    = ptrs.GNames;
    g_cachedGWorld    = ptrs.GWorld;
    g_cachedUEVersion = ptrs.UEVersion;
    g_cachedVersionDetected = ptrs.bVersionDetected;
    g_cachedGObjectsMethod  = ptrs.gobjectsMethod;
    g_cachedGNamesMethod    = ptrs.gnamesMethod;
    g_cachedGWorldMethod    = ptrs.gworldMethod;

    // Initialize subsystems
    if (ptrs.bUE4NameArray) {
        FNamePool::InitUE4(ptrs.GNames, ptrs.ue4StringOffset);
    } else {
        FNamePool::Init(ptrs.GNames, ptrs.fnameEntryHeaderOffset);
    }
    ObjectArray::Init(ptrs.GObjects);

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
    // If version detection failed or misidentified as UE5, correct it now.
    // (e.g., FF7 Remake is UE4.18 but version defaults to 504 when detection fails)
    if (!DynOff::bUseFProperty && ptrs.UEVersion >= 500) {
        LOG_WARN("UE5_Init: UProperty mode detected (no FProperty) but version=%u (>= 500). "
                 "Overriding to 424 (UE4 pre-4.25)", ptrs.UEVersion);
        ptrs.UEVersion = 424;
        g_cachedUEVersion = ptrs.UEVersion;  // Update cached value for pipe protocol
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
    LOG_SUMMARY("DynOff: CPN=%s FProp=%s Outer=+0x%02X Super=+0x%02X ChildProps=+0x%02X Name=+0x%02X Offset=+0x%02X validated=%s",
                DynOff::bCasePreservingName ? "yes" : "no",
                DynOff::bUseFProperty ? "yes" : "no",
                DynOff::UOBJECT_OUTER,
                DynOff::USTRUCT_SUPER, DynOff::USTRUCT_CHILDPROPS,
                DynOff::bUseFProperty ? DynOff::FFIELD_NAME : Constants::OFF_UOBJECT_NAME,
                DynOff::bUseFProperty ? DynOff::FPROPERTY_OFFSET : DynOff::UPROPERTY_OFFSET,
                DynOff::bOffsetsValidated.load(std::memory_order_acquire) ? "yes" : "no");

    // Switch to Pipe channel — all subsequent runtime logging goes to pipe file
    Logger::SetChannel(LogChannel::Pipe);

    return true;
}

void UE5_Shutdown() {
    LOG_INFO("UE5_Shutdown: Cleaning up...");
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
    if (!UE5_Init()) {
        LOG_ERROR("UE5_AutoStart: UE5_Init failed — aborting pipe server start");
        return false;
    }
    bool ok = UE5_StartPipeServer();
    LOG_INFO("UE5_AutoStart: pipe server %s", ok ? "started" : "FAILED to start");
    return ok;
}

bool UE5_StartPipeServer() {
    return s_pipeServer.Start();
}

void UE5_StopPipeServer() {
    s_pipeServer.Stop();
}

bool UE5_IsPipeConnected() {
    return s_pipeServer.IsClientConnected();
}

} // extern "C"
