using UE5DumpUI.Core;
using UE5DumpUI.Models;
using UE5DumpUI.ViewModels;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// How the Pointers panel reads the DLL's <c>*_method</c> label.
///
/// <para>The panel's two "something unusual happened" badges — <c>ShowGObjectsWarning</c>
/// ("found via fallback") and <c>ShowGWorldRecovered</c> ("found via a recovery path") — used
/// to be written as <c>method != "aob"</c>. That was only ever equivalent to the intended
/// question while a successful scan could report nothing but <c>"aob"</c>, and the signature
/// tables have always also held symbol exports and CallFollow entries. Once the DLL started
/// labelling those honestly, the equality test would have called the STRONGEST result the
/// scanner can produce a "fallback".</para>
///
/// <para>Measured on Satisfactory (UE 5.6) 2026-08-12: GObjects, GNames, GWorld and GEngine
/// all resolve through MSVC symbol exports (<c>GOBJ_EXP</c> / <c>GNAM_EXP_TOSTR</c> /
/// <c>GWLD_EXP</c> / <c>GENG_EXP</c>). Every case below is that machine's real payload or a
/// deliberate counterpart to it.</para>
/// </summary>
public class PointerPanelScanMethodTests
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

    private static EngineState Resolved(string method) => new()
    {
        UEVersion = 506,
        IsVersionTooOld = false,
        GObjectsAddr = "0x7FFF07673620",
        GNamesAddr = "0x7FFF0BDDD8C0",
        GWorldAddr = "0x7FFF06EBCB88",
        GObjectsMethod = method,
        GNamesMethod = method,
        GWorldMethod = method,
        GObjectsPatternsHit = 1,
        GNamesPatternsHit = 1,
        GWorldPatternsHit = 1,
    };

    // ── every value a SUCCESSFUL scan can report must read as normal ─────────

    [Theory]
    [InlineData("aob")]                 // ordinary byte pattern
    [InlineData("symbol")]              // SymbolExport      — Satisfactory's four
    [InlineData("symbol_call_follow")]  // SymbolCallFollow  — GNAM_EXP_TOSTR
    [InlineData("call_follow")]         // CallFollow
    public void DirectScanResult_RaisesNoWarningAndNoRecoveredBadge(string method)
    {
        var vm = NewVm();
        vm.Update(Resolved(method));

        Assert.False(vm.ShowGObjectsWarning);
        Assert.False(vm.ShowGNamesWarning);
        Assert.False(vm.ShowGWorldWarning);
        Assert.False(vm.ShowGWorldRecovered);
    }

    // ── the cases the badges exist for must still fire ───────────────────────

    [Theory]
    [InlineData("engine_recovery")]
    [InlineData("instance_scan_recovery")]
    public void RecoveryPath_StillReportsRecovered(string method)
    {
        // The negative control for the theory above: if the membership test were widened
        // carelessly these would go quiet, and a GWorld that came from GEngine->GameViewport
        // would render as a clean scan hit.
        var vm = NewVm();
        vm.Update(Resolved(method));

        Assert.True(vm.ShowGWorldRecovered);
        Assert.True(vm.ShowGObjectsWarning);
        Assert.False(vm.ShowGWorldWarning);   // it WAS found, just not by the scan
    }

    [Fact]
    public void NotFound_WarnsButIsNotCalledRecovered()
    {
        var vm = NewVm();
        vm.Update(Resolved("not_found"));

        Assert.True(vm.ShowGObjectsWarning);
        Assert.True(vm.ShowGNamesWarning);
        Assert.True(vm.ShowGWorldWarning);
        // "not found" is not a recovery — the badge would be actively wrong.
        Assert.False(vm.ShowGWorldRecovered);
    }

    [Fact]
    public void UnknownFutureValue_IsTreatedAsNotADirectScan()
    {
        // Fail loud rather than silent: a label this build does not recognise should show the
        // badge and prompt someone to look, not be waved through as a normal hit.
        var vm = NewVm();
        vm.Update(Resolved("some_method_added_later"));

        Assert.True(vm.ShowGObjectsWarning);
        Assert.True(vm.ShowGWorldRecovered);
    }
}
