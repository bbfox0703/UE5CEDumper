r"""`[A4-ASSESS-2026-09-09]` — do the two sides of the named pipe agree on what crosses it?

    py tools/verify/pipe_wire_parity.py commands    # the LOUD axis: command names
    py tools/verify/pipe_wire_parity.py params      # request parameter keys
    py tools/verify/pipe_wire_parity.py replies     # reply keys nobody reads
    py tools/verify/pipe_wire_parity.py all

⛔ WHY THIS EXISTS. The CE Lua <-> DLL mailbox is version-gated AND hash-gated
(`tools/check_mailbox_contract.py`). The NAMED PIPE — the primary UI<->DLL channel, 99 commands and
~358 reply keys — is gated by nothing, and there is no shared constant table across the language
boundary: the C# side hand-types the strings.

    var req = new JsonObject { ["cmd"] = "walk_instance", ["addr"] = addr };

A wrong COMMAND name fails loudly (`unknown command`). A wrong KEY does not: it yields a default or
a null and the reply still says `ok: true`. That silent half has landed three times in two days:
`[SW7]` (a `delegate_pad` computed and never serialised), the `l12` rig (sent `addr=` where Fern
reads `instance_addr` — 2,000 requests measured nothing and reported cleanly), and `FlyStatus.Noclip`
(published by the DLL, never read by `ApplyFlyReadout`).

⛔⛔ WHAT THIS IS NOT. It is a POPULATION PRODUCER, not a finding list — the same relationship the
three agent sweeps had to the ~166 claims. Every hit needs a hand-read, because:

  * a key consumed only by CE Lua, a rig, a test or an offline tool is NOT dead;
  * a key read through a variable (`obj[name]`) is invisible to a literal grep;
  * a purely informational echo (build stamps, human-readable diagnostics) is fine.

⚠ AND THE COMPARISON IS GLOBAL, NOT PER-COMMAND, SO IT UNDERSTATES THE RISK. A key that is valid
for command X and a silent no-op for command Y matches here. The `l12` failure lived in exactly that
layer, and reaching it needs the per-command pairing this script does not do.

⚠ The rule audit #4 earned applies to this script itself: *a fix verified against the LIST it was
written from is not verified.* A wire checked only against the keys these regexes happen to see is
not checked — hence `--show-idioms`, which prints what each side's extraction actually matched so a
reader can see what it would have missed.
"""
from __future__ import annotations

import argparse
import os
import re
import subprocess
import sys

REPO = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..'))


def git(*a: str) -> str:
    return subprocess.run(['git'] + list(a), capture_output=True, text=True,
                          cwd=REPO, errors='replace').stdout


def tracked(*pats: str):
    return [f for f in git('ls-files', *pats).split('\n') if f.strip()]


def slurp(files) -> str:
    out = []
    for f in files:
        try:
            with open(os.path.join(REPO, f), encoding='utf-8', errors='replace') as fh:
                out.append(fh.read())
        except OSError:
            pass
    return '\n'.join(out)


DLL_SRC = ('dll/src/*.cpp', 'dll/src/*.h')
UI_SRC = ('ui/UE5DumpUI/*.cs', 'ui/UE5DumpUI/**/*.cs')
# Everything else that is legitimately a pipe client or a consumer of these names.
OTHER = ('scripts/*.lua', 'scripts/*.CT', 'tools/*.py', 'tools/**/*.py',
         'ui/UE5DumpUI.Tests/**/*.cs', 'dll/tests/*.cpp', 'docs/dll-spec.md')


def cmd_commands(show: bool) -> int:
    hdr = slurp(tracked('dll/src/*.h'))
    const = dict(re.findall(r'(CMD_[A-Z_0-9]+)\s*(?:=|\[\]\s*=)\s*"([a-z_0-9]+)"', hdr))
    fern = slurp(['dll/src/Fern.cpp'])
    dispatched = set(re.findall(r'cmd == Renge::(CMD_[A-Z_0-9]+)', fern))
    handled = {const[c] for c in dispatched if c in const}

    ui = slurp(tracked(*UI_SRC))
    sent = set(re.findall(r'\[\s*"cmd"\s*\]\s*=\s*"([a-z_0-9]+)"', ui))

    print('CMD_* constants declared        : %d' % len(const))
    print('constants Fern dispatches on    : %d' % len(dispatched))
    print('command names the C# UI sends   : %d' % len(sent))
    unresolved = sorted(dispatched - set(const))
    if unresolved:
        print('  ! constant with no literal here: ' + ' '.join(unresolved[:10]))

    ghost = sorted(sent - handled)
    unused = sorted(handled - sent)
    print('\n[A] sent by the UI, handled by NO DLL branch : %d   %s'
          % (len(ghost), ' '.join(ghost)))
    print('[B] handled by the DLL, never sent by the UI : %d   %s'
          % (len(unused), ' '.join(unused)))
    print('\n⚠ [B] is EXPECTED to be non-empty in general -- CE Lua, the tools/verify rigs and the\n'
          '  test harness are also pipe clients. It sizes the surface; it is not a finding.')
    if show:
        print('\nidiom matched, DLL: `cmd == Renge::CMD_*` -> the literal in dll/src/*.h')
        print('idiom matched, UI : `["cmd"] = "..."` inside any C# file')
    return 0


def cmd_params(show: bool) -> int:
    dll = slurp(tracked(*DLL_SRC))
    reads = set(re.findall(
        r'\b(?:request|req|p|params)\.(?:value|find|contains|count)\(\s*"([a-z_0-9]{2,40})"', dll))
    ui = slurp(tracked(*UI_SRC))
    sent = set()
    for m in re.finditer(r'new JsonObject\s*\{(.{0,4000}?)\}', ui, re.S):
        body = m.group(1)
        if '"cmd"' not in body:
            continue
        sent |= set(re.findall(r'\[\s*"([a-z_0-9]{2,40})"\s*\]\s*=', body))
    sent.discard('cmd')

    print('request-parameter keys the DLL reads : %d' % len(reads))
    print('request-parameter keys the UI sends  : %d' % len(sent))
    only_ui = sorted(sent - reads)
    only_dll = sorted(reads - sent)
    print('\n[A] SENT by the UI, read by no DLL request.value() : %d   %s'
          % (len(only_ui), ' '.join(only_ui)))
    print('\n[B] READ by the DLL, sent by no UI JsonObject      : %d' % len(only_dll))
    print('    ' + ' '.join(only_dll))
    print('\n⚠ [B] IS HEAVILY CONTAMINATED and must not be quoted as a finding: the UI builds many\n'
          '  requests in shapes this regex does not match, and CE Lua / the rigs send their own.\n'
          '  [A] is the direction worth reading -- a key the UI sends that nothing ever reads.')
    if show:
        print('\nidiom matched, DLL: `request|req|p|params . value|find|contains|count ("key")`')
        print('idiom matched, UI : keys assigned inside a `new JsonObject { ... "cmd" ... }`')
    return 0


def cmd_replies(show: bool) -> int:
    dll = slurp(tracked(*DLL_SRC))
    pub = set(re.findall(
        r'\b(?:data|obj|o|j|resp|result|item|entry|e|fld|f)\s*\[\s*"([a-z_][a-z0-9_]{1,40})"\s*\]\s*=',
        dll))
    ui = slurp(tracked(*UI_SRC))
    ui_keys = set(re.findall(r'\[\s*"([a-z_][a-z0-9_]{1,40})"\s*\]', ui))
    ui_keys |= set(re.findall(r'GetProp\w*\(\s*"([a-z_][a-z0-9_]{1,40})"', ui))
    other_keys = set(re.findall(r'["\']([a-z_][a-z0-9_]{1,40})["\']', slurp(tracked(*OTHER))))

    dead = sorted(k for k in pub if k not in ui_keys and k not in other_keys)
    print('reply keys the DLL assigns          : %d' % len(pub))
    print('keys the C# UI indexes by literal   : %d' % len(ui_keys))
    print('\n[A] published by the DLL, read by NOBODY in-tree : %d' % len(dead))
    for k in dead:
        print('    %s' % k)
    print('\n⛔ CANDIDATES, NOT FINDINGS. Each needs a hand-read: a build stamp echoed for humans is\n'
          '  fine; `mode_resolved` / `cmc_addr` on the fly-status reply are the shape that is not.')
    if show:
        print('\nidiom matched, DLL: `<obj>["key"] = ...` for a short list of receiver names')
        print('idiom matched, UI : any `["key"]` literal index, plus GetProp*("key")')
        print('consumers also searched: ' + ', '.join(OTHER))
    return 0


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument('axis', choices=['commands', 'params', 'replies', 'all'])
    ap.add_argument('--show-idioms', action='store_true',
                    help='print what each extraction actually matched, so a reader can see '
                         'what it would have missed')
    a = ap.parse_args()

    rc = 0
    for axis in (['commands', 'params', 'replies'] if a.axis == 'all' else [a.axis]):
        print('=' * 78)
        print('AXIS: %s' % axis)
        print('=' * 78)
        rc |= {'commands': cmd_commands, 'params': cmd_params,
               'replies': cmd_replies}[axis](a.show_idioms)
        print()
    return rc


if __name__ == '__main__':
    sys.exit(main())
