using System.IO;
using UE5DumpUI;
using UE5DumpUI.Services;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// Locks the ExperimentalGate persistence contract: default off, survives a
/// round-trip through a fresh instance, raises Changed only on real flips, keeps
/// an untick's owed teardown across a restart, and writes to
/// %AppData%\UE5CEDumper\experimental.json. Reuses
/// <see cref="MockPlatformService"/> from AobUsageServiceTests.
/// </summary>
public class ExperimentalGateTests : IDisposable
{
    private readonly string _tempDir;
    private readonly MockPlatformService _platform;

    public ExperimentalGateTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"UE5DumpExpTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _platform = new MockPlatformService(_tempDir);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); }
        catch { /* best effort */ }
    }

    [Fact]
    public void DefaultsToDisabled_WhenNoFile()
    {
        var gate = new ExperimentalGate(_platform);
        Assert.False(gate.IsEnabled);
        Assert.False(File.Exists(gate.FilePath)); // load must not create the file
    }

    [Fact]
    public void FilePath_IsUnderAppDataWithExpectedName()
    {
        var gate = new ExperimentalGate(_platform);
        Assert.Equal(
            Path.Combine(_tempDir, Constants.LogFolderName, Constants.ExperimentalSettingsFile),
            gate.FilePath);
    }

    [Fact]
    public void EnablingPersists_AndFreshInstanceReadsItBack()
    {
        var gate = new ExperimentalGate(_platform);
        gate.IsEnabled = true;
        Assert.True(File.Exists(gate.FilePath));

        // A brand-new gate over the same directory must observe the saved flag.
        var reopened = new ExperimentalGate(_platform);
        Assert.True(reopened.IsEnabled);

        reopened.IsEnabled = false;
        Assert.False(new ExperimentalGate(_platform).IsEnabled);
    }

    [Fact]
    public void SnapshotQuotaMb_DefaultsTo1GB_AndPersists()
    {
        var gate = new ExperimentalGate(_platform);
        Assert.Equal(1024, gate.SnapshotQuotaMb);   // default 1 GB

        gate.SnapshotQuotaMb = 2048;
        Assert.Equal(2048, new ExperimentalGate(_platform).SnapshotQuotaMb);  // persisted

        // Quota and the enable flag round-trip independently.
        gate.IsEnabled = true;
        var reopened = new ExperimentalGate(_platform);
        Assert.True(reopened.IsEnabled);
        Assert.Equal(2048, reopened.SnapshotQuotaMb);
    }

    [Fact]
    public void SnapshotQuotaMb_Unlimited_Zero_SurvivesARoundTrip()
    {
        // [W1-QUOTA-UNLIMITED]. 0 means "Unlimited", and it is chosen by the user (the quota
        // combo) or BY ITSELF (ApplyAutoQuota, once the retained set outgrows the 5 GB preset).
        // Under DefaultIgnoreCondition = WhenWritingDefault the key was OMITTED -- 0 is
        // default(int) -- the next launch reloaded the = 1024 initializer, and SnapshotStore then
        // FIFO-deleted the snapshots the user had opted to keep. The test above round-trips 2048
        // only, which serializes fine and could never see it.
        var gate = new ExperimentalGate(_platform);
        gate.SnapshotQuotaMb = 0;
        Assert.Equal(0, new ExperimentalGate(_platform).SnapshotQuotaMb);

        // The flag beside it must survive the same round-trip, in both states.
        gate.IsEnabled = true;
        var reopened = new ExperimentalGate(_platform);
        Assert.True(reopened.IsEnabled);
        Assert.Equal(0, reopened.SnapshotQuotaMb);
        reopened.IsEnabled = false;
        Assert.False(new ExperimentalGate(_platform).IsEnabled);
        Assert.Equal(0, new ExperimentalGate(_platform).SnapshotQuotaMb);
    }

    [Fact]
    public void Changed_FiresOnFlip_NotOnSameValue()
    {
        var gate = new ExperimentalGate(_platform);
        int fired = 0;
        gate.Changed += (_, _) => fired++;

        gate.IsEnabled = false; // no-op (already false)
        Assert.Equal(0, fired);

        gate.IsEnabled = true;  // real flip
        Assert.Equal(1, fired);

        gate.IsEnabled = true;  // no-op (already true)
        Assert.Equal(1, fired);

        gate.IsEnabled = false; // real flip
        Assert.Equal(2, fired);
    }

    [Fact]
    public void DefaultsToUnlocked()
    {
        Assert.False(new ExperimentalGate(_platform).IsLocked);
    }

    [Fact]
    public void Lock_IsSessionOnly_NotPersisted()
    {
        var gate = new ExperimentalGate(_platform);
        gate.IsEnabled = true;
        gate.Lock();
        Assert.True(gate.IsLocked);

        // A fresh instance (restart) starts unlocked — the enable flag persists
        // but the lock does not.
        var reopened = new ExperimentalGate(_platform);
        Assert.True(reopened.IsEnabled);
        Assert.False(reopened.IsLocked);
    }

    [Fact]
    public void Lock_FiresChangedOnce_ThenIdempotent()
    {
        var gate = new ExperimentalGate(_platform);
        int fired = 0;
        gate.Changed += (_, _) => fired++;

        gate.Lock();              // real lock
        Assert.Equal(1, fired);
        gate.Lock();              // no-op (already locked)
        Assert.Equal(1, fired);
    }

    [Fact]
    public void Locked_RefusesToDisable_WithinSession()
    {
        var gate = new ExperimentalGate(_platform);
        gate.IsEnabled = true;
        gate.Lock();

        gate.IsEnabled = false;   // must be ignored while locked
        Assert.True(gate.IsEnabled);

        // But a restart clears the lock, so unticking works again.
        var reopened = new ExperimentalGate(_platform);
        Assert.False(reopened.IsLocked);
        reopened.IsEnabled = false;
        Assert.False(reopened.IsEnabled);
    }

    // ---- [EXPOFF-ROUND3-PAUSED] An untick owes the teardown of what the features run in the game until it has run
    // over a live connection; an untick made while disconnected is paid at the next connect, so it must outlive a
    // restart in between -- and only an owed untick is paid, as a connect must leave alone what a CE record runs.

    [Fact]
    public void An_untick_owes_a_teardown_and_a_restart_still_owes_it()
    {
        var gate = new ExperimentalGate(_platform);
        gate.IsEnabled = true;
        Assert.False(gate.TeardownOwed);

        gate.IsEnabled = false;
        Assert.True(gate.TeardownOwed);
        Assert.True(new ExperimentalGate(_platform).TeardownOwed);
    }

    [Fact]
    public void A_paid_teardown_and_a_tick_each_clear_it_for_good()
    {
        var gate = new ExperimentalGate(_platform);
        gate.IsEnabled = true;
        gate.IsEnabled = false;
        gate.TeardownOwed = false;   // the teardown ran
        Assert.False(new ExperimentalGate(_platform).TeardownOwed);

        gate.IsEnabled = true;
        gate.IsEnabled = false;
        gate.IsEnabled = true;       // on again: the cards show what runs
        Assert.False(gate.TeardownOwed);
        Assert.False(new ExperimentalGate(_platform).TeardownOwed);
    }

    [Fact]
    public void Paying_the_teardown_raises_nothing()
    {
        var gate = new ExperimentalGate(_platform);
        gate.IsEnabled = true;
        gate.IsEnabled = false;
        int fired = 0;
        gate.Changed += (_, _) => fired++;

        gate.TeardownOwed = false;
        Assert.Equal(0, fired);
    }

    [Fact]
    public void An_untick_the_lock_refuses_owes_nothing()
    {
        var gate = new ExperimentalGate(_platform);
        gate.IsEnabled = true;
        gate.Lock();

        gate.IsEnabled = false;
        Assert.False(gate.TeardownOwed);
    }

    [Fact]
    public void A_file_written_before_the_flag_owes_nothing()
    {
        var path = new ExperimentalGate(_platform).FilePath;
        File.WriteAllText(path, "{\n  \"enabled\": false,\n  \"snapshotQuotaMb\": 1024\n}");

        var gate = new ExperimentalGate(_platform);
        Assert.False(gate.IsEnabled);
        Assert.False(gate.TeardownOwed);
    }
}
