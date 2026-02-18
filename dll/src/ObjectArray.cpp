// ============================================================
// ObjectArray.cpp — FChunkedFixedUObjectArray implementation
// ============================================================

#include "ObjectArray.h"
#include "Memory.h"
#include "Logger.h"
#include "Constants.h"
#include "FNamePool.h"

namespace ObjectArray {

// FUObjectArray layout offsets (auto-detected)
struct ArrayLayout {
    int32_t objectsOffset;    // FUObjectItem** Objects
    int32_t maxElementsOffset;
    int32_t numElementsOffset;
    int32_t maxChunksOffset;
    int32_t numChunksOffset;
};

static uintptr_t  s_arrayAddr = 0;
static ArrayLayout s_layout = { 0x00, 0x10, 0x14, 0x18, 0x1C }; // Default layout

static bool DetectLayout(uintptr_t addr) {
    // Default layout: Objects at 0x00, then PreAllocatedObjects, then MaxElements, NumElements
    int32_t numAtDefault = 0;
    int32_t maxAtDefault = 0;
    Mem::ReadSafe(addr + 0x14, numAtDefault);
    Mem::ReadSafe(addr + 0x10, maxAtDefault);

    if (numAtDefault > 0 && numAtDefault <= maxAtDefault && maxAtDefault <= 0x800000) {
        s_layout = { 0x00, 0x10, 0x14, 0x18, 0x1C };
        LOG_INFO("ObjectArray: Default layout detected (Num=%d, Max=%d)", numAtDefault, maxAtDefault);
        return true;
    }

    // Alternate layout (some games): Objects at 0x10, NumElements at 0x04
    int32_t numAtAlt = 0;
    Mem::ReadSafe(addr + 0x04, numAtAlt);

    if (numAtAlt > 0 && numAtAlt <= 0x800000) {
        s_layout = { 0x10, 0x08, 0x04, 0x0C, -1 };
        LOG_INFO("ObjectArray: Alternate layout detected (Num=%d)", numAtAlt);
        return true;
    }

    LOG_WARN("ObjectArray: Could not detect layout, using default");
    return true;
}

void Init(uintptr_t gobjectsAddr) {
    s_arrayAddr = gobjectsAddr;
    DetectLayout(gobjectsAddr);
    LOG_INFO("ObjectArray: Initialized at 0x%llX, Count=%d",
             static_cast<unsigned long long>(gobjectsAddr), GetCount());
}

int32_t GetCount() {
    if (!s_arrayAddr) return 0;
    int32_t count = 0;
    Mem::ReadSafe(s_arrayAddr + s_layout.numElementsOffset, count);
    return count;
}

int32_t GetMax() {
    if (!s_arrayAddr) return 0;
    int32_t max = 0;
    Mem::ReadSafe(s_arrayAddr + s_layout.maxElementsOffset, max);
    return max;
}

uintptr_t GetByIndex(int32_t index) {
    if (!s_arrayAddr || index < 0 || index >= GetCount()) return 0;

    // Read chunk table pointer
    uintptr_t chunkTable = 0;
    if (!Mem::ReadSafe(s_arrayAddr + s_layout.objectsOffset, chunkTable) || !chunkTable) return 0;

    int32_t chunkIndex = index / Constants::OBJECTS_PER_CHUNK;
    int32_t withinChunk = index % Constants::OBJECTS_PER_CHUNK;

    // Read chunk pointer from table
    uintptr_t chunk = 0;
    if (!Mem::ReadSafe(chunkTable + chunkIndex * sizeof(uintptr_t), chunk) || !chunk) return 0;

    // Read FUObjectItem at the index within chunk
    uintptr_t itemAddr = chunk + withinChunk * sizeof(FUObjectItem);
    uintptr_t object = 0;
    Mem::ReadSafe(itemAddr, object);

    return object;
}

FUObjectItem* GetItem(int32_t index) {
    if (!s_arrayAddr || index < 0 || index >= GetCount()) return nullptr;

    uintptr_t chunkTable = 0;
    if (!Mem::ReadSafe(s_arrayAddr + s_layout.objectsOffset, chunkTable) || !chunkTable) return nullptr;

    int32_t chunkIndex = index / Constants::OBJECTS_PER_CHUNK;
    int32_t withinChunk = index % Constants::OBJECTS_PER_CHUNK;

    uintptr_t chunk = 0;
    if (!Mem::ReadSafe(chunkTable + chunkIndex * sizeof(uintptr_t), chunk) || !chunk) return nullptr;

    return Mem::Ptr<FUObjectItem>(chunk + withinChunk * sizeof(FUObjectItem));
}

void ForEach(std::function<bool(int32_t idx, uintptr_t obj)> cb) {
    int32_t count = GetCount();
    for (int32_t i = 0; i < count; ++i) {
        uintptr_t obj = GetByIndex(i);
        if (obj != 0) {
            if (!cb(i, obj)) break;
        }
    }
}

uintptr_t FindByName(const std::string& name) {
    uintptr_t result = 0;
    ForEach([&](int32_t /*idx*/, uintptr_t obj) -> bool {
        // Read FName from UObject
        uint32_t nameIndex = 0;
        if (!Mem::ReadSafe(obj + Constants::OFF_UOBJECT_NAME, nameIndex)) return true;

        std::string objName = FNamePool::GetString(nameIndex);
        if (objName == name) {
            result = obj;
            return false; // Stop iteration
        }
        return true;
    });
    return result;
}

uintptr_t FindByFullName(const std::string& fullName) {
    // Forward declared — uses UStructWalker::GetFullName
    // This is implemented after UStructWalker is available
    (void)fullName;
    return 0;
}

} // namespace ObjectArray
