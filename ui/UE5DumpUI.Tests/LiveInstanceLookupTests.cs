using System;
using System.IO;
using System.Linq;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// [MAINWINDOW-NAVIGATE-EXACT-NAME] "A live object of this class, to show or to search from" is one question with one
/// answer: the DLL's <c>find_live_instance</c>.
///
/// <para>Ten MainWindow handlers -- the four that take a (class, member) pair to Live Walker, the six Locate in GWorld /
/// GameEngine -- and Detect Stats' live probe asked <c>find_instances</c> for the class by EXACT name, five rows, and
/// skipped names starting <c>Default__</c>. Name equality is not derivation: a class whose live objects are all of a
/// subclass (a Blueprint's <c>_C</c>, a game's <c>BP_PlayerController</c>) answered none, and the navigate fell back to
/// the class view, the Locate said "no live instance". And a template not named <c>Default__</c> -- a class default
/// object's default subobject -- passed the skip, so Live Walker opened on a template and the path search started from
/// one.</para>
///
/// <para>MainWindowViewModel's handlers are lambdas subscribed in its constructor, and the DLL's handler sits in a file
/// no test target compiles, so these pin the source.</para>
/// </summary>
public class LiveInstanceLookupTests
{
    private static string CodeOnly(string s) => string.Join('\n', s.Split('\n').Select(l =>
    {
        int c = l.IndexOf("//", StringComparison.Ordinal);
        return c >= 0 ? l[..c] : l;
    }));

    private static string Source(string relative) =>
        CodeOnly(File.ReadAllText(NumericInputCoercionTests.RepoFile(relative)));

    /// <summary>The handler a subscription starts, up to the `};` that closes its lambda at the same indent.</summary>
    private static string Handler(string src, string subscription)
    {
        int at = src.IndexOf(subscription, StringComparison.Ordinal);
        Assert.True(at >= 0, subscription + " not found");
        int lineStart = src.LastIndexOf('\n', at) + 1;
        string indent = src[lineStart..at];
        int end = src.IndexOf("\n" + indent + "};", at, StringComparison.Ordinal);
        Assert.True(end > at, subscription + ": end of the handler not found");
        return src[at..end];
    }

    [Theory]
    [InlineData("InterestingFunctions.NavigateToFunction +=")]
    [InlineData("LiveFuncs.NavigateToFunction +=")]
    [InlineData("InterestingProperties.NavigateToProperty +=")]
    [InlineData("Console.NavigateToFunction +=")]
    [InlineData("InterestingFunctions.LocateInGWorld +=")]
    [InlineData("InterestingFunctions.LocateInGameEngine +=")]
    [InlineData("InterestingProperties.LocateInGWorld +=")]
    [InlineData("InterestingProperties.LocateInGameEngine +=")]
    [InlineData("DetectStats.LocateInGWorld +=")]
    [InlineData("_xrefLocateHandler = async")]
    public void A_MainWindow_lookup_asks_for_a_live_object_by_derivation(string subscription)
    {
        var body = Handler(Source("ui/UE5DumpUI/ViewModels/MainWindowViewModel.cs"), subscription);
        Assert.DoesNotContain("FindInstancesAsync(", body, StringComparison.Ordinal);
        Assert.DoesNotContain("Default__", body, StringComparison.Ordinal);
        Assert.Contains("LiveInstanceAsync(className)", body, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("InterestingFunctions.NavigateToFunction +=")]
    [InlineData("LiveFuncs.NavigateToFunction +=")]
    [InlineData("Console.NavigateToFunction +=")]
    public void A_function_navigate_says_what_it_opened_when_the_function_is_not_selected(string subscription)
    {
        // Review 1, MED: a function's row names the class that DECLARES it, and the object opened may be of a subclass,
        // whose Live Walker list holds only its own functions. The line said "'Jump' not in this class" of the class
        // that declares Jump.
        var body = Handler(Source("ui/UE5DumpUI/ViewModels/MainWindowViewModel.cs"), subscription);
        Assert.Contains("FunctionNavigateText(", body, StringComparison.Ordinal);
        Assert.DoesNotContain("not in this class", body, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("InterestingFunctions.LocateInGWorld +=")]
    [InlineData("InterestingFunctions.LocateInGameEngine +=")]
    [InlineData("InterestingProperties.LocateInGWorld +=")]
    [InlineData("InterestingProperties.LocateInGameEngine +=")]
    [InlineData("DetectStats.LocateInGWorld +=")]
    [InlineData("_xrefLocateHandler = async")]
    public void A_Locate_always_writes_the_top_bar_with_its_pick(string subscription)
    {
        // Review 2: a Debug Camera's object, or an older DLL's exact-name answer, was said by no Locate. Review 3: it was
        // written only when there was something to say, so the NEXT Locate left the previous object's warning there.
        var body = Handler(Source("ui/UE5DumpUI/ViewModels/MainWindowViewModel.cs"), subscription);
        Assert.Contains("StatusText = LocateStatusText(live, className, ", body, StringComparison.Ordinal);
        Assert.Contains("LiveWalker.StatusText = NoLiveInstanceToLocateText(className, ", body, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("InterestingFunctions.NavigateToFunction +=", 2)]
    [InlineData("LiveFuncs.NavigateToFunction +=", 2)]
    [InlineData("Console.NavigateToFunction +=", 2)]
    [InlineData("InterestingProperties.NavigateToProperty +=", 3)]
    public void A_navigate_says_its_pick_on_every_line_it_writes(string subscription, int lines)
    {
        // Review 3: the class-view fallback ("showing class metadata") and the class-not-found line are where a navigate's
        // older-DLL miss is said -- no subclass was looked for -- and deleting the notes there kept every test green. The
        // function navigates' hit line carries it through FunctionNavigateText; the property navigate's hit line, here.
        var body = Handler(Source("ui/UE5DumpUI/ViewModels/MainWindowViewModel.cs"), subscription);
        Assert.Equal(lines, System.Text.RegularExpressions.Regex.Matches(body, @"\+ PickNote\(live, className\)").Count);
    }

    [Fact]
    public void An_older_DLL_s_line_says_how_to_replace_it_and_that_it_may_be_a_template()
    {
        // Review 3: "Refresh the proxy DLL" sent the user to Proxy Deploy's Refresh, which re-reads the deploy status and
        // replaces nothing; and the line named one of the old lookup's two limits.
        var miss = new UE5DumpUI.Models.LiveInstanceResult { Pick = UE5DumpUI.Models.LiveInstanceResult.PickLegacy };
        foreach (var line in new[]
                 {
                     UE5DumpUI.ViewModels.MainWindowViewModel.PickNote(miss, "Character"),
                     UE5DumpUI.ViewModels.MainWindowViewModel.NoLiveInstanceToLocateText("Character", "GWorld", miss),
                 })
        {
            Assert.DoesNotContain("Refresh the proxy", line, StringComparison.Ordinal);
            Assert.Contains("Update All", line, StringComparison.Ordinal);
            Assert.Contains("template", line, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("str.Tip.LF.OpenLive")]
    [InlineData("str.Tip.Con.OpenLive")]
    [InlineData("str.Tip.IF.OpenLive")]
    public void Every_Open_in_Live_tooltip_says_a_subclass_object_may_be_opened(string key)
    {
        // Review 2: all three function navigates can land on a subclass object now; only one tooltip said so.
        var axaml = File.ReadAllText(NumericInputCoercionTests.RepoFile("ui/UE5DumpUI/Resources/Strings/en.axaml"));
        int at = axaml.IndexOf($"x:Key=\"{key}\">", StringComparison.Ordinal);
        Assert.True(at > 0, key + " not found");
        string tip = axaml[at..axaml.IndexOf("</sys:String>", at, StringComparison.Ordinal)];
        Assert.Contains("subclass", tip, StringComparison.Ordinal);
        Assert.DoesNotContain("CDO-only", tip, StringComparison.Ordinal);
        Assert.DoesNotContain("non-CDO", tip, StringComparison.Ordinal);
    }

    private static UE5DumpUI.Models.LiveInstanceResult Pick(string name, string cls, string pick = "live") =>
        new() { Address = "0x2A0", Name = name, ClassName = cls, Pick = pick };

    [Fact]
    public void A_selected_function_says_where_it_landed()
    {
        var line = UE5DumpUI.ViewModels.MainWindowViewModel.FunctionNavigateText(
            "Character", "Jump", Pick("BP_Hero_C_0", "BP_Hero_C"), picked: true, "function");
        Assert.Equal("Navigated to Character::Jump (live instance 0x2A0)", line);
    }

    [Fact]
    public void An_inherited_function_on_a_subclass_object_is_said_to_be_inherited_not_missing()
    {
        var line = UE5DumpUI.ViewModels.MainWindowViewModel.FunctionNavigateText(
            "Character", "Jump", Pick("BP_Hero_C_0", "BP_Hero_C"), picked: false, "function");
        Assert.DoesNotContain("not in this class", line, StringComparison.Ordinal);
        Assert.Contains("BP_Hero_C_0", line, StringComparison.Ordinal);
        Assert.Contains("a subclass of Character", line, StringComparison.Ordinal);
        Assert.Contains("Jump is declared on Character", line, StringComparison.Ordinal);
    }

    [Fact]
    public void A_function_missing_from_the_class_itself_still_says_so()
    {
        // The class is matched case-insensitively, as the DLL matches it.
        var line = UE5DumpUI.ViewModels.MainWindowViewModel.FunctionNavigateText(
            "PlayerController", "Nope", Pick("PlayerController_0", "playercontroller"), picked: false, "exec");
        Assert.Equal("Navigated to PlayerController @ 0x2A0; exec 'Nope' not in this class", line);
    }

    [Fact]
    public void The_Debug_Camera_s_own_object_is_named_as_the_Console_names_it()
    {
        // Review 2: the note was glued on with no separator, in a third wording of the Console's own sentence.
        var dcc = Pick("CheatManager_1", "CheatManager", UE5DumpUI.Models.LiveInstanceResult.PickDebugCameraOwned);
        Assert.Equal("Navigated to CheatManager::Slomo (live instance 0x2A0) "
                     + UE5DumpUI.Helpers.InvokePickText.DebugCameraOwnedText("CheatManager_1", "CheatManager"),
                     UE5DumpUI.ViewModels.MainWindowViewModel.FunctionNavigateText("CheatManager", "Slomo", dcc, true, "exec"));
        Assert.Equal("", UE5DumpUI.ViewModels.MainWindowViewModel.PickNote(Pick("X_0", "X"), "X"));
    }

    [Fact]
    public void An_older_DLL_s_answer_says_its_limits_hit_or_miss()
    {
        // Review 2: its miss said "no live instance of X or of a subclass", and no subclass had been looked for.
        var hit = Pick("PlayerController_0", "PlayerController", UE5DumpUI.Models.LiveInstanceResult.PickLegacy);
        var miss = new UE5DumpUI.Models.LiveInstanceResult { Pick = UE5DumpUI.Models.LiveInstanceResult.PickLegacy };
        Assert.Contains("exact class name only",
            UE5DumpUI.ViewModels.MainWindowViewModel.PickNote(hit, "PlayerController"), StringComparison.Ordinal);
        Assert.Contains("exact class name only",
            UE5DumpUI.ViewModels.MainWindowViewModel.PickNote(miss, "Character"), StringComparison.Ordinal);
        var legacyLine = UE5DumpUI.ViewModels.MainWindowViewModel.NoLiveInstanceToLocateText("Character", "GWorld", miss);
        Assert.DoesNotContain("or of a subclass", legacyLine, StringComparison.Ordinal);
        Assert.Contains("predates find_live_instance", legacyLine, StringComparison.Ordinal);
        Assert.Contains("or of a subclass", UE5DumpUI.ViewModels.MainWindowViewModel.NoLiveInstanceToLocateText(
            "Character", "GWorld", new UE5DumpUI.Models.LiveInstanceResult()), StringComparison.Ordinal);
    }

    [Fact]
    public void No_MainWindow_lookup_is_left_on_the_exact_name()
    {
        var src = Source("ui/UE5DumpUI/ViewModels/MainWindowViewModel.cs");
        Assert.DoesNotContain("exactMatch: true, limit: 5", src, StringComparison.Ordinal);
    }

    [Fact]
    public void Detect_Stats_probes_a_live_object_by_derivation()
    {
        var src = Source("ui/UE5DumpUI/ViewModels/DetectStatsViewModel.cs");
        Assert.DoesNotContain("FindInstancesAsync(grp.Key", src, StringComparison.Ordinal);
        Assert.Contains("FindLiveInstanceAsync(grp.Key", src, StringComparison.Ordinal);
    }

    [Fact]
    public void The_DLL_answers_a_live_object_or_none_through_the_user_facing_resolver()
    {
        Assert.Contains("CMD_FIND_LIVE_INSTANCE = \"find_live_instance\"", Source("dll/src/Renge.h"),
                        StringComparison.Ordinal);
        var fern = Source("dll/src/Fern.cpp");
        int at = fern.IndexOf("cmd == Renge::CMD_FIND_LIVE_INSTANCE", StringComparison.Ordinal);
        Assert.True(at > 0, "the find_live_instance handler was not found");
        int end = fern.IndexOf("\n        if (cmd == Renge::CMD_", at + 1, StringComparison.Ordinal);
        string handler = fern[at..(end > at ? end : fern.Length)];
        // Review 1: its own walk -- an exact-class object first, no destroyed object, no partial-name fallback -- under
        // its own name in the log, and a cancel that does not say "nothing was invoked".
        Assert.Contains("Aura::FindLiveInstanceOf(className)", handler, StringComparison.Ordinal);
        Assert.Contains("Renge::LiveInstanceLookupCancelledText(className)", handler, StringComparison.Ordinal);
        Assert.DoesNotContain("Renge::InstanceLookupCancelledText(", handler, StringComparison.Ordinal);
        // The class-default pick is an invoke's fallback; a navigation must not open it.
        Assert.Contains("Aura::UserPick::Live", handler, StringComparison.Ordinal);
        Assert.Contains("Aura::UserPick::DebugCameraOwned", handler, StringComparison.Ordinal);
        Assert.DoesNotContain("UserPick::ClassDefault", handler, StringComparison.Ordinal);
        Assert.DoesNotContain("UserPick::Partial", handler, StringComparison.Ordinal);
    }
}
