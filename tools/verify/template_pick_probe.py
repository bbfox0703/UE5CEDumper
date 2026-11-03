#!/usr/bin/env python3
r"""Report-only probe for [INVOKE-NONSTATIC-ON-CDO]: does a user-facing class lookup land on a TEMPLATE?

    py tools/verify/template_pick_probe.py [--process DumperTest] [Class ...]

Headless; the UI must NOT be connected. For each class name (default: component classes a pawn's class default object
holds as default subobjects), the mailbox's CMD_FIND_INSTANCE picks an object -- a lookup, nothing is invoked -- and
the probe prints it with its ObjectFlags (RF_ClassDefaultObject 0x10, RF_ArchetypeObject 0x20, read at UObject+0x08)
and its outer chain. A pick whose outer chain reaches a `Default__` object, or whose flags carry 0x30, is a template
the lookup took for a live object. Read-only: it writes only the mailbox's input fields.
"""
from __future__ import annotations

import argparse
import pathlib
import struct
import sys
import time

HERE = pathlib.Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
sys.stdout.reconfigure(encoding="utf-8", errors="replace")
from pipe_client import PipeClient  # noqa: E402
import mailbox_poke as MB  # noqa: E402

CMD_FIND_INSTANCE = 2
OFF_CLASSNAME = 0x028
DEFAULT_CLASSES = ("CharacterMovementComponent", "CapsuleComponent", "SkeletalMeshComponent", "SpringArmComponent",
                   "CameraComponent", "ArrowComponent")


def data(reply):
    return reply.get("data", reply)


def find_instance(m, base, name, timeout=60.0):
    if m.i32(base + MB.OFF_STATUS) == MB.STATUS_PROCESSING:
        raise SystemExit("mailbox is PROCESSING before the command -- a previous one is wedged")
    m.write(base + MB.OFF_STATUS, struct.pack("<i", MB.STATUS_IDLE))
    raw = name.encode("utf-8")[:255]
    m.write(base + OFF_CLASSNAME, raw + b"\x00" * (256 - len(raw)))
    m.write(base + MB.OFF_INSTANCE, struct.pack("<Q", 0xDEADBEEF))
    m.write(base + MB.OFF_RESULT, struct.pack("<i", 0x7FFFFFFF))
    m.write(base + MB.OFF_CMD, struct.pack("<i", CMD_FIND_INSTANCE))
    t0 = time.time()
    while time.time() - t0 < timeout:
        if m.i32(base + MB.OFF_STATUS) == MB.STATUS_DONE:
            res, inst = m.i32(base + MB.OFF_RESULT), m.u64(base + MB.OFF_INSTANCE)
            m.write(base + MB.OFF_CMD, struct.pack("<i", MB.CMD_IDLE))
            return res, inst
        time.sleep(0.005)
    raise SystemExit(f"FIND_INSTANCE {name!r}: TIMEOUT")


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    ap.add_argument("--process", default="DumperTest")
    ap.add_argument("classes", nargs="*")
    a = ap.parse_args()
    pid = MB.pid_of(a.process)
    base = MB.mailbox_addr(str(pid))
    m = MB.Mem(pid)
    with PipeClient(timeout=120) as pc:
        print("build:", data(pc.request("get_pointers")).get("build_number"))
        pc.ensure_scanned()
        for cls in a.classes or DEFAULT_CLASSES:
            res, obj = find_instance(m, base, cls)
            if res != 0 or not obj:
                print(f"{cls:28} result={res} (nothing)")
                continue
            chain, o, template = [], obj, False
            for _ in range(16):
                g = data(pc.request("get_object", addr=hex(o)))
                flags = struct.unpack("<I", m.read(o + 0x08, 4))[0]
                name = g.get("name", "?")
                chain.append(f"{name} [{g.get('class', '?')}] flags={flags:#x}")
                template |= name.startswith("Default__") or bool(flags & 0x30)
                nxt = int(str(g.get("outer_addr", "0")), 16) if g.get("outer_addr") else 0
                if not nxt or nxt == o:
                    break
                o = nxt
            print(f"{cls:28} -> {'TEMPLATE' if template else 'live    '}  " + "  <  ".join(chain[:4]))
    return 0


if __name__ == "__main__":
    sys.exit(main())
