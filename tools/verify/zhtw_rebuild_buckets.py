r"""Second half of the 2026-08-22 zh-TW restructure: rebuild the bucket headings.

`zhtw_restructure.py` removed 21 sections but left the `## 第 N 步` headings where they were, so the
10 survivors ended up under the wrong ones (AD4 sat under 第 1 步 — "UE5DumpUI only" — when it needs
a game and a fight). This puts each survivor back under its ORIGINAL bucket, drops the buckets that
are now empty, and re-derives the count table from the rebuilt file rather than hand-editing it.

It also writes the CHARTER into the file. That is the actual fix: the file's own text admitted the
words `非人工` / `人工` / `肉眼` appeared nowhere in it, so the "human-only" criterion was a habit
rather than a rule, and habits do not survive a session hand-over.
"""
import io
import re
import sys

sys.stdout.reconfigure(encoding="utf-8", errors="replace")

ZH = "docs/pending-verification_zh-TW.md"
APPLY = "--apply" in sys.argv

BUCKETS = [
    # 2026-08-23: AD4 closed [AD4-CONTESTED-2026-08-23] and its section was deleted,
    # which emptied this bucket. An EMPTY bucket is kept, not removed: the grouping is
    # about LOGISTICS (what has to be set up), and the next row that needs a running
    # game with no CE belongs here. Deleting it would make the next author re-derive
    # the axis. The renderer already omits a bucket with no members from the table.
    ("第 2 步 — 要注入一個執行中的遊戲", "一款執行中的 UE 遊戲 + 注入",
     "任何一款 UE 遊戲都可以，但 PASS 要靠人在遊戲裡做一件 Auto 做不到的事。",
     []),
    ("第 3 步 — 遊戲 ＋ Cheat Engine", "遊戲 + Cheat Engine",
     "還要開 CE 並載入 .CT。",
     ["AA2 / AA3", "M1–M5"]),
    ("第 4 步 — 需要特定條件的遊戲", "符合特定條件的遊戲",
     "要先找到符合條件的遊戲，而且要有人在裡面操作或判斷。",
     # MG2 closed in full 2026-08-23 [MG2-CONTAINER-2026-08-23] + the DataTable half
     # once [DTROWMAP]/[DTTEXT] were fixed; its section is gone. V1a survives as step 2
     # only (the NumericAll UX judgement) -- its heading was renamed, so match on the
     # bare key, not the old full title.
     ["B8（deferred", "V1a", "b719 freeze", "V8"]),
    ("第 5 步 — 目前沒有可測的環境", "目前沒有",
     "⚠ 這一組**永遠是低優先**，即使登記表寫 MED —— 「找不到樣本」本身就是訊號。",
     ["U2", "G3"]),
]

CHARTER = """### ⭐ 這份清單只收「非人工不可」的項目（2026-08-22 重整）

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
"""


def title_of(heading):
    """The item title with the `### ` and the ⬜/🟡/✅ marker stripped, so a key can be
    anchored to the START of it. ⚠ Substring matching is NOT safe here: the key "G3"
    appears inside MG2's heading (which lists "MG1 / MG3 / A2"), and the first version of
    this script silently emitted MG2 twice and dropped G3 entirely. Same shape as
    working-lessons §1.y — match the field, not any occurrence of the string."""
    s = heading[4:].lstrip()
    return re.sub(r"^[⬜🟡✅❌⛔]\s*", "", s)


def strip_trailing_headings(sec):
    """A section captured by splitting on `### ` still carries any `## ` heading that
    followed it. Leaving it in duplicated `## 第 4 步` when the sections were reordered."""
    m = re.search(r"(?m)^## ", sec)
    return sec[:m.start()] if m else sec


def main():
    t = io.open(ZH, encoding="utf-8").read()
    secs = {}
    for s in re.split(r"(?m)^(?=### )", t):
        if s.startswith("### "):
            secs[s.splitlines()[0]] = strip_trailing_headings(s)

    items = secs
    body = []
    counts = []
    for title, needs, blurb, keys in BUCKETS:
        chosen = []
        for k in keys:
            hit = [h for h in items if title_of(h).startswith(k)]
            if len(hit) != 1:
                print(f"⚠ key {k!r} matched {len(hit)} section(s): "
                      f"{[title_of(h)[:40] for h in hit]}")
                return 1
            chosen.append(items[hit[0]])
        counts.append((title, needs, len(chosen)))
        body.append(f"## {title}\n\n{blurb}\n\n" + "".join(chosen))

    # rebuild the count table from what was just assembled
    rows = "\n".join(f"| **{ti}** | {n} | {nd} |" for ti, nd, n in counts)
    table = ("| 分組 | 項目數 | 需要準備 |\n|---|---|---|\n" + rows +
             f"\n| **合計** | **{sum(n for _, _, n in counts)}** | |")

    start = t.find("| 分組 | 項目數 | 需要準備 |")
    end = t.find("\n\n", t.find("| **合計**", start))
    head = t[:start] + table + t[end:]

    # swap the note under the table
    head = re.sub(r"> 這張表是\*\*數出來的\*\*.*?(?=\n\n)",
                  "> 這張表是**數出來的**，不要手改 —— 用 `tools/verify/zhtw_rebuild_buckets.py --apply`\n"
                  "> 重建，它會從檔案本身重新數。第 0、1 步已經整組清空。",
                  head, count=1, flags=re.S)

    # insert the charter right after the 怎麼用這份清單 table note
    anchor = head.find("### 分組是「後勤」")
    head = head[:anchor] + CHARTER + "\n" + head[anchor:]

    tail_start = head.find("## 第 ")
    tail_end = head.find("## 做完一項之後")
    out = head[:tail_start] + "".join(body) + head[tail_end:]

    if not APPLY:
        print(table)
        print(f"\n(dry run) new length {len(out):,}")
        return 0
    io.open(ZH, "w", encoding="utf-8", newline="\n").write(out)
    print(table)
    print(f"\napplied: {len(out):,} chars")
    return 0


if __name__ == "__main__":
    sys.exit(main())
