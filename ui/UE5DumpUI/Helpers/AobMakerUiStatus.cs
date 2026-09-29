using CommunityToolkit.Mvvm.ComponentModel;
using UE5DumpUI.Core;

namespace UE5DumpUI.Helpers;

/// <summary>
/// [AOBM-UI-INDICATOR] Is AOBMaker.UI -- the AOBMaker app, not its Cheat Engine plugin -- running? Drives the toolbar's
/// "UI" dot, and wraps the real <see cref="IAobMakerUiClient"/> so every GenerateAob answer refreshes it too.
///
/// <para><b>Why this one polls when <see cref="AobMakerStatus"/> does not.</b> The plugin's check is a pipe CONNECT; this
/// one only LISTS <c>\\.\pipe\</c>, which AOBMaker.UI never sees. A connect would cost it a log line each time and hold
/// its single pipe instance, turning away Cheat Engine's own "Send to AOBMaker" for as long as it lasted.</para>
///
/// <para><b>Why two misses before grey.</b> AOBMaker.UI disposes its one instance after every client and only then
/// creates the next, so a listing can land in the gap. A listed name is green at once; an absent one turns grey on
/// the second poll in a row.</para>
/// </summary>
public sealed partial class AobMakerUiStatus : ObservableObject, IAobMakerUiClient
{
    public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(3);
    internal const int MissesToGoOffline = 2;

    internal const string KeyTipOn = "str.Tip.Toolbar.AobMakerUiOn";
    internal const string KeyTipOff = "str.Tip.Toolbar.AobMakerUiOff";

    private int _misses;
    private int _polling;

    public AobMakerUiStatus(IAobMakerUiClient? client) => Client = client;

    /// <summary>Null when the app runs without an AOBMaker.UI client (tests): the dot stays grey and hidden.</summary>
    public IAobMakerUiClient? Client { get; }

    [ObservableProperty] private bool _isRunning;

    partial void OnIsRunningChanged(bool value) => OnPropertyChanged(nameof(Tip));

    public string Tip => IsRunning
        ? AobMakerStatus.Say(KeyTipOn,
            "AOBMaker UI (the AOBMaker app): running. SYM for GObjects / GNames asks it for a unique AOB")
        : AobMakerStatus.Say(KeyTipOff,
            "AOBMaker UI (the AOBMaker app): not running. Only SYM for GObjects / GNames needs it; run it at the same elevation as this app");

    /// <summary>List the pipe once and publish what it shows. A poll still running is not stacked on. Never throws.</summary>
    public async Task<bool> PollAsync(CancellationToken ct = default)
    {
        if (Client == null)
        {
            IsRunning = false;
            return false;
        }
        if (Interlocked.Exchange(ref _polling, 1) == 1) return IsRunning;
        try
        {
            bool listed;
            try { listed = await Client.IsPipeListedAsync(ct); }
            catch (Exception) { listed = false; }
            Observe(listed);
            return IsRunning;
        }
        finally
        {
            Volatile.Write(ref _polling, 0);
        }
    }

    /// <summary>One sighting: listed turns the dot green at once; unlisted turns it grey only on the second in a row.</summary>
    internal void Observe(bool listed)
    {
        if (listed)
        {
            _misses = 0;
            IsRunning = true;
        }
        else if (++_misses >= MissesToGoOffline)
        {
            IsRunning = false;
        }
    }

    /// <inheritdoc/>
    public async Task<GenerateAobResult> GenerateAobAsync(string hexAddress, int processId, CancellationToken ct = default)
    {
        if (Client == null) return new GenerateAobResult(null, GenerateAobFailure.NotRunning, null);
        var result = await Client.GenerateAobAsync(hexAddress, processId, ct);
        if (result.Failure == GenerateAobFailure.NotRunning)
        {
            // The connect found nothing: count a miss and look again now, instead of waiting out the timer.
            Observe(false);
            await PollAsync(ct);
        }
        else
        {
            // Any other answer -- a refusal and silence included -- means a server was there.
            Observe(true);
        }
        return result;
    }

    /// <inheritdoc/>
    public Task<bool> IsPipeListedAsync(CancellationToken ct = default)
        => Client?.IsPipeListedAsync(ct) ?? Task.FromResult(false);
}
