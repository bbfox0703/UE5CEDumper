r"""The 06-01..07-03 blank sweep -- clusters, hunks and the per-agent diet.

    py tools/verify/blank_sweep.py clusters            # the batching table
    py tools/verify/blank_sweep.py files SNAPSHOT      # one cluster's files
    py tools/verify/blank_sweep.py hunks SNAPSHOT      # the band's LINE RANGES, per file
    py tools/verify/blank_sweep.py months              # when the band's lines were authored

    ... --band aug                                     # the OTHER blank (>2026-08-03)

The band is author-time 2026-06-01 .. 07-03: the window that NO audit ever scoped. 50,451
surviving production lines across 211 files, the largest never-audited block in the tree.

WHY CLUSTERS AND NOT "N FILES PER AGENT". The defects the 2026-09 fix pass actually found sat
in the WIRE -- a field the DLL never creates, a consumer that never asks -- where no per-file
read can reach them. A batch that carries the DLL side, the pipe command and the UI consumer
together is the only shape that can see those. Line count is the SECONDARY key.

WHY `hunks` EXISTS. `clusters` prints a `band%` column: what fraction of the files an agent
must open is actually never-audited code. It runs 8% (CE-BRIDGE) to 95% (PIVOT-SPC). Handing
an 8% cluster's whole files to an agent means reading 5,613 lines to audit 451 -- and, worse,
the agent then reports findings in the 92% that five earlier audits already cleared. Low-band%
clusters get HUNKS; high-band% clusters get whole files. The split is measured, not guessed.

Blame and the production predicate are imported from audit_coverage.py, never re-implemented,
so the sweep and the coverage measurement cannot drift apart.
"""
from __future__ import annotations

import argparse
import os
import re
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import audit_coverage as ac  # noqa: E402

# The two never-audited bands, as (label, inclusive-lo, exclusive-hi) author-time epochs.
# `jun` is the 31.3% hole between audit #5's predicate and audit #3's window opening.
# `aug` is everything after audit #4 closed -- 19.7%, and it is STILL GROWING, which is the
# one structural difference between them: `jun` is a closed interval that can be swept once,
# `aug` accumulates every commit made since. Sweep it and it starts refilling the next day.
BANDS = {
    'jun': ('2026-06-01..07-03', 1780272000, 1783036800),
    'aug': ('>2026-08-03',       1785715200, 9999999999),
}
BAND_LABEL, LO, HI = BANDS['jun']

# (cluster, regex over the repo-relative path). FIRST match wins, so order is precedence.
# `ui/UE5DumpUI/` and `dll/src/` sit last as catch-alls; `clusters` fails loudly on anything
# that reaches neither, because a silently dropped subsystem is how a sweep reports 100%.
RULES = [
    ('SNAPSHOT',    r'(?i)snapshot'),
    ('PIVOT-SPC',   r'(?i)(pivot|/spc|spcquery|spcmodels|classfacet)'),
    ('TELEPORT',    r'(?i)(teleport|wirbel|laufen|solitar|edel|hemmung|solide|grausam|schlacht'
                    r'|movementscript|protectionscript|debugcamera|currenttarget|bugitgo)'),
    ('AURA-GRAPH',  r'(?i)(dll/src/aura|graphpath|dll/src/serie)'),
    ('WIRE',        r'(?i)(dll/src/fern|dll/src/mimic|dll/src/frieren|dll/src/stark'
                    r'|pipeclient|dll/src/tot)'),
    ('LIVEWALKER',  r'(?i)(livewalker|instancefinder|relatedobjects)'),
    ('VALUESEARCH', r'(?i)(valuesearch|dll/src/radar|dll/src/orden|groupmatch|valuescanmodels'
                    r'|floatround|roundmode)'),
    ('EXPORT',      r'(?i)(cexml|csxexport|cheattable|structreturn|dumpservice|export'
                    r'|bakedscript)'),
    ('SCAN-CORE',   r'(?i)dll/src/(genau|ubel|macht|himmel|denken|sein|linie|lugner)'),
    ('OBJTREE',     r'(?i)(objecttree|classstruct|pointerpanel|propertysearch|interesting'
                    r'|console|propertyxref|functionprops)'),
    ('WIRE-DTO',    r'(?i)ui/UE5DumpUI/(models|core)/'),
    ('CE-BRIDGE',   r'(?i)(aobmaker|aobusage|proxydeploy|scriptgenerator|parambuffer)'),
    ('APP-SHELL',   r'(?i)ui/UE5DumpUI/'),
    ('DLL-OTHER',   r'(?i)dll/src/'),
]

# Every audit doc in the repo is LATER than this band (the earliest is 2026-07-14, the band
# closes 07-03), so all six count when asking "did anyone even NAME this file?".
# Being NAMED is not being AUDITED -- this ranks risk, it certifies nothing.
AUDIT_DOCS = [
    'docs/audit-2026-07-14-findings.md',
    'docs/audit-2026-08-04-findings.md',
    'docs/audit-2026-08-13-early-code-findings.md',
    'docs/audit-2026-08-26-dxgi-appcompat-crash.md',
    'docs/audit-2026-09-05-vendor-ue582.md',
    'docs/archive/audit-2026-08-16-med-rederivation.md',
]

WHOLE_FILE_CUTOFF = 40.0   # band% at or above this -> the agent reads whole files


def cluster_of(f: str) -> str:
    for name, rx in RULES:
        if re.search(rx, f):
            return name
    return '*** UNASSIGNED'


def band_lines(f: str):
    """1-based line numbers of f whose blame author-time falls inside the band."""
    return [i for i, (_, t) in enumerate(ac.blame(f), 1) if LO <= t < HI]


def audit_blob() -> str:
    blob = ''
    for p in AUDIT_DOCS:
        try:
            with open(os.path.join(ac.REPO, p), encoding='utf-8', errors='replace') as fh:
                text = fh.read()
        except OSError:
            print('  ! missing audit doc: %s' % p, file=sys.stderr)
            continue
        keep, drop = [], False
        for line in text.split('\n'):
            if line.startswith('## '):
                drop = any(t in line for t in ac.SELF_REFERENTIAL_TAGS)
            keep.append('' if drop else line)
        blob += '\n'.join(keep)
    return blob


def scan(want=None):
    """-> {cluster: [(file, [band line numbers], named, whole_line_count)]}"""
    blob = audit_blob()
    out = {}
    files = ac.source_files(production_only=True)
    for n, f in enumerate(files):
        if want and cluster_of(f) != want:
            continue
        lines = band_lines(f)
        if not lines:
            continue
        base = os.path.basename(f)
        named = (f in blob
                 or re.search(r'(?<![\w./-])%s(?![\w])' % re.escape(base), blob) is not None)
        try:
            with open(os.path.join(ac.REPO, f), encoding='utf-8', errors='replace') as fh:
                whole = sum(1 for _ in fh)
        except OSError:
            whole = 0
        out.setdefault(cluster_of(f), []).append((f, lines, named, whole))
        if not want and n % 60 == 0:
            print('  ...%d/%d' % (n, len(files)), file=sys.stderr, flush=True)
    return out


def ranges(lines):
    """[3,4,5,9] -> [(3,5),(9,9)]"""
    out = []
    for ln in lines:
        if out and ln == out[-1][1] + 1:
            out[-1][1] = ln
        else:
            out.append([ln, ln])
    return [(a, b) for a, b in out]


def cmd_months() -> int:
    """Author-month histogram for the selected band.

    Only meaningful for `aug`, and that is the point: `jun` is a closed 32-day interval,
    while `aug` runs to HEAD and therefore contains code written days ago by the work that
    is doing the sweeping. Lines authored this week were reviewed in their own PR and are
    not 'never audited' in the sense the older months are -- so the histogram is what tells
    you how much of the band is genuinely stale.
    """
    import collections
    import time
    months = collections.Counter()
    files = ac.source_files(production_only=True)
    for n, f in enumerate(files):
        for _, t in ac.blame(f):
            if LO <= t < HI:
                months[time.strftime('%Y-%m', time.gmtime(t))] += 1
        if n % 60 == 0:
            print('  ...%d/%d' % (n, len(files)), file=sys.stderr, flush=True)
    tot = sum(months.values())
    print('\nband %s -- %d lines by author-month\n' % (BAND_LABEL, tot))
    for m in sorted(months):
        v = months[m]
        print('  %-9s %7d  %5.1f%%  %s'
              % (m, v, 100.0 * v / max(tot, 1), '#' * (v * 40 // max(tot, 1))))
    return 0


def cmd_clusters() -> int:
    g = scan()
    order = [r[0] for r in RULES] + ['*** UNASSIGNED']
    print('\n%-13s %7s %8s %6s %6s %8s %7s  %s'
          % ('cluster', 'band', 'WHOLE', 'files', 'band%', 'UNNAMED', 'un%', 'agent diet'))
    tb = tw = tf = tu = 0
    for name in order:
        rows = g.get(name)
        if not rows:
            continue
        b = sum(len(l) for _, l, _, _ in rows)
        w = sum(x for _, _, _, x in rows)
        u = sum(len(l) for _, l, nm, _ in rows if not nm)
        pct = 100.0 * b / max(w, 1)
        tb += b
        tw += w
        tf += len(rows)
        tu += u
        print('%-13s %7d %8d %6d %5.0f%% %8d %6.0f%%  %s'
              % (name, b, w, len(rows), pct, u, 100.0 * u / max(b, 1),
                 'whole files' if pct >= WHOLE_FILE_CUTOFF else 'HUNKS'))
    print('-' * 80)
    print('%-13s %7d %8d %6d %5.0f%% %8d %6.0f%%'
          % ('TOTAL', tb, tw, tf, 100.0 * tb / max(tw, 1), tu, 100.0 * tu / max(tb, 1)))
    if '*** UNASSIGNED' in g:
        print('\n*** %d file(s) matched NO rule -- fix RULES before trusting this table'
              % len(g['*** UNASSIGNED']))
        return 2
    return 0


def cmd_files(name: str) -> int:
    rows = scan(name).get(name, [])
    if not rows:
        print('no files in cluster %r' % name)
        return 2
    for f, lines, named, whole in sorted(rows, key=lambda r: -len(r[1])):
        print('%5d/%-5d %-7s %s' % (len(lines), whole, '' if named else 'UNNAMED', f))
    print('\n%d files, %d band lines of %d whole'
          % (len(rows), sum(len(l) for _, l, _, _ in rows), sum(w for _, _, _, w in rows)))
    return 0


def cmd_hunks(name: str) -> int:
    rows = scan(name).get(name, [])
    if not rows:
        print('no files in cluster %r' % name)
        return 2
    span = 0
    for f, lines, _, whole in sorted(rows, key=lambda r: -len(r[1])):
        rs = ranges(lines)
        span += sum(b - a + 1 for a, b in rs)
        print('%s  (%d band lines / %d, %d hunk(s))' % (f, len(lines), whole, len(rs)))
        print('   ' + ' '.join('%d-%d' % (a, b) if a != b else '%d' % a for a, b in rs))
    print('\n%d hunk lines across %d files' % (span, len(rows)))
    return 0


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument('--band', choices=sorted(BANDS), default='jun',
                    help="which never-audited band (default: jun, the 06-01..07-03 hole)")
    sub = ap.add_subparsers(dest='action', required=True)
    sub.add_parser('clusters')
    sub.add_parser('months')
    for a in ('files', 'hunks'):
        p = sub.add_parser(a)
        p.add_argument('cluster')
    a = ap.parse_args()

    global LO, HI, BAND_LABEL
    BAND_LABEL, LO, HI = BANDS[a.band]
    print('band: %s  (%s)' % (a.band, BAND_LABEL), file=sys.stderr)

    if a.action == 'clusters':
        return cmd_clusters()
    if a.action == 'months':
        return cmd_months()
    return cmd_files(a.cluster) if a.action == 'files' else cmd_hunks(a.cluster)


if __name__ == '__main__':
    sys.stdout.reconfigure(encoding='utf-8')
    sys.exit(main())
