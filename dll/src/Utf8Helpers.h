#pragma once

// ============================================================
// Utf8Helpers — UTF-8 string sanitization + UTF-16 → UTF-8 encoding
//
// Header-only so the unit test target can pick it up without depending on
// the rest of the DLL build (no MinHook, no nlohmann, no Win32 hooks).
//
// Two pure functions:
//
//   Sanitize(in) — walk a candidate UTF-8 byte sequence and replace any
//     malformed run with '?'. Defends against the historical
//     "invalid UTF-8 byte 0xA0" nlohmann::json failure: even when the
//     source path looks sound, ill-formed UTF-8 (CESU-8-style surrogate
//     encodings, overlongs, truncated sequences) can leak through and
//     trip nlohmann's strict validator.
//
//   EncodeUtf16(data, len) — convert a UTF-16 code-unit buffer to valid
//     UTF-8. Surrogate pairs are recognized and combined into a 4-byte
//     UTF-8 sequence (required for emoji and supplementary-plane chars).
//     Lone surrogates are replaced with '?'.
//
// Both are isolated logic with no dependencies beyond <string> + <cstdint>,
// so utf8_helpers_test.cpp can include them and run assert-based tests
// without linking into the full DLL.
// ============================================================

#include <string>
#include <cstdint>
#include <cstddef>

namespace Utf8Helpers {

inline std::string Sanitize(const std::string& in) {
    std::string out;
    out.reserve(in.size());
    size_t i = 0;
    while (i < in.size()) {
        unsigned char b0 = static_cast<unsigned char>(in[i]);
        if (b0 < 0x80) {
            // ASCII — pass through, but reject control bytes other than \t/\n/\r
            // because they can interact badly with downstream XML/JSON consumers.
            if (b0 < 0x20 && b0 != '\t' && b0 != '\n' && b0 != '\r') out += '?';
            else out += static_cast<char>(b0);
            ++i;
            continue;
        }
        // Multi-byte sequence: decode strictly, reject overlongs and surrogates.
        int extra = 0;
        uint32_t cp = 0;
        uint32_t minCp = 0;
        if ((b0 & 0xE0) == 0xC0) { extra = 1; cp = b0 & 0x1F; minCp = 0x80; }
        else if ((b0 & 0xF0) == 0xE0) { extra = 2; cp = b0 & 0x0F; minCp = 0x800; }
        else if ((b0 & 0xF8) == 0xF0) { extra = 3; cp = b0 & 0x07; minCp = 0x10000; }
        else { out += '?'; ++i; continue; }

        // Truncated sequence — fall back to per-byte rejection (we don't know
        // how many bytes belong to the bad sequence, so play it safe and let
        // the loop reject each byte individually).
        if (i + static_cast<size_t>(extra) >= in.size()) { out += '?'; ++i; continue; }

        bool ok = true;
        for (int k = 1; k <= extra; ++k) {
            unsigned char bk = static_cast<unsigned char>(in[i + k]);
            if ((bk & 0xC0) != 0x80) { ok = false; break; }
            cp = (cp << 6) | (bk & 0x3F);
        }
        if (!ok) {
            // Structurally malformed (a continuation byte was missing).
            // Reject only the lead byte; let the loop re-evaluate the next
            // byte (it might be a valid lead or another lone continuation).
            out += '?'; ++i; continue;
        }
        if (cp < minCp || cp > 0x10FFFF || (cp >= 0xD800 && cp <= 0xDFFF)) {
            // Structurally well-formed but semantically invalid — we KNOW
            // the sequence length so skip past it as one malformed unit.
            // Produces one '?' per bad codepoint instead of one per byte,
            // keeping sanitized output compact.
            out += '?'; i += static_cast<size_t>(extra) + 1; continue;
        }
        for (int k = 0; k <= extra; ++k) out += in[i + k];
        i += static_cast<size_t>(extra) + 1;
    }
    return out;
}

// Heuristic guard against a junk / misidentified FName whose "wide" bit landed
// on an ANSI buffer. An asset path like "/Game/Foo/Bar" reinterpreted as
// UTF-16LE decodes to a long run of CJK mojibake — each ASCII byte-pair becomes
// one BMP code unit (e.g. bytes "/M" → U+4D2F 䴯). Genuine wide FNames are short
// localized display strings; UE asset/class/property FNames are ASCII. Return
// true when a wide decode is implausible so callers drop it instead of
// surfacing 亂碼 (DQ3 HD-2D: a value in raw heap resolved to a 435-char mojibake
// class name through a backward-scan false positive).
//
// Operates on the raw UTF-16 code units (NUL-terminated within `len`). The
// signal is length + non-ASCII density: a real wide FName is never long, and a
// long mostly-non-ASCII wide name is the mojibake fingerprint. Trade-off: a
// legitimately long all-CJK FName (rare to nonexistent in UE) would also be
// dropped — acceptable for this dumper's domain.
inline bool IsImplausibleWideName(const wchar_t* data, size_t len) {
    if (!data) return false;
    size_t total = 0, nonAscii = 0;
    for (size_t i = 0; i < len; ++i) {
        uint16_t u = static_cast<uint16_t>(data[i]);
        if (u == 0) break;          // NUL-terminated
        ++total;
        if (u >= 0x80) ++nonAscii;
    }
    if (total == 0) return false;
    // No real wide FName approaches this length — long wide = mojibake.
    if (total > 64) return true;
    // Medium length dominated by non-ASCII (>=75%) is the mojibake signature,
    // while still preserving realistic short localized names (< 24 chars).
    if (total > 24 && nonAscii * 4 >= total * 3) return true;
    return false;
}

// Decode a narrow (ANSI) UE FName payload into a clean string, or "" when it is junk.
// A real narrow FName is pure printable ASCII by construction — UE stores localized /
// non-ASCII text with the wide bit set (handled by EncodeUtf16 + IsImplausibleWideName).
// So ANY byte outside [0x20,0x7F), other than a NUL terminator, means the bytes are NOT
// a valid narrow FName: the entry is corrupt, or an invalid / uninitialized FName index
// landed mid-entry inside a packed FNamePool. (In a packed pool, indices 1 and 2 fall
// inside the 6-byte 'None' entry, so an uninitialized 4-byte NameProperty value such as
// ComparisonIndex=2 decodes the tail of 'None' plus adjacent packed-entry header bytes.)
// Returning "" lets callers treat it as a miss and render 'None' instead of surfacing a
// misleading concatenation like "??ByteProperty??IntProperty" (the sanitized header
// bytes between packed entries + the engine-intrinsic name tokens they sit next to).
//
// This is the ANSI sibling of IsImplausibleWideName, but stricter: a narrow FName has NO
// legitimate non-printable bytes, so the first one is decisive (no length/density
// threshold — that under-fires on a junk run dominated by printable name tokens). A
// legitimate literal '?' (0x3F) is printable and is preserved.
inline std::string SanitizeAnsiName(const char* data, size_t maxLen) {
    if (!data) return "";
    std::string out;
    out.reserve(maxLen < 64 ? maxLen : 64);
    for (size_t i = 0; i < maxLen; ++i) {
        unsigned char c = static_cast<unsigned char>(data[i]);
        if (c == 0) break;                      // NUL terminator
        if (c < 0x20 || c >= 0x7F) return "";   // non-ASCII => corrupt / misidentified entry
        out += static_cast<char>(c);
    }
    return out;
}

inline std::string EncodeUtf16(const wchar_t* data, size_t len) {
    std::string result;
    if (!data || len == 0) return result;
    result.reserve(len * 3);
    for (size_t i = 0; i < len; ++i) {
        uint32_t ch = static_cast<uint32_t>(static_cast<uint16_t>(data[i]));
        if (ch == 0) break;

        // Surrogate pair (high then low) → combine to 4-byte UTF-8.
        if (ch >= 0xD800 && ch <= 0xDBFF && i + 1 < len) {
            uint32_t low = static_cast<uint32_t>(static_cast<uint16_t>(data[i + 1]));
            if (low >= 0xDC00 && low <= 0xDFFF) {
                uint32_t cp = 0x10000u + ((ch - 0xD800u) << 10) + (low - 0xDC00u);
                result += static_cast<char>(0xF0 | (cp >> 18));
                result += static_cast<char>(0x80 | ((cp >> 12) & 0x3F));
                result += static_cast<char>(0x80 | ((cp >> 6) & 0x3F));
                result += static_cast<char>(0x80 | (cp & 0x3F));
                ++i;
                continue;
            }
        }

        // Lone surrogate (high without low, low without high) → '?'.
        if (ch >= 0xD800 && ch <= 0xDFFF) {
            result += '?';
            continue;
        }

        if (ch < 0x80) {
            result += static_cast<char>(ch);
        } else if (ch < 0x800) {
            result += static_cast<char>(0xC0 | (ch >> 6));
            result += static_cast<char>(0x80 | (ch & 0x3F));
        } else {
            // BMP character (0x800..0xFFFF, surrogate range already filtered).
            result += static_cast<char>(0xE0 | (ch >> 12));
            result += static_cast<char>(0x80 | ((ch >> 6) & 0x3F));
            result += static_cast<char>(0x80 | (ch & 0x3F));
        }
    }
    return result;
}

// Cheap "did this decode to real text?" guard for DecodeFStringBuffer. A buffer
// decoded at the WRONG element width is dominated by Sanitize's '?' replacement
// marker; genuine text is not. Counts decoded CHARACTERS (skipping UTF-8
// continuation bytes so a multi-byte CJK glyph counts once) and rejects when
// more than a third of them are replacements. A handful of legitimate literal
// '?' in a real sentence still passes.
inline bool LooksLikeDecodedText(const std::string& s) {
    if (s.empty()) return false;
    size_t total = 0, bad = 0;
    for (unsigned char c : s) {
        if ((c & 0xC0) == 0x80) continue;   // UTF-8 continuation — same char
        ++total;
        if (c == '?') ++bad;
    }
    if (total == 0) return false;
    return bad * 3 < total;
}

// Strict UTF-8 well-formedness (RFC 3629): correct lead/continuation shapes, no
// overlong encodings, no surrogates, nothing past U+10FFFF. `hasMultiByte` reports
// whether any multi-byte sequence was present — that flag, not the byte count, is the
// decisive half of the width decision in DecodeFStringBuffer.
//
// This is the structural test that a replacement-RATIO guess cannot be: the first n
// bytes of a UTF-16 CJK buffer contain lone continuation bytes (a UTF-16 low byte like
// 0x87 is a continuation byte with no lead), which is not a "mostly text" question but
// a "this cannot be UTF-8" one. (B28)
//
// `hasMultiByte` is written ONLY on success (audit #5 AD25). It used to be set true the
// moment any valid multi-byte sequence was accepted, so a buffer whose FIRST sequence
// was fine and whose second was not returned false with the flag left true — an
// out-param contradicting its own return. Nothing reads it on a false return today
// (DecodeFStringBuffer gates on `utf8Ok`, which short-circuits), so this is a latent
// trap rather than a live bug; but "only meaningful when it returned true" is a rule the
// next caller has to be told, and not needing to tell them is better than telling them.
// Accumulating into a local and committing at the single success exit means the
// out-param is now simply true or false about the buffer as a whole.
inline bool IsWellFormedUtf8(const uint8_t* p, size_t n, bool& hasMultiByte) {
    hasMultiByte = false;
    bool sawMultiByte = false;
    for (size_t i = 0; i < n; ) {
        const uint8_t c = p[i];
        if (c < 0x80) { ++i; continue; }         // ASCII
        size_t need; uint32_t cp, lo, hi;
        if      ((c & 0xE0) == 0xC0) { need = 1; cp = c & 0x1Fu; lo = 0x80;    hi = 0x7FF; }
        else if ((c & 0xF0) == 0xE0) { need = 2; cp = c & 0x0Fu; lo = 0x800;   hi = 0xFFFF; }
        else if ((c & 0xF8) == 0xF0) { need = 3; cp = c & 0x07u; lo = 0x10000; hi = 0x10FFFF; }
        else return false;                       // lone continuation, or a 5/6-byte form
        if (i + need >= n) return false;         // truncated sequence
        for (size_t k = 1; k <= need; ++k) {
            const uint8_t cc = p[i + k];
            if ((cc & 0xC0) != 0x80) return false;
            cp = (cp << 6) | (cc & 0x3Fu);
        }
        if (cp < lo || cp > hi) return false;                // overlong / out of range
        if (cp >= 0xD800 && cp <= 0xDFFF) return false;      // surrogate half
        sawMultiByte = true;
        i += need + 1;
    }
    hasMultiByte = sawMultiByte;   // committed only here — see the AD25 note above
    return true;
}

// TruncateUtf8 — shorten `s` to at most `maxBytes` bytes WITHOUT splitting a
// multi-byte sequence, appending `ellipsis` when anything was cut.
//
// Exists because a bare `s.resize(50)` on a validated UTF-8 preview cuts at a raw
// byte boundary. A CJK string is 3 bytes per character, so 50 lands mid-sequence
// two times in three, and the result is no longer well-formed UTF-8. nlohmann's
// default `dump()` is STRICT: it throws type_error.316 on invalid UTF-8, and the
// throw is caught far enough up that the whole response becomes {"error":...} —
// so a search that genuinely matched returns zero results. (audit #5 U7)
//
// Cutting is done by walking BACK from the limit over continuation bytes
// (0b10xxxxxx), which is the standard boundary test and needs no decoder: a lead
// byte is anything that is not 0b10xxxxxx. Bounded to 3 steps because that is the
// longest possible continuation run in UTF-8; more than that means the input was
// already malformed, in which case the byte-exact cut is kept — this helper
// shortens strings, it does not repair them.
//
// `maxBytes` is a budget for the TEXT only; the ellipsis is added on top, exactly
// as the open-coded version it replaces did.
inline std::string TruncateUtf8(const std::string& s, size_t maxBytes,
                                const char* ellipsis = "\xe2\x80\xa6" /* U+2026 */) {
    if (s.size() <= maxBytes) return s;

    size_t cut = maxBytes;
    for (int back = 0; back < 3 && cut > 0; ++back) {
        if ((static_cast<uint8_t>(s[cut]) & 0xC0) != 0x80) break;  // s[cut] starts a new char
        --cut;
    }
    // cut now indexes a lead byte (or 0), so [0, cut) ends on a character boundary.
    std::string out = s.substr(0, cut);
    if (ellipsis) out += ellipsis;
    return out;
}

// ============================================================
// DecodeFStringBuffer — decode a raw FString / FUtf8String buffer whose
// element WIDTH is unknown, choosing UTF-8 (1-byte: FUtf8String, or a game
// that stores its FText display string as UTF-8) vs UTF-16 (2-byte TCHAR).
//
//   buf       raw bytes read from the string's Data pointer
//   bufLen    how many bytes were actually read (ideally >= numUnits*2 so the
//             UTF-16 hypothesis can be tested; >= numUnits is enough for UTF-8)
//   numUnits  the FString header's Num — a code-UNIT count INCLUDING the
//             trailing null (Len = Num - 1)
//
// Candidate terminator positions:
//   * UTF-8  : byte[numUnits-1] == 0 AND no 0x00 in [0, numUnits-1)
//   * UTF-16 : the unit at index numUnits-1 is 0x0000 (bytes at 2*(N-1))
//
// Both hypotheses are EVALUATED BEFORE EITHER IS RETURNED. Returning on the first one
// that merely passed was wrong, because the two pieces of evidence are not equally
// strong (audit #4 B28):
//
//   * The UTF-8 evidence (byte n-1 == 0) sits INSIDE a UTF-16 string's own payload
//     (n-1 < 2n-2 for every n > 1), so UTF-16 text produces it routinely — any U+xx00
//     character, and every ASCII character's high byte. "中文一二" hits it exactly.
//   * The UTF-16 evidence (a zero unit at byte 2n-2) sits OUTSIDE an n-byte UTF-8
//     string's payload, in unrelated heap. It can only be satisfied by chance.
//
// So the decision is, in order:
//   1. Reject the UTF-8 hypothesis unless the bytes are STRICTLY well-formed UTF-8.
//      This alone kills the reported CJK failures: a UTF-16 prefix carries lone
//      continuation bytes. A replacement-RATIO test could not — "中文一二" decodes to
//      "-N?e", one bad char in four, which any sane ratio accepts.
//   2. Well-formed AND containing a multi-byte sequence ⇒ UTF-8, decided. A UTF-16
//      prefix essentially cannot produce a valid multi-byte sequence, and this is the
//      shipped real-world case (an FText holding UTF-8 CJK).
//   3. Otherwise the UTF-8 candidate is pure ASCII, which a UTF-16 prefix explains just
//      as well ("第1章" → ",{1", "中A文" → "-NA"), so prefer UTF-16 when it decodes
//      cleanly — the stronger evidence wins.
//
// THE ASCII TIE, and why rules 1-3 alone got it wrong (audit #5 AD5).
//
// The case rule 3 cannot settle is a UTF-8 buffer that is PURE ASCII: rule 2 needs a
// multi-byte sequence and there is none, so UTF-16 wins whenever its 2n-byte window
// happens to read as text. That window is n real ASCII bytes plus n bytes of adjacent
// heap, and each ASCII PAIR becomes one BMP unit — "Continue" comes back as
// U+6F43 U+746E U+6E69 U+6575. It reaches here through Ubel::ReadFTextString, which
// blind-scans FTextData+0x08..0x90 and offers every 16-byte candidate to
// TryDecodeFStringAt; a cooked build storing an FText display string as UTF-8 with
// English (i.e. ASCII) text is the ordinary case, not an exotic one.
//
// This block previously argued the residual was near-unreachable because "both
// conditions must hold — LooksLikeDecodedText rejects a binary tail". That is
// MEASURED FALSE, and it is the sentence that kept the bug open:
//   * The two conditions are the SAME heap state, not independent coincidences.
//     Zero-filled slack — the common allocator state — supplies both at once, and the
//     mis-decode then fires 100% of the time rather than by luck.
//   * The mojibake contains ZERO replacement characters, so LooksLikeDecodedText
//     accepts it. It was never going to reject this; it rejects binary, and a run of
//     valid CJK code points is not binary.
//
// Rule 3a below settles it with a structural fact instead of a heuristic: an FString
// is a TArray<TCHAR> whose Num includes the terminator, so exactly one null unit
// exists and it is the last. An interior null unit proves the second half of the
// window is not part of the string. Applied ONLY as a tie-break when the UTF-8
// hypothesis independently succeeded, so it cannot change any case that is not this
// ambiguity. Rule 2 still protects every multi-byte case regardless of heap.
//
// Returns sanitized UTF-8, or "" if neither hypothesis yields a null-terminated,
// mostly-textual string.
// ============================================================
inline std::string DecodeFStringBuffer(const uint8_t* buf, size_t bufLen, int32_t numUnits) {
    if (!buf || numUnits < 2) return "";   // < 2 units = empty (just the null)
    const size_t n = static_cast<size_t>(numUnits);

    // --- UTF-8 hypothesis: n bytes, terminator at [n-1], no interior null ---
    std::string utf8Decoded;
    bool utf8Ok = false, utf8HasMultiByte = false;
    if (bufLen >= n && buf[n - 1] == 0) {
        bool interiorNull = false;
        for (size_t i = 0; i + 1 < n; ++i) {
            if (buf[i] == 0) { interiorNull = true; break; }
        }
        if (!interiorNull && IsWellFormedUtf8(buf, n - 1, utf8HasMultiByte)) {
            utf8Decoded = Sanitize(std::string(reinterpret_cast<const char*>(buf), n - 1));
            utf8Ok = LooksLikeDecodedText(utf8Decoded);
        }
    }

    // Step 2: a well-formed multi-byte sequence settles the width on its own.
    if (utf8Ok && utf8HasMultiByte) return utf8Decoded;

    // --- UTF-16 hypothesis: n wchar_t units, terminating unit at [n-1] ---
    if (bufLen >= n * 2) {
        const size_t termByte = (n - 1) * 2;
        if (buf[termByte] == 0 && buf[termByte + 1] == 0) {
            // Recombine little-endian byte pairs into UTF-16 units without
            // assuming buf is 2-byte aligned.
            std::wstring w;
            w.reserve(n);
            for (size_t i = 0; i < n; ++i) {
                uint16_t u = static_cast<uint16_t>(buf[i * 2]) |
                             (static_cast<uint16_t>(buf[i * 2 + 1]) << 8);
                w.push_back(static_cast<wchar_t>(u));
            }
            // Step 3a — the ASCII tie-breaker.
            //
            // An FString is a TArray<TCHAR> whose Num INCLUDES the terminator,
            // so a genuine n-unit UTF-16 string has exactly ONE null unit and it
            // sits at n-1. An earlier null unit means these 2n bytes are not an
            // n-unit UTF-16 string at all — the second half is adjacent heap.
            //
            // Deliberately guarded on utf8Ok, i.e. used ONLY to break the tie the
            // KNOWN RESIDUAL above describes, where both hypotheses "work" and
            // the wrong one wins. Ungated, this would also reject buffers with a
            // real interior zero unit that today decode as a truncated prefix —
            // a behaviour change outside this defect, and measurably so.
            bool interiorNullUnit = false;
            if (utf8Ok) {
                for (size_t i = 0; i + 1 < n; ++i) {
                    if (w[i] == 0) { interiorNullUnit = true; break; }
                }
            }

            // Never an early `return ""` here: the function's tail
            // (`return utf8Ok ? utf8Decoded : std::string();`) is what delivers
            // the correct UTF-8 answer once this hypothesis steps aside.
            if (!interiorNullUnit) {
                std::string decoded = Sanitize(EncodeUtf16(w.data(), w.size()));
                // Step 3: stronger evidence wins the ASCII-ambiguous case.
                if (LooksLikeDecodedText(decoded)) return decoded;
            }
        }
    }

    return utf8Ok ? utf8Decoded : std::string();
}

} // namespace Utf8Helpers
