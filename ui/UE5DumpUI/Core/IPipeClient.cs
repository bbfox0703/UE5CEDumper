using System.Text.Json.Nodes;
using UE5DumpUI.Models;

namespace UE5DumpUI.Core;

/// <summary>
/// Named Pipe client interface for communicating with the injected DLL.
/// </summary>
public interface IPipeClient : IDisposable
{
    /// <summary>Whether the pipe is currently connected.</summary>
    bool IsConnected { get; }

    /// <summary>Fired when connection state changes.</summary>
    event Action<bool>? ConnectionStateChanged;

    /// <summary>Fired when a push event is received from the DLL.</summary>
    event Action<JsonObject>? EventReceived;

    /// <summary>
    /// Fired when the DLL reports a change in game-thread liveness. Payload is
    /// true when the game thread appears paused/suspended (not ticking
    /// ProcessEvent), so live-camera / function-invoke features will time out;
    /// false when it resumes. The DLL rides this on every response envelope; the
    /// client raises it only on a transition. Raised on a background pipe thread —
    /// handlers must marshal to the UI thread before touching UI state.
    /// </summary>
    event Action<bool>? GameThreadStalledChanged;

    /// <summary>
    /// Fired for every pipe line — TX request, RX response, push event — so the
    /// UI can show a live activity tail (System tab). Raised on background pipe
    /// threads; handlers must marshal to the UI thread before touching UI state.
    /// </summary>
    event Action<PipeLogEntry>? Activity;

    /// <summary>Connect to the named pipe server <paramref name="pipeName"/>, bare (no \\.\pipe\ path): one game's
    /// <see cref="PipeTargets.NameFor"/>. There is no default: since [PIPE-PER-PROCESS-2026-11-06] each game serves its
    /// own name, and a connect that named none would have to guess the game.</summary>
    Task ConnectAsync(string pipeName, CancellationToken ct = default);

    /// <summary>Disconnect from the pipe server.</summary>
    Task DisconnectAsync();

    /// <summary>Send a JSON request and await the response.</summary>
    Task<JsonObject> SendAsync(JsonObject request, CancellationToken ct = default);
}
