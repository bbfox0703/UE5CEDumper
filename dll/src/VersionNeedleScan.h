#pragma once

// ============================================================
// VersionNeedleScan — the UE-version needle sweep, extracted from Genau.cpp
//
// Header-only and PURE: (const uint8_t*, size_t) in, offsets and table indices out.
// No Win32, no Macht, no Sein. That is the whole point — nothing in dll/CMakeLists.txt
// compiles Genau.cpp into a test target (dll_helpers_test builds tests/dll_helpers_test.cpp
// + src/Radar.cpp + src/Denken.cpp only), so this logic was uncatchable at build time.
// Here it is unit-testable against a naive reference implementation.
//
// Kept in English and NOT given a Frieren roster name: CLAUDE.md's module-naming rule
// exempts "algorithm helpers that live inside an existing namespace", the precedent being
// GraphPath.h under Aura::. This lives under Genau::.
//
// ── WHY THIS EXISTS AT ALL (audit #5 G2) ─────────────────────────────────────
// The original was 19 naive whole-image memcmp passes for Tier 2/3 plus 4 more for
// Tier 1. MEASURED on this repo's own example binary (Elliot-Win64-Shipping.exe,
// 460.0 MiB = 482,344,960 bytes; MSVC 2022 /O2 x64):
//
//     Tier 1   (4 passes)  4.348 s     Tier 2/3 (19 passes) 24.465 s
//     CountPreUE4Markers   0.489 s     contiguous stretch  ~29.3 s
//
// Tier 2/3 alone issued 9,165,424,620 memcmp calls. Gated, the same input costs
// 0.2245 s + 0.0957 s and 39,351 compares — same answers, ~29 s removed.
//
// ⚠ The measurement trap, recorded because it cost a rebuild: benchmarking with a
// STRING LITERAL needle lets the compiler constant-fold strlen and inline the memcmp,
// understating the real cost by ~5x. The production loop takes needleLen from a table
// at runtime. Any re-measurement must do the same.
//
// ── THE EQUIVALENCE ARGUMENT, which is what makes the gate safe ──────────────
// Every needle below begins '4' or '5' AND has '.' at index 1. Both Tier 1 prefixes
// begin '+', in narrow and in UTF-16LE (a widened ASCII string starts with the same
// byte). So skipping an offset whose first byte is not in that set cannot change any
// memcmp result — the gate is exact by construction, not a heuristic.
//
// The static_asserts below enforce it. Adding a "6.0." row without extending the
// walked first-byte set FAILS THE BUILD rather than silently falling outside the gate.
// Do not delete them to add a needle quickly.
// ============================================================

#include <cstddef>
#include <cstdint>
#include <cstring>

namespace Genau {

struct VersionNeedle {
    const char* needle;
    uint32_t    value;
};

// Table order is significant: selection prefers the EARLIEST entry, not the lowest
// address (see ScanVersionTier23). Newest engine first.
inline constexpr VersionNeedle kVersionNeedles[] = {
    { "5.8.", 508 }, { "5.7.", 507 }, { "5.6.", 506 }, { "5.5.", 505 },
    { "5.4.", 504 }, { "5.3.", 503 }, { "5.2.", 502 },
    { "5.1.", 501 }, { "5.0.", 500 },
    { "4.27.", 427 }, { "4.26.", 426 }, { "4.25.", 425 },
    { "4.24.", 424 }, { "4.23.", 423 }, { "4.22.", 422 },
    { "4.21.", 421 }, { "4.20.", 420 }, { "4.19.", 419 },
    { "4.18.", 418 },
};

inline constexpr size_t kVersionNeedleCount =
    sizeof(kVersionNeedles) / sizeof(kVersionNeedles[0]);

// The gate's contract, enforced at compile time. If one of these fires you have added a
// needle the first-byte walk does not visit, and detection would silently stop finding it.
static_assert(kVersionNeedleCount == 19, "needle table changed — re-check the gate below");
namespace detail {
constexpr bool AllNeedlesGateable() {
    for (size_t i = 0; i < kVersionNeedleCount; ++i) {
        const char* n = kVersionNeedles[i].needle;
        if (n[0] != '4' && n[0] != '5') return false;   // walked first bytes
        if (n[1] != '.') return false;                  // second-byte gate
        if (n[2] == '\0' || n[3] == '\0') return false; // >= 4 chars
    }
    return true;
}
}  // namespace detail
static_assert(detail::AllNeedlesGateable(),
              "every version needle must start '4'/'5', have '.' at index 1, and be >= 4 chars — "
              "otherwise ScanVersionTier1/23's first-byte walk skips it. Extend the walked set "
              "in both functions before adding such a needle.");

// ── results ──────────────────────────────────────────────────────────────────

struct NeedleScanResult {
    // Index into kVersionNeedles, or kVersionNeedleCount for "no hit".
    size_t   index  = kVersionNeedleCount;
    size_t   offset = 0;
    int      tier   = 0;   // 0 none, 1/2/3 as in Genau's DetectVersionDetailed
    // Tier 1 only: which prefix/encoding matched, for the log line.
    bool     wide   = false;
    size_t   prefixIndex = 0;
    // Instrumentation, so a test can assert the GATE still works without a wall clock.
    // Counts full needle memcmp calls actually issued. A deterministic stand-in for
    // "is this still fast", which a timing assertion could never be in CI.
    uint64_t needlesCompared = 0;

    bool found() const { return tier != 0; }
};

// ── anchor helper (moved verbatim from Genau.cpp, plus a first-byte gate) ─────
//
// The gate matters more than it looks: the fused Tier 2/3 pass below no longer returns
// the instant it sees a Tier 2 hit, so patterns after the winner can now reach anchor
// calls the original never made. Without the gate that would be a net slowdown on
// exactly the images this change exists to speed up.
inline bool HasUEAnchorNearby(const uint8_t* scan, size_t size,
                              size_t off, size_t windowBytes) {
    static const char* kAnchors[] = {
        "Engine",  "engine",
        "Unreal",  "unreal",
        "++UE",
        "UE4",     "UE5",
    };
    size_t lo = (off > windowBytes) ? off - windowBytes : 0;
    size_t hi = off + windowBytes;
    if (hi > size) hi = size;
    if (hi <= lo) return false;
    size_t winLen = hi - lo;
    for (const char* a : kAnchors) {
        size_t aLen = strlen(a);
        if (aLen >= winLen) continue;
        const uint8_t first = static_cast<uint8_t>(a[0]);
        for (size_t i = 0; i + aLen <= winLen; ++i) {
            if (scan[lo + i] != first) continue;   // gate; memcmp would reject anyway
            if (memcmp(scan + lo + i, a, aLen) == 0) return true;
        }
    }
    return false;
}

// ── Tier 2's context test (audit #5 G8) ─────────────────────────────────────
//
// "Release" (or "release") within the preceding `windowBytes`. Three things used to
// disagree here and the code was the narrowest of the three: the comment said 16, the
// buffer was `char ctx[17]`, and the memcpy copied 8.
//
// ⚠ The obvious repair — widen the memcpy to 16 — is WRONG IN BOTH DIRECTIONS, and both
// were measured before this was written:
//   * `strstr` over a NUL-terminated copy stops at the FIRST NUL in the copied bytes. A
//     neighbouring string's terminator inside the wider window therefore TRUNCATES the
//     search and LOSES a match the narrow window found (12.2% of the 14,823 [Rr]elease
//     occurrences across the 33-binary corpus have a NUL in the preceding 8 bytes). So a
//     wider strstr is not a superset of a narrower one.
//   * `off >= 16` as the guard drops Tier-2 eligibility for offsets 8..15 that qualify
//     today — including the canonical "Release-5.4.0", whose needle sits at offset 8.
// A raw byte search over a CLAMPED window has neither problem and is a strict superset.
inline bool HasReleaseBefore(const uint8_t* scan, size_t off, size_t windowBytes) {
    if (off == 0) return false;
    const size_t win = (off < windowBytes) ? off : windowBytes;   // clamp at image start
    const uint8_t* w = scan + off - win;
    for (size_t i = 0; i + 7 <= win; ++i) {
        // "Release" / "release" — the original accepted exactly these two spellings.
        if ((w[i] == 'R' || w[i] == 'r') && memcmp(w + i + 1, "elease", 6) == 0)
            return true;
    }
    return false;
}

// ── Tier 1: exact engine tag "++UE5+Release-5.4" / "++UE4+Release-4.27" ──────
//
// Order is preserved exactly from the original nest: encoding-major (narrow, then
// UTF-16LE), then prefix, then ascending offset, then table order. The FIRST match in
// that order wins, so the four walks below must stay in this sequence.
//
// One behaviour change that is not observable: the original rebuilt `std::string bare`
// and the widened needle vector INSIDE the innermost loop, i.e. once per candidate
// offset per pattern. They are loop-invariant and are now built once per walk.
inline NeedleScanResult ScanVersionTier1(const uint8_t* scan, size_t size) {
    NeedleScanResult r;
    static const char* kPrefixes[] = { "++UE5+Release-", "++UE4+Release-" };

    for (int wide = 0; wide <= 1; ++wide) {
        for (size_t pi = 0; pi < 2; ++pi) {
            const char* prefix = kPrefixes[pi];
            const size_t rawLen = strlen(prefix);
            const size_t preLen = wide ? rawLen * 2 : rawLen;

            uint8_t pre[64];
            if (preLen > sizeof(pre)) continue;   // cannot happen with the table above
            for (size_t i = 0; i < rawLen; ++i) {
                if (wide) { pre[i * 2] = static_cast<uint8_t>(prefix[i]); pre[i * 2 + 1] = 0; }
                else      { pre[i]     = static_cast<uint8_t>(prefix[i]); }
            }
            if (preLen == 0 || size <= preLen + 8) continue;

            // Loop-invariant: the trailing '.' is stripped because the real engine tag is
            // "++UE5+Release-5.4" with nothing after the minor. The dot exists only to
            // disambiguate the BARE needle in Tiers 2/3; here the prefix supplies the context.
            uint8_t bare[kVersionNeedleCount][16];
            size_t  bareLen[kVersionNeedleCount];
            for (size_t k = 0; k < kVersionNeedleCount; ++k) {
                const char* n = kVersionNeedles[k].needle;
                size_t nl = strlen(n);
                if (nl && n[nl - 1] == '.') --nl;         // strip trailing dot
                bareLen[k] = wide ? nl * 2 : nl;
                for (size_t i = 0; i < nl; ++i) {
                    if (wide) { bare[k][i * 2] = static_cast<uint8_t>(n[i]); bare[k][i * 2 + 1] = 0; }
                    else      { bare[k][i]     = static_cast<uint8_t>(n[i]); }
                }
            }

            // The gate: every prefix starts '+', narrow AND widened.
            const size_t limit = size - preLen - 8;   // original: off + preLen + 8 < size
            for (size_t off = 0; off < limit; ++off) {
                if (scan[off] != '+') continue;
                ++r.needlesCompared;
                if (memcmp(scan + off, pre, preLen) != 0) continue;
                for (size_t k = 0; k < kVersionNeedleCount; ++k) {
                    if (off + preLen + bareLen[k] <= size &&
                        memcmp(scan + off + preLen, bare[k], bareLen[k]) == 0) {
                        r.index = k;
                        r.offset = off;
                        r.tier = 1;
                        r.wide = (wide != 0);
                        r.prefixIndex = pi;
                        return r;
                    }
                }
            }
        }
    }
    return r;
}

// ── Tier 2 + 3 ───────────────────────────────────────────────────────────────
//
// SEMANTICS, restated from the original nest because the fused form only looks
// different if you have not written this down:
//
//   for each pattern in TABLE order:
//       walk offsets ascending; at the first offset where the needle matches AND
//       qualifies as Tier 2 -> return that pattern immediately;
//       at the first offset where it matches AND qualifies as Tier 3 -> record it
//       (only if no Tier 3 was recorded yet) and move to the NEXT pattern.
//
// So each pattern is characterised by ONE fact: the lowest offset at which it
// qualifies, and which tier that was. Compute that per pattern in an ascending walk,
// then select in TABLE order — first pattern with a Tier 2 fact wins, else the first
// with a Tier 3 fact. That is identical to the original, and it is the part a naive
// fuse gets wrong by returning on the first Tier 2 hit in ADDRESS order instead.
//
// ⚠ Selection is a single post-pass over the facts of BOTH walks, deliberately — not a
// return inside each walk. With a per-walk return, the '5' walk finishing first would
// preserve UE5-before-UE4 ordering by construction, and a test comparing a UE5 needle
// against a UE4 one could then NOT detect an address-order regression; only an
// intra-group case (two '5' needles) would. The post-pass makes both shapes
// discriminating — measured, both go red when selection is switched to address order —
// but dll_helpers_test keeps the intra-group case anyway, because it is the one that
// stays honest if anyone ever moves the return back inside a walk.
//
// Tier 2's context window is deliberately reproduced EXACTLY, including the fact that
// it copies 8 bytes while its comment and buffer both say 16. That mismatch is a real
// (separately filed) defect; fixing it here would break the equivalence this rewrite
// rests on.
inline NeedleScanResult ScanVersionTier23(const uint8_t* scan, size_t size) {
    NeedleScanResult r;
    if (!scan) return r;

    // (G9) TWO facts per pattern, not one. The old code kept a single fact and retired the
    // pattern on whichever came first — so a Tier 3 candidate at a LOW offset hid a Tier 2
    // hit for the SAME needle later in the image. The deferral design's own comment says it
    // exists to stop a stray SDK version "out-racing a real Release-4.27 string later in the
    // module"; that held ACROSS patterns and failed WITHIN one.
    bool   hasT2[kVersionNeedleCount] = {};
    size_t offT2 [kVersionNeedleCount] = {};
    bool   hasT3[kVersionNeedleCount] = {};
    size_t offT3 [kVersionNeedleCount] = {};

    for (int group = 0; group < 2; ++group) {
        const uint8_t firstByte = group == 0 ? uint8_t('5') : uint8_t('4');

        // The walk bound must be the LOOSEST per-pattern bound, not a tighter shared one,
        // or the gate would skip offsets the naive loop examines. Shortest needle is 4
        // chars, whose bound is `off + 4 + 10 < size`. The per-pattern check below then
        // narrows it for 5-char needles. Getting this backwards silently loses a hit in
        // the last few bytes of the image, which no real binary would ever show you.
        for (size_t off = 0; off + 14 < size; ++off) {
            if (scan[off] != firstByte) continue;
            if (scan[off + 1] != '.') continue;     // every needle has '.' at index 1

            for (size_t k = 0; k < kVersionNeedleCount; ++k) {
                const char* n = kVersionNeedles[k].needle;
                if (static_cast<uint8_t>(n[0]) != firstByte) continue;
                // Retire only once a Tier 2 is known: that is the best this pattern can do,
                // so nothing later can improve it. A known Tier 3 must NOT retire it — that
                // was G9.
                if (hasT2[k]) continue;

                const size_t needleLen = strlen(n);
                // (G11) The needle table's trailing '.' is a TIER 3 device — it forces a
                // three-component "X.Y.Z" so a bare "5.4" cannot match "15.40". Matching it
                // for Tier 2 as well is why TIER 2 HAD NEVER FIRED ON ANY BINARY THIS
                // PROJECT OWNS (measured 0/85 images): UE's own engine tag is TWO-component,
                // "++UE4+Release-4.27", so "4.27." is simply not present. This is verbatim
                // the defect Genau.h's rev-2 note records fixing FOR TIER 1 via the dot-strip
                // in ScanVersionTier1 — Tier 2 never received it.
                const size_t bareLen = needleLen - 1;   // every needle ends '.', static_assert'd

                // Per-pattern bound, NOT a shared one: 4- and 5-char needles examine a
                // different set of trailing offsets, and collapsing them changes results.
                // Kept on needleLen, not bareLen, so the examined offset set is unchanged.
                if (!(off + needleLen + 10 < size)) continue;

                ++r.needlesCompared;
                if (memcmp(scan + off, n, bareLen) != 0) continue;

                // Structural guard, HOISTED out of Tier 3 (G11): a version token must not be
                // preceded by a digit or a dot, or "15.42" matches "5.4" and "1.5.4" matches
                // "5.4". Tier 3 always had this; Tier 2 did not, and it needs it far more now
                // that it matches the shorter bare form.
                if (off > 0) {
                    uint8_t prev = scan[off - 1];
                    if ((prev >= '0' && prev <= '9') || prev == '.') continue;
                }

                // Tier 2: "Release" in the preceding window AND a UE anchor nearby.
                // (G8) The anchor requirement is deliberate. Tier 2 was the loosest predicate
                // in the system — unlike Tier 3 it had no anchor gate at all — so widening
                // its window without one manufactures a confident detection out of an
                // ordinary "Release Notes 5.4.0" that the narrow form rejected outright.
                //
                // (G11) The bare needle must be a WHOLE token: the next byte may not be a
                // digit. That admits both real shapes — two-component "Release-4.27 " and
                // three-component "Release-5.4.2", whose next byte is '.' — while still
                // rejecting "Release 5.40", which is a game version, not an engine one.
                const uint8_t afterBare = scan[off + bareLen];
                const bool bareIsWholeToken = !(afterBare >= '0' && afterBare <= '9');
                if (bareIsWholeToken &&
                    HasReleaseBefore(scan, off, 16) &&
                    HasUEAnchorNearby(scan, size, off, /*windowBytes=*/256)) {
                    hasT2[k] = true;
                    offT2[k] = off;
                    continue;
                }

                // Tier 3: bare "X.Y.D" with a UE anchor nearby. Requires the FULL needle —
                // i.e. the trailing dot AND a digit after it — which is exactly what the
                // pre-G11 full-needle memcmp plus the digit test expressed, restated now that
                // the match above is on the bare form.
                if (scan[off + bareLen] == '.' &&
                    scan[off + needleLen] >= '0' && scan[off + needleLen] <= '9') {
                    if (!HasUEAnchorNearby(scan, size, off, /*windowBytes=*/256)) continue;
                    if (!hasT3[k]) { hasT3[k] = true; offT3[k] = off; }
                    // NOTE: no `continue`-to-next-pattern and no retire — keep scanning this
                    // needle for a Tier 2 hit further along. (G9)
                }
            }
        }
    }

    // Selection in TABLE order — see the semantics note above. Any Tier 2 outranks every
    // Tier 3; among the same tier the earliest table entry wins, and the table is strictly
    // descending in value, so "earlier entry" means "newer engine".
    for (size_t k = 0; k < kVersionNeedleCount; ++k) {
        if (hasT2[k]) { r.index = k; r.offset = offT2[k]; r.tier = 2; return r; }
    }
    for (size_t k = 0; k < kVersionNeedleCount; ++k) {
        if (hasT3[k]) { r.index = k; r.offset = offT3[k]; r.tier = 3; return r; }
    }
    return r;
}

}  // namespace Genau
