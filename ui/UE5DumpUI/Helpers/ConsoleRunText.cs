namespace UE5DumpUI.Helpers;

/// <summary>
/// [CONSOLE-ROWRUN-SELECTED] The Console's status for a run it did not send because another is still in flight.
/// </summary>
internal static class ConsoleRunText
{
    internal const string KeyStillRunning = "str.Con.StillRunning";

    /// <summary>A second invoke sent while the first waits on a busy game thread could run the command twice: a
    /// timed-out invoke stays queued and still runs.</summary>
    internal static string StillRunningText(string funcName) => AobMakerStatus.Say(KeyStillRunning,
        "⏳ {0} was not sent — another command is still running. Run it again once that one finishes.", funcName);
}
