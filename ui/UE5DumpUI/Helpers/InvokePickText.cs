namespace UE5DumpUI.Helpers;

/// <summary>
/// [FINDINSTANCE-SUBSTRING-USERFACING] What the Console says about, and whether it pins, the object a class name
/// resolved to (<c>invoke_function</c>'s <c>instance_pick</c>).
/// </summary>
internal static class InvokePickText
{
    internal const string KeyObject = "str.Invoke.Pick.Object";
    internal const string KeyClassDefault = "str.Invoke.Pick.ClassDefault";
    internal const string KeyDebugCameraOwned = "str.Invoke.Pick.DebugCameraOwned";

    /// <summary>Whether the Console may keep running the class's commands on this object. A class-default pick
    /// re-resolves instead, so a CheatManager spawned later is found, and so does a Debug-Camera-owned one, so a
    /// Debug Camera destroyed later leaves no pin on it. An empty pick is an older DLL that never said: pinned, as
    /// before.</summary>
    internal static bool IsPinnable(string pick)
        => pick is "" or Constants.InvokePickLive or Constants.InvokePickAddress;

    /// <summary>What to append to the run's status line: " " and the sentence for the pick, or "" when the DLL named
    /// no object.</summary>
    internal static string Note(string pick, string name, string className)
    {
        if (pick.Length == 0 || name.Length == 0) return "";
        return " " + pick switch
        {
            // A partial-name pick that is a default object has no live object of its class either.
            Constants.InvokePickClassDefault or Constants.InvokePickPartialNameDefault => ClassDefaultText(name, className),
            Constants.InvokePickDebugCameraOwned => DebugCameraOwnedText(name, className),
            _ => ObjectText(name, className),
        };
    }

    internal static string ObjectText(string name, string className)
        => AobMakerStatus.Say(KeyObject, "on {0} ({1})", name, className);

    // Neither sentence says the call ran: the note is on the failure line too, where nothing may have been dispatched.
    internal static string ClassDefaultText(string name, string className)
        => AobMakerStatus.Say(KeyClassDefault,
            "⚠ the object is {0}, the class default object: no live {1} exists, so an engine cheat command has no " +
            "effect there (a command that does not use its owner may still work)", name, className);

    internal static string DebugCameraOwnedText(string name, string className)
        => AobMakerStatus.Say(KeyDebugCameraOwned,
            "⚠ the object is {0} ({1}), which belongs to the Debug Camera: no other live one exists", name, className);
}
