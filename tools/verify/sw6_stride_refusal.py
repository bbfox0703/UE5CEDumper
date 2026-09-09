r"""SW6 — the delegate-stride REFUSAL, on the two arms that can actually reach it.

    py tools/verify/sw6_stride_refusal.py        # DumperTest must be running + injected

THE ROW. `[D4B-DELEGATEPAD]` gave four readers a refusal: rather than guess a stride from an
ElementSize that is neither *base* nor *base+8*, they decline. ⭐ A refusal is a NEW FAILURE MODE
the fix introduced — pre-fix the reader simply read the field — so if it ever fires wrongly it
blanks working data. It had never fired at all: 5.4 / 5.7 / 5.8 and UE 4.23 / 4.27 all report one
of the two legal sizes across 600 classes each, and the register recorded the arms as reachable
"only on an engine whose delegate ElementSize is neither" — i.e. never.

⭐ BUT THE HOUSE TECHNIQUE REACHES IT. The size the array readers validate is the engine's own
`FProperty::ElementSize` on the ArrayProperty's **Inner** property, read LIVE on every walk
(`Ubel.cpp:4370-4372`), and the wire already hands us that Inner's address as `array_inner_addr`
(`Fern.cpp:1538`, non-lean). One 4-byte write makes 32 — neither 16 nor 24 — and both array
readers must refuse. `DelegatePadFromElementSize(32, 16) < 0` is already unit-pinned
(`dll_helpers_test.cpp:6590`); what has never happened is a CALL SITE reaching it.

⛔ THE SCALAR ARMS ARE NOT REACHABLE, and `--scalar` exists to MEASURE that rather than to assume
it. `Ubel.cpp:5499` / `:5996` validate `fi.Size`, which `EnumerateFields` reads ONCE
(`Ubel.cpp:761`) and `WalkClass` memoises into `s_walkClassCache` (`Ubel.cpp:879`) — a 2048-entry
LRU with **no invalidation command anywhere in the DLL**. `WalkInstance` iterates that cached copy
(`Ubel.cpp:4100`), so a poke landing after the class has been walked is invisible, and so is its
restore.

Evicting the LRU looked like the way in. It is not, and the reason is worth recording because it
is not obvious from either cache's own comments. Measured on a COLD process, 2026-09-09:

    at start                                entries=0     fields=0
    after walk_instance (DumperTestActor)   entries=10    fields=194
    after walk_class_batch x600             entries=2048  fields=29251
    after walk_class_batch ALL (3946)       entries=2048  fields=29251   <- IDENTICAL

The second half of that pass changed nothing at all. `walk_class_batch` goes through
`Ubel::WalkClassEx` (`Aura.cpp` WalkClassesBatch), which consults the **separate, UNBOUNDED**
`s_walkClassExCache` (`Ubel.cpp:1200`) and only calls `WalkClass` on a MISS (`Ubel.cpp:1211-1214`).
Once that front cache is warm — which 600 classes achieve, because each walk pulls in super chains
and struct types — no later class walk reaches `WalkClass` at all, so the LRU stops taking inserts
and therefore stops evicting. Our class, inserted early, simply never reaches the back.

⭐ AND THE ARRAY ARMS ARE THE BETTER EVIDENCE ANYWAY. The scalar refusal is a string the DLL
builds and the UI prints verbatim — a render echo. The array arms change the SHAPE of the wire
payload: `array_elem_size` becomes the poked value and the `elements` key disappears where two
elements stood. That is a structural observable, not an echo of a string.

⭐ THIS RIG FOUND AN ASYMMETRY THAT HAS SINCE BEEN FIXED, and both halves are worth keeping in
mind because the register's acceptance text was written against the broken shape:
  * the two ARRAY sites warned but were SILENT on the wire's value side — `Ubel.cpp:4515-4522`
    moved the reader's `elements` only on success and DISCARDED `delResult.error`, and an
    ArrayProperty never sets `typedValue`, so a refused array reached the UI as a bare header
    with no elements: indistinguishable from an empty array;
  * the two SCALAR sites rendered their string but emitted NO LOG LINE AT ALL, so "DLL side: the
    WALK warning naming the size" was unsatisfiable there.
Neither arm could satisfy the row's two-sided acceptance on its own. Both were fixed on
2026-09-09 after this rig measured it: the array refusal now lands in `typedValue`, and all four
sites warn. The assertions below check BOTH sides on the array arm, which is what closes the row.

⚠ BLAST RADIUS, and why the window is kept short. The Inner FProperty is per-CLASS, so the poked
size is what the ENGINE would use too (`FScriptArrayHelper`, `CopyCompleteValue`). Nothing may
spawn a `DumperTestActor`, reload the level, or re-bind those delegates while the poke is live.
The rig writes, walks once, and restores in a `finally`.

ACCEPTANCE (both sides, per the charter — and both now come from the SAME arm):
  * DLL/log side  — a `WALK` warning naming the poked size, from the reader under test, written
    INSIDE the poke window (timestamp-scoped);
  * wire/UI side  — `array_elem_size` reads back as the poked value, the `elements` key
    disappears where the baseline had two elements, AND `value` carries a refusal string naming
    the size, which is what the UI renders.
"""
from __future__ import annotations

import argparse
import glob
import io
import json
import os
import re
import subprocess
import sys
import time
from pathlib import Path

HERE = Path(__file__).resolve().parent
ROOT = HERE.parent.parent
CLIENT = HERE / "pipe_client.py"

POKE = 32          # neither base nor base+8, for either reader -- see legal_sizes()

# Each reader validates against its OWN FIXED base, not against whatever the field currently
# reports. ⛔ Deriving the legal set from the LIVE value is wrong and this rig did it that way
# first: on a checked build the live size is 24, and `{live, live±8}` wrongly admits 32, so the
# guard refused a poke that the readers do in fact reject. The bases are:
#   ReadMulticastDelegateArrayElements -> kMcdBaseSize, a literal 16   (Ubel.cpp:3396)
#   ReadDelegateArrayElements          -> kSdBase = 8 + sizeof(FName)  (Ubel.cpp:3266)
SUBJECTS = [
    ("Arr_MulticastDelegates", "ReadMulticastDelegateArrayElements", lambda fname: 16),
    ("Arr_Delegates", "ReadDelegateArrayElements", lambda fname: 8 + fname),
]


def legal_sizes(base: int) -> tuple[int, int]:
    """`DelegatePadFromElementSize` accepts exactly these two (Grimoire.h:704-709)."""
    return (base, base + 8)


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


def injected_process() -> str:
    root = Path(os.environ["LOCALAPPDATA"]) / "UE5CEDumper" / "Logs"
    best, best_t = None, 0.0
    for d in root.iterdir() if root.is_dir() else []:
        if not d.is_dir():
            continue
        for f in d.glob("*.log"):
            t = f.stat().st_mtime
            if t > best_t:
                best, best_t = d.name, t
    if not best or time.time() - best_t > 600:
        raise SystemExit("no freshly-written log folder -- is the DLL loaded?")
    return best


def walk_log_lines_since(proc: str, t0: str) -> list[str]:
    d = Path(os.environ["LOCALAPPDATA"]) / "UE5CEDumper" / "Logs" / proc
    out = []
    for f in glob.glob(str(d / "walk*.log")):
        for ln in io.open(f, encoding="utf-8", errors="replace"):
            m = re.match(r"^\[(\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2})", ln)
            if m and m.group(1) >= t0:
                out.append(ln.rstrip("\n"))
    return out


SCALARS = [
    ("Del_Unicast", "(delegate — unexpected ElementSize %d, not read)", lambda f: 8 + f),
    ("Multicast_Inline", "(multicast — unexpected ElementSize %d, not read)", lambda f: 16),
]


def evict_class_cache(skip_addr: str, want: int = 0) -> int:
    """⭐ THE ONLY WAY TO REACH THE SCALAR ARMS. `fi.Size` is read once by EnumerateFields and
    memoised by WalkClass into `s_walkClassCache` (Ubel.cpp:879), and the DLL exposes NO
    invalidation command -- so a poke landing after the class has been walked is invisible, and
    so would its restore be. The cache is a 2048-entry LRU (Ubel.h:204), so ageing our class out
    and letting the next walk re-read memory is possible -- but only with enough pressure.

    ⛔ 2200 CLASSES IS NOT ENOUGH, measured 2026-09-09. Eviction runs ONLY on an INSERT --
    `PublishWalkClass` returns early on a cache HIT (Ubel.cpp:906), and walking an
    already-cached class merely touches it. After 2200 others our class was still inside the
    2048 most-recent, so it was never at the back when an insert popped one, and the wire kept
    reporting the STALE size. Walking EVERY other class makes it the oldest entry by
    construction. Verified empirically by the field's own `size` on the wire, never assumed:
    the caller treats an unchanged `size` as "this arm measured nothing", NOT as a refusal that
    failed to fire."""
    listing = call("list_classes", {"limit": 50000, "game_only": False})
    addrs = [c["class_addr"] for c in listing.get("classes", [])
             if c.get("class_addr") and c["class_addr"].lower() != skip_addr.lower()]
    if want:
        addrs = addrs[:want]
    for i in range(0, len(addrs), 60):
        call("walk_class_batch", {"addrs": addrs[i:i + 60]})
    return len(addrs)


def scalar_arm(inst: dict, off: dict, poke: int, fails: list) -> list:
    """The arm the row's UI-side acceptance actually needs -- the scalar sites are the only two
    that write the refusal into `typedValue`, which the UI renders verbatim."""
    fname = 12 if off.get("case_preserving") else 8
    esz = off["fproperty_elemsize"]
    done = []
    for name, tmpl, base_of in SCALARS:
        sp = call("search_properties", {"query": name, "game_only": False, "limit": 20,
                                        "types": [], "deep": False})
        hit = next((r for r in sp.get("results", [])
                    if r.get("prop_name") == name and r.get("class_name") == "DumperTestActor"),
                   None)
        if not hit or not hit.get("field_addr"):
            fails.append("scalar %s: no field_addr from search_properties" % name)
            continue
        faddr = int(hit["field_addr"], 16)
        base = base_of(fname)
        legal = legal_sizes(base)
        addr = faddr + esz
        live = read_i32(addr)
        print("\n--- %s (SCALAR)" % name)
        print("  field      : %s   ElementSize@%s = %d   legal %r"
              % (hit["field_addr"], hex(addr), live, legal))
        if live not in legal or poke in legal:
            fails.append("scalar %s: live=%d legal=%r poke=%d -- unusable baseline"
                         % (name, live, legal, poke))
            continue

        try:
            write_i32(addr, poke)
            n = evict_class_cache(inst["class_addr"])
            f2 = walk(inst).get(name, {})
            size_now, val_now = f2.get("size"), f2.get("value")
            print("  evicted %d class(es);  wire size=%r  value=%r" % (n, size_now, val_now))
            if size_now != poke:
                fails.append("scalar %s: after poking and walking %d other classes the wire "
                             "still reports size=%r, so s_walkClassCache was NOT refreshed and "
                             "this arm measured nothing (it is NOT evidence the refusal failed)"
                             % (name, n, size_now))
                continue
            want = tmpl % poke
            if val_now != want:
                fails.append("scalar %s: expected %r, got %r" % (name, want, val_now))
            else:
                done.append(name)
        finally:
            write_i32(addr, live)
            evict_class_cache(inst["class_addr"])
            back = walk(inst).get(name, {})
            ok = back.get("size") == live
            print("  restored   : size=%r %s" % (back.get("size"),
                                                 "(baseline)" if ok else "MISMATCH"))
            if not ok:
                fails.append("scalar %s: the restore did not come back (size=%r)"
                             % (name, back.get("size")))
    return done


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--poke", type=int, default=POKE)
    ap.add_argument("--scalar", action="store_true",
                    help="probe the SCALAR arms. ⛔ EXPECTED TO FAIL: it reports that the class "
                         "cache was never refreshed, which is the measurement, not a defect in "
                         "the refusal. See the docstring for the numbers.")
    a = ap.parse_args()

    proc = injected_process()
    off = call("get_offsets", {})
    elemsize_off = off.get("fproperty_elemsize")
    print("process    : %s" % proc)
    print("offsets    : fproperty_elemsize=%r use_fproperty=%r"
          % (elemsize_off, off.get("use_fproperty")))
    if not isinstance(elemsize_off, int) or elemsize_off <= 0:
        raise SystemExit("get_offsets did not publish fproperty_elemsize -- refusing to guess")

    r = call("find_instances", {"class_name": "DumperTestActor", "limit": 8})
    inst = next(i for i in r.get("instances", [])
                if not (i.get("name") or "").startswith("Default__"))
    print("actor      : %s  %s" % (inst["name"], inst["addr"]))

    fields = walk(inst)
    fails: list[str] = []
    results = []

    fname = 12 if off.get("case_preserving") else 8
    print("FName      : %d bytes" % fname)

    for name, reader, base_of in SUBJECTS:
        f = fields.get(name)
        if not f:
            raise SystemExit("fixture is STALE -- %s missing" % name)
        inner = f.get("array_inner_addr")
        base_size = f.get("array_elem_size")
        base_elems = [e.get("v", "") for e in f.get("elements", [])]
        print("\n--- %s" % name)
        print("  inner      : %s   elem_size=%r   elements=%r"
              % (inner, base_size, base_elems))
        if not inner or inner == "0x0":
            raise SystemExit("%s has no array_inner_addr on the wire (lean payload?)" % name)
        if not base_elems:
            fails.append("%s has NO elements at baseline, so 'elements disappeared' could not "
                         "be observed" % name)
            continue

        addr = int(inner, 16) + elemsize_off
        live = read_i32(addr)
        print("  ElementSize at %s reads %d (wire says %r)" % (hex(addr), live, base_size))
        if live != base_size:
            fails.append("%s: the byte at Inner+0x%X is %d but the wire reported %r -- the "
                         "address is not the ElementSize field; REFUSING to write"
                         % (name, elemsize_off, live, base_size))
            continue
        base = base_of(fname)
        legal = legal_sizes(base)
        print("  reader base=%d -> legal sizes %r; poking %d" % (base, legal, a.poke))
        if live not in legal:
            fails.append("%s: the LIVE size %d is already outside %r, so this field is not in "
                         "a healthy baseline state" % (name, live, legal))
            continue
        if a.poke in legal:
            fails.append("%s: poke value %d is one of this reader's legal sizes %r -- it would "
                         "not be refused" % (name, a.poke, legal))
            continue

        t0 = time.strftime("%Y-%m-%d %H:%M:%S")
        time.sleep(1.1)                        # so t0 cannot swallow a pre-poke line
        try:
            write_i32(addr, a.poke)
            f2 = walk(inst).get(name, {})
            poked_size = f2.get("array_elem_size")
            poked_elems = f2.get("elements")
            print("  POKED      : elem_size=%r   elements=%r" % (poked_size, poked_elems))
            lines = [l for l in walk_log_lines_since(proc, t0) if reader in l]
            for l in lines[:2]:
                print("  log        : %s" % l.strip()[:170])

            if poked_size != a.poke:
                fails.append("%s: the wire still reports elem_size=%r after the poke"
                             % (name, poked_size))
            # ⭐ ADDED 2026-09-09 AFTER THE FIX. Ubel.cpp used to DISCARD the array reader's
            # `error`, so a refused array reached the UI as a bare header -- indistinguishable
            # from an empty array. It now lands in `typedValue`, which is what the UI renders,
            # so this arm is two-sided like the scalar ones.
            poked_val = f2.get("value")
            print("  value      : %r" % poked_val)
            if not poked_val or "not read" not in poked_val:
                fails.append("%s: the wire carries no refusal string (value=%r). The reader "
                             "refused but said so only in the log, so a user sees a delegate "
                             "array that merely looks empty." % (name, poked_val))
            elif str(a.poke) not in poked_val:
                fails.append("%s: the refusal string does not name the size %d: %r"
                             % (name, a.poke, poked_val))
            if poked_elems:
                fails.append("%s: elements are STILL present (%r) -- the reader did not refuse; "
                             "it read the array at a stride nobody validated, which is exactly "
                             "the defect the refusal exists to prevent"
                             % (name, [e.get("v") for e in poked_elems]))
            if not lines:
                fails.append("%s: no %r WALK warning inside the poke window. The refusal may "
                             "have happened silently, or a different reader handled the field."
                             % (name, reader))
            elif str(a.poke) not in lines[0]:
                fails.append("%s: the warning does not name the size %d: %r"
                             % (name, a.poke, lines[0].strip()))
            results.append((name, reader, poked_size, bool(lines)))
        finally:
            write_i32(addr, live)
            back = walk(inst).get(name, {})
            ok = (back.get("array_elem_size") == base_size
                  and [e.get("v", "") for e in back.get("elements", [])] == base_elems)
            print("  restored   : %s" % ("identical to baseline" if ok else
                                         "MISMATCH %r %r" % (back.get("array_elem_size"),
                                                             [e.get("v") for e in
                                                              back.get("elements", [])])))
            if not ok:
                fails.append("%s: the restore did not bring the baseline back" % name)

    scalars_done = scalar_arm(inst, off, a.poke, fails) if a.scalar else []

    print()
    if fails:
        print("SW6: FAIL")
        for f in fails:
            print("  - %s" % f)
        return 1
    print("SW6: PASS -- both ARRAY refusal arms fired on a manufactured ElementSize of %d: each "
          "reader logged a WALK warning naming the size, and the wire lost its `elements` key "
          "while keeping the header. %d arm(s) measured; the write is reversible."
          % (a.poke, len(results)))
    if scalars_done:
        print("           -- and the SCALAR arms %s rendered their refusal string too."
              % ", ".join(scalars_done))
    else:
        print("   The two SCALAR arms were not run (pass --scalar; they are blocked by the "
              "class cache -- see the docstring).")
    print("⭐ Both sides now come from the SAME arm. Until 2026-09-09 they could not: "
          "Ubel.cpp discarded the array reader's `error`, so a refused array reached the UI as "
          "a bare header and only the log knew; and the two SCALAR sites emitted no log line at "
          "all, so the row's stated DLL-side acceptance was unsatisfiable there. Both were "
          "fixed after this rig measured the asymmetry.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
