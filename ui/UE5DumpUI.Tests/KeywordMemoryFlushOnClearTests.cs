using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UE5DumpUI.Core;
using UE5DumpUI.Helpers;
using UE5DumpUI.Models;
using UE5DumpUI.Services;
using UE5DumpUI.ViewModels;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// Audit #5 AE16 — CLAUDE.md's keyword-search rule says "<c>Flush()</c> before clearing
/// the box on tab-switch/navigation", and eight clear-the-box paths did not.
///
/// <para>
/// The loss is invisible and total. <see cref="KeywordSearchMemory.Schedule"/> only ARMS
/// a 700 ms debounce; the keyword is remembered when it fires. Clearing the box inside
/// that window replaces the text the probe is going to read, so the pass that eventually
/// runs sees "" and remembers nothing — and "type a keyword, look at the matches, press
/// Clear" is the most ordinary sequence in every one of these panels.
/// </para>
/// <para>
/// The finding named <see cref="GameClassFilterViewModel.ClearFiltersCommand"/>. The
/// fix-time sibling grep found seven more, of which the ones reachable headless are
/// covered here; the two Live Walker paths and the Class Pivot handoff need a loaded
/// panel/snapshot and are covered by inspection (their edits are one line each, the
/// identical line, beside an existing <c>Schedule</c>).
/// </para>
/// <para>
/// NEGATIVE CONTROL for every test below: delete the <c>_filterMemory.Flush()</c> line
/// from the command under test and the history assertion fails, because the debounce
/// never fires inside a synchronous test.
/// </para>
/// </summary>
public class KeywordMemoryFlushOnClearTests
{
    /// <summary>The direct rail: this is exactly what the missing line cost.</summary>
    [Fact]
    public void A_scheduled_keyword_is_lost_unless_something_flushes_it()
    {
        var text = "health";
        var mem = new KeywordSearchMemory(() => (text, true));
        mem.Schedule(text);
        Assert.Empty(mem.History);      // still only ARMED — nothing remembered yet

        mem.Flush();
        Assert.Contains("health", mem.History);
    }

    /// <summary>And the reason the order matters: flushing AFTER the box is blanked
    /// probes the blank text and remembers nothing. This is the bug written as a
    /// test — it is what "Flush() before clearing" is protecting against.</summary>
    [Fact]
    public void Flushing_after_the_box_is_blanked_remembers_nothing()
    {
        var text = "health";
        var mem = new KeywordSearchMemory(() => (text, true));
        mem.Schedule(text);
        text = "";                      // the clear happened first
        mem.Flush();
        Assert.Empty(mem.History);
    }

    // ── The panels ──────────────────────────────────────────────────────────
    //
    // Every one of these filters rebuilds its bound Results collection from a PRIVATE
    // full list, so a hand-seeded Results row is wiped by the first keystroke and the
    // memory's "did it match anything?" probe sees zero. Each test therefore loads
    // through the real command with a stub service — which is also the only way the
    // probe is exercised the way it runs in the app.

    private sealed class NoopPlatform : IPlatformService
    {
        public bool TryAcquireSingleInstance() => true;
        public void ReleaseSingleInstance() { }
        public string GetAppDataPath() => System.IO.Path.GetTempPath();
        public string GetLogDirectoryPath() => System.IO.Path.GetTempPath();
        public Task<bool> CopyToClipboardAsync(string text) => Task.FromResult(true);
        public Task RevealInExplorerAsync(string path) => Task.CompletedTask;
        public string GetMachineName() => "TEST";
        public void CloseImeForWindow(IntPtr windowHandle) { }
        public Task<string?> ShowSaveFileDialogAsync(string a, string b, string c)
            => Task.FromResult<string?>(null);
    }

    private static AllFunctionEntry Func(string cls, string name, uint flags = 0)
        => new() { ClassName = cls, FuncName = name, FuncAddr = "0xF00D", FunctionFlags = flags };

    private const uint FuncExec = 0x0000_0200;

    private sealed class LoadableDump : StubDumpService
    {
        public List<GameClassEntry> Classes = new();
        public List<AllFunctionEntry> Functions = new();
        public List<PeProfileEntry> PeEntries = new();
        public List<PropertySearchMatch> Props = new();

        public override Task<ClassListResult> ListClassesAsync(
            bool gameOnly = true, int limit = 5000, CancellationToken ct = default)
            => Task.FromResult(new ClassListResult
            {
                Classes = Classes, Total = Classes.Count, TotalClasses = Classes.Count,
                RequestedLimit = limit,
            });

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

    [Fact]
    public async Task AE16_GameClassFilter_ClearFilters_keeps_the_keyword()
    {
        var dump = new LoadableDump();
        dump.Classes.Add(new GameClassEntry
        {
            ClassName = "BP_Hero_C", SuperName = "Character", ClassPath = "/Game/BP_Hero_C",
        });
        var vm = new GameClassFilterViewModel(dump, new MockLoggingService(), new NoopPlatform());
        await vm.LoadCommand.ExecuteAsync(null);

        vm.FilterText = "hero";                       // OnFilterTextChanged -> Schedule
        Assert.NotEmpty(vm.Results);                  // the probe's "has matches" leg
        Assert.Empty(vm.FilterHistory);               // ... still only ARMED

        vm.ClearFiltersCommand.Execute(null);
        Assert.Contains("hero", vm.FilterHistory);
        Assert.Equal("", vm.FilterText);
    }

    [Fact]
    public async Task AE16_Console_ClearFilter_keeps_the_keyword()
    {
        var dump = new LoadableDump();
        dump.Functions.Add(Func("CheatManager", "FlyMode", FuncExec));
        var vm = new ConsoleViewModel(dump, new MockLoggingService());
        await vm.LoadCommand.ExecuteAsync(null);

        vm.FilterText = "fly";
        Assert.NotEmpty(vm.Results);

        vm.ClearFilterCommand.Execute(null);
        Assert.Contains("fly", vm.FilterHistory);
        Assert.Equal("", vm.FilterText);
    }

    [Fact]
    public async Task AE16_LiveFuncs_Clear_button_keeps_the_keyword()
    {
        var dump = new LoadableDump();
        dump.PeEntries.Add(new PeProfileEntry
        {
            ClassName = "BP_Hero_C", FuncName = "Dash", FuncAddr = "0xF00D", Count = 3,
        });
        var vm = new LiveFuncsViewModel(dump, new MockLoggingService());
        await vm.RefreshCommand.ExecuteAsync(null);

        vm.FilterText = "dash";
        Assert.NotEmpty(vm.Results);

        // OnLeavingTab already flushed; the Clear BUTTON did not, and it is the one
        // reachable while the user is still looking at the matches.
        vm.ClearCommand.Execute(null);
        Assert.Contains("dash", vm.FilterHistory);
        Assert.Equal("", vm.FilterText);
    }

    [Fact]
    public async Task AE16_InterestingFunctions_ClearFilters_keeps_the_keyword()
    {
        var dump = new LoadableDump();
        dump.Functions.Add(Func("BP_Hero_C", "Dash"));
        var vm = new InterestingFunctionsViewModel(dump, new MockLoggingService());
        await vm.LoadCommand.ExecuteAsync(null);
        vm.ShowAll = true;               // score-threshold independent

        vm.FilterText = "dash";
        Assert.NotEmpty(vm.Results);

        vm.ClearFiltersCommand.Execute(null);
        Assert.Contains("dash", vm.FilterHistory);
        Assert.Equal("", vm.FilterText);
    }

    [Fact]
    public async Task AE16_InterestingProperties_ClearFilters_keeps_the_keyword()
    {
        var dump = new LoadableDump();
        dump.Props.Add(new PropertySearchMatch
        {
            ClassName = "BP_Hero_C", PropName = "Health", PropType = "FloatProperty",
        });
        var vm = new InterestingPropertiesViewModel(dump, new MockLoggingService());
        await vm.LoadCommand.ExecuteAsync(null);
        vm.ShowAll = true;

        vm.FilterText = "health";
        Assert.NotEmpty(vm.Results);

        vm.ClearFiltersCommand.Execute(null);
        Assert.Contains("health", vm.FilterHistory);
        Assert.Equal("", vm.FilterText);
    }
}
