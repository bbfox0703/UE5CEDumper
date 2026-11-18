namespace UE5DumpUI.Services;

/// <summary>
/// Splits a CamelCase / snake_case identifier into its constituent words
/// so the <see cref="KeywordScoringTable"/> can match keywords as
/// whole tokens instead of substrings.
///
/// Whole tokens, because a substring match would let <c>Component</c>
/// (which contains "mp") false-match the Stats keyword "MP" for every
/// function on a class with "Component" in its name.
/// Tokenizing first means "MP" only matches when it appears as a
/// standalone token (e.g. <c>GetMP</c> or <c>PlayerMP</c>) and not as
/// a buried substring.
///
/// Split rules (applied in order):
/// 1. Underscores are token separators (and consumed) -- <c>BP_Player_C</c>
///    -> {"BP", "Player", "C"}
/// 2. Hyphens are token separators (and consumed) -- handles
///    rare kebab-case in path-like names.
/// 3. lower-to-upper transition splits -- <c>AddMoney</c> -> {"Add", "Money"}
/// 4. Run of uppers followed by a lower splits before the LAST upper --
///    <c>HUDWidget</c> -> {"HUD", "Widget"}, <c>BPCharacter</c> ->
///    {"BP", "Character"}.
/// 5. Digits stay attached to whichever side they came from -- we don't
///    split on digit boundaries because UE BP names like
///    <c>OnDestroyed_K2Node_42</c> already underscore-separate the
///    interesting tokens.
///
/// All tokens are returned in **lower-case** to make case-insensitive
/// keyword comparison a single dict lookup downstream.
/// </summary>
public static class KeywordTokenizer
{
    /// <summary>
    /// Tokenize <paramref name="identifier"/> and return the lowercased
    /// token list. Empty input returns an empty array (not null).
    /// </summary>
    public static string[] Tokenize(string? identifier)
    {
        if (string.IsNullOrEmpty(identifier))
            return Array.Empty<string>();

        var tokens = new List<string>(8);
        var current = new System.Text.StringBuilder(identifier.Length);

        for (int i = 0; i < identifier.Length; i++)
        {
            char c = identifier[i];

            // Rules 1+2: separators -> finalize current, drop the char.
            if (c == '_' || c == '-')
            {
                FlushIfNonEmpty(tokens, current);
                continue;
            }

            // Rule 3: lower-to-upper. Split BEFORE the upper.
            if (current.Length > 0
                && char.IsLower(current[^1])
                && char.IsUpper(c))
            {
                FlushIfNonEmpty(tokens, current);
                current.Append(c);
                continue;
            }

            // Rule 4: run-of-uppers followed by lower. The current builder
            // already holds e.g. "HUDW" and we're now seeing 'i' (lower).
            // We want to split BEFORE the W (the last upper of the run).
            // Detection: current.Length >= 2, last two chars are both
            // upper, AND we're now seeing a lower. Split position: keep
            // current up to-but-not-including last char, then start new
            // with that last upper + the current lower.
            if (current.Length >= 2
                && char.IsUpper(current[^1])
                && char.IsUpper(current[^2])
                && char.IsLower(c))
            {
                char lastUpper = current[^1];
                current.Length--;                  // drop the last upper from current
                FlushIfNonEmpty(tokens, current);  // emit "HUD"
                current.Append(lastUpper);         // start new with W
                current.Append(c);                 // ...append the lower
                continue;
            }

            current.Append(c);
        }

        FlushIfNonEmpty(tokens, current);
        return tokens.ToArray();
    }

    /// <summary>
    /// Build a tokenized HashSet for fast O(1) keyword lookup. Mirrors
    /// what <see cref="KeywordScoringTable"/> does internally per row,
    /// exposed here so unit tests can verify tokenization without
    /// going through the scorer.
    /// </summary>
    public static HashSet<string> TokenizeAsSet(string? identifier)
    {
        var arr = Tokenize(identifier);
        if (arr.Length == 0) return new HashSet<string>(StringComparer.Ordinal);
        return new HashSet<string>(arr, StringComparer.Ordinal);
    }

    private static void FlushIfNonEmpty(List<string> tokens, System.Text.StringBuilder buf)
    {
        if (buf.Length == 0) return;
        // Lowercase emit so downstream comparisons are case-blind by construction.
        // Cheaper than calling ToLowerInvariant on each comparison.
        for (int i = 0; i < buf.Length; i++)
            buf[i] = char.ToLowerInvariant(buf[i]);
        tokens.Add(buf.ToString());
        buf.Clear();
    }
}
