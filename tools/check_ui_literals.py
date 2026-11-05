#!/usr/bin/env python3
"""Does the UI gain a NEW hard-coded user-visible string where neither older gate looks? A ratchet, not a sweep.

    py tools/check_ui_literals.py              # compare with the baseline; exit 1 on any change
    py tools/check_ui_literals.py --update     # lower the baseline after literals moved out (refuses on growth)
    py tools/check_ui_literals.py --rebaseline # rewrite it regardless: ONLY when the counting rules change
    py tools/check_ui_literals.py --list       # print every counted site, exit 0

[I18N-READINESS-2026-11-04] check_axaml_strings holds the views' markup and check_vm_status_literals the view models'
status lines. English also reaches the screen where neither looks, and nothing stopped NEW text there:

  * code-behind (every .cs under Views/): a control built in C# -- `Text = "..."`, `Content`, `Title`, `Header`,
    `Watermark`, `PlaceholderText` -- and `ToolTip.SetTip(control, "...")`;
  * a message handed to a UI helper, anywhere in Views/ ViewModels/ Helpers/: `ConfirmDialog.ShowAsync(...)`,
    `SetOperationResult(...)`, a `StatusProgress`'s `Complete(...)`, `SetError(...)`;
  * a view model's `ErrorMessage = ...`.

The maintainer's policy (working-lessons §6): no schedule for a second language, so the existing literals stay and
move only when a file is touched for another reason; this gate freezes each file's count so the total only goes down.
Counted: one per site whose argument / right-hand side holds a string with a word in it (two letters; a separator or a
glyph is not text), that is not a resource key ("str.X") and not logic (a comparison operand, a pattern). An
interpolated string's holes are cut out first, so `$"{name}"` is not text. Generators and exporters write artifacts,
not UI, and are not looked at (ArtifactTextTests keeps them off the dictionary instead).
"""
from __future__ import annotations

import json
import os
import re
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from cs_lex import expr_end, split_args, string_tokens, strip_comments  # noqa: E402

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
UI = os.path.join(ROOT, "ui", "UE5DumpUI")
BASELINE = os.path.join(ROOT, "tools", "ui_literals_baseline.json")
FOLDERS = ("Views", "ViewModels", "Helpers")

# Code-behind display properties assigned in C# (Views/ only).
DISPLAY_ASSIGN = re.compile(r"(?<![\w.])(?:\w+\.)?(Text|Content|Title|Header|Watermark|PlaceholderText)\s*=(?![=>])")
# A view model's error line.
ERROR_ASSIGN = re.compile(r"(?<![\w.])ErrorMessage\s*=(?![=>])")
# Calls whose arguments are shown to the user.
UI_CALLS = re.compile(r"(?<![\w])(ToolTip\.SetTip|ConfirmDialog\.ShowAsync|SetOperationResult|SetError|\.Complete)\s*\(")
RES_KEY = re.compile(r"str\.[\w.]+")
WORDISH = re.compile(r"[A-Za-z]{2}")
LOGIC_BEFORE = re.compile(r"(?:==|!=|\bis|\bcase)\s*$")
LOGIC_AFTER = re.compile(r"^\s*(?:==|!=|=>)")


def has_text(expr: str) -> bool:
    for s, e, body in string_tokens(expr):
        if not WORDISH.search(body) or RES_KEY.fullmatch(body):
            continue
        if LOGIC_BEFORE.search(expr[:s]) or LOGIC_AFTER.match(expr[e:]):
            continue
        return True
    return False


def counted(text: str, folder: str) -> list[str]:
    """[`line: what`, ...] for each counted site in one file's comment-stripped text."""
    rows = []

    def row(pos: int, what: str):
        line = text.count("\n", 0, pos) + 1
        rows.append(f"{line}: {what[:110]}")

    if folder == "Views":
        for m in DISPLAY_ASSIGN.finditer(text):
            rhs = text[m.end():expr_end(text, m.end())]
            if has_text(rhs):
                row(m.start(), (m.group(0) + rhs).strip().replace("\n", " "))
    if folder == "ViewModels":
        for m in ERROR_ASSIGN.finditer(text):
            rhs = text[m.end():expr_end(text, m.end())]
            if has_text(rhs):
                row(m.start(), (m.group(0) + rhs).strip().replace("\n", " "))
    for m in UI_CALLS.finditer(text):
        if re.search(r"(void|Task<[^>]*>|Task|static|string)\s*$", text[max(0, m.start() - 40):m.start()]):
            continue    # the helper's own definition
        args = split_args(text, m.end() - 1)
        if any(has_text(a) for a in args):
            row(m.start(), m.group(1) + "(" + ", ".join(args)[:100].replace("\n", " "))
    return rows


def scan() -> dict[str, list[str]]:
    found: dict[str, list[str]] = {}
    for folder in FOLDERS:
        for base, dirs, files in os.walk(os.path.join(UI, folder)):
            dirs[:] = [d for d in dirs if d not in {"obj", "bin"}]
            for name in sorted(files):
                if not name.endswith(".cs"):
                    continue
                path = os.path.join(base, name)
                rel = os.path.relpath(path, ROOT).replace(os.sep, "/")
                rows = counted(strip_comments(open(path, encoding="utf-8").read()), folder)
                if rows:
                    found[rel] = rows
    return found


# The counting rules' controls, run before every scan: a rule that silently stopped counting is a gate that passes.
SELFTEST = [
    ('var b = new Button { Content = "Apply now", Width = 80 };', "Views", 1),
    ('var b = new Button { Content = Res.Get("str.Apply"), Width = 80 };', "Views", 0),
    ('label.Text = $"{name}";', "Views", 0),                                   # a hole is not text
    ('label.Text = $"{count} found";', "Views", 1),
    ('Title = mode == "edit" ? Res.Get("str.A") : Res.Get("str.B");', "Views", 0),   # a comparison operand
    ('ToolTip.SetTip(reset, "Clear this color");', "Views", 1),
    ('ToolTip.SetTip(swatch, $"{name}  #{rgb}");', "Views", 0),
    ('ErrorMessage = "Pipe broke";', "ViewModels", 1),
    ('ErrorMessage = ex.Message;', "ViewModels", 0),
    ('SetOperationResult("Deploy failed", 1);', "ViewModels", 1),
    ('progress.Complete(Res.Format("str.Done", n));', "Helpers", 0),
    ('if (await ConfirmDialog.ShowAsync(owner, "Delete it?", "Delete")) {}', "Views", 1),
    ('label.Text = "x";', "Views", 0),                                         # no word
    ('Text = "Hello";', "ViewModels", 0),                                      # display properties: Views only
]


def selftest() -> list[str]:
    return [f"{src!r} ({folder}): counted {len(counted(src, folder))}, expected {want}"
            for src, folder, want in SELFTEST if len(counted(src, folder)) != want]


def main() -> int:
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(errors="backslashreplace")
    bad = selftest()
    if bad:
        print("CHECK FAILED: the counting rules' own controls failed -- the gate cannot be trusted:")
        for b in bad:
            print("   ", b)
        return 1
    found = scan()
    counts = {k: len(v) for k, v in sorted(found.items())}
    total = sum(counts.values())

    if "--list" in sys.argv:
        for rel, rows in sorted(found.items()):
            for r in rows:
                print(f"  {rel}:{r}")
        print(f"{total} hard-coded UI string site(s) in {len(counts)} file(s)")
        return 0

    baseline: dict[str, int] = {}
    if os.path.exists(BASELINE):
        with open(BASELINE, encoding="utf-8") as fh:
            baseline = json.load(fh)
    grew = [(r, baseline.get(r, 0), counts.get(r, 0)) for r in sorted(set(counts) | set(baseline))
            if counts.get(r, 0) > baseline.get(r, 0)]
    shrank = [(r, baseline.get(r, 0), counts.get(r, 0)) for r in sorted(set(counts) | set(baseline))
              if counts.get(r, 0) < baseline.get(r, 0)]

    if "--update" in sys.argv or "--rebaseline" in sys.argv:
        if grew and "--rebaseline" not in sys.argv:
            print("REFUSED: --update only lowers the baseline, and these files GREW (new UI text belongs in en.axaml):")
            for rel, was, now in grew:
                print(f"    {rel}: {was} -> {now}")
            return 1
        with open(BASELINE, "w", encoding="utf-8", newline="\n") as fh:
            json.dump(counts, fh, indent=2, sort_keys=True)
            fh.write("\n")
        print(f"baseline written: {total} site(s) in {len(counts)} file(s)"
              + (" (--rebaseline: the counting rules changed)" if "--rebaseline" in sys.argv else ""))
        return 0

    rc = 0
    if grew:
        rc = 1
        print("FAIL: new hard-coded UI text. It belongs in en.axaml, read with Res.Get / Res.Format")
        print("(working-lessons §6: the existing literals are frozen per file; new ones are not allowed).")
        for rel, was, now in grew:
            print(f"    {rel}: {was} -> {now}")
            for r in found.get(rel, []):
                print(f"        {r}")
    if shrank:
        rc = 1
        print("FAIL: a file has FEWER hard-coded UI strings than the baseline -- good; lower it:")
        print("    py tools/check_ui_literals.py --update")
        for rel, was, now in shrank:
            print(f"    {rel}: {was} -> {now}")
    if rc == 0:
        print(f"CHECK OK: {total} grandfathered hard-coded UI string site(s) in {len(counts)} file(s), none added.")
    return rc


if __name__ == "__main__":
    raise SystemExit(main())
