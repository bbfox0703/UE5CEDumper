using CommunityToolkit.Mvvm.ComponentModel;
using UE5DumpUI.Core;

namespace UE5DumpUI.Helpers;

/// <summary>
/// [AOBMAKER-EVAL-2026-09-29] One "can we reach Cheat Engine's AOBMaker plugin?" flag, shared by every panel whose
/// HEX / +CE / ASM buttons read it, plus the [AOBM-ATTACH-CHECK] warning about which process CE has open.
///
/// <para><b>Why one shared object instead of a flag per panel.</b> The older panels each keep their own
/// <c>IsAobMakerAvailable</c>, so the toolbar ⟳ has to repaint them one by one ([R7-S7]) and a panel missing from
/// that list goes stale. Panels holding this instance cannot drift apart: a probe from any of them, the toolbar ⟳,
/// or a push that finds the pipe gone repaints every button at once.</para>
///
/// <para>It is a CACHE, as every availability flag in the app is. Nothing polls: the bridge reconnects on every
/// request anyway, so each push re-learns the truth and <see cref="Apply"/> publishes it.</para>
/// </summary>
public sealed partial class AobMakerStatus : ObservableObject
{
    /// <summary>A tab switch can fire several probes in a row; each one is a pipe connect that waits up to 2 s when
    /// Cheat Engine is absent.</summary>
    private static readonly TimeSpan ProbeCooldown = TimeSpan.FromSeconds(5);
    private DateTime _lastProbe = DateTime.MinValue;

    public AobMakerStatus(IAobMakerBridge? bridge) => Bridge = bridge;

    /// <summary>Null when the app runs without the bridge (tests, or no plugin configured): every button stays off.</summary>
    public IAobMakerBridge? Bridge { get; }

    [ObservableProperty] private bool _isAvailable;

    /// <summary>[AOBM-ATTACH-CHECK] Why a push would land in the wrong place, or empty when CE has the game open or we
    /// cannot tell. See <see cref="DescribeAttach"/>.</summary>
    [ObservableProperty] private string _attachWarning = "";

    public bool HasAttachWarning => !string.IsNullOrEmpty(AttachWarning);

    partial void OnAttachWarningChanged(string value) => OnPropertyChanged(nameof(HasAttachWarning));

    partial void OnIsAvailableChanged(bool value)
    {
        // [AOBM-ATTACH-CHECK] With the plugin out of reach nothing can tell which process CE has open, and a check that
        // cannot tell must not accuse (DescribeAttach). The warning used to outlive Cheat Engine itself: measured
        // 2026-09-29, CE closed with it showing and the chip went Offline beside "CE is not on this game".
        if (!value) AttachWarning = "";
        OnPropertyChanged(nameof(DllTip));
    }

    internal const string KeyDllOn = "str.Tip.Toolbar.AobMakerDllOn";

    /// <summary>[AOBM-UI-INDICATOR] The DLL dot's tooltip: what the plugin is for when it is reachable, and WHY it is not
    /// otherwise (absent, busy, refused), which is the remedy the dot alone cannot say.</summary>
    public string DllTip => IsAvailable
        ? Say(KeyDllOn, "AOBMaker DLL (its Cheat Engine plugin): connected. HEX, ASM, +CE and SYM push into Cheat Engine")
        : AobMakerUnavailable.Text(Bridge);

    /// <summary>A second failure for a NEW reason leaves <see cref="IsAvailable"/> false and so raises nothing; the
    /// tooltip that names the reason must repaint anyway ([R7-S7]).</summary>
    public void NotifyReasonChanged() => OnPropertyChanged(nameof(DllTip));

    /// <summary>Publish what a probe or a push just learned. Writing the same value raises nothing.</summary>
    public void Apply(bool available) => IsAvailable = available;

    /// <summary>Probe the pipe now and publish the result.</summary>
    public async Task<bool> ProbeAsync()
    {
        if (Bridge == null)
        {
            IsAvailable = false;
            return false;
        }
        _lastProbe = DateTime.UtcNow;
        bool ok;
        try { ok = await Bridge.CheckAvailabilityAsync(); }
        catch (Exception) { ok = false; }
        IsAvailable = ok;
        NotifyReasonChanged();
        return ok;
    }

    /// <summary>Probe unless one ran within <see cref="ProbeCooldown"/>. Fire-and-forget, for tab activation.</summary>
    public void TryProbe()
    {
        if (Bridge == null || DateTime.UtcNow - _lastProbe < ProbeCooldown) return;
        _ = ProbeAsync();
    }

    /// <summary>
    /// [AOBM-ATTACH-CHECK] Ask the plugin which process CE has open, compare it with the game this UI is connected to,
    /// and publish the verdict in <see cref="AttachWarning"/>. Returns the same text.
    /// <para>Warn, never refuse: a push into a CE that has nothing open is sometimes exactly right (the Inject DLL
    /// bootstrap is pushed before CE opens the game), so the caller keeps its push and adds this sentence.</para>
    /// </summary>
    public async Task<string> CheckAttachAsync(int gamePid, string gameModule)
    {
        if (Bridge == null || gamePid <= 0)
        {
            AttachWarning = "";
            return "";
        }
        CeAttachedProcess? ce;
        try { ce = await Bridge.GetAttachedProcessAsync(); }
        catch (Exception) { ce = null; }
        AttachWarning = DescribeAttach(ce, gamePid, gameModule);
        return AttachWarning;
    }

    internal const string KeyAttachNone = "str.AobMaker.Attach.None";
    internal const string KeyAttachOther = "str.AobMaker.Attach.Other";

    /// <summary>
    /// [AOBM-ATTACH-CHECK] The warning for what CE reports, or empty. Empty whenever either side is unknown: a check
    /// that cannot tell must not accuse. The comparison is by process id; the name is display only, because the
    /// plugin reads it through the ANSI API and a non-ASCII exe name arrives with <c>?</c> in it.
    /// </summary>
    internal static string DescribeAttach(CeAttachedProcess? ce, int gamePid, string gameModule)
    {
        if (ce is not { } p || gamePid <= 0 || p.ProcessId == gamePid) return "";
        if (p.ProcessId == 0)
            return Say(KeyAttachNone, "Cheat Engine has no process open: open {0} in CE before pushing to it",
                       gameModule);
        var name = string.IsNullOrEmpty(p.ProcessName) ? "?" : p.ProcessName;
        return Say(KeyAttachOther,
                   "Cheat Engine has {0} (pid {1}) open, not {2} (pid {3}): records pushed now point into the wrong process",
                   name, p.ProcessId, gameModule, gamePid);
    }

    /// <summary><c>Res.Format</c> answers "" for a key it cannot resolve, and always does headless; a status line
    /// must never go blank over that, so each sentence keeps a literal core. en.axaml supplies the wording
    /// (the <see cref="AobMakerUnavailable"/> pattern).</summary>
    internal static string Say(string key, string fallback, params object[] args)
    {
        var resolved = Res.Format(key, args);
        return string.IsNullOrEmpty(resolved) ? string.Format(fallback, args) : resolved;
    }
}
