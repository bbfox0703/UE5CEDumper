using System;

using System.Collections.Generic;

using System.Text;

using UE5DumpUI.Models;

using UE5DumpUI.Services;

using Xunit;



namespace UE5DumpUI.Tests;



/// <summary>

/// <c>[INVOKEHINTQUOTE-2026-08-22]</c> — an emitted CE Lua script must PARSE.

///

/// <para>Every existing test on the generated scripts asserts that some substring is

/// present or absent. None of them asked whether the result is valid Lua, and one of

/// them — <c>Y13_ComplexReturnHint_OnlyClaimsTheDumpWhenItReallyHoldsIt</c> — was

/// generating the exact broken script and passing on it, because the substrings it

/// looked for were all there. The script simply did not compile.</para>

///

/// <para>What happened: <c>BakedScriptGenerator</c> interpolated an English hint into a

/// single-quoted Lua literal without <c>EscapeLua</c>, and the hint's does-not-fit branch

/// contains an apostrophe ("CE's"). That closes the string early and the WHOLE

/// <c>[ENABLE]</c> block becomes a syntax error:

/// <c>Lua error in the script at line 2:119: ')' expected near 's'</c>.</para>

///

/// <para><b>The failure mode is what makes it worth a guard.</b> Cheat Engine does not

/// report it: ticking the record leaves <c>Active</c> at <c>false</c> with no dialog and

/// no output, so the user sees a checkbox that will not stay ticked and nothing else.

/// Measured in CE 7.7 against DumperTest while running Y10 step 4 on

/// <c>SequenceCameraShakeTestUtil::GetCameraCachePOV</c> (ParmsSize 2064, an

/// <c>FMinimalViewInfo</c> return of 2048 bytes at +16 — i.e. any large by-value struct

/// return, which is not an exotic case).</para>

///

/// <para>The scanner below was written against the real broken artifact first and shown

/// to BOTH fire (1 unterminated string, at the print line) and clear (0 after escaping

/// that one apostrophe and nothing else), before being ported here.</para>

/// </summary>

public class CeLuaQuotingTests

{

    // ------------------------------------------------------------------

    // The detector.

    // ------------------------------------------------------------------



    /// <summary>Lines on which a quoted Lua string is left open at end-of-line.

    ///

    /// <para>Lua short strings may not span a raw newline, so per-line balance is the

    /// right rule. Comments are skipped — <c>-- ... when CE's ...</c> is emitted

    /// deliberately and is not a defect — which is exactly why this cannot be a naive

    /// apostrophe count.</para></summary>

    internal static List<string> UnterminatedStringLines(string script)

    {

        var bad = new List<string>();

        var lines = script.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');

        int? inLong = null;                       // open long bracket level, or null



        for (int ln = 0; ln < lines.Length; ln++)

        {

            string line = lines[ln];

            int i = 0, n = line.Length;

            char quote = '\0';



            while (i < n)

            {

                char c = line[i];



                if (inLong is int lvl)

                {

                    if (c == ']')

                    {

                        int j = i + 1, eq = 0;

                        while (j < n && line[j] == '=') { eq++; j++; }

                        if (j < n && line[j] == ']' && eq == lvl) { inLong = null; i = j + 1; continue; }

                    }

                    i++; continue;

                }



                if (quote != '\0')

                {

                    if (c == '\\') { i += 2; continue; }

                    if (c == quote) quote = '\0';

                    i++; continue;

                }



                if (c == '-' && i + 1 < n && line[i + 1] == '-')

                {

                    int j = i + 2;

                    if (j < n && line[j] == '[')

                    {

                        int k = j + 1, eq = 0;

                        while (k < n && line[k] == '=') { eq++; k++; }

                        if (k < n && line[k] == '[') { inLong = eq; i = k + 1; continue; }

                    }

                    break;                        // -- line comment: rest of line is prose

                }



                if (c == '[')

                {

                    int k = i + 1, eq = 0;

                    while (k < n && line[k] == '=') { eq++; k++; }

                    if (k < n && line[k] == '[') { inLong = eq; i = k + 1; continue; }

                }



                if (c == '\'' || c == '"') quote = c;

                i++;

            }



            if (quote != '\0')

                bad.Add($"line {ln + 1}: {line.Trim()}");

        }

        return bad;

    }



    /// <summary>The detector must be able to FAIL, or a green run means nothing.

    /// This is the defect verbatim, and the same line with the apostrophe escaped.</summary>

    [Fact]

    public void TheScanner_FiresOnTheRealDefect_AndClearsWhenItIsEscaped()

    {

        const string broken =

            "local x = 1\n" +

            "  print('[Invoke] OK: F -> R -- read it in CE's memory viewer at mb + 0x328')\n";

        Assert.Single(UnterminatedStringLines(broken));



        string repaired = broken.Replace("CE's", "CE\\'s");

        Assert.Empty(UnterminatedStringLines(repaired));

    }



    /// <summary>...and must NOT fire on the things the generators legitimately emit:

    /// an apostrophe inside a <c>--</c> comment, a bracket that is not a long string,

    /// and a real <c>--[[ ]]</c> block spanning lines.</summary>

    [Fact]

    public void TheScanner_DoesNotFireOnLegitimateApostrophes()

    {

        const string ok =

            "-- in some setups but throws / returns garbage when CE's resolver only\n" +

            "local s = '[Invoke] Before'\n" +

            "--[[ a long comment\n" +

            "     that mentions CE's Lua Engine and spans lines ]]\n" +

            "print(s)\n";

        Assert.Empty(UnterminatedStringLines(ok));

    }



    // ------------------------------------------------------------------

    // The generators.

    // ------------------------------------------------------------------



    private static BakedParamValue Ret(string type, int size, int offset) =>

        new(ParamName: "ReturnValue", UeTypeName: type, Size: size, Offset: offset, LiteralText: "");



    public static IEnumerable<object[]> BakedCases() => new List<object[]>

    {

        // name,                         parmsSize, returnParam

        new object[] { "no return",            16,   null! },

        new object[] { "scalar return",        16,   Ret("IntProperty", 4, 8) },

        new object[] { "complex, fits",        64,   Ret("StrProperty", 16, 32) },

        // THE DEFECT: a complex return past the dump ceiling takes the branch whose

        // wording contains an apostrophe. GetCameraCachePOV's real shape.

        new object[] { "complex, past window", 2064, Ret("StructProperty", 2048, 16) },

        new object[] { "complex, huge parms",  4096, Ret("StructProperty", 512, 1024) },

    };



    [Theory]

    [MemberData(nameof(BakedCases))]

    public void BakedScript_IsParseableLua(string name, int parmsSize, BakedParamValue? ret)

    {

        string script = BakedScriptGenerator.Generate(

            "SequenceCameraShakeTestUtil", "GetCameraCachePOV", parmsSize,

            new List<BakedParamValue>(), returnParam: ret, verifyReturn: true);



        var bad = UnterminatedStringLines(script);

        Assert.True(bad.Count == 0,

            $"[{name}] the emitted script does not parse — CE reverts Active to false " +

            $"with no dialog:{Environment.NewLine}{string.Join(Environment.NewLine, bad)}");

    }



    /// <summary>Anti-vacuity: the matrix above must actually reach the apostrophe branch,

    /// otherwise it is five green rows over a code path that never ran.</summary>

    [Fact]

    public void TheMatrix_ActuallyReachesTheApostropheBranch()

    {

        string script = BakedScriptGenerator.Generate(

            "SequenceCameraShakeTestUtil", "GetCameraCachePOV", 2064,

            new List<BakedParamValue>(), returnParam: Ret("StructProperty", 2048, 16),

            verifyReturn: true);



        Assert.Contains("past the", script);            // the does-not-fit wording

        Assert.Contains("CE\\'s memory", script);       // escaped, and still present

        Assert.DoesNotContain("in CE's memory", script);

    }



    /// <summary>The same rule over the freeze generator, which interpolates a class name,

    /// a property name and a user-typed value — all three can carry an apostrophe.</summary>

    [Theory]

    [InlineData("BP_Teammate_C", "Health", "100")]

    [InlineData("BP_Don'tCare_C", "Health", "100")]

    [InlineData("BP_Teammate_C", "It'sHealth", "100")]

    public void FreezeScript_IsParseableLua(string cls, string prop, string value)

    {

        var p = new FreezeScriptParams

        {

            ClassName = cls,

            PropertyName = prop,

            PropertyOffset = 0x40,

            UeTypeName = "FloatProperty",

            PropertySize = 4,

            BoolFieldMask = 0,

            ValueLiteral = value,

        };



        var bad = UnterminatedStringLines(FreezeScriptGenerator.Generate(p));

        Assert.True(bad.Count == 0,

            $"freeze script does not parse:{Environment.NewLine}{string.Join(Environment.NewLine, bad)}");

    }

    // ------------------------------------------------------------------
    // Every other Lua-emitting generator, over the same rule.
    //
    // The sweep matters because the original defect came from an INTERPOLATED
    // variable defined several lines above its use, so a grep for an apostrophe
    // on the emitting line structurally cannot find its siblings — only running
    // the generators can. The DLL path cases carry a hostile-but-ordinary input:
    // a user whose Windows account is named O'Brien.
    // ------------------------------------------------------------------

    public static IEnumerable<object[]> SimpleGenerators() => new List<object[]>
    {
        new object[] { "DebugCamera",  DebugCameraScriptGenerator.Generate() },
        new object[] { "Foreground",   ForegroundScriptGenerator.Generate() },
        new object[] { "Protection",   ProtectionScriptGenerator.Generate() },
        new object[] { "SeeThrough",   SeeThroughScriptGenerator.Generate() },
        new object[] { "InjectRemind", CeInjectScriptGenerator.GenerateReminder() },
        new object[] { "Autorun",      CeAutorunScriptGenerator.Generate(@"D:\UE5Dumper.dll") },
        new object[] { "Autorun O'Brien",
                       CeAutorunScriptGenerator.Generate(@"C:\Users\O'Brien\UE5Dumper.dll") },
        new object[] { "Inject",       CeInjectScriptGenerator.Generate(@"D:\UE5Dumper.dll") },
        new object[] { "Inject O'Brien",
                       CeInjectScriptGenerator.Generate(@"C:\Users\O'Brien\UE5Dumper.dll") },
        new object[] { "Fly",          FlyScriptGenerator.Generate(FlyScriptGenerator.FlyToggle.Enabled) },
        new object[] { "Noclip",       FlyScriptGenerator.Generate(FlyScriptGenerator.FlyToggle.Noclip) },
        new object[] { "Movement",     MovementScriptGenerator.Generate(MovementScriptGenerator.Knob.WalkSpeed, 250) },
        new object[] { "Gravity dir",  MovementScriptGenerator.GenerateGravityDirection(0, 0, -1) },
        new object[] { "PtrQuery",     PointerQueryScriptGenerator.Generate(PointerQueryScriptGenerator.Target.GWorld) },
        new object[] { "PtrQuery eng", PointerQueryScriptGenerator.Generate(PointerQueryScriptGenerator.Target.GameEngine) },
        new object[] { "TimeDilation", TimeDilationScriptGenerator.Generate(TimeDilationScriptGenerator.Target.Global, 0.5) },
        new object[] { "TimeDil pawn", TimeDilationScriptGenerator.Generate(TimeDilationScriptGenerator.Target.Pawn, 2.0) },
    };

    [Theory]
    [MemberData(nameof(SimpleGenerators))]
    public void EveryGeneratedScript_IsParseableLua(string name, string script)
    {
        Assert.False(string.IsNullOrWhiteSpace(script), $"[{name}] generated nothing");
        var bad = UnterminatedStringLines(script);
        Assert.True(bad.Count == 0,
            $"[{name}] the emitted script does not parse:{Environment.NewLine}" +
            string.Join(Environment.NewLine, bad));
    }

    [Theory]
    [InlineData(TeleportScriptGenerator.Action.Save)]
    [InlineData(TeleportScriptGenerator.Action.Recall)]
    [InlineData(TeleportScriptGenerator.Action.RecallLast)]
    [InlineData(TeleportScriptGenerator.Action.BugIt)]
    [InlineData(TeleportScriptGenerator.Action.BugItGo)]
    [InlineData(TeleportScriptGenerator.Action.Cursor)]
    [InlineData(TeleportScriptGenerator.Action.GetPov)]
    [InlineData(TeleportScriptGenerator.Action.ClearAll)]
    [InlineData(TeleportScriptGenerator.Action.Relative)]
    [InlineData(TeleportScriptGenerator.Action.Explicit)]
    [InlineData(TeleportScriptGenerator.Action.CursorOn)]
    [InlineData(TeleportScriptGenerator.Action.CursorOff)]
    [InlineData(TeleportScriptGenerator.Action.GetPose)]
    public void TeleportScript_IsParseableLua(TeleportScriptGenerator.Action action)
    {
        var bad = UnterminatedStringLines(TeleportScriptGenerator.Generate(action));
        Assert.True(bad.Count == 0,
            $"[teleport {action}] does not parse:{Environment.NewLine}" +
            string.Join(Environment.NewLine, bad));
    }
}
