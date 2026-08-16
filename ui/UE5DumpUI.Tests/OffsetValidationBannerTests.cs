using System.Collections.Generic;
using System.Text.Json.Nodes;
using UE5DumpUI.Models;
using UE5DumpUI.Services;
using UE5DumpUI.ViewModels;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// Offset-validation banner — audit #5 U3/X3.
///
/// The DLL has always published a three-flag verdict on <c>get_offsets</c>
/// (<c>validated</c> / <c>probe_ran</c> / <c>fallback_reason</c>) and the UI issued the
/// command nowhere, so nothing could tell the user that Live Walker values, exports and
/// Force/Freeze writes were all derived from unmeasured UE-version defaults.
///
/// Two things are pinned here: that the verdict is actually FETCHED and carried into
/// <see cref="EngineState"/>, and that a missing verdict stays silent rather than crying
/// wolf on every older DLL.
/// </summary>
public class OffsetValidationBannerTests
{
    private static (DumpService svc, MockPipeClient pipe) Make(
        JsonObject? offsetsResponse, List<string>? seen = null)
    {
        var pipe = new MockPipeClient();
        pipe.SetHandler(req =>
        {
            var cmd = req["cmd"]?.GetValue<string>() ?? "";
            seen?.Add(cmd);
            if (cmd == "get_offsets")
                return offsetsResponse ?? new JsonObject { ["error"] = "unknown command" };
            return new JsonObject { ["ok"] = true, ["gobjects"] = "0x1000", ["ue_version"] = 505 };
        });
        return (new DumpService(pipe, new MockLoggingService()), pipe);
    }

    [Fact]
    public async Task GetPointers_IssuesGetOffsets_AndCarriesTheVerdict()
    {
        var seen = new List<string>();
        var (svc, _) = Make(new JsonObject
        {
            ["ok"] = true,
            ["validated"] = false,
            ["probe_ran"] = true,
            ["fallback_reason"] = "unmeasured:elemsize",
        }, seen);

        var state = await svc.GetPointersAsync(TestContext.Current.CancellationToken);

        Assert.Contains("get_offsets", seen);   // the command had zero clients before X3
        Assert.False(state.OffsetsValidated);
        Assert.True(state.OffsetsProbeRan);
        Assert.Equal("unmeasured:elemsize", state.OffsetsFallbackReason);
    }

    [Fact]
    public async Task ValidatedRunCarriesNoReason()
    {
        var (svc, _) = Make(new JsonObject
        {
            ["ok"] = true,
            ["validated"] = true,
            ["probe_ran"] = true,
            ["fallback_reason"] = "",
        });

        var state = await svc.GetPointersAsync(TestContext.Current.CancellationToken);

        Assert.True(state.OffsetsValidated);
        Assert.Equal("", state.OffsetsFallbackReason);
    }

    [Fact]
    public async Task OlderDllWithoutGetOffsets_ReadsAsValidated_SoNoBannerAppears()
    {
        // The DLL answers with an error object. Absent evidence must not become a warning —
        // the same convention version_detected uses, and the reason TryGetOffsetsAsync
        // swallows the failure instead of failing the connect.
        var (svc, _) = Make(null);

        var state = await svc.GetPointersAsync(TestContext.Current.CancellationToken);

        Assert.True(state.OffsetsValidated);
        Assert.False(state.OffsetsProbeRan);
        Assert.Equal("", state.OffsetsFallbackReason);
    }

    // ── The banner itself ──────────────────────────────────────────────────────

    private static PointerPanelViewModel VmWith(bool validated, bool probeRan,
                                                string reason, bool tooOld = false)
    {
        var vm = new PointerPanelViewModel(new MockPlatformService(System.IO.Path.GetTempPath()));
        vm.Update(new EngineState
        {
            GObjectsAddr = "0x1000",
            UEVersion = 505,
            IsVersionTooOld = tooOld,
            OffsetsValidated = validated,
            OffsetsProbeRan = probeRan,
            OffsetsFallbackReason = reason,
        });
        return vm;
    }

    [Fact]
    public void PartiallyMeasured_ShowsTheBanner_NamingTheProbeThatFellBack()
    {
        var vm = VmWith(validated: false, probeRan: true, reason: "unmeasured:elemsize");

        Assert.True(vm.ShowOffsetsUnvalidatedWarning);
        Assert.Contains("unmeasured:elemsize", vm.OffsetsUnvalidatedText);
    }

    [Fact]
    public void DetectionNeverRan_GetsItsOwnWording()
    {
        var vm = VmWith(validated: false, probeRan: false, reason: "");

        Assert.True(vm.ShowOffsetsUnvalidatedWarning);
        Assert.Contains("never ran", vm.OffsetsUnvalidatedText);
    }

    [Fact]
    public void FullyMeasured_ShowsNothing()
    {
        var vm = VmWith(validated: true, probeRan: true, reason: "");
        Assert.False(vm.ShowOffsetsUnvalidatedWarning);
    }

    /// <summary>
    /// Under the too-old refusal the DLL skipped the scan on purpose, so the offsets are
    /// unmeasured by design. The refusal banner already explains it; a second warning would
    /// be true, redundant, and read as a separate fault.
    /// </summary>
    [Fact]
    public void VersionTooOldRefusal_SuppressesTheOffsetBanner()
    {
        var vm = VmWith(validated: false, probeRan: false, reason: "", tooOld: true);
        Assert.False(vm.ShowOffsetsUnvalidatedWarning);
    }

    /// <summary>
    /// The trap PointerPanelViewModel documents for ShowVersionTooOldWarning: these are
    /// [ObservableProperty] with no NotifyPropertyChangedFor, so unless Update() raises the
    /// computed pair by hand the banner never appears on a REFRESH of an attached panel.
    /// </summary>
    [Fact]
    public void Update_RaisesTheComputedPair_SoARefreshCanRevealTheBanner()
    {
        var vm = VmWith(validated: true, probeRan: true, reason: "");
        var raised = new List<string>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName ?? "");

        vm.Update(new EngineState
        {
            GObjectsAddr = "0x1000",
            UEVersion = 505,
            OffsetsValidated = false,
            OffsetsProbeRan = true,
            OffsetsFallbackReason = "unmeasured:ffield-next",
        });

        Assert.Contains(nameof(PointerPanelViewModel.ShowOffsetsUnvalidatedWarning), raised);
        Assert.Contains(nameof(PointerPanelViewModel.OffsetsUnvalidatedText), raised);
        Assert.True(vm.ShowOffsetsUnvalidatedWarning);
    }
}
