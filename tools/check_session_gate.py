"""P6 detector -- an address from a PREVIOUS game launch must not reach the running game.

    py tools/check_session_gate.py [--list] [--selftest]

⛔ STATUS: A FINDING-PHASE DETECTOR, DELIBERATELY NOT REGISTERED IN check_all.py. It is red by design
today: Class Pivot's four row handoffs are ungated, which is `[W1-PIVOT-SESSION]`. The blank sweep
is in its record-don't-fix phase, so they are not repaired here; the detector is registered in the
SAME commit that gates them, during the fix pass.

WHAT IT REFUSES. In a ViewModel that reads STORED snapshots (it references `SnapshotMeta`), a command
that hands an ADDRESS to the live game -- `NavigateToInstance` / `LocateInGWorld` /
`LocateInGameEngine` with an address payload, or a clipboard copy of one -- where nothing that
enables it depends on the game session. A snapshot row's address means something only in the launch
that captured it (`EngineState.GameSessionId` = PeHash + ProcessCreationTime). Handed to a later
launch, Live Walker walks an arbitrary address and renders whatever is there as a UObject, and Copy
Address gives CE a meaningless number.

THE PREDICATE. A handoff command is SAFE when its `[RelayCommand(CanExecute = X)]` reaches the session,
or EVERY element that binds it has `IsEnabled` / `IsVisible` bound to a property whose expression
reaches the session (`GameSessionId`, `_currentSessionId`), directly or through other properties.
⚠ Snapshot Group and SPC Group gate ONLY in XAML -- their RelayCommands carry no CanExecute -- so
reading the ViewModel alone would call two gated panels ungated. That is why this reads both.

Legitimate population EMPTY: a CLASS-NAME payload (Detect Player Stats' `LocateInGWorld(row.ClassName)`)
means the same thing in every launch and is not an address, so it is not counted; an address handoff
with no session gate has no legitimate reason to exist. No baseline, no allowlist.

MEASURED POPULATION (2026-09-10): Snapshot Diff, Snapshot Group, SPC single and SPC Group -- gated;
Class Pivot -- 4 ungated handoffs (recorded); Detect Player Stats -- class-name payload, not counted.
Live Walker bookmarks are the other persisted-address family and are OUT of this predicate's scope:
they re-resolve through the GWorld spine and check the saved class name before calling a restore
"loaded", which is an identity check rather than a session gate.
"""
from __future__ import annotations

import argparse
import pathlib
import re
import sys

REPO = pathlib.Path(__file__).resolve().parents[1]
VMS = REPO / 'ui' / 'UE5DumpUI' / 'ViewModels'
VIEWS = REPO / 'ui' / 'UE5DumpUI' / 'Views'

HANDOFF = re.compile(r'\b(NavigateToInstance|LocateInGWorld|LocateInGameEngine)\?\.Invoke\(([^)]*)\)')
RELAY = re.compile(r'\[RelayCommand(?:\(([^)]*)\))?\]\s*(?:\[[^\]]*\]\s*)*'
                   r'(?:(?:private|public|internal|protected)\s+)?(?:async\s+)?'
                   r'[\w<>?\[\],.]+(?:<[^>]*>)?\s+(\w+)\s*\(')
BOOLPROP = re.compile(r'\b(?:public|private|internal|protected)\s+(?:static\s+)?bool\s+(\w+)\s*'
                      r'(?:\(\s*\))?\s*=>\s*(.*?);', re.S)
SESSION = re.compile(r'GameSessionId|_currentSessionId')
ELEM = re.compile(r'<([A-Za-z][\w.:]*)\b([^<>]*?)/?>', re.S)


def strip_comments(src: str) -> str:
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


def match_close(src: str, pos: int, o: str, c: str) -> int:
    depth = 0
    for i in range(pos, len(src)):
        if src[i] == o:
            depth += 1
        elif src[i] == c:
            depth -= 1
            if depth == 0:
                return i
    return -1


def snapshot_classes(vm_files: dict) -> dict:
    """{class: merged text of ALL its partial files}, for classes whose files read SnapshotMeta."""
    declared, readers = {}, set()
    for path, raw in vm_files.items():
        src = strip_comments(raw)
        names = re.findall(r'\bclass\s+(\w+)', src)
        for n in names:
            declared.setdefault(n, []).append(src)
        if 'SnapshotMeta' in src:
            readers.update(names)
    return {c: '\n'.join(declared[c]) for c in sorted(readers)}


def gated_props(text: str):
    props = {m.group(1): m.group(2) for m in BOOLPROP.finditer(text)}
    memo = {}

    def gated(p: str, seen=None) -> bool:
        if p in memo:
            return memo[p]
        seen = set() if seen is None else seen
        if p in seen or p not in props:
            return False
        seen.add(p)
        e = props[p]
        ok = bool(SESSION.search(e)) or any(
            q in props and gated(q, seen) for q in re.findall(r'\b([A-Z_]\w*)\b', e))
        memo[p] = ok
        return ok
    return gated


def handoff_commands(text: str) -> list:
    """-> [(method, command_name, canexecute_or_None, what)] for address-handoff commands."""
    out = []
    for m in RELAY.finditer(text):
        method = m.group(2)
        op = m.end() - 1
        cl = match_close(text, op, '(', ')')
        if cl < 0:
            continue
        j = cl + 1
        while j < len(text) and text[j] not in '{;=':
            j += 1
        if j >= len(text) or text[j] == ';':
            continue
        if text.startswith('=>', j):
            k = text.find(';', j)
            body = text[j:k]
        else:
            k = match_close(text, j, '{', '}')
            body = text[j:k]
        whats = [h.group(1) for h in HANDOFF.finditer(body) if re.search(r'addr', h.group(2), re.I)]
        if 'CopyToClipboardAsync' in body and re.search(r'addr', method + body, re.I):
            whats.append('CopyToClipboard')
        if not whats:
            continue
        ce = re.search(r'CanExecute\s*=\s*(?:nameof\s*\(\s*)?(\w+)', m.group(1) or '')
        out.append((method, re.sub(r'Async$', '', method) + 'Command', ce.group(1) if ce else None,
                    '+'.join(sorted(set(whats)))))
    return out


def analyse(vm_files: dict, views: dict) -> list:
    """-> [(class, method, status, detail)] with status in SAFE / UNGATED / UNREACHED."""
    rows = []
    for cls, text in snapshot_classes(vm_files).items():
        gated = gated_props(text)
        panel = [(p, v) for p, v in views.items()
                 if re.search(r'x:DataType="vm:%s"' % cls, v) or '(vm:%s)' % cls in v]
        for method, cmd, ce, what in handoff_commands(text):
            elems = []
            for path, v in panel:
                for e in ELEM.finditer(v):
                    attrs = e.group(2)
                    if not re.search(r'Command="[^"]*\b%s\b' % re.escape(cmd), attrs):
                        continue
                    ok, how = False, 'no IsEnabled/IsVisible'
                    for attr in ('IsEnabled', 'IsVisible'):
                        b = re.search(r'%s="\{Binding\s+([^}"]*)\}"' % attr, attrs)
                        if not b:
                            continue
                        path_ = b.group(1).strip()
                        if path_.startswith('!'):
                            how = '%s={!…} (a negation cannot express a session match)' % attr
                            continue
                        leaf = re.findall(r'(\w+)', path_)[-1]
                        how = '%s={%s}' % (attr, leaf)
                        if gated(leaf):
                            ok = True
                            break
                    elems.append((path, ok, how))
            if ce and gated(ce):
                rows.append((cls, method, 'SAFE', 'CanExecute=%s reaches the session' % ce))
            elif not elems:
                rows.append((cls, method, 'UNREACHED', 'no element binds %s' % cmd))
            elif all(ok for _, ok, _ in elems):
                rows.append((cls, method, 'SAFE', '; '.join(h for _, _, h in elems)))
            else:
                rows.append((cls, method, 'UNGATED', '%s -- %s' % (what, '; '.join(
                    h for _, ok, h in elems if not ok))))
    return rows


def load_tree():
    vms = {str(p.relative_to(REPO)): p.read_text(encoding='utf-8', errors='replace')
           for p in VMS.glob('*.cs')}
    views = {str(p.relative_to(REPO)): p.read_text(encoding='utf-8', errors='replace')
             for p in VIEWS.glob('*.axaml')}
    return vms, views


# Class Pivot's pre-fix shape, VERBATIM in the parts that decide the verdict, so the selftest stays
# valid after the fix pass changes the real files.
PIVOT_VM = '''
public partial class ClassPivotViewModel : ObservableObject
{
    private SnapshotMeta? _selectedSnapshot;
    public event Action<string>? NavigateToInstance;
    public event Action<string>? LocateInGWorld;
    public event Action<string>? LocateInGameEngine;
    public bool CanLocateResult => SelectedResult != null;
    public bool CanLocateResultInGWorld => SelectedResult != null;
    [RelayCommand]
    private void OpenInLiveWalker(PivotResultRow? row)
    {
        if (row == null) return;
        NavigateToInstance?.Invoke(row.ObjAddr);
    }
    [RelayCommand]
    private void LocateResultInGWorld(PivotResultRow? row)
    {
        if (row == null) return;
        LocateInGWorld?.Invoke(row.ObjAddr);
    }
    [RelayCommand]
    private async Task CopyAddressAsync(PivotResultRow? row)
    {
        var hex = row.ObjAddr;
        await _platform.CopyToClipboardAsync(hex);
    }
}
'''
PIVOT_VIEW = '''
<UserControl x:DataType="vm:ClassPivotViewModel">
  <Button Content="x" Command="{Binding OpenInLiveWalkerCommand}"
          CommandParameter="{Binding SelectedResult}" Padding="8,4"/>
  <Button Content="x" Command="{Binding CopyAddressCommand}"
          CommandParameter="{Binding SelectedResult}" Padding="8,4"/>
  <Button Content="x" Command="{Binding LocateResultInGWorldCommand}"
          CommandParameter="{Binding SelectedResult}" IsEnabled="{Binding CanLocateResultInGWorld}"/>
</UserControl>
'''
GROUP_VM = '''
public partial class SpcQueryViewModel : ObservableObject
{
    private SnapshotMeta? _m;
    private string _currentSessionId = "";
    public bool CanUseResultRowActions =>
        !string.IsNullOrEmpty(_currentSessionId) && NewestSelectedSessionId == _currentSessionId;
    public bool CanLocateResultRowInGWorld => CanUseResultRowActions;
    [RelayCommand]
    private void OpenGroupInLiveWalker(Cand? cand) { NavigateToInstance?.Invoke(cand.InstanceAddr); }
    [RelayCommand]
    private void LocateGroupSlotInGWorld(Slot? slot) { LocateInGWorld?.Invoke(slot.InstanceAddr, slot.FieldOffset, slot.FieldName); }
    [RelayCommand(CanExecute = nameof(CanUseResultRowActions))]
    private void OpenViaCanExecute(Cand? cand) { NavigateToInstance?.Invoke(cand.InstanceAddr); }
}
'''
GROUP_VIEW = '''
<UserControl x:DataType="vm:SpcQueryViewModel">
  <Button Command="{Binding $parent[UserControl].((vm:SpcQueryViewModel)DataContext).OpenGroupInLiveWalkerCommand}"
          IsEnabled="{Binding $parent[UserControl].((vm:SpcQueryViewModel)DataContext).CanUseResultRowActions}"/>
  <Button Command="{Binding $parent[UserControl].((vm:SpcQueryViewModel)DataContext).LocateGroupSlotInGWorldCommand}"
          IsEnabled="{Binding $parent[UserControl].((vm:SpcQueryViewModel)DataContext).CanLocateResultRowInGWorld}"/>
  <Button Command="{Binding OpenViaCanExecuteCommand}"/>
</UserControl>
'''
STATS_VM = '''
public partial class DetectStatsViewModel : ObservableObject
{
    private SnapshotMeta? _m;
    [RelayCommand]
    private void LocateClass(StatRow? row) { LocateInGWorld?.Invoke(row.ClassName); }
}
'''


def selftest() -> bool:
    cases = [
        ('Class Pivot pre-fix: its three address handoffs are UNGATED',
         {'vm.cs': PIVOT_VM}, {'v.axaml': PIVOT_VIEW},
         {('OpenInLiveWalker', 'UNGATED'), ('LocateResultInGWorld', 'UNGATED'),
          ('CopyAddressAsync', 'UNGATED')}),
        ('SPC Group: gated in XAML only, through a property chain, and via CanExecute -- all SAFE',
         {'vm.cs': GROUP_VM}, {'v.axaml': GROUP_VIEW},
         {('OpenGroupInLiveWalker', 'SAFE'), ('LocateGroupSlotInGWorld', 'SAFE'),
          ('OpenViaCanExecute', 'SAFE')}),
        ('a class-name payload is not an address and is not counted',
         {'vm.cs': STATS_VM}, {}, set()),
    ]
    ok_all = True
    for what, vms, views, want in cases:
        got = {(m, s) for _, m, s, _ in analyse(vms, views)}
        ok = got == want
        ok_all &= ok
        print('  %-4s %s' % ('PASS' if ok else 'FAIL', what))
        if not ok:
            print('         want %s\n         got  %s' % (sorted(want), sorted(got)))
    return ok_all


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument('--list', action='store_true')
    ap.add_argument('--selftest', action='store_true')
    a = ap.parse_args()
    if a.selftest:
        ok = selftest()
        print('selftest: %s' % ('PASS' if ok else 'FAIL'))
        return 0 if ok else 2
    rows = analyse(*load_tree())
    bad = [r for r in rows if r[2] == 'UNGATED']
    for cls, method, status, detail in rows:
        if a.list or status != 'SAFE':
            print('  %-9s %s.%s  -- %s' % (status, cls, method, detail))
    if bad:
        print('check_session_gate: %d snapshot-address handoff%s reach the live game with no session'
              ' gate' % (len(bad), '' if len(bad) == 1 else 's'))
        return 1
    print('check_session_gate: OK -- every snapshot-address handoff is gated on the game session')
    return 0


if __name__ == '__main__':
    sys.stdout.reconfigure(encoding='utf-8')
    sys.exit(main())
