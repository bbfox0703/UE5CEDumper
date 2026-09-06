#!/usr/bin/env python3
"""Assert the DumperTest sample's README still describes the code it ships with.

WHY THIS EXISTS
  tools/ue-sample/ is a verification target: a tester reads README.md, then types those
  numbers into Value Search and looks for those strings in Live Walker. The README IS the
  acceptance criteria — there is no test that runs it, because what it tests is a live
  process being injected into and walked.

  That makes it a textbook instance of audit #4's 4a root cause: *the report and the
  reality are computed by different code paths*. Change `I32 = 1234567` in the .cpp and
  forget the table, and the next tester scans for a number that is not there — then spends
  the session doubting the dumper. Nothing else in this repo can catch that.

  Four invariants, all mechanical, no prose policing:

    1. every UPROPERTY in the sample headers is mentioned in the README
       (a new property cannot be added undocumented)
    2. every backticked field name in a README table row exists in the headers
       (no phantom rows left behind by a rename)
    3. every numeric and CJK literal in a README value cell appears in the sources
       (the numbers a tester types are the numbers the code sets; README shows GLYPHS
       while the code carries \\uXXXX escapes, so the CJK half is compared by escaping
       the README side — which is also what stops someone "helpfully" de-escaping the .cpp)
    4. the DumperTestStrings constant NAMES match their own escape sequences

  Invariant 4 is the one that protects B28 itself. `Even2_OneNull` claims: two characters
  (even), exactly one of them with a low byte of 0x00. Those two facts ARE the trigger
  being tested. Edit the literal to 統一性 and the name silently lies, the string stops
  being an even-length U+xx00 case, and the test keeps "passing" while testing nothing.

NEGATIVE CONTROLS -- all four were run and observed to FAIL, on a temp copy:
    1. I32 changed in the .cpp only              -> caught (invariant 3, number)
    2. the primary B28 literal edited            -> caught (invariant 3, CJK)
    3. an undocumented UPROPERTY added           -> caught (invariant 1)
    4. a 3rd char appended to Even2_TwoNull      -> caught (invariant 4)
  Two of them MISSED on the first attempt, and both misses were bugs in THIS file:
    * markdown bold between two code spans parsed as the token '**', which compiled to
      the glob ^.*$ and matched every field -- invariant 1 was a silent no-op reporting OK
    * the CJK corpus-wide fallback found 統一 inside 統一言語, so an edited literal passed
  A gate written after the thing it guards, and never observed failing, is a guess.

Usage:
  py tools/check_ue_sample_values.py           # repo root inferred from this file
  py tools/check_ue_sample_values.py --list    # print what was parsed, then check
Exit 0 = clean, 1 = the README and the sources disagree.
"""
import argparse
import os
import re
import sys

SAMPLE = os.path.join("tools", "ue-sample")
SRC = os.path.join(SAMPLE, "DumperTest", "Source", "DumperTest")
HEADERS = ["DumperTestActor.h", "DumperTestTypes.h"]
# Value literals may live in any of these: the .cpp sets most of them, but an enum's
# numeric value (Grade = Elite = 2) is only in the types header.
VALUE_SOURCES = ["DumperTestActor.cpp", "DumperTestActor.h", "DumperTestTypes.h"]

# Fields whose value is COMPUTED rather than written as a literal. Checked for existence
# and documentation like everything else; exempt only from the literal match, with the
# reason recorded so the exemption cannot quietly grow.
COMPUTED = {
    "FixedArr": "set by a loop, (i + 1) * 100 -- 800 never appears as a literal",
    "TickCount": "starts at 0 and is incremented by the timer",
}

UPROP_RE = re.compile(r"UPROPERTY\s*\([^)]*\)\s*(?P<decl>[^;]+);")
# Trailing identifier of a declaration, before any [8] array bound or : 1 bitfield width.
NAME_RE = re.compile(r"(?P<name>[A-Za-z_][A-Za-z0-9_]*)\s*(?:\[[^\]]*\])?\s*(?::\s*\d+\s*)?$")
BACKTICK_RE = re.compile(r"`([^`]+)`")
NUM_RE = re.compile(r"\d+(?:\.\d+)?")
STRINGS_RE = re.compile(
    r"static\s+const\s+TCHAR\*\s+(?P<name>\w+)\s*=\s*TEXT\(\"(?P<lit>[^\"]*)\"\)")


def read(root, *parts):
    with open(os.path.join(root, *parts), encoding="utf-8-sig") as fh:
        return fh.read()


RAW_RE = re.compile(
    r"^[ \t]*(?:int8|uint8|int16|uint16|int32|uint32|int64|uint64|float|double|bool)"
    r"[ \t]+(?P<name>[A-Za-z_]\w*)\s*;\s*$", re.M)


def collect_fields(root):
    """name -> (header, kind). kind is 'UPROPERTY' or 'raw'.

    The RAW members matter as much as the reflected ones: they are the deliberate
    non-UPROPERTY holes that "Guess What" and the Native-C scan have to find, so the
    README documents them and this gate must recognise them as real fields. The first
    version of this check knew only about UPROPERTY and reported RawInt/RawFloat/
    RawDouble as phantom README rows -- flagging the sample's most deliberate feature
    as a typo.
    """
    fields = {}
    for hdr in HEADERS:
        text = read(root, SRC, hdr)
        for m in UPROP_RE.finditer(text):
            decl = " ".join(m.group("decl").split())
            # Drop any initialiser first. `UPROPERTY() float BaseValue = 0.f;` otherwise
            # yields the trailing identifier of "0.f" -- the field was recorded as `f`,
            # and then reported as undocumented. Found by a negative control.
            decl = decl.split("=")[0].strip()
            nm = NAME_RE.search(decl)
            if nm:
                fields[nm.group("name")] = (hdr, "UPROPERTY")
        # Plain members: same line shape, but no UPROPERTY() in front of them.
        for m in RAW_RE.finditer(text):
            if m.group("name") not in fields:
                fields[m.group("name")] = (hdr, "raw")
    return fields


INDIRECT_RE = re.compile(r"\b(?:DumperTestStrings|EDumperTestGrade)::(\w+)")


def source_lines_for(corpus, field):
    """Every source line mentioning the field -- plus one level of indirection.

    The field's own line is usually NOT where its value is written:

        Text_Even2_OneNull = FText::FromString(DumperTestStrings::Even2_OneNull);
        Grade              = EDumperTestGrade::Elite;

    carry a NAME, while the escape sequence and the enum's `= 2` live on the
    declaration. Following that one hop is what lets the CJK half of invariant 3
    work at all, and it is why the constants are named rather than inlined.
    """
    lines = corpus.splitlines()
    hit = [ln for ln in lines if re.search(r"\b" + re.escape(field) + r"\b", ln)]
    targets = {t for ln in hit for t in INDIRECT_RE.findall(ln)}
    for t in targets:
        hit += [ln for ln in lines if re.search(r"\b" + re.escape(t) + r"\b", ln)]
    return "\n".join(hit)


def escape_cjk(text):
    """README carries glyphs, the sources carry \\uXXXX. Compare on the source's terms."""
    return "".join("\\u%04X" % ord(c) if ord(c) > 127 else c for c in text)


def check(root, verbose):
    errors = []
    fields = collect_fields(root)
    # Strip fenced blocks BEFORE any backtick parsing. A ``` fence is three backticks:
    # the naive `([^`]+)` pairing consumes one of them, desynchronising every code span
    # after it, so real field names get swallowed into multi-line prose blobs and one of
    # those blobs was the token '**'. Two of this gate's own bugs came from that single
    # oversight. Fences carry shell commands, never field claims -- drop them outright.
    readme = re.sub(r"```.*?```", "", read(root, SAMPLE, "README.md"), flags=re.S)
    corpus = "\n".join(read(root, SRC, f) for f in VALUE_SOURCES)

    if not fields:
        return ["parsed 0 UPROPERTY fields -- the header layout changed and this check is blind"]

    # --- 1. every field is documented -------------------------------------------------
    # BOTH sets are restricted to IDENTIFIER-SHAPED tokens, and the glob form to a
    # trailing-* prefix (`Str_*`). This is not tidiness -- the first version accepted any
    # backticked token containing '*', and markdown bold sitting between two code spans
    # ("`a` ** `b`") was parsed as the token '**', which compiled to the glob ^.*$ and
    # matched EVERY field. Check 1 silently became a no-op and reported OK. A negative
    # control caught it; nothing else would have. Exactly the failure mode this gate
    # exists to prevent, in the gate itself.
    ticked = set()
    globs = []
    for tok in BACKTICK_RE.findall(readme):
        for part in re.split(r"[/,]", tok):
            part = part.strip()
            if re.fullmatch(r"[A-Za-z_][A-Za-z0-9_]*\*", part):
                globs.append(re.compile("^" + re.escape(part[:-1]) + r"[A-Za-z0-9_]+$"))
            elif re.fullmatch(r"[A-Za-z_][A-Za-z0-9_]*", part):
                ticked.add(part)
    for field in sorted(fields):
        if field in ticked or any(g.match(field) for g in globs):
            continue
        hdr, kind = fields[field]
        errors.append("%s '%s' (%s) is not mentioned in README.md -- a tester will "
                      "never look for it" % (kind, field, hdr))

    # --- 2 + 3, over the table rows ----------------------------------------------------
    # Only cell 0 of a table row is a FIELD CLAIM. Backticks anywhere else are prose or
    # values (`OptionalPresent`, `Set[idx]`, `TextProperty`) and must not be policed as
    # field names -- the first draft of this check did, and flagged a string literal.
    known = set(fields) | {"Health.BaseValue", "Health.CurrentValue"}
    loose = 0
    for line in readme.splitlines():
        if not line.startswith("|"):
            continue
        cells = [c.strip() for c in line.strip().strip("|").split("|")]
        if len(cells) < 2:
            continue
        claims = [t for t in BACKTICK_RE.findall(cells[0])
                  if re.fullmatch(r"[A-Za-z_][A-Za-z0-9_]*", t)]

        # 2. no phantom rows
        for tok in claims:
            if tok not in known and re.match(r"^(Text|Str|Opt|Map|Set|Arr|Raw|I\d|U\d|F\d|b[A-Z])", tok):
                errors.append("README table row claims field '%s', but no such UPROPERTY "
                              "exists (renamed?)" % tok)

        # 3. literals match
        row_fields = [t for t in claims if t in fields]
        checkable = [f for f in row_fields if f not in COMPUTED]
        if not checkable:
            if row_fields and verbose:
                print("  skip (computed): %s -- %s" % (row_fields, COMPUTED[row_fields[0]]))
            continue
        haystack = "\n".join(source_lines_for(corpus, f) for f in checkable)
        value_cell = cells[1]

        def find(token, what):
            """Field-scoped first; corpus-wide as a documented fallback.

            Field scoping is the strong form, but some values are legitimately written
            somewhere the field's own name never appears -- `A.Value = 7777;` on a local
            that is then Add()ed to the container. Falling back to the whole sample keeps
            the invariant that actually matters (a number in the README is a number the
            code sets) while giving up only the attribution. Fallbacks are counted and
            shown by --list so the weaker tier cannot quietly become the normal one.
            """
            if token in haystack:
                return True
            if token in corpus:
                if verbose:
                    print("  loose match: %s %s '%s' found outside its own lines"
                          % (checkable, what, token))
                return None      # accepted, but counted
            return False

        for hexlit in re.findall(r"0x[0-9A-Fa-f]+", value_cell):
            r = find(hexlit, "hex")
            loose += r is None
            if r is False:
                errors.append("README row %s claims %s but no source contains it" % (checkable, hexlit))
        for num in NUM_RE.findall(re.sub(r"0x[0-9A-Fa-f]+", "", value_cell)):
            r = find(num, "number")
            loose += r is None
            if r is False:
                errors.append("README row %s claims value '%s' but no source contains it"
                              % (checkable, num))
        # CJK is FIELD-SCOPED ONLY -- deliberately no corpus-wide fallback.
        # The literals are substrings of one another (統一 lives inside 統一言語), so a
        # corpus-wide search finds a changed string in a *different* constant and passes.
        # Measured: editing Even2_OneNull from 統一 to 最強 was caught by nothing until
        # this fallback was removed. Numbers can afford the looser tier; these cannot.
        cjk = escape_cjk("".join(ch for ch in value_cell
                                 if ord(ch) > 0x2BFF or 0x3000 <= ord(ch)))
        if cjk and cjk not in haystack:
            errors.append("README row %s shows CJK that its own source lines do not contain "
                          "(escaped: %s) -- the literal and the doc have diverged"
                          % (checkable, cjk))

    # --- 4. the DumperTestStrings names tell the truth ---------------------------------
    cpp = read(root, SRC, "DumperTestActor.cpp")
    found = 0
    for m in STRINGS_RE.finditer(cpp):
        name, lit = m.group("name"), m.group("lit")
        escapes = re.findall(r"\\u([0-9A-Fa-f]{4})", lit)
        if len(escapes) * 6 != len(lit):
            errors.append("DumperTestStrings::%s mixes raw characters with escapes -- every "
                          "CJK literal here must be \\uXXXX only (encoding-proof)" % name)
            continue
        found += 1
        nulls = sum(1 for e in escapes if e.upper().endswith("00"))
        claim = re.match(r"(Even|Odd)(\d+)_(One|Two|No)Null$", name)
        if not claim:
            errors.append("DumperTestStrings::%s does not follow "
                          "<Even|Odd><N>_<One|Two|No>Null -- the name is the test's "
                          "specification, so it cannot be freeform" % name)
            continue
        parity, count, nullword = claim.group(1), int(claim.group(2)), claim.group(3)
        if len(escapes) != count:
            errors.append("DumperTestStrings::%s says %d characters but holds %d"
                          % (name, count, len(escapes)))
        if (count % 2 == 0) != (parity == "Even"):
            errors.append("DumperTestStrings::%s says %s but %d is not"
                          % (name, parity, count))
        want = {"One": 1, "Two": 2, "No": 0}[nullword]
        if nulls != want:
            errors.append("DumperTestStrings::%s says %sNull (%d chars with a 0x00 low byte) "
                          "but holds %d -- this IS the B28 trigger, so the name must be exact"
                          % (name, nullword, want, nulls))
        if verbose:
            print("  %-18s %d chars, %d low-byte-00  %s" % (name, len(escapes), nulls, lit))
    if found < 5:
        errors.append("parsed only %d DumperTestStrings constants (expected 5) -- the B28 "
                      "literals moved and invariant 4 is blind" % found)

    if verbose:
        print("  fields parsed: %d | README backticked names: %d | globs: %d | loose matches: %d"
              % (len(fields), len(ticked), len(globs), loose))
    return errors


def check_identity_covers_mirror(root):
    """Every mirrored source file must be in capture_package_identity.SOURCES.

    ⛔ WHY THIS IS A GATE AND NOT A COMMENT. That list is used for TWO things — the content
    digest (`hash_sources`) and the "STALE BINARY" mtime scan — so a file that is mirrored but
    unlisted is invisible to drift detection twice over, and `package-identity.json` will report
    "in sync" while the packaged binary genuinely differs. A false negative in the tool whose only
    job is catching drift is worse than having no tool.

    ⚠ Found by hand 2026-09-06: DumperTestHUD.h/.cpp had been mirrored since 2026-08-14 and were
    never listed. The HUD is the sample's own health readout and the ONLY way to learn the values
    of the non-UPROPERTY raw fields, so its silent drift would break exactly the rows that have no
    other oracle. Nothing would have caught it; hence this check.
    """
    src = os.path.join(root, SRC)
    ident = os.path.join(root, SAMPLE, "capture_package_identity.py")
    if not os.path.isdir(src) or not os.path.isfile(ident):
        return []
    text = open(ident, encoding="utf-8", errors="replace").read()
    m = re.search(r"^SOURCES\s*=\s*\[(.*?)\]", text, re.S | re.M)
    if not m:
        return ["capture_package_identity.py: could not find the SOURCES list to check against"]
    listed = set(re.findall(r'"([^"]+)"', m.group(1)))
    e = re.search(r"^EXCLUDED\s*=\s*\((.*?)\)", text, re.S | re.M)
    excluded = set(re.findall(r'"([^"]+)"', e.group(1))) if e else set()

    on_disk = {f for f in os.listdir(src)
               if f.endswith((".h", ".cpp", ".cs")) and f not in excluded}
    missing = sorted(on_disk - listed)
    phantom = sorted(listed - on_disk - excluded)
    out = []
    for f in missing:
        out.append("%s is mirrored but NOT in capture_package_identity.SOURCES -- its content and "
                   "its mtime are both invisible to drift detection" % f)
    for f in phantom:
        out.append("capture_package_identity.SOURCES lists %s, which is not in the mirror -- "
                   "hash_sources will silently skip it" % f)
    return out


def main():
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--root", default=None, help="repo root (default: inferred)")
    ap.add_argument("--list", action="store_true", dest="verbose",
                    help="print what was parsed before checking")
    args = ap.parse_args()

    root = args.root or os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    if not os.path.isdir(os.path.join(root, SAMPLE)):
        print("SKIP: %s not present" % SAMPLE)
        return 0

    errors = check_identity_covers_mirror(root) + check(root, args.verbose)
    if errors:
        print("check_ue_sample_values: %d problem(s)\n" % len(errors))
        for e in errors:
            print("  - %s" % e)
        print("\nThe README is the acceptance criteria for a test nothing else can run.")
        print("Fix whichever side is wrong -- but fix both to agree.")
        return 1
    print("check_ue_sample_values: OK -- README and sources agree")
    return 0


if __name__ == "__main__":
    sys.exit(main())
