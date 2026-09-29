using System.Text.Json.Nodes;
using UE5DumpUI.Core;
using UE5DumpUI.Services;
using UE5DumpUI.ViewModels;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// [AOBM-DISSECT-INJECT] The Auto Structure Dissect record and the Tools action that ships it with
/// <c>ue5_dissect.lua</c>. The record is a stateful toggle: a bail-out that registered nothing unticks it and keeps the
/// Lua Engine window open; only a clean enable or disable closes it (CLAUDE.md, CE Lua output hygiene).
/// </summary>
public class DissectScriptGeneratorTests
{
    private static readonly string Script = DissectScriptGenerator.Generate();

    private static string EnableBlock => Script[..Script.IndexOf("[DISABLE]", StringComparison.Ordinal)];
    private static string DisableBlock => Script[Script.IndexOf("[DISABLE]", StringComparison.Ordinal)..];

    [Fact]
    public void Both_blocks_are_quiet_lua_with_their_own_debug_gate()
    {
        Assert.StartsWith("[ENABLE]\n{$lua}\nif syntaxcheck then return end\n", Script);
        Assert.Contains("[DISABLE]\n{$lua}\nif syntaxcheck then return end\n", Script);
        Assert.Contains("local DEBUG = UE5_DEBUG or 0", EnableBlock);
        Assert.Contains("local DEBUG = UE5_DEBUG or 0", DisableBlock);
        Assert.DoesNotContain("\r", Script);
        Assert.DoesNotContain("print('[UE5Dissect] auto", Script);   // the success lines go through dbg
        Assert.Contains("dbg('[UE5Dissect] auto Structure Dissect on')", EnableBlock);
    }

    [Fact]
    public void It_loads_the_embedded_file_by_the_name_the_action_injects_it_under()
    {
        Assert.Equal(DissectLuaResource.DefaultFileName, DissectScriptGenerator.TableFileName);
        Assert.Contains($"findTableFile('{DissectLuaResource.DefaultFileName}')", EnableBlock);
        Assert.DoesNotContain("dofile", Script);   // the table, never the disk
    }

    [Fact]
    public void Every_enable_bail_out_unticks_and_only_the_clean_end_closes()
    {
        // Five things can stop it: no DLL, no table file, a load error, a chunk that did not return the API, and
        // enableAutoCallback raising. Each registered nothing, so each must untick (deferred: CE sets Active after the
        // block) and return before the close.
        var enable = EnableBlock;
        Assert.Equal(5, Count(enable, CeLuaHygiene.DeferredUntickLua("  ")));
        Assert.Equal(5, Count(enable, "showMessage("));
        Assert.Equal(1, Count(enable, CeLuaHygiene.CloseCall));
        Assert.True(enable.LastIndexOf("  return\n", StringComparison.Ordinal)
                    < enable.IndexOf(CeLuaHygiene.CloseCall, StringComparison.Ordinal));
        Assert.Contains("DEBUG == 0 then " + CeLuaHygiene.CloseCall, enable);
    }

    [Fact]
    public void A_UETools_warning_at_enable_keeps_the_window_open_without_unticking()
    {
        // [AOBM-DISSECT-UETOOLS] enableAutoCallback returns false plus a reason when CE 7.7's own UE dissector could
        // not be suspended. The module prints that ungated, and a success-close straight after would shut the Lua
        // Engine window over it. Our callbacks DID register, so it is no bail-out: the record stays ticked.
        var enable = EnableBlock;
        Assert.Contains("local eok, eres = pcall(mod.enableAutoCallback)", enable);
        Assert.Contains("if eres ~= false and DEBUG == 0 then " + CeLuaHygiene.CloseCall, enable);
        Assert.Equal(5, Count(enable, CeLuaHygiene.DeferredUntickLua("  ")));
    }

    [Fact]
    public void The_enable_hands_the_module_its_own_memory_record()
    {
        // [AOBM-DISSECT-UETOOLS] CE can untick this record without running [DISABLE] (a process change answered Yes)
        // or free it while ticked (a delete), leaving ours registered and UETools suspended under a record showing
        // auto mode off. The module follows the record it is handed, and CE's auto assembler runs a {$lua} block
        // behind `local syntaxcheck,memrec=...` (autoassembler.pas, luacode) -- so memrec is what the enable passes on.
        Assert.Contains("pcall(mod.enableAutoCallback, memrec)", EnableBlock);
    }

    [Fact]
    public void The_enable_block_run_as_CE_runs_it_passes_memrec_through_and_nil_when_there_is_none()
    {
        // Behavioural twin of the text check above, on CE's own Lua VM: the ENABLE block is loaded behind CE's own
        // prefix line and called as CE calls it -- (syntaxcheck, memrec). A chunk run without a record (autoAssemble()
        // from Lua) gets memrec = nil, and must hand the module nil rather than fail.
        string enable = EnableBlock;
        int from = enable.IndexOf("{$lua}\n", StringComparison.Ordinal) + "{$lua}\n".Length;
        int to = enable.IndexOf("{$asm}", from, StringComparison.Ordinal);
        string body = enable[from..to];
        Assert.DoesNotContain("]==]", body);
        string lua = """
            UE5_DEBUG = 0
            function getAddressSafe() return 0x1000 end
            function reinitializeSymbolhandler() end
            function findTableFile() return { Stream = { Size = 1 } } end
            function createStringStream()
              return { copyFrom = function() end, destroy = function() end,
                DataString = "return { enableAutoCallback = function(...) GOT_N = select('#', ...); GOT = ...; return true end }" }
            end
            function showMessage(m) SHOWN = m end
            function createTimer() return {} end
            function synchronize() end
            function getLuaEngine() return { Close = function() end } end
            local chunk = assert(load('local syntaxcheck,memrec=...\n' .. [==[
            """ + body + """
            ]==]))
            local REC = { ID = 7, Active = false }
            chunk(false, REC)
            assert(SHOWN == nil, 'the enable bailed out: ' .. tostring(SHOWN))
            assert(GOT == REC, 'enableAutoCallback was not handed the memory record: ' .. tostring(GOT))
            GOT, GOT_N = 'unset', nil
            chunk(false, nil)
            assert(SHOWN == nil, 'the enable bailed out without a record: ' .. tostring(SHOWN))
            assert(GOT == nil and GOT_N == 1, 'without a record the module gets nil: ' .. tostring(GOT))
            print('OK')
            """;
        var (exit, output) = CeLua53Host.Run(lua);
        Assert.True(exit == 0 && output.Contains("OK"), output);
    }

    [Fact]
    public void Enable_checks_the_DLL_first_and_keeps_the_module_where_DISABLE_can_reach_it()
    {
        var enable = EnableBlock;
        int probe = enable.IndexOf("getAddressSafe('UE5_GetObjectClass')", StringComparison.Ordinal);
        Assert.True(probe >= 0);
        Assert.True(probe < enable.IndexOf("findTableFile", StringComparison.Ordinal));
        // The chunk RETURNS its table (the two helpers define globals instead), so the return value is the module.
        Assert.Contains("local ok, mod = pcall(fn)", enable);
        Assert.Contains($"{DissectScriptGenerator.ModuleGlobal} = mod", enable);
        Assert.Contains($"pcall({DissectScriptGenerator.ModuleGlobal}.disableAutoCallback)", DisableBlock);
    }

    [Fact]
    public void A_DLL_injected_after_CE_opened_the_game_is_found_before_any_verdict()
    {
        // Measured 2026-09-29 on DumperTest: CE opened the game, then the DLL was injected; the pipe answered, and
        // ticking this record said "UE5Dumper.dll is not loaded". CE lists modules when it opens a process, so the
        // DLL's exports appear only after the symbol handler is re-enumerated -- which must come before the verdict.
        var enable = EnableBlock;
        int firstProbe = enable.IndexOf("getAddressSafe('UE5_GetObjectClass')", StringComparison.Ordinal);
        int reinit = enable.IndexOf("reinitializeSymbolhandler()", StringComparison.Ordinal);
        int secondProbe = enable.IndexOf("getAddressSafe('UE5_GetObjectClass')", reinit < 0 ? 0 : reinit,
                                         StringComparison.Ordinal);
        int verdict = enable.IndexOf("is not loaded in this game", StringComparison.Ordinal);
        Assert.True(firstProbe >= 0 && reinit > firstProbe, "no re-enumeration after the first probe");
        Assert.True(secondProbe > reinit, "no second probe after the re-enumeration");
        Assert.True(verdict > secondProbe, "the verdict comes before the second probe");
    }

    [Fact]
    public void A_failed_disable_is_said_ungated_and_keeps_the_window_open()
    {
        var disable = DisableBlock;
        Assert.Contains("print('[UE5Dissect] the auto-dissect callbacks are still registered: '", disable);
        // [AOBM-DISSECT-UETOOLS] A disable that could not put CE 7.7's own UE dissector back returns false: its
        // ungated warning has to stay readable too.
        Assert.Contains("disOk, disRes = pcall(", disable);
        Assert.Contains("if disOk and disRes ~= false and DEBUG == 0 then " + CeLuaHygiene.CloseCall, disable);
    }

    [Fact]
    public void The_embedded_script_is_the_repo_one_folded_to_LF()
    {
        var embedded = DissectLuaResource.Read();
        var repo = File.ReadAllText(NumericInputCoercionTests.RepoFile("scripts/ue5_dissect.lua"));
        Assert.Equal(CeLuaHygiene.NormalizeTableFilePayload(repo), embedded);
        Assert.DoesNotContain("\r", embedded);
        Assert.Contains("function dissect.enableAutoCallback()", embedded);
        Assert.EndsWith("return dissect\n", embedded);
    }

    // ---- Tools -> Add Auto Structure Dissect to Current CE Table ----

    [Fact]
    public async Task The_action_embeds_the_file_then_adds_the_record_unticked()
    {
        var bridge = new ScriptedAobMakerBridge { Available = true };
        var vm = BuildVm(bridge);

        await vm.InjectDissectAutoCommand.ExecuteAsync(null);

        var file = Assert.Single(bridge.TableFiles);
        Assert.Equal(DissectLuaResource.DefaultFileName, file.FileName);
        Assert.Equal(DissectLuaResource.Read(), file.Content);
        var rec = Assert.Single(bridge.AaScripts);
        Assert.Equal(DissectScriptGenerator.RecordDescription, rec.Description);
        Assert.Equal(DissectScriptGenerator.Generate(), rec.Script);
        Assert.False(rec.AutoActivate);
        Assert.Equal(DissectScriptGenerator.RecordGroup, rec.Group);
        Assert.StartsWith("Auto Structure Dissect added", vm.StatusText);
        // [AOBM-DISSECT-UETOOLS] The record suspends CE's own UE dissector while ticked; the user is told up front.
        Assert.Contains("Use when dissecting structures", vm.StatusText);
    }

    [Fact]
    public async Task A_failed_embed_adds_no_record()
    {
        var bridge = new ScriptedAobMakerBridge { Available = true, InjectResult = (false, "Stream size mismatch") };
        var vm = BuildVm(bridge);

        await vm.InjectDissectAutoCommand.ExecuteAsync(null);

        Assert.Empty(bridge.AaScripts);
        Assert.Equal($"Could not embed {DissectLuaResource.DefaultFileName} in the CE table: Stream size mismatch",
                     vm.StatusText);
    }

    [Fact]
    public async Task A_record_that_does_not_arrive_is_reported_as_such()
    {
        var bridge = new ScriptedAobMakerBridge { Available = true, AaResult = false };
        var vm = BuildVm(bridge);

        await vm.InjectDissectAutoCommand.ExecuteAsync(null);

        Assert.Single(bridge.TableFiles);
        Assert.StartsWith($"{DissectLuaResource.DefaultFileName} is embedded, but its enable record did not reach CE",
                          vm.StatusText);
    }

    [Fact]
    public async Task No_plugin_sends_nothing_and_says_why()
    {
        var bridge = new ScriptedAobMakerBridge { Available = false };
        var vm = BuildVm(bridge);

        await vm.InjectDissectAutoCommand.ExecuteAsync(null);

        Assert.Equal(1, bridge.CheckCalls);
        Assert.Empty(bridge.TableFiles);
        Assert.StartsWith("Auto Structure Dissect needs the AOBMaker CE plugin: ", vm.StatusText);

        var none = BuildVm(null);
        await none.InjectDissectAutoCommand.ExecuteAsync(null);
        Assert.StartsWith("Auto Structure Dissect needs the AOBMaker CE plugin: ", none.StatusText);
    }

    private static int Count(string haystack, string needle)
    {
        int n = 0;
        for (int i = haystack.IndexOf(needle, StringComparison.Ordinal); i >= 0;
             i = haystack.IndexOf(needle, i + needle.Length, StringComparison.Ordinal))
            n++;
        return n;
    }

    private static MainWindowViewModel BuildVm(IAobMakerBridge? bridge)
        => new(new NoopPipeClient(), new StubDumpService(), new MockLoggingService(),
               new MockPlatformService(Path.GetTempPath()), aobMaker: bridge);

    private sealed class NoopPipeClient : IPipeClient
    {
        public bool IsConnected => false;
        public event Action<bool>? ConnectionStateChanged { add { } remove { } }
        public event Action<JsonObject>? EventReceived { add { } remove { } }
        public event Action<UE5DumpUI.Models.PipeLogEntry>? Activity { add { } remove { } }
        public event Action<bool>? GameThreadStalledChanged { add { } remove { } }
        public Task ConnectAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task DisconnectAsync() => Task.CompletedTask;
        public Task<JsonObject> SendAsync(JsonObject request, CancellationToken ct = default)
            => Task.FromResult(new JsonObject());
        public void Dispose() { }
    }
}
