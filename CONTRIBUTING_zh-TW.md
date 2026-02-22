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

AOB（Array of Bytes）Pattern 是定位引擎全域指標（GObjects、GNames、GWorld）的核心機制。目前 `dll/src/Signatures.h` 中有來自 **6 個來源共 51 組 Pattern**。

### 為什麼需要驗證

錯誤的 AOB Pattern 可能導致誤判 — 匹配到錯誤的記憶體位置，引起當機或亂碼。由於維護者不一定持有每款遊戲，因此需要充分的證據來確認所貢獻的 Pattern 是正確的。

### 需要提供的資訊

請開啟 Issue 並加上 `aob-pattern` 標籤，附上以下**所有**項目：

| 項目 | 說明 |
|------|------|
| **Pattern 位元組** | 十六進位字串，使用 `??` 作為萬用字元。範例：`48 8B 05 ?? ?? ?? ?? 48 8B 0C C8 48 8D 04 D1` |
| **目標** | 對應的全域指標：`GObjects`、`GNames` 或 `GWorld` |
| **解析方式** | `direct`（位址 = 匹配位置）、`rip-relative`（RIP + 偏移）或 `dereference`（讀取解析位址處的指標） |
| **RIP 偏移** | 若為 RIP 相對定址：從 Pattern 起始到 4 位元組位移值的偏移（例如 `48 8B 05 [xx xx xx xx]` 為 `3`） |
| **遊戲名稱** | Steam/商店頁面上顯示的完整名稱 |
| **UE 版本** | 來自 PE VERSIONINFO、本工具偵測結果、或 RE-UE4SS 設定 |
| **掃描日誌摘錄** | `UE5Dumper-scan-0.log` 中顯示 Pattern 匹配與驗證結果的相關區段 |
| **Object Tree 截圖** | UI 顯示正確物件名稱（非亂碼）的截圖 |

### 日誌檔位置

```
%LOCALAPPDATA%\UE5CEDumper\Logs\UE5Dumper-scan-0.log
```

### 掃描日誌範例

有效的 Pattern 貢獻應顯示類似以下的日誌輸出：

```
[INFO][SCAN] Pattern V_NEW matched at 0x7FF71A234560
[INFO][SCAN] Resolved via RIP: 0x7FF71B7A1820
[INFO][SCAN] ValidateGObjects: NumElements=483670, Layout A (Objects+0x00, Num+0x14)
[INFO][SCAN] GObjects confirmed at 0x7FF71B7A1820
```

### 驗證流程

1. **維護者審查**：檢查 Pattern 格式是否正確、解析邏輯是否合理。
2. **日誌驗證**：掃描日誌須顯示驗證成功（NumElements 在合理範圍、偵測到有效的 Layout）。
3. **視覺確認**：Object Tree 截圖須顯示可辨識的 UE 型別名稱（Package、Class、Object、BlueprintGeneratedClass 等），而非亂碼。
4. **第三方確認**（建議）：如有其他使用者能在同一款遊戲上確認 Pattern 有效，將以更高信心度接受。此項為強烈建議但非絕對必要，前提是日誌 + 截圖證據已足夠清楚。
5. **迴歸測試**：合併前確認新 Pattern 不會在現有測試遊戲上產生誤判。

### Pattern 風格指南

請遵循 `Signatures.h` 中的既有慣例：

```cpp
// V_NEW — 簡短描述此 Pattern 的來源
// Target: GObjects | GNames | GWorld
// Resolution: direct | rip(offset) | deref
// Tested on: 遊戲名稱 (UE X.XX)
{ "\x48\x8B\x05\x00\x00\x00\x00\x48\x8B\x0C\xC8",
  "xxxx???xxxx", 11, PatternSource::Community },
```

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
| DLL 掃描日誌 | `%LOCALAPPDATA%\UE5CEDumper\Logs\UE5Dumper-scan-0.log` |
| DLL Pipe 日誌 | `%LOCALAPPDATA%\UE5CEDumper\Logs\UE5Dumper-pipe-0.log` |
| UI 日誌 | `%APPDATA%\UE5CEDumper\Logs\UE5DumpUI-0.log` |
| 個別程序鏡像 | `%LOCALAPPDATA%\UE5CEDumper\Logs\<程序名稱>\UE5Dumper-scan-0.log` |

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
