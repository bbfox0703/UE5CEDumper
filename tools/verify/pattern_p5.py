r"""P5 -- "a cap conflated with a deadline or a cancel": one flag, several stop causes, one wrong reason.

    py tools/verify/pattern_p5.py flags     # DLL: a flag assigned from >= 2 distinct stop causes
    py tools/verify/pattern_p5.py wire      # every stop key the pipe publishes, and what feeds it
    py tools/verify/pattern_p5.py ui        # every UI rendering of a stop flag, and the causes its TEXT names
    py tools/verify/pattern_p5.py control   # each axis must re-find its known positives, or FAIL

THE SHAPE. A single boolean that means "the scan stopped early" for more than one reason -- the clock
ran out, the user cancelled, a worker faulted, the result cap was hit. Each cause tells the user to do
something different (raise the timeout, narrow the predicate, re-run, nothing at all). A consumer
that names ONE cause for a flag that carries several gives the wrong advice for the rest.

KNOWN POSITIVES: `deadline_hit` forced by the result CAP (`Aura.cpp` ~8257, drifted from the 8244-8247
the June sweep cited); `scan.incomplete()` folding clock, cancel and worker fault into one bit;
`FindReferences`' `(deadlineHit && matches.empty()) || workerFaulted`; the D2 residual -- a worker fault
shown to the user as a deadline (grep docs/todo.md for "D2's deliberate residual").
⚠ NOT "all recorded", as this paragraph first said: the CAP half is filed NOWHERE -- its only trace is a
rig note in docs/verification-register.md ("non-fault causes"). The `docs/todo.md:1314` / `:1738` this
paragraph cited were line numbers that never held / no longer held those rows.

⛔ THE UI AXIS FIRST SAW A RENDERING ONLY WHEN ITS OWN LINE HELD `if (`, `?`, `&&` or `||`, and dropped
36 flag reads. About half were real renderings: every ternary broken across lines (`var capSuffix =
r.Aborted` / `? "..."`), and every flag passed to a renderer as an argument -- including
`PartialResultNotice.ScanSuffix(..., cs.DeadlineHit, ...)`, the ONLY caller of `DeadlineClause`. The
control now asserts both shapes. Still not followed: a flag stored in a field and rendered elsewhere
(`_scanTruncated = result.DeadlineHit`) -- one hop away, and a reader's job.

⚠ MEASURED LIMITS (the P5 adjudication, 2026-09-10, `[PATTERN-P5-2026-09-10]`):
  * the flags axis misses a flag assigned to a LOCAL and copied into stats later -- FindReferencesToUObject
    (`bool deadlineHit = scan.incomplete();`, Aura.cpp ~3884) is reached only through its wire row;
  * a cancel that arrives through `scan.deadlineHit` (Tot sets it) is not expanded, so FindInContainersDeep
    is listed as clock + fault and is really clock + fault + cancel-when-empty;
  * `per_slot_cap` (a number, not a stop flag) is pulled in by key name;
  * the "text names" heuristic was wrong on ~12 of 51 UI rows -- it reads catch blocks, comments and the
    OTHER branch of a ternary. It says where to look, never what the text says.

⚠ THE "TEMPLATE" IS ONLY HALF A TEMPLATE. The June sweep named `begin_group_scan` the model to copy:
`deadline_hit`, `per_slot_cap_hit` and `per_slot_cap` as distinct fields. The per-slot cap IS
separate -- but the survey for this tool found that the group scan's own `deadline_hit` is still set
for a cancel (`Tot::Requested`), a timeout AND the candidate cap (`// truncated (cap hit)`). The shape
worth copying is its per-slot half; its deadline half is itself an instance.

⛔ HOW CAUSES ARE READ. Only the GUARD and the right-hand side are classified -- never the flag's own
name, which would make every `deadlineHit = true` look like a clock cause. The guard is the assignment
line plus the lines above it back to the previous statement. `incomplete()` expands to clock + cancel +
fault. Causes are aggregated per (function, flag), because a single site rarely conflates anything --
the conflation is two sites writing the same flag for different reasons.

⛔ THE WIRE AXIS MATCHES ANY JSON OBJECT, NOT `data[`. Four of the six `deadline_hit` publish sites
write through `scanInfo[...]`; a `data[`-only matcher sees two and reports a clean, wrong, smaller
population -- the same class of mistake as the P6 detector's second bug. The control asserts six.

⛔ A POPULATION PRODUCER, NOT A FINDING LIST. P5 is a SWEEP: some merges are CORRECT
(`LI_OUT_TRUNCATED` means "the returned set is a prefix", which is true for both of its causes,
`Mimic.h:243-246`), and deciding that is a reader's job.
"""
from __future__ import annotations

import argparse
import collections
import glob
import io
import os
import re
import sys

REPO = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..'))
DLL = os.path.join(REPO, 'dll', 'src')
UI = os.path.join(REPO, 'ui', 'UE5DumpUI')

FLAG_LHS = re.compile(r'([A-Za-z_][\w.>-]*?)(\w*(?:Hit|[Tt]runcated|[Aa]borted|[Cc]ancelled|Faulted|[Ii]ncomplete))'
                      r'\s*(\|=|=)(?!=)\s*(.+?);')
CAUSES = {
    'cap':    re.compile(r'>=\s*\w*(?:max|Max|limit|Limit|cap|Cap)\w*|\bmax\w*Results?\b|\bkMax\w+|size\(\)\s*>=|[Cc]apHit'),
    'clock':  re.compile(r'[Dd]eadline|steady_clock|\bt0\b|[Tt]imeout|\bdt\s*>'),
    'cancel': re.compile(r'Tot::Requested|ShutdownRequested|[Cc]ancel'),
    'fault':  re.compile(r'[Ff]aulted|\bfault'),
}
EXPANDS = {'incomplete()': {'clock', 'cancel', 'fault'}}
STOP_KEY = re.compile(r'\b(\w+)\["([a-z_]*(?:deadline|truncat|abort|cancel|cap|budget|incomplete|partial|skip)[a-z_]*)"\]'
                      r'\s*=\s*(.+?);')
UI_FLAG = re.compile(r'\.(DeadlineHit|Truncated|Aborted|BudgetHit|PerSlotCapHit|Incomplete)\b')
TEXT_CAUSES = {
    'clock':   re.compile(r'deadline|timeout|timed out|\bs deadline|seconds|Timeout slider', re.I),
    'cap':     re.compile(r'\bcap\b|capped|result cap|\blimit\b|\btop\s+\{|showing first', re.I),
    'cancel':  re.compile(r'cancel', re.I),
    'fault':   re.compile(r'fault|failed', re.I),
    'neutral': re.compile(r'partial|incomplete|lower bound|a floor|CellMarker|PartialResultNotice', re.I),
}


def strip(src: str) -> str:
    out, i, n = [], 0, len(src)
    while i < n:
        if src.startswith('//', i):
            j = src.find('\n', i)
            j = n if j < 0 else j
            out.append(' ' * (j - i))
            i = j
        elif src.startswith('/*', i):
            j = src.find('*/', i + 2)
            j = n if j < 0 else j + 2
            out.append(re.sub(r'[^\n]', ' ', src[i:j]))
            i = j
        else:
            out.append(src[i])
            i += 1
    return ''.join(out)


def enclosing_function(lines, idx):
    sig = re.compile(r'^[A-Za-z_][\w:<>,\s\*&]*\b(\w+)\s*\([^;]*$')
    # 3000, not 600: the value scan is longer than 600 lines, and its row printed as "?()".
    for i in range(idx, max(-1, idx - 3000), -1):
        s = lines[i]
        if s and not s[0].isspace() and '(' in s and not s.lstrip().startswith(('#', '}')):
            m = sig.match(s)
            if m and m.group(1) not in ('if', 'for', 'while', 'switch', 'return'):
                return m.group(1)
    return '?'


def guard_text(lines, idx, rhs):
    """The assignment's RHS plus the lines above it back to the previous statement."""
    parts = [rhs, lines[idx].split('=')[0].rsplit(None, 1)[0] if '(' in lines[idx].split('=')[0] else '']
    for k in range(idx - 1, max(-1, idx - 4), -1):
        s = lines[k].strip()
        if not s:
            continue
        if s.endswith(';') or s.endswith('}'):
            break
        parts.append(s)
    return ' '.join(parts)


def causes_of(text: str) -> set:
    found = {c for c, rx in CAUSES.items() if rx.search(text)}
    for k, v in EXPANDS.items():
        if k in text:
            found |= v
    return found


def collect_flags():
    """-> {(file, function, flag): [(line, causes, snippet)]} for every stop-flag assignment."""
    out = collections.defaultdict(list)
    for p in sorted(glob.glob(os.path.join(DLL, '*.cpp'))):
        lines = strip(io.open(p, encoding='utf-8', errors='replace').read()).split('\n')
        for i, l in enumerate(lines):
            m = FLAG_LHS.search(l)
            if not m:
                continue
            flag, rhs = m.group(2), m.group(4).strip()
            if rhs.startswith('[') or re.search(r'\b(?:bool|int|auto|const)\s+\w*$', l[:m.start(2)]):
                continue                                  # a lambda, or a local declaration
            c = causes_of(guard_text(lines, i, rhs))
            out[(os.path.basename(p), enclosing_function(lines, i), flag)].append((i + 1, c, l.strip()))
    return out


def multi_cause(flags: dict) -> list:
    rows = []
    for key, sites in flags.items():
        allc = set().union(*[c for _, c, _ in sites]) if sites else set()
        if len(allc) >= 2:
            rows.append((key, allc, sites))
    return sorted(rows, key=lambda r: (r[0][0], r[0][1]))


def collect_wire():
    text = strip(io.open(os.path.join(DLL, 'Fern.cpp'), encoding='utf-8', errors='replace').read())
    rows = []
    for m in STOP_KEY.finditer(text):
        rows.append((m.group(2), text.count('\n', 0, m.start()) + 1, m.group(1), m.group(3).strip()))
    return rows


def collect_ui():
    rows = []
    for p in sorted(glob.glob(os.path.join(UI, '**', '*.cs'), recursive=True)):
        s = p.replace('\\', '/')
        if '/obj/' in s or '/bin/' in s or '.Tests' in s:
            continue
        lines = io.open(p, encoding='utf-8', errors='replace').read().split('\n')
        for i, l in enumerate(lines):
            m = None if l.lstrip().startswith('//') else UI_FLAG.search(l)   # doc comments are not renderings
            if not m:
                continue
            # A rendering is a flag that DECIDES something: a conditional on its own line OR THE NEXT
            # (C# breaks `var x = r.Aborted` / `? "..." : ...` across lines), or the flag handed to a
            # call as an argument -- `ScanSuffix(..., cs.DeadlineHit, ...)`, the only DeadlineClause
            # emitter, was invisible to the one-line predicate this replaced.
            nxt = lines[i + 1] if i + 1 < len(lines) else ''
            cond = re.search(r'\bif\s*\(|\?|&&|\|\|', l) or re.match(r'\s*[?:]', nxt)
            arg = re.search(re.escape(m.group(0)) + r'\s*[,)]', l)
            if not (cond or arg):
                continue
            window = ' '.join(lines[i:i + 3])
            named = {c for c, rx in TEXT_CAUSES.items() if rx.search(window)}
            has_text = bool(re.search(r'"[^"]{4,}"|StaticResource|Res\.Get|PartialResultNotice|Marker', window))
            rows.append((os.path.relpath(p, REPO).replace('\\', '/'), i + 1, m.group(1), named,
                         has_text, l.strip()))
    return rows


def cmd_flags() -> int:
    rows = multi_cause(collect_flags())
    print('\nP5a -- DLL stop flags written for >= 2 DISTINCT causes, per (function, flag): %d\n' % len(rows))
    for (f, fn, flag), allc, sites in rows:
        print('  %s  %s()  %s   causes: %s' % (f, fn, flag, ', '.join(sorted(allc))))
        for ln, c, snip in sites:
            print('      :%-5d %-22s %s' % (ln, ','.join(sorted(c)) or '-', snip[:96]))
    return 0


def cmd_wire() -> int:
    rows = collect_wire()
    by = collections.Counter(k for k, _, _, _ in rows)
    print('\nP5b -- stop keys the pipe publishes: %d sites, %d keys  (%s)\n'
          % (len(rows), len(by), ', '.join('%s x%d' % kv for kv in by.most_common())))
    for key, ln, obj, rhs in sorted(rows):
        print('  %-18s Fern.cpp:%-5d %s["%s"] = %s' % (key, ln, obj, key, rhs[:80]))
    return 0


def cmd_ui() -> int:
    rows = collect_ui()
    print('\nP5c -- UI renderings of a stop flag: %d (text-bearing: %d)\n'
          % (len(rows), sum(1 for r in rows if r[4])))
    for path, ln, flag, named, has_text, snip in rows:
        tag = ','.join(sorted(named)) if named else ('(no cause named)' if has_text else '(no text)')
        print('  %-50s :%-5d %-14s text names: %-22s %s'
              % (path[len('ui/UE5DumpUI/'):] if path.startswith('ui/UE5DumpUI/') else path, ln, flag,
                 tag, snip[:70]))
    return 0


def cmd_control() -> int:
    flags = multi_cause(collect_flags())
    a1 = any(f == 'Aura.cpp' and flag == 'deadlineHit' and 'cap' in allc and len(allc) >= 2
             for (f, _, flag), allc, _ in flags)
    a2 = any(f == 'Aura.cpp' and flag == 'deadlineHit' and 'fault' in allc and len(allc) >= 2
             for (f, _, flag), allc, _ in flags)
    wire = collect_wire()
    n_deadline = sum(1 for k, _, _, _ in wire if k == 'deadline_hit')
    b = n_deadline == 6
    ui = collect_ui()
    c = any('ValueSearchViewModel' in p and flag == 'DeadlineHit' and {'clock', 'cap'} <= named
            for p, _, flag, named, _, _ in ui)
    d = any('InstanceFinderViewModel' in p and flag == 'DeadlineHit' and 'cs.DeadlineHit' in snip
            for p, _, flag, _, _, snip in ui)
    e = any(snip.startswith('var capSuffix = ') for _, _, _, _, _, snip in ui)
    print('\nCONTROL -- does every axis re-find its known positives, over what it prints?\n')
    print('  %-4s flags  a cap forces a deadline-named flag (Aura.cpp ~8257 / ~9460)' % ('PASS' if a1 else '****'))
    print('  %-4s flags  a fault shares a deadline-named flag (Aura.cpp ~3052, incomplete())' % ('PASS' if a2 else '****'))
    print('  %-4s wire   all SIX deadline_hit publish sites seen, not just the data[...] two  (saw %d)'
          % ('PASS' if b else '****', n_deadline))
    print('  %-4s ui     ValueSearch renders DeadlineHit as "deadline / result cap"' % ('PASS' if c else '****'))
    print('  %-4s ui     a flag passed to a renderer as an ARGUMENT (ScanSuffix(..., cs.DeadlineHit, ...))'
          % ('PASS' if d else '****'))
    print('  %-4s ui     a ternary broken across lines (`var capSuffix = r.Aborted` / `? ...`)'
          % ('PASS' if e else '****'))
    print()
    if not (a1 and a2 and b and c and d and e):
        print('*** an axis cannot re-find its known positive -- it is BROKEN; do not trust it')
        return 2
    print('every axis re-finds its known positives')
    return 0


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = ap.add_subparsers(dest='action', required=True)
    for a in ('flags', 'wire', 'ui', 'control'):
        sub.add_parser(a)
    a = ap.parse_args()
    return {'flags': cmd_flags, 'wire': cmd_wire, 'ui': cmd_ui, 'control': cmd_control}[a.action]()


if __name__ == '__main__':
    sys.stdout.reconfigure(encoding='utf-8')
    sys.exit(main())
