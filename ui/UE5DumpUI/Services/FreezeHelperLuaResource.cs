using System.IO;

namespace UE5DumpUI.Services;

/// <summary>
/// Reads the CE Lua property-freeze helper (<c>ue5_freeze_helper.lua</c>)
/// embedded in the UE5DumpUI assembly via the <c>&lt;EmbeddedResource&gt;</c>
/// link in <c>UE5DumpUI.csproj</c>.
///
/// Sister to <see cref="HelperLuaResource"/> (which reads the invoke
/// helper). Both share the same pattern: single source of truth lives
/// under <c>scripts/</c> in the repo, .csproj links them in as embedded
/// resources with a stable LogicalName.
///
/// Consumers:
///   * <see cref="ViewModels.MainWindowViewModel"/> -- Tools menu inject /
///     export commands.
///   * Tests -- assert the helper resource is reachable from the
///     assembly manifest.
/// </summary>
public static class FreezeHelperLuaResource
{
    /// <summary>Logical resource name -- must match the
    /// <c>&lt;LogicalName&gt;</c> in <c>UE5DumpUI.csproj</c>.</summary>
    private const string ResourceName = "UE5DumpUI.Resources.CE.ue5_freeze_helper.lua";

    /// <summary>Default file name suggested in the SaveFileDialog AND
    /// the name the generated AA Script looks up via <c>findTableFile</c>.
    /// Keep these in lockstep with <see cref="FreezeScriptGenerator.HelperFileName"/>.</summary>
    public const string DefaultFileName = "ue5_freeze_helper.lua";

    /// <summary>
    /// Read the embedded helper as a UTF-8 string.
    /// </summary>
    /// <exception cref="InvalidOperationException">Resource is missing
    /// from the assembly. Indicates a packaging bug -- the
    /// <c>EmbeddedResource</c> link was lost or the LogicalName was
    /// changed without updating <see cref="ResourceName"/>.</exception>
    public static string Read()
    {
        var asm = typeof(FreezeHelperLuaResource).Assembly;
        using var stream = asm.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException(
                $"Embedded resource '{ResourceName}' not found in " +
                $"{asm.GetName().Name}. Check UE5DumpUI.csproj " +
                "<EmbeddedResource> link to scripts/ue5_freeze_helper.lua.");
        using var reader = new StreamReader(stream);
        // Fold to LF: CE stores table files LF-normalised, and the post-write size
        // check compares against that. See CeLuaHygiene.NormalizeTableFilePayload
        // -- the working tree's line endings are a property of the checkout, so this
        // cannot be left to the .lua file. [FREEZEINJECT-CRLF-2026-08-20]
        return CeLuaHygiene.NormalizeTableFilePayload(reader.ReadToEnd());
    }
}
