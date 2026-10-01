#!/usr/bin/env python3
"""No tracked text file may hold a control byte: the mark a collapsed backslash escape leaves behind.

    py tools/check_text_integrity.py            # exit 1 and name each file, line and byte
    py tools/check_text_integrity.py --list     # how many files were scanned, and which kinds are skipped
    py tools/check_text_integrity.py --selftest # the negative controls only (every run does them first)

WHY. Most multi-line edits here are made by a small patch script. Passed through a shell heredoc, the
script has its backslash escapes collapsed before Python sees them, and it then WRITES the character
the escape names: a `\\0` becomes a NUL, a `\\b` a backspace, a `\\v` a vertical tab, a `\\n` a line
break in the middle of a string. The result still compiles and still reads normally in a diff.

Measured: a NUL reached `Mimic.cpp` and `docs/todo.md` on 2026-08-23 (git then treated both as binary);
and the first run of this gate, 2026-10-01, found a vertical tab that had sat in a path inside
`ProxyOrphanScannerTests.cs` since 2026-07-30 — `Win64<VT>ersion.dll` for `Win64\\version.dll` — with
every test passing, because no assertion read that field.

WHAT IT CHECKS. Every tracked file that is not a known binary kind, for any byte below 0x20 other than
tab, LF and CR, and for DEL. It reads the working tree, so it sees an edit before it is committed.

WHAT IT CANNOT SEE. A patch that matched nothing and changed nothing, and an escape that collapsed
into an ordinary character (a `\\n` that became a real newline). Those need the script to assert that
each anchor matched exactly once, and a look at the diff.
"""
from __future__ import annotations

import os
import pathlib
import subprocess
import sys

sys.stdout.reconfigure(encoding="utf-8", errors="replace")

ROOT = pathlib.Path(__file__).resolve().parents[1]
# Kinds that are binary by nature. Everything else that is tracked is text and is scanned: a list of
# TEXT kinds would silently skip the next new extension.
BINARY_EXT = {".png", ".webp", ".gif", ".jpg", ".jpeg", ".ico", ".bmp", ".gz", ".zip", ".7z", ".bin",
              ".dll", ".exe", ".pdb", ".lib", ".obj", ".res", ".snk", ".ttf", ".otf", ".woff", ".woff2",
              ".pdf", ".dmp", ".uasset", ".umap", ".pak"}
ALLOWED = {0x09, 0x0A, 0x0D}
NAMES = {0x00: "NUL", 0x07: "BEL", 0x08: "backspace", 0x0B: "vertical tab", 0x0C: "form feed",
         0x1B: "ESC", 0x7F: "DEL"}


def control_bytes(data: bytes):
    """-> [(line, byte)] for every control byte that has no business in a text file."""
    out = []
    line = 1
    for b in data:
        if b == 0x0A:
            line += 1
        elif (b < 0x20 and b not in ALLOWED) or b == 0x7F:
            out.append((line, b))
    return out


_SELFTEST = [
    (b"plain text\twith a tab\r\nand CRLF\n", 0),
    (b"Win64\x0bersion.dll", 1),                 # a collapsed \v
    (b"a\x00b", 1),                              # a collapsed \0
    (b"word\x08boundary\x08", 2),                # a collapsed \b, twice
    ("繁體中文 and é\n".encode("utf-8"), 0),      # bytes >= 0x80 are not control bytes
    (b"line 1\nline 2 \x7f\n", 1),
]


def selftest():
    for data, want in _SELFTEST:
        got = control_bytes(data)
        if len(got) != want:
            print("SELFTEST FAILED: %r -> %r, wanted %d hit(s)" % (data, got, want))
            return False
    if control_bytes(b"x\ny\x0bz")[0] != (2, 0x0B):
        print("SELFTEST FAILED: the line number of a hit is wrong")
        return False
    return True


def tracked_files():
    out = subprocess.run(["git", "ls-files", "-z"], cwd=ROOT, capture_output=True, check=True).stdout
    return [f.decode("utf-8", "replace") for f in out.split(b"\0") if f]


def main():
    if not selftest():
        return 1
    if "--selftest" in sys.argv:
        print("check_text_integrity: selftest OK (%d cases)" % len(_SELFTEST))
        return 0
    scanned, skipped, bad = 0, 0, []
    for rel in tracked_files():
        if os.path.splitext(rel)[1].lower() in BINARY_EXT:
            skipped += 1
            continue
        try:
            data = (ROOT / rel).read_bytes()
        except OSError:
            continue                              # deleted in the working tree, or a submodule path
        scanned += 1
        hits = control_bytes(data)
        if hits:
            bad.append((rel, hits))
    if "--list" in sys.argv:
        print("scanned %d tracked text file(s); skipped %d of a binary kind (%s)"
              % (scanned, skipped, " ".join(sorted(BINARY_EXT))))
    if bad:
        print("check_text_integrity FAILED: %d file(s) hold a control byte\n" % len(bad))
        for rel, hits in bad:
            for line, b in hits[:5]:
                print("  %s:%d   0x%02X %s" % (rel, line, b, NAMES.get(b, "control byte")))
            if len(hits) > 5:
                print("  %s   ... and %d more" % (rel, len(hits) - 5))
        print("\nA control byte in a text file is almost always a backslash escape that a shell heredoc "
              "collapsed\nbefore a patch script ran. Put back the two characters it stood for (\\v, \\b, "
              "\\0 ...), and write\nthe next patch script with the file-writing tool, not a heredoc.")
        return 1
    print("check_text_integrity OK: %d tracked text file(s), no control bytes" % scanned)
    return 0


if __name__ == "__main__":
    sys.exit(main())
