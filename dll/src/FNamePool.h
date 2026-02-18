#pragma once

// ============================================================
// FNamePool.h — UE5 FNamePool string resolution
// ============================================================

#include <cstdint>
#include <string>

namespace FNamePool {

// Initialize with the FNamePool address found by OffsetFinder
void Init(uintptr_t gnamesAddr);

// Resolve an FName to its string representation
// nameIndex: FName::ComparisonIndex (the main index)
// number:    FName::Number (for _N suffix when > 0)
std::string GetString(int32_t nameIndex, int32_t number = 0);

// Get the raw FNameEntry address for a given index
uintptr_t GetEntry(int32_t nameIndex);

// Check if the pool has been initialized
bool IsInitialized();

} // namespace FNamePool
