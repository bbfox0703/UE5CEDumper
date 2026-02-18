// ============================================================
// ExportAPI.cpp — C ABI export implementation
// ============================================================

#include "ExportAPI.h"
#include "Logger.h"
#include "OffsetFinder.h"
#include "ObjectArray.h"
#include "FNamePool.h"
#include "UStructWalker.h"
#include "PipeServer.h"

#include <string>
#include <cstring>
#include <mutex>

// Global cached state (also accessed by PipeServer)
uintptr_t g_cachedGObjects  = 0;
uintptr_t g_cachedGNames    = 0;
uintptr_t g_cachedGWorld    = 0;
uint32_t  g_cachedUEVersion = 0;

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

    // Initialize subsystems
    FNamePool::Init(ptrs.GNames);
    ObjectArray::Init(ptrs.GObjects);

    s_initialized = true;
    LOG_INFO("UE5_Init: Complete (UE%u, GObjects=0x%llX, GNames=0x%llX, Objects=%d)",
             ptrs.UEVersion,
             static_cast<unsigned long long>(ptrs.GObjects),
             static_cast<unsigned long long>(ptrs.GNames),
             ObjectArray::GetCount());

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
    if (!UE5_Init()) return false;
    return UE5_StartPipeServer();
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
