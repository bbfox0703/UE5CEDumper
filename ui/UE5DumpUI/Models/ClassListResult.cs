using CommunityToolkit.Mvvm.ComponentModel;

namespace UE5DumpUI.Models;

/// <summary>
/// A single UClass entry from the list_classes command.
/// </summary>
public partial class GameClassEntry : ObservableObject
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

    /// <summary>Batch "Find Func" result: which UFunctions take this class as a
    /// parameter/return. Format "N · func1, func2[, …]" / "0" / "—" / "" (not run).</summary>
    [ObservableProperty] private string _xrefInfo = "";

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

    /// <summary>
    /// True when the DLL stopped walking GObjects at <see cref="RequestedLimit"/>
    /// instead of reaching the end — this list is a PAGE, not the pool.
    /// </summary>
    public bool Truncated { get; set; }

    /// <summary>The row cap this result was requested with — named in the
    /// "capped" message so the user knows which number to raise.</summary>
    public int RequestedLimit { get; set; }

    public List<GameClassEntry> Classes { get; set; } = new();

    /// <summary>
    /// Exact-name class-address lookup that reports WHICH KIND of miss a miss was.
    ///
    /// Why this is not a bare <c>FirstOrDefault</c>: list_classes returns at most
    /// <see cref="RequestedLimit"/> rows and the DLL stops the moment it has them, so
    /// on a large title a perfectly real class simply is not in the page. Every caller
    /// used to render that as "Class X not found", which reads as "X does not exist" —
    /// the exact conclusion that costs a user the afternoon (audit #5 X2).
    /// </summary>
    public ClassAddrLookup FindClassAddr(string className)
    {
        if (string.IsNullOrEmpty(className))
            return new ClassAddrLookup("", Truncated, RequestedLimit);

        foreach (var c in Classes)
        {
            if (!c.ClassName.Equals(className, StringComparison.Ordinal)) continue;
            if (string.IsNullOrEmpty(c.ClassAddr)) continue;
            return new ClassAddrLookup(c.ClassAddr, Truncated, RequestedLimit);
        }
        return new ClassAddrLookup("", Truncated, RequestedLimit);
    }
}

/// <summary>
/// Outcome of <see cref="ClassListResult.FindClassAddr"/>. A hit carries the address;
/// a miss carries enough context to say honestly whether the class is absent or merely
/// past the cap.
/// </summary>
public readonly record struct ClassAddrLookup(string Addr, bool Truncated, int Limit)
{
    public bool Found => !string.IsNullOrEmpty(Addr);

    /// <summary>
    /// Predicate half of a miss message — meaningless on a hit. Caller renders it as
    /// <c>$"Class {name} {MissReason}"</c> so the two shapes stay one sentence apart.
    /// </summary>
    public string MissReason => Truncated
        ? $"not in the class list — it was CAPPED at {Limit:N0} rows, so the class may still exist"
        : "not found";
}
