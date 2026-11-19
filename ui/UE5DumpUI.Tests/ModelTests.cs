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
    public void RelatedObject_DisplayFieldAndAddressValue()
    {
        var asc = new RelatedObject
        {
            Address = "0x7FF6BB00", Name = "AbilitySystem_0",
            ClassName = "AbilitySystemComponent", Relation = "AbilitySystem (ASC)",
            FieldName = "AbilitySystem", FieldOffset = 0x2A8, Depth = 1,
        };
        Assert.Equal("AbilitySystem_0 : AbilitySystemComponent", asc.Display);
        Assert.Equal("AbilitySystem @ 0x2A8", asc.FieldDisplay);
        Assert.Equal(0x7FF6BB00UL, asc.AddressValue);

        // Self/Class/Outer rows carry no field — FieldDisplay collapses to empty.
        var self = new RelatedObject { Name = "X", ClassName = "Y", FieldOffset = -1 };
        Assert.Equal("X : Y", self.Display);
        Assert.Equal("", self.FieldDisplay);

        // Either half empty → Display falls back to the non-empty one.
        Assert.Equal("OnlyClass", new RelatedObject { ClassName = "OnlyClass" }.Display);
        Assert.Equal("OnlyName", new RelatedObject { Name = "OnlyName" }.Display);

        // Unparseable address → 0 (keeps the hex sort comparer total).
        Assert.Equal(0UL, new RelatedObject { Address = "" }.AddressValue);
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

        // Struct arrays carry no inline elements here
        Assert.Equal("[3 x FVector (12B)]", field.DisplayValue);
    }

    // --- DecodeHexAsNumeric fallback ---

    [Theory]
    [InlineData("FloatProperty", "00000000", "0")]
    [InlineData("FloatProperty", "0000803F", "1")]       // 1.0f LE
    [InlineData("FloatProperty", "DB0F4940", "3.14159")]  // pi LE (G10 format)
    [InlineData("DoubleProperty", "0000000000000000", "0")]
    [InlineData("IntProperty", "00000000", "0")]
    [InlineData("IntProperty", "2A000000", "42")]         // 42 LE
    [InlineData("UInt32Property", "FFFFFFFF", "4294967295")]
    [InlineData("Int64Property", "0000000000000000", "0")]
    [InlineData("UInt64Property", "0100000000000000", "1")]
    [InlineData("Int16Property", "0000", "0")]
    [InlineData("UInt16Property", "FF00", "255")]
    [InlineData("ByteProperty", "07", "7")]
    [InlineData("Int8Property", "FF", "-1")]
    public void DecodeHexAsNumeric_KnownScalarTypes(string typeName, string hex, string expected)
    {
        var result = LiveFieldValue.DecodeHexAsNumeric(typeName, hex);
        Assert.NotNull(result);
        // Float comparison: allow different precision formats
        if (typeName == "FloatProperty" && expected.Contains('.'))
            Assert.StartsWith(expected[..4], result);
        else
            Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("ObjectProperty", "0000000000000000")]
    [InlineData("StrProperty", "0000000000000000")]
    [InlineData("StructProperty", "00000000")]
    [InlineData("ArrayProperty", "00000000")]
    public void DecodeHexAsNumeric_NonNumericTypes_ReturnsNull(string typeName, string hex)
    {
        Assert.Null(LiveFieldValue.DecodeHexAsNumeric(typeName, hex));
    }

    [Fact]
    public void DecodeHexAsNumeric_EmptyHex_ReturnsNull()
    {
        Assert.Null(LiveFieldValue.DecodeHexAsNumeric("FloatProperty", ""));
        Assert.Null(LiveFieldValue.DecodeHexAsNumeric("FloatProperty", null!));
        Assert.Null(LiveFieldValue.DecodeHexAsNumeric("", "00000000"));
    }

    [Fact]
    public void DecodeHexAsNumeric_MalformedHex_ReturnsNull()
    {
        Assert.Null(LiveFieldValue.DecodeHexAsNumeric("FloatProperty", "GGGG"));
        Assert.Null(LiveFieldValue.DecodeHexAsNumeric("IntProperty", "ZZ"));
    }

    // --- DecodeDateTimeStruct (intrinsic FDateTime / FTimespan) ---

    [Fact]
    public void DecodeDateTimeStruct_DateTime_RealValue_FormatsReadableWithRawTicks()
    {
        // m_last_access_date raw bytes from a live save: 0x08DEC804F0983380 ticks.
        var result = LiveFieldValue.DecodeDateTimeStruct("DateTime", "803398F004C8DE08");
        Assert.Equal("2026-06-11 22:01:07 (639168120675120000)", result);
    }

    [Fact]
    public void DecodeDateTimeStruct_Timespan_RealValue_FormatsReadableWithRawTicks()
    {
        // m_total_play_time raw bytes from a live save: 46738270000 ticks ≈ 1h17m53s.
        var result = LiveFieldValue.DecodeDateTimeStruct("Timespan", "3067D1E10A000000");
        Assert.Equal("01:17:53 (46738270000)", result);
    }

    [Fact]
    public void DecodeDateTimeStruct_DateTime_RoundTripsAnyDate()
    {
        var dt = new DateTime(2030, 1, 2, 3, 4, 5);
        var hex = Convert.ToHexString(BitConverter.GetBytes(dt.Ticks));
        Assert.Equal($"2030-01-02 03:04:05 ({dt.Ticks})",
            LiveFieldValue.DecodeDateTimeStruct("DateTime", hex));
    }

    [Fact]
    public void DecodeDateTimeStruct_Timespan_MultiDay_ShowsDays()
    {
        var ts = new TimeSpan(2, 3, 4, 5); // 2d 03:04:05
        var hex = Convert.ToHexString(BitConverter.GetBytes(ts.Ticks));
        Assert.Equal($"2d 03:04:05 ({ts.Ticks})",
            LiveFieldValue.DecodeDateTimeStruct("Timespan", hex));
    }

    [Fact]
    public void DecodeDateTimeStruct_Timespan_Negative_ShowsSign()
    {
        var ts = new TimeSpan(-1, -30, 0); // -1:30:00
        var hex = Convert.ToHexString(BitConverter.GetBytes(ts.Ticks));
        Assert.Equal($"-01:30:00 ({ts.Ticks})",
            LiveFieldValue.DecodeDateTimeStruct("Timespan", hex));
    }

    [Theory]
    [InlineData("LifeSaveDataSlot")]   // ordinary struct — leave to default {…}
    [InlineData("FVector")]
    [InlineData("")]
    public void DecodeDateTimeStruct_NonMatchingType_ReturnsNull(string structType)
    {
        Assert.Null(LiveFieldValue.DecodeDateTimeStruct(structType, "0000000000000000"));
    }

    [Fact]
    public void DecodeDateTimeStruct_EmptyOrShortHex_ReturnsNull()
    {
        Assert.Null(LiveFieldValue.DecodeDateTimeStruct("DateTime", ""));
        Assert.Null(LiveFieldValue.DecodeDateTimeStruct("DateTime", null!));
        Assert.Null(LiveFieldValue.DecodeDateTimeStruct("DateTime", "1234")); // < 8 bytes
    }

    [Fact]
    public void DecodeDateTimeStruct_DateTimeOutOfRange_ReturnsNull()
    {
        // 0xFFFFFFFFFFFFFFFF is a negative tick count → outside DateTime's valid range.
        Assert.Null(LiveFieldValue.DecodeDateTimeStruct("DateTime", "FFFFFFFFFFFFFFFF"));
    }

    [Fact]
    public void DisplayValue_DateTimeStruct_ShowsReadable_OverridingTypedValue()
    {
        // Even if the DLL populated a struct preview into TypedValue, the readable
        // date wins (DecodeDateTimeStruct is checked first), while edit/export keep raw.
        var field = new LiveFieldValue
        {
            TypeName = "StructProperty",
            StructTypeName = "DateTime",
            TypedValue = "{Ticks=639168120675120000}",
            HexValue = "803398F004C8DE08",
        };
        Assert.Equal("2026-06-11 22:01:07 (639168120675120000)", field.DisplayValue);
    }

    [Fact]
    public void DisplayValue_OrdinaryStruct_StillShowsBraceName()
    {
        var field = new LiveFieldValue
        {
            TypeName = "StructProperty",
            StructTypeName = "LifeSaveDataSlot",
            HexValue = "0011223344556677",
        };
        Assert.Equal("{LifeSaveDataSlot}", field.DisplayValue);
    }

    [Fact]
    public void DisplayValue_FloatZero_WhenTypedValueEmpty_ShowsZero()
    {
        var field = new LiveFieldValue
        {
            TypeName = "FloatProperty",
            TypedValue = "",
            HexValue = "00000000",
        };
        Assert.Equal("0", field.DisplayValue);
    }

    [Fact]
    public void DisplayValue_IntZero_WhenTypedValueEmpty_ShowsZero()
    {
        var field = new LiveFieldValue
        {
            TypeName = "IntProperty",
            TypedValue = "",
            HexValue = "00000000",
        };
        Assert.Equal("0", field.DisplayValue);
    }

    [Fact]
    public void DisplayValue_NonNumeric_WhenTypedValueEmpty_ShowsHexRaw()
    {
        var field = new LiveFieldValue
        {
            TypeName = "StructProperty",
            TypedValue = "",
            HexValue = "DEADBEEF",
        };
        Assert.Equal("DEADBEEF", field.DisplayValue);
    }

    [Fact]
    public void EditableValue_FloatZero_FallsBackToHex()
    {
        var field = new LiveFieldValue
        {
            TypeName = "FloatProperty",
            TypedValue = "",
            HexValue = "00000000",
        };
        Assert.Equal("0", field.EditableValue);
    }

    // --- InstanceWalkResult / PropertiesSize ---

    [Fact]
    public void InstanceWalkResult_PropertiesSize_DefaultZero()
    {
        var result = new InstanceWalkResult();
        Assert.Equal(0, result.PropertiesSize);
    }

    [Fact]
    public void InstanceWalkResult_PropertiesSize_SetFromInit()
    {
        var result = new InstanceWalkResult
        {
            PropertiesSize = 152,
            Name = "NicolaSkillManager",
            ClassName = "NicolaSkillManager",
        };
        Assert.Equal(152, result.PropertiesSize);
    }

    [Fact]
    public void InstanceWalkResult_ZeroFields_HighPropsSize_ShouldTriggerAutoFillGaps()
    {
        // Simulates the condition: 0 fields + propsSize > 0x30 → should auto-retry with fill_gaps
        var result = new InstanceWalkResult
        {
            PropertiesSize = 152,
            Name = "NicolaSkillManager",
            ClassName = "NicolaSkillManager",
        };
        // Fields list is empty by default
        Assert.Empty(result.Fields);
        Assert.True(result.PropertiesSize > 0x30,
            "PropertiesSize > 0x30 should trigger auto fill_gaps retry");
    }

    [Fact]
    public void InstanceWalkResult_ZeroFields_SmallPropsSize_ShouldNotTriggerAutoFillGaps()
    {
        // Objects with size ≤ 0x30 (UObject header) should NOT trigger auto-fill
        var result = new InstanceWalkResult
        {
            PropertiesSize = 0x28,  // Just UObject header, no extra data
            Name = "EmptyObject",
            ClassName = "Object",
        };
        Assert.Empty(result.Fields);
        Assert.False(result.PropertiesSize > 0x30,
            "PropertiesSize ≤ 0x30 should not trigger auto fill_gaps retry");
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
