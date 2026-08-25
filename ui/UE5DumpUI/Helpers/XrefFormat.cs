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
                                          bool deadlineHit = false)
    {
        var marker = deadlineHit ? PartialResultNotice.CellMarker : "";
        if (xrefs.Count == 0) return "0" + marker;
        var preview = string.Join(", ", xrefs.Take(2).Select(x => x.FunctionName));
        var body = xrefs.Count > 2 ? $"{xrefs.Count} · {preview}, …" : $"{xrefs.Count} · {preview}";
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
}
