using System.Text.Json;
using UE5DumpUI.Models;
using UE5DumpUI.Services;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// Covers the InjectTableFile path added so UE5DumpUI can ship the
/// runtime helper Lua straight into the user's open CE table without
/// the manual save-to-disk + Table -&gt; Add File... dance.
///
/// Three layers exercised here:
/// 1. Wire model -- the new fileName/content fields make it into the
///    serialized JSON (and only when set, since AOT serializer omits
///    nulls).
/// 2. Bridge service argument validation -- empty fileName / empty
///    content reject before any pipe round-trip happens.
/// 3. End-to-end against the AOBMaker plugin pipe is intentionally NOT
///    here: that requires CE running, which test environments don't
///    have. The plugin-side handler self-verifies via Stream.Size so a
///    bad payload would surface as an InjectTableFileResult.success=false
///    in production.
/// </summary>
public class AobMakerInjectTableFileTests
{
    [Fact]
    public void Serialize_InjectTableFile_EmitsFileNameAndContent()
    {
        var msg = new AobMakerMessage
        {
            Type = "InjectTableFile",
            FileName = "ue5_invoke_helper.lua",
            Content = "print('hello')",
        };

        var json = JsonSerializer.Serialize(msg, AobMakerJsonContext.Relaxed.AobMakerMessage);

        Assert.Contains("\"type\":\"InjectTableFile\"", json);
        Assert.Contains("\"fileName\":\"ue5_invoke_helper.lua\"", json);
        Assert.Contains("\"content\":\"print('hello')\"", json);

        // Other unrelated fields stay omitted (AOT context defaults to
        // ignore-when-null/default), so the wire payload is minimal.
        Assert.DoesNotContain("\"address\"", json);
        Assert.DoesNotContain("\"script\"", json);
        Assert.DoesNotContain("\"symbol\"", json);
    }

    [Fact]
    public void Serialize_RelaxedEncoder_KeepsLiteralSingleQuotes()
    {
        // CE Lua's JSON parser doesn't decode \uXXXX escapes -- the
        // relaxed encoder must emit literal ' rather than ' for
        // the AOBMaker plugin to round-trip the helper content.
        var msg = new AobMakerMessage
        {
            Type = "InjectTableFile",
            FileName = "x.lua",
            Content = "if not loaded then loaded = true end -- 'hi'",
        };

        var json = JsonSerializer.Serialize(msg, AobMakerJsonContext.Relaxed.AobMakerMessage);

        Assert.Contains("'hi'", json);
        Assert.DoesNotContain("\\u0027", json);
    }

    [Fact]
    public async Task InjectTableFileAsync_EmptyFileName_Throws()
    {
        var bridge = new AobMakerBridgeService();
        await Assert.ThrowsAsync<ArgumentException>(
            () => bridge.InjectTableFileAsync("", "content", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task InjectTableFileAsync_EmptyContent_Throws()
    {
        var bridge = new AobMakerBridgeService();
        await Assert.ThrowsAsync<ArgumentException>(
            () => bridge.InjectTableFileAsync("foo.lua", "", TestContext.Current.CancellationToken));
    }

    // ---- [W1-PIPEBUSY-LOG] a BUSY pipe is not "Cheat Engine not running" ----
    //
    // The internal seam supplies a pipe name nobody serves, a short timeout, and the existence probe -- so these never
    // reach a real Cheat Engine and never pay the 2 s connect the note below was avoiding.

    private static string NobodysPipe() => "UE5DumpUITest_" + Guid.NewGuid().ToString("N");

    [Fact]
    public async Task Connect_to_a_BUSY_pipe_is_logged_as_busy_not_as_CE_not_running()
    {
        // A pipe whose only instance another client holds times out exactly like an absent one (measured 2026-09-10
        // with two Cheat Engines: the loser's plugin retry-spams err=231 while every window looks fine).
        var log = new MockLoggingService();
        var bridge = new AobMakerBridgeService(log, NobodysPipe(), 150, _ => true);

        Assert.False(await bridge.CheckAvailabilityAsync(TestContext.Current.CancellationToken));

        Assert.Contains(log.Messages, m => m.StartsWith("[WARN", StringComparison.Ordinal)
                                         && m.Contains("EXISTS but no instance was free", StringComparison.Ordinal));
        Assert.DoesNotContain(log.Messages, m => m.Contains("Cheat Engine not running", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Connect_to_an_ABSENT_pipe_stays_a_quiet_not_running()
    {
        // The control, green both ways: nothing listening is the common case and stays at Debug.
        var log = new MockLoggingService();
        var bridge = new AobMakerBridgeService(log, NobodysPipe(), 150, _ => false);

        Assert.False(await bridge.CheckAvailabilityAsync(TestContext.Current.CancellationToken));

        Assert.Contains(log.Messages, m => m.StartsWith("[DEBUG", StringComparison.Ordinal)
                                         && m.Contains("Cheat Engine not running", StringComparison.Ordinal));
        Assert.DoesNotContain(log.Messages, m => m.StartsWith("[WARN", StringComparison.Ordinal));
    }

    // Note: a "no-CE-plugin -> graceful false" test through the PUBLIC constructor is still
    // omitted -- it would attempt the real 2 s connect, and could reach a real Cheat Engine.
    // The two tests above go through the internal seam instead; the MainWindowViewModel-level
    // test (MainWindowInjectHelperTests) covers the unavailable code path via a recording bridge.
}
