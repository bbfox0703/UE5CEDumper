using System.Text.Json;
using UE5DumpUI.Core;
using UE5DumpUI.Models;
using UE5DumpUI.Services;
using UE5DumpUI.ViewModels;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// [AOBM-ACTIVATE-RESULT] SYM used to say "Registered" whenever the plugin replied <c>success</c>, which only ever
/// meant the record exists. AOBMaker v20260930 (build 155) adds <c>activated</c> and <c>symbolRegistered</c>; an older
/// plugin sends neither, so their absence is "not known", never success, and a reply that never came may still have
/// left a record behind (AOBMaker reply §3 rule 8).
/// </summary>
public class AobMakerActivateResultTests
{
    private static AobMakerMessage? Parse(string json)
        => JsonSerializer.Deserialize(json, AobMakerJsonContext.Default.AobMakerMessage);

    private static PointerPanelViewModel Vm() => new(new MockPlatformService(Path.GetTempPath()));

    // ---- the reply ----

    [Fact]
    public void A_current_plugin_reply_is_read_whole()
    {
        var r = AobMakerBridgeService.ToSymbolScriptResult(Parse(
            "{\"type\":\"CreateSymbolScriptResult\",\"success\":true,\"activated\":false,\"symbolRegistered\":false," +
            "\"message\":\"CE did not activate the script: Lua error in the script at line 9\"}"));

        Assert.Equal(new SymbolScriptResult(true, false, false,
            "CE did not activate the script: Lua error in the script at line 9"), r);
    }

    [Fact]
    public void An_older_plugin_reply_leaves_activation_unknown()
    {
        var r = AobMakerBridgeService.ToSymbolScriptResult(Parse("{\"type\":\"CreateSymbolScriptResult\",\"success\":true}"));

        Assert.True(r.Created);
        Assert.Null(r.Activated);
        Assert.Null(r.SymbolRegistered);
    }

    [Fact]
    public void A_failed_reply_keeps_its_reason()
    {
        var r = AobMakerBridgeService.ToSymbolScriptResult(Parse(
            "{\"type\":\"CreateSymbolScriptResult\",\"success\":false,\"message\":\"Script record created but verification failed\"}"));

        Assert.False(r.Created);
        Assert.Equal("Script record created but verification failed", r.Message);
    }

    // ---- what the System tab says ----

    [Fact]
    public void Active_and_registered_is_the_only_plain_success()
    {
        var vm = Vm();
        vm.ReportSymbolRegistration(new SymbolScriptResult(true, true, true, null), "gworld_addr", "d");

        Assert.Equal("Registered CE symbol 'gworld_addr'.", vm.SymbolStatusText);
        Assert.Null(vm.ErrorMessage);
    }

    [Fact]
    public void Created_but_not_enabled_says_so_in_red_with_CEs_reason()
    {
        var vm = Vm();
        vm.ReportSymbolRegistration(new SymbolScriptResult(true, false, false,
            "CE did not activate the script: Lua error in the script at line 9"), "gworld_addr", "d");

        Assert.Equal("", vm.SymbolStatusText);
        Assert.Equal("CE symbol script 'gworld_addr' was added, but Cheat Engine did not enable it: " +
                     "CE did not activate the script: Lua error in the script at line 9", vm.ErrorMessage);
    }

    [Fact]
    public void Enabled_but_the_symbol_is_not_usable_says_so_in_red()
    {
        var vm = Vm();
        vm.ReportSymbolRegistration(new SymbolScriptResult(true, true, false,
            "the script is active, but the symbol gworld_addr does not resolve"), "gworld_addr", "d");

        Assert.Equal("", vm.SymbolStatusText);
        Assert.Equal("CE symbol script 'gworld_addr' is enabled, but the symbol is not usable: " +
                     "the script is active, but the symbol gworld_addr does not resolve", vm.ErrorMessage);
    }

    [Fact]
    public void An_older_plugin_gets_a_success_that_does_not_claim_more_than_it_knows()
    {
        var vm = Vm();
        vm.ReportSymbolRegistration(SymbolScriptResult.FromCreated(true), "gworld_addr", "d");

        Assert.Null(vm.ErrorMessage);
        Assert.Equal("Added CE symbol script 'gworld_addr'. This AOBMaker plugin does not say whether Cheat Engine " +
                     "enabled it: check that the record is ticked (AOBMaker v20260930 or later reports it)",
                     vm.SymbolStatusText);
    }

    [Fact]
    public void No_answer_warns_against_a_blind_retry()
    {
        var vm = Vm();
        vm.ReportSymbolRegistration(new SymbolScriptResult(false, null, null, null, TimedOut: true), "gworld_addr", "d");

        Assert.Equal("", vm.SymbolStatusText);
        Assert.Equal("No answer from the AOBMaker plugin in time for 'gworld_addr': the script may still have been " +
                     "added. Check Cheat Engine's address list before pressing SYM again", vm.ErrorMessage);
    }

    [Fact]
    public void The_new_sentences_live_in_en_axaml()
    {
        var dir = AppContext.BaseDirectory;
        string? path = null;
        for (int i = 0; i < 8 && dir != null && path == null; i++, dir = Path.GetDirectoryName(dir))
        {
            var candidate = Path.Combine(dir, "ui", "UE5DumpUI", "Resources", "Strings", "en.axaml");
            if (File.Exists(candidate)) path = candidate;
        }
        Assert.NotNull(path);
        var text = File.ReadAllText(path!);
        foreach (var key in new[] { "NotActivated", "NotRegistered", "ActivationUnknown", "TimedOut" })
            Assert.Contains($"x:Key=\"str.Pointers.Symbol.{key}\"", text, StringComparison.Ordinal);
    }

    // ---- wiring: every SYM path reads the detailed reply ----

    [Fact]
    public async Task SYM_on_GWorld_reports_what_the_plugin_said_about_activation()
    {
        var bridge = new ScriptedAobMakerBridge
        {
            Available = true,
            SymbolDetail = new SymbolScriptResult(true, false, false, "CE did not activate the script: x"),
        };
        var vm = new PointerPanelViewModel(new MockPlatformService(Path.GetTempPath()), null, new MockLoggingService(),
                                           bridge);
        vm.Update(new EngineState
        {
            GWorldAob = "48 8B 1D ?? ?? ?? ??", GWorldAobPos = 3, GWorldAobLen = 7,
            ModuleName = "Game.exe", ProcessId = 4242,
        });

        await vm.RegisterGWorldSymbolCommand.ExecuteAsync(null);

        Assert.Single(bridge.Symbols);
        Assert.StartsWith("CE symbol script 'gworld_addr' was added, but Cheat Engine did not enable it", vm.ErrorMessage);
    }
}
