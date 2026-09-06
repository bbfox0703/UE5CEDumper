r"""Audit A1 — the TSoftObjectPtr / TLazyObjectPtr payload envelope, measured on a live game.

    py tools/verify/a1_softlazy_envelope.py <ProcessFolderName>
    py tools/verify/a1_softlazy_envelope.py ES2-Win64-Shipping
    py tools/verify/a1_softlazy_envelope.py LushfoilSim-Win64-Shipping

THE QUESTION. `5eafd419` (+ the follow-up `fffe5fcf`) stopped reading `TSoftObjectPtr`'s
`FSoftObjectPath` at a hardcoded `+0x10`. That is right only up to UE 5.2: 5.3 deleted
`TPersistentObjectPtr::TagAtLastTest` and the path moved to `+0x08`. `TLazyObjectPtr`'s `FGuid`
was read at `+0x10` too and was wrong in EVERY era. The envelope is now MEASURED
(`Ubel.cpp` `PersistentPtrEnvelope`), and only a real measurement is latched.

WHY THE SUITE CANNOT CLOSE IT. `dll/CMakeLists.txt` compiles only `dll_helpers_test.cpp` +
`Radar.cpp` + `Denken.cpp`; `Ubel.cpp` is in NO test target. The helper test pins the ARITHMETIC
(`FSoftObjectPathSizeFor` / `PersistentPtrEnvelopeFor`); nothing exercises the latch, the log line,
or the readers. So the DLL-side half of this row can only be taken from a running game.

WHAT THIS RIG DOES. It finds soft/lazy properties by TYPE (`search_properties` matches by name, so
the type filter is applied client-side), walks the classes that own them -- which is what makes the
measurement fire, since it is lazy and latches once per distinct envelope per process -- and then
reads the two `payload envelope measured` lines out of `offsets-0.log`.

⚠ THE LINE IS IN offsets-0.log, NOT walk-0.log. It is written from `Ubel.cpp` but its category is
`DYNO:PersistPtr`, and `Sein.cpp`'s table routes `DYNO` to LF_Offsets. Grepping walk-0.log returns
nothing and reads as a failure. (`docs/verification-register.md` says so out loud; this rig obeys.)

⚠ EXPECTED VALUE IS VERSION-DEPENDENT **AND PER TYPE**, so the rig derives it rather than
hardcoding one. From 5.3 both are `+0x08`. Before 5.3 they DIFFER: soft is `+0x10`
(FWeakObjectPtr 8 + Tag 4 + pad 4, because FSoftObjectPath is 8-aligned) and lazy is `+0x0C`
(no pad -- FUniqueObjectGuid is a bare FGuid, alignof 4). A pre-5.3 title is a NEGATIVE CONTROL,
not a failure. ⛔ Do NOT assume the two share a value: this rig did on its first run, and scored a
CORRECT DLL as a FAIL on OCTOPATH for reading lazy at the 0x0C the engine actually uses.
"""
import pathlib
import re
import sys

HERE = pathlib.Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
from pipe_client import PipeClient  # noqa: E402

SOFT_TYPES = {"SoftObjectProperty", "SoftClassProperty"}
LAZY_TYPES = {"LazyObjectProperty"}
# One broad query per starting letter: search_properties matches by NAME, and we want a wide
# sample to filter by TYPE. Cheap -- each is one round trip and the cap does the bounding.
QUERIES = ["a", "e", "i", "o", "s", "t", "r", "n", "l", "c", "m", "d", "u", "g", "p", "b"]


def say(s):
    enc = sys.stdout.encoding or "utf-8"
    sys.stdout.write(str(s).encode(enc, "replace").decode(enc, "replace") + "\n")
    sys.stdout.flush()


def read_log(logdir, basename):
    p = logdir / (basename + "-0.log")
    if not p.is_file():
        raise SystemExit("a1: %s does not exist -- wrong process folder, or the DLL never loaded" % p)
    try:
        return p.read_text(encoding="utf-8", errors="replace").splitlines()
    except OSError as e:
        raise SystemExit(
            "a1: cannot read %s (%s). errno 13/32 means the DLL holds the log EXCLUSIVELY -- the "
            "build-3370 `_wfopen_s` defect. Rebuild with the Sein.cpp fix, or this rig would report "
            "'no measurement' about a log it never opened." % (p, e))


def main():
    if len(sys.argv) < 2:
        raise SystemExit(__doc__)
    proc = sys.argv[1]
    logdir = pathlib.Path.home() / "AppData/Local/UE5CEDumper/Logs" / proc
    fails, notes = [], []

    with PipeClient() as c:
        say("build answering the pipe: %s" % c.assert_build())
        p = c.ensure_scanned()
        ue = p.get("ue_version")
        say("UE version = %s   objects = %s   module = %s"
            % (ue, p.get("object_count"), p.get("module_name")))
        if (p.get("object_count") or 0) < 1000:
            fails.append("A1: object_count %r -- the engine did not boot" % p.get("object_count"))
        if p.get("module_name", "").split(".")[0] != proc:
            notes.append("the pipe is answering for %r but logs were read from %r"
                         % (p.get("module_name"), proc))

        # ⛔ SOFT AND LAZY DO NOT SHARE A TAGGED ENVELOPE, and assuming they do is how this rig
        # first scored a CORRECT DLL as a FAIL on OCTOPATH. `PersistentPtrEnvelopeFor` takes the
        # tagged value as a PARAMETER (Grimoire.h), and the two call sites pass different ones:
        #   SoftPathOffset  -> 0x10   FWeakObjectPtr(8) + Tag(4) + pad(4), because FSoftObjectPath
        #                             is 8-aligned (it holds an FString)
        #   LazyGuidOffset  -> 0x0C   FWeakObjectPtr(8) + Tag(4) and NO pad, because
        #                             FUniqueObjectGuid is a bare FGuid (4x uint32, alignof 4)
        # `Ubel.cpp:426-428` says it out loud: "There is no era in which 0x10 is correct here."
        # Both collapse to 0x08 from 5.3, where TagAtLastTest was deleted.
        untagged = (ue or 0) >= 503
        expect = {"TSoftObjectPtr": 0x08 if untagged else 0x10,
                  "TLazyObjectPtr": 0x08 if untagged else 0x0C}
        say("expected envelopes for UE %s: soft +0x%02X, lazy +0x%02X   (%s)"
            % (ue, expect["TSoftObjectPtr"], expect["TLazyObjectPtr"],
               "untagged, 5.3+" if untagged else "tagged, pre-5.3 -- NEGATIVE CONTROL"))

        # --- find soft/lazy properties BY TYPE -----------------------------------
        soft, lazy = {}, {}
        seen_rows = 0
        for q in QUERIES:
            r = c.request("search_properties", query=q, limit=20000, game_only=False, timeout=240)
            rows = r.get("results") or r.get("matches") or r.get("properties") or []
            seen_rows += len(rows)
            for x in rows:
                t = x.get("prop_type") or ""
                inner = x.get("inner_type") or ""
                if t in SOFT_TYPES or inner in SOFT_TYPES:
                    soft.setdefault(x.get("class_addr"), x)
                if t in LAZY_TYPES or inner in LAZY_TYPES:
                    lazy.setdefault(x.get("class_addr"), x)
            if len(soft) >= 12 and len(lazy) >= 3:
                break
        say("\nproperty rows sampled: %d   soft-owning classes: %d   lazy-owning classes: %d"
            % (seen_rows, len(soft), len(lazy)))
        for x in list(soft.values())[:4]:
            say("   SOFT  %-34s . %-28s %s" % (x.get("class_name"), x.get("prop_name"),
                                               x.get("prop_type")))
        for x in list(lazy.values())[:4]:
            say("   LAZY  %-34s . %-28s %s" % (x.get("class_name"), x.get("prop_name"),
                                               x.get("prop_type")))
        if not soft:
            fails.append("A1: no SoftObject/SoftClass property found -- the soft leg cannot be exercised here")
        if not lazy:
            notes.append("no LazyObjectProperty in this pool. The lazy envelope line can only appear "
                         "if a walk touches one, so its absence here is NOT a failure of the fix -- "
                         "it is a missing vehicle. Record the lazy leg as NOT EXERCISED on this host.")

        # --- walking is what makes the measurement fire ---------------------------
        walked = 0
        for addr in list(soft) + list(lazy):
            if not addr:
                continue
            try:
                c.request("walk_class", addr=str(addr).replace("0x", ""), timeout=120)
                walked += 1
            except Exception as e:  # noqa: BLE001 - a single bad class must not end the run
                notes.append("walk_class(%s) failed: %s" % (addr, e))
        say("classes walked to trigger the latch: %d" % walked)

    # --- the DLL-side observable ---------------------------------------------
    lines = read_log(logdir, "offsets")
    env = [l for l in lines if "payload envelope measured" in l]
    say("\n[offsets-0.log] 'payload envelope measured' lines: %d" % len(env))
    for l in env:
        say("   " + l.strip()[:180])

    got = {}
    for l in env:
        m = re.search(r"(\w+) payload envelope measured: \+0x([0-9A-Fa-f]+)", l)
        if m:
            got[m.group(1)] = int(m.group(2), 16)

    for what in ("TSoftObjectPtr", "TLazyObjectPtr"):
        want = expect[what]
        if what not in got:
            (fails if what == "TSoftObjectPtr" else notes).append(
                "A1: no '%s payload envelope measured' line -- nothing walked touched one "
                "(the latch is once per distinct envelope per process)" % what)
        elif got[what] != want:
            fails.append("A1: %s measured +0x%02X, expected +0x%02X for UE %s"
                         % (what, got[what], want, ue))
        else:
            say("   OK  %-15s +0x%02X  == expected" % (what, got[what]))

    changed = [l for l in env if "CHANGED" in l]
    if changed:
        notes.append("a measurement CHANGED mid-process (%d line(s)) -- two distinct envelopes in "
                     "one pool is worth reading before trusting either" % len(changed))

    say("\n================ A1 RESULT (%s, UE %s) ================" % (proc, ue))
    for n in notes:
        say("NOTE: " + n)
    if fails:
        say("FAIL (%d):" % len(fails))
        for f_ in fails:
            say("   - " + f_)
        return 1
    say("PASS -- every envelope that could be exercised on this host matched the derived expectation.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
