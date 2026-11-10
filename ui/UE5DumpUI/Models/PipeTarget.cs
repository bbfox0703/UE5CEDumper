namespace UE5DumpUI.Models;

/// <summary>
/// [PIPE-PER-PROCESS-2026-11-06] One row of Connect's game list: a game serving \\.\pipe\UE5DumpBfx_&lt;pid&gt;, or the
/// legacy-pipe row (<see cref="IsLegacy"/>, <see cref="Pid"/> 0), which says an old DLL runs somewhere and cannot be
/// chosen: the legacy name carries no pid, and finding out whose it is would mean opening it.
/// </summary>
/// <param name="ImageName">The game's exe file name; "" when the process could not be read.</param>
/// <param name="WindowTitle">The game's main window title, or null when it has none.</param>
public sealed record PipeTarget(int Pid, string ImageName, string? WindowTitle, bool IsLegacy = false);
