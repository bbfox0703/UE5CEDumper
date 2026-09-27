using UE5DumpUI.Core;
using UE5DumpUI.Models;
using UE5DumpUI.Services;
using UE5DumpUI.ViewModels;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// [SDK-LIVE-VALUE-TYPES] Live Walker's "Export .h" declared each member's TYPE from what the instance
/// HOLDS. A <c>walk_instance</c> row's <c>EnumName</c> is the enum VALUE's name (<c>Ubel.cpp</c>
/// <c>ResolveEnumValue</c>) and its <c>PtrClassName</c> is the pointee's RUNTIME class, so the header
/// read <c>EDumperTestGrade__Elite Grade;</c> and <c>class BP_ThirdPersonCharacter_C* CharacterOwner;</c>,
/// and changed whenever a value did. The same wire key <c>enum_name</c> means the enum TYPE in
/// <c>walk_class</c>, which is where declared types come from. For an object view the export never
/// walked the class at all, so it had no super and re-declared every inherited property (audit #5 W2).
/// </summary>
public class SdkLiveWalkerExportTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"UE5DumpSdkLive_{Guid.NewGuid():N}");

    public SdkLiveWalkerExportTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best-effort */ }
    }

    private sealed class SavingPlatform(string path) : IPlatformService
    {
        public bool TryAcquireSingleInstance() => true;
        public void ReleaseSingleInstance() { }
        public string GetAppDataPath() => Path.GetDirectoryName(path)!;
        public string GetLogDirectoryPath() => Path.Combine(Path.GetDirectoryName(path)!, "Logs");
        public Task<bool> CopyToClipboardAsync(string text) => Task.FromResult(true);
        public Task RevealInExplorerAsync(string p) => Task.CompletedTask;
        public string GetMachineName() => "TEST-MACHINE";
        public void CloseImeForWindow(IntPtr windowHandle) { }
        public Task<string?> ShowSaveFileDialogAsync(string defaultFileName, string filterName, string filterExtension)
            => Task.FromResult<string?>(path);
    }

    private const string ActorAddr = "0x100000";
    private const string ActorClassAddr = "0x900000";

    /// <summary>DumperTestActor_0 as <c>walk_instance</c> reports it: VALUES in the type-shaped fields.</summary>
    private static InstanceWalkResult ActorInstance(string grade = "EDumperTestGrade::Elite") => new()
    {
        Address = ActorAddr, Name = "DumperTestActor_0", ClassName = "DumperTestActor", ClassAddr = ActorClassAddr,
        Fields = new List<LiveFieldValue>
        {
            new() { Name = "PrimaryActorTick", TypeName = "StructProperty", Offset = 0x08, Size = 8, StructTypeName = "ActorTickFunction" },
            new() { Name = "Grade", TypeName = "EnumProperty", Offset = 0x10, Size = 1, EnumName = grade, TypedValue = grade },
            new() { Name = "WideGrade", TypeName = "EnumProperty", Offset = 0x14, Size = 4, EnumName = "EDumperTestWideGrade::Wide_Base" },
            new() { Name = "CharacterOwner", TypeName = "ObjectProperty", Offset = 0x18, Size = 8, PtrClassName = "BP_ThirdPersonCharacter_C" },
            new() { Name = "LazyAnchors", TypeName = "ArrayProperty", Offset = 0x20, Size = 0x10, ArrayInnerType = "ObjectProperty" },
        },
    };

    /// <summary>The same class as <c>walk_class</c> reports it: DECLARED types, and its super.</summary>
    private static ClassInfoModel ActorClass() => new()
    {
        Name = "DumperTestActor", FullPath = "//Script/DumperTest/DumperTestActor",
        SuperName = "Actor", SuperAddress = "0x800000", PropertiesSize = 0x30,
        SuperPropertiesSize = 0x10, OwnPropertiesStart = 0x10,
        Fields =
        {
            new FieldInfoModel { Name = "PrimaryActorTick", TypeName = "StructProperty", Offset = 0x08, Size = 8, StructType = "ActorTickFunction" },
            new FieldInfoModel { Name = "Grade", TypeName = "EnumProperty", Offset = 0x10, Size = 1, EnumName = "EDumperTestGrade" },
            new FieldInfoModel { Name = "WideGrade", TypeName = "EnumProperty", Offset = 0x14, Size = 4, EnumName = "EDumperTestWideGrade" },
            new FieldInfoModel { Name = "CharacterOwner", TypeName = "ObjectProperty", Offset = 0x18, Size = 8, ObjClassName = "Character" },
            new FieldInfoModel { Name = "LazyAnchors", TypeName = "ArrayProperty", Offset = 0x20, Size = 0x10, InnerType = "ObjectProperty", InnerObjClass = "Actor" },
        },
    };

    private async Task<string> ExportAsync(StubDumpService dump)
    {
        var path = Path.Combine(_dir, $"export_{Guid.NewGuid():N}.h");
        var vm = new LiveWalkerViewModel(dump, new MockLoggingService(), new SavingPlatform(path));
        await vm.NavigateToAddressCommand.ExecuteAsync(ActorAddr);
        await vm.ExportSdkHeaderCommand.ExecuteAsync(null);
        Assert.True(File.Exists(path), "the export wrote no file");
        return File.ReadAllText(path);
    }

    /// <summary>The header's lines without their trailing comments.</summary>
    private static string Code(string header) => string.Join("\n",
        header.Split('\n').Select(l => l.TrimEnd('\r'))
              .Select(l => l.IndexOf("//", StringComparison.Ordinal) is var c and >= 0 ? l[..c] : l));

    [Fact]
    public async Task AnObjectView_ExportsTheDeclaredTypes_AndItsSuper()
    {
        var dump = new StubDumpService();
        dump.RegisterStruct(ActorAddr, ActorInstance());
        dump.RegisterClass(ActorClassAddr, ActorClass());

        var code = Code(await ExportAsync(dump));

        Assert.Contains("struct DumperTestActor : public Actor", code);
        Assert.Contains("EDumperTestGrade Grade;", code);
        Assert.Contains("EDumperTestWideGrade WideGrade;", code);
        Assert.Contains("class Character* CharacterOwner;", code);
        Assert.Contains("TArray<class Actor*> LazyAnchors;", code);
        // The inherited row stays with the super instead of being declared again.
        Assert.DoesNotContain("PrimaryActorTick", code);
        Assert.DoesNotContain("Elite", code);
        Assert.DoesNotContain("BP_ThirdPersonCharacter_C", code);
    }

    [Fact]
    public async Task AValueChange_DoesNotChangeTheHeader()
    {
        async Task<string> With(string grade)
        {
            var dump = new StubDumpService();
            dump.RegisterStruct(ActorAddr, ActorInstance(grade));
            dump.RegisterClass(ActorClassAddr, ActorClass());
            return await ExportAsync(dump);
        }

        Assert.Equal(await With("EDumperTestGrade::Elite"), await With("EDumperTestGrade::Rookie"));
    }

    [Fact]
    public async Task WithoutAClassWalk_NoValueBecomesAType()
    {
        // No walk_class answer (the stub returns an empty ClassInfo): the live rows are all there is,
        // and a value still must not be declared as a type -- an unknown enum is an integer of its
        // size, an unknown pointee is UObject.
        var dump = new StubDumpService();
        dump.RegisterStruct(ActorAddr, ActorInstance());

        var code = Code(await ExportAsync(dump));

        Assert.Contains("uint8_t Grade;", code);
        Assert.Contains("uint32_t WideGrade;", code);
        Assert.Contains("class UObject* CharacterOwner;", code);
        Assert.DoesNotContain("Elite", code);
        Assert.DoesNotContain("Wide_Base", code);
        Assert.DoesNotContain("BP_ThirdPersonCharacter_C", code);
    }

    // ------------------------------------------------------------------
    // The declarations themselves.
    // ------------------------------------------------------------------

    [Theory]
    [InlineData(1, "uint8_t")]
    [InlineData(2, "uint16_t")]
    [InlineData(4, "uint32_t")]
    [InlineData(8, "uint64_t")]
    public void AnEnumWithNoKnownType_IsAnIntegerOfItsSize(int size, string expected)
    {
        // A 4-byte enum declared uint8_t shifts every member after it (the padding pass trusts the
        // row's Size, the compiler trusts the declaration).
        var field = new FieldInfoModel { Name = "E", TypeName = "EnumProperty", Offset = 0, Size = size };
        Assert.Equal(expected, SdkExportService.MapCppDecl(field).Type);
    }

    [Fact]
    public void AClassProperty_WhoseMetaclassIsUnknown_IsAPlainUClassPointer()
    {
        // walk_class sends a ClassProperty's PropertyClass, which is always `Class`; its MetaClass is
        // not on the wire. `TSubclassOf<class Class>` (222 of 224 TSubclassOf in a real 5.4 export)
        // declares a subclass of UClass, which no property holds.
        Assert.Equal("UClass*", SdkExportService.MapCppDecl(
            new FieldInfoModel { Name = "C", TypeName = "ClassProperty", ObjClassName = "Class", Size = 8 }).Type);
        Assert.Equal("TSoftClassPtr<UObject>", SdkExportService.MapCppDecl(
            new FieldInfoModel { Name = "S", TypeName = "SoftClassProperty", ObjClassName = "Class", Size = 0x28 }).Type);
        Assert.Equal("TArray<UClass*>", SdkExportService.MapCppDecl(
            new FieldInfoModel { Name = "A", TypeName = "ArrayProperty", InnerType = "ClassProperty", InnerObjClass = "Class", Size = 0x10 }).Type);
    }
}
