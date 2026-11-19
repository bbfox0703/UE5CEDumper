#!/usr/bin/env python3
"""build_corpus_manifest.py — regenerate tools/ghidra/corpus-manifest.tsv from MEASUREMENT.

Nothing in the output is typed by hand. Every column is read from one of five sources:

  1. tools/ghidra/sweep.sh          TAG | PROJECT | GLOB | GS_TRUE      (the source of truth)
  2. <GHIDRA_PROJS>/*.rep           program names + Ghidra's recorded `Executable Location`
                                    and `Executable MD5`, read STRAIGHT OFF DISK — no Ghidra,
                                    no project lock, nothing opened for write.
  3. the recorded path itself       does the binary still exist, is a .pdb beside it, MD5 today
  4. Steam libraryfolders.vdf +     appid / installdir / SizeOnDisk for what is installed NOW
     steamapps/appmanifest_*.acf
  5. Steam appcache/appinfo.vdf     appid for titles that are NOT installed (so the maintainer
                                    can find them again). Entry framing only — an appid is
                                    reported solely when the installdir string is inside that
                                    app's own blob, never inferred.

Anything none of the five can answer is written literally as UNKNOWN.

  py tools/ghidra/build_corpus_manifest.py [--projs D:/Tools/GHIDRA_Projs] [-o <tsv>]

Hashing every corpus binary reads several GB; pass --no-hash to skip it (BINARY_STATE then
reports PRESENT/GONE without distinguishing a drifted build from an identical one).
"""
from __future__ import annotations
import argparse, hashlib, json, os, re, struct, sys

sys.stdout.reconfigure(encoding='utf-8', errors='replace')
REPO = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

# ── 1. sweep.sh ─────────────────────────────────────────────────────────────────────────────
def parse_sweep(path):
    """Pull the ROWS=( ... ) array. Returns [(tag, project, glob, gs_true)] in file order."""
    rows, inside = [], False
    for line in open(path, encoding='utf-8'):
        s = line.strip()
        if s.startswith('ROWS=('):
            inside = True
            continue
        if inside:
            if s == ')':
                break
            m = re.match(r'^"([^|]+)\|([^|]*)\|([^|]*)\|(.*)"$', s)
            if m:
                rows.append((m.group(1), m.group(2), m.group(3), m.group(4)))
    return rows

# ── 2. Ghidra project metadata, read off disk ───────────────────────────────────────────────
KEYS = [b'Executable Location', b'Executable MD5']

def _read_utf(buf, pos):
    if pos + 4 > len(buf):
        return None
    n = struct.unpack_from('>I', buf, pos)[0]
    if n == 0 or n > 4096 or pos + 4 + n > len(buf):
        return None
    try:
        return buf[pos + 4:pos + 4 + n].decode('utf-8')
    except UnicodeDecodeError:
        return None

def _scan_gbf(path, found, chunk=1 << 24):
    tail = b''
    with open(path, 'rb') as f:
        while True:
            block = f.read(chunk)
            if not block:
                return False
            buf = tail + block
            for key in KEYS:
                for m in re.finditer(re.escape(key), buf):
                    v = _read_utf(buf, m.end())
                    if v:
                        found.setdefault(key.decode(), set()).add(v)
            if len(found) == len(KEYS):
                return True
            tail = buf[-512:]

def read_project(rep):
    """{program_name: {'exe_path','exe_md5','db_bytes'}} for one .rep, off disk."""
    out = {}
    idx = os.path.join(rep, 'idata', '~index.dat')
    if not os.path.isfile(idx):
        return out
    names = {}
    for line in open(idx, encoding='utf-8', errors='replace'):
        m = re.match(r'^\s*([0-9a-f]{8}):(.*?):([0-9a-f]+)\s*$', line)
        if m:
            names[m.group(1)] = m.group(2)
    for pid, pname in names.items():
        dbdir = os.path.join(rep, 'idata', '00', '~' + pid + '.db')
        rec = {'exe_path': None, 'exe_md5': None, 'db_bytes': 0}
        if os.path.isdir(dbdir):
            gbfs = sorted(os.listdir(dbdir))
            rec['db_bytes'] = sum(os.path.getsize(os.path.join(dbdir, g)) for g in gbfs)
            found = {}
            for g in gbfs:
                if _scan_gbf(os.path.join(dbdir, g), found):
                    break
            def one(k):
                v = found.get(k)
                return sorted(v)[0] if v and len(v) == 1 else (' | '.join(sorted(v)) if v else None)
            rec['exe_path'] = one('Executable Location')
            rec['exe_md5'] = one('Executable MD5')
        out[pname] = rec
    return out

# ── 4/5. Steam ──────────────────────────────────────────────────────────────────────────────
def steam_root():
    try:
        import winreg
        for hive, key in ((winreg.HKEY_LOCAL_MACHINE, r'SOFTWARE\WOW6432Node\Valve\Steam'),
                          (winreg.HKEY_LOCAL_MACHINE, r'SOFTWARE\Valve\Steam')):
            try:
                with winreg.OpenKey(hive, key) as k:
                    p = winreg.QueryValueEx(k, 'InstallPath')[0]
                    if os.path.isdir(p):
                        return p
            except OSError:
                pass
    except ImportError:
        pass
    d = r'C:\Program Files (x86)\Steam'
    return d if os.path.isdir(d) else None

def steam_libraries(root):
    """Library roots from libraryfolders.vdf. NOTE: that file's `apps` map is NOT proof of
    installation — 1144800 is listed under H: here with a byte count while its appmanifest and
    its folder are both gone. Only appmanifest_<id>.acf + the installdir folder are proof."""
    libs = []
    vdf = os.path.join(root, 'steamapps', 'libraryfolders.vdf') if root else None
    if vdf and os.path.isfile(vdf):
        for m in re.finditer(r'"path"\s*"([^"]+)"', open(vdf, encoding='utf-8', errors='replace').read()):
            p = m.group(1).replace('\\\\', '\\')
            if os.path.isdir(p):
                libs.append(p)
    return libs

def steam_installed(libs):
    """{installdir_lower: (appid, lib, size_gb, name, buildid)} from appmanifest_*.acf."""
    out = {}
    for lib in libs:
        sa = os.path.join(lib, 'steamapps')
        if not os.path.isdir(sa):
            continue
        for fn in os.listdir(sa):
            if not (fn.startswith('appmanifest_') and fn.endswith('.acf')):
                continue
            t = open(os.path.join(sa, fn), encoding='utf-8', errors='replace').read()
            def g(k):
                m = re.search(r'"%s"\s*"([^"]*)"' % k, t)
                return m.group(1) if m else ''
            d = g('installdir')
            if d:
                out[d.lower()] = (g('appid'), lib, round(int(g('SizeOnDisk') or 0) / 2**30, 1),
                                  g('name'), g('buildid'))
    return out

def appinfo_lookup(root, needles):
    """{needle: appid} for titles no longer installed. Walks appcache/appinfo.vdf entry framing
    and reports an appid only when the needle bytes lie inside that entry's own blob."""
    out = {}
    p = os.path.join(root, 'appcache', 'appinfo.vdf') if root else None
    if not p or not os.path.isfile(p) or not needles:
        return out
    data = open(p, 'rb').read()
    magic = struct.unpack_from('<I', data, 0)[0]
    pos = 16 if magic == 0x07564429 else 8
    enc = {n: n.encode('utf-8') for n in needles}
    while pos + 8 <= len(data):
        appid, size = struct.unpack_from('<II', data, pos)
        if appid == 0:
            break
        body = data[pos + 8:pos + 8 + size]
        for n, b in enc.items():
            if n not in out and b in body:
                out[n] = str(appid)
        pos += 8 + size
    return out

# ── glue ────────────────────────────────────────────────────────────────────────────────────
def to_win(p):
    return p.lstrip('/').replace('/', '\\') if p else None

def hashes(p):
    """(md5, sha256) in ONE read. md5 is what Ghidra recorded at import, so it is the
    comparison key; sha256 is the stronger identity the manifest schema asks for."""
    m, s = hashlib.md5(), hashlib.sha256()
    with open(p, 'rb') as f:
        for b in iter(lambda: f.read(1 << 22), b''):
            m.update(b)
            s.update(b)
    return m.hexdigest(), s.hexdigest()

COLS = ['TAG', 'ROLE', 'PROJECT', 'REP', 'PROGRAM', 'SELECTED', 'ACQUISITION', 'STEAM_APPID',
        'STEAM_INSTALLDIR', 'STEAM_LIB_TODAY', 'STEAM_GB', 'BINARY_STATE', 'PATH_TODAY',
        'GHIDRA_PATH', 'RECORDED_MD5', 'PDB', 'REP_GB', 'NOTE']

def classify_acquisition(path_win):
    """STEAM / SELF-BUILT / ARCHIVE / UNKNOWN — from the recorded path shape only."""
    if not path_win:
        return 'UNKNOWN', None
    low = path_win.lower()
    m = re.search(r'\\steamapps\\common\\([^\\]+)', low)
    if m:
        return 'STEAM', re.search(r'\\steamapps\\common\\([^\\]+)', path_win).group(1)
    if '\\ue_analyze_data\\' in low or '\\unreal projects\\' in low:
        return 'SELF-BUILT', None
    if '\\game archive\\' in low or low.startswith('d:\\tmp\\'):
        return 'ARCHIVE', None
    return 'UNKNOWN', None

SCHEMA = 'ue5cedumper.corpus-manifest/2'
SRC_MAP = {'STEAM': 'steam', 'SELF-BUILT': 'self-built', 'ARCHIVE': 'archive',
           'UNKNOWN': 'unknown'}


def pick_anchor(progs):
    """One representative program per tag — the schema carries a single binary per entry.
    A modular tag's programs all come out of ONE install, so any of them answers "is the
    install present"; the choice just has to be deterministic and meaningful.
      1. the program sweep.sh's own GLOB selects (that IS the maintainer's choice),
      2. else CoreUObject (the module that defines GObjects — the decisive one),
      3. else the single/first program by name."""
    sel = [p for p in progs if p['selected'] == 'yes']
    pool = sel or progs
    for p in pool:
        if p['glob'] != '-' and p['glob'] in p['program']:
            return p
    for p in pool:
        if 'CoreUObject' in p['program']:
            return p
    return sorted(pool, key=lambda p: p['program'])[0]


# Roots searched for a SECOND copy of a binary that Steam cannot re-serve. "This file exists"
# is not the same question as "is this the ONLY copy" — and the second question is the one that
# decides what to back up. MEASURED: UE4.23-Flying has a bit-identical twin on X: that a
# same-path check cannot see, while UE4.15-Flying and both StackOBots have exactly one copy.
#
# D:\UE_Analyze_Data is listed FIRST and is the one that matters operationally: X: is a
# spinning-disk backup (800k+ files, slow to walk) while D: is the fast working copy, so a row
# whose only recorded path is on X: should still resolve to a D: twin if one exists.
# MEASURED 2026-07-29: all six rows the manifest called "binary GONE" — DQ I&II, FF7 Remake,
# FF7 Rebirth, Hogwarts Legacy, Manor Lords, Octopath — have md5-IDENTICAL copies under
# 'D:\UE_Analyze_Data\Game Binary backup'. They were reported missing only because this list
# did not include that root, not because anything was lost.
DUPE_ROOTS = [r'D:\UE_Analyze_Data', r'X:\UE_Analyze_Data',
              r'D:\tmp\Game archive', r'D:\Unreal Projects']


_DUPE_INDEX = None


def _dupe_index(roots=None):
    """Lazy ONE-PASS basename -> [paths] index over DUPE_ROOTS, built once and reused.

    The previous design walked every root inside every call. That was tolerable only while the
    search was skipped for Steam rows; now that it is not (see the call site), 38 rows x a walk
    of X: — a spinning disk carrying 800k+ files — would dominate the runtime."""
    global _DUPE_INDEX
    if _DUPE_INDEX is not None:
        return _DUPE_INDEX
    idx = {}
    for root in (roots if roots is not None else DUPE_ROOTS):
        if not os.path.isdir(root):
            continue
        for dirpath, _dirs, files in os.walk(root):
            for fn in files:
                if os.path.splitext(fn)[1].lower() not in ('.exe', '.dll'):
                    continue
                idx.setdefault(fn.lower(), []).append(os.path.join(dirpath, fn))
    _DUPE_INDEX = idx
    return idx


def find_duplicate_copies(target, want_md5, roots=None, name_hint=None, size_prefilter=True):
    """Other paths holding byte-identical bytes. Name-indexed, then md5-confirmed — a same-name
    same-size file is NOT proof (a repackage emits a fresh PE at identical size; measured on
    UE423_Flying, 43f6b130 vs f61ec1be).

    `name_hint` lets a GONE row still be searched: the filename comes from the Ghidra record
    rather than from a file that no longer exists. That case is the whole point — a row whose
    binary is missing is exactly the one where a surviving copy matters most.

    ⚠ `size_prefilter` MUST be False unless the file at `target` IS the corpus build. The md5 test
    below compares against `want_md5` (Ghidra's import-time hash = the corpus build), but the
    prefilter sizes candidates against the file *currently* at `target`, which on a DRIFTED row is
    the build that REPLACED the corpus one. The surviving corpus copy has the old size, so it would
    be skipped before its md5 was ever computed, and the row would report `duplicate_copies: []`
    and "the .rep is the last copy" while byte-identical copies exist. That is worse than a nulled
    field — a null reads as *unknown*, this is a positive false claim, and it fires only on drifted
    rows, i.e. exactly the rows the field exists to protect. Classic shape: an optimisation correct
    only under an assumption ("the file on disk is the corpus build") that is precisely false where
    it matters."""
    if not want_md5:
        return []
    base = os.path.basename(name_hint or target or '').lower()
    if not base:
        return []
    out = []
    tnorm = os.path.normcase(os.path.abspath(target)) if target else None
    size = (os.path.getsize(target)
            if (size_prefilter and target and os.path.isfile(target)) else None)
    for cand in _dupe_index(roots).get(base, ()):
        if tnorm and os.path.normcase(os.path.abspath(cand)) == tnorm:
            continue
        try:
            # Size is only a cheap prefilter; md5 is the real test. A GONE or DRIFTED row has no
            # trustworthy live size, so it goes straight to the hash.
            if size is not None and os.path.getsize(cand) != size:
                continue
            if hashes(cand)[0] == want_md5:
                out.append(cand)
        except OSError:
            pass
    return sorted(out)


def write_json(entries, path):
    """Emit tools/ghidra/corpus-manifest.json in the schema tools/ghidra/preflight.py reads.
    EVERY unmeasured field is null — null means UNKNOWN and preflight reports it as unknown.
    A guessed app id would send the maintainer to install the wrong game."""
    from datetime import datetime, timezone
    doc = {'schema': SCHEMA,
           'generated_utc': datetime.now(timezone.utc).strftime('%Y-%m-%dT%H:%M:%SZ'),
           'generator': 'tools/ghidra/build_corpus_manifest.py',
           'entries': {}}
    for tag in entries:
        progs = entries[tag]
        a = pick_anchor(progs)
        relpath = None
        if a['acq'] == 'STEAM' and a['installdir'] and a['ghidra_path']:
            m = re.search(r'\\steamapps\\common\\[^\\]+\\(.*)$', a['ghidra_path'])
            if m:
                relpath = m.group(1).replace('\\', '/')
        note = []
        if len(progs) > 1:
            note.append(f'{len(progs)} programs in this project: '
                        + ', '.join(sorted(p['program'] for p in progs))
                        + f'; anchor = {a["program"]}')
        if a['state'] == 'DRIFTED':
            # "the .rep is the last copy" is a claim about the WHOLE machine, so it may only be
            # made when the duplicate search came back empty. It was previously unconditional and
            # therefore printed next to a DUPLICATE COPIES line listing two surviving copies —
            # self-contradictory, and the wrong half is the one that drives a keep/drop decision.
            note.append('BINARY DRIFTED — the file at this path is NOT the build Ghidra '
                        'imported. Steam serves only the current build, so a reinstall cannot '
                        'restore it'
                        + ('; the .rep is the last copy.' if not a['dupes'] else
                           ', but byte-identical copies of the corpus build survive elsewhere '
                           '(see duplicate_copies) — this row is NOT single-copy.'))
        if a['state'] == 'GONE':
            note.append('BINARY GONE — title uninstalled; reinstall to re-import.')
        if a['acq'] == 'ARCHIVE':
            note.append('Not a live install — restored from a maintainer-held archive copy.')
        if a['acq'] == 'SELF-BUILT':
            note.append('Built by the maintainer; recreating it needs that exact engine '
                        'version installed plus a repackage.')
            # A build-output directory is not storage. `Binaries*/Win64` under a live
            # Unreal project is rewritten by the next build and emptied by Clean.
            # MEASURED trap: the X:\UE_Analyze_Data packaged copy of UE423_Flying is a
            # DIFFERENT binary (md5 43f6b130..) from the imported one (f61ec1be..) — a
            # repackage produces a fresh PE, so it is NOT a backup of this row.
            if '\\Unreal Projects\\' in (a['ghidra_path'] or ''):
                note.append('VOLATILE LOCATION — this is a live Unreal project build-output '
                            'dir, not an archive. A rebuild or Clean destroys THIS copy, and a '
                            'repackage elsewhere yields a different PE (measured: 43f6b130 vs '
                            'f61ec1be), so a same-named packaged build is not a substitute.'
                            + (' A byte-identical copy does exist elsewhere — see '
                               'duplicate_copies; this row is NOT single-copy.' if a['dupes']
                               else ' No byte-identical copy found in any archive root — this '
                                    'row is SINGLE-COPY.'))
        if a['acq'] in ('SELF-BUILT', 'ARCHIVE') and not a['dupes'] and a['state'] != 'GONE':
            note.append('SINGLE COPY — no byte-identical duplicate in any archive root, and '
                        'no store can re-serve it. Losing this file loses the row.')
        # BUILD IDENTITY is only recordable when the installed bytes ARE the corpus bytes.
        # On a DRIFTED row the buildid/size/sha256 on disk describe the build that REPLACED
        # the corpus one, so writing them would assert the wrong build is the right one —
        # precisely the false pass the schema warns about (three Everspace 2 sweep rows are
        # three builds of ONE app; only the newest is on disk). Null them instead.
        identical = a['state'] == 'MATCH'
        buildid = a['buildid'] if (identical and a['buildid']) else None
        size_b = a['size'] if identical else None
        sha_b = a['sha256'] if identical else None
        # binary_md5 is the ONLY identity that survives the binary itself: Ghidra recorded it at
        # IMPORT time, so it is present even for a row whose file is GONE or DRIFTED. Unlike
        # size/sha256 above it is never nulled — it describes the corpus build, not today's file.
        # Without it a wrong-build row (UE5.5-Everspace2) is indistinguishable from an unmeasured
        # one, and a re-downloaded title cannot be checked against what was actually analysed.
        md5_b = a['md5']
        if a['dupes']:
            note.append('DUPLICATE COPIES (byte-identical, verified by md5): '
                        + ' | '.join(a['dupes']))
        doc['entries'][tag] = {
            'ghidra_project': a['project'],
            'source': SRC_MAP.get(a['acq'], 'unknown'),
            'steam_app_id': a['appid'] if a['acq'] == 'STEAM' and a['appid'] not in
                            ('UNKNOWN', '-', None) else None,
            'steam_installdir': a['installdir'] if a['acq'] == 'STEAM' else None,
            'binary_relpath': relpath,
            'binary_last_seen': a['ghidra_path'],
            'needs_pdb': {'YES': True, 'NO': False}.get(a['pdb']),
            'pdb_relpath': a['pdb_path'],
            'pdb_size_bytes': a['pdb_size'],
            'steam_buildid': buildid,
            'binary_size_bytes': size_b,
            'binary_sha256': sha_b,
            'binary_md5': md5_b,
            'duplicate_copies': a['dupes'],
            'provenance': 'ghidra-metadata',
            'notes': ' | '.join(note),
        }
    with open(path, 'w', encoding='utf-8', newline='\n') as f:
        json.dump(doc, f, indent=1, ensure_ascii=False)
        f.write('\n')
    print(f'{len(doc["entries"])} tags -> {path}')


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--projs', default=os.environ.get('GHIDRA_PROJS', r'D:\Tools\GHIDRA_Projs'))
    ap.add_argument('--sweep', default=os.path.join(REPO, 'tools', 'ghidra', 'sweep.sh'))
    ap.add_argument('-o', '--out', default=os.path.join(REPO, 'tools', 'ghidra', 'corpus-manifest.tsv'))
    ap.add_argument('--no-hash', action='store_true')
    a = ap.parse_args()

    rows = parse_sweep(a.sweep)
    root = steam_root()
    libs = steam_libraries(root)
    installed = steam_installed(libs)

    cache, out, wanted, entries = {}, [], set(), {}
    rep_gb = {}
    for tag, proj, glob, gs_true in rows:
        # ORACLE = sweep.sh supplies GS_TRUE for this tag (it can answer "did it resolve to the
        # RIGHT address?"). NOISE-PROBE = empty GS_TRUE, answers only "did anything hit that
        # should not have?". This is the single most useful column for a keep/drop decision, and
        # it is read from sweep.sh so it can never disagree with it.
        role = 'NOISE-PROBE' if not gs_true.strip() else 'ORACLE'
        rep = os.path.join(a.projs, proj + '.rep')
        rep_exists = os.path.isdir(rep)
        if proj not in cache:
            cache[proj] = read_project(rep) if rep_exists else {}
            rep_gb[proj] = round(sum(
                os.path.getsize(os.path.join(dp, f))
                for dp, _, fs in os.walk(rep) for f in fs) / 2**30, 2) if rep_exists else 0.0
        progs = cache[proj]
        if not progs:
            out.append(dict(zip(COLS, [tag, role, proj, 'MISSING', 'UNKNOWN', '-', 'UNKNOWN',
                                       'UNKNOWN', 'UNKNOWN', '-', '-', 'UNKNOWN', 'UNKNOWN',
                                       'UNKNOWN', 'UNKNOWN', 'UNKNOWN', f'{rep_gb[proj]:.2f}',
                                       'rep-not-on-disk'])))
            continue
        for pname in sorted(progs):
            if pname.endswith('.0'):
                continue          # failed duplicate import; scan_patterns.java skips these too
            sel = 'yes' if (glob == '-' or glob in pname) else 'no'
            rec = progs[pname]
            gp = to_win(rec['exe_path'])
            acq, installdir = classify_acquisition(gp)
            note = []
            appid = lib_today = gb = 'UNKNOWN'
            if acq == 'STEAM' and installdir:
                hit = installed.get(installdir.lower())
                if hit:
                    appid, lib_today, gb = hit[0], hit[1], f'{hit[2]}'
                else:
                    wanted.add(installdir)
                    lib_today, gb = 'NOT-INSTALLED', '-'
            elif acq != 'STEAM':
                appid = installdir = lib_today = gb = '-'
            # binary today
            state, today, pdb = 'GONE', '-', 'UNKNOWN'
            pdb_path = None
            probe = gp
            if gp and not os.path.isfile(gp) and acq == 'STEAM' and installdir and lib_today not in ('UNKNOWN', 'NOT-INSTALLED', '-'):
                # same installdir, different library root -> RELOCATED
                tail = gp.split('\\steamapps\\common\\', 1)[1]
                alt = os.path.join(lib_today, 'steamapps', 'common', tail)
                if os.path.isfile(alt):
                    probe, note = alt, note + ['relocated-library']
            sha = size_bytes = None
            if probe and os.path.isfile(probe):
                today = probe
                pdb_path = os.path.splitext(probe)[0] + '.pdb'
                pdb = 'YES' if os.path.isfile(pdb_path) else 'NO'
                size_bytes = os.path.getsize(probe)
                if a.no_hash:
                    state = 'PRESENT'
                else:
                    m, sha = hashes(probe)
                    state = 'MATCH' if m == rec['exe_md5'] else 'DRIFTED'
            # Search EVERY row, including STEAM and including GONE ones.
            # "A Steam row's second copy is the store" is false whenever the store has moved on
            # and the live file no longer matches the md5 Ghidra imported. And skipping GONE rows
            # would skip the search precisely when a surviving copy matters most.
            # size_prefilter ONLY when today's file IS the corpus build. On DRIFTED/GONE it would
            # size candidates against the wrong build and hide the surviving copy — see the
            # warning in find_duplicate_copies.
            dupes = ([] if a.no_hash
                     else find_duplicate_copies(today, rec['exe_md5'], name_hint=gp,
                                                size_prefilter=(state == 'MATCH')))
            entries.setdefault(tag, []).append({
                'tag': tag, 'role': role, 'project': proj, 'program': pname, 'selected': sel,
                'acq': acq, 'appid': appid, 'installdir': installdir, 'lib': lib_today,
                'state': state, 'today': today, 'ghidra_path': gp, 'md5': rec['exe_md5'],
                'sha256': sha, 'size': size_bytes, 'pdb': pdb, 'pdb_path': pdb_path,
                'pdb_size': (os.path.getsize(pdb_path) if pdb == 'YES' else None),
                'dupes': dupes, 'glob': glob,
                'buildid': (installed.get((installdir or '').lower()) or ('',))[-1]
                            if acq == 'STEAM' and installdir else None,
            })
            out.append(dict(zip(COLS, [
                tag, role, proj, 'YES' if rep_exists else 'MISSING', pname, sel, acq, appid,
                installdir or '-', lib_today, gb, state, today, gp or 'UNKNOWN',
                rec['exe_md5'] or 'UNKNOWN', pdb, f'{rep_gb[proj]:.2f}',
                ';'.join(note) or '-'])))

    # backfill app ids for titles that are no longer installed
    ids = appinfo_lookup(root, sorted(wanted))
    for r in out:
        if r['STEAM_LIB_TODAY'] == 'NOT-INSTALLED':
            r['STEAM_APPID'] = ids.get(r['STEAM_INSTALLDIR'], 'UNKNOWN')
    for progs in entries.values():
        for e in progs:
            if e['lib'] == 'NOT-INSTALLED':
                e['appid'] = ids.get(e['installdir'], None)

    write_json(entries, os.path.splitext(a.out)[0] + '.json')

    with open(a.out, 'w', encoding='utf-8', newline='\n') as f:
        f.write('# Generated by tools/ghidra/build_corpus_manifest.py — DO NOT HAND-EDIT.\n')
        f.write('# Grain: one row per (sweep TAG, Ghidra program). TAG is the join key to sweep.sh.\n')
        f.write('# Verify coverage with: py tools/ghidra/preflight.py   (drift = exit 2/3)\n')
        f.write('\t'.join(COLS) + '\n')
        for r in out:
            f.write('\t'.join(str(r[c]) for c in COLS) + '\n')
    print(f'{len(out)} rows over {len(rows)} tags -> {a.out}')

if __name__ == '__main__':
    main()
