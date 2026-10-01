namespace UE5DumpUI.Services;

/// <summary>[AOBM-DISSECT-INJECT] What <see cref="CsxStructurePushGenerator.Generate"/> made: the AA script, the root
/// structure's name, and how many structures and elements it builds.</summary>
public sealed record CsxPushScript(string Script, string RootName, int Structures, int Elements);

/// <summary>
/// [AOBM-DISSECT-INJECT] The CSX half: turn Live Walker's own CSX export into a CE Lua script that builds the same
/// structures in Structure Dissect, pushed through the CE plugin's <c>CreateAAScript</c>.
/// </summary>
public static class CsxStructurePushGenerator
{
    /// <summary>The pushed record's description prefix; the root structure's name follows it.</summary>
    public const string RecordPrefix = "UE5CEDumper: Structure ";

    public static CsxPushScript Generate(string csx) => new("", "", 0, 0);
}
