#pragma once

// ============================================================
// Constants.h — Centralized magic strings and numbers for DLL
//
// NOTE: AOB patterns and symbol exports have moved to Signatures.h
// ============================================================

#include <atomic>

namespace Constants {

// --- Logging ---
constexpr const wchar_t* LOG_FOLDER_NAME  = L"UE5CEDumper";
constexpr const wchar_t* LOG_SUBFOLDER    = L"Logs";
constexpr const wchar_t* LOG_FILE_PREFIX  = L"UE5Dumper";
constexpr const wchar_t* LOG_SCAN_PREFIX  = L"UE5Dumper-scan";
constexpr const wchar_t* LOG_PIPE_PREFIX  = L"UE5Dumper-pipe";
constexpr int            LOG_MAX_FILES    = 5;
constexpr size_t         LOG_MAX_SIZE_MB  = 5;
constexpr size_t         LOG_MAX_SIZE     = LOG_MAX_SIZE_MB * 1024 * 1024;

// --- Named Pipe ---
constexpr const wchar_t* PIPE_NAME        = L"\\\\.\\pipe\\UE5DumpBfx";
constexpr const char*    PIPE_NAME_NARROW  = "\\\\.\\pipe\\UE5DumpBfx";
constexpr unsigned long  PIPE_BUF_SIZE    = 65536;

// --- UObject offsets ---
// UObjectBase layout: VTable(8) + Flags(4) + Index(4) + Class*(8) + FName(?) + Outer*(8)
// Most offsets are stable, but Outer shifts when CasePreservingName is active (FName = 0x10):
//   Standard (UE4.25-UE5.4, UE5.5+ non-CPN): Outer = 0x20
//   CasePreservingName (UE4.27-CPN):          Outer = 0x28
// NamePrivate at 0x18 reads ComparisonIndex (first 4 bytes), stable regardless of FName size.
constexpr int OFF_UOBJECT_VTABLE       = 0x00;
constexpr int OFF_UOBJECT_FLAGS        = 0x08;
constexpr int OFF_UOBJECT_INDEX        = 0x0C;
constexpr int OFF_UOBJECT_CLASS        = 0x10;
constexpr int OFF_UOBJECT_NAME         = 0x18;

// --- UStruct / FField / FProperty offsets (runtime-detected) ---
// ValidateAndFixOffsets() dynamically detects all offsets below.
// Defaults match UE5.0-5.1 layout (FFieldVariant=0x10 bytes).
// UE5.1.1+ uses FFieldVariant=0x08 bytes, shifting FField::Next/Name/etc by -8.
//
// Version differences (from RE-UE4SS MemberVarLayoutTemplates):
//   UE5.0-5.1.0: FFieldVariant=0x10 → Next=0x20, Name=0x28, Offset_Internal=0x4C
//   UE5.1.1-5.5: FFieldVariant=0x08 → Next=0x18, Name=0x20, Offset_Internal=0x44
// UStruct offsets (Super, Children, ChildProperties) are stable: 0x40/0x48/0x50.
//
// UE4 differences:
//   UE4 <4.25:   No FField/FProperty, properties are UProperty (UObject-derived) in Children chain
//   UE4.25-4.27: FField/FProperty exists, layout similar to UE5.0-5.1 (FFieldVariant=0x10)
//   UE4.27-CPN:  FName=0x10 bytes, shifts FField::Flags+0x8, FFieldClass offsets+0x8,
//                 and UObject::Outer from 0x20 to 0x28

} // namespace Constants

namespace DynOff {

// === UObject — runtime-detected ===
// Most are stable, but Outer shifts when CasePreservingName enlarges FName.
inline int UOBJECT_OUTER      = 0x20;  // OuterPrivate: 0x20 (standard), 0x28 (CPN)

// === UStruct — stable across UE4.25+ and UE5.0-5.5 ===
inline int USTRUCT_SUPER      = 0x40;
inline int USTRUCT_CHILDREN   = 0x48;  // UField* chain (functions; in UE4 <4.25: all properties here)
inline int USTRUCT_CHILDPROPS = 0x50;  // FField* chain (properties; absent in UE4 <4.25)
inline int USTRUCT_PROPSSIZE  = 0x58;

// === FField — defaults for UE5.0-5.1.0 (FFieldVariant=0x10) ===
// UE5.1.1+ shifts these: Next=0x18, Name=0x20
inline int FFIELD_CLASS       = 0x08;  // FFieldClass* — stable
inline int FFIELD_OWNER       = 0x10;  // FFieldVariant Owner — stable position, variable size
inline int FFIELD_NEXT        = 0x20;  // FField* next in chain
inline int FFIELD_NAME        = 0x28;  // FName

// === FProperty (inherits from FField) — defaults for UE5.0-5.1.0 ===
// UE5.1.1+ shifts these: ElemSize=0x34, Flags=0x38, Offset=0x44
inline int FPROPERTY_ELEMSIZE = 0x38;
inline int FPROPERTY_FLAGS    = 0x40;  // uint64 PropertyFlags
inline int FPROPERTY_OFFSET   = 0x4C;  // int32 Offset_Internal

// === FFieldClass — stable ===
inline int FFIELDCLASS_NAME   = 0x00;  // FName at start of FFieldClass

// === FStructProperty (subclass of FProperty) ===
// UScriptStruct* — first field after FProperty base layout.
// Derived from FPROPERTY_OFFSET + 0x2C (UE5.0: 0x78, UE5.1.1+: 0x70).
inline int FSTRUCTPROP_STRUCT = 0x78;

// === FBoolProperty layout (subclass of FProperty) ===
//   uint8 FieldSize, ByteOffset, ByteMask, FieldMask
// These 4 bytes are consecutive, located after the standard FProperty fields.
// Same offset as FSTRUCTPROP_STRUCT for most builds.
inline int FBOOLPROP_FIELDSIZE = 0x78;

// === UE4 UProperty offsets (UProperty inherits UObject → UField → UProperty) ===
// Used when bUseFProperty == false (UE4 <4.25).
// UField::Next is at UObject_TotalSize (0x28 or 0x30 for CPN).
inline int UFIELD_NEXT        = 0x28;  // UField::Next (standard): 0x28
inline int UPROPERTY_OFFSET   = 0x44;  // UProperty::Offset_Internal
inline int UPROPERTY_ELEMSIZE = 0x34;  // UProperty::ElementSize
inline int UPROPERTY_FLAGS    = 0x38;  // UProperty::PropertyFlags (uint64)

// === Detection state ===
inline bool bCasePreservingName  = false;  // FName is 0x10 bytes (CompIdx + DisplayIdx + Number + pad)
inline bool bUseFProperty        = true;   // true = FField/FProperty (UE4.25+), false = UProperty (UE4 <4.25)
// bOffsetsValidated is atomic with release/acquire ordering: the release-store after
// writing all DynOff values fences the preceding non-atomic writes, ensuring they are
// visible to any thread that acquire-loads this flag and sees 'true'.
inline std::atomic<bool> bOffsetsValidated{false};

} // namespace DynOff

namespace Constants {

// --- Object Array ---
constexpr int OBJECTS_PER_CHUNK        = 64 * 1024;

// --- FNamePool ---
constexpr int FNAME_CHUNK_SIZE         = 0x20000;  // 128 KB per chunk
constexpr int FNAME_STRIDE             = 2;         // Alignment stride

} // namespace Constants
