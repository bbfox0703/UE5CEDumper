r"""P8 -- "a repaint that never fires": a display property that reads something no notification covers.

    py tools/verify/pattern_p8.py reads      # computed properties reading a PLAIN member (transitively)
    py tools/verify/pattern_p8.py ordering   # a plain member assigned AFTER its notifier already fired
    py tools/verify/pattern_p8.py control    # both axes must re-find the known positive, or FAIL

THE SHAPE. In an `ObservableObject`, a computed property the UI binds (`DisplayValue => ...`) repaints
only when something raises `PropertyChanged` for it. If it reads a PLAIN `{ get; set; }` member -- no
`[ObservableProperty]`, no `OnPropertyChanged` -- then changing that member changes nothing on screen.
The known positive is `[W1-CONTAINER-STALE]`'s secondary half: `LiveFieldValue.ArrayElements` is a
plain property that `DisplayValue` reads through `FormatArrayDisplay()`, and `CopyLiveValuesFrom`
assigns it AFTER `ArrayCount` -- whose `[NotifyPropertyChangedFor(nameof(DisplayValue))]` has already
fired. Count unchanged: nothing repaints. Count changed: it repaints while the OLD list is in place.

⛔ WHY TRANSITIVE. The first survey looked only at identifiers read directly inside `=> expr;` and
MISSED the known positive: `DisplayValue` reaches `ArrayElements` only through a same-class method.
Reads here follow calls into same-class methods and other computed properties, to a fixed point.

⭐ THE DISCRIMINATOR. A plain member set once, at construction, never needs a notification -- a data row
built by an object initializer is immutable in practice. So each `reads` row is split by whether the
member is RE-ASSIGNED after construction: a bare `P =` inside one of the class's own methods, or
`.P =` anywhere in the UI (an object initializer writes `P = x` with no dot, so a dot means a later
write). Only re-assigned members are candidates; the rest are listed for completeness.

⛔ A POPULATION PRODUCER, NOT A FINDING LIST. P8 is a SWEEP: a plain member can be legitimately
immutable in ways this heuristic cannot prove.

⚠ MEASURED LIMITS (the P4/P7/P8 adjudication, 2026-09-10, `[PATTERN-P4-P7-P8-2026-09-10]`).
3 of the 9 `reads` rows were artifacts, and the control stayed green:
  * the `.P =` match is by member NAME, not type -- `GroupSlotMatch.ClassName` writes
    (DumpService.cs:2191/2429) were attributed to `PropertySearchMatch`, which shares the name;
  * a READ, `PropertyName = match.PropName` (PropertySearchViewModel.cs:375), was counted as a write.
Both are false positives, which adjudication removes; neither can hide a real row.
"""
from __future__ import annotations

import argparse
import glob
import io
import os
import re
import sys

REPO = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..'))
UI = os.path.join(REPO, 'ui', 'UE5DumpUI')

PLAIN = re.compile(r'public\s+[\w<>\[\],.?\s]+?\s+(\w+)\s*\{\s*get;\s*(?:set|internal set|private set);\s*\}')
OBS_FIELD = re.compile(r'((?:\[[^\]]*\]\s*)+)(?:private|protected|internal)?\s*[\w<>\[\],.?]+\s+_?(\w+)\s*[=;]')
METHOD = re.compile(r'(?:private|public|internal|protected)\s+(?:static\s+)?(?:override\s+)?(?:async\s+)?'
                    r'[\w<>\[\],.?]+\s+(\w+)\s*\(')
EXPR_PROP = re.compile(r'public\s+(?:override\s+)?[\w<>\[\],.?]+\s+(\w+)\s*=>')
BLOCK_PROP = re.compile(r'public\s+(?:override\s+)?[\w<>\[\],.?]+\s+(\w+)\s*\{\s*get\b')


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
        elif src[i] == '"':
            j = i + 1
            while j < n and src[j] != '"':
                j += 2 if src[j] == '\\' else 1
            out.append('"' + re.sub(r'[{}()]', ' ', src[i + 1:j]) + '"')
            i = j + 1
        else:
            out.append(src[i])
            i += 1
    return ''.join(out)


def close(s: str, pos: int, o='{', c='}') -> int:
    d = 0
    for i in range(pos, len(s)):
        if s[i] == o:
            d += 1
        elif s[i] == c:
            d -= 1
            if d == 0:
                return i
    return -1


def to_semicolon(s: str, pos: int) -> int:
    d = 0
    for i in range(pos, len(s)):
        ch = s[i]
        if ch in '({[':
            d += 1
        elif ch in ')}]':
            d -= 1
        elif ch == ';' and d == 0:
            return i
    return len(s)


def observable_classes(files: dict) -> dict:
    """{class: (file, body)} for classes deriving ObservableObject (partials merged)."""
    out = {}
    for path, raw in files.items():
        s = strip(raw)
        for m in re.finditer(r'\bclass\s+(\w+)([^{;]*)\{', s):
            if 'ObservableObject' not in m.group(2) and m.group(1) not in out:
                continue
            op = m.end() - 1
            body = s[op:close(s, op)]
            f, b = out.get(m.group(1), (path, ''))
            out[m.group(1)] = (f, b + '\n' + body)
    return out


def analyse_class(body: str):
    plain = set(PLAIN.findall(body))
    notify = {}                                          # generated prop -> {targets}
    for m in OBS_FIELD.finditer(body):
        attrs = m.group(1)
        if 'ObservableProperty' not in attrs:
            continue
        n = m.group(2)
        prop = n[0].upper() + n[1:]
        notify[prop] = set(re.findall(r'NotifyPropertyChangedFor\s*\(\s*nameof\s*\(\s*(\w+)', attrs))
    bodies = {}
    for m in METHOD.finditer(body):
        op = body.find('(', m.end() - 1)
        cl = close(body, op, '(', ')')
        j = cl + 1
        while j < len(body) and body[j] not in '{;=':
            j += 1
        if j < len(body) and body[j] == '{':
            bodies[m.group(1)] = body[j:close(body, j)]
        elif body.startswith('=>', j):
            bodies[m.group(1)] = body[j:to_semicolon(body, j)]
    computed = {}
    for m in EXPR_PROP.finditer(body):
        computed[m.group(1)] = body[m.end():to_semicolon(body, m.end())]
    for m in BLOCK_PROP.finditer(body):
        op = body.find('{', m.end() - 4)
        blk = body[op:close(body, op)]
        if re.search(r'\b(?:set|init)\b', blk) or not re.search(r'\bget\s*(?:\{|=>)', blk):
            continue      # a full property, or an AUTO property ({ get; init; }) -- not computed
        computed[m.group(1)] = blk
    return plain, notify, bodies, computed


def reads_of(name: str, bodies: dict, computed: dict) -> set:
    seen, todo, ids = set(), [name], set()
    while todo:
        n = todo.pop()
        if n in seen:
            continue
        seen.add(n)
        text = computed.get(n) if n in computed else bodies.get(n, '')
        for i in re.findall(r'\b([A-Za-z_]\w*)\b', text or ''):
            ids.add(i)
            if (i in bodies or i in computed) and i not in seen:
                todo.append(i)
    return ids


def load(root=UI) -> dict:
    out = {}
    for p in glob.glob(os.path.join(root, '**', '*.cs'), recursive=True):
        s = p.replace('\\', '/')
        if '/obj/' in s or '/bin/' in s or '.Tests' in s:
            continue
        out[os.path.relpath(p, REPO).replace('\\', '/')] = io.open(p, encoding='utf-8', errors='replace').read()
    return out


def collect_reads(files: dict) -> list:
    """-> [(file, class, computed, plain_member, reassigned_where)]"""
    stripped = {f: strip(t) for f, t in files.items()}
    rows = []
    for cls, (path, body) in observable_classes(files).items():
        plain, notify, bodies, computed = analyse_class(body)
        if not plain:
            continue
        # A `.P =` counts only in a file that NAMES this class. `ClassName` / `PropName` are common
        # member names: the first run matched them tree-wide and marked every PropertySearchMatch
        # member "re-assigned" on the strength of other types' assignments.
        mentioning = [f for f, t in stripped.items() if re.search(r'\b%s\b' % re.escape(cls), t)]
        for d in computed:
            for p in sorted(reads_of(d, bodies, computed) & plain):
                where = [mn for mn, b in bodies.items() if re.search(r'(?<![.\w])%s\s*=(?!=)' % p, b)]
                where += ['.%s= in %s' % (p, os.path.basename(f)) for f in mentioning
                          if re.search(r'\.%s\s*=(?!=)' % re.escape(p), stripped[f])]
                rows.append((path, cls, d, p, where))
    return rows


def collect_ordering(files: dict) -> list:
    """-> [(file, class, method, notifier, plain_member, display)] -- plain assigned after notifier."""
    rows = []
    for cls, (path, body) in observable_classes(files).items():
        plain, notify, bodies, computed = analyse_class(body)
        for mname, mbody in bodies.items():
            order = [(a.start(), a.group(1)) for a in
                     re.finditer(r'(?<![.\w])(\w+)\s*=(?!=)', mbody)]
            pos = {}
            for at, n in order:
                pos.setdefault(n, at)
            for m_, targets in notify.items():
                if m_ not in pos:
                    continue
                for d in targets:
                    for p in reads_of(d, bodies, computed) & plain:
                        if p in pos and pos[p] > pos[m_]:
                            tail = mbody[pos[p]:]
                            if re.search(r'OnPropertyChanged\s*\(\s*(?:nameof\s*\(\s*)?"?%s' % d, tail):
                                continue
                            rows.append((path, cls, mname, m_, p, d))
    return rows


def cmd_reads() -> int:
    rows = collect_reads(load())
    live = [r for r in rows if r[4]]
    print('\nP8a -- computed properties reading a PLAIN member (transitively): %d' % len(rows))
    print('   of which the member is RE-ASSIGNED after construction (the candidates): %d\n' % len(live))
    for path, cls, d, p, where in sorted(rows, key=lambda r: (not r[4], r[0], r[1])):
        tag = 'RE-ASSIGNED in %s' % ', '.join(where) if where else 'set at construction only'
        print('  %-34s %-22s %-24s <- %-22s %s' % (os.path.basename(path), cls, d, p, tag))
    return 0


def cmd_ordering() -> int:
    rows = collect_ordering(load())
    grouped = {}
    for path, cls, mname, m_, p, d in rows:
        g = grouped.setdefault((os.path.basename(path), cls, mname, p), [set(), set()])
        g[0].add(m_)
        g[1].add(d)
    print('\nP8b -- a plain member assigned AFTER the notifier that repaints its reader: %d distinct'
          ' (%d raw notifier x reader pairs)\n' % (len(grouped), len(rows)))
    for (f, cls, mname, p), (ms, ds) in sorted(grouped.items()):
        print('  %s  %s.%s: plain %s is assigned after %d notifier(s) [%s] that repaint %s'
              % (f, cls, mname, p, len(ms), ', '.join(sorted(ms)), ', '.join(sorted(ds))))
    return 0


def cmd_control() -> int:
    files = load()
    a = any(c == 'LiveFieldValue' and d == 'DisplayValue' and p == 'ArrayElements' and w
            for _, c, d, p, w in collect_reads(files))
    b = any(c == 'LiveFieldValue' and mn == 'CopyLiveValuesFrom' and p == 'ArrayElements'
            for _, c, mn, _, p, _ in collect_ordering(files))
    print('\nCONTROL -- the known positive [W1-CONTAINER-STALE] (secondary: ArrayElements)\n')
    print('  %-4s reads     LiveFieldValue.DisplayValue <- ArrayElements, re-assigned' % ('PASS' if a else '****'))
    print('  %-4s ordering  CopyLiveValuesFrom assigns ArrayElements after its notifier' % ('PASS' if b else '****'))
    print()
    if not (a and b):
        print('*** an axis cannot re-find its known positive -- it is BROKEN; do not trust it')
        return 2
    print('both axes re-find the known positive')
    return 0


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = ap.add_subparsers(dest='action', required=True)
    for a in ('reads', 'ordering', 'control'):
        sub.add_parser(a)
    a = ap.parse_args()
    return {'reads': cmd_reads, 'ordering': cmd_ordering, 'control': cmd_control}[a.action]()


if __name__ == '__main__':
    sys.stdout.reconfigure(encoding='utf-8')
    sys.exit(main())
