using System.Collections.Generic;
using System.Linq;
using UE5DumpUI.Core;
using UE5DumpUI.Models;

namespace UE5DumpUI.Helpers;

/// <summary>
/// Formats batch-xref results into the compact inline-cell summary shared by
/// the Property Search, Interesting Properties and Instance Finder "Find Funcs"
/// columns: "N · func1, func2[, …]", or "0" when nothing references the field.
/// </summary>
public static class XrefFormat
{
    /// <summary>
    /// Cell text for one row's xref result.
    ///
    /// <para>
    /// <paramref name="deadlineHit"/> is not optional decoration. The DLL runs each
    /// sweep against a real 30 s budget and latches the flag when it runs out; the
    /// three batch loops used to consume <c>res.Xrefs</c> and throw the rest away, so a
    /// row whose sweep timed out was written as a bare <c>0</c> — which the user reads
    /// as "no Blueprint function touches this field, so freezing it is safe". The
    /// single-row dialog built on the SAME DLL call has always printed
    /// "[DEADLINE HIT — partial]", so two UI paths over one call disagreed. (audit #5 Z9)
    /// </para>
    /// </summary>
    public static string FunctionsSummary(IReadOnlyList<PropertyXrefMatch> xrefs,
                                          bool deadlineHit = false, bool capHit = false)
    {
        var marker = deadlineHit ? PartialResultNotice.CellMarker : "";
        if (xrefs.Count == 0) return "0" + marker;
        // [W3-XREF-CAP] A capped result is a LOWER BOUND: "200+". Deliberately not the partial marker -- a
        // re-run asks for the same cap and cannot find more, so IsPartialCell must not re-scan it forever.
        var count = capHit ? $"{xrefs.Count}+" : $"{xrefs.Count}";
        var preview = string.Join(", ", xrefs.Take(2).Select(x => x.FunctionName));
        var body = xrefs.Count > 2 ? $"{count} · {preview}, …" : $"{count} · {preview}";
        return body + marker;
    }

    /// <summary>
    /// True when a cell produced by <see cref="FunctionsSummary"/> should NOT be treated
    /// as a finished answer — i.e. re-running the row could still find more. The batch
    /// loops use it to decide whether a cached cell may be skipped on a re-run.
    /// </summary>
    public static bool IsPartialCell(string? cell)
        => !string.IsNullOrEmpty(cell) && cell.EndsWith(PartialResultNotice.CellMarker,
                                                        System.StringComparison.Ordinal);

    /// <summary>[W3-XREF-CAP] The Find Funcs dialog's status line. A capped scan says so as its own clause,
    /// beside (never inside) the deadline's, and its counts read as lower bounds: "200+ function(s)", and in
    /// class mode the "(N matched)" sum, which adds up workers that each stopped at the cap.</summary>
    public static string XrefDialogStatus(FindPropertyXrefsResult res, bool classMode)
    {
        var s = res.Scan;
        bool capped = s?.CapHit ?? false;
        var plus = capped ? "+" : "";
        var stats = s == null
            ? ""
            : (classMode
                ? $" — scanned {s.FunctionsScanned:N0} funcs ({s.FunctionsWithScript:N0}{plus} matched) "
                : $" — scanned {s.FunctionsScanned:N0} funcs ({s.FunctionsWithScript:N0} with bytecode) ")
            + $"over {s.ObjectsTotal:N0} objects in {s.DurationMs}ms"
            + (s.DeadlineHit ? " [DEADLINE HIT — partial]" : "")
            + (capped ? $" [CAP HIT — only the first {s.Cap:N0} are listed; more may exist]" : "");
        return classMode
            ? $"{res.Xrefs.Count}{plus} function(s) take this class{stats}"
            : $"{res.Xrefs.Count}{plus} function(s) reference this field{stats}";
    }
}
