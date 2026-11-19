using UE5DumpUI.Models;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// Locks the Diagnostics (Sense) model contract — the numbers that settle
/// multipipe-eval.md's open question about DLL-side serial-dispatch head-of-line
/// blocking.
///
/// The two behaviours worth pinning are both about NOT lying: CPU% must be
/// distinguishable from "0%" before a second sample exists, and share-of-busy
/// must be derived from the same total the rows are summed from.
/// </summary>
public class DiagnosticsModelTests
{
    [Fact]
    public void CpuPercent_defaults_to_unknown_not_zero()
    {
        // -1 means "needs a second sample to difference against". Showing 0%
        // would read as "idle", which is a different and wrong claim.
        var p = new DiagnosticsProcess();
        Assert.False(p.HasCpu);
        Assert.Equal(-1.0, p.CpuPercent);
    }

    [Theory]
    [InlineData(0.0, true)]
    [InlineData(12.5, true)]
    [InlineData(-1.0, false)]
    public void HasCpu_is_true_only_for_a_real_sample(double cpu, bool expected)
    {
        Assert.Equal(expected, new DiagnosticsProcess { CpuPercent = cpu }.HasCpu);
    }

    [Fact]
    public void Share_percentages_sum_to_100_over_the_reported_rows()
    {
        // Mirrors DumpService's derivation: share is computed against the DLL's
        // total_busy_ms, so the column can never disagree with the rows.
        long total = 200;
        var rows = new[]
        {
            new DiagnosticsCommandEntry { Cmd = "a", TotalMs = 150 },
            new DiagnosticsCommandEntry { Cmd = "b", TotalMs = 40 },
            new DiagnosticsCommandEntry { Cmd = "c", TotalMs = 10 },
        };
        foreach (var r in rows) r.SharePercent = total > 0 ? r.TotalMs * 100.0 / total : 0.0;

        Assert.Equal(75.0, rows[0].SharePercent);
        Assert.Equal(20.0, rows[1].SharePercent);
        Assert.Equal(5.0, rows[2].SharePercent);
        double sum = 0;
        foreach (var r in rows) sum += r.SharePercent;
        Assert.Equal(100.0, sum, 6);
    }

    [Fact]
    public void Share_is_zero_rather_than_NaN_when_nothing_has_been_dispatched()
    {
        // A freshly reset session has total_busy_ms == 0; dividing by it would
        // put NaN in the grid.
        long total = 0;
        var r = new DiagnosticsCommandEntry { Cmd = "a", TotalMs = 0 };
        r.SharePercent = total > 0 ? r.TotalMs * 100.0 / total : 0.0;
        Assert.Equal(0.0, r.SharePercent);
        Assert.False(double.IsNaN(r.SharePercent));
    }

    [Fact]
    public void HasFired_is_false_for_the_never_fired_sentinel()
    {
        // The PE hook installs lazily on the first invoke, so "never fired" is the
        // NORMAL state on a fresh connection. -1 is the DLL's UINT64_MAX sentinel
        // mapped into signed range at the wire boundary; a huge positive value is
        // the same sentinel arriving from an older DLL that does not map it.
        Assert.False(new DiagnosticsGameThread().HasFired);
        Assert.False(new DiagnosticsGameThread { MsSinceLastFire = -1 }.HasFired);
        Assert.False(new DiagnosticsGameThread
        {
            MsSinceLastFire = long.MaxValue, HookFireCount = 0
        }.HasFired);
    }

    [Fact]
    public void HasFired_is_true_only_with_a_real_age_and_a_real_count()
    {
        Assert.True(new DiagnosticsGameThread
        {
            MsSinceLastFire = 16, HookFireCount = 918273
        }.HasFired);
        // An age without any fires is incoherent — don't present it as live.
        Assert.False(new DiagnosticsGameThread
        {
            MsSinceLastFire = 16, HookFireCount = 0
        }.HasFired);
    }

    [Fact]
    public void Result_defaults_are_safe_to_bind_before_the_first_fetch()
    {
        // The panel binds these before any snapshot arrives.
        var d = new DiagnosticsResult();
        Assert.NotNull(d.Commands);
        Assert.Empty(d.Commands);
        Assert.NotNull(d.Process);
        Assert.NotNull(d.GameThread);
        Assert.False(d.Process.HasCpu);
    }
}
