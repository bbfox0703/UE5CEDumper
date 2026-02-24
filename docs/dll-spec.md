# C++ DLL Interface Specification

> Covers all public C++ headers: Memory, OffsetFinder, ObjectArray, FNamePool, UStructWalker, ExportAPI, PipeServer, and the CE Lua bridge.
> For the JSON pipe protocol, see [pipe-protocol.md](pipe-protocol.md).

-----

## Memory.h

```cpp
#pragma once
#include <Windows.h>
#include <cstdint>

namespace Mem {

    // --- Direct access (DLL is injected; direct pointer cast) ---

    template<typename T>
    inline T Read(uintptr_t addr) {
        return *reinterpret_cast<T*>(addr);
    }

    template<typename T>
    inline T* Ptr(uintptr_t addr) {
        return reinterpret_cast<T*>(addr);
    }

    // --- SEH-protected safe reads ---

    template<typename T>
    bool ReadSafe(uintptr_t addr, T& out);       // returns false on access violation

    bool ReadBytesSafe(uintptr_t addr, void* buf, size_t size);

    // --- Module info ---

    uintptr_t GetModuleBase(const wchar_t* moduleName = nullptr); // nullptr = main EXE
    size_t    GetModuleSize(const wchar_t* moduleName = nullptr);

    // --- Scanning ---

    // pattern: space-separated bytes, "??" = wildcard (e.g. "48 8B 05 ?? ?? ?? ??")
    uintptr_t AOBScan(const char* pattern, uintptr_t start = 0, size_t size = 0);

    // Resolve a RIP-relative instruction: reads int32 at instrAddr+opcodeLen, returns rip+rel32
    uintptr_t ResolveRIP(uintptr_t instrAddr, int opcodeLen = 3, int totalLen = 7);

    // --- TArray utilities ---

    struct TArrayView {
        uintptr_t Data  = 0;   // pointer to heap buffer
        int32_t   Count = 0;
        int32_t   Max   = 0;
    };

    bool      ReadTArray(uintptr_t addr, TArrayView& out);
    uintptr_t ReadTArrayElement(const TArrayView& arr, int32_t index); // reads pointer-sized element
}
```

-----

## OffsetFinder.h

```cpp
#pragma once
#include <cstdint>

namespace OffsetFinder {

    struct EnginePointers {
        uintptr_t GObjects  = 0;   // FUObjectArray* (game module .data section)
        uintptr_t GNames    = 0;   // FNamePool* or TNameEntryArray* base
        uintptr_t GWorld    = 0;   // UWorld** (non-critical, may be 0)
        uint32_t  UEVersion = 0;   // e.g. 507=UE5.7, 427=UE4.27, 422=UE4.22

        bool bUE4NameArray       = false; // true = TNameEntryArray (pre-4.25 some games)
        int  ue4StringOffset     = 0x10;  // FNameEntry string byte offset for UE4 mode
        int  fnameEntryHeaderOffset = 0;  // 0=standard, 4=hash-prefixed (UE4.26 SE fork)
    };

    // Main entry point — runs all scans, populates out, returns false on hard failure
    bool FindAll(EnginePointers& out);

    // Individual scanners (called internally by FindAll)
    uintptr_t FindGObjects();
    uintptr_t FindGNames();
    uintptr_t FindGWorld();
    uint32_t  DetectVersion();

    // Dynamic offset detection — MUST be called after FNamePool + ObjectArray are initialized.
    // Probes known UE structs (Guid: A/B/C/D at 0/4/8/12, Vector: X/Y/Z at 0/4/8) to discover
    // correct FField::Name, FField::Next, FProperty::Offset_Internal, UStruct::ChildProperties.
    // Populates DynOff namespace and detects CasePreservingName (+0x8 shift for UE5.5+/5.7).
    bool ValidateAndFixOffsets(uint32_t ueVersion);

    // Lazy UEnum::Names offset detection — called on first EnumProperty encounter
    bool DetectUEnumNames();
}
```

### AOB Scan Strategy

Scans game module **executable sections only** (`.text`). Pattern handlers try both direct and deref variants, then validate via `ValidateGObjects()` / `ValidateGNames()`:

```cpp
// RIP-relative resolution pattern
uintptr_t target = Mem::ResolveRIP(match + opcodeLen, opcodeLen, instrLen);
// Try direct: ValidateGObjects(target)
// Try deref:  ValidateGObjects(Mem::Read<uintptr_t>(target))
// Try offset: ValidateGObjects(target - 0x10)   ← for V12/RE2 GUObjectArray patterns
```

`FindGNames` fallback when all 17 AOB patterns fail: scans `.data` section for 8-byte-aligned pointers whose dereference matches a `"None"` FNameEntry (`00 01 4E 6F 6E 65`). Used on UE5.5+ / UE4.27 games (EverSpace 2, Hogwarts Legacy).

-----

## Constants.h — DynOff Namespace

All FField/FProperty/UStruct offsets are stored as **runtime-mutable `inline int`** values. Never use compile-time constants for these.

```cpp
namespace DynOff {
    // UObject base (stable, never shift)
    inline int UOBJECT_VTABLE  = 0x00; // constexpr — never changes
    inline int UOBJECT_FLAGS   = 0x08;
    inline int UOBJECT_CLASS   = 0x10; // UClass*
    inline int UOBJECT_NAME    = 0x18; // FName (ComparisonIndex at +0x18, Number at +0x1C)
    inline int UOBJECT_OUTER   = 0x20; // shifts to 0x28 with CasePreservingName

    // UStruct offsets (populated by ValidateAndFixOffsets)
    inline int USTRUCT_SUPER       = 0x40; // UStruct* SuperStruct
    inline int USTRUCT_CHILDREN    = 0x48; // UField* Children (functions)
    inline int USTRUCT_CHILDPROPS  = 0x50; // FField* ChildProperties (properties)
    inline int USTRUCT_PROPSSIZE   = 0x58; // int32 PropertiesSize

    // FField offsets (shift by +0x8 when CasePreservingName is active)
    inline int FFIELD_CLASS    = 0x08; // FFieldClass* ClassPrivate (stable)
    inline int FFIELD_NEXT     = 0x20; // FField* Next
    inline int FFIELD_NAME     = 0x28; // FName — key diagnostic: "rty" = this is wrong

    // FProperty offsets (also shift with CasePreservingName)
    inline int FPROPERTY_ELEMSIZE  = 0x38; // int32 ElementSize
    inline int FPROPERTY_FLAGS     = 0x40; // uint64 PropertyFlags
    inline int FPROPERTY_OFFSET    = 0x4C; // int32 Offset_Internal

    // UE4 <4.25 UProperty mode (set when FProperty-to-UProperty fallback triggers)
    inline int UFIELD_NEXT         = 0x28; // UField::Next
    inline int UPROPERTY_OFFSET    = 0x44; // UProperty::Offset_Internal

    // UEnum (lazy-detected on first EnumProperty encounter)
    inline int UENUM_NAMES         = 0x40; // TArray<TPair<FName,int64>> Names
    inline int UENUM_ENTRY_SIZE    = 0x10; // sizeof(TPair<FName,int64>)

    // State flags (atomic, set by ValidateAndFixOffsets)
    inline std::atomic<bool> bCasePreservingName{false};
    inline std::atomic<bool> bUseFProperty{true};
    inline std::atomic<bool> bOffsetsValidated{false};
}
```

**CasePreservingName (UE5.5+/5.7):** When detected, `FField::Name` shifts from +0x28 to +0x30 and all subsequent FProperty fields shift by +0x8. Diagnostic clue: field names displaying as "rty" (tail of "Property") = `FFIELD_NAME` is reading FFieldClass data.

**UE4 <4.25 FProperty-to-UProperty fallback:** When `ValidateAndFixOffsets` cannot find `ChildProperties` in FProperty mode, it retries scanning for UObject-derived property chains and switches `bUseFProperty = false`.

-----

## ObjectArray.h

```cpp
#pragma once
#include <cstdint>
#include <functional>
#include <string>
#include <vector>

// FUObjectItem size varies by game/UE version:
//   16B = UE5 standard, some UE4 without GC clustering
//   24B = most UE4 (Object* + Flags + ClusterRootIndex + SerialNumber + pad)
// GetItemSize() returns the detected stride.

namespace ObjectArray {

    void      Init(uintptr_t gobjectsAddr);
    int32_t   GetCount();
    int32_t   GetMax();
    int32_t   GetItemSize();        // detected stride: 16, 20, or 24
    bool      IsFlat();             // true = FFixedUObjectArray (UE4.11–4.20)

    uintptr_t GetByIndex(int32_t index);  // returns UObject* (0 if slot empty)

    // Iterate all non-null slots; cb returns false to stop
    void ForEach(std::function<bool(int32_t idx, uintptr_t obj)> cb);

    // Linear name search
    uintptr_t FindByName(const std::string& name);
    uintptr_t FindByFullName(const std::string& fullPath);

    // --- Paginated search results (used by pipe commands) ---

    struct SearchResult {
        uintptr_t   addr;
        int32_t     index;
        std::string name;
        std::string className;
        uintptr_t   outer;
    };

    struct SearchResultSet {
        std::vector<SearchResult> results;
        int32_t scanned = 0;   // total indices iterated (for correct UI pagination)
        int32_t nonNull = 0;
        int32_t named   = 0;
    };

    SearchResultSet SearchByName(const std::string& query, int maxResults = 200);
    SearchResultSet FindInstancesByClass(const std::string& className, int maxResults = 500);

    // --- Address-to-object reverse lookup ---

    struct AddressLookupResult {
        bool        found       = false;
        bool        exactMatch  = false;  // false = addr is inside the UObject
        uintptr_t   objectAddr  = 0;
        int32_t     index       = -1;
        std::string name;
        std::string className;
        uintptr_t   outer       = 0;
        int32_t     offsetFromBase = 0;  // addr - objectAddr
    };

    AddressLookupResult FindByAddress(uintptr_t addr);
}
```

**Array layout detection** (`DetectItemSize`): walks stride-aligned positions from chunk[0], validates each slot via `FNamePool::GetString()`. Scoring: `named*10 - bad*3`; when all scores negative, picks fewest-bad stride. Flat array (`FFixedUObjectArray`) detected before chunked probing by checking `chunk[1]` validity with magnitude + `ReadSafe` checks.

-----

## FNamePool.h

```cpp
#pragma once
#include <cstdint>
#include <string>

namespace FNamePool {

    // UE5 FNamePool (standard or hash-prefixed UE4.26 SE fork)
    // headerOffset: 0 = standard ([2B header][string])
    //               4 = hash-prefixed ([4B hash][2B header][string])
    void Init(uintptr_t gnamesAddr, int headerOffset = 0);

    // UE4 TNameEntryArray (flat chunked pointer array, double-deref)
    // stringOffset: byte offset from FNameEntry* to the string (0x10, 0x06, 0x0C, 0x08)
    void InitUE4(uintptr_t nameArrayAddr, int stringOffset = 0x10);

    bool IsInitialized();
    bool IsUE4Mode();   // true = TNameEntryArray path

    // Resolve FName to string. number > 0 appends _N suffix.
    std::string GetString(int32_t nameIndex, int32_t number = 0);

    // Returns raw FNameEntry* address (for diagnostics)
    uintptr_t GetEntry(int32_t nameIndex);
}
```

**Three operating modes:**

| Mode | Triggered by | Entry layout | Stride |
|------|-------------|--------------|--------|
| UE5 standard | `Init(addr, 0)` | `[2B header][string]` | 2 |
| Hash-prefixed | `Init(addr, 4)` | `[4B hash][2B header][string]` | 4 |
| UE4 TNameEntryArray | `InitUE4(addr)` | double-deref, string at fixed offset | — |

**Chunk calculation (UE5 modes):**
```cpp
int chunkIdx    = nameIndex >> 16;
int chunkOffset = (nameIndex & 0xFFFF) * s_stride;  // s_stride = 2 or 4
uintptr_t chunk = Read<uintptr_t>(poolBase + chunksOffset + chunkIdx * 8);
uintptr_t entry = chunk + chunkOffset;
uint16_t  hdr   = Read<uint16_t>(entry + s_headerOffset);
int       len   = hdr >> 6;
// string at entry + s_headerOffset + 2
```

-----

## UStructWalker.h

```cpp
#pragma once
#include <cstdint>
#include <string>
#include <vector>

// --- Class inspection ---

struct FieldInfo {
    uintptr_t   Address;
    std::string Name;
    std::string TypeName;     // "FloatProperty", "ArrayProperty", etc.
    int32_t     Offset;
    int32_t     Size;
    uint64_t    PropertyFlags;
};

struct ClassInfo {
    std::string            Name;
    std::string            FullPath;     // e.g. "/Game/BP_Player.BP_Player_C"
    uintptr_t              Address;      // UClass*
    uintptr_t              SuperClass;   // parent UClass*
    std::string            SuperName;
    int32_t                PropertiesSize;
    std::vector<FieldInfo> Fields;       // all fields inc. inherited
};

namespace UStructWalker {

    ClassInfo   WalkClass(uintptr_t uclassAddr);
    uintptr_t   GetClass(uintptr_t uobjectAddr);
    uintptr_t   GetOuter(uintptr_t uobjectAddr);
    std::string GetName(uintptr_t uobjectAddr);
    std::string GetFullName(uintptr_t uobjectAddr);   // "Class /Game/BP.BP_C"
    int32_t     GetIndex(uintptr_t uobjectAddr);
}

// --- Live instance walking ---

struct LiveFieldValue {
    std::string name, typeName;
    int32_t     offset, size;
    std::string hexValue;          // raw hex bytes
    std::string typedValue;        // human-readable (float, int, bool, enum name, etc.)

    // ObjectProperty / pointer fields
    uintptr_t   ptrValue      = 0;
    std::string ptrName, ptrClassName;

    // BoolProperty bit fields
    int32_t     boolBitIndex  = -1;
    uint8_t     boolFieldMask = 0;
    uint8_t     boolByteOffset = 0;

    // ArrayProperty (Phase A: type info)
    int32_t     arrayCount        = -1;
    std::string arrayInnerType;        // "FloatProperty", "StructProperty", etc.
    std::string arrayInnerStructType;  // struct class name (for StructProperty inner)
    int32_t     arrayElemSize     = 0;
    uintptr_t   arrayInnerFFieldAddr = 0;
    uintptr_t   arrayInnerStructAddr = 0;

    // ArrayProperty (Phase B: scalar inline elements, count <= 64)
    struct ArrayElement {
        int32_t     index;
        std::string value;    // typed value
        std::string hex;      // raw hex
        std::string enumName; // resolved enum name (for EnumProperty/ByteProperty inner)
    };
    std::vector<ArrayElement> arrayElements;

    // StructProperty (resolved sub-fields)
    uintptr_t   structDataAddr  = 0;
    uintptr_t   structClassAddr = 0;
    std::string structTypeName;

    // EnumProperty / ByteProperty-with-enum
    int64_t     enumValue = 0;
    std::string enumName;

    // StrProperty (FString)
    std::string strValue;

    // EnumEntry list (for CE DropDownList)
    struct EnumEntry {
        std::string name;
        int64_t     value;
    };
};

struct InstanceWalkResult {
    uintptr_t   addr, classAddr, outerAddr;
    std::string name, className, outerName, outerClassName;
    std::vector<LiveFieldValue> fields;
};

namespace UStructWalker {
    // Walk all live field values from a UObject instance.
    // classAddr = 0 → auto-resolved from UObject::ClassPrivate.
    // arrayLimit = max inline array elements (default 64, capped for Phase B).
    InstanceWalkResult WalkInstance(uintptr_t instanceAddr,
                                    uintptr_t classAddr = 0,
                                    int32_t   arrayLimit = 64);

    // Type formatting
    std::string InterpretValue(const std::string& typeName,
                               const void* data, int32_t size);
}

// --- Array reading (Phase B–F) ---

struct ReadArrayResult {
    bool        ok = false;
    std::string error;
    int32_t     totalCount = 0;
    int32_t     readCount  = 0;
    uintptr_t   enumAddr   = 0;    // for CE DropDownList generation
    std::vector<LiveFieldValue::ArrayElement> elements;
};

// Phase B: scalar element values (Float, Int, Bool, Byte, Enum, Name, ...)
bool IsScalarArrayType(const std::string& innerTypeName);
ReadArrayResult ReadArrayElements(
    uintptr_t instanceAddr, int32_t fieldOffset,
    uintptr_t innerFFieldAddr, const std::string& innerTypeName,
    int32_t elemSize, int32_t offset = 0, int32_t limit = 64);

// Phase D: ObjectProperty / SoftObjectProperty arrays (pointer elements)
bool IsPointerArrayType(const std::string& innerTypeName);
ReadArrayResult ReadPointerArrayElements(
    uintptr_t instanceAddr, int32_t fieldOffset,
    int32_t elemSize, int32_t offset = 0, int32_t limit = 64);

// Phase E: WeakObjectProperty / LazyObjectProperty arrays
bool IsWeakPointerArrayType(const std::string& innerTypeName);
ReadArrayResult ReadWeakObjectArrayElements(
    uintptr_t instanceAddr, int32_t fieldOffset,
    int32_t elemSize, int32_t offset = 0, int32_t limit = 64);

// Phase F: StructProperty arrays (inline structs)
bool IsStructArrayType(const std::string& innerTypeName);
ReadArrayResult ReadStructArrayElements(
    uintptr_t instanceAddr, int32_t fieldOffset,
    uintptr_t innerStructAddr, int32_t elemSize,
    int32_t offset = 0, int32_t limit = 64);

// Enumerate all entries of a UEnum (for CE DropDownList / enum resolution)
std::vector<LiveFieldValue::EnumEntry> GetEnumEntries(uintptr_t enumAddr);
```

-----

## ExportAPI.h — C ABI Exports

```cpp
#pragma once
#include <cstdint>
#include <Windows.h>

extern "C" {

    // === Initialization ===
    __declspec(dllexport) bool     UE5_Init();          // AOB scan + subsystem init
    __declspec(dllexport) bool     UE5_AutoStart();     // Init + StartPipeServer (called by AutoStartThreadProc)
    __declspec(dllexport) void     UE5_Shutdown();
    __declspec(dllexport) uint32_t UE5_GetVersion();    // e.g. 507, 427, 422

    // === Global Pointers ===
    __declspec(dllexport) uintptr_t UE5_GetGObjectsAddr();
    __declspec(dllexport) uintptr_t UE5_GetGNamesAddr();

    // === Object Queries ===
    __declspec(dllexport) int32_t   UE5_GetObjectCount();
    __declspec(dllexport) uintptr_t UE5_GetObjectByIndex(int32_t index);
    __declspec(dllexport) bool      UE5_GetObjectName(uintptr_t obj, char* buf, int32_t bufLen);
    __declspec(dllexport) bool      UE5_GetObjectFullName(uintptr_t obj, char* buf, int32_t bufLen);
    __declspec(dllexport) uintptr_t UE5_GetObjectClass(uintptr_t obj);
    __declspec(dllexport) uintptr_t UE5_GetObjectOuter(uintptr_t obj);

    // === Search ===
    __declspec(dllexport) uintptr_t UE5_FindObject(const char* fullPath);
    __declspec(dllexport) uintptr_t UE5_FindClass(const char* className);

    // === WalkClass batch mode (avoids callback across DLL boundary) ===
    __declspec(dllexport) int32_t   UE5_WalkClassBegin(uintptr_t uclassAddr); // returns field count
    __declspec(dllexport) bool      UE5_WalkClassGetField(int32_t index,
                                        uintptr_t* outAddr,
                                        char* nameOut,  int32_t nameBufLen,
                                        char* typeOut,  int32_t typeBufLen,
                                        int32_t* offsetOut,
                                        int32_t* sizeOut);
    __declspec(dllexport) void      UE5_WalkClassEnd();

    // === FName Resolution ===
    __declspec(dllexport) bool      UE5_ResolveFName(uint64_t fname, char* buf, int32_t bufLen);

    // === Pipe Server ===
    __declspec(dllexport) bool      UE5_StartPipeServer();
    __declspec(dllexport) void      UE5_StopPipeServer();
    __declspec(dllexport) bool      UE5_IsPipeConnected();
}
```

**Export naming rules:**
- All exports prefixed with `UE5_`
- No callbacks across DLL boundary — use Begin/Get/End batch mode
- Buffers allocated by caller; DLL only writes into them

-----

## PipeServer.h

```cpp
#pragma once
#include <Windows.h>
#include <atomic>
#include <mutex>
#include <string>
#include <thread>
#include <unordered_map>
#include <memory>

class PipeServer {
public:
    ~PipeServer();
    bool Start();
    void Stop();
    bool IsClientConnected() const { return m_clientConnected.load(); }
    void PushEvent(const std::string& jsonLine);  // push async watch event

private:
    std::thread       m_acceptThread;
    std::atomic<bool> m_running{false};
    std::atomic<bool> m_clientConnected{false};
    HANDLE            m_pipe{INVALID_HANDLE_VALUE};
    std::mutex        m_pipeMutex;
    std::mutex        m_writeMutex;

    struct WatchEntry {
        uintptr_t         addr;
        uint32_t          size;
        uint32_t          interval_ms;
        std::thread       watchThread;
        std::atomic<bool> active{true};
    };
    std::unordered_map<uintptr_t, std::unique_ptr<WatchEntry>> m_watches;
    std::mutex m_watchMutex;

    void        AcceptLoop();
    void        HandleClient(HANDLE pipe);
    std::string DispatchCommand(const std::string& jsonLine);
    void        StartWatch(uintptr_t addr, uint32_t size, uint32_t interval_ms);
    void        StopWatch(uintptr_t addr);
    void        StopAllWatches();
    bool        WriteLine(HANDLE pipe, const std::string& line);
    std::string ReadLine(HANDLE pipe);
};

// Pipe constants (from Constants.h)
// PIPE_NAME    = L"\\\\.\\pipe\\UE5DumpBfx"
// PIPE_BUF_SIZE = 65536
```

For the JSON protocol, see [pipe-protocol.md](pipe-protocol.md).

-----

## CE Lua Bridge

CE Lua's only responsibility is to **inject the DLL**. The DLL auto-starts via `AutoStartThreadProc` on `DLL_PROCESS_ATTACH` — no `callFunction` or `executeCodeEx` required.

```lua
-- ue5dump.lua (simplified)
-- DLL_PATH resolved from: OpenDialog1.FileName dir → SaveDialog1.FileName dir → CE root dir

local DLL_PATH = resolveDllPath()  -- tries 3 candidates

loadLibrary(DLL_PATH)
print("[UE5Dump] DLL injected — auto-start thread will initialize in 1s")
print("[UE5Dump] Launch UE5DumpUI.exe to connect")
-- Done. DLL handles everything else via AutoStartThreadProc.
```

**Why no `callFunction` / `executeCodeEx`:** These use `CreateRemoteThread` in the game process with an address from CE's own address space — instant crash. The auto-start thread approach sidesteps this entirely.
