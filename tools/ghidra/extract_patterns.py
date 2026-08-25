#!/usr/bin/env python3
"""Extract every AOB signature from dll/src/Himmel.h into a TSV the Ghidra
verifier can consume:  id \t target \t resolve \t pattern \t io \t opc \t tot \t adj \t pri \t src
Symbol-export / symbol-call-follow entries are emitted with pattern kind SYMBOL.

With --check, ALSO (a) assert that Himmel.h's own header summary agrees with what was
parsed, and (b) recompute every RIP entry's (instrOffset, opcodeLen, totalLen) against
its own pattern bytes — see the geometry block below — exiting non-zero if either
disagrees. Without the flag the exit code is always 0
(printing a discrepancy is not detecting one) — which is exactly how the counts in
CLAUDE.md / roadmap.md / Features.md / dll-spec.md / architecture.md drifted to four
different wrong values while Himmel.h's header stayed right. Machine-check the one copy
that has authority; the derived prose is regenerated from it by hand, and this is what
tells you to do that.
"""
import re, sys, os

_argv = [a for a in sys.argv[1:] if a != "--check"]
CHECK = "--check" in sys.argv[1:]

HIMMEL = _argv[0] if len(_argv) > 0 else r"D:\Github\UE5CEDumper\dll\src\Himmel.h"
OUT = _argv[1] if len(_argv) > 1 else "patterns.tsv"

src = open(HIMMEL, encoding="utf-8").read()

# strip // comments (patterns never contain //)
lines = []
for ln in src.splitlines():
    i = ln.find("//")
    if i >= 0:
        ln = ln[:i]
    lines.append(ln)
clean = "\n".join(lines)

# Drop #define directives (including backslash line-continuations). Without this the
# `SIG_RIP(id, pat, tgt, ...)` MACRO DEFINITION itself parses as a signature row, yielding a
# phantom entry with id="id", target="tgt" and pattern "<UNRESOLVED:pat>" — which inflated the
# reported pattern count by one and produced a spurious "SKIP unparsable" in every sweep.
_nodef, _skipping = [], False
for ln in clean.splitlines():
    if _skipping or ln.lstrip().startswith("#define"):
        _skipping = ln.rstrip().endswith("\\")
        _nodef.append("")
        continue
    _nodef.append(ln)
clean = "\n".join(_nodef)

# 1) constants: constexpr const char* AOB_X = "..." ("..." ...);
consts = {}
for m in re.finditer(r'constexpr\s+const\s+char\*\s+(\w+)\s*=\s*((?:"[^"]*"\s*)+);', clean):
    name = m.group(1)
    parts = re.findall(r'"([^"]*)"', m.group(2))
    consts[name] = "".join(parts)

# also EXPORT_* symbol constants
for m in re.finditer(r'constexpr\s+const\s+char\*\s+(EXPORT_\w+)\s*=\s*((?:"[^"]*"\s*)+);', clean):
    parts = re.findall(r'"([^"]*)"', m.group(2))
    consts[m.group(1)] = "".join(parts)

def split_args(s):
    out, depth, cur, instr = [], 0, "", False
    i = 0
    while i < len(s):
        c = s[i]
        if instr:
            cur += c
            if c == '"':
                instr = False
            elif c == '\\':
                cur += s[i+1]; i += 1
        elif c == '"':
            instr = True; cur += c
        elif c in "([{":
            depth += 1; cur += c
        elif c in ")]}":
            depth -= 1; cur += c
        elif c == "," and depth == 0:
            out.append(cur.strip()); cur = ""
        else:
            cur += c
        i += 1
    if cur.strip():
        out.append(cur.strip())
    return out

def unq(x):
    x = x.strip()
    if x.startswith('"'):
        return "".join(re.findall(r'"([^"]*)"', x))
    return x

USED_CONSTS = set()

def patof(tok):
    tok = tok.strip()
    if tok.startswith('"'):
        return unq(tok)
    USED_CONSTS.add(tok)
    return consts.get(tok, "<UNRESOLVED:%s>" % tok)

rows = []

# 2) macro forms
for m in re.finditer(r'\bSIG_RIP(_DIRECT)?\s*\(', clean):
    start = m.end()
    depth = 1; i = start
    while depth and i < len(clean):
        if clean[i] == '(': depth += 1
        elif clean[i] == ')': depth -= 1
        i += 1
    a = split_args(clean[start:i-1])
    if len(a) < 10: continue
    idn, pat, tgt, io, opc, tot, adj, pri, srcname, note = a[:10]
    rows.append(dict(id=unq(idn), target=tgt.split("::")[-1],
                     resolve="RipDirect" if m.group(1) else "RipBoth",
                     pattern=patof(pat), io=io, opc=opc, tot=tot, adj=adj, pri=pri,
                     src=unq(srcname), note=unq(note)))

for m in re.finditer(r'\bSIG_GWORLD_RIP\s*\(', clean):
    start = m.end()
    depth = 1; i = start
    while depth and i < len(clean):
        if clean[i] == '(': depth += 1
        elif clean[i] == ')': depth -= 1
        i += 1
    a = split_args(clean[start:i-1])
    if len(a) < 10: continue
    idn, pat, io, opc, tot, adj, pri, allownull, srcname, note = a[:10]
    rows.append(dict(id=unq(idn), target="GWorld", resolve="RipBoth",
                     pattern=patof(pat), io=io, opc=opc, tot=tot, adj=adj, pri=pri,
                     src=unq(srcname), note=unq(note)))

for m in re.finditer(r'\bSIG_(EXPORT|SYM_CALL)\s*\(', clean):
    start = m.end()
    depth = 1; i = start
    while depth and i < len(clean):
        if clean[i] == '(': depth += 1
        elif clean[i] == ')': depth -= 1
        i += 1
    a = split_args(clean[start:i-1])
    if len(a) < 5: continue
    idn, sym, tgt, pri, note = a[:5]
    rows.append(dict(id=unq(idn), target=tgt.split("::")[-1],
                     resolve="SymbolExport" if m.group(1) == "EXPORT" else "SymbolCallFollow",
                     pattern=patof(sym), io=0, opc=0, tot=0, adj=0, pri=pri,
                     src="EXP", note=unq(note)))

# 3) raw brace initialisers: { "ID", AOB_X, AobTarget::Y, AobResolve::Z, io, opc, tot, adj, pri, callOff, bool, "src", "note" }
for m in re.finditer(r'\{\s*("(?:[^"]*)")\s*,\s*(\w+)\s*,\s*AobTarget::(\w+)\s*,\s*AobResolve::(\w+)\s*,', clean):
    start = m.start()
    depth = 0; i = start
    while i < len(clean):
        if clean[i] == '{': depth += 1
        elif clean[i] == '}':
            depth -= 1
            if depth == 0: break
        i += 1
    a = split_args(clean[start+1:i])
    if len(a) < 12: continue
    rows.append(dict(id=unq(a[0]), target=a[2].split("::")[-1], resolve=a[3].split("::")[-1],
                     pattern=patof(a[1]), io=a[4], opc=a[5], tot=a[6], adj=a[7], pri=a[8],
                     src=unq(a[11]) if len(a) > 11 else "?",
                     note=unq(a[12]) if len(a) > 12 else ""))

seen = set()
uniq = []
for r in rows:
    if r["id"] in seen:
        continue
    seen.add(r["id"])
    uniq.append(r)

def ev(x):
    if isinstance(x, int):
        return x
    x = str(x).strip()
    try:
        return int(x, 0)
    except Exception:
        return 0

# Create the output directory. `out/` is gitignored (.gitignore:33 `[Oo]ut/`) and nothing under it
# is tracked, so on a FRESH CHECKOUT — every CI run — `out/sweep/` does not exist and this open()
# died with FileNotFoundError. Callers should not have to know that.
_outdir = os.path.dirname(os.path.abspath(OUT))
if _outdir:
    os.makedirs(_outdir, exist_ok=True)

with open(OUT, "w", encoding="utf-8") as f:
    f.write("id\ttarget\tresolve\tio\topc\ttot\tadj\tpri\tsrc\tpattern\tnote\n")
    for r in sorted(uniq, key=lambda r: (r["target"], ev(r["pri"]))):
        f.write("\t".join([r["id"], r["target"], r["resolve"], str(ev(r["io"])), str(ev(r["opc"])),
                           str(ev(r["tot"])), str(ev(r["adj"])), str(ev(r["pri"])), r["src"],
                           r["pattern"], r["note"]]) + "\n")

from collections import Counter
c = Counter((r["target"], r["resolve"]) for r in uniq)
print("total:", len(uniq))
for k, v in sorted(c.items()):
    print("  ", k, v)
bad = [r["id"] for r in uniq if "<UNRESOLVED" in r["pattern"]]
if bad:
    print("UNRESOLVED:", bad)

# DEAD CONSTANTS — declared but never referenced by any PATTERNS[] array, so never scanned for.
# This has bitten twice: AOB_GNAMES_UD1 (a UEDumper example pinning `cmp [rbp-0x18],0`, a stack
# slot) and AOB_GOBJECTS_CT2 (a bare MSVC prologue with no RIP operand at all, so it could not
# have resolved anything even if wired up). Both sat in the file for a long time looking like
# active signatures. Deliberate exceptions are whitelisted below.
NOT_IN_TABLES = {
    # Consumed directly by Genau::ResolveNameKeyTable — it de-obfuscates FNameEntry payloads
    # rather than resolving a global pointer, so it is not an AobSignature at all.
    "AOB_NAMEDECRYPT_ME1",
}
declared = {n for n in consts if n.startswith("AOB_")}
dead = sorted(declared - USED_CONSTS - NOT_IN_TABLES)
if dead:
    print("DEAD (declared but in no PATTERNS[] array — remove or wire up):")
    for d in dead:
        print("   ", d)
print("->", os.path.abspath(OUT))

# ── --check: assert Himmel.h's header summary against what we actually parsed ──────────────
# Read from `src`, NOT `clean` — the summary lives in the comment block that `clean` strips.
if CHECK:
    failures = []

    aob = sum(1 for r in uniq if r["resolve"] in ("RipBoth", "RipDirect"))
    callfollow = sum(1 for r in uniq if r["resolve"] == "CallFollow")
    symbols = sum(1 for r in uniq if r["resolve"] in ("SymbolExport", "SymbolCallFollow"))
    srctags = len({r["src"] for r in uniq})
    actual = dict(aob=aob, callfollow=callfollow, symbols=symbols,
                  total=len(uniq), srctags=srctags)

    # "= 151 AOB + 1 CallFollow + 6 symbol exports = 158 entries, over 31 distinct `source` tags"
    m = re.search(r"=\s*(\d+)\s*AOB\s*\+\s*(\d+)\s*CallFollow\s*\+\s*(\d+)\s*symbol\s+exports"
                  r"\s*=\s*(\d+)\s*entries,\s*over\s*(\d+)\s*distinct", src)
    if not m:
        # A reworded header is NOT a pass. Silently stopping the comparison is the failure
        # mode this flag exists to prevent.
        failures.append("could not find the '= N AOB + N CallFollow + N symbol exports = N "
                        "entries, over N distinct `source` tags' summary in Himmel.h — if you "
                        "reworded it, update this regex in the same commit")
    else:
        claimed = dict(zip(("aob", "callfollow", "symbols", "total", "srctags"),
                           (int(g) for g in m.groups())))
        for k in ("aob", "callfollow", "symbols", "total", "srctags"):
            if claimed[k] != actual[k]:
                failures.append("header says %s=%d, parsed %d" % (k, claimed[k], actual[k]))

    # "Signatures: the AOB pattern database — 158 entries over FIVE targets"
    m2 = re.search(r"AOB pattern database\s*[—\-–]\s*(\d+)\s*entries", src)
    if not m2:
        failures.append("could not find the 'AOB pattern database — N entries' line in Himmel.h")
    elif int(m2.group(1)) != len(uniq):
        failures.append("header intro says %s entries, parsed %d" % (m2.group(1), len(uniq)))

    # ── RIP GEOMETRY — recompute (instrOffset, opcodeLen, totalLen) against the pattern's OWN
    # bytes. Nothing else in the tree does this: the blocktest oracle covers 35 of 158 entries,
    # and `ASSERT_TABLE_ORDER` only checks priority order, so a triple that points into the
    # middle of its own instruction compiles, sorts, scans, matches — and resolves to garbage on
    # every hit, silently. Four entries shipped that way (GOBJ_PS1, GOBJ_PS6, GWLD_TQ_3,
    # GWLD_TQ_4 — audit #5 AD12/AD13/AD15/AD16); all four are caught by the rules below, and the
    # recurring slip is a single one: writing the offset of the DISPLACEMENT where the field
    # wants the offset of the INSTRUCTION.
    #
    # The resolver is Macht::ResolveRIP(matchAddr + instrOffset, opcodeLen, totalLen): it reads a
    # disp32 at instrOffset+opcodeLen and adds it to matchAddr+instrOffset+totalLen. So:
    #   * the 4 displacement bytes must be WILDCARDED wherever the pattern covers them — a
    #     literal there is proof the window is misaligned (nothing else could match), and
    #   * a pattern may stop exactly AT the displacement (GNAM_SAT425_1 does, legitimately — the
    #     disp is read from process memory, not from the pattern), but stopping 1-3 bytes INTO it
    #     means the window does not line up with where the pattern actually ends.
    # ⚠ Do NOT "simplify" this to `instrOffset + totalLen <= len(pattern)`. That is the obvious
    # rule, it catches PS1 and PS6 — and it FALSE-POSITIVES GNAM_SAT425_1, whose triple is
    # correct. The partial-window test is what separates the two.
    def _geom_failures(r):
        t = r["pattern"].split()
        n = len(t)
        io, opc, tot = ev(r["io"]), ev(r["opc"]), ev(r["tot"])
        out = []
        if io < 0 or opc < 1 or tot <= opc:
            out.append("nonsense triple (instrOffset=%d opcodeLen=%d totalLen=%d)" % (io, opc, tot))
            return out          # everything below would be noise
        # totalLen = opcodeLen + disp32(4) + immediate. GWLD_DI427_2 is the imm32 case
        # (`mov qword[rip+d32],imm32`, totalLen 11), so this is not "must be opcodeLen+4".
        imm = tot - opc - 4
        if imm not in (0, 1, 2, 4):
            out.append("totalLen-opcodeLen-4 = %d, which is not a legal immediate size "
                       "(0/1/2/4) — the disp32 cannot be where this triple says" % imm)
        if io + opc > n:
            out.append("the opcode runs past the pattern: needs %d bytes, pattern has %d"
                       % (io + opc, n))
            return out
        d0 = io + opc
        litdisp = ["[%d]=%s" % (d0 + k, t[d0 + k]) for k in range(4)
                   if d0 + k < n and t[d0 + k] != "??"]
        if litdisp:
            out.append("disp32 at %d..%d is not wildcarded (%s) — instrOffset almost certainly "
                       "names the displacement instead of the instruction"
                       % (d0, d0 + 3, " ".join(litdisp)))
        if d0 < n < io + tot:
            out.append("the pattern ends at %d, strictly inside the instruction's tail "
                       "(displacement %d..%d, instruction ends at %d) — a pattern may stop AT "
                       "the displacement (%d, the disp is read from process memory, not from the "
                       "pattern) or cover the whole instruction, but stopping between the two "
                       "means the triple is misaligned"
                       % (n, d0, io + tot - 1, io + tot, d0))
        # The byte before the disp32 is the ModRM. RIP-relative addressing is mod=00, rm=101,
        # i.e. (b & 0xC7) == 0x05. Checked only where the nibble is literal.
        mi = d0 - 1
        if 0 <= mi < n:
            tok = t[mi]
            hi, lo = tok[0], tok[1]
            if lo != "?" and (int(lo, 16) & 0x7) != 0x5:
                out.append("ModRM at %d is %s — rm=%d, but RIP-relative needs rm=101(5)"
                           % (mi, tok, int(lo, 16) & 0x7))
            if hi != "?" and (int(hi, 16) & 0xC) != 0x0:
                out.append("ModRM at %d is %s — mod=%d, but RIP-relative needs mod=00"
                           % (mi, tok, (int(hi, 16) >> 2) & 0x3))
        return out

    for r in sorted(uniq, key=lambda r: (r["target"], ev(r["pri"]))):
        if r["resolve"] not in ("RipBoth", "RipDirect"):
            continue            # SYMBOL/CallFollow entries carry the degenerate 0,0,0 triple
        for msg in _geom_failures(r):
            failures.append("%s (%s): %s" % (r["id"], r["target"], msg))

    # CLAUDE.md claims "no dead constants (extract_patterns.py checks)" — make that true.
    if bad:
        failures.append("%d signature(s) reference an unresolved constant: %s" % (len(bad), bad))
    if dead:
        failures.append("%d dead AOB constant(s) in no PATTERNS[] array: %s" % (len(dead), dead))

    if failures:
        print("\nCHECK FAILED:")
        for f in failures:
            print("  *", f)
        print("\nFor a COUNT mismatch: Himmel.h's header is the ONE authoritative copy. Fix it")
        print("first, then regenerate the derived prose in CLAUDE.md / docs/roadmap.md /")
        print("docs/Features.md / docs/dll-spec.md / docs/architecture.md (dev-log.md is")
        print("append-only — leave it).")
        print("For a GEOMETRY failure: instrOffset is the offset of the RIP INSTRUCTION's FIRST")
        print("byte (its REX/opcode), NOT of the displacement. Write out the pattern's byte map")
        print("before changing a number — every instance of this so far has been the same slip.")
        sys.exit(1)
    print("CHECK OK: %d AOB + %d CallFollow + %d symbol exports = %d entries, %d source tags"
          % (aob, callfollow, symbols, len(uniq), srctags))
