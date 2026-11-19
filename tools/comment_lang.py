#!/usr/bin/env python3
"""Comments and comment-free code for the languages beside C++ / C#: XAML, Lua (and Cheat Engine .CT), PowerShell,
Python. check_comment_history counts history in these comments; tools/verify/comment_only_diff proves an edit
touched only them.

Each language has two functions on a file's text:
  comments(...) -> [(line number, comment text)], one entry per comment LINE;
  code(...)     -> the code with every comment removed, as a list of lines (blank lines dropped, whitespace runs
                   collapsed) -- two versions of a file whose code() lists are equal differ in comments only.
They are scanners, not parsers, except for Python, where the standard tokenizer and ast do the work. What a scanner
does not model it treats as code, so a misreading can only make code() report a difference, never hide one.
"""
from __future__ import annotations

import ast
import io
import re
import tokenize

XML_EXTS = (".axaml", ".xaml")
LUA_EXTS = (".lua", ".ct")
PS1_EXTS = (".ps1", ".psm1")
PY_EXTS = (".py",)
EXTS = XML_EXTS + LUA_EXTS + PS1_EXTS + PY_EXTS


def handles(path: str) -> bool:
    return path.lower().endswith(EXTS)


def _collapse(lines: list[str]) -> list[str]:
    out = []
    for line in lines:
        s = re.sub(r"\s+", " ", line).strip()
        if s:
            out.append(s)
    return out


# ---- XAML / XML: <!-- ... --> --------------------------------------------------------------------------------------

_XML_COMMENT = re.compile(r"<!--(.*?)-->", re.S)


def xml_comments(text: str) -> list[tuple[int, str]]:
    out = []
    for m in _XML_COMMENT.finditer(text):
        first = text.count("\n", 0, m.start(1)) + 1
        for k, line in enumerate(m.group(1).split("\n")):
            out.append((first + k, line))
    return out


def xml_code(text: str) -> list[str]:
    # A comment becomes the newlines it held, so line structure (and a diff's line numbers) survive.
    return _collapse(_XML_COMMENT.sub(lambda m: "\n" * m.group(0).count("\n"), text).split("\n"))


# ---- Lua (and a .CT, whose scripts are Lua inside XML): -- line, --[[ ]] / --[==[ ]==] blocks ------------------------

_LONG_OPEN = re.compile(r"\[(=*)\[")


def _lua_scan(text: str):
    """Yield ('code', line_no, chunk) and ('comment', line_no, chunk) pieces, in order."""
    i, n, line = 0, len(text), 1
    buf, buf_line = [], 1
    while i < n:
        c = text[i]
        if c == "\n":
            buf.append(c)
            line += 1
            i += 1
            continue
        if c in "\"'":                                   # short string: ends at the same quote or the line end
            j = i + 1
            while j < n and text[j] != c and text[j] != "\n":
                j += 2 if text[j] == "\\" else 1
            j = min(j + 1, n)
            buf.append(text[i:j])
            i = j
            continue
        if c == "[":
            m = _LONG_OPEN.match(text, i)
            if m:                                        # long string [[ ]] / [=[ ]=]
                close = "]" + m.group(1) + "]"
                j = text.find(close, m.end())
                j = n if j < 0 else j + len(close)
                chunk = text[i:j]
                buf.append(chunk)
                line += chunk.count("\n")
                i = j
                continue
        if text.startswith("--", i):
            yield ("code", buf_line, "".join(buf))
            buf = []
            m = _LONG_OPEN.match(text, i + 2)
            if m:                                        # block comment --[[ ]] / --[==[ ]==]
                close = "]" + m.group(1) + "]"
                j = text.find(close, m.end())
                j = n if j < 0 else j + len(close)
                body = text[m.end():j - len(close)] if j <= n else text[m.end():]
                for k, part in enumerate(body.split("\n")):
                    yield ("comment", line + k, part)
                line += text[i:j].count("\n")
                i = j
            else:                                        # line comment, to the end of the line
                j = text.find("\n", i)
                j = n if j < 0 else j
                yield ("comment", line, text[i + 2:j])
                i = j
            buf_line = line
            continue
        buf.append(c)
        i += 1
    yield ("code", buf_line, "".join(buf))


def lua_comments(text: str) -> list[tuple[int, str]]:
    return [(no, chunk) for kind, no, chunk in _lua_scan(text) if kind == "comment"]


def lua_code(text: str) -> list[str]:
    parts = []
    for kind, _no, chunk in _lua_scan(text):
        parts.append(chunk if kind == "code" else "")
    return _collapse("".join(parts).split("\n"))


# ---- PowerShell: # line, <# #> blocks --------------------------------------------------------------------------------

def _ps1_scan(text: str):
    i, n, line = 0, len(text), 1
    buf = []
    while i < n:
        c = text[i]
        if c == "\n":
            buf.append(c)
            line += 1
            i += 1
            continue
        if c in "\"'":                                   # a string (here-strings and escapes are treated as code)
            j = text.find(c, i + 1)
            j = n if j < 0 else j + 1
            chunk = text[i:j]
            buf.append(chunk)
            line += chunk.count("\n")
            i = j
            continue
        if text.startswith("<#", i):
            yield ("code", "".join(buf))
            buf = []
            j = text.find("#>", i + 2)
            j = n if j < 0 else j + 2
            body = text[i + 2:j - 2]
            for k, part in enumerate(body.split("\n")):
                yield ("comment", line + k, part)
            line += text[i:j].count("\n")
            i = j
            continue
        if c == "#":
            yield ("code", "".join(buf))
            buf = []
            j = text.find("\n", i)
            j = n if j < 0 else j
            yield ("comment", line, text[i + 1:j])
            i = j
            continue
        buf.append(c)
        i += 1
    yield ("code", "".join(buf))


def ps1_comments(text: str) -> list[tuple[int, str]]:
    return [(p[1], p[2]) for p in _ps1_scan(text) if p[0] == "comment"]


def ps1_code(text: str) -> list[str]:
    parts = []
    for p in _ps1_scan(text):
        parts.append(p[1] if p[0] == "code" else "")
    return _collapse("".join(parts).split("\n"))


# ---- Python: # comments and docstrings (module, class, function) ----------------------------------------------------

def _docstring_nodes(tree: ast.AST):
    for node in ast.walk(tree):
        if isinstance(node, (ast.Module, ast.ClassDef, ast.FunctionDef, ast.AsyncFunctionDef)):
            body = getattr(node, "body", [])
            if body and isinstance(body[0], ast.Expr) and isinstance(body[0].value, ast.Constant) \
                    and isinstance(body[0].value.value, str):
                yield body[0]


def py_comments(text: str) -> list[tuple[int, str]]:
    out: list[tuple[int, str]] = []
    try:
        for tok in tokenize.generate_tokens(io.StringIO(text).readline):
            if tok.type == tokenize.COMMENT:
                out.append((tok.start[0], tok.string[1:]))
    except (tokenize.TokenError, IndentationError, SyntaxError):
        pass
    try:
        tree = ast.parse(text)
    except SyntaxError:
        return sorted(out)
    for expr in _docstring_nodes(tree):
        for k, part in enumerate(expr.value.value.split("\n")):
            out.append((expr.lineno + k, part))
    return sorted(out)


def py_code(text: str) -> list[str]:
    """The module's AST with every docstring blanked: comments never reach the AST, so equal dumps mean the edit
    touched comments and docstrings only. A file that does not parse is compared as text."""
    try:
        tree = ast.parse(text)
    except SyntaxError:
        return _collapse(text.split("\n"))
    for expr in _docstring_nodes(tree):
        expr.value.value = ""
    return [ast.dump(tree, include_attributes=False)]


# ---- dispatch -------------------------------------------------------------------------------------------------------

def comments(path: str, text: str) -> list[tuple[int, str]]:
    p = path.lower()
    if p.endswith(XML_EXTS):
        return xml_comments(text)
    if p.endswith(LUA_EXTS):
        return lua_comments(text)
    if p.endswith(PS1_EXTS):
        return ps1_comments(text)
    if p.endswith(PY_EXTS):
        return py_comments(text)
    raise ValueError(f"no comment scanner for {path}")


def code(path: str, text: str) -> list[str]:
    p = path.lower()
    if p.endswith(XML_EXTS):
        return xml_code(text)
    if p.endswith(LUA_EXTS):
        return lua_code(text)
    if p.endswith(PS1_EXTS):
        return ps1_code(text)
    if p.endswith(PY_EXTS):
        return py_code(text)
    raise ValueError(f"no comment scanner for {path}")


# The scanners' own controls; check_comment_history runs them before it scans.
SELFTEST = [
    ("a.axaml", '<Grid>\n  <!-- a\n  b -->\n  <TextBlock Text="x"/>\n</Grid>', [(2, " a"), (3, "  b ")]),
    ("a.lua", "local s = 'x -- not a comment' -- real\nlocal t = [[ -- still a string ]]", [(1, " real")]),
    ("a.lua", "x = 1\n--[[ block\nline two ]]\ny = 2", [(2, " block"), (3, "line two ")]),
    ("a.ps1", "$a = 'x # not' # real\n<# one\ntwo #>", [(1, " real"), (2, " one"), (3, "two ")]),
    ("a.py", '"""doc"""\nx = "# not"  # real\ndef f():\n    """fdoc"""\n',
     [(1, "doc"), (2, " real"), (4, "fdoc")]),
]


def selftest() -> list[str]:
    bad = []
    for path, src, want in SELFTEST:
        got = comments(path, src)
        if got != want:
            bad.append(f"{path}: comments {got!r}, expected {want!r}")
    # code(): a comment edit leaves code() equal; a code edit does not.
    pairs = [
        ("a.axaml", '<A/>\n<!-- x -->\n<B/>', '<A/>\n<!-- y\n -->\n<B/>', True),
        ("a.axaml", '<A X="1"/>', '<A X="2"/>', False),
        ("a.lua", "x = 1 -- one", "x = 1 -- two", True),
        ("a.lua", "x = 'a' -- one", "x = 'b' -- one", False),
        ("a.ps1", "$x = 1 # one", "$x = 1 # two", True),
        ("a.py", '"""a"""\nx = 1  # one\n', '"""b"""\nx = 1  # two\n', True),
        ("a.py", "x = 1\n", "x = 2\n", False),
    ]
    for path, a, b, same in pairs:
        if (code(path, a) == code(path, b)) != same:
            bad.append(f"{path}: code() {'differs' if same else 'is equal'} for {a!r} vs {b!r}")
    return bad


if __name__ == "__main__":
    problems = selftest()
    print("\n".join(problems) if problems else "comment_lang self-test: ok")
    raise SystemExit(1 if problems else 0)
