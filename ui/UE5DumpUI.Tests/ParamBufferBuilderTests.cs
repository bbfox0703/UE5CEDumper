using UE5DumpUI.Models;
using UE5DumpUI.Services;
using Xunit;

namespace UE5DumpUI.Tests;

public class ParamBufferBuilderTests
{
    [Fact]
    public void BuildParamsHex_ZeroParmsSize_ReturnsEmpty()
    {
        var result = ParamBufferBuilder.BuildParamsHex(
            Array.Empty<FunctionParamModel>(),
            Array.Empty<string>(),
            parmsSize: 0);

        Assert.Equal("", result);
    }

    [Fact]
    public void BuildParamsHex_Int32AtOffset0()
    {
        var @params = new List<FunctionParamModel>
        {
            new() { Name = "Amount", TypeName = "IntProperty", Size = 4, Offset = 0 },
        };

        var hex = ParamBufferBuilder.BuildParamsHex(@params, new[] { "42" }, parmsSize: 4);

        // 42 decimal = 0x2A, little-endian int32 = 2A000000
        Assert.Equal("2A000000", hex);
    }

    [Fact]
    public void BuildParamsHex_FloatAtOffset4()
    {
        var @params = new List<FunctionParamModel>
        {
            new() { Name = "Speed", TypeName = "FloatProperty", Size = 4, Offset = 4 },
        };

        var hex = ParamBufferBuilder.BuildParamsHex(@params, new[] { "1.0" }, parmsSize: 8);

        // float 1.0 = 3F800000 (big-endian), little-endian bytes = 0000803F at offset 4
        // Full buffer: 00000000 0000803F
        Assert.Equal("000000000000803F", hex);
    }

    [Fact]
    public void BuildParamsHex_BoolAtOffset8()
    {
        var @params = new List<FunctionParamModel>
        {
            new() { Name = "Flag", TypeName = "BoolProperty", Size = 1, Offset = 8 },
        };

        var hex = ParamBufferBuilder.BuildParamsHex(@params, new[] { "1" }, parmsSize: 9);

        // 8 zero bytes + 0x01
        Assert.Equal("000000000000000001", hex);
    }

    [Fact]
    public void BuildParamsHex_MixedTypes()
    {
        var @params = new List<FunctionParamModel>
        {
            new() { Name = "X", TypeName = "IntProperty", Size = 4, Offset = 0 },
            new() { Name = "Y", TypeName = "FloatProperty", Size = 4, Offset = 4 },
            new() { Name = "Flag", TypeName = "BoolProperty", Size = 1, Offset = 8 },
        };
        var values = new[] { "100", "2.5", "1" };

        var hex = ParamBufferBuilder.BuildParamsHex(@params, values, parmsSize: 12);

        // int32 100 = 0x64 → 64000000
        // float 2.5 = 40200000 → 00002040
        // bool 1 = 01
        // Remaining 3 bytes = 000000
        Assert.StartsWith("64000000", hex);
        Assert.Equal(24, hex.Length); // 12 bytes = 24 hex chars
    }

    [Fact]
    public void BuildParamsHex_HexInput_ParsedCorrectly()
    {
        var @params = new List<FunctionParamModel>
        {
            new() { Name = "Target", TypeName = "ObjectProperty", Size = 8, Offset = 0 },
        };

        var hex = ParamBufferBuilder.BuildParamsHex(@params, new[] { "0xFF" }, parmsSize: 8);

        // ulong 0xFF = 255, little-endian = FF00000000000000
        Assert.Equal("FF00000000000000", hex);
    }

    [Fact]
    public void BuildParamsHex_Int64Property()
    {
        var @params = new List<FunctionParamModel>
        {
            new() { Name = "Val", TypeName = "Int64Property", Size = 8, Offset = 0 },
        };

        var hex = ParamBufferBuilder.BuildParamsHex(@params, new[] { "256" }, parmsSize: 8);

        // int64 256 = 0x100, little-endian = 0001000000000000
        Assert.Equal("0001000000000000", hex);
    }

    [Fact]
    public void BuildParamsHex_DoubleProperty()
    {
        var @params = new List<FunctionParamModel>
        {
            new() { Name = "Val", TypeName = "DoubleProperty", Size = 8, Offset = 0 },
        };

        var hex = ParamBufferBuilder.BuildParamsHex(@params, new[] { "1.0" }, parmsSize: 8);

        // double 1.0 = 3FF0000000000000 → little-endian: 000000000000F03F
        Assert.Equal("000000000000F03F", hex);
    }

    [Fact]
    public void BuildParamsHex_BufferZeroPadded()
    {
        var @params = new List<FunctionParamModel>
        {
            new() { Name = "X", TypeName = "IntProperty", Size = 4, Offset = 0 },
        };

        var hex = ParamBufferBuilder.BuildParamsHex(@params, new[] { "1" }, parmsSize: 16);

        // 16 bytes = 32 hex chars. First 4 bytes = int32(1), rest zeroed
        Assert.Equal(32, hex.Length);
        Assert.StartsWith("01000000", hex);
        Assert.EndsWith("000000000000000000000000", hex);
    }

    [Fact]
    public void BuildParamsHex_InvalidInput_DefaultsToZero()
    {
        var @params = new List<FunctionParamModel>
        {
            new() { Name = "X", TypeName = "IntProperty", Size = 4, Offset = 0 },
        };

        var hex = ParamBufferBuilder.BuildParamsHex(@params, new[] { "not_a_number" }, parmsSize: 4);

        // Invalid input → default 0
        Assert.Equal("00000000", hex);
    }

    [Fact]
    public void BuildParamsHex_UInt16Property()
    {
        var @params = new List<FunctionParamModel>
        {
            new() { Name = "Val", TypeName = "UInt16Property", Size = 2, Offset = 0 },
        };

        var hex = ParamBufferBuilder.BuildParamsHex(@params, new[] { "258" }, parmsSize: 2);

        // uint16 258 = 0x0102, little-endian = 0201
        Assert.Equal("0201", hex);
    }

    [Fact]
    public void BuildParamsHex_EnumProperty()
    {
        var @params = new List<FunctionParamModel>
        {
            new() { Name = "Mode", TypeName = "EnumProperty", Size = 4, Offset = 0 },
        };

        var hex = ParamBufferBuilder.BuildParamsHex(@params, new[] { "3" }, parmsSize: 4);

        Assert.Equal("03000000", hex);
    }

    // --- ShortTypeName ---

    [Theory]
    [InlineData("BoolProperty", "bool")]
    [InlineData("IntProperty", "int32")]
    [InlineData("FloatProperty", "float")]
    [InlineData("ObjectProperty", "UObject*")]
    [InlineData("StructProperty", "struct")]
    [InlineData("StrProperty", "FString")]
    [InlineData("Utf8StrProperty", "FUtf8String")]
    [InlineData("AnsiStrProperty", "FAnsiString")]
    [InlineData("SomeCustomProperty", "SomeCustom")]
    public void ShortTypeName_MapsCorrectly(string typeName, string expected)
    {
        Assert.Equal(expected, ParamBufferBuilder.ShortTypeName(typeName));
    }

    // --- IsPickablePointerType (Stage 2) ---

    [Theory]
    [InlineData("ObjectProperty")]
    [InlineData("ClassProperty")]
    [InlineData("WeakObjectProperty")]
    [InlineData("SoftObjectProperty")]
    [InlineData("SoftClassProperty")]
    [InlineData("InterfaceProperty")]
    [InlineData("LazyObjectProperty")]
    public void IsPickablePointerType_PointerTypes_True(string typeName)
    {
        // Mirrors the DLL-side WalkFunctions enrichment list. If a new pointer
        // type gets added to one side, the other side must follow — this
        // theory locks the contract.
        Assert.True(ParamBufferBuilder.IsPickablePointerType(typeName));
    }

    [Theory]
    [InlineData("IntProperty")]
    [InlineData("FloatProperty")]
    [InlineData("BoolProperty")]
    [InlineData("StructProperty")]
    [InlineData("StrProperty")]
    [InlineData("TextProperty")]
    [InlineData("NameProperty")]
    [InlineData("ArrayProperty")]
    [InlineData("MapProperty")]
    [InlineData("SetProperty")]
    [InlineData("EnumProperty")]
    [InlineData("ByteProperty")]
    [InlineData("")]
    [InlineData("UnknownProperty")]
    public void IsPickablePointerType_NonPointerTypes_False(string typeName)
    {
        // Scalars / containers / bool / string-likes don't get the picker UI.
        // Empty + unknown type names also fall through to false so the dialog
        // doesn't render orphan buttons for surprise inputs.
        Assert.False(ParamBufferBuilder.IsPickablePointerType(typeName));
    }

    // --- WriteStructParam ---

    [Fact]
    public void WriteStructParam_FVector_UE4_WritesThreeFloats()
    {
        var layout = KnownStructLayouts.GetLayout("Vector", ueVersion: 427)!;
        var buf = new byte[12];
        var values = new[] { "1.0", "2.0", "3.0" };

        ParamBufferBuilder.WriteStructParam(buf, 0, layout.Fields, values);

        // float 1.0 = 0x3F800000 → LE: 0000803F
        Assert.Equal("0000803F00000040", Convert.ToHexString(buf[..8]));
        // float 3.0 = 0x40400000 → LE: 00004040
        Assert.Equal("00004040", Convert.ToHexString(buf[8..]));
    }

    [Fact]
    public void WriteStructParam_FVector_UE5_WritesThreeDoubles()
    {
        var layout = KnownStructLayouts.GetLayout("Vector", ueVersion: 505)!;
        var buf = new byte[24];
        var values = new[] { "1.0", "2.0", "3.0" };

        ParamBufferBuilder.WriteStructParam(buf, 0, layout.Fields, values);

        // double 1.0 = 000000000000F03F (LE)
        Assert.Equal("000000000000F03F", Convert.ToHexString(buf[..8]));
        // double 2.0 = 0000000000000040 (LE)
        Assert.Equal("0000000000000040", Convert.ToHexString(buf[8..16]));
        // double 3.0 = 0000000000000840 (LE)
        Assert.Equal("0000000000000840", Convert.ToHexString(buf[16..]));
    }

    [Fact]
    public void WriteStructParam_FColor_WritesBGRA()
    {
        var layout = KnownStructLayouts.GetLayout("Color", ueVersion: 505)!;
        var buf = new byte[4];
        // B=0, G=128, R=255, A=200
        var values = new[] { "0", "128", "255", "200" };

        ParamBufferBuilder.WriteStructParam(buf, 0, layout.Fields, values);

        Assert.Equal(0, buf[0]);   // B
        Assert.Equal(128, buf[1]); // G
        Assert.Equal(255, buf[2]); // R
        Assert.Equal(200, buf[3]); // A
    }

    [Fact]
    public void WriteStructParam_WithBaseOffset()
    {
        var layout = KnownStructLayouts.GetLayout("IntPoint", ueVersion: 505)!;
        var buf = new byte[16]; // IntPoint at offset 8
        var values = new[] { "10", "20" };

        ParamBufferBuilder.WriteStructParam(buf, 8, layout.Fields, values);

        // First 8 bytes should be zero
        Assert.Equal("0000000000000000", Convert.ToHexString(buf[..8]));
        // int32 10 at offset 8, int32 20 at offset 12
        Assert.Equal("0A00000014000000", Convert.ToHexString(buf[8..]));
    }

    // --- WriteStructParam with DynamicStructField ---

    [Fact]
    public void WriteStructParam_DynamicStructField_WritesCorrectly()
    {
        var fields = new List<DynamicStructField>
        {
            new("BaseValue", "FloatProperty", 0, 4),
            new("CurrentValue", "FloatProperty", 4, 4),
        };
        var buf = new byte[8];
        var values = new[] { "1.0", "2.5" };

        ParamBufferBuilder.WriteStructParam(buf, 0, fields, values);

        // float 1.0 = 0000803F, float 2.5 = 00002040
        Assert.Equal("0000803F00002040", Convert.ToHexString(buf));
    }

    [Fact]
    public void WriteStructParam_DynamicStructField_WithBaseOffset()
    {
        var fields = new List<DynamicStructField>
        {
            new("X", "IntProperty", 0, 4),
            new("Y", "IntProperty", 4, 4),
        };
        var buf = new byte[16]; // struct at offset 8
        var values = new[] { "10", "20" };

        ParamBufferBuilder.WriteStructParam(buf, 8, fields, values);

        // First 8 bytes should be zero
        Assert.Equal("0000000000000000", Convert.ToHexString(buf[..8]));
        // int32 10 at offset 8, int32 20 at offset 12
        Assert.Equal("0A00000014000000", Convert.ToHexString(buf[8..]));
    }

    [Fact]
    public void WriteStructParam_DynamicStructField_MixedTypes()
    {
        var fields = new List<DynamicStructField>
        {
            new("Health", "FloatProperty", 0, 4),
            new("IsAlive", "BoolProperty", 4, 1),
        };
        var buf = new byte[8];
        var values = new[] { "100.0", "1" };

        ParamBufferBuilder.WriteStructParam(buf, 0, fields, values);

        // float 100.0 = 0000C842
        Assert.Equal("0000C842", Convert.ToHexString(buf[..4]));
        Assert.Equal(1, buf[4]); // bool true
    }

    // --- GetDefaultValue ---

    [Theory]
    [InlineData("FloatProperty", "0.0")]
    [InlineData("BoolProperty", "0")]
    [InlineData("ObjectProperty", "0x0")]
    [InlineData("IntProperty", "0")]
    public void GetDefaultValue_ReturnsExpected(string typeName, string expected)
    {
        Assert.Equal(expected, ParamBufferBuilder.GetDefaultValue(typeName));
    }

    // --- Contract: every IsPickablePointerType=true type must be handled
    //     end-to-end (default text + buffer writer). Catches the
    //     SoftClassProperty truncation regression where the picker UI
    //     was wired up but downstream consumers fell through to the
    //     size-based default (writing 4 bytes of a 64-bit address).

    public static readonly TheoryData<string> PickablePointerTypes = new()
    {
        "ObjectProperty",
        "ClassProperty",
        "WeakObjectProperty",
        "SoftObjectProperty",
        "SoftClassProperty",
        "InterfaceProperty",
        "LazyObjectProperty",
    };

    [Theory]
    [MemberData(nameof(PickablePointerTypes))]
    public void PickablePointerType_GetDefaultValue_IsHexZero(string typeName)
    {
        // Pointer-flavoured params take addresses; the textbox seed must be
        // "0x0" so ParseULong's hex path kicks in. A plain "0" still parses
        // fine, but the visual mismatch trips users up.
        Assert.Equal("0x0", ParamBufferBuilder.GetDefaultValue(typeName));
    }

    [Theory]
    [MemberData(nameof(PickablePointerTypes))]
    public void PickablePointerType_WriteParam_WritesFullEightBytes(string typeName)
    {
        // Picks emit a real 64-bit address (e.g. 0x00007FF61234ABCD). If
        // WriteParam falls through to the size-based default, the upper
        // bits get truncated to int32 and the call site dereferences
        // garbage. Verify all 8 bytes land at the param offset.
        var param = new FunctionParamModel
        {
            Name = "Target",
            TypeName = typeName,
            Size = 8,
            Offset = 0,
        };
        const string addr = "0x7FF61234ABCD";
        var hex = ParamBufferBuilder.BuildParamsHex(
            new[] { param }, new[] { addr }, parmsSize: 8);

        // ulong 0x7FF61234ABCD = u64 0x00007FF61234ABCD.
        // Bytes (MSB-first): 00 00 7F F6 12 34 AB CD.
        // Little-endian → CD AB 34 12 F6 7F 00 00.
        // Convert.ToHexString uppercases → CDAB3412F67F0000.
        Assert.Equal("CDAB3412F67F0000", hex);
    }

    // --- FIRE must agree with the exported script (audit #5 Y2/Y3/Y4/Y5) ------
    //
    // The FIRE path (this builder) and Copy AA Script (BakedScriptGenerator) parse the
    // SAME dialog text. They used to disagree, so one dialog produced two different calls
    // into a live game. Each test below pins a case where FIRE previously sent the wrong
    // bytes; the "baked" column in each comment is what the exported script always sent.

    [Fact]
    public void OneByteEnum_IsWritten_NotSilentlyDropped()
    {
        // Y2: EnumProperty was grouped with IntProperty and gated on `available >= 4`, so a
        // 1-byte `enum class : uint8` wrote NOTHING and the game got 0.  baked: 3
        var @params = new List<FunctionParamModel>
        {
            new() { Name = "State", TypeName = "EnumProperty", Size = 1, Offset = 0 },
        };

        var hex = ParamBufferBuilder.BuildParamsHex(@params, new[] { "3" }, parmsSize: 1);

        Assert.Equal("03", hex);
    }

    [Fact]
    public void TwoByteEnum_UsesItsRealWidth()
    {
        var @params = new List<FunctionParamModel>
        {
            new() { Name = "State", TypeName = "EnumProperty", Size = 2, Offset = 0 },
        };

        var hex = ParamBufferBuilder.BuildParamsHex(@params, new[] { "258" }, parmsSize: 2);

        Assert.Equal("0201", hex);   // 258 = 0x0102, little-endian
    }

    [Theory]
    [InlineData("true", "01")]
    [InlineData("TRUE", "01")]
    [InlineData("yes", "01")]
    [InlineData("on", "01")]
    [InlineData("1", "01")]
    [InlineData("false", "00")]
    [InlineData("no", "00")]
    [InlineData("0", "00")]
    public void BoolParam_AcceptsTheSameSpellingsAsTheExportedScript(string typed, string expected)
    {
        // Y3: `true` went through a byte parser, failed, and FIRE sent 0 while baked sent 1.
        var @params = new List<FunctionParamModel>
        {
            new() { Name = "Flag", TypeName = "BoolProperty", Size = 1, Offset = 0 },
        };

        var hex = ParamBufferBuilder.BuildParamsHex(@params, new[] { typed }, parmsSize: 1);

        Assert.Equal(expected, hex);
    }

    [Fact]
    public void SignedByteParam_AcceptsNegatives()
    {
        // Y5: byte.TryParse rejects any negative outright, so -1 silently became 0.
        var @params = new List<FunctionParamModel>
        {
            new() { Name = "Delta", TypeName = "Int8Property", Size = 1, Offset = 0 },
        };

        var hex = ParamBufferBuilder.BuildParamsHex(@params, new[] { "-1" }, parmsSize: 1);

        Assert.Equal("FF", hex);
    }

    [Fact]
    public void FloatParam_RejectsThousandsSeparators()
    {
        // Y4: the default TryParse overload allows thousands separators, so `1,5` fired as
        // 15.0 while the baked script refused it. Refusing (0) is the honest answer, and it
        // matches the sibling.
        var @params = new List<FunctionParamModel>
        {
            new() { Name = "Scale", TypeName = "FloatProperty", Size = 4, Offset = 0 },
        };

        var hex = ParamBufferBuilder.BuildParamsHex(@params, new[] { "1,5" }, parmsSize: 4);

        Assert.Equal("00000000", hex);
    }

    [Theory]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("-Infinity")]
    public void FloatParam_RefusesNonFiniteValues(string typed)
    {
        // Y4: TryParse ACCEPTS these, so they reached the game as real floats.
        var @params = new List<FunctionParamModel>
        {
            new() { Name = "Scale", TypeName = "FloatProperty", Size = 4, Offset = 0 },
        };

        var hex = ParamBufferBuilder.BuildParamsHex(@params, new[] { typed }, parmsSize: 4);

        Assert.Equal("00000000", hex);
    }

    [Fact]
    public void FloatParam_StillAcceptsAPlainInvariantDecimal()
    {
        // The guard must not cost the normal case.
        var @params = new List<FunctionParamModel>
        {
            new() { Name = "Scale", TypeName = "FloatProperty", Size = 4, Offset = 0 },
        };

        var hex = ParamBufferBuilder.BuildParamsHex(@params, new[] { "1.5" }, parmsSize: 4);

        Assert.Equal("0000C03F", hex);   // 1.5f
    }

    // ── audit #5: the width family on the FIRE path ─────────────────────────
    //
    // Every integer write in ParamBufferBuilder masks to the field width
    // ((short), (int), unchecked((byte)u)), so an out-of-range value used to
    // reach the game truncated with nothing said. The masks stay — they are Y5's
    // fix, so a signed -1 still reaches the game as 0xFF — and a range check now
    // sits in front of them.

    [Theory]
    // Fits: the exact unsigned max, and the negative that Y5 requires to survive.
    [InlineData("ByteProperty", 1, "255", true)]
    [InlineData("ByteProperty", 1, "-1", true)]
    [InlineData("Int8Property", 1, "-128", true)]
    [InlineData("Int16Property", 2, "65535", true)]
    [InlineData("Int16Property", 2, "-32768", true)]
    // Does not fit in EITHER signedness — the case the whole family is about.
    [InlineData("ByteProperty", 1, "9999", false)]
    [InlineData("ByteProperty", 1, "256", false)]
    [InlineData("Int8Property", 1, "-129", false)]
    [InlineData("Int16Property", 2, "65536", false)]
    [InlineData("UInt16Property", 2, "70000", false)]
    [InlineData("IntProperty", 4, "5000000000", false)]
    // Enum width comes from the engine, not the type name.
    [InlineData("EnumProperty", 1, "9999", false)]
    [InlineData("EnumProperty", 1, "200", true)]
    [InlineData("EnumProperty", 4, "9999", true)]
    // Hex forms go through the same parser as the writer.
    [InlineData("ByteProperty", 1, "0xFF", true)]
    [InlineData("ByteProperty", 1, "0x100", false)]
    public void TryValidateScalar_RangeChecksAgainstTheEngineReportedWidth(
        string typeName, int size, string text, bool expectOk)
    {
        var ok = ParamBufferBuilder.TryValidateScalar(typeName, size, text, out var err);

        Assert.Equal(expectOk, ok);
        if (expectOk) Assert.Equal("", err);
        else Assert.Contains("does not fit", err);
    }

    [Theory]
    // Not integer-ranged: validated by their own parsers, never by width.
    [InlineData("BoolProperty", 1, "true")]
    [InlineData("FloatProperty", 4, "1e30")]
    [InlineData("DoubleProperty", 8, "1e300")]
    // A pointer is a raw 8-byte address, not a ranged integer.
    [InlineData("ObjectProperty", 8, "0xFFFFFFFFFFFFFFFF")]
    [InlineData("UInt64Property", 8, "18446744073709551615")]
    // Non-numeric text is the parsers' business, not this check's.
    [InlineData("ByteProperty", 1, "not a number")]
    [InlineData("ByteProperty", 1, "")]
    public void TryValidateScalar_LeavesNonRangedInputAlone(string typeName, int size, string text)
        => Assert.True(ParamBufferBuilder.TryValidateScalar(typeName, size, text, out _));

    [Theory]
    // Y5 fixed "a negative silently fires 0" for ByteProperty only; every unsigned
    // path kept the bug, so FIRE sent 0 while the exported script baked 0xFFFF...
    // for the same input. Found at fix time by the width-family sweep, not by a
    // finder. (audit #5, root cause #4's eighth occurrence.)
    [InlineData("UInt16Property", 2, "-1", "FFFF")]
    [InlineData("Int16Property", 2, "-1", "FFFF")]
    [InlineData("ByteProperty", 1, "-1", "FF")]
    [InlineData("UInt64Property", 8, "-1", "FFFFFFFFFFFFFFFF")]
    public void WriteParam_NegativeOnAnUnsignedParam_MatchesWhatTheScriptBakes(
        string typeName, int size, string text, string expectedHexPrefix)
    {
        var @params = new[]
        {
            new FunctionParamModel { Name = "p", TypeName = typeName, Offset = 0, Size = size },
        };
        var hex = ParamBufferBuilder.BuildParamsHex(@params, new[] { text }, parmsSize: 16);

        Assert.StartsWith(expectedHexPrefix, hex, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("ByteProperty", 1, 1)]
    [InlineData("Int8Property", 1, 1)]
    [InlineData("Int16Property", 2, 2)]
    [InlineData("UInt16Property", 2, 2)]
    [InlineData("IntProperty", 4, 4)]
    [InlineData("UInt32Property", 4, 4)]
    [InlineData("Int64Property", 8, 8)]
    [InlineData("EnumProperty", 1, 1)]
    [InlineData("EnumProperty", 2, 2)]
    [InlineData("EnumProperty", 4, 4)]
    public void EffectiveIntWidth_AgreesWithHowManyBytesWriteParamActuallyTouches(
        string typeName, int size, int expectedWidth)
    {
        // EffectiveIntWidth's table mirrors WriteParam's switch, which is a drift
        // risk by construction — this is what pins them together. If someone
        // changes one, the buffer stops matching and this fails.
        Assert.Equal(expectedWidth, ParamBufferBuilder.EffectiveIntWidth(typeName, size));

        var @params = new[]
        {
            new FunctionParamModel { Name = "p", TypeName = typeName, Offset = 0, Size = size },
        };
        // The buffer starts zeroed, so write -1: all-ones at EVERY width, which
        // makes the number of non-zero leading bytes exactly the width written.
        // (Writing 0 measures nothing at all — it leaves a zeroed buffer zeroed,
        // which is how the first version of this test managed to "pass" its own
        // premise while measuring the buffer length.)
        var hex = ParamBufferBuilder.BuildParamsHex(@params, new[] { "-1" }, parmsSize: 16);
        var bytes = Convert.FromHexString(hex);

        int touched = 0;
        while (touched < bytes.Length && bytes[touched] == 0xFF) touched++;
        Assert.Equal(expectedWidth, touched);
    }
}
