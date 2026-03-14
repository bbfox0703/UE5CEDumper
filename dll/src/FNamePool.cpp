// ============================================================
// FNamePool.cpp — UE FNamePool / TNameEntryArray string resolution
// Supports UE4.25+ (FNamePool) and UE5 (FNamePool with varying chunk bits)
// Also supports UE4.26-style hash-prefixed FNameEntry (4B hash + 2B header)
// ============================================================

#include "FNamePool.h"
#include "Memory.h"
#define LOG_CAT "FNAM"
#include "Logger.h"
#include "Constants.h"

#include <atomic>
#include <cstring>
#include <vector>
#include <string>

namespace FNamePool {

static uintptr_t s_poolAddr = 0;
static std::atomic<bool> s_initialized{false};

// UE4 TNameEntryArray mode (pre-FNamePool)
static bool s_isUE4Mode = false;
static int  s_ue4StringOffset = 0x10;  // Offset within FNameEntry to null-terminated string
static constexpr int UE4_CHUNK_SIZE = 0x4000; // 16384 entries per chunk

// Header format detection
// UE has changed the FNameEntry header format between versions:
// Format A (older UE4/early UE5): bit 0 = wide flag, bits 6..15 = length
// Format B (newer UE5):           bit 0 = wide flag, bits 1..11 = length
static int s_lenShift = 6;     // Default: bits >> 6
static int s_lenMask  = 0x3FF; // 10 bits of length
static int s_wideFlag = 0;     // Bit index for wide flag

// FNameEntry header offset: number of bytes before the 2-byte header within each entry.
// Standard UE5: 0 (entry = [2B header][string])
// Hash-prefixed UE4.26 (FF7Re): 4 (entry = [4B hash][2B header][string])
static int s_headerOffset = 0;

// FNameEntry alignment stride: used to convert FName index offset to byte offset.
// Standard UE5: 2 (alignof(FNameEntry) with uint16 header)
// Hash-prefixed UE4.26: 4 (alignof(FNameEntry) with uint32 hash + uint16 header)
// Formula: entry_byte_offset = (nameIndex & blockOffsetMask) * s_stride
static int s_stride = 2;

// FNameBlockOffsetBits: how many bits of the FName index are used for the within-chunk offset.
// Standard UE5: 16 bits (chunkIndex = nameIndex >> 16, offset = (nameIndex & 0xFFFF) * stride)
// Some UE4 builds: 14 bits
// This is auto-detected in DetectBlockOffsetBits().
static int s_blockOffsetBits = 16;
static int s_blockOffsetMask = 0xFFFF;

// The FNamePool stores chunk pointers after an initial header
// Typical layout: lock (8 bytes), CurrentBlock+CurrentByteCursor (8 bytes), then Blocks[] array
// But the address from AOB often points directly to the chunk array or to pool start
static int s_chunksOffset = 0; // Offset from pool address to chunk pointer array

// Helper: try to decode a "None" FNameEntry at the given address with specified header offset.
// Returns true if successfully verified.
static bool TryDecodeNone(uintptr_t entryAddr, int hdrOff) {
    uint16_t header = 0;
    if (!Mem::ReadSafe(entryAddr + hdrOff, header)) return false;

    auto tryFormat = [&](int shift, int mask) -> bool {
        int len = (header >> shift) & mask;
        if (len != 4) return false;
        char name[5] = {};
        if (!Mem::ReadBytesSafe(entryAddr + hdrOff + 2, name, 4)) return false;
        return strcmp(name, "None") == 0;
    };

    // Format A: len = header >> 6 (10 bits)
    if (tryFormat(6, 0x3FF)) return true;
    // Format B: len = (header >> 1) & 0x7FF (11 bits)
    if (tryFormat(1, 0x7FF)) return true;

    return false;
}

// Internal GetEntry that works during Init() before s_initialized is set.
// Only for use within Init() — external callers should use the public GetEntry().
static uintptr_t GetEntryInternal(int32_t nameIndex) {
    if (!s_poolAddr) return 0;

    if (s_isUE4Mode) {
        int32_t chunkIndex = nameIndex / UE4_CHUNK_SIZE;
        int32_t elemIndex  = nameIndex % UE4_CHUNK_SIZE;
        if (chunkIndex < 0 || chunkIndex > 256) return 0;
        uintptr_t chunkPtr = 0;
        if (!Mem::ReadSafe(s_poolAddr + chunkIndex * sizeof(uintptr_t), chunkPtr) || !chunkPtr) return 0;
        uintptr_t entryPtr = 0;
        if (!Mem::ReadSafe(chunkPtr + elemIndex * sizeof(uintptr_t), entryPtr)) return 0;
        return entryPtr;
    }

    int32_t chunkIndex  = nameIndex >> s_blockOffsetBits;
    int32_t chunkOffset = (nameIndex & s_blockOffsetMask) * s_stride;
    if (chunkIndex < 0 || chunkIndex > 8192) return 0;
    uintptr_t chunkPtr = 0;
    uintptr_t chunksBase = s_poolAddr + s_chunksOffset;
    if (!Mem::ReadSafe(chunksBase + chunkIndex * sizeof(uintptr_t), chunkPtr) || !chunkPtr) return 0;
    return chunkPtr + chunkOffset;
}

static void DetectHeaderFormat() {
    // Try to read FNameEntry at index 0 (should be "None", length 4)
    // Use GetEntryInternal since s_initialized is not yet set during Init().
    uintptr_t entry = GetEntryInternal(0);
    if (!entry) return;

    // The header is at entry + s_headerOffset
    uint16_t header = 0;
    if (!Mem::ReadSafe(entry + s_headerOffset, header)) return;

    // Try Format A: len = header >> 6
    int lenA = header >> 6;
    if (lenA == 4) {
        char name[5] = {};
        if (Mem::ReadBytesSafe(entry + s_headerOffset + 2, name, 4) && strcmp(name, "None") == 0) {
            s_lenShift = 6;
            s_lenMask = 0x3FF;
            s_wideFlag = 0;
            LOG_INFO("FNamePool: Header Format A (shift=6, hdrOff=%d), verified 'None'", s_headerOffset);
            return;
        }
    }

    // Try Format B: len = (header >> 1) & 0x7FF
    int lenB = (header >> 1) & 0x7FF;
    if (lenB == 4) {
        char name[5] = {};
        if (Mem::ReadBytesSafe(entry + s_headerOffset + 2, name, 4) && strcmp(name, "None") == 0) {
            s_lenShift = 1;
            s_lenMask = 0x7FF;
            s_wideFlag = 0;
            LOG_INFO("FNamePool: Header Format B (shift=1, hdrOff=%d), verified 'None'", s_headerOffset);
            return;
        }
    }

    LOG_WARN("FNamePool: Could not auto-detect header format, using default (shift=6)");
}

// Validate a candidate chunk pointer by checking if entry 0 contains "None".
// Returns true if the chunk at (poolAddr + chunksOffset)[0] -> FNameEntry looks like "None".
static bool ValidateChunkForNone(uintptr_t poolAddr, int chunksOffset) {
    uintptr_t chunk0 = 0;
    if (!Mem::ReadSafe(poolAddr + chunksOffset, chunk0) || !chunk0) return false;

    // Pointer sanity
    if (chunk0 < 0x10000 || chunk0 > 0x00007FFFFFFFFFFF) return false;

    // Entry 0 is at chunk0 + 0 (nameIndex=0 -> chunkIndex=0, offset=0)
    // Try with current header offset first, then try both 0 and 4
    if (TryDecodeNone(chunk0, s_headerOffset)) return true;
    if (s_headerOffset != 0 && TryDecodeNone(chunk0, 0)) return true;
    if (s_headerOffset != 4 && TryDecodeNone(chunk0, 4)) return true;

    return false;
}

static void DetectChunksOffset() {
    // FNamePool layout varies across UE versions:
    //   Standard: Lock(8) + CurrentBlock(4) + CurrentByteCursor(4) + Blocks[]  -> Blocks at +0x10
    //   Some:     Blocks at +0x00 (address points directly to chunk array)
    //   Others:   Blocks at +0x08, +0x20, +0x40
    //
    // We validate each candidate by checking if Blocks[0] -> entry[0] == "None".
    // This avoids false positives from FRWLock or other non-zero values at offset 0.

    // Prioritize 0x10 (standard layout) first, then try others including 0
    int offsets[] = { 0x10, 0x00, 0x08, 0x20, 0x40 };
    for (int off : offsets) {
        if (ValidateChunkForNone(s_poolAddr, off)) {
            s_chunksOffset = off;
            LOG_INFO("FNamePool: Chunks at offset 0x%X (validated with 'None')", off);
            return;
        }
    }

    // Fallback: try any offset that has a readable pointer (less reliable)
    LOG_WARN("FNamePool: 'None' validation failed for all offsets, trying readable-pointer fallback");
    for (int off : offsets) {
        uintptr_t chunk0 = 0;
        if (Mem::ReadSafe(s_poolAddr + off, chunk0) && chunk0 != 0 &&
            chunk0 > 0x10000 && chunk0 < 0x00007FFFFFFFFFFF) {
            uint16_t testHeader = 0;
            if (Mem::ReadSafe(chunk0, testHeader)) {
                s_chunksOffset = off;
                LOG_WARN("FNamePool: Chunks at offset 0x%X (unvalidated fallback)", off);
                return;
            }
        }
    }

    LOG_ERROR("FNamePool: Could not detect chunks offset at all");
    s_chunksOffset = 0x10; // Best guess: standard layout
}

// Auto-detect FNameBlockOffsetBits by trying different bit widths.
// Standard UE5 uses 16 bits. Some UE4 builds use 14 bits.
// We test by reading FName entries at known indices and checking if they produce valid strings.
static void DetectBlockOffsetBits() {
    // Default: 16 bits (covers most UE5 games)
    // Try 16 first, then 14 (some UE4 games)
    int candidates[] = { 16, 14 };

    for (int bits : candidates) {
        int mask = (1 << bits) - 1;

        // Test: index 1 should produce a non-empty, valid ASCII string
        int32_t testIdx = 1;
        int32_t ci = testIdx >> bits;
        int32_t co = (testIdx & mask) * s_stride;

        uintptr_t chunkPtr = 0;
        uintptr_t chunksBase = s_poolAddr + s_chunksOffset;
        if (!Mem::ReadSafe(chunksBase + ci * sizeof(uintptr_t), chunkPtr) || !chunkPtr) continue;

        uintptr_t entry = chunkPtr + co;

        // Read header at the detected header offset
        uint16_t header = 0;
        if (!Mem::ReadSafe(entry + s_headerOffset, header)) continue;

        // Try both header formats
        auto tryLen = [&](int shift, int lenMask) -> int {
            return (header >> shift) & lenMask;
        };

        int lenA = tryLen(6, 0x3FF);
        int lenB = tryLen(1, 0x7FF);

        // Accept if either format gives a plausible length (1-256)
        int len = (lenA >= 1 && lenA <= 256) ? lenA : ((lenB >= 1 && lenB <= 256) ? lenB : 0);
        if (len <= 0) continue;

        // Read the string and check if it's valid ASCII
        // String starts at entry + s_headerOffset + 2
        char buf[8] = {};
        int readLen = len > 7 ? 7 : len;
        if (!Mem::ReadBytesSafe(entry + s_headerOffset + 2, buf, readLen)) continue;

        bool valid = true;
        for (int i = 0; i < readLen; ++i) {
            auto c = static_cast<unsigned char>(buf[i]);
            if (c < 0x20 || c >= 0x7F) { valid = false; break; }
        }

        if (valid) {
            s_blockOffsetBits = bits;
            s_blockOffsetMask = mask;
            LOG_INFO("FNamePool: BlockOffsetBits = %d (FName[1] len=%d, str='%.7s')", bits, len, buf);
            return;
        }
    }

    // Keep default
    LOG_INFO("FNamePool: BlockOffsetBits = %d (default)", s_blockOffsetBits);
}

// Auto-detect FNameEntry stride by scanning chunk[0] for the "None" string.
// Dumper-7 approach: the byte offset where "None" appears = FNameEntryHeaderSize.
// Stride = (headerSize == 2) ? 2 : 4.
// Must be called after DetectChunksOffset() so s_chunksOffset is set.
static void DetectStride() {
    uintptr_t chunksBase = s_poolAddr + s_chunksOffset;
    uintptr_t chunk0 = 0;
    if (!Mem::ReadSafe(chunksBase, chunk0) || !chunk0) return;

    // Scan first 16 bytes of chunk[0] for "None" (0x4E6F6E65)
    // The offset where "None" appears tells us the header size.
    constexpr uint32_t NoneAsU32 = 0x656E6F4E; // "None" in little-endian
    char buf[16] = {};
    if (!Mem::ReadBytesSafe(chunk0, buf, 16)) return;

    int noneOffset = -1;
    for (int i = 0; i <= 12; ++i) {
        uint32_t val = 0;
        memcpy(&val, buf + i, 4);
        if (val == NoneAsU32) {
            noneOffset = i;
            break;
        }
    }

    if (noneOffset < 0) {
        LOG_WARN("FNamePool: Could not find 'None' in chunk[0], keeping stride=%d", s_stride);
        return;
    }

    // noneOffset = FNameEntryHeaderSize (bytes before the string starts)
    // headerSize 2 → standard UE5 [2B header][string], stride 2
    // headerSize 4 → could be hash-prefixed [4B][string] or padded header, stride 4
    // headerSize 6 → hash-prefixed [4B hash][2B header][string], stride 4
    int newStride = (noneOffset <= 2) ? 2 : 4;

    if (newStride != s_stride) {
        LOG_INFO("FNamePool: DetectStride: 'None' at chunk[0]+%d, changing stride %d -> %d",
                 noneOffset, s_stride, newStride);
        s_stride = newStride;
    } else {
        LOG_INFO("FNamePool: DetectStride: 'None' at chunk[0]+%d, stride=%d confirmed", noneOffset, s_stride);
    }
}

void Init(uintptr_t gnamesAddr, int headerOffset) {
    s_poolAddr = gnamesAddr;
    s_isUE4Mode = false;
    s_headerOffset = headerOffset;

    // Initial stride guess based on header offset.
    // Hash-prefixed entries (headerOffset=4) have uint32_t ComparisonId as first member,
    // raising alignof(FNameEntry) to 4 (vs 2 for standard entries with uint16_t header).
    s_stride = (headerOffset >= 4) ? 4 : Constants::FNAME_STRIDE;
    LOG_INFO("FNamePool: Entry stride = %d (initial, hdrOff=%d)", s_stride, headerOffset);

    DetectChunksOffset();
    DetectStride(); // Refine stride by scanning chunk[0] for "None"
    DetectBlockOffsetBits();
    DetectHeaderFormat();

    // Mark initialized with release ordering — fences all preceding writes so
    // any thread that acquire-loads s_initialized==true sees consistent state.
    s_initialized.store(true, std::memory_order_release);

    // Verify by reading a few known names
    std::string none = GetString(0);
    std::string name1 = GetString(1);
    LOG_INFO("FNamePool: Initialized at 0x%llX (hdrOff=%d, stride=%d), FName[0]='%s', FName[1]='%s'",
             static_cast<unsigned long long>(gnamesAddr), headerOffset, s_stride,
             none.c_str(), name1.c_str());
}

void InitUE4(uintptr_t nameArrayAddr, int stringOffset) {
    s_poolAddr = nameArrayAddr;
    s_isUE4Mode = true;
    s_ue4StringOffset = stringOffset;

    // Mark initialized with release ordering
    s_initialized.store(true, std::memory_order_release);

    // Verify by reading name at index 0 ("None")
    std::string none = GetString(0);
    std::string name1 = GetString(1);
    LOG_INFO("FNamePool: InitUE4 at 0x%llX (strOff=0x%X), FName[0]='%s', FName[1]='%s'",
             static_cast<unsigned long long>(nameArrayAddr), stringOffset,
             none.c_str(), name1.c_str());
}

uintptr_t GetEntry(int32_t nameIndex) {
    if (!s_initialized.load(std::memory_order_acquire) || !s_poolAddr) return 0;

    if (s_isUE4Mode) {
        // UE4 TNameEntryArray: Chunks[chunkIdx] -> FNameEntry*[UE4_CHUNK_SIZE]
        // Double dereference: array -> chunk -> entry pointer
        int32_t chunkIndex = nameIndex / UE4_CHUNK_SIZE;
        int32_t elemIndex  = nameIndex % UE4_CHUNK_SIZE;

        // Bounds guard: UE4 typically has < 256 chunks (256 * 16384 = 4M names max)
        if (chunkIndex < 0 || chunkIndex > 256) return 0;

        // Read chunk pointer from array
        uintptr_t chunkPtr = 0;
        if (!Mem::ReadSafe(s_poolAddr + chunkIndex * sizeof(uintptr_t), chunkPtr) || !chunkPtr)
            return 0;

        // Each element in the chunk is a pointer to FNameEntry
        uintptr_t entryPtr = 0;
        if (!Mem::ReadSafe(chunkPtr + elemIndex * sizeof(uintptr_t), entryPtr))
            return 0;

        return entryPtr;
    }

    // UE5 FNamePool path: packed inline entries with 2-byte header
    int32_t chunkIndex  = nameIndex >> s_blockOffsetBits;
    int32_t chunkOffset = (nameIndex & s_blockOffsetMask) * s_stride;

    // Bounds guard: FNamePool typically has < 8192 chunks (each 128KB = 1GB total max)
    if (chunkIndex < 0 || chunkIndex > 8192) return 0;

    // Read chunk pointer
    uintptr_t chunkPtr = 0;
    uintptr_t chunksBase = s_poolAddr + s_chunksOffset;
    if (!Mem::ReadSafe(chunksBase + chunkIndex * sizeof(uintptr_t), chunkPtr) || !chunkPtr) {
        return 0;
    }

    return chunkPtr + chunkOffset;
}

std::string GetString(int32_t nameIndex, int32_t number) {
    if (nameIndex <= 0 && number == 0) return "None";

    uintptr_t entry = GetEntry(nameIndex);
    if (!entry) return "";

    if (s_isUE4Mode) {
        // UE4: null-terminated string at fixed offset within FNameEntry
        char buf[256] = {};
        if (!Mem::ReadBytesSafe(entry + s_ue4StringOffset, buf, 255)) return "";
        buf[255] = '\0';

        // Sanitize: ensure valid ASCII
        std::string result;
        result.reserve(64);
        for (int i = 0; i < 255 && buf[i]; ++i) {
            auto c = static_cast<unsigned char>(buf[i]);
            if (c >= 0x20 && c < 0x7F) {
                result += static_cast<char>(c);
            } else {
                result += '?';
            }
        }

        if (number > 0) {
            result += "_" + std::to_string(number - 1);
        }
        return result;
    }

    // UE5/UE4.23+ FNamePool path:
    // Entry layout: [s_headerOffset bytes prefix][2-byte header][string data]
    // s_headerOffset is 0 for standard UE5, 4 for hash-prefixed UE4.26
    uint16_t header = 0;
    if (!Mem::ReadSafe(entry + s_headerOffset, header)) return "";

    int len = (header >> s_lenShift) & s_lenMask;
    bool isWide = (header & (1 << s_wideFlag)) != 0;

    if (len <= 0 || len > 1024) return "";

    // String starts at entry + s_headerOffset + 2
    int strStart = s_headerOffset + 2;

    std::string result;

    if (isWide) {
        // Wide character name — convert UTF-16 (wchar_t on Windows) to UTF-8.
        // UE FNames can store localized display names (e.g., Japanese) in wide format.
        std::vector<wchar_t> wbuf(len + 1, 0);
        if (!Mem::ReadBytesSafe(entry + strStart, wbuf.data(), len * sizeof(wchar_t))) return "";
        result.reserve(len * 3); // worst case: 3 bytes per BMP char
        for (int i = 0; i < len; ++i) {
            auto ch = static_cast<uint32_t>(wbuf[i]);
            if (ch == 0) break;
            if (ch < 0x80) {
                result += static_cast<char>(ch);
            } else if (ch < 0x800) {
                result += static_cast<char>(0xC0 | (ch >> 6));
                result += static_cast<char>(0x80 | (ch & 0x3F));
            } else {
                // BMP character (0x800..0xFFFF) — covers CJK, etc.
                result += static_cast<char>(0xE0 | (ch >> 12));
                result += static_cast<char>(0x80 | ((ch >> 6) & 0x3F));
                result += static_cast<char>(0x80 | (ch & 0x3F));
            }
        }
    } else {
        // ANSI name — sanitize non-ASCII bytes to produce valid UTF-8.
        // UE FNames should be pure ASCII; non-ASCII means corrupted/encrypted data.
        std::vector<char> buf(len + 1, 0);
        if (!Mem::ReadBytesSafe(entry + strStart, buf.data(), len)) return "";
        result.reserve(len);
        for (int i = 0; i < len; ++i) {
            auto c = static_cast<unsigned char>(buf[i]);
            if (c >= 0x20 && c < 0x7F) {
                result += static_cast<char>(c);
            } else if (c == 0) {
                break; // Null terminator
            } else {
                result += '?';
            }
        }
    }

    // Append _N suffix if number > 0
    if (number > 0) {
        result += "_" + std::to_string(number - 1);
    }

    return result;
}

bool IsInitialized() {
    return s_initialized.load(std::memory_order_acquire);
}

bool IsUE4Mode() {
    return s_isUE4Mode;
}

void LogDiagnostics() {
    // One-shot: only run once per session to avoid flooding logs
    static std::atomic<bool> s_diagDone{false};
    if (s_diagDone.exchange(true)) return;

    if (!s_initialized.load(std::memory_order_acquire)) {
        LOG_WARN("FNamePool::LogDiagnostics: not initialized");
        s_diagDone = false; // Allow retry
        return;
    }

    LOG_INFO("FNamePool Diagnostics:");
    LOG_INFO("  poolAddr=0x%llX, chunksOffset=0x%X, stride=%d, blockOffsetBits=%d, "
             "headerOffset=%d, lenShift=%d, lenMask=0x%X, isUE4=%d",
             (unsigned long long)s_poolAddr, s_chunksOffset, s_stride, s_blockOffsetBits,
             s_headerOffset, s_lenShift, s_lenMask, s_isUE4Mode ? 1 : 0);

    if (s_isUE4Mode) return;

    // Count valid chunk pointers
    uintptr_t chunksBase = s_poolAddr + s_chunksOffset;
    int maxValidChunk = -1;
    int totalValidChunks = 0;
    for (int i = 0; i <= 512; ++i) {
        uintptr_t chunkPtr = 0;
        if (Mem::ReadSafe(chunksBase + i * sizeof(uintptr_t), chunkPtr) && chunkPtr != 0) {
            ++totalValidChunks;
            maxValidChunk = i;
        }
    }
    LOG_INFO("  Chunk scan: %d valid chunks, max index=%d", totalValidChunks, maxValidChunk);

    // Probe sample of entries across different chunks and track failure reasons
    struct FailStats {
        int entryNull = 0;        // GetEntry returned 0
        int headerReadFail = 0;   // Couldn't read header
        int lenZero = 0;          // Decoded length was 0
        int lenOverflow = 0;      // Decoded length > 1024
        int stringReadFail = 0;   // Couldn't read string bytes
        int emptyResult = 0;      // All non-printable characters
        int success = 0;
        int total = 0;
    } stats;

    // Test entries at various chunk:offset combinations
    // Generate test indices spanning different chunks
    std::vector<int32_t> testIndices;
    for (int chunk = 0; chunk <= maxValidChunk && chunk <= 100; ++chunk) {
        // Test a few offsets within each chunk
        for (int off = 1; off < 200; off += 47) {
            int32_t idx = (chunk << s_blockOffsetBits) | off;
            testIndices.push_back(idx);
        }
    }

    int firstFailures = 0;
    for (int32_t idx : testIndices) {
        ++stats.total;
        uintptr_t entry = GetEntry(idx);
        if (!entry) { ++stats.entryNull; continue; }

        uint16_t header = 0;
        if (!Mem::ReadSafe(entry + s_headerOffset, header)) { ++stats.headerReadFail; continue; }

        int len = (header >> s_lenShift) & s_lenMask;
        if (len <= 0) {
            ++stats.lenZero;
            if (firstFailures < 5) {
                LOG_INFO("  FAIL idx=0x%08X chunk=%d off=%d entry=0x%llX header=0x%04X len=0",
                         idx, idx >> s_blockOffsetBits, idx & s_blockOffsetMask,
                         (unsigned long long)entry, header);
                ++firstFailures;
            }
            continue;
        }
        if (len > 1024) {
            ++stats.lenOverflow;
            if (firstFailures < 5) {
                LOG_INFO("  FAIL idx=0x%08X chunk=%d off=%d entry=0x%llX header=0x%04X len=%d (overflow)",
                         idx, idx >> s_blockOffsetBits, idx & s_blockOffsetMask,
                         (unsigned long long)entry, header, len);
                ++firstFailures;
            }
            continue;
        }

        char buf[8] = {};
        int readLen = (len > 7) ? 7 : len;
        if (!Mem::ReadBytesSafe(entry + s_headerOffset + 2, buf, readLen)) { ++stats.stringReadFail; continue; }

        bool hasValid = false;
        for (int i = 0; i < readLen; ++i) {
            auto c = static_cast<unsigned char>(buf[i]);
            if (c >= 0x20 && c < 0x7F) { hasValid = true; break; }
        }
        if (!hasValid) { ++stats.emptyResult; continue; }

        ++stats.success;
    }

    LOG_INFO("  Probe results (%d tested): success=%d, entryNull=%d, headerFail=%d, "
             "lenZero=%d, lenOverflow=%d, stringFail=%d, empty=%d",
             stats.total, stats.success, stats.entryNull, stats.headerReadFail,
             stats.lenZero, stats.lenOverflow, stats.stringReadFail, stats.emptyResult);

    // Verify a few specific high-value entries (common engine names)
    for (int32_t testIdx : { 1, 2, 3, 10, 100, 1000 }) {
        std::string s = GetString(testIdx);
        LOG_INFO("  FName[%d] = '%s'", testIdx, s.c_str());
    }

    // Dump first 16 bytes of chunks 0 and 1 for visual inspection
    for (int c = 0; c <= 1 && c <= maxValidChunk; ++c) {
        uintptr_t chunkPtr = 0;
        if (Mem::ReadSafe(chunksBase + c * sizeof(uintptr_t), chunkPtr) && chunkPtr) {
            uint8_t dump[32] = {};
            Mem::ReadBytesSafe(chunkPtr, dump, 32);
            LOG_INFO("  Chunk[%d] @0x%llX: %02X %02X %02X %02X %02X %02X %02X %02X "
                     "%02X %02X %02X %02X %02X %02X %02X %02X "
                     "%02X %02X %02X %02X %02X %02X %02X %02X "
                     "%02X %02X %02X %02X %02X %02X %02X %02X",
                     c, (unsigned long long)chunkPtr,
                     dump[0], dump[1], dump[2], dump[3], dump[4], dump[5], dump[6], dump[7],
                     dump[8], dump[9], dump[10], dump[11], dump[12], dump[13], dump[14], dump[15],
                     dump[16], dump[17], dump[18], dump[19], dump[20], dump[21], dump[22], dump[23],
                     dump[24], dump[25], dump[26], dump[27], dump[28], dump[29], dump[30], dump[31]);
        }
    }
}

} // namespace FNamePool
