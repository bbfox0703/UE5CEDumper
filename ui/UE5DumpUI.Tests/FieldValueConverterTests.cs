using UE5DumpUI.Core;
using UE5DumpUI.Models;
using Xunit;

namespace UE5DumpUI.Tests;

public class FieldValueConverterTests
{
    // --- IsEditableType ---

    [Theory]
    [InlineData("FloatProperty", true)]
    [InlineData("DoubleProperty", true)]
    [InlineData("IntProperty", true)]
    [InlineData("UInt32Property", true)]
    [InlineData("Int64Property", true)]
    [InlineData("UInt64Property", true)]
    [InlineData("Int16Property", true)]
    [InlineData("UInt16Property", true)]
    [InlineData("ByteProperty", true)]
    [InlineData("Int8Property", true)]
    [InlineData("BoolProperty", true)]
    [InlineData("EnumProperty", true)]
    [InlineData("NameProperty", false)]
    [InlineData("StrProperty", false)]
    [InlineData("TextProperty", false)]
    [InlineData("ObjectProperty", false)]
    [InlineData("StructProperty", false)]
    [InlineData("ArrayProperty", false)]
    [InlineData("MapProperty", false)]
    [InlineData("SetProperty", false)]
    [InlineData("DelegateProperty", false)]
    public void IsEditableType_ReturnsExpected(string typeName, bool expected)
    {
        Assert.Equal(expected, FieldValueConverter.IsEditableType(typeName));
    }

    // --- FloatProperty ---

    [Theory]
    [InlineData("3.14")]
    [InlineData("0")]
    [InlineData("-1.5")]
    [InlineData("100.0")]
    public void Float_ValidValues_Succeed(string input)
    {
        var (success, data, _) = FieldValueConverter.TryConvert("FloatProperty", input, 4);
        Assert.True(success);
        Assert.Equal(4, data.Length);
        var expected = float.Parse(input, System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(expected, BitConverter.ToSingle(data));
    }

    [Theory]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("-Infinity")]
    [InlineData("abc")]
    [InlineData("")]
    [InlineData("   ")]
    public void Float_InvalidValues_Fail(string input)
    {
        var (success, _, _) = FieldValueConverter.TryConvert("FloatProperty", input, 4);
        Assert.False(success);
    }

    // --- DoubleProperty ---

    [Fact]
    public void Double_ValidValue_Succeeds()
    {
        var (success, data, _) = FieldValueConverter.TryConvert("DoubleProperty", "1.23456789012345", 8);
        Assert.True(success);
        Assert.Equal(8, data.Length);
    }

    [Fact]
    public void Double_NaN_Fails()
    {
        var (success, _, error) = FieldValueConverter.TryConvert("DoubleProperty", "NaN", 8);
        Assert.False(success);
        Assert.Contains("NaN", error);
    }

    // --- IntProperty ---

    [Theory]
    [InlineData("42", 42)]
    [InlineData("-1", -1)]
    [InlineData("0", 0)]
    [InlineData("2147483647", int.MaxValue)]
    [InlineData("-2147483648", int.MinValue)]
    public void Int32_ValidValues_Succeed(string input, int expected)
    {
        var (success, data, _) = FieldValueConverter.TryConvert("IntProperty", input, 4);
        Assert.True(success);
        Assert.Equal(expected, BitConverter.ToInt32(data));
    }

    [Theory]
    [InlineData("2147483648")]  // overflow
    [InlineData("abc")]
    [InlineData("3.14")]
    public void Int32_InvalidValues_Fail(string input)
    {
        var (success, _, _) = FieldValueConverter.TryConvert("IntProperty", input, 4);
        Assert.False(success);
    }

    // --- UInt32Property ---

    [Theory]
    [InlineData("0", 0u)]
    [InlineData("4294967295", uint.MaxValue)]
    public void UInt32_ValidValues_Succeed(string input, uint expected)
    {
        var (success, data, _) = FieldValueConverter.TryConvert("UInt32Property", input, 4);
        Assert.True(success);
        Assert.Equal(expected, BitConverter.ToUInt32(data));
    }

    [Fact]
    public void UInt32_Negative_Fails()
    {
        var (success, _, _) = FieldValueConverter.TryConvert("UInt32Property", "-1", 4);
        Assert.False(success);
    }

    // --- Int16Property ---

    [Theory]
    [InlineData("32767", short.MaxValue)]
    [InlineData("-32768", short.MinValue)]
    public void Int16_Boundary_Succeeds(string input, short expected)
    {
        var (success, data, _) = FieldValueConverter.TryConvert("Int16Property", input, 2);
        Assert.True(success);
        Assert.Equal(expected, BitConverter.ToInt16(data));
    }

    [Fact]
    public void Int16_Overflow_Fails()
    {
        var (success, _, _) = FieldValueConverter.TryConvert("Int16Property", "32768", 2);
        Assert.False(success);
    }

    // --- UInt16Property ---

    [Theory]
    [InlineData("0", (ushort)0)]
    [InlineData("65535", ushort.MaxValue)]
    public void UInt16_Boundary_Succeeds(string input, ushort expected)
    {
        var (success, data, _) = FieldValueConverter.TryConvert("UInt16Property", input, 2);
        Assert.True(success);
        Assert.Equal(expected, BitConverter.ToUInt16(data));
    }

    [Fact]
    public void UInt16_Overflow_Fails()
    {
        var (success, _, _) = FieldValueConverter.TryConvert("UInt16Property", "65536", 2);
        Assert.False(success);
    }

    // --- ByteProperty ---

    [Theory]
    [InlineData("0", (byte)0)]
    [InlineData("255", (byte)255)]
    [InlineData("128", (byte)128)]
    public void Byte_ValidValues_Succeed(string input, byte expected)
    {
        var (success, data, _) = FieldValueConverter.TryConvert("ByteProperty", input, 1);
        Assert.True(success);
        Assert.Equal(new[] { expected }, data);
    }

    [Theory]
    [InlineData("256")]
    [InlineData("-1")]
    public void Byte_Overflow_Fails(string input)
    {
        var (success, _, _) = FieldValueConverter.TryConvert("ByteProperty", input, 1);
        Assert.False(success);
    }

    // --- Int8Property ---

    [Theory]
    [InlineData("-128", (sbyte)-128)]
    [InlineData("127", (sbyte)127)]
    public void SByte_Boundary_Succeeds(string input, sbyte expected)
    {
        var (success, data, _) = FieldValueConverter.TryConvert("Int8Property", input, 1);
        Assert.True(success);
        Assert.Equal(new[] { (byte)expected }, data);
    }

    [Fact]
    public void SByte_Overflow_Fails()
    {
        var (success, _, _) = FieldValueConverter.TryConvert("Int8Property", "128", 1);
        Assert.False(success);
    }

    // --- Int64 / UInt64 ---

    [Fact]
    public void Int64_ValidValue_Succeeds()
    {
        var (success, data, _) = FieldValueConverter.TryConvert("Int64Property", "-9223372036854775808", 8);
        Assert.True(success);
        Assert.Equal(long.MinValue, BitConverter.ToInt64(data));
    }

    [Fact]
    public void UInt64_ValidValue_Succeeds()
    {
        var (success, data, _) = FieldValueConverter.TryConvert("UInt64Property", "18446744073709551615", 8);
        Assert.True(success);
        Assert.Equal(ulong.MaxValue, BitConverter.ToUInt64(data));
    }

    // --- BoolProperty (ApplyBoolMask) ---

    [Fact]
    public void ApplyBoolMask_SetBit()
    {
        Assert.Equal(0x04, FieldValueConverter.ApplyBoolMask(0x00, 0x04, true));
    }

    [Fact]
    public void ApplyBoolMask_ClearBit()
    {
        Assert.Equal(0xFB, FieldValueConverter.ApplyBoolMask(0xFF, 0x04, false));
    }

    [Fact]
    public void ApplyBoolMask_AlreadySet_NoChange()
    {
        Assert.Equal(0x06, FieldValueConverter.ApplyBoolMask(0x06, 0x02, true));
    }

    [Fact]
    public void ApplyBoolMask_AlreadyClear_NoChange()
    {
        Assert.Equal(0x04, FieldValueConverter.ApplyBoolMask(0x04, 0x02, false));
    }

    [Fact]
    public void ApplyBoolMask_PreservesOtherBits()
    {
        // Set bit 0x08 while preserving 0xA1
        Assert.Equal(0xA9, FieldValueConverter.ApplyBoolMask(0xA1, 0x08, true));
    }

    // --- TryParseBool ---

    [Theory]
    [InlineData("true", true)]
    [InlineData("false", false)]
    [InlineData("1", true)]
    [InlineData("0", false)]
    [InlineData("True", true)]
    [InlineData("FALSE", false)]
    [InlineData("  true  ", true)]
    public void TryParseBool_ValidValues(string input, bool expected)
    {
        Assert.True(FieldValueConverter.TryParseBool(input, out var value));
        Assert.Equal(expected, value);
    }

    [Theory]
    [InlineData("yes")]
    [InlineData("no")]
    [InlineData("2")]
    [InlineData("")]
    public void TryParseBool_InvalidValues(string input)
    {
        Assert.False(FieldValueConverter.TryParseBool(input, out _));
    }

    // --- EnumProperty ---

    [Fact]
    public void Enum_ByName_Succeeds()
    {
        var entries = new List<EnumEntryValue>
        {
            new() { Value = 0, Name = "None" },
            new() { Value = 1, Name = "Walking" },
            new() { Value = 2, Name = "Flying" },
        };

        var (success, data, _) = FieldValueConverter.TryConvert("EnumProperty", "Walking", 1, entries);
        Assert.True(success);
        Assert.Equal(new byte[] { 1 }, data);
    }

    [Fact]
    public void Enum_ByName_CaseInsensitive()
    {
        var entries = new List<EnumEntryValue>
        {
            new() { Value = 0, Name = "None" },
            new() { Value = 2, Name = "ROLE_Authority" },
        };

        var (success, data, _) = FieldValueConverter.TryConvert("EnumProperty", "role_authority", 1, entries);
        Assert.True(success);
        Assert.Equal(new byte[] { 2 }, data);
    }

    [Fact]
    public void Enum_ByRawInt_Succeeds()
    {
        var entries = new List<EnumEntryValue>
        {
            new() { Value = 0, Name = "None" },
            new() { Value = 1, Name = "Walking" },
        };

        var (success, data, _) = FieldValueConverter.TryConvert("EnumProperty", "5", 1, entries);
        Assert.True(success);
        Assert.Equal(new byte[] { 5 }, data);
    }

    [Fact]
    public void Enum_InvalidName_NoIntFallback_Fails()
    {
        var entries = new List<EnumEntryValue>
        {
            new() { Value = 0, Name = "None" },
            new() { Value = 1, Name = "Walking" },
        };

        var (success, _, error) = FieldValueConverter.TryConvert("EnumProperty", "InvalidName", 1, entries);
        Assert.False(success);
        Assert.Contains("Unknown enum", error);
    }

    [Fact]
    public void Enum_NoEntries_RawInt_Succeeds()
    {
        var (success, data, _) = FieldValueConverter.TryConvert("EnumProperty", "7", 1, null);
        Assert.True(success);
        Assert.Equal(new byte[] { 7 }, data);
    }

    [Fact]
    public void Enum_4ByteSize_Succeeds()
    {
        var (success, data, _) = FieldValueConverter.TryConvert("EnumProperty", "256", 4, null);
        Assert.True(success);
        Assert.Equal(4, data.Length);
        Assert.Equal(256, BitConverter.ToInt32(data));
    }

    // --- ByteProperty with EnumEntries (enum-backed byte) ---

    [Fact]
    public void ByteWithEnum_ByName_Succeeds()
    {
        var entries = new List<EnumEntryValue>
        {
            new() { Value = 0, Name = "None" },
            new() { Value = 1, Name = "Active" },
        };

        var (success, data, _) = FieldValueConverter.TryConvert("ByteProperty", "Active", 1, entries);
        Assert.True(success);
        Assert.Equal(new byte[] { 1 }, data);
    }

    // --- Edge cases ---

    [Fact]
    public void EmptyInput_Fails()
    {
        var (success, _, error) = FieldValueConverter.TryConvert("IntProperty", "", 4);
        Assert.False(success);
        Assert.Contains("empty", error);
    }

    [Fact]
    public void WhitespaceOnly_Fails()
    {
        var (success, _, _) = FieldValueConverter.TryConvert("IntProperty", "   ", 4);
        Assert.False(success);
    }

    [Fact]
    public void LeadingTrailingWhitespace_Trimmed()
    {
        var (success, data, _) = FieldValueConverter.TryConvert("IntProperty", "  42  ", 4);
        Assert.True(success);
        Assert.Equal(42, BitConverter.ToInt32(data));
    }

    [Fact]
    public void UnknownType_Fails()
    {
        var (success, _, error) = FieldValueConverter.TryConvert("WeakObjectProperty", "123", 8);
        Assert.False(success);
        Assert.Contains("not editable", error);
    }

    // ── audit #5 AE1: the enum path masked instead of refusing ───────────────
    //
    // TryConvertEnum wrote `(byte)(rawValue & 0xFF)` and reported success, so
    // typing 9999 for a 1-byte enum put 15 in the game while LiveWalker's status
    // line said "Written: Field = 9999". Every sibling converter in that file
    // already refused out-of-range and named the range; the enum path did not.

    [Theory]
    [InlineData(1, "255", true)]
    [InlineData(1, "-1", true)]      // 0xFF from the other direction — Y5's rule
    [InlineData(1, "-128", true)]
    [InlineData(1, "0", true)]
    [InlineData(1, "256", false)]
    [InlineData(1, "9999", false)]   // the reported case
    [InlineData(1, "-129", false)]
    [InlineData(2, "65535", true)]
    [InlineData(2, "65536", false)]
    [InlineData(4, "4294967295", true)]
    [InlineData(4, "4294967296", false)]
    [InlineData(8, "9223372036854775807", true)]
    public void TryConvert_EnumProperty_RefusesWhatItCannotStore(
        int size, string input, bool expectOk)
    {
        var (ok, data, err) = FieldValueConverter.TryConvert(
            "EnumProperty", input, size, Array.Empty<EnumEntryValue>());

        Assert.Equal(expectOk, ok);
        if (expectOk)
        {
            Assert.Equal(size, data.Length);
        }
        else
        {
            Assert.Empty(data);
            Assert.Contains("does not fit", err);
            Assert.Contains("range", err);
        }
    }

    [Fact]
    public void TryConvert_EnumProperty_OutOfRange_DoesNotSilentlyTruncate()
    {
        // The exact regression: 9999 & 0xFF == 15. If this ever returns ok with
        // a 15 in it, the mask is back in front of the range check.
        var (ok, data, _) = FieldValueConverter.TryConvert(
            "EnumProperty", "9999", 1, Array.Empty<EnumEntryValue>());

        Assert.False(ok);
        Assert.Empty(data);
    }

    [Theory]
    // The union of the signed and unsigned ranges for each width.
    [InlineData(-128L, 1, true)]
    [InlineData(255L, 1, true)]
    [InlineData(-129L, 1, false)]
    [InlineData(256L, 1, false)]
    [InlineData(-32768L, 2, true)]
    [InlineData(65535L, 2, true)]
    [InlineData(65536L, 2, false)]
    [InlineData(-2147483648L, 4, true)]
    [InlineData(4294967295L, 4, true)]
    [InlineData(4294967296L, 4, false)]
    [InlineData(long.MaxValue, 8, true)]
    [InlineData(long.MinValue, 8, true)]
    // Unknown/zero width is treated as the narrowest, not as "anything goes".
    [InlineData(9999L, 0, false)]
    public void FitsInWidth_AcceptsEitherSignednessAndNothingWider(
        long value, int sizeBytes, bool expected)
        => Assert.Equal(expected, FieldValueConverter.FitsInWidth(value, sizeBytes));
}
