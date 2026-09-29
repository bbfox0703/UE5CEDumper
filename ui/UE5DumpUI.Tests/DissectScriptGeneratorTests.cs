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
        Assert.Contains("if DEBUG == 0 then " + CeLuaHygiene.CloseCall, enable);
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
    public void A_failed_disable_is_said_ungated_and_keeps_the_window_open()
    {
        var disable = DisableBlock;
        Assert.Contains("print('[UE5Dissect] the auto-dissect callbacks are still registered: '", disable);
        Assert.Contains("if disOk and DEBUG == 0 then " + CeLuaHygiene.CloseCall, disable);
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
