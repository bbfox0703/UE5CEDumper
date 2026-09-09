r"""SW4 — does a delegate record pushed into Cheat Engine resolve to the InvocationList?

    py tools/verify/sw4_ce_delegate_pad.py install     # then RESTART Cheat Engine
    <in UE5DumpUI: Live Walker on DumperTestActor, push the delegate row to CE>
    py tools/verify/sw4_ce_delegate_pad.py check --field Multicast_Inline
    py tools/verify/sw4_ce_delegate_pad.py uninstall

THE ROW. `[D4B-DELEGATEPAD]` put `delegate_pad` on the wire so `CeXmlExportService.CeOffset`
adds it before emitting a delegate leaf. Without it a CE record for a delegate on a CHECKED build
points at the 8-byte access detector instead of at `InvocationList::Data` — a pointer that reads
0. The DLL half was fixed and measured 2026-09-09 (`[SW7]` found it had never reached the wire at
all); what had never happened is CHEAT ENGINE resolving such a record and someone reading the
answer.

⛔ A SHIPPING TITLE PROVES NOTHING HERE. The access detector exists only where `DO_CHECK` is on,
so the pad is 0 in Shipping/Test and a right and a wrong exporter emit the SAME offset. This must
run on **DumperTest Development** (or another checked build), and the rig refuses otherwise by
requiring the wire to report a non-zero `delegate_pad`.

⛔ AND THE COMPARISON MUST BE AGAINST A NUMBER, NOT A SCREENSHOT. CE's Address column shows the
resolved address, but "is that the InvocationList?" by eye means trusting the reader to know
which of two addresses 8 bytes apart is right. The harness writes
`getMemoryRecord(i).CurrentAddress` — what CE actually resolved — to a file, and this rig
compares it to the two candidates the wire itself supplies.

⚠ TWO SHAPE CORRECTIONS from the scouting pass, checked here rather than assumed:
  * a **scalar `DelegateProperty`** never emits `<Offsets>` at all — `MapCeField` routes it to a
    flat `"8 Bytes", ShowAsHex` leaf — so the row's "the group must resolve to the InvocationList"
    describes the MULTICAST shape. `--field` defaults to `Multicast_Inline` for that reason.
  * the UI produces **two different addresses for the same field**: `CeOffset = Offset +
    DelegatePad` for the clipboard XML, but an UNPADDED `FieldAddress` elsewhere. Whichever the
    pushed record carries, this rig reports which of the two it matched.
"""
from __future__ import annotations

import argparse
import io
import json
import os
import re
import shutil
import subprocess
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
ROOT = HERE.parent.parent
CLIENT = HERE / "pipe_client.py"
SRC_LUA = HERE / "ce" / "ue5-sw4-record-dump.lua"
AUTORUN = Path(r"C:\Program Files\Cheat Engine") / "autorun" / "custom"
DST_LUA = AUTORUN / "ue5-sw4-record-dump.lua"
JOB = AUTORUN / "ue5-sw4-job.txt"
OUTDIR = ROOT / "out" / "sw4"
DUMP = OUTDIR / "sw4-records.txt"


def call(cmd: str, args: dict | None = None) -> dict:
    r = subprocess.run([sys.executable, str(CLIENT), cmd, "--args", json.dumps(args or {})],
                       capture_output=True, text=True, encoding="utf-8", errors="replace",
                       cwd=str(ROOT))
    out = r.stdout or ""
    i = out.find("{")
    if i < 0:
        raise SystemExit("pipe_client gave no JSON for %s:\n%s" % (cmd, out))
    return json.loads(out[i:])


def deref(addr: int) -> int:
    """Read the qword AT `addr` -- what a CE pointer record follows."""
    r = call("read_mem", {"addr": hex(addr), "size": 8})
    if not r.get("ok"):
        return 0
    return int.from_bytes(bytes.fromhex(r["bytes"])[:8], "little")


def install(a) -> int:
    if not AUTORUN.is_dir():
        raise SystemExit("no %s -- is Cheat Engine installed here?" % AUTORUN)
    OUTDIR.mkdir(parents=True, exist_ok=True)
    if DUMP.exists():
        DUMP.unlink()
    shutil.copy2(SRC_LUA, DST_LUA)
    io.open(JOB, "w", encoding="utf-8").write("%s\n2000\n" % OUTDIR)
    print("installed  : %s" % DST_LUA)
    print("job        : outdir=%s poll=2000ms" % OUTDIR)
    print("\n⚠ autorun runs at CE STARTUP — restart Cheat Engine now.")
    print("Then push the delegate row to CE from the Live Walker, and run `check`.")
    return 0


def uninstall(a) -> int:
    for p in (JOB, DST_LUA):
        if p.exists():
            p.unlink()
            print("removed    : %s" % p)
    print("⚠ Cheat Engine keeps the timer until it is RESTARTED.")
    return 0


def check(a) -> int:
    fails: list[str] = []

    # ---- the wire: the two candidate addresses, from the DLL itself ----------------
    r = call("find_instances", {"class_name": "DumperTestActor", "limit": 8})
    inst = next(i for i in r.get("instances", [])
                if not (i.get("name") or "").startswith("Default__"))
    w = call("walk_instance", {"addr": inst["addr"], "class_addr": inst["class_addr"],
                               "array_limit": 8})
    f = {x.get("name"): x for x in w.get("fields", [])}
    fld = f.get(a.field)
    if not fld:
        raise SystemExit("%s not found on DumperTestActor" % a.field)

    pad = fld.get("delegate_pad")
    base = int(inst["addr"], 16) + int(fld["offset"])
    print("actor      : %s  %s" % (inst["name"], inst["addr"]))
    print("field      : %s  offset=0x%X  delegate_pad=%r"
          % (a.field, int(fld["offset"]), pad))
    print("  unpadded (the DETECTOR on a checked build) : 0x%X" % base)
    if not pad:
        raise SystemExit(
            "REFUSING: delegate_pad is %r. On a Shipping/Test build the pad is 0 and a right "
            "and a wrong exporter emit the SAME offset, so this run could not tell them apart. "
            "Use DumperTest Development." % pad)
    padded = base + int(pad)
    print("  padded   (InvocationList::Data)            : 0x%X" % padded)

    # ---- what CE resolved ------------------------------------------------------------
    if not DUMP.is_file():
        print("\nSW4: FAIL")
        print("  - %s was never written. The harness did not run: autorun is read at CE "
              "STARTUP, so Cheat Engine must be restarted AFTER install." % DUMP)
        return 1
    text = io.open(DUMP, encoding="utf-8", errors="replace").read()
    print("\nCE table:")
    for ln in text.splitlines():
        print("  %s" % ln[:170])

    rows = []
    for ln in text.splitlines():
        m = re.match(r"^(\d+) \| desc=(.*?) \| addr=([0-9A-Fa-f]+) \| type=(.*?) \| offsets=(.*)$",
                     ln)
        if m:
            rows.append({"i": int(m.group(1)), "desc": m.group(2),
                         "addr": int(m.group(3), 16), "type": m.group(4),
                         "offsets": m.group(5)})
    if not rows:
        fails.append("CE's table is EMPTY — nothing was pushed, so there is no record to "
                     "resolve. Push the delegate row from the Live Walker first.")
        print("\nSW4: FAIL")
        for x in fails:
            print("  - %s" % x)
        return 1

    # ⚠ A MULTICAST ROW IS A GROUP HEADER WITH `<Offsets><Offset>0</Offset></Offsets>`, i.e. a
    # POINTER record: CE reads the qword AT the record's address and resolves to what it points
    # at. So what CE reports is the DEREFERENCE, not the padded address itself. Both candidates
    # are dereferenced here, and the contrast is the whole proof: on a checked build the detector
    # is an idle `std::atomic<uint64>` reading 0, so an unpadded record resolves to ADDRESS 0 —
    # verbatim what this row says must not happen.
    d_pad, d_base = deref(padded), deref(base)
    print("  [padded]   = 0x%X   <- InvocationList::Data" % d_pad)
    print("  [unpadded] = 0x%X   <- the access detector" % d_base)

    want = {padded, d_pad} - {0}
    bad = {base, d_base}
    mine = [r for r in rows if a.field in r["desc"]]
    hit_padded = [r for r in mine if r["addr"] in want]
    hit_unpadded = [r for r in mine if r["addr"] in bad]
    print("\n%s records: %r" % (a.field, [(r["desc"], hex(r["addr"])) for r in mine]))
    print("  resolving to the PADDED   side %r : %d"
          % ([hex(x) for x in sorted(want)], len(hit_padded)))
    print("  resolving to the UNPADDED side %r : %d"
          % ([hex(x) for x in sorted(bad)], len(hit_unpadded)))

    if not mine:
        fails.append("no record whose description mentions %r is in CE's table -- nothing was "
                     "pushed for this field" % a.field)
    if d_base != 0:
        print("  ⚠ the detector does not read 0 right now, so the two sides are less "
              "starkly different than usual; the address comparison still decides it")
    if hit_unpadded:
        fails.append("a record resolved to the UNPADDED side (%r) -- the access detector, not "
                     "InvocationList::Data, which is exactly what delegate_pad exists to prevent"
                     % [(r["desc"], hex(r["addr"])) for r in hit_unpadded])
    if not hit_padded:
        fails.append("no %s record resolved to the padded side %r"
                     % (a.field, [hex(x) for x in sorted(want)]))

    print()
    if fails:
        print("SW4: FAIL")
        for x in fails:
            print("  - %s" % x)
        return 1
    print("SW4: PASS -- Cheat Engine resolved the pushed %s record to 0x%X, which is "
          "InvocationList::Data (field + delegate_pad %d), NOT the access detector at 0x%X. "
          "Measured on a CHECKED build, where the two differ." % (a.field, padded, pad, base))
    return 0


def main() -> int:
    ap = argparse.ArgumentParser()
    sub = ap.add_subparsers(dest="cmd", required=True)
    sub.add_parser("install")
    c = sub.add_parser("check")
    c.add_argument("--field", default="Multicast_Inline")
    sub.add_parser("uninstall")
    a = ap.parse_args()
    return {"install": install, "check": check, "uninstall": uninstall}[a.cmd](a)


if __name__ == "__main__":
    sys.exit(main())
