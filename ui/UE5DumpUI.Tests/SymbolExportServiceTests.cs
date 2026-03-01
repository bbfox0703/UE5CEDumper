using UE5DumpUI.Models;
using UE5DumpUI.Services;
using Xunit;

namespace UE5DumpUI.Tests;

public class SymbolExportServiceTests
{
    private static List<SymbolEntry> CreateTestSymbols() =>
    [
        new SymbolEntry
        {
            Address = "0x7FF612345678",
            ModuleRelative = "0x1A2B3C0",
            Name = "GObjects",
            ClassName = "FUObjectArray",
            Category = "Object",
        },
        new SymbolEntry
        {
            Address = "0x7FF612340000",
            ModuleRelative = "0x1A27348",
            Name = "AActor",
            ClassName = "Class",
            Category = "Class",
        },
        new SymbolEntry
        {
            Address = "0x7FF612350000",
            ModuleRelative = "0x1A37348",
            Name = "EGameMode",
            ClassName = "Enum",
            Category = "Enum",
        },
        new SymbolEntry
        {
            Address = "0x7FF612360000",
            ModuleRelative = "0x1A47348",
            Name = "BeginPlay",
            ClassName = "Function",
            Category = "Function",
        },
    ];

    // --- x64dbg ---

    [Fact]
    public void GenerateX64dbg_EmitsLabelsJson()
    {
        var symbols = CreateTestSymbols();
        var json = SymbolExportService.GenerateX64dbgDatabase(symbols, "game.exe");

        Assert.Contains("\"labels\"", json);
        Assert.Contains("\"module\": \"game.exe\"", json);
        Assert.Contains("\"text\": \"GObjects\"", json);
        Assert.Contains("\"text\": \"AActor\"", json);
        Assert.Contains("\"manual\": true", json);
    }

    [Fact]
    public void GenerateX64dbg_UsesModuleRelativeAddresses()
    {
        var symbols = CreateTestSymbols();
        var json = SymbolExportService.GenerateX64dbgDatabase(symbols, "game.exe");

        // Module-relative address without "0x" prefix
        Assert.Contains("\"address\": \"1A2B3C0\"", json);
        Assert.Contains("\"address\": \"1A27348\"", json);
    }

    [Fact]
    public void GenerateX64dbg_EmitsCommentsForNonGenericObjects()
    {
        var symbols = CreateTestSymbols();
        var json = SymbolExportService.GenerateX64dbgDatabase(symbols, "game.exe");

        Assert.Contains("\"comments\"", json);
        // Class/Enum/Function should have comments
        Assert.Contains("\"text\": \"Class\"", json);
        Assert.Contains("\"text\": \"Enum\"", json);
        Assert.Contains("\"text\": \"Function\"", json);
        // "Object" category should NOT have comments (FUObjectArray is "Object")
        Assert.DoesNotContain("\"text\": \"FUObjectArray\"", json);
    }

    [Fact]
    public void GenerateX64dbg_EmptyList_EmitsEmptyLabels()
    {
        var json = SymbolExportService.GenerateX64dbgDatabase([], "game.exe");

        Assert.Contains("\"labels\"", json);
        Assert.Contains("\"comments\"", json);
        // Ensure arrays are empty
        Assert.DoesNotContain("\"text\"", json);
    }

    // --- Ghidra ---

    [Fact]
    public void GenerateGhidra_EmitsLabelAddressPairs()
    {
        var symbols = CreateTestSymbols();
        var text = SymbolExportService.GenerateGhidraSymbols(symbols);

        Assert.Contains("GObjects 0x7FF612345678 l", text);
        Assert.Contains("AActor 0x7FF612340000 l", text);
        Assert.Contains("EGameMode 0x7FF612350000 l", text);
    }

    [Fact]
    public void GenerateGhidra_FunctionType_EmitsF()
    {
        var symbols = CreateTestSymbols();
        var text = SymbolExportService.GenerateGhidraSymbols(symbols);

        // Function category should use 'f' type
        Assert.Contains("BeginPlay 0x7FF612360000 f", text);
    }

    [Fact]
    public void GenerateGhidra_HasHeader()
    {
        var text = SymbolExportService.GenerateGhidraSymbols([]);
        Assert.Contains("UE5CEDumper", text);
        Assert.Contains("Ghidra", text);
    }

    // --- IDA ---

    [Fact]
    public void GenerateIda_EmitsIdcScript()
    {
        var symbols = CreateTestSymbols();
        var idc = SymbolExportService.GenerateIdaScript(symbols);

        Assert.Contains("#include <idc.idc>", idc);
        Assert.Contains("static main()", idc);
        Assert.Contains("MakeName(0x7FF612345678, \"GObjects\")", idc);
        Assert.Contains("MakeName(0x7FF612340000, \"AActor\")", idc);
    }

    [Fact]
    public void GenerateIda_EmitsMakeComm_ForNonGenericObjects()
    {
        var symbols = CreateTestSymbols();
        var idc = SymbolExportService.GenerateIdaScript(symbols);

        // Class/Enum/Function should have MakeComm
        Assert.Contains("MakeComm(0x7FF612340000, \"Class\")", idc);
        Assert.Contains("MakeComm(0x7FF612350000, \"Enum\")", idc);
        // "Object" category should NOT have MakeComm
        Assert.DoesNotContain("MakeComm(0x7FF612345678", idc);
    }

    [Fact]
    public void GenerateIda_EscapesQuotesInNames()
    {
        var symbols = new List<SymbolEntry>
        {
            new()
            {
                Address = "0x100",
                ModuleRelative = "0x100",
                Name = "Name_With\"Quotes",
                ClassName = "",
                Category = "Object",
            },
        };
        var idc = SymbolExportService.GenerateIdaScript(symbols);

        // Quotes should be escaped with backslash
        Assert.Contains("\\\"", idc);
        // The raw unescaped sequence should not appear
        Assert.DoesNotContain("Name_With\"Quotes", idc);
    }

    // --- SanitizeLabel ---

    [Theory]
    [InlineData("AActor", "AActor")]
    [InlineData("BP_Player Character", "BP_Player_Character")]
    [InlineData("/Script/Engine.AActor", "_Script_Engine.AActor")]
    [InlineData("Name<With>Angles", "Name_With_Angles")]
    [InlineData("", "unknown")]
    public void SanitizeLabel_CorrectlyTransforms(string input, string expected)
    {
        Assert.Equal(expected, SymbolExportService.SanitizeLabel(input));
    }
}
