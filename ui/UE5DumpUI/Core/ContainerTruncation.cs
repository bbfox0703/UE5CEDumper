namespace UE5DumpUI.Core;

/// <summary>
/// Pure formatting for the "container drill-down was capped at the array limit" disclosure
/// (finding <c>[CONTAINERCAP-2026-08-18]</c>).
/// <para>
/// A <c>TArray</c> / <c>TMap</c> / <c>TSet</c> is walked only up to the user's Array Limit
/// (<see cref="Constants.DefaultArrayLimit"/>, surfaced as the toolbar slider). The DLL still
/// reports the container's TRUE total (<c>arrayCount</c> / <c>mapCount</c> / <c>setCount</c>)
/// alongside the capped element list, so when fewer elements were received than the true total the
/// UI must say so — silence let a user conclude a 500-entry <c>TMap</c> did not contain an item they
/// simply could not see. Mirrors the existing GWorld actor-list "⚠ showing N of M actors" pattern.
/// </para>
/// </summary>
public static class ContainerTruncation
{
    /// <summary>
    /// True when the drill-down shows fewer elements than the container actually holds.
    /// <para>
    /// <paramref name="received"/> must be &gt; 0: a 0 means the elements could not be read at all
    /// (a different failure the caller surfaces separately), NOT that the cap hit — the cap is
    /// clamped to at least 1, so a readable non-empty container always yields &gt;= 1 element. The
    /// comparison is strict (<c>received &lt; total</c>), so a complete read (<c>received == total</c>)
    /// never badges.
    /// </para>
    /// </summary>
    public static bool IsTruncated(int received, int total) =>
        received > 0 && total > received;

    /// <summary>
    /// Suffix for a breadcrumb / header label, e.g. <c>"  ⚠ showing 128 of 199"</c>. Returns an
    /// empty string when not truncated, so a caller can unconditionally append it.
    /// </summary>
    public static string BadgeSuffix(int received, int total) =>
        IsTruncated(received, total)
            ? $"  ⚠ showing {received:N0} of {total:N0}"
            : "";

    /// <summary>
    /// Actionable panel status line pointing the user at the control that raises the cap. Empty
    /// string when not truncated.
    /// </summary>
    public static string StatusLine(int received, int total) =>
        IsTruncated(received, total)
            ? $"Showing the first {received:N0} of {total:N0} entries — raise the \"Array Limit\" slider in the toolbar and re-open this container to read more."
            : "";
}
