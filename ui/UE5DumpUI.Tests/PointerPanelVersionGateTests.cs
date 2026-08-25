using System.ComponentModel;
using UE5DumpUI.Core;
using UE5DumpUI.Models;
using UE5DumpUI.ViewModels;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// Tests for the two REFUSAL banners in the Pointers panel and the controls they must switch off.
///
/// The DLL has one flag (<c>is_version_too_old</c>) covering two different refusals, split on the
/// version number: 400-410 = "UE 4.0-4.10, the right family but too old", and the sentinel 300 =
/// "positively identified as pre-UE4 (UE3), a different object model". They must never collapse
/// into one message, because the 4.10 text's remedy line ("set a UE version override") is
/// meaningless for UE3 — the override list has no value below 4.18 and no value at any version
/// would make UE3's absent structures appear.
///
/// Also pins the notification fix: <c>ShowVersionTooOldWarning</c> was missing from
/// <c>NotifyComputedProperties</c>, so the red banner never re-raised on an
/// <see cref="PointerPanelViewModel.Update"/> of an already-attached panel.
/// </summary>
public class PointerPanelVersionGateTests
{
    private sealed class StubPlatform : IPlatformService
    {
        public bool TryAcquireSingleInstance() => true;
        public void ReleaseSingleInstance() { }
        public string GetAppDataPath() => Path.GetTempPath();
        public string GetLogDirectoryPath() => Path.GetTempPath();
        public Task<bool> CopyToClipboardAsync(string text) => Task.FromResult(true);
        public Task RevealInExplorerAsync(string path) => Task.CompletedTask;
        public string GetMachineName() => "TESTHOST";
        public void CloseImeForWindow(IntPtr windowHandle) { }
        public Task<string?> ShowSaveFileDialogAsync(string defaultFileName, string filterName,
                                                     string filterExtension)
            => Task.FromResult<string?>(null);
    }

    private static PointerPanelViewModel NewVm() => new(new StubPlatform());

    /// <summary>A refused engine: no pointers, and the DLL says it skipped the scan.</summary>
    private static EngineState Refused(int ueVersion) => new()
    {
        UEVersion = ueVersion,
        VersionDetected = true,
        IsLowConfidence = false,
        IsVersionTooOld = true,
        GObjectsAddr = "0x0",
        GNamesAddr = "0x0",
        GWorldAddr = "0x0",
        GObjectsMethod = "not_found",
        GNamesMethod = "not_found",
        GWorldMethod = "not_found",
        ObjectCount = 0,
    };

    [Fact]
    public void PreUE4Sentinel_ShowsOnlyThePreUE4Banner()
    {
        var vm = NewVm();
        vm.Update(Refused(PointerPanelViewModel.PreUE4SentinelVersion));

        Assert.True(vm.ShowPreUE4Warning);
        Assert.False(vm.ShowVersionTooOldWarning);
    }

    [Fact]
    public void Ue410_ShowsOnlyTheTooOldBanner()
    {
        var vm = NewVm();
        vm.Update(Refused(410));

        Assert.True(vm.ShowVersionTooOldWarning);
        Assert.False(vm.ShowPreUE4Warning);
    }

    [Fact]
    public void SupportedVersion_ShowsNeitherBanner()
    {
        var vm = NewVm();
        vm.Update(new EngineState
        {
            UEVersion = 504,
            VersionDetected = false,     // the stripped-tag state a UE3 game also lands in
            IsLowConfidence = true,
            IsVersionTooOld = false,     // ...but NOT refused, so no banner
            ObjectCount = 12345,
        });

        Assert.False(vm.ShowVersionTooOldWarning);
        Assert.False(vm.ShowPreUE4Warning);
    }

    [Fact]
    public void PreUE4Sentinel_DoesNotAlsoClaimSparseDelegatesUnsupported()
    {
        // The sentinel is below every real version, so an unguarded "< 423" test would add a
        // redundant "sparse delegates unsupported" line next to the real explanation — implying
        // the rest of the panel is otherwise working.
        var vm = NewVm();
        vm.Update(Refused(PointerPanelViewModel.PreUE4SentinelVersion));

        Assert.False(vm.IsSparseDelegatesUnsupported);
    }

    [Fact]
    public void RealPreSparseVersion_StillClaimsSparseDelegatesUnsupported()
    {
        // Guards the >= 400 floor added for the sentinel from swallowing the genuine case.
        var vm = NewVm();
        vm.Update(new EngineState { UEVersion = 422, ObjectCount = 1 });

        Assert.True(vm.IsSparseDelegatesUnsupported);
    }

    [Theory]
    [InlineData(PointerPanelViewModel.PreUE4SentinelVersion)]
    [InlineData(410)]
    public void RefusedEngine_HidesExtraScan(int ueVersion)
    {
        // Extra Scan probes the same UE4/UE5 presets and the same hardcoded UObject::Class chain,
        // so on a refused engine it is a guaranteed no-op. Offering it would contradict the banner.
        var vm = NewVm();
        vm.Update(Refused(ueVersion));

        Assert.False(vm.CanExtraScan);
    }

    [Fact]
    public void MissingGObjectsOnSupportedEngine_StillOffersExtraScan()
    {
        // The recovery path that Extra Scan exists for must be unaffected.
        var vm = NewVm();
        vm.Update(new EngineState
        {
            UEVersion = 503,
            IsVersionTooOld = false,
            GObjectsAddr = "0x0",
            GWorldMethod = "not_found",
        });

        Assert.True(vm.CanExtraScan);
    }

    [Theory]
    [InlineData(PointerPanelViewModel.PreUE4SentinelVersion)]
    [InlineData(410)]
    public void RefusedEngine_SilencesEveryPerPointerFailureLine(int ueVersion)
    {
        // LIVE-OBSERVED on a UE3 title before this guard: the panel showed
        // "🔴 All AOB patterns failed" + "⚠ AOB failed — found via not found" on GObjects,
        // GNames and GWorld — after a scan that never ran. Those lines are FALSE on a refused
        // engine (0 hits means 0 patterns TRIED) and they contradict the banner directly above,
        // which says every pointer is empty by design.
        var vm = NewVm();
        vm.Update(Refused(ueVersion));

        Assert.False(vm.GObjectsAobAllFailed);
        Assert.False(vm.GNamesAobAllFailed);
        Assert.False(vm.GWorldAobAllFailed);
        Assert.False(vm.ShowGObjectsWarning);
        Assert.False(vm.ShowGNamesWarning);
        Assert.False(vm.ShowGWorldWarning);
        Assert.False(vm.IsGEngineNotFound);
        // The Sparse card must stay silent too — neither "unsupported" nor "AOB not found".
        Assert.False(vm.IsSparseDelegatesUnsupported);
        Assert.False(vm.IsSparseDelegatesNotFound);
    }

    [Fact]
    public void GenuineAobFailureOnSupportedEngine_StillReportsIt()
    {
        // The counterpart: on a supported engine a real 0-hit sweep must still say so, or the
        // guard above would have silenced the one honest diagnostic the panel has.
        var vm = NewVm();
        vm.Update(new EngineState
        {
            UEVersion = 504,
            IsVersionTooOld = false,
            GObjectsAddr = "0x0",
            GNamesAddr = "0x0",
            GObjectsMethod = "not_found",
            GNamesMethod = "not_found",
            GWorldMethod = "not_found",
            GEngineMethod = "not_found",
            GObjectsPatternsHit = 0,
            GNamesPatternsHit = 0,
            GWorldPatternsHit = 0,
        });

        Assert.True(vm.GObjectsAobAllFailed);
        Assert.True(vm.GNamesAobAllFailed);
        Assert.True(vm.GWorldAobAllFailed);
        Assert.True(vm.ShowGObjectsWarning);
        Assert.True(vm.ShowGWorldWarning);
        Assert.True(vm.IsGEngineNotFound);
    }

    [Fact]
    public void Update_RaisesChangeNotificationForBothBanners()
    {
        // Regression guard for the missing OnPropertyChanged: without it an already-attached
        // panel keeps showing the pre-Update value and the banner never appears.
        var vm = NewVm();
        var raised = new List<string>();
        ((INotifyPropertyChanged)vm).PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != null) raised.Add(e.PropertyName);
        };

        vm.Update(Refused(PointerPanelViewModel.PreUE4SentinelVersion));

        Assert.Contains(nameof(PointerPanelViewModel.ShowVersionTooOldWarning), raised);
        Assert.Contains(nameof(PointerPanelViewModel.ShowPreUE4Warning), raised);
    }

    [Fact]
    public void SentinelMatchesTheDllConstant()
    {
        // Mirrors Grimoire::PRE_UE4_SENTINEL_VERSION. If the DLL's value ever moves, this is the
        // canary — the two are compared only by this number, there is no shared header.
        Assert.Equal(300, PointerPanelViewModel.PreUE4SentinelVersion);
    }
}
