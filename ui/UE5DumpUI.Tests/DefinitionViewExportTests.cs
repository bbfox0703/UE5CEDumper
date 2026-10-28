using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using UE5DumpUI.Helpers;
using UE5DumpUI.Models;
using UE5DumpUI.Services;
using UE5DumpUI.ViewModels;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// [CEXML-DEFINITION-VIEW-AS-DATA] A pointer whose target is a TYPE -- a DataTable's RowStruct, every ClassProperty /
/// TSubclassOf -- walks as a DEFINITION view: <c>Ubel::WalkInstance</c> answers with the type's own member layout and
/// reads no values, because those offsets describe instances of the type, not the UScriptStruct / UClass object. The
/// exporters laid that layout out as data under the pointer, so CE read the type object's own memory at the row
/// offsets (measured on Extinction's DT_LevelSelectData: <c>RowStruct (28) > World (20) > WeakPtr (0)</c> =
/// <c>0000019F684E02C0</c>, its AssetPath garbage), and Live Walker / Instance Finder stamped addresses on a definition's
/// rows, so edit, freeze, +CE and Copy CE XML targeted it too.
///
/// <para>Negative controls: drop the IsDefinition check in a resolver and the walk-result facts fail; drop the
/// metaclass pre-filter and the older-DLL facts fail (an older DLL's lean walk carries no flag); stamp FieldAddress on a
/// definition view again and the Live Walker / Instance Finder facts fail.</para>
/// </summary>
public class DefinitionViewExportTests
{
    private const string TypeAddr = "0xDEF00000";

    /// <summary>The definition view the DLL returns for BlinkDataLevel: its member layout, no values.</summary>
    private static InstanceWalkResult DefinitionWalk(bool flagged = true) => new()
    {
        Address = TypeAddr, Name = "BlinkDataLevel", ClassName = "ScriptStruct", IsDefinition = flagged,
        Fields = new List<LiveFieldValue>
        {
            new() { Name = "World", TypeName = "AssetObjectProperty", Offset = 0x20, Size = 0x20 },
            new() { Name = "PreviewImage", TypeName = "AssetObjectProperty", Offset = 0x58, Size = 0x20 },
        },
    };

    private sealed class CountingDump : StubDumpService
    {
        public List<string> Walked { get; } = new();
        public override Task<InstanceWalkResult> WalkInstanceAsync(string addr, string? classAddr = null,
            int arrayLimit = 64, int previewLimit = 2, bool fillGaps = false, bool lean = false,
            CancellationToken ct = default)
        {
            Walked.Add(addr);
            return base.WalkInstanceAsync(addr, classAddr, arrayLimit, previewLimit, fillGaps, lean, ct);
        }

        public override async Task<IReadOnlyList<InstanceWalkResult>> WalkInstanceBatchAsync(
            IReadOnlyList<(string Addr, string? ClassAddr)> items, int arrayLimit = 64, int previewLimit = 2,
            bool fillGaps = false, bool lean = false, CancellationToken ct = default)
        {
            var list = new List<InstanceWalkResult>();
            foreach (var (addr, cls) in items)
                list.Add(await WalkInstanceAsync(addr, cls, arrayLimit, previewLimit, fillGaps, lean, ct));
            return list;
        }
    }

    private static LiveFieldValue PointerTo(string ptrClassName, string name = "RowStruct", string type = "ObjectProperty")
        => new()
        {
            Name = name, TypeName = type, Offset = 0x28, Size = 8,
            PtrAddress = TypeAddr, PtrName = "BlinkDataLevel", PtrClassName = ptrClassName, PtrClassAddr = "0xC1A55",
        };

    private static async Task<Dictionary<string, List<LiveFieldValue>>> UnifiedResolveAsync(
        StubDumpService dump, LiveFieldValue field, bool lean = true)
    {
        var structs = new Dictionary<string, List<LiveFieldValue>>(StringComparer.Ordinal);
        var instances = new Dictionary<string, List<LiveFieldValue>>(StringComparer.Ordinal);
        await CeXmlExportService.ResolveDrilldownAsync(dump, new List<LiveFieldValue> { field }, structs, instances,
            depth: 2, lean: lean, ct: TestContext.Current.CancellationToken);
        return instances;
    }

    // ══ the resolvers: a definition walk is not drilled ══

    [Fact]
    public async Task UnifiedResolver_ADefinitionWalk_IsNotDrilled()
    {
        var dump = new CountingDump();
        dump.RegisterStruct(TypeAddr, DefinitionWalk());

        // No class name on the pointing field: the walk's own flag is all there is to go on.
        var instances = await UnifiedResolveAsync(dump, PointerTo(""), lean: false);

        Assert.DoesNotContain(TypeAddr, instances.Keys);
    }

    [Theory]
    [InlineData("ScriptStruct")]
    [InlineData("Class")]
    [InlineData("BlueprintGeneratedClass")]
    [InlineData("WidgetBlueprintGeneratedClass")]
    public async Task UnifiedResolver_APointerToADefinitionMetaclass_IsNotEvenWalked(string metaclass)
    {
        // An older DLL's LEAN walk drops is_definition, so the walk alone cannot tell; the pointing field's class name can.
        var dump = new CountingDump();
        dump.RegisterStruct(TypeAddr, DefinitionWalk(flagged: false));

        var instances = await UnifiedResolveAsync(dump, PointerTo(metaclass, type: "ClassProperty"));

        Assert.DoesNotContain(TypeAddr, instances.Keys);
        Assert.DoesNotContain(TypeAddr, dump.Walked);
    }

    [Theory]
    [InlineData("Actor")]
    [InlineData("Function")]   // a UFunction walks as an instance of its metaclass's reflected layout: honest data
    public async Task UnifiedResolver_AnInstancePointer_IsStillDrilled(string ptrClass)
    {
        var dump = new CountingDump();
        dump.RegisterStruct(TypeAddr, new InstanceWalkResult
        {
            Address = TypeAddr, ClassName = ptrClass,
            Fields = new List<LiveFieldValue> { new() { Name = "Health", TypeName = "FloatProperty", Offset = 0x100, Size = 4 } },
        });

        var instances = await UnifiedResolveAsync(dump, PointerTo(ptrClass, name: "Owner"));

        Assert.Contains(TypeAddr, instances.Keys);
    }

    [Fact]
    public async Task UnifiedResolver_ClassArrayElements_ToBlueprintClasses_AreNotWalked()
    {
        var dump = new CountingDump();
        dump.RegisterStruct(TypeAddr, DefinitionWalk(flagged: false));
        var array = new LiveFieldValue
        {
            Name = "AbilityClasses", TypeName = "ArrayProperty", Offset = 0x40, Size = 0x10,
            ArrayInnerType = "ClassProperty", ArrayCount = 1, ArrayElemSize = 8, ArrayDataAddr = "0x50000",
            ArrayElements = new List<ArrayElementValue>
            {
                new() { Index = 0, PtrAddress = TypeAddr, PtrName = "GA_Dash_C", PtrClassName = "BlueprintGeneratedClass" },
            },
        };

        var instances = await UnifiedResolveAsync(dump, array);

        Assert.DoesNotContain(TypeAddr, instances.Keys);
        Assert.DoesNotContain(TypeAddr, dump.Walked);
    }

    [Fact]
    public async Task BatchResolver_ADefinitionWalk_IsNotDrilled()
    {
        var dump = new CountingDump();
        dump.RegisterStruct(TypeAddr, DefinitionWalk());

        var resolved = await CeXmlExportService.ResolvePointerInstancesAsync(
            dump, new List<LiveFieldValue> { PointerTo("") }, depth: 1);

        Assert.DoesNotContain(TypeAddr, resolved.Keys);
    }

    [Fact]
    public async Task BatchResolver_APointerToADefinitionMetaclass_IsNotEvenWalked()
    {
        var dump = new CountingDump();
        dump.RegisterStruct(TypeAddr, DefinitionWalk(flagged: false));

        var resolved = await CeXmlExportService.ResolvePointerInstancesAsync(
            dump, new List<LiveFieldValue> { PointerTo("ScriptStruct") }, depth: 1, lean: true);

        Assert.DoesNotContain(TypeAddr, resolved.Keys);
        Assert.DoesNotContain(TypeAddr, dump.Walked);
    }

    // ══ CSX: no child structure for a type ══

    [Fact]
    public async Task Csx_ADefinitionTarget_GetsNoChildStructure()
    {
        var dump = new CountingDump();
        dump.RegisterStruct(TypeAddr, DefinitionWalk());

        var csx = await CsxExportService.GenerateCsxAsync(dump, "DataTable", new List<LiveFieldValue> { PointerTo("") },
            drilldownDepth: 2, ct: TestContext.Current.CancellationToken);

        Assert.Contains("Description=\"RowStruct\"", csx);
        Assert.DoesNotContain("Description=\"World\"", csx);
        Assert.DoesNotContain("Description=\"PreviewImage\"", csx);
    }

    [Fact]
    public async Task Csx_APointerToAClass_IsNotEvenWalked()
    {
        var dump = new CountingDump();
        dump.RegisterStruct(TypeAddr, DefinitionWalk(flagged: false));

        var csx = await CsxExportService.GenerateCsxAsync(dump, "GameMode",
            new List<LiveFieldValue> { PointerTo("Class", name: "DefaultPawnClass", type: "ClassProperty") },
            drilldownDepth: 2, ct: TestContext.Current.CancellationToken);

        Assert.DoesNotContain("Description=\"World\"", csx);
        Assert.DoesNotContain(TypeAddr, dump.Walked);
    }

    // ══ the metaclass list is Ubel's own ══

    private static string RepoFile(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (int i = 0; i < 8 && dir is not null; i++, dir = dir.Parent)
        {
            string candidate = Path.Combine(dir.FullName, relative);
            if (File.Exists(candidate)) return candidate;
        }
        throw new FileNotFoundException($"could not find {relative} walking up from {AppContext.BaseDirectory}");
    }

    [Fact]
    public void TheDefinitionMetaclasses_AreTheOnesUbelWalkInstanceAnswersWithADefinition()
    {
        string ubel = File.ReadAllText(RepoFile(@"dll\src\Ubel.cpp")).Replace("\r\n", "\n");
        int mark = ubel.IndexOf("result.isDefinition = true;", StringComparison.Ordinal);
        Assert.True(mark > 0, "Ubel.cpp no longer sets result.isDefinition -- re-derive the list");
        int ifStart = ubel.LastIndexOf("if (result.className ==", mark, StringComparison.Ordinal);
        Assert.True(ifStart > 0 && mark - ifStart < 600);
        var dll = Regex.Matches(ubel[ifStart..mark], "result\\.className == \"(\\w+)\"")
            .Select(m => m.Groups[1].Value).ToHashSet(StringComparer.Ordinal);

        Assert.NotEmpty(dll);
        Assert.True(dll.SetEquals(ReflectionMetaClassifier.DefinitionViewMetas),
            $"Ubel: {string.Join(",", dll.OrderBy(s => s))} / UI: {string.Join(",", ReflectionMetaClassifier.DefinitionViewMetas.OrderBy(s => s))}");
        foreach (var name in dll) Assert.True(ReflectionMetaClassifier.WalksAsDefinition(name), name);
        Assert.False(ReflectionMetaClassifier.WalksAsDefinition("Function"));
        Assert.False(ReflectionMetaClassifier.WalksAsDefinition(""));
        Assert.False(ReflectionMetaClassifier.WalksAsDefinition(null));
    }

    [Fact]
    public void ALeanWalk_StillSaysItIsADefinition()
    {
        // The exporters walk lean, and an exporter must be able to tell a definition from an instance.
        string fern = File.ReadAllText(RepoFile(@"dll\src\Fern.cpp"));
        Assert.DoesNotContain("!lean && result.isDefinition", fern);
        Assert.Contains("if (result.isDefinition)", fern);
    }

    // ══ Live Walker and Instance Finder: a definition's rows have no address ══

    private static (LiveWalkerViewModel vm, MockPlatformService platform) WalkerOn(InstanceWalkResult walk)
    {
        var dump = new StubDumpService();
        dump.RegisterStruct(walk.Address, walk);
        var platform = new MockPlatformService(Path.GetTempPath());
        return (new LiveWalkerViewModel(dump, new MockLoggingService(), platform), platform);
    }

    [Fact]
    public async Task LiveWalker_ADefinitionView_StampsNoAddress_AndSaysWhatItIs()
    {
        var (vm, _) = WalkerOn(DefinitionWalk());

        await vm.NavigateToAddressCommand.ExecuteAsync(TypeAddr);

        Assert.Equal(2, vm.Fields.Count);
        Assert.All(vm.Fields, f => Assert.True(string.IsNullOrEmpty(f.FieldAddress), f.Name));
        Assert.All(vm.Fields, f => Assert.False(f.IsEditable, f.Name));
        Assert.Contains("type definition", vm.StatusText);
    }

    [Fact]
    public async Task LiveWalker_AnInstance_StillStampsAddresses()
    {
        var (vm, _) = WalkerOn(new InstanceWalkResult
        {
            Address = "0x2000", Name = "Pawn_0", ClassName = "Actor",
            Fields = new List<LiveFieldValue> { new() { Name = "Health", TypeName = "FloatProperty", Offset = 0x100, Size = 4 } },
        });

        await vm.NavigateToAddressCommand.ExecuteAsync("0x2000");

        Assert.Equal("0x2100", vm.Fields.Single().FieldAddress);
    }

    [Fact]
    public async Task LiveWalker_ADefinitionView_RefusesCopyCeXml_AndCopyFieldAddress()
    {
        var (vm, platform) = WalkerOn(DefinitionWalk());
        await vm.NavigateToAddressCommand.ExecuteAsync(TypeAddr);

        await vm.ExportCeXmlCommand.ExecuteAsync(null);
        Assert.Null(platform.LastClipboard);
        Assert.Contains("type definition", vm.StatusText);

        await vm.CopyFieldAddressCommand.ExecuteAsync(vm.Fields[0]);
        Assert.Null(platform.LastClipboard);
    }

    // DEFVIEW-FLAG-STALE-IN-CONTAINER-VIEW (review): the container / DataTable views render without a walk, so nothing
    // reset the definition flag there. Drill a TArray<TSubclassOf<>>, open an element (a definition), press Back: the
    // container's rows have real addresses, and every action on them was refused as "a type definition".
    [Fact]
    public async Task LiveWalker_BackFromADefinition_IntoAContainer_ActsOnTheContainersRows()
    {
        var dump = new StubDumpService();
        var array = new LiveFieldValue
        {
            Name = "AbilityClasses", TypeName = "ArrayProperty", Offset = 0x40, Size = 0x10,
            ArrayInnerType = "ClassProperty", ArrayCount = 1, ArrayElemSize = 8, ArrayDataAddr = "0x50000",
            ArrayElements = new List<ArrayElementValue>
            {
                new() { Index = 0, PtrAddress = TypeAddr, PtrName = "GA_Dash_C", PtrClassName = "BlueprintGeneratedClass" },
            },
        };
        dump.RegisterStruct("0x10000000", new InstanceWalkResult
        {
            Address = "0x10000000", Name = "Pawn_0", ClassName = "Pawn", Fields = new List<LiveFieldValue> { array },
        });
        dump.RegisterStruct(TypeAddr, DefinitionWalk());
        var platform = new MockPlatformService(Path.GetTempPath());
        var vm = new LiveWalkerViewModel(dump, new MockLoggingService(), platform);

        await vm.NavigateToAddressCommand.ExecuteAsync("0x10000000");
        await vm.NavigateToContainerCommand.ExecuteAsync(vm.Fields.Single());
        var element = vm.Fields.Single();
        Assert.Equal("0x50000", element.FieldAddress);
        await vm.NavigateToFieldCommand.ExecuteAsync(element);
        Assert.Contains("type definition", vm.StatusText);   // the element really walked as a definition
        await vm.GoBackCommand.ExecuteAsync(null);

        await vm.CopyFieldAddressCommand.ExecuteAsync(vm.Fields.Single());

        Assert.NotNull(platform.LastClipboard);
        Assert.Contains("50000", platform.LastClipboard);
        Assert.DoesNotContain("type definition", vm.StatusText);
    }

    [Fact]
    public async Task InstanceFinder_ADefinitionWalk_StampsNoAddress_AndRefusesItsExports()
    {
        var dump = new StubDumpService();
        dump.RegisterStruct(TypeAddr, DefinitionWalk());
        var platform = new MockPlatformService(Path.GetTempPath());
        var vm = new InstanceFinderViewModel(dump, new MockLoggingService(), platform);
        vm.SelectedInstance = new InstanceResult { Address = TypeAddr, Name = "BlinkDataLevel", ClassName = "ScriptStruct" };
        Assert.Equal(2, vm.Fields.Count);   // the walk landed

        Assert.All(vm.Fields, f => Assert.True(string.IsNullOrEmpty(f.FieldAddress), f.Name));

        await vm.ExportCeXmlCommand.ExecuteAsync(null);
        Assert.Null(platform.LastClipboard);
        Assert.Contains("type definition", vm.StatusText);

        await vm.CopyFieldAddressCommand.ExecuteAsync(vm.Fields[0]);
        Assert.Null(platform.LastClipboard);
    }

    private static InstanceFinderViewModel FinderOn(UE5DumpUI.Core.IDumpService dump, ScriptedAobMakerBridge? bridge = null,
                                                    MockPlatformService? platform = null)
        => new(dump, new MockLoggingService(), platform ?? new MockPlatformService(Path.GetTempPath()),
               new AobMakerStatus(bridge ?? new ScriptedAobMakerBridge { Available = true }));

    // IF-DEFVIEW-FIELDNAME-AS-ACTION (review): HEX and +CE on a definition row said "⚠ World: nothing done" -- the ROW's
    // name where the refused action belongs -- because the refusal sat in FieldCeAddress, which knows no action. Each
    // command now refuses itself, under its button's label; nothing reaches CE.
    [Fact]
    public async Task InstanceFinder_ADefinitionRow_RefusesHexAndAddToCe_UnderTheirButtonLabels()
    {
        var dump = new StubDumpService();
        dump.RegisterStruct(TypeAddr, DefinitionWalk());
        var bridge = new ScriptedAobMakerBridge { Available = true };
        var vm = FinderOn(dump, bridge);
        vm.SelectedInstance = new InstanceResult { Address = TypeAddr, Name = "BlinkDataLevel", ClassName = "ScriptStruct" };
        var row = vm.Fields[0];

        await vm.HexFieldAddressCommand.ExecuteAsync(row);
        Assert.Null(bridge.LastHex);
        Assert.Equal(LiveWalkerViewModel.DefinitionNoDataText("HEX"), vm.StatusText);

        await vm.AddFieldToCeCommand.ExecuteAsync(row);
        Assert.Empty(bridge.Records);
        Assert.Equal(LiveWalkerViewModel.DefinitionNoDataText("+CE"), vm.StatusText);
    }

    /// <summary>A dump whose walk of <see cref="PendingAddr"/> never lands: the grid keeps the previous rows.</summary>
    private sealed class PendingDump : StubDumpService
    {
        public const string PendingAddr = "0x9000";
        public override Task<InstanceWalkResult> WalkInstanceAsync(string addr, string? classAddr = null,
            int arrayLimit = 64, int previewLimit = 2, bool fillGaps = false, bool lean = false,
            CancellationToken ct = default)
            => addr == PendingAddr
                ? new TaskCompletionSource<InstanceWalkResult>().Task
                : base.WalkInstanceAsync(addr, classAddr, arrayLimit, previewLimit, fillGaps, lean, ct);
    }

    // DEFVIEW-IF-COPYADDR-GUARD-BYPASS (review): Copy Addr refused a definition row only while that definition was still
    // the selection, and added the offset to the SELECTED instance's address. Pick another instance and, while its walk
    // is in flight, the grid still shows the definition's rows -- Addr then copied the new instance plus a type-layout
    // offset. A live instance's rows are the same case the other way round ([R7-S14]): they belong to the instance they
    // were walked from, so the address is that one's.
    [Fact]
    public async Task InstanceFinder_CopyAddr_ActsOnTheRowsShown_NotOnTheNewSelection()
    {
        var dump = new PendingDump();
        dump.RegisterStruct(TypeAddr, DefinitionWalk());
        dump.RegisterStruct("0x2000", new InstanceWalkResult
        {
            Address = "0x2000", Name = "Pawn_0", ClassName = "Actor",
            Fields = new List<LiveFieldValue> { new() { Name = "Health", TypeName = "FloatProperty", Offset = 0x100, Size = 4 } },
        });
        var platform = new MockPlatformService(Path.GetTempPath());
        var vm = FinderOn(dump, platform: platform);
        InstanceResult Pending() => new() { Address = PendingDump.PendingAddr, Name = "Pawn_9", ClassName = "Actor" };

        vm.SelectedInstance = new InstanceResult { Address = TypeAddr, Name = "BlinkDataLevel", ClassName = "ScriptStruct" };
        var definitionRow = vm.Fields[0];
        vm.SelectedInstance = Pending();
        await vm.CopyFieldAddressCommand.ExecuteAsync(definitionRow);
        Assert.Null(platform.LastClipboard);
        Assert.Equal(LiveWalkerViewModel.DefinitionNoDataText("Addr"), vm.StatusText);

        vm.SelectedInstance = new InstanceResult { Address = "0x2000", Name = "Pawn_0", ClassName = "Actor" };
        var liveRow = vm.Fields.Single();
        vm.SelectedInstance = Pending();
        await vm.CopyFieldAddressCommand.ExecuteAsync(liveRow);
        Assert.NotNull(platform.LastClipboard);
        Assert.Contains("2100", platform.LastClipboard);
    }
}
