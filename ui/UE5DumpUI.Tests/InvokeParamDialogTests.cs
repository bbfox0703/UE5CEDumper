using UE5DumpUI.Models;
using UE5DumpUI.Services;
using UE5DumpUI.Views;
using Xunit;

namespace UE5DumpUI.Tests;

public class InvokeParamDialogTests
{
    // --- [INVOKE-NONSTATIC-ON-CDO] the header lines carry the DLL's template warning ---

    [Theory]
    [InlineData(0, "")]
    [InlineData(-4, "ProcessEvent error code -4 (exception during call)")]
    public void HeaderLines_carry_the_template_warning_on_success_and_on_failure(int result, string error)
    {
        var r = new InvokeFunctionResult
        {
            Result = result, Error = error, InstanceName = "Default__Actor", InstanceClassName = "Actor",
            InstanceTemplate = "class_default", NonStaticOnTemplate = true,
        };

        var lines = InvokeParamDialog.HeaderLines(r, 3);

        Assert.Equal(2, lines.Count);
        Assert.StartsWith("[#3] ", lines[0], System.StringComparison.Ordinal);
        Assert.Equal(UE5DumpUI.Helpers.InvokePickText.TemplateClassDefaultText("Default__Actor", "Actor"), lines[1]);
    }

    [Theory]
    [InlineData(null)]    // an older DLL
    [InlineData(false)]   // a static function on a template, or a live object
    public void HeaderLines_control_no_warning_without_the_verdict(bool? warn)
    {
        var r = new InvokeFunctionResult
        {
            Result = 0, InstanceName = "Default__KismetSystemLibrary", InstanceClassName = "KismetSystemLibrary",
            InstanceTemplate = warn is null ? null : "class_default", NonStaticOnTemplate = warn,
        };

        Assert.Single(InvokeParamDialog.HeaderLines(r, 1));
    }

    // --- [MAINWINDOW-INVOKE-LIMIT1-CDO] a dialog opened on a class, with no object ---
    // Three MainWindow handlers looked up "a live instance" by exact class name with limit 1: that one row is normally
    // the class default object, which they skip, so the dialog opened with no address and its FIRE sent neither
    // instance_addr nor class_name -- the DLL answered "Either instance_addr or class_name is required". The dialog now
    // sends the class name, and the DLL resolves it at each FIRE with the resolver behind the Console's Run (not the
    // Console's per-class pin).

    [Fact]
    public void FireTarget_without_an_address_sends_the_class_name()
    {
        var t = InvokeParamDialog.FireTarget("", "CheatManager", null);
        Assert.Null(t.InstanceAddr);
        Assert.Equal("CheatManager", t.ClassName);
        Assert.Null(t.ExpectClassAddr);
    }

    [Fact]
    public void FireTarget_without_an_address_drops_the_class_check()
    {
        // The DLL applies expect_class_addr to the object it picks too, as an equality test: sent with a class name it
        // would refuse every subclass object the resolver may take (a Blueprint's _C), so it must not be sent.
        Assert.Null(InvokeParamDialog.FireTarget("", "PlayerController", "0x7FF6CC00").ExpectClassAddr);
    }

    [Fact]
    public void FireTarget_control_with_an_address_is_unchanged()
    {
        Assert.Equal(("0x1000", (string?)null, "0xC0"), InvokeParamDialog.FireTarget("0x1000", "Actor", "0xC0"));
    }

    [Fact]
    public void HeaderLines_name_the_object_a_class_name_fire_landed_on()
    {
        var r = new InvokeFunctionResult
        {
            Result = 0, InstancePick = "live", InstanceName = "CheatManager_0", InstanceClassName = "CheatManager",
            InstanceTemplate = "", NonStaticOnTemplate = false,
        };

        var lines = InvokeParamDialog.HeaderLines(r, 1, byClassName: true);

        Assert.Single(lines);
        Assert.EndsWith(" " + UE5DumpUI.Helpers.InvokePickText.ObjectText("CheatManager_0", "CheatManager"), lines[0],
            System.StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0, "")]
    [InlineData(-4, "ProcessEvent error code -4 (exception during call)")]
    public void HeaderLines_a_class_name_fire_on_the_class_default_says_no_live_one_exists_once(int result, string error)
    {
        var r = new InvokeFunctionResult
        {
            Result = result, Error = error, InstancePick = "class_default", InstanceName = "Default__CheatManager",
            InstanceClassName = "CheatManager", InstanceTemplate = "class_default", NonStaticOnTemplate = true,
        };

        var lines = InvokeParamDialog.HeaderLines(r, 2, byClassName: true);

        Assert.Single(lines);
        Assert.Contains(UE5DumpUI.Helpers.InvokePickText.ClassDefaultText("Default__CheatManager", "CheatManager"),
            lines[0], System.StringComparison.Ordinal);
        Assert.DoesNotContain(UE5DumpUI.Helpers.InvokePickText.TemplateClassDefaultText("Default__CheatManager", "CheatManager"),
            lines);
    }

    private static string CodeOnly(string s) => string.Join('\n', s.Split('\n').Select(l =>
    {
        int c = l.IndexOf("//", System.StringComparison.Ordinal);
        return c >= 0 ? l[..c] : l;
    }));

    /// <summary>The handler a `+=` subscribes, up to the `};` that closes its lambda at the same indent.</summary>
    private static string Handler(string src, string subscription)
    {
        int at = src.IndexOf(subscription, System.StringComparison.Ordinal);
        Assert.True(at >= 0, subscription + " not found");
        int lineStart = src.LastIndexOf('\n', at) + 1;
        string indent = src[lineStart..at];
        int end = src.IndexOf("\n" + indent + "};", at, System.StringComparison.Ordinal);
        Assert.True(end > at, subscription + ": end of the handler not found");
        return src[at..end];
    }

    [Fact]
    public void MainWindow_invoke_handlers_look_up_no_object_by_exact_class_name()
    {
        var src = CodeOnly(System.IO.File.ReadAllText(
            NumericInputCoercionTests.RepoFile("ui/UE5DumpUI/ViewModels/MainWindowViewModel.cs")));
        foreach (var sub in new[] { "Console.RequestParameterInvoke +=", "Console.RequestCopyBakedScript +=",
                                    "InterestingFunctions.RequestCopyBakedScript +=" })
        {
            var body = Handler(src, sub);
            Assert.Contains("new Views.InvokeParamDialog(", body, System.StringComparison.Ordinal);
            Assert.DoesNotContain("FindInstancesAsync(", body, System.StringComparison.Ordinal);
        }
        var consoleDialog = Handler(src, "Console.RequestParameterInvoke +=");
        Assert.DoesNotContain("expectClassAddr:", consoleDialog, System.StringComparison.Ordinal);
        // Review 1: the dialog that can FIRE is given no address, so FIRE goes by class name (a ban on text alone let
        // a swapped variable or a helper lookup through).
        Assert.Contains("\"\", _dump, _engineState?.UEVersion ?? 0", consoleDialog, System.StringComparison.Ordinal);
        Assert.Contains("mode: Views.InvokeDialogMode.PipeInvoke", consoleDialog, System.StringComparison.Ordinal);
        Assert.DoesNotContain("exactMatch: true, limit: 1)", src, System.StringComparison.Ordinal);
    }

    [Fact]
    public void Fire_sends_what_FireTarget_returns_and_names_the_object_by_class()
    {
        var src = CodeOnly(System.IO.File.ReadAllText(
            NumericInputCoercionTests.RepoFile("ui/UE5DumpUI/Views/InvokeParamDialog.cs")));
        Assert.Contains("FireTarget(_instanceAddr, _className, _expectClassAddr)", src, System.StringComparison.Ordinal);
        Assert.Contains("className: target.ClassName", src, System.StringComparison.Ordinal);
        Assert.Contains("expectClassAddr: target.ExpectClassAddr", src, System.StringComparison.Ordinal);
        Assert.Contains("HeaderLines(result, _fireCount, byClassName: string.IsNullOrEmpty(_instanceAddr))", src,
            System.StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(InvokeDialogMode.PipeInvoke, "", true)]
    [InlineData(InvokeDialogMode.PipeInvoke, "0x1000", false)]        // FIRE goes to that address
    [InlineData(InvokeDialogMode.CopyBakedScript, "", false)]        // no FIRE at all
    [InlineData(InvokeDialogMode.CopyBakedScript, "0x1000", false)]
    public void The_target_by_class_line_shows_only_where_FIRE_goes_by_class_name(InvokeDialogMode mode, string addr,
                                                                                 bool shown)
    {
        // Review 1 of [MAINWINDOW-INVOKE-LIMIT1-CDO]: nothing pinned the pre-FIRE line.
        Assert.Equal(shown, InvokeParamDialog.ShowsTargetByClassLine(mode, addr));
    }

    [Fact]
    public void The_dialog_builds_the_target_by_class_line_from_the_predicate()
    {
        var src = CodeOnly(System.IO.File.ReadAllText(
            NumericInputCoercionTests.RepoFile("ui/UE5DumpUI/Views/InvokeParamDialog.cs")));
        int ctor = src.IndexOf("public InvokeParamDialog(", System.StringComparison.Ordinal);
        Assert.True(ctor > 0, "the constructor moved -- re-point this pin");
        int gate = src.IndexOf("if (ShowsTargetByClassLine(mode, instanceAddr))", ctor, System.StringComparison.Ordinal);
        int line = src.IndexOf("InvokePickText.TargetByClassText(className)", ctor, System.StringComparison.Ordinal);
        int nextMember = src.IndexOf("\n    private ", ctor, System.StringComparison.Ordinal);
        Assert.True(gate > ctor && line > gate && (nextMember < 0 || line < nextMember),
            "the constructor shows the line under the predicate");
    }

    // --- HexToBytes ---

    [Fact]
    public void HexToBytes_EmptyString_ReturnsEmpty()
    {
        var bytes = InvokeParamDialog.HexToBytes("");
        Assert.Empty(bytes);
    }

    [Fact]
    public void HexToBytes_SingleByte()
    {
        var bytes = InvokeParamDialog.HexToBytes("FF");
        Assert.Single(bytes);
        Assert.Equal(0xFF, bytes[0]);
    }

    [Fact]
    public void HexToBytes_MultipleByte()
    {
        var bytes = InvokeParamDialog.HexToBytes("2A000000");
        Assert.Equal(4, bytes.Length);
        Assert.Equal(0x2A, bytes[0]);
        Assert.Equal(0x00, bytes[1]);
    }

    [Fact]
    public void HexToBytes_LowercaseHex()
    {
        var bytes = InvokeParamDialog.HexToBytes("ff00ab");
        Assert.Equal(3, bytes.Length);
        Assert.Equal(0xFF, bytes[0]);
        Assert.Equal(0xAB, bytes[2]);
    }

    // --- DecodeParamValue ---

    [Fact]
    public void DecodeParamValue_BoolTrue()
    {
        var buf = new byte[] { 1 };
        var p = new FunctionParamModel { Name = "Flag", TypeName = "BoolProperty", Size = 1, Offset = 0 };
        Assert.Equal("true", InvokeParamDialog.DecodeParamValue(buf, p));
    }

    [Fact]
    public void DecodeParamValue_BoolFalse()
    {
        var buf = new byte[] { 0 };
        var p = new FunctionParamModel { Name = "Flag", TypeName = "BoolProperty", Size = 1, Offset = 0 };
        Assert.Equal("false", InvokeParamDialog.DecodeParamValue(buf, p));
    }

    [Fact]
    public void DecodeParamValue_Int32()
    {
        // 42 in little-endian = 0x2A000000
        var buf = new byte[] { 0x2A, 0x00, 0x00, 0x00 };
        var p = new FunctionParamModel { Name = "X", TypeName = "IntProperty", Size = 4, Offset = 0 };
        Assert.Equal("42", InvokeParamDialog.DecodeParamValue(buf, p));
    }

    [Fact]
    public void DecodeParamValue_Float()
    {
        // float 1.0 = 0x3F800000, little-endian = 0000803F
        var buf = new byte[] { 0x00, 0x00, 0x80, 0x3F };
        var p = new FunctionParamModel { Name = "Speed", TypeName = "FloatProperty", Size = 4, Offset = 0 };
        Assert.Equal("1", InvokeParamDialog.DecodeParamValue(buf, p));
    }

    [Fact]
    public void DecodeParamValue_Double()
    {
        // double 1.0 = 0x3FF0000000000000, little-endian = 000000000000F03F
        var buf = new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0xF0, 0x3F };
        var p = new FunctionParamModel { Name = "Val", TypeName = "DoubleProperty", Size = 8, Offset = 0 };
        Assert.Equal("1", InvokeParamDialog.DecodeParamValue(buf, p));
    }

    [Fact]
    public void DecodeParamValue_Int64()
    {
        // int64 256 = 0x100, little-endian = 0001000000000000
        var buf = new byte[] { 0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
        var p = new FunctionParamModel { Name = "Val", TypeName = "Int64Property", Size = 8, Offset = 0 };
        Assert.Equal("256", InvokeParamDialog.DecodeParamValue(buf, p));
    }

    [Fact]
    public void DecodeParamValue_UInt64_ShowsHex()
    {
        var buf = new byte[] { 0xFF, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
        var p = new FunctionParamModel { Name = "Ptr", TypeName = "ObjectProperty", Size = 8, Offset = 0 };
        Assert.Equal("0xFF", InvokeParamDialog.DecodeParamValue(buf, p));
    }

    [Fact]
    public void DecodeParamValue_WithOffset()
    {
        // Buffer: [0x00, 0x00, 0x00, 0x00, 0x01]
        // Bool at offset 4
        var buf = new byte[] { 0x00, 0x00, 0x00, 0x00, 0x01 };
        var p = new FunctionParamModel { Name = "Flag", TypeName = "BoolProperty", Size = 1, Offset = 4 };
        Assert.Equal("true", InvokeParamDialog.DecodeParamValue(buf, p));
    }

    [Fact]
    public void DecodeParamValue_OffsetOutOfRange_ReturnsQuestion()
    {
        var buf = new byte[] { 0x00 };
        var p = new FunctionParamModel { Name = "X", TypeName = "IntProperty", Size = 4, Offset = 10 };
        Assert.Equal("?", InvokeParamDialog.DecodeParamValue(buf, p));
    }

    [Fact]
    public void DecodeParamValue_ByteProperty()
    {
        var buf = new byte[] { 42 };
        var p = new FunctionParamModel { Name = "Val", TypeName = "ByteProperty", Size = 1, Offset = 0 };
        Assert.Equal("42", InvokeParamDialog.DecodeParamValue(buf, p));
    }

    [Fact]
    public void DecodeParamValue_UInt16()
    {
        // uint16 258 = 0x0102, little-endian = 0201
        var buf = new byte[] { 0x02, 0x01 };
        var p = new FunctionParamModel { Name = "Val", TypeName = "UInt16Property", Size = 2, Offset = 0 };
        Assert.Equal("258", InvokeParamDialog.DecodeParamValue(buf, p));
    }

    [Fact]
    public void DecodeParamValue_EnumProperty()
    {
        // int32 3
        var buf = new byte[] { 0x03, 0x00, 0x00, 0x00 };
        var p = new FunctionParamModel { Name = "Mode", TypeName = "EnumProperty", Size = 4, Offset = 0 };
        Assert.Equal("3", InvokeParamDialog.DecodeParamValue(buf, p));
    }

    [Fact]
    public void DecodeParamValue_UnknownType_FallsBackToSize()
    {
        // Unknown 4-byte type should fallback to int32 read
        var buf = new byte[] { 0x05, 0x00, 0x00, 0x00 };
        var p = new FunctionParamModel { Name = "X", TypeName = "SomeCustomProperty", Size = 4, Offset = 0 };
        Assert.Equal("5", InvokeParamDialog.DecodeParamValue(buf, p));
    }

    [Fact]
    public void DecodeParamValue_UnknownType_LargeSize_ShowsHexDash()
    {
        // Unknown 3-byte type shows BitConverter hex
        var buf = new byte[] { 0xAA, 0xBB, 0xCC };
        var p = new FunctionParamModel { Name = "X", TypeName = "StructProperty", Size = 3, Offset = 0 };
        Assert.Equal("AA-BB-CC", InvokeParamDialog.DecodeParamValue(buf, p));
    }

    // --- DecodeDynamicStructParamValue ---

    [Fact]
    public void DecodeDynamicStructParamValue_CustomStruct()
    {
        // GameplayAttributeData: BaseValue(float,0), CurrentValue(float,4)
        var buf = new byte[8];
        BitConverter.TryWriteBytes(buf.AsSpan(0), 100.0f);  // BaseValue = 100
        BitConverter.TryWriteBytes(buf.AsSpan(4), 200.0f);  // CurrentValue = 200

        var p = new FunctionParamModel
        {
            Name = "NewValue", TypeName = "StructProperty", StructName = "GameplayAttributeData",
            Size = 8, Offset = 0,
            StructFields = new List<DynamicStructField>
            {
                new("BaseValue", "FloatProperty", 0, 4),
                new("CurrentValue", "FloatProperty", 4, 4),
            },
        };

        var result = InvokeParamDialog.DecodeDynamicStructParamValue(buf, p);
        Assert.Equal("BaseValue=100, CurrentValue=200", result);
    }

    [Fact]
    public void DecodeDynamicStructParamValue_PackedBools_ReadTheirOwnBit()
    {
        // [A3-FIRE-STRUCT-BOOLMASK], the READ side (B05 review): the mask reaches the UI, but the
        // post-call readout decoded every packed bool as its whole byte, so FHitResult's
        // bBlockingHit (bit 0) set made bStartPenetrating (bit 1) read true as well. Mask 0 is
        // unresolved and keeps the whole-byte read, as PreviewScalarValue does DLL-side.
        var buf = new byte[] { 0x01 };
        var p = new FunctionParamModel
        {
            Name = "Hit", TypeName = "StructProperty", Size = 1, Offset = 0,
            StructFields = new List<DynamicStructField>
            {
                new("bBlockingHit", "BoolProperty", 0, 1, 0x01),
                new("bStartPenetrating", "BoolProperty", 0, 1, 0x02),
                new("bUnresolved", "BoolProperty", 0, 1, 0),
            },
        };

        Assert.Equal("bBlockingHit=true, bStartPenetrating=false, bUnresolved=true",
                     InvokeParamDialog.DecodeDynamicStructParamValue(buf, p));
    }

    [Fact]
    public void DecodeDynamicStructParamValue_PackedBools_AtANonZeroOffset()
    {
        // Review of d5e9148d: every packed-bool case sat at buffer offset 0, so reading buf[sf.Offset]
        // instead of buf[p.Offset + sf.Offset] survived. Here the two bytes differ.
        var buf = new byte[] { 0x01, 0x03, 0x00, 0x00, 0x00, 0x02 };
        var p = new FunctionParamModel
        {
            Name = "Hit", TypeName = "StructProperty", Size = 2, Offset = 4,
            StructFields = new List<DynamicStructField>
            {
                new("bA", "BoolProperty", 1, 1, 0x01),
                new("bB", "BoolProperty", 1, 1, 0x02),
            },
        };

        Assert.Equal("bA=false, bB=true", InvokeParamDialog.DecodeDynamicStructParamValue(buf, p));
    }

    [Fact]
    public void DecodeDynamicStructParamValue_EmptyFields_FallsBackToScalar()
    {
        var buf = new byte[] { 0x2A, 0x00, 0x00, 0x00 };
        var p = new FunctionParamModel
        {
            Name = "X", TypeName = "StructProperty",
            Size = 4, Offset = 0,
            StructFields = new List<DynamicStructField>(),
        };

        // Empty StructFields → falls back to DecodeParamValue (scalar)
        var result = InvokeParamDialog.DecodeDynamicStructParamValue(buf, p);
        // StructProperty with size 4, unknown type → fallback to int32 by size
        Assert.Equal("42", result);
    }

    [Fact]
    public void DecodeDynamicStructParamValue_WithOffset()
    {
        // 4 bytes padding + struct at offset 4
        var buf = new byte[12];
        BitConverter.TryWriteBytes(buf.AsSpan(4), 42);    // X = 42
        BitConverter.TryWriteBytes(buf.AsSpan(8), 99);    // Y = 99

        var p = new FunctionParamModel
        {
            Name = "Pos", TypeName = "StructProperty", StructName = "MyPoint",
            Size = 8, Offset = 4,
            StructFields = new List<DynamicStructField>
            {
                new("X", "IntProperty", 0, 4),
                new("Y", "IntProperty", 4, 4),
            },
        };

        var result = InvokeParamDialog.DecodeDynamicStructParamValue(buf, p);
        Assert.Equal("X=42, Y=99", result);
    }

    [Fact]
    public void DecodeDynamicStructParamValue_MixedTypes()
    {
        // Struct with float, bool, int32
        var buf = new byte[12];
        BitConverter.TryWriteBytes(buf.AsSpan(0), 3.14f);  // Value = 3.14
        buf[4] = 1;                                         // Active = true
        BitConverter.TryWriteBytes(buf.AsSpan(8), 7);       // Count = 7

        var p = new FunctionParamModel
        {
            Name = "Data", TypeName = "StructProperty", StructName = "CustomData",
            Size = 12, Offset = 0,
            StructFields = new List<DynamicStructField>
            {
                new("Value", "FloatProperty", 0, 4),
                new("Active", "BoolProperty", 4, 1),
                new("Count", "IntProperty", 8, 4),
            },
        };

        var result = InvokeParamDialog.DecodeDynamicStructParamValue(buf, p);
        Assert.Equal("Value=3.14, Active=true, Count=7", result);
    }

    // --- DecodeStructParamValue ---

    [Fact]
    public void DecodeStructParamValue_FVector_UE4_ShowsXYZ()
    {
        var layout = KnownStructLayouts.GetLayout("Vector", ueVersion: 427)!;
        // float 1.0 = 0000803F, float 2.0 = 00000040, float 3.0 = 00004040
        var buf = new byte[]
        {
            0x00, 0x00, 0x80, 0x3F,  // X = 1.0
            0x00, 0x00, 0x00, 0x40,  // Y = 2.0
            0x00, 0x00, 0x40, 0x40,  // Z = 3.0
        };
        var p = new FunctionParamModel
        {
            Name = "Location", TypeName = "StructProperty", StructName = "Vector",
            Size = 12, Offset = 0,
        };

        var result = InvokeParamDialog.DecodeStructParamValue(buf, p, layout);

        Assert.Equal("X=1, Y=2, Z=3", result);
    }

    [Fact]
    public void DecodeStructParamValue_FVector_UE5_ShowsXYZ()
    {
        var layout = KnownStructLayouts.GetLayout("Vector", ueVersion: 505)!;
        // double 100.0 = 0x4059000000000000 → LE: 0000000000005940
        // double 200.0 = 0x4069000000000000 → LE: 0000000000006940
        // double 300.0 = 0x4072C00000000000 → LE: 000000000000C27240 wait...
        // Let's be precise:
        // double 100.0 LE bytes
        var buf = new byte[24];
        BitConverter.TryWriteBytes(buf.AsSpan(0), 100.0);
        BitConverter.TryWriteBytes(buf.AsSpan(8), 200.0);
        BitConverter.TryWriteBytes(buf.AsSpan(16), 300.0);

        var p = new FunctionParamModel
        {
            Name = "Location", TypeName = "StructProperty", StructName = "Vector",
            Size = 24, Offset = 0,
        };

        var result = InvokeParamDialog.DecodeStructParamValue(buf, p, layout);

        Assert.Equal("X=100, Y=200, Z=300", result);
    }

    [Fact]
    public void DecodeStructParamValue_FColor_ShowsBGRA()
    {
        var layout = KnownStructLayouts.GetLayout("Color", ueVersion: 505)!;
        var buf = new byte[] { 0, 128, 255, 200 };
        var p = new FunctionParamModel
        {
            Name = "Col", TypeName = "StructProperty", StructName = "Color",
            Size = 4, Offset = 0,
        };

        var result = InvokeParamDialog.DecodeStructParamValue(buf, p, layout);

        Assert.Equal("B=0, G=128, R=255, A=200", result);
    }

    [Fact]
    public void DecodeStructParamValue_WithOffset()
    {
        var layout = KnownStructLayouts.GetLayout("IntPoint", ueVersion: 505)!;
        // 8 bytes padding + IntPoint(X=10, Y=20)
        var buf = new byte[16];
        BitConverter.TryWriteBytes(buf.AsSpan(8), 10);
        BitConverter.TryWriteBytes(buf.AsSpan(12), 20);

        var p = new FunctionParamModel
        {
            Name = "Pos", TypeName = "StructProperty", StructName = "IntPoint",
            Size = 8, Offset = 8,
        };

        var result = InvokeParamDialog.DecodeStructParamValue(buf, p, layout);

        Assert.Equal("X=10, Y=20", result);
    }

    // --- Struct layout must agree with the engine (audit #5 Y7) ---------------

    [Fact]
    public void ResolveTrustedLayout_SizeAgrees_UsesTheKnownLayout()
    {
        // UE5 FVector: 3 doubles = 24 bytes, and the engine agrees.
        var layout = InvokeParamDialog.ResolveTrustedLayout("StructProperty", "Vector", 24, ueVersion: 505);

        Assert.NotNull(layout);
        Assert.Equal(24, layout!.TotalSize);
        Assert.Equal(3, layout.Fields.Count);
    }

    [Fact]
    public void ResolveTrustedLayout_VersionGuessContradictsTheEngine_IsRefused()
    {
        // The detected version says UE5 (24-byte LWC FVector) but the engine reports 12 -- the
        // UE4 layout. The guess is wrong; expanding on it would put Y at +8 where the engine has
        // it at +4, so every sub-field the user filled in would land in the wrong bytes.
        var layout = InvokeParamDialog.ResolveTrustedLayout("StructProperty", "Vector", 12, ueVersion: 505);

        Assert.Null(layout);
    }

    [Fact]
    public void ResolveTrustedLayout_TheOtherDirectionToo()
    {
        var layout = InvokeParamDialog.ResolveTrustedLayout("StructProperty", "Vector", 24, ueVersion: 427);
        Assert.Null(layout);
    }

    [Fact]
    public void ResolveTrustedLayout_EngineReportedNoSize_KeepsTheGuess()
    {
        // Nothing to contradict it -- a guess beats no layout at all.
        var layout = InvokeParamDialog.ResolveTrustedLayout("StructProperty", "Vector", 0, ueVersion: 505);
        Assert.NotNull(layout);
    }

    [Fact]
    public void ResolveTrustedLayout_NonStructOrUnknown_IsNull()
    {
        Assert.Null(InvokeParamDialog.ResolveTrustedLayout("IntProperty", "Vector", 4, 505));
        Assert.Null(InvokeParamDialog.ResolveTrustedLayout("StructProperty", "", 24, 505));
        Assert.Null(InvokeParamDialog.ResolveTrustedLayout("StructProperty", "FSomeGameStruct", 40, 505));
    }

    // ── audit #5: the width family's READ side ──────────────────────────────
    //
    // DecodeParamValue grouped "EnumProperty" with "IntProperty" behind an
    // `available >= 4` guard, so UE's dominant shape — `enum class E : uint8` —
    // was decoded as FOUR bytes whenever four bytes of buffer remained: the value
    // shown was the enum byte plus three bytes belonging to whatever followed it.
    // This is the read half of the mistake Y2 fixed on the write side of the same
    // file, and the read half is the one that corrupts nothing and just lies.

    [Fact]
    public void DecodeParamValue_OneByteEnum_MidBuffer_ReadsOnlyItsOwnByte()
    {
        // 0x07 is the enum; the three bytes after it belong to the NEXT param.
        var buf = new byte[] { 0x07, 0xAA, 0xBB, 0xCC, 0, 0, 0, 0 };
        var p = new FunctionParamModel
        {
            Name = "E", TypeName = "EnumProperty", Offset = 0, Size = 1,
        };

        // Before the fix this returned the int32 at offset 0: -860116473.
        Assert.Equal("7", InvokeParamDialog.DecodeParamValue(buf, p));
    }

    [Fact]
    public void DecodeParamValue_OneByteEnum_AtBufferEnd_WasAlreadyCorrect()
    {
        // The tell that identified the defect: with fewer than 4 bytes left the old
        // guard failed and it fell through to the size switch, so the LAST param
        // decoded correctly while the same enum mid-buffer did not. Pinned so the
        // two paths cannot diverge again.
        var buf = new byte[] { 0x07 };
        var p = new FunctionParamModel
        {
            Name = "E", TypeName = "EnumProperty", Offset = 0, Size = 1,
        };

        Assert.Equal("7", InvokeParamDialog.DecodeParamValue(buf, p));
    }

    [Theory]
    [InlineData(1, "7")]
    [InlineData(2, "-22009")]       // 0xAA07 as a signed int16
    [InlineData(4, "-860116473")]  // 0xCCBBAA07 as a signed int32
    public void DecodeParamValue_EnumDecodesAtTheEngineReportedWidth(int size, string expected)
    {
        var buf = new byte[] { 0x07, 0xAA, 0xBB, 0xCC, 0, 0, 0, 0 };
        var p = new FunctionParamModel
        {
            Name = "E", TypeName = "EnumProperty", Offset = 0, Size = size,
        };

        Assert.Equal(expected, InvokeParamDialog.DecodeParamValue(buf, p));
    }

    [Fact]
    public void DecodeParamValue_IntProperty_StillReadsFourBytes()
    {
        // Negative control for the fix itself: splitting EnumProperty out of the
        // IntProperty group must not change IntProperty.
        var buf = new byte[] { 0x07, 0xAA, 0xBB, 0xCC, 0, 0, 0, 0 };
        var p = new FunctionParamModel
        {
            Name = "I", TypeName = "IntProperty", Offset = 0, Size = 4,
        };

        Assert.Equal("-860116473", InvokeParamDialog.DecodeParamValue(buf, p));
    }
}
