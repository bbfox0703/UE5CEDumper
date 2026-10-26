r"""Which UE exes were linked by lld-link (a clang build) rather than MSVC's link.exe -- decided from the PE alone.

Why this exists ([VND584-05]): the ProcessEvent fingerprint's byte-narrowed second pass exists for clang-built
binaries, and the question "does the corpus hold enough of them" needs a test that does not lie. A missing Rich header
is NOT that test. MEASURED 2026-10-27 over the corpus and both Steam libraries: eight distinct UE exes carry no Rich
header; one of them, Avowed, is an lld build, and the other seven carry an MSVC mark (a toolset linker minor, or
VC_FEATURE / POGO debug entries).

The three signals, strongest first:
  * debug-directory entry types: MSVC's link.exe writes VC_FEATURE (12) and POGO (13); lld-link writes neither.
    Shipping builds usually strip the whole directory, so this decides only when one is present.
  * the optional header's linker version: lld-link always writes 14.00, link.exe its own toolset minor (14.29 for
    VS 2019 16.11, 14.38 / 14.43 for VS 2022). 14.00 is also VS 2015's own link.exe, which is why it decides only
    together with the next signal.
  * the Rich header in the DOS stub: link.exe always writes it, lld-link never does, a protector may strip it.
Verdict: anything with an MSVC mark (a Rich header, an MSVC debug entry, a linker minor other than 00) is "msvc";
linker 14.00 with a debug directory holding no MSVC entry is "lld"; linker 14.00 with no Rich header and no debug
directory at all is "lld*" -- lld's signature, or a VS 2015 link.exe build with both marks stripped, which a UE5 title
cannot be (UE5 needs VS 2019 or later). Avowed (UE 5.3) is an "lld": CODEVIEW and REPRO entries, no MSVC one.

    py tools/verify/linker_survey.py <exe> [<exe> ...]
    py tools/verify/linker_survey.py --scan <root> [<root> ...]     # every UE-sized exe under each root
"""
import os
import struct
import sys

sys.stdout.reconfigure(encoding="utf-8", errors="replace")

MSVC_DEBUG = {12: "VC_FEATURE", 13: "POGO"}
SKIP = ("crashreport", "unrealcefsubprocess", "epicwebhelper", "crashpad", "redist", "easyanticheat", "eac_",
        "installer", "setup", "vc_", "dxsetup", "ue4prereq", "uninstall")
MIN_SIZE = 20 * 1024 * 1024   # a UE game exe; launchers and helpers are far smaller


def signature(path):
    """(rich, linker 'M.mm', debug type ids, verdict) for one PE file."""
    with open(path, "rb") as fh:
        head = fh.read(0x1000)
        if head[:2] != b"MZ":
            raise ValueError("not a PE file")
        lfanew = struct.unpack_from("<I", head, 0x3C)[0]
        fh.seek(lfanew)
        nt = fh.read(24 + 240)
        if nt[:4] != b"PE\0\0":
            raise ValueError("no PE signature")
        nsec, = struct.unpack_from("<H", nt, 6)
        optsz, = struct.unpack_from("<H", nt, 20)
        magic, = struct.unpack_from("<H", nt, 24)
        lmaj, lmin = nt[26], nt[27]
        dirs = 24 + (112 if magic == 0x20B else 96)
        dbg_rva, dbg_size = struct.unpack_from("<II", nt, dirs + 6 * 8)
        fh.seek(lfanew + 24 + optsz)
        sect = fh.read(nsec * 40)
        types = []
        for i in range(nsec):
            vsz, va, rsz, raw = struct.unpack_from("<IIII", sect, i * 40 + 8)
            if dbg_rva and va <= dbg_rva < va + max(vsz, rsz):
                fh.seek(raw + dbg_rva - va)
                block = fh.read(dbg_size)
                types = [struct.unpack_from("<I", block, j * 28 + 12)[0] for j in range(len(block) // 28)]
                break
    rich = b"Rich" in head[0x80:min(lfanew, len(head))]
    msvc_debug = any(t in MSVC_DEBUG for t in types)
    linker = "%d.%02d" % (lmaj, lmin)
    if rich or msvc_debug or (lmaj, lmin) != (14, 0):
        verdict = "msvc"
    elif types:
        verdict = "lld"
    else:
        verdict = "lld*"
    return rich, linker, types, verdict


def ue_exes(root):
    for d, _subs, files in os.walk(root):
        for f in files:
            low = f.lower()
            if low.endswith(".exe") and not any(s in low for s in SKIP):
                p = os.path.join(d, f)
                try:
                    if os.path.getsize(p) >= MIN_SIZE:
                        yield p
                except OSError:
                    pass


def main(argv):
    if not argv:
        print(__doc__)
        return 2
    paths = []
    if argv[0] == "--scan":
        for root in argv[1:]:
            paths.extend(ue_exes(root))
    else:
        paths = argv
    counts = {}
    for p in sorted(set(paths)):
        try:
            rich, linker, types, verdict = signature(p)
        except (OSError, ValueError, struct.error) as e:
            print("%-6s %s  (%s)" % ("ERROR", p, e))
            continue
        counts[verdict] = counts.get(verdict, 0) + 1
        names = ",".join(MSVC_DEBUG.get(t, str(t)) for t in types) or "-"
        print("%-5s rich=%-3s linker=%s debug=[%s]  %s" % (verdict, "yes" if rich else "no", linker, names, p))
    print("\n%d exe(s): %s" % (sum(counts.values()), ", ".join("%s %d" % kv for kv in sorted(counts.items()))))
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
