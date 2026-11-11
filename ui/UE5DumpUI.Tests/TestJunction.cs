namespace UE5DumpUI.Tests;

/// <summary>A directory junction for a test, made as VolumeRootTests makes its own: `mklink /J` is unprivileged, unlike a
/// symlink. True only when the link exists and is a reparse point.</summary>
internal static class TestJunction
{
    internal static bool Make(string link, string target)
    {
        var psi = new System.Diagnostics.ProcessStartInfo("cmd.exe")
        {
            Arguments = $"/c mklink /J \"{link}\" \"{target}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        using var p = System.Diagnostics.Process.Start(psi);
        if (p is null) return false;
        p.WaitForExit(20_000);
        return p.HasExited && p.ExitCode == 0
            && File.GetAttributes(link).HasFlag(FileAttributes.ReparsePoint);
    }
}
