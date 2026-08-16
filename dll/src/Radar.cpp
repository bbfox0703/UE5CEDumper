// ============================================================
// Radar — 拉達爾 (影子戰士 — Shadow Warrior)
// CE-style value scan: SessionManager implementation + type/predicate helpers
// ============================================================

#include "Radar.h"

#include <algorithm>
#include <cctype>
#include <cmath>
#include <cstdio>
#include <cstring>
#include <sstream>

namespace Radar {

// --- DataType / ScanType helpers ---

size_t SizeOf(DataType dt) {
    switch (dt) {
        case DataType::Int8:   case DataType::UInt8:  case DataType::Bool:   return 1;
        case DataType::Int16:  case DataType::UInt16:                        return 2;
        case DataType::Int32:  case DataType::UInt32: case DataType::Float:  return 4;
        case DataType::Int64:  case DataType::UInt64: case DataType::Double: return 8;
        // String types use the candidate's prevStr field instead of
        // the byte buffer — return 0 so callers can branch on it.
        case DataType::FString: case DataType::FName: case DataType::FText: return 0;
        // Vector primitives have NO single width: UE5's LWC made "FVector" /
        // "FRotator" 3xdouble (24B) while "FVector3f" / "FRotator3f" stayed
        // 3xfloat (12B), and one game holds both. 0 = variable, the same
        // signal the string + multi-numeric families use; the authoritative
        // per-field width is FieldDescriptor::vectorWidth, read from the
        // property's reflected ElementSize at index-build time.
        // This used to answer a flat 12, which made every LWC vector scan
        // compare the first four bytes of each double against a float — junk
        // that could never match. (audit #5 AB5)
        case DataType::FVector: case DataType::FRotator: case DataType::FTransform: return 0;
        // Multi-numeric meta types have no single width — the scan engine
        // resolves SizeOf per member; 0 signals "variable" like strings.
        case DataType::NumericNoByte:
        case DataType::NumericAll: return 0;
    }
    return 0;
}

const char* NameOf(DataType dt) {
    switch (dt) {
        case DataType::Int8:   return "Int8";
        case DataType::Int16:  return "Int16";
        case DataType::Int32:  return "Int32";
        case DataType::Int64:  return "Int64";
        case DataType::UInt8:  return "UInt8";
        case DataType::UInt16: return "UInt16";
        case DataType::UInt32: return "UInt32";
        case DataType::UInt64: return "UInt64";
        case DataType::Float:  return "Float";
        case DataType::Double: return "Double";
        case DataType::Bool:   return "Bool";
        case DataType::FString:    return "FString";
        case DataType::FName:      return "FName";
        case DataType::FText:      return "FText";
        case DataType::FVector:    return "FVector";
        case DataType::FRotator:   return "FRotator";
        case DataType::FTransform: return "FTransform";
        case DataType::NumericNoByte: return "NumericNoByte";
        case DataType::NumericAll:    return "NumericAll";
    }
    return "?";
}

bool TryParseDataType(const std::string& s, DataType& out) {
    if (s == "Int8")   { out = DataType::Int8;   return true; }
    if (s == "Int16")  { out = DataType::Int16;  return true; }
    if (s == "Int32")  { out = DataType::Int32;  return true; }
    if (s == "Int64")  { out = DataType::Int64;  return true; }
    if (s == "UInt8")  { out = DataType::UInt8;  return true; }
    if (s == "UInt16") { out = DataType::UInt16; return true; }
    if (s == "UInt32") { out = DataType::UInt32; return true; }
    if (s == "UInt64") { out = DataType::UInt64; return true; }
    if (s == "Float")  { out = DataType::Float;  return true; }
    if (s == "Double") { out = DataType::Double; return true; }
    if (s == "Bool")   { out = DataType::Bool;   return true; }
    if (s == "FString")    { out = DataType::FString;    return true; }
    if (s == "FName")      { out = DataType::FName;      return true; }
    if (s == "FText")      { out = DataType::FText;      return true; }
    if (s == "FVector")    { out = DataType::FVector;    return true; }
    if (s == "FRotator")   { out = DataType::FRotator;   return true; }
    if (s == "FTransform") { out = DataType::FTransform; return true; }
    if (s == "NumericNoByte") { out = DataType::NumericNoByte; return true; }
    if (s == "NumericAll")    { out = DataType::NumericAll;    return true; }
    return false;
}

const char* NameOf(ScanType st) {
    switch (st) {
        case ScanType::Exact:      return "Exact";
        case ScanType::Bigger:     return "Bigger";
        case ScanType::Smaller:    return "Smaller";
        case ScanType::Between:    return "Between";
        case ScanType::Changed:    return "Changed";
        case ScanType::Unchanged:  return "Unchanged";
        case ScanType::Increased:  return "Increased";
        case ScanType::Decreased:  return "Decreased";
        case ScanType::Contains:   return "Contains";
        case ScanType::StartsWith: return "StartsWith";
        case ScanType::EndsWith:   return "EndsWith";
    }
    return "Exact";
}

bool TryParseScanType(const std::string& s, ScanType& out) {
    if (s == "Exact")      { out = ScanType::Exact;      return true; }
    if (s == "Bigger")     { out = ScanType::Bigger;     return true; }
    if (s == "Smaller")    { out = ScanType::Smaller;    return true; }
    if (s == "Between")    { out = ScanType::Between;    return true; }
    if (s == "Changed")    { out = ScanType::Changed;    return true; }
    if (s == "Unchanged")  { out = ScanType::Unchanged;  return true; }
    if (s == "Increased")  { out = ScanType::Increased;  return true; }
    if (s == "Decreased")  { out = ScanType::Decreased;  return true; }
    if (s == "Contains")   { out = ScanType::Contains;   return true; }
    if (s == "StartsWith") { out = ScanType::StartsWith; return true; }
    if (s == "EndsWith")   { out = ScanType::EndsWith;   return true; }
    return false;
}

const char* NameOf(RoundMode rm) {
    switch (rm) {
        case RoundMode::Round: return "Round";
        case RoundMode::Trunc: return "Trunc";
        case RoundMode::Ceil:  return "Ceil";
    }
    return "Round";
}

bool TryParseRoundMode(const std::string& s, RoundMode& out) {
    if (s == "Round") { out = RoundMode::Round; return true; }
    if (s == "Trunc") { out = RoundMode::Trunc; return true; }
    if (s == "Ceil")  { out = RoundMode::Ceil;  return true; }
    return false;
}

bool IsPrevValueScanType(ScanType st) {
    switch (st) {
        case ScanType::Changed:
        case ScanType::Unchanged:
        case ScanType::Increased:
        case ScanType::Decreased:
            return true;
        default:
            return false;
    }
}

bool IsFirstScanType(ScanType st) {
    switch (st) {
        case ScanType::Exact:
        case ScanType::Bigger:
        case ScanType::Smaller:
        case ScanType::Between:
        case ScanType::Contains:
        case ScanType::StartsWith:
        case ScanType::EndsWith:
            return true;
        default:
            return false;
    }
}

bool IsStringDataType(DataType dt) {
    return dt == DataType::FString || dt == DataType::FName || dt == DataType::FText;
}

bool IsVectorDataType(DataType dt) {
    return dt == DataType::FVector || dt == DataType::FRotator || dt == DataType::FTransform;
}

bool IsSupportedVectorWidth(int32_t width) {
    return width == VECTOR_WIDTH_FLOAT || width == VECTOR_WIDTH_DOUBLE;
}

bool DecodeVectorBytes(const uint8_t* src, int32_t width, double out[3]) {
    if (!src || !out) return false;
    if (width == VECTOR_WIDTH_FLOAT) {
        float v[3];
        std::memcpy(v, src, sizeof(v));
        out[0] = static_cast<double>(v[0]);
        out[1] = static_cast<double>(v[1]);
        out[2] = static_cast<double>(v[2]);
        return true;
    }
    if (width == VECTOR_WIDTH_DOUBLE) {
        std::memcpy(out, src, sizeof(double) * 3);
        return true;
    }
    // Refuse rather than guess. A struct that passed the NAME gate but reports
    // some other size is not an X/Y/Z triple we can read (FVector2D, FVector4,
    // or a game's own namesake) — reading it at a guessed width would emit
    // candidates whose value is nonsense.
    return false;
}

void StoreVectorCanonical(const double v[3], uint8_t* dst) {
    if (!v || !dst) return;
    std::memcpy(dst, v, sizeof(double) * 3);
}

bool IsSubstringScanType(ScanType st) {
    return st == ScanType::Contains || st == ScanType::StartsWith || st == ScanType::EndsWith;
}

bool IsScanTypeValidFor(DataType dt, ScanType st) {
    if (IsStringDataType(dt)) {
        // Strings: Exact, Contains, StartsWith, EndsWith, Changed, Unchanged.
        // No ordering -> reject Bigger/Smaller/Between/Increased/Decreased.
        switch (st) {
            case ScanType::Exact:
            case ScanType::Contains:
            case ScanType::StartsWith:
            case ScanType::EndsWith:
            case ScanType::Changed:
            case ScanType::Unchanged:
                return true;
            default:
                return false;
        }
    }
    if (IsVectorDataType(dt)) {
        // Vectors: component-wise ordering applies; substring predicates reject.
        switch (st) {
            case ScanType::Exact:
            case ScanType::Bigger:
            case ScanType::Smaller:
            case ScanType::Between:
            case ScanType::Changed:
            case ScanType::Unchanged:
            case ScanType::Increased:
            case ScanType::Decreased:
                return true;
            default:
                return false;
        }
    }
    // Numeric primitives: substring predicates reject, everything else valid.
    return !IsSubstringScanType(st);
}

const std::vector<std::string>& PropertyTypeNames(DataType dt) {
    // Per-DataType static tables, populated on first call. Returning by
    // const-ref keeps the hot loop allocation-free.
    static const std::vector<std::string> kInt8   = { "Int8Property" };
    static const std::vector<std::string> kInt16  = { "Int16Property" };
    static const std::vector<std::string> kInt32  = { "IntProperty" };
    static const std::vector<std::string> kInt64  = { "Int64Property" };
    static const std::vector<std::string> kUInt8  = { "ByteProperty" };
    static const std::vector<std::string> kUInt16 = { "UInt16Property" };
    static const std::vector<std::string> kUInt32 = { "UInt32Property" };
    static const std::vector<std::string> kUInt64 = { "UInt64Property" };
    static const std::vector<std::string> kFloat  = { "FloatProperty" };
    static const std::vector<std::string> kDouble = { "DoubleProperty" };
    static const std::vector<std::string> kBool   = { "BoolProperty" };
    static const std::vector<std::string> kFString = { "StrProperty" };
    static const std::vector<std::string> kFName   = { "NameProperty" };
    static const std::vector<std::string> kFText   = { "TextProperty" };
    // Vector / Rotator / Transform are represented as StructProperty in
    // ClassInfo.Fields. The scan-side caller must also match the inner
    // UScriptStruct name (see VectorStructNames) before emitting.
    static const std::vector<std::string> kStruct = { "StructProperty" };
    // NumericNoByte union — every word/dword/qword/float/double property
    // type, minus the 1-byte families (ByteProperty / Int8Property) and
    // BoolProperty. MUST stay in sync with the names recognised by
    // TryDataTypeFromPropertyTypeName below, or a field could be
    // accepted into the scan index yet fail per-field DataType resolution.
    static const std::vector<std::string> kNumericNoByte = {
        "Int16Property", "UInt16Property",
        "IntProperty",   "UInt32Property",
        "Int64Property", "UInt64Property",
        "FloatProperty", "DoubleProperty",
    };
    // NumericAll union — NumericNoByte plus the 1-byte families
    // (Int8Property + ByteProperty). MUST stay in sync with the names
    // TryDataTypeFromPropertyTypeName resolves.
    static const std::vector<std::string> kNumericAll = {
        "Int8Property",  "ByteProperty",
        "Int16Property", "UInt16Property",
        "IntProperty",   "UInt32Property",
        "Int64Property", "UInt64Property",
        "FloatProperty", "DoubleProperty",
    };
    static const std::vector<std::string> kEmpty;

    switch (dt) {
        case DataType::Int8:   return kInt8;
        case DataType::Int16:  return kInt16;
        case DataType::Int32:  return kInt32;
        case DataType::Int64:  return kInt64;
        case DataType::UInt8:  return kUInt8;
        case DataType::UInt16: return kUInt16;
        case DataType::UInt32: return kUInt32;
        case DataType::UInt64: return kUInt64;
        case DataType::Float:  return kFloat;
        case DataType::Double: return kDouble;
        case DataType::Bool:   return kBool;
        case DataType::FString: return kFString;
        case DataType::FName:   return kFName;
        case DataType::FText:   return kFText;
        case DataType::FVector:
        case DataType::FRotator:
        case DataType::FTransform: return kStruct;
        case DataType::NumericNoByte: return kNumericNoByte;
        case DataType::NumericAll:    return kNumericAll;
    }
    return kEmpty;
}

const std::vector<std::string>& VectorStructNames(DataType dt) {
    // UE5 introduced Large World Coordinate variants — FVector became
    // double-precision (kept as "Vector"), with explicit float variants
    // FVector3f / FVector4f. Cooked-game StructProperty inner names
    // vary by version + variant, so this list is a NAME gate only: it says
    // "this struct is an X/Y/Z triple", never how wide it is.
    //
    // The width is settled separately, per field, from the property's own
    // reflected ElementSize (IsSupportedVectorWidth / DecodeVectorBytes), so
    // "Vector" is correct to accept on BOTH engines — 12 bytes on UE4, 24 on
    // an LWC UE5 build — and a struct here whose size is neither is refused
    // by the index builder instead of being read at a guessed width.
    // Before build 3035 the scan read a flat 12 bytes from whatever this list
    // matched, so on UE5 it compared the low four bytes of three doubles.
    static const std::vector<std::string> kVec  = { "Vector", "Vector3f", "Vector_NetQuantize", "Vector_NetQuantizeNormal" };
    static const std::vector<std::string> kRot  = { "Rotator", "Rotator3f" };
    // FTransform deferred: layout starts with FQuat Rotation (16B UE4 /
    // 32B UE5 LWC), so reading 12 bytes from struct start would grab
    // the Rotation quat's first three floats, not Translation. Proper
    // support needs per-version offset detection (Translation lives at
    // +16 non-LWC, +32 LWC). Leaving FTransform DataType in the enum
    // so the C# wire enum can stay stable, but no struct names map to
    // it -- scan returns 0 candidates until the per-version offset
    // table lands.
    static const std::vector<std::string> kEmpty;
    switch (dt) {
        case DataType::FVector:    return kVec;
        case DataType::FRotator:   return kRot;
        case DataType::FTransform: return kEmpty;
        default:                   return kEmpty;
    }
}

// --- Multi-numeric meta type helpers ---

bool IsMultiNumericDataType(DataType dt) {
    return dt == DataType::NumericNoByte || dt == DataType::NumericAll;
}

const std::vector<DataType>& MultiNumericMembers(DataType dt) {
    static const std::vector<DataType> kNoByte = {
        DataType::Int16, DataType::UInt16,
        DataType::Int32, DataType::UInt32,
        DataType::Int64, DataType::UInt64,
        DataType::Float, DataType::Double,
    };
    static const std::vector<DataType> kAll = {
        DataType::Int8,  DataType::UInt8,
        DataType::Int16, DataType::UInt16,
        DataType::Int32, DataType::UInt32,
        DataType::Int64, DataType::UInt64,
        DataType::Float, DataType::Double,
    };
    static const std::vector<DataType> kEmpty;
    switch (dt) {
        case DataType::NumericNoByte: return kNoByte;
        case DataType::NumericAll:    return kAll;
        default:                      return kEmpty;
    }
}

bool TryDataTypeFromPropertyTypeName(const std::string& propTypeName, DataType& out) {
    // The full numeric member set is mapped (NumericAll includes the
    // 1-byte families). BoolProperty deliberately returns false — neither
    // meta type includes bool. NumericNoByte never feeds byte-width names
    // here because its PropertyTypeNames union excludes them.
    if (propTypeName == "Int8Property")   { out = DataType::Int8;   return true; }
    if (propTypeName == "ByteProperty")   { out = DataType::UInt8;  return true; }
    if (propTypeName == "Int16Property")  { out = DataType::Int16;  return true; }
    if (propTypeName == "UInt16Property") { out = DataType::UInt16; return true; }
    if (propTypeName == "IntProperty")    { out = DataType::Int32;  return true; }
    if (propTypeName == "UInt32Property") { out = DataType::UInt32; return true; }
    if (propTypeName == "Int64Property")  { out = DataType::Int64;  return true; }
    if (propTypeName == "UInt64Property") { out = DataType::UInt64; return true; }
    if (propTypeName == "FloatProperty")  { out = DataType::Float;  return true; }
    if (propTypeName == "DoubleProperty") { out = DataType::Double; return true; }
    return false;
}

const char* PropertyTypeNameOf(DataType dt) {
    // Inverse of TryDataTypeFromPropertyTypeName — keep the two in lock-step so a
    // native-scan descriptor stamped here always re-resolves on refine.
    switch (dt) {
        case DataType::Int8:   return "Int8Property";
        case DataType::UInt8:  return "ByteProperty";
        case DataType::Int16:  return "Int16Property";
        case DataType::UInt16: return "UInt16Property";
        case DataType::Int32:  return "IntProperty";
        case DataType::UInt32: return "UInt32Property";
        case DataType::Int64:  return "Int64Property";
        case DataType::UInt64: return "UInt64Property";
        case DataType::Float:  return "FloatProperty";
        case DataType::Double: return "DoubleProperty";
        default:               return "";   // Bool / string / vector / meta
    }
}

std::vector<SnapshotFieldPick> SelectSnapshotNumericFields(
    const std::vector<std::string>& propTypeNames, DataType numericScope) {
    std::vector<SnapshotFieldPick> picks;
    const std::vector<DataType>& members = MultiNumericMembers(numericScope);
    if (members.empty()) return picks;  // not a meta scope -> capture nothing

    for (int32_t i = 0; i < static_cast<int32_t>(propTypeNames.size()); ++i) {
        DataType dt;
        if (!TryDataTypeFromPropertyTypeName(propTypeNames[i], dt)) continue;  // non-numeric / bool
        bool inScope = false;
        for (DataType m : members) {
            if (m == dt) { inScope = true; break; }
        }
        if (!inScope) continue;  // e.g. Int8/UInt8 under NumericNoByte
        picks.push_back({ i, dt });
    }
    return picks;
}

int SelectArrayInnerKey(const std::vector<std::string>& typeNames,
                        const std::vector<std::string>& fieldNames) {
    int firstName = -1, firstInt = -1;
    size_t n = std::min(typeNames.size(), fieldNames.size());
    for (size_t i = 0; i < n; ++i) {
        const std::string& t = typeNames[i];
        if (t == "NameProperty") {
            if (firstName < 0) firstName = static_cast<int>(i);
            std::string lo;
            lo.reserve(fieldNames[i].size());
            for (char c : fieldNames[i])
                lo.push_back(static_cast<char>(std::tolower(static_cast<unsigned char>(c))));
            if (lo.find("id")   != std::string::npos ||
                lo.find("name") != std::string::npos ||
                lo.find("tag")  != std::string::npos ||
                lo.find("key")  != std::string::npos ||
                lo.find("row")  != std::string::npos)
                return static_cast<int>(i);  // keyworded FName — strongest signal
        }
        else if (firstInt < 0 &&
                 (t == "IntProperty"  || t == "Int64Property"  || t == "UInt32Property" ||
                  t == "UInt64Property" || t == "Int16Property" || t == "UInt16Property" ||
                  t == "ByteProperty" || t == "Int8Property")) {
            firstInt = static_cast<int>(i);
        }
    }
    if (firstName >= 0) return firstName;  // any FName beats an integer key
    return firstInt;                        // -1 if neither -> caller uses elem index
}

// Reduce a fractional value to the integer-valued double the game DISPLAYS, per
// the rounding mode. Round = half-away-from-zero (std::round), Trunc = toward
// zero, Ceil = toward +inf. Shared by the float compare predicate and the
// integer-coercion in BuildNumericTargets / ParseValueBytes so "Between 10.9~11.1"
// on an integer width yields 11~11 (Round) / 10~11 (Trunc) / 11~12 (Ceil).
double ReduceRounded(double x, RoundMode m) {
    switch (m) {
        case RoundMode::Trunc: return std::trunc(x);
        case RoundMode::Ceil:  return std::ceil(x);
        case RoundMode::Round:
        default:               return std::round(x);
    }
}

bool BuildNumericTargets(DataType metaDt, const std::string& raw, NumericTargetSet& out,
                         RoundMode roundMode) {
    out.entries.clear();
    const auto& members = MultiNumericMembers(metaDt);
    if (members.empty()) return false;

    // Trim surrounding whitespace (mirrors ParseValueBytes — the UI's
    // NumericTextBox can ship a trailing newline).
    size_t lo = 0, hi = raw.size();
    auto isWs = [](char c) {
        return c == ' ' || c == '\t' || c == '\r' || c == '\n' || c == '\f' || c == '\v';
    };
    while (lo < hi && isWs(raw[lo])) ++lo;
    while (hi > lo && isWs(raw[hi - 1])) --hi;
    if (lo >= hi) return false;
    const std::string s = raw.substr(lo, hi - lo);

    const bool isHex = s.size() > 2 && s[0] == '0' && (s[1] == 'x' || s[1] == 'X');
    const bool isNeg = s[0] == '-';

    // Parse each interpretation independently; require the WHOLE string
    // to be consumed so "100.5" doesn't pass as integer 100. Base 0
    // mirrors ParseValueBytes (auto-detects hex; leading-0 = octal).
    bool     hasSigned = false; int64_t  sv = 0;
    bool     hasUnsigned = false; uint64_t uv = 0;
    bool     hasFloat = false; double   dv = 0.0;

    try {
        size_t pos = 0;
        long long v = std::stoll(s, &pos, 0);
        if (pos == s.size()) { hasSigned = true; sv = static_cast<int64_t>(v); }
    } catch (...) {}

    if (!isNeg) {
        try {
            size_t pos = 0;
            unsigned long long v = std::stoull(s, &pos, isHex ? 16 : 0);
            if (pos == s.size()) { hasUnsigned = true; uv = static_cast<uint64_t>(v); }
        } catch (...) {}
    }

    if (!isHex) {
        try {
            size_t pos = 0;
            double v = std::stod(s, &pos);
            if (pos == s.size()) { hasFloat = true; dv = v; }
        } catch (...) {}
    }

    // Rounding-mode integer coercion (build 1672): a fractional input (e.g.
    // "10.9", "11.0") parses as a float but NOT as a clean integer, so integer-
    // width members would otherwise be skipped. Reduce it to the displayed
    // integer via the mode so those members still match (Round 10.9->11,
    // Trunc->10, Ceil->11). A clean integer string already set hasSigned/
    // hasUnsigned, so the !has* guards make this a no-op there; Float/Double
    // members keep the exact fractional `dv` regardless.
    if (hasFloat) {
        const double r = ReduceRounded(dv, roundMode);
        if (!hasSigned && r >= -9223372036854775808.0 && r < 9223372036854775808.0) {
            sv = static_cast<int64_t>(r); hasSigned = true;
        }
        if (!hasUnsigned && !isNeg && r >= 0.0 && r < 18446744073709551616.0) {
            uv = static_cast<uint64_t>(r); hasUnsigned = true;
        }
    }

    auto push = [&](DataType dt, const void* src, size_t n) {
        NumericTargetSet::Entry e;
        e.dt = dt;
        std::memset(e.bytes, 0, sizeof(e.bytes));
        std::memcpy(e.bytes, src, n);
        out.entries.push_back(e);
    };

    for (DataType m : members) {
        switch (m) {
            case DataType::Int8:
                if (hasSigned && sv >= INT8_MIN && sv <= INT8_MAX) {
                    int8_t t = static_cast<int8_t>(sv); push(m, &t, 1);
                }
                break;
            case DataType::UInt8:
                if (hasUnsigned && uv <= UINT8_MAX) {
                    uint8_t t = static_cast<uint8_t>(uv); push(m, &t, 1);
                }
                break;
            case DataType::Int16:
                if (hasSigned && sv >= INT16_MIN && sv <= INT16_MAX) {
                    int16_t t = static_cast<int16_t>(sv); push(m, &t, 2);
                }
                break;
            case DataType::Int32:
                if (hasSigned && sv >= INT32_MIN && sv <= INT32_MAX) {
                    int32_t t = static_cast<int32_t>(sv); push(m, &t, 4);
                }
                break;
            case DataType::Int64:
                if (hasSigned) { int64_t t = sv; push(m, &t, 8); }
                break;
            case DataType::UInt16:
                if (hasUnsigned && uv <= UINT16_MAX) {
                    uint16_t t = static_cast<uint16_t>(uv); push(m, &t, 2);
                }
                break;
            case DataType::UInt32:
                if (hasUnsigned && uv <= UINT32_MAX) {
                    uint32_t t = static_cast<uint32_t>(uv); push(m, &t, 4);
                }
                break;
            case DataType::UInt64:
                if (hasUnsigned) { uint64_t t = uv; push(m, &t, 8); }
                break;
            case DataType::Float:
                if (hasFloat) { float t = static_cast<float>(dv); push(m, &t, 4); }
                break;
            case DataType::Double:
                if (hasFloat) { double t = dv; push(m, &t, 8); }
                break;
            default:
                break;
        }
    }

    return !out.entries.empty();
}

// --- Compare predicate ---

namespace {

// Read a value of the given DataType from `bytes`. We return the
// "natural" wide type (int64_t for signed, uint64_t for unsigned,
// double for floats) so the predicate logic only needs three branches.
template <typename Out>
inline Out LoadTyped(DataType dt, const uint8_t* bytes);

template <>
inline int64_t LoadTyped<int64_t>(DataType dt, const uint8_t* bytes) {
    int64_t v = 0;
    switch (dt) {
        case DataType::Int8:  v = static_cast<int64_t>(*reinterpret_cast<const int8_t*>(bytes));  break;
        case DataType::Int16: v = static_cast<int64_t>(*reinterpret_cast<const int16_t*>(bytes)); break;
        case DataType::Int32: v = static_cast<int64_t>(*reinterpret_cast<const int32_t*>(bytes)); break;
        case DataType::Int64: v = *reinterpret_cast<const int64_t*>(bytes); break;
        default: break;
    }
    return v;
}

template <>
inline uint64_t LoadTyped<uint64_t>(DataType dt, const uint8_t* bytes) {
    uint64_t v = 0;
    switch (dt) {
        case DataType::UInt8:  v = static_cast<uint64_t>(*bytes); break;
        case DataType::UInt16: v = static_cast<uint64_t>(*reinterpret_cast<const uint16_t*>(bytes)); break;
        case DataType::UInt32: v = static_cast<uint64_t>(*reinterpret_cast<const uint32_t*>(bytes)); break;
        case DataType::UInt64: v = *reinterpret_cast<const uint64_t*>(bytes); break;
        case DataType::Bool:   v = (*bytes != 0) ? 1u : 0u; break;
        default: break;
    }
    return v;
}

template <>
inline double LoadTyped<double>(DataType dt, const uint8_t* bytes) {
    double v = 0.0;
    switch (dt) {
        case DataType::Float:  v = static_cast<double>(*reinterpret_cast<const float*>(bytes)); break;
        case DataType::Double: v = *reinterpret_cast<const double*>(bytes); break;
        default: break;
    }
    return v;
}

bool IsFloatType(DataType dt) { return dt == DataType::Float || dt == DataType::Double; }
bool IsSignedIntType(DataType dt) {
    return dt == DataType::Int8 || dt == DataType::Int16
        || dt == DataType::Int32 || dt == DataType::Int64;
}
bool IsUnsignedIntType(DataType dt) {
    return dt == DataType::UInt8 || dt == DataType::UInt16
        || dt == DataType::UInt32 || dt == DataType::UInt64
        || dt == DataType::Bool;
}

template <typename T>
bool ApplyOrdered(ScanType st, T cur, T a, T b) {
    switch (st) {
        case ScanType::Exact:     return cur == a;
        case ScanType::Bigger:    return cur >  a;
        case ScanType::Smaller:   return cur <  a;
        // Normalize reversed bounds (parity with C# SnapshotNumeric.BetweenMatch).
        case ScanType::Between:   return (a <= b) ? (cur >= a && cur <= b)
                                                  : (cur >= b && cur <= a);
        case ScanType::Changed:   return cur != a;
        case ScanType::Unchanged: return cur == a;
        case ScanType::Increased: return cur >  a;
        case ScanType::Decreased: return cur <  a;
    }
    return false;
}

// Displayed-integer float predicate (build 1672 — replaces the old ± tolerance
// ApplyOrderedTol). Semantics documented on ComparePredicate in Radar.h. Let
// r(x) = ReduceRounded(x, mode). For TARGETED predicates a WHOLE target lets the
// player search by the displayed integer (reduce the field value); a FRACTIONAL
// target keeps exact-literal compare (the user is being precise — positions /
// non-whole GAS values). PREV-VALUE predicates always compare reduced cur vs
// reduced prev ("did the displayed value change/increase/decrease"). Shared by
// the scalar ComparePredicate and per-axis by CompareVectorPredicate.
inline bool IsWhole(double x) { return x == std::floor(x); }

bool CompareFloatScalar(ScanType st, double cur, double a, double b, RoundMode mode) {
    const double rc = ReduceRounded(cur, mode);
    switch (st) {
        case ScanType::Exact:     return IsWhole(a) ? (rc == a) : (cur == a);
        case ScanType::Bigger:    return IsWhole(a) ? (rc >  a) : (cur >  a);
        case ScanType::Smaller:   return IsWhole(a) ? (rc <  a) : (cur <  a);
        case ScanType::Between: {
            // Normalize reversed bounds (lo > hi) so the range matches regardless of
            // entry order — parity with C# SnapshotNumeric.BetweenMatch (Min/Max).
            const double lo = a < b ? a : b, hi = a < b ? b : a;
            return (IsWhole(lo) && IsWhole(hi)) ? (rc >= lo && rc <= hi)
                                                : (cur >= lo && cur <= hi);
        }
        case ScanType::Changed:   return rc != ReduceRounded(a, mode);
        case ScanType::Unchanged: return rc == ReduceRounded(a, mode);
        case ScanType::Increased: return rc >  ReduceRounded(a, mode);
        case ScanType::Decreased: return rc <  ReduceRounded(a, mode);
        default:                  return false;
    }
}

}  // namespace

bool ComparePredicate(DataType dt, ScanType st,
                      const uint8_t* rawBytes,
                      const uint8_t* targetBytes,
                      const uint8_t* target2Bytes,
                      RoundMode      roundMode) {
    if (!rawBytes || !targetBytes) return false;
    if (st == ScanType::Between && !target2Bytes) return false;
    // Substring predicates have no meaning for numeric types.
    if (IsSubstringScanType(st)) return false;

    if (IsFloatType(dt)) {
        double cur = LoadTyped<double>(dt, rawBytes);
        double a   = LoadTyped<double>(dt, targetBytes);
        double b   = target2Bytes ? LoadTyped<double>(dt, target2Bytes) : 0.0;
        // The rounding mode is only meaningful for Float/Double: integral types
        // get strict comparison (an integer reduces to itself), with any
        // fractional-target coercion already done at parse time.
        return CompareFloatScalar(st, cur, a, b, roundMode);
    }
    if (IsSignedIntType(dt)) {
        int64_t cur = LoadTyped<int64_t>(dt, rawBytes);
        int64_t a   = LoadTyped<int64_t>(dt, targetBytes);
        int64_t b   = target2Bytes ? LoadTyped<int64_t>(dt, target2Bytes) : 0;
        return ApplyOrdered<int64_t>(st, cur, a, b);
    }
    if (IsUnsignedIntType(dt)) {
        uint64_t cur = LoadTyped<uint64_t>(dt, rawBytes);
        uint64_t a   = LoadTyped<uint64_t>(dt, targetBytes);
        uint64_t b   = target2Bytes ? LoadTyped<uint64_t>(dt, target2Bytes) : 0;
        return ApplyOrdered<uint64_t>(st, cur, a, b);
    }
    return false;
}

// --- String predicate ---

namespace {

// ASCII-aware case folder. We don't pull in <locale> / ICU; for the
// cheat use cases that hit string scans (English item names, save
// keys, dialogue tags) a byte-level tolower over [A-Z] is sufficient.
// Non-ASCII bytes (UTF-8 multibyte sequences) compare bitwise — two
// strings that differ only in non-ASCII case (e.g. "Ä" vs "ä") will
// not match in case-insensitive mode. Documented in Radar.h.
inline char FoldAscii(char c) {
    return (c >= 'A' && c <= 'Z') ? static_cast<char>(c + ('a' - 'A')) : c;
}

bool EqualsFolded(const std::string& a, const std::string& b, bool caseSensitive) {
    if (a.size() != b.size()) return false;
    if (caseSensitive) return a == b;
    for (size_t i = 0; i < a.size(); ++i) {
        if (FoldAscii(a[i]) != FoldAscii(b[i])) return false;
    }
    return true;
}

bool StartsWithFolded(const std::string& s, const std::string& prefix, bool caseSensitive) {
    if (prefix.size() > s.size()) return false;
    if (caseSensitive) return std::memcmp(s.data(), prefix.data(), prefix.size()) == 0;
    for (size_t i = 0; i < prefix.size(); ++i) {
        if (FoldAscii(s[i]) != FoldAscii(prefix[i])) return false;
    }
    return true;
}

bool EndsWithFolded(const std::string& s, const std::string& suffix, bool caseSensitive) {
    if (suffix.size() > s.size()) return false;
    size_t off = s.size() - suffix.size();
    if (caseSensitive) return std::memcmp(s.data() + off, suffix.data(), suffix.size()) == 0;
    for (size_t i = 0; i < suffix.size(); ++i) {
        if (FoldAscii(s[off + i]) != FoldAscii(suffix[i])) return false;
    }
    return true;
}

bool ContainsFolded(const std::string& s, const std::string& needle, bool caseSensitive) {
    if (needle.empty()) return true;
    if (needle.size() > s.size()) return false;
    if (caseSensitive) return s.find(needle) != std::string::npos;
    // Inline ASCII-fold substring search — O(N*M) but bounded by short
    // user-entered needles + ~50K candidates. No allocations.
    const size_t lastStart = s.size() - needle.size();
    for (size_t i = 0; i <= lastStart; ++i) {
        bool match = true;
        for (size_t j = 0; j < needle.size(); ++j) {
            if (FoldAscii(s[i + j]) != FoldAscii(needle[j])) { match = false; break; }
        }
        if (match) return true;
    }
    return false;
}

}  // namespace

bool CompareStringPredicate(ScanType           st,
                            const std::string& cur,
                            const std::string& target,
                            bool               caseSensitive) {
    switch (st) {
        case ScanType::Exact:      return EqualsFolded(cur, target, caseSensitive);
        case ScanType::Contains:   return ContainsFolded(cur, target, caseSensitive);
        case ScanType::StartsWith: return StartsWithFolded(cur, target, caseSensitive);
        case ScanType::EndsWith:   return EndsWithFolded(cur, target, caseSensitive);
        case ScanType::Changed:    return !EqualsFolded(cur, target, caseSensitive);
        case ScanType::Unchanged:  return  EqualsFolded(cur, target, caseSensitive);
        // Numeric-ordering predicates are nonsensical for strings.
        case ScanType::Bigger:
        case ScanType::Smaller:
        case ScanType::Between:
        case ScanType::Increased:
        case ScanType::Decreased:
            return false;
    }
    return false;
}

// --- Vector predicate ---

bool CompareVectorPredicate(ScanType      st,
                            const double* cur,
                            const double* target,
                            const double* target2,
                            RoundMode     roundMode) {
    if (!cur || !target) return false;
    if (st == ScanType::Between && !target2) return false;
    if (IsSubstringScanType(st)) return false;

    const double kZero[3] = { 0.0, 0.0, 0.0 };
    const double* b = target2 ? target2 : kZero;

    // Each axis runs the SAME scalar displayed-integer compare; combine per the
    // scan type (ALL axes for match/range/unchanged, ANY axis for change/dir).
    auto axisOk = [&](int i) {
        return CompareFloatScalar(st, cur[i], target[i], b[i], roundMode);
    };
    switch (st) {
        case ScanType::Exact:
        case ScanType::Bigger:
        case ScanType::Smaller:
        case ScanType::Between:
        case ScanType::Unchanged:
            return axisOk(0) && axisOk(1) && axisOk(2);
        case ScanType::Changed:
        case ScanType::Increased:
        case ScanType::Decreased:
            return axisOk(0) || axisOk(1) || axisOk(2);
        case ScanType::Contains:
        case ScanType::StartsWith:
        case ScanType::EndsWith:
            return false;
    }
    return false;
}

// --- Display helpers ---

std::string FieldDisplayName(const FieldDescriptor& desc, int32_t elementIndex) {
    // Struct-array-inner descriptors carry a "[]" placeholder marking where the
    // element index belongs (e.g. "SaveSlotList[].GP" -> "SaveSlotList[3].GP"),
    // so the index lands after the ARRAY name, not at the very end. UE property
    // names never contain "[]", so the sentinel is unambiguous.
    auto ph = desc.fieldName.find("[]");
    if (ph != std::string::npos) {
        std::string s = desc.fieldName;
        if (elementIndex < 0) { s.erase(ph, 2); return s; }   // defensive: no index
        s.replace(ph, 2, "[" + std::to_string(elementIndex) + "]");
        return s;
    }
    if (elementIndex < 0) return desc.fieldName;
    return desc.fieldName + "[" + std::to_string(elementIndex) + "]";
}

int32_t OptionalFlagOffset(int32_t optionalSize, int32_t innerSize) {
    // Need a known inner size and room past it for the trailing bool. When
    // optionalSize == innerSize the optional is intrusive (the value's own bit
    // pattern marks unset — pointer-shaped Ts), so there's no flag to gate on.
    if (innerSize <= 0 || optionalSize <= innerSize) return -1;
    return innerSize;
}

// --- V3-C: value rendering / filter / sort over a candidate pool ---

namespace {

// Render a float/double WITHOUT scientific notation, the way the Live Walker
// drilldown (Ubel::InterpretValue / FmtPreviewNum) already does. The historical
// `oss << v` used the default ostringstream precision (6 significant figures,
// %g format), which flips to the unreadable exponent form past ~6 digits — e.g.
// a garbage FloatProperty showed "5.73356e+17" in the Value column. This keeps
// the SAME 6 significant figures for in-range values (so normal hits like
// "1.39391" / "100" / "81.0702" are byte-identical to before) but adapts the
// decimal places to the magnitude so the output is always plain fixed-point.
// Trailing zeros are then trimmed. Non-finite values keep their plain spelling.
std::string FormatNoSci(double v, int sigFigs) {
    if (!std::isfinite(v)) {
        if (std::isnan(v)) return "nan";
        return v < 0.0 ? "-inf" : "inf";
    }
    if (v == 0.0) return "0";
    double a = std::fabs(v);
    int mag = static_cast<int>(std::floor(std::log10(a)));  // exponent of the value
    int decimals = sigFigs - 1 - mag;                       // places to keep N sig figs
    if (decimals < 0)  decimals = 0;
    if (decimals > 17) decimals = 17;   // a double carries ~15-17 sig digits anyway
    char buf[340];                      // max double at decimals=0 -> ~309 integer digits
    std::snprintf(buf, sizeof(buf), "%.*f", decimals, v);
    std::string s(buf);
    auto dot = s.find('.');
    if (dot != std::string::npos) {
        size_t last = s.find_last_not_of('0');
        s.erase((last == dot) ? dot : last + 1);   // drop the dot if the fraction is all zeros
    }
    return s;
}

// Numeric scalar -> string. Integers via the stream; float/double via the
// no-scientific-notation formatter above (matches the Live Walker display).
std::string FormatScalarBytes(DataType dt, const uint8_t* b) {
    std::ostringstream oss;
    switch (dt) {
        case DataType::Int8:   { int8_t  v; std::memcpy(&v, b, 1); oss << static_cast<int>(v); break; }
        case DataType::Int16:  { int16_t v; std::memcpy(&v, b, 2); oss << v; break; }
        case DataType::Int32:  { int32_t v; std::memcpy(&v, b, 4); oss << v; break; }
        case DataType::Int64:  { int64_t v; std::memcpy(&v, b, 8); oss << v; break; }
        case DataType::UInt8:  { oss << static_cast<unsigned int>(b[0]); break; }
        case DataType::UInt16: { uint16_t v; std::memcpy(&v, b, 2); oss << v; break; }
        case DataType::UInt32: { uint32_t v; std::memcpy(&v, b, 4); oss << v; break; }
        case DataType::UInt64: { uint64_t v; std::memcpy(&v, b, 8); oss << v; break; }
        case DataType::Float:  { float  v; std::memcpy(&v, b, 4); return FormatNoSci(v, 6); }
        case DataType::Double: { double v; std::memcpy(&v, b, 8); return FormatNoSci(v, 6); }
        case DataType::Bool:   { oss << (b[0] ? "true" : "false"); break; }
        default: break;  // strings/vectors/multi handled by FormatCandidateValue
    }
    return oss.str();
}

// Render a candidate's CANONICAL vector snapshot (3 doubles — see
// Candidate::prevValue). Width-free by construction: the source width was
// resolved and decoded away at read time, so this renderer cannot disagree
// with the predicate about how wide the field was.
std::string FormatVectorCanonical(const uint8_t* b) {
    double v[3] = { 0.0, 0.0, 0.0 };
    std::memcpy(v, b, sizeof(v));
    // Per-component fixed-point (no scientific notation), matching the scalar path.
    return FormatNoSci(v[0], 6) + ", " + FormatNoSci(v[1], 6) + ", " + FormatNoSci(v[2], 6);
}

std::string ToLower(std::string s) {
    for (char& ch : s) ch = static_cast<char>(std::tolower(static_cast<unsigned char>(ch)));
    return s;
}

}  // namespace

std::string FormatCandidateValue(const Candidate& c, DataType dt,
                                 const FieldDescriptor& desc) {
    if (IsStringDataType(dt)) return c.prevStr;
    if (IsVectorDataType(dt)) return FormatVectorCanonical(c.prevValue);
    if (IsMultiNumericDataType(dt)) {
        DataType m;
        if (TryDataTypeFromPropertyTypeName(desc.fieldType, m))
            return FormatScalarBytes(m, c.prevValue);
        return "";
    }
    return FormatScalarBytes(dt, c.prevValue);
}

double DecodeNumericToDouble(DataType dt, const uint8_t* b) {
    switch (dt) {
        case DataType::Int8:   { int8_t  v; std::memcpy(&v, b, 1); return static_cast<double>(v); }
        case DataType::Int16:  { int16_t v; std::memcpy(&v, b, 2); return static_cast<double>(v); }
        case DataType::Int32:  { int32_t v; std::memcpy(&v, b, 4); return static_cast<double>(v); }
        case DataType::Int64:  { int64_t v; std::memcpy(&v, b, 8); return static_cast<double>(v); }
        case DataType::UInt8:  return static_cast<double>(b[0]);
        case DataType::UInt16: { uint16_t v; std::memcpy(&v, b, 2); return static_cast<double>(v); }
        case DataType::UInt32: { uint32_t v; std::memcpy(&v, b, 4); return static_cast<double>(v); }
        case DataType::UInt64: { uint64_t v; std::memcpy(&v, b, 8); return static_cast<double>(v); }
        case DataType::Float:  { float  v; std::memcpy(&v, b, 4); return static_cast<double>(v); }
        case DataType::Double: { double v; std::memcpy(&v, b, 8); return v; }
        case DataType::Bool:   return b[0] ? 1.0 : 0.0;
        default: return 0.0;
    }
}

bool TryParseSortKey(const std::string& s, SortKey& out) {
    if (s.empty() || s == "scan")     { out = SortKey::ScanOrder;     return true; }
    if (s == "addr" || s == "address"){ out = SortKey::Address;       return true; }
    if (s == "value")                 { out = SortKey::Value;         return true; }
    if (s == "class")                 { out = SortKey::ClassName;     return true; }
    if (s == "field")                 { out = SortKey::FieldName;     return true; }
    if (s == "instance")              { out = SortKey::InstanceName;  return true; }
    if (s == "type")                  { out = SortKey::FieldType;     return true; }
    if (s == "offset")                { out = SortKey::Offset;        return true; }
    if (s == "index")                 { out = SortKey::InstanceIndex; return true; }
    return false;
}

namespace {

// Case-insensitive substring test WITHOUT allocating a lowercased copy of the
// haystack (`needleLower` must already be lowercased). The filter runs this
// over every displayed column of every candidate on each filter change, so at
// the raised cap ceiling (1M, V2) avoiding ~6 string allocations per candidate
// matters — it roughly halved the filter pass in the scale bench. Naive O(n·m)
// is fine: needles are short and columns are short.
bool ContainsCI(const char* hay, size_t hayLen, const std::string& needleLower) {
    const size_t n = needleLower.size();
    if (n == 0) return true;
    if (n > hayLen) return false;
    for (size_t i = 0; i + n <= hayLen; ++i) {
        size_t j = 0;
        for (; j < n; ++j) {
            char ch = static_cast<char>(std::tolower(static_cast<unsigned char>(hay[i + j])));
            if (ch != needleLower[j]) break;
        }
        if (j == n) return true;
    }
    return false;
}
inline bool ContainsCI(const std::string& hay, const std::string& needleLower) {
    return ContainsCI(hay.data(), hay.size(), needleLower);
}

// Does any displayed column of this candidate contain `needleLower`?
// Mirrors the columns the client-side keyword filter used to match
// (Class.Field / DefiningClass / Type / Value / Offset / Address / Instance).
bool CandidateMatchesFilter(const Candidate& c, DataType dt,
                            const FieldDescriptor& d, const InstanceRecord& inst,
                            const std::string& needleLower) {
    if (needleLower.empty()) return true;
    if (ContainsCI(d.className, needleLower)) return true;
    if (ContainsCI(d.definingClassName, needleLower)) return true;
    // FieldDisplayName allocates ("name" / "name[i]"); for a direct field
    // (the common case) it's just desc.fieldName — test that in place. Only
    // container-element candidates pay the "name[i]" construction.
    if (c.elementIndex < 0) {
        if (ContainsCI(d.fieldName, needleLower)) return true;
    } else if (ContainsCI(FieldDisplayName(d, c.elementIndex), needleLower)) {
        return true;
    }
    if (ContainsCI(d.fieldType, needleLower)) return true;
    if (ContainsCI(inst.instanceName, needleLower)) return true;
    if (ContainsCI(FormatCandidateValue(c, dt, d), needleLower)) return true;
    // Lowercase hex of offset + address (needle is already lowercased, so a
    // user typing either case of an offset/address pasted from the grid hits).
    char buf[32];
    int n = std::snprintf(buf, sizeof buf, "0x%x", static_cast<unsigned>(d.fieldOffset));
    if (n > 0 && ContainsCI(buf, static_cast<size_t>(n), needleLower)) return true;
    n = std::snprintf(buf, sizeof buf, "0x%llx", static_cast<unsigned long long>(c.addr));
    if (n > 0 && ContainsCI(buf, static_cast<size_t>(n), needleLower)) return true;
    return false;
}

}  // namespace

std::vector<uint32_t> BuildOrderedView(
    const std::vector<Candidate>&       candidates,
    const std::vector<FieldDescriptor>& descriptors,
    const std::vector<InstanceRecord>&  instances,
    DataType dt, const std::string& filter,
    SortKey sortKey, bool sortDesc,
    const std::unordered_set<std::string>& excludeClasses) {

    const std::string needle = ToLower(filter);
    const bool hasExclude = !excludeClasses.empty();

    std::vector<uint32_t> idx;
    idx.reserve(candidates.size());
    for (uint32_t i = 0; i < candidates.size(); ++i) {
        const Candidate&       c = candidates[i];
        const FieldDescriptor& d = descriptors[c.descriptorIdx];
        // Class-noise exclude (case-sensitive exact class match — the picker
        // sends exact class names from the histogram). Cheap; before the filter.
        if (hasExclude && excludeClasses.count(d.className)) continue;
        const InstanceRecord&  n = instances[c.instanceIdx];
        if (CandidateMatchesFilter(c, dt, d, n, needle)) idx.push_back(i);
    }

    if (sortKey == SortKey::ScanOrder) {
        if (sortDesc) std::reverse(idx.begin(), idx.end());
        return idx;
    }

    auto less = [&](uint32_t a, uint32_t b) -> bool {
        const Candidate&       ca = candidates[a]; const Candidate&       cb = candidates[b];
        const FieldDescriptor& da = descriptors[ca.descriptorIdx];
        const FieldDescriptor& db = descriptors[cb.descriptorIdx];
        const InstanceRecord&  ia = instances[ca.instanceIdx];
        const InstanceRecord&  ib = instances[cb.instanceIdx];
        switch (sortKey) {
            case SortKey::Address:       return ca.addr < cb.addr;
            case SortKey::Offset:        return da.fieldOffset < db.fieldOffset;
            case SortKey::InstanceIndex: return ia.instanceIndex < ib.instanceIndex;
            case SortKey::ClassName:     return da.className < db.className;
            case SortKey::InstanceName:  return ia.instanceName < ib.instanceName;
            case SortKey::FieldType:     return da.fieldType < db.fieldType;
            case SortKey::FieldName:
                return FieldDisplayName(da, ca.elementIndex)
                     < FieldDisplayName(db, cb.elementIndex);
            case SortKey::Value: {
                if (IsStringDataType(dt)) return ca.prevStr < cb.prevStr;
                if (IsVectorDataType(dt))
                    return FormatCandidateValue(ca, dt, da)
                         < FormatCandidateValue(cb, dt, db);
                DataType ma = dt, mb = dt;
                if (IsMultiNumericDataType(dt)) {
                    TryDataTypeFromPropertyTypeName(da.fieldType, ma);
                    TryDataTypeFromPropertyTypeName(db.fieldType, mb);
                }
                return DecodeNumericToDouble(ma, ca.prevValue)
                     < DecodeNumericToDouble(mb, cb.prevValue);
            }
            default: return false;
        }
    };
    // stable_sort keeps scan order among equal keys.
    std::stable_sort(idx.begin(), idx.end(), [&](uint32_t a, uint32_t b) {
        return sortDesc ? less(b, a) : less(a, b);
    });
    return idx;
}

// Sort a class-count map into a (count desc, name asc) vector. Shared by the
// single + group histograms.
static std::vector<std::pair<std::string, int>> SortClassHistogram(
    std::unordered_map<std::string, int>& counts) {
    std::vector<std::pair<std::string, int>> v(counts.begin(), counts.end());
    std::sort(v.begin(), v.end(),
        [](const std::pair<std::string, int>& a, const std::pair<std::string, int>& b) {
            if (a.second != b.second) return a.second > b.second;
            return a.first < b.first;
        });
    return v;
}

std::vector<std::pair<std::string, int>> BuildClassHistogram(
    const std::vector<Candidate>&       candidates,
    const std::vector<FieldDescriptor>& descriptors) {
    std::unordered_map<std::string, int> counts;
    for (const auto& c : candidates) {
        const std::string& cn = descriptors[c.descriptorIdx].className;
        if (!cn.empty()) ++counts[cn];
    }
    return SortClassHistogram(counts);
}

// --- SessionManager ---

SessionManager& SessionManager::Instance() {
    // Heap-allocated, intentionally leaked singleton. The DLL may hold
    // tens of thousands of candidates per session × multiple sessions
    // × ~200 bytes of string metadata each — easily multi-MB of small
    // allocations. Running the destructor chain over those during
    // process exit can cause perceptible hangs. The Windows process
    // heap is reclaimed wholesale at termination, so the leak is
    // harmless — same reasoning as discrete's ValueScanSession.
    static SessionManager* inst = new SessionManager();
    return *inst;
}

uint64_t SessionManager::Begin(DataType dt,
                               std::vector<Candidate>       candidates,
                               std::vector<FieldDescriptor> descriptors,
                               std::vector<InstanceRecord>  instances) {
    ExpireOldSessions();

    std::lock_guard<std::mutex> lk(mu_);
    uint64_t id = nextId_++;
    auto sess = std::make_unique<Session>();
    sess->id          = id;
    sess->dt          = dt;
    sess->candidates  = std::move(candidates);
    sess->descriptors = std::move(descriptors);
    sess->instances   = std::move(instances);
    sess->lastUse     = std::chrono::steady_clock::now();
    sessions_.emplace(id, std::move(sess));
    return id;
}

bool SessionManager::End(uint64_t sessionId) {
    std::lock_guard<std::mutex> lk(mu_);
    return sessions_.erase(sessionId) > 0;
}

void SessionManager::ExpireOldSessions() {
    auto now = std::chrono::steady_clock::now();
    std::lock_guard<std::mutex> lk(mu_);
    for (auto it = sessions_.begin(); it != sessions_.end(); ) {
        if (now - it->second->lastUse > kExpirySeconds) {
            it = sessions_.erase(it);
        } else {
            ++it;
        }
    }
}

void SessionManager::DropAll() {
    std::lock_guard<std::mutex> lk(mu_);
    sessions_.clear();
}

SessionManager::Stats SessionManager::GetStats() {
    Stats s;
    std::lock_guard<std::mutex> lk(mu_);
    s.sessionCount = sessions_.size();
    s.nextId       = nextId_;
    for (const auto& kv : sessions_) {
        s.totalCandidates += kv.second->candidates.size();
    }
    return s;
}

// --- Group scan: ordered view + GroupSessionManager (build 1276) ---

// Candidate's OBJECT-level class = first non-empty slot's first match's
// descriptor className (mirrors BuildGroupOrderedView's classOf / sort key).
// Returns by const ref so the exclude-skip + histogram hot paths don't allocate
// a string per candidate.
static const std::string& GroupCandidateClass(const GroupCandidate& gc,
                                              const std::vector<FieldDescriptor>& descriptors) {
    static const std::string kEmpty;
    for (const auto& sl : gc.slotMatches)
        if (!sl.empty()) return descriptors[sl[0].descriptorIdx].className;
    return kEmpty;
}

bool GroupTextContainsCI(const std::string& hay, const std::string& needleLower) {
    return ContainsCI(hay, needleLower);
}

std::string GroupSlotValueString(const GroupSlotMatch& sm, const SlotSpec& spec,
                                 const std::vector<FieldDescriptor>& descriptors) {
    if (sm.descriptorIdx >= descriptors.size()) return {};
    const FieldDescriptor& d = descriptors[sm.descriptorIdx];
    Candidate tmp;
    // Bound by the SOURCE buffer, not the destination: a group leaf is numeric
    // (<= 8 bytes) while a Candidate's buffer must hold a canonical vector
    // triple, so the two arrays are no longer the same size. Copying
    // sizeof(tmp.prevValue) read 8 bytes past the end of `sm`.
    static_assert(sizeof(GroupSlotMatch::prevValue) <= sizeof(Candidate::prevValue),
                  "a group leaf must fit in a candidate's value buffer");
    std::memcpy(tmp.prevValue, sm.prevValue, sizeof(sm.prevValue));
    tmp.descriptorIdx = sm.descriptorIdx;
    tmp.elementIndex  = sm.elementIndex;
    return FormatCandidateValue(tmp, spec.dt, d);
}

std::string ToLowerAscii(const std::string& in) { return ToLower(in); }

std::vector<std::string> SplitFilterTerms(const std::string& filter) {
    std::vector<std::string> terms;
    std::string cur;
    for (char c : filter) {
        if (c == ' ' || c == '\t' || c == '\n' || c == '\r') {
            if (!cur.empty()) { terms.push_back(ToLower(cur)); cur.clear(); }
        } else {
            cur.push_back(c);
        }
    }
    if (!cur.empty()) terms.push_back(ToLower(cur));
    return terms;
}

bool IsZeroValueText(const std::string& s) {
    bool sawDigit = false;
    for (char c : s) {
        if (c == ' ' || c == '\t') continue;
        if (c == '0') { sawDigit = true; continue; }
        // Punctuation a zero can legitimately carry: sign, decimal point, and the
        // separators a vector value uses ("0, 0, 0").
        if (c == '.' || c == ',' || c == '+' || c == '-') continue;
        return false;   // any other character (incl. any non-zero digit) => not zero
    }
    return sawDigit;    // "" / "-" / "," are not a zero, they are not a number
}

std::vector<size_t> OrderGroupSlotLeaves(
    const std::vector<GroupSlotMatch>&  matches,
    const std::vector<FieldDescriptor>& descriptors) {

    std::vector<size_t> order(matches.size());
    for (size_t i = 0; i < matches.size(); ++i) order[i] = i;

    auto ownTier = [&](size_t i) -> int {
        if (matches[i].descriptorIdx >= descriptors.size()) return 1;
        const FieldDescriptor& d = descriptors[matches[i].descriptorIdx];
        // Declared by the object's own class => tier 0. A leaf nested in a struct
        // reports the STRUCT as its defining class, so it lands in tier 1 beside
        // the inherited fields — correct: it is not a field of this class either.
        return (!d.definingClassName.empty() && d.definingClassName == d.className) ? 0 : 1;
    };
    std::stable_sort(order.begin(), order.end(),
                     [&](size_t a, size_t b) { return ownTier(a) < ownTier(b); });
    return order;
}

std::vector<size_t> PickGroupWitnessAssignment(
    const std::vector<std::vector<GroupSlotMatch>>& slotMatches,
    const std::vector<SlotSpec>&                   slots,
    const std::vector<FieldDescriptor>&            descriptors,
    const std::vector<std::string>&                terms) {

    std::vector<size_t> picks(slotMatches.size(), 0);
    // A term consumed by an earlier slot. Without this, `tickcount frozenint`
    // would let both slots claim whichever leaf matches the first term they see,
    // and the second named field would never reach the row — which is the entire
    // reason the filter had to learn AND.
    std::vector<bool> termUsed(terms.size(), false);
    // Leaf addresses already claimed by an earlier slot, and the defining
    // class/struct of each — so a later slot can prefer a SIBLING.
    std::vector<uintptr_t>   shownLeaves;
    std::vector<std::string> shownDefiningClasses;
    shownLeaves.reserve(slotMatches.size());
    shownDefiningClasses.reserve(slotMatches.size());

    for (size_t s = 0; s < slotMatches.size(); ++s) {
        const std::vector<GroupSlotMatch>& matches = slotMatches[s];
        if (matches.empty()) continue;   // claims nothing; picks[s] stays 0

        auto isFree = [&](size_t k) {
            for (uintptr_t used : shownLeaves)
                if (used == matches[k].leafAddr) return false;
            return true;
        };
        // A leaf whose descriptorIdx is out of range can still be CLAIMED (pass
        // 3) but must never be text-matched — indexing `descriptors` with it is
        // the one way this pure function could read out of bounds.
        auto descOf = [&](size_t k) -> const FieldDescriptor* {
            return matches[k].descriptorIdx < descriptors.size()
                       ? &descriptors[matches[k].descriptorIdx] : nullptr;
        };

        // A zero is almost never the value someone is hunting: engine bookkeeping
        // fields sit at 0 by default, so a row reading `TickInterval=0,
        // InitialLifeSpan=0` is the least informative pair a candidate can offer
        // while its real fields matched too. Every pass therefore tries non-zero
        // leaves first and only then falls back — a tie-break INSIDE each rule, not
        // a new rule that could outrank the filter.
        auto isZeroLeaf = [&](size_t k) {
            return s < slots.size() &&
                   IsZeroValueText(GroupSlotValueString(matches[k], slots[s], descriptors));
        };

        size_t pick = 0;
        bool   picked = false;

        // Pass 1: a free leaf matching one of the filter terms. Two sub-passes —
        // an UNUSED term first, so each named field lands in its own slot.
        size_t claimedTerm = terms.size();
        if (!terms.empty() && s < slots.size()) {
            auto leafMatchesTerm = [&](size_t k, const std::string& term) {
                const FieldDescriptor* d = descOf(k);
                if (!d) return false;
                return GroupTextContainsCI(d->fieldName, term) ||
                       GroupTextContainsCI(d->className, term) ||
                       GroupTextContainsCI(d->definingClassName, term) ||
                       GroupTextContainsCI(
                           GroupSlotValueString(matches[k], slots[s], descriptors), term);
            };
            for (int wantUnused = 1; wantUnused >= 0 && !picked; --wantUnused) {
                for (int skipZero = 1; skipZero >= 0 && !picked; --skipZero) {
                    for (size_t k = 0; k < matches.size() && !picked; ++k) {
                        if (!isFree(k)) continue;
                        if (skipZero && isZeroLeaf(k)) continue;
                        for (size_t t = 0; t < terms.size(); ++t) {
                            if (wantUnused && termUsed[t]) continue;
                            if (!leafMatchesTerm(k, terms[t])) continue;
                            pick = k; picked = true; claimedTerm = t;
                            break;
                        }
                    }
                }
            }
        }
        if (claimedTerm < terms.size()) termUsed[claimedTerm] = true;
        // Pass 2: a free leaf defined by the same class/struct as one already shown.
        if (!picked && !shownDefiningClasses.empty()) {
            for (int skipZero = 1; skipZero >= 0 && !picked; --skipZero) {
                for (size_t k = 0; k < matches.size() && !picked; ++k) {
                    if (!isFree(k)) continue;
                    if (skipZero && isZeroLeaf(k)) continue;
                    const FieldDescriptor* d = descOf(k);
                    if (!d || d->definingClassName.empty()) continue;
                    for (const std::string& shown : shownDefiningClasses)
                        if (d->definingClassName == shown) { pick = k; picked = true; break; }
                }
            }
        }
        // Pass 3: any free leaf. (None free => pick stays 0, a duplicate.)
        for (int skipZero = 1; skipZero >= 0 && !picked; --skipZero)
            for (size_t k = 0; k < matches.size() && !picked; ++k)
                if (isFree(k) && !(skipZero && isZeroLeaf(k))) { pick = k; picked = true; }

        picks[s] = pick;
        shownLeaves.push_back(matches[pick].leafAddr);
        if (const FieldDescriptor* d = descOf(pick))
            shownDefiningClasses.push_back(d->definingClassName);
    }
    return picks;
}

std::vector<uint32_t> BuildGroupOrderedView(
    const std::vector<GroupCandidate>&  candidates,
    const std::vector<SlotSpec>&        slots,
    const std::vector<FieldDescriptor>& descriptors,
    const std::vector<InstanceRecord>&  instances,
    const std::string& filter, SortKey sortKey, bool sortDesc,
    const std::unordered_set<std::string>& excludeClasses) {

    // space = AND (CLAUDE.md's keyword-box rule): every term must match SOMETHING
    // on the candidate, but each may match a DIFFERENT field. Naming two fields is
    // the only way to request one specific pairing out of the many a candidate
    // satisfies — a `Changed`+`Unchanged` scan can hold 2 x 36 = 72 of them and
    // nothing in the data says which one you meant.
    const std::vector<std::string> terms = SplitFilterTerms(filter);
    const bool hasExclude = !excludeClasses.empty();

    // Render a slot match's value exactly as the wire does (resolves the field's
    // own width for a NumericNoByte/NumericAll slot via descriptor.fieldType).
    auto slotValue = [&](const GroupSlotMatch& sm, const SlotSpec& spec) -> std::string {
        const FieldDescriptor& d = descriptors[sm.descriptorIdx];
        Candidate tmp;
        // Source-bounded — see GroupSlotValueString for why.
        std::memcpy(tmp.prevValue, sm.prevValue, sizeof(sm.prevValue));
        tmp.descriptorIdx = sm.descriptorIdx;
        tmp.elementIndex  = sm.elementIndex;
        return FormatCandidateValue(tmp, spec.dt, d);
    };

    auto matchesTerm = [&](const GroupCandidate& gc, const std::string& term) -> bool {
        if (ContainsCI(instances[gc.instanceIdx].instanceName, term)) return true;
        for (size_t s = 0; s < gc.slotMatches.size(); ++s) {
            const SlotSpec& spec = slots[s];  // slotMatches.size() == slots.size() invariant
            for (const auto& sm : gc.slotMatches[s]) {
                const FieldDescriptor& d = descriptors[sm.descriptorIdx];
                if (ContainsCI(d.className, term)) return true;
                if (ContainsCI(d.definingClassName, term)) return true;
                if (ContainsCI(d.fieldName, term)) return true;
                if (ContainsCI(slotValue(sm, spec), term)) return true;
            }
        }
        return false;
    };
    auto matchesFilter = [&](const GroupCandidate& gc) -> bool {
        for (const std::string& t : terms)
            if (!matchesTerm(gc, t)) return false;   // term-level AND
        return true;                                  // no terms => keep all
    };

    std::vector<uint32_t> idx;
    idx.reserve(candidates.size());
    for (uint32_t i = 0; i < candidates.size(); ++i) {
        // Class-noise exclude on the candidate's object class (NOT per-slot
        // owner_class) so it matches single-scan / find_instances semantics.
        if (hasExclude && excludeClasses.count(GroupCandidateClass(candidates[i], descriptors)))
            continue;
        if (matchesFilter(candidates[i])) idx.push_back(i);
    }

    if (sortKey == SortKey::ScanOrder) {
        if (sortDesc) std::reverse(idx.begin(), idx.end());
        return idx;
    }

    // Which leaf each slot DISPLAYS, per surviving candidate.
    //
    // The key extractors below used to read `slotMatches[0][0]` — the first leaf the
    // scan happened to keep — while the row Fern renders shows `slotMatches[0][picks[0]]`.
    // With `per_slot_cap` at 256 a slot routinely keeps dozens of leaves, so the grid was
    // ordered by a value the user cannot see: "sort by Value" on a filtered group scan
    // produced an order with no visible relationship to the Value column. (audit #5 AB6)
    //
    // Precomputed once per candidate rather than inside the comparator: std::stable_sort
    // calls `less` O(n log n) times and the assignment is a per-slot scan over every kept
    // leaf. Keyed by candidate index so the comparator stays a lookup.
    //
    // The same function Fern calls, deliberately — CLAUDE.md records that this rule
    // drifting from the filter it must agree with is what produced four "the scan missed
    // my field" reports, and a sort key computed from a second, private notion of "the
    // leaf" would be that drift returning in the one place nobody looks.
    std::unordered_map<uint32_t, std::vector<size_t>> picksByCandidate;
    picksByCandidate.reserve(idx.size());
    for (uint32_t i : idx)
        picksByCandidate.emplace(
            i, PickGroupWitnessAssignment(candidates[i].slotMatches, slots, descriptors, terms));

    // The displayed leaf of slot `s`, or nullptr when that slot kept nothing.
    auto shown = [&](uint32_t ci, size_t s) -> const GroupSlotMatch* {
        const GroupCandidate& gc = candidates[ci];
        if (s >= gc.slotMatches.size() || gc.slotMatches[s].empty()) return nullptr;
        auto it = picksByCandidate.find(ci);
        size_t pick = (it != picksByCandidate.end() && s < it->second.size()) ? it->second[s] : 0;
        if (pick >= gc.slotMatches[s].size()) pick = 0;   // defensive: never index past the row
        return &gc.slotMatches[s][pick];
    };

    // Object-level key extractors (first non-empty slot, else slot 0 — as displayed).
    auto firstMatch = [&](uint32_t ci) -> const GroupSlotMatch* {
        for (size_t s = 0; s < candidates[ci].slotMatches.size(); ++s)
            if (const GroupSlotMatch* m = shown(ci, s)) return m;
        return nullptr;
    };
    auto classOf = [&](uint32_t ci) -> std::string {
        const GroupSlotMatch* m = firstMatch(ci);
        return m ? descriptors[m->descriptorIdx].className : std::string();
    };
    auto slot0Offset = [&](uint32_t ci) -> int32_t {
        const GroupSlotMatch* m = shown(ci, 0);
        return m ? m->offset : 0;
    };
    auto slot0Num = [&](uint32_t ci) -> double {
        const GroupSlotMatch* m = shown(ci, 0);
        if (!m) return 0.0;
        DataType t = slots.empty() ? DataType::Int32 : slots[0].dt;
        if (IsMultiNumericDataType(t))
            TryDataTypeFromPropertyTypeName(descriptors[m->descriptorIdx].fieldType, t);
        return DecodeNumericToDouble(t, m->prevValue);
    };

    auto less = [&](uint32_t a, uint32_t b) -> bool {
        const GroupCandidate& ca = candidates[a];
        const GroupCandidate& cb = candidates[b];
        switch (sortKey) {
            case SortKey::ClassName:     return classOf(a) < classOf(b);
            case SortKey::InstanceName:  return instances[ca.instanceIdx].instanceName
                                              < instances[cb.instanceIdx].instanceName;
            case SortKey::InstanceIndex: return instances[ca.instanceIdx].instanceIndex
                                              < instances[cb.instanceIdx].instanceIndex;
            case SortKey::Offset:        return slot0Offset(a) < slot0Offset(b);
            case SortKey::Value:         return slot0Num(a) < slot0Num(b);
            default:                     return false;  // unsupported key -> scan order
        }
    };
    std::stable_sort(idx.begin(), idx.end(), [&](uint32_t a, uint32_t b) {
        return sortDesc ? less(b, a) : less(a, b);
    });
    return idx;
}

std::vector<std::pair<std::string, int>> BuildGroupClassHistogram(
    const std::vector<GroupCandidate>&  candidates,
    const std::vector<FieldDescriptor>& descriptors) {
    std::unordered_map<std::string, int> counts;
    for (const auto& gc : candidates) {
        const std::string& cn = GroupCandidateClass(gc, descriptors);
        if (!cn.empty()) ++counts[cn];
    }
    return SortClassHistogram(counts);
}

GroupSessionManager& GroupSessionManager::Instance() {
    // Intentionally leaked singleton — same reasoning as SessionManager::Instance.
    static GroupSessionManager* inst = new GroupSessionManager();
    return *inst;
}

uint64_t GroupSessionManager::Begin(std::vector<SlotSpec>        slots,
                                    std::vector<GroupCandidate>  candidates,
                                    std::vector<FieldDescriptor> descriptors,
                                    std::vector<InstanceRecord>  instances) {
    ExpireOldSessions();
    std::lock_guard<std::mutex> lk(mu_);
    uint64_t id = nextId_++;
    auto sess = std::make_unique<GroupSession>();
    sess->id          = id;
    sess->slots       = std::move(slots);
    sess->candidates  = std::move(candidates);
    sess->descriptors = std::move(descriptors);
    sess->instances   = std::move(instances);
    sess->lastUse     = std::chrono::steady_clock::now();
    sessions_.emplace(id, std::move(sess));
    return id;
}

bool GroupSessionManager::End(uint64_t sessionId) {
    std::lock_guard<std::mutex> lk(mu_);
    return sessions_.erase(sessionId) > 0;
}

void GroupSessionManager::ExpireOldSessions() {
    auto now = std::chrono::steady_clock::now();
    std::lock_guard<std::mutex> lk(mu_);
    for (auto it = sessions_.begin(); it != sessions_.end(); ) {
        if (now - it->second->lastUse > kExpirySeconds) it = sessions_.erase(it);
        else ++it;
    }
}

void GroupSessionManager::DropAll() {
    std::lock_guard<std::mutex> lk(mu_);
    sessions_.clear();
}

}  // namespace Radar
