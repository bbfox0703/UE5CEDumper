using UE5DumpUI.Models;
using UE5DumpUI.Services;
using UE5DumpUI.ViewModels;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// Tests for the per-row "🌍 Locate in GWorld" handoff added to the Interesting
/// Properties panel. Mirrors the Interesting Functions LocateRowInGWorld contract:
/// the command raises <see cref="InterestingPropertiesViewModel.LocateInGWorld"/>
/// with the row's class name ONLY when GWorld is available and the row is valid —
/// MainWindow then resolves a representative live instance and runs the path search.
/// </summary>
public class InterestingPropertiesViewModelTests
{
    private static InterestingPropertiesViewModel MakeVm()
        => new(new StubDumpService(), new MockLoggingService());

    private static ScoredPropertyRow Row(string className) => new()
    {
        Match             = new PropertySearchMatch { ClassName = className, PropName = "Health" },
        FinalScore        = 10,
        Category          = PropertyCategory.Stats,
        KeywordHits       = 1,
        ClassBonus        = 0,
        IsUnusualLocation = false,
    };

    [Fact]
    public void LocateRowInGWorld_GWorldAvailable_FiresWithClassName()
    {
        var vm = MakeVm();
        string? captured = null;
        vm.LocateInGWorld += cls => captured = cls;

        vm.LocateRowInGWorldCommand.Execute(Row("BP_PlayerState_C"));

        Assert.Equal("BP_PlayerState_C", captured);
    }

    [Fact]
    public void LocateRowInGWorld_FiresEvenWhenTheClientFlagIsFalse()
    {
        // Locate is not gated on a client-side GWorld flag. "A property is a
        // class-level definition, so without a resolved GWorld there is nothing to
        // locate against" has a false premise: a client flag does NOT mean "GWorld is
        // resolved". EngineState.HasGWorld means "the AOB scan produced a non-zero
        // &GWorld SLOT address" — while the DLL has world-recovery fallbacks that work
        // when that scan did not. Gated on it, the button would be dead on games where
        // locate works fine (TQ2, proxy mode).
        //
        // The DLL's find_path_from_gworld is the source of truth and returns an
        // explicit invalid/no-path status when there really is no live UWorld, which
        // the locate flow surfaces.
        var vm = MakeVm();
        // (the VM has no GWorld-availability flag to set false)
        string? captured = null;
        vm.LocateInGWorld += a => captured = a;

        vm.LocateRowInGWorldCommand.Execute(Row("BP_PlayerState_C"));

        Assert.Equal("BP_PlayerState_C", captured);
    }

    [Fact]
    public void LocateRowInGWorld_NullRow_DoesNotFire()
    {
        var vm = MakeVm();
        bool fired = false;
        vm.LocateInGWorld += _ => fired = true;

        vm.LocateRowInGWorldCommand.Execute(null);

        Assert.False(fired);
    }

    [Fact]
    public void LocateRowInGWorld_EmptyClassName_DoesNotFire()
    {
        var vm = MakeVm();
        bool fired = false;
        vm.LocateInGWorld += _ => fired = true;

        vm.LocateRowInGWorldCommand.Execute(Row(""));

        Assert.False(fired);
    }
}
