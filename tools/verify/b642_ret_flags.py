r"""b642 — a UFunction's return must be described CONSISTENTLY by two independent reads.

    py tools/verify/b642_ret_flags.py [--min 200]     (a host running + injected)

TWO READS, AND THAT IS THE WHOLE DESIGN. The DLL learns about a return twice, from different
places:

    ret_offset   <- UFunction::ReturnValueOffset      (a field on the UFunction, 0xFFFF = void)
    params[].ret <- CPF_ReturnParm off PropertyFlags  (a bit on the parameter itself)

They must agree on every function. A disagreement is detectable without any reference build, which
matters because **there is no pre-fix binary to run** — this is a two-detector cross-check, not a
controlled experiment, and the write-up must say so rather than claiming a negative control it
does not have. What can be said is that a pre-fix DLL, which never set the per-param flag, would
fail assertion (b) on every non-void function on the host.

ASSERTIONS
  (a) SANITY GATE FIRST. Skip `num_parms==0 && parms_size==0 && ret_offset==0xFFFF` — that is also
      exactly what a FAILED funcFlagsOff probe looks like, so counting it as a passing "void
      function" would let a broken probe read as a clean sweep. The skip RATE is itself a finding.
  (b) `ret_offset != 0xFFFF` ⇒ exactly one param with `ret==true`, at `offset == ret_offset`, and a
      non-empty function-level `ret` string.
  (c) `ret_offset == 0xFFFF` ⇒ zero `ret==true` params and an empty function-level `ret`.
  (d) never more than one `ret==true` per function.
  (e) count functions with any `out==true` param. Zero across a whole host is the same-root-cause
      smell — `CPF_OutParm` (0x100) also lives in the low 32 bits — so it is FLAGGED, not failed.

⚠ `get_offsets` is recorded for PROVENANCE ONLY. Do not assert `flags == elemsize + 4`: that
relation is true by construction in all three writers and can never fail, so asserting it would
look like a check and be a tautology.
"""
import argparse
import json
import pathlib
import sys

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
sys.stdout.reconfigure(encoding="utf-8", errors="replace")

from pipe_client import PipeClient  # noqa: E402

VOID = 0xFFFF
WANT = ["KismetMathLibrary", "KismetSystemLibrary", "KismetStringLibrary", "Actor", "Pawn",
        "PlayerController", "CharacterMovementComponent", "GameplayStatics"]
OUTDIR = pathlib.Path("out/b642")


def say(s):
    enc = sys.stdout.encoding or "utf-8"
    sys.stdout.write(str(s).encode(enc, "replace").decode(enc, "replace") + chr(10))
    sys.stdout.flush()


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--min", type=int, default=200)
    a = ap.parse_args()

    violations, skipped, checked, with_out = [], 0, 0, 0
    # Vacuity guards: a clean sweep means nothing if only one branch ever ran, or if every
    # return happened to sit at offset 0 (where a wrong ret_offset would still compare equal).
    branch_b, branch_c, nonzero_ret = 0, 0, 0
    per_class = {}

    with PipeClient() as c:
        c.assert_build()
        c.ensure_scanned()

        off = c.request("get_offsets")
        od = off.get("data", off)
        prov = {k: od.get(k) for k in
                ("use_fproperty", "fproperty_flags", "fproperty_elemsize",
                 "uproperty_flags", "uproperty_elemsize") if k in od}
        say("provenance (NOT asserted): %s" % json.dumps(prov))

        # ---- pick the classes -------------------------------------------------
        lc = c.request("list_classes", game_only=False, limit=50000)
        ld = lc.get("data", lc)
        by_name = {}
        for row in (ld.get("classes") or ld.get("results") or []):
            by_name.setdefault(row.get("class_name"), row.get("class_addr"))
        targets = {n: by_name[n] for n in WANT if n in by_name}
        say("named classes found: %d of %d" % (len(targets), len(WANT)))

        laf = c.request("list_all_functions", game_only=False, limit=300000)
        lafd = laf.get("data", laf)
        if lafd.get("truncated") or lafd.get("aborted"):
            say("⚠ list_all_functions was capped — the >20-function class list is a sample, "
                "not a census. The per-class assertions below are still valid.")
        counts = {}
        for f in (lafd.get("results") or lafd.get("functions") or []):
            key = (f.get("class_name"), f.get("class_addr"))
            counts[key] = counts.get(key, 0) + 1
        for (cn, ca), n in sorted(counts.items(), key=lambda kv: -kv[1]):
            if n > 20 and cn not in targets and ca:
                targets[cn] = ca
            if len(targets) >= 40:
                break
        say("walking %d class(es)" % len(targets))

        # ---- the sweep --------------------------------------------------------
        for cname, caddr in targets.items():
            w = c.request("walk_functions", addr=caddr)
            fns = w.get("functions") or []
            per_class[cname] = len(fns)
            for fn in fns:
                name = fn.get("name")
                ro = int(fn.get("ret_offset") if fn.get("ret_offset") is not None else VOID)
                nparms = int(fn.get("num_parms") or 0)
                psize = int(fn.get("parms_size") or 0)
                rettype = (fn.get("ret") or "").strip()
                params = fn.get("params") or []

                # (a) sanity gate
                if nparms == 0 and psize == 0 and ro == VOID:
                    skipped += 1
                    continue
                checked += 1

                rets = [p for p in params if p.get("ret")]
                if any(p.get("out") for p in params):
                    with_out += 1

                # (d)
                if len(rets) > 1:
                    violations.append("%s::%s has %d params flagged ret==true"
                                      % (cname, name, len(rets)))
                    continue

                if ro != VOID:
                    branch_b += 1
                    if ro != 0:
                        nonzero_ret += 1
                    # (b)
                    if len(rets) != 1:
                        violations.append(
                            "%s::%s ret_offset=%d but %d param(s) carry ret==true — the two reads "
                            "disagree (this is what a pre-fix DLL does on EVERY non-void function)"
                            % (cname, name, ro, len(rets)))
                    # ⚠ NOT `.get("offset") or -1`: offset 0 is legitimate and extremely common
                    # (every no-arg getter returns at 0), and `0 or -1` is -1. That bug
                    # produced 100+ confident false violations on its first run.
                    elif (rets[0].get("offset") if rets[0].get("offset") is not None
                          else -1) != ro:
                        violations.append(
                            "%s::%s ret_offset=%d but the ret param sits at %s"
                            % (cname, name, ro, rets[0].get("offset")))
                    if not rettype:
                        violations.append("%s::%s ret_offset=%d but the function-level ret string "
                                          "is empty" % (cname, name, ro))
                else:
                    branch_c += 1
                    # (c)
                    if rets:
                        violations.append("%s::%s is void (ret_offset=0xFFFF) but %d param(s) "
                                          "carry ret==true" % (cname, name, len(rets)))
                    if rettype:
                        violations.append("%s::%s is void but the function-level ret string is %r"
                                          % (cname, name, rettype))

    say("")
    say("checked %d function(s); skipped %d by the sanity gate (%.1f%%)"
        % (checked, skipped, 100.0 * skipped / max(checked + skipped, 1)))
    say("functions with at least one out param: %d" % with_out)
    say("branch (b) non-void: %d   branch (c) void: %d   of those, ret_offset != 0: %d"
        % (branch_b, branch_c, nonzero_ret))
    if branch_b == 0 or branch_c == 0:
        say("   ⚠ FLAG: one branch never ran, so the sweep only exercised half the rule.")
    if nonzero_ret == 0 and branch_b:
        say("   ⚠ FLAG: every non-void return sits at offset 0, where a WRONG ret_offset would "
            "still compare equal — the offset assertion is vacuous on this host.")
    if with_out == 0:
        say("   ⚠ FLAG (not a failure): zero out-params across the whole host. CPF_OutParm (0x100) "
            "is also in the low 32 bits, so this is the same-root-cause smell b642 is about.")
    if skipped > checked:
        say("   ⚠ FLAG: more functions were skipped than checked — a failed funcFlagsOff probe "
            "looks exactly like this, so treat the pass below with suspicion.")

    OUTDIR.mkdir(parents=True, exist_ok=True)
    stem = "dumpertest"
    (OUTDIR / (stem + ".json")).write_text(json.dumps(
        {"checked": checked, "skipped": skipped, "with_out": with_out,
         "violations": violations, "per_class": per_class, "provenance": prov},
        indent=1), encoding="utf-8")
    say("wrote %s" % (OUTDIR / (stem + ".json")))

    say("")
    if violations:
        say("b642: FAIL — %d violation(s)" % len(violations))
        for v in violations[:25]:
            say("   - %s" % v)
        return 1
    if checked < a.min:
        say("b642: INCONCLUSIVE — only %d function(s) checked, below the --min of %d. A clean "
            "sweep over a handful is not evidence about the host." % (checked, a.min))
        return 2
    say("b642: PASS — %d functions, both reads agree on every one." % checked)
    say("⚠ This is a TWO-DETECTOR CROSS-CHECK, not a controlled experiment: no pre-fix binary "
        "exists to run, so it was never shown able to fail on this machine. What can be said is "
        "that a pre-fix DLL would violate (b) on every non-void function here.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
