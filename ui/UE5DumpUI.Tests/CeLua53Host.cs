using System.Diagnostics;
using System.IO;
using System.Text;

namespace UE5DumpUI.Tests;

/// <summary>
/// Runs a Lua chunk on Cheat Engine's OWN Lua VM: out/ce_lua53/lua53ce.exe, the stock lua.c linked against the
/// installed CE's lua53-64.dll (built by <c>py tools/verify/ce_lua53_host.py</c>, gitignored). A test that needs it
/// calls <see cref="RequireHost"/> first, which SKIPS -- never fails -- on a machine without the host (CI, a fresh
/// clone): the host needs a local CE install, and a test must not reach outside the repo for it.
/// Why not a stock interpreter: CE's VM is 5.3 without the 5.1/5.2 compat functions, and string arithmetic yields
/// floats there -- a script can pass on 5.4 and fail in CE (ce_lua53_host.py's header has the measurements).
/// </summary>
internal static class CeLua53Host
{
    public static string? HostPath()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !File.Exists(Path.Combine(d.FullName, "build.ps1"))) d = d.Parent;
        if (d == null) return null;
        string exe = Path.Combine(d.FullName, "out", "ce_lua53", "lua53ce.exe");
        return File.Exists(exe) ? exe : null;
    }

    /// <summary>The host's path, or a skip naming how to build it.</summary>
    public static string RequireHost()
    {
        string? h = HostPath();
        if (h == null)
            Xunit.Assert.Skip("CE's Lua host is not built here (py tools/verify/ce_lua53_host.py); it needs a local CE install.");
        return h!;
    }

    /// <summary>Run <paramref name="lua"/> (written as UTF-8 bytes, as CE reads a script) and return the exit code and
    /// the combined output. <paramref name="rawBytes"/>, when given, is written instead: a script that must carry
    /// non-UTF-8 bytes literally.</summary>
    public static (int Exit, string Output) Run(string lua, byte[]? rawBytes = null)
    {
        string host = RequireHost();
        string file = Path.Combine(Path.GetTempPath(), "ue5-celua-" + Guid.NewGuid().ToString("N") + ".lua");
        File.WriteAllBytes(file, rawBytes ?? new UTF8Encoding(false).GetBytes(lua));
        try
        {
            var psi = new ProcessStartInfo(host)
            {
                RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false,
                CreateNoWindow = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
            };
            psi.ArgumentList.Add(file);
            using var p = Process.Start(psi)!;
            string output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
            if (!p.WaitForExit(30_000)) { try { p.Kill(); } catch { /* best effort */ } return (-1, output + "\n[timeout]"); }
            return (p.ExitCode, output);
        }
        finally { try { File.Delete(file); } catch { /* best effort */ } }
    }
}
