# SIMD Scanning Research Notes

> Research findings from analyzing AOBMaker's C# SIMD-based memory scanning implementation.
> Purpose: evaluate potential speedup for UE5CEDumper's C++ AOBScan.

-----

## Summary

| Aspect | Our Scanner (`Memory.cpp`) | AOBMaker |
|--------|---------------------------|----------|
| Language | C++ (MSVC) | C# (.NET) |
| SIMD | Implicit via CRT `memchr()` | Explicit AVX2 (`Vector256`) / SSE (`Vector128`) |
| Anchor | First byte of pattern | First complete literal byte |
| Wildcards | Full byte (`??`) only | Full (`??`) + nibble (`4?`, `?F`) |
| Parallelism | Single-threaded | `Parallel.ForEach` across regions |
| Est. Speedup | Baseline | 3-5x potential (anchor + parallel) |
| Assessment | Adequate for current use | Phase 4 optimization candidate |

-----

## 1. Core Algorithm: Two-Phase Anchor-Based Scanning

AOBMaker uses a two-phase approach:

**Phase 1** — SIMD anchor detection: Compare a single "anchor byte" across 32 bytes simultaneously using AVX2. This filters ~97% of positions.

**Phase 2** — Scalar pattern verification: For each anchor match, verify the full pattern byte-by-byte with nibble mask support.

### Pseudocode

```
SIMD_SCAN(buffer, pattern, nibbleMask, baseAddress):
    anchorOffset = FindFirstLiteralByte(pattern, nibbleMask)

    if anchorOffset < 0:
        return ScalarScan(buffer, pattern, nibbleMask)

    anchorByte = pattern[anchorOffset]
    needle = Vector256.Create(anchorByte)  // Broadcast to all 32 lanes
    results = []
    i = 0

    // Phase 1: SIMD (32 bytes per iteration)
    while i + anchorOffset + 32 <= buffer.Length:
        chunk = Load256(buffer[i + anchorOffset])
        matchMask = Equals256(chunk, needle)
        bits = ExtractMSB(matchMask)    // 32-bit bitmask

        while bits != 0:
            bitPos = TrailingZeroCount(bits)
            candidateI = i + bitPos

            // Phase 2: Scalar verify
            if VerifyNibble(buffer, candidateI, pattern, nibbleMask):
                results.Add(baseAddress + candidateI)

            bits &= bits - 1            // Clear lowest set bit

        i += 32

    // Tail: scalar fallback
    ScalarScan(buffer[i:], pattern, nibbleMask)
    return results
```

### Anchor Selection

```
FindFirstLiteralByte(pattern, nibbleMask):
    for k = 0 to pattern.Length:
        if nibbleMask[k] == 0xFF:   // Full literal byte
            return k
    return -1                        // No anchor -> pure scalar
```

-----

## 2. Nibble Wildcard Support

AOBMaker extends Cheat Engine's wildcard format with partial byte matching:

| Format | nibbleMask | Matches |
|--------|-----------|---------|
| `48` | `0xFF` | Exact byte 0x48 |
| `??` | `0x00` | Any byte |
| `4?` | `0xF0` | 0x40-0x4F |
| `?8` | `0x0F` | 0x08, 0x18, ..., 0xF8 |

### Verification

```
VerifyNibble(buffer, offset, pattern, nibbleMask):
    for j = 0 to pattern.Length:
        mask = nibbleMask[j]
        if mask == 0x00: continue          // Full wildcard
        if (buffer[offset+j] & mask) != (pattern[j] & mask):
            return false
    return true
```

-----

## 3. Multi-Region Parallel Scanning

```
ParallelScan(processHandle, pattern, nibbleMask):
    regions = EnumerateValidRegions(processHandle)
    // Split large regions (>64MB) into chunks

    Parallel.ForEach(regions, (region) =>
        buffer = ReadProcessMemory(region)
        matches = SIMD_SCAN(buffer, pattern, nibbleMask, region.BaseAddress)
        results.AddRange(matches)   // ConcurrentBag

        if earlyStop: loopState.Stop()
    )
    return results
```

### Region Filtering

| Filter | Purpose |
|--------|---------|
| `MEM_IMAGE` | EXE/DLL code sections |
| `MEM_PRIVATE` | Heap / JIT code |
| `MEM_MAPPED` | Shared memory (usually off) |
| `PAGE_EXECUTE*` | Executable pages only |

-----

## 4. C++ Implementation Sketch

If we adopt this for `Memory.cpp` in the future:

```cpp
#include <immintrin.h>  // AVX2
#include <intrin.h>     // _BitScanForward

int FindAnchor(const uint8_t* pattern, const uint8_t* mask, int len) {
    for (int k = 0; k < len; k++)
        if (mask[k] == 0xFF) return k;
    return -1;
}

bool VerifyPattern(const uint8_t* buf, const uint8_t* pattern,
                   const uint8_t* mask, int len) {
    for (int j = 0; j < len; j++) {
        if (mask[j] == 0x00) continue;
        if ((buf[j] & mask[j]) != (pattern[j] & mask[j])) return false;
    }
    return true;
}

uintptr_t ScanAVX2(const uint8_t* region, size_t regionSize,
                   const uint8_t* pattern, const uint8_t* mask, int patLen) {
    int anchorOff = FindAnchor(pattern, mask, patLen);
    if (anchorOff < 0) return 0;  // fallback to scalar

    __m256i needle = _mm256_set1_epi8(pattern[anchorOff]);
    size_t maxI = regionSize - patLen;

    for (size_t i = 0; i + anchorOff + 32 <= regionSize && i <= maxI; i += 32) {
        __m256i chunk = _mm256_loadu_si256(
            (const __m256i*)(region + i + anchorOff));
        __m256i cmp = _mm256_cmpeq_epi8(chunk, needle);
        uint32_t bits = (uint32_t)_mm256_movemask_epi8(cmp);

        while (bits) {
            unsigned long bitPos;
            _BitScanForward(&bitPos, bits);
            size_t candidate = i + bitPos;

            if (candidate <= maxI &&
                VerifyPattern(region + candidate, pattern, mask, patLen))
                return (uintptr_t)(region + candidate);

            bits &= bits - 1;
        }
    }
    return 0;
}
```

-----

## 5. Comparison with Our Current Scanner

Our `Memory.cpp` uses `memchr()` to find the first byte, then scalar verification. On MSVC, `memchr()` internally uses SSE2/AVX2 optimizations, so the raw throughput difference is smaller than expected.

### Where AOBMaker Gains Advantage

1. **Explicit anchor selection** — `memchr` always uses byte 0; AOBMaker picks the first non-wildcard byte (handles patterns like `?? ?? 48 8B` better)
2. **Parallel region scanning** — Multi-threaded across memory regions
3. **Nibble wildcards** — Half-byte matching for more precise patterns
4. **Early stopping** — Atomic flag across threads

### Where Our Scanner Is Fine

1. **Single-pattern scans** — We scan for specific known patterns, not arbitrary user input
2. **Executable sections only** — Already filtered, small scan space
3. **CRT SIMD** — `memchr` on MSVC is already SIMD-optimized
4. **Simplicity** — Less code surface for bugs in injected DLL context

-----

## 6. Recommendation

**Current assessment**: Phase 4 nice-to-have. Our scanner works for the use case (scanning game module executable sections for ~5 known patterns). The main bottleneck is pattern quality (wrong matches), not scan speed.

**If scan speed becomes an issue**:
1. Add anchor selection (pick first non-wildcard byte instead of byte 0)
2. Add nibble wildcard support for more precise patterns
3. Consider parallel scanning if scanning large `.data` sections for GNames fallback

**Not worth adopting now**:
- Full AVX2 explicit implementation (CRT handles it)
- Multi-threaded scanning (scan space is small, ~50-200MB executable sections)
