using UE5DumpUI.Models;

namespace UE5DumpUI.Mcp;

/// <summary>
/// [MCP-WRITE-GRANTS-2026-11-14] The DLL's rules for an AI's invoke, mirrored so that an invoke grant that
/// could never be used is refused when it is asked for, not at its first call: a grant on <c>K2_DestroyActor</c> is
/// never recorded (the host's prompt comes before the tool runs, so the user may still see one asked for). The DLL
/// still decides at every call (Voll::InvokeRefusedForBackground and, under a grant, Voll::InvokeParamIndirection);
/// <c>McpInvokeRulesTests</c> reads Voll.h and pins the lists here to it.
///
/// Stricter than the DLL in one way: a function taking an FText is refused too, as write_invoke's call builder refuses
/// one (a zeroed FText is a crash, not an empty text). Weaker in one way: walk_functions sends a struct's members one
/// level deep, while the DLL walks four.
/// </summary>
internal static class McpInvokeRules
{
    /// <summary>Voll::InvokeRefusedForBackground's names, lower case, compared with the function's lower-cased name.
    /// </summary>
    internal static readonly string[] RefusedNames =
    [
        "toggledebugcamera", "enabledebugcamera", "disabledebugcamera", "sendtoconsole", "serverexec",
        "consolecommand", "executeconsolecommand", "quitgame", "quit", "openlevel", "openlevelbysoftobjectptr",
        "clienttravel", "servertravel", "switchlevel", "localtravel", "cheatscript", "launchurl", "restartlevel",
        "restartgame", "resetlevel", "returntomainmenuhost", "k2_destroyactor", "k2_destroycomponent", "setlifespan",
        "destroytarget", "destroyall", "destroypawns", "destroyallpawnsexcepttarget", "savegametoslot",
        "asyncsavegametoslot", "savegametoslotforlocalplayer", "asyncsavegametoslotforlocalplayer", "deletegameinslot",
        "removeplayer", "setcollisionprofilenameproperty", "edittextpropertysourcestring", "addinterpfloat",
        "addinterpvector", "addinterplinearcolor", "remoteevent", "setbind", "loadstreamlevel",
        "loadstreamlevelbysoftobjectptr", "unloadstreamlevel", "unloadstreamlevelbysoftobjectptr", "streamlevelin",
        "streamlevelout", "onlyloadlevel", "setshouldbeloaded", "setisrequestingunloadandremoval", "loadlevelinstance",
        "loadlevelinstancebysoftobjectptr", "unloadlevelinstance", "writeprogress",
        "writeachievementprogress", "writeleaderboardinteger", "createproxyobjectforflush",
    ];

    /// <summary>Voll::InvokeRefusedForBackground's families: a name that starts with the prefix and ends with the
    /// suffix.</summary>
    internal static readonly (string Prefix, string Suffix)[] RefusedFamilies =
    [
        ("k2_settimer", ""), ("set", "propertybyname"), ("executeubergraph", ""),
        ("createproxyobjectforinapppurchase", ""),
    ];

    /// <summary>The functions refused by full name, lower case: an async action's Activate.</summary>
    internal static readonly string[] RefusedFullNames =
    [
        "//script/engine/blueprintasyncactionbase.activate", "//script/engine/onlineblueprintcallproxybase.activate",
    ];

    /// <summary>Voll::InvokeParamIndirection's name tokens: a name or string parameter holding one says what to call or
    /// write.</summary>
    internal static readonly string[] NameLikeTokens = ["func", "event", "property", "command", "delegate", "callback"];

    private const uint FuncNet = 0x00000040;
    private const uint FuncUbergraph = 0x00008000;

    private static readonly HashSet<string> DelegateTypes = new(StringComparer.Ordinal)
    {
        "DelegateProperty", "MulticastDelegateProperty", "MulticastInlineDelegateProperty",
        "MulticastSparseDelegateProperty",
    };

    private static readonly HashSet<string> StringTypes = new(StringComparer.Ordinal)
    {
        "NameProperty", "StrProperty", "Utf8StrProperty", "AnsiStrProperty", "TextProperty",
    };

    /// <summary>Why no invoke grant may name this function, or null: what the DLL refuses an AI's call of, and what a
    /// grant cannot show the user -- a parameter that reaches other functions or properties by name.</summary>
    internal static string? WhyNotGrantable(FunctionInfoModel fn)
    {
        if ((fn.FunctionFlags & FuncNet) != 0) return "is a network RPC: it can reach a server outside this machine";
        if ((fn.FunctionFlags & FuncUbergraph) != 0)
            return "is a Blueprint's event graph, entered at whatever entry point it is given";
        string name = fn.Name.ToLowerInvariant();
        string full = fn.FullName.ToLowerInvariant();
        if (RefusedFullNames.Contains(full))
            return "starts an async action, which can write to the online service, join a session or write a save slot";
        if (RefusedNames.Contains(name)) return "is on the list of calls the DLL refuses an AI session";
        foreach (var (prefix, suffix) in RefusedFamilies)
            if (name.Length >= prefix.Length + suffix.Length && name.StartsWith(prefix, StringComparison.Ordinal)
                && name.EndsWith(suffix, StringComparison.Ordinal))
                return "is in a family of calls the DLL refuses an AI session (they reach any function or property)";
        foreach (var p in fn.Parameters)
            if (WhyParam(p.Name, p.TypeName, p.StructName, p.ObjectClassName) is { } why) return why;
            else if (p.StructFields.FirstOrDefault(f => WhyParam(f.Name, f.TypeName, "", "") is not null) is { } member)
                return WhyParam(member.Name, member.TypeName, "", "");
        return null;
    }

    private static string? WhyParam(string name, string type, string structName, string objClass)
    {
        if (type == "StructProperty" && structName == "LatentActionInfo")
            return "is latent: its LatentActionInfo names the function the engine calls when it finishes";
        if (DelegateTypes.Contains(type)) return "takes a delegate (" + name + "), which calls whatever it is bound to";
        if (type == "FieldPathProperty") return "takes a property path (" + name + ")";
        if (objClass == "Function") return "takes a UFunction (" + name + ")";
        // A struct named for one too: an FGameplayTag EventTag wraps the event's name.
        if (StringTypes.Contains(type) || type == "StructProperty")
        {
            string n = name.ToLowerInvariant();
            if (NameLikeTokens.Any(t => n.Contains(t, StringComparison.Ordinal)))
                return "takes a name (" + name + ") that says what to call or write";
        }
        if (type == "TextProperty") return "takes an FText (" + name + "), which this server cannot send";
        return null;
    }
}
