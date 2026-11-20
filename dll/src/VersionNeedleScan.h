#pragma once

// ============================================================
// VersionNeedleScan — the UE-version needle sweep Genau.cpp runs
//
// Header-only and PURE: (const uint8_t*, size_t) in, offsets and table indices out.
// No Win32, no Macht, no Sein. That is the whole point: Genau.cpp reaches only
// `dll_core_test`, not the light dll_helpers_test. Kept pure here, this logic is
// unit-testable in the light target against a naive reference implementation.
//
// Kept in English and NOT given a Frieren roster name: CLAUDE.md's module-naming rule
// exempts "algorithm helpers that live inside an existing namespace", the precedent being
// GraphPath.h under Aura::. This lives under Genau::.
//
// ── WHY THIS EXISTS AT ALL ───────────────────────────────────────────────────
// A naive sweep is one whole-image memcmp pass per needle, Tier 1 and Tier 2/3 alike:
// billions of compares and tens of seconds over a large Shipping exe. Behind the
// first-byte gate below the same input costs a fraction of a second and orders of
// magnitude fewer compares, with the same answers.
//
// ⚠ The measurement trap: benchmarking with a STRING LITERAL needle lets the compiler
// constant-fold strlen and inline the memcmp, understating the real cost by ~5x. The
// production loop takes needleLen from a table at runtime. Any re-measurement must do
// the same.
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
    { "5.9.", 509 }, { "5.8.", 508 }, { "5.7.", 507 }, { "5.6.", 506 }, { "5.5.", 505 },
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
static_assert(kVersionNeedleCount == 20, "needle table changed — re-check the gate below");
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

// The longest branch name ScanVersionBranchName reads, in characters; a longer run is not a branch name.
inline constexpr size_t kBranchMaxChars = 96;

struct NeedleScanResult {
    // Index into kVersionNeedles, or kVersionNeedleCount for "no hit".
    size_t   index  = kVersionNeedleCount;
    size_t   offset = 0;
    int      tier   = 0;   // 0 none, 1/2/3 as in Genau's DetectVersionDetailed
    // Tier 1 only: which prefix/encoding matched, for the log line.
    bool     wide   = false;
    size_t   prefixIndex = 0;
    // Set by ScanVersionBranchName: the branch name it read, narrowed, for the log line.
    char     branch[kBranchMaxChars + 1] = {};
    // Instrumentation, so a test can assert the GATE still works without a wall clock.
    // Counts full needle memcmp calls actually issued. A deterministic stand-in for
    // "is this still fast", which a timing assertion could never be in CI.
    uint64_t needlesCompared = 0;

    bool found() const { return tier != 0; }
};

// ── anchor helper, with a first-byte gate ────────────────────────────────────
//
// The gate matters more than it looks: the fused Tier 2/3 pass below does not return
// the instant it sees a Tier 2 hit, so patterns after the winner reach anchor calls
// too. Without the gate that would be a net slowdown on exactly the images this
// header exists to speed up.
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

// ── Tier 2's context test ───────────────────────────────────────────────────
//
// "Release" (or "release") within the preceding `windowBytes`.
//
// ⚠ A wider `strstr` over a copied window — the obvious alternative — is WRONG IN BOTH DIRECTIONS:
//   * `strstr` over a NUL-terminated copy stops at the FIRST NUL in the copied bytes. A
//     neighbouring string's terminator inside the wider window therefore TRUNCATES the
//     search and LOSES a match a narrower window finds. So a wider strstr is not a
//     superset of a narrower one.
//   * `off >= 16` as the guard drops Tier-2 eligibility for offsets 8..15 — including the
//     canonical "Release-5.4.0", whose needle sits at offset 8.
// A raw byte search over a CLAMPED window has neither problem and is a strict superset.
inline bool HasReleaseBefore(const uint8_t* scan, size_t off, size_t windowBytes) {
    if (off == 0) return false;
    const size_t win = (off < windowBytes) ? off : windowBytes;   // clamp at image start
    const uint8_t* w = scan + off - win;
    for (size_t i = 0; i + 7 <= win; ++i) {
        // "Release" / "release" — exactly these two spellings.
        if ((w[i] == 'R' || w[i] == 'r') && memcmp(w + i + 1, "elease", 6) == 0)
            return true;
    }
    return false;
}

// ── Tier 1: exact engine tag "++UE5+Release-5.4" / "++UE4+Release-4.27" ──────
//
// Order: encoding-major (narrow, then UTF-16LE), then prefix, then ascending offset,
// then table order. The FIRST match in that order wins, so the four walks below must
// stay in this sequence.
//
// `bare` and the widened needle vector are loop-invariant, so they are built once per
// walk, never per candidate offset per pattern.
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

// ── Tier 1, second shape: a licensee's engine branch name ([GWT-BRANCH]) ───────
//
// The engine stamps its build with a branch name, "++<depot>+<stream>", and ScanVersionTier1 reads Epic's own: depot
// UE4/UE5, stream "Release-M.m". A licensee building from a stream of its own keeps the depot and names the stream
// itself, so the tag ScanVersionTier1 looks for is not in the image at all. Such a stream can still name the engine
// version -- "++ue4+ue4_main+4.27" (UTF-16), "++UE4+4.27-Nitro" -- and this reads it. A licensee's own depot
// ("++Alabama+release.2.1", "++Arkansas+release-1.2.0.1") names a GAME release, which is why the depot itself must be
// UE4 or UE5.
//
// The grammar is deliberately narrow; each rule is a refusal in dll_helpers_test:
//   * a NUL-terminated printable string, narrow or UTF-16LE, of at most kBranchMaxChars;
//   * starting "++UE4+" or "++UE5+", the "UE" in either case (a depot can be lower-case: "++ue4+");
//   * holding a whole token "M.m": after a '+' or a '-', followed by the end, a '+' or a '-', M the depot's own
//     major and "M.m." a row of kVersionNeedles, so the table's 4.18 floor holds here as in every tier.
// Epic's own tag fits the same grammar (its token follows "Release-"), so the caller runs this after ScanVersionTier1
// has found nothing, and no verdict that function reaches can change. The first match in encoding-major, then
// ascending-offset order wins, as there.

// The kVersionNeedles row a branch name names, from its text; false when it names none under the grammar above.
inline bool BranchNameVersionIndex(const char* text, size_t n, size_t& index) {
    if (n < 6 || text[0] != '+' || text[1] != '+' || (text[2] != 'U' && text[2] != 'u') ||
        (text[3] != 'E' && text[3] != 'e') || (text[4] != '4' && text[4] != '5') || text[5] != '+')
        return false;
    const char major = text[4];
    // text[p] is the separator before a candidate token; the token needs "M." and one digit after it.
    for (size_t p = 5; p + 3 < n; ++p) {
        if (text[p] != '+' && text[p] != '-') continue;
        if (text[p + 1] != major || text[p + 2] != '.') continue;
        size_t q = p + 3;
        while (q < n && text[q] >= '0' && text[q] <= '9') ++q;
        if (q == p + 3) continue;
        if (q < n && text[q] != '+' && text[q] != '-') continue;
        const size_t tokenLen = q - (p + 1);
        for (size_t k = 0; k < kVersionNeedleCount; ++k) {
            const char* needle = kVersionNeedles[k].needle;   // "M.m.": the token plus the table's trailing dot
            if (strlen(needle) == tokenLen + 1 && memcmp(needle, text + p + 1, tokenLen) == 0) {
                index = k;
                return true;
            }
        }
    }
    return false;
}

inline NeedleScanResult ScanVersionBranchName(const uint8_t* scan, size_t size) {
    NeedleScanResult r;
    if (!scan) return r;
    for (int wide = 0; wide <= 1; ++wide) {
        const size_t step = wide ? 2 : 1;
        for (size_t off = 0; off + 6 * step <= size; ++off) {
            if (scan[off] != '+') continue;   // the gate: every branch name starts '+', narrow and widened
            ++r.needlesCompared;
            // The i-th character at `off` in this encoding, or -1 where there is none.
            auto at = [&](size_t i) -> int {
                const size_t b = off + i * step;
                if (b + step > size || (wide && scan[b + 1] != 0)) return -1;
                return scan[b];
            };
            // The fixed prefix first: most '+' bytes in an image are code, and this rejects them before any decode.
            const int u = at(2), e = at(3), m = at(4);
            if (at(0) != '+' || at(1) != '+' || (u != 'U' && u != 'u') || (e != 'E' && e != 'e') ||
                (m != '4' && m != '5') || at(5) != '+')
                continue;
            char text[kBranchMaxChars + 1];
            size_t n = 0;
            bool terminated = false;
            for (size_t i = 0; ; ++i) {
                const int c = at(i);
                if (c == 0) { terminated = true; break; }
                if (c < 0x20 || c > 0x7E || n == kBranchMaxChars) break;   // -1 included: not a string
                text[n++] = static_cast<char>(c);
            }
            if (!terminated) continue;
            text[n] = '\0';
            size_t k = 0;
            if (!BranchNameVersionIndex(text, n, k)) continue;
            r.index = k;
            r.offset = off;
            r.tier = 1;
            r.wide = (wide != 0);
            memcpy(r.branch, text, n + 1);
            return r;
        }
    }
    return r;
}

// ── Tier 2 + 3 ───────────────────────────────────────────────────────────────
//
// SEMANTICS: each pattern is characterised by the lowest offset at which it qualifies
// as Tier 2 and the lowest at which it qualifies as Tier 3, both from an ascending walk.
// Selection is in TABLE order — the first pattern with a Tier 2 fact wins, else the
// first with a Tier 3 fact. A naive fuse gets this wrong by returning on the first
// Tier 2 hit in ADDRESS order instead.
//
// ⚠ Selection is a single post-pass over the facts of BOTH walks, deliberately — not a
// return inside each walk. With a per-walk return, the '5' walk finishing first would
// preserve UE5-before-UE4 ordering by construction, and a test comparing a UE5 needle
// against a UE4 one could then NOT detect an address-order regression; only an
// intra-group case (two '5' needles) would. The post-pass makes both shapes
// discriminating, but dll_helpers_test keeps the intra-group case anyway, because it is
// the one that stays honest if anyone ever moves the return inside a walk.
inline NeedleScanResult ScanVersionTier23(const uint8_t* scan, size_t size) {
    NeedleScanResult r;
    if (!scan) return r;

    // TWO facts per pattern, not one: a single fact retired on whichever tier came first
    // would let a Tier 3 candidate at a LOW offset hide a Tier 2 hit for the SAME needle
    // later in the image. The deferral exists to stop a stray SDK version "out-racing a real
    // Release-4.27 string later in the module", WITHIN a pattern as well as across them.
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
                // so nothing later can improve it. A known Tier 3 must NOT retire it.
                if (hasT2[k]) continue;

                const size_t needleLen = strlen(n);
                // The needle table's trailing '.' is a TIER 3 device — it forces a
                // three-component "X.Y.Z" so a bare "5.4" cannot match "15.40". Tier 2 must
                // NOT match it: UE's own engine tag is TWO-component, "++UE4+Release-4.27",
                // so "4.27." is simply not present (ScanVersionTier1 strips the dot for the
                // same reason).
                const size_t bareLen = needleLen - 1;   // every needle ends '.', static_assert'd

                // Per-pattern bound, NOT a shared one: 4- and 5-char needles examine a
                // different set of trailing offsets, and collapsing them changes results.
                // Kept on needleLen, not bareLen, so the examined offset set is unchanged.
                if (!(off + needleLen + 10 < size)) continue;

                ++r.needlesCompared;
                if (memcmp(scan + off, n, bareLen) != 0) continue;

                // Structural guard, for BOTH tiers: a version token must not be preceded by a
                // digit or a dot, or "15.42" matches "5.4" and "1.5.4" matches "5.4". Tier 2
                // needs it most, since it matches the shorter bare form.
                if (off > 0) {
                    uint8_t prev = scan[off - 1];
                    if ((prev >= '0' && prev <= '9') || prev == '.') continue;
                }

                // Tier 2: "Release" in the preceding window AND a UE anchor nearby.
                // The anchor requirement is deliberate: without it the 16-byte "Release" window
                // makes a confident detection out of an ordinary "Release Notes 5.4.0".
                //
                // The bare needle must be a WHOLE token: the next byte may not be a
                // digit. That admits both real shapes — two-component "Release-4.27\0" and
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
                // the trailing dot AND a digit after it — tested here because the match
                // above is on the bare form.
                if (scan[off + bareLen] == '.' &&
                    scan[off + needleLen] >= '0' && scan[off + needleLen] <= '9') {
                    if (!HasUEAnchorNearby(scan, size, off, /*windowBytes=*/256)) continue;
                    if (!hasT3[k]) { hasT3[k] = true; offT3[k] = off; }
                    // NOTE: no `continue`-to-next-pattern and no retire — keep scanning this
                    // needle for a Tier 2 hit further along.
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
