
# UE5CEDumper — CLAUDE.md

## 專案目標

開發一個 **C++ DLL + Cheat Engine Lua Bridge** 的 Unreal Engine 5 Dumper。
目標：支援 UE5 全版本（5.0 ~ 5.4+），可找到 global pointers（GObjects / GNames / GWorld），並建立完整的物件/結構架構，整合 CE 使用。

-----

## 架構概覽

```
┌──────────────────────────────────────────────────────┐
│  Game Process                                        │
│                                                      │
│  CE Lua (ue5dump.lua)                                │
│      │  loadLibrary / callFunction                   │
│      ▼                                               │
│  UE5Dumper.dll  [injected]                           │
│      ├── ExportAPI       ← C ABI，供 CE Lua 呼叫     │
│      ├── PipeServer      ← Named Pipe IPC server     │
│      ├── OffsetFinder    ← AOB scan GObjects/GNames  │
│      ├── ObjectArray     ← FChunkedFixedUObjectArray │
│      ├── FNamePool       ← UE5 FNamePool reader      │
│      ├── UStructWalker   ← FField chain 遍歷         │
│      └── Memory          ← 基礎 read/write 封裝      │
└──────────────────────┬───────────────────────────────┘
                       │ Named Pipe: \\.\pipe\UE5DumpBfx
                       │ JSON over pipe (newline-delimited)
┌──────────────────────▼───────────────────────────────┐
│  UE5DumpUI  (Avalonia + ReactiveUI，獨立 exe)        │
│      ├── PipeClient          ← async pipe 連線管理   │
│      ├── ObjectTreePanel     ← UObject 階層瀏覽      │
│      ├── ClassStructPanel    ← Property Grid 顯示    │
│      ├── PointerPanel        ← GObjects/GNames 位址  │
│      └── HexViewPanel        ← Live Memory Hex View  │
└──────────────────────────────────────────────────────┘
```

-----

## 目錄結構

```
UE5CEDumper/
├── CLAUDE.md
├── .gitmodules
│
├── dll/                            ← C++ DLL (injected)
│   ├── CMakeLists.txt
│   └── src/
│       ├── dllmain.cpp
│       ├── ExportAPI.cpp / .h      ← C ABI export
│       ├── PipeServer.cpp / .h     ← Named Pipe IPC server
│       ├── PipeProtocol.h          ← JSON command/response 定義
│       ├── OffsetFinder.cpp / .h
│       ├── ObjectArray.cpp / .h
│       ├── FNamePool.cpp / .h
│       ├── UStructWalker.cpp / .h
│       └── Memory.cpp / .h
│
├── docs/                           ← 相關文件
│
├── ui/                             ← C# Avalonia UI App
│   ├── UE5DumpUI.csproj
│   ├── App.axaml / App.axaml.cs
│   ├── Models/
│   │   ├── UObjectNode.cs          ← Tree node model
│   │   ├── FieldInfo.cs            ← Property grid model
│   │   └── EngineState.cs          ← GObjects/GNames 等狀態
│   ├── Services/
│   │   ├── PipeClient.cs           ← Named Pipe 連線 + 收發
│   │   └── DumpService.cs          ← 業務邏輯封裝
│   ├── ViewModels/
│   │   ├── MainWindowViewModel.cs
│   │   ├── ObjectTreeViewModel.cs
│   │   ├── ClassStructViewModel.cs
│   │   ├── PointerPanelViewModel.cs
│   │   └── HexViewViewModel.cs
│   └── Views/
│       ├── MainWindow.axaml / .cs
│       ├── ObjectTreePanel.axaml / .cs
│       ├── ClassStructPanel.axaml / .cs
│       ├── PointerPanel.axaml / .cs
│       └── HexViewPanel.axaml / .cs
│
├── scripts/
│   ├── ue5dump.lua                 ← CE 主腳本（注入 + 啟動 pipe）
│   └── utils.lua
│
└── vendor/
    ├── UE5Dumper/                  ← git submodule（僅參考）
    ├── GSpots/                     ← git submodule（僅參考）
    └── Dumper-7/                   ← git submodule（僅參考）
```

### Git Submodule 設定

```bash
git submodule add https://github.com/Encryqed/Dumper-7 vendor/Dumper-7
git submodule add --force https://github.com/Spuckwaffel/UEDumper.git vendor/UEDumper
git submodule add --force https://github.com/Do0ks/GSpots.git vendor/GSpots
```

-----

## Build 環境

### DLL（C++）

- **IDE**: Visual Studio 2026 (v18, MSVC 19.50): generate related .sln files
- **C++ Standard**: C++23
- **Target**：x64 Release DLL、Statically-linked DLL (static CRT, `/MT` / `/MTd`)
- **Flags**: `/utf-8 /W4 /permissive-`
- **Compiler**: MSVC v145 (cl.exe)
- **Build**: CMake 3.25+ with Ninja generator
- **依賴**：nlohmann/json（header-only，放 vendor/）

### CMakeLists.txt

```cmake
cmake_minimum_required(VERSION 3.20)
project(UE5Dumper)

set(CMAKE_CXX_STANDARD 20)

add_library(UE5Dumper SHARED
    dll/src/dllmain.cpp
    dll/src/ExportAPI.cpp
    dll/src/PipeServer.cpp
    dll/src/OffsetFinder.cpp
    dll/src/ObjectArray.cpp
    dll/src/FNamePool.cpp
    dll/src/UStructWalker.cpp
    dll/src/Memory.cpp
)

target_include_directories(UE5Dumper PRIVATE dll/src vendor/nlohmann)
target_compile_definitions(UE5Dumper PRIVATE UNICODE _UNICODE)
target_link_libraries(UE5Dumper PRIVATE ws2_32)
```

### UI App（C# Avalonia）

- **.NET 8**
- **Avalonia 11.x**
- **ReactiveUI + ReactiveUI.Fody**
- **單檔發布**（`PublishSingleFile` + `SelfContained`，約 60~80MB）

```xml
<!-- UE5DumpUI.csproj 重點設定 -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net8.0-windows</TargetFramework>
    <Nullable>enable</Nullable>
    <PublishSingleFile>true</PublishSingleFile>
    <SelfContained>true</SelfContained>
    <RuntimeIdentifier>win-x64</RuntimeIdentifier>
    <IncludeNativeLibrariesForSelfExtract>true</IncludeNativeLibrariesForSelfExtract>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Avalonia" Version="11.*" />
    <PackageReference Include="Avalonia.Desktop" Version="11.*" />
    <PackageReference Include="Avalonia.Themes.Fluent" Version="11.*" />
    <PackageReference Include="ReactiveUI.Fody" Version="*" />
    <PackageReference Include="ReactiveUI" Version="*" />
  </ItemGroup>
</Project>
```

-----

## C++ DLL 實作規格

### Memory.h — 基礎封裝

```cpp
#pragma once
#include <Windows.h>
#include <cstdint>

namespace Mem {
    // 讀取目標 process 自身記憶體（DLL 已 injected，直接 deref）
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

### OffsetFinder.h — GObjects / GNames 定位

```cpp
#pragma once
#include <cstdint>

namespace OffsetFinder {

    struct EnginePointers {
        uintptr_t GObjects  = 0;   // FUObjectArray*
        uintptr_t GNames    = 0;   // FNamePool*
        uint32_t  UEVersion = 0;   // e.g. 500, 501, 503, 504
    };

    // 掃描並快取所有 global pointer
    // 失敗回傳 false，錯誤訊息透過 GetLastError() 取得
    bool FindAll(EnginePointers& out);

    // 取得 UE 版本，透過 FEngineVersion::Get() 字串解析
    uint32_t DetectVersion();
}
```

**AOB Pattern 策略（依 UE 版本）：**

|目標                  |Pattern                           |備註                   |
|--------------------|----------------------------------|---------------------|
|GObjects            |`48 8B 05 ?? ?? ?? ?? 48 8B 0C C8`|RIP-relative，解 offset|
|GNames (FNamePool)  |`48 8D 35 ?? ?? ?? ?? EB`         |RIP-relative         |
|AllocateUObjectIndex|掃函數特徵後取引用                         |備用方案                 |

RIP-relative 解析：

```cpp
// addr = 找到 pattern 的位址
uintptr_t rip    = addr + 7;           // 指令結束後
int32_t   rel32  = Mem::Read<int32_t>(addr + 3);
uintptr_t target = rip + rel32;        // 真正的全域指標位址
uintptr_t value  = Mem::Read<uintptr_t>(target);
```

### ObjectArray.h — FChunkedFixedUObjectArray

```cpp
#pragma once
#include <cstdint>
#include <functional>

// UE5 FUObjectArray 結構
struct FUObjectItem {
    uintptr_t Object;     // UObject*
    int32_t   Flags;
    int32_t   ClusterRootIndex;
    int32_t   SerialNumber;
    int32_t   _pad;
};

struct FChunkedFixedUObjectArray {
    // Objects: chunk table，每個 chunk 64 * 1024 elements
    static constexpr int32_t ElementsPerChunk = 64 * 1024;

    uintptr_t** Objects;        // FUObjectItem** (chunk 指標陣列)
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

    // callback: return false 停止遍歷
    void ForEach(std::function<bool(int32_t idx, uintptr_t obj)> cb);
}
```

### FNamePool.h — UE5 字串讀取

```cpp
#pragma once
#include <cstdint>
#include <string>

// UE5 FNamePool 結構
// Chunk size: 0x20000 bytes
// Stride: 2 bytes (每個 entry align)
struct FNameEntry {
    // Header: 低 6 bit = len, bit 6 = wide char flag
    uint16_t Header;

    // AnsiName or WideName 緊接在 Header 後
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
    // nameIndex = FName::ComparisonIndex (低32bit)
    // number    = FName::Number (高32bit, 用於 _1 _2 suffix)
}
```

### UStructWalker.h — 類別結構遍歷

```cpp
#pragma once
#include <cstdint>
#include <string>
#include <vector>

struct FieldInfo {
    uintptr_t   Address;      // FField* 位址
    std::string Name;
    std::string TypeName;
    int32_t     Offset;
    int32_t     Size;
    uint64_t    PropertyFlags;
};

struct ClassInfo {
    std::string          Name;
    std::string          FullPath;
    uintptr_t            SuperClass;    // 父類 UClass* 位址
    int32_t              PropertiesSize;
    std::vector<FieldInfo> Fields;
};

namespace UStructWalker {
    // 遍歷 UStruct 的所有 FField（UE4.25+ 用 FField，非 UProperty）
    ClassInfo WalkClass(uintptr_t uclassAddr);

    // 取得 UObject 的 UClass*
    uintptr_t GetClass(uintptr_t uobjectAddr);

    // 取得完整 path name，如 /Game/BP_Player.BP_Player_C
    std::string GetFullName(uintptr_t uobjectAddr);
}
```

**重要 UObject / UStruct offsets（UE5，需 OffsetFinder 動態確認）：**

|欄位             |結構         |典型 Offset|備註             |
|---------------|-----------|---------|---------------|
|InternalIndex  |UObjectBase|0x0C     |               |
|NamePrivate    |UObjectBase|0x18     |FName          |
|ClassPrivate   |UObjectBase|0x10     |UClass*        |
|OuterPrivate   |UObjectBase|0x20     |UObject*       |
|SuperStruct    |UStruct    |0x40     |父類             |
|ChildProperties|UStruct    |0x50     |FField* chain 頭|
|PropertiesSize |UStruct    |0x58     |               |
|Next (FField)  |FField     |0x20     |下一個 FField*    |
|Name (FField)  |FField     |0x28     |FName          |
|Class (FField) |FField     |0x08     |FFieldClass*   |
|Offset_Internal|FProperty  |0x4C     |屬性 offset      |
|ElementSize    |FProperty  |0x38     |大小             |


> **注意**：所有 offset 需透過 OffsetFinder 動態驗證，不可 hardcode。

### ExportAPI.h — 對外 C ABI

```cpp
#pragma once
#include <cstdint>
#include <Windows.h>

// 所有 export 函數使用 C ABI，供 CE Lua callFunction 使用
extern "C" {

    // === 初始化 ===
    __declspec(dllexport) bool     UE5_Init();
    __declspec(dllexport) void     UE5_Shutdown();
    __declspec(dllexport) uint32_t UE5_GetVersion();      // 500, 503, 504 ...

    // === Global Pointers ===
    __declspec(dllexport) uintptr_t UE5_GetGObjectsAddr();
    __declspec(dllexport) uintptr_t UE5_GetGNamesAddr();

    // === Object 查詢 ===
    __declspec(dllexport) int32_t   UE5_GetObjectCount();
    __declspec(dllexport) uintptr_t UE5_GetObjectByIndex(int32_t index);
    __declspec(dllexport) bool      UE5_GetObjectName(uintptr_t obj,
                                        char* buf, int32_t bufLen);
    __declspec(dllexport) bool      UE5_GetObjectFullName(uintptr_t obj,
                                        char* buf, int32_t bufLen);
    __declspec(dllexport) uintptr_t UE5_GetObjectClass(uintptr_t obj);
    __declspec(dllexport) uintptr_t UE5_GetObjectOuter(uintptr_t obj);

    // === 搜尋 ===
    __declspec(dllexport) uintptr_t UE5_FindObject(const char* fullPath);
    __declspec(dllexport) uintptr_t UE5_FindClass(const char* className);

    // === WalkClass（Begin/Get/End 批次模式，避免 callback 跨 DLL 邊界）===
    __declspec(dllexport) int32_t   UE5_WalkClassBegin(uintptr_t uclassAddr);
    __declspec(dllexport) bool      UE5_WalkClassGetField(int32_t index,
                                        uintptr_t* outAddr,
                                        char* nameOut, int32_t nameBufLen,
                                        char* typeOut, int32_t typeBufLen,
                                        int32_t* offsetOut,
                                        int32_t* sizeOut);
    __declspec(dllexport) void      UE5_WalkClassEnd();

    // === FName 直接解析 ===
    __declspec(dllexport) bool      UE5_ResolveFName(uint64_t fname,
                                        char* buf, int32_t bufLen);

    // === Pipe Server 控制 ===
    __declspec(dllexport) bool      UE5_StartPipeServer();
    __declspec(dllexport) void      UE5_StopPipeServer();
    __declspec(dllexport) bool      UE5_IsPipeConnected();
}
```

-----

### PipeProtocol.h — JSON IPC 協定定義

Pipe 名稱：`\\.\pipe\UE5DumpBfx`
格式：JSON，newline-delimited（每筆訊息一行 `\n`）
方向：雙向，Request/Response + 主動推送 Event

**Request（UI → DLL）**

```jsonc
// 初始化，回傳 UE 版本與 global pointer 位址
{ "id": 1, "cmd": "init" }

// 取得 global pointer 位址
{ "id": 2, "cmd": "get_pointers" }

// 取得 object 總數
{ "id": 3, "cmd": "get_object_count" }

// 分頁取得 object 列表
{ "id": 4, "cmd": "get_object_list", "offset": 0, "limit": 200 }

// 取得單一 object 詳情
{ "id": 5, "cmd": "get_object", "addr": "0x7FF123456789" }

// 搜尋 object
{ "id": 6, "cmd": "find_object", "path": "/Game/BP_Player.BP_Player_C" }

// 遍歷 class 欄位
{ "id": 7, "cmd": "walk_class", "addr": "0x7FF123456789" }

// 讀取記憶體（回傳 hex string）
{ "id": 8, "cmd": "read_mem", "addr": "0x7FF123456789", "size": 256 }

// 寫入記憶體
{ "id": 9, "cmd": "write_mem", "addr": "0x7FF123456789", "bytes": "3F800000" }

// 訂閱位址定期推送（Live Watch）
{ "id": 10, "cmd": "watch", "addr": "0x7FF123456789", "size": 4, "interval_ms": 500 }

// 取消訂閱
{ "id": 11, "cmd": "unwatch", "addr": "0x7FF123456789" }
```

**Response（DLL → UI）**

```jsonc
// init 回應
{ "id": 1, "ok": true, "ue_version": 504 }

// get_pointers 回應
{
  "id": 2, "ok": true,
  "gobjects": "0x7FF600A12340",
  "gnames":   "0x7FF600B56780",
  "object_count": 58432
}

// get_object_list 回應
{
  "id": 4, "ok": true, "total": 58432,
  "objects": [
    { "addr": "0x...", "name": "BP_Player_C", "class": "BlueprintGeneratedClass",
      "outer": "0x..." },
    ...
  ]
}

// walk_class 回應
{
  "id": 7, "ok": true,
  "class": {
    "name": "BP_Player_C",
    "full_path": "/Game/BP_Player.BP_Player_C",
    "super_addr": "0x...",
    "super_name": "Character",
    "props_size": 1024,
    "fields": [
      { "addr": "0x...", "name": "Health",    "type": "FloatProperty",
        "offset": 720, "size": 4 },
      { "addr": "0x...", "name": "MaxHealth", "type": "FloatProperty",
        "offset": 724, "size": 4 },
      { "addr": "0x...", "name": "Inventory", "type": "ArrayProperty",
        "offset": 728, "size": 16 }
    ]
  }
}

// 錯誤回應
{ "id": 5, "ok": false, "error": "Object not found" }

// Watch 主動推送 Event（無 id）
{ "event": "watch", "addr": "0x7FF...", "bytes": "0000803F", "timestamp": 1234567890 }
```

**PipeServer.h**

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

    // 主動推送 event 給已連線的 client
    void PushEvent(const std::string& jsonLine);

private:
    std::thread        m_acceptThread;
    std::atomic<bool>  m_running{false};
    std::atomic<bool>  m_clientConnected{false};
    HANDLE             m_clientPipe{INVALID_HANDLE_VALUE};
    std::mutex         m_writeMutex;

    // Watch 相關
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

-----

## CE Lua Bridge 規格

CE Lua 的職責縮減為：**注入 DLL + 啟動 Pipe Server**，UI 完全交給外部 App。

### ue5dump.lua — 主腳本

```lua
-- ue5dump.lua
local DLL_PATH = getCheatEngineDir() .. "ue5dumper\\UE5Dumper.dll"

local function callDLL(funcName, retType, ...)
    local fn = getAddress(funcName)
    assert(fn ~= 0, "[UE5Dump] 找不到函數: " .. funcName)
    return callFunction(fn, retType, ...)
end

local function main()
    -- 1. 注入 DLL
    loadLibrary(DLL_PATH)
    print("[UE5Dump] DLL 載入完成")

    -- 2. 初始化 core（AOB scan GObjects/GNames）
    assert(callDLL("UE5_Init", "bool"),
           "[UE5Dump] 初始化失敗，GObjects/GNames 掃描失敗")
    local ver = callDLL("UE5_GetVersion", "uint32")
    print(string.format("[UE5Dump] UE 版本: %d", ver))

    -- 3. 啟動 Pipe Server，等待外部 UI 連線
    assert(callDLL("UE5_StartPipeServer", "bool"),
           "[UE5Dump] Pipe Server 啟動失敗")
    print("[UE5Dump] Pipe Server 啟動，等待 UE5DumpUI 連線...")
    print("[UE5Dump] Pipe: \\\\.\\pipe\\UE5DumpBfx")
end

main()
```

-----

## Avalonia UI App 規格

### 技術棧

- **.NET 10** + **Avalonia 11.3.12** 或更高的相容版本。可使用 VS 2026 IDE開啟 (.sln file)
- **ReactiveUI + ReactiveUI.Fody**（ViewModel 屬性自動通知）
- **主題**：`FluentTheme` Dark mode
- **發布**：`PublishSingleFile` Native AoT、單一 exe
- **測試**：最低版本需求：`xunit.v3 3.2.2`、`Application Insights 3.0`、`Microsoft.Testing.Platform 2.1.0`
- **其它**：最低版本需求：`SkiaSharp 3.119.2`、`MicroCom.Runtime 0.11.3`、`SeriLog 4.3.1`、`Tmds.DBus.Protocol 0.90.3`

### AOT 相容性

| 元件 | 狀態 | 說明 |
|------|------|------|
| CommunityToolkit.Mvvm | ?? | Source generators，無 reflection |
| Serilog (Console/File) | ?? | 基本 sinks 相容 |
| Avalonia Compiled Bindings | ?? | `AvaloniaUseCompiledBindingsByDefault=true` |
| LibraryImport (P/Invoke) | ?? | 專為 AOT 設計 |
| ViewLocator | ?? | 已改為明確型別對應，無 reflection |

## Language

Code comments and UI strings are in English.

## I18n (Internationalization)

- UI 支援動態語言切換：繁體中文 (zh-TW) Taiwan、English (en)、日本語 (ja)
- 使用 `DynamicResource` 綁定，切換時無需重啟

### 5. Rules
- **Single Instance**: Use Mutex to ensure only one instance runs
- **Async everywhere**: All I/O, pipe operations, and alert actions must be async
- **Platform Abstraction**: Any system/OS-dependent call (P/Invoke, Registry, OS commands) MUST go through an interface in the project with central declaratrion (i.e. `Core`), and with implementations in platform-specific projects. The `Core` project must NEVER contain direct platform-specific code.
- **Log output**: All project log output folder is %APPDATA\UE5CEDumper\Logs, with 5 versions rotation. Everytime Application/DLL started, it will triggere a rotate. Each log file max size is 5MB.
- **Magic words management**: All magic words should keep in the same file (seperated by different projects) with proper comments


### PipeClient.cs — 連線管理

```csharp
// Services/PipeClient.cs
public class PipeClient : IDisposable
{
    private const string PipeName = "UE5DumpBfx";
    private NamedPipeClientStream? _pipe;
    private StreamReader? _reader;
    private StreamWriter? _writer;
    private CancellationTokenSource _cts = new();

    // 連線狀態（ReactiveUI）
    public IObservable<bool> IsConnected { get; }

    // 收到 Event 推送時觸發（watch 等）
    public event Action<JsonObject>? EventReceived;

    public async Task ConnectAsync()
    {
        _pipe = new NamedPipeClientStream(".", PipeName,
            PipeDirection.InOut, PipeOptions.Asynchronous);
        await _pipe.ConnectAsync(5000); // 5 秒 timeout
        _reader = new StreamReader(_pipe, Encoding.UTF8);
        _writer = new StreamWriter(_pipe, Encoding.UTF8) { AutoFlush = true };

        // 背景讀取推送 event
        _ = Task.Run(ReadLoopAsync);
    }

    // 送 request，等 response（用 id 配對）
    public async Task<JsonObject> SendAsync(JsonObject request,
        CancellationToken ct = default)
    {
        var json = request.ToJsonString();
        await _writer!.WriteLineAsync(json);
        // response 由 ReadLoop 配對後回傳（用 TaskCompletionSource）
        ...
    }

    private async Task ReadLoopAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            var line = await _reader!.ReadLineAsync();
            if (line is null) break;
            var obj = JsonNode.Parse(line) as JsonObject;
            if (obj?["event"] != null)
                EventReceived?.Invoke(obj);  // 推送 event
            else
                MatchResponse(obj!);          // 配對 request id
        }
    }
}
```

### DumpService.cs — 業務邏輯封裝

```csharp
// Services/DumpService.cs
public class DumpService
{
    private readonly PipeClient _pipe;

    public async Task<EngineState> InitAsync()
    {
        var res = await _pipe.SendAsync(new JsonObject { ["cmd"] = "init" });
        return new EngineState {
            UEVersion   = res["ue_version"]!.GetValue<int>(),
            GObjectsAddr = res["gobjects"]!.GetValue<string>()!,
            GNamesAddr   = res["gnames"]!.GetValue<string>()!,
        };
    }

    public async Task<List<UObjectNode>> GetObjectListAsync(int offset, int limit)
    {
        var res = await _pipe.SendAsync(new JsonObject {
            ["cmd"] = "get_object_list",
            ["offset"] = offset,
            ["limit"] = limit
        });
        // 解析 objects array → List<UObjectNode>
        ...
    }

    public async Task<ClassInfo> WalkClassAsync(string addr)
    {
        var res = await _pipe.SendAsync(new JsonObject {
            ["cmd"] = "walk_class",
            ["addr"] = addr
        });
        // 解析 class + fields → ClassInfo
        ...
    }

    public async Task<byte[]> ReadMemAsync(string addr, int size)
    {
        var res = await _pipe.SendAsync(new JsonObject {
            ["cmd"] = "read_mem",
            ["addr"] = addr,
            ["size"] = size
        });
        return Convert.FromHexString(res["bytes"]!.GetValue<string>()!);
    }
}
```

### MainWindow Layout（AXAML 骨架）

```xml
<!-- Views/MainWindow.axaml -->
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        x:Class="UE5DumpUI.Views.MainWindow"
        Title="UE5 Dump UI" Background="#1E1E1E"
        Width="1400" Height="900">

  <DockPanel>
    <!-- 頂部工具列 -->
    <StackPanel DockPanel.Dock="Top" Orientation="Horizontal"
                Background="#2D2D2D" Height="40">
      <Button Content="Connect" Command="{Binding ConnectCommand}" />
      <TextBlock Text="{Binding StatusText}" Foreground="#4EC9B0"
                 VerticalAlignment="Center" Margin="12,0" />
    </StackPanel>

    <!-- 主體：左側 Tree + 右側 Panel -->
    <Grid>
      <Grid.ColumnDefinitions>
        <ColumnDefinition Width="350" />
        <ColumnDefinition Width="4" />   <!-- Splitter -->
        <ColumnDefinition Width="*" />
      </Grid.ColumnDefinitions>

      <!-- 左：Object Tree -->
      <views:ObjectTreePanel Grid.Column="0" DataContext="{Binding ObjectTree}" />

      <GridSplitter Grid.Column="1" Background="#3C3C3C" />

      <!-- 右：Tab 切換各面板 -->
      <TabControl Grid.Column="2" Background="#1E1E1E">
        <TabItem Header="Class Structure">
          <views:ClassStructPanel DataContext="{Binding ClassStruct}" />
        </TabItem>
        <TabItem Header="Global Pointers">
          <views:PointerPanel DataContext="{Binding Pointers}" />
        </TabItem>
        <TabItem Header="Hex View">
          <views:HexViewPanel DataContext="{Binding HexView}" />
        </TabItem>
      </TabControl>
    </Grid>
  </DockPanel>
</Window>
```

### ObjectTreeViewModel.cs 骨架

```csharp
// ViewModels/ObjectTreeViewModel.cs
public class ObjectTreeViewModel : ReactiveObject
{
    private readonly DumpService _dump;

    [Reactive] public ObservableCollection<UObjectNode> Nodes { get; set; } = new();
    [Reactive] public UObjectNode? SelectedNode { get; set; }
    [Reactive] public string SearchText { get; set; } = "";
    [Reactive] public bool IsLoading { get; set; }

    public ReactiveCommand<Unit, Unit> LoadCommand { get; }
    public ReactiveCommand<string, Unit> SearchCommand { get; }

    // 選取 object 時通知 ClassStructViewModel 更新
    public IObservable<UObjectNode?> SelectionChanged =>
        this.WhenAnyValue(x => x.SelectedNode);
}
```

### HexViewPanel 重點

```xml
<!-- Views/HexViewPanel.axaml -->
<!-- 使用 ItemsControl + 等寬字體顯示 hex dump -->
<StackPanel>
  <StackPanel Orientation="Horizontal" Margin="0,0,0,8">
    <TextBox Watermark="Address (0x...)" Text="{Binding Address}" Width="200"/>
    <TextBox Watermark="Size" Text="{Binding Size}" Width="80" Margin="8,0"/>
    <Button Content="Read" Command="{Binding ReadCommand}" />
    <ToggleButton Content="Live Watch" IsChecked="{Binding IsWatching}"
                  Command="{Binding ToggleWatchCommand}" Margin="8,0,0,0"/>
  </StackPanel>

  <!-- Hex + ASCII 並排顯示 -->
  <ScrollViewer>
    <ItemsControl ItemsSource="{Binding HexRows}" FontFamily="Consolas"
                  FontSize="13" Foreground="#D4D4D4">
      <ItemsControl.ItemTemplate>
        <DataTemplate>
          <StackPanel Orientation="Horizontal">
            <TextBlock Text="{Binding Offset}" Foreground="#569CD6" Width="80"/>
            <TextBlock Text="{Binding HexPart}" Width="350" />
            <TextBlock Text="{Binding AsciiPart}" Foreground="#6A9955" />
          </StackPanel>
        </DataTemplate>
      </ItemsControl.ItemTemplate>
    </ItemsControl>
  </ScrollViewer>
</StackPanel>
```

-----

## 關鍵技術注意事項

### UE 版本差異

|版本         |主要差異                                   |
|-----------|---------------------------------------|
|UE5.0 ~ 5.1|FNamePool 標準格式                         |
|UE5.2      |`FChunkedFixedUObjectArray` stride 可能不同|
|UE5.3+     |部分遊戲啟用 Object Pointer Encryption       |
|UE5.4+     |`FField` 鏈結構穩定，無大改動                    |

### FField vs UProperty

- UE4.24 以前：`UProperty`（繼承 UObject）
- UE4.25 以後 / UE5 全版本：`FField`（**不**繼承 UObject）
- `UStruct::ChildProperties` = `FField*` chain 頭
- `UStruct::Children` = `UField*` chain（函數用，UFunction 繼承 UObject）
- WalkClass 必須同時處理兩條 chain

### FNamePool Chunk 計算

```cpp
// FNamePool layout
// Chunks: uintptr_t* 陣列
// 每個 chunk 最大 0x20000 bytes
// Stride: 每個 FNameEntry 對齊到 2 bytes

uintptr_t GetNameEntry(int32_t nameIndex) {
    int32_t chunkIndex  = nameIndex >> 16;       // 高16bit = chunk
    int32_t chunkOffset = (nameIndex & 0xFFFF) * 2; // 低16bit * stride
    uintptr_t chunk = Mem::Read<uintptr_t>(GNames + chunkIndex * 8);
    return chunk + chunkOffset;
}
```

### Export 函數命名規則

- 所有 export 以 `UE5_` 開頭
- 避免使用 callback 跨 DLL 邊界（改用 Begin/Get/End 批次模式）
- buffer 由 caller（Lua 側）分配，DLL 只寫入

-----

## 實作順序

### Phase 1 — DLL Core

1. `Memory.cpp` — AOBScan + GetModuleBase
1. `OffsetFinder.cpp` — GObjects / GNames pattern scan
1. `ObjectArray.cpp` — FChunkedFixedUObjectArray + ForEach
1. `FNamePool.cpp` — GetString
1. `ExportAPI.cpp` — C ABI 包裝，CE Lua 驗證可用

### Phase 2 — Pipe IPC

1. `PipeServer.cpp` — Named Pipe server + JSON dispatch
1. CE Lua 更新：改為只做 init + StartPipeServer
1. 用 PowerShell 測試 pipe（`[System.IO.Pipes.NamedPipeClientStream]`）

### Phase 3 — UI App

1. Avalonia 專案骨架 + ReactiveUI + Dark theme
1. `PipeClient.cs` — 連線 + 收發 + ReadLoop
1. `DumpService.cs` — 業務邏輯封裝
1. `PointerPanel` — 最簡單，先驗證 pipe 通
1. `ObjectTreePanel` — 分頁載入，虛擬化 TreeView
1. `ClassStructPanel` — walk_class → DataGrid 顯示
1. `HexViewPanel` — read_mem + live watch

### Phase 4 — 完善

1. UStructWalker 完整實作（FField chain + SuperStruct 繼承鏈）
1. Object Tree 搜尋 / 過濾
1. 單檔發布設定與測試

-----

## 參考來源

- `Encryqed/Dumper-7`：自動 offset 偵測邏輯（`OffsetFinder`、`ObjectArray.cpp`）
- `Spuckwaffel/UEDumper`：Live Editor UI 架構參考
- `UE4SS-RE/RE-UE4SS`：UE5 runtime reflection 另一角度實作
- Unreal Engine 原始碼（UObject.h, UStruct.h, FNamePool, FField）

-----

## 備用 / 未來擴展

- `UE5_WalkWorld()`：遍歷 UWorld 所有 Actor，加入 Object Tree 的 World 節點
- 加密 GObjects 支援（hook `InitObjectArrayDecryption`，參考 Dumper-7 介面）
- Object diff：快照比較，找出新增/刪除的 UObject
- Export to JSON/CSV：dump 完整架構到檔案
- CE Table 整合：UI 選取欄位後，自動產生對應的 CE address record
