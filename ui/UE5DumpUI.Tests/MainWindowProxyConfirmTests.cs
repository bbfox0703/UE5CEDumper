using UE5DumpUI.ViewModels;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// The LKG proxy-confirm dwell must record a proxy as working ONLY when the SAME
/// connection that scheduled it is still up after the dwell. A check of IsConnected
/// alone would record as confirmed-working a proxy that loaded, connected, then
/// crashed the game at t=5s, if the user reconnected (to the same or a DIFFERENT
/// game) before the 20 s dwell elapsed.
///
/// The gate is factored into the pure <see cref="MainWindowViewModel.ShouldConfirmProxy"/>
/// so the epoch logic is unit-testable without the timer / dispatcher / proxy VM.
/// </summary>
public class MainWindowProxyConfirmTests
{
    [Fact]
    public void SameSessionStillConnected_Records()
    {
        // Scheduled at epoch 7, still connected, no disconnect since → record.
        Assert.True(MainWindowViewModel.ShouldConfirmProxy(isConnected: true, scheduledEpoch: 7, currentEpoch: 7));
    }

    [Fact]
    public void ReconnectedAfterCrash_DoesNotRecord()
    {
        // Session A scheduled at epoch 7; A crashed (disconnect bumped the epoch to 8)
        // and the user reconnected (connected again) before the dwell fired. A's dwell
        // must NOT record its crashed proxy against the new session.
        Assert.False(MainWindowViewModel.ShouldConfirmProxy(isConnected: true, scheduledEpoch: 7, currentEpoch: 8));
    }

    [Fact]
    public void DisconnectedAtDwell_DoesNotRecord()
    {
        // The dwell fired while disconnected (game gone) → never record.
        Assert.False(MainWindowViewModel.ShouldConfirmProxy(isConnected: false, scheduledEpoch: 7, currentEpoch: 7));
    }

    [Fact]
    public void DisconnectedAndEpochAdvanced_DoesNotRecord()
    {
        Assert.False(MainWindowViewModel.ShouldConfirmProxy(isConnected: false, scheduledEpoch: 7, currentEpoch: 9));
    }
}
