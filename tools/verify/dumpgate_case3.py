"""Dump Explorer identity gate, case (3) -- manufacture "same game, DIFFERENT BUILD".

    py dumpgate_case3.py livehash              # what pe_hash does the LIVE game report?
    py dumpgate_case3.py flip <dump.jsonl>     # write a case-(3) twin beside it

WHY THIS IS NOT "wait for a DQ7R patch". The register bullet says case (3) "needs an
actual DQ7R patch to come along, so it is opportunistic, not schedulable". That is
wrong, and the gate's own source says why (DumpExplorerViewModel.cs:396-403): the
tier-2 branch is three PLAIN STRING comparisons over `meta.module` and `meta.pe_hash`
as read from the dump's first line. Nothing hashes the running exe at match time, so
flipping one hex digit in that line manufactures a different build exactly as far as
the gate can tell. A patch would be a slower way to produce the same two strings.

⚠⚠ THE TRAP THIS RIG EXISTS FOR: case (3) SILENTLY BECOMES A DIFFERENT CASE IF THE
LIVE HASH IS EMPTY. The tier-2 branch picks the "Different build of the same game"
caveat only when BOTH hashes are non-empty and differ; if either side is empty it
falls through to "Build identity unknown (no pe_hash) -- matched on module name only."
Both messages are amber caveats in the same label, so a run that never checked the
live hash can photograph the WRONG BRANCH and file it as a pass. Hence `livehash`:
confirm it is non-empty BEFORE the run, not after.

THE PAIR IS THE POINT. Load the ORIGINAL dump (both hashes equal -> no caveat) and
then the flipped twin (differ -> caveat), in one session against one game. A single
load of a flipped file proves only that a literal renders; the pair shows the gate
choosing between branches on the one input that changed.
"""
import json
import pathlib
import sys

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))


def livehash():
    from pipe_client import PipeClient
    with PipeClient().connect() as c:
        r = c.request("get_pointers")
        d = r.get("data", r) or {}
        h = str(d.get("pe_hash", "") or "")
        mod = str(d.get("module_name", d.get("module", "")) or "")
        print("  live module   : %r" % mod)
        print("  live pe_hash  : %r  (len %d)" % (h, len(h)))
        if not h:
            print()
            print("  STOP -- the live pe_hash is EMPTY. Case (3) is UNREACHABLE on this")
            print("  process: the gate would show 'Build identity unknown', not 'Different")
            print("  build'. Do not photograph it as case (3).")
            return 1
        print()
        print("  OK -- non-empty, so a flipped dump hash lands in the tier-2 differ branch.")
        return 0


def flip(src):
    p = pathlib.Path(src)
    lines = p.read_text(encoding="utf-8").splitlines(True)
    if not lines:
        raise SystemExit("dumpgate: %s is empty" % p)
    meta = json.loads(lines[0])
    old = str(meta.get("pe_hash", "") or "")
    if not old:
        raise SystemExit("dumpgate: %s has NO pe_hash in its meta line -- that is case "
                         "(2)-adjacent 'identity unknown', not case (3)" % p.name)
    # Flip the FIRST hex digit to something it is not, keeping length and hex-ness so
    # the only difference from the live hash is the value. Length changes could plausibly
    # be rejected upstream; this keeps the change minimal and inside the same domain.
    ch = old[0]
    new = ("1" if ch.upper() != "1" else "2") + old[1:]
    meta["pe_hash"] = new
    lines[0] = json.dumps(meta, separators=(",", ":")) + "\n"
    out = p.with_suffix(".case3.jsonl")
    out.write_text("".join(lines), encoding="utf-8")
    print("  module   : %r  (UNCHANGED -- must still match live, or this is case 2)"
          % meta.get("module", ""))
    print("  pe_hash  : %s  ->  %s" % (old, new))
    print("  wrote    : %s  (%d B, %d lines)"
          % (out, out.stat().st_size, len(lines)))
    print()
    print("  EXPECTED on load, verbatim from DumpExplorerViewModel.cs:400 --")
    print('    "Different build of the same game -- offsets may have moved. "')
    print("  and on the ORIGINAL file, that sentence must be ABSENT (the control).")
    return 0


if __name__ == "__main__":
    a = sys.argv[1:] or ["livehash"]
    if a[0] == "livehash":
        raise SystemExit(livehash())
    if a[0] == "flip" and len(a) > 1:
        raise SystemExit(flip(a[1]))
    raise SystemExit(__doc__)
