namespace UE5DumpUI.Helpers;

/// <summary>Why a System → Run Self-Test invoke came back with the wrong value.</summary>
public enum SelfTestFailureCause
{
    /// <summary>The DLL's hook state could not be read, so nothing is claimed.</summary>
    Unknown,
    /// <summary>The DLL REFUSED the call — it never reached ProcessEvent at all.
    /// Outranks every hook-state reading, because none of them describes a call
    /// that did not happen.</summary>
    NotDispatched,
    /// <summary>No ProcessEvent hook is installed — the call took the direct fallback.</summary>
    HookNotInstalled,
    /// <summary>Hook installed but has never fired: mis-detected slot, or an idle game thread.</summary>
    HookNeverFired,
    /// <summary>Hook installed AND firing — the invoke path is live, so the helper no-opped.</summary>
    HookFiring,
}

/// <summary>
/// Which remedy a FAILED Self-Test should advise. ([PEHOOK-2026-08-17])
///
/// <para><b>Why this is not a constant string.</b> The old message asserted one
/// cause — "Hook may be on the wrong vtable slot … re-deploy the DLL" — and was
/// wrong on both halves. Re-deploying cannot fix a mis-detected slot (the binary
/// is fine; the detected offset is wrong), and the test's own evidence cannot
/// even establish that a slot IS wrong: <c>docs/working-lessons.md</c> §4.4
/// records KismetMathLibrary helpers silently no-opping through ProcessEvent with
/// a perfectly correct hook, producing byte-for-byte the same signature — inputs
/// written, return slot untouched.</para>
///
/// <para><b>The discriminator the invoke itself does not carry.</b> The DLL
/// already publishes it on <c>get_diagnostics</c>: whether the hook is installed
/// and whether it has ever fired. A hook that fires is, by construction, sitting
/// on the function the game calls constantly — so the slot is right and the zero
/// is §4.4's no-op. A hook that has never fired is the mis-detection signature
/// (and also what an idle game thread looks like, which the advice must say
/// rather than overclaim).</para>
///
/// <para>Pure on purpose: the decision is testable, the pipe round-trip and the
/// resource lookup that consume it are not.</para>
/// </summary>
public static class SelfTestAdvice
{
    public const string KeyUnknown          = "str.System.SelfTest.Fail.Unknown";
    public const string KeyNotDispatched    = "str.System.SelfTest.Fail.NotDispatched";
    public const string KeyHookNotInstalled = "str.System.SelfTest.Fail.HookOff";
    public const string KeyHookNeverFired   = "str.System.SelfTest.Fail.HookNeverFired";
    public const string KeyHookFiring       = "str.System.SelfTest.Fail.HookFiring";

    /// <summary>
    /// Classify a Self-Test failure from the invoke's own result plus the DLL's
    /// hook telemetry.
    /// </summary>
    /// <param name="invokeResult">The DLL's <c>result</c> for the invoke. <b>Checked
    /// first, and it outranks everything.</b> A non-zero result means the DLL
    /// REFUSED the call — the return slot is untouched because nothing ran, not
    /// because a helper no-opped. Reading the slot without this produced the worst
    /// possible message: a refused invoke on a healthy, firing hook was reported as
    /// "the invoke path itself is working", every clause of which was false.</param>
    /// <param name="haveDiagnostics">False when the <c>get_diagnostics</c> probe
    /// failed or was never issued. It maps to <see cref="SelfTestFailureCause.Unknown"/>
    /// rather than to a default guess: an absent measurement is not evidence for
    /// either cause, and guessing here is exactly the old defect.</param>
    /// <param name="hookActive">Game-thread ProcessEvent hook installed and enabled.</param>
    /// <param name="hookHasFired">The hook has fired at least once this process.</param>
    public static SelfTestFailureCause Classify(int invokeResult, bool haveDiagnostics,
                                                bool hookActive, bool hookHasFired)
    {
        if (invokeResult != 0) return SelfTestFailureCause.NotDispatched;
        if (!haveDiagnostics)  return SelfTestFailureCause.Unknown;
        if (!hookActive)       return SelfTestFailureCause.HookNotInstalled;
        // "Fired" is checked only once the hook is known to be up. A fire count
        // left over from an earlier, since-disabled hook says nothing about the
        // call that just failed, so the inactive case wins.
        return hookHasFired ? SelfTestFailureCause.HookFiring
                            : SelfTestFailureCause.HookNeverFired;
    }

    /// <summary>en.axaml key holding the advice sentence for a cause.</summary>
    public static string KeyFor(SelfTestFailureCause cause) => cause switch
    {
        SelfTestFailureCause.NotDispatched    => KeyNotDispatched,
        SelfTestFailureCause.HookNotInstalled => KeyHookNotInstalled,
        SelfTestFailureCause.HookNeverFired   => KeyHookNeverFired,
        SelfTestFailureCause.HookFiring       => KeyHookFiring,
        _                                     => KeyUnknown,
    };
}
