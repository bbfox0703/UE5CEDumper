using UE5DumpUI.Models;

namespace UE5DumpUI.Services;

/// <summary>[DUMPDIFF-UI] Placeholder until the port lands.</summary>
public static class DumpDiffService
{
    public static Task<DumpDiffInput> LoadAsync(string path, IProgress<long>? linesRead = null,
        CancellationToken ct = default) => Task.FromResult(new DumpDiffInput { FilePath = path });

    public static DumpDiffResult Diff(DumpDiffInput oldDump, DumpDiffInput newDump, bool includeEngine) =>
        new() { OldDump = oldDump, NewDump = newDump, IncludeEngine = includeEngine };

    internal static string NormalizePath(string? path) => path ?? "";
    internal static HashSet<string> FailedNames(DumpDiffInput d) => new();
    internal static DumpDiffCounts CountChanges(List<DumpDiffTypeChange> changed) => new();
    internal static bool ParamsDiffer(DumpDiffFunc? a, DumpDiffFunc? b) => false;
    internal static string FormatPropType(DumpDiffProp p) => "";
    internal static string FormatParams(DumpDiffFunc f) => "";
}
