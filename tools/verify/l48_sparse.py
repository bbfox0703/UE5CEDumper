#!/usr/bin/env python3
r"""L48 `[P1-SPARSEDELEGATE-REFS]`: Find Refs must not blame the game when a sparse delegate could not be read.

    py tools/verify/l48_sparse.py locate  --state out\l48_state.json                 (pipe; UI CLOSED)
    py tools/verify/l48_sparse.py poke    --state out\l48_state.json --delegate OnActorBeginOverlap
    py tools/verify/l48_sparse.py restore --state out\l48_state.json
    py tools/verify/l48_sparse.py arm     --state out\l48_state.json                 (arm B: decryption -> 0x1000)
    py tools/verify/l48_sparse.py disarm  --state out\l48_state.json

`locate` walks FSparseDelegateStorage out of process, EXACTLY as Aura.cpp's Find Refs sparse pass does: the
outer TMap (TSetElement stride 0x60: owner UObject* at +0, inner TMap at +0x08), the inner TMap (stride
FNameSlotIn8Aligned + 0x18: the delegate FName at +0, the TSharedPtr to its FMulticastScriptDelegate at the FName
SLOT), the invocation-list header probed at pad 0 and pad 8 (LocateInvocationList), and each binding
(`8 + sizeof(FName)` apart: FWeakObjectPtr {index, serial} at +0, the function FName at +8). Names come from the
FNamePool read out of process (stock UE5 layout: Blocks[] at GNames+0x10, stride 2, len = header >> 6), and a
weak index is named through `get_object_list offset=<idx> limit=1`. It prints every (owner, delegate, target)
triple and flags target != owner: only those can be LISTED by Find Refs (Aura.cpp suppresses owner == target).
The storage is resolved lazily by the DLL, so `locate` walks the live DumperTestActor first.

`poke` writes Num = 0 into one delegate's invocation-list header (IsBoundInvocationListHeader rejects Num < 1, so
the DLL then counts it `sparse_unlocated`). The engine only sees a delegate that broadcasts nothing. `restore`
writes every poked Num back and reads it back. `arm` / `disarm` set UE5_SetObjectDecryption to 0x1000 / 0 through
a remote thread (sw1_worker_fault.call_setter), so every GObjects read faults and the scan cannot finish. Arm B is
GLOBAL: keep the window to one Find Refs click, then disarm. `poke`, `restore`, `arm` and `disarm` never open the
pipe, so they are safe while the UI holds its two lanes.
"""
import argparse
import json
import os
import struct
import sys

sys.stdout.reconfigure(encoding="utf-8", errors="replace")
HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import mailbox_poke as MP   # noqa: E402  (Mem: fail-loud RPM/WPM)

OUTER_STRIDE, OUTER_VALUE = 0x60, 0x08


def tmap_header(m, addr):
    data, num = m.u64(addr), m.i32(addr + 0x08)
    secondary = m.u64(addr + 0x20)
    bits = secondary if secondary else addr + 0x10
    return data, num, bits


def bit_set(m, bits, i):
    return (m.i32(bits + (i >> 5) * 4) >> (i & 31)) & 1


class Names:
    def __init__(self, m, gnames):
        self.m, self.g, self.cache = m, gnames, {}

    def __call__(self, comp):
        if comp in self.cache:
            return self.cache[comp]
        try:
            block = self.m.u64(self.g + 0x10 + (comp >> 16) * 8)
            entry = block + (comp & 0xFFFF) * 2
            hdr = struct.unpack("<H", self.m.read(entry, 2))[0]
            wide, n = hdr & 1, (hdr >> 6) & 0x3FF
            raw = self.m.read(entry + 2, n * (2 if wide else 1))
            s = raw.decode("utf-16-le" if wide else "latin-1", "replace")
        except SystemExit:
            s = "<fname %d?>" % comp
        self.cache[comp] = s
        return s


def locate(a):
    from pipe_client import PipeClient
    pid = MP.pid_of(a.process)
    m = MP.Mem(pid)
    with PipeClient() as c:
        print("build %s" % c.assert_build())
        c.ensure_scanned()
        insts = c.request("find_instances", class_name="DumperTestActor", exact_match=True,
                          limit=10).get("instances") or []
        actor = next(i for i in insts if not i["name"].startswith("Default__"))
        w = c.request("walk_instance", addr=actor["addr"], array_limit=4)
        for f in w.get("fields") or []:
            if "Sparse" in (f.get("type") or ""):
                print("   %s.%s (%s) = %s" % (actor["name"], f["name"], f.get("type"), str(f.get("value"))[:120]))
        gp = c.request("get_pointers")
        storage = int(gp.get("sparse_delegates") or "0", 16)
        gnames = int(gp.get("gnames") or gp.get("GNames") or "0", 16)
        off = c.request("get_offsets")
        cpn = bool(off.get("case_preserving"))
        fname_size = 0xC if cpn else 0x8
        slot = (fname_size + 7) & ~7
        inner_stride, sd_size = slot + 0x18, 8 + fname_size
        print("sparse_delegates=0x%X gnames=0x%X case_preserving=%s -> inner stride 0x%X, binding size 0x%X"
              % (storage, gnames, cpn, inner_stride, sd_size))
        if not storage or not gnames:
            raise SystemExit("FAIL: storage or GNames unresolved -- walk a sparse-bound object first")
        name = Names(m, gnames)
        obj_cache = {}

        def obj_at_index(idx):
            if idx not in obj_cache:
                r = c.request("get_object_list", offset=idx, limit=1).get("objects") or []
                obj_cache[idx] = ("%s (%s) @ %s" % (r[0]["name"], r[0]["class"], r[0]["addr"])) if r else "<none>"
            return obj_cache[idx]

        def obj_name(addr):
            r = c.request("get_object", addr="0x%X" % addr)
            return "%s (%s)" % (r.get("name"), r.get("class"))

        entries = []
        odata, onum, obits = tmap_header(m, storage)
        print("outer TMap: data=0x%X num=%d" % (odata, onum))
        for oi in range(onum):
            if not bit_set(m, obits, oi):
                continue
            oslot = odata + oi * OUTER_STRIDE
            owner = m.u64(oslot)
            if not owner:
                continue
            idata, inum, ibits = tmap_header(m, oslot + OUTER_VALUE)
            for ii in range(inum):
                if not bit_set(m, ibits, ii):
                    continue
                islot = idata + ii * inner_stride
                dname = name(m.i32(islot))
                mcd = m.u64(islot + slot)
                hdr = None
                for pad in (0, 8):
                    data, num, mx = m.u64(mcd + pad), m.i32(mcd + pad + 8), m.i32(mcd + pad + 12)
                    if 0x10000 <= data < 0x7FFFFFFFFFFF and 1 <= num <= mx:
                        hdr = (pad, data, num, mx)
                        break
                e = {"owner": "0x%X" % owner, "owner_name": obj_name(owner), "delegate": dname,
                     "mcd": "0x%X" % mcd, "pad": None, "num_addr": None, "num": None, "bindings": []}
                if hdr:
                    pad, data, num, mx = hdr
                    e.update(pad=pad, num_addr="0x%X" % (mcd + pad + 8), num=num)
                    for bi in range(num):
                        b = data + bi * sd_size
                        widx, wser = m.i32(b), m.i32(b + 4)
                        e["bindings"].append({"weak_index": widx, "serial": wser, "func": name(m.i32(b + 8)),
                                              "target": obj_at_index(widx)})
                entries.append(e)
    for e in entries:
        print("\n%s  .%s   mcd=%s pad=%s num=%s" % (e["owner_name"], e["delegate"], e["mcd"], e["pad"], e["num"]))
        for b in e["bindings"]:
            cross = "" if b["target"].split(" @ ")[-1].lower() == e["owner"].lower() else "   <== CROSS-OBJECT (listable)"
            print("     [%d] -> %s ::%s%s" % (e["bindings"].index(b), b["target"], b["func"], cross))
    json.dump({"pid": pid, "entries": entries, "poked": []}, open(a.state, "w"), indent=1)
    print("\nstate -> %s (%d delegate entries)" % (a.state, len(entries)))
    return 0


def poke(a):
    st = json.load(open(a.state))
    m = MP.Mem(st["pid"])
    e = next((x for x in st["entries"] if x["delegate"] == a.delegate and "DumperTestActor" in x["owner_name"]), None)
    if not e or not e["num_addr"]:
        raise SystemExit("FAIL: no located entry for %s" % a.delegate)
    na = int(e["num_addr"], 16)
    cur = m.i32(na)
    if cur != e["num"]:
        raise SystemExit("REFUSED: live Num %d != located %d -- re-run locate" % (cur, e["num"]))
    m.write(na, struct.pack("<i", 0))
    print("poked %s.%s Num %d -> %d at %s" % (e["owner_name"], e["delegate"], cur, m.i32(na), e["num_addr"]))
    st["poked"].append({"num_addr": e["num_addr"], "orig": cur, "delegate": a.delegate})
    json.dump(st, open(a.state, "w"), indent=1)
    return 0


def restore(a):
    st = json.load(open(a.state))
    m = MP.Mem(st["pid"])
    bad = 0
    for p in st["poked"]:
        na = int(p["num_addr"], 16)
        m.write(na, struct.pack("<i", p["orig"]))
        back = m.i32(na)
        print("restored %s Num -> %d (want %d) %s" % (p["delegate"], back, p["orig"], "OK" if back == p["orig"] else "MISMATCH"))
        bad += back != p["orig"]
    st["poked"] = []
    json.dump(st, open(a.state, "w"), indent=1)
    return 1 if bad else 0


def set_decryption(a, value):
    import sw1_worker_fault as SW
    st = json.load(open(a.state))
    pid = st["pid"]
    mod = next((x for x in SW.modules(pid) if x[0].lower() == "ue5dumper.dll"), None)
    if not mod:
        raise SystemExit("FAIL: UE5Dumper.dll not mapped in %d" % pid)
    SW.call_setter(pid, mod[1], SW.export_rva(mod[2]), value)
    print("UE5_SetObjectDecryption(0x%X) called in pid %d (%s)" % (value, pid, mod[2]))
    return 0


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("phase", choices=("locate", "poke", "restore", "arm", "disarm"))
    ap.add_argument("--process", default="DumperTest-Win64-Shipping")
    ap.add_argument("--state", required=True)
    ap.add_argument("--delegate", default="OnActorBeginOverlap")
    a = ap.parse_args()
    if a.phase == "arm":
        return set_decryption(a, 0x1000)
    if a.phase == "disarm":
        return set_decryption(a, 0)
    return {"locate": locate, "poke": poke, "restore": restore}[a.phase](a)


if __name__ == "__main__":
    sys.exit(main())
