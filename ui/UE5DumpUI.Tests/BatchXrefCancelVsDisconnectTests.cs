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

    // ==================================================================
    // Game Class Filter — batch "Find Func" (find_functions_by_class).
    //
    // The FOURTH site of the same three defects, filed separately as
    // AE17 (the deadline flag discarded), AE18 (so a timed-out sweep is
    // written as a bare "0") and AE19 (a pipe death reported as "you
    // cancelled" — audit #3's L14 at a third unfixed site).
    // ==================================================================

    private sealed class NoopPlatform : IPlatformService
    {
        public bool TryAcquireSingleInstance() => true;
        public void ReleaseSingleInstance() { }
        public string GetAppDataPath() => System.IO.Path.GetTempPath();
        public string GetLogDirectoryPath() => System.IO.Path.GetTempPath();
        public Task<bool> CopyToClipboardAsync(string text) => Task.FromResult(true);
        public Task RevealInExplorerAsync(string path) => Task.CompletedTask;
        public string GetMachineName() => "TEST";
        public void CloseImeForWindow(IntPtr windowHandle) { }
        public Task<string?> ShowSaveFileDialogAsync(string a, string b, string c)
            => Task.FromResult<string?>(null);
    }

    private sealed class ClassXrefDump : StubDumpService
    {
        public Action? OnCall { get; set; }
        public bool Throw { get; set; }
        public bool Deadline { get; set; }
        public int Calls { get; private set; }

        public override Task<FindPropertyXrefsResult> FindFunctionsByClassAsync(
            string classAddr, bool gameOnly = true, int maxResults = 200,
            CancellationToken ct = default)
        {
            Calls++;
            OnCall?.Invoke();
            if (Throw) throw new OperationCanceledException("Pipe disconnected during send");
            return Task.FromResult(new FindPropertyXrefsResult
            {
                Scan = Deadline
                    ? new PropertyXrefScanStats { DeadlineHit = true, DurationMs = 30_000 }
                    : new PropertyXrefScanStats { DeadlineHit = false },
            });
        }
    }

    private static GameClassEntry ClassRow(string name) =>
        new() { ClassName = name, ClassAddr = "0xC1A550", SuperName = "Actor", ClassPath = "/Game/" + name };

    private static GameClassFilterViewModel ClassVm(ClassXrefDump dump, NoopLog log)
        => new(dump, log, new NoopPlatform());

    /// <summary>
    /// AE17/AE18: a sweep that ran out of the DLL's 30 s budget must not be written as a
    /// bare "0" — that reads as "no function takes this class", the opposite of what a
    /// timeout establishes.
    /// </summary>
    [Fact]
    public async Task ClassFilter_batch_marks_a_deadline_hit_cell_as_partial()
    {
        var rows = new List<GameClassEntry> { ClassRow("BP_Enemy_C") };
        var vm = ClassVm(new ClassXrefDump { Deadline = true }, new NoopLog());

        await vm.BatchFindFuncCommand.ExecuteAsync(rows);

        Assert.NotEqual("0", rows[0].XrefInfo);
        Assert.True(Helpers.XrefFormat.IsPartialCell(rows[0].XrefInfo));
        Assert.Contains("deadline", vm.StatusText);
        Assert.Contains("not found YET", vm.StatusText);
    }

    /// <summary>Negative control: a sweep that FINISHED and found nothing keeps its
    /// clean "0", or the marker would be noise on every row.</summary>
    [Fact]
    public async Task ClassFilter_batch_leaves_a_complete_empty_result_as_a_clean_zero()
    {
        var rows = new List<GameClassEntry> { ClassRow("BP_Enemy_C") };
        var vm = ClassVm(new ClassXrefDump { Deadline = false }, new NoopLog());

        await vm.BatchFindFuncCommand.ExecuteAsync(rows);

        Assert.Equal("0", rows[0].XrefInfo);
        Assert.DoesNotContain("deadline", vm.StatusText);
    }

    /// <summary>A partial cell must be re-scanned, not treated as cached — otherwise
    /// the partial answer is permanent for the session.</summary>
    [Fact]
    public async Task ClassFilter_batch_rescans_a_partial_row()
    {
        var rows = new List<GameClassEntry> { ClassRow("BP_Enemy_C") };
        rows[0].XrefInfo = "0" + PartialResultNotice.CellMarker;
        var dump = new ClassXrefDump();
        var vm = ClassVm(dump, new NoopLog());

        await vm.BatchFindFuncCommand.ExecuteAsync(rows);

        Assert.Equal(1, dump.Calls);
        Assert.Equal("0", rows[0].XrefInfo);
    }

    [Fact]
    public async Task ClassFilter_batch_still_skips_a_complete_cached_row()
    {
        var rows = new List<GameClassEntry> { ClassRow("BP_Enemy_C") };
        rows[0].XrefInfo = "2 · A, B";
        var dump = new ClassXrefDump();
        var vm = ClassVm(dump, new NoopLog());

        await vm.BatchFindFuncCommand.ExecuteAsync(rows);

        Assert.Equal(0, dump.Calls);
        Assert.Contains("1 cached", vm.StatusText);
    }

    /// <summary>AE19: a token-less OCE is a pipe death, not the user's Cancel.</summary>
    [Fact]
    public async Task ClassFilter_batch_reports_a_disconnect_as_failed_not_cancelled()
    {
        var log = new NoopLog();
        var vm = ClassVm(new ClassXrefDump { Throw = true }, log);

        await vm.BatchFindFuncCommand.ExecuteAsync(
            new List<GameClassEntry> { ClassRow("A_C"), ClassRow("B_C") });

        Assert.Contains("failed", vm.StatusText);
        Assert.DoesNotContain("cancelled", vm.StatusText);
        Assert.NotEmpty(log.Errors);   // used to leave no trace at all
    }

    /// <summary>Negative control: a REAL cancel must still read as a cancel.</summary>
    [Fact]
    public async Task ClassFilter_batch_still_reports_a_real_cancel_as_cancelled()
    {
        var dump = new ClassXrefDump();
        var vm = ClassVm(dump, new NoopLog());
        dump.OnCall = () => vm.CancelXrefBatchCommand.Execute(null);

        await vm.BatchFindFuncCommand.ExecuteAsync(
            new List<GameClassEntry> { ClassRow("A_C"), ClassRow("B_C") });

        Assert.Contains("cancelled", vm.StatusText);
        Assert.DoesNotContain("failed", vm.StatusText);
    }
}
