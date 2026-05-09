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

} // namespace Utf8Helpers
