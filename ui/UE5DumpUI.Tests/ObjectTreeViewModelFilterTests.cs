using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UE5DumpUI.Core;
using UE5DumpUI.Models;
using UE5DumpUI.ViewModels;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// VM-level tests for the Object Tree "Instances only" toggle. The critical guarantee
/// (the concern that motivated this feature): the toggle filters the ENTIRE loaded pool
/// (<c>_allNodes</c>), NOT just the display-capped page — so a global keyword search
/// over instances can't miss matches sitting beyond the visible cap.
/// </summary>
public class ObjectTreeViewModelFilterTests
{
    /// <summary>Returns the whole node list as a single page on offset 0 (mirrors the
    /// DLL contract: subsequent offsets scan nothing), so <c>LoadAsync</c> pulls the
    /// full pool into <c>_allNodes</c> in one round-trip.</summary>
    private sealed class WholePoolDump : StubDumpService
    {
        private readonly List<UObjectNode> _objects;
        public WholePoolDump(List<UObjectNode> objects) => _objects = objects;

        public override Task<ObjectListResult> GetObjectListAsync(
            int offset, int limit, CancellationToken ct = default, bool includePath = false)
        {
            if (offset > 0)
                return Task.FromResult(new ObjectListResult { Total = _objects.Count, Scanned = 0, Objects = new() });
            return Task.FromResult(new ObjectListResult
            {
                Total = _objects.Count,
                Scanned = _objects.Count,
                Objects = _objects,
            });
        }
    }

    private static async Task<ObjectTreeViewModel> LoadVmAsync(List<UObjectNode> nodes)
    {
        var vm = new ObjectTreeViewModel(
            new WholePoolDump(nodes),
            new MockLoggingService(),
            new MockPlatformService(Path.GetTempPath()));
        await vm.LoadCommand.ExecuteAsync(null);
        return vm;
    }

    private static UObjectNode Node(string className, string name) =>
        new() { ClassName = className, Name = name, Address = "0x140000000" };

    private static string N0(int n) => n.ToString("N0", CultureInfo.CurrentCulture);

    [Fact]
    public async Task InstancesOnly_FiltersWholePool_BeyondDisplayCap()
    {
        // 2000 reflection metas FIRST, then 6000 live instances — 8000 total, well over
        // both the page size (2000) and the display cap (5000). If the filter only
        // scanned a page/the display cap it would find at most ~3000 instances; the whole
        // pool holds 6000. The instances deliberately sit AFTER the metas so a page-only
        // bug would under-count them.
        var nodes = new List<UObjectNode>();
        string[] metas = { "Class", "Function", "ScriptStruct", "Package", "Enum", "IntProperty" };
        for (int i = 0; i < 2000; i++) nodes.Add(Node(metas[i % metas.Length], $"Meta_{i}"));
        for (int i = 0; i < 6000; i++) nodes.Add(Node("BP_Enemy_C", $"Enemy_{i}"));

        var vm = await LoadVmAsync(nodes);

        // Baseline: unfiltered pool is 8000, display capped at 5000.
        Assert.Equal(8000, vm.ObjectCount);
        Assert.Equal(Constants.ObjectTreeMaxDisplay, vm.FilteredNodes.Count);
        Assert.Contains(N0(8000), vm.DisplayCount);

        vm.InstancesOnly = true;

        // The match COUNT must reflect all 6000 instances across the whole pool — proving
        // the scan wasn't limited to a page or to the 5000 display cap.
        Assert.Contains(N0(6000), vm.DisplayCount);
        // Display stays capped at 5000, and every shown row is a live instance (no metas).
        Assert.Equal(Constants.ObjectTreeMaxDisplay, vm.FilteredNodes.Count);
        Assert.All(vm.FilteredNodes, n => Assert.Equal("BP_Enemy_C", n.ClassName));
    }

    [Fact]
    public async Task InstancesOnly_HidesReflectionMeta_KeepsInstances()
    {
        var nodes = new List<UObjectNode>
        {
            Node("Class", "BP_Enemy_C"),                 // a UClass object
            Node("Function", "BP_Enemy_C:Attack"),       // a UFunction object
            Node("ScriptStruct", "FVector"),             // a UScriptStruct object
            Node("Package", "/Game/Enemy"),              // a UPackage object
            Node("ObjectProperty", "TargetActor"),       // UE4 property descriptor
            Node("BP_Enemy_C", "Enemy_0"),               // a live instance
            Node("Character", "Hero"),                   // a live instance
        };

        var vm = await LoadVmAsync(nodes);
        vm.InstancesOnly = true;

        Assert.Equal(2, vm.FilteredNodes.Count);
        Assert.Contains(vm.FilteredNodes, n => n.ClassName == "BP_Enemy_C");
        Assert.Contains(vm.FilteredNodes, n => n.ClassName == "Character");
        Assert.DoesNotContain(vm.FilteredNodes,
            n => ReflectionMetaClassName(n.ClassName));
    }

    [Fact]
    public async Task InstancesOnly_CombinesWithTextFilter_AsAnd()
    {
        var nodes = new List<UObjectNode>
        {
            Node("Class", "BP_Enemy_C"),        // matches "Enemy" but is a meta → excluded
            Node("Function", "SpawnEnemy"),     // matches "Enemy" but is a meta → excluded
            Node("BP_Enemy_C", "Enemy_Boss"),   // instance + matches "Enemy" → kept
            Node("BP_Ally_C", "Ally_0"),        // instance but no "Enemy" → excluded
        };

        var vm = await LoadVmAsync(nodes);
        // Set the text filter FIRST (its own ApplyFilter is debounced and won't have run
        // synchronously), THEN flip InstancesOnly — that change fires ApplyFilter
        // synchronously and it reads the current FilterText, so both predicates apply in
        // one pass. Order matters: InstancesOnly is the deterministic trigger here.
        vm.FilterText = "Enemy";
        vm.InstancesOnly = true;

        Assert.Single(vm.FilteredNodes);
        Assert.Equal("Enemy_Boss", vm.FilteredNodes[0].Name);
    }

    private static bool ReflectionMetaClassName(string className) =>
        UE5DumpUI.Helpers.ReflectionMetaClassifier.IsReflectionMeta(className);

    // ── Top Search box → server-side global keyword search (Phase 2) ────────────────

    /// <summary>Records the args the VM passes to the server search and returns a
    /// caller-controlled result (so truncation / instances-only wiring can be asserted).</summary>
    private sealed class SearchRecordingDump : StubDumpService
    {
        public string? LastQuery;
        public int LastLimit;
        public bool LastInstancesOnly;
        public ObjectListResult Result = new();

        public override Task<ObjectListResult> SearchObjectsAsync(
            string query, int limit = 200, bool instancesOnly = false, CancellationToken ct = default)
        {
            LastQuery = query;
            LastLimit = limit;
            LastInstancesOnly = instancesOnly;
            return Task.FromResult(Result);
        }
    }

    private static ObjectTreeViewModel NewVm(StubDumpService dump) =>
        new(dump, new MockLoggingService(), new MockPlatformService(Path.GetTempPath()));

    [Fact]
    public async Task SearchAsync_SendsInstancesOnlyAndSearchCap_ToServer()
    {
        var dump = new SearchRecordingDump
        {
            Result = new ObjectListResult { Total = 3, Objects = new() { Node("BP_Enemy_C", "Enemy_0") } },
        };
        var vm = NewVm(dump);
        vm.InstancesOnly = true;
        vm.SearchText = "Enemy Boss";

        await vm.SearchCommand.ExecuteAsync(null);

        // The top Search now forwards the Instances-only toggle and the higher search cap
        // (not the old 2000 page size), so the server does the same space=AND + gate.
        Assert.Equal("Enemy Boss", dump.LastQuery);
        Assert.True(dump.LastInstancesOnly);
        Assert.Equal(Constants.ObjectTreeSearchCap, dump.LastLimit);
    }

    [Fact]
    public async Task SearchAsync_Truncated_StatusFlagsCapped()
    {
        var dump = new SearchRecordingDump
        {
            Result = new ObjectListResult { Total = Constants.ObjectTreeSearchCap, Truncated = true },
        };
        var vm = NewVm(dump);
        vm.SearchText = "a";

        await vm.SearchCommand.ExecuteAsync(null);

        // A hit cap is surfaced (not silently truncated) so the user knows to narrow or Reload.
        Assert.Contains("capped", vm.StatusText);
    }

    [Fact]
    public async Task SearchAsync_NotTruncated_StatusIsPlainCount()
    {
        var dump = new SearchRecordingDump
        {
            Result = new ObjectListResult { Total = 12, Truncated = false },
        };
        var vm = NewVm(dump);
        vm.SearchText = "hero";

        await vm.SearchCommand.ExecuteAsync(null);

        Assert.DoesNotContain("capped", vm.StatusText);
        Assert.Contains("12", vm.StatusText);
    }

    // [OBJTREE-SEARCH-TIP] The top search box suggests a FIXED list of common UE class
    // names (SearchSuggestions); the remembered-keyword memory belongs to the bottom filter
    // box. The tooltip promised "recent searches", a feature this box does not have.
    [Fact]
    public void SearchFieldTooltip_describes_the_fixed_suggestion_list_the_box_binds()
    {
        var panel = File.ReadAllText(NumericInputCoercionTests.RepoFile("ui/UE5DumpUI/Views/ObjectTreePanel.axaml"));
        Assert.Contains("ItemsSource=\"{Binding SearchSuggestions}\"", panel, StringComparison.Ordinal);

        var tip = EnString("str.Tip.ObjectTree.SearchField");
        Assert.DoesNotContain("recent", tip, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("common UE class names", tip, StringComparison.Ordinal);
    }

    private static string EnString(string key)
    {
        var axaml = File.ReadAllText(NumericInputCoercionTests.RepoFile("ui/UE5DumpUI/Resources/Strings/en.axaml"));
        var open = $"x:Key=\"{key}\">";
        int start = axaml.IndexOf(open, StringComparison.Ordinal);
        Assert.True(start >= 0, $"{key} missing from en.axaml");
        start += open.Length;
        return axaml[start..axaml.IndexOf("</sys:String>", start, StringComparison.Ordinal)];
    }
}
