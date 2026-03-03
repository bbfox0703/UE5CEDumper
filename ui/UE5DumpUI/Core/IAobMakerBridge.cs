namespace UE5DumpUI.Core;

/// <summary>
/// Bridge to AOBMaker CE Plugin for navigating CE Memory Viewer.
/// Communicates via <c>\\.\pipe\AOBMakerCEBridge</c> named pipe.
/// </summary>
public interface IAobMakerBridge
{
    /// <summary>Cached availability — true if the last pipe connect succeeded.</summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Test pipe connectivity and update <see cref="IsAvailable"/>.
    /// </summary>
    Task<bool> CheckAvailabilityAsync(CancellationToken ct = default);

    /// <summary>
    /// Navigate CE Memory Viewer hex dump (bottom pane) to the specified address.
    /// Sends <c>NavigateHexView</c>.
    /// </summary>
    /// <param name="hexAddress">Bare hex address without 0x prefix (e.g. "7FF769E29110")</param>
    Task<bool> NavigateHexViewAsync(string hexAddress, CancellationToken ct = default);
}
