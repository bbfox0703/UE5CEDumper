using UE5DumpUI.Helpers;
using UE5DumpUI.Models;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// Self-Test failure advice — [PEHOOK-2026-08-17].
///
/// The old message asserted one cause for every wrong answer: "Hook may be on the
/// wrong vtable slot … re-deploy the DLL". Both halves were wrong. Re-deploying
/// cannot fix a mis-detected slot (the binary is current; the detected offset is
/// what is wrong), and the test's own evidence — arguments written, return slot
/// untouched — is byte-for-byte what <c>docs/working-lessons.md</c> §4.4 documents
/// for a helper that no-ops through ProcessEvent with a perfectly CORRECT hook.
///
/// So the message must be driven by a measurement, not by a guess, and must say
/// nothing at all when the measurement is unavailable. That is what is pinned here.
/// </summary>
public class SelfTestAdviceTests
{
    [Fact]
    public void HookFiring_MeansTheHookIsNotTheProblem()
    {
        // A hook that fires is, by construction, sitting on the function the game
        // calls constantly — the slot is right, so a wrong return value is §4.4's
        // BlueprintFastCall no-op and NOT evidence of a broken invoke path.
        var cause = SelfTestAdvice.Classify(0, haveDiagnostics: true, hookActive: true, hookHasFired: true);

        Assert.Equal(SelfTestFailureCause.HookFiring, cause);
        Assert.Equal(SelfTestAdvice.KeyHookFiring, SelfTestAdvice.KeyFor(cause));
    }

    [Fact]
    public void HookInstalledButNeverFired_IsTheMisdetectionSignature()
    {
        // The DumperTest reading: installed at the version-table guess, zero traffic.
        var cause = SelfTestAdvice.Classify(0, haveDiagnostics: true, hookActive: true, hookHasFired: false);

        Assert.Equal(SelfTestFailureCause.HookNeverFired, cause);
        Assert.Equal(SelfTestAdvice.KeyHookNeverFired, SelfTestAdvice.KeyFor(cause));
    }

    [Fact]
    public void NoHook_AdvisesTheDetectionPath()
    {
        var cause = SelfTestAdvice.Classify(0, haveDiagnostics: true, hookActive: false, hookHasFired: false);

        Assert.Equal(SelfTestFailureCause.HookNotInstalled, cause);
        Assert.Equal(SelfTestAdvice.KeyHookNotInstalled, SelfTestAdvice.KeyFor(cause));
    }

    [Fact]
    public void InactiveHookWins_EvenWhenSomethingFiredEarlier()
    {
        // The fire count is process-wide and survives a soft-disable, so a leftover
        // non-zero count says nothing about the call that just failed. "Not
        // installed" is the honest reading of an inactive hook.
        var cause = SelfTestAdvice.Classify(0, haveDiagnostics: true, hookActive: false, hookHasFired: true);

        Assert.Equal(SelfTestFailureCause.HookNotInstalled, cause);
    }

    /// <summary>
    /// THE WORST-MESSAGE GUARD. When the DLL refuses the invoke it returns non-zero
    /// and leaves the parameter buffer untouched — byte-identical to a call that ran
    /// and wrote nothing. Read the hook telemetry alone and a refused call on a
    /// healthy, FIRING hook gets explained as "the invoke path itself is working,
    /// so the helper must be a BlueprintFastCall no-op" — every clause false, and
    /// the suggested next step refused identically. The result code outranks all
    /// hook state, including the state that looks healthiest.
    /// </summary>
    [Theory]
    [InlineData(-3, true,  true)]   // refused while the hook is up AND firing
    [InlineData(-3, true,  false)]
    [InlineData(-3, false, false)]
    [InlineData(-7, true,  true)]   // hook not active at enqueue time
    [InlineData(-5, true,  true)]   // game-thread timeout
    [InlineData(-8, true,  true)]   // background-worker refusal
    public void ARefusedInvokeIsNeverExplainedAsAHookVerdict(int result, bool hookActive, bool hookHasFired)
    {
        var cause = SelfTestAdvice.Classify(result, haveDiagnostics: true, hookActive, hookHasFired);

        Assert.Equal(SelfTestFailureCause.NotDispatched, cause);
        Assert.Equal(SelfTestAdvice.KeyNotDispatched, SelfTestAdvice.KeyFor(cause));
    }

    /// <summary>A refused call is refused whether or not diagnostics were readable —
    /// it needs no telemetry to explain.</summary>
    [Fact]
    public void RefusalOutranksEvenAMissingMeasurement()
    {
        Assert.Equal(SelfTestFailureCause.NotDispatched,
                     SelfTestAdvice.Classify(-3, haveDiagnostics: false, hookActive: false, hookHasFired: false));
    }

    /// <summary>
    /// THE REGRESSION GUARD. An absent measurement is not evidence for either
    /// cause. When the diagnostics probe fails, the advice must name both
    /// possibilities rather than defaulting to one — defaulting is the original bug.
    /// </summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(true,  false)]
    [InlineData(false, true)]
    [InlineData(true,  true)]
    public void NoDiagnostics_ClaimsNothing(bool hookActive, bool hookHasFired)
    {
        var cause = SelfTestAdvice.Classify(0, haveDiagnostics: false, hookActive, hookHasFired);

        Assert.Equal(SelfTestFailureCause.Unknown, cause);
        Assert.Equal(SelfTestAdvice.KeyUnknown, SelfTestAdvice.KeyFor(cause));
    }

    /// <summary>
    /// The classifier's second input comes from <see cref="DiagnosticsGameThread.HasFired"/>,
    /// which is a DERIVED property — it needs BOTH a positive count and a real age.
    /// A DLL that reports "never fired" as -1 must not be read as "fired".
    /// </summary>
    [Fact]
    public void HasFired_RequiresBothCountAndAge()
    {
        var neverFired = new DiagnosticsGameThread { HookActive = true, HookFireCount = 0, MsSinceLastFire = -1 };
        var firing     = new DiagnosticsGameThread { HookActive = true, HookFireCount = 4210, MsSinceLastFire = 3 };

        Assert.False(neverFired.HasFired);
        Assert.True(firing.HasFired);

        Assert.Equal(SelfTestFailureCause.HookNeverFired,
                     SelfTestAdvice.Classify(0, true, neverFired.HookActive, neverFired.HasFired));
        Assert.Equal(SelfTestFailureCause.HookFiring,
                     SelfTestAdvice.Classify(0, true, firing.HookActive, firing.HasFired));
    }

    /// <summary>Every cause maps to a distinct, non-empty en.axaml key — a collision
    /// would silently print one cause's advice for another's evidence.</summary>
    [Fact]
    public void EveryCauseHasItsOwnKey()
    {
        // Enumerated from the enum itself, so a new cause added without a key
        // fails here instead of silently mapping to the Unknown default.
        var keys = Enum.GetValues<SelfTestFailureCause>()
                       .Select(SelfTestAdvice.KeyFor)
                       .ToArray();
        Assert.Equal(5, keys.Length);

        Assert.All(keys, k => Assert.StartsWith("str.System.SelfTest.Fail.", k));
        Assert.Equal(keys.Length, new HashSet<string>(keys).Count);
    }

    /// <summary>
    /// Every advice key must actually EXIST in en.axaml. <c>Res.Get</c> returns an
    /// empty string for a key it cannot find, so a typo does not throw and does not
    /// log — it silently prints a failure message with no advice in it, which is
    /// indistinguishable from the advice being deliberately blank.
    /// </summary>
    [Fact]
    public void EveryAdviceKeyExistsInEnAxaml()
    {
        var axaml = FindRepoFile(Path.Combine("ui", "UE5DumpUI", "Resources", "Strings", "en.axaml"));
        Assert.NotNull(axaml);   // shipped artifact — not finding it is a real failure
        var text = File.ReadAllText(axaml!);

        var keys = Enum.GetValues<SelfTestFailureCause>()
                       .Select(SelfTestAdvice.KeyFor)
                       .Append("str.System.SelfTest.Fail")
                       .Distinct();
        foreach (var key in keys)
            Assert.Contains($"x:Key=\"{key}\"", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// The advice must never tell the user that re-deploying the DLL is a remedy.
    /// That was the old message's instruction and it cannot help: a mis-detected
    /// vtable slot is not a staleness problem, and the one measured case had a
    /// current DLL.
    ///
    /// <para>This pins the RULE, not the old sentence. An earlier version banned the
    /// exact literal "and re-deploy the DLL", which "redeploy the DLL", "deploy the
    /// DLL again" and any reword would have walked straight past.</para>
    /// </summary>
    [Fact]
    public void NoAdviceRecommendsRedeploying()
    {
        var axaml = FindRepoFile(Path.Combine("ui", "UE5DumpUI", "Resources", "Strings", "en.axaml"));
        Assert.NotNull(axaml);

        int checkedLines = 0;
        foreach (var line in File.ReadAllLines(axaml!))
        {
            if (!line.Contains("str.System.SelfTest.Fail", StringComparison.Ordinal)) continue;
            checkedLines++;
            bool mentions = line.Contains("re-deploy", StringComparison.OrdinalIgnoreCase)
                         || line.Contains("redeploy",  StringComparison.OrdinalIgnoreCase)
                         || line.Contains("deploy the DLL again", StringComparison.OrdinalIgnoreCase);
            if (!mentions) continue;

            // Mentioning it is fine ONLY to rule it out.
            bool ruledOut = line.Contains("NOT help", StringComparison.OrdinalIgnoreCase)
                         || line.Contains("not fix",  StringComparison.OrdinalIgnoreCase)
                         || line.Contains("not change", StringComparison.OrdinalIgnoreCase);
            Assert.True(ruledOut,
                $"A Self-Test advice string mentions re-deploying without ruling it out: {line.Trim()}");
        }

        // Guard the guard: if the key prefix is ever renamed this loop would
        // silently inspect nothing and pass.
        Assert.True(checkedLines >= 5, $"expected >=5 advice lines, scanned {checkedLines}");
    }

    private static string? FindRepoFile(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (int i = 0; i < 8 && dir != null; i++, dir = dir.Parent)
        {
            string candidate = Path.Combine(dir.FullName, relative);
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }
}
