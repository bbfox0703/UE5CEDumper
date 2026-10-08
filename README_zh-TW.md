# UE5CEDumper — 搭配 Cheat Engine 的 UE Dumper（Unreal Engine 4 / 5）
<img src="./img/UE5CEDumper.jpg" alt="UE5CEDumper：Unreal Engine 4 與 5 的 UE Dumper"/>  

UE5CEDumper 是一款 UE Dumper（Unreal Engine dumper），適用於以 Unreal Engine 4.11 到 5.8 製作的 Windows x64 遊戲。載入遊戲的 DLL 會找出引擎的全域表（GObjects、GNames、GWorld）並讀取反射資料；獨立的 UI 連上這個 DLL 後，可以瀏覽物件與類別及其即時屬性值、依名稱或數值搜尋，並把找到的結果匯出。

它是設計來搭配 Cheat Engine 使用的：可匯出指標鏈記錄、Structure Dissect 定義與 Auto Assembler 腳本，裝了選用的 AOBMaker 外掛時還能直接送進執行中的 Cheat Engine。另外也能匯出 SDK headers、USMAP 檔，以及完整的中繼資料傾印（Dump All，`.jsonl`）供離線使用。

> ### 使用範圍
>
> **僅限 Windows x64、單機／離線使用。** 這是給你**合法擁有**的遊戲、在你自己的機器上使用的檢視與除錯工具。
> 請勿用於多人、競技或線上模式 — 除了不公平之外，反作弊、帳號封鎖與法律風險實際上都集中在那裡。
> UE5CEDumper 讀取執行中 process 的記憶體；它不散布任何遊戲程式碼、資產或金鑰，也不碰 pak/IoStore 的容器加密。

---
[![Platform](https://img.shields.io/badge/Platform-Windows%20x64-0078D4)](https://www.microsoft.com/windows)
[![UE Version](https://img.shields.io/badge/UE-4.11--5.8-orange)](https://www.unrealengine.com/)
[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4)](https://dotnet.microsoft.com/)
[![C++](https://img.shields.io/badge/C++-23-00599C)](https://isocpp.org/)
[![Avalonia](https://img.shields.io/badge/Avalonia-UI-8B5CF6)](https://avaloniaui.net/)
[![License](https://img.shields.io/badge/License-MIT-green.svg)](LICENSE)
[![Built with Claude Code](https://img.shields.io/badge/Built%20with-Claude%20Code-CC785C?logo=claude)](https://claude.ai/code)
[![Local LLM](https://img.shields.io/badge/Local%20LLM-Google%20Gemma%204-4285F4?logo=google&logoColor=white)](https://deepmind.google/models/gemma/)

## 螢幕截圖 (Screenshots)
<img src="./img/MainUI.gif" alt="Live Walker"/>  
<img src="./img/Value_Search.webp" alt="Value Search"/>  

## 重點亮點 (Highlights)

快速了解你實際能用它做什麼 — 完整的 Table 製作功能列表請見 **[docs/Features_zh-TW.md](docs/Features_zh-TW.md)**：

- **全域指標免設定、自動偵測** — 一般的 UE Dumper 都要你先自行找到 GObjects / GNames / GWorld 三大指標。UE5CEDumper 內建 150 組以上的 AOB 特徵碼資料庫（另加 symbol export 查找），涵蓋 **GObjects、GNames、GWorld、GEngine 與 SparseDelegates**，並以 UE 4.11 – 5.8 的多個執行檔語料逐一驗證，因此**大多數 UE 遊戲的三大指標都能自動偵測**。所有特徵碼都落空時也不會直接放棄，而是自動 fallback：GObjects 改做資料區段掃描、GNames 改用字串參照與指標掃描，**GWorld 則先在 GObjects 中搜尋 UWorld 實例反查靜態指標，再退回 `GEngine → GameViewport → World`**。
- **即時記憶體檢查** — 瀏覽物件、找出某類別的每個實例、以即時數值深入 struct / class 佈局。
- **Value Search（數值搜尋）** — Cheat-Engine 風格的 First Scan / Next Scan，掃描每個 UE 屬性（數字、字串、向量、array / map / set），不需知道 offset 就能找到目標。**Group 模式**能找出同時持有多個數值的物件（例如 `Str + Def + Dex + Int`），另有可選的 **Deep** 模式深入巢狀容器。
- **Teleport（傳送）** — 3 個 marker 存檔 / 召回（BugItGo 風格）、俯視 / 2.5D 遊戲的**傳送到游標**、可自設全域熱鍵，以及唯讀的**相機 POV** 讀值。¹
- **角色調整（Player tuning）** — **Super Jump / Move Speed / Gravity** 滑桿、**God Mode（無敵）**、**Time Dilation（全域或僅玩家的慢動作 / 凍結 / 快轉）** — 全部以反射強制設定並對抗每 tick 覆寫，因此重生後仍維持。可綁熱鍵或匯出成 CE 開 / 關記錄。
- **Debug Camera（除錯相機）** — 強制開 / 關自由飛行除錯相機，即使在通常會卡住的 Shipping build 也能可靠開關。
- **Console（主控台）** — 發現並一鍵呼叫許多遊戲保留的 `fly` / `god` / `ghost` / 遊戲特定 exec 指令。
- **即時函式剖析器（Live Funcs）** — 錄下*一個*遊戲內動作（開商店、衝刺），就能看到實際觸發了哪些 UFunction，附 baseline 差異 + 雜訊過濾。名稱搜尋找不到時，用「行為」找。
- **一鍵 CE 匯出** — 指標鏈 XML、Structure Dissect (CSX)、SDK headers、AA 腳本、多列 `.CT` 批次。
- **Dump Explorer（離線瀏覽）** — 離線瀏覽匯出的「Dump All」`.jsonl`，一個關鍵字同時搜尋 class + property + function。**Compare…** 可與同一款遊戲的另一份 dump 比對，輸出 HTML 報告，列出修補檔移動、改型別、新增或移除了哪些項目。
- **注入免 Cheat Engine** — `version.dll` **proxy DLL**、UI 的 **Inject into running game…**，或 **`inject-ue.ps1`** 命令列（管理員遊戲會自動提權）。Proxy Deploy **逐遊戲建議正確的 proxy**。

> ¹ 少數被大量精簡的 Shipping build（例如泰坦任務 2）無法做*游標*傳送 — 它們移除了標準的游標 / viewport / line-trace API 並改用自訂虛擬游標。詳見 [docs/teleport-spec.md](docs/teleport-spec.md)。

## 實測版本矩陣 (Tested Version Matrix)

依 UE 版本區間分組。各遊戲的細節（佈局差異、proxy 註記、驗證情形）收錄於 **[docs/test-games.md](docs/test-games.md)**。*Satisfactory* 出現在兩列，因為它的 UE 版本隨遊戲版本而變動。

| UE 版本 | GObjects | GNames | DynOff | 已驗證遊戲 |
|---|:---:|:---:|:---:|---|
| **4.11 – 4.14** | ✅ | ✅ | ✅ | NEKOPALIVE (ネコパラ) |
| **4.15 – 4.17** | ✅ | ✅ | ✅ | Extinction |
| **4.18 – 4.20** | ✅ | ✅ | ✅ | Final Fantasy VII Remake Intergrade, The Occupation, 勇者鬥惡龍 XI S, 八方旅人 |
| **4.21 – 4.24** | ✅ | ✅ | ✅ | 《STAR WARS 絕地：組織殞落™》, 偶像大師 星耀季節 |
| **4.25 – 4.27** | ✅ | ✅ | ✅ | Final Fantasy VII Rebirth, 勇者鬥惡龍 I&II / III HD-2D 重製版, 劍星 (Stellar Blade), Tower of Mask, 霍格華茲的傳承, 復活邪神 2 七英雄的復仇, Ghostwire: Tokyo, TimeSplitters Rewind, The Artisan of Glimmith, Barn Finders, 機動戰士 GUNDAM SEED 激鬥命運 復刻版, 女神異聞錄３ Reload (Persona 3 Reload) |
| **5.0 – 5.2** | ✅ | ✅ | ✅ | Squirrel With A Gun, Caravan Sandwitch, Meltopia, Retro Rewind Demo |
| **5.3 – 5.4** | ✅ | ✅ | ✅ | Satisfactory (v1.1.3.1 滿意工廠), Colossal, Avowed, 艾恩葛朗特 迴盪新聲 Demo (Echoes of Aincrad), 冒險家艾略特的千年奇譚 (The Adventures of Elliot), MindsEye, DragonSword Awakening, 天外世界 2 (The Outer Worlds 2) |
| **5.5 – 5.7** | ✅ | ✅ | ✅ | 泰坦任務 2, EverSpace 2, Lushfoil Photography Sim, 莊園領主 (Manor Lords), Cat Island Petrichor Demo, Way of the Hunter 2 Demo, COMBAT PILOT: CARRIER QUALIFICATION Demo, Solarpunk (太陽龐克), Pionero Capital Demo, Satisfactory (滿意工廠 v1.2.3.1), Star Trek Voyager – Across the Unknown |
| **5.8** | ✅ | ✅ | ✅ | Ski-E-O Demo |

*UE 4.11 是支援下限；4.10 以下會直接顯示為不支援。*

---

## 開始使用 (Getting started)

把 DLL 載入遊戲有三種方式，一次只用一種；請先載入存檔，確保遊戲物件已存在於記憶體。

### 方式 A：Cheat Engine

1. 用 Cheat Engine 附加遊戲，開啟 `UE5CEDumper.CT`。
2. 先啟用 `init <== enable after process attached`，再啟用 `Inject DLL + Start Pipe Server`。
3. 啟動 **UE5DumpUI.exe** 並點擊 **Connect**。

### 方式 B：Proxy DLL（推薦，免 Cheat Engine）

1. 在 **UE5DumpUI.exe** 開啟 **Proxy Deploy** 分頁，部署到該遊戲。它會建議這款遊戲適用的 proxy DLL，並複製到遊戲 `.exe` 旁邊。
2. 啟動遊戲並載入存檔。
3. 點擊 **Connect**，再點擊 **Start Scan**。

### 方式 C：對執行中的遊戲注入（免 CE、免重開）

- **從 UI**：Proxy Deploy 分頁 → **Inject into running game…** → 選遊戲 → **Inject**。
- **從命令列**：`.\inject-ue.ps1`（`-List` 列出偵測到的遊戲，`-ProcessId <pid>` 指定其中一個），接著啟動 **UE5DumpUI.exe** 並 **Connect**。

之後就能瀏覽物件樹、尋找類別或實例，並匯出需要的內容。逐步教學請見 [Wiki](https://github.com/bbfox0703/UE5CEDumper/wiki)，完整功能列表請見 [docs/Features_zh-TW.md](docs/Features_zh-TW.md)。

> **僅限 x64 遊戲。** 與所有注入方式相同，可能被防毒標記，並會被 kernel 反作弊（EAC / BattlEye）擋下。使用範圍請見本文件開頭的說明。

### 選用：AOBMaker

[AOBMaker](https://github.com/bbfox0703/AOBMaker-Release) 與它的 Cheat Engine 外掛，可讓 UE5CEDumper 把找到的內容直接送進開啟中的 CE table：記憶體記錄、AA 腳本、GObjects / GNames / GWorld 的符號，以及把 Live Walker 的結構送進 Structure Dissect；也能讓 CE 的記憶體檢視與反組譯器跳到指定位址。其餘功能不需要它也能運作。

## 系統需求 (Requirements)

- Windows 10/11 x64
- 執行中的 Unreal Engine 4 或 5 遊戲 (x64)
- Cheat Engine（僅方式 A 需要；最後測試版本為 Cheat Engine 7.7）

要從原始碼建置：先執行 `bootstrap.cmd` 查看這台機器需要什麼，再執行 `build.cmd`。詳見 [docs/toolchain.md](docs/toolchain.md)。

---

## 注意事項 (Notes)

* **`UObject` 之外的資料**：部分遊戲（例如《FF7 Rebirth》）把 HP 這類重要數值存在自己的結構裡。Live Walker 可協助查看這些區域，但無法自動找出來。
* **Start from GWorld 無法使用時**：若遊戲不在上方表格內，請改用 **Object Tree** 或 **Instance Finder** 作為進入點。
* **透過 EA app 啟動的遊戲**不會載入任何 proxy DLL，請在遊戲執行後改用方式 A 或 C。
* **切到背景就暫停的遊戲**會讓需要呼叫遊戲的操作逾時。此時 UI 會顯示「game thread stalled」提示，實驗性的 **Keep Foreground** 開關可繞過它。
* **大型 Array / Map / Set** 的讀取數量受 Live Walker 的 **Array Limit** 滑桿限制。

各遊戲的註記收錄於 [docs/test-games.md](docs/test-games.md)。

---

## 回報問題 (Reporting a problem)

請開一個 [issue](https://github.com/bbfox0703/UE5CEDumper/issues)，寫明遊戲名稱、做了什麼、發生了什麼，並附上這兩個 log 資料夾：`%LOCALAPPDATA%\UE5CEDumper\Logs\<遊戲 exe 名稱>` 與 `%LOCALAPPDATA%\UE5CEDumper\Logs\UE5DumpUI`。其中遊戲的 `scan` log 最有幫助。

---

## 參考專案與致謝 (References & Credits)

| 專案 | 用途 |
|---|---|
| [Encryqed/Dumper-7](https://github.com/Encryqed/Dumper-7) | 動態偏移量偵測模式、FField/FProperty 探測策略 |
| [UE4SS-RE/RE-UE4SS](https://github.com/UE4SS-RE/RE-UE4SS) | UE5 執行時反射機制參考 |
| [Spuckwaffel/UEDumper](https://github.com/Spuckwaffel/UEDumper) | 即時編輯器 UI 架構參考 |
| [trumank/patternsleuth](https://github.com/trumank/patternsleuth) | GObjects/GNames 的額外 AOB 特徵碼 |
| [Do0ks/GSpots](https://github.com/Do0ks/GSpots) | GObjects/GNames 的 AOB 特徵碼 |
| [nlohmann/json](https://github.com/nlohmann/json) | DLL 使用的 JSON 函式庫 |
| [cheat-engine/cheat-engine](https://github.com/cheat-engine/cheat-engine) | CE Lua 腳本 API 參考 |
| **AOBMaker (內部工具)** | AOB 特徵碼產生工具，AA 腳本產生工具、快速 CE-goto 功能 (非必備) |
| UE4 Dumper.CT | Cake-san 的 Cheat Table — 額外的 UE4 AOB 特徵碼（`Himmel.h` 中的 CT 系列） |

**測試** — 感謝 **Marc@OCT** 與 **SeryogaSK@OCT**（[OCT](https://opencheattables.com/)）協助測試本工具。

---

## 使用 Claude Code 開發

本專案在 Anthropic 的 [Claude Code](https://claude.ai/code) 協助下開發。C++ DLL、C# Avalonia UI、建置腳本及文件均由開發者與 Claude Code 協作完成。本 repo 另附一個本機 LLM 輔助工具，任何 repo 的 Claude Code session 都能共用，詳見 [tools/llm/README.md](tools/llm/README.md)。

---

**授權條款**: [MIT](LICENSE) © 2026 bbfox0703
