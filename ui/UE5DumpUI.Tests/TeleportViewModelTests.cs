using UE5DumpUI.Core;
using UE5DumpUI.Models;
using UE5DumpUI.Services;
using UE5DumpUI.ViewModels;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// VM-level tests for the Teleport panel: connection gating, error-code →
/// message mapping (incl. the -7 force flow), marker-list refresh on connect,
/// and hotkey-scheme selection. All over a fake IDumpService.
/// </summary>
public class TeleportViewModelTests
{
    private sealed class FakeDumpService : StubDumpService
    {
        public TeleportPose NextPose { get; set; } = new() { Code = 0 };
        public TeleportResult NextResult { get; set; } = new() { Code = 0, Tier = 1 };
        public List<TeleportMarker> NextMarkers { get; set; } = new();
        public TeleportPov NextPov { get; set; } = new() { Code = 0 };
        public int GetPovCalls { get; private set; }

        public ProtectState NextProtectState { get; set; } = new();
        public int GetProtectStateCalls { get; private set; }
        public override Task<ProtectState> GetProtectStateAsync(CancellationToken ct = default)
        {
            GetProtectStateCalls++;
            return Task.FromResult(NextProtectState);
        }

        public int GetPoseCalls { get; private set; }
        public int SaveCalls { get; private set; }
        public int RecallCalls { get; private set; }
        public bool LastForce { get; private set; }
        public int ClearCalls { get; private set; }
        public int CursorCalls { get; private set; }
        public int GetMarkersCalls { get; private set; }
        public int RecallLastCalls { get; private set; }
        public (double X, double Y, double Z, double? P)? LastExplicit { get; private set; }

        public int NextDebugCameraState { get; set; } = -1;
        public int SetDebugCameraCalls { get; private set; }
        public int GetDebugCameraCalls { get; private set; }
        public bool? LastSetDebugCameraEnable { get; private set; }

        public override Task<int> SetDebugCameraAsync(bool enable, CancellationToken ct = default)
        { SetDebugCameraCalls++; LastSetDebugCameraEnable = enable; return Task.FromResult(NextDebugCameraState); }

        public override Task<int> GetDebugCameraStateAsync(CancellationToken ct = default)
        { GetDebugCameraCalls++; return Task.FromResult(NextDebugCameraState); }

        public int NextGodModeState { get; set; } = -1;
        public int SetGodModeCalls { get; private set; }
        public int GetGodModeCalls { get; private set; }
        public bool? LastSetGodModeEnable { get; private set; }

        public override Task<int> SetGodModeAsync(bool enable, CancellationToken ct = default)
        { SetGodModeCalls++; LastSetGodModeEnable = enable; return Task.FromResult(NextGodModeState); }

        public override Task<int> GetGodModeAsync(CancellationToken ct = default)
        { GetGodModeCalls++; return Task.FromResult(NextGodModeState); }

        // ── Player stealth meter (Solide) ──
        public IReadOnlyList<StealthCandidate> NextStealthCandidates { get; set; } = new List<StealthCandidate>();
        public ForceFieldResult NextForce { get; set; } = new() { Held = 1, Resolved = true };
        public int FindStealthCalls { get; private set; }
        public int ForceFieldCalls { get; private set; }
        public int ResetFieldCalls { get; private set; }
        public string? LastForceClass { get; private set; }
        public string? LastForceField { get; private set; }
        public string? LastForceKind { get; private set; }
        public double LastForceValue { get; private set; }

        public override Task<IReadOnlyList<StealthCandidate>> FindStealthMeterAsync(int max = 8, CancellationToken ct = default)
        { FindStealthCalls++; return Task.FromResult(NextStealthCandidates); }

        public override Task<ForceFieldResult> ForceFieldAsync(string className, string fieldName, string kind, double value = 0, bool on = false, CancellationToken ct = default)
        { ForceFieldCalls++; LastForceClass = className; LastForceField = fieldName; LastForceKind = kind; LastForceValue = value; return Task.FromResult(NextForce); }

        public override Task<int> ResetFieldAsync(string className, string fieldName, CancellationToken ct = default)
        { ResetFieldCalls++; LastForceClass = className; LastForceField = fieldName; return Task.FromResult(0); }

        // ── Movement tuning (Laufen) ──
        public MovementParams NextMovementParams { get; set; } = new();
        public MovementSetResult NextMovementSet { get; set; } = new() { State = 1 };
        public int SetMovementCalls { get; private set; }
        public int ResetMovementCalls { get; private set; }
        public string? LastMovementKnob { get; private set; }
        public double LastMovementMultiplier { get; private set; }

        public override Task<MovementParams> GetMovementParamsAsync(CancellationToken ct = default)
            => Task.FromResult(NextMovementParams);

        public override Task<MovementSetResult> SetMovementMultiplierAsync(string knob, double multiplier, CancellationToken ct = default)
        { SetMovementCalls++; LastMovementKnob = knob; LastMovementMultiplier = multiplier; return Task.FromResult(NextMovementSet); }

        public override Task<MovementSetResult> ResetMovementAsync(string knob, CancellationToken ct = default)
        { ResetMovementCalls++; LastMovementKnob = knob; return Task.FromResult(new MovementSetResult { State = 0 }); }

        // ── Time dilation (Hemmung) ──
        public TimeState NextTimeState { get; set; } = new();
        public TimeDilationSetResult NextTimeSet { get; set; } = new() { State = 1 };
        public int SetTimeCalls { get; private set; }
        public int ResetTimeCalls { get; private set; }
        public string? LastTimeTarget { get; private set; }
        public double LastTimeValue { get; private set; }

        public override Task<TimeState> GetTimeStateAsync(CancellationToken ct = default)
            => Task.FromResult(NextTimeState);

        public override Task<TimeDilationSetResult> SetTimeDilationAsync(string target, double value, CancellationToken ct = default)
        { SetTimeCalls++; LastTimeTarget = target; LastTimeValue = value; return Task.FromResult(NextTimeSet); }

        public override Task<TimeDilationSetResult> ResetTimeDilationAsync(string target, CancellationToken ct = default)
        { ResetTimeCalls++; LastTimeTarget = target; return Task.FromResult(new TimeDilationSetResult { State = 0 }); }

        public MovementVectorResult NextGravDir { get; set; } = new() { State = 1 };
        public int SetGravDirCalls { get; private set; }
        public int ResetGravDirCalls { get; private set; }
        public double LastGravDirX { get; private set; }
        public double LastGravDirY { get; private set; }
        public double LastGravDirZ { get; private set; }

        public override Task<MovementVectorResult> SetGravityDirectionAsync(double x, double y, double z, CancellationToken ct = default)
        { SetGravDirCalls++; LastGravDirX = x; LastGravDirY = y; LastGravDirZ = z; return Task.FromResult(NextGravDir); }

        public override Task<MovementVectorResult> ResetGravityDirectionAsync(CancellationToken ct = default)
        { ResetGravDirCalls++; return Task.FromResult(new MovementVectorResult { State = 0 }); }

        // ── Fly (Dunste) ──
        public FlyStatus NextFlyStatus { get; set; } = new() { HasCmc = true };
        public int FlySetCalls { get; private set; }
        public int FlyGetStateCalls { get; private set; }
        public bool? LastFlyEnable { get; private set; }
        public double? LastFlySpeed { get; private set; }
        public int? LastFlyPreset { get; private set; }
        public bool? LastFlyNoclip { get; private set; }

        public override Task<FlyStatus> FlySetAsync(bool? enable, double? speed, int? preset, bool? noclip, CancellationToken ct = default)
        { FlySetCalls++; LastFlyEnable = enable; LastFlySpeed = speed; LastFlyPreset = preset; LastFlyNoclip = noclip; return Task.FromResult(NextFlyStatus); }

        public override Task<FlyStatus> FlyGetStateAsync(CancellationToken ct = default)
        { FlyGetStateCalls++; return Task.FromResult(NextFlyStatus); }

        // ── See-through (Schlacht) ──
        public SeeThroughStatus NextSeeThroughStatus { get; set; } = new() { Active = false };
        public int SeeThroughSetCalls { get; private set; }
        public int SeeThroughGetStateCalls { get; private set; }
        public bool? LastSeeThroughEnable { get; private set; }
        public int? LastSeeThroughCount { get; private set; }

        public override Task<SeeThroughStatus> SeeThroughSetAsync(bool? enable, int? count, CancellationToken ct = default)
        { SeeThroughSetCalls++; LastSeeThroughEnable = enable; LastSeeThroughCount = count; return Task.FromResult(NextSeeThroughStatus); }

        public override Task<SeeThroughStatus> SeeThroughGetStateAsync(CancellationToken ct = default)
        { SeeThroughGetStateCalls++; return Task.FromResult(NextSeeThroughStatus); }

        public override Task<TeleportPose> TeleportGetPoseAsync(CancellationToken ct = default)
        { GetPoseCalls++; return Task.FromResult(NextPose); }

        public override Task<TeleportPose> TeleportSaveMarkerAsync(int slot, CancellationToken ct = default)
        { SaveCalls++; return Task.FromResult(NextPose); }

        public override Task<TeleportResult> TeleportRecallMarkerAsync(int slot, bool force, CancellationToken ct = default)
        { RecallCalls++; LastForce = force; return Task.FromResult(NextResult); }

        public override Task<TeleportResult> TeleportRecallExplicitAsync(double x, double y, double z,
            double? pitch = null, double? yaw = null, double? roll = null, CancellationToken ct = default)
        { LastExplicit = (x, y, z, pitch); return Task.FromResult(NextResult); }

        public override Task<TeleportResult> TeleportToCursorAsync(double zOffset, int channel, bool fallbackCenter, CancellationToken ct = default)
        { CursorCalls++; return Task.FromResult(NextResult); }

        public override Task<List<TeleportMarker>> TeleportGetMarkersAsync(CancellationToken ct = default)
        { GetMarkersCalls++; return Task.FromResult(NextMarkers); }

        public override Task<int> TeleportClearMarkerAsync(int slot, CancellationToken ct = default)
        { ClearCalls++; return Task.FromResult(0); }

        public override Task<TeleportResult> TeleportRecallLastAsync(CancellationToken ct = default)
        { RecallLastCalls++; return Task.FromResult(NextResult); }

        public override Task<TeleportPov> TeleportGetPovAsync(CancellationToken ct = default)
        { GetPovCalls++; return Task.FromResult(NextPov); }

        public int RelativeCalls { get; private set; }
        public (double Distance, bool Horizontal)? LastRelative { get; private set; }
        public override Task<TeleportPose> TeleportRelativeAsync(double distance, bool horizontal, CancellationToken ct = default)
        { RelativeCalls++; LastRelative = (distance, horizontal); return Task.FromResult(NextPose); }

        public int NextCursorState { get; set; } = -1;
        public int SetCursorCalls { get; private set; }
        public int GetCursorCalls { get; private set; }
        public bool? LastSetCursorShow { get; private set; }
        public override Task<int> SetMouseCursorAsync(bool show, CancellationToken ct = default)
        { SetCursorCalls++; LastSetCursorShow = show; return Task.FromResult(NextCursorState); }
        public override Task<int> GetMouseCursorAsync(CancellationToken ct = default)
        { GetCursorCalls++; return Task.FromResult(NextCursorState); }
    }

    private sealed class NoopLogger : ILoggingService
    {
        public void Info(string message) { }
        public void Warn(string message) { }
        public void Error(string message) { }
        public void Error(string message, Exception ex) { }
        public void Debug(string message) { }
        public void Info(string category, string message) { }
        public void Warn(string category, string message) { }
        public void Error(string category, string message) { }
        public void Error(string category, string message, Exception ex) { }
        public void Debug(string category, string message) { }
        public void StartProcessMirror(string processName) { }
        public void StopProcessMirror() { }
    }

    private sealed class FakePlatform : IPlatformService
    {
        // Unique per-instance AppData root so the marker-hotkey store file from
        // one test never bleeds into another test's VM load (tests run parallel).
        private readonly string _dir = Path.Combine(Path.GetTempPath(),
            "ue5cd-vmtest-" + Guid.NewGuid().ToString("N"));
        public string? LastClipboard { get; private set; }
        public bool TryAcquireSingleInstance() => true;
        public void ReleaseSingleInstance() { }
        public string GetAppDataPath() => _dir;
        public string GetLogDirectoryPath() => _dir;
        public Task<bool> CopyToClipboardAsync(string text) { LastClipboard = text; return Task.FromResult(true); }
        public Task RevealInExplorerAsync(string path) => Task.CompletedTask;
        public string GetMachineName() => "TEST";
        public void CloseImeForWindow(IntPtr windowHandle) { }
        public Task<string?> ShowSaveFileDialogAsync(string defaultFileName, string filterName, string filterExtension)
            => Task.FromResult<string?>(null);
    }

    private sealed class FakeHotkeyService : IGlobalHotkeyService
    {
        public List<(uint Mods, uint Vk, string Label)> Registered { get; } = new();
        public IGlobalHotkeyRegistration? RegisterCursorHotkey(Action onPressed)
            => new FakeReg("Ctrl+F8");
        public IGlobalHotkeyRegistration? RegisterSpecific(uint modifiers, uint vk, string label, Action onPressed)
        {
            Registered.Add((modifiers, vk, label));
            return new FakeReg(label);
        }
        private sealed class FakeReg : IGlobalHotkeyRegistration
        {
            public FakeReg(string label) => Label = label;
            public string Label { get; }
            public void Dispose() { }
        }
    }

    /// <summary>Every RegisterSpecific fails (combo held by another app) — models
    /// the "saved hotkey is taken at startup" case.</summary>
    private sealed class FailingHotkeyService : IGlobalHotkeyService
    {
        public IGlobalHotkeyRegistration? RegisterCursorHotkey(Action onPressed) => null;
        public IGlobalHotkeyRegistration? RegisterSpecific(uint modifiers, uint vk, string label, Action onPressed) => null;
    }

    /// <summary>Minimal in-test IExperimentalGate — flips <see cref="IsEnabled"/> and
    /// raises Changed, mirroring the shared opt-in the Teleport cards bind to.</summary>
    private sealed class FakeExperimentalGate : IExperimentalGate
    {
        private bool _enabled;
        public FakeExperimentalGate(bool enabled = false) => _enabled = enabled;
        public bool IsEnabled
        {
            get => _enabled;
            set { if (_enabled == value) return; _enabled = value; Changed?.Invoke(this, EventArgs.Empty); }
        }
        public int SnapshotQuotaMb { get; set; } = 1024;
        public bool IsLocked => false;
        public void Lock() { }
        public event EventHandler? Changed;
    }

    private static TeleportViewModel CreateVm(FakeDumpService fake, out FakePlatform platform,
        IGlobalHotkeyService? hotkeys = null, IExperimentalGate? experimentalGate = null)
    {
        platform = new FakePlatform();
        return new TeleportViewModel(fake, new NoopLogger(), platform, aobMaker: null,
            globalHotkeys: hotkeys, experimentalGate: experimentalGate);
    }

    [Fact]
    public void Starts_disconnected_with_three_markers()
    {
        var vm = CreateVm(new FakeDumpService(), out _);
        Assert.False(vm.IsConnected);
        Assert.False(vm.CanOperate);
        Assert.Equal(3, vm.Markers.Count);
        Assert.All(vm.Markers, m => Assert.Equal("(empty)", m.Summary));
    }

    [Fact]
    public void SetConnected_refreshes_markers()
    {
        var fake = new FakeDumpService
        {
            NextMarkers = new()
            {
                new() { Slot = 0, Valid = true, X = 10, Y = 20, Z = 30, Map = "Act1" },
                new() { Slot = 1, Valid = false },
                new() { Slot = 2, Valid = false },
            },
        };
        var vm = CreateVm(fake, out _);
        vm.SetConnected(true);

        Assert.True(vm.IsConnected);
        Assert.True(vm.CanOperate);
        Assert.Equal(1, fake.GetMarkersCalls);
        Assert.True(vm.Markers[0].Valid);
        Assert.Contains("Act1", vm.Markers[0].Summary);
        Assert.False(vm.Markers[1].Valid);
    }

    [Fact]
    public async Task RefreshPose_populates_display_on_success()
    {
        var fake = new FakeDumpService
        {
            NextPose = new() { Code = 0, X = 1.5, Y = 2.5, Z = 3.5, Yaw = 90, Map = "World1", Source = "raw" },
        };
        var vm = CreateVm(fake, out _);
        vm.IsConnected = true;

        await vm.RefreshPoseCommand.ExecuteAsync(null);

        Assert.Equal("1.500", vm.PoseX);
        Assert.Equal("90.00", vm.PoseYaw);
        Assert.Equal("World1", vm.PoseMap);
        Assert.Equal("raw", vm.PoseSource);
    }

    [Fact]
    public async Task GetPov_populates_camera_display_and_pawn_delta()
    {
        var fake = new FakeDumpService
        {
            // camera at (0,0,100), pawn at (0,0,0) → delta 100; fov 90.
            NextPov = new()
            {
                Code = 0, CamX = 0, CamY = 0, CamZ = 100, Pitch = -30, Yaw = 45, Roll = 0,
                Fov = 90, Source = "raw", HasPawn = true, PawnX = 0, PawnY = 0, PawnZ = 0,
            },
        };
        var vm = CreateVm(fake, out _);
        vm.IsConnected = true;

        await vm.GetPovCommand.ExecuteAsync(null);

        Assert.Equal(1, fake.GetPovCalls);
        Assert.Equal("100.000", vm.PovZ);
        Assert.Equal("45.00", vm.PovYaw);
        Assert.Equal("90.0°", vm.PovFov);
        Assert.Equal("raw", vm.PovSource);       // cached-POV fallback surfaced
        Assert.Contains("100.0", vm.PovDelta);   // Δ to pawn
    }

    [Fact]
    public async Task GetPov_shows_hint_on_error_code()
    {
        var fake = new FakeDumpService { NextPov = new() { Code = TeleportCodes.Invoke } };
        var vm = CreateVm(fake, out _);
        vm.IsConnected = true;

        await vm.GetPovCommand.ExecuteAsync(null);

        Assert.Equal("—", vm.PovZ);              // cleared
        Assert.Contains("Game thread idle", vm.StatusText);
    }

    [Fact]
    public async Task RefreshPose_shows_hint_on_error_code()
    {
        var fake = new FakeDumpService { NextPose = new() { Code = TeleportCodes.NoPawn } };
        var vm = CreateVm(fake, out _);
        vm.IsConnected = true;

        await vm.RefreshPoseCommand.ExecuteAsync(null);

        Assert.Equal("—", vm.PoseX);
        Assert.Contains("pawn", vm.StatusText, StringComparison.OrdinalIgnoreCase);
    }

    // ── Time dilation (Hemmung) ──

    [Fact]
    public async Task ApplyWorldTime_sends_global_target_and_slider_value()
    {
        var fake = new FakeDumpService
        {
            NextTimeSet   = new() { State = 1 },
            NextTimeState = new() { Global = new() { Resolved = true, Active = true, Current = 0.5, Base = 1.0 } },
        };
        var vm = CreateVm(fake, out _);
        vm.IsConnected = true;
        vm.WorldTimeDilation = 0.5;

        await vm.ApplyWorldTimeCommand.ExecuteAsync(null);

        Assert.Equal(1, fake.SetTimeCalls);
        Assert.Equal("global", fake.LastTimeTarget);
        Assert.Equal(0.5, fake.LastTimeValue);
        Assert.Equal("ON", vm.WorldTimeState);
    }

    // ── Player stealth meter (Solide) ──

    [Fact]
    public async Task DetectStealthMeter_loads_top_candidate_and_readout()
    {
        var fake = new FakeDumpService
        {
            NextStealthCandidates = new List<StealthCandidate>
            {
                new() { ClassName = "BP_Player_C", FieldName = "Visibility", PropType = "FloatProperty", Current = 0.8, Score = 9 },
                new() { ClassName = "BP_Player_C", FieldName = "NoiseLevel", PropType = "FloatProperty", Current = 0.2, Score = 8 },
            },
        };
        var vm = CreateVm(fake, out _);
        vm.IsConnected = true;

        await vm.DetectStealthMeterCommand.ExecuteAsync(null);

        Assert.Equal(1, fake.FindStealthCalls);
        Assert.Contains("Visibility", vm.StealthFieldText);
        Assert.Equal("Ready", vm.StealthState);
    }

    [Fact]
    public async Task DetectStealthMeter_none_found_shows_amber_not_found()
    {
        var fake = new FakeDumpService();   // empty candidate list
        var vm = CreateVm(fake, out _);
        vm.IsConnected = true;

        await vm.DetectStealthMeterCommand.ExecuteAsync(null);

        Assert.Equal("Not found", vm.StealthState);
        Assert.Equal("#C9A04E", vm.StealthBadgeColor);
    }

    [Fact]
    public async Task HoldStealth_forces_detected_field_to_zero()
    {
        var fake = new FakeDumpService
        {
            NextStealthCandidates = new List<StealthCandidate>
            {
                new() { ClassName = "BP_Player_C", FieldName = "Visibility" },
            },
            NextForce = new() { Held = 2, Resolved = true },
        };
        var vm = CreateVm(fake, out _);
        vm.IsConnected = true;

        await vm.DetectStealthMeterCommand.ExecuteAsync(null);
        await vm.HoldStealthCommand.ExecuteAsync(null);

        Assert.Equal(1, fake.ForceFieldCalls);
        Assert.Equal("BP_Player_C", fake.LastForceClass);
        Assert.Equal("Visibility", fake.LastForceField);
        Assert.Equal("numeric", fake.LastForceKind);
        Assert.Equal(0.0, fake.LastForceValue);
        Assert.Equal("Holding @0", vm.StealthState);
        // Uncapped pool → the unqualified claim is honest and must survive.
        Assert.Contains("you are minimal to detection", vm.StatusText);
        Assert.DoesNotContain("UNHELD", vm.StatusText);
    }

    // "You are minimal to detection" is FALSE for every instance past the cap, so a
    // truncated pool must withdraw the claim rather than qualify it quietly. This is the
    // strongest wording in the app that a capped pool can invalidate.
    [Fact]
    public async Task HoldStealth_capped_pool_withdraws_the_undetectable_claim()
    {
        var fake = new FakeDumpService
        {
            NextStealthCandidates = new List<StealthCandidate>
            {
                new() { ClassName = "BP_Guard_C", FieldName = "Awareness" },
            },
            NextForce = new() { Held = 256, Resolved = true, Truncated = true },
        };
        var vm = CreateVm(fake, out _);
        vm.IsConnected = true;

        await vm.DetectStealthMeterCommand.ExecuteAsync(null);
        await vm.HoldStealthCommand.ExecuteAsync(null);

        Assert.DoesNotContain("you are minimal to detection", vm.StatusText);
        Assert.Contains("UNHELD", vm.StatusText);
        Assert.Contains("BP_Guard_C", vm.StatusText);
        Assert.Equal("Holding @0", vm.StealthState);   // the hold itself still succeeded
    }

    // Regression for audit #3 M9: turning the experimental gate OFF force-disabled
    // Keep-Foreground / Fly / SeeThrough but NOT an active Solide stealth hold, so its
    // re-assert worker kept writing after the (gated) Stealth card was hidden, with no
    // visible way to stop it. The gate-off teardown now releases the hold too.
    [Fact]
    public async Task ExperimentalGateOff_releases_active_stealth_hold()
    {
        var fake = new FakeDumpService
        {
            NextStealthCandidates = new List<StealthCandidate>
            {
                new() { ClassName = "BP_Enemy_C", FieldName = "Awareness" },
            },
            NextForce = new() { Held = 3, Resolved = true },
        };
        var gate = new FakeExperimentalGate(enabled: true);
        var vm = CreateVm(fake, out _, experimentalGate: gate);
        vm.IsConnected = true;

        await vm.DetectStealthMeterCommand.ExecuteAsync(null);
        await vm.HoldStealthCommand.ExecuteAsync(null);
        Assert.Equal("Holding @0", vm.StealthState);
        Assert.Equal(0, fake.ResetFieldCalls);

        gate.IsEnabled = false;   // hides the Stealth card → teardown must release the hold

        Assert.Equal(1, fake.ResetFieldCalls);
        Assert.Equal("BP_Enemy_C", fake.LastForceClass);
        Assert.Equal("Awareness", fake.LastForceField);
        Assert.Equal("Off", vm.StealthState);
    }

    // The teardown must NOT fire a needless release when no hold is active.
    [Fact]
    public async Task ExperimentalGateOff_no_stealth_hold_does_not_reset()
    {
        var fake = new FakeDumpService
        {
            NextStealthCandidates = new List<StealthCandidate>
            {
                new() { ClassName = "BP_Enemy_C", FieldName = "Awareness" },
            },
        };
        var gate = new FakeExperimentalGate(enabled: true);
        var vm = CreateVm(fake, out _, experimentalGate: gate);
        vm.IsConnected = true;

        await vm.DetectStealthMeterCommand.ExecuteAsync(null);   // "Ready", not holding
        Assert.Equal("Ready", vm.StealthState);

        gate.IsEnabled = false;

        Assert.Equal(0, fake.ResetFieldCalls);
    }

    // Regression for audit #3 L13: SetConnected(false) reset every card badge EXCEPT the
    // Stealth card, so a reconnect (possibly to a different game) showed a stale hold.
    [Fact]
    public async Task Disconnect_resets_stealth_card()
    {
        var fake = new FakeDumpService
        {
            NextStealthCandidates = new List<StealthCandidate>
            {
                new() { ClassName = "BP_Enemy_C", FieldName = "Awareness" },
            },
            NextForce = new() { Held = 1, Resolved = true },
        };
        var vm = CreateVm(fake, out _);
        vm.SetConnected(true);
        await vm.DetectStealthMeterCommand.ExecuteAsync(null);
        await vm.HoldStealthCommand.ExecuteAsync(null);
        Assert.Equal("Holding @0", vm.StealthState);

        vm.SetConnected(false);

        Assert.Equal("Off", vm.StealthState);
        Assert.Equal("—", vm.StealthFieldText);
    }

    [Fact]
    public async Task HoldStealth_zero_held_shows_not_found()
    {
        var fake = new FakeDumpService
        {
            NextStealthCandidates = new List<StealthCandidate> { new() { ClassName = "C", FieldName = "F" } },
            NextForce = new() { Held = 0, Resolved = false },
        };
        var vm = CreateVm(fake, out _);
        vm.IsConnected = true;

        await vm.DetectStealthMeterCommand.ExecuteAsync(null);
        await vm.HoldStealthCommand.ExecuteAsync(null);

        Assert.Equal("Not found", vm.StealthState);
    }

    [Fact]
    public async Task ResetStealth_releases_hold_and_resets_badge()
    {
        var fake = new FakeDumpService
        {
            NextStealthCandidates = new List<StealthCandidate> { new() { ClassName = "BP_Player_C", FieldName = "Visibility" } },
        };
        var vm = CreateVm(fake, out _);
        vm.IsConnected = true;

        await vm.DetectStealthMeterCommand.ExecuteAsync(null);
        await vm.HoldStealthCommand.ExecuteAsync(null);
        await vm.ResetStealthCommand.ExecuteAsync(null);

        Assert.Equal(1, fake.ResetFieldCalls);
        Assert.Equal("Off", vm.StealthState);
    }

    [Fact]
    public async Task ApplyPawnTime_sends_pawn_target_and_slider_value()
    {
        var fake = new FakeDumpService { NextTimeSet = new() { State = 1 } };
        var vm = CreateVm(fake, out _);
        vm.IsConnected = true;
        vm.PawnTimeDilation = 2.0;

        await vm.ApplyPawnTimeCommand.ExecuteAsync(null);

        Assert.Equal("pawn", fake.LastTimeTarget);
        Assert.Equal(2.0, fake.LastTimeValue);
    }

    [Fact]
    public async Task ApplyWorldThenPawn_holds_both_levers_independently()
    {
        // The whole point of the dual-row card: set World and Player to different
        // values; each Apply hits only its own target and neither resets the other.
        var fake = new FakeDumpService { NextTimeSet = new() { State = 1 } };
        var vm = CreateVm(fake, out _);
        vm.IsConnected = true;

        vm.WorldTimeDilation = 0.5;
        await vm.ApplyWorldTimeCommand.ExecuteAsync(null);
        Assert.Equal("global", fake.LastTimeTarget);
        Assert.Equal(0.5, fake.LastTimeValue);

        vm.PawnTimeDilation = 2.0;
        await vm.ApplyPawnTimeCommand.ExecuteAsync(null);
        Assert.Equal("pawn", fake.LastTimeTarget);
        Assert.Equal(2.0, fake.LastTimeValue);

        Assert.Equal(2, fake.SetTimeCalls);   // one Set per lever; no ResetTime calls
        Assert.Equal(0, fake.ResetTimeCalls);
    }

    [Fact]
    public async Task ApplyWorldTime_negative_state_shows_no_owner_hint()
    {
        var fake = new FakeDumpService { NextTimeSet = new() { State = -6 } };
        var vm = CreateVm(fake, out _);
        vm.IsConnected = true;

        await vm.ApplyWorldTimeCommand.ExecuteAsync(null);

        Assert.Equal("Unavailable", vm.WorldTimeState);
        Assert.Contains("WorldSettings", vm.StatusText);
    }

    [Fact]
    public async Task ResetWorldTime_restores_slider_to_normal()
    {
        var fake = new FakeDumpService();
        var vm = CreateVm(fake, out _);
        vm.IsConnected = true;
        vm.WorldTimeDilation = 0.2;

        await vm.ResetWorldTimeCommand.ExecuteAsync(null);

        Assert.Equal(1, fake.ResetTimeCalls);
        Assert.Equal("global", fake.LastTimeTarget);
        Assert.Equal(1.0, vm.WorldTimeDilation);
    }

    [Fact]
    public async Task ApplyWorldTimePreset_sets_slider_then_applies()
    {
        var fake = new FakeDumpService { NextTimeSet = new() { State = 1 } };
        var vm = CreateVm(fake, out _);
        vm.IsConnected = true;

        await vm.ApplyWorldTimePresetCommand.ExecuteAsync("0.25");

        Assert.Equal(0.25, vm.WorldTimeDilation);
        Assert.Equal(1, fake.SetTimeCalls);
        Assert.Equal(0.25, fake.LastTimeValue);
    }

    [Fact]
    public async Task ApplyPawnTimePreset_sets_pawn_slider_then_applies()
    {
        var fake = new FakeDumpService { NextTimeSet = new() { State = 1 } };
        var vm = CreateVm(fake, out _);
        vm.IsConnected = true;

        await vm.ApplyPawnTimePresetCommand.ExecuteAsync("0.5");

        Assert.Equal(0.5, vm.PawnTimeDilation);
        Assert.Equal("pawn", fake.LastTimeTarget);
        Assert.Equal(0.5, fake.LastTimeValue);
    }

    [Fact]
    public void WorldTimePercentText_reflects_slider()
    {
        var vm = CreateVm(new FakeDumpService(), out _);
        vm.WorldTimeDilation = 0.5;
        Assert.Equal("50%", vm.WorldTimePercentText);
    }

    [Fact]
    public void PawnEffectiveRateText_is_world_times_pawn()
    {
        // The player's real speed is world × pawn (UE multiplies global TimeDilation
        // into the pawn's CustomTimeDilation) — the readout surfaces that product so
        // "Whole world" slowing the player isn't a surprise.
        var vm = CreateVm(new FakeDumpService(), out _);
        vm.WorldTimeDilation = 0.5;
        vm.PawnTimeDilation = 2.0;
        // world 0.5 × pawn 2 = 1.0 (bullet time: player normal in a half-speed world)
        Assert.StartsWith("Combined player speed: 1", vm.PawnEffectiveRateText);
        Assert.Contains("world 0.5", vm.PawnEffectiveRateText);
        Assert.Contains("pawn 2", vm.PawnEffectiveRateText);
    }

    [Fact]
    public void PawnEffectiveRateText_updates_when_either_slider_changes()
    {
        var vm = CreateVm(new FakeDumpService(), out _);
        int raised = 0;
        vm.PropertyChanged += (_, e) =>
        { if (e.PropertyName == nameof(vm.PawnEffectiveRateText)) raised++; };
        vm.WorldTimeDilation = 0.25;   // world slider change must refresh the combined readout
        vm.PawnTimeDilation = 3.0;     // pawn slider change too
        Assert.True(raised >= 2);
        Assert.StartsWith("Combined player speed: 0.75", vm.PawnEffectiveRateText); // 0.25 × 3
    }

    [Fact]
    public void SetConnected_reflects_held_dilation_and_syncs_both_sliders()
    {
        // The DLL keeps holding BOTH levers as long as the game lives, so on a UI
        // reconnect the card must reflect each engaged override (badge ON + slider).
        var fake = new FakeDumpService
        {
            NextTimeState = new()
            {
                Global = new() { Resolved = true, Active = true, Current = 0.3, Base = 1.0, Value = 0.3 },
                Pawn   = new() { Resolved = true, Active = true, Current = 2.0, Base = 1.0, Value = 2.0 },
            },
        };
        var vm = CreateVm(fake, out _);
        vm.SetConnected(true);

        Assert.Equal("ON", vm.WorldTimeState);
        Assert.Equal(0.3, vm.WorldTimeDilation);   // world slider synced to the held value
        Assert.Equal("ON", vm.PawnTimeState);
        Assert.Equal(2.0, vm.PawnTimeDilation);    // pawn slider synced to the held value
    }

    [Fact]
    public void SetConnected_no_held_dilation_keeps_persisted_slider()
    {
        // Nothing engaged (menu / no world): the persisted slider preference is left
        // untouched and the badge reads Unavailable.
        var fake = new FakeDumpService { NextTimeState = new() };   // both levers Resolved=false
        var vm = CreateVm(fake, out _);
        vm.WorldTimeDilation = 0.7;   // simulate a restored preference
        vm.SetConnected(true);

        Assert.Equal(0.7, vm.WorldTimeDilation);          // unchanged
        Assert.Equal("Unavailable", vm.WorldTimeState);
    }

    [Fact]
    public async Task Recall_maps_minus7_to_force_hint_and_does_not_force()
    {
        var fake = new FakeDumpService
        {
            NextResult = new() { Code = TeleportCodes.MapMismatch, CurrentMap = "Act2", MarkerMap = "Act1" },
        };
        var vm = CreateVm(fake, out _);
        vm.IsConnected = true;

        await vm.RecallMarkerCommand.ExecuteAsync(0);

        Assert.False(fake.LastForce);
        Assert.Contains("Force", vm.StatusText);
        Assert.Contains("Act1", vm.StatusText);
    }

    [Fact]
    public async Task ForceRecall_passes_force_true()
    {
        var fake = new FakeDumpService { NextResult = new() { Code = 0, Tier = 1 } };
        var vm = CreateVm(fake, out _);
        vm.IsConnected = true;

        await vm.ForceRecallMarkerCommand.ExecuteAsync(1);

        Assert.True(fake.LastForce);
        Assert.Equal(1, fake.RecallCalls);
    }

    [Fact]
    public async Task Recall_tier2_warns_about_snap_back()
    {
        var fake = new FakeDumpService { NextResult = new() { Code = 0, Tier = 2 } };
        var vm = CreateVm(fake, out _);
        vm.IsConnected = true;

        await vm.RecallMarkerCommand.ExecuteAsync(0);

        Assert.Contains("snap back", vm.StatusText);
    }

    [Fact]
    public async Task CopyAsBugItGo_copies_and_fills_run_field()
    {
        var fake = new FakeDumpService { NextPose = new() { Code = 0, X = 100, Y = 200, Z = 300 } };
        var vm = CreateVm(fake, out var platform);
        vm.IsConnected = true;

        await vm.CopyAsBugItGoCommand.ExecuteAsync(null);

        Assert.NotNull(platform.LastClipboard);
        Assert.StartsWith("BugItGo 100.000 200.000 300.000", platform.LastClipboard);
        // Also pasted into the Run field so BugItGo can fire immediately.
        Assert.StartsWith("BugItGo 100.000 200.000 300.000", vm.BugItGoInput);
    }

    [Fact]
    public async Task RunBugItGo_empty_field_shows_message_without_parsing()
    {
        var fake = new FakeDumpService();
        var vm = CreateVm(fake, out _);
        vm.IsConnected = true;
        vm.BugItGoInput = "   ";   // whitespace only

        await vm.RunBugItGoCommand.ExecuteAsync(null);

        Assert.Null(fake.LastExplicit);
        Assert.Contains("empty", vm.StatusText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RunBugItGo_parses_and_recalls_explicit()
    {
        var fake = new FakeDumpService { NextResult = new() { Code = 0, Tier = 1 } };
        var vm = CreateVm(fake, out _);
        vm.IsConnected = true;
        vm.BugItGoInput = "BugItGo 5 6 7";

        await vm.RunBugItGoCommand.ExecuteAsync(null);

        Assert.NotNull(fake.LastExplicit);
        Assert.Equal(5, fake.LastExplicit!.Value.X, 3);
        Assert.Equal(7, fake.LastExplicit.Value.Z, 3);
    }

    [Fact]
    public async Task RunBugItGo_rejects_garbage_without_calling_dll()
    {
        var fake = new FakeDumpService();
        var vm = CreateVm(fake, out _);
        vm.IsConnected = true;
        vm.BugItGoInput = "not a coordinate";

        await vm.RunBugItGoCommand.ExecuteAsync(null);

        Assert.Null(fake.LastExplicit);
        Assert.Contains("parse", vm.StatusText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Fly_toggle_enables_then_disables()
    {
        var fake = new FakeDumpService();
        var vm = CreateVm(fake, out _);
        vm.IsConnected = true;

        fake.NextFlyStatus = new FlyStatus { HasCmc = true, Active = true, CurrentMode = 5, State = 1 };
        await vm.ApplyFlyCommand.ExecuteAsync(null);
        Assert.True(fake.LastFlyEnable);
        Assert.Equal("ON", vm.FlyState);

        fake.NextFlyStatus = new FlyStatus { HasCmc = true, Active = false, CurrentMode = 1, State = 0 };
        await vm.ResetFlyCommand.ExecuteAsync(null);
        Assert.False(fake.LastFlyEnable);
        Assert.Equal("OFF", vm.FlyState);
    }

    // ⛔ [SLICEB-2026-09-09] -- BOTH fly status lines were claims from the ATTEMPT.
    //
    // The DLL's SetEnabled writes one byte (UCharacterMovementComponent::MovementMode) and
    // that write IS the effect; everything else is bookkeeping. Macht::WriteBytes returns
    // false when VirtualProtect refuses a freed page or the memcpy faults, and both call
    // sites dropped it -- so the enable path returned 1 and logged "Fly: ENABLED" over a
    // pawn that never left its old mode, and the disable path cleared `active` and logged
    // "Fly: DISABLED" over a pawn still in MOVE_Flying with nothing tracking it.
    //
    // ⭐ THE UI HALF IS SEPARATELY WRONG, which is why these are two tests and not one:
    // ApplyFly keyed its "Fly ON" on st.HasCmc -- "a CharacterMovement was RESOLVED" -- and
    // ResetFly said "Fly OFF." unconditionally. Both would have gone on lying even after the
    // DLL started telling the truth. FlyStatus.State has carried the code all along.

    [Fact]
    public async Task Fly_enable_that_the_DLL_REFUSED_is_not_reported_as_ON()
    {
        var fake = new FakeDumpService();
        var vm = CreateVm(fake, out _);
        vm.IsConnected = true;

        // The CMC resolved (HasCmc), and the MovementMode write still failed: FR_ERR_WRITE,
        // Active false, and the pawn left in mode 1 (MOVE_Walking).
        fake.NextFlyStatus = new FlyStatus
        { HasCmc = true, Active = false, CurrentMode = 1, State = -10 };
        await vm.ApplyFlyCommand.ExecuteAsync(null);

        Assert.Equal("OFF", vm.FlyState);
        Assert.DoesNotContain("Fly ON", vm.StatusText, StringComparison.Ordinal);
        Assert.Contains("did NOT engage", vm.StatusText, StringComparison.Ordinal);
        Assert.Contains("-10", vm.StatusText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Fly_disable_whose_MovementMode_restore_FAILED_says_so()
    {
        var fake = new FakeDumpService();
        var vm = CreateVm(fake, out _);
        vm.IsConnected = true;

        // The worker stopped (Active false) but the restore write was refused, so the pawn
        // is still in mode 5 == MOVE_Flying. "Fly OFF." here is the [FREEZESTUCK] shape.
        fake.NextFlyStatus = new FlyStatus
        { HasCmc = true, Active = false, CurrentMode = 5, State = -10 };
        await vm.ResetFlyCommand.ExecuteAsync(null);

        Assert.Contains("NOT restored", vm.StatusText, StringComparison.Ordinal);
        Assert.Contains("still be flying", vm.StatusText, StringComparison.Ordinal);
        // ⭐ THE CONTROL IS IN Fly_toggle_enables_then_disables ABOVE: State = 0 must keep
        //    saying plainly "Fly OFF.", or this "fix" has simply made every disable shout.
    }

    [Fact]
    public async Task Fly_disable_that_SUCCEEDED_still_says_plainly_Fly_OFF()
    {
        var fake = new FakeDumpService();
        var vm = CreateVm(fake, out _);
        vm.IsConnected = true;

        fake.NextFlyStatus = new FlyStatus
        { HasCmc = true, Active = false, CurrentMode = 1, State = 0 };
        await vm.ResetFlyCommand.ExecuteAsync(null);

        Assert.Equal("Fly OFF.", vm.StatusText);
        Assert.Equal("OFF", vm.FlyState);
    }

    [Fact]
    public void Fly_preset_change_pushes_config_without_enable()
    {
        var fake = new FakeDumpService();
        var vm = CreateVm(fake, out _);
        vm.IsConnected = true;
        fake.NextFlyStatus = new FlyStatus { HasCmc = true, Active = false, CurrentMode = 1 };

        vm.FlyPresetIndex = 2;   // OnFlyPresetIndexChanged → PushFlyConfigAsync (config-only)

        Assert.Equal(2, fake.LastFlyPreset);
        Assert.Null(fake.LastFlyEnable);   // no enable field on a config-only push
    }

    [Fact]
    public void Fly_noclip_toggle_pushes_config_without_enable()
    {
        var fake = new FakeDumpService();
        var vm = CreateVm(fake, out _);
        vm.IsConnected = true;
        fake.NextFlyStatus = new FlyStatus { HasCmc = true, Active = false, Noclip = true };

        vm.FlyNoclip = true;   // OnFlyNoclipChanged → PushFlyConfigAsync (config-only)

        Assert.Equal(true, fake.LastFlyNoclip);
        Assert.Null(fake.LastFlyEnable);
    }

    [Fact]
    public void Hotkey_rows_cover_all_teleport_actions()
    {
        var vm = CreateVm(new FakeDumpService(), out _, new FakeHotkeyService());
        // Main card (22): 3 save + 3 recall + recall_last + bugit + bugitgo +
        // debugcam_on/off + godmode_on/off + superjump_toggle + movespeed_toggle +
        // gravity_toggle + gravdir_toggle + pov_get + relative + coords + cursor_on/off.
        Assert.Equal(22, vm.HotkeyRows.Count);
        Assert.Contains(vm.HotkeyRows, r => r.ActionId == "save0" && r.DisplayName == "Save marker 1");
        Assert.Contains(vm.HotkeyRows, r => r.ActionId == "recall2" && r.DisplayName == "Recall marker 3");
        Assert.Contains(vm.HotkeyRows, r => r.ActionId == "recall_last" && r.DisplayName == "Recall last");
        Assert.Contains(vm.HotkeyRows, r => r.ActionId == "bugit");
        Assert.Contains(vm.HotkeyRows, r => r.ActionId == "bugitgo");
        Assert.Contains(vm.HotkeyRows, r => r.ActionId == "debugcam_on" && r.DisplayName == "Debug cam ON");
        Assert.Contains(vm.HotkeyRows, r => r.ActionId == "debugcam_off" && r.DisplayName == "Debug cam OFF");
        Assert.Contains(vm.HotkeyRows, r => r.ActionId == "godmode_on" && r.DisplayName == "God Mode ON");
        Assert.Contains(vm.HotkeyRows, r => r.ActionId == "godmode_off" && r.DisplayName == "God Mode OFF");
        Assert.Contains(vm.HotkeyRows, r => r.ActionId == "superjump_toggle" && r.DisplayName == "Super Jump toggle");
        Assert.Contains(vm.HotkeyRows, r => r.ActionId == "movespeed_toggle" && r.DisplayName == "Move Speed toggle");
        Assert.Contains(vm.HotkeyRows, r => r.ActionId == "gravity_toggle" && r.DisplayName == "Gravity toggle");
        Assert.Contains(vm.HotkeyRows, r => r.ActionId == "gravdir_toggle" && r.DisplayName == "Gravity Dir toggle");
        Assert.Contains(vm.HotkeyRows, r => r.ActionId == "pov_get" && r.DisplayName == "Get POV");
        Assert.Contains(vm.HotkeyRows, r => r.ActionId == "relative" && r.DisplayName == "TP facing dir");
        Assert.Contains(vm.HotkeyRows, r => r.ActionId == "coords" && r.DisplayName == "TP to coords");
        Assert.Contains(vm.HotkeyRows, r => r.ActionId == "cursor_on" && r.DisplayName == "Cursor ON");
        Assert.Contains(vm.HotkeyRows, r => r.ActionId == "cursor_off" && r.DisplayName == "Cursor OFF");
        Assert.All(vm.HotkeyRows, r => Assert.False(r.HasBinding));

        // Experimental card (4): Fly + Keep Foreground ON/OFF + See-through, split out.
        Assert.Equal(4, vm.ExperimentalHotkeyRows.Count);
        Assert.Contains(vm.ExperimentalHotkeyRows, r => r.ActionId == "fly_toggle" && r.DisplayName == "Fly toggle");
        Assert.Contains(vm.ExperimentalHotkeyRows, r => r.ActionId == "foreground_on" && r.DisplayName == "Keep Foreground ON");
        Assert.Contains(vm.ExperimentalHotkeyRows, r => r.ActionId == "foreground_off" && r.DisplayName == "Keep Foreground OFF");
        Assert.Contains(vm.ExperimentalHotkeyRows, r => r.ActionId == "seethrough_toggle" && r.DisplayName == "See-through toggle");
        Assert.All(vm.ExperimentalHotkeyRows, r => Assert.False(r.HasBinding));
    }

    // ── God Mode / Keep Foreground "Add to CE" delivery ────────────────
    // Regression: these must NOT copy raw AA to the clipboard (a bare AA body
    // can't be pasted into a CE record). With no AOBMaker they fall back to
    // paste-able CE memory-record XML (WrapAaScriptXml).

    [Fact]
    public async Task CopyGodModeScript_without_aobmaker_copies_pasteable_ce_xml()
    {
        var vm = CreateVm(new FakeDumpService(), out var platform);   // aobMaker null → clipboard fallback

        await vm.CopyGodModeScriptCommand.ExecuteAsync(null);

        Assert.NotNull(platform.LastClipboard);
        Assert.Contains("<CheatTable>", platform.LastClipboard);
        Assert.Contains("<VariableType>Auto Assembler Script</VariableType>", platform.LastClipboard);
        Assert.False(platform.LastClipboard!.TrimStart().StartsWith("[ENABLE]", StringComparison.Ordinal),
            "clipboard must be wrapped CE XML, not a bare AA body");
    }

    [Fact]
    public async Task CopyForegroundLockScript_without_aobmaker_copies_pasteable_ce_xml()
    {
        var vm = CreateVm(new FakeDumpService(), out var platform);

        await vm.CopyForegroundLockScriptCommand.ExecuteAsync(null);

        Assert.NotNull(platform.LastClipboard);
        Assert.Contains("<CheatTable>", platform.LastClipboard);
        Assert.Contains("<VariableType>Auto Assembler Script</VariableType>", platform.LastClipboard);
        Assert.False(platform.LastClipboard!.TrimStart().StartsWith("[ENABLE]", StringComparison.Ordinal),
            "clipboard must be wrapped CE XML, not a bare AA body");
    }

    // ── Debug Camera force on/off ──────────────────────────────────────

    [Fact]
    public async Task ForceDebugCameraOn_calls_dll_and_sets_badge()
    {
        var fake = new FakeDumpService { NextDebugCameraState = 1 };
        var vm = CreateVm(fake, out _);
        vm.SetConnected(true);

        await vm.ForceDebugCameraOnCommand.ExecuteAsync(null);

        Assert.Equal(1, fake.SetDebugCameraCalls);
        Assert.True(fake.LastSetDebugCameraEnable);
        Assert.Equal("ON", vm.DebugCameraState);
    }

    [Fact]
    public async Task ForceDebugCameraOff_sends_disable_and_sets_badge()
    {
        var fake = new FakeDumpService { NextDebugCameraState = 0 };
        var vm = CreateVm(fake, out _);
        vm.SetConnected(true);

        await vm.ForceDebugCameraOffCommand.ExecuteAsync(null);

        Assert.Equal(1, fake.SetDebugCameraCalls);
        Assert.False(fake.LastSetDebugCameraEnable);
        Assert.Equal("OFF", vm.DebugCameraState);
    }

    [Fact]
    public async Task ForceGodModeOn_calls_dll_and_sets_badge()
    {
        var fake = new FakeDumpService { NextGodModeState = 1 };
        var vm = CreateVm(fake, out _);
        vm.SetConnected(true);

        await vm.ForceGodModeOnCommand.ExecuteAsync(null);

        Assert.Equal(1, fake.SetGodModeCalls);
        Assert.True(fake.LastSetGodModeEnable);
        Assert.Equal("ON", vm.GodModeState);
    }

    [Fact]
    public async Task ForceGodModeOff_sends_disable_and_sets_badge()
    {
        var fake = new FakeDumpService { NextGodModeState = 0 };
        var vm = CreateVm(fake, out _);
        vm.SetConnected(true);

        await vm.ForceGodModeOffCommand.ExecuteAsync(null);

        Assert.Equal(1, fake.SetGodModeCalls);
        Assert.False(fake.LastSetGodModeEnable);
        Assert.Equal("OFF", vm.GodModeState);
    }

    [Fact]
    public async Task ForceGodMode_does_nothing_when_disconnected()
    {
        var fake = new FakeDumpService { NextGodModeState = 1 };
        var vm = CreateVm(fake, out _);   // not connected

        await vm.ForceGodModeOnCommand.ExecuteAsync(null);

        Assert.Equal(0, fake.SetGodModeCalls);
        Assert.Equal("Unknown", vm.GodModeState);
    }

    // ══ Audit #5 AD4 — the God Mode badge told three different situations apart
    // by collapsing them into one number, so it could not.
    //
    // get_god_mode returns a single tri-state. get_protect_state has shipped in the
    // DLL since build 1251 carrying want / live / resolvable separately, with zero
    // clients. Each case below rendered as a flat "OFF" or "Unknown" before.
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>Enabled, but nothing to write to yet — the hold is armed and will
    /// engage when a pawn spawns. Used to read "Unknown", i.e. indistinguishable
    /// from a broken connection.</summary>
    [Fact]
    public async Task GodMode_wanted_but_unresolvable_reads_pending_not_unknown()
    {
        var fake = new FakeDumpService
        {
            NextProtectState = new ProtectState { Want = 1, Live = -1, Resolvable = false },
        };
        var vm = CreateVm(fake, out _);
        vm.SetConnected(true);

        await vm.RefreshGodModeCommand.ExecuteAsync(null);

        Assert.Equal("ON (pending)", vm.GodModeState);
    }

    /// <summary>Immune, but not because of us. Reporting a plain "ON" would credit
    /// the tool for a state it is not maintaining — and the badge would then flip
    /// "off" by itself when the game changed it back.</summary>
    [Fact]
    public async Task GodMode_immune_without_a_request_reads_not_held()
    {
        var fake = new FakeDumpService
        {
            NextProtectState = new ProtectState { Want = 0, Live = 1, Resolvable = true },
        };
        var vm = CreateVm(fake, out _);
        vm.SetConnected(true);

        await vm.RefreshGodModeCommand.ExecuteAsync(null);

        Assert.Equal("ON (not held)", vm.GodModeState);
    }

    /// <summary>THE CELL THAT MATTERS. The hold is engaged and resolvable, and the
    /// game just won the drift race — the re-assert worker will take it back. This
    /// read "OFF", identical to never having enabled it, which is the exact
    /// conflation the fix exists to remove. Omitting this case would have
    /// reproduced the defect inside its own repair.</summary>
    [Fact]
    public async Task GodMode_engaged_but_drifted_reads_contested_not_off()
    {
        var fake = new FakeDumpService
        {
            NextProtectState = new ProtectState { Want = 1, Live = 0, Resolvable = true },
        };
        var vm = CreateVm(fake, out _);
        vm.SetConnected(true);

        await vm.RefreshGodModeCommand.ExecuteAsync(null);

        Assert.Equal("ON (contested)", vm.GodModeState);
    }

    /// <summary>The unambiguous cells must not move — the three pre-existing
    /// assertions above depend on them.</summary>
    [Theory]
    [InlineData(1, 1, true,  "ON")]
    [InlineData(0, 0, true,  "OFF")]
    [InlineData(0, -1, false, "Unknown")]
    public async Task GodMode_unambiguous_cells_are_unchanged(
        int want, int live, bool resolvable, string expected)
    {
        var fake = new FakeDumpService
        {
            NextProtectState = new ProtectState { Want = want, Live = live, Resolvable = resolvable },
        };
        var vm = CreateVm(fake, out _);
        vm.SetConnected(true);

        await vm.RefreshGodModeCommand.ExecuteAsync(null);

        Assert.Equal(expected, vm.GodModeState);
    }

    /// <summary>The badge must reflect a hold that survived a UI reconnect without
    /// the user pressing ↻ — `want` lives in the DLL, and nothing queried it on
    /// connect (AutoTick polls pose + markers only).</summary>
    [Fact]
    public async Task Connecting_reads_the_held_protect_state_without_a_manual_refresh()
    {
        var fake = new FakeDumpService
        {
            NextProtectState = new ProtectState { Want = 1, Live = 1, Resolvable = true },
        };
        var vm = CreateVm(fake, out _);

        vm.SetConnected(true);
        await Task.Delay(50, TestContext.Current.CancellationToken);   // fire-and-forget

        Assert.True(fake.GetProtectStateCalls >= 1);
        Assert.Equal("ON", vm.GodModeState);
    }

    /// <summary>…and it must not stomp the status line or hold the busy flag —
    /// that is why it is not RefreshGodModeAsync.</summary>
    [Fact]
    public async Task Connect_time_protect_read_leaves_status_and_busy_alone()
    {
        var fake = new FakeDumpService
        {
            NextProtectState = new ProtectState { Want = 1, Live = 1, Resolvable = true },
        };
        var vm = CreateVm(fake, out _);

        vm.SetConnected(true);
        await Task.Delay(50, TestContext.Current.CancellationToken);

        Assert.False(vm.IsBusy);
        Assert.Equal("Connected", vm.StatusText);
    }

    /// <summary>A force with no pawn yet must show the REQUEST, not "Unknown":
    /// set_god_mode reports only the observed value, and the user needs to see that
    /// their toggle registered. This is the path the filed fix left behind.</summary>
    [Fact]
    public async Task ForceGodModeOn_with_no_pawn_reads_pending_not_unknown()
    {
        var fake = new FakeDumpService { NextGodModeState = -1 };
        var vm = CreateVm(fake, out _);
        vm.SetConnected(true);

        await vm.ForceGodModeOnCommand.ExecuteAsync(null);

        Assert.Equal(1, fake.SetGodModeCalls);
        Assert.Equal("ON (pending)", vm.GodModeState);
    }

    // ── Movement tuning (Laufen): Move Speed / Gravity / Super Jump ─────

    [Fact]
    public async Task ApplyMoveSpeed_sends_walk_speed_multiplier_and_sets_badge()
    {
        var fake = new FakeDumpService
        {
            NextMovementSet = new MovementSetResult { State = 1 },
            NextMovementParams = new MovementParams
            {
                HasCmc = true,
                WalkSpeed = new MovementKnob { Resolved = true, Active = true, Current = 1200, Base = 600, Multiplier = 2.0 },
            },
        };
        var vm = CreateVm(fake, out _);
        vm.SetConnected(true);
        vm.MoveSpeedExponent = 0.30103;   // 10^0.30103 ≈ 2.0×

        await vm.ApplyMoveSpeedCommand.ExecuteAsync(null);

        Assert.Equal(1, fake.SetMovementCalls);
        Assert.Equal("walk_speed", fake.LastMovementKnob);
        Assert.Equal(2.0, fake.LastMovementMultiplier, 2);   // 2 decimal places
        Assert.Equal("ON", vm.MoveSpeedState);
    }

    [Fact]
    public async Task ResetMoveSpeed_calls_dll_and_resets_slider()
    {
        var fake = new FakeDumpService
        {
            NextMovementParams = new MovementParams
            {
                HasCmc = true,
                WalkSpeed = new MovementKnob { Resolved = true, Active = false, Current = 600 },
            },
        };
        var vm = CreateVm(fake, out _);
        vm.SetConnected(true);
        vm.MoveSpeedExponent = 0.5;

        await vm.ResetMoveSpeedCommand.ExecuteAsync(null);

        Assert.Equal(1, fake.ResetMovementCalls);
        Assert.Equal("walk_speed", fake.LastMovementKnob);
        Assert.Equal(0.0, vm.MoveSpeedExponent);   // slider snapped back to 100%
        Assert.Equal("OFF", vm.MoveSpeedState);
    }

    [Fact]
    public async Task ApplyGravity_sends_gravity_knob()
    {
        var fake = new FakeDumpService
        {
            NextMovementSet = new MovementSetResult { State = 1 },
            NextMovementParams = new MovementParams
            {
                HasCmc = true,
                Gravity = new MovementKnob { Resolved = true, Active = true, Current = 0.5, Base = 1.0, Multiplier = 0.5 },
            },
        };
        var vm = CreateVm(fake, out _);
        vm.SetConnected(true);
        vm.GravityExponent = -0.30103;   // ≈ 0.5×

        await vm.ApplyGravityCommand.ExecuteAsync(null);

        Assert.Equal("gravity", fake.LastMovementKnob);
        Assert.Equal("ON", vm.GravityState);
    }

    [Fact]
    public async Task ForceSuperJumpOn_sends_jump_knob_with_sqrt_height_multiplier()
    {
        var fake = new FakeDumpService
        {
            NextMovementSet = new MovementSetResult { State = 1 },
            NextMovementParams = new MovementParams
            {
                HasCmc = true,
                Jump = new MovementKnob { Resolved = true, Active = true, Current = 840, Base = 420, Multiplier = 2.0 },
            },
        };
        var vm = CreateVm(fake, out _);
        vm.SetConnected(true);
        vm.SuperJumpExponent = 0.60206;   // 10^0.60206 ≈ 4.0× HEIGHT → √4 = 2.0× velocity

        await vm.ForceSuperJumpOnCommand.ExecuteAsync(null);

        Assert.Equal("jump", fake.LastMovementKnob);
        Assert.Equal(2.0, fake.LastMovementMultiplier, 2);   // velocity multiplier = √height
        Assert.Equal("ON", vm.SuperJumpState);
    }

    [Fact]
    public async Task ForceSuperJumpOff_resets_jump_knob()
    {
        var fake = new FakeDumpService
        {
            NextMovementParams = new MovementParams
            {
                HasCmc = true,
                Jump = new MovementKnob { Resolved = true, Active = false, Current = 420 },
            },
        };
        var vm = CreateVm(fake, out _);
        vm.SetConnected(true);

        await vm.ForceSuperJumpOffCommand.ExecuteAsync(null);

        Assert.Equal(1, fake.ResetMovementCalls);
        Assert.Equal("jump", fake.LastMovementKnob);
        Assert.Equal("OFF", vm.SuperJumpState);
    }

    [Fact]
    public async Task ResetSuperJump_turns_off_and_snaps_slider_to_100()
    {
        var fake = new FakeDumpService
        {
            NextMovementParams = new MovementParams
            {
                HasCmc = true,
                Jump = new MovementKnob { Resolved = true, Active = false, Current = 420 },
            },
        };
        var vm = CreateVm(fake, out _);
        vm.SetConnected(true);
        vm.SuperJumpExponent = 0.8;   // ~630% height

        await vm.ResetSuperJumpCommand.ExecuteAsync(null);

        Assert.Equal(1, fake.ResetMovementCalls);
        Assert.Equal("jump", fake.LastMovementKnob);
        Assert.Equal(0.0, vm.SuperJumpExponent);   // slider back to 100%
        Assert.Equal("OFF", vm.SuperJumpState);
    }

    [Fact]
    public async Task ApplyMoveSpeed_does_nothing_when_disconnected()
    {
        var fake = new FakeDumpService();
        var vm = CreateVm(fake, out _);   // not connected

        await vm.ApplyMoveSpeedCommand.ExecuteAsync(null);

        Assert.Equal(0, fake.SetMovementCalls);
    }

    [Fact]
    public async Task ApplyGravDir_sends_xyz_and_sets_badge()
    {
        var fake = new FakeDumpService
        {
            NextGravDir = new MovementVectorResult { State = 1, Resolved = true },
            NextMovementParams = new MovementParams
            {
                HasCmc = true,
                GravityDirection = new MovementVectorKnob { Resolved = true, Active = true, X = 0, Y = 0.7, Z = -0.7 },
            },
        };
        var vm = CreateVm(fake, out _);
        vm.SetConnected(true);
        vm.GravDirX = 0; vm.GravDirY = 0.7; vm.GravDirZ = -0.7;

        await vm.ApplyGravDirCommand.ExecuteAsync(null);

        Assert.Equal(1, fake.SetGravDirCalls);
        Assert.Equal(0.7, fake.LastGravDirY, 2);
        Assert.Equal(-0.7, fake.LastGravDirZ, 2);
        Assert.Equal("ON", vm.GravDirState);
    }

    [Fact]
    public async Task ResetGravDir_resets_sliders_to_down()
    {
        var fake = new FakeDumpService
        {
            NextMovementParams = new MovementParams
            {
                HasCmc = true,
                GravityDirection = new MovementVectorKnob { Resolved = true, Active = false, Z = -1 },
            },
        };
        var vm = CreateVm(fake, out _);
        vm.SetConnected(true);
        vm.GravDirX = 0.5; vm.GravDirZ = 0;

        await vm.ResetGravDirCommand.ExecuteAsync(null);

        Assert.Equal(1, fake.ResetGravDirCalls);
        Assert.Equal(0.0, vm.GravDirX);
        Assert.Equal(-1.0, vm.GravDirZ);   // sliders back to "down"
        Assert.Equal("OFF", vm.GravDirState);
    }

    [Fact]
    public async Task GravDir_unavailable_when_not_reflected()
    {
        var fake = new FakeDumpService
        {
            NextMovementParams = new MovementParams
            {
                HasCmc = true,
                GravityDirection = new MovementVectorKnob { Resolved = false },
            },
        };
        var vm = CreateVm(fake, out _);
        vm.SetConnected(true);

        await vm.RefreshGravDirCommand.ExecuteAsync(null);

        Assert.Equal("Unavailable", vm.GravDirState);   // pre-5.4 / not reflected
    }

    // ---- [W2-GRAVDIR-VERDICT] a transient absence is not a verdict about the engine ----
    //
    // The card said "needs UE5.4+ (no reflected GravityDirection)" and painted an amber Unavailable badge
    // whenever a pawn / CMC did not resolve AT THAT INSTANT -- main menu, loading, cutscene, spectator,
    // vehicle pawn -- on engines that fully support it. The apply status checked `resolved` first, and
    // that comes from a fresh read which is false with no pawn as well.

    [Fact]
    public async Task GravDir_readout_without_a_pawn_is_Unknown_not_a_version_verdict()
    {
        var fake = new FakeDumpService { NextMovementParams = new MovementParams { HasCmc = false } };
        var vm = CreateVm(fake, out _);
        vm.SetConnected(true);

        await vm.RefreshGravDirCommand.ExecuteAsync(null);

        Assert.Equal("Unknown", vm.GravDirState);
        Assert.DoesNotContain("UE5.4", vm.GravDirCurrentText);
        Assert.DoesNotContain("UE5.4", vm.StatusText);
    }

    [Fact]
    public async Task ApplyGravDir_without_a_pawn_says_so_not_needs_UE54()
    {
        var fake = new FakeDumpService
        {
            NextGravDir = new MovementVectorResult { State = -3, Resolved = false },   // MR_ERR_NO_PAWN
            NextMovementParams = new MovementParams { HasCmc = false },
        };
        var vm = CreateVm(fake, out _);
        vm.SetConnected(true);

        await vm.ApplyGravDirCommand.ExecuteAsync(null);

        Assert.DoesNotContain("UE5.4", vm.StatusText);
        Assert.Contains("enter gameplay", vm.StatusText);
        Assert.Equal("Unknown", vm.GravDirState);
    }

    [Fact]
    public async Task ApplyGravDir_on_a_pre_UE54_engine_still_says_so()
    {
        // The control, green before and after: a CMC without a reflected GravityDirection.
        var fake = new FakeDumpService
        {
            NextGravDir = new MovementVectorResult { State = -4, Resolved = false },   // MR_ERR_REFLECT
            NextMovementParams = new MovementParams
            {
                HasCmc = true, GravityDirection = new MovementVectorKnob { Resolved = false },
            },
        };
        var vm = CreateVm(fake, out _);
        vm.SetConnected(true);

        await vm.ApplyGravDirCommand.ExecuteAsync(null);

        Assert.Contains("UE5.4", vm.StatusText);
        Assert.Equal("Unavailable", vm.GravDirState);
    }

    [Theory]
    [InlineData(-4, false, false)]   // MR_ERR_REFLECT, but the fresh read finds no live CMC: ResolveCtx also
                                     // returns -4 when the pawn / CMC class lookup fails
    [InlineData(-4, true,  true)]    // MR_ERR_REFLECT from a failed vector read: the field IS reflected
    [InlineData(-3, true,  false)]   // the SET saw no pawn; a (pre-5.4) pawn spawned before the read
    public async Task ApplyGravDir_says_needs_UE54_only_when_both_signals_agree(int state, bool hasCmc, bool resolved)
    {
        // -4 alone is not the pre-5.4 verdict (Laufen.cpp ResolveCtx and the ReadVec3At fallback return it
        // too), and the fresh read alone reports a later instant than the set. The verdict needs both.
        var fake = new FakeDumpService
        {
            NextGravDir = new MovementVectorResult { State = state, Resolved = resolved },
            NextMovementParams = new MovementParams
            {
                HasCmc = hasCmc, GravityDirection = new MovementVectorKnob { Resolved = resolved },
            },
        };
        var vm = CreateVm(fake, out _);
        vm.SetConnected(true);

        await vm.ApplyGravDirCommand.ExecuteAsync(null);

        Assert.DoesNotContain("UE5.4", vm.StatusText);
    }

    [Fact]
    public async Task LocateGravDir_without_a_pawn_is_not_a_version_verdict()
    {
        // The review of B21 found a third entrance: Locate re-read the params, painted the right
        // Unknown badge, then overwrote the status with "needs UE5.4+".
        var fake = new FakeDumpService { NextMovementParams = new MovementParams { HasCmc = false } };
        var vm = CreateVm(fake, out _);
        vm.SetConnected(true);

        await vm.LocateGravDirInGWorldCommand.ExecuteAsync(null);

        Assert.DoesNotContain("UE5.4", vm.StatusText);
        Assert.Contains("enter gameplay", vm.StatusText);
    }

    [Fact]
    public async Task LocateGravDir_on_a_pre_UE54_engine_still_says_so()
    {
        // The control, green before and after: a CMC without a reflected GravityDirection.
        var fake = new FakeDumpService
        {
            NextMovementParams = new MovementParams
            {
                HasCmc = true, GravityDirection = new MovementVectorKnob { Resolved = false },
            },
        };
        var vm = CreateVm(fake, out _);
        vm.SetConnected(true);

        await vm.LocateGravDirInGWorldCommand.ExecuteAsync(null);

        Assert.Contains("UE5.4", vm.StatusText);
    }

    // ---- [W2-MS-PROMISE] a refused apply must not promise a queued override ----

    [Fact]
    public async Task ApplyMoveSpeed_without_a_pawn_promises_nothing()
    {
        var fake = new FakeDumpService
        {
            NextMovementSet = new MovementSetResult { State = -3 },   // MR_ERR_NO_PAWN: Laufen stored nothing
            NextMovementParams = new MovementParams { HasCmc = false },
        };
        var vm = CreateVm(fake, out _);
        vm.SetConnected(true);

        await vm.ApplyMoveSpeedCommand.ExecuteAsync(null);

        Assert.DoesNotContain("applies once", vm.StatusText);
        Assert.Contains("enter gameplay first", vm.StatusText);
    }

    [Theory]
    [InlineData(true)]    // the pawn lane
    [InlineData(false)]   // the world lane
    public async Task ApplyTime_without_its_owner_promises_nothing(bool pawnLane)
    {
        // The twin found while fixing [W2-MS-PROMISE]: Hemmung::SetDilation also returns before storing
        // anything when its owner does not resolve, so nothing is queued to "apply once" it exists.
        var fake = new FakeDumpService { NextTimeSet = new TimeDilationSetResult { State = -3 } };
        var vm = CreateVm(fake, out _);
        vm.SetConnected(true);

        await (pawnLane ? vm.ApplyPawnTimeCommand : vm.ApplyWorldTimeCommand).ExecuteAsync(null);

        Assert.DoesNotContain("applies once", vm.StatusText);
    }

    [Fact]
    public async Task ForceDebugCamera_does_nothing_when_disconnected()
    {
        var fake = new FakeDumpService { NextDebugCameraState = 1 };
        var vm = CreateVm(fake, out _);   // not connected

        await vm.ForceDebugCameraOnCommand.ExecuteAsync(null);

        Assert.Equal(0, fake.SetDebugCameraCalls);
        Assert.Equal("Unknown", vm.DebugCameraState);
    }

    [Fact]
    public async Task Disconnect_resets_debug_camera_badge()
    {
        var fake = new FakeDumpService { NextDebugCameraState = 1 };
        var vm = CreateVm(fake, out _);
        vm.SetConnected(true);
        await vm.ForceDebugCameraOnCommand.ExecuteAsync(null);
        Assert.Equal("ON", vm.DebugCameraState);

        vm.SetConnected(false);

        Assert.Equal("Unknown", vm.DebugCameraState);
    }

    // ── Directional teleport ───────────────────────────────────────────

    [Fact]
    public async Task TeleportRelative_passes_distance_and_mode_and_applies_pose()
    {
        var fake = new FakeDumpService
        {
            NextPose = new() { Code = 0, X = 7, Y = 8, Z = 9, Yaw = 45 },
        };
        var vm = CreateVm(fake, out _);
        vm.IsConnected = true;
        vm.RelativeDistance = 250;
        vm.RelativeHorizontal = false;

        await vm.TeleportRelativeCommand.ExecuteAsync(null);

        Assert.Equal(1, fake.RelativeCalls);
        Assert.Equal((250, false), fake.LastRelative);
        Assert.Equal("7.000", vm.PoseX);          // landed pose surfaced
        Assert.Contains("Teleported", vm.StatusText);
    }

    [Fact]
    public async Task TeleportRelative_does_nothing_when_disconnected()
    {
        var fake = new FakeDumpService();
        var vm = CreateVm(fake, out _);   // not connected

        await vm.TeleportRelativeCommand.ExecuteAsync(null);

        Assert.Equal(0, fake.RelativeCalls);
    }

    // ── Teleport to explicit coordinates ───────────────────────────────

    [Fact]
    public async Task TeleportToCoords_without_rotation_passes_xyz_only()
    {
        var fake = new FakeDumpService { NextResult = new() { Code = 0, Tier = 1 } };
        var vm = CreateVm(fake, out _);
        vm.IsConnected = true;
        vm.CoordX = 11; vm.CoordY = 22; vm.CoordZ = 33;
        vm.CoordSetRotation = false;

        await vm.TeleportToCoordsCommand.ExecuteAsync(null);

        Assert.NotNull(fake.LastExplicit);
        Assert.Equal(11, fake.LastExplicit!.Value.X, 3);
        Assert.Equal(33, fake.LastExplicit.Value.Z, 3);
        Assert.Null(fake.LastExplicit.Value.P);   // rotation omitted
        Assert.Contains("Teleported", vm.StatusText);
    }

    [Fact]
    public async Task TeleportToCoords_with_rotation_passes_pitch()
    {
        var fake = new FakeDumpService { NextResult = new() { Code = 0, Tier = 1 } };
        var vm = CreateVm(fake, out _);
        vm.IsConnected = true;
        vm.CoordX = 1; vm.CoordY = 2; vm.CoordZ = 3;
        vm.CoordSetRotation = true;
        vm.CoordPitch = -15;

        await vm.TeleportToCoordsCommand.ExecuteAsync(null);

        Assert.NotNull(fake.LastExplicit);
        Assert.Equal(-15, fake.LastExplicit!.Value.P!.Value, 3);
    }

    [Fact]
    public async Task FillCoordsFromCurrent_populates_coord_fields()
    {
        var fake = new FakeDumpService
        {
            NextPose = new() { Code = 0, X = 100, Y = 200, Z = 300, Pitch = 5, Yaw = 10, Roll = 0 },
        };
        var vm = CreateVm(fake, out _);
        vm.IsConnected = true;

        await vm.FillCoordsFromCurrentCommand.ExecuteAsync(null);

        Assert.Equal(100, vm.CoordX);
        Assert.Equal(300, vm.CoordZ);
        Assert.Equal(10, vm.CoordYaw);
    }

    // ── Force mouse cursor ─────────────────────────────────────────────

    [Fact]
    public async Task ForceCursorOn_calls_dll_and_sets_badge()
    {
        var fake = new FakeDumpService { NextCursorState = 1 };
        var vm = CreateVm(fake, out _);
        vm.SetConnected(true);

        await vm.ForceCursorOnCommand.ExecuteAsync(null);

        Assert.Equal(1, fake.SetCursorCalls);
        Assert.True(fake.LastSetCursorShow);
        Assert.Equal("ON", vm.MouseCursorState);
    }

    [Fact]
    public async Task ForceCursorOff_sends_hide_and_sets_badge()
    {
        var fake = new FakeDumpService { NextCursorState = 0 };
        var vm = CreateVm(fake, out _);
        vm.SetConnected(true);

        await vm.ForceCursorOffCommand.ExecuteAsync(null);

        Assert.False(fake.LastSetCursorShow);
        Assert.Equal("OFF", vm.MouseCursorState);
    }

    [Fact]
    public async Task RefreshCursor_reads_live_state()
    {
        var fake = new FakeDumpService { NextCursorState = 1 };
        var vm = CreateVm(fake, out _);
        vm.SetConnected(true);
        // Connect now PRIMES every badge the disconnect branch resets, the cursor
        // included ([BADGEPRIME-2026-09-10]), so wait for that and start the count from
        // zero. Asserting ">= 1" instead would have hidden the duplicate-call regression
        // this test exists to catch.
        await vm.ConnectPrime;
        int before = fake.GetCursorCalls;

        await vm.RefreshCursorCommand.ExecuteAsync(null);

        Assert.Equal(before + 1, fake.GetCursorCalls);
        Assert.Equal("ON", vm.MouseCursorState);
    }

    [Fact]
    public async Task Disconnect_resets_cursor_badge()
    {
        var fake = new FakeDumpService { NextCursorState = 1 };
        var vm = CreateVm(fake, out _);
        vm.SetConnected(true);
        await vm.ForceCursorOnCommand.ExecuteAsync(null);
        Assert.Equal("ON", vm.MouseCursorState);

        vm.SetConnected(false);

        Assert.Equal("Unknown", vm.MouseCursorState);
    }

    // ── Startup hotkey-conflict surfacing ──────────────────────────────

    [Fact]
    public void Saved_hotkey_taken_at_startup_is_flagged_not_silently_dropped()
    {
        var platform = new FakePlatform();
        // Pre-seed a saved binding so the ctor's LoadAndRegisterHotkeys tries it.
        new TeleportHotkeyStore(platform).Save(new Dictionary<string, TeleportHotkeyBinding>
        {
            ["save0"] = new TeleportHotkeyBinding(HotkeyModifiers.Control, 0x74 /*F5*/),
        });

        var vm = new TeleportViewModel(new FakeDumpService(), new NoopLogger(), platform,
            aobMaker: null, globalHotkeys: new FailingHotkeyService());

        var row = vm.HotkeyRows.First(r => r.ActionId == "save0");
        Assert.True(row.Conflicted);              // flagged, not dropped
        Assert.True(row.HasBinding);              // label still shown
        Assert.Contains("⚠", row.DisplayLabel);
        Assert.True(vm.HasHotkeyWarning);         // top banner is set
    }

    [Fact]
    public void Clearing_a_conflicted_hotkey_clears_the_warning()
    {
        var platform = new FakePlatform();
        new TeleportHotkeyStore(platform).Save(new Dictionary<string, TeleportHotkeyBinding>
        {
            ["save0"] = new TeleportHotkeyBinding(HotkeyModifiers.Control, 0x74),
        });
        var vm = new TeleportViewModel(new FakeDumpService(), new NoopLogger(), platform,
            aobMaker: null, globalHotkeys: new FailingHotkeyService());
        var row = vm.HotkeyRows.First(r => r.ActionId == "save0");
        Assert.True(vm.HasHotkeyWarning);

        vm.ClearHotkeyCommand.Execute(row);

        Assert.False(row.Conflicted);
        Assert.False(row.HasBinding);
        Assert.False(vm.HasHotkeyWarning);
    }

    // ── Experimental gating (Keep Foreground / Fly / Standalone trainer) ──

    [Fact]
    public void Experimental_feature_hotkeys_live_in_their_own_collection()
    {
        var vm = CreateVm(new FakeDumpService(), out _, hotkeys: new FakeHotkeyService());

        // The three trainer-flavoured hotkeys moved out of the main card…
        foreach (var id in new[] { "foreground_on", "foreground_off", "fly_toggle" })
        {
            Assert.Contains(vm.ExperimentalHotkeyRows, r => r.ActionId == id);
            Assert.DoesNotContain(vm.HotkeyRows, r => r.ActionId == id);
        }
        // …while the ordinary ones stay in the main card.
        Assert.Contains(vm.HotkeyRows, r => r.ActionId == "save0");
        Assert.DoesNotContain(vm.ExperimentalHotkeyRows, r => r.ActionId == "save0");
    }

    [Fact]
    public void Experimental_disabled_hides_cards_and_hotkey_card()
    {
        var vm = CreateVm(new FakeDumpService(), out _, hotkeys: new FakeHotkeyService(),
            experimentalGate: new FakeExperimentalGate(enabled: false));

        Assert.False(vm.ExperimentalEnabled);       // feature cards hidden
        Assert.False(vm.ShowExperimentalHotkeys);   // hotkey card hidden
    }

    [Fact]
    public void Experimental_enabled_shows_cards_and_hotkey_card_when_hotkeys_available()
    {
        var vm = CreateVm(new FakeDumpService(), out _, hotkeys: new FakeHotkeyService(),
            experimentalGate: new FakeExperimentalGate(enabled: true));

        Assert.True(vm.ExperimentalEnabled);
        Assert.True(vm.ShowExperimentalHotkeys);
    }

    [Fact]
    public void Experimental_hotkey_card_stays_hidden_without_a_hotkey_service()
    {
        // Enabled gate but no hotkey service (headless) → cards show, hotkey card doesn't.
        var vm = CreateVm(new FakeDumpService(), out _, hotkeys: null,
            experimentalGate: new FakeExperimentalGate(enabled: true));

        Assert.True(vm.ExperimentalEnabled);
        Assert.False(vm.ShowExperimentalHotkeys);   // needs CanBindCursorHotkey too
    }

    [Fact]
    public void Toggling_gate_raises_property_changed_for_visibility()
    {
        var gate = new FakeExperimentalGate(enabled: false);
        var vm = CreateVm(new FakeDumpService(), out _, hotkeys: new FakeHotkeyService(),
            experimentalGate: gate);
        var raised = new List<string>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName ?? "");

        gate.IsEnabled = true;

        Assert.Contains(nameof(vm.ExperimentalEnabled), raised);
        Assert.Contains(nameof(vm.ShowExperimentalHotkeys), raised);
        Assert.True(vm.ShowExperimentalHotkeys);
    }

    [Fact]
    public void Experimental_hotkey_not_registered_while_gate_off_but_combo_still_shown()
    {
        var platform = new FakePlatform();
        new TeleportHotkeyStore(platform).Save(new Dictionary<string, TeleportHotkeyBinding>
        {
            ["fly_toggle"] = new TeleportHotkeyBinding(HotkeyModifiers.Control, 0x77 /*F8*/),
        });
        var hotkeys = new FakeHotkeyService();

        var vm = new TeleportViewModel(new FakeDumpService(), new NoopLogger(), platform,
            aobMaker: null, globalHotkeys: hotkeys, experimentalGate: new FakeExperimentalGate(enabled: false));

        // Gate off → the combo is NOT grabbed globally…
        Assert.DoesNotContain(hotkeys.Registered, r => r.Vk == 0x77);
        // …but the saved combo still shows in the (hidden) row so it isn't lost.
        var row = vm.ExperimentalHotkeyRows.First(r => r.ActionId == "fly_toggle");
        Assert.True(row.HasBinding);
    }

    [Fact]
    public void Experimental_hotkey_registered_when_gate_on_at_startup()
    {
        var platform = new FakePlatform();
        new TeleportHotkeyStore(platform).Save(new Dictionary<string, TeleportHotkeyBinding>
        {
            ["fly_toggle"] = new TeleportHotkeyBinding(HotkeyModifiers.Control, 0x77 /*F8*/),
        });
        var hotkeys = new FakeHotkeyService();

        _ = new TeleportViewModel(new FakeDumpService(), new NoopLogger(), platform,
            aobMaker: null, globalHotkeys: hotkeys, experimentalGate: new FakeExperimentalGate(enabled: true));

        Assert.Contains(hotkeys.Registered, r => r.Vk == 0x77);
    }

    [Fact]
    public void Enabling_gate_registers_experimental_hotkey_that_was_gated_off()
    {
        var platform = new FakePlatform();
        new TeleportHotkeyStore(platform).Save(new Dictionary<string, TeleportHotkeyBinding>
        {
            ["fly_toggle"] = new TeleportHotkeyBinding(HotkeyModifiers.Control, 0x77 /*F8*/),
        });
        var hotkeys = new FakeHotkeyService();
        var gate = new FakeExperimentalGate(enabled: false);
        var vm = new TeleportViewModel(new FakeDumpService(), new NoopLogger(), platform,
            aobMaker: null, globalHotkeys: hotkeys, experimentalGate: gate);
        Assert.DoesNotContain(hotkeys.Registered, r => r.Vk == 0x77);

        gate.IsEnabled = true;   // user ticks the opt-in

        Assert.Contains(hotkeys.Registered, r => r.Vk == 0x77);
    }

    // ── See-through occluders (Schlacht) ──

    [Fact]
    public async Task ApplySeeThrough_enables_and_reflects_active_state()
    {
        var fake = new FakeDumpService { NextSeeThroughStatus = new() { Active = true, HasTarget = true, HiddenCount = 2, PierceCount = 3 } };
        var vm = CreateVm(fake, out _);
        vm.IsConnected = true;
        vm.SeeThroughPierce = 3;

        await vm.ApplySeeThroughCommand.ExecuteAsync(null);

        Assert.Equal(1, fake.SeeThroughSetCalls);
        Assert.True(fake.LastSeeThroughEnable);
        Assert.Equal(3, fake.LastSeeThroughCount);   // pierce depth threaded through
        Assert.Equal("ON", vm.SeeThroughState);
    }

    // The DLL refuses to enable See-through when the game-thread hook is down,
    // because its ~10 Hz tracing invokes would otherwise run off the game thread.
    // Nothing visibly happens on a refusal, so the card MUST say why — and must say
    // it is retryable, since a MinHook trampoline-allocation failure is a VM-layout
    // accident, not a property of the game.
    [Fact]
    public async Task ApplySeeThrough_refused_for_no_hook_explains_and_offers_a_retry()
    {
        var fake = new FakeDumpService
        {
            NextSeeThroughStatus = new() { Active = false, Code = -5, State = -5, HookActive = false }
        };
        var vm = CreateVm(fake, out _);
        vm.IsConnected = true;

        await vm.ApplySeeThroughCommand.ExecuteAsync(null);

        Assert.Equal("Unavailable", vm.SeeThroughState);
        Assert.Contains("Game-thread hook unavailable", vm.SeeThroughCurrentText);
        Assert.Contains("retry", vm.SeeThroughCurrentText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("retry", vm.StatusText, StringComparison.OrdinalIgnoreCase);
    }

    // Once the hook comes back, a Refresh must CLEAR the warning rather than leave
    // the card looking broken — the recovery half of "reflect failure/restore".
    [Fact]
    public async Task RefreshSeeThrough_after_the_hook_recovers_drops_the_warning()
    {
        var fake = new FakeDumpService
        {
            NextSeeThroughStatus = new() { Active = false, Code = -5, State = -5, HookActive = false }
        };
        var vm = CreateVm(fake, out _);
        vm.IsConnected = true;
        await vm.ApplySeeThroughCommand.ExecuteAsync(null);
        Assert.Equal("Unavailable", vm.SeeThroughState);

        // Hook recovered on a later attempt; the stale refusal code is still there.
        fake.NextSeeThroughStatus = new() { Active = false, Code = -5, HookActive = true };
        await vm.RefreshSeeThroughCommand.ExecuteAsync(null);

        Assert.Equal("OFF", vm.SeeThroughState);
        Assert.DoesNotContain("hook", vm.SeeThroughCurrentText, StringComparison.OrdinalIgnoreCase);
    }

    // A fresh session has never invoked anything, so the hook is legitimately not
    // installed yet. That must NOT read as a failure.
    [Fact]
    public async Task Lazy_hook_not_yet_installed_is_not_reported_as_unavailable()
    {
        var fake = new FakeDumpService
        {
            NextSeeThroughStatus = new() { Active = false, Code = 0, HookActive = false }
        };
        var vm = CreateVm(fake, out _);
        vm.IsConnected = true;

        await vm.RefreshSeeThroughCommand.ExecuteAsync(null);

        Assert.Equal("OFF", vm.SeeThroughState);
        Assert.DoesNotContain("hook", vm.SeeThroughCurrentText, StringComparison.OrdinalIgnoreCase);
    }

    // Disabling while the game thread is paused CANNOT un-hide; the DLL keeps the
    // record and reports the leftover in HiddenCount. Observed live on Elliot --
    // clicking in the UI backgrounds the game, which is exactly when this fires -- so
    // reporting only "OFF" would leave an actor invisible with no hint why.
    [Fact]
    public async Task ResetSeeThrough_with_actors_left_hidden_says_so_and_how_to_recover()
    {
        var fake = new FakeDumpService
        {
            NextSeeThroughStatus = new() { Active = false, HiddenCount = 1 }
        };
        var vm = CreateVm(fake, out _);
        vm.IsConnected = true;

        await vm.ResetSeeThroughCommand.ExecuteAsync(null);

        Assert.Equal("OFF", vm.SeeThroughState);
        Assert.Contains("still hidden", vm.StatusText);
        Assert.Contains("still hidden", vm.SeeThroughCurrentText);
        // Recovery is AUTOMATIC now (the DLL waits for the game thread), so the message
        // must say that rather than hand the user a chore -- and it points at the
        // feature that avoids the situation entirely.
        Assert.Contains("automatically", vm.StatusText);
        Assert.Contains("Keep Foreground", vm.StatusText);
        Assert.Contains("Keep Foreground", vm.SeeThroughCurrentText);
    }

    [Fact]
    public async Task Changing_pierce_depth_while_active_pushes_it_live()
    {
        var fake = new FakeDumpService { NextSeeThroughStatus = new() { Active = true, HasTarget = true } };
        var vm = CreateVm(fake, out _);
        vm.IsConnected = true;
        await vm.ApplySeeThroughCommand.ExecuteAsync(null);   // now active
        int callsBefore = fake.SeeThroughSetCalls;

        vm.SeeThroughPierce = 4;                              // live change

        Assert.True(fake.SeeThroughSetCalls > callsBefore);   // pushed
        Assert.Equal(4, fake.LastSeeThroughCount);
        Assert.Null(fake.LastSeeThroughEnable);               // config-only (no enable field)
    }

    [Fact]
    public async Task ResetSeeThrough_disables_and_reflects_off_state()
    {
        var fake = new FakeDumpService { NextSeeThroughStatus = new() { Active = false } };
        var vm = CreateVm(fake, out _);
        vm.IsConnected = true;

        await vm.ResetSeeThroughCommand.ExecuteAsync(null);

        Assert.Equal(1, fake.SeeThroughSetCalls);
        Assert.False(fake.LastSeeThroughEnable);
        Assert.Equal("OFF", vm.SeeThroughState);
    }

    [Fact]
    public void SeeThrough_hotkey_is_experimental_and_in_its_own_collection()
    {
        var vm = CreateVm(new FakeDumpService(), out _, hotkeys: new FakeHotkeyService());
        Assert.Contains(vm.ExperimentalHotkeyRows, r => r.ActionId == "seethrough_toggle");
        Assert.DoesNotContain(vm.HotkeyRows, r => r.ActionId == "seethrough_toggle");
    }

    [Fact]
    public async Task RecallLast_calls_dll_and_reports_success()
    {
        var fake = new FakeDumpService { NextResult = new() { Code = 0, Tier = 1 } };
        var vm = CreateVm(fake, out _);
        vm.IsConnected = true;

        await vm.RecallLastCommand.ExecuteAsync(null);

        Assert.Equal(1, fake.RecallLastCalls);
        Assert.Contains("last position", vm.StatusText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RecallLast_empty_slot_explains_auto_save()
    {
        var fake = new FakeDumpService { NextResult = new() { Code = TeleportCodes.EmptyMarker } };
        var vm = CreateVm(fake, out _);
        vm.IsConnected = true;

        await vm.RecallLastCommand.ExecuteAsync(null);

        Assert.Contains("auto-save", vm.StatusText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SetConnected_routes_last_sentinel_to_last_display()
    {
        var fake = new FakeDumpService
        {
            NextMarkers = new()
            {
                new() { Slot = 0, Valid = false },
                new() { Slot = 1, Valid = false },
                new() { Slot = 2, Valid = false },
                // slot -1 = system "last" sentinel (Fern get_markers).
                new() { Slot = -1, Valid = true, X = 12, Y = 3, Z = 80, Map = "World1" },
            },
        };
        var vm = CreateVm(fake, out _);
        vm.SetConnected(true);

        Assert.True(vm.LastValid);
        Assert.True(vm.CanRecallLast);
        Assert.Contains("World1", vm.LastSummary);
        // The sentinel must not leak into the 3 real marker rows.
        Assert.Equal(3, vm.Markers.Count);
        Assert.All(vm.Markers, m => Assert.False(m.Valid));
    }

    [Fact]
    public void Capture_assigns_combo_and_registers_global_hotkey()
    {
        var hk = new FakeHotkeyService();
        var vm = CreateVm(new FakeDumpService(), out _, hk);
        var row = vm.HotkeyRows.First(r => r.ActionId == "save0");

        vm.BeginCaptureCommand.Execute(row);
        Assert.True(vm.IsCapturingHotkey);

        // Hold Ctrl, press F7 → Ctrl+F7 (Win32 MOD_CONTROL=2, VK_F7=0x76).
        bool handled = vm.ApplyCapturedKey(Avalonia.Input.Key.F7, Avalonia.Input.KeyModifiers.Control);

        Assert.True(handled);
        Assert.False(vm.IsCapturingHotkey);
        Assert.Equal("Ctrl+F7", row.Label);
        Assert.Contains(hk.Registered, x => x.Mods == 2 && x.Vk == 0x76 && x.Label == "Ctrl+F7");
    }

    [Fact]
    public void Capture_modifier_only_key_keeps_listening()
    {
        var vm = CreateVm(new FakeDumpService(), out _, new FakeHotkeyService());
        var row = vm.HotkeyRows.First();
        vm.BeginCaptureCommand.Execute(row);

        // LeftCtrl alone is not a bindable key → not handled, still capturing.
        bool handled = vm.ApplyCapturedKey(Avalonia.Input.Key.LeftCtrl, Avalonia.Input.KeyModifiers.Control);
        Assert.False(handled);
        Assert.True(vm.IsCapturingHotkey);
    }

    [Fact]
    public void BeginCapture_toggles_off_when_clicked_again()
    {
        var vm = CreateVm(new FakeDumpService(), out _, new FakeHotkeyService());
        var row = vm.HotkeyRows.First();

        vm.BeginCaptureCommand.Execute(row);   // start
        Assert.True(vm.IsCapturingHotkey);
        Assert.Equal("Cancel", row.CaptureButtonText);

        vm.BeginCaptureCommand.Execute(row);   // click "Cancel" → abort
        Assert.False(vm.IsCapturingHotkey);
        Assert.Equal("Set", row.CaptureButtonText);
        Assert.False(row.HasBinding);
    }

    [Fact]
    public void Capture_escape_cancels()
    {
        var vm = CreateVm(new FakeDumpService(), out _, new FakeHotkeyService());
        var row = vm.HotkeyRows.First();
        vm.BeginCaptureCommand.Execute(row);

        bool handled = vm.ApplyCapturedKey(Avalonia.Input.Key.Escape, Avalonia.Input.KeyModifiers.None);
        Assert.True(handled);
        Assert.False(vm.IsCapturingHotkey);
        Assert.False(row.HasBinding);
    }

    [Fact]
    public void Clear_hotkey_removes_binding()
    {
        var vm = CreateVm(new FakeDumpService(), out _, new FakeHotkeyService());
        var row = vm.HotkeyRows.First();
        vm.BeginCaptureCommand.Execute(row);
        vm.ApplyCapturedKey(Avalonia.Input.Key.F5, Avalonia.Input.KeyModifiers.None);
        Assert.True(row.HasBinding);

        vm.ClearHotkeyCommand.Execute(row);
        Assert.False(row.HasBinding);
    }

    [Fact]
    public async Task Operations_noop_when_disconnected()
    {
        var fake = new FakeDumpService();
        var vm = CreateVm(fake, out _);
        // not connected
        await vm.RefreshPoseCommand.ExecuteAsync(null);
        await vm.SaveMarkerCommand.ExecuteAsync(0);
        await vm.TeleportToCursorCommand.ExecuteAsync(null);

        Assert.Equal(0, fake.GetPoseCalls);
        Assert.Equal(0, fake.SaveCalls);
        Assert.Equal(0, fake.CursorCalls);
    }

    [Fact]
    public void Disconnect_turns_off_auto_refresh()
    {
        var vm = CreateVm(new FakeDumpService(), out _);
        vm.SetConnected(true);
        vm.AutoRefresh = true;
        vm.SetConnected(false);
        Assert.False(vm.AutoRefresh);
        Assert.False(vm.IsConnected);
    }

    [Fact]
    public async Task Disconnect_clears_pov_display()
    {
        var fake = new FakeDumpService
        {
            NextPov = new() { Code = 0, CamZ = 100, Fov = 90, HasPawn = true },
        };
        var vm = CreateVm(fake, out _);
        vm.IsConnected = true;
        await vm.GetPovCommand.ExecuteAsync(null);
        Assert.Equal("100.000", vm.PovZ);   // populated

        vm.SetConnected(false);
        Assert.Equal("—", vm.PovZ);          // cleared on disconnect
        Assert.Equal("—", vm.PovFov);
        Assert.Equal("", vm.PovSource);
        Assert.Equal("", vm.PovDelta);
    }

    // ── Velocity / acceleration readout + Locate in GWorld ─────────────

    [Fact]
    public async Task RefreshPose_populates_velocity_when_movement_present()
    {
        var fake = new FakeDumpService
        {
            NextPose = new()
            {
                Code = 0, X = 1, Y = 2, Z = 3, Map = "W", Source = "raw",
                PawnAddr = "0x1234", HasMovement = true,
                VelX = 100, VelY = 0, VelZ = -50, Speed = 111.8,
                AccX = 10, AccY = 20, AccZ = 0,
            },
        };
        var vm = CreateVm(fake, out _);
        vm.IsConnected = true;

        await vm.RefreshPoseCommand.ExecuteAsync(null);

        Assert.Equal("100.0", vm.VelX);
        Assert.Equal("-50.0", vm.VelZ);
        Assert.Equal("10.0", vm.AccX);
        Assert.Contains("cm/s", vm.Speed);
        Assert.Equal("", vm.MovementNote);            // available → no note
        Assert.Equal("0x1234", vm.PawnAddrDisplay);
    }

    [Fact]
    public async Task RefreshPose_marks_velocity_unavailable_without_movement()
    {
        var fake = new FakeDumpService
        {
            NextPose = new() { Code = 0, PawnAddr = "0xABC", HasMovement = false },
        };
        var vm = CreateVm(fake, out _);
        vm.IsConnected = true;

        await vm.RefreshPoseCommand.ExecuteAsync(null);

        Assert.Equal("—", vm.VelX);
        Assert.Equal("—", vm.Speed);
        Assert.Equal("—", vm.AccZ);
        Assert.Contains("unavailable", vm.MovementNote, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("0xABC", vm.PawnAddrDisplay);    // pawn addr still shown
    }

    [Fact]
    public async Task RefreshPose_error_clears_velocity_and_pawn()
    {
        var fake = new FakeDumpService
        {
            NextPose = new() { Code = 0, PawnAddr = "0xAAA", HasMovement = true, VelX = 5, Speed = 5 },
        };
        var vm = CreateVm(fake, out _);
        vm.IsConnected = true;
        await vm.RefreshPoseCommand.ExecuteAsync(null);
        Assert.Equal("5.0", vm.VelX);                 // populated
        Assert.Equal("0xAAA", vm.PawnAddrDisplay);

        fake.NextPose = new() { Code = TeleportCodes.NoPawn };
        await vm.RefreshPoseCommand.ExecuteAsync(null);

        Assert.Equal("—", vm.VelX);                   // cleared on error
        Assert.Equal("", vm.PawnAddrDisplay);
        Assert.Equal("", vm.MovementNote);
    }

    [Fact]
    public async Task LocateCurrentPose_fires_event_with_pawn_addr()
    {
        var fake = new FakeDumpService
        {
            NextPose = new() { Code = 0, PawnAddr = "0xDEAD", HasMovement = true },
        };
        var vm = CreateVm(fake, out _);
        vm.IsConnected = true;
        string? located = null;
        vm.LocateInGWorld += a => located = a;

        await vm.LocateCurrentPoseInGWorldCommand.ExecuteAsync(null);

        Assert.Equal("0xDEAD", located);
        Assert.Equal(1, fake.GetPoseCalls);           // reads a fresh pose first
        Assert.Equal("0xDEAD", vm.PawnAddrDisplay);   // display updated too
    }

    [Fact]
    public async Task LocateCurrentPose_no_pawn_addr_does_not_fire()
    {
        var fake = new FakeDumpService
        {
            NextPose = new() { Code = 0, PawnAddr = "0x0" },   // unresolved pawn
        };
        var vm = CreateVm(fake, out _);
        vm.IsConnected = true;
        bool fired = false;
        vm.LocateInGWorld += _ => fired = true;

        await vm.LocateCurrentPoseInGWorldCommand.ExecuteAsync(null);

        Assert.False(fired);
        Assert.Contains("no pawn", vm.StatusText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LocatePosition_fires_value_event_with_root_component_and_offset()
    {
        var fake = new FakeDumpService
        {
            NextPose = new()
            {
                Code = 0, PawnAddr = "0xDEAD",
                LocOwnerAddr = "0xR007", LocFieldOffset = 0x140, LocFieldName = "RelativeLocation",
            },
        };
        var vm = CreateVm(fake, out _);
        vm.IsConnected = true;
        (string owner, int off, string name)? got = null;
        vm.LocateValueInGWorld += (o, f, n) => got = (o, f, n);

        await vm.LocatePositionInGWorldCommand.ExecuteAsync(null);

        Assert.NotNull(got);
        Assert.Equal("0xR007", got!.Value.owner);
        Assert.Equal(0x140, got.Value.off);
        Assert.Equal("RelativeLocation", got.Value.name);
        Assert.Equal(1, fake.GetPoseCalls);   // reads a fresh pose first
    }

    [Fact]
    public async Task LocateVelocity_fires_value_event_with_movement_component_and_offset()
    {
        var fake = new FakeDumpService
        {
            NextPose = new()
            {
                Code = 0, PawnAddr = "0xDEAD", HasMovement = true,
                VelOwnerAddr = "0xCMC0", VelFieldOffset = 0x16C, VelFieldName = "Velocity",
            },
        };
        var vm = CreateVm(fake, out _);
        vm.IsConnected = true;
        (string owner, int off, string name)? got = null;
        vm.LocateValueInGWorld += (o, f, n) => got = (o, f, n);

        await vm.LocateVelocityInGWorldCommand.ExecuteAsync(null);

        Assert.NotNull(got);
        Assert.Equal("0xCMC0", got!.Value.owner);
        Assert.Equal(0x16C, got.Value.off);
        Assert.Equal("Velocity", got.Value.name);
    }

    [Fact]
    public async Task LocateVelocity_no_movement_does_not_fire()
    {
        var fake = new FakeDumpService
        {
            // Pawn resolved but no CharacterMovement → no velocity vector to locate.
            NextPose = new() { Code = 0, PawnAddr = "0xDEAD", HasMovement = false },
        };
        var vm = CreateVm(fake, out _);
        vm.IsConnected = true;
        bool fired = false;
        vm.LocateValueInGWorld += (_, _, _) => fired = true;

        await vm.LocateVelocityInGWorldCommand.ExecuteAsync(null);

        Assert.False(fired);
        Assert.Contains("velocity", vm.StatusText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LocatePosition_no_loc_addr_does_not_fire()
    {
        var fake = new FakeDumpService
        {
            // Old DLL / unresolved owner → loc owner address missing.
            NextPose = new() { Code = 0, PawnAddr = "0xDEAD", LocOwnerAddr = "" },
        };
        var vm = CreateVm(fake, out _);
        vm.IsConnected = true;
        bool fired = false;
        vm.LocateValueInGWorld += (_, _, _) => fired = true;

        await vm.LocatePositionInGWorldCommand.ExecuteAsync(null);

        Assert.False(fired);
        Assert.Contains("position", vm.StatusText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TeleportPose_HasLocAddr_HasVelAddr_reject_null_and_zero()
    {
        Assert.False(new TeleportPose { LocOwnerAddr = "" }.HasLocAddr);
        Assert.False(new TeleportPose { LocOwnerAddr = "0x0" }.HasLocAddr);
        Assert.True(new TeleportPose { LocOwnerAddr = "0x7FF00010" }.HasLocAddr);
        Assert.False(new TeleportPose { VelOwnerAddr = "0X0" }.HasVelAddr);
        Assert.True(new TeleportPose { VelOwnerAddr = "0x7FF00020" }.HasVelAddr);
    }

    [Fact]
    public async Task LocateCurrentPose_error_code_shows_hint_without_firing()
    {
        var fake = new FakeDumpService { NextPose = new() { Code = TeleportCodes.NoPawn } };
        var vm = CreateVm(fake, out _);
        vm.IsConnected = true;
        bool fired = false;
        vm.LocateInGWorld += _ => fired = true;

        await vm.LocateCurrentPoseInGWorldCommand.ExecuteAsync(null);

        Assert.False(fired);
        Assert.Contains("pawn", vm.StatusText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LocateCurrentPose_noop_when_disconnected()
    {
        var fake = new FakeDumpService { NextPose = new() { Code = 0, PawnAddr = "0x1" } };
        var vm = CreateVm(fake, out _);   // not connected
        bool fired = false;
        vm.LocateInGWorld += _ => fired = true;

        await vm.LocateCurrentPoseInGWorldCommand.ExecuteAsync(null);

        Assert.False(fired);
        Assert.Equal(0, fake.GetPoseCalls);
    }

    [Fact]
    public void TeleportPose_HasPawnAddr_rejects_null_and_zero()
    {
        Assert.False(new TeleportPose { PawnAddr = "" }.HasPawnAddr);
        Assert.False(new TeleportPose { PawnAddr = "0x0" }.HasPawnAddr);
        Assert.False(new TeleportPose { PawnAddr = "0X0" }.HasPawnAddr);
        Assert.True(new TeleportPose { PawnAddr = "0x7FF00010" }.HasPawnAddr);
    }

    // ── Coordinate Library: experimental gating + pending-import lifecycle ──

    [Fact]
    public void CoordLibrary_card_is_hidden_until_the_experimental_gate_is_on()
    {
        // The card binds IsVisible to ExperimentalEnabled: it writes the pawn
        // position live and emits CE scripts that do the same.
        var gate = new FakeExperimentalGate(enabled: false);
        var vm = CreateVm(new FakeDumpService(), out _, experimentalGate: gate);

        Assert.False(vm.ExperimentalEnabled);

        gate.IsEnabled = true;
        Assert.True(vm.ExperimentalEnabled);
    }

    [Fact]
    public void CoordLibrary_pending_import_is_cancelled_when_the_gate_is_switched_off()
    {
        // A previewed-but-unapplied import must not survive behind a hidden card,
        // where the user can neither see it nor cancel it.
        var gate = new FakeExperimentalGate(enabled: true);
        var vm = CreateVm(new FakeDumpService(), out _, experimentalGate: gate);

        vm.BuildImportPreview(
            CoordCsvCodec.Parse("label,map,x,y,z\nChest 1,Map01,1,2,3\n"), "test.csv");
        Assert.True(vm.HasPendingCoordImport);

        gate.IsEnabled = false;

        Assert.False(vm.HasPendingCoordImport);
        Assert.Equal("", vm.CoordImportPreview);
    }

    [Fact]
    public void CoordLibrary_pending_import_is_dropped_when_the_game_changes()
    {
        // The diff was computed against the PREVIOUS game's library, so applying it
        // after a game switch would write those rows into the new game's file. This
        // fires even while the card is hidden, so the stale preview is invisible.
        var vm = CreateVm(new FakeDumpService(), out _,
                          experimentalGate: new FakeExperimentalGate(enabled: true));

        vm.BuildImportPreview(
            CoordCsvCodec.Parse("label,map,x,y,z\nChest 1,Map01,1,2,3\n"), "test.csv");
        Assert.True(vm.HasPendingCoordImport);

        vm.LoadCoordLibraryForGame("OtherGame-Win64-Shipping.exe");

        Assert.False(vm.HasPendingCoordImport);
    }

    // ---- [W2-TPREL-MAP] an absent map is not an empty map ----
    //
    // teleport_relative's reply carries no `map` key, ParsePose read that as "", and ApplyPose wrote ""
    // over the known map: every library row re-flagged as another map's, the summary reading "you are
    // on ''", and "Add from fields" persisting map = "" -- which then matches no map ever again.

    [Fact]
    public async Task ParsePose_reports_an_absent_map_as_absent_not_empty()
    {
        var pipe = new MockPipeClient();
        pipe.SetHandler(req =>
        {
            if (req["cmd"]?.GetValue<string>() == "teleport_relative")   // the DLL's reply: no map, no source
                return new System.Text.Json.Nodes.JsonObject { ["ok"] = true, ["code"] = 0, ["x"] = 1.0 };
            return new System.Text.Json.Nodes.JsonObject
                { ["ok"] = true, ["code"] = 0, ["map"] = "", ["source"] = "raw" };
        });
        var svc = new UE5DumpUI.Services.DumpService(pipe, new MockLoggingService());

        var rel = await svc.TeleportRelativeAsync(100, true, TestContext.Current.CancellationToken);
        var pose = await svc.TeleportGetPoseAsync(TestContext.Current.CancellationToken);

        Assert.True(rel.MapAbsent);
        Assert.True(rel.SourceAbsent);
        Assert.False(pose.MapAbsent);      // "" IS a report -- of the empty name. The KEY decides.
        Assert.False(pose.SourceAbsent);
    }

    [Fact]
    public async Task TeleportRelative_keeps_the_known_map()
    {
        var fake = new FakeDumpService { NextPose = new() { Code = 0, Map = "Map01", Source = "invoke" } };
        var vm = CreateVm(fake, out _);
        vm.IsConnected = true;
        await vm.RefreshPoseCommand.ExecuteAsync(null);
        Assert.Equal("Map01", vm.PoseMap);

        // What teleport_relative's reply parses to: a landing, and no map / source keys.
        fake.NextPose = new() { Code = 0, X = 10, MapAbsent = true, SourceAbsent = true };
        await vm.TeleportRelativeCommand.ExecuteAsync(null);

        Assert.Equal("10.000", vm.PoseX);        // the landing itself is applied
        Assert.Equal("Map01", vm.PoseMap);
        Assert.Equal("invoke", vm.PoseSource);   // the twin: an absent source is not "raw"
    }

    [Fact]
    public async Task A_reply_that_reports_a_map_is_still_believed()
    {
        // The control, green before and after: MapAbsent is about the KEY, not the value.
        var fake = new FakeDumpService { NextPose = new() { Code = 0, Map = "Map01" } };
        var vm = CreateVm(fake, out _);
        vm.IsConnected = true;
        await vm.RefreshPoseCommand.ExecuteAsync(null);

        fake.NextPose = new() { Code = 0, Map = "Map02" };
        await vm.TeleportRelativeCommand.ExecuteAsync(null);

        Assert.Equal("Map02", vm.PoseMap);
    }

    [Fact]
    public void AddCoordFromFields_reads_the_map_at_add_time()
    {
        // The second entrance: nothing had read the pose yet (a fresh connect, or right after a
        // directional teleport), so PoseMap was "" and the entry went to disk with map = "".
        var fake = new FakeDumpService { NextPose = new() { Code = 0, Map = "Map01" } };
        var vm = CreateVm(fake, out _);
        vm.IsConnected = true;
        vm.LoadCoordLibraryForGame("Game.exe");
        vm.CoordX = 1; vm.CoordY = 2; vm.CoordZ = 3;

        vm.AddCoordFromFieldsCommand.Execute(null);

        Assert.Equal("Map01", Assert.Single(vm.CoordEntries).Map);
    }

    [Fact]
    public async Task Connect_primes_the_pose_map()
    {
        var fake = new FakeDumpService { NextPose = new() { Code = 0, Map = "Map01" } };
        var vm = CreateVm(fake, out _);

        vm.SetConnected(true);
        await vm.ConnectPrime;

        Assert.Equal("Map01", vm.PoseMap);
    }

    [Fact]
    public async Task An_unknown_current_map_is_not_a_different_map()
    {
        var fake = new FakeDumpService { NextPose = new() { Code = 0, Map = "Map01" } };
        var vm = CreateVm(fake, out _);
        vm.IsConnected = true;
        vm.LoadCoordLibraryForGame("Game.exe");
        await vm.RefreshPoseCommand.ExecuteAsync(null);   // PoseMap = Map01, so the entry is Map01's
        vm.AddCoordFromFieldsCommand.Execute(null);

        vm.SetConnected(false);                           // clears the pose, and with it the map
        vm.SelectedCoord = vm.CoordResults[0];

        Assert.Equal("", vm.PoseMap);
        Assert.True(vm.CoordResults[0].OnCurrentMap);
        Assert.DoesNotContain("different map", vm.SelectedCoordSummary);
    }

    // ---- [W2-POSEATTACH-QUIETPOLL] a parent-relative read is a state on the card ----
    //
    // Only the manual Refresh said so, in the status line, which any later message erases. The 0.5s
    // auto-refresh -- the mode this tab is left in -- applied parent-relative numbers and said nothing.

    [Fact]
    public async Task QuietPoll_surfaces_a_parent_relative_read()
    {
        var fake = new FakeDumpService { NextPose = new() { Code = 0, Source = "raw", ParentRelative = true } };
        var vm = CreateVm(fake, out _);
        vm.IsConnected = true;

        await vm.RefreshPoseQuietAsync();

        Assert.True(vm.PoseParentRelative);
    }

    [Fact]
    public async Task A_healthy_read_clears_the_parent_relative_state()
    {
        // The control, green before and after (nothing set the state before the fix).
        var fake = new FakeDumpService { NextPose = new() { Code = 0, Source = "raw", ParentRelative = true } };
        var vm = CreateVm(fake, out _);
        vm.IsConnected = true;
        await vm.RefreshPoseQuietAsync();

        fake.NextPose = new() { Code = 0, Source = "invoke" };
        await vm.RefreshPoseQuietAsync();

        Assert.False(vm.PoseParentRelative);
    }

    [Fact]
    public async Task A_directional_teleport_keeps_the_parent_relative_state()
    {
        var fake = new FakeDumpService { NextPose = new() { Code = 0, Source = "raw", ParentRelative = true } };
        var vm = CreateVm(fake, out _);
        vm.IsConnected = true;
        await vm.RefreshPoseQuietAsync();

        // teleport_relative's reply carries no read metadata at all -- see [W2-TPREL-MAP].
        fake.NextPose = new() { Code = 0, X = 5, MapAbsent = true, SourceAbsent = true };
        await vm.TeleportRelativeCommand.ExecuteAsync(null);

        Assert.True(vm.PoseParentRelative);
    }

    [Fact]
    public async Task Disconnect_clears_the_parent_relative_state()
    {
        var fake = new FakeDumpService { NextPose = new() { Code = 0, Source = "raw", ParentRelative = true } };
        var vm = CreateVm(fake, out _);
        vm.IsConnected = true;
        await vm.RefreshPoseQuietAsync();
        Assert.True(vm.PoseParentRelative);   // precondition

        vm.SetConnected(false);

        Assert.False(vm.PoseParentRelative);
    }

    [Fact]
    public void The_Current_Pose_card_shows_the_parent_relative_state()
    {
        // No test renders the panel, so pin the markup. Compiled bindings (x:DataType) already make a
        // misspelt property a build error; this pins that the chip EXISTS and is bound to the state.
        var axaml = System.IO.File.ReadAllText(
            NumericInputCoercionTests.RepoFile("ui/UE5DumpUI/Views/TeleportPanel.axaml"));
        Assert.Contains("IsVisible=\"{Binding PoseParentRelative}\"", axaml);
        Assert.Contains("{StaticResource str.TP.ParentRelative}", axaml);
    }

    [Fact]
    public async Task CoordLibrary_noDll_export_refuses_without_AOBMaker()
    {
        // Matches the Standalone Trainer export: gated on AOBMaker with NO
        // clipboard fallback, by design. The emitted script calls UE5T_* helpers
        // defined by that trainer's Setup record, which only an AOBMaker push can
        // deliver -- a clipboard blob would be a record that can never work, whose
        // failure reads as a bug in the script rather than a missing prerequisite.
        var vm = CreateVm(new FakeDumpService(), out var platform,
                          experimentalGate: new FakeExperimentalGate(enabled: true));
        vm.LoadCoordLibraryForGame("Game.exe");
        // Add via the fields, not Save-current-pos: the latter needs a live pipe.
        vm.CoordX = 1; vm.CoordY = 2; vm.CoordZ = 3;
        vm.AddCoordFromFieldsCommand.Execute(null);
        Assert.NotEmpty(vm.CoordEntries);

        await vm.ExportCoordLuaNoDllCommand.ExecuteAsync(null);

        Assert.Contains("AOBMaker", vm.CoordStatus);
        Assert.Null(platform.LastClipboard);      // no fallback, deliberately
    }

    [Fact]
    public void CoordLibrary_load_without_a_store_is_a_no_op()
    {
        // Headless tests construct the VM with no store; it must not throw.
        var vm = CreateVm(new FakeDumpService(), out _);
        vm.LoadCoordLibraryForGame("Game.exe");
        Assert.Empty(vm.CoordEntries);
    }
    /// <summary>
    /// [BADGEPRIME-2026-09-10] — connect must PRIME every badge the disconnect branch
    /// resets, not three of twelve.
    /// </summary>
    /// <remarks>
    /// <para>Observed live on a Shipping fixture before the fix: Keep Foreground, Move
    /// Speed, Debug Camera, Gravity, Super Jump and Fly all read "State: Unknown" while the
    /// pipe answered <c>state: 0</c> / <c>has_cmc: true</c> for each — and God Mode and
    /// Time Dilation showed real values, which were exactly the two that were primed. That
    /// pairing is the control, and it is reproduced here: the assertion is that NO badge is
    /// left Unknown, so a future card added to the reset list without a prime fails.</para>
    ///
    /// <para>It matters because a DLL hold survives a UI reconnect for as long as the game
    /// lives, so a still-active Fly or Move Speed hold showed "Unknown" and the user had no
    /// sign the game was still modified.</para>
    /// </remarks>
    [Fact]
    public async Task Connect_primes_every_badge_that_disconnect_resets()
    {
        var fake = new FakeDumpService { NextCursorState = 1 };
        var vm = CreateVm(fake, out _);

        vm.SetConnected(true);
        await vm.ConnectPrime;

        // ⛔ ASSERT THAT CONNECT *ASKED*, NOT WHAT THE BADGE SAYS. A first version of this
        // test compared badge text against "Unknown" and failed on DebugCamera, GodMode and
        // ForegroundLock -- none of which was a product defect: those are FAKE defaults
        // that legitimately mean unknown (GetForegroundLockAsync is not even overridden).
        // Badge text conflates "the VM never asked" with "the fake had nothing to say",
        // and only the first of those is the defect. The call counters separate them.
        Assert.True(fake.GetProtectStateCalls >= 1, "connect did not read the God Mode hold");
        Assert.True(fake.GetDebugCameraCalls >= 1, "connect did not read the Debug Camera state");
        Assert.True(fake.GetCursorCalls >= 1, "connect did not read the mouse-cursor state");
        Assert.True(fake.FlyGetStateCalls >= 1, "connect did not read the Fly state");
    }

    /// <summary>The other half of the symmetry: a badge the prime DID light up goes back to
    /// Unknown on disconnect. Without this the test above could pass over a UI that lights
    /// badges and never clears them, which is the B9/B17 defect in the other direction.</summary>
    [Fact]
    public async Task Disconnect_returns_a_primed_badge_to_Unknown()
    {
        var fake = new FakeDumpService { NextCursorState = 1 };
        var vm = CreateVm(fake, out _);
        vm.SetConnected(true);
        await vm.ConnectPrime;
        // The cursor is a badge the fake CAN answer for, so it is genuinely lit here --
        // which is what makes the reset below a real observation rather than a no-op.
        Assert.Equal("ON", vm.MouseCursorState);

        vm.SetConnected(false);

        // ⚠ THE BADGES DO NOT SHARE A WORD, and assuming they did is what this assertion
        // caught: Apply*State(-1) renders "Unknown" for the cursor but "Unavailable" for
        // Fly, See-through and Gravity. Pinning the literal per card is deliberate -- a
        // helper that accepted either would stop noticing if one card's reset broke.
        Assert.Equal("Unknown", vm.MouseCursorState);
        Assert.Equal("Unavailable", vm.FlyState);
        Assert.Equal("Unavailable", vm.SeeThroughState);
        Assert.Equal("Unavailable", vm.GravityState);
    }

    /// <summary>The prime must never write StatusText — it would stamp over "Connected" —
    /// and must not leave the UI busy.</summary>
    [Fact]
    public async Task Connect_prime_is_quiet()
    {
        var fake = new FakeDumpService { NextCursorState = 1 };
        var vm = CreateVm(fake, out _);

        vm.SetConnected(true);
        await vm.ConnectPrime;

        Assert.Equal("Connected", vm.StatusText);
        Assert.False(vm.IsBusy);
    }

    /// <summary>
    /// [POSEATTACH-2026-09-10] — a parent-relative fallback must SAY it is not world space.
    /// </summary>
    /// <remarks>
    /// docs/teleport-spec.md:218-220 required this flag when the fallback was designed:
    /// "return the raw values anyway with `source = raw` <b>and a warning flag</b>". The
    /// fallback shipped in 2026-07; the flag did not. So a degraded attached-pawn read and
    /// a healthy unattached one both arrived as <c>source = "raw"</c> — and the UI model's
    /// own XML documented "raw" as MEANING not-attached. Those numbers were shown as world
    /// coordinates, saved into a marker that passes the map guard, and later driven back
    /// into the pawn as a world-space destination.
    /// </remarks>
    [Fact]
    public async Task Pose_read_that_degraded_to_parent_relative_warns_loudly()
    {
        var fake = new FakeDumpService
        {
            NextPose = new TeleportPose { Code = 0, Source = "raw", ParentRelative = true },
        };
        var vm = CreateVm(fake, out _);
        vm.SetConnected(true);
        await vm.ConnectPrime;

        await vm.RefreshPoseCommand.ExecuteAsync(null);

        Assert.Contains("PARENT-RELATIVE", vm.StatusText, StringComparison.Ordinal);
        Assert.Contains("not world coordinates", vm.StatusText, StringComparison.OrdinalIgnoreCase);
        // ⭐ It must also tell the user what NOT to do — the damage is off-screen (a marker
        // saved from these numbers teleports the pawn somewhere else entirely), so naming
        // the consequence is the point rather than flagging a colour.
        Assert.Contains("marker", vm.StatusText, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The anti-over-shout control: a HEALTHY raw read must stay quiet. Without
    /// this, "warn on every raw pose" would pass the test above and cry wolf on the common
    /// case — every unattached pawn reads raw.</summary>
    [Fact]
    public async Task Healthy_raw_pose_read_does_NOT_warn()
    {
        var fake = new FakeDumpService
        {
            NextPose = new TeleportPose { Code = 0, Source = "raw", ParentRelative = false },
        };
        var vm = CreateVm(fake, out _);
        vm.SetConnected(true);
        await vm.ConnectPrime;

        await vm.RefreshPoseCommand.ExecuteAsync(null);

        Assert.DoesNotContain("PARENT-RELATIVE", vm.StatusText, StringComparison.Ordinal);
        Assert.Contains("Pose read (raw)", vm.StatusText, StringComparison.Ordinal);
    }

}
