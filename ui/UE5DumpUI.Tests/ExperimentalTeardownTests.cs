using System.IO;
using UE5DumpUI.Core;
using UE5DumpUI.Models;
using UE5DumpUI.Services;
using UE5DumpUI.ViewModels;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// [LF-EXPOFF-TRACE] [EXPOFF-TEARDOWN] What turning the experimental features off does to what they run in the game,
/// as the maintainer chose after the review (2026-10-09, 「連線中也可取消，立即清理」): unticked while connected,
/// Teleport's teardown turns Keep Foreground, Fly, See-through and a Stealth hold off at once, and only then releases
/// their hotkeys; unticked while disconnected, the next connect with the features still off turns off whatever the
/// connect prime reads on -- never a connect with them on. The DLL's last-client teardown ends See-through, not the
/// other three, so without this they ran on behind hidden cards.
/// </summary>
public class ExperimentalTeardownTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"UE5DumpExpTeardown_{Guid.NewGuid():N}");

    public ExperimentalTeardownTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); }
        catch { /* best effort */ }
    }

    /// <summary>The DLL as the Teleport cards see it: each experimental feature's state, answered by its get and
    /// moved by its set, and what was sent to turn one off, in order. A hold, when set, keeps an answer back.</summary>
    private sealed class FakeDll : StubDumpService
    {
        public int Foreground;
        public bool Fly;
        public bool SeeThrough;
        public List<ForcedFieldInfo> Forced { get; } = new();
        public List<string> Off { get; } = new();
        /// <summary>The next foreground-lock OFF waits on this; one OFF takes it, so two teardowns can be held apart.</summary>
        public TaskCompletionSource<bool>? HoldForegroundOff;
        /// <summary>The next foreground read waits on this; one read takes it.</summary>
        public TaskCompletionSource<bool>? HoldForegroundRead;

        public const string Meter = "BP_Player_C::Visibility";

        public void HoldTheMeter() => Forced.Add(new ForcedFieldInfo
        {
            ClassName = "BP_Player_C", FieldName = "Visibility", Kind = "numeric", Value = 0, Held = 1,
        });

        public override async Task<int> SetForegroundLockAsync(bool enable, CancellationToken ct = default)
        {
            if (!enable)
            {
                Off.Add("foreground");
                if (HoldForegroundOff is { } hold)
                {
                    HoldForegroundOff = null;
                    await hold.Task;
                }
            }
            Foreground = enable ? 1 : 0;
            return Foreground;
        }

        /// <summary>How the foreground read fails, when it does: "throw" (the pipe), or "-1" (the DLL's error code).</summary>
        public string? ForegroundReadFails;

        public override async Task<int> GetForegroundLockAsync(CancellationToken ct = default)
        {
            if (ForegroundReadFails == "throw") throw new InvalidOperationException("the read failed");
            if (ForegroundReadFails == "-1") return -1;
            if (HoldForegroundRead is { } hold)
            {
                HoldForegroundRead = null;
                await hold.Task;
            }
            return Foreground;
        }

        /// <summary>No pawn to fly (a menu, a loading screen, death, a vehicle): Fly's reply says no CharacterMovement
        /// and still says whether the worker runs, as Dunste's GetStatus does.</summary>
        public bool NoPawn;

        private FlyStatus FlyNow => new() { HasCmc = !NoPawn, Active = Fly, State = Fly ? 1 : 0 };

        public override Task<FlyStatus> FlySetAsync(bool? enable, double? speed, int? preset, bool? noclip,
                                                    CancellationToken ct = default)
        {
            if (enable is bool on)
            {
                if (!on) Off.Add("fly");
                Fly = on;
            }
            return Task.FromResult(FlyNow);
        }

        public override Task<FlyStatus> FlyGetStateAsync(CancellationToken ct = default) => Task.FromResult(FlyNow);

        public override Task<SeeThroughStatus> SeeThroughSetAsync(bool? enable, int? count, CancellationToken ct = default)
        {
            if (enable is bool on)
            {
                if (!on) Off.Add("seethrough");
                SeeThrough = on;
            }
            return Task.FromResult(new SeeThroughStatus { Active = SeeThrough, HasTarget = true });
        }

        public override Task<SeeThroughStatus> SeeThroughGetStateAsync(CancellationToken ct = default)
            => Task.FromResult(new SeeThroughStatus { Active = SeeThrough, HasTarget = true });

        public override Task<IReadOnlyList<StealthCandidate>> FindStealthMeterAsync(int max = 8, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<StealthCandidate>>(new List<StealthCandidate>
            {
                new() { ClassName = "BP_Player_C", FieldName = "Visibility", Score = 9 },
            });

        public override Task<IReadOnlyList<ForcedFieldInfo>> GetForcedFieldsAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ForcedFieldInfo>>(Forced.ToList());

        public override Task<ForceFieldResult> ForceFieldAsync(string className, string fieldName, string kind,
                                                               double value = 0, bool on = false, CancellationToken ct = default)
        {
            HoldTheMeter();
            return Task.FromResult(new ForceFieldResult { Held = 1, Resolved = true });
        }

        public override Task<int> ResetFieldAsync(string className, string fieldName, CancellationToken ct = default)
        {
            Off.Add($"reset_field {className}::{fieldName}");
            Forced.RemoveAll(f => f.ClassName == className && f.FieldName == fieldName);
            return Task.FromResult(0);
        }

        public override Task<List<TeleportMarker>> TeleportGetMarkersAsync(CancellationToken ct = default)
            => Task.FromResult(new List<TeleportMarker>());
    }

    /// <summary>The registrations still held: a registration leaves the list when it is disposed.</summary>
    private sealed class Hotkeys : IGlobalHotkeyService
    {
        public List<string> Live { get; } = new();

        public IGlobalHotkeyRegistration? RegisterCursorHotkey(Action onPressed) => null;

        public IGlobalHotkeyRegistration? RegisterSpecific(uint modifiers, uint vk, string label, Action onPressed)
        {
            Live.Add(label);
            return new Registration(this, label);
        }

        private sealed class Registration(Hotkeys owner, string label) : IGlobalHotkeyRegistration
        {
            public string Label => label;
            public void Dispose() => owner.Live.Remove(label);
        }
    }

    /// <summary>The opt-in as the app's gate behaves when unlocked: Changed on a real flip, and an untick owes a
    /// teardown that a tick withdraws. <paramref name="owed"/> is what an earlier session left in experimental.json.</summary>
    private sealed class Switch(bool enabled, bool owed = false) : IExperimentalGate
    {
        private bool _enabled = enabled;

        public bool IsEnabled
        {
            get => _enabled;
            set
            {
                if (_enabled == value) return;
                _enabled = value;
                TeardownOwed = !value;
                Changed?.Invoke(this, EventArgs.Empty);
            }
        }

        public bool TeardownOwed { get; set; } = owed;
        public int SnapshotQuotaMb { get; set; }
        public bool IsLocked => false;
        public void Lock() { }
        public event EventHandler? Changed;
    }

    /// <summary>Teleport with the experimental hotkeys Keep Foreground OFF and Fly toggle bound, as saved.</summary>
    private (TeleportViewModel vm, FakeDll dll, Hotkeys hotkeys, Switch gate) Make(bool enabled, bool owed = false)
    {
        var platform = new MockPlatformService(_dir);
        new TeleportHotkeyStore(platform).Save(new Dictionary<string, TeleportHotkeyBinding>
        {
            ["foreground_off"] = new TeleportHotkeyBinding(HotkeyModifiers.Control, 0x78 /*F9*/),
            ["fly_toggle"] = new TeleportHotkeyBinding(HotkeyModifiers.Control, 0x77 /*F8*/),
        });
        var dll = new FakeDll();
        var hotkeys = new Hotkeys();
        var gate = new Switch(enabled, owed);
        var vm = new TeleportViewModel(dll, new MockLoggingService(), platform, aobMaker: null,
                                       globalHotkeys: hotkeys, experimentalGate: gate);
        return (vm, dll, hotkeys, gate);
    }

    private static async Task Connect(TeleportViewModel vm)
    {
        vm.SetConnected(true);
        await vm.ConnectPrime;
        await vm.ExperimentalTeardown;
    }

    /// <summary>Keep Foreground, Fly, See-through and a Stealth hold all on, through their own cards.</summary>
    private static async Task TurnAllOn(TeleportViewModel vm)
    {
        await vm.ForceForegroundLockOnCommand.ExecuteAsync(null);
        await vm.ApplyFlyCommand.ExecuteAsync(null);
        await vm.ApplySeeThroughCommand.ExecuteAsync(null);
        await vm.DetectStealthMeterCommand.ExecuteAsync(null);
        await vm.HoldStealthCommand.ExecuteAsync(null);
        Assert.Equal("ON", vm.ForegroundLockState);
        Assert.Equal("ON", vm.FlyState);
        Assert.Equal("ON", vm.SeeThroughState);
        Assert.Equal("Holding @0", vm.StealthState);
    }

    /// <summary>What a teardown sends, in order. Keep Foreground goes last (the round-3 review's LOW): an untick is a
    /// click in the UI, so the game is unfocused, and a game that idles when unfocused could no longer run the
    /// See-through un-hide or the noclip collision restore, which need its game thread.</summary>
    private static readonly string[] AllOff = { "fly", "seethrough", "reset_field " + FakeDll.Meter, "foreground" };

    [Fact]
    public async Task Unticked_while_connected_everything_the_features_run_goes_off_at_once()
    {
        var (vm, dll, _, gate) = Make(enabled: true);
        await Connect(vm);
        await TurnAllOn(vm);

        gate.IsEnabled = false;
        await vm.ExperimentalTeardown;

        Assert.Equal(AllOff, dll.Off);
        Assert.Equal(0, dll.Foreground);
        Assert.False(dll.Fly);
        Assert.False(dll.SeeThrough);
        Assert.Empty(dll.Forced);
        Assert.Equal("OFF", vm.ForegroundLockState);
        Assert.Equal("OFF", vm.FlyState);
        Assert.Equal("Off", vm.StealthState);
    }

    [Fact]
    public async Task Unticked_while_connected_the_hotkeys_go_only_after_what_they_control_is_off()
    {
        var (vm, dll, hotkeys, gate) = Make(enabled: true);
        await Connect(vm);
        await TurnAllOn(vm);
        Assert.Equal(2, hotkeys.Live.Count);
        var offHold = new TaskCompletionSource<bool>();
        dll.HoldForegroundOff = offHold;

        gate.IsEnabled = false;
        Assert.Equal(AllOff, dll.Off);   // all sent, the last not yet answered
        Assert.Equal(2, hotkeys.Live.Count);   // Keep Foreground OFF's own key stays while the lock may still be on

        offHold.SetResult(true);
        await vm.ExperimentalTeardown;
        Assert.Equal(AllOff, dll.Off);
        Assert.Empty(hotkeys.Live);
    }

    [Fact]
    public async Task Ticked_again_before_the_teardown_ends_the_hotkeys_stay()
    {
        var (vm, dll, hotkeys, gate) = Make(enabled: true);
        await Connect(vm);
        await TurnAllOn(vm);
        var offHold = new TaskCompletionSource<bool>();
        dll.HoldForegroundOff = offHold;

        gate.IsEnabled = false;
        gate.IsEnabled = true;
        offHold.SetResult(true);
        await vm.ExperimentalTeardown;

        Assert.Equal(2, hotkeys.Live.Count);
    }

    [Fact]
    public async Task Unticked_while_disconnected_the_hotkeys_go_at_once_and_nothing_is_sent()
    {
        var (vm, dll, hotkeys, gate) = Make(enabled: true);
        Assert.Equal(2, hotkeys.Live.Count);

        gate.IsEnabled = false;
        await vm.ExperimentalTeardown;
        Assert.Empty(hotkeys.Live);
        Assert.Empty(dll.Off);
    }

    [Fact]
    public async Task Unticked_while_disconnected_the_next_connect_turns_off_what_the_DLL_still_runs()
    {
        var (vm, dll, _, gate) = Make(enabled: true);
        await Connect(vm);
        await TurnAllOn(vm);
        vm.SetConnected(false);

        gate.IsEnabled = false;
        await vm.ExperimentalTeardown;
        Assert.Empty(dll.Off);   // nothing reaches the DLL while disconnected

        await Connect(vm);
        Assert.Equal(AllOff, dll.Off);
        Assert.Equal(0, dll.Foreground);
        Assert.False(dll.Fly);
        Assert.Empty(dll.Forced);
        Assert.Equal("Off", vm.StealthState);
    }

    /// <summary>The round-3 review's LOW: an untick that came after a connect, but before its prime had read the DLL,
    /// saw the flags the disconnect had reset; no connect teardown was started (the features were on), so what the
    /// prime found running afterwards stayed on behind the hidden cards.</summary>
    [Fact]
    public async Task Unticked_before_the_connect_prime_lands_what_it_finds_running_goes_off()
    {
        var (vm, dll, hotkeys, gate) = Make(enabled: true);
        dll.Fly = true;   // left by an earlier session, or a CE record
        var readHold = new TaskCompletionSource<bool>();
        dll.HoldForegroundRead = readHold;

        vm.SetConnected(true);   // the prime waits on its foreground read; Fly is read after it
        gate.IsEnabled = false;
        readHold.SetResult(true);
        await vm.ConnectPrime;
        await vm.ExperimentalTeardown;

        Assert.Contains("fly", dll.Off);
        Assert.False(dll.Fly);
        Assert.Empty(hotkeys.Live);
    }

    // ---- The round-3 review's LOW: the rules for a foreground read that failed (badge Unknown) were stated in comments
    // and checked by nothing. An untick turns Keep Foreground off whatever its badge reads, as the hook may hold behind
    // an Unknown; a connect that owes a teardown turns it off only when it was read ON, so a failed read neither costs a
    // round trip nor stamps over "Connected".

    [Theory]
    [InlineData("throw")]
    [InlineData("-1")]
    public async Task Unticked_while_connected_Keep_Foreground_goes_off_even_when_its_read_failed(string fails)
    {
        var (vm, dll, _, gate) = Make(enabled: true);
        dll.Foreground = 1;
        dll.ForegroundReadFails = fails;
        await Connect(vm);
        Assert.Equal("Unknown", vm.ForegroundLockState);

        gate.IsEnabled = false;
        await vm.ExperimentalTeardown;
        Assert.Contains("foreground", dll.Off);
        Assert.Equal(0, dll.Foreground);
    }

    [Theory]
    [InlineData("throw")]
    [InlineData("-1")]
    public async Task A_connect_owing_a_teardown_leaves_Keep_Foreground_alone_when_its_read_failed(string fails)
    {
        var (vm, dll, _, _) = Make(enabled: false, owed: true);
        vm.SetConnected(false);   // the badge back to Unknown, as after any earlier session
        dll.ForegroundReadFails = fails;

        await Connect(vm);
        Assert.Empty(dll.Off);
        Assert.Equal("Connected", vm.StatusText);
    }

    // ---- The review's MED: with no pawn the DLL answers Fly active and has_cmc false, the card reads Unavailable, and
    // the worker waits for a pawn and flies it again -- so a flight the card cannot show is still one to turn off.

    [Fact]
    public async Task Unticked_while_connected_a_flight_waiting_for_a_pawn_is_turned_off()
    {
        var (vm, dll, _, gate) = Make(enabled: true);
        await Connect(vm);
        await vm.ApplyFlyCommand.ExecuteAsync(null);
        dll.NoPawn = true;
        await vm.RefreshFlyCommand.ExecuteAsync(null);
        Assert.Equal("Unavailable", vm.FlyState);

        gate.IsEnabled = false;
        await vm.ExperimentalTeardown;
        Assert.Contains("fly", dll.Off);
        Assert.False(dll.Fly);
    }

    [Fact]
    public async Task Unticked_while_connected_with_no_pawn_and_no_flight_Fly_is_left_alone()
    {
        // The DLL's flag decides, not the missing pawn: an Unavailable badge over a worker that is not running is not
        // a flight.
        var (vm, dll, _, gate) = Make(enabled: true);
        await Connect(vm);
        dll.NoPawn = true;
        await vm.RefreshFlyCommand.ExecuteAsync(null);
        Assert.Equal("Unavailable", vm.FlyState);

        gate.IsEnabled = false;
        await vm.ExperimentalTeardown;
        Assert.DoesNotContain("fly", dll.Off);
    }

    [Fact]
    public async Task Unticked_while_disconnected_a_flight_waiting_for_a_pawn_is_turned_off_at_the_next_connect()
    {
        var (vm, dll, _, gate) = Make(enabled: true);
        await Connect(vm);
        await vm.ApplyFlyCommand.ExecuteAsync(null);
        vm.SetConnected(false);
        gate.IsEnabled = false;
        dll.NoPawn = true;

        await Connect(vm);
        Assert.Equal("Unavailable", vm.FlyState);
        Assert.Contains("fly", dll.Off);
        Assert.False(dll.Fly);
    }

    [Fact]
    public async Task A_connect_with_the_features_on_turns_nothing_off()
    {
        var (vm, dll, _, _) = Make(enabled: true);
        dll.Foreground = 1;
        dll.Fly = true;
        dll.SeeThrough = true;
        dll.HoldTheMeter();

        await Connect(vm);
        Assert.Empty(dll.Off);
        Assert.Equal("Holding @0", vm.StealthState);
    }

    [Fact]
    public async Task A_connect_that_owes_a_teardown_with_nothing_running_sends_nothing()
    {
        // Keep Foreground goes off only when the prime read it ON: an OFF at every such connect would cost a round
        // trip and stamp over its "Connected". What was owed is then paid.
        var (vm, dll, _, gate) = Make(enabled: false, owed: true);

        await Connect(vm);
        Assert.Empty(dll.Off);
        Assert.Equal("Connected", vm.StatusText);
        Assert.False(gate.TeardownOwed);
    }

    // ---- The round-3 review's LOW: the connect teardown ran at EVERY connect with the features off, not only after an
    // untick made while disconnected (the maintainer's words). Fly, Keep Foreground and See-through can be driven from
    // CE through the mailbox, so a UI connect reset a flight a CE record had started, and its record stayed ticked.

    [Fact]
    public async Task A_connect_with_the_features_off_and_no_untick_owed_leaves_what_runs_alone()
    {
        var (vm, dll, _, _) = Make(enabled: false);
        dll.Foreground = 1;
        dll.Fly = true;
        dll.SeeThrough = true;
        dll.HoldTheMeter();

        await Connect(vm);
        Assert.Empty(dll.Off);
        Assert.True(dll.Fly);
        Assert.Equal(1, dll.Foreground);
        Assert.Equal("Connected", vm.StatusText);
    }

    [Fact]
    public async Task An_untick_whose_teardown_ran_owes_nothing_to_the_next_connect()
    {
        var (vm, dll, _, gate) = Make(enabled: true);
        await Connect(vm);
        await TurnAllOn(vm);
        gate.IsEnabled = false;
        await vm.ExperimentalTeardown;
        Assert.Equal(AllOff, dll.Off);
        Assert.False(gate.TeardownOwed);

        vm.SetConnected(false);
        dll.Fly = true;   // a CE record, with the features off
        await Connect(vm);
        Assert.Equal(AllOff, dll.Off);   // nothing more
        Assert.True(dll.Fly);
    }

    [Fact]
    public async Task An_untick_made_while_disconnected_is_paid_at_the_next_connect_only()
    {
        var (vm, dll, _, gate) = Make(enabled: true);
        await Connect(vm);
        await TurnAllOn(vm);
        vm.SetConnected(false);
        gate.IsEnabled = false;
        await vm.ExperimentalTeardown;
        Assert.True(gate.TeardownOwed);

        await Connect(vm);
        Assert.Equal(AllOff, dll.Off);
        Assert.False(gate.TeardownOwed);

        vm.SetConnected(false);
        dll.Fly = true;   // a CE record, after the teardown was paid
        await Connect(vm);
        Assert.Equal(AllOff, dll.Off);
        Assert.True(dll.Fly);
    }

    [Fact]
    public async Task An_untick_whose_teardown_lost_the_connection_stays_owed()
    {
        var (vm, dll, _, gate) = Make(enabled: true);
        await Connect(vm);
        await TurnAllOn(vm);
        var offHold = new TaskCompletionSource<bool>();
        dll.HoldForegroundOff = offHold;

        gate.IsEnabled = false;
        vm.SetConnected(false);   // the pipe drops before Keep Foreground's OFF is answered
        offHold.SetResult(true);
        await vm.ExperimentalTeardown;
        Assert.True(gate.TeardownOwed);
    }

    [Fact]
    public async Task A_teardown_overtaken_by_a_later_untick_leaves_that_untick_owed()
    {
        var (vm, dll, _, gate) = Make(enabled: true);
        await Connect(vm);
        await TurnAllOn(vm);
        var first = new TaskCompletionSource<bool>();
        dll.HoldForegroundOff = first;
        gate.IsEnabled = false;
        var firstTeardown = vm.ExperimentalTeardown;
        gate.IsEnabled = true;
        var second = new TaskCompletionSource<bool>();
        dll.HoldForegroundOff = second;
        gate.IsEnabled = false;   // owed again, with a teardown of its own

        first.SetResult(true);    // the first ends while connected: it served the earlier untick, not this one
        await firstTeardown;
        vm.SetConnected(false);   // and the second loses the connection
        second.SetResult(true);
        await vm.ExperimentalTeardown;
        Assert.True(gate.TeardownOwed);
    }

    [Fact]
    public async Task A_connect_teardown_overtaken_by_a_later_connect_leaves_the_untick_owed()
    {
        var (vm, dll, _, gate) = Make(enabled: false, owed: true);
        dll.Foreground = 1;
        var off = new TaskCompletionSource<bool>();
        dll.HoldForegroundOff = off;
        vm.SetConnected(true);
        await vm.ConnectPrime;
        var firstTeardown = vm.ExperimentalTeardown;   // waits on Keep Foreground's OFF
        var read = new TaskCompletionSource<bool>();
        dll.HoldForegroundRead = read;
        vm.SetConnected(true);    // a later connect primes again, and owes the teardown itself

        off.SetResult(true);      // the first ends, connected -- but the connect it served is not the current one
        await firstTeardown;
        vm.SetConnected(false);   // and the later one's prime never lands on a connection
        read.SetResult(true);
        await vm.ConnectPrime;
        await vm.ExperimentalTeardown;
        Assert.True(gate.TeardownOwed);
    }

    [Fact]
    public async Task Ticked_before_the_connect_prime_lands_nothing_is_turned_off()
    {
        var (vm, dll, _, gate) = Make(enabled: false, owed: true);
        dll.Foreground = 1;
        var readHold = new TaskCompletionSource<bool>();
        dll.HoldForegroundRead = readHold;

        vm.SetConnected(true);
        gate.IsEnabled = true;
        readHold.SetResult(true);
        await vm.ConnectPrime;
        await vm.ExperimentalTeardown;

        Assert.Empty(dll.Off);
        Assert.Equal("ON", vm.ForegroundLockState);
    }

    [Fact]
    public async Task Two_connects_in_a_row_turn_a_feature_off_once()
    {
        // The main window calls SetConnected(true) twice on one connect: from the pipe's state handler, and again once
        // the scan has finished. The later prime reads the DLL again, so the earlier one's teardown stands aside.
        var (vm, dll, _, _) = Make(enabled: false, owed: true);
        dll.Foreground = 1;
        var readHold = new TaskCompletionSource<bool>();
        dll.HoldForegroundRead = readHold;

        vm.SetConnected(true);
        var first = vm.ExperimentalTeardown;
        vm.SetConnected(true);
        var second = vm.ExperimentalTeardown;
        readHold.SetResult(true);
        await first;
        await second;

        Assert.Equal(new[] { "foreground" }, dll.Off);
        Assert.Equal(0, dll.Foreground);
    }
}
