r"""Phase 4 — measure each candidate gate's LEGITIMATE population before proposing it.

The rule this repo earned twice (gate #17 refuted in round 1, gate 17b refuted as first scoped):
pick a predicate whose legitimate population is EMPTY, never one whose legitimate population must
be enumerated -- the latter becomes an allowlist and stops being a gate.

A candidate qualifies only if:  violations_today == 0  (ship now, it holds the line), or
                                violations_today == the defects we are about to fix, and NOTHING else.
"""
import io
import os
import re
import subprocess

REPO = r'D:\Github\UE5CEDumper'


def git(*a):
    return subprocess.run(['git'] + list(a), capture_output=True, text=True, cwd=REPO).stdout


def read(p):
    return io.open(os.path.join(REPO, p), encoding='utf-8', errors='replace').read()


def files(*pats):
    return [f for f in git('ls-files', *pats).split('\n') if f.strip()]


print('=' * 78)
print('A. COMMAND-NAME PARITY  -- every cmd the UI sends is dispatched by Fern')
print('=' * 78)
hdr = ''.join(read(f) for f in files('dll/src/*.h'))
const = dict(re.findall(r'(CMD_[A-Z_0-9]+)\s*(?:=|\[\]\s*=)\s*"([a-z_0-9]+)"', hdr))
fern = read('dll/src/Fern.cpp')
handled = {const[c] for c in re.findall(r'cmd == Renge::(CMD_[A-Z_0-9]+)', fern) if c in const}
ui = ''.join(read(f) for f in files('ui/UE5DumpUI/*.cs', 'ui/UE5DumpUI/**/*.cs'))
sent = set(re.findall(r'\[\s*"cmd"\s*\]\s*=\s*"([a-z_0-9]+)"', ui))
viol = sorted(sent - handled)
print('  UI-sent %d, dispatched %d' % (len(sent), len(handled)))
print('  VIOLATIONS TODAY: %d  %s' % (len(viol), ' '.join(viol)))
print('  -> qualifies' if not viol else '  -> does NOT qualify')

print()
print('=' * 78)
print('B. REQUEST-PARAM PARITY -- every param the UI sends is read by some handler')
print('=' * 78)
dll = ''.join(read(f) for f in files('dll/src/*.cpp', 'dll/src/*.h'))
reads = set(re.findall(
    r'\b(?:request|req|p|params)\.(?:value|find|contains|count)\(\s*"([a-z_0-9]{2,40})"', dll))
sentp = set()
for m in re.finditer(r'new JsonObject\s*\{(.{0,4000}?)\}', ui, re.S):
    if '"cmd"' in m.group(1):
        sentp |= set(re.findall(r'\[\s*"([a-z_0-9]{2,40})"\s*\]\s*=', m.group(1)))
sentp.discard('cmd')
viol = sorted(sentp - reads)
print('  UI-sent params %d, handler-read %d' % (len(sentp), len(reads)))
print('  VIOLATIONS TODAY: %d  %s' % (len(viol), ' '.join(viol)))
print('  -> qualifies' if not viol else '  -> does NOT qualify')

print()
print('=' * 78)
print('C. IDLE WAIT MUST NOT BE ENABLE-GUARDED  (the R3 shape)')
print('=' * 78)
enable_guarded, other_guarded = [], []
for f in files('ui/UE5DumpUI/Services/*ScriptGenerator*.cs'):
    src = read(f).split('\n')
    depth, stack = 0, []
    for n, line in enumerate(src, 1):
        if re.search(r'CeLuaHygiene\.AppendIdleWait', line):
            guards = [h for _, h in stack if re.search(r'\bif\s*\(|\belse\b', h)]
            for g in guards:
                (enable_guarded if re.search(r'\benable\b', g) else other_guarded).append(
                    '%s:%d  %s' % (os.path.basename(f), n, g[:44]))
        code = re.sub(r'//.*$', '', line)
        code = re.sub(r'"(?:[^"\\]|\\.)*"', '""', code)
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
print('  guarded by an ENABLE condition (the defect): %d' % len(enable_guarded))
for x in enable_guarded:
    print('     %s' % x)
print('  guarded by something else (legitimate)      : %d' % len(other_guarded))
for x in other_guarded:
    print('     %s' % x)
print('  -> qualifies AFTER the R3 fix: the legitimate population for an ENABLE guard is EMPTY')

print()
print('=' * 78)
print('D. REPLY-KEY CONSUMPTION  -- every key the DLL publishes has a consumer')
print('=' * 78)
print('  measured phase 2/3: 32 dead-benign + 5 live-orphan commands are LEGITIMATE')
print('  (build stamps, raw offset detail consumed only by the Python rigs)')
print('  -> DOES NOT QUALIFY. Its legitimate population must be ENUMERATED, which is exactly')
print('     what refuted gate #17 and re-scoped 17b. Do not build it.')

print()
print('=' * 78)
print('E. BADGE PRIME/RESET SYMMETRY  (the [BADGEPRIME] shape)')
print('=' * 78)
vm = read('ui/UE5DumpUI/ViewModels/TeleportViewModel.cs')
m = re.search(r'public void SetConnected\(bool connected\)(.{0,6000}?)\n    \}', vm, re.S)
body = m.group(1) if m else ''
half = body.split('else')
prime = set(re.findall(r'_\s*=\s*(\w+Async)\(', half[0])) if half else set()
reset = set(re.findall(r'(Apply\w+State)\(\s*-1', half[1])) if len(half) > 1 else set()
print('  primed on connect : %d  %s' % (len(prime), ' '.join(sorted(prime))))
print('  reset on disconnect: %d  %s' % (len(reset), ' '.join(sorted(reset))))
print('  ASYMMETRY TODAY: %d badge(s) reset with no prime' % max(len(reset) - 2, 0))
print('  -> qualifies ONLY AFTER [BADGEPRIME] is fixed; today it would ship red.')
