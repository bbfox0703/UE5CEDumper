namespace UE5DumpUI.Core;

/// <summary>
/// The single vocabulary for telling the user "what you are looking at is not the whole
/// answer".
///
/// <para>
/// Audit #5's L8 batch found the same defect in four unrelated places at once — Z4 (the
/// class-noise picker presenting a capped page as a complete class census), Z8
/// (<c>list_all_functions</c> capping silently, so the Console panel turned a page into
/// a positive claim about the GAME), Z9 (a batch xref cell writing <c>0</c> for a sweep
/// that timed out, which reads as "nothing references this field, so freezing it is
/// safe") and Z12 (a scan suffix whose stated purpose was to separate a clean miss from
/// a truncated scan, built from the wrong pass's counters). In every one of them a
/// truncation signal existed and was discarded before it reached the user.
/// </para>
/// <para>
/// The wording therefore lives HERE rather than being re-invented per panel, and it
/// continues the phrasing the two earlier fixes already shipped:
/// <c>[CLASSTOTAL-2026-08-18]</c>'s <c>"⚠ STOPPED at the N-row cap — …"</c> and
/// <c>[CONTAINERCAP-2026-08-18]</c>'s <c>"⚠ showing N of M"</c>
/// (<see cref="ContainerTruncation"/>). Three rules hold across all of them: the marker
/// is <c>⚠</c>, the separator is an em dash, and the sentence always names the CAUSE
/// before the consequence — a bare "partial" tells the reader nothing they can act on.
/// </para>
/// <para>Pure string building, no Avalonia, no I/O — unit-testable without a VM.</para>
/// </summary>
public static class PartialResultNotice
{
    /// <summary>
    /// The scan was stopped before it finished (user cancel, client gone, shutdown).
    /// Leading double space so a caller can append it straight onto a status line.
    /// </summary>
    /// <param name="noun">What is partial, as it reads in "this {noun} is partial".</param>
    public static string Cancelled(string noun = "list")
        => $"  ⚠ SCAN CANCELLED — this {noun} is partial";

    /// <summary>
    /// The collector stopped at a row cap, so the result is a page of a larger pool.
    /// </summary>
    /// <param name="cap">The cap that was actually applied (not the caller's request —
    /// the DLL may clamp).</param>
    /// <param name="thing">Plural noun for what exists beyond the cap ("matches",
    /// "functions", "classes").</param>
    /// <param name="advice">What the user can do about it. MUST be reachable from the
    /// panel showing the message: Z10 was this exact string advising "raise Max" on a
    /// panel that has no Max control, because it had been copied from Instance Finder,
    /// which does.</param>
    public static string RowCap(int cap, string thing, string advice)
        => $"  ⚠ STOPPED at the {cap:N0}-row cap — more {thing} exist; {advice}";

    /// <summary>
    /// Clause for a time-budgeted scan that ran out of budget. Not prefixed with spaces:
    /// it is embedded inside a bracketed suffix by <see cref="ScanSuffix"/>.
    /// </summary>
    public const string DeadlineClause = "⚠ DEADLINE HIT, this scan is partial; retry to continue";

    /// <summary>
    /// Marker for a grid CELL, which has no room for a sentence. Appended to the cell's
    /// own value so <c>0</c> (a real, complete "nothing references this") and
    /// <c>0 ⚠ partial</c> (the sweep timed out and found nothing YET) stop looking
    /// identical. The full explanation goes in the panel's status line.
    /// </summary>
    public const string CellMarker = " ⚠ partial";

    /// <summary>
    /// Roll-up clause for a batch that produced <paramref name="partialUnits"/> partial
    /// results out of <paramref name="totalUnits"/>. Empty when nothing was partial, so a
    /// caller can append it unconditionally.
    /// </summary>
    /// <param name="unit">What is being counted, singular. Two of the three Z9 loops scan
    /// once per ROW; Instance Finder dedups by class and scans once per CLASS (many rows
    /// share one result), so counting its classes and calling them rows would be a second
    /// small lie inside the honesty fix.</param>
    public static string BatchPartialClause(int partialUnits, int totalUnits, string unit = "row")
        => partialUnits <= 0
            ? ""
            : $" ⚠ {partialUnits:N0} of {totalUnits:N0} {unit}(s) hit the scan deadline and are marked partial — "
              + $"a 0 on those {unit}s means \"not found YET\", not \"none\".";

    /// <summary>
    /// A group scan kept fewer WITNESSES per slot than the slot actually matched.
    ///
    /// <para>
    /// The odd one out among these notices: nothing about the result set is missing —
    /// every matching object is present and every row is correct. What is capped is the
    /// per-slot list of FIELDS that satisfied the slot, i.e. the "All fields" popup and
    /// the row's <c>(+N)</c> annotation. Saying "results are partial" here would be
    /// false; the sentence has to name the witness list specifically, which is why it
    /// is not <see cref="RowCap"/>.
    /// </para>
    /// <para>
    /// The DLL computed this and only wrote it to its own log, so the panel that had
    /// already been fixed twice for "a matched field reads as a missed one" still could
    /// not say it. (audit #5 AE13)
    /// </para>
    /// </summary>
    /// <param name="cap">The cap the DLL actually applied (after its own clamp), or 0
    /// when an older DLL sent the flag without the number.</param>
    public static string PerSlotWitnessCap(int cap)
        => cap > 0
            ? $"  ⚠ a slot matched more than {cap:N0} fields — only that many were kept, so "
              + "\"All fields\" is a page and a later Changed/Decreased refine can re-read "
              + "only what was kept; use more distinctive values."
            : "  ⚠ a slot matched more fields than the per-slot cap kept — so \"All fields\" "
              + "is a page and a later Changed/Decreased refine can re-read only what was "
              + "kept; use more distinctive values.";

    /// <summary>
    /// A refine pass whose INPUT was already truncated. The refine itself completed;
    /// what is partial is the set it was allowed to see.
    ///
    /// <para>
    /// Worth its own sentence because the consequence differs from a plain cap: pruning
    /// a truncated set yields survivors that are correct individually and incomplete
    /// collectively, so the usual "narrow it further" advice makes things worse — the
    /// only repair is to redo the pass that truncated. Value Search announced the
    /// truncation on First Scan and then a Next Scan silently dropped both that note and
    /// the picker's "Counts are partial" badge, which reads as the problem having gone
    /// away. (audit #5 AE24)
    /// </para>
    /// </summary>
    /// <param name="priorPass">The pass that truncated, named as the user's button.</param>
    public static string InheritedTruncation(string priorPass = "First Scan")
        => $"  ⚠ the {priorPass} it refined was TRUNCATED — these survivors are a subset of a "
         + $"partial set, so a match that never entered it cannot appear here; re-run {priorPass} "
         + $"with a longer timeout or a narrower predicate.";

    /// <summary>
    /// A DERIVED list — autocomplete suggestions, facets, a distinct-value dropdown —
    /// computed from a page that stopped at a cap, and therefore not the set of values
    /// that exist.
    ///
    /// <para>
    /// Distinct from <see cref="RowCap"/>, which describes the capped list itself. This
    /// describes something built OUT of it, where the consequence is different and worse:
    /// the panel's own status line may already admit the cap, while a dropdown beside it
    /// silently presents its contents as the complete set of choices, and a value that
    /// exists only past the cap looks like a value that does not exist. (audit #5 AE15)
    /// </para>
    /// </summary>
    /// <param name="listName">Plural, as it reads in "{listName} are derived from" —
    /// e.g. "Super / Package suggestions".</param>
    /// <param name="shown">Rows the page actually contained.</param>
    /// <param name="total">Rows in the pool.</param>
    /// <param name="unit">Plural noun for a row ("classes", "functions").</param>
    public static string DerivedListFromCappedPage(string listName, int shown, int total, string unit)
        => $"⚠ {listName} are derived from the {shown:N0} {unit} loaded, not all {total:N0} — "
         + $"a value that only occurs past the cap is not offered here; type it in.";

    /// <summary>
    /// The <c>[scanned X/Y in Zms]</c> suffix for a reverse-address container scan.
    ///
    /// <para>
    /// Its stated purpose has always been "so the user can tell a clean miss from a
    /// deadline-truncated scan", and it could not do that (Z12): the numbers came from
    /// the SHALLOW pass even when the recursive DEEP pass was the one that ran, and the
    /// deep pass's second bound — a per-container element probe cap — was never
    /// mentioned at all, so a deep miss caused by the cap looked exactly like proof of
    /// absence. Both bounds are named here.
    /// </para>
    /// </summary>
    /// <param name="scanned">Objects the reported pass walked.</param>
    /// <param name="total">Objects in GObjects.</param>
    /// <param name="durationMs">Wall time of every pass that ran, summed.</param>
    /// <param name="deadlineHit">Either pass ran out of its time budget.</param>
    /// <param name="deepScan">The recursive deep descent ran (shallow found nothing).</param>
    /// <param name="deepElemCap">Per-container element probe cap used by the deep pass.</param>
    /// <param name="anyContainerMatch">Whether any container match came back. The
    /// element-cap caveat is only worth saying on a MISS — on a hit the cap did not
    /// stand between the user and their answer.</param>
    public static string ScanSuffix(int scanned, int total, long durationMs,
                                    bool deadlineHit, bool deepScan, int deepElemCap,
                                    bool anyContainerMatch)
    {
        // total == 0 means the scan never ran (or the DLL predates the stats block);
        // inventing a "0/0" suffix would be a claim we cannot back.
        if (total <= 0) return "";

        var pass = deepScan ? "scanned (incl. deep descent) " : "scanned ";
        var body = $"{pass}{scanned:N0}/{total:N0} in {durationMs:N0}ms";

        var caveats = new List<string>(2);
        if (deadlineHit) caveats.Add(DeadlineClause);
        // The deep descent probes at most N elements per container, so a deep miss is
        // bounded by that cap as well as by the clock. Say so, or the user reads the
        // miss as proof the value is in no container.
        if (deepScan && !anyContainerMatch)
            caveats.Add($"⚠ the deep descent probes at most {deepElemCap:N0} element(s) per container, "
                      + "so this miss is not proof of absence");

        return caveats.Count == 0
            ? $"  [{body}]"
            : $"  [{body} — {string.Join(" — ", caveats)}]";
    }
}
