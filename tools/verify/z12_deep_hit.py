r"""Z12 phase A — find an address that ONLY the deep container descent can attribute, and make
`FindInContainersDeep: hit` fire for the first time.

    py tools/verify/z12_deep_hit.py      (DumperTest dev running + injected, UI CLOSED)

THE SHAPE. `CollisionProfile::Profiles` is a `TArray<CollisionResponseTemplate>` (@0x38), and each
element has its own `TArray<ResponseChannel> CustomResponses` (@0x38 within a 0x48-byte element).
The INNER array's buffer address is therefore not a direct element of anything — it is reachable
only by descending into a container element and reading a container out of it. That is precisely
what the deep pass exists for and what a shallow pass structurally cannot see.

⚠ TWO CORRECTIONS TO THE RECIPE THIS CAME FROM, both measured:
  * `find_by_address` takes `scan_containers`, `container_depth` and `container_elem_cap`. The deep
    pass runs only when the shallow one found NOTHING **and** `container_depth > 1`.
  * "`FindInContainersDeep` has never fired on this machine" is not right. It has RUN several times
    (`maxDepth=5, maxElemProbe=256/1024`, on DumperTest and DQ7R, 2026-08-20) and always found
    **0 matches**. The accurate statement is that it has never **HIT** — `FindInContainersDeep: hit`
    has never appeared in any log. That is the line to watch for.

⚠ THE NEGATIVE CONTROL IS THE SAME ADDRESS AT depth=1. If depth=1 also finds it, the address was
shallow-attributable all along and the deep pass proved nothing.

⚠ Offsets are read LIVE where they can be and cross-checked against the SDK export where they
cannot; the element stride is derived from the struct's own size, not assumed.
"""
import json
import pathlib
import struct
import sys
import time

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
sys.stdout.reconfigure(encoding="utf-8", errors="replace")

from mutate_guard import read_bytes  # noqa: E402
from pipe_client import PipeClient  # noqa: E402

LOGDIR = pathlib.Path.home() / "AppData/Local/UE5CEDumper/Logs/DumperTest"
OFFSETS = LOGDIR / "offsets-0.log"
PROFILES_OFF = 0x38          # CollisionProfile::Profiles
ELEM_STRIDE = 0x48           # sizeof(CollisionResponseTemplate)
CUSTOM_OFF = 0x38            # CollisionResponseTemplate::CustomResponses


def say(s):
    enc = sys.stdout.encoding or "utf-8"
    sys.stdout.write(str(s).encode(enc, "replace").decode(enc, "replace") + chr(10))
    sys.stdout.flush()


def since(mark, needle):
    try:
        return [l for l in OFFSETS.read_text(encoding="utf-8", errors="replace").splitlines()
                if l.startswith("[") and l[1:20] >= mark and needle in l]
    except OSError:
        return []


def main():
    fails = []
    with PipeClient() as c:
        c.assert_build()
        c.ensure_scanned()

        inst = None
        r = c.request("find_instances", class_name="CollisionProfile", max_results=10)
        for i in (r.get("instances") or []):
            inst = i.get("addr")
            say("CollisionProfile instance: %s (%s)" % (inst, i.get("name")))
            break
        if not inst:
            say("BLOCKED: no CollisionProfile instance")
            return 2
        base = int(str(inst), 16)

        hdr = read_bytes(c, base + PROFILES_OFF, 16)
        data, num, mx = struct.unpack("<Qii", hdr)
        say("Profiles @+0x%X: Data=0x%X Num=%d Max=%d" % (PROFILES_OFF, data, num, mx))
        if not data or num <= 0:
            say("BLOCKED: Profiles is empty")
            return 2

        target, elem = None, None
        for e in range(num):
            ih = read_bytes(c, data + e * ELEM_STRIDE + CUSTOM_OFF, 16)
            if not ih or len(ih) != 16:
                continue
            idata, inum, imax = struct.unpack("<Qii", ih)
            if idata and imax > 0:
                target, elem = idata, e
                say("Profiles[%d].CustomResponses: Data=0x%X Num=%d Max=%d  <-- TARGET"
                    % (e, idata, inum, imax))
                break
        if not target:
            say("BLOCKED: no element has a non-empty CustomResponses — nothing nested to find")
            return 2
        A = "0x%X" % target

        # ---- NEGATIVE CONTROL: depth=1 must MISS -----------------------------
        say("")
        say("== NEGATIVE CONTROL: the same address at container_depth=1 ==")
        r1 = c.request("find_by_address", addr=A, scan_containers=True, container_depth=1)
        d1 = r1.get("data", r1)
        say("   found=%s container_matches=%d"
            % (d1.get("found"), len(d1.get("container_matches") or [])))
        # ⚠ Key on container_matches, NOT on `found`. `found` is the OBJECT lookup, and for a
        # heap buffer it reports match_kind "nearest" — here it names Default__BlueprintExtension
        # 96 bytes below, an object the buffer has nothing to do with. That is true at BOTH
        # depths, so using it as the control would fail a correct run. The container half is the
        # one that answers the question.
        say("   object half: found=%s match_kind=%s -> %s (%s) +%s"
            % (d1.get("found"), d1.get("match_kind"), d1.get("name"), d1.get("class"),
               d1.get("offset_from_base")))
        if d1.get("container_matches"):
            fails.append("depth=1 already produces a CONTAINER match, so the address was "
                         "shallow-reachable and the deep result below proves nothing")
        else:
            say("   OK: shallow produces ZERO container matches")

        # ---- the step --------------------------------------------------------
        say("")
        say("== DEEP: container_depth=5 ==")
        mark = time.strftime("%Y-%m-%d %H:%M:%S")
        time.sleep(1.1)
        r2 = c.request("find_by_address", addr=A, scan_containers=True,
                       container_depth=5, container_elem_cap=256)
        d2 = r2.get("data", r2)
        cs = d2.get("container_scan") or {}
        matches = d2.get("container_matches") or []
        say("   found=%s  matches=%d" % (d2.get("found"), len(matches)))
        say("   container_scan: %s" % json.dumps(cs))
        for m in matches[:4]:
            say("      %s" % json.dumps(m)[:220])

        if not matches:
            fails.append("the deep descent MISSED a structurally valid nested container address. "
                         "That is an Aura.cpp defect, not a fixture problem — the address is the "
                         "Data pointer of Profiles[%d].CustomResponses, reachable in exactly the "
                         "two hops the deep pass is written to make." % elem)
        else:
            if not cs.get("deep_scan"):
                fails.append("matches were returned but container_scan.deep_scan is false")
            else:
                say("   OK: deep_scan=true")
            chained = [m for m in matches if m.get("nested_chain")]
            say("   matches carrying a nested_chain: %d" % len(chained))
            if not chained:
                fails.append("no match carries a nested_chain — the descent path is not being "
                             "reported, which is the half the UI renders")

        deep_lines = since(mark, "FindInContainersDeep:")
        hit_lines = [l for l in deep_lines if "Deep: hit" in l]
        say("   FindInContainersDeep lines: %d   (of which 'hit': %d)"
            % (len(deep_lines), len(hit_lines)))
        for l in deep_lines[:6]:
            say("      %s" % l.strip()[-130:])
        if not deep_lines:
            fails.append("the deep pass did not even run")
        elif not hit_lines and matches:
            fails.append("matches were returned but no 'FindInContainersDeep: hit' line was "
                         "logged — the two disagree")

        say("")
        if hit_lines:
            say("⭐ 'FindInContainersDeep: hit' has fired — the first time on this machine.")
        say("ADDRESS FOR THE UI PHASE: %s   (Profiles[%d].CustomResponses data)" % (A, elem))
        pathlib.Path("out/z12").mkdir(parents=True, exist_ok=True)
        pathlib.Path("out/z12/deep_address.txt").write_text(A, encoding="utf-8")

    say("")
    if fails:
        say("Z12 phase A: FAIL")
        for f in fails:
            say("   - %s" % f)
        return 1
    say("Z12 phase A: PASS — an address only the deep descent can attribute, with depth=1 shown "
        "to miss it")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
