using UE5DumpUI.Models;
using UE5DumpUI.Services;
using UE5DumpUI.Views;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// [VND584-01] A weak / soft / lazy parameter leads with <c>FWeakObjectPtr { ObjectIndex, SerialNumber }</c>, never an
/// address. The invoke paths write the picked object's address and the DLL swaps it for the engine's own value
/// before ProcessEvent (Ubel::PrepareObjectParams), so what comes back in that slot is the engine's pair.
/// </summary>
public class Vnd584InvokeObjectParamTests
{
    [Fact]
    public void Lazy_GetsNoPicker_AndOnlyItsEmptyDefault()
    {
        // UHT refuses lazy UFunction parameters, and the DLL refuses a picked object in one -- no GUID can be made --
        // so a picker would be a button whose every use fails.
        Assert.False(ParamBufferBuilder.IsPickablePointerType("LazyObjectProperty"));
        Assert.True(ParamBufferBuilder.IsEmptyOnlyParam("LazyObjectProperty"));
        Assert.True(ParamBufferBuilder.TryValidateScalar("LazyObjectProperty", 0x20, "0x0", out _));
        Assert.False(ParamBufferBuilder.TryValidateScalar("LazyObjectProperty", 0x20, "0x1F2A3B4C5D0", out var err));
        Assert.Contains("cannot be built", err);
    }

    [Theory]
    [InlineData("WeakObjectProperty")]
    [InlineData("SoftObjectProperty")]
    [InlineData("SoftClassProperty")]
    public void WeakAndSoft_KeepThePicker(string type)
    {
        // Control: the DLL makes the engine's value from the picked address, so these stay pickable and writable.
        Assert.True(ParamBufferBuilder.IsPickablePointerType(type));
        Assert.False(ParamBufferBuilder.IsUnwritableParam(type));
    }

    [Theory]
    [InlineData("WeakObjectProperty", 8)]
    [InlineData("SoftObjectProperty", 0x28)]
    [InlineData("SoftClassProperty", 0x28)]
    [InlineData("LazyObjectProperty", 0x20)]
    public void AfterTheCall_TheSlotDecodesAsTheWeakPair(string type, int size)
    {
        var buf = new byte[size];
        BitConverter.GetBytes(77).CopyTo(buf, 0);
        BitConverter.GetBytes(5).CopyTo(buf, 4);
        var p = new FunctionParamModel { Name = "Target", TypeName = type, Size = size, Offset = 0 };
        Assert.Equal("{index 77, serial 5}", InvokeParamDialog.DecodeParamValue(buf, p));
    }

    [Fact]
    public void AfterTheCall_WeakSerialZeroIsNull()
    {
        // UE's FWeakObjectPtr::Internal_GetObjectItem: serial 0 is null whatever the index says.
        var buf = new byte[8];
        BitConverter.GetBytes(12).CopyTo(buf, 0);
        var p = new FunctionParamModel { Name = "Target", TypeName = "WeakObjectProperty", Size = 8, Offset = 0 };
        Assert.Equal("null", InvokeParamDialog.DecodeParamValue(buf, p));
    }

    [Theory]
    [InlineData("SoftObjectProperty", 0x28)]
    [InlineData("SoftClassProperty", 0x28)]
    [InlineData("LazyObjectProperty", 0x20)]
    public void AfterTheCall_ASoftPointerWithAnUnsetWeakHalf_IsNotCalledNull(string type, int size)
    {
        // Review R1: a soft / lazy pointer is null only when its PATH / GUID is (TPersistentObjectPtr::IsNull is
        // !ObjectID.IsValid()). A soft reference to an asset not loaded yet -- the usual reason it is soft -- has the
        // weak half {0, 0} and a valid path. Calling it "null" told the user a valid reference was nothing.
        var buf = new byte[size];
        BitConverter.GetBytes(12).CopyTo(buf, 0);
        buf[0x10] = 0x5A;   // a path / GUID byte
        var p = new FunctionParamModel { Name = "Asset", TypeName = type, Size = size, Offset = 0 };
        Assert.Equal("{index 12, serial 0}", InvokeParamDialog.DecodeParamValue(buf, p));
    }

    // ---- Review R5: the CE paths print the same pair FIRE does, not a pointer ----

    [Theory]
    [InlineData("WeakObjectProperty", 8)]
    [InlineData("SoftObjectProperty", 0x28)]
    [InlineData("SoftClassProperty", 0x28)]
    [InlineData("LazyObjectProperty", 0x20)]
    public void CeReturnDecode_PrintsTheWeakPair(string type, int size)
    {
        var sb = new System.Text.StringBuilder();
        CeInvokeReturn.AppendDecodeAndPrint(sb, "C", "F", "Ret", type, size, 16, "_PD", "  ");
        var lua = sb.ToString();
        Assert.Contains("{index %d, serial %d}", lua);
        Assert.DoesNotContain("0x%X", lua);
        // The null rule is the weak pointer's only.
        Assert.Equal(type == "WeakObjectProperty", lua.Contains("= null", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("WeakObjectProperty", 8)]
    [InlineData("SoftObjectProperty", 0x28)]
    public void BakedVerify_PrintsTheWeakPair(string type, int size)
    {
        var ret = new BakedParamValue("ReturnValue", type, size, 16, "");
        var script = BakedScriptGenerator.Generate("C", "F", 0x40, new List<BakedParamValue>(), returnParam: ret,
                                                   verifyReturn: true);
        Assert.Contains("{index %d, serial %d}", script);
        Assert.DoesNotContain("readUFunctionReturn(16, 'qword')", script);
    }

    // ---- Review R6: Lazy says its own reason, and every path refuses a value in it ----

    [Fact]
    public void Lazy_RefusalSaysWhy()
    {
        Assert.False(ParamBufferBuilder.TryValidateScalar("LazyObjectProperty", 0x20, "0x1234", out var err));
        Assert.Contains("GUID", err);
    }

    [Fact]
    public void Lazy_TheCeFormGateSaysWhy()
    {
        var func = new FunctionInfoModel
        {
            Name = "Take",
            NumParms = 1,
            ParmsSize = 0x20,
            Params = { new FunctionParamModel { Name = "Ref", TypeName = "LazyObjectProperty", Size = 0x20, Offset = 0 } },
        };
        var script = InvokeScriptGenerator.Generate("C_C", "Take", func);
        Assert.Contains("GUID", script);
    }

    [Fact]
    public void Lazy_TheBakedHelperGetsItsOwnToken()
    {
        // 'pointer' made the Lua helper write the value as an address; 'lazy' is refused there unless it is 0.
        Assert.Equal("lazy", BakedScriptGenerator.MapToHelperType("LazyObjectProperty"));
    }

    // ---- Review R3: the DLL wiring no test target compiles, pinned by its source order ----

    private static string Src(string rel) => File.ReadAllText(NumericInputCoercionTests.RepoFile(rel));

    [Fact]
    public void Fern_ConvertsAfterTheHexOverlay_BeforeAnyFString_AndBeforeTheCall()
    {
        var s = Src("dll/src/Fern.cpp");
        int start = s.IndexOf("cmd == Renge::CMD_INVOKE_FUNCTION", StringComparison.Ordinal);
        Assert.True(start > 0);
        int overlay = s.IndexOf("memcpy(paramBuf.data(), hexBytes.data(), copyLen);", start, StringComparison.Ordinal);
        int convert = s.IndexOf("FrierenInvoke::PrepareObjectParams(", start, StringComparison.Ordinal);
        int strings = s.IndexOf("request.contains(\"str_params\")", start, StringComparison.Ordinal);
        int call = s.IndexOf("UE5_CallProcessEventDirect(instanceAddr", start, StringComparison.Ordinal);
        Assert.True(overlay > 0 && convert > overlay, "the conversion must see the caller's bytes");
        Assert.True(strings > convert, "a refusal must come before any FString is allocated");
        Assert.True(call > convert, "the conversion must come before ProcessEvent");
    }

    [Fact]
    public void Mimic_ConvertsBeforeBothCalls_AndRefusesWithItsOwnCode()
    {
        var s = Src("dll/src/Mimic.cpp");
        // The bodies, not the forward declarations above them.
        int start = s.IndexOf("static void HandleInvoke() {", StringComparison.Ordinal);
        int end = s.IndexOf("static void HandleInvokeByName() {", start, StringComparison.Ordinal);
        Assert.True(start > 0 && end > start);
        var body = s[start..end];
        int convert = body.IndexOf("FrierenInvoke::PrepareObjectParams(", StringComparison.Ordinal);
        Assert.True(convert > 0);
        Assert.True(body.IndexOf("UE5_CallProcessEventDirect(", StringComparison.Ordinal) > convert);
        Assert.True(body.IndexOf("UE5_CallProcessEventEx(", StringComparison.Ordinal) > convert);
        Assert.True(body.IndexOf("MB_ERR_INVOKE_OBJECT_PARAM", convert, StringComparison.Ordinal) > convert);
    }

    [Fact]
    public void AnObjectPointer_StillDecodesAsAnAddress()
    {
        // Control: a UObject* IS its address.
        var buf = BitConverter.GetBytes(0x1F2A3B4C5D0UL);
        var p = new FunctionParamModel { Name = "Ptr", TypeName = "ObjectProperty", Size = 8, Offset = 0 };
        Assert.Equal("0x1F2A3B4C5D0", InvokeParamDialog.DecodeParamValue(buf, p));
    }
}
