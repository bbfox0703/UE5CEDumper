using UE5DumpUI.Models;
using UE5DumpUI.Services;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// A UE property name is not a C++ identifier, and the SDK header emitted it verbatim.
///
/// <para>Each case below is a header <c>cl.exe</c> rejects (measured with
/// <c>tools/verify/compile_sdk_header.py</c>): a keyword (<c>class</c>, <c>default</c>), two members
/// with one name, a member that takes a generated padding name (C2040), a member named after a type
/// the same struct spells without <c>struct</c>/<c>class</c> in front (C2327), and a Blueprint
/// variable with a space in it. <c>StaticClass</c> compiles on its own, but hides the static
/// function every UE SDK base declares, so <c>T::StaticClass()</c> stops compiling at the use site.
/// Renames follow Dumper-7's shape: the first holder keeps the name, the next gets <c>_0</c>.</para>
/// </summary>
public class SdkMemberNameTests
{
    private static FieldInfoModel F(string name, string type, int offset, int size) =>
        new() { Name = name, TypeName = type, Offset = offset, Size = size };

    private static string Schema(int propsSize, params FieldInfoModel[] fields)
    {
        var info = new ClassInfoModel { Name = "FClash", SuperName = "", PropertiesSize = propsSize };
        info.Fields.AddRange(fields);
        return SdkExportService.GenerateClassHeaderFromSchema(info);
    }

    /// <summary>Every declared member name, bitfields included, in emission order — without the
    /// generated padding, which the padding test checks by its exact line instead.</summary>
    private static List<string> DeclaredNames(string header)
    {
        var names = new List<string>();
        foreach (var raw in header.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (!line.StartsWith("    ", StringComparison.Ordinal)) continue;
            if (line.EndsWith(") PADDING", StringComparison.Ordinal)) continue;
            int semi = line.IndexOf(';');
            if (semi < 0) continue;
            var decl = line[4..semi].Trim();

            int colon = decl.IndexOf(':');
            if (colon >= 0) decl = decl[..colon].Trim();      // `uint8_t bX : 1` / `uint8_t : 3`
            int bracket = decl.IndexOf('[');
            if (bracket >= 0) decl = decl[..bracket].Trim();  // `uint8_t Pad_0008[0x0008]`

            int space = decl.LastIndexOf(' ');
            var name = space >= 0 ? decl[(space + 1)..] : "";
            if (name.Length > 0) names.Add(name);  // an unnamed bitfield filler has no name
        }
        return names;
    }

    [Fact]
    public void CppKeywords_AreRenamed()
    {
        var header = Schema(0x10,
            F("class", "IntProperty", 0x0, 4),
            F("default", "FloatProperty", 0x4, 4),
            F("delete", "IntProperty", 0x8, 4),
            F("NULL", "IntProperty", 0xC, 4));

        Assert.Equal(new[] { "class_0", "default_0", "delete_0", "NULL_0" }, DeclaredNames(header));
    }

    [Fact]
    public void UeSdkFunctionNames_AreRenamed()
    {
        var header = Schema(0x0C,
            F("StaticClass", "IntProperty", 0x0, 4),
            F("StaticName", "IntProperty", 0x4, 4),
            F("GetDefaultObj", "IntProperty", 0x8, 4));

        Assert.Equal(new[] { "StaticClass_0", "StaticName_0", "GetDefaultObj_0" }, DeclaredNames(header));
    }

    [Fact]
    public void UObjectMemberNames_AreLeftAlone()
    {
        // Dumper-7 renames these on CLASSES only, because its SDK emits a UObject base holding
        // them. This header emits no such base, and hiding a base member compiles, so renaming
        // every struct's `Name` would rename thousands of members to protect nothing.
        var header = Schema(0x14,
            F("Name", "NameProperty", 0x0, 8),
            F("Class", "IntProperty", 0x8, 4),
            F("Flags", "IntProperty", 0xC, 4),
            F("Outer", "IntProperty", 0x10, 4));

        Assert.Equal(new[] { "Name", "Class", "Flags", "Outer" }, DeclaredNames(header));
    }

    [Fact]
    public void DuplicateMembers_SecondGetsSuffix()
    {
        var header = Schema(0x08,
            F("Value", "IntProperty", 0x0, 4),
            F("Value", "IntProperty", 0x4, 4));

        Assert.Equal(new[] { "Value", "Value_0" }, DeclaredNames(header));
    }

    [Fact]
    public void Renames_NeverTakeARealMembersName()
    {
        // `Value_0` is a real UE name further down; the duplicate must skip past it, and a
        // renamed keyword must skip past `class_0` the same way.
        var header = Schema(0x14,
            F("Value", "IntProperty", 0x0, 4),
            F("Value", "IntProperty", 0x4, 4),
            F("Value_0", "IntProperty", 0x8, 4),
            F("class", "IntProperty", 0xC, 4),
            F("class_0", "IntProperty", 0x10, 4));

        var names = DeclaredNames(header);
        Assert.Equal(new[] { "Value", "Value_1", "Value_0", "class_1", "class_0" }, names);
        Assert.Equal(names.Count, names.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void NonIdentifierCharacters_BecomeUnderscores()
    {
        // A Blueprint variable keeps the space it was typed with. The member whose UE name is
        // already `My_Var` keeps it; the one that only became `My_Var` by sanitising is suffixed.
        var header = Schema(0x14,
            F("Max Health", "FloatProperty", 0x0, 4),
            F("2ndSlot", "IntProperty", 0x4, 4),
            F("My Var", "IntProperty", 0x8, 4),
            F("My_Var", "IntProperty", 0xC, 4),
            F("体力", "IntProperty", 0x10, 4));

        Assert.Equal(new[] { "Max_Health", "_2ndSlot", "My_Var_0", "My_Var", "体力" }, DeclaredNames(header));
    }

    [Fact]
    public void EmptyName_GetsAPlaceholder()
    {
        var header = Schema(0x04, F("", "IntProperty", 0x0, 4));
        Assert.Equal(new[] { "Unnamed" }, DeclaredNames(header));
    }

    [Fact]
    public void MemberNamedAfterATypeTheStructSpells_IsRenamed()
    {
        // C2327 on MSVC when the type is used AFTER the member; GCC/Clang also reject it when used
        // before ("changes meaning"). `struct FVector` is an elaborated specifier, whose lookup
        // skips a data member, so that one is not a clash and keeps its name.
        var header = Schema(0x30,
            F("FName", "IntProperty", 0x0, 4),
            F("Tag", "NameProperty", 0x8, 8),
            F("EMode", "IntProperty", 0x10, 4),
            new FieldInfoModel { Name = "Mode", TypeName = "EnumProperty", EnumName = "EMode", Offset = 0x14, Size = 1 },
            F("FVector", "IntProperty", 0x18, 4),
            new FieldInfoModel { Name = "Loc", TypeName = "StructProperty", StructType = "FVector", Offset = 0x1C, Size = 0x14 });

        Assert.Equal(new[] { "FName_0", "Tag", "EMode_0", "Mode", "FVector", "Loc" }, DeclaredNames(header));
    }

    [Fact]
    public void MemberNamedLikeGeneratedPadding_IsRenamed()
    {
        // 0x08..0x10 is a gap, so the emitter writes `uint8_t Pad_0008[0x0008];` (C2040 on a clash).
        var header = Schema(0x14,
            F("Big", "Int64Property", 0x0, 8),
            F("Pad_0008", "IntProperty", 0x10, 4));

        Assert.Contains("uint8_t Pad_0008[0x0008];", header);
        Assert.Contains("int32_t Pad_0008_0;", header);
    }

    [Fact]
    public void PackedBitfieldNames_AreRenamedToo()
    {
        var header = Schema(0x01,
            new FieldInfoModel { Name = "delete", TypeName = "BoolProperty", Offset = 0, Size = 1, BoolFieldMask = 0x01 },
            new FieldInfoModel { Name = "bOk", TypeName = "BoolProperty", Offset = 0, Size = 1, BoolFieldMask = 0x02 },
            new FieldInfoModel { Name = "bOk", TypeName = "BoolProperty", Offset = 0, Size = 1, BoolFieldMask = 0x04 });

        Assert.Contains("uint8_t delete_0 : 1;", header);
        Assert.Equal(new[] { "delete_0", "bOk", "bOk_0" }, DeclaredNames(header));
    }

    [Fact]
    public void LiveWalkerPath_IsRenamedToo()
    {
        // LiveFieldValue is a second metadata shape into the same emitter.
        var fields = new List<LiveFieldValue>
        {
            new() { Name = "class", TypeName = "IntProperty", Offset = 0x0, Size = 4 },
            new() { Name = "Value", TypeName = "IntProperty", Offset = 0x4, Size = 4 },
            new() { Name = "Value", TypeName = "IntProperty", Offset = 0x8, Size = 4 },
        };
        var header = SdkExportService.GenerateClassHeader("FLive", "", 0x0C, fields);

        Assert.Equal(new[] { "class_0", "Value", "Value_0" }, DeclaredNames(header));
    }

    [Fact]
    public void RenamedMember_KeepsItsUeNameInTheComment()
    {
        // Someone searching the header for the property's real name must still find it.
        var header = Schema(0x04, F("class", "IntProperty", 0x0, 4));
        var line = header.Split('\n').Single(l => l.Contains("class_0;"));
        Assert.Contains("UE name: class", line);

        var plain = Schema(0x04, F("Health", "IntProperty", 0x0, 4));
        Assert.DoesNotContain("UE name", plain);
    }
}
