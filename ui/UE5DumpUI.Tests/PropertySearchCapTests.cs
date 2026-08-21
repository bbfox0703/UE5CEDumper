using System;
using System.IO;
using System.Text.RegularExpressions;
using UE5DumpUI;
using UE5DumpUI.Models;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// [PROPSEARCHCAP-2026-08-19] — Property Search now has a Max control, so the three things
/// that can silently come apart are pinned here: the clamp range agreeing between the UI and
/// the DLL, the default staying 200 rather than drifting to Instance Finder's 5000, and the
/// status line only offering "raise Max" while raising it is actually possible.
/// </summary>
public class PropertySearchCapTests
{
    private static string RepoFile(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (int i = 0; i < 8 && dir is not null; i++, dir = dir.Parent)
        {
            string candidate = Path.Combine(dir.FullName, relative);
            if (File.Exists(candidate)) return candidate;
        }
        throw new FileNotFoundException($"could not find {relative} walking up from {AppContext.BaseDirectory}");
    }

    [Fact]
    public void TheDefaultStaysAtTwoHundredAndIsNotInstanceFinders()
    {
        // ⚠ The whole point of this work is the LEVER, not a bigger default. A property-search
        // row is a match per property per class WITH a resolved preview value, so it is far
        // heavier than an instance address; quietly raising the default would make every user
        // pay for the minority of searches that need it. If someone deliberately changes this,
        // they should have to change this assertion and say why in the commit.
        Assert.Equal(200, Constants.DefaultPropertySearchCap);
        Assert.NotEqual(Constants.DefaultInstanceSearchCap, Constants.DefaultPropertySearchCap);
        Assert.Equal(Constants.DefaultPropertySearchCap, new PropertySearchUiOptions().PropertySearchCap);
    }

    [Fact]
    public void TheClampRangeIsTheSameNumberInTheUiTheControlAndTheDll()
    {
        Assert.Equal(100, Constants.MinSearchCap);
        Assert.Equal(50000, Constants.MaxSearchCap);

        // The control is the user's clamp; the DLL's is the guarantee. They are written in
        // three different languages in three different files, so read the other two back
        // rather than trusting that they were kept in step by hand.
        string axaml = File.ReadAllText(RepoFile(@"ui\UE5DumpUI\Views\PropertySearchPanel.axaml"));
        var nud = Regex.Match(axaml,
            @"<NumericUpDown\s+Value=""\{Binding PropertySearchCapValue\}""[^>]*?" +
            @"Minimum=""(?<min>\d+)""\s+Maximum=""(?<max>\d+)""[^>]*?ClipValueToMinMax=""True""",
            RegexOptions.Singleline);
        Assert.True(nud.Success, "the PropertySearchCapValue NumericUpDown was not found, or lost its clamp attributes");
        Assert.Equal(Constants.MinSearchCap.ToString(), nud.Groups["min"].Value);
        Assert.Equal(Constants.MaxSearchCap.ToString(), nud.Groups["max"].Value);

        // ⚠ ClipValueToMinMax is load-bearing, not decoration: without it Avalonia lets a typed
        // value exceed Maximum and only paints a validation error, so an out-of-range cap would
        // reach the wire and be silently clamped somewhere else.
        Assert.Contains(@"ClipValueToMinMax=""True""", nud.Value);
    }

    [Fact]
    public void TheDllClampsTheSameCeilingBecauseTheUiClampIsOnlyAConvenience()
    {
        // A pipe client that is not this UI (tools/verify/pipe_client.py, a CE script, an older
        // build) can send any limit at all. The ceiling has to exist server-side or it is not a
        // ceiling. This reads the handler rather than asserting that someone remembered.
        string fern = File.ReadAllText(RepoFile(@"dll\src\Fern.cpp"));
        int at = fern.IndexOf("cmd == Renge::CMD_SEARCH_PROPERTIES)", StringComparison.Ordinal);
        Assert.True(at > 0, "CMD_SEARCH_PROPERTIES handler not found");
        string handler = fern.Substring(at, Math.Min(1600, fern.Length - at));

        Assert.Contains("if (limit < 1) limit = 1;", handler);
        Assert.Contains($"if (limit > {Constants.MaxSearchCap}) limit = {Constants.MaxSearchCap};", handler);
        // The WIRE default must stay 200 even though the UI now always sends a limit — an older
        // client that sends none would otherwise change behaviour on a DLL upgrade alone.
        Assert.Contains($"request.value(\"limit\", {Constants.DefaultPropertySearchCap})", handler);
    }

    [Fact]
    public void TheCapAdviceOffersRaiseMaxOnlyWhileRaisingItIsPossible()
    {
        // Audit #5 Z10 removed "raise Max" because the panel had no Max. Now it has one, so the
        // advice is back — but at the ceiling it would be the same lie in a new place, and that
        // is the exact failure this pair of changes exists to avoid. Mirrors the VM's rule.
        static string Advice(int cap, bool gameOnly)
        {
            string a = "narrow it with a longer property name or a Type filter";
            if (!gameOnly) a += ", or tick \"Game classes only\" to skip engine classes";
            if (cap < Constants.MaxSearchCap) a += $", or raise Max above {cap:N0}";
            return a;
        }

        Assert.Contains("raise Max above 200", Advice(200, gameOnly: true));
        Assert.Contains("raise Max above 49,999", Advice(Constants.MaxSearchCap - 1, true));
        Assert.DoesNotContain("raise Max", Advice(Constants.MaxSearchCap, gameOnly: true));
        // The Game-classes clause is independent of the cap clause; both can appear.
        Assert.Contains("Game classes only", Advice(200, gameOnly: false));
        Assert.Contains("raise Max", Advice(200, gameOnly: false));
        Assert.DoesNotContain("Game classes only", Advice(200, gameOnly: true));
    }

    [Fact]
    public void TheViewModelSourceStillPassesTheCapToTheSearch()
    {
        // The defect was not a missing control — it was that the panel never sent a limit at
        // all, so the wire default won. A control bound to a property nothing reads would look
        // exactly like a fix and behave exactly like the bug.
        string vm = File.ReadAllText(RepoFile(@"ui\UE5DumpUI\ViewModels\PropertySearchViewModel.cs"));
        int at = vm.IndexOf("_dump.SearchPropertiesAsync(", StringComparison.Ordinal);
        Assert.True(at > 0, "the SearchPropertiesAsync call site was not found");
        string call = vm.Substring(at, Math.Min(400, vm.Length - at));
        Assert.Contains("limit: PropertySearchCap", call);
    }
}
