# C++ DLL Interface Specification

> Moved from CLAUDE.md. Contains C++ header definitions, offset tables, and CE Lua bridge spec.

-----

## Memory.h — Base Abstraction

```cpp
#pragma once
#include <Windows.h>
#include <cstdint>

namespace Mem {
    // Read target process memory (DLL is injected, direct deref)
    template<typename T>
    inline T Read(uintptr_t addr) {
        return *reinterpret_cast<T*>(addr);
    }

    template<typename T>
    inline T* Ptr(uintptr_t addr) {
        return reinterpret_cast<T*>(addr);
    }

    uintptr_t GetModuleBase(const wchar_t* moduleName = nullptr);
    uintptr_t AOBScan(const char* pattern, uintptr_t start = 0, size_t size = 0);
}
```

-----

## OffsetFinder.h — GObjects / GNames Location

```cpp
#pragma once
#include <cstdint>

namespace OffsetFinder {

    struct EnginePointers {
        uintptr_t GObjects  = 0;   // FUObjectArray*
        uintptr_t GNames    = 0;   // FNamePool*
        uint32_t  UEVersion = 0;   // e.g. 500, 501, 503, 504
    };

    // Scan and cache all global pointers
    // Returns false on failure; error via GetLastError()
    bool FindAll(EnginePointers& out);

    // Detect UE version via FEngineVersion::Get() string parsing
    uint32_t DetectVersion();
}
```

**AOB Pattern Strategy (by UE version):**

| Target | Pattern | Notes |
|--------|---------|-------|
| GObjects | `48 8B 05 ?? ?? ?? ?? 48 8B 0C C8` | RIP-relative, resolve offset |
| GNames (FNamePool) | `48 8D 35 ?? ?? ?? ?? EB` | RIP-relative |
| AllocateUObjectIndex | Scan function signature then follow reference | Fallback |

RIP-relative resolution:

```cpp
// addr = address where pattern was found
uintptr_t rip    = addr + 7;           // after instruction end
int32_t   rel32  = Mem::Read<int32_t>(addr + 3);
uintptr_t target = rip + rel32;        // actual global pointer address
uintptr_t value  = Mem::Read<uintptr_t>(target);
```

-----

## ObjectArray.h — FChunkedFixedUObjectArray

```cpp
#pragma once
#include <cstdint>
#include <functional>

// UE5 FUObjectArray structure
struct FUObjectItem {
    uintptr_t Object;     // UObject*
    int32_t   Flags;
    int32_t   ClusterRootIndex;
    int32_t   SerialNumber;
    int32_t   _pad;
};

struct FChunkedFixedUObjectArray {
    // Objects: chunk table, each chunk 64 * 1024 elements
    static constexpr int32_t ElementsPerChunk = 64 * 1024;

    uintptr_t** Objects;        // FUObjectItem** (chunk pointer array)
    uintptr_t   PreAllocatedObjects;
    int32_t     MaxElements;
    int32_t     NumElements;
    int32_t     MaxChunks;
    int32_t     NumChunks;

    FUObjectItem* GetItem(int32_t index) const;
    uintptr_t     GetObject(int32_t index) const;
};

namespace ObjectArray {
    void Init(uintptr_t gobjectsAddr);
    int32_t   GetCount();
    uintptr_t GetByIndex(int32_t index);

    // callback: return false to stop iteration
    void ForEach(std::function<bool(int32_t idx, uintptr_t obj)> cb);
}
```

-----

## FNamePool.h — UE5 String Reader

```cpp
#pragma once
#include <cstdint>
#include <string>

// UE5 FNamePool structure
// Chunk size: 0x20000 bytes
// Stride: 2 bytes (each entry aligned)
struct FNameEntry {
    // Header: low 6 bits = len, bit 6 = wide char flag
    uint16_t Header;

    // AnsiName or WideName follows immediately after Header
    const char* GetAnsiName() const {
        return reinterpret_cast<const char*>(this + 1);
    }

    int32_t GetLength() const {
        return Header >> 6;
    }

    bool IsWide() const {
        return (Header & 1) != 0;
    }
};

namespace FNamePool {
    void Init(uintptr_t gnamesAddr);
    std::string GetString(int32_t nameIndex, int32_t number = 0);
    // nameIndex = FName::ComparisonIndex (low 32 bits)
    // number    = FName::Number (high 32 bits, for _1 _2 suffix)
}
```

-----

## UStructWalker.h — Class Structure Traversal

```cpp
#pragma once
#include <cstdint>
#include <string>
#include <vector>

struct FieldInfo {
    uintptr_t   Address;      // FField* address
    std::string Name;
    std::string TypeName;
    int32_t     Offset;
    int32_t     Size;
    uint64_t    PropertyFlags;
};

struct ClassInfo {
    std::string          Name;
    std::string          FullPath;
    uintptr_t            SuperClass;    // Parent UClass* address
    int32_t              PropertiesSize;
    std::vector<FieldInfo> Fields;
};

namespace UStructWalker {
    // Walk all FFields of a UStruct (UE4.25+ uses FField, not UProperty)
    ClassInfo WalkClass(uintptr_t uclassAddr);

    // Get UClass* of a UObject
    uintptr_t GetClass(uintptr_t uobjectAddr);

    // Get full path name, e.g. /Game/BP_Player.BP_Player_C
    std::string GetFullName(uintptr_t uobjectAddr);
}
```

-----

## UObject / UStruct Offset Table

All FField/FProperty/UStruct offsets use `DynOff::*` mutable inline ints — detected at runtime by `ValidateAndFixOffsets()` probing known structs (Guid, Vector). CasePreservingName (UE5.5+) shifts FField/FProperty offsets by +0x8.

| Field | Struct | Default Offset | DynOff Variable | Notes |
|-------|--------|----------------|-----------------|-------|
| InternalIndex | UObjectBase | 0x0C | constexpr (stable) | |
| NamePrivate | UObjectBase | 0x18 | constexpr (stable) | FName |
| ClassPrivate | UObjectBase | 0x10 | constexpr (stable) | UClass* |
| OuterPrivate | UObjectBase | 0x20 | constexpr (stable) | UObject* |
| SuperStruct | UStruct | 0x40 | `DynOff::USTRUCT_SUPER` | Parent class |
| ChildProperties | UStruct | 0x50 | `DynOff::USTRUCT_CHILDPROPS` | FField* chain head |
| Children | UStruct | 0x48 | `DynOff::USTRUCT_CHILDREN` | UField* chain (functions) |
| PropertiesSize | UStruct | 0x58 | `DynOff::USTRUCT_PROPSSIZE` | |
| Next (FField) | FField | 0x20 | `DynOff::FFIELD_NEXT` | Next FField* |
| Name (FField) | FField | 0x28 | `DynOff::FFIELD_NAME` | FName; shifts +0x8 with CasePreservingName |
| Class (FField) | FField | 0x08 | `DynOff::FFIELD_CLASS` | FFieldClass* |
| Offset_Internal | FProperty | 0x4C | `DynOff::FPROPERTY_OFFSET` | Property offset; shifts +0x8 with CasePreservingName |
| ElementSize | FProperty | 0x38 | `DynOff::FPROPERTY_ELEMSIZE` | Size |
| PropertyFlags | FProperty | 0x40 | `DynOff::FPROPERTY_FLAGS` | |

-----

## ExportAPI.h — C ABI Exports

```cpp
#pragma once
#include <cstdint>
#include <Windows.h>

// All exports use C ABI for CE Lua callFunction
extern "C" {

    // === Initialization ===
    __declspec(dllexport) bool     UE5_Init();
    __declspec(dllexport) void     UE5_Shutdown();
    __declspec(dllexport) uint32_t UE5_GetVersion();      // 500, 503, 504 ...

    // === Global Pointers ===
    __declspec(dllexport) uintptr_t UE5_GetGObjectsAddr();
    __declspec(dllexport) uintptr_t UE5_GetGNamesAddr();

    // === Object Queries ===
    __declspec(dllexport) int32_t   UE5_GetObjectCount();
    __declspec(dllexport) uintptr_t UE5_GetObjectByIndex(int32_t index);
    __declspec(dllexport) bool      UE5_GetObjectName(uintptr_t obj,
                                        char* buf, int32_t bufLen);
    __declspec(dllexport) bool      UE5_GetObjectFullName(uintptr_t obj,
                                        char* buf, int32_t bufLen);
    __declspec(dllexport) uintptr_t UE5_GetObjectClass(uintptr_t obj);
    __declspec(dllexport) uintptr_t UE5_GetObjectOuter(uintptr_t obj);

    // === Search ===
    __declspec(dllexport) uintptr_t UE5_FindObject(const char* fullPath);
    __declspec(dllexport) uintptr_t UE5_FindClass(const char* className);

    // === WalkClass (Begin/Get/End batch mode, avoids callback across DLL boundary) ===
    __declspec(dllexport) int32_t   UE5_WalkClassBegin(uintptr_t uclassAddr);
    __declspec(dllexport) bool      UE5_WalkClassGetField(int32_t index,
                                        uintptr_t* outAddr,
                                        char* nameOut, int32_t nameBufLen,
                                        char* typeOut, int32_t typeBufLen,
                                        int32_t* offsetOut,
                                        int32_t* sizeOut);
    __declspec(dllexport) void      UE5_WalkClassEnd();

    // === FName Direct Resolution ===
    __declspec(dllexport) bool      UE5_ResolveFName(uint64_t fname,
                                        char* buf, int32_t bufLen);

    // === Pipe Server Control ===
    __declspec(dllexport) bool      UE5_StartPipeServer();
    __declspec(dllexport) void      UE5_StopPipeServer();
    __declspec(dllexport) bool      UE5_IsPipeConnected();
}
```

-----

## PipeServer.h

```cpp
#pragma once
#include <Windows.h>
#include <string>
#include <thread>
#include <atomic>
#include <unordered_map>
#include <mutex>

class PipeServer {
public:
    static constexpr wchar_t PIPE_NAME[] = L"\\\\.\\pipe\\UE5DumpBfx";
    static constexpr DWORD   BUF_SIZE    = 65536;

    bool Start();
    void Stop();
    bool IsClientConnected() const { return m_clientConnected; }

    // Push event to connected client
    void PushEvent(const std::string& jsonLine);

private:
    std::thread        m_acceptThread;
    std::atomic<bool>  m_running{false};
    std::atomic<bool>  m_clientConnected{false};
    HANDLE             m_clientPipe{INVALID_HANDLE_VALUE};
    std::mutex         m_writeMutex;

    // Watch-related
    struct WatchEntry {
        uintptr_t addr;
        uint32_t  size;
        uint32_t  interval_ms;
        std::thread watchThread;
        std::atomic<bool> active{true};
    };
    std::unordered_map<uintptr_t, WatchEntry> m_watches;
    std::mutex m_watchMutex;

    void AcceptLoop();
    void HandleClient(HANDLE pipe);
    std::string DispatchCommand(const std::string& jsonLine);
    void StartWatch(uintptr_t addr, uint32_t size, uint32_t interval_ms);
    void StopWatch(uintptr_t addr);
};
```

For the JSON IPC protocol specification, see [pipe-protocol.md](pipe-protocol.md).

-----

## CE Lua Bridge

CE Lua's responsibility is reduced to: **inject DLL + start Pipe Server**. UI is entirely handled by the external app.

### ue5dump.lua — Main Script

```lua
-- ue5dump.lua
local DLL_PATH = getCheatEngineDir() .. "ue5dumper\\UE5Dumper.dll"

local function callDLL(funcName, retType, ...)
    local fn = getAddress(funcName)
    assert(fn ~= 0, "[UE5Dump] Function not found: " .. funcName)
    return callFunction(fn, retType, ...)
end

local function main()
    -- 1. Inject DLL
    loadLibrary(DLL_PATH)
    print("[UE5Dump] DLL loaded")

    -- 2. Initialize core (AOB scan GObjects/GNames)
    assert(callDLL("UE5_Init", "bool"),
           "[UE5Dump] Init failed, GObjects/GNames scan failed")
    local ver = callDLL("UE5_GetVersion", "uint32")
    print(string.format("[UE5Dump] UE Version: %d", ver))

    -- 3. Start Pipe Server, wait for external UI connection
    assert(callDLL("UE5_StartPipeServer", "bool"),
           "[UE5Dump] Pipe Server start failed")
    print("[UE5Dump] Pipe Server started, waiting for UE5DumpUI...")
    print("[UE5Dump] Pipe: \\\\.\\pipe\\UE5DumpBfx")
end

main()
```

### Export Function Naming Rules

- All exports prefixed with `UE5_`
- Avoid callbacks across DLL boundary (use Begin/Get/End batch mode instead)
- Buffers allocated by caller (Lua side), DLL only writes into them
