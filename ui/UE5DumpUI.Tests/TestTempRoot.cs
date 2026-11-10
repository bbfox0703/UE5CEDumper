namespace UE5DumpUI.Tests;

/// <summary>
/// [TEST-TEMP-HYGIENE-2026-11-11] Red stub: the API the tests pin, doing nothing yet.
/// </summary>
internal static class TestTempRoot
{
    internal const string RootName = "UE5CEDumper.Tests";
    internal const string OwnerFileName = ".owner";
    internal static readonly TimeSpan OwnerlessMinAge = TimeSpan.FromMinutes(10);
    internal static readonly TimeSpan LegacyMinAge = TimeSpan.FromHours(1);

    internal static string SystemTemp { get; private set; } = Path.GetTempPath();
    internal static string RunDir { get; private set; } = "";

    internal static List<string> SweepDeadRuns(string root, DateTime nowUtc) => new();
    internal static List<string> SweepLegacy(string temp, DateTime nowUtc) => new();
}
