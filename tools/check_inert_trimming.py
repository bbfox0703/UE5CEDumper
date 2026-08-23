"""Refuse a TextBlock whose TextTrimming cannot possibly fire and whose tail is unreadable.

    py tools/check_inert_trimming.py [--list]

THE DEFECT, which has now shipped four times:

  [FORCESTATUSCLIP-2026-08-22]  PropertySearchPanel status line
  [V8PREVIEWCLIP-2026-08-23]    Live Walker Value column
  [TYPECOLCLIP-2026-08-23]      Live Walker Type column
  [DUMPHDRCLIP-2026-08-23]      Dump Explorer meta header

A horizontal `StackPanel` measures every child with INFINITE available width and then
hands it its DESIRED width. A `TextBlock` in one is therefore never constrained by the
panel, so `TextTrimming` NEVER FIRES -- the text is hard-cut at the panel edge with no
ellipsis and no way to read the rest. The markup looks correct and reads correctly in
review; only the running app shows the cut, and only at some window widths.

`TextTrimming` in that position is the tell: the author expected the text to be clipped
and asked for an ellipsis. If it also has no tooltip anywhere up its ancestor chain, the
clipped tail is unrecoverable -- and in every case above the TAIL was the load-bearing
part (`-- cap reached, more exist unheld`; the dump timestamp).

WHAT IS **NOT** A HIT, each verified by hand 2026-08-23 against a real dropout:

  * an explicit `Width`/`MaxWidth` on the TextBlock or anywhere between it and the panel
    -- it is then self-constrained and trimming works normally
    (ValueSearchPanel.axaml:694, `Width="520"`);
  * a `ToolTip.Tip` on the TextBlock **or on any ancestor** -- the tail stays readable
    even when clipped (MainWindow.axaml:338/353, where the tooltip is on the wrapping
    `Border`, not the TextBlock). ⚠ An earlier draft of this check looked only at DIRECT
    children of the panel, which excluded those two for the WRONG reason and would have
    hidden a genuine case nested one level deeper.

Deliberately NOT flagged: a bound TextBlock with no TextTrimming at all. There are ~130
of those and nearly all are short scalars (`PoseX`, `ArrayLimit`); flagging them is noise,
and without the author's own trimming hint there is no objective severity signal.
"""
from __future__ import annotations

import argparse
import glob
import sys
import xml.etree.ElementTree as ET

LAYOUT = ('StackPanel', 'Grid', 'DockPanel', 'WrapPanel', 'Canvas', 'RelativePanel')


def _local(tag: str) -> str:
    return tag.split('}')[-1]


def scan(root_glob: str = 'ui/UE5DumpUI/**/*.axaml'):
    hits = []
    for path in sorted(glob.glob(root_glob, recursive=True)):
        lines = open(path, encoding='utf-8').read().splitlines()
        root = ET.parse(path).getroot()
        parent = {c: p for p in root.iter() for c in p}
        for el in root.iter():
            if _local(el.tag) != 'TextBlock' or not el.get('TextTrimming'):
                continue
            if el.get('Width') or el.get('MaxWidth'):
                continue
            chain, p = [], parent.get(el)
            while p is not None:
                chain.append(p)
                if _local(p.tag) in LAYOUT:
                    break
                p = parent.get(p)
            if p is None or _local(p.tag) != 'StackPanel':
                continue
            if (p.get('Orientation') or '') != 'Horizontal':
                continue
            if any(a.get('Width') or a.get('MaxWidth') for a in chain):
                continue
            if el.get('ToolTip.Tip') or any(a.get('ToolTip.Tip') for a in chain):
                continue
            txt = el.get('Text') or ''
            ln = next((i + 1 for i, l in enumerate(lines) if txt and txt in l), 0)
            hits.append((path.replace(chr(92), '/'), ln, txt))
    return hits


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument('--list', action='store_true', help='print every hit and exit 0')
    a = ap.parse_args(argv)
    hits = scan()
    if a.list:
        for f, ln, t in hits:
            print(f'{f}:{ln}  {t}')
        print(f'{len(hits)} hit(s)')
        return 0
    if hits:
        print('CHECK FAILED: TextTrimming that can never fire, with no readable tail.')
        print('A horizontal StackPanel gives each child its DESIRED width, so trimming is')
        print('inert -- the text is hard-cut with no ellipsis. Make it the fill child of a')
        print('DockPanel (see PropertySearchPanel.axaml), or add ToolTip.Tip so the cut')
        print('tail stays readable. See this file\'s docstring for what is NOT a hit.')
        for f, ln, t in hits:
            print(f'  {f}:{ln}  {t}')
        return 1
    print('CHECK OK: no TextBlock has inert TextTrimming with an unreadable tail.')
    return 0


if __name__ == '__main__':
    sys.exit(main())
