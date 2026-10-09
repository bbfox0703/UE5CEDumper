"""Gate: no buffer the DLL hands ProcessEvent is sized from a ParmsSize alone. [UE-OVERRIDE-411]

    py tools/check_processevent_buffers.py [--selftest]

WHY. UFunction::ParmsSize is read from the UFunction's tail, and where the tail sits depends on the UE version: a
version from the wrong side of 4.18 reads the field next door (ReturnValueOffset, or NumParms). A buffer sized from
that number ended where the return value starts, and ProcessEvent wrote past it in the game's heap -- the
[UE-OVERRIDE-411] review's MED. Every buffer is now sized by `Ubel::ParamBufferSize` (or
`DynOff::ProcessEventBufferBytes`), which never answers less than where the function's own parameter chain ends. The
second review found the call sites themselves unpinned: they live in files no test target compiles, so the next
feature that writes `std::vector<uint8_t> buf(fi.parmsSize, 0)` the way every caller once did would stay green.

WHAT IT REFUSES, in dll/src after comments and string literals are blanked:
  1. An allocation -- `std::vector<..>` construction, `.resize` / `.assign`, `new T[..]`, the malloc family,
     `make_unique<T[]>` -- whose size expression reads a ParmsSize (`parmsSize` / `ParmsSize`, as a name or a member),
     directly or through a local assigned from one in the same file, and does not go through a helper.
     "Through a helper" means the expression calls `ParamBufferSize(` / `ProcessEventBufferBytes(`, or names a local
     every assignment of which does: `max(chain end, what the caller asked for)` is fine, the caller's number alone
     is not. A local with one unprotected assignment stays unprotected whatever else is assigned to it.
  2. A `UE5_CallProcessEventEx` call whose size argument is not a buffer's own `.size()` or a `sizeof(..)`, or 0 (no
     owned copy: the legacy export's caller keeps its buffer). Its size is the bytes the queued request copies and
     copies back, so anything else can disagree with the buffer it describes.
The bodies of the two helpers are exempt: they are where a ParmsSize legitimately meets a chain end.
⚠ Names are tracked per FILE, not per function -- a crude scope that can only over-report; rename the local.
"""
from __future__ import annotations

import argparse
import pathlib
import re
import sys

REPO = pathlib.Path(__file__).resolve().parents[1]
SRC = REPO / "dll" / "src"

PARMS = re.compile(r"(?<![\w])[pP]armsSize(?![\w])")
HELPER = re.compile(r"(?<![\w])(?:ParamBufferSize|ProcessEventBufferBytes)\s*\(")
# The helper bodies: (file name, function name).
EXEMPT = {("Grimoire.h", "ProcessEventBufferBytes"), ("Ubel.cpp", "ParamBufferSize")}

# A plain assignment or an initialisation: the name right before a lone '=' (so not ==, <=, +=, ...).
ASSIGN = re.compile(r"(?<![\w])([A-Za-z_]\w*)\s*=(?!=)\s*([^;{}]*);")
# A vector's size comes only from parentheses; a braced list is its elements.
ALLOC_CALLS = re.compile(
    r"(?:std::vector\s*<[^;(){}]*?>\s*(?:[A-Za-z_]\w*\s*)?\()"
    r"|(?:\.(?:resize|assign)\s*\()"
    r"|(?:(?<![\w])(?:malloc|calloc|realloc|_alloca|alloca|_malloca|HeapAlloc|VirtualAlloc|LocalAlloc|GlobalAlloc"
    r"|CoTaskMemAlloc)\s*\()"
    r"|(?:std::make_(?:unique|shared)\s*<[^;(){}]*\[\]\s*>\s*\()")
NEW_ARRAY = re.compile(r"(?<![\w])new\s+[\w:\s]+?\[")
PE_EX = re.compile(r"(?<![\w])UE5_CallProcessEventEx\s*\(")


def blank_comments_and_strings(text: str) -> str:
    """Replace comments and string / char literals with spaces, keeping every newline (line numbers survive)."""
    out = []
    i, n = 0, len(text)
    while i < n:
        c = text[i]
        if text.startswith("//", i):
            j = text.find("\n", i)
            j = n if j < 0 else j
            out.append(" " * (j - i)); i = j
        elif text.startswith("/*", i):
            j = text.find("*/", i + 2)
            j = n if j < 0 else j + 2
            out.append(re.sub(r"[^\n]", " ", text[i:j])); i = j
        elif c == 'R' and text.startswith('R"', i):
            m = re.match(r'R"([^(\s]{0,16})\(', text[i:])
            if not m:
                out.append(c); i += 1; continue
            end = text.find(")" + m.group(1) + '"', i)
            j = n if end < 0 else end + len(m.group(1)) + 2
            out.append(re.sub(r"[^\n]", " ", text[i:j])); i = j
        elif c in "\"'":
            j = i + 1
            while j < n and text[j] != c and text[j] != "\n":
                j += 2 if text[j] == "\\" else 1
            j = min(j + 1, n)
            out.append(c + " " * max(j - i - 2, 0) + (c if j - i >= 2 else "")); i = j
        else:
            out.append(c); i += 1
    return "".join(out)


def matching(text: str, open_at: int) -> int:
    """Index of the bracket closing the one at `open_at` (of the same kind), or len(text)."""
    pairs = {"(": ")", "{": "}", "[": "]"}
    o = text[open_at]
    c = pairs[o]
    depth = 0
    for k in range(open_at, len(text)):
        if text[k] == o:
            depth += 1
        elif text[k] == c:
            depth -= 1
            if depth == 0:
                return k
    return len(text)


def blank_exempt_bodies(text: str, fname: str) -> str:
    for f, func in EXEMPT:
        if f != fname:
            continue
        for m in re.finditer(r"(?<![\w])" + func + r"\s*\([^;{}]*\)\s*(?:const\s*)?\{", text):
            start = m.end() - 1
            end = matching(text, start)
            text = text[:start] + re.sub(r"[^\n]", " ", text[start:end + 1]) + text[end + 1:]
    return text


def names_in(expr: str, names: set[str]) -> bool:
    return any(re.search(r"(?<![\w])" + re.escape(n) + r"(?![\w])", expr) for n in names)


def classify_names(text: str):
    """(unprotected, protected) locals. Protected first, to a fixpoint: every assignment goes through a helper or
    another protected local. Then unprotected, to a fixpoint: some assignment reads a ParmsSize or an unprotected
    local and is not helped. The order matters -- deciding a local before the locals it is built from are known reads
    `max(chain end, asked)` as unhelped -- and the two sets cannot meet: a protected local has no unhelped
    assignment."""
    assigns: dict[str, list[str]] = {}
    for m in ASSIGN.finditer(text):
        name, rhs = m.group(1), m.group(2)
        if name in ("if", "while", "for", "return", "case"):
            continue
        assigns.setdefault(name, []).append(rhs)
    helped = lambda rhs, prot: bool(HELPER.search(rhs)) or names_in(rhs, prot)
    prot: set[str] = set()
    changed = True
    while changed:
        changed = False
        for name, rhss in assigns.items():
            if name not in prot and all(helped(r, prot) for r in rhss):
                prot.add(name); changed = True
    unprot: set[str] = set()
    changed = True
    while changed:
        changed = False
        for name, rhss in assigns.items():
            if name in unprot:
                continue
            if any((PARMS.search(r) or names_in(r, unprot)) and not helped(r, prot) for r in rhss):
                unprot.add(name); changed = True
    return unprot, prot


def size_is_unprotected(expr: str, unprot: set[str], prot: set[str]) -> bool:
    reads = bool(PARMS.search(expr)) or names_in(expr, unprot)
    return reads and not (HELPER.search(expr) or names_in(expr, prot))


def split_args(text: str, open_at: int) -> list[str]:
    close = matching(text, open_at)
    body = text[open_at + 1:close]
    args, depth, cur = [], 0, []
    for ch in body:
        if ch in "([{":
            depth += 1
        elif ch in ")]}":
            depth -= 1
        if ch == "," and depth == 0:
            args.append("".join(cur)); cur = []
        else:
            cur.append(ch)
    args.append("".join(cur))
    return args


def line_of(text: str, pos: int) -> int:
    return text.count("\n", 0, pos) + 1


def check_text(raw: str, fname: str) -> list[tuple[int, str, str]]:
    """Violations in one file: (line, kind, the offending expression)."""
    text = blank_exempt_bodies(blank_comments_and_strings(raw), fname)
    unprot, prot = classify_names(text)
    out = []
    for m in ALLOC_CALLS.finditer(text):
        open_at = m.end() - 1
        args = split_args(text, open_at)
        # A vector's size is its first argument; the malloc family's is the whole list (calloc multiplies two).
        expr = args[0] if text[m.start()] == "s" or m.group(0).startswith(".") else ",".join(args)
        if size_is_unprotected(expr, unprot, prot):
            out.append((line_of(text, m.start()), "allocation", " ".join(expr.split())))
    for m in NEW_ARRAY.finditer(text):
        open_at = m.end() - 1
        expr = text[open_at + 1:matching(text, open_at)]
        if size_is_unprotected(expr, unprot, prot):
            out.append((line_of(text, m.start()), "allocation", " ".join(expr.split())))
    for m in PE_EX.finditer(text):
        before = text[max(0, m.start() - 40):m.start()]
        if re.search(r"(?:int32_t|int)\s*$", before):
            continue   # its declaration or definition, not a call
        args = split_args(text, m.end() - 1)
        if len(args) != 4:
            continue
        size = " ".join(args[3].split())
        inner = re.sub(r"^(?:static_cast\s*<\s*\w+\s*>\s*|\(\s*\w+\s*\)\s*)", "", size).strip()
        inner = inner[1:-1].strip() if inner.startswith("(") and inner.endswith(")") else inner
        if re.fullmatch(r"[\w.\->\[\]]+\.size\(\)|sizeof\s*\(.*\)|0", inner):
            continue
        out.append((line_of(text, m.start()), "UE5_CallProcessEventEx size", size))
    return out


def scan(root: pathlib.Path):
    found = []
    for p in sorted(list(root.glob("*.cpp")) + list(root.glob("*.h"))):
        for line, kind, expr in check_text(p.read_text(encoding="utf-8", errors="replace"), p.name):
            found.append((p.name, line, kind, expr))
    return found


# (name, file name, source, violations expected). The red ones are the shapes the gate exists for; the green ones are
# today's call sites, so a gate that flags them is the one that is wrong.
SELFTEST = [
    ("red: a vector sized from fi.parmsSize", "Wirbel.cpp",
     "std::vector<uint8_t> buf(fi.parmsSize, 0);", 1),
    ("red: the max(.., 1) idiom on parmsSize", "Wirbel.cpp",
     "std::vector<uint8_t> buf((std::max<size_t>)(fi.parmsSize, 1), 0);", 1),
    ("red: through a local", "Laufen.cpp",
     "size_t n = fi.parmsSize;\nstd::vector<uint8_t> b(n);", 1),
    ("red: malloc", "Edel.cpp", "auto* p = static_cast<uint8_t*>(malloc(hit.parmsSize));", 1),
    ("red: new[]", "Edel.cpp", "uint8_t* p = new uint8_t[f.parmsSize];", 1),
    ("red: resize", "Edel.cpp", "buf.resize(fi.parmsSize);", 1),
    ("red: a local with one unprotected assignment stays unprotected", "Fern.cpp",
     "size_t bufSize = parmsSize;\nif (ok) bufSize = Ubel::ParamBufferSize(u, fi.parmsSize);\n"
     "std::vector<uint8_t> paramBuf(bufSize, 0);", 1),
    ("red: a size argument that is not the buffer's", "Edel.cpp",
     "UE5_CallProcessEventEx(i, f, reinterpret_cast<uintptr_t>(buf.data()), (uint32_t)fi.parmsSize);", 1),
    ("green: the helper", "Wirbel.cpp",
     "std::vector<uint8_t> buf(Ubel::ParamBufferSize(fi), 0);", 0),
    ("green: max(chain end, what the caller asked for)", "Fern.cpp",
     "const size_t authoritative = Ubel::ParamBufferSize(ufuncAddr, resolved ? fi.parmsSize : 0);\n"
     "const size_t asked = (parmsSize > 0) ? static_cast<size_t>(parmsSize) : 0;\n"
     "const size_t bufSize = (std::max)(authoritative, asked);\nstd::vector<uint8_t> paramBuf(bufSize, 0);", 0),
    ("green: a comment and a string are not code", "Wirbel.cpp",
     "// std::vector<uint8_t> buf(fi.parmsSize, 0);\nLOG_INFO(\"buf(fi.parmsSize)\");", 0),
    ("green: the buffer's own size, and sizeof", "Wirbel.cpp",
     "UE5_CallProcessEventEx(i, f, reinterpret_cast<uintptr_t>(buf.data()), static_cast<uint32_t>(buf.size()));\n"
     "UE5_CallProcessEventEx(i, f, p, static_cast<uint32_t>(sizeof(g_invokeMailbox.paramsData)));", 0),
    ("green: a declaration is not a call", "Wirbel.cpp",
     "extern \"C\" int32_t UE5_CallProcessEventEx(uintptr_t instance, uintptr_t ufunc, uintptr_t params, "
     "uint32_t size);", 0),
    ("green: the helper body itself", "Ubel.cpp",
     "uint32_t ParamBufferSize(const FunctionInfo& fi) {\n    std::vector<int> v(fi.parmsSize);\n    return 0;\n}", 0),
    ("red: ...but only in its own file", "Aura.cpp",
     "uint32_t ParamBufferSize(const FunctionInfo& fi) {\n    std::vector<int> v(fi.parmsSize);\n    return 0;\n}", 1),
    ("green: a sanity bound is not an allocation", "Schlacht.cpp",
     "if (out.parmsSize < 0 || out.parmsSize > kMaxSaneParmsSize) return false;", 0),
]


def selftest() -> int:
    bad = 0
    for name, fname, src, want in SELFTEST:
        got = len(check_text(src, fname))
        ok = got == want
        bad += not ok
        print(f"  {'ok ' if ok else 'BAD'}  {name}: {got} violation(s), want {want}")
    print(f"selftest: {len(SELFTEST) - bad}/{len(SELFTEST)} passed")
    return 1 if bad else 0


def main(argv=None) -> int:
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--selftest", action="store_true", help="the gate's own red / green controls only")
    a = ap.parse_args(argv)
    # Every run proves the checker can still go red before it is trusted to say green.
    if selftest():
        print("CHECK FAILED: the gate's own controls disagree -- fix the gate before trusting its verdict")
        return 1
    if a.selftest:
        return 0
    found = scan(SRC)
    for f, line, kind, expr in found:
        print(f"  {f}:{line}  {kind}: {expr}")
    if found:
        print(f"CHECK FAILED: {len(found)} ProcessEvent buffer size(s) read a ParmsSize without "
              f"Ubel::ParamBufferSize / DynOff::ProcessEventBufferBytes")
        return 1
    print("CHECK OK: every allocation sized from a ParmsSize goes through Ubel::ParamBufferSize / "
          "DynOff::ProcessEventBufferBytes, and every UE5_CallProcessEventEx passes its buffer's own size")
    return 0


if __name__ == "__main__":
    sys.exit(main())
