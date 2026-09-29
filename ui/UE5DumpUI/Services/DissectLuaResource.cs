using System.IO;

namespace UE5DumpUI.Services;

/// <summary>
/// [AOBM-DISSECT-INJECT] Reads the CE Structure Dissect builder (<c>ue5_dissect.lua</c>) embedded in the UE5DumpUI
/// assembly through the <c>&lt;EmbeddedResource&gt;</c> link in <c>UE5DumpUI.csproj</c>, the same way the invoke and
/// freeze helpers ship. The single source stays <c>scripts/ue5_dissect.lua</c>.
/// <para>Unlike those two helpers, this chunk RETURNS its API table instead of defining globals, which is why the
/// record that loads it (<see cref="DissectScriptGenerator"/>) has to keep the return value.</para>
/// </summary>
public static class DissectLuaResource
{
    /// <summary>Logical resource name; must match the <c>&lt;LogicalName&gt;</c> in <c>UE5DumpUI.csproj</c>.</summary>
    private const string ResourceName = "UE5DumpUI.Resources.CE.ue5_dissect.lua";

    /// <summary>The name the file gets inside the CE table, and the one the enable record looks up with
    /// <c>findTableFile</c>. Kept equal to <see cref="DissectScriptGenerator.TableFileName"/>.</summary>
    public const string DefaultFileName = DissectScriptGenerator.TableFileName;

    /// <summary>Read the embedded script as a UTF-8 string, folded to LF.</summary>
    /// <exception cref="InvalidOperationException">The resource is missing: a packaging bug (the
    /// <c>EmbeddedResource</c> link was lost, or its LogicalName changed without <see cref="ResourceName"/>).</exception>
    public static string Read()
    {
        var asm = typeof(DissectLuaResource).Assembly;
        using var stream = asm.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException(
                $"Embedded resource '{ResourceName}' not found in {asm.GetName().Name}. Check UE5DumpUI.csproj " +
                "<EmbeddedResource> link to scripts/ue5_dissect.lua.");
        using var reader = new StreamReader(stream);
        // CE stores table files LF-normalised and the plugin's post-write size check compares against that; the
        // checkout's line endings are not ours to trust. [FREEZEINJECT-CRLF-2026-08-20]
        return CeLuaHygiene.NormalizeTableFilePayload(reader.ReadToEnd());
    }
}
