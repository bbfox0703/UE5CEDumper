#pragma once

// ============================================================
// FNamePool.h — UE5 FNamePool string resolution
// ============================================================

#include <cstdint>
#include <string>

namespace FNamePool {

// Initialize with the FNamePool address found by OffsetFinder (UE4.23+/UE5)
// headerOffset: bytes before the 2-byte header within each FNameEntry.
//   0 = standard (UE5 / most UE4): [2B header][string]
//   4 = hash-prefixed (UE4.26 / FF7Re): [4B hash][2B header][string]
void Init(uintptr_t gnamesAddr, int headerOffset = 0);

// Initialize for UE4 TNameEntryArray mode (UE4 <4.23)
// nameArrayAddr: pointer to the TNameEntryArray chunk pointer array
// stringOffset:  offset within FNameEntry to the null-terminated string (typically 0x10)
void InitUE4(uintptr_t nameArrayAddr, int stringOffset = 0x10);

// Resolve an FName to its string representation
// nameIndex: FName::ComparisonIndex (the main index)
// number:    FName::Number (for _N suffix when > 0)
std::string GetString(int32_t nameIndex, int32_t number = 0);

// Get the raw FNameEntry address for a given index
uintptr_t GetEntry(int32_t nameIndex);

// Check if the pool has been initialized
bool IsInitialized();

// Check if running in UE4 TNameEntryArray mode
bool IsUE4Mode();

} // namespace FNamePool
