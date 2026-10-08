using System.IO;
using UE5DumpUI;
using UE5DumpUI.Models;
using UE5DumpUI.Services;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// Locks the UiOptionsStore persistence contract: defaults when no file, round-trips
/// every sub-object through a fresh instance, falls back to defaults on a corrupt
/// file, and — the key regression — a default-TRUE option turned OFF must SURVIVE a
/// reload (the store must NOT use JsonIgnoreCondition.WhenWritingDefault, which would
/// silently drop the off-value and revert it to on). Reuses
/// <see cref="MockPlatformService"/> from AobUsageServiceTests.
/// </summary>
public class UiOptionsStoreTests : IDisposable
{
    private readonly string _tempDir;
    private readonly MockPlatformService _platform;

    public UiOptionsStoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"UE5DumpUiOptTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _platform = new MockPlatformService(_tempDir);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); }
        catch { /* best effort */ }
    }

    [Fact]
    public void Load_NoFile_ReturnsDefaults_AndDoesNotCreateFile()
    {
        var store = new UiOptionsStore(_platform);
        var o = store.Load();

        // Representative defaults (must match the ViewModel [ObservableProperty] initializers).
        Assert.True(o.LiveWalker.DedupSharedObjects);
        Assert.True(o.LiveWalker.ExcludeSystemComponents);
        Assert.True(o.ValueSearch.GameOnly);
        Assert.True(o.ValueSearch.ParallelScan);
        Assert.Equal(50000, o.ValueSearch.MaxResults);
        Assert.Equal(ValueScanDataType.Int32, o.ValueSearch.SelectedDataType);
        Assert.Equal(7, o.Main.ArrayLimitExponent);
        Assert.Equal(100.0, o.Teleport.ZOffset);
        Assert.Equal(1.0, o.Teleport.WorldTimeDilation);   // must match VM _worldTimeDilation default
        Assert.Equal(1.0, o.Teleport.PawnTimeDilation);    // must match VM _pawnTimeDilation default
        Assert.Equal(ProxyType.Version, o.ProxyDeploy.SelectedProxyType);
        Assert.Equal(9, o.LiveFuncs.FetchLimitExponent);   // must match VM _fetchLimitExponent default (2^9 = 512)
        Assert.Equal(0, o.LiveFuncs.MinCallsExponent);     // must match VM _minCallsExponent default (2^0 = 1)
        Assert.False(o.Main.DumpAllObjectIndex);           // D4.1: the object index is opt-in, OFF by default
        Assert.False(o.LiveFuncs.HidePerFrame);            // [LIVEFUNCS-HIDE-PERFRAME] opt-in, OFF by default

        Assert.False(File.Exists(store.FilePath)); // load must not create the file
    }

    [Fact]
    public void FilePath_IsUnderAppDataWithExpectedName()
    {
        var store = new UiOptionsStore(_platform);
        Assert.Equal(
            Path.Combine(_tempDir, Constants.LogFolderName, Constants.UiOptionsFile),
            store.FilePath);
    }

    [Fact]
    public void RoundTrip_PreservesEveryKind_AcrossFreshInstance()
    {
        var store = new UiOptionsStore(_platform);
        var o = new UiOptionsSettings();

        // Change a value of every kind across several panels.
        o.Main.ArrayLimitExponent = 10;
        o.Main.SelectedAddressFormatIndex = 2;
        o.LiveWalker.DescShowType = true;
        o.LiveWalker.UseAobSymbol = true;
        o.LiveWalker.GWorldLocateDepth = 9;
        o.ValueSearch.SelectedDataType = ValueScanDataType.Float;
        o.ValueSearch.SelectedScanType = ValueScanType.Bigger;
        o.ValueSearch.MaxResults = 12345;
        o.ValueSearch.RoundingMode = FloatRoundMode.Trunc;
        o.Snapshot.SelectedFamily = "Integers only";
        o.Snapshot.SelectedScope = "NumericAll";
        o.Snapshot.RoundingMode = FloatRoundMode.Ceil;
        o.Teleport.ZOffset = 250.5;
        o.Teleport.TraceChannel = 3;
        o.Teleport.WorldTimeDilation = 0.5;
        o.Teleport.PawnTimeDilation = 2.0;
        o.Spc.SelectedJoinMode = "Loose";
        o.Spc.RoundingMode = FloatRoundMode.Trunc;
        o.Pivot.SelectedSource = "DataTable";
        o.ProxyDeploy.SelectedProxyType = ProxyType.Dxgi;
        o.LiveFuncs.FetchLimitExponent = 13;
        o.LiveFuncs.MinCallsExponent = 3;
        o.Main.DumpAllObjectIndex = true;
        o.LiveFuncs.HidePerFrame = true;

        store.Save(o);
        Assert.True(File.Exists(store.FilePath));

        var r = new UiOptionsStore(_platform).Load();   // simulate restart
        Assert.Equal(10, r.Main.ArrayLimitExponent);
        Assert.Equal(2, r.Main.SelectedAddressFormatIndex);
        Assert.True(r.LiveWalker.DescShowType);
        Assert.True(r.LiveWalker.UseAobSymbol);
        Assert.Equal(9, r.LiveWalker.GWorldLocateDepth);
        Assert.Equal(ValueScanDataType.Float, r.ValueSearch.SelectedDataType);
        Assert.Equal(ValueScanType.Bigger, r.ValueSearch.SelectedScanType);
        Assert.Equal(12345, r.ValueSearch.MaxResults);
        Assert.Equal(FloatRoundMode.Trunc, r.ValueSearch.RoundingMode);
        Assert.Equal("Integers only", r.Snapshot.SelectedFamily);
        Assert.Equal("NumericAll", r.Snapshot.SelectedScope);
        Assert.Equal(FloatRoundMode.Ceil, r.Snapshot.RoundingMode);
        Assert.Equal(250.5, r.Teleport.ZOffset);
        Assert.Equal(3, r.Teleport.TraceChannel);
        Assert.Equal(0.5, r.Teleport.WorldTimeDilation);
        Assert.Equal(2.0, r.Teleport.PawnTimeDilation);
        Assert.Equal("Loose", r.Spc.SelectedJoinMode);
        Assert.Equal(FloatRoundMode.Trunc, r.Spc.RoundingMode);
        Assert.Equal("DataTable", r.Pivot.SelectedSource);
        Assert.Equal(ProxyType.Dxgi, r.ProxyDeploy.SelectedProxyType);
        Assert.Equal(13, r.LiveFuncs.FetchLimitExponent);
        Assert.Equal(3, r.LiveFuncs.MinCallsExponent);
        Assert.True(r.Main.DumpAllObjectIndex);
        Assert.True(r.LiveFuncs.HidePerFrame);
    }

    [Fact]
    public void DefaultTrueOption_TurnedOff_SurvivesReload()
    {
        // REGRESSION (the WhenWritingDefault trap): turning OFF a default-true option
        // means storing the bool type-default (false). With WhenWritingDefault that
        // would be omitted and revert to true on reload. The store must persist it.
        var store = new UiOptionsStore(_platform);
        var o = new UiOptionsSettings();
        o.LiveWalker.DedupSharedObjects = false;
        o.LiveWalker.ExcludeSystemComponents = false;
        o.ValueSearch.GameOnly = false;
        o.ValueSearch.ParallelScan = false;
        o.Snapshot.AutoSkipNoise = false;
        o.InterestingFuncs.GameOnly = false;
        store.Save(o);

        var r = new UiOptionsStore(_platform).Load();
        Assert.False(r.LiveWalker.DedupSharedObjects);
        Assert.False(r.LiveWalker.ExcludeSystemComponents);
        Assert.False(r.ValueSearch.GameOnly);
        Assert.False(r.ValueSearch.ParallelScan);
        Assert.False(r.Snapshot.AutoSkipNoise);
        Assert.False(r.InterestingFuncs.GameOnly);
    }

    [Fact]
    public void Load_CorruptFile_ReturnsDefaults()
    {
        var store = new UiOptionsStore(_platform);
        Directory.CreateDirectory(Path.GetDirectoryName(store.FilePath)!);
        File.WriteAllText(store.FilePath, "{ this is not valid json ]]");

        var o = store.Load();
        Assert.True(o.ValueSearch.GameOnly);             // fell back to defaults
        Assert.Equal(50000, o.ValueSearch.MaxResults);
    }

    [Fact]
    public void The_object_trees_collapsed_state_round_trips_and_an_older_file_opens_it()
    {
        // [OT-COLLAPSE-PERSIST] The store writes what the settings root reaches, through the source-generated context.
        var store = new UiOptionsStore(_platform);
        var o = new UiOptionsSettings();
        o.Main.ObjectTreeCollapsed = true;
        store.Save(o);
        Assert.True(new UiOptionsStore(_platform).Load().Main.ObjectTreeCollapsed);

        // A file written before the option existed has no such key: the tree opens, as it always did.
        File.WriteAllText(store.FilePath, "{\"schemaVersion\":1,\"main\":{\"selectedAddressFormatIndex\":1}}");
        var older = new UiOptionsStore(_platform).Load();
        Assert.False(older.Main.ObjectTreeCollapsed);
        Assert.Equal(1, older.Main.SelectedAddressFormatIndex);
    }

    [Fact]
    public void The_object_trees_collapsed_state_is_tracked_applied_and_saved_by_the_main_window()
    {
        // MainWindowViewModel cannot be built in a unit test; pin its three persistence sites by source, as
        // LiveFuncsViewModelTests.HidePerFrame_PersistsThroughTheMainWindow does.
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "build.ps1"))) root = root.Parent;
        Assert.NotNull(root);
        var src = File.ReadAllText(Path.Combine(root!.FullName, "ui", "UE5DumpUI", "ViewModels", "MainWindowViewModel.cs"));
        Assert.Contains("nameof(ObjectTreeViewModel.IsCollapsed)", src);
        Assert.Contains("Track(ObjectTree, ObjectTreePersist);", src);
        Assert.Contains("ObjectTree.IsCollapsed = o.Main.ObjectTreeCollapsed;", src);
        Assert.Contains("o.Main.ObjectTreeCollapsed = ObjectTree.IsCollapsed;", src);
    }

    [Fact]
    public void Load_DeletesStaleTempFile()
    {
        var store = new UiOptionsStore(_platform);
        Directory.CreateDirectory(Path.GetDirectoryName(store.FilePath)!);
        var temp = store.FilePath + ".tmp";
        File.WriteAllText(temp, "orphaned");

        store.Load();
        Assert.False(File.Exists(temp));   // cleaned up
    }
}
