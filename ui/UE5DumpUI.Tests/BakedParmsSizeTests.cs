using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using UE5DumpUI.Models;
using UE5DumpUI.Services;
using UE5DumpUI.ViewModels;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// [UE-OVERRIDE-411] review 2: the parmsSize a baked invoke carries gates the mailbox's 1024-byte paramsData slab in
/// ue5_invoke_helper.lua (<c>parmsSize &gt; 1024</c> is refused) and bounds every write and the zero-fill. It was the
/// ParmsSize the DLL read from the UFunction's tail, and on a 4.11-4.17 title read with a 4.18+ version that is the
/// NumParms byte -- so a getter with a 2 KB out parameter passed the gate as "parmsSize 3" and ProcessEvent wrote past
/// the slab. Every baked script now carries where the function's parameter chain ends.
/// </summary>
public class BakedParmsSizeTests
{
    /// <summary>A GetViewInfo-shaped getter: one FMinimalViewInfo-sized out parameter, 0x810 bytes, and the tail's
    /// ParmsSize misread as its NumParms neighbour, 3.</summary>
    private static FunctionInfoModel MisreadGetter(ushort parmsSize = 3) => new()
    {
        Name = "GetViewInfo", NumParms = 1, ParmsSize = parmsSize,
        Params = new()
        {
            new() { Name = "OutView", TypeName = "StructProperty", Offset = 0, Size = 0x810, IsOut = true, IsParm = true },
        },
    };

    [Fact]
    public void AMisreadParmsSizeIsReplacedByWhereTheChainEnds()
    {
        Assert.Equal(0x810, BakedScriptGenerator.BakedParmsSize(MisreadGetter()));
    }

    [Fact]
    public void TheDialogShapeTakesTheSameAnswer()
    {
        var fn = MisreadGetter();
        Assert.Equal(0x810, BakedScriptGenerator.BakedParmsSize(fn.ParmsSize, fn.Parameters.ToList()));
    }

    [Fact]
    public void ACorrectParmsSizeIsKept()
    {
        Assert.Equal(0x810, BakedScriptGenerator.BakedParmsSize(MisreadGetter(0x810)));
        // A ParmsSize larger than the chain (padding the DLL reports) is not shrunk.
        Assert.Equal(0x818, BakedScriptGenerator.BakedParmsSize(MisreadGetter(0x818)));
    }

    [Fact]
    public void ABlueprintLocalPastTheParametersDoesNotCount()
    {
        // OnUse: ParmsSize 9, its locals end at 36 -- ProcessEvent builds them in its own frame.
        Assert.Equal(9, BakedScriptGenerator.BakedParmsSize(FunctionParametersTests.BlueprintFunction(flagged: true)));
    }

    [Fact]
    public void AListRowTakesTheDllsChainEnd_AndAnOlderDllsRowKeepsParmsSize()
    {
        Assert.Equal(0x810, BakedScriptGenerator.BakedParmsSize(new AllFunctionEntry { ParmsSize = 3, BufferBytes = 0x810 }));
        Assert.Equal(3, BakedScriptGenerator.BakedParmsSize(new AllFunctionEntry { ParmsSize = 3, BufferBytes = 0 }));
        Assert.Equal(16, BakedScriptGenerator.BakedParmsSize(new AllFunctionEntry { ParmsSize = 16, BufferBytes = 12 }));
    }

    [Fact]
    public void TheBakedScriptCarriesTheChainEnd_SoTheHelpersSlabGateSeesIt()
    {
        var fn = MisreadGetter();
        string script = BakedScriptGenerator.Generate("PlayerCameraManager", fn.Name,
            BakedScriptGenerator.BakedParmsSize(fn), Array.Empty<BakedParamValue>());
        Assert.Contains("'GetViewInfo', 2064, PARAMS)", script);
    }

    [Fact]
    public void ACheatTableRowFromTheFunctionListCarriesTheChainEnd()
    {
        var row = new ScoredFunctionRow
        {
            Entry = new AllFunctionEntry { ClassName = "PlayerCameraManager", FuncName = "GetViewInfo",
                                           NumParms = 1, ParmsSize = 3, BufferBytes = 0x810 },
            FinalScore = 5, Category = FunctionCategory.Other, KeywordHits = 1, ClassBonus = 0, FlagBonus = 0,
        };
        var f = (CtFunctionRow)InterestingFunctionsViewModel.BuildRowsFromSelection(new[] { row }).Single();
        Assert.Equal(0x810, f.ParmsSize);
        Assert.Contains("'GetViewInfo', 2064, PARAMS)", f.GenerateScript());
    }

    [Fact]
    public async Task TheFunctionListReadsTheDllsBufferBytes()
    {
        var pipe = new MockPipeClient();
        pipe.SetHandler(_ => new JsonObject
        {
            ["ok"] = true, ["total"] = 2,
            ["functions"] = new JsonArray
            {
                new JsonObject { ["class_name"] = "C", ["func_name"] = "F", ["num_parms"] = 1, ["parms_size"] = 3,
                                 ["buffer_bytes"] = 0x810 },
                new JsonObject { ["class_name"] = "C", ["func_name"] = "Old", ["num_parms"] = 1, ["parms_size"] = 8 },
            },
        });
        var svc = new DumpService(pipe, new MockLoggingService(), UE5DumpUI.Core.IdentityCodePage.Instance);
        var res = await svc.ListAllFunctionsAsync(ct: TestContext.Current.CancellationToken);
        Assert.Equal(0x810u, res.Functions[0].BufferBytes);
        Assert.Equal(0u, res.Functions[1].BufferBytes);
    }

    // The call sites are view-model commands and a dialog constructor, which need a window and a pipe; what they hand
    // the generator is pinned by reading them, so a new call site that bakes the raw tail read goes red here.
    [Fact]
    public void NoCallSiteBakesTheRawParmsSize()
    {
        string ui = Path.Combine(RepoRoot(), "ui", "UE5DumpUI");
        var raw = new Regex(@"\.ParmsSize\b");
        var offenders = new List<string>();
        int sites = 0;
        foreach (var file in Directory.EnumerateFiles(ui, "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")) continue;
            string text = File.ReadAllText(file);
            foreach (Match m in Regex.Matches(text, @"BakedScriptGenerator\.Generate\("))
            {
                var args = Arguments(text, m.Index + m.Length);
                if (args.Count < 3) continue;
                sites++;
                if (raw.IsMatch(args[2])) offenders.Add($"{Path.GetFileName(file)}: {args[2].Trim()}");
            }
            foreach (Match m in Regex.Matches(text, @"new CtFunctionRow\s*\{[^}]*?ParmsSize\s*=\s*([^,\r\n]+)"))
            {
                sites++;
                if (!m.Groups[1].Value.Contains("BakedParmsSize")) offenders.Add($"{Path.GetFileName(file)}: {m.Groups[1].Value.Trim()}");
            }
        }
        Assert.True(sites >= 6, $"only {sites} call sites found -- the scan no longer sees them");
        Assert.True(offenders.Count == 0, "baked from the tail read: " + string.Join("; ", offenders));

        string dialog = File.ReadAllText(Path.Combine(ui, "Views", "InvokeParamDialog.cs"));
        Assert.Matches(@"_parmsSize\s*=\s*BakedScriptGenerator\.BakedParmsSize\(parmsSize,\s*allParams\)", dialog);
    }

    /// <summary>The comma-separated arguments of the call whose '(' ends at <paramref name="start"/>. Angle brackets
    /// are not nesting: a lambda's '=>' would close one.</summary>
    private static List<string> Arguments(string text, int start)
    {
        var args = new List<string>();
        int depth = 0, from = start;
        for (int i = start; i < text.Length; i++)
        {
            char c = text[i];
            if (c is '(' or '[' or '{') depth++;
            else if (c is ')' or ']' or '}')
            {
                if (depth == 0) { args.Add(text[from..i]); return args; }
                depth--;
            }
            else if (c == ',' && depth == 0) { args.Add(text[from..i]); from = i + 1; }
        }
        return args;
    }

    private static string RepoRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !File.Exists(Path.Combine(d.FullName, "build.ps1"))) d = d.Parent;
        return d?.FullName ?? throw new DirectoryNotFoundException("repo root");
    }
}
