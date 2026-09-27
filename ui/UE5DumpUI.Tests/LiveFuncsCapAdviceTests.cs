using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// [LIVEFUNCS-CAP-ADVICE] Live Funcs fetches only the top FetchLimit functions by count, and its
/// filter runs over the rows already fetched (ApplyFilter over _allEntries), so the filter cannot
/// bring back a function that fell below the cut. The capped-fetch warning advised "use the
/// filter"; only a shorter recording window helps.
/// </summary>
public class LiveFuncsCapAdviceTests
{
    [Fact]
    public void Capped_fetch_warning_does_not_offer_the_filter_as_a_remedy()
    {
        var vm = File.ReadAllText(NumericInputCoercionTests.RepoFile("ui/UE5DumpUI/ViewModels/LiveFuncsViewModel.cs"));
        Assert.DoesNotContain("use the filter before trusting", vm, StringComparison.Ordinal);
        Assert.Contains("shorter recording window", vm, StringComparison.Ordinal);
    }
}
