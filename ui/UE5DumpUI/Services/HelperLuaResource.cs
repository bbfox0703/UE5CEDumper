using System.IO;
using System.Reflection;

namespace UE5DumpUI.Services;

/// <summary>
/// Reads the CE Lua helper (<c>ue5_invoke_helper.lua</c>) that ships
/// embedded in the UE5DumpUI assembly via the
/// <c>&lt;EmbeddedResource&gt;</c> link in <c>UE5DumpUI.csproj</c>.
///
/// The resource has a stable LogicalName so the lookup works regardless
/// of the physical source path (file lives under <c>scripts/</c> in the
/// repo, not <c>Resources/</c>).
///
/// Called by <see cref="ViewModels.MainWindowViewModel.ExportCeHelperLuaAsync"/>
/// from the Tools menu so users can drop the helper next to their .CT
/// and add it via <c>Table -&gt; Add File...</c>.
/// </summary>
public static class HelperLuaResource
{
    /// <summary>Logical resource name -- must match the
    /// <c>&lt;LogicalName&gt;</c> in <c>UE5DumpUI.csproj</c>.</summary>
    private const string ResourceName = "UE5DumpUI.Resources.CE.ue5_invoke_helper.lua";

    /// <summary>Default file name suggested in the SaveFileDialog.</summary>
    public const string DefaultFileName = "ue5_invoke_helper.lua";

    /// <summary>
    /// Read the embedded helper as a UTF-8 string.
    /// </summary>
    /// <exception cref="InvalidOperationException">Resource is missing
    /// from the assembly. Indicates a packaging bug -- the
    /// <c>EmbeddedResource</c> link was lost or the LogicalName was
    /// changed without updating <see cref="ResourceName"/>.</exception>
    public static string Read()
    {
        var asm = typeof(HelperLuaResource).Assembly;
        using var stream = asm.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException(
                $"Embedded resource '{ResourceName}' not found in " +
                $"{asm.GetName().Name}. Check UE5DumpUI.csproj " +
                "<EmbeddedResource> link to scripts/ue5_invoke_helper.lua.");
        using var reader = new StreamReader(stream);
        // Fold to LF: CE stores table files LF-normalised, and the post-write size
        // check compares against that. See CeLuaHygiene.NormalizeTableFilePayload
        // -- the working tree's line endings are a property of the checkout, so this
        // cannot be left to the .lua file. [FREEZEINJECT-CRLF-2026-08-20]
        return CeLuaHygiene.NormalizeTableFilePayload(reader.ReadToEnd());
    }

    /// <summary>
    /// Diagnostic: list all embedded resource names. Useful when the
    /// runtime can't find the helper -- the name in the manifest may
    /// have drifted from <see cref="ResourceName"/>.
    /// </summary>
    public static string[] ListEmbeddedNames()
        => typeof(HelperLuaResource).Assembly.GetManifestResourceNames();
}
