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

    // --- InvokeScriptGenerator: Dual-path support ---

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
        Assert.Contains("'ShopKeeper_C'", script);
        Assert.Contains("'openShop'", script);
        // DLL path
        Assert.Contains("UE5_CallProcessEvent", script);
        // CE UE fallback path
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
        Assert.Contains("'Amount", script);
        Assert.Contains("'SkipCounting", script);
        Assert.Contains("'Success", script);
        // CE path uses size types
        Assert.Contains("szDword", script);
        Assert.Contains("szByte", script);
        // DLL path uses memory writes
        Assert.Contains("writeInteger(pBuf +", script);
        Assert.Contains("writeBytes(pBuf +", script);
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
        Assert.DoesNotContain("createForm", script);
        Assert.Contains("params = {}", script); // CE path: empty params
    }

    [Fact]
    public void Generate_PointerParam_UsesHexParsing()
    {
        var func = new FunctionInfoModel
        {
            Name = "setTarget",
            ParmsSize = 8,
            Params = new List<FunctionParamModel>
            {
                new() { Name = "Target", TypeName = "ObjectProperty", Size = 8, Offset = 0 },
            },
        };

        var script = InvokeScriptGenerator.Generate("AI_C", "setTarget", func);

        Assert.Contains("szQword", script);   // CE path
        Assert.Contains("writeQword", script); // DLL path
        Assert.Contains("0x0", script);        // default for pointer
    }

    [Fact]
    public void Generate_FloatParam_UsesTonumber()
    {
        var func = new FunctionInfoModel
        {
            Name = "setSpeed",
            ParmsSize = 4,
            Params = new List<FunctionParamModel>
            {
                new() { Name = "Speed", TypeName = "FloatProperty", Size = 4, Offset = 0 },
            },
        };

        var script = InvokeScriptGenerator.Generate("Character_C", "setSpeed", func);

        Assert.Contains("szFloat", script);    // CE path
        Assert.Contains("writeFloat", script); // DLL path
        Assert.Contains("0.0", script);        // default for float
    }

    [Fact]
    public void Generate_SpecialCharsInName_Escaped()
    {
        var func = new FunctionInfoModel
        {
            Name = "K2_OnReset",
            Params = new(),
        };

        var script = InvokeScriptGenerator.Generate("BP_Base'_C", "K2_OnReset", func);

        // Single quote in class name should be escaped for Lua
        Assert.Contains("BP_Base\\'_C", script);
    }

    [Fact]
    public void Generate_UsesLfLineEndings()
    {
        var func = new FunctionInfoModel
        {
            Name = "test",
            Params = new(),
        };

        var script = InvokeScriptGenerator.Generate("TestClass", "test", func);

        Assert.Contains("\n", script);
        Assert.DoesNotContain("\r", script);
    }

    [Fact]
    public void Generate_UsesAsciiOnly()
    {
        var func = new FunctionInfoModel
        {
            Name = "test",
            Params = new(),
        };

        var script = InvokeScriptGenerator.Generate("TestClass", "test", func);

        Assert.DoesNotContain("\u2014", script);
        Assert.DoesNotContain("\u2192", script);
        Assert.All(script, c => Assert.True(c < 128, $"Non-ASCII char found: U+{(int)c:X4}"));
    }

    [Fact]
    public void Generate_UsesSingleQuotesForLuaStrings()
    {
        var func = new FunctionInfoModel
        {
            Name = "openShop",
            Params = new(),
        };

        var script = InvokeScriptGenerator.Generate("ShopKeeper_C", "openShop", func);

        Assert.Contains("'ShopKeeper_C'", script);
        Assert.Contains("'openShop'", script);
        Assert.DoesNotContain("\"ShopKeeper_C\"", script);
        Assert.DoesNotContain("\"openShop\"", script);
    }

    // --- Dual-path specific tests ---

    [Fact]
    public void Generate_ContainsDllMethodDetection()
    {
        var func = new FunctionInfoModel
        {
            Name = "test",
            Params = new(),
        };

        var script = InvokeScriptGenerator.Generate("TestClass", "test", func);

        // Should contain DLL detection logic
        Assert.Contains("USE_DLL", script);
        Assert.Contains("getAddress('UE5_FindInstanceOfClass')", script);
        // Should contain both dllCall (int return) and dllCallPtr (pointer return)
        Assert.Contains("dllCall(", script);
        Assert.Contains("dllCallPtr(", script);
        // Should contain CE fallback
        Assert.Contains("UseUE", script);
        Assert.Contains("UE_IsConnected", script);
    }

    [Fact]
    public void Generate_DllPath_UsesProcessEvent()
    {
        var func = new FunctionInfoModel
        {
            Name = "openShop",
            Params = new(),
        };

        var script = InvokeScriptGenerator.Generate("ShopKeeper_C", "openShop", func);

        // DLL path uses our exports (pointer returns via dllCallPtr)
        Assert.Contains("dllCallPtr('UE5_FindInstanceOfClass'", script);
        Assert.Contains("dllCallPtr('UE5_GetObjectClass'", script);
        Assert.Contains("dllCallPtr('UE5_FindFunctionByName'", script);
        Assert.Contains("dllCall('UE5_CallProcessEvent'", script);  // int32 return
        // Auto-init: if DLL loaded but not initialized, call UE5_Init
        Assert.Contains("UE5_Init", script);
        Assert.Contains("DLL not initialized", script);
        // executeCodeEx canary in method detection — auto-fallback to CE UE tools
        Assert.Contains("UE5_GetVersion", script);
        Assert.Contains("executeCodeEx returns nil", script);
        Assert.Contains("use PIPE button in DumperUI instead", script);
        // Helper functions
        Assert.Contains("dllCallPtr", script);
        Assert.Contains("cstr", script);
        // Null-terminator safety
        Assert.Contains("writeBytes(buf + #s, {0})", script);
    }

    [Fact]
    public void Generate_ParamBuffer_IncludesParmsSize()
    {
        var func = new FunctionInfoModel
        {
            Name = "addMoney",
            ParmsSize = 42,
            Params = new List<FunctionParamModel>
            {
                new() { Name = "Amount", TypeName = "IntProperty", Size = 4, Offset = 0 },
            },
        };

        var script = InvokeScriptGenerator.Generate("TestClass", "addMoney", func);

        // PARMS_SIZE embedded in script
        Assert.Contains("PARMS_SIZE   = 42", script);
        // DLL path allocates buffer
        Assert.Contains("allocateMemory(42)", script);
        Assert.Contains("deAlloc(pBuf)", script);
    }

    [Fact]
    public void Generate_ParamOffsets_WrittenCorrectly()
    {
        var func = new FunctionInfoModel
        {
            Name = "doThing",
            ParmsSize = 13,
            Params = new List<FunctionParamModel>
            {
                new() { Name = "X", TypeName = "IntProperty", Size = 4, Offset = 0 },
                new() { Name = "Y", TypeName = "FloatProperty", Size = 4, Offset = 4 },
                new() { Name = "Flag", TypeName = "BoolProperty", Size = 1, Offset = 8 },
                new() { Name = "Ptr", TypeName = "ObjectProperty", Size = 8, Offset = 9, IsReturn = true },
            },
        };

        var script = InvokeScriptGenerator.Generate("TestClass", "doThing", func);

        // DLL path should write params at correct offsets (Return param excluded)
        Assert.Contains("writeInteger(pBuf + 0,", script);
        Assert.Contains("writeFloat(pBuf + 4,", script);
        Assert.Contains("writeBytes(pBuf + 8,", script);
        // Return param should NOT be written
        Assert.DoesNotContain("pBuf + 9", script);
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
