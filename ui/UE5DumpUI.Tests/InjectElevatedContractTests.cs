using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// [AV-INJECT-2026-11-11] tools/verify/inject.py hands every injection to <c>UE5DumpUI.exe --inject-elevated</c>: the
/// AV flagged python.exe starting the remote thread itself, and an AV exception follows the image of the process that
/// acts. Nothing pinned that mode before, so a change to its switch, argument order, result line or exit codes would
/// have broken every rig's injection with every test green. It is pinned here from both ends, and in behaviour where
/// that needs no process: a bad pid and a missing DLL are refused before any process is opened.
/// </summary>
public sealed class InjectElevatedContractTests
{
    private static int RunElevatedInject(string pid, string dll, string resultFile)
    {
        var method = typeof(Program).GetMethod("RunElevatedInject", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        return (int)method.Invoke(null, [pid, dll, resultFile])!;
    }

    private static string ProgramSource() => File.ReadAllText(NumericInputCoercionTests.RepoFile("ui/UE5DumpUI/Program.cs"));

    private static string InjectPy() => File.ReadAllText(NumericInputCoercionTests.RepoFile("tools/verify/inject.py"));

    [Fact]
    public void ABadPid_Exits2_AndTheResultFileSaysSo()
    {
        string result = Path.GetTempFileName();
        try
        {
            Assert.Equal(2, RunElevatedInject("not-a-pid", "x.dll", result));
            Assert.Equal("Invalid PID.", File.ReadAllText(result));
        }
        finally { File.Delete(result); }
    }

    [Fact]
    public void AMissingDll_Exits1_WithTheReason()
    {
        // Pid 0: if the DLL check ever moved after OpenProcess, that call fails harmlessly instead of reaching a
        // real process, and the message below no longer matches.
        string result = Path.GetTempFileName();
        string dll = Path.Combine(Path.GetTempPath(), "ue5inject-missing-" + Guid.NewGuid().ToString("N") + ".dll");
        try
        {
            Assert.Equal(1, RunElevatedInject("0", dll, result));
            Assert.Equal("DLL not found: " + dll, File.ReadAllText(result));
        }
        finally { File.Delete(result); }
    }

    [Fact]
    public void TheSwitch_TheArgumentOrder_AndTheSuccessLine_AreWhatInjectPyReads()
    {
        string program = ProgramSource();
        Assert.Contains("args.Length >= 4 && args[0] == \"--inject-elevated\"", program);
        Assert.Contains("RunElevatedInject(args[1], args[2], args[3])", program);
        Assert.Contains("result.Ok ? $\"OK {result.HModule:X}\"", program);
        Assert.Contains("return result.Ok ? 0 : 1;", program);

        string py = InjectPy();
        Assert.Contains("[exe_path, \"--inject-elevated\", str(pid), dll_path, result_path]", py);
        Assert.Contains("proc.returncode == 0 and text.startswith(\"OK \")", py);
        Assert.Contains("int(text[3:], 16)", py);
        Assert.Contains("encoding=\"utf-8-sig\"", py);   // the exe's texts carry an em dash
        Assert.Contains("dll_path = os.path.abspath(dll_path)", py);   // the exe hands the path over verbatim
    }

    [Fact]
    public void InjectPy_NeverInjectsFromPythonItself_AndNeverElevates()
    {
        // The hand-off is the point: python.exe must not be the process that writes into the target or starts a
        // thread in it. Elevation is never automatic (handover.md section 4): an elevated game needs the run elevated.
        string py = InjectPy();
        foreach (string api in new[]
                 {
                     "CreateRemoteThread", "WriteProcessMemory", "VirtualAllocEx", "NtCreateThreadEx",
                     "QueueUserAPC", "OpenProcess",
                 })
            Assert.DoesNotMatch(new Regex(@"\bk32\." + api + @"\b"), py);
        Assert.DoesNotContain("runas", py, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ShellExecute", py, StringComparison.Ordinal);
    }

    [Fact]
    public void InjectPy_ResultFileName_IsUnique_AndRemoved()
    {
        string py = InjectPy();
        Assert.Contains("tempfile.mkstemp(", py);
        Assert.Contains("os.remove(result_path)", py);
        Assert.Matches(new Regex(@"EXE_TIMEOUT_S = (\d+)"), py);
        int timeout = int.Parse(Regex.Match(py, @"EXE_TIMEOUT_S = (\d+)").Groups[1].Value, CultureInfo.InvariantCulture);
        // Above the exe's own 10 s wait for the remote LoadLibraryW, so a slow load reports its own reason.
        Assert.True(timeout > 10, $"EXE_TIMEOUT_S = {timeout}");
        Assert.Matches(new Regex(@"InjectTimeoutMs\s*=\s*10000;"), File.ReadAllText(
            NumericInputCoercionTests.RepoFile("ui/UE5DumpUI/Services/WindowsPlatformService.cs")));
    }
}
