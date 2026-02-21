# UE5CEDumper

一套針對 Unreal Engine 5 遊戲的**執行期結構探查工具**，由 C++ 注入式 DLL、Cheat Engine Lua Bridge 及 Avalonia UI 三個元件組成。透過 AOB 掃描定位引擎全域指標（GObjects / GNames / GWorld），遍歷 UObject 層次結構，並透過 Named Pipe 將即時資料串流至外部 GUI。

> **現況**：DLL 核心已通過 TQ2（UE 5.7）測試。動態 Offset 偵測（CasePreservingName）已編譯完成，待下次注入測試驗證。Avalonia UI 骨架已完成，Pipe 整合進行中。

---

## 功能一覽

| 功能 | 狀態 |
|---|---|
| GObjects AOB 掃描（9 組 Pattern，UE5.0–5.7） | ✅ |
| GNames AOB 掃描（6 組 Pattern）＋ `.data` 指標掃描 Fallback | ⚠️ Fallback 未測試 |
| GWorld AOB 掃描（6 組 Pattern） | ✅ |
| UE 版本偵測（PE VERSIONINFO ＋ 記憶體字串掃描） | ✅ |
| FChunkedFixedUObjectArray — ForEach / GetByIndex | ✅ |
| FNamePool 多格式讀取（格式 A/B、Chunk 索引） | ✅ |
| 動態 FField/FProperty/UStruct Offset 偵測（DynOff） | ⚠️ 已編譯，未測試 |
| CasePreservingName 偵測（UE5.5+，FName = 16 bytes） | ⚠️ 已編譯，未測試 |
| Live Data Walker — walk_class / walk_instance / walk_world | ✅ |
| Instance Finder — 依 Class 名稱搜尋所有實例 | ✅ |
| CE XML 匯出 — get_ce_pointer_info | ✅ |
| Named Pipe IPC 伺服器（JSON 換行分隔） | ✅ |
| CE Plugin 模式（Type 5 主選單入口） | ✅ |
| Avalonia UI（深色主題，i18n：zh-TW / en / ja） | 🔲 骨架完成 |
| Git Commit Hash ＋ 建置時間戳嵌入 DLL | ✅ |

---

## 系統架構

```
┌──────────────────────────────────────────────────────┐
│  遊戲程序（x64，UE5）                               │
│                                                      │
│  CE Lua  (UE5CEDumper.CT / ue5dump.lua)              │
│      │  injectDLL()                                  │
│      ▼                                               │
│  UE5Dumper.dll  [已注入]                             │
│      ├── ExportAPI       ← C ABI，供 CE Lua 呼叫     │
│      ├── PipeServer      ← Named Pipe IPC 伺服器     │
│      ├── OffsetFinder    ← AOB 掃描 + DynOff 偵測    │
│      ├── ObjectArray     ← FChunkedFixedUObjectArray  │
│      ├── FNamePool       ← UE5 FNamePool 讀取器      │
│      ├── UStructWalker   ← FField 鏈遍歷              │
│      ├── CEPlugin        ← CE Plugin Type 5 入口      │
│      ├── Logger          ← 檔案日誌，5 版輪轉，5MB 上限│
│      └── Memory          ← AOBScan + ResolveRIP      │
└──────────────────────┬───────────────────────────────┘
                       │ \\.\pipe\UE5DumpBfx
                       │ JSON，換行分隔
┌──────────────────────▼───────────────────────────────┐
│  UE5DumpUI.exe  (Avalonia 11 + CommunityToolkit.Mvvm)│
│      ├── PipeClient       ← 非同步，Request/Response │
│      ├── ObjectTreePanel  ← UObject 層次瀏覽器       │
│      ├── ClassStructPanel ← 欄位/屬性格狀顯示         │
│      ├── PointerPanel     ← GObjects / GNames 顯示   │
│      ├── HexViewPanel     ← 即時記憶體 Hex 檢視       │
│      ├── LiveWalkerPanel  ← 即時欄位值監控            │
│      └── InstanceFinder   ← 依 Class 名稱搜尋         │
└──────────────────────────────────────────────────────┘
```

---

## 目錄結構

```
UE5CEDumper/
├── build.cmd                   ← 建置包裝器（debug/release/publish/test）
├── build.ps1                   ← PowerShell 建置驅動
├── CMakeLists.txt              ← 根 CMake（委派至 dll/）
│
├── dll/                        ← C++ 注入式 DLL
│   ├── CMakeLists.txt
│   └── src/
│       ├── dllmain.cpp         ← DLL_PROCESS_ATTACH、AutoStartThreadProc
│       ├── Constants.h         ← 集中管理字串常數 + DynOff 命名空間
│       ├── ExportAPI.cpp/.h    ← C ABI 匯出（UE5_Init、UE5_WalkClassBegin…）
│       ├── PipeServer.cpp/.h   ← Named Pipe 伺服器 + JSON 分派
│       ├── PipeProtocol.h      ← IPC 命令/回應定義
│       ├── OffsetFinder.cpp/.h ← GObjects/GNames 掃描 + ValidateAndFixOffsets
│       ├── ObjectArray.cpp/.h  ← FChunkedFixedUObjectArray
│       ├── FNamePool.cpp/.h    ← FNamePool Chunk 讀取器
│       ├── UStructWalker.cpp/.h← FField 鏈 + SuperStruct 繼承鏈遍歷
│       ├── CEPlugin.cpp        ← CE Plugin Type 5 主選單
│       ├── Logger.cpp/.h       ← 檔案日誌，5 版輪轉，5 MB 上限
│       └── Memory.cpp/.h       ← AOBScan（僅掃執行區段）、ResolveRIP、ReadSafe
│
├── ui/                         ← C# Avalonia UI
│   ├── UE5DumpUI.sln
│   └── UE5DumpUI/
│       ├── UE5DumpUI.csproj
│       ├── Core/               ← 平台抽象介面（IPlatformService…）
│       ├── Models/             ← UObjectNode、FieldInfoModel、EngineState…
│       ├── Services/           ← PipeClient、DumpService、LoggingService…
│       ├── ViewModels/         ← MainWindow、ObjectTree、ClassStruct…
│       └── Views/              ← AXAML 面板
│
├── scripts/
│   ├── UE5CEDumper.CT          ← Cheat Engine Cheat Table（主要入口）
│   ├── ue5dump.lua             ← CE Lua 輔助腳本
│   └── test_pipe.ps1           ← PowerShell Pipe 煙霧測試
│
├── dist/                       ← 預先建置的 Release 二進位檔
│   ├── UE5Dumper.dll
│   └── UE5DumpUI.exe
│
└── vendor/                     ← Git Submodules（僅供參考）
    ├── Dumper-7/
    ├── UEDumper/
    └── GSpots/
```

---

## 環境需求

### 建置工具

| 工具 | 版本 |
|---|---|
| Visual Studio / MSVC | 2022 v17+（MSVC v143） |
| CMake | 3.25+ |
| Ninja | 任意近期版本 |
| .NET SDK | 10.0 |

> `build.cmd` / `build.ps1` 透過 `vswhere` 自動定位 MSVC，不使用硬編路徑。

### 執行環境

- Windows 10/11 x64
- Cheat Engine 7.5+
- 已執行的 Unreal Engine 5 遊戲程序（x64）

---

## 建置方式

```cmd
:: 建置全部（Release）
build

:: 僅建置 DLL
build dll

:: 僅建置 UI
build ui

:: Debug 建置
build debug

:: 執行測試
build test

:: Native AOT 單檔發布
build publish

:: 先清除再 Release 建置
build clean
```

輸出位置：
- `build/UE5Dumper.dll` — 注入式 DLL
- `ui/UE5DumpUI/bin/Release/.../UE5DumpUI.exe` — UI 應用程式
- `dist/` — Release 或 Publish 成功後自動複製至此

---

## 使用流程

### 1. 建置

```cmd
build release
```

### 2. 部署 DLL

將 `dist\UE5Dumper.dll` 放在與 `dist\UE5CEDumper.CT` **相同的資料夾**中。

### 3. 透過 Cheat Engine 注入

1. 開啟 Cheat Engine，附加至 UE5 遊戲程序。
2. 開啟 `dist\UE5CEDumper.CT`。
3. 勾選 **[Inject DLL + Start Pipe Server]**。
4. Lua 主控台會顯示 15 秒倒數，DLL 在背景掃描 GObjects/GNames。

### 4. 連接 UI

```cmd
dist\UE5DumpUI.exe
```

點擊工具列的 **Connect** 按鈕，UI 將連接至 `\\.\pipe\UE5DumpBfx`。

### 5. 除錯診斷

- DLL 日誌：`%LOCALAPPDATA%\UE5CEDumper\Logs\UE5Dumper-0.log`
- UI 日誌：`%APPDATA%\UE5CEDumper\Logs\UE5DumpUI-0.log`
- Pipe 煙霧測試：`powershell -f scripts\test_pipe.ps1`

---

## Pipe IPC 協定

通訊格式為換行分隔 JSON，透過 `\\.\pipe\UE5DumpBfx` 傳輸。

**主要命令（UI → DLL）：**

| 命令 | 說明 |
|---|---|
| `init` | 初始化引擎，回傳 UE 版本與全域指標位址 |
| `get_pointers` | 回傳 GObjects / GNames 位址與物件總數 |
| `get_object_list` | 分頁取得 UObject 列表 |
| `walk_class` | 回傳 UClass 的所有 FField 欄位資訊 |
| `walk_world` | 遍歷 GWorld → PersistentLevel → Actors |
| `find_instances` | 搜尋指定 Class 名稱的所有實例 |
| `get_ce_pointer_info` | 回傳 Cheat Engine 相容的 XML 指標鏈 |
| `read_mem` / `write_mem` | 原始記憶體存取 |
| `watch` / `unwatch` | 訂閱即時記憶體更新（主動推送事件） |
| `get_offsets` | 回傳執行期偵測的 DynOff 值（診斷用途） |

---

## 動態 Offset 偵測（DynOff）

UE 5.5+ 引入了 **CasePreservingName** 機制，`FName` 從 8 bytes 擴大為 16 bytes（新增 `DisplayIndex` 欄位）。這使得 `FField::Flags`、`FField::Name` 及所有 `FProperty` 欄位的 Offset 偏移 +8 bytes。

`ValidateAndFixOffsets()`（位於 `OffsetFinder.cpp`）透過探測已知結構（Guid — 欄位 A/B/C/D 在 Offset 0/4/8/12；Vector — X/Y/Z 在 Offset 0/4/8）來自動偵測正確的記憶體佈局，並在執行期動態更新 `DynOff::*` 變數。

所有 `UStructWalker` 程式碼均使用 `DynOff::*`，完全不依賴硬編的 FField/FProperty Offset。

---

## UE 版本支援情況

| UE 版本 | GObjects | GNames | DynOff |
|---|---|---|---|
| 5.0–5.2 | ✅ | ✅ | 不需要 |
| 5.3–5.4 | ✅ | ✅ | 不需要 |
| 5.5–5.7 | ✅ | ⚠️ 依賴 Fallback | ⚠️ 已編譯，未測試 |

---

## 已知問題

- **TQ2（UE 5.7）上 GNames Pattern 全部失敗** — 4 組 LEA-based Pattern 均解析到錯誤的全域變數。已實作 `.data` 區段指標掃描 Fallback（`FindGNamesByPointerScan`），但尚未測試。
- **DynOff / CasePreservingName** — 已完成編譯，需在 TQ2 上進行注入測試驗證。
- **Avalonia UI** — 骨架已完成；端對端 Pipe 整合待 DynOff 驗證完成後推進。

---

## 參考來源與致謝

| 專案 | 用途 |
|---|---|
| [Encryqed/Dumper-7](https://github.com/Encryqed/Dumper-7) | 動態 Offset 偵測模式、FField/FProperty 探測策略 |
| [Spuckwaffel/UEDumper](https://github.com/Spuckwaffel/UEDumper) | Live Editor UI 架構參考 |
| [UE4SS-RE/RE-UE4SS](https://github.com/UE4SS-RE/RE-UE4SS) | UE5 執行期反射，另一角度的實作參考 |
| [Do0ks/GSpots](https://github.com/Do0ks/GSpots) | GObjects/GNames 的額外 AOB Pattern |
| [trumank/patternsleuth](https://github.com/trumank/patternsleuth) | GObjects/GNames 的額外 AOB Pattern 來源 |
| [nlohmann/json](https://github.com/nlohmann/json) | DLL 使用的 Header-only JSON 函式庫 |
| [cheat-engine/cheat-engine](https://github.com/cheat-engine/cheat-engine) | CE Lua 腳本 API 參考 |
| AOBMaker（私有） | AOB Pattern 生成工具 |

---

## 授權條款

[MIT](LICENSE) © 2026 bbfox0703
