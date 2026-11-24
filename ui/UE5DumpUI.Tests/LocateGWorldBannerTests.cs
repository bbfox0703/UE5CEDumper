using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UE5DumpUI.Models;
using UE5DumpUI.ViewModels;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// Tests for the Live Walker "Locate in GWorld" failure banner. A failed locate
/// clears the grid (HasData=false), and a reason left in the low-contrast top status
/// line would look identical to the idle app-logo empty state. So the reason surfaces
/// in <see cref="LiveWalkerViewModel.LocateFailureMessage"/> as a prominent in-grid
/// banner, and the idle logo is suppressed while it shows.
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

    /// <summary>[UI-TEXT-AUDIT-2026-11-18] The tab these texts send the user to is labelled by
    /// str.Tab.InstanceFinder, so they name it by that label: the Value Search inst tooltip (which also says what its
    /// class-name search returns, as the other hand-off tooltips do), the Force templates-only note and the fallback copy
    /// ForceText keeps for a missing key, the Snapshot auto-skip tooltip and this not_reachable explanation.</summary>
    [Fact]
    public async Task TextsSendingTheUserToTheInstancesTab_NameItByItsLabel()
    {
        var tab = UE5DumpUI.Core.Res.Get("str.Tab.InstanceFinder");
        Assert.False(string.IsNullOrEmpty(tab), "str.Tab.InstanceFinder is not in en.axaml");

        var stub = new PathStub
        {
            Next = new GWorldPathResult { Found = false, Status = "not_reachable", Visited = 1 },
        };
        var vm = MakeVm(stub);
        await vm.LocateInGWorldAsync("0x1000", 0, null, stopAtParent: false,
            ct: TestContext.Current.CancellationToken);
        Assert.True(vm.HasLocateFailure);

        var vsTip = UE5DumpUI.Core.Res.Get("str.Tip.VS.OpenInst");
        var texts = new[]
        {
            vsTip,
            UE5DumpUI.Helpers.ForceText.TemplatesOnlyNote("Pawn", 2),
            UE5DumpUI.Core.Res.Get("str.Tip.Snapshot.AutoSkipNoise"),
            vm.LocateFailureMessage,
        };
        foreach (var text in texts)
        {
            Assert.DoesNotContain("Instance Finder", text, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("the " + tab + " tab", text, StringComparison.Ordinal);
        }
        Assert.Contains("class name contains", vsTip, StringComparison.Ordinal);
        Assert.Contains("templates", vsTip, StringComparison.Ordinal);

        // en.axaml always resolves in a test, so the fallback is read from the source.
        var forceText = File.ReadAllText(NumericInputCoercionTests.RepoFile("ui/UE5DumpUI/Helpers/ForceText.cs"));
        Assert.DoesNotContain("Instance Finder", forceText, StringComparison.Ordinal);
    }

    /// <summary>[UI-TEXT-AUDIT-2026-11-18] The DLL's Current Target note for a player with no target-like
    /// reference, which the Related tab shows as its status, names the tab it sends the user to by its label,
    /// str.Tab.InstanceFinder. The note itself is edel_target_test's PICK.05, word for word; this pins the label it
    /// names, which no C++ test can read, so a rename on either side fails one of the two.</summary>
    [Fact]
    public void CurrentTargetNoCandidateNote_NamesTheInstancesTabByItsLabel()
    {
        Assert.Equal("Instances", UE5DumpUI.Core.Res.Get("str.Tab.InstanceFinder"));
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

    // ── The FAILURE branch, closed on the REPLY SPACE rather than by clicking ──
    //
    // "Click 🌍 on an object unreachable from GWorld" is a GAME PROCEDURE, not the assertion. The
    // FAIL condition — no message, a silent no-op — is a pure function of GWorldPathResult.Status
    // through GWorldPathFailureStatus, injectable with PathStub.
    //
    // ⚠ Two status names that look plausible cannot occur here: "no_path" appears NOWHERE in the
    // DLL or the UI (the real one is `not_reachable`), and `invalid` cannot reach this switch
    // through this command — Fern answers the precondition failures as no_gworld / no_engine /
    // invalid_target first. Chasing either by hand would find nothing.

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
    /// ⭐ THE STRUCTURAL ONE: it does not enumerate statuses.
    ///
    /// <para>Adding one <c>[InlineData]</c> per known status can only ever keep pace with the
    /// switch; a status added to the DLL tomorrow, or a typo in one, is exactly the case that would
    /// fall through — and falling through is the silent no-op this guards against. So assert the
    /// STRUCTURAL property instead: <c>GWorldPathFailureStatus</c>'s default arm is
    /// <c>$"No {rootLabel} path found ({path.Status})."</c>, which cannot be empty for ANY input,
    /// including an empty status string. Therefore every <c>Found=false</c> reply that is not
    /// <c>cancelled</c> raises a non-empty banner — statuses that do not exist yet included.</para>
    ///
    /// <para>The inputs are chosen to be un-guessable rather than plausible: a status the DLL
    /// genuinely cannot send here (<c>invalid</c>), a phantom name (<c>no_path</c>), a
    /// future-shaped one, and the empty string — which is what a dropped JSON field decodes to.</para>
    /// </summary>
    [Theory]
    [InlineData("no_path")]            // a phantom — no code anywhere emits it
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
