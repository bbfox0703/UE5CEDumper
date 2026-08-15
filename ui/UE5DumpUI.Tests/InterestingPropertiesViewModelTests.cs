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
        // audit #5 AE10. This assertion used to be the opposite, arguing that "a
        // property is a class-level definition, so without a resolved GWorld there is
        // nothing to locate against". The flaw is in the premise, not the conclusion:
        // IsGWorldAvailable does NOT mean "GWorld is resolved". It comes from
        // EngineState.HasGWorld, whose own definition is "the AOB scan produced a
        // non-zero &GWorld SLOT address" — while the DLL has world-recovery fallbacks
        // that work when that scan did not. Measured consequence: the button was dead
        // on games where locate worked fine (TQ2, proxy mode).
        //
        // The DLL's find_path_from_gworld is the source of truth and returns an
        // explicit invalid/no-path status when there really is no live UWorld, which
        // the locate flow surfaces. Value Search was decoupled from the flag for
        // exactly this reason; seven sibling VMs were not.
        var vm = MakeVm();
        // (the IsGWorldAvailable flag is gone entirely — see pass 2 of AE10)
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
