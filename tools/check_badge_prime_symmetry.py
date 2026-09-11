"""Gate 17d — every badge SetConnected(false) resets must be primed on connect.

    py tools/check_badge_prime_symmetry.py [--list] [--selftest]

WHAT IT REFUSES. `TeleportViewModel.SetConnected(false)` walks each gameplay card's badge
back to Unknown/Unavailable with `Apply<X>State(-1)`. The DLL's holds SURVIVE a UI
reconnect for as long as the game lives, so the connect branch has to read each of them
back or the card lies by omission: a still-active Fly or Move Speed hold shows as "never
asked" and the user has no sign the game is still modified.

Before 2026-09-10 the connect branch primed THREE badges and the disconnect branch reset
twelve. Nine cards were therefore permanently unprimed, which is `[BADGEPRIME-2026-09-10]`
-- observed live on a Shipping fixture, with God Mode and Time Dilation (the two that WERE
primed) rendering correctly as the control.

⛔ WHY A GATE AND NOT A TYPE. The obvious fix is one table of cards, each carrying its own
reset and prime, so the two cannot diverge. That was considered and REJECTED for now:
`SetConnected` carries four findings' worth of subtle behaviour (B9's connect/disconnect
asymmetry, B17's pose clear, B26's pushed-symbol reset, L13's stealth card), and rewriting
it wholesale is a poor trade against a check that costs nothing. Phase 4 of the audit-#4
assessment says exactly this: prefer the structure, keep the gate only if the structure is
rejected. If the table is ever built, DELETE this gate rather than keeping both.

⚠ THE PREDICATE IS SYMMETRY, NOT A LIST. It reads both branches out of the source, so a
card added tomorrow is covered without editing this file. There is no baseline and no
allowlist -- the legitimate population of "reset but never primed" is empty by
construction, which is the only kind of predicate worth gating here (proposed gate #17 was
refuted, and 17b re-scoped, for failing exactly that test).
"""
from __future__ import annotations

import argparse
import io
import pathlib
import re
import sys

REPO = pathlib.Path(__file__).resolve().parents[1]
VM = REPO / 'ui/UE5DumpUI/ViewModels/TeleportViewModel.cs'

RESET = re.compile(r'(Apply\w+State)\s*\(\s*(?:[A-Za-z.]+\s*,\s*)?-1\s*\)')
# [A4-STEALTH-PRIME] The TUPLE form, `(XState, XBadgeColor) = (...)`. The Stealth card was reset that way, so this gate
# printed CHECK OK over a badge nothing primed. A tuple reset of property `XState` counts as badge `ApplyXState`.
TUPLE = re.compile(r'\(\s*(\w+)State\s*,\s*\w+\s*\)\s*=\s*\(')
IDENT = re.compile(r'(\w+Async)')
DECL = r'\b(?:private|public|internal)[^\r\n]*?\b%s\s*\('


def brace_body(src: str, start: int) -> str:
    """From `start`, return the text through the matching close of the first `{`."""
    depth, k, started = 0, start, False
    while k < len(src):
        if src[k] == '{':
            depth += 1
            started = True
        elif src[k] == '}':
            depth -= 1
            if started and depth == 0:
                break
        k += 1
    return src[start:k + 1]


def split_setconnected(src: str) -> tuple[str, str]:
    """Return (connect_branch, disconnect_branch) of SetConnected."""
    i = src.index('public void SetConnected(bool connected)')
    body = brace_body(src, i)
    k = body.index('\n        else')
    return body[:k], body[k:]


def body_of(src: str, name: str) -> str:
    """The full text of method `name`, or '' when it is not declared in this file."""
    m = re.search(DECL % re.escape(name), src)
    return brace_body(src, m.start()) if m else ''


def analyse(src: str) -> tuple[set, set, set]:
    """(reset_badges, primed_badges, unprimed)."""
    connect, disconnect = split_setconnected(src)
    reset = {m.group(1) for m in RESET.finditer(disconnect)}
    reset |= {'Apply%sState' % m.group(1) for m in TUPLE.finditer(disconnect)}

    # ⚠ TRANSITIVE CLOSURE TO A FIXPOINT, not a hand-unrolled two levels. The first
    # version walked exactly two levels and reported EVERY badge unprimed -- a checker
    # that is wrong in the RED direction is the dangerous kind, because it looks like a
    # finding rather than like a bug in the checker.
    #
    # It also matches BARE identifiers: the primes reach PrimeOneAsync as METHOD GROUPS
    # (PrimeOneAsync(RefreshHeldProtectStateAsync, ...)), so a pattern requiring '(' misses
    # them completely -- which is how the first attempt lost God Mode and the time lanes.
    seen: set[str] = set()
    work = list(IDENT.findall(connect))
    reachable = connect
    while work:
        name = work.pop()
        if name in seen:
            continue
        seen.add(name)
        body = body_of(src, name)
        if not body:
            continue
        reachable += body
        work.extend(IDENT.findall(body))

    # A badge counts as primed when the connect path reaches something that APPLIES it:
    # Apply<X>State with a real value, its Apply<X>Readout sibling, or one of the two
    # aggregate readouts that drive several badges from a single reply.
    primed = set()
    for badge in reset:
        stem = badge[len('Apply'):-len('State')]
        pats = [r'Apply' + re.escape(stem) + r'State\s*\(\s*(?!-1)',
                r'Apply' + re.escape(stem) + r'\w*Readout\s*\(',
                r'\(\s*' + re.escape(stem) + r'State\s*,\s*\w+\s*\)\s*=']   # a tuple SET on the connect path
        if stem == 'GodMode':
            pats.append(r'ApplyProtectState\s*\(')
        if stem == 'Lane':
            pats.append(r'ApplyTimeDilationReadout\s*\(')
        if any(re.search(p, reachable) for p in pats):
            primed.add(badge)
    return reset, primed, reset - primed


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument('--list', action='store_true')
    ap.add_argument('--selftest', action='store_true')
    a = ap.parse_args()

    src = io.open(VM, encoding='utf-8', errors='replace').read()

    if a.selftest:
        # ⛔ The negative control, on an in-memory COPY -- never on the file: strip the
        # prime call and the gate must go red. Without it the check could be vacuously
        # green, e.g. if it silently stopped finding the reset branch at all.
        broken = src.replace('ConnectPrime = PrimeHeldBadgesAsync();',
                             'ConnectPrime = RefreshHeldTimeStateAsync();')
        if broken == src:
            print('selftest: FAIL -- the control could not find the prime call to remove')
            return 1
        # [A4-STEALTH-PRIME] The second control: a TUPLE reset nothing primes must be caught too.
        tupled = src.replace('ApplyMouseCursorState(-1);',
                             'ApplyMouseCursorState(-1); (ZzzState, ZzzBadgeColor) = ("Off", "#999999");', 1)
        _, _, un_ok = analyse(src)
        _, _, un_bad = analyse(broken)
        _, _, un_tup = analyse(tupled)
        ok1, ok2, ok3 = (not un_ok), bool(un_bad), ('ApplyZzzState' in un_tup)
        print('  %-6s intact tree has 0 unprimed        (got %d)'
              % ('ok' if ok1 else 'FAIL', len(un_ok)))
        print('  %-6s prime removed -> unprimed appears (got %d)'
              % ('ok' if ok2 else 'FAIL', len(un_bad)))
        print('  %-6s a tuple reset is seen             (got %s)'
              % ('ok' if ok3 else 'FAIL', sorted(un_tup)))
        print('selftest: %s' % ('PASS' if ok1 and ok2 and ok3 else 'FAIL'))
        return 0 if (ok1 and ok2 and ok3) else 1

    reset, primed, unprimed = analyse(src)
    if a.list:
        for b in sorted(reset):
            print('  %-28s %s' % (b, 'primed' if b in primed else 'NOT PRIMED'))

    if unprimed:
        print('CHECK FAILED: %d badge(s) reset on disconnect and primed by nothing on '
              'connect:' % len(unprimed))
        for b in sorted(unprimed):
            print('   %s' % b)
        print('\nThe DLL holds survive a UI reconnect, so these cards show "never asked" '
              'over state\nthe DLL can answer for. Add the read to PrimeHeldBadgesAsync '
              '-- quietly: no IsBusy,\nno StatusText. [BADGEPRIME-2026-09-10]')
        return 1

    print('CHECK OK: %d badge(s) reset on disconnect, all %d primed on connect.'
          % (len(reset), len(primed)))
    return 0


if __name__ == '__main__':
    sys.exit(main())
