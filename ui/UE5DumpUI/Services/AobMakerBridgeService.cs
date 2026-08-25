using System.IO.Pipes;
using System.Text;
using System.Threading;
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
    private const string TypeCreateAAScript = "CreateAAScript";
    private const string TypeCreateSymbolScript = "CreateSymbolScript";
    private const string TypeCreateMemoryRecord = "CreateMemoryRecord";
    private const string TypeInjectTableFile = "InjectTableFile";

    // Inject ships an entire helper Lua file payload, runs CE Lua via
    // synchronize() (which yields to CE's main thread), and verifies the
    // stream size on the way out — the navigation-class 5 s deadline is
    // tight against that. Give it a roomier window so a momentary CE
    // pause doesn't show up as a spurious failure to the user.
    private const int InjectResponseTimeoutMs = 15000;

    private readonly ILoggingService? _log;
    private NamedPipeClientStream? _pipe;

    // All public methods share the single _pipe field with per-request
    // reconnect + CleanupPipe and can be invoked concurrently (e.g. a tab
    // switch fires CheckAvailabilityAsync fire-and-forget while the user
    // clicks a button that calls CreateAAScriptAsync / InjectTableFileAsync).
    // Without this gate, one method's CleanupPipe() would dispose the _pipe
    // another is mid-read/write on → ObjectDisposedException → spurious
    // "not connected". The public methods never call each other (only the
    // private Reconnect/Write/Read/Cleanup helpers), so a non-reentrant
    // semaphore serializes them deadlock-free.
    private readonly SemaphoreSlim _opLock = new(1, 1);
    private bool _disposed;

    public bool IsAvailable { get; private set; }

    public AobMakerBridgeService(ILoggingService? log = null)
    {
        _log = log;
    }

    public async Task<bool> CheckAvailabilityAsync(CancellationToken ct = default)
    {
        await _opLock.WaitAsync(ct);
        try
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
        finally
        {
            _opLock.Release();
        }
    }

    public async Task<bool> NavigateHexViewAsync(string hexAddress, CancellationToken ct = default)
    {
        await _opLock.WaitAsync(ct);
        try
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
                CleanupPipe();  // CE Plugin disconnects after each request — release handle immediately
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
                CleanupPipe();
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
        finally
        {
            _opLock.Release();
        }
    }

    public async Task<bool> NavigateDisassemblerAsync(string hexAddress, CancellationToken ct = default)
    {
        await _opLock.WaitAsync(ct);
        try
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
                CleanupPipe();  // CE Plugin disconnects after each request — release handle immediately
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
                CleanupPipe();
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
        finally
        {
            _opLock.Release();
        }
    }

    public async Task<bool> CreateAAScriptAsync(string description, string script,
        bool autoActivate = true, string? group = null, CancellationToken ct = default)
    {
        await _opLock.WaitAsync(ct);
        try
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
                    Type = TypeCreateAAScript,
                    Description = description,
                    Script = script,
                    AutoActivate = autoActivate,
                    Group = string.IsNullOrEmpty(group) ? null : group,
                };

                await WriteMessageAsync(_pipe!, request, ct);

                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeoutCts.CancelAfter(ResponseTimeoutMs);

                var response = await ReadMessageAsync(_pipe!, timeoutCts.Token);
                CleanupPipe();
                if (response == null || !response.Success)
                {
                    _log?.Warn(Constants.LogCatInit,
                        $"AOBMaker CreateAAScript failed: {response?.Message ?? "no response"}");
                    return false;
                }

                IsAvailable = true;
                _log?.Info(Constants.LogCatInit, $"AOBMaker: created AA script '{description}'");
                return true;
            }
            catch (OperationCanceledException)
            {
                _log?.Warn(Constants.LogCatInit, $"AOBMaker CreateAAScript timed out for '{description}'");
                CleanupPipe();
                return false;
            }
            catch (Exception ex)
            {
                _log?.Warn(Constants.LogCatInit, $"AOBMaker CreateAAScript error: {ex.Message}");
                IsAvailable = false;
                CleanupPipe();
                return false;
            }
        }
        finally
        {
            _opLock.Release();
        }
    }

    public async Task<bool> CreateSymbolScriptAsync(string name, string aob, int pos, int aoblen,
        string symbol, string module, bool autoActivate = true, CancellationToken ct = default)
    {
        await _opLock.WaitAsync(ct);
        try
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
                    Type = TypeCreateSymbolScript,
                    Name = name,
                    Aob = aob,
                    Pos = pos,
                    AobLen = aoblen,
                    Symbol = symbol,
                    Module = module,
                    AutoActivate = autoActivate
                };

                await WriteMessageAsync(_pipe!, request, ct);

                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeoutCts.CancelAfter(ResponseTimeoutMs);

                var response = await ReadMessageAsync(_pipe!, timeoutCts.Token);
                CleanupPipe();
                if (response == null || !response.Success)
                {
                    _log?.Warn(Constants.LogCatInit,
                        $"AOBMaker CreateSymbolScript failed: {response?.Message ?? "no response"}");
                    return false;
                }

                IsAvailable = true;
                _log?.Info(Constants.LogCatInit,
                    $"AOBMaker: created symbol script '{name}' → {symbol} (AOB: {aob})");
                return true;
            }
            catch (OperationCanceledException)
            {
                _log?.Warn(Constants.LogCatInit, $"AOBMaker CreateSymbolScript timed out for '{name}'");
                CleanupPipe();
                return false;
            }
            catch (Exception ex)
            {
                _log?.Warn(Constants.LogCatInit, $"AOBMaker CreateSymbolScript error: {ex.Message}");
                IsAvailable = false;
                CleanupPipe();
                return false;
            }
        }
        finally
        {
            _opLock.Release();
        }
    }

    public async Task<bool> CreateMemoryRecordAsync(string description, string address,
        int valueType, bool isSigned = false, bool showAsHex = false, CancellationToken ct = default)
    {
        await _opLock.WaitAsync(ct);
        try
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
                    Type = TypeCreateMemoryRecord,
                    Description = description,
                    Address = address,
                    ValueType = valueType,
                    IsSigned = isSigned,
                    ShowAsHex = showAsHex
                };

                await WriteMessageAsync(_pipe!, request, ct);

                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeoutCts.CancelAfter(ResponseTimeoutMs);

                var response = await ReadMessageAsync(_pipe!, timeoutCts.Token);
                CleanupPipe();
                if (response == null || !response.Success)
                {
                    _log?.Warn(Constants.LogCatInit,
                        $"AOBMaker CreateMemoryRecord failed: {response?.Message ?? "no response"}");
                    return false;
                }

                IsAvailable = true;
                _log?.Info(Constants.LogCatInit,
                    $"AOBMaker: created memory record '{description}' @ {address} (type {valueType})");
                return true;
            }
            catch (OperationCanceledException)
            {
                _log?.Warn(Constants.LogCatInit, $"AOBMaker CreateMemoryRecord timed out for '{description}'");
                CleanupPipe();
                return false;
            }
            catch (Exception ex)
            {
                _log?.Warn(Constants.LogCatInit, $"AOBMaker CreateMemoryRecord error: {ex.Message}");
                IsAvailable = false;
                CleanupPipe();
                return false;
            }
        }
        finally
        {
            _opLock.Release();
        }
    }

    public async Task<(bool Ok, string? ErrorMessage)> InjectTableFileAsync(string fileName, string content,
        CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(fileName))
            throw new ArgumentException("fileName must not be empty", nameof(fileName));
        if (string.IsNullOrEmpty(content))
            throw new ArgumentException("content must not be empty", nameof(content));

        await _opLock.WaitAsync(ct);
        try
        {
            if (!await ReconnectAsync(ct))
            {
                IsAvailable = false;
                return (false, null);
            }

            try
            {
                var request = new AobMakerMessage
                {
                    Type = TypeInjectTableFile,
                    FileName = fileName,
                    Content = content
                };

                await WriteMessageAsync(_pipe!, request, ct);

                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeoutCts.CancelAfter(InjectResponseTimeoutMs);

                var response = await ReadMessageAsync(_pipe!, timeoutCts.Token);
                CleanupPipe();
                if (response == null || !response.Success)
                {
                    var msg = response?.Message ?? "no response";
                    _log?.Warn(Constants.LogCatInit,
                        $"AOBMaker InjectTableFile '{fileName}' failed: {msg}");
                    return (false, response?.Message);
                }

                IsAvailable = true;
                _log?.Info(Constants.LogCatInit,
                    $"AOBMaker: injected table file '{fileName}' ({content.Length:N0} chars)");
                return (true, null);
            }
            catch (OperationCanceledException)
            {
                _log?.Warn(Constants.LogCatInit, $"AOBMaker InjectTableFile timed out for '{fileName}'");
                CleanupPipe();
                return (false, "timed out waiting for CE Plugin response");
            }
            catch (Exception ex)
            {
                _log?.Warn(Constants.LogCatInit, $"AOBMaker InjectTableFile error: {ex.Message}");
                IsAvailable = false;
                CleanupPipe();
                return (false, ex.Message);
            }
        }
        finally
        {
            _opLock.Release();
        }
    }

    // --- Per-request reconnect (CE Plugin disconnects after each request) ---

    /// <summary>
    /// Open a fresh connection to the CE-plugin pipe. Never throws — every public
    /// method turns a false into the same user-visible "AOBMaker not connected".
    ///
    /// That is exactly why the failure must be LOGGED here (audit #5 AC3): six of the
    /// seven call sites do nothing but <c>IsAvailable = false; return false;</c>, so a
    /// bare catch made "CE isn't running", "the plugin isn't loaded", "another client
    /// owns the single pipe instance", "the ACL refused us" and "the caller cancelled"
    /// one indistinguishable outcome with zero diagnostics anywhere.
    ///
    /// Split by level on purpose: not-running is the overwhelmingly common case and a
    /// tab switch probes on every activation, so it stays at Debug; anything else is a
    /// real fault and gets a Warn that names the exception type.
    /// </summary>
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
        catch (OperationCanceledException)
        {
            // The caller withdrew (tab switch / window close). Not a fault, and NOT
            // rethrown: every call site is written against a bool.
            _log?.Debug(Constants.LogCatInit,
                $"AOBMaker bridge: connect to '{PipeName}' cancelled by caller");
            CleanupPipe();
            return false;
        }
        catch (TimeoutException)
        {
            // ConnectAsync's own deadline: nothing is listening. Cheat Engine is not
            // running, or it is but the AOBMaker plugin was never loaded.
            _log?.Debug(Constants.LogCatInit,
                $"AOBMaker bridge: no server on \\\\.\\pipe\\{PipeName} within {ConnectTimeoutMs} ms " +
                "(Cheat Engine not running, or the AOBMaker plugin is not loaded)");
            CleanupPipe();
            return false;
        }
        catch (Exception ex)
        {
            // Everything else means a server EXISTS and we still could not reach it —
            // pipe busy (all instances taken), access denied, broken pipe. Those need
            // a different remedy from "start CE", so they must read differently.
            _log?.Warn(Constants.LogCatInit,
                $"AOBMaker bridge: connect to '{PipeName}' failed ({ex.GetType().Name}): {ex.Message}");
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
        // Use Relaxed encoder to avoid \uXXXX escaping of single quotes and non-ASCII
        // — CE Plugin's Lua JSON parser doesn't handle \uXXXX sequences
        var json = JsonSerializer.Serialize(message, AobMakerJsonContext.Relaxed.AobMakerMessage);
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
        _opLock.Dispose();
    }
}
