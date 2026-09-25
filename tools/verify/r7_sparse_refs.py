"""Find References and the walker over sparse delegates, optionally with the global storage POKED into a bad shape.

    py tools/verify/r7_sparse_refs.py --build 3550 --refs-class CharacterMovementComponent \
        --walk-class CapsuleComponent --out out/r7live/a01
    py tools/verify/r7_sparse_refs.py --process DumperTest-Win64-Shipping --refs-name DumperTestSparseListener \
        --poke-header -1 --out out/r7live/s4/poke_m1
    py tools/verify/r7_sparse_refs.py --process DumperTest-Win64-Shipping --refs-name DumperTestSparseListener \
        --walk-class DumperTestActor --poke-keys --out out/r7live/s2/keys

For Review 7 rows R7-A-01, R7-S2, R7-S4, R7-X2 (promoted from out/review7/live_check.py). Prints, per Find
References target, every MulticastSparseDelegateProperty hit and the reply's `scan` block (sparse_skipped,
sparse_unlocated, deadline_hit, duration_ms); per walked instance, every sparse field's display value.

--poke-header N  writes N over the storage's ArrayNum (+0x08) for the duration of the calls.
--poke-keys      writes the FObjectKey-shaped {3, 5} = 0x0000000500000003 over every live outer key (the first 8
                 bytes of each 0x60 element whose key is an aligned userspace pointer).
Both record the originals first, restore them in a `finally`, and read them back; a mismatch is reported loudly.
The window is kept as short as the calls themselves: the engine reads this map when a sparse delegate is bound,
unbound or broadcast, so run it on an idle fixture.
"""
import argparse
import json
import os
import struct
import sys
import time

sys.stdout.reconfigure(encoding="utf-8", errors="replace")
HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
from pipe_client import PipeClient  # noqa: E402
from mailbox_poke import Mem, pid_of  # noqa: E402

FOBJECTKEY = 0x0000000500000003


def data_of(r):
    return r.get("data") or r


def main():
    ap = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    ap.add_argument("--out", required=True)
    ap.add_argument("--build", default=None)
    ap.add_argument("--process", default=None, help="image stem, required for --poke-*")
    ap.add_argument("--refs-class", default=None)
    ap.add_argument("--refs-name", default=None, help="substring of an object name (find_instances by class first)")
    ap.add_argument("--walk-class", default=None)
    ap.add_argument("--max-targets", type=int, default=8)
    ap.add_argument("--max-walks", type=int, default=4)
    ap.add_argument("--poke-header", type=lambda s: int(s, 0), default=None)
    ap.add_argument("--poke-keys", action="store_true")
    a = ap.parse_args()
    os.makedirs(a.out, exist_ok=True)
    lines, log = [], {"args": vars(a)}

    def say(s=""):
        print(s, flush=True)
        lines.append(s)

    with PipeClient(timeout=600) as c:
        say(f"# {time.strftime('%Y-%m-%d %H:%M:%S')} build {c.assert_build(a.build)}")
        c.ensure_scanned(timeout=600, poll=3)
        p = data_of(c.request("get_pointers"))
        storage = int(str(p.get("sparse_delegates") or "0"), 16)
        say(f"# ue {p.get('ue_version')} objects {p.get('object_count')} sparse_delegates 0x{storage:X}")
        log["pointers"] = p

        targets = []
        if a.refs_class or a.refs_name:
            cls = a.refs_class or a.refs_name
            fi = data_of(c.request("find_instances", class_name=cls, exact_match=False, limit=256))
            inst = [i for i in (fi.get("instances") or []) if not (i.get("name") or "").startswith("Default__")]
            if a.refs_name:
                inst = [i for i in inst if a.refs_name.lower() in (i.get("name") or "").lower()] or inst
            targets = inst[:a.max_targets]
            say(f"refs targets ({cls}): {len(targets)} of {len(inst)}")

        mem, saved = None, []
        if a.poke_header is not None or a.poke_keys:
            if not a.process or not storage:
                raise SystemExit("--poke-* needs --process and a located sparse storage")
            mem = Mem(pid_of(a.process))
            data_ptr = mem.u64(storage)
            num = mem.i32(storage + 0x08)
            say(f"storage header: data 0x{data_ptr:X} ArrayNum {num}")
            if a.poke_keys:
                for i in range(min(max(num, 0), 64)):
                    slot = data_ptr + i * 0x60
                    key = mem.u64(slot)
                    if 0x10000 <= key < 0x7FFFFFFFFFFF and key % 8 == 0:
                        saved.append((slot, mem.read(slot, 8)))
            if a.poke_header is not None:
                saved.append((storage + 0x08, mem.read(storage + 0x08, 4)))

        try:
            for addr, orig in saved:           # apply the pokes (originals already recorded)
                if len(orig) == 8:
                    mem.write(addr, struct.pack("<Q", FOBJECTKEY))
                else:
                    mem.write(addr, struct.pack("<i", a.poke_header))
            if saved:
                say(f"POKED {len(saved)} location(s)")
            log["refs"] = []
            for t in targets:
                r = data_of(c.request("find_refs_to_uobject", addr=t["addr"], max_results=64))
                refs = r.get("references") or []
                sparse = [x for x in refs if x.get("field_type") == "MulticastSparseDelegateProperty"]
                say(f"find_refs {t.get('name')} {t['addr']}: {len(refs)} ref(s), {len(sparse)} sparse, "
                    f"scan={json.dumps(r.get('scan') or {}, sort_keys=True)}")
                for x in sparse:
                    say(f"    sparse hit: owner {x.get('owner_name')} ({x.get('owner_class')}) field {x.get('field_name')}")
                log["refs"].append({"target": t, "reply": r})
            if a.walk_class:
                fi = data_of(c.request("find_instances", class_name=a.walk_class, exact_match=False, limit=256))
                inst = [i for i in (fi.get("instances") or []) if not (i.get("name") or "").startswith("Default__")]
                log["walks"] = []
                for i in inst[:a.max_walks]:
                    w = data_of(c.request("walk_instance", addr=i["addr"], array_limit=8))
                    sp = [f for f in (w.get("fields") or []) if f.get("type") == "MulticastSparseDelegateProperty"]
                    bound = [f for f in sp if "unbound" not in str(f.get("value"))]
                    say(f"walk {i.get('name')} {i['addr']}: {len(sp)} sparse field(s), {len(bound)} not 'unbound'")
                    for f in bound:
                        say(f"    {f.get('name'):32} {str(f.get('value'))[:110]}")
                    log["walks"].append({"inst": i, "sparse": sp})
        finally:
            for addr, orig in saved:
                mem.write(addr, orig)
            bad = [hex(addr) for addr, orig in saved if mem.read(addr, len(orig)) != orig]
            if saved:
                say(f"RESTORED {len(saved)} location(s); read-back mismatches: {bad or 'none'}")
    json.dump(log, open(os.path.join(a.out, "sparse_refs.json"), "w", encoding="utf-8"), indent=1)
    open(os.path.join(a.out, "sparse_refs.txt"), "w", encoding="utf-8").write("\n".join(lines) + "\n")
    return 0


if __name__ == "__main__":
    sys.exit(main())
