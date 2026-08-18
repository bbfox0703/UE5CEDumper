# 待驗證項目 —— 操作清單（只驗證，不改程式碼）

> **這份寫的是「怎麼操作」，不是「為什麼」。** 每一項只有兩件事：**做什麼**、**預期看到什麼**。
> 成因、來龍去脈、哪個 build 改了什麼原理，全部在英文正本
> [todo.md](todo.md) 的 `## Pending live-game verification` —— 需要證據時去那裡查，
> 用項目編號（`ST1`、`G10`、`B4`…）grep 就找得到。
>
> **英文版是唯一正本。** 事實有出入以 todo.md 為準，要改也先改那邊。
> ⚠ **這份不是 todo.md 的翻譯，不要把它翻回去。** 上一版就是逐句翻譯，結果過期了 430 個 build。

> 每個 marker 落在哪個檔案、怎麼 grep，在
> [log-verification-checklist.md](log-verification-checklist.md)。

-----

## 怎麼用這份清單

**按「要準備多少東西」分成第 0～5 步。** 從第 0 步往下做，愈後面成本愈高。
項目編號保留原樣，所以在對話裡講「B4」「G10」還是同一個東西。

| 分組 | 項目數 | 需要準備 |
|---|---|---|
| **第 1 步 — 只開 UE5DumpUI** | 2 | UE5DumpUI |
| **第 2 步 — 要注入一個執行中的遊戲** | 19 | 一款執行中的 UE 遊戲 + 注入 |
| **第 3 步 — 遊戲 ＋ Cheat Engine** | 9 | 遊戲 + Cheat Engine |
| **第 4 步 — 需要特定條件的遊戲** | 15 | 符合特定條件的遊戲 |
| **第 5 步 — 目前沒有可測的環境** | 2 | 目前沒有 |
| **合計** | **47** | |

> 這張表是**數出來的**，不要手改：`grep -c '^### ' docs/pending-verification_zh-TW.md` 再扣掉
> 「怎麼用這份清單」底下的兩個小節。第 0 步已經整組做完，所以那一列不見了。

### ⚠ 四條會害人記錯結果的鐵則

1. **PASS 條件是「某個東西不出現」時，一定要跑反方向那一次。**
   「不存在」是全世界最容易誤打誤撞產生的結果 —— 沒跑對照組，你證明的是「沒測到」而不是「通過」。
2. **空的 grep 不是證據。** 先確認指令真的送出去了（去 `ui-pipe-0.log` 找那個 cmd），
   再確認 DLL 已經 flush（看 log 檔大小還在不在長）。
3. **拿修正時用的那份清單去驗那個修正，等於沒驗。**
   清單型的修正要拿「世界」去驗，不是拿它自己的清單。
4. **閘門答對 ≠ 使用者看得到。** PASS 條件只要是「畫面上會出現某串字」，就一定要真的去看那串字。

### 開 log 前要知道的三件事

- **沒有 log level，什麼都不會被過濾** —— `[DEBUG]` 行也算數。
- **`SEETHRU` / `Grausam` / `SENSE` / `PROXY` 會 fall through 到 `init-0.log`**，不在 `walk` / `pipe`。
- **一律用「格式字串」grep，不要用行號** —— 2026-08 查過一次，Genau 的行號全部差 12～14 行，字串卻完全正確。

- **log 位置**：`%LOCALAPPDATA%\UE5CEDumper\Logs\<行程名>\`。
  封存檔是 LZX 就地壓縮的，檔名不變，`rg` / 記事本照樣讀得到。

-----

> ## ✅ 第 0 步 已全部完成 (2026-08-17)
>
> `U1/M1/M3`、`D1/D3`、`AA38` 三項都已結案並 commit，證據在
> [todo.md](todo.md)（grep 項目編號）。**不用開遊戲**的項目目前歸零 ——
> 現在最便宜的一批是第 1 步。

-----

## 第 1 步 — 只開 UE5DumpUI

不用注入任何遊戲。

### ⬜ AE4 / AE5 / AE6 / AE7 —— Proxy Deploy 面板的並行防護與選項保持

*優先度 **高***

| # | 做什麼 | 預期 |
|---|---|---|
| 1 | 開 UE5DumpUI → Proxy Deploy 分頁，先 Scan Steam 掃出遊戲，勾選兩個以上，按 Deploy 後立刻按 Remove。 | 第二個動作被拒絕，訊息點名正在跑的工作（例如「Busy: Deploy is running…」）。<br>⚠ 出現舊訊息「Wait for scan to finish」＝失敗（當下根本沒有掃描在跑）。 |
| 2 | 分別執行 Deploy / Remove / Refresh / Update All，觀察面板進度條。 | 四個動作進行中進度條都會跑動，結束後停止。 |
| 3 | 回歸：依序跑 Scan Steam、Scan drives（中途按 Cancel）、Find leftovers（中途按 Cancel）。 | 三種掃描都能正常執行並各自被自己的 Cancel 中止。<br>⚠ 要看的失敗是「另一張卡片上冒出不該亮的 Cancel」。 |
| 4 | 回歸：Find leftovers → 勾一筆 → Delete；刪除進行中嘗試啟動任一掃描，反向再試一次。 | 刪除中掃描被擋、掃描中刪除也被擋，兩邊互斥。 |
| 5 | 快速連續點選 proxy 型別 radio：version → dinput8 → dxgi。 | 表格的 Status / Installed Version 欄最後顯示的型別與 radio 目前選的一致。 |
| 6 | 來源切到 Scan drives，在磁碟清單還在載入時切回 Steam 再切回 Drives，然後勾選幾個磁碟。 | 勾選的磁碟維持勾選，不會被清空。 |

### ⬜ AC1 —— Force Overwrite 不得覆蓋他人的 DLL

*build 3191 · 優先度 **高***

| # | 做什麼 | 預期 |
|---|---|---|
| 1 | 前置：把任一非本專案的 DLL 複製到某遊戲的 Binaries\Win64，改名為 dxgi.dll（只要不含我們的 ProductName 即可），驗證完刪除。放好後在 Proxy Deploy 面板按 Refresh。 | 該列顯示 `Other proxy: <name>`<br>⚠ 全程不需開遊戲，只需 UE5DumpUI |
| 2 | 只勾 Force Overwrite（不勾 Replace other tools' DLLs）→ Deploy，然後檢查那個 foreign DLL 的位元組大小與版本 | 部署被拒絕，該列仍標示原擁有者，且檔案大小/版本完全未變<br>⚠ 只看到「refused」訊息不算通過，一定要實際比對檔案未被寫入 |
| 3 | 兩個核取方塊都勾 → Deploy，再看 proxy log | 部署成功，且 log 出現 `Replacing another program's dxgi.dll (…)` 的 warn 行，內容有寫出被覆蓋的舊 ProductName |
| 4 | 完全關閉並重新啟動 App，看兩個核取方塊的狀態 | Force Overwrite 仍為勾選，「Replace other tools' DLLs」回到 OFF<br>⚠ 兩個都回到勾選 = 修正失效，這步是整批的重點 |
| 5 | 在已部署同版本我方 proxy 的遊戲上，只勾 Force Overwrite → Deploy | 會重新部署，不出現「already current」跳過 |
| 6 | 對含 foreign DLL 的遊戲執行 Update All | 該遊戲被略過，行為與以前相同 |

-----

## 第 2 步 — 要注入一個執行中的遊戲

任何一款 UE 遊戲都可以。

### ⬜ A6 —— Force 是否對子類別一併生效

*build 3036 · 優先度 **高***

| # | 做什麼 | 預期 |
|---|---|---|
| 1 | Property Search 搜尋一個基底類別上的欄位（挑有「inherited by N」標記的列，例如 bCanBeDamaged @ Actor）→ 右鍵 → Force | 顯示非零的 held 數量<br>⚠ 若仍出現「0 live instances of Actor … — nothing held」即為失敗，直接停止 |
| 2 | 看 Property Search 的「Forced fields (N held)」狀態列 | 廣泛基底類別應為數百筆；若達上限要顯示「cap reached, more exist unheld」，而非只寫「on 256 instance(s)」 |
| 3 | 對一個有同字首兄弟類別的類別下 Force（如 Enemy vs EnemyProjectile、或任一 Foo / FooComponent 組合），檢查 ForcedFields 狀態列與 DLL log 的 FindInstancesDerivedFrom base=… 行 | 不相關的同字首類別「沒有」被 hold |
| 4 | 回歸：Teleport 分頁 → Stealth card → Detect → Hold @0 → Reset | Hold 回報非零數量，Reset 後數值回復 |
| 5 | 回歸：對基底類別 Force 一個 bool 後執行 reset_all_fields，再觀察後續新生成的物件 | 新生成物件不會仍帶著被強制的值（表示沒有寫到 CDO） |

### ⬜ AD4 —— God Mode 徽章要說明原因而非只有開關

*build 3203 · 優先度 **高***

| # | 做什麼 | 預期 |
|---|---|---|
| 1 | 遊戲停在選單／無 pawn 狀態時連線，按 ↻ | 徽章顯示 `Unknown` |
| 2 | 仍在無 pawn 狀態下按 Force ON | 徽章顯示 `ON (pending)`（不是 `Unknown`）<br>⚠ 若顯示 `ON` 而非 `ON (pending)`，是已知的 Solitar 落差，記在 Solitar 名下，不要當成 badge 的 bug |
| 3 | 進入遊戲讓 pawn 生成，按 ↻ | 徽章顯示 `ON` |
| 4 | 讓遊戲以傷害重置該旗標，連續按 ↻ 數次 | 多數為 `ON`，偶爾出現 `ON (contested)`<br>⚠ `ON (contested)` 很少出現是正常的，不代表沒驗到 |
| 5 | 按 Force OFF 後 ↻；再到一個 pawn 本身就免疫、且未強制任何東西的遊戲上觀察徽章 | 前者為 `OFF`；後者為 `ON (not held)` |
| 6 | Force ON 後關閉 UI，重開並重新連線，不要按 ↻；同時盯著狀態列 | 徽章直接是 `ON`；狀態列全程維持 `Connected`，按鈕不閃爍 |

### ⬜ A3 —— 每個 class 的多個 FVector 都要能掃到

*build 3168 · 優先度 **高***

| # | 做什麼 | 預期 |
|---|---|---|
| 1 | Value Search 選 Float（或 NumericAll），任意值，對一個有 pawn/actor 的 class 執行掃描 | 結果中出現欄位名結尾為 `.Velocity` / `.Scale3D` 的列，不再只有 `.Location` |
| 2 | 反向對照：同樣條件但 data type 改成 FVector 再掃一次 | 結果與 3168 之前相同、沒有變化<br>⚠ 不可拿 FVector 掃描當通過依據；這步只是對照，有變化反而代表改到不該改的地方 |
| 3 | 對同一欄位改用 Group Scan 或 Property Search 的 Deep 模式 | 一樣找得到（這條路徑在 3168 之前就找得到） |
| 4 | grep `scan-*.log` 搜尋 `hit the 4000 scan-field cap` | 一般 class 上不出現這行<br>⚠ 若經常出現，代表 cap 值設錯，要回報 |

### ⬜ Skia ABI (SkiaSharp 3.119.4 / HarfBuzzSharp 8.3.1.3) —— UI 降版對齊後不再崩潰且畫面正常

*build 3127 · 優先度 **高***

| # | 做什麼 | 預期 |
|---|---|---|
| 1 | 用 build ≥3127 的 UI 逐一切換每個分頁（Object Tree / Class Struct / Live Walker / Property Search / Value Search / Teleport / Snapshot…），並找一段繁體中文字串看渲染。 | 無缺字、字距正常、文字未被裁切、DataGrid 每列都正常繪出。 |
| 2 | 注入 Elliot → Live Walker → GameState → 開 AOB、depth 4 → Copy CE XML，之後讓 UI 繼續開著數分鐘。 | UI 不崩潰（舊版是 Copy 後約 2.3 秒 0xC0000374，且 14 分鐘內崩兩次）。<br>⚠ 單次乾淨 session 不算通過；要累積數個 session 的日常使用才可結案。 |
| 3 | 若發生崩潰，取 WER dump，並用 VC\Tools\Llvm\x64\bin 下的 x64 llvm-symbolizer 符號化整條 stack。 | 崩潰即判定 FAIL；記錄 faulting module 是否仍為 libSkiaSharp。<br>⚠ 遞迴搜尋會先找到 ARM64 版 llvm-symbolizer，那個跑不起來。 |
| 4 | 評估效能或記憶體前先關掉 page heap：reg delete "HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options\UE5DumpUI.exe" /f | UI 速度與記憶體回到正常水準。 |

### ⬜ A5 / V6 / AE9 / U8 —— 四個一開遊戲就能看的面板行為

*build 3016-3031 · 優先度 **高***

| # | 做什麼 | 預期 |
|---|---|---|
| 1 | 注入遊戲後開 Property Search，搜一個遊戲中會變動的欄位（例如 Health），盯著 Preview 欄。 | Preview 跟著遊戲內實際數值變動；沒有活體實例的列顯示「… (CDO default)」。<br>⚠ 兩種列各要看到一次，只看到一種等於只測了一半。 |
| 2 | Live Walker 輸入欄位搜尋關鍵字 → 按 Refresh，並讓 auto-refresh 再跑幾拍。 | 高亮保留、↑/↓ 步進仍落在高亮列、表格不跳回最上方。 |
| 3 | Value Search → First Scan → 用 Value 排序 → 按 New Scan。 | 排序選單回到「Scan order」；再選一次「Value」會真的重新排序。 |
| 4 | Live Walker 找一個值帶數字尾碼的 NameProperty（Slot_1、Slot_2），同時用 Value Search 看同一位址。 | 面板與 Value Search 顯示同一組 8 bytes、尾碼數字一致。<br>⚠ 物件／實例「名稱」被截斷是另一條未修的線，不要當成這項失敗。 |

### ⬜ AE2 / AE3 —— Class/Struct 面板在快速切換選取下的同步

*優先度 **中***

| # | 做什麼 | 預期 |
|---|---|---|
| 1 | 回歸：在 Object Tree 隨機點選數個節點，包含 instance 與 class-like 列（`*_C`、`ScriptStruct`、`Function`）。 | 每次點擊 Class/Struct 標頭都跟著換，欄位有載入內容。 |
| 2 | 用關鍵字過濾樹狀圖，使 instance 與 class-like 列**交錯**出現，按住 ↓ 快速捲動後放開在一個 class-like 列上。 | Class/Struct 標頭與反白的那一列相符。<br>⚠ 清單若只有單一種類（全 instance 或全 class-like）跑再多次都證明不了；要記錄用的過濾字串。 |
| 3 | 在同一次快速捲動中觀察載入指示器（spinner）。 | 面板穩定後 spinner 不會卡著不消失；載入還在跑時也不會提前閃掉。 |
| 4 | 先成功載入某節點，再讓它的 class 位址失效（例如切換關卡／卸載後重新選取同一列），然後**再點一次同一列**。 | 出現錯誤訊息行，且再次點擊會重新嘗試載入（不是靜默忽略、停留在舊 class）。 |
| 5 | 選取樹狀節點 P → 用任一 handoff 把別的 class 推進 Class/Struct（Interesting Funcs / Property Search / Dump Explorer）→ 再點一次節點 P。 | 面板重新載入 P，而不是停留在被推進來的 class。 |
| 6 | 在有選取節點的狀態下於樹狀過濾框連續打字。 | 不會重複重走 class，面板也不會被清空。 |

### ⬜ G2 —— 版本掃描加速後結果仍正確

*優先度 **中** · 原步驟 3、4 已於 2026-08-18 驗畢並刪除*

| # | 做什麼 | 預期 |
|---|---|---|
| 1 | 把 `DetectVersion: PE resource failed, falling back to memory string scan` 到下一條 `SCAN:Ver` 之間的時間拆開量：加一條分隔 log，或改用一款 pre-UE4 檢查會提早結束的遊戲重測。同時記下遊戲名與 exe 位元組大小。 | 單獨的版本字串掃描本身在 1 秒以內。<br>⚠ 未拆分前不可記「G2 比宣稱慢」——目前量到的 2.4 s 內含 `CountPreUE4Markers` 另一次全檔掃描。 |
| 2 | ⛔ **本機做不到，先別開遊戲。** 全機 **71** 個 UE 執行檔（14 個已安裝 + 57 個 `D:\UE_Analyze_data` 備份／自建樣本）已用 `py tools/verify/pe_version_probe.py --scan <目錄>` 離線掃過：只有 3 個會落到記憶體字串那一段，且**全部是 UE4 世代**（DQ7R、OCTOPATH、Elliot）。原列候選 **TQ2 / Solarpunk / Manor Lords / ES2 / STVoyager 全部已被推翻** —— 它們的 `ProductVersion` 都是真的 `5.x`，會停在 Tier 0，理由跟 Lushfoil 一模一樣。 | 要收掉這項需要**一款新的、版本資源被拔掉或寫成產品版號（1.x）的 UE5 遊戲**。⚠ **裝之前先用 `pe_version_probe.py` 篩**，不要靠引擎版本猜。<br>⚠ Solarpunk 的字串 `ProductVersion` 是 `UE5-CL-0`，看起來像無法辨識，但 `VS_FIXEDFILEINFO` 是 5.7.1.0，第一關就結束了 —— 字串要到第三關才會被看。 |

### 🟡 W2 / W3 —— SDK header 繼承邊界與位元欄位（**只剩 UI 匯出這一步**）

*build 2842 · 優先度 **中** · 步驟 1-4 已於 2026-08-17 用 pipe 驗過並 commit*

| # | 做什麼 | 預期 |
|---|---|---|
| 5 | 在 UI 匯出該類別（`DumperTestActor`）的 SDK header | struct 從 super 的大小（**672**）起算、不重覆宣告 `AActor` 的屬性；含 packed bool 的類別發出 `uint8_t bX : 1`，其位元組數與到下一欄位的間隙相符<br>⚠ 目前卡在 UE5DumpUI 無法授權給 computer-use（散裝 exe、登錄檔沒有安裝項目） |

### ⬜ W1 / W7 —— 匯出的 .usmap 能被真實解析器讀出

*build 2853 · 優先度 **中***

| # | 做什麼 | 預期 |
|---|---|---|
| 1 | 連上任一遊戲，Export → USMAP 匯出檔案 | 產出 .usmap 檔 |
| 2 | 在 FModel 用 Directory selector → Mappings file 載入該檔（或直接跑 CUE4Parse 的 UsmapParser） | 成功載入 |
| 3 | 查 AActor 的 bHidden / InitialLifeSpan | 屬性名稱與型別都正確列出<br>⚠ 「沒有報錯」不算通過；空表或亂碼視為失敗 |
| 4 | 順便查一個 Blueprint 類別（*_C） | 查不到是預期的（W8 未修，*_C 被過濾），不要當成解析失敗 |

### ⬜ G11 —— 版本偵測 Tier 2 上線後結果不變

*build 3112 · 優先度 **中***

| # | 做什麼 | 預期 |
|---|---|---|
| 1 | 挑一款尚未記錄的遊戲，換到 build ≥3112 前先記下 ueVersion / versionDetected / lowConfidence，換版後再看一次。 | 三個值完全相同；有任何變動就回報遊戲與前後值。<br>⚠ 已完成 Elliot 與 DragonSword Awakening，至少要再補一款。 |
| 2 | 注入 Avowed（packed 標題），比對偵測到的版本。 | 與舊 build 相同。 |
| 3 | grep scan-0.log 的 DetectVersion: Tier 2 Release prefix -> NNN。 | 若出現，記錄遊戲名稱、版本，以及是否與該遊戲實際版本相符。<br>⚠ VERSIONINFO 完整的遊戲會停在 PE VERSIONINFO 那行、根本不進 tier ladder；要用 version resource 被 strip 的標題（Elliot）。 |
| 4 | 用先前會回報 Tier 3 (low confidence) 的遊戲重跑一次。 | 回報的版本與先前相同。 |

### ⬜ V7 / AF4 / AB6 —— 失敗要看得見、切頁後還能捲動、排序要跟畫面一致

*build 3016-3031 · 優先度 **中***

| # | 做什麼 | 預期 |
|---|---|---|
| 1 | Live Walker 開一個物件 → 在遊戲中讓它被銷毀/卸載 → 按 Refresh。 | 狀態列下方出現鮭魚紅錯誤行（約 10 秒 timeout 後）。 |
| 2 | Live Walker 開物件 → 切到別的 tab → 切回來 → 使用 🌍 Locate in GWorld、書籤還原、或 ↑/↓ 比對步進。 | 表格仍會捲動並定位到目標列。<br>⚠ 壞掉時不會跳錯，只是按鈕按了沒反應——要看畫面有沒有捲動。 |
| 3 | Group Scan 下一個會讓單一 slot 保留多個 leaf 的 filter，然後依 Value 欄排序。 | 排序結果與畫面上 Value 欄顯示的值一致。 |

### 🟡 D2 —— Group Scan 掃得到物件自己的 scalar 欄位（**只剩步驟 4：UI clamp**）

*build 2680 / 2690 · 步驟 1-3 已於 2026-08-17 用 pipe 驗過並 commit*

| # | 做什麼 | 預期 |
|---|---|---|
| 4 | 把 Leaves/slot: NumericUpDown 調離預設值再掃一次 | 數值被 clamp 在 8–4096<br>⚠ 卡在 UE5DumpUI 無法授權；封包半部已驗（未調動時不帶 per_slot_cap） |

⚠ **清單原本寫「grep pipe-0.log」是錯的** —— `RefineGroup cand … leaves entered=` 掛在 `[SCAN:grp]`，
會寫進 **`scan-0.log`**。

### ⬜ D2（顯示配對） —— Group Scan 列上顯示的是真正的配對

*build 2715 / 2719 · 優先度 **中***

| # | 做什麼 | 預期 |
|---|---|---|
| 1 | 不下任何 filter，直接看 Group 結果的 master row | 預設顯示的一對值優先為非 0（不會是 PrimaryActorTick.TickInterval=0, InitialLifeSpan=0），且每個 slot 後面帶 (+N) 的 match count |
| 2 | Filter 輸入 tickcount frozenint（空白 = AND），再把兩個字順序對調重試 | 該列變成 TickCount=NN (+1), FrozenInt=424242 (+35)；字序對調結果相同 |
| 3 | 展開該列按 All fields，再按一次收合 | 列出該 slot 保留的所有 leaf，且物件自己的欄位排在最前面（FrozenInt 不必往下捲）；第二次按會收合，重開會重新查詢<br>⚠ 某個值「沒出現在列上」不代表沒 match — 先看 (+N) 與 All fields 再下結論 |
| 4 | 對 All fields 裡任一 leaf 依序按 Live / Addr / Pivot / Locate | 四個都能正常跳轉；deep 或 Snapshot 來源的列若取不到 leaf 位址則整個省略 → 0x… 箭頭，而不是印 → 0x0 或物件 base |

### ⬜ B10 —— WalkClassEx memo 的耗時與欄位正確性

*優先度 **中***

| # | 做什麼 | 預期 |
|---|---|---|
| 1 | 對同一款遊戲、同一個目標做一次 Snapshot capture。 | 擷取完成。 |
| 2 | grep `%LOCALAPPDATA%\UE5CEDumper\Logs\UE5DumpUI\view-0.log`（或遊戲資料夾的 `ui-view-*.log`）的 `PERF Snapshot capture`。 | 有 `wall … ms`。目前唯一留存的數字是 5,256.2 ms（2026-08-04，修正後），沒有 pre-2596 可比就把本次記成新基準，下次同一遊戲同一擷取再比。<br>⚠ 這條在 UI 端 view-0.log，不在 pipe-0.log。 |
| 3 | 打開任一含 struct / enum / bool 欄位的物件 property grid。 | struct 型別、enum 名稱、bool mask 三欄都有值。FAIL = 這些欄位變空白，或並行掃描時當掉。 |

### ⬜ B19 —— log 保留掃描遇到鎖住的檔案不會整批放棄

*優先度 **中***

| # | 做什麼 | 預期 |
|---|---|---|
| 1 | 用一個會持續持有檔案的程式打開 `%LOCALAPPDATA%\UE5CEDumper\Logs\<proc>\` 底下任一封存 log。 | 該檔案處於被開啟鎖定狀態。 |
| 2 | 把「同一個資料夾內另一個」封存 log 的修改時間往回改成 21 天以前。 | 資料夾內同時存在一個被鎖住的檔和一個超齡的檔。 |
| 3 | 啟動有注入 DLL 的遊戲。 | 被回溯日期的檔案已被刪除、被鎖住的檔案還在。FAIL = 兩個都還在（掃描在鎖住的檔案處中止）。 |

### ⬜ Dump Explorer 跨遊戲身分閘 (2)(3) —— 載入別款遊戲的 dump 必須被拒絕

*優先度 **中***

| # | 做什麼 | 預期 |
|---|---|---|
| 1 | 用遊戲 X 匯出一份 Dump All (.jsonl)。 | 檔案產生。 |
| 2 | 改連「另一款」遊戲 Y，載入 X 的 .jsonl 並按 Re-check。 | 比對被拒絕、狀態列同時寫出 X 與 Y 的 module 名、所有列都是未比對、Jump 沒有東西可跳；log 出現 `DumpExplorer live match refused: dump module '…' != live module '…'`。 |
| 3 | （機會性，等 X 真的更新版本後）連上 X，載入更新前的舊 dump。 | 仍然比對成功，但帶 "Different build — offsets may have moved" 註記。 |

### 🟡 G8 / G9 —— 版本偵測改分層規則後不變（**只剩步驟 1：需要 Elliot**）

*優先度 **低** · 步驟 2 已於 2026-08-17 用 DumperTest 冷掃驗過並 commit*

| # | 做什麼 | 預期 |
|---|---|---|
| 1 | 注入 **Elliot**（version resource 被 strip 的標題），掃描後 grep `scan-0.log` 的 `DetectVersion: Tier 1` | 出現 `DetectVersion: Tier 1 (ascii\|utf16) '++UEx+Release-N.N' -> NNN`<br>⚠ **不要用 DumperTest**：實測它停在 `PE VERSIONINFO -> UE 5.4 -> 504`，結構上進不了 tier ladder |

### ⬜ AF6 / AE8 —— 兩個順手檢查：拒絕要出聲、被拒的掃描不計數

*build 3016-3031 · 優先度 **低***

| # | 做什麼 | 預期 |
|---|---|---|
| 1 | 在 Property Search 的 Force 輸入框打一個超出範圍的巨大整數並送出。 | 跳出明確的拒絕訊息並指出實際會用的替代值；不能是靜默無反應。 |
| 2 | 故意觸發一次會被拒絕的 scan 點擊，然後打開 diagnostics 的量測清單。 | 該次被拒的點擊不出現在量測清單裡。<br>⚠ 同批的 AF1 需要格式異常的 UEnum，無法隨需重現，不列入本清單。 |

### ⬜ Genau RIP decode (b2544) —— RIP 解碼修正沒有改動解出的位址

*優先度 **低***

| # | 做什麼 | 預期 |
|---|---|---|
| 1 | 同一款遊戲，分別用修正前與修正後的 DLL 各注入一次，各留一份 `scan-0.log`。 | 兩份 log 都跑完整個 FindAll。 |
| 2 | 比對兩份 log 的 candidate / probe 計數，以及 GObjects / GNames / GWorld 最終解出的位址。 | 計數下降（這是收益），而三個位址逐 byte 完全相同（這才是驗收標準）。位址有變就是 regression。<br>⚠ 不能用 sweep.sh 的 pattern diff 判定：它會跳過 Symbol*/CallFollow 簽章，乾淨的 diff 只代表「沒測到」。 |

-----

## 第 3 步 — 遊戲 ＋ Cheat Engine

還要開 CE 並載入 .CT。

### ⬜ AB1 / AB2 —— plugin 的 Inject & Connect 路徑（**前 3 步已完成，只剩這 3 步**）

*build 2913 / 2932 · 優先度 **高***

| # | 做什麼 | 預期 |
|---|---|---|
| 1 | plugin 啟用狀態下，對一個執行中的遊戲用選單「UE5CEDumper: Inject & Connect」。 | 選單立刻返回、對話框說掃描已在背景開始；CE 視窗不因掃描凍結；DLL 有注入、pipe 有開、CE Lua mailbox 可用；幾秒後遊戲不崩潰。<br>⚠ 對話框說成功就必須真的通得了 pipe；說注入失敗就必須真的沒有模組被載入。 |
| 2 | 到 CE Settings 勾選 cbInjectDLLWithAPC，重複上一步。 | 同樣不崩潰（這是修正前最容易炸的路徑）。 |
| 3 | 反向對照：對一個資料夾名稱含「Cheat Engine」的遊戲注入。 | 該遊戲仍然啟動 poller，功能正常。 |

### ⬜ U16 —— 大型 enum 的成員清單（**U4 / U6 / F3 已完成，只剩這一步**）

*優先度 **中** · 需要：有 `EPhysicalSurface` 規模（數十個成員）enum 欄位的遊戲*

| # | 做什麼 | 預期 |
|---|---|---|
| 1 | 開啟含大型 enum 欄位的 class，把該列推到 CE，展開 CE 的 DropDownList。 | 成員完整，沒有缺尾。<br>⚠ 已量過的最大表只有 26 個成員，所以「大型」這一半還沒真的壓到。 |
| 2 | grep `walk-0.log` 的 `ResolveEnumValue: UEnum`。 | `read N of M` 中 N 等於 M；出現任何 `GetEnumEntries: ... truncated read` 就是真的有問題，要記錄下來。 |

### ⬜ AA2 / AA3 —— 凍結能撐過死亡/重生並在失聯時自行停手

*build 2926 · 優先度 **中***

| # | 做什麼 | 預期 |
|---|---|---|
| 1 | 先做反向對照：注入舊版 DLL，配上新的 helper，啟動凍結。 | 必須被拒絕並顯示「the DLL is older than this script」。<br>⚠ 若照跑不誤，代表 contract 檢查沒生效，以下步驟全部無意義。 |
| 2 | 換新 DLL，對一個有大量存活實例的類別（敵人、拾取物）啟動 class-wide 凍結。 | 數值真的被鎖住。<br>⚠ 被 guard 靜默拒絕的樣子跟「凍結沒作用」一模一樣，一定要確認值有 hold 住。 |
| 3 | 檢查 init-0.log 的 LIST_INSTANCES ... classWitness=0x... 這行。 | witness 非 0；為 0 表示 guard 退回舊路徑、修正沒生效。 |
| 4 | 維持凍結，製造 churn：把凍結中的 actor 打死重生，或跨越 level streaming 邊界。 | 約一次 rescan（~5 秒）內重新接上；且沒有任何不相干物件的欄位被改動。 |
| 5 | AA3：凍結執行中把 DLL 卸載/重新注入，讓 rescan 永久失敗。 | ~15 秒內 Lua console 印出一次「... consecutive rescans failed -- freeze STOPPED writing」，之後不再寫入。 |

### ⬜ G10 / MA1 —— Hint 快取與 AOB 掃描取消守衛

*優先度 **中***

| # | 做什麼 | 預期 |
|---|---|---|
| 1 | 在有 hint 快取的遊戲上暖啟動掃描，於 `scan-0.log` 搜尋 `Hint MISS`，看該行括號內的 match 數。 | 顯示真實比對數（`(N matches, none validated; …)`）；對冷掃描曾記錄數百筆的 pattern，絕不可寫成 `1 match`。<br>⚠ 沒有 `Hint MISS` 行就是這步無法判定，不算通過；需先製造一次 hint 失效（例如換 build 或改動 PE）。 |
| 2 | 在一款沒有 hint 的冷掃描開始約 2 秒後，把 CE 裡的 script 取消勾選。 | `scan-0.log` 在 ~1 秒內出現 `AOB scan CANCELLED after N/M batches`，以及 `FindAll: scan was CANCELLED — NOT writing the hint cache`。 |
| 3 | 承上那次被取消的掃描後，diff `%LOCALAPPDATA%\UE5CEDumper\UE5CEDumper.{Machine}.json`。 | 該 PE hash 的項目完全沒有變動。<br>⚠ 三個守衛必須分開各驗一次，不要一次跑完就一起判定。 |
| 4 | 在**同一個** process 內重新勾選啟用，再跑一次掃描。 | 跑完整的重新掃描，而不是被 `UE5_Init` latch 直接短路跳過。 |
| 5 | 在同一 process 內鑽進一個 `MulticastSparseDelegateProperty`。 | `FindSparseDelegateStorage: Scanning` 第二次出現，而不是被 latch 住直接回 0。 |
| 6 | 連上 UI → 指令進行中把 UI 斷線 → 重新連線 → 重跑一次完整掃描。 | 掃描正常完成、有寫入 hint 快取，且日誌中**沒有** `CANCELLED` 行。 |

### ⬜ ST1 —— 自家直接呼叫要走 trampoline 不進自家 hook

*build 3205 · 優先度 **中***

| # | 做什麼 | 預期 |
|---|---|---|
| 1 | 連線後在 Pointers 分頁執行 KismetMathLibrary 自我測試（directCall: true），然後看 pipe log | 出現 `via trampoline — not re-entering our hook`，而不是舊的 `(caller-asserted safe)`<br>⚠ grep 一律用格式字串，不要用行號 |
| 2 | 執行 get_pointers 記下 hook_fire_count，重跑上一步後再讀一次 | 數字不因我們自己的呼叫而增加 |
| 3 | 把 invoke timeout 設短，在暫停／選單狀態的遊戲上觸發一個 game-thread invoke 讓它逾時並留在佇列；接著從 CE 觸發一次 static-native invoke | 那個請求仍然留在佇列中，沒有被執行掉<br>⚠ 這步是整批的關鍵，需要 Cheat Engine |
| 4 | 恢復遊戲執行 | 佇列中的請求此時才在 game thread 上執行完成 |
| 5 | 反向對照：對一個自行覆寫 ProcessEvent 的 class（有自己 slot 的 BP）直接呼叫 | log 顯示 `(caller-asserted safe)`，且呼叫仍然成功<br>⚠ 這裡 fail-open 是正確行為，不要當成沒修好 |
| 6 | 保持一個 invoke 在佇列中，正常遊玩數分鐘 | 沒有 `SEH exception during queued PE call`，沒有 0xC0000409 |

### ⬜ AA12 / AA13 (key: FreezeOutcome) —— Freeze 腳本不再謊報成功

*build 3125 · 優先度 **中***

| # | 做什麼 | 預期 |
|---|---|---|
| 1 | Property Search 找一個有 live instance 的數值欄位 → Copy Freeze Script → 貼進 CE → 打勾。 | 數值被凍住、Lua Engine 視窗自動關閉、記錄維持打勾。 |
| 2 | 在 UE5Dumper.dll「未注入」的狀態下打勾同一份腳本。 | 跳出 showMessage 指明原因、記錄自動取消打勾、Lua 視窗保持開啟。 |
| 3 | 對目前 0 個 live instance 的 class（尚未生成的敵人）打勾，然後讓該類生成一隻。 | 記錄維持打勾、視窗保持開啟、只輸出 [Freeze] armed: no live instances of X right now；生成後約 5 秒內凍結生效。<br>⚠ 這裡若自動取消打勾即為 FAIL，必須回報。 |
| 4 | 把 CFG.className 改成不存在的名稱後打勾。 | 行為與上一步完全相同（armed, 0），不得聲稱是拼字錯誤。 |
| 5 | 嵌入 build 3125 之前（pre-1.2）的 ue5_freeze_helper.lua，再打勾新產生的腳本。 | 出現「older ue5_freeze_helper.lua … re-inject it」、視窗保持開啟、記錄維持打勾。 |
| 6 | 同時打勾兩份不同的 freeze 腳本，再取消其中一份。 | 另一份仍持續凍結生效。 |

### ⬜ B18 —— Extra Scan 跑到一半被取消要立刻收工（**Fern::Stop graceful 已完成，只剩這一步**）

*優先度 **中** · 需要：**GObjects 無法用 AOB 一次解出**的遊戲，否則 Extra Scan 根本不會跑久*

| # | 做什麼 | 預期 |
|---|---|---|
| 1 | 讓 Extra Scan 真的跑久，跑到一半取消 CE record 或關掉 UI。 | `PipeServer: Stop watches+scan joins done` 在 `Stop entry` 後約一秒內出現。FAIL = 中間隔了好幾秒，或 CE 視窗整個凍住直到掃完。 |

### ⬜ .CT DLL discovery —— 到底是哪一個 slot 答的（**B5 主動半與探索半都已完成，只剩這一步**）

*優先度 **中** · ⚠ 先確認 CE 安裝資料夾底下沒有 `UE5Dumper.dll`，否則那個較便宜的 slot 會先答（見 todo.md `[STALEDLL-2026-08-18]`）*

| # | 做什麼 | 預期 |
|---|---|---|
| 1 | 移走 `dll-path.txt`，設 `UE5_DEBUG=1`，**先把 CE 的 Lua Engine 開著**，再從 CE 最近開啟檔案清單載入 `.CT` 並勾 init。 | slot 報告寫「folder of the most recent UE5CEDumper.CT in CE's recent-files list」。<br>⚠ 若寫的是 CE 自己的資料夾，代表這一步又沒測到。 |

### ⬜ M1–M5 / DLL LOW L1,L5,L8,L10,L12 / Solide L2–L4 + 截斷徽章 —— hold worker 的競態、斷線與 256 上限徽章

*優先度 **中***

| # | 做什麼 | 預期 |
|---|---|---|
| 1 | 開啟 See-through，然後分別做四種關閉：(a) 移動中關掉 (b) 遊戲暫停/卡住時關掉 (c) 直接拔掉 UI 連線 (d) 關閉遊戲。 | 四種情況下所有被隱藏的 actor 都恢復可見。任何一個 actor 留在隱形狀態就是 FAIL（只能用眼睛看螢幕判定）。 |
| 2 | 起一個 force-field hold，hold 進行中中斷 UI 連線，再重連並看 `get_forced_fields`。 | hold 仍列在清單上，而且用 CE 讀該欄位的值仍被壓住。<br>⚠ 只看清單不算通過：殭屍 job 會照樣列出來但已經停止 re-assert，一定要在 CE 讀值。 |
| 3 | 保持一個 hold 生效、UI 仍連著，直接關閉遊戲。 | 不當機、不卡住、Windows 應用程式事件記錄沒有新項目（沒有正面 log 可查，證據就是「什麼都沒發生」）。 |
| 4 | 對一個活體實例超過 256 的 class（投射物、群眾 NPC、可破壞物件）下 Force。 | strip 那一列顯示 `⚠ capped` 與 `(256 held)`，狀態列結尾是 "cap reached, more exist unheld"；換一個小 class 則兩者都不出現。 |
| 5 | 對上一步按 Reset，再讀那些實例的欄位值。 | 沒有任何實例卡在被強制的值。 |

-----

## 第 4 步 — 需要特定條件的遊戲

手上要有符合條件的樣本才做得動；條件寫在每項的「需要」欄。

### ⬜ A12 —— Group 模式下的同一件事

*build 3261 · 優先度 **高** · 需要：和 A11 同一個容器；接著 A11 那一項做，動作一樣，只是面板切 Group*

| # | 做什麼 | 預期 |
|---|---|---|
| 1 | Value Search 切 **Group** 模式、**Deep 開**，兩個 slot 的值都放在同一個 `TArray<FStruct>` 元素裡，First Scan | 出現候選列，slot 欄位帶 `[i]` 索引<br>⚠ 沒開 Deep 的話這一整項都沒測到 |
| 2 | 讓那個容器在遊戲裡長大到必須重新配置，再 Next Scan | 該列**存活**，且 `scan-0.log` 出現 `RefineGroup re-anchor: N … repointed` |
| 3 | 刪掉一個排在命中元素「前面」的元素，再 Next Scan | 該列被丟掉，且 `RefineGroup cand[...]` 那行的 `container-moved=` 不是 0<br>⚠ 這個計數是唯一能分辨「容器搬走了」和「predicate 不符」的東西 |
| 4 | 換成 `TMap`（value struct 同時裝兩個值）重跑第 2、3 步 | 行為同上，**而且第一次 Next Scan 不會整批被丟掉**<br>⚠ 遊戲裡什麼都沒動卻整批消失 = `MaxCapacity` / `MaxIndex` 用錯單位，這是唯一看得出來的地方 |
| 5 | 反向對照：拿一組「非容器」的普通欄位做 Group scan，什麼都不改就 Next Scan | 列都還在，而且完全沒有 `RefineGroup re-anchor` 那行 |
| 6 | grep log 的 `carries no ValueAnchor` | 不該出現（出現＝三個 by-name 傳遞環節有一個漏掉，離線測不到） |

⚠ **巢狀第 2 層以上刻意不處理**（`UnverifiableNested`），行為與 3261 之前相同，不算失敗。

### ⬜ A11 —— 容器長大後 Value Search 的候選不該消失

*build 3253 · 優先度 **高** · 需要：要一款有 `TArray` / `TMap` UPROPERTY，且元素數量會在遊玩中增減的遊戲（背包、生成物清單、buff 清單）。*

| # | 做什麼 | 預期 |
|---|---|---|
| 1 | Value Search 掃一個位在容器元素裡的已知值（`TArray<FStruct>` 欄位或 `TMap` 的 value），First Scan | 該列帶 `[i]` 元素索引<br>⚠ 先確認它真的是容器元素而不是直接欄位 |
| 2 | 在遊戲裡「新增」到該容器直到它必須擴充（撿東西、生怪），再用同一個值 Next Scan | 候選**存活**，且 `scan-0.log` 出現 `Refine re-anchor: N container element(s) repointed after a realloc`<br>⚠ 候選活著但完全沒有 re-anchor 那行 = 容器還有餘裕沒真的搬家，這次沒測到 repoint |
| 3 | 刪掉一個索引「排在候選前面」的元素，再 Next Scan | 候選被丟掉，log 的 `dropped` 數字增加<br>⚠ 這才是無聲錯值那一種：尾巴就地往前移，舊位址讀得很乾淨但回的是鄰居的值 |
| 4 | TSet / TMap：刪掉候選指著的那一筆，再 Next Scan | 被丟掉<br>⚠ 釋放掉的 sparse slot 會被下一次 Add 就地重用，位址一模一樣，只有 allocation bit 看得出來 |
| 5 | 反向對照（不可略過）：掃一個容器值之後，只「append」而不觸發重新配置，再 Next Scan | 候選**存活**<br>⚠ 這些消失了就是 regression 不是修好 —— 天真的 `{dataPtr,count}` 規則正是會把它們全殺掉 |
| 6 | 對一個「非容器」的普通欄位重做第 1 步 | 行為不變，而且完全沒有 `Refine re-anchor` 那行 |

### ⬜ M2 / TSet 迴歸 —— 計數與非迴歸（**M1 / M3 / A2 / U1 / V1 已完成，只剩這兩項**）

*build 2830 · 優先度 **中***

| # | 做什麼 | 預期 |
|---|---|---|
| 1 | 找一個**實際列數沒有被 array limit 截掉**的 TMap/TSet（元素數小於 Options 的 array limit，預設 128），並在遊戲中移除其中一筆。 | header 的 count 與實際列數一致。<br>⚠ 元素數超過 array limit 時列表會靜默停在上限，這一步就無法判定（見 todo.md `[CONTAINERCAP-2026-08-18]`）。 |
| 2 | 展開 `TSet<FName>` / `TSet<UObject*>`，並開啟任一 UDataTable。 | 元素與資料列仍正確解析。<br>⚠ DumperTest 這三種樣本都沒有，必須用真實遊戲。 |

### ⬜ U3 / U17 —— struct 預覽的 LWC 寬度與 GAS 樣本（**步驟 1、2 已完成，只剩這些**）

*build 3169 / 3171 · 優先度 **中** · 需要：一個 UE5 LWC（24-byte FVector）遊戲、一個使用 GAS 的遊戲*

| # | 做什麼 | 預期 |
|---|---|---|
| 1 | 在 UE5 LWC（24-byte FVector）遊戲上展開一個 struct-valued 的 TMap/TSet 元素。 | 三個分量都出現，數量級正確。 |
| 2 | 在使用 GAS 的遊戲上做同樣展開（CDO 走訪即可，主選單就夠）。 | 成員完整、寬度正確。 |

### ⬜ G1 / X3 / U7 / AF2 —— 三個要碰到特定遊戲才看得到的顯示

*build 3016-3031 · 優先度 **中** · 需要：三種樣本：offset 偵測只量到一部分的遊戲、含超過 50 bytes 非 ASCII StrProperty 的在地化遊戲、以及候選 class 超過 30 與少於 30 各一款的遊戲。*

| # | 做什麼 | 預期 |
|---|---|---|
| 1 | 在 offset 偵測部分失敗的遊戲上打開 Pointers tab。 | 出現琥珀色橫幅「Dynamic offsets only partially measured (unmeasured:…)」並列出探針名稱。<br>⚠ 沒有橫幅不等於通過；要對同一 process 跑 get_offsets 確認回報 validated: true 才算。 |
| 2 | 在在地化遊戲用 Property Search 找一個超過 50 bytes 的非 ASCII（CJK）StrProperty。 | 有結果列回來，preview 以「…」結尾（修正前是整個搜尋 0 列並報錯）。 |
| 3 | Experimental → Detect Player Stats，先在候選 class 超過 30 的遊戲跑一次。 | 超過上限的列以琥珀色顯示「? not checked」（不是「· guess」），狀態列顯示「30 of N classes live-probed」。<br>⚠ 再到候選 class 少於 30 的遊戲跑一次，正確結果是完全沒有這個後綴——兩邊都做才算測完。 |

### ⬜ AE10 —— AOB 掃不到 &GWorld 的遊戲上 🌍 要能用

*build 2961 · 優先度 **中** · 需要：AOB 掃不到 &GWorld 的遊戲（Pointers 面板沒有 GWorld 位址，或以 proxy 模式執行，例如 TQ2），外加一款 GWorld 正常解析的遊戲做回歸。*

| # | 做什麼 | 預期 |
|---|---|---|
| 1 | 在該遊戲檢查 Instance Finder、Interesting Functions、Interesting Properties、Detect Stats、Class Pivot、Snapshot（Diff + Group）、SPC Query 各列的 🌍 按鈕。 | 全部可點，不再是灰的。 |
| 2 | 點其中一個 🌍。 | 找到路徑，或顯示 DLL 明確的「no path」/「invalid」訊息。<br>⚠ 沒有任何訊息、靜默無反應就是失敗。 |
| 3 | 反向對照：在關卡尚未載入的主選單（確定沒有活的 UWorld）再點一次 🌍。 | 回報 DLL 的 invalid/no-path 狀態，不能看起來像成功。 |
| 4 | 回歸：在 GWorld 正常解析的遊戲上重跑幾個 🌍 交接。 | 行為與這次改動前完全相同。 |

### ⬜ D2（樣本心跳） —— DumperTest 樣本的 HUD 心跳仍在動

*優先度 **中** · 需要：For Testing 資料夾內的 DumperTest 封包（Shipping 或 Development 皆可）*

| # | 做什麼 | 預期 |
|---|---|---|
| 1 | 以 -windowed -ResX=1280 -ResY=720 啟動封包，不要加 -DumperTestNoHud | 畫面上出現 ADumperTestHUD 的五行文字<br>⚠ [DumperTest] ADumperTestActor ready at 0x… 這行只在 Development 出現，Shipping 沒有不代表壞掉 |
| 2 | 記下 T0 的各欄位值，等約 15 秒再記 T1 | frames 上升；TickCount 每秒 +1；Health.CurrentValue 下降；Health.BaseValue=100 與 FrozenInt=424242 完全不動 |
| 3 | 比對 F32_Ticking / F64_Ticking / RawDouble_Ticking 的差值與 TickCount 增量 | 差值分別為 −10.25 / +0.25 / +0.5 乘上 TickCount 的增量<br>⚠ Shipping 會靜默忽略 -ExecCmds="t.MaxFPS 30"；要限 FPS 請改用 Development 封包 |

### ⬜ B25 —— pre-4.11 拒絕不再只憑一個 PE 欄位就擋掉

*優先度 **中** · 需要：PE ProductVersion 落在 4.0–4.10 的遊戲，或可用 UE 版本 override 硬造；反向對照另需一個真正的 UE3 binary。*

| # | 做什麼 | 預期 |
|---|---|---|
| 1 | 用 UE 版本 override，或找一款 PE ProductVersion 報 4.0–4.10 的遊戲，注入後 grep `scan-0.log` 的 `below the … floor — NOT accepting that on its own`。 | 該行出現，而且掃描照樣跑完（tier 3 → low confidence → gate 不啟動）。FAIL = 對一款其實能用的遊戲印出 `SKIPPING the scan`。 |
| 2 | 反向對照：拿一個真正的 UE3 binary 注入。 | 仍然被拒絕，`scan-0.log` 出現 `PRE-UE4 engine POSITIVELY identified`。<br>⚠ 沒跑反向對照就不算測完 — 只證明「不再擋」等於沒證明「該擋的還會擋」。 |

### ⬜ B29 —— 第三方 wrapper 存在時仍會正常注入

*優先度 **中** · 需要：裝了第三方 dxgi.dll / dinput8.dll wrapper（例如 ReShade）的 UE 遊戲。*

| # | 做什麼 | 預期 |
|---|---|---|
| 1 | 把 ReShade 或任一第三方 `dxgi.dll` / `dinput8.dll` wrapper 放進 UE 遊戲資料夾。 | 遊戲資料夾內存在非我方的同名 DLL。 |
| 2 | 附加 CE，點 *UE5CEDumper: Inject && Connect*，並 grep `init-0.log` 的 `is loaded but is not ours`。 | 正常注入，且該行出現並指名那個外來模組。FAIL = 舊訊息 "already loaded … no injection needed"，之後 UI 連不上。 |
| 3 | 再用一款路徑含非 ASCII 字元的遊戲重做一次，看同一則訊息。 | 訊息裡的路徑完整顯示，不再變成 `EVERSPACE? 2` 這種問號。 |

### ⬜ GObjects layout fix (build 2782) — DragonSword，PARTIAL 剩餘項 —— base anchor 命中時要選到 UE5-Extended 而非 relaxed B

*優先度 **低** · 需要：DragonSword Awakening，且該次啟動剛好從 FUObjectArray base anchor（位址結尾 …F8B0）解出 GObjects；結尾 …F8C0 的那次不算數。*

| # | 做什麼 | 預期 |
|---|---|---|
| 1 | 啟動 DragonSword Awakening 並注入，於 scan-0.log／offsets-0.log 找出 GObjects 解析到的位址。 | 位址結尾是 …F8B0（base anchor）。若是 …F8C0 則本次不具驗證力，直接結束、下次再試。<br>⚠ 這個 anchor 每次啟動不固定，不能靠重跑同一次判定；沒命中 …F8B0 就不要記成通過。 |
| 2 | 確認同一份 log 中的 preset 行內容。 | 讀到 preset UE5-Extended，不是 relaxed B。 |
| 3 | 回歸檢查：對其他原本就能解析成功的測試遊戲各注入一次，grep log 中的 Could not detect layout, using default。 | 完全沒有這一行；原本能解出的 layout 仍照舊解出。 |

### ⬜ X2 —— AA(B)/FIRE 對超出 5,000 列上限的類別仍可用

*build 2888 · 優先度 **低** · 需要：類別數超過 5,000 的大型 UE 遊戲（例如 DQ7R、Hogwarts Legacy、FF7R）。*

| # | 做什麼 | 預期 |
|---|---|---|
| 1 | Game Class Filter → Load。 | 狀態列結尾出現「⚠ STOPPED at the 5,000-row cap — more classes exist」。<br>⚠ 沒出現這行代表這款遊戲太小，換一款，否則後面驗不出東西。 |
| 2 | Interesting Funcs → Load，挑一列，其類別在 Game Class Filter 清單中查不到（用類別名過濾確認確實不存在）。 | 確認該類別在上限之外。 |
| 3 | 對該列按 AA(B)。 | 腳本正常產生／送達 CE；不再出現「Class X not found」。 |
| 4 | 到 Console 分頁，對一個帶參數的 exec 指令按 Run（FIRE 對話框），以及它自己的 AA(B)。 | 兩者皆成功。 |
| 5 | 反向對照：對一個真的不存在的類別操作。 | 單純顯示 not found，不應出現「may still exist」的措辭。 |

### ⬜ G12（heuristic 分支）—— 走 fallback 時 offset 仍正確

*build 3119 · 優先度 **低** · 需要：offset 驗證走 heuristic fallback 的遊戲：scan-0.log / offsets-0.log 出現 Cannot find Guid or Vector struct（Solarpunk 是紀錄中的案例，但後續 build 可能改走 Guid）。*

| # | 做什麼 | 預期 |
|---|---|---|
| 1 | 注入候選遊戲後 grep scan-0.log / offsets-0.log 的 Cannot find Guid or Vector struct 與 ValidateAndFixOffsets: Using struct。 | 確認走的是 heuristic fallback，而非 Using struct 'Guid'。<br>⚠ 走到 Guid 分支就等於沒測到，要把實際分支記下來。 |
| 2 | 在該遊戲上用 Live Walker 檢查 enum 名稱與 TArray inner type。 | 兩者皆正確，不再偏移 8 bytes。 |

### ⬜ B8（deferred 半） —— 遊戲執行緒安靜時關 Fly 仍會補回碰撞

*優先度 **低** · 需要：背景時真的會停止 tick 的遊戲（有吃 t.IdleWhenNotForeground）。Elliot 背景仍在 tick，測不到。*

| # | 做什麼 | 預期 |
|---|---|---|
| 1 | Teleport 分頁 → Fly ON + Noclip → 飛穿一道牆 → alt-tab 切到 UI 等超過 500 ms → 按 Disable。 | Disable 有被按到（不是靠關遊戲觸發）。<br>⚠ 關閉遊戲永遠測不到這半：關遊戲不會呼叫 UE5_Shutdown，Fly 的 disable 根本不執行。 |
| 2 | grep `walk-0.log` 的 `Fly:`。 | 出現 `Fly: DISABLED but the pawn's collision is still OFF (game thread unresponsive)`，切回遊戲後再出現 `Fly: game thread resumed after N ms — pawn collision restored`。FAIL = 只有一行 `Fly: DISABLED`，之後角色掉出世界。<br>⚠ 不在 init-0.log；Dunste 的 LOG_CAT 被路由到 walk。 |
| 3 | 回遊戲撞牆，並順便檢查 `Fly: collision disable deferred`。 | 角色被牆擋住；`deferred` 那行可以出現，但每次 stall 只能出現一次，不能連續刷。 |

### ⬜ V1a 容器重配置 / NumericAll 結果量 —— 容器重新配置時 Next Scan 要降級而非亂報

*優先度 **低** · 需要：有 TSet / TMap / TArray UPROPERTY、且元素會在兩次掃描之間增刪的遊戲。*

| # | 做什麼 | 預期 |
|---|---|---|
| 1 | 掃一個存在容器內的已知值，接著在遊戲中讓該容器新增/刪除元素（觸發重新配置），再按 Next Scan。 | 該候選被丟掉（SEH 安全讀取失敗即淘汰），不會回報一個錯的命中位址。 |
| 2 | 選 NumericAll 掃一個 0 / 1 / 255 這類小值。 | 橘色結果量警告出現，且結果數量還在人可以用的範圍（純 UX 判斷，沒有機械式 PASS 線）。 |

### ⬜ b719 freeze / b648 PE / b636 fast path / b642 FPROPERTY_FLAGS / b637+644 return value —— 舊版 invoke、回傳值與屬性凍結的一次性複查

*優先度 **低** · 需要：ES2 (UE5.5) 與 Geri (UE4.27)；屬性凍結那項要一款 NPC 會重生的遊戲（首選 Geri）。*

| # | 做什麼 | 預期 |
|---|---|---|
| 1 | 在會重生 NPC 的遊戲上開 Property freeze (Route B)，觀察一段時間。 | tick 對 FPS 的影響可接受、重生時有重新掃描、換場景後 vtable liveness 守衛擋得住、多腳本並存不打架。 |
| 2 | 在 ES2 (UE5.5) 與 Geri (UE4.27) 各做一次 instance invoke。 | log 出現 `GameThreadDispatch: validation OK — hook fired N times`，以前 timeout 回 `-5` 的 invoke 現在會成功。 |
| 3 | 在活躍 session 比較 static-native PE fast path 與 game-thread dispatch 的延遲。 | 有狀態的 UFunction 仍走 dispatch，不會誤落進 fast path。 |
| 4 | 掃過 12 款以上已測遊戲的 Class Structure Return 欄位。 | baked PARAMS 不再把 ReturnValue 當成輸入參數。 |
| 5 | 各做一次 pointer-return 與 FString-return 的 invoke。 | pointer 回傳顯示 `0x` 前綴；FString 回傳顯示 "see After: dump above" 提示。 |

-----

## 第 5 步 — 目前沒有可測的環境

**一律低優先**，就算 register 標 MED 也一樣 —— 找不到樣本本身就是「這種遊戲很罕見」的證據。

### ⬜ U2 —— CPN 遊戲的 FName 陣列 stride

*優先度 **低** · 需要：WITH_CASE_PRESERVING_NAME 開啟的 UE5.5+/5.7 遊戲。TQ2 實測 case_preserving=false（20-0 sweep），Solarpunk 亦為 false，DumperTest 因引擎旗標不可能；目前 30+ 款測過的遊戲中沒有任何一款符合。*

| # | 做什麼 | 預期 |
|---|---|---|
| 1 | 對候選遊戲注入後下 get_offsets，看 case_preserving（每款只要一次呼叫，可以便宜地掃很多款） | 找到 case_preserving=true 的遊戲<br>⚠ 必須同時看 probe_ran=true；probe_ran=false 時的 false 只代表還沒探測，不是結論 |
| 2 | 若找到，Live Walker 展開任一 actor 的 Tags（TArray<FName>） | 每個元素都是完整正確的 FName；不是第二個之後從前一個的中段讀起（stride 16，非 8） |

### ⬜ G3 —— Extra Scan → Apply 的 rescan 閘門

*build 3121 · 優先度 **低** · 需要：有項目未解析（例如 GWorld 掃不到）才會觸發 Extra Scan → Apply 的遊戲；目前 34 款測試遊戲全部都能解析 GWorld。*

| # | 做什麼 | 預期 |
|---|---|---|
| 1 | 在可觸發的遊戲上按 Extra Scan，再按 Apply，然後 grep offsets-0.log 的 ValidateAndFixOffsets: Starting。 | 該行恰好出現一次。 |
| 2 | 同一次 Apply 後 grep apply_rescan: Applied GEngine=0x。 | GEngine 先前未解析時，該行仍會出現。 |

-----

## 做完一項之後

1. 在 [todo.md](todo.md) 把該項打勾，附上**證據**（log 行、截圖說明、實測數字）。
2. 這份檔案把整條刪掉 —— 這裡只留「還沒做完」的。
3. 驗出新缺陷的話，寫進
   [audit-2026-08-13-early-code-findings.md](audit-2026-08-13-early-code-findings.md)，
   然後跑 `py tools/check_audit_register.py`。
