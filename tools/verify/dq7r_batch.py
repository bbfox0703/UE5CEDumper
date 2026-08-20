r"""Three register items that all need ONE big, localized game — run in a single DQ7R launch.

    py tools/verify/dq7r_batch.py

DQ7R is the right host for all three and DumperTest is the wrong one for all three, each for its
own reason:

  U7                  needs a **localized** title, i.e. a `StrProperty` holding non-ASCII text over
                      50 bytes. Before the fix the whole search returned zero rows with an error;
                      success is *rows come back and the preview ends in an ellipsis*.
  Solide `capped`     needs a class with **more than 256 live instances**, because the assertion is
                      `held == 256 AND truncated == true` — the cap must be ADMITTED, not silently
                      applied. DumperTest's biggest actor pool is 58, so the branch is unreachable
                      there and `truncated=false` is merely *correct*, not informative.
  FREEZESCOPE step 4  the same problem: the step asks for a derived `LIST_INSTANCES` count "in the
                      hundreds/thousands, not 1/1", and on DumperTest the honest answer is 58.

⚠ TWO DQ7R PROCESSES EXIST (`DQ7R.exe` launcher + `DQ7R-Win64-Shipping.exe`). The proxy lives in the
shipping one, and `pid_of` refuses to guess between same-prefixed names, so the full stem is passed
everywhere.

⚠ Proxy mode starts the pipe server ONLY — no scan. `ensure_scanned` is what triggers and waits for
it; on a 149,370-object title that is not instant.

SAFETY: the force in part 2 is released in the same run (`reset_field`, then `reset_all_fields`) and
what remains held is reported afterwards, so a leak is visible rather than assumed.
"""
import json
import pathlib
import struct
import sys
import time

HERE = pathlib.Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
from pipe_client import PipeClient  # noqa: E402

PROC = "DQ7R-Win64-Shipping"
LOG = pathlib.Path.home() / "AppData/Local/UE5CEDumper/Logs" / PROC


def say(s):
    enc = sys.stdout.encoding or "utf-8"
    sys.stdout.write(str(s).encode(enc, "replace").decode(enc, "replace") + "\n")
    # Flush: a backgrounded rig's stdout is a FILE, which Python block-buffers --
    # a long run then shows an EMPTY output file and looks hung.
    sys.stdout.flush()


def u(s, n=60):
    if not isinstance(s, str):
        return repr(s)
    return "U+" + "/".join("%04X" % ord(c) for c in s[:n]) + ("…" if len(s) > n else "")


def logs(needle):
    out = []
    for f in sorted(LOG.glob("*-0.log")):
        try:
            out += [l for l in f.read_text(encoding="utf-8", errors="replace").splitlines()
                    if needle in l]
        except OSError:
            pass
    return out


def main():
    fails = []
    with PipeClient() as c:
        c.assert_build()
        say("scanning (proxy mode starts the pipe only) ...")
        t0 = time.time()
        st = c.ensure_scanned(timeout=600)
        say("scanned in %.0f s: %s" % (time.time() - t0, json.dumps(st)[:200]))

        # ------------------------------------------------------------ 1: U7
        say("")
        say("== U7: a non-ASCII StrProperty preview over 50 bytes ==")
        rows, scanned = [], 0
        for q in ("Name", "Text", "Desc", "Message", "Comment", "Title"):
            r = c.request("search_properties", query=q, game_only=False, limit=400)
            got = r.get("results") or []
            scanned += len(got)
            for x in got:
                pv = x.get("preview")
                if not isinstance(pv, str) or not pv:
                    continue
                if any(ord(ch) > 0x7F for ch in pv) and len(pv.encode("utf-8")) > 20:
                    rows.append((q, x))
        say("   searched 6 keywords, %d property rows; non-ASCII previews: %d" % (scanned, len(rows)))
        if not rows:
            say("   NOTE: no non-ASCII preview surfaced. The fix's success criterion is 'rows come")
            say("         back' (before it, the whole search errored to zero rows) -- and %d rows"
                % scanned)
            say("         did come back, so the hard-failure half is disproved. The ellipsis half")
            say("         needs a >50-byte CJK string that this keyword set did not reach.")
        else:
            trunc = [x for _, x in rows if str(x.get("preview", "")).rstrip().endswith("…")]
            for q, x in rows[:6]:
                pv = x["preview"]
                say("      [%s] %s.%s  %d bytes  ends-ellipsis=%s"
                    % (q, x.get("class_name"), x.get("prop_name"),
                       len(pv.encode("utf-8")), pv.rstrip().endswith("…")))
                say("           %s" % u(pv, 24))
            say("   previews ending in an ellipsis: %d" % len(trunc))
            if scanned == 0:
                fails.append("U7: the search returned zero rows -- the pre-fix hard failure")

        # ------------------------------------- 2 + 3: a class big enough to matter
        say("")
        say("== finding a class with >256 live instances (needed by BOTH remaining checks) ==")
        fi = c.request("find_instances", class_name="Actor", max_results=20000,
                       exact_match=False, class_histogram=True)
        hist = fi.get("class_histogram") or {}
        if isinstance(hist, dict):
            pairs = sorted(hist.items(), key=lambda kv: -int(kv[1]))
        else:
            pairs = sorted(((h.get("class_name"), h.get("count")) for h in hist),
                           key=lambda kv: -int(kv[1] or 0))
        say("   histogram entries: %d ; top:" % len(pairs))
        for k, v in pairs[:8]:
            say("      %-44s %s" % (k, v))
        big = next((k for k, v in pairs if int(v) > 256
                    and not str(k).startswith("Default__")), None)
        say("   chosen: %s" % big)

        say("")
        say("== FREEZESCOPE step 4: a DERIVED sweep must return hundreds, not 1/1 ==")
        base = "Actor"
        der = c.request("find_instances", class_name=base, max_results=20000, exact_match=False)
        say("   (pipe) find_instances substring '%s': total=%s" % (base, der.get("total")))
        say("   ⚠ that is the SUBSTRING count, not the derivation count -- the mailbox path is what")
        say("     the freeze uses. Run aa2_class_witness.py against this host for scope=derived.")

        # ------------------------------------------------------------ Solide capped
        say("")
        say("== Solide `capped`: held == 256 AND truncated == true ==")
        if not big:
            fails.append("no class with >256 live instances on this host either -- the cap branch "
                         "stays unreachable and this is a NOSAMPLE result, not a pass")
        else:
            w = c.request("walk_instance",
                          addr=next(i["addr"] for i in (fi.get("instances") or [])
                                    if i.get("class") == big), array_limit=2)
            flds = [f for f in (w.get("data", w).get("fields") or [])
                    if f.get("type") == "BoolProperty"]
            say("   %s has %d BoolProperty field(s); using %s"
                % (big, len(flds), flds[0].get("name") if flds else None))
            if not flds:
                fails.append("no BoolProperty on %s to force" % big)
            else:
                c.request("reset_all_fields")
                fr = c.request("force_field", class_name=big, field_name=flds[0]["name"],
                               kind="bool", on=False)
                say("   force_field -> ok=%s resolved=%s held=%s truncated=%s"
                    % (fr.get("ok"), fr.get("resolved"), fr.get("held"), fr.get("truncated")))
                held, trunc = fr.get("held"), fr.get("truncated")
                if held == 256 and trunc:
                    say("   ✅ at the cap AND says so")
                elif held == 256 and not trunc:
                    fails.append("Solide capped: held is exactly 256 but truncated=%r -- the cap "
                                 "was applied SILENTLY, which is the defect" % trunc)
                else:
                    say("   held=%s (<256): the cap branch was not reached by this class" % held)
                c.request("reset_field", class_name=big, field_name=flds[0]["name"])
                c.request("reset_all_fields")
                time.sleep(0.4)
                left = c.request("get_forced_fields").get("fields") or []
                say("   after reset: %d field(s) still held  <-- must be 0" % len(left))
                if left:
                    fails.append("cleanup: %d field(s) still held" % len(left))

    say("")
    for x in fails:
        say("FAIL: %s" % x)
    if not fails:
        say("PASS / recorded")
    return 1 if fails else 0


if __name__ == "__main__":
    sys.exit(main())
