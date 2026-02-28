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

AOB（Array of Bytes）Pattern 是定位引擎全域指標（GObjects、GNames、GWorld）的核心機制。目前 `dll/src/Signatures.h` 中有來自 **13 個來源共 95 組以上的 Pattern**，包含 MSVC Symbol Export 及多種解析策略。

### 為什麼品質很重要

錯誤的 AOB Pattern 可能導致誤判 — 匹配到錯誤的記憶體位置，引起當機或亂碼。過短或過於通用的 Pattern 可能匹配到多個位置，使驗證變得不可靠。由於維護者不一定持有每款遊戲，因此需要充分的證據來確認所貢獻的 Pattern 不僅正確，而且品質足以在遊戲更新後仍然可靠。

### Pattern 品質指南

提交前，請依照以下標準評估您的 Pattern：

#### 1. UE 版本相容性

務必標明 Pattern 測試的 UE 版本。UE 內部結構在主要版本之間會改變 — 在 UE5.3 上有效的 Pattern 可能無法適用於 UE4.27 或 UE5.5。請包含：
- 精確的 UE 版本（例如 `UE 5.04`、`UE 4.27`、`UE 5.5`）
- 版本資訊來源：PE VERSIONINFO、本工具偵測結果、RE-UE4SS 設定、或 SteamDB

#### 2. 優先選擇核心引擎函式

最佳的 AOB Pattern 應針對**UE 核心引擎函式**，這些函式在不同遊戲與版本之間較為穩定。偏好來源：

| 優先度 | 函式 / 位置 | 原因 |
|--------|------------|------|
| **最佳** | `FUObjectArray` 建構式 / `UObject::StaticAllocateObject` | 核心分配 — 必定存在 |
| **最佳** | `FName::ToString`、`FName::FName()` 建構式 | FNamePool 存取 — 基礎功能 |
| **最佳** | `UGameEngine::Tick`、`UWorld::Tick` | GWorld 存取 — 標準引擎迴圈 |
| **良好** | `FGCObject` 相關函式 | GC 子系統中的 GObjects 引用 |
| **良好** | MSVC Mangled Symbol Export（`?GUObjectArray@@3V...`） | 精確匹配 — 無歧義 |
| **避免** | 遊戲特有的 Blueprint 或自訂程式碼 | 在不同遊戲上會失效 |
| **避免** | 僅在初始化時執行、可能被編譯器最佳化掉的程式碼 | 跨編譯器版本不可靠 |

#### 3. 匹配精度

Pattern 品質取決於**在目標模組中匹配的唯一性**：

| 匹配次數 | 評估 |
|----------|------|
| **1（唯一）** | 理想 — 無歧義 |
| **2-5** | 可接受，前提是驗證能確認正確的匹配 |
| **6+** | 過於通用 — 需增加更多上下文位元組或萬用字元來縮小範圍 |
| **0** | Pattern 未匹配 — 可能為版本特定，仍請附上版本資訊提交 |

可在掃描日誌中檢查匹配次數 — 尋找類似 `"matched at 0x..."` 的行。

#### 4. 指令上下文

- **使用完整指令**：包含完整的 x86-64 指令，而非任意位元組邊界
- **RIP 相對定址 Pattern**：`48 8B 05 ?? ?? ?? ??`（mov rax,[rip+disp32]）或 `48 8D 0D ?? ?? ?? ??`（lea rcx,[rip+disp32]）前綴是標準形式。加入周圍指令以提高唯一性
- **暫存器選擇很重要**：使用特定通用暫存器（GPR）如 `r8`、`r9`、`r10` 的 Pattern（透過 REX 前綴 `4C` vs `48`）能提供額外的辨別度
- **最小長度**：Pattern 應至少 10 位元組（理想為 15+），以減少誤判

#### 5. Symbol Export（AOB 的替代方案）

若遊戲執行檔匯出 MSVC Mangled Symbol（常見於非 Monolithic / 模組化的 UE 建置），這是最可靠的方法：

```cpp
// 直接變數匯出 — 位址即為全域變數
"?GUObjectArray@@3VFUObjectArray@@A"     // → GObjects
"?GWorld@@3VUWorldProxy@@A"              // → GWorld

// 函式匯出 — 掃描函式本體找 RIP 相對引用
"?ToString@FName@@QEBAXAEAVFString@@@Z"  // → GNames（透過 FNamePool 引用）
"??0FName@@QEAA@PEB_WW4EFindName@@@Z"   // → GNames（透過 FName 建構式）
```

Symbol Export 有**最高優先度（priority 0）**，因為它們是精確匹配，誤判風險為零。

### 需要提供的資訊

請開啟 Issue 並加上 `aob-pattern` 標籤，附上以下**所有**項目：

| 項目 | 說明 |
|------|------|
| **Pattern 位元組** | 十六進位字串，使用 `??` 作為萬用字元。範例：`48 8B 05 ?? ?? ?? ?? 48 8B 0C C8 48 85 C9 74` |
| **目標** | 對應的全域指標：`GObjects`、`GNames` 或 `GWorld` |
| **解析方式** | `rip-direct`、`rip-deref`、`rip-both`、`symbol-export`、`symbol-call-follow` 或 `call-follow` |
| **RIP 偏移** | 若為 RIP 相對定址：從 Pattern 起始到 RIP 指令的偏移，以及位移值前的 Opcode 長度 |
| **遊戲名稱** | Steam/商店頁面上顯示的完整名稱 |
| **UE 版本** | 精確版本（例如 `UE 5.04`）+ 判定方式（PE VERSIONINFO / 工具偵測 / RE-UE4SS / SteamDB） |
| **來源函式** | 此 Pattern 來自哪個引擎函式（例如 `FUObjectArray::AllocateUObjectIndex`、`FName::ToString`）。使用 IDA/Ghidra/x64dbg 辨識 |
| **匹配次數** | 此 Pattern 在遊戲模組中匹配了幾次（來自掃描日誌） |
| **掃描日誌摘錄** | 掃描日誌中顯示 Pattern 匹配與驗證結果的相關區段 |
| **Object Tree 截圖** | UI 顯示正確物件名稱（非亂碼）的截圖 |

### 日誌檔位置

```
%LOCALAPPDATA%\UE5CEDumper\Logs\<程序名稱>\scan-0.log
```

### 掃描日誌範例

有效的 Pattern 貢獻應顯示類似以下的日誌輸出：

```
[INFO] [SCAN] GOBJ_V_NEW: 1 match(es), best=0x7FF71B7A1820
[INFO] [SCAN] ValidateGObjects: NumElements=483670, Layout A
[INFO] [SCAN] GObjects confirmed at 0x7FF71B7A1820 via GOBJ_V_NEW
```

### 驗證流程

1. **維護者審查**：檢查 Pattern 格式、解析邏輯、指令邊界及匹配精度。
2. **日誌驗證**：掃描日誌須顯示驗證成功（NumElements 在合理範圍、偵測到有效的 Layout、合理的匹配次數）。
3. **視覺確認**：Object Tree 截圖須顯示可辨識的 UE 型別名稱（Package、Class、Object、BlueprintGeneratedClass 等），而非亂碼。
4. **唯一性檢查**：在單一模組中匹配 6 次以上的 Pattern 將被退回，除非增加額外的上下文位元組以減少匹配數。
5. **第三方確認**（建議）：如有其他使用者能在同一款遊戲上確認 Pattern 有效，將以更高信心度接受。
6. **迴歸測試**：合併前確認新 Pattern 不會在現有測試遊戲上產生誤判。

### Pattern 風格指南

請遵循 `Signatures.h` 中的既有慣例：

```cpp
// AOB Pattern 定義
constexpr const char* AOB_GOBJECTS_VNEW = "48 8B 05 ?? ?? ?? ?? 48 8B 0C C8 48 85 C9 74";

// 在 GOBJECTS_PATTERNS[] 中使用 SIG_RIP 巨集註冊：
//   SIG_RIP(id, pattern, target, instrOffset, opcodeLen, totalLen, adjustment, priority, source, notes)
SIG_RIP("GOBJ_VNEW", AOB_GOBJECTS_VNEW, AobTarget::GObjects,
        0, 3, 7, 0, 50, "Community", "FUObjectArray access in AllocateUObjectIndex (UE 5.04)"),
```

Symbol Export：
```cpp
SIG_EXPORT("GWLD_EXP", EXPORT_GWORLD, AobTarget::GWorld, 0, "UWorldProxy symbol"),
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
