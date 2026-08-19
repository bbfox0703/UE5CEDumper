using System.IO;
using System.Linq;
using UE5DumpUI.Core;
using UE5DumpUI.Models;
using UE5DumpUI.Services;
using UE5DumpUI.ViewModels;
using Xunit;

namespace UE5DumpUI.Tests;

public class SnapshotViewModelTests : IDisposable
{
    private readonly string _tempDir;
    private readonly SnapshotStore _store;

    public SnapshotViewModelTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"UE5DumpSnapVm_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _store = new SnapshotStore(new MockPlatformService(_tempDir));
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); }
        catch { /* best effort */ }
    }

    // The logger handed to the most recently built VM. The store swallows a failed
    // capture (the outer catch deletes the partial row) and the VM only records the
    // reason in StatusText / ErrorMessage / the log, so a bare "collection was empty"
    // tells us nothing. Every store-shaped assertion in this class routes through
    // Diag() so a CI-only flake names its own cause on the first occurrence.
    private MockLoggingService _lastLog = new();

    // `got` is object? (not int) so the pre-store asserts — which report a bool?/string —
    // can route through the same diagnosis. Putting Diag ONLY on the persisted-count assert
    // was not enough: the 2026-07-22 CI failure surfaced 8 lines earlier, at the first
    // stub-recorded flag, and printed nothing.
    private string Diag(string what, object? got, SnapshotViewModel vm)
    {
        // ALL levels, not just WARN/ERROR: the INFO lines trace how far the capture got
        // (chunk counts, "Finalising…", the completion summary), which is what tells us
        // whether it died early or at the very end.
        var all = _lastLog.Messages;
        var tail = all.Count > 12 ? all.GetRange(all.Count - 12, 12) : all;
        return $"{what}, got {got?.ToString() ?? "null"}. status='{vm.StatusText}' error='{vm.ErrorMessage}' " +
               $"capturing={vm.IsCapturing} log[{all.Count}]:{Environment.NewLine}  " +
               string.Join(Environment.NewLine + "  ", tail);
    }

    // Stub that streams 3 objects (4 fields) across two non-empty chunks, then a
    // terminal empty chunk — mirrors the DLL's stateless cursor pagination.
    private sealed class CaptureStub : StubDumpService
    {
        public string? LastDataType;
        public bool? LastGameOnly;
        public bool? LastNativeC;
        public bool? LastAutoSkipNoise;
        public string? LastNumericFamily;

        public override Task<int> BeginSnapshotAsync(string dataType, CancellationToken ct = default)
        {
            LastDataType = dataType;
            return Task.FromResult(3);
        }

        public override Task<SnapshotChunkResult> SnapshotChunkAsync(
            string dataType, bool gameOnly, int offset, int limit,
            bool nativeC = false, bool autoSkipNoise = true,
            string numericFamily = "Any", CancellationToken ct = default)
        {
            LastGameOnly = gameOnly;
            LastNativeC = nativeC;
            LastAutoSkipNoise = autoSkipNoise;
            LastNumericFamily = numericFamily;
            var r = new SnapshotChunkResult
            {
                Total = 3, WalkMs = 5, SerializeMs = 2,
                ReadMs = 3, RxLogMs = 1, ParseMs = 7, BuildMs = 4,
            };
            if (offset == 0)
            {
                r.Scanned = 2;
                r.Objects.Add(MakeObj(0, ("A", "IntProperty", "01000000"), ("B", "FloatProperty", "0000803F")));
                r.Objects.Add(MakeObj(1, ("A", "IntProperty", "02000000")));
            }
            else if (offset == 2)
            {
                r.Scanned = 1;
                r.Objects.Add(MakeObj(2, ("A", "IntProperty", "03000000")));
            }
            else
            {
                r.Scanned = 0;  // terminal
            }
            return Task.FromResult(r);
        }

        private static SnapshotCapturedObject MakeObj(int idx, params (string n, string t, string h)[] fields)
        {
            var o = new SnapshotCapturedObject
            {
                Index = idx, Addr = $"0x{idx:X}", Name = $"Obj_{idx}",
                ClassName = "BP_Thing_C", OuterClassName = "World",
                Path = $"/Game/Map.Map:PersistentLevel.Obj_{idx}",
            };
            foreach (var (n, t, h) in fields)
                o.Fields.Add(new SnapshotCapturedField { Name = n, Type = t, Hex = h });
            return o;
        }
    }

    // Delivers one chunk, then BLOCKS on the second fetch until cancelled — so a test can
    // cancel mid-stream deterministically and assert the partial snapshot is cleaned up.
    private sealed class BlockingCaptureStub : StubDumpService
    {
        public readonly TaskCompletionSource SecondChunkReached =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override Task<int> BeginSnapshotAsync(string dataType, CancellationToken ct = default)
            => Task.FromResult(100);

        public override async Task<SnapshotChunkResult> SnapshotChunkAsync(
            string dataType, bool gameOnly, int offset, int limit,
            bool nativeC = false, bool autoSkipNoise = true,
            string numericFamily = "Any", CancellationToken ct = default)
        {
            if (offset == 0)
            {
                var r = new SnapshotChunkResult { Total = 100, Scanned = 2 };
                var o = new SnapshotCapturedObject
                {
                    Index = 0, Addr = "0x0", Name = "Obj_0",
                    ClassName = "BP_Thing_C", OuterClassName = "World",
                    Path = "/Game/Map.Map:PersistentLevel.Obj_0",
                };
                o.Fields.Add(new SnapshotCapturedField { Name = "A", Type = "IntProperty", Hex = "01000000" });
                r.Objects.Add(o);
                return r;
            }
            SecondChunkReached.TrySetResult();
            await Task.Delay(System.Threading.Timeout.Infinite, ct);   // throws OCE on cancel
            return new SnapshotChunkResult { Total = 100, Scanned = 0 };
        }
    }

    [Fact]
    public async Task Capture_CancelMidStream_DeletesPartialSnapshot()
    {
        var ct = TestContext.Current.CancellationToken;
        var dump = new BlockingCaptureStub();
        var vm = new SnapshotViewModel(dump, _store, new MockLoggingService())
        {
            SelectedScope = "NumericNoByte", GameOnly = true, Label = "partial",
        };
        vm.SetEngineState(new EngineState { PeHash = "PEHASH", UEVersion = 504, ModuleBase = "7FF600000000", ProcessCreationTime = "01D9ABCDEF012345" });

        var task = vm.CaptureCommand.ExecuteAsync(null);
        await dump.SecondChunkReached.Task.WaitAsync(TimeSpan.FromSeconds(10), ct);   // producer blocked on chunk 2
        vm.CancelCommand.Execute(null);
        await task;

        Assert.False(vm.IsCapturing);
        Assert.Contains("cancel", vm.StatusText, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(await _store.ListSnapshotsAsync(ct));   // the partial snapshot was deleted
    }

    // Simulates a pipe DISCONNECT mid-capture: delivers one chunk, then the second
    // fetch throws a BARE OperationCanceledException (no token) WITHOUT the capture
    // token being cancelled — exactly how PipeClient surfaces a disconnect
    // (DisconnectAsync/Dispose complete pending requests via TrySetCanceled() with no
    // token). The producer must NOT treat this as a clean cancel.
    private sealed class DisconnectingCaptureStub : StubDumpService
    {
        public override Task<int> BeginSnapshotAsync(string dataType, CancellationToken ct = default)
            => Task.FromResult(100);

        public override Task<SnapshotChunkResult> SnapshotChunkAsync(
            string dataType, bool gameOnly, int offset, int limit,
            bool nativeC = false, bool autoSkipNoise = true,
            string numericFamily = "Any", CancellationToken ct = default)
        {
            if (offset == 0)
            {
                var r = new SnapshotChunkResult { Total = 100, Scanned = 2 };
                var o = new SnapshotCapturedObject
                {
                    Index = 0, Addr = "0x0", Name = "Obj_0",
                    ClassName = "BP_Thing_C", OuterClassName = "World",
                    Path = "/Game/Map.Map:PersistentLevel.Obj_0",
                };
                o.Fields.Add(new SnapshotCapturedField { Name = "A", Type = "IntProperty", Hex = "01000000" });
                r.Objects.Add(o);
                return Task.FromResult(r);
            }
            // Disconnect: a bare OCE while the capture token is NOT cancelled.
            throw new OperationCanceledException();
        }
    }

    // Regression for audit #3 H1: a pipe disconnect mid-capture used to be swallowed by
    // the producer's unfiltered catch, so the half-captured snapshot was finalised
    // is_usable=1 (the column default) and read downstream as a complete dataset. The
    // fix filters that catch on lct.IsCancellationRequested, so a bare disconnect-OCE
    // now faults the producer, CompleteSnapshotAsync is skipped, and the partial is
    // deleted — never left usable.
    [Fact]
    public async Task Capture_DisconnectMidStream_DoesNotSaveUsablePartial()
    {
        var ct = TestContext.Current.CancellationToken;
        var dump = new DisconnectingCaptureStub();
        var vm = new SnapshotViewModel(dump, _store, new MockLoggingService())
        {
            SelectedScope = "NumericNoByte", GameOnly = true, Label = "disc",
        };
        vm.SetEngineState(new EngineState { PeHash = "PEHASH", UEVersion = 504, ModuleBase = "7FF600000000", ProcessCreationTime = "01D9ABCDEF012345" });

        await vm.CaptureCommand.ExecuteAsync(null);

        Assert.False(vm.IsCapturing);
        // No snapshot may survive as usable — the truncated partial is deleted, not
        // finalised. (Before the fix this list held one usable, truncated snapshot.)
        Assert.Empty(await _store.ListSnapshotsAsync(ct));
    }

    // A disconnect BEFORE any snapshot row is created (bare OCE from BeginSnapshotAsync,
    // token uncancelled) — so there's no partial to delete/VACUUM and the auto-loop stops
    // deterministically without depending on SQLite reclaim timing (which is fast locally
    // but slow under CI lock contention — that timing dependency flaked the earlier
    // mid-stream variant).
    private sealed class DisconnectOnBeginStub : StubDumpService
    {
        public override Task<int> BeginSnapshotAsync(string dataType, CancellationToken ct = default)
            => Task.FromException<int>(new OperationCanceledException());
    }

    // Regression for audit #3 M7: a pipe disconnect during an AUTO-snapshot capture used
    // to be reported as CaptureOutcome.Cancelled, which the loop's `case Cancelled: return`
    // handled by exiting WITHOUT StopAutoSnapshot() — leaving AutoSnapshotEnabled stuck
    // true, _autoCts leaked, and manual capture disabled (wedged). A disconnect is now
    // reported as Failed (our token isn't cancelled), so the loop stops cleanly via
    // `case Failed`.
    [Fact]
    public async Task AutoSnapshot_DisconnectMidCapture_StopsLoopWithoutWedge()
    {
        var ct = TestContext.Current.CancellationToken;
        var dump = new DisconnectOnBeginStub();
        var vm = new SnapshotViewModel(dump, _store, new MockLoggingService())
        {
            SelectedScope = "NumericNoByte", GameOnly = true,
        };
        vm.SetEngineState(new EngineState { PeHash = "PEHASH", UEVersion = 504, ModuleBase = "7FF600000000", ProcessCreationTime = "01D9ABCDEF012345" });

        // Start the periodic loop; its first capture hits the disconnect (bare OCE).
        vm.AutoSnapshotEnabled = true;
        var loop = vm.AutoLoopTaskForTests;
        Assert.NotNull(loop);
        await loop!.WaitAsync(TimeSpan.FromSeconds(30), ct);

        // The loop stopped itself (not wedged): toggle reset + manual capture re-enabled.
        Assert.False(vm.AutoSnapshotEnabled);
        Assert.True(vm.CanManualCapture);
        // No usable snapshot was left behind (none was even created).
        Assert.Empty(await _store.ListSnapshotsAsync(ct));
    }

    [Fact]
    public async Task Capture_StreamsAllChunks_PersistsWithCorrectCounts()
    {
        var dump = new CaptureStub();
        _lastLog = new MockLoggingService();
        var vm = new SnapshotViewModel(dump, _store, _lastLog)
        {
            SelectedScope = "NumericNoByte",
            GameOnly = true,
            Label = "run1",
        };
        vm.SetEngineState(new EngineState { PeHash = "PEHASH", UEVersion = 504, ModuleBase = "7FF600000000", ProcessCreationTime = "01D9ABCDEF012345" });

        Assert.True(vm.CanCapture);
        await vm.CaptureCommand.ExecuteAsync(null);

        Assert.False(vm.IsCapturing);
        Assert.Equal("NumericNoByte", dump.LastDataType);
        // These four are written by CaptureStub.SnapshotChunkAsync, so a NULL here means the
        // first chunk fetch never happened — the capture threw between BeginSnapshotAsync
        // (proven to have run by LastDataType above) and the producer's first fetch, i.e. in
        // CreateSnapshotAsync / BeginCaptureSessionAsync. That is the shape the CI-only flake
        // took on 2026-07-22, so these carry Diag() too — not just the store assert below.
        Assert.True(dump.LastGameOnly, Diag("chunk fetch never ran (LastGameOnly)", dump.LastGameOnly, vm));
        Assert.False(dump.LastNativeC, Diag("Native-C default OFF (P3)", dump.LastNativeC, vm));
        Assert.True(dump.LastAutoSkipNoise, Diag("auto-skip noise default ON", dump.LastAutoSkipNoise, vm));

        var list = await _store.ListSnapshotsAsync(TestContext.Current.CancellationToken);
        // A capture that threw is swallowed: the outer catch deletes the partial row and
        // `finally` clears IsCapturing, so every assert above still passes and only the
        // count betrays it. Report the VM's own explanation instead of "was empty".
        Assert.True(list.Count == 1, Diag("expected 1 persisted snapshot", list.Count, vm));
        var saved = list[0];
        Assert.Equal("run1", saved.Label);
        Assert.Equal("PEHASH", saved.PeHash);
        // GameSessionId = PeHash-ProcessCreationTime (build 1227+); the per-launch
        // process creation time replaces ModuleBase, which is constant on no-ASLR games.
        Assert.Equal("PEHASH-01D9ABCDEF012345", saved.GameSessionId);
        Assert.Equal(504, saved.UeVersion);
        Assert.Equal(3, saved.ObjectCount);     // 3 objects streamed
        Assert.Equal(4, saved.FieldCount);      // 2 + 1 + 1 fields

        // The list refreshed into the VM, and the label reset for the next run.
        Assert.Single(vm.Snapshots);
        Assert.Equal("", vm.Label);
    }

    [Fact]
    public async Task Capture_PassesIncludeNativeFields_ToSnapshotChunk()
    {
        var dump = new CaptureStub();
        var vm = new SnapshotViewModel(dump, _store, new MockLoggingService())
        {
            SelectedScope = "NumericNoByte",
            GameOnly = true,
            IncludeNativeFields = true,   // P3 opt-in
            Label = "native-run",
        };
        vm.SetEngineState(new EngineState { PeHash = "PEHASH", UEVersion = 504, ModuleBase = "7FF600000000", ProcessCreationTime = "01D9ABCDEF012345" });

        await vm.CaptureCommand.ExecuteAsync(null);

        Assert.True(dump.LastNativeC);
    }

    [Fact]
    public async Task Capture_PassesAutoSkipNoise_ToSnapshotChunk()
    {
        var dump = new CaptureStub();
        var vm = new SnapshotViewModel(dump, _store, new MockLoggingService())
        {
            SelectedScope = "NumericNoByte",
            GameOnly = true,
            AutoSkipNoise = false,   // user opted OUT of the source-level skip
            Label = "no-skip-run",
        };
        vm.SetEngineState(new EngineState { PeHash = "PEHASH", UEVersion = 504, ModuleBase = "7FF600000000", ProcessCreationTime = "01D9ABCDEF012345" });

        await vm.CaptureCommand.ExecuteAsync(null);

        Assert.False(dump.LastAutoSkipNoise);
    }

    [Fact]
    public void CanCapture_RequiresEngineState()
    {
        var vm = new SnapshotViewModel(new CaptureStub(), _store, new MockLoggingService());
        Assert.False(vm.CanCapture);
        vm.SetEngineState(new EngineState { PeHash = "X" });
        Assert.True(vm.CanCapture);
    }

    [Fact]
    public async Task Capture_DefaultFamily_SendsAny()
    {
        var dump = new CaptureStub();
        var vm = new SnapshotViewModel(dump, _store, new MockLoggingService()) { Label = "fam-default" };
        vm.SetEngineState(new EngineState { PeHash = "PEHASH", UEVersion = 504, ModuleBase = "7FF600000000", ProcessCreationTime = "01D9ABCDEF012345" });

        await vm.CaptureCommand.ExecuteAsync(null);

        Assert.Equal("Any", dump.LastNumericFamily);   // "All numeric" -> wire "Any"
    }

    [Theory]
    [InlineData("Floats only", "FloatsOnly")]
    [InlineData("Integers only", "IntegersOnly")]
    public async Task Capture_PassesNumericFamily_ToSnapshotChunk(string label, string wire)
    {
        var dump = new CaptureStub();
        var vm = new SnapshotViewModel(dump, _store, new MockLoggingService())
        {
            SelectedFamily = label,
            Label = "fam-run",
        };
        vm.SetEngineState(new EngineState { PeHash = "PEHASH", UEVersion = 504, ModuleBase = "7FF600000000", ProcessCreationTime = "01D9ABCDEF012345" });

        await vm.CaptureCommand.ExecuteAsync(null);

        Assert.Equal(wire, dump.LastNumericFamily);
    }

    [Fact]
    public async Task Capture_MaxDatasetCap_StopsEarly_AndKeepsPartial()
    {
        var dump = new ManyChunkStub();
        // Decorate the real store so the capture session reports a footprint OVER the
        // 512 MB cap on the very first poll — the cap should stop the producer early.
        var store = new CapDecoratorStore(_store, fakeBytes: 600L * 1024 * 1024);
        var vm = new SnapshotViewModel(dump, store, new MockLoggingService())
        {
            SelectedMaxDataset = "512 MB",
            Label = "capped",
        };
        vm.SetEngineState(new EngineState { PeHash = "PEHASH", UEVersion = 504, ModuleBase = "7FF600000000", ProcessCreationTime = "01D9ABCDEF012345" });

        await vm.CaptureCommand.ExecuteAsync(null);

        Assert.False(vm.IsCapturing);
        // Stopped well before the stub's 10-chunk natural end (the cap fired).
        Assert.True(dump.FetchCount < 10, $"expected early stop, fetched {dump.FetchCount} chunks");
        Assert.Contains("cap", vm.StatusText, StringComparison.OrdinalIgnoreCase);
        // CRITICAL: the partial snapshot is KEPT + finalised (NOT deleted like a cancel).
        var list = await _store.ListSnapshotsAsync(TestContext.Current.CancellationToken);
        var saved = Assert.Single(list);
        Assert.True(saved.ObjectCount > 0, "partial snapshot should hold the captured objects");
    }

    // Streams 1 object per chunk over a 20-object total (10 chunks of scanned=2) so a
    // working max-dataset cap stops it EARLY; a broken cap still terminates at chunk 10.
    private sealed class ManyChunkStub : StubDumpService
    {
        public int FetchCount;

        public override Task<int> BeginSnapshotAsync(string dataType, CancellationToken ct = default)
            => Task.FromResult(20);

        public override Task<SnapshotChunkResult> SnapshotChunkAsync(
            string dataType, bool gameOnly, int offset, int limit,
            bool nativeC = false, bool autoSkipNoise = true,
            string numericFamily = "Any", CancellationToken ct = default)
        {
            FetchCount++;
            var r = new SnapshotChunkResult { Total = 20, Scanned = 2 };
            if (offset < 20)
            {
                var o = new SnapshotCapturedObject
                {
                    Index = offset, Addr = $"0x{offset:X}", Name = $"Obj_{offset}",
                    ClassName = "BP_Thing_C", OuterClassName = "World",
                    Path = $"/Game/Map.Map:PersistentLevel.Obj_{offset}",
                };
                o.Fields.Add(new SnapshotCapturedField { Name = "A", Type = "IntProperty", Hex = "01000000" });
                r.Objects.Add(o);
            }
            else r.Scanned = 0;   // terminal (only reached if the cap never fires)
            return Task.FromResult(r);
        }
    }

    // Wraps a real ISnapshotStore but hands out capture sessions whose CurrentSizeBytes
    // is forced to a fixed value, so a test can drive the max-dataset cap deterministically
    // without writing hundreds of MB. All other calls pass through to the inner store.
    private sealed class CapDecoratorStore : ISnapshotStore
    {
        private readonly ISnapshotStore _inner;
        private readonly long _fakeBytes;
        public CapDecoratorStore(ISnapshotStore inner, long fakeBytes) { _inner = inner; _fakeBytes = fakeBytes; }

        public async Task<ICaptureSession> BeginCaptureSessionAsync(CancellationToken ct = default)
            => new CapSession(await _inner.BeginCaptureSessionAsync(ct), _fakeBytes);

        public string DatabasePath => _inner.DatabasePath;
        public void SetActiveGame(string? p) => _inner.SetActiveGame(p);
        public Task<long> CreateSnapshotAsync(SnapshotMeta m, CancellationToken ct = default) => _inner.CreateSnapshotAsync(m, ct);
        public Task<int> WriteChunkAsync(long s, IReadOnlyList<SnapshotCapturedObject> o, CancellationToken ct = default) => _inner.WriteChunkAsync(s, o, ct);
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
        public Task<IReadOnlyList<PivotClassInfo>> ListPivotClassesAsync(long s, CancellationToken ct = default) => _inner.ListPivotClassesAsync(s, ct);
        public Task<IReadOnlyList<PivotFieldInfo>> ListPivotFieldsAsync(long s, string c, CancellationToken ct = default) => _inner.ListPivotFieldsAsync(s, c, ct);
        public Task<PivotResult> PivotAsync(PivotQuery q, CancellationToken ct = default) => _inner.PivotAsync(q, ct);
        public Task<IReadOnlyList<PivotClassInfo>> ListPivotArrayClassesAsync(long s, CancellationToken ct = default) => _inner.ListPivotArrayClassesAsync(s, ct);
        public Task<IReadOnlyList<PivotArrayFieldInfo>> ListPivotArrayFieldsAsync(long s, string c, CancellationToken ct = default) => _inner.ListPivotArrayFieldsAsync(s, c, ct);
        public Task<IReadOnlyList<PivotFieldInfo>> ListPivotArrayPropsAsync(long s, string c, string af, CancellationToken ct = default) => _inner.ListPivotArrayPropsAsync(s, c, af, ct);
        public Task<PivotResult> PivotArrayAsync(ArrayPivotQuery q, CancellationToken ct = default) => _inner.PivotArrayAsync(q, ct);
        public Task<int> EnforceQuotaAsync(long b, CancellationToken ct = default) => _inner.EnforceQuotaAsync(b, ct);
        public HashSet<string> GetClassDenylist(DenylistScope scope) => _inner.GetClassDenylist(scope);
        public void SetClassDenylist(DenylistScope scope, HashSet<string> classes) => _inner.SetClassDenylist(scope, classes);
    }

    private sealed class CapSession : ICaptureSession
    {
        private readonly ICaptureSession _inner;
        private readonly long _fakeBytes;
        public CapSession(ICaptureSession inner, long fakeBytes) { _inner = inner; _fakeBytes = fakeBytes; }
        public long CurrentSizeBytes() => _fakeBytes;   // force the cap to trip on the first poll
        public int WriteChunk(long s, IReadOnlyList<SnapshotCapturedObject> o, CancellationToken ct = default) => _inner.WriteChunk(s, o, ct);
        public Task CompleteSnapshotAsync(long s, int oc, int fc, bool isUsable = true, CancellationToken ct = default) => _inner.CompleteSnapshotAsync(s, oc, fc, isUsable, ct);
        public ValueTask DisposeAsync() => _inner.DisposeAsync();
    }

    private static SnapshotCapturedObject Obj(int idx, string field, string type, string hex)
    {
        var o = new SnapshotCapturedObject
        {
            Index = idx, Addr = $"0x{idx:X}", Name = $"O_{idx}",
            ClassName = "C", OuterClassName = "W", Path = $"/Game/M.M:L.O_{idx}",
        };
        o.Fields.Add(new SnapshotCapturedField { Name = field, Type = type, Hex = hex });
        return o;
    }

    [Fact]
    public async Task RunDiff_PopulatesChangedRows_AndDefaultsPickers()
    {
        var ct = TestContext.Current.CancellationToken;
        _store.SetActiveGame("G");
        long a = await _store.CreateSnapshotAsync(new SnapshotMeta { Label = "a" }, ct);
        await _store.WriteChunkAsync(a, new[] { Obj(1, "HP", "IntProperty", "64000000") }, ct);  // 100
        await _store.FinalizeSnapshotAsync(a, 1, 1, ct);
        long b = await _store.CreateSnapshotAsync(new SnapshotMeta { Label = "b" }, ct);
        await _store.WriteChunkAsync(b, new[] { Obj(1, "HP", "IntProperty", "5A000000") }, ct);  // 90
        await _store.FinalizeSnapshotAsync(b, 1, 1, ct);

        // Store already scoped to "G" above; a single awaited refresh avoids the
        // SetEngineState fire-and-forget refresh racing this one.
        var vm = new SnapshotViewModel(new CaptureStub(), _store, new MockLoggingService());
        await vm.RefreshCommand.ExecuteAsync(null);

        // Default pickers: A = older, B = newer.
        Assert.Equal(a, vm.DiffA!.Id);
        Assert.Equal(b, vm.DiffB!.Id);
        Assert.True(vm.CanRunDiff);

        await vm.RunDiffCommand.ExecuteAsync(null);
        var row = Assert.Single(vm.DiffRows);
        Assert.Equal("HP", row.PropName);
        Assert.Equal("100", row.OldValue);
        Assert.Equal("90", row.NewValue);
        Assert.Equal(SnapshotDiffDirection.Down, row.Direction);
    }

    [Fact]
    public async Task GroupMode_TwoSnapshots_DefaultsCompareToSecond()
    {
        var ct = TestContext.Current.CancellationToken;
        _store.SetActiveGame("GG2");
        long a = await _store.CreateSnapshotAsync(new SnapshotMeta { Label = "a" }, ct);
        await _store.WriteChunkAsync(a, new[] { Obj(1, "HP", "IntProperty", "64000000") }, ct);
        await _store.FinalizeSnapshotAsync(a, 1, 1, ct);
        long b = await _store.CreateSnapshotAsync(new SnapshotMeta { Label = "b" }, ct);
        await _store.WriteChunkAsync(b, new[] { Obj(1, "HP", "IntProperty", "5A000000") }, ct);
        await _store.FinalizeSnapshotAsync(b, 1, 1, ct);

        var vm = new SnapshotViewModel(new CaptureStub(), _store, new MockLoggingService());
        await vm.RefreshCommand.ExecuteAsync(null);
        Assert.Null(vm.GroupCompareSnapshot);   // empty until Group mode

        vm.IsGroupMode = true;

        Assert.Equal(b, vm.GroupSnapshot!.Id);          // primary = newest
        Assert.Equal(a, vm.GroupCompareSnapshot!.Id);   // compare pre-filled with the OTHER one
        Assert.True(vm.IsGroupCompareMode);             // → Mode B ready in one switch
    }

    [Fact]
    public async Task GroupMode_ThreeSnapshots_LeavesCompareEmpty()
    {
        var ct = TestContext.Current.CancellationToken;
        _store.SetActiveGame("GG3");
        foreach (var lbl in new[] { "a", "b", "c" })
        {
            long id = await _store.CreateSnapshotAsync(new SnapshotMeta { Label = lbl }, ct);
            await _store.WriteChunkAsync(id, new[] { Obj(1, "HP", "IntProperty", "64000000") }, ct);
            await _store.FinalizeSnapshotAsync(id, 1, 1, ct);
        }

        var vm = new SnapshotViewModel(new CaptureStub(), _store, new MockLoggingService());
        await vm.RefreshCommand.ExecuteAsync(null);

        vm.IsGroupMode = true;

        Assert.NotNull(vm.GroupSnapshot);        // primary still defaults to newest
        Assert.Null(vm.GroupCompareSnapshot);    // 3+ → ambiguous, left for the user
    }

    [Fact]
    public async Task Diff_ClientFilter_NarrowsRowsLive()
    {
        var ct = TestContext.Current.CancellationToken;
        _store.SetActiveGame("G2");
        long a = await _store.CreateSnapshotAsync(new SnapshotMeta { Label = "a" }, ct);
        await _store.WriteChunkAsync(a, new[]
        {
            Obj(1, "HP", "IntProperty", "64000000"),    // 100
            Obj(2, "Mana", "IntProperty", "0A000000"),  // 10
        }, ct);
        await _store.FinalizeSnapshotAsync(a, 2, 2, ct);
        long b = await _store.CreateSnapshotAsync(new SnapshotMeta { Label = "b" }, ct);
        await _store.WriteChunkAsync(b, new[]
        {
            Obj(1, "HP", "IntProperty", "5A000000"),    // 90  (down)
            Obj(2, "Mana", "IntProperty", "14000000"),  // 20  (up)
        }, ct);
        await _store.FinalizeSnapshotAsync(b, 2, 2, ct);

        var vm = new SnapshotViewModel(new CaptureStub(), _store, new MockLoggingService());
        await vm.RefreshCommand.ExecuteAsync(null);
        await vm.RunDiffCommand.ExecuteAsync(null);
        Assert.Equal(2, vm.DiffRows.Count);

        // Typing a field filter narrows the grid live (no re-query).
        vm.DiffPropFilter = "HP";
        Assert.Equal("HP", Assert.Single(vm.DiffRows).PropName);

        vm.DiffPropFilter = "";   // cleared -> restored
        Assert.Equal(2, vm.DiffRows.Count);

        // Direction filter is also client-side.
        vm.SelectedDiffDirection = "Increased";
        Assert.Equal("Mana", Assert.Single(vm.DiffRows).PropName);
    }

    [Fact]
    public async Task Diff_GlobalFilter_ValueRange_AndPickerOptions()
    {
        var ct = TestContext.Current.CancellationToken;
        _store.SetActiveGame("G4");
        long a = await _store.CreateSnapshotAsync(new SnapshotMeta { Label = "a" }, ct);
        await _store.WriteChunkAsync(a, new[]
        {
            Obj(1, "HP", "IntProperty", "64000000"),    // 100
            Obj(2, "Mana", "IntProperty", "0A000000"),  // 10
        }, ct);
        await _store.FinalizeSnapshotAsync(a, 2, 2, ct);
        long b = await _store.CreateSnapshotAsync(new SnapshotMeta { Label = "b" }, ct);
        await _store.WriteChunkAsync(b, new[]
        {
            Obj(1, "HP", "IntProperty", "5A000000"),    // 90  (down)
            Obj(2, "Mana", "IntProperty", "14000000"),  // 20  (up)
        }, ct);
        await _store.FinalizeSnapshotAsync(b, 2, 2, ct);

        var vm = new SnapshotViewModel(new CaptureStub(), _store, new MockLoggingService());
        await vm.RefreshCommand.ExecuteAsync(null);
        await vm.RunDiffCommand.ExecuteAsync(null);
        Assert.Equal(2, vm.DiffRows.Count);

        // Picker candidates are the distinct field/class values from the result set.
        Assert.Contains("HP", vm.DiffFieldOptions);
        Assert.Contains("Mana", vm.DiffFieldOptions);
        Assert.Contains("C", vm.DiffClassOptions);

        // Global filter matches across columns: "90" appears only on the HP row's New.
        vm.DiffGlobalFilter = "90";
        Assert.Equal("HP", Assert.Single(vm.DiffRows).PropName);
        vm.DiffGlobalFilter = "";
        Assert.Equal(2, vm.DiffRows.Count);

        // New-value range is button-applied; New<=50 keeps only Mana (20), drops HP (90).
        vm.DiffNewMax = "50";
        Assert.Equal(2, vm.DiffRows.Count);   // not applied until the button
        vm.ApplyDiffRangeCommand.Execute(null);
        Assert.Equal("Mana", Assert.Single(vm.DiffRows).PropName);

        vm.ResetDiffRangeCommand.Execute(null);
        Assert.Equal(2, vm.DiffRows.Count);
        Assert.Equal("", vm.DiffNewMax);
    }

    [Fact]
    public async Task OpenInLiveWalker_RaisesNavigateWithObjectAddress()
    {
        var ct = TestContext.Current.CancellationToken;
        _store.SetActiveGame("G3");
        var vm = new SnapshotViewModel(new CaptureStub(), _store, new MockLoggingService());

        string? navAddr = null;
        vm.NavigateToInstance += a => navAddr = a;

        var row = new SnapshotDiffRow { ObjAddr = "0x7FF600001234", ClassName = "C", PropName = "HP" };
        vm.OpenInLiveWalkerCommand.Execute(row);
        Assert.Equal("0x7FF600001234", navAddr);

        // Null / empty address is a no-op.
        navAddr = null;
        vm.OpenInLiveWalkerCommand.Execute(new SnapshotDiffRow { ObjAddr = "" });
        Assert.Null(navAddr);
    }

    // ---- Snapshot Group Match (S3 UI) ----

    private static SnapshotCapturedObject GMakeObj(int idx, string cls,
        params (string n, string t, int off, string h)[] fields)
    {
        var o = new SnapshotCapturedObject
        {
            Index = idx, Addr = $"0x{idx * 0x1000:X}", Name = $"{cls}_{idx}",
            ClassName = cls, OuterClassName = "World", Path = $"/Game/Map:{cls}_{idx}",
        };
        foreach (var (n, t, off, h) in fields)
            o.Fields.Add(new SnapshotCapturedField { Name = n, Type = t, Offset = off, Hex = h });
        return o;
    }

    private async Task<SnapshotViewModel> NewVmWithSnapshotAsync(CancellationToken ct)
    {
        _store.SetActiveGame("GVM");
        long id = await _store.CreateSnapshotAsync(
            new SnapshotMeta { Label = "s", Scope = "NumericNoByte", PeHash = "GVM", GameSessionId = "GVM-T" }, ct);
        await _store.WriteChunkAsync(id, new[]
        {
            GMakeObj(1, "BP_Player_C",
                ("Str", "IntProperty", 0x20, "18000000"),   // 24
                ("Def", "IntProperty", 0x24, "0A000000"),   // 10
                ("Dex", "IntProperty", 0x28, "0E000000")),  // 14
            GMakeObj(2, "BP_Player_C",
                ("Str", "IntProperty", 0x20, "18000000"),   // 24
                ("Def", "IntProperty", 0x24, "63000000")),  // 99 (no 10 -> won't match)
        }, ct);
        await _store.FinalizeSnapshotAsync(id, 2, 5, ct);

        _lastLog = new MockLoggingService();
        var vm = new SnapshotViewModel(new CaptureStub(), _store, _lastLog);
        vm.SetEngineState(new EngineState { PeHash = "GVM", UEVersion = 504, ModuleBase = "7FF600000000", ProcessCreationTime = "T" });
        // Drain the refresh SetEngineState itself started. Running RefreshCommand here
        // instead did not replace that refresh, it raced it — two concurrent
        // UiCollection.Reset passes over one ObservableCollection (see DrainAsync).
        await DrainAsync(vm.PendingRefresh, "SetEngineState refresh");
        return vm;
    }

    /// <summary>Await the refresh the VM ITSELF started, with a real deadline.
    /// SetEngineState's refresh is fire-and-forget, so a test that followed it with
    /// an awaited RefreshCommand ran BOTH over the same Snapshots collection:
    /// measured 25/4000 in-process iterations saw duplicated rows or a torn null
    /// slot (surfacing as an NRE inside OnIsGroupModeChanged, naming nothing).
    /// Bounded at 10s because a hang is not a test result.</summary>
    private static async Task DrainAsync(Task? pending, string what)
    {
        Assert.True(pending is not null, $"{what}: the VM never started it (seam unassigned)");
        var finished = await Task.WhenAny(
            pending!, Task.Delay(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
        Assert.True(ReferenceEquals(finished, pending), $"{what} did not finish within 10s");
        await pending!;   // observe any fault it captured
    }

    [Fact]
    public async Task GroupMatch_PopulatesCandidates_FromSeededSnapshot()
    {
        var ct = TestContext.Current.CancellationToken;
        var vm = await NewVmWithSnapshotAsync(ct);

        vm.IsGroupMode = true;
        Assert.NotNull(vm.GroupSnapshot);                  // defaulted to the newest snapshot
        vm.GroupInputs[0].ScanType = ValueScanType.Exact; vm.GroupInputs[0].Value = "24";
        vm.GroupInputs[1].ScanType = ValueScanType.Exact; vm.GroupInputs[1].Value = "10";
        Assert.True(vm.CanRunGroupMatch);

        await vm.RunGroupMatchCommand.ExecuteAsync(null);

        // Surface the VM's own explanation on failure. This assert has been seen to flake
        // under the full parallel suite, and a bare "collection was empty" says nothing:
        // GroupStatusText already distinguishes a store error from a cancellation from a
        // genuine zero-row match, so fail with it attached.
        Assert.True(vm.GroupCandidates.Count == 1,
            Diag("expected 1 candidate", vm.GroupCandidates.Count, vm) +
            $" groupStatus='{vm.GroupStatusText}' snapshot={vm.GroupSnapshot?.Id.ToString() ?? "null"}");
        var c = vm.GroupCandidates[0];
        Assert.Equal(1, c.InstanceIndex);                  // only object 1 holds 24 AND 10
        Assert.All(c.Slots, s => Assert.True(s.Locked));
    }

    [Fact]
    public async Task GroupRows_AddRemove_BoundedTwoToFour()
    {
        var ct = TestContext.Current.CancellationToken;
        var vm = await NewVmWithSnapshotAsync(ct);

        Assert.Equal(2, vm.GroupInputs.Count);
        Assert.False(vm.CanRemoveGroupRow);                // at the min
        vm.AddGroupRowCommand.Execute(null);
        vm.AddGroupRowCommand.Execute(null);
        Assert.Equal(4, vm.GroupInputs.Count);
        Assert.False(vm.CanAddGroupRow);                   // at the max
        vm.AddGroupRowCommand.Execute(null);               // no-op past 4
        Assert.Equal(4, vm.GroupInputs.Count);
        vm.RemoveGroupRowCommand.Execute(null);
        Assert.Equal(3, vm.GroupInputs.Count);
    }

    [Fact]
    public async Task GroupMatch_MissingValue_ShowsErrorNoCandidates()
    {
        var ct = TestContext.Current.CancellationToken;
        var vm = await NewVmWithSnapshotAsync(ct);

        vm.GroupInputs[0].Value = "24";                    // slot 2 left blank -> store validation error
        await vm.RunGroupMatchCommand.ExecuteAsync(null);

        Assert.Empty(vm.GroupCandidates);
        Assert.False(string.IsNullOrEmpty(vm.GroupStatusText));
    }

    private async Task<SnapshotViewModel> NewVmWith2SnapshotsAsync(CancellationToken ct)
    {
        var vm = await NewVmWithSnapshotAsync(ct);
        long id2 = await _store.CreateSnapshotAsync(
            new SnapshotMeta { Label = "s2", Scope = "NumericNoByte", PeHash = "GVM", GameSessionId = "GVM-T" }, ct);
        await _store.WriteChunkAsync(id2, new[] { GMakeObj(9, "BP_Player_C", ("Str", "IntProperty", 0x20, "18000000")) }, ct);
        await _store.FinalizeSnapshotAsync(id2, 1, 1, ct);
        await vm.RefreshCommand.ExecuteAsync(null);
        return vm;
    }

    [Fact]
    public async Task GroupCompareSnapshot_PreservedAcrossRefresh()
    {
        var ct = TestContext.Current.CancellationToken;
        var vm = await NewVmWith2SnapshotsAsync(ct);
        Assert.True(vm.Snapshots.Count >= 2);

        vm.GroupSnapshot = vm.Snapshots[0];
        vm.GroupCompareSnapshot = vm.Snapshots[1];   // Mode B
        Assert.True(vm.IsGroupCompareMode);
        long compareId = vm.GroupCompareSnapshot!.Id;

        await vm.RefreshCommand.ExecuteAsync(null);  // e.g. after a capture completes

        Assert.NotNull(vm.GroupCompareSnapshot);     // preserved (was: silently dropped -> reverts to Mode A)
        Assert.Equal(compareId, vm.GroupCompareSnapshot!.Id);
        Assert.True(vm.IsGroupCompareMode);
    }

    [Fact]
    public async Task GroupRowActions_DisabledForCrossSessionSnapshot()
    {
        var ct = TestContext.Current.CancellationToken;
        _store.SetActiveGame("GVM");
        long id = await _store.CreateSnapshotAsync(
            new SnapshotMeta { Label = "old-session", Scope = "NumericNoByte", PeHash = "GVM", GameSessionId = "GVM-OLD" }, ct);
        await _store.WriteChunkAsync(id, new[] { GMakeObj(1, "BP_Player_C", ("Str", "IntProperty", 0x20, "18000000")) }, ct);
        await _store.FinalizeSnapshotAsync(id, 1, 1, ct);

        var vm = new SnapshotViewModel(new CaptureStub(), _store, new MockLoggingService());
        // Current live session is GVM-NEW; the snapshot is from GVM-OLD (a previous run).
        vm.SetEngineState(new EngineState { PeHash = "GVM", UEVersion = 504, ModuleBase = "7FF600000000", ProcessCreationTime = "NEW" });
        await DrainAsync(vm.PendingRefresh, "SetEngineState refresh");
        vm.GroupSnapshot = vm.Snapshots[0];

        // Cross-session: every per-slot handoff (Live / Copy / Locate-GWorld / Locate-GameEngine)
        // must be disabled — its captured obj_addr is stale in the running game.
        Assert.False(vm.CanUseGroupRowActions);
        Assert.False(vm.CanLocateGroupRowInGWorld);
    }

    [Fact]
    public async Task IsGroupCompareMode_RaisedWhenPrimaryChanges()
    {
        var ct = TestContext.Current.CancellationToken;
        var vm = await NewVmWith2SnapshotsAsync(ct);
        vm.GroupSnapshot = vm.Snapshots[0];
        vm.GroupCompareSnapshot = vm.Snapshots[1];
        Assert.True(vm.IsGroupCompareMode);

        bool raised = false;
        vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(vm.IsGroupCompareMode)) raised = true; };
        vm.GroupSnapshot = vm.Snapshots[1];          // primary now equals compare -> collapses to Mode A

        Assert.True(raised);                          // OnGroupSnapshotChanged re-raises it
        Assert.False(vm.IsGroupCompareMode);
    }

    [Fact]
    public async Task Capture_BlockedWhenDiskBelowGuard()
    {
        var ct = TestContext.Current.CancellationToken;
        var dump = new CaptureStub();
        // 1 GB free on a 1 TB drive → the 10% / 50 GB guard needs 50 GB → refuse.
        var platform = new DiskStubPlatformService(_tempDir)
        {
            FreeBytes  = 1L * 1024 * 1024 * 1024,
            TotalBytes = 1024L * 1024 * 1024 * 1024,
        };
        var vm = new SnapshotViewModel(dump, _store, new MockLoggingService(), gate: null, platform: platform)
        {
            SelectedScope = "NumericNoByte",
        };
        vm.SetEngineState(new EngineState { PeHash = "PEHASH", UEVersion = 504, ModuleBase = "7FF600000000", ProcessCreationTime = "01D9ABCDEF012345" });

        await vm.CaptureCommand.ExecuteAsync(null);

        Assert.False(vm.IsCapturing);
        Assert.Empty(await _store.ListSnapshotsAsync(ct));   // guard refused before any write
        Assert.Null(dump.LastDataType);                      // BeginSnapshot never reached
        Assert.Contains("disk", vm.StatusText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Capture_AllowedWhenDiskAboveGuard()
    {
        var ct = TestContext.Current.CancellationToken;
        var dump = new CaptureStub();
        // Plenty of free space → the guard passes and the capture proceeds normally.
        var platform = new DiskStubPlatformService(_tempDir)
        {
            FreeBytes  = 500L * 1024 * 1024 * 1024,
            TotalBytes = 1024L * 1024 * 1024 * 1024,
        };
        var vm = new SnapshotViewModel(dump, _store, new MockLoggingService(), gate: null, platform: platform)
        {
            SelectedScope = "NumericNoByte", Label = "ok",
        };
        vm.SetEngineState(new EngineState { PeHash = "PEHASH", UEVersion = 504, ModuleBase = "7FF600000000", ProcessCreationTime = "01D9ABCDEF012345" });

        await vm.CaptureCommand.ExecuteAsync(null);

        Assert.Equal("NumericNoByte", dump.LastDataType);    // BeginSnapshot reached
        Assert.Single(await _store.ListSnapshotsAsync(ct));  // snapshot persisted
    }

    // Platform double whose free/total disk space is configurable, for the free-disk-space
    // guard tests. Only the disk methods matter here; the rest are minimal no-ops.
    private sealed class DiskStubPlatformService : IPlatformService
    {
        private readonly string _appData;
        public long FreeBytes { get; set; } = long.MaxValue;
        public long TotalBytes { get; set; }
        public DiskStubPlatformService(string appData) => _appData = appData;

        public long GetFreeDiskSpaceBytes(string path) => FreeBytes;
        public long GetTotalDiskSpaceBytes(string path) => TotalBytes;

        public bool TryAcquireSingleInstance() => true;
        public void ReleaseSingleInstance() { }
        public string GetAppDataPath() => _appData;
        public string GetLogDirectoryPath() => Path.Combine(_appData, "Logs");
        public Task<bool> CopyToClipboardAsync(string text) => Task.FromResult(true);
        public Task RevealInExplorerAsync(string path) => Task.CompletedTask;
        public string GetMachineName() => "TEST-MACHINE";
        public void CloseImeForWindow(IntPtr windowHandle) { }
        public Task<string?> ShowSaveFileDialogAsync(string defaultFileName, string filterName, string filterExtension)
            => Task.FromResult<string?>(null);
    }
}
