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
     "**目前 0 項。** 這一組收的是：任何一款 UE 遊戲都行，但 PASS 要靠人在遊戲裡做一件 Auto 做不到的事。",
     []),
    ("第 3 步 — 遊戲 ＋ Cheat Engine", "遊戲 + Cheat Engine",
     "**目前 0 項。** 這一組收的是：還要開 CE 並載入 `.CT`，而且判定要靠人。\n"
     "\n"
     "> ℹ️ 這兩組空掉不是因為沒人做，是因為做完了 —— 2026-08-24～25 一輪把它們清光，包括三項原本\n"
     "> 排進 CE session 的（Y10 的自我取消勾選、1024-byte params clamp、AA12/AA13 第 4 步）。\n"
     "> ⭐ **三項最後都沒開 CE**：它們主張的是產生腳本的**文字性質**，不是 CE 的執行期行為。\n"
     "> 下次有人想把一列丟進這兩組之前，先問同一個問題。",
     # AA2/AA3 and M1–M5 were DELETED from the checklist 2026-08-24: AA2/AA3 closed end to
     # end ([AA2-CONTRACT-AA3-STOP-2026-08-23] + [AA2-STEP4-CHURN-2026-08-23]) and M1–M5
     # step 1 arm (b) closed ([SEETHRU-ARMS-AB-2026-08-23]) while its arm (a) is not
     # human-only and moved to todo.md. The bucket is empty on purpose; the renderer omits
     # an empty bucket, and the axis comment above says why the row itself stays.
     []),
    ("第 4 步 — 需要特定條件的遊戲", "符合特定條件的遊戲",
     "要先找到符合條件的遊戲，而且要有人在裡面操作或判斷。",
     # MG2 closed in full 2026-08-23 [MG2-CONTAINER-2026-08-23] + the DataTable half
     # once [DTROWMAP]/[DTTEXT] were fixed; its section is gone. V1a survives as step 2
     # only (the NumericAll UX judgement) -- its heading was renamed, so match on the
     # bare key, not the old full title.
     # V8 closed in full 2026-08-23 [V8-DLLHALF / V8-PAINTED]; its one remaining look
     # was done and the leftover ([V8PREVIEWCLIP-2026-08-23]) is a todo.md fix item,
     # not a "a human must judge this" checklist row.
     # B8 deleted 2026-08-24 ([B8-DEFERRED-2026-08-23]). b719's heading was cut down to
     # "b719 —— Property freeze (Route B)" when b636/b637+644/b642 closed and b648 moved to
     # todo.md, so the key had to shorten with it.
     ["V1a", "b719"]),
    ("第 5 步 — 目前沒有可測的環境", "目前沒有",
     "⚠ 這一組**永遠是低優先**，即使登記表寫 MED —— 「找不到樣本」本身就是訊號。",
     # G3 deleted 2026-08-24 ([G3-STAGE-2026-08-23], steps 3+4 closed by staging).
     ["U2"]),
]

CHARTER = """
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
2. **當時留下 10 項。**
3. **CLAUDE.md 原本寫「先改 todo.md，再 mirror」** —— 一個 mirror 指令必然產出翻譯版，已改掉。

⚠ **「10 項」是 2026-08-22 的快照，不是現況** —— 這行原本寫「只留 10 項，就是下面這些」，而下面
早就不是 10 項了。**現況一律看上面那張數出來的表**，或跑 `tools/verify/zhtw_rebuild_buckets.py`。
⚠ 別在這份檔案裡寫下未錨定的計數指令當範例 —— 字面的區塊標記會被自己數進去。這一行原本就犯了：
它內嵌了計數樣式，於是沒有錨定 `^` 的 grep 會多數一項。

⭐ **那 21 項的移出，事後看是這份檔案最有價值的一次改動。** 2026-08-24～25 一輪把它們幾乎清光，
而清掉的方式幾乎都不是「照著步驟做」，是**發現那一列主張的其實是文字或邏輯性質**，於是改寫成離線
測試。⚠ 反過來說，留在這裡的那幾項就是**真的**不行的：它們的 PASS 是人的判斷，或全世界沒有樣本。
把一列丟進來之前，先確認它屬於後者而不是前者。

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
                  "> 重建，它會從檔案本身重新數。\n"
                  ">\n"
                  "> ⚠ **第 0～3 步現在全是 0**（不只第 0、1 步 —— 這行以前只寫到第 1 步，已過期）。空的分組標題\n"
                  "> **不要刪**：重建腳本是照標題分組數的，刪掉標題等於讓那一組從此數不到。",
                  head, count=1, flags=re.S)

    # Write the charter in, IDEMPOTENTLY — and it was not.
    #
    # ⚠ This used to insert unconditionally, so EVERY `--apply` added ANOTHER copy. Six runs
    # left six copies, ~145 lines of a 310-line file. Worse, the newest copy was hand-corrected
    # in the doc afterwards while CHARTER above was never back-ported, so the five older ones
    # had DRIFTED against it and re-running would have re-introduced the stale wording. Same
    # stale-generator shape as scripts/gen_proxy_forwarders.py, found the same day (2026-08-27).
    #
    # Cutting from the FIRST existing charter heading up to the anchor also self-heals a file
    # that already carries duplicates, so the repair does not need a separate one-off script.
    # Normalise the blank lines on both sides rather than relying on CHARTER's own leading /
    # trailing newlines: the replace path cuts at an existing heading (so the text before it
    # already ends in a blank line) while the insert path does not, and the naive form left
    # one extra blank line in exactly one of the two cases.
    anchor = head.find("### 分組是「後勤」")
    existing = head.find(CHARTER.strip().split("\n", 1)[0])
    cut = existing if 0 <= existing < anchor else anchor
    head = head[:cut].rstrip("\n") + "\n\n" + CHARTER.strip("\n") + "\n\n" + head[anchor:]

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
