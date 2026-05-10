// ============================================================
// Scharf — 夏爾夫 (鋒利目光的觀察者 — Sharp-eyed Scrutinizer)
// FProperty alignment validation: catches misaligned fields that would
// indicate a wrong FPROPERTY_OFFSET probe.
//
// Extracted so the walker's alignment heuristic can be unit-tested without
// spinning up a real game process. The original inline check in Ubel.cpp
// produced false-positive "Misaligned field" warnings on every UE 5.x game
// (uint8 EnumProperty packed at sub-4-byte offsets, FName at 4-byte alignment
// in non-CPN mode), drowning out real FPROPERTY_OFFSET regressions. Scharf
// uses ElemSize and CasePreservingName mode to give a precise judgement
// per type — the way a sharp-eyed examiner would.
// ============================================================

#pragma once

#include <cstdint>
#include <string>

namespace Scharf {

// Required alignment in bytes for a property of the given type/size.
// Returns 0 when the type is not subject to alignment validation
// (variable-layout types, container internals, etc.).
//
// elemSize is FPROPERTY_ELEMSIZE — the actual sizeof() the engine
// reports for this property. EnumProperty in particular is variable:
// uint8 enum is 1-byte aligned, uint32 enum is 4-byte aligned.
//
// casePreservingName: if true, FName/NameProperty is 16 bytes aligned 8;
// otherwise 8 bytes aligned 4.
inline int32_t RequiredAlignment(const std::string& typeName, int32_t elemSize, bool casePreservingName) noexcept {
    // Order matters: "WeakObjectProperty" / "SoftObjectProperty" / "LazyObjectProperty" all
    // contain "ObjectProperty" as a substring, so the specific variants must match first.
    // WeakObjectProperty: 2x int32 (8 bytes), 4-byte aligned.
    if (typeName == "WeakObjectProperty") return 4;
    if (typeName == "SoftObjectProperty" || typeName == "SoftClassProperty") return 8;
    if (typeName == "LazyObjectProperty") return 8;

    // Pointer-shaped (8-byte) properties — exact match for plain Object/ClassProperty.
    if (typeName == "ObjectProperty" || typeName == "ClassProperty") return 8;
    if (typeName == "InterfaceProperty") return 8;

    // Containers — outer is TArray = ptr+2 ints, alignment driven by ptr.
    if (typeName == "ArrayProperty" || typeName == "MapProperty" || typeName == "SetProperty") return 8;

    // Heap-backed strings/text contain a pointer.
    if (typeName == "StrProperty" || typeName == "TextProperty") return 8;

    // Delegates contain a FWeakObjectPtr + FName, 4-byte aligned baseline,
    // but multicast variants embed pointers/arrays — 8-byte safer.
    if (typeName == "DelegateProperty") return 4;
    if (typeName.find("MulticastDelegateProperty") != std::string::npos) return 8;
    if (typeName == "MulticastSparseDelegateProperty") return 1;  // FSparseDelegate { uint8 bIsBound; }

    // FName: 8 bytes aligned 4 (non-CPN), or 16 bytes aligned 8 (CPN).
    if (typeName == "NameProperty") return casePreservingName ? 8 : 4;

    // Primitive scalars — alignment == size.
    if (typeName == "BoolProperty"  || typeName == "ByteProperty") return 1;
    if (typeName == "Int8Property")  return 1;
    if (typeName == "Int16Property" || typeName == "UInt16Property") return 2;
    if (typeName == "IntProperty"   || typeName == "UInt32Property" || typeName == "FloatProperty") return 4;
    if (typeName == "Int64Property" || typeName == "UInt64Property" || typeName == "DoubleProperty") return 8;

    // EnumProperty — alignment derives from underlying integer width.
    // ElemSize is reliable here (engine writes it via FPROPERTY_ELEMSIZE).
    if (typeName == "EnumProperty") {
        if (elemSize >= 8) return 8;
        if (elemSize >= 4) return 4;
        if (elemSize >= 2) return 2;
        if (elemSize >= 1) return 1;
        return 0;  // unknown — don't validate
    }

    // Variable-layout: StructProperty is determined by the script struct's own layout.
    // FieldPathProperty, OptionalProperty depend on inner type. Skip validation.
    return 0;
}

// True when the field's offset violates its expected alignment.
// Returns false for offset == 0 (always aligned), unknown types, or types
// with no validation requirement.
inline bool IsAlignmentSuspicious(const std::string& typeName, int32_t offset, int32_t elemSize, bool casePreservingName) noexcept {
    if (offset <= 0) return false;
    int32_t align = RequiredAlignment(typeName, elemSize, casePreservingName);
    if (align <= 1) return false;
    return (offset % align) != 0;
}

}  // namespace Scharf
