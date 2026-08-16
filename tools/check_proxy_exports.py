#!/usr/bin/env python3
r"""Assert every Proxy*.def forwards the real DLL's full export surface, at the real ordinals.

WHY THIS EXISTS
  A proxy DLL is a name AND an ordinal map. Getting either half wrong is silent at build
  time and catastrophic at load time, and audit PX1 measured both halves wrong on two of
  the four proxies at once:

    * A MISSING NAME kills the game before we exist. `dinput8.dll` really exports six
      functions; ProxyDinput8.def listed five. A by-name static import of the sixth fails
      process creation with STATUS_ENTRYPOINT_NOT_FOUND -- before DllMain, before Sein has
      a log file, with no diagnostic anywhere.

    * A STOLEN ORDINAL is worse, because it "works". link.exe hands UNPINNED exports out
      in name-sorted order starting at (highest PINNED ordinal + 1). Neither hand-written
      .def pinned anything, so our UE5_* block started at @6 on dinput8 and @10 on version
      -- exactly on top of GetdfDIJoystick and of VerFindFileA..VerQueryValueW. An ordinal
      import there does not fail; it CALLS UE5_AutoStart and gets `true` back where an
      LPCDIDATAFORMAT is expected, then dereferences address 1.

  The two GENERATED .def files (dxgi, winmm -- scripts/gen_proxy_forwarders.py) were
  correct; the two HAND-WRITTEN ones were both wrong, in the same way, for the same
  reason. That is the exact failure family tools/check_derived_counts.py and
  tools/check_mailbox_contract.py exist for: a fact stated by hand that nothing
  re-derives. `ProxyDinput8.def:4` said "Maps the 5 dinput8 exports" and that wrong number
  was then copied into three more comments, which made it look verified.

WHY A COMMITTED BASELINE RATHER THAN READING System32 IN CI
  The pinned ordinals are a machine-local fact (PX1 residual risk #2). Reading System32 in
  CI would make an unrelated PR go red whenever the runner's Windows build exports a
  different set than the maintainer's -- and `dxgi.dll` genuinely does vary by build. So
  the measured tables are committed to tools/pe/proxy-export-baseline.tsv (same pattern as
  tools/pe/aob-specificity-baseline.tsv, which CI already compares byte-wise), the default
  check is .def <-> baseline and needs no System32 at all, and re-measuring is an explicit
  opt-in. This also makes the baked ordinal map a recorded, re-derivable artifact instead
  of a number nobody can re-check -- which was the residual risk itself.

WHAT IS CHECKED (default mode, deterministic, stdlib-only, <1s)
  1. every named export in the baseline has a .def entry           (missing name -> FAIL)
  2. that entry pins @N, and N equals the real ordinal             (unpinned/wrong -> FAIL)
  3. no two .def entries claim the same ordinal                    (collision -> FAIL)
  4. max pinned ordinal >= max real ordinal                        (see below -> FAIL)
  5. no .def entry pins an ordinal the real DLL exports NONAME     (misbind -> FAIL)
  Tolerated and merely reported: a .def entry the baseline does not know (a newer Windows,
  or a deliberate extra), and real NONAME ordinals with no .def entry (winmm has one).

  Rule 4 is the one that is easy to miss and is why "pin the new export only" would have
  been worse than the bug: the UE5_* block is deliberately unpinned, so it lands at
  (max pinned + 1). Keeping max-pinned at the top of the real range is what structurally
  guarantees our exports can never occupy a real slot -- no per-symbol check required.

NEGATIVE CONTROLS -- all six were run and observed to FAIL, on temp copies of the .def
(a --root pointing at a scratch tree, so the real tree was never mutated):
    0. UNMODIFIED tree                     -> passes, 0 errors  (control for the controls)
    1. GetdfDIJoystick line deleted        -> 2 errors (rules 1, 4)
    2. all six @N pins stripped            -> 7 errors (rules 2 x6, 4)
    3. only @6 pinned, @1..@5 left bare    -> 5 errors (rule 2 x5)
    4. @5 duplicated onto GetdfDIJoystick  -> 3 errors (rules 2, 3, 4)
    5. an @2 entry added to ProxyWinmm.def -> 1 error  (rule 5, the NONAME slot)
    6. ProxyVersion.def pins reverted      -> 18 errors (rules 2 x17, 4)  <- the shipped bug
  Case 6 is the one that matters: it reproduces PX1's version.dll half exactly, so this
  check is known to catch the defect it was written for and not merely something like it.
  And the control that matters next: dxgi and winmm PASS unmodified, with no edits -- so
  the check distinguished the two broken .defs from the two correct ones rather than
  failing on everything, which is the only way to tell a real gate from a noisy one.

Usage:
  py tools/check_proxy_exports.py                  # .def <-> baseline (what CI runs)
  py tools/check_proxy_exports.py --list           # ...and print every entry compared
  py tools/check_proxy_exports.py --verify-system  # baseline <-> this machine's System32
  py tools/check_proxy_exports.py --artifacts      # built proxy DLLs <-> baseline
  py tools/check_proxy_exports.py --refresh        # re-measure System32, rewrite baseline
"""
import argparse
import os
import re
import struct
import sys

BASELINE = os.path.join("tools", "pe", "proxy-export-baseline.tsv")

# .def stem -> (real DLL name, source of truth for the forwarding block).
# Derived from dll/CMakeLists.txt's /DEF: + OUTPUT_NAME pairs; a fifth proxy adds a row.
PROXIES = {
    "ProxyDinput8": "dinput8",
    "ProxyVersion": "version",
    "ProxyDxgi": "dxgi",
    "ProxyWinmm": "winmm",
}

# Where a built proxy may sit, in preference order (--artifacts).
ARTIFACT_DIRS = [os.path.join("build", "dll"), os.path.join("dist", "proxy")]

NONAME = "*NONAME*"


# ---- PE export-table reader -------------------------------------------------
# Deliberately a copy of scripts/gen_proxy_forwarders.py::read_exports rather than an
# import: that file is the GENERATOR and this is the CHECK on its output, so sharing the
# parser would let one bug agree with itself. The 64-bit guard below is reused verbatim,
# because it is the trap that would silently invalidate everything downstream.

def read_exports(path):
    """Return (named, noname_ordinals): named = [(name, ordinal), ...] sorted by ordinal."""
    with open(path, "rb") as fh:
        d = fh.read()
    pe = struct.unpack_from("<I", d, 0x3C)[0]
    magic = struct.unpack_from("<H", d, pe + 24)[0]
    machine = struct.unpack_from("<H", d, pe + 4)[0]
    # Refuse a 32-bit source outright. Under 32-bit Python, WOW64 redirects
    # %SystemRoot%\System32 to SysWOW64, whose export tables have DIFFERENT ordinals for
    # the same names (measured: winmm 180 named exports in System32 vs 192 in SysWOW64,
    # 174 shared names at different ordinals). The parse would succeed and the baseline
    # would be wholesale wrong with nothing downstream able to tell.
    if magic != 0x20B or machine != 0x8664:
        raise RuntimeError(
            "%s is not a 64-bit PE (magic=0x%X, machine=0x%04X); expected magic=0x20B "
            "machine=0x8664. Under 32-bit Python, WOW64 redirects System32 to SysWOW64 "
            "-- re-run with 64-bit Python." % (path, magic, machine))
    dd = pe + 24 + 112
    nsec = struct.unpack_from("<H", d, pe + 6)[0]
    so = pe + 24 + struct.unpack_from("<H", d, pe + 20)[0]
    secs = [(struct.unpack_from("<I", d, so + i * 40 + 12)[0],
             max(struct.unpack_from("<I", d, so + i * 40 + 8)[0], 1),
             struct.unpack_from("<I", d, so + i * 40 + 20)[0]) for i in range(nsec)]

    def rva2off(r):
        for va, vs, pr in secs:
            if va <= r < va + vs:
                return pr + (r - va)
        return None

    off = rva2off(struct.unpack_from("<I", d, dd)[0])
    if off is None:
        raise RuntimeError("%s has no export directory" % path)

    base = struct.unpack_from("<I", d, off + 16)[0]
    n_func, n_name = struct.unpack_from("<II", d, off + 20)
    addr_rva, name_rva, ord_rva = struct.unpack_from("<III", d, off + 28)
    a_off, n_off, o_off = rva2off(addr_rva), rva2off(name_rva), rva2off(ord_rva)

    by_index = {}
    for i in range(n_name):
        no = rva2off(struct.unpack_from("<I", d, n_off + i * 4)[0])
        by_index[struct.unpack_from("<H", d, o_off + i * 2)[0]] = \
            d[no:d.index(b"\0", no)].decode("ascii")

    named, noname = [], []
    for i in range(n_func):
        if struct.unpack_from("<I", d, a_off + i * 4)[0] == 0:
            continue                      # gap in the ordinal space, not an export
        (named.append((by_index[i], base + i)) if i in by_index
         else noname.append(base + i))
    named.sort(key=lambda t: t[1])
    return named, noname


# ---- .def parser ------------------------------------------------------------
# Grammar of an EXPORTS line: name [= internal] [@ordinal [NONAME]] [DATA] [PRIVATE]
DEF_LINE = re.compile(
    r"^\s*(?P<name>[A-Za-z_?$][\w?$@]*)"
    r"(?:\s*=\s*(?P<impl>[\w?$@.]+))?"
    r"(?:\s*@(?P<ord>\d+))?"
    r"(?P<flags>(?:\s+(?:NONAME|DATA|PRIVATE|RESIDENTNAME))*)\s*$")


def parse_def(path):
    """Return [(name, ordinal_or_None, lineno), ...] for the EXPORTS section."""
    out, in_exports = [], False
    with open(path, "r", encoding="utf-8") as fh:
        for lineno, raw in enumerate(fh, 1):
            line = raw.split(";", 1)[0].rstrip()
            if not line.strip():
                continue
            head = line.strip().split()[0].upper()
            if head in ("LIBRARY", "NAME", "VERSION", "STACKSIZE", "HEAPSIZE",
                        "SECTIONS", "DESCRIPTION"):
                in_exports = False
                continue
            if head == "EXPORTS":
                in_exports = True
                rest = line.strip()[len("EXPORTS"):].strip()
                if not rest:
                    continue
                line = rest
            if not in_exports:
                continue
            m = DEF_LINE.match(line)
            if not m:
                out.append((None, None, lineno))   # unparsable -> reported as an error
                continue
            o = m.group("ord")
            out.append((m.group("name"), int(o) if o else None, lineno))
    return out


# ---- baseline ---------------------------------------------------------------

def baseline_path(root):
    return os.path.join(root, BASELINE)


def load_baseline(root):
    """Return {proxy_stem: {"named": [(name, ord)], "noname": [ord], "meta": str}}."""
    path = baseline_path(root)
    if not os.path.isfile(path):
        return None
    data, meta = {}, ""
    with open(path, "r", encoding="utf-8") as fh:
        for raw in fh:
            line = raw.rstrip("\n")
            if line.startswith("#!"):
                meta = line[2:].strip()
                continue
            if not line or line.startswith("#") or line.startswith("proxy\t"):
                continue
            stem, ordinal, name = line.split("\t")
            e = data.setdefault(stem, {"named": [], "noname": []})
            (e["noname"].append(int(ordinal)) if name == NONAME
             else e["named"].append((name, int(ordinal))))
    for e in data.values():
        e["named"].sort(key=lambda t: t[1])
        e["noname"].sort()
        e["meta"] = meta
    return data


def measure_system(verbose=False):
    """Read every proxied DLL from the real System32. Returns the same shape as load_baseline."""
    sysdir = os.path.join(os.environ.get("SystemRoot", r"C:\Windows"), "System32")
    data = {}
    for stem, dll in sorted(PROXIES.items()):
        p = os.path.join(sysdir, dll + ".dll")
        if not os.path.isfile(p):
            raise RuntimeError("%s not found -- cannot measure %s" % (p, stem))
        named, noname = read_exports(p)
        data[stem] = {"named": named, "noname": noname, "meta": ""}
        if verbose:
            print("  measured %-14s %3d named, noname=%s" % (dll, len(named), noname or "-"))
    return data


def write_baseline(root, data):
    path = baseline_path(root)
    os.makedirs(os.path.dirname(path), exist_ok=True)
    ver = sys.getwindowsversion() if hasattr(sys, "getwindowsversion") else None
    stamp = "measured-on Windows %s.%s.%s" % (ver.major, ver.minor, ver.build) if ver else \
            "measured-on unknown"
    lines = [
        "# proxy-export-baseline.tsv -- the real System32 export tables that each",
        "# dll/src/Proxy*.def pins its @ordinals against.",
        "#",
        "# GENERATED by `py tools/check_proxy_exports.py --refresh`. Do not hand-edit: the",
        "# whole point is that the ordinal map is re-derived from the OS rather than typed.",
        "#",
        "# Committed rather than read live in CI because these tables vary by Windows build",
        "# (dxgi especially), and a runner whose System32 differs from the maintainer's must",
        "# not redden an unrelated PR. `--verify-system` re-checks this file against the",
        "# machine it runs on; `--refresh` rewrites it. A name here that a given Windows",
        "# lacks is fine (we simply forward one export nobody calls); the reverse is not.",
        "#",
        "# %s marks an ordinal the real DLL exports WITHOUT a name. Those are deliberately" % NONAME,
        "# not forwarded (we cannot name them), which leaves a NULL EAT slot -- so an ordinal",
        "# import of one fails loudly with STATUS_ORDINAL_NOT_FOUND instead of misbinding.",
        "#!%s" % stamp,
        "proxy\tordinal\tname",
    ]
    for stem in sorted(data):
        e = data[stem]
        rows = [(o, n) for n, o in e["named"]] + [(o, NONAME) for o in e["noname"]]
        for o, n in sorted(rows):
            lines.append("%s\t%d\t%s" % (stem, o, n))
    with open(path, "w", encoding="utf-8", newline="\n") as fh:
        fh.write("\n".join(lines) + "\n")
    return path


# ---- the check --------------------------------------------------------------

def check_defs(root, base, verbose=False):
    errors, notes = [], []
    for stem in sorted(PROXIES):
        def_path = os.path.join(root, "dll", "src", stem + ".def")
        rel = os.path.relpath(def_path, root).replace("\\", "/")
        if not os.path.isfile(def_path):
            errors.append("%s: not found (PROXIES lists it; add the .def or drop the row)" % rel)
            continue
        if stem not in base:
            errors.append("%s: no rows in %s -- run --refresh" % (rel, BASELINE))
            continue

        entries = parse_def(def_path)
        for name, _o, lineno in entries:
            if name is None:
                errors.append("%s:%d: unparsable EXPORTS line" % (rel, lineno))
        entries = [e for e in entries if e[0] is not None]
        by_name = {}
        for name, o, lineno in entries:
            by_name.setdefault(name, (o, lineno))

        real = base[stem]["named"]
        real_ords = {o: n for n, o in real}

        # 1 + 2: full name coverage, pinned at the real ordinal.
        for name, ordinal in real:
            if name not in by_name:
                errors.append(
                    "%s: real %s.dll exports '%s' @%d but the .def has no entry -- a by-name "
                    "import fails process creation, and @%d is handed to whichever unpinned "
                    "export sorts there" % (rel, PROXIES[stem], name, ordinal, ordinal))
                continue
            got, lineno = by_name[name]
            if got is None:
                errors.append(
                    "%s:%d: '%s' is not pinned; the real ordinal is @%d. Unpinned exports are "
                    "assigned from (max pinned + 1) in name-sorted order, so an ordinal import "
                    "silently binds to a different function" % (rel, lineno, name, ordinal))
            elif got != ordinal:
                errors.append("%s:%d: '%s' is pinned @%d but the real ordinal is @%d"
                              % (rel, lineno, name, got, ordinal))

        # 3: no two entries claim one ordinal.
        seen = {}
        for name, o, lineno in entries:
            if o is None:
                continue
            if o in seen:
                errors.append("%s:%d: ordinal @%d claimed by both '%s' and '%s'"
                              % (rel, lineno, o, seen[o], name))
            seen[o] = name

        # 4: the unpinned UE5_* block starts at (max pinned + 1), so keeping max-pinned at
        #    the top of the real range is what keeps it out of the real ordinal space.
        max_real = max([o for _n, o in real] + base[stem]["noname"] or [0])
        max_pinned = max(seen) if seen else 0
        if max_pinned < max_real:
            errors.append(
                "%s: highest pinned ordinal is @%d but real %s.dll goes up to @%d -- the "
                "unpinned UE5_* exports start at @%d and would occupy real slots"
                % (rel, max_pinned, PROXIES[stem], max_real, max_pinned + 1))

        # 5: never pin an entry onto a real NONAME ordinal.
        for o in base[stem]["noname"]:
            if o in seen:
                errors.append(
                    "%s: '%s' is pinned @%d, which real %s.dll exports WITHOUT a name. Leave "
                    "it empty so an ordinal import fails loudly rather than misbinding"
                    % (rel, seen[o], o, PROXIES[stem]))
            else:
                notes.append("%s: @%d is a real NONAME export, deliberately not forwarded"
                             % (rel, o))

        # Tolerated: .def entries the baseline does not know. Only report the ones inside
        # the real ordinal range -- above it is just our own UE5_* block.
        for name, o, _l in entries:
            if o is not None and name not in real_ords.values() and o <= max_real:
                notes.append("%s: '%s' @%d is not in the baseline (newer Windows?)" % (rel, name, o))

        if verbose:
            print("  %-16s %3d real named exports, %3d .def entries, max pinned @%d"
                  % (stem, len(real), len(entries), max_pinned))
    return errors, notes


def check_artifacts(root, base, verbose=False):
    """Compare the BUILT proxy DLLs against the baseline -- the post-build half of PX1."""
    errors, notes, checked = [], [], 0
    for stem, dll in sorted(PROXIES.items()):
        for d in ARTIFACT_DIRS:
            path = os.path.join(root, d, dll + ".dll")
            if not os.path.isfile(path):
                continue
            rel = os.path.relpath(path, root).replace("\\", "/")
            if stem not in base:
                errors.append("%s: no baseline rows for %s" % (rel, stem))
                continue
            named, _noname = read_exports(path)
            ours = {n: o for n, o in named}
            checked += 1
            for name, ordinal in base[stem]["named"]:
                if name not in ours:
                    errors.append("%s: does not export '%s' (real %s.dll does, @%d)"
                                  % (rel, name, dll, ordinal))
                elif ours[name] != ordinal:
                    errors.append("%s: exports '%s' @%d; real %s.dll has it @%d"
                                  % (rel, name, ours[name], dll, ordinal))
            real_names = {n for n, _o in base[stem]["named"]}
            stolen = [(o, n) for n, o in named
                      if n not in real_names and o in {oo for _nn, oo in base[stem]["named"]}]
            for o, n in sorted(stolen):
                errors.append("%s: '%s' sits at @%d, which real %s.dll uses for '%s'"
                              % (rel, n, o, dll, dict((oo, nn) for nn, oo in base[stem]["named"])[o]))
            first_ours = min([o for n, o in named if n not in real_names] or [0])
            if verbose:
                print("  %-24s %3d exports, real block intact, ours start @%d"
                      % (rel, len(named), first_ours))
    if checked == 0:
        # Hard error, not a note. --artifacts is an explicit request to check the binaries;
        # reporting OK because there were none to check is a gate that guards nothing, which
        # is exactly how this repo's last build-time guard shipped green (working-lessons 2.2).
        errors.append("--artifacts found no built proxy DLLs under %s -- nothing was checked. "
                      "Build with -Target DLL first, or drop --artifacts."
                      % " or ".join(d.replace("\\", "/") for d in ARTIFACT_DIRS))
    return errors, notes


def check_system(base, verbose=False):
    """Report where this machine's System32 disagrees with the committed baseline."""
    errors = []
    live = measure_system(verbose)
    for stem in sorted(PROXIES):
        dll = PROXIES[stem]
        want = {n: o for n, o in base.get(stem, {"named": []})["named"]}
        got = {n: o for n, o in live[stem]["named"]}
        for n, o in sorted(got.items(), key=lambda t: t[1]):
            if n not in want:
                errors.append("%s.dll: this machine exports '%s' @%d, absent from the baseline"
                              % (dll, n, o))
            elif want[n] != o:
                errors.append("%s.dll: '%s' is @%d here, @%d in the baseline"
                              % (dll, n, o, want[n]))
        for n, o in sorted(want.items(), key=lambda t: t[1]):
            if n not in got:
                errors.append("%s.dll: baseline has '%s' @%d, this machine does not export it"
                              % (dll, n, o))
    return errors


def main():
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--root", default=None, help="repo root (default: inferred)")
    ap.add_argument("--list", action="store_true", dest="verbose",
                    help="print what was parsed before checking")
    ap.add_argument("--refresh", action="store_true",
                    help="re-measure System32 and rewrite the baseline")
    ap.add_argument("--verify-system", action="store_true",
                    help="compare the baseline against THIS machine's System32")
    ap.add_argument("--artifacts", action="store_true",
                    help="also compare the built proxy DLLs against the baseline")
    args = ap.parse_args()

    root = args.root or os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

    if args.refresh:
        if os.name != "nt":
            print("check_proxy_exports: --refresh needs Windows (reads System32)")
            return 2
        path = write_baseline(root, measure_system(args.verbose))
        print("check_proxy_exports: rewrote %s -- review the diff before committing"
              % os.path.relpath(path, root).replace("\\", "/"))
        return 0

    base = load_baseline(root)
    if base is None:
        print("check_proxy_exports: %s missing -- run --refresh on Windows to create it"
              % BASELINE)
        return 1

    errors, notes = check_defs(root, base, args.verbose)

    if args.artifacts:
        e, n = check_artifacts(root, base, args.verbose)
        errors += e
        notes += n

    if args.verify_system:
        if os.name != "nt":
            print("check_proxy_exports: --verify-system needs Windows, skipping that half")
        else:
            drift = check_system(base, args.verbose)
            if drift:
                print("check_proxy_exports: baseline vs this machine's System32 -- %d difference(s)"
                      % len(drift))
                for d in drift:
                    print("  ! %s" % d)
                print("\nThe baseline was measured elsewhere (%s). A name the baseline has and"
                      % (base[next(iter(base))]["meta"] or "unknown build"))
                print("this machine lacks is harmless. The reverse means a real export is")
                print("unforwarded on this Windows -- re-run --refresh and re-pin the .def.")
                errors.append("System32 drift: %d difference(s) (listed above)" % len(drift))
            else:
                print("check_proxy_exports: baseline matches this machine's System32")

    for n in notes:
        print("  . %s" % n)

    if errors:
        print("\ncheck_proxy_exports: %d problem(s)\n" % len(errors))
        for e in errors:
            print("  - %s" % e)
        print("\nA proxy DLL is a name map AND an ordinal map. A missing name kills the game")
        print("before we have a log file; a stolen ordinal calls the wrong function and")
        print("'works'. Fix dll/src/Proxy*.def -- and remember a .def line exports nothing on")
        print("its own, it needs an implementation in the matching Lugner_*.cpp/.asm.")
        return 1
    print("check_proxy_exports: OK -- %d proxies forward every real export at the real ordinal"
          % len(PROXIES))
    return 0


if __name__ == "__main__":
    sys.exit(main())
