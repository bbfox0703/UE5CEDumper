using UE5DumpUI.Helpers;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// Pure policy behind the staging / quarantine story of audit #5 AC4-AC6. No disk.
/// </summary>
public class AtomicFileHygieneTests
{
    private const string CacheName = "UE5CEDumper.TEST-MACHINE.json";
    private static readonly DateTime Now = new(2026, 8, 19, 12, 0, 0, DateTimeKind.Utc);

    // ── staging names ────────────────────────────────────────────────

    [Fact]
    public void TempPathFor_KeepsTheDllCompatibleShape()
    {
        // Byte-compatible with Flamme::MakeTempPath — the DLL writes the same file.
        Assert.Equal(@"C:\d\" + CacheName + ".tmp.4242",
                     AtomicFileHygiene.TempPathFor(@"C:\d\" + CacheName, 4242));
    }

    // ── stale-temp selection ─────────────────────────────────────────

    [Fact]
    public void IsStaleTemp_OldTempOfThisFile_IsStale()
    {
        Assert.True(AtomicFileHygiene.IsStaleTemp(
            CacheName, CacheName + ".tmp.999",
            Now - TimeSpan.FromHours(3), Now, AtomicFileHygiene.StaleTempAge));
    }

    [Fact]
    public void IsStaleTemp_FreshTemp_IsSpared()
    {
        // The DLL may be mid-write RIGHT NOW with a different PID. Age, never liveness.
        Assert.False(AtomicFileHygiene.IsStaleTemp(
            CacheName, CacheName + ".tmp.999",
            Now - TimeSpan.FromSeconds(5), Now, AtomicFileHygiene.StaleTempAge));
    }

    [Theory]
    // The cache file itself, the reset backups and the quarantine all live in the same
    // app-data root. None of them may be swept.
    [InlineData("UE5CEDumper.TEST-MACHINE.json")]
    [InlineData("UE5CEDumper.TEST-MACHINE.json.001")]
    [InlineData("UE5CEDumper.TEST-MACHINE.json.corrupt-20260819-120000000")]
    // A DIFFERENT machine's cache, and a different app-data file entirely.
    [InlineData("UE5CEDumper.OTHER-PC.json.tmp.999")]
    [InlineData("ui-options.json.tmp.999")]
    // The bare prefix with no PID after it is not a staging file.
    [InlineData("UE5CEDumper.TEST-MACHINE.json.tmp.")]
    public void IsStaleTemp_OutOfScopeNames_AreNeverStale(string candidate)
    {
        Assert.False(AtomicFileHygiene.IsStaleTemp(
            CacheName, candidate,
            Now - TimeSpan.FromDays(30), Now, AtomicFileHygiene.StaleTempAge));
    }

    // ── quarantine naming + pruning ──────────────────────────────────

    [Fact]
    public void QuarantineNameFor_IsSortableAndMillisecondUnique()
    {
        var a = AtomicFileHygiene.QuarantineNameFor(CacheName, new DateTime(2026, 8, 19, 12, 0, 0, 100, DateTimeKind.Utc));
        var b = AtomicFileHygiene.QuarantineNameFor(CacheName, new DateTime(2026, 8, 19, 12, 0, 0, 900, DateTimeKind.Utc));

        Assert.NotEqual(a, b);                                  // same second, still distinct
        Assert.True(string.CompareOrdinal(a, b) < 0);           // lexicographic == chronological
        Assert.StartsWith(AtomicFileHygiene.CorruptPrefixFor(CacheName), a);
    }

    [Fact]
    public void SelectCorruptCopiesToPrune_DropsOldestKeepsNewest()
    {
        var names = new[]
        {
            CacheName + ".corrupt-20260819-120000000",
            CacheName + ".corrupt-20260817-090000000",   // oldest
            CacheName + ".corrupt-20260818-100000000",
        };

        var prune = AtomicFileHygiene.SelectCorruptCopiesToPrune(names, CacheName, keep: 2);

        Assert.Single(prune);
        Assert.Equal(CacheName + ".corrupt-20260817-090000000", prune[0]);
    }

    [Fact]
    public void SelectCorruptCopiesToPrune_UnderTheCap_PrunesNothing()
    {
        var names = new[] { CacheName + ".corrupt-20260819-120000000" };
        Assert.Empty(AtomicFileHygiene.SelectCorruptCopiesToPrune(names, CacheName, keep: 5));
    }

    [Fact]
    public void SelectCorruptCopiesToPrune_IgnoresEverythingButOurQuarantine()
    {
        var names = new[]
        {
            CacheName,                                            // the live cache
            CacheName + ".001",                                   // a reset backup
            CacheName + ".tmp.4242",                              // a staging file
            "UE5CEDumper.OTHER-PC.json.corrupt-20260101-000000000",
        };

        Assert.Empty(AtomicFileHygiene.SelectCorruptCopiesToPrune(names, CacheName, keep: 0));
    }
}
