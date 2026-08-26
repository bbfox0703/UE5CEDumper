using System.Text.Json.Nodes;

namespace UE5DumpUI.Helpers;

/// <summary>
/// Reading the cross-cutting hints the DLL rides on every response envelope.
/// Pure + static so it is unit-testable: <c>PipeClient</c> itself owns a real
/// <c>NamedPipeClientStream</c> and cannot be driven from a test.
/// </summary>
internal static class PipeEnvelope
{
    /// <summary>
    /// True when this line is a SUCCESS envelope — i.e. the DLL stated an opinion
    /// about game-thread liveness at all. Push events and <c>ok:false</c> errors
    /// return FALSE: they say nothing, and must leave a standing banner alone.
    ///
    /// <para><paramref name="stalled"/> is the measured value when the DLL sent one,
    /// and FALSE when it withheld the key. Withholding means "no PE hook yet, so I
    /// cannot tell" — and that must WITHDRAW an unprovable claim rather than be
    /// silently ignored: the hook can go back down mid-session
    /// (<c>Frieren.cpp Stark::RemoveHook()</c> on a validation failure), and a banner
    /// raised by a real measurement would then stay up for the rest of the session.
    /// An older DLL that never sent the key lands here too, and "not stalled" is the
    /// right reading of silence from one.</para>
    ///
    /// <para>⚠ <c>TryGetValue</c>, never <c>GetValue&lt;bool&gt;()</c>: the latter throws
    /// <c>InvalidOperationException</c> on a non-bool, which is NOT caught by
    /// PipeClient's <c>catch (JsonException)</c>. It escapes to the outer handler, the
    /// read loop exits, and every in-flight request fails with "Pipe disconnected" —
    /// a malformed value would kill the lane rather than be ignored.
    /// (STALLDEFAULT-2026-08-26)</para>
    /// </summary>
    public static bool TryReadStalled(JsonObject obj, out bool stalled)
    {
        stalled = false;
        if (obj["event"] is not null) return false;
        if ((obj["ok"] as JsonValue)?.TryGetValue<bool>(out var ok) != true || !ok) return false;
        if ((obj["game_thread_stalled"] as JsonValue)?.TryGetValue<bool>(out var s) == true)
            stalled = s;
        return true;
    }
}
