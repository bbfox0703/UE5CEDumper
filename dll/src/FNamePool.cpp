// ============================================================
// FNamePool.cpp — UE5 FNamePool string resolution
// ============================================================

#include "FNamePool.h"
#include "Memory.h"
#define LOG_CAT "FNAM"
#include "Logger.h"
#include "Constants.h"

#include <cstring>
#include <vector>
#include <string>

namespace FNamePool {

static uintptr_t s_poolAddr = 0;
static bool       s_initialized = false;

// Header format detection
// UE5 has changed the FNameEntry header format between versions:
// Format A (older): bit 0 = wide flag, bits 6..15 = length
// Format B (newer): bit 0 = wide flag, bits 1..11 = length
static int s_lenShift = 6;     // Default: bits >> 6
static int s_lenMask  = 0x3FF; // 10 bits of length
static int s_wideFlag = 0;     // Bit index for wide flag

// The FNamePool in UE5 stores chunk pointers after an initial header
// Typical layout: lock (8 bytes), CurrentBlock+CurrentByteCursor (8 bytes), then Blocks[] array
// But the address from AOB often points directly to the chunk array or to pool start
static int s_chunksOffset = 0; // Offset from pool address to chunk pointer array

static void DetectHeaderFormat() {
    // Try to read FNameEntry at index 0 (should be "None", length 4)
    uintptr_t entry = GetEntry(0);
    if (!entry) return;

    uint16_t header = 0;
    if (!Mem::ReadSafe(entry, header)) return;

    // Try Format A: len = header >> 6
    int lenA = header >> 6;
    if (lenA == 4) {
        char name[5] = {};
        if (Mem::ReadBytesSafe(entry + 2, name, 4) && strcmp(name, "None") == 0) {
            s_lenShift = 6;
            s_lenMask = 0x3FF;
            s_wideFlag = 0;
            LOG_INFO("FNamePool: Header Format A (shift=6), verified 'None'");
            return;
        }
    }

    // Try Format B: len = (header >> 1) & 0x7FF
    int lenB = (header >> 1) & 0x7FF;
    if (lenB == 4) {
        char name[5] = {};
        if (Mem::ReadBytesSafe(entry + 2, name, 4) && strcmp(name, "None") == 0) {
            s_lenShift = 1;
            s_lenMask = 0x7FF;
            s_wideFlag = 0;
            LOG_INFO("FNamePool: Header Format B (shift=1), verified 'None'");
            return;
        }
    }

    LOG_WARN("FNamePool: Could not auto-detect header format, using default (shift=6)");
}

static void DetectChunksOffset() {
    // The FNamePool address may point to the pool object or directly to chunks
    // Try offset 0 first (chunks directly)
    uintptr_t chunk0 = 0;
    if (Mem::ReadSafe(s_poolAddr, chunk0) && chunk0 != 0) {
        // Verify this looks like a chunk pointer (should be in valid memory range)
        uint16_t testHeader = 0;
        if (Mem::ReadSafe(chunk0, testHeader)) {
            s_chunksOffset = 0;
            LOG_INFO("FNamePool: Chunks at offset 0");
            return;
        }
    }

    // Try common offsets for the Blocks[] array within FNamePool
    // FNamePool typically has: Lock(8) + CurrentBlock(4) + CurrentByteCursor(4) + Blocks[]
    int offsets[] = { 0x10, 0x08, 0x20, 0x40 };
    for (int off : offsets) {
        chunk0 = 0;
        if (Mem::ReadSafe(s_poolAddr + off, chunk0) && chunk0 != 0) {
            uint16_t testHeader = 0;
            if (Mem::ReadSafe(chunk0, testHeader)) {
                s_chunksOffset = off;
                LOG_INFO("FNamePool: Chunks at offset 0x%X", off);
                return;
            }
        }
    }

    LOG_WARN("FNamePool: Could not detect chunks offset, using 0");
    s_chunksOffset = 0;
}

void Init(uintptr_t gnamesAddr) {
    s_poolAddr = gnamesAddr;
    s_initialized = true;

    DetectChunksOffset();
    DetectHeaderFormat();

    // Verify by reading a few known names
    std::string none = GetString(0);
    LOG_INFO("FNamePool: Initialized at 0x%llX, FName[0] = '%s'",
             static_cast<unsigned long long>(gnamesAddr), none.c_str());
}

uintptr_t GetEntry(int32_t nameIndex) {
    if (!s_initialized || !s_poolAddr) return 0;

    int32_t chunkIndex  = nameIndex >> 16;
    int32_t chunkOffset = (nameIndex & 0xFFFF) * Constants::FNAME_STRIDE;

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

    uint16_t header = 0;
    if (!Mem::ReadSafe(entry, header)) return "";

    int len = (header >> s_lenShift) & s_lenMask;
    bool isWide = (header & (1 << s_wideFlag)) != 0;

    if (len <= 0 || len > 1024) return "";

    std::string result;

    if (isWide) {
        // Wide character name
        std::vector<wchar_t> wbuf(len + 1, 0);
        if (!Mem::ReadBytesSafe(entry + 2, wbuf.data(), len * sizeof(wchar_t))) return "";
        // Convert wide to narrow (simple ASCII conversion)
        result.resize(len);
        for (int i = 0; i < len; ++i) {
            result[i] = (wbuf[i] < 128) ? static_cast<char>(wbuf[i]) : '?';
        }
    } else {
        // ANSI name — sanitize non-ASCII bytes to produce valid UTF-8.
        // UE FNames should be pure ASCII; non-ASCII means corrupted/encrypted data.
        std::vector<char> buf(len + 1, 0);
        if (!Mem::ReadBytesSafe(entry + 2, buf.data(), len)) return "";
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
    return s_initialized;
}

} // namespace FNamePool
