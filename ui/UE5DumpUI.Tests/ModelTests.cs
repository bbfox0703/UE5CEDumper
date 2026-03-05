using UE5DumpUI.Models;
using Xunit;

namespace UE5DumpUI.Tests;

public class ModelTests
{
    [Fact]
    public void UObjectNode_DefaultValues()
    {
        var node = new UObjectNode();

        Assert.Equal("", node.Address);
        Assert.Equal("", node.Name);
        Assert.Equal("", node.ClassName);
        Assert.Equal("", node.OuterAddr);
        Assert.False(node.IsExpanded);
        Assert.Empty(node.Children);
    }

    [Fact]
    public void UObjectNode_InitProperties()
    {
        var node = new UObjectNode
        {
            Address = "0x7FF123456789",
            Name = "TestObject",
            ClassName = "Actor",
            OuterAddr = "0x7FF000000000",
        };

        Assert.Equal("0x7FF123456789", node.Address);
        Assert.Equal("TestObject", node.Name);
        Assert.Equal("Actor", node.ClassName);
    }

    [Fact]
    public void FieldInfoModel_InitProperties()
    {
        var field = new FieldInfoModel
        {
            Address = "0x100",
            Name = "Health",
            TypeName = "FloatProperty",
            Offset = 720,
            Size = 4,
        };

        Assert.Equal("Health", field.Name);
        Assert.Equal(720, field.Offset);
        Assert.Equal(4, field.Size);
    }

    [Fact]
    public void ClassInfoModel_DefaultFieldsList()
    {
        var ci = new ClassInfoModel();
        Assert.NotNull(ci.Fields);
        Assert.Empty(ci.Fields);
    }

    [Fact]
    public void EngineState_InitProperties()
    {
        var state = new EngineState
        {
            UEVersion = 504,
            GObjectsAddr = "0x7FF600A12340",
            GNamesAddr = "0x7FF600B56780",
            ObjectCount = 58432,
        };

        Assert.Equal(504, state.UEVersion);
        Assert.Equal(58432, state.ObjectCount);
    }

    [Fact]
    public void ObjectListResult_DefaultObjectsList()
    {
        var result = new ObjectListResult();
        Assert.NotNull(result.Objects);
        Assert.Empty(result.Objects);
        Assert.Equal(0, result.Total);
    }

    [Fact]
    public void LiveFieldValue_DisplayValue_ArrayWithElements()
    {
        var field = new LiveFieldValue
        {
            ArrayCount = 3,
            ArrayInnerType = "FloatProperty",
            ArrayElemSize = 4,
            ArrayElements = new List<ArrayElementValue>
            {
                new() { Index = 0, Value = "1.5" },
                new() { Index = 1, Value = "2" },
                new() { Index = 2, Value = "3.75" },
            },
        };

        Assert.Equal("[3 x FloatProperty (4B)] = [1.5, 2, 3.75]", field.DisplayValue);
    }

    [Fact]
    public void LiveFieldValue_DisplayValue_ArrayWithEnumElements()
    {
        var field = new LiveFieldValue
        {
            ArrayCount = 2,
            ArrayInnerType = "EnumProperty",
            ArrayElemSize = 4,
            ArrayElements = new List<ArrayElementValue>
            {
                new() { Index = 0, Value = "0", EnumName = "ROLE_Authority" },
                new() { Index = 1, Value = "2", EnumName = "ROLE_SimulatedProxy" },
            },
        };

        Assert.Equal("[2 x EnumProperty (4B)] = [ROLE_Authority, ROLE_SimulatedProxy]", field.DisplayValue);
    }

    [Fact]
    public void LiveFieldValue_DisplayValue_ArrayWithMoreThanPreview()
    {
        var elems = new List<ArrayElementValue>();
        for (int i = 0; i < 10; i++)
            elems.Add(new ArrayElementValue { Index = i, Value = i.ToString() });

        var field = new LiveFieldValue
        {
            ArrayCount = 10,
            ArrayInnerType = "IntProperty",
            ArrayElemSize = 4,
            ArrayElements = elems,
        };

        Assert.Equal("[10 x IntProperty (4B)] = [0, 1, 2, 3, 4, ...]", field.DisplayValue);
    }

    [Fact]
    public void LiveFieldValue_DisplayValue_ArrayNoElements_FallsBackToTypeInfo()
    {
        var field = new LiveFieldValue
        {
            ArrayCount = 5,
            ArrayInnerType = "FloatProperty",
            ArrayElemSize = 4,
        };

        Assert.Equal("[5 x FloatProperty (4B)]", field.DisplayValue);
    }

    [Fact]
    public void LiveFieldValue_DisplayValue_EmptyArray()
    {
        var field = new LiveFieldValue
        {
            ArrayCount = 0,
            ArrayInnerType = "FloatProperty",
            ArrayElemSize = 4,
        };

        Assert.Equal("[0 x FloatProperty (4B)]", field.DisplayValue);
    }

    [Fact]
    public void LiveFieldValue_DisplayValue_ArrayStructType()
    {
        var field = new LiveFieldValue
        {
            ArrayCount = 3,
            ArrayInnerType = "StructProperty",
            ArrayStructType = "FVector",
            ArrayElemSize = 12,
        };

        // Struct arrays don't have inline elements in Phase B
        Assert.Equal("[3 x FVector (12B)]", field.DisplayValue);
    }

    // --- InvokeFunctionResult ---

    [Fact]
    public void InvokeFunctionResult_DefaultValues()
    {
        var result = new InvokeFunctionResult();

        Assert.Equal(0, result.Result);
        Assert.Equal("", result.InstanceAddr);
        Assert.Equal("", result.FuncAddr);
        Assert.Equal(0, result.ParmsSize);
        Assert.Equal("", result.ResultHex);
        Assert.Equal("", result.Message);
        Assert.Equal("", result.Error);
        Assert.True(result.Success);  // Result=0 and Error=""
    }

    [Fact]
    public void InvokeFunctionResult_Success_WhenResultIsZero()
    {
        var result = new InvokeFunctionResult
        {
            Result = 0,
            Message = "ProcessEvent OK",
        };

        Assert.True(result.Success);
    }

    [Fact]
    public void InvokeFunctionResult_NotSuccess_WhenResultNonZero()
    {
        var result = new InvokeFunctionResult
        {
            Result = -2,
            Error = "vtable read failed",
        };

        Assert.False(result.Success);
    }

    [Fact]
    public void InvokeFunctionResult_NotSuccess_WhenErrorSet()
    {
        var result = new InvokeFunctionResult
        {
            Result = 0,
            Error = "Something went wrong",
        };

        Assert.False(result.Success);
    }
}
