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
    public bool TeardownOwed { get; set; }
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
/// [LF-EXPOFF-TRACE] The System tab's experimental checkbox as the view shows it: free to untick connected or not (the
/// maintainer's choice after the review, 「連線中也可取消，立即清理」), and disabled only by the session lock, when its
/// tooltip -- which a disabled control opens only with ToolTip.ShowOnDisabled -- says why. The connection is the main
/// window's own, raised by its pipe through the posted state handler. And the defence line behind the box, as Live
/// Funcs' panel shows it.
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

    /// <summary>The System tab of a main window, shown, connected through its pipe when <paramref name="connected"/>.
    /// The panel's one brush resource is the app's App.axaml's, which the headless application does not load.</summary>
    private static (PointerPanel panel, MainWindowViewModel main, SwitchGate gate) Shown(bool enabled, bool connected)
    {
        LoadAppResources();
        if (!Application.Current!.Resources.ContainsKey("AobMakerUiAccent"))
            Application.Current.Resources["AobMakerUiAccent"] = new SolidColorBrush(Color.Parse("#F78FB3"));
        var gate = new SwitchGate(enabled);
        var pipe = new StatePipe();
        var main = new MainWindowViewModel(pipe, DispatchProxy.Create<IDumpService, Canned>(),
                                           DispatchProxy.Create<ILoggingService, Canned>(), new BlankPlatform(),
                                           experimentalGate: gate);
        if (connected)
        {
            pipe.Raise(true);
            Dispatcher.UIThread.RunJobs();
            Assert.True(main.IsConnected, "the main window did not take the pipe's connect");
        }
        var panel = new PointerPanel { DataContext = main.Pointers };
        var window = new Window { Content = panel, Width = 1200, Height = 900 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (panel, main, gate);
    }

    /// <summary>The opt-in: the panel's one checkbox with no label of its own.</summary>
    private static CheckBox TheBox(Visual panel)
        => panel.GetVisualDescendants().OfType<CheckBox>().Single(c => c.Content == null);

    [Fact]
    public Task Ticked_and_connected_the_box_unticks() => Headless.Run(() =>
    {
        var (panel, main, gate) = Shown(enabled: true, connected: true);
        try
        {
            var box = TheBox(panel);
            Assert.True(box.IsChecked);
            Assert.True(box.IsEnabled, "connected, the ticked box is disabled");
            Assert.Equal(Res("str.Experimental.EnableHint"), ToolTip.GetTip(box) as string);

            box.SetCurrentValue(CheckBox.IsCheckedProperty, (bool?)false);   // as a click writes it
            Dispatcher.UIThread.RunJobs();
            Assert.False(gate.IsEnabled, "connected, the untick did not reach the gate");
            Assert.False(main.ExperimentalEnabled);
            Assert.True(box.IsEnabled);
        }
        finally { main.Dispose(); }
    });

    [Fact]
    public Task Unticked_and_connected_the_box_ticks_and_stays_free() => Headless.Run(() =>
    {
        var (panel, main, gate) = Shown(enabled: false, connected: true);
        try
        {
            var box = TheBox(panel);
            Assert.True(box.IsEnabled);

            box.SetCurrentValue(CheckBox.IsCheckedProperty, (bool?)true);
            Dispatcher.UIThread.RunJobs();
            Assert.True(gate.IsEnabled);
            Assert.True(box.IsEnabled, "ticked while connected, the box cannot be unticked again");
        }
        finally { main.Dispose(); }
    });

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public Task Locked_the_box_says_the_features_stay_on_until_a_restart(bool connected) => Headless.Run(() =>
    {
        var (panel, main, gate) = Shown(enabled: true, connected);
        try
        {
            gate.Lock();
            Dispatcher.UIThread.RunJobs();
            var box = TheBox(panel);
            Assert.False(box.IsEnabled);
            Assert.Equal(Res("str.Experimental.LockedForSession"), ToolTip.GetTip(box) as string);
            // A disabled control gets no pointer events, so without this its tooltip never opens and the reason is unseen.
            Assert.True(ToolTip.GetShowOnDisabled(box), "the tooltip of the locked box would never open");
        }
        finally { main.Dispose(); }
    });

    [Fact]
    public Task The_locked_box_opens_its_tooltip_under_the_pointer() => Headless.Run(() =>
    {
        // The property alone proves nothing about what the user sees: a disabled control is left out of the input hit
        // test, so the pointer over it is over nothing with a tooltip -- unless the tooltip service is told otherwise.
        var (panel, main, gate) = Shown(enabled: true, connected: false);
        try
        {
            gate.Lock();
            Dispatcher.UIThread.RunJobs();
            var box = TheBox(panel);
            Assert.False(box.IsEnabled);
            ToolTip.SetShowDelay(box, 0);   // the app's delay is a timer the headless dispatcher does not wait out
            var window = (Window)TopLevel.GetTopLevel(panel)!;
            var centre = box.TranslatePoint(new Point(box.Bounds.Width / 2, box.Bounds.Height / 2), window)!.Value;

            window.MouseMove(centre);
            Dispatcher.UIThread.RunJobs();
            Assert.True(ToolTip.GetIsOpen(box), "the pointer is over the locked box and its tooltip did not open");
            Assert.Equal(Res("str.Experimental.LockedForSession"), ToolTip.GetTip(box) as string);
        }
        finally { main.Dispose(); }
    });

    /// <summary>The defence line behind the box, on Live Funcs' own panel: a trace kept while the features were on, and
    /// then they are off by any path, shows neither Open in Call Trace nor its sentences on the status line; on again,
    /// the DLL still holds the trace, so both come back.</summary>
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

        gate.IsEnabled = true;
        Dispatcher.UIThread.RunJobs();
        Assert.True(open.IsEffectivelyVisible, "on again, Open in Call Trace stays hidden over a trace the DLL holds");
        Assert.Equal(before, status.Text);
    });
}
