namespace UE5DumpUI.Models;

/// <summary>
/// What a proxy deploy is permitted to overwrite. Two policies that used to
/// share a single <c>bool force</c> (audit #5 AC1) and have nothing in common:
///
/// <list type="bullet">
/// <item><b>ForceSameVersion</b> — redeploy over OUR proxy even when the version
/// already matches. Benign, reversible, and by far the commoner reason a user
/// reaches for "Force Overwrite". This is the one that is persisted across
/// sessions in <c>ui-options.json</c>.</item>
/// <item><b>ForeignConsent</b> — replace a file that is provably NOT ours:
/// ReShade, Special K, Ultimate ASI Loader, or a wrapper the game shipped.
/// Irreversible — there is no backup and no Recycle Bin on this path — and the
/// same operation erases the row that named the owner, because a successful
/// deploy clears <c>ErrorMessage</c>. Deliberately NOT persisted; see
/// <c>ProxyDeployViewModel.AllowForeignOverwrite</c>.</item>
/// </list>
///
/// <para>A record struct with NAMED members rather than two adjacent bools: with
/// positional bools, <c>DeployAsync(src, game, type, true, false, ct)</c>
/// compiles, reads plausibly, and destroys files if the pair is transposed.</para>
///
/// <para>Lives in <c>Models</c> rather than beside the service because
/// <c>Core.IProxyDeployService</c> names it, and Core must not depend on
/// Services.</para>
/// </summary>
public readonly record struct DeployOptions(bool ForceSameVersion, bool ForeignConsent)
{
    /// <summary>Neither policy — the safe default for refresh-driven and
    /// automated paths.</summary>
    public static DeployOptions None => new(false, false);
}

/// <summary>What <c>ProxyDeployService.PlanDeploy</c> decided, before any file
/// is touched.</summary>
public enum DeployVerdict
{
    /// <summary>Write the file.</summary>
    Proceed,

    /// <summary>Target is ours and already the same version; nothing to do.</summary>
    AlreadyCurrent,

    /// <summary>Target belongs to another program and no foreign consent was
    /// given. Refuse — never fall through to a copy.</summary>
    NeedsForeignConsent,
}
