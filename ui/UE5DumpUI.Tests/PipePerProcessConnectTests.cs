using System.IO.Pipes;
using System.Text.Json.Nodes;
using UE5DumpUI.Core;
using UE5DumpUI.Models;
using UE5DumpUI.Services;
using UE5DumpUI.ViewModels;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// [PIPE-PER-PROCESS-2026-11-06] Each game serves \\.\pipe\UE5DumpBfx_&lt;pid&gt;, and Connect names the game: at once
/// when one game serves, through the game list when several do (or one beside an old DLL's legacy pipe), and never to
/// the legacy pipe -- alone, it is a refusal with an explanation, not a list. A game the UI injected is dialled by pid.
/// After connecting, the pid the DLL reports must be the game's.
/// </summary>
public sealed class PipePerProcessConnectTests
{
    // ── PipeTargets: the name, and what Connect makes of a listing ──────────────────────────────────────────────

    [Fact]
    public void The_name_is_the_prefix_and_the_decimal_pid()
    {
        Assert.Equal("UE5DumpBfx_1234", PipeTargets.NameFor(1234));
        Assert.Equal("UE5DumpBfx_7", PipeTargets.NameFor(7));
    }

    [Theory]
    [InlineData("UE5DumpBfx_1234", 1234)]
    [InlineData("ue5dumpbfx_7", 7)]          // Windows compares pipe names without case
    [InlineData("UE5DumpBfx_2147483647", int.MaxValue)]
    public void A_game_pipe_names_its_pid(string name, int pid)
    {
        Assert.True(PipeTargets.TryParsePid(name, out int got));
        Assert.Equal(pid, got);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("UE5DumpBfx")]               // the legacy name: no pid
    [InlineData("UE5DumpBfx_")]
    [InlineData("UE5DumpBfx_0")]             // no process has pid 0
    [InlineData("UE5DumpBfx_12a")]
    [InlineData("UE5DumpBfx_-5")]
    [InlineData("UE5DumpBfx_+5")]
    [InlineData("UE5DumpBfx_ 12")]
    [InlineData("UE5DumpBfx_１２")]          // full-width digits are not ASCII digits
    [InlineData("UE5DumpBfx_99999999999")]   // past int
    [InlineData("UE5DumpBfx-probe-1-ab")]    // pipe_remote_probe's own pipes
    [InlineData("xUE5DumpBfx_12")]
    public void Anything_else_is_not_a_game(string? name) => Assert.False(PipeTargets.TryParsePid(name, out _));

    [Fact]
    public void A_scan_keeps_each_game_once_in_pid_order_and_notes_the_legacy_pipe()
    {
        var scan = PipeTargets.Scan(new[]
        {
            "UE5DumpBfx_300", "AOBMakerCEBridge", "UE5DumpBfx_100", "ue5dumpbfx_300", "ue5dumpbfx", "UE5DumpBfx-probe-1-x",
        });
        Assert.Equal(new[] { 100, 300 }, scan.Pids);
        Assert.True(scan.LegacyPresent);
        Assert.False(PipeTargets.Scan(new[] { "UE5DumpBfx_1" }).LegacyPresent);
    }

    [Theory]
    [InlineData(new int[0], false, ConnectPlan.NoPipe)]
    [InlineData(new int[0], true, ConnectPlan.LegacyOnly)]
    [InlineData(new[] { 5 }, false, ConnectPlan.ConnectOne)]
    [InlineData(new[] { 5 }, true, ConnectPlan.Choose)]       // the user learns an old DLL runs somewhere
    [InlineData(new[] { 5, 6 }, false, ConnectPlan.Choose)]
    [InlineData(new[] { 5, 6 }, true, ConnectPlan.Choose)]
    public void The_plan_follows_the_listing(int[] pids, bool legacy, ConnectPlan plan)
        => Assert.Equal(plan, PipeTargets.Decide(new PipeScan(pids, legacy)));

    // ── Who holds a full pipe (Stage C): the DLL's at-capacity line ─────────────────────────────────────────────

    private const string FullLine668 =
        "[2026-11-10 20:47:40.123] [INFO] [PIPE:svr] PipeServer: all 6 pipe instances in use, waiting for a free slot -- "
        + "\\\\.\\pipe\\UE5DumpBfx_668 holders=[{\"seq\":3,\"pid\":4321,\"exe\":\"python.exe\",\"last_cmd\":\"get_pointers\","
        + "\"in_flight\":false},{\"seq\":4,\"pid\":77,\"exe\":\"UE5DumpUI.exe\",\"last_cmd\":\"\",\"in_flight\":true}]";

    [Fact]
    public void The_latest_full_line_for_the_pipe_names_its_holders()
    {
        var text = "[2026-11-10 20:00:00.000] [INFO] [PIPE:svr] PipeServer: all 6 pipe instances in use, waiting for a "
                   + "free slot -- \\\\.\\pipe\\UE5DumpBfx_668 holders=[{\"pid\":1,\"exe\":\"old.exe\",\"last_cmd\":\"\","
                   + "\"in_flight\":false}]\n"
                   + "[2026-11-10 20:01:00.000] [INFO] [PIPE:svr] PipeServer: a pipe slot freed, resuming accept -- "
                   + "\\\\.\\pipe\\UE5DumpBfx_668\n"
                   + FullLine668 + "\n";
        var seen = PipeHolders.ParseLatest(text, "UE5DumpBfx_668");
        Assert.NotNull(seen);
        Assert.Equal("2026-11-10 20:47:40.123", seen!.Time);
        Assert.Equal(new[] { new PipeHolder(4321, "python.exe", "get_pointers", false),
                             new PipeHolder(77, "UE5DumpUI.exe", "", true) }, seen.Holders);
        Assert.Equal(0, seen.More);
        Assert.False(seen.Superseded);
    }

    [Fact]
    public void A_full_line_freed_since_is_marked_superseded()
    {
        var text = FullLine668 + "\n[2026-11-10 20:48:00.000] [INFO] [PIPE:svr] PipeServer: a pipe slot freed, resuming "
                   + "accept -- \\\\.\\pipe\\UE5DumpBfx_668\n";
        Assert.True(PipeHolders.ParseLatest(text, "UE5DumpBfx_668")!.Superseded);
    }

    [Fact]
    public void Another_pipe_and_a_pid_that_merely_starts_the_same_are_not_read()
    {
        Assert.Null(PipeHolders.ParseLatest(FullLine668, "UE5DumpBfx_66"));
        Assert.Null(PipeHolders.ParseLatest(FullLine668, "UE5DumpBfx_6680"));
        Assert.NotNull(PipeHolders.ParseLatest(FullLine668, "UE5DumpBfx_668"));
    }

    [Fact]
    public void Holders_that_did_not_fit_are_counted_and_a_broken_line_is_no_answer()
    {
        var withMore = FullLine668 + " more=3";
        Assert.Equal(3, PipeHolders.ParseLatest(withMore, "UE5DumpBfx_668")!.More);
        Assert.Null(PipeHolders.ParseLatest(FullLine668[..^20], "UE5DumpBfx_668"));   // cut mid-JSON
        Assert.Null(PipeHolders.ParseLatest("", "UE5DumpBfx_668"));
        Assert.Null(PipeHolders.ParseLatest(null, "UE5DumpBfx_668"));
    }

    [Fact]
    public async Task A_full_pipe_names_who_holds_it_from_the_dlls_log()
    {
        var logRoot = Path.Combine(Path.GetTempPath(), "UE5DumpHolders_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(logRoot, "Game"));
        try
        {
            File.WriteAllText(Path.Combine(logRoot, "Game", "pipe-0.log"),
                FullLine668.Replace("UE5DumpBfx_668", "UE5DumpBfx_100") + "\n");
            var rig = new Rig("UE5DumpBfx_100");
            rig.Platform.LogRoot = logRoot;
            rig.Platform.Processes[100] = ("Game.exe", "Game");
            rig.Pipe.Busy = new HashSet<string> { "UE5DumpBfx_100" };
            await rig.Vm.ConnectCommand.ExecuteAsync(null);
            var msg = rig.Vm.ErrorMessage ?? "";
            Assert.StartsWith(Res.Format("str.Connect.Busy", 100), msg);
            Assert.Contains("20:47:40", msg);
            Assert.Contains("python.exe", msg);
            Assert.Contains("4321", msg);
            Assert.Contains("get_pointers", msg);
            Assert.Contains("UE5DumpUI.exe", msg);
        }
        finally
        {
            Directory.Delete(logRoot, recursive: true);
        }
    }

    // ── The main window's Connect ───────────────────────────────────────────────────────────────────────────────

    private sealed class Rig
    {
        public readonly PipePlatform Platform = new();
        public readonly RecordingPipe Pipe = new();
        public readonly PidDump Dump = new();
        public readonly MainWindowViewModel Vm;
        public readonly List<IReadOnlyList<PipeTarget>> PickerCalls = new();
        public readonly List<string> Notices = new();
        public Func<IReadOnlyList<PipeTarget>, int?> Pick = rows => null;

        public Rig(params string[] pipes)
        {
            Platform.Pipes = pipes;
            Vm = new MainWindowViewModel(Pipe, Dump, new MockLoggingService(), Platform);
            Vm.PickPipeTargetAsync = rows => { PickerCalls.Add(rows); return Task.FromResult(Pick(rows)); };
            Vm.ShowNoticeAsync = (title, _) => { Notices.Add(title); return Task.CompletedTask; };
        }
    }

    // ── [UI-ELEVATED-ACCESS-DENIED] Windows refusing the pipe ───────────────────────────────────────────────────────
    //
    // Measured 2026-11-12 (verification-register.md [PIPE-PER-PROCESS-2026-11-06] item 4): a non-elevated UI connecting to
    // a game that runs as administrator showed only "Access to the path is denied." -- the OS's words, naming neither
    // the game nor a way out. The DLL keeps the pipe's default security, under which an elevated creator's owner is
    // Administrators and everyone else may only read; an administrator is always let in.

    [Fact]
    public async Task A_game_running_as_administrator_is_named_as_such_to_a_UI_that_is_not()
    {
        var rig = new Rig("UE5DumpBfx_100");
        rig.Pipe.Refused = new() { "UE5DumpBfx_100" };
        await rig.Vm.ConnectCommand.ExecuteAsync(null);
        Assert.Equal(new[] { "UE5DumpBfx_100" }, rig.Pipe.Dialed);
        Assert.False(rig.Vm.IsConnected);
        Assert.Equal("Connection Error", rig.Vm.StatusText);
        Assert.Contains("administrator", rig.Vm.ErrorMessage);
        Assert.Contains("elevated", rig.Vm.ErrorMessage);
        Assert.DoesNotContain("Access to the path is denied", rig.Vm.ErrorMessage);
        // The toolbar shows about 25 characters before its ellipsis: the cause must be in them.
        Assert.InRange(rig.Vm.ErrorMessage!.IndexOf("administrator", StringComparison.Ordinal), 0, 30);
    }

    [Fact]
    public async Task An_elevated_game_picked_from_the_list_is_named_as_such()
    {
        var rig = new Rig("UE5DumpBfx_100", "UE5DumpBfx_200");
        rig.Pick = _ => 200;
        rig.Pipe.Refused = new() { "UE5DumpBfx_200" };
        await rig.Vm.ConnectCommand.ExecuteAsync(null);
        Assert.Contains("200", rig.Vm.ErrorMessage);
        Assert.Contains("administrator", rig.Vm.ErrorMessage);
        Assert.DoesNotContain("Access to the path is denied", rig.Vm.ErrorMessage);
    }

    [Fact]
    public async Task An_elevated_game_dialled_by_pid_is_named_as_such()
    {
        // Proxy Deploy's connect after an inject, and its "already active" connect, dial by pid.
        var rig = new Rig("UE5DumpBfx_55");
        rig.Pipe.Refused = new() { "UE5DumpBfx_55" };
        await rig.Vm.ConnectToAsync(55);
        Assert.Contains("55", rig.Vm.ErrorMessage);
        Assert.Contains("administrator", rig.Vm.ErrorMessage);
        Assert.DoesNotContain("Access to the path is denied", rig.Vm.ErrorMessage);
    }

    [Fact]
    public async Task A_refusal_to_a_UI_that_runs_as_administrator_does_not_blame_elevation()
    {
        // An administrator is always let into the DLL's pipe, so a refusal then means something else set its
        // permissions -- most likely another process that created the game's pipe name first.
        var rig = new Rig("UE5DumpBfx_100");
        rig.Platform.Elevated = true;
        rig.Pipe.Refused = new() { "UE5DumpBfx_100" };
        await rig.Vm.ConnectCommand.ExecuteAsync(null);
        Assert.Contains("100", rig.Vm.ErrorMessage);
        Assert.Contains("another process", rig.Vm.ErrorMessage);
        Assert.DoesNotContain("this UI does not", rig.Vm.ErrorMessage);
        Assert.DoesNotContain("Access to the path is denied", rig.Vm.ErrorMessage);
    }

    [Fact]
    public void An_access_refusal_blames_elevation_only_for_a_client_that_is_not_elevated()
    {
        var denied = new UnauthorizedAccessException("Access to the path is denied.");
        Assert.Equal(PipeDialRefusal.GameElevated, PipeTargets.ClassifyRefusal(denied, clientElevated: false));
        Assert.Equal(PipeDialRefusal.Denied, PipeTargets.ClassifyRefusal(denied, clientElevated: true));
        // Not refusals: a missing or full pipe, and anything else, keep their own handling.
        Assert.Equal(PipeDialRefusal.None, PipeTargets.ClassifyRefusal(new TimeoutException(), false));
        Assert.Equal(PipeDialRefusal.None, PipeTargets.ClassifyRefusal(new IOException("pipe"), false));
        Assert.Equal(PipeDialRefusal.None, PipeTargets.ClassifyRefusal(new InvalidOperationException(), true));
    }

    [Fact]
    public async Task A_refusal_is_remembered_for_the_post_inject_retry_and_forgotten_by_the_next_dial()
    {
        var rig = new Rig("UE5DumpBfx_55");
        rig.Pipe.Refused = new() { "UE5DumpBfx_55" };
        await rig.Vm.ConnectToAsync(55);
        Assert.Equal(rig.Vm.ErrorMessage, rig.Vm.LastConnectRefusal);
        rig.Pipe.Refused = null;
        rig.Dump.AnsweringPid = 55;
        await rig.Vm.ConnectToAsync(55);
        Assert.True(rig.Vm.IsConnected);
        Assert.Null(rig.Vm.LastConnectRefusal);
    }

    [Fact]
    public async Task One_game_connects_at_once()
    {
        var rig = new Rig("UE5DumpBfx_4242", "SomeOtherPipe");
        rig.Dump.AnsweringPid = 4242;
        await rig.Vm.ConnectCommand.ExecuteAsync(null);
        Assert.Equal(new[] { "UE5DumpBfx_4242" }, rig.Pipe.Dialed);
        Assert.Empty(rig.PickerCalls);
        Assert.True(rig.Vm.IsConnected);
    }

    [Fact]
    public async Task Only_the_legacy_pipe_is_refused_with_a_notice_and_no_list()
    {
        var rig = new Rig("UE5DumpBfx");
        await rig.Vm.ConnectCommand.ExecuteAsync(null);
        Assert.Empty(rig.Pipe.Dialed);
        Assert.Empty(rig.PickerCalls);
        Assert.Equal(new[] { Res.Get("str.Connect.LegacyTitle") }, rig.Notices);
        Assert.Equal(Res.Get("str.Connect.LegacyStatus"), rig.Vm.StatusText);
        Assert.False(rig.Vm.IsConnected);
    }

    [Fact]
    public async Task Several_games_open_the_list_with_their_names_and_dial_the_choice()
    {
        var rig = new Rig("UE5DumpBfx_300", "UE5DumpBfx_100");
        rig.Platform.Processes[100] = ("GameA-Win64-Shipping.exe", "Game A");
        rig.Platform.Processes[300] = ("GameB.exe", null);
        rig.Pick = _ => 300;
        rig.Dump.AnsweringPid = 300;
        await rig.Vm.ConnectCommand.ExecuteAsync(null);

        var rows = Assert.Single(rig.PickerCalls);
        Assert.Equal(new[] { 100, 300 }, rows.Select(r => r.Pid));
        Assert.Equal(new PipeTarget(100, "GameA-Win64-Shipping.exe", "Game A"), rows[0]);
        Assert.Equal(new PipeTarget(300, "GameB.exe", null), rows[1]);
        Assert.Equal(new[] { "UE5DumpBfx_300" }, rig.Pipe.Dialed);
        Assert.True(rig.Vm.IsConnected);
    }

    [Fact]
    public async Task A_game_beside_the_legacy_pipe_is_listed_with_it_and_the_legacy_row_cannot_be_chosen()
    {
        var rig = new Rig("UE5DumpBfx_100", "UE5DumpBfx");
        rig.Pick = rows => rows.Single(r => r.IsLegacy).Pid;   // a picker that let the legacy row through
        await rig.Vm.ConnectCommand.ExecuteAsync(null);

        var rows = Assert.Single(rig.PickerCalls);
        Assert.Equal(2, rows.Count);
        Assert.True(rows[^1].IsLegacy);
        Assert.Empty(rig.Pipe.Dialed);
        Assert.Empty(rig.Notices);
        Assert.Equal(Res.Get("str.Connect.Cancelled"), rig.Vm.StatusText);
    }

    [Fact]
    public async Task Cancelling_the_list_dials_nothing()
    {
        var rig = new Rig("UE5DumpBfx_1", "UE5DumpBfx_2");
        await rig.Vm.ConnectCommand.ExecuteAsync(null);
        Assert.Single(rig.PickerCalls);
        Assert.Empty(rig.Pipe.Dialed);
        Assert.Equal(Res.Get("str.Connect.Cancelled"), rig.Vm.StatusText);
    }

    [Fact]
    public async Task No_game_says_so_and_dials_nothing()
    {
        var rig = new Rig("SomeOtherPipe");
        await rig.Vm.ConnectCommand.ExecuteAsync(null);
        Assert.Empty(rig.Pipe.Dialed);
        Assert.Equal(Res.Get("str.Connect.NoPipe"), rig.Vm.ErrorMessage);
        Assert.False(rig.Vm.IsConnected);
    }

    [Fact]
    public async Task A_failed_listing_is_reported_not_read_as_no_game()
    {
        var rig = new Rig();
        rig.Platform.Pipes = null;
        await rig.Vm.ConnectCommand.ExecuteAsync(null);
        Assert.Empty(rig.Pipe.Dialed);
        Assert.Equal(Res.Get("str.Connect.ListFailed"), rig.Vm.ErrorMessage);
    }

    [Fact]
    public async Task A_game_the_UI_injected_is_dialled_by_pid_before_its_pipe_is_listed()
    {
        var rig = new Rig();   // the DLL is still scanning: its pipe is not up yet
        rig.Dump.AnsweringPid = 55;
        await rig.Vm.ConnectToAsync(55);
        Assert.Equal(new[] { "UE5DumpBfx_55" }, rig.Pipe.Dialed);
        Assert.Empty(rig.PickerCalls);
    }

    [Fact]
    public async Task A_named_game_whose_pipe_never_comes_beside_the_legacy_pipe_is_explained()
    {
        var rig = new Rig("UE5DumpBfx");   // perhaps this game runs the old DLL: its own pipe never appears
        rig.Pipe.Listening = new HashSet<string>();
        await rig.Vm.ConnectToAsync(55);
        Assert.Equal(new[] { "UE5DumpBfx_55" }, rig.Pipe.Dialed);   // dialled: the legacy pipe may be another game's
        Assert.Empty(rig.Notices);
        Assert.Equal(Res.Format("str.Connect.PidUnlistedBesideLegacy", 55), rig.Vm.ErrorMessage);
        Assert.False(rig.Vm.IsConnected);
    }

    [Fact]
    public async Task A_listed_game_whose_every_connection_is_taken_says_so()
    {
        // Stage B: a DLL from before it takes 3 clients and the UI uses 2, so one rig fills it. Still listed when the
        // connect times out: every connection is taken, or the game is not responding.
        var rig = new Rig("UE5DumpBfx_100");
        rig.Pipe.Busy = new HashSet<string> { "UE5DumpBfx_100" };
        await rig.Vm.ConnectCommand.ExecuteAsync(null);
        Assert.Equal(new[] { "UE5DumpBfx_100" }, rig.Pipe.Dialed);
        Assert.Equal(Res.Format("str.Connect.Busy", 100), rig.Vm.ErrorMessage);
        Assert.False(rig.Vm.IsConnected);
    }

    [Fact]
    public async Task A_game_that_exits_between_the_listing_and_the_dial_is_reported_gone_not_busy()
    {
        // The picker can stay open as long as the user likes; the game can exit meanwhile. .NET retries a missing pipe
        // until its timeout, so the connect times out exactly as it does on a busy one.
        var rig = new Rig("UE5DumpBfx_100", "UE5DumpBfx_200");
        rig.Pick = _ => 200;
        rig.Pipe.Listening = new HashSet<string>();
        rig.Pipe.OnDial = _ => rig.Platform.Pipes = new[] { "UE5DumpBfx_100" };   // 200 exited
        await rig.Vm.ConnectCommand.ExecuteAsync(null);
        Assert.Equal(Res.Format("str.Connect.PipeGone", 200), rig.Vm.ErrorMessage);
        Assert.False(rig.Vm.IsConnected);
    }

    [Fact]
    public async Task A_named_game_beside_another_games_legacy_pipe_is_still_dialled()
    {
        // Another game runs an old proxy; the named game runs the current DLL and opens its pipe while it is dialled.
        var rig = new Rig("UE5DumpBfx");
        rig.Dump.AnsweringPid = 55;
        await rig.Vm.ConnectToAsync(55);
        Assert.Equal(new[] { "UE5DumpBfx_55" }, rig.Pipe.Dialed);
        Assert.Empty(rig.Notices);
        Assert.True(rig.Vm.IsConnected);
    }

    [Fact]
    public async Task Connecting_to_another_game_while_connected_switches_to_it()
    {
        var rig = new Rig("UE5DumpBfx_100", "UE5DumpBfx_200");
        rig.Dump.AnswerFromConnection(rig.Pipe);
        rig.Pick = _ => 100;
        await rig.Vm.ConnectCommand.ExecuteAsync(null);
        Assert.True(rig.Vm.IsConnected);

        await rig.Vm.ConnectToAsync(200);   // Proxy Deploy: a game that already runs the DLL, picked to inject
        Assert.Equal(new[] { "UE5DumpBfx_100", "UE5DumpBfx_200" }, rig.Pipe.Dialed);
        Assert.Equal(1, rig.Pipe.Disconnects);   // the first game, once, before dialling the second
        Assert.Null(rig.Vm.ErrorMessage);
        Assert.True(rig.Vm.IsConnected);
    }

    [Fact]
    public async Task Connecting_to_the_connected_game_again_keeps_its_connection()
    {
        var rig = new Rig("UE5DumpBfx_100");
        rig.Dump.AnsweringPid = 100;
        await rig.Vm.ConnectCommand.ExecuteAsync(null);
        await rig.Vm.ConnectToAsync(100);
        Assert.Equal(0, rig.Pipe.Disconnects);
        Assert.True(rig.Vm.IsConnected);
        Assert.Null(rig.Vm.ErrorMessage);
    }

    [Fact]
    public async Task A_connect_by_pid_runs_as_the_Connect_command_which_holds_the_button()
    {
        var rig = new Rig();
        rig.Dump.AnsweringPid = 55;
        var gate = new TaskCompletionSource();
        rig.Pipe.Gate = gate.Task;
        var running = rig.Vm.ConnectCommand.ExecuteAsync(55);
        Assert.True(rig.Vm.ConnectCommand.IsRunning);
        Assert.False(((System.Windows.Input.ICommand)rig.Vm.ConnectCommand).CanExecute(null));   // the toolbar button
        gate.SetResult();
        await running;
        Assert.Equal(new[] { "UE5DumpBfx_55" }, rig.Pipe.Dialed);
    }

    [Fact]
    public void The_post_inject_connect_is_wired_through_the_Connect_command()
    {
        var vm = File.ReadAllText(NumericInputCoercionTests.RepoFile("ui/UE5DumpUI/ViewModels/MainWindowViewModel.cs"));
        Assert.Contains("ProxyDeploy.RequestConnectAsync = pid => ConnectCommand.ExecuteAsync(pid);", vm);
    }

    [Fact]
    public async Task The_post_inject_retry_is_not_refused_while_the_new_pipe_is_not_up_yet()
    {
        var rig = new Rig("UE5DumpBfx");
        rig.Vm.SuppressConnectErrors = true;
        rig.Dump.AnsweringPid = 55;
        await rig.Vm.ConnectToAsync(55);
        Assert.Equal(new[] { "UE5DumpBfx_55" }, rig.Pipe.Dialed);
        Assert.Empty(rig.Notices);
    }

    [Fact]
    public async Task Another_process_answering_for_the_game_is_disconnected()
    {
        var rig = new Rig("UE5DumpBfx_100");
        rig.Dump.AnsweringPid = 999;
        await rig.Vm.ConnectCommand.ExecuteAsync(null);
        Assert.Equal(new[] { "UE5DumpBfx_100" }, rig.Pipe.Dialed);
        Assert.Equal(1, rig.Pipe.Disconnects);
        Assert.False(rig.Vm.IsConnected);
        Assert.Equal(Res.Format("str.Connect.WrongPid", "UE5DumpBfx_100", 999, 100), rig.Vm.ErrorMessage);
    }

    // ── The real pieces, against real pipes ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void The_Windows_listing_finds_a_pipe_this_test_serves()
    {
        string name = "UE5DumpTest_" + Guid.NewGuid().ToString("N");
        using var server = new NamedPipeServerStream(name, PipeDirection.InOut, 1);
        using var windows = new WindowsPlatformService();
        IPlatformService platform = windows;   // through the interface: its default must not be what answers
        var names = platform.ListNamedPipes();
        Assert.NotNull(names);
        Assert.Contains(name, names!, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_Windows_description_names_this_process()
    {
        using var windows = new WindowsPlatformService();
        IPlatformService platform = windows;
        var (image, _) = platform.DescribeProcess(Environment.ProcessId);
        Assert.EndsWith(".exe", image, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(("", (string?)null), platform.DescribeProcess(int.MaxValue));
    }

    [Fact]
    public async Task Both_lanes_dial_the_one_name_they_are_given()
    {
        string name = "UE5DumpTest_" + Guid.NewGuid().ToString("N");
        // 64 KB buffers, as the DLL creates (Grimoire::PIPE_BUF_SIZE). With .NET's default of 0 the UTF-8 preamble the
        // client's writer sends on connect blocks until the server reads, and nothing here reads.
        using var a = new NamedPipeServerStream(name, PipeDirection.InOut, 2, PipeTransmissionMode.Byte,
                                                PipeOptions.Asynchronous, 65536, 65536);
        using var b = new NamedPipeServerStream(name, PipeDirection.InOut, 2, PipeTransmissionMode.Byte,
                                                PipeOptions.Asynchronous, 65536, 65536);
        var ct = TestContext.Current.CancellationToken;
        var accepted = Task.WhenAll(a.WaitForConnectionAsync(ct), b.WaitForConnectionAsync(ct));
        using var lanes = new LaneRoutingPipeClient(new MockLoggingService());
        await lanes.ConnectAsync(name, ct);
        await accepted.WaitAsync(TimeSpan.FromSeconds(10), ct);
        Assert.True(a.IsConnected && b.IsConnected);
        Assert.True(lanes.IsConnected);
        // The server ends the session, as the DLL does: each lane's read sees the pipe close.
        a.Dispose();
        b.Dispose();
        for (int i = 0; i < 100 && lanes.IsConnected; i++) await Task.Delay(50, ct);
        Assert.False(lanes.IsConnected);
    }

    [Fact]
    public async Task A_pipe_this_user_may_only_read_refuses_the_first_lane_with_UnauthorizedAccess()
    {
        // [UI-ELEVATED-ACCESS-DENIED] The type the main window's catch relies on, through the real stack: a pipe whose
        // DACL lets this user only read -- what everyone but Administrators gets on an elevated game's pipe -- refuses
        // the read/write open the lanes make. The interactive lane is dialled first, so its refusal is the one seen.
        string name = "UE5DumpRefuseTest_" + Guid.NewGuid().ToString("N");
        var sec = new PipeSecurity();
        sec.AddAccessRule(new PipeAccessRule(System.Security.Principal.WindowsIdentity.GetCurrent().User!,
            PipeAccessRights.Read, System.Security.AccessControl.AccessControlType.Allow));
        using var server = NamedPipeServerStreamAcl.Create(name, PipeDirection.InOut, 2, PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous, 65536, 65536, sec);
        var ct = TestContext.Current.CancellationToken;
        var accept = server.WaitForConnectionAsync(ct);
        using var lanes = new LaneRoutingPipeClient(new MockLoggingService());
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => lanes.ConnectAsync(name, ct));
        Assert.False(accept.IsCompleted);   // the server saw no connection
    }

    // ── Doubles ─────────────────────────────────────────────────────────────────────────────────────────────────

    private sealed class PipePlatform : IPlatformService
    {
        public IReadOnlyList<string>? Pipes { get; set; } = Array.Empty<string>();
        /// <summary>The Logs root GetLogDirectoryPath answers: where the DLLs' per-game log folders are.</summary>
        public string LogRoot { get; set; } = Path.Combine(Path.GetTempPath(), "UE5DumpNoLogs_" + Guid.NewGuid().ToString("N"));
        public Dictionary<int, (string, string?)> Processes { get; } = new();
        public IReadOnlyList<string>? ListNamedPipes() => Pipes;
        public (string ImageName, string? WindowTitle) DescribeProcess(int pid)
            => Processes.TryGetValue(pid, out var p) ? p : ("", null);
        /// <summary>Whether this UI runs as administrator.</summary>
        public bool Elevated { get; set; }
        public bool IsElevated() => Elevated;

        public bool TryAcquireSingleInstance() => true;
        public void ReleaseSingleInstance() { }
        public string GetAppDataPath() => Path.GetTempPath();
        public string GetLogDirectoryPath() => LogRoot;
        public Task<bool> CopyToClipboardAsync(string text) => Task.FromResult(true);
        public Task RevealInExplorerAsync(string path) => Task.CompletedTask;
        public string GetMachineName() => "TEST";
        public void CloseImeForWindow(IntPtr windowHandle) { }
        public Task<string?> ShowSaveFileDialogAsync(string defaultFileName, string filterName, string filterExtension)
            => Task.FromResult<string?>(null);
    }

    private sealed class RecordingPipe : IPipeClient
    {
        public List<string> Dialed { get; } = new();
        public int Disconnects { get; private set; }
        public bool IsConnected { get; private set; }
        /// <summary>When set, only these names answer; any other times out, as NamedPipeClientStream does.</summary>
        public HashSet<string>? Listening { get; set; }
        /// <summary>When set, a connect waits for it.</summary>
        public Task? Gate { get; set; }
        /// <summary>Names whose every instance is taken: listed, yet a connect times out, as NamedPipeClientStream's
        /// does while it waits for a free instance.</summary>
        public HashSet<string>? Busy { get; set; }
        /// <summary>Called with the name on every dial, before the connect resolves: the world changing meanwhile.</summary>
        public Action<string>? OnDial { get; set; }
        /// <summary>Names Windows refuses to open, as NamedPipeClientStream does for a game running as administrator:
        /// the exception type and text measured live on 2026-11-12.</summary>
        public HashSet<string>? Refused { get; set; }
        public event Action<bool>? ConnectionStateChanged { add { } remove { } }
        public event Action<JsonObject>? EventReceived { add { } remove { } }
        public event Action<PipeLogEntry>? Activity { add { } remove { } }
        public event Action<bool>? GameThreadStalledChanged { add { } remove { } }
        public async Task ConnectAsync(string pipeName, CancellationToken ct = default)
        {
            Dialed.Add(pipeName);
            OnDial?.Invoke(pipeName);
            if (Gate is not null) await Gate;
            // As PipeClient: a live connection is kept, whatever name is asked for.
            if (IsConnected) return;
            if (Refused?.Contains(pipeName) ?? false) throw new UnauthorizedAccessException("Access to the path is denied.");
            if ((Listening is not null && !Listening.Contains(pipeName)) || (Busy?.Contains(pipeName) ?? false))
                throw new TimeoutException("The operation has timed out.");
            IsConnected = true;
            ConnectedTo = pipeName;
        }
        /// <summary>The game the live connection reached, which may not be the last name asked for.</summary>
        public string? ConnectedTo { get; private set; }
        public Task DisconnectAsync()
        {
            Disconnects++;
            IsConnected = false;
            return Task.CompletedTask;
        }
        public Task<JsonObject> SendAsync(JsonObject request, CancellationToken ct = default)
            => Task.FromResult(new JsonObject());
        public void Dispose() { }
    }

    /// <summary>A DLL in proxy mode (not scanned yet), which keeps Connect on its shortest path; reports
    /// <see cref="AnsweringPid"/> as the process that answered.</summary>
    private sealed class PidDump : StubDumpService
    {
        private Func<int>? _answer;
        public int AnsweringPid { get; set; }
        /// <summary>Answer with the pid of the game the live connection reached, as the DLL does.</summary>
        public void AnswerFromConnection(RecordingPipe pipe)
            => _answer = () => PipeTargets.TryParsePid(pipe.ConnectedTo, out int pid) ? pid : 0;
        public override Task<EngineState> InitAsync(CancellationToken ct = default)
            => Task.FromResult(new EngineState { ProcessId = _answer?.Invoke() ?? AnsweringPid, GObjectsAddr = "0x0" });
    }
}
