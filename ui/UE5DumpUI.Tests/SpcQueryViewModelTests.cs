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

    private SpcQueryViewModel NewVm(IPlatformService? platform = null)
        => new SpcQueryViewModel(_store, new MockLoggingService(), platform);

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
