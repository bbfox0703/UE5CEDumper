namespace UE5DumpUI.Helpers;

/// <summary>
/// [CONSOLE-ROWRUN-SELECTED] The Console's status for a run it did not send because another is still in flight.
/// </summary>
internal static class ConsoleRunText
{
    internal const string KeyStillRunning = "str.Con.StillRunning";

    /// <summary>A second invoke sent while the first waits on a busy game thread could run the command twice: a
    /// timed-out invoke stays queued and still runs. The running command can be this same one, reached through a second
    /// path, so the text neither calls it "another" nor says to run this again.</summary>
    internal static string StillRunningText(string funcName) => AobMakerStatus.Say(KeyStillRunning,
        "⏳ {0} was not sent — a Console command is still running. Wait for its result first.", funcName);

    /// <summary>The same refusal for the Debug Camera Force (<paramref name="want"/> is "ON" or "OFF").</summary>
    internal static string ForceStillRunningText(string want) => StillRunningText("Force " + want);
}
