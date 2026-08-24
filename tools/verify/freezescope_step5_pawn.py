r"""FREEZESCOPE step 5, headless -- does the derived sweep reach THE PLAYER PAWN?

    py tools/verify/freezescope_step5_pawn.py

WHAT STEP 5 ASKS, AND WHY IT IS NOT RUN AS WRITTEN
  The register's step 5 is: "with bCanBeDamaged frozen to false, take damage on the
  player pawn -> the pawn is unharmed. Pre-fix the freeze held one incidental
  ChaosDebugDrawActor and the pawn died normally -- that is the whole finding."

  Taking damage needs a game with combat, a human to trigger it, and a human to judge
  "unharmed". Worse, it is verified in the installed UE 5.4 source that
  AActor::TakeDamage NEVER consults bCanBeDamaged, and UGameplayStatics::ApplyDamage
  merely forwards to it -- the only engine path honouring the flag is the radial-overlap
  filter OverlapActor->CanBeDamaged() (GameplayStatics.cpp:744). So a naive "hit the
  pawn" mutator can report the pawn dying WITH THE FREEZE WORKING PERFECTLY, i.e. a
  false FAIL. That is why the classification files step 5 under "needs care".

WHAT THIS RIG MEASURES INSTEAD
  The finding's actual mechanism, one layer below the damage: IS THE PAWN IN THE HELD
  SET AT ALL? Pre-fix the derived sweep held a single incidental actor and never wrote
  the pawn's byte. So read the pawn's OWN bCanBeDamaged bit out of the process with
  ReadProcessMemory -- before arming, while armed, and after release.

  This is an independent witness, not a re-reading of what the DLL reported: the address
  comes from teleport_get_pose, the offset and mask from search_properties, and the byte
  from the OS. Nothing here is derived from Solide's own `held` count, so a DLL that
  reports a large `held` while touching nothing still fails.

FREE BONUS -- THE 7 NEIGHBOUR BITS
  bCanBeDamaged is an FBoolProperty BIT (mask 0x04 on this build), so the write is a
  read-modify-write on a shared byte. The rig records all 8 bits, so a fix that clobbers
  the neighbours is visible rather than assumed -- the same concern AA1 raised.

SAFETY
  The force is released in the same run (reset_field then reset_all_fields) and the rig
  re-reads the byte afterwards, so a leak is measured rather than hoped for.
"""
import ctypes
import ctypes.wintypes as w
import pathlib
import subprocess
import sys
import time

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
from pipe_client import PipeClient  # noqa: E402

k32 = ctypes.WinDLL("kernel32", use_last_error=True)
k32.OpenProcess.restype = w.HANDLE
k32.ReadProcessMemory.argtypes = [w.HANDLE, w.LPCVOID, w.LPVOID,
                                  ctypes.c_size_t, ctypes.POINTER(ctypes.c_size_t)]
PROCESS_VM_READ, PROCESS_QUERY_INFORMATION = 0x0010, 0x0400

CLS, FIELD = "Actor", "bCanBeDamaged"


def say(s):
    enc = sys.stdout.encoding or "utf-8"
    sys.stdout.write(str(s).encode(enc, "replace").decode(enc, "replace") + "\n")
    sys.stdout.flush()


def pid_of(match):
    out = subprocess.run(["tasklist", "/FO", "CSV", "/NH"],
                         capture_output=True, text=True, errors="replace").stdout
    for line in out.splitlines():
        p = [x.strip('"') for x in line.split('","')]
        if len(p) >= 2 and match.lower() in p[0].lower():
            return int(p[1]), p[0]
    return None, None


def read_byte(h, addr):
    buf = (ctypes.c_ubyte * 1)()
    got = ctypes.c_size_t(0)
    if not k32.ReadProcessMemory(h, ctypes.c_void_p(addr), buf, 1, ctypes.byref(got)):
        return None
    return buf[0] if got.value == 1 else None


def bits(b):
    return "--------" if b is None else format(b, "08b")


def main():
    fails = []
    pid, name = pid_of("DumperTest")
    if not pid:
        raise SystemExit("step5: no DumperTest process -- launch it first")
    h = k32.OpenProcess(PROCESS_VM_READ | PROCESS_QUERY_INFORMATION, False, pid)
    if not h:
        raise SystemExit("step5: OpenProcess failed for pid %d" % pid)
    say("target: %s (pid %d)" % (name, pid))

    with PipeClient().connect() as c:
        say("DLL build %s" % c.assert_build())

        rows = c.request("search_properties", query=FIELD, limit=10).get("results") or []
        rows = [r for r in rows if r.get("prop_name") == FIELD]
        if not rows:
            raise SystemExit("step5: %s not found by search_properties" % FIELD)
        row = rows[0]
        off, mask = int(row["prop_offset"]), int(row["bool_mask"])
        say("field  : %s.%s  offset=+%d (0x%X)  bool_mask=0x%02X  inheritors=%s"
            % (row["defining_class_name"], FIELD, off, off, mask,
               row.get("inherited_by_count")))
        if row.get("defining_class_name") != CLS:
            fails.append("field is declared on %r, not Actor -- the derived-sweep premise "
                         "does not hold" % row.get("defining_class_name"))

        pose = c.request("teleport_get_pose")
        pawn = pose.get("pawn_addr")
        pawn = int(pawn, 16) if isinstance(pawn, str) else int(pawn or 0)
        if not pawn:
            raise SystemExit("step5: no pawn_addr from teleport_get_pose -- is a level loaded?")
        addr = pawn + off
        say("pawn   : 0x%X   byte under test: 0x%X" % (pawn, addr))
        say("")

        # ---------------------------------------------------------- BEFORE
        c.request("reset_all_fields")
        time.sleep(0.3)
        b0 = read_byte(h, addr)
        if b0 is None:
            raise SystemExit("step5: could not read the pawn byte -- refusing to guess")
        say("BEFORE   byte=0x%02X bits=%s   %s=%s"
            % (b0, bits(b0), FIELD, bool(b0 & mask)))
        if not (b0 & mask):
            fails.append("BEFORE: the bit is already CLEAR, so a later clear proves nothing. "
                         "This host cannot discriminate -- pick another field or host.")

        # ---------------------------------------------------------- ARMED
        fr = c.request("force_field", class_name=CLS, field_name=FIELD, kind="bool", on=False)
        say("         force_field -> ok=%s resolved=%s held=%s truncated=%s"
            % (fr.get("ok"), fr.get("resolved"), fr.get("held"), fr.get("truncated")))
        time.sleep(0.6)
        b1 = read_byte(h, addr)
        say("ARMED    byte=0x%02X bits=%s   %s=%s"
            % (b1 if b1 is not None else 0, bits(b1), FIELD,
               None if b1 is None else bool(b1 & mask)))

        # THE FINDING. Pre-fix the derived sweep held one incidental ChaosDebugDrawActor,
        # so the PAWN's bit stayed set no matter what `held` reported.
        if b1 is None or (b1 & mask):
            fails.append("*** STEP 5 FAILS: the pawn's own %s bit is STILL SET while the "
                         "derived force is armed (held=%s). The sweep is not reaching the "
                         "player pawn -- which is the entire finding." % (FIELD, fr.get("held")))
        else:
            say("         OK: the PAWN's own bit was cleared -- the derived sweep reached it")

        # the 7 neighbours must be untouched (AA1's concern, free here)
        if b1 is not None:
            if (b0 & ~mask & 0xFF) != (b1 & ~mask & 0xFF):
                fails.append("neighbour bits moved: 0x%02X -> 0x%02X outside mask 0x%02X"
                             % (b0, b1, mask))
            else:
                say("         OK: the other 7 bits of the byte are unchanged")

        # ---------------------------------------------------------- RELEASED
        c.request("reset_field", class_name=CLS, field_name=FIELD)
        c.request("reset_all_fields")
        time.sleep(0.6)
        b2 = read_byte(h, addr)
        say("RELEASED byte=0x%02X bits=%s   %s=%s"
            % (b2 if b2 is not None else 0, bits(b2), FIELD,
               None if b2 is None else bool(b2 & mask)))
        if b2 is None or not (b2 & mask):
            fails.append("RELEASE: the pawn's bit was not restored (0x%02X -> 0x%02X); "
                         "Solitar should put the captured base back"
                         % (b0, b2 if b2 is not None else 0))
        else:
            say("         OK: restored")
        left = c.request("get_forced_fields").get("fields") or []
        if left:
            fails.append("cleanup: %d field(s) still held" % len(left))

        # ------------------------------------------------ NEGATIVE CONTROLS
        # Without these the PASS above is just an observation: a DLL that wrote the bit
        # on every actor it could reach would look identical. Arming the SAME field via a
        # class the pawn does NOT derive from must leave the pawn's bit SET.
        #
        # StaticMeshActor is the one that matters: it resolves and holds ~30 REAL objects,
        # so it proves a NON-ZERO `held` does not imply the pawn is covered -- which is
        # the mistake the original finding was hiding behind. ChaosDebugDrawActor is here
        # by name because it is the incidental class the pre-fix freeze actually held.
        say("")
        say("negative controls -- arming the same field on classes the pawn is NOT under:")
        for cls in ("StaticMeshActor", "ChaosDebugDrawActor", "WorldSettings"):
            fr2 = c.request("force_field", class_name=cls, field_name=FIELD,
                            kind="bool", on=False)
            time.sleep(0.5)
            b3 = read_byte(h, addr)
            still = None if b3 is None else bool(b3 & mask)
            say("   %-20s resolved=%-5s held=%-4s -> pawn bit still set = %s"
                % (cls, fr2.get("resolved"), fr2.get("held"), still))
            if still is not True:
                fails.append("negative control %s: the pawn's bit went %r -- a class the "
                             "pawn does not derive from must not touch it, so the sweep is "
                             "over-broad and the positive result above means nothing"
                             % (cls, still))
            c.request("reset_field", class_name=cls, field_name=FIELD)
            c.request("reset_all_fields")
            time.sleep(0.4)

    say("")
    if fails:
        say("FAIL (%d)" % len(fails))
        for f in fails:
            say("  - %s" % f)
        return 1
    say("PASS -- the derived force reached the player pawn's own byte and released it")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
