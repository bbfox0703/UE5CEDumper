// ============================================================
// UStructWalker.cpp — FField chain traversal implementation
// ============================================================

#include "UStructWalker.h"
#include "Memory.h"
#define LOG_CAT "WALK"
#include "Logger.h"
#include "Constants.h"
#include "FNamePool.h"
#include "OffsetFinder.h"

#include <algorithm>
#include <unordered_map>

#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif
#include <Windows.h>

namespace UStructWalker {

// Read FName from an address and resolve to string
static std::string ReadFName(uintptr_t fnameAddr) {
    // FName is typically: int32 ComparisonIndex, int32 Number
    int32_t compIndex = 0;
    int32_t number = 0;

    if (!Mem::ReadSafe(fnameAddr, compIndex)) return "";
    Mem::ReadSafe(fnameAddr + 4, number);

    return FNamePool::GetString(compIndex, number);
}

// ============================================================
// ResolveEnumValue — resolve an enum integer value to its name string.
// Uses a per-UEnum cache (static unordered_map) for performance.
// Triggers lazy DetectUEnumNames() on first call.
// ============================================================
static std::string ResolveEnumValue(uintptr_t enumAddr, int64_t value) {
    if (!enumAddr) return "";

    // Lazy init: trigger DetectUEnumNames on first call
    if (!DynOff::bUEnumNamesDetected.load(std::memory_order_acquire))
        OffsetFinder::DetectUEnumNames();
    if (!DynOff::bUEnumNamesDetected.load(std::memory_order_acquire))
        return "";  // Detection failed

    // Cache: keyed by UEnum address → vector of (value, name) pairs
    static std::unordered_map<uintptr_t, std::vector<std::pair<int64_t, std::string>>> s_cache;

    auto it = s_cache.find(enumAddr);
    if (it == s_cache.end()) {
        // Read UEnum::Names TArray<TPair<FName, int64>>
        uintptr_t data = 0;
        int32_t count = 0;
        Mem::ReadSafe(enumAddr + DynOff::UENUM_NAMES, data);
        Mem::ReadSafe(enumAddr + DynOff::UENUM_NAMES + 8, count);

        std::vector<std::pair<int64_t, std::string>> entries;
        if (data && count > 0 && count < 1024) {
            entries.reserve(count);
            for (int i = 0; i < count; ++i) {
                uintptr_t entryAddr = data + static_cast<uintptr_t>(i) * DynOff::UENUM_ENTRY_SIZE;
                int32_t nameIdx = 0;
                int64_t val = 0;
                Mem::ReadSafe(entryAddr, nameIdx);
                Mem::ReadSafe(entryAddr + 8, val);
                std::string name = FNamePool::GetString(nameIdx);
                entries.push_back({val, std::move(name)});
            }
            LOG_DEBUG("ResolveEnumValue: Cached UEnum 0x%llX with %d entries",
                static_cast<unsigned long long>(enumAddr), count);
        }
        it = s_cache.emplace(enumAddr, std::move(entries)).first;
    }

    // Lookup value
    for (const auto& [v, n] : it->second) {
        if (v == value) return n;
    }
    return "";  // Value not in enum
}

// ============================================================
// ReadFString — read an FString (TArray<wchar_t>) from a live
// instance and convert UTF-16 → UTF-8.
// Returns empty string on failure or if string is empty/too long.
// ============================================================
static std::string ReadFString(uintptr_t instanceAddr, int32_t offset) {
    // FString = TArray<wchar_t> = { wchar_t* Data (8B), int32 Count (4B), int32 Max (4B) }
    uintptr_t data = 0;
    int32_t count = 0;
    Mem::ReadSafe(instanceAddr + offset, data);
    Mem::ReadSafe(instanceAddr + offset + 8, count);

    if (!data || count <= 0 || count > 256) return "";

    // Read wchar_t buffer (count includes null terminator in most UE builds)
    std::vector<wchar_t> wbuf(count, 0);
    if (!Mem::ReadBytesSafe(data, wbuf.data(), count * sizeof(wchar_t)))
        return "";

    // Ensure null termination
    wbuf.back() = 0;

    // UTF-16 → UTF-8 via WideCharToMultiByte
    int needed = WideCharToMultiByte(CP_UTF8, 0, wbuf.data(), -1, nullptr, 0, nullptr, nullptr);
    if (needed <= 0) return "";
    std::string result(needed - 1, '\0');  // -1 to exclude null terminator
    WideCharToMultiByte(CP_UTF8, 0, wbuf.data(), -1, result.data(), needed, nullptr, nullptr);
    return result;
}

uintptr_t GetClass(uintptr_t uobjectAddr) {
    if (!uobjectAddr) return 0;
    uintptr_t cls = 0;
    Mem::ReadSafe(uobjectAddr + Constants::OFF_UOBJECT_CLASS, cls);
    return cls;
}

uintptr_t GetOuter(uintptr_t uobjectAddr) {
    if (!uobjectAddr) return 0;
    uintptr_t outer = 0;
    Mem::ReadSafe(uobjectAddr + DynOff::UOBJECT_OUTER, outer);
    return outer;
}

std::string GetName(uintptr_t uobjectAddr) {
    if (!uobjectAddr) return "";
    return ReadFName(uobjectAddr + Constants::OFF_UOBJECT_NAME);
}

int32_t GetIndex(uintptr_t uobjectAddr) {
    if (!uobjectAddr) return -1;
    int32_t index = -1;
    Mem::ReadSafe(uobjectAddr + Constants::OFF_UOBJECT_INDEX, index);
    return index;
}

std::string GetFullName(uintptr_t uobjectAddr) {
    if (!uobjectAddr) return "";

    // Build path by walking Outer chain
    std::vector<std::string> parts;
    uintptr_t current = uobjectAddr;

    // Safety limit to prevent infinite loops
    for (int i = 0; i < 64 && current != 0; ++i) {
        std::string name = GetName(current);
        if (name.empty()) break;
        parts.push_back(name);
        current = GetOuter(current);
    }

    // Reverse to get outermost first
    std::reverse(parts.begin(), parts.end());

    // Join with '/' for packages, '.' for subobjects
    // Convention: Package/SubPackage.ObjectName:SubObject
    std::string result;
    for (size_t i = 0; i < parts.size(); ++i) {
        if (i == 0) {
            result = "/" + parts[i];
        } else if (i == parts.size() - 1 && parts.size() > 2) {
            result += "." + parts[i];
        } else {
            result += "/" + parts[i];
        }
    }

    return result;
}

// Read the type name from an FFieldClass* (FProperty mode)
static std::string GetFieldTypeName(uintptr_t ffieldAddr) {
    // FField::ClassPrivate at offset 0x08 -> FFieldClass*
    uintptr_t fieldClass = 0;
    if (!Mem::ReadSafe(ffieldAddr + DynOff::FFIELD_CLASS, fieldClass) || !fieldClass) {
        return "Unknown";
    }

    // FFieldClass has Name (FName) at offset 0x00
    return ReadFName(fieldClass + DynOff::FFIELDCLASS_NAME);
}

// Walk the FField chain starting from the first field (UE4.25+ / UE5)
static void WalkFFieldChain(uintptr_t firstField, std::vector<FieldInfo>& fields) {
    uintptr_t current = firstField;
    int safetyLimit = 4096;

    while (current != 0 && safetyLimit-- > 0) {
        FieldInfo fi{};
        fi.Address = current;

        // Read field name
        fi.Name = ReadFName(current + DynOff::FFIELD_NAME);

        // Read type name from FFieldClass
        fi.TypeName = GetFieldTypeName(current);

        // Read offset and size (FProperty fields, may not be valid for non-property FFields)
        Mem::ReadSafe<int32_t>(current + DynOff::FPROPERTY_OFFSET, fi.Offset);
        Mem::ReadSafe<int32_t>(current + DynOff::FPROPERTY_ELEMSIZE, fi.Size);
        Mem::ReadSafe<uint64_t>(current + DynOff::FPROPERTY_FLAGS, fi.PropertyFlags);

        if (!fi.Name.empty()) {
            fields.push_back(fi);
        }

        // Move to next FField
        uintptr_t next = 0;
        if (!Mem::ReadSafe(current + DynOff::FFIELD_NEXT, next)) break;
        current = next;
    }
}

// Walk the UProperty chain (UE4 <4.25) — properties are UObject-derived (UField chain)
static void WalkUPropertyChain(uintptr_t firstField, std::vector<FieldInfo>& fields) {
    uintptr_t current = firstField;
    int safetyLimit = 4096;

    while (current != 0 && safetyLimit-- > 0) {
        FieldInfo fi{};
        fi.Address = current;

        // UProperty is UObject-derived, so Name is at UObject::Name
        fi.Name = ReadFName(current + Constants::OFF_UOBJECT_NAME);

        // Type name: UProperty's class name (e.g., "IntProperty", "FloatProperty")
        uintptr_t cls = 0;
        if (Mem::ReadSafe(current + Constants::OFF_UOBJECT_CLASS, cls) && cls) {
            fi.TypeName = ReadFName(cls + Constants::OFF_UOBJECT_NAME);
        } else {
            fi.TypeName = "Unknown";
        }

        // Read UProperty-specific fields
        Mem::ReadSafe<int32_t>(current + DynOff::UPROPERTY_OFFSET, fi.Offset);
        Mem::ReadSafe<int32_t>(current + DynOff::UPROPERTY_ELEMSIZE, fi.Size);
        Mem::ReadSafe<uint64_t>(current + DynOff::UPROPERTY_FLAGS, fi.PropertyFlags);

        if (!fi.Name.empty()) {
            fields.push_back(fi);
        }

        // Move to next UField via UField::Next
        uintptr_t next = 0;
        if (!Mem::ReadSafe(current + DynOff::UFIELD_NEXT, next)) break;
        current = next;
    }
}

ClassInfo WalkClass(uintptr_t uclassAddr) {
    ClassInfo info{};
    if (!uclassAddr) return info;

    info.Address = uclassAddr;
    info.Name = GetName(uclassAddr);
    info.FullPath = GetFullName(uclassAddr);

    // Read SuperStruct
    Mem::ReadSafe(uclassAddr + DynOff::USTRUCT_SUPER, info.SuperClass);
    if (info.SuperClass) {
        info.SuperName = GetName(info.SuperClass);
    }

    // Read PropertiesSize
    Mem::ReadSafe(uclassAddr + DynOff::USTRUCT_PROPSSIZE, info.PropertiesSize);

    LOG_DEBUG("WalkClass: %s (super=%s, size=%d) at 0x%llX",
              info.Name.c_str(), info.SuperName.c_str(), info.PropertiesSize,
              static_cast<unsigned long long>(uclassAddr));

    // Walk the property chain — dispatch based on UProperty vs FProperty mode
    if (DynOff::bUseFProperty) {
        // UE4.25+ / UE5: FField chain via ChildProperties
        uintptr_t childProps = 0;
        if (Mem::ReadSafe(uclassAddr + DynOff::USTRUCT_CHILDPROPS, childProps) && childProps) {
            WalkFFieldChain(childProps, info.Fields);
        }
    } else {
        // UE4 <4.25: UProperty chain via Children (UField chain includes properties)
        uintptr_t children = 0;
        if (Mem::ReadSafe(uclassAddr + DynOff::USTRUCT_CHILDREN, children) && children) {
            WalkUPropertyChain(children, info.Fields);
        }
    }

    // Walk inherited fields from SuperStruct chain
    uintptr_t super = info.SuperClass;
    int depth = 0;
    while (super != 0 && depth < 32) {
        if (DynOff::bUseFProperty) {
            uintptr_t superChildProps = 0;
            if (Mem::ReadSafe(super + DynOff::USTRUCT_CHILDPROPS, superChildProps) && superChildProps) {
                std::vector<FieldInfo> inherited;
                WalkFFieldChain(superChildProps, inherited);
                info.Fields.insert(info.Fields.begin(), inherited.begin(), inherited.end());
            }
        } else {
            uintptr_t superChildren = 0;
            if (Mem::ReadSafe(super + DynOff::USTRUCT_CHILDREN, superChildren) && superChildren) {
                std::vector<FieldInfo> inherited;
                WalkUPropertyChain(superChildren, inherited);
                info.Fields.insert(info.Fields.begin(), inherited.begin(), inherited.end());
            }
        }

        uintptr_t nextSuper = 0;
        Mem::ReadSafe(super + DynOff::USTRUCT_SUPER, nextSuper);
        super = nextSuper;
        ++depth;
    }

    // Sort by offset for clean display
    std::sort(info.Fields.begin(), info.Fields.end(),
              [](const FieldInfo& a, const FieldInfo& b) { return a.Offset < b.Offset; });

    LOG_INFO("WalkClass: %s — %zu fields", info.Name.c_str(), info.Fields.size());
    return info;
}

// --- Live Instance Walking ---

std::string InterpretValue(const std::string& typeName, const void* data, int32_t size) {
    if (!data || size <= 0) return "";

    auto bytes = static_cast<const uint8_t*>(data);

    if (typeName == "FloatProperty" && size >= 4) {
        float v;
        memcpy(&v, bytes, 4);
        // 10 decimal places; if fractional part is all zeros, show as integer
        char buf[64];
        snprintf(buf, sizeof(buf), "%.10f", v);
        std::string s(buf);
        auto dot = s.find('.');
        if (dot != std::string::npos) {
            bool allZero = true;
            for (size_t i = dot + 1; i < s.size(); ++i) {
                if (s[i] != '0') { allZero = false; break; }
            }
            if (allZero) s.erase(dot);
        }
        return s;
    }
    if (typeName == "DoubleProperty" && size >= 8) {
        double v;
        memcpy(&v, bytes, 8);
        // 15 decimal places; if fractional part is all zeros, show as integer
        char buf[80];
        snprintf(buf, sizeof(buf), "%.15f", v);
        std::string s(buf);
        auto dot = s.find('.');
        if (dot != std::string::npos) {
            bool allZero = true;
            for (size_t i = dot + 1; i < s.size(); ++i) {
                if (s[i] != '0') { allZero = false; break; }
            }
            if (allZero) s.erase(dot);
        }
        return s;
    }
    if (typeName == "IntProperty" && size >= 4) {
        int32_t v;
        memcpy(&v, bytes, 4);
        return std::to_string(v);
    }
    if (typeName == "UInt32Property" && size >= 4) {
        uint32_t v;
        memcpy(&v, bytes, 4);
        return std::to_string(v);
    }
    if (typeName == "Int64Property" && size >= 8) {
        int64_t v;
        memcpy(&v, bytes, 8);
        // std::to_string never uses scientific notation for integers
        return std::to_string(v);
    }
    if (typeName == "UInt64Property" && size >= 8) {
        uint64_t v;
        memcpy(&v, bytes, 8);
        return std::to_string(v);
    }
    if (typeName == "Int16Property" && size >= 2) {
        int16_t v;
        memcpy(&v, bytes, 2);
        return std::to_string(v);
    }
    if (typeName == "UInt16Property" && size >= 2) {
        uint16_t v;
        memcpy(&v, bytes, 2);
        return std::to_string(v);
    }
    if (typeName == "ByteProperty" && size >= 1) {
        return std::to_string(bytes[0]);
    }
    if (typeName == "Int8Property" && size >= 1) {
        return std::to_string(static_cast<int8_t>(bytes[0]));
    }
    if (typeName == "BoolProperty") {
        // Note: for bitfield bools, the caller should pass the correct byte
        // and use FieldMask to determine the bit value. This fallback handles
        // the simple case where the raw byte is passed.
        return bytes[0] ? "true" : "false";
    }
    if (typeName == "NameProperty" && size >= 4) {
        // FName — resolve via FNamePool
        int32_t nameIdx;
        memcpy(&nameIdx, bytes, 4);
        return FNamePool::GetString(nameIdx);
    }

    // StructProperty: for small structs, show inline float hints
    // Many gameplay structs (FGameplayAttributeData, FVector, FRotator, etc.)
    // contain float fields. Show a summary like "f:[100.0, 100.0]" for quick analysis.
    if (typeName == "StructProperty" && size >= 4) {
        // Skip the first 8 bytes if size > 8 — often a vtable/pointer preamble.
        // For structs <= 8 bytes, interpret from byte 0.
        int floatStart = (size > 8) ? 8 : 0;
        int floatCount = (size - floatStart) / 4;
        if (floatCount > 0 && floatCount <= 16) {
            // Check if at least one float in range looks meaningful (not 0, not NaN/garbage)
            bool anyMeaningful = false;
            for (int i = 0; i < floatCount; ++i) {
                float v;
                memcpy(&v, bytes + floatStart + i * 4, 4);
                if (v != 0.0f && v == v && v > -1e12f && v < 1e12f) { // not zero, not NaN, reasonable range
                    anyMeaningful = true;
                    break;
                }
            }
            if (anyMeaningful) {
                std::string hint = "f:[";
                for (int i = 0; i < floatCount; ++i) {
                    if (i > 0) hint += ", ";
                    float v;
                    memcpy(&v, bytes + floatStart + i * 4, 4);
                    char buf[48];
                    // No scientific notation — fixed 4 decimal places
                    snprintf(buf, sizeof(buf), "%.4f", v);
                    hint += buf;
                }
                hint += "]";
                return hint;
            }
        }
    }

    return ""; // Unknown type — caller shows hex
}

// ============================================================
// IsScalarArrayType — check if an inner type name supports inline
// element reading (Phase B). Returns true for numeric, bool, enum,
// and name types. StructProperty, ObjectProperty, MapProperty, etc.
// are NOT scalar and are handled in Phase D/E.
// ============================================================
bool IsScalarArrayType(const std::string& innerTypeName) {
    return innerTypeName == "FloatProperty"
        || innerTypeName == "DoubleProperty"
        || innerTypeName == "IntProperty"
        || innerTypeName == "UInt32Property"
        || innerTypeName == "Int64Property"
        || innerTypeName == "UInt64Property"
        || innerTypeName == "Int16Property"
        || innerTypeName == "UInt16Property"
        || innerTypeName == "ByteProperty"
        || innerTypeName == "Int8Property"
        || innerTypeName == "BoolProperty"
        || innerTypeName == "NameProperty"
        || innerTypeName == "EnumProperty";
}

// ============================================================
// ReadArrayElements — read scalar elements from a TArray (Phase B).
//
// Reads up to `limit` elements starting at index `offset`.
// For EnumProperty / ByteProperty-with-enum, resolves enum names
// via the UEnum* stored in the Inner FProperty.
// ============================================================
ReadArrayResult ReadArrayElements(
    uintptr_t instanceAddr, int32_t fieldOffset,
    uintptr_t innerFFieldAddr, const std::string& innerTypeName,
    int32_t elemSize, int32_t offset, int32_t limit)
{
    ReadArrayResult result;
    result.ok = false;

    if (elemSize <= 0 || elemSize > 256) {
        result.error = "Invalid element size";
        return result;
    }

    // Read TArray header
    Mem::TArrayView arr;
    if (!Mem::ReadTArray(instanceAddr + fieldOffset, arr)) {
        result.error = "TArray read failed";
        return result;
    }
    result.totalCount = arr.Count;

    if (arr.Count <= 0 || !arr.Data) {
        result.ok = true;
        result.readCount = 0;
        return result;
    }

    // Clamp offset/limit
    if (offset < 0) offset = 0;
    if (offset >= arr.Count) {
        result.ok = true;
        result.readCount = 0;
        return result;
    }
    int32_t end = offset + limit;
    if (end > arr.Count) end = arr.Count;
    if (end - offset > 256) end = offset + 256;  // hard cap per request

    // For enum arrays: read UEnum* once from Inner FProperty
    uintptr_t enumPtr = 0;
    if (innerFFieldAddr) {
        if (innerTypeName == "EnumProperty") {
            Mem::ReadSafe(innerFFieldAddr + DynOff::FENUMPROP_ENUM, enumPtr);
        } else if (innerTypeName == "ByteProperty") {
            uintptr_t candidateEnum = 0;
            if (Mem::ReadSafe(innerFFieldAddr + DynOff::FBYTEPROP_ENUM, candidateEnum) && candidateEnum) {
                // Validate it's a UEnum
                uintptr_t enumClass = GetClass(candidateEnum);
                std::string enumClassName = enumClass ? GetName(enumClass) : "";
                if (enumClassName == "Enum" || enumClassName == "UserDefinedEnum")
                    enumPtr = candidateEnum;
            }
        }
    }

    // Read elements
    std::vector<uint8_t> buf(elemSize, 0);
    result.elements.reserve(end - offset);

    for (int32_t i = offset; i < end; ++i) {
        LiveFieldValue::ArrayElement elem;
        elem.index = i;

        uintptr_t elemAddr = arr.Data + static_cast<int64_t>(i) * elemSize;
        if (!Mem::ReadBytesSafe(elemAddr, buf.data(), elemSize)) {
            elem.value = "???";
            elem.hex = "??";
            result.elements.push_back(std::move(elem));
            continue;
        }

        // Build per-element hex string
        std::string hex;
        hex.reserve(elemSize * 2);
        for (int b = 0; b < elemSize; ++b) {
            char hx[3];
            snprintf(hx, sizeof(hx), "%02X", buf[b]);
            hex += hx;
        }
        elem.hex = std::move(hex);

        // Interpret value
        if (enumPtr) {
            // Enum element: read raw integer value and resolve name
            int64_t rawVal = 0;
            if (elemSize == 1) rawVal = buf[0];
            else if (elemSize == 2) { int16_t v; memcpy(&v, buf.data(), 2); rawVal = v; }
            else if (elemSize == 4) { int32_t v; memcpy(&v, buf.data(), 4); rawVal = v; }
            else if (elemSize == 8) { int64_t v; memcpy(&v, buf.data(), 8); rawVal = v; }
            elem.enumName = ResolveEnumValue(enumPtr, rawVal);
            elem.value = elem.enumName.empty() ? std::to_string(rawVal) : elem.enumName;
        } else {
            elem.value = InterpretValue(innerTypeName, buf.data(), elemSize);
        }

        result.elements.push_back(std::move(elem));
    }

    result.ok = true;
    result.readCount = static_cast<int32_t>(result.elements.size());
    return result;
}

// ============================================================
// CorrectSubclassOffsets — one-time calibration of FSTRUCTPROP_STRUCT
// and related subclass extension offsets.
//
// The derivation formula (Offset_Internal + 0x2C) may be wrong for
// newer UE versions (e.g., UE5.7 uses +0x30). We calibrate by probing
// a known StructProperty FField for the UScriptStruct* pointer.
// If a non-zero delta is found, all subclass offsets are updated.
// ============================================================
static void CorrectSubclassOffsets(const std::vector<FieldInfo>& fields) {
    static std::atomic<bool> s_checked{false};
    if (s_checked.load(std::memory_order_acquire)) return;
    if (!DynOff::bUseFProperty) { s_checked.store(true, std::memory_order_release); return; }

    static const int kProbeDeltas[] = { 0, 4, -4, 8, -8, 0xC, -0xC };
    for (const auto& fi : fields) {
        if (fi.TypeName != "StructProperty") continue;

        for (int delta : kProbeDeltas) {
            int tryOff = DynOff::FSTRUCTPROP_STRUCT + delta;
            if (tryOff < 0) continue;
            uintptr_t candidate = 0;
            if (!Mem::ReadSafe(fi.Address + tryOff, candidate) || !candidate) continue;
            // Validate: must be a UScriptStruct (UObject) with a readable ASCII name
            std::string sname = GetName(candidate);
            if (sname.empty() || sname[0] < 0x20 || sname[0] >= 0x7F) continue;

            if (delta != 0) {
                int corrected = DynOff::FSTRUCTPROP_STRUCT + delta;
                Logger::Info("WALK", "CorrectSubclassOffsets: delta=%d, FSTRUCTPROP 0x%X -> 0x%X (validated with '%s' -> '%s')",
                    delta, DynOff::FSTRUCTPROP_STRUCT, corrected, fi.Name.c_str(), sname.c_str());
                DynOff::FSTRUCTPROP_STRUCT  = corrected;
                // Note: FARRAYPROP_INNER may differ from FSTRUCTPROP_STRUCT (UE5.7 has
                // EArrayPropertyFlags before Inner). Set it to same base; the ArrayProperty
                // probe will try delta=8 to account for this.
                DynOff::FARRAYPROP_INNER   = corrected;
                DynOff::FBOOLPROP_FIELDSIZE = corrected;
                DynOff::FENUMPROP_ENUM     = corrected;
                DynOff::FBYTEPROP_ENUM     = corrected;
            }
            s_checked.store(true, std::memory_order_release);
            return;
        }
        // This StructProperty probe failed — try next one
    }
    // No StructProperty found in this class; will retry on next WalkInstance call
}

InstanceWalkResult WalkInstance(uintptr_t instanceAddr, uintptr_t classAddr) {
    InstanceWalkResult result;
    result.addr = instanceAddr;

    if (!instanceAddr) return result;

    if (!classAddr)
        classAddr = GetClass(instanceAddr);

    result.classAddr = classAddr;
    result.name      = GetName(instanceAddr);
    result.className = classAddr ? GetName(classAddr) : "";

    // Read OuterPrivate
    uintptr_t outerAddr = GetOuter(instanceAddr);
    result.outerAddr = outerAddr;
    if (outerAddr) {
        result.outerName      = GetName(outerAddr);
        uintptr_t outerClass  = GetClass(outerAddr);
        result.outerClassName = outerClass ? GetName(outerClass) : "";
    }

    // Walk the class to get field layout
    ClassInfo ci = WalkClass(classAddr);

    // Pre-pass: calibrate subclass extension offsets using StructProperty probe.
    // Must run BEFORE the main loop so ArrayProperty fields use corrected offsets.
    CorrectSubclassOffsets(ci.Fields);

    for (const auto& fi : ci.Fields) {
        LiveFieldValue fv;
        fv.name     = fi.Name;
        fv.typeName = fi.TypeName;
        fv.offset   = fi.Offset;
        fv.size     = fi.Size;

        // Handle ObjectProperty: read pointer, resolve name/class
        if (fi.TypeName == "ObjectProperty" || fi.TypeName == "ClassProperty" ||
            fi.TypeName == "SoftObjectProperty" || fi.TypeName == "WeakObjectProperty" ||
            fi.TypeName == "LazyObjectProperty") {
            uintptr_t ptr = 0;
            if (fi.Size >= 8 && Mem::ReadSafe(instanceAddr + fi.Offset, ptr) && ptr) {
                fv.ptrValue = ptr;
                fv.ptrName = GetName(ptr);
                fv.ptrClassName = "";
                uintptr_t ptrCls = GetClass(ptr);
                if (ptrCls) fv.ptrClassName = GetName(ptrCls);
            }
            // Hex of the pointer
            if (fi.Size >= 8) {
                char buf[20];
                snprintf(buf, sizeof(buf), "%016llX", static_cast<unsigned long long>(ptr));
                fv.hexValue = buf;
            }
            result.fields.push_back(std::move(fv));
            continue;
        }

        // Handle ArrayProperty: read TArray header + Inner element type
        if (fi.TypeName == "ArrayProperty") {
            Mem::TArrayView arr;
            if (Mem::ReadTArray(instanceAddr + fi.Offset, arr)) {
                fv.arrayCount = arr.Count;
                // Hex of the TArray header (Data ptr + Count + Max)
                char buf[48];
                snprintf(buf, sizeof(buf), "%016llX %08X %08X",
                    static_cast<unsigned long long>(arr.Data), arr.Count, arr.Max);
                fv.hexValue = buf;
            } else {
                fv.arrayCount = 0;
            }

            // Read FArrayProperty::Inner (FProperty*) to get element type info.
            // Note: In UE5.7+, FArrayProperty may store EArrayPropertyFlags (4B + 4B pad)
            // BEFORE Inner, so Inner can be at FARRAYPROP_INNER + 8. The probe list
            // includes delta=8 to handle this.  Delta=0xC covers the case where the base
            // offset hasn't been corrected yet (0x74 + 0xC = 0x80 for TQ2).
            if (DynOff::bUseFProperty) {
                static const int kInnerProbeOffsets[] = { 0, 8, 4, 0xC, -4, -8, 0x10, -0x10 };
                bool innerFound = false;
                for (int delta : kInnerProbeOffsets) {
                    int tryOff = DynOff::FARRAYPROP_INNER + delta;
                    if (tryOff < 0) continue;
                    uintptr_t inner = 0;
                    if (!Mem::ReadSafe(fi.Address + tryOff, inner) || !inner) continue;

                    // Validate: Inner must be an FField with a readable FFieldClass name
                    std::string innerTypeName = GetFieldTypeName(inner);
                    Logger::Debug("WALK:ArrayP", "  probe delta=%d off=0x%X inner=0x%llX typeName='%s'",
                        delta, tryOff, static_cast<unsigned long long>(inner), innerTypeName.c_str());

                    if (!innerTypeName.empty() && innerTypeName != "Unknown"
                        && innerTypeName.find("Property") != std::string::npos) {
                        fv.arrayInnerType = innerTypeName;

                        // Read element size from Inner FProperty
                        Mem::ReadSafe<int32_t>(inner + DynOff::FPROPERTY_ELEMSIZE, fv.arrayElemSize);

                        // If inner is StructProperty, also read the UScriptStruct name
                        if (innerTypeName == "StructProperty") {
                            uintptr_t innerStruct = 0;
                            if (Mem::ReadSafe(inner + DynOff::FSTRUCTPROP_STRUCT, innerStruct) && innerStruct) {
                                fv.arrayInnerStructType = GetName(innerStruct);
                            }
                        }

                        Logger::Info("WALK:ArrayP", "FArrayProperty::Inner found at FField+0x%X (delta=%d) for '%s' -> '%s' elemSize=%d",
                            tryOff, delta, fi.Name.c_str(), innerTypeName.c_str(), fv.arrayElemSize);
                        // Persist corrected FARRAYPROP_INNER if delta != 0
                        if (delta != 0) {
                            Logger::Info("WALK:ArrayP", "Correcting FARRAYPROP_INNER: 0x%X -> 0x%X",
                                DynOff::FARRAYPROP_INNER, tryOff);
                            DynOff::FARRAYPROP_INNER = tryOff;
                        }
                        fv.arrayInnerFFieldAddr = inner;
                        innerFound = true;
                        break;
                    }
                }

                // Phase B: read inline scalar element values (up to 64)
                if (innerFound && IsScalarArrayType(fv.arrayInnerType)
                    && arr.Data && fv.arrayCount > 0 && fv.arrayCount <= 64
                    && fv.arrayElemSize > 0) {
                    auto elemResult = ReadArrayElements(
                        instanceAddr, fi.Offset,
                        fv.arrayInnerFFieldAddr, fv.arrayInnerType,
                        fv.arrayElemSize, 0, 64);
                    if (elemResult.ok && !elemResult.elements.empty()) {
                        fv.arrayElements = std::move(elemResult.elements);
                        Logger::Debug("WALK:ArrayP", "Inline elements: %d read for '%s'",
                            static_cast<int>(fv.arrayElements.size()), fi.Name.c_str());
                    }
                }

                if (!innerFound) {
                    // Diagnostic: hex dump around FARRAYPROP_INNER to help identify correct offset
                    uint8_t dumpBuf[64] = {};
                    int dumpStart = DynOff::FARRAYPROP_INNER - 16;
                    if (dumpStart < 0) dumpStart = 0;
                    Mem::ReadBytesSafe(fi.Address + dumpStart, dumpBuf, 64);
                    char hexDump[200] = {};
                    for (int i = 0; i < 64 && i < (int)sizeof(hexDump)/3; i++)
                        snprintf(hexDump + i*3, 4, "%02X ", dumpBuf[i]);
                    Logger::Info("WALK:ArrayP", "Inner NOT found for '%s' (FField=0x%llX, FARRAYPROP_INNER=0x%X, FSTRUCTPROP_STRUCT=0x%X)",
                        fi.Name.c_str(), static_cast<unsigned long long>(fi.Address),
                        DynOff::FARRAYPROP_INNER, DynOff::FSTRUCTPROP_STRUCT);
                    Logger::Info("WALK:ArrayP", "  hex @+0x%X..+0x%X: %s", dumpStart, dumpStart+64, hexDump);
                }
            }

            result.fields.push_back(std::move(fv));
            continue;
        }

        // Handle StructProperty: extract inner UScriptStruct* for navigation
        if (fi.TypeName == "StructProperty") {
            // Try the derived offset first, then probe nearby offsets
            static const int kStructPtrProbeOffsets[] = { 0, 4, -4, 8, -8, 0x10, -0x10 };
            bool found = false;
            for (int delta : kStructPtrProbeOffsets) {
                int tryOffset = DynOff::FSTRUCTPROP_STRUCT + delta;
                if (tryOffset < 0) continue;
                uintptr_t candidate = 0;
                if (!Mem::ReadSafe(fi.Address + tryOffset, candidate) || !candidate) continue;
                // Validate: must be a UScriptStruct (inherits UObject), so GetName should return ASCII
                std::string sname = GetName(candidate);
                if (!sname.empty() && sname[0] >= 0x20 && sname[0] < 0x7F) {
                    fv.structClassAddr = candidate;
                    fv.structTypeName  = sname;
                    fv.structDataAddr  = instanceAddr + fi.Offset;
                    if (delta != 0) {
                        Logger::Info("WALK:StructP", "FStructProperty::Struct at FField+0x%X (base=0x%X, delta=%d) for '%s' -> '%s'",
                            tryOffset, DynOff::FSTRUCTPROP_STRUCT, delta, fi.Name.c_str(), sname.c_str());
                        // Persist correction to DynOff (CorrectSubclassOffsets handles the global
                        // update, but if it didn't run yet or missed, update here too)
                        DynOff::FSTRUCTPROP_STRUCT = tryOffset;
                    }
                    found = true;
                    break;
                }
            }
            if (!found) {
                Logger::Debug("WALK:StructP", "FStructProperty::Struct not found for '%s' (FField=0x%llX, probed 0x%X +/- 16)",
                    fi.Name.c_str(), static_cast<unsigned long long>(fi.Address), DynOff::FSTRUCTPROP_STRUCT);
            }
            // Fall through to read hex value below
        }

        // BoolProperty: extract FieldMask/ByteOffset for bitfield display
        if (fi.TypeName == "BoolProperty") {
            // FBoolProperty layout after FProperty base:
            //   uint8 FieldSize, ByteOffset, ByteMask, FieldMask
            // Try known offset first, then probe nearby if needed
            uint8_t boolBytes[4] = {};
            bool boolInfoRead = false;

            for (int tryOff : { DynOff::FBOOLPROP_FIELDSIZE, DynOff::FBOOLPROP_FIELDSIZE - 4,
                                DynOff::FBOOLPROP_FIELDSIZE + 4, DynOff::FBOOLPROP_FIELDSIZE + 8 }) {
                if (tryOff < 0) continue;
                if (!Mem::ReadBytesSafe(fi.Address + tryOff, boolBytes, 4)) continue;

                uint8_t fieldSize  = boolBytes[0];
                uint8_t byteOff    = boolBytes[1];
                uint8_t byteMask   = boolBytes[2];
                uint8_t fieldMask  = boolBytes[3];

                // Validate: FieldSize should be 1 (single byte), ByteOffset typically 0-7,
                // FieldMask should be a single bit (power of 2) and non-zero
                if (fieldSize == 1 && fieldMask != 0 && (fieldMask & (fieldMask - 1)) == 0 &&
                    byteOff <= 7 && byteMask != 0 && (byteMask & (byteMask - 1)) == 0) {
                    fv.boolFieldMask = fieldMask;
                    fv.boolByteOffset = byteOff;

                    // Compute bit index from FieldMask
                    int bitIdx = 0;
                    uint8_t mask = fieldMask;
                    while (mask > 1) { mask >>= 1; ++bitIdx; }
                    fv.boolBitIndex = bitIdx;

                    boolInfoRead = true;
                    break;
                }
            }

            // Read actual value using FieldMask
            uint8_t rawByte = 0;
            int readOffset = fi.Offset + fv.boolByteOffset;
            if (Mem::ReadSafe(instanceAddr + readOffset, rawByte)) {
                char hexBuf[3];
                snprintf(hexBuf, sizeof(hexBuf), "%02X", rawByte);
                fv.hexValue = hexBuf;

                if (boolInfoRead) {
                    bool value = (rawByte & fv.boolFieldMask) != 0;
                    char desc[64];
                    snprintf(desc, sizeof(desc), "%s (bit %d, mask 0x%02X)",
                             value ? "true" : "false", fv.boolBitIndex, fv.boolFieldMask);
                    fv.typedValue = desc;
                } else {
                    fv.typedValue = rawByte ? "true" : "false";
                }
            }

            result.fields.push_back(std::move(fv));
            continue;
        }

        // Handle EnumProperty: read underlying int, resolve via UEnum
        if (fi.TypeName == "EnumProperty") {
            uintptr_t enumPtr = 0;
            Mem::ReadSafe(fi.Address + DynOff::FENUMPROP_ENUM, enumPtr);

            // Read raw value based on property size
            int64_t rawVal = 0;
            if (fi.Size == 1) { uint8_t v = 0; Mem::ReadSafe(instanceAddr + fi.Offset, v); rawVal = v; }
            else if (fi.Size == 2) { int16_t v = 0; Mem::ReadSafe(instanceAddr + fi.Offset, v); rawVal = v; }
            else if (fi.Size == 4) { int32_t v = 0; Mem::ReadSafe(instanceAddr + fi.Offset, v); rawVal = v; }
            else if (fi.Size == 8) { int64_t v = 0; Mem::ReadSafe(instanceAddr + fi.Offset, v); rawVal = v; }

            fv.enumValue = rawVal;
            if (enumPtr) fv.enumName = ResolveEnumValue(enumPtr, rawVal);
            fv.typedValue = fv.enumName.empty() ? std::to_string(rawVal) : fv.enumName;

            // Populate hex
            if (fi.Size > 0 && fi.Size <= 8) {
                uint8_t buf[8] = {};
                Mem::ReadBytesSafe(instanceAddr + fi.Offset, buf, fi.Size);
                std::string hex;
                hex.reserve(fi.Size * 2);
                for (int i = 0; i < fi.Size; ++i) { char hx[3]; snprintf(hx, sizeof(hx), "%02X", buf[i]); hex += hx; }
                fv.hexValue = hex;
            }
            result.fields.push_back(std::move(fv));
            continue;
        }

        // Handle ByteProperty: check if it has a UEnum* (byte-sized enum)
        if (fi.TypeName == "ByteProperty") {
            uintptr_t enumPtr = 0;
            Mem::ReadSafe(fi.Address + DynOff::FBYTEPROP_ENUM, enumPtr);
            if (enumPtr) {
                // Validate it's actually a UEnum by checking its class name
                uintptr_t enumClass = GetClass(enumPtr);
                std::string enumClassName = enumClass ? GetName(enumClass) : "";
                if (enumClassName == "Enum" || enumClassName == "UserDefinedEnum") {
                    uint8_t rawVal = 0;
                    Mem::ReadSafe(instanceAddr + fi.Offset, rawVal);
                    fv.enumValue = rawVal;
                    fv.enumName = ResolveEnumValue(enumPtr, rawVal);
                    fv.typedValue = fv.enumName.empty() ? std::to_string(rawVal) : fv.enumName;
                    char hx[3];
                    snprintf(hx, sizeof(hx), "%02X", rawVal);
                    fv.hexValue = hx;
                    result.fields.push_back(std::move(fv));
                    continue;
                }
            }
            // Fall through to generic scalar handling below
        }

        // Handle StrProperty / TextProperty: read FString (TArray<wchar_t>) → UTF-8
        if (fi.TypeName == "StrProperty" || fi.TypeName == "TextProperty") {
            fv.strValue = ReadFString(instanceAddr, fi.Offset);
            fv.typedValue = fv.strValue.empty() ? "(empty)" : fv.strValue;
            // Hex of the TArray<wchar_t> header (Data ptr + Count)
            uintptr_t strData = 0;
            int32_t strCount = 0;
            Mem::ReadSafe(instanceAddr + fi.Offset, strData);
            Mem::ReadSafe(instanceAddr + fi.Offset + 8, strCount);
            char buf[48];
            snprintf(buf, sizeof(buf), "%016llX %08X",
                static_cast<unsigned long long>(strData), strCount);
            fv.hexValue = buf;
            result.fields.push_back(std::move(fv));
            continue;
        }

        // Scalar or struct: read raw bytes and interpret
        if (fi.Size > 0 && fi.Size <= 256) {
            std::vector<uint8_t> buf(fi.Size, 0);
            if (Mem::ReadBytesSafe(instanceAddr + fi.Offset, buf.data(), fi.Size)) {
                // Build hex string
                std::string hex;
                hex.reserve(fi.Size * 2);
                for (auto b : buf) {
                    char hx[3];
                    snprintf(hx, sizeof(hx), "%02X", b);
                    hex += hx;
                }
                fv.hexValue = hex;
                fv.typedValue = InterpretValue(fi.TypeName, buf.data(), fi.Size);
            }
        }

        result.fields.push_back(std::move(fv));
    }

    LOG_DEBUG("WalkInstance: %s (%s) — %zu fields at 0x%llX",
              result.name.c_str(), result.className.c_str(),
              result.fields.size(), static_cast<unsigned long long>(instanceAddr));
    return result;
}

} // namespace UStructWalker
