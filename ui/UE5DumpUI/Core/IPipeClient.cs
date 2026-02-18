using System.Text.Json.Nodes;

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

    /// <summary>Connect to the named pipe server.</summary>
    Task ConnectAsync(CancellationToken ct = default);

    /// <summary>Disconnect from the pipe server.</summary>
    Task DisconnectAsync();

    /// <summary>Send a JSON request and await the response.</summary>
    Task<JsonObject> SendAsync(JsonObject request, CancellationToken ct = default);
}
