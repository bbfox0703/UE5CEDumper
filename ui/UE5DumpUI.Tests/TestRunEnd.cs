using Xunit;

[assembly: AssemblyFixture(typeof(UE5DumpUI.Tests.TestRunEnd))]

namespace UE5DumpUI.Tests;

/// <summary>
/// [TEST-TEMP-HYGIENE-2026-11-11] Ends the test run (<see cref="TestTempRoot.EndRun"/>) once every test in the assembly
/// has run, inside the test session.
///
/// Not at process exit: xunit.v3's Microsoft.Testing.Platform bridge forces exit code 1 ("Foreground threads were left
/// running") when the process is still alive about 11 s after the session ends, whatever keeps it alive -- a process-exit
/// handler included. Removing the run's folder there took longer than that while another process held the SSD at 100%
/// I/O (measured 2026-11-11: build.ps1 ended FAILED with every test passing; a 15-s delay there reproduces it). In here
/// the removal only makes the session that much longer.
/// </summary>
public sealed class TestRunEnd : IDisposable
{
    public void Dispose() => TestTempRoot.EndRun();
}
