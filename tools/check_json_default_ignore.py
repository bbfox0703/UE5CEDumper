"""P2 detector -- a value a user can legitimately choose must survive a save and a reload.

    py tools/check_json_default_ignore.py [--list] [--selftest]

STATUS: REGISTERED in check_all.py (fix pass, 2026-09-10), in the same commit that repaired the two
instances it was written against: [W1-QUOTA-UNLIMITED] (ExperimentalSettings' context dropped
WhenWritingDefault) and the benign AobUsageFile.Version (now [JsonIgnore(Condition = Never)]). It was
built during the finding phase and deliberately left unregistered, red by design, until then. The
repair's red-before-green test is ExperimentalGateTests.SnapshotQuotaMb_Unlimited_Zero_SurvivesARoundTrip.

WHAT IT REFUSES. A VALUE-TYPE property whose initializer differs from `default(T)`, serialized under
`JsonIgnoreCondition.WhenWritingDefault` -- set on a source-generated context's
`[JsonSourceGenerationOptions]`, or on the property itself.

`WhenWritingDefault` compares against `default(T)`, NOT against the initializer. So when the value
is set back to `default(T)` the key is OMITTED from the file, and the next load re-runs the
initializer. The value the user chose silently reverts.

That is `[W1-QUOTA-UNLIMITED]`, and it was MEASURED, not argued: `SnapshotQuotaMb = 1024` under a
`WhenWritingDefault` context; "Unlimited" is 0; the file came out as `{ "enabled": true }`, the reload
came back as 1024, and `SnapshotStore` then FIFO-deleted the snapshots the user had opted to keep.
It had a second entrance needing no user action at all -- `ApplyAutoQuota` sets 0 itself once the
retained set outgrows the 5 GB preset. The rule was already written down
(`docs/teleport-coord-library-spec.md:618`: DefaultIgnoreCondition "MUST NOT be WhenWritingDefault")
and five files named the trap; nothing enforced it.

⭐ THE PREDICATE'S LEGITIMATE POPULATION IS EMPTY BY CONSTRUCTION -- the only kind worth gating in this
repo (proposed gate #17 was refuted, and 17b re-scoped, for failing exactly that). A property that
genuinely never takes `default(T)` -- a schema `Version` -- says so with
`[JsonIgnore(Condition = JsonIgnoreCondition.Never)]`, which costs nothing and states the intent.
There is no baseline and no allowlist.

SCOPE. Governed types are every type registered with `[JsonSerializable(typeof(X))]` on a
`WhenWritingDefault` context, plus every repo class reachable through their property types, plus any
property carrying `[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]` directly.
Value types are the C# primitives, their `T?` forms, and every enum declared under ui/ -- with enum
initializers RESOLVED to their numeric value, because `Mode.First` is `default` when First == 0.
⚠ Not covered: user-defined `struct` properties. None is persisted under such a context today; the
limit is stated rather than silent.
"""
from __future__ import annotations

import argparse
import pathlib
import re
import sys

REPO = pathlib.Path(__file__).resolve().parents[1]
UI = REPO / 'ui' / 'UE5DumpUI'

PRIMITIVES = {'bool', 'byte', 'sbyte', 'short', 'ushort', 'int', 'uint', 'long', 'ulong', 'float',
              'double', 'decimal', 'char', 'nint', 'nuint'}
DEFAULT_LIT = re.compile(r'^(?:0+(?:\.0*)?[fFdDmMuUlL]*|0x0+|false|default(?:\([^)]*\))?|null)$')
NEVER = re.compile(r'JsonIgnore\s*\(\s*Condition\s*=\s*JsonIgnoreCondition\.Never\s*\)')
WWD_PROP = re.compile(r'JsonIgnore\s*\(\s*Condition\s*=\s*JsonIgnoreCondition\.WhenWritingDefault')
# UNANCHORED, because it runs under finditer over a whole class body. ⚠ The first attempt at the
# multi-declaration fix kept this pattern's `^...$` anchors from the old per-line design, and with
# no re.M they can only ever match a ONE-LINE class body: the selftest failed three cases and the
# TREE RUN WENT GREEN with the known positive missing. Read on its own, that exit 0 would have
# recorded P2 as clean. The initializer stops at the first `;` -- a value-type initializer never
# contains one.
PROP = re.compile(r'public\s+(?:required\s+)?([\w<>\[\],.?\s]+?)\s+(\w+)\s*\{\s*get;[^}]*\}'
                  r'(?:\s*=\s*([^;{}]+?)\s*;)?')


def strip_comments(src: str) -> str:
    """Blank // and /* */ comments (keeping newlines) so a commented-out attribute never counts."""
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
            out.append(src[i:j + 1])
            i = j + 1
        else:
            out.append(src[i])
            i += 1
    return ''.join(out)


def brace_body(src: str, open_pos: int) -> str:
    depth = 0
    for i in range(open_pos, len(src)):
        if src[i] == '{':
            depth += 1
        elif src[i] == '}':
            depth -= 1
            if depth == 0:
                return src[open_pos + 1:i]
    return ''


def parse(sources: dict) -> tuple:
    """-> (classes {name: [(file, line, type, prop, init, attrs)]}, enums {name: {member: value}},
           contexts [(file, [registered type strings])] for WhenWritingDefault contexts)"""
    classes, enums, contexts = {}, {}, []
    for path, raw in sources.items():
        src = strip_comments(raw)
        for m in re.finditer(r'\benum\s+(\w+)\s*(?::\s*\w+\s*)?\{', src):
            body = brace_body(src, m.end() - 1)
            vals, nxt = {}, 0
            for part in [p.strip() for p in body.split(',') if p.strip()]:
                part = re.sub(r'\[[^\]]*\]', '', part).strip()
                mm = re.match(r'(\w+)\s*(?:=\s*(.+))?$', part)
                if not mm:
                    continue
                if mm.group(2) is not None:
                    lit = mm.group(2).strip()
                    try:
                        nxt = int(lit, 0)
                    except ValueError:
                        nxt = None                   # non-literal: unknown, treated as non-default
                vals[mm.group(1)] = nxt
                nxt = None if nxt is None else nxt + 1
            enums[m.group(1)] = vals

        for m in re.finditer(r'\b(?:class|record)\s+(\w+)[^{;]*\{', src):
            name = m.group(1)
            body_start = m.end() - 1
            body = brace_body(src, body_start)
            base_line = src.count('\n', 0, body_start) + 1
            # ⚠ Declarations are matched ANYWHERE in the body, not one per line. The first draft
            # anchored a per-line regex with `$`, and its own selftest caught the result: two
            # properties on one line merged into ONE property whose "initializer" ran to the last
            # `;` -- it flagged the default-valued one and never saw the other. Attributes are taken
            # only from the span AFTER the previous `;` or `}`, so a method's attributes can never
            # leak onto the next property.
            props, prev = [], 0
            for pm in PROP.finditer(body):
                span = body[prev:pm.start()]
                cut = max(span.rfind(';'), span.rfind('}'))
                attrs = ' '.join(re.findall(r'\[[^\]]*\]', span[cut + 1:]))
                props.append((path, base_line + body.count('\n', 0, pm.start()),
                              pm.group(1).strip(), pm.group(2), (pm.group(3) or '').strip(),
                              attrs))
                prev = pm.end()
            classes.setdefault(name, []).extend(props)

            if re.search(r':\s*JsonSerializerContext\b', src[m.start():body_start]):
                head_start = max(src.rfind('}', 0, m.start()), src.rfind(';', 0, m.start()), 0)
                head = src[head_start:m.start()]
                opts = re.search(r'JsonSourceGenerationOptions\s*\((.*?)\)\s*\]', head, re.S)
                if opts and 'WhenWritingDefault' in opts.group(1):
                    contexts.append((path, re.findall(r'JsonSerializable\s*\(\s*typeof\s*\(\s*'
                                                     r'([^)]+?)\s*\)\s*\)', head)))
    return classes, enums, contexts


def is_value_type(t: str, enums: dict) -> bool:
    base = t.rstrip('?').strip()
    return base in PRIMITIVES or base in enums


def is_default_init(t: str, init: str, enums: dict) -> bool:
    if not init:
        return True                                  # no initializer: default(T) IS the initializer
    if DEFAULT_LIT.match(init.strip()):
        return True
    base = t.rstrip('?').strip()
    if base in enums:
        mm = re.match(r'^(?:%s\.)?(\w+)$' % re.escape(base), init.strip())
        if mm and enums[base].get(mm.group(1)) == 0:
            return True
    return False


def violations(sources: dict) -> list:
    classes, enums, contexts = parse(sources)
    governed = set()
    frontier = []
    for _, types in contexts:
        for t in types:
            frontier += [x for x in re.findall(r'\w+', t) if x in classes]
    while frontier:
        c = frontier.pop()
        if c in governed:
            continue
        governed.add(c)
        for _, _, t, _, _, _ in classes.get(c, []):
            frontier += [x for x in re.findall(r'\w+', t) if x in classes and x not in governed]

    out = []
    for c, props in classes.items():
        for path, line, t, name, init, attrs in props:
            under = c in governed or WWD_PROP.search(attrs)
            if not under or NEVER.search(attrs):
                continue
            if is_value_type(t, enums) and not is_default_init(t, init, enums):
                out.append((path, line, c, name, t, init))
    return sorted(set(out))


def load_tree() -> dict:
    srcs = {}
    for p in UI.rglob('*.cs'):
        s = str(p).replace('\\', '/')
        if '/obj/' in s or '/bin/' in s or '.Tests' in s:
            continue
        srcs[str(p.relative_to(REPO)).replace('\\', '/')] = p.read_text(encoding='utf-8',
                                                                          errors='replace')
    return srcs


# The pre-fix ExperimentalSettings shape, VERBATIM in the parts that matter -- the selftest must not
# depend on the real file, which the fix changes.
PREFIX_SHAPE = '''
public sealed class ExperimentalSettings
{
    public bool Enabled { get; set; }
    public int SnapshotQuotaMb { get; set; } = 1024;
}
[JsonSerializable(typeof(ExperimentalSettings))]
[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault)]
internal partial class ExperimentalSettingsJsonContext : JsonSerializerContext
{
}
'''

SELFTEST = [
    ('the pre-fix quota shape is REFUSED', {'a.cs': PREFIX_SHAPE}, {'SnapshotQuotaMb'}),
    ('the same shape with no DefaultIgnoreCondition is clean',
     {'a.cs': PREFIX_SHAPE.replace(',\n    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault', '')},
     set()),
    ('[JsonIgnore(Condition = Never)] exempts a genuinely-never-default value',
     {'a.cs': PREFIX_SHAPE.replace('    public int SnapshotQuotaMb',
                                   '    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]\n'
                                   '    public int SnapshotQuotaMb')}, set()),
    ('a reachable nested class is governed too',
     {'a.cs': '''
public sealed class Root { public Inner Child { get; set; } = new(); }
public sealed class Inner { public double Scale { get; set; } = 1.5; }
[JsonSerializable(typeof(Root))]
[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault)]
internal partial class Ctx : JsonSerializerContext { }
'''}, {'Scale'}),
    ('an enum initializer is RESOLVED: member 0 is default, member 2 is not',
     {'a.cs': '''
public enum Mode { First, Second, Third }
public sealed class S { public Mode A { get; set; } = Mode.First; public Mode B { get; set; } = Mode.Third; }
[JsonSerializable(typeof(S))]
[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault)]
internal partial class Ctx : JsonSerializerContext { }
'''}, {'B'}),
    ('per-property WhenWritingDefault with a non-default initializer is refused; without one, clean',
     {'a.cs': '''
public sealed class Msg
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool Quiet { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool Loud { get; set; } = true;
}
'''}, {'Loud'}),
    ('a reference type is not the hazard (an empty list is still written)',
     {'a.cs': '''
public sealed class L { public List<string> Items { get; set; } = new(); public string Name { get; set; } = ""; }
[JsonSerializable(typeof(L))]
[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault)]
internal partial class Ctx : JsonSerializerContext { }
'''}, set()),
]


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument('--list', action='store_true')
    ap.add_argument('--selftest', action='store_true')
    a = ap.parse_args()

    if a.selftest:
        ok_all = True
        for what, srcs, want in SELFTEST:
            got = {v[3] for v in violations(srcs)}
            ok = got == want
            ok_all &= ok
            print('  %-4s %s   (want %s, got %s)' % ('PASS' if ok else 'FAIL', what,
                                                    sorted(want), sorted(got)))
        print('selftest: %s' % ('PASS' if ok_all else 'FAIL'))
        return 0 if ok_all else 2

    v = violations(load_tree())
    if a.list or v:
        for path, line, c, name, t, init in v:
            print('  %s:%d  %s.%s  (%s = %s)' % (path, line, c, name, t, init))
    if v:
        print('check_json_default_ignore: %d value-type propert%s would silently revert on reload'
              % (len(v), 'y' if len(v) == 1 else 'ies'))
        return 1
    print('check_json_default_ignore: OK -- no chosen value can be dropped by WhenWritingDefault')
    return 0


if __name__ == '__main__':
    sys.stdout.reconfigure(encoding='utf-8')
    sys.exit(main())
