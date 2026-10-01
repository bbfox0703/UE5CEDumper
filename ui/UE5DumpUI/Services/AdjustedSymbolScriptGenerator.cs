namespace UE5DumpUI.Services;

/// <summary>
/// [AOBM-GWORLD-GENAOB] A CE symbol script for a pointer whose signature ADJUSTS the RIP target (Himmel's
/// <c>adjustment</c>, e.g. GOBJ_AV1's -0x10 on Avowed).
/// </summary>
public static class AdjustedSymbolScriptGenerator
{
    public static string Generate(string symbolName, string module, string aob, int pos, int aobLen, int adjustment)
        => "";
}
