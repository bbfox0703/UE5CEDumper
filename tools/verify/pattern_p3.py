r"""P3 -- "a fix that reached some of its twins and not the rest".

    py tools/verify/pattern_p3.py outparams   # optional OUT-params whose callers disagree
    py tools/verify/pattern_p3.py typemaps    # UE type-name maps missing a type their siblings handle
    py tools/verify/pattern_p3.py consumers   # LiveFieldValue members some exporters read, others don't
    py tools/verify/pattern_p3.py validity    # validity/confidence state, per DLL transport
    py tools/verify/pattern_p3.py tags        # fix tags that reach part of a twin group
    py tools/verify/pattern_p3.py control     # every axis must re-find its known positives, or FAIL

⭐ WHY "TWINS" AND NOT "TRANSPORTS". The plan first defined P3 as "a fix landed on one of N
transports" -- the DLL's three exits (the pipe `Fern`, the CE mailbox `Mimic`, the C ABI
`Frieren`). The sweep then found the same defect in three other places: a delegate-array fix on the
FProperty arm and not its UProperty twin; the UE5.5 string types on CE XML's scalar path and not its
container path; `DelegatePad` in the CE XML exporter and not the CSX one. The common shape is a fix
that knew about some of its twins. Transports are one kind of twin.

⛔⛔ EVERY AXIS IS A POPULATION PRODUCER, NOT A FINDING LIST. Twins can legitimately differ -- a
pipe-only feature, an exporter that does not need a CE-specific field, a type map that deliberately
refuses a type. P3 is therefore a SWEEP, never a CI gate: this repo's gate rule is a predicate whose
legitimate population is EMPTY, and P3's is not.

⛔ WHY `validity` IS A TABLE AND NOT A VERDICT. The obvious matcher -- "is this validity flag read by
all three transports?" -- FAILS ITS OWN KNOWN POSITIVE: `Frieren.cpp:610` reads `bOffsetsValidated`,
so a read-count check calls `[W5-OFFSETS-UNMEASURED]` covered, when that read only feeds a log line
and nothing reaches a C ABI caller. Reading is not exposing, and exposure is not mechanically
decidable here. So this axis prints every reference with its line and a reader decides.

⛔ THE CONTROL RUNS OVER THE SAME COLLECTORS THE AXES PRINT. P1's first draft had a control that
passed over the unfiltered population while the view hid both known positives. Here every axis is a
`collect_*` function, and `control` calls exactly those.

⚠⚠ LIMITS THE FIRST ADJUDICATION MEASURED (2026-09-10, 112 rows, `[PATTERN-P3-2026-09-10]`).
Recorded so the next reader corrects for them instead of rediscovering them:
  * typemaps -- the C# method extractor used a bare brace counter, and `SdkExportService` EMITS C++
    SOURCE full of "{" / "};" literals, so one body swallowed a later method. Fixed (`cs_skip`).
    Measured afterwards: 29 rows -> 28; the one removed row was fabricated; NONE added and none
    changed, so no map had been hidden and the adjudicated bound stands.
  * validity -- matches RAW identifiers, so a flag the pipe publishes through a renamed cache global
    (`bLowConfidence` -> `g_cachedIsLowConfidence` -> `is_low_confidence`) is shown as "Fern never
    references it", and the cache STORE in Frieren is shown as the exposure: the inverse of reality.
  * consumers -- over-reports four ways: reads through a helper (`ContainerGeometry`, the shared
    `ResolveDrilldownAsync`) are invisible; `lfv_members()` parses EVERY class in LiveFieldValue.cs,
    so sub-type members appear; one member (`InnerType`) is not on LiveFieldValue at all; and
    pass-through COPIES in CE XML's reconstruction count as reads. 43 of 57 rows were gaps against
    `SdkExportService`, which exports class LAYOUTS and is not a live-value twin.
  * tags -- over-counts twice: a fix placed in a shared CALLEE shows as "tag absent" in every caller
    that inherits it, and pipe-only commands have no mailbox or C ABI counterpart at all.
"""
from __future__ import annotations

import argparse
import collections
import os
import re
import sys

REPO = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..'))
DLL = os.path.join(REPO, 'dll', 'src')
UI = os.path.join(REPO, 'ui', 'UE5DumpUI')


def read(path):
    with open(path, encoding='utf-8', errors='replace') as fh:
        return fh.read()


def rel(path):
    return os.path.relpath(path, REPO).replace('\\', '/')


def files_under(root, exts, skip=('.Tests', '/obj/', '/bin/')):
    out = []
    for dp, _, fns in os.walk(root):
        d = dp.replace('\\', '/')
        if any(s in d for s in skip):
            continue
        for f in fns:
            if f.endswith(exts):
                out.append(os.path.join(dp, f))
    return sorted(out)


def line_of(text, pos):
    return text.count('\n', 0, pos) + 1


def strip_cpp_comments(text):
    """Blank out // and /* */ comments, keeping every newline so positions still map to lines."""
    out, i, n = [], 0, len(text)
    while i < n:
        if text.startswith('//', i):
            j = text.find('\n', i)
            j = n if j < 0 else j
            out.append(' ' * (j - i))
            i = j
        elif text.startswith('/*', i):
            j = text.find('*/', i + 2)
            j = n if j < 0 else j + 2
            out.append(re.sub(r'[^\n]', ' ', text[i:j]))
            i = j
        elif text[i] == '"':
            j = i + 1
            while j < n and text[j] != '"':
                j += 2 if text[j] == '\\' else 1
            out.append(text[i:j + 1])
            i = j + 1
        else:
            out.append(text[i])
            i += 1
    return ''.join(out)


def match_close(text, open_pos, o='(', c=')'):
    depth = 0
    for i in range(open_pos, len(text)):
        ch = text[i]
        if ch == o:
            depth += 1
        elif ch == c:
            depth -= 1
            if depth == 0:
                return i
    return -1


def split_top(argtext):
    parts, depth, cur = [], 0, []
    for ch in argtext:
        if ch in '([{':
            depth += 1
        elif ch in ')]}':
            depth -= 1
        if ch == ',' and depth == 0:
            parts.append(''.join(cur))
            cur = []
        else:
            cur.append(ch)
    if ''.join(cur).strip():
        parts.append(''.join(cur))
    return [p.strip() for p in parts]


# ---------------------------------------------------------------------------------------------
# Axis 1 -- OUT-params with a nullptr default whose callers disagree
# ---------------------------------------------------------------------------------------------

DEFAULT_NULL = re.compile(r'=\s*nullptr\s*(?=[,)])')
DEFN_PREFIX = re.compile(r'^\s*(?:(?:static|inline|virtual|extern|constexpr|friend)\s+)*'
                         r'[\w:<>,\s\*&]*[\w\*&>]\s*$')


def collect_outparams():
    """-> {func: {'params': {(idx, name)}, 'decls': [...], 'calls': [(file, line, verdicts)]}}"""
    srcs = files_under(DLL, ('.h', '.hpp', '.cpp'))
    clean = {p: strip_cpp_comments(read(p)) for p in srcs}

    funcs = collections.defaultdict(lambda: {'params': set(), 'decls': [], 'calls': []})
    for p, text in clean.items():
        for m in DEFAULT_NULL.finditer(text):
            depth, i = 0, m.start()
            while i >= 0:
                if text[i] == ')':
                    depth += 1
                elif text[i] == '(':
                    if depth == 0:
                        break
                    depth -= 1
                i -= 1
            if i < 0:
                continue
            name_m = re.search(r'(\w+)\s*$', text[:i])
            if not name_m:
                continue
            fname = name_m.group(1)
            params = split_top(text[i + 1:m.start()] + '=0')
            param_txt = params[-1]
            if re.search(r'\bconst\b', param_txt) or '*' not in param_txt:
                continue                                    # an INPUT filter, not an out-param
            pname = re.search(r'(\w+)\s*=0$', param_txt)
            if not pname:
                continue
            funcs[fname]['params'].add((len(params) - 1, pname.group(1)))
            funcs[fname]['decls'].append((rel(p), line_of(text, m.start())))

    for fname, info in funcs.items():
        call_re = re.compile(r'(?<![\w.>])(?:\w+::)*\b%s\s*\(' % re.escape(fname))
        for p, text in clean.items():
            for m in call_re.finditer(text):
                ls = text.rfind('\n', 0, m.start()) + 1
                prefix = text[ls:m.start()]
                if DEFN_PREFIX.match(prefix) and prefix.strip() and \
                   not re.search(r'\b(return|if|while|else|case|throw)\b|[=(,!?]', prefix):
                    continue                                # a declaration or definition
                op = text.index('(', m.start() + len(fname) - 1)
                cl = match_close(text, op)
                if cl < 0:
                    continue
                args = split_top(text[op + 1:cl])
                verdicts = []
                for idx, pname in sorted(info['params']):
                    if len(args) <= idx:
                        verdicts.append((pname, 'omitted'))
                    elif args[idx] in ('nullptr', 'NULL', '0'):
                        verdicts.append((pname, 'nullptr'))
                    else:
                        verdicts.append((pname, 'passes'))
                info['calls'].append((rel(p), line_of(text, m.start()), verdicts))
    return funcs


def outparam_mixed(funcs):
    """(func, param) pairs where at least one caller passes it and at least one does not."""
    out = []
    for fname, info in sorted(funcs.items()):
        for idx, pname in sorted(info['params']):
            v = [(f, ln, dict(vs)[pname]) for f, ln, vs in info['calls'] if pname in dict(vs)]
            passes = [x for x in v if x[2] == 'passes']
            drops = [x for x in v if x[2] != 'passes']
            if passes and drops:
                out.append((fname, pname, passes, drops))
    return out


def cmd_outparams() -> int:
    funcs = collect_outparams()
    mixed = outparam_mixed(funcs)
    nparams = sum(len(i['params']) for i in funcs.values())
    print('\nP3a -- optional OUT-params (non-const pointer, default nullptr): %d on %d functions'
          % (nparams, len(funcs)))
    print('   whose callers DISAGREE (some pass it, some drop it): %d\n' % len(mixed))
    for fname, pname, passes, drops in mixed:
        print('  %s(.., %s)' % (fname, pname))
        for f, ln, v in passes:
            print('      passes   %s:%d' % (f, ln))
        for f, ln, v in drops:
            print('      %-8s %s:%d' % (v, f, ln))
    unreq = [(fn, pn) for fn, i in funcs.items() for _, pn in i['params']
             if not any(dict(vs).get(pn) == 'passes' for _, _, vs in i['calls'])]
    if unreq:
        print('\n  (never requested by ANY caller -- a structure, not a P3 row: %s)'
              % ', '.join('%s.%s' % x for x in sorted(unreq)))
    return 0


# ---------------------------------------------------------------------------------------------
# Axis 2 -- UE type-name maps missing a type their siblings handle
# ---------------------------------------------------------------------------------------------

TYPE_LIT = re.compile(r'"([A-Za-z0-9]+Property)"')
CS_SIG = re.compile(
    r'^[ \t]*(?:(?:public|private|internal|protected|static|override|virtual|async|sealed|new'
    r'|extern|unsafe|partial|readonly)\s+)+(?:[\w<>\[\],.?]+\s+)+?(\w+)\s*(?:<[^>()]*>)?\s*\(',
    re.M)

FAMILIES = {
    'NUMERIC':  ['Int8Property', 'ByteProperty', 'Int16Property', 'UInt16Property', 'IntProperty',
                 'UInt32Property', 'Int64Property', 'UInt64Property', 'FloatProperty',
                 'DoubleProperty', 'EnumProperty'],
    'TEXT':     ['NameProperty', 'StrProperty', 'Utf8StrProperty', 'AnsiStrProperty',
                 'TextProperty'],
    'DELEGATE': ['DelegateProperty', 'MulticastDelegateProperty',
                 'MulticastInlineDelegateProperty', 'MulticastSparseDelegateProperty'],
    'OBJECT':   ['ObjectProperty', 'WeakObjectProperty', 'LazyObjectProperty',
                 'SoftObjectProperty', 'ClassProperty', 'SoftClassProperty', 'InterfaceProperty'],
}
# The UE5.5 string types were added as a SET; a function holding part of the set is the exact
# shape of "a fix that reached some sites".
TRIO = ['StrProperty', 'Utf8StrProperty', 'AnsiStrProperty']
SIBLING_SKIP = {'ArrayProperty', 'MapProperty', 'SetProperty', 'OptionalProperty',
                'StructProperty'}   # an INNER-type map legitimately lacks the containers
BROAD = 8


def cs_skip(text, i):
    """If a C# comment or literal starts at i, return the index just past it; else i.

    ⛔ WHY THIS EXISTS. The first draft matched braces with a bare counter, and a P3 adjudicator
    caught the symptom: `EmitClassHeaderFromLive` -- which contains no type switch -- inherited
    `MapFunctionParamType`'s exact lacks-list. `SdkExportService` EMITS C++ SOURCE, so its method
    bodies are full of "{" and "};" string literals; the counter walked straight past the method's
    real end and swallowed a later one. That case only produced a duplicate row. The mirror case is
    the dangerous one: an unbalanced "}" literal ENDS a body early, drops a type map below the
    BROAD threshold and HIDES it -- a recall hole in the bound P3 exists to establish.
    """
    n = len(text)
    if text.startswith('//', i):
        j = text.find('\n', i)
        return n if j < 0 else j
    if text.startswith('/*', i):
        j = text.find('*/', i + 2)
        return n if j < 0 else j + 2
    if text.startswith('"""', i):                                   # C# 11 raw string
        j = text.find('"""', i + 3)
        return n if j < 0 else j + 3
    m = re.match(r'(\$@|@\$|\$|@)?"', text[i:i + 3])
    if m and (m.group(1) or text[i] == '"'):
        pre = m.group(1) or ''
        verbatim, interp = '@' in pre, '$' in pre
        k = i + len(pre) + 1
        depth = 0
        while k < n:
            ch = text[k]
            if depth > 0:                                            # inside an interpolation hole
                nk = cs_skip(text, k)
                if nk != k:
                    k = nk
                    continue
                if ch == '{':
                    depth += 1
                elif ch == '}':
                    depth -= 1
                k += 1
                continue
            if interp and ch == '{':
                if text.startswith('{{', k):
                    k += 2
                    continue
                depth = 1
                k += 1
                continue
            if not verbatim and ch == '\\':
                k += 2
                continue
            if ch == '"':
                if verbatim and text.startswith('""', k):
                    k += 2
                    continue
                return k + 1
            k += 1
        return n
    if text[i] == "'":                                               # char literal
        if text.startswith("'\\", i):
            j = text.find("'", i + 2)
            return n if j < 0 else j + 1
        if i + 2 < n and text[i + 2] == "'":
            return i + 3
    return i


def cs_match_close(text, open_pos, o, c):
    depth, k, n = 0, open_pos, len(text)
    while k < n:
        nk = cs_skip(text, k)
        if nk != k:
            k = nk
            continue
        ch = text[k]
        if ch == o:
            depth += 1
        elif ch == c:
            depth -= 1
            if depth == 0:
                return k
        k += 1
    return -1


def cs_methods(text):
    """-> [(name, start_line, body_text)] including expression-bodied members.
    Every brace, paren and ';' is found by a scanner that skips comments and literals."""
    out = []
    n = len(text)
    for m in CS_SIG.finditer(text):
        op = m.end() - 1
        cl = cs_match_close(text, op, '(', ')')
        if cl < 0:
            continue
        j = cl + 1
        while j < n and text[j] not in '{;=':
            nj = cs_skip(text, j)
            j = nj if nj != j else j + 1
        if j >= n or text[j] == ';':
            continue
        if text.startswith('=>', j):
            depth, k = 0, j
            while k < n:
                nk = cs_skip(text, k)
                if nk != k:
                    k = nk
                    continue
                ch = text[k]
                if ch in '({[':
                    depth += 1
                elif ch in ')}]':
                    depth -= 1
                elif ch == ';' and depth == 0:
                    break
                k += 1
            out.append((m.group(1), line_of(text, m.start()), text[j:k]))
        elif text[j] == '{':
            k = cs_match_close(text, j, '{', '}')
            if k > 0:
                out.append((m.group(1), line_of(text, m.start()), text[j:k]))
    return out


def collect_typemaps():
    """-> [(file, method, line, types:set, missing:{reason: [types]})] for BROAD type maps."""
    rows = []
    for p in files_under(UI, ('.cs',)):
        text = read(p)
        if '"' not in text or 'Property"' not in text:
            continue
        maps = []
        for name, ln, body in cs_methods(text):
            types = set(TYPE_LIT.findall(body))
            if len(types) >= BROAD:
                maps.append((name, ln, types))
        for name, ln, types in maps:
            missing = {}
            for fam, members in FAMILIES.items():
                have = [t for t in members if t in types]
                if len(have) * 2 >= len(members) and len(have) < len(members):
                    missing['family %s' % fam] = [t for t in members if t not in types]
            have_trio = [t for t in TRIO if t in types]
            if have_trio and len(have_trio) < len(TRIO):
                missing['UE5.5 string set'] = [t for t in TRIO if t not in types]
            sib = set()
            for n2, l2, t2 in maps:
                if (n2, l2) != (name, ln):
                    sib |= t2
            gap = sorted(t for t in sib - types if t not in SIBLING_SKIP)
            if gap:
                missing['same-file siblings'] = gap
            if missing:
                rows.append((rel(p), name, ln, types, missing))
    return rows


def cmd_typemaps() -> int:
    rows = collect_typemaps()
    print('\nP3b -- C# methods mapping >= %d UE type names, missing a type a sibling handles: %d\n'
          % (BROAD, len(rows)))
    print('⚠ C# only. The C++ side (Ubel, Radar, Orden) switches on type names too and is NOT')
    print('  covered by this axis -- a known limit, stated rather than silent.\n')
    for f, name, ln, types, missing in rows:
        print('  %s:%d  %s  (%d types)' % (f, ln, name, len(types)))
        for why, ts in missing.items():
            print('      %-20s lacks %s' % (why, ', '.join(ts)))
    return 0


# ---------------------------------------------------------------------------------------------
# Axis 3 -- LiveFieldValue members some exporters read and others do not
# ---------------------------------------------------------------------------------------------

EXPORTERS = ['CeXmlExportService.cs', 'CsxExportService.cs', 'SdkExportService.cs']


def lfv_members():
    text = read(os.path.join(UI, 'Models', 'LiveFieldValue.cs'))
    names = set(re.findall(r'^\s*public\s+(?!class\b|enum\b|struct\b|record\b|static\s+class\b)'
                           r'[\w<>\[\],.?\s]+?\s+(\w+)\s*\{\s*(?:get|init|set)', text, re.M))
    for m in re.finditer(r'\[ObservableProperty\][^\n]*\n(?:\s*\[[^\n]*\n)*\s*'
                         r'(?:private|protected|internal)?\s*[\w<>\[\],.?]+\s+_?(\w+)\s*[=;]',
                         text):
        n = m.group(1)
        names.add(n[0].upper() + n[1:])
    return names


def collect_consumers():
    texts = {e: read(os.path.join(UI, 'Services', e)) for e in EXPORTERS}
    rows = []
    for mem in sorted(lfv_members()):
        acc = re.compile(r'\??\.%s\b' % re.escape(mem))
        who = [e for e in EXPORTERS if acc.search(texts[e])]
        if who and len(who) < len(EXPORTERS):
            rows.append((mem, who, [e for e in EXPORTERS if e not in who]))
    return rows


def cmd_consumers() -> int:
    rows = collect_consumers()
    print('\nP3c -- LiveFieldValue members read by SOME exporters and not others: %d' % len(rows))
    print('   exporters compared: %s\n' % ', '.join(EXPORTERS))
    for mem, who, missing in rows:
        print('  %-28s read by %-40s NOT by %s' % (mem, ', '.join(who), ', '.join(missing)))
    return 0


# ---------------------------------------------------------------------------------------------
# Axis 4 -- validity / confidence state, per transport (a TABLE -- see the header)
# ---------------------------------------------------------------------------------------------

TRANSPORTS = ['Fern.cpp', 'Mimic.cpp', 'Frieren.cpp']
VALIDITY = re.compile(r'\b(?:[bgsk]_?|is|has)?[A-Za-z]*?(?:Validated|Fallback|Confidence|Tentative'
                      r'|Stale|Measured|Detected|Unverified|ProbeRan)[A-Za-z]*\b')


def collect_validity():
    idents = set()
    for p in files_under(DLL, ('.h', '.hpp')):
        idents |= set(VALIDITY.findall(strip_cpp_comments(read(p))))
    tlines = {t: strip_cpp_comments(read(os.path.join(DLL, t))).split('\n') for t in TRANSPORTS}
    rows = []
    for ident in sorted(idents):
        refs = {}
        for t, lines in tlines.items():
            hits = [(i + 1, l.strip()) for i, l in enumerate(lines)
                    if re.search(r'\b%s\b' % re.escape(ident), l)]
            if hits:
                refs[t] = hits
        if refs and len(refs) < len(TRANSPORTS):
            rows.append((ident, refs))
    return rows


def cmd_validity() -> int:
    rows = collect_validity()
    print('\nP3d -- validity/confidence identifiers referenced by SOME transports, not all: %d'
          % len(rows))
    print('⛔ A reference is not an exposure. Read each line: does it reach a CALLER, or only a log?\n')
    for ident, refs in rows:
        print('  %s' % ident)
        for t in TRANSPORTS:
            if t not in refs:
                print('      %-12s (never referenced)' % t)
                continue
            for ln, s in refs[t][:4]:
                print('      %-12s :%-5d %s' % (t, ln, s[:100]))
            if len(refs[t]) > 4:
                print('      %-12s ... +%d more' % (t, len(refs[t]) - 4))
    return 0


# ---------------------------------------------------------------------------------------------
# Axis 5 -- fix tags that reach part of a twin group
# ---------------------------------------------------------------------------------------------

TAG = re.compile(r'\[([A-Z][A-Z0-9]*(?:-[A-Z0-9]+){1,6})\]')
TWIN_GROUPS = {
    'transports': ['dll/src/Fern.cpp', 'dll/src/Mimic.cpp', 'dll/src/Frieren.cpp'],
    'exporters':  ['ui/UE5DumpUI/Services/CeXmlExportService.cs',
                   'ui/UE5DumpUI/Services/CsxExportService.cs',
                   'ui/UE5DumpUI/Services/SdkExportService.cs',
                   'ui/UE5DumpUI/Services/UsmapExportService.cs'],
}


def collect_tags():
    where = collections.defaultdict(set)
    roots = [(DLL, ('.h', '.hpp', '.cpp')), (UI, ('.cs', '.axaml')),
             (os.path.join(REPO, 'scripts'), ('.lua', '.CT'))]
    for root, exts in roots:
        for p in files_under(root, exts):
            for tag in set(TAG.findall(read(p))):
                where[tag].add(rel(p))
    rows = []
    for tag, fs in sorted(where.items()):
        for g, members in TWIN_GROUPS.items():
            hit = [m for m in members if m in fs]
            if hit and len(hit) < len(members):
                rows.append((tag, g, hit, [m for m in members if m not in fs], sorted(fs)))
    return rows, len(where)


def cmd_tags() -> int:
    rows, ntags = collect_tags()
    print('\nP3e -- fix tags in code (%d distinct) that reach PART of a twin group: %d\n'
          % (ntags, len(rows)))
    for tag, g, hit, miss, fs in rows:
        print('  [%s]  %s: in %s   NOT in %s'
              % (tag, g, ', '.join(os.path.basename(h) for h in hit),
                 ', '.join(os.path.basename(m) for m in miss)))
    return 0


# ---------------------------------------------------------------------------------------------
# Control -- over the SAME collectors
# ---------------------------------------------------------------------------------------------

def cmd_control() -> int:
    checks = []

    mixed = outparam_mixed(collect_outparams())
    have = {(f, p) for f, p, _, _ in mixed}
    checks.append(('outparams', 'TeleportRelative.outLandingKnown  [TPREL-ZEROPOSE]',
                   ('TeleportRelative', 'outLandingKnown') in have))
    checks.append(('outparams', 'GetPoseImpl.outParentRelative     [POSEATTACH]',
                   ('GetPoseImpl', 'outParentRelative') in have))

    tm = {(n, why): ts for _, n, _, _, miss in collect_typemaps() for why, ts in miss.items()}
    checks.append(('typemaps', 'MapInnerTypeToCeField lacks StrProperty  [W5-CEXML-FSTRING]',
                   any(n == 'MapInnerTypeToCeField' and 'StrProperty' in ts
                       for (n, _), ts in tm.items())))
    checks.append(('typemaps', 'WidthBytes lacks EnumProperty  [W2-GROUPMATCH-ENUM]',
                   any(n == 'WidthBytes' and 'EnumProperty' in ts for (n, _), ts in tm.items())))

    cons = {m: miss for m, _, miss in collect_consumers()}
    checks.append(('consumers', 'DelegatePad not read by CsxExportService  [W5-CSX-DELEGATEPAD]',
                   'CsxExportService.cs' in cons.get('DelegatePad', [])))

    val = {i for i, _ in collect_validity()}
    checks.append(('validity', 'bOffsetsValidated is in the table  [W5-OFFSETS-UNMEASURED]',
                   'bOffsetsValidated' in val))

    tags, _ = collect_tags()
    tset = {(t, g) for t, g, _, _, _ in tags}
    checks.append(('tags', '[TPREL-ZEROPOSE-2026-09-10] partial across transports',
                   ('TPREL-ZEROPOSE-2026-09-10', 'transports') in tset))

    print('\nCONTROL -- does every axis re-find its known positives, over what it prints?\n')
    bad = 0
    for axis, what, ok in checks:
        bad += 0 if ok else 1
        print('  %-4s %-10s %s' % ('PASS' if ok else '****', axis, what))
    print()
    if bad:
        print('*** %d known positive(s) not re-found -- those axes are BROKEN; do not trust them'
              % bad)
        return 2
    print('every axis re-finds its known positives')
    return 0


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = ap.add_subparsers(dest='action', required=True)
    for a in ('outparams', 'typemaps', 'consumers', 'validity', 'tags', 'control'):
        sub.add_parser(a)
    a = ap.parse_args()
    return {'outparams': cmd_outparams, 'typemaps': cmd_typemaps, 'consumers': cmd_consumers,
            'validity': cmd_validity, 'tags': cmd_tags, 'control': cmd_control}[a.action]()


if __name__ == '__main__':
    sys.stdout.reconfigure(encoding='utf-8')
    sys.exit(main())
