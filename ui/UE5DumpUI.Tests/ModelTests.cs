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
    public void HexViewRow_InitProperties()
    {
        var row = new HexViewRow
        {
            Offset = "00000100",
            HexPart = "48 65 6C 6C 6F",
            AsciiPart = "Hello",
        };

        Assert.Equal("00000100", row.Offset);
    }

    [Fact]
    public void ObjectListResult_DefaultObjectsList()
    {
        var result = new ObjectListResult();
        Assert.NotNull(result.Objects);
        Assert.Empty(result.Objects);
        Assert.Equal(0, result.Total);
    }
}
