#!/usr/bin/env python3
"""AF16-AF23 Xref half — find a property that TWO OR MORE Blueprint functions touch.

    py tools/verify/af16_xref_fixture.py                 # find and confirm a fixture
    py tools/verify/af16_xref_fixture.py --top 15        # show more candidates

The row needs the Xref dialog to return >= 2 rows so its six headers can be sorted.
Five earlier hunts picked fields by intuition and every one returned 0 or 1. The
reason was structural rather than unlucky: the class they used owns 2 UFunctions,
one of which is an event stub. So this does not guess at all -- it inverts the
mapping the DLL already exposes:

    for each script-backed UFunction:  walk_function_props -> the props it touches
    invert:                            prop_addr -> {functions that touch it}
    any prop with >= 2 distinct functions IS the fixture, by construction.

⚠ NEWNESS OF THE ANSWER IS NOT THE POINT -- CONFIRMATION IS. The inverted map is
built from `walk_function_props`, so a fixture found there could still disagree with
`find_property_xrefs`, which is what the DIALOG actually calls. Every reported
candidate is therefore re-asked through `find_property_xrefs` and only counted if
THAT returns >= 2. Two different commands agreeing is the whole value; if they ever
disagree, that disagreement is the finding and this rig prints it rather than
quietly preferring one.

⚠ Anti-vacuity, enforced: the run FAILS if no script-backed function was found, if
no reply used the exact bytecode path, or if the first reply does not carry the
fields this rig reads -- a renamed key would otherwise look exactly like "this game
has no such property", which is the failure mode that costs a whole session.

⛔ The UI must be DISCONNECTED: Fern::kMaxPipeInstances = 3 and the UI holds 2 lanes.
"""
from __future__ import annotations

import argparse
import collections
import pathlib
import sys

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
from pipe_client import PipeClient  # noqa: E402

sys.stdout.reconfigure(encoding="utf-8", errors="replace")
sys.stderr.reconfigure(encoding="utf-8", errors="replace")

FUNC_NATIVE = 0x0000_0400  # AllFunctionsResult.cs:56 — script-backed means NOT this


def _shape_check(tag: str, reply: dict, wanted: list[str]) -> None:
    """A renamed key must fail loudly, not silently yield an empty result."""
    missing = [k for k in wanted if k not in reply]
    if missing:
        raise SystemExit(
            "%s reply is missing %s — this rig reads keys that no longer exist, so an\n"
            "empty result here would be a LIE about the game. Actual keys: %s"
            % (tag, missing, sorted(reply.keys()))
        )


def main() -> int:
    ap = argparse.ArgumentParser(description="find a >=2-function property fixture")
    ap.add_argument("--top", type=int, default=8, help="candidates to print (default 8)")
    ap.add_argument("--confirm", type=int, default=5,
                    help="how many to re-ask via find_property_xrefs (default 5)")
    args = ap.parse_args()

    with PipeClient() as c:
        c.assert_build()
        c.ensure_scanned()

        r = c.request("list_all_functions", game_only=True)
        _shape_check("list_all_functions", r, ["functions"])
        funcs = r.get("functions") or []
        c.check_complete(r)
        script = [f for f in funcs
                  if not (int(f.get("function_flags") or 0) & FUNC_NATIVE) and f.get("func_addr")]
        print("functions: %d total, %d script-backed (non-native)" % (len(funcs), len(script)))
        assert script, "no script-backed function at all — nothing below could mean anything"

        owner = {}           # prop_addr -> (class, name)
        touched = collections.defaultdict(set)   # prop_addr -> {func_addr}
        fnames = {}          # func_addr -> func_name
        bytecode_seen = 0
        checked = 0
        for f in script:
            fa = f["func_addr"]
            try:
                w = c.request("walk_function_props", func_addr=fa)
            except Exception:
                continue
            if checked == 0:
                _shape_check("walk_function_props", w, ["props"])
            checked += 1
            is_bytecode = str(w.get("method", "")).lower().startswith("bytecode")
            if is_bytecode:
                bytecode_seen += 1
            else:
                # MEASURED 2026-08-23: the other path is `disasm`, the native-disassembly
                # HEURISTIC (Path 2). Counting it here manufactures fixtures that the dialog
                # can never show: DOLLCharacter::AutoPossessPlayer came back from 16 disasm
                # replies and `find_property_xrefs` -- a Kismet BYTECODE xref (Aura.cpp:5541),
                # i.e. a different question -- correctly returned 0 for it, with and without
                # game_only. Skipping keeps this rig's candidates answerable by the dialog.
                continue
            for p in (w.get("props") or []):
                pa = p.get("prop_addr")
                if not pa or str(p.get("scope", "")).lower() != "instance":
                    continue
                touched[pa].add(fa)
                owner.setdefault(pa, (f.get("class_name"), p.get("name")))
                fnames[fa] = f.get("func_name")
            if checked % 100 == 0:
                print("  … probed %d/%d, %d distinct instance props so far"
                      % (checked, len(script), len(touched)))

        print("probed %d function(s); %d took the exact bytecode path; %d distinct instance props"
              % (checked, bytecode_seen, len(touched)))
        assert bytecode_seen, ("not one reply used the bytecode path — every hit would be the "
                              "native-disasm heuristic, which is NOT what this row tests")

        ranked = sorted(touched.items(), key=lambda kv: -len(kv[1]))
        multi = [(pa, fs) for pa, fs in ranked if len(fs) >= 2]
        print()
        print("properties touched by >= 2 functions: %d" % len(multi))
        for pa, fs in ranked[:args.top]:
            cls, nm = owner.get(pa, ("?", "?"))
            print("  %-18s %-34s %-28s %d func(s)  %s"
                  % (pa, cls, nm, len(fs),
                     ", ".join(sorted(fnames.get(x) or "?" for x in fs)[:3])))

        if not multi:
            print()
            print("NO FIXTURE — every instance property in this title is touched by at most one")
            print("script-backed function. That is a real measurement, not a failed search:")
            print("%d functions were probed and %d distinct props were seen."
                  % (checked, len(touched)))
            return 2

        # ── confirm through the command the DIALOG actually uses ──────────────
        print()
        print("re-asking the top %d through find_property_xrefs (the dialog's own command):"
              % min(args.confirm, len(multi)))
        confirmed = []
        for pa, fs in multi[:args.confirm]:
            cls, nm = owner.get(pa, ("?", "?"))
            try:
                x = c.request("find_property_xrefs", prop_addr=pa, game_only=True)
            except Exception as e:
                print("  %-18s %-30s ERROR %s" % (pa, nm, e))
                continue
            n = len(x.get("xrefs") or [])
            agree = "AGREE" if n == len(fs) else "DIFFER(map=%d)" % len(fs)
            print("  %-18s %-30s xrefs=%d  %s" % (pa, nm, n, agree))
            if n >= 2:
                confirmed.append((pa, cls, nm, n))

        print()
        if not confirmed:
            print("the inverted map found candidates but find_property_xrefs confirmed NONE.")
            print("That disagreement is itself the finding — record it, do not paper over it.")
            return 3

        pa, cls, nm, n = confirmed[0]
        print("FIXTURE:  class %s   field %s   (%d xrefs)" % (cls, nm, n))
        print("Use the CLASS and FIELD NAME in the UI — the address dies with the process.")
        return 0


if __name__ == "__main__":
    raise SystemExit(main())
