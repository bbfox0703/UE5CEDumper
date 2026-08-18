using System.Text;
using System.Text.RegularExpressions;
using UE5DumpUI.Models;
using UE5DumpUI.Services;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// The exported SDK header must be valid C++.
///
/// <para>It was not: <c>OptionalProperty</c> and any unresolved <c>StructProperty</c> baked the array
/// extent into the TYPE (<c>uint8_t[0x40] CellBounds;</c>), which is a syntax error. A real
/// 75,342-line export carried 5 of them — every one an <c>OptionalProperty</c> — against 7,543
/// well-formed <c>uint8_t Pad_0000[0x0028];</c> padding declarations, so the padding emitter was
/// always right and only the two fallbacks were wrong. <c>CellBounds</c> is an engine (World
/// Partition) property, so this was not sample-only.</para>
///
/// <para>Nothing caught it because the emitters were unit-covered but never over a
/// <c>TOptional</c> field, and because a generated header is only ever READ in this repo's checks,
/// never compiled. <see cref="EveryMemberDeclaratorIsWellFormed"/> is the general oracle — it walks
/// every emitted member and rejects an extent that appears before the identifier, whatever produced
/// it — and it also writes the compile-smoke artifact that <c>tools/verify/compile_sdk_header.py</c>
/// feeds to a real compiler.</para>
/// </summary>
public class SdkHeaderDeclaratorTests
{
    // ------------------------------------------------------------------
    // The two fallback paths, at the level the defect actually appeared:
    // the emitted declaration, not the type string in isolation.
    // ------------------------------------------------------------------

    [Fact]
    public void OptionalProperty_EmitsExtentAfterTheIdentifier()
    {
        // The exact field from the export that surfaced this: FWorldPartitionCellBounds is a
        // TOptional UPROPERTY, so the DLL reports OptionalProperty and no struct type.
        var classInfo = new ClassInfoModel
        {
            Name = "UWorldPartitionRuntimeCell",
            SuperName = "",
            PropertiesSize = 0xC8,
            Fields =
            {
                new FieldInfoModel { Name = "CellBounds", TypeName = "OptionalProperty", Offset = 0x88, Size = 0x40 },
            },
        };

        var header = SdkExportService.GenerateClassHeaderFromSchema(classInfo);

        Assert.Contains("uint8_t CellBounds[0x40];", header);
        Assert.DoesNotContain("uint8_t[0x40] CellBounds", header);
    }

    [Fact]
    public void OptionalProperty_FromTheLiveWalkerPath_EmitsExtentAfterTheIdentifier()
    {
        // LiveFieldValue is a second, independent metadata shape feeding the same emitter — the
        // schema path passing is not evidence about this one.
        var fields = new List<LiveFieldValue>
        {
            new() { Name = "Opt_Int_Set", TypeName = "OptionalProperty", Offset = 0x608, Size = 8 },
        };

        var header = SdkExportService.GenerateClassHeader("ADumperTestActor", "", 0x610, fields);

        Assert.Contains("uint8_t Opt_Int_Set[0x8];", header);
        Assert.DoesNotContain("uint8_t[0x8] Opt_Int_Set", header);
    }

    [Fact]
    public void UnresolvedStructProperty_EmitsExtentAfterTheIdentifier()
    {
        // A StructProperty whose UScriptStruct the DLL could not name falls through the same
        // raw-byte route. It is a distinct branch from the `_ =>` default above.
        var classInfo = new ClassInfoModel
        {
            Name = "FMysteryHolder",
            SuperName = "",
            PropertiesSize = 0x10,
            Fields =
            {
                new FieldInfoModel { Name = "Blob", TypeName = "StructProperty", Offset = 0, Size = 0xC },
            },
        };

        var header = SdkExportService.GenerateClassHeaderFromSchema(classInfo);

        Assert.Contains("uint8_t Blob[0xC];", header);
        Assert.DoesNotContain("uint8_t[0xC] Blob", header);
    }

    [Fact]
    public void ZeroSizedRawFallback_DoesNotEmitAZeroLengthArray()
    {
        // `uint8_t Name[0x0];` is rejected by MSVC (C2466), which would reproduce the very failure
        // this fix is about. A byte is the smallest thing C++ can express here.
        var classInfo = new ClassInfoModel
        {
            Name = "FDegenerate",
            SuperName = "",
            PropertiesSize = 1,
            Fields =
            {
                new FieldInfoModel { Name = "Nothing", TypeName = "OptionalProperty", Offset = 0, Size = 0 },
            },
        };

        var header = SdkExportService.GenerateClassHeaderFromSchema(classInfo);

        Assert.DoesNotContain("[0x0]", header);
        Assert.Contains("uint8_t Nothing[0x1];", header);
    }

    // ------------------------------------------------------------------
    // Negative controls — the paths that were already correct must not move.
    // ------------------------------------------------------------------

    [Fact]
    public void ResolvedStructProperty_IsStillAPlainNamedMember()
    {
        var classInfo = new ClassInfoModel
        {
            Name = "AThing",
            SuperName = "",
            PropertiesSize = 0x18,
            Fields =
            {
                new FieldInfoModel { Name = "Location", TypeName = "StructProperty", StructType = "FVector", Offset = 0, Size = 0x18 },
            },
        };

        var header = SdkExportService.GenerateClassHeaderFromSchema(classInfo);

        Assert.Contains("struct FVector Location;", header);
        Assert.DoesNotContain("uint8_t Location", header);
    }

    [Fact]
    public void PaddingDeclarationsAreByteIdenticalToBeforeTheFix()
    {
        // 7,543 of these in the export that found the bug. They were the correct model for the
        // fix, so a change here would mean the fix went the wrong way.
        var fields = new List<LiveFieldValue>
        {
            new() { Name = "Health", TypeName = "FloatProperty", Offset = 0x100, Size = 4 },
            new() { Name = "MaxHealth", TypeName = "FloatProperty", Offset = 0x110, Size = 4 },
        };

        var header = SdkExportService.GenerateClassHeader("AMyActor", "AActor", 0x120, fields);

        Assert.Contains("uint8_t Pad_0104[0x000C]; // 0x0104 (0x000C) PADDING", header);
        Assert.Contains("float Health; // 0x0100 (0x0004) FloatProperty", header);
    }

    [Fact]
    public void BitfieldRunIsUnaffected()
    {
        var fields = new List<LiveFieldValue>
        {
            new() { Name = "bFlagA", TypeName = "BoolProperty", Offset = 0x10, Size = 1, BoolFieldMask = 0x01 },
            new() { Name = "bFlagB", TypeName = "BoolProperty", Offset = 0x10, Size = 1, BoolFieldMask = 0x02 },
        };

        var header = SdkExportService.GenerateClassHeader("AActor", "", 0x18, fields);

        Assert.Contains("uint8_t bFlagA : 1;", header);
        Assert.Contains("uint8_t bFlagB : 1;", header);
    }

    // ------------------------------------------------------------------
    // The general oracle + the compile-smoke artifact.
    // ------------------------------------------------------------------

    /// <summary>
    /// A member line, minus its trailing comment, must parse as `TYPE NAME` or `TYPE NAME[0xN]`.
    /// The broken form `uint8_t[0x40] CellBounds` also matches this shape, which is why the type
    /// half is separately required to carry no '[' — that single rule is the whole defect.
    /// </summary>
    private static readonly Regex MemberRe = new(
        @"^(?<type>.+?)\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)(?<arr>\[0x[0-9A-Fa-f]+\])?$",
        RegexOptions.CultureInvariant);

    private static List<string> BadDeclarators(string header)
    {
        var bad = new List<string>();
        foreach (var raw in header.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (!line.StartsWith("    ", StringComparison.Ordinal)) continue;

            int semi = line.IndexOf(';');
            if (semi < 0) continue;

            var decl = line.Substring(4, semi - 4).Trim();
            if (decl.Length == 0) { bad.Add(line); continue; }

            // `uint8_t bX : 1` / `uint8_t : 3` — bitfields have their own grammar.
            int colon = decl.IndexOf(':');
            if (colon >= 0)
            {
                var width = decl[(colon + 1)..].Trim();
                if (!int.TryParse(width, out int bits) || bits < 1 || bits > 8) bad.Add(line);
                continue;
            }

            var m = MemberRe.Match(decl);
            if (!m.Success || m.Groups["type"].Value.Contains('[')) bad.Add(line);
        }
        return bad;
    }

    [Fact]
    public void EveryMemberDeclaratorIsWellFormed()
    {
        var header = BuildSmokeHeader();

        var bad = BadDeclarators(header);
        Assert.True(bad.Count == 0,
            "Malformed C++ declarations in the generated header:\n  " + string.Join("\n  ", bad));

        // The oracle must be able to fail, or it proves nothing (working-lessons §1).
        var sabotaged = header.Replace("uint8_t CellBounds[0x40];", "uint8_t[0x40] CellBounds;");
        Assert.NotEqual(header, sabotaged);            // the fixture really does contain that line
        Assert.Single(BadDeclarators(sabotaged));
    }

    [Fact]
    public void WritesTheCompileSmokeArtifact()
    {
        // Best effort: the assertion above is what gates the build. This drops a self-contained
        // translation unit for tools/verify/compile_sdk_header.py, which is the only thing in the
        // repo that puts a generated header in front of a real compiler. The rig fails loudly if
        // the file is missing or older than the emitter, so a silent no-write cannot pass as a run.
        var root = FindRepoRoot();
        if (root is null) return;

        var path = Path.Combine(root, "out", "sdk-smoke", "sdk_smoke.cpp");
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, CompileUnitPrelude + BuildSmokeHeader(), new UTF8Encoding(false));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // A read-only or sandboxed checkout must not turn a convenience artifact into a red
            // test. Nothing downstream can mistake this for a pass: the rig treats a missing OR
            // stale artifact as a hard failure.
            return;
        }

        Assert.True(File.Exists(path));
    }

    /// <summary>
    /// One header exercising every emitter branch a real export hits: both raw-byte fallbacks,
    /// a resolved struct, containers, pointers, an enum, a packed bitfield run, a native bool,
    /// interior padding and tail padding — through BOTH entry points (schema and live).
    /// </summary>
    private static string BuildSmokeHeader()
    {
        var schema = new ClassInfoModel
        {
            Name = "ASdkSmokeActor",
            FullPath = "/Script/SdkSmoke.SdkSmokeActor",
            SuperName = "AActor",
            PropertiesSize = 0x140,
            Fields =
            {
                new FieldInfoModel { Name = "Health", TypeName = "FloatProperty", Offset = 0x00, Size = 4 },
                new FieldInfoModel { Name = "Level", TypeName = "IntProperty", Offset = 0x04, Size = 4 },
                // 0x08..0x0F is a gap → interior padding
                new FieldInfoModel { Name = "Location", TypeName = "StructProperty", StructType = "FVector", Offset = 0x10, Size = 0x18 },
                new FieldInfoModel { Name = "CellBounds", TypeName = "OptionalProperty", Offset = 0x28, Size = 0x40 },
                new FieldInfoModel { Name = "Blob", TypeName = "StructProperty", Offset = 0x68, Size = 0xC },
                new FieldInfoModel { Name = "Tag", TypeName = "NameProperty", Offset = 0x78, Size = 8 },
                new FieldInfoModel { Name = "Label", TypeName = "StrProperty", Offset = 0x80, Size = 0x10 },
                new FieldInfoModel { Name = "Owner", TypeName = "ObjectProperty", ObjClassName = "APlayerController", Offset = 0x90, Size = 8 },
                new FieldInfoModel { Name = "SpawnClass", TypeName = "ClassProperty", ObjClassName = "AActor", Offset = 0x98, Size = 8 },
                new FieldInfoModel { Name = "Watched", TypeName = "WeakObjectProperty", ObjClassName = "AActor", Offset = 0xA0, Size = 8 },
                new FieldInfoModel
                {
                    Name = "OwnedComponents", TypeName = "ArrayProperty",
                    InnerType = "ObjectProperty", InnerObjClass = "UActorComponent",
                    Offset = 0xA8, Size = 0x10,
                },
                new FieldInfoModel
                {
                    Name = "Scores", TypeName = "MapProperty",
                    KeyType = "StrProperty", ValueType = "IntProperty",
                    Offset = 0xB8, Size = 0x50,
                },
                new FieldInfoModel { Name = "MoveMode", TypeName = "EnumProperty", EnumName = "EMovementMode", Offset = 0x108, Size = 1 },
                new FieldInfoModel { Name = "bFlagA", TypeName = "BoolProperty", Offset = 0x109, Size = 1, BoolFieldMask = 0x01 },
                new FieldInfoModel { Name = "bFlagB", TypeName = "BoolProperty", Offset = 0x109, Size = 1, BoolFieldMask = 0x04 },
                new FieldInfoModel { Name = "bPlainBool", TypeName = "BoolProperty", Offset = 0x10A, Size = 1, BoolFieldMask = 0xFF },
                // tail gap to 0x140 → tail padding
            },
        };

        var live = new List<LiveFieldValue>
        {
            new() { Name = "Opt_Int_Set", TypeName = "OptionalProperty", Offset = 0x00, Size = 8 },
            new() { Name = "Xform", TypeName = "StructProperty", StructTypeName = "FTransform", Offset = 0x10, Size = 0x60 },
            new()
            {
                Name = "Hits", TypeName = "ArrayProperty",
                ArrayInnerType = "StructProperty", ArrayStructType = "FHitResult",
                Offset = 0x70, Size = 0x10,
            },
        };

        return SdkExportService.GenerateClassHeaderFromSchema(schema)
             + "\n"
             + SdkExportService.GenerateClassHeader("FSdkSmokeStruct", "", 0x90, live);
    }

    /// <summary>
    /// Minimal UE-shaped stubs so the generated header is a complete translation unit with NO
    /// standard includes — that lets the rig run <c>cl /Zs</c> without a Visual Studio dev shell.
    /// </summary>
    private const string CompileUnitPrelude = """
// Generated by SdkHeaderDeclaratorTests.WritesTheCompileSmokeArtifact - do not edit.
// Compile with: py tools/verify/compile_sdk_header.py
typedef unsigned char      uint8_t;
typedef signed char        int8_t;
typedef short              int16_t;
typedef unsigned short     uint16_t;
typedef int                int32_t;
typedef unsigned int       uint32_t;
typedef long long          int64_t;
typedef unsigned long long uint64_t;

struct FName { int32_t Index; int32_t Number; };
struct FString { void* Data; int32_t Num; int32_t Max; };
struct FUtf8String { void* Data; int32_t Num; int32_t Max; };
struct FAnsiString { void* Data; int32_t Num; int32_t Max; };
struct FText { void* Data; };
struct FScriptDelegate { void* Obj; FName Name; };
struct FMulticastScriptDelegate { void* Data; };
struct FMulticastInlineDelegate { void* Data; };
struct FMulticastSparseDelegate { void* Data; };
struct FFieldPath { void* Data; };
struct FVector { double X, Y, Z; };
struct FTransform { double Data[16]; };
struct FHitResult { void* Data; };

template<class T> struct TArray { T* Data; int32_t Num; int32_t Max; };
template<class K, class V> struct TMap { void* Data; };
template<class T> struct TSet { void* Data; };
template<class T> struct TSubclassOf { void* Ptr; };
template<class T> struct TWeakObjectPtr { int32_t Index; int32_t Serial; };
template<class T> struct TSoftObjectPtr { void* Ptr; };
template<class T> struct TSoftClassPtr { void* Ptr; };
template<class T> struct TLazyObjectPtr { void* Ptr; };
template<class T> struct TScriptInterface { void* Obj; void* Iface; };

class UObject { public: void* VTablePtr; };
class UClass : public UObject { public: void* ClassData; };
class AActor : public UObject { public: void* ActorData[4]; };
class UActorComponent : public UObject { public: void* ComponentData; };
class APlayerController : public AActor { public: void* ControllerData; };
class USceneComponent : public UActorComponent { public: void* SceneData; };

enum class EMovementMode : uint8_t { None = 0, Walking = 1 };


"""; // closing delimiter at column 0 on purpose: the prelude lines are not indented

    /// <summary>Walk up from the test binary to the repo root (the folder holding build.ps1 + docs).</summary>
    private static string? FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (int i = 0; i < 10 && dir is not null; i++, dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "build.ps1"))
                && Directory.Exists(Path.Combine(dir.FullName, "docs")))
                return dir.FullName;
        }
        return null;
    }
}
