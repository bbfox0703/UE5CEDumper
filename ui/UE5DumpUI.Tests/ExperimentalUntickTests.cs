using System.IO;
using System.Text.Json.Nodes;
using UE5DumpUI.Core;
using UE5DumpUI.Services;
using UE5DumpUI.ViewModels;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// [LF-EXPOFF-TRACE] The System tab's experimental checkbox unticks connected or not (the maintainer's choice after the
/// review, 2026-10-09: 「連線中也可取消，立即清理」): what the features run in the game is turned off at once, or at the next
/// connect, by Teleport (<c>ExperimentalTeardownTests</c>). Only the session lock disables the box.
/// The real gate and the real main window, so the connection is the app's own flag and the refusal while locked is the
/// gate's own.
/// </summary>
public class ExperimentalUntickTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"UE5DumpExpUntick_{Guid.NewGuid():N}");

    public ExperimentalUntickTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); }
        catch { /* best effort */ }
    }

    private (MainWindowViewModel main, ExperimentalGate gate) Make(bool enabled, bool connected,
                                                                   StubDumpService? dump = null)
    {
        var platform = new MockPlatformService(_dir);
        var gate = new ExperimentalGate(platform) { IsEnabled = enabled };
        var main = new MainWindowViewModel(pipeClient: new NoopPipe(), dump: dump ?? new StubDumpService(),
                                           log: new MockLoggingService(), platform: platform, experimentalGate: gate);
        main.IsConnected = connected;
        return (main, gate);
    }

    private static List<string> Record(PointerPanelViewModel vm)
    {
        var raised = new List<string>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName ?? "");
        return raised;
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Ticked_it_can_be_unticked_connected_or_not(bool connected)
    {
        var (main, gate) = Make(enabled: true, connected);
        Assert.True(main.Pointers.CanToggleExperimental);

        main.Pointers.ExperimentalEnabled = false;
        Assert.False(gate.IsEnabled);
        Assert.False(main.ExperimentalEnabled);   // the tabs go with it
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Unticked_it_can_be_ticked_connected_or_not(bool connected)
    {
        var (main, gate) = Make(enabled: false, connected);
        Assert.True(main.Pointers.CanToggleExperimental);

        main.Pointers.ExperimentalEnabled = true;
        Assert.True(gate.IsEnabled);
        Assert.True(main.Pointers.CanToggleExperimental);   // ticked, it stays free to untick
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void The_session_lock_wins_connected_or_not(bool connected)
    {
        var (main, gate) = Make(enabled: true, connected);
        gate.Lock();
        Assert.False(main.Pointers.CanToggleExperimental);

        main.Pointers.ExperimentalEnabled = false;
        Assert.True(gate.IsEnabled);
        Assert.True(main.Pointers.ExperimentalEnabled);
    }

    [Fact]
    public void A_connect_or_a_disconnect_leaves_the_box_free()
    {
        var (main, _) = Make(enabled: true, connected: false);
        main.IsConnected = true;
        Assert.True(main.Pointers.CanToggleExperimental);
        main.IsConnected = false;
        Assert.True(main.Pointers.CanToggleExperimental);
    }

    [Fact]
    public void A_lock_raises_what_the_box_binds()
    {
        var (main, gate) = Make(enabled: true, connected: true);
        var raised = Record(main.Pointers);

        gate.Lock();
        Assert.Contains(nameof(PointerPanelViewModel.CanToggleExperimental), raised);
        Assert.Contains(nameof(PointerPanelViewModel.ExperimentalToggleTip), raised);
    }

    /// <summary>[EXPOFF-TEARDOWN] The main window hands its gate to Detect Stats, so the box's untick reaches a scan
    /// running on the hidden tab (DetectStatsViewModelTests has the scan's side).</summary>
    [Fact]
    public async Task The_boxs_untick_reaches_a_running_Detect_Stats_scan()
    {
        var dump = new HeldSearch();
        var (main, _) = Make(enabled: true, connected: true, dump);
        string idle = main.DetectStats.StatusText;

        var run = main.DetectStats.DetectCommand.ExecuteAsync(null);
        Assert.True(main.DetectStats.IsBusy);
        main.Pointers.ExperimentalEnabled = false;
        Assert.Equal(idle, main.DetectStats.StatusText);

        dump.Hold.SetResult(true);
        await run;
        Assert.False(main.DetectStats.IsBusy);
        Assert.Equal(idle, main.DetectStats.StatusText);
    }

    // ---- The round-3 review's LOW: which tabs lock the opt-in was a literal list in the view's tab switch, and the claims
    // that rest on it -- what the rest of an experimental feature needs on an untick, and the locked box's tooltip,
    // which names those tabs -- were checked by nothing: dropping a tab from the list passed every suite.

    /// <summary>Every experimental tab of the main window (shown only with the features on): its Tag and its header.</summary>
    private static List<(string tag, string header)> ExperimentalTabs()
    {
        var axaml = System.Xml.Linq.XDocument.Load(NumericInputCoercionTests.RepoFile("ui/UE5DumpUI/Views/MainWindow.axaml"));
        var en = File.ReadAllText(NumericInputCoercionTests.RepoFile("ui/UE5DumpUI/Resources/Strings/en.axaml"));
        var tabs = new List<(string, string)>();
        foreach (var tab in axaml.Descendants().Where(e => e.Name.LocalName == "TabItem"
                                                          && (string?)e.Attribute("IsVisible") == "{Binding ExperimentalEnabled}"))
        {
            string key = ((string)tab.Attribute("Header")!).Replace("{StaticResource ", "").TrimEnd('}');
            tabs.Add(((string)tab.Attribute("Tag")!, EnString(en, key)));
        }
        return tabs;
    }

    private static string EnString(string en, string key)
    {
        var m = System.Text.RegularExpressions.Regex.Match(en, "x:Key=\"" + System.Text.RegularExpressions.Regex.Escape(key)
                                                               + "\">([^<]*)</sys:String>");
        Assert.True(m.Success, $"{key} is not in en.axaml");
        return System.Net.WebUtility.HtmlDecode(m.Groups[1].Value);
    }

    [Fact]
    public void The_locked_box_names_exactly_the_experimental_tabs_that_lock_it()
    {
        var en = File.ReadAllText(NumericInputCoercionTests.RepoFile("ui/UE5DumpUI/Resources/Strings/en.axaml"));
        string locked = EnString(en, "str.Experimental.LockedForSession");
        var tabs = ExperimentalTabs();
        Assert.Contains(tabs, t => MainWindowViewModel.LocksExperimental(t.tag));    // not vacuous either way
        Assert.Contains(tabs, t => !MainWindowViewModel.LocksExperimental(t.tag));
        foreach (var (tag, header) in tabs)
            Assert.True(MainWindowViewModel.LocksExperimental(tag) == locked.Contains(header, StringComparison.Ordinal),
                        $"{tag} ('{header}'): locks = {MainWindowViewModel.LocksExperimental(tag)}, but the tooltip "
                        + (locked.Contains(header, StringComparison.Ordinal) ? "names it" : "does not name it"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("LiveFuncs")]
    [InlineData("Teleport")]
    [InlineData("Pointers")]
    public void A_tab_that_is_not_experimental_locks_nothing(string? tag)
        => Assert.False(MainWindowViewModel.LocksExperimental(tag));

    [Fact]
    public void The_tab_switch_locks_the_opt_in_through_that_answer()
    {
        // The view's tab switch is not reachable from a unit test; its call is pinned instead, so the list cannot be
        // kept in two places again.
        var view = File.ReadAllText(NumericInputCoercionTests.RepoFile("ui/UE5DumpUI/Views/MainWindow.axaml.cs"));
        Assert.Matches(@"if \(MainWindowViewModel\.LocksExperimental\(tag\)\)\s*vm\.LockExperimental\(\);", view);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(view, @"vm\.LockExperimental\(\)"));
    }

    /// <summary>A scorer batch that answers, empty, only when the test lets it.</summary>
    private sealed class HeldSearch : StubDumpService
    {
        public TaskCompletionSource<bool> Hold { get; } = new();

        public override async Task<UE5DumpUI.Models.PropertySearchBatchResult> SearchPropertiesBatchAsync(
            string[] queries, string[]? types = null, bool gameOnly = true, int limitPerQuery = 200,
            CancellationToken ct = default)
        {
            await Hold.Task;
            return new UE5DumpUI.Models.PropertySearchBatchResult();
        }
    }

    private sealed class NoopPipe : IPipeClient
    {
        public bool IsConnected => false;
        public event Action<bool>? ConnectionStateChanged { add { } remove { } }
        public event Action<JsonObject>? EventReceived { add { } remove { } }
        public event Action<UE5DumpUI.Models.PipeLogEntry>? Activity { add { } remove { } }
        public event Action<bool>? GameThreadStalledChanged { add { } remove { } }
        public Task ConnectAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task DisconnectAsync() => Task.CompletedTask;
        public Task<JsonObject> SendAsync(JsonObject request, CancellationToken ct = default)
            => Task.FromResult(new JsonObject());
        public void Dispose() { }
    }
}
