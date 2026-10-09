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

    private (MainWindowViewModel main, ExperimentalGate gate) Make(bool enabled, bool connected)
    {
        var platform = new MockPlatformService(_dir);
        var gate = new ExperimentalGate(platform) { IsEnabled = enabled };
        var main = new MainWindowViewModel(pipeClient: new NoopPipe(), dump: new StubDumpService(),
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
