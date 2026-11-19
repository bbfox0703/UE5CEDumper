namespace UE5DumpUI.Services;

/// <summary>
/// Cross-session identity helpers for snapshots. UE spawns runtime objects with
/// an FName numeric suffix (<c>BP_Enemy_C_47</c>) whose number drifts between
/// game launches. Normalising the suffix (<c>BP_Enemy_C_47</c> →
/// <c>BP_Enemy_C</c>) lets the SPC / Pivot engines join the same logical object
/// across sessions. Pure / AOT-safe (no regex). See
/// docs/experimental-snapshot-spc-pivot.md §5.
/// </summary>
public static class SnapshotIdentity
{
    private static bool IsSep(char c) => c == '/' || c == '.' || c == ':';

    /// <summary>
    /// Strip the trailing <c>_&lt;digits&gt;</c> FName spawn counter from the
    /// object's own (leaf) name — the last component after the final
    /// <c>/</c> <c>.</c> <c>:</c> separator. Outer / package / asset names are
    /// left intact, so distinct assets (<c>Foo_3</c> vs <c>Foo_4</c>) are not
    /// merged. The class suffix <c>_C</c> is preserved (C is not a digit), and
    /// a bare <c>_5</c> (empty name) is left intact.
    ///
    /// Limitation: a spawn counter on an OUTER component (e.g. a component
    /// whose owning Pawn is <c>BP_Player_C_0</c>) is not normalised — the SPC /
    /// Pivot loose-join mode covers that case instead. See
    /// docs/experimental-snapshot-spc-pivot.md §5.
    /// </summary>
    public static string NormalizePath(string? path)
    {
        if (string.IsNullOrEmpty(path)) return "";

        // Start of the leaf component (after the last separator).
        int start = 0;
        for (int i = 0; i < path.Length; i++)
            if (IsSep(path[i])) start = i + 1;

        int j = path.Length - 1;
        if (j >= start && char.IsAsciiDigit(path[j]))
        {
            while (j >= start && char.IsAsciiDigit(path[j])) j--;
            // require a non-empty name before the underscore so "_5" stays "_5"
            if (j > start && path[j] == '_') return path.Substring(0, j);
        }
        return path;
    }
}
