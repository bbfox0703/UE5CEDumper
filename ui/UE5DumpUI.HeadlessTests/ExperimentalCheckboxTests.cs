using System.Reflection;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using UE5DumpUI.Core;
using UE5DumpUI.Models;
using UE5DumpUI.ViewModels;
using UE5DumpUI.Views;
using Xunit;
using static UE5DumpUI.HeadlessTests.LiveFuncsLayout;

namespace UE5DumpUI.HeadlessTests;

/// <summary>A gate that behaves as the app's does: Changed on a real flip and on the first lock, and no untick while
/// locked. The app's own one persists to a file, which a test has no business writing.</summary>
internal sealed class SwitchGate(bool enabled) : IExperimentalGate
{
    private bool _enabled = enabled;

    public bool IsEnabled
    {
        get => _enabled;
        set
        {
            if (_enabled == value || (IsLocked && !value)) return;
            _enabled = value;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    public int SnapshotQuotaMb { get; set; }
    public bool IsLocked { get; private set; }

    public void Lock()
    {
        if (IsLocked) return;
        IsLocked = true;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public event EventHandler? Changed;
}

/// <summary>
/// [LF-EXPOFF-TRACE] The System tab's experimental checkbox, unticked only while disconnected: what the view shows while
/// it waits for a Disconnect (disabled, with a tooltip that still shows and says why), and a dropped pipe counting as a
/// Disconnect through the main window's own state handler -- the path a unit test cannot run, as it posts to the UI
/// thread. And the defence line behind it, as Live Funcs' panel shows it.
/// </summary>
public class ExperimentalCheckboxTests
{
    /// <summary>App data paths are never read here: nothing in these tests saves.</summary>
    private sealed class BlankPlatform : IPlatformService
    {
        public bool TryAcquireSingleInstance() => true;
        public void ReleaseSingleInstance() { }
        public string GetAppDataPath() => "";
        public string GetLogDirectoryPath() => "";
        public Task<bool> CopyToClipboardAsync(string text) => Task.FromResult(true);
        public Task RevealInExplorerAsync(string path) => Task.CompletedTask;
        public string GetMachineName() => "test";
        public void CloseImeForWindow(IntPtr windowHandle) { }
        public Task<string?> ShowSaveFileDialogAsync(string defaultFileName, string filterName, string filterExtension)
            => Task.FromResult<string?>(null);
    }

    /// <summary>A pipe whose connection state the test moves, raised on the calling thread as the real one raises it
    /// on its own.</summary>
    private sealed class StatePipe : IPipeClient
    {
        public bool IsConnected { get; private set; }
        public event Action<bool>? ConnectionStateChanged;
        public event Action<JsonObject>? EventReceived { add { } remove { } }
        public event Action<PipeLogEntry>? Activity { add { } remove { } }
        public event Action<bool>? GameThreadStalledChanged { add { } remove { } }

        public void Raise(bool connected)
        {
            IsConnected = connected;
            ConnectionStateChanged?.Invoke(connected);
        }

        public Task ConnectAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task DisconnectAsync() => Task.CompletedTask;
        public Task<JsonObject> SendAsync(JsonObject request, CancellationToken ct = default)
            => Task.FromResult(new JsonObject());
        public void Dispose() { }
    }

    /// <summary>The System tab with its view model, shown. The panel's one brush resource is the app's App.axaml's,
    /// which the headless application does not load.</summary>
    private static (PointerPanel panel, PointerPanelViewModel vm, SwitchGate gate) Shown(bool enabled)
    {
        LoadAppResources();
        if (!Application.Current!.Resources.ContainsKey("AobMakerUiAccent"))
            Application.Current.Resources["AobMakerUiAccent"] = new SolidColorBrush(Color.Parse("#F78FB3"));
        var gate = new SwitchGate(enabled);
        var vm = new PointerPanelViewModel(new BlankPlatform(), experimentalGate: gate);
        var panel = new PointerPanel { DataContext = vm };
        var window = new Window { Content = panel, Width = 1200, Height = 900 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (panel, vm, gate);
    }

    /// <summary>The opt-in: the panel's one checkbox with no label of its own.</summary>
    private static CheckBox TheBox(Visual panel)
        => panel.GetVisualDescendants().OfType<CheckBox>().Single(c => c.Content == null);

    [Fact]
    public Task Ticked_and_connected_the_box_is_disabled_and_its_tooltip_says_to_disconnect_first() => Headless.Run(() =>
    {
        var (panel, vm, _) = Shown(enabled: true);
        var box = TheBox(panel);
        Assert.True(box.IsChecked);
        Assert.True(box.IsEnabled);
        Assert.Equal(Res("str.Experimental.EnableHint"), ToolTip.GetTip(box) as string);

        vm.SetConnected(true);
        Dispatcher.UIThread.RunJobs();
        Assert.False(box.IsEnabled);
        Assert.Equal(Res("str.Experimental.DisconnectFirst"), ToolTip.GetTip(box) as string);
        // A disabled control gets no pointer events, so without this its tooltip never opens and the reason is unseen.
        Assert.True(ToolTip.GetShowOnDisabled(box), "the tooltip of the disabled box would never open");

        vm.SetConnected(false);
        Dispatcher.UIThread.RunJobs();
        Assert.True(box.IsEnabled);
        Assert.Equal(Res("str.Experimental.EnableHint"), ToolTip.GetTip(box) as string);
    });

    [Fact]
    public Task The_disabled_box_opens_its_tooltip_under_the_pointer() => Headless.Run(() =>
    {
        // The property alone proves nothing about what the user sees: a disabled control is left out of the input hit
        // test, so the pointer over it is over nothing with a tooltip -- unless the tooltip service is told otherwise.
        var (panel, vm, _) = Shown(enabled: true);
        vm.SetConnected(true);
        Dispatcher.UIThread.RunJobs();
        var box = TheBox(panel);
        Assert.False(box.IsEnabled);
        ToolTip.SetShowDelay(box, 0);   // the app's delay is a timer the headless dispatcher does not wait out
        var window = (Window)TopLevel.GetTopLevel(panel)!;
        var centre = box.TranslatePoint(new Point(box.Bounds.Width / 2, box.Bounds.Height / 2), window)!.Value;

        window.MouseMove(centre);
        Dispatcher.UIThread.RunJobs();
        Assert.True(ToolTip.GetIsOpen(box), "the pointer is over the disabled box and its tooltip did not open");
        Assert.Equal(Res("str.Experimental.DisconnectFirst"), ToolTip.GetTip(box) as string);
    });

    [Fact]
    public Task Unticked_and_connected_the_box_can_be_ticked() => Headless.Run(() =>
    {
        var (panel, vm, gate) = Shown(enabled: false);
        vm.SetConnected(true);
        Dispatcher.UIThread.RunJobs();
        var box = TheBox(panel);
        Assert.True(box.IsEnabled);

        box.IsChecked = true;
        Dispatcher.UIThread.RunJobs();
        Assert.True(gate.IsEnabled);
        Assert.False(box.IsEnabled);
        Assert.Equal(Res("str.Experimental.DisconnectFirst"), ToolTip.GetTip(box) as string);
    });

    [Fact]
    public Task Locked_the_box_keeps_the_plain_tooltip_connected_or_not() => Headless.Run(() =>
    {
        // A Disconnect would not free a locked box, so its tooltip does not send the user to one.
        var (panel, vm, gate) = Shown(enabled: true);
        gate.Lock();
        vm.SetConnected(true);
        Dispatcher.UIThread.RunJobs();
        var box = TheBox(panel);
        Assert.False(box.IsEnabled);
        Assert.Equal(Res("str.Experimental.EnableHint"), ToolTip.GetTip(box) as string);
    });

    [Fact]
    public Task A_dropped_pipe_frees_the_box_as_a_Disconnect_does() => Headless.Run(() =>
    {
        var dump = DispatchProxy.Create<IDumpService, Canned>();
        var log = DispatchProxy.Create<ILoggingService, Canned>();
        var pipe = new StatePipe();
        var main = new MainWindowViewModel(pipe, dump, log, new BlankPlatform(), experimentalGate: new SwitchGate(true));
        try
        {
            pipe.Raise(true);
            Dispatcher.UIThread.RunJobs();
            Assert.True(main.IsConnected);
            Assert.False(main.Pointers.CanToggleExperimental);

            // The game closed: the pipe says so, and nobody pressed Disconnect.
            pipe.Raise(false);
            Dispatcher.UIThread.RunJobs();
            Assert.False(main.IsConnected);
            Assert.True(main.Pointers.CanToggleExperimental);
        }
        finally { main.Dispose(); }
    });

    /// <summary>The defence line behind the box, on Live Funcs' own panel: a trace kept while the features were on, and
    /// then they are off by any path, shows neither Open in Call Trace nor its sentences on the status line.</summary>
    [Fact]
    public Task Off_Live_Funcs_shows_neither_Open_in_Call_Trace_nor_the_trace_sentences() => Headless.Run(() =>
    {
        var gate = new SwitchGate(true);
        var (panel, vm) = LaidWithAStackChosen(usPerCapture: 20, gate: gate);
        Assert.True(vm.HasTraceToOpen);
        var open = panel.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == Res("str.LF.Trace.Open"));
        Assert.True(open.IsEffectivelyVisible, "Open in Call Trace is hidden with a trace kept and the features on");
        string kept = string.Format(Res("str.LF.Trace.KeptAll"), vm.LastTraceInfo!.Kept);
        var status = LiveFuncsLayout.Shown(panel, vm.StatusText);   // this class's own Shown lays out the System tab
        Assert.Contains(kept, status.Text, StringComparison.Ordinal);
        string before = status.Text!;

        gate.IsEnabled = false;
        Dispatcher.UIThread.RunJobs();
        Assert.False(open.IsEffectivelyVisible, "Open in Call Trace shows with the experimental features off");
        Assert.DoesNotContain(kept, status.Text ?? "", StringComparison.Ordinal);
        Assert.StartsWith(status.Text + " " + kept, before, StringComparison.Ordinal);   // the Stop's own line stays
        Assert.DoesNotContain(kept, ToolTip.GetTip(status) as string ?? "", StringComparison.Ordinal);
    });
}
