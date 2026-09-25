using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using UE5DumpUI.Core;
using UE5DumpUI.Models;
using UE5DumpUI.Services;
using UE5DumpUI.ViewModels;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// [PATH-CE-MODULE-VIEW] Cheat Engine never sees a module's Unicode name. Its symbol handler names a module
/// <c>WinCPToUTF8(szModule)</c> from ANSI <c>Module32First</c>, and Lua <c>enumModules</c> / <c>process</c> hand
/// out the raw ANSI bytes. Measured on this PC (ACP 950) against ANSI <c>Module32First</c> itself, which gives
/// exactly <c>WideCharToMultiByte(CP_ACP, 0)</c>, best fit included: <c>遊戲-…</c> keeps its name (Big5 holds it),
/// <c>ゲーム-…</c> is <c>???-…</c>, <c>Game™-…</c> is <c>Game?-…</c>, <c>Café-…</c> is <c>Cafe-…</c>,
/// <c>Game®-…</c> is <c>GameR-…</c>. Every string the UI hands to CE must use THAT name.
/// </summary>
public class CeModuleViewTests
{
    // ── the conversion itself, on explicit code pages so the test does not depend on the machine's ACP ──

    [Theory]
    [InlineData("遊戲-Win64-Shipping.exe", 950u, "遊戲-Win64-Shipping.exe")]    // Big5 holds it
    [InlineData("ゲーム-Win64-Shipping.exe", 950u, "???-Win64-Shipping.exe")]   // kana: not in Big5
    [InlineData("Game™-Win64-Shipping.exe", 950u, "Game?-Win64-Shipping.exe")]
    [InlineData("Café-Win64-Shipping.exe", 950u, "Cafe-Win64-Shipping.exe")]    // best fit, as Module32First
    [InlineData("Game®-Win64-Shipping.exe", 950u, "GameR-Win64-Shipping.exe")]
    [InlineData("Tony's-Win64-Shipping.exe", 950u, "Tony's-Win64-Shipping.exe")]
    [InlineData("Café-Win64-Shipping.exe", 1252u, "Café-Win64-Shipping.exe")]   // 1252 holds é
    [InlineData("Game™-Win64-Shipping.exe", 1252u, "Game™-Win64-Shipping.exe")] // and ™ (0x99)
    [InlineData("遊戲-Win64-Shipping.exe", 1252u, "??-Win64-Shipping.exe")]
    [InlineData("", 950u, "")]
    public void AnsiView_IsTheAnsiRoundTrip_BestFitIncluded(string name, uint codePage, string expected)
    {
        Assert.Equal(expected, WindowsSystemCodePage.AnsiView(name, codePage));
    }

    // ── where the name comes from ──

    [Fact]
    public async Task DumpService_SetsCeModuleName_FromTheSystemCodePage()
    {
        var pipe = new MockPipeClient();
        pipe.SetHandler(req => req["cmd"]?.GetValue<string>() == "get_pointers"
            ? new JsonObject { ["ok"] = true, ["module_name"] = "Game™-Win64-Shipping.exe" }
            : new JsonObject { ["ok"] = true });
        var svc = new DumpService(pipe, new MockLoggingService(), new FixedCodePage(n => n.Replace('™', '?')));

        var state = await svc.InitAsync(TestContext.Current.CancellationToken);

        Assert.Equal("Game™-Win64-Shipping.exe", state.ModuleName);        // identity: the real name
        Assert.Equal("Game?-Win64-Shipping.exe", state.CeModuleName);      // what CE calls it
    }

    [Fact]
    public void EngineState_WithoutACeName_FallsBackToTheModuleName()
    {
        // Every EngineState built without the service (tests, an older path) keeps today's behaviour.
        var s = new EngineState { ModuleName = "Game-Win64-Shipping.exe" };
        Assert.Equal("Game-Win64-Shipping.exe", s.CeModuleName);
    }

    // ── the consumers ──

    [Fact]
    public async Task ObjectTree_CopyAddress_ModuleOffset_UsesCesNameForTheModule()
    {
        var platform = new MockPlatformService(Path.GetTempPath());
        var vm = new ObjectTreeViewModel(new StubDumpService(), new MockLoggingService(), platform);
        vm.SetEngineState(new EngineState
        {
            ModuleName = "Game™-Win64-Shipping.exe",
            CeModuleName = "Game?-Win64-Shipping.exe",
            ModuleBase = "0x7FF600000000",
        });
        vm.SelectedAddressFormatIndex = (int)AddressFormat.ModuleOffset;

        await vm.CopyAddressCommand.ExecuteAsync(new UObjectNode { Address = "0x7FF600001234", Name = "X" });

        Assert.Equal("\"Game?-Win64-Shipping.exe\"+1234", platform.LastClipboard);
    }

    [Fact]
    public void EveryCeFacingModuleString_InTheViewModels_UsesCeModuleName()
    {
        // Pinned by source: a new "module"+RVA call site added with .ModuleName would silently hand CE a name it
        // does not know. FormatAddress / the AA define / the AOBMaker symbol script / the GWorld-walk export all
        // go to CE.
        string dir = Path.Combine(RepoRoot(), "ui", "UE5DumpUI", "ViewModels");
        var offenders = Directory.EnumerateFiles(dir, "*.cs")
            .SelectMany(f => Regex.Matches(File.ReadAllText(f),
                    @"FormatAddress\(\s*[^;]*?_engineState\??\.ModuleName|\\""\{_engineState\.ModuleName\}\\""")
                .Select(m => $"{Path.GetFileName(f)}: {m.Value}"))
            .ToList();
        Assert.Empty(offenders);

        string walker = File.ReadAllText(Path.Combine(dir, "LiveWalkerViewModel.cs"));
        Assert.DoesNotMatch(@"GWorldAobLen,\s*_engineState\.ModuleName", walker);

        string pointer = File.ReadAllText(Path.Combine(dir, "PointerPanelViewModel.cs"));
        Assert.DoesNotContain("string module = !string.IsNullOrEmpty(ModuleName) ? ModuleName", pointer);
    }

    [Fact]
    public void Trainer_ScansCesOwnMainModule_NeverTheBakedName()
    {
        // CE's `process` and enumModules()' Name are the SAME ANSI bytes; a baked UTF-8 name is not.
        var setup = StandaloneTrainerScriptGenerator.Generate(new TrainerOffsets
        {
            Chain = { new TrainerChainHop { Field = "OwningGameInstance", Offset = 0x1B8, Deref = true } },
            Module = "遊戲-Win64-Shipping.exe", GWorldAob = "48 8B 1D ?? ?? ?? ??", GWorldAobPos = 3, GWorldAobLen = 7,
        })[0].Script;

        Assert.Contains("local mod = process", setup);
        Assert.DoesNotContain("local mod = UE5T.module", setup);
        // No process opened: say so and untick, instead of string.lower(nil) inside the helper.
        Assert.Matches(@"if mod == nil or mod == '' then\s+showMessage\('\[UE5 Trainer\][^']*[Aa]ttach", setup);
    }

    [Fact]
    public void AobScanHelpers_MatchAModuleNameGivenInEitherEncoding()
    {
        // enumModules' Name is ANSI bytes; a caller that holds CE's UTF-8 name (the symbol handler's) must still
        // match. Both copies of the helper (trainer + GWorld-walk export) stay identical.
        var trainer = StandaloneTrainerScriptGenerator.Generate(new TrainerOffsets
        {
            Chain = { new TrainerChainHop { Field = "OwningGameInstance", Offset = 0x1B8, Deref = true } },
            GWorldAob = "48 8B 1D ?? ?? ?? ??", GWorldAobPos = 3, GWorldAobLen = 7,
        })[0].Script;
        Assert.Contains("ansiToUTF8(mod.Name)", trainer);

        string export = File.ReadAllText(Path.Combine(RepoRoot(), "ui", "UE5DumpUI", "Services", "CeXmlExportService.cs"));
        Assert.Contains("ansiToUTF8(mod.Name)", export);
    }

    private sealed class FixedCodePage(Func<string, string> map) : ISystemCodePage
    {
        public string AnsiView(string text) => map(text);
    }

    private static string RepoRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !File.Exists(Path.Combine(d.FullName, "build.ps1"))) d = d.Parent;
        return d?.FullName ?? throw new DirectoryNotFoundException("repo root");
    }
}
