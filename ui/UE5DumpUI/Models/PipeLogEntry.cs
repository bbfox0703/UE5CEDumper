namespace UE5DumpUI.Models;

/// <summary>
/// One line of named-pipe traffic for the System-tab "Pipe Activity" log — a
/// live, in-UI tail of the same TX/RX the pipe log file records. It lets the
/// user watch light commands stay responsive (interleave) while a heavy scan
/// runs (the interactive / bulk lane split) without opening the %LOCALAPPDATA% logs.
/// Immutable; constructed on the pipe threads and rendered on the UI thread.
/// </summary>
public sealed class PipeLogEntry
{
    /// <summary>Local wall-clock "HH:mm:ss.fff" when the line was recorded.</summary>
    public string Time { get; init; } = "";

    /// <summary>Lane tag — "I" interactive / "B" bulk (empty if single-connection).</summary>
    public string Lane { get; init; } = "";

    /// <summary>Direction glyph: "→" sent (TX), "←" reply (RX), "⚡" push event.</summary>
    public string Dir { get; init; } = "";

    /// <summary>Command name (TX/RX) or event type (push event).</summary>
    public string Cmd { get; init; } = "";

    /// <summary>Request id (0 = none, e.g. a push event).</summary>
    public int Id { get; init; }

    /// <summary>Trailing detail — e.g. the round-trip "12 ms" or "err 30 ms".</summary>
    public string Detail { get; init; } = "";

    /// <summary>Pre-formatted single monospace line for the activity list.</summary>
    public string Display =>
        $"{Time} {Lane,-1} {Dir,-2} {Cmd,-26} {(Id > 0 ? "#" + Id.ToString() : ""),-6} {Detail}";
}
