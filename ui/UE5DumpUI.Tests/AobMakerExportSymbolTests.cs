using UE5DumpUI.Core;
using UE5DumpUI.Helpers;
using UE5DumpUI.Models;
using UE5DumpUI.ViewModels;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// [AOBM-EXPORT-GWORLD-AOB] On a modular build (Satisfactory) Genau finds GWorld and &amp;GEngine through their exports,
/// so there is no AOB triple and SYM stayed disabled. The export is itself a restart- and patch-stable Cheat Engine
/// symbol -- measured on Satisfactory with CE 7.7: CE lists it undecorated, <c>GWorld</c> /
/// <c>FactoryGameSteam-Engine-Win64-Shipping.GWorld</c>, and never under its MSVC name -- so SYM registers
/// <c>gworld_addr</c> / <c>gengine_addr</c> from it with a define + registersymbol script.
/// </summary>
public class AobMakerExportSymbolTests
{
    // ---- CE's name for the export ----

    [Theory]
    [InlineData("?GWorld@@3VUWorldProxy@@A", "GWorld")]
    [InlineData("?GEngine@@3PEAVUEngine@@EA", "GEngine")]
    [InlineData("?GUObjectArray@@3VFUObjectArray@@A", "GUObjectArray")]
    public void A_global_variable_export_is_named_as_CE_lists_it(string mangled, string ce)
        => Assert.Equal(ce, AobMakerActions.CeExportSymbol(mangled));

    [Theory]
    [InlineData("")]
    [InlineData("48 8B 1D ?? ?? ?? ??")]                       // an AOB, not an export
    [InlineData("?ToString@FName@@QEBAXAEAVFString@@@Z")]     // a function
    [InlineData("?Foo@Bar@@3HA")]                              // Bar::Foo: how CE names a scoped one is not measured
    [InlineData("?GWorld@@3")]                                 // truncated
    public void Anything_else_has_no_name_rather_than_a_guessed_one(string mangled)
        => Assert.Equal("", AobMakerActions.CeExportSymbol(mangled));

    // ---- SYM ----

    private static (PointerPanelViewModel Vm, ScriptedAobMakerBridge Bridge) Panel(EngineState state,
        SymbolScriptResult? aa = null)
    {
        var bridge = new ScriptedAobMakerBridge { Available = true, AaDetail = aa };
        var vm = new PointerPanelViewModel(new MockPlatformService(Path.GetTempPath()), null, new MockLoggingService(),
                                           bridge);
        vm.Update(state);
        vm.ApplyAobMakerProbe(true);
        return (vm, bridge);
    }

    private static EngineState Satisfactory => new()
    {
        GWorldAddr = "0x7FF8B739CB88", GWorldExport = "?GWorld@@3VUWorldProxy@@A",
        GEngine = "0x7FF8B739F768", GEngineExport = "?GEngine@@3PEAVUEngine@@EA",
        ModuleName = "FactoryGameSteam-Win64-Shipping.exe", ProcessId = 4242,
    };

    private static readonly SymbolScriptResult Active = new(true, true, null, null);

    [Fact]
    public void An_export_enables_SYM_for_GWorld_and_GEngine_without_an_AOB()
    {
        var (vm, _) = Panel(Satisfactory);
        Assert.True(vm.CanRegisterGWorldSymbol);
        Assert.True(vm.CanRegisterGEngineSymbol);
    }

    [Fact]
    public async Task SYM_on_GWorld_registers_the_export_under_gworld_addr()
    {
        var (vm, bridge) = Panel(Satisfactory, Active);

        await vm.RegisterGWorldSymbolCommand.ExecuteAsync(null);

        Assert.Empty(bridge.Symbols);   // no AOB to scan for
        var aa = Assert.Single(bridge.AaScripts);
        Assert.Equal("GWorld → gworld_addr", aa.Description);
        Assert.Contains("define(gworld_addr,GWorld)", aa.Script);
        Assert.Contains("registersymbol(gworld_addr)", aa.Script);
        Assert.Contains("unregistersymbol(gworld_addr)", aa.Script);
        Assert.True(aa.AutoActivate);
        Assert.Equal("Registered CE symbol 'gworld_addr'.", vm.SymbolStatusText);
        Assert.Null(vm.ErrorMessage);
    }

    [Fact]
    public async Task SYM_on_GEngine_registers_the_export_under_gengine_addr()
    {
        var (vm, bridge) = Panel(Satisfactory, Active);

        await vm.RegisterGEngineSymbolCommand.ExecuteAsync(null);

        var aa = Assert.Single(bridge.AaScripts);
        Assert.Equal("&GEngine → gengine_addr", aa.Description);
        Assert.Contains("define(gengine_addr,GEngine)", aa.Script);
        Assert.Equal("Registered CE symbol 'gengine_addr'.", vm.SymbolStatusText);
    }

    [Fact]
    public async Task An_export_CE_could_not_resolve_is_reported_in_red_with_CEs_reason()
    {
        // e.g. pressed before CE had loaded the module's exports: the define does not resolve and CE refuses to enable.
        var (vm, _) = Panel(Satisfactory, new SymbolScriptResult(true, false, null, "Not all symbols could be resolved"));

        await vm.RegisterGWorldSymbolCommand.ExecuteAsync(null);

        Assert.Equal("", vm.SymbolStatusText);
        Assert.StartsWith("CE symbol script 'gworld_addr' was added, but Cheat Engine did not enable it", vm.ErrorMessage);
        Assert.Contains("Not all symbols could be resolved", vm.ErrorMessage);
    }

    [Fact]
    public async Task An_AOB_triple_still_wins_over_an_export()
    {
        var (vm, bridge) = Panel(new EngineState
        {
            GWorldAddr = "0x7FF8B739CB88", GWorldAob = "48 8B 1D ?? ?? ?? ??", GWorldAobPos = 3, GWorldAobLen = 7,
            GWorldExport = "?GWorld@@3VUWorldProxy@@A", ModuleName = "Game.exe", ProcessId = 4242,
        });

        await vm.RegisterGWorldSymbolCommand.ExecuteAsync(null);

        Assert.Single(bridge.Symbols);
        Assert.Empty(bridge.AaScripts);
    }

    [Fact]
    public void An_export_CE_cannot_be_named_for_leaves_SYM_disabled()
    {
        var (vm, _) = Panel(new EngineState
        {
            GWorldAddr = "0x7FF8B739CB88", GWorldExport = "?Foo@Bar@@3HA", ModuleName = "Game.exe",
        });
        Assert.False(vm.CanRegisterGWorldSymbol);
    }
}
