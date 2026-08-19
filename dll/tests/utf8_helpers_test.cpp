// ============================================================
// Utf8Helpers self-test
//
// Stand-alone executable (no GoogleTest / Catch2 dependency) that exercises
// Utf8Helpers::Sanitize and Utf8Helpers::EncodeUtf16 with crafted inputs.
// Exit code is the count of failures — 0 = all pass.
//
// Built as a separate CMake target (utf8_helpers_test). build.ps1 -Target Test
// runs it before C# tests; any failure short-circuits the test phase.
// ============================================================

#include "../src/Utf8Helpers.h"

#include <cstdio>
#include <cstdint>
#include <string>

static int g_pass = 0;
static int g_fail = 0;

#define EXPECT(label, cond) do { \
    if (cond) { ++g_pass; } \
    else { ++g_fail; std::printf("  FAIL: %s\n    at %s:%d\n", label, __FILE__, __LINE__); } \
} while (0)

#define EXPECT_EQ_STR(label, actual, expected) do { \
    std::string _a = (actual); \
    std::string _e = (expected); \
    if (_a == _e) { ++g_pass; } \
    else { \
        ++g_fail; \
        std::printf("  FAIL: %s\n    actual=", label); \
        for (char c : _a) std::printf("\\x%02X", static_cast<unsigned char>(c)); \
        std::printf("\n    expected="); \
        for (char c : _e) std::printf("\\x%02X", static_cast<unsigned char>(c)); \
        std::printf("\n    at %s:%d\n", __FILE__, __LINE__); \
    } \
} while (0)

// ----- Sanitize tests --------------------------------------------------------

static void Test_Sanitize_AsciiPassthrough() {
    EXPECT_EQ_STR("ascii passthrough",
                  Utf8Helpers::Sanitize("hello world"), "hello world");
    EXPECT_EQ_STR("empty string",
                  Utf8Helpers::Sanitize(""), "");
    EXPECT_EQ_STR("preserved tab/newline/cr",
                  Utf8Helpers::Sanitize("a\tb\nc\rd"), "a\tb\nc\rd");
}

static void Test_Sanitize_RejectsControlBytes() {
    // \x01..\x1F (except \t \n \r) → '?'
    EXPECT_EQ_STR("control byte 0x01",
                  Utf8Helpers::Sanitize(std::string("a\x01" "b")), "a?b");
    EXPECT_EQ_STR("control byte 0x07",
                  Utf8Helpers::Sanitize(std::string("a\x07" "b")), "a?b");
}

static void Test_Sanitize_ValidMultiBytePassthrough() {
    // U+00A0 NO-BREAK SPACE (correctly encoded as 0xC2 0xA0)
    EXPECT_EQ_STR("U+00A0 NBSP correct",
                  Utf8Helpers::Sanitize("\xC2\xA0"), "\xC2\xA0");
    // U+4E2D 中 (3-byte CJK)
    EXPECT_EQ_STR("U+4E2D CJK 中",
                  Utf8Helpers::Sanitize("\xE4\xB8\xAD"), "\xE4\xB8\xAD");
    // U+1F600 😀 (4-byte emoji, valid)
    EXPECT_EQ_STR("U+1F600 emoji 😀",
                  Utf8Helpers::Sanitize("\xF0\x9F\x98\x80"), "\xF0\x9F\x98\x80");
}

static void Test_Sanitize_RejectsLoneContinuation() {
    // Lone 0xA0 (no leader) — this is THE Squad bug 0xA0 case
    EXPECT_EQ_STR("lone 0xA0 → ?",
                  Utf8Helpers::Sanitize(std::string("\xA0", 1)), "?");
    // Multiple lone continuations
    EXPECT_EQ_STR("multiple lone continuations",
                  Utf8Helpers::Sanitize(std::string("\xA0\xA0\x80", 3)), "???");
}

static void Test_Sanitize_RejectsSurrogateEncodings() {
    // CESU-8: U+D800 encoded as 0xED 0xA0 0x80 — what the broken Serie.cpp wide
    // path used to produce on lone surrogates. nlohmann::json type_error.316.
    EXPECT_EQ_STR("CESU-8 high surrogate",
                  Utf8Helpers::Sanitize("\xED\xA0\x80"), "?");
    // U+DFFF → 0xED 0xBF 0xBF
    EXPECT_EQ_STR("CESU-8 low surrogate",
                  Utf8Helpers::Sanitize("\xED\xBF\xBF"), "?");
}

static void Test_Sanitize_RejectsOverlongs() {
    // U+0041 'A' overlong-encoded as 0xC1 0x81 (should be 1 byte 0x41)
    EXPECT_EQ_STR("overlong U+0041",
                  Utf8Helpers::Sanitize("\xC1\x81"), "?");
    // NUL overlong-encoded as 0xC0 0x80 (the "Modified UTF-8" pattern Java uses)
    EXPECT_EQ_STR("overlong NUL",
                  Utf8Helpers::Sanitize(std::string("\xC0\x80", 2)), "?");
}

static void Test_Sanitize_RejectsTruncated() {
    // 2-byte sequence missing continuation
    EXPECT_EQ_STR("truncated 2-byte",
                  Utf8Helpers::Sanitize("\xC2"), "?");
    // 3-byte sequence with only 1 continuation
    EXPECT_EQ_STR("truncated 3-byte",
                  Utf8Helpers::Sanitize("\xE4\xB8"), "??");
    // 4-byte sequence cut off
    EXPECT_EQ_STR("truncated 4-byte",
                  Utf8Helpers::Sanitize("\xF0\x9F\x98"), "???");
}

static void Test_Sanitize_MixedGoodAndBad() {
    // The Squad-style scenario: a real name with one bad lone surrogate inside
    EXPECT_EQ_STR("mixed valid+bad",
                  Utf8Helpers::Sanitize("hi\xED\xA0\x80world"),
                  "hi?world");
}

static void Test_Sanitize_Idempotent() {
    // Sanitize(Sanitize(x)) == Sanitize(x) for any x
    std::string input = "abc\xED\xA0\x80\xF0\x9F\x98\x80\xA0";
    std::string once = Utf8Helpers::Sanitize(input);
    std::string twice = Utf8Helpers::Sanitize(once);
    EXPECT("sanitize is idempotent", once == twice);
}

// ----- EncodeUtf16 tests -----------------------------------------------------

static void Test_EncodeUtf16_AsciiPassthrough() {
    wchar_t hello[] = { 'h', 'e', 'l', 'l', 'o' };
    EXPECT_EQ_STR("ascii encode",
                  Utf8Helpers::EncodeUtf16(hello, 5), "hello");
}

static void Test_EncodeUtf16_NullTerminationStops() {
    wchar_t buf[] = { 'a', 'b', 0, 'c', 'd' };
    EXPECT_EQ_STR("stops at NUL",
                  Utf8Helpers::EncodeUtf16(buf, 5), "ab");
}

static void Test_EncodeUtf16_BMPCharacters() {
    wchar_t cjk[] = { 0x4E2D, 0x6587 };  // 中文
    EXPECT_EQ_STR("CJK 中文",
                  Utf8Helpers::EncodeUtf16(cjk, 2),
                  "\xE4\xB8\xAD\xE6\x96\x87");
    wchar_t latin[] = { 0x00E9 };  // é
    EXPECT_EQ_STR("Latin é",
                  Utf8Helpers::EncodeUtf16(latin, 1), "\xC3\xA9");
}

static void Test_EncodeUtf16_SurrogatePair() {
    // U+1F600 😀 GRINNING FACE = 0xD83D 0xDE00 (high surrogate + low surrogate)
    // Should produce 4-byte UTF-8: 0xF0 0x9F 0x98 0x80
    wchar_t emoji[] = { 0xD83D, 0xDE00 };
    EXPECT_EQ_STR("emoji surrogate pair → 4-byte UTF-8",
                  Utf8Helpers::EncodeUtf16(emoji, 2),
                  "\xF0\x9F\x98\x80");
}

static void Test_EncodeUtf16_MultipleSurrogatePairs() {
    // 😀😀
    wchar_t emojis[] = { 0xD83D, 0xDE00, 0xD83D, 0xDE00 };
    EXPECT_EQ_STR("two emoji",
                  Utf8Helpers::EncodeUtf16(emojis, 4),
                  "\xF0\x9F\x98\x80\xF0\x9F\x98\x80");
}

static void Test_EncodeUtf16_LoneSurrogates() {
    // The Squad 0xA0 bug: lone high surrogate, no low partner.
    // Old code emitted 0xED 0xA0 0x80 (CESU-8). New code emits '?'.
    wchar_t loneHigh[] = { 0xD800 };
    EXPECT_EQ_STR("lone high surrogate → ?",
                  Utf8Helpers::EncodeUtf16(loneHigh, 1), "?");

    // Lone low surrogate (no high before it)
    wchar_t loneLow[] = { 0xDC00 };
    EXPECT_EQ_STR("lone low surrogate → ?",
                  Utf8Helpers::EncodeUtf16(loneLow, 1), "?");

    // High surrogate followed by ASCII (not a valid pair)
    wchar_t badPair[] = { 0xD800, 'A' };
    EXPECT_EQ_STR("high surrogate + ASCII → ? + A",
                  Utf8Helpers::EncodeUtf16(badPair, 2), "?A");
}

static void Test_EncodeUtf16_ReversedSurrogates() {
    // Low followed by high (out-of-order) — both lone
    wchar_t reversed[] = { 0xDC00, 0xD800 };
    EXPECT_EQ_STR("reversed surrogates → ??",
                  Utf8Helpers::EncodeUtf16(reversed, 2), "??");
}

static void Test_EncodeUtf16_MixedRealistic() {
    // Realistic mixed content: ASCII + CJK + surrogate pair + lone surrogate
    wchar_t mix[] = { 'h', 'i', 0x4E2D, 0xD83D, 0xDE00, 0xD800, '!' };
    EXPECT_EQ_STR("mixed realistic",
                  Utf8Helpers::EncodeUtf16(mix, 7),
                  "hi\xE4\xB8\xAD\xF0\x9F\x98\x80?!");
}

static void Test_EncodeUtf16_OutputAlwaysValidUtf8() {
    // Any output from EncodeUtf16 must round-trip cleanly through Sanitize.
    // This is the invariant nlohmann::json relies on.
    //
    // audit #5 AD26 — the input used to open `'A', 0xD800, 0xDE00` under the comment
    // "ASCII + lone high + lone low (mixed)". It was neither: 0xD800 is a high
    // surrogate and 0xDE00 is a LOW one, so those two units are a perfectly VALID
    // pair and EncodeUtf16 combined them into U+10200. The array's only lone
    // surrogate was the 0xDC00 near the end, so the "lone HIGH" branch
    // (`ch` in D800..DBFF with a non-low successor) was never executed by the test
    // that claims to be the pathological case. A lone high followed by ASCII is what
    // actually reaches it.
    wchar_t pathological[] = {
        'A',                        // ASCII
        0xD800, 'B',                // LONE HIGH — successor is not a low surrogate
        0xD83D, 0xDE00,             // valid emoji pair -> U+1F600
        0x4E2D,                     // CJK
        0xDC00,                     // LONE LOW
        0,                          // NUL stops
    };
    std::string encoded = Utf8Helpers::EncodeUtf16(pathological, 8);
    std::string sanitized = Utf8Helpers::Sanitize(encoded);
    EXPECT("EncodeUtf16 output passes Sanitize unchanged", encoded == sanitized);

    // Pin the bytes too. Sanitize-stability alone would still hold if a future edit
    // silently dropped a lone surrogate instead of replacing it, which is the
    // difference this test exists to notice.
    EXPECT_EQ_STR("EncodeUtf16 replaces BOTH lone surrogates and keeps the valid pair",
                  encoded, "A?B\xF0\x9F\x98\x80\xE4\xB8\xAD?");
}

// ----- IsImplausibleWideName tests -------------------------------------------

// Build the UTF-16 code units that result from reading an ASCII byte sequence
// as UTF-16LE — exactly the mojibake an ANSI buffer produces under a stray wide
// flag (e.g. "/M" → 0x4D2F 䴯). Mirrors the DQ3 HD-2D backward-scan junk.
static std::wstring AsciiAsUtf16LE(const std::string& ascii) {
    std::wstring out;
    for (size_t i = 0; i + 1 < ascii.size(); i += 2) {
        uint16_t u = static_cast<uint16_t>(
            (static_cast<unsigned char>(ascii[i + 1]) << 8) |
             static_cast<unsigned char>(ascii[i]));
        out.push_back(static_cast<wchar_t>(u));
    }
    return out;
}

static void Test_ImplausibleWide_RejectsAsciiAsUtf16Mojibake() {
    // A real asset path reinterpreted as UTF-16LE — the actual failure mode.
    std::string path =
        "/Game/NiagaraCore/Materials/IMP_L_C01_Church_05_Inst.IMP_L_C01_Church";
    std::wstring moji = AsciiAsUtf16LE(path);
    EXPECT("long ASCII-as-UTF16 path is implausible",
           Utf8Helpers::IsImplausibleWideName(moji.data(), moji.size()));
}

static void Test_ImplausibleWide_AcceptsShortLocalized() {
    // Genuine short localized names must survive (no false drops).
    wchar_t jp[] = { 0x30EC, 0x30D9, 0x30EB };       // レベル
    EXPECT("short katakana name is plausible",
           !Utf8Helpers::IsImplausibleWideName(jp, 3));
    wchar_t mixed[] = { 'H', 'P', 0x4F53, 0x529B };  // HP体力
    EXPECT("short mixed ASCII+CJK is plausible",
           !Utf8Helpers::IsImplausibleWideName(mixed, 4));
}

static void Test_ImplausibleWide_AcceptsAsciiAndEmpty() {
    wchar_t ascii[] = { 'P', 'l', 'a', 'y', 'e', 'r', 'P', 'a', 'w', 'n' };
    EXPECT("ascii wide name is plausible",
           !Utf8Helpers::IsImplausibleWideName(ascii, 10));
    EXPECT("zero length is plausible (nothing to reject)",
           !Utf8Helpers::IsImplausibleWideName(ascii, 0));
    EXPECT("null data is plausible",
           !Utf8Helpers::IsImplausibleWideName(nullptr, 5));
}

static void Test_ImplausibleWide_LengthAndDensityRules() {
    // > 64 wide chars of anything → implausible (length rule); real wide FNames
    // are short localized strings, never this long.
    std::wstring longAscii(80, L'A');
    EXPECT("80 wide chars exceed the length cap",
           Utf8Helpers::IsImplausibleWideName(longAscii.data(), longAscii.size()));
    // 30 all-CJK chars → implausible (density rule: >24 chars and >=75% non-ASCII).
    std::wstring midCjk(30, static_cast<wchar_t>(0x4E2D));
    EXPECT("30 CJK chars trip the density rule",
           Utf8Helpers::IsImplausibleWideName(midCjk.data(), midCjk.size()));
    // 20 all-CJK chars → still plausible (under the 24-char density gate).
    std::wstring shortCjk(20, static_cast<wchar_t>(0x4E2D));
    EXPECT("20 CJK chars stay plausible",
           !Utf8Helpers::IsImplausibleWideName(shortCjk.data(), shortCjk.size()));
    // NUL terminates the count: long buffer, NUL at index 3 → plausible.
    std::wstring nulCut(80, static_cast<wchar_t>(0x4E2D));
    nulCut[3] = 0;
    EXPECT("NUL terminator caps the counted length",
           !Utf8Helpers::IsImplausibleWideName(nulCut.data(), nulCut.size()));
}

// ----- SanitizeAnsiName tests ------------------------------------------------

static void Test_SanitizeAnsi_CleanNamePassthrough() {
    EXPECT_EQ_STR("clean ascii name",
                  Utf8Helpers::SanitizeAnsiName("IntProperty", 11), "IntProperty");
    EXPECT_EQ_STR("clean name with underscore/digits",
                  Utf8Helpers::SanitizeAnsiName("SpawnTriggerLabel_3", 19), "SpawnTriggerLabel_3");
    // A legitimate literal '?' (0x3F) is printable and must be preserved.
    EXPECT_EQ_STR("literal question mark preserved",
                  Utf8Helpers::SanitizeAnsiName("What?", 5), "What?");
    // Other printable specials a name may legitimately contain.
    EXPECT_EQ_STR("printable specials preserved",
                  Utf8Helpers::SanitizeAnsiName("A$B@C.D", 7), "A$B@C.D");
}

static void Test_SanitizeAnsi_RejectsJunkRun() {
    // The exact Elliot bug: an invalid FName index (2) lands mid-'None' in a packed
    // FNamePool, so the decode begins with a non-printable packed-entry header byte and
    // concatenates engine-intrinsic tokens — "??ByteProperty??IntProperty". The first
    // junk byte is decisive: SanitizeAnsiName returns "" so the caller renders 'None'.
    // unsigned char so the >0x7F header bytes don't truncate (C4310); reinterpret to
    // const char* for the call (the production buffer is char* read from process memory).
    const unsigned char idx2[] = { 0x0C, 0x03, 'B','y','t','e','P','r','o','p','e','r','t','y',
                                   0x0B, 0x03, 'I','n','t','P','r','o','p','e','r','t','y' };
    EXPECT_EQ_STR("packed-header junk run rejected",
                  Utf8Helpers::SanitizeAnsiName(reinterpret_cast<const char*>(idx2), sizeof(idx2)), "");
    // The index-1 variant starts with the clean 'None' tail then hits junk — still "".
    const unsigned char idx1[] = { 'n','e', 0x0C, 0x03, 'B','y','t','e','P','r','o','p','e','r','t','y' };
    EXPECT_EQ_STR("clean-prefix-then-junk rejected",
                  Utf8Helpers::SanitizeAnsiName(reinterpret_cast<const char*>(idx1), sizeof(idx1)), "");
    // A single high byte (e.g. Latin-1 0xE9) means a non-narrow / corrupt entry → "".
    const unsigned char hi[] = { 'A', 'b', 0xE9, 'c' };
    EXPECT_EQ_STR("high byte rejected",
                  Utf8Helpers::SanitizeAnsiName(reinterpret_cast<const char*>(hi), sizeof(hi)), "");
}

static void Test_SanitizeAnsi_NulAndBounds() {
    // NUL terminates within maxLen; trailing bytes are ignored.
    const char withNul[] = { 'O','k', 0, 'j','u','n','k' };
    EXPECT_EQ_STR("stops at NUL",
                  Utf8Helpers::SanitizeAnsiName(withNul, sizeof(withNul)), "Ok");
    // maxLen bounds the scan even with no NUL.
    EXPECT_EQ_STR("maxLen bounds the read",
                  Utf8Helpers::SanitizeAnsiName("IntProperty", 3), "Int");
    EXPECT_EQ_STR("empty (zero len)",
                  Utf8Helpers::SanitizeAnsiName("anything", 0), "");
    EXPECT_EQ_STR("null data",
                  Utf8Helpers::SanitizeAnsiName(nullptr, 8), "");
    // Immediate NUL → empty (degenerate entry → caller renders 'None').
    const char nul0[] = { 0, 'x' };
    EXPECT_EQ_STR("immediate NUL is empty",
                  Utf8Helpers::SanitizeAnsiName(nul0, sizeof(nul0)), "");
}

// ----- DecodeFStringBuffer tests ---------------------------------------------

// The exact bytes CE dumped for Star Trek Voyager (stock UE5.6): the FText
// display string "在维修期间继续探索星系" stored as UTF-8 (33 bytes) + NUL.
// numUnits for a 1-byte buffer = byte count incl. NUL = 34.
static const uint8_t kUtf8Cjk[] = {
    0xE5,0x9C,0xA8, 0xE7,0xBB,0xB4, 0xE4,0xBF,0xAE, 0xE6,0x9C,0x9F,
    0xE9,0x97,0xB4, 0xE7,0xBB,0xA7, 0xE7,0xBB,0xAD, 0xE6,0x8E,0xA2,
    0xE7,0xB4,0xA2, 0xE6,0x98,0x9F, 0xE7,0xB3,0xBB, 0x00
};
static const char* kUtf8CjkExpected =
    "\xE5\x9C\xA8\xE7\xBB\xB4\xE4\xBF\xAE\xE6\x9C\x9F\xE9\x97\xB4"
    "\xE7\xBB\xA7\xE7\xBB\xAD\xE6\x8E\xA2\xE7\xB4\xA2\xE6\x98\x9F\xE7\xB3\xBB";

static void Test_Decode_Utf8CjkExactBuffer() {
    // Read exactly Num bytes (near-page-end fallback path).
    EXPECT_EQ_STR("UTF-8 CJK, buffer == Num bytes",
                  Utf8Helpers::DecodeFStringBuffer(kUtf8Cjk, sizeof(kUtf8Cjk), 34),
                  kUtf8CjkExpected);
}

static void Test_Decode_Utf8CjkWithTrailingHeapBytes() {
    // Production reads Num*2 (=68) bytes: 34 real + 34 adjacent heap. These are the
    // real dump bytes that follow (…7B 09 at [66,67]), so this is the realistic
    // shape of the read.
    //
    // audit #5 AD27 — the comment here used to say the heap tail "must NOT put a NUL
    // pair at the UTF-16 terminator position [66,67], or the UTF-16 hypothesis would
    // false-fire", making those two bytes read as load-bearing setup. They are not,
    // and Test_Decode_Utf8MultiByteBeatsZeroHeapTail below asserts the opposite as
    // its whole point: DecodeFStringBuffer's Rule 2 returns on `utf8Ok &&
    // utf8HasMultiByte` BEFORE the UTF-16 hypothesis is evaluated at all, so for a
    // genuine multi-byte UTF-8 buffer the terminator position cannot matter. Two
    // tests in one file disagreeing about the same rule is worse than either being
    // wrong alone, so the claim is dropped and the tail is asserted inert instead.
    uint8_t buf[68];
    for (size_t i = 0; i < 34; ++i) buf[i] = kUtf8Cjk[i];
    for (size_t i = 34; i < 68; ++i) buf[i] = static_cast<uint8_t>(0x40 + (i & 0x1F));
    buf[66] = 0x7B; buf[67] = 0x09;
    EXPECT_EQ_STR("UTF-8 CJK, buffer == Num*2 bytes with heap tail",
                  Utf8Helpers::DecodeFStringBuffer(buf, sizeof(buf), 34),
                  kUtf8CjkExpected);

    // The negative control for the sentence that was removed: zero exactly the pair
    // the old comment said would break it. Same answer, because Rule 2 got there first.
    buf[66] = 0x00; buf[67] = 0x00;
    EXPECT_EQ_STR("...and a NUL pair at the UTF-16 terminator changes nothing (Rule 2)",
                  Utf8Helpers::DecodeFStringBuffer(buf, sizeof(buf), 34),
                  kUtf8CjkExpected);
}

static void Test_Decode_Utf16Cjk() {
    // 中文 as UTF-16LE + NUL unit → num = 3.  中=0x4E2D 文=0x6587.
    const uint8_t buf[] = { 0x2D,0x4E, 0x87,0x65, 0x00,0x00 };
    EXPECT_EQ_STR("UTF-16 CJK 中文",
                  Utf8Helpers::DecodeFStringBuffer(buf, sizeof(buf), 3),
                  "\xE4\xB8\xAD\xE6\x96\x87");
}

static void Test_Decode_Utf16Ascii() {
    // "OK" UTF-16LE + NUL → num = 3. buf[2]='K' (non-zero) so the UTF-8 gate
    // does not fire; UTF-16 wins.
    const uint8_t buf[] = { 'O',0x00, 'K',0x00, 0x00,0x00 };
    EXPECT_EQ_STR("UTF-16 ASCII OK",
                  Utf8Helpers::DecodeFStringBuffer(buf, sizeof(buf), 3), "OK");
}

static void Test_Decode_Utf8Ascii() {
    // "OK" UTF-8 + NUL → num = 3, read as Num*2=6 bytes with heap tail.
    const uint8_t buf[] = { 'O','K',0x00, 0x11,0x22,0x33 };
    EXPECT_EQ_STR("UTF-8 ASCII OK",
                  Utf8Helpers::DecodeFStringBuffer(buf, sizeof(buf), 3), "OK");
}

static void Test_Decode_Utf16AsciiEvenLenInteriorNull() {
    // "ABC" UTF-16LE + NUL → num = 4. buf[n-1]=buf[3]=0x00 (high byte of 'B')
    // WOULD trip a naive UTF-8 terminator check — the interior-null gate
    // (buf[1]=0x00) must route this to the UTF-16 path instead.
    const uint8_t buf[] = { 'A',0x00, 'B',0x00, 'C',0x00, 0x00,0x00 };
    EXPECT_EQ_STR("UTF-16 ASCII ABC (even len, interior null gate)",
                  Utf8Helpers::DecodeFStringBuffer(buf, sizeof(buf), 4), "ABC");
}

// --- B28: UTF-16 CJK buffers the UTF-8 hypothesis used to swallow -----------
//
// Every case here has buf[n-1] == 0 for a reason INSIDE the string's own data — a
// U+xx00 character's low byte, or an ASCII character's high byte — with no interior
// zero before it. The old gate returned the n-byte prefix as UTF-8 and never reached
// the UTF-16 branch. These are ordinary Chinese, not adversarial input.
static void Test_Decode_Utf16Cjk_TrailingZeroLowByte() {
    // 中文一二 : 4E2D 6587 4E00 4E8C + NUL → num = 5.
    // buf[4] = 0x00 is 一's LOW byte, and bytes 0..3 (2D 4E 87 65) have no zero.
    // The UTF-8 prefix "-N\x87e" is NOT well-formed UTF-8 — 0x87 is a continuation
    // byte with no lead — which is what now rejects it. A replacement-ratio test
    // could not: Sanitize gives "-N?e", 1 bad of 4, comfortably inside any ratio.
    const uint8_t buf[] = { 0x2D,0x4E, 0x87,0x65, 0x00,0x4E, 0x8C,0x4E, 0x00,0x00 };
    EXPECT_EQ_STR("UTF-16 中文一二 (U+4E00 low byte at n-1)",
                  Utf8Helpers::DecodeFStringBuffer(buf, sizeof(buf), 5),
                  "\xE4\xB8\xAD\xE6\x96\x87\xE4\xB8\x80\xE4\xBA\x8C");
}

static void Test_Decode_Utf16Cjk_AsciiLookingPrefix() {
    // 第1章 : 7B2C 0031 7AE0 + NUL → num = 4. buf[3] = 0x00 is '1's HIGH byte.
    // The UTF-8 prefix is ",{1" — pure ASCII, well-formed, ZERO replacement chars,
    // so neither well-formedness nor any ratio can reject it. This is the case that
    // forces "evaluate both, prefer the stronger evidence" rather than "return the
    // first hypothesis that passes".
    const uint8_t buf[] = { 0x2C,0x7B, 0x31,0x00, 0xE0,0x7A, 0x00,0x00 };
    EXPECT_EQ_STR("UTF-16 第1章 (ASCII-looking UTF-8 prefix)",
                  Utf8Helpers::DecodeFStringBuffer(buf, sizeof(buf), 4),
                  "\xE7\xAC\xAC" "1" "\xE7\xAB\xA0");
}

static void Test_Decode_Utf16Cjk_MixedAscii() {
    // 中A文 : 4E2D 0041 6587 + NUL → num = 4. buf[3] = 0x00 is 'A's high byte;
    // the UTF-8 prefix "-NA" is again clean ASCII.
    const uint8_t buf[] = { 0x2D,0x4E, 0x41,0x00, 0x87,0x65, 0x00,0x00 };
    EXPECT_EQ_STR("UTF-16 中A文 (mixed ASCII)",
                  Utf8Helpers::DecodeFStringBuffer(buf, sizeof(buf), 4),
                  "\xE4\xB8\xAD" "A" "\xE6\x96\x87");
}

static void Test_Decode_Utf8MultiByteBeatsZeroHeapTail() {
    // The guard on the fix itself. A genuine UTF-8 CJK buffer whose adjacent heap
    // happens to be 0x00 0x00 at exactly the UTF-16 terminator position [2n-2] —
    // the regression the audit warned a naive "prefer UTF-16" would cause. 你好 is
    // 6 UTF-8 bytes + NUL → num = 7, so the UTF-16 terminator would sit at [12].
    // Rule 2 (well-formed AND multi-byte ⇒ UTF-8) settles it before the UTF-16
    // hypothesis is even evaluated.
    uint8_t buf[14] = {
        0xE4,0xBD,0xA0, 0xE5,0xA5,0xBD, 0x00,          // "你好" + NUL
        0x41,0x00, 0x42,0x00, 0x43,0x00,               // plausible-looking tail
        0x00                                            // [12] and [13] are 0x00
    };
    buf[12] = 0x00; buf[13] = 0x00;
    EXPECT_EQ_STR("UTF-8 你好 survives a zero heap tail at the UTF-16 terminator",
                  Utf8Helpers::DecodeFStringBuffer(buf, sizeof(buf), 7),
                  "\xE4\xBD\xA0\xE5\xA5\xBD");
}

// ============================================================
// TruncateUtf8 — audit #5 U7
//
// The property-search preview used to cut with a bare s.resize(50). These tests
// pin the property that actually matters: the RESULT is still well-formed UTF-8,
// checked with IsWellFormedUtf8 rather than by comparing against a hand-written
// expected string — a byte-exact expectation would pass for a helper that cut
// correctly at 50 and still says nothing about limits 49 and 51.
// ============================================================
static void Test_TruncateUtf8_ShorterThanLimitUntouched() {
    EXPECT_EQ_STR("ascii under limit is returned verbatim",
                  Utf8Helpers::TruncateUtf8("hello", 50), "hello");
    // Exactly at the limit is NOT truncation — no ellipsis.
    std::string exact(50, 'x');
    EXPECT_EQ_STR("exactly-at-limit is untouched",
                  Utf8Helpers::TruncateUtf8(exact, 50), exact);
}

static void Test_TruncateUtf8_AsciiCutsExactlyAtLimit() {
    std::string s(80, 'a');
    std::string out = Utf8Helpers::TruncateUtf8(s, 50);
    EXPECT("ascii keeps the full byte budget", out.size() == 50 + 3);  // + U+2026
    EXPECT_EQ_STR("ascii tail is the ellipsis", out.substr(50), "\xe2\x80\xa6");
}

static void Test_TruncateUtf8_NeverSplitsAMultiByteSequence() {
    // "中" is 3 bytes (E4 B8 AD). A 60-byte string of them means every limit that
    // is not a multiple of 3 lands INSIDE a sequence — 2 of every 3 limits.
    std::string cjk;
    for (int i = 0; i < 20; ++i) cjk += "\xe4\xb8\xad";
    EXPECT("fixture is 60 bytes", cjk.size() == 60);

    int splitLimits = 0;
    for (size_t limit = 1; limit < 60; ++limit) {
        std::string out = Utf8Helpers::TruncateUtf8(cjk, limit);
        bool multi = false;
        bool ok = Utf8Helpers::IsWellFormedUtf8(
            reinterpret_cast<const uint8_t*>(out.data()), out.size(), multi);
        if (!ok) ++splitLimits;
        // The cut must never EXCEED the budget, and must lose at most 2 bytes to
        // boundary alignment (the longest back-walk for a 3-byte sequence).
        size_t textBytes = out.size() - 3;   // minus the ellipsis
        EXPECT("cut respects the byte budget", textBytes <= limit);
        EXPECT("cut loses at most 2 bytes to alignment", limit - textBytes <= 2);
    }
    EXPECT("no limit produces invalid UTF-8", splitLimits == 0);

    // The negative control this test exists for: the OLD behaviour, byte-exact
    // resize, must be demonstrably detectable by the same check — otherwise the
    // check above proves nothing. 50 is the limit the shipped call site uses.
    std::string naive = cjk.substr(0, 50);
    naive += "\xe2\x80\xa6";
    bool m2 = false;
    EXPECT("negative control: the old byte-exact cut IS invalid UTF-8",
           !Utf8Helpers::IsWellFormedUtf8(
               reinterpret_cast<const uint8_t*>(naive.data()), naive.size(), m2));
}

static void Test_TruncateUtf8_FourByteSequenceAndMalformedInput() {
    // U+1F600 is 4 bytes — the longest sequence, i.e. the deepest back-walk.
    std::string emoji;
    for (int i = 0; i < 10; ++i) emoji += "\xf0\x9f\x98\x80";
    for (size_t limit = 1; limit < 40; ++limit) {
        std::string out = Utf8Helpers::TruncateUtf8(emoji, limit);
        bool multi = false;
        EXPECT("4-byte sequences survive every limit",
               Utf8Helpers::IsWellFormedUtf8(
                   reinterpret_cast<const uint8_t*>(out.data()), out.size(), multi));
    }

    // Malformed input: a run of continuation bytes longer than any real sequence.
    // The helper must terminate and shorten, not loop or hang — it is not a repairer.
    std::string junk(80, '\x80');
    std::string out = Utf8Helpers::TruncateUtf8(junk, 50);
    EXPECT("malformed input is still shortened", out.size() < junk.size());
}

static void Test_IsWellFormedUtf8() {
    bool multi = false;
    EXPECT("ASCII is well-formed",
           Utf8Helpers::IsWellFormedUtf8(reinterpret_cast<const uint8_t*>("abc"), 3, multi));
    EXPECT("ASCII has no multi-byte", !multi);

    const uint8_t cjk[] = { 0xE4,0xB8,0xAD };          // 中
    EXPECT("3-byte CJK is well-formed", Utf8Helpers::IsWellFormedUtf8(cjk, 3, multi));
    EXPECT("CJK sets hasMultiByte", multi);

    // The exact shape a UTF-16 CJK prefix produces: a continuation byte with no lead.
    const uint8_t lone[] = { 0x2D,0x4E,0x87,0x65 };
    EXPECT("lone continuation byte rejected",
           !Utf8Helpers::IsWellFormedUtf8(lone, 4, multi));

    // audit #5 AD25 — the out-param must not contradict the return value.
    //
    // A VALID multi-byte sequence followed by an INVALID one is the only input that
    // can separate the two: the old code set hasMultiByte=true the instant it accepted
    // 中, then returned false three bytes later, leaving the caller a true flag on a
    // rejected buffer. Seeded true first, so "left untouched" fails the same as
    // "wrongly set" — without the seed a fixed implementation and a no-op are
    // indistinguishable here, since `multi` happens to be true from the CJK case above.
    const uint8_t goodThenBad[] = { 0xE4,0xB8,0xAD, 0xFF };   // 中 then an invalid lead
    multi = true;
    EXPECT("valid multi-byte followed by garbage is rejected",
           !Utf8Helpers::IsWellFormedUtf8(goodThenBad, 4, multi));
    EXPECT("...and hasMultiByte is false on that rejection", !multi);

    // The mirror case: the flag must still be false after a failure that never saw a
    // multi-byte sequence at all, so the fix cannot be "always clear at the top".
    multi = true;
    const uint8_t asciiThenBad[] = { 'a', 0xFF };
    EXPECT("ASCII followed by garbage is rejected",
           !Utf8Helpers::IsWellFormedUtf8(asciiThenBad, 2, multi));
    EXPECT("...and hasMultiByte is false there too", !multi);

    const uint8_t truncated[] = { 0xE4,0xB8 };          // 3-byte lead, only 2 bytes
    EXPECT("truncated sequence rejected",
           !Utf8Helpers::IsWellFormedUtf8(truncated, 2, multi));

    const uint8_t overlong[] = { 0xC0,0x80 };           // overlong NUL
    EXPECT("overlong rejected", !Utf8Helpers::IsWellFormedUtf8(overlong, 2, multi));

    const uint8_t surrogate[] = { 0xED,0xA0,0x80 };     // U+D800 encoded as UTF-8
    EXPECT("surrogate half rejected", !Utf8Helpers::IsWellFormedUtf8(surrogate, 3, multi));

    const uint8_t fiveByte[] = { 0xF8,0x80,0x80,0x80,0x80 };
    EXPECT("5-byte form rejected", !Utf8Helpers::IsWellFormedUtf8(fiveByte, 5, multi));
}

// --- AD5: the ASCII tie — an English FText stored as UTF-8, over zeroed slack ---
//
// Production reads Num*2 bytes, so half the window is adjacent heap. When the heap is
// ZERO-FILLED (the ordinary allocator state) a pure-ASCII UTF-8 string satisfies the
// UTF-16 hypothesis too: the terminator pair lands at [2n-2], every ASCII byte PAIR
// becomes one BMP unit, and the result is valid CJK with ZERO replacement characters —
// so LooksLikeDecodedText accepts it and the wrong hypothesis wins.
//
// The header used to argue this needed two independent coincidences. It needs one:
// zeroed slack supplies both at once, and the mis-decode then fires every time.
//
// Rule 3a settles it structurally — an FString's Num includes its terminator, so a
// UTF-16 reading with an INTERIOR null unit is not an n-unit string.

static void Test_Decode_Utf8AsciiOverZeroedSlack_Short() {
    // "OK" UTF-8 (3 bytes incl. NUL), read as Num*2 = 6 with zeroed heap.
    // Before the fix this returned U+4B4F ("䭏"): 'O' | 'K'<<8.
    const uint8_t buf[] = { 'O','K',0x00, 0x00,0x00,0x00 };
    EXPECT_EQ_STR("UTF-8 ASCII over zeroed slack (OK)",
                  Utf8Helpers::DecodeFStringBuffer(buf, sizeof(buf), 3), "OK");
}

static void Test_Decode_Utf8AsciiOverZeroedSlack_Word() {
    // "Continue" — the reported case. 9 bytes incl. NUL, read as 18.
    // Before the fix: U+6F43 U+746E U+6E69 U+6575 ("潃湴極eu"-shaped mojibake).
    const uint8_t buf[] = {
        'C','o','n','t','i','n','u','e',0x00,
        0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00
    };
    EXPECT_EQ_STR("UTF-8 ASCII over zeroed slack (Continue)",
                  Utf8Helpers::DecodeFStringBuffer(buf, sizeof(buf), 9), "Continue");
}

static void Test_Decode_Utf8AsciiOverZeroedSlack_WithSpace() {
    // Odd byte count, and a space — LooksLikeDecodedText treats spaces as textual,
    // so this is squarely the case the old "rejects a binary tail" defence claimed
    // to cover. "Press Start" = 12 bytes incl. NUL, read as 24.
    const uint8_t buf[] = {
        'P','r','e','s','s',' ','S','t','a','r','t',0x00,
        0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00
    };
    EXPECT_EQ_STR("UTF-8 ASCII over zeroed slack (Press Start)",
                  Utf8Helpers::DecodeFStringBuffer(buf, sizeof(buf), 12), "Press Start");
}

// --- AD5 controls: the tie-break must not reach anything that is not the tie ---

static void Test_Decode_TieBreakDoesNotTouchGenuineUtf16() {
    // Genuine UTF-16 "OK": buf[n-1] = 'K' != 0, so the UTF-8 hypothesis never even
    // forms and utf8Ok is false — the tie-break is gated off entirely. If the gate
    // were ungated this would still pass, which is why the next control exists.
    const uint8_t buf[] = { 'O',0x00, 'K',0x00, 0x00,0x00 };
    EXPECT_EQ_STR("genuine UTF-16 unaffected by the tie-break",
                  Utf8Helpers::DecodeFStringBuffer(buf, sizeof(buf), 3), "OK");
}

static void Test_Decode_TieBreakIsGatedOnUtf8Success() {
    // THE CONTROL FOR THE GATE ITSELF. A UTF-16 buffer carrying a real interior zero
    // unit, where the UTF-8 hypothesis FAILS (0x87 is a lone continuation byte, so the
    // bytes are not well-formed UTF-8). An UNGATED interior-zero-unit rule would reject
    // this reading too and change the answer; because the rule is gated on utf8Ok it
    // does not run, and the decode is byte-identical to before the fix.
    // 中<NUL>二 : 4E2D 0000 4E8C + NUL, with a non-UTF-8 first pair.
    const uint8_t buf[] = { 0x2D,0x4E, 0x00,0x00, 0x8C,0x4E, 0x00,0x00 };
    const std::string got = Utf8Helpers::DecodeFStringBuffer(buf, sizeof(buf), 4);
    EXPECT("interior-zero UTF-16 still decodes (tie-break stayed out)", !got.empty());
}

static void Test_Decode_MultiByteStillWinsOverZeroedSlack() {
    // Rule 2 must keep priority: a well-formed multi-byte sequence settles the width
    // before the tie-break is ever consulted. "中" = E4 B8 AD + NUL, zeroed slack.
    const uint8_t buf[] = { 0xE4,0xB8,0xAD,0x00, 0x00,0x00,0x00,0x00 };
    EXPECT_EQ_STR("UTF-8 multi-byte beats zeroed slack",
                  Utf8Helpers::DecodeFStringBuffer(buf, sizeof(buf), 4), "\xE4\xB8\xAD");
}

static void Test_Decode_RejectsEmptyAndGarbage() {
    const uint8_t buf[] = { 0x00,0x00,0x00,0x00 };
    EXPECT_EQ_STR("num < 2 is empty",
                  Utf8Helpers::DecodeFStringBuffer(buf, sizeof(buf), 1), "");
    EXPECT_EQ_STR("null buffer is empty",
                  Utf8Helpers::DecodeFStringBuffer(nullptr, 10, 5), "");
    // No terminator at either candidate position → "".
    const uint8_t noTerm[] = { 0x41,0x42,0x43,0x44,0x45,0x46 };
    EXPECT_EQ_STR("no terminator anywhere → empty",
                  Utf8Helpers::DecodeFStringBuffer(noTerm, sizeof(noTerm), 3), "");
    // UTF-16 buffer whose decode is mostly replacement chars → rejected by
    // LooksLikeDecodedText (lone surrogates everywhere).
    const uint8_t surrogates[] = { 0x00,0xD8, 0x00,0xD8, 0x00,0xD8, 0x00,0x00 };
    EXPECT_EQ_STR("mostly-replacement UTF-16 rejected",
                  Utf8Helpers::DecodeFStringBuffer(surrogates, sizeof(surrogates), 4), "");
}

static void Test_LooksLikeDecodedText() {
    EXPECT("plain text looks like text",
           Utf8Helpers::LooksLikeDecodedText("hello"));
    EXPECT("CJK looks like text",
           Utf8Helpers::LooksLikeDecodedText("\xE4\xB8\xAD\xE6\x96\x87"));
    EXPECT("one literal ? in a sentence still passes",
           Utf8Helpers::LooksLikeDecodedText("Ready?"));
    EXPECT("all replacements rejected",
           !Utf8Helpers::LooksLikeDecodedText("????"));
    EXPECT("empty rejected", !Utf8Helpers::LooksLikeDecodedText(""));
}

// ----- main ------------------------------------------------------------------

int main() {
    std::printf("Utf8Helpers self-test\n");
    std::printf("---------------------\n");

    Test_Sanitize_AsciiPassthrough();
    Test_Sanitize_RejectsControlBytes();
    Test_Sanitize_ValidMultiBytePassthrough();
    Test_Sanitize_RejectsLoneContinuation();
    Test_Sanitize_RejectsSurrogateEncodings();
    Test_Sanitize_RejectsOverlongs();
    Test_Sanitize_RejectsTruncated();
    Test_Sanitize_MixedGoodAndBad();
    Test_Sanitize_Idempotent();

    Test_EncodeUtf16_AsciiPassthrough();
    Test_EncodeUtf16_NullTerminationStops();
    Test_EncodeUtf16_BMPCharacters();
    Test_EncodeUtf16_SurrogatePair();
    Test_EncodeUtf16_MultipleSurrogatePairs();
    Test_EncodeUtf16_LoneSurrogates();
    Test_EncodeUtf16_ReversedSurrogates();
    Test_EncodeUtf16_MixedRealistic();
    Test_EncodeUtf16_OutputAlwaysValidUtf8();

    Test_ImplausibleWide_RejectsAsciiAsUtf16Mojibake();
    Test_ImplausibleWide_AcceptsShortLocalized();
    Test_ImplausibleWide_AcceptsAsciiAndEmpty();
    Test_ImplausibleWide_LengthAndDensityRules();

    Test_SanitizeAnsi_CleanNamePassthrough();
    Test_SanitizeAnsi_RejectsJunkRun();
    Test_SanitizeAnsi_NulAndBounds();

    Test_Decode_Utf8CjkExactBuffer();
    Test_Decode_Utf8CjkWithTrailingHeapBytes();
    Test_Decode_Utf16Cjk();
    Test_Decode_Utf16Ascii();
    Test_Decode_Utf8Ascii();
    Test_Decode_Utf16AsciiEvenLenInteriorNull();
    Test_Decode_RejectsEmptyAndGarbage();
    // B28 — UTF-16 CJK buffers the UTF-8 hypothesis used to swallow
    Test_Decode_Utf16Cjk_TrailingZeroLowByte();
    Test_Decode_Utf16Cjk_AsciiLookingPrefix();
    Test_Decode_Utf16Cjk_MixedAscii();
    Test_Decode_Utf8MultiByteBeatsZeroHeapTail();
    Test_Decode_Utf8AsciiOverZeroedSlack_Short();
    Test_Decode_Utf8AsciiOverZeroedSlack_Word();
    Test_Decode_Utf8AsciiOverZeroedSlack_WithSpace();
    Test_Decode_TieBreakDoesNotTouchGenuineUtf16();
    Test_Decode_TieBreakIsGatedOnUtf8Success();
    Test_Decode_MultiByteStillWinsOverZeroedSlack();
    Test_IsWellFormedUtf8();
    Test_LooksLikeDecodedText();

    // U7 — character-boundary truncation for the property-search preview
    Test_TruncateUtf8_ShorterThanLimitUntouched();
    Test_TruncateUtf8_AsciiCutsExactlyAtLimit();
    Test_TruncateUtf8_NeverSplitsAMultiByteSequence();
    Test_TruncateUtf8_FourByteSequenceAndMalformedInput();

    std::printf("---------------------\n");
    std::printf("Pass: %d   Fail: %d\n", g_pass, g_fail);
    return g_fail;
}
