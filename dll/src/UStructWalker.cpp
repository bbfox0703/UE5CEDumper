// ============================================================
// UStructWalker.cpp — FField chain traversal implementation
// ============================================================

#include "UStructWalker.h"
#include "Memory.h"
#include "Logger.h"
#include "Constants.h"
#include "FNamePool.h"

#include <algorithm>

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

uintptr_t GetClass(uintptr_t uobjectAddr) {
    if (!uobjectAddr) return 0;
    uintptr_t cls = 0;
    Mem::ReadSafe(uobjectAddr + Constants::OFF_UOBJECT_CLASS, cls);
    return cls;
}

uintptr_t GetOuter(uintptr_t uobjectAddr) {
    if (!uobjectAddr) return 0;
    uintptr_t outer = 0;
    Mem::ReadSafe(uobjectAddr + Constants::OFF_UOBJECT_OUTER, outer);
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

// Read the type name from an FFieldClass*
static std::string GetFieldTypeName(uintptr_t ffieldAddr) {
    // FField::ClassPrivate at offset 0x08 -> FFieldClass*
    uintptr_t fieldClass = 0;
    if (!Mem::ReadSafe(ffieldAddr + Constants::OFF_FFIELD_CLASS, fieldClass) || !fieldClass) {
        return "Unknown";
    }

    // FFieldClass has Name (FName) at offset 0x00
    return ReadFName(fieldClass + Constants::OFF_FFIELDCLASS_NAME);
}

// Walk the FField chain starting from the first field
static void WalkFFieldChain(uintptr_t firstField, std::vector<FieldInfo>& fields) {
    uintptr_t current = firstField;
    int safetyLimit = 4096;

    while (current != 0 && safetyLimit-- > 0) {
        FieldInfo fi{};
        fi.Address = current;

        // Read field name
        fi.Name = ReadFName(current + Constants::OFF_FFIELD_NAME);

        // Read type name from FFieldClass
        fi.TypeName = GetFieldTypeName(current);

        // Read offset and size (FProperty fields, may not be valid for non-property FFields)
        Mem::ReadSafe<int32_t>(current + Constants::OFF_FPROPERTY_OFFSET, fi.Offset);
        Mem::ReadSafe<int32_t>(current + Constants::OFF_FPROPERTY_ELEMSIZE, fi.Size);
        Mem::ReadSafe<uint64_t>(current + Constants::OFF_FPROPERTY_FLAGS, fi.PropertyFlags);

        if (!fi.Name.empty()) {
            fields.push_back(fi);
        }

        // Move to next FField
        uintptr_t next = 0;
        if (!Mem::ReadSafe(current + Constants::OFF_FFIELD_NEXT, next)) break;
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
    Mem::ReadSafe(uclassAddr + Constants::OFF_USTRUCT_SUPER, info.SuperClass);
    if (info.SuperClass) {
        info.SuperName = GetName(info.SuperClass);
    }

    // Read PropertiesSize
    Mem::ReadSafe(uclassAddr + Constants::OFF_USTRUCT_PROPSSIZE, info.PropertiesSize);

    LOG_DEBUG("WalkClass: %s (super=%s, size=%d) at 0x%llX",
              info.Name.c_str(), info.SuperName.c_str(), info.PropertiesSize,
              static_cast<unsigned long long>(uclassAddr));

    // Walk the FField chain (ChildProperties)
    uintptr_t childProps = 0;
    if (Mem::ReadSafe(uclassAddr + Constants::OFF_USTRUCT_CHILDPROPS, childProps) && childProps) {
        WalkFFieldChain(childProps, info.Fields);
    }

    // Also walk inherited fields from SuperStruct chain
    uintptr_t super = info.SuperClass;
    int depth = 0;
    while (super != 0 && depth < 32) {
        uintptr_t superChildProps = 0;
        if (Mem::ReadSafe(super + Constants::OFF_USTRUCT_CHILDPROPS, superChildProps) && superChildProps) {
            std::vector<FieldInfo> inherited;
            WalkFFieldChain(superChildProps, inherited);
            // Prepend inherited fields (they come first in memory layout)
            info.Fields.insert(info.Fields.begin(), inherited.begin(), inherited.end());
        }

        uintptr_t nextSuper = 0;
        Mem::ReadSafe(super + Constants::OFF_USTRUCT_SUPER, nextSuper);
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
        // Use snprintf for clean formatting (avoid trailing zeros)
        char buf[32];
        snprintf(buf, sizeof(buf), "%.4f", v);
        return buf;
    }
    if (typeName == "DoubleProperty" && size >= 8) {
        double v;
        memcpy(&v, bytes, 8);
        char buf[32];
        snprintf(buf, sizeof(buf), "%.6f", v);
        return buf;
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
    if (typeName == "BoolProperty") {
        return bytes[0] ? "true" : "false";
    }
    if (typeName == "NameProperty" && size >= 4) {
        // FName — resolve via FNamePool
        int32_t nameIdx;
        memcpy(&nameIdx, bytes, 4);
        return FNamePool::GetString(nameIdx);
    }

    return ""; // Unknown type — caller shows hex
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

    // Walk the class to get field layout
    ClassInfo ci = WalkClass(classAddr);

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

        // Handle ArrayProperty: read TArray header
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
