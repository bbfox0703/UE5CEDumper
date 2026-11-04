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
