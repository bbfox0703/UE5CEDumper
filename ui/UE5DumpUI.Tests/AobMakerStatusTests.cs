using UE5DumpUI.Core;
using UE5DumpUI.Helpers;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// [AOBMAKER-EVAL-2026-09-29] The shared AOBMaker flag and the [AOBM-ATTACH-CHECK] warning. The check warns and
/// never refuses, and a check that cannot tell must not accuse.
/// </summary>
public class AobMakerStatusTests
{
    [Fact]
    public void Unknown_on_either_side_or_the_same_pid_is_not_a_warning()
    {
        Assert.Equal("", AobMakerStatus.DescribeAttach(null, 4242, "Game.exe"));
        Assert.Equal("", AobMakerStatus.DescribeAttach(new CeAttachedProcess(999, "Other.exe"), 0, "Game.exe"));
        Assert.Equal("", AobMakerStatus.DescribeAttach(new CeAttachedProcess(4242, "Game.exe"), 4242, "Game.exe"));
    }

    [Fact]
    public void Pid_zero_means_CE_has_nothing_open()
    {
        var text = AobMakerStatus.DescribeAttach(new CeAttachedProcess(0, ""), 4242, "Game.exe");
        Assert.Equal("Cheat Engine has no process open: open Game.exe in CE before pushing to it", text);
    }

    [Fact]
    public void Another_pid_is_named_and_compared_by_id_not_by_name()
    {
        // Same exe name, different launch: still the wrong process. The plugin reads the name through the ANSI API,
        // so the name cannot be the key.
        var text = AobMakerStatus.DescribeAttach(new CeAttachedProcess(999, "Game.exe"), 4242, "Game.exe");
        Assert.Contains("Game.exe (pid 999)", text);
        Assert.Contains("not Game.exe (pid 4242)", text);

        Assert.Contains("? (pid 7)", AobMakerStatus.DescribeAttach(new CeAttachedProcess(7, ""), 4242, "Game.exe"));
    }

    [Fact]
    public async Task CheckAttach_publishes_the_warning_and_clears_it_again()
    {
        var bridge = new ScriptedAobMakerBridge { Available = true, Attached = new CeAttachedProcess(999, "Other.exe") };
        var status = new AobMakerStatus(bridge);
        var raised = new List<string?>();
        status.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        var text = await status.CheckAttachAsync(4242, "Game.exe");

        Assert.Equal(text, status.AttachWarning);
        Assert.True(status.HasAttachWarning);
        Assert.Contains(nameof(AobMakerStatus.HasAttachWarning), raised);

        bridge.Attached = new CeAttachedProcess(4242, "Game.exe");
        Assert.Equal("", await status.CheckAttachAsync(4242, "Game.exe"));
        Assert.False(status.HasAttachWarning);
    }

    [Fact]
    public async Task A_bridge_without_the_command_answers_cannot_tell()
    {
        // IAobMakerBridge.GetAttachedProcessAsync has a default body, so an older double (or plugin) is "unknown",
        // never "nothing open".
        var status = new AobMakerStatus(new BridgeWithoutAttachQuery());
        Assert.Equal("", await status.CheckAttachAsync(4242, "Game.exe"));
        Assert.Equal("", await new AobMakerStatus(null).CheckAttachAsync(4242, "Game.exe"));
    }

    [Fact]
    public async Task Probe_publishes_and_a_throwing_bridge_reads_as_unavailable()
    {
        var bridge = new ScriptedAobMakerBridge { Available = true };
        var status = new AobMakerStatus(bridge);
        Assert.True(await status.ProbeAsync());
        Assert.True(status.IsAvailable);

        var throwing = new AobMakerStatus(new BridgeWithoutAttachQuery { ThrowOnCheck = true });
        Assert.False(await throwing.ProbeAsync());
        Assert.False(throwing.IsAvailable);
    }

    [Fact]
    public void TryProbe_does_not_reconnect_on_every_tab_switch()
    {
        // Each probe is a pipe connect that waits up to 2 s when CE is absent.
        var bridge = new ScriptedAobMakerBridge { Available = true };
        var status = new AobMakerStatus(bridge);

        status.TryProbe();
        status.TryProbe();
        status.TryProbe();

        Assert.Equal(1, bridge.CheckCalls);
    }

    // ---- the plugin command itself, over a real pipe (the bridge's internal seam) ----

    [Theory]
    [InlineData("""{"type":"AttachedProcessResult","success":true,"processId":4242,"processName":"Game.exe"}""", 4242, "Game.exe")]
    [InlineData("""{"type":"AttachedProcessResult","success":true,"processId":0,"processName":""}""", 0, "")]
    public async Task GetAttachedProcess_reads_the_plugins_answer(string reply, int pid, string name)
    {
        var sent = await ServeOnceAsync(reply, async bridge =>
        {
            var ce = await bridge.GetAttachedProcessAsync(TestContext.Current.CancellationToken);
            Assert.Equal(new CeAttachedProcess(pid, name), ce);
        });
        Assert.Contains("\"type\":\"GetAttachedProcess\"", sent);
    }

    [Theory]
    [InlineData("""{"type":"AttachedProcessResult","success":false,"message":"no"}""")]
    [InlineData("""{"type":"AttachedProcessResult","success":true}""")]
    public async Task GetAttachedProcess_without_a_pid_is_cannot_tell(string reply)
        => await ServeOnceAsync(reply, async bridge =>
            Assert.Null(await bridge.GetAttachedProcessAsync(TestContext.Current.CancellationToken)));

    /// <summary>Serve one request on a fresh pipe with <paramref name="reply"/>; returns what the bridge sent.</summary>
    private static async Task<string> ServeOnceAsync(string reply, Func<UE5DumpUI.Services.AobMakerBridgeService, Task> act)
    {
        var name = "UE5DumpUITest_Attach_" + Guid.NewGuid().ToString("N");
        var ct = TestContext.Current.CancellationToken;
        using var server = new System.IO.Pipes.NamedPipeServerStream(name, System.IO.Pipes.PipeDirection.InOut, 1,
            System.IO.Pipes.PipeTransmissionMode.Byte, System.IO.Pipes.PipeOptions.Asynchronous);
        var serve = Task.Run(async () =>
        {
            await server.WaitForConnectionAsync(ct);
            var len = new byte[4];
            await server.ReadExactlyAsync(len, ct);
            var payload = new byte[BitConverter.ToUInt32(len, 0)];
            await server.ReadExactlyAsync(payload, ct);
            var answer = System.Text.Encoding.UTF8.GetBytes(reply);
            await server.WriteAsync(BitConverter.GetBytes((uint)answer.Length), ct);
            await server.WriteAsync(answer, ct);
            await server.FlushAsync(ct);
            return System.Text.Encoding.UTF8.GetString(payload);
        }, ct);

        await act(new UE5DumpUI.Services.AobMakerBridgeService(new MockLoggingService(), name, 2000, _ => true));
        return await serve;
    }

    [Fact]
    public async Task The_attach_warning_goes_when_the_plugin_does()
    {
        // Measured 2026-09-29: with the warning showing, Cheat Engine closed, and the chip went Offline with
        // "CE is not on this game" still beside it. Unreachable means nothing can tell, and a check that cannot tell
        // must not accuse.
        var bridge = new ScriptedAobMakerBridge { Available = true, Attached = new CeAttachedProcess(999, "Other.exe") };
        var status = new AobMakerStatus(bridge);
        status.Apply(true);
        await status.CheckAttachAsync(4242, "Game.exe");
        Assert.True(status.HasAttachWarning);

        bridge.Available = false;
        await status.ProbeAsync();

        Assert.False(status.IsAvailable);
        Assert.False(status.HasAttachWarning);
        Assert.Equal("", status.AttachWarning);
    }

    [Fact]
    public void A_push_that_finds_the_pipe_gone_clears_the_warning_too()
    {
        var status = new AobMakerStatus(new ScriptedAobMakerBridge { Available = true });
        status.Apply(true);
        status.AttachWarning = "Cheat Engine has Other.exe (pid 999) open";

        status.Apply(false);

        Assert.False(status.HasAttachWarning);
    }

    [Fact]
    public async Task The_DLL_tip_says_what_the_plugin_is_for_or_why_it_is_unreachable()
    {
        var bridge = new ScriptedAobMakerBridge { Available = true };
        var status = new AobMakerStatus(bridge);
        await status.ProbeAsync();
        Assert.Contains("connected", status.DllTip);

        bridge.Available = false;
        var raised = new List<string?>();
        status.PropertyChanged += (_, e) => raised.Add(e.PropertyName);
        await status.ProbeAsync();

        Assert.Equal(AobMakerUnavailable.Text(bridge), status.DllTip);
        Assert.Contains(nameof(AobMakerStatus.DllTip), raised);
    }

    [Fact]
    public async Task A_second_failure_repaints_the_reason_although_the_flag_did_not_change()
    {
        var status = new AobMakerStatus(new ScriptedAobMakerBridge { Available = false });
        await status.ProbeAsync();
        var raised = new List<string?>();
        status.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        await status.ProbeAsync();

        Assert.DoesNotContain(nameof(AobMakerStatus.IsAvailable), raised);
        Assert.Contains(nameof(AobMakerStatus.DllTip), raised);
    }

    private sealed class BridgeWithoutAttachQuery : IAobMakerBridge
    {
        public bool ThrowOnCheck { get; init; }
        public bool IsAvailable => true;
        public Task<bool> CheckAvailabilityAsync(CancellationToken ct = default)
            => ThrowOnCheck ? throw new IOException("pipe broke") : Task.FromResult(true);
        public Task<bool> NavigateHexViewAsync(string hexAddress, CancellationToken ct = default) => Task.FromResult(true);
        public Task<bool> NavigateDisassemblerAsync(string hexAddress, CancellationToken ct = default) => Task.FromResult(true);
        public Task<bool> CreateAAScriptAsync(string description, string script, bool autoActivate = true,
            string? group = null, CancellationToken ct = default) => Task.FromResult(true);
        public Task<bool> CreateSymbolScriptAsync(string name, string aob, int pos, int aoblen, string symbol,
            string module, bool autoActivate = true, CancellationToken ct = default) => Task.FromResult(true);
        public Task<bool> CreateMemoryRecordAsync(string description, string address, int valueType,
            bool isSigned = false, bool showAsHex = false, CancellationToken ct = default) => Task.FromResult(true);
        public Task<(bool Ok, string? ErrorMessage)> InjectTableFileAsync(string fileName, string content,
            CancellationToken ct = default) => Task.FromResult((true, (string?)null));
    }
}
