using System.IO;
using System.Text.Json.Nodes;
using UE5DumpUI.Core;
using UE5DumpUI.Services;
using UE5DumpUI.ViewModels;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// [FABRICATE-DEFAULT-UNSEEDED] Options shows the Fabricate count, and Live Walker's Copy CE Field must use that same
/// count from the start. Only the exponent's change handler copied it across, and loading the default exponent is not a
/// change, so Live Walker sat at 0 -- no fabricated array rows -- until the slider was moved. Measured live (L43): the
/// selected Arr_Delegates row copied its two live rows only, and fabricated [2] / [3] after a 4 -> 8 -> 4 nudge.
/// </summary>
public class FabricateDefaultSeedTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"UE5DumpFabSeed_{Guid.NewGuid():N}");

    public FabricateDefaultSeedTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); }
        catch { /* best effort */ }
    }

    private MainWindowViewModel Make(MockPlatformService platform) =>
        new(pipeClient: new NoopPipe(), dump: new StubDumpService(), log: new MockLoggingService(), platform: platform,
            experimentalGate: new ExperimentalGate(platform));

    [Fact]
    public void A_fresh_window_hands_Live_Walker_the_count_Options_shows()
    {
        var main = Make(new MockPlatformService(_dir));
        Assert.Equal(4, main.FabricateArrayCount);   // the default exponent 2
        Assert.Equal(main.FabricateArrayCount, main.LiveWalker.FabricateArrayCount);
    }

    [Fact]
    public void Loading_the_default_options_hands_Live_Walker_the_count_Options_shows()
    {
        var platform = new MockPlatformService(_dir);
        var main = Make(platform);
        main.InitializeOptionsPersistence(new UiOptionsStore(platform));   // nothing saved yet: the defaults
        Assert.Equal(4, main.FabricateArrayCount);
        Assert.Equal(main.FabricateArrayCount, main.LiveWalker.FabricateArrayCount);
    }

    [Fact]
    public void Control_a_moved_slider_still_reaches_Live_Walker()
    {
        var main = Make(new MockPlatformService(_dir));
        main.FabricateArrayCountExponent = 3;
        Assert.Equal(8, main.LiveWalker.FabricateArrayCount);
        main.FabricateArrayCountExponent = 0;
        Assert.Equal(0, main.LiveWalker.FabricateArrayCount);   // 0 is "off"
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
