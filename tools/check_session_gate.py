"""P6 detector -- an address from a PREVIOUS game launch must not reach the running game.

    py tools/check_session_gate.py [--list] [--selftest]

⛔ STATUS: A FINDING-PHASE DETECTOR, DELIBERATELY NOT REGISTERED IN check_all.py. It is red by design
today: Class Pivot's four row handoffs are ungated, which is `[W1-PIVOT-SESSION]`. The blank sweep
is in its record-don't-fix phase, so they are not repaired here; the detector is registered in the
SAME commit that gates them, during the fix pass.

WHAT IT REFUSES. In a ViewModel class that reads STORED snapshots (it references `SnapshotMeta` /
`ISnapshotStore`, directly or through a helper type), a command that hands an ADDRESS to the live game -- `NavigateToInstance` /
`LocateInGWorld` / `LocateInGameEngine` with an address payload, or a clipboard copy of one -- where
nothing that enables it depends on the game session. A snapshot row's address means something only in
the launch that captured it (`EngineState.GameSessionId` = PeHash + ProcessCreationTime). Handed to a
later launch, Live Walker walks an arbitrary address and renders whatever is there as a UObject, and
Copy Address gives CE a meaningless number.

THE PREDICATE. A handoff command is SAFE when its `[RelayCommand(CanExecute = X)]` reaches the session,
or EVERY element that binds it has `IsEnabled` / `IsVisible` bound to a property whose expression
reaches the session (`GameSessionId`, `_currentSessionId`), directly or through other properties.
⚠ Snapshot Group and SPC Group gate ONLY in XAML -- their RelayCommands carry no CanExecute -- so
reading the ViewModel alone would call two gated panels ungated. That is why this reads both.

Legitimate population EMPTY: a CLASS-NAME payload (Detect Player Stats' `LocateInGWorld(row.ClassName)`)
means the same thing in every launch and is not an address, so it is not counted; an address handoff
with no session gate has no legitimate reason to exist. No baseline, no allowlist.

⛔ ATTRIBUTION IS BY CLASS BODY, NOT BY FILE -- and the first run proved why. Its first draft mapped
every class name declared in a file to the WHOLE file, so the helper and row classes declared beside
each ViewModel (`PivotFieldPick`, `DiscoverSnapshotPick`, `NoiseRowVm`, `SpcGroupCellVm`,
`SpcSnapshotPick`) each re-reported their neighbour's commands, judged against the wrong panel's
`IsEnabled`: the run said FIFTEEN ungated handoffs when the true number is four. Its selftest keyed on
(method, status) and so could not see the duplication either; it now keys on (class, method, status)
and carries a helper-class-beside-the-VM case. The same draft missed any command whose attribute was
`[RelayCommand(CanExecute = nameof(X))]` -- the nested `)` defeated a `[^)]*` -- which the selftest
caught.

⛔ AND AN ADDRESS COUNTS ONLY WHEN IT COMES FROM A ROW. Once readers were found transitively, the
Pointer panel came into scope -- it reads snapshots for unrelated reasons -- and its nine copy commands
were reported as ungated handoffs. They copy the LIVE global pointers (GObjects, GNames, GWorld ...),
which belong to the current launch by definition. A payload now counts only when it is rooted at the
command's own parameter (a CommandParameter row) or at a Selected* row. That is the third bug in this
detector, and all three were found the same way: by checking the run against the hand-made map row
by row, never by its total -- two of the three left the headline number looking right.

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
RELAY = re.compile(r'\[RelayCommand(\((?:[^()]|\([^()]*\))*\))?\]\s*(?:\[[^\]]*\]\s*)*'
                   r'(?:(?:private|public|internal|protected)\s+)?(?:async\s+)?'
                   r'[\w<>?\[\],.]+(?:<[^>]*>)?\s+(\w+)\s*\(')
BOOLPROP = re.compile(r'\b(?:public|private|internal|protected)\s+(?:static\s+)?bool\s+(\w+)\s*'
                      r'(?:\(\s*\))?\s*=>\s*(.*?);', re.S)
CLASS = re.compile(r'\b(?:class|record|struct)\s+(\w+)[^{;]*\{')
SESSION = re.compile(r'GameSessionId|_currentSessionId')
ELEM = re.compile(r'<([A-Za-z][\w.:]*)\b([^<>]*?)/?>', re.S)


def strip_code(src: str) -> str:
    """Blank comments, and blank {}() INSIDE string literals, keeping every newline and every
    identifier -- so `$"{addr:X}"` cannot unbalance a brace or paren matcher, while a
    `CanExecute = "CanX"` string form stays readable."""
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
            out.append('"' + re.sub(r'[{}()]', ' ', src[i + 1:j]) + '"')
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


def class_bodies(src: str) -> list:
    """-> [(name, own_body)] with every NESTED class body blanked out of its container's text."""
    spans = []
    for m in CLASS.finditer(src):
        op = m.end() - 1
        cl = match_close(src, op, '{', '}')
        if cl > 0:
            spans.append((m.group(1), op, cl))
    out = []
    for name, op, cl in spans:
        body = list(src[op + 1:cl])
        for _, o2, c2 in spans:
            if o2 > op and c2 < cl:
                for k in range(o2 - op - 1, c2 - op):
                    if 0 <= k < len(body) and body[k] != '\n':
                        body[k] = ' '
        out.append((name, ''.join(body)))
    return out


def snapshot_classes(vm_files: dict) -> dict:
    """{class: its own body text, partials merged} for classes that read stored snapshots --
    directly (SnapshotMeta / ISnapshotStore) or THROUGH a class that does.

    ⚠ The transitive step is not optional. The first class-body version required `SnapshotMeta` in a
    class's OWN body, and SPC dropped out of scope entirely: `SpcQueryViewModel` reaches snapshots
    only through its helper `SpcSnapshotPick.Meta`. SPC happens to be gated, so the headline number
    still looked right -- but an ungated SPC would have vanished from the report without a trace.
    """
    merged = {}
    for raw in vm_files.values():
        for name, body in class_bodies(strip_code(raw)):
            merged[name] = merged.get(name, '') + '\n' + body
    readers = {c for c, t in merged.items() if re.search(r'\b(?:SnapshotMeta|ISnapshotStore)\b', t)}
    grew = True
    while grew:
        grew = False
        for c, t in merged.items():
            if c not in readers and any(re.search(r'\b%s\b' % re.escape(r), t) for r in readers):
                readers.add(c)
                grew = True
    return {c: merged[c] for c in sorted(readers)}


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
            body = text[j:text.find(';', j)]
        else:
            body = text[j:match_close(text, j, '{', '}')]
        params = []
        for chunk in text[op + 1:cl].split(','):
            pm = re.search(r'(\w+)\s*(?:=\s*.+)?$', chunk.strip())
            if pm:
                params.append(pm.group(1))
        # An address counts only when it comes from a ROW: the command's own parameter (a
        # CommandParameter) or a Selected* row. See the header for the nine live-pointer copies
        # this rule exists to exclude.
        roots = [re.escape(x) for x in params] + [r'Selected\w*']
        rooted = re.compile(r'\b(?:%s)\s*\??\.\s*\w*addr' % '|'.join(roots), re.I)
        whats = [h.group(1) for h in HANDOFF.finditer(body) if rooted.search(h.group(2))]
        if 'CopyToClipboardAsync' in body and rooted.search(body):
            whats.append('CopyToClipboard')
        if not whats:
            continue
        ce = re.search(r'CanExecute\s*=\s*(?:nameof\s*\(\s*)?"?(\w+)', m.group(1) or '')
        out.append((method, re.sub(r'Async$', '', method) + 'Command', ce.group(1) if ce else None,
                    '+'.join(sorted(set(whats)))))
    return out


def analyse(vm_files: dict, views: dict) -> list:
    """-> [(class, method, status, detail)] with status in SAFE / UNGATED / UNREACHED."""
    rows = []
    for cls, text in snapshot_classes(vm_files).items():
        gated = gated_props(text)
        panel = [(p, v) for p, v in views.items()
                 if re.search(r'vm:%s\b' % re.escape(cls), v)]
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
                            how = '%s={!...} (a negation cannot express a session match)' % attr
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


# Class Pivot's pre-fix shape, VERBATIM in the parts that decide the verdict, WITH a helper class
# declared beside it the way the real file does -- the case the first draft got wrong.
PIVOT_VM = '''
public partial class ClassPivotViewModel : ObservableObject
{
    private SnapshotMeta? _selectedSnapshot;
    public event Action<string>? NavigateToInstance;
    public event Action<string>? LocateInGWorld;
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
        var hex = $"{row.ObjAddr:X}";
        await _platform.CopyToClipboardAsync(hex);
    }
}
public sealed class PivotFieldPick : ObservableObject
{
    public SnapshotMeta? Owner { get; set; }
    public string Name { get; set; } = "";
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
  <DataTemplate x:DataType="vm:PivotFieldPick"><TextBlock Text="{Binding Name}"/></DataTemplate>
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

LIVE_VM = """
public partial class PointerPanelViewModel : ObservableObject
{
    private readonly ISnapshotStore _store;
    public string GObjectsAddr { get; set; } = "";
    [RelayCommand]
    private async Task CopyGObjectsAsync() { await _platform.CopyToClipboardAsync(GObjectsAddr); }
}
"""


def selftest() -> bool:
    C = 'ClassPivotViewModel'
    S = 'SpcQueryViewModel'
    cases = [
        ('Class Pivot pre-fix: its three address handoffs are UNGATED -- and the helper class '
         'declared beside it inherits NONE of them',
         {'vm.cs': PIVOT_VM}, {'v.axaml': PIVOT_VIEW},
         {(C, 'OpenInLiveWalker', 'UNGATED'), (C, 'LocateResultInGWorld', 'UNGATED'),
          (C, 'CopyAddressAsync', 'UNGATED')}),
        ('SPC Group: gated in XAML only, through a property chain, and via CanExecute = nameof(...) '
         '-- all SAFE',
         {'vm.cs': GROUP_VM}, {'v.axaml': GROUP_VIEW},
         {(S, 'OpenGroupInLiveWalker', 'SAFE'), (S, 'LocateGroupSlotInGWorld', 'SAFE'),
          (S, 'OpenViaCanExecute', 'SAFE')}),
        ('a class-name payload is not an address and is not counted',
         {'vm.cs': STATS_VM}, {}, set()),
        ('a VM that reaches snapshots ONLY through a helper type is still in scope',
         {'vm.cs': GROUP_VM.replace('    private SnapshotMeta? _m;\n',
                                    '    public ObservableCollection<SpcSnapshotPick> SnapshotPicks { get; } = new();\n')
                   + '\npublic sealed class SpcSnapshotPick { public SnapshotMeta Meta { get; } = new(); }\n'},
         {'v.axaml': GROUP_VIEW},
         {(S, 'OpenGroupInLiveWalker', 'SAFE'), (S, 'LocateGroupSlotInGWorld', 'SAFE'),
          (S, 'OpenViaCanExecute', 'SAFE')}),
        ('a LIVE global pointer copied by a snapshot-reading class is not a snapshot address',
         {'vm.cs': LIVE_VM}, {}, set()),
    ]
    ok_all = True
    for what, vms, views, want in cases:
        got = {(c, m, s) for c, m, s, _ in analyse(vms, views)}
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
