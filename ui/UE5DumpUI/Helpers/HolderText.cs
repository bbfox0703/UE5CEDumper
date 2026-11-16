using UE5DumpUI.Models;

namespace UE5DumpUI.Helpers;

// RED: the keys and the surface only.
internal static class HolderText
{
    internal const string KeyAi = "str.TP.HolderAi";
    internal const string KeyAiNoPid = "str.TP.HolderAiNoPid";
    internal const string KeyCe = "str.TP.HolderCe";
    internal const string KeyOther = "str.TP.HolderOther";
    internal const string KeyOtherNoPid = "str.TP.HolderOtherNoPid";
    internal const string KeyExited = "str.TP.HolderExited";
    internal const string KeyUnrecorded = "str.TP.HolderUnrecorded";
    internal const string KeyStOne = "str.TP.HolderStOne";
    internal const string KeyStMany = "str.TP.HolderStMany";
    internal const string KeyStCeOne = "str.TP.HolderStCeOne";
    internal const string KeyStCeMany = "str.TP.HolderStCeMany";

    internal static string For(HoldEntry? hold, Func<long, bool> exited) => "";

    internal static string ForSeeThrough(SeeThroughStatus status) => "";
}
