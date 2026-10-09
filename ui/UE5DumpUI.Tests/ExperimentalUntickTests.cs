using System.IO;
using System.Text.Json.Nodes;
using UE5DumpUI.Core;
using UE5DumpUI.Services;
using UE5DumpUI.ViewModels;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// [LF-EXPOFF-TRACE] The System tab's experimental checkbox can be unticked only while disconnected (the maintainer's
/// choice, 2026-10-09): ticked and connected it is disabled and its tooltip says to Disconnect first; ticking stays
/// allowed at any time; the session lock still wins. A trace, and anything else an experimental feature holds in the
/// game, goes with the connection, so nothing it kept can outlive the opt-out.
/// The real gate, so the refusal while locked is the app's own.
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

    private (PointerPanelViewModel vm, ExperimentalGate gate) Make(bool enabled, bool connected)
    {
        var platform = new MockPlatformService(_dir);
        var gate = new ExperimentalGate(platform) { IsEnabled = enabled };
        var vm = new PointerPanelViewModel(platform, log: new MockLoggingService(), experimentalGate: gate);
        vm.SetConnected(connected);
        return (vm, gate);
    }

    private static List<string> Record(PointerPanelViewModel vm)
    {
        var raised = new List<string>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName ?? "");
        return raised;
    }

    /// <summary>What the checkbox binds: whether it can be clicked, and the tooltip that says why not.</summary>
    private static void AssertRaisesTheBox(List<string> raised)
    {
        Assert.Contains(nameof(PointerPanelViewModel.CanToggleExperimental), raised);
        Assert.Contains(nameof(PointerPanelViewModel.ExperimentalUntickNeedsDisconnect), raised);
        Assert.Contains(nameof(PointerPanelViewModel.ExperimentalToggleTip), raised);
    }

    [Fact]
    public void Unticked_it_can_be_ticked_connected_or_not()
    {
        var (vm, gate) = Make(enabled: false, connected: false);
        Assert.True(vm.CanToggleExperimental);
        vm.SetConnected(true);
        Assert.True(vm.CanToggleExperimental);
        Assert.False(vm.ExperimentalUntickNeedsDisconnect);

        vm.ExperimentalEnabled = true;
        Assert.True(gate.IsEnabled);
    }

    [Fact]
    public void Ticked_and_connected_it_cannot_be_unticked_and_says_to_disconnect_first()
    {
        var (vm, _) = Make(enabled: true, connected: true);
        Assert.False(vm.CanToggleExperimental);
        Assert.True(vm.ExperimentalUntickNeedsDisconnect);
    }

    [Fact]
    public void Ticked_and_disconnected_it_can_be_unticked()
    {
        var (vm, gate) = Make(enabled: true, connected: false);
        Assert.True(vm.CanToggleExperimental);
        Assert.False(vm.ExperimentalUntickNeedsDisconnect);

        vm.ExperimentalEnabled = false;
        Assert.False(gate.IsEnabled);
    }

    [Fact]
    public void Connected_an_untick_is_refused_and_the_box_reads_the_gate_again()
    {
        // The disabled box sends none; the rule holds at the property all the same, as the gate holds the lock's.
        var (vm, gate) = Make(enabled: true, connected: true);
        var raised = Record(vm);

        vm.ExperimentalEnabled = false;
        Assert.True(gate.IsEnabled);
        Assert.True(vm.ExperimentalEnabled);
        Assert.Contains(nameof(PointerPanelViewModel.ExperimentalEnabled), raised);
    }

    [Fact]
    public void The_session_lock_wins_whatever_the_connection()
    {
        var (vm, gate) = Make(enabled: true, connected: false);
        gate.Lock();
        Assert.False(vm.CanToggleExperimental);
        // A Disconnect would not free it, so the tooltip does not send the user to one.
        Assert.False(vm.ExperimentalUntickNeedsDisconnect);

        vm.SetConnected(true);
        Assert.False(vm.CanToggleExperimental);
        Assert.False(vm.ExperimentalUntickNeedsDisconnect);

        vm.SetConnected(false);
        Assert.False(vm.CanToggleExperimental);
        vm.ExperimentalEnabled = false;
        Assert.True(gate.IsEnabled);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_connect_or_a_disconnect_raises_the_checkbox(bool connect)
    {
        var (vm, _) = Make(enabled: true, connected: !connect);
        var raised = Record(vm);

        vm.SetConnected(connect);
        AssertRaisesTheBox(raised);
        Assert.Equal(!connect, vm.CanToggleExperimental);
    }

    [Fact]
    public void Ticking_while_connected_raises_the_checkbox_and_then_waits_for_a_disconnect()
    {
        var (vm, gate) = Make(enabled: false, connected: true);
        var raised = Record(vm);

        vm.ExperimentalEnabled = true;
        Assert.True(gate.IsEnabled);
        AssertRaisesTheBox(raised);
        Assert.False(vm.CanToggleExperimental);
        Assert.True(vm.ExperimentalUntickNeedsDisconnect);

        raised.Clear();
        gate.Lock();
        AssertRaisesTheBox(raised);
        Assert.False(vm.ExperimentalUntickNeedsDisconnect);
    }

    [Fact]
    public void The_main_windows_connection_reaches_the_checkbox()
    {
        // Connect and Disconnect set IsConnected themselves, and the pipe's state handler sets it on a connect and on a
        // drop; the drop's own path, through the posted handler, is the headless suite's (ExperimentalCheckboxTests).
        var platform = new MockPlatformService(_dir);
        var gate = new ExperimentalGate(platform) { IsEnabled = true };
        var main = new MainWindowViewModel(pipeClient: new NoopPipe(), dump: new StubDumpService(),
                                           log: new MockLoggingService(), platform: platform, experimentalGate: gate);
        Assert.True(main.Pointers.CanToggleExperimental);

        main.IsConnected = true;
        Assert.False(main.Pointers.CanToggleExperimental);
        Assert.True(main.Pointers.ExperimentalUntickNeedsDisconnect);

        main.IsConnected = false;
        Assert.True(main.Pointers.CanToggleExperimental);
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
