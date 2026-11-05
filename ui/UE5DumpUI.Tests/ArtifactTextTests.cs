using System.Reflection;
using UE5DumpUI.Models;
using UE5DumpUI.Services;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// [I18N-READINESS-2026-11-04] P4. What the tool WRITES -- a CE table, a Lua script, an XML / CSX / SDK / USMAP file --
/// and the names other programs look things up by (a CE record's Description, a CE group, a CT category) are artifact
/// text, not UI text. Taken from the string dictionary, they would come out in whatever language the UI had at export
/// time: a .CT saved under one language would read differently under another, a record found by its Description would
/// not be found, and an apostrophe in a translation would break the Lua that quotes it. They stay English constants;
/// the UI shows its own, translatable copy where it shows the same fact.
/// </summary>
public class ArtifactTextTests
{
    private static string Ui(params string[] parts)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            var c = Path.Combine(new[] { dir.FullName, "ui", "UE5DumpUI" }.Concat(parts).ToArray());
            if (File.Exists(c)) return File.ReadAllText(c);
        }
        throw new FileNotFoundException(string.Join("/", parts) + " not found above " + AppContext.BaseDirectory);
    }

    private static string CodeOnly(string s) => string.Join('\n', s.Split('\n').Select(l =>
    {
        int c = l.IndexOf("//", StringComparison.Ordinal);
        return c >= 0 ? l[..c] : l;
    }));

    /// <summary>The enum note a CE XML export writes as a comment is an English constant; only the status line's copy
    /// comes from en.axaml.</summary>
    [Fact]
    public void A_CE_XML_exports_enum_note_is_an_English_constant()
    {
        var field = typeof(CeXmlExportService).GetField("EnumNamesUnavailableExportNote",
            BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static);
        Assert.True(field is { IsLiteral: true }, "CeXmlExportService.EnumNamesUnavailableExportNote is not a const");
        var constant = (string)field!.GetRawConstantValue()!;
        var enumField = new LiveFieldValue { Name = "Mode", TypeName = "EnumProperty", Offset = 0, Size = 1 };
        Assert.Equal(constant, CeXmlExportService.EnumNamesNote(true, new[] { enumField }));
        // The status line still names it, from the dictionary.
        Assert.Equal(" ⚠ " + CeXmlExportService.EnumNamesUnavailableNote, CeXmlExportService.EnumNamesStatus(new[] { constant }));
    }

    /// <summary>No script generator or exporter reads the string dictionary for what it writes. CeXmlExportService is the
    /// one exporter that also feeds a status line, and is held to its export note by the test above.</summary>
    [Fact]
    public void No_generator_or_exporter_reads_the_string_dictionary()
    {
        var services = Directory.GetFiles(Path.GetDirectoryName(FindUi("Services", "CsxExportService.cs"))!, "*.cs")
            .Where(p => p.EndsWith("ScriptGenerator.cs", StringComparison.Ordinal)
                        || Path.GetFileName(p) is "CsxExportService.cs" or "SdkExportService.cs" or "UsmapExportService.cs"
                            or "SymbolExportService.cs" or "CheatTableBuilder.cs")
            .ToList();
        Assert.True(services.Count >= 15, $"only {services.Count} generator / exporter files found");
        foreach (var p in services)
        {
            var code = CodeOnly(File.ReadAllText(p));
            foreach (var call in new[] { "Res.Get(", "Res.Format(", "Status.Say(", "Text.Say(" })
                Assert.False(code.Contains(call, StringComparison.Ordinal), $"{Path.GetFileName(p)} calls {call}");
        }
    }

    /// <summary>The standalone trainer looks a CT record up by its Description inside a Lua string literal: the
    /// description goes through the Lua escaper, as every other quoted value in the generators does.</summary>
    [Fact]
    public void The_trainers_untick_escapes_the_description_it_quotes()
    {
        var code = CodeOnly(Ui("Services", "StandaloneTrainerScriptGenerator.cs"));
        Assert.Contains("getMemoryRecordByDescription('{CeLuaHygiene.EscapeLuaString(desc)}')", code, StringComparison.Ordinal);
    }

    /// <summary>A generated CT groups its records by category name: the CT's own group, not the chip's label, so a
    /// translated chip does not rename the groups of a table the user saved.</summary>
    [Theory]
    [InlineData("InterestingFunctionsViewModel.cs", "KeywordScoringTable.CtGroupName(sr.Category)")]
    [InlineData("InterestingPropertiesViewModel.cs", "PropertyScoringTable.CtGroupName(sr.Category)")]
    public void A_generated_CTs_categories_are_group_names_not_chip_labels(string file, string call)
        => Assert.Contains(call, CodeOnly(Ui("ViewModels", file)), StringComparison.Ordinal);

    private static string FindUi(params string[] parts)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            var c = Path.Combine(new[] { dir.FullName, "ui", "UE5DumpUI" }.Concat(parts).ToArray());
            if (File.Exists(c)) return c;
        }
        throw new FileNotFoundException(string.Join("/", parts) + " not found above " + AppContext.BaseDirectory);
    }
}
