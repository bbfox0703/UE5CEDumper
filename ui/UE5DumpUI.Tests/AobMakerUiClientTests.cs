using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using UE5DumpUI.Core;
using UE5DumpUI.Models;
using UE5DumpUI.Services;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// [AOBM-GNAMES-SYMBOL] The client for AOBMaker.UI's own pipe. Its quirks are the point: <c>success</c> is left out
/// when false, a refusal is a message starting <c>Rejected:</c>, <c>pos</c> / <c>aoblen</c> exist only for a
/// RIP-relative seed, and an unknown request gets silence.
/// </summary>
public class AobMakerUiClientTests
{
    private const string Address = "0x7FF610001000";

    [Fact]
    public void No_reply_is_NoReply()
        => Assert.Equal(GenerateAobFailure.NoReply, AobMakerUiClient.Interpret(null, Address, null).Failure);

    [Fact]
    public void A_reply_without_success_is_a_failure_carrying_the_servers_words()
    {
        var r = AobMakerUiClient.Interpret(Parse("""{"type":"GenerateAobResult","message":"Could not find a unique AOB","matchCount":3}"""),
                                           Address, null);
        Assert.Null(r.Aob);
        Assert.Equal(GenerateAobFailure.Failed, r.Failure);
        Assert.Equal("Could not find a unique AOB", r.ServerMessage);
    }

    [Fact]
    public void A_Rejected_message_is_a_refusal_not_a_failure()
    {
        var r = AobMakerUiClient.Interpret(
            Parse("""{"type":"GenerateAobResult","message":"Rejected: caller integrity is lower than the server's"}"""),
            Address, null);
        Assert.Equal(GenerateAobFailure.Refused, r.Failure);
    }

    [Fact]
    public void Success_without_an_aob_is_still_a_failure()
        => Assert.Equal(GenerateAobFailure.Failed,
            AobMakerUiClient.Interpret(Parse("""{"type":"GenerateAobResult","success":true,"aob":""}"""), Address, null).Failure);

    [Fact]
    public void A_RIP_relative_answer_keeps_pos_and_aoblen_and_a_plain_one_leaves_them_null()
    {
        var rip = AobMakerUiClient.Interpret(Parse(
            """{"success":true,"aob":"48 8B 05 ?? ?? ?? ?? 48 85 C0","injectionOffset":0,"matchCount":1,"pos":3,"aoblen":7,"module":"Game.exe"}"""),
            Address, null);
        Assert.Equal(GenerateAobFailure.None, rip.Failure);
        Assert.Equal(new GeneratedAob("48 8B 05 ?? ?? ?? ?? 48 85 C0", 0, 3, 7, 1, "Game.exe"), rip.Aob);

        var plain = AobMakerUiClient.Interpret(Parse("""{"success":true,"aob":"89 C8 C3","injectionOffset":2,"matchCount":1}"""),
                                               Address, null);
        Assert.Null(plain.Aob!.Pos);
        Assert.Null(plain.Aob.AobLen);
        Assert.Equal(2, plain.Aob.InjectionOffset);
    }

    [Fact]
    public void The_request_carries_only_type_address_and_processId()
    {
        var json = JsonSerializer.Serialize(
            new AobMakerUiMessage { Type = "GenerateAob", Address = Address, ProcessId = 4242 },
            AobMakerUiJsonContext.Default.AobMakerUiMessage);
        Assert.Equal($$"""{"type":"GenerateAob","address":"{{Address}}","processId":4242}""", json);
    }

    // ---- over a real pipe, with the internal seam's short deadlines ----

    private static string NobodysPipe() => "UE5DumpUITest_AobUi_" + Guid.NewGuid().ToString("N");

    [Fact]
    public async Task No_server_is_NotRunning()
    {
        var client = new AobMakerUiClient(new MockLoggingService(), NobodysPipe(), 150, 500);
        var r = await client.GenerateAobAsync(Address, 4242, TestContext.Current.CancellationToken);
        Assert.Equal(GenerateAobFailure.NotRunning, r.Failure);
    }

    [Fact]
    public async Task A_timeout_on_a_listed_pipe_is_Busy_not_NotRunning()
    {
        // [AOBM-UI-BUSY] AOBMaker.UI has ONE pipe instance. While it serves someone else a connect times out although
        // the app runs, and the user was told to start an app that was already open.
        var client = new AobMakerUiClient(new MockLoggingService(), NobodysPipe(), 150, 500, _ => true);
        var r = await client.GenerateAobAsync(Address, 4242, TestContext.Current.CancellationToken);
        Assert.Equal(GenerateAobFailure.Busy, r.Failure);
    }

    [Fact]
    public async Task A_timeout_on_an_unlisted_pipe_stays_NotRunning()
    {
        var client = new AobMakerUiClient(new MockLoggingService(), NobodysPipe(), 150, 500, _ => false);
        var r = await client.GenerateAobAsync(Address, 4242, TestContext.Current.CancellationToken);
        Assert.Equal(GenerateAobFailure.NotRunning, r.Failure);
    }

    [Fact]
    public async Task IsPipeListed_asks_for_its_own_pipe()
    {
        var name = NobodysPipe();
        string? asked = null;
        var client = new AobMakerUiClient(new MockLoggingService(), name, 150, 500, n => { asked = n; return true; });
        Assert.True(await client.IsPipeListedAsync(TestContext.Current.CancellationToken));
        Assert.Equal(name, asked);
    }

    [Fact]
    public async Task Listing_sees_a_live_pipe_without_connecting_to_it()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "the \\\\.\\pipe\\ namespace is Windows-only");
        var name = NobodysPipe();
        var ct = TestContext.Current.CancellationToken;
        var client = new AobMakerUiClient(new MockLoggingService(), name, 150, 500);
        using (var server = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
                                                      PipeOptions.Asynchronous))
        {
            var waiting = server.WaitForConnectionAsync(ct);

            Assert.True(await client.IsPipeListedAsync(ct));
            await Task.Delay(100, ct);
            Assert.False(waiting.IsCompleted);   // nobody connected: AOBMaker.UI would have logged nothing

            server.Dispose();
            try { await waiting; } catch (Exception) { /* the dispose ends the wait */ }
        }
        Assert.False(await client.IsPipeListedAsync(ct));
    }

    [Fact]
    public async Task A_round_trip_sends_the_prefixed_address_and_reads_the_answer()
    {
        var name = NobodysPipe();
        var ct = TestContext.Current.CancellationToken;
        using var server = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
                                                     PipeOptions.Asynchronous);
        var serve = Task.Run(async () =>
        {
            await server.WaitForConnectionAsync(ct);
            var request = await ReadFrameAsync(server, ct);
            await WriteFrameAsync(server,
                """{"type":"GenerateAobResult","success":true,"aob":"48 8D 0D ?? ?? ?? ??","injectionOffset":0,"matchCount":1,"pos":3,"aoblen":7,"module":"Game.exe"}""",
                ct);
            return request;
        }, ct);

        var client = new AobMakerUiClient(new MockLoggingService(), name, 2000, 5000);
        var r = await client.GenerateAobAsync("7FF610001000", 4242, ct);   // bare hex: the client adds 0x

        var sent = JsonDocument.Parse(await serve).RootElement;
        Assert.Equal("GenerateAob", sent.GetProperty("type").GetString());
        Assert.Equal(Address, sent.GetProperty("address").GetString());
        Assert.Equal(4242, sent.GetProperty("processId").GetInt32());
        Assert.Equal(GenerateAobFailure.None, r.Failure);
        Assert.Equal(3, r.Aob!.Pos);
    }

    [Fact]
    public async Task A_server_that_stays_silent_is_NoReply_within_the_deadline()
    {
        // AOBMaker.UI answers a request type it does not know with silence, so an older build looks exactly like this.
        var name = NobodysPipe();
        var ct = TestContext.Current.CancellationToken;
        using var server = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
                                                     PipeOptions.Asynchronous);
        var serve = Task.Run(async () =>
        {
            await server.WaitForConnectionAsync(ct);
            await ReadFrameAsync(server, ct);
            await Task.Delay(3000, ct);   // hold the connection open, say nothing
        }, ct);

        var client = new AobMakerUiClient(new MockLoggingService(), name, 2000, 300);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var r = await client.GenerateAobAsync(Address, 4242, ct);

        Assert.Equal(GenerateAobFailure.NoReply, r.Failure);
        Assert.True(sw.ElapsedMilliseconds < 2500, $"waited {sw.ElapsedMilliseconds} ms");
    }

    private static AobMakerUiMessage? Parse(string json)
        => JsonSerializer.Deserialize(json, AobMakerUiJsonContext.Default.AobMakerUiMessage);

    private static async Task<string> ReadFrameAsync(Stream s, CancellationToken ct)
    {
        var len = new byte[4];
        await s.ReadExactlyAsync(len, ct);
        var payload = new byte[BitConverter.ToUInt32(len, 0)];
        await s.ReadExactlyAsync(payload, ct);
        return Encoding.UTF8.GetString(payload);
    }

    private static async Task WriteFrameAsync(Stream s, string json, CancellationToken ct)
    {
        var payload = Encoding.UTF8.GetBytes(json);
        await s.WriteAsync(BitConverter.GetBytes((uint)payload.Length), ct);
        await s.WriteAsync(payload, ct);
        await s.FlushAsync(ct);
    }
}
