using System.Collections;
using System.Collections.Specialized;
using System.Threading;
using System.Threading.Tasks;
using UE5DumpUI.Helpers;
using UE5DumpUI.Models;
using UE5DumpUI.Services;
using UE5DumpUI.ViewModels;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// [KEYWORD-BOX-VIEW-KEEP] Every client-side keyword box rebuilt its list on every edit, and
/// the rebuild (a <c>Clear()</c> + re-add) throws the grid back to its first row and drops the
/// selection -- even when the edit shows exactly the rows already shown (a trailing space, one
/// more letter of a name every row already matches). The maintainer's rules (2026-09-27):
/// <list type="bullet">
/// <item>rows that did not change are not rebuilt -- no <see cref="INotifyCollectionChanged"/>
/// event, and the selected row still selected;</item>
/// <item>a rebuild hands the View its selection and top row to put back (Narrowed);</item>
/// <item>a keyword cleared from 2+ characters to empty asks the View for Cleared (first
/// selected row to the top, or the top row kept at the top).</item>
/// </list>
/// Each box is driven through its real filter property; the scrolling itself is the View's
/// (<c>Views/FilterViewBinding</c>), proven live.
/// </summary>
public class FilterBoxViewKeepTests
{
    // ── The per-box adapter ─────────────────────────────────────────────────

    /// <summary>One keyword box: <see cref="Wide"/> shows two rows, <see cref="Same"/> the same
    /// two, <see cref="Narrow"/> one of them.</summary>
    public sealed record Box(
        Action<string> SetFilter,
        IList Rows,
        Func<object?> Selected,
        Action<object?> Select,
        FilterViewKeeper Keeper,
        string Wide, string Same, string Narrow);

    public static TheoryData<string> Boxes => new()
    {
        "Console", "InterestingFunctions", "InterestingProperties", "LiveFuncs", "DetectStats",
    };

    private static Task<Box> Make(string name) => name switch
    {
        "Console"               => ConsoleBox(),
        "InterestingFunctions"  => InterestingFunctionsBox(),
        "InterestingProperties" => InterestingPropertiesBox(),
        "LiveFuncs"             => LiveFuncsBox(),
        "DetectStats"           => Task.FromResult(DetectStatsBox()),
        _ => throw new ArgumentException(name),
    };

    /// <summary>Stands in for the View: records every restore the keeper asks for.</summary>
    private static List<(FilterViewState State, FilterViewRestore Mode)> AttachView(Box box)
    {
        var got = new List<(FilterViewState, FilterViewRestore)>();
        box.Keeper.CaptureView = () => new FilterViewState(
            box.Selected() is { } s ? new[] { s } : Array.Empty<object>(),
            box.Rows.Count > 0 ? box.Rows[0] : null);
        box.Keeper.RestoreView = (state, mode) => got.Add((state, mode));
        return got;
    }

    private static Func<int> Watch(IList list)
    {
        int n = 0;
        ((INotifyCollectionChanged)list).CollectionChanged += (_, _) => n++;
        return () => n;
    }

    // ── The rules ───────────────────────────────────────────────────────────

    [Theory, MemberData(nameof(Boxes))]
    public async Task Same_rows_keep_the_list_and_the_selection(string name)
    {
        var box = await Make(name);
        box.SetFilter(box.Wide);
        Assert.Equal(2, box.Rows.Count);                 // the premise: two rows match
        box.Select(box.Rows[1]);
        var picked = box.Selected();
        var changes = Watch(box.Rows);
        var got = AttachView(box);

        box.SetFilter(box.Same);                         // the same two rows

        Assert.Equal(0, changes());
        Assert.Same(picked, box.Selected());
        Assert.Empty(got);
    }

    [Theory, MemberData(nameof(Boxes))]
    public async Task A_narrowing_hands_the_View_its_selection_to_keep(string name)
    {
        var box = await Make(name);
        box.SetFilter(box.Wide);
        box.Select(box.Rows[0]);
        var picked = box.Selected();
        var got = AttachView(box);

        box.SetFilter(box.Narrow);

        Assert.Single(box.Rows);
        var (state, mode) = Assert.Single(got);
        Assert.Equal(FilterViewRestore.Narrowed, mode);
        Assert.Same(picked, Assert.Single(state.Selected));
    }

    [Theory, MemberData(nameof(Boxes))]
    public async Task Clearing_a_real_keyword_asks_the_View_for_Cleared(string name)
    {
        var box = await Make(name);
        box.SetFilter(box.Narrow);
        box.Select(box.Rows[0]);
        var picked = box.Selected();
        var got = AttachView(box);

        box.SetFilter("");                               // select all, Delete

        Assert.True(box.Rows.Count > 1);
        var (state, mode) = Assert.Single(got);
        Assert.Equal(FilterViewRestore.Cleared, mode);
        Assert.Same(picked, Assert.Single(state.Selected));
    }

    // ── The boxes ───────────────────────────────────────────────────────────

    private const uint FuncExec = 0x0000_0200;

    private static AllFunctionEntry Func(string cls, string name, uint flags = 0)
        => new() { ClassName = cls, FuncName = name, FuncAddr = "0xF00D", FunctionFlags = flags };

    private sealed class LoadableDump : StubDumpService
    {
        public List<AllFunctionEntry> Functions = new();
        public List<PeProfileEntry> PeEntries = new();
        public List<PropertySearchMatch> Props = new();

        public override Task<AllFunctionsResult> ListAllFunctionsAsync(
            bool gameOnly = true, int limit = 100000, CancellationToken ct = default)
            => Task.FromResult(new AllFunctionsResult
            {
                Functions = Functions, Total = Functions.Count, ScannedClasses = 1,
            });

        public override Task<PeProfileResult> PeProfileGetAsync(
            int limit = 200, CancellationToken ct = default)
            => Task.FromResult(new PeProfileResult
            {
                Entries = PeEntries, DistinctFuncs = PeEntries.Count,
            });

        public override Task<PropertySearchBatchResult> SearchPropertiesBatchAsync(
            string[] queries, string[]? types = null, bool gameOnly = true,
            int limitPerQuery = 200, CancellationToken ct = default)
        {
            var per = new List<PropertySearchQueryEnvelope>();
            foreach (var q in queries)
                per.Add(new PropertySearchQueryEnvelope
                {
                    Query = q, MatchCount = Props.Count, Results = Props,
                });
            return Task.FromResult(new PropertySearchBatchResult
            {
                QueryCount = queries.Length, Total = Props.Count, PerQuery = per,
            });
        }
    }

    private static async Task<Box> ConsoleBox()
    {
        var dump = new LoadableDump();
        dump.Functions.Add(Func("CheatManager", "FlyMode", FuncExec));
        dump.Functions.Add(Func("CheatManager", "FlySpeed", FuncExec));
        dump.Functions.Add(Func("CheatManager", "God", FuncExec));
        var vm = new ConsoleViewModel(dump, new MockLoggingService());
        await vm.LoadCommand.ExecuteAsync(null);
        return new Box(t => vm.FilterText = t, vm.Results, () => vm.SelectedResult,
                       o => vm.SelectedResult = (AllFunctionEntry?)o, vm.ResultsView,
                       "fl", "fly", "flym");
    }

    private static async Task<Box> InterestingFunctionsBox()
    {
        var dump = new LoadableDump();
        dump.Functions.Add(Func("BP_Hero_C", "Dash"));
        dump.Functions.Add(Func("BP_Hero_C", "DashCharge"));
        dump.Functions.Add(Func("BP_Hero_C", "Jump"));
        var vm = new InterestingFunctionsViewModel(dump, new MockLoggingService());
        await vm.LoadCommand.ExecuteAsync(null);
        vm.ShowAll = true;               // score-threshold independent
        return new Box(t => vm.FilterText = t, vm.Results, () => vm.SelectedResult,
                       o => vm.SelectedResult = (ScoredFunctionRow?)o, vm.ResultsView,
                       "da", "das", "dashc");
    }

    private static async Task<Box> InterestingPropertiesBox()
    {
        var dump = new LoadableDump();
        foreach (var name in new[] { "Health", "HealthMax", "Gold" })
            dump.Props.Add(new PropertySearchMatch
            {
                ClassName = "BP_Unit_C", PropName = name, PropType = "FloatProperty",
            });
        var vm = new InterestingPropertiesViewModel(dump, new MockLoggingService());
        await vm.LoadCommand.ExecuteAsync(null);
        vm.ShowAll = true;
        return new Box(t => vm.FilterText = t, vm.Results, () => vm.SelectedResult,
                       o => vm.SelectedResult = (ScoredPropertyRow?)o, vm.ResultsView,
                       "he", "hea", "healthm");
    }

    private static async Task<Box> LiveFuncsBox()
    {
        var dump = new LoadableDump();
        foreach (var (name, count) in new[] { ("Dash", 5L), ("DashEnd", 4L), ("Jump", 3L) })
            dump.PeEntries.Add(new PeProfileEntry
            {
                ClassName = "BP_Hero_C", FuncName = name, FuncAddr = "0xF00D", Count = count,
            });
        var vm = new LiveFuncsViewModel(dump, new MockLoggingService());
        await vm.RefreshCommand.ExecuteAsync(null);
        return new Box(t => vm.FilterText = t, vm.Results, () => vm.SelectedResult,
                       o => vm.SelectedResult = (PeProfileEntry?)o, vm.ResultsView,
                       "da", "das", "dashe");
    }

    private static DetectedStat Stat(string prop) => new()
    {
        Match = new PropertySearchMatch { ClassName = "BP_Unit_C", PropName = prop, PropType = "FloatProperty" },
        Category = PropertyCategory.Other, BaseScore = 10, Confidence = 10, IsConfirmed = false,
    };

    private static Box DetectStatsBox()
    {
        // "hea", not "he": the filter also matches the category label, and "Other" holds "he".
        var vm = new DetectStatsViewModel(new StubDumpService(), new MockLoggingService());
        vm.SeedForTests(new[] { Stat("Health"), Stat("HealthMax"), Stat("Gold") });
        return new Box(t => vm.FilterText = t, vm.Results, () => vm.SelectedResult,
                       o => vm.SelectedResult = (DetectedStat?)o, vm.ResultsView,
                       "hea", "heal", "healthm");
    }
}
