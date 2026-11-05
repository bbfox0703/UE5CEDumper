#!/usr/bin/env python3
"""A small C# lexer for the UI-string gates: strings (plain, verbatim, interpolated, raw), char literals, comments,
balanced brackets, call arguments and the end of an expression. No Roslyn -- the gates run in CI and on a bare
checkout. Shared by check_res_format.py and check_ui_literals.py, so the two read C# the same way.
"""
from __future__ import annotations

import re


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


def expr_end(t: str, i: int) -> int:
    """The end of the expression starting at t[i]: a depth-0 ',' or ';', or the ')' / '}' / ']' that closes the
    construct it sits in (an object initializer's next member, a call's next argument)."""
    depth = 0
    k = i
    while k < len(t):
        if is_string_start(t, k):
            k = scan_string(t, k)
            continue
        if t[k] == "'":
            k = scan_char(t, k)
            continue
        c = t[k]
        if c in "([{":
            depth += 1
        elif c in ")]}":
            if depth == 0:
                return k
            depth -= 1
        elif c in ",;" and depth == 0:
            return k
        k += 1
    return k


def string_tokens(expr: str):
    """(start, end, text) for each string token in expr; an interpolated string's holes are cut out of its text."""
    k = 0
    while k < len(expr):
        if is_string_start(expr, k):
            e = scan_string(expr, k)
            token = expr[k:e]
            q = token.index('"')
            body = token[q + 1:-1] if token.endswith('"') else token[q + 1:]
            if "$" in token[:q]:
                body = _cut_holes(body)
            yield k, e, body
            k = e
            continue
        if expr[k] == "'":
            k = scan_char(expr, k)
            continue
        k += 1


def _cut_holes(body: str) -> str:
    out, k = [], 0
    while k < len(body):
        if body.startswith("{{", k) or body.startswith("}}", k):
            out.append(body[k])
            k += 2
            continue
        if body[k] == "{":
            k = scan_balanced(body, k)
            out.append(" ")
            continue
        out.append(body[k])
        k += 1
    return "".join(out)
