// ============================================================
// Serie — 賽莉耶 (千年大魔法使 — Living-History Great Mage)
// FNamePool: FName string resolution (UE5 pool + UE4 TNameEntry)
// ============================================================

#include "Serie.h"
#include "Macht.h"
#define LOG_CAT "FNAM"
#include "Sein.h"
#include "Grimoire.h"
#include "Utf8Helpers.h"

#include <atomic>
#include <climits>
#include <memory>
#include <algorithm>
#include <cstring>
#include <vector>
#include <string>

namespace Serie {

static uintptr_t s_poolAddr = 0;
static std::atomic<bool> s_initialized{false};

// UE4 TNameEntryArray mode (pre-FNamePool)
static bool s_isUE4Mode = false;
static int  s_ue4StringOffset = 0x10;  // Offset within FNameEntry to null-terminated string
static constexpr int UE4_CHUNK_SIZE = 0x4000; // 16384 entries per chunk
static constexpr int32_t UE4_NAME_MAX_CHUNKS = 256;   // UE4 TNameEntryArray max chunk count (256 * 16384 = 4M names)
static constexpr int32_t UE5_NAME_MAX_CHUNKS = 8192;  // UE5 FNamePool max chunk count (8192 * 128KB = 1GB)
static constexpr int     FNAME_MAX_LEN       = 1024;  // Max plausible decoded FName length (sanity ceiling)

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
// chunkIndex = nameIndex >> bits, offset = (nameIndex & mask) * stride.
//
// ⛔ This is NOT detected and MUST NOT BE. It is a compile-time engine constant —
// `static constexpr uint32 FNameBlockOffsetBits = 16;` (vendored
// Core/Private/UObject/UnrealNames.cpp:253), with no #ifndef and no override — so 16 is
// right on every stock FNamePool (UE 4.23+). Pre-FNamePool builds resolve through
// s_isUE4Mode and never reach here.
//
// Audit #5 G4 established there is NO reliable offline discriminator, and the reasoning
// is what stops this being "fixed" again: a LOW index cannot distinguish 14 from 16 (both
// widths address the same byte — Serie::BlockBitsAreIndistinguishable), and a boundary
// index >= 2^14 need not be an entry BOUNDARY even in a valid 16-bit pool, so a
// fixed-index probe can WRONGLY DOWNGRADE a working pool. Anything that prints a
// "confirmation" beside this number is reporting a default as a measurement.
// The property that actually matters — do real names resolve — IS measured, over ten
// live object FName indices, by Frieren's `Name sanity: N/10 objects resolved`.
static int s_blockOffsetBits = 16;
static int s_blockOffsetMask = 0xFFFF;

// Byte offset within chunk[0] at which entry 0's CHARACTERS begin ("None"), as measured
// by DetectStride. -1 until measured. Feeds Serie::FirstEntryAfterNone so the init
// verification samples a REAL entry boundary — see that helper for why index 1 is not one.
static int s_noneOffset = -1;

// The FNamePool stores chunk pointers after an initial header
// Typical layout: lock (8 bytes), CurrentBlock+CurrentByteCursor (8 bytes), then Blocks[] array
// But the address from AOB often points directly to the chunk array or to pool start
static int s_chunksOffset = 0; // Offset from pool address to chunk pointer array

// ── Obfuscated payload support (licensee forks) ───────────────────────────────
// Some forks keep the STOCK 2-byte header but insert an extra field after it and
// XOR the character payload. MindsEye (Build A Rocket Boy, UE 5.4.4):
//   +0x00 u16 header  (stock: bIsWide:1 | LowercaseProbeHash:5 | Len:10)
//   +0x02 u16 tag     (non-stock — selects this block's XOR key)
//   +0x04 chars[len]  (XOR-obfuscated)
// s_payloadGap is the number of bytes between the header and the characters
// (0 = stock, so every stock title reads exactly the same address as before).
// The key is looked up in the fork's OWN tag->key table, read straight out of memory
// (see LookupTagKey). No game code is ever called: the fork's lookup takes an SRW lock
// before probing, so faulting inside it would unwind out of game frames with that lock
// still held and wedge every later FName::ToString.
static bool      s_obfuscated  = false;
static int       s_payloadGap  = 0;
static uintptr_t s_keyTableCtx = 0;

// tag -> key cache, 64K tags, allocated only in obfuscated mode so a stock title
// carries none of this. ONE atomic word per tag, not a (value, flag) pair: with two
// plain writes the compiler is free to publish the flag before the value, and a reader
// on another thread then sees "resolved" with a still-zero key. That produced wrong
// keys in practice — tag 0x0001 resolved to 0x09 on one thread and 0x00 on another in
// the same millisecond, decoding "Object" as "Fkclj}". A single word cannot tear.
//   bit 8 = resolved, bits 0-7 = key
// There is deliberately NO "miss" state (audit #5 G6): a genuine absent tag resolves to
// key 0 (Genau's "absent tag = plaintext" rule) and is cached RESOLVED like any other,
// while a TRANSIENT lookup failure (torn read of the live table) is left UNCACHED so the
// next name retries. Caching a transient miss permanently blanked every FName with that
// tag for the process life.
static constexpr uint16_t TAGKEY_RESOLVED = 0x100;
static std::unique_ptr<std::atomic<uint16_t>[]> s_tagKey;

// Look the fork's XOR key up in ITS OWN tag->key table — exact, no heuristics.
// Layout recovered from the fork's lookup routine (open hashing, 24-byte entries):
//   ctx +0x10  entries base        ctx +0x18  count      ctx +0x44  (== count => empty)
//   ctx +0x48  inline buckets      ctx +0x50  bucket array (0 => use inline)
//   ctx +0x58  capacity (power of two);  bucket = tag & (capacity - 1)
//   entry +0x00 u16 tag   +0x08 u64 value (LOW BYTE is the key)   +0x10 i32 next (-1 = end)
// Pure memory reads: no control ever transfers into game code, so the SRW lock the
// game takes around its own probe can never be left held by us.
// Classify a lookup so a TRANSIENT failure is never confused with a genuine miss
// (audit #5 G6). Returns true + `outKey` when the tag is found. On a miss, `readError`
// splits the two cases the old single-bool return conflated:
//   readError = true  -> could NOT determine (no context / a torn read of the live table
//                        the game mutates as it runs / a corrupt-looking chain). The
//                        caller must not cache this.
//   readError = false -> the hash chain terminated cleanly with no match, i.e. the tag is
//                        genuinely ABSENT -> the block is plaintext (key 0).
static bool LookupTagKey(uint16_t tag, uint8_t& outKey, bool& readError) {
    readError = true;   // pessimistic: anything but a clean walk is "unknown"
    if (!s_keyTableCtx) return false;

    int32_t count = 0, sentinel = 0, capacity = 0;
    uintptr_t entries = 0, buckets = 0;
    if (!Macht::ReadSafe(s_keyTableCtx + 0x18, count)) return false;
    if (!Macht::ReadSafe(s_keyTableCtx + 0x44, sentinel)) return false;
    if (count == sentinel) return false;                      // table reports empty (transient/racing)
    if (!Macht::ReadSafe(s_keyTableCtx + 0x10, entries) || !entries) return false;
    if (!Macht::ReadSafe(s_keyTableCtx + 0x58, capacity) || capacity <= 0) return false;
    if (!Macht::ReadSafe(s_keyTableCtx + 0x50, buckets)) return false;
    if (!buckets) buckets = s_keyTableCtx + 0x48;             // inline bucket array

    int32_t idx = 0;
    if (!Macht::ReadSafe(buckets + static_cast<uintptr_t>(tag & (capacity - 1)) * 4, idx))
        return false;

    // Bounded chain walk — a corrupt/misread table must not spin forever.
    for (int hop = 0; hop < 64; ++hop) {
        if (idx < 0) { readError = false; return false; }    // clean end of chain -> ABSENT (plaintext)
        if (idx >= count) return false;                       // out-of-range link -> corrupt/racing
        uintptr_t entry = entries + static_cast<uintptr_t>(idx) * 24;
        uint16_t  etag  = 0;
        if (!Macht::ReadSafe(entry + 0x00, etag)) return false;
        if (etag == tag) {
            uint8_t key = 0;
            if (!Macht::ReadSafe(entry + 0x08, key)) return false;   // low byte of the u64 value
            readError = false;
            outKey = key;
            return true;
        }
        if (!Macht::ReadSafe(entry + 0x10, idx)) return false;
    }
    return false;   // chain too long -> corrupt, readError stays true
}

static bool GetTagKey(uint16_t tag, uint8_t& outKey) {
    if (!s_tagKey) return false;
    uint16_t cached = s_tagKey[tag].load(std::memory_order_acquire);
    if (cached & TAGKEY_RESOLVED) { outKey = static_cast<uint8_t>(cached & 0xFF); return true; }

    uint8_t key = 0;
    bool readError = false;
    if (!LookupTagKey(tag, key, readError)) {
        if (readError) {
            // audit #5 G6: a TRANSIENT miss (no context / torn read of the live table /
            // corrupt-looking chain) is NOT cached. Poisoning it — as the old permanent
            // TAGKEY_MISS did — blanked every FName carrying this tag for the rest of the
            // process, though the fork's own lookup would have succeeded on a later read.
            // Leave the slot unresolved so the next name retries.
            return false;
        }
        // Genuine ABSENT -> Genau's "absent tag = plaintext": the fork stores this block
        // unXOR'd (its own lookup returns NULL and XORs with 0). Resolve to key 0 and
        // cache it. If the game later inserts this tag, the ANSI decode's two-attempt
        // InvalidateTagKey path self-heals to the real key on the first bad decode.
        s_tagKey[tag].store(TAGKEY_RESOLVED, std::memory_order_release);   // RESOLVED | key(0)
        outKey = 0;
        return true;
    }
    // Racing threads compute the identical value, so the last writer wins harmlessly.
    s_tagKey[tag].store(static_cast<uint16_t>(TAGKEY_RESOLVED | key), std::memory_order_release);
    outKey = key;
    return true;
}

// Drop a cached tag so the next resolve re-reads the game's table. Used when a decode
// comes out non-ASCII: the table is live (the game adds names as it runs), so a lock-free
// read can legitimately catch it mid-update.
static void InvalidateTagKey(uint16_t tag) {
    if (s_tagKey) s_tagKey[tag].store(0, std::memory_order_release);
}


// Helper: try to decode a "None" FNameEntry at the given address with specified header offset.
// Returns true if successfully verified.
static bool TryDecodeNone(uintptr_t entryAddr, int hdrOff) {
    uint16_t header = 0;
    if (!Macht::ReadSafe(entryAddr + hdrOff, header)) return false;

    auto tryFormat = [&](int shift, int mask) -> bool {
        int len = (header >> shift) & mask;
        if (len != 4) return false;
        char name[5] = {};
        if (!Macht::ReadBytesSafe(entryAddr + hdrOff + 2, name, 4)) return false;
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
        // audit #5 G5: reject a negative nameIndex before `%` yields a negative elemIndex.
        if (!UE4NameIndexInBounds(nameIndex, UE4_CHUNK_SIZE, UE4_NAME_MAX_CHUNKS)) return 0;
        int32_t chunkIndex = nameIndex / UE4_CHUNK_SIZE;
        int32_t elemIndex  = nameIndex % UE4_CHUNK_SIZE;
        uintptr_t chunkPtr = 0;
        if (!Macht::ReadSafe(s_poolAddr + chunkIndex * sizeof(uintptr_t), chunkPtr) || !chunkPtr) return 0;
        uintptr_t entryPtr = 0;
        if (!Macht::ReadSafe(chunkPtr + elemIndex * sizeof(uintptr_t), entryPtr)) return 0;
        return entryPtr;
    }

    int32_t chunkIndex  = nameIndex >> s_blockOffsetBits;
    int32_t chunkOffset = (nameIndex & s_blockOffsetMask) * s_stride;
    if (chunkIndex < 0 || chunkIndex > UE5_NAME_MAX_CHUNKS) return 0;
    uintptr_t chunkPtr = 0;
    uintptr_t chunksBase = s_poolAddr + s_chunksOffset;
    if (!Macht::ReadSafe(chunksBase + chunkIndex * sizeof(uintptr_t), chunkPtr) || !chunkPtr) return 0;
    return chunkPtr + chunkOffset;
}

static void DetectHeaderFormat() {
    // Try to read FNameEntry at index 0 (should be "None", length 4)
    // Use GetEntryInternal since s_initialized is not yet set during Init().
    uintptr_t entry = GetEntryInternal(0);
    if (!entry) return;

    // The header is at entry + s_headerOffset
    uint16_t header = 0;
    if (!Macht::ReadSafe(entry + s_headerOffset, header)) return;

    // Try Format A: len = header >> 6
    int lenA = header >> 6;
    if (lenA == 4) {
        char name[5] = {};
        if (Macht::ReadBytesSafe(entry + s_headerOffset + 2, name, 4) && strcmp(name, "None") == 0) {
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
        if (Macht::ReadBytesSafe(entry + s_headerOffset + 2, name, 4) && strcmp(name, "None") == 0) {
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
    if (!Macht::ReadSafe(poolAddr + chunksOffset, chunk0) || !chunk0) return false;

    // Pointer sanity
    if (!Grimoire::IsUserspacePointer(chunk0)) return false;

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
        if (Macht::ReadSafe(s_poolAddr + off, chunk0) && chunk0 != 0 &&
            chunk0 > Grimoire::PTR_USERSPACE_MIN && chunk0 < Grimoire::PTR_USERSPACE_MAX) {
            uint16_t testHeader = 0;
            if (Macht::ReadSafe(chunk0, testHeader)) {
                s_chunksOffset = off;
                LOG_WARN("FNamePool: Chunks at offset 0x%X (unvalidated fallback)", off);
                return;
            }
        }
    }

    LOG_ERROR("FNamePool: Could not detect chunks offset at all");
    s_chunksOffset = 0x10; // Best guess: standard layout
}

// Auto-detect FNameEntry stride by scanning chunk[0] for the "None" string.
// Dumper-7 approach: the byte offset where "None" appears = FNameEntryHeaderSize.
// Stride = (headerSize == 2) ? 2 : 4.
// Must be called after DetectChunksOffset() so s_chunksOffset is set.
static void DetectStride() {
    uintptr_t chunksBase = s_poolAddr + s_chunksOffset;
    uintptr_t chunk0 = 0;
    if (!Macht::ReadSafe(chunksBase, chunk0) || !chunk0) return;

    // Scan first 16 bytes of chunk[0] for "None" (0x4E6F6E65)
    // The offset where "None" appears tells us the header size.
    constexpr uint32_t NoneAsU32 = 0x656E6F4E; // "None" in little-endian
    char buf[16] = {};
    if (!Macht::ReadBytesSafe(chunk0, buf, 16)) return;

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

    // Publish where entry 0's characters start so the init check can sample a REAL entry
    // boundary (Serie::FirstEntryAfterNone) instead of an index that is never one.
    // Set LAST: s_stride may have just changed above, and the two are read together.
    s_noneOffset = noneOffset;
}

// Sample the lowest REAL entry boundary above "None" and render it for an init log line.
// ONE implementation for BOTH Init paths, deliberately: the defect this replaces survived
// because the same wrong index (1) was hand-written in two places and only one of them was
// ever looked at (`docs/working-lessons.md` §2.17 — a fix that landed in one of its copies).
static std::string FirstEntrySampleText() {
    const int32_t idx = Serie::FirstEntryAfterNone(s_noneOffset, s_stride);
    if (idx <= 0) return "FName[?]=<entry geometry not established>";
    char buf[320] = {};
    snprintf(buf, sizeof(buf), "FName[%d]='%s'", idx, GetString(idx).c_str());
    return buf;
}

void InitObfuscated(uintptr_t gnamesAddr, int chunksOffset, int payloadGap, uintptr_t keyTableCtx) {
    // Obfuscated-fork path. Deliberately does NOT run the stock auto-detection
    // (DetectChunksOffset / DetectStride / DetectHeaderFormat):
    // every one of those probes reads the character payload looking for a literal
    // "None", which cannot work before the payload is decrypted. Genau has already
    // proved the geometry — it recovered a key, decoded entry 0 to exactly "None" and
    // chain-verified the block — so the parameters it proved are adopted directly.
    // Keeping the stock detectors untouched is what makes this change inert for every
    // other title: no stock path is modified, only bypassed on this one branch.
    s_poolAddr       = gnamesAddr;
    s_isUE4Mode      = false;
    s_headerOffset   = 0;              // stock 2-byte header at entry+0
    s_stride         = Grimoire::FNAME_STRIDE;
    s_chunksOffset   = chunksOffset;   // proved by Genau (MindsEye: 0x10)
    s_blockOffsetBits = 16;
    s_blockOffsetMask = 0xFFFF;
    s_lenShift       = 6;              // stock Format A: len = header >> 6
    s_lenMask        = 0x3FF;
    s_wideFlag       = 0;
    s_payloadGap     = payloadGap;
    s_obfuscated     = true;
    s_keyTableCtx    = keyTableCtx;
    // DetectStride is bypassed here, so publish the same fact it would have: entry 0's
    // characters start after the 2-byte header plus the fork's inserted field. This is
    // the case that forbids hardcoding the boundary index — MindsEye's payloadGap of 2
    // makes it 4, not the 3 both stock layouts give.
    s_noneOffset     = 2 + payloadGap;
    s_tagKey = std::make_unique<std::atomic<uint16_t>[]>(0x10000);
    for (size_t i = 0; i < 0x10000; ++i) s_tagKey[i].store(0, std::memory_order_relaxed);

    s_initialized.store(true, std::memory_order_release);

    LOG_INFO("FNamePool: Initialized OBFUSCATED at 0x%llX (chunks+0x%02X, payloadGap=%d, keyTable=0x%llX), %s",
             static_cast<unsigned long long>(gnamesAddr), chunksOffset, payloadGap,
             static_cast<unsigned long long>(keyTableCtx), FirstEntrySampleText().c_str());
}

void Init(uintptr_t gnamesAddr, int headerOffset) {
    s_poolAddr = gnamesAddr;
    s_isUE4Mode = false;
    s_headerOffset = headerOffset;
    s_obfuscated = false;
    s_payloadGap = 0;
    // Reset before detection: DetectStride has two early returns (no chunk[0], no 'None'),
    // and a stale value carried from a previous Init would make the verification line sample
    // a boundary derived from the OLD pool's geometry and read as a confirmation of this one.
    s_noneOffset = -1;

    // Initial stride guess based on header offset.
    // Hash-prefixed entries (headerOffset=4) have uint32_t ComparisonId as first member,
    // raising alignof(FNameEntry) to 4 (vs 2 for standard entries with uint16_t header).
    s_stride = (headerOffset >= 4) ? 4 : Grimoire::FNAME_STRIDE;
    LOG_INFO("FNamePool: Entry stride = %d (initial, hdrOff=%d)", s_stride, headerOffset);

    DetectChunksOffset();
    DetectStride(); // Refine stride by scanning chunk[0] for "None"
    DetectHeaderFormat();

    // Not a detection — a statement of fact. See s_blockOffsetBits for why probing it is
    // unsatisfiable AND uninformative, and what measures name health instead.
    LOG_INFO("FNamePool: BlockOffsetBits = %d (stock FNamePool engine constant, UE 4.23+; "
             "not detectable offline — audit #5 G4)", s_blockOffsetBits);

    // Mark initialized with release ordering — fences all preceding writes so
    // any thread that acquire-loads s_initialized==true sees consistent state.
    s_initialized.store(true, std::memory_order_release);

    // Verify by reading a real entry. NOT index 0 — GetString short-circuits `nameIndex <= 0`
    // to the literal "None" without touching memory, so it verifies nothing — and NOT index 1,
    // which is interior to entry 0 on every layout we support (Serie::FirstEntryAfterNone).
    LOG_INFO("FNamePool: Initialized at 0x%llX (hdrOff=%d, stride=%d), %s",
             static_cast<unsigned long long>(gnamesAddr), headerOffset, s_stride,
             FirstEntrySampleText().c_str());
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
        // Bounds guard (audit #5 G5): reject a negative nameIndex FIRST. A poison
        // ComparisonIndex 0xFFFFFFFF read as int32 = -1 gives chunkIndex 0 / elemIndex -1,
        // which passes a chunkIndex-only guard and derefs chunkPtr + (-1)*8 as an
        // FNameEntry*, returning a fabricated name as real. The UE5 path below is safe via
        // its own `chunkIndex < 0` guard (arithmetic >> keeps the sign).
        if (!UE4NameIndexInBounds(nameIndex, UE4_CHUNK_SIZE, UE4_NAME_MAX_CHUNKS)) return 0;
        int32_t chunkIndex = nameIndex / UE4_CHUNK_SIZE;
        int32_t elemIndex  = nameIndex % UE4_CHUNK_SIZE;

        // Read chunk pointer from array
        uintptr_t chunkPtr = 0;
        if (!Macht::ReadSafe(s_poolAddr + chunkIndex * sizeof(uintptr_t), chunkPtr) || !chunkPtr)
            return 0;

        // Each element in the chunk is a pointer to FNameEntry
        uintptr_t entryPtr = 0;
        if (!Macht::ReadSafe(chunkPtr + elemIndex * sizeof(uintptr_t), entryPtr))
            return 0;

        return entryPtr;
    }

    // UE5 FNamePool path: packed inline entries with 2-byte header
    int32_t chunkIndex  = nameIndex >> s_blockOffsetBits;
    int32_t chunkOffset = (nameIndex & s_blockOffsetMask) * s_stride;

    // Bounds guard: FNamePool typically has < 8192 chunks (each 128KB = 1GB total max)
    if (chunkIndex < 0 || chunkIndex > UE5_NAME_MAX_CHUNKS) return 0;

    // Read chunk pointer
    uintptr_t chunkPtr = 0;
    uintptr_t chunksBase = s_poolAddr + s_chunksOffset;
    if (!Macht::ReadSafe(chunksBase + chunkIndex * sizeof(uintptr_t), chunkPtr) || !chunkPtr) {
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
        if (!Macht::ReadBytesSafe(entry + s_ue4StringOffset, buf, 255)) return "";
        buf[255] = '\0';

        // Sanitize to printable ASCII; reject a junk-laden decode (invalid/uninitialized
        // FName index) so the caller renders 'None' instead of "??ByteProperty??...".
        std::string result = Utf8Helpers::SanitizeAnsiName(buf, 255);
        if (result.empty()) return "";

        if (number > 0) {
            result += "_" + std::to_string(number - 1);
        }
        return result;
    }

    // UE5/UE4.23+ FNamePool path:
    // Entry layout: [s_headerOffset bytes prefix][2-byte header][string data]
    // s_headerOffset is 0 for standard UE5, 4 for hash-prefixed UE4.26
    uint16_t header = 0;
    if (!Macht::ReadSafe(entry + s_headerOffset, header)) return "";

    int len = (header >> s_lenShift) & s_lenMask;
    bool isWide = (header & (1 << s_wideFlag)) != 0;

    if (len <= 0 || len > FNAME_MAX_LEN) return "";

    // String starts at entry + s_headerOffset + 2 (+ s_payloadGap on an obfuscated
    // fork, where a non-stock field sits between the header and the characters).
    // s_payloadGap is 0 for every stock title, so this is the same address as before.
    int strStart = s_headerOffset + 2 + s_payloadGap;

    // Obfuscated forks XOR the payload with a key selected by the u16 tag that sits
    // between the header and the characters. A tag we cannot resolve yields "" rather
    // than plausible-looking garbage.
    uint8_t xorKey = 0;
    uint16_t obfTag = 0;
    if (s_obfuscated) {
        if (!Macht::ReadSafe(entry + s_headerOffset + 2, obfTag)) return "";
        if (!GetTagKey(obfTag, xorKey)) return "";
    }

    std::string result;

    if (isWide) {
        // Wide character name — convert UTF-16 (wchar_t on Windows) to UTF-8.
        // UE FNames can store localized display names (e.g., Japanese) in wide format.
        //
        // Surrogate handling is critical: nlohmann::json strict-validates UTF-8 and
        // rejects 3-byte sequences that encode surrogate codepoints (CESU-8). Squad
        // logs at build 488 showed 13 "invalid UTF-8 byte at index 1: 0xA0" failures
        // on get_object_list — that 0xA0 is the second byte of 0xED 0xA0 0x80 = U+D800
        // (high surrogate). Utf8Helpers::EncodeUtf16 detects surrogate pairs and emits
        // proper 4-byte UTF-8; lone surrogates collapse to '?'. The same helper is
        // unit-tested by utf8_helpers_test.cpp.
        std::vector<wchar_t> wbuf(len + 1, 0);
        if (!Macht::ReadBytesSafe(entry + strStart, wbuf.data(), len * sizeof(wchar_t))) return "";
        // Re-trim length to first NUL inside the readable region so EncodeUtf16
        // doesn't process trailing zeros / past-end garbage.
        size_t actualLen = 0;
        while (actualLen < static_cast<size_t>(len) && wbuf[actualLen] != 0) ++actualLen;
        // Reject a junk/misidentified FName whose wide bit landed on an ANSI
        // buffer (asset path → long CJK mojibake). Returning "" lets callers
        // (FindByAddress backward scan, object list, Live Walker) treat the
        // decode as a miss instead of surfacing 亂碼.
        if (Utf8Helpers::IsImplausibleWideName(wbuf.data(), actualLen)) return "";
        result = Utf8Helpers::EncodeUtf16(wbuf.data(), actualLen);
    } else {
        // ANSI name — a real narrow FName is pure printable ASCII. SanitizeAnsiName
        // returns "" when any byte is non-ASCII, which means the entry is corrupt or an
        // invalid/uninitialized FName index landed mid-entry inside a packed FNamePool
        // (e.g. index 2 falls inside the 'None' entry → "??ByteProperty??IntProperty").
        // Returning "" lets callers render 'None' instead of a misleading concatenation.
        // A literal '?' (0x3F) is printable and is preserved. The wide branch above has
        // the equivalent guard via IsImplausibleWideName.
        std::vector<char> buf(len + 1, 0);
        // Two attempts: a lock-free read of the fork's LIVE key table can catch it
        // mid-update, and a wrong key still yields bytes — but almost never printable
        // ASCII ones. So on a rejected decode, drop the cached tag and resolve once more.
        for (int attempt = 0; attempt < 2; ++attempt) {
            std::fill(buf.begin(), buf.end(), char(0));
            if (!Macht::ReadBytesSafe(entry + strStart, buf.data(), len)) return "";
            if (xorKey) {
                for (int i = 0; i < len; ++i)
                    buf[i] = static_cast<char>(static_cast<uint8_t>(buf[i]) ^ xorKey);
            }
            result = Utf8Helpers::SanitizeAnsiName(buf.data(), static_cast<size_t>(len));
            if (!result.empty() || !s_obfuscated || attempt == 1) break;
            InvalidateTagKey(obfTag);
            if (!GetTagKey(obfTag, xorKey)) return "";
        }

        // Obfuscation diagnostic: a WRONG key still yields printable text, so a bad
        // decode cannot be detected by charset alone. Log a bounded sample of
        // (entry, tag, key) with the decoded bytes so a mis-keyed name can be compared
        // against the game's own output and the table walk corrected.
        if (s_obfuscated) {
            static int s_obfDbg = 0;
            if (s_obfDbg < 24) {
                ++s_obfDbg;
                LOG_DEBUG("FNamePool: obf idx=%d entry=0x%llX tag=0x%04X key=0x%02X len=%d -> '%s'",
                          nameIndex, static_cast<unsigned long long>(entry),
                          obfTag, xorKey, len, result.c_str());
            }
        }
        if (result.empty()) return "";
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
        if (Macht::ReadSafe(chunksBase + i * sizeof(uintptr_t), chunkPtr) && chunkPtr != 0) {
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
        if (!Macht::ReadSafe(entry + s_headerOffset, header)) { ++stats.headerReadFail; continue; }

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
        if (len > FNAME_MAX_LEN) {
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
        if (!Macht::ReadBytesSafe(entry + s_headerOffset + 2, buf, readLen)) { ++stats.stringReadFail; continue; }

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
        if (Macht::ReadSafe(chunksBase + c * sizeof(uintptr_t), chunkPtr) && chunkPtr) {
            uint8_t dump[32] = {};
            Macht::ReadBytesSafe(chunkPtr, dump, 32);
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

} // namespace Serie
