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
