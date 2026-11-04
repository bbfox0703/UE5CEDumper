using System.IO;
using System.Threading.Tasks;
using UE5DumpUI.Models;
using UE5DumpUI.Services;
using UE5DumpUI.ViewModels;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// [CEXPORT-STRIDE-NOTE] When the object-array stride is a guess, the top-bar badge and the dump's header say so, but
/// an exported CE table or CSX did not: a table built on an aliased pool carried no word of it once it left the app.
/// Each export now opens with the badge's words as a comment, passed in by the view model that holds the engine state
/// (a parameter, not ambient state: a test that turned a process-wide flag on would put the note into every export
/// built meanwhile by tests running in parallel).
/// </summary>
public class CeExportStrideNoteTests
{
    private static readonly EngineState Guessed = new()
    {
        ModuleName = "Game.exe", ModuleBase = "0x140000000", UEVersion = 505,
        ItemDetect = "tentative", ItemDetectValidated = 1, ItemDetectProbes = 200,
    };

    private const string Note = "Object-array stride is a guess (1 of 200 probes validated) — object counts and names may be wrong";

    private static readonly LiveFieldValue[] OneField =
        { new() { Name = "HP", TypeName = "FloatProperty", Offset = 0x10, Size = 4, FieldAddress = "0x1010" } };

    [Fact]
    public void TheNote_IsTheBadgesWords_OnlyWhileTheStrideIsAGuess()
    {
        Assert.Equal(Note, Guessed.StrideExportNote);
        Assert.Equal("", new EngineState { ItemDetect = "detected" }.StrideExportNote);
        Assert.Equal("", new EngineState { ItemDetect = "" }.StrideExportNote);   // an older DLL: no verdict
        // A refused engine guessed nothing ([REFUSED-STRIDE-NOTICE]).
        Assert.Equal("", new EngineState { ItemDetect = "undetected", IsVersionTooOld = true }.StrideExportNote);
        Assert.StartsWith("Object-array stride not detected", new EngineState { ItemDetect = "undetected" }.StrideExportNote);
    }

    [Fact]
    public void ACommentLine_HoldsNoDoubleDash_HoweverManyDashesTheNoteHas()
    {
        // Review round 1: one Replace("--", "- -") pass leaves "--" from a run of three ("---" -> "- --").
        var line = CeXmlExportService.XmlCommentLines(new[] { "a---b----c" });
        var body = line["<!--".Length..line.LastIndexOf("-->", StringComparison.Ordinal)];
        Assert.DoesNotContain("--", body);
        Assert.Contains("a", body);
    }

    [Fact]
    public void InstanceXml_OpensWithTheNote()
    {
        var xml = CeXmlExportService.GenerateInstanceXml("\"Game.exe\"+1000", "Inst", "TestClass", OneField,
                                                         headerNotes: new[] { Note });
        Assert.Contains($"<!-- {Note} -->", xml);
        // The note only: "<!--" alone would also see PackedLayoutNotice, process-wide state another test may hold on.
        Assert.DoesNotContain(Note, CeXmlExportService.GenerateInstanceXml("\"Game.exe\"+1000", "Inst", "TestClass", OneField));
    }

    [Fact]
    public void HierarchicalXml_OpensWithTheNote()
    {
        var xml = CeXmlExportService.GenerateHierarchicalXml("\"Game.exe\"+1000", "Root",
            new List<BreadcrumbItem> { new() { Address = "0x1000", Label = "Root", FieldName = "0x1000", IsPointerDeref = true } },
            OneField, headerNotes: new[] { Note });
        Assert.Contains($"<!-- {Note} -->", xml);
    }

    [Fact]
    public void AobWrappedXml_OpensWithTheNote()
    {
        var xml = CeXmlExportService.GenerateAobWrappedXml("World",
            new List<BreadcrumbItem> { new() { Address = "0x1000", Label = "GWorld", FieldName = "GWorld", IsPointerDeref = true } },
            OneField, aob: "48 8B 1D ?? ?? ?? ??", aobPos: 3, aobLen: 7, moduleName: "Game.exe", headerNotes: new[] { Note });
        Assert.Contains($"<!-- {Note} -->", xml);
    }

    [Fact]
    public async Task Csx_OpensWithTheNote()
    {
        var csx = await CsxExportService.GenerateCsxAsync(new StubDumpService(), "S", OneField, headerNotes: new[] { Note },
                                                          ct: TestContext.Current.CancellationToken);
        Assert.Contains($"<!-- {Note} -->", csx);
    }

    [Fact]
    public async Task LiveWalker_CopyCeXml_CarriesTheNote()
    {
        var dump = new StubDumpService();
        dump.RegisterStruct("0x1000", new InstanceWalkResult
        {
            Address = "0x1000", Name = "Obj_0", ClassName = "BP_Obj_C", ClassAddr = "0xC000",
            Fields = new List<LiveFieldValue> { new() { Name = "HP", TypeName = "FloatProperty", Offset = 0x10, Size = 4 } },
        });
        var platform = new MockPlatformService(Path.GetTempPath());
        var vm = new LiveWalkerViewModel(dump, new MockLoggingService(), platform);
        vm.SetEngineState(Guessed);
        await vm.NavigateToAddressCommand.ExecuteAsync("0x1000");

        await vm.ExportCeXmlCommand.ExecuteAsync(null);

        Assert.Contains($"<!-- {Note} -->", platform.LastClipboard);
    }

    // ── [CEXPORT-ENUMNAMES-NOTE] When UEnum::Names was never located, every enum field arrives with no entries and the
    // exported dropdowns come out empty. The walk now says so, and an export holding an enum field says why. ──

    private static StubDumpService EnumDump(bool namesFailed, string typeName = "EnumProperty")
    {
        var dump = new StubDumpService();
        dump.RegisterStruct("0x1000", new InstanceWalkResult
        {
            Address = "0x1000", Name = "Obj_0", ClassName = "BP_Obj_C", ClassAddr = "0xC000",
            EnumNamesFailed = namesFailed,
            Fields = new List<LiveFieldValue>
            {
                new() { Name = "Mode", TypeName = typeName, Offset = 0x10, Size = 1, EnumAddr = "0xE000" },
            },
        });
        return dump;
    }

    [Fact]
    public async Task LiveWalker_CopyCeXml_OfAnEnumField_SaysTheNamesAreUnavailable()
    {
        var platform = new MockPlatformService(Path.GetTempPath());
        var vm = new LiveWalkerViewModel(EnumDump(namesFailed: true), new MockLoggingService(), platform);
        await vm.NavigateToAddressCommand.ExecuteAsync("0x1000");

        await vm.ExportCeXmlCommand.ExecuteAsync(null);

        Assert.Contains("<!-- Enum member names", platform.LastClipboard);
        Assert.Contains("Enum member names", vm.StatusText);
    }

    [Fact]
    public async Task LiveWalker_CopyCeXml_SaysNothingAboutEnums_WhenTheNamesWereRead_OrNoFieldIsAnEnum()
    {
        var platform = new MockPlatformService(Path.GetTempPath());
        var vm = new LiveWalkerViewModel(EnumDump(namesFailed: false), new MockLoggingService(), platform);
        await vm.NavigateToAddressCommand.ExecuteAsync("0x1000");
        await vm.ExportCeXmlCommand.ExecuteAsync(null);
        Assert.DoesNotContain("Enum member names", platform.LastClipboard);

        var platform2 = new MockPlatformService(Path.GetTempPath());
        var dump2 = new StubDumpService();
        dump2.RegisterStruct("0x1000", new InstanceWalkResult
        {
            Address = "0x1000", Name = "Obj_0", ClassName = "BP_Obj_C", ClassAddr = "0xC000", EnumNamesFailed = true,
            Fields = new List<LiveFieldValue> { new() { Name = "HP", TypeName = "FloatProperty", Offset = 0x10, Size = 4 } },
        });
        var vm2 = new LiveWalkerViewModel(dump2, new MockLoggingService(), platform2);
        await vm2.NavigateToAddressCommand.ExecuteAsync("0x1000");
        await vm2.ExportCeXmlCommand.ExecuteAsync(null);
        Assert.DoesNotContain("Enum member names", platform2.LastClipboard);
    }

    [Fact]
    public async Task InstanceFinder_CopyCeXml_OfAnEnumField_SaysTheNamesAreUnavailable()
    {
        var dump = new StubDumpService();
        dump.RegisterStruct("0x10000000", new InstanceWalkResult
        {
            Address = "0x10000000", Name = "Obj_0", ClassName = "Obj", EnumNamesFailed = true,
            Fields = new List<LiveFieldValue>
            {
                new() { Name = "Mode", TypeName = "ByteProperty", Offset = 0x28, Size = 1, EnumAddr = "0xE000" },
            },
        });
        var platform = new MockPlatformService(Path.GetTempPath());
        var vm = new InstanceFinderViewModel(dump, new MockLoggingService(), platform);
        vm.SelectedInstance = new InstanceResult { Address = "0x10000000", Name = "Obj_0", ClassName = "Obj" };

        await vm.ExportCeXmlCommand.ExecuteAsync(null);

        Assert.Contains("<!-- Enum member names", platform.LastClipboard);
        Assert.Contains("Enum member names", vm.StatusText);
    }

    // Review round 1 of [CEXPORT-ENUMNAMES-NOTE].

    /// <summary>A stub whose walks of one address say the latch is set: the DLL detects UEnum::Names lazily, so the walk
    /// that finds the first enum is the one whose reply first carries the flag.</summary>
    private sealed class LatchOnWalkOf : StubDumpService
    {
        private readonly string _addr;
        public LatchOnWalkOf(string addr) => _addr = addr;
        public override async Task<InstanceWalkResult> WalkInstanceAsync(string addr, string? classAddr = null,
            int arrayLimit = 64, int previewLimit = 2, bool fillGaps = false, bool lean = false,
            CancellationToken ct = default)
        {
            var r = await base.WalkInstanceAsync(addr, classAddr, arrayLimit, previewLimit, fillGaps, lean, ct);
            return addr != _addr ? r : new InstanceWalkResult
            {
                Address = r.Address, Name = r.Name, ClassName = r.ClassName, ClassAddr = r.ClassAddr,
                EnumNamesFailed = true, Fields = r.Fields,
            };
        }
    }

    private static LatchOnWalkOf NestedEnumDump()
    {
        var dump = new LatchOnWalkOf("0x2000");
        dump.RegisterStruct("0x1000", new InstanceWalkResult
        {
            Address = "0x1000", Name = "Obj_0", ClassName = "BP_Obj_C", ClassAddr = "0xC000",
            Fields = new List<LiveFieldValue>
            {
                new() { Name = "Stats", TypeName = "StructProperty", Offset = 0x10, Size = 0x10,
                        StructTypeName = "FStats", StructClassAddr = "0xC800", StructDataAddr = "0x2000" },
            },
        });
        dump.RegisterStruct("0x2000", new InstanceWalkResult   // as sent under the latch: the UEnum, no entries
        {
            Address = "0x2000", Name = "FStats", ClassName = "FStats", ClassAddr = "0xC800",
            Fields = new List<LiveFieldValue>
            {
                new() { Name = "Mode", TypeName = "ByteProperty", Offset = 0x0, Size = 1, EnumAddr = "0xE000" },
            },
        });
        return dump;
    }

    [Fact]
    public async Task LiveWalker_CopyCeXml_LearnsTheLatchFromItsOwnWalks()
    {
        // The top level holds no enum, so its walk does not run the detection; the struct walk the export makes does.
        var platform = new MockPlatformService(Path.GetTempPath());
        var vm = new LiveWalkerViewModel(NestedEnumDump(), new MockLoggingService(), platform);
        await vm.NavigateToAddressCommand.ExecuteAsync("0x1000");

        await vm.ExportCeXmlCommand.ExecuteAsync(null);

        Assert.Contains("<!-- Enum member names", platform.LastClipboard);   // was silent until a Refresh
    }

    [Fact]
    public async Task InstanceFinder_CopyCeXml_LearnsTheLatchFromItsOwnWalks()
    {
        var dump = NestedEnumDump();
        dump.RegisterStruct("0x10000000", new InstanceWalkResult
        {
            Address = "0x10000000", Name = "Obj_0", ClassName = "Obj",
            Fields = new List<LiveFieldValue>
            {
                new() { Name = "Stats", TypeName = "StructProperty", Offset = 0x28, Size = 0x10,
                        StructTypeName = "FStats", StructClassAddr = "0xC800", StructDataAddr = "0x2000" },
            },
        });
        var platform = new MockPlatformService(Path.GetTempPath());
        var vm = new InstanceFinderViewModel(dump, new MockLoggingService(), platform);
        vm.SelectedInstance = new InstanceResult { Address = "0x10000000", Name = "Obj_0", ClassName = "Obj" };

        await vm.ExportCeXmlCommand.ExecuteAsync(null);

        Assert.Contains("<!-- Enum member names", platform.LastClipboard);
    }

    [Fact]
    public void TheNote_CoversAnArrayOfByteEnums_AsTheDllSendsIt()
    {
        // A TArray<TEnumAsByte<E>> under the latch: inner type ByteProperty, the UEnum's address, no entries.
        var arr = new LiveFieldValue
        {
            Name = "Modes", TypeName = "ArrayProperty", ArrayInnerType = "ByteProperty", ArrayEnumAddr = "0xE000",
        };
        Assert.Equal(CeXmlExportService.EnumNamesUnavailableNote, CeXmlExportService.EnumNamesNote(true, new[] { arr }));
        // Control: a plain byte array holds no enum.
        Assert.Equal("", CeXmlExportService.EnumNamesNote(true,
            new[] { new LiveFieldValue { Name = "Bytes", TypeName = "ArrayProperty", ArrayInnerType = "ByteProperty" } }));
    }

    [Fact]
    public void TheNote_CoversADataTablesRowEnums()
    {
        // Review round 2: a DataTable is exported through its synthetic RowMap field, whose own type says nothing; its
        // rows' fields are where the enums are, and the note never looked there.
        var rowMap = new LiveFieldValue
        {
            Name = "RowMap", TypeName = "DataTableRows",
            DataTableRowData = new()
            {
                new DataTableRowInfo { RowName = "Sword", Fields = new() { new LiveFieldValue { Name = "Kind", TypeName = "EnumProperty" } } },
            },
        };
        Assert.Equal(CeXmlExportService.EnumNamesUnavailableNote, CeXmlExportService.EnumNamesNote(true, new[] { rowMap }));
        // Control: a table whose rows hold no enum.
        var plain = new LiveFieldValue
        {
            Name = "RowMap", TypeName = "DataTableRows",
            DataTableRowData = new() { new DataTableRowInfo { Fields = new() { new LiveFieldValue { TypeName = "IntProperty" } } } },
        };
        Assert.Equal("", CeXmlExportService.EnumNamesNote(true, new[] { plain }));
    }

    [Fact]
    public async Task PushToCeStructureDissect_SaysNothingAboutDropdowns_ItHasNone()
    {
        var bridge = new ScriptedAobMakerBridge { Available = true };
        var vm = new LiveWalkerViewModel(EnumDump(namesFailed: true), new MockLoggingService(),
                                         new MockPlatformService(Path.GetTempPath()), bridge);
        vm.ApplyAobMakerProbe(true);
        await vm.NavigateToAddressCommand.ExecuteAsync("0x1000");

        await vm.PushCsxToCeCommand.ExecuteAsync(null);

        Assert.Single(bridge.AaScripts);
        Assert.DoesNotContain("Enum member names", vm.StatusText);
        Assert.DoesNotContain("Enum member names", bridge.AaScripts[0].Script);
    }

    // Review round 1 of [CEXPORT-STRIDE-NOTE]: each export's wiring of the note, held.

    private static (LiveWalkerViewModel vm, MockPlatformService platform) GuessedWalker(string? savePath = null)
    {
        var dump = new StubDumpService();
        dump.RegisterStruct("0x1000", new InstanceWalkResult
        {
            Address = "0x1000", Name = "Obj_0", ClassName = "BP_Obj_C", ClassAddr = "0xC000",
            Fields = new List<LiveFieldValue> { new() { Name = "HP", TypeName = "FloatProperty", Offset = 0x10, Size = 4 } },
        });
        var platform = new MockPlatformService(Path.GetTempPath()) { NextSavePath = savePath };
        var vm = new LiveWalkerViewModel(dump, new MockLoggingService(), platform);
        vm.SetEngineState(Guessed);
        return (vm, platform);
    }

    [Fact]
    public async Task LiveWalker_CopyCeField_CarriesTheNote()
    {
        var (vm, platform) = GuessedWalker();
        await vm.NavigateToAddressCommand.ExecuteAsync("0x1000");
        vm.SelectedField = vm.Fields.Single();

        await vm.ExportCeFieldXmlCommand.ExecuteAsync(null);

        Assert.Contains($"<!-- {Note} -->", platform.LastClipboard);
    }

    [Fact]
    public async Task LiveWalker_ExportCsx_CarriesTheNote()
    {
        var file = Path.Combine(Path.GetTempPath(), $"UE5DumpStrideCsx_{Guid.NewGuid():N}.csx");
        var (vm, _) = GuessedWalker(file);
        await vm.NavigateToAddressCommand.ExecuteAsync("0x1000");
        try
        {
            await vm.ExportCsx77Command.ExecuteAsync(null);

            Assert.Contains($"<!-- {Note} -->", await File.ReadAllTextAsync(file, TestContext.Current.CancellationToken));
        }
        finally { File.Delete(file); }
    }

    // The AOB-wrapped branch of Copy CE XML / Copy CE Field passes the same exportNotes as the hierarchical one; its
    // generator is held above (AobWrappedXml_OpensWithTheNote). Push to CE Structure Dissect passes the CSX notes, but
    // CsxStructurePushGenerator's parser drops every XML comment (the packed-layout note too), so nothing of it reaches
    // CE there.

    [Fact]
    public async Task InstanceFinder_CopyCeXml_CarriesTheNote()
    {
        var dump = new StubDumpService();
        dump.RegisterStruct("0x10000000", new InstanceWalkResult
        {
            Address = "0x10000000", Name = "Obj_0", ClassName = "Obj",
            Fields = new List<LiveFieldValue> { new() { Name = "HP", TypeName = "FloatProperty", Offset = 0x28, Size = 4 } },
        });
        var platform = new MockPlatformService(Path.GetTempPath());
        var vm = new InstanceFinderViewModel(dump, new MockLoggingService(), platform);
        vm.SetEngineState(Guessed);
        vm.SelectedInstance = new InstanceResult { Address = "0x10000000", Name = "Obj_0", ClassName = "Obj" };

        await vm.ExportCeXmlCommand.ExecuteAsync(null);

        Assert.Contains($"<!-- {Note} -->", platform.LastClipboard);
    }
}
