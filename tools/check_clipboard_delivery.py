"""Refuse a DELIVERY clipboard copy whose result is thrown away.

    py tools/check_clipboard_delivery.py [--list] [--selftest]

THE DEFECT. `IPlatformService.CopyToClipboardAsync` returns whether the text actually
reached the clipboard, and its contract (Core/IPlatformService.cs:30-49) says it NEVER
throws for an ordinary clipboard failure -- so the returned bool is the only signal.
The 2026-09-08 blind-spot sweep classified all 57 call sites: 55 dropped it and 29 then
claimed success anyway. 14 of those 29 are DELIVERY copies, where the clipboard IS the
deliverable: a generated CE script the status line tells the user to paste into Cheat
Engine. When the write does nothing and the app says it worked, the user pastes
WHATEVER WAS ON THE CLIPBOARD BEFORE -- an older AA script from an earlier button press
-- into CE and runs it against a live game.

WHY THIS PREDICATE AND NOT "any discarded result". Measured, not argued:

  * "discarded" alone       55 hits, 29 defects, 26 legitimate -> 47% false positive.
                            Its correct count is 26, i.e. a BASELINE.
  * "discarded AND claimed" needs an allowlist of the 9 sink names the claims land in
                            (StatusText, LookupStatusText, GroupStatusText,
                            DiffStatusText, CoordStatus, _statusLabel.Text,
                            _resultLabel.Text, _log.Info, and a colour literal
                            #4EC9B0) -- and a tenth arrives with the next panel.
  * "discarded AND the payload is a generated script/XML"
                            14 hits, 14 defects, 0 false positives.

The discriminator is real rather than lucky: all 26 legitimate drops copy an ADDRESS, a
NAME or a CLASS NAME -- a convenience the user re-clicks -- and not one of them copies a
script. Losing an address costs a click; losing an AA script gets the wrong script run
against a live game.

⭐ THE RULE BOTH THIS AND GATE 17a ARE BUILT ON: pick a predicate whose LEGITIMATE
population is EMPTY, rather than one whose legitimate population must be enumerated. The
round-1 proposal here was an allowlist of ~25 "effect appliers"; it scored 0 of 10
findings and repeated the failure `check_property_family.py`'s header already records
(G12 shipped "both writers now go through here" when there were THREE). No allowlist and
no baseline: the correct count is 0 and it stays 0, because a new export path that
copies a script and drops the bool is a new violation by construction.

THE FIX at every site is `Helpers/ClipboardDelivery.TryAsync` + `FailureText`, whose own
header carries the delivery-vs-convenience split. Do NOT route the 26 convenience copies
through it -- that is exactly what would turn this check back into a waiver list.

⚠ NOT FLAGGED, deliberately: a convenience copy (an address, a name), and the two
existing correct consumers (`PropertySearchViewModel.cs:736`,
`Views/InvokeParamDialog.cs:921`) -- they test the result, which is the whole point.
"""
from __future__ import annotations

import argparse
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
UI = ROOT / "ui" / "UE5DumpUI"

CALL = re.compile(r"CopyToClipboardAsync\s*\(")

# The payload shapes that make a copy a DELIVERY: a generated CE script / memory-record
# XML. Names taken from the two builders plus the local identifiers the call sites use.
DELIVERY_ARG = re.compile(
    r"\bWrapAaScriptXml\b|\bGenerate\w*Xml\b|\bCheatTableBuilder\b|\bCeXmlExportService\b"
    r"|(?<![A-Za-z0-9_])(xml|script|aaScript|ceXml"
    # ⭐ WIDENED 2026-09-09, and only after MEASURING that the legitimate population stays
    # empty. The shipped set was the identifiers the 14 known defects happened to use, so a
    # future call copying a `lua`/`payload`/`snippet` would have walked straight past --
    # "the correct count is 0 from here on" was true of those names, not of the shape.
    # ⚠ Widening a predicate is exactly how this kind of gate turns into an allowlist, so it
    # was tried FIRST and the tree re-run: the extra names produce **two** new hits and both
    # are the DECLARATION and the DEFINITION of CopyToClipboardAsync itself (its parameter is
    # named `text`). No real call site appears. Those two are excluded by DECLARATION below
    # rather than by dropping the names again.
    r"|lua|luaText|snippet|cheatTable|record|payload|body|text|code|table"
    r")(?![A-Za-z0-9_])", re.I)

# `Task<bool> CopyToClipboardAsync(string text)` -- the interface method and its override, not
# a call. A signature has the RETURN TYPE immediately in front of the name; a call
# never does.
# ⚠ `statement_of` returns the text BEFORE the call -- that is how CONSUMED works
# -- so this must anchor at the END of that prefix rather than match the call itself.
# Written the other way round first, and the selftest said so: both declaration cases
# still reported a hit. A real call cannot collide: `Task<bool> t = _platform.Copy...`
# has a prefix ending in `t = `, not in `>`.
DECLARATION = re.compile(r"\bTask\s*<\s*bool\s*>\s*$")

# The statement CONSUMES the result if any of these appear before the call in it.
#
# ⚠ `if (`/`while (` count as consumption ONLY when the call sits INSIDE the condition
# (`if (!await ...Copy...)`). A leading `if (!sentToCe)` HEAD followed by the call is the
# opposite -- the result is dropped -- and that is the shape of 5 of the 14 real defects
# this gate was written for. The first draft matched the head, reported CHECK OK over a
# tree that still had them, and only the selftest case said so. working-lessons 1.2a,
# and the same mistake gate 17a made twice; peel_heads is the same fix.
CONSUMED = re.compile(r"=|\breturn\b|\bif\s*\(|\bwhile\s*\(|&&|\|\||!await|\?|\bAssert\b")

HEAD = re.compile(r"^\s*(?:\}\s*)?(?:else\s+)?(?:if|for|while|foreach)\s*\(")


def peel_heads(stmt: str) -> str:
    """Strip leading `if (...)` / `for (...)` / `else` heads, parens balanced."""
    while True:
        m = HEAD.match(stmt)
        if not m:
            m2 = re.match(r"^\s*(?:\}\s*)?else\s+", stmt)
            if m2 and m2.end() < len(stmt):
                stmt = stmt[m2.end():]
                continue
            return stmt
        i = stmt.index("(", m.start())
        depth = 0
        for j in range(i, len(stmt)):
            if stmt[j] == "(":
                depth += 1
            elif stmt[j] == ")":
                depth -= 1
                if depth == 0:
                    stmt = stmt[j + 1:]
                    break
        else:
            return stmt


def cs_files():
    for p in sorted(UI.rglob("*.cs")):
        parts = set(p.parts)
        if "obj" in parts or "bin" in parts:
            continue
        yield p


def statement_of(text: str, call_at: int) -> tuple[str, int]:
    """Return (statement text, its start offset) for the statement containing call_at.

    Walks back to the previous `;`, `{` or `}` that is not inside brackets. Walking back
    rather than splitting the whole file keeps multi-line calls intact -- 4 of the 14
    real defects spanned two lines, and a line-based check saw none of them.
    """
    i, depth = call_at, 0
    while i > 0:
        c = text[i - 1]
        if c in ")]":
            depth += 1
        elif c in "([":
            depth -= 1
        elif depth <= 0 and c in ";{}":
            break
        i -= 1
    return text[i:call_at], i


def scan_text(text: str):
    """Yield (offset, statement, arg) for every DELIVERY copy whose result is dropped."""
    for m in CALL.finditer(text):
        stmt, start = statement_of(text, m.start())
        # The method's own signature is not a call. It only became reachable when the payload
        # predicate was widened to include `text` (its parameter's name), so it is skipped
        # here rather than by narrowing the predicate back down.
        if DECLARATION.search(stmt):
            continue
        if CONSUMED.search(peel_heads(stmt)):
            continue                     # bound, tested, or returned
        # the argument list, paren-matched so a nested call comes with it
        i, depth, arg_start = m.end() - 1, 0, m.end()
        while i < len(text):
            if text[i] == "(":
                depth += 1
            elif text[i] == ")":
                depth -= 1
                if depth == 0:
                    break
            i += 1
        arg = text[arg_start:i]
        if not DELIVERY_ARG.search(arg):
            continue                     # a convenience copy: address / name
        yield m.start(), stmt.strip(), " ".join(arg.split())[:120]


def line_of(text: str, off: int) -> int:
    return text.count("\n", 0, off) + 1


def run():
    problems = []
    for p in cs_files():
        text = p.read_text(encoding="utf-8", errors="replace")
        for off, stmt, arg in scan_text(text):
            problems.append((p.relative_to(ROOT).as_posix(), line_of(text, off), arg))
    return problems


# ---------------------------------------------------------------- self-test
# Negative controls per working-lessons 1.2 / 1.2a: the bad cases vary on more than one
# axis (single- vs multi-line, nested builder call, `var x =` vs `if (!await ...)`),
# because 1.2a is the lesson that one passing control validates one axis only.
SELFTEST = [
    ("dropped, single line",
     'await _platform.CopyToClipboardAsync(CheatTableBuilder.WrapAaScriptXml(d, s));', 1),
    ("dropped, multi-line call",
     'await _platform.CopyToClipboardAsync(\n'
     '    Services.CheatTableBuilder.WrapAaScriptXml(description, script));', 1),
    # ⭐ THE WIDENING, and its control. Added 2026-09-09.
    ("dropped, payload named lua -- outside the ORIGINAL name set",
     'var lua = Build();\nawait _platform.CopyToClipboardAsync(lua);', 1),
    ("dropped, payload named payload",
     'await _platform.CopyToClipboardAsync(payload);', 1),
    ("the interface DECLARATION is not a call site",
     'Task<bool> CopyToClipboardAsync(string text);', 0),
    ("the implementation's signature is not a call site either",
     'public Task<bool> CopyToClipboardAsync(string text)\n{\n    return Inner(text);\n}', 0),
    ("a convenience copy of an ADDRESS is still not a delivery",
     'await _platform.CopyToClipboardAsync(row.Address);', 0),

    ("dropped, local named xml",
     'var xml = Build();\nawait _platform.CopyToClipboardAsync(xml);', 1),
    ("dropped inside if (!sentToCe)",
     'if (!sentToCe)\n    await _platform.CopyToClipboardAsync(WrapAaScriptXml(d, s));', 1),
    ("consumed by assignment",
     'copied = await _platform.CopyToClipboardAsync(WrapAaScriptXml(d, s));', 0),
    ("consumed inline in an if",
     'if (!await _platform.CopyToClipboardAsync(WrapAaScriptXml(d, s))) return;', 0),
    ("consumed by return",
     'return await platform.CopyToClipboardAsync(payload);', 0),
    ("convenience copy: an address",
     'await _platform.CopyToClipboardAsync(formatted);', 0),
    ("convenience copy: a name",
     'await _platform.CopyToClipboardAsync(CurrentClassName);', 0),
    ("routed through the helper",
     'if (!await Helpers.ClipboardDelivery.TryAsync(_platform, xml)) return;', 0),
]


def selftest() -> int:
    bad = 0
    for name, src, expect in SELFTEST:
        got = len(list(scan_text(src)))
        ok = got == expect
        bad += 0 if ok else 1
        print("  %-4s %-36s expected %d, got %d" % ("ok" if ok else "FAIL", name, expect, got))
    print("selftest: %d case(s), %d failed" % (len(SELFTEST), bad))
    return 1 if bad else 0


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--list", action="store_true",
                    help="every CopyToClipboardAsync site with its verdict")
    ap.add_argument("--selftest", action="store_true", help="run the negative controls")
    args = ap.parse_args()

    if args.selftest:
        return selftest()

    if args.list:
        total = delivery = 0
        for p in cs_files():
            text = p.read_text(encoding="utf-8", errors="replace")
            bad = {off for off, _, _ in scan_text(text)}
            for m in CALL.finditer(text):
                total += 1
                stmt, _ = statement_of(text, m.start())
                verdict = ("BAD" if m.start() in bad else
                           "ok/consumed" if CONSUMED.search(peel_heads(stmt)) else "ok/convenience")
                if m.start() in bad:
                    delivery += 1
                print("%-14s %s:%d" % (verdict, p.relative_to(ROOT).as_posix(),
                                       line_of(text, m.start())))
        print("\n%d call site(s); %d dropped DELIVERY copies." % (total, delivery))
        return 0

    problems = run()
    if problems:
        print("CHECK FAILED: %d delivery copy(ies) discard the result.\n" % len(problems))
        for f, ln, arg in problems:
            print("  %s:%d\n      payload: %s" % (f, ln, arg))
        print("\nFix: route it through Helpers/ClipboardDelivery and branch on the answer:\n"
              "  if (!await Helpers.ClipboardDelivery.TryAsync(_platform, xml))\n"
              "  {\n"
              "      SetError(Helpers.ClipboardDelivery.FailureText(\"the CE AA script\"));\n"
              "      return;\n"
              "  }")
        return 1

    n = sum(1 for p in cs_files()
            for _ in CALL.finditer(p.read_text(encoding="utf-8", errors="replace")))
    print("CHECK OK: %d clipboard call site(s), every DELIVERY copy checks its result." % n)
    return 0


if __name__ == "__main__":
    sys.exit(main())
