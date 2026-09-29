using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using UE5DumpUI.Core;
using UE5DumpUI.Models;

namespace UE5DumpUI.Services;

/// <summary>
/// [AOBM-GNAMES-SYMBOL] Client for AOBMaker.UI's own pipe. Same framing as the Cheat Engine plugin's bridge (4-byte
/// little-endian length, then UTF-8 JSON), one request per connection, but a different server with different rules:
/// <list type="bullet">
/// <item>it exists only while the AOBMaker.UI app is open;</item>
/// <item>it answers <c>GenerateAob</c> only for the same user at the same or a higher integrity level, with a message
/// starting <c>Rejected:</c> otherwise;</item>
/// <item>it leaves <c>success</c> out when it is false, and sends NO reply to a request type it does not know.</item>
/// </list>
/// The last point is why every read here is bounded by a deadline.
/// </summary>
public sealed class AobMakerUiClient : IAobMakerUiClient
{
    /// <summary>AOBMaker's default. It can be renamed in AOBMaker's settings; a renamed pipe reads as "not running".</summary>
    private const string PipeName = "AOBMaker";
    private const string TypeGenerateAob = "GenerateAob";
    private const string RejectedPrefix = "Rejected:";
    private const int ConnectTimeoutMs = 2000;
    private const int MaxMessageSize = 10 * 1024 * 1024;

    /// <summary>The server's 5 s idle clock covers only the wire; the scan that proves the AOB unique is handler work
    /// and can take longer on a large module.</summary>
    private const int GenerateTimeoutMs = 30000;

    private readonly ILoggingService? _log;
    private readonly string _pipeName;
    private readonly int _connectTimeoutMs;
    private readonly int _generateTimeoutMs;
    private readonly Func<string, bool> _pipeExists;

    public AobMakerUiClient(ILoggingService? log = null) : this(log, PipeName, ConnectTimeoutMs, GenerateTimeoutMs) { }

    /// <summary>Test seam: the pipe to reach, both deadlines (so a test never waits out the real ones), and how to tell
    /// a listed pipe from an absent one.</summary>
    internal AobMakerUiClient(ILoggingService? log, string pipeName, int connectTimeoutMs, int generateTimeoutMs,
                              Func<string, bool>? pipeExists = null)
    {
        _log = log;
        _pipeName = pipeName;
        _connectTimeoutMs = connectTimeoutMs;
        _generateTimeoutMs = generateTimeoutMs;
        _pipeExists = pipeExists ?? AobMakerBridgeService.PipeExists;
    }

    /// <inheritdoc/>
    public Task<bool> IsPipeListedAsync(CancellationToken ct = default)
        => Task.Run(() => _pipeExists(_pipeName), ct);

    /// <inheritdoc/>
    public async Task<GenerateAobResult> GenerateAobAsync(string hexAddress, int processId, CancellationToken ct = default)
    {
        var address = hexAddress.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? hexAddress : "0x" + hexAddress;
        using var pipe = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        try
        {
            await pipe.ConnectAsync(_connectTimeoutMs, ct);
        }
        catch (TimeoutException)
        {
            // [AOBM-UI-BUSY] A connect waits for a FREE instance and AOBMaker.UI has only one, so a timeout alone cannot
            // tell "not running" from "serving someone else". The listing can: a busy pipe is still listed.
            if (_pipeExists(_pipeName))
            {
                _log?.Info(Constants.LogCatInit, $"AOBMaker.UI: '{_pipeName}' is running but busy with another client");
                return new GenerateAobResult(null, GenerateAobFailure.Busy, null);
            }
            _log?.Debug(Constants.LogCatInit, $"AOBMaker.UI: no server on '{_pipeName}' (the app is not running)");
            return new GenerateAobResult(null, GenerateAobFailure.NotRunning, null);
        }
        catch (Exception ex)
        {
            _log?.Warn(Constants.LogCatInit, $"AOBMaker.UI: connect to '{_pipeName}' failed ({ex.GetType().Name}): {ex.Message}");
            return new GenerateAobResult(null,
                ex is UnauthorizedAccessException ? GenerateAobFailure.Refused : GenerateAobFailure.NotRunning, ex.Message);
        }

        AobMakerUiMessage? reply;
        try
        {
            await WriteAsync(pipe, new AobMakerUiMessage { Type = TypeGenerateAob, Address = address, ProcessId = processId }, ct);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(_generateTimeoutMs);
            reply = await ReadAsync(pipe, timeout.Token);
        }
        catch (OperationCanceledException)
        {
            _log?.Warn(Constants.LogCatInit, $"AOBMaker.UI GenerateAob for {address}: no reply within {_generateTimeoutMs} ms");
            return new GenerateAobResult(null, GenerateAobFailure.NoReply, null);
        }
        catch (Exception ex)
        {
            _log?.Warn(Constants.LogCatInit, $"AOBMaker.UI GenerateAob for {address} failed: {ex.Message}");
            return new GenerateAobResult(null, GenerateAobFailure.NoReply, ex.Message);
        }

        return Interpret(reply, address, _log);
    }

    /// <summary>Turn a reply into a result. Pure, so the protocol's quirks are testable without a pipe.</summary>
    internal static GenerateAobResult Interpret(AobMakerUiMessage? reply, string address, ILoggingService? log)
    {
        if (reply == null)
            return new GenerateAobResult(null, GenerateAobFailure.NoReply, null);
        if (reply.Success != true || string.IsNullOrEmpty(reply.Aob))
        {
            var refused = reply.Message?.StartsWith(RejectedPrefix, StringComparison.Ordinal) == true;
            log?.Warn(Constants.LogCatInit, $"AOBMaker.UI GenerateAob for {address}: {reply.Message ?? "no AOB in the reply"}");
            return new GenerateAobResult(null,
                refused ? GenerateAobFailure.Refused : GenerateAobFailure.Failed, reply.Message);
        }
        log?.Info(Constants.LogCatInit,
            $"AOBMaker.UI GenerateAob for {address}: {reply.Aob} (offset {reply.InjectionOffset}, pos {reply.Pos}, len {reply.AobLen}, {reply.MatchCount} match)");
        return new GenerateAobResult(
            new GeneratedAob(reply.Aob, reply.InjectionOffset ?? 0, reply.Pos, reply.AobLen, reply.MatchCount ?? 0,
                             reply.Module ?? ""),
            GenerateAobFailure.None, reply.Message);
    }

    private static async Task WriteAsync(Stream stream, AobMakerUiMessage message, CancellationToken ct)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(message, AobMakerUiJsonContext.Default.AobMakerUiMessage);
        await stream.WriteAsync(BitConverter.GetBytes((uint)payload.Length), ct);
        await stream.WriteAsync(payload, ct);
        await stream.FlushAsync(ct);
    }

    private static async Task<AobMakerUiMessage?> ReadAsync(Stream stream, CancellationToken ct)
    {
        var lengthBuf = new byte[4];
        if (await ReadExactAsync(stream, lengthBuf, ct) < 4) return null;
        var length = BitConverter.ToUInt32(lengthBuf, 0);
        if (length == 0 || length > MaxMessageSize) return null;
        var payload = new byte[length];
        if (await ReadExactAsync(stream, payload, ct) < payload.Length) return null;
        return JsonSerializer.Deserialize(Encoding.UTF8.GetString(payload), AobMakerUiJsonContext.Default.AobMakerUiMessage);
    }

    private static async Task<int> ReadExactAsync(Stream stream, byte[] buffer, CancellationToken ct)
    {
        var total = 0;
        while (total < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(total, buffer.Length - total), ct);
            if (read == 0) break;
            total += read;
        }
        return total;
    }
}
