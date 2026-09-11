using UE5DumpUI.Core;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// Whether a volume's Recycle Bin will actually accept a delete.
///
/// <para>This suite exists because the shipped detector answered the question with
/// <c>SHQueryRecycleBin(root) == S_OK</c> and that call cannot see the answer. Measured on a
/// real fixed volume with <c>NukeOnDelete=1</c> (2026-08-12): the API returned <c>S_OK</c>,
/// the probe therefore said the bin worked, and an <c>FOF_ALLOWUNDO</c> delete of a
/// throwaway file returned <c>rc=0 / !aborted</c> while the bin's item count stayed put and
/// the file was gone. The caller reported "moved to the Recycle Bin" for a permanent
/// destruction — which is the precise outcome B13/B41 was written to prevent.</para>
///
/// <para>The cases below are the combinations that machine could not produce, plus the two
/// that it did.</para>
/// </summary>
public class RecycleBinPolicyTests
{
    // ── the default machine: nothing has ever been configured ────────────────

    [Fact]
    public void NothingConfigured_BinWorks()
    {
        // Every value absent. This is the overwhelmingly common case and MUST come out
        // enabled, otherwise the cleanup feature refuses on every clean machine.
        Assert.False(RecycleBinPolicy.IsDisabled(null, null, null, null, null));
    }

    [Fact]
    public void AllZeroes_BinWorks()
    {
        // Present-and-zero must agree with absent. A machine that has had the property
        // sheet opened and closed once looks like this.
        Assert.False(RecycleBinPolicy.IsDisabled(0, 0, 0, 0, 0));
    }

    // ── the case the old probe missed, and the reason this file exists ───────

    [Fact]
    public void PerVolumeNukeOnDelete_DisablesTheBin()
    {
        // The measured T: / D: case. Nothing else set; only the volume flag.
        Assert.True(RecycleBinPolicy.IsDisabled(null, null, null, null, 1));
    }

    [Fact]
    public void PerVolumeNukeOnDelete_OnlyAffectsThatVolume()
    {
        // Sibling volume on the same machine reads its own (absent) flag and stays
        // enabled. Guards against a fix that keys off a global signal and refuses
        // everywhere the moment one volume is turned off.
        Assert.False(RecycleBinPolicy.IsDisabled(null, null, null, null, null));
    }

    // ── "use one setting for all locations" ──────────────────────────────────

    [Fact]
    public void UseGlobalSettings_GlobalNukeWins()
    {
        Assert.True(RecycleBinPolicy.IsDisabled(null, null, 1, 1, null));
    }

    [Fact]
    public void UseGlobalSettings_IgnoresTheVolumeFlag_WhenGlobalSaysKeep()
    {
        // The direction that is easy to get wrong: with UseGlobalSettings=1 Explorer does
        // NOT consult the per-volume flag, so a stale volume flag of 1 must not make us
        // refuse a bin that actually works.
        Assert.False(RecycleBinPolicy.IsDisabled(null, null, 1, 0, 1));
    }

    [Fact]
    public void UseGlobalSettings_IgnoresTheVolumeFlag_WhenGlobalSaysNuke()
    {
        // ...and the mirror: a per-volume 0 must not rescue a global 1.
        Assert.True(RecycleBinPolicy.IsDisabled(null, null, 1, 1, 0));
    }

    [Fact]
    public void UseGlobalSettingsZero_FallsBackToTheVolumeFlag()
    {
        Assert.True(RecycleBinPolicy.IsDisabled(null, null, 0, 0, 1));
        Assert.False(RecycleBinPolicy.IsDisabled(null, null, 0, 1, 0));
    }

    // ── [A3-RECYCLE-GUID-FAILOPEN] a FAILED volume lookup is not "absent" ───────────────
    // The docs promised a failed lookup fails closed; it failed OPEN: the caller passed null, IsDisabled tests `== 1`,
    // null read as "bin enabled", and the verdict fell to SHQueryRecycleBin -- measured blind to NukeOnDelete. Exposure:
    // SUBST and RAM-disk-style volumes (no fixed volume on the measuring machine fails the lookup).

    [Fact]
    public void FailedVolumeLookup_WhereTheVolumeFlagWouldDecide_FailsClosed()
    {
        Assert.True(RecycleBinPolicy.IsDisabled(null, null, null, null, null, volumeLookupFailed: true));
        Assert.True(RecycleBinPolicy.IsDisabled(null, null, 0, 0, null, volumeLookupFailed: true));
    }

    [Fact]
    public void FailedVolumeLookup_DoesNotOverrideWhatDecidesWithoutTheVolumeFlag()
    {
        // The control, and the reason a refuse-everything fix is wrong: under "use one setting for all locations"
        // Explorer never reads the volume flag, so its lookup cannot matter -- a global 0 keeps the bin, a global 1
        // disables it. A policy disables regardless.
        Assert.False(RecycleBinPolicy.IsDisabled(null, null, 1, 0, null, volumeLookupFailed: true));
        Assert.True(RecycleBinPolicy.IsDisabled(null, null, 1, 1, null, volumeLookupFailed: true));
        Assert.True(RecycleBinPolicy.IsDisabled(1, null, null, null, null, volumeLookupFailed: true));
    }

    [Fact]
    public void ThePlatformService_ReportsAFailedLookup_RatherThanPassingNullAlone()
    {
        string? src = null;
        var dir = new System.IO.DirectoryInfo(System.AppContext.BaseDirectory);
        for (int i = 0; i < 8 && dir is not null && src is null; i++, dir = dir.Parent)
        {
            var c = System.IO.Path.Combine(dir.FullName, "ui", "UE5DumpUI", "Services", "WindowsPlatformService.cs");
            if (System.IO.File.Exists(c)) src = System.IO.File.ReadAllText(c);
        }
        Assert.NotNull(src);
        Assert.Contains("volumeLookupFailed: guid.Length == 0", src!);
    }

    // ── Group Policy outranks the property sheet ─────────────────────────────

    [Fact]
    public void MachinePolicy_DisablesEverything()
    {
        Assert.True(RecycleBinPolicy.IsDisabled(1, null, null, null, null));
        // ...even when every user-level setting says the bin is fine.
        Assert.True(RecycleBinPolicy.IsDisabled(1, 0, 1, 0, 0));
    }

    [Fact]
    public void UserPolicy_DisablesEverything()
    {
        Assert.True(RecycleBinPolicy.IsDisabled(null, 1, null, null, null));
    }

    [Fact]
    public void MachinePolicyIsCheckedBeforeUserPolicy()
    {
        // A user hive that says 0 cannot override a machine policy of 1.
        Assert.True(RecycleBinPolicy.IsDisabled(1, 0, null, null, null));
    }

    [Fact]
    public void PolicyZero_DoesNotDisable()
    {
        // "Policy present and explicitly off" must not be read as "policy present".
        Assert.False(RecycleBinPolicy.IsDisabled(0, 0, null, null, null));
    }

    // ── guid extraction ──────────────────────────────────────────────────────

    [Fact]
    public void VolumeGuid_ExtractedFromVolumeName()
    {
        Assert.Equal("{5d2d1806-7aac-4c0b-9215-48b46d0445ff}",
            RecycleBinPolicy.VolumeGuidFromVolumeName(@"\\?\Volume{5d2d1806-7aac-4c0b-9215-48b46d0445ff}\"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(@"D:\")]
    [InlineData(@"\\?\Volume{unterminated")]
    [InlineData(@"\\?\Volume-no-braces\")]
    public void VolumeGuid_UnrecognisedShapesYieldEmpty(string? input)
    {
        // "" is the signal for "could not identify the volume". The caller must then pass
        // the per-volume value as ABSENT rather than as 0 — reading a failed lookup as
        // "enabled" is exactly how a disabled volume would slip back through.
        Assert.Equal("", RecycleBinPolicy.VolumeGuidFromVolumeName(input));
    }
}
