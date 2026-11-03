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

    internal static bool IsPinnable(string pick) => true;

    internal static string Note(string pick, string name, string className) => "";

    internal static string ObjectText(string name, string className)
        => AobMakerStatus.Say(KeyObject, "on {0} ({1})", name, className);

    internal static string ClassDefaultText(string name, string className)
        => AobMakerStatus.Say(KeyClassDefault,
            "⚠ ran on {0}, the class default object: no live {1} exists, so an engine cheat command has no effect " +
            "there (a command that does not use its owner may still work)", name, className);

    internal static string DebugCameraOwnedText(string name, string className)
        => AobMakerStatus.Say(KeyDebugCameraOwned,
            "⚠ ran on {0} ({1}), which belongs to the Debug Camera: no other live one exists", name, className);
}
