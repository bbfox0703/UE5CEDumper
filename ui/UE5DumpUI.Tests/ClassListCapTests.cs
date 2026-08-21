using System;
using System.IO;
using System.Text.RegularExpressions;
using UE5DumpUI;
using UE5DumpUI.Models;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// [CLASSCAP-2026-08-21] — the Classes tab's status line has always ended
/// <c>"… ⚠ STOPPED at the 5,000-row cap — filter to narrow, or raise the cap"</c>, and there was
/// no cap to raise: the toolbar had no numeric control and <c>ListClassesAsync</c> was called with
/// no <c>limit</c>, so the wire default of 5,000 always won.
///
/// <para>⭐ <b>Found by doing a live check, not by reading.</b> It surfaced while closing
/// <c>[CLASSTOTAL-2026-08-18]</c> on Avowed, where the line read "5,000 classes shown of 7,409
/// total … or raise the cap" with nothing on screen that could raise it. The pipe-level part of
/// CLASSTOTAL had passed weeks earlier and could never have shown this — the numbers were right,
/// the sentence beside them was not.</para>
///
/// <para>Third instance of the audit #5 <b>Z10</b> shape (never name a lever the user cannot
/// reach), after Property Search. Same fix, same tests.</para>
/// </summary>
public class ClassListCapTests
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
    public void TheDefaultStaysAtFiveThousandAndIsThePersistedDefaultToo()
    {
        // 5,000 is the long-standing wire default and stays the default — a class row carries a
        // walked property/function summary, so a bigger pool is not free. Avowed measures 7,409
        // game classes (5,102 game-only), so real titles genuinely land past it.
        Assert.Equal(5000, Constants.DefaultClassListCap);
        Assert.Equal(Constants.DefaultClassListCap, new GameClassFilterUiOptions().ClassListCap);
    }

    [Fact]
    public void TheClampRangeIsTheSameNumberInTheUiTheControlAndTheDll()
    {
        Assert.Equal(100, Constants.MinSearchCap);
        Assert.Equal(50000, Constants.MaxSearchCap);

        string axaml = File.ReadAllText(RepoFile(@"ui\UE5DumpUI\Views\GameClassFilterPanel.axaml"));
        var nud = Regex.Match(axaml,
            @"<NumericUpDown\s+Value=""\{Binding ClassListCapValue\}""[^>]*?" +
            @"Minimum=""(?<min>\d+)""\s+Maximum=""(?<max>\d+)""[^>]*?ClipValueToMinMax=""True""",
            RegexOptions.Singleline);
        Assert.True(nud.Success, "the ClassListCapValue NumericUpDown was not found, or lost its clamp attributes");
        Assert.Equal(Constants.MinSearchCap.ToString(), nud.Groups["min"].Value);
        Assert.Equal(Constants.MaxSearchCap.ToString(), nud.Groups["max"].Value);
    }

    [Fact]
    public void TheDllClampsListClassesToo()
    {
        // Any pipe client can send any limit; the ceiling has to exist server-side or it is not a
        // ceiling. Read the handler back rather than trusting that someone remembered.
        string fern = File.ReadAllText(RepoFile(@"dll\src\Fern.cpp"));
        int at = fern.IndexOf("cmd == Renge::CMD_LIST_CLASSES)", StringComparison.Ordinal);
        Assert.True(at > 0, "CMD_LIST_CLASSES handler not found");
        string handler = fern.Substring(at, Math.Min(1200, fern.Length - at));

        Assert.Contains("if (limit < 1) limit = 1;", handler);
        Assert.Contains($"if (limit > {Constants.MaxSearchCap}) limit = {Constants.MaxSearchCap};", handler);
        // ⚠ The WIRE default must stay 5000 even though the UI now always sends a limit.
        Assert.Contains($"request.value(\"limit\", {Constants.DefaultClassListCap})", handler);
    }

    [Fact]
    public void TheViewModelSourceActuallyPassesTheCap()
    {
        // The defect was not a missing control — the panel never sent a limit, so the wire default
        // won. A control bound to a property nothing reads looks like a fix and behaves like a bug.
        string vm = File.ReadAllText(RepoFile(@"ui\UE5DumpUI\ViewModels\GameClassFilterViewModel.cs"));
        int at = vm.IndexOf("_dump.ListClassesAsync(", StringComparison.Ordinal);
        Assert.True(at > 0, "the ListClassesAsync call site was not found");
        Assert.Contains("limit: ClassListCap", vm.Substring(at, Math.Min(300, vm.Length - at)));
    }

    [Fact]
    public void TheCapNoteOffersMaxOnlyWhileRaisingItIsPossible()
    {
        // Mirrors the VM rule. At the ceiling "raise Max" is the same lie Z10 removed, in a new
        // place — which is the entire reason the old unconditional wording was a defect.
        static string CapNote(int requestedLimit, int cap) =>
            "  ⚠ STOPPED at the " + requestedLimit.ToString("N0") + "-row cap — filter to narrow"
            + (cap < Constants.MaxSearchCap ? ", or raise Max above " + cap.ToString("N0") : "");

        Assert.Contains("raise Max above 5,000", CapNote(5000, 5000));
        Assert.DoesNotContain("raise Max", CapNote(Constants.MaxSearchCap, Constants.MaxSearchCap));
        // The "STOPPED" half is unconditional — a capped list must always say it is capped, even
        // at the ceiling where nothing more can be done about it.
        Assert.Contains("STOPPED", CapNote(Constants.MaxSearchCap, Constants.MaxSearchCap));
    }

    [Fact]
    public void TheOldUnconditionalWordingIsGoneFromTheSource()
    {
        // The exact string that was the defect. Kept as its own assertion because the rule above
        // is a reimplementation — if someone reverts the VM but leaves the helper, this catches it.
        string vm = File.ReadAllText(RepoFile(@"ui\UE5DumpUI\ViewModels\GameClassFilterViewModel.cs"));
        Assert.DoesNotContain("filter to narrow, or raise the cap", vm);
    }
}
