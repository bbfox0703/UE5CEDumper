using UE5DumpUI.Models;
using UE5DumpUI.Services;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// Tests for <see cref="FreezeScriptGenerator"/>.
///
/// Exercise four axes:
/// 1. Type mapping (UE -> helper) covers every numeric + bool case.
/// 2. Lua escaping survives single-quote / backslash / newline.
/// 3. The rendered script includes (a) the helper-file lookup, (b) a
///    CFG block with className/offset/type/value embedded literally,
///    (c) start() in ENABLE and stop() in DISABLE.
/// 4. Embedded helper resource is reachable from the assembly manifest
///    (catches packaging drift).
/// </summary>
public class FreezeScriptGeneratorTests
{
    [Theory]
    [InlineData("BoolProperty",    "bool")]
    [InlineData("ByteProperty",    "uint8")]
    [InlineData("Int8Property",    "int8")]
    [InlineData("Int16Property",   "int16")]
    [InlineData("UInt16Property",  "uint16")]
    [InlineData("IntProperty",     "int32")]
    [InlineData("UInt32Property",  "uint32")]
    [InlineData("EnumProperty",    "int32")]
    [InlineData("Int64Property",   "int64")]
    [InlineData("UInt64Property",  "uint64")]
    [InlineData("FloatProperty",   "float")]
    [InlineData("DoubleProperty",  "double")]
    public void MapToHelperType_KnownTypes_MapsCorrectly(string ue, string expected)
    {
        Assert.Equal(expected, FreezeScriptGenerator.MapToHelperType(ue));
        Assert.True(FreezeScriptGenerator.IsTypeSupported(ue));
    }

    [Theory]
    [InlineData("StructProperty")]
    [InlineData("ObjectProperty")]
    [InlineData("ArrayProperty")]
    [InlineData("StrProperty")]
    [InlineData("NameProperty")]
    [InlineData("UnknownProperty")]
    public void MapToHelperType_UnsupportedTypes_ReturnsEmpty(string ue)
    {
        Assert.Equal("", FreezeScriptGenerator.MapToHelperType(ue));
        Assert.False(FreezeScriptGenerator.IsTypeSupported(ue));
    }

    // ==================================================================
    // Audit #5 Y15 — an EnumProperty's width comes from the ENGINE.
    //
    // The mapping used to answer "int32" for every enum, so freezing the
    // dominant UE shape (`enum class E : uint8`) emitted a 4-byte
    // writeInteger over a 1-byte field — destroying the three bytes after
    // it, 20 times a second, for as long as the freeze was active.
    // ==================================================================

    [Theory]
    [InlineData(1, "uint8")]
    [InlineData(2, "uint16")]
    [InlineData(4, "int32")]
    [InlineData(8, "int64")]
    [InlineData(0, "int32")]   // unreported (older DLL) → legacy default
    [InlineData(3, "int32")]   // nonsense width → legacy default, never a partial write
    [InlineData(-1, "int32")]
    public void HelperTypeForSize_PicksWriterByWidth(int size, string expected)
    {
        Assert.Equal(expected, FreezeScriptGenerator.HelperTypeForSize(size));
    }

    [Theory]
    [InlineData(1, "uint8")]
    [InlineData(2, "uint16")]
    [InlineData(4, "int32")]
    [InlineData(8, "int64")]
    [InlineData(0, "int32")]
    public void MapToHelperType_EnumProperty_FollowsReportedSize(int size, string expected)
    {
        Assert.Equal(expected, FreezeScriptGenerator.MapToHelperType("EnumProperty", size));
        // Supported at every width — the gate is a property of the TYPE.
        Assert.True(FreezeScriptGenerator.IsTypeSupported("EnumProperty"));
    }

    [Theory]
    // Every type whose width its NAME already fixes must ignore the size argument —
    // a bogus/missing size from the wire must not turn a float into a byte. Only
    // EnumProperty is width-ambiguous, so only EnumProperty consults it.
    [InlineData("BoolProperty",   "bool")]
    [InlineData("ByteProperty",   "uint8")]
    [InlineData("Int8Property",   "int8")]
    [InlineData("Int16Property",  "int16")]
    [InlineData("IntProperty",    "int32")]
    [InlineData("Int64Property",  "int64")]
    [InlineData("UInt64Property", "uint64")]
    [InlineData("FloatProperty",  "float")]
    [InlineData("DoubleProperty", "double")]
    public void MapToHelperType_NonEnumTypes_IgnoreReportedSize(string ue, string expected)
    {
        foreach (var size in new[] { 0, 1, 2, 4, 8, 99 })
            Assert.Equal(expected, FreezeScriptGenerator.MapToHelperType(ue, size));
    }

    [Fact]
    public void MapToHelperType_SizelessOverload_MatchesSizeZero()
    {
        // The 1-arg form is what IsTypeSupported and the row gates call. It must be
        // exactly the legacy behaviour, not a second table that can drift.
        foreach (var ue in new[]
                 {
                     "BoolProperty", "ByteProperty", "Int8Property", "Int16Property",
                     "UInt16Property", "IntProperty", "UInt32Property", "EnumProperty",
                     "Int64Property", "UInt64Property", "FloatProperty", "DoubleProperty",
                     "StructProperty", "NopeProperty",
                 })
        {
            Assert.Equal(FreezeScriptGenerator.MapToHelperType(ue, 0),
                         FreezeScriptGenerator.MapToHelperType(ue));
        }
    }

    [Fact]
    public void Generate_OneByteEnum_EmitsUint8NotInt32()
    {
        var p = new FreezeScriptParams
        {
            ClassName      = "ABP_Player_C",
            PropertyName   = "CurrentStance",
            PropertyOffset = 0x2C1,
            UeTypeName     = "EnumProperty",
            PropertySize   = 1,
            BoolFieldMask  = 0,
            ValueLiteral   = "3",
        };

        var script = FreezeScriptGenerator.Generate(p);

        Assert.Contains("valueType          = 'uint8',", script);
        // The defect verbatim: a 4-byte writer aimed at a 1-byte field.
        Assert.DoesNotContain("valueType          = 'int32',", script);
    }

    [Theory]
    [InlineData(1, "uint8")]
    [InlineData(2, "uint16")]
    [InlineData(4, "int32")]
    [InlineData(8, "int64")]
    public void Generate_Enum_CfgValueTypeAlwaysMatchesTheMapping(int size, string expected)
    {
        var p = new FreezeScriptParams
        {
            ClassName      = "AFoo",
            PropertyName   = "Bar",
            PropertyOffset = 0x10,
            UeTypeName     = "EnumProperty",
            PropertySize   = size,
            BoolFieldMask  = 0,
            ValueLiteral   = "0",
        };

        var script = FreezeScriptGenerator.Generate(p);

        Assert.Equal(expected, FreezeScriptGenerator.MapToHelperType("EnumProperty", size));
        Assert.Contains($"valueType          = '{expected}',", script);
        // The debug line names the same type — the CFG and the log must not disagree
        // about what is being written (audit #4's root cause: report and reality
        // computed by different code paths).
        Assert.Contains($"({expected}@0x10)", script);
    }

    [Theory]
    [InlineData("plain",          "plain")]
    [InlineData(@"back\slash",    @"back\\slash")]
    [InlineData("with'quote",     @"with\'quote")]
    [InlineData("line\nbreak",    @"line\nbreak")]
    [InlineData("carriage\rret",  @"carriage\rret")]
    [InlineData("tab\there",      @"tab\there")]
    public void EscapeLua_HandlesSpecialChars(string input, string expected)
    {
        Assert.Equal(expected, FreezeScriptGenerator.EscapeLua(input));
    }

    [Fact]
    public void Generate_FloatProperty_ProducesExpectedSections()
    {
        var p = new FreezeScriptParams
        {
            ClassName      = "BP_Teammate_C",
            PropertyName   = "CurrentHealth",
            PropertyOffset = 0x4F8,
            UeTypeName     = "FloatProperty",
            PropertySize   = 4,
            BoolFieldMask  = 0,
            ValueLiteral   = "9999.0",
        };

        var script = FreezeScriptGenerator.Generate(p);

        // [ENABLE] / [DISABLE] block structure
        Assert.Contains("[ENABLE]", script);
        Assert.Contains("[DISABLE]", script);

        // Helper file lookup (no filesystem fallback)
        Assert.Contains("findTableFile('ue5_freeze_helper.lua')", script);

        // CFG block fields literal
        Assert.Contains("className          = 'BP_Teammate_C',", script);
        Assert.Contains("propOffset         = 0x4F8,", script);
        Assert.Contains("valueType          = 'float',", script);
        Assert.Contains("value              = 9999.0,", script);

        // Start in ENABLE, stop in DISABLE -- handles tracked in a shared
        // keyed table so multiple Freeze scripts don't clobber each other.
        var enableIdx = script.IndexOf("[ENABLE]");
        var disableIdx = script.IndexOf("[DISABLE]");
        Assert.True(enableIdx < disableIdx);
        var enableBlock = script.Substring(enableIdx, disableIdx - enableIdx);
        var disableBlock = script.Substring(disableIdx);
        Assert.Contains("handleOrErr.start", enableBlock);
        Assert.Contains("h.stop", disableBlock);
        // Per-script key includes the class + prop + offset
        Assert.Contains("BP_Teammate_C::CurrentHealth@0x4F8", script);
        // Shared global table -- avoids one script's [DISABLE] killing another's handle
        Assert.Contains("_ue5_freeze_handles", script);
    }

    [Fact]
    public void Generate_BoolProperty_EmitsBoolHelperType()
    {
        var p = new FreezeScriptParams
        {
            ClassName      = "PlayerCharacter",
            PropertyName   = "bCanBeDamaged",
            PropertyOffset = 0x328,
            UeTypeName     = "BoolProperty",
            PropertySize   = 1,
            BoolFieldMask  = 0,   // native bool: owns its whole byte
            ValueLiteral   = "false",
        };

        var script = FreezeScriptGenerator.Generate(p);

        Assert.Contains("valueType          = 'bool',", script);
        Assert.Contains("value              = false,", script);
        // A native bool owns its whole byte, so NO mask must be emitted —
        // emitting one would make the helper touch a single bit of a byte
        // that is entirely this property's. (audit #5 AA1)
        Assert.DoesNotContain("boolMask", script);
    }

    // ── audit #5 AA1: packed bitfield bools ──────────────────────────────
    //
    // UE packs `uint8 bFoo:1` bools eight to a byte. The freeze pipeline used
    // to drop the FBoolProperty FieldMask, so the helper stamped the whole
    // byte ~16x/sec: up to 7 sibling bools clobbered, and — whenever the mask
    // was not 0x01 — the intended bool never set at all (writing 1 sets bit 0),
    // so the feature silently no-opped WHILE corrupting its neighbours.

    [Theory]
    [InlineData(0x01)]
    [InlineData(0x02)]
    [InlineData(0x04)]
    [InlineData(0x08)]
    [InlineData(0x10)]
    [InlineData(0x20)]
    [InlineData(0x40)]
    [InlineData(0x80)]
    public void Generate_PackedBoolMask_EmitsBoolMaskIntoCfg(int mask)
    {
        var p = new FreezeScriptParams
        {
            ClassName      = "PlayerCharacter",
            PropertyName   = "bIsInvulnerable",
            PropertyOffset = 0x328,
            UeTypeName     = "BoolProperty",
            PropertySize   = 1,
            BoolFieldMask  = mask,
            ValueLiteral   = "true",
        };

        var script = FreezeScriptGenerator.Generate(p);

        Assert.Contains($"boolMask           = 0x{mask:X2},", script);
    }

    [Theory]
    // 0 = the DLL reported no mask (native bool, or a pre-AA1 DLL).
    [InlineData(0)]
    // 0xFF = UE's OWN native-bool marker: SetBoolSize writes FieldMask = 255
    // when bIsNativeBool. Treating it as a bit mask would write bit 0..7 of a
    // byte the property already owns outright.
    [InlineData(0xFF)]
    // Multi-bit values are not a shape UE produces for a single bool; ORing
    // them in would set bits belonging to nobody.
    [InlineData(0x03)]
    [InlineData(0x05)]
    [InlineData(0x81)]
    // Defensive: a negative can only arrive from a corrupt wire value.
    [InlineData(-1)]
    public void Generate_NonPackedBoolMask_OmitsBoolMask(int mask)
    {
        var p = new FreezeScriptParams
        {
            ClassName      = "PlayerCharacter",
            PropertyName   = "bCanBeDamaged",
            PropertyOffset = 0x328,
            UeTypeName     = "BoolProperty",
            PropertySize   = 1,
            BoolFieldMask  = mask,
            ValueLiteral   = "false",
        };

        var script = FreezeScriptGenerator.Generate(p);

        Assert.DoesNotContain("boolMask", script);
    }

    [Fact]
    public void Generate_NonBoolType_NeverEmitsBoolMask()
    {
        // The mask is meaningless off a BoolProperty. A row carrying a stale
        // one must not turn an int freeze into a bit write — the CFG guard is
        // on the resolved helper type, not just on the mask value.
        var p = new FreezeScriptParams
        {
            ClassName      = "Foo",
            PropertyName   = "Count",
            PropertyOffset = 0x10,
            UeTypeName     = "IntProperty",
            PropertySize   = 4,
            BoolFieldMask  = 0x04,
            ValueLiteral   = "42",
        };

        var script = FreezeScriptGenerator.Generate(p);

        Assert.Contains("valueType          = 'int32',", script);
        Assert.DoesNotContain("boolMask", script);
    }

    [Theory]
    [InlineData(0x01, true)]
    [InlineData(0x02, true)]
    [InlineData(0x80, true)]
    [InlineData(0x00, false)]   // no mask reported
    [InlineData(0xFF, false)]   // UE's native-bool marker
    [InlineData(0x03, false)]   // two bits
    [InlineData(0x100, false)]  // outside a byte
    [InlineData(-2, false)]
    public void IsPackedBoolMask_AcceptsOnlySingleBitsInAByte(int mask, bool expected)
        => Assert.Equal(expected, FreezeScriptGenerator.IsPackedBoolMask(mask));

    [Fact]
    public void FreezeHelper_WriteBool_HonoursTheMaskItIsGiven()
    {
        // The generator emitting `boolMask` is only half the fix — the helper
        // has to ACT on it. This pins the helper source, because nothing else
        // in the suite executes Lua: the byte-stamping write must be reachable
        // ONLY when no packed mask was supplied.
        var lua = FreezeHelperLuaResource.Read();

        // The mask reaches the writer (tick passes it as the 3rd argument).
        Assert.Contains("w(addr + offset, value, mask)", lua);
        Assert.Contains("handle.cfg.boolMask", lua);

        // Read-modify-write of a single bit, arithmetic-only because CE's Lua
        // has no bAnd/bOr/bNot (same idiom as UE5T_setbit).
        Assert.Contains("isPackedBoolMask(mask)", lua);
        Assert.Contains("math.floor(b / mask) % 2", lua);

        // The 0/0xFF exclusions live in the helper too, not only in C#.
        Assert.Contains("BOOL_BIT_MASKS", lua);
        Assert.DoesNotContain("[255]", lua);
        Assert.DoesNotContain("[0] =", lua);

        // The old unconditional comment must be gone: it documented the defect
        // as intended behaviour, which is how it survived so long.
        Assert.DoesNotContain("We do NOT support packed bitfield bools", lua);
    }

    [Fact]
    public void Generate_ClassNameWithQuote_IsEscaped()
    {
        var p = new FreezeScriptParams
        {
            ClassName      = "Weird'Class",
            PropertyName   = "X",
            PropertyOffset = 0x10,
            UeTypeName     = "IntProperty",
            PropertySize   = 4,
            BoolFieldMask  = 0,
            ValueLiteral   = "1",
        };

        var script = FreezeScriptGenerator.Generate(p);

        // Single quote must be backslash-escaped inside the Lua literal
        Assert.Contains(@"className          = 'Weird\'Class',", script);
    }

    [Fact]
    public void Generate_OffsetRendersAsHex()
    {
        // 256 = 0x100 -- verify the formatter produces 0x{X} not 256.
        var p = new FreezeScriptParams
        {
            ClassName      = "Foo",
            PropertyName   = "Bar",
            PropertyOffset = 256,
            UeTypeName     = "IntProperty",
            PropertySize   = 4,
            BoolFieldMask  = 0,
            ValueLiteral   = "0",
        };

        var script = FreezeScriptGenerator.Generate(p);

        Assert.Contains("propOffset         = 0x100,", script);
    }

    [Fact]
    public void FreezeHelperLuaResource_Read_ReturnsNonTrivialContent()
    {
        var content = FreezeHelperLuaResource.Read();

        Assert.NotNull(content);
        Assert.True(content.Length > 500,
            $"freeze helper content suspiciously short ({content.Length} chars)");
        // Sanity check: contains the public API surface the generator depends on.
        Assert.Contains("freezeProperty", content);
        Assert.Contains("CMD_LIST_INSTANCES", content);
    }
}
