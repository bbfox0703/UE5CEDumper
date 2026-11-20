using System.ComponentModel;
using UE5DumpUI.Core;
using UE5DumpUI.Helpers;
using UE5DumpUI.Models;
using UE5DumpUI.ViewModels;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// [RESCAN-DECOY-REINIT] The Pointers panel offers Extra Scan for a published GObjects that reads no objects (the DLL
/// replaces it from <see cref="Constants.RescanReplaceMinDllBuild"/> on), keeps the result in view once the scan made
/// the button unnecessary, and reports what apply_rescan actually did.
/// </summary>
public class ExtraScanReplaceTests
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

    // A supported engine whose GObjects, GNames and GWorld were all found by AOB.
    private static EngineState Found(int objects, string gnames = "0x7FF600002000", int dll = Constants.RescanReplaceMinDllBuild,
                                     string gobjectsMethod = "aob")
        => new()
        {
            UEVersion = 503,
            GObjectsAddr = "0x7FF600001000",
            GObjectsMethod = gobjectsMethod,
            GNamesAddr = gnames,
            GWorldAddr = "0x7FF600003000",
            GWorldMethod = "aob",
            ObjectCount = objects,
            DllBuildNumber = dll,
        };

    [Fact]
    public void AGObjectsThatReadsNoObjects_IsOfferedExtraScan()
    {
        var vm = NewVm();
        vm.Update(Found(0));
        Assert.True(vm.CanExtraScan);
        vm.Update(Found(-1));
        Assert.True(vm.CanExtraScan);   // a negative count is no objects either
    }

    [Fact]
    public void WithoutGNames_OrFromAnOlderDll_ItIsNot()
    {
        // The DLL judges candidates by the names their heads resolve, so it replaces nothing without GNames; an older
        // DLL answers "all pointers found".
        var vm = NewVm();
        vm.Update(Found(0, gnames: "0x0"));
        Assert.False(vm.CanExtraScan);
        vm.Update(Found(0, dll: Constants.RescanReplaceMinDllBuild - 1));
        Assert.False(vm.CanExtraScan);
    }

    [Fact]
    public void APoolThatReadsObjects_IsNotOffered_AsBefore()
    {
        var vm = NewVm();
        vm.Update(Found(153466));
        Assert.False(vm.CanExtraScan);
    }

    [Fact]
    public void TheResult_StaysInViewAfterTheScanHidesTheButton()
    {
        // The refresh that follows a successful apply reads objects, which hides the button; the result line beside it
        // must not go with it.
        var vm = NewVm();
        vm.Update(Found(0));
        Assert.True(vm.ShowExtraScanSection);
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);
        vm.ScanComplete = true;
        vm.ScanResultText = "result";
        raised.Clear();   // what follows must come from Update itself
        vm.Update(Found(153466));
        Assert.False(vm.CanExtraScan);
        Assert.True(vm.ShowExtraScanSection);
        Assert.Contains(nameof(PointerPanelViewModel.ShowExtraScanSection), raised);
        Assert.Contains(nameof(PointerPanelViewModel.ExtraScanOffered), raised);
        vm.ResetScanState();
        Assert.False(vm.ShowExtraScanSection);
    }

    [Fact]
    public void TheOffer_AppearsThroughNotificationsAlone()
    {
        // The view's compiled bindings re-read only what is raised: a pool that stops reading objects must raise both.
        var vm = NewVm();
        vm.Update(Found(153466));
        Assert.False(vm.ShowExtraScanSection);
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);
        vm.Update(Found(0));
        Assert.True(vm.ExtraScanOffered);
        Assert.Contains(nameof(PointerPanelViewModel.ExtraScanOffered), raised);
        Assert.Contains(nameof(PointerPanelViewModel.ShowExtraScanSection), raised);
    }

    private static RescanStatusResult Status(bool gobjects, bool gworld = false) => new()
    {
        Phase = 3,
        FoundGObjects = gobjects,
        GObjectsAddr = gobjects ? "0x7FF600009000" : "",
        FoundGWorld = gworld,
        GWorldAddr = gworld ? "0x7FF60000A000" : "",
    };

    private static ApplyRescanResult Applied(string outcome, string gobjects, int objects) => new()
    {
        State = new EngineState { GObjectsAddr = gobjects, ObjectCount = objects },
        GObjectsOutcome = outcome,
    };

    [Fact]
    public void AFill_ReadsAsBefore()
    {
        var r = ExtraScanReport.Describe(Status(true, gworld: true), Applied(Constants.RescanOutcomeFilled, "0x7FF600009000", 5000));
        Assert.Equal("Found: GObjects: 0x7FF600009000, GWorld: 0x7FF60000A000", r.ResultText);
        Assert.Equal(Res.Get("str.Pointers.Scan.Applied"), r.StatusText);
        Assert.True(r.Applied);
        // A DLL that predates the outcome key only ever filled.
        r = ExtraScanReport.Describe(Status(true), Applied("", "0x7FF600009000", 5000));
        Assert.Equal("Found: GObjects: 0x7FF600009000", r.ResultText);
        Assert.True(r.Applied);
    }

    [Fact]
    public void AReplace_NamesThePointerApplyChose_NotTheJobsFirstHit()
    {
        var r = ExtraScanReport.Describe(Status(true), Applied(Constants.RescanOutcomeReplaced, "0x7FF60000B000", 153466));
        Assert.Equal(Res.Format("str.Pointers.Scan.Replaced", "0x7FF60000B000", 153466), r.ResultText);
        Assert.DoesNotContain("0x7FF600009000", r.ResultText);
        Assert.True(r.Applied);
        // Kept where it was: not a replacement.
        r = ExtraScanReport.Describe(Status(true), Applied(Constants.RescanOutcomeReinitInPlace, "0x7FF600001000", 153466));
        Assert.Equal(Res.Format("str.Pointers.Scan.ReinitInPlace", "0x7FF600001000", 153466), r.ResultText);
        Assert.True(r.Applied);
        // The probe re-ran; whether it measured is the offsets banner's to say.
        Assert.DoesNotContain("measured", Res.Get("str.Pointers.Scan.Replaced"));
        Assert.DoesNotContain("measured", Res.Get("str.Pointers.Scan.ReinitInPlace"));
    }

    [Fact]
    public void NoQualifyingCandidate_OrAnApplyThatActedOnNothing_SaysSo()
    {
        var r = ExtraScanReport.Describe(Status(true), Applied(Constants.RescanOutcomeNoneQualified, "0x7FF600001000", 0));
        Assert.Equal(Res.Get("str.Pointers.Scan.NoneQualified"), r.ResultText);
        Assert.Equal(Res.Get("str.Pointers.Scan.CompleteNoResults"), r.StatusText);
        Assert.False(r.Applied);
        foreach (string outcome in new[] { "already_applied", "changed", "cancelled", "aborted", "not_sought" })
        {
            r = ExtraScanReport.Describe(Status(true), Applied(outcome, "0x7FF600001000", 0));
            Assert.Equal(Res.Format("str.Pointers.Scan.NotApplied", outcome), r.ResultText);
            Assert.False(r.Applied);
        }
    }

    [Fact]
    public void TheNewTexts_AreInTheDictionary()
    {
        foreach (string key in new[] { "str.Pointers.Scan.Replaced", "str.Pointers.Scan.ReinitInPlace",
                                       "str.Pointers.Scan.NoneQualified", "str.Pointers.Scan.NotApplied",
                                       "str.Pointers.Scan.Found", "str.Pointers.Scan.FoundGObjects", "str.Pointers.Scan.FoundGWorld" })
            Assert.False(string.IsNullOrEmpty(Res.Get(key)), key);
    }

    [Fact]
    public void TheStaticStructRecovery_HasALabel()
    {
        // [INIT-RECOVERY-SCANADDR] UE5_Init's static-struct recovery publishes "static_struct_recovery"; the panel showed
        // the raw word.
        var vm = NewVm();
        vm.Update(Found(5000, gobjectsMethod: "static_struct_recovery"));
        Assert.Equal(Res.Get("str.Pointers.Method.StaticStructRecovery"), vm.GObjectsMethodLabel);
        Assert.NotEqual("static_struct_recovery", vm.GObjectsMethodLabel);
    }

    [Fact]
    public void EveryApply_RefreshesThePanels()
    {
        // An apply that replaced nothing can still find the DLL moved ("changed": the pool reads objects now;
        // "already_applied": another client's apply), and the refresh is one get_pointers.
        string vm = File.ReadAllText(NumericInputCoercionTests.RepoFile("ui/UE5DumpUI/ViewModels/PointerPanelViewModel.cs"))
            .Replace("\r\n", "\n");
        int at = vm.IndexOf("var report = ExtraScanReport.Describe(status, applied);", StringComparison.Ordinal);
        Assert.True(at > 0, "the apply branch was not found");
        int end = vm.IndexOf("break;", at, StringComparison.Ordinal);
        string branch = vm[at..end];
        Assert.Contains("\n                        RescanApplied?.Invoke();", branch);
        Assert.DoesNotContain("if (report.Applied)", branch);
    }
}
