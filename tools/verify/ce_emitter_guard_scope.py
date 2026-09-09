r"""Is each AppendIdleWait*/AppendContractCheck call INSIDE a C# conditional, or unconditional?

⛔ Checks the WORLD, not the list -- the house rule audit #4's B34/B14 earned. Brace-depth
tracking from the enclosing method, so an outdented comment (the exact thing that hid the
SeeThrough case) cannot fool it.
"""
import io
import os
import re
import subprocess

REPO = r'D:\Github\UE5CEDumper'
files = [f for f in subprocess.run(
    ['git', 'ls-files', 'ui/UE5DumpUI/Services/*ScriptGenerator*.cs'],
    capture_output=True, text=True, cwd=REPO).stdout.split('\n') if f.strip()]

CALL = re.compile(r'CeLuaHygiene\.(AppendIdleWaitOrBail|AppendIdleWait|AppendContractCheck)\b')

for f in files:
    src = io.open(os.path.join(REPO, f), encoding='utf-8').read().split('\n')
    depth = 0
    stack = []          # (depth_at_open, header_text)
    for n, line in enumerate(src, 1):
        code = re.sub(r'//.*$', '', line)
        code = re.sub(r'"(?:[^"\\]|\\.)*"', '""', code)   # kill string literals

        m = CALL.search(line)
        if m:
            guards = [h for d, h in stack if re.search(r'\bif\s*\(|\belse\b', h)]
            tag = 'GUARDED by: ' + ' / '.join(guards) if guards else 'unconditional'
            flag = '  <== ' if guards else '      '
            print('%s%-46s %-5s %-22s %s' % (flag, os.path.basename(f), n, m.group(1), tag))

        for ch in code:
            if ch == '{':
                head = ''
                for k in range(n - 1, max(n - 4, 0), -1):
                    t = re.sub(r'//.*$', '', src[k - 1]).strip()
                    if t and t != '{':
                        head = t
                        break
                stack.append((depth, head))
                depth += 1
            elif ch == '}':
                depth -= 1
                if stack:
                    stack.pop()
