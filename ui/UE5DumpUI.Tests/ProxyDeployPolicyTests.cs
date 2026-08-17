using UE5DumpUI.Models;
using UE5DumpUI.Services;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// Audit #5 AC1 — the deploy overwrite policy.
///
/// <para>Two policies used to share one <c>bool force</c>:</para>
/// <list type="number">
/// <item>"redeploy over OUR proxy even at the same version" — benign,
/// reversible, and the commoner reason a user ticks the box; <b>persisted</b>
/// in <c>ui-options.json</c> and restored every launch.</item>
/// <item>"replace a file that is provably NOT ours" — irreversible, no backup,
/// no Recycle Bin, and the same successful deploy blanks the row that named the
/// owner, so nothing on screen or on disk records what was destroyed.</item>
/// </list>
///
/// <para>Because one flag armed both, consent given once for (1) became standing
/// cross-session, cross-game authorisation for (2) — applied per game inside the
/// deploy loop, over a Select All that can be an entire Steam library, with no
/// confirmation anywhere on the path.</para>
///
/// <para><b>Why this file tests <c>PlanDeploy</c> and not <c>DeployAsync</c>:</b>
/// ownership is decided by <c>FileVersionInfo.ProductName</c>, and fabricating a
/// PE carrying a version resource would test the fixture, not the policy — the
/// same reasoning <c>ProxyUndeployTests</c> already records for
/// <c>PlanUndeploy</c>.</para>
///
/// <para><b>A green build is not the guard.</b> <see cref="DeployOptions"/> is a
/// record struct with NAMED members precisely so that re-merging the two flags
/// into one breaks compilation rather than quietly restoring the defect. If a
/// future change makes these tests compile against a single bool again, that is
/// the regression, not a refactor.</para>
/// </summary>
public class ProxyDeployPolicyTests
{
    // ------------------------------------------------------------------
    // The regression the whole fix exists for.
    // ------------------------------------------------------------------

    /// <summary>ForceSameVersion must NEVER imply consent to destroy a third
    /// party's DLL. This is the exact combination the persisted checkbox
    /// produces on its own, and it must refuse.</summary>
    [Fact]
    public void ForceSameVersion_DoesNotAuthoriseDestroyingAForeignDll()
    {
        var verdict = ProxyDeployService.PlanDeploy(
            targetExists: true, targetIsOurs: false, sameVersion: false,
            new DeployOptions(ForceSameVersion: true, ForeignConsent: false));

        Assert.Equal(DeployVerdict.NeedsForeignConsent, verdict);
    }

    /// <summary>Same, with every other input varied — the refusal cannot depend
    /// on the version comparison, which is meaningless for a file that is not
    /// ours (DeployAsync never even computes it in that case).</summary>
    [Theory]
    [InlineData(true,  true)]
    [InlineData(true,  false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public void ForeignTarget_WithoutConsent_AlwaysRefuses(bool forceSameVersion, bool sameVersion)
    {
        var verdict = ProxyDeployService.PlanDeploy(
            targetExists: true, targetIsOurs: false, sameVersion: sameVersion,
            new DeployOptions(ForceSameVersion: forceSameVersion, ForeignConsent: false));

        Assert.Equal(DeployVerdict.NeedsForeignConsent, verdict);
    }

    // ------------------------------------------------------------------
    // The rest of the truth table — the capability must still work.
    // ------------------------------------------------------------------

    /// <summary>Explicit foreign consent still replaces the file. The fix
    /// re-scopes the authority; it does not remove the feature the tooltip has
    /// always advertised.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ForeignTarget_WithConsent_Proceeds(bool forceSameVersion)
    {
        var verdict = ProxyDeployService.PlanDeploy(
            targetExists: true, targetIsOurs: false, sameVersion: false,
            new DeployOptions(ForceSameVersion: forceSameVersion, ForeignConsent: true));

        Assert.Equal(DeployVerdict.Proceed, verdict);
    }

    [Fact]
    public void OurTarget_SameVersion_WithoutForce_IsAlreadyCurrent()
    {
        var verdict = ProxyDeployService.PlanDeploy(
            targetExists: true, targetIsOurs: true, sameVersion: true, DeployOptions.None);

        Assert.Equal(DeployVerdict.AlreadyCurrent, verdict);
    }

    [Fact]
    public void OurTarget_SameVersion_WithForce_Redeploys()
    {
        var verdict = ProxyDeployService.PlanDeploy(
            targetExists: true, targetIsOurs: true, sameVersion: true,
            new DeployOptions(ForceSameVersion: true, ForeignConsent: false));

        Assert.Equal(DeployVerdict.Proceed, verdict);
    }

    [Fact]
    public void OurTarget_DifferentVersion_UpgradesWithoutForce()
    {
        var verdict = ProxyDeployService.PlanDeploy(
            targetExists: true, targetIsOurs: true, sameVersion: false, DeployOptions.None);

        Assert.Equal(DeployVerdict.Proceed, verdict);
    }

    /// <summary>Nothing there — no policy question to answer, whatever the
    /// flags say. In particular a missing file must not require foreign
    /// consent, or a first deploy would refuse itself.</summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(true,  false)]
    [InlineData(false, true)]
    [InlineData(true,  true)]
    public void NoExistingFile_AlwaysProceeds(bool forceSameVersion, bool foreignConsent)
    {
        var verdict = ProxyDeployService.PlanDeploy(
            targetExists: false, targetIsOurs: false, sameVersion: false,
            new DeployOptions(ForceSameVersion: forceSameVersion, ForeignConsent: foreignConsent));

        Assert.Equal(DeployVerdict.Proceed, verdict);
    }

    // ------------------------------------------------------------------
    // Defaults — the safe value must be the one you get by saying nothing.
    // ------------------------------------------------------------------

    [Fact]
    public void DefaultOptions_GrantNeitherPolicy()
    {
        DeployOptions defaulted = default;

        Assert.False(defaulted.ForceSameVersion);
        Assert.False(defaulted.ForeignConsent);
        Assert.Equal(DeployOptions.None, defaulted);

        // …and therefore a defaulted call cannot destroy a foreign DLL.
        Assert.Equal(DeployVerdict.NeedsForeignConsent,
            ProxyDeployService.PlanDeploy(true, false, false, defaulted));
    }
}
