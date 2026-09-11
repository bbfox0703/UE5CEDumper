using System.Globalization;
using UE5DumpUI.Models;

namespace UE5DumpUI.Services;

/// <summary>
/// Decodes a captured field's little-endian hex bytes (as emitted by the DLL's
/// snapshot_chunk) into a canonical numeric value, using the field's DECLARED
/// property type — the same structured, type-correct interpretation the value
/// scan uses (no byte-reinterpret). Used by SPC Increased/Decreased/Delta
/// predicates; exact Changed/Unchanged compares the raw hex instead. Pure /
/// AOT-safe. Int64/UInt64 beyond 2^53 lose precision in the double (acceptable
/// for direction; exact compares use hex).
/// </summary>
public static class SnapshotNumeric
{
    public static bool TryFromHex(string declaredType, string hex, out double value)
    {
        value = 0;
        if (string.IsNullOrEmpty(hex) || (hex.Length & 1) != 0) return false;

        int byteLen = hex.Length / 2;
        if (byteLen == 0 || byteLen > 8) return false;

        Span<byte> b = stackalloc byte[8];
        b.Clear();
        for (int i = 0; i < byteLen; i++)
        {
            if (!byte.TryParse(hex.AsSpan(i * 2, 2), NumberStyles.HexNumber,
                               CultureInfo.InvariantCulture, out b[i]))
                return false;
        }

        switch (declaredType)
        {
            // Non-finite floats (NaN / ±Infinity — uninitialised slack, garbage
            // in a deep struct-array slot, or a genuinely-NaN gameplay value) have
            // no meaningful numeric value: SQLite's REAL column rejects NaN ("Cannot
            // store 'NaN' values"), and NaN comparisons in SPC/diff are nonsense.
            // Return false → numeric_value stored as NULL; the raw bits are still
            // preserved in the hex column.
            case "FloatProperty":  if (byteLen < 4) return false; value = BitConverter.ToSingle(b);  return double.IsFinite(value);
            case "DoubleProperty": if (byteLen < 8) return false; value = BitConverter.ToDouble(b);  return double.IsFinite(value);
            case "IntProperty":    if (byteLen < 4) return false; value = BitConverter.ToInt32(b);   return true;
            case "UInt32Property": if (byteLen < 4) return false; value = BitConverter.ToUInt32(b);  return true;
            case "Int16Property":  if (byteLen < 2) return false; value = BitConverter.ToInt16(b);   return true;
            case "UInt16Property": if (byteLen < 2) return false; value = BitConverter.ToUInt16(b);  return true;
            case "Int64Property":  if (byteLen < 8) return false; value = BitConverter.ToInt64(b);   return true;
            case "UInt64Property": if (byteLen < 8) return false; value = BitConverter.ToUInt64(b);  return true;
            case "Int8Property":   value = (sbyte)b[0]; return true;
            case "ByteProperty":   value = b[0];        return true;
            // [P3-SNAPNUM-ENUM] An enum is captured as ONE unsigned byte (the DLL's
            // Radar::TryDataTypeFromPropertyTypeName maps EnumProperty -> UInt8). Decode it unsigned
            // at whatever length was captured -- `b` is zero-filled past byteLen, so this is the
            // zero-extended little-endian value. Without this arm every enum field got a NULL
            // numeric_value, so SPC and Group Match skipped it and the grid showed raw hex.
            case "EnumProperty":   value = BitConverter.ToUInt64(b); return true;
            default:               return false;
        }
    }

    /// <summary>True for the float/double property types (precision-lossy display).</summary>
    public static bool IsFloatType(string declaredType) =>
        declaredType is "FloatProperty" or "DoubleProperty";

    /// <summary>Reduce a value to the integer the game DISPLAYS, per the mode — the
    /// C# mirror of the DLL's <c>Radar::ReduceRounded</c>. Round = half-away-from-zero
    /// (matches the live DLL + CE), Trunc = toward zero, Ceil = toward +inf. Delegates
    /// to <see cref="RoundModePreview.Reduce"/> (the single C# source of truth shared
    /// with the live-preview formatter). Pure.</summary>
    public static double Reduce(double x, FloatRoundMode mode) => RoundModePreview.Reduce(x, mode);

    /// <summary>True when <paramref name="x"/> is a whole number (no fractional part).</summary>
    private static bool IsWhole(double x) => RoundModePreview.IsWhole(x);

    /// <summary>The integer a fractional <paramref name="target"/> coerces to for an
    /// INTEGER-typed field, per the mode (e.g. Between 10.9 with Round → 11, Trunc → 10,
    /// Ceil → 11) — the C# mirror of the DLL's BuildNumericTargets coercion. A whole
    /// target is returned unchanged.</summary>
    public static double CoerceIntTarget(double target, FloatRoundMode mode) =>
        RoundModePreview.CoerceIntTarget(target, mode);

    /// <summary>
    /// Displayed-integer Exact match of a decoded numeric value <paramref name="v"/>
    /// against a <paramref name="target"/> (build 1672, replaces the tolerance band).
    /// Float/double fields: a WHOLE target matches any value whose DISPLAYED integer
    /// (reduce via <paramref name="mode"/>) equals it (513 finds 513.36 under Round /
    /// Trunc; 514 finds it under Ceil); a FRACTIONAL target keeps exact-literal compare.
    /// Integer fields stay strict — a fractional target is first coerced to an integer
    /// via the mode. Pure.
    /// </summary>
    public static bool ExactMatch(double v, double target, string declaredType,
                                  FloatRoundMode mode = FloatRoundMode.Round)
    {
        if (!IsFloatType(declaredType)) return v == CoerceIntTarget(target, mode);
        return IsWhole(target) ? Reduce(v, mode) == target : v == target;
    }

    /// <summary>Displayed-integer Bigger (<paramref name="bigger"/>=true) / Smaller
    /// (false) compare of a field value against a target. Float fields reduce the value
    /// for a whole target (literal for a fractional one); integer fields coerce a
    /// fractional target via the mode. Pure.</summary>
    public static bool OrderedMatch(double v, double target, string declaredType,
                                    FloatRoundMode mode, bool bigger)
    {
        if (IsFloatType(declaredType))
        {
            double lhs = IsWhole(target) ? Reduce(v, mode) : v;
            return bigger ? lhs > target : lhs < target;
        }
        double t = CoerceIntTarget(target, mode);
        return bigger ? v > t : v < t;
    }

    /// <summary>Displayed-integer Between: is <paramref name="v"/> within the inclusive
    /// [lo, hi] range? Float fields reduce the value when BOTH bounds are whole (literal
    /// otherwise); integer fields coerce each fractional bound via the mode (the 10~11 /
    /// 11~11 behavior). Bounds are min/max-normalised. Pure.</summary>
    public static bool BetweenMatch(double v, double lo, double hi, string declaredType,
                                    FloatRoundMode mode)
    {
        double a = System.Math.Min(lo, hi), b = System.Math.Max(lo, hi);
        if (IsFloatType(declaredType))
        {
            if (IsWhole(a) && IsWhole(b)) { double rv = Reduce(v, mode); return rv >= a && rv <= b; }
            return v >= a && v <= b;
        }
        double la = CoerceIntTarget(a, mode), lb = CoerceIntTarget(b, mode);
        return v >= la && v <= lb;
    }

    /// <summary>Displayed-integer "≥ low" (inclusive) — SPC AtLeast. Float fields
    /// reduce the value for a whole bound (literal otherwise); integer fields coerce a
    /// fractional bound via the mode. Pure.</summary>
    public static bool AtLeastMatch(double v, double low, string declaredType, FloatRoundMode mode)
    {
        if (IsFloatType(declaredType)) return (IsWhole(low) ? Reduce(v, mode) : v) >= low;
        return v >= CoerceIntTarget(low, mode);
    }

    /// <summary>Displayed-integer "≤ high" (inclusive) — SPC AtMost. Mirror of
    /// <see cref="AtLeastMatch"/>. Pure.</summary>
    public static bool AtMostMatch(double v, double high, string declaredType, FloatRoundMode mode)
    {
        if (IsFloatType(declaredType)) return (IsWhole(high) ? Reduce(v, mode) : v) <= high;
        return v <= CoerceIntTarget(high, mode);
    }

    /// <summary>Displayed-integer temporal compare of an OLD vs NEW value for the
    /// prev-value predicates — the "did the DISPLAYED value change/increase/decrease"
    /// question the user asked for. Float fields reduce both sides via the mode (so
    /// sub-display jitter like 100.1→100.4 reads as Unchanged); integer values are
    /// already integral. <paramref name="op"/>: +1 = Increased, -1 = Decreased,
    /// 0 = Unchanged, any other = Changed. Pure.</summary>
    public static bool TemporalMatch(double oldV, double newV, string declaredType,
                                     FloatRoundMode mode, int op)
    {
        double a = oldV, b = newV;
        if (IsFloatType(declaredType)) { a = Reduce(oldV, mode); b = Reduce(newV, mode); }
        return op switch
        {
            1  => b > a,   // Increased
            -1 => b < a,   // Decreased
            0  => b == a,  // Unchanged
            _  => b != a,  // Changed
        };
    }

    /// <summary>
    /// Render a captured field's hex to a human-readable value for the diff
    /// grid. Integers are rendered exactly (no double precision loss); floats
    /// show up to 6 significant decimals. Falls back to the raw hex if the type
    /// isn't a known numeric. Pure / AOT-safe.
    /// </summary>
    public static string Render(string declaredType, string hex)
    {
        if (string.IsNullOrEmpty(hex) || (hex.Length & 1) != 0) return hex ?? "";
        int byteLen = hex.Length / 2;
        if (byteLen == 0 || byteLen > 8) return hex;

        Span<byte> b = stackalloc byte[8];
        b.Clear();
        for (int i = 0; i < byteLen; i++)
        {
            if (!byte.TryParse(hex.AsSpan(i * 2, 2), NumberStyles.HexNumber,
                               CultureInfo.InvariantCulture, out b[i]))
                return hex;
        }

        return declaredType switch
        {
            "FloatProperty"  when byteLen >= 4 => BitConverter.ToSingle(b).ToString("0.######", CultureInfo.InvariantCulture),
            "DoubleProperty" when byteLen >= 8 => BitConverter.ToDouble(b).ToString("0.######", CultureInfo.InvariantCulture),
            "IntProperty"    when byteLen >= 4 => BitConverter.ToInt32(b).ToString(CultureInfo.InvariantCulture),
            "UInt32Property" when byteLen >= 4 => BitConverter.ToUInt32(b).ToString(CultureInfo.InvariantCulture),
            "Int16Property"  when byteLen >= 2 => BitConverter.ToInt16(b).ToString(CultureInfo.InvariantCulture),
            "UInt16Property" when byteLen >= 2 => BitConverter.ToUInt16(b).ToString(CultureInfo.InvariantCulture),
            "Int64Property"  when byteLen >= 8 => BitConverter.ToInt64(b).ToString(CultureInfo.InvariantCulture),
            "UInt64Property" when byteLen >= 8 => BitConverter.ToUInt64(b).ToString(CultureInfo.InvariantCulture),
            "Int8Property"  => ((sbyte)b[0]).ToString(CultureInfo.InvariantCulture),
            "ByteProperty"  => b[0].ToString(CultureInfo.InvariantCulture),
            "EnumProperty"  => BitConverter.ToUInt64(b).ToString(CultureInfo.InvariantCulture),   // [P3-SNAPNUM-ENUM]
            _               => hex,
        };
    }
}
