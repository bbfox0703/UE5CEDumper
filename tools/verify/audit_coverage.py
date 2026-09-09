r"""`[A4-ASSESS-2026-09-09]` — which audit's scope could have contained each surviving line?

    py tools/verify/audit_coverage.py timeline          # every production line, bucketed
    py tools/verify/audit_coverage.py window LO..HI     # survivors of one audit's window
    py tools/verify/audit_coverage.py blank LO..HI      # ...that no LATER audit doc names

⛔ WHY THIS EXISTS, AND WHAT IT CAN AND CANNOT SAY.
The audit #3 re-check (2026-09-08) established the argument this tool automates: **a line still
blamed into a commit range is a line no later commit touched**, so any audit whose baseline sits at
or after that range covers exactly ZERO of those lines *by construction* — not as an estimate.
That is what makes "never re-swept" a measurement rather than a guess.

⚠ It measures SCOPE, not attention. A line inside an audit's window is a line that audit *could*
have read; nothing here says a human did. And `blank`'s "no later audit document names this file" is
a GENEROUS proxy in both directions — being mentioned is not being audited — so its line count is a
LOWER BOUND on the blank, never an upper one.

⚠ Bucket boundaries are AUTHOR-TIME, which is what `git blame --line-porcelain` reports and what
audit #5's own scoping measurement used. Committer-time would drift on any rebase.

The audit windows, taken from the audit documents themselves (derive, never hand-edit):
  audit #3  88ee170..af2ce50   2026-07-03 .. 07-15   (build 1872 -> 2168)
  audit #4  af2ce50..7cc3d5e2  2026-07-15 .. 08-03   (build 2168 -> 2554)
  audit #5  predicate: authored before 2026-06-01    -> disjoint from BOTH by construction
  audit #6  vendor UE 5.8.2, ProcessEvent vtable     -> narrow
"""
from __future__ import annotations

import argparse
import collections
import os
import re
import subprocess
import sys

REPO = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..'))

# Bands, as (label, inclusive-lo epoch, exclusive-hi epoch). The two audit windows are
# expressed as dates rather than commit ranges so a line authored in the window but landed
# by a later merge still lands in the right bucket.
BANDS = [
    ('<2026-06-01   audit #5 scope',        0,          1780272000),
    ('06-01..07-03  NO AUDIT EVER',         1780272000, 1783036800),
    ('07-03..07-15  audit #3',              1783036800, 1784073600),
    ('07-15..08-03  audit #4',              1784073600, 1785715200),
    ('>2026-08-03   no area audit',         1785715200, 9999999999),
]

LATER_AUDIT_DOCS = [
    'docs/audit-2026-08-13-early-code-findings.md',
    'docs/audit-2026-08-26-dxgi-appcompat-crash.md',
    'docs/audit-2026-09-05-vendor-ue582.md',
    'docs/archive/audit-2026-08-16-med-rederivation.md',
    'docs/todo.md',
]

SRC_EXT = ('.cpp', '.h', '.hpp', '.cs', '.py', '.lua', '.axaml', '.ps1', '.asm', '.CT')

# ⛔ SELF-REFERENCE GUARD. `docs/todo.md` is in LATER_AUDIT_DOCS because the sweeps live
# there — but it also carries `[A4-ASSESS-2026-09-09]`, whose whole content is the LIST of
# blank files. Left in, the report of the blank is what erases the blank: the first run
# after the section landed reported 22 files / 1,178 lines where the run that produced the
# section reported 31 / 3,409. Any section whose heading carries one of these tags is cut
# from the corpus before matching. Add a tag here if a later section ever re-lists them.
SELF_REFERENTIAL_TAGS = ('[A4-ASSESS-2026-09-09]',)


def git(*a: str) -> str:
    return subprocess.run(['git'] + list(a), capture_output=True, text=True,
                          cwd=REPO, errors='replace').stdout


def is_production(f: str) -> bool:
    return f.startswith('dll/src/') or (f.startswith('ui/UE5DumpUI/') and '.Tests' not in f)


def source_files(production_only: bool):
    out = []
    for f in git('ls-files').split('\n'):
        f = f.strip()
        if not f or not f.endswith(SRC_EXT):
            continue
        if f.startswith(('docs/', 'external/', 'third_party/')):
            continue
        if production_only and not is_production(f):
            continue
        out.append(f)
    return out


def blame(f: str):
    """Yield (sha, author_time) per line of f at HEAD."""
    sha = None
    for line in git('blame', '--line-porcelain', 'HEAD', '--', f).split('\n'):
        if len(line) > 40 and line[40:41] == ' ' and re.fullmatch(r'[0-9a-f]{40}', line[:40]):
            sha = line[:40]
        elif line.startswith('author-time ') and sha:
            yield sha, int(line.split()[1])
            sha = None


def cmd_timeline() -> int:
    files = source_files(production_only=True)
    print('production files: %d' % len(files), flush=True)
    buckets = collections.Counter()
    for n, f in enumerate(files):
        for _, t in blame(f):
            for label, lo, hi in BANDS:
                if lo <= t < hi:
                    buckets[label] += 1
                    break
        if n % 60 == 0:
            print('  ...%d/%d' % (n, len(files)), flush=True)

    tot = sum(buckets.values())
    print('\nsurviving production lines at HEAD: %d\n' % tot)
    unscoped = 0
    for label, _, _ in BANDS:
        v = buckets[label]
        print('  %-30s %7d  %4.1f%%' % (label, v, 100.0 * v / max(tot, 1)))
        if 'NO AUDIT EVER' in label or 'no area audit' in label:
            unscoped += v
    print('\n  never inside ANY area audit scope: %d  (%.1f%%)'
          % (unscoped, 100.0 * unscoped / max(tot, 1)))
    return 0


def cmd_window(rng: str, blank: bool) -> int:
    lo, hi = rng.split('..')
    window = set(x.strip() for x in git('rev-list', '%s..%s' % (lo, hi)).split('\n') if x.strip())
    if not window:
        print('*** empty commit range %s -- check the two revisions exist' % rng)
        return 2
    print('commits in window %s: %d' % (rng, len(window)), flush=True)

    files = source_files(production_only=False)
    alive: dict[str, int] = {}
    for n, f in enumerate(files):
        c = sum(1 for sha, _ in blame(f) if sha in window)
        if c:
            alive[f] = c
        if n % 100 == 0:
            print('  ...%d/%d' % (n, len(files)), flush=True)

    prod = {f: c for f, c in alive.items() if is_production(f)}
    print('\nlines still alive at HEAD from this window: %d across %d files'
          % (sum(alive.values()), len(alive)))
    print('  production : %6d across %3d files' % (sum(prod.values()), len(prod)))
    print('  other      : %6d across %3d files'
          % (sum(alive.values()) - sum(prod.values()), len(alive) - len(prod)))

    if not blank:
        for f, c in sorted(alive.items(), key=lambda kv: -kv[1])[:30]:
            print('  %5d  %s' % (c, f))
        return 0

    # ⚠ Generous on purpose: a file MENTIONED by a later audit doc is treated as covered,
    # which can only shrink the reported blank. The number is a floor.
    blob = ''
    cut = 0
    for p in LATER_AUDIT_DOCS:
        try:
            with open(os.path.join(REPO, p), encoding='utf-8', errors='replace') as fh:
                text = fh.read()
        except OSError:
            print('  ! missing later-audit doc: %s' % p)
            continue
        # Drop any `## ...` section whose heading carries a self-referential tag.
        keep, drop = [], False
        for line in text.split('\n'):
            if line.startswith('## '):
                drop = any(t in line for t in SELF_REFERENTIAL_TAGS)
                cut += 1 if drop else 0
            keep.append('' if drop else line)
        blob += '\n'.join(keep)
    if cut:
        print('  (self-reference guard: %d section(s) excluded from the corpus)' % cut)

    def named(f: str) -> bool:
        base = os.path.basename(f)
        return f in blob or re.search(r'(?<![\w./-])%s(?![\w])' % re.escape(base), blob) is not None

    gap = {f: c for f, c in prod.items() if not named(f)}
    print('\nPRODUCTION files carrying window lines that NO later audit document names:')
    print('  %d files / %d lines  (a LOWER BOUND -- see this file\'s header)'
          % (len(gap), sum(gap.values())))
    for f, c in sorted(gap.items(), key=lambda kv: -kv[1]):
        print('   %5d  %s' % (c, f))
    return 0


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = ap.add_subparsers(dest='action', required=True)
    sub.add_parser('timeline')
    w = sub.add_parser('window')
    w.add_argument('range', help='LO..HI, e.g. af2ce50..7cc3d5e2')
    b = sub.add_parser('blank')
    b.add_argument('range', help='LO..HI, e.g. af2ce50..7cc3d5e2')
    a = ap.parse_args()

    if a.action == 'timeline':
        return cmd_timeline()
    return cmd_window(a.range, blank=(a.action == 'blank'))


if __name__ == '__main__':
    sys.exit(main())
