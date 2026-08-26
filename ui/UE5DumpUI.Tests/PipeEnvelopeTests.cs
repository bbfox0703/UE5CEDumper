using System.Text.Json.Nodes;
using UE5DumpUI.Helpers;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// [STALLDEFAULT-2026-08-26]. The DLL used to stamp
/// <c>game_thread_stalled: false</c> on every success envelope by computing
/// <c>!IsGameThreadResponsive()</c> — a GATE predicate that maps "no PE hook yet, so
/// nothing was measured" to "responsive". So the wire asserted a healthy game thread
/// on a fresh connection, which is exactly when nothing has been measured.
///
/// <para>It now withholds the key when it cannot tell. That makes ABSENCE meaningful,
/// and this is where the meaning is decided.</para>
/// </summary>
public class PipeEnvelopeTests
{
    private static JsonObject O(string json) => (JsonObject)JsonNode.Parse(json)!;

    /// <summary>A push event states nothing about liveness and must leave a standing
    /// banner alone. It has never carried the key.</summary>
    [Fact]
    public void A_push_event_is_not_an_observation()
    {
        Assert.False(PipeEnvelope.TryReadStalled(O("""{"event":"scan_progress"}"""), out _));
    }

    /// <summary>
    /// So does an error envelope — <c>MakeError</c> builds a 3-key object that has never
    /// carried the hint. ⚠ Justified from that pinned shape, NOT from "a stalled game
    /// produces errors": a game-thread dispatch timeout is reported as <c>ok:true</c>
    /// with the text in <c>data["error"]</c>, and it DOES carry the hint.
    /// </summary>
    [Fact]
    public void An_error_envelope_is_not_an_observation()
    {
        Assert.False(PipeEnvelope.TryReadStalled(
            O("""{"id":1,"ok":false,"error":"boom"}"""), out _));
    }

    /// <summary>
    /// ⭐ The withdrawal, and the reason the DLL half and the client half had to ship in
    /// one commit. The hook can go back DOWN mid-session (Frieren's validation-failure
    /// path calls <c>Stark::RemoveHook()</c>), which returns liveness to unknown. If a
    /// withheld key were simply ignored, a banner raised by a real measured stall would
    /// stay up for the rest of the session.
    /// </summary>
    [Fact]
    public void A_success_envelope_without_the_key_withdraws_the_claim()
    {
        Assert.True(PipeEnvelope.TryReadStalled(O("""{"id":1,"ok":true}"""), out var stalled));
        Assert.False(stalled);
    }

    [Fact]
    public void A_measured_stall_is_reported()
    {
        Assert.True(PipeEnvelope.TryReadStalled(
            O("""{"id":1,"ok":true,"game_thread_stalled":true}"""), out var stalled));
        Assert.True(stalled);
    }

    [Fact]
    public void A_measured_responsive_is_reported()
    {
        Assert.True(PipeEnvelope.TryReadStalled(
            O("""{"id":1,"ok":true,"game_thread_stalled":false}"""), out var stalled));
        Assert.False(stalled);
    }

    /// <summary>
    /// The REAL stalled-game reply: a game-thread dispatch timeout is a SUCCESS
    /// envelope whose payload carries an <c>error</c> string. A paused game raises its
    /// banner through exactly this shape, so treating a payload <c>error</c> as grounds
    /// to withdraw would stop the banner ever appearing.
    /// </summary>
    [Fact]
    public void A_dispatch_timeout_still_reports_the_stall()
    {
        const string reply =
            """{"id":1,"ok":true,"game_thread_stalled":true,"error":"ProcessEvent error code -5 (game-thread dispatch timeout)"}""";
        Assert.True(PipeEnvelope.TryReadStalled(O(reply), out var stalled));
        Assert.True(stalled);
    }

    /// <summary>
    /// ⭐ The hazard that made <c>TryGetValue</c> mandatory. <c>GetValue&lt;bool&gt;()</c>
    /// throws <c>InvalidOperationException</c> on a non-bool, and PipeClient's read loop
    /// catches only <c>JsonException</c> — the throw escapes to the outer handler, the
    /// loop exits, and every in-flight request fails with "Pipe disconnected". A
    /// malformed value must be ignored, not kill the lane.
    /// </summary>
    [Fact]
    public void A_malformed_value_is_ignored_rather_than_killing_the_lane()
    {
        var ex = Record.Exception(() => PipeEnvelope.TryReadStalled(
            O("""{"id":1,"ok":true,"game_thread_stalled":"unknown"}"""), out var stalled));
        Assert.Null(ex);

        Assert.True(PipeEnvelope.TryReadStalled(
            O("""{"id":1,"ok":true,"game_thread_stalled":"unknown"}"""), out var s2));
        Assert.False(s2);
    }

    /// <summary>An <c>ok</c> that is not a bool must not throw either.</summary>
    [Fact]
    public void A_malformed_ok_is_not_an_observation()
    {
        var ex = Record.Exception(() => PipeEnvelope.TryReadStalled(
            O("""{"id":1,"ok":"yes"}"""), out _));
        Assert.Null(ex);
        Assert.False(PipeEnvelope.TryReadStalled(O("""{"id":1,"ok":"yes"}"""), out _));
    }
}
