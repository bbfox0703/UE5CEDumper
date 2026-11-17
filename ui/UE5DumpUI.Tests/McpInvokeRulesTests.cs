using System.Text.RegularExpressions;
using UE5DumpUI.Mcp;
using UE5DumpUI.Models;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// [MCP-WRITE-GRANTS-2026-11-14] Phase 1b. The server's copy of the DLL's invoke rules refuses a grant the DLL would
/// refuse every call of. A copy drifts, so its lists are read back from Voll.h here: a name added to one side only fails
/// the build.
/// </summary>
public sealed class McpInvokeRulesTests
{
    private static string Voll() => File.ReadAllText(NumericInputCoercionTests.RepoFile("dll/src/Voll.h"));

    private static string Between(string text, string start, string end)
    {
        int s = text.IndexOf(start, StringComparison.Ordinal);
        Assert.True(s >= 0, "missing: " + start);
        int e = text.IndexOf(end, s, StringComparison.Ordinal);
        Assert.True(e > s, "missing after it: " + end);
        return text[s..e];
    }

    [Fact]
    public void TheRefusedNames_AreTheDlls()
    {
        string rules = Between(Voll(), "static constexpr Rule kRules[] = {", "};");
        var dll = Regex.Matches(rules, "\\{\"([a-z0-9_]+)\",").Select(m => m.Groups[1].Value).Order().ToArray();
        Assert.NotEmpty(dll);
        Assert.Equal(dll, McpInvokeRules.RefusedNames.Order().ToArray());
    }

    [Fact]
    public void TheRefusedFamilies_AreTheDlls()
    {
        string families = Between(Voll(), "static constexpr Family kFamilies[] = {", "};");
        var dll = Regex.Matches(families, "\\{\"([a-z0-9_]*)\", \"([a-z0-9_]*)\",")
            .Select(m => (m.Groups[1].Value, m.Groups[2].Value)).Order().ToArray();
        Assert.NotEmpty(dll);
        Assert.Equal(dll, McpInvokeRules.RefusedFamilies.Order().ToArray());
    }

    [Fact]
    public void TheRefusedFullNames_AndTheNameTokens_AreTheDlls()
    {
        string voll = Voll();
        string background = Between(voll, "inline std::string InvokeRefusedForBackground(", "const std::string n =");
        var full = Regex.Matches(background, "full == \"([^\"]+)\"").Select(m => m.Groups[1].Value).Order().ToArray();
        Assert.Equal(full, McpInvokeRules.RefusedFullNames.Order().ToArray());
        string tokens = Between(voll, "static constexpr const char* kNameLike[] = {", "};");
        Assert.Equal(Regex.Matches(tokens, "\"([a-z]+)\"").Select(m => m.Groups[1].Value).ToArray(),
            McpInvokeRules.NameLikeTokens);
    }

    private static FunctionInfoModel Fn(string name, uint flags = 0x04020401, params FunctionParamModel[] parms)
        => new() { Name = name, FullName = "//Script/Engine/Actor." + name, FunctionFlags = flags, Params = [.. parms] };

    private static FunctionParamModel P(string name, string type, string structName = "", string objClass = "",
        IReadOnlyList<DynamicStructField>? fields = null)
        => new()
        {
            Name = name, TypeName = type, IsParm = true, StructName = structName, ObjectClassName = objClass,
            StructFields = fields ?? [],
        };

    [Fact]
    public void AnEverydayFunction_IsGrantable()
    {
        Assert.Null(McpInvokeRules.WhyNotGrantable(Fn("SetActorHiddenInGame", parms: P("bNewHidden", "BoolProperty"))));
        Assert.Null(McpInvokeRules.WhyNotGrantable(Fn("K2_AttachToComponent",
            parms: [P("Parent", "ObjectProperty", objClass: "SceneComponent"), P("SocketName", "NameProperty")])));
        Assert.Null(McpInvokeRules.WhyNotGrantable(Fn("Activate", parms: P("bReset", "BoolProperty"))));   // a component's
    }

    [Fact]
    public void WhatTheDllRefuses_OrAGrantCannotShow_IsNot()
    {
        Assert.NotNull(McpInvokeRules.WhyNotGrantable(Fn("k2_destroyactor")));                       // any case
        Assert.NotNull(McpInvokeRules.WhyNotGrantable(Fn("SetIntPropertyByName")));                 // a family
        Assert.NotNull(McpInvokeRules.WhyNotGrantable(Fn("Fire", 0x04020441)));                     // FUNC_Net
        Assert.NotNull(McpInvokeRules.WhyNotGrantable(Fn("Graph", 0x00008000)));                    // an event graph
        Assert.NotNull(McpInvokeRules.WhyNotGrantable(new FunctionInfoModel
        {
            Name = "Activate", FullName = "//Script/Engine/BlueprintAsyncActionBase.Activate", FunctionFlags = 0x04020401,
        }));
        Assert.NotNull(McpInvokeRules.WhyNotGrantable(Fn("PlayAfter",
            parms: P("Info", "StructProperty", structName: "MyInfo",
                fields: [new DynamicStructField("EventName", "NameProperty", 0, 8)]))));   // a struct member, one deep
        Assert.NotNull(McpInvokeRules.WhyNotGrantable(Fn("SetLabel", parms: P("Label", "TextProperty"))));
        // A struct that wraps a name, named for an event: SendGameplayEventToActor's FGameplayTag EventTag.
        Assert.NotNull(McpInvokeRules.WhyNotGrantable(Fn("SendGameplayEventToActor",
            parms: [P("Actor", "ObjectProperty", objClass: "Actor"),
                    P("EventTag", "StructProperty", structName: "GameplayTag",
                        fields: [new DynamicStructField("TagName", "NameProperty", 0, 8)])])));
        Assert.NotNull(McpInvokeRules.WhyNotGrantable(Fn("Run", parms: P("Func", "ObjectProperty", objClass: "Function"))));
    }
}
