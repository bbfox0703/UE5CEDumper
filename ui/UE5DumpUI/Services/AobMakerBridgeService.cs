using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using UE5DumpUI.Core;
using UE5DumpUI.Models;

namespace UE5DumpUI.Services;

/// <summary>
/// Named Pipe client for AOBMaker CE Plugin bridge.
/// Connects to <c>\\.\pipe\AOBMakerCEBridge</c> to navigate CE Memory Viewer.
/// Wire format: 4-byte LE uint32 length prefix + UTF-8 JSON payload.
/// Per-request reconnect (CE Plugin disconnects after each request).
/// </summary>
public sealed class AobMakerBridgeService : IAobMakerBridge, IDisposable
{
    private const string PipeName = "AOBMakerCEBridge";
    private const int ConnectTimeoutMs = 2000;
    private const int ResponseTimeoutMs = 5000;
    private const int MaxMessageSize = 10 * 1024 * 1024;

    // AOBMaker CE Plugin message types
    private const string TypeNavigateHexView = "NavigateHexView";
    private const string TypeNavigateDisassembler = "NavigateDisassembler";

    private readonly ILoggingService? _log;
    private NamedPipeClientStream? _pipe;
    private bool _disposed;

    public bool IsAvailable { get; private set; }

    public AobMakerBridgeService(ILoggingService? log = null)
    {
        _log = log;
    }

    public async Task<bool> CheckAvailabilityAsync(CancellationToken ct = default)
    {
        try
        {
            if (await ReconnectAsync(ct))
            {
                IsAvailable = true;
                CleanupPipe();
                _log?.Info(Constants.LogCatInit, "AOBMaker CE Plugin bridge: available");
                return true;
            }
        }
        catch (Exception ex)
        {
            _log?.Debug(Constants.LogCatInit, $"AOBMaker CE Plugin bridge check failed: {ex.Message}");
        }

        IsAvailable = false;
        return false;
    }

    public async Task<bool> NavigateHexViewAsync(string hexAddress, CancellationToken ct = default)
    {
        if (!await ReconnectAsync(ct))
        {
            IsAvailable = false;
            return false;
        }

        try
        {
            var request = new AobMakerMessage
            {
                Type = TypeNavigateHexView,
                Address = hexAddress
            };

            await WriteMessageAsync(_pipe!, request, ct);

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(ResponseTimeoutMs);

            var response = await ReadMessageAsync(_pipe!, timeoutCts.Token);
            if (response == null || !response.Success)
            {
                _log?.Warn(Constants.LogCatInit,
                    $"AOBMaker NavigateHexView failed: {response?.Message ?? "no response"}");
                return false;
            }

            IsAvailable = true;
            _log?.Info(Constants.LogCatInit, $"AOBMaker: navigated hex view to {hexAddress}");
            return true;
        }
        catch (OperationCanceledException)
        {
            _log?.Warn(Constants.LogCatInit, $"AOBMaker NavigateHexView timed out for {hexAddress}");
            return false;
        }
        catch (Exception ex)
        {
            _log?.Warn(Constants.LogCatInit, $"AOBMaker NavigateHexView error: {ex.Message}");
            IsAvailable = false;
            CleanupPipe();
            return false;
        }
    }

    public async Task<bool> NavigateDisassemblerAsync(string hexAddress, CancellationToken ct = default)
    {
        if (!await ReconnectAsync(ct))
        {
            IsAvailable = false;
            return false;
        }

        try
        {
            var request = new AobMakerMessage
            {
                Type = TypeNavigateDisassembler,
                Address = hexAddress
            };

            await WriteMessageAsync(_pipe!, request, ct);

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(ResponseTimeoutMs);

            var response = await ReadMessageAsync(_pipe!, timeoutCts.Token);
            if (response == null || !response.Success)
            {
                _log?.Warn(Constants.LogCatInit,
                    $"AOBMaker NavigateDisassembler failed: {response?.Message ?? "no response"}");
                return false;
            }

            IsAvailable = true;
            _log?.Info(Constants.LogCatInit, $"AOBMaker: navigated disassembler to {hexAddress}");
            return true;
        }
        catch (OperationCanceledException)
        {
            _log?.Warn(Constants.LogCatInit, $"AOBMaker NavigateDisassembler timed out for {hexAddress}");
            return false;
        }
        catch (Exception ex)
        {
            _log?.Warn(Constants.LogCatInit, $"AOBMaker NavigateDisassembler error: {ex.Message}");
            IsAvailable = false;
            CleanupPipe();
            return false;
        }
    }

    // --- Per-request reconnect (CE Plugin disconnects after each request) ---

    private async Task<bool> ReconnectAsync(CancellationToken ct)
    {
        try
        {
            CleanupPipe();
            _pipe = new NamedPipeClientStream(".", PipeName,
                PipeDirection.InOut, PipeOptions.Asynchronous);
            await _pipe.ConnectAsync(ConnectTimeoutMs, ct);
            return true;
        }
        catch
        {
            CleanupPipe();
            return false;
        }
    }

    // --- Length-prefixed JSON read/write (matches AOBMaker protocol) ---

    private static async Task<AobMakerMessage?> ReadMessageAsync(Stream stream, CancellationToken ct)
    {
        var lengthBuf = new byte[4];
        var bytesRead = await ReadExactAsync(stream, lengthBuf, 4, ct);
        if (bytesRead < 4) return null;

        var length = BitConverter.ToUInt32(lengthBuf, 0);
        if (length == 0 || length > MaxMessageSize) return null;

        var payloadBuf = new byte[length];
        bytesRead = await ReadExactAsync(stream, payloadBuf, (int)length, ct);
        if (bytesRead < (int)length) return null;

        var json = Encoding.UTF8.GetString(payloadBuf);
        return JsonSerializer.Deserialize(json, AobMakerJsonContext.Default.AobMakerMessage);
    }

    private static async Task WriteMessageAsync(Stream stream, AobMakerMessage message, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(message, AobMakerJsonContext.Default.AobMakerMessage);
        var payload = Encoding.UTF8.GetBytes(json);
        var lengthBuf = BitConverter.GetBytes((uint)payload.Length);

        await stream.WriteAsync(lengthBuf, ct);
        await stream.WriteAsync(payload, ct);
        await stream.FlushAsync(ct);
    }

    private static async Task<int> ReadExactAsync(Stream stream, byte[] buffer, int count, CancellationToken ct)
    {
        var totalRead = 0;
        while (totalRead < count)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(totalRead, count - totalRead), ct);
            if (read == 0) break;
            totalRead += read;
        }
        return totalRead;
    }

    private void CleanupPipe()
    {
        if (_pipe != null)
        {
            try { _pipe.Dispose(); } catch { /* ignore */ }
            _pipe = null;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        CleanupPipe();
    }
}
