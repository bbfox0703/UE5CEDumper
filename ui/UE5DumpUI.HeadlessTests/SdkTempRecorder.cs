using System.Runtime.CompilerServices;
using UE5DumpUI.Tests;

namespace UE5DumpUI.HeadlessTests;

/// <summary>
/// [TEST-TEMP-HYGIENE-2026-11-11] Records the SDK leftovers this suite's own `dotnet test` just made, so the unit
/// tests' next run can tell them as ours a day later (SdkTempLeftovers). build.ps1 runs this suite after the unit
/// tests, which therefore never see this build young. It records and deletes nothing; %TEMP% is not moved here, since
/// this suite writes no scratch of its own there.
/// </summary>
internal static class SdkTempRecorder
{
    [ModuleInitializer]
    internal static void Record()
    {
        var temp = Path.GetTempPath();
        SdkTempLeftovers.RecordYoungGroups(temp, DateTime.UtcNow, Path.Combine(temp, SdkTempLeftovers.SharedFolderName));
    }
}
