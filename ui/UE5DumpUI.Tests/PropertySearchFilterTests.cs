using UE5DumpUI.Core;
using UE5DumpUI;
using UE5DumpUI.Models;
using UE5DumpUI.ViewModels;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// Audit #3 M10: the client-side ResultFilter box must follow the shared keyword-box
/// MUST-rule — space = AND (term-level AND, field-level OR) via
/// ObjectTreeFilter.MatchesAllTerms, and per-keyword memory via KeywordSearchMemory.
/// Before the fix it matched the whole string with one Contains per field, so a
/// two-word query like "max health" (never a literal substring of any single field)
/// found nothing.
/// </summary>
public class PropertySearchFilterTests
{
    private sealed class SearchDump : StubDumpService
    {
        public List<PropertySearchMatch> Next { get; set; } = new();
        public override Task<PropertySearchResult> SearchPropertiesAsync(
            string query, string[]? types = null, bool gameOnly = true, bool deep = false,
            int limit = 200, CancellationToken ct = default)
            => Task.FromResult(new PropertySearchResult { Results = Next });
    }

    private sealed class NoopLog : ILoggingService
    {
        public void Info(string message) { }
        public void Warn(string message) { }
        public void Error(string message) { }
        public void Error(string message, Exception ex) { }
        public void Debug(string message) { }
        public void Info(string category, string message) { }
        public void Warn(string category, string message) { }
        public void Error(string category, string message) { }
        public void Error(string category, string message, Exception ex) { }
        public void Debug(string category, string message) { }
        public void StartProcessMirror(string processName) { }
        public void StopProcessMirror() { }
    }

    private static async Task<PropertySearchViewModel> SearchedVm(params PropertySearchMatch[] rows)
    {
        var dump = new SearchDump { Next = new List<PropertySearchMatch>(rows) };
        var vm = new PropertySearchViewModel(dump, new NoopLog()) { SearchQuery = "x" };
        await vm.SearchCommand.ExecuteAsync(null);   // populates _allResults + Results
        return vm;
    }

    private static PropertySearchMatch Row(string cls, string prop, string type = "FloatProperty") =>
        new() { ClassName = cls, DefiningClassName = cls, PropName = prop, PropType = type };

    [Fact]
    public async Task ResultFilter_SpaceIsAnd_WithFieldLevelOr()
    {
        var vm = await SearchedVm(
            Row("BP_PlayerState_C", "MaxHealth"),        // "max"+"health" both in PropName
            Row("BP_PlayerState_C", "CurrentHealth"),    // only "health"
            Row("BP_MaxCombo_C",    "Value", "IntProperty")); // only "max" (in ClassName)

        vm.ResultFilter = "max health";
        vm.ApplyResultFilter();   // deterministic (bypass the 150 ms debounce)

        // Term-level AND + field-level OR: only MaxHealth has BOTH terms (each matching
        // some field). The old whole-string Contains found the literal "max health" in
        // no field → zero rows.
        Assert.Single(vm.Results);
        Assert.Equal("MaxHealth", vm.Results[0].PropName);
        vm.Dispose();
    }

    [Fact]
    public async Task ResultFilter_TermMatchesClassOrType_NotJustPropName()
    {
        var vm = await SearchedVm(
            Row("BP_Enemy_C", "Awareness", "FloatProperty"),
            Row("BP_Ally_C",  "Health",    "IntProperty"));

        // "enemy" matches only the class, "float" matches only the type — both on row 1.
        vm.ResultFilter = "enemy float";
        vm.ApplyResultFilter();

        Assert.Single(vm.Results);
        Assert.Equal("Awareness", vm.Results[0].PropName);
        vm.Dispose();
    }

    [Fact]
    public async Task ResultFilter_Empty_ShowsAll()
    {
        var vm = await SearchedVm(Row("A", "One"), Row("B", "Two"));
        vm.ResultFilter = "";
        vm.ApplyResultFilter();
        Assert.Equal(2, vm.Results.Count);
        vm.Dispose();
    }

    [Fact]
    public void ResultFilterHistory_IsExposed_ForAutoCompleteBinding()
    {
        var vm = new PropertySearchViewModel(new SearchDump(), new NoopLog());
        Assert.NotNull(vm.ResultFilterHistory);   // bound to the AutoCompleteBox ItemsSource
        vm.Dispose();
    }

    // ==================================================================
    // Audit #5 Z4 / Z10 — the cap must reach BOTH surfaces, and the advice
    // must name a control the panel actually has.
    // ==================================================================

    private sealed class CappedSearchDump : StubDumpService
    {
        public PropertySearchResult Next { get; set; } = new();
        public override Task<PropertySearchResult> SearchPropertiesAsync(
            string query, string[]? types = null, bool gameOnly = true, bool deep = false,
            int limit = 200, CancellationToken ct = default) => Task.FromResult(Next);
    }

    /// <param name="configure">Applied BEFORE the search runs — the cap is read while the
    /// status line is composed, so setting it afterwards would test nothing.</param>
    private static async Task<PropertySearchViewModel> VmFor(
        PropertySearchResult result, Action<PropertySearchViewModel>? configure = null)
    {
        var vm = new PropertySearchViewModel(new CappedSearchDump { Next = result },
                                             new NoopLog()) { SearchQuery = "Health" };
        configure?.Invoke(vm);
        await vm.SearchCommand.ExecuteAsync(null);
        return vm;
    }

    /// <summary>
    /// The class-noise picker presents its per-class hit counts as a census of the
    /// result. On a capped search they are a lower bound — and this very method already
    /// read the same flags fifteen lines later to print its own cap warning, so the
    /// panel warned "capped" in one place while the picker beside it implied a complete
    /// tally. The "⚠ Counts are partial" string has existed in en.axaml the whole time
    /// and could never appear here.
    /// </summary>
    [Fact]
    public async Task Truncated_search_marks_the_class_picker_counts_as_partial()
    {
        var vm = await VmFor(new PropertySearchResult
        {
            Total = 200, Truncated = true,
            Results = new List<PropertySearchMatch> { Row("BP_Enemy_C", "Health") },
        });

        Assert.True(vm.ClassFilter.CountsPartial);
        vm.Dispose();
    }

    [Fact]
    public async Task Aborted_search_marks_the_class_picker_counts_as_partial()
    {
        var vm = await VmFor(new PropertySearchResult
        {
            Total = 7, Aborted = true,
            Results = new List<PropertySearchMatch> { Row("BP_Enemy_C", "Health") },
        });

        Assert.True(vm.ClassFilter.CountsPartial);
        vm.Dispose();
    }

    /// <summary>Negative control — a complete search must NOT cry wolf.</summary>
    [Fact]
    public async Task Complete_search_leaves_the_class_picker_counts_exact()
    {
        var vm = await VmFor(new PropertySearchResult
        {
            Total = 1,
            Results = new List<PropertySearchMatch> { Row("BP_Enemy_C", "Health") },
        });

        Assert.False(vm.ClassFilter.CountsPartial);
        vm.Dispose();
    }

    /// <summary>
    /// Z10, then [PROPSEARCHCAP-2026-08-19]. The rule Z10 established is "never name a lever
    /// the user cannot reach", and it happened to be satisfied by deleting "raise Max" because
    /// this panel had no Max. It has one now, so the SAME rule requires the advice back —
    /// while the cap can still go up. ⚠ This test was rewritten rather than deleted: the
    /// invariant did not change, only which side of it the panel is on.
    /// </summary>
    [Fact]
    public async Task Cap_advice_offers_Max_only_because_the_panel_now_owns_that_lever()
    {
        var vm = await VmFor(new PropertySearchResult
        {
            Total = 200, Truncated = true,
            Results = new List<PropertySearchMatch> { Row("BP_Enemy_C", "Health") },
        });

        Assert.Contains("STOPPED at the 200-row cap", vm.StatusText);
        Assert.Contains("raise Max above 200", vm.StatusText);
        // ...and it still names the levers it always did.
        Assert.Contains("Type filter", vm.StatusText);
        vm.Dispose();
    }

    /// <summary>
    /// The other half of the same rule, and the one that keeps this honest as the cap moves:
    /// at the ceiling "raise Max" is exactly the lie Z10 removed, just in a new place.
    /// </summary>
    [Fact]
    public async Task Cap_advice_stops_offering_Max_once_the_cap_is_at_the_ceiling()
    {
        var vm = await VmFor(new PropertySearchResult
        {
            Total = Constants.MaxSearchCap, Truncated = true,
            Results = new List<PropertySearchMatch> { Row("BP_Enemy_C", "Health") },
        }, vm => vm.PropertySearchCap = Constants.MaxSearchCap);

        Assert.DoesNotContain("raise Max", vm.StatusText);
        Assert.Contains("Type filter", vm.StatusText);
        vm.Dispose();
    }

    /// <summary>With "Game classes only" already on, that lever is spent — don't offer
    /// it. Advice the user has already followed is noise.</summary>
    [Fact]
    public async Task Cap_advice_offers_GameClassesOnly_only_while_it_is_still_off()
    {
        var capped = new PropertySearchResult
        {
            Total = 200, Truncated = true,
            Results = new List<PropertySearchMatch> { Row("BP_Enemy_C", "Health") },
        };

        var on = new PropertySearchViewModel(new CappedSearchDump { Next = capped },
                     new NoopLog()) { SearchQuery = "Health", GameClassesOnly = true };
        await on.SearchCommand.ExecuteAsync(null);
        Assert.DoesNotContain("Game classes only", on.StatusText);
        on.Dispose();

        var off = new PropertySearchViewModel(new CappedSearchDump { Next = capped },
                      new NoopLog()) { SearchQuery = "Health", GameClassesOnly = false };
        await off.SearchCommand.ExecuteAsync(null);
        Assert.Contains("Game classes only", off.StatusText);
        off.Dispose();
    }
}
