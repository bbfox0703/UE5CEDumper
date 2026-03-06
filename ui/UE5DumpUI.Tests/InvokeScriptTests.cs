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

    // --- InvokeScriptGenerator: Mailbox-based scripts ---

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
        // Mailbox-based invocation
        Assert.Contains("g_invokeMailbox", script);
        Assert.Contains("waitDone", script);
        // No form creation for zero-param functions
        Assert.DoesNotContain("createForm", script);
        // No executeCodeEx (mailbox uses ReadProcessMemory/WriteProcessMemory)
        Assert.DoesNotContain("executeCodeEx", script);
        Assert.DoesNotContain("dllCall", script);
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
        // Mailbox param writes use PD (params_data base)
        Assert.Contains("writeInteger(PD +", script);
        Assert.Contains("writeBytes(PD +", script);
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

        // Return param should not appear in form — treated as no-param direct invoke
        Assert.DoesNotContain("createForm", script);
        Assert.Contains("waitDone", script);
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

        Assert.Contains("writeQword", script); // 8-byte pointer write
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

        Assert.Contains("writeFloat", script); // float write
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

    // --- Mailbox-specific tests ---

    [Fact]
    public void Generate_UsesMailboxApproach()
    {
        var func = new FunctionInfoModel
        {
            Name = "test",
            Params = new(),
        };

        var script = InvokeScriptGenerator.Generate("TestClass", "test", func);

        // Should find mailbox symbol
        Assert.Contains("g_invokeMailbox", script);
        // Should use mailbox helpers
        Assert.Contains("writeMbStr", script);
        Assert.Contains("waitDone", script);
        Assert.Contains("readErr", script);
        // Should NOT use executeCodeEx or DLL call helpers
        Assert.DoesNotContain("executeCodeEx", script);
        Assert.DoesNotContain("dllCall", script);
        Assert.DoesNotContain("dllCallPtr", script);
        Assert.DoesNotContain("cstr(", script);
        // Should NOT contain third-party CE plugin references
        Assert.DoesNotContain("UE_InvokeActorEvent", script);
        Assert.DoesNotContain("UE_GetAllObjectsOfClass", script);
        Assert.DoesNotContain("UE_GetFunctionsOfObject", script);
    }

    [Fact]
    public void Generate_MailboxCommands_CorrectSequence()
    {
        var func = new FunctionInfoModel
        {
            Name = "openShop",
            Params = new(),
        };

        var script = InvokeScriptGenerator.Generate("ShopKeeper_C", "openShop", func);

        // CMD_FIND_INSTANCE = 2
        Assert.Contains("writeInteger(mb + 0x000, 2)", script);
        // CMD_FIND_FUNCTION = 3
        Assert.Contains("writeInteger(mb + 0x000, 3)", script);
        // CMD_INVOKE = 1
        Assert.Contains("writeInteger(mb + 0x000, 1)", script);
        // Reads instance + function addresses from mailbox
        Assert.Contains("readQword(mb + 0x010)", script);  // instanceAddr
        Assert.Contains("readQword(mb + 0x018)", script);  // ufuncPtr
        // Reads result code
        Assert.Contains("readInteger(mb + 0x008)", script); // result
    }

    [Fact]
    public void Generate_NoExecuteCodeEx()
    {
        var func = new FunctionInfoModel
        {
            Name = "openShop",
            Params = new(),
        };

        var script = InvokeScriptGenerator.Generate("ShopKeeper_C", "openShop", func);

        // The whole point of mailbox: no executeCodeEx / CreateRemoteThread
        Assert.DoesNotContain("executeCodeEx", script);
        Assert.DoesNotContain("CreateRemoteThread", script);
        // Comment explains this
        Assert.Contains("shared memory mailbox", script);
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

        // Should write params at correct offsets via PD (params_data base)
        Assert.Contains("writeInteger(PD + 0,", script);
        Assert.Contains("writeFloat(PD + 4,", script);
        Assert.Contains("writeBytes(PD + 8,", script);
        // Return param should NOT be written
        Assert.DoesNotContain("PD + 9", script);
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
        // Mailbox zero-fill uses PD base
        Assert.Contains("PD + i, 0", script);
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
