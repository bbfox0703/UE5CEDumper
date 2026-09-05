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
// casePreservingName: if true, FName/NameProperty is 12 bytes -- still ALIGNED 4;
// otherwise 8 bytes, also aligned 4. The flag does not change FName's alignment at all.
//
// ⚠ It is therefore [[maybe_unused]]: NO arm of RequiredAlignment reads it any more. Audit A9
// removed its only use (the NameProperty arm used to return `casePreservingName ? 8 : 4`, which
// was the defect -- alignof(FName) is 4 in both modes), and a comment claiming "other arms use
// it" was left behind and is now deleted; it was false the moment that arm changed.
//
// KEPT rather than removed, deliberately, for two reasons that are not "churn avoidance":
//   * IsAlignmentSuspicious below forwards it, and its callers (Ubel's ResolveElementAlignment
//     and IsAlignmentSuspicious sites) already hold DynOff::bCasePreservingName. Dropping it
//     would push the flag out of an API whose whole subject is layout-under-CPN.
//   * The tests pass `true` precisely to assert that the answer does NOT change. Removing the
//     parameter would silently delete that assertion's subject.
// If a property type is ever added whose ALIGNMENT (not size) really does depend on CPN, the
// plumbing is already here -- drop the attribute then, not before.
inline int32_t RequiredAlignment(const std::string& typeName, int32_t elemSize,
                                 [[maybe_unused]] bool casePreservingName) noexcept {
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

    // FName: 8 bytes (non-CPN) or 12 bytes (CPN) -- three int32, alignof 4 in BOTH modes.
    // `class FName` carries no alignas, so case-preserving does NOT raise its alignment.
    // The 8 seen in a TPair<FName, ptr> comes from the VALUE via max(keyAlign, valAlign),
    // never from FName -- which is why returning 8 here corrupted TMap<uint8,FName>:
    // ComputeMapValueOffset put the value at +8 where the engine puts it at +4.
    if (typeName == "NameProperty") return 4;

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
