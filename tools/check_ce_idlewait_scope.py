"""Gate 17c — a CE mailbox emitter's idle wait must not be conditioned on `enable`.

    py tools/check_ce_idlewait_scope.py [--list] [--selftest]

WHAT IT REFUSES, AND WHY THE PREDICATE IS THIS NARROW.

Every `*ScriptGenerator` that talks to the mailbox shares ONE `EmitBlock` between
`[ENABLE]` and `[DISABLE]`, and the `cmd` store at the bottom is emitted for BOTH. So an
idle wait that only one of the two blocks receives is always wrong: the other block writes
operands, clears status and stores `cmd` while another command may still be in flight --
the AA10 hazard the emitters' own comments describe.

That makes the legitimate population EMPTY by construction, which is the only kind of
predicate worth gating here. The repo has paid for the alternative twice: proposed gate #17
was refuted in round 1, and 17b had to be re-scoped, both because their legitimate
population had to be ENUMERATED and the check would have decayed into an allowlist.

⛔ SO THIS DELIBERATELY DOES **NOT** CHECK "every idle wait is unconditional". That version
has a real legitimate population -- `CoordLibraryScriptGenerator` guards on `if (dll)` and
`BakedScriptGenerator` on `if (verifyReturn)`, and both are correct, because those blocks
genuinely do not talk to the mailbox at all. Flagging them would be the allowlist failure
all over again. The check is only ever about a guard on the ENABLE/DISABLE discriminator.

FOUND BY: `[R3-SEETHRU-2026-09-10]`. `SeeThroughScriptGenerator` was the only violator in
14 generators; its call sat between the `{` and the `else`, with the explanatory comment
OUTDENTED to the outer level, which is exactly what made it read as unconditional to every
reviewer -- including two audits. A brace-depth check cannot be fooled by indentation, so
that is what this does.
"""
from __future__ import annotations

import argparse
import io
import pathlib
import re
import subprocess
import sys

REPO = pathlib.Path(__file__).resolve().parents[1]

CALL = re.compile(r'CeLuaHygiene\.(AppendIdleWaitOrBail|AppendIdleWait)\b')
# The ENABLE/DISABLE discriminator, however the generator happens to spell it.
DISCRIM = re.compile(r'\b(enable|isEnable|enabled|on)\b')
OPENS_COND = re.compile(r'\b(if|else\s+if)\s*\(|^\s*else\b')


def strip_code(line: str) -> str:
    """Comments and string literals removed, so neither can move the brace depth."""
    out = re.sub(r'//.*$', '', line)
    return re.sub(r'"(?:[^"\\]|\\.)*"', '""', out)


def scan_text(text: str) -> list[tuple[int, str, str]]:
    """Return (line_no, call_name, guard_text) for every guarded idle-wait call."""
    src = text.split('\n')
    hits: list[tuple[int, str, str]] = []
    depth = 0
    stack: list[tuple[int, str]] = []
    for n, line in enumerate(src, 1):
        m = CALL.search(line)
        if m:
            for _, head in stack:
                if OPENS_COND.search(head) and DISCRIM.search(head):
                    hits.append((n, m.group(1), head.strip()))
                    break
        code = strip_code(line)
        for ch in code:
            if ch == '{':
                head = ''
                for k in range(n - 1, max(n - 5, 0), -1):
                    t = strip_code(src[k - 1]).strip()
                    if t and t != '{':
                        head = t
                        break
                stack.append((depth, head))
                depth += 1
            elif ch == '}':
                depth -= 1
                if stack:
                    stack.pop()
    return hits


def targets() -> list[pathlib.Path]:
    out = subprocess.run(['git', 'ls-files', 'ui/UE5DumpUI/Services/*ScriptGenerator*.cs'],
                         capture_output=True, text=True, cwd=str(REPO)).stdout
    return [REPO / f for f in out.split('\n') if f.strip()]


def run(show: bool = False) -> list[str]:
    bad: list[str] = []
    seen = 0
    for p in targets():
        text = io.open(p, encoding='utf-8', errors='replace').read()
        calls = len(CALL.findall(text))
        seen += calls
        for ln, name, head in scan_text(text):
            bad.append('%s:%d  %s guarded by: %s' % (p.name, ln, name, head))
        if show and calls:
            print('  %-42s %d idle-wait call(s)' % (p.name, calls))
    if show:
        print('  total idle-wait call sites: %d' % seen)
    return bad


# ── negative controls ────────────────────────────────────────────────────────
# Each case is (name, C# fragment, expect_flagged). The gate must go RED on the
# defect shape and stay green on every legitimate one -- one control per axis,
# because working-lessons 1.2a says one control validates one AXIS, not the tool.
SELFTEST = [
    ("the R3 defect: call inside if (enable)",
     'void F(bool enable){\n if (enable)\n {\n  Line(sb,"x");\n'
     ' CeLuaHygiene.AppendIdleWaitOrBail(sb,"mb","T");\n }\n else\n {\n  Line(sb,"y");\n }\n}',
     True),

    ("the fixed shape: unconditional, mode chosen by a ternary",
     'void F(bool enable){\n CeLuaHygiene.AppendIdleWaitOrBail(sb,"mb","T",\n'
     '   enable ? MailboxTimeout.UntickAndReturn : MailboxTimeout.SilentReturn);\n'
     ' if (enable)\n {\n  Line(sb,"x");\n }\n}',
     False),

    # ⛔ THE CONTROL THAT KEEPS THE PREDICATE NARROW. A guard that is not the
    # enable/disable discriminator is legitimate and must NOT be flagged, or this
    # gate becomes the allowlist that refuted #17.
    ("legitimate: guarded by if (dll)",
     'void F(bool dll){\n if (dll)\n {\n  CeLuaHygiene.AppendIdleWait(sb,"mb","z");\n }\n}',
     False),

    ("legitimate: guarded by if (verifyReturn)",
     'void F(bool verifyReturn){\n if (verifyReturn)\n {\n'
     '  CeLuaHygiene.AppendIdleWaitOrBail(sb,"mb","T");\n }\n}',
     False),

    # An OUTDENTED comment is what hid the real defect from two audits; the checker
    # must not be fooled by layout in either direction.
    ("the defect, with the comment outdented as it really was",
     'void F(bool enable){\n if (enable)\n {\n  Line(sb,"x");\n'
     '// bounded wait for IDLE before the FIRST write\n'
     'CeLuaHygiene.AppendIdleWaitOrBail(sb,"mb","T");\n  Line(sb,"w");\n }\n}',
     True),

    ("a brace inside a string literal must not shift the depth",
     'void F(bool enable){\n Line(sb,"if (enable) {");\n'
     ' CeLuaHygiene.AppendIdleWaitOrBail(sb,"mb","T");\n}',
     False),
]


def selftest() -> int:
    bad = 0
    for name, frag, expect in SELFTEST:
        got = bool(scan_text(frag))
        ok = got == expect
        if not ok:
            bad += 1
        print('  %-6s %-52s expected %s, got %s'
              % ('ok' if ok else 'FAIL', name, expect, got))
    print('selftest: %d case(s), %d failed' % (len(SELFTEST), bad))
    return 1 if bad else 0


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument('--list', action='store_true', help='every generator and its call count')
    ap.add_argument('--selftest', action='store_true', help='run the negative controls')
    a = ap.parse_args()

    if a.selftest:
        return selftest()

    bad = run(show=a.list)
    if bad:
        print('CHECK FAILED: %d idle wait(s) conditioned on the enable/disable '
              'discriminator:' % len(bad))
        for b in bad:
            print('   %s' % b)
        print('\nThe cmd store below it is emitted for BOTH blocks, so the block that '
              'misses the wait\nwrites the mailbox while another command may still be in '
              'flight. Emit the call\nunconditionally and pass '
              '`enable ? MailboxTimeout.UntickAndReturn : MailboxTimeout.SilentReturn`, '
              'as\nMovementScriptGenerator and TimeDilationScriptGenerator do.')
        return 1

    total = sum(len(CALL.findall(io.open(p, encoding='utf-8', errors='replace').read()))
                for p in targets())
    print('CHECK OK: %d mailbox idle-wait call site(s), none conditioned on enable.' % total)
    return 0


if __name__ == '__main__':
    sys.exit(main())
