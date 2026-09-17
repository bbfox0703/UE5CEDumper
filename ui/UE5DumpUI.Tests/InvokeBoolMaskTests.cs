using System;
using System.IO;
using UE5DumpUI.Models;
using UE5DumpUI.Services;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// [A3-FIRE-STRUCT-BOOLMASK] FIRE — and its Copy AA Script twin — wrote a packed-bool struct
/// sub-field as a WHOLE byte.
///
/// <para><c>FHitResult</c>'s <c>bBlockingHit</c> and <c>bStartPenetrating</c> share a byte. A
/// struct param is written one sub-field at a time, so typing one bit either zeroed its sibling
/// or landed on bit 0, and the dialog still said OK. The recorded safe shape: a single-bit mask
/// gets a read-modify-write of that bit; mask 0 / 0xFF keep today's whole-byte write; the mask
/// travels on an additive key that defaults to 0; and the Copy AA Script path changes in the SAME
/// commit (<c>ue5_invoke_helper.lua</c>'s side is pinned by <c>scripts/tests/invoke_helper_test.lua</c>).
/// ⛔ Not deduping rows by offset, not refusing such structs.</para>
/// </summary>
public class InvokeBoolMaskTests
{
    private static DynamicStructField B(string name, int mask) => new(name, "BoolProperty", 0, 1, BoolFieldMask: mask);

    [Fact]
    public void WriteStructParam_TwoPackedBoolsInOneByte_BothSurvive()
    {
        var buf = new byte[4];
        ParamBufferBuilder.WriteStructParam(buf, 0,
            new[] { B("bBlockingHit", 0x01), B("bStartPenetrating", 0x02) }, new[] { "true", "true" });
        Assert.Equal(0x03, buf[0]);
    }

    [Fact]
    public void WriteStructParam_PackedBoolFalse_ClearsOnlyItsOwnBit()
    {
        var buf = new byte[] { 0x07, 0, 0, 0 };   // siblings already written
        ParamBufferBuilder.WriteStructParam(buf, 0, new[] { B("bStartPenetrating", 0x02) }, new[] { "false" });
        Assert.Equal(0x05, buf[0]);
    }

    [Theory]
    [InlineData(0)]      // native or unresolved: the DLL sends no mask
    [InlineData(0xFF)]   // a native FieldMask, if one ever arrives
    public void WriteStructParam_NoSingleBitMask_KeepsTheWholeByteWrite(int mask)
    {
        var buf = new byte[] { 0x00, 0, 0, 0 };
        ParamBufferBuilder.WriteStructParam(buf, 0, new[] { B("bNative", mask) }, new[] { "true" });
        Assert.Equal(0x01, buf[0]);
    }

    [Fact]
    public void BakedScript_APackedBoolRow_CarriesItsMask()
    {
        var s = BakedScriptGenerator.Generate("C", "F", 8,
            new[] { new BakedParamValue("Hit.bStartPenetrating", "BoolProperty", 1, 0, "true", BoolFieldMask: 0x02) });
        Assert.Contains("mask=0x02", s);
    }

    [Fact]
    public void BakedScript_ANativeBoolRow_HasNoMask()
    {
        var s = BakedScriptGenerator.Generate("C", "F", 8,
            new[] { new BakedParamValue("bNative", "BoolProperty", 1, 0, "true") });
        Assert.DoesNotContain("mask=", s);
    }

    /// <summary>The dialog flattens a struct param into one baked row per sub-field; the mask has to
    /// ride along or the helper never sees it. CollectBakedValues lives on an Avalonia window, so
    /// the flattening is pinned by reading it back (the ClassListCapTests pattern).</summary>
    [Fact]
    public void InvokeDialog_FlattensTheSubFieldMaskIntoTheBakedRow()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        string? path = null;
        for (int i = 0; i < 8 && dir is not null && path is null; i++, dir = dir.Parent)
        {
            var c = Path.Combine(dir.FullName, "ui", "UE5DumpUI", "Views", "InvokeParamDialog.cs");
            if (File.Exists(c)) path = c;
        }
        Assert.NotNull(path);
        var src = File.ReadAllText(path!);
        int at = src.IndexOf("internal IReadOnlyList<BakedParamValue> CollectBakedValues()", StringComparison.Ordinal);
        Assert.True(at > 0, "CollectBakedValues not found — re-point this pin");
        int end = src.IndexOf("\n    }", at, StringComparison.Ordinal);
        Assert.Contains("BoolFieldMask: sf.BoolFieldMask", src.Substring(at, end - at));
    }
}
