# Wiki zh-TW translation style

The GitHub Wiki (`UE5CEDumper.wiki.git`) keeps three parallel sets: `<Page>.md` (English, the
source), `<Page>-zh-TW.md` and `<Page>-ja-JP.md`. This document is the rulebook for the zh-TW set.

**Why it exists.** The first zh-TW set (2026-06/07) was a literal, clause-by-clause rendering of
the English: Mainland vocabulary (進程、加載、列表、支持、十六進制、剪貼板), word-for-word
calques (無線電按鈕 for *radio button*, 右手分頁 for *right-hand tabs*, 拒絕接觸它 for *refuses to
touch it*) and English sentence order. The maintainer hand-fixed Home / Quick Start / Options
(wiki commits `45dd01b`, `47f5285`, `4acfcc2`, `1320975`, `0a6eec5`) and gave up on the rest,
because every page had the same problem. The glossary below is derived from those hand edits —
they are the ground truth for tone.

## Rules

1. **Translate from the English page, never polish the old zh-TW.** The old text's sentence
   structure is the defect; editing it keeps the defect.
2. **Fix the English first.** A translation of a stale English page is stale in two languages.
   Check every claim that names a button, a column, a count, a default or a file against the code
   before translating.
3. **Write Taiwanese technical Chinese, not Mainland Chinese** — see the glossary. When unsure,
   Microsoft's zh-TW Windows UI wording wins.
4. **Rewrite the sentence, keep the meaning.** Split long English sentences; drop pronouns the
   Chinese does not need; turn nominalised English ("the loading of…") into verbs.
   ⚠ **But do not compress until the subject is ambiguous.** The maintainer's web edits of
   2026-09-26 (Proxy Deploy) put omitted subjects and objects back: 「這個不行」→「這個 Proxy DLL
   不行」, 「選擇此類型時部署」→「UI 選擇 version.dll 類型時部署」, 「滑鼠移過去」→「滑鼠游標移到
   該項目上面」, 「被複製到」→「被複製一份到」. Short sentences, yes — but each one names what it
   is about.
5. **Explain a term the reader will not know** rather than transliterate it — the maintainer added
   explanations in every hand edit (e.g. what flattening a GAS attribute means in CE).
6. **UI element names stay in English, bold**, exactly as the English UI shows them (**Scan
   Steam**, **Force Overwrite**, **Live Walker**). The UI is English-only; a translated button name
   is one the reader cannot find. Page links keep the `-zh-TW` suffix.
7. **Keep code, paths, identifiers, log text and quoted UI messages verbatim** (`version.dll`,
   `Binaries/Win64/`, 「Loaded 15,234 named objects…」).
8. **Pronoun**: prefer none; when one is needed use 您 (the maintainer's choice in Quick Start).
9. **Punctuation**: full-width （）「」：；，。 around Chinese text; list items use 「**term**：說明」,
   not the English em-dash `——` construction.
10. **Numbers**: 400K → 40 萬；ranges 10～30 秒.
11. **Home / Quick Start / Options** carry the maintainer's own wording: re-translate the
    paragraphs the English changed, and keep the maintainer's sentences where the meaning did not
    change.

## Glossary

| English | ✅ Use | ❌ Avoid | Note |
|---|---|---|---|
| process (OS) | 程式、處理程序 | 進程 | |
| load / loader | 載入、載入流程 | 加載、加載程式 | |
| sideload | 載入 | 側載 | maintainer edit |
| list | 清單 | 列表 | |
| support | 支援 | 支持 | |
| hexadecimal | 十六進位 | 十六進制 | |
| clipboard | 剪貼簿 | 剪貼板 | maintainer edit |
| radio button | 選項按鈕 | 無線電按鈕 | |
| checkbox | 核取方塊 | 復選框 | |
| tooltip | 工具提示，或「滑鼠移過去會顯示說明」 | 懸停提示 | |
| collapse / expand (panel) | 收合 / 展開 | 摺疊 | |
| log file | 記錄檔 | 日誌 | |
| registry | 登錄檔 | 註冊表 | |
| Steam library | Steam 收藏庫 | Steam 程式庫 | Steam's own zh-TW UI |
| anti-tamper | 防竄改 | 反竄改 | |
| launcher | 啟動器 | 啟動程式 | |
| named pipe | 具名管道（named pipe） | 管道 | |
| fall back to | 改用 | 回退到 | |
| crash | 當掉、崩潰 | | |
| hardcoded | 寫死 | 硬編碼 | maintainer edit |
| runtime | 執行階段 | 執行時間 | maintainer edit |
| helper | 輔助程式 | 幫助程式 | maintainer edit |
| single | 單一 | 單個 | maintainer edit |
| greyed out / disabled | 禁用、無法選取 | 灰化 | maintainer edit |
| anchor (a root address) | 定錨 | 錨定 | maintainer edit |
| base address | 基底位址 | 基址 | maintainer edit |
| drill (into a pointer) | 展開子節點、深入 | 鑽取 alone | maintainer edit |
| leaf (node) | 數值節點、節點 | 葉節點 | maintainer edit |
| nested | 多層、多重 | 巢狀 | maintainer edit |
| superset | 超集合 | 超集 | maintainer edit |
| survive a restart | 重啟後仍可正常使用 | 存活 | maintainer edit |
| CE memory record | CE 資料欄位、CE 記錄 | 記憶體記錄 | maintainer edit |
| direction chain (SPC) | 走勢鏈 | 方向鏈 | maintainer edit |
| quota | 使用空間配額 | 配額 alone | maintainer edit |
| owns (a field) | 持有 | 擁有 | maintainer edit |
| teleport around | 到處傳送 | 四處跳躍 | maintainer edit |
| offset | 位移 | 偏移量 | project-wide |
| instance | 實例 | | |
| server-side (inside the game DLL) | 由遊戲內的 DLL 執行 | 伺服器端 | there is no server |
| cache | 快取 | 緩存 | |
| substring match | 部分符合 | 子字串 | |
| default | 預設 | 默認 | |
| build (verb / noun) | 建置 | 構建 | |
| settings / config | 設定 | 配置 | |
| file / folder | 檔案 / 資料夾 | 文件 / 文件夾 | |
| memory | 記憶體 | 內存 | |
| program / software | 程式 / 軟體 | 程序 / 軟件 | |
| information | 資訊 | 信息 | |
| occupy / taken (a file name) | 佔用 | 占用 | maintainer's choice 2026-09-26 (MOE lists 占 as standard; this Wiki uses 佔) |

### From the Ministry of Education cross-strait table

Source: 教育部《國語辭典簡編本》附錄〈兩岸常用詞語對照表〉,
<https://dict.concised.moe.edu.tw/appendix.jsp?ID=54&la=0&powerMode=0> (619 pairs; the
maintainer's reference). Only the computing-relevant pairs are copied here; the site is the
authority for anything else. A full local copy of all 7 pages (620 pairs, `注音 / 臺灣語詞 /
大陸語詞` TSV) may exist at `out/moe-cross-strait/moe-cross-strait-table.tsv` — `out/` is
gitignored and machine-local, and the copy is for lookup only, not for redistribution.

| ✅ Taiwan | ❌ Mainland | | ✅ Taiwan | ❌ Mainland |
|---|---|---|---|---|
| 列（row）/ 欄（column） | 行（row）/ 列（column） | | 程式、程式員 | 程序、程序員 |
| 資料、資料庫 | 數據、數據庫 | | 軟體、硬體 | 軟件、硬件 |
| 記憶體 | 存儲器、內存 | | 儲存設備 | 存儲設備 |
| 磁碟、硬碟、隨身碟 | 磁盤、硬盤、U盤 | | 伺服器 | 服務器 |
| 網路、網際網路 | 網絡、互聯網 | | 網站、首頁 | 站點、主頁 |
| 專案 | 項目 | | 作業系統 | 操作系統 |
| 啟動 | 激活 | | 解除安裝 | 卸載 |
| 除錯 | 調試 | | 當機 | 死機 |
| 字元、位元 | 字符、比特 | | 游標 | 光標 |
| 滑鼠 | 鼠標 | | 搖桿 | 手柄 |
| 螢幕 | 屏幕 | | 影片 | 視頻 |
| 解析度 | 分辨率 | | 高畫質 | 高清 |
| 剪貼簿 | 剪貼板 | | 列印、印表機 | 打印、打印機 |
| 晶片、主機板 | 芯片、主板 | | 機率 | 概率 |
| 複製（clone） | 克隆 | | 埠（port） | 端口 |
| 快閃記憶體 | 閃存 | | 寬頻 | 寬帶 |
| 儲值 | 充值 | | 輸入鍵（Enter） | 回車鍵 |

### From the Wikibooks computing-term table (English → 臺灣 → 大陸)

Source: Wikibooks〈大陆台湾计算机术语对照表〉,
<https://zh.wikibooks.org/zh-tw/大陆台湾计算机术语对照表> (CC BY-SA 4.0; 461 terms, keyed by
the ENGLISH word, with Windows vs macOS wording marked). It is the better lookup while
translating, because it starts from the English. A local TSV copy may exist at
`out/wikibooks-cs-terms/wikibooks-cs-terms.tsv` (gitignored, machine-local). The pairs this Wiki
actually uses:

| English | ✅ Taiwan | ❌ Mainland |
|---|---|---|
| thread | 執行緒 | 線程 |
| pointer / array / string | 指標 / 陣列 / 字串 | 指針 / 數組 / 字符串 |
| byte / bit | 位元組 / 位元 | 字節 / 位 |
| stack / heap | 堆疊 / 堆積 | 棧 / 堆 |
| object / class / instance | 物件 / 類別 / 實例 | 對象 / 類 / 實例 |
| function / variable | 函式 / 變數 | 函數 / 變量 |
| parameter / argument | 參數 / 引數 | 參數（形參 / 實參） |
| enumeration / Boolean | 列舉 / 布林 | 枚舉 / 布爾 |
| callback / handle | 回呼 / 控制代碼 | 回調 / 句柄 |
| hash / signature | 雜湊 / 簽章 | 哈希 / 簽名 |
| module / library / package | 模組 / 程式庫 / 套件 | 模塊 / 程序庫 / 包 |
| plugin | 外掛程式、外掛 | 插件 |
| script (incl. Lua / CE) | 指令碼 | 腳本 |
| template | 範本 | 模板 |
| overflow | 溢位 | 溢出 |
| session | 工作階段 | 會話 |
| profile (settings) / profiler | 設定檔 / 效能分析工具 | 配置文件 / 分析器 |
| window / menu / drop-down | 視窗 / 選單 / 下拉式選單 | 窗口 / 菜單 / 下拉菜單 |
| tab | 分頁、索引標籤 | 標籤頁 |
| settings / advanced / apply / add | 設定 / 進階 / 套用 / 加入 | 設置 / 高級 / 應用 / 添加 |
| import / export | 匯入 / 匯出 | 導入 / 導出 |
| built-in | 內建 | 內置 |
| algorithm | 演算法 | 算法 |

Conflicts, decided: **runtime** stays 執行階段 (the maintainer's edit; Wikibooks has 執行期);
**process** stays 處理程序 (Windows Task Manager's zh-TW wording; Wikibooks has 行程); **build**
stays 建置 (Wikibooks' Windows column). **Script** is 指令碼 — the maintainer's call
(2026-09-26), Lua and CE scripts included: here a script is *program code*. 腳本 reads as a
screenplay, and its Mainland sense leans to "simplified / automation" or front-end and game-story
scripting (JavaScript, 劇情腳本, 自動化腳本) — not what a CE Lua script is.

⚠ **列 / 行 is a trap for tables**: in Taiwan a *row* is 列 and a *column* is 欄 (the Mainland
reverses 行/列). Every "Status column" is 「Status 欄」, every "row" is 「列」.
