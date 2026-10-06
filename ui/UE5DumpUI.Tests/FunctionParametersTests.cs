using UE5DumpUI.Models;
using UE5DumpUI.Services;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// [FUNCPARM-CONSUMERS] walk_functions lists a UFunction's whole property chain, and on a Blueprint function its
/// locals follow the parameters there. In 2026-08 (Y1, TRAP 1) the Invoke form offered two of them,
/// <c>CallFunc_Conv_SoftObjectReferenceToObject_ReturnValue</c> at offset 8 and <c>K2Node_DynamicCast_AsActor</c>
/// at 16, as arguments of a function whose parmsSize was 8. Whatever means "the arguments" must take the entries
/// the DLL flags as CPF_Parm, or, from a DLL that predates the flag, UE's own definition: the leading NumParms.
/// </summary>
public class FunctionParametersTests
{
    internal static readonly string[] LocalNames =
    {
        "CallFunc_Conv_SoftObjectReferenceToObject_ReturnValue", "K2Node_DynamicCast_AsActor", "Temp_int_Variable",
    };

    /// <summary>OnUse: one argument and the return inside ParmsSize, then three Blueprint locals past it.
    /// <paramref name="flagged"/> false = a DLL that sends no <c>parm</c> key.</summary>
    internal static FunctionInfoModel BlueprintFunction(bool flagged)
    {
        bool? Parm(bool isParm) => flagged ? isParm : null;
        return new FunctionInfoModel
        {
            Name = "OnUse", Address = "0x7FF600001000", NumParms = 2, ParmsSize = 9, ReturnType = "BoolProperty",
            Params = new()
            {
                new() { Name = "User", TypeName = "ObjectProperty", Offset = 0, Size = 8, ObjectClassName = "Pawn",
                        IsParm = Parm(true) },
                new() { Name = "ReturnValue", TypeName = "BoolProperty", Offset = 8, Size = 1,
                        IsOut = true, IsReturn = true, IsParm = Parm(true) },
                new() { Name = LocalNames[0], TypeName = "ObjectProperty", Offset = 16, Size = 8,
                        ObjectClassName = "Object", IsParm = Parm(false) },
                new() { Name = LocalNames[1], TypeName = "ObjectProperty", Offset = 24, Size = 8,
                        ObjectClassName = "Actor", IsParm = Parm(false) },
                new() { Name = LocalNames[2], TypeName = "IntProperty", Offset = 32, Size = 4, IsParm = Parm(false) },
            },
        };
    }

    /// <summary>Recalc: no parameter at all, one local, which UE lays out from offset 0.</summary>
    internal static FunctionInfoModel LocalsOnlyFunction(bool flagged) => new()
    {
        Name = "Recalc", Address = "0x7FF600002000", NumParms = 0, ParmsSize = 0,
        Params = new()
        {
            new() { Name = LocalNames[2], TypeName = "IntProperty", Offset = 0, Size = 4,
                    IsParm = flagged ? false : null },
        },
    };

    private static string[] Names(IEnumerable<FunctionParamModel> ps) => ps.Select(p => p.Name).ToArray();

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void The_parameters_are_the_argument_and_the_return_never_a_local(bool flagged)
    {
        var fn = BlueprintFunction(flagged);

        Assert.Equal(new[] { "User", "ReturnValue" }, Names(fn.Parameters));
        Assert.Equal(new[] { "User" }, Names(fn.InputParams));
    }

    [Fact]
    public void The_flag_wins_over_a_misread_NumParms()
    {
        var fn = BlueprintFunction(flagged: true);
        var misread = new FunctionInfoModel { Name = fn.Name, NumParms = 5, ParmsSize = fn.ParmsSize, Params = fn.Params };

        Assert.Equal(new[] { "User", "ReturnValue" }, Names(misread.Parameters));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_function_with_only_locals_has_no_parameters(bool flagged)
    {
        var fn = LocalsOnlyFunction(flagged);

        Assert.Empty(fn.Parameters);
        Assert.Empty(fn.InputParams);
    }

    [Fact]
    public void Only_an_unflagged_non_empty_chain_falls_back_on_NumParms()
    {
        Assert.False(BlueprintFunction(flagged: true).ParametersFromNumParms);
        Assert.True(BlueprintFunction(flagged: false).ParametersFromNumParms);
        Assert.False(new FunctionInfoModel { Name = "Tick" }.ParametersFromNumParms);
    }

    // --- the CE Invoke script's form: one box per argument ---

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void The_invoke_script_offers_the_argument_and_no_local(bool flagged)
    {
        var script = InvokeScriptGenerator.Generate("BP_Door_C", "OnUse", BlueprintFunction(flagged));

        Assert.Contains("local PARAM_COUNT = 1", script, StringComparison.Ordinal);
        Assert.Contains("'User", script, StringComparison.Ordinal);
        foreach (var local in LocalNames)
            Assert.DoesNotContain(local, script, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_function_with_only_locals_is_invoked_without_a_form(bool flagged)
    {
        var script = InvokeScriptGenerator.Generate("BP_Door_C", "Recalc", LocalsOnlyFunction(flagged));

        Assert.DoesNotContain("createForm", script, StringComparison.Ordinal);
        Assert.DoesNotContain(LocalNames[2], script, StringComparison.Ordinal);
    }

    // --- the span a call needs: what the script zero-fills, and what decides the slab refusal ---

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void The_span_a_call_needs_ends_at_the_parameters(bool flagged)
    {
        Assert.Equal(9, InvokeScriptGenerator.RequiredSpan(BlueprintFunction(flagged)));
        Assert.Equal(0, InvokeScriptGenerator.RequiredSpan(LocalsOnlyFunction(flagged)));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_local_past_the_mailbox_slab_does_not_refuse_a_call_whose_parameters_fit(bool flagged)
    {
        var fn = BlueprintFunction(flagged);
        var withBigLocal = new FunctionInfoModel
        {
            Name = fn.Name, NumParms = fn.NumParms, ParmsSize = fn.ParmsSize, ReturnType = fn.ReturnType,
            Params = fn.Params.Append(new FunctionParamModel
            {
                Name = "K2Node_MakeStruct_HitResult", TypeName = "StructProperty", Offset = 40,
                Size = CeMailboxLayout.ParamsDataBytes, IsParm = flagged ? false : null,
            }).ToList(),
        };

        var script = InvokeScriptGenerator.Generate("BP_Door_C", "OnUse", withBigLocal);

        Assert.DoesNotContain("bytes of parameters, but the mailbox holds", script, StringComparison.Ordinal);
        Assert.Contains("btnFire", script, StringComparison.Ordinal);
        // The zero-fill still covers the whole walked chain, clamped to the slab: zeroing bytes ProcessEvent never
        // copies costs nothing, and it must not depend on which entries are parameters.
        Assert.Contains($"for i = 0, {CeMailboxLayout.ParamsDataBytes - 1} do writeByte(PD + i, 0) end", script,
            StringComparison.Ordinal);
    }

    [Fact]
    public void The_zero_fill_covers_every_walked_entry_even_when_NumParms_was_misread()
    {
        // [A3-CEFORM-4X-STALESLAB], review of 2bc914dc: with a DLL older than 3622 the parameters come from NumParms,
        // a tail read older DLLs have misread (0 here, and ParmsSize with it). The zero-fill must not depend on it:
        // the callee frees whatever Data pointer it finds in an out-FString slot left dirty.
        var fn = new FunctionInfoModel
        {
            Name = "SetName", NumParms = 0, ParmsSize = 0,
            Params = new()
            {
                new() { Name = "Id", TypeName = "IntProperty", Offset = 0, Size = 4 },
                new() { Name = "OutName", TypeName = "StrProperty", Offset = 8, Size = 16, IsOut = true },
            },
        };

        Assert.Equal(24, InvokeScriptGenerator.ZeroFillSpan(fn));
    }

    // --- the SDK header's function signature ---

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void The_SDK_signature_lists_the_argument_and_no_local(bool flagged)
    {
        Assert.Equal("    bool OnUse(UObject* User); // 0x7FF600001000",
            SdkExportService.GenerateFunctionSignature(BlueprintFunction(flagged)));
        Assert.Equal("    void Recalc(); // 0x7FF600002000",
            SdkExportService.GenerateFunctionSignature(LocalsOnlyFunction(flagged)));
    }

    // --- the view models' invoke paths: what InvokeParamDialog is given ---
    //
    // The dialog cannot open in a unit test (each path returns before it without a desktop lifetime), so this
    // pins the source: a view model reads the function's arguments through Parameters / InputParams, never the
    // raw chain, which on a Blueprint function would put the locals in the form and in the post-call readout.

    [Fact]
    public void No_view_model_reads_the_raw_property_chain()
    {
        var dir = Path.GetDirectoryName(NumericInputCoercionTests.RepoFile("ui/UE5DumpUI/ViewModels/MainWindowViewModel.cs"))!;
        var files = Directory.GetFiles(dir, "*.cs");
        Assert.Contains(files, f => File.ReadAllText(f).Contains("new Views.InvokeParamDialog(", StringComparison.Ordinal));

        var hits = files
            .SelectMany(f => File.ReadAllLines(f).Select((line, i) => (f, line, i)))
            .Where(x => System.Text.RegularExpressions.Regex.IsMatch(x.line, @"\.Params\b"))
            .Select(x => $"{Path.GetFileName(x.f)}:{x.i + 1}: {x.line.Trim()}")
            .ToList();
        Assert.Empty(hits);
    }
}
