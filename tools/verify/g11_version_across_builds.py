r"""G11 step 1 — did version detection change across the build-3112 boundary?

    py tools/verify/g11_version_across_builds.py
    py tools/verify/g11_version_across_builds.py --all      # every record, not just spanning games

⭐ WHY THIS EXISTS. G11 step 1 asks for a **before/after**: record `ueVersion` / `versionDetected` /
`lowConfidence` for a game on a build < 3112, upgrade, and check all three are unchanged. Elliot and
DragonSword Awakening were done live. The trouble is that the "before" side cannot be produced any
more — this machine is long past 3112 — so the row looked permanently blocked for every other title.

It is not. Every `scan-*.log` is **self-contained**: it opens with the `Logger started | build:`
banner AND carries the `FindAll: UE Version` line, so an archived log from a pre-3112 run is a
recorded "before" for whichever game wrote it. The archive on this machine goes back far enough to
cover several titles, which turns a blocked live step into an offline measurement.

⚠ TWO FORMAT STRINGS, and they spell the same field differently — grep for one and you silently
lose half the corpus:

    live   FindAll: UE Version = %u (tier=%d, detected=%s, lowConfidence=%s, publisher=%s)
    cached FindAll: UE Version = %u (cached, rev=%u, detected=%s, lowConf=%s) - skipped DetectVersion
                                                              ^^^^^^^^ note: lowConf, not lowConfidence

⚠ A CACHED line is not an independent re-detection — it is `Flamme` replaying
`UE5CEDumper.{Machine}.json`. `kVersionDetectLogicRev` = 5 means the first launch of a build >= 3112
re-detects and rewrites the cache, so the meaningful post-3112 witness is the first LIVE line after
the boundary. Both are printed; the verdict only trusts what it can.

⚠ It FAILS LOUDLY: a `FindAll: UE Version` line matching neither pattern is reported as UNPARSED
rather than skipped, because a silently-dropped record is exactly how this kind of sweep lies.
Read-only.
"""
import glob
import io
import os
import re
import sys

sys.stdout.reconfigure(encoding="utf-8", errors="replace")

SHOW_ALL = "--all" in sys.argv
# --selftest corrupts ONE post-boundary record in memory, so the comparison has to
# report DIFF. A sweep whose only observed output is "SAME" has not been shown able
# to say anything else; this is the negative control that makes the real run mean
# something. It never touches disk.
SELFTEST = "--selftest" in sys.argv
BOUNDARY = 3112

LOGS = os.path.join(os.environ["LOCALAPPDATA"], "UE5CEDumper", "Logs")

RE_BUILD = re.compile(r"build:\s*\d+\.\d+\.\d+\.(\d+)")
RE_LIVE = re.compile(
    r"FindAll: UE Version = (\d+) \(tier=(-?\d+), detected=(\w+), "
    r"lowConfidence=(\w+), publisher=([^)]*)\)")
RE_CACHED = re.compile(
    r"FindAll: UE Version = (\d+) \(cached, rev=(\d+), detected=(\w+), lowConf=(\w+)\)")
RE_ANY = re.compile(r"FindAll: UE Version")
RE_TS = re.compile(r"^\[(\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2})")


def scan_file(path):
    """-> (build, [records]) ; records are dicts. build None if no banner."""
    try:
        lines = io.open(path, encoding="utf-8", errors="replace").readlines()
    except OSError as e:
        return None, [{"kind": "UNREADABLE", "detail": str(e)}]
    build = None
    recs = []
    for l in lines:
        if build is None:
            m = RE_BUILD.search(l)
            if m:
                build = int(m.group(1))
        if not RE_ANY.search(l):
            continue
        ts = RE_TS.match(l)
        ts = ts.group(1) if ts else "?"
        m = RE_LIVE.search(l)
        if m:
            recs.append({"kind": "live", "ts": ts, "ver": int(m.group(1)),
                         "tier": m.group(2), "detected": m.group(3),
                         "lowconf": m.group(4), "publisher": m.group(5)})
            continue
        m = RE_CACHED.search(l)
        if m:
            recs.append({"kind": "cached", "ts": ts, "ver": int(m.group(1)),
                         "rev": m.group(2), "detected": m.group(3),
                         "lowconf": m.group(4), "publisher": "-"})
            continue
        recs.append({"kind": "UNPARSED", "ts": ts, "raw": l.rstrip()[:160]})
    return build, recs


def main():
    games = {}
    unparsed = []
    no_build = 0
    for path in glob.glob(os.path.join(LOGS, "*", "*.log")):
        game = os.path.basename(os.path.dirname(path))
        build, recs = scan_file(path)
        for r in recs:
            if r["kind"] in ("UNPARSED", "UNREADABLE"):
                unparsed.append((game, os.path.basename(path), r))
                continue
            if build is None:
                no_build += 1
                continue
            r["build"] = build
            games.setdefault(game, []).append(r)

    print(f"log root : {LOGS}")
    print(f"games    : {len(games)}   records: {sum(len(v) for v in games.values())}")
    print(f"boundary : build {BOUNDARY} (kVersionDetectLogicRev=5 forces a re-detect on the "
          f"first launch at or above it)\n")

    if SELFTEST:
        # ⚠ It must corrupt a record of a SPANNING game. The first version of this
        # control picked the first game with any post-boundary record and hit
        # Avowed — post-only, so it never enters the comparison and the verdict
        # stayed SAME. A control that cannot fire proves nothing, and it looked
        # exactly like a passing control.
        done = False
        for game in sorted(games):
            pre = [r for r in games[game] if r["build"] < BOUNDARY]
            post = [r for r in games[game] if r["build"] >= BOUNDARY]
            if pre and post:
                live = [r for r in post if r["kind"] == "live"]
                target = (live or post)[0]      # the record the verdict actually reads
                target["ver"] += 1
                print(f"[selftest] corrupted the compared post-boundary record for {game}: "
                      f"ver -> {target['ver']}. Expect a DIFF verdict below.\n")
                done = True
                break
        if not done:
            print("[selftest] no spanning game to corrupt — the control cannot run\n")

    spanning, one_sided = [], []
    for game, recs in sorted(games.items()):
        pre = [r for r in recs if r["build"] < BOUNDARY]
        post = [r for r in recs if r["build"] >= BOUNDARY]
        (spanning if pre and post else one_sided).append((game, pre, post))

    print(f"=== {len(spanning)} game(s) with records on BOTH sides of {BOUNDARY} ===\n")
    verdicts = []
    for game, pre, post in spanning:
        pre.sort(key=lambda r: (r["build"], r["ts"]))
        post.sort(key=lambda r: (r["build"], r["ts"]))
        before = pre[-1]                                    # newest pre-boundary
        live_post = [r for r in post if r["kind"] == "live"]
        after = live_post[0] if live_post else post[0]      # first LIVE after, else first
        same = (before["ver"] == after["ver"]
                and before["detected"] == after["detected"]
                and before["lowconf"] == after["lowconf"])
        verdicts.append((game, same, before, after, bool(live_post)))
        mark = "SAME " if same else "DIFF!"
        print(f"  [{mark}] {game}")
        print(f"      before  build {before['build']:<5} {before['kind']:<7} "
              f"ver={before['ver']} detected={before['detected']} lowConf={before['lowconf']}"
              f"  ({before['ts']})")
        print(f"      after   build {after['build']:<5} {after['kind']:<7} "
              f"ver={after['ver']} detected={after['detected']} lowConf={after['lowconf']}"
              f"  ({after['ts']})")
        if not live_post:
            print("      ⚠ no LIVE post-3112 line — the 'after' is a CACHED replay, so it is "
                  "not an independent re-detection")
        print(f"      records pre={len(pre)} post={len(post)}")
        if SHOW_ALL:
            for r in pre + post:
                print(f"        b{r['build']:<5} {r['kind']:<7} ver={r['ver']} "
                      f"detected={r['detected']} lowConf={r['lowconf']} {r['ts']}")
        print()

    print(f"=== {len(one_sided)} game(s) with records on ONE side only (cannot answer G11) ===")
    for game, pre, post in one_sided:
        side = "pre-only" if pre else "post-only"
        builds = sorted({r["build"] for r in (pre or post)})
        print(f"  {game:<44} {side:<10} builds {builds[0]}..{builds[-1]}  ({len(pre or post)} rec)")

    print()
    if unparsed:
        print(f"⚠ {len(unparsed)} UNPARSED/UNREADABLE FindAll line(s) — investigate, do not ignore:")
        for g, f, r in unparsed[:15]:
            print(f"  {g}/{f}: {r.get('raw', r.get('detail'))}")
    else:
        print("✅ every FindAll line parsed by one of the two patterns")
    if no_build:
        print(f"⚠ {no_build} record(s) dropped: their log carried no build banner")

    print()
    diff = [v for v in verdicts if not v[1]]
    if not verdicts:
        print("VERDICT: no game spans the boundary — G11 step 1 cannot be answered from this archive")
    elif diff:
        print(f"VERDICT: {len(diff)} game(s) CHANGED across {BOUNDARY} — report them:")
        for g, _, b, a, _ in diff:
            print(f"  {g}: ({b['ver']},{b['detected']},{b['lowconf']}) -> "
                  f"({a['ver']},{a['detected']},{a['lowconf']})")
    else:
        live = sum(1 for v in verdicts if v[4])
        print(f"VERDICT: all {len(verdicts)} spanning game(s) report the SAME three values "
              f"across build {BOUNDARY}; {live} of them have a LIVE (re-detected) post-boundary "
              f"witness rather than a cached replay")
    return 0


if __name__ == "__main__":
    sys.exit(main())
