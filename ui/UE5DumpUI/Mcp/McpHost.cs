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
    public static int Run()
    {
        // stdout carries JSON-RPC and nothing else. Anything that writes Console.Out by accident lands in stderr,
        // which the client keeps as a log and never parses.
        Stream stdout = Console.OpenStandardOutput();
        Stream stdin = Console.OpenStandardInput();
        Console.SetOut(new StreamWriter(Console.OpenStandardError(), new UTF8Encoding(false)) { AutoFlush = true });

        using var platform = new WindowsPlatformService();
        using var log = new McpLog(platform.GetLogDirectoryPath());
        string version = typeof(McpHost).Assembly.GetName().Version?.ToString() ?? Constants.AppVersion;
        log.Info(Constants.LogCatInit, $"[MCP] UE5DumpUI {version} serving MCP on stdio, pid {Environment.ProcessId}.");
        try
        {
            using var pipe = new PipeClient(log, "M");
            using var game = new McpGameLink(pipe, platform, log);
            var server = new McpServer(McpTools.Create(game), log, stdout, version);
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
