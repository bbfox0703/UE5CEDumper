using System.Collections.Specialized;
using System.Threading;
using System.Threading.Tasks;
using UE5DumpUI.Models;
using UE5DumpUI.Services;
using UE5DumpUI.ViewModels;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// [KEYWORD-BOX-VIEW-KEEP] Every client-side keyword box rebuilt its list on every edit, and
/// the rebuild (a <c>Clear()</c> + re-add) throws the grid back to its first row and drops the
/// selection -- even when the edit shows exactly the rows already shown (a trailing space, one
/// more letter of a name every row already matches). The maintainer's rule (2026-09-27): rows
/// that did not change are not rebuilt. These pin it per box, through the real filter property:
/// no <see cref="INotifyCollectionChanged"/> event, and the selected row still selected.
/// </summary>
public class FilterBoxViewKeepTests
{
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

    /// <summary>Counts the list's change notifications.</summary>
    private static Func<int> Watch(INotifyCollectionChanged list)
    {
        int n = 0;
        list.CollectionChanged += (_, _) => n++;
        return () => n;
    }

    // ── Console ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Console_same_rows_keep_the_list_and_the_selection()
    {
        var dump = new LoadableDump();
        dump.Functions.Add(Func("CheatManager", "FlyMode", FuncExec));
        dump.Functions.Add(Func("CheatManager", "FlySpeed", FuncExec));
        dump.Functions.Add(Func("CheatManager", "God", FuncExec));
        var vm = new ConsoleViewModel(dump, new MockLoggingService());
        await vm.LoadCommand.ExecuteAsync(null);
        vm.FilterText = "fl";
        Assert.Equal(2, vm.Results.Count);          // the premise: two rows match
        vm.SelectedResult = vm.Results[1];
        var picked = vm.SelectedResult;
        var changes = Watch(vm.Results);

        vm.FilterText = "fly";                          // the same two rows

        Assert.Equal(0, changes());
        Assert.Same(picked, vm.SelectedResult);
    }

    // ── Interesting Functions ───────────────────────────────────────────────

    [Fact]
    public async Task InterestingFunctions_same_rows_keep_the_list_and_the_selection()
    {
        var dump = new LoadableDump();
        dump.Functions.Add(Func("BP_Hero_C", "Dash"));
        dump.Functions.Add(Func("BP_Hero_C", "DashCharge"));
        dump.Functions.Add(Func("BP_Hero_C", "Jump"));
        var vm = new InterestingFunctionsViewModel(dump, new MockLoggingService());
        await vm.LoadCommand.ExecuteAsync(null);
        vm.ShowAll = true;
        vm.FilterText = "da";
        Assert.Equal(2, vm.Results.Count);          // the premise: two rows match
        vm.SelectedResult = vm.Results[1];
        var picked = vm.SelectedResult;
        var changes = Watch(vm.Results);

        vm.FilterText = "das";

        Assert.Equal(0, changes());
        Assert.Same(picked, vm.SelectedResult);
    }

    // ── Interesting Properties ──────────────────────────────────────────────

    [Fact]
    public async Task InterestingProperties_same_rows_keep_the_list_and_the_selection()
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
        vm.FilterText = "he";
        Assert.Equal(2, vm.Results.Count);
        vm.SelectedResult = vm.Results[1];
        var picked = vm.SelectedResult;
        var changes = Watch(vm.Results);

        vm.FilterText = "hea";

        Assert.Equal(0, changes());
        Assert.Same(picked, vm.SelectedResult);
    }

    // ── Live Funcs ──────────────────────────────────────────────────────────

    [Fact]
    public async Task LiveFuncs_same_rows_keep_the_list_and_the_selection()
    {
        var dump = new LoadableDump();
        foreach (var (name, count) in new[] { ("Dash", 5L), ("DashEnd", 4L), ("Jump", 3L) })
            dump.PeEntries.Add(new PeProfileEntry
            {
                ClassName = "BP_Hero_C", FuncName = name, FuncAddr = "0xF00D", Count = count,
            });
        var vm = new LiveFuncsViewModel(dump, new MockLoggingService());
        await vm.RefreshCommand.ExecuteAsync(null);
        vm.FilterText = "da";
        Assert.Equal(2, vm.Results.Count);          // the premise: two rows match
        vm.SelectedResult = vm.Results[1];
        var picked = vm.SelectedResult;
        var changes = Watch(vm.Results);

        vm.FilterText = "das";

        Assert.Equal(0, changes());
        Assert.Same(picked, vm.SelectedResult);
    }

    // ── Detect Stats ────────────────────────────────────────────────────────

    private static DetectedStat Stat(string prop) => new()
    {
        Match = new PropertySearchMatch { ClassName = "BP_Unit_C", PropName = prop, PropType = "FloatProperty" },
        Category = PropertyCategory.Other, BaseScore = 10, Confidence = 10, IsConfirmed = false,
    };

    [Fact]
    public void DetectStats_same_rows_keep_the_list_and_the_selection()
    {
        var vm = new DetectStatsViewModel(new StubDumpService(), new MockLoggingService());
        vm.SeedForTests(new[] { Stat("Health"), Stat("HealthMax"), Stat("Gold") });
        vm.FilterText = "hea";
        Assert.Equal(2, vm.Results.Count);          // the premise: two rows match
        vm.SelectedResult = vm.Results[1];
        var picked = vm.SelectedResult;
        var changes = Watch(vm.Results);

        vm.FilterText = "heal";

        Assert.Equal(0, changes());
        Assert.Same(picked, vm.SelectedResult);
    }
}
