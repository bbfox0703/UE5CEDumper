using System.Text;
using UE5DumpUI.Services;

namespace UE5DumpUI.Mcp;

/// <summary>
/// [PIPE-PER-PROCESS-2026-11-06] Stage D. <c>UE5DumpUI.exe --mcp</c>: the MCP server instead of the window.
///
/// Program.Main branches here before the single-instance mutex and before Avalonia, as it does for
/// <c>--inject-elevated</c>, so a server and the UI run side by side, and so do several servers (each AI session
/// starts its own). An MCP client starts the exe with stdin / stdout redirected to pipes; the exe is a GUI-subsystem
/// program, which opens no console, and its standard handles are the ones the client passed.
///
/// What it shares with a running UI: nothing it writes. It has its own pipe connection to the game (the DLL serves
/// 6), its own log file, and it never touches the UI's settings, bookmarks or logs.
/// </summary>
internal static class McpHost
{
    /// <summary>Reads the command line: --mcp once, anywhere, and at most one --writes=any-host or --writes=off. Any
    /// other argument stops the server before it serves, so a typo never serves under a policy the user did not write.
    /// </summary>
    internal static bool TryParseArgs(string[] args, out McpWriteMode mode, out string? error)
    {
        mode = McpWriteMode.Default;
        error = null;
        bool sawMcp = false, sawWrites = false;
        foreach (string a in args)
        {
            if (a == Constants.McpSwitch && !sawMcp)
            {
                sawMcp = true;
                continue;
            }
            if (a.StartsWith(Constants.McpWritesArgPrefix, StringComparison.Ordinal) && !sawWrites)
            {
                string value = a[Constants.McpWritesArgPrefix.Length..];
                if (value == Constants.McpWritesAnyHost) mode = McpWriteMode.AnyHost;
                else if (value == Constants.McpWritesOff) mode = McpWriteMode.Off;
                else return Refuse("an unknown value in '" + a + "'", out mode, out error);
                sawWrites = true;
                continue;
            }
            return Refuse("unexpected argument '" + a + "'", out mode, out error);
        }
        return sawMcp || Refuse("no " + Constants.McpSwitch, out mode, out error);
    }

    private static bool Refuse(string what, out McpWriteMode mode, out string? error)
    {
        mode = McpWriteMode.Default;
        error = Usage(what);
        return false;
    }

    private static string Usage(string what)
        => "UE5DumpUI " + Constants.McpSwitch + ": " + what + ". It takes " + Constants.McpSwitch + " once and at most "
           + "one of " + Constants.McpWritesArgPrefix + Constants.McpWritesAnyHost + " or "
           + Constants.McpWritesArgPrefix + Constants.McpWritesOff + ".";

    public static int Run(string[] args)
    {
        // stdout carries JSON-RPC and nothing else. Anything that writes Console.Out by accident lands in stderr,
        // which the client keeps as a log and never parses.
        Stream stdout = Console.OpenStandardOutput();
        Stream stdin = Console.OpenStandardInput();
        Console.SetOut(new StreamWriter(Console.OpenStandardError(), new UTF8Encoding(false)) { AutoFlush = true });

        using var platform = new WindowsPlatformService();
        using var log = new McpLog(platform.GetLogDirectoryPath());
        string version = typeof(McpHost).Assembly.GetName().Version?.ToString() ?? Constants.AppVersion;
        if (!TryParseArgs(args, out var mode, out string? error))
        {
            // stderr is the client's log of this server; stdout stays empty, so no client mistakes this for a reply.
            Console.Error.WriteLine(error);
            log.Error(Constants.LogCatInit, "[MCP] " + error);
            return 1;
        }
        log.Info(Constants.LogCatInit,
            $"[MCP] UE5DumpUI {version} serving MCP on stdio, pid {Environment.ProcessId}, writes {mode}.");
        try
        {
            using var pipe = new PipeClient(log, "M");
            // One decision and one grant table for the server, which decides at initialize, and the link, whose
            // gate checks every send against them.
            var grants = new McpGrants(mode, log);
            using var game = new McpGameLink(pipe, platform, log, grants);
            // The UI's experimental opt-in binds the AI too (Stage D2): read on every call, so the UI's change counts at
            // once.
            var server = new McpServer(McpTools.Create(game, grants, () => new ExperimentalGate(platform).IsEnabled),
                log, stdout, version, grants);
            int code = server.RunAsync(stdin).GetAwaiter().GetResult();
            try { pipe.DisconnectAsync().GetAwaiter().GetResult(); } catch (Exception) { /* exiting anyway */ }
            return code;
        }
        catch (Exception e)
        {
            // Never the UI's crash.log: a server dying must not overwrite the report of a UI crash.
            log.Error(Constants.LogCatInit, "[MCP] Server failed", e);
            try { Console.Error.WriteLine("UE5DumpUI --mcp failed: " + e.Message); } catch (Exception) { }
            return 1;
        }
    }
}
