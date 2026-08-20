r"""AA2/AA3 step 3 -- CMD_LIST_INSTANCES must publish a REAL identity witness.

    py tools/verify/aa2_class_witness.py DumperTest

THE ROW asks for one line: `init-0.log` should show `LIST_INSTANCES ... classWitness=0x...`,
"a zero witness means the guard fell back". That is a mailbox command, so it normally needs
Cheat Engine -- but the mailbox is just a struct, and `mailbox_poke.py` already drives it from
Python. So this runs with NO CE at all.

  WHICH MAKES THE ROW'S OWN ASSERTION TOO BROAD, and the rig has to say so rather than emit a
  false FAIL. `Mimic.cpp` publishes the witness in `instanceAddr` for the EXACT scope only. In
  DERIVED scope (contract 3) the returned objects have many concrete classes, so one page-wide
  witness would make the caller refuse every write while reporting a healthy freeze -- exactly
  the AA2 defect inverted. There the witness is deliberately **0** and travels PER ENTRY inside
  `paramsData` instead. So `classWitness=0x0` is a defect on an exact listing and CORRECT on a
  derived one, and reading the log line alone cannot tell them apart.

WHAT IS CHECKED, and by what independent detector
  A  EXACT scope, a class with live instances: witness non-zero, AND `*(obj + classOffset)`
     read out of the process with ReadProcessMemory equals it, for EVERY returned object.
     The DLL is not believed about the witness; the witness is checked against the objects.
  B  the zero is a CLEAR, not a leftover: `instanceAddr` is poisoned with a recognisable
     value before the trigger. A published 0 must therefore have been written. Without this,
     "0 means fell back" and "0 means nobody touched the field" are the same observation.
  C  DERIVED scope: witness 0 by design, `ufuncAddr` carries the class offset, entries are 16
     bytes, and each per-entry UClass* matches that object's real ClassPrivate. Asserted only
     after confirming the page spans MORE THAN ONE concrete class -- otherwise per-entry
     witnesses would be trivially satisfiable and the check vacuous.
  D  `cmdFlags` is cleared by the handler, so a derived request cannot widen the NEXT caller's
     command. Checked by reading the field back AND by re-running with no flag and confirming
     the reply is exact-shaped again. This is the whole contract-1/2 compatibility story.
  E  the log line the row actually names, matched by FORMAT STRING.

The class offset used for the independent read is the one the DLL published, so a self-consistent
lie is conceivable in principle -- but a WRONG offset cannot make hundreds of unrelated objects
all read back the same pointer, and check C's multi-class requirement makes it read back the
RIGHT DIFFERENT pointer per object. That is what rules it out.
"""
import argparse
import pathlib
import struct
import sys
import time

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
from mailbox_poke import (Mem, pid_of, mailbox_addr, OFF_CMD, OFF_STATUS, OFF_RESULT,
                          OFF_INITSTATE, OFF_INSTANCE, OFF_UFUNC, OFF_ERRORMSG,
                          OFF_PARAMS, STATUS_IDLE, STATUS_DONE, STATUS_PROCESSING,
                          CMD_IDLE, INIT_READY)

OFF_PARMSSIZE, OFF_NUMPARMS, OFF_FUNCFLAGS = 0x020, 0x022, 0x024
OFF_CLASSNAME, OFF_CMDFLAGS, OFF_CMDOUTFLAGS = 0x028, 0x728, 0x72C
CMD_LIST_INSTANCES = 6
LI_IN_DERIVED, LI_OUT_TRUNCATED = 0x1, 0x1
POISON = 0xDEADBEEFCAFEF00D
LOGDIR = pathlib.Path.home() / "AppData/Local/UE5CEDumper/Logs"


def say(s):
    enc = sys.stdout.encoding or "utf-8"
    sys.stdout.write(str(s).encode(enc, "replace").decode(enc, "replace") + "\n")
    # Flush: a backgrounded rig's stdout is a FILE, which Python block-buffers --
    # a long run then shows an EMPTY output file and looks hung.
    sys.stdout.flush()


def list_instances(m, base, cls, derived=False, page=0, timeout=20.0):
    st = m.i32(base + OFF_STATUS)
    if st == STATUS_PROCESSING:
        raise SystemExit("mailbox is already PROCESSING -- a previous command is wedged")
    m.write(base + OFF_STATUS, struct.pack("<i", STATUS_IDLE))
    name = cls.encode("utf-8")[:255]
    m.write(base + OFF_CLASSNAME, name + b"\x00" * (256 - len(name)))
    m.write(base + OFF_PARAMS, struct.pack("<I", page) + b"\x00" * 28)
    m.write(base + OFF_CMDFLAGS, struct.pack("<I", LI_IN_DERIVED if derived else 0))
    m.write(base + OFF_CMDOUTFLAGS, struct.pack("<I", 0xFFFFFFFF))   # poison the OUT flags too
    # B: poison BOTH witness fields. A 0 read back afterwards is then proof the DLL
    #    wrote it, not proof that nobody did.
    m.write(base + OFF_INSTANCE, struct.pack("<Q", POISON))
    m.write(base + OFF_UFUNC, struct.pack("<Q", POISON))
    m.write(base + OFF_RESULT, struct.pack("<i", 0x7FFFFFFF))
    m.write(base + OFF_CMD, struct.pack("<i", CMD_LIST_INSTANCES))   # trigger LAST
    t0 = time.time()
    while time.time() - t0 < timeout:
        if m.i32(base + OFF_STATUS) == STATUS_DONE:
            out = dict(
                result=m.i32(base + OFF_RESULT),
                witness=m.u64(base + OFF_INSTANCE),
                clsoff=m.u64(base + OFF_UFUNC),
                total=struct.unpack("<H", m.read(base + OFF_PARMSSIZE, 2))[0],
                returned=struct.unpack("<H", m.read(base + OFF_NUMPARMS, 2))[0],
                pages=struct.unpack("<I", m.read(base + OFF_FUNCFLAGS, 4))[0],
                outflags=struct.unpack("<I", m.read(base + OFF_CMDOUTFLAGS, 4))[0],
                inflags_after=struct.unpack("<I", m.read(base + OFF_CMDFLAGS, 4))[0],
                blob=m.read(base + OFF_PARAMS, 1024),
                err=m.read(base + OFF_ERRORMSG, 256).split(b"\x00")[0].decode("utf-8", "replace"),
                ms=(time.time() - t0) * 1000.0)
            m.write(base + OFF_CMD, struct.pack("<i", CMD_IDLE))
            return out
        time.sleep(0.004)
    raise SystemExit("TIMEOUT: status=%#x. 0 = the DLL never picked it up (stale mailbox "
                     "address); 0xFF = it took the command and wedged."
                     % m.i32(base + OFF_STATUS))


def logs_since(proc, mark, needle):
    out = []
    for f in sorted((LOGDIR / proc).glob("*-0.log")):
        try:
            for l in f.read_text(encoding="utf-8", errors="replace").splitlines():
                if l.startswith("[") and l[1:20] >= mark and needle in l:
                    out.append(l)
        except OSError:
            pass
    return out


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("process", nargs="?", default="DumperTest")
    ap.add_argument("--exact-class", default="DumperTestActor")
    ap.add_argument("--derived-class", default="Actor")
    a = ap.parse_args()
    fails = []

    pid = int(a.process) if a.process.isdigit() else pid_of(a.process)
    base = mailbox_addr(a.process)
    m = Mem(pid)
    init = m.i32(base + OFF_INITSTATE)
    say("pid %d  mailbox %#x  initState=%d (%s)"
        % (pid, base, init, "READY" if init == INIT_READY else "NOT READY"))
    if init != INIT_READY:
        raise SystemExit("initState is not READY -- a timeout below would mean nothing")
    mark = time.strftime("%Y-%m-%d %H:%M:%S")
    time.sleep(1.1)

    # ------------------------------------------------------------------ A + B
    say("")
    say("== A: EXACT scope on %r -- the witness must be real ==" % a.exact_class)
    r = list_instances(m, base, a.exact_class, derived=False)
    say("   result=%d returned=%d/%d pages=%d  %.1f ms%s"
        % (r["result"], r["returned"], r["total"], r["pages"], r["ms"],
           ("  err=%r" % r["err"]) if r["err"] else ""))
    say("   classWitness = %#x" % r["witness"])
    say("   classOffset  = %#x  (UObject::ClassPrivate)" % r["clsoff"])
    if r["witness"] == POISON or r["clsoff"] == POISON:
        fails.append("A/B: the DLL never WROTE the witness fields -- the poison survived, so "
                     "any value read here is the caller's own and means nothing")
    elif r["returned"] == 0:
        fails.append("A: no live instances of %r -- the witness check would be vacuous"
                     % a.exact_class)
    elif r["witness"] == 0:
        fails.append("A: classWitness is 0 on an EXACT listing with %d live instance(s) -- "
                     "this is the fallback the row is about" % r["returned"])
    else:
        objs = [struct.unpack_from("<Q", r["blob"], i * 8)[0] for i in range(r["returned"])]
        bad = []
        for o in objs:
            real = m.u64(o + r["clsoff"])
            if real != r["witness"]:
                bad.append((o, real))
        say("   independently read *(obj + %#x) for all %d object(s): %d mismatch(es)"
            % (r["clsoff"], len(objs), len(bad)))
        for o, real in bad[:4]:
            say("      obj %#x -> %#x  != witness" % (o, real))
        if bad:
            fails.append("A: %d/%d objects' real ClassPrivate does not equal the published "
                         "witness" % (len(bad), len(objs)))
        else:
            say("   OK: every returned object really IS of the class the witness names")

    # ------------------------------------------------------------------ C
    say("")
    say("== C: DERIVED scope on %r -- witness 0 by DESIGN, per-entry instead ==" % a.derived_class)
    rd = list_instances(m, base, a.derived_class, derived=True)
    say("   result=%d returned=%d/%d pages=%d truncated=%s  %.1f ms"
        % (rd["result"], rd["returned"], rd["total"], rd["pages"],
           bool(rd["outflags"] & LI_OUT_TRUNCATED), rd["ms"]))
    say("   classWitness = %#x   <-- must be 0 here, and that is CORRECT" % rd["witness"])
    say("   classOffset  = %#x" % rd["clsoff"])
    if rd["outflags"] == 0xFFFFFFFF:
        fails.append("C: cmdOutFlags was never rewritten -- the truncation bit cannot be trusted")
    if rd["witness"] != 0:
        fails.append("C: derived scope published a page-wide witness (%#x); a caller would "
                     "refuse every instance whose class is not that one" % rd["witness"])
    if rd["clsoff"] != r["clsoff"] or rd["clsoff"] == 0:
        fails.append("C: class offset disagrees between scopes (%#x vs %#x)"
                     % (rd["clsoff"], r["clsoff"]))
    ents = [struct.unpack_from("<QQ", rd["blob"], i * 16) for i in range(rd["returned"])]
    distinct = {c for _, c in ents}
    say("   entries=%d  distinct concrete classes on this page=%d" % (len(ents), len(distinct)))
    if len(ents) == 0:
        fails.append("C: derived sweep returned nothing")
    elif len(distinct) < 2:
        fails.append("C: only %d distinct class(es) on the page -- per-entry witnesses would be "
                     "trivially satisfied, so this check would be vacuous" % len(distinct))
    else:
        badc = [(o, c, m.u64(o + rd["clsoff"])) for o, c in ents if m.u64(o + rd["clsoff"]) != c]
        zero = [o for o, c in ents if c == 0]
        say("   per-entry witness vs real ClassPrivate: %d mismatch(es), %d zero witness(es)"
            % (len(badc), len(zero)))
        for o, c, real in badc[:4]:
            say("      obj %#x  witness %#x  real %#x" % (o, c, real))
        if badc:
            fails.append("C: %d entry witness(es) do not match the object's real class" % len(badc))
        if zero:
            fails.append("C: %d entry(ies) carry a ZERO witness -- those writes go unguarded"
                         % len(zero))
        if not badc and not zero:
            say("   OK: %d objects across %d classes, every witness matches memory"
                % (len(ents), len(distinct)))

    # ------------------------------------------------------------------ D
    say("")
    say("== D: cmdFlags must be CLEARED, so a derived request cannot widen the next command ==")
    say("   cmdFlags read back after the derived call: %#x   <-- must be 0" % rd["inflags_after"])
    if rd["inflags_after"] != 0:
        fails.append("D: cmdFlags survived the handler (%#x) -- the next contract-1/2 caller "
                     "would silently get a derived sweep" % rd["inflags_after"])
    r2 = list_instances(m, base, a.derived_class, derived=False)
    say("   immediate re-run with NO flag: returned=%d/%d witness=%#x"
        % (r2["returned"], r2["total"], r2["witness"]))
    if rd["total"] > 0 and r2["total"] >= rd["total"]:
        fails.append("D: the unflagged re-run returned %d, not fewer than the derived %d -- "
                     "it looks like it stayed derived" % (r2["total"], rd["total"]))
    else:
        say("   OK: exact (%d) is a strict subset of derived (%d), so the flag did not stick"
            % (r2["total"], rd["total"]))

    # ------------------------------------------------------------------ E
    say("")
    say("== E: the log line the row actually names ==")
    time.sleep(0.4)
    hits = logs_since(a.process, mark, "LIST_INSTANCES returned")
    say("   'LIST_INSTANCES returned ... classWitness=' lines: %d" % len(hits))
    for l in hits[-4:]:
        say("      " + l.strip()[:170])
    if not hits:
        fails.append("E: the DLL logged no LIST_INSTANCES line at all")

    say("")
    for x in fails:
        say("FAIL: %s" % x)
    if not fails:
        say("PASS (A, B, C, D, E)")
    return 1 if fails else 0


if __name__ == "__main__":
    sys.exit(main())
