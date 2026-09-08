using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UE5DumpUI.Core;
using UE5DumpUI.Helpers;
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
}
