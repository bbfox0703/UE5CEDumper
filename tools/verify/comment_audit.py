"""Measure how stale the code COMMENTS are, mechanically -- the rig behind docs/comment-integrity-eval.md.

    py tools/verify/comment_audit.py [--roots dll/src ui/UE5DumpUI scripts] [--blame]

Not a gate: it measures. The checks that proved worth gating are proposed in the eval doc.

Checks, per comment line:
  A1 file:line references  -- target file in the repo? line in range? --blame: did the referenced line MOVE
                              since the comment was written (git blame -> that commit's copy of the target)?
  A2 symbol references     -- `Ns::Name` (C++) / `Type.Member(` (C#) whose Name never appears outside comments
  A3 [TAG] references      -- a finding tag no doc (docs/**, archive included) records
  A4 doc references        -- working-lessons section numbers that do not exist; *.md files that moved/vanished
Writes out/comment_audit/report.json and prints a summary.
"""
import collections, json, os, re, subprocess, sys

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..'))
os.chdir(ROOT)
args = sys.argv[1:]
BLAME = '--blame' in args
roots = ['dll/src', 'ui/UE5DumpUI', 'scripts']
if '--roots' in args:
    i = args.index('--roots'); roots = []
    for a in args[i + 1:]:
        if a.startswith('--'): break
        roots.append(a)

files = subprocess.run(['git', 'ls-files'], capture_output=True, text=True, encoding='utf-8').stdout.split('\n')
files = [f for f in files if f]
by_base = collections.defaultdict(list)
for f in files:
    by_base[os.path.basename(f).lower()].append(f)
EXT = ('.cpp', '.h', '.hpp', '.cs', '.axaml', '.lua', '.CT', '.py')
targets = [f for f in files if f.startswith(tuple(r.rstrip('/') + '/' for r in roots)) and f.endswith(EXT)]


def read(p):
    b = open(p, 'rb').read()
    return b.decode('utf-8-sig', errors='replace').replace('\r\n', '\n').split('\n')


def comments(path, lines):
    """yield (lineno, text) of comment text (line comments + block comments)."""
    ext = os.path.splitext(path)[1].lower()
    inblock = False
    for n, l in enumerate(lines, 1):
        if ext in ('.cpp', '.h', '.hpp', '.cs'):
            if inblock:
                e = l.find('*/')
                yield n, l if e < 0 else l[:e]
                if e >= 0: inblock = False
                continue
            i = l.find('//'); j = l.find('/*')
            if i >= 0 and (j < 0 or i < j):
                # skip "//" inside a string literal crudely: count quotes before it
                if l[:i].count('"') % 2 == 0:
                    yield n, l[i + 2:]
            elif j >= 0 and l[:j].count('"') % 2 == 0:
                e = l.find('*/', j + 2)
                yield n, l[j + 2:] if e < 0 else l[j + 2:e]
                if e < 0: inblock = True
        elif ext in ('.lua', '.ct'):
            i = l.find('--')
            if i >= 0 and l[:i].count("'") % 2 == 0 and l[:i].count('"') % 2 == 0:
                yield n, l[i + 2:]
        elif ext == '.axaml':
            if '<!--' in l or inblock:
                inblock = '-->' not in l
                yield n, l
        elif ext == '.py':
            i = l.find('#')
            if i >= 0 and l[:i].count("'") % 2 == 0 and l[:i].count('"') % 2 == 0:
                yield n, l[i + 1:]


# ---------- code-token index (identifiers outside comments) for A2 ----------
CODE_EXT = ('.cpp', '.h', '.hpp', '.cs', '.lua', '.py')
ident_in_code = collections.Counter()
for f in files:
    if not f.endswith(CODE_EXT) or not f.startswith(('dll/', 'ui/', 'scripts/', 'tools/')):
        continue
    lines = read(f)
    cl = {n for n, _ in comments(f, lines)}
    for n, l in enumerate(lines, 1):
        code = l
        if n in cl:
            i = l.find('//') if '//' in l else l.find('--') if f.endswith('.lua') else l.find('#') if f.endswith('.py') else -1
            code = l[:i] if i >= 0 else ''
        for t in re.findall(r'[A-Za-z_][A-Za-z0-9_]{2,}', code):
            ident_in_code[t] += 1

# ---------- doc index for A3 / A4 ----------
doc_text = ''
for f in files:
    if f.endswith('.md'):
        try: doc_text += open(f, encoding='utf-8', errors='replace').read() + '\n'
        except OSError: pass
wl = open('docs/working-lessons.md', encoding='utf-8').read()
wl_sections = set(re.findall(r'^#{2,4} (\d+(?:\.[0-9a-z]+)*)\b', wl, re.M))
md_files = {os.path.basename(f).lower(): f for f in files if f.endswith('.md')}

FILELINE = re.compile(r'\b([A-Za-z_][\w.-]*\.(?:cpp|h|hpp|cs|axaml|lua|py|pas|md|java|CT|ps1|ini))[ ]?[:@](\d{1,5})(?:\s*[-–]\s*(\d{1,5}))?\b')
CPPSYM = re.compile(r'\b([A-Z][A-Za-z0-9_]+)::([A-Za-z_][A-Za-z0-9_]{2,})\b')
CSMEMBER = re.compile(r'\b([A-Z][A-Za-z0-9_]+)\.([A-Z][A-Za-z0-9_]{2,})\(')
TAG = re.compile(r'\[([A-Z][A-Z0-9]*(?:-[A-Z0-9]+){1,})\]')
WLREF = re.compile(r'working-lessons(?:\.md)?\s*§\s*(\d+(?:\.[0-9a-z]+)*)')
MDREF = re.compile(r'\b([\w-]+\.md)\b')

res = collections.defaultdict(list)
n_comment_lines = 0
for f in targets:
    lines = read(f)
    for n, c in comments(f, lines):
        n_comment_lines += 1
        for m in FILELINE.finditer(c):
            base, ln = m.group(1), int(m.group(2))
            cands = by_base.get(base.lower(), [])
            if not cands:
                res['A1_external_or_missing'].append((f, n, m.group(0)))
            elif len(cands) > 1:
                res['A1_ambiguous'].append((f, n, m.group(0)))
            else:
                tgt = cands[0]
                L = len(read(tgt))
                if ln > L:
                    res['A1_out_of_range'].append((f, n, m.group(0), tgt, L))
                else:
                    res['A1_in_range'].append((f, n, m.group(0), tgt, ln))
        for m in CPPSYM.finditer(c):
            ns, name = m.groups()
            if ns in ('std', 'UE', 'FName', 'EObjectFlags') and name[0].islower():
                continue
            if ident_in_code[name] == 0:
                res['A2_dead_symbol'].append((f, n, m.group(0)))
            else:
                res['A2_symbol_ok'].append((f, n, m.group(0)))
        if f.endswith('.cs'):
            for m in CSMEMBER.finditer(c):
                if ident_in_code[m.group(2)] == 0:
                    res['A2_dead_symbol'].append((f, n, m.group(0)))
        for m in TAG.finditer(c):
            t = m.group(1)
            if not re.search(r'[A-Z]{2}', t):
                continue
            if ('[' + t) not in doc_text and t not in doc_text:
                res['A3_tag_unrecorded'].append((f, n, t))
            else:
                res['A3_tag_ok'].append((f, n, t))
        for m in WLREF.finditer(c):
            sec = m.group(1)
            if sec not in wl_sections:
                res['A4_wl_section_missing'].append((f, n, m.group(0)))
        for m in MDREF.finditer(c):
            b = m.group(1).lower()
            if b not in md_files:
                res['A4_md_missing'].append((f, n, m.group(1)))
            elif md_files[b].startswith('docs/archive/'):
                res['A4_md_archived'].append((f, n, m.group(1)))

# ---------- A1 drift via blame ----------
if BLAME:
    drift = collections.Counter()
    blame_cache, show_cache = {}, {}

    def blame_commit(path, line):
        key = path
        if key not in blame_cache:
            out = subprocess.run(['git', 'blame', '--line-porcelain', path], capture_output=True, text=True,
                                 encoding='utf-8', errors='replace').stdout
            commits, cur = [], None
            for l in out.split('\n'):
                if re.match(r'^[0-9a-f]{40} ', l):
                    cur = l.split()[0]
                elif l.startswith('\t'):
                    commits.append(cur)
            blame_cache[key] = commits
        cs = blame_cache[key]
        return cs[line - 1] if line - 1 < len(cs) else None

    def show(commit, path):
        k = (commit, path)
        if k not in show_cache:
            r = subprocess.run(['git', 'show', f'{commit}:{path}'], capture_output=True)
            show_cache[k] = r.stdout.decode('utf-8-sig', errors='replace').replace('\r\n', '\n').split('\n') if r.returncode == 0 else None
        return show_cache[k]

    for (f, n, ref, tgt, ln) in res['A1_in_range']:
        c = blame_commit(f, n)
        if not c or c.startswith('0000000'):
            drift['uncommitted'] += 1; continue
        then = show(c, tgt)
        if then is None or ln > len(then):
            drift['target_absent_at_write_time'] += 1
            res['A1_drift_unknown'].append((f, n, ref)); continue
        now = read(tgt)
        if now[ln - 1].strip() == then[ln - 1].strip():
            drift['same_line_still_there'] += 1
        else:
            drift['line_moved_or_changed'] += 1
            res['A1_drifted'].append((f, n, ref, then[ln - 1].strip()[:80], now[ln - 1].strip()[:80]))
    res['A1_drift_summary'] = [dict(drift)]

os.makedirs('out/comment_audit', exist_ok=True)
json.dump({k: v for k, v in res.items()}, open('out/comment_audit/report.json', 'w', encoding='utf-8'), indent=1, ensure_ascii=False)
print('roots', roots, 'files', len(targets), 'comment lines', n_comment_lines)
for k in sorted(res):
    v = res[k]
    print(f'  {k:28} {len(v) if k != "A1_drift_summary" else v[0]}')
