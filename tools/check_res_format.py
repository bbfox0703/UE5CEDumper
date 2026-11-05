#!/usr/bin/env python3
"""Does every en.axaml template a call formats fit that call's arguments?

    py tools/check_res_format.py          # report + exit 1 on a mismatch
    py tools/check_res_format.py --list   # every checked call site, exit 0

[I18N-READINESS-2026-11-04] A template from the string table goes through string.Format (Res.FormatTemplate). A
template whose placeholders do not fit the call -- "{2}" with two arguments, a stray brace -- used to throw
FormatException out of the command setting the line; Res.FormatTemplate now degrades instead, but a degraded line is
still a wrong line, so this gate keeps en.axaml's own templates from reaching it. Checked, per call site:

  * Res.Format("str.X", args...)                         -- key + argument count
  * AobMakerStatus.Say(key, "fallback", args...)          -- and the fallback's placeholders equal en.axaml's
  * Say("str.X", args...) in the files with a StringLookup seam (CallTrace / LiveFuncs view models)

and, for each template found: its highest {n} + 1 equals the argument count (TOO FEW throws; an unused argument is a
lost value), its placeholders have no gap, and no brace is left once "{{", "}}" and the placeholders are taken out.
A key chosen by a ternary between two templates shares one argument list: each template may use a subset of it
(LiveFuncs' trace-memory line adds the free memory only in its "over" form), and the two together must use it all.
A key that is not a literal (or a const naming one) cannot be checked statically: those sites are listed, not failed
-- the repo's rule is that keys stay full literals, which check_axaml_strings enforces from the other side.

A grep-style scan with a small C# lexer (strings, interpolation, comments), no Roslyn: it has to run in CI and on a
bare checkout.
"""
from __future__ import annotations

import html
import os
import re
import sys
from collections import defaultdict

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
UI = os.path.join(ROOT, "ui", "UE5DumpUI")
EN = os.path.join(UI, "Resources", "Strings", "en.axaml")
# The view models whose own Say(key, args) formats a looked-up template (their StringLookup seam).
SAY_SEAM_FILES = {"CallTraceViewModel.cs", "LiveFuncsViewModel.cs"}


# ---------------------------------------------------------------- a small C# lexer
def is_string_start(t: str, i: int) -> bool:
    if t[i] == '"':
        return True
    if t[i] in "$@":
        j = i
        while j < len(t) and t[j] in "$@":
            j += 1
        return j < len(t) and t[j] == '"'
    return False


def scan_string(t: str, i: int) -> int:
    """t[i] starts a string token (with any $ / @ prefix). The index after it."""
    j = i
    interp = verbatim = False
    while t[j] in "$@":
        interp |= t[j] == "$"
        verbatim |= t[j] == "@"
        j += 1
    if t.startswith('"""', j):                      # raw string literal
        k = j
        while k < len(t) and t[k] == '"':
            k += 1
        end = t.find('"' * (k - j), k)
        return end + (k - j) if end >= 0 else len(t)
    k = j + 1
    while k < len(t):
        c = t[k]
        if verbatim:
            if c == '"':
                if t.startswith('""', k):
                    k += 2
                    continue
                return k + 1
        else:
            if c == "\\":
                k += 2
                continue
            if c == '"':
                return k + 1
            if c == "\n":
                return k
        if interp and c == "{":
            if t.startswith("{{", k):
                k += 2
                continue
            k = scan_balanced(t, k)
            continue
        k += 1
    return k


def scan_char(t: str, i: int) -> int:
    k = i + 1
    while k < len(t):
        if t[k] == "\\":
            k += 2
            continue
        if t[k] == "'" or t[k] == "\n":
            return k + 1
        k += 1
    return k


def scan_balanced(t: str, i: int) -> int:
    """t[i] opens a bracket. The index after its match."""
    depth = 0
    k = i
    while k < len(t):
        if is_string_start(t, k):
            k = scan_string(t, k)
            continue
        if t[k] == "'":
            k = scan_char(t, k)
            continue
        if t[k] in "([{":
            depth += 1
        elif t[k] in ")]}":
            depth -= 1
            if depth == 0:
                return k + 1
        k += 1
    return k


def strip_comments(t: str) -> str:
    """// and /* */ comments blanked; strings and line numbers kept."""
    out, k, n = [], 0, len(t)
    while k < n:
        if is_string_start(t, k):
            e = scan_string(t, k)
        elif t[k] == "'":
            e = scan_char(t, k)
        elif t.startswith("//", k):
            e = t.find("\n", k)
            e = n if e < 0 else e
            out.append(" " * (e - k))
            k = e
            continue
        elif t.startswith("/*", k):
            e = t.find("*/", k + 2)
            e = n if e < 0 else e + 2
            out.append(re.sub(r"[^\n]", " ", t[k:e]))
            k = e
            continue
        else:
            out.append(t[k])
            k += 1
            continue
        out.append(t[k:e])
        k = e
    return "".join(out)


def split_args(t: str, open_idx: int) -> list[str]:
    """t[open_idx] == '('. Its top-level arguments."""
    end = scan_balanced(t, open_idx)
    inner = t[open_idx + 1:end - 1]
    args, depth, cur, k = [], 0, [], 0
    while k < len(inner):
        if is_string_start(inner, k):
            e = scan_string(inner, k)
            cur.append(inner[k:e])
            k = e
            continue
        if inner[k] == "'":
            e = scan_char(inner, k)
            cur.append(inner[k:e])
            k = e
            continue
        c = inner[k]
        if c in "([{":
            depth += 1
        elif c in ")]}":
            depth -= 1
        if c == "," and depth == 0:
            args.append("".join(cur).strip())
            cur = []
        else:
            cur.append(c)
        k += 1
    if "".join(cur).strip():
        args.append("".join(cur).strip())
    return args


def cs_files():
    for base, dirs, files in os.walk(UI):
        dirs[:] = [d for d in dirs if d not in {"obj", "bin", ".vs"}]
        for f in sorted(files):
            if f.endswith(".cs"):
                yield os.path.join(base, f)


def rel(p: str) -> str:
    return os.path.relpath(p, ROOT).replace(os.sep, "/")


# ---------------------------------------------------------------- templates
def load_en() -> dict[str, str]:
    raw = open(EN, encoding="utf-8").read()
    return {m.group(1): html.unescape(m.group(2))
            for m in re.finditer(r'<sys:String\s+x:Key="(str\.[^"]+)"[^>]*>(.*?)</sys:String>', raw, re.S)}


PLACEHOLDER = re.compile(r"\{(\d+)(?:,[^}:]*)?(?::[^}]*)?\}")


def placeholders(s: str) -> set[int]:
    return {int(x) for x in PLACEHOLDER.findall(s.replace("{{", "").replace("}}", ""))}


def stray_brace(s: str) -> bool:
    rest = PLACEHOLDER.sub("", s.replace("{{", "").replace("}}", ""))
    return "{" in rest or "}" in rest


LITERAL = re.compile(r'^"((?:[^"\\]|\\.)*)"$')
TERNARY = re.compile(r'^[^?]*\?\s*"(str\.[^"]+)"\s*:\s*"(str\.[^"]+)"\s*$', re.S)
LITERAL_PARTS = re.compile(r'(\s*"(?:[^"\\]|\\.)*"\s*\+?)+', re.S)


def const_keys() -> dict[str, list[tuple[str, str]]]:
    cm: dict[str, list[tuple[str, str]]] = defaultdict(list)
    for p in cs_files():
        for m in re.finditer(r'\bconst\s+string\s+(\w+)\s*=\s*"(str\.[^"]+)"', open(p, encoding="utf-8").read()):
            cm[m.group(1)].append((rel(p), m.group(2)))
    return cm


def resolve_keys(arg: str, path: str, cm) -> list[str]:
    m = LITERAL.match(arg.strip())
    if m:
        return [m.group(1)]
    m = TERNARY.match(arg.strip())
    if m:
        return [m.group(1), m.group(2)]
    name = arg.strip().split(".")[-1]
    if name in cm:
        same = [v for f, v in cm[name] if f == rel(path)]
        if same:
            return same[:1]
        values = {v for _, v in cm[name]}
        if len(values) == 1:
            return list(values)
    return []


def literal_text(arg: str) -> str | None:
    """A literal or a concatenation of literals, as C# reads it (\\" and \\n); None for anything else."""
    if not LITERAL_PARTS.fullmatch(arg):
        return None
    return "".join(re.findall(r'"((?:[^"\\]|\\.)*)"', arg)).replace('\\"', '"').replace("\\n", "\n")


def check(template: str, nargs: int, shared: bool) -> str | None:
    """`shared`: the key is one of a ternary's two, which share one argument list -- each may use a subset of it (the
    other alternative uses the rest; main checks that the two together use every argument)."""
    if stray_brace(template):
        return "a stray brace -- string.Format throws on it"
    ph = placeholders(template)
    need = max(ph) + 1 if ph else 0
    if need > nargs:
        return f"TOO FEW arguments: the template needs {need}, the call passes {nargs}"
    if shared:
        return None
    if need < nargs:
        return f"an unused argument: the template uses {sorted(ph)}, the call passes {nargs}"
    if ph != set(range(need)):
        return f"a gap in the placeholders {sorted(ph)}"
    return None


def main() -> int:
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(errors="backslashreplace")
    en = load_en()
    cm = const_keys()
    findings, unresolved, checked = [], [], []

    for p in cs_files():
        t = strip_comments(open(p, encoding="utf-8").read())
        name = os.path.basename(p)
        patterns = [(r"\bRes\.Format\s*\(", "Res.Format", 1), (r"\bAobMakerStatus\.Say\s*\(", "Say", 2)]
        if name == "AobMakerStatus.cs":
            patterns.append((r"(?<![\w.])Say\s*\(", "Say", 2))
        if name in SAY_SEAM_FILES:
            patterns.append((r"(?<![\w.])Say\s*\(", "Say(key)", 1))
        for pattern, label, fixed in patterns:
            for m in re.finditer(pattern, t):
                if re.search(r"(string|static|private|internal|public)\s+$", t[max(0, m.start() - 20):m.start()]):
                    continue    # the helper's own definition
                args = split_args(t, m.end() - 1)
                line = t.count("\n", 0, m.start()) + 1
                where = f"{rel(p)}:{line} {label}"
                if not args:
                    continue
                keys = resolve_keys(args[0], p, cm)
                if not keys:
                    unresolved.append(f"{where} key={args[0][:60]}")
                    continue
                nargs = len(args) - fixed
                fallback = literal_text(args[1]) if fixed == 2 and len(args) >= 2 else None
                shared = len(keys) > 1
                if shared and all(k in en for k in keys):
                    used = set().union(*(placeholders(en[k]) for k in keys))
                    if used != set(range(nargs)):
                        findings.append(f"{where} {' / '.join(keys)}: the two templates together use {sorted(used)}, "
                                        f"the call passes {nargs} argument(s)")
                for key in keys:
                    if key not in en:
                        findings.append(f"{where} {key}: not in en.axaml")
                        continue
                    checked.append(f"{where} {key} ({nargs} arg(s))")
                    why = check(en[key], nargs, shared)
                    if why:
                        findings.append(f"{where} {key}: {why}")
                    if fallback is not None and placeholders(fallback) != placeholders(en[key]):
                        findings.append(f"{where} {key}: the C# fallback's placeholders {sorted(placeholders(fallback))}"
                                        f" differ from en.axaml's {sorted(placeholders(en[key]))}")

    print(f"en.axaml: {len(en)} keys; {len(checked)} formatted call site(s) checked, "
          f"{len(unresolved)} with a key no scan can resolve")
    if "--list" in sys.argv:
        for c in checked:
            print("  CHECKED   ", c)
        for u in unresolved:
            print("  UNRESOLVED", u)
        return 0
    for u in unresolved:
        print("  (not checked: the key is not a literal) ", u)
    if findings:
        print(f"\nCHECK FAILED: {len(findings)} template(s) do not fit their call. Res.FormatTemplate would degrade "
              "each line to the bare template plus its arguments.")
        for f in findings:
            print("   ", f)
        return 1
    print("CHECK OK: every formatted en.axaml template fits its call's arguments, with no stray brace.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
