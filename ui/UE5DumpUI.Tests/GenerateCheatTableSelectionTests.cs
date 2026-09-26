using UE5DumpUI.Models;
using UE5DumpUI.ViewModels;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// [GENCT-ONE-ROW] Generate CT refuses only an EMPTY selection (its guard is Count == 0), and
/// a single row makes a table. The refusal asked for "2+ rows", which sent a user with one
/// row looking for a second.
/// </summary>
public class GenerateCheatTableSelectionTests
{
    [Fact]
    public void Interesting_Funcs_empty_selection_asks_for_at_least_one_row()
    {
        var vm = new InterestingFunctionsViewModel(new StubDumpService(), new MockLoggingService());
        vm.GenerateCheatTableCommand.Execute(new List<ScoredFunctionRow>());
        Assert.DoesNotContain("2+", vm.StatusText, StringComparison.Ordinal);
        Assert.Contains("at least one row", vm.StatusText, StringComparison.Ordinal);
    }

    [Fact]
    public void Interesting_Props_empty_selection_asks_for_at_least_one_row()
    {
        var vm = new InterestingPropertiesViewModel(new StubDumpService(), new MockLoggingService());
        vm.GenerateCheatTableCommand.Execute(new List<ScoredPropertyRow>());
        Assert.DoesNotContain("2+", vm.StatusText, StringComparison.Ordinal);
        Assert.Contains("at least one row", vm.StatusText, StringComparison.Ordinal);
    }
}
