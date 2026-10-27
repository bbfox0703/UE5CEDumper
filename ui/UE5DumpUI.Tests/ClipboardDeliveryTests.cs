using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using UE5DumpUI.Core;
using UE5DumpUI.Helpers;
using UE5DumpUI.Models;
using UE5DumpUI.Services;
using UE5DumpUI.ViewModels;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// The DELIVERY half of the clipboard contract — blind-spot sweep round 3, sub-shape (a).
///
/// <para>Round 3 classified all 57 <c>CopyToClipboardAsync</c> call sites: 55 dropped the
/// result and 29 then claimed success anyway. 14 of the 29 are DELIVERY copies, where the
/// clipboard is the deliverable — a generated CE script the status line tells the user to
/// paste into Cheat Engine. When the write does nothing and the app says it worked, the
/// user pastes <b>the previous clipboard content</b> into CE and runs it against a live
/// game.</para>
///
/// <para><b>Why these tests are behavioural and not source-text assertions.</b> Every one
/// of those 14 sites already sat inside a <c>try/catch</c> that looked like handling. It
/// is dead code: <c>IPlatformService.CopyToClipboardAsync</c> documents that it never
/// throws for an ordinary clipboard failure, so only the returned <c>bool</c> carries it.
/// A test that greps for a <c>catch</c> would have passed on every defective site.</para>
///
/// <para>⚠ The existing doubles hard-code <c>Task.FromResult(true)</c>
/// (<c>InstanceFinderViewModelTests.NoopPlatform</c> and ~8 siblings), which is exactly
/// why no test could observe the false path before this file. <see cref="RefusingPlatform"/>
/// is the negative control the suite was missing.</para>
/// </summary>
public class ClipboardDeliveryTests
{
    /// <summary>A platform whose clipboard refuses every write — the state the 14 sites
    /// claimed success over. Records what it was asked to copy so a test can prove the
    /// payload was built before the write was attempted.</summary>
    private sealed class RefusingPlatform : IPlatformService
    {
        public List<string> Attempts { get; } = new();
        public bool TryAcquireSingleInstance() => true;
        public void ReleaseSingleInstance() { }
        public string GetAppDataPath() => System.IO.Path.GetTempPath();
        public string GetLogDirectoryPath() => System.IO.Path.GetTempPath();
        public Task<bool> CopyToClipboardAsync(string text)
        {
            Attempts.Add(text);
            return Task.FromResult(false);          // "did nothing", per the contract
        }
        public Task RevealInExplorerAsync(string path) => Task.CompletedTask;
        public string GetMachineName() => "TEST";
        public void CloseImeForWindow(IntPtr windowHandle) { }
        public Task<string?> ShowSaveFileDialogAsync(string a, string b, string c)
            => Task.FromResult<string?>(null);
    }

    private sealed class AcceptingPlatform : IPlatformService
    {
        public List<string> Attempts { get; } = new();
        public bool TryAcquireSingleInstance() => true;
        public void ReleaseSingleInstance() { }
        public string GetAppDataPath() => System.IO.Path.GetTempPath();
        public string GetLogDirectoryPath() => System.IO.Path.GetTempPath();
        public Task<bool> CopyToClipboardAsync(string text)
        {
            Attempts.Add(text);
            return Task.FromResult(true);
        }
        public Task RevealInExplorerAsync(string path) => Task.CompletedTask;
        public string GetMachineName() => "TEST";
        public void CloseImeForWindow(IntPtr windowHandle) { }
        public Task<string?> ShowSaveFileDialogAsync(string a, string b, string c)
            => Task.FromResult<string?>(null);
    }

    [Fact]
    public async Task ARefusedWrite_ReportsFailure()
    {
        var p = new RefusingPlatform();
        Assert.False(await ClipboardDelivery.TryAsync(p, "<CheatEntry>...</CheatEntry>"));
        Assert.Single(p.Attempts);          // it really did try
    }

    [Fact]
    public async Task AnAcceptedWrite_ReportsSuccess_TheControl()
    {
        // Without this the "false" case above is satisfied by a helper that always fails.
        var p = new AcceptingPlatform();
        Assert.True(await ClipboardDelivery.TryAsync(p, "<CheatEntry>...</CheatEntry>"));
        Assert.Equal("<CheatEntry>...</CheatEntry>", Assert.Single(p.Attempts));
    }

    [Fact]
    public async Task ANullPlatform_IsFailure_NotSilentSuccess()
    {
        // Several VMs hold IPlatformService as nullable. `platform?.Copy...` would leave
        // the caller claiming delivery over a write that was never even attempted.
        Assert.False(await ClipboardDelivery.TryAsync(null, "<CheatEntry/>"));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public async Task AnEmptyPayload_IsFailure(string? payload)
    {
        var p = new AcceptingPlatform();
        Assert.False(await ClipboardDelivery.TryAsync(p, payload!));
        Assert.Empty(p.Attempts);           // and it does not clear the user's clipboard
    }

    [Fact]
    public void FailureText_TellsTheUserNotToPaste()
    {
        // The actionable part is not "the copy failed" but "the clipboard still holds
        // something else" — otherwise the user pastes an older AA script into CE.
        var msg = ClipboardDelivery.FailureText("the CE AA script");
        Assert.Contains("the CE AA script", msg);
        Assert.Contains("NOT delivered", msg);
        Assert.Contains("do not paste", msg, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FailureText_PutsTheImperativeWhereTruncationCannotEatIt()
    {
        // ⛔ THE REGRESSION THIS PINS, measured on a live run 2026-09-09
        // ([CLIPELLIPSIS-2026-09-09]): the MainWindow toolbar caps StatusText and
        // ErrorMessage at MaxWidth=360 with CharacterEllipsis (MainWindow.axaml's status bar) and
        // truncated this message at roughly character 50 —
        //   "ERROR: could not write to the clipboard — the inject ..."
        // while "so do not paste" sat at character 135 of 206. The one sentence the message
        // exists to deliver was the one the user could not see.
        //
        // 40 is well inside any plausible truncation of a 360px-wide 12pt line, and the
        // assertion is on the INDEX rather than on presence because presence was already
        // true when the defect was live.
        var msg = ClipboardDelivery.FailureText("the CE AA script");
        var idx = msg.IndexOf("do not paste", StringComparison.OrdinalIgnoreCase);
        Assert.InRange(idx, 0, 40);

        // ⚠ And it must stay ahead of the caller-supplied `what`, which varies in length —
        // otherwise a longer one pushes the imperative back out of view again.
        Assert.True(idx < msg.IndexOf("the CE AA script", StringComparison.Ordinal),
            "the imperative must precede the caller-supplied subject, or the truncation "
            + "point moves with it");
    }
}

/// <summary>
/// The blind-spot sweep's round-3 tail: the CONVENIENCE copies that carry a claim -- an address, a path or a name,
/// followed by a status line saying it was copied. The sweep left them outside gate 17b on purpose (their payload
/// is not a script), and each set "Copied ..." whatever <c>CopyToClipboardAsync</c> returned. Losing an address
/// costs a click, but a line that says it arrived sends the user to paste the PREVIOUS clipboard into Cheat Engine
/// instead of re-clicking.
///
/// <para>Each test copies once through an accepting clipboard (the control: the claim is still made when it is true),
/// then again through one that refuses the write, and asks that the second line stop claiming the copy and say not to
/// paste. Behavioural for the view models; the two code-built dialogs are pinned in source, as no unit test reaches
/// a Window here.</para>
/// </summary>
public class ConvenienceCopyClaimTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"UE5DumpClipClaim_{Guid.NewGuid():N}");
    private readonly MockLoggingService _log = new();

    public ConvenienceCopyClaimTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    private SnapshotStore Store() => new(new MockPlatformService(_dir));

    /// <summary>The line a refused copy leaves: no claim, the imperative, and the text that did not arrive.</summary>
    private static void AssertNotClaimed(string status, MockPlatformService p)
    {
        Assert.NotNull(p.LastClipboard);                                  // it really did try
        Assert.DoesNotContain("Copied", status, StringComparison.Ordinal);
        Assert.Contains("do not paste", status, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(p.LastClipboard!, status, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ClassPivot_CopyAddress_RefusedWrite_IsNotClaimed()
    {
        var p = new MockPlatformService(_dir);
        var vm = new ClassPivotViewModel(Store(), _log, p);
        var row = new PivotResultRow { ObjAddr = "0x7FF600001000", KeyValue = "K" };

        await vm.CopyAddressCommand.ExecuteAsync(row);
        Assert.StartsWith("Copied 7FF600001000", vm.StatusText, StringComparison.Ordinal);

        p.RefuseClipboard = true;
        await vm.CopyAddressCommand.ExecuteAsync(row);
        AssertNotClaimed(vm.StatusText, p);
    }

    [Fact]
    public async Task DumpExplorer_CopyPath_RefusedWrite_IsNotClaimed()
    {
        var p = new MockPlatformService(_dir);
        var vm = new DumpExplorerViewModel(new StubDumpService(), _log, p);
        var row = new DumpEntry { Name = "Thing", Path = "/Script/Game.Thing" };

        await vm.CopyPathCommand.ExecuteAsync(row);
        Assert.StartsWith("Copied path: /Script/Game.Thing", vm.StatusText, StringComparison.Ordinal);

        p.RefuseClipboard = true;
        await vm.CopyPathCommand.ExecuteAsync(row);
        AssertNotClaimed(vm.StatusText, p);
    }

    [Fact]
    public async Task RelatedObjects_CopyAddress_RefusedWrite_IsNotClaimed()
    {
        var p = new MockPlatformService(_dir);
        var vm = new RelatedObjectsViewModel(new StubDumpService(), _log, p);
        var row = new RelatedObject { Address = "0x7FF600002000", Name = "N", ClassName = "C" };

        await vm.CopyAddressCommand.ExecuteAsync(row);
        Assert.StartsWith("Copied 0x7FF600002000", vm.StatusText, StringComparison.Ordinal);

        p.RefuseClipboard = true;
        await vm.CopyAddressCommand.ExecuteAsync(row);
        AssertNotClaimed(vm.StatusText, p);
    }

    [Fact]
    public async Task Snapshot_CopyDiffAddress_RefusedWrite_IsNotClaimed()
    {
        var p = new MockPlatformService(_dir);
        var vm = new SnapshotViewModel(new StubDumpService(), Store(), _log, platform: p);
        var row = new SnapshotDiffRow { ObjAddr = "0x7FF600001000", PropOffset = 0x40, ClassName = "C", PropName = "HP" };

        await vm.CopyDiffAddressCommand.ExecuteAsync(row);
        Assert.StartsWith("Copied 7FF600001040", vm.DiffStatusText, StringComparison.Ordinal);

        p.RefuseClipboard = true;
        await vm.CopyDiffAddressCommand.ExecuteAsync(row);
        AssertNotClaimed(vm.DiffStatusText, p);
    }

    [Fact]
    public async Task Snapshot_CopyGroupSlotAddress_RefusedWrite_IsNotClaimed()
    {
        var p = new MockPlatformService(_dir);
        var vm = new SnapshotViewModel(new StubDumpService(), Store(), _log, platform: p);
        var slot = new GroupSlotMatch { Addr = "0x7FF600001040", ClassName = "C", FieldName = "HP" };

        await vm.CopyGroupSlotAddressCommand.ExecuteAsync(slot);
        Assert.StartsWith("Copied 7FF600001040", vm.GroupStatusText, StringComparison.Ordinal);

        p.RefuseClipboard = true;
        await vm.CopyGroupSlotAddressCommand.ExecuteAsync(slot);
        AssertNotClaimed(vm.GroupStatusText, p);
    }

    [Fact]
    public async Task Spc_CopyAddress_RefusedWrite_IsNotClaimed()
    {
        var p = new MockPlatformService(_dir);
        var vm = new SpcQueryViewModel(Store(), _log, p);
        var row = new SpcResultRow { ObjAddr = "0x7FF600001000", PropOffset = 0x40, ClassName = "C", PropName = "HP" };

        await vm.CopyAddressCommand.ExecuteAsync(row);
        Assert.StartsWith("Copied 7FF600001040", vm.StatusText, StringComparison.Ordinal);

        p.RefuseClipboard = true;
        await vm.CopyAddressCommand.ExecuteAsync(row);
        AssertNotClaimed(vm.StatusText, p);
    }

    [Fact]
    public async Task Spc_CopyGroupSlotAddress_RefusedWrite_IsNotClaimed()
    {
        var p = new MockPlatformService(_dir);
        var vm = new SpcQueryViewModel(Store(), _log, p);
        var slot = new GroupSlotMatch { Addr = "0x7FF600001040", ClassName = "C", FieldName = "HP" };

        await vm.CopyGroupSlotAddressCommand.ExecuteAsync(slot);
        Assert.StartsWith("Copied 7FF600001040", vm.GroupStatusText, StringComparison.Ordinal);

        p.RefuseClipboard = true;
        await vm.CopyGroupSlotAddressCommand.ExecuteAsync(slot);
        AssertNotClaimed(vm.GroupStatusText, p);
    }

    [Fact]
    public async Task InstanceFinder_CopyContainerAddress_RefusedWrite_IsNotClaimed()
    {
        var p = new MockPlatformService(_dir);
        var vm = new InstanceFinderViewModel(new StubDumpService(), _log, p);
        var match = new ContainerMatch { OwnerAddress = "0x7FF600003000", OwnerClassName = "C" };

        await vm.CopyContainerAddressCommand.ExecuteAsync(match);
        Assert.StartsWith("Copied ", vm.LookupStatusText, StringComparison.Ordinal);

        p.RefuseClipboard = true;
        await vm.CopyContainerAddressCommand.ExecuteAsync(match);
        AssertNotClaimed(vm.LookupStatusText, p);
    }

    /// <summary>
    /// The two code-built dialogs. Their Copy button set a green "Copied: X" after a copy whose result was thrown away;
    /// the handler must bind the result and keep a failure line for the refused write. Pinned in source because no
    /// unit test here can open a Window.
    /// </summary>
    [Theory]
    [InlineData("ui/UE5DumpUI/Views/FunctionPropsDialog.cs")]
    [InlineData("ui/UE5DumpUI/Views/PropertyXrefDialog.cs")]
    public void Dialog_CopyButton_ClaimsOnlyACopyThatArrived(string relative)
    {
        var source = File.ReadAllText(NumericInputCoercionTests.RepoFile(relative)).Replace("\r\n", "\n");
        int start = source.IndexOf("private async void OnCopyClicked(", StringComparison.Ordinal);
        Assert.True(start >= 0, $"no OnCopyClicked in {relative}");
        int end = source.IndexOf("\n    }\n", start, StringComparison.Ordinal);
        var body = source[start..end];

        Assert.DoesNotMatch(new Regex(@"(?m)^\s*await\s+_platform\.CopyToClipboardAsync\("), body);
        Assert.Matches(new Regex(@"bool\s+copied\s*=\s*await\s+_platform\.CopyToClipboardAsync\("), body);
        Assert.Contains("NotCopiedText(", body, StringComparison.Ordinal);
    }
}
