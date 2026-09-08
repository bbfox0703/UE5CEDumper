"""Refuse a `memrec.Active = false` that CE will silently ignore.

    py tools/check_ce_untick_placement.py [--list] [--selftest]

THE DEFECT. An IMMEDIATE `memrec.Active = false` inside an `[ENABLE]` block does
NOTHING. It falls out of `TMemoryRecord.setActive` in CE's `memoryrecordunit.pas`:

    if state = fActive then exit;               // (1) no-op when already in that state
    if processingThread <> nil then exit;       // (2) no-op while processing
    ...
    if autoassemble(script, ..., state, ...) then   // (3) OUR [ENABLE] BLOCK RUNS HERE
      fActive := state;                         // (4) and only NOW does it become true

While our script runs at (3), `fActive` is still false, so the untick hits (1) and
returns having changed nothing -- then (4) ticks the row anyway. A DEFERRED untick works
because by the time the timer fires, (4) has run: `state(false) != fActive(true)` clears
(1) and the activation is finished so (2) is clear. Full derivation, measured against
CE 7.7 and then confirmed against the 7.5 source, lives on
`CeLuaHygiene.DeferredUntickLua`'s doc comment. [FREEZEUNTICK-2026-08-20]

WHY A GATE, AND WHY THIS ONE. The 2026-09-08 blind-spot sweep found the immediate form
still shipping in THREE places -- `scripts/UE5CEDumper.CT:879` (live code, in the
shipped table), and the copy-me samples in `ue5_freeze_helper.lua` and
`ue5_invoke_helper.lua` -- five weeks after the C# generators were all fixed. The C#
half is covered by `CeMailboxBailoutTests`; nothing covered the hand-written artifacts,
and nothing covered the samples at all.

⚠ THE ROUND-1 GATE DESIGN WAS REFUTED AND THIS IS THE REPLACEMENT. The first proposal
was an allowlist of ~25 "effect applier" functions whose returns must be consumed. It
scored 0 of 10 findings, and worse, a hand-curated list is the exact failure this repo
has already measured once: `check_property_family.py`'s header records that G12 shipped
"Both writers now go through here" when there were THREE. This check needs NO curation
and NO baseline -- the correct count outside `scripts/tests/` is 0, and it stays 0.

WHAT COUNTS AS DEFERRED (the only two shapes in this repo, both verified by hand):
  * the one-line `CeLuaHygiene.DeferredUntickLua` form -- `createTimer` on the SAME line
    as the untick (emitted as one line on purpose: it appears at 32 bail-out sites and
    six lines apiece would bury the logic);
  * lexically inside an `OnTimer = function` / `OnClose = function` body -- the shape
    `TeleportScriptGenerator` (:220, :281), `BakedScriptGenerator` (:431),
    `InvokeScriptGenerator` (:488) and `CoordLibraryScriptGenerator` (:645) use.
Each of those five is a NEGATIVE CONTROL for this check: they must never be flagged,
and `--selftest` asserts exactly that.

⚠ BLOCK COMMENTS ARE IN SCOPE, deliberately -- do not "fix" this by skipping them. Two
of the three defects live inside the leading `--[[ ]]` doc block of a helper, in a
sample the file itself introduces with "copy this whole thing" and (falsely) as "the
same shape UE5DumpUI's own generated scripts use". For these files the samples ARE the
deliverable, so a wrong sample is a shipped defect.

⚠ `[DISABLE]` blocks are NOT flagged. There the record is already going inactive, so
the untick is redundant rather than wrong.

SCOPE: `scripts/*.lua` and `scripts/*.CT` (the `<AssemblerScript>` bodies), plus the
emitted Lua of `ui/UE5DumpUI/Services/*ScriptGenerator.cs`, `CeXmlExportService.cs` and
`CeLuaHygiene.cs` -- for the C# the Lua is reconstructed by concatenating the string
literals in source order, which is what the generators emit.
`scripts/tests/` is excluded: `untick_bailout_test.lua` and the fixtures carry the bad
form ON PURPOSE, as their own negative controls.
"""
from __future__ import annotations

import argparse
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent

UNTICK = re.compile(r"memrec\s*(?:~=\s*nil\s+and\s+memrec\s*)?\.?\s*Active\s*=\s*false", re.I)
UNTICK_SIMPLE = re.compile(r"memrec\.Active\s*=\s*false", re.I)
DEFER_OPEN = re.compile(r"\b(OnTimer|OnClose)\s*=\s*function\b", re.I)
SAME_LINE_TIMER = re.compile(r"\bcreateTimer\b", re.I)
# ⚠ A marker must be the WHOLE line once a trailing comment is stripped -- and the .CT's
# `<AssemblerScript>[ENABLE]` prefix has to be stripped with it. Both halves were learned
# from a failing negative control, one after the other:
#   * a whole-line anchor missed `<AssemblerScript>[ENABLE]`, so the PREVIOUS record's
#     `[DISABLE]` leaked across every remaining record and hid `UE5CEDumper.CT:879` -- the
#     one live defect this gate exists for;
#   * relaxing it to "ends with the marker" then matched
#     `ue5_freeze_helper.lua:163`, a trailing comment reading `-- <-- reachable from
#     [DISABLE]`, which hid `:170` the same way.
# working-lessons 1.2a, three times inside one 200-line file: each fix passed the cases
# that existed and broke on the shape nobody had written a case for yet.
MARKER = re.compile(r"^(?:<AssemblerScript>)?\s*\[(ENABLE|DISABLE)\]$")
TRAILING_COMMENT = re.compile(r"--.*$")


def marker_of(text: str):
    """"enable" / "disable" / None -- for a line that IS a marker, not one mentioning it."""
    m = MARKER.match(TRAILING_COMMENT.sub("", text).strip())
    return m.group(1).lower() if m else None


# C# emitters: the Lua actually written out, in source order.
CS_EMIT = re.compile(r"""(?:Line\s*\(\s*sb\s*,\s*|\.Append(?:Line)?\s*\(\s*)\$?@?"((?:[^"\\]|\\.)*)"|"""
                     r"""\.Append\s*\(\s*\$?@?"((?:[^"\\]|\\.)*)\"""", re.X)

CS_FILES_GLOBS = ("ui/UE5DumpUI/Services/*ScriptGenerator.cs",
                  "ui/UE5DumpUI/Services/CeXmlExportService.cs",
                  "ui/UE5DumpUI/Services/CeLuaHygiene.cs")


def _lua_lines_from_lua(path: Path):
    """(text, source_line) for a real .lua file. Block comments INCLUDED - see header."""
    for i, ln in enumerate(path.read_text(encoding="utf-8", errors="replace").splitlines(), 1):
        yield ln, i


def _lua_lines_from_ct(path: Path):
    """(text, source_line) for the AA scripts embedded in a .CT."""
    lines = path.read_text(encoding="utf-8", errors="replace").splitlines()
    inside = False
    for i, ln in enumerate(lines, 1):
        if "<AssemblerScript>" in ln:
            inside = True
        if inside:
            yield ln, i
        if "</AssemblerScript>" in ln:
            inside = False


def _lua_lines_from_cs(path: Path):
    """(emitted_lua, source_line) - the string literals a generator writes, in order."""
    for i, ln in enumerate(path.read_text(encoding="utf-8", errors="replace").splitlines(), 1):
        for m in CS_EMIT.finditer(ln):
            lit = m.group(1) if m.group(1) is not None else m.group(2)
            if lit is None:
                continue
            yield lit.replace("\\\"", "\""), i


def scan(stream) -> list[tuple[int, str, str]]:
    """Return [(source_line, code, why)] for every NON-deferred untick in an [ENABLE].

    State machine, deliberately small: track which block we are in ([ENABLE] until the
    next [DISABLE]) and whether we are inside a deferral closure. Depth is counted from
    the OnTimer/OnClose opener with Lua's own block keywords, so a nested `if` inside
    the callback cannot close it early.
    """
    hits: list[tuple[int, str, str]] = []
    in_enable = True          # a bare .lua sample with no marker is ENABLE-shaped
    defer_depth = 0           # >0 => inside an OnTimer/OnClose body
    for text, srcline in stream:
        mark = marker_of(text)
        if mark:
            in_enable, defer_depth = (mark == "enable"), 0
            continue

        # A Lua LINE comment is prose, not emitted code. Block comments stay in scope
        # (the copy-me samples live in one), but a paragraph ABOUT the untick is not a
        # site -- ue5_freeze_helper.lua:973 explains the mechanism and is not a defect.
        if text.strip().startswith("--"):
            continue

        opens_defer = bool(DEFER_OPEN.search(text))

        if defer_depth > 0 or opens_defer:
            # count block openers/closers on this line
            opens = len(re.findall(r"\b(function|if|for|while|do)\b", text))
            closes = len(re.findall(r"\bend\b", text))
            if opens_defer and defer_depth == 0:
                defer_depth = 1
                # the opener line's own `function` is what we just counted as depth 1
                opens -= 1
            defer_depth += opens - closes
            if defer_depth < 0:
                defer_depth = 0

        if not UNTICK_SIMPLE.search(text) and not UNTICK.search(text):
            continue
        if not in_enable:
            continue
        if SAME_LINE_TIMER.search(text):
            continue                      # the one-line DeferredUntickLua form
        if defer_depth > 0:
            continue                      # inside OnTimer / OnClose
        hits.append((srcline, text.strip(),
                     "immediate untick in [ENABLE] -- CE's setActive early-exits while "
                     "autoassemble is still running, so this changes nothing and the row "
                     "stays ticked"))
    return hits


def targets() -> list[tuple[Path, object]]:
    out: list[tuple[Path, object]] = []
    for p in sorted((ROOT / "scripts").glob("*.lua")):
        out.append((p, _lua_lines_from_lua))
    for p in sorted((ROOT / "scripts").glob("*.CT")):
        out.append((p, _lua_lines_from_ct))
    for g in CS_FILES_GLOBS:
        for p in sorted(ROOT.glob(g)):
            out.append((p, _lua_lines_from_cs))
    return out


def run() -> list[str]:
    problems: list[str] = []
    for path, reader in targets():
        for line, code, why in scan(reader(path)):
            rp = path.relative_to(ROOT).as_posix()
            problems.append("%s:%d  %s\n      %s" % (rp, line, code, why))
    return problems


# ---------------------------------------------------------------- self-test
# Negative controls, per working-lessons 1.2: prove the check FAILS when it should and
# PASSES on each shape that is legitimately fine. 1.2a is why the bad cases below vary
# on more than one axis (indentation, `~= nil and`, [DISABLE], nesting depth).
SELFTEST = [
    # (name, lua, expect_hits)
    ("bare immediate untick", "[ENABLE]\nif memrec then memrec.Active = false end\n", 1),
    ("indented immediate untick", "[ENABLE]\n      if memrec then memrec.Active = false end\n", 1),
    ("guarded immediate untick",
     "[ENABLE]\n  if memrec ~= nil and memrec.Active then memrec.Active = false end\n", 1),
    ("one-line deferred (DeferredUntickLua)",
     "[ENABLE]\nif memrec then local _u=createTimer(nil,false) _u.Interval=50 "
     "_u.OnTimer=function(x) x.destroy() memrec.Active = false end _u.Enabled=true end\n", 0),
    ("multi-line OnTimer body",
     "[ENABLE]\nlocal t = createTimer(nil)\nt.Interval = 50\nt.OnTimer = function(timer)\n"
     "  timer.destroy()\n  if memrec then memrec.Active = false end\nend\n", 0),
    ("OnTimer body with a nested if",
     "[ENABLE]\nt.OnTimer = function(s)\n  s.Enabled = false\n  if DEBUG == 0 then\n"
     "    print('x')\n  end\n  if memrec then memrec.Active = false end\nend\n", 0),
    ("OnClose body",
     "[ENABLE]\nfrm.OnClose = function(sender)\n  frm = nil\n"
     "  if memrec ~= nil and memrec.Active then memrec.Active = false end\n"
     "  return caFree\nend\n", 0),
    ("after the OnTimer body has closed",
     "[ENABLE]\nt.OnTimer = function(s)\n  s.destroy()\nend\n"
     "if memrec then memrec.Active = false end\n", 1),
    ("[DISABLE] is not flagged",
     "[ENABLE]\nprint('x')\n[DISABLE]\nif memrec then memrec.Active = false end\n", 0),
    # The .CT shape, and the state leak it caused. Added AFTER the first nine cases went
    # green over a scan that still missed the live defect -- see the ENABLE_MARK comment.
    (".CT marker sharing a line with its XML tag",
     "<AssemblerScript>[ENABLE]\nif memrec then memrec.Active = false end\n", 1),
    ("a previous record's [DISABLE] must not leak",
     "<AssemblerScript>[ENABLE]\nprint('a')\n[DISABLE]\nue5_shutdown()\n"
     "</AssemblerScript>\n<AssemblerScript>[ENABLE]\n"
     "if memrec then memrec.Active = false end\n", 1),
    ("a trailing comment mentioning [DISABLE] does not flip state",
     "[ENABLE]\nlocal h = x   -- <-- reachable from [DISABLE]\n"
     "if memrec then memrec.Active = false end\n", 1),
    ("prose mentioning [ENABLE] mid-sentence does not flip state",
     "[DISABLE]\n-- an immediate untick in [ENABLE] does nothing, see the header\n"
     "if memrec then memrec.Active = false end\n", 0),
]


def selftest() -> int:
    bad = 0
    for name, lua, expect in SELFTEST:
        got = len(scan((ln, i) for i, ln in enumerate(lua.splitlines(), 1)))
        ok = got == expect
        bad += 0 if ok else 1
        print("  %-4s %-38s expected %d, got %d" % ("ok" if ok else "FAIL", name, expect, got))
    print("selftest: %d case(s), %d failed" % (len(SELFTEST), bad))
    return 1 if bad else 0


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--list", action="store_true", help="list every untick site and its verdict")
    ap.add_argument("--selftest", action="store_true", help="run the negative controls")
    args = ap.parse_args()

    if args.selftest:
        return selftest()

    if args.list:
        total = 0
        for path, reader in targets():
            rows = [(ln, t) for t, ln in reader(path)
                    if UNTICK_SIMPLE.search(t) or UNTICK.search(t)]
            if not rows:
                continue
            bad = {ln for ln, _, _ in scan(reader(path))}
            for ln, t in rows:
                total += 1
                print("%-6s %s:%d  %s" % ("BAD" if ln in bad else "ok",
                                          path.relative_to(ROOT).as_posix(), ln, t.strip()[:110]))
        print("\n%d untick site(s) examined." % total)
        return 0

    problems = run()
    if problems:
        print("CHECK FAILED: %d immediate untick(s) inside [ENABLE].\n" % len(problems))
        for p in problems:
            print("  " + p)
        print("\nFix: use the deferred form. In C#, call CeLuaHygiene.AppendDeferredUntick "
              "(or emit CeLuaHygiene.DeferredUntickLua). In a hand-written .lua/.CT, emit the "
              "same one line:\n"
              "  if memrec then local _u=createTimer(nil,false) _u.Interval=50 "
              "_u.OnTimer=function(x) x.destroy() memrec.Active = false end _u.Enabled=true end")
        return 1

    n = sum(1 for path, reader in targets()
            for t, _ in reader(path) if UNTICK_SIMPLE.search(t) or UNTICK.search(t))
    print("CHECK OK: %d untick site(s), every one deferred." % n)
    return 0


if __name__ == "__main__":
    sys.exit(main())
