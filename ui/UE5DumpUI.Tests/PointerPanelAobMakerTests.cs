using UE5DumpUI.Core;
using UE5DumpUI.Helpers;
using UE5DumpUI.Models;
using UE5DumpUI.ViewModels;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// The Pointer panel's AOBMaker buttons, each section under its finding tag: a GObjects / GNames symbol from
/// AOBMaker.UI's GenerateAob, the scan-hit ASM buttons, and what HEX / ASM report.
/// <para>The symbol is the dangerous one: a plausible, wrong symbol roots every CE record the user builds on it. So the
/// rule under test is that NOTHING is pushed unless replaying the AOB lands exactly where the DLL resolved the pointer
/// (once the signature's own published adjustment is added back, [AOBM-GWORLD-GENAOB]).</para>
/// </summary>
public class PointerPanelAobMakerTests
{
    private const ulong Match = 0x7FF6_1000_0000;          // where the DLL's pattern matched
    private const ulong GObjects = 0x7FF6_1234_5670;       // what the DLL resolved
    private const int Context = 12;                         // bytes of the pattern before the RIP instruction

    /// <summary>12 bytes of context, then <c>mov rax, [rip+X]</c> landing on <see cref="GObjects"/>.</summary>
    private static byte[] Code(ulong target = GObjects)
    {
        var w = Enumerable.Repeat((byte)0xCC, AobMakerActions.ScanHitWindowBytes).ToArray();
        new byte[] { 0x48, 0x89, 0x5C, 0x24, 0x08, 0x57, 0x48, 0x83, 0xEC, 0x20, 0x8B, 0xF9, 0x48, 0x8B, 0x05 }.CopyTo(w, 0);
        BitConverter.GetBytes((int)((long)target - (long)(Match + Context + 7))).CopyTo(w, Context + 3);
        return w;
    }

    private static EngineState State(ulong gobjects = GObjects) => new()
    {
        GObjectsAddr = $"0x{gobjects:X}", GObjectsScanAddr = $"0x{Match:X}",
        GNamesAddr = "0x7FF613000000", GNamesScanAddr = "",
        SparseDelegatesScanAddr = "0x7FF610002000", GEngineScanAddr = "0x7FF610003000",
        ProcessId = 4242, ModuleName = "Game.exe",
    };

    private sealed record Rig(PointerPanelViewModel Vm, ScriptedAobMakerBridge Bridge, ScriptedUiClient Ui,
                              MemoryDump Dump, MockPlatformService Platform);

    private static Rig Build(GenerateAobResult answer, byte[]? code = null, EngineState? state = null,
                             bool withUiClient = true)
    {
        var bridge = new ScriptedAobMakerBridge { Available = true };
        var ui = new ScriptedUiClient { Answer = answer };
        var dump = new MemoryDump();
        if (code != null) dump.Map(Match, code);
        var platform = new MockPlatformService(Path.GetTempPath());
        var vm = new PointerPanelViewModel(platform, dump, new MockLoggingService(), bridge,
                                           aobMakerUi: withUiClient ? ui : null);
        vm.Update(state ?? State());
        return new Rig(vm, bridge, ui, dump, platform);
    }

    /// <summary>What AOBMaker.UI answers for the seed, with <paramref name="lead"/> bytes of context before it.</summary>
    private static GenerateAobResult Answer(int lead = 4, int? pos = 4 + 3, int? aoblen = 4 + 7)
        => new(new GeneratedAob("48 89 5C 24 ?? 48 8B 05 ?? ?? ?? ??", lead, pos, aoblen, 1, "Game.exe"),
               GenerateAobFailure.None, "Unique AOB found");

    [Fact]
    public async Task A_replay_that_lands_on_GObjects_is_pushed_as_the_symbol()
    {
        var rig = Build(Answer(), Code());

        await rig.Vm.RegisterGObjectsSymbolCommand.ExecuteAsync(null);

        // GenerateAob got the INSTRUCTION, not the pattern start, and the game's pid.
        var call = Assert.Single(rig.Ui.Calls);
        Assert.Equal($"0x{Match + Context:X}", call.Address);
        Assert.Equal(4242, call.Pid);
        var sym = Assert.Single(rig.Bridge.Symbols);
        Assert.Equal("gobjects_addr", sym.Symbol);
        Assert.Equal("48 89 5C 24 ?? 48 8B 05 ?? ?? ?? ??", sym.Aob);
        Assert.Equal((7, 11), (sym.Pos, sym.AobLen));
        Assert.Equal("Game.exe", sym.Module);
        Assert.True(sym.AutoActivate);
        Assert.Null(rig.Vm.ErrorMessage);
        Assert.Contains("gobjects_addr", rig.Vm.SymbolStatusText);
    }

    [Fact]
    public async Task A_replay_that_lands_elsewhere_is_not_pushed()
    {
        // AOBMaker decoded a longer instruction than the scan hit holds (an imm8 after the displacement, say): the
        // symbol would land one byte past GObjects.
        var rig = Build(Answer(aoblen: 4 + 8), Code());

        await rig.Vm.RegisterGObjectsSymbolCommand.ExecuteAsync(null);

        Assert.Empty(rig.Bridge.Symbols);
        Assert.StartsWith("Not pushed: the AOB would register 'gobjects_addr' at 0x", rig.Vm.ErrorMessage);
        Assert.Equal("", rig.Vm.SymbolStatusText);
    }

    [Fact]
    public async Task An_answer_without_pos_is_not_pushed()
    {
        var rig = Build(Answer(pos: null, aoblen: null), Code());

        await rig.Vm.RegisterGObjectsSymbolCommand.ExecuteAsync(null);

        Assert.Empty(rig.Bridge.Symbols);
        Assert.StartsWith("Not pushed: AOBMaker.UI did not decode the instruction at 0x", rig.Vm.ErrorMessage);
    }

    [Fact]
    public async Task An_adjusted_signature_is_refused_before_AOBMaker_is_asked()
    {
        // The DLL's GObjects is 0x10 below the RIP target (GOBJ_AV1's -0x10): no symbol script can say that.
        var rig = Build(Answer(), Code(), State(GObjects - 0x10));

        await rig.Vm.RegisterGObjectsSymbolCommand.ExecuteAsync(null);

        Assert.Empty(rig.Ui.Calls);
        Assert.Empty(rig.Bridge.Symbols);
        Assert.StartsWith("Not pushed: no instruction in the scan hit for 'gobjects_addr' points straight at it",
                          rig.Vm.ErrorMessage);
    }

    [Fact]
    public async Task An_unreadable_scan_hit_is_refused_before_AOBMaker_is_asked()
    {
        var rig = Build(Answer(), code: null);

        await rig.Vm.RegisterGObjectsSymbolCommand.ExecuteAsync(null);

        Assert.Empty(rig.Ui.Calls);
        Assert.Equal($"Not pushed: could not read game memory at 0x{Match:X} to check where 'gobjects_addr' would land",
                     rig.Vm.ErrorMessage);
    }

    [Fact]
    public async Task No_AOB_from_AOBMaker_UI_says_what_the_user_has_to_do()
    {
        var rig = Build(new GenerateAobResult(null, GenerateAobFailure.NotRunning, null), Code());

        await rig.Vm.RegisterGObjectsSymbolCommand.ExecuteAsync(null);

        Assert.Empty(rig.Bridge.Symbols);
        Assert.StartsWith("AOBMaker.UI is not running", rig.Vm.ErrorMessage);
    }

    [Fact]
    public void The_button_needs_the_AOBMaker_UI_client_and_a_scan_hit()
    {
        var rig = Build(Answer(), Code());
        Assert.True(rig.Vm.CanRegisterGObjectsSymbol);
        Assert.False(rig.Vm.CanRegisterGNamesSymbol);   // no GNames scan hit in this state

        Assert.False(Build(Answer(), Code(), withUiClient: false).Vm.CanRegisterGObjectsSymbol);
    }

    // ---- [AOBM-PTR-SCANASM] ----

    [Fact]
    public async Task ASM_on_the_SparseDelegates_and_GEngine_scan_hits_goes_to_the_disassembler()
    {
        var rig = Build(Answer(), Code());
        Assert.True(rig.Vm.CanAsmSparseDelegatesScan);
        Assert.True(rig.Vm.CanAsmGEngineScan);

        await rig.Vm.AsmSparseDelegatesScanCommand.ExecuteAsync(null);
        Assert.Equal("7FF610002000", rig.Bridge.LastAsm);

        await rig.Vm.AsmGEngineScanCommand.ExecuteAsync(null);
        Assert.Equal("7FF610003000", rig.Bridge.LastAsm);

        await rig.Vm.CopyGEngineScanAddrCommand.ExecuteAsync(null);
        Assert.Equal("7FF610003000", rig.Platform.LastClipboard);
    }

    // ---- [AOBM-SYSTAB-ASM-SILENT] ----
    // The ASM buttons dropped the bridge's answer, so neither a success nor a refusal said anything and CE was the only
    // witness. They now report on this panel's two lines: success on the green one the symbol buttons use, a refusal
    // on the red one.

    private static EngineState EveryScanHit() => new()
    {
        GObjectsAddr = $"0x{GObjects:X}", GObjectsScanAddr = $"0x{Match:X}",
        GNamesAddr = "0x7FF613000000", GNamesScanAddr = "0x7FF610001000",
        GWorldAddr = "0x7FF614000000", GWorldScanAddr = "0x7FF610004000",
        SparseDelegatesAddr = "0x7FF615000000", SparseDelegatesScanAddr = "0x7FF610002000",
        GEngine = "0x7FF616000000", GEngineScanAddr = "0x7FF610003000",
        ProcessId = 4242, ModuleName = "Game.exe",
    };

    private static CommunityToolkit.Mvvm.Input.IAsyncRelayCommand Asm(PointerPanelViewModel vm, string pointer)
        => pointer switch
        {
            "GObjects" => vm.AsmGObjectsScanCommand,
            "GNames" => vm.AsmGNamesScanCommand,
            "GWorld" => vm.AsmGWorldScanCommand,
            "FSparseDelegateStorage" => vm.AsmSparseDelegatesScanCommand,
            "&GEngine" => vm.AsmGEngineScanCommand,
            _ => throw new ArgumentOutOfRangeException(nameof(pointer), pointer, null),
        };

    [Theory]
    [InlineData("GObjects", "0x7FF610000000")]
    [InlineData("GNames", "0x7FF610001000")]
    [InlineData("GWorld", "0x7FF610004000")]
    [InlineData("FSparseDelegateStorage", "0x7FF610002000")]
    [InlineData("&GEngine", "0x7FF610003000")]
    public async Task ASM_on_a_scan_hit_says_where_CE_went(string pointer, string scanAddr)
    {
        var rig = Build(Answer(), Code(), EveryScanHit());

        await Asm(rig.Vm, pointer).ExecuteAsync(null);

        Assert.Equal(scanAddr[2..], rig.Bridge.LastAsm);
        Assert.Equal($"CE disassembler: {pointer} AOB scan hit @ {scanAddr}", rig.Vm.SymbolStatusText);
        Assert.Null(rig.Vm.ErrorMessage);
    }

    [Fact]
    public async Task ASM_that_CE_refuses_says_so_in_red()
    {
        var rig = Build(Answer(), Code(), EveryScanHit());
        await rig.Vm.AsmGWorldScanCommand.ExecuteAsync(null);      // a success line to replace
        rig.Bridge.NavigateResult = false;

        await rig.Vm.AsmGObjectsScanCommand.ExecuteAsync(null);

        Assert.Equal($"Cheat Engine did not move its view to GObjects AOB scan hit @ 0x{Match:X}", rig.Vm.ErrorMessage);
        Assert.Equal("", rig.Vm.SymbolStatusText);
        Assert.True(rig.Vm.IsAobMakerAvailable);   // a refusal is not a lost plugin
    }

    [Fact]
    public async Task ASM_that_finds_the_plugin_gone_says_why_and_turns_the_buttons_off()
    {
        var rig = Build(Answer(), Code(), EveryScanHit());
        Assert.True(rig.Vm.CanAsmGObjectsScan);
        rig.Bridge.NavigateResult = false;
        rig.Bridge.AvailableAfterCall = false;

        await rig.Vm.AsmGObjectsScanCommand.ExecuteAsync(null);

        Assert.Equal(AobMakerUnavailable.Text(rig.Bridge), rig.Vm.ErrorMessage);
        Assert.False(rig.Vm.IsAobMakerAvailable);
        Assert.False(rig.Vm.CanAsmGObjectsScan);
    }

    // The HEX buttons beside them had the same silence.

    private static CommunityToolkit.Mvvm.Input.IAsyncRelayCommand Hex(PointerPanelViewModel vm, string pointer)
        => pointer switch
        {
            "GObjects" => vm.HexGObjectsCommand,
            "GNames" => vm.HexGNamesCommand,
            "GWorld" => vm.HexGWorldCommand,
            "FSparseDelegateStorage" => vm.HexSparseDelegatesCommand,
            "&GEngine" => vm.HexGEngineCommand,
            _ => throw new ArgumentOutOfRangeException(nameof(pointer), pointer, null),
        };

    [Theory]
    [InlineData("GObjects", "0x7FF612345670")]
    [InlineData("GNames", "0x7FF613000000")]
    [InlineData("GWorld", "0x7FF614000000")]
    [InlineData("FSparseDelegateStorage", "0x7FF615000000")]
    [InlineData("&GEngine", "0x7FF616000000")]
    public async Task HEX_on_a_pointer_says_where_CE_went(string pointer, string addr)
    {
        var rig = Build(Answer(), Code(), EveryScanHit());

        await Hex(rig.Vm, pointer).ExecuteAsync(null);

        Assert.Equal(addr[2..], rig.Bridge.LastHex);
        Assert.Equal($"CE hex view: {pointer} @ {addr}", rig.Vm.SymbolStatusText);
        Assert.Null(rig.Vm.ErrorMessage);
    }

    [Fact]
    public async Task HEX_that_CE_refuses_says_so_in_red()
    {
        var rig = Build(Answer(), Code(), EveryScanHit());
        rig.Bridge.NavigateResult = false;

        await rig.Vm.HexGWorldCommand.ExecuteAsync(null);

        Assert.Equal("Cheat Engine did not move its view to GWorld @ 0x7FF614000000", rig.Vm.ErrorMessage);
        Assert.Equal("", rig.Vm.SymbolStatusText);
    }

    [Fact]
    public void No_scan_hit_no_ASM()
    {
        var state = State();
        var rig = Build(Answer(), Code(), new EngineState
        {
            GObjectsAddr = state.GObjectsAddr, GObjectsScanAddr = state.GObjectsScanAddr,
            ProcessId = 4242, ModuleName = "Game.exe",
        });
        Assert.False(rig.Vm.CanAsmSparseDelegatesScan);
        Assert.False(rig.Vm.CanAsmGEngineScan);
    }

    internal sealed class ScriptedUiClient : IAobMakerUiClient
    {
        public GenerateAobResult Answer { get; set; } = new(null, GenerateAobFailure.NotRunning, null);
        public List<(string Address, int Pid)> Calls { get; } = new();

        public Task<GenerateAobResult> GenerateAobAsync(string hexAddress, int processId, CancellationToken ct = default)
        {
            Calls.Add((hexAddress, processId));
            return Task.FromResult(Answer);
        }
    }

    /// <summary>Reads succeed only inside a mapped range, as the DLL's SEH-guarded read does.</summary>
    internal sealed class MemoryDump : StubDumpService
    {
        private readonly List<(ulong Base, byte[] Bytes)> _map = new();

        public void Map(ulong at, byte[] bytes) => _map.Add((at, bytes));

        public override Task<byte[]> ReadMemAsync(string addr, int size, CancellationToken ct = default)
        {
            var a = Convert.ToUInt64(addr.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? addr[2..] : addr, 16);
            foreach (var (b, bytes) in _map)
                if (a >= b && a + (ulong)size <= b + (ulong)bytes.Length)
                    return Task.FromResult(bytes.AsSpan((int)(a - b), size).ToArray());
            throw new InvalidOperationException($"read_mem failed at {addr}");
        }
    }

    // ---- [AOBM-GWORLD-GENAOB] part 2: a signature that ADJUSTS the RIP target (GOBJ_AV1 on Avowed: -0x10) ----
    //
    // CreateSymbolScript registers the raw RIP target, so an adjusted pointer gets a symbol script of our own: the DLL
    // publishes the winning signature's adjustment, the replay is checked against the RIP target, and the script adds
    // the adjustment back. Without the published adjustment (an older DLL) nothing changes: the refusal above stands.

    private static EngineState AdjustedState(int adjustment = -0x10) => new()
    {
        GObjectsAddr = $"0x{GObjects - 0x10:X}", GObjectsScanAddr = $"0x{Match:X}", GObjectsAdjustment = adjustment,
        ProcessId = 4242, ModuleName = "Game.exe",
    };

    [Fact]
    public async Task An_adjusted_signature_the_DLL_published_is_pushed_as_our_own_symbol_script()
    {
        var rig = Build(Answer(), Code(), AdjustedState());
        rig.Bridge.AaDetail = new SymbolScriptResult(true, true, null, null);

        await rig.Vm.RegisterGObjectsSymbolCommand.ExecuteAsync(null);

        Assert.Equal($"0x{Match + Context:X}", Assert.Single(rig.Ui.Calls).Address);   // the instruction, as before
        Assert.Empty(rig.Bridge.Symbols);   // CreateSymbolScript cannot add the -0x10
        var aa = Assert.Single(rig.Bridge.AaScripts);
        Assert.Equal("GObjects → gobjects_addr", aa.Description);
        Assert.True(aa.AutoActivate);
        Assert.Contains("AOBScanModuleUnique('Game.exe', '48 89 5C 24 ?? 48 8B 05 ?? ?? ?? ??'", aa.Script);
        Assert.Contains("-0x10", aa.Script);
        Assert.Null(rig.Vm.ErrorMessage);
        Assert.Contains("gobjects_addr", rig.Vm.SymbolStatusText);
    }

    [Fact]
    public async Task An_adjustment_that_did_not_apply_falls_back_to_the_plain_RIP_target()
    {
        // Genau tries target + adjustment first and the target itself second: when the second won, the DLL's GObjects
        // IS the RIP target and the plugin's own symbol script is exact.
        var rig = Build(Answer(), Code(), new EngineState
        {
            GObjectsAddr = $"0x{GObjects:X}", GObjectsScanAddr = $"0x{Match:X}", GObjectsAdjustment = -0x10,
            ProcessId = 4242, ModuleName = "Game.exe",
        });

        await rig.Vm.RegisterGObjectsSymbolCommand.ExecuteAsync(null);

        Assert.Single(rig.Bridge.Symbols);
        Assert.Empty(rig.Bridge.AaScripts);
    }

    [Fact]
    public async Task An_adjusted_replay_that_misses_the_RIP_target_is_not_pushed()
    {
        var rig = Build(Answer(aoblen: 4 + 8), Code(), AdjustedState());

        await rig.Vm.RegisterGObjectsSymbolCommand.ExecuteAsync(null);

        Assert.Empty(rig.Bridge.Symbols);
        Assert.Empty(rig.Bridge.AaScripts);
        Assert.StartsWith("Not pushed: the AOB would register 'gobjects_addr' at 0x", rig.Vm.ErrorMessage);
    }

    // ---- the script itself, RUN on CE's own Lua VM against stubs ----

    private static string EnableOf(string script)
    {
        int from = script.IndexOf("{$lua}\n", StringComparison.Ordinal) + "{$lua}\n".Length;
        return script[from..script.IndexOf("{$asm}", from, StringComparison.Ordinal)];
    }

    private static string DisableOf(string script)
    {
        int d = script.IndexOf("[DISABLE]", StringComparison.Ordinal);
        int from = script.IndexOf("{$lua}\n", d, StringComparison.Ordinal) + "{$lua}\n".Length;
        return script[from..script.IndexOf("{$asm}", from, StringComparison.Ordinal)];
    }

    private static string RunScript(string body, string scanResult)
    {
        string lua = $$"""
            UE5_DEBUG = 0
            SYMS, SHOWN, SCANNED = {}, nil, nil
            function AOBScanModuleUnique(m, aob, flags) SCANNED = m .. '|' .. aob .. '|' .. tostring(flags) return {{scanResult}} end
            function readInteger(a, signed) if a == 0x1000 + 3 and signed then return 0x200 end return nil end   -- disp32 at pos 3
            function registerSymbol(n, a) SYMS[n] = a end
            function unregisterSymbol(n) SYMS[n] = nil end
            function showMessage(m) SHOWN = m end
            function synchronize(f) f() end
            function getLuaEngine() return { Close = function() end } end
            local chunk = assert(load('local syntaxcheck,memrec=...\n' .. [==[
            {{body}}
            ]==]))
            local ok, err = pcall(chunk, false, { ID = 3 })
            print('OK=' .. tostring(ok) .. '|' .. tostring(err))
            print('SCANNED=' .. tostring(SCANNED))
            print('SYM=' .. (SYMS.gobjects_addr and string.format('0x%X', SYMS.gobjects_addr) or 'nil'))
            print('SHOWN=' .. tostring(SHOWN))
            """;
        var (exit, output) = CeLua53Host.Run(lua);
        Assert.True(exit == 0, output);
        return output.Replace("\r\n", "\n");
    }

    private static string AdjustedScript()
        => Services.AdjustedSymbolScriptGenerator.Generate("gobjects_addr", "Game.exe", "48 8B 05 ?? ?? ?? ??", 3, 7, -0x10);

    [Fact]
    public void The_adjusted_script_registers_the_RIP_target_plus_the_adjustment()
    {
        var output = RunScript(EnableOf(AdjustedScript()), "0x1000");

        Assert.Contains("OK=true|nil", output);
        Assert.Contains("SCANNED=Game.exe|48 8B 05 ?? ?? ?? ??|+X", output);
        // hit 0x1000 + instruction end 7 + disp 0x200 = 0x1207, the RIP target; -0x10 makes it 0x11F7
        Assert.Contains("SYM=0x11F7", output);
        Assert.Contains("SHOWN=nil", output);
    }

    [Fact]
    public void A_scan_that_finds_nothing_raises_registers_nothing_and_shows_no_modal()
    {
        // Enabled THROUGH the plugin: a showMessage would hold its pipe ([AOBM-TRAINER-SETUP-MODAL]); the raise makes
        // CE refuse the activation, and the plugin reports the reason.
        var output = RunScript(EnableOf(AdjustedScript()), "nil");

        Assert.Contains("OK=false|", output);
        Assert.Contains("gobjects_addr", output);
        Assert.Contains("SYM=nil", output);
        Assert.Contains("SHOWN=nil", output);
    }

    [Fact]
    public void Disabling_unregisters_the_symbol()
    {
        var script = AdjustedScript();
        Assert.StartsWith("if syntaxcheck then return end", EnableOf(script));
        Assert.StartsWith("if syntaxcheck then return end", DisableOf(script));
        Assert.Contains("unregisterSymbol('gobjects_addr')", DisableOf(script));
        Assert.Contains(Services.CeLuaHygiene.Attribution, script);
    }
}
