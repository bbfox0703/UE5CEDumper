# 貢獻指南 — UE5CEDumper

感謝您對本專案的關注！本文件說明如何提交 AOB Pattern、回報偵測失敗、以及貢獻程式碼。

---

## 目錄

- [AOB Pattern 貢獻](#aob-pattern-貢獻)
- [問題回報（偵測 / 提取失敗）](#問題回報)
- [程式碼貢獻](#程式碼貢獻)
- [開發環境設定](#開發環境設定)

---

## AOB Pattern 貢獻

AOB（Array of Bytes）Pattern 是定位引擎全域指標（GObjects、GNames、GWorld）的核心機制。目前 `dll/src/Signatures.h` 中有來自 **14 個來源共 133 組 Pattern**，涵蓋 UE4.18 至 UE5.7+，已在 20 款以上遊戲中驗證。

### 最有幫助的方式：回報偵測失敗

如果你的遊戲未被偵測到，**最有幫助的做法**是開啟 Issue 並加上 `detection-failure` 標籤，附上完整的掃描日誌（詳見下方[問題回報](#問題回報)）。掃描日誌包含維護者分析失敗原因並建立新 Pattern 所需的所有診斷資料 — 你不需要自己逆向工程出 Pattern。

### 給逆向工程師：直接貢獻 Pattern

如果你有逆向工程經驗（IDA/Ghidra/x64dbg）且想直接貢獻 Pattern，請開啟 Issue 並加上 `aob-pattern` 標籤，附上：

| 項目 | 說明 |
|------|------|
| **Pattern 位元組** | 十六進位字串，使用 `??` 作為萬用字元（例如 `48 8B 05 ?? ?? ?? ?? 48 8B 0C C8 48 85 C9 74`） |
| **目標** | 對應的全域指標：`GObjects`、`GNames` 或 `GWorld` |
| **解析方式** | `rip-direct`、`rip-deref`、`rip-both`、`symbol-export`、`symbol-call-follow` 或 `call-follow` |
| **遊戲名稱 + UE 版本** | 完整名稱 + 精確版本（例如 `UE 5.04`） |
| **來源函式** | Pattern 來自哪個引擎函式（例如 `FUObjectArray::AllocateUObjectIndex`） |
| **掃描日誌 + Object Tree 截圖** | 證明 Pattern 能正確解析 |

請遵循 `Signatures.h` 中的既有慣例來撰寫 Pattern 格式與註冊巨集。

---

## 問題回報

若 UE5CEDumper 無法偵測遊戲或產生不正確的結果，請開啟 Issue 並加上 `detection-failure` 標籤，提供以下資訊。

### 必要資訊

| 項目 | 說明 |
|------|------|
| **遊戲名稱** | 完整名稱 + Steam/商店頁面連結 |
| **UE 版本** | 若已知（來自 RE-UE4SS、SteamDB 或其他來源）。「未知」亦可 |
| **掃描日誌** | 完整的 `UE5Dumper-scan-0.log`（以附件方式上傳） |
| **UI 日誌** | 若涉及 UI，完整的 `UE5DumpUI-0.log`（以附件方式上傳） |
| **截圖** | UI 顯示失敗狀態的截圖 |
| **什麼可用 / 什麼不可用** | 範例：「Object Tree 載入但名稱為亂碼」或「Pipe 連線失敗」 |
| **CE 版本** | 使用的 Cheat Engine 版本 |
| **RE-UE4SS 狀態** | RE-UE4SS 是否能在此遊戲上運作？若可以，使用哪個版本及是否有自訂設定？ |

### 日誌檔位置

| 日誌 | 路徑 |
|------|------|
| DLL 掃描日誌 | `%LOCALAPPDATA%\UE5CEDumper\Logs\<程序名稱>\scan-0.log` |
| DLL 偏移日誌 | `%LOCALAPPDATA%\UE5CEDumper\Logs\<程序名稱>\offsets-0.log` |
| DLL Pipe 日誌 | `%LOCALAPPDATA%\UE5CEDumper\Logs\<程序名稱>\pipe-0.log` |
| DLL Walk 日誌 | `%LOCALAPPDATA%\UE5CEDumper\Logs\<程序名稱>\walk-0.log` |
| UI 日誌 | `%LOCALAPPDATA%\UE5CEDumper\Logs\ui-0.log` |

### 失敗類別

為加速分類處理，請標明符合您狀況的類別：

| 類別 | 症狀 |
|------|------|
| **無法連線** | UI 無法連接 Pipe，或 DLL 掃描在 Pipe 啟動前中止 |
| **GObjects 未找到** | 掃描日誌顯示「GObjects not found」或所有 Pattern 均失敗 |
| **GNames 未找到** | 掃描日誌顯示「GNames not found」或所有驗證器均失敗 |
| **名稱亂碼** | Object Tree 載入但名稱包含 `????`、截斷文字或隨機字元 |
| **Object Tree 空白** | UI 連線成功，顯示物件計數 > 0，但樹狀結構為空 |
| **GWorld 失敗** | 「Start from GWorld」按鈕無顯示或出現錯誤訊息 |
| **注入當機** | DLL 注入後遊戲當機 |
| **UE 版本錯誤** | 偵測到的版本與實際引擎版本不符 |

### 最有幫助的資訊

**掃描日誌**是最有價值的單一資訊。它包含十六進位轉存、驗證結果及診斷資料，讓我們能在沒有該遊戲的情況下診斷問題。請務必附上完整的掃描日誌，而非僅摘錄片段。

---

## 程式碼貢獻

### Pull Request 流程

1. Fork 本倉庫，從 `dev` 分支建立功能分支。
2. 遵循既有的程式碼風格與慣例。
3. 確保 DLL 和 UI 均能成功建置（`build release`）。
4. 執行 UI 測試（`build test`）。
5. 若新增 AOB Pattern，請在 PR 描述中附上上述驗證證據。
6. 提交 PR 至 `dev` 分支。

### 程式碼風格

- **C++（DLL）**：C++23，MSVC。使用 `LOG_INFO`/`LOG_DEBUG`/`LOG_WARN` 巨集輸出日誌。所有記憶體讀取使用 `Mem::ReadSafe`（SEH 保護）。
- **C#（UI）**：.NET 10，Avalonia 11，CommunityToolkit.Mvvm。所有 I/O 必須為非同步。UI 字串置於 `Resources/Strings/en.axaml`。
- **註解與 UI 字串**：僅使用英文。
- **平台抽象**：任何作業系統相關呼叫必須透過 `Core/` 中的介面。`Core` 專案不得包含直接的平台特定程式碼。

### Commit 訊息

- 使用簡潔、描述性的 Commit 訊息。
- 在適用處引用 Issue 編號（例如：`Fix stride detection for UE4.18 (#42)`）。

---

## 開發環境設定

### 前置需求

- Visual Studio 2022+（v17+），含 C++ Desktop 工作負載
- .NET SDK 10.0
- CMake 3.25+
- Ninja（任意近期版本）
- Cheat Engine 7.5+（用於測試）

### 建置方式

```cmd
:: 完整建置（DLL + UI）
build release

:: 僅 DLL
build dll

:: 僅 UI
build ui

:: 執行測試
build test
```

### 測試

測試需要一個執行中的 UE4/UE5 遊戲。請參閱 `CLAUDE.md` 中的推薦測試遊戲列表及已知可用的設定組合。

---

## 有問題嗎？

歡迎在 [Discussions](../../discussions) 中提問，適用於非 Bug 回報或功能需求的一般問題。
