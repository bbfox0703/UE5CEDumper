using UE5DumpUI.Models;
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
            Name = "OnUse", Address = "0x7FF600001000", NumParms = 2, ParmsSize = 16, ReturnType = "BoolProperty",
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
}
