using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UE5DumpUI.Core;
using UE5DumpUI.Models;
using UE5DumpUI.Services;
using UE5DumpUI.ViewModels;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// Audit #5 Z5 — "Find Funcs cancelled at N/M" for something that was not a cancel.
///
/// <para>
/// <see cref="PipeClient"/> distinguishes three causes as of audit #5 AC10: a CALLER
/// cancel throws an <see cref="OperationCanceledException"/> carrying the caller's own
/// token, a deliberate teardown throws one carrying the client's token, and an
/// unexpected pipe death — game crash, DLL unload — arrives as an
/// <see cref="System.IO.IOException"/>. Two batch loops still caught
/// <c>OperationCanceledException</c> bare, so every one of those read as "you pressed
/// Cancel", with nothing written to the log. This is audit #3's L14 applied at the two
/// sites it missed; the two siblings that already carried the fix (Property Search,
/// Instance Finder) are the shape copied here.
/// </para>
/// <para>
/// Both tests below drive the SAME loop with the SAME exception TYPE and differ only in
/// which token it carries — which is precisely the distinction the old code could not
/// make.
/// </para>
/// </summary>
public class BatchXrefCancelVsDisconnectTests
{
    private sealed class NoopLog : ILoggingService
    {
        public List<string> Errors { get; } = new();
        public void Info(string message) { }
        public void Warn(string message) { }
        public void Error(string message) => Errors.Add(message);
        public void Error(string message, Exception ex) => Errors.Add(message);
        public void Debug(string message) { }
        public void Info(string category, string message) { }
        public void Warn(string category, string message) { }
        public void Error(string category, string message) => Errors.Add(message);
        public void Error(string category, string message, Exception ex) => Errors.Add(message);
        public void Debug(string category, string message) { }
        public void StartProcessMirror(string processName) { }
        public void StopProcessMirror() { }
    }

    // ==================================================================
    // Interesting PROPERTIES — batch "Find Funcs" (find_property_xrefs)
    // ==================================================================

    private sealed class XrefDump : StubDumpService
    {
        /// <summary>Runs before each call; lets a test cancel the batch mid-flight.</summary>
        public Action? OnCall { get; set; }
        public Func<FindPropertyXrefsResult>? Throwing { get; set; }
        public int Calls { get; private set; }

        public override Task<FindPropertyXrefsResult> FindPropertyXrefsAsync(
            string propAddr, bool gameOnly = true, int maxResults = 200,
            CancellationToken ct = default)
        {
            Calls++;
            OnCall?.Invoke();
            if (Throwing != null) return Task.FromResult(Throwing());
            return Task.FromResult(new FindPropertyXrefsResult());
        }
    }

    private static ScoredPropertyRow PropRow(string name) => new()
    {
        Match = new PropertySearchMatch
        {
            ClassName = "BP_Enemy_C", PropName = name, FieldAddr = "0xDEAD0000",
        },
        FinalScore = 10, Category = PropertyCategory.Stats,
        KeywordHits = 1, ClassBonus = 0, IsUnusualLocation = false,
    };

    /// <summary>
    /// A pipe-side OCE that is NOT the user's cancel (token-less / another token) must
    /// read as a FAILURE and be logged. Before the fix: "Find Funcs cancelled at 0/2",
    /// silently.
    /// </summary>
    [Fact]
    public async Task Properties_batch_reports_a_disconnect_as_failed_not_cancelled()
    {
        var log = new NoopLog();
        var dump = new XrefDump
        {
            // Token-less: exactly what a connection-loss OCE looks like to the caller.
            Throwing = () => throw new OperationCanceledException("Pipe disconnected during send"),
        };
        var vm = new InterestingPropertiesViewModel(dump, log);

        await vm.BatchFindFuncsCommand.ExecuteAsync(
            new List<ScoredPropertyRow> { PropRow("Health"), PropRow("Mana") });

        Assert.Contains("failed", vm.StatusText);
        Assert.DoesNotContain("cancelled", vm.StatusText);
        Assert.NotEmpty(log.Errors);   // a disconnect used to leave no trace at all
    }

    /// <summary>Negative control: a REAL cancel must still read as a cancel.</summary>
    [Fact]
    public async Task Properties_batch_still_reports_a_real_cancel_as_cancelled()
    {
        var log = new NoopLog();
        var dump = new XrefDump();
        var vm = new InterestingPropertiesViewModel(dump, log);
        dump.OnCall = () => vm.CancelXrefBatchCommand.Execute(null);

        await vm.BatchFindFuncsCommand.ExecuteAsync(
            new List<ScoredPropertyRow> { PropRow("Health"), PropRow("Mana") });

        Assert.Contains("cancelled", vm.StatusText);
        Assert.DoesNotContain("failed", vm.StatusText);
    }

    // ==================================================================
    // Interesting FUNCTIONS — batch "Props" (walk_function_props)
    // ==================================================================

    private sealed class PropsDump : StubDumpService
    {
        public Action? OnCall { get; set; }
        public bool Throw { get; set; }

        public override Task<FunctionPropRefsResult> WalkFunctionPropsAsync(
            string funcAddr, CancellationToken ct = default)
        {
            OnCall?.Invoke();
            if (Throw) throw new OperationCanceledException("Pipe disconnected during send");
            return Task.FromResult(new FunctionPropRefsResult());
        }
    }

    private static ScoredFunctionRow FuncRow(string name) => new()
    {
        Entry = new AllFunctionEntry
        {
            ClassName = "BP_Enemy_C", FuncName = name, FuncAddr = "0xBEEF0000",
        },
        FinalScore = 10, Category = FunctionCategory.Stats,
        KeywordHits = 1, ClassBonus = 0, FlagBonus = 0,
    };

    [Fact]
    public async Task Functions_props_batch_reports_a_disconnect_as_failed_not_cancelled()
    {
        var log = new NoopLog();
        var vm = new InterestingFunctionsViewModel(new PropsDump { Throw = true }, log);

        await vm.BatchFindFuncPropsCommand.ExecuteAsync(
            new List<ScoredFunctionRow> { FuncRow("GetHealth"), FuncRow("SetHealth") });

        Assert.Contains("failed", vm.StatusText);
        Assert.DoesNotContain("cancelled", vm.StatusText);
        Assert.NotEmpty(log.Errors);
    }

    [Fact]
    public async Task Functions_props_batch_still_reports_a_real_cancel_as_cancelled()
    {
        var log = new NoopLog();
        var dump = new PropsDump();
        var vm = new InterestingFunctionsViewModel(dump, log);
        dump.OnCall = () => vm.CancelXrefBatchCommand.Execute(null);

        await vm.BatchFindFuncPropsCommand.ExecuteAsync(
            new List<ScoredFunctionRow> { FuncRow("GetHealth"), FuncRow("SetHealth") });

        Assert.Contains("cancelled", vm.StatusText);
        Assert.DoesNotContain("failed", vm.StatusText);
    }

    // ==================================================================
    // Z9 — the deadline flag must reach the cell and the roll-up.
    // ==================================================================

    private sealed class DeadlineXrefDump : StubDumpService
    {
        public override Task<FindPropertyXrefsResult> FindPropertyXrefsAsync(
            string propAddr, bool gameOnly = true, int maxResults = 200,
            CancellationToken ct = default)
            => Task.FromResult(new FindPropertyXrefsResult
            {
                Scan = new PropertyXrefScanStats { DeadlineHit = true, DurationMs = 30_000 },
            });
    }

    /// <summary>
    /// A sweep that ran out of the DLL's 30 s budget wrote a bare "0" into the cell —
    /// the signal the user reads as "no Blueprint function touches this field, so
    /// freezing it is safe". The single-row dialog on the same DLL call has always
    /// printed "[DEADLINE HIT — partial]", so two UI paths over one call disagreed.
    /// </summary>
    [Fact]
    public async Task Properties_batch_marks_a_timed_out_row_partial_in_the_cell_and_the_summary()
    {
        var rows = new List<ScoredPropertyRow> { PropRow("Health") };
        var vm = new InterestingPropertiesViewModel(new DeadlineXrefDump(), new NoopLog());

        await vm.BatchFindFuncsCommand.ExecuteAsync(rows);

        Assert.NotEqual("0", rows[0].XrefInfo);
        Assert.True(Helpers.XrefFormat.IsPartialCell(rows[0].XrefInfo));
        Assert.Contains("deadline", vm.StatusText);
        Assert.Contains("not found YET", vm.StatusText);
    }

    /// <summary>
    /// A partial cell must not be treated as cached on a re-run: re-running it can still
    /// find something, and skipping it would make the partial answer permanent.
    /// </summary>
    [Fact]
    public async Task Properties_batch_rescans_a_partial_row_instead_of_treating_it_as_cached()
    {
        var rows = new List<ScoredPropertyRow> { PropRow("Health") };
        rows[0].XrefInfo = "0" + PartialResultNotice.CellMarker;   // left over from a timed-out run
        var dump = new XrefDump();
        var vm = new InterestingPropertiesViewModel(dump, new NoopLog());

        await vm.BatchFindFuncsCommand.ExecuteAsync(rows);

        Assert.Equal(1, dump.Calls);          // re-scanned, not skipped
        Assert.Equal("0", rows[0].XrefInfo);  // ...and the clean result replaced the marker
    }

    /// <summary>Negative control: a COMPLETE cell is still cached, so the change above
    /// cannot be "the cache stopped working".</summary>
    [Fact]
    public async Task Properties_batch_still_skips_a_complete_cached_row()
    {
        var rows = new List<ScoredPropertyRow> { PropRow("Health") };
        rows[0].XrefInfo = "2 · A, B";
        var dump = new XrefDump();
        var vm = new InterestingPropertiesViewModel(dump, new NoopLog());

        await vm.BatchFindFuncsCommand.ExecuteAsync(rows);

        Assert.Equal(0, dump.Calls);
        Assert.Contains("1 cached", vm.StatusText);
    }
}
