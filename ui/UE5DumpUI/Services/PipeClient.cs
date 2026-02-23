using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using UE5DumpUI.Core;

namespace UE5DumpUI.Services;

/// <summary>
/// Named Pipe client for communicating with the injected DLL.
/// </summary>
public sealed class PipeClient : IPipeClient
{
    private NamedPipeClientStream? _pipe;
    private StreamReader? _reader;
    private StreamWriter? _writer;
    private CancellationTokenSource _cts = new();
    private int _nextId = 1;
    private readonly ConcurrentDictionary<int, TaskCompletionSource<JsonObject>> _pending = new();
    private readonly ILoggingService _log;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private Task? _readLoopTask;

    public bool IsConnected { get; private set; }
    public event Action<bool>? ConnectionStateChanged;
    public event Action<JsonObject>? EventReceived;

    public PipeClient(ILoggingService log)
    {
        _log = log;
    }

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        if (IsConnected) return;

        // Dispose previous CTS before creating a new one (prevent WaitHandle leak)
        _cts.Dispose();
        _cts = new CancellationTokenSource();

        _pipe = new NamedPipeClientStream(".", Constants.PipeName,
            PipeDirection.InOut, PipeOptions.Asynchronous);

        _log.Info(Constants.LogCatPipe, $"Connecting to pipe: {Constants.PipeName}...");
        await _pipe.ConnectAsync(Constants.PipeConnectTimeoutMs, ct);

        _reader = new StreamReader(_pipe, Encoding.UTF8);
        _writer = new StreamWriter(_pipe, Encoding.UTF8) { AutoFlush = true };

        IsConnected = true;
        ConnectionStateChanged?.Invoke(true);
        _log.Info(Constants.LogCatPipe, "Pipe connected");

        _readLoopTask = Task.Run(ReadLoopAsync, _cts.Token);
    }

    public async Task DisconnectAsync()
    {
        if (!IsConnected) return;

        _log.Info(Constants.LogCatPipe, "Disconnecting pipe...");
        _cts.Cancel();

        // Complete all pending requests with cancellation
        foreach (var kvp in _pending)
        {
            kvp.Value.TrySetCanceled();
        }
        _pending.Clear();

        if (_readLoopTask != null)
        {
            try { await _readLoopTask; } catch { /* expected */ }
        }

        _reader?.Dispose();
        _writer?.Dispose();
        _pipe?.Dispose();
        _reader = null;
        _writer = null;
        _pipe = null;

        IsConnected = false;
        ConnectionStateChanged?.Invoke(false);
        _log.Info(Constants.LogCatPipe, "Pipe disconnected");
    }

    public async Task<JsonObject> SendAsync(JsonObject request, CancellationToken ct = default)
    {
        if (!IsConnected || _writer == null)
            throw new InvalidOperationException("Not connected to pipe server");

        int id = Interlocked.Increment(ref _nextId);
        request["id"] = id;

        var tcs = new TaskCompletionSource<JsonObject>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = tcs;

        // Register cancellation
        await using var reg = ct.Register(() => {
            if (_pending.TryRemove(id, out var removed))
                removed.TrySetCanceled();
        });

        var json = request.ToJsonString();
        _log.Debug(Constants.LogCatPipe, $"Pipe TX: {json}");

        try
        {
            // Serialize writes to prevent interleaved JSON on the pipe
            await _writeLock.WaitAsync(ct);
            try
            {
                await _writer.WriteLineAsync(json);
            }
            finally
            {
                _writeLock.Release();
            }
        }
        catch (IOException) when (!IsConnected || _cts.IsCancellationRequested)
        {
            // Pipe closed during write (disconnect in progress) — cancel this request
            _pending.TryRemove(id, out _);
            throw new OperationCanceledException("Pipe disconnected during send");
        }
        catch (ObjectDisposedException) when (!IsConnected || _cts.IsCancellationRequested)
        {
            _pending.TryRemove(id, out _);
            throw new OperationCanceledException("Pipe disconnected during send");
        }

        return await tcs.Task;
    }

    private async Task ReadLoopAsync()
    {
        try
        {
            while (!_cts.IsCancellationRequested && _reader != null)
            {
                var line = await _reader.ReadLineAsync(_cts.Token);
                if (line is null)
                {
                    _log.Warn(Constants.LogCatPipe, "Pipe: ReadLine returned null (disconnected)");
                    break;
                }

                _log.Debug(Constants.LogCatPipe, $"Pipe RX: {line}");

                try
                {
                    var obj = JsonNode.Parse(line) as JsonObject;
                    if (obj == null) continue;

                    if (obj["event"] != null)
                    {
                        // Push event from DLL
                        EventReceived?.Invoke(obj);
                    }
                    else
                    {
                        // Response to a request
                        var id = obj["id"]?.GetValue<int>() ?? 0;
                        if (_pending.TryRemove(id, out var tcs))
                        {
                            tcs.TrySetResult(obj);
                        }
                    }
                }
                catch (JsonException ex)
                {
                    _log.Error(Constants.LogCatPipe, $"Pipe: JSON parse error: {ex.Message}");
                }
            }
        }
        catch (OperationCanceledException) { /* normal shutdown */ }
        catch (IOException) { /* expected when pipe closes during cancellation */ }
        catch (ObjectDisposedException) { /* expected when pipe disposed during read */ }
        catch (Exception ex)
        {
            _log.Error(Constants.LogCatPipe, "Pipe: ReadLoop error", ex);
        }
        finally
        {
            // If we exit the read loop unexpectedly, mark disconnected
            if (IsConnected)
            {
                IsConnected = false;
                ConnectionStateChanged?.Invoke(false);
            }
        }
    }

    public void Dispose()
    {
        _cts.Cancel();

        // Cancel all pending requests so callers don't hang
        foreach (var kvp in _pending)
        {
            kvp.Value.TrySetCanceled();
        }
        _pending.Clear();

        _reader?.Dispose();
        _writer?.Dispose();
        _pipe?.Dispose();
        _cts.Dispose();
        _writeLock.Dispose();
    }
}
