using System;
using System.Collections.Generic;
using UE5DumpUI.Core;
using UE5DumpUI.Helpers;
using UE5DumpUI.Models;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// Audit #5 L8, findings Z4 / Z8 / Z9 / Z10 / Z12 — one theme: a truncation or deadline
/// signal existed and was discarded before the user saw it.
///
/// <para>
/// These cover the pure half: the shared disclosure vocabulary
/// (<see cref="PartialResultNotice"/>) and the xref cell formatter
/// (<see cref="XrefFormat"/>). The VM wiring that feeds them is covered in the
/// per-panel test files.
/// </para>
/// </summary>
public class PartialResultNoticeTests
{
    /// <summary>
    /// AF7's row names this sentence: when the native disassembler stops early the Props
    /// dialog must say it "hit its instruction budget". Pinned because a truncated list is
    /// indistinguishable from a complete one — the whole point is that a field missing from
    /// it is "not seen yet", not "not used", and the sentence has to carry that.
    /// </summary>
    [Fact]
    public void DisassemblyBudget_SaysWhatWasTruncatedAndWhatTheAbsenceMeans()
    {
        var s = PartialResultNotice.DisassemblyBudget();
        Assert.Contains("hit its instruction budget", s, StringComparison.Ordinal);
        Assert.Contains("PREFIX", s, StringComparison.Ordinal);
        Assert.Contains("not seen yet", s, StringComparison.Ordinal);
        // and it must not be silently empty — the failure mode of every optional note
        Assert.True(s.Trim().Length > 40, "the note collapsed to nothing: " + s);
    }

    // ==================================================================
    // Z10 — the advice must name a lever the panel actually has.
    // ==================================================================

    /// <summary>
    /// The Property Search cap suffix used to end "…narrow the query or raise Max". That
    /// panel has no Max control — the string had been lifted from Instance Finder, which
    /// really does own an InstanceSearchCap NumericUpDown — so half the advice pointed at
    /// a control the user could hunt for and never find. RowCap now takes the advice as a
    /// parameter precisely so each caller supplies its OWN levers.
    /// </summary>
    [Fact]
    public void RowCap_puts_the_callers_advice_in_the_sentence()
    {
        var s = PartialResultNotice.RowCap(200, "matches", "narrow it with a Type filter");

        Assert.Contains("STOPPED at the 200-row cap", s);
        Assert.Contains("more matches exist", s);
        Assert.Contains("narrow it with a Type filter", s);
    }

    [Fact]
    public void RowCap_thousands_separates_the_cap_so_100000_is_readable()
        => Assert.Contains("100,000-row cap",
                           PartialResultNotice.RowCap(100000, "functions", "x"));

    /// <summary>
    /// One vocabulary, not four. Every disclosure this batch touches carries the ⚠
    /// marker and an em dash before the consequence — the phrasing the two fixes that
    /// landed just before it already shipped ([CLASSTOTAL] / [CONTAINERCAP]).
    /// </summary>
    [Theory]
    [InlineData("cancel")]
    [InlineData("rowcap")]
    [InlineData("scan-deadline")]
    [InlineData("batch")]
    public void Every_notice_uses_the_shared_marker_and_em_dash(string which)
    {
        var s = which switch
        {
            "cancel"        => PartialResultNotice.Cancelled(),
            "rowcap"        => PartialResultNotice.RowCap(10, "matches", "narrow it"),
            "scan-deadline" => PartialResultNotice.ScanSuffix(5, 10, 1, deadlineHit: true,
                                   deepScan: false, deepElemCap: 256, anyContainerMatch: false),
            _               => PartialResultNotice.BatchPartialClause(2, 9),
        };

        Assert.Contains("⚠", s);
        Assert.Contains("—", s);
    }

    // ==================================================================
    // Z12 — the [scanned X/Y] suffix must discriminate.
    // ==================================================================

    [Fact]
    public void ScanSuffix_is_empty_when_no_scan_ran()
        => Assert.Equal("", PartialResultNotice.ScanSuffix(0, 0, 0, false, false, 256, false));

    [Fact]
    public void ScanSuffix_complete_shallow_scan_carries_no_warning()
    {
        var s = PartialResultNotice.ScanSuffix(430112, 430112, 812,
                    deadlineHit: false, deepScan: false, deepElemCap: 256,
                    anyContainerMatch: true);

        Assert.Equal("  [scanned 430,112/430,112 in 812ms]", s);
        Assert.DoesNotContain("⚠", s);
    }

    [Fact]
    public void ScanSuffix_deadline_says_the_scan_is_partial_and_retryable()
    {
        var s = PartialResultNotice.ScanSuffix(120000, 430112, 15004,
                    deadlineHit: true, deepScan: false, deepElemCap: 256,
                    anyContainerMatch: false);

        Assert.Contains("DEADLINE HIT", s);
        Assert.Contains("partial", s);
        Assert.Contains("retry", s);
    }

    /// <summary>
    /// The heart of Z12. A deep MISS has a SECOND bound the suffix never mentioned: the
    /// deep descent probes at most N elements per container, so "scanned 430,112/430,112"
    /// with no warning read as an exhaustive negative when the cap could have ended it.
    /// </summary>
    [Fact]
    public void ScanSuffix_deep_miss_discloses_the_element_cap_so_a_miss_is_not_read_as_absence()
    {
        var s = PartialResultNotice.ScanSuffix(430112, 430112, 4210,
                    deadlineHit: false, deepScan: true, deepElemCap: 256,
                    anyContainerMatch: false);

        Assert.Contains("deep descent", s);
        Assert.Contains("256 element(s) per container", s);
        Assert.Contains("not proof of absence", s);
    }

    /// <summary>
    /// On a deep HIT the cap did not stand between the user and the answer, so the
    /// caveat would be noise — but the suffix must still say the deep pass ran, because
    /// the numbers it quotes are now the DEEP pass's (the DLL used to report the shallow
    /// pass's counters here, describing a pass that had nothing to do with the answer).
    /// </summary>
    [Fact]
    public void ScanSuffix_deep_hit_names_the_pass_but_drops_the_cap_caveat()
    {
        var s = PartialResultNotice.ScanSuffix(430112, 430112, 4210,
                    deadlineHit: false, deepScan: true, deepElemCap: 256,
                    anyContainerMatch: true);

        Assert.Contains("deep descent", s);
        Assert.DoesNotContain("not proof of absence", s);
        Assert.DoesNotContain("⚠", s);
    }

    /// <summary>Both bounds can bite at once; neither may swallow the other.</summary>
    [Fact]
    public void ScanSuffix_deep_miss_after_a_deadline_reports_BOTH_bounds()
    {
        var s = PartialResultNotice.ScanSuffix(9000, 430112, 15004,
                    deadlineHit: true, deepScan: true, deepElemCap: 64,
                    anyContainerMatch: false);

        Assert.Contains("DEADLINE HIT", s);
        Assert.Contains("64 element(s) per container", s);
    }

    /// <summary>The cap the user actually set is the one named, not a constant.</summary>
    [Fact]
    public void ScanSuffix_names_the_users_own_element_cap()
        => Assert.Contains("1,024 element(s)",
               PartialResultNotice.ScanSuffix(1, 2, 3, false, true, 1024, false));

    // ==================================================================
    // Z9 — a timed-out xref sweep must not be written as a bare 0.
    // ==================================================================

    /// <summary>
    /// The exact failure Z9 describes: a row whose game-wide bytecode sweep hit the
    /// DLL's 30 s budget was written as "0", which the user reads as "no Blueprint
    /// function touches this field, so freezing it is safe".
    /// </summary>
    [Fact]
    public void FunctionsSummary_zero_after_a_deadline_is_not_the_same_cell_as_a_clean_zero()
    {
        var clean   = XrefFormat.FunctionsSummary(new List<PropertyXrefMatch>(), deadlineHit: false);
        var partial = XrefFormat.FunctionsSummary(new List<PropertyXrefMatch>(), deadlineHit: true);

        Assert.Equal("0", clean);
        Assert.NotEqual(clean, partial);
        Assert.Contains("partial", partial);
    }

    [Fact]
    public void FunctionsSummary_marks_a_partial_hit_list_too()
    {
        var xrefs = new List<PropertyXrefMatch>
        {
            new() { FunctionName = "ReceiveTick" },
            new() { FunctionName = "OnDamaged" },
            new() { FunctionName = "OnHealed" },
        };

        Assert.Equal("3 · ReceiveTick, OnDamaged, …", XrefFormat.FunctionsSummary(xrefs));
        Assert.Contains("partial", XrefFormat.FunctionsSummary(xrefs, deadlineHit: true));
    }

    /// <summary>
    /// The batch loops skip rows that already carry a cell. A PARTIAL cell must not be
    /// treated as done — re-running it can still find something, and treating it as
    /// cached would make the partial answer permanent for the session.
    /// </summary>
    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("0", false)]
    [InlineData("2 · A, B", false)]
    [InlineData("—", false)]
    [InlineData("0 ⚠ partial", true)]
    [InlineData("2 · A, B ⚠ partial", true)]
    public void IsPartialCell_only_flags_cells_the_deadline_truncated(string? cell, bool expected)
        => Assert.Equal(expected, XrefFormat.IsPartialCell(cell));

    [Fact]
    public void BatchPartialClause_is_silent_when_nothing_was_partial()
        => Assert.Equal("", PartialResultNotice.BatchPartialClause(0, 40));

    /// <summary>The roll-up must spell out what a 0 on those rows now means, because the
    /// cell marker alone is easy to read as a rendering artefact.</summary>
    [Fact]
    public void BatchPartialClause_explains_what_a_zero_on_a_partial_row_means()
    {
        var s = PartialResultNotice.BatchPartialClause(3, 40);

        Assert.Contains("3 of 40", s);
        Assert.Contains("deadline", s);
        Assert.Contains("not found YET", s);
    }
}
