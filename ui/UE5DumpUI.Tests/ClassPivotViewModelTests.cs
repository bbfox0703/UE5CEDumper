using System.IO;
using System.Linq;
using UE5DumpUI.Core;
using UE5DumpUI.Models;
using UE5DumpUI.Services;
using UE5DumpUI.ViewModels;
using Xunit;

namespace UE5DumpUI.Tests;

public class ClassPivotViewModelTests : IDisposable
{
    private readonly string _tempDir;
    private readonly SnapshotStore _store;

    public ClassPivotViewModelTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"UE5DumpPivotVm_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _store = new SnapshotStore(new MockPlatformService(_tempDir));
        _store.SetActiveGame("G");
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); }
        catch { /* best effort */ }
    }

    private static string IntHex(int v) =>
        string.Concat(BitConverter.GetBytes(v).Select(b => b.ToString("X2")));

    private static SnapshotCapturedObject Obj(int idx, string cls, string path,
        params (string name, int val)[] fields)
    {
        var o = new SnapshotCapturedObject
        {
            Index = idx, Addr = $"0x{0x1000 + idx:X}", Name = $"O_{idx}",
            ClassName = cls, OuterClassName = "World", Path = path,
        };
        foreach (var (name, val) in fields)
            o.Fields.Add(new SnapshotCapturedField { Name = name, Type = "IntProperty", Hex = IntHex(val), Offset = 0x10 });
        return o;
    }

    private async Task SeedInventoryAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        long id = await _store.CreateSnapshotAsync(new SnapshotMeta { Label = "inv" }, ct);
        await _store.WriteChunkAsync(id, new[]
        {
            Obj(1, "BP_Item_C", "/G.M:L.Item_0", ("ItemID", 1), ("Quantity", 10)),
            Obj(2, "BP_Item_C", "/G.M:L.Item_1", ("ItemID", 1), ("Quantity", 20)),
            Obj(3, "BP_Item_C", "/G.M:L.Item_2", ("ItemID", 2), ("Quantity", 5)),
        }, ct);
        await _store.FinalizeSnapshotAsync(id, 3, 6, ct);
    }

    private ClassPivotViewModel NewVm()
        => new ClassPivotViewModel(_store, new MockLoggingService());

    /// <summary>Thin decorator that counts the heavy list calls, delegating the
    /// rest to a real store — proves the per-snapshot cache avoids re-scanning.</summary>
    private sealed class CountingStore : ISnapshotStore
    {
        private readonly ISnapshotStore _inner;
        public int ClassCalls;
        public int FieldCalls;
        public CountingStore(ISnapshotStore inner) => _inner = inner;

        public Task<IReadOnlyList<PivotClassInfo>> ListPivotClassesAsync(long s, CancellationToken ct = default)
        { ClassCalls++; return _inner.ListPivotClassesAsync(s, ct); }
        public Task<IReadOnlyList<PivotFieldInfo>> ListPivotFieldsAsync(long s, string c, CancellationToken ct = default)
        { FieldCalls++; return _inner.ListPivotFieldsAsync(s, c, ct); }

        public string DatabasePath => _inner.DatabasePath;
        public void SetActiveGame(string? p) => _inner.SetActiveGame(p);
        public Task<long> CreateSnapshotAsync(SnapshotMeta m, CancellationToken ct = default) => _inner.CreateSnapshotAsync(m, ct);
        public Task<int> WriteChunkAsync(long s, IReadOnlyList<SnapshotCapturedObject> o, CancellationToken ct = default) => _inner.WriteChunkAsync(s, o, ct);
        public Task<ICaptureSession> BeginCaptureSessionAsync(CancellationToken ct = default) => _inner.BeginCaptureSessionAsync(ct);
        public Task FinalizeSnapshotAsync(long s, int oc, int fc, CancellationToken ct = default) => _inner.FinalizeSnapshotAsync(s, oc, fc, ct);
        public Task<IReadOnlyList<SnapshotMeta>> ListSnapshotsAsync(CancellationToken ct = default) => _inner.ListSnapshotsAsync(ct);
        public Task DeleteSnapshotAsync(long s, bool reclaim = false, CancellationToken ct = default) => _inner.DeleteSnapshotAsync(s, reclaim, ct);
        public Task DeleteAllSnapshotsAsync(CancellationToken ct = default) => _inner.DeleteAllSnapshotsAsync(ct);
        public Task<SnapshotUsage> GetUsageAsync(CancellationToken ct = default) => _inner.GetUsageAsync(ct);
        public Task<SnapshotDiffResult> DiffSnapshotsAsync(long a, long b, SnapshotDiffFilter f, CancellationToken ct = default) => _inner.DiffSnapshotsAsync(a, b, f, ct);
        public Task<SnapshotGroupResult> GroupMatchAsync(SnapshotGroupQuery q, CancellationToken ct = default) => _inner.GroupMatchAsync(q, ct);
        public Task<SpcResult> SpcQueryAsync(SpcQuery q, CancellationToken ct = default) => _inner.SpcQueryAsync(q, ct);
        public Task<SpcGroupResult> SpcGroupQueryAsync(SpcGroupQuery q, CancellationToken ct = default) => _inner.SpcGroupQueryAsync(q, ct);
        public Task<DiscoveryResult> DiscoverChangesAsync(DiscoveryQuery q, CancellationToken ct = default) => _inner.DiscoverChangesAsync(q, ct);
        public Task<PivotResult> PivotAsync(PivotQuery q, CancellationToken ct = default) => _inner.PivotAsync(q, ct);
        public Task<IReadOnlyList<PivotClassInfo>> ListPivotArrayClassesAsync(long s, CancellationToken ct = default) => _inner.ListPivotArrayClassesAsync(s, ct);
        public Task<IReadOnlyList<PivotArrayFieldInfo>> ListPivotArrayFieldsAsync(long s, string c, CancellationToken ct = default) => _inner.ListPivotArrayFieldsAsync(s, c, ct);
        public Task<IReadOnlyList<PivotFieldInfo>> ListPivotArrayPropsAsync(long s, string c, string af, CancellationToken ct = default) => _inner.ListPivotArrayPropsAsync(s, c, af, ct);
        public Task<PivotResult> PivotArrayAsync(ArrayPivotQuery q, CancellationToken ct = default) => _inner.PivotArrayAsync(q, ct);
        public Task<int> EnforceQuotaAsync(long b, CancellationToken ct = default) => _inner.EnforceQuotaAsync(b, ct);
        public HashSet<string> GetClassDenylist(DenylistScope scope) => _inner.GetClassDenylist(scope);
        public void SetClassDenylist(DenylistScope scope, HashSet<string> classes) => _inner.SetClassDenylist(scope, classes);
    }

    [Fact]
    public async Task ClassList_IsCachedPerSnapshot_NotRescannedOnReselect()
    {
        await SeedInventoryAsync();
        var counting = new CountingStore(_store);
        var vm = new ClassPivotViewModel(counting, new MockLoggingService());
        await vm.RefreshAsync();
        await vm.PendingLoad!;            // first class load for the newest snapshot
        Assert.Equal(1, counting.ClassCalls);

        var snap = vm.SelectedSnapshot!;
        // Re-select the SAME snapshot (force the change handler) → cache hit, no scan.
        vm.SelectedSnapshot = null;
        vm.SelectedSnapshot = snap;
        if (vm.PendingLoad != null) await vm.PendingLoad;
        Assert.Equal(1, counting.ClassCalls);   // still 1 — served from cache
        Assert.Contains(vm.Classes, c => c.ClassName == "BP_Item_C");
    }

    // The class picker is a filter TextBox + ListBox (the AutoCompleteBox oscillated/froze; the
    // ComboBox dropped clicks). Typing the filter narrows the list by substring and must NOT hit
    // the DB. A filter that KEEPS the picked class preserves the selection (so a spurious
    // re-filter post-click can't wipe it); a filter that EXCLUDES it clears the selection.
    [Fact]
    public async Task ClassFilter_NarrowsClasses_NoDbHit_PreservesOrClearsSelection()
    {
        var ct = TestContext.Current.CancellationToken;
        long id = await _store.CreateSnapshotAsync(new SnapshotMeta { Label = "two" }, ct);
        await _store.WriteChunkAsync(id, new[]
        {
            Obj(1, "BP_Item_C",  "/G.M:L.Item_0",  ("ItemID", 1)),
            Obj(2, "BP_Other_C", "/G.M:L.Other_0", ("Slot", 3)),
        }, ct);
        await _store.FinalizeSnapshotAsync(id, 2, 2, ct);

        var counting = new CountingStore(_store);
        var vm = new ClassPivotViewModel(counting, new MockLoggingService());
        await vm.RefreshAsync();
        await vm.PendingLoad!;   // classes loaded (BP_Item_C, BP_Other_C)
        Assert.Equal(2, vm.Classes.Count);

        // Pick a class → one field load.
        vm.SelectedClass = vm.Classes.First(c => c.ClassName == "BP_Item_C");
        await vm.PendingLoad!;
        int calls = counting.FieldCalls;
        Assert.Equal(1, calls);

        // Filter that KEEPS the picked class → narrows the list, selection PRESERVED, no new DB hit.
        vm.ClassFilter = "item";
        Assert.Single(vm.Classes);
        Assert.Equal("BP_Item_C", vm.Classes[0].ClassName);
        Assert.Equal("BP_Item_C", vm.SelectedClass?.ClassName);   // survived the filter → kept (the anti-flicker fix)
        Assert.Equal(calls, counting.FieldCalls);                 // re-select was a cache hit — zero DB

        // Filter that EXCLUDES the picked class → selection cleared, still no DB hit.
        vm.ClassFilter = "other";
        Assert.Single(vm.Classes);
        Assert.Equal("BP_Other_C", vm.Classes[0].ClassName);
        Assert.Null(vm.SelectedClass);
        Assert.Equal(calls, counting.FieldCalls);

        // Clearing the filter restores the full list.
        vm.ClassFilter = "";
        Assert.Equal(2, vm.Classes.Count);
    }

    // Regression for the ~135 OpenAsync/sec DB storm: a SUPERSEDED-but-COMPLETED field load
    // must still populate the (immutable-keyed) cache, so re-selecting that class is a cache
    // hit instead of re-hitting the store. Pre-fix the supersession guard ran BEFORE the cache
    // write, so the cache stayed empty and every re-fire re-opened the DB. Uses the GatedStore
    // (its ListPivotFields ignores ct, so a held load completes via SetResult even after it is
    // superseded — exactly the path the fix caches). The cache-hit apply is synchronous, so a
    // miss leaves Fields stale at the assert — deterministic, no delays.
    [Fact]
    public async Task FieldList_SupersededLoad_StillCaches_ReselectServesFromCache()
    {
        var store = new GatedStore();
        var vm = new ClassPivotViewModel(store, new MockLoggingService());
        await vm.RefreshAsync();
        await vm.PendingLoad!;   // classes A, B

        var clsA = vm.Classes.First(c => c.ClassName == "A");
        var clsB = vm.Classes.First(c => c.ClassName == "B");

        // Select A (gated, in flight), then B — A becomes the superseded load.
        vm.SelectedClass = clsA;
        var loadA = vm.PendingLoad!;
        await WaitForGate(store, "A");
        vm.SelectedClass = clsB;
        var loadB = vm.PendingLoad!;
        await WaitForGate(store, "B");

        // Complete B (applied), then the SUPERSEDED A. With the cache-before-guard fix A's
        // completed result is cached even though its UI apply is skipped.
        store.Gates["B"].SetResult(new[] { Field("BetaField") });
        await loadB;
        store.Gates["A"].SetResult(new[] { Field("AlphaField") });
        await loadA;
        Assert.Equal("BetaField", Assert.Single(vm.Fields).Name);   // A's stale apply was skipped

        // Re-select A: a synchronous CACHE HIT applies A's fields immediately. Pre-fix this was
        // a cache MISS → async (re)load → Fields would still read "BetaField" at this point.
        vm.SelectedClass = clsA;
        Assert.Equal("AlphaField", Assert.Single(vm.Fields).Name);
    }

    [Fact]
    public async Task Refresh_LoadsSnapshotsAndClasses()
    {
        await SeedInventoryAsync();
        var vm = NewVm();

        await vm.RefreshAsync();
        await vm.PendingLoad!;   // class load started by the snapshot selection

        Assert.NotNull(vm.SelectedSnapshot);
        Assert.Contains(vm.Classes, c => c.ClassName == "BP_Item_C");
    }

    [Fact]
    public async Task SelectClass_SuggestsKey_AndPreTicksValues()
    {
        await SeedInventoryAsync();
        var vm = NewVm();
        await vm.RefreshAsync();
        await vm.PendingLoad!;

        vm.SelectedClass = vm.Classes.First(c => c.ClassName == "BP_Item_C");
        await vm.PendingLoad!;   // field load + key suggestion

        // ItemID is the best key (int, id-named, partitions 2<3) -> Field mode.
        Assert.Equal("Field", vm.SelectedKeyMode);
        Assert.Equal("ItemID", vm.SelectedKeyField);
        // Quantity is an interesting value field -> pre-ticked; the key is not.
        Assert.True(vm.Fields.First(f => f.Name == "Quantity").IsValue);
        Assert.False(vm.Fields.First(f => f.Name == "ItemID").IsValue);
    }

    [Fact]
    public async Task RunPivot_FieldMode_PopulatesGroups()
    {
        await SeedInventoryAsync();
        var vm = NewVm();
        await vm.RefreshAsync();
        await vm.PendingLoad!;
        vm.SelectedClass = vm.Classes.First(c => c.ClassName == "BP_Item_C");
        await vm.PendingLoad!;

        Assert.True(vm.CanRunPivot);
        await vm.RunPivotCommand.ExecuteAsync(null);

        Assert.Equal(2, vm.Results.Count);
        var top = vm.Results[0];
        Assert.Equal("1", top.KeyValue);
        Assert.Equal(2, top.Count);
        Assert.Contains("Quantity=", top.ValuesDisplay);
    }

    [Fact]
    public async Task RunPivot_CompositeKey_TickedKeyField_GroupsByTuple()
    {
        await SeedInventoryAsync();
        var vm = NewVm();
        await vm.RefreshAsync();
        await vm.PendingLoad!;
        vm.SelectedClass = vm.Classes.First(c => c.ClassName == "BP_Item_C");
        await vm.PendingLoad!;

        // Primary key = ItemID (auto-suggested Field mode). Additionally tick Quantity
        // as a key → composite (ItemID · Quantity), so each item row is its own group.
        vm.SelectedKeyMode  = "Field";
        vm.SelectedKeyField = "ItemID";
        vm.Fields.First(f => f.Name == "Quantity").IsKey = true;

        await vm.RunPivotCommand.ExecuteAsync(null);

        Assert.Equal(3, vm.Results.Count);
        Assert.Contains(vm.Results, r => r.KeyValue == "1 · 10");
        Assert.Contains(vm.Results, r => r.KeyValue == "1 · 20");
        Assert.Contains(vm.Results, r => r.KeyValue == "2 · 5");
    }

    [Fact]
    public void OpenInLiveWalker_RaisesNavigate()
    {
        var vm = NewVm();
        string? nav = null;
        vm.NavigateToInstance += a => nav = a;

        vm.OpenInLiveWalkerCommand.Execute(new PivotResultRow { ObjAddr = "0x7FF600001234" });
        Assert.Equal("0x7FF600001234", nav);

        nav = null;
        vm.OpenInLiveWalkerCommand.Execute(new PivotResultRow { ObjAddr = "" });
        Assert.Null(nav);
    }

    // ---- Concurrency guard: a stale field-load must not clobber the latest ----

    // ISnapshotStore stub whose ListPivotFieldsAsync is gated per class so a test
    // can complete an earlier (superseded) load AFTER a later one.
    private sealed class GatedStore : ISnapshotStore
    {
        // Concurrent because the VM now invokes the store off the UI thread
        // (Task.Run), so gates are created on a thread-pool thread.
        public readonly System.Collections.Concurrent.ConcurrentDictionary<string, TaskCompletionSource<IReadOnlyList<PivotFieldInfo>>> Gates = new();

        public string DatabasePath => "";
        public void SetActiveGame(string? peHash) { }
        public Task<IReadOnlyList<SnapshotMeta>> ListSnapshotsAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<SnapshotMeta>>(new[] { new SnapshotMeta { Id = 1, Label = "s" } });
        public Task<IReadOnlyList<PivotClassInfo>> ListPivotClassesAsync(long snapshotId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<PivotClassInfo>>(new[]
            {
                new PivotClassInfo { ClassName = "A", InstanceCount = 2 },
                new PivotClassInfo { ClassName = "B", InstanceCount = 2 },
            });
        public Task<IReadOnlyList<PivotFieldInfo>> ListPivotFieldsAsync(long snapshotId, string className, CancellationToken ct = default)
        {
            var tcs = new TaskCompletionSource<IReadOnlyList<PivotFieldInfo>>();
            Gates[className] = tcs;
            return tcs.Task;
        }

        public Task<long> CreateSnapshotAsync(SnapshotMeta meta, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<int> WriteChunkAsync(long id, IReadOnlyList<SnapshotCapturedObject> o, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<ICaptureSession> BeginCaptureSessionAsync(CancellationToken ct = default) => throw new NotImplementedException();
        public Task FinalizeSnapshotAsync(long id, int oc, int fc, CancellationToken ct = default) => throw new NotImplementedException();
        public Task DeleteSnapshotAsync(long id, bool reclaim = false, CancellationToken ct = default) => throw new NotImplementedException();
        public Task DeleteAllSnapshotsAsync(CancellationToken ct = default) => throw new NotImplementedException();
        public Task<SnapshotUsage> GetUsageAsync(CancellationToken ct = default) => throw new NotImplementedException();
        public Task<SnapshotDiffResult> DiffSnapshotsAsync(long a, long b, SnapshotDiffFilter f, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<SnapshotGroupResult> GroupMatchAsync(SnapshotGroupQuery q, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<SpcResult> SpcQueryAsync(SpcQuery q, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<SpcGroupResult> SpcGroupQueryAsync(SpcGroupQuery q, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<DiscoveryResult> DiscoverChangesAsync(DiscoveryQuery q, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<PivotResult> PivotAsync(PivotQuery q, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<int> EnforceQuotaAsync(long bytes, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<PivotClassInfo>> ListPivotArrayClassesAsync(long s, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<PivotArrayFieldInfo>> ListPivotArrayFieldsAsync(long s, string c, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<PivotFieldInfo>> ListPivotArrayPropsAsync(long s, string c, string af, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<PivotResult> PivotArrayAsync(ArrayPivotQuery q, CancellationToken ct = default) => throw new NotImplementedException();

        // N1: per-game, per-tab class denylist. The gated-class pivot test fleet
        // exercises the picker filter via Pivot scope, so back it with a real set.
        public readonly HashSet<string> PivotDenylist = new(StringComparer.Ordinal);
        public HashSet<string> GetClassDenylist(DenylistScope scope) =>
            scope == DenylistScope.Pivot
                ? new HashSet<string>(PivotDenylist, StringComparer.Ordinal)
                : new HashSet<string>(StringComparer.Ordinal);
        public void SetClassDenylist(DenylistScope scope, HashSet<string> classes)
        {
            if (scope != DenylistScope.Pivot) return;
            PivotDenylist.Clear();
            foreach (var c in classes) PivotDenylist.Add(c);
        }
    }

    private static PivotFieldInfo Field(string name)
        => new() { Name = name, DeclaredType = "IntProperty", DistinctCount = 1, InstanceCount = 2 };

    // ---- Phase C4: DataTable-native (zero-config) pivot source ----

    /// <summary>Fake dump service serving one DataTable list + a 2-row walk.</summary>
    private sealed class DtDumpService : StubDumpService
    {
        public override Task<FindInstancesResult> FindInstancesAsync(
            string className, bool exactMatch = false, int limit = 500, bool newestFirst = false, string nameFilter = "", IReadOnlyList<string>? excludeClasses = null, CancellationToken ct = default)
            => Task.FromResult(new FindInstancesResult
            {
                Instances = new()
                {
                    new InstanceResult { Name = "DT_Items", Address = "0xDA7A", ClassName = "DataTable" },
                    // A non-DataTable instance must be filtered out by the VM.
                    new InstanceResult { Name = "SomeActor", Address = "0xBEEF", ClassName = "Actor" },
                },
            });

        public override Task<DataTableWalkResult> WalkDataTableRowsAsync(
            string addr, int offset = 0, int limit = 64, CancellationToken ct = default)
            => Task.FromResult(new DataTableWalkResult
            {
                RowCount = 2, RowStructName = "FItemRow",
                Rows = new()
                {
                    new DataTableRowInfo { RowName = "Sword", DataAddr = "0x1000",
                        Fields = new() { new LiveFieldValue { Name = "Damage", TypeName = "IntProperty", TypedValue = "50" } } },
                    new DataTableRowInfo { RowName = "Shield", DataAddr = "0x2000",
                        Fields = new() { new LiveFieldValue { Name = "Damage", TypeName = "IntProperty", TypedValue = "0" } } },
                },
            });
    }

    [Fact]
    public async Task DataTableSource_ListsTables_FiltersNonDataTables()
    {
        var vm = new ClassPivotViewModel(_store, new MockLoggingService(), null, new DtDumpService());

        vm.SelectedSource = "DataTable";   // empty list → triggers a DataTable refresh
        await vm.PendingLoad!;

        // The Actor instance is filtered; only the DataTable remains.
        Assert.Equal("DT_Items", Assert.Single(vm.DataTables).Name);
        Assert.True(vm.IsDataTableSource);
        Assert.False(vm.IsSnapshotSource);
    }

    [Fact]
    public async Task DataTableSource_SelectTable_LoadsFields_AndRunProjectsRows()
    {
        var vm = new ClassPivotViewModel(_store, new MockLoggingService(), null, new DtDumpService());
        vm.SelectedSource = "DataTable";
        await vm.PendingLoad!;

        vm.SelectedDataTable = vm.DataTables[0];
        await vm.PendingLoad!;            // walk DataTable → field picker + cached rows

        Assert.Contains(vm.Fields, f => f.Name == "Damage");
        Assert.True(vm.CanRunPivot);

        await vm.RunPivotCommand.ExecuteAsync(null);

        // One row per DataTable row, keyed by RowName, no collisions.
        Assert.Equal(2, vm.Results.Count);
        Assert.Equal(new[] { "Sword", "Shield" }, vm.Results.Select(r => r.KeyValue));
        Assert.All(vm.Results, r => Assert.Equal(1, r.Count));
        Assert.Contains("Damage=50", vm.Results[0].ValuesDisplay);
        Assert.Equal("0x1000", vm.Results[0].ObjAddr);   // CE handoff = row struct addr
    }

    /// <summary>DataTable walk gated per address so a test can complete a stale
    /// (superseded) walk AFTER the selection was cleared.</summary>
    private sealed class GatedDtDumpService : StubDumpService
    {
        public readonly System.Collections.Concurrent.ConcurrentDictionary<string, TaskCompletionSource<DataTableWalkResult>> Gates = new();

        public override Task<FindInstancesResult> FindInstancesAsync(
            string className, bool exactMatch = false, int limit = 500, bool newestFirst = false, string nameFilter = "", IReadOnlyList<string>? excludeClasses = null, CancellationToken ct = default)
            => Task.FromResult(new FindInstancesResult
            {
                Instances = new()
                {
                    new InstanceResult { Name = "DT_A", Address = "0xA", ClassName = "DataTable" },
                    new InstanceResult { Name = "DT_B", Address = "0xB", ClassName = "DataTable" },
                },
            });

        public override Task<DataTableWalkResult> WalkDataTableRowsAsync(
            string addr, int offset = 0, int limit = 64, CancellationToken ct = default)
        {
            var tcs = new TaskCompletionSource<DataTableWalkResult>();
            Gates[addr] = tcs;
            return tcs.Task;
        }
    }

    [Fact]
    public async Task DataTableSource_NullSelection_SupersedesInflightWalk()
    {
        // Regression: LoadDataTableFieldsAsync bumped the field-load guard AFTER the
        // null-selection early-return, so a slow walk for the previously-selected
        // DataTable could complete and repopulate Fields after the selection was
        // cleared. Bumping at entry makes the null path supersede the in-flight walk.
        var dump = new GatedDtDumpService();
        var vm = new ClassPivotViewModel(_store, new MockLoggingService(), null, dump);
        vm.SelectedSource = "DataTable";
        await vm.PendingLoad!;   // FindInstances → DataTables populated

        // Select A → gated walk begins (genuinely in flight).
        vm.SelectedDataTable = vm.DataTables.First(d => d.Name == "DT_A");
        var loadA = vm.PendingLoad!;
        await WaitForDtGate(dump, "0xA");

        // Clear the selection → entry-bumped guard supersedes A's walk.
        vm.SelectedDataTable = null;
        if (vm.PendingLoad != null) await vm.PendingLoad;

        // Let the stale A walk finish — it must bail and NOT repopulate Fields.
        dump.Gates["0xA"].SetResult(new DataTableWalkResult
        {
            RowCount = 1, RowStructName = "FRow",
            Rows = new()
            {
                new DataTableRowInfo { RowName = "Stale", DataAddr = "0x1",
                    Fields = new() { new LiveFieldValue { Name = "StaleField", TypeName = "IntProperty", TypedValue = "1" } } },
            },
        });
        await loadA;

        Assert.Empty(vm.Fields);   // stale walk bailed on the entry-bumped guard
    }

    private static async Task WaitForDtGate(GatedDtDumpService dump, string addr)
    {
        for (int i = 0; i < 400 && !dump.Gates.ContainsKey(addr); i++)
            await Task.Delay(5, TestContext.Current.CancellationToken);
    }

    // ---- C5: right-click "Pivot this property" handoff ----

    [Fact]
    public async Task PivotForAsync_SelectsClass_AndTicksProperty()
    {
        await SeedInventoryAsync();
        var vm = NewVm();

        await vm.PivotForAsync("BP_Item_C", "Quantity");

        Assert.Equal("Snapshot", vm.SelectedSource);
        Assert.Equal("BP_Item_C", vm.SelectedClass?.ClassName);
        // ItemID is the auto-suggested key; the handed-off Quantity becomes a value.
        Assert.True(vm.Fields.First(f => f.Name == "Quantity").IsValue);
    }

    [Fact]
    public async Task PivotForAsync_UnknownClass_ReportsAndDoesNotThrow()
    {
        await SeedInventoryAsync();
        var vm = NewVm();

        await vm.PivotForAsync("DoesNotExist", "Whatever");

        Assert.Null(vm.SelectedClass);
        Assert.Contains("not in the selected snapshot", vm.StatusText);
    }

    // ---- C3: change-driven discovery (the automatic front-door) ----

    // Seed a before/after pair on one PlayerState: Gold drops, Level is constant.
    private async Task SeedBeforeAfterAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        long s1 = await _store.CreateSnapshotAsync(new SnapshotMeta { Label = "before" }, ct);
        await _store.WriteChunkAsync(s1, new[]
        {
            Obj(1, "PlayerState", "/G.M:L.PlayerState_0", ("Gold", 100), ("Level", 5)),
        }, ct);
        await _store.FinalizeSnapshotAsync(s1, 1, 2, ct);

        long s2 = await _store.CreateSnapshotAsync(new SnapshotMeta { Label = "after" }, ct);
        await _store.WriteChunkAsync(s2, new[]
        {
            Obj(1, "PlayerState", "/G.M:L.PlayerState_0", ("Gold", 50), ("Level", 5)),
        }, ct);
        await _store.FinalizeSnapshotAsync(s2, 1, 2, ct);
    }

    [Fact]
    public async Task RunDiscover_SurfacesChangedTarget_DropsConstant()
    {
        await SeedBeforeAfterAsync();
        var vm = NewVm();
        await vm.RefreshAsync();            // loads snapshots + sets From/To defaults
        Assert.True(vm.CanDiscover);        // two distinct snapshots

        await vm.RunDiscoverCommand.ExecuteAsync(null);

        Assert.Contains(vm.DiscoverResults, c => c.PropName == "Gold");
        Assert.DoesNotContain(vm.DiscoverResults, c => c.PropName == "Level");
    }

    [Fact]
    public async Task UseDiscoverCandidate_PivotsTheChosenTarget()
    {
        await SeedBeforeAfterAsync();
        var vm = NewVm();
        await vm.RefreshAsync();
        await vm.RunDiscoverCommand.ExecuteAsync(null);
        var gold = vm.DiscoverResults.First(c => c.PropName == "Gold");

        await vm.UseDiscoverCandidateAsync(gold);

        Assert.Equal("Snapshot", vm.SelectedSource);
        Assert.Equal("PlayerState", vm.SelectedClass?.ClassName);
        Assert.True(vm.Fields.First(f => f.Name == "Gold").IsValue);
        Assert.NotEmpty(vm.Results);        // pivot auto-ran on the chosen target
    }

    [Fact]
    public async Task CanDiscover_RequiresTwoDistinctSnapshots()
    {
        var ct = TestContext.Current.CancellationToken;
        long s1 = await _store.CreateSnapshotAsync(new SnapshotMeta { Label = "only" }, ct);
        await _store.WriteChunkAsync(s1, new[]
        {
            Obj(1, "PlayerState", "/G.M:L.PlayerState_0", ("Gold", 100)),
        }, ct);
        await _store.FinalizeSnapshotAsync(s1, 1, 1, ct);

        var vm = NewVm();
        await vm.RefreshAsync();
        // Only one snapshot → From and To resolve to the same → discovery disabled.
        Assert.False(vm.CanDiscover);
    }

    // ---- C6: Snapshot Array source (inner-key pivot) ----

    private async Task SeedCargoAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        long id = await _store.CreateSnapshotAsync(new SnapshotMeta { Label = "cargo" }, ct);
        var ps = new SnapshotCapturedObject
        {
            Index = 9, Addr = "0x9000", Name = "PS_9", ClassName = "PlayerState",
            OuterClassName = "World", Path = "/G.M:L.PlayerState_0",
        };
        var arr = new SnapshotCapturedArray { Field = "Cargo" };
        arr.Elements.Add(MakeSlot(0, "Fuel", 100));
        arr.Elements.Add(MakeSlot(1, "Ore", 50));
        ps.Arrays.Add(arr);
        await _store.WriteChunkAsync(id, new[] { ps }, ct);
        await _store.FinalizeSnapshotAsync(id, 1, 2, ct);
    }

    private static SnapshotCapturedArrayElement MakeSlot(int i, string item, int qty)
    {
        var el = new SnapshotCapturedArrayElement { Index = i, KeyName = "ItemID", KeyValue = item };
        el.Fields.Add(new SnapshotCapturedField { Name = "Quantity", Type = "IntProperty", Hex = IntHex(qty), Offset = 0x8 });
        return el;
    }

    [Fact]
    public async Task ArraySource_LoadsArrayClassFieldsProps_AndPivots()
    {
        await SeedCargoAsync();
        var vm = NewVm();
        await vm.RefreshAsync();
        await vm.PendingLoad!;

        vm.SelectedSource = "Snapshot Array";   // reloads class list to array classes
        await vm.PendingLoad!;
        Assert.True(vm.IsArraySource);
        Assert.Contains(vm.Classes, c => c.ClassName == "PlayerState");

        vm.SelectedClass = vm.Classes.First(c => c.ClassName == "PlayerState");
        await vm.PendingLoad!;   // LoadArrayFieldsAsync (auto-selects the array field)
        await vm.PendingLoad!;   // LoadArrayPropsAsync

        var cargo = Assert.Single(vm.ArrayFields);
        Assert.Equal("Cargo", cargo.ArrayField);
        Assert.Equal("ItemID", cargo.InnerKeyName);
        Assert.NotNull(vm.SelectedArrayField);
        Assert.Contains(vm.Fields, f => f.Name == "Quantity");
        Assert.True(vm.CanRunPivot);

        await vm.RunPivotCommand.ExecuteAsync(null);

        // Grouped by inner-key value (ItemID): Fuel + Ore.
        Assert.Equal(2, vm.Results.Count);
        Assert.Contains(vm.Results, r => r.KeyValue == "Fuel");
        Assert.Contains(vm.Results, r => r.KeyValue == "Ore");
    }

    [Fact]
    public async Task ResultFilter_FiltersAndRestores()
    {
        await SeedInventoryAsync();
        var vm = NewVm();
        await vm.RefreshAsync();
        await vm.PendingLoad!;
        vm.SelectedClass = vm.Classes.First(c => c.ClassName == "BP_Item_C");
        await vm.PendingLoad!;
        await vm.RunPivotCommand.ExecuteAsync(null);
        int all = vm.Results.Count;
        Assert.True(all > 0);

        vm.ResultFilter = "no-such-token-xyz";   // matches nothing
        Assert.Empty(vm.Results);

        vm.ResultFilter = "";                     // cleared → full set back
        Assert.Equal(all, vm.Results.Count);
    }

    [Fact]
    public void LocateResultInGWorld_RaisesEvent_RegardlessOfTheClientGWorldFlag()
    {
        // audit #5 AE10 — this used to assert the command was gated off when
        // IsGWorldAvailable was false. That flag is EngineState.HasGWorld, i.e. "the
        // AOB scan produced a non-zero &GWorld slot address", NOT "a live UWorld
        // exists": the DLL has world-recovery fallbacks that work when the scan did
        // not, so the gate disabled the button on games where locate worked. The DLL
        // answers authoritatively; the client must not pre-refuse.
        var vm = NewVm();
        string? hit = null;
        vm.LocateInGWorld += a => hit = a;
        var row = new PivotResultRow { ObjAddr = "0xABC", KeyValue = "K" };
        vm.SelectedResult = row;

        Assert.True(vm.CanLocateResultInGWorld);   // selection is the only precondition
        vm.LocateResultInGWorldCommand.Execute(row);
        Assert.Equal("0xABC", hit);

        hit = null;
        vm.LocateResultInGWorldCommand.Execute(row);
        Assert.Equal("0xABC", hit);                // and repeatable
    }

    [Fact]
    public void LocateResultInGWorld_StillRequiresAnAddress()
    {
        // Removing the GWorld gate must not remove the REAL precondition.
        var vm = NewVm();
        string? hit = null;
        vm.LocateInGWorld += a => hit = a;

        vm.LocateResultInGWorldCommand.Execute(new PivotResultRow { ObjAddr = "", KeyValue = "K" });
        Assert.Null(hit);
        vm.LocateResultInGWorldCommand.Execute(null);
        Assert.Null(hit);
    }

    [Fact]
    public void LocateResultInGameEngine_RaisesEvent_NotGatedOnGWorld()
    {
        var vm = NewVm();
        string? hit = null;
        vm.LocateInGameEngine += a => hit = a;
        vm.LocateResultInGameEngineCommand.Execute(new PivotResultRow { ObjAddr = "0xDEF" });
        Assert.Equal("0xDEF", hit);
    }

    [Fact]
    public async Task RapidClassSwitch_StaleLoadDoesNotClobberLatest()
    {
        var store = new GatedStore();
        var vm = new ClassPivotViewModel(store, new MockLoggingService());
        await vm.RefreshAsync();        // loads snapshot + classes (classes are immediate)
        await vm.PendingLoad!;

        // Select A (gated load starts), then B (gated load starts), so A is the
        // STALE load and B the newest. The store call runs on a thread-pool
        // thread (Task.Run), so we must wait for A's gate to register BEFORE
        // superseding it with B — otherwise on a slow runner A can be superseded
        // and bail out before it ever reaches the gated store call, leaving no
        // Gates["A"] (the flaky CI failure). Waiting for A's gate first removes
        // the race while keeping the same intent (a genuinely in-flight stale A).
        vm.SelectedClass = vm.Classes.First(c => c.ClassName == "A");
        var loadA = vm.PendingLoad!;
        await WaitForGate(store, "A");
        vm.SelectedClass = vm.Classes.First(c => c.ClassName == "B");
        var loadB = vm.PendingLoad!;
        await WaitForGate(store, "B");

        // Complete the NEWER load first, then the stale one.
        store.Gates["B"].SetResult(new[] { Field("BetaField") });
        await loadB;
        store.Gates["A"].SetResult(new[] { Field("AlphaField") });
        await loadA;

        // The stale A load must have bailed — Fields shows only B's field.
        Assert.Equal("BetaField", Assert.Single(vm.Fields).Name);
    }

    [Fact]
    public async Task CacheHit_DoesNotLetStaleInflightLoadClobberLatest()
    {
        // Regression: the monotonic field-load guard was bumped only on the
        // cache-MISS path, so a miss in flight (class A) that completed AFTER a
        // switch to an already-CACHED class (B) overwrote B's fields with A's.
        var store = new GatedStore();
        var vm = new ClassPivotViewModel(store, new MockLoggingService());
        await vm.RefreshAsync();
        await vm.PendingLoad!;

        // Load + complete B so its field list is cached in the VM.
        vm.SelectedClass = vm.Classes.First(c => c.ClassName == "B");
        var loadB1 = vm.PendingLoad!;
        await WaitForGate(store, "B");
        store.Gates["B"].SetResult(new[] { Field("BetaField") });
        await loadB1;
        Assert.Equal("BetaField", Assert.Single(vm.Fields).Name);

        // Select A (cache miss → gated, in flight), then re-select B (cache hit →
        // applied instantly, no scan). Wait for A's gate so it is genuinely in
        // flight before B supersedes it (slow-runner safety, as in RapidClassSwitch).
        vm.SelectedClass = vm.Classes.First(c => c.ClassName == "A");
        var loadA = vm.PendingLoad!;
        await WaitForGate(store, "A");
        vm.SelectedClass = vm.Classes.First(c => c.ClassName == "B");   // cache hit
        if (vm.PendingLoad != null) await vm.PendingLoad;
        Assert.Equal("BetaField", Assert.Single(vm.Fields).Name);       // B from cache

        // Let the stale A load finish — it must bail on the entry-bumped guard and
        // NOT clobber B's cached fields.
        store.Gates["A"].SetResult(new[] { Field("AlphaField") });
        await loadA;
        Assert.Equal("BetaField", Assert.Single(vm.Fields).Name);
    }

    // The store call is deferred to a thread pool (Task.Run), so spin briefly until
    // the gated load has registered its TaskCompletionSource.
    private static async Task WaitForGate(GatedStore store, string cls)
    {
        for (int i = 0; i < 400 && !store.Gates.ContainsKey(cls); i++)
            await Task.Delay(5, TestContext.Current.CancellationToken);
    }

    // ==================================================================
    // Audit #5 AF5 — a refresh must not discard the user's choices.
    //
    // RefreshAsync rebuilds Snapshots through UiCollection.Reset, then used to
    // hard-set SelectedSnapshot to Snapshots[0] and re-tick the two newest
    // DiscoverPicks. Any deliberate pick was silently reverted. That made the
    // bubbled-SelectionChanged bug (fixed by Helpers/TabActivation) destructive
    // rather than merely wasteful: an ordinary in-tab ComboBox pick re-ran tab
    // activation, which refreshed, which reset the very selection just made.
    //
    // Everything asserts BY Id — UiCollection.Reset repopulates with new
    // SnapshotMeta instances, so reference or index comparisons would pass for
    // the wrong reason.
    // ==================================================================

    private async Task<long> SeedLabelledAsync(string label)
    {
        var ct = TestContext.Current.CancellationToken;
        long id = await _store.CreateSnapshotAsync(new SnapshotMeta { Label = label }, ct);
        await _store.WriteChunkAsync(id, new[]
        {
            Obj(1, "BP_Item_C", "/G.M:L.Item_0", ("ItemID", 1), ("Quantity", 10)),
        }, ct);
        await _store.FinalizeSnapshotAsync(id, 1, 2, ct);
        return id;
    }

    [Fact]
    public async Task RefreshAsync_KeepsTheSelectedSnapshot()
    {
        await SeedLabelledAsync("first");
        await SeedLabelledAsync("second");
        await SeedLabelledAsync("third");

        var vm = NewVm();
        await vm.RefreshAsync();
        if (vm.PendingLoad != null) await vm.PendingLoad;

        // Pick something that is NOT the newest — the default would mask the bug.
        var wanted = vm.Snapshots.Last();
        vm.SelectedSnapshot = wanted;
        if (vm.PendingLoad != null) await vm.PendingLoad;
        long wantedId = wanted.Id;

        await vm.RefreshAsync();
        if (vm.PendingLoad != null) await vm.PendingLoad;

        Assert.NotNull(vm.SelectedSnapshot);
        Assert.Equal(wantedId, vm.SelectedSnapshot!.Id);
    }

    [Fact]
    public async Task RefreshAsync_KeepsTheDiscoverPickTicks()
    {
        await SeedLabelledAsync("first");
        await SeedLabelledAsync("second");
        await SeedLabelledAsync("third");

        var vm = NewVm();
        await vm.RefreshAsync();
        if (vm.PendingLoad != null) await vm.PendingLoad;

        // Tick oldest + newest — deliberately NOT the two-newest default.
        foreach (var p in vm.DiscoverPicks) p.IsSelected = false;
        vm.DiscoverPicks.First().IsSelected = true;
        vm.DiscoverPicks.Last().IsSelected  = true;
        var wantedIds = vm.DiscoverPicks.Where(p => p.IsSelected)
                                        .Select(p => p.Id).OrderBy(i => i).ToList();

        await vm.RefreshAsync();
        if (vm.PendingLoad != null) await vm.PendingLoad;

        var actualIds = vm.DiscoverPicks.Where(p => p.IsSelected)
                                        .Select(p => p.Id).OrderBy(i => i).ToList();
        Assert.Equal(wantedIds, actualIds);
    }

    /// <summary>The default must still apply on a FIRST build — preserving a
    /// selection must not turn into "never select anything".</summary>
    [Fact]
    public async Task RefreshAsync_FirstBuild_StillDefaultsToNewestAndTwoNewestTicks()
    {
        await SeedLabelledAsync("first");
        await SeedLabelledAsync("second");
        await SeedLabelledAsync("third");

        var vm = NewVm();
        await vm.RefreshAsync();
        if (vm.PendingLoad != null) await vm.PendingLoad;

        Assert.NotNull(vm.SelectedSnapshot);
        Assert.Equal(vm.Snapshots[0].Id, vm.SelectedSnapshot!.Id);
        Assert.Equal(2, vm.DiscoverPicks.Count(p => p.IsSelected));
        Assert.True(vm.DiscoverPicks[0].IsSelected);
        Assert.True(vm.DiscoverPicks[1].IsSelected);
    }

    /// <summary>A snapshot deleted between refreshes must fall through to the
    /// newest rather than leaving the picker empty.</summary>
    [Fact]
    public async Task RefreshAsync_SelectedSnapshotDeleted_FallsBackToNewest()
    {
        await SeedLabelledAsync("first");
        long doomed = await SeedLabelledAsync("doomed");

        var vm = NewVm();
        await vm.RefreshAsync();
        if (vm.PendingLoad != null) await vm.PendingLoad;

        vm.SelectedSnapshot = vm.Snapshots.First(m => m.Id == doomed);
        if (vm.PendingLoad != null) await vm.PendingLoad;

        await _store.DeleteSnapshotAsync(doomed, false, TestContext.Current.CancellationToken);
        await vm.RefreshAsync();
        if (vm.PendingLoad != null) await vm.PendingLoad;

        Assert.NotNull(vm.SelectedSnapshot);
        Assert.NotEqual(doomed, vm.SelectedSnapshot!.Id);
        Assert.Equal(vm.Snapshots[0].Id, vm.SelectedSnapshot!.Id);
    }
}
