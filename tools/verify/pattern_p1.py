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

# A line that plausibly PUBLISHES something a caller can see: an out-param, a result struct member,
# a stats field, a return of a status. Used only to rank -- never to decide.
PUBLISHES = re.compile(
    r'(?:^|[^\w])(?:out|res|result|stats|st|o|r)\s*(?:\.|->)\s*\w+\s*=(?!=)'
    r'|\*\s*out\w*\s*=(?!=)'
    r'|\bdata\s*\[\s*"'
    r'|\breturn\s+(?:TP_ERR|ERR_|-?\d)')

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
                return m.group(1), i
    return '?', max(0, idx - 1)


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


def cmd_logs(show_all: bool) -> int:
    rows = []
    for path in cpp_files():
        lines = read(path)
        base = os.path.basename(path)
        for i, line in enumerate(lines):
            if not LOG_CALL.search(line):
                continue
            msg = log_message(lines, i)
            if not msg or not DEGRADED.search(msg):
                continue
            fn, fstart = enclosing_function(lines, i)
            body = '\n'.join(lines[fstart:min(len(lines), fstart + 400)])
            rows.append((base, i + 1, fn, bool(PUBLISHES.search(body)), msg))

    silent = [r for r in rows if not r[3]]
    print('\nP1b -- log calls carrying a DEGRADATION fact: %d' % len(rows))
    print('   of which the enclosing function publishes NOTHING a caller can see: %d'
          % len(silent))
    print('\n⛔ A POPULATION, NOT A FINDING LIST. Every row needs a hand-read.\n')

    for base, ln, fn, pub, msg in sorted(rows, key=lambda r: (r[3], r[0], r[1])):
        if pub and not show_all:
            continue
        print('  %-18s :%-5d %-34s %s' % (base, ln, fn[:34], msg[:96]))
    if not show_all:
        print('\n  (%d rows whose function DOES publish something were hidden; --all to see them)'
              % (len(rows) - len(silent)))
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
        print('  %-18s :%-5d %-28s %-24s (%s)' % (base, ln, st[:28], name, snake))
    return 0


def cmd_control() -> int:
    """Red-before-green: the axis must re-find the June sweep's confirmed instances."""
    hits = []
    for path in cpp_files():
        lines = read(path)
        base = os.path.basename(path)
        for i, line in enumerate(lines):
            if LOG_CALL.search(line):
                msg = log_message(lines, i)
                if msg and DEGRADED.search(msg):
                    hits.append((base, i + 1, msg))

    print('\nCONTROL -- can the logs axis re-find the June sweep\'s known positives?\n')
    bad = 0
    for want_file, want_text in KNOWN_POSITIVES:
        found = [h for h in hits if h[0] == want_file and want_text.lower() in h[2].lower()]
        ok = bool(found)
        bad += 0 if ok else 1
        print('  %-4s %-14s %-22r %s' % ('PASS' if ok else '****', want_file, want_text,
                                         ('%s:%d' % (found[0][0], found[0][1])) if ok
                                         else 'NOT RE-FOUND'))
    print()
    if bad:
        print('*** %d known positive(s) not re-found -- the matcher is BROKEN, do not trust its'
              ' output' % bad)
        return 2
    print('all known positives re-found; the axis is sound enough to enumerate with')
    return 0


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = ap.add_subparsers(dest='action', required=True)
    lg = sub.add_parser('logs')
    lg.add_argument('--all', action='store_true',
                    help='include rows whose enclosing function does publish something')
    sub.add_parser('members')
    sub.add_parser('control')
    a = ap.parse_args()
    if a.action == 'logs':
        return cmd_logs(a.all)
    if a.action == 'members':
        return cmd_members()
    return cmd_control()


if __name__ == '__main__':
    sys.stdout.reconfigure(encoding='utf-8')
    sys.exit(main())
