# UE5CEDumper
<img src="./img/UE5CEDumper.jpg" alt="UE5CEDumper"/>  

一套針對 Unreal Engine 4 & 5 遊戲的**執行期結構探查工具**、配和 Cheat Engine、可更方便的開發 Table。

UE5CEDumper 是一款UE資料的互動式的檢查工具。不同於其它 Dumper，它為遊戲記憶體提供了一個**即時視窗**，讓你能即時瀏覽物件、尋找實例，並匯出支援 Cheat Engine (CE) 的結構定義。

> 本工具為「實戰型」的 Table 製作人員打造，旨在消除UE物件的偵測、識別到在 CE 中的實作之間的鴻溝。

> 這並不定一般 Dumper 無法把大量資料倒出並分析，其集中在簡單找到 UE 結構，並和 CE 開發配合使用。故請視為是一個通用型的 UE 工具。

---
[![Platform](https://img.shields.io/badge/Platform-Windows%20x64-0078D4)](https://www.microsoft.com/windows)
[![UE Version](https://img.shields.io/badge/UE-4.18--5.7-orange)](https://www.unrealengine.com/)
[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4)](https://dotnet.microsoft.com/)
[![C++](https://img.shields.io/badge/C++-23-00599C)](https://isocpp.org/)
[![Avalonia](https://img.shields.io/badge/Avalonia-UI-8B5CF6)](https://avaloniaui.net/)
[![License](https://img.shields.io/badge/License-MIT-green.svg)](LICENSE)
[![Built with Claude Code](https://img.shields.io/badge/Built%20with-Claude%20Code-CC785C?logo=claude)](https://claude.ai/code)

## 實測版本矩陣 (Tested Version Matrix)

| UE 版本 | GObjects | GNames | DynOff | 已驗證遊戲 |
|---|:---:|:---:|:---:|---|
| **4.18** | ✅ | ✅ | ✅ | *Final Fantasy VII Remake Intergrade* |
| **4.21 – 4.24** | ✅ | ✅ | ✅ | *《STAR WARS 絕地：組織殞落™》, 勇者鬥惡龍 XI S, 偶像大師 星耀季節, 八方旅人, 勇者鬥惡龍 I&II / III HD-2D 重製版* |
| **4.25 – 4.27** | ✅ | ✅ | ✅ | *Final Fantasy VII Rebirth, Tower of Mask, 霍格華茲的傳承, 復活邪神 2 七英雄的復仇, Ghostwire: Tokyo* |
| **5.0 – 5.2** | ✅ | ✅ | ✅ | (透過通用特徵碼確認支援) |
| **5.3 – 5.4** | ✅ | ✅ | ✅ | *Satisfactory (v1.1.3.1 滿意工廠)* |
| **5.5 – 5.7** | ✅ | ✅* | ✅** | *泰坦任務 2, EverSpace 2, Lushfoil Photography Sim, 莊園領主 (Manor Lords), Cat Island Petrichor Demo, Way of the Hunter 2 Demo, COMBAT PILOT: CARRIER QUALIFICATION Demo* |

*\*GNames 在 5.5+ 版本使用 .data 指標掃描回退機制。*
*\**DynOff 支援 **CasePreservingName (FName = 16 bytes)** 佈局。

---

## 專為 Table 製作人員設計的功能

| 功能 | 說明 |
|---|---|
| **AOB 掃描** | 整合來自 14 個來源的 128 組 AOB 特徵碼 + 5 組符號匯出。支援 `FChunkedFixed` 與 `FFixed` 物件陣列，並可偵測加密 GObjects 與預設佈局。 |
| **DynOff (動態偏移量)** | 執行時自動探測 `FField`、`FProperty` 與 `UStruct`。遊戲改版後不再需要手動更新偏移量。 |
| **Live Walker (即時檢視)** | 即時檢查實例的記憶體數據，數值變動一目瞭然。支援指標導航 (→)、內嵌結構展開 ({})、容器深入 ([])。瀏覽軌跡 (Breadcrumb) 完整記錄導航歷程。可設定自動重新整理間隔。 |
| **定義檢視 (Definition View)** | 深入 UScriptStruct/UClass 定義以查看欄位佈局、類型及即時 Hex 數值。與 CE XML 匯出無縫整合，支援指標鏈導航。 |
| **容器深入 (Container Drill-Down)** | 支援深入 **Array**、**Map**、**Set** 容器。ObjectProperty 元素顯示已解析的名稱，並可點擊跳轉至目標物件。 |
| **UFunction 呼叫器** | 直接從 UI 透過 `ProcessEvent` 呼叫遊戲函式。透過 MinHook 掛鈎實現遊戲執行緒安全調度。支援 ScriptStruct 參數 — 12 組已知引擎結構 (FVector, FRotator, FColor 等) + 動態結構佈局探索。 |
| **實例搜尋器** | 瞬間定位類別中所有作用中的實例。支援一鍵開啟 Live Walker 檢視。 |
| **屬性搜尋 (Property Search)** | 跨所有類別搜尋特定屬性名稱，無需瀏覽完整的物件階層。 |
| **類別結構 (Class Structure)** | 瀏覽類別定義的完整欄位佈局、類型、偏移量及繼承鏈。 |
| **多格式 CE 匯出** | **CE XML** (指標鏈位址紀錄)、**CSX** (CE Structure Dissect)、**SDK Header** (.h)、**CE Field** (單一欄位)、**CE AA Script** (Auto Assembler)。支援純量、位元欄位、結構、陣列、Map/Set、及 UEnum 下拉選單。 |
| **Proxy DLL** | `version.dll` 代理注入 — 不需要 Cheat Engine。將檔案放在遊戲 exe 旁，啟動遊戲後連接 UI 即可使用。 |
| **現代化 UI** | 外部 **Avalonia UI** 透過高速具名管道連線 (29 組 JSON-RPC 指令)。以 Native AOT 發佈。確保遊戲執行效能不受影響。 |

---

## 架構與工作流程 (Architecture & Workflow)

### 方式 A：Cheat Engine（推薦）

1. **注入 DLL**: 開啟 Cheat Engine 附加遊戲，確保遊戲存檔已載入。開啟 `UE5CEDumper.CT`。
2. **啟用 CE 腳本**: 先啟用 `init <== enable after process attached`，再啟用 `Inject DLL + Start Pipe Server`。DLL 自動定位引擎全域指標，並偵測 UE 版本與記憶體佈局。
3. **連接 UI**: 等待數秒讓掃描完成。啟動 **UE5DumpUI.exe** 並點擊 **Connect**。即時數據透過具名管道 (Named Pipe, JSON-RPC) 串流至 UI。
4. **瀏覽及分析**: 瀏覽 `UObject` 階層，找到目標 Class，深入容器查看元素，或由 CE 中的位址反查並匯出。

### 方式 B：Proxy DLL（免 Cheat Engine）

1. **放置 DLL**: 將 `version.dll`（由 `build.ps1 -Target ProxyDLL` 產生）複製到遊戲根目錄（與 `.exe` 同層）。
2. **啟動遊戲**: 正常啟動遊戲。Proxy DLL 會自動載入並啟動管道伺服器。
3. **載入存檔**: 進入遊戲世界，確保 UE 物件已載入記憶體。
4. **連接 + 掃描**: 啟動 **UE5DumpUI.exe**，點擊 **Connect**，再點擊 **Start Scan**。DLL 執行 AOB 掃描並將引擎資料回傳至 UI。
5. **瀏覽及分析**: 與方式 A 相同 — 瀏覽物件、尋找實例、匯出 CE 結構。

> **注意**: 請勿同時使用兩種方式。若 Proxy DLL 已放在遊戲目錄中，請勿再透過 CE 注入 `UE5Dumper.dll`。DLL 會偵測重複實例並跳過自動啟動以避免衝突。

| **Game Process (Injected)** |
| :---: |
| DLL + CE Lua Bridge（或 Proxy DLL）|
| ⬇️ |
| **Named Pipe IPC (JSON-RPC Protocol)** |
| ⬇️ |
| **External GUI (Avalonia UI App)** |

## 系統需求 (Requirements)

### 編譯環境 (Build)

| 工具 | 版本要求 |
|---|---|
| Visual Studio / MSVC | 2022 v17+ (MSVC v143) |
| CMake | 3.25+ |
| Ninja | 任何近期版本 |
| .NET SDK | 10.0 |

> `build.cmd` / `build.ps1` 會透過 `vswhere` 自動定位 MSVC，無需手動設定路徑。

### 執行環境 (Runtime)

- Windows 10/11 x64
- Cheat Engine 7.6+（CE 注入方式）*或* Proxy DLL（免 CE）
- 執行中的 Unreal Engine 4 或 5 遊戲作業程序 (x64)

---

## 重要注意事項 (Important Notes)

* **自定義數據結構**: 在如《FF7 Rebirth》等遊戲中，部分關鍵數據（如 HP）存儲在標準 `UObject` 之外的自定義結構中。Live Walker 可協助探查這些區域，但無法直接自動發現。
* **GWorld 連通性**: `GWorld` 遍歷在約 60% 的實測遊戲中可正常運作。若無法運作，請改用 **Object Tree** 或 **Instance Finder** 作為主要進入點。
* **容器元素限制**: Array/Map/Set 的元素讀取受可調限制值控制，避免過度記憶體存取。如需處理大型容器，請在 Live Walker 中調整 **Array Limit** 滑桿。

---

## 專案貢獻 (Contributing)

請參閱 [CONTRIBUTING.md](CONTRIBUTING.md) 以瞭解：
- **提交 AOB 特徵碼** — 需要提供哪些證據及驗證流程。
- **回報偵測失敗** — 需要附上哪些日誌與資訊。
- **程式碼貢獻** — PR 流程與程式碼風格規範。

---

## 參考專案與致謝 (References & Credits)

| 專案 | 用途 |
|---|---|
| [Encryqed/Dumper-7](https://github.com/Encryqed/Dumper-7) | 動態偏移量偵測模式、FField/FProperty 探測策略 |
| [Spuckwaffel/UEDumper](https://github.com/Spuckwaffel/UEDumper) | 即時編輯器 UI 架構參考 |
| [UE4SS-RE/RE-UE4SS](https://github.com/UE4SS-RE/RE-UE4SS) | UE5 執行時反射機制參考 |
| [Do0ks/GSpots](https://github.com/Do0ks/GSpots) | GObjects/GNames 的額外 AOB 特徵碼 |
| [trumank/patternsleuth](https://github.com/trumank/patternsleuth) | 額外的 AOB 掃描模式 |
| UE4 Dumper.CT | Cake-san 的 Cheat Table — 額外的 AOB 特徵碼 |
| [nlohmann/json](https://github.com/nlohmann/json) | DLL 使用的 JSON 函式庫 |
| [cheat-engine/cheat-engine](https://github.com/cheat-engine/cheat-engine) | CE Lua 腳本 API 參考 |
| **AOBMaker (內部工具)** | AOB 特徵碼產生工具 |

---

## 使用 Claude Code 開發

本專案在 Anthropic 的 [Claude Code](https://claude.ai/code) 協助下開發。C++ DLL、C# Avalonia UI、建置腳本及文件均由開發者與 Claude Code 協作完成。

---

## 下載

編譯、以目前的版本、建立你自己的程式碼；或是使用以下連結來下載我預先編譯好的版本：

https://opencheattables.com/viewtopic.php?f=17&t=1797

---

**授權條款**: [MIT](LICENSE) © 2026 bbfox0703