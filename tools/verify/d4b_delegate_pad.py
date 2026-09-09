r"""D4b / D3b — every delegate shape must read correctly in BOTH build configurations.

    py tools/verify/d4b_delegate_pad.py            # DumperTest must be running + injected

⛔ RUN IT TWICE, ONCE PER CONFIGURATION. That is the entire point:

    py tools/verify/launch_dumpertest.py dev       # DO_CHECK on  -> pad 8
    py tools/verify/launch_dumpertest.py shipping  # DO_CHECK off -> pad 0

The SAME assertions must pass on both, from the same fixture source. One configuration alone
proves nothing here, because the defect was a build-dependent layout that happened to be
correct on the side every real title is built with.

THE DEFECT (D4b, found 2026-09-09 under D4's own fix; D3b is the same root cause).
UE 5.3 gave `TScriptDelegate` and `TMulticastScriptDelegate` a base class,
`TDelegateAccessHandlerBase<ThreadSafetyMode>`. With `DO_CHECK` on -- Debug, Development,
DebugGame -- that base holds one `FMRSWRecursiveAccessDetector`, whose only member is a
`std::atomic<uint64>`, so EVERY delegate payload starts 8 bytes late. With `DO_CHECK` off
(Test/Shipping) the base is empty and EBO applies, which is why the pre-5.3 constants had
never been caught: all ~12 titles measured in this repo are Shipping builds.

    Shipping / UE <= 5.2 : FMulticastScriptDelegate { TArray InvocationList @ +0x00 }
    Development (5.3+)   : FMulticastScriptDelegate { atomic State @ +0x00,
                                                      TArray InvocationList @ +0x08 }

Measured here 2026-09-09, the bound `OnActorHit` on DumperTest Development:

    +0x00  0000000000000000   atomic State
    +0x08  405CBFF5D5010000   Data = 0x1D5F5BF5C40
    +0x10  01000000           Num  = 1     <- the subscriber, invisible at the old +0x08
    +0x14  04000000           Max  = 4

Read at the old offsets that is `Data=0, Num=0xF5BF5C40` -- Num is the LOW HALF OF DATA, which
is the fingerprint to recognise this by. The DLL's own WARN line carried it for a day
(`implausible InvocationList Num=322437056`) before anyone decoded it.

⚠ WHY THE ARRAY ROW NEEDS ELEMENT [1] BOUND. `Arr_MulticastDelegates` had two EMPTY elements
until 2026-09-09, and with both empty a 16-byte stride and a 24-byte stride read the same
zeros and print the same `(0 bindings)`. D3b was literally unfalsifiable on that fixture.
`BeginPlay` now binds `[1]` and leaves `[0]` empty, so a reader stuck on 16 lands inside
element [0] and reports both as empty -- a visible, one-line difference.
"""
from __future__ import annotations

import json
import subprocess
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
ROOT = HERE.parent.parent
CLIENT = HERE / "pipe_client.py"

PROBE = "D4b_OnPingProbe"     # bound to Multicast_Inline, Del_Unicast and Arr_...[1]
HIT_PROBE = "D4_OnActorHitProbe"

# sizeof(FMulticastScriptDelegate) with no access detector: a bare TArray header.
MCD_BASE = 16


def call(cmd: str, args: dict) -> dict:
    r = subprocess.run([sys.executable, str(CLIENT), cmd, "--args", json.dumps(args)],
                       capture_output=True, text=True, encoding="utf-8", errors="replace",
                       cwd=str(ROOT))
    out = r.stdout or ""
    i = out.find("{")
    if i < 0:
        raise SystemExit("pipe_client gave no JSON for %s:\n%s\n%s" % (cmd, out, r.stderr))
    return json.loads(out[i:])


def find_actor() -> dict:
    r = call("find_instances", {"class_name": "DumperTestActor", "limit": 8})
    if not r.get("ok"):
        raise SystemExit("find_instances failed: %s" % r)
    for inst in r.get("instances", []):
        if not inst.get("name", "").startswith("Default__"):
            return inst
    raise SystemExit("no non-CDO DumperTestActor found -- is the level loaded?")


def main() -> int:
    inst = find_actor()
    print("actor      : %s  %s" % (inst["name"], inst["addr"]))

    w = call("walk_instance", {"addr": inst["addr"], "class_addr": inst["class_addr"],
                               "array_limit": 8})
    if not w.get("ok"):
        raise SystemExit("walk_instance failed: %s" % w)
    fields = {f.get("name"): f for f in w.get("fields", [])}

    missing = [n for n in ("OnActorHit", "Arr_MulticastDelegates", "Arr_Delegates",
                           "Multicast_Inline", "Del_Unicast") if n not in fields]
    if missing:
        raise SystemExit("fixture is STALE -- missing %s. Repackage: "
                         "py tools/ue-sample/repackage.py --engine 5.4 --project DumperTest "
                         "--configs Development,Shipping" % ", ".join(missing))

    arr = fields["Arr_MulticastDelegates"]
    elem_size = arr.get("array_elem_size")
    pad = (elem_size - MCD_BASE) if isinstance(elem_size, int) else None
    print("elem_size  : %s  ->  access-detector pad = %s  (%s build)"
          % (elem_size, pad,
             {0: "Shipping/Test, or UE <= 5.2", 8: "checked: Development/Debug"}
             .get(pad, "UNKNOWN")))

    fails: list[str] = []
    if pad not in (0, 8):
        fails.append("ElementSize %r implies pad %r -- expected 16 (unpadded) or 24 (+detector)"
                     % (elem_size, pad))

    # --- 1. sparse (D4b proper): the state D4's fix made visible must now RESOLVE ---
    hit = fields["OnActorHit"].get("value", "")
    print("OnActorHit : %r" % hit)
    if HIT_PROBE not in hit:
        fails.append("sparse delegate does not name its subscriber %s -- got %r"
                     % (HIT_PROBE, hit))
    if "unreadable" in hit or "0 bindings" in hit:
        fails.append("sparse delegate still reports unreadable/empty over ONE live "
                     "subscriber: %r" % hit)

    # --- 2. non-array MulticastInlineDelegateProperty ---
    mi = fields["Multicast_Inline"].get("value", "")
    print("Multicast_ : %r" % mi)
    if PROBE not in mi:
        fails.append("Multicast_Inline does not name %s -- got %r" % (PROBE, mi))

    # --- 3. standalone FScriptDelegate: the PADDED unicast type ---
    du = fields["Del_Unicast"].get("value", "")
    print("Del_Unicast: %r" % du)
    if PROBE not in du:
        fails.append("Del_Unicast does not name %s -- got %r" % (PROBE, du))

    # --- 4. the array STRIDE (D3b) ---
    # [0] deliberately empty, [1] deliberately bound. A 16-byte stride in a checked build
    # reads [1] from inside [0] and reports both empty; that is the whole discriminator.
    vals = [e.get("v", "") for e in arr.get("elements", [])]
    print("Arr elems  : %r" % (vals,))
    if len(vals) != 2:
        fails.append("expected 2 array elements, got %d" % len(vals))
    else:
        if vals[0] != "(0 bindings)":
            fails.append("element [0] should be genuinely empty -- got %r" % vals[0])
        if PROBE not in vals[1]:
            fails.append("element [1] does not name %s -- got %r. A wrong element stride "
                         "reads [1] from inside [0], which is exactly this symptom."
                         % (PROBE, vals[1]))

    # --- 5. the SIXTH site: TArray<FScriptDelegate> (ReadDelegateArrayElements) ---
    # ⛔ [D4B-DELEGATEPAD]'s enumeration said "five readers" and missed this one. Its elements
    # are the STANDALONE unicast type, which IS padded -- unlike a multicast's invocation-list
    # elements. It also ignored the ElementSize its callers passed and dropped both ReadSafe
    # returns, so it carried all three of this sweep's shapes at once. Same [0]-empty /
    # [1]-bound discriminator: a reader stuck on the unpadded stride reads [1] from inside [0].
    sd = fields["Arr_Delegates"]
    sd_elem = sd.get("array_elem_size")
    sd_vals = [e.get("v", "") for e in sd.get("elements", [])]
    print("Arr_Deleg  : elem_size=%s  %r" % (sd_elem, sd_vals))
    if not isinstance(sd_elem, int) or sd_elem - (16 if pad == 0 else 24) != 0:
        fails.append("TArray<FScriptDelegate> ElementSize %r does not match the %d implied by "
                     "the multicast side -- the two must agree, both are `8 + sizeof(FName)` "
                     "plus the same detector" % (sd_elem, 16 if pad == 0 else 24))
    if len(sd_vals) != 2:
        fails.append("expected 2 Arr_Delegates elements, got %d" % len(sd_vals))
    else:
        if sd_vals[0] != "(unbound)":
            fails.append("Arr_Delegates[0] should be genuinely unbound -- got %r" % sd_vals[0])
        if PROBE not in sd_vals[1]:
            fails.append("Arr_Delegates[1] does not name %s -- got %r. A reader on the unpadded "
                         "stride reads [1] from inside [0], which is exactly this symptom."
                         % (PROBE, sd_vals[1]))

    print()
    if fails:
        print("D4b/D3b: FAIL")
        for f in fails:
            print("  - %s" % f)
        return 1
    print("D4b/D3b: PASS -- all four delegate shapes read correctly at pad %d; the array's "
          "bound element [1] is reported at [1], not swallowed by [0]." % pad)
    return 0


if __name__ == "__main__":
    sys.exit(main())
