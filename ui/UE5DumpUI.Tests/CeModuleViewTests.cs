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
    // (skeptic MODVIEW-5C-TRAIL, measured) ANSI Module32First cuts the ANSI path at its LAST 0x5C byte, and a DBCS
    // character's trail byte can be 0x5C: Big5 功 = A5 5C, Shift-JIS ソ = 83 5C. CE never sees what precedes it.
    [InlineData("功夫-Win64-Shipping.exe", 950u, "夫-Win64-Shipping.exe")]
    [InlineData("ソード-Win64-Shipping.exe", 932u, "ード-Win64-Shipping.exe")]
    [InlineData("遊功-Win64-Shipping.exe", 950u, "-Win64-Shipping.exe")]
    public void AnsiModuleName_IsWhatAnsiModule32FirstGives(string name, uint codePage, string expected)
    {
        Assert.Equal(expected, WindowsSystemCodePage.AnsiModuleName(name, codePage));
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
    public void DumpService_TheCodePageIsRequired_AndTheAppPassesTheSystemOne()
    {
        // (skeptic T2) An OPTIONAL code page let a composition root forget it and silently fall back to identity --
        // the Audit #4 B27 shape CompositionRootWiringTests exists for. Required: the compiler finds every site.
        var p = typeof(DumpService).GetConstructors().Single().GetParameters()
            .Single(x => x.ParameterType == typeof(ISystemCodePage));
        Assert.False(p.IsOptional);
        string app = File.ReadAllText(Path.Combine(RepoRoot(), "ui", "UE5DumpUI", "App.axaml.cs"));
        Assert.Contains("new DumpService(_pipeClient, _logging, new WindowsSystemCodePage())", app);
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

        // (skeptic T10) The GWorld-walk export's module argument is unused -- its script scans CE's `process` --
        // so it is not pinned here; its doc says so.

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

    [Fact]
    public async Task PointerPanel_SymbolScripts_HandAobMakerCesName()
    {
        // (skeptic T8) Behavioural, where a negative text pin guarded it before.
        var bridge = new RecordingBridge();
        var vm = new PointerPanelViewModel(new MockPlatformService(Path.GetTempPath()), aobMaker: bridge)
            { IsAobMakerAvailable = true };
        vm.Update(new EngineState
        {
            ModuleName = "ゲーム-Win64-Shipping.exe", CeModuleName = "???-Win64-Shipping.exe",
            GWorldAob = "48 8B 1D ?? ?? ?? ??", GWorldAobPos = 3, GWorldAobLen = 7,
            GEngineAob = "48 8B 0D ?? ?? ?? ??", GEngineAobPos = 3, GEngineAobLen = 7,
        });

        await vm.RegisterGWorldSymbolCommand.ExecuteAsync(null);
        await vm.RegisterGEngineSymbolCommand.ExecuteAsync(null);

        Assert.Equal(new[] { "???-Win64-Shipping.exe", "???-Win64-Shipping.exe" }, bridge.Modules);
    }

    [Fact]
    public void AobScanHelper_OnCesVm_MatchesTheAnsiBytesAndCesUtf8Name()
    {
        // (skeptic T10) The either-encoding clause, RUN on CE's own Lua VM (skips where the host is not built).
        var setup = StandaloneTrainerScriptGenerator.Generate(new TrainerOffsets
        {
            Chain = { new TrainerChainHop { Field = "OwningGameInstance", Offset = 0x1B8, Deref = true } },
            GWorldAob = "48 8B 1D ?? ?? ?? ??", GWorldAobPos = 3, GWorldAobLen = 7,
        })[0].Script;
        var helper = Regex.Match(setup, @"if not AOBScanModuleUE then\n.*?\nend\n", RegexOptions.Singleline).Value;
        Assert.False(string.IsNullOrEmpty(helper), "AOBScanModuleUE helper not found in the trainer");

        const string stubs = """
            local BIG5 = "\185C\192\184-Win64-Shipping.exe"                      -- 遊戲 in Big5 (enumModules' Name)
            local UTF8 = "\233\129\138\230\136\178-Win64-Shipping.exe"        -- 遊戲 in UTF-8 (CE's symbol name)
            function synchronize(f) f() end
            function enumModules() return { { Name = "ntdll.dll", Address = 1, Size = 1 },
                                            { Name = BIG5, Address = 0x1000, Size = 0x100 } } end
            function ansiToUTF8(s) if s == BIG5 then return UTF8 end return s end
            local lastBase
            function createMemScan() return { firstScan = function(self, ...) local a = { ... } lastBase = a[6] end,
              waitTillDone = function() end, destroy = function() end } end
            function createFoundList() return { initialize = function() end, getCount = function() return 1 end,
              destroy = function() end, [0] = "1234" } end
            """;
        const string asserts = """
            assert(AOBScanModuleUE(BIG5, "48") == "1234", "the ANSI bytes (process) did not match")
            assert(AOBScanModuleUE(UTF8, "48") == "1234", "CE's UTF-8 name did not match")
            assert(AOBScanModuleUE("other.exe", "48") == nil, "an unknown module matched")
            ansiToUTF8 = function() return nil end
            assert(AOBScanModuleUE(BIG5, "48") == "1234", "a nil ansiToUTF8 broke the raw match")
            print("OK")
            """;
        var (exit, output) = CeLua53Host.Run(stubs + "\n" + helper + "\n" + asserts);
        Assert.True(exit == 0 && output.Contains("OK"), output);
    }

    [Fact]
    public void FernSendsTheRealName_AndCesViewOnlyWhereCeResolvesIt()
    {
        // (skeptic T3) No test target compiles Fern.cpp, so the red tests could only exercise the helpers. Pin that
        // the three sites USE them and that no '?' narrowing survives.
        string fern = File.ReadAllText(Path.Combine(RepoRoot(), "dll", "src", "Fern.cpp"));
        Assert.DoesNotContain("(wc < 128) ? static_cast<char>(wc) : '?'", fern);
        Assert.DoesNotContain("(wc < 128) ? static_cast<char>(towlower(wc)) : '?'", fern);
        Assert.Contains("data[\"module_name\"] = Utf8Helpers::LeafUtf8(", fern);
        Assert.Contains("Renge::CeModuleRelative(Methode::CeModuleNameUtf8(", fern);
        Assert.DoesNotContain("static std::string AnsiViewUtf8", fern);   // moved to Methode.h, where it is tested
    }

    [Fact]
    public void TheRigsThatPrintModuleName_WriteUtf8()
    {
        // (skeptic RIG-PRINT-NONASCII) With the real non-ASCII name, print() to a cp950 pipe raises
        // UnicodeEncodeError. run_version_evidence.py and sweep_title.py already reconfigure stdout; dumpgate_case3.py
        // did not.
        foreach (var rig in new[] { "run_version_evidence.py", "sweep_title.py", "dumpgate_case3.py" })
            Assert.Contains("sys.stdout.reconfigure(encoding=\"utf-8\"",
                File.ReadAllText(Path.Combine(RepoRoot(), "tools", "verify", rig)));
    }

    private sealed class RecordingBridge : IAobMakerBridge
    {
        public readonly List<string> Modules = new();
        public bool IsAvailable => true;
        public Task<bool> CheckAvailabilityAsync(CancellationToken ct = default) => Task.FromResult(true);
        public Task<(bool Ok, string? ErrorMessage)> InjectTableFileAsync(string fileName, string content,
            CancellationToken ct = default) => Task.FromResult((false, (string?)null));
        public Task<bool> NavigateHexViewAsync(string hexAddress, CancellationToken ct = default) => Task.FromResult(false);
        public Task<bool> NavigateDisassemblerAsync(string hexAddress, CancellationToken ct = default) => Task.FromResult(false);
        public Task<bool> CreateAAScriptAsync(string description, string script, bool autoActivate = true,
            string? group = null, CancellationToken ct = default) => Task.FromResult(false);
        public Task<bool> CreateSymbolScriptAsync(string name, string aob, int pos, int aoblen, string symbol,
            string module, bool autoActivate = true, CancellationToken ct = default)
        { Modules.Add(module); return Task.FromResult(true); }
        public Task<bool> CreateMemoryRecordAsync(string description, string address, int valueType,
            bool isSigned = false, bool showAsHex = false, CancellationToken ct = default) => Task.FromResult(false);
    }

    private sealed class FixedCodePage(Func<string, string> map) : ISystemCodePage
    {
        public string AnsiModuleName(string moduleFile) => map(moduleFile);
    }

    private static string RepoRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !File.Exists(Path.Combine(d.FullName, "build.ps1"))) d = d.Parent;
        return d?.FullName ?? throw new DirectoryNotFoundException("repo root");
    }
}
