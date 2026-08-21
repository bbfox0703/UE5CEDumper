# 待驗證項目 —— 操作清單（只驗證，不改程式碼）

> **這份寫的是「怎麼操作」，不是「為什麼」。** 每一項只有兩件事：**做什麼**、**預期看到什麼**。
> 成因、來龍去脈、哪個 build 改了什麼原理，全部在英文正本
> [todo.md](todo.md) 的 `## Pending live-game verification` —— 需要證據時去那裡查，
> 用項目編號（`PEHOOK`、`G10`、`B4`…）grep 就找得到。
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
| **第 1 步 — 只開 UE5DumpUI** | 2 | UE5DumpUI（其中一項要 **AOT/trimmed** 版） |
| **第 2 步 — 要注入一個執行中的遊戲** | 18 | 一款執行中的 UE 遊戲 + 注入 |
| **第 3 步 — 遊戲 ＋ Cheat Engine** | 10 | 遊戲 + Cheat Engine |
| **第 4 步 — 需要特定條件的遊戲** | 19 | 符合特定條件的遊戲 |
| **第 5 步 — 目前沒有可測的環境** | 2 | 目前沒有 |
| **合計** | **51** | |

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
> `U1/MG1/MG3`、`D1/D3`、`AA38` 三項都已結案並 commit，證據在
> [todo.md](todo.md)（grep 項目編號）。**不用開遊戲**的項目目前歸零 ——
> 現在最便宜的一批是第 1 步。

-----

## 第 1 步 — 只開 UE5DumpUI

不用注入任何遊戲。

### ⬜ AF16–AF23 —— DataGrid 欄位標題排序（**必須用 AOT 版**）

*優先度 **中** · ⚠ **一定要用 `build.ps1 -Mode Publish` 出來的 trimmed 版**。這個問題在一般 dev build
上不會出現，用 dev build 測等於沒測。*

| # | 做什麼 | 預期 |
|---|--------|------|
| 1 | 點 Live Funcs 的 **Period**、Detect Stats 的 **✓** 和 **Offset**、Live Walker 函式表的 **Params** 這四個欄位標題。 | 每個都會重新排序，再點一次反向。Period 要照**數值**排（16.7 ms 的列排在 1000 ms 之上），不是照顯示字串。 |
| 2 | 從 Interesting Functions 開 Props 對話框、從 Class Struct 開 Xref 對話框，每個欄位標題都點一次。 | 兩邊各 6 個標題都會重排。`Access` / `Refs` 要照**數字**排（「12W / 3R」排在「2W / 1R」之上）。 |
| 3 | 點 Class Pivot Discover 表的 Changed / Cat / Shape / Score、Snapshot 清單的 Label / Size、Snapshot Diff 的 **Change**、Snapshot 與 SPC group 表的 **Class**、Invoke 參數挑選視窗的 4 個標題。 | 全部都會重排。**Size** 要照數值排（「980 MB」排在「1.2 GB」之下）。 |

-----

### 🟡 AE4 / AE5 / AE6 / AE7 —— Proxy Deploy 面板的並行防護與選項保持（**只剩步驟 4 的互斥閘**）

*優先度 **高** · 步驟 1 已於 2026-08-17 驗畢；步驟 2、3、5、6 已於 2026-08-19 驗畢，均已刪除*

| # | 做什麼 | 預期 |
|---|---|---|
| 4 | 回歸：Find leftovers → 勾一筆 → Delete；**刪除還在進行中**時嘗試啟動任一掃描，反向再試一次。 | 刪除中掃描被擋、掃描中刪除也被擋，兩邊互斥。<br>⚠ 刪除若在你點下一個動作前就跑完，這步就是「沒測到」，不是通過 —— 先讓待刪清單長一點。<br>⚠ 只看到確認對話框跳出來不算：對話框開啟不等於刪除正在跑。 |

-----

## 第 2 步 — 要注入一個執行中的遊戲

任何一款 UE 遊戲都可以。

### ⬜ AF7 / AF8 —— 反組譯預算截斷要說出來、Int8Property 的正負號

*優先度 **中** · 兩項都可能因為找不到樣本而測不了，那也是結論。*

| # | 做什麼 | 預期 |
|---|--------|------|
| 1 | 用 Property Search 找一個 `Int8Property` 欄位，Force 成**負值**（例如 `-5`），再 `get_forced_fields`。 | 讀回來就是 **-5**。修正前讀回來是 **251**，於是 worker 每個 tick 都重寫同一個 byte，UI 永遠顯示 drift。<br>⚠ 沒有任何遊戲露出 `Int8Property` 的話，這項就是無樣本可測。 |
| 2 | 同一個欄位改 Force 成 `200`。 | 被**拒絕**（超出 int8 範圍），而不是寫進去變成 -56。 |
| 3 | 對一個**原生**（非 Blueprint）UFunction 下 `walk_function_props`，看回覆有沒有 `budget_hit`。 | 這個 key 存在。若為 `true`，Props 對話框狀態列變琥珀色並寫出「hit its instruction budget」，Interesting Functions 批次的 **Uses** 欄顯示 `⚠ partial`。<br>⚠ 要找夠大的原生函式才會觸發 —— 先 grep DLL log 的 `AnalyzeNativeFunctionProps ... BUDGET` 找目標。 |

-----

### ⬜ AF22 / AF12 / AF13 —— Force 對話框的用字、Group 每格上限要講出來

*優先度 **中***

| # | 做什麼 | 預期 |
|---|--------|------|
| 1 | Property Search → 對某列按右鍵 → **Force value…**。 | 標題是「Force property value」、欄位標籤是「Force value (…)」、確認鈕寫 **「Hold this value」**，而且繼承欄位的警告**不會**提到 `className` 或 CFG block。 |
| 2 | 再走一次一般的 **Freeze** 流程。 | 仍然寫「Create freeze script」，也仍然給 CFG block 那段建議（這是上一步的對照組）。 |
| 3 | Snapshot 分頁 → 用一個夠常見的數值做 Group match，讓某個 slot 在某個物件上配到超過 256 個欄位。 | 狀態列多出「a slot matched more than 256 fields」那段提示（和 live Group Scan 一模一樣的句子）。 |
| 4 | 把 Value Search 的 per-slot cap 改成 1024，再跑一次**快照**的 Group 查詢。 | 快照這邊仍然顯示 256 —— 這是正確的，重點是現在會講出來而不是讓人以為兩邊同步。 |

-----

### 🟡 A6 —— Force 是否對子類別一併生效（**只剩步驟 3、5**）

*build 3036 · 優先度 **高** · 步驟 1、2、4 已於 2026-08-19 驗畢並刪除*

| # | 做什麼 | 預期 |
|---|---|---|
| 3 | 對一個有同字首兄弟類別的類別下 Force（如 Enemy vs EnemyProjectile、或任一 Foo / FooComponent 組合），檢查 ForcedFields 狀態列與 DLL log 的 FindInstancesDerivedFrom base=… 行 | 不相關的同字首類別「沒有」被 hold<br>⚠ 前面步驟看到「hold 了數百筆」不能替代這步：字首比對也會 hold 數百筆，兩者長得一樣。 |
| 5 | 回歸：對基底類別 Force 一個 bool 後執行 reset_all_fields，再觀察後續**新生成**的物件 | 新生成物件不會仍帶著被強制的值（表示沒有寫到 CDO）<br>⚠ 一定要在 reset 之後真的生出新物件；看既有物件測不到這件事。 |

### 🟡 AD4 —— God Mode 徽章要說明原因而非只有開關（**只剩步驟 4：`ON (contested)`**）

*build 3203 · 優先度 **高** · 步驟 1、2、3、5、6 已於 2026-08-19 驗畢並刪除*

| # | 做什麼 | 預期 |
|---|---|---|
| 4 | 進入戰鬥**實際挨打**，讓遊戲以傷害重置該旗標，同時連續按 ↻ 數次 | 多數為 `ON`，至少要看到一次 `ON (contested)`<br>⚠ 需要真的有人在玩（挨打），掛在選單或站著不動測不到。<br>⚠ 這格出現得很少是設計使然（re-assert worker 很快就贏），但**沒看到就是沒測到、不是通過**（見鐵則 1）。 |

### ⬜ A3 —— 每個 class 的多個 FVector 都要能掃到

*build 3168 · 優先度 **高***

| # | 做什麼 | 預期 |
|---|---|---|
| 1 | Value Search 選 Float（或 NumericAll），任意值，對一個有 pawn/actor 的 class 執行掃描 | 結果中出現欄位名結尾為 `.Velocity` / `.Scale3D` 的列，不再只有 `.Location` |
| 2 | 反向對照：同樣條件但 data type 改成 FVector 再掃一次 | 結果與 3168 之前相同、沒有變化<br>⚠ 不可拿 FVector 掃描當通過依據；這步只是對照，有變化反而代表改到不該改的地方 |
| 3 | 對同一欄位改用 Group Scan 或 Property Search 的 Deep 模式 | 一樣找得到（這條路徑在 3168 之前就找得到） |
| 4 | grep `scan-*.log` 搜尋 `hit the 4000 scan-field cap` | 一般 class 上不出現這行<br>⚠ 若經常出現，代表 cap 值設錯，要回報 |

### ⬜ V6 / U8 —— 兩個一開遊戲就能看的面板行為

*build 3016-3031 · 優先度 **高** · 原步驟 1（A5 Preview）已於 2026-08-19 驗畢並刪除；原步驟 2（AE9 排序選單）已於 2026-08-17 驗畢並刪除*

| # | 做什麼 | 預期 |
|---|---|---|
| 1 | Live Walker 輸入欄位搜尋關鍵字 → 按 Refresh，並讓 auto-refresh 再跑幾拍。 | 高亮保留、↑/↓ 步進仍落在高亮列、表格不跳回最上方。<br>✅ 「按 Refresh」那半段已於 2026-08-17 驗畢，**只剩 auto-refresh 那半段**。<br>⛔ **auto-refresh 那半段先別做**：要等 `[AUTOREFRESH-2026-08-19]` 的修正進到**已發佈**的 build。另一台機器目前跑 `dist` 1.0.0.3262，整批程式跑完前不會更新，在那之前 Auto 本來就會停在 0，測了也只是重測那個已知缺陷。 |
| 2 | Live Walker 找一個值帶數字尾碼的 NameProperty（Slot_1、Slot_2），同時用 Value Search 看同一位址。 | 面板與 Value Search 顯示同一組 8 bytes、尾碼數字一致。<br>⚠ 物件／實例「名稱」被截斷是另一條未修的線，不要當成這項失敗。 |

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
| 2 | ✅ **`ascii` 已於 2026-08-18 用 OCTOPATH 驗出**（`winmm.dll` proxy）：`DetectVersion: Tier 1 (ascii) '++UE4+Release-4.18' -> 418`。四種組合已收三種（`utf16`+UE4、`ascii`+UE4、Tier 0 直接結束），**只剩 UE5 分支**。 | ⛔ **UE5 分支本機無宿主，先別開遊戲**：全機 18 個已安裝 UE 執行檔用 `py tools/verify/tier1_host_survey.py` 離線掃過，只有 3 個能產生 Tier-1 行，全是 UE4。需要「**同時**穿過 Tier 0 **且**映像檔內含 `++UE5+Release-` needle」的遊戲 —— Light Maze/Lushfoil/Manor Lords 有 needle 但停在 Tier 0；Solarpunk/TQ2/ES2/STVoyager/Satisfactory/DSA/Avowed 連 needle 都沒有。<br>⚠ **裝新遊戲前先用該工具篩**，不要靠引擎版本猜。 |

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

### ⬜ Genau RIP decode (b2544) —— RIP 解碼修正沒有改動解出的位址

*優先度 **低***

| # | 做什麼 | 預期 |
|---|---|---|
| 1 | 同一款遊戲，分別用修正前與修正後的 DLL 各注入一次，各留一份 `scan-0.log`。 | 兩份 log 都跑完整個 FindAll。 |
| 2 | 比對兩份 log 的 candidate / probe 計數，以及 GObjects / GNames / GWorld 最終解出的位址。 | 計數下降（這是收益），而三個位址逐 byte 完全相同（這才是驗收標準）。位址有變就是 regression。<br>⚠ 不能用 sweep.sh 的 pattern diff 判定：它會跳過 Symbol*/CallFollow 簽章，乾淨的 diff 只代表「沒測到」。 |

### ⬜ W8 —— 匯出的 .usmap 要含 Blueprint 產生的 class

*優先度 **中** · 需要一款 Blueprint 用得多的正式遊戲（幾乎所有商業 UE 遊戲都算）。*

| # | 做什麼 | 預期 |
|---|--------|------|
| 1 | 對同一款遊戲匯出 `.usmap`，把「N structs」那個數字和這個 build 之前的紀錄比對。 | 數字**增加到數千**（原本只有原生 class 的幾百個），差額大致等於遊戲裡 `BlueprintGeneratedClass` 的數量。 |
| 2 | 在檔案裡找一個已知的 `BP_*_C` 或 `WBP_*_C` 名稱。 | 找得到。修正前這些**全部被丟掉**。 |
| 3 | 若手邊已裝 FModel / CUE4Parse，用它讀這個 `.usmap`（`W1 / W7` 那項本來就需要這個 parser）。 | 能讀出來且不報錯。 |

-----

### ⬜ AC13 / AC14 —— Pipe 傳輸計時、關閉時的 reader

*優先度 **低***

| # | 做什麼 | 預期 |
|---|--------|------|
| 1 | UI 連上已注入的遊戲後，**在連線狀態下直接關掉 UI**。看 `pipe-0.log` 收尾那幾行。 | 乾淨結束，**不可以**出現 `Pipe: ReadLoop error`。修正前那一行是關閉時的 NullReferenceException，把正常關機記成故障。 |
| 2 | 看 System 分頁的 IPC 時間數字，記下來。 | 記下數值即可，這是下一步的基準。 |
| 3 | 在 UI 正在送要求的當下把遊戲關掉（讓寫入失敗），再看 System 分頁的 IPC 數字。 | 失敗那筆的傳輸時間**有被算進去**。修正前寫入失敗的要求一律算 0 ms，等於 pipe 最不穩的時候數字反而最好看。 |

-----

### ⬜ AC15 / AE27 / AF25 —— 掃描速度、Package 欄、Teleport opcode

*優先度 **低** · 三項都是「結果必須完全一樣」的回歸確認*

| # | 做什麼 | 預期 |
|---|--------|------|
| 1 | Proxy Deploy → 掃描 Steam 程式庫，再跑一次一般磁碟掃描。 | 找到的遊戲、名稱、路徑**完全相同**。唯一該有的差別是變快（每款遊戲少讀一次完整的 VERSIONINFO）。 |
| 2 | Game Class Filter → 在 Package 欄位輸入關鍵字，再點 Package 欄位標題排序。 | 結果和以前一樣。若出現**空白或過期的 Package 欄**，就是新的快取失效寫錯了。 |
| 3 | Teleport 分頁 → 產生 CE Lua／`.CT` 的 teleport 記錄，實際跑一次。 | 腳本內容一字不差，teleport 正常。opcode 仍然是 8，只是改成從共用常數取得。 |

-----

## 第 3 步 — 遊戲 ＋ Cheat Engine

還要開 CE 並載入 .CT。

### ⬜ MB3 —— CE mailbox 迴圈改成「逐次保護」（**這批最該先跑的一項**）

*優先度 **中** · ⚠ 改的是每一條 `.CT` 指令都會經過的輪詢迴圈，而且**沒有任何測試目標會編譯
`Mimic.cpp`**，所以這些程式碼一行都還沒跑過。真正的風險不是例外路徑（很難觸發），而是**一般路徑**：
如果重構把正常派送弄壞了，所有 CE 指令會一起壞掉。*

| # | 做什麼 | 預期 |
|---|--------|------|
| 1 | 注入後，隨便跑兩筆會用到 mailbox 的 `.CT` 記錄（例如 Teleport 存點／回點，加上一次 Invoke）。 | 兩筆都和以前一樣成功。 |
| 2 | 看 `init-0.log`／`pipe-0.log`。 | **沒有** `Mailbox: tick threw`，**沒有** `result=-11`。若真的出現 `-11`，代表某個 handler 真的丟了例外 —— 請把 log 留下來，那是有價值的發現。 |
| 3 | 更省事的前置做法：用 `tools/verify/mailbox_addr.py` 解出 `g_invokeMailbox`，**完全不開 CE** 直接戳一筆指令。 | 指令正常完成。這一步不需要 CE，可以先做。 |
| 4 | 例外路徑本身（目前**沒有辦法**主動觸發，只能等它自己發生）。 | 萬一發生：mailbox **繼續**運作，後續指令照常；腳本收到 `-11` 和「the operation did NOT complete」，而不是卡在 `status=PROCESSING` 等到逾時。 |

-----

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
| 4 | 維持凍結，製造 churn：把凍結中的 actor 打死重生，或跨越 level streaming 邊界。 | 約一次 rescan（~5 秒）內重新接上；且沒有任何不相干物件的欄位被改動。 |
| 5 | AA3：凍結執行中把 DLL 卸載/重新注入，讓 rescan 永久失敗。 | ~15 秒內 Lua console 印出一次「... consecutive rescans failed -- freeze STOPPED writing」，之後不再寫入。 |

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

### ⬜ V11 —— 「Register symbol」成功和失敗要看得出差別

*優先度 **中** · 需要 CE ＋ AOBMaker plugin。*

| # | 做什麼 | 預期 |
|---|--------|------|
| 1 | CE ＋ AOBMaker 都連著時，在 GWorld 卡片按 **Register symbol**。 | 面板上方出現**綠色**一行，句子裡有 `gworld_addr`。 |
| 2 | 把 CE 關掉，再按一次同一顆按鈕。 | 面板上方出現**紅色**一行，句子裡一樣有 `gworld_addr`，而且綠色那行不見了。<br>⚠ 修正前這兩種情況畫面上**完全一樣**（都是什麼都不顯示），只有 log 的等級不同。 |
| 3 | 在 **&GEngine** 卡片重複步驟 1、2。 | 行為相同，訊息裡是 `gengine_addr`。<br>⚠ 這是修正時 grep 出來的第二個站點，原本的 finding 只寫了一個。 |

-----

### ⬜ Y10 / Y13 —— Verify 模式：合約檢查要先跑，dump 視窗要涵蓋回傳值

*優先度 **高** · 需要 CE。⚠ 這兩項**沒有動 mailbox 合約**（仍是 3 / min 1），舊的 `.CT` 照樣有效。*

| # | 做什麼 | 預期 |
|---|--------|------|
| 1 | 挑一個回傳**複雜型別**（FString／struct）而且回傳欄位落在第 32 byte 之後的 UFunction，勾 **Verify return**，把 baked script 推到 CE，勾起記錄。 | Lua Engine 的 Before/After dump **涵蓋到回傳欄位**（視窗會自動加寬到剛好蓋住它）。 |
| 2 | 看那行 `[Invoke] OK: … complex return` 的字。 | 只有在 dump 真的蓋得到時才會寫「see After: dump above」；蓋不到時改成講出偏移量並叫你去 CE 記憶體檢視器看。<br>⚠ 修正前不管蓋不蓋得到都寫 see After: dump。 |
| 3 | 取消勾選，**把 CE 從遊戲 detach**，再勾一次。 | 先跳出合約檢查的訊息（句子裡有 `g_mailboxContract`），而且**記錄會自己取消勾選**。<br>⚠ 重點是這時候**一個 `writeByte` 都不可以跑過** —— 修正前是先寫再說。 |
| 4 | 對照組：正常連著 CE 時，挑一個 params 很大的 UFunction（by-value struct 參數）跑 Verify。 | 正常跑完。歸零迴圈現在夾在 1024 byte 以內；修正前 `parmsSize` 超過 1024 就會寫穿 `cmdFlags`／`cmdOutFlags` 和整個 mailbox 結構的尾巴。 |

-----

### ⬜ Y12 —— AOBMaker 沒連時，剪貼簿要放「貼得進去」的 CE XML

*優先度 **中** · 需要 CE。*

| # | 做什麼 | 預期 |
|---|--------|------|
| 1 | 把 CE 關掉（或讓 AOBMaker 斷線），按 **Copy AA Script (Baked)**，再到 CE 的位址清單按右鍵 → Paste。 | 出現一筆型別是 **Auto Assembler Script** 的記錄。<br>⚠ 修正前剪貼簿放的是裸的 `[ENABLE]`／`[DISABLE]` 內文，CE 根本不接受它變成一筆記錄。 |
| 2 | 看對話框的結果訊息。 | 寫的是「copied as **CE XML**」，並且叫你貼到位址清單，而不是含糊的「copied to clipboard」。 |

-----

## 第 4 步 — 需要特定條件的遊戲

手上要有符合條件的樣本才做得動；條件寫在每項的「需要」欄。

### ⬜ AC17 —— 掛載成資料夾的磁碟區，資源回收筒判斷

*優先度 **低** · 需要：一個**掛載到資料夾**的磁碟區（`mountvol`，或磁碟管理 →「變更磁碟機代號及路徑」
→「新增」→ 指定一個空的 NTFS 資料夾）。一般有磁碟機代號的磁碟測不出這一項。*

| # | 做什麼 | 預期 |
|---|--------|------|
| 1 | 把一個固定式磁碟區掛載到資料夾（例如 `C:\Mount\Games\`），在底下放一份殘留的 proxy DLL。 | 準備完成即可。 |
| 2 | Proxy Deploy → 殘留 proxy 清理 → Report，再 Execute。 | 檔案進**資源回收筒**（不是直接刪除）。 |
| 3 | 反向對照：把一個**卸除式**磁碟區用同樣方式掛載到資料夾，底下放一份殘留 proxy，再跑一次。 | 這次**被拒絕**。修正前的固定式磁碟前置判斷問的是「宿主磁碟」（`DriveInfo` 會經過 `Path.GetPathRoot` 正規化），宿主幾乎永遠是固定式，所以對掛載點路徑等同於「一律放行」。 |

-----

### ⬜ AF21 —— 高 DPI 下視窗位置不會被判成跑到畫面外

*優先度 **低** · 需要：把 Windows 顯示縮放改成 **150%**（100% 縮放下這個問題不會出現）*

| # | 做什麼 | 預期 |
|---|--------|------|
| 1 | 縮放設成 150%，把主視窗拖到大約有三分之一露在螢幕右緣外面，關掉程式，再開。 | 開回原來的位置。修正前這個檢查是用 DIP 寬度（只有實際寬度的三分之二）去算，所以一個放得好好的視窗會被判定為在畫面外，位置就不再被記錄。 |
| 2 | 對照組：縮放改回 100%，重複同一組動作。 | 一樣開回原位（這條在修正前後都會過 —— 它證明的是修正沒有把原本正常的情況弄壞）。 |

-----

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

### ⬜ MG2 / TSet 迴歸 —— 計數與非迴歸（**MG1 / MG3 / A2 / U1 / V1 已完成，只剩這兩項**）

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

### ⬜ CLASSTOTAL —— Classes 分頁報的是真正的 class 總數，不是上限值

*優先度 **中** · 分類 **B**（最便宜的第一步是 A：對 Elliot 送一次 `list_classes`，看 `total_classes` 有沒有大於 5000）· 需要：class 數 > 5,000 的遊戲（Elliot 約 6,609）**和**一款 < 5,000 的小遊戲*

| # | 做什麼 | 預期 |
|---|---|---|
| 1 | 在 class 超過 5,000 的遊戲（Elliot）開 Classes 分頁，把「Game classes only」取消勾選後 Load。 | 狀態列寫「**5,000 classes shown of ~6,609 total** … ⚠ STOPPED at the 5,000-row cap」，兩個數字**不一樣**。<br>⚠ 兩個都是 5000 就是 FAIL —— 修正前就是這樣，「total」等於沒回答任何事。 |
| 2 | 同一款遊戲，去看 Interesting Funcs 的「{N} functions across **{K} classes**」。 | Classes 分頁的 total 與 K 相同（都約 6,609），兩個面板互相對得上。 |
| 3 | 對照組：換一款 class 數 < 5,000 的小遊戲 Load。 | 顯示「N classes shown of N total」（兩數相等），而且**沒有** STOPPED 提示。<br>⚠ 沒跑這步就無法排除「不管怎樣都報一個比較大的假數字」。 |

### ⬜ V10 —— Extra Scan 找到的結果不會被它自己觸發的 refresh 擦掉

*優先度 **中** · **需要**：一款第一次掃描後 GObjects 或 GWorld **仍未解出**的遊戲。都解得出來就是無樣本可測。*

| # | 做什麼 | 預期 |
|---|--------|------|
| 1 | 按 **Extra Scan**，等它跑完。 | 綠色的「Found: GObjects: 0x…」**留在畫面上**。<br>⚠ 修正前它會出現一瞬間，然後被掃描自己觸發的指標 refresh 擦掉，所以每次成功都看不到結果。 |
| 2 | 掃描**進行中**時去動 **UE version** 那個下拉選單。 | Extra Scan 按鈕在掃描真正結束前都保持 disabled。<br>⚠ 修正前那個下拉只被 `IsApplyingOverride` 擋，所以會在掃描中把 `IsScanning` 清掉，讓人可以再開第二個掃描。 |
| 3 | 對照組：斷線再重連。 | 掃描結果那一區被清空 —— 換一款遊戲不該看到上一款的結果。 |

-----

### ⬜ Y11 —— FIRE 對做不出來的參數型別要老實拒絕

*優先度 **中** · **需要**：一個參數含 `FText`、`TArray` 或 `TMap` 的 UFunction。找不到就是無樣本可測。*

| # | 做什麼 | 預期 |
|---|--------|------|
| 1 | 找一個吃 `FText` 參數的 UFunction，按 **FIRE**（欄位維持預設值 `0`）。 | **被拒絕**，訊息指名 FText。<br>⚠ 全零的 FText 不是空 FText —— 它含一個引擎會 deref 的 `TSharedRef`，送零會當掉。匯出腳本那邊（helper 的 `ftext` 分支）本來就是無條件拒絕，這步是讓 FIRE 給出同一個答案。 |
| 2 | 找一個吃 `TArray`／`TMap`／`TSet`／struct 參數的 UFunction，欄位**不要動**，按 FIRE。 | 正常送出，那個欄位維持**全零**（＝該型別的預設空值）。 |
| 3 | 同一個欄位打進一個值（例如 `42`），再按 FIRE。 | **被拒絕**並說明原因。<br>⚠ 修正前那串文字會被當成 int32 直接寫在結構的 Data 指標上，然後交給 ProcessEvent。 |
| 4 | 對照組：一般的 int／float／FString／指標參數照樣 FIRE。 | 全部照舊可用 —— 這步是確認閘門沒有把支援的型別一起擋掉。 |

-----

### ⬜ V8 —— DataTable 下鑽只抓得到前 64 列，畫面要講出來

*優先度 **中** · **需要**：一個列數**超過 64** 的 `UDataTable`。*

| # | 做什麼 | 預期 |
|---|--------|------|
| 1 | 在 Live Walker 走到那個 `UDataTable`，下鑽它的 **RowMap**。 | 麵包屑、標頭、以及下鑽前那一列 RowMap 預覽**三個地方**都有「⚠ showing 64 of N」。<br>⚠ 修正前麵包屑寫的是 DLL 回報的**真實總數**（例如 5000），底下卻只有 64 列，所以「沒抓到」的列看起來像「這張表裡沒有」。 |
| 2 | 看狀態列。 | 說這個檢視每次只抓固定筆數，而且**不會**叫你去調 Array Limit 滑桿 —— 那個滑桿管不到這個檢視，講了就是在第一個假話上再疊一個。 |
| 3 | 對照組：下鑽一個列數 **≤64** 的 DataTable。 | 上面那些字**一個都不出現**。 |

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
