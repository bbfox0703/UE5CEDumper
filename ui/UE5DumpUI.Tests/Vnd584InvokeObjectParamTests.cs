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
    public void AfterTheCall_SerialZeroIsNull()
    {
        // UE's FWeakObjectPtr::Internal_GetObjectItem: serial 0 is null whatever the index says.
        var buf = new byte[0x28];
        BitConverter.GetBytes(12).CopyTo(buf, 0);
        var p = new FunctionParamModel { Name = "Asset", TypeName = "SoftObjectProperty", Size = 0x28, Offset = 0 };
        Assert.Equal("null", InvokeParamDialog.DecodeParamValue(buf, p));
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
