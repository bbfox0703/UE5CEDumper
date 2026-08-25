r"""One-shot: restore `pending-verification_zh-TW.md` to its original charter.

    py tools/verify/zhtw_restructure.py --dry
    py tools/verify/zhtw_restructure.py --apply

⭐ WHY. The file's charter was "only what a HUMAN must verify, and steps only". It had drifted into
a second copy of `todo.md`'s register: 31 items, **20 of them carrying evidence markers** (finding
tags, `file:line`, log quotes, dated ✅), averaging ~913 chars per item. Three things caused it and
all three are fixable: CLAUDE.md literally instructed "edit todo.md first, then **mirror**"; the
"human-only" criterion was never written down *inside* the file (its own text notes that the words
`非人工` / `人工` / `肉眼` appear nowhere in it); and every session appended evidence into the steps
table because that is where the row was.

**The rule applied here — one line, checkable:** a row STAYS only if it cannot be completed without
a human present, i.e. an in-game action Auto + computer-use cannot perform, a judgement only a
person can make, or no fixture exists anywhere. Everything else MOVES to `todo.md`.

⚠ **Nothing is deleted.** Every section that moves is appended to `todo.md` VERBATIM first, under one
clearly-marked section, so the operational steps survive the move. This script is idempotent-hostile
by design: run it once, check the diff, commit.
"""
import io
import re
import sys

sys.stdout.reconfigure(encoding="utf-8", errors="replace")

ZH = "docs/pending-verification_zh-TW.md"
TD = "docs/todo.md"
APPLY = "--apply" in sys.argv

# Rows that STAY: a human must be present. Keyed by a distinctive prefix of the heading.
STAY = [
    ("AD4",        "戰鬥中實際挨打才會出現的旗標爭用"),
    ("AA2 / AA3",  "把凍結中的 actor 打死重生"),
    ("M1–M5",      "要有人移動／卡住遊戲的那兩條 arm"),
    ("MG2",        "要在遊戲裡移除容器裡的一筆"),
    ("B8（deferred", "要真的飛穿牆再回去撞牆"),
    ("V1a",        "要在遊戲裡讓容器重新配置"),
    ("b719 freeze", "要有會重生 NPC 的遊戲並持續觀察"),
    ("V8",         "純畫面判斷:那三處字串有沒有真的印出來"),
    ("U2",         "全世界沒有 CPN 樣本"),
    ("G3",         "本機沒有可觸發的環境"),
]


def heading_of(sec):
    return sec.splitlines()[0]


def stays(sec):
    h = heading_of(sec)
    for key, _ in STAY:
        if key in h:
            return key
    return None


def main():
    zh = io.open(ZH, encoding="utf-8").read()
    td = io.open(TD, encoding="utf-8").read()

    head_end = zh.find("\n## 第 ")
    parts = re.split(r"(?m)^(?=### )", zh)
    prefix = parts[0]
    secs = parts[1:]

    # sections before the first 第 N 步 heading are the "how to use" block — keep them attached
    keep_meta = [s for s in secs if zh.find(s) < head_end]
    items = [s for s in secs if zh.find(s) >= head_end]

    stay, move = [], []
    for s in items:
        (stay if stays(s) else move).append(s)

    print(f"items {len(items)}  ->  STAY {len(stay)}   MOVE {len(move)}\n")
    print("STAY:")
    for s in stay:
        print(f"   {heading_of(s)[4:88]}")
    print("\nMOVE to todo.md:")
    for s in move:
        print(f"   {heading_of(s)[4:88]}")

    missing = [k for k, _ in STAY if not any(k in heading_of(s) for s in stay)]
    if missing:
        print(f"\n⚠ STAY keys that matched nothing: {missing}")
        return 1

    if not APPLY:
        print("\n(dry run — pass --apply to write)")
        return 0

    # ---- 1. append the moved sections to todo.md, verbatim ----
    block = [
        "\n-----\n",
        "## Verification steps migrated from the 繁中 checklist (2026-08-22)\n\n",
        "These are the operational `做什麼 | 預期` tables for items that **do not need a human**:\n",
        "Auto + computer-use can drive them end to end (UI clicking, the pipe, log greps, offline\n",
        "tools). They lived in [`pending-verification_zh-TW.md`](pending-verification_zh-TW.md), whose\n",
        "charter is *only what a human must verify* — carrying them there had turned that file into a\n",
        "second copy of this register.\n\n",
        "⚠ **Moved VERBATIM, including the ✅/🟡 status cells**, so no evidence was lost in the move.\n",
        "Where a step is already marked done, it is done — this is not a fresh queue.\n\n",
        "⭐ **These are still open verification work**; they are tracked by the item ids that already\n",
        "appear elsewhere in this file. What changed is only where the STEPS live.\n\n",
    ]
    td = td.rstrip("\n") + "\n" + "".join(block) + "\n".join(s.rstrip() + "\n" for s in move)
    io.open(TD, "w", encoding="utf-8", newline="\n").write(td)

    # ---- 2. rewrite the zh-TW file with only the STAY rows ----
    out = prefix + "".join(keep_meta) + "".join(stay)
    io.open(ZH, "w", encoding="utf-8", newline="\n").write(out)
    print(f"\napplied: todo.md +{len(''.join(move)):,} chars, zh-TW now {len(out):,} chars")
    return 0


if __name__ == "__main__":
    sys.exit(main())
