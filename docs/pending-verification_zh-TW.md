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
| **第 2 步 — 要注入一個執行中的遊戲** | 0 | 一款執行中的 UE 遊戲 + 注入 |
| **第 3 步 — 遊戲 ＋ Cheat Engine** | 2 | 遊戲 + Cheat Engine |
| **第 4 步 — 需要特定條件的遊戲** | 4 | 符合特定條件的遊戲 |
| **第 5 步 — 目前沒有可測的環境** | 2 | 目前沒有 |
| **合計** | **8** | |

> 這張表是**數出來的**，不要手改 —— 用 `tools/verify/zhtw_rebuild_buckets.py --apply`
> 重建，它會從檔案本身重新數。第 0、1 步已經整組清空。

### ⭐ 這份清單只收「非人工不可」的項目（2026-08-22 重整）

**判準只有一條，而且要能被檢查:**

> **一列留在這裡，當且僅當「Auto + Computer Use 沒辦法從頭到尾自己跑完」** ——
> 需要人在遊戲裡做 Auto 做不到的動作、需要人用眼睛下判斷、或全世界根本沒有樣本。

⚠ **這條判準以前不存在於這個檔案裡**，只存在於選材時的習慣 —— 檔案自己都寫過
「`非人工`、`人工`、`肉眼` 這幾個字在本檔案裡一個都沒有」。**沒有寫下來的規則不會活過一次交接**，
於是它慢慢變成 [todo.md](todo.md) 登記表的中文副本:重整前有 **31 項**、其中 **20 項**帶著證據標記
(finding tag、`file:line`、log 行、日期化的 ✅)，平均每項 913 字。

**重整做了三件事**（2026-08-22）:
1. **21 項移回 [todo.md](todo.md)** —— 它們 Auto + Computer Use 跑得完（開 UI、走 pipe、grep log、
   離線工具）。步驟表格**原封不動**搬過去，沒有刪掉任何東西，見那份文件的
   「Verification steps migrated from the 繁中 checklist」一節。
2. **只留 10 項**，就是下面這些。
3. **CLAUDE.md 原本寫「先改 todo.md，再 mirror」** —— 一個 mirror 指令必然產出翻譯版，已改掉。

⛔ **要加新項目之前先問**:Auto + Computer Use 跑得完嗎？跑得完就寫進 todo.md，不要寫在這裡。
⛔ **不要把證據寫進步驟表格。** 這裡只放**做什麼**和**預期看到什麼**；證據、成因、finding tag
一律進 todo.md。這正是它上次走樣的方式。

### ⭐ 這份清單只收「非人工不可」的項目（2026-08-22 重整）

**判準只有一條，而且要能被檢查:**

> **一列留在這裡，當且僅當「Auto + Computer Use 沒辦法從頭到尾自己跑完」** ——
> 需要人在遊戲裡做 Auto 做不到的動作、需要人用眼睛下判斷、或全世界根本沒有樣本。

⚠ **這條判準以前不存在於這個檔案裡**，只存在於選材時的習慣 —— 檔案自己都寫過
「`非人工`、`人工`、`肉眼` 這幾個字在本檔案裡一個都沒有」。**沒有寫下來的規則不會活過一次交接**，
於是它慢慢變成 [todo.md](todo.md) 登記表的中文副本:重整前有 **31 項**、其中 **20 項**帶著證據標記
(finding tag、`file:line`、log 行、日期化的 ✅)，平均每項 913 字。

**重整做了三件事**（2026-08-22）:
1. **21 項移回 [todo.md](todo.md)** —— 它們 Auto + Computer Use 跑得完（開 UI、走 pipe、grep log、
   離線工具）。步驟表格**原封不動**搬過去，沒有刪掉任何東西，見那份文件的
   「Verification steps migrated from the 繁中 checklist」一節。
2. **只留 10 項**，就是下面這些。
3. **CLAUDE.md 原本寫「先改 todo.md，再 mirror」** —— 一個 mirror 指令必然產出翻譯版，已改掉。

⛔ **要加新項目之前先問**:Auto + Computer Use 跑得完嗎？跑得完就寫進 todo.md，不要寫在這裡。
⛔ **不要把證據寫進步驟表格。** 這裡只放**做什麼**和**預期看到什麼**；證據、成因、finding tag
一律進 todo.md。這正是它上次走樣的方式。

### ⭐ 這份清單只收「非人工不可」的項目（2026-08-22 重整）

**判準只有一條，而且要能被檢查:**

> **一列留在這裡，當且僅當「Auto + Computer Use 沒辦法從頭到尾自己跑完」** ——
> 需要人在遊戲裡做 Auto 做不到的動作、需要人用眼睛下判斷、或全世界根本沒有樣本。

⚠ **這條判準以前不存在於這個檔案裡**，只存在於選材時的習慣 —— 檔案自己都寫過
「`非人工`、`人工`、`肉眼` 這幾個字在本檔案裡一個都沒有」。**沒有寫下來的規則不會活過一次交接**，
於是它慢慢變成 [todo.md](todo.md) 登記表的中文副本:重整前有 **31 項**、其中 **20 項**帶著證據標記
(finding tag、`file:line`、log 行、日期化的 ✅)，平均每項 913 字。

**重整做了三件事**（2026-08-22）:
1. **21 項移回 [todo.md](todo.md)** —— 它們 Auto + Computer Use 跑得完（開 UI、走 pipe、grep log、
   離線工具）。步驟表格**原封不動**搬過去，沒有刪掉任何東西，見那份文件的
   「Verification steps migrated from the 繁中 checklist」一節。
2. **只留 10 項**，就是下面這些。
3. **CLAUDE.md 原本寫「先改 todo.md，再 mirror」** —— 一個 mirror 指令必然產出翻譯版，已改掉。

⛔ **要加新項目之前先問**:Auto + Computer Use 跑得完嗎？跑得完就寫進 todo.md，不要寫在這裡。
⛔ **不要把證據寫進步驟表格。** 這裡只放**做什麼**和**預期看到什麼**；證據、成因、finding tag
一律進 todo.md。這正是它上次走樣的方式。

### ⭐ 這份清單只收「非人工不可」的項目（2026-08-22 重整）

**判準只有一條，而且要能被檢查:**

> **一列留在這裡，當且僅當「Auto + Computer Use 沒辦法從頭到尾自己跑完」** ——
> 需要人在遊戲裡做 Auto 做不到的動作、需要人用眼睛下判斷、或全世界根本沒有樣本。

⚠ **這條判準以前不存在於這個檔案裡**，只存在於選材時的習慣 —— 檔案自己都寫過
「`非人工`、`人工`、`肉眼` 這幾個字在本檔案裡一個都沒有」。**沒有寫下來的規則不會活過一次交接**，
於是它慢慢變成 [todo.md](todo.md) 登記表的中文副本:重整前有 **31 項**、其中 **20 項**帶著證據標記
(finding tag、`file:line`、log 行、日期化的 ✅)，平均每項 913 字。

**重整做了三件事**（2026-08-22）:
1. **21 項移回 [todo.md](todo.md)** —— 它們 Auto + Computer Use 跑得完（開 UI、走 pipe、grep log、
   離線工具）。步驟表格**原封不動**搬過去，沒有刪掉任何東西，見那份文件的
   「Verification steps migrated from the 繁中 checklist」一節。
2. **只留 10 項**，就是下面這些。
3. **CLAUDE.md 原本寫「先改 todo.md，再 mirror」** —— 一個 mirror 指令必然產出翻譯版，已改掉。

⛔ **要加新項目之前先問**:Auto + Computer Use 跑得完嗎？跑得完就寫進 todo.md，不要寫在這裡。
⛔ **不要把證據寫進步驟表格。** 這裡只放**做什麼**和**預期看到什麼**；證據、成因、finding tag
一律進 todo.md。這正是它上次走樣的方式。

### 分組是「後勤」，不是「誰判定」—— 兩條軸要分開（2026-08-22 修正）

上面那張 **第 0～5 步** 的表回答的是「這一項要先準備多少東西」。那是**後勤軸**，拿來排一場
session 的順序很好用，保留。

⚠ **但它一直被當成第二件事在用，而那是錯的。** 這份清單當初的選材標準是「**非人工不可**的項目」，
於是「要有人在遊戲裡挨打」被記成「要有人用眼睛判斷過了沒」。**那是兩回事。**
維護者 2026-08-21 講得更直接：

> 「我發覺我測的那些東西在 Auto + Computer Use 下其實是做得到。
> 『沒有記下條件的數字不算量測』這就是人工的缺點了，人工是用肉眼看，沒有證據，
> 這也是我說人工測不可靠的原因。」

ℹ️ 順帶一提：`非人工`、`人工`、`肉眼` 這幾個字在本檔案裡**一個都沒有**（grep 是空的）。
那個前提從來不是寫在這裡的規則，是**選材時的心態**——所以它才會沒人發現地一直生效。

**規則：用「判定軸」分類，「操作軸」只當標籤，永遠不要讓操作方式決定分類。**

| | 判定軸（PASS 由什麼settle） | 證據 | 這一格自己的失效方式 |
|---|---|---|---|
| **D0** | 純測試／對原始碼、AXAML、產生文字的靜態斷言。不啟動任何行程 | `ui/UE5DumpUI.Tests` 裡一條綠的斷言，任何機器、CI 都能重跑 | **可以字串答對、程式錯**。斷言一個 formatter 不證明它被呼叫、更不證明輸出綁進了視覺樹——就是鐵則 4。所以 D0 通常**留下殘餘**而不是直接關掉一列 |
| **D1** | 行程級但無 GUI：headless pipe client、DLL log grep、PE／binary 探測、檔案系統 | 線上下來的 JSON、**用格式字串**比對到的 log 行、檔案位元組 | **缺席不算證據，除非先證明那個通道帶得動這件事**（鐵則 2）。grep 回 0 和「指令根本沒送出去」「DLL 還沒 flush」「這個 log 分類 route 到別的檔」長得一模一樣 |
| **D2** | **自動化操作 + 對「留得下來的東西」斷言**：剪貼簿、log 行、匯出檔、pipe 讀回的值、Win32 window rect、從 CE Lua Engine 讀 `memrec.Active` | 那份被存下來的成品本身 | ⭐ **這一格就是解決維護者抱怨的那一格**——成品正是肉眼留不下來的證據。但**成品和畫面可能互相矛盾**，那正是這份清單要抓的缺陷跑到量測儀器上；此時用兩個偵測器 |
| **D3** | 自動化操作，但 PASS 靠**畫面像素**判斷 | 截圖 | 判讀本身就是判斷。**先想想能不能降到 D2**——多數「看得到某串字」其實可以改讀 log 或剪貼簿 |
| **D4** | 真的需要人對「遊戲看起來對不對」下判斷，沒有任何成品能定案 | 人的判斷 | 這一格要**很小**。本檔案目前只有一處誠實宣告「純 UX 判斷，沒有機械式 PASS 線」，那才是真 D4 |
| **D5** | 全世界都沒有樣本 | 沒有 | 缺樣本本身就是「這種遊戲很罕見」的證據，見第 5 步 |

⭐ **「要有人點」和「要有人判斷」不一樣。點擊可以自動化,判斷常常不行。** 一列看起來像 D4、
但只要把斷言搬到某個**留得下來的成品**上就變 D2 —— 那種重新分類最值錢，因為它把「沒有證據的通過」
換成「有證據的通過」。

▶ **不要為了套這張表去大改既有的 50 個小節。** 每次真的動到某一列時，順手把它的判定軸寫進那一列
就好；一次性重新分桶會弄丟現有的條件說明，那些比分類重要。

### ⚠ 四條會害人記錯結果的鐵則

1. **PASS 條件是「某個東西不出現」時，一定要跑反方向那一次。**
   「不存在」是全世界最容易誤打誤撞產生的結果 —— 沒跑對照組，你證明的是「沒測到」而不是「通過」。
2. **空的 grep 不是證據。** 先確認指令真的送出去了（去 `ui-pipe-0.log` 找那個 cmd），
   再確認 DLL 已經 flush（看 log 檔大小還在不在長）。
3. **拿修正時用的那份清單去驗那個修正，等於沒驗。**
   清單型的修正要拿「世界」去驗，不是拿它自己的清單。
4. **閘門答對 ≠ 使用者看得到。** PASS 條件只要是「畫面上會出現某串字」，就一定要真的確認那串字**到得了使用者眼前**。
   ⚠ 2026-08-22 補強：「真的去看」不等於「一定要用眼睛」——把那串字從**剪貼簿、log、匯出檔**撈出來斷言，證據力比看一眼強，因為留得下來（見上表 D2）。
   但 D0 的測試**不算**：它讀的是檔案或函式，不是 merge 後、綁定後、排版後的畫面。同一天的實例——快照那句「結果被截斷」的提示，VM 字串完全正確，卻被放在沒有 `TextWrapping` 也沒有 `ToolTip` 的 `TextBlock` 裡，自己被截斷。

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

## 第 2 步 — 要注入一個執行中的遊戲

任何一款 UE 遊戲都可以，但 PASS 要靠人在遊戲裡做一件 Auto 做不到的事。

## 第 3 步 — 遊戲 ＋ Cheat Engine

還要開 CE 並載入 .CT。

### ⬜ AA2 / AA3 —— 凍結能撐過死亡/重生並在失聯時自行停手

*build 2926 · 優先度 **中***

| # | 做什麼 | 預期 |
|---|---|---|
| 1 | 先做反向對照：注入舊版 DLL，配上新的 helper，啟動凍結。 | 必須被拒絕並顯示「the DLL is older than this script」。<br>⚠ 若照跑不誤，代表 contract 檢查沒生效，以下步驟全部無意義。 |
| 4 | 維持凍結，製造 churn：把凍結中的 actor 打死重生，或跨越 level streaming 邊界。 | 約一次 rescan（~5 秒）內重新接上；且沒有任何不相干物件的欄位被改動。 |
| 5 | AA3：凍結執行中把 DLL 卸載/重新注入，讓 rescan 永久失敗。 | ~15 秒內 Lua console 印出一次「... consecutive rescans failed -- freeze STOPPED writing」，之後不再寫入。 |

### ⬜ M1–M5 步驟 1 —— See-through 開著時關閉遊戲，只剩 (a)(b) 兩種關法

*優先度 **中** · 需要：有人在遊戲裡移動，或有辦法把遊戲弄到沒有回應*

ℹ️ 同一列的 arm (c)(d) 與 M1–M5 步驟 2/3/4/5 都已通過；證據、finding id 與 rig 用法在 todo.md
（`[SEETHRUNOOP-2026-08-22]`、`[SEETHRUTALLY-2026-08-22]`、`[SOLIDEHOLD-2026-08-22]`）。

| # | 做什麼 | 預期 |
|---|---|---|
| a | Teleport 分頁開啟 See-through，確認 `hidden_count > 0`。接著在遊戲裡**持續移動**（讓 worker 正在 trace／隱藏／還原），**移動中**用視窗右上 ✕ 關閉遊戲。 | 遊戲乾淨結束。<br>log 沒有 `tick threw`；Windows 事件檢視器「Windows 記錄 → 應用程式」**零新增**錯誤。<br>⚠ `taskkill /F` 不算：那條路徑根本不會跑 DLL 的關閉流程，這一列要防的就是 `std::terminate` / `0xC0000409`。 |
| b | 同樣開著 See-through，把遊戲弄到**畫面卡住／沒有回應**（大量載入、或按住標題列拖著不放），在卡住狀態下關閉遊戲視窗。 | 同上：乾淨結束、log 沒有 `tick threw`、應用程式記錄零新增。<br>⚠ 若跳出「這個程式沒有回應」而你按了「結束工作」，等同 `taskkill /F`，不算通過。 |

## 第 4 步 — 需要特定條件的遊戲

要先找到符合條件的遊戲，而且要有人在裡面操作或判斷。

### ⬜ B8（deferred 半） —— 遊戲執行緒安靜時關 Fly 仍會補回碰撞

*優先度 **低** · 需要：背景時真的會停止 tick 的遊戲（有吃 t.IdleWhenNotForeground）。Elliot 背景仍在 tick，測不到。*

| # | 做什麼 | 預期 |
|---|---|---|
| 1 | Teleport 分頁 → Fly ON + Noclip → 飛穿一道牆 → alt-tab 切到 UI 等超過 500 ms → 按 Disable。 | Disable 有被按到（不是靠關遊戲觸發）。<br>⚠ 關閉遊戲永遠測不到這半：關遊戲不會呼叫 UE5_Shutdown，Fly 的 disable 根本不執行。 |
| 2 | grep `walk-0.log` 的 `Fly:`。 | 出現 `Fly: DISABLED but the pawn's collision is still OFF (game thread unresponsive)`，切回遊戲後再出現 `Fly: game thread resumed after N ms — pawn collision restored`。FAIL = 只有一行 `Fly: DISABLED`，之後角色掉出世界。<br>⚠ 不在 init-0.log；Dunste 的 LOG_CAT 被路由到 walk。 |
| 3 | 回遊戲撞牆，並順便檢查 `Fly: collision disable deferred`。 | 角色被牆擋住；`deferred` 那行可以出現，但每次 stall 只能出現一次，不能連續刷。 |

### ⬜ V1a 第 2 步 —— NumericAll 結果量的橘色警告（第 1 步已於 2026-08-23 關閉）

*優先度 **低** · 需要：人用眼睛判斷「這個數量還能不能用」*

ℹ️ 容器重新配置那一步（原第 1 步）已由 `tools/verify/v1a_container_realloc.py` 在 DumperTest 上關閉，
証據在 todo.md `[V1A-REALLOC-2026-08-23]`；順手找到並修掉了 `[V1AEMPTY-2026-08-23]`。

| # | 做什麼 | 預期 |
|---|---|---|
| 2 | 選 NumericAll 掃一個 0 / 1 / 255 這類小值。 | 橘色結果量警告出現，且結果數量還在人可以用的範圍。<br>⚠ 這一格是純 UX 判斷，沒有機械式 PASS 線——「警告有沒有出現」可以自動測，「數量能不能用」不行。 |

### ⬜ b719 freeze / b648 PE / b636 fast path / b642 FPROPERTY_FLAGS / b637+644 return value —— 舊版 invoke、回傳值與屬性凍結的一次性複查

*優先度 **低** · 需要：ES2 (UE5.5) 與 Geri (UE4.27)；屬性凍結那項要一款 NPC 會重生的遊戲（首選 Geri）。*

| # | 做什麼 | 預期 |
|---|---|---|
| 1 | 在會重生 NPC 的遊戲上開 Property freeze (Route B)，觀察一段時間。 | tick 對 FPS 的影響可接受、重生時有重新掃描、換場景後 vtable liveness 守衛擋得住、多腳本並存不打架。 |
| 2 | 在 ES2 (UE5.5) 與 Geri (UE4.27) 各做一次 instance invoke。 | log 出現 `GameThreadDispatch: validation OK — hook fired N times`，以前 timeout 回 `-5` 的 invoke 現在會成功。 |
| 3 | 在活躍 session 比較 static-native PE fast path 與 game-thread dispatch 的延遲。 | 有狀態的 UFunction 仍走 dispatch，不會誤落進 fast path。 |
| 5 | 各做一次 pointer-return 與 FString-return 的 invoke。 | pointer 回傳顯示 `0x` 前綴；FString 回傳顯示 "see After: dump above" 提示。 |

### 🟡 V8 —— DataTable 下鑽只抓得到前 64 列（**只剩「畫面真的印出來了嗎」**）

*優先度 **低**（原為中）· 需要：一個列數**超過 64** 的 `UDataTable`*

⚠ **內容對不對已經全部由測試釘住**（清單在 todo.md「V8 — what the tests already pin」）。
但那些測試斷言的是 **ViewModel 的字串**，不是畫面上的像素 —— 所以還是要有人看一眼。

| # | 做什麼 | 預期 |
|---|--------|------|
| 1 | 手上若剛好有列數 >64 的 `UDataTable`，在 Live Walker 下鑽它的 RowMap，**只看一眼**：那三處字串有沒有真的顯示出來、有沒有被截掉或蓋住。 | 三處都看得到完整的「⚠ showing 64 of N」。<br>ℹ️ 內容對不對已經有測試在管，這一步只回答「印出來了嗎」。 |

-----

## 第 5 步 — 目前沒有可測的環境

⚠ 這一組**永遠是低優先**，即使登記表寫 MED —— 「找不到樣本」本身就是訊號。

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
