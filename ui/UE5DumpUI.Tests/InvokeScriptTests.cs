using UE5DumpUI.Models;
using UE5DumpUI.Services;
using Xunit;

namespace UE5DumpUI.Tests;

public class InvokeScriptTests
{
    // --- FunctionInfoModel.DecodeFunctionFlags ---

    [Fact]
    public void DecodeFunctionFlags_Native_ReturnsNative()
    {
        var result = FunctionInfoModel.DecodeFunctionFlags(0x0000_0400);
        Assert.Contains("Native", result);
    }

    [Fact]
    public void DecodeFunctionFlags_BlueprintCallable_Returns()
    {
        var result = FunctionInfoModel.DecodeFunctionFlags(0x0400_0000);
        Assert.Contains("BlueprintCallable", result);
    }

    [Fact]
    public void DecodeFunctionFlags_MultipleFlags_ReturnsAll()
    {
        // Native(0x400) | BlueprintCallable(0x4000000) | Static(0x2000)
        var result = FunctionInfoModel.DecodeFunctionFlags(0x0400_2400);
        Assert.Contains("Native", result);
        Assert.Contains("BlueprintCallable", result);
        Assert.Contains("Static", result);
    }

    [Fact]
    public void DecodeFunctionFlags_Zero_ReturnsEmpty()
    {
        var result = FunctionInfoModel.DecodeFunctionFlags(0);
        Assert.Equal("", result);
    }

    // --- InvokeScriptGenerator ---

    [Fact]
    public void Generate_NoParams_ProducesDirectInvoke()
    {
        var func = new FunctionInfoModel
        {
            Name = "openShop",
            Params = new(),
        };

        var script = InvokeScriptGenerator.Generate("ShopKeeper_C", "openShop", func);

        Assert.Contains("[ENABLE]", script);
        Assert.Contains("[DISABLE]", script);
        Assert.Contains("OWNER_CLASS", script);
        Assert.Contains("\"ShopKeeper_C\"", script);
        Assert.Contains("\"openShop\"", script);
        Assert.Contains("UE_InvokeActorEvent", script);
        // No form creation for zero-param functions
        Assert.DoesNotContain("createForm", script);
    }

    [Fact]
    public void Generate_WithParams_ProducesForm()
    {
        var func = new FunctionInfoModel
        {
            Name = "addMoney",
            NumParms = 3,
            ParmsSize = 6,
            Params = new List<FunctionParamModel>
            {
                new() { Name = "Amount", TypeName = "IntProperty", Size = 4, Offset = 0 },
                new() { Name = "SkipCounting", TypeName = "BoolProperty", Size = 1, Offset = 4 },
                new() { Name = "Success", TypeName = "BoolProperty", Size = 1, Offset = 5, IsOut = true },
            },
        };

        var script = InvokeScriptGenerator.Generate("playerCharacterBP_C", "addMoney", func);

        Assert.Contains("createForm", script);
        Assert.Contains("\"Amount", script);
        Assert.Contains("\"SkipCounting", script);
        Assert.Contains("\"Success", script);
        Assert.Contains("szDword", script); // IntProperty -> szDword
        Assert.Contains("szByte", script);  // BoolProperty -> szByte
        Assert.Contains("FIRE", script);
    }

    [Fact]
    public void Generate_ReturnParam_ExcludedFromForm()
    {
        var func = new FunctionInfoModel
        {
            Name = "getValue",
            ReturnType = "IntProperty",
            Params = new List<FunctionParamModel>
            {
                new() { Name = "ReturnValue", TypeName = "IntProperty", Size = 4, Offset = 0, IsReturn = true },
            },
        };

        var script = InvokeScriptGenerator.Generate("TestClass", "getValue", func);

        // Return param should not appear in form or param table
        Assert.DoesNotContain("createForm", script); // 0 input params = no form
        Assert.Contains("params = {}", script); // empty params
    }

    [Fact]
    public void Generate_PointerParam_UsesHexParsing()
    {
        var func = new FunctionInfoModel
        {
            Name = "setTarget",
            Params = new List<FunctionParamModel>
            {
                new() { Name = "Target", TypeName = "ObjectProperty", Size = 8, Offset = 0 },
            },
        };

        var script = InvokeScriptGenerator.Generate("AI_C", "setTarget", func);

        Assert.Contains("szQword", script);
        Assert.Contains("0x0", script); // default for pointer
        Assert.Contains("0x", script);  // hex-aware parsing
    }

    [Fact]
    public void Generate_FloatParam_UsesTonumber()
    {
        var func = new FunctionInfoModel
        {
            Name = "setSpeed",
            Params = new List<FunctionParamModel>
            {
                new() { Name = "Speed", TypeName = "FloatProperty", Size = 4, Offset = 0 },
            },
        };

        var script = InvokeScriptGenerator.Generate("Character_C", "setSpeed", func);

        Assert.Contains("szFloat", script);
        Assert.Contains("0.0", script); // default for float
    }

    [Fact]
    public void Generate_SpecialCharsInName_Escaped()
    {
        var func = new FunctionInfoModel
        {
            Name = "K2_OnReset",
            Params = new(),
        };

        var script = InvokeScriptGenerator.Generate("BP_Base\"_C", "K2_OnReset", func);

        // Double quote in class name should be escaped
        Assert.Contains("BP_Base\\\"_C", script);
    }

    // --- InputParams property ---

    [Fact]
    public void InputParams_ExcludesReturnParam()
    {
        var func = new FunctionInfoModel
        {
            Params = new List<FunctionParamModel>
            {
                new() { Name = "A", IsReturn = false },
                new() { Name = "B", IsReturn = false },
                new() { Name = "ReturnValue", IsReturn = true },
            },
        };

        var input = func.InputParams.ToList();
        Assert.Equal(2, input.Count);
        Assert.DoesNotContain(input, p => p.Name == "ReturnValue");
    }
}
