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
    public void InstanceXml_OpensWithTheNote()
    {
        var xml = CeXmlExportService.GenerateInstanceXml("\"Game.exe\"+1000", "Inst", "TestClass", OneField,
                                                         headerNotes: new[] { Note });
        Assert.Contains($"<!-- {Note} -->", xml);
        Assert.DoesNotContain("<!--", CeXmlExportService.GenerateInstanceXml("\"Game.exe\"+1000", "Inst", "TestClass", OneField));
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
