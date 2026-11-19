using System;
using System.IO;
using System.Text.RegularExpressions;
using UE5DumpUI;
using UE5DumpUI.Core;
using UE5DumpUI.Models;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// [CLASSCAP] — the Classes tab's cap note may offer only a lever the user can reach: the status
/// line says <c>"… ⚠ STOPPED at the N-row cap — filter to narrow"</c> and adds "or raise Max" only
/// while Max can still go higher, and <c>ListClassesAsync</c> is called with the toolbar's
/// <c>limit</c>, so the wire default of 5,000 does not silently win.
///
/// <para>⭐ The rule: never name a lever the user cannot reach (Property Search keeps it too). A
/// pipe-level check of <c>[CLASSTOTAL-2026-08-18]</c> cannot see this — the numbers can be right
/// while the sentence beside them is not — so the sentence is pinned here.</para>
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
        // walked property/function summary, so a bigger pool is not free. Real titles genuinely
        // land past it.
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
        // ⚠ The WIRE default must stay 5000 even though the UI always sends a limit.
        Assert.Contains($"request.value(\"limit\", {Constants.DefaultClassListCap})", handler);
    }

    [Fact]
    public void TheViewModelSourceActuallyPassesTheCap()
    {
        // A control alone is not the fix — a panel that sends no limit lets the wire default win.
        // A control bound to a property nothing reads looks like a fix and behaves like a bug.
        string vm = File.ReadAllText(RepoFile(@"ui\UE5DumpUI\ViewModels\GameClassFilterViewModel.cs"));
        int at = vm.IndexOf("_dump.ListClassesAsync(", StringComparison.Ordinal);
        Assert.True(at > 0, "the ListClassesAsync call site was not found");
        Assert.Contains("limit: ClassListCap", vm.Substring(at, Math.Min(300, vm.Length - at)));
    }

    [Fact]
    public void TheCapNoteOffersMaxOnlyWhileRaisingItIsPossible()
    {
        // Mirrors the VM rule. At the ceiling "raise Max" names a lever the user cannot reach,
        // which is why the clause must be conditional.
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
        // The unconditional wording, asserted absent on its own because the rule above is a
        // reimplementation — a VM that goes back to it while the helper stays is caught here.
        string vm = File.ReadAllText(RepoFile(@"ui\UE5DumpUI\ViewModels\GameClassFilterViewModel.cs"));
        Assert.DoesNotContain("filter to narrow, or raise the cap", vm);
    }
}

/// <summary>
/// The reachable-lever rule, in ONE place: <c>PartialResultNotice.RaiseMaxClause</c> holds the "or raise
/// Max" guard beside the sentence it guards rather than beside each control. These pin the
/// clause, and that each panel listed below actually goes through it.
/// </summary>
public class RaiseMaxClauseTests
{
    private static string RepoFile(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (int i = 0; i < 8 && dir is not null; i++, dir = dir.Parent)
        {
            string candidate = Path.Combine(dir.FullName, relative);
            if (File.Exists(candidate)) return candidate;
        }
        throw new FileNotFoundException($"could not find {relative}");
    }

    [Fact]
    public void ItNamesTheCapBelowTheCeilingAndSaysNothingAtIt()
    {
        Assert.Equal(", or raise Max above 200",
                     PartialResultNotice.RaiseMaxClause(200, Constants.MaxSearchCap));
        Assert.Equal(", or raise Max above 5,000",
                     PartialResultNotice.RaiseMaxClause(5000, Constants.MaxSearchCap));
        // At the ceiling — and, defensively, above it, which a hand-edited settings file can
        // produce in the window before the load clamp runs.
        Assert.Equal("", PartialResultNotice.RaiseMaxClause(
                             Constants.MaxSearchCap, Constants.MaxSearchCap));
        Assert.Equal("", PartialResultNotice.RaiseMaxClause(
                             Constants.MaxSearchCap + 1, Constants.MaxSearchCap));
    }

    [Fact]
    public void EveryPanelThatOffersRaiseMaxGoesThroughIt()
    {
        // ⚠ A panel that re-derives the rule inline is correct on the day it is written and
        // drifts the next time the ceiling moves. Each panel listed here can
        // advise raising its Max, so each must build that clause through RaiseMaxClause, which
        // says nothing once the cap is at the ceiling.
        foreach (var vm in new[]
                 {
                     @"ui\UE5DumpUI\ViewModels\InstanceFinderViewModel.cs",
                     @"ui\UE5DumpUI\ViewModels\PropertySearchViewModel.cs",
                     @"ui\UE5DumpUI\ViewModels\GameClassFilterViewModel.cs",
                     @"ui\UE5DumpUI\ViewModels\ValueSearchViewModel.cs",
                 })
        {
            string src = File.ReadAllText(RepoFile(vm));

            // ⚠ The open paren is load-bearing: it tells a CALL from a mention of the bare name,
            // which a comment beside a hand-rolled copy can make. A test for "goes through X" that
            // a comment can satisfy is not a test.
            Assert.Contains("PartialResultNotice.RaiseMaxClause(", src);

            // And the clause's wording is left to RaiseMaxClause: the phrase is banned outright from
            // these files, not just its interpolated form, so a copy that keeps the phrase in one
            // literal fails, whether it interpolates the number or concatenates it on.
            Assert.DoesNotContain("or raise Max above", src);
        }
    }
}

/// <summary>
/// [W3-CAP-NOSAVE] Every option BuildOptions copies FROM a tracked view model must be in the persist set Track() wires for
/// that view model -- otherwise changing it alone schedules no save, and the value reaches disk only if some OTHER option
/// on the same panel happens to change too. A symmetry pin rather than a one-off assert per option, so the next option
/// missing from its set fails here.
/// </summary>
public class UiOptionsPersistSymmetryTests
{
    private static string RepoFile(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (int i = 0; i < 8 && dir is not null; i++, dir = dir.Parent)
        {
            string candidate = Path.Combine(dir.FullName, relative);
            if (File.Exists(candidate)) return candidate;
        }
        throw new FileNotFoundException($"could not find {relative}");
    }

    // BuildOptions may copy a DERIVED getter whose change is announced under its SOURCE property's name. Each alias names
    // that source, which must itself be in the set -- an alias is a redirection, never an exemption.
    private static readonly Dictionary<string, string> Aliases = new(StringComparer.Ordinal)
    {
        // [W1-SPC-JOINMODE] In-session is launch-scoped, so the file gets Strict in its place.
        ["Spc.JoinModeForOptions"] = "SelectedJoinMode",
    };

    [Fact]
    public void EveryOptionBuildOptionsCopiesFromATrackedViewModel_IsInThatViewModelsPersistSet()
    {
        string src = File.ReadAllText(RepoFile(@"ui\UE5DumpUI\ViewModels\MainWindowViewModel.cs"));
        var (missing, checkedCount) = FindUnpersistedOptions(src);
        // A pattern that silently stops matching would pass this test vacuously, so the checked count has a floor
        // (the main window's own options count too, [R7-D-05]).
        Assert.True(checkedCount >= 50, $"only {checkedCount} tracked options checked -- the BuildOptions pattern no longer matches");
        Assert.True(missing.Count == 0, "changing these alone schedules no save:\n  " + string.Join("\n  ", missing));
    }

    /// <summary>[R7-D-05] The checker must see the MAIN WINDOW's own options too: <c>Track(this, MainPersist)</c>, a
    /// BuildOptions line with a bare right-hand side (<c>o.Main.X = X;</c>) and a set written as <c>nameof(X)</c>. Blind
    /// to them, it would pass a new toolbar option missing from MainPersist -- the [W3-CAP-NOSAVE] shape.</summary>
    [Fact]
    public void TheChecker_SeesAMainWindowOptionMissingFromMainPersist()
    {
        const string synthetic = """
            Track(this, MainPersist);
            Track(A, APersist); Track(B, BPersist); Track(C, CPersist); Track(D, DPersist); Track(E, EPersist);
            Track(F, FPersist); Track(G, GPersist); Track(H, HPersist); Track(I, IPersist);
            private static readonly HashSet<string> MainPersist = new() { nameof(Kept), };
                private UiOptionsSettings BuildOptions()
                {
                    o.Main.Kept = Kept;
                    o.Main.Foo = Foo;
                    return o;
                }
            """;
        var (missing, _) = FindUnpersistedOptions(synthetic.Replace("\r\n", "\n") + "\n");
        Assert.Equal(new[] { "this.Foo (not in MainPersist)" }, missing);
    }

    private static (List<string> Missing, int Checked) FindUnpersistedOptions(string src)
    {
        src = src.Replace("\r\n", "\n");
        var tracks = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Match m in Regex.Matches(src, @"Track\((\w+),\s*(\w+)\);"))
            tracks[m.Groups[1].Value] = m.Groups[2].Value;
        Assert.True(tracks.Count >= 10, $"only {tracks.Count} Track(...) wirings found -- the pattern no longer matches");

        int at = src.IndexOf("private UiOptionsSettings BuildOptions()", StringComparison.Ordinal);
        Assert.True(at > 0, "BuildOptions not found");
        int end = src.IndexOf("\n    }\n", at, StringComparison.Ordinal);
        Assert.True(end > at, "the end of BuildOptions not found");
        string body = src.Substring(at, end - at);

        var missing = new List<string>();
        int checkedCount = 0;
        // `o.X.Y = Vm.Prop;` for a tracked child view model, or -- [R7-D-05] -- `o.Main.Y = Prop;`, the main window's own
        // property, which it tracks as `Track(this, MainPersist)`. The bare form is read on the Main section ONLY: a
        // literal on another line (`= true;`) must not be mistaken for a property.
        foreach (Match m in Regex.Matches(body, @"^\s*o\.(?:\w+\.\w+\s*=\s*(?<vm>\w+)\.(?<p>\w+)|Main\.\w+\s*=\s*(?<p>\w+))\s*;",
                                          RegexOptions.Multiline))
        {
            string vm = m.Groups["vm"].Success ? m.Groups["vm"].Value : "this", prop = m.Groups["p"].Value;
            if (!tracks.TryGetValue(vm, out var setName)) continue;
            if (Aliases.TryGetValue($"{vm}.{prop}", out var source)) prop = source;
            var setMatch = Regex.Match(src, @"HashSet<string>\s+" + setName + @"\s*=\s*new\(\)\s*\{(?<b>.*?)\};",
                                       RegexOptions.Singleline);
            Assert.True(setMatch.Success, $"persist set {setName} not found");
            checkedCount++;
            // `nameof(Vm.Prop)` or -- the main window's own set -- `nameof(Prop)`.
            if (!Regex.IsMatch(setMatch.Groups["b"].Value, @"[.(]" + prop + @"\)"))
                missing.Add($"{vm}.{prop} (not in {setName})");
        }
        return (missing, checkedCount);
    }
}
