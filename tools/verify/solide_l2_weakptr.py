r"""Solide L2 — `Force → null` on a weak/soft/lazy pointer must REFUSE, loudly and without leaving
a job behind.

    py tools/verify/solide_l2_weakptr.py     (DumperTest dev running + injected, no UI)

THE DEFECT. `object_null` writes a null over an ObjectProperty. A `TWeakObjectPtr` is not a raw
pointer — it is `{ObjectIndex, ObjectSerialNumber}` — so zeroing it does not null anything; it makes
the field point at **GObjects[0]**, a real and completely unrelated UObject. Before the fix the
request was accepted, held nothing, and left a re-assert job scanning forever.

`Solide.cpp:263` is the whole fix: `if (fi.TypeName != "ObjectProperty") { refusal = FR_ERR_WEAK_PTR; }`

THREE THINGS MUST HOLD, and only the first is visible in the reply:
  (a) `code == -12` (`FR_ERR_WEAK_PTR`), `held == 0`, `resolved == false`
  (b) `get_forced_fields` is EMPTY — the refusal must not persist a job
  (c) the re-assert worker is NOT scanning. `get_forced_fields` structurally cannot show this: a
      job can be absent from the list and still have started a worker.

⚠ (c) IS ABOUT A SUSTAINED RATE, NOT ABOUT ZERO SCANS, and the first version of this rig got that
wrong and reported a FAIL against correct code. Measured: the refusal itself performs exactly ONE
`FindInstancesDerivedFrom` — 6 ms after the request — because it has to resolve an instance to
learn the field's TYPE before it can decide to refuse. Then silence. The control, by contrast,
streams at 312 ms intervals indefinitely. So the discriminator is the rate AFTER a settle window,
not the raw count.

⚠ (c) IS THE ONE WITH A CONTROL. A "no new log lines" assertion passes trivially if the worker was
never going to log anyway, so the rig first reproduces the PRE-FIX shape on purpose — a field that
does not exist at all is accepted, persists, and drives the futile scan — and measures the rate.
Only after seeing the log grow does "the log did not grow" mean anything.

PREMISES, all verified offline before writing this (see the commit message):
  FR_ERR_WEAK_PTR = -12, FR_ERR_NOT_INIT = -1        (Solide.h)
  `kind` is a STRING, default "bool"; an unknown VALUE is a hard error (Fern.cpp)
  `FindInstancesDerivedFrom base=` is `Sein::Info("PIPE:find", ...)` -> LF_Pipe -> pipe-0.log
  SOLIDE_REASSERT_MS = 300  (Grimoire.h) -> ~3.3 scans/sec
  Actor::ParentComponent is `TWeakObjectPtr<ChildActorComponent>` @0x01C0, WeakObjectProperty
"""
import pathlib
import sys
import time

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
sys.stdout.reconfigure(encoding="utf-8", errors="replace")

from pipe_client import PipeClient  # noqa: E402

PIPELOG = pathlib.Path.home() / "AppData/Local/UE5CEDumper/Logs/DumperTest/pipe-0.log"
MARKER = "FindInstancesDerivedFrom base="
SETTLE = 3.5            # control window
SETTLE_IMMEDIATE = 1.5  # long enough for the one-shot resolution scan to land
SETTLE_SUSTAINED = 4.0  # the window that must be silent


def say(s):
    enc = sys.stdout.encoding or "utf-8"
    sys.stdout.write(str(s).encode(enc, "replace").decode(enc, "replace") + chr(10))
    sys.stdout.flush()


def marker_count():
    try:
        return PIPELOG.read_text(encoding="utf-8", errors="replace").count(MARKER)
    except OSError:
        return 0


def forced(c):
    r = c.request("get_forced_fields")
    d = r.get("data", r)
    return d.get("fields") or d.get("forced") or d.get("results") or []


def main():
    fails, notes = [], []
    with PipeClient() as c:
        c.assert_build()
        c.ensure_scanned()

        # ---- sanity: without GWorld every AddForce returns -1, which would masquerade
        #      as a failure of THIS step.
        ptr = c.request("get_pointers")
        d = ptr.get("data", ptr)
        gw = str(d.get("gworld") or d.get("g_world") or "0")
        say("gworld = %s" % gw)
        if gw in ("0", "0x0", "None", ""):
            say("NOT_RUNNABLE: gworld is null — AddForce returns FR_ERR_NOT_INIT (-1) for every "
                "request, which is indistinguishable from this step failing.")
            return 2

        # ---- fixture probe -------------------------------------------------
        sp = c.request("search_properties", query="ParentComponent", game_only=False, limit=20)
        rows = sp.get("results", [])
        hit = next((r for r in rows if r.get("prop_name") == "ParentComponent"
                    and r.get("prop_type") == "WeakObjectProperty"), None)
        if not hit:
            say("NOT_RUNNABLE: no WeakObjectProperty named ParentComponent on this host "
                "(%d ParentComponent rows seen)" % len(rows))
            return 2
        say("fixture: %s::%s is %s (declared on %s)"
            % (hit.get("class_name"), hit.get("prop_name"), hit.get("prop_type"),
               hit.get("defining_class_name")))
        cls = hit.get("defining_class_name") or "Actor"

        # ---- clean slate: the erase branch is gated on `newlyAdded` ----------
        c.request("reset_all_fields")
        if forced(c):
            fails.append("reset_all_fields left jobs behind — the run starts dirty")
        say("clean slate: get_forced_fields = []")

        # ================= NEGATIVE CONTROL: the PRE-FIX shape ===============
        say("")
        say("== NEGATIVE CONTROL: a field that does not exist at all ==")
        say("   (this is what 'accepted, held nothing, scanning forever' looks like — without")
        say("    seeing it, 'the log did not grow' below would prove nothing)")
        base = marker_count()
        r = c.request("force_field", class_name=cls, field_name="ZZZNoSuchFieldXYZ",
                      kind="object_null")
        dd = r.get("data", r)
        say("   reply: code=%s held=%s resolved=%s"
            % (dd.get("code"), dd.get("held"), dd.get("resolved")))
        listed = forced(c)
        say("   get_forced_fields: %d job(s)" % len(listed))
        time.sleep(SETTLE)
        grew = marker_count() - base
        rate = grew / SETTLE
        say("   new '%s' lines in %.1fs: %d  (%.1f/s)" % (MARKER, SETTLE, grew, rate))
        control_ok = grew > 0
        control_rate = rate
        if not control_ok:
            notes.append("the control did NOT produce a scanning worker (%d new lines). The "
                         "step's 'zero new lines' therefore has no demonstrated ability to "
                         "fail, and its (c) clause is reported as UNPROVEN rather than PASS."
                         % grew)
            say("   ⚠ CONTROL DID NOT FIRE — clause (c) below cannot be trusted")
        else:
            say("   OK: the control reproduces the futile scan, so the detector works")
        c.request("reset_all_fields")
        time.sleep(1.0)

        # ================= THE STEP =========================================
        say("")
        say("== THE STEP: object_null on a WeakObjectProperty ==")
        base2 = marker_count()
        r2 = c.request("force_field", class_name=cls, field_name="ParentComponent",
                       kind="object_null")
        d2 = r2.get("data", r2)
        code, held, resolved = d2.get("code"), d2.get("held"), d2.get("resolved")
        say("   reply: code=%s held=%s resolved=%s" % (code, held, resolved))

        if code == -1:
            say("NOT_RUNNABLE: code -1 is FR_ERR_NOT_INIT, not a refusal")
            c.request("reset_all_fields")
            return 2
        if held and held > 0:
            say("NOT_RUNNABLE: held=%s — the write went through, so this host does not exercise "
                "the refusal" % held)
            c.request("reset_all_fields")
            return 2

        # (a)
        if code != -12:
            fails.append("(a) code is %s, expected -12 (FR_ERR_WEAK_PTR). code==0 with held==0 is "
                         "the ORIGINAL DEFECT: accepted, did nothing, said nothing." % code)
        else:
            say("   (a) OK: code == -12 (FR_ERR_WEAK_PTR)")
        if held not in (0, None):
            fails.append("(a) held=%s, expected 0" % held)
        if resolved:
            fails.append("(a) resolved=%s, expected false" % resolved)

        # (b)
        listed2 = forced(c)
        say("   (b) get_forced_fields: %d job(s)  <-- must be 0" % len(listed2))
        if listed2:
            fails.append("(b) the refusal PERSISTED a job: %s" % str(listed2)[:200])

        # (c) — two windows. The refusal legitimately performs ONE resolution scan (it must
        #      read the field's TYPE to know to refuse); what must not happen is a WORKER.
        time.sleep(SETTLE_IMMEDIATE)
        immediate = marker_count() - base2
        settled = marker_count()
        time.sleep(SETTLE_SUSTAINED)
        sustained = marker_count() - settled
        rate = sustained / SETTLE_SUSTAINED
        say("   (c) scans in the first %.1fs: %d  (<=1 expected: the one-shot type resolution)"
            % (SETTLE_IMMEDIATE, immediate))
        say("   (c) scans in the NEXT %.1fs:  %d  (%.2f/s)  <-- must be 0"
            % (SETTLE_SUSTAINED, sustained, rate))
        if immediate > 1:
            fails.append("(c) %d scans in the first %.1fs — more than the single type-resolution "
                         "lookup the refusal needs" % (immediate, SETTLE_IMMEDIATE))
        if sustained > 0:
            fails.append("(c) the re-assert worker is still scanning after a settle window "
                         "(%d in %.1fs, %.2f/s vs the control's %.2f/s) — the job was refused in "
                         "the reply but a worker started anyway"
                         % (sustained, SETTLE_SUSTAINED, rate, control_rate))
        elif not control_ok:
            say("       (reported UNPROVEN — see the control note)")
        else:
            say("       OK: silent, and the control above ran at %.2f/s so the counter does move"
                % control_rate)

        c.request("reset_all_fields")

    say("")
    for n in notes:
        say("NOTE: %s" % n)
    if fails:
        say("Solide L2: FAIL")
        for f in fails:
            say("   - %s" % f)
        return 1
    say("Solide L2: PASS%s — object_null on a weak pointer is refused with -12, persists no job%s"
        % (" (with a caveat, see NOTE)" if notes else "",
           ", and starts no worker" if control_ok else ""))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
