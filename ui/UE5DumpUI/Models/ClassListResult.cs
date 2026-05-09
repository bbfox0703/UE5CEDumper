namespace UE5DumpUI.Models;

/// <summary>
/// A single UClass entry from the list_classes command.
/// </summary>
public class GameClassEntry
{
    public string ClassName { get; set; } = "";
    public string ClassAddr { get; set; } = "";
    public string ClassPath { get; set; } = "";
    public string SuperName { get; set; } = "";
    public int PropertyCount { get; set; }
    public int PropertiesSize { get; set; }
    public int Score { get; set; }

    /// <summary>Display-friendly properties size as hex.</summary>
    public string SizeHex => $"0x{PropertiesSize:X}";

    /// <summary>
    /// Package prefix derived from <see cref="ClassPath"/> — the leading
    /// `/Script/Engine` / `/Game` / `/Script/ES2` portion that the
    /// "Package:" filter matches against. Pre-computed once so the
    /// DataGrid binding doesn't recompute per repaint and so the filter
    /// label aligns with a visible column.
    /// </summary>
    public string Package => ExtractPackagePrefix(ClassPath);

    /// <summary>
    /// Extract the package prefix from a class path.
    /// e.g. "/Script/Engine.Actor" -> "/Script/Engine"
    ///      "/Game/BP_Player.BP_Player_C" -> "/Game"
    /// Takes everything up to (but not including) the third '/' or the
    /// first '.', whichever comes first.
    /// </summary>
    private static string ExtractPackagePrefix(string classPath)
    {
        if (string.IsNullOrEmpty(classPath)) return "";

        // Strip everything after the first dot (package.class)
        int dotIdx = classPath.IndexOf('.');
        string pkg = dotIdx >= 0 ? classPath[..dotIdx] : classPath;

        // Take first 2 segments: e.g. "/Script/Engine" from
        // "/Script/Engine" or "/Game" from "/Game/Maps/Level1"
        int slashCount = 0;
        for (int i = 0; i < pkg.Length; i++)
        {
            if (pkg[i] == '/')
            {
                slashCount++;
                if (slashCount == 3) return pkg[..i];
            }
        }
        return pkg;
    }
}

/// <summary>
/// Result set from the list_classes command.
/// </summary>
public class ClassListResult
{
    public int Total { get; set; }
    public int ScannedObjects { get; set; }
    public int TotalClasses { get; set; }
    public List<GameClassEntry> Classes { get; set; } = new();
}
