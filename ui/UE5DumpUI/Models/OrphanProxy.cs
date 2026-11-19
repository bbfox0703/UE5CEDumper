using CommunityToolkit.Mvvm.ComponentModel;

namespace UE5DumpUI.Models;

/// <summary>
/// One leftover proxy DLL of ours, found in a folder whose game appears to be gone.
///
/// <para><b>This is deliberately NOT a <see cref="DetectedGame"/>, and an instance must NEVER be
/// added to the panel's <c>Games</c> collection.</b> "Update all" iterates <c>Games</c> and
/// redeploys wherever <c>File.Exists(target) &amp;&amp; IsOurProxyDll(target)</c> holds — which an
/// orphan row satisfies exactly. It would write a fresh proxy into the uninstalled game's
/// folder instead of removing it, i.e. the feature would re-create what it exists to delete. Two
/// lesser breakages follow too: the proxy-suggestion pass calls <c>Path.GetFileName(game.ExePath)</c>
/// on an empty path, and the deploy-status refresh would label orphans as deployed and pollute the
/// panel's counts.</para>
/// </summary>
public sealed partial class OrphanProxy : ObservableObject
{
    /// <summary>Full path of the leftover DLL (the first one, when several are present).</summary>
    public required string DllPath { get; init; }

    /// <summary>The folder holding it — the leaf of the chain, e.g. <c>...\Binaries\Win64</c>.</summary>
    public required string DllDirectory { get; init; }

    /// <summary>Comma-separated file names found in that folder, all of them ours.</summary>
    public required string DllNames { get; init; }

    /// <summary>
    /// Full paths of the files this row was AUTHORISED to recycle, exactly as shown to the user.
    ///
    /// <para>Load-bearing, not informational. The removal path re-plans from disk (conditions can
    /// change while the confirmation is on screen) and then INTERSECTS the new plan with this list
    /// and with <see cref="ChainDirs"/>. That intersection is what makes the promise in the report —
    /// "the real result can be smaller than this list, it will never be larger" — actually true.
    /// Without it a folder that merely stopped being shared between the scan and the click would be
    /// pruned even though the user was told no folder would be touched.</para>
    /// </summary>
    public required IReadOnlyList<string> AuthorisedFiles { get; init; }

    /// <summary>Combined size of the leftover files, in bytes.</summary>
    public long SizeBytes { get; init; }

    /// <summary><c>FileVersion</c> of the leftover DLL — what lets the user recognise it as ours.</summary>
    public string FileVersion { get; init; } = "";

    /// <summary>Directories that would be removed, DEEPEST-FIRST, in removal order. Empty for a
    /// <see cref="OrphanVerdict.FileOnly"/> row. Also the CEILING on what removal may touch — see
    /// <see cref="AuthorisedFiles"/>.</summary>
    public required IReadOnlyList<string> ChainDirs { get; init; }

    /// <summary>The topmost directory that would be removed, for the one-line summary.</summary>
    public string TopmostRemovableDir { get; init; } = "";

    /// <summary>Which discovery sources found this row.</summary>
    public OrphanScanSources Source { get; init; }

    /// <summary>The decision. Only <see cref="OrphanVerdict.Deletable"/> and
    /// <see cref="OrphanVerdict.FileOnly"/> can be acted on.</summary>
    public OrphanVerdict Verdict { get; init; }

    /// <summary>Why it is blocked, when it is. Shown verbatim in the list and the confirmation.</summary>
    public IReadOnlyList<string> Blockers { get; init; } = Array.Empty<string>();

    /// <summary>Plain-language justification for calling this an orphan. Required in the
    /// confirmation window: an unexplained delete list cannot be audited by whoever clicks it.</summary>
    public string EvidenceText { get; init; } = "";

    /// <summary>Opt-in per row. Defaults to FALSE, and there is deliberately no select-all.</summary>
    [ObservableProperty] private bool _isSelected;

    /// <summary>Result text after an attempted removal.</summary>
    [ObservableProperty] private string _statusText = "";

    /// <summary>True when <see cref="StatusText"/> reports a FAILURE. Without this, a locked file or a
    /// permission refusal would render in success green, which is the one colour it must not be.</summary>
    [ObservableProperty] private bool _statusIsError;

    /// <summary>Brush key for the status line — red on failure, green on success.</summary>
    public string StatusColor => StatusIsError ? "#E05252" : "#7EC97A";

    partial void OnStatusIsErrorChanged(bool value) => OnPropertyChanged(nameof(StatusColor));

    /// <summary>True once this row has been successfully cleaned.</summary>
    [ObservableProperty] private bool _isRemoved;

    /// <summary>True when the row can be selected for removal at all. Depends on
    /// <see cref="IsRemoved"/>, so that property re-raises it (see OnIsRemovedChanged) — otherwise a
    /// cleaned row's checkbox stays enabled and it can be submitted again.</summary>
    public bool IsActionable => !IsRemoved && OrphanVerdictRules.IsActionable(Verdict);

    partial void OnIsRemovedChanged(bool value)
    {
        OnPropertyChanged(nameof(IsActionable));
        OnPropertyChanged(nameof(ActionSummary));
    }

    /// <summary>
    /// One-line "what will happen" for the list.
    ///
    /// <para>The folder count is phrased as "up to", and deliberately so: the chain is an ATTEMPT
    /// list, walked with the non-recursive <c>Directory.Delete</c>, which stops at the first level
    /// that still holds anything. Promising a number we might not deliver would be the one place this
    /// UI could mislead.</para>
    /// </summary>
    public string ActionSummary => Verdict switch
    {
        OrphanVerdict.Deletable when ChainDirs.Count > 0 =>
            $"Recycle {DllNames}, then remove up to {ChainDirs.Count} folder(s) it leaves empty, " +
            $"stopping below {LastSegmentOf(TopmostRemovableDir)}",
        OrphanVerdict.Deletable => $"Recycle {DllNames}",
        OrphanVerdict.FileOnly => $"Recycle {DllNames} — folders left in place",
        _ => Blockers.Count > 0 ? Blockers[0] : "Blocked",
    };

    /// <summary>Human-readable size for the list.</summary>
    public string SizeText => SizeBytes <= 0 ? "" : $"{SizeBytes:N0} bytes";

    private static string LastSegmentOf(string path)
    {
        string p = path.TrimEnd('\\', '/');
        int i = p.LastIndexOfAny(new[] { '\\', '/' });
        return i < 0 ? p : p[(i + 1)..];
    }
}
