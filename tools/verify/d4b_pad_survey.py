r"""D4b — the access-detector pad, surveyed across ENGINE classes on whatever title is injected.

    py tools/verify/d4b_pad_survey.py                    # any UE game, injected
    py tools/verify/d4b_pad_survey.py --expect-pad 8     # assert a specific side

WHY THIS EXISTS ALONGSIDE `d4b_delegate_pad.py`. That rig proves the fix READS correctly, and it
needs DumperTest's purpose-built bound delegates to do it. This one asks a narrower question that
needs **no fixture at all**, so it runs on any injected title:

    does `DynOff::DelegatePadFromElementSize` recognise the sizes this engine actually reports?

UE 5.3 gave `TScriptDelegate`/`TMulticastScriptDelegate` a `TDelegateAccessHandlerBase` base whose
`DO_CHECK` specialization holds one `std::atomic<uint64>`, so a delegate payload is 8 bytes longer
in Debug/Development/DebugGame than in Test/Shipping. The derivation refuses anything that is
neither, and a refusal means the field is reported as unread. **A size this repo has never seen
would therefore turn a working read into a blank** -- which is the honest failure, but still a
regression on that title. This is the cheap screen for it.

WHAT IS MEASURED
  1  every `DelegateProperty` / `MulticastInlineDelegateProperty` / `MulticastDelegateProperty`
     on the surveyed classes reports an ElementSize the derivation accepts;
  2  ⭐ they all imply the SAME pad. The pad is a property of the BUILD, not of the class, so a
     split verdict means one of the two base sizes is wrong for this engine;
  3  `MulticastSparseDelegateProperty` reports `sizeof(FSparseDelegate)` -- 1 -- which is the
     reason the sparse walker cannot use this derivation and needs `LocateInvocationList`;
  4  nothing renders the "unexpected ElementSize" refusal.

⚠ `walk_class` reports the CLASS's field table, so this needs no instance and no bound delegate.
It says nothing about whether the bindings READ correctly -- that is `d4b_delegate_pad.py`'s job
and it needs the fixture.
"""
from __future__ import annotations

import argparse
import json
import subprocess
import sys
from collections import defaultdict
from pathlib import Path

HERE = Path(__file__).resolve().parent
ROOT = HERE.parent.parent
CLIENT = HERE / "pipe_client.py"

# How many loaded UClasses to walk. Deliberately a SAMPLE of whatever the title has loaded
# rather than a fixed name list: `list_classes` takes no name filter (only `game_only` and
# `limit`), and hard-coding engine class names would make the survey silently measure nothing
# on a title that names them differently or has not loaded them.
SAMPLE = 600
BATCH = 60          # walk_class_batch is the pipe-amortised form -- one call, many classes

MCD_BASE = 16          # sizeof(FMulticastScriptDelegate) with no detector: a bare TArray header
PAD = 8                # DynOff::kDelegateDetectorPad
MULTICAST = ("MulticastInlineDelegateProperty", "MulticastDelegateProperty")
UNICAST = "DelegateProperty"
SPARSE = "MulticastSparseDelegateProperty"


def call(cmd: str, args: dict) -> dict:
    r = subprocess.run([sys.executable, str(CLIENT), cmd, "--args", json.dumps(args)],
                       capture_output=True, text=True, encoding="utf-8", errors="replace",
                       cwd=str(ROOT))
    out = r.stdout or ""
    i = out.find("{")
    if i < 0:
        raise SystemExit("pipe_client gave no JSON for %s:\n%s\n%s" % (cmd, out, r.stderr))
    return json.loads(out[i:])


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--expect-pad", type=int, choices=(0, 8),
                    help="fail unless the surveyed classes imply this pad")
    a = ap.parse_args()

    # sizeof(FName) drives the unicast base; ask the DLL rather than assuming 8.
    off = call("get_offsets", {})
    cpn = bool(off.get("case_preserving", False))
    fname = 12 if cpn else 8
    uni_base = 8 + fname
    print("FName      : %d bytes (case-preserving=%s) -> unicast base %d"
          % (fname, cpn, uni_base))

    pads: dict[int, list[str]] = defaultdict(list)
    unknown: list[str] = []
    sparse_sizes: set[int] = set()
    seen_classes = 0

    listing = call("list_classes", {"limit": SAMPLE, "game_only": False})
    addrs = [c["class_addr"] for c in listing.get("classes", []) if c.get("class_addr")]
    print("listing    : %d class(es) offered" % len(addrs))

    for i in range(0, len(addrs), BATCH):
        r = call("walk_class_batch", {"addrs": addrs[i:i + BATCH]})
        if not r.get("ok"):
            continue
        for cls in r.get("classes", []):
            if not cls.get("ok", True):
                continue
            seen_classes += 1
            cname = cls.get("name", cls.get("class_name", "?"))
            for f in cls.get("fields", []):
                t, sz, n = f.get("type", ""), f.get("size"), f.get("name", "?")
                if "Delegate" not in t or not isinstance(sz, int):
                    continue
                where = "%s::%s" % (cname, n)
                if t == SPARSE:
                    sparse_sizes.add(sz)
                    continue
                base = MCD_BASE if t in MULTICAST else (uni_base if t == UNICAST else None)
                if base is None:
                    continue
                if sz == base:
                    pads[0].append(where)
                elif sz == base + PAD:
                    pads[PAD].append(where)
                else:
                    unknown.append("%s (%s, ElementSize=%d, expected %d or %d)"
                                   % (where, t, sz, base, base + PAD))

    print("classes    : %d walked" % seen_classes)
    total = sum(len(v) for v in pads.values())
    for p in sorted(pads):
        ex = ", ".join(pads[p][:3])
        print("pad %-2d     : %4d propert%s   e.g. %s"
              % (p, len(pads[p]), "y" if len(pads[p]) == 1 else "ies", ex))
    print("sparse     : ElementSize %s (sizeof(FSparseDelegate) -- says nothing about the pad, "
          "which is why the sparse walker derives it from the object instead)"
          % (sorted(sparse_sizes) or "none seen"))

    fails: list[str] = []
    if seen_classes == 0:
        fails.append("no class could be walked -- is a game injected and the engine really up?")
    if total == 0:
        fails.append("no delegate-family property found on %d classes; the survey measured "
                     "nothing (a zero here is not evidence about the pad)" % seen_classes)
    if unknown:
        fails.append("%d property/properties report an ElementSize the derivation REFUSES, so "
                     "the DLL would render them unread on this title:\n      %s"
                     % (len(unknown), "\n      ".join(unknown[:8])))
    if len(pads) > 1:
        fails.append("SPLIT VERDICT: %s. The pad is a property of the BUILD, not of the class, "
                     "so two answers mean one of the base sizes is wrong for this engine."
                     % {p: len(v) for p, v in pads.items()})
    if sparse_sizes and sparse_sizes != {1}:
        fails.append("MulticastSparseDelegateProperty ElementSize %s -- expected 1 "
                     "(sizeof(FSparseDelegate))" % sorted(sparse_sizes))

    derived = next(iter(pads), None) if len(pads) == 1 else None
    if a.expect_pad is not None and derived != a.expect_pad:
        fails.append("expected pad %d, derived %r" % (a.expect_pad, derived))

    print()
    if fails:
        print("D4b survey: FAIL")
        for f in fails:
            print("  - %s" % f)
        return 1
    print("D4b survey: PASS -- %d delegate propert%s across %d classes, all implying pad %d "
          "(%s build), and no size the derivation cannot name."
          % (total, "y" if total == 1 else "ies", seen_classes, derived,
             "checked: Development/Debug" if derived == PAD else "Shipping/Test, or UE <= 5.2"))
    return 0


if __name__ == "__main__":
    sys.exit(main())
