r"""`[A4-ASSESS-2026-09-09]` phase 2 — the pipe contract, PER COMMAND rather than globally.

    py tools/verify/pipe_command_contract.py list            # every command, its params and reply keys
    py tools/verify/pipe_command_contract.py show <cmd>      # one command in detail
    py tools/verify/pipe_command_contract.py json            # machine-readable, for the pairing pass

⛔ WHY PER-COMMAND. `pipe_wire_parity.py` compares the two sides GLOBALLY and measures clean:
0 UI-sent params no DLL reads, 0 command names either side lacks. That is a real result and it is
also the weaker question. A key that is valid for command X and a silent no-op for command Y
matches in a global comparison -- and that is precisely where the `l12` rig failure lived (it sent
`addr=` where `Fern` reads `instance_addr`; both names exist somewhere on the DLL side, so a global
check sees nothing, while 2,000 requests measured nothing and reported `ok: true`).

WHAT IT EXTRACTS, from `Fern.cpp`'s dispatch chain:
  * the handler block for each `if (cmd == Renge::CMD_*)`, by brace matching;
  * PARAMS  -- `request.value("k", ...)` / `.find("k")` / `.contains("k")` inside that block;
  * REPLIES -- `<recv>["k"] = ...` inside that block.

⚠ WHAT IT CANNOT SEE, and every consumer must be told:
  * a handler that delegates to a helper function -- the helper's reads and writes are OUTSIDE the
    block, so a command can look param-less or reply-less and not be. `has_delegation` flags the
    blocks that call into one, and those are the rows a human must read.
  * keys built by string concatenation or held in a variable.
  * nested lambdas whose braces the matcher counts but whose semantics it does not.
So this is a POPULATION PRODUCER, exactly like the claim sweeps: every pairing needs a hand-read.
"""
from __future__ import annotations

import argparse
import io
import json
import os
import re
import subprocess
import sys

REPO = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..'))
FERN = 'dll/src/Fern.cpp'

PARAM = re.compile(r'\brequest\.(?:value|find|contains|count)\(\s*"([a-z_0-9]{1,40})"')
REPLY = re.compile(r'\b(?:data|obj|o|j|resp|result|item|entry|e|fld|f)\s*\[\s*"([a-z_0-9]{1,40})"\s*\]\s*=')
DELEGATE = re.compile(r'\b(?:Handle|Build|Emit|Make|Fill|Write)[A-Z]\w*\s*\(')


def read(p: str) -> str:
    return io.open(os.path.join(REPO, p), encoding='utf-8', errors='replace').read()


def const_map() -> dict:
    hdr = ''.join(read(f) for f in
                  [x for x in subprocess.run(['git', 'ls-files', 'dll/src/*.h'],
                                             capture_output=True, text=True,
                                             cwd=REPO).stdout.split('\n') if x.strip()])
    return dict(re.findall(r'(CMD_[A-Z_0-9]+)\s*(?:=|\[\]\s*=)\s*"([a-z_0-9]+)"', hdr))


def blocks():
    """Yield (literal, const, start_line, end_line, body) per dispatch branch."""
    cm = const_map()
    src = read(FERN).split('\n')
    out = []
    for i, line in enumerate(src):
        m = re.search(r'cmd == Renge::(CMD_[A-Z_0-9]+)', line)
        if not m:
            continue
        const = m.group(1)
        # Walk forward to the first '{' then brace-match. Strings and // comments are
        # neutralised first so a '{' inside an emitted Lua line cannot unbalance us.
        depth, started, j = 0, False, i
        while j < len(src):
            code = re.sub(r'//.*$', '', src[j])
            code = re.sub(r'"(?:[^"\\]|\\.)*"', '""', code)
            for ch in code:
                if ch == '{':
                    depth += 1
                    started = True
                elif ch == '}':
                    depth -= 1
            if started and depth <= 0:
                break
            j += 1
        body = '\n'.join(src[i:j + 1])
        out.append({
            'cmd': cm.get(const, '?' + const),
            'const': const,
            'line': i + 1,
            'end': j + 1,
            'lines': j - i + 1,
            'params': sorted(set(PARAM.findall(body))),
            'replies': sorted(set(REPLY.findall(body))),
            'has_delegation': bool(DELEGATE.search(body)),
        })
    return out


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument('action', choices=['list', 'show', 'json'])
    ap.add_argument('cmd', nargs='?')
    a = ap.parse_args()

    bs = blocks()
    if a.action == 'json':
        print(json.dumps(bs, ensure_ascii=False, separators=(',', ':')))
        return 0

    if a.action == 'show':
        for b in bs:
            if b['cmd'] == a.cmd or b['const'] == a.cmd:
                print(json.dumps(b, ensure_ascii=False, indent=1))
                return 0
        print('*** no such command: %s' % a.cmd)
        return 2

    print('%-34s %5s %5s %5s %5s  %s' % ('command', 'line', 'len', 'prm', 'rep', 'delegates?'))
    thin = 0
    for b in sorted(bs, key=lambda x: x['cmd']):
        print('%-34s %5d %5d %5d %5d  %s'
              % (b['cmd'], b['line'], b['lines'], len(b['params']), len(b['replies']),
                 'YES' if b['has_delegation'] else ''))
        if not b['replies'] and b['has_delegation']:
            thin += 1
    print('\n%d command(s).' % len(bs))
    print('%d have NO extractable reply key but DO delegate -- their contract is in a helper '
          'and a human must read those.' % thin)
    return 0


if __name__ == '__main__':
    sys.exit(main())
