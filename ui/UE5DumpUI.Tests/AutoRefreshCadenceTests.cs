using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UE5DumpUI.Helpers;
using UE5DumpUI.Models;
using UE5DumpUI.ViewModels;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// Live Walker auto-refresh cadence and suspend/resume lifecycle — `[AUTOREFRESH-2026-08-19]`.
///
/// <para>Reported by the maintainer against dist <b>1.0.0.3262</b>: "Live Walker `Auto`
/// refresh 無效，秒數數到0後就停在那" — the countdown runs down to 0 and stays there while
/// nothing refreshes. The session's pipe log carries <b>zero</b> periodic walks over 21
/// minutes and <b>zero</b> errors, so the panel really did nothing at all and said nothing
/// about it.</para>
///
/// <para>Two independent defects sit behind that, and both are pinned here:</para>
/// <list type="number">
/// <item><b>The countdown could freeze.</b> Its only reset lived inside the refresh tick,
///   PAST the tick's early-return guard, so one permanently-skipping condition pinned the
///   label at "0s" forever with the Auto toggle still reading ON. <c>RefreshAsync</c>
///   catches every exception, so a *failing* refresh could not cause this — only a
///   *skipped* one could.</item>
/// <item><b>Nothing ever brought auto-refresh back.</b> Switching tabs stopped it, and (as
///   of audit X5) so does a pipe disconnect — neither had a path back on, so Auto stayed
///   silently off for the rest of the session.</item>
/// </list>
/// </summary>
public class AutoRefreshCadenceTests
{
    // ── The countdown rule ──────────────────────────────────────────────────

    [Fact]
    public void NormalRunCountsDownAndShowsTheNumber()
    {
        var step = AutoRefreshCadence.Step(remaining: 8, intervalSec: 10, AutoRefreshSkip.None);
        Assert.Equal(7, step.Remaining);
        Assert.Equal("sec · 7s", step.Label);
    }

    [Fact]
    public void ReachingZeroReArmsToTheFullInterval()
    {
        // The counter displays the TIMER'S PERIOD, which keeps elapsing whether or not
        // the last tick did any work — so it must roll over rather than clamp.
        var step = AutoRefreshCadence.Step(remaining: 1, intervalSec: 10, AutoRefreshSkip.None);
        Assert.Equal(10, step.Remaining);
    }

    [Fact]
    public void ARefreshInFlightHoldsTheNumberAndSaysSo()
    {
        var step = AutoRefreshCadence.Step(remaining: 4, intervalSec: 10, AutoRefreshSkip.InProgress);
        Assert.Equal(4, step.Remaining);   // held, not decremented
        Assert.Equal(AutoRefreshCadence.LabelRefreshing, step.Label);
    }

    /// <summary>
    /// The regression itself, with the shipped rule modelled alongside as a negative
    /// control: run both for four full intervals under a condition that never clears.
    /// The 3262 rule ends pinned at 0; the fixed rule never can be.
    /// </summary>
    [Theory]
    [InlineData(AutoRefreshSkip.Editing)]
    [InlineData(AutoRefreshSkip.NoData)]
    public void ASkippedTickCanNeverFreezeTheCountdown(AutoRefreshSkip skip)
    {
        const int interval = 10;

        int shipped = interval;    // build 3262: decrement, clamp at 0, reset only after a refresh
        int fixedRemaining = interval;
        string label = "";

        for (int i = 0; i < interval * 4; i++)
        {
            shipped = shipped - 1 < 0 ? 0 : shipped - 1;

            var step = AutoRefreshCadence.Step(fixedRemaining, interval, skip);
            fixedRemaining = step.Remaining;
            label = step.Label;
        }

        Assert.Equal(0, shipped);                          // the defect, reproduced
        Assert.InRange(fixedRemaining, 1, interval);       // the fix: it cannot stick
        Assert.NotEqual("sec · 0s", label);

        // ...and it names the reason instead of showing a number that is not moving.
        Assert.Equal(skip == AutoRefreshSkip.Editing
                        ? AutoRefreshCadence.LabelEditing
                        : AutoRefreshCadence.LabelNoData, label);
    }

    [Fact]
    public void AZeroOrNegativeIntervalCannotProduceANonAdvancingCounter()
    {
        // Guards a DispatcherTimer that would otherwise spin, and a re-arm to 0.
        var step = AutoRefreshCadence.Step(remaining: 1, intervalSec: 0, AutoRefreshSkip.None);
        Assert.Equal(1, step.Remaining);
        Assert.Equal(1, AutoRefreshCadence.NormalizeInterval(0, 0));
        Assert.Equal(6, AutoRefreshCadence.NormalizeInterval(3, 6));    // benchmarked floor wins
        Assert.Equal(12, AutoRefreshCadence.NormalizeInterval(12, 6));  // user's value wins
    }

    // ── Which condition skipped the tick ────────────────────────────────────

    [Fact]
    public void ClassifyReportsEachReasonAndPrefersTheInFlightOne()
    {
        Assert.Equal(AutoRefreshSkip.None,
            AutoRefreshCadence.Classify(false, false, true, "0x1000"));
        Assert.Equal(AutoRefreshSkip.InProgress,
            AutoRefreshCadence.Classify(true, false, true, "0x1000"));
        Assert.Equal(AutoRefreshSkip.Editing,
            AutoRefreshCadence.Classify(false, true, true, "0x1000"));
        Assert.Equal(AutoRefreshSkip.NoData,
            AutoRefreshCadence.Classify(false, false, false, "0x1000"));
        Assert.Equal(AutoRefreshSkip.NoData,
            AutoRefreshCadence.Classify(false, false, true, ""));
        Assert.Equal(AutoRefreshSkip.NoData,
            AutoRefreshCadence.Classify(false, false, true, null));

        // In-flight outranks editing: the panel's other state is mid-update while it runs.
        Assert.Equal(AutoRefreshSkip.InProgress,
            AutoRefreshCadence.Classify(true, true, false, null));
    }

    // ── The resume predicate, with every negative control ───────────────────

    [Fact]
    public void ResumeRequiresAllFourConditions()
    {
        Assert.True(AutoRefreshCadence.ShouldResume(true, false, true, "0x1000"));

        Assert.False(AutoRefreshCadence.ShouldResume(false, false, true, "0x1000")); // nothing pending
        Assert.False(AutoRefreshCadence.ShouldResume(true, true, true, "0x1000"));   // already running
        Assert.False(AutoRefreshCadence.ShouldResume(true, false, false, "0x1000")); // nothing rooted
        Assert.False(AutoRefreshCadence.ShouldResume(true, false, true, ""));        // no address
        Assert.False(AutoRefreshCadence.ShouldResume(true, false, true, null));
    }

    // ── The ViewModel lifecycle: disconnect -> reconnect -> re-root ─────────

    private static LiveWalkerViewModel MakeVm(StubDumpService dump)
        => new LiveWalkerViewModel(dump, new MockLoggingService(),
                                   new MockPlatformService(Path.GetTempPath()));

    /// <summary>Register a walk result so a navigation drives the real UpdateDisplay path.</summary>
    private static StubDumpService DumpWith(string addr, string name)
    {
        var dump = new StubDumpService();
        dump.RegisterStruct(addr, new InstanceWalkResult
        {
            Name = name,
            Address = addr,
            ClassName = "Actor",
            Fields = new List<LiveFieldValue>
            {
                new() { Name = "Health", TypeName = "FloatProperty", Offset = 0x100, Size = 4 },
            },
        });
        return dump;
    }

    private static async Task<LiveWalkerViewModel> ConnectedVmOn(string addr)
    {
        var vm = MakeVm(DumpWith(addr, "Pawn_0"));
        await vm.NavigateToAddressCommand.ExecuteAsync(addr);
        Assert.True(vm.HasData);              // the walk landed, so Auto has something to do
        Assert.Equal(addr, vm.CurrentAddress);
        return vm;
    }

    [Fact]
    public async Task AutoRefreshComesBackAfterADisconnectAndAReRoot()
    {
        // The maintainer's actual path: one UI session, connected to one game, the pipe
        // drops, then it connects to a DIFFERENT game and the panel is re-rooted there.
        var vm = await ConnectedVmOn("0x1000");
        vm.IsAutoRefreshing = true;
        Assert.True(vm.IsAutoRefreshing);

        vm.ClearOnDisconnect();
        // X5's stop is correct and must stay: a live timer would re-walk a dead pipe, and
        // after reconnecting to another game it would walk the previous game's addresses.
        Assert.False(vm.IsAutoRefreshing);
        Assert.False(vm.HasData);
        Assert.Equal("", vm.CurrentAddress);

        // Reconnect + navigate. NavigateToAddressAsync calls the NON-resumable
        // StopAutoRefreshTimer on its way in — the resume must survive that.
        await vm.NavigateToAddressCommand.ExecuteAsync("0x1000");

        Assert.True(vm.HasData);
        Assert.True(vm.IsAutoRefreshing);     // fails before the fix: stayed off forever
    }

    [Fact]
    public async Task ATabSwitchAwayAndBackRestoresAutoRefresh()
    {
        var vm = await ConnectedVmOn("0x1000");
        vm.IsAutoRefreshing = true;

        vm.StopAutoRefreshTimer(resumable: true);   // what leaving the tab does
        Assert.False(vm.IsAutoRefreshing);

        vm.ResumeAutoRefreshIfPending();            // what returning to the tab does
        Assert.True(vm.IsAutoRefreshing);
    }

    [Fact]
    public async Task UntickingAutoIsRespectedAcrossADisconnect()
    {
        // The negative control for the two tests above: only something OUTSIDE the user's
        // control may re-arm. A user who turned Auto off must find it off.
        var vm = await ConnectedVmOn("0x1000");
        vm.IsAutoRefreshing = true;
        vm.IsAutoRefreshing = false;                // user unticks the toggle

        vm.ClearOnDisconnect();
        await vm.NavigateToAddressCommand.ExecuteAsync("0x1000");

        Assert.True(vm.HasData);
        Assert.False(vm.IsAutoRefreshing);
    }

    [Fact]
    public async Task ANavigationReRootStillTurnsAutoOffAndLeavesItOff()
    {
        // Pre-existing behaviour that must NOT change: drilling elsewhere stops Auto, and
        // the re-root's own UpdateDisplay must not sneak it back on.
        var vm = await ConnectedVmOn("0x1000");
        vm.IsAutoRefreshing = true;

        await vm.NavigateToAddressCommand.ExecuteAsync("0x1000");

        Assert.True(vm.HasData);
        Assert.False(vm.IsAutoRefreshing);
    }

    [Fact]
    public async Task RebuildingTheGridClearsAStrandedEditingLatch()
    {
        // IsEditing is set from DataGridBeginningEditEventArgs and cleared only from
        // CellEditEnded — which Avalonia does NOT raise when it tears an edit down because
        // the rows were replaced. A stranded `true` vetoed every auto-refresh tick from
        // then on, silently and for the rest of the session.
        var vm = await ConnectedVmOn("0x1000");
        vm.IsEditing = true;

        await vm.NavigateToAddressCommand.ExecuteAsync("0x1000");   // rebuilds the grid
        Assert.False(vm.IsEditing);

        vm.IsEditing = true;
        vm.ClearOnDisconnect();
        Assert.False(vm.IsEditing);
    }
}
