"""Deterministic stratified sample of substantive comment blocks (>= 3 consecutive comment lines).

    py tools/verify/comment_sample.py [seed]      # default seed 20260926 -> out/comment_audit/sample.json

The sample judged for docs/comment-integrity-eval.md (90 blocks, 30 per stratum). Re-run with a NEW seed to
track the stale rate over time; the same seed reproduces the 2026-09-26 sample while the files are unchanged.
"""
import sys
import json, os, random, re, subprocess

os.chdir(os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..')))
files = subprocess.run(['git', 'ls-files', 'dll/src', 'ui/UE5DumpUI', 'scripts'], capture_output=True,
                       text=True).stdout.split()
CORE = {'Genau', 'Macht', 'Himmel', 'Aura', 'Ubel', 'Serie', 'Denken', 'Grimoire', 'Lineal', 'GraphPath',
        'VersionNeedleScan', 'Voll', 'Scharf', 'Sense', 'Neu'}


def stratum(f):
    if f.startswith('dll/src/'):
        return 'S1_dll_core' if os.path.splitext(os.path.basename(f))[0] in CORE else 'S2_dll_features'
    return 'S3_ui_scripts'


def is_comment(f, l):
    s = l.strip()
    if f.endswith(('.cpp', '.h', '.cs')):
        return s.startswith(('//', '/*', '*'))
    if f.endswith(('.lua', '.CT')):
        return s.startswith('--')
    return False


blocks = {'S1_dll_core': [], 'S2_dll_features': [], 'S3_ui_scripts': []}
for f in files:
    if not f.endswith(('.cpp', '.h', '.cs', '.lua', '.CT')):
        continue
    lines = open(f, encoding='utf-8-sig', errors='replace').read().split('\n')
    i = 0
    while i < len(lines):
        if is_comment(f, lines[i]):
            j = i
            while j < len(lines) and is_comment(f, lines[j]):
                j += 1
            if j - i >= 3:
                blocks[stratum(f)].append((f, i + 1, j))
            i = j
        else:
            i += 1

rng = random.Random(int(sys.argv[1]) if len(sys.argv) > 1 else 20260926)
sample = {}
for k, v in blocks.items():
    sample[k] = [dict(file=f, start=s, end=e) for f, s, e in rng.sample(v, 30)]
    print(k, 'blocks', len(v), 'sampled 30')
os.makedirs('out/comment_audit', exist_ok=True)
json.dump(sample, open('out/comment_audit/sample.json', 'w', encoding='utf-8'), indent=1)
