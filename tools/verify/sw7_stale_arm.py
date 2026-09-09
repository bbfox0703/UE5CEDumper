r"""SW7 — a delegate whose TARGET is gone must still name its function: "(stale)::Func".

    py tools/verify/sw7_stale_arm.py        # DumperTest must be running + injected

THE ROW. `[STALE-NONE-2026-09-09]` narrowed `"(stale)"`, which is an AFFIRMATIVE claim -- a target
WAS bound and has since been collected -- so that an UNTOUCHED slot says `(unbound)` instead. Only
the unbound half was verified live (`Arr_Delegates[0]` went `(stale)::None` -> `(unbound)` on both
build configurations). ⭐ **A narrowing is only correct if it did not also delete the state it was
narrowing**, and that half was never observed: a delegate that really IS stale must still say so.

WHY THE SERIAL, AND WHY IT IS NOT A FAKE. `Ubel::ResolveWeakObjectPtr` returns 0 on exactly two
conditions -- `objectIndex <= 0`, or `Aura::GetSerialNumber(objectIndex) != serialNumber`, whose
own comment reads `// stale reference`. A serial mismatch IS how UE detects a recycled object, so
poking `serial + 1` manufactures the engine's own staleness rather than an approximation of it:
`objectIndex` stays > 0 and the FName eight bytes along is never touched, which is the whole point
-- the FUNCTION NAME must survive while the TARGET does not.

⛔ `serial = 0` WOULD NOT WORK and is the trap to avoid: `ResolveWeakObjectPtr` special-cases only
`objectIndex`, so a zero serial against a genuinely-zero serial is a silent no-op and the run would
pass while measuring nothing. `+1` is unconditional.

⛔⛔ AND DO NOT LOCATE THE ARRAY ELEMENT VIA `delegate_pad`. That key is set at exactly two sites
(`Ubel.cpp:5507`, `:6004`), both scalar, and Fern emits it only when non-zero, so it is **absent
for a TArray of delegates**. A rig that derives the element address from it pokes the access
detector instead, the walk returns the healthy string, and the run looks like a clean pass. The
array arm below derives the pad from `array_elem_size` the way `d4b_delegate_pad.py` does.

⭐ AND THAT IS HOW THIS RIG FOUND A SHIPPED BUG ON ITS FIRST RUN, 2026-09-09. The scalar arm
printed `pad=0 (from the wire)` on a Development build whose array-derived pad was 8 — impossible,
since both are the same standalone `FScriptDelegate`. Fern's emission sat inside the ARRAY branch
(`if (arrayCount >= 0)` -> `if (!arrayInnerType...)`), which a scalar `DelegateProperty` never
enters: `arrayCount` stays -1. `Ubel.cpp:5507` had been setting the pad on every checked build and
the value never left the process. `Ubel.cpp:6004` (MulticastInline) reports an invocation list, so
it cleared that gate and was never affected — the loss was exactly one field KIND. Live effect:
`CeXmlExportService.CeOffset` added 0 for a scalar DelegateProperty, putting the CE record on the
access detector — the very failure Fern's own comment warns about. `CeXmlDelegatePadTests` stayed
green the whole time because it builds `LiveFieldValue` in C# with `DelegatePad` already set and
never crosses DLL -> wire -> C#. The `scalar_pad != arr_pad` assertion below is that discovery,
kept as a permanent check.

TWO ARMS, because five render sites share `Ubel::DescribeScriptDelegate` and they reach it by
different routes:
  * `Del_Unicast`         -- the scalar `DelegateProperty` handler, pad from the WIRE;
  * `Arr_Delegates[1]`    -- `ReadDelegateArrayElements`, pad DERIVED from `array_elem_size`.

ACCEPTANCE (the register's charter wants both sides):
  * wire/DLL side -- the field's resolved-pointer keys disappear (`ResolveWeakObjectPtr` returned
    0) while the rendered value keeps the function name;
  * value side    -- `(stale)::D4b_OnPingProbe`, and NOT `(unbound)` and NOT a bare `(stale)`.
    Those three strings are the whole distinction the fix is about.
"""
from __future__ import annotations

import json
import subprocess
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
ROOT = HERE.parent.parent
CLIENT = HERE / "pipe_client.py"

PROBE = "D4b_OnPingProbe"
STALE = "(stale)::" + PROBE
# ⚠ The BOUND string is DERIVED from the baseline, never hardcoded. `d4b_delegate_pad.py` only
# ever asserted `PROBE in value`, so the target half ("DumperTestActor_0") has never actually
# been measured -- writing it in as a literal would make this rig fail on a renamed actor for a
# reason that has nothing to do with staleness.


def call(cmd: str, args: dict | None = None) -> dict:
    r = subprocess.run([sys.executable, str(CLIENT), cmd, "--args", json.dumps(args or {})],
                       capture_output=True, text=True, encoding="utf-8", errors="replace",
                       cwd=str(ROOT))
    out = r.stdout or ""
    i = out.find("{")
    if i < 0:
        raise SystemExit("pipe_client gave no JSON for %s:\n%s\n%s" % (cmd, out, r.stderr))
    return json.loads(out[i:])


def walk(inst) -> dict:
    w = call("walk_instance", {"addr": inst["addr"], "class_addr": inst["class_addr"],
                               "array_limit": 8})
    if not w.get("ok"):
        raise SystemExit("walk_instance failed: %s" % w)
    return {f.get("name"): f for f in w.get("fields", [])}


def read_i32(addr: int) -> int:
    r = call("read_mem", {"addr": hex(addr), "size": 4})
    if not r.get("ok"):
        raise SystemExit("read_mem failed at %s: %s" % (hex(addr), r))
    return int.from_bytes(bytes.fromhex(r["bytes"])[:4], "little")


def write_i32(addr: int, v: int) -> None:
    w = call("write_mem", {"addr": hex(addr), "bytes": v.to_bytes(4, "little").hex().upper()})
    if not w.get("ok"):
        raise SystemExit("write_mem failed at %s: %s" % (hex(addr), w))


def main() -> int:
    r = call("find_instances", {"class_name": "DumperTestActor", "limit": 8})
    inst = next(i for i in r.get("instances", [])
                if not i.get("name", "").startswith("Default__"))
    print("actor      : %s  %s" % (inst["name"], inst["addr"]))

    off = call("get_offsets", {})
    fname = 12 if off.get("case_preserving") else 8
    print("FName      : %d bytes" % fname)

    fields = walk(inst)
    for n in ("Del_Unicast", "Arr_Delegates"):
        if n not in fields:
            raise SystemExit("fixture is STALE -- %s missing. Repackage DumperTest." % n)

    # ---- where each arm's FWeakObjectPtr.SerialNumber lives -------------------------
    sd = fields["Del_Unicast"]
    scalar_pad = sd.get("delegate_pad", 0)          # on the wire for SCALARS only
    scalar_serial = int(inst["addr"], 16) + int(sd["offset"]) + int(scalar_pad) + 4

    ar = fields["Arr_Delegates"]
    elem = ar.get("array_elem_size")
    data = ar.get("array_data_addr")
    if not isinstance(elem, int) or not data or data == "0x0":
        raise SystemExit("Arr_Delegates has no usable array header: elem=%r data=%r"
                         % (elem, data))
    arr_pad = elem - (8 + fname)                    # DERIVED -- see the ⛔ in the docstring
    arr_serial = int(data, 16) + elem + arr_pad + 4  # element [1] is the bound one

    print("scalar     : pad=%s (from the wire)  serial@%s" % (scalar_pad, hex(scalar_serial)))
    print("array      : elem_size=%d pad=%d (DERIVED)  serial@%s"
          % (elem, arr_pad, hex(arr_serial)))
    if arr_pad not in (0, 8):
        raise SystemExit("derived array pad %d is neither 0 nor 8 -- refusing to poke" % arr_pad)

    # ⭐ THE CROSS-CHECK THAT FOUND THE WIRE BUG, and the reason it is asserted rather than
    # printed. `Del_Unicast` and an `Arr_Delegates` element are the SAME C++ type -- a standalone
    # `TScriptDelegate<FNotThreadSafeDelegateMode>` -- so on one build their pads cannot differ.
    # A wire-side `0` against a derived `8` is not a disagreement about layout; it means the key
    # never arrived. Measured 2026-09-09 on Development: wire 0 vs derived 8, because
    # `Fern.cpp`'s emission sat in the ARRAY branch while only SCALAR sites set the value.
    # ⚠ Without this, the Shipping arm cannot tell "pad legitimately 0" from "key missing" --
    # which is exactly the ambiguity that let the bug ship. Here the array side supplies the
    # independent answer, so the check has teeth on BOTH configurations.
    if scalar_pad != arr_pad:
        raise SystemExit(
            "the scalar's wire pad (%r) disagrees with the pad DERIVED from the array's "
            "ElementSize (%d). Both are standalone FScriptDelegate on the same build, so they "
            "must agree; a wire 0 against a derived 8 means `delegate_pad` never reached the "
            "wire. Refusing to poke -- the scalar address would land in the access detector."
            % (scalar_pad, arr_pad))

    base = (fields["Del_Unicast"].get("value", ""),
            [e.get("v", "") for e in ar.get("elements", [])])
    print("baseline   : scalar=%r  array=%r" % base)

    fails: list[str] = []
    # A healthy binding is "Target::Probe" -- named AND resolved. Anything else and the poke
    # below would be measuring a delegate that was already broken.
    for label, v in (("scalar", base[0]), ("array [1]", base[1][1] if len(base[1]) > 1 else "")):
        if not v.endswith("::" + PROBE) or v.startswith("(stale)"):
            fails.append("%s baseline must be a resolved %r binding before poking -- got %r"
                         % (label, PROBE, v))
    if len(base[1]) != 2:
        fails.append("expected 2 Arr_Delegates elements, got %d" % len(base[1]))
    if base[1] and base[1][0] != "(unbound)":
        fails.append("array [0] baseline should be %r -- got %r" % ("(unbound)", base[1][0]))
    if fails:
        print("\nSW7: FAIL (baseline)")
        for f in fails:
            print("  - %s" % f)
        return 1

    old_scalar = read_i32(scalar_serial)
    old_arr = read_i32(arr_serial)
    print("serials    : scalar=%d array=%d" % (old_scalar, old_arr))

    # Same fixture, same bound actor -- so the two FWeakObjectPtrs reference one object and
    # their serials are the SAME NUMBER. Derived from the baseline strings, not assumed: only
    # asserted when both arms actually named the same target. Under the wire bug this read
    # `scalar=0 array=1520`, the 0 being the top half of the access detector.
    if base[0] == base[1][1] and old_scalar != old_arr:
        raise SystemExit("both arms name the same target (%r) but their serials differ "
                         "(%d vs %d) -- one of the two addresses is not a SerialNumber. "
                         "Refusing to poke." % (base[0], old_scalar, old_arr))

    poked = ("", [])
    try:
        write_i32(scalar_serial, old_scalar + 1)
        write_i32(arr_serial, old_arr + 1)
        f2 = walk(inst)
        poked = (f2["Del_Unicast"].get("value", ""),
                 [e.get("v", "") for e in f2["Arr_Delegates"].get("elements", [])])
        print("poked      : scalar=%r  array=%r" % poked)

        # ⭐ THE WHOLE POINT. The target is gone, the NAME is not.
        if poked[0] != STALE:
            fails.append("scalar: expected %r, got %r. `(unbound)` here would mean the "
                         "narrowing swallowed a genuinely stale binding; a bare `(stale)` "
                         "would mean the function name was lost with the target."
                         % (STALE, poked[0]))
        if len(poked[1]) != 2 or poked[1][1] != STALE:
            fails.append("array [1]: expected %r, got %r" % (STALE, poked[1]))
        if len(poked[1]) == 2 and poked[1][0] != "(unbound)":
            fails.append("array [0] must stay %r -- it was never bound and nothing was poked "
                         "on it; got %r" % ("(unbound)", poked[1][0]))

        # wire side: the resolved-target keys must be gone, which is the DLL saying
        # ResolveWeakObjectPtr returned 0 rather than the renderer choosing a string.
        el1 = f2["Arr_Delegates"].get("elements", [{}, {}])[1]
        if el1.get("pa") or el1.get("pn"):
            fails.append("the wire still carries a resolved pointer for the stale element "
                         "(pa=%r pn=%r) -- then the target did NOT go away and the string is "
                         "not evidence" % (el1.get("pa"), el1.get("pn")))
    finally:
        write_i32(scalar_serial, old_scalar)
        write_i32(arr_serial, old_arr)
        after = walk(inst)
        back = (after["Del_Unicast"].get("value", ""),
                [e.get("v", "") for e in after["Arr_Delegates"].get("elements", [])])
        print("restored   : %s" % ("identical to baseline" if back == base else repr(back)))
        if back != base:
            fails.append("the restore did not bring the baseline back: %r" % (back,))

    print()
    if fails:
        print("SW7: FAIL")
        for f in fails:
            print("  - %s" % f)
        return 1
    print("SW7: PASS -- a delegate whose target is gone still names its function "
          "(%r), on BOTH the scalar and the array reader; the never-bound element stays "
          "(unbound); the write is reversible." % STALE)
    return 0


if __name__ == "__main__":
    sys.exit(main())
