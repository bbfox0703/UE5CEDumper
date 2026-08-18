"""CURRENT vs PROPOSED ProcessEvent pattern -- regression sweep over the real corpus.

Committed as the artifact behind the [PEHOOK-2026-08-17] claim in docs/todo.md:
"over the 22 shipped UE games in the local corpus plus both DumperTest configs,
60 candidate vtables each, not one binary changed a first match it already had."
Re-run it before touching the patterns in Frieren.cpp:

  py -c "import io,os;print('\\n'.join(os.path.join(d,f) \
      for d,_,fs in os.walk(r'D:\\UE_Analyze_data\\Game Binary backup') \
      for f in fs if f.lower().endswith('-win64-shipping.exe')))" > list.txt
  py tools/verify/pe_pattern_regression.py $(cat list.txt)

NOTE the harvester over-generates vtable bases on purpose (see vtable_bases), so
the per-offset tallies below are NOT "the ProcessEvent slot" -- only the
current-vs-proposed COMPARISON per base is meaningful. Binaries reporting
vtables=0 are packed/obfuscated and are simply not covered.

The change under test relaxes pattern 1 to tolerate a mandatory SIB byte. Relaxing a
pattern can only do two things: find a slot it used to miss (the fix), or find an
EARLIER slot on a binary it already got right (a regression -- the detector returns
the FIRST match ascending). This measures the second on every UE binary on disk.

Candidate vtables are harvested from .rdata without any PDB: any 8-aligned run of
>= MIN_RUN consecutive pointers into an executable section. That over-generates
bases, which is the conservative direction here -- more candidates is more chances
to expose a changed answer.

PASS = for every candidate vtable, proposed's first match equals current's whenever
current had one. Gaining a match where current had NONE is the fix, not a regression.
"""
import os
import re
import struct
import sys

MIN_OFF, MAX_OFF, BODY = 0x100, 0x300, 0x2000
MIN_RUN = 96          # UObject-family vtables are ~80-110 entries
MAX_BASES = 60        # per binary, to bound runtime

# Transcribed from dll/src/Frieren.cpp. mask 'x' = fixed, '?' = wildcard.
RX_PAT1 = re.compile(rb"\xf7..\x00\x00\x00\x00\x04\x00\x00", re.S)
RX_PAT2 = re.compile(rb"\xf7..\x00\x00\x00\x00\x00\x40\x00", re.S)
# PROPOSED: one extra wildcard between ModRM and disp32 (the SIB byte).
RX_PAT1_SIB = re.compile(rb"\xf7...\x00\x00\x00\x00\x04\x00\x00", re.S)
RX_PAT2_SIB = re.compile(rb"\xf7...\x00\x00\x00\x00\x00\x40\x00", re.S)


class PE:
    def __init__(self, path):
        self.data = open(path, "rb").read()
        d = self.data
        pe = struct.unpack_from("<I", d, 0x3C)[0]
        if d[pe:pe + 4] != b"PE\0\0":
            raise ValueError("not a PE")
        machine, nsec = struct.unpack_from("<HH", d, pe + 4)
        if machine != 0x8664:
            raise ValueError("not x64")
        opt = pe + 24
        if struct.unpack_from("<H", d, opt)[0] != 0x20B:
            raise ValueError("not PE32+")
        self.image_base = struct.unpack_from("<Q", d, opt + 24)[0]
        opt_size = struct.unpack_from("<H", d, pe + 20)[0]
        self.sections = []
        s = opt + opt_size
        for i in range(nsec):
            off = s + i * 40
            name = d[off:off + 8].rstrip(b"\0").decode("latin1")
            vsize, vaddr, rawsize, rawptr, = struct.unpack_from("<IIII", d, off + 8)
            chars = struct.unpack_from("<I", d, off + 36)[0]
            self.sections.append(dict(name=name, va=vaddr, vsize=vsize,
                                      ptr=rawptr, rsize=rawsize, exec=bool(chars & 0x20000000)))
        self._body = {}

    def read(self, rva, n):
        for s in self.sections:
            if s["va"] <= rva < s["va"] + max(s["vsize"], s["rsize"]):
                delta = rva - s["va"]
                if delta >= s["rsize"]:
                    return None
                return self.data[s["ptr"] + delta: s["ptr"] + delta + n]
        return None

    def is_exec_rva(self, rva):
        for s in self.sections:
            if s["exec"] and s["va"] <= rva < s["va"] + max(s["vsize"], s["rsize"]):
                return True
        return False

    def body(self, rva):
        b = self._body.get(rva)
        if b is None:
            b = self.read(rva, BODY)
            b = b"" if not b or len(b) < 0x400 else b.ljust(BODY, b"\0")
            self._body[rva] = b
        return b


def vtable_bases(pe):
    """8-aligned runs of >= MIN_RUN pointers into an executable section."""
    out = []
    for s in pe.sections:
        if s["exec"] or s["name"] not in (".rdata", ".data"):
            continue
        blob = pe.data[s["ptr"]: s["ptr"] + s["rsize"]]
        n = len(blob) // 8
        run_start, run_len = 0, 0
        for i in range(n):
            va = struct.unpack_from("<Q", blob, i * 8)[0]
            good = va > pe.image_base and pe.is_exec_rva(va - pe.image_base)
            if good:
                if run_len == 0:
                    run_start = i
                run_len += 1
            else:
                if run_len >= MIN_RUN:
                    out.append(s["va"] + run_start * 8)
                    if len(out) >= MAX_BASES:
                        return out
                run_len = 0
        if run_len >= MIN_RUN:
            out.append(s["va"] + run_start * 8)
    return out[:MAX_BASES]


def first_match(pe, vt, proposed):
    for off in range(MIN_OFF, MAX_OFF + 1, 8):
        raw = pe.read(vt + off, 8)
        if not raw or len(raw) < 8:
            continue
        va = struct.unpack("<Q", raw)[0]
        if not va or va <= pe.image_base:
            continue
        body = pe.body(va - pe.image_base)
        if not body:
            continue
        ok1 = RX_PAT1.search(body, 0, 0x400) is not None
        if proposed and not ok1:
            ok1 = RX_PAT1_SIB.search(body, 0, 0x400) is not None
        if not ok1:
            continue
        ok2 = RX_PAT2.search(body) is not None
        if proposed and not ok2:
            ok2 = RX_PAT2_SIB.search(body) is not None
        if ok2:
            return off
    return None


def check(path):
    try:
        pe = PE(path)
    except Exception as e:                                    # noqa: BLE001
        return ("SKIP", os.path.basename(path), str(e), 0, {}, {})
    bases = vtable_bases(pe)
    cur, new, regress, fixed = {}, {}, 0, 0
    for vt in bases:
        c = first_match(pe, vt, False)
        p = first_match(pe, vt, True)
        if c is not None:
            cur[c] = cur.get(c, 0) + 1
        if p is not None:
            new[p] = new.get(p, 0) + 1
        if c is not None and p != c:
            regress += 1
        if c is None and p is not None:
            fixed += 1
    status = "REGRESSION" if regress else ("FIXED" if fixed else "same")
    return (status, os.path.basename(path), "", len(bases), cur, new)


def main(paths):
    worst = 0
    for p in paths:
        status, name, err, nb, cur, new = check(p)
        if err:
            print("  SKIP %-42s %s" % (name, err))
            continue
        fmt = lambda d: ", ".join("0x%X x%d" % (k, v) for k, v in sorted(d.items())) or "none"
        print("  %-10s %-42s vtables=%-3d current[%s]  proposed[%s]"
              % (status, name, nb, fmt(cur), fmt(new)))
        if status == "REGRESSION":
            worst = 1
    print("\nRESULT: %s" % ("REGRESSION FOUND -- DO NOT SHIP" if worst else
                            "no binary changed a first-match it already had"))
    return worst


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
