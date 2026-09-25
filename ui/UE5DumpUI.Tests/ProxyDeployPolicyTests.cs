using System;
using System.IO;
using System.Linq;
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

    // ------------------------------------------------------------------
    // [PROXY-DOUBLE-GUARD] Deploy never adds a SECOND of our proxy types.
    // ------------------------------------------------------------------
    // Maintainer request: a Select-All Deploy over games that already carry another of our proxies made doubles
    // that then had to be fixed game by game -- and Undeploy removes both, so the user must remember which one
    // worked. Neither Force Overwrite (same type, any version) nor foreign consent (another program's file) is a
    // licence to add a second type.

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void PlanDeploy_TargetAbsent_OtherOurs_RefusedEvenWithForceOrConsent(bool force, bool consent)
        => Assert.Equal(DeployVerdict.OtherProxyOfOurs, ProxyDeployService.PlanDeploy(
            targetExists: false, targetIsOurs: false, sameVersion: false, new DeployOptions(force, consent),
            otherOfOursPresent: true));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PlanDeploy_ForeignTarget_OtherOurs_RefusedEvenWithConsent(bool force)
        => Assert.Equal(DeployVerdict.OtherProxyOfOurs, ProxyDeployService.PlanDeploy(
            targetExists: true, targetIsOurs: false, sameVersion: false, new DeployOptions(force, ForeignConsent: true),
            otherOfOursPresent: true));

    [Fact]
    public void PlanDeploy_TargetOurs_OtherOurs_FollowsTheSameTypeRules()
    {
        // The folder is already doubled; redeploying the type that is THERE adds nothing, so Force decides as usual.
        Assert.Equal(DeployVerdict.AlreadyCurrent, ProxyDeployService.PlanDeploy(
            true, true, sameVersion: true, new DeployOptions(false, false), otherOfOursPresent: true));
        Assert.Equal(DeployVerdict.Proceed, ProxyDeployService.PlanDeploy(
            true, true, sameVersion: true, new DeployOptions(ForceSameVersion: true, false), otherOfOursPresent: true));
        Assert.Equal(DeployVerdict.Proceed, ProxyDeployService.PlanDeploy(
            true, true, sameVersion: false, new DeployOptions(false, false), otherOfOursPresent: true));
    }

    [Fact]
    public void OursPresent_ListsOnlyOurProxies_AtOurNames()
    {
        string dir = Path.Combine(Path.GetTempPath(), "ue5-ours-present", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            foreach (var n in new[] { "version.dll", "winmm.dll", "dxgi.dll", "other.dll" })
                File.WriteAllBytes(Path.Combine(dir, n), new byte[] { 0x4D, 0x5A });
            // dxgi.dll here is ANOTHER program's (ReShade, say): foreign files are AC1's business, not the guard's.
            var ours = ProxyDeployService.OursPresent(dir, p => !p.EndsWith("dxgi.dll", StringComparison.OrdinalIgnoreCase));

            Assert.Equal(new[] { "version.dll", "winmm.dll" }, ours.OrderBy(x => x, StringComparer.Ordinal));
        }
        finally { try { Directory.Delete(dir, true); } catch { /* best effort */ } }
    }

    [Fact]
    public async System.Threading.Tasks.Task DeployAsync_Backstop_RefusesASecondOfOurTypes_WithoutWriting()
    {
        // The view model pre-checks; the service must refuse anyway, for any caller. Ownership is a PE ProductName
        // read, so the probe is replaced (a fabricated PE would test the fixture, not the wiring).
        string dir = Path.Combine(Path.GetTempPath(), "ue5-backstop", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllBytes(Path.Combine(dir, "winmm.dll"), new byte[] { 0x4D, 0x5A });
            string source = Path.Combine(dir, "source-version.dll");
            File.WriteAllBytes(source, new byte[] { 0x4D, 0x5A });
            var svc = new ProxyDeployService(new MockLoggingService(), new MockPlatformService(dir))
            {
                OwnerProbe = p => p.EndsWith("winmm.dll", StringComparison.OrdinalIgnoreCase)
                    ? DllOwner.Ours : DllOwner.NotOurs,
            };
            var game = new DetectedGame { Name = "G", BinariesDir = dir, ExePath = Path.Combine(dir, "G.exe") };

            bool ok = await svc.DeployAsync(source, game, ProxyType.Version,
                new DeployOptions(ForceSameVersion: true, ForeignConsent: true), TestContext.Current.CancellationToken);

            Assert.False(ok);
            Assert.False(File.Exists(Path.Combine(dir, "version.dll")));
            Assert.Equal(ProxyDeployStatus.DeployedOtherType, game.Status);
            Assert.StartsWith("Skipped:", game.StatusDetail);
        }
        finally { try { Directory.Delete(dir, true); } catch { /* best effort */ } }
    }

    // ── [PROXY-PRODUCTNAME-UNREADABLE] a third state: a proxy-named file we cannot read ──
    //
    // Measured (.NET 10, the UI's ownership read): no version resource, a file ACL denying read, and a file held open
    // with FileShare.None ALL give ProductName == null with no exception. Our DLLs always carry a version resource, so
    // "readable, no resource" is not ours (maintainer: that stays); "could not read it at all" is UNREADABLE --
    // never written or deleted, and said so, instead of "another program's".

    private static string TempDir()
    {
        string d = Path.Combine(Path.GetTempPath(), "ue5-unreadable", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(d);
        return d;
    }

    [Fact]
    public void ReadOwner_ReadableWithoutAVersionResource_IsNotOurs()
    {
        string d = TempDir();
        try
        {
            string f = Path.Combine(d, "version.dll");
            File.WriteAllText(f, "not a PE");
            Assert.Equal(DllOwner.NotOurs, ProxyDeployService.ReadOwner(f));
        }
        finally { try { Directory.Delete(d, true); } catch { /* best effort */ } }
    }

    [Fact]
    public void ReadOwner_HeldOpenWithNoSharing_IsUnreadable()
    {
        string d = TempDir();
        try
        {
            string f = Path.Combine(d, "version.dll");
            File.WriteAllText(f, "not a PE");
            using (new FileStream(f, FileMode.Open, FileAccess.Read, FileShare.None))
                Assert.Equal(DllOwner.Unreadable, ProxyDeployService.ReadOwner(f));
            Assert.Equal(DllOwner.NotOurs, ProxyDeployService.ReadOwner(f));   // released: readable again
        }
        finally { try { Directory.Delete(d, true); } catch { /* best effort */ } }
    }

    // ── the second skeptic review (wf_af037a8c-153) on the unreadable state ──

    [Fact]
    public void ReadOwner_HeldByAWriterThatSharesRead_IsUnreadable()
    {
        // (UNREAD-WRITER-SHARE, measured) The fallback open shared Write, so a proxy held by a writer that shares
        // read (a hex editor, a mod manager, Python's open) opened fine and read as "another program's".
        string d = TempDir();
        try
        {
            string f = Path.Combine(d, "version.dll");
            File.WriteAllText(f, "not a PE");
            using (new FileStream(f, FileMode.Open, FileAccess.ReadWrite, FileShare.Read))
                Assert.Equal(DllOwner.Unreadable, ProxyDeployService.ReadOwner(f));
        }
        finally { try { Directory.Delete(d, true); } catch { /* best effort */ } }
    }

    [Theory]
    [InlineData(1, false)]   // removed one, left an unreadable one: NOT "nothing of ours left behind"
    [InlineData(0, false)]
    public void Undeploy_LeavingAnUnreadableFile_IsNotASuccess(int removed, bool success)
    {
        // (UNREAD-UNDEPLOY-NOTE-WIPED) A success let the view model's refresh wipe the 'Cannot read' note at once --
        // and the contract is 'true when nothing of ours is left behind', which an unreadable file may be.
        var (status, message, ok) = ProxyDeployService.ResolveUndeployOutcome(
            removed, Array.Empty<string>(), Array.Empty<string>(), new[] { "winmm.dll" });
        Assert.Equal(success, ok);
        Assert.Equal(ProxyDeployStatus.Unreadable, status);
        Assert.Contains("Cannot read winmm.dll", message);
    }

    [Fact]
    public async System.Threading.Tasks.Task Service_AnUnreadableFileAtAnotherName_IsReportedAndSkipped_AsUnreadable()
    {
        // (UNREAD-REFRESH-OTHERNAME) The refresh showed a clean NotDeployed for a folder whose only proxy-named file
        // could not be read -- the folder Deploy then skipped. (UNREAD-RESULTLINE-CLAIMS-OURS) The backstop called it
        // DeployedOtherType, 'another of OUR types', for a file it cannot tell the owner of.
        string d = TempDir();
        try
        {
            File.WriteAllBytes(Path.Combine(d, "winmm.dll"), new byte[] { 0x4D, 0x5A });
            string source = Path.Combine(d, "source-version.dll");
            File.WriteAllBytes(source, new byte[] { 0x4D, 0x5A });
            var svc = new ProxyDeployService(new MockLoggingService(), new MockPlatformService(d))
            {
                OwnerProbe = p => p.EndsWith("winmm.dll", StringComparison.OrdinalIgnoreCase)
                    ? DllOwner.Unreadable : DllOwner.NotOurs,
            };
            var game = new DetectedGame { Name = "G", BinariesDir = d, ExePath = Path.Combine(d, "G.exe") };
            var ct = TestContext.Current.CancellationToken;

            await svc.RefreshDeployStatusAsync(new[] { game }, source, ProxyType.Version, ct: ct);
            Assert.Equal(ProxyDeployStatus.Unreadable, game.Status);
            Assert.Contains("Cannot read winmm.dll", game.StatusDetail);

            bool ok = await svc.DeployAsync(source, game, ProxyType.Version, new DeployOptions(), ct);
            Assert.False(ok);
            Assert.False(File.Exists(Path.Combine(d, "version.dll")));
            Assert.Equal(ProxyDeployStatus.Unreadable, game.Status);
        }
        finally { try { Directory.Delete(d, true); } catch { /* best effort */ } }
    }

    [Fact]
    public void IsUnreadableDll_IsARequiredMember()
    {
        // (UNREAD-DIM-DEFAULT-FALSE) A default returning false let a wrapper that forgot it silently disable every
        // unreadable guard -- the fallback-to-default shape the first review flagged as T2.
        Assert.True(typeof(UE5DumpUI.Core.IProxyDeployService).GetMethod(nameof(UE5DumpUI.Core.IProxyDeployService.IsUnreadableDll))!.IsAbstract);
    }

    [Fact]
    public void PlanDeploy_AnUnreadableTarget_IsNeverReplaced_ForceAndConsentIncluded()
    {
        Assert.Equal(DeployVerdict.TargetUnreadable, ProxyDeployService.PlanDeploy(
            targetExists: true, targetIsOurs: false, sameVersion: false,
            new DeployOptions(ForceSameVersion: true, ForeignConsent: true), targetUnreadable: true));
    }

    [Fact]
    public async System.Threading.Tasks.Task Service_AnUnreadableTarget_DeployRefuses_RefreshAndUndeploySayWhy()
    {
        string d = TempDir();
        try
        {
            string target = Path.Combine(d, "version.dll");
            File.WriteAllBytes(target, new byte[] { 0x4D, 0x5A, 1, 2 });
            string source = Path.Combine(d, "source-version.dll");
            File.WriteAllBytes(source, new byte[] { 0x4D, 0x5A });
            var svc = new ProxyDeployService(new MockLoggingService(), new MockPlatformService(d))
            {
                OwnerProbe = p => p.EndsWith("\\version.dll", StringComparison.OrdinalIgnoreCase)
                    ? DllOwner.Unreadable : DllOwner.NotOurs,
            };
            var game = new DetectedGame { Name = "G", BinariesDir = d, ExePath = Path.Combine(d, "G.exe") };
            var ct = TestContext.Current.CancellationToken;

            bool ok = await svc.DeployAsync(source, game, ProxyType.Version,
                new DeployOptions(ForceSameVersion: true, ForeignConsent: true), ct);
            Assert.False(ok);
            Assert.Equal(new byte[] { 0x4D, 0x5A, 1, 2 }, File.ReadAllBytes(target));        // untouched
            Assert.Equal(ProxyDeployStatus.Unreadable, game.Status);
            Assert.StartsWith("Cannot read version.dll", game.StatusDetail);

            await svc.RefreshDeployStatusAsync(new[] { game }, source, ProxyType.Version, ct: ct);
            Assert.Equal(ProxyDeployStatus.Unreadable, game.Status);
            Assert.StartsWith("Cannot read version.dll", game.StatusDetail);

            await svc.UndeployAsync(game, ct);
            Assert.True(File.Exists(target));                                               // never deleted
            Assert.Contains("Cannot read version.dll", game.StatusDetail);
            Assert.DoesNotContain("not our proxy", game.StatusDetail);                      // not "another program's"
        }
        finally { try { Directory.Delete(d, true); } catch { /* best effort */ } }
    }

    [Fact]
    public void DescribeOtherTypeSkip_NamesEveryProxy_AndSaysHowToSwitch_WithoutViabilityWording()
    {
        string one = ProxyDeployService.DescribeOtherTypeSkip(new[] { "winmm.dll" });
        string two = ProxyDeployService.DescribeOtherTypeSkip(new[] { "dxgi.dll", "winmm.dll" });

        Assert.StartsWith("Skipped:", one);
        Assert.Contains("winmm.dll", one);
        Assert.Contains("Undeploy first", one);
        Assert.Contains("dxgi.dll", two);
        Assert.Contains("winmm.dll", two);
        // working-lessons §6: never refuse a flavour for whether it can load. This refusal is about a proxy of ours
        // already on disk, and must not read as a load-viability verdict.
        foreach (var t in new[] { one, two })
        {
            Assert.DoesNotContain("cannot load", t, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("import", t, StringComparison.OrdinalIgnoreCase);
        }
    }

    // (third review, UNREAD-NOTE-DOUBLED-OTHERNAME) The row decides whether a note names the unreadable files.
    [Fact]
    public void DetailNamesUnreadable_ReadsWhatTheRefreshWrote()
    {
        string one = ProxyDeployService.DescribeUnreadable(new[] { "winmm.dll" });
        string two = ProxyDeployService.DescribeUnreadable(new[] { "dxgi.dll", "winmm.dll" });
        Assert.True(ProxyDeployService.DetailNamesUnreadable(one, new[] { "winmm.dll" }));
        Assert.True(ProxyDeployService.DetailNamesUnreadable(two, new[] { "winmm.dll", "dxgi.dll" }));
        Assert.True(ProxyDeployService.DetailNamesUnreadable("Multiple proxy DLLs deployed. " + one, new[] { "WINMM.DLL" }));
        Assert.False(ProxyDeployService.DetailNamesUnreadable(one, new[] { "dxgi.dll" }));
        Assert.False(ProxyDeployService.DetailNamesUnreadable(one, new[] { "winmm.dll", "dxgi.dll" }));
        // A preserved row: the failure names the file, but not as unreadable.
        Assert.False(ProxyDeployService.DetailNamesUnreadable("File locked (game running?): winmm.dll", new[] { "winmm.dll" }));
        Assert.False(ProxyDeployService.DetailNamesUnreadable(null, new[] { "winmm.dll" }));
    }
}
