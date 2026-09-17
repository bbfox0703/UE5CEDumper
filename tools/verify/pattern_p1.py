r"""P1 -- "computed and never published", the blank sweep's most common defect shape.

    py tools/verify/pattern_p1.py logs        # P1b: degradation facts that only a LOG carries
    py tools/verify/pattern_p1.py members     # P1a: result-struct members no transport publishes
    py tools/verify/pattern_p1.py control     # re-find the known positives, or FAIL

⛔ WHY A NEW TOOL AND NOT `pipe_wire_parity.py`. That script measures the MIRROR of this shape --
"published but nobody reads it" -- by comparing reply keys against C# consumers. P1 is the other
direction: a value the DLL works out that never reaches ANY transport. The distinction is not
academic. Of P1's five confirmed instances from the June sweep, FOUR would not be found by
`pipe_wire_parity.py` at all, because they never become reply keys:

    [W1-SNAP-FAULT]        scan.workerFaulted   -> one Sein::Warn, nothing else
    [W4-STRIDE-TENTATIVE]  "tentative" verdict  -> one LOG_WARN, nothing else
    [W3-XREF-CAP]          the maxResults cap   -> nothing at all
    [W4-RELATED-STOPS]     four stop conditions -> a bare std::vector with nowhere to put them

⛔⛔ THIS IS A POPULATION PRODUCER, NOT A FINDING LIST -- the same relationship
`pipe_wire_parity.py` has to its hits. Every row needs a hand-read, because a fact that is
genuinely internal, or is published under a different name, or is deliberately log-only, is not a
defect. The `logs` axis in particular has a LEGITIMATE non-empty population, which is exactly why
P1 is a SWEEP and must never become a CI gate: this repo's gate rule is to pick a predicate whose
legitimate population is EMPTY, and P1's is not. (P2 and P6 are the gate-shaped patterns.)

⭐ WHY THE `logs` AXIS IS THE SHARP ONE. Both of the June sweep's most serious P1 rows have the
same signature: the code DISTINGUISHES a degraded case, says so in prose to a log file nobody
reads during play, and records the distinction in no state a caller can see. Searching the log
MESSAGE TEXT for degradation vocabulary finds that shape directly -- and by construction it
re-finds both known positives, which is what `control` asserts.

⛔⛔ THE FIRST DRAFT HAD A TRIAGE FILTER, AND IT HID BOTH KNOWN POSITIVES. It ranked each row by
whether the enclosing function "publishes something a caller can see" and showed only the rows
where it did not -- 77 of 225. Both June positives were in the hidden 148: `DetectItemSize` DOES
publish (its packed verdict), just not the tentative one; `CaptureSnapshotChunk` DOES publish (its
result fields), just not the fault. **"The function publishes something" says nothing about whether
it publishes THIS fact**, which is the entire question -- so the filter had 0/2 recall on exactly
the shape it existed to find. And `control` passed anyway, because it checked the unfiltered set
while the view printed the filtered one. Two fixes, both structural:
  * the filter is GONE -- every row is shown, and a reader decides;
  * `logs` and `control` now share ONE `collect()`, so the control validates the exact population
    the view prints and can never again pass on a different set than the one a reader sees.
"""
from __future__ import annotations

import argparse
import os
import re
import sys

REPO = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..'))
DLL = os.path.join(REPO, 'dll', 'src')

LOG_CALL = re.compile(r'\b(?:Sein::(?:Info|Warn|Error|Debug)|LOG_(?:INFO|WARN|ERROR|DEBUG|SUMMARY))\s*\(')

# Prose that means "this answer is not the whole answer". Deliberately generous: the point is to
# enumerate a reviewable population, not to be precise.
# ⚠ NO TRAILING \b, AND THAT IS THE WHOLE POINT. The first draft had one, and `control` caught it
# immediately: these are STEMS, so `tentativ\b` does not match "tentatively", `degrad\b` misses
# "degraded" and `truncat\b` misses "truncated" -- it silently lost one of the two known positives.
# The leading \b is kept so a stem cannot match mid-word.
DEGRADED = re.compile(
    r'(?i)\b(partial|tentativ|unverified|degrad|fallback|fell back|could ?n[o\']t|cannot|can\'t|'
    r'unable|fail|missing|skipp|truncat|capp?ed|cap hit|refus|stale|incomplete|'
    r'not validated|gave up|giving up|abort|bail|no longer|assume|guess|approximat|'
    r'best.effort|may be wrong|not exact|unreliab)')

# The June sweep's confirmed P1 instances that this axis MUST re-find. A matcher that cannot
# re-find its own known positives is broken; that is the red-before-green control for this work.
KNOWN_POSITIVES = [
    ('Aura.cpp', 'worker FAULTED'),          # [W1-SNAP-FAULT]
    ('Aura.cpp', 'tentatively set'),         # [W4-STRIDE-TENTATIVE]
]

# Struct member names that are plumbing, not facts a consumer would want.
MEMBER_NOISE = re.compile(r'(?i)^(reserved|pad\d*|_\w*|size|count|len|length|capacity|data|ptr|p)$')


def cpp_files(exts=('.cpp',)):
    out = []
    for f in sorted(os.listdir(DLL)):
        if f.endswith(exts):
            out.append(os.path.join(DLL, f))
    return out


def read(path):
    with open(path, encoding='utf-8', errors='replace') as fh:
        return fh.read().split('\n')


def enclosing_function(lines, idx):
    """Best-effort: the nearest preceding line that looks like a function definition."""
    sig = re.compile(r'^[A-Za-z_][\w:<>,\s\*&]*\b(\w+)\s*\([^;]*$')
    for i in range(idx, max(-1, idx - 400), -1):
        s = lines[i]
        if s and not s[0].isspace() and '(' in s and not s.lstrip().startswith(('//', '*', '#')):
            m = sig.match(s)
            if m:
                return m.group(1)
    return '?'


def log_message(lines, idx):
    """Join the string literals of a log call that may span several lines."""
    buf = []
    for i in range(idx, min(len(lines), idx + 6)):
        buf.append(lines[i])
        if lines[i].count('(') <= lines[i].count(')') and i > idx:
            break
        if i == idx and lines[i].rstrip().endswith(';'):
            break
    joined = ' '.join(buf)
    return ' '.join(re.findall(r'"((?:[^"\\]|\\.)*)"', joined))


def collect():
    """THE population -- the one `logs` prints AND the one `control` validates. Never filter it
    in one caller and not the other: that is precisely how the first draft's control went green
    over a view that hid both known positives."""
    rows = []
    for path in cpp_files():
        lines = read(path)
        base = os.path.basename(path)
        for i, line in enumerate(lines):
            if not LOG_CALL.search(line):
                continue
            msg = log_message(lines, i)
            if msg and DEGRADED.search(msg):
                rows.append((base, i + 1, enclosing_function(lines, i), msg))
    return rows


def cmd_logs() -> int:
    rows = collect()
    print('\nP1b -- log calls whose MESSAGE carries a degradation fact: %d' % len(rows))
    print('\n⛔ A POPULATION, NOT A FINDING LIST. Every row needs a hand-read: is this fact ALSO')
    print('   recorded somewhere a caller can reach, and if not, what does a user lose?\n')
    for base, ln, fn, msg in rows:
        print('  %-20s :%-5d %-32s %s' % (base, ln, fn[:32], msg[:96]))
    return 0


def cmd_members() -> int:
    """P1a: members of DLL result/stats structs that no transport mentions by name."""
    transports = ''
    for name in ('Fern.cpp', 'Mimic.cpp', 'Frieren.cpp'):
        p = os.path.join(DLL, name)
        if os.path.exists(p):
            transports += '\n'.join(read(p))

    struct_re = re.compile(r'\bstruct\s+(\w*(?:Result|Stats|Info|State|Limits)\w*)\s*\{', re.I)
    member_re = re.compile(r'^\s*(?:const\s+)?[\w:<>,\s\*&]+?\b(\w+)\s*(?:=[^;]*)?;\s*(?://.*)?$')

    total = missing = 0
    out = []
    for path in cpp_files(('.h', '.hpp')):
        lines = read(path)
        base = os.path.basename(path)
        for i, line in enumerate(lines):
            m = struct_re.search(line)
            if not m:
                continue
            depth, j = 0, i
            body = []
            while j < len(lines):
                depth += lines[j].count('{') - lines[j].count('}')
                body.append((j + 1, lines[j]))
                if depth <= 0 and j > i:
                    break
                j += 1
            for ln, s in body[1:]:
                mm = member_re.match(s)
                if not mm:
                    continue
                name = mm.group(1)
                if MEMBER_NOISE.match(name) or len(name) < 3:
                    continue
                total += 1
                snake = re.sub(r'(?<!^)(?=[A-Z])', '_', name).lower()
                if re.search(r'\b%s\b' % re.escape(name), transports) or \
                   re.search(r'"%s"' % re.escape(snake), transports):
                    continue
                missing += 1
                out.append((base, ln, m.group(1), name, snake))

    print('\nP1a -- members of *Result/*Stats/*Info/*State/*Limits structs: %d' % total)
    print('   named by NO transport (Fern / Mimic / Frieren): %d' % missing)
    print('\n⛔ A POPULATION, NOT A FINDING LIST. A member can be internal by design, published')
    print('   under another name, or reached through a variable a literal grep cannot see.\n')
    for base, ln, st, name, snake in out:
        print('  %-20s :%-5d %-28s %-24s (%s)' % (base, ln, st[:28], name, snake))
    return 0


def cmd_control() -> int:
    """Red-before-green, over the SAME `collect()` population `logs` prints."""
    rows = collect()
    print('\nCONTROL -- does the population `logs` prints contain the June sweep\'s known'
          ' positives?  (%d rows)\n' % len(rows))
    bad = 0
    for want_file, want_text in KNOWN_POSITIVES:
        found = [r for r in rows if r[0] == want_file and want_text.lower() in r[3].lower()]
        ok = bool(found)
        bad += 0 if ok else 1
        print('  %-4s %-14s %-22r %s' % ('PASS' if ok else '****', want_file, want_text,
                                         ('%s:%d' % (found[0][0], found[0][1])) if ok
                                         else 'NOT IN THE PRINTED POPULATION'))
    print()
    if bad:
        print('*** %d known positive(s) missing from what a reader sees -- the axis is BROKEN'
              % bad)
        return 2
    print('all known positives are in the population a reader sees')
    return 0


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = ap.add_subparsers(dest='action', required=True)
    sub.add_parser('logs')
    sub.add_parser('members')
    sub.add_parser('control')
    a = ap.parse_args()
    if a.action == 'logs':
        return cmd_logs()
    if a.action == 'members':
        return cmd_members()
    return cmd_control()


if __name__ == '__main__':
    sys.stdout.reconfigure(encoding='utf-8')
    sys.exit(main())
