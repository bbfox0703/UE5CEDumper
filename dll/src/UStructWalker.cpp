// ============================================================
// UStructWalker.cpp — FField chain traversal implementation
// ============================================================

#include "UStructWalker.h"
#include "Memory.h"
#define LOG_CAT "WALK"
#include "Logger.h"
#include "Constants.h"
#include "FNamePool.h"
#include "ObjectArray.h"
#include "OffsetFinder.h"

#include <algorithm>
#include <unordered_map>

#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif
#include <Windows.h>

// Defined in ExportAPI.cpp — cached UE version for layout branching
extern uint32_t g_cachedUEVersion;

namespace UStructWalker {

// File-scope enum cache: keyed by UEnum* → vector of (value, name) pairs.
// Shared between ResolveEnumValue (lookup) and GetEnumEntries (full list export).
static std::unordered_map<uintptr_t, std::vector<std::pair<int64_t, std::string>>> s_enumCache;

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

    auto it = s_enumCache.find(enumAddr);
    if (it == s_enumCache.end()) {
        // Read UEnum::Names TArray<TPair<FName, int64>>
        uintptr_t data = 0;
        int32_t count = 0;
        Mem::ReadSafe(enumAddr + DynOff::UENUM_NAMES, data);
        Mem::ReadSafe(enumAddr + DynOff::UENUM_NAMES + 8, count);

        std::vector<std::pair<int64_t, std::string>> entries;
        if (data && count > 0 && count < 16384) {
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
        it = s_enumCache.emplace(enumAddr, std::move(entries)).first;
    }

    // Lookup value
    for (const auto& [v, n] : it->second) {
        if (v == value) return n;
    }
    return "";  // Value not in enum
}

// ============================================================
// GetEnumEntries — return all cached entries for a UEnum address.
// Triggers cache population if not yet cached.
// Used by PipeServer to send full enum lists for CE DropDownList.
// ============================================================
std::vector<LiveFieldValue::EnumEntry> GetEnumEntries(uintptr_t enumAddr) {
    if (!enumAddr) return {};

    // Trigger cache population (value -999999 won't match any real enum entry)
    ResolveEnumValue(enumAddr, -999999);

    auto it = s_enumCache.find(enumAddr);
    if (it == s_enumCache.end()) return {};

    std::vector<LiveFieldValue::EnumEntry> result;
    result.reserve(it->second.size());
    for (const auto& [v, n] : it->second)
        result.push_back({v, n});
    return result;
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

// ============================================================
// ReadSoftObjectPath — resolve FSoftObjectPath at the given
// address to a human-readable asset path string.
//
// UE4 / UE5.0: FSoftObjectPath = { FName AssetPathName; FString SubPathString; }
// UE5.1+:      FSoftObjectPath = { FTopLevelAssetPath { FName PackageName; FName AssetName; }; FString SubPathString; }
// ============================================================
static std::string ReadSoftObjectPath(uintptr_t addr) {
    if (!addr) return "";

    int fnameSize = DynOff::bCasePreservingName ? 0x10 : 0x08;

    bool isTopLevelAssetPath = (g_cachedUEVersion >= 501);

    if (isTopLevelAssetPath) {
        // UE5.1+: FTopLevelAssetPath = { FName PackageName, FName AssetName }
        std::string packageName = ReadFName(addr);
        std::string assetName   = ReadFName(addr + fnameSize);

        if (packageName.empty() || packageName == "None") return "";
        if (assetName.empty() || assetName == "None")
            return packageName;
        return packageName + "." + assetName;
    } else {
        // UE4 / UE5.0: FName AssetPathName
        std::string assetPathName = ReadFName(addr);
        if (!assetPathName.empty() && assetPathName != "None")
            return assetPathName;
        // Fallback: try UE5.1+ layout in case version was misdetected
        std::string packageName = ReadFName(addr);
        std::string assetName   = ReadFName(addr + fnameSize);
        if (!packageName.empty() && packageName != "None"
            && !assetName.empty() && assetName != "None")
            return packageName + "." + assetName;
        return "";
    }
}

// ============================================================
// ReadFTextString — read the display string from an FText.
//
// FText = { ITextData* Data (8B); void* SharedRefController (8B); ... }
// ITextData contains an FString at a version-dependent offset.
// We probe common offsets within ITextData to find the FString.
// ============================================================
static std::string ReadFTextString(uintptr_t ftextAddr) {
    if (!ftextAddr) return "";

    // Read ITextData* (first 8 bytes of FText)
    uintptr_t textDataPtr = 0;
    if (!Mem::ReadSafe(ftextAddr, textDataPtr) || !textDataPtr) return "";

    // Probe ITextData at multiple offsets for a valid FString
    // FString = { wchar_t* Data (8B), int32 Count (4B), int32 Max (4B) }
    static const int probeOffsets[] = { 0x28, 0x30, 0x38, 0x40, 0x20, 0x48 };

    for (int offset : probeOffsets) {
        uintptr_t strData = 0;
        int32_t   strCount = 0;
        int32_t   strMax = 0;

        if (!Mem::ReadSafe(textDataPtr + offset, strData) || !strData) continue;
        if (!Mem::ReadSafe(textDataPtr + offset + 8, strCount)) continue;
        if (!Mem::ReadSafe(textDataPtr + offset + 12, strMax)) continue;

        // Validate: reasonable FString (non-null data, positive count, count <= max)
        if (strCount <= 0 || strCount > 4096 || strMax < strCount) continue;

        // Try to read the FString at this offset
        std::string result = ReadFString(textDataPtr, offset);
        if (!result.empty())
            return result;
    }

    return "";
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

// Read the type name from a UProperty* (UObject subclass, UE4 UProperty mode).
// UProperty inherits UObject, so its Class at +0x10 is a UClass whose Name is the type.
static std::string GetUPropertyTypeName(uintptr_t upropAddr) {
    uintptr_t cls = 0;
    if (!Mem::ReadSafe(upropAddr + Constants::OFF_UOBJECT_CLASS, cls) || !cls) return "";
    if (cls < 0x10000 || cls > 0x00007FFFFFFFFFFF) return "";

    uint32_t nameIdx = 0;
    if (!Mem::ReadSafe(cls + Constants::OFF_UOBJECT_NAME, nameIdx)) return "";
    return FNamePool::GetString(nameIdx);
}

// Walk the FField chain starting from the first field (UE4.25+ / UE5)
static void WalkFFieldChain(uintptr_t firstField, std::vector<FieldInfo>& fields) {
    // UE5.3+: ChildProperties may come from an FFieldVariant read — strip tag bit defensively
    uintptr_t current = DynOff::StripFFieldTag(firstField);
    int safetyLimit = 4096;

    while (current != 0 && safetyLimit-- > 0) {
        // UE5.3+: if tag bit indicates UObject rather than FField, skip this entry
        if (DynOff::IsFFieldVariantUObject(current)) break;

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

        // Move to next FField (strip tag bit for UE5.3+ safety)
        uintptr_t next = 0;
        if (!Mem::ReadSafe(current + DynOff::FFIELD_NEXT, next)) break;
        current = DynOff::StripFFieldTag(next);
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
        // Tag-bit stripping is handled inside WalkFFieldChain for UE5.3+ safety
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

// --- WalkClassEx: enriched field type metadata ---

// Helper: read an FProperty* from a field address at a given offset, validate it returns
// a known property type name.  Returns the inner FProperty* address and its type name,
// or (0, "") if not found.
static std::pair<uintptr_t, std::string> ProbeInnerProperty(uintptr_t fieldAddr, int baseOffset) {
    static const int kProbeDeltas[] = { 0, 8, 4, 0xC, -4, -8, 0x10, -0x10 };
    for (int delta : kProbeDeltas) {
        int off = baseOffset + delta;
        if (off < 0) continue;
        uintptr_t inner = 0;
        if (!Mem::ReadSafe(fieldAddr + off, inner) || !inner) continue;
        std::string tn = GetFieldTypeName(inner);
        if (!tn.empty() && tn != "Unknown" && tn.find("Property") != std::string::npos)
            return { inner, tn };
    }
    return { 0, "" };
}

// Helper: given an FProperty* for StructProperty/ObjectProperty/ClassProperty,
// read the UScriptStruct*/UClass* at the subclass extension offset and return its name.
static std::string ReadSubclassTypeName(uintptr_t propAddr) {
    uintptr_t ptr = 0;
    if (!Mem::ReadSafe(propAddr + DynOff::FSTRUCTPROP_STRUCT, ptr) || !ptr) return "";
    std::string name = GetName(ptr);
    if (name.empty() || name[0] < 0x20 || name[0] >= 0x7F) return "";
    return name;
}

ClassInfo WalkClassEx(uintptr_t uclassAddr) {
    ClassInfo info = WalkClass(uclassAddr);

    // Enrich each field with extended type metadata
    for (auto& fi : info.Fields) {
        if (!fi.Address) continue;

        const auto& tn = fi.TypeName;

        // StructProperty -> UScriptStruct name
        if (tn == "StructProperty") {
            fi.structType = ReadSubclassTypeName(fi.Address);
        }

        // ObjectProperty / ClassProperty / WeakObjectProperty / SoftObjectProperty / SoftClassProperty
        // / InterfaceProperty -> target UClass name
        // FObjectPropertyBase::PropertyClass is at the same offset as FStructProperty::Struct
        else if (tn == "ObjectProperty" || tn == "ClassProperty"
              || tn == "WeakObjectProperty" || tn == "SoftObjectProperty"
              || tn == "SoftClassProperty" || tn == "InterfaceProperty"
              || tn == "LazyObjectProperty") {
            fi.objClassName = ReadSubclassTypeName(fi.Address);
        }

        // ArrayProperty -> inner type
        else if (tn == "ArrayProperty") {
            auto [innerProp, innerTn] = ProbeInnerProperty(fi.Address, DynOff::FARRAYPROP_INNER);
            if (innerProp) {
                fi.innerType = innerTn;
                if (innerTn == "StructProperty")
                    fi.innerStructType = ReadSubclassTypeName(innerProp);
                else if (innerTn == "ObjectProperty" || innerTn == "ClassProperty")
                    fi.innerObjClass = ReadSubclassTypeName(innerProp);
            }
        }

        // MapProperty -> key type + value type
        // FMapProperty layout: KeyProp at ext+0, ValueProp at ext+8 (same probe as WalkInstance)
        else if (tn == "MapProperty") {
            static const int kProbeDeltas[] = { 0, 8, 4, 0xC, -4, -8, 0x10, -0x10 };
            for (int delta : kProbeDeltas) {
                int tryOff = DynOff::FSTRUCTPROP_STRUCT + delta;
                if (tryOff < 0) continue;
                uintptr_t keyProp = 0;
                if (!Mem::ReadSafe(fi.Address + tryOff, keyProp) || !keyProp) continue;
                std::string keyTn = GetFieldTypeName(keyProp);
                if (keyTn.empty() || keyTn == "Unknown" || keyTn.find("Property") == std::string::npos)
                    continue;
                // Found KeyProp — ValueProp is at +8
                uintptr_t valueProp = 0;
                Mem::ReadSafe(fi.Address + tryOff + 8, valueProp);
                std::string valTn = valueProp ? GetFieldTypeName(valueProp) : "";
                if (valTn.empty() || valTn.find("Property") == std::string::npos) continue;

                fi.keyType = keyTn;
                fi.valueType = valTn;
                if (keyTn == "StructProperty")   fi.keyStructType = ReadSubclassTypeName(keyProp);
                if (valTn == "StructProperty")   fi.valueStructType = ReadSubclassTypeName(valueProp);
                break;
            }
        }

        // SetProperty -> element type
        else if (tn == "SetProperty") {
            auto [elemProp, elemTn] = ProbeInnerProperty(fi.Address, DynOff::FARRAYPROP_INNER);
            if (elemProp) {
                fi.elemType = elemTn;
                if (elemTn == "StructProperty")
                    fi.elemStructType = ReadSubclassTypeName(elemProp);
            }
        }

        // EnumProperty -> UEnum name
        else if (tn == "EnumProperty") {
            uintptr_t enumPtr = 0;
            if (Mem::ReadSafe(fi.Address + DynOff::FENUMPROP_ENUM, enumPtr) && enumPtr) {
                std::string ename = GetName(enumPtr);
                if (!ename.empty() && ename[0] >= 0x20 && ename[0] < 0x7F)
                    fi.enumName = ename;
            }
        }

        // ByteProperty -> check if it has an associated UEnum
        else if (tn == "ByteProperty") {
            uintptr_t enumPtr = 0;
            if (Mem::ReadSafe(fi.Address + DynOff::FBYTEPROP_ENUM, enumPtr) && enumPtr) {
                std::string ename = GetName(enumPtr);
                if (!ename.empty() && ename[0] >= 0x20 && ename[0] < 0x7F)
                    fi.enumName = ename;
            }
        }

        // BoolProperty -> FieldMask byte
        else if (tn == "BoolProperty") {
            uint8_t boolBytes[4] = {};
            for (int tryOff : { DynOff::FBOOLPROP_FIELDSIZE, DynOff::FBOOLPROP_FIELDSIZE - 4,
                                DynOff::FBOOLPROP_FIELDSIZE + 4, DynOff::FBOOLPROP_FIELDSIZE + 8 }) {
                if (tryOff < 0) continue;
                if (!Mem::ReadBytesSafe(fi.Address + tryOff, boolBytes, 4)) continue;
                uint8_t fieldSize = boolBytes[0];
                uint8_t fieldMask = boolBytes[3];
                if (fieldSize == 1 && fieldMask != 0 && (fieldMask & (fieldMask - 1)) == 0) {
                    fi.boolFieldMask = fieldMask;
                    break;
                }
            }
        }
    }

    return info;
}

// --- WalkFunctions: enumerate UFunctions of a UClass ---

std::vector<FunctionInfo> WalkFunctions(uintptr_t uclassAddr) {
    std::vector<FunctionInfo> funcs;
    if (!uclassAddr) return funcs;

    // CPF_ReturnParm = 0x0400, CPF_OutParm = 0x0100
    constexpr uint64_t CPF_ReturnParm = 0x0400;
    constexpr uint64_t CPF_OutParm    = 0x0100;

    // Walk the UField::Children chain (UStruct::Children at 0x48)
    // This chain contains UFunctions (and possibly other UField types)
    uintptr_t child = 0;
    if (!Mem::ReadSafe(uclassAddr + DynOff::USTRUCT_CHILDREN, child) || !child)
        return funcs;

    int safetyLimit = 4096;
    while (child != 0 && safetyLimit-- > 0) {
        // Check if this child is a UFunction by reading its class name
        uintptr_t childClass = 0;
        if (Mem::ReadSafe(child + Constants::OFF_UOBJECT_CLASS, childClass) && childClass) {
            std::string clsName = ReadFName(childClass + Constants::OFF_UOBJECT_NAME);

            if (clsName == "Function") {
                FunctionInfo fi{};
                fi.name = GetName(child);
                fi.fullName = GetFullName(child);
                fi.address = child;

                // Read FunctionFlags (UFunction::FunctionFlags)
                // Heuristic: try several common offsets for different UE versions
                uint32_t funcFlags = 0;
                for (int tryOff : { 0xB0, 0xC0, 0x88, 0xA8, 0xB8 }) {
                    if (Mem::ReadSafe<uint32_t>(child + tryOff, funcFlags) && funcFlags != 0)
                        break;
                }
                fi.functionFlags = funcFlags;

                // Walk the UFunction's own property chain (its parameters)
                // UFunction inherits UStruct, so ChildProperties is at USTRUCT_CHILDPROPS
                if (DynOff::bUseFProperty) {
                    uintptr_t paramChain = 0;
                    if (Mem::ReadSafe(child + DynOff::USTRUCT_CHILDPROPS, paramChain) && paramChain) {
                        uintptr_t cur = DynOff::StripFFieldTag(paramChain);
                        int paramLimit = 256;
                        while (cur != 0 && paramLimit-- > 0) {
                            if (DynOff::IsFFieldVariantUObject(cur)) break;

                            FunctionParam param{};
                            param.name = ReadFName(cur + DynOff::FFIELD_NAME);
                            param.typeName = GetFieldTypeName(cur);
                            Mem::ReadSafe<int32_t>(cur + DynOff::FPROPERTY_ELEMSIZE, param.size);

                            uint64_t propFlags = 0;
                            Mem::ReadSafe<uint64_t>(cur + DynOff::FPROPERTY_FLAGS, propFlags);

                            param.isReturn = (propFlags & CPF_ReturnParm) != 0;
                            param.isOut = (propFlags & CPF_OutParm) != 0;

                            if (param.isReturn)
                                fi.returnType = param.typeName;

                            if (!param.name.empty())
                                fi.params.push_back(param);

                            uintptr_t next = 0;
                            if (!Mem::ReadSafe(cur + DynOff::FFIELD_NEXT, next)) break;
                            cur = DynOff::StripFFieldTag(next);
                        }
                    }
                } else {
                    // UE4 <4.25: UProperty chain via Children
                    uintptr_t paramChain = 0;
                    if (Mem::ReadSafe(child + DynOff::USTRUCT_CHILDREN, paramChain) && paramChain) {
                        uintptr_t cur = paramChain;
                        int paramLimit = 256;
                        while (cur != 0 && paramLimit-- > 0) {
                            FunctionParam param{};
                            param.name = ReadFName(cur + Constants::OFF_UOBJECT_NAME);

                            uintptr_t paramCls = 0;
                            if (Mem::ReadSafe(cur + Constants::OFF_UOBJECT_CLASS, paramCls) && paramCls)
                                param.typeName = ReadFName(paramCls + Constants::OFF_UOBJECT_NAME);

                            Mem::ReadSafe<int32_t>(cur + DynOff::UPROPERTY_ELEMSIZE, param.size);

                            uint64_t propFlags = 0;
                            Mem::ReadSafe<uint64_t>(cur + DynOff::UPROPERTY_FLAGS, propFlags);

                            param.isReturn = (propFlags & CPF_ReturnParm) != 0;
                            param.isOut = (propFlags & CPF_OutParm) != 0;

                            if (param.isReturn) fi.returnType = param.typeName;
                            if (!param.name.empty()) fi.params.push_back(param);

                            uintptr_t next = 0;
                            if (!Mem::ReadSafe(cur + DynOff::UFIELD_NEXT, next)) break;
                            cur = next;
                        }
                    }
                }

                funcs.push_back(std::move(fi));
            }
        }

        // Move to next UField via UField::Next
        uintptr_t next = 0;
        if (!Mem::ReadSafe(child + DynOff::UFIELD_NEXT, next)) break;
        child = next;
    }

    LOG_INFO("WalkFunctions: %zu functions found at 0x%llX",
             funcs.size(), static_cast<unsigned long long>(uclassAddr));
    return funcs;
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
    if (end - offset > 4096) end = offset + 4096;  // hard cap per request

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
    result.enumAddr = enumPtr;  // Expose for CE DropDownList sharing

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
            elem.rawIntValue = rawVal;
            elem.enumName = ResolveEnumValue(enumPtr, rawVal);
            elem.value = elem.enumName.empty() ? std::to_string(rawVal) : elem.enumName;
        } else {
            elem.value = InterpretValue(innerTypeName, buf.data(), elemSize);
            // Store FName ComparisonIndex for NameProperty CE DropDownList
            if (innerTypeName == "NameProperty" && elemSize >= 4) {
                int32_t nameIdx = 0;
                memcpy(&nameIdx, buf.data(), 4);
                elem.rawIntValue = nameIdx;
            }
        }

        result.elements.push_back(std::move(elem));
    }

    result.ok = true;
    result.readCount = static_cast<int32_t>(result.elements.size());
    return result;
}

// ============================================================
// IsPointerArrayType — check if an inner type name is a pointer
// type whose elements are raw UObject* pointers (Phase D).
// ObjectProperty and ClassProperty store UObject* (8 bytes).
// WeakObjectProperty is deferred to Phase E.
// SoftObjectProperty, LazyObjectProperty, InterfaceProperty have
// different internal layouts — handled inline by WalkInstance.
// ============================================================
bool IsPointerArrayType(const std::string& innerTypeName) {
    return innerTypeName == "ObjectProperty"
        || innerTypeName == "ClassProperty";
}

// ============================================================
// ReadPointerArrayElements — read pointer elements from a TArray
// of UObject pointers (Phase D).
//
// For each element, reads the 8-byte pointer, then resolves the
// object name and class name via GetName/GetClass.
// ============================================================
ReadArrayResult ReadPointerArrayElements(
    uintptr_t instanceAddr, int32_t fieldOffset,
    int32_t elemSize, int32_t offset, int32_t limit)
{
    ReadArrayResult result;
    result.ok = false;

    // ObjectProperty elements are always 8 bytes (UObject pointer on x64)
    if (elemSize <= 0) elemSize = 8;

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
    if (end - offset > 4096) end = offset + 4096;  // hard cap per request

    result.elements.reserve(end - offset);

    for (int32_t i = offset; i < end; ++i) {
        LiveFieldValue::ArrayElement elem;
        elem.index = i;

        uintptr_t ptr = 0;
        uintptr_t elemAddr = arr.Data + static_cast<int64_t>(i) * elemSize;
        if (!Mem::ReadSafe(elemAddr, ptr)) {
            elem.value = "???";
            elem.hex = "????????????????";
            result.elements.push_back(std::move(elem));
            continue;
        }

        // Hex of the pointer value
        char hexBuf[20];
        snprintf(hexBuf, sizeof(hexBuf), "%016llX", static_cast<unsigned long long>(ptr));
        elem.hex = hexBuf;
        elem.ptrAddr = ptr;

        if (ptr) {
            elem.ptrName = GetName(ptr);
            uintptr_t cls = GetClass(ptr);
            if (cls) {
                elem.ptrClassName = GetName(cls);
            }

            // Display value: "Name (ClassName)" or just hex address if name fails
            if (!elem.ptrName.empty()) {
                elem.value = elem.ptrName;
                if (!elem.ptrClassName.empty())
                    elem.value += " (" + elem.ptrClassName + ")";
            } else {
                elem.value = hexBuf;  // Fallback to hex address
            }
        } else {
            elem.value = "null";
        }

        result.elements.push_back(std::move(elem));
    }

    result.ok = true;
    result.readCount = static_cast<int32_t>(result.elements.size());
    return result;
}

// ============================================================
// ResolveWeakObjectPtr — resolve FWeakObjectPtr to UObject* (Phase E).
//
// FWeakObjectPtr = { int32 ObjectIndex, int32 SerialNumber }.
// ObjectIndex is a GObjects index. The SerialNumber must match the
// FUObjectItem's serial to confirm the object is still alive.
// Returns the UObject* or 0 if stale/invalid.
// ============================================================
uintptr_t ResolveWeakObjectPtr(int32_t objectIndex, int32_t serialNumber) {
    if (objectIndex <= 0) return 0;
    uintptr_t obj = ObjectArray::GetByIndex(objectIndex);
    if (!obj) return 0;
    int32_t actualSerial = ObjectArray::GetSerialNumber(objectIndex);
    if (actualSerial != serialNumber) return 0;  // stale reference
    return obj;
}

// ============================================================
// IsWeakPointerArrayType — check if inner type is a weak-pointer type
// (Phase E). Currently only WeakObjectProperty.
// ============================================================
bool IsWeakPointerArrayType(const std::string& innerTypeName) {
    return innerTypeName == "WeakObjectProperty";
}

// ============================================================
// ReadWeakObjectArrayElements — read FWeakObjectPtr elements from a
// TArray (Phase E). Each element is { int32 ObjectIndex, int32 Serial }.
// Resolves each to a UObject* via GObjects + serial verification.
// ============================================================
ReadArrayResult ReadWeakObjectArrayElements(
    uintptr_t instanceAddr, int32_t fieldOffset,
    int32_t elemSize, int32_t offset, int32_t limit)
{
    ReadArrayResult result;
    result.ok = false;

    if (elemSize <= 0) elemSize = 8;  // FWeakObjectPtr is 8 bytes

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
    if (end - offset > 4096) end = offset + 4096;

    result.elements.reserve(end - offset);

    for (int32_t i = offset; i < end; ++i) {
        LiveFieldValue::ArrayElement elem;
        elem.index = i;

        uintptr_t elemAddr = arr.Data + static_cast<int64_t>(i) * elemSize;

        // Read FWeakObjectPtr { int32 ObjectIndex, int32 SerialNumber }
        int32_t objIdx = 0, serial = 0;
        if (!Mem::ReadSafe(elemAddr, objIdx) || !Mem::ReadSafe(elemAddr + 4, serial)) {
            elem.value = "???";
            elem.hex = "????????????????";
            result.elements.push_back(std::move(elem));
            continue;
        }

        // Hex: ObjectIndex + SerialNumber
        char hexBuf[20];
        snprintf(hexBuf, sizeof(hexBuf), "%08X%08X", objIdx, serial);
        elem.hex = hexBuf;

        // Resolve via GObjects
        uintptr_t ptr = ResolveWeakObjectPtr(objIdx, serial);
        elem.ptrAddr = ptr;

        if (ptr) {
            elem.ptrName = GetName(ptr);
            uintptr_t cls = GetClass(ptr);
            if (cls) {
                elem.ptrClassName = GetName(cls);
            }

            if (!elem.ptrName.empty()) {
                elem.value = elem.ptrName;
                if (!elem.ptrClassName.empty())
                    elem.value += " (" + elem.ptrClassName + ")";
            } else {
                elem.value = hexBuf;
            }
        } else if (objIdx > 0) {
            elem.value = "null (stale)";
        } else {
            elem.value = "null";
        }

        result.elements.push_back(std::move(elem));
    }

    result.ok = true;
    result.readCount = static_cast<int32_t>(result.elements.size());
    return result;
}

// ============================================================
// Phase F: IsStructArrayType
// ============================================================
bool IsStructArrayType(const std::string& innerTypeName) {
    return innerTypeName == "StructProperty";
}

// ============================================================
// Phase F: Cached struct field layout for struct array expansion
// ============================================================
struct CachedStructField {
    std::string name;
    std::string typeName;
    int32_t     offset = 0;
    int32_t     size = 0;
    uint8_t     boolFieldMask = 0;   // BoolProperty: FieldMask byte
    uintptr_t   enumAddr = 0;        // EnumProperty / ByteProperty-with-enum: UEnum*
    std::string nestedTypeName;      // StructProperty: struct type name
};

static std::unordered_map<uintptr_t, std::vector<CachedStructField>> s_structFieldCache;

static const std::vector<CachedStructField>& GetCachedStructFields(uintptr_t structAddr) {
    auto it = s_structFieldCache.find(structAddr);
    if (it != s_structFieldCache.end())
        return it->second;

    // Walk the struct to get field layout
    ClassInfo ci = WalkClass(structAddr);
    std::vector<CachedStructField> cached;
    cached.reserve(ci.Fields.size());

    for (const auto& fi : ci.Fields) {
        CachedStructField cf;
        cf.name     = fi.Name;
        cf.typeName = fi.TypeName;
        cf.offset   = fi.Offset;
        cf.size     = fi.Size;

        // BoolProperty: read FieldMask from FBoolProperty FField
        if (fi.TypeName == "BoolProperty" && fi.Address) {
            uint8_t boolBytes[4] = {};
            for (int tryOff : { DynOff::FBOOLPROP_FIELDSIZE, DynOff::FBOOLPROP_FIELDSIZE - 4,
                                DynOff::FBOOLPROP_FIELDSIZE + 4, DynOff::FBOOLPROP_FIELDSIZE + 8 }) {
                if (tryOff < 0) continue;
                if (!Mem::ReadBytesSafe(fi.Address + tryOff, boolBytes, 4)) continue;
                uint8_t fieldSize = boolBytes[0];
                uint8_t fieldMask = boolBytes[3];
                if (fieldSize >= 1 && fieldSize <= 8 && fieldMask != 0 && (fieldMask & (fieldMask - 1)) == 0) {
                    cf.boolFieldMask = fieldMask;
                    break;
                }
            }
        }

        // EnumProperty: read UEnum*
        if (fi.TypeName == "EnumProperty" && fi.Address) {
            Mem::ReadSafe(fi.Address + DynOff::FENUMPROP_ENUM, cf.enumAddr);
        }

        // ByteProperty: check for UEnum* (ByteProperty-with-enum)
        if (fi.TypeName == "ByteProperty" && fi.Address) {
            Mem::ReadSafe(fi.Address + DynOff::FBYTEPROP_ENUM, cf.enumAddr);
        }

        // StructProperty: read nested struct type name
        if (fi.TypeName == "StructProperty" && fi.Address) {
            uintptr_t nestedStruct = 0;
            if (Mem::ReadSafe(fi.Address + DynOff::FSTRUCTPROP_STRUCT, nestedStruct) && nestedStruct) {
                cf.nestedTypeName = GetName(nestedStruct);
            }
        }

        cached.push_back(std::move(cf));
    }

    Logger::Debug("WALK:ArrayF", "Cached struct fields for 0x%llX: %d fields",
        static_cast<unsigned long long>(structAddr), static_cast<int>(cached.size()));

    auto [ins, _] = s_structFieldCache.emplace(structAddr, std::move(cached));
    return ins->second;
}

// ============================================================
// Phase F: ReadStructArrayElements
// ============================================================
ReadArrayResult ReadStructArrayElements(
    uintptr_t instanceAddr, int32_t fieldOffset,
    uintptr_t innerStructAddr, int32_t elemSize,
    int32_t offset, int32_t limit)
{
    ReadArrayResult result;

    Mem::TArrayView arr;
    if (!Mem::ReadTArray(instanceAddr + fieldOffset, arr)) {
        result.error = "Failed to read TArray header";
        return result;
    }
    result.totalCount = arr.Count;
    if (arr.Count <= 0 || !arr.Data) {
        result.ok = true;
        return result;
    }

    int32_t end = (std::min)(offset + limit, arr.Count);
    if (offset >= arr.Count) { result.ok = true; return result; }

    // Get cached field layout
    const auto& cachedFields = GetCachedStructFields(innerStructAddr);
    if (cachedFields.empty()) {
        result.ok = true;  // Struct has no fields — return empty elements
        return result;
    }

    // Cap element buffer at 1024 bytes to avoid stack overflow
    const int32_t maxBufSize = 1024;
    int32_t readSize = (std::min)(elemSize, maxBufSize);

    for (int32_t i = offset; i < end; ++i) {
        LiveFieldValue::ArrayElement elem;
        elem.index = i;

        uintptr_t elemAddr = arr.Data + static_cast<uintptr_t>(i) * elemSize;

        // Bulk read element bytes
        std::vector<uint8_t> buf(readSize, 0);
        if (!Mem::ReadBytesSafe(elemAddr, buf.data(), readSize)) {
            elem.value = "???";
            result.elements.push_back(std::move(elem));
            continue;
        }

        // Hex of first 16 bytes (or less)
        int hexLen = (std::min)(readSize, 16);
        std::string hexStr;
        hexStr.reserve(hexLen * 2);
        for (int h = 0; h < hexLen; ++h) {
            char hx[3];
            snprintf(hx, sizeof(hx), "%02X", buf[h]);
            hexStr += hx;
        }
        elem.hex = hexStr;

        // Build compact value string and sub-fields
        std::string compact = "{";
        bool first = true;

        for (const auto& cf : cachedFields) {
            // Skip fields that extend beyond our read buffer
            if (cf.offset < 0 || cf.offset + cf.size > readSize) continue;

            LiveFieldValue::ArrayElement::StructSubField sf;
            sf.name     = cf.name;
            sf.typeName = cf.typeName;
            sf.offset   = cf.offset;
            sf.size     = cf.size;

            // Interpret based on type
            if (cf.typeName == "BoolProperty") {
                uint8_t byteVal = buf[cf.offset];
                bool boolVal = (cf.boolFieldMask != 0)
                    ? (byteVal & cf.boolFieldMask) != 0
                    : byteVal != 0;
                sf.value = boolVal ? "true" : "false";
            } else if ((cf.typeName == "EnumProperty" || cf.typeName == "ByteProperty") && cf.enumAddr) {
                int64_t rawVal = 0;
                if (cf.size == 1) rawVal = static_cast<int8_t>(buf[cf.offset]);
                else if (cf.size == 2) { int16_t v; memcpy(&v, buf.data() + cf.offset, 2); rawVal = v; }
                else if (cf.size == 4) { int32_t v; memcpy(&v, buf.data() + cf.offset, 4); rawVal = v; }
                else if (cf.size == 8) { int64_t v; memcpy(&v, buf.data() + cf.offset, 8); rawVal = v; }
                sf.value = ResolveEnumValue(cf.enumAddr, rawVal);
                if (sf.value.empty()) sf.value = std::to_string(rawVal);
            } else if (cf.typeName == "StructProperty") {
                sf.value = cf.nestedTypeName.empty() ? "{Struct}" : "{" + cf.nestedTypeName + "}";
            } else if (cf.typeName == "ObjectProperty" || cf.typeName == "ClassProperty"
                    || cf.typeName == "WeakObjectProperty"
                    || cf.typeName == "InterfaceProperty") {
                // Resolve pointer: read UObject* from element memory
                uintptr_t ptr = 0;
                if (cf.size == 8 && cf.offset + 8 <= readSize) {
                    memcpy(&ptr, buf.data() + cf.offset, 8);
                } else if (cf.size == 4 && cf.offset + 4 <= readSize) {
                    uint32_t p32 = 0;
                    memcpy(&p32, buf.data() + cf.offset, 4);
                    ptr = p32;
                }
                sf.ptrAddr = ptr;
                if (ptr) {
                    sf.ptrName = GetName(ptr);
                    uintptr_t cls = GetClass(ptr);
                    if (cls) {
                        sf.ptrClassName = GetName(cls);
                        sf.ptrClassAddr = cls;
                    }
                    sf.value = !sf.ptrName.empty() ? sf.ptrName : "ptr";
                } else {
                    sf.value = "null";
                }
            } else if (cf.typeName == "SoftObjectProperty" || cf.typeName == "SoftClassProperty") {
                sf.value = ReadSoftObjectPath(elemAddr + cf.offset + 0x10);
                if (sf.value.empty()) sf.value = "(none)";
            } else if (cf.typeName == "LazyObjectProperty") {
                uintptr_t gAddr = elemAddr + cf.offset + 0x10;
                uint32_t ga = 0, gb = 0, gc = 0, gd = 0;
                Mem::ReadSafe(gAddr, ga); Mem::ReadSafe(gAddr + 4, gb);
                Mem::ReadSafe(gAddr + 8, gc); Mem::ReadSafe(gAddr + 12, gd);
                char gs[48]; snprintf(gs, sizeof(gs), "{%08X-%08X-%08X-%08X}", ga, gb, gc, gd);
                sf.value = gs;
            } else if (cf.typeName == "TextProperty") {
                sf.value = ReadFTextString(elemAddr + cf.offset);
                if (sf.value.empty()) sf.value = "(empty)";
            } else if (cf.typeName == "StrProperty") {
                sf.value = "(str)";
            } else if (cf.typeName == "ArrayProperty") {
                sf.value = "(Array)";
            } else if (cf.typeName == "MapProperty") {
                sf.value = "(Map)";
            } else if (cf.typeName == "SetProperty") {
                sf.value = "(Set)";
            } else {
                // Scalar: use InterpretValue
                sf.value = InterpretValue(cf.typeName, buf.data() + cf.offset, cf.size);
                if (sf.value.empty()) {
                    // Fallback: hex of the field bytes
                    std::string fhex;
                    int flen = (std::min)(cf.size, 8);
                    for (int h = 0; h < flen; ++h) {
                        char hx[3];
                        snprintf(hx, sizeof(hx), "%02X", buf[cf.offset + h]);
                        fhex += hx;
                    }
                    sf.value = fhex;
                }
            }

            // Append to compact string
            if (!first) compact += ", ";
            first = false;
            compact += cf.name + "=" + sf.value;

            elem.structFields.push_back(std::move(sf));
        }
        compact += "}";
        elem.value = compact;

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

InstanceWalkResult WalkInstance(uintptr_t instanceAddr, uintptr_t classAddr, int32_t arrayLimit) {
    // Clamp arrayLimit to sane range [1, 16384]
    if (arrayLimit < 1) arrayLimit = 1;
    if (arrayLimit > 16384) arrayLimit = 16384;
    InstanceWalkResult result;
    result.addr = instanceAddr;

    if (!instanceAddr) return result;

    if (!classAddr)
        classAddr = GetClass(instanceAddr);

    result.classAddr = classAddr;
    result.className = classAddr ? GetName(classAddr) : "";

    // Detect whether instanceAddr is a real UObject or raw struct data
    // (e.g., struct element inside Map/Array/Set container).
    // A real UObject has a ClassPrivate at OFF_UOBJECT_CLASS that points to
    // a valid UClass with a resolvable FName. Raw struct data has arbitrary
    // bytes at that offset — reading GetName/GetOuter produces garbage.
    bool isRawStruct = false;
    if (classAddr) {
        uintptr_t testClass = 0;
        Mem::ReadSafe(instanceAddr + Constants::OFF_UOBJECT_CLASS, testClass);
        if (!testClass || testClass < 0x10000 || testClass > 0x00007FFFFFFFFFFF) {
            isRawStruct = true;
        } else {
            std::string testName = GetName(testClass);
            isRawStruct = testName.empty();
        }
    }

    if (isRawStruct) {
        // Raw struct data — use class name as display name, skip outer
        result.name = result.className;
    } else {
        result.name = GetName(instanceAddr);

        // Read OuterPrivate (only valid for real UObjects)
        uintptr_t outerAddr = GetOuter(instanceAddr);
        result.outerAddr = outerAddr;
        if (outerAddr) {
            result.outerName      = GetName(outerAddr);
            uintptr_t outerClass  = GetClass(outerAddr);
            result.outerClassName = outerClass ? GetName(outerClass) : "";
        }
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

        // Handle WeakObjectProperty: FWeakObjectPtr { int32 ObjectIndex, int32 SerialNumber }
        if (fi.TypeName == "WeakObjectProperty") {
            int32_t objIdx = 0, serial = 0;
            Mem::ReadSafe(instanceAddr + fi.Offset, objIdx);
            Mem::ReadSafe(instanceAddr + fi.Offset + 4, serial);
            uintptr_t ptr = ResolveWeakObjectPtr(objIdx, serial);
            if (ptr) {
                fv.ptrValue = ptr;
                fv.ptrName = GetName(ptr);
                uintptr_t cls = GetClass(ptr);
                if (cls) {
                    fv.ptrClassName = GetName(cls);
                    fv.ptrClassAddr = cls;
                }
            }
            char buf[20];
            snprintf(buf, sizeof(buf), "%08X%08X", objIdx, serial);
            fv.hexValue = buf;
            result.fields.push_back(std::move(fv));
            continue;
        }

        // Handle ObjectProperty / ClassProperty: read pointer, resolve name/class
        if (fi.TypeName == "ObjectProperty" || fi.TypeName == "ClassProperty") {
            uintptr_t ptr = 0;
            if (fi.Size >= 8 && Mem::ReadSafe(instanceAddr + fi.Offset, ptr) && ptr) {
                fv.ptrValue = ptr;
                fv.ptrName = GetName(ptr);
                fv.ptrClassName = "";
                uintptr_t ptrCls = GetClass(ptr);
                if (ptrCls) {
                    fv.ptrClassName = GetName(ptrCls);
                    fv.ptrClassAddr = ptrCls;
                }
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

        // Handle SoftObjectProperty / SoftClassProperty: read FSoftObjectPath asset path
        // TSoftObjectPtr layout: +0x00 FWeakObjectPtr(8B), +0x08 Tag(4B), +0x0C pad(4B), +0x10 FSoftObjectPath
        if (fi.TypeName == "SoftObjectProperty" || fi.TypeName == "SoftClassProperty") {
            uintptr_t fieldAddr = instanceAddr + fi.Offset;
            std::string assetPath = ReadSoftObjectPath(fieldAddr + 0x10);
            fv.strValue = assetPath;
            fv.typedValue = assetPath.empty() ? "(none)" : assetPath;

            // Hex from raw bytes (cap at 32 bytes)
            int showBytes = (fi.Size > 0 && fi.Size <= 64) ? (std::min)(fi.Size, (int32_t)32) : 0;
            if (showBytes > 0) {
                std::vector<uint8_t> rawBuf(showBytes, 0);
                if (Mem::ReadBytesSafe(fieldAddr, rawBuf.data(), showBytes)) {
                    std::string hex;
                    hex.reserve(showBytes * 2);
                    for (int i = 0; i < showBytes; ++i) {
                        char hx[3];
                        snprintf(hx, sizeof(hx), "%02X", rawBuf[i]);
                        hex += hx;
                    }
                    if (fi.Size > 32) hex += "...";
                    fv.hexValue = hex;
                }
            }
            result.fields.push_back(std::move(fv));
            continue;
        }

        // Handle LazyObjectProperty: read FUniqueObjectGuid (FGuid = 4 x uint32)
        // TLazyObjectPtr layout: +0x00 FWeakObjectPtr(8B), +0x08 Tag(4B), +0x0C pad(4B), +0x10 FGuid(16B)
        if (fi.TypeName == "LazyObjectProperty") {
            uintptr_t guidAddr = instanceAddr + fi.Offset + 0x10;
            uint32_t a = 0, b = 0, c = 0, d = 0;
            Mem::ReadSafe(guidAddr + 0, a);
            Mem::ReadSafe(guidAddr + 4, b);
            Mem::ReadSafe(guidAddr + 8, c);
            Mem::ReadSafe(guidAddr + 12, d);

            char guidStr[48];
            snprintf(guidStr, sizeof(guidStr), "{%08X-%08X-%08X-%08X}", a, b, c, d);
            fv.typedValue = guidStr;
            fv.strValue = guidStr;

            // Hex from raw bytes
            int showBytes = (fi.Size > 0 && fi.Size <= 64) ? (std::min)(fi.Size, (int32_t)32) : 0;
            if (showBytes > 0) {
                std::vector<uint8_t> rawBuf(showBytes, 0);
                if (Mem::ReadBytesSafe(instanceAddr + fi.Offset, rawBuf.data(), showBytes)) {
                    std::string hex;
                    hex.reserve(showBytes * 2);
                    for (int i = 0; i < showBytes; ++i) {
                        char hx[3];
                        snprintf(hx, sizeof(hx), "%02X", rawBuf[i]);
                        hex += hx;
                    }
                    if (fi.Size > 32) hex += "...";
                    fv.hexValue = hex;
                }
            }
            result.fields.push_back(std::move(fv));
            continue;
        }

        // Handle InterfaceProperty: FScriptInterface = { UObject* ObjectPointer(8B); void* InterfacePointer(8B) }
        if (fi.TypeName == "InterfaceProperty") {
            uintptr_t objPtr = 0;
            uintptr_t ifacePtr = 0;
            Mem::ReadSafe(instanceAddr + fi.Offset, objPtr);
            Mem::ReadSafe(instanceAddr + fi.Offset + 8, ifacePtr);

            if (objPtr) {
                fv.ptrValue = objPtr;
                fv.ptrName = GetName(objPtr);
                uintptr_t cls = GetClass(objPtr);
                if (cls) {
                    fv.ptrClassName = GetName(cls);
                    fv.ptrClassAddr = cls;
                }
            }

            char buf[48];
            snprintf(buf, sizeof(buf), "%016llX %016llX",
                static_cast<unsigned long long>(objPtr),
                static_cast<unsigned long long>(ifacePtr));
            fv.hexValue = buf;
            result.fields.push_back(std::move(fv));
            continue;
        }

        // Handle ArrayProperty: read TArray header + Inner element type
        if (fi.TypeName == "ArrayProperty") {
            Mem::TArrayView arr;
            if (Mem::ReadTArray(instanceAddr + fi.Offset, arr)) {
                fv.arrayCount = arr.Count;
                fv.arrayDataAddr = arr.Data;
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
                                fv.arrayInnerStructAddr = innerStruct;  // Phase F: store for struct array expansion
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

                // Phase B: read inline scalar element values (up to arrayLimit)
                if (innerFound && IsScalarArrayType(fv.arrayInnerType)
                    && arr.Data && fv.arrayCount > 0
                    && fv.arrayElemSize > 0) {
                    auto elemResult = ReadArrayElements(
                        instanceAddr, fi.Offset,
                        fv.arrayInnerFFieldAddr, fv.arrayInnerType,
                        fv.arrayElemSize, 0, arrayLimit);
                    if (elemResult.ok && !elemResult.elements.empty()) {
                        fv.arrayElements = std::move(elemResult.elements);
                        Logger::Debug("WALK:ArrayP", "Inline elements: %d read for '%s'",
                            static_cast<int>(fv.arrayElements.size()), fi.Name.c_str());
                    }
                    // Populate full enum entries for CE DropDownList
                    if (elemResult.enumAddr) {
                        fv.arrayEnumAddr = elemResult.enumAddr;
                        fv.arrayEnumEntries = GetEnumEntries(elemResult.enumAddr);
                    }
                }

                // Phase D: read pointer array element names (up to arrayLimit)
                if (innerFound && IsPointerArrayType(fv.arrayInnerType)
                    && arr.Data && fv.arrayCount > 0
                    && fv.arrayElemSize > 0) {
                    auto ptrResult = ReadPointerArrayElements(
                        instanceAddr, fi.Offset, fv.arrayElemSize, 0, arrayLimit);
                    if (ptrResult.ok && !ptrResult.elements.empty()) {
                        fv.arrayElements = std::move(ptrResult.elements);
                        Logger::Debug("WALK:ArrayP", "Ptr elements: %d read for '%s'",
                            static_cast<int>(fv.arrayElements.size()), fi.Name.c_str());
                    }
                }

                // Phase E: read weak object pointer array element names (up to arrayLimit)
                if (innerFound && IsWeakPointerArrayType(fv.arrayInnerType)
                    && arr.Data && fv.arrayCount > 0
                    && fv.arrayElemSize > 0) {
                    auto weakResult = ReadWeakObjectArrayElements(
                        instanceAddr, fi.Offset, fv.arrayElemSize, 0, arrayLimit);
                    if (weakResult.ok && !weakResult.elements.empty()) {
                        fv.arrayElements = std::move(weakResult.elements);
                        Logger::Debug("WALK:ArrayP", "Weak ptr elements: %d read for '%s'",
                            static_cast<int>(fv.arrayElements.size()), fi.Name.c_str());
                    }
                }

                // Phase F: read struct array element fields (up to arrayLimit)
                if (innerFound && IsStructArrayType(fv.arrayInnerType)
                    && fv.arrayInnerStructAddr != 0
                    && arr.Data && fv.arrayCount > 0
                    && fv.arrayElemSize > 0) {
                    auto structResult = ReadStructArrayElements(
                        instanceAddr, fi.Offset,
                        fv.arrayInnerStructAddr, fv.arrayElemSize, 0, arrayLimit);
                    if (structResult.ok && !structResult.elements.empty()) {
                        fv.arrayElements = std::move(structResult.elements);
                        Logger::Debug("WALK:ArrayP", "Struct elements: %d read for '%s'",
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
            } else {
                // UProperty mode (UE4 <4.25): UArrayProperty::Inner is a UProperty* (UObject subclass).
                // Located at end of UProperty base class = UPROPERTY_OFFSET + 0x2C (standard delta).
                int baseOff = DynOff::UPROPERTY_OFFSET + 0x2C;
                static const int kUPropProbeOffsets[] = { 0, 8, -8, 0x10, -0x10, 4, -4 };
                bool innerFound = false;
                for (int delta : kUPropProbeOffsets) {
                    int tryOff = baseOff + delta;
                    if (tryOff < 0) continue;
                    uintptr_t inner = 0;
                    if (!Mem::ReadSafe(fi.Address + tryOff, inner) || !inner) continue;
                    if (inner < 0x10000 || inner > 0x00007FFFFFFFFFFF) continue;

                    std::string innerTypeName = GetUPropertyTypeName(inner);
                    if (!innerTypeName.empty() && innerTypeName.find("Property") != std::string::npos) {
                        fv.arrayInnerType = innerTypeName;
                        Mem::ReadSafe<int32_t>(inner + DynOff::UPROPERTY_ELEMSIZE, fv.arrayElemSize);

                        if (innerTypeName == "StructProperty") {
                            // UStructProperty::Struct at same base offset
                            uintptr_t innerStruct = 0;
                            if (Mem::ReadSafe(inner + baseOff, innerStruct) && innerStruct) {
                                fv.arrayInnerStructType = GetName(innerStruct);
                                fv.arrayInnerStructAddr = innerStruct;
                            }
                        }

                        Logger::Info("WALK:ArrayP", "UArrayProperty::Inner at UProperty+0x%X (delta=%d) for '%s' -> '%s' elemSize=%d",
                            tryOff, delta, fi.Name.c_str(), innerTypeName.c_str(), fv.arrayElemSize);
                        fv.arrayInnerFFieldAddr = inner;  // reuse field for UProperty* too
                        innerFound = true;
                        break;
                    }
                }

                // Phase B-F: same inline element reading as FProperty mode
                if (innerFound && IsScalarArrayType(fv.arrayInnerType)
                    && arr.Data && fv.arrayCount > 0 && fv.arrayElemSize > 0) {
                    auto elemResult = ReadArrayElements(
                        instanceAddr, fi.Offset,
                        fv.arrayInnerFFieldAddr, fv.arrayInnerType,
                        fv.arrayElemSize, 0, arrayLimit);
                    if (elemResult.ok && !elemResult.elements.empty()) {
                        fv.arrayElements = std::move(elemResult.elements);
                    }
                }
                if (innerFound && IsPointerArrayType(fv.arrayInnerType)
                    && arr.Data && fv.arrayCount > 0 && fv.arrayElemSize > 0) {
                    auto ptrResult = ReadPointerArrayElements(
                        instanceAddr, fi.Offset, fv.arrayElemSize, 0, arrayLimit);
                    if (ptrResult.ok && !ptrResult.elements.empty()) {
                        fv.arrayElements = std::move(ptrResult.elements);
                    }
                }
                if (innerFound && IsWeakPointerArrayType(fv.arrayInnerType)
                    && arr.Data && fv.arrayCount > 0 && fv.arrayElemSize > 0) {
                    auto weakResult = ReadWeakObjectArrayElements(
                        instanceAddr, fi.Offset, fv.arrayElemSize, 0, arrayLimit);
                    if (weakResult.ok && !weakResult.elements.empty()) {
                        fv.arrayElements = std::move(weakResult.elements);
                    }
                }
                if (innerFound && IsStructArrayType(fv.arrayInnerType)
                    && fv.arrayInnerStructAddr != 0
                    && arr.Data && fv.arrayCount > 0 && fv.arrayElemSize > 0) {
                    auto structResult = ReadStructArrayElements(
                        instanceAddr, fi.Offset,
                        fv.arrayInnerStructAddr, fv.arrayElemSize, 0, arrayLimit);
                    if (structResult.ok && !structResult.elements.empty()) {
                        fv.arrayElements = std::move(structResult.elements);
                    }
                }

                if (!innerFound) {
                    Logger::Info("WALK:ArrayP", "UArrayProperty::Inner NOT found for '%s' (UProperty=0x%llX, baseOff=0x%X)",
                        fi.Name.c_str(), static_cast<unsigned long long>(fi.Address), baseOff);
                }
            }

            result.fields.push_back(std::move(fv));
            continue;
        }

        // Handle MapProperty: read TMap (wraps TSet<TPair<Key,Value>>)
        if (fi.TypeName == "MapProperty") {
            Mem::TSparseArrayView sa;
            if (Mem::ReadTSparseArray(instanceAddr + fi.Offset, sa)) {
                fv.mapCount = sa.MaxIndex - sa.NumFreeIndices;
                if (fv.mapCount < 0) fv.mapCount = 0;
                fv.mapDataAddr = sa.Data;
                // Hex header: Data + MaxIndex + NumFree
                char buf[48];
                snprintf(buf, sizeof(buf), "%016llX %08X %08X",
                    static_cast<unsigned long long>(sa.Data), sa.MaxIndex, sa.NumFreeIndices);
                fv.hexValue = buf;
            } else {
                fv.mapCount = 0;
            }

            // Probe for KeyProp and ValueProp FProperty*
            if (DynOff::bUseFProperty) {
                static const int kProbeOffsets[] = { 0, 8, 4, 0xC, -4, -8, 0x10, -0x10 };
                for (int delta : kProbeOffsets) {
                    int tryOff = DynOff::FSTRUCTPROP_STRUCT + delta;
                    if (tryOff < 0) continue;
                    uintptr_t keyProp = 0;
                    if (!Mem::ReadSafe(fi.Address + tryOff, keyProp) || !keyProp) continue;

                    std::string keyTypeName = GetFieldTypeName(keyProp);
                    if (keyTypeName.empty() || keyTypeName == "Unknown"
                        || keyTypeName.find("Property") == std::string::npos) continue;

                    // Found KeyProp — ValueProp is at +8 from KeyProp
                    uintptr_t valueProp = 0;
                    Mem::ReadSafe(fi.Address + tryOff + 8, valueProp);
                    std::string valueTypeName = valueProp ? GetFieldTypeName(valueProp) : "";

                    if (!valueTypeName.empty() && valueTypeName.find("Property") != std::string::npos) {
                        fv.mapKeyType = keyTypeName;
                        fv.mapValueType = valueTypeName;
                        Mem::ReadSafe<int32_t>(keyProp + DynOff::FPROPERTY_ELEMSIZE, fv.mapKeySize);
                        Mem::ReadSafe<int32_t>(valueProp + DynOff::FPROPERTY_ELEMSIZE, fv.mapValueSize);
                        Logger::Info("WALK:MapP", "FMapProperty KeyProp='%s'(%d) ValueProp='%s'(%d) at delta=%d for '%s'",
                            keyTypeName.c_str(), fv.mapKeySize, valueTypeName.c_str(), fv.mapValueSize,
                            delta, fi.Name.c_str());

                        // If key/value is StructProperty, read UScriptStruct* for navigation
                        if (keyTypeName == "StructProperty") {
                            uintptr_t kStruct = 0;
                            if (Mem::ReadSafe(keyProp + DynOff::FSTRUCTPROP_STRUCT, kStruct) && kStruct) {
                                fv.mapKeyStructAddr = kStruct;
                                fv.mapKeyStructType = GetName(kStruct);
                            }
                        }
                        if (valueTypeName == "StructProperty") {
                            uintptr_t vStruct = 0;
                            if (Mem::ReadSafe(valueProp + DynOff::FSTRUCTPROP_STRUCT, vStruct) && vStruct) {
                                fv.mapValueStructAddr = vStruct;
                                fv.mapValueStructType = GetName(vStruct);
                            }
                        }

                        // Read inline element values if count is manageable
                        if (fv.mapCount > 0
                            && sa.Data && fv.mapKeySize > 0 && fv.mapValueSize > 0) {
                            int32_t pairSize = fv.mapKeySize + fv.mapValueSize;
                            int32_t stride = Mem::ComputeSetElementStride(pairSize);
                            int read = 0;
                            for (int32_t idx = 0; idx < sa.MaxIndex && read < fv.mapCount && read < arrayLimit; ++idx) {
                                if (!Mem::IsSparseIndexAllocated(sa, idx)) continue;
                                uintptr_t elemAddr = sa.Data + static_cast<uintptr_t>(idx) * stride;
                                LiveFieldValue::ContainerElement ce;
                                ce.index = idx;
                                // Read key bytes
                                std::vector<uint8_t> keyBuf(fv.mapKeySize);
                                if (Mem::ReadBytesSafe(elemAddr, keyBuf.data(), fv.mapKeySize)) {
                                    ce.key = InterpretValue(keyTypeName, keyBuf.data(), fv.mapKeySize);
                                    // Hex
                                    std::string kh;
                                    int klen = (std::min)(fv.mapKeySize, 16);
                                    for (int h = 0; h < klen; ++h) {
                                        char hx[3]; snprintf(hx, sizeof(hx), "%02X", keyBuf[h]);
                                        kh += hx;
                                    }
                                    ce.keyHex = kh;
                                    // Pointer key: resolve name, addr, class
                                    if (keyTypeName == "ObjectProperty" || keyTypeName == "ClassProperty") {
                                        uintptr_t ptr = 0;
                                        memcpy(&ptr, keyBuf.data(), (std::min)(fv.mapKeySize, (int32_t)sizeof(ptr)));
                                        if (ptr) {
                                            ce.keyPtrAddr = ptr;
                                            ce.keyPtrName = GetName(ptr);
                                            uintptr_t cls = GetClass(ptr);
                                            if (cls) ce.keyPtrClassName = GetName(cls);
                                        }
                                    }
                                    // FName key: resolve name
                                    if (keyTypeName == "NameProperty" && ce.key.empty()) {
                                        ce.key = ce.keyHex;  // fallback
                                    }
                                }
                                // Read value bytes
                                std::vector<uint8_t> valBuf(fv.mapValueSize);
                                if (Mem::ReadBytesSafe(elemAddr + fv.mapKeySize, valBuf.data(), fv.mapValueSize)) {
                                    ce.value = InterpretValue(valueTypeName, valBuf.data(), fv.mapValueSize);
                                    std::string vh;
                                    int vlen = (std::min)(fv.mapValueSize, 16);
                                    for (int h = 0; h < vlen; ++h) {
                                        char hx[3]; snprintf(hx, sizeof(hx), "%02X", valBuf[h]);
                                        vh += hx;
                                    }
                                    ce.valueHex = vh;
                                    if (valueTypeName == "ObjectProperty" || valueTypeName == "ClassProperty") {
                                        uintptr_t ptr = 0;
                                        memcpy(&ptr, valBuf.data(), (std::min)(fv.mapValueSize, (int32_t)sizeof(ptr)));
                                        if (ptr) {
                                            ce.valuePtrAddr = ptr;
                                            ce.valuePtrName = GetName(ptr);
                                            uintptr_t cls = GetClass(ptr);
                                            if (cls) ce.valuePtrClassName = GetName(cls);
                                        }
                                    }
                                }
                                fv.containerElements.push_back(std::move(ce));
                                ++read;
                            }
                            Logger::Debug("WALK:MapP", "Read %d/%d map entries for '%s'", read, fv.mapCount, fi.Name.c_str());
                        }
                        break;
                    }
                }
            } else {
                // UProperty mode (UE4 <4.25): UMapProperty has KeyProp + ValueProp as UProperty*.
                int baseOff = DynOff::UPROPERTY_OFFSET + 0x2C;
                static const int kUPropProbeOffsets[] = { 0, 8, -8, 0x10, -0x10, 4, -4 };
                for (int delta : kUPropProbeOffsets) {
                    int tryOff = baseOff + delta;
                    if (tryOff < 0) continue;
                    uintptr_t keyProp = 0;
                    if (!Mem::ReadSafe(fi.Address + tryOff, keyProp) || !keyProp) continue;
                    if (keyProp < 0x10000 || keyProp > 0x00007FFFFFFFFFFF) continue;

                    std::string keyTypeName = GetUPropertyTypeName(keyProp);
                    if (keyTypeName.empty() || keyTypeName.find("Property") == std::string::npos) continue;

                    uintptr_t valueProp = 0;
                    Mem::ReadSafe(fi.Address + tryOff + 8, valueProp);
                    std::string valueTypeName = (valueProp && valueProp > 0x10000
                        && valueProp < 0x00007FFFFFFFFFFF) ? GetUPropertyTypeName(valueProp) : "";

                    if (!valueTypeName.empty() && valueTypeName.find("Property") != std::string::npos) {
                        fv.mapKeyType = keyTypeName;
                        fv.mapValueType = valueTypeName;
                        Mem::ReadSafe<int32_t>(keyProp + DynOff::UPROPERTY_ELEMSIZE, fv.mapKeySize);
                        Mem::ReadSafe<int32_t>(valueProp + DynOff::UPROPERTY_ELEMSIZE, fv.mapValueSize);
                        Logger::Info("WALK:MapP", "UMapProperty KeyProp='%s'(%d) ValueProp='%s'(%d) at delta=%d for '%s'",
                            keyTypeName.c_str(), fv.mapKeySize, valueTypeName.c_str(), fv.mapValueSize,
                            delta, fi.Name.c_str());

                        if (keyTypeName == "StructProperty") {
                            uintptr_t kStruct = 0;
                            if (Mem::ReadSafe(keyProp + baseOff, kStruct) && kStruct) {
                                fv.mapKeyStructAddr = kStruct;
                                fv.mapKeyStructType = GetName(kStruct);
                            }
                        }
                        if (valueTypeName == "StructProperty") {
                            uintptr_t vStruct = 0;
                            if (Mem::ReadSafe(valueProp + baseOff, vStruct) && vStruct) {
                                fv.mapValueStructAddr = vStruct;
                                fv.mapValueStructType = GetName(vStruct);
                            }
                        }

                        // Read inline element values
                        if (fv.mapCount > 0 && sa.Data && fv.mapKeySize > 0 && fv.mapValueSize > 0) {
                            int32_t pairSize = fv.mapKeySize + fv.mapValueSize;
                            int32_t stride = Mem::ComputeSetElementStride(pairSize);
                            int read = 0;
                            for (int32_t idx = 0; idx < sa.MaxIndex && read < fv.mapCount && read < arrayLimit; ++idx) {
                                if (!Mem::IsSparseIndexAllocated(sa, idx)) continue;
                                uintptr_t elemAddr = sa.Data + static_cast<uintptr_t>(idx) * stride;
                                LiveFieldValue::ContainerElement ce;
                                ce.index = idx;
                                std::vector<uint8_t> keyBuf(fv.mapKeySize);
                                if (Mem::ReadBytesSafe(elemAddr, keyBuf.data(), fv.mapKeySize)) {
                                    ce.key = InterpretValue(keyTypeName, keyBuf.data(), fv.mapKeySize);
                                    std::string kh;
                                    int klen = (std::min)(fv.mapKeySize, 16);
                                    for (int h = 0; h < klen; ++h) {
                                        char hx[3]; snprintf(hx, sizeof(hx), "%02X", keyBuf[h]);
                                        kh += hx;
                                    }
                                    ce.keyHex = kh;
                                    if (keyTypeName == "ObjectProperty" || keyTypeName == "ClassProperty") {
                                        uintptr_t ptr = 0;
                                        memcpy(&ptr, keyBuf.data(), (std::min)(fv.mapKeySize, (int32_t)sizeof(ptr)));
                                        if (ptr) {
                                            ce.keyPtrAddr = ptr;
                                            ce.keyPtrName = GetName(ptr);
                                            uintptr_t cls = GetClass(ptr);
                                            if (cls) ce.keyPtrClassName = GetName(cls);
                                        }
                                    }
                                    if (keyTypeName == "NameProperty" && ce.key.empty()) {
                                        ce.key = ce.keyHex;
                                    }
                                }
                                std::vector<uint8_t> valBuf(fv.mapValueSize);
                                if (Mem::ReadBytesSafe(elemAddr + fv.mapKeySize, valBuf.data(), fv.mapValueSize)) {
                                    ce.value = InterpretValue(valueTypeName, valBuf.data(), fv.mapValueSize);
                                    std::string vh;
                                    int vlen = (std::min)(fv.mapValueSize, 16);
                                    for (int h = 0; h < vlen; ++h) {
                                        char hx[3]; snprintf(hx, sizeof(hx), "%02X", valBuf[h]);
                                        vh += hx;
                                    }
                                    ce.valueHex = vh;
                                    if (valueTypeName == "ObjectProperty" || valueTypeName == "ClassProperty") {
                                        uintptr_t ptr = 0;
                                        memcpy(&ptr, valBuf.data(), (std::min)(fv.mapValueSize, (int32_t)sizeof(ptr)));
                                        if (ptr) {
                                            ce.valuePtrAddr = ptr;
                                            ce.valuePtrName = GetName(ptr);
                                            uintptr_t cls = GetClass(ptr);
                                            if (cls) ce.valuePtrClassName = GetName(cls);
                                        }
                                    }
                                }
                                fv.containerElements.push_back(std::move(ce));
                                ++read;
                            }
                            Logger::Debug("WALK:MapP", "Read %d/%d map entries for '%s'", read, fv.mapCount, fi.Name.c_str());
                        }
                        break;
                    }
                }
            }

            result.fields.push_back(std::move(fv));
            continue;
        }

        // Handle SetProperty: read TSet (TSparseArray of elements)
        if (fi.TypeName == "SetProperty") {
            Mem::TSparseArrayView sa;
            if (Mem::ReadTSparseArray(instanceAddr + fi.Offset, sa)) {
                fv.setCount = sa.MaxIndex - sa.NumFreeIndices;
                if (fv.setCount < 0) fv.setCount = 0;
                fv.setDataAddr = sa.Data;
                char buf[48];
                snprintf(buf, sizeof(buf), "%016llX %08X %08X",
                    static_cast<unsigned long long>(sa.Data), sa.MaxIndex, sa.NumFreeIndices);
                fv.hexValue = buf;
            } else {
                fv.setCount = 0;
            }

            // Probe for ElementProp FProperty*
            if (DynOff::bUseFProperty) {
                static const int kProbeOffsets[] = { 0, 8, 4, 0xC, -4, -8, 0x10, -0x10 };
                for (int delta : kProbeOffsets) {
                    int tryOff = DynOff::FSTRUCTPROP_STRUCT + delta;
                    if (tryOff < 0) continue;
                    uintptr_t elemProp = 0;
                    if (!Mem::ReadSafe(fi.Address + tryOff, elemProp) || !elemProp) continue;

                    std::string elemTypeName = GetFieldTypeName(elemProp);
                    if (elemTypeName.empty() || elemTypeName == "Unknown"
                        || elemTypeName.find("Property") == std::string::npos) continue;

                    fv.setElemType = elemTypeName;
                    Mem::ReadSafe<int32_t>(elemProp + DynOff::FPROPERTY_ELEMSIZE, fv.setElemSize);
                    Logger::Info("WALK:SetP", "FSetProperty ElementProp='%s'(%d) at delta=%d for '%s'",
                        elemTypeName.c_str(), fv.setElemSize, delta, fi.Name.c_str());

                    // If element is StructProperty, read UScriptStruct* for navigation
                    if (elemTypeName == "StructProperty") {
                        uintptr_t eStruct = 0;
                        if (Mem::ReadSafe(elemProp + DynOff::FSTRUCTPROP_STRUCT, eStruct) && eStruct) {
                            fv.setElemStructAddr = eStruct;
                            fv.setElemStructType = GetName(eStruct);
                        }
                    }

                    // Read inline element values if count is manageable
                    if (fv.setCount > 0
                        && sa.Data && fv.setElemSize > 0) {
                        int32_t stride = Mem::ComputeSetElementStride(fv.setElemSize);
                        int read = 0;
                        for (int32_t idx = 0; idx < sa.MaxIndex && read < fv.setCount && read < arrayLimit; ++idx) {
                            if (!Mem::IsSparseIndexAllocated(sa, idx)) continue;
                            uintptr_t elemAddr = sa.Data + static_cast<uintptr_t>(idx) * stride;
                            LiveFieldValue::ContainerElement ce;
                            ce.index = idx;
                            std::vector<uint8_t> elemBuf(fv.setElemSize);
                            if (Mem::ReadBytesSafe(elemAddr, elemBuf.data(), fv.setElemSize)) {
                                ce.key = InterpretValue(elemTypeName, elemBuf.data(), fv.setElemSize);
                                std::string eh;
                                int elen = (std::min)(fv.setElemSize, 16);
                                for (int h = 0; h < elen; ++h) {
                                    char hx[3]; snprintf(hx, sizeof(hx), "%02X", elemBuf[h]);
                                    eh += hx;
                                }
                                ce.keyHex = eh;
                                if (elemTypeName == "ObjectProperty" || elemTypeName == "ClassProperty") {
                                    uintptr_t ptr = 0;
                                    memcpy(&ptr, elemBuf.data(), (std::min)(fv.setElemSize, (int32_t)sizeof(ptr)));
                                    if (ptr) {
                                        ce.keyPtrAddr = ptr;
                                        ce.keyPtrName = GetName(ptr);
                                        uintptr_t cls = GetClass(ptr);
                                        if (cls) ce.keyPtrClassName = GetName(cls);
                                    }
                                }
                            }
                            fv.containerElements.push_back(std::move(ce));
                            ++read;
                        }
                        Logger::Debug("WALK:SetP", "Read %d/%d set entries for '%s'", read, fv.setCount, fi.Name.c_str());
                    }
                    break;
                }
            } else {
                // UProperty mode (UE4 <4.25): USetProperty::ElementProp is a UProperty*.
                int baseOff = DynOff::UPROPERTY_OFFSET + 0x2C;
                static const int kUPropProbeOffsets[] = { 0, 8, -8, 0x10, -0x10, 4, -4 };
                for (int delta : kUPropProbeOffsets) {
                    int tryOff = baseOff + delta;
                    if (tryOff < 0) continue;
                    uintptr_t elemProp = 0;
                    if (!Mem::ReadSafe(fi.Address + tryOff, elemProp) || !elemProp) continue;
                    if (elemProp < 0x10000 || elemProp > 0x00007FFFFFFFFFFF) continue;

                    std::string elemTypeName = GetUPropertyTypeName(elemProp);
                    if (elemTypeName.empty() || elemTypeName.find("Property") == std::string::npos) continue;

                    fv.setElemType = elemTypeName;
                    Mem::ReadSafe<int32_t>(elemProp + DynOff::UPROPERTY_ELEMSIZE, fv.setElemSize);
                    Logger::Info("WALK:SetP", "USetProperty ElementProp='%s'(%d) at delta=%d for '%s'",
                        elemTypeName.c_str(), fv.setElemSize, delta, fi.Name.c_str());

                    if (elemTypeName == "StructProperty") {
                        uintptr_t eStruct = 0;
                        if (Mem::ReadSafe(elemProp + baseOff, eStruct) && eStruct) {
                            fv.setElemStructAddr = eStruct;
                            fv.setElemStructType = GetName(eStruct);
                        }
                    }

                    if (fv.setCount > 0 && sa.Data && fv.setElemSize > 0) {
                        int32_t stride = Mem::ComputeSetElementStride(fv.setElemSize);
                        int read = 0;
                        for (int32_t idx = 0; idx < sa.MaxIndex && read < fv.setCount && read < arrayLimit; ++idx) {
                            if (!Mem::IsSparseIndexAllocated(sa, idx)) continue;
                            uintptr_t elemAddr = sa.Data + static_cast<uintptr_t>(idx) * stride;
                            LiveFieldValue::ContainerElement ce;
                            ce.index = idx;
                            std::vector<uint8_t> elemBuf(fv.setElemSize);
                            if (Mem::ReadBytesSafe(elemAddr, elemBuf.data(), fv.setElemSize)) {
                                ce.key = InterpretValue(elemTypeName, elemBuf.data(), fv.setElemSize);
                                std::string eh;
                                int elen = (std::min)(fv.setElemSize, 16);
                                for (int h = 0; h < elen; ++h) {
                                    char hx[3]; snprintf(hx, sizeof(hx), "%02X", elemBuf[h]);
                                    eh += hx;
                                }
                                ce.keyHex = eh;
                                if (elemTypeName == "ObjectProperty" || elemTypeName == "ClassProperty") {
                                    uintptr_t ptr = 0;
                                    memcpy(&ptr, elemBuf.data(), (std::min)(fv.setElemSize, (int32_t)sizeof(ptr)));
                                    if (ptr) {
                                        ce.keyPtrAddr = ptr;
                                        ce.keyPtrName = GetName(ptr);
                                        uintptr_t cls = GetClass(ptr);
                                        if (cls) ce.keyPtrClassName = GetName(cls);
                                    }
                                }
                            }
                            fv.containerElements.push_back(std::move(ce));
                            ++read;
                        }
                        Logger::Debug("WALK:SetP", "Read %d/%d set entries for '%s'", read, fv.setCount, fi.Name.c_str());
                    }
                    break;
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
            if (enumPtr) {
                fv.enumName = ResolveEnumValue(enumPtr, rawVal);
                fv.enumAddr = enumPtr;
                fv.enumEntries = GetEnumEntries(enumPtr);
            }
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
                    fv.enumAddr = enumPtr;
                    fv.enumEntries = GetEnumEntries(enumPtr);
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

        // Handle StrProperty: read FString (TArray<wchar_t>) → UTF-8
        if (fi.TypeName == "StrProperty") {
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

        // Handle TextProperty: FText = { ITextData* Data; void* SharedRefController; ... }
        // Dereference Data pointer, then probe for FString at common offsets within ITextData.
        if (fi.TypeName == "TextProperty") {
            fv.strValue = ReadFTextString(instanceAddr + fi.Offset);
            fv.typedValue = fv.strValue.empty() ? "(empty)" : fv.strValue;

            // Hex: show the ITextData pointer
            uintptr_t textDataPtr = 0;
            Mem::ReadSafe(instanceAddr + fi.Offset, textDataPtr);
            char buf[20];
            snprintf(buf, sizeof(buf), "%016llX", static_cast<unsigned long long>(textDataPtr));
            fv.hexValue = buf;
            result.fields.push_back(std::move(fv));
            continue;
        }

        // Handle DelegateProperty: single FScriptDelegate = { FWeakObjectPtr(8B), FName(8/16B) }
        if (fi.TypeName == "DelegateProperty") {
            int fnameSize = DynOff::bCasePreservingName ? 0x10 : 0x08;
            uintptr_t fieldAddr = instanceAddr + fi.Offset;

            int32_t objIdx = 0, serial = 0;
            Mem::ReadSafe(fieldAddr, objIdx);
            Mem::ReadSafe(fieldAddr + 4, serial);
            uintptr_t target = ResolveWeakObjectPtr(objIdx, serial);
            std::string funcName = ReadFName(fieldAddr + 8);

            if (target && !funcName.empty()) {
                std::string targetName = GetName(target);
                fv.typedValue = targetName + "::" + funcName;
                fv.ptrValue = target;
                fv.ptrName = targetName;
                uintptr_t cls = GetClass(target);
                if (cls) {
                    fv.ptrClassName = GetName(cls);
                    fv.ptrClassAddr = cls;
                }
            } else if (!funcName.empty()) {
                fv.typedValue = "(stale)::" + funcName;
            } else {
                fv.typedValue = "(unbound)";
            }

            // Hex: FWeakObjectPtr + FName raw bytes
            int delegateSize = 8 + fnameSize;
            std::vector<uint8_t> buf(delegateSize, 0);
            if (Mem::ReadBytesSafe(fieldAddr, buf.data(), delegateSize)) {
                std::string hex;
                hex.reserve(delegateSize * 2);
                for (auto b : buf) { char hx[3]; snprintf(hx, sizeof(hx), "%02X", b); hex += hx; }
                fv.hexValue = hex;
            }
            result.fields.push_back(std::move(fv));
            continue;
        }

        // Handle MulticastInlineDelegateProperty / MulticastDelegateProperty:
        // FMulticastScriptDelegate = { TArray<FScriptDelegate> InvocationList (16B) }
        // FScriptDelegate = { FWeakObjectPtr(8B), FName(8/16B) }
        if (fi.TypeName == "MulticastInlineDelegateProperty" ||
            fi.TypeName == "MulticastDelegateProperty") {
            int fnameSize = DynOff::bCasePreservingName ? 0x10 : 0x08;
            int delegateElemSize = 8 + fnameSize;  // FWeakObjectPtr + FName
            uintptr_t fieldAddr = instanceAddr + fi.Offset;

            // Read TArray<FScriptDelegate> header
            uintptr_t data = 0;
            int32_t count = 0;
            Mem::ReadSafe(fieldAddr, data);
            Mem::ReadSafe(fieldAddr + 8, count);

            if (count < 0 || count > 256) count = 0;  // Sanity clamp

            std::string display;
            if (count == 0) {
                display = "(0 bindings)";
            } else {
                display = "(" + std::to_string(count) + " binding" + (count > 1 ? "s" : "") + ")";

                // Read up to 8 binding target names for preview
                int previewCount = (std::min)(count, (int32_t)8);
                std::vector<std::string> bindings;
                for (int i = 0; i < previewCount; ++i) {
                    uintptr_t elemAddr = data + static_cast<int64_t>(i) * delegateElemSize;
                    int32_t objIdx = 0, serial = 0;
                    if (!Mem::ReadSafe(elemAddr, objIdx) || !Mem::ReadSafe(elemAddr + 4, serial))
                        continue;

                    uintptr_t target = ResolveWeakObjectPtr(objIdx, serial);
                    std::string funcName = ReadFName(elemAddr + 8);

                    if (target && !funcName.empty()) {
                        bindings.push_back(GetName(target) + "::" + funcName);
                    } else if (!funcName.empty()) {
                        bindings.push_back("(stale)::" + funcName);
                    }
                }

                if (!bindings.empty()) {
                    display += " [";
                    for (size_t i = 0; i < bindings.size(); ++i) {
                        if (i > 0) display += ", ";
                        display += bindings[i];
                    }
                    if (count > previewCount) display += ", ...";
                    display += "]";
                }
            }

            fv.typedValue = display;

            // Hex: TArray header (Data ptr + Count)
            char buf[48];
            snprintf(buf, sizeof(buf), "%016llX %08X",
                static_cast<unsigned long long>(data), count);
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

// ============================================================
// ResolvePropertyPreviews — fill PropertyMatch.preview with
// live values from representative instances (Phase 2 of search).
// ============================================================
void ResolvePropertyPreviews(
    std::vector<ObjectArray::PropertyMatch>& matches,
    const std::unordered_map<uintptr_t, uintptr_t>& instanceMap)
{
    for (auto& m : matches) {
        auto it = instanceMap.find(m.classAddr);
        if (it == instanceMap.end()) continue;

        uintptr_t inst = it->second;
        if (!inst) continue;

        const std::string& t = m.propType;
        int32_t off = m.propOffset;
        int32_t sz  = m.propSize;

        // --- Scalar primitives: read bytes + InterpretValue ---
        if (t == "FloatProperty" || t == "DoubleProperty" ||
            t == "IntProperty" || t == "UInt32Property" ||
            t == "Int64Property" || t == "UInt64Property" ||
            t == "Int16Property" || t == "UInt16Property" ||
            t == "ByteProperty" || t == "Int8Property" ||
            t == "NameProperty")
        {
            if (sz > 0 && sz <= 64) {
                uint8_t buf[64] = {};
                if (Mem::ReadBytesSafe(inst + off, buf, sz)) {
                    m.preview = InterpretValue(t, buf, sz);
                }
            }
            continue;
        }

        // --- BoolProperty: bitfield-aware ---
        if (t == "BoolProperty") {
            uint8_t rawByte = 0;
            int readOff = off + m.boolByteOffset;
            if (Mem::ReadSafe(inst + readOff, rawByte)) {
                if (m.boolFieldMask != 0) {
                    m.preview = (rawByte & m.boolFieldMask) ? "true" : "false";
                } else {
                    m.preview = rawByte ? "true" : "false";
                }
            }
            continue;
        }

        // --- StrProperty: read FString ---
        if (t == "StrProperty") {
            std::string s = ReadFString(inst, off);
            if (s.empty()) {
                m.preview = "(empty)";
            } else {
                // Truncate long strings
                if (s.size() > 50) {
                    s.resize(50);
                    s += "\xe2\x80\xa6";  // UTF-8 ellipsis '…'
                }
                m.preview = "\"" + s + "\"";
            }
            continue;
        }

        // --- EnumProperty: read raw int + resolve enum name ---
        if (t == "EnumProperty") {
            int64_t rawVal = 0;
            if (sz == 1)      { uint8_t v = 0; Mem::ReadSafe(inst + off, v); rawVal = v; }
            else if (sz == 2) { int16_t v = 0; Mem::ReadSafe(inst + off, v); rawVal = v; }
            else if (sz == 4) { int32_t v = 0; Mem::ReadSafe(inst + off, v); rawVal = v; }
            else if (sz == 8) { int64_t v = 0; Mem::ReadSafe(inst + off, v); rawVal = v; }

            if (m.enumAddr) {
                std::string enumName = ResolveEnumValue(m.enumAddr, rawVal);
                if (!enumName.empty()) {
                    m.preview = enumName;
                    continue;
                }
            }
            m.preview = std::to_string(rawVal);
            continue;
        }

        // --- ObjectProperty / ClassProperty: read pointer, resolve name ---
        if (t == "ObjectProperty" || t == "ClassProperty" ||
            t == "WeakObjectProperty" || t == "LazyObjectProperty" ||
            t == "SoftClassProperty")
        {
            uintptr_t ptr = 0;
            Mem::ReadSafe(inst + off, ptr);
            if (!ptr) {
                m.preview = "null";
            } else {
                std::string name = GetName(ptr);
                if (!name.empty()) {
                    m.preview = name;
                } else {
                    char buf[24];
                    snprintf(buf, sizeof(buf), "0x%llX", static_cast<unsigned long long>(ptr));
                    m.preview = buf;
                }
            }
            continue;
        }

        // --- StructProperty: try float hint, fallback to type name ---
        if (t == "StructProperty") {
            if (sz > 0 && sz <= 256) {
                std::vector<uint8_t> buf(sz, 0);
                if (Mem::ReadBytesSafe(inst + off, buf.data(), sz)) {
                    std::string hint = InterpretValue(t, buf.data(), sz);
                    if (!hint.empty()) {
                        m.preview = hint;
                        continue;
                    }
                }
            }
            // Fallback: show struct type name
            if (!m.structType.empty()) {
                m.preview = "{" + m.structType + "}";
            }
            continue;
        }

        // --- ArrayProperty: read TArray count ---
        if (t == "ArrayProperty") {
            Mem::TArrayView arr;
            if (Mem::ReadTArray(inst + off, arr) && arr.Count >= 0) {
                m.preview = "[" + std::to_string(arr.Count) + " x " +
                            (m.innerType.empty() ? "?" : m.innerType) + "]";
            }
            continue;
        }

        // --- MapProperty: read FScriptMap count ---
        if (t == "MapProperty") {
            // FScriptMap layout: FScriptSet { FHashAllocator {SparseArray {Data(8) Count(4) ...}}}
            // The count is at offset +8 within the FScriptMap (FScriptSet.Elements.Count)
            int32_t count = 0;
            Mem::ReadSafe(inst + off + 8, count);
            if (count >= 0 && count < 1000000) {
                std::string keyStr = m.keyType.empty() ? "?" : m.keyType;
                std::string valStr = m.valueType.empty() ? "?" : m.valueType;
                // Shorten type names: remove "Property" suffix
                auto shorten = [](const std::string& s) -> std::string {
                    const std::string suffix = "Property";
                    if (s.size() > suffix.size() &&
                        s.compare(s.size() - suffix.size(), suffix.size(), suffix) == 0)
                        return s.substr(0, s.size() - suffix.size());
                    return s;
                };
                m.preview = "{Map: " + std::to_string(count) + ", " +
                            shorten(keyStr) + "\xe2\x86\x92" + shorten(valStr) + "}";
            }
            continue;
        }

        // --- SetProperty: read count ---
        if (t == "SetProperty") {
            int32_t count = 0;
            Mem::ReadSafe(inst + off + 8, count);
            if (count >= 0 && count < 1000000) {
                m.preview = "{Set: " + std::to_string(count) + "}";
            }
            continue;
        }

        // --- TextProperty: just show type ---
        if (t == "TextProperty") {
            m.preview = "(FText)";
            continue;
        }
    }
}

} // namespace UStructWalker
