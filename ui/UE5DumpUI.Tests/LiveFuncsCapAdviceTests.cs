using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// [LIVEFUNCS-CAP-ADVICE] Live Funcs fetches only the top FetchLimit functions by count, and its
/// filter runs over the rows already fetched (ApplyFilter over _allEntries), so the filter cannot
/// bring back a function that fell below the cut. The capped-fetch warning advised "use the
/// filter"; what helps is a higher Fetch limit (the user's since 2026-10-06) or a shorter
/// recording window.
/// </summary>
public class LiveFuncsCapAdviceTests
{
    [Fact]
    public void Capped_fetch_warning_does_not_offer_the_filter_as_a_remedy()
    {
        var vm = File.ReadAllText(NumericInputCoercionTests.RepoFile("ui/UE5DumpUI/ViewModels/LiveFuncsViewModel.cs"));
        Assert.DoesNotContain("use the filter before trusting", vm, StringComparison.Ordinal);
        Assert.Contains("shorter recording window", vm, StringComparison.Ordinal);
        Assert.Contains("higher Fetch limit", vm, StringComparison.Ordinal);
    }
}
