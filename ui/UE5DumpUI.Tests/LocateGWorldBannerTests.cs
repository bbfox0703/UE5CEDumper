using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UE5DumpUI.Models;
using UE5DumpUI.ViewModels;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// Tests for the Live Walker "Locate in GWorld" failure banner. A failed locate
/// used to clear the grid (HasData=false) and bury the reason in the low-contrast
/// top status line — visually identical to the idle app-logo empty state. The fix
/// surfaces the reason in <see cref="LiveWalkerViewModel.LocateFailureMessage"/> as a
/// prominent in-grid banner and suppresses the idle logo while it shows.
/// </summary>
public class LocateGWorldBannerTests
{
    private sealed class PathStub : StubDumpService
    {
        public GWorldPathResult Next = new();
        public override Task<GWorldPathResult> FindPathFromGWorldAsync(
            string target, string? objectAddr = null, int maxDepth = 5, CancellationToken ct = default,
            string rootKind = "gworld", bool deep = false, int containerDepth = 1)
            => Task.FromResult(Next);
    }

    // Depth 7 (>= RecommendedGWorldLocateDepth) so these banner tests don't trip
    // the low-depth confirm gate (which, headless, returns false = cancel and would
    // skip the search these tests exercise). The gate itself is covered separately.
    private static LiveWalkerViewModel MakeVm(PathStub stub)
        => new(stub, new MockLoggingService(), new MockPlatformService(Path.GetTempPath()))
        {
            GWorldLocateDepth = 7,
        };

    [Fact]
    public async Task LocateInGWorld_NotReachable_RaisesBanner_AndHidesLogo()
    {
        var stub = new PathStub
        {
            Next = new GWorldPathResult { Found = false, Status = "not_reachable", Visited = 1234 },
        };
        var vm = MakeVm(stub);

        await vm.LocateInGWorldAsync("0x1000", 0, null, stopAtParent: false,
            ct: TestContext.Current.CancellationToken);

        Assert.True(vm.HasLocateFailure);
        Assert.Contains("Not reachable", vm.LocateFailureMessage);
        Assert.False(vm.HasData);
        Assert.False(vm.ShowEmptyStateLogo);          // banner takes over the empty area
        Assert.True(string.IsNullOrEmpty(vm.StatusText)); // reason is in the banner, not the top line
    }

    [Fact]
    public async Task LocateInGWorld_Cancelled_NoBanner_KeepsStatusLine()
    {
        var stub = new PathStub
        {
            Next = new GWorldPathResult { Found = false, Status = "cancelled" },
        };
        var vm = MakeVm(stub);

        await vm.LocateInGWorldAsync("0x1000", 0, null, stopAtParent: false,
            ct: TestContext.Current.CancellationToken);

        // A user-initiated cancel must NOT raise the failure banner — it preserves the
        // current view and reports via the mild top status line instead.
        Assert.False(vm.HasLocateFailure);
        Assert.Contains("cancelled", vm.StatusText);
    }

    [Fact]
    public async Task LocateInGWorld_SuccessAfterFailure_ClearsBanner()
    {
        var stub = new PathStub
        {
            Next = new GWorldPathResult { Found = false, Status = "not_reachable", Visited = 1 },
        };
        var vm = MakeVm(stub);

        await vm.LocateInGWorldAsync("0x1000", 0, null, stopAtParent: false,
            ct: TestContext.Current.CancellationToken);
        Assert.True(vm.HasLocateFailure);

        // A fresh locate attempt clears the prior banner up-front (ClearStatus) even
        // before its own result lands.
        stub.Next = new GWorldPathResult { Found = false, Status = "cancelled" };
        await vm.LocateInGWorldAsync("0x2000", 0, null, stopAtParent: false,
            ct: TestContext.Current.CancellationToken);

        Assert.False(vm.HasLocateFailure);
    }

    [Fact]
    public async Task LocateInGameEngine_NotReachable_BannerMentionsGameEngine()
    {
        // The engine-rooted variant must surface a GameEngine-specific reason (an
        // engine root reaches engine-layer objects best; most world actors are easier
        // via 🌍 Locate in GWorld). It still suggests raising depth / Deep, since a
        // not_reachable is depth-bounded, not a proof of non-existence.
        var stub = new PathStub
        {
            Next = new GWorldPathResult { Found = false, Status = "not_reachable", Visited = 42 },
        };
        var vm = MakeVm(stub);

        await vm.LocateInGameEngineAsync("0x1000", 0, null, stopAtParent: false,
            ct: TestContext.Current.CancellationToken);

        Assert.True(vm.HasLocateFailure);
        Assert.Contains("GameEngine", vm.LocateFailureMessage);
    }

    [Fact]
    public async Task LocateInGameEngine_NoEngine_BannerExplains()
    {
        var stub = new PathStub
        {
            Next = new GWorldPathResult { Found = false, Status = "no_engine" },
        };
        var vm = MakeVm(stub);

        await vm.LocateInGameEngineAsync("0x1000", 0, null, stopAtParent: false,
            ct: TestContext.Current.CancellationToken);

        Assert.True(vm.HasLocateFailure);
        Assert.Contains("UGameEngine", vm.LocateFailureMessage);
    }

    [Fact]
    public async Task LocateInGWorld_LowDepth_ConfirmGate_SkipsSearchOnCancel_ThenQuietForSession()
    {
        // depth < RecommendedGWorldLocateDepth (7) trips the confirm gate. Headless,
        // ConfirmDialog returns false (no owner window) = "cancel", so the first
        // low-depth locate skips the search entirely — no failure banner.
        var stub = new PathStub
        {
            Next = new GWorldPathResult { Found = false, Status = "not_reachable", Visited = 1 },
        };
        var vm = new LiveWalkerViewModel(stub, new MockLoggingService(),
            new MockPlatformService(Path.GetTempPath()))
        {
            GWorldLocateDepth = 5,   // below the threshold
        };

        await vm.LocateInGWorldAsync("0x1000", 0, null, stopAtParent: false,
            ct: TestContext.Current.CancellationToken);
        Assert.False(vm.HasLocateFailure);   // gate cancelled → search never ran

        // Warned once → the gate stays quiet for the session, so a second low-depth
        // locate proceeds and the stubbed not_reachable now raises the banner.
        await vm.LocateInGWorldAsync("0x1000", 0, null, stopAtParent: false,
            ct: TestContext.Current.CancellationToken);
        Assert.True(vm.HasLocateFailure);
    }

    // ── AE10 step 2: the FAILURE branch, closed on the REPLY SPACE rather than by clicking ──
    //
    // The row asked for the 🌍 button to be clicked on an object unreachable from GWorld. Two
    // attempts to arrange that failed for reasons unrelated to the claim (a Package row exposes no
    // detail strip; CDOs turn out to be reachable through the class chain), and a third would have
    // been a fourth search for a subject nobody has found. But "click something unreachable" is a
    // GAME PROCEDURE, not the assertion. The 繁中 row states the actual FAIL condition as
    // 沒有任何訊息、靜默無反應 — no message, a silent no-op — which is a pure function of
    // GWorldPathResult.Status through GWorldPathFailureStatus, injectable with PathStub.
    //
    // ⚠ The row's own wording names two statuses that cannot occur: "no_path" appears NOWHERE in
    // the DLL or the UI (the real one is `not_reachable`), and `invalid` cannot reach this switch
    // through this command — Fern answers the precondition failures as no_gworld / no_engine /
    // invalid_target first. Chasing either by hand would have found nothing, forever.

    [Theory]
    [InlineData("deadline",       "timed out")]
    [InlineData("visited_cap",    "too large")]
    [InlineData("no_gworld",      "GWorld is not available")]
    [InlineData("invalid_target", "Could not resolve the target object")]
    public async Task EveryKnownFailureStatus_RaisesTheBanner_WithItsOwnExplanation(
        string status, string expected)
    {
        // The other three arms already have tests above: not_reachable (both root labels),
        // no_engine ("UGameEngine"), and cancelled — which is the deliberate NON-banner control.
        var stub = new PathStub { Next = new GWorldPathResult { Found = false, Status = status, Visited = 7 } };
        var vm = MakeVm(stub);

        await vm.LocateInGWorldAsync("0x1000", 0, null, stopAtParent: false,
            ct: TestContext.Current.CancellationToken);

        Assert.True(vm.HasLocateFailure, status + ": no banner — this is the silent no-op the row is about");
        Assert.Contains(expected, vm.LocateFailureMessage);
        Assert.False(vm.ShowEmptyStateLogo);
        Assert.False(vm.HasData);
        Assert.True(string.IsNullOrEmpty(vm.StatusText));
    }

    /// <summary>
    /// ⭐ THE ONE THAT ACTUALLY CLOSES THE ROW, because it does not enumerate statuses.
    ///
    /// <para>Adding one <c>[InlineData]</c> per known status can only ever keep pace with the
    /// switch; a status added to the DLL tomorrow, or a typo in one, is exactly the case that would
    /// fall through — and falling through is what the row fears. So assert the STRUCTURAL property
    /// instead: <c>GWorldPathFailureStatus</c>'s default arm is
    /// <c>$"No {rootLabel} path found ({path.Status})."</c>, which cannot be empty for ANY input,
    /// including an empty status string. Therefore every <c>Found=false</c> reply that is not
    /// <c>cancelled</c> raises a non-empty banner — statuses that do not exist yet included.</para>
    ///
    /// <para>The inputs are chosen to be un-guessable rather than plausible: a status the DLL
    /// genuinely cannot send here (<c>invalid</c>), the row's own phantom (<c>no_path</c>), a
    /// future-shaped one, and the empty string — which is what a dropped JSON field decodes to.</para>
    /// </summary>
    [Theory]
    [InlineData("no_path")]            // the row's phantom — no code anywhere emits it
    [InlineData("invalid")]            // real in Aura.cpp, unreachable through this command
    [InlineData("reconstruct_error")]  // shaped like a future addition
    [InlineData("")]                   // a dropped/blank field on the wire
    public async Task AnUnknownFailureStatus_StillRaisesANonEmptyBanner_NeverASilentNoOp(string status)
    {
        var stub = new PathStub { Next = new GWorldPathResult { Found = false, Status = status, Visited = 0 } };
        var vm = MakeVm(stub);

        await vm.LocateInGWorldAsync("0x1000", 0, null, stopAtParent: false,
            ct: TestContext.Current.CancellationToken);

        Assert.True(vm.HasLocateFailure,
            $"status \"{status}\": the locate failed and said NOTHING — that is the exact 繁中 FAIL "
            + "condition 沒有任何訊息、靜默無反應.");
        Assert.False(string.IsNullOrWhiteSpace(vm.LocateFailureMessage));
        Assert.Contains("No GWorld path found", vm.LocateFailureMessage);
        Assert.False(vm.ShowEmptyStateLogo);
    }

    [Fact]
    public void HasDataBecomingTrue_RetiresBannerStructurally()
    {
        // Some Live Walker nav paths set HasData directly and bypass UpdateDisplay
        // (world-root / container drill / synthetic-container). The banner→clear must
        // be a structural invariant (OnHasDataChanged), not dependent on UpdateDisplay
        // or each caller's ClearStatus discipline.
        var vm = MakeVm(new PathStub());
        vm.LocateFailureMessage = "Not reachable — nothing references this object.";
        Assert.True(vm.HasLocateFailure);

        vm.HasData = true;   // any path that displays real data

        Assert.False(vm.HasLocateFailure);
        Assert.False(vm.ShowEmptyStateLogo);
    }
}
