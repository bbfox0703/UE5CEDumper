namespace UE5DumpUI.Tests;

/// <summary>
/// [TEST-TEMP-HYGIENE-2026-11-11] Red stub: the API the tests pin, deleting nothing yet.
/// </summary>
internal static class SdkTempLeftovers
{
    internal static readonly TimeSpan MinAge = TimeSpan.FromDays(1);
    internal static readonly TimeSpan FolderBeforeLog = TimeSpan.FromSeconds(2);
    internal static readonly TimeSpan FolderAfterLog = TimeSpan.FromSeconds(1);

    internal sealed record Plan(IReadOnlyList<string> Folders, IReadOnlyList<string> Logs);

    internal static Plan Find(string temp, string repoRoot, DateTime nowUtc) => new(new List<string>(), new List<string>());

    internal static Plan Sweep(string temp, string repoRoot, DateTime nowUtc) => new(new List<string>(), new List<string>());
}
