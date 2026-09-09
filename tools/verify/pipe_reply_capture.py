r"""`[A4-ASSESS-2026-09-09]` phase 3 — capture what each command ACTUALLY replies, on a live game.

    py tools/verify/pipe_reply_capture.py                 # capture, then diff against the tree
    py tools/verify/pipe_reply_capture.py --out out/reply_shapes.json

⛔ WHY A LIVE CAPTURE AND NOT ANOTHER REGEX. Phase 2 answered the reply-key question statically and
answered it well in one direction, but two things a static pass CANNOT do:

  1. **Delegating handlers are invisible.** `pipe_command_contract.py` brace-matches the dispatch
     block, so a handler that builds its reply in a helper looks reply-less. 7 commands are in that
     state and the tool says so rather than reporting them as empty.
  2. **The reverse direction had no usable instrument.** "Keys the UI reads that this command never
     publishes" needs the real key set. Grepping a +/-line window over a 3,500-line DumpService
     produced 93 "sites" that were an artifact of the window, and they were discarded, not filed.

A live reply is ground truth for both. This sends each command, keeps the exact key set that came
back, and diffs it against the static extraction and against what the C# side names.

⛔⛔ SAFETY — THIS RIG IS READ-ONLY BY CONSTRUCTION, AND THE SKIP LIST IS PART OF THE RESULT.
Every command that writes game memory, arms a hold, moves the pawn, starts a worker, or changes a
persisted setting is in SKIP and is NEVER sent. That is not timidity: a capture rig that toggles Fly
mid-run would poison every later reply in the same session, and this repo's rule is one injected
game at a time. The skipped set is REPORTED, so "we measured 70 of 99" is never mistaken for
"we measured the wire".

⚠ WHAT A CAPTURED KEY SET IS NOT. It is what THIS build, on THIS fixture, in THIS engine state
replied. A handler with a branch that did not run publishes fewer keys than its contract allows --
so a key present statically and absent here is a BRANCH, not a defect. The diff prints both
directions and labels that one accordingly rather than calling it a miss.
"""
from __future__ import annotations

import argparse
import json
import os
import pathlib
import re
import subprocess
import sys

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
from pipe_client import PipeClient          # noqa: E402

REPO = pathlib.Path(__file__).resolve().parents[2]

# ⛔ NEVER SENT. Each line says WHY, because a skip with no reason becomes a permanent hole.
SKIP = {
    'fly_set':                  'moves the pawn / starts a worker',
    'teleport_relative':        'moves the pawn',
    'teleport_recall_marker':   'moves the pawn',
    'teleport_recall_last':     'moves the pawn',
    'teleport_to_cursor':       'moves the pawn',
    'teleport_save_marker':     'writes a marker slot',
    'teleport_clear_marker':    'clears a marker slot',
    'force_field':              'arms a re-asserting hold on game memory',
    'reset_field':              'mutates a hold',
    'reset_all_fields':         'mutates every hold',
    'set_god_mode':             'writes bCanBeDamaged',
    'set_movement_multiplier':  'writes CMC knobs',
    'reset_movement':           'writes CMC knobs',
    'set_gravity_direction':    'writes gravity',
    'reset_gravity_direction':  'writes gravity',
    'set_time_dilation':        'writes world/pawn time',
    'reset_time_dilation':      'writes world/pawn time',
    'seethrough_set':           'hides actors + starts a worker',
    'set_debug_camera':         'detaches the view',
    'set_foreground_lock':      'changes window/foreground behaviour',
    'set_mouse_cursor':         'writes bShowMouseCursor + input mode',
    'write_mem':                'writes arbitrary game memory',
    'invoke_function':          'calls a UFunction in the game',
    'set_ue_version_override':  'persists a per-game setting',
    'set_invoke_timeout':       'persists a per-game setting',
    'set_packed_consts':        'persists tuning',
    'rescan':                   'restarts the offset scan',
    'apply_rescan':             'swaps the live offset table',
    'trigger_scan':             'starts a scan worker',
    'begin_snapshot':           'starts a long capture that writes a DB',
    'snapshot_chunk':           'only meaningful mid-snapshot',
    'begin_value_scan':         'starts scan state the later reads depend on',
    'refine_value_scan':        'mutates scan state',
    'end_value_scan':           'mutates scan state',
    'begin_group_scan':         'starts scan state',
    'refine_group_scan':        'mutates scan state',
    'end_group_scan':           'mutates scan state',
    'pe_profile_start':         'installs/starts the ProcessEvent profiler',
    'pe_profile_stop':          'stops the profiler',
    'watch':                    'registers a watch that fires later',
    'unwatch':                  'mutates watch state',
    'reset_diagnostics':        'clears counters other rows read',
    'detect_noise_classes':     'long, and it writes the hint cache',
}

# Commands that need an argument to reply usefully. Filled in at run time from live data.
def build_args(c: PipeClient) -> dict:
    a: dict = {}
    objs = c.request('get_object_list', offset=0, limit=40).get('objects', []) or []
    addr = next((o.get('addr') for o in objs if o.get('addr')), None)
    cls = next((o.get('class') for o in objs if o.get('class')), None)
    # ⛔ A REPLY IS ONLY AS RICH AS THE OBJECT YOU WALK. The first pool object is engine
    # boilerplate, so `walk_instance` never emitted a delegate/map/set/enum field and 134
    # C# keys looked like "reads something that never arrives" -- all of them just branches
    # this walk did not take. Pull instances that actually DECLARE those types so every
    # field-type handler runs at least once. The rig's own coverage is the measurement.
    rich = []
    for fam in ('DelegateProperty', 'MulticastInlineDelegateProperty', 'MapProperty',
                'SetProperty', 'EnumProperty', 'ByteProperty', 'StructProperty',
                'ArrayProperty', 'ObjectProperty', 'SoftObjectProperty', 'TextProperty',
                'OptionalProperty', 'InterfaceProperty', 'BoolProperty'):
        try:
            res = c.request('search_properties', types=[fam], game_only=False,
                            limit=200, deep=True).get('results', []) or []
        except Exception:                                  # noqa: BLE001
            continue
        seen = set()
        for row in res:
            cn = row.get('class_name')
            if not cn or cn in seen:
                continue
            seen.add(cn)
            try:
                fi = c.request('find_instances', class_name=cn, max_results=2)
            except Exception:                              # noqa: BLE001
                continue
            for inst in (fi.get('instances') or []):
                if inst.get('addr'):
                    rich.append(inst['addr'])
                    break
            if len(seen) >= 4:
                break
    a['_rich_addrs'] = rich[:40]

    if addr:
        for k in ('get_object', 'walk_instance', 'get_related_objects', 'find_by_address',
                  'get_ce_pointer_info', 'read_array_elements', 'walk_function_props',
                  'find_refs_to_uobject', 'find_path_from_gworld', 'walk_functions',
                  'walk_class', 'find_property_xrefs', 'walk_datatable_rows',
                  'get_function_code_addr'):
            a[k] = {'addr': addr}
        a['walk_instance'] = {'addr': addr, 'array_limit': 2, 'preview_limit': 1}
        a['walk_instance_batch'] = {'items': [{'addr': addr}], 'array_limit': 2}
        a['walk_class_batch'] = {'addrs': [addr]}
        a['read_mem'] = {'addr': addr, 'size': 16}
    if cls:
        a['find_instances'] = {'class_name': cls, 'max_results': 5}
        a['find_functions_by_class'] = {'class_name': cls}
        a['find_object'] = {'name': cls}
        a['search_objects'] = {'query': cls[:6], 'limit': 5}
    a.setdefault('search_properties', {'types': ['BoolProperty'], 'limit': 5})
    a.setdefault('search_properties_batch', {'queries': [{'types': ['BoolProperty'], 'limit': 3}]})
    a.setdefault('list_classes', {'limit': 10})
    a.setdefault('list_enums', {'limit': 10})
    a.setdefault('list_all_functions', {'limit': 10})
    a.setdefault('get_object_list', {'offset': 0, 'limit': 10})
    a.setdefault('walk_world', {'max_actors': 5})
    a.setdefault('query_candidates', {'limit': 5})
    a.setdefault('query_group_candidates', {'limit': 5})
    a.setdefault('query_group_slot_leaves', {'limit': 5})
    return a


def collect_keys(node, depth: int = 0, acc: set | None = None) -> set:
    """Every key ANYWHERE in the reply, not only the top level.

    ⛔ THE TOP LEVEL IS THE WRONG UNIT, and a first run proved it: comparing top-level keys
    against what C# indexes produced 170 "the UI reads something that never arrives" hits,
    and essentially all of them -- `delegate_pad`, `array_dim`, `enum_name`, the whole
    `map_*` / `set_*` family -- are fields of NESTED rows inside `fields[]` / `objects[]`.
    They arrive on every walk. Measuring the envelope and concluding about the contents is
    the same error as phase 2's +/-line window, one layer down.
    """
    if acc is None:
        acc = set()
    if depth > 6:
        return acc
    if isinstance(node, dict):
        for k, v in node.items():
            acc.add(k)
            collect_keys(v, depth + 1, acc)
    elif isinstance(node, list):
        for v in node[:40]:          # a sample is enough: rows are homogeneous
            collect_keys(v, depth + 1, acc)
    return acc


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument('--out', default='out/reply_shapes.json')
    args = ap.parse_args()

    static = {b['cmd']: b for b in json.loads(subprocess.run(
        [sys.executable, str(REPO / 'tools/verify/pipe_command_contract.py'), 'json'],
        capture_output=True, text=True, cwd=str(REPO)).stdout)}
    commands = sorted(static)

    captured, failed = {}, {}
    with PipeClient() as c:
        print('build:', c.assert_build())
        c.ensure_scanned()
        n = c.request('get_object_count').get('count')
        print('objects in pool: %s' % n)
        if not n:
            print('*** the pool is EMPTY -- a process that exists is not a game that booted. '
                  'Nothing measured; relaunch the fixture.')
            return 2

        argmap = build_args(c)
        for cmd in commands:
            if cmd in SKIP:
                continue
            try:
                r = c.request(cmd, **argmap.get(cmd, {}))
                keys = collect_keys(r)
                # Walk the type-rich instances too, unioning their keys in: one walk of one
                # object exercises only the field types that object happens to declare.
                if cmd == 'walk_instance':
                    for ra in argmap.get('_rich_addrs', []):
                        try:
                            keys |= collect_keys(c.request(
                                'walk_instance', addr=ra, array_limit=4, preview_limit=2))
                        except Exception:                  # noqa: BLE001
                            pass
                captured[cmd] = sorted(keys)
            except Exception as ex:                     # noqa: BLE001 - report, never abort
                failed[cmd] = str(ex)[:120]

    print('\nsent %d, captured %d, errored %d, skipped %d (of %d commands)'
          % (len(commands) - len(SKIP), len(captured), len(failed), len(SKIP), len(commands)))
    if failed:
        print('\nerrored (reported, not hidden):')
        for k, v in sorted(failed.items()):
            print('   %-28s %s' % (k, v))

    # ---- diff 1: live vs the static extraction -----------------------------------
    print('\n%s\nLIVE vs STATIC -- validates pipe_command_contract.py and reaches its blind spot\n%s'
          % ('=' * 78, '=' * 78))
    blind = 0
    for cmd in sorted(captured):
        live = set(captured[cmd]) - {'id', 'ok', 'cmd', 'error'}
        st = set(static[cmd]['replies'])
        only_live = sorted(live - st)
        if only_live and not st:
            blind += 1
        if only_live:
            print('%-28s +%d live-only%s' % (cmd, len(only_live),
                                             '  (static saw NONE -- delegating handler)'
                                             if not st else ''))
    print('\n%d command(s) whose whole reply the static pass could not see.' % blind)

    # ---- diff 2: live keys nobody in the tree names -------------------------------
    ui = ''
    for p in subprocess.run(['git', 'ls-files', 'ui/UE5DumpUI/*.cs', 'ui/UE5DumpUI/**/*.cs',
                             'tools/**/*.py', 'tools/*.py', 'scripts/*.lua', 'scripts/*.CT'],
                            capture_output=True, text=True, cwd=str(REPO)).stdout.split('\n'):
        if p.strip():
            try:
                ui += (REPO / p).read_text(encoding='utf-8', errors='replace')
            except OSError:
                pass
    print('\n%s\nLIVE KEYS NO CONSUMER READS\n%s' % ('=' * 78, '=' * 78))

    # ⛔ "IS THE NAME PRESENT" IS THE WRONG PREDICATE, AND IT FAILED HERE WITHIN HOURS.
    # A first version asked whether the key appears anywhere in the tree. `mode_resolved`
    # and `cmc_addr` came back CONSUMED -- and their only occurrence in the whole repo was
    # a docstring line in tools/verify/pipe_wire_parity.py, written earlier the same night,
    # naming them as EXAMPLES of unread keys. The report of the orphan made the orphan
    # disappear: working-lessons 1.w3, a second time, in a second tool.
    # So the predicate is now CONSUMPTION SHAPE, not presence -- a form that could actually
    # read the value. Prose, comments and docstrings no longer count, which is both the fix
    # for the self-reference and a stricter question to begin with.
    def consumed(k: str) -> bool:
        pascal = ''.join(p.capitalize() for p in k.split('_'))
        pats = [
            r'\[\s*"%s"\s*\]' % re.escape(k),          # C#/Python literal index
            r"\[\s*'%s'\s*\]" % re.escape(k),
            r'\.get\(\s*["\']%s["\']' % re.escape(k),  # Python .get("k")
            r'GetProp\w*\(\s*["\']%s["\']' % re.escape(k),
            r'\b%s\s*=' % re.escape(pascal),           # C# model property assignment
            r'\b%s\s*\{\s*get' % re.escape(pascal),    # C# auto-property declaration
        ]
        return any(re.search(p, ui) for p in pats)

    orphans = {}
    for cmd, keys in sorted(captured.items()):
        miss = [k for k in keys if k not in ('id', 'ok', 'cmd', 'error') and not consumed(k)]
        if miss:
            orphans[cmd] = miss
            print('%-28s %s' % (cmd, ' '.join(miss)[:88]))
    print('\n%d command(s) carry a key no consumer reads.' % len(orphans))
    print('⚠ CANDIDATES, not findings -- a build stamp echoed for a human is fine. And the')
    print('  reverse of the old bug still applies: a key read through a variable is invisible.')

    outp = REPO / args.out
    outp.parent.mkdir(parents=True, exist_ok=True)
    outp.write_text(json.dumps(
        {'captured': captured, 'failed': failed, 'skipped': SKIP, 'orphans': orphans},
        ensure_ascii=False, indent=1), encoding='utf-8')
    print('\nwritten %s' % outp)
    return 0


if __name__ == '__main__':
    sys.exit(main())
