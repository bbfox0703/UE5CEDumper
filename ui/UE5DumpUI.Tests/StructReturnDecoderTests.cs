using System.Buffers.Binary;
using UE5DumpUI.Models;
using UE5DumpUI.Services;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// Tests for the pick #5 structured-return decoder. Three layers:
///
/// 1. Resolution-order contract: KnownStructLayouts (per-version
///    locked) wins, then DLL-discovered StructFields, then bail.
///    Locks the precedence so a future addition to KnownStructLayouts
///    can't be silently shadowed by a stale DLL StructFields list.
/// 2. Per-type decode correctness on a real FVector / FRotator byte
///    layout — the same byte→typed-value mapping the dialog already
///    uses (we delegate to InvokeParamDialog.DecodeParamValue), so
///    these double as a regression net for the shared decoder.
/// 3. Offset-handling: rows surface ABSOLUTE buffer offsets (return
///    param offset + struct sub-field offset), not relative ones,
///    so the user can copy a row's offset straight into other tools.
///    This is the contract that matters for the "Offset" column UX.
/// </summary>
public class StructReturnDecoderTests
{
    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private static FunctionParamModel MakeFVectorReturnParam(int offset = 4)
        => new()
        {
            Name       = "ReturnValue",
            TypeName   = "StructProperty",
            StructName = "Vector",
            Size       = 12,
            Offset     = offset,
            IsReturn   = true,
            // Leave StructFields empty so KnownStructLayouts path wins
            // (the dialog's resolution order test).
        };

    private static byte[] FVectorBuffer(int offset, float x, float y, float z, int totalSize = 32)
    {
        var buf = new byte[totalSize];
        BinaryPrimitives.WriteSingleLittleEndian(buf.AsSpan(offset, 4),     x);
        BinaryPrimitives.WriteSingleLittleEndian(buf.AsSpan(offset + 4, 4), y);
        BinaryPrimitives.WriteSingleLittleEndian(buf.AsSpan(offset + 8, 4), z);
        return buf;
    }

    // ------------------------------------------------------------------
    // CanDecode contract
    // ------------------------------------------------------------------

    [Fact]
    public void CanDecode_NullParam_ReturnsFalse()
        => Assert.False(StructReturnDecoder.CanDecode(null!, ueVersion: 427));

    [Fact]
    public void CanDecode_NonStructParam_ReturnsFalse()
    {
        var p = new FunctionParamModel { Name = "x", TypeName = "FloatProperty", Size = 4 };
        Assert.False(StructReturnDecoder.CanDecode(p, ueVersion: 427));
    }

    [Fact]
    public void CanDecode_KnownStruct_SizeAgreesWithTheLayout_ReturnsTrue()
    {
        // 12 bytes is UE4's float FVector, and the 427 layout is 12 bytes. Agreed.
        var p = MakeFVectorReturnParam();
        Assert.True(StructReturnDecoder.CanDecode(p, ueVersion: 427));
    }

    [Fact]
    public void CanDecode_KnownStruct_SizeContradictsTheDetectedVersion_ReturnsFalse()
    {
        // audit #5 AC2. The engine says this param is 12 bytes; UE5's FVector layout
        // is 24 (LWC turned the floats into doubles). They cannot both be right, and
        // the ENGINE is ground truth — the layout is a guess keyed on a DETECTED
        // version, and mis-detection plus licensee forks are both things this project
        // has met. Decoding on the 24-byte layout would label every value with the
        // wrong field at the wrong offset, silently.
        //
        // This assertion used to read `Assert.True(... 505)` — it pinned the
        // pre-Y7 behaviour on the RESULT path, which is precisely why AC2 survived:
        // Y7 fixed the INPUT path in the dialog and a green test kept saying the
        // result path was fine.
        var p = MakeFVectorReturnParam();
        Assert.False(StructReturnDecoder.CanDecode(p, ueVersion: 505));
        Assert.Empty(StructReturnDecoder.Decode(new byte[64], p, ueVersion: 505));
    }

    [Fact]
    public void CanDecode_KnownStruct_UE5SizedVector_ReturnsTrue()
    {
        // The positive control for the case above: when the engine DOES report 24,
        // the UE5 layout agrees and must still be used.
        var p = new FunctionParamModel
        {
            Name = "ReturnValue", TypeName = "StructProperty", StructName = "Vector",
            Size = 24, Offset = 0, IsReturn = true,
        };
        Assert.True(StructReturnDecoder.CanDecode(p, ueVersion: 505));
    }

    [Fact]
    public void CanDecode_KnownStruct_EngineReportedNoSize_TrustsTheLayout()
    {
        // Size <= 0 means the DLL did not report one, so there is nothing to
        // contradict the guess and the layout is still the best signal available.
        var p = new FunctionParamModel
        {
            Name = "ReturnValue", TypeName = "StructProperty", StructName = "Vector",
            Size = 0, Offset = 0, IsReturn = true,
        };
        Assert.True(StructReturnDecoder.CanDecode(p, ueVersion: 505));
    }

    [Fact]
    public void CanDecode_UnknownStructWithDynamicFields_ReturnsTrue()
    {
        var p = new FunctionParamModel
        {
            Name       = "ReturnValue",
            TypeName   = "StructProperty",
            StructName = "MyGameSpecificStruct_DefinitelyNotInTable",
            Size       = 8,
            Offset     = 0,
            IsReturn   = true,
            StructFields = new List<DynamicStructField>
            {
                new("A", "IntProperty", 0, 4),
                new("B", "IntProperty", 4, 4),
            },
        };
        Assert.True(StructReturnDecoder.CanDecode(p, ueVersion: 505));
    }

    [Fact]
    public void CanDecode_UnknownStructWithoutFields_ReturnsFalse()
    {
        var p = new FunctionParamModel
        {
            Name       = "ReturnValue",
            TypeName   = "StructProperty",
            StructName = "Mystery",
            Size       = 16,
        };
        Assert.False(StructReturnDecoder.CanDecode(p, ueVersion: 505));
    }

    // ------------------------------------------------------------------
    // FVector decode — the canonical "Geri PlayerCameraManager::
    // GetCameraLocation" verification target from todo.md pick #5.
    // ------------------------------------------------------------------

    [Fact]
    public void Decode_FVector_ProducesThreeFloatRows()
    {
        var p = MakeFVectorReturnParam(offset: 4);
        var buf = FVectorBuffer(offset: 4, x: 100.5f, y: -50.25f, z: 89.99f);
        var rows = StructReturnDecoder.Decode(buf, p, ueVersion: 427);

        Assert.Equal(3, rows.Count);
        Assert.Equal("X", rows[0].Name);
        Assert.Equal("Y", rows[1].Name);
        Assert.Equal("Z", rows[2].Name);
        Assert.All(rows, r => Assert.Equal("FloatProperty", r.Type));
        // Values are decoded via the dialog's shared DecodeParamValue,
        // which uses InvariantCulture for floats.
        Assert.Equal("100.5",   rows[0].Value);
        Assert.Equal("-50.25",  rows[1].Value);
        Assert.Equal("89.99",   rows[2].Value);
    }

    [Fact]
    public void Decode_FVector_OffsetsAreAbsolute()
    {
        // Return param at offset 4; FVector sub-fields are at 0/4/8
        // relative; absolute offsets should be 4/8/12.
        var p = MakeFVectorReturnParam(offset: 4);
        var buf = FVectorBuffer(offset: 4, x: 0, y: 0, z: 0);
        var rows = StructReturnDecoder.Decode(buf, p, ueVersion: 427);

        Assert.Equal(4,  rows[0].Offset);
        Assert.Equal(8,  rows[1].Offset);
        Assert.Equal(12, rows[2].Offset);
    }

    [Fact]
    public void Decode_FRotator_ProducesPitchYawRollRows()
    {
        var p = new FunctionParamModel
        {
            Name       = "ReturnValue",
            TypeName   = "StructProperty",
            StructName = "Rotator",
            Size       = 12,
            Offset     = 0,
            IsReturn   = true,
        };
        var buf = new byte[12];
        // FRotator layout (UE4 + UE5 non-LWC): Pitch, Yaw, Roll
        BinaryPrimitives.WriteSingleLittleEndian(buf.AsSpan(0, 4),  90.0f);
        BinaryPrimitives.WriteSingleLittleEndian(buf.AsSpan(4, 4),  45.0f);
        BinaryPrimitives.WriteSingleLittleEndian(buf.AsSpan(8, 4),  0.0f);

        var rows = StructReturnDecoder.Decode(buf, p, ueVersion: 427);
        Assert.Equal(3, rows.Count);
        var names = rows.Select(r => r.Name).ToHashSet();
        Assert.Contains("Pitch", names);
        Assert.Contains("Yaw",   names);
        Assert.Contains("Roll",  names);
    }

    // ------------------------------------------------------------------
    // Resolution order — KnownStructLayouts wins over StructFields
    // ------------------------------------------------------------------

    [Fact]
    public void Decode_KnownStructLayoutWinsOverDynamicFields()
    {
        // Caller supplies BOTH a recognised StructName AND a bogus
        // StructFields list. The decoder must prefer KnownStructLayouts
        // (3 rows X/Y/Z) over the dynamic fields (1 fake row).
        var p = new FunctionParamModel
        {
            Name       = "ReturnValue",
            TypeName   = "StructProperty",
            StructName = "Vector",
            Size       = 12,
            Offset     = 0,
            IsReturn   = true,
            StructFields = new List<DynamicStructField>
            {
                new("ShouldNotAppear", "Int32Property", 0, 4),
            },
        };
        var buf = FVectorBuffer(offset: 0, x: 1f, y: 2f, z: 3f, totalSize: 12);
        var rows = StructReturnDecoder.Decode(buf, p, ueVersion: 427);

        Assert.Equal(3, rows.Count);
        Assert.DoesNotContain(rows, r => r.Name == "ShouldNotAppear");
    }

    // ------------------------------------------------------------------
    // Dynamic-fields fallback — when DLL discovered a user USTRUCT
    // ------------------------------------------------------------------

    [Fact]
    public void Decode_DynamicFields_DecodesEachByItsType()
    {
        // Mock a user USTRUCT { int32 Score; bool IsAlive; float Speed; }
        // Layout (with UE 4-byte int32 alignment, bool packed at +4,
        // Speed at +8 because struct fields are byte-aligned per UE):
        //   Score @ 0  (4 bytes)
        //   IsAlive @ 4 (1 byte)
        //   Speed @ 8  (4 bytes)
        var p = new FunctionParamModel
        {
            Name       = "ReturnValue",
            TypeName   = "StructProperty",
            StructName = "FMyResult",  // not in KnownStructLayouts
            Size       = 12,
            Offset     = 4,
            IsReturn   = true,
            StructFields = new List<DynamicStructField>
            {
                new("Score",   "IntProperty",   0, 4),
                new("IsAlive", "BoolProperty",  4, 1),
                new("Speed",   "FloatProperty", 8, 4),
            },
        };
        var buf = new byte[24];
        BinaryPrimitives.WriteInt32LittleEndian(buf.AsSpan(4, 4), 999);    // Score
        buf[8] = 1;                                                         // IsAlive
        BinaryPrimitives.WriteSingleLittleEndian(buf.AsSpan(12, 4), 5.5f); // Speed

        var rows = StructReturnDecoder.Decode(buf, p, ueVersion: 505);
        Assert.Equal(3, rows.Count);
        Assert.Equal("999",  rows[0].Value);
        Assert.Equal("true", rows[1].Value);
        Assert.Equal("5.5",  rows[2].Value);

        // Absolute offsets: return param Offset (4) + sub-field offset.
        Assert.Equal(4,  rows[0].Offset);
        Assert.Equal(8,  rows[1].Offset);
        Assert.Equal(12, rows[2].Offset);
    }

    // ------------------------------------------------------------------
    // Bail-outs return empty list (never throw, never null)
    // ------------------------------------------------------------------

    [Fact]
    public void Decode_NonStructReturn_ReturnsEmpty()
    {
        var p = new FunctionParamModel
        {
            Name = "ReturnValue",
            TypeName = "FloatProperty",
            Size = 4,
            Offset = 0,
            IsReturn = true,
        };
        var buf = new byte[4];
        var rows = StructReturnDecoder.Decode(buf, p, ueVersion: 427);
        Assert.Empty(rows);
    }

    [Fact]
    public void Decode_UnknownStructNoDynamicFields_ReturnsEmpty()
    {
        var p = new FunctionParamModel
        {
            Name = "ReturnValue",
            TypeName = "StructProperty",
            StructName = "Mystery",
            Size = 16,
            Offset = 0,
            IsReturn = true,
        };
        var rows = StructReturnDecoder.Decode(new byte[16], p, ueVersion: 427);
        Assert.Empty(rows);
    }

    // ------------------------------------------------------------------
    // Tolerance for malformed buffers — should not throw out
    // ------------------------------------------------------------------

    [Fact]
    public void Decode_BufferShorterThanLayout_FallsBackPerField()
    {
        // Return param at offset 0, FVector layout expects 12 bytes
        // but we only supply 4. The dialog's DecodeParamValue already
        // returns "?" for out-of-bounds reads; the decoder must surface
        // those without throwing.
        var p = MakeFVectorReturnParam(offset: 0);
        var buf = new byte[4];
        var rows = StructReturnDecoder.Decode(buf, p, ueVersion: 427);
        Assert.Equal(3, rows.Count);
        // First row's offset (0) has 4 bytes available, decodes fine
        // (all zeros → "0"); Y at offset 4 is out-of-bounds → "?".
        Assert.Equal("0", rows[0].Value);
        Assert.Equal("?", rows[1].Value);
        Assert.Equal("?", rows[2].Value);
    }
}
