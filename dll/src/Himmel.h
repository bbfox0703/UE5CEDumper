#pragma once

#include <cstdint>
#include <cstddef>   // size_t — used by the compile-time table-order assertions below

// ============================================================
// Himmel — 欣梅爾 (勇者 — The Hero, Remembered Forever)
// Signatures: the AOB pattern database — 158 entries over FIVE targets
//
// Every byte-pattern signature the scanner uses lives in this file, for all five
// AobTarget values:
//
//   GObjects         FUObjectArray / GUObjectArray            55 AOB + 1 symbol export
//   GNames           FNamePool (4.23+) or TNameEntryArray      29 AOB + 1 CallFollow
//                                                              + 3 symbol exports
//                                                              (CT2 removed b2407 — see note)
//   GWorld           UWorldProxy                               50 AOB + 1 symbol export
//                                                              (V2/V4/V5/V6 removed b2409)
//   SparseDelegates  FSparseDelegateStorage::SparseDelegates   10 AOB — lazily resolved on
//                    (UE 4.23+)                                  first sparse-delegate
//                                                                drill-down, NOT in FindAll
//   GEngine          the &GEngine SLOT, not the object          7 AOB + 1 symbol export —
//                                                                resolved AFTER GObjects/GNames
//                                                                because its validator needs
//                                                                reflection (see that section)
//
//   = 151 AOB + 1 CallFollow + 6 symbol exports = 158 entries, over 31 distinct `source` tags
//     (counting the combined ones like DI427+SP57, which record every binary that vouches).
//
// THESE COUNTS GO STALE SILENTLY — regenerate them, do not hand-edit:
//   py tools/ghidra/extract_patterns.py dll/src/Himmel.h out.tsv
// prints exactly what it parses out of here. This block was once duplicated by a second summary
// at the BOTTOM of the file, which is precisely how it drifted: the header delegated authority
// downward, stopped being maintained, and ended up four patterns short while the tail was right.
// One copy only. (Merged build 2478.)
//
// HOW TO ADD NEW PATTERNS:
//   1. Add a constexpr const char* in the appropriate section
//   2. Name it AOB_{TARGET}_{SOURCE}{N} (e.g., AOB_GOBJECTS_RE3)
//   3. Add a comment with: opcode meaning, UE version, game
//   4. Add an AobSignature entry to the corresponding PATTERNS[] array —
//      **in priority order**. Every table is sorted, and a static_assert at the bottom of
//      this file enforces both sortedness and uniqueness of priorities. Pick the band from
//      how SPECIFIC the pattern is (count its LITERAL, non-wildcard bytes), per the band
//      table above the arrays — not from how new it is or who contributed it.
//   5. VERIFY IT AGAINST THE CORPUS before trusting it:
//         bash tools/ghidra/sweep.sh && py tools/ghidra/aggregate_sweep.py out/sweep
//      70 programs, 55 with ground truth, UE 4.10-5.8. The bar is: correct on the binary
//      it was mined from, and zero hits *or* correct everywhere else. A pattern that looks
//      clean on one binary routinely produces decoys on another engine version — that is the
//      entire point of the multi-binary gauntlet. See tools/ghidra/GROUND-TRUTH.md.
//
// Sources (the `source` field of each AobSignature; ID ranges are what ACTUALLY exists here,
// not what was once contributed — D7_1, GNAM_CT2, GOBJ_CT2, GNAM_UD1 and GWLD_V2/V4/V5/V6 have
// all been removed, each documented at its old site with the byte string and the reason):
//   V       : Original UE5CEDumper patterns (V1-V13, per target)
//   PS      : patternsleuth (PS1-PS7)          github.com/trumank/patternsleuth
//   RE      : RE-UE4SS CustomGameConfigs (RE1-RE3)  github.com/UE4SS-RE/RE-UE4SS
//   CT      : UE4 Dumper.CT (CT1/CT3/CT4)      vendor/UE4 Dumper.CT
//   UD      : UEDumper (GOBJ_UD1, GNAM_UD2)    github.com/Spuckwaffel/UEDumper
//   EXP     : MSVC mangled symbol exports (GObjects / GWorld / GEngine / FName accessors)
//   AV      : Avowed (Obsidian, UE 5.3 — packed 20-byte FUObjectItem)
//   ES2     : Everspace 2 (UE 5.5)
//   SF      : SatisfFactory (UE 5.3, modular build — patterns in DLLs)
//   TQ      : TQ2 (UE 5.x)
//   G42     : UE 4.2 game analysis (docs/UE 4.2 AOBs.txt)
//   G427    : UE 4.27 game analysis (work/UE 4.27 AOBs.txt)
//   SAT422  : Satisfactory old UE 4.22 build analysis (work/SF UE 4.22 AOBs.txt)
//   ES53    : Everspace 2 UE 5.3 build analysis (work/ES2 UE 5.3 AOBs.txt)
//   SAT425  : Satisfactory UE 4.25 build analysis (work/SF UE 4.25 AOBs.txt)
//   SAT426  : Satisfactory UE 4.26 build analysis (work/SF UE 4.26 AOBs.txt)
//   SAT52   : Satisfactory UE 5.2 build analysis (work/SF UE 5.21 AOBs.txt)
//   OT      : Octopath Traveller (UE4, Ghidra + CE analysis, codename "Kingship")
//   GH      : Ghidra cross-game analysis (aob_export/analysis_report.md)
//   ME      : MindsEye (Build A Rocket Boy, UE 5.4.4 licensee fork — capstone + .pdata analysis).
//             NOTE: AOB_NAMEDECRYPT_ME1 is deliberately NOT in any PATTERNS[] array — it does
//             not resolve a global pointer, so Genau::ResolveNameKeyTable consumes it directly.
//   MEL55   : Meltopia (UE 5.5, PDB via the MSDIA loader — PDB-Universal fails on that file)
//   PAL51   : Palworld (UE 5.1, NO PDB — the corpus's only 5.1 sample). Ground truth recovered
//             by disassembly + pattern consensus rather than symbols; see the SPARSE_PAL51_1
//             comment for how the sparse-delegate and GObjects addresses were each confirmed
//             structurally before anything was mined from them.
//   SP57    : Solarpunk (rokaplay, UE 5.7 — ships a full PDB; symbols + xrefs mined offline
//             via Ghidra headless, then every candidate verified unique against the .text
//             image before inclusion — see docs/reversing-nonstandard-ue-games.md)
//   DI427   : DropIn - VR Battle Royale (UE 4.27.2, CL-18319896 — ships a full 286 MB PDB;
//             the project's FIRST symbolised UE 4.27 oracle). Mined with the same Ghidra
//             headless flow as SP57, but every candidate additionally had to survive a
//             THREE-BINARY gauntlet before inclusion: UNIQUE-OK (every hit resolves to the
//             true VA, zero decoys) on DropIn, and zero hits *or* correct on Solarpunk
//             (UE 5.7) and Avowed (UE 5.3, packed 20-byte items). See
//             tools/ghidra/scan_patterns.java — it reports hits/ok/decoy AND whether a
//             correct hit sorts before its decoys, which is what actually decides safety
//             for a weakly-validated target.
//   ES55    : Everspace 2, 2025-05-17 snapshot (UE 5.5, ships a full PDB — the second
//             symbolised oracle). Note the project name "ES2-0517" is a DATE, not a
//             version. Version pinned structurally: FFieldVariant=0x08 (>=5.1.1),
//             UEnum::Names still TArray<TTuple> (<5.6), FUObjectItem 24B WITH RefCount,
//             classic FChunkedFixedUObjectArray order (<5.8), and the PDB's
//             EUnrealEngineObjectUE5Version enum ends at ASSETREGISTRY_PACKAGEBUILDDEPENDENCIES.
//   AV53    : Avowed (UE 5.3) sparse delegates specifically — found STRUCTURALLY, not by string
//             xref (`SparseDelegateReport` is compiled out of this binary). Avowed's known
//             deviations stop at the object array; its sparse storage is stock UE 5.3.
//   FD      : UWorld::FinishDestroy — the read-then-conditional-write-back GWorld shape,
//             PDB-confirmed on HeliumRain 4.20 + DropIn 4.24 + DropIn 4.27, correct 4.11-5.2.
//   DI427+SP57 / X+FF7R / PAL51+X / X+GH51
//           : CROSS-VERSION patterns — mined on one oracle, then confirmed decoy-free on
//             others, so the source records every binary that vouches for them rather than
//             just the one they came from. GENG_X1 (UWorld::GetGameViewport, correct on 8
//             engine versions), GENG_X3 (its head only, which is what reaches FF7 Remake's
//             UE 4.18 fork and UE 5.5), and the SPARSE_X1/X2 pair mined on Grimhook 5.1 that
//             closed the sparse "n=1" cluster.
//
// SWEEP CORPUS as of build 2505 — 70 programs, of which 55 carry ground truth, spanning
// UE 4.10 through 5.8 and CONTIGUOUS from 4.20 up (the 4.23 hole closed 2026-07-28; the 5.3 rung
// stopped being partial 2026-07-29 when a stock ThirdPerson build in all three configs replaced
// Avowed's SparseDelegates-only truth, and stock 5.4.4 landed the same day so EVERY UE5 version
// now has a symbolised oracle). Those 55 are PROGRAMS not games: a modular Satisfactory
// project contributes Core + CoreUObject + Engine separately, since each defines different
// globals. Most come from full PDBs; a handful (4.11 Nekopara, 4.13 Fantasynth, 4.18 FF7 Remake,
// 4.18 DQ XI S, 4.21 Freud Gate, 4.27 DQ7R, 5.0 Light Maze, 5.4 Elliot) were DERIVED BY
// DISASSEMBLY instead, which is why the per-game reasoning lives in GROUND-TRUTH.md rather than
// here — that file is the authoritative corpus table and this paragraph is a summary of it.
// The remaining 13 are symbol-less MONOLITHIC titles (Palworld 5.1, TQ2, Octopath, FF7 Rebirth,
// Manor Lords, DQ I&II HD-2D, The Artisan of Glimmith, Hogwarts Legacy, ...) used as noise
// probes. They cannot say "right", only "did anything hit that should not have" — and that
// question needs monolithic EXEs, because a 4-30 MB Satisfactory engine DLL understates the
// collision rate of a 100-200 MB shipped game by several-fold.
//
// THE CORPUS NOW REACHES BELOW THE SUPPORTED FLOOR ON PURPOSE. UE 4.10.4 joined 2026-07-29 (two
// rows, Shipping + Development, both full-PDB), and it is the ONLY place the regression matrix
// carries a ❌: GObjects is unresolvable on both. That is the expected result — LEAVE IT ❌ — and
// it converts "below 4.11 is gated as UNSUPPORTED" (Genau's MIN_SUPPORTED_UE_VERSION) from an
// assertion into a measurement with two independent causes:
//   (1) it cannot be FOUND — at 4.10 the array is a function-local static behind a magic-static
//       guard in GetUObjectArray(), so consumers reach it by CALL and the address is never
//       materialised inline; every GOBJ_* pattern is `lea reg,[rip+GUObjectArray]`-shaped. 4.11
//       promoted it to a plain global, which is why 4.11 Nekopara resolves one row below.
//   (2) it could not be READ if it were — 4.10 has no FUObjectItem at all (TUObjectArray is
//       TStaticIndirectArrayThreadSafeRead, elements are bare UObjectBase*), and ArrayLayout
//       structurally cannot express its inline chunk table.
// So do NOT "fix" this by mining a GetUObjectArray-shaped pattern: cause (1) is the cheap half and
// solving it alone buys nothing. GNames/GWorld/GEngine resolve normally on both rows, so they
// still earn their scan as the corpus's oldest coverage for those three.
//
// BOTH ENDS OF THE RANGE ARE SELF-BUILT, which is what makes them trustworthy: UE4.23-Flying
// (Shipping, Epic's installed 4.23.1 Launcher engine) and the StackOBot 5.7.4 / 5.8 PAIR
// (Shipping, unmodified engine source). So 4.23 — the version that introduced BOTH FNamePool and
// sparse delegates — and 5.8 are measured rather than interpolated, and 5.7.4-vs-5.8 is a
// controlled A/B where the engine is the only variable.
//
// UE 5.8 — THE ONE LAYOUT CHANGE THAT REACHES THIS FILE. 5.8 moved `FUObjectArray::ObjObjects`
// from +0x10 to +0x00 (cache-locality reorder; `PreAllocatedObjects` went to the end), so on 5.8
// the FUObjectArray BASE and ObjObjects are the SAME address. Consequence for pattern authoring,
// and only this: the GObjects patterns that carry a version-fixed adjustment encode PRE-5.8
// arithmetic. THE THREE FAMILIES DO NOT TARGET THE SAME THING, and this paragraph used to say
// they all produced "a base anchor" (audit #5 AD23) — the entries themselves say otherwise, and
// getting it backwards is how a new pattern gets authored with the wrong adjustment:
//
//   * `-0x10` on V10/AV1/AV2/RE2/V12 — anchor `ObjObjects.Objects` (the chunk table) at
//     base+0x10, target the FUObjectArray BASE. This family really is base-producing.
//   * `-0x14` on DI427_3/G427_2 — anchor `ObjObjects.NumElements`, target **ObjObjects**, NOT the
//     base. DI427_3's own comment says so in as many words, and `ValidateGObjects`'s "Default"
//     preset independently confirms the arithmetic: it reads Num at +0x14 from the address it is
//     given, so NumElements-0x14 is ObjObjects by construction.
//   * `+0x0C` on G427_4 — anchor `ObjLastNonGCIndex` at base+0x04, target **ObjObjects** at
//     base+0x10. Also not base-producing.
//
// Which changes the 5.8 conclusion for one of them. `-0x10` and `+0x0C` do overshoot on 5.8:
// both cross the FUObjectArray/ObjObjects boundary that 5.8 moved. `-0x14` does NOT — its anchor
// and its target are both INSIDE ObjObjects, so the reorder shifts them together and the
// subtraction still lands on ObjObjects. Where the arithmetic does break it is a
// MISS, not a wrong answer (ValidateGObjects finds no sane Num/Max at base-0x10), but it means
// a pattern mined ON 5.8 should anchor the BASE — which is what the 5.8 oracle actually lands on
// (GOBJ_ES53_1, adjustment 0). Same reason `sweep.sh`'s 5.8 row carries ONE truth value instead
// of the usual `base | base+0x10` pair: the alias would score a hit on ObjObjects.NumChunks as
// correct. Everything else 5.8 changed is NOT this file's business — the FUObjectArray field
// order lives in the `"UE5.8"` ArrayLayout preset (Genau.cpp / Aura.cpp) and the
// `virtual ~FFieldClass()` reflection break in DynOff — because Himmel holds byte patterns only.
//
// TWO THINGS THE ENLARGED CORPUS SETTLED, both worth remembering before pruning a pattern:
//   * GWLD_V7 went from "0 correct, looks like dead weight" to UNIQUE-OK the moment Meltopia
//     gained symbols. A pattern with no proof is not the same as a pattern with counter-proof.
//   * The four GWorld patterns removed in build 2409 (V2/V4/V5/V6) were re-tested against the
//     three NEW oracles and are still DECOY-ONLY on every one — now 0 correct across 12 oracle
//     groups. That is counter-proof, and it is why they went and V7 stayed.
// ============================================================

// ============================================================
// AOB Pattern Metadata Types
// ============================================================

enum class AobTarget : uint8_t {
    GObjects        = 0,
    GNames          = 1,
    GWorld          = 2,
    SparseDelegates = 3,  // FSparseDelegateStorage::SparseDelegates (UE 4.23+)
    GEngine         = 4,  // UEngine* GEngine — the &GEngine SLOT, not the object
};

// How to resolve the AOB match address into a final pointer
enum class AobResolve : uint8_t {
    RipDirect        = 0,  // RIP-relative -> address is direct target
    RipDeref         = 1,  // RIP-relative -> deref once (pointer-to-pointer)
    RipBoth          = 2,  // Try direct first, if validation fails try deref
    SymbolExport     = 3,  // MSVC mangled symbol → address IS the variable
    CallFollow       = 4,  // Follow CALL in AOB match, scan function body for RIP refs
    SymbolCallFollow = 5,  // MSVC mangled symbol → address IS a function → scan body for RIP refs
};

// Can this winner's (pattern, instrOffset+opcodeLen, instrOffset+totalLen) triple be
// replayed by a CE script? Only for the RIP forms.
//
//  • SymbolExport / SymbolCallFollow store an MSVC MANGLED NAME in `pattern`, not a
//    byte string. Handing it to AOBScanModuleUE scans for the literal characters of
//    "?GWorld@@3VUWorldProxy@@A" and finds nothing.
//  • CallFollow's `pattern` IS a byte string, but the address comes from following the
//    CALL and scanning the callee body — a fixed offset into the match cannot express it.
//
// All three also carry instrOffset/opcodeLen/totalLen = 0, so the emitted range would be
// the degenerate [0, 0) even if the pattern were scannable. Publishing any of them makes
// the UI's "an AOB is available" test (non-empty string) true while every address in the
// exported table resolves to `??` — audit #4 B2.
//
// ⚠ RipDeref is refused too (audit #5 AD10). CE replays the triple as exactly one step —
// `addr = match + len + i32[match + pos]` — which yields the RIP TARGET. RipDeref's answer
// is one further load THROUGH that target, and there is nowhere in (pattern, pos, len) to
// say so; a CE script built from it would register the pointer-to-pointer slot as if it
// were the pointer.
//
// ⛔ This is a NECESSARY condition, not a sufficient one, and RipBoth is why: it approves
// the form, but which of its two arms actually won is a RUNTIME fact this function cannot
// see, and the deref arm has the identical problem RipDeref has. `adjustment` is the same
// kind of hole — the triple carries no `+/- N`. Both are settled by actually replaying the
// triple and comparing (Genau's CeReplayMatchesResolved), which is the only gate a publish
// site may use on its own.
constexpr bool IsCeReplayableAob(AobResolve r) {
    return r == AobResolve::RipDirect
        || r == AobResolve::RipBoth;
}

// What actually FOUND this pointer — reported to the UI as `*_method` and printed in
// `FindAll: Complete`.
//
// A DIFFERENT question from IsCeReplayableAob above, and the two must not be conflated:
// that one asks "can a CE script replay this?", this one asks "which mechanism won?".
// A symbol export is not replayable AND is the strongest result we can get (priority 0,
// tried first, immune to a recompile moving bytes around).
//
// Before this existed the label was hardcoded to "aob" for every non-zero result, so a
// symbol-export win reported `method="aob"` while `pattern_id="GWLD_EXP"` and the AOB
// triple was empty — three fields in one payload disagreeing. Measured on Satisfactory
// (UE 5.6) 2026-08-12, where all four of GObjects/GNames/GWorld/GEngine resolve by export.
//
// ⚠ The consumer side treats this as "was it the NORMAL scan path, or a fallback/recovery"
// — see PointerPanelViewModel's ShowGObjectsWarning / ShowGWorldRecovered. Every value
// returned here is a normal-scan value; recovery paths ("engine_recovery",
// "instance_scan_recovery") and "not_found" are assigned elsewhere and must stay distinct
// from these. Adding a value here without teaching that side about it turns a correct
// scan into a spurious "recovered" badge.
constexpr const char* ScanMethodName(AobResolve r) {
    switch (r) {
        case AobResolve::SymbolExport:     return "symbol";
        case AobResolve::SymbolCallFollow: return "symbol_call_follow";
        case AobResolve::CallFollow:       return "call_follow";
        default:                           return "aob";
    }
}

// Unified AOB signature descriptor.
// All fields are POD — constexpr-constructible, stored in .rdata.
struct AobSignature {
    const char* id;           // Unique identifier, e.g. "GOBJ_V1", "GWORLD_ES2_1"
    const char* pattern;      // AOB pattern string ("48 8B 05 ?? ?? ?? ??") or mangled symbol name
    AobTarget   target;       // What global pointer this pattern finds
    AobResolve  resolve;      // How to resolve the match address
    int  instrOffset;         // Byte offset from match start to the RIP instruction (0 = at match start)
    int  opcodeLen;           // Opcode bytes before the 4-byte displacement (typically 3)
    int  totalLen;            // Total instruction length (typically 7 for REX+opcode+modrm+disp32)
    int  adjustment;          // Post-resolution offset adjustment (e.g. -0x10 for struct base)
    int  priority;            // Lower = tried first. 0=symbol exports, 10-20=long, 50=standard, 80=legacy
    int  callOffset;          // For CallFollow: byte offset of E8 opcode within the pattern
    bool gworldAllowNull;     // For GWorld: accept null dereference (write-patterns at startup)
    const char* source;       // Attribution: "V", "PS", "RE", "ES2", "SF", "TQ", etc.
    const char* notes;        // Human-readable: game name, UE version
};

// ── Compile-time RIP geometry validation (audit #5 AD17) ─────────────────────────────────
// Sibling of ASSERT_TABLE_ORDER at the bottom of this file, and it exists for the same
// reason that one does: something about an entry was wrong in a way NOTHING could see.
// Four entries shipped with a triple pointing into the middle of their own instruction —
// GOBJ_PS1 (instrOffset at the LEA's ModRM), GOBJ_PS6, GWLD_TQ_3 and GWLD_TQ_4 (all three
// naming the DISPLACEMENT where the field wants the INSTRUCTION). Each compiled, sorted,
// scanned and matched perfectly; every hit then resolved to a garbage address. The
// blocktest oracle covers 35 of 158 entries, so it could not have caught them either.
//
// The rules come from what Macht::ResolveRIP actually does with the triple — read a disp32
// at instrOffset+opcodeLen, add it to matchAddr+instrOffset+totalLen — so a pattern's own
// bytes are enough to falsify a wrong one offline:
//   * the disp32 bytes must be WILDCARDED wherever the pattern covers them (a literal there
//     could never match a real displacement, so the window is misaligned);
//   * the pattern may stop AT the displacement — the disp is read from process memory, not
//     from the pattern, and GNAM_SAT425_1 legitimately does exactly this — or cover the
//     whole instruction, but never stop strictly between the two;
//   * totalLen - opcodeLen - 4 is the immediate size, so it must be 0, 1, 2 or 4
//     (GWLD_DI427_2's `mov qword[rip+d32],imm32` is the totalLen=11 case);
//   * the ModRM byte just before the displacement must encode mod=00, rm=101, checked per
//     nibble so the many `4?`-style entries are still covered on the half that is literal.
// ⚠ Do NOT replace the "stops inside the instruction" test with the obvious
// `instrOffset + totalLen <= byteCount`. That is the rule you would write first, it does
// catch PS1 and PS6, and it FALSE-POSITIVES GNAM_SAT425_1, whose triple is correct.
// `tools/ghidra/extract_patterns.py --check` runs these same rules in CI and prints WHICH
// entry and WHY; this copy fires in the compiler, at the moment the table is edited.
constexpr int AobByteCount(const char* p) {
    int n = 0;
    for (int i = 0; p[i]; ) {
        if (p[i] == ' ') { ++i; continue; }
        ++n;
        while (p[i] && p[i] != ' ') ++i;
    }
    return n;
}

// Pointer to byte-token `idx` (0-based), or nullptr if the pattern is shorter than that.
constexpr const char* AobByteAt(const char* p, int idx) {
    int n = 0;
    for (int i = 0; p[i]; ) {
        if (p[i] == ' ') { ++i; continue; }
        if (n == idx) return p + i;
        ++n;
        while (p[i] && p[i] != ' ') ++i;
    }
    return nullptr;
}

// 0..15 for a hex nibble, -1 for the '?' wildcard, -2 for anything malformed.
constexpr int AobNibble(char c) {
    if (c == '?') return -1;
    if (c >= '0' && c <= '9') return c - '0';
    if (c >= 'A' && c <= 'F') return c - 'A' + 10;
    if (c >= 'a' && c <= 'f') return c - 'a' + 10;
    return -2;
}

constexpr bool AobIsWildcardByte(const char* p, int idx) {
    const char* t = AobByteAt(p, idx);
    return t && t[0] == '?' && t[1] == '?';
}

constexpr bool RipGeometryOk(const AobSignature& s) {
    // Symbol / CallFollow entries carry the degenerate 0,0,0 triple by design and their
    // `pattern` is a mangled name, not bytes — see IsCeReplayableAob.
    if (s.resolve != AobResolve::RipDirect && s.resolve != AobResolve::RipDeref &&
        s.resolve != AobResolve::RipBoth)
        return true;

    const int n = AobByteCount(s.pattern);
    const int io = s.instrOffset, opc = s.opcodeLen, tot = s.totalLen;
    if (io < 0 || opc < 1 || tot <= opc) return false;

    const int imm = tot - opc - 4;                       // disp32 is always 4 bytes
    if (imm != 0 && imm != 1 && imm != 2 && imm != 4) return false;
    if (io + opc > n) return false;                      // opcode must be in the pattern

    const int d0 = io + opc;                             // first displacement byte
    for (int k = 0; k < 4; ++k)
        if (d0 + k < n && !AobIsWildcardByte(s.pattern, d0 + k)) return false;
    if (d0 < n && n < io + tot) return false;            // stops inside the instruction

    if (d0 >= 1 && d0 - 1 < n) {                         // ModRM: mod=00, rm=101
        const char* m = AobByteAt(s.pattern, d0 - 1);
        const int hi = AobNibble(m[0]), lo = AobNibble(m[1]);
        if (hi == -2 || lo == -2) return false;
        if (lo >= 0 && (lo & 0x7) != 0x5) return false;
        if (hi >= 0 && (hi & 0xC) != 0x0) return false;
    }
    return true;
}

// Index of the first entry whose geometry does not match its pattern, or -1 if all are fine.
// Returning the index rather than a bool is deliberate: a static_assert message has to be a
// string literal, so the index is the only way the compiler can point at the culprit.
template <int N>
constexpr int FirstBadRipGeometry(const AobSignature (&t)[N]) {
    for (int i = 0; i < N; ++i)
        if (!RipGeometryOk(t[i])) return i;
    return -1;
}

namespace Sig {

// ============================================================
// GObjects / FUObjectArray
// ============================================================

// --- Original patterns (V-series) ---

// V1: mov rax,[rip+X]; mov rcx,[rax+rcx*8]  — classic UE5.0-5.2
constexpr const char* AOB_GOBJECTS_V1 = "48 8B 05 ?? ?? ?? ?? 48 8B 0C C8";
// V2: mov r9,[rip+X]; mov [rip+Y],r9  — common UE5.3+
constexpr const char* AOB_GOBJECTS_V2 = "4C 8B 0D ?? ?? ?? ?? 4C 89 0D";
// V3: mov r8,[rip+X]; test r8,r8
constexpr const char* AOB_GOBJECTS_V3 = "4C 8B 05 ?? ?? ?? ?? 4D 85 C0";
// V4: mov rax,[rip+X]; mov rcx,[rax+rcx*8]; test rcx,rcx  (longer context)
constexpr const char* AOB_GOBJECTS_V4 = "48 8B 05 ?? ?? ?? ?? 48 8B 0C C8 48 85 C9";
// V5: mov r10,[rip+X]; test r10,r10
constexpr const char* AOB_GOBJECTS_V5 = "4C 8B 15 ?? ?? ?? ?? 4D 85 D2";
// V6: mov rcx,[rip+X]; mov [rdx],rax  — alt mov rcx variant
constexpr const char* AOB_GOBJECTS_V6 = "48 8B 0D ?? ?? ?? ?? 48 89 02";
// V7: mov r9,[rip+X]; cdq; movzx edx,dx  — GSpots variant
constexpr const char* AOB_GOBJECTS_V7 = "4C 8B 0D ?? ?? ?? ?? 99 0F B7 D2";
// V8: mov r9,[rip+X]; mov edx,eax; shr edx,10h  — bit shift variant
constexpr const char* AOB_GOBJECTS_V8 = "4C 8B 0D ?? ?? ?? ?? 8B D0 C1 EA 10";
// V9: mov r9,[rip+X]; cdqe; lea rcx,[rax+rax*2]  — extended index
constexpr const char* AOB_GOBJECTS_V9 = "4C 8B 0D ?? ?? ?? ?? 48 98 48 8D 0C 40 49";
// V10: lea rcx,[rip+X]; call; call; mov byte[],1  — Split Fiction (UE5.5+)
//   Needs -0x10 adjustment (points into struct, not base)
constexpr const char* AOB_GOBJECTS_V10 = "48 8D 0D ?? ?? ?? ?? E8 ?? ?? ?? ?? E8 ?? ?? ?? ?? C6 05 ?? ?? ?? ?? 01";
// V11: lea reg,[rip+X]; mov r9,rcx; mov [rcx],rax; mov eax,-1  — Little Nightmares 3
constexpr const char* AOB_GOBJECTS_V11 = "48 8D ?? ?? ?? ?? ?? 4C 8B C9 48 89 01 B8 FF FF FF FF";
// V12: mov reg,[rip+X]; mov r8,[rax+rcx*8]; test r8,r8; jz  — FF7 Remake
//   Needs -0x10 adjustment
constexpr const char* AOB_GOBJECTS_V12 = "48 8B ?? ?? ?? ?? ?? 4C 8B 04 C8 4D 85 C0 74 07";
// V13: mov rax,[rip+X]; mov rcx,[rax+rcx*8]; lea rax,[rdx+rdx*2]; jmp+3  — Palworld
constexpr const char* AOB_GOBJECTS_V13 = "48 8B 05 ?? ?? ?? ?? 48 8B 0C C8 4C 8D 04 D1 EB 03";
// AV1: mov rdx,[rip+X]; movsxd r8,r8d; shl r8,4  — Avowed / Obsidian UE5.3
//   X resolves to ObjObjects.Objects (chunk table) = GUObjectArray + 0x10, so needs -0x10.
//   The standard GObjects patterns (incl. patternsleuth's) do NOT match Avowed; this is the
//   chunk-table load inside FUObjectArray::AllocateUObjectIndex (verified unique).
constexpr const char* AOB_GOBJECTS_AV1 = "48 8B 15 ?? ?? ?? ?? 4D 63 C0 49 C1 E0 04";
// AV2: mov rdx,[rip+X]; shr eax,10; lea rcx,[rcx+rcx*4]; shl ecx,2; add rcx,[rdx+rax*8]
//   The GENERIC FUObjectItem chunk-index codegen (idx>>16 = chunk, (idx&0xffff)*0x14 within
//   it — the lea*5 + shl<<2 bakes in the 20-byte item stride). X = GUObjectArray + 0x10 (so
//   -0x10). NOT unique (~10+ identical sites — object access is everywhere) but that is a
//   FEATURE: it is far more resilient to a game patch than AV1's single AllocateUObjectIndex
//   site, and the 20-byte stride math makes a false hit on a standard 24-byte-item UE game
//   essentially impossible. ValidateGObjects picks the real base among the matches.
constexpr const char* AOB_GOBJECTS_AV2 = "48 8B 15 ?? ?? ?? ?? C1 E8 10 48 8D 0C 89 C1 E1 02 48 03 0C C2";

// --- patternsleuth patterns (instrOffset != 0, use TryPatternRIPOffset) ---

// PS1: cmp/cmp/jne; lea rdx; lea rcx,[rip+X]  — instrOffset=21, opcodeLen=3, totalLen=7
//   Byte map (28 bytes): [0]  8B 05 d32  mov eax,[rip]      (6)
//                        [6]  3B 05 d32  cmp eax,[rip]      (6)
//                        [12] 75 ??      jne rel8           (2)
//                        [14] 48 8D 15 d32  lea rdx,[rip]   (7)
//                        [21] 48 8D 0D d32  lea rcx,[rip]   (7)  <- the anchor, ends at 28
//   Was 23 until build 3262 — that pointed at the LEA's MODRM byte (48 8D **0D**), two bytes
//   inside the instruction, so the disp32 was read from 26 (the last two wildcards of the real
//   displacement plus two bytes past the match) and the next-instruction base was 30. Every
//   match resolved to garbage. AD12.
constexpr const char* AOB_GOBJECTS_PS1 = "8B 05 ?? ?? ?? ?? 3B 05 ?? ?? ?? ?? 75 ?? 48 8D 15 ?? ?? ?? ?? 48 8D 0D ?? ?? ?? ??";
// PS2: jz; lea rcx,[rip+X]; mov byte; call  — instrOffset=2, opcodeLen=3, totalLen=7
constexpr const char* AOB_GOBJECTS_PS2 = "74 ?? 48 8D 0D ?? ?? ?? ?? C6 05 ?? ?? ?? ?? 01 E8";
// PS3: jne; mov; lea rcx,[rip+X]; call; xor r9d  — instrOffset=5, opcodeLen=3, totalLen=7
constexpr const char* AOB_GOBJECTS_PS3 = "75 ?? 48 ?? ?? 48 8D 0D ?? ?? ?? ?? E8 ?? ?? ?? ?? 45 33 C9 4C 89 74 24";
// PS4: test; mov qword; mov eax,-1; lea r11,[rip+X]  — instrOffset=16, opcodeLen=3, totalLen=7
constexpr const char* AOB_GOBJECTS_PS4 = "45 84 C0 48 C7 41 10 00 00 00 00 B8 FF FF FF FF 4C 8D 1D ?? ?? ?? ??";
// PS5: or esi; and eax; mov [rdi+8]; lea rcx,[rip+X]  — instrOffset=12, opcodeLen=3, totalLen=7
constexpr const char* AOB_GOBJECTS_PS5 = "81 CE 00 00 00 02 83 E0 FB 89 47 08 48 8D 0D ?? ?? ?? ??";
// PS6: mov eax,[rip]; sub eax,[rip]; sub eax,[rip+X]  — arithmetic, instrOffset=12, opcodeLen=2, totalLen=6
//   Byte map (18 bytes): [0] 8B 05 d32 (6) · [6] 2B 05 d32 (6) · [12] 2B 05 d32 (6, ends at 18).
//   Was 14 until build 3262 — 14 is where the DISPLACEMENT starts, not the instruction, so the
//   resolver read the disp32 from 16 and based the RIP on 20. Its sibling PS7 (instrOffset=17 =
//   the `03 0D` opcode, disp at 19, end at 23 = pattern length) had it right all along, which is
//   what shows this was a slip rather than a convention. AD13.
constexpr const char* AOB_GOBJECTS_PS6 = "8B 05 ?? ?? ?? ?? 2B 05 ?? ?? ?? ?? 2B 05 ?? ?? ?? ??";
// PS7: call; mov eax,[rip]; mov ecx,[rip]; add ecx,[rip+X]  — arithmetic, instrOffset=17, opcodeLen=2, totalLen=6
constexpr const char* AOB_GOBJECTS_PS7 = "E8 ?? ?? ?? ?? 8B 05 ?? ?? ?? ?? 8B 0D ?? ?? ?? ?? 03 0D ?? ?? ?? ??";

// --- RE-UE4SS CustomGameConfigs ---

// RE1: FF7 Rebirth — special: add [rip+X],ecx; dec eax; cmp edx,eax; jge
//   instrOffset=2, resolution: nextInstr(+6) + DerefToInt32(matchAddr+2)
constexpr const char* AOB_GOBJECTS_RE1 = "03 ?? ?? ?? ?? ?? FF C8 3B D0 0F 8D ?? ?? ?? ?? 44 8B";
// RE2: FF7 Remake — mov reg,[rip+X]; mov r8,[rax+rcx*8]; test r8; jz; ?; ?; ?; setz
//   instrOffset=3, needs -0x10 adjustment (same as V12 but slightly different context)
constexpr const char* AOB_GOBJECTS_RE2 = "48 8B ?? ?? ?? ?? ?? 4C 8B 04 C8 4D 85 C0 74 07 ?? ?? ?? 0F 94";
// RE3: Little Nightmares 3 Demo — lea; mov r9,rcx; mov; mov eax,-1; mov [rcx+8]; cmovne; inc; mov; cmp
//   (extended context variant of V11)
constexpr const char* AOB_GOBJECTS_RE3 = "48 8D ?? ?? ?? ?? ?? 4C 8B C9 48 89 01 B8 FF FF FF FF 89 41 08 0F 45 ?? ?? ?? ?? ?? FF C0 89 41 08 3B";

// --- UE4 Dumper.CT patterns (x64) ---

// CT1: mov r8; lea rax; mov [rsi+10h]; mov qword — UE4 Dumper.CT v5+
//   44 8B * * * 48 8D 05 * * * * * * * * * 48 89 71 10
constexpr const char* AOB_GOBJECTS_CT1 = "44 8B ?? ?? ?? 48 8D 05 ?? ?? ?? ?? ?? ?? ?? ?? ?? 48 89 71 10";
// CT2 — REMOVED in build 2415 as dead code. It was
//   "40 53 48 83 EC 20 48 8B D9 48 85 D2 74 ?? 8B"
// i.e. `push rbx; sub rsp,0x20; mov rbx,rcx; test rdx,rdx; jz; mov` — a bare MSVC function
// prologue, which matches thousands of functions in any UE binary. Like AOB_GNAMES_UD1 it was
// declared but never referenced by GOBJECTS_PATTERNS[], so it has never been scanned for.
// Just as well: it contains **no RIP-relative operand at all**, so there is nothing for
// TryResolveMatch to resolve — wiring it up could never have produced an address.
// CT3: mov r8,[rip+X]; cmp [r8+?]  — 4C 8B 05 * * * * 45 3B 88
constexpr const char* AOB_GOBJECTS_CT3 = "4C 8B 05 ?? ?? ?? ?? 45 3B 88";

// --- UEDumper patterns ---

// UD1: mov rax,[rip+X]; mov rcx,[rax+rcx*8]; lea rax,[rcx+rdx*8]; test rax,rax
constexpr const char* AOB_GOBJECTS_UD1 = "48 8B 05 ?? ?? ?? ?? 48 8B 0C C8 48 8D 04 D1 48 85 C0";

// --- UE 4.2 game analysis patterns (G42 series) ---

// G42_1: lea rax,[GUObjectArray]; xor esi; mov [rcx],rax; mov [rcx+10h],rsi  — UE4.2 constructor init
constexpr const char* AOB_GOBJECTS_G42_1 = "48 8D 05 ?? ?? ?? ?? 33 F6 48 89 01 48 89 71";
// G42_2: lea rcx,[GUObjectArray]; call RemoveUObjectDeleteListener; lea rcx,[rbx+18]; mov rbx  — UE4.2
constexpr const char* AOB_GOBJECTS_G42_2 = "48 8D 0D ?? ?? ?? ?? E8 ?? ?? ?? ?? 48 8D 4B 18 48 8B 5C";
// G42_3: lea rcx,[GUObjectArray]; mov r8d,[rsp+?]; mov edx,[rsp+?]; mov [GUObjectAllocator],rax  — UE4.2
constexpr const char* AOB_GOBJECTS_G42_3 = "48 8D 0D ?? ?? ?? ?? 44 8B 44 24 ?? 8B 54 24 ?? 48 89";
// G42_4: lea rcx,[GUObjectArray]; call; lea rcx,[rbp+58]; ... add rsp,40; pop r14; jmp  — UE4.2 long epilogue
//   Frame displacements + the frame size wildcarded in build 2437 (were 0x58/0x50/0x58/0x60/
//   0x68 and `add rsp,0x40`). Measured neutral — still 1/1 UNIQUE-OK on Everspace 4.20, where it
//   is the landing pattern, and still no hits elsewhere. Neutral is the right trade here: 24
//   literal bytes remain, so nothing is lost, and the pattern stops depending on one build's
//   frame layout for free.
constexpr const char* AOB_GOBJECTS_G42_4 = "48 8D 0D ?? ?? ?? ?? E8 ?? ?? ?? ?? 48 8D 4D ?? 48 8B 5C 24 ?? 48 8B 6C 24 ?? 48 8B 74 24 ?? 48 8B 7C 24 ?? 48 83 C4 ?? 41 5E 48 FF 25 ?? ?? ?? ?? 45";

// --- UE 4.27 game analysis patterns (G427 series) ---

// G427_1: mov rax,[ObjObjects.Objects]; sar ecx,?; movsxd rcx,ecx; mov rdx,[rax+rcx*8]  — UE4.27 FEngineLoop::PreInitPostStartupScreen
constexpr const char* AOB_GOBJECTS_G427_1 = "48 8B 05 ?? ?? ?? ?? C1 F9 ?? 48 63 C9 48 8B";
// G427_2: cmp eax,[ObjObjects.NumElements]; jge; cdq; movzx edx,dx; add eax,edx  — UE4.27 FEngineLoop::PreInitPostStartupScreen
//   opcodeLen=2 (3B 05), totalLen=6, adjustment=-0x14 (NumElements at ObjObjects+0x14)
constexpr const char* AOB_GOBJECTS_G427_2 = "3B 05 ?? ?? ?? ?? 7D ?? 99 0F B7 D2 03 C2";
// G427_3: mov rax,[ObjObjects.Objects]; mov rcx,[rax+?*8]; lea r8,[?+rdx*8]; jmp; xor r8d; mov eax,[r8+8]  — UE4.27 FGCObject ctor
constexpr const char* AOB_GOBJECTS_G427_3 = "48 8B 05 ?? ?? ?? ?? ?? 8B 0C ?? ?? 8D 04 ?? EB ?? 45 33 C0 41 8B ?? 08";
// G427_4: mov eax,[ObjLastNonGCIndex]; mov r9d,eax; mov [rcx+8],eax; inc r9d  — UE4.27 TObjectIteratorBase
//   opcodeLen=2 (8B 05), totalLen=6, adjustment=+0x0C (ObjLastNonGCIndex at GUObjectArray+0x04, ObjObjects at +0x10)
constexpr const char* AOB_GOBJECTS_G427_4 = "8B 05 ?? ?? ?? ?? 44 8B C8 89 41 08 41";

// --- Everspace 2 UE 5.3 build patterns (ES53 series) ---

// ES53_1: sub rsp,28; lea rcx,[GUObjectArray]; call FUObjectArray::FUObjectArray; lea rcx,[atexit_fn]; add rsp,28; jmp atexit
//   instrOffset=4 (LEA RCX starts at byte 4), 26 bytes — very specific ctor+atexit pattern
constexpr const char* AOB_GOBJECTS_ES53_1 = "48 83 EC 28 48 8D 0D ?? ?? ?? ?? E8 ?? ?? ?? ?? 48 8D 0D ?? ?? ?? ?? 48 83 C4 28 E9";

// --- Satisfactory UE 4.22 patterns (SAT422 series) ---

// SAT422_1: lea rcx,[GUObjectArray]; call CloseDisregardForGC; lea rcx,[rbp+?]; call ~FString; call NotifyRegistrationComplete; call; mov  — FEngineLoop::PreInit
//   34 bytes, very specific 4-CALL chain in engine init sequence
constexpr const char* AOB_GOBJECTS_SAT422_1 = "48 8D 0D ?? ?? ?? ?? E8 ?? ?? ?? ?? 48 8D 8D ?? ?? 00 00 E8 ?? ?? ?? ?? E8 ?? ?? ?? ?? E8 ?? ?? ?? ?? 48 89";

// --- Satisfactory UE 4.25 patterns (SAT425 series) ---

// SAT425_1: lea rcx,[GUObjectArray]; mov eax,-1; mov r15d,param; mov [RDI],rcx; mov [RDI+8],eax  — FObjectIterator ctor
constexpr const char* AOB_GOBJECTS_SAT425_1 = "48 8D 0D ?? ?? ?? ?? B8 FF FF FF FF 45 8B ?? 48 89 ?? 89 47 08";
// SAT425_2: lea rcx,[GUObjectArray]; mov r8d,[rsp+?]; mov edx,[rsp+?]; mov [GUObjectAllocator],rax (x3); call  — UObjectBaseInit
//   31 bytes, very specific init sequence
constexpr const char* AOB_GOBJECTS_SAT425_2 = "48 8D 0D ?? ?? ?? ?? 44 8B ?? 24 ?? ?? 00 00 8B ?? 24 ?? ?? 00 00 48 89 05 ?? ?? ?? ?? 48 89";

// --- Satisfactory UE 4.26 patterns (SAT426 series) ---

// SAT426_1: lea rcx,[GUObjectArray]; call RemoveUObjectDeleteListener; test rbx,rbx; jz; mov  — FUObjectAnnotationSparse::RemoveAnnotation
constexpr const char* AOB_GOBJECTS_SAT426_1 = "48 8D 0D ?? ?? ?? ?? E8 ?? ?? ?? ?? 48 85 DB 74 ?? 48";
// SAT426_2: mov ecx,[GUObjectArray]; mov ebx,[GUObjectArray.NumElements]; mov [GUnreachableObjectIndex],r13d; cmp byte; cmovnz  — GatherUnreachableObjects
//   opcodeLen=2 (8B 0D), totalLen=6
constexpr const char* AOB_GOBJECTS_SAT426_2 = "8B 0D ?? ?? ?? ?? 8B 1D ?? ?? ?? ?? 44 89 ?? ?? ?? ?? ?? 80 38 00 41";

// --- Satisfactory UE 5.2 patterns (SAT52 series) ---

// SAT52_1: lea r10,[GUObjectArray]; xor r15d; mov [rcx],r10; mov ecx,-1; mov ebp,param  — TObjectIteratorBase ctor
constexpr const char* AOB_GOBJECTS_SAT52_1 = "4C 8D 15 ?? ?? ?? ?? 45 33 ?? 4C 89 ?? B9 FF FF FF FF 41 8B";
// SAT52_2: lea rcx,[GUObjectArray]; call IsValid; test al; jnz; call ExecCheck  — ~UObjectBase
constexpr const char* AOB_GOBJECTS_SAT52_2 = "48 8D 0D ?? ?? ?? ?? E8 ?? ?? ?? ?? 84 C0 75 ?? E8";

// --- Octopath Traveller patterns (OT series) ---

// OT_1: mov edx,edi; lea rcx,[GUObjectArray]; call AllocateObjectPool; mov eax,[MaxObjsNotGC]; test; jle; add [GObj+C]
//   UE4 FUObjectArray::Init — uses LEA RCX (48 8D 0D), not LEA RAX (48 8D 05) like G42 series
//   instrOffset=2 (LEA starts at byte 2), opcodeLen=3, totalLen=7
constexpr const char* AOB_GOBJECTS_OT_1 = "8B D7 48 8D 0D ?? ?? ?? ?? E8 ?? ?? ?? ?? 8B 05 ?? ?? ?? ?? 85 C0 7E ?? 01 05";
// OT_2: generalized OT_1 — wildcards register choices and REX prefix for cross-game UE4 compatibility
//   mov r32,r32; REX lea rcx,[GUObjectArray]; call; mov eax,[rip]; test; jle; add [rip],r32; call
//   instrOffset=2, opcodeLen=3, totalLen=7 (REX at byte 2 is always 48/4C in x64)
constexpr const char* AOB_GOBJECTS_OT_2 = "8B ?? ?? 8D 0D ?? ?? ?? ?? E8 ?? ?? ?? ?? 8B 05 ?? ?? ?? ?? 85 ?? 7E ?? 01 ?? ?? ?? ?? ?? E8";

// --- Ghidra cross-game analysis patterns (GH series) ---

// GH_1: UObjectBase::AddObject — and eax,-5; mov [rdi+8]; xor r8d; lea rcx,[GUObjectArray]; mov rdx,rdi; call; test ebx; jz
//   instrOffset=12 (LEA RCX at byte 12), 30 bytes, 22 fixed — cross-game ES/ES2/SAT
constexpr const char* AOB_GOBJECTS_GH_1 = "BA EB 19 83 E0 FB 89 47 08 45 33 C0 48 8D 0D ?? ?? ?? ?? 48 8B D7 E8 ?? ?? ?? ?? 85 DB 74";
// GH_2: UnMarkAllObjects — test esi; jle; mov rdx,rdi; lea rcx,[GUObjectArray]; call; add rsp,B8h
//   instrOffset=12, 31 bytes, 19 fixed — cross-game ES/ES2/SAT
constexpr const char* AOB_GOBJECTS_GH_2 = "F3 85 F6 0F 8E ?? ?? ?? ?? 48 8B D7 48 8D 0D ?? ?? ?? ?? E8 ?? ?? ?? ?? 48 81 C4 B8 00 00 00";
// GH_3: IncrementalPurgeGarbage — mov rax,[bPurgeComplete]; cmp byte[rax],0; jz; lea rcx,[GUObjectArray]; mov byte[flag],1; call
//   instrOffset=12, 27 bytes, 15 fixed — cross-game ES/ES2/SAT. Extends PS2 with 12-byte leading context.
constexpr const char* AOB_GOBJECTS_GH_3 = "48 8B 05 ?? ?? ?? ?? 80 38 00 74 ?? 48 8D 0D ?? ?? ?? ?? C6 05 ?? ?? ?? 00 01 E8";
// GH_4: FWeakObjectPtr::operator= — mov ebx,ecx; test rdx; jz; mov edx,[rdx+0C]; mov [rcx],edx; lea rcx,[GUObjectArray]; call; mov [rbx+4]; add rsp,20
//   instrOffset=12, 31 bytes, 22 fixed — ES2/SAT
constexpr const char* AOB_GOBJECTS_GH_4 = "8B D9 48 85 D2 74 ?? 8B 52 0C 89 11 48 8D 0D ?? ?? ?? ?? E8 ?? ?? ?? ?? 89 43 04 48 83 C4 20";


// ============================================================
// GNames / FNamePool
// ============================================================

// --- Original patterns (V-series) ---

// V1: lea rsi,[rip+X]; jmp
constexpr const char* AOB_GNAMES_V1 = "48 8D 35 ?? ?? ?? ?? EB";
// V2: lea rcx,[rip+X]; call; mov byte ptr
constexpr const char* AOB_GNAMES_V2 = "48 8D 0D ?? ?? ?? ?? E8 ?? ?? ?? ?? C6 05";
// V3: lea rax,[rip+X]; jmp
constexpr const char* AOB_GNAMES_V3 = "48 8D 05 ?? ?? ?? ?? EB";
// V4: lea r8,[rip+X]; jmp   (REX.R variant)
constexpr const char* AOB_GNAMES_V4 = "4C 8D 05 ?? ?? ?? ?? EB";
// V5: lea rcx,[rip+X]; call; mov byte ptr[??],1  — extended context
constexpr const char* AOB_GNAMES_V5 = "48 8D 0D ?? ?? ?? ?? E8 ?? ?? ?? ?? C6 05 ?? ?? ?? ?? 01";
// V6: mov rax,[rip+X]; test rax,rax; jnz; mov ecx,0808h  — GSpots UE5+
constexpr const char* AOB_GNAMES_V6 = "48 8B 05 ?? ?? ?? ?? 48 85 C0 75 ?? B9 08 08 00";
// V7: FName ctor call-site — mov r8d,1; lea rcx; call; mov byte — FF7 Rebirth
//   Resolves CALL target, then scans inside for FNamePool refs
constexpr const char* AOB_GNAMES_V7_FNAME_CTOR = "41 B8 01 00 00 00 48 8D 4C 24 ?? E8 ?? ?? ?? ?? C6 44 24";
// V8: lea rax,[rip+X]; jmp 0x13; lea rcx,[rip+Y]; call; mov byte; movaps  — Palworld
//   First LEA resolves to FNamePool.
constexpr const char* AOB_GNAMES_V8 = "48 8D 05 ?? ?? ?? ?? EB 13 48 8D 0D ?? ?? ?? ?? E8 ?? ?? ?? ?? C6 05 ?? ?? ?? ?? ?? 0F 10";

// --- patternsleuth patterns ---

// PS1: jz+9; lea r8,[rip+X]; jmp; lea rcx; call  — instrOffset=2, opcodeLen=3, totalLen=7
constexpr const char* AOB_GNAMES_PS1 = "74 09 4C 8D 05 ?? ?? ?? ?? EB ?? 48 8D 0D ?? ?? ?? ?? E8";
// PS2: sub rsp,0x20; shr edx,3; lea rbp,[rip+X]  — instrOffset=7, opcodeLen=3, totalLen=7
constexpr const char* AOB_GNAMES_PS2 = "48 83 EC 20 C1 EA 03 48 8D 2D ?? ?? ?? ??";

// --- Dumper-7 pattern ---

// D7_1 — REMOVED in build 2404. It was "48 8D 0D ?? ?? ?? ?? E8" = `lea rcx,[rip+X]; call`,
// THREE literal bytes, i.e. a match on essentially every this-call in the image: 27,001 hits on
// a UE4.20 title, 104,897 on UE4.27, 40,000 on UE5.5 — every one of them validated (several
// SEH-guarded reads each) before the scan could reach the patterns that actually resolve there
// (GNAM_CT3 pri 800 / GNAM_G42_1 pri 840 on 4.20). It was never the sole correct pattern on any
// of the eight binaries in the sweep, and its own comment already recorded that V2/V5 cover the
// same sites with real context.
//   Dumper-7 can afford this pattern because it follows the CALL and checks the callee for
//   InitializeSRWLock + a "ByteProperty" reference; we do not implement that second stage, so
//   for us it was pure cost. If it is ever wanted back, it needs AobResolve::CallFollow plus
//   that callee check — not a re-add of the bare byte string.

// --- UE4 Dumper.CT patterns ---

// CT1: lea rax,[rip+X]; jmp 0x16; lea rcx,[rip+Y]; call  — UE4 Dumper.CT v6+ (UE4.23+)
//   Same as V8 variant but with jmp 0x16 instead of 0x13
constexpr const char* AOB_GNAMES_CT1 = "4C 8D 05 ?? ?? ?? ?? EB 16 48 8D 0D ?? ?? ?? ?? E8";
// CT2 — REMOVED in build 2407. It was
//   "48 8D 0D ?? ?? ?? ?? E8 ?? ?? ?? ?? 4C 8B C0 C6"
// which is AOB_GNAMES_UD2 minus its final `05` byte, i.e. the same site with one byte less
// context. Measured over all 26 programs in the sweep the two produced BYTE-IDENTICAL hit
// counts on every single one (0/0, 10/10, 11/11, 15/15, 36/36, 932/932 on FF7R, ...) — there
// is no binary where CT2's extra looseness finds anything UD2 does not. The `C6` it stops on
// is `mov byte ptr`, and the only encoding that ever follows here is `C6 05` (rip-relative),
// which is exactly what UD2 pins. Keeping both cost a scan slot for zero coverage: patterns are
// scanned in batches of 8 and ScanForTarget returns on the first validated hit, so a redundant
// entry can push a genuinely different pattern into an extra full-.text pass.
// To restore: re-add with pattern above at priority 300 (UD2 then moves back to 320).
// CT3: sub rsp,28h; mov rax,[rip+X]; test rax; jnz; mov ecx,0x0808; mov rbx,[rsp+20h]; call
//   — pre-FNamePool (UE4 <4.23), deref pointer
constexpr const char* AOB_GNAMES_CT3 = "48 83 EC 28 48 8B 05 ?? ?? ?? ?? 48 85 C0 75 ?? B9 ?? ?? 00 00 48 89 5C 24 20 E8";
// CT4: ret; ? DB; mov [rip+X],rbx; ?; ?; mov rbx,[rsp+20h]
//   — pre-FNamePool write pattern, instrOffset=5
constexpr const char* AOB_GNAMES_CT4 = "C3 ?? DB 48 89 1D ?? ?? ?? ?? ?? ?? 48 8B 5C 24 20";

// --- UEDumper example patterns ---

// UD1 — REMOVED in build 2407. It was
//   "E8 ?? ?? ?? ?? 83 7D E8 00 4C 8D 05 ?? ?? ?? ?? 48 8D 15 ?? ?? ?? ??"
// i.e. `call; cmp dword [rbp-0x18],0; lea r8,[rip+X]; lea rdx,[rip+Y]`, and it was DEAD CODE:
// declared here since the pattern DB was written, but never referenced by GNAMES_PATTERNS[] or
// anything else, so it has never been scanned for in any build. The suspicion about it was
// well founded — `cmp [rbp-0x18], 0` pins an exact frame-pointer-relative stack slot, which is
// a property of one compilation of one function in one game, not of UE. UEDumper can afford it
// (its README calls the entry an example to be re-derived per game); a cross-game scanner
// cannot. Deleted rather than wired up: adding it would have cost a scan slot for a pattern
// that cannot generalise.
// UD2: lea rcx,[rip+X]; call FNamePool::FNamePool; mov r8,rax; mov byte[bInit]  — the lazy-init
//   head shared by the FName accessors. NOTE this is the SAME SITE the old GNAM_CT2 matched;
//   see the removal note in GNAMES_PATTERNS[].
constexpr const char* AOB_GNAMES_UD2 = "48 8D 0D ?? ?? ?? ?? E8 ?? ?? ?? ?? 4C 8B C0 C6 05";

// --- UE 4.2 game analysis patterns (G42 series) ---

// G42_1: mov rax,[Names]; test rax; jnz; mov ecx,0x408  — UE4.2 pre-FNamePool (TStaticIndirectArrayThreadSafeRead)
constexpr const char* AOB_GNAMES_G42_1 = "48 8B 05 ?? ?? ?? ?? 48 85 C0 75 ?? B9 ?? ?? ?? ?? 48";

// --- Satisfactory UE 4.22 patterns (SAT422 series) ---

// SAT422_1: FName::GetNames — the pre-FNamePool (TStaticIndirectArrayThreadSafeRead) lazy-init
//   head, WITH the game-thread assertion that UE 4.22 inserts and 4.20 does not:
//     mov rax,[Names]; test rax,rax; jnz(near) done;
//     cmp byte[GIsGameThreadIdInitialized],al; mov [rsp+0x20],rbx; jz skip;
//     call [__imp_GetCurrentThreadId]
//   18 literal bytes.
//
//   CORRECTED in build 2407. The previous form omitted the `48 85 C0` (test rax,rax) between
//   the load and the jump:
//     "48 8B 05 ?? ?? ?? ?? 0F 85 ?? ?? ?? ?? 38 05 ?? ?? ?? ?? 48 89"
//   MSVC cannot emit `mov`+`jnz` with no flag-setting instruction between them, so that string
//   was unmatchable by construction — and the sweep confirms it: ZERO hits across all 26
//   programs, including the very Satisfactory UE 4.22 build it is named after. Re-derived here
//   from that build's PDB (FName::GetNames @ 0x140BCEBF0, load at +4).
//   Consequence of the old form being dead: UE 4.22 had to fall through to GNAM_CT4 — a
//   `ret; mov [rip],rbx` WRITE pattern — which reaches the right answer only after rejecting a
//   decoy. This restores a direct, purpose-built anchor for 4.22.
constexpr const char* AOB_GNAMES_SAT422_1 =
    "48 8B 05 ?? ?? ?? ?? 48 85 C0 0F 85 ?? ?? ?? ?? 38 05 ?? ?? ?? ?? 48 89 5C 24 20 74 ?? FF 15";

// --- Satisfactory UE 4.25 patterns (SAT425 series) ---

// SAT425_1: cmp [bNamePoolInitialized],0; mov [rsp+?],edi; mov [rsp+?],r8d; jz; lea r8,[NamePoolData]  — FName::AppendString
//   instrOffset=18 (LEA R8 at byte 18), 21 bytes
constexpr const char* AOB_GNAMES_SAT425_1 = "80 3D ?? ?? ?? ?? 00 89 7C 24 ?? 44 89 44 24 ?? 74 ?? 4C 8D 05";
// SAT425_2: lea rax,[NamePoolData]; mov eax,[rax+8]; inc eax; shl eax,11h; add rsp,28; ret  — FName::GetNameEntryMemorySize
constexpr const char* AOB_GNAMES_SAT425_2 = "48 8D 05 ?? ?? ?? ?? 8B 40 08 FF C0 C1 E0 11 48 83 C4 28 C3";
// SAT425_3: lea rax,[NamePoolData]; jmp; lea rcx,[NamePoolData]; call FNamePool::FNamePool; mov byte  — FName::GetNumAnsiNames
//   Generalized V8 with EB ?? (any JMP offset) instead of EB 13
constexpr const char* AOB_GNAMES_SAT425_3 = "48 8D 05 ?? ?? ?? ?? EB ?? 48 8D 0D ?? ?? ?? ?? E8 ?? ?? ?? ?? C6 05";

// --- Satisfactory UE 5.2 patterns (SAT52 series) ---

// SAT52_1: lea rdx,[NamePoolData]; jmp; lea rcx,[NamePoolData]; ... mov rdx,rax  — FName::ToString init dual-LEA
//   Both LEAs point to NamePoolData. Use first LEA (offset 0) for resolution
constexpr const char* AOB_GNAMES_SAT52_1 = "48 8D 15 ?? ?? ?? ?? EB ?? 48 8D 0D ?? ?? ?? ?? 48 8B";

// --- Everspace 2 UE 5.3 build patterns (ES53 series) ---

// ES53_1: lea rcx,[FNamePool]; call FNamePool::FNamePool; mov rdx,rax; mov byte[],1  — FName::ToString init path
//   Like V5 but has extra MOV RDX,RAX (48 8B D0) between CALL and MOV byte
constexpr const char* AOB_GNAMES_ES53_1 = "48 8D 0D ?? ?? ?? ?? E8 ?? ?? ?? ?? 48 8B D0 C6 05 ?? ?? ?? ?? 01";

// --- Ghidra cross-game analysis patterns (GH series) ---

// GH_1: ReserveNameBatch — mov [rsp+18],esi; push rdi; sub rsp,20; shr edx,3; lea rbp,[NamePoolData]; dec edx; mov ebx,ecx; mov rdi,magic_const
//   instrOffset=12 (LEA RBP at byte 12), 31 bytes, 27 fixed — cross-game ES/ES2/SAT. Best new GNames pattern.
//   Contains unique integer division constant 0xCCCCCCCCCCCC (compiler-generated magic number).
constexpr const char* AOB_GNAMES_GH_1 = "89 74 24 18 57 48 83 EC 20 C1 EA 03 48 8D 2D ?? ?? ?? ?? FF CA 8B D9 48 BF CD CC CC CC CC CC";
// GH_2: FNameEntryId::FromValidEName — sub rsp,20; cmp byte[bInitialized],0; mov rbx,rcx; lea rcx,[NamePoolData]; movsxd rdi,edx; jnz; call
//   instrOffset=12, 31 bytes, 19 fixed — cross-game ES/ES2/SAT
constexpr const char* AOB_GNAMES_GH_2 = "EC 20 80 3D ?? ?? ?? 00 00 48 8B D9 48 8D 0D ?? ?? ?? ?? 48 63 FA 75 ?? E8 ?? ?? ?? ?? 48 8B";


// ============================================================
// GWorld
// ============================================================

// V1: mov rax,[rip+X]; cmp rcx,rax; cmovz rax,[rip+Y]
constexpr const char* AOB_GWORLD_V1 = "48 8B 05 ?? ?? ?? ?? 48 3B C8 48 0F 44 05";
// V2 / V4 / V5 / V6 — REMOVED in build 2409 as dead weight (0 correct on 9 GWorld oracles
// across 31 programs). Byte strings + the full rationale are recorded at the bottom of
// GWORLD_PATTERNS[]; the short version is that every shape they cover has a longer sibling that
// does work, and on GWorld specifically a wrong answer is worse than no answer.
// V3: mov rbx,[rip+X]; test rbx,rbx  — the one of the family that IS correct (6 of 9 oracles)
constexpr const char* AOB_GWORLD_V3 = "48 8B 1D ?? ?? ?? ?? 48 85 DB";
// V7: mov rbx,[rip+X]; test rbx,rbx; jz 0x33; mov r8b  — Palworld
constexpr const char* AOB_GWORLD_V7 = "48 8B 1D ?? ?? ?? ?? 48 85 DB 74 33 41 B0";


// ============================================================
// MSVC Mangled Symbol Exports
// ============================================================
// Many retail UE games (especially modular builds) export these symbols.
// GetProcAddress resolves them in O(1) before any AOB scan.
// Source: RE-UE4SS (Satisfactory, Returnal use these exclusively)

constexpr const char* EXPORT_GOBJECTARRAY     = "?GUObjectArray@@3VFUObjectArray@@A";
constexpr const char* EXPORT_FNAME_CTOR       = "??0FName@@QEAA@PEB_WW4EFindName@@@Z";
constexpr const char* EXPORT_FNAME_TOSTRING   = "?ToString@FName@@QEBAXAEAVFString@@@Z";
constexpr const char* EXPORT_FNAME_CTOR_CHAR  = "??0FName@@QEAA@PEBDW4EFindName@@@Z";
constexpr const char* EXPORT_GWORLD           = "?GWorld@@3VUWorldProxy@@A";
// GEngine is exported by the Engine module in EVERY modular build we have binaries for —
// verified with tools/pe/pe_imports_exports.py against Satisfactory's
// FactoryGame-Engine-Win64-Shipping.dll on BOTH UE 4.26 (ordinal 13690) and UE 5.2
// (ordinal 19170), sitting directly beside `?GWorld@@3VUWorldProxy@@A` in the export table.
// We had a symbol export for GObjects and GWorld but simply never added one for GEngine, so a
// modular title paid for a full AOB sweep to find something GetProcAddress returns in O(1).
// The exported address IS &GEngine (the slot), which is exactly what AobTarget::GEngine wants.
// Costs nothing on a monolithic build: GetProcAddress just returns null and the scan proceeds.
constexpr const char* EXPORT_GENGINE          = "?GEngine@@3PEAVUEngine@@EA";


// ============================================================
// Patterns: Everspace 2 (UE 5.5)
// ============================================================

// --- GWorld (ES2) ---
// ES2_1: mov rax,[GWorld]; lea rdx,[rbp+1F8]; mov rcx,[rax+18]; mov [rbp+48],rcx; lea rcx,[rbp+48]; call
constexpr const char* AOB_GWORLD_ES2_1 = "48 8B 05 ?? ?? ?? ?? 48 8D 95 F8 01 00 00 48 8B 48 18 48 89 4D 48 48 8D 4D 48 E8";
// ES2_2: cmovz r13,[GWorld]; mov r10,[rax+358]; mov rax,[rsi]; mov [rbp-50],rax; mov rax,[rsi+8]
//   CMOVZ: opcodeLen=4 (4C 0F 44 2D), totalLen=8
constexpr const char* AOB_GWORLD_ES2_2 = "4C 0F 44 2D ?? ?? ?? ?? 4C 8B 90 58 03 00 00 48 8B 06 48 89 45 B0 48 8B 46 08";
// ES2_3: mov rax,[GWorld]; mov r8,rbx; mov rcx,[r8]; cmp [rcx+2C0],rax; jne
constexpr const char* AOB_GWORLD_ES2_3 = "48 8B 05 ?? ?? ?? ?? 4C 8B C3 49 8B 08 48 39 81 C0 02 00 00 0F 85 ?? ?? ?? ??";
// ES2_4: cmp [GWorld],rbx; jnz+8; and qword [GWorld],0; mov rcx,[rbx+440]; test rcx,rcx
constexpr const char* AOB_GWORLD_ES2_4 = "48 39 1D ?? ?? ?? ?? 75 08 48 83 25 ?? ?? ?? ?? 00 48 8B 8B 40 04 00 00 48 85 C9";
// ES2_5: mov rdx,[GWorld]; lea rcx,[rsi+28]; mov r9,rax; call r12; add rdi,10; sub r14,1
constexpr const char* AOB_GWORLD_ES2_5 = "48 8B 15 ?? ?? ?? ?? 48 8D 4E 28 4C 8B C8 41 FF D4 48 83 C7 10 49 83 EE 01";
// ES2_6: mov rdx,[GWorld]; lea rcx,[rdi+28]; cmovne r8,[rsp+20]; mov r9,rax; call rbx; mov rcx,[rsp+20]
constexpr const char* AOB_GWORLD_ES2_6 = "48 8B 15 ?? ?? ?? ?? 48 8D 4F 28 4C 0F 45 44 24 20 4C 8B C8 FF D3 48 8B 4C 24";

// --- GNames (ES2) ---
// ES2_1: lea rdx,[NamePoolData]; mov ecx,ebx; movzx eax,bx; mov [rsp+3C],eax; shr ecx,10; mov [rsp+38],ecx; mov rax,[rsp+38]
constexpr const char* AOB_GNAMES_ES2_1 = "48 8D 15 ?? ?? ?? ?? 8B CB 0F B7 C3 89 44 24 3C C1 E9 10 89 4C 24 ?? 48 8B";

// --- GObjects (ES2) ---
// ES2_1: lea rcx,[GUObjectArray]; mov esi,r9d; mov ebp,r8d; mov r15,rdx; call [rip+X]
constexpr const char* AOB_GOBJECTS_ES2_1 = "48 8D 0D ?? ?? ?? ?? 41 8B F1 41 8B E8 4C 8B FA FF 15";


// ============================================================
// Patterns: SatisfFactory (UE 5.3, modular build — in DLLs)
// ============================================================

// --- GWorld (SF, in Game-Engine-Win64-Shipping.DLL) ---
// SF_1: mov rax,[GWorld]; cmp [rcx+2C0],rax  — UGameEngine::Tick
constexpr const char* AOB_GWORLD_SF_1 = "48 8B 05 ?? ?? ?? ?? 48 39 81 C0 02 00 00";
// SF_2: mov rax,[GWorld]; lea r8,[rsp+38]; lea rdx,[rsp+20]; mov [rsp+38],rax  — FAudioDeviceManager::CreateMainAudioDevice
constexpr const char* AOB_GWORLD_SF_2 = "48 8B 05 ?? ?? ?? ?? 4C 8D 44 24 ?? 48 8D 54 24 ?? 48 89 44";
// SF_3: cmp [GWorld],rdi; jne; mov [GWorld],rbx; call  — UWorld::FinishDestroy
constexpr const char* AOB_GWORLD_SF_3 = "48 39 3D ?? ?? ?? ?? 75 ?? 48 89 1D ?? ?? ?? ?? E8 ?? ?? ?? ?? 48";
// SF_4: mov rdi,[GWorld]; mov rbx,[rsp+?]; mov rax,rdi  — UEngine::GetWorldFromContextObject
//   FRAME DISPLACEMENT WILDCARDED in build 2437 (was 0x70): coverage 2 binaries -> 6, UNIQUE-OK
//   on five of them and correct-site-first on UE 4.27. Note this makes it a near-superset of
//   GWLD_G42_4 (same site, different frame size) — deliberately kept as separate entries because
//   G42_4 must NOT be wildcarded; see its comment.
constexpr const char* AOB_GWORLD_SF_4 = "48 8B 3D ?? ?? ?? ?? 48 8B 5C 24 ?? 48 8B";
// SF_5: mov rax,[GWorld]; mov ebx,edx; mov rdi,rcx; lea rdx,[r11-38]  — FMallocLeakReporter::WriteReports
constexpr const char* AOB_GWORLD_SF_5 = "48 8B 05 ?? ?? ?? ?? 8B DA 48 8B F9 49 8D";

// --- GNames (SF, in GameSteam-Core-Win64-Shipping.DLL) ---
// SF_1: lea r8,[NamePoolData]; jmp; lea rcx,[NamePoolData]; call FNamePool::FNamePool; mov r8,rax
constexpr const char* AOB_GNAMES_SF_1 = "4C 8D 05 ?? ?? ?? ?? EB ?? 48 8D 0D ?? ?? ?? ?? E8 ?? ?? ?? ?? 4C 8B C0";
// SF_2: lea rax,[NamePoolData]; movups [rsp+38],xmm0; shl rdi,6; add rdi,rax
constexpr const char* AOB_GNAMES_SF_2 = "48 8D 05 ?? ?? ?? ?? 0F 11 44 24 38 48 C1";
// SF_3: lea rcx,[NamePoolData]; mov edi,edx; jne; call FNamePool::FNamePool
constexpr const char* AOB_GNAMES_SF_3 = "48 8D 0D ?? ?? ?? ?? 8B FA 75 ?? E8 ?? ?? ?? ?? 48";

// --- GObjects (SF, via _imp_ import table in EXE) ---
// SF_1: mov rax,[_imp_GUObjectArray]; cmp [rax+0C],sil; je; lea rdx
constexpr const char* AOB_GOBJECTS_SF_1 = "48 8B 05 ?? ?? ?? ?? 40 38 70 0C 74 2E 48 8D 15";


// ============================================================
// Patterns: TQ2
// ============================================================

// --- GWorld (TQ2) ---
// TQ_1: mov rbx,[GWorld]; test rbx,rbx; jz; mov r8b,1; xor edx,edx; mov rcx,rbx; call  — extended V3
constexpr const char* AOB_GWORLD_TQ_1 = "48 8B 1D ?? ?? ?? ?? 48 85 ?? 74 ?? 41 B0 01 33 ?? ?? 8B ?? E8";
// TQ_2: mov rdx,[GWorld]; mov rcx,[GWorld_related]; call; jmp; mov rax,r15; cmp byte [rsi],1
constexpr const char* AOB_GWORLD_TQ_2 = "48 8B 15 ?? ?? ?? ?? 48 8B 0D ?? ?? ?? ?? E8 ?? ?? ?? ?? EB 03 ?? 8B ?? 80 ?? 01";
// TQ_3: ?? prefix; mov rax,[GWorld]; mov rsi,rcx; movaps [r11-38],xmm8; movaps xmm8,xmm1; test rax,rax; je
//   instrOffset=0, opcodeLen=3, totalLen=7. The leading `??` is the REX byte OF THE RIP
//   INSTRUCTION ITSELF (`48 8B 05 d32`), not a prefix in front of it — so the instruction starts
//   at byte 0 and its displacement starts at 3.
//   ⚠ The old comment "RIP at offset 3" and the old instrOffset=3 named the DISPLACEMENT. With
//   instrOffset=3 the resolver read the disp32 from byte 6 — where byte 8 is the literal `8B` of
//   the following `mov rsi,rcx` — and based the RIP on byte 10. Fixed build 3262. AD15.
constexpr const char* AOB_GWORLD_TQ_3 = "?? 8B 05 ?? ?? ?? ?? ?? 8B ?? ?? 0F 29 43 ?? 44 0F 28 C1 ?? 85 ?? 0F";
// TQ_4: ?? prefix; mov [GWorld],rcx; test rsi,rsi; jz; mov rax,[rsi]; mov rcx,rsi; call [rax+E0]
//   Wildcard-prefixed write pattern. Same shape and same fix as TQ_3: `?? 89 0D d32` is
//   `48 89 0D d32` (mov [rip+d32],rcx) starting at byte 0, so instrOffset=0 / opcodeLen=3 /
//   totalLen=7 and the displacement is at 3. Was instrOffset=3. AD15/AD16.
constexpr const char* AOB_GWORLD_TQ_4 = "?? 89 0D ?? ?? ?? ?? ?? 85 ?? 74 ?? 48 8B 06 ?? 8B ?? FF 90 ?? 00 00";

// --- UE 4.2 game analysis patterns (G42 series) ---

// G42_1: mov rbx,[GWorld]; mov rsi,[rbp+28]; call GetGlobalLogSingleton  — UE4.2
constexpr const char* AOB_GWORLD_G42_1 = "48 8B 1D ?? ?? ?? ?? 48 8B 75 ?? E8";
// G42_2: mov rbx,[GWorld]; test rbx; jz; mov r8b,1  — UE4.2 (wildcard jz offset)
constexpr const char* AOB_GWORLD_G42_2 = "48 8B 1D ?? ?? ?? ?? 48 85 DB 74 ?? 41 B0 01";
// G42_3: mov rax,[rax+30]; test rax; jnz; mov rax,[GWorld]; ret  — UE4.2 fallback return
//   RIP instruction starts at offset 9 (48 8B 05)
constexpr const char* AOB_GWORLD_G42_3 = "48 8B 40 30 48 85 C0 75 ?? 48 8B 05 ?? ?? ?? ?? C3";
// G42_4: mov rdi,[GWorld]; mov rbx,[rsp+0x60]  — UE4.2 epilogue context
//   THE ONE PATTERN WHERE THE FRAME DISPLACEMENT MUST STAY LITERAL, and the measurement that
//   proves the stack-displacement rule needs its "enough other context" qualifier. This has only
//   SEVEN literal bytes, so `24 60` is a meaningful fraction of its selectivity. Wildcarding it
//   to `24 ??` was tested: it gains UE 4.24 but turns a clean UNIQUE-OK on 4.20 / 4.22 / 4.25
//   into OK-BEHIND, and on UE 4.27 explodes to 38 hits / 37 decoys. Contrast GWLD_SF_4, the same
//   site with two more literal bytes, where wildcarding is a clear win. Do not "fix" this one.
constexpr const char* AOB_GWORLD_G42_4 = "48 8B 3D ?? ?? ?? ?? 48 8B 5C 24 60";
// G42_5: mov rax,[GWorld]; mov rbx,rcx; lea rcx,[rbp+20]; mov rdx,[rax+18]  — UE4.2 extended
constexpr const char* AOB_GWORLD_G42_5 = "48 8B 05 ?? ?? ?? ?? 48 8B D9 48 8D 4D 20 48";

// --- UE 4.27 game analysis patterns (G427 series) ---

// G427_1: mov rbx,[GWorld]; test rbx; jz; ??;??;01; xor edx; mov rcx,rbx  — UE4.27 FEngineLoop::Tick
//   Extended version of G42_2 with more trailing context and wildcarded MOV R8B encoding
constexpr const char* AOB_GWORLD_G427_1 = "48 8B 1D ?? ?? ?? ?? 48 85 DB 74 ?? ?? ?? 01 33 D2 48 8B CB";
// G427_2: mov rdi,[GWorld]; mov rbx,[rsp+?]; mov rax,rdi; 48  — UE4.27 UEngine::GetWorldFromContextObject
//   Stack offset wildcarded (varies: 0x50, 0x60, 0x70)
constexpr const char* AOB_GWORLD_G427_2 = "48 8B 3D ?? ?? ?? ?? 48 8B 5C 24 ?? 48 8B C7 48";
// G427_3: mov rdi,[R?+?]; mov r8,rsi; mov rax,[GWorld]; mov rdx,rdi  — UE4.27 UGameEngine::Tick (R8-R15 src)
//   instrOffset=10 (0x0A): RIP instruction 48 8B 05 starts at byte offset 10
constexpr const char* AOB_GWORLD_G427_3 = "49 8B ?? ?? ?? ?? ?? 4C 8B C6 48 8B 05 ?? ?? ?? ?? 48 8B D7";
// G427_4: mov rdi,[R?+?]; mov r8,rsi; mov rax,[GWorld]; mov rdx,rdi  — UE4.27 UGameEngine::Tick (RAX-RDI src)
//   instrOffset=10 (0x0A): RIP instruction 48 8B 05 starts at byte offset 10
constexpr const char* AOB_GWORLD_G427_4 = "48 8B ?? ?? ?? ?? ?? 4C 8B C6 48 8B 05 ?? ?? ?? ?? 48 8B D7";
// G427_5: mov rax,[GWorld]; cmp rax,rbx; cmovz rax,rsi; mov [GWorld],rax  — UE4.27 UWorld::FinishDestroy
//   Uses CMP RAX,RBX (48 3B C3) vs V1's CMP RAX,RCX (48 3B C8)
constexpr const char* AOB_GWORLD_G427_5 = "48 8B 05 ?? ?? ?? ?? 48 3B C3 ?? 0F 44 ?? 48 89 05";

// --- Satisfactory UE 4.22 patterns (SAT422 series) ---

// SAT422_1: mov rax,[GWorld]; mov r??d,edx; mov rbx,rcx; lea rdx,[rbp+?]; 48  — FMallocLeakReporter::WriteReports
//   UE4.22 version of SF_5 (different register encoding: 44 8B vs 8B DA)
constexpr const char* AOB_GWORLD_SAT422_1 = "48 8B 05 ?? ?? ?? ?? 44 8B ?? 48 8B D9 48 8D 55 ?? 48";
// SAT422_2: mov [GWorld],rcx; test rcx,rcx; jz(near); mov ebx,[rcx+0Ch]; test ebx,ebx  — SetGlobalWorld
//   Canonical GWorld setter with null check + ObjectIndex read. Write pattern.
constexpr const char* AOB_GWORLD_SAT422_2 = "48 89 0D ?? ?? ?? ?? 48 85 C9 0F 84 ?? ?? ?? ?? 8B 59 0C 85 DB";

// --- Satisfactory UE 4.25 patterns (SAT425 series) ---

// SAT425_1: cmp rcx,[GWorld]; jz; inc ebx; add r14,8; cmp ebx,[r12+0xC40]  — UGameEngine::Tick
constexpr const char* AOB_GWORLD_SAT425_1 = "48 3B 0D ?? ?? ?? ?? 74 ?? FF C3 49 83 ?? 08 41 3B";
// SAT425_2: mov [GWorld],rcx; mov rax,gs:[TLS]; mov ecx,[_tls_index]; mov edx,4  — UGameEngine::Tick write + TLS
constexpr const char* AOB_GWORLD_SAT425_2 = "48 89 0D ?? ?? ?? ?? 65 48 8B 04 25 ?? ?? ?? ?? 8B 0D";
// SAT425_3: mov [GWorld],rax; mov rcx,[r15+88h]; test byte; jnz  — write + context
constexpr const char* AOB_GWORLD_SAT425_3 = "48 89 05 ?? ?? ?? ?? 49 8B 8F ?? ?? ?? 00 F6 81 ?? ?? ?? 00 ?? 75";

// --- Everspace 2 UE 5.3 build patterns (ES53 series) ---

// ES53_1: mov [GWorld],rax; movaps xmm2,xmm6; mov rax,[r12]; mov rdx,r15  — UGameEngine::Tick write
//   Write pattern: gworldAllowNull=true
constexpr const char* AOB_GWORLD_ES53_1 = "48 89 05 ?? ?? ?? ?? 0F 28 ?? 49 8B 04 24 49 8B D7";
// ES53_2: mov [GWorld],rcx; test rsi,rsi; jz; mov rax,[rsi]; mov rcx,rsi(?); call [rax+E0]
//   Write pattern with RCX register: gworldAllowNull=true
constexpr const char* AOB_GWORLD_ES53_2 = "48 89 0D ?? ?? ?? ?? 48 85 F6 74 ?? 48 8B 06 48 ?? ?? FF";

// --- Satisfactory UE 4.26 patterns (SAT426 series) ---

// SAT426_1: mov rax,[GWorld]; mov rcx,[r15+rdx]; cmp [rcx+??],rax; jz; inc edi  — UGameEngine::Tick
constexpr const char* AOB_GWORLD_SAT426_1 = "48 8B 05 ?? ?? ?? ?? 49 8B 0C ?? 48 39 81 ?? ?? ?? 00 74 ?? FF";
// SAT426_2: mov [GWorld],rax; call FTickTaskManager::Get; mov rdx,[rbx+?]; mov rcx,rax  — UWorld::FinishDestroy
//   Write pattern: gworldAllowNull=true
constexpr const char* AOB_GWORLD_SAT426_2 = "48 89 05 ?? ?? ?? ?? E8 ?? ?? ?? ?? 48 8B 93 ?? ?? ?? 00 48";

// --- Satisfactory UE 5.2 patterns (SAT52 series) ---

// SAT52_1: mov rcx,[GWorld]; test rcx; jz; lea rdx,[rbx+A0]; call UWorld::SetAudioDevice  — FAudioDeviceManager::CreateMainAudioDevice
constexpr const char* AOB_GWORLD_SAT52_1 = "48 8B 0D ?? ?? ?? ?? 48 85 C9 74 ?? 48 8D 93 ?? ?? ?? 00 E8";
// SAT52_2: mov [GWorld],rcx; test r14; jz; mov rax,[r14]; mov rcx,r14; call [rax+E0]  — UGameEngine::Tick
//   Write pattern: gworldAllowNull=true
constexpr const char* AOB_GWORLD_SAT52_2 = "48 89 0D ?? ?? ?? ?? 4D 85 ?? 74 ?? 49 8B ?? 49 8B ?? FF 90 ?? ?? 00 00";

// --- Ghidra cross-game analysis patterns (GH series) ---

// GH_1: FMallocLeakReporter::WriteReports — mov [rsp+?],edi; push rbp; mov rbp,rsp; sub rsp,?; mov rax,[GWorld]; mov rbx,rcx; lea rcx,[rbp+10]; mov rdx,[rax+18]
//   instrOffset=12, 31 bytes, 25 fixed — cross-game ES/ES2/SAT. Best new GWorld pattern.
constexpr const char* AOB_GWORLD_GH_1 = "89 7C 24 ?? 55 48 8B EC 48 83 EC ?? 48 8B 05 ?? ?? ?? ?? 48 8B D9 48 8D 4D 10 48 8B 50 18 48";
// GH_2: FUMGViewportClient::GetWorld — mov rax,[rax+30]; test rax; jnz; mov rax,[GWorld]; ret; mov [rsp+10],rbx; push rsi; sub rsp,20
//   instrOffset=9, 28 bytes, 23 fixed — cross-game ES/ES2/SAT. Extends G42_3 with trailing context.
constexpr const char* AOB_GWORLD_GH_2 = "48 8B 40 30 48 85 C0 75 ?? 48 8B 05 ?? ?? ?? ?? C3 48 89 5C 24 10 56 48 83 EC 20 48";
// GH_3: UEngine::GetWorldFromContextObject — call; cmp byte[rsp+?],0; jnz; mov rdi,[GWorld];
//   mov rbx,[rsp+?]; mov rax,rdi; mov rdi,[rsp+?]   instrOffset=12, cross-game ES/ES2/SAT.
//   FRAME DISPLACEMENTS WILDCARDED in build 2437 (were 0x58 / 0x60) — see the stack-displacement
//   rule above. Measured: coverage went from 5 binaries to SEVEN (it now also reaches UE 4.24
//   and 4.27) and it is UNIQUE-OK, zero decoys, on every one of them. A pure gain: the frame
//   offsets were excluding two engine versions and contributing nothing to selectivity, because
//   the other 22 literal bytes already carry it.
constexpr const char* AOB_GWORLD_GH_3 = "E8 ?? ?? ?? ?? 80 7C 24 ?? 00 75 ?? 48 8B 3D ?? ?? ?? ?? 48 8B 5C 24 ?? 48 8B C7 48 8B 7C 24";
// GH_4: FEngineLoop::Tick — xorps xmm1,xmm1; ucomiss xmm0,xmm1; jz; mov rbx,[GWorld]; test rbx; jz; mov r8b,1; xor edx
//   instrOffset=8, 27 bytes, 21 fixed — cross-game ES/ES2/SAT. Unique XORPS+UCOMISS prefix.
constexpr const char* AOB_GWORLD_GH_4 = "0F 57 C9 0F 2E C1 74 ?? 48 8B 1D ?? ?? ?? ?? 48 85 DB 74 ?? 41 B0 01 33 D2 48 8B";


// ============================================================
// Patterns: Solarpunk (UE 5.7, rokaplay — full PDB)
// ============================================================
// UE 5.7's MSVC codegen inserts an extra load / picks different registers around
// the GWorld access, so ALL of the Tier-1 (100–290) UE5 GWorld patterns (ES2_1-6,
// SF_1, GH_1/2, TQ_1/2, V1) get ZERO hits on this build. The scan then reaches the
// generic GWLD_SF_2 (Tier 2, pri 300), which matched a single DECOY .data global and
// passed the (intentionally loose) ValidateGWorldBasic → wrong GWorld. These four
// patterns re-anchor GWorld at the TOP of Tier 1 (pri 100–160, before every pattern
// that misses/mis-fires). Each was verified to hit ONLY the real GWorld slot (0
// decoys) across the whole .text image.

// SP57_1: mov rax,[GWorld]; mov rcx,[rbx+rax]; cmp [rcx+2C0],rax; jz  — UGameEngine::Tick
//   Loosened GWLD_SF_1: tolerates the inserted `mov rcx,[rbx+rax]` before the
//   `cmp [rcx+0x2C0],rax` world-compare. `4?` pins that byte to a REX prefix
//   (0x40-0x4F) via nibble wildcard — tighter than `??`. 0x2C0 seen across UE5.4-5.7.
constexpr const char* AOB_GWORLD_SP57_1 = "48 8B 05 ?? ?? ?? ?? 4? 8B ?? ?? 48 39 81 C0 02 00 00";
// SP57_2: mov rax,[GWorld]; mov rsi,rcx; lea rcx,[rbp+10]; mov rdx,[rax+18]  — FMallocLeakReporter::WriteReports
//   Same function as GWLD_GH_1 but UE5.7 uses `mov rsi,rcx` (48 8B F1) not `mov rbx,rcx`.
constexpr const char* AOB_GWORLD_SP57_2 = "48 8B 05 ?? ?? ?? ?? 48 8B F1 48 8D 4D 10 48 8B 50 18";
// SP57_3: mov rdi,[GWorld]; jmp; test rdi; jne; cmp ebx,1; jne  — UEngine::GetWorldFromContextObject
constexpr const char* AOB_GWORLD_SP57_3 = "48 8B 3D ?? ?? ?? ?? EB ?? 48 85 FF 75 ?? 83 FB 01 75";
// SP57_4: mov rax,[GWorld]; mov rdi,[rax+298]; test rdi; jz  — UActorComponent::On(Create|Destroy)PhysicsState
//   0x298 is a UWorld member offset — UE5.7-specific, so ordered LAST of the four.
constexpr const char* AOB_GWORLD_SP57_4 = "48 8B 05 ?? ?? ?? ?? 48 8B B8 98 02 00 00 48 85 FF 74";


// ============================================================
// FSparseDelegateStorage::SparseDelegates (UE 4.23+)
// ============================================================
// The static TMap<UObjectBase*, TMap<FName, TSharedPtr<TMulticastScriptDelegate>>>
// that backs every MulticastSparseDelegateProperty. Field on a UObject only
// stores `FSparseDelegate { uint8 bIsBound; }` (1-8 bytes); actual binding
// list lives in this global. Resolving its address lets the walker enumerate
// per-(owner, propertyName) FScriptDelegate bindings.
//
// Cross-version availability: UE 4.23 introduced sparse delegates, and **4.23 IS IN THE CORPUS**
// — the maintainer built the 4.23.1 "Flying" template himself (UE4.23-Flying, full PDB, added
// 2026-07-28), so the earliest version the feature has ever had is MEASURED, not interpolated.
// The outer TMap is keyed by a raw `UObjectBase const*` at every version we can check —
// PDB-verified from the mangled symbol on 4.23 (Flying), 4.24 (DropIn_UE424), 4.25 (Everspace 2
// depot), 4.26 (Satisfactory), 4.27 (DropIn) and across 5.x, and vendor/UnrealEngine 5.8 declares
// it identically. The 4.23 symbol is character-identical to the 4.24 one, which demangles to
//   TMap<UObjectBase const*, TMap<FName, TSharedPtr<TMulticastScriptDelegate<FWeakObjectPtr>>>>
// i.e. the shape is unchanged from the version that introduced it, and NO version is left
// unverified. SPARSE_DI427_1 is what resolves it live on the 4.23 build.
//
// The older note here ("UE 4.23-4.27 used FObjectKey, 16 bytes") was wrong on both counts:
// FObjectKey is 8 bytes ({int32 ObjectIndex; int32 ObjectSerialNumber}) and is not used as this
// key at any verified version. Aura's walker still probes the live key shape rather than gating
// on a version number — keep it that way. That is what covers any licensee fork no sample can,
// and it costs one pointer-shape check.
//
// ── WHY THERE IS NO `SPARSE_EXP` SYMBOL-EXPORT ENTRY, although the symbol does exist ─────────
// `FSparseDelegateStorage::SparseDelegates` is COREUOBJECT_API, so a MODULAR build really does
// export it — right beside the `?GUObjectArray@@3VFUObjectArray@@A` we already use. Measured with
// tools/pe/pe_imports_exports.py over the three modular oracles, the exported name is:
//   4.26 Sat  ?SparseDelegates@FSparseDelegateStorage@@0V?$TMap@PEBVUObjectBase@@V?$TMap@VFName@@
//             V?$TSharedPtr@V?$TMulticastScriptDelegate@UFWeakObjectPtr@@@@$0A@ ...
//   5.2  Sat  ... identical EXCEPT the TSharedPtr mode argument is `$00`, not `$0A@`
//   5.6  Sat  ... and the delegate parameter is `UFNotThreadSafeDelegateMode`, not `UFWeakObjectPtr`
// THREE DIFFERENT MANGLED NAMES ON THREE ENGINE VERSIONS. That is the blocker: the mangling
// embeds the entire template argument list, so unlike GUObjectArray/GWorld/GEngine (plain class
// names, stable since UE4) there is no single string to hand GetProcAddress — and
// AobResolve::SymbolExport is exactly `GetProcAddress(module, sig.pattern)` over every loaded
// module (Genau::TrySymbolExport), an EXACT-name lookup with no prefix matching.
//
// IF SOMEONE WANTS IT ANYWAY, here is the route that actually works, so it does not have to be
// re-derived. The SIBLING static declared immediately after it —
//   ?SparseDelegateObjectOffsets@FSparseDelegateStorage@@0V?$TMap@U?$TTuple@VFName@@V1@@@_K
//   VFDefaultSetAllocator@@U?$TDefaultMapHashableKeyFuncs@U?$TTuple@VFName@@V1@@@_K$0A@@@@@A
// — is BYTE-IDENTICAL on all three (its `TMap<TPair<FName,FName>, size_t>` carries no
// delegate-mode parameter), and on all three it sits exactly **0x50 above** SparseDelegates
// (sizeof(TMap), and they are adjacent in SparseDelegate.h). So `symbol - 0x50` reaches the
// target. It needs two things this file cannot supply: `ResolveSymbolExport` currently IGNORES
// `sig.adjustment`, and 0x50 is 3 samples on 3 engine versions, not a proof.
// NOT DONE, because the payoff is close to nil: monolithic shipping games export nothing at all,
// and every modular oracle already resolves sparse through SPARSE_ES2_1 / X1 / X2.
// (`SparseDelegateObjectListener` is also stably mangled but is NOT usable — its delta to
// SparseDelegates is 0x10 on 4.26/5.2 and 0x08 on 5.6, because FObjectListener changed size.
// `SparseDelegateMapCritical`'s own mangling is unstable: FWindowsCriticalSection -> UE::FWindowsRecursiveMutex.)

// ES2_1: NotifyUObjectDeleted middle — lea rcx,[crit]; call [EnterCriticalSection];
//        mov rdx,r??; lea rcx,[SparseDelegates]; call TSet::Remove; mov eax,[SparseDelegates+8]
//        Twin-reference (lea+mov of same static) makes false-positives near-zero.
//        instrOffset=16, 29 bytes; the `?? ?? ??` after critical-section call
//        is the 3-byte mov rdx,rXX (param register varies by build).
//
//        Cross-version validated:
//          ES2 (UE 5.4, bCasePreservingName=false) → SparseDelegates @ +9AA5F10
//          TQ2 (UE 5.7, bCasePreservingName=true ) → SparseDelegates @ +D46D170
//        Effectively universal across UE 5.x — same pattern, different layout
//        branches handled by Aura::WalkSparseDelegateBindings (FName=8 vs 16).
constexpr const char* AOB_SPARSE_ES2_1 =
    "48 8D 0D ?? ?? ?? ?? FF 15 ?? ?? ?? ?? 48 8B ?? 48 8D 0D ?? ?? ?? ?? E8 ?? ?? ?? ?? 8B 05";

// SP57_1: mov rdx,[SparseDelegates]; movsxd rax,r?d; lea rcx,[rax+rax*2]; shl rcx,5;
//         cmp [rcx+r?],r?; jz  — the TSet<...>::Find/FindOrAdd/EmplaceByHash element-index
//         math (element stride 0x60 = *3<<5). SPARSE_ES2_1 (NotifyUObjectDeleted) does NOT
//         match this build. Verified: matches the 3 always-present hot accessors; the two
//         same-stride decoys resolve to a different global but sit at HIGHER addresses, so
//         the real (lower) sites validate first. RipDirect -> the TSet object base.
constexpr const char* AOB_SPARSE_SP57_1 =
    "48 8B 15 ?? ?? ?? ?? ?? 63 ?? 48 8D 0C 40 48 C1 E1 05 4C 39 ?? 11 74";
// SP57_2: mov r8,[SparseDelegates]; movsxd rax,ebx; lea rdx,[rax+rax*2]; shl rdx,5;
//         cmp [r8+rax],r11; jz  — TSet::Remove (r8 variant). Verified UNIQUE (0 decoys).
constexpr const char* AOB_SPARSE_SP57_2 =
    "4C 8B 05 ?? ?? ?? ?? 48 63 C3 48 8D 14 40 48 C1 E2 05 4E 39 1C 02 74";

// --- DI427: UE 4.27.2 sparse-delegate accessors (DropIn, PDB-verified) -------
// The 4.27 element math is identical in SHAPE to UE5.7 (stride 0x60 = *3 << 5) but
// MSVC picks different registers, which is why SPARSE_SP57_1/2 both get 0 hits here.
// Both of the patterns below are UNIQUE-OK on DropIn and 0-hit on Solarpunk/Avowed.
//
// TRAP worth recording: the obvious "make it register-agnostic with nibbles" move makes
// this WORSE. `83 F8 FF 74 ?? 48 8D ?4 40 48 C1 E? 05 48 03 ?? ...` picks up two unrelated
// 0x60-stride global TSets that sit at LOWER addresses than the real sites — and because
// ValidateSparseDelegates is deliberately weak (it only range-checks two ints), a decoy
// that scans first WINS. Exact-register forms are the safe ones here.

// DI427_1: TSet::FindId head — call [rip] EnterCriticalSection; lea rcx,[SparseDelegates];
//          call TSet::FindId; movsxd the out-param; cmp -1. Contains NO stride/offset
//          arithmetic at all (pure x64 ABI shape), so it is the most version-portable of
//          the set. 5 sites (Clear / Contains x2 / Remove x2), all correct, 0 decoys.
constexpr const char* AOB_SPARSE_DI427_1 =
    "FF 15 ?? ?? ?? ?? 4? 8B C? 48 8D 54 24 ?? 48 8D 0D ?? ?? ?? ?? E8 ?? ?? ?? ?? 48 63 44 24 ?? ?? 33 ?? 83 F8 FF 74 ?? 4? 8D";

// DI427_2: the element-address block plus the inner-TMap fetch tail —
//          cmp -1; lea r,[rax+rax*2]; shl r,5; add r,[SparseDelegates]; jmp;
//          mov r,<null>; test; lea r,[elem+8]; cmovz. The `+8 / cmovz` tail is
//          load-bearing: without it the same block collides with 0x60-stride decoys.
//          5 sites, all correct, 0 decoys.
constexpr const char* AOB_SPARSE_DI427_2 =
    "48 63 44 24 ?? 4? 33 ?? 83 F8 FF 74 ?? 48 8D ?? 40 48 C1 E? 05 48 03 ?? ?? ?? ?? ?? EB ?? 4? 8B ?? 48 85 ?? 4? 8D ?? 08 4? 0F 44 ??";

// --- PAL51: UE 5.1 sparse-delegate element address (Palworld) -----------------
// WHY it exists: Palworld was the corpus's first UE 5.1 sample, and SparseDelegates resolved
// there through exactly ONE pattern (SPARSE_ES2_1). That is the thinnest coverage of any target
// on any binary, and it matters more here than elsewhere because ValidateSparseDelegates is the
// weakest validator we have — it can only range-check two ints, so it cannot rescue a miss.
//
// Ground truth was established without a PDB: the SPARSE_ES2_1 site disassembles to
// FSparseDelegateStorage::NotifyUObjectDeleted — `lea rcx,[crit]; call EnterCriticalSection;
// lea rcx,[0x148FB66B0]; call TMap::Remove; mov eax,[+0x8]; cmp eax,[+0x34]` — i.e. the address
// is passed as `this` to a TMap method and its +0x8 / +0x34 int32s are the very fields the
// validator checks. (The same function then does `lea rcx,[GUObjectArray]; call
// RemoveUObjectDeleteListener`, which independently confirmed Palworld's GObjects too.)
//
// This pattern anchors a DIFFERENT site — the element-address computation:
//   lea rdi,[rax+rax*2]; shl rdi,5      <- element stride 0x60
//   add rdi,[SparseDelegates]           <- ADD, not the MOV that SP57_1/2 use
//   lea rdi,[rdi+8]; cmovz rdi,rbp; test rdi,rdi; jz near
//   mov eax,[rdi+8]; cmp eax,[rdi+0x34] <- the TSet Num-vs-Max compare
// SPARSE_DI427_2 models the same semantics but with a SHORT jz and a different instruction
// order, which is why it takes 0 hits here. 29 literal bytes.
//
// Measured over the full 35-program sweep it fires on exactly three binaries and is decoy-free
// on all of them: Palworld (2 hits, both on the true address — the goal), **UE 4.26 Satisfactory
// (2/2, UNIQUE-OK — an unplanned bonus, so this is not 5.1-only)**, and DQ I&II HD-2D (2 hits
// converging on one address, unverifiable without symbols). Zero hits on the other 32 programs.
//
// The register-agnostic nibbled variant was measured and REJECTED — it produced a decoy on
// Palworld itself, reproducing the trap already recorded for DI427_2 above. Exact-register
// forms remain the safe ones for this target.
constexpr const char* AOB_SPARSE_PAL51_1 =
    "48 8D 3C 40 48 C1 E7 05 48 03 3D ?? ?? ?? ?? 48 8D 7F 08 48 0F 44 ?? 48 85 FF 0F 84 "
    "?? ?? ?? ?? 8B 47 08 3B 47 34";

// --- MEL55: FSparseDelegateStorage twin-reference + element math (Meltopia) ---------
// A second anchor for the UE 5.2-5.6 band, where SPARSE_ES2_1 was the ONLY pattern that hit.
// Sparse coverage was measured across the whole corpus and that band was uniformly n=1, which
// matters more than it looks: ValidateSparseDelegates can only range-check two ints, so it
// cannot rescue a miss the way the GObjects/GNames/GWorld/GEngine validators can.
//
//   lea rcx,[SparseDelegates]      <- passed as `this` to TSet::FindOrAddId
//   call <FindOrAddId>
//   movsxd rax,[rsp+d32]           <- the out-param element index (displacement WILDCARDED)
//   lea rdi,[rax+rax*2]; shl rdi,5 <- element stride 0x60
//   add rdi,[SparseDelegates]      <- the SAME global again
// The twin reference is what carries the uniqueness — the same property that makes
// SPARSE_ES2_1 reliable — and the 0x60 stride math confirms it is this TSet and not another.
//
// Mined on Meltopia (UE 5.5, PDB): 3 hits, all 3 correct, zero decoys. On TQ2 (UE 5.6, no
// symbols) 3 hits all converging on ONE address, so that title gains a second anchor as well.
// Zero hits on Everspace 2 5.5, Satisfactory 5.2/5.6, Solarpunk 5.7, DropIn 4.27, Avowed and
// FF7 Rebirth — it is codegen-specific, not version-specific, which is exactly why it is
// additive rather than a replacement.
//
// TWO REJECTED ALTERNATIVES, both instructive:
//   * The TSet hash-bucket probe (`dec ecx; mov eax,rNd; and rcx,rax; mov eax,[rdx+rcx*4];
//     cmp eax,-1; jz; mov rdx,[Sparse]`) reads like a great anchor and is NOT: it is the
//     GENERIC TSet lookup used by every TSet in the engine, so it resolved to 39-43 DIFFERENT
//     globals per binary and was DECOY-ONLY on Solarpunk and Satisfactory 5.2.
//   * The register-nibbled form of this pattern took 0 hits — over-wildcarding does not
//     generalise a pattern, it just stops it matching.
// The leading `lea rdx,[rsp+d32]` (the out-param address) is KEPT with its displacement
// wildcarded. It was briefly dropped on a misreading of the stack-displacement rule: the rule
// bans a LITERAL frame offset, not the instruction. Keeping the form costs nothing, adds four
// literal bytes of context, and measured identically (3/3 on Meltopia either way).
constexpr const char* AOB_SPARSE_MEL55_1 =
    "48 8D 94 24 ?? ?? ?? ?? 48 8D 0D ?? ?? ?? ?? E8 ?? ?? ?? ?? 48 63 84 24 ?? ?? ?? ?? "
    "48 8D 3C 40 48 C1 E7 05 48 03 3D";

// ============================================================
// SparseDelegates — AV53 (UE 5.3, Avowed / Obsidian fork)
// ============================================================
// Closes the "Avowed (5.3) sparse: zero hits" gap. All 7 prior patterns MISS: same functions,
// different MSVC scheduling. ES2_1 needs `lea rcx,[crit]; call [IAT]; mov rdx,rXX;
// lea rcx,[Sparse]` with a 3-byte gap — Avowed has ~42 bytes of inlined PointerHash there.
// MEL55_1/PAL51_1 need `add r,[Sparse]`; Avowed emits `mov r,[Sparse]` + `lea r,[base+idx]`.
//
// The fork does NOT change this structure. Measured against stock UE 5.x at
// FSparseDelegateStorage::GetSparseDelegate: outer stride 0x60, TSet::HashSize at +0x48,
// element HashNextId at +0x58, inner TMap at element+8, inner stride 0x20 with the value at
// +8, PointerHash = ptr>>4 into the Murmur finalizer. So Avowed's known deviations (packed
// 20-byte FUObjectItem, static FUObjectArray) stop at the object array — ValidateSparseDelegates'
// hardcoded kOuterStride = 0x60 was already correct for it.
//
// `SparseDelegateReport` does NOT exist in this binary (the !UE_BUILD_SHIPPING console command
// is compiled out), so the string-xref route GROUND-TRUTH.md suggests for FF7 Rebirth is dead
// here. This was found structurally instead: scan .text for TSet stride math adjacent to a
// rip-relative .data reference, bucket by global, and take the one with a pure-0x60 profile.
// Corroborated by SparseDelegateMapCritical sitting exactly 0x28 (sizeof CRITICAL_SECTION)
// below it — the two statics of SparseDelegate.cpp, adjacent.
//
// 22 literal bytes. 3 hits on Avowed, all correct, 0 decoys; 0 hits on all 41 other programs.
// HONEST CAVEAT, measured: the head alone (through `shl rdx,5`, 14 literal bytes) scores
// identically, so the `lea rcx,[rax+rdx]; cmp [rax+rdx],rsi` tail is inert on this corpus —
// the selectivity comes from the exact register allocation, not the length.
//
// Two sibling candidates were mined and REJECTED on measurement, not taste:
//   * a twin-ref form in GetSparseDelegate — correct, but it bakes in `[rsp+0x20]`, which is a
//     frame LOCAL (`mov [rsp+0x20],rdi` spills the key) and not shadow space; DI427_1/2 encode
//     the same out-param idiom and wildcard that disp8. One added spill in a future build kills
//     its only hit.
//   * a `mov rdx` register variant — strictly dominated: a nibbled form covers its sites plus
//     AV53_1's, so it buys nothing.
//   Both also push SPARSE_PATTERNS from 8 to 9 entries = 2 batches (kBatchSize = 8), which costs
//   a second full AVX2 pass over 430 MB of .text across the titles that find nothing in batch 1,
//   for a pattern that can only ever hit Avowed. If more Avowed sites are wanted, WIDEN this
//   pattern in place rather than appending a 9th entry.
constexpr const char* AOB_SPARSE_AV53_1 =
    "48 8B 05 ?? ?? ?? ?? 48 63 C9 48 8D 14 49 48 C1 E2 05 48 8D 0C 10 48 39 34 10";

// ============================================================
// SparseDelegates — X1/X2 (cross-version; mined on Grimhook UE 5.1)
// ============================================================
// These close the "sparse n=1" cluster that GROUND-TRUTH.md has carried as an open item: six
// binaries reached SparseDelegates through SPARSE_ES2_1 and NOTHING else, so a patch that moved
// that one site would have taken sparse-delegate support with it. After these, only Avowed is
// still n=1.
//
//   Everspace 2 5.5 / 5.5b  1 -> 2      Satisfactory 5.2 CoreUObject  1 -> 3
//   Satisfactory 5.6        1 -> 3      CrashReportClient 5.6         1 -> 3
//   Grimhook 5.1            1 -> 3      Avowed 5.3                    1 -> 1 (unchanged)
//
// NO binary that currently fails starts working — this is redundancy, on the same footing as
// PAL51_1 / MEL55_1 / AV53_1, each of which was added to a binary that already resolved.
//
// Both anchor on FSparseDelegateStorage::Remove / RemoveAll / Clear, at DIFFERENT sites from
// SPARSE_ES2_1's (which lives in NotifyUObjectDeleted) — genuine redundancy across functions,
// not a re-anchor on the same instruction stream. Verified decoy-free on 39 programs including
// 8 monolithic game EXEs up to 414 MB of .text: 0 decoys, anywhere, for either.
//
// X1 is the empty-map epilogue: after removing the last entry, unregister the UObject delete
// listener. That is not a generic TSet idiom — only FSparseDelegateStorage does it — which is
// why 62 hits produce zero decoys. `8B ?5 [+0x08]` / `3B ?5 [+0x34]` are the outer-TSet Num/Max,
// i.e. the very two ints ValidateSparseDelegates range-checks, so they stay literal (STRUCT
// displacements, not frame).
//
// TRUNCATED DELIBERATELY. The mined form ended with one more `48 8D 0D` (the GUObjectArray ref).
// Measured, those 3 bytes are inert on 36 of 38 programs — and they COST both Everspace 2 5.5
// builds, because 5.5 emits `lea rdx,…; call` with no second lea. Since ES2 5.5 is one of the
// exact n=1 binaries this exists to fix, the longer form failed at its own purpose. Longer is
// not safer; it is only safer where the extra bytes are load-bearing.
// UNIQUE-OK on 11 oracles / 9 engine versions (4.24 -> 5.6).
constexpr const char* AOB_SPARSE_X1 =
    "48 8D 0D ?? ?? ?? ?? E8 ?? ?? ?? ?? 8B ?5 ?? ?? ?? ?? 3B ?5 ?? ?? ?? ?? 75 ?? 48 8D 15";

// X2 anchors one block EARLIER — the inner-TMap emptiness test that guards the same call. It is
// the only new pattern that fires on Everspace 2 5.5, and it keeps `+0x08` / `+0x34` as LITERAL
// struct displacements (version-stable UE layout, the evidence that makes a pattern
// trustworthy). instrOffset = 11: `8B 4? 08`(3) + `3B 4? 34`(3) + `75 ??`(2) + `4? 8B D?`(3),
// so the RIP-relative `48 8D 0D` starts at byte 11, NOT byte 0. A deliberate wrong-offset
// control confirmed how quiet that mistake is: instrOffset 26 resolves to
// SparseDelegateObjectListener — a plausible adjacent global 8 bytes below truth — and goes
// DECOY-ONLY on all 15 binaries while the hit count stays healthy.
// UNIQUE-OK on 10 oracles / 6 engine versions.
constexpr const char* AOB_SPARSE_X2 =
    "8B 4? 08 3B 4? 34 75 ?? 4? 8B D? 48 8D 0D ?? ?? ?? ?? E8 ?? ?? ?? ?? 8B ?5 ?? ?? ?? ?? 3B ?5";

// ============================================================
// GObjects — DI427 (UE 4.27, 32-byte FUObjectItem)
// ============================================================
// WHY these exist: on DropIn the pre-existing GObjects patterns had no DECOY-FREE anchor. Root
// cause, measured over all 400 xrefs to ObjObjects.Objects: the destination register of the
// chunk load is rdi(156) / rsi(92) / r14(63) / rbx(40) / r15(19) / r12(15) / rbp(7) / rax(6) /
// r13(2) — and NEVER rcx, because rcx is the *index* register at every one of these sites.
// GOBJ_V1 hardcodes `48 8B 0C C8` (dest = rcx), so the whole V-series is structurally unable to
// fire here. Nibble-masking the REX + modrm is the fix.
//   SCOPED CORRECTLY b2499. The original wording — "every one of the 52 pre-existing GObjects
//   patterns MISSES or resolves only to decoys" — overstates it, and the sweep says so: on
//   UE4.27-DropIn, GOBJ_ES53_1 reaches truth (1 correct behind 34 decoys, OK-BEHIND) and is
//   still the SELECTED lander there, while G42_2 / GH_4 / PS3 are UNIQUE-OK and SAT426_1 is
//   OK-FIRST. "No decoy-free anchor" is the claim the data supports; "nothing worked" is not.
//
// SECOND TRAP: do NOT shorten these. The 14-byte core
// `48 8B 05 ?? ?? ?? ?? 4? 8B ?? C8 4? 85 ??` is decoy-free on DropIn but produces 1 decoy
// on Solarpunk and 9 on Avowed. The `75 ?? E8` (jnz over the noreturn check-fail call)
// tail is what takes all three to zero.
//
// THIRD, AND IT DECIDES THEIR BANDS — THESE ARE BUILD-CONFIG SHAPES, NOT "UE 4.27" SHAPES.
// Proven by a controlled A/B rather than inferred: the maintainer's own UE 4.27.2 "Flying"
// template, one project compiled three ways on one engine, with the config as the only variable
// (`D:\UE_Analyze_Data\Varies Version builds\4.27.2\{Development,DebugGame,Shipping}\Win64`):
//
//            DI427_1   DI427_2   DI427_3        (raw .text hit counts, offline scan)
//   Development   832      1415       246
//   DebugGame     832      1415       246       identical counts, different addresses
//   Shipping        0         0         0       <-- ALL THREE VANISH
//   (DropIn 4.27   925      1524       282  — a Development build, hence the match)
//
// The reason is in what a Shipping build strips. `_1` and `_2` both anchor on the
// `E8 <check-fail>; nop; int3` that `check()` emits; `_3` anchors on IndexToObject's
// `cmp <idx>,[NumElements]; jge` guard, which is the same check-shaped construct; and `_1`
// additionally needs the 32-byte `FUObjectItem` — those 8 bytes are `TStatId`, gated at 4.27 by
// `#if STATS || ENABLE_STATNAMEDEVENTS_UOBJECT` (`UObjectArray.h` @ 4.27.2-release), and `STATS`
// is 0 in Shipping. Two independently symbolised 4.27 SHIPPING binaries (Breeders, Maelstrom)
// carry the stock 24-byte item, so the 32-byte item was never a 4.27 trait.
//
// What that means per pattern, over the 51-program sweep:
//   * GOBJ_DI427_1 fires on exactly ONE binary — UE4.27-DropIn (925 hits, 0 decoys) — and is
//     never the selected pattern even there. It is the ONLY entry in this file that is both
//     config-gated AND item-size-gated, so it is DEMOTED 105 -> 256: a Development-only
//     fingerprint should not hold a GObjects batch-1 slot that every shipped game pays to scan.
//     Band by SEMANTICS, not by its 13 literal bytes — the same judgement the GOBJ_ES53_1
//     counter-example in the band block above makes in the opposite direction.
//     **DEMOTION VERIFIED by the full 58-program sweep (2026-07-29):** not one lander moved on
//     any oracle, REPORT.md's band audit came back EMPTY ("all patterns sit in a band consistent
//     with their specificity"), and DI427_1 now appears only in the §6 noise table. Neutral, as
//     predicted — the point was ordering hygiene, not a measurable win.
//   * GOBJ_DI427_2 (5 binaries) and _3 (4 binaries) DO reach genuine SHIPPING builds — 4.22 and
//     4.26/5.2 Satisfactory, which evidently ship with checks ENABLED, and that is not an
//     assumption: `_2`'s whole anchor IS the check-fail tail, so its 1,801 hits on the 4.22
//     Shipping EXE are the proof. Not config-gated in practice; they keep their bands.
// Do NOT read the demotion as "prune it": it is UNIQUE-OK wherever it fires, and rule 5 of
// GROUND-TRUTH.md (never prune on absence of proof) applies. It is insurance for the next
// Development/DebugGame build that walks in.

// DI427_1: inlined FChunkedFixedUObjectArray::GetObjectPtr + the 32-byte-item shift.
//   mov rax,[ObjObjects.Objects]; mov <r>,[rax+rcx*8]; test <r>,<r>; jnz; call check-fail;
//   nop; int3; mov <r2>,<withinIdx>; shl <r2>,5
//   The trailing `4? C1 E? 05` (shl r,5) is the 32-byte-FUObjectItem fingerprint — no other
//   pattern in this file encodes a 32-byte stride (they assume 16/20/24). Which is exactly why
//   it only ever matches a STATS-enabled (Development/DebugGame) build — see the block above.
constexpr const char* AOB_GOBJECTS_DI427_1 =
    "48 8B 05 ?? ?? ?? ?? 4? 8B ?? C8 4? 85 ?? 75 ?? E8 ?? ?? ?? ?? 90 CC 4? 8B ?? 4? C1 E? 05";
// DI427_2: item-size-AGNOSTIC core of _1 (stops before the shift), so it also covers the
//   sites where MSVC folded the mov away, and would still fire on a 24-byte-item build.
//   Broadest of the set -> lowest priority of the three.
constexpr const char* AOB_GOBJECTS_DI427_2 =
    "48 8B 05 ?? ?? ?? ?? 4? 8B ?? C8 4? 85 ?? 75 ?? E8 ?? ?? ?? ?? 90 CC";
// DI427_3: FUObjectArray::IndexToObject's real (non-check) bounds test + the
//   NumElementsPerChunk=64K divide/modulo. The 15-byte tail
//   `0F B7 D2 03 C2 8B C8 0F B7 C0 2B C2 C1 F9 10` is 100% literal and is the strongest
//   FChunkedFixedUObjectArray fingerprint in the image. Resolves to ObjObjects.NumElements,
//   so it needs adjustment -0x14 to land on ObjObjects.
constexpr const char* AOB_GOBJECTS_DI427_3 =
    "3B ?D ?? ?? ?? ?? 0F 8D ?? ?? ?? ?? 8B C? 89 ?? ?? 99 0F B7 D2 03 C2 8B C8 0F B7 C0 2B C2 C1 F9 10";

// ============================================================
// GNames / GWorld — DI427 (UE 4.27)
// ============================================================
// GNAM_DI427_1: the FName resolve prologue shared by ~10 leaf accessors
//   (operator== / GetComparisonNameEntry / GetDisplayNameEntry / GetEntry / ToString / ...).
//   lea rcx,[NamePoolData]; call FNamePool::FNamePool; mov <r>,rax; mov byte[bInit],1;
//   then reload the spilled FName as a qword and shift the Number half out.
//   Intended replacement for the GNAM_V5/V2/D7_1 family, which on this binary fire
//   16 686 / 16 692 / 104 897 times with ZERO correct hits. 10 sites, all correct.
constexpr const char* AOB_GNAMES_DI427_1 =
    "48 8D 0D ?? ?? ?? ?? E8 ?? ?? ?? ?? 4? 8B ?? C6 05 ?? ?? ?? ?? 01 48 8B 44 24 ?? 48 C1 E8 20";
// GNAM_DI427_2: same lazy-init head, but continued into the FNameEntry address math —
//   `add eax,eax` (FNameEntry stride 2) then `add rax,[pool + blockIdx*8 + 0x10]`
//   (Entries.Blocks at +0x10). Nothing but FName code does shr-32 / double / index-at-+0x10.
constexpr const char* AOB_GNAMES_DI427_2 =
    "48 8D 0D ?? ?? ?? ?? E8 ?? ?? ?? ?? 4? 8B ?? C6 05 ?? ?? ?? ?? 01 48 8B 44 24 ?? 48 C1 E8 20 03 C0 4? 03 ?? ?? 10";

// GWLD_DI427_1: UEngine::LoadMap — the canonical `GWorld = NewWorld` store, followed by
//   `WorldContext.World()->WorldType = WorldContext.WorldType`. The two structural
//   displacements are UE4.27-correct and PDB-confirmed: FWorldContext::ThisCurrentWorld
//   at +0x280 and UWorld::WorldType at +0x10A. No existing pattern anchors on LoadMap.
constexpr const char* AOB_GWORLD_DI427_1 =
    "48 89 15 ?? ?? ?? ?? E8 ?? ?? ?? ?? 41 0F B6 ?? 24 49 8B ?? 24 80 02 00 00 88 ?? 0A 01 00 00";
// GWLD_DI427_2: FSeamlessTravelHandler::Tick — `mov qword ptr [rip+d32], 0`, the
//   `GWorld = nullptr` teardown store. NOTE totalLen = 11, not 7: the disp32 still starts
//   at byte 3 but the instruction carries a trailing imm32. Every one of the 52 existing
//   GWorld patterns uses 48 8B / 48 39 / 48 3B / 4C 0F 44 / 48 89 — the C7-imm store form
//   is absent from the table entirely, so this shape is invisible to the scanner in EVERY
//   game today, not just this one.
constexpr const char* AOB_GWORLD_DI427_2 =
    "48 C7 05 ?? ?? ?? ?? 00 00 00 00 49 8B ?? 24 80 00 00 00 48 81 C? 38 01 00 00";

// ============================================================
// GEngine (UEngine* GEngine) — the &GEngine SLOT
// ============================================================
// Resolving the *slot* (not just the live object) is what makes this worth a target:
//   * FindGameEngine / RecoverGWorldViaEngine currently locate the engine by walking the
//     whole GObjects pool resolving a "GameViewport" property offset per class. With the
//     slot that becomes a single deref.
//   * The Teleport tab's UE_GameEngine CE symbol can stop being an allocateMemory snapshot
//     of a UEngine* (which goes stale on restart) and register against &GEngine like
//     UE_GWorld does, auto-following engine recreation.
//
// X1 is CROSS-VERSION: UWorld::GetGameViewport is a tiny, stable accessor whose body is
// `sub rsp,0x2X; mov rdx,rcx; mov rcx,[GEngine]; call; test rax,rax; jz`. The only
// difference between UE 4.27 and UE 5.7 is the stack size, hence the `2?` nibble.
// Verified: DropIn 2/2 correct, Solarpunk 1/1 correct, and on Avowed (UE 5.3, no symbols)
// both hits converge on ONE .data global — the expected shape for its GEngine, which the
// runtime validator then confirms or rejects.
constexpr const char* AOB_GENGINE_X1 =
    "48 83 EC 2? 48 8B D1 48 8B 0D ?? ?? ?? ?? E8 ?? ?? ?? ?? 48 85 C0 74 ??";
// X3: X1's HEAD only — `sub rsp,0x2X; mov rdx,rcx; mov rcx,[GEngine]; call` — with the REX of
// the `mov rdx,rcx` nibble-masked and X1's trailing `test rax,rax; jz` dropped.
//
// Why it exists: FF7 Remake (UE 4.18, SquareEnix fork) is the ONLY binary in a 26-program sweep
// where GEngine resolved to nothing at all — every GEngine pattern missed. Its
// GetWorldFromContextObject wrapper spills the result (`mov rbx,rax`) BEFORE the null check, so
// X1's `48 85 C0` no longer follows the call and no amount of nibble-masking can bridge it.
//
// Dropping the tail is safe here, and measured rather than assumed: the head alone is
// UNIQUE-OK with ZERO decoys on both symbolised oracles it was calibrated against
// (DropIn 4.27: 3/3 correct; Solarpunk 5.7: 2/2) — i.e. it finds strictly MORE correct sites
// than X1 (2 and 1) while introducing none that are wrong. On FF7R it produces exactly one hit,
// at 0x145879EE8, and that site was confirmed by disassembly to be
// `GEngine->GetWorldFromContextObject(Obj)` — the callee returns a UWorld which the caller
// immediately runs through GUObjectArray.IndexToObject. `GENGC_A` (a wholly different shape)
// independently resolves to the same address.
// X1 is kept AHEAD of this: it is the tighter of the two and costs nothing when it hits.
constexpr const char* AOB_GENGINE_X3 =
    "48 83 EC 2? 4? 8B D1 48 8B 0D ?? ?? ?? ?? E8";
// X2: FEngineLoop::Tick — `mov rbx,[GEngine]; test rbx,rbx; jz; call; mov rcx,[rbx+0x10]`.
// Also cross-version (DropIn 6/6, Solarpunk 7/7) and far more redundant than X1.
constexpr const char* AOB_GENGINE_X2 =
    "48 8B 1D ?? ?? ?? ?? 48 85 DB 74 ?? E8 ?? ?? ?? ?? 48 8B 4B 10 4C 8D 40";
// DI427: UGameplayStatics::GetRealTimeSeconds shape — 6 redundant sites on UE 4.27.
constexpr const char* AOB_GENGINE_DI427_1 =
    "48 83 EC 28 48 8B D1 41 B8 01 00 00 00 48 8B 0D ?? ?? ?? ?? E8 ?? ?? ?? ?? 48 85 C0 74 ?? F3 0F 10 80";
// SP57: UEngine::IsStereoscopic3D — UE 5.7 only, kept as an independent third anchor.
constexpr const char* AOB_GENGINE_SP57_1 =
    "48 89 5C 24 08 57 48 83 EC 20 48 8B 3D ?? ?? ?? ?? 33 DB 48 85 D2 74";
// X4: the generic "use a member of GEngine" accessor shape —
//   `mov rax,[GEngine]; test rax,rax; jz; mov rcx,[rax+disp32]; test rcx,rcx; jz`
// i.e. null-check the engine, load one of its object members (GameViewport / GameInstance /
// GameUserSettings / ...) at a 32-bit displacement, null-check that too. Mined on Palworld
// (UE 5.1, no PDB — the corpus had no 5.1 sample) but it turned out to be the most broadly
// portable GEngine pattern in the file. Measured over the full 35-program sweep it is correct on
// TWELVE oracles spanning UE 4.20 -> 5.7: UNIQUE-OK (decoy-free) on 4.22, 4.24, 4.26, 4.27, 5.2,
// 5.5 Meltopia and 5.6; correct-with-the-real-site-first on 4.20, 4.25, both 5.5 Everspace builds
// and 5.7. On Avowed (UE 5.3, no symbols) all 53 hits converge on a SINGLE address — the
// signature of a real global rather than a generic idiom, which is exactly what disqualified the
// rejected `mov rcx,[G]; test; jz; call [vtable]` variant (76-93 *different* targets per binary).
//
// HONEST EXCEPTION — the SquareEnix forks. On FF7 Remake this is DECOY-ONLY: 106 hits across 3
// different addresses, none of them the real GEngine; FF7 Rebirth is similarly divergent (90
// hits, 6 targets). Those two evidently reuse this member-access shape for something else. It
// costs nothing today because GENG_X3 (priority 105) wins on FF7 Remake before this is reached,
// and ValidateGEngineSlot would reject the decoys anyway (it derefs the slot and demands a
// reflected "GameViewport" property). But do not read "correct on 12 oracles" as "safe
// everywhere" — on a UE4.18-era SquareEnix fork this pattern is noise, and if it ever became the
// lander there the tail would need tightening.
//
// The `?? ?? 00 00` is load-bearing: it pins the member load to a 32-bit displacement, which is
// what UEngine's layout forces and what keeps this off the far commoner 8-bit-displacement
// `mov rcx,[rax+0x30]` idiom. Placed after X1/X3/X2 because it is OK-FIRST rather than
// UNIQUE-OK on three of the seven — it is the broad safety net, not the precision instrument.
constexpr const char* AOB_GENGINE_X4 =
    "48 8B 05 ?? ?? ?? ?? 48 85 C0 74 ?? 48 8B 88 ?? ?? 00 00 48 85 C9 74";
// ES55: UEngine::GetEngineSubsystem<T> prologue — `mov rdi,[GEngine]; call; cmp byte[flag],0`.
// Covers UE 5.5 AND 5.7 (7 sites on ES2 5.5, 6 on Solarpunk 5.7), 0 hits on UE4.27/5.3.
// This is the pattern that closes the 5.5 hole: X1/X2 both MISS on 5.5, because 5.5 emits
// FEngineLoop::Tick's null check as a NEAR jz (`0F 84`) where 4.27/5.7 use a short `74` —
// a length change no nibble can bridge. The obvious 5.5 FEngineLoop::Tick pattern was
// REJECTED instead: it takes 6 hits on Avowed that resolve to six DIFFERENT globals
// (a generic two-global null-check idiom). Divergent hits = generic shape; the accepted
// patterns' extra hits all converge on one address.
constexpr const char* AOB_GENGINE_ES55_1 =
    "48 89 5C 24 08 57 48 83 EC 20 48 8B 3D ?? ?? ?? ?? E8 ?? ?? ?? ?? 80 3D ?? ?? ?? ?? 00 48 8B D8";

// ============================================================
// GWorld — FD (UWorld::FinishDestroy, mined 2026-07-27 for the pre-4.20 flat-array era)
// ============================================================
// FD_1: mov rax,[GWorld]; cmp rax,<this>; cmovz rax,<replacement>; mov [GWorld],rax; call
//
//   mov   rax,[rip+GWorld]     48 8B 05 d32   <- resolved here (instrOffset = 0)
//   cmp   rax,<this>           48 3B C?       C6 on 4.11, C7 on 4.13, C3 on 4.20-5.2
//   cmovz rax,<replacement>    48 0F 44 C?
//   mov   [rip+GWorld],rax     48 89 05 d32   <- the SAME global, written back
//   call  ...                  E8
//
// 22 bytes: 12 fully-literal + 2 nibble-fixed + 8 wildcard. Both nibble masks pin the operand
// to rax, tying the compare and the cmov to the value just loaded. A READ FOLLOWED BY A
// CONDITIONAL WRITE-BACK OF THE SAME GLOBAL IS SELF-EVIDENCING — that, not the length, is why
// it is clean. Source confirmed from PDB symbols on THREE independent oracles (HeliumRain 4.20,
// DropIn 4.24, DropIn 4.27).
//
// WHY IT WAS MINED — and READ THIS BEFORE CONCLUDING FROM THE SWEEP THAT IT IS POINTLESS.
// The offline sweep shows 4.11 and 4.13 already resolving GWorld correctly, via GWLD_G42_1 at
// priority 880 after 5 and 6 fall-throughs. That is the harness model, not the runtime:
// scan_patterns.java HAS the truth and walks past a decoy, whereas ValidateGWorldBasic is
// deliberately loose and ACCEPTS the first one it is handed. So live, both titles land on a
// wrong GWorld and are rescued only by instance-scan recovery.
//   * NEKOPALIVE's decoy is a TSharedPtr {Object, ReferenceController} singleton at 0x1423C9940
//     whose +0 reads like a UWorld pointer — reached by GWLD_SAT52_1 (365).
//   * Fantasynth's is reached EARLIER, by GWLD_SF_2 (300); its analogues are 14288E648 /
//     1427B0B40 / 14277D3C0.
// At 265 this pattern is scanned BEFORE both (300 and 365), so the true GWorld is validated and
// returned before either decoy is ever presented. That — not the fall-through count — is the
// fix. It was explicitly checked against all four decoy addresses and hits none of them.
//
// This exists because the maintainer's call was "add a pattern, do NOT tighten a validator 30+
// oracles depend on, to fix one 2016 title" (see the V2/V4/V5/V6 removal note below for why a
// wrong GWorld is worse than no GWorld).
//
// RELATION TO GWLD_G427_5 (390, same function): this is a strict generalisation. The nibbled
// CMP reaches the rsi/rdi codegen that G427_5's hardcoded `48 3B C3` cannot, the exact
// `48 0F 44 C?` tightens a CMOV that G427_5 leaves wildcarded, and the trailing `E8` adds a
// literal byte. G427_5 is DECOY-ONLY on both 4.11 and 4.13.
//
// MEASURED on the full landing sweep — 46 programs / 23 with GWorld truth, UE 4.11 -> 5.7:
// 21 hits, 16 UNIQUE-OK, 5 NO-TRUTH, 25 MISS. ZERO decoys anywhere, and NEVER more than ONE hit
// on any single binary. Absent from both the hotspot and the dead-weight table.
//   UNIQUE-OK (16): 4.11, 4.13, 4.18 DQ XI S, 4.20 x2, 4.21, 4.24, 4.25, 4.26, 4.27 x3, 5.0,
//                   5.1, 5.2
//   NO-TRUTH,1 hit: FF7 Remake, Octopath, Artisan, Palworld, DQ I&II
//   MISS:           4.22, FF7 Rebirth, Avowed, Elliot, all 5.5/5.6, Solarpunk
// It became the LANDER on four binaries, three of them an improvement and none a regression:
// 4.11 (G42_1 @880, 5 wasted -> 0), 4.13 (G42_1 @880, 6 -> 0), 4.26 Satisfactory Engine
// (SF_2 @300, 2 -> 0), 5.2 Satisfactory Engine (lateral from SF_2). Those were the only three
// GWorld entries in the report's fall-through list, so that list is now GObjects-only.
//
// CAVEATS, recorded rather than papered over:
//   * NO 4.12 BINARY EXISTS in the corpus. 4.12 is bracketed structurally (identical shape on
//     4.11 and 4.13 and every version through 5.2) but never measured. Do not write it up as
//     verified.
//   * `48 3B C?` also admits `cmp rcx,r??`. It cost zero decoys over 45 programs, but it is
//     looser than the intent — noted rather than tightened blind.
//   * instrOffset was PROVEN by counter-example: setting it to 4 keeps the hit count IDENTICAL
//     while the resolved address lands outside the image. The usual silent failure.
constexpr const char* AOB_GWORLD_FD_1 =
    "48 8B 05 ?? ?? ?? ?? 48 3B C? 48 0F 44 C? 48 89 05 ?? ?? ?? ?? E8";

// ============================================================
// GNames — XX (pre-4.23 FName::GetNames write-back, mined 2026-07-28)
// ============================================================
// XX_1: call malloc; mov rax,rbx; mov [rip+Names],rbx; mov rbx,[rsp+20]; add rsp,28; ret
//
// WHY IT EXISTS. GROUND-TRUTH.md called pre-4.23 GNames the corpus's thinnest coverage,
// resting on "two patterns, GNAM_CT3 (700) and GNAM_G42_1 (710), of the SAME shape".
// Measuring it made that worse, not better: G42_1's byte string is a STRICT SUPERSET
// WINDOW of CT3 offset by +4 (CT3[4:] equals G42_1 except that G42_1 wildcards the two
// bytes CT3 pins as literal 00 00), so every CT3 match at A implies a G42_1 match at
// A+4 — verified token-by-token and empirically on all 36 programs. They are ONE SHAPE
// MATCHED TWICE. A compiler change to that lazy-init prologue takes pre-4.23 GNames out
// entirely, and always could have.
//
// WHY IT IS THE SAME FUNCTION, and why that is the honest ceiling. An xref census of the
// TNameEntryArray global (capstone over every .pdata-covered function + a raw .text
// disp32 sweep) finds 3 xrefs on Satisfactory 4.22 (all inside FName::GetNames) and 9 on
// 4.15.3 over 4 functions — but the extra functions are INLINE EXPANSIONS of GetNames
// carrying the identical lazy-init shape (disassembled and confirmed verbatim), so they
// add no structural independence. FName::ToString / AppendString / the comparison and
// hash paths contain ZERO references: they call GetNames and use the returned register.
// A caller-side anchor was mined and REFUSED — `and edx,0x3FFF` preceded by a call to
// GetNames sits at distance {11,12,27,30} on 4.15.3 but at {14,19,20} on 4.22, so no
// fixed-offset AOB spans the band and there is nothing to hang a CallFollow on.
// So this is a different SITE in the same function, not a different function. That is
// the most independence the engine actually offers, and saying otherwise would be false.
//
// The site is chosen to share nothing with the prologue the other three anchor on: the
// SUCCESS-PATH WRITE-BACK plus epilogue. No load, no test, no conditional branch, no
// shadow-space spill, no allocation-size immediate — the five things that differ between
// builds are exactly the five things absent here.
//
// MEASURED, 8 of 8 pre-4.23 oracles, correct hit at INDEX 0 on every one — the only
// pattern in this file that covers all of them:
//   4.11 Nekopara 1 hit UNIQUE-OK    4.20 Everspace   41 hits OK-FIRST
//   4.13 Fantasynth 1 hit UNIQUE-OK  4.20 HeliumRain  29 hits OK-FIRST
//   4.15.3 Flying 1 hit UNIQUE-OK    4.21 FreudGate   29 hits OK-FIRST
//   4.15.3 CrashRC 1 hit UNIQUE-OK   4.22 Satisfactory 1 hit UNIQUE-OK
// Contrast the incumbents, which reach truth on only 6 of 8: UNIQUE-OK on 4.11 / 4.13 /
// 4.15-Flying / 4.20-Everspace, OK-BEHIND on 4.20-HeliumRain AND 4.21-FreudGate, and
// MISS on 4.22-Satisfactory and 4.15.3-CrashReportClient.
// Whole corpus, 36 programs: 4 UNIQUE-OK, 3 OK-FIRST, 10 DECOY-ONLY, 12 MISS, 7 NO-TRUTH,
// and ZERO spurious-correct anywhere.
//
// BAND: 717 is justified by SEMANTICS, not by its 17 literal bytes — the GOBJ_ES53_1
// precedent above. A bare `call; mov; mov [rip]; epilogue` is a common shape, so it earns
// no Tier-1 slot; it sits beside the other pre-4.23 anchors it is insurance for.
constexpr const char* AOB_GNAMES_XX_1 =
    "E8 ?? ?? ?? ?? 48 8B C3 48 89 1D ?? ?? ?? ?? 48 8B 5C 24 20 48 83 C4 28 C3";


// ============================================================
// Unified Pattern Arrays (sorted by priority)
// ============================================================
// Priority scheme (lower = tried first; each target's array is sorted by priority at
// scan time). Values are SPARSE across 0–1000 BY DESIGN — the gaps let a new pattern
// slot into the right band without renumbering its neighbours. The absolute number is
// meaningless; only the order within a target's array matters. When adding a pattern,
// pick an unused value in the matching band (they step by 10, so there is room between
// any two). Bands:
//     0– 30   Symbol exports / call-follow (exact address, O(1))
//    40– 90   Symbol-derived (FName ctor call-site scan, etc.)
//   100–290   Tier 1 — long, highly-specific, verified-unique (newest-engine, decoy-proof)
//   300–490   Tier 2 — medium specificity, good surrounding context (per-game)
//   500–590   Tier 3 — standard short patterns (common codegen)
//   600–690   Patternsleuth (arithmetic / offset-anchored)
//   700–790   UE4 / legacy-specific
//   800–990   Very short generic / last-resort
//
// BAND DISCIPLINE (build 2405). A pattern's band is set by how SPECIFIC it is — count its
// LITERAL (non-wildcard) bytes — not by how old it is or who contributed it. The GNames
// table had drifted badly in both directions and was re-sorted from measured data:
//   * GNAM_V1/V3/V4 are 8 bytes with FOUR literal bytes, the least specific patterns in the
//     file, yet sat at 500-540. Measured: DECOY-ONLY on UE4.20/5.5/5.7 and 539-2060 hits
//     where they do reach truth. They belong in 800-990 and are now there.
//   * GNAM_V5 (7 literal bytes) sat in the TIER 1 band at 110 while producing 16,686 hits on
//     UE4.27 and OK-BEHIND on every engine it touches. Demoting it is a straight upgrade:
//     UE5.5 and UE5.6 now select GNAM_ES53_1 and UE5.7 selects GNAM_SAT425_3, all UNIQUE-OK.
//   * The pre-FNamePool UE4 patterns (CT3 20 literal bytes, G42_1, CT4, SAT422_1) were
//     stranded at 800-860 in the last-resort band despite being the LONGEST and most specific
//     entries — they were hand-derived later and deliberately lengthened. They target
//     TStaticIndirectArrayThreadSafeRead / TNameEntryArray, a different structure entirely,
//     and measurably MISS on all four FNamePool binaries (4.27/5.5/5.6/5.7), so moving them
//     up to 700-730 cannot cost anything and saves ~710 wasted validations on a UE4.20 title.
// Rule of thumb: fewer than ~8 literal bytes means 800+, no matter what it is anchored on.
//
// ── STACK DISPLACEMENTS: wildcard the value, keep the instruction ────────────────────────
// `lea rdx,[rsp+????????]` is fine in a pattern. `lea rdx,[rsp+00000318]` is not.
//
// A frame displacement encodes the CALLEE'S FRAME LAYOUT — local count, register spills,
// inlining decisions, alignment. None of that is a property of Unreal Engine; it is a property
// of one compilation, and it moves when a patch adds a single local. A STRUCT displacement is
// the opposite and must be KEPT: `cmp [rcx+0x2C0],rax` (UWorld member) or `cmp eax,[rdi+0x34]`
// (TSet Max) pin UE's real data layout, which is version-stable and is exactly the evidence that
// makes a pattern trustworthy. So the rule is not "avoid stack instructions" — it is
// "wildcard FRAME displacements, keep STRUCT displacements".
//
// TWO MEASURED QUALIFIERS, both from build 2437:
//   1. ONLY IF THE PATTERN HAS ENOUGH OTHER LITERAL CONTEXT. Wildcarding GWLD_GH_3's two frame
//      offsets took it from 5 binaries to SEVEN, UNIQUE-OK and decoy-free on every one — a pure
//      gain, because its other 22 literal bytes carry the selectivity. Doing the same to
//      GWLD_G42_4, which has only SEVEN literal bytes total, turned clean UNIQUE-OK results into
//      OK-BEHIND on three engine versions and produced 38 hits / 37 decoys on UE 4.27. On a short
//      pattern the frame offset IS the selectivity — which is itself a reason to distrust the
//      pattern, but wildcarding makes it worse, not better.
//   2. SMALL SHADOW-SPACE CONSTANTS ARE NOT FRAME LAYOUT. `sub rsp,0x28` / `mov [rsp+0x20],rbx`
//      (<= 0x40) are the standard x64 prologue for a function with <= 4 register parameters and
//      are effectively idiomatic across compilers and builds. Ten patterns here bake those in
//      and are fine. The rule targets the large, genuinely frame-specific values (0x50+).
//
// BUILD 2407 — the same audit applied to GObjects and GWorld, which build 2405 left alone.
// Measured over 26 programs (11 with PDB truth) via tools/ghidra/sweep.sh + aggregate_sweep.py:
//   * GWLD_V3/V4/V5/V2/V6 sat at 500-580 on 4-7 literal bytes and are the noisiest block in the
//     file — GWLD_V3 takes 22,017 matches (95.7 per MB of .text on a monolithic EXE; 2,658 on
//     FF7 Remake alone). V2/V4/V5/V6 reach the true GWorld on ZERO oracles. Now 900-980.
//   * GOBJ_V1/V2/V3/V5/V6/V7/CT3 + the PS6/PS7 arithmetic pair, same story (GOBJ_V1: 10,152
//     matches, 53/MB). Now 890-970.
// Nothing demoted here is load-bearing: every oracle lands on a pattern at priority <= 435 for
// GWorld and <= 210 for GObjects, and the post-change sweep confirms not one landing pattern
// moved. What demotion actually buys is ORDERING SAFETY, not speed — see the per-table notes;
// the GObjects block really did outrank longer patterns, the GWorld block already sat last.
//
// The reason NOT to reflexively demote a noisy-but-early pattern: patterns are scanned in
// BATCHES OF 8 and ScanForTarget returns on the first validated match, so a pattern that wins
// from batch 1 avoids every later .text pass. Rejecting a few hundred candidates by validation
// is far cheaper than an extra AVX2 sweep of a 130 MB .text. That is precisely why
// GOBJ_ES53_1 stays at 100 despite costing up to 475 wasted validations (UE 5.5) — it is the
// landing pattern for six module-instances, and buying that with one batch is a good trade.
//
// A COUNTER-EXAMPLE worth keeping, because it shows literal-byte count is necessary but not
// sufficient: GOBJ_ES53_1 has 16 literal bytes yet takes 21-131 matches on every monolithic
// title. Its shape (`sub rsp,28; lea rcx,[X]; call ctor; lea rcx,[Y]; add rsp,28; jmp atexit`)
// is the generic MSVC function-scope-static registration thunk, so it matches once per static
// with a destructor. It stays at priority 100 regardless: it is the pattern the runtime lands
// on for FIVE engine versions, and its decoys are all rejected by ValidateGObjects. Judge a
// band by specificity AND semantics, not byte count alone.

// Helper macro to reduce boilerplate for common RipBoth patterns
#define SIG_RIP(id, pat, tgt, ioff, opc, tot, adj, pri, src, note) \
    { id, pat, tgt, AobResolve::RipBoth, ioff, opc, tot, adj, pri, 0, false, src, note }
#define SIG_RIP_DIRECT(id, pat, tgt, ioff, opc, tot, adj, pri, src, note) \
    { id, pat, tgt, AobResolve::RipDirect, ioff, opc, tot, adj, pri, 0, false, src, note }
#define SIG_EXPORT(id, sym, tgt, pri, note) \
    { id, sym, tgt, AobResolve::SymbolExport, 0, 0, 0, 0, pri, 0, false, "EXP", note }
#define SIG_SYM_CALL(id, sym, tgt, pri, note) \
    { id, sym, tgt, AobResolve::SymbolCallFollow, 0, 0, 0, 0, pri, 0, false, "EXP", note }
#define SIG_GWORLD_RIP(id, pat, ioff, opc, tot, adj, pri, allowNull, src, note) \
    { id, pat, AobTarget::GWorld, AobResolve::RipBoth, ioff, opc, tot, adj, pri, 0, allowNull, src, note }

// ── GObjects ─────────────────────────────────────────────────────────────
constexpr AobSignature GOBJECTS_PATTERNS[] = {
    // 0: Symbol export (O(1))
    SIG_EXPORT("GOBJ_EXP", EXPORT_GOBJECTARRAY, AobTarget::GObjects, 0, "MSVC mangled symbol"),

    // 100–290: Tier 1 — long, specific patterns
    SIG_RIP("GOBJ_ES53_1", AOB_GOBJECTS_ES53_1, AobTarget::GObjects, 4, 3, 7, 0, 100, "ES53", "ES2 UE5.3 FUObjectArray ctor+atexit"),
    // 105 was GOBJ_DI427_1 — DEMOTED to 256 in build 2499. It is the file's only pattern that is
    // both build-config-gated (needs `check()`) and item-size-gated (needs the 32-byte STATS
    // FUObjectItem), so it can only ever match a Development/DebugGame build: measured 0 hits on
    // the Shipping config of the very same 4.27.2 project that gives it 832. See the three-config
    // A/B table in its comment above.
    { "GOBJ_V10", AOB_GOBJECTS_V10, AobTarget::GObjects, AobResolve::RipBoth,
      0, 3, 7, -0x10, 110, 0, false, "V", "Split Fiction UE5.5+ lea+call+call" },
    SIG_RIP("GOBJ_DI427_3", AOB_GOBJECTS_DI427_3, AobTarget::GObjects, 0, 2, 6, -0x14, 115, "DI427",
            "UE4.27 IndexToObject bounds test + 64K chunk divide (-> NumElements, adj -0x14)"),
    SIG_RIP("GOBJ_AV1", AOB_GOBJECTS_AV1, AobTarget::GObjects, 0, 3, 7, -0x10, 120, "AV",
            "Avowed/Obsidian UE5.3 AllocateUObjectIndex MOV RDX,[ObjObjects.Objects]"),
    SIG_RIP("GOBJ_AV2", AOB_GOBJECTS_AV2, AobTarget::GObjects, 0, 3, 7, -0x10, 130, "AV",
            "Avowed/Obsidian UE5.3 FUObjectItem chunk-index (20B stride, ~10+ sites, patch-resilient)"),
    SIG_RIP("GOBJ_G42_4", AOB_GOBJECTS_G42_4, AobTarget::GObjects, 0, 3, 7, 0, 140, "G42", "UE4.2 long lea+call+epilogue"),
    SIG_RIP("GOBJ_SAT425_2", AOB_GOBJECTS_SAT425_2, AobTarget::GObjects, 0, 3, 7, 0, 150, "SAT425", "Satisfactory UE4.25 UObjectBaseInit 31-byte sequence"),
    SIG_RIP("GOBJ_SAT422_1", AOB_GOBJECTS_SAT422_1, AobTarget::GObjects, 0, 3, 7, 0, 160, "SAT422", "Satisfactory UE4.22 FEngineLoop::PreInit 4-CALL chain"),
    SIG_RIP("GOBJ_SAT425_1", AOB_GOBJECTS_SAT425_1, AobTarget::GObjects, 0, 3, 7, 0, 170, "SAT425", "Satisfactory UE4.25 FObjectIterator ctor"),
    { "GOBJ_RE3", AOB_GOBJECTS_RE3, AobTarget::GObjects, AobResolve::RipBoth,
      0, 3, 7, 0, 180, 0, false, "RE", "Little Nightmares 3 Demo extended" },
    { "GOBJ_V11", AOB_GOBJECTS_V11, AobTarget::GObjects, AobResolve::RipBoth,
      0, 3, 7, 0, 190, 0, false, "V", "Little Nightmares 3" },
    SIG_RIP("GOBJ_RE2", AOB_GOBJECTS_RE2, AobTarget::GObjects, 0, 3, 7, -0x10, 200, "RE", "FF7 Remake extended"),
    SIG_RIP("GOBJ_V13", AOB_GOBJECTS_V13, AobTarget::GObjects, 0, 3, 7, 0, 210, "V", "Palworld extended context"),
    SIG_RIP("GOBJ_ES2_1", AOB_GOBJECTS_ES2_1, AobTarget::GObjects, 0, 3, 7, 0, 220, "ES2", "UE5.5 AllocateUObjectIndex"),
    SIG_RIP("GOBJ_SAT52_1", AOB_GOBJECTS_SAT52_1, AobTarget::GObjects, 0, 3, 7, 0, 230, "SAT52", "Satisfactory UE5.2 TObjectIteratorBase ctor"),
    SIG_RIP("GOBJ_V12", AOB_GOBJECTS_V12, AobTarget::GObjects, 0, 3, 7, -0x10, 240, "V", "FF7 Remake"),
    SIG_RIP("GOBJ_SF_1", AOB_GOBJECTS_SF_1, AobTarget::GObjects, 0, 3, 7, 0, 250, "SF", "SatisfFactory via _imp_ (in EXE)"),
    // _1 BEFORE _2 (swapped 255<->256 in build 3262, AD11). DI427_2 is a strict PREFIX of
    // DI427_1 — its 23 tokens are _1's first 23, and both carry the identical (0,3,7,0) triple —
    // so every _1 match is also a _2 match AT THE SAME ADDRESS, resolving to the same target.
    // Pass 1 takes the first pattern with a validating match and scans matches in address order,
    // so with _2 ahead of it _1 could never win anything _2 had not already won or already
    // failed: it was UNREACHABLE, and the 105 -> 256 demotion (build 2499) is what made it so.
    // The demotion's PURPOSE is untouched — both stay at the tail of Tier 1, so a
    // Development-only fingerprint still does not hold an early slot every shipped game pays to
    // scan; only their order relative to each other changed.
    // ⛔ Deliberately NOT pruned. The block above its constant records a measured decision not to
    // (GROUND-TRUTH.md rule 5, "never prune on absence of proof"), and putting the longer,
    // more-specific pattern first is this file's own band rule: "a short generic pattern
    // outranking a long purpose-built one is exactly the ordering that lets a decoy win".
    SIG_RIP("GOBJ_DI427_1", AOB_GOBJECTS_DI427_1, AobTarget::GObjects, 0, 3, 7, 0, 255, "DI427",
            "UE4.27 GetObjectPtr + 32-byte-item shl 5 — DEVELOPMENT/DEBUGGAME ONLY (was 105)"),
    SIG_RIP("GOBJ_DI427_2", AOB_GOBJECTS_DI427_2, AobTarget::GObjects, 0, 3, 7, 0, 256, "DI427",
            "UE4.27 GetObjectPtr core, item-size agnostic (broadest — last in Tier 1)"),

    // 300–490: Tier 2 — medium patterns
    SIG_RIP("GOBJ_G42_2", AOB_GOBJECTS_G42_2, AobTarget::GObjects, 0, 3, 7, 0, 260, "G42", "UE4.2 RemoveUObjectDeleteListener"),
    SIG_RIP("GOBJ_G42_3", AOB_GOBJECTS_G42_3, AobTarget::GObjects, 0, 3, 7, 0, 300, "G42", "UE4.2 lea+mov r8d+mov edx"),
    SIG_RIP("GOBJ_G42_1", AOB_GOBJECTS_G42_1, AobTarget::GObjects, 0, 3, 7, 0, 310, "G42", "UE4.2 lea+xor+mov constructor"),
    SIG_RIP("GOBJ_GH_1", AOB_GOBJECTS_GH_1, AobTarget::GObjects, 12, 3, 7, 0, 320, "GH", "Ghidra UObjectBase::AddObject cross-game"),
    SIG_RIP("GOBJ_GH_4", AOB_GOBJECTS_GH_4, AobTarget::GObjects, 12, 3, 7, 0, 330, "GH", "Ghidra FWeakObjectPtr::operator= cross-game"),
    { "GOBJ_RE1", AOB_GOBJECTS_RE1, AobTarget::GObjects, AobResolve::RipBoth,
      0, 2, 6, 0, 340, 0, false, "RE", "FF7 Rebirth add+cmp+jge" },
    SIG_RIP("GOBJ_GH_2", AOB_GOBJECTS_GH_2, AobTarget::GObjects, 12, 3, 7, 0, 350, "GH", "Ghidra UnMarkAllObjects cross-game"),
    SIG_RIP("GOBJ_V4",  AOB_GOBJECTS_V4,  AobTarget::GObjects, 0, 3, 7, 0, 360, "V", "classic UE5 longer context"),
    SIG_RIP("GOBJ_V8",  AOB_GOBJECTS_V8,  AobTarget::GObjects, 0, 3, 7, 0, 370, "V", "bit shift variant"),
    SIG_RIP("GOBJ_V9",  AOB_GOBJECTS_V9,  AobTarget::GObjects, 0, 3, 7, 0, 380, "V", "extended index cdqe"),
    SIG_RIP("GOBJ_UD1", AOB_GOBJECTS_UD1, AobTarget::GObjects, 0, 3, 7, 0, 400, "UD", "UEDumper"),
    SIG_RIP("GOBJ_GH_3", AOB_GOBJECTS_GH_3, AobTarget::GObjects, 12, 3, 7, 0, 410, "GH", "Ghidra IncrementalPurgeGarbage cross-game"),
    SIG_RIP("GOBJ_G427_1", AOB_GOBJECTS_G427_1, AobTarget::GObjects, 0, 3, 7, 0, 420, "G427", "UE4.27 Objects SAR context"),
    SIG_RIP("GOBJ_G427_3", AOB_GOBJECTS_G427_3, AobTarget::GObjects, 0, 3, 7, 0, 430, "G427", "UE4.27 FGCObject extended context"),
    SIG_RIP("GOBJ_SAT426_1", AOB_GOBJECTS_SAT426_1, AobTarget::GObjects, 0, 3, 7, 0, 440, "SAT426", "Satisfactory UE4.26 RemoveAnnotation lea+call+test"),
    SIG_RIP("GOBJ_SAT426_2", AOB_GOBJECTS_SAT426_2, AobTarget::GObjects, 0, 2, 6, 0, 450, "SAT426", "Satisfactory UE4.26 GatherUnreachableObjects"),
    SIG_RIP("GOBJ_SAT52_2", AOB_GOBJECTS_SAT52_2, AobTarget::GObjects, 0, 3, 7, 0, 460, "SAT52", "Satisfactory UE5.2 ~UObjectBase IsValid"),

    // 500–590: Tier 3 — now EMPTY for GObjects. The whole V-series short-pattern block moved to
    // the 800–990 last-resort band in build 2407; see the band-discipline note above. All six
    // carry 6–7 literal bytes, and not one of them is the pattern the runtime lands on for
    // ANY of the 11 symbolised oracles.

    // 600–690: Patternsleuth (instrOffset != 0)
    SIG_RIP("GOBJ_PS1", AOB_GOBJECTS_PS1, AobTarget::GObjects, 21, 3, 7, 0, 600, "PS", "cmp/cmp/jne; lea"),
    SIG_RIP("GOBJ_PS2", AOB_GOBJECTS_PS2, AobTarget::GObjects,  2, 3, 7, 0, 610, "PS", "jz; lea rcx"),
    SIG_RIP("GOBJ_PS3", AOB_GOBJECTS_PS3, AobTarget::GObjects,  5, 3, 7, 0, 620, "PS", "jne; mov; lea rcx"),
    SIG_RIP("GOBJ_PS4", AOB_GOBJECTS_PS4, AobTarget::GObjects, 16, 3, 7, 0, 630, "PS", "test; mov; lea r11"),
    SIG_RIP("GOBJ_PS5", AOB_GOBJECTS_PS5, AobTarget::GObjects, 12, 3, 7, 0, 640, "PS", "or; and; mov; lea rcx"),
    // 700–790: UE 4.27 patterns with offsets/adjustments
    SIG_RIP("GOBJ_G427_2", AOB_GOBJECTS_G427_2, AobTarget::GObjects, 0, 2, 6, -0x14, 700, "G427", "UE4.27 NumElements CMP (adj -0x14)"),
    SIG_RIP("GOBJ_G427_4", AOB_GOBJECTS_G427_4, AobTarget::GObjects, 0, 2, 6, 0x0C, 720, "G427", "UE4.27 ObjLastNonGCIndex (adj +0x0C)"),

    // 800–990: UE4/legacy
    SIG_RIP("GOBJ_CT1", AOB_GOBJECTS_CT1, AobTarget::GObjects, 5, 3, 7, 0, 800, "CT", "UE4 Dumper.CT v5+"),
    SIG_RIP("GOBJ_OT_1", AOB_GOBJECTS_OT_1, AobTarget::GObjects, 2, 3, 7, 0, 820, "OT", "Octopath Traveller UE4 FUObjectArray::Init LEA RCX"),
    SIG_RIP("GOBJ_OT_2", AOB_GOBJECTS_OT_2, AobTarget::GObjects, 2, 3, 7, 0, 840, "OT", "UE4 FUObjectArray::Init generalized (wildcarded regs)"),
    // 890–970: the short V-series + patternsleuth-arithmetic block, demoted here in build 2407
    // (was 390–660). Measured over 31 programs: GOBJ_V1 alone takes 10,152 matches (53/MB on a
    // monolithic EXE) and GOBJ_V3 1,333, while V2/V3/V5/V6 reach the true address on ZERO of the
    // 17 oracles and V1/V7 never win one either.
    //
    // Unlike the GWorld block this IS a real ordering change: at 390–660 they came BEFORE
    // GOBJ_G427_2 (700), G427_4 (720), CT1 (800) and the Octopath OT_1/OT_2 pair (820/840) —
    // all of which carry 9–13 literal bytes against these six or seven. A short generic pattern
    // outranking a long purpose-built one is exactly the ordering that lets a decoy win.
    // They stay in the table as insurance for engine builds the corpus does not cover.
    SIG_RIP("GOBJ_V7",  AOB_GOBJECTS_V7,  AobTarget::GObjects, 0, 3, 7, 0, 890, "V", "GSpots cdq movzx"),
    SIG_RIP("GOBJ_V2",  AOB_GOBJECTS_V2,  AobTarget::GObjects, 0, 3, 7, 0, 900, "V", "common UE5.3+"),
    SIG_RIP("GOBJ_V1",  AOB_GOBJECTS_V1,  AobTarget::GObjects, 0, 3, 7, 0, 910, "V", "classic UE5.0-5.2"),
    SIG_RIP("GOBJ_V6",  AOB_GOBJECTS_V6,  AobTarget::GObjects, 0, 3, 7, 0, 920, "V", "alt mov rcx"),
    SIG_RIP("GOBJ_V3",  AOB_GOBJECTS_V3,  AobTarget::GObjects, 0, 3, 7, 0, 930, "V", "mov r8"),
    SIG_RIP("GOBJ_V5",  AOB_GOBJECTS_V5,  AobTarget::GObjects, 0, 3, 7, 0, 940, "V", "mov r10"),
    SIG_RIP("GOBJ_CT3", AOB_GOBJECTS_CT3, AobTarget::GObjects, 0, 3, 7, 0, 950, "CT", "mov r8; cmp"),
    SIG_RIP("GOBJ_PS6", AOB_GOBJECTS_PS6, AobTarget::GObjects, 12, 2, 6, 0, 960, "PS", "arithmetic sub eax"),
    SIG_RIP("GOBJ_PS7", AOB_GOBJECTS_PS7, AobTarget::GObjects, 17, 2, 6, 0, 970, "PS", "arithmetic add ecx"),
};

// ── Obfuscated FName payloads (licensee forks) ───────────────────────────
// Not part of any PATTERNS[] table: this does not resolve a global pointer, so it is
// consumed directly by Genau::ResolveNameKeyTable rather than through ScanForTarget.
// It is scanned ONLY after the experimental gate is on AND both stock FNameEntry
// layouts have already been rejected, so an ordinary title never runs it.
//
// ME1: the fork's FNameEntry payload de-obfuscator, matched at its function entry.
//   mov [rsp+8],rbx / mov [rsp+10],rsi / push rdi / sub rsp,20
//   movzx r8d,word [rcx]      <- stock 2-byte header
//   lea   rdx,[rcx+4]         <- chars at entry+4 (stock is +2: the fork inserts a u16 tag)
//   shr   r8,6                <- len = header >> 6 (stock Format A)
//   call  memcpy              <- rel32 wildcarded
//   movzx edi,word [rbx] / shr edi,6
//   call  <key-table ctx getter>   <- rel32 wildcarded; followed at match+0x2F
//   movzx edx,word [rbx+2]    <- the non-stock u16 tag that selects the XOR key
// The match address is EVIDENCE, never a call target — Genau follows the second call
// and the getter's rip-relative LEA to reach the tag->key table and reads it directly.
// Verified unique in MindsEye's 145 MB .text (the 16-byte MSVC prologue alone hits 139
// times; the semantic tail is what carries the uniqueness).
constexpr const char* AOB_NAMEDECRYPT_ME1 =
    "48 89 5C 24 08 48 89 74 24 10 57 48 83 EC 20 44 0F B7 01 48 8B F2 "
    "48 8D 51 04 49 C1 E8 06 48 8B D9 48 8B CE E8 ?? ?? ?? ?? 0F B7 3B "
    "C1 EF 06 E8 ?? ?? ?? ?? 0F B7 53 02";
// Offset within a match of the `call <ctx getter>` instruction (its rel32 is at +1).
constexpr int AOB_NAMEDECRYPT_ME1_CTX_CALL_OFF = 0x2F;

// ── GNames ───────────────────────────────────────────────────────────────
constexpr AobSignature GNAMES_PATTERNS[] = {
    // 0–20: Symbol exports → scan function body for FNamePool references
    SIG_SYM_CALL("GNAM_EXP_TOSTR", EXPORT_FNAME_TOSTRING, AobTarget::GNames, 0, "FName::ToString export"),
    SIG_SYM_CALL("GNAM_EXP_CTOR",  EXPORT_FNAME_CTOR,     AobTarget::GNames, 10, "FName ctor (wchar) export"),
    SIG_SYM_CALL("GNAM_EXP_CTOR2", EXPORT_FNAME_CTOR_CHAR,AobTarget::GNames, 20, "FName ctor (char) export"),

    // 40: FName ctor call-site (follows CALL, scans body)
    { "GNAM_V7", AOB_GNAMES_V7_FNAME_CTOR, AobTarget::GNames, AobResolve::CallFollow,
      0, 0, 0, 0, 40, 11, false, "V", "FF7 Rebirth FName ctor call-site" },

    // 100–290: Tier 1 — long, specific patterns
    SIG_RIP("GNAM_V8",    AOB_GNAMES_V8,     AobTarget::GNames, 0, 3, 7, 0, 100, "V", "Palworld extended context"),
    SIG_RIP("GNAM_DI427_2", AOB_GNAMES_DI427_2, AobTarget::GNames, 0, 3, 7, 0, 105, "DI427",
            "UE4.27 FName resolve + FNameEntry addr math (stride 2, Blocks at +0x10)"),
    SIG_RIP("GNAM_DI427_1", AOB_GNAMES_DI427_1, AobTarget::GNames, 0, 3, 7, 0, 115, "DI427",
            "UE4.27 FName resolve prologue, 10 sites (replaces the V5/V2/D7_1 decoy family)"),
    SIG_RIP("GNAM_ES53_1", AOB_GNAMES_ES53_1, AobTarget::GNames, 0, 3, 7, 0, 120, "ES53", "ES2 UE5.3 FNamePool init + MOV RDX,RAX"),
    SIG_RIP("GNAM_GH_1",  AOB_GNAMES_GH_1,   AobTarget::GNames, 12, 3, 7, 0, 130, "GH", "Ghidra ReserveNameBatch 27-fixed cross-game"),
    SIG_RIP("GNAM_SAT52_1", AOB_GNAMES_SAT52_1, AobTarget::GNames, 0, 3, 7, 0, 140, "SAT52", "Satisfactory UE5.2 dual-LEA NamePoolData"),
    SIG_RIP("GNAM_SAT425_1", AOB_GNAMES_SAT425_1, AobTarget::GNames, 18, 3, 7, 0, 150, "SAT425", "Satisfactory UE4.25 FName::AppendString LEA R8"),
    SIG_RIP("GNAM_SAT425_2", AOB_GNAMES_SAT425_2, AobTarget::GNames, 0, 3, 7, 0, 160, "SAT425", "Satisfactory UE4.25 FName::GetNameEntryMemorySize"),
    SIG_RIP("GNAM_GH_2",  AOB_GNAMES_GH_2,   AobTarget::GNames, 12, 3, 7, 0, 170, "GH", "Ghidra FNameEntryId::FromValidEName cross-game"),
    SIG_RIP("GNAM_ES2_1", AOB_GNAMES_ES2_1,  AobTarget::GNames, 0, 3, 7, 0, 180, "ES2", "UE5.5 ResolveEntry"),
    SIG_RIP("GNAM_SAT425_3", AOB_GNAMES_SAT425_3, AobTarget::GNames, 0, 3, 7, 0, 190, "SAT425", "Satisfactory UE4.25 GetNumAnsiNames (general V8)"),
    SIG_RIP("GNAM_SF_1",  AOB_GNAMES_SF_1,   AobTarget::GNames, 0, 3, 7, 0, 200, "SF", "SatisfFactory NamePoolData init (in Core DLL)"),
    SIG_RIP("GNAM_CT1",   AOB_GNAMES_CT1,    AobTarget::GNames, 0, 3, 7, 0, 210, "CT", "UE4 Dumper.CT v6+ lea r8; jmp 16"),
    // 300: was GNAM_CT2 + GNAM_UD2. CT2 removed b2407 — identical hit set on all 26 programs.
    SIG_RIP("GNAM_UD2",   AOB_GNAMES_UD2,    AobTarget::GNames, 0, 3, 7, 0, 300, "UD", "UEDumper lea rcx; call; mov r8 (supersedes CT2)"),

    // 340–380: Tier 2 — medium patterns
    SIG_RIP("GNAM_SF_2",  AOB_GNAMES_SF_2,   AobTarget::GNames, 0, 3, 7, 0, 340, "SF", "SatisfFactory SHL pattern (in Core DLL)"),
    SIG_RIP("GNAM_SF_3",  AOB_GNAMES_SF_3,   AobTarget::GNames, 0, 3, 7, 0, 360, "SF", "SatisfFactory FNameEntryId (in Core DLL)"),
    SIG_RIP("GNAM_V6",    AOB_GNAMES_V6,     AobTarget::GNames, 0, 3, 7, 0, 380, "V", "GSpots UE5+ mov rax; test; jnz"),

    // 600–620: Patternsleuth
    SIG_RIP("GNAM_PS1",   AOB_GNAMES_PS1,    AobTarget::GNames, 2, 3, 7, 0, 600, "PS", "jz+9; lea r8"),
    SIG_RIP("GNAM_PS2",   AOB_GNAMES_PS2,    AobTarget::GNames, 7, 3, 7, 0, 620, "PS", "sub rsp; shr; lea rbp"),

    // 700–720: UE4 pre-FNamePool (TNameEntryArray / TStaticIndirectArrayThreadSafeRead).
    // A different structure entirely, and measurably MISSES on every FNamePool binary, so
    // sitting below the Tier-2 block costs nothing and saves wasted validations on a UE4 title.
    SIG_RIP("GNAM_CT3",   AOB_GNAMES_CT3,    AobTarget::GNames, 4, 3, 7, 0, 700, "CT", "UE4 <4.23 pre-FNamePool deref"),
    SIG_RIP("GNAM_G42_1", AOB_GNAMES_G42_1,  AobTarget::GNames, 0, 3, 7, 0, 710, "G42", "UE4.2 pre-FNamePool TStaticIndirectArray"),
    // 715: ahead of CT4 (720) so a UE 4.22 title lands on its purpose-built anchor rather than
    // on CT4's write-pattern, which only gets there after the validator rejects a decoy.
    SIG_RIP("GNAM_SAT422_1", AOB_GNAMES_SAT422_1, AobTarget::GNames, 0, 3, 7, 0, 715, "SAT422", "Satisfactory UE4.22 FName::GetNames + game-thread assert (PDB-corrected b2407)"),
    SIG_RIP("GNAM_XX_1",  AOB_GNAMES_XX_1,   AobTarget::GNames, 8, 3, 7, 0, 717, "XX", "pre-4.23 FName::GetNames write-back+epilogue — 8/8 pre-4.23 oracles at index 0"),
    SIG_RIP("GNAM_CT4",   AOB_GNAMES_CT4,    AobTarget::GNames, 3, 3, 7, 0, 720, "CT", "UE4 pre-FNamePool write pattern"),

    // 850–890: last resort — the short V-series, demoted here in build 2405.
    //
    // WHY THESE SURVIVED while the equivalent GWorld block (V2/V4/V5/V6) was deleted in 2409:
    // they are redundant, not WRONG. Over 31 programs / 10 GNames oracle groups:
    //     GNAM_V2  6 literal bytes  23,125 matches  reaches truth on 8 groups, decoy-only on 2
    //     GNAM_V5  7 literal bytes  22,839 matches  8 / 2
    //     GNAM_V3  4 literal bytes  15,000 matches  7 / 3
    //     GNAM_V4  4 literal bytes   5,890 matches  6 / 4
    //     GNAM_V1  4 literal bytes   1,182 matches  6 / 4
    // Every GWorld pattern deleted in 2409 scored **0** correct; these score 6–8 of 10. None is
    // ever the pattern the runtime lands on, and on every oracle where one is correct there are
    // 3–14 other correct patterns, so deleting them would not change a single result today —
    // but "correct yet redundant" is worth keeping as insurance for an engine build the corpus
    // does not cover, whereas "never correct" is not. The other half of the argument is the
    // validator: ValidateGNames reads the pool structure and is strong, while
    // ValidateGWorldBasic is deliberately loose and has been fooled in the field (Solarpunk).
    // At 850–890 they are only ever reached when everything above has failed; on all 10 oracles
    // GNames resolves by 715 at the latest, so they are never even scanned.
    SIG_RIP("GNAM_V5",    AOB_GNAMES_V5,     AobTarget::GNames, 0, 3, 7, 0, 850, "V", "lea rcx; call; mov byte[],1 extended"),
    SIG_RIP("GNAM_V2",    AOB_GNAMES_V2,     AobTarget::GNames, 0, 3, 7, 0, 860, "V", "lea rcx; call; mov byte ptr"),
    SIG_RIP("GNAM_V1",    AOB_GNAMES_V1,     AobTarget::GNames, 0, 3, 7, 0, 870, "V", "lea rsi; jmp"),
    SIG_RIP("GNAM_V3",    AOB_GNAMES_V3,     AobTarget::GNames, 0, 3, 7, 0, 880, "V", "lea rax; jmp"),
    SIG_RIP("GNAM_V4",    AOB_GNAMES_V4,     AobTarget::GNames, 0, 3, 7, 0, 890, "V", "lea r8; jmp"),
};

// ── GWorld ───────────────────────────────────────────────────────────────
constexpr AobSignature GWORLD_PATTERNS[] = {
    // 0: Symbol export (O(1))
    SIG_EXPORT("GWLD_EXP", EXPORT_GWORLD, AobTarget::GWorld, 0, "UWorldProxy symbol"),

    // 100–250: Tier 1 — long, specific, verified-unique.
    // ENTRIES ARE IN PRIORITY ORDER, and must stay that way. They previously were not: the SP57
    // block (100–160) was written as one run ahead of the ES2 block (110–250), so reading the
    // file gave a different order from the one ScanForTarget actually uses (it sorts by
    // priority). That is how a "why was this not re-prioritised?" question gets asked about a
    // pattern that WAS re-prioritised — the file was lying, not the code.
    SIG_GWORLD_RIP("GWLD_SP57_1", AOB_GWORLD_SP57_1, 0, 3, 7, 0, 100, false, "SP57", "UE5.7 UGameEngine::Tick cmp [rcx+2C0] (tolerates inserted mov)"),
    // PROMOTED 210 -> 101. This is the most successful GWorld pattern in the table: it WINS on
    // 6 of 16 oracles (no other GWorld pattern wins more than 2) and has **zero decoys anywhere**
    // in the corpus — 10 UNIQUE-OK, 6 NO-TRUTH on probes, 23 MISS. It was sitting behind 13 AOBs.
    //
    // The saving is a whole .text pass, not a few validations. Patterns are scanned in BATCHES OF
    // 8: order WITHIN a batch changes only validation order, but crossing a batch boundary costs
    // an entire extra AVX2 sweep. At 210 this sat in batch 2, so the six games it wins each paid
    // for batch 1 first. At 101 they resolve in batch 1 and never scan batch 2.
    //
    // What it displaces out of batch 1 is GWLD_ES2_3, which wins on nothing — so the swap is
    // free. Deliberately 101 and not 95: the 40-90 band means "symbol-derived", and being 1st vs
    // 2nd inside a batch is worth nothing anyway.
    SIG_GWORLD_RIP("GWLD_TQ_1",  AOB_GWORLD_TQ_1,  0, 3, 7, 0, 101, false, "TQ", "TQ2 extended V3 — corpus's most successful GWorld shape"),
    // PROMOTED 265 -> 102, after the corpus-wide measurement the previous note demanded.
    // The deciding number is ZERO: replaying ScanForTarget's priority order over all 51
    // measured binaries at 265 vs 102 changes ZERO landers, adds ZERO wasted validations,
    // and moves ZERO programs to a later batch — while moving SIX from batch 3 to batch 1
    // (4.11 Nekopara, 4.13 Fantasynth, 4.15.3 Flying, FF7R, 4.26-Sat Engine.dll,
    // 5.2-Sat Engine.dll).
    //
    // What it displaces is worth nothing: GWLD_SP57_3 falls to batch 2 but hits exactly 1
    // of 51 binaries (Solarpunk), where GWLD_SP57_1 @100 lands first in batch 1 under BOTH
    // layouts — so it is unreachable there either way; and GWLD_GH_2 falls to batch 3 with
    // 0 hits on 51 binaries (it is in REPORT.md's "never hits anything, anywhere" list).
    // The census also showed five of the eight batch-1 slots — DI427_1, DI427_2, SP57_2,
    // ES2_2, SP57_3 — win on ZERO binaries. Batch 1 was not scarce.
    //
    // The strongest corroboration is the 4.15.3 oracle: EVERY pattern at priority 100-260
    // MISSES on it, FD_1 is the FIRST HITTING pattern, and the one that would win in its
    // absence (GWLD_SF_2 @300) resolves a DECOY (FNiagaraDataSetID::DeathEvent+0xB0) —
    // the exact failure mode FD_1 was written to stop, now reproduced on a symbolised
    // binary in a band that previously had no symbols at all.
    //
    // SCOPE THE CLAIM HONESTLY: "changes no answer" is true ON THIS 51-BINARY CORPUS, not
    // unbounded. The move puts FD_1 ahead of 17 patterns, so on an unmeasured engine where
    // FD_1 and one of those both hit, FD_1 now decides. Residual risk is small (0 decoys
    // on 51 binaries, never more than 1 hit on any) but it is an inference, not a proof.
    SIG_GWORLD_RIP("GWLD_FD_1",  AOB_GWORLD_FD_1,   0, 3, 7, 0, 102, false, "FD", "UWorld::FinishDestroy read + conditional write-back (4.11-5.2, generalises G427_5)"),
    // 105/115: UE 4.27 (DropIn, PDB-verified). Both are WRITE sites -> allowNull.
    // NOTE DI427_2 has totalLen = 11: `mov qword[rip+d32], imm32` — the disp32 still starts
    // at byte 3 but the instruction carries a trailing imm32. Mis-encoding this as 7 is the
    // classic way a C7-form store pattern silently resolves to garbage.
    SIG_GWORLD_RIP("GWLD_DI427_1", AOB_GWORLD_DI427_1, 0, 3,  7, 0, 105, true, "DI427", "UE4.27 UEngine::LoadMap GWorld=NewWorld store"),
    SIG_GWORLD_RIP("GWLD_ES2_1", AOB_GWORLD_ES2_1, 0, 3, 7, 0, 110, false, "ES2", "UE5.5 26-byte lea+mov chain"),
    SIG_GWORLD_RIP("GWLD_DI427_2", AOB_GWORLD_DI427_2, 0, 3, 11, 0, 115, true, "DI427", "UE4.27 FSeamlessTravelHandler::Tick GWorld=nullptr (C7-imm store form)"),
    SIG_GWORLD_RIP("GWLD_SP57_2", AOB_GWORLD_SP57_2, 0, 3, 7, 0, 120, false, "SP57", "UE5.7 FMallocLeakReporter::WriteReports (mov rsi,rcx variant)"),
    SIG_GWORLD_RIP("GWLD_ES2_2", AOB_GWORLD_ES2_2, 0, 4, 8, 0, 130, false, "ES2", "UE5.5 CMOVZ r13"),
    SIG_GWORLD_RIP("GWLD_SP57_3", AOB_GWORLD_SP57_3, 0, 3, 7, 0, 140, false, "SP57", "UE5.7 UEngine::GetWorldFromContextObject fallback"),
    SIG_GWORLD_RIP("GWLD_ES2_3", AOB_GWORLD_ES2_3, 0, 3, 7, 0, 150, false, "ES2", "UE5.5 cmp [rcx+2C0]"),
    SIG_GWORLD_RIP("GWLD_SP57_4", AOB_GWORLD_SP57_4, 0, 3, 7, 0, 160, false, "SP57", "UE5.7 UActorComponent::On*PhysicsState mov [rax+298]"),
    SIG_GWORLD_RIP("GWLD_ES2_4", AOB_GWORLD_ES2_4, 0, 3, 7, 0, 170, false, "ES2", "UE5.5 cmp+and GWorld"),
    SIG_GWORLD_RIP("GWLD_ES2_5", AOB_GWORLD_ES2_5, 0, 3, 7, 0, 180, false, "ES2", "UE5.5 call r12 loop"),
    SIG_GWORLD_RIP("GWLD_ES2_6", AOB_GWORLD_ES2_6, 0, 3, 7, 0, 190, false, "ES2", "UE5.5 cmovne+call rbx"),
    SIG_GWORLD_RIP("GWLD_GH_1",  AOB_GWORLD_GH_1,  12, 3, 7, 0, 200, false, "GH", "Ghidra FMallocLeakReporter 25-fixed cross-game"),
    SIG_GWORLD_RIP("GWLD_TQ_2",  AOB_GWORLD_TQ_2,  0, 3, 7, 0, 220, false, "TQ", "TQ2 dual mov"),
    SIG_GWORLD_RIP("GWLD_GH_2",  AOB_GWORLD_GH_2,   9, 3, 7, 0, 230, false, "GH", "Ghidra FUMGViewportClient::GetWorld cross-game"),
    SIG_GWORLD_RIP("GWLD_V7",    AOB_GWORLD_V7,     0, 3, 7, 0, 240, false, "V", "Palworld long context"),
    SIG_GWORLD_RIP("GWLD_V1",    AOB_GWORLD_V1,     0, 3, 7, 0, 250, false, "V", "cmp/cmovz"),

    // 260–320: SatisfFactory DLL patterns + Ghidra cross-game
    SIG_GWORLD_RIP("GWLD_GH_3",  AOB_GWORLD_GH_3,  12, 3, 7, 0, 260, false, "GH", "Ghidra GetWorldFromContextObject cross-game"),
    SIG_GWORLD_RIP("GWLD_SF_1",  AOB_GWORLD_SF_1,   0, 3, 7, 0, 270, false, "SF", "Engine DLL UGameEngine::Tick"),
    SIG_GWORLD_RIP("GWLD_SF_2",  AOB_GWORLD_SF_2,   0, 3, 7, 0, 300, false, "SF", "Engine DLL FAudioDeviceManager"),
    SIG_GWORLD_RIP("GWLD_SF_3",  AOB_GWORLD_SF_3,   0, 3, 7, 0, 305, false, "SF", "Engine DLL UWorld::FinishDestroy"),
    // GWLD_G427_2 sits at 308 — one slot AHEAD of GWLD_SF_4 — and is deliberately out of its
    // numeric "UE 4.27" band (was 375, moved build 3262, AD11/AD14). Wildcarding SF_4's frame
    // displacement in build 2437 made it a strict PREFIX of G427_2 (SF_4's 14 tokens are
    // G427_2's first 14) with the identical (0,3,7,0) triple, so from 2437 on G427_2 could never
    // be reached — SF_4 matches everywhere it does, at the same addresses, resolving the same
    // way. The bands are ordered by SPECIFICITY, not by source tag, and G427_2 is SF_4 plus two
    // more literal bytes (`C7 48`, the `mov rax,rdi` that pins UEngine::GetWorldFromContextObject
    // rather than any `mov rdi,[rip]; mov rbx,[rsp+?]; mov r??` epilogue), so 308 is the
    // band-consistent slot. SF_4's own comment already flags that this wildcarding made it a
    // near-superset of GWLD_G42_4; this is the case it missed.
    SIG_GWORLD_RIP("GWLD_G427_2", AOB_GWORLD_G427_2, 0, 3, 7, 0, 308, false, "G427", "UE4.27 GetWorldFromContextObject (specific superset of SF_4)"),
    SIG_GWORLD_RIP("GWLD_SF_4",  AOB_GWORLD_SF_4,   0, 3, 7, 0, 310, false, "SF", "Engine DLL GetWorldFromContextObject"),
    SIG_GWORLD_RIP("GWLD_SF_5",  AOB_GWORLD_SF_5,   0, 3, 7, 0, 315, false, "SF", "Engine DLL FMallocLeakReporter"),
    SIG_GWORLD_RIP("GWLD_GH_4",  AOB_GWORLD_GH_4,   8, 3, 7, 0, 320, false, "GH", "Ghidra FEngineLoop::Tick XORPS cross-game"),

    // 325–365: UE 4.2 / Satisfactory read patterns
    SIG_GWORLD_RIP("GWLD_G42_3", AOB_GWORLD_G42_3,  9, 3, 7, 0, 325, false, "G42", "UE4.2 fallback return pattern"),
    SIG_GWORLD_RIP("GWLD_G42_2", AOB_GWORLD_G42_2,  0, 3, 7, 0, 330, false, "G42", "UE4.2 test+jz+mov r8b"),
    SIG_GWORLD_RIP("GWLD_G42_5", AOB_GWORLD_G42_5,  0, 3, 7, 0, 335, false, "G42", "UE4.2 mov+mov rbx+lea"),
    SIG_GWORLD_RIP("GWLD_G42_4", AOB_GWORLD_G42_4,  0, 3, 7, 0, 345, false, "G42", "UE4.2 mov rdi+mov rbx"),
    SIG_GWORLD_RIP("GWLD_SAT422_1", AOB_GWORLD_SAT422_1, 0, 3, 7, 0, 350, false, "SAT422", "Satisfactory UE4.22 FMallocLeakReporter"),
    SIG_GWORLD_RIP("GWLD_SAT425_1", AOB_GWORLD_SAT425_1, 0, 3, 7, 0, 355, false, "SAT425", "Satisfactory UE4.25 UGameEngine::Tick CMP"),
    SIG_GWORLD_RIP("GWLD_SAT426_1", AOB_GWORLD_SAT426_1, 0, 3, 7, 0, 360, false, "SAT426", "Satisfactory UE4.26 UGameEngine::Tick cmp+jz"),
    SIG_GWORLD_RIP("GWLD_SAT52_1",  AOB_GWORLD_SAT52_1,  0, 3, 7, 0, 365, false, "SAT52", "Satisfactory UE5.2 FAudioDeviceManager"),

    // 370–390: UE 4.27 patterns
    SIG_GWORLD_RIP("GWLD_G427_1", AOB_GWORLD_G427_1, 0, 3, 7, 0, 370, false, "G427", "UE4.27 FEngineLoop::Tick extended"),
    SIG_GWORLD_RIP("GWLD_G427_3", AOB_GWORLD_G427_3, 10, 3, 7, 0, 380, false, "G427", "UE4.27 UGameEngine::Tick (49 prefix)"),
    SIG_GWORLD_RIP("GWLD_G427_4", AOB_GWORLD_G427_4, 10, 3, 7, 0, 385, false, "G427", "UE4.27 UGameEngine::Tick (48 prefix)"),
    SIG_GWORLD_RIP("GWLD_G427_5", AOB_GWORLD_G427_5, 0, 3, 7, 0, 390, false, "G427", "UE4.27 UWorld::FinishDestroy cmp rbx"),

    // 395–400: Wildcard-prefixed TQ2 patterns
    SIG_GWORLD_RIP("GWLD_TQ_3",  AOB_GWORLD_TQ_3,   0, 3, 7, 0, 395, false, "TQ", "TQ2 ??-prefix mov rax"),
    // gworldAllowNull stays TRUE and is NOT part of the AD16 fix: TQ_4 is a `mov [GWorld],rcx`
    // WRITE site, so at the moment it matches the slot legitimately still reads 0 — the same
    // reason every entry in the 405–435 write band carries it. What made the resolved address
    // arbitrary was the instrOffset, now 0; with the geometry right, allowNull admits the real
    // &GWorld slot rather than whatever byte 6 happened to point at.
    { "GWLD_TQ_4", AOB_GWORLD_TQ_4, AobTarget::GWorld, AobResolve::RipBoth,
      0, 3, 7, 0, 400, 0, true, "TQ", "TQ2 ??-prefix write pattern" },

    // 405–420: Write patterns (Satisfactory UE 4.25, ES2 UE 5.3)
    { "GWLD_SAT425_3", AOB_GWORLD_SAT425_3, AobTarget::GWorld, AobResolve::RipBoth,
      0, 3, 7, 0, 405, 0, true, "SAT425", "Satisfactory UE4.25 write + R15 context" },
    { "GWLD_SAT425_2", AOB_GWORLD_SAT425_2, AobTarget::GWorld, AobResolve::RipBoth,
      0, 3, 7, 0, 410, 0, true, "SAT425", "Satisfactory UE4.25 write + TLS" },
    { "GWLD_ES53_1", AOB_GWORLD_ES53_1, AobTarget::GWorld, AobResolve::RipBoth,
      0, 3, 7, 0, 415, 0, true, "ES53", "ES2 UE5.3 UGameEngine::Tick MOVAPS write" },
    { "GWLD_ES53_2", AOB_GWORLD_ES53_2, AobTarget::GWorld, AobResolve::RipBoth,
      0, 3, 7, 0, 420, 0, true, "ES53", "ES2 UE5.3 UGameEngine::Tick RCX write" },

    // 425–435: Satisfactory write patterns
    { "GWLD_SAT422_2", AOB_GWORLD_SAT422_2, AobTarget::GWorld, AobResolve::RipBoth,
      0, 3, 7, 0, 425, 0, true, "SAT422", "Satisfactory UE4.22 SetGlobalWorld RCX write" },
    { "GWLD_SAT426_2", AOB_GWORLD_SAT426_2, AobTarget::GWorld, AobResolve::RipBoth,
      0, 3, 7, 0, 430, 0, true, "SAT426", "Satisfactory UE4.26 FinishDestroy RAX write" },
    { "GWLD_SAT52_2", AOB_GWORLD_SAT52_2, AobTarget::GWorld, AobResolve::RipBoth,
      0, 3, 7, 0, 435, 0, true, "SAT52", "Satisfactory UE5.2 UGameEngine::Tick RCX write" },

    // 880–900: last resort. GWLD_G42_1 (7 literal bytes) moved 340 -> 880 in build 2407 so it no
    // longer outranks the 10–14-byte SAT422/SAT425/SAT426/G427 block it used to precede.
    // GWLD_V3 is 6 literal bytes (`mov rbx,[rip+d32]; test rbx,rbx`) and the single noisiest
    // pattern in the file — 22,581 matches over 31 programs, 95.7 per MB of .text on a
    // monolithic game EXE, 2,658 on FF7 Remake alone — but it DOES reach the true GWorld on 6 of
    // the 9 oracle groups, so it stays as the last-resort read form.
    SIG_GWORLD_RIP("GWLD_G42_1", AOB_GWORLD_G42_1,  0, 3, 7, 0, 880, false, "G42", "UE4.2 mov+mov rsi+call"),
    SIG_GWORLD_RIP("GWLD_V3",    AOB_GWORLD_V3,     0, 3, 7, 0, 900, false, "V", "mov rbx test rbx"),
};

// ── REMOVED in build 2409: GWLD_V2 / V4 / V5 / V6 ────────────────────────
// All four were 4–7 literal bytes and, measured across 31 programs (9 groups with GWorld
// ground truth), reached the true GWorld on **ZERO** of them while firing constantly:
//     GWLD_V4  "48 8B 3D ?? ?? ?? ?? 48 85 FF"        5,809 matches, 0 correct
//     GWLD_V6  "48 89 1D ?? ?? ?? ?? E8"              2,403 matches, 0 correct   (write)
//     GWLD_V2  "48 89 05 ?? ?? ?? ?? 48 85 C0 74"     1,301 matches, 0 correct   (write)
//     GWLD_V5  "48 39 05 ?? ?? ?? ?? 74"                929 matches, 0 correct
// Contrast GWLD_V3, kept above: same family, same length class, but 6 of 9 correct.
//
// Every shape is already covered by a longer sibling that DOES work — the `mov rdi,[GWorld]`
// read by SP57_3 / G427_2 / SF_4, the rax-write by SAT426_2 / ES53_1 / SAT425_3, the rbx-write
// by SF_3 — so removing them loses no mechanism, only the degenerate context-free form.
//
// The deciding argument is specific to GWorld: **a wrong GWorld is worse than no GWorld.**
// ValidateGWorldBasic is deliberately loose, and when it is fooled the damage is silent — that
// is exactly what happened on Solarpunk, where GWLD_SF_2 matched a decoy .data global, passed
// validation, and produced a wrong world. When nothing resolves, Genau instead falls back to
// instance-scan recovery, which found the RIGHT world on that same title. A pattern that has
// never once been correct is therefore pure downside on this target, however low its priority.
// (For GNames the calculus differs and GNAM_V1/V3/V4 were kept — see the note on that table.)
// To restore: re-add with the byte strings above at priorities 920/960/940/980.

// ── SparseDelegates (FSparseDelegateStorage::SparseDelegates) ────────────
// Lazily resolved on first MulticastSparseDelegateProperty drill-down — NOT
// part of the FindAll boot sequence. Resolves directly to the TMap value.
constexpr AobSignature SPARSE_PATTERNS[] = {
    // Solarpunk UE 5.7 — SPARSE_ES2_1 gets 0 hits on this build; these anchor on the
    // TSet element-index math instead. RipDirect -> TSet object base. Verified.
    SIG_RIP_DIRECT("SPARSE_SP57_1", AOB_SPARSE_SP57_1, AobTarget::SparseDelegates,
                   0, 3, 7, 0, 100,
                   "SP57", "UE5.7 TSet::Find/FindOrAdd/Emplace element index (mov rdx)"),
    SIG_RIP_DIRECT("SPARSE_DI427_1", AOB_SPARSE_DI427_1, AobTarget::SparseDelegates,
                   14, 3, 7, 0, 110,
                   "DI427", "UE4.27 EnterCriticalSection + TSet::FindId head (no stride math)"),
    SIG_RIP_DIRECT("SPARSE_SP57_2", AOB_SPARSE_SP57_2, AobTarget::SparseDelegates,
                   0, 3, 7, 0, 120,
                   "SP57", "UE5.7 TSet::Remove element index (mov r8, unique)"),
    SIG_RIP_DIRECT("SPARSE_DI427_2", AOB_SPARSE_DI427_2, AobTarget::SparseDelegates,
                   21, 3, 7, 0, 130,
                   "DI427", "UE4.27 element addr + inner-TMap fetch tail (tail is load-bearing)"),
    SIG_RIP_DIRECT("SPARSE_ES2_1", AOB_SPARSE_ES2_1, AobTarget::SparseDelegates,
                   16, 3, 7, 0, 140,
                   "ES2", "UE4.27 (DropIn, PDB-verified) + ES2 5.4 + TQ2 5.7 NotifyUObjectDeleted twin-ref"),
    // 150: placed LAST deliberately. SPARSE_ES2_1 already resolves Palworld, so this is the
    // second anchor for when that site changes — not a replacement. Ordering it after ES2_1
    // means adding it cannot perturb any existing selection, which the re-sweep confirmed.
    SIG_RIP_DIRECT("SPARSE_PAL51_1", AOB_SPARSE_PAL51_1, AobTarget::SparseDelegates,
                   8, 3, 7, 0, 150,
                   "PAL51", "UE5.1 element addr (add r,[Sparse]) + TSet Num/Max compare"),
    // 160: last, for the same reason as PAL51_1 — SPARSE_ES2_1 already resolves both binaries
    // this hits, so ordering it behind everything guarantees it cannot perturb a selection.
    // instrOffset = 8: the pattern opens with `lea rdx,[rsp+d32]` (8 bytes), so the RIP-relative
    // `lea rcx,[SparseDelegates]` starts at byte 8, NOT byte 0. Restoring that leading
    // instruction without moving instrOffset silently resolved off the wrong instruction and
    // dropped the pattern to 0 correct — caught by the verification sweep, which is exactly the
    // failure mode instrOffset mistakes always take.
    SIG_RIP_DIRECT("SPARSE_MEL55_1", AOB_SPARSE_MEL55_1, AobTarget::SparseDelegates,
                   8, 3, 7, 0, 160,
                   "MEL55", "UE5.5/5.6 twin-ref lea+add of SparseDelegates around the 0x60 stride math"),
    // 170: last, same reasoning as PAL51_1/MEL55_1 — it hits ONLY Avowed (0 hits on the other
    // 41 programs), so ordering it behind everything guarantees it cannot perturb a selection.
    // This is the 8th entry, which keeps SPARSE_PATTERNS at exactly one batch (kBatchSize = 8).
    // Do not append a 9th without measuring the extra .text pass it imposes on every title that
    // finds nothing in batch 1.
    SIG_RIP_DIRECT("SPARSE_AV53_1", AOB_SPARSE_AV53_1, AobTarget::SparseDelegates,
                   0, 3, 7, 0, 170,
                   "AV53", "UE5.3 (Avowed fork) element addr + pointer-key compare; stock 0x60 stride"),
    // 180/190 — the pair that closes the n=1 cluster. They take this table from 8 to 10 entries,
    // i.e. from ONE batch to TWO (kBatchSize = 8), so the second batch is paid for by the FIRST
    // of them and the marginal cost of the second is exactly zero. That cost lands only on
    // titles where all 8 batch-1 patterns miss — and it is paid precisely when it is needed: if
    // the SPARSE_ES2_1 site changes in a patch, batch 1 comes back empty and batch 2 catches it.
    // Ordered last so they cannot perturb any existing selection.
    SIG_RIP_DIRECT("SPARSE_X1", AOB_SPARSE_X1, AobTarget::SparseDelegates,
                   0, 3, 7, 0, 180,
                   "X+GH51", "Remove/RemoveAll/Clear empty-map epilogue (unregister delete listener)"),
    SIG_RIP_DIRECT("SPARSE_X2", AOB_SPARSE_X2, AobTarget::SparseDelegates,
                   11, 3, 7, 0, 190,
                   "X+GH51", "inner-TMap emptiness test guarding the same call; reaches ES2 5.5"),
};

// ── GEngine (UEngine* GEngine — the &GEngine SLOT) ───────────────────────
// Resolved AFTER GObjects/GNames/offsets in FindAll, because the validator has to
// deref the slot and ask the reflected class for a "GameViewport" property.
// X1/X2 are cross-version (verified on UE 4.27 + UE 5.7; X1 also matches UE 5.3).
// Ordering is empirical, from a SIX-binary sweep with real symbols on five of them:
// Everspace 4.20, DropIn 4.27, ES2 5.5, Satisfactory 5.6, Solarpunk 5.7 (+ Avowed 5.3,
// symbol-less, so it can only ever say "no hits" — never "wrong hit").
//
// X1 is the broadest single pattern in the file: UWorld::GetGameViewport is a tiny stable
// accessor that survives UE 4.20 -> 5.7 with only its stack size changing (hence `2?`).
//
// HISTORY worth keeping: X1 and DI427_1 were briefly demoted here on the strength of an
// apparent decoy count on Everspace 4.20. That was a measurement artifact — the sweep had
// been given a PLACEHOLDER truth value for that binary, so every hit necessarily compared
// unequal and got labelled a decoy. With Everspace's real PDB both are UNIQUE-OK on 4.20
// (X1 1/1, DI427_1 5/5). tools/ghidra/scan_patterns.java now emits NO-TRUTH instead of
// DECOY-ONLY when it has no plausible truth, so the same mistake cannot be made silently.
constexpr AobSignature GENGINE_PATTERNS[] = {
    // 0: Symbol export (O(1)) — modular builds export &GEngine from the Engine module.
    SIG_EXPORT("GENG_EXP", EXPORT_GENGINE, AobTarget::GEngine, 0, "MSVC mangled symbol"),

    SIG_RIP_DIRECT("GENG_X1", AOB_GENGINE_X1, AobTarget::GEngine,
                   7, 3, 7, 0, 100, "DI427+SP57", "UWorld::GetGameViewport — UE4.20+4.27+5.7, decoy-free"),
    SIG_RIP_DIRECT("GENG_X3", AOB_GENGINE_X3, AobTarget::GEngine,
                   7, 3, 7, 0, 105, "X+FF7R", "X1 head only (no test/jz tail) — reaches FF7R UE4.18"),
    SIG_RIP_DIRECT("GENG_X2", AOB_GENGINE_X2, AobTarget::GEngine,
                   0, 3, 7, 0, 110, "DI427+SP57", "FEngineLoop::Tick (UE4.27+5.7, 6-7 sites)"),
    // GENG_X4 is the WEAKEST pattern in this table and the note used to overstate it. Its shape
    // is a generic singleton-null-check-member idiom, so on three games added to the corpus in
    // 2026-07 it converged on a GAME-SIDE manager singleton rather than &GEngine: 50 decoys of
    // 55 hits on DQ7R (4.27), 6 on DQ XI S (4.18), 3 on Elliot (5.4). It stays because it is
    // still what reaches FF7 Remake, and ValidateGEngineSlot rejects its decoys (no reflected
    // "GameViewport"), so it costs validations rather than correctness.
    //
    // It is also the counter-example to GROUND-TRUTH.md rule 4 ("convergent hits = a real
    // global"). X4's hits DO converge — on one wrong address, and by a wide margin. The rule
    // only holds within one pattern; across patterns the discriminator is whether the
    // semantically-specific shapes agree with it. On DQ7R they did not: X1/X2/X3/DI427_1 all
    // pointed at 145FF4B28 (proven by UWorld::GetGameViewport / GetRealTimeSeconds / a GetWorld
    // fallback that loads GEngine and GWorld in one function) while X4 alone pointed elsewhere.
    // Rank candidates by DISTINCT PATTERNS AGREEING, never by raw hit count.
    SIG_RIP_DIRECT("GENG_X4", AOB_GENGINE_X4, AobTarget::GEngine,
                   0, 3, 7, 0, 115, "PAL51+X", "generic singleton accessor — broad reach, noisiest; decoys are game singletons"),
    SIG_RIP_DIRECT("GENG_ES55_1", AOB_GENGINE_ES55_1, AobTarget::GEngine,
                   10, 3, 7, 0, 120, "ES55", "UE5.5+5.7 UEngine::GetEngineSubsystem<T> prologue"),
    SIG_RIP_DIRECT("GENG_SP57_1", AOB_GENGINE_SP57_1, AobTarget::GEngine,
                   10, 3, 7, 0, 130, "SP57", "UE5.5+5.7 UEngine::IsStereoscopic3D"),
    SIG_RIP_DIRECT("GENG_DI427_1", AOB_GENGINE_DI427_1, AobTarget::GEngine,
                   13, 3, 7, 0, 140, "DI427", "UE4.20+4.27 GetRealTimeSeconds shape (5-6 sites)"),
};

#undef SIG_RIP
#undef SIG_RIP_DIRECT
#undef SIG_EXPORT
#undef SIG_SYM_CALL
#undef SIG_GWORLD_RIP


// ============================================================
// Compile-time table invariants
// ============================================================
// ScanForTarget sorts by priority at run time, so an out-of-order table is not a BUG — it is
// worse than that in practice: it makes the file misreport itself. Entries drift under stale
// band headers, and someone reading `// 500–590: Tier 3` above a pattern that is actually at
// 870 reasonably concludes it was never re-prioritised. That exact confusion is what prompted
// this guard, so the invariant is now enforced by the compiler instead of by discipline.
//
// Duplicate priorities are also rejected: two patterns on the same number have an order that
// depends on the sort's stability, which makes a regression sweep unreproducible.
template <size_t N>
constexpr bool IsSortedByPriority(const AobSignature (&arr)[N]) {
    for (size_t i = 1; i < N; ++i)
        if (arr[i - 1].priority > arr[i].priority) return false;
    return true;
}
template <size_t N>
constexpr bool HasUniquePriorities(const AobSignature (&arr)[N]) {
    for (size_t i = 1; i < N; ++i)
        if (arr[i - 1].priority == arr[i].priority) return false;   // relies on sortedness
    return true;
}

#define ASSERT_TABLE_ORDER(tbl)                                                      \
    static_assert(IsSortedByPriority(tbl), #tbl " must be listed in priority order"); \
    static_assert(HasUniquePriorities(tbl), #tbl " has two entries on the same priority")

// These fire at COMPILE TIME, so a mis-ordered or duplicate priority cannot reach a build.
// Worth knowing why they exist: the file HAD drifted out of order before they were added —
// GNAM_V5 (850) sat inside the Tier-1 block, GOBJ_PS7 (970) under a "600-690" header, and
// GWLD_G42_1 (880) inside the 325-365 run. None of that changed behaviour, because
// ScanForTarget sorts a copy at scan time; what it did was make the file MISREPORT ITSELF to a
// reader, who would then reason about scan order from the listing and be wrong.
ASSERT_TABLE_ORDER(GOBJECTS_PATTERNS);
ASSERT_TABLE_ORDER(GNAMES_PATTERNS);
ASSERT_TABLE_ORDER(GWORLD_PATTERNS);
ASSERT_TABLE_ORDER(SPARSE_PATTERNS);
ASSERT_TABLE_ORDER(GENGINE_PATTERNS);

#undef ASSERT_TABLE_ORDER

// Every RIP entry's (instrOffset, opcodeLen, totalLen) must line up with its own pattern
// bytes — see the RipGeometryOk block near AobSignature for why this is not obvious and what
// each rule is for. The assert can only name the TABLE; `FirstBadRipGeometry` returns the
// offending INDEX, and `extract_patterns.py --check` prints the id and the reason.
#define ASSERT_RIP_GEOMETRY(tbl)                                                            \
    static_assert(FirstBadRipGeometry(tbl) < 0,                                             \
                  #tbl ": an entry's (instrOffset, opcodeLen, totalLen) does not line up "  \
                       "with its own pattern bytes. Run "                                   \
                       "`py tools/ghidra/extract_patterns.py dll/src/Himmel.h "             \
                       "out/patterns.tsv --check` for the entry id and the reason.")

ASSERT_RIP_GEOMETRY(GOBJECTS_PATTERNS);
ASSERT_RIP_GEOMETRY(GNAMES_PATTERNS);
ASSERT_RIP_GEOMETRY(GWORLD_PATTERNS);
ASSERT_RIP_GEOMETRY(SPARSE_PATTERNS);
ASSERT_RIP_GEOMETRY(GENGINE_PATTERNS);

#undef ASSERT_RIP_GEOMETRY

// NOTE: the per-target pattern counts live in the FILE HEADER and nowhere else. A second copy
// used to sit here; keeping both is what let the header go four patterns stale. One copy only.

} // namespace Sig
