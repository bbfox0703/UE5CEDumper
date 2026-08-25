using System;
using System.IO;
using System.Linq;
using UE5DumpUI.Core;
using UE5DumpUI.Models;
using UE5DumpUI.Services;
using UE5DumpUI.ViewModels;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// SPC Query GROUP mode (Single/Group toggle) — drives the full VM → store path:
/// the per-slot chain is assembled from each pick's active-slot cells, the baseline
/// is forced to Any, and the store returns object-level GroupCandidates. See
/// docs/group-value-scan-spec.md §3.1.
/// </summary>
public class SpcGroupViewModelTests : IDisposable
{
    private readonly string _tempDir;
    private readonly SnapshotStore _store;

    public SpcGroupViewModelTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"UE5DumpSpcGroupVm_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _store = new SnapshotStore(new MockPlatformService(_tempDir));
        _store.SetActiveGame("G");
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); }
        catch { /* best effort */ }
    }

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

    private static SnapshotCapturedObject Obj(int idx, string cls,
        params (string name, int off, int val)[] fields)
    {
        var o = new SnapshotCapturedObject
        {
            Index = idx, Addr = $"0x{idx * 0x1000:X}", Name = $"{cls}_{idx}",
            ClassName = cls, OuterClassName = "World",
            Path = $"/Game/M.M:PersistentLevel.{cls}_{idx}",
        };
        foreach (var (name, off, val) in fields)
            o.Fields.Add(new SnapshotCapturedField { Name = name, Type = "IntProperty", Offset = off, Hex = IntHex(val) });
        return o;
    }

    private async Task<long> SeedAsync(string label, params SnapshotCapturedObject[] objs)
    {
        var ct = TestContext.Current.CancellationToken;
        long id = await _store.CreateSnapshotAsync(new SnapshotMeta { Label = label, GameSessionId = "S" }, ct);
        int rows = await _store.WriteChunkAsync(id, objs, ct);
        await _store.FinalizeSnapshotAsync(id, objs.Length, rows, ct);
        return id;
    }

    private SpcQueryViewModel NewVm(IPlatformService? platform = null)
        => new SpcQueryViewModel(_store, new MockLoggingService(), platform);

    // Set the active-slot predicate on a given pick (mirrors the UI: choose the slot,
    // then edit the picker grid's Compare cell for that snapshot row).
    private static void SetSlotPredicate(SpcQueryViewModel vm, SpcSnapshotPick pick, int slot, string predicate)
    {
        vm.ActiveGroupSlot = slot;
        pick.GroupActivePredicate = predicate;
    }

    [Fact]
    public async Task GroupQuery_PerSlotChain_FindsTheDamagedActor()
    {
        long s1 = await SeedAsync("old",
            Obj(1, "BP_Player_C", ("CurHP", 0x10, 100), ("MaxHP", 0x14, 100)),
            Obj(2, "BP_Player_C", ("CurHP", 0x10, 100), ("MaxHP", 0x14, 100)));
        long s2 = await SeedAsync("new",
            Obj(1, "BP_Player_C", ("CurHP", 0x10, 80),  ("MaxHP", 0x14, 100)),   // took damage
            Obj(2, "BP_Player_C", ("CurHP", 0x10, 90),  ("MaxHP", 0x14, 130)));  // level-up (both moved)

        var vm = NewVm();
        await vm.RefreshAsync();
        vm.IsGroupMode = true;
        foreach (var p in vm.SnapshotPicks) p.IsSelected = true;   // both snapshots

        var newPick = vm.SnapshotPicks.First(p => p.Id == s2);
        SetSlotPredicate(vm, newPick, 0, "Decreased");   // CurHP ↓
        SetSlotPredicate(vm, newPick, 1, "Unchanged");   // MaxHP =

        Assert.True(vm.CanRunGroupQuery);
        await vm.RunGroupQueryCommand.ExecuteAsync(null);

        var c = Assert.Single(vm.GroupCandidates);   // only actor 1 keeps MaxHP unchanged
        Assert.Equal(1, c.InstanceIndex);
        Assert.Equal(2, c.Slots.Count);
        Assert.Contains(c.Slots, s => s.FieldName == "CurHP");
        Assert.Contains(c.Slots, s => s.FieldName == "MaxHP");
    }

    [Fact]
    public async Task ActiveSlotFacade_EditsOnlyTheChosenSlotsCell()
    {
        await SeedAsync("old", Obj(1, "C", ("A", 0x10, 1), ("B", 0x14, 2)));
        await SeedAsync("new", Obj(1, "C", ("A", 0x10, 1), ("B", 0x14, 2)));
        var vm = NewVm();
        await vm.RefreshAsync();
        vm.IsGroupMode = true;
        var pick = vm.SnapshotPicks.OrderBy(p => p.Id).Last();

        vm.ActiveGroupSlot = 0;
        pick.GroupActivePredicate = "Increased";
        vm.ActiveGroupSlot = 1;
        pick.GroupActivePredicate = "Decreased";

        // Switching back shows slot 0's value, not slot 1's — the cells are independent.
        vm.ActiveGroupSlot = 0;
        Assert.Equal("Increased", pick.GroupActivePredicate);
        Assert.Equal("Increased", pick.GroupPredicateOf(0));
        Assert.Equal("Decreased", pick.GroupPredicateOf(1));
    }

    [Fact]
    public void AddRemoveSlot_GatesAndChoicesTrack()
    {
        var vm = NewVm();
        Assert.Equal(2, vm.GroupSlots.Count);
        Assert.True(vm.CanAddGroupSlot);
        Assert.False(vm.CanRemoveGroupSlot);

        vm.AddGroupSlotCommand.Execute(null);
        vm.AddGroupSlotCommand.Execute(null);
        Assert.Equal(4, vm.GroupSlots.Count);
        Assert.Equal(4, vm.ActiveSlotChoices.Count);
        Assert.False(vm.CanAddGroupSlot);   // capped at 4

        vm.AddGroupSlotCommand.Execute(null);   // no-op past the cap
        Assert.Equal(4, vm.GroupSlots.Count);

        vm.ActiveGroupSlot = 3;
        vm.RemoveGroupSlotCommand.Execute(null);
        Assert.Equal(3, vm.GroupSlots.Count);
        Assert.Equal(2, vm.ActiveGroupSlot);    // clamped when the active slot was removed
    }

    [Fact]
    public async Task GroupMatrix_PreservedAcrossRefresh()
    {
        long s2 = (await SeedTwo());
        var vm = NewVm();
        await vm.RefreshAsync();
        vm.IsGroupMode = true;
        var pick = vm.SnapshotPicks.First(p => p.Id == s2);
        SetSlotPredicate(vm, pick, 0, "Decreased");

        // A capture on the Snapshot tab triggers a picker rebuild — the matrix survives.
        await vm.RefreshAsync();
        var again = vm.SnapshotPicks.First(p => p.Id == s2);
        Assert.Equal("Decreased", again.GroupPredicateOf(0));
    }

    private async Task<long> SeedTwo()
    {
        await SeedAsync("old", Obj(1, "C", ("A", 0x10, 1), ("B", 0x14, 2)));
        return await SeedAsync("new", Obj(1, "C", ("A", 0x10, 1), ("B", 0x14, 2)));
    }

    [Fact]
    public async Task CopyGroupSlotAddress_WritesLeafAddr()
    {
        var platform = new RecordingPlatform();
        var vm = NewVm(platform);
        await vm.CopyGroupSlotAddressCommand.ExecuteAsync(
            new GroupSlotMatch { Addr = "0x7FF600001040", ClassName = "C", FieldName = "HP" });
        Assert.Equal("7FF600001040", platform.LastClipboard);
    }

    [Fact]
    public void OpenGroupInLiveWalker_RaisesNavigate()
    {
        var vm = NewVm();
        string? nav = null;
        vm.NavigateToInstance += a => nav = a;
        vm.OpenGroupInLiveWalkerCommand.Execute(new GroupCandidate { InstanceAddr = "0x7FF600001234" });
        Assert.Equal("0x7FF600001234", nav);
    }
}
