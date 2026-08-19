using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UE5DumpUI.Core;
using UE5DumpUI.Models;
using UE5DumpUI.ViewModels;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// Audit #5 L8, findings Z6 / Z7 / Z12 on <see cref="InstanceFinderViewModel"/>.
///
/// <para>
/// Z6 — a reverse-address lookup bumped the class-search generation without owning the
/// flag that generation guards, stranding the panel's ProgressBar spinning forever.
/// Z7 — the client-side filter promised to preserve the selection AND its loaded field
/// grid, and did neither: it blanked the grid and re-issued a <c>walk_instance</c> for
/// the address already loaded, twice per class-noise tick.
/// Z12 — the "[scanned X/Y]" suffix that exists to separate a clean miss from a
/// truncated scan is now built from the pass that actually ran.
/// </para>
/// </summary>
public class InstanceFinderViewModelTests
{
    // ── stubs ────────────────────────────────────────────────────────────

    private sealed class FakeDump : StubDumpService
    {
        /// <summary>Gate a class search so a test can hold one "in flight".</summary>
        public TaskCompletionSource<FindInstancesResult>? SearchGate { get; set; }
        public FindInstancesResult NextSearch { get; set; } = new();
        public AddressLookupResult NextLookup { get; set; } = new();

        public int WalkInstanceCalls { get; private set; }

        public override Task<FindInstancesResult> FindInstancesAsync(
            string className, bool exactMatch = false, int limit = 500, bool newestFirst = false,
            string nameFilter = "", IReadOnlyList<string>? excludeClasses = null,
            CancellationToken ct = default)
            => SearchGate?.Task ?? Task.FromResult(NextSearch);

        public override Task<AddressLookupResult> FindByAddressAsync(
            string addr, int containerElemCap = 256, CancellationToken ct = default)
            => Task.FromResult(NextLookup);

        public override Task<InstanceWalkResult> WalkInstanceAsync(
            string addr, string? classAddr = null, int arrayLimit = 64, int previewLimit = 2,
            bool fillGaps = false, bool lean = false, CancellationToken ct = default)
        {
            WalkInstanceCalls++;
            return Task.FromResult(new InstanceWalkResult
            {
                Address = addr,
                Fields = new List<LiveFieldValue>
                {
                    new() { Name = "Health", Offset = 0x40, TypedValue = "100" },
                },
            });
        }
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

    private static InstanceFinderViewModel NewVm(FakeDump dump)
        => new(dump, new NoopLog(), new NoopPlatform());

    private static FindInstancesResult Result(params (string name, string cls)[] rows)
    {
        var r = new FindInstancesResult { Scanned = 1000, NonNull = 900, Named = 800 };
        int i = 0;
        foreach (var (name, cls) in rows)
            r.Instances.Add(new InstanceResult
            {
                Address = $"0x{0x1000 + (i++ * 0x100):X}", Name = name, ClassName = cls,
            });
        return r;
    }

    // ==================================================================
    // Z6 — whoever bumps the search generation takes over IsSearching.
    // ==================================================================

    /// <summary>
    /// The exact stranding. A class search is in flight (IsSearching true). The user
    /// runs a reverse-address lookup, which bumps <c>_searchGen</c> so the pending
    /// response can't overwrite the single lookup result — but the lookup owns
    /// <c>IsLookingUp</c>, not <c>IsSearching</c>. The superseded search's finally then
    /// skips its own clear ("only the latest op owns the flag") and nothing else runs,
    /// so the indeterminate ProgressBar animates for the rest of the session with
    /// nothing in flight.
    /// </summary>
    [Fact]
    public async Task LookupAddress_releases_the_search_spinner_it_supersedes()
    {
        var gate = new TaskCompletionSource<FindInstancesResult>();
        var dump = new FakeDump { SearchGate = gate };
        var vm = NewVm(dump);
        vm.SearchClassName = "Pawn";

        var search = vm.SearchCommand.ExecuteAsync(null);
        Assert.True(vm.IsSearching, "precondition: the class search owns the spinner");

        // Supersede it with a reverse-address lookup (which owns IsLookingUp, not
        // IsSearching — that asymmetry is the whole finding).
        dump.SearchGate = null;
        vm.LookupAddress = "0x7FF700001000";
        await vm.LookupAddressCommand.ExecuteAsync(null);

        Assert.False(vm.IsSearching);

        // Let the superseded search land. Its finally sees a bumped generation and
        // skips its own clear, so the flag must ALREADY be false and stay false.
        gate.SetResult(Result(("Old", "Pawn")));
        await search;
        Assert.False(vm.IsSearching);
    }

    /// <summary>
    /// ClearOnDisconnect is the SIBLING bumper — same invariant, same defect, and it is
    /// reached on every pipe drop rather than only on a deliberate lookup.
    /// </summary>
    [Fact]
    public async Task ClearOnDisconnect_releases_the_search_spinner_it_supersedes()
    {
        var gate = new TaskCompletionSource<FindInstancesResult>();
        var dump = new FakeDump { SearchGate = gate };
        var vm = NewVm(dump);
        vm.SearchClassName = "Pawn";

        var search = vm.SearchCommand.ExecuteAsync(null);
        Assert.True(vm.IsSearching);

        vm.ClearOnDisconnect();

        Assert.False(vm.IsSearching);
        gate.SetResult(Result(("Old", "Pawn")));
        await search;
        Assert.False(vm.IsSearching);
    }

    /// <summary>Negative control: an ordinary search still clears its own flag, so the
    /// fix above cannot be "IsSearching is never true".</summary>
    [Fact]
    public async Task An_uninterrupted_search_still_owns_and_clears_the_spinner()
    {
        var dump = new FakeDump { NextSearch = Result(("Hero", "BP_Hero_C")) };
        var vm = NewVm(dump);
        vm.SearchClassName = "BP";

        await vm.SearchCommand.ExecuteAsync(null);

        Assert.False(vm.IsSearching);
        Assert.Single(vm.Instances);
    }

    // ==================================================================
    // Z7 — the filter must actually preserve the loaded field grid.
    // ==================================================================

    /// <summary>
    /// UiCollection.Reset calls its detach callback unconditionally, so
    /// <c>SelectedInstance = null</c> fired the changed-handler and cleared Fields; the
    /// restore then re-entered the handler and issued a FRESH walk_instance for the
    /// address already loaded. Every 200 ms keystroke pause blanked the grid and
    /// refetched it — and one class-noise tick cost TWO walks, because ApplyInstanceFilter
    /// runs immediately AND again when the server re-run lands.
    /// </summary>
    [Fact]
    public async Task Filtering_keeps_a_surviving_selections_field_grid_without_a_second_walk()
    {
        var dump = new FakeDump { NextSearch = Result(("Hero", "BP_Hero_C"), ("Rock", "BP_Rock_C")) };
        var vm = NewVm(dump);
        vm.SearchClassName = "BP";
        await vm.SearchCommand.ExecuteAsync(null);

        vm.SelectedInstance = vm.Instances[0];
        await WaitFor(() => vm.HasFields);
        int walksAfterSelect = dump.WalkInstanceCalls;
        Assert.Equal(1, walksAfterSelect);

        // Type a keyword the selection still matches. The old code cleared Fields on the
        // detach and re-walked on the restore.
        vm.InstanceFilterText = "Hero";
        vm.ApplyInstanceFilter();   // deterministic (bypass the 200 ms debounce)

        Assert.Same(vm.Instances[0], vm.SelectedInstance);
        Assert.True(vm.HasFields, "the loaded field grid must survive a filter the selection passes");
        Assert.NotEmpty(vm.Fields);
        Assert.Equal(walksAfterSelect, dump.WalkInstanceCalls);
    }

    /// <summary>
    /// Negative control, and the half that must NOT change: when the selection is
    /// filtered OUT it is genuinely gone, so the grid clears as before. Without this the
    /// fix above could be "never clear anything", which would leave a stale field grid
    /// attached to a row no longer on screen.
    /// </summary>
    [Fact]
    public async Task Filtering_OUT_the_selection_still_clears_the_field_grid()
    {
        var dump = new FakeDump { NextSearch = Result(("Hero", "BP_Hero_C"), ("Rock", "BP_Rock_C")) };
        var vm = NewVm(dump);
        vm.SearchClassName = "BP";
        await vm.SearchCommand.ExecuteAsync(null);

        vm.SelectedInstance = vm.Instances[0];
        await WaitFor(() => vm.HasFields);

        vm.InstanceFilterText = "Rock";
        vm.ApplyInstanceFilter();

        Assert.Null(vm.SelectedInstance);
        Assert.False(vm.HasFields);
        Assert.Empty(vm.Fields);
    }

    /// <summary>Two filter passes over a surviving selection cost zero extra walks —
    /// the "one class-noise tick costs TWO walks" half of Z7.</summary>
    [Fact]
    public async Task Repeated_filter_passes_over_a_surviving_selection_cost_no_extra_walks()
    {
        var dump = new FakeDump { NextSearch = Result(("Hero", "BP_Hero_C"), ("Rock", "BP_Rock_C")) };
        var vm = NewVm(dump);
        vm.SearchClassName = "BP";
        await vm.SearchCommand.ExecuteAsync(null);

        vm.SelectedInstance = vm.Instances[0];
        await WaitFor(() => vm.HasFields);

        vm.InstanceFilterText = "Hero";
        vm.ApplyInstanceFilter();   // deterministic (bypass the 200 ms debounce)
        vm.InstanceFilterText = "Her";
        vm.ApplyInstanceFilter();
        vm.InstanceFilterText = "";
        vm.ApplyInstanceFilter();

        Assert.Equal(1, dump.WalkInstanceCalls);
        Assert.True(vm.HasFields);
    }

    // ==================================================================
    // Z12 — the scan suffix reflects the pass that actually ran.
    // ==================================================================

    [Fact]
    public async Task Lookup_miss_after_a_DEEP_scan_discloses_the_element_cap()
    {
        var dump = new FakeDump
        {
            NextLookup = new AddressLookupResult
            {
                Found = false,
                ContainerScan = new ContainerScanStats
                {
                    ObjectsScanned = 430_112, ObjectsTotal = 430_112,
                    DurationMs = 4210, DeadlineHit = false, DeepScan = true,
                },
            },
        };
        var vm = NewVm(dump);
        vm.DeepScanElemCap = 256;
        vm.LookupAddress = "0x7FF700001000";

        await vm.LookupAddressCommand.ExecuteAsync(null);

        Assert.Contains("No UObject found", vm.LookupStatusText);
        Assert.Contains("deep descent", vm.LookupStatusText);
        Assert.Contains("256 element(s) per container", vm.LookupStatusText);
        Assert.Contains("not proof of absence", vm.LookupStatusText);
    }

    [Fact]
    public async Task Lookup_miss_after_a_complete_SHALLOW_scan_makes_no_excuses()
    {
        var dump = new FakeDump
        {
            NextLookup = new AddressLookupResult
            {
                Found = false,
                ContainerScan = new ContainerScanStats
                {
                    ObjectsScanned = 430_112, ObjectsTotal = 430_112,
                    DurationMs = 812, DeadlineHit = false, DeepScan = false,
                },
            },
        };
        var vm = NewVm(dump);
        vm.LookupAddress = "0x7FF700001000";

        await vm.LookupAddressCommand.ExecuteAsync(null);

        Assert.Contains("[scanned 430,112/430,112 in 812ms]", vm.LookupStatusText);
        Assert.DoesNotContain("⚠", vm.LookupStatusText);
    }

    [Fact]
    public async Task Lookup_after_a_deadline_says_the_scan_is_partial()
    {
        var dump = new FakeDump
        {
            NextLookup = new AddressLookupResult
            {
                Found = false,
                ContainerScan = new ContainerScanStats
                {
                    ObjectsScanned = 120_000, ObjectsTotal = 430_112,
                    DurationMs = 15_004, DeadlineHit = true, DeepScan = false,
                },
            },
        };
        var vm = NewVm(dump);
        vm.LookupAddress = "0x7FF700001000";

        await vm.LookupAddressCommand.ExecuteAsync(null);

        Assert.Contains("DEADLINE HIT", vm.LookupStatusText);
        Assert.Contains("retry", vm.LookupStatusText);
    }

    private static async Task WaitFor(Func<bool> cond)
    {
        for (int i = 0; i < 200 && !cond(); i++) await Task.Delay(5);
        Assert.True(cond(), "condition never became true");
    }
}
