using System.Collections.Generic;
using System.Linq;
using UE5DumpUI.Models;
using UE5DumpUI.Services;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// Audit #5 Y16 — the enum-width family's fourth finding, and its three sites.
///
/// <para>UE sizes an enum by its UNDERLYING type. The common
/// <c>TEnumAsByte</c> / <c>enum class : uint8</c> is 1 byte, so mapping every
/// <c>EnumProperty</c> to <c>int32</c> writes 4 bytes and clobbers the next
/// parameter in <c>params_data</c>. Sites 1–2 corrupt memory; site 3 only
/// misreports a return value.</para>
///
/// <para>The size was in scope at all three sites the whole time — this is the
/// family's recurring shape: <b>the finding is the dropped field, not the
/// guess</b>. W6 had <c>CeWidthForSize</c> sitting unused; Y15's
/// <c>MapToHelperType</c> carried an "out of v1 scope" comment; here
/// <c>BakedParamValue.Size</c>'s own doc comment stated the defective
/// assumption outright.</para>
///
/// <para>No Lua change was needed: <c>writeBakedParams</c> already accepts the
/// <c>byte</c> / <c>int16</c> / <c>int64</c> tokens, so no user has to re-embed
/// the helper.</para>
/// </summary>
public class BakedEnumWidthTests
{
    // ------------------------------------------------------------------
    // The width rule itself.
    // ------------------------------------------------------------------

    [Theory]
    [InlineData(1, "byte")]     // TEnumAsByte / enum class : uint8 — the common case
    [InlineData(2, "int16")]
    [InlineData(4, "int32")]
    [InlineData(8, "int64")]
    public void EnumMapsToTheEngineReportedWidth(int size, string expected)
    {
        Assert.Equal(expected, BakedScriptGenerator.MapToHelperType("EnumProperty", size));
    }

    /// <summary>An unknown size keeps the historical answer rather than guessing
    /// small — writing too FEW bytes would leave the high bytes of a real int32
    /// enum stale, which is a different corruption, not a safer one.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(16)]
    public void EnumWithAnUnusableSize_FallsBackToInt32(int size)
    {
        Assert.Equal("int32", BakedScriptGenerator.MapToHelperType("EnumProperty", size));
    }

    /// <summary>The size-less overload is the legacy answer, so callers that only
    /// ask "is this type supported" are unaffected.</summary>
    [Fact]
    public void SizelessOverload_KeepsTheLegacyEnumAnswer()
    {
        Assert.Equal("int32", BakedScriptGenerator.MapToHelperType("EnumProperty"));
        Assert.Equal(BakedScriptGenerator.MapToHelperType("EnumProperty", 0),
                     BakedScriptGenerator.MapToHelperType("EnumProperty"));
    }

    /// <summary>
    /// THE GUARD THAT MATTERS. Every type whose width is fixed by its NAME must
    /// ignore <paramref name="size"/> entirely. Without this, a mis-reported size
    /// would turn types that are currently correct into wrong writes — i.e. the
    /// fix would become a bigger version of the bug.
    /// </summary>
    [Theory]
    [InlineData("BoolProperty")]
    [InlineData("ByteProperty")]
    [InlineData("Int8Property")]
    [InlineData("Int16Property")]
    [InlineData("UInt16Property")]
    [InlineData("IntProperty")]
    [InlineData("UInt32Property")]
    [InlineData("Int64Property")]
    [InlineData("UInt64Property")]
    [InlineData("FloatProperty")]
    [InlineData("DoubleProperty")]
    [InlineData("ObjectProperty")]
    [InlineData("NameProperty")]
    [InlineData("StrProperty")]
    public void MapToHelperTypeIgnoresSizeForEveryTypeButEnum(string ueType)
    {
        var baseline = BakedScriptGenerator.MapToHelperType(ueType, 0);
        foreach (int size in new[] { 1, 2, 4, 8, 99 })
            Assert.Equal(baseline, BakedScriptGenerator.MapToHelperType(ueType, size));
    }

    // ------------------------------------------------------------------
    // Site 2 — the baked WRITE path goes through MapInputType.
    // ------------------------------------------------------------------

    [Theory]
    [InlineData(1, "byte")]
    [InlineData(2, "int16")]
    [InlineData(8, "int64")]
    public void MapInputType_CarriesTheEnumWidthThrough(int size, string expected)
    {
        Assert.Equal(expected, BakedScriptGenerator.MapInputType("EnumProperty", size));
    }

    /// <summary>String params must keep their wide/narrow distinction whatever the
    /// size says — that distinction is what the helper needs to build the right
    /// char buffer, and it is not a width question.</summary>
    [Theory]
    [InlineData("StrProperty", "fstring")]
    [InlineData("Utf8StrProperty", "fstringn")]
    [InlineData("AnsiStrProperty", "fstringn")]
    public void MapInputType_StringsAreUnaffectedBySize(string ueType, string expected)
    {
        foreach (int size in new[] { 0, 1, 4, 16 })
            Assert.Equal(expected, BakedScriptGenerator.MapInputType(ueType, size));
    }

    // ------------------------------------------------------------------
    // The cosmetic straggler — the emitted comment must not keep asserting
    // int32 after the write was corrected.
    // ------------------------------------------------------------------

    [Theory]
    [InlineData(1, "enum(uint8)")]
    [InlineData(2, "enum(int16)")]
    [InlineData(4, "enum(int32)")]
    [InlineData(8, "enum(int64)")]
    public void EmittedComment_NamesTheWidthActuallyWritten(int size, string expected)
    {
        Assert.Equal(expected, BakedScriptGenerator.ShortTypeNameForComment("EnumProperty", size));
    }

    [Fact]
    public void EmittedComment_IgnoresSizeForFixedWidthTypes()
    {
        foreach (var t in new[] { "IntProperty", "ByteProperty", "Int64Property", "FloatProperty" })
        {
            var baseline = BakedScriptGenerator.ShortTypeNameForComment(t, 0);
            foreach (int size in new[] { 1, 2, 8 })
                Assert.Equal(baseline, BakedScriptGenerator.ShortTypeNameForComment(t, size));
        }
    }

    // ------------------------------------------------------------------
    // Cross-path agreement — the row's own complaint was that the three ways
    // of invoking a UFunction from ONE dialog input disagreed.
    // ------------------------------------------------------------------

    /// <summary>The FIRE path (ParamBufferBuilder) and the baked-script path must
    /// agree on how many bytes a 1-byte enum occupies. They are different
    /// mechanisms — a byte count vs a helper token — so this asserts the mapping
    /// between them rather than comparing them directly.</summary>
    [Theory]
    [InlineData(1, "byte")]
    [InlineData(2, "int16")]
    [InlineData(4, "int32")]
    [InlineData(8, "int64")]
    public void FirePathAndBakedPath_AgreeOnEnumWidth(int size, string bakedToken)
    {
        Assert.Equal(size, ParamBufferBuilder.EffectiveIntWidth("EnumProperty", size));
        Assert.Equal(bakedToken, BakedScriptGenerator.MapToHelperType("EnumProperty", size));
    }

    // ------------------------------------------------------------------
    // Site 1 — the interactive CE invoke FORM. Asserted on the emitted Lua,
    // because that is what actually reaches the game.
    // ------------------------------------------------------------------

    private static string InvokeScriptFor(int enumSize)
        => InvokeScriptGenerator.Generate("Shop_C", "SetMode", new FunctionInfoModel
        {
            Name = "SetMode",
            ParmsSize = 8,
            Params = new List<FunctionParamModel>
            {
                new() { Name = "Mode",  TypeName = "EnumProperty", Size = enumSize, Offset = 0 },
                // The param the over-wide write would clobber. Its offset is the
                // whole point: a 4-byte write at 0 reaches into it when Mode is 1 B.
                new() { Name = "Count", TypeName = "IntProperty",  Size = 4, Offset = 4 },
            },
        });

    [Theory]
    [InlineData(1, "writeBytes")]
    [InlineData(2, "writeSmallInteger")]
    [InlineData(8, "writeQword")]
    [InlineData(4, "writeInteger")]
    public void InvokeForm_WritesTheEnumAtItsRealWidth(int size, string expectedWriteFn)
    {
        var script = InvokeScriptFor(size);

        // The Mode write is the line carrying PD + 0.
        var line = script.Split('\n')
                         .FirstOrDefault(l => l.Contains("PD + 0", StringComparison.Ordinal)
                                           && l.Contains("write", StringComparison.Ordinal));

        Assert.NotNull(line);
        Assert.Contains(expectedWriteFn, line!, StringComparison.Ordinal);
    }

    /// <summary>The neighbouring int param must keep its own width — the fix must
    /// not widen or narrow anything except the enum.</summary>
    [Fact]
    public void InvokeForm_NeighbouringIntParamIsUnaffected()
    {
        var script = InvokeScriptFor(1);
        var line = script.Split('\n')
                         .FirstOrDefault(l => l.Contains("PD + 4", StringComparison.Ordinal)
                                           && l.Contains("write", StringComparison.Ordinal));

        Assert.NotNull(line);
        Assert.Contains("writeInteger", line!, StringComparison.Ordinal);
    }
}
