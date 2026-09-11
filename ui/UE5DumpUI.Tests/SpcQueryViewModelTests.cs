using System.IO;
using System.Linq;
using UE5DumpUI.Core;
using UE5DumpUI.Models;
using UE5DumpUI.Services;
using UE5DumpUI.ViewModels;
using Xunit;

namespace UE5DumpUI.Tests;

public class SpcQueryViewModelTests : IDisposable
{
    private readonly string _tempDir;
    private readonly SnapshotStore _store;

    public SpcQueryViewModelTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"UE5DumpSpcVm_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _store = new SnapshotStore(new MockPlatformService(_tempDir));
        _store.SetActiveGame("G");
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); }
        catch { /* best effort */ }
    }

    // Records the last clipboard write so CopyAddress can be asserted.
    private sealed class RecordingPlatform : IPlatformService
    {
        public string? LastClipboard;
        public bool TryAcquireSingleInstance() => true;
        public void ReleaseSingleInstance() { }
        public string GetAppDataPath() => Path.GetTempPath();
        public string GetLogDirectoryPath() => Path.GetTempPath();
        public Task<bool> CopyToClipboardAsync(string text) { LastClipboard = text; return Task.FromResult(true); }
        public Task RevealInExplorerAsync(string path) => Task.CompletedTask;
        public string GetMachineName() => "TEST";
        public void CloseImeForWindow(IntPtr windowHandle) { }
        public Task<string?> ShowSaveFileDialogAsync(string a, string b, string c) => Task.FromResult<string?>(null);
    }

    private static string IntHex(int v) =>
        string.Concat(BitConverter.GetBytes(v).Select(b => b.ToString("X2")));

    private static SnapshotCapturedObject Obj(int idx, string addr,
        params (string name, int val)[] fields)
    {
        var o = new SnapshotCapturedObject
        {
            Index = idx, Addr = addr, Name = $"O_{idx}", ClassName = "PlayerState",
            OuterClassName = "World", Path = "/Game/M.M:PersistentLevel.PlayerState_0",
        };
        foreach (var (name, val) in fields)
            o.Fields.Add(new SnapshotCapturedField { Name = name, Type = "IntProperty", Hex = IntHex(val), Offset = 0x40 });
        return o;
    }

    private async Task<long> SeedAsync(string label, params (string name, int val)[] fields)
    {
        var ct = TestContext.Current.CancellationToken;
        long id = await _store.CreateSnapshotAsync(new SnapshotMeta { Label = label, GameSessionId = "S" }, ct);
        await _store.WriteChunkAsync(id, new[] { Obj(1, "0x7FF600001000", fields) }, ct);
        await _store.FinalizeSnapshotAsync(id, 1, fields.Length, ct);
        return id;
    }

    // ---- [A4-PIVOT-CROSSGAME-ID] SPC's ticks do not follow an id into a different game ----

    private async Task SeedGameAsync(string pe, int count)
    {
        _store.SetActiveGame(pe);
        for (int i = 0; i < count; i++) await SeedAsync($"{pe}{i}", ("HP", 100 - i));
    }

    private static EngineState GameState(string pe) =>
        new() { PeHash = pe, UEVersion = 504, ModuleBase = "7FF600000000", ProcessCreationTime = "T" };

    [Fact]
    public async Task ADifferentGame_GetsTheFirstVisitDefault_NotTheOtherGamesTicks()
    {
        await SeedGameAsync("A", 2);
        await SeedGameAsync("B", 3);
        var vm = NewVm();
        vm.SetEngineState(GameState("A"));
        await vm.PendingRefresh!;
        foreach (var p in vm.SnapshotPicks) p.IsSelected = p.Id == 1;   // the user ticks A#1 only

        vm.SetEngineState(GameState("B"));
        await vm.PendingRefresh!;

        Assert.False(vm.SnapshotPicks.First(p => p.Id == 1).IsSelected);   // A#1's tick did not land on B#1
        Assert.True(vm.SnapshotPicks.First(p => p.Id == 3).IsSelected);    // B's first-visit default: the two newest
    }

    private SpcQueryViewModel NewVm(IPlatformService? platform = null)
        => new SpcQueryViewModel(_store, new MockLoggingService(), platform);

    // ---- [W1-SPC-JOINMODE] the join mode is persisted and restored through session-safe members ----
    //
    // Opening the tab auto-ticks the two newest picks and auto-selects the join mode, so an AUTO-chosen
    // In-session reached ui-options.json with no user action. ApplyOptions then wrote it back through the
    // public setter, which latched _joinModeUserOverride for good: AutoSelectJoinMode early-returned
    // forever, and the documented cross-session fallback to Strict was dead. In-session joins on GObjects
    // slot numbers, which mean nothing in another launch. The wiring lives in MainWindowViewModel, which
    // no test constructs, so it is pinned from the source.
    [Fact]
    public void JoinMode_OptionsWiring_GoesThroughTheSessionSafeMembers()
    {
        var src = File.ReadAllText(NumericInputCoercionTests.RepoFile("ui/UE5DumpUI/ViewModels/MainWindowViewModel.cs"));
        Assert.Contains("Spc.RestoreJoinModeFromOptions(o.Spc.SelectedJoinMode)", src);
        Assert.Contains("o.Spc.SelectedJoinMode = Spc.JoinModeForOptions", src);
        Assert.DoesNotContain("Spc.SelectedJoinMode = o.Spc.SelectedJoinMode", src);
        Assert.DoesNotContain("o.Spc.SelectedJoinMode = Spc.SelectedJoinMode", src);
    }

    private async Task<long> SeedInSessionAsync(string label, string session, int hp)
    {
        var ct = TestContext.Current.CancellationToken;
        long id = await _store.CreateSnapshotAsync(new SnapshotMeta { Label = label, GameSessionId = session }, ct);
        await _store.WriteChunkAsync(id, new[] { Obj(1, "0x7FF600001000", ("HP", hp)) }, ct);
        await _store.FinalizeSnapshotAsync(id, 1, 1, ct);
        return id;
    }

    [Fact]
    public async Task AutoChosenInSession_IsPersistedAsStrict()
    {
        await SeedInSessionAsync("a", "S", 100);
        await SeedInSessionAsync("b", "S", 90);
        var vm = NewVm();
        await vm.RefreshAsync();                        // auto-ticks both: one session

        Assert.Equal("In-session", vm.SelectedJoinMode);
        Assert.Equal("Strict", vm.JoinModeForOptions);  // launch-scoped: never written
    }

    [Fact]
    public async Task RestoredInSession_DoesNotLatchAFakeOverride()
    {
        // The recorded repro: an options file holding In-session, then picks from two launches.
        await SeedInSessionAsync("old", "S-OLD", 100);
        await SeedInSessionAsync("new", "S-NEW", 90);
        var vm = NewVm();
        vm.RestoreJoinModeFromOptions("In-session");
        await vm.RefreshAsync();                        // auto-ticks both: they span launches

        Assert.Equal("Strict", vm.SelectedJoinMode);    // the cross-session fallback still works
    }

    [Fact]
    public async Task RestoredLoose_StaysAUserChoice()
    {
        // Auto-selection only ever picks In-session or Strict, so a persisted Loose came from the user.
        await SeedInSessionAsync("a", "S", 100);
        await SeedInSessionAsync("b", "S", 90);
        var vm = NewVm();
        vm.RestoreJoinModeFromOptions("Loose");
        await vm.RefreshAsync();                        // one session would auto-pick In-session

        Assert.Equal("Loose", vm.SelectedJoinMode);
        Assert.Equal("Loose", vm.JoinModeForOptions);
    }

    [Fact]
    public async Task RestoredStrict_IsNotAUserChoice()
    {
        // Review of de4a7e05: Strict is also what an AUTO In-session is written as, so restoring it through
        // the setter latched the very fake override this fix removes -- latent only because the one call
        // site runs while the combo already holds Strict. Pinned in the order that shows it.
        await SeedInSessionAsync("a", "S", 100);
        await SeedInSessionAsync("b", "S", 90);
        var vm = NewVm();
        await vm.RefreshAsync();                        // one session: auto-picks In-session
        Assert.Equal("In-session", vm.SelectedJoinMode);

        vm.RestoreJoinModeFromOptions("Strict");

        Assert.Equal("In-session", vm.SelectedJoinMode);
    }

    [Fact]
    public void RestoredUnknownJoinMode_IsIgnored()
    {
        var vm = NewVm();
        vm.RestoreJoinModeFromOptions("Bogus");
        Assert.Equal("Strict", vm.SelectedJoinMode);
    }

    [Fact]
    public async Task Refresh_PopulatesPicks_AutoSelectsTwoNewest()
    {
        await SeedAsync("a", ("HP", 100));
        await SeedAsync("b", ("HP", 90));
        await SeedAsync("c", ("HP", 80));

        var vm = NewVm();
        await vm.RefreshAsync();

        Assert.Equal(3, vm.SnapshotPicks.Count);
        // Oldest-first; the two newest (the tail) auto-selected for convenience.
        Assert.False(vm.SnapshotPicks[0].IsSelected);
        Assert.True(vm.SnapshotPicks[1].IsSelected);
        Assert.True(vm.SnapshotPicks[2].IsSelected);
        Assert.Equal(2, vm.SelectedCount);
        Assert.True(vm.CanRunQuery);
        // The oldest CHECKED snapshot is the baseline: predicate fixed to Any + disabled.
        Assert.True(vm.SnapshotPicks[1].IsBaseline);
        Assert.Equal("Any", vm.SnapshotPicks[1].SelectedPredicate);
        Assert.False(vm.SnapshotPicks[1].PredicateEnabled);
        Assert.False(vm.SnapshotPicks[2].IsBaseline);
        Assert.True(vm.SnapshotPicks[2].PredicateEnabled);
    }

    [Fact]
    public void CanRunQuery_RequiresTwoSelected()
    {
        var vm = NewVm();
        Assert.False(vm.CanRunQuery);   // nothing loaded
    }

    [Fact]
    public async Task RunQuery_DirectionalChain_IsolatesField()
    {
        long s1 = await SeedAsync("t1", ("Stamina", 100), ("Counter", 1));
        long s2 = await SeedAsync("t2", ("Stamina", 30),  ("Counter", 2));
        long s3 = await SeedAsync("t3", ("Stamina", 80),  ("Counter", 3));

        var vm = NewVm();
        await vm.RefreshAsync();

        // Select all three and assign the [Any, Decreased, Increased] chain.
        foreach (var p in vm.SnapshotPicks) p.IsSelected = true;
        vm.SnapshotPicks.First(p => p.Id == s2).SelectedPredicate = "Decreased";
        vm.SnapshotPicks.First(p => p.Id == s3).SelectedPredicate = "Increased";
        // s1 is the baseline — its predicate is ignored regardless.
        vm.SnapshotPicks.First(p => p.Id == s1).SelectedPredicate = "Increased";

        Assert.Equal(3, vm.SelectedCount);
        await vm.RunQueryCommand.ExecuteAsync(null);

        var row = Assert.Single(vm.Results);
        Assert.Equal("Stamina", row.PropName);
        Assert.Equal("100  →  30  →  80", row.SequenceDisplay);
    }

    [Fact]
    public async Task ResultFilter_Pickers_Global_And_SequenceRange()
    {
        await SeedAsync("t1", ("HP", 100), ("Mana", 10));
        await SeedAsync("t2", ("HP", 90),  ("Mana", 20));
        await SeedAsync("t3", ("HP", 80),  ("Mana", 30));

        var vm = NewVm();
        await vm.RefreshAsync();
        foreach (var p in vm.SnapshotPicks) p.IsSelected = true;   // chain [Any, Any, Any]

        await vm.RunQueryCommand.ExecuteAsync(null);
        Assert.Equal(2, vm.Results.Count);   // HP + Mana both present in all 3

        // Picker candidates from the result set.
        Assert.Contains("HP", vm.ResultFieldOptions);
        Assert.Contains("Mana", vm.ResultFieldOptions);

        // Field filter narrows live.
        vm.ResultFieldFilter = "HP";
        Assert.Equal("HP", Assert.Single(vm.Results).PropName);
        vm.ResultFieldFilter = "";
        Assert.Equal(2, vm.Results.Count);

        // Sequence range (button-applied): First>=50 keeps HP(100), drops Mana(10).
        vm.SeqFirstMin = "50";
        Assert.Equal(2, vm.Results.Count);   // not applied until the button
        vm.ApplyResultRangeCommand.Execute(null);
        Assert.Equal("HP", Assert.Single(vm.Results).PropName);

        vm.ResetResultRangeCommand.Execute(null);
        Assert.Equal(2, vm.Results.Count);
        Assert.Equal("", vm.SeqFirstMin);
    }

    [Fact]
    public async Task RunQuery_FewerThanTwoSelected_NoQuery()
    {
        await SeedAsync("a", ("HP", 100));
        await SeedAsync("b", ("HP", 90));
        var vm = NewVm();
        await vm.RefreshAsync();
        foreach (var p in vm.SnapshotPicks) p.IsSelected = false;

        await vm.RunQueryCommand.ExecuteAsync(null);
        Assert.Empty(vm.Results);
        Assert.Contains("at least two", vm.StatusText);
    }

    [Fact]
    public void OpenInLiveWalker_RaisesNavigateWithAddress()
    {
        var vm = NewVm();
        string? nav = null;
        vm.NavigateToInstance += a => nav = a;

        vm.OpenInLiveWalkerCommand.Execute(new SpcResultRow { ObjAddr = "0x7FF600001234" });
        Assert.Equal("0x7FF600001234", nav);

        nav = null;
        vm.OpenInLiveWalkerCommand.Execute(new SpcResultRow { ObjAddr = "" });
        Assert.Null(nav);
    }

    [Fact]
    public async Task CopyAddress_WritesObjAddrPlusOffset()
    {
        var platform = new RecordingPlatform();
        var vm = NewVm(platform);
        var row = new SpcResultRow { ObjAddr = "0x7FF600001000", PropOffset = 0x40,
            ClassName = "PlayerState", PropName = "HP" };

        await vm.CopyAddressCommand.ExecuteAsync(row);
        Assert.Equal("7FF600001040", platform.LastClipboard);
    }
}
