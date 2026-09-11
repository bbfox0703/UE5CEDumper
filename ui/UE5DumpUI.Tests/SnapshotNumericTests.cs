using UE5DumpUI.Models;
using UE5DumpUI.Services;
using Xunit;

namespace UE5DumpUI.Tests;

public class SnapshotNumericTests
{
    [Theory]
    // Little-endian hex (as the DLL emits) -> canonical numeric.
    [InlineData("FloatProperty",  "0000803F", 1.0)]            // 0x3F800000
    [InlineData("DoubleProperty", "0000000000000440", 2.5)]   // 0x4004000000000000
    [InlineData("IntProperty",    "1E000000", 30.0)]
    [InlineData("IntProperty",    "FFFFFFFF", -1.0)]
    [InlineData("UInt32Property", "FFFFFFFF", 4294967295.0)]
    [InlineData("Int16Property",  "FEFF", -2.0)]
    [InlineData("UInt16Property", "FFFF", 65535.0)]
    [InlineData("Int64Property",  "0500000000000000", 5.0)]
    [InlineData("Int8Property",   "FF", -1.0)]
    [InlineData("ByteProperty",   "FF", 255.0)]
    public void TryFromHex_DecodesPerDeclaredType(string type, string hex, double expected)
    {
        Assert.True(SnapshotNumeric.TryFromHex(type, hex, out var v));
        Assert.Equal(expected, v, precision: 6);
    }

    // [P3-SNAPNUM-ENUM] No EnumProperty arm, so every enum field captured since AB14 made enums
    // scannable got numeric_value NULL: every SPC numeric predicate and every Group Match slot
    // skipped it, and the grid showed raw hex. The DLL stores an enum as ONE unsigned byte
    // (Radar.cpp: EnumProperty -> UInt8); decode unsigned at the captured length.
    [Theory]
    [InlineData("EnumProperty", "03", 3.0)]
    [InlineData("EnumProperty", "FF", 255.0)]      // unsigned, never -1
    [InlineData("EnumProperty", "0001", 256.0)]    // a wider capture, zero-extended little-endian
    public void TryFromHex_DecodesAnEnumUnsigned(string type, string hex, double expected)
    {
        Assert.True(SnapshotNumeric.TryFromHex(type, hex, out var v));
        Assert.Equal(expected, v);
    }

    [Theory]
    // Float fields with the DEFAULT Round mode: a WHOLE-NUMBER target matches any
    // float that ROUNDS to it (the GAS 513.36 BaseValue found by searching "513").
    // A FRACTIONAL target keeps an exact-literal compare. Integer fields stay exact.
    [InlineData("FloatProperty",  513.3599853516, 513.0, true)]   // rounds to 513 ✓
    [InlineData("FloatProperty",  513.3599853516, 514.0, false)]  // rounds to 513, not 514
    [InlineData("FloatProperty",  513.6,          514.0, true)]   // 513.6 -> 514 ✓
    [InlineData("FloatProperty",  513.6,          513.0, false)]  // 513.6 -> 514, not 513
    [InlineData("FloatProperty",  513.0,          513.0, true)]   // exact-integer float
    [InlineData("DoubleProperty", 99.9,           100.0, true)]   // rounds up to 100
    [InlineData("FloatProperty",  513.5,          513.5, true)]   // fractional target: exact literal
    [InlineData("FloatProperty",  513.55,         513.5, false)]  // fractional target, no match
    [InlineData("IntProperty",    513.0,          513.0, true)]   // integer exact
    [InlineData("IntProperty",    513.0,          514.0, false)]  // integer never rounds
    public void ExactMatch_RoundMode_RoundsFloatsToWholeTargets(string type, double value, double target, bool expected)
    {
        Assert.Equal(expected, SnapshotNumeric.ExactMatch(value, target, type, FloatRoundMode.Round));
    }

    [Theory]
    // Trunc: a float reduces toward zero before the whole-target compare.
    [InlineData(513.9, 513.0, true)]    // 513.9 -> 513 ✓
    [InlineData(513.9, 514.0, false)]   // 513.9 -> 513, not 514
    [InlineData(513.1, 513.0, true)]    // 513.1 -> 513 ✓
    public void ExactMatch_TruncMode_TruncatesFloats(double value, double target, bool expected)
    {
        Assert.Equal(expected, SnapshotNumeric.ExactMatch(value, target, "FloatProperty", FloatRoundMode.Trunc));
    }

    [Theory]
    // Ceil: a float reduces toward +infinity before the whole-target compare.
    [InlineData(513.1, 514.0, true)]    // 513.1 -> 514 ✓
    [InlineData(513.1, 513.0, false)]   // 513.1 -> 514, not 513
    [InlineData(514.0, 514.0, true)]    // exact integer float
    public void ExactMatch_CeilMode_CeilsFloats(double value, double target, bool expected)
    {
        Assert.Equal(expected, SnapshotNumeric.ExactMatch(value, target, "FloatProperty", FloatRoundMode.Ceil));
    }

    [Theory]
    // CoerceIntTarget reduces a (possibly fractional) target to an integer per mode.
    [InlineData(10.9, FloatRoundMode.Round, 11.0)]
    [InlineData(10.9, FloatRoundMode.Trunc, 10.0)]
    [InlineData(10.9, FloatRoundMode.Ceil,  11.0)]
    [InlineData(10.1, FloatRoundMode.Round, 10.0)]
    [InlineData(10.1, FloatRoundMode.Ceil,  11.0)]
    public void CoerceIntTarget_ReducesPerMode(double target, FloatRoundMode mode, double expected)
    {
        Assert.Equal(expected, SnapshotNumeric.CoerceIntTarget(target, mode));
    }

    [Theory]
    // OrderedMatch on a float vs a whole target reduces the value first (bigger=true).
    [InlineData(513.9, 513.0, FloatRoundMode.Round, true,  true)]   // round 514 > 513
    [InlineData(513.9, 513.0, FloatRoundMode.Trunc, true,  false)]  // trunc 513 !> 513
    [InlineData(513.1, 514.0, FloatRoundMode.Round, false, true)]   // round 513 < 514
    public void OrderedMatch_ReducesFloatPerMode(double value, double target, FloatRoundMode mode, bool bigger, bool expected)
    {
        Assert.Equal(expected, SnapshotNumeric.OrderedMatch(value, target, "FloatProperty", mode, bigger));
    }

    [Theory]
    // BetweenMatch on an INTEGER field with fractional bounds: the bounds are coerced
    // to integers per the mode, giving CE-style ranges (10.9..11.1):
    //   Round -> 11..11, Trunc -> 10..11, Ceil -> 11..12.
    [InlineData(11.0, FloatRoundMode.Round, true)]    // 11 in 11..11
    [InlineData(10.0, FloatRoundMode.Round, false)]   // 10 not in 11..11
    [InlineData(10.0, FloatRoundMode.Trunc, true)]    // 10 in 10..11
    [InlineData(12.0, FloatRoundMode.Ceil,  true)]    // 12 in 11..12
    [InlineData(11.0, FloatRoundMode.Ceil,  true)]    // 11 in 11..12
    public void BetweenMatch_CoercesIntegerBoundsPerMode(double value, FloatRoundMode mode, bool expected)
    {
        Assert.Equal(expected, SnapshotNumeric.BetweenMatch(value, 10.9, 11.1, "IntProperty", mode));
    }

    [Theory]
    // TemporalMatch (op: 1=Increased, -1=Decreased, 0=Unchanged, other=Changed) on a
    // float field reduces both sides per the mode before comparing.
    [InlineData(513.0, 514.0, 1,  FloatRoundMode.Round, true)]    // increased
    [InlineData(514.0, 513.0, -1, FloatRoundMode.Round, true)]    // decreased
    [InlineData(513.4, 513.1, 0,  FloatRoundMode.Round, true)]    // both round to 513 -> unchanged
    [InlineData(513.4, 513.6, 0,  FloatRoundMode.Round, false)]   // 513 vs 514 -> changed, not unchanged
    public void TemporalMatch_ReducesBothSidesPerMode(double oldV, double newV, int op, FloatRoundMode mode, bool expected)
    {
        Assert.Equal(expected, SnapshotNumeric.TemporalMatch(oldV, newV, "FloatProperty", mode, op));
    }

    [Theory]
    [InlineData("FloatProperty",  "0000C842", "100")]            // 100.0 -> "100"
    [InlineData("FloatProperty",  "0000803F", "1")]              // 1.0 -> "1"
    [InlineData("DoubleProperty", "0000000000000440", "2.5")]
    [InlineData("IntProperty",    "1E000000", "30")]
    [InlineData("IntProperty",    "FFFFFFFF", "-1")]
    [InlineData("UInt32Property", "FFFFFFFF", "4294967295")]
    [InlineData("Int64Property",  "FFFFFFFFFFFFFFFF", "-1")]      // exact, no double loss
    [InlineData("ByteProperty",   "FF", "255")]
    [InlineData("StructProperty", "DEADBEEF", "DEADBEEF")]       // unknown -> raw hex
    public void Render_ProducesDisplayString(string type, string hex, string expected)
    {
        Assert.Equal(expected, SnapshotNumeric.Render(type, hex));
    }

    [Theory]
    [InlineData("EnumProperty", "07", "7")]
    [InlineData("EnumProperty", "FF", "255")]
    public void Render_ShowsAnEnumAsItsNumber_NotRawHex(string type, string hex, string expected)
        => Assert.Equal(expected, SnapshotNumeric.Render(type, hex));

    [Theory]
    [InlineData("BoolProperty", "01")]      // not a captured numeric type
    [InlineData("StructProperty", "00000000")]
    [InlineData("IntProperty", "")]          // empty hex
    [InlineData("IntProperty", "1E0")]       // odd-length hex
    [InlineData("IntProperty", "1E")]        // too few bytes for Int32
    public void TryFromHex_RejectsInvalid(string type, string hex)
    {
        Assert.False(SnapshotNumeric.TryFromHex(type, hex, out _));
    }

    [Theory]
    // Non-finite floats must NOT yield a numeric value: SQLite's REAL column
    // rejects NaN ("Cannot store 'NaN' values") and NaN/Inf are meaningless for
    // SPC/diff. The deep recursive capture (build 1205) can reach such leaves.
    // (Little-endian hex as the DLL emits.)
    [InlineData("FloatProperty",  "0000C07F")]          // float qNaN  0x7FC00000
    [InlineData("FloatProperty",  "0000807F")]          // float +Inf  0x7F800000
    [InlineData("FloatProperty",  "000080FF")]          // float -Inf  0xFF800000
    [InlineData("DoubleProperty", "000000000000F87F")]  // double qNaN 0x7FF8000000000000
    [InlineData("DoubleProperty", "000000000000F07F")]  // double +Inf 0x7FF0000000000000
    [InlineData("DoubleProperty", "000000000000F0FF")]  // double -Inf 0xFFF0000000000000
    public void TryFromHex_RejectsNonFiniteFloats(string type, string hex)
    {
        Assert.False(SnapshotNumeric.TryFromHex(type, hex, out _));
    }

    [Theory]
    // Native-C scan P0 contract (docs/native-c-value-scan-spec.md §4.3): the DLL's
    // Ubel::NormalizeGuessedTypeToProperty maps every "Guess What" label to one of
    // these canonical property-type strings before emitting a native snapshot row.
    // SnapshotNumeric.TryFromHex MUST accept each (with correctly-sized hex) or the
    // native field would store NULL numeric_value and silently break SPC / Pivot.
    // This is the C# half of the cross-language round-trip the DLL test locks via
    // Radar::TryDataTypeFromPropertyTypeName.
    [InlineData("FloatProperty",  "0000803F")]          // <- Float / Float?  (1.0)
    [InlineData("DoubleProperty", "000000000000F03F")]  // <- Double / Double? (1.0)
    [InlineData("IntProperty",    "01000000")]          // <- Int32?
    [InlineData("Int16Property",  "0100")]              // <- Int16?
    [InlineData("ByteProperty",   "01")]                // <- Byte?
    [InlineData("Int64Property",  "0100000000000000")]  // <- Int64? (if ever emitted)
    public void TryFromHex_AcceptsEveryNormalizedNativeType(string canonicalType, string hex)
    {
        Assert.True(SnapshotNumeric.TryFromHex(canonicalType, hex, out _));
    }
}
