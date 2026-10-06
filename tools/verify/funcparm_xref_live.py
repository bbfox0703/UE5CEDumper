r"""[FUNCPARM-CONSUMERS] step 1 -- Find Func (find_functions_by_class) on DumperTest, no UI.

    py tools/verify/funcparm_xref_live.py [--label NAME]
    (DumperTest Shipping running and injected with a DLL from build 3624 or later; never while the UI holds the pipe)

The verification-register row's step 1, as a rig:
  (a) Character's class, game_only: ExecuteUbergraph_ABP_Manny is ABSENT. It holds a local typed Character (its
      cast node's output, K2Node_DynamicCast_AsCharacter), which a DLL before 3624 counted as a parameter.
  (b) the positive control, Actor's class, game_only: D4_OnActorHitProbe (DumperTestActor) and OnPeerBeginOverlap
      (DumperTestSparseListener), which take AActor* parameters, are listed with kind "param". A total miss there
      would mean the CPF_Parm read failed, which (a) alone would pass.
Each reply must be complete (no deadline_hit / cap_hit), or an absence proves nothing.

Writes out/funcparm/<label>.json.
"""
import argparse
import json
import pathlib
import sys

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
sys.stdout.reconfigure(encoding="utf-8", errors="replace")

from pipe_client import PipeClient  # noqa: E402

OUTDIR = pathlib.Path("out/funcparm")
ABSENT = "ExecuteUbergraph_ABP_Manny"
CONTROLS = ("D4_OnActorHitProbe", "OnPeerBeginOverlap")


def xrefs(c, path):
    obj = c.request("find_object", path=path)
    addr = obj.get("addr") or obj.get("address")
    if not addr:
        raise SystemExit(f"find_object {path}: no address in {obj}")
    r = c.request("find_functions_by_class", class_addr=addr, game_only=True, max_results=2000)
    PipeClient.check_complete(r.get("scan", {}), f"find_functions_by_class {path}")
    if r.get("scan", {}).get("cap_hit"):
        raise SystemExit(f"{path}: cap_hit, a prefix is not the population")
    return addr, r.get("xrefs", [])


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--label", default="run")
    a = ap.parse_args()

    with PipeClient() as c:
        build = c.assert_build()
        c.ensure_scanned()
        char_addr, char_x = xrefs(c, "/Script/Engine.Character")
        actor_addr, actor_x = xrefs(c, "/Script/Engine.Actor")

    absent_hits = [x for x in char_x if x.get("func_name") == ABSENT]
    found = {x.get("func_name"): x for x in actor_x if x.get("func_name") in CONTROLS}
    control_ok = all(name in found and found[name].get("kind") == "param" for name in CONTROLS)
    verdict = "PASS" if not absent_hits and control_ok else "FAIL"
    report = dict(build=build, character=dict(addr=char_addr, xrefs=len(char_x),
                                              names=sorted({x.get("func_name") for x in char_x})),
                  absent_hits=absent_hits,
                  actor=dict(addr=actor_addr, xrefs=len(actor_x)),
                  controls={k: found.get(k) for k in CONTROLS}, verdict=verdict)
    OUTDIR.mkdir(parents=True, exist_ok=True)
    out = OUTDIR / f"{a.label}.json"
    out.write_text(json.dumps(report, indent=1, ensure_ascii=False), encoding="utf-8")
    print(f"build {build}: Character xrefs {len(char_x)}, {ABSENT} present: {bool(absent_hits)}")
    for k in CONTROLS:
        x = found.get(k)
        print(f"  control {k}: {'kind ' + str(x.get('kind')) + ', owner ' + str(x.get('owner_class')) if x else 'MISSING'}")
    print(f"{verdict} -> {out}")
    return 0 if verdict == "PASS" else 1


if __name__ == "__main__":
    sys.exit(main())
