using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UE5DumpUI.Models;

namespace UE5DumpUI.Services;

/// <summary>
/// Reads a coordinate block back out of a previously generated AA script (R7 — the
/// user pastes the WHOLE script and we parse only our own fenced region).
///
/// Mechanism copied from AOBMaker's <c>TryParseConfig</c> — brace-balanced scan plus
/// per-key anchored regex — because that is what makes the format tolerant of the
/// edits a human actually makes: reordered fields, a missing or extra comma, an
/// entry split over several lines, an inserted comment, and unknown keys from a
/// newer build. A line-oriented regex would break on every one of those.
///
/// Four defects of that parser are deliberately NOT inherited (each confirmed by
/// running AOBMaker's own shipped assembly):
///  1. Version baked into the marker literal, so a v2 block is indistinguishable
///     from "no block at all" and the UI reports "no markers found". Here the
///     version is CAPTURED and an unsupported one gets its own message.
///  2. Markers matched anywhere via IndexOf, including inside a string literal.
///     Here they must start a line.
///  3. 100% silent failure — no throw, no diagnostic, ever. Here every rejection is
///     reported with a line number.
///  4. Only double-quoted strings recognised, single-quoted ones silently ignored so
///     the field falls back to a default. Here both quote styles are accepted, and an
///     unparseable value is a reported error rather than a silent default.
/// </summary>
public static class CoordLuaParser
{
    /// <summary>Newest format version this build understands.</summary>
    public const int SupportedVersion = CoordLibraryScriptGenerator.FormatVersion;

    // Line-anchored so a marker inside a string literal or a nested comment cannot
    // be mistaken for the real fence. Version captured, not baked into the literal.
    private static readonly Regex s_startFence = new(
        @"^[ \t]*--[ \t]*@UE5CD:COORDS[ \t]+v(\d+)[ \t]*$",
        RegexOptions.Multiline | RegexOptions.CultureInvariant);

    private static readonly Regex s_endFence = new(
        @"^[ \t]*--[ \t]*@UE5CD:END[ \t]*$",
        RegexOptions.Multiline | RegexOptions.CultureInvariant);

    private static readonly Regex s_coordsTable = new(
        @"local[ \t]+COORDS[ \t]*=[ \t]*\{",
        RegexOptions.CultureInvariant);

    /// <summary>
    /// Parse a pasted script. Returns the same shape the CSV importer produces, so
    /// both feed one preview/commit path.
    /// </summary>
    public static CoordCsvParseResult Parse(string? script)
    {
        var result = new CoordCsvParseResult();
        if (string.IsNullOrWhiteSpace(script))
        {
            result.Issues.Add(new CoordCsvIssue(0, "", "", "nothing was pasted"));
            return result;
        }

        var text = script!;

        // A pasted CE .CT clipboard fragment carries the script XML-ESCAPED. Our own
        // exporter escapes FIVE entities (CheatTableBuilder.EscapeXml) while
        // CeXmlExportService.ExtractAssemblerScript only reverses three, so decode all
        // five here — and only when the paste really is the XML form, so a label that
        // legitimately contains the text "&lt;" in a raw script is left alone.
        if (LooksLikeCheatTableXml(text)) text = DecodeXmlEntities(text);

        var start = s_startFence.Match(text);
        if (!start.Success)
        {
            result.Issues.Add(new CoordCsvIssue(0, "", "",
                "no '-- @UE5CD:COORDS v1' block found. Paste the whole generated script, " +
                "including the comment lines around the coordinate list."));
            return result;
        }

        if (!int.TryParse(start.Groups[1].Value, out int version))
        {
            result.Issues.Add(new CoordCsvIssue(LineOf(text, start.Index), "", start.Value,
                "the block's version is not a number"));
            return result;
        }
        if (version > SupportedVersion)
        {
            // Distinguishable from "no block" — the AOBMaker bug this avoids.
            result.Issues.Add(new CoordCsvIssue(LineOf(text, start.Index), "", start.Value,
                $"this block was written by a newer UE5CEDumper (format v{version}); " +
                $"this build understands v{SupportedVersion}"));
            return result;
        }

        var end = s_endFence.Match(text, start.Index + start.Length);
        if (!end.Success)
        {
            result.Issues.Add(new CoordCsvIssue(LineOf(text, start.Index), "", "",
                "the block is not closed — '-- @UE5CD:END' is missing. Was the paste truncated?"));
            return result;
        }

        var source = text[(start.Index + start.Length)..end.Index];
        int blockOffset = start.Index + start.Length;

        // Every lookup below runs on a copy with string literals and comments masked,
        // so `label='Arena z=0 plane'` or `-- z=5 was the old floor` cannot be read as a
        // field (B21, hole 3); values are then read from the source at the same offsets.
        // A string that never closes stops the import: the maintainer's rule (2026-10-28)
        // is an error, not a guess at where the stray quote's field ends.
        var strings = new List<(int Start, int End)>();
        var block = MaskLiterals(source, out int openAt, out string what, strings);
        if (block == null)
        {
            int eol = source.IndexOf('\n', openAt);
            result.Issues.Add(new CoordCsvIssue(LineOf(text, blockOffset + openAt), "",
                Trim(eol < 0 ? source[openAt..] : source[openAt..eol]),
                $"{what} -- nothing was imported; fix it and import again"));
            return result;
        }

        // Optional: absent in blocks written before the field existed, which is why a
        // missing value leaves the user's current setting alone rather than zeroing it.
        var zt = Regex.Match(block,
            @"(?<![A-Za-z0-9_])Z_TOLERANCE[ 	]*=[ 	]*(-?(?:\d+\.?\d*|\.\d+)(?:[eE][+-]?\d+)?)",
            RegexOptions.CultureInvariant);
        if (zt.Success && CoordPrecision.TryParse(zt.Groups[1].Value, out var ztv))
            result.ZTolerance = CoordPrecision.Round(ztv);

        var table = s_coordsTable.Match(block);
        if (!table.Success)
        {
            result.Issues.Add(new CoordCsvIssue(LineOf(text, blockOffset), "", "",
                "the block has no 'local COORDS = {' table"));
            return result;
        }

        int open = block.IndexOf('{', table.Index);
        var inner = ExtractBalanced(block, open);
        if (inner == null)
        {
            result.Issues.Add(new CoordCsvIssue(LineOf(text, blockOffset + table.Index), "", "",
                "the COORDS table's braces are unbalanced"));
            return result;
        }

        // An EVEN number of stray quotes on one line pair with each other, so every string
        // "closes" and the first one silently ends the value early. What sits on either side
        // of a string tells them apart: in an entry a string may only come after `=`, a
        // separator or an opening brace or bracket, and be followed by a separator, a closing
        // brace or bracket, or the next `key =` (the format tolerates a missing comma).
        // Anything else is a stray quote -- and Lua would refuse the line too.
        int innerEnd = open + 1 + inner.Length;
        foreach (var (s0, s1) in strings)
        {
            if (s0 <= open || s1 >= innerEnd) continue;
            bool before = MayPrecedeAString(block, s0 - 1);
            if (before && MayFollowAString(block, s1 + 1)) continue;
            int at = before ? s1 : s0;
            int sol = source.LastIndexOf('\n', at) + 1, eol = source.IndexOf('\n', at);
            result.Issues.Add(new CoordCsvIssue(LineOf(text, blockOffset + at), "",
                Trim(source[sol..(eol < 0 ? source.Length : eol)]),
                (before
                    ? "unpaired quote: text follows a string's closing quote"
                    : "unpaired quote: a string starts right after other text") +
                ", so a quote inside a string was not escaped (\\' or \\\") -- nothing was imported; " +
                "fix it and import again"));
            return result;
        }

        int innerOffset = blockOffset + open + 1;
        foreach (var (masked, entryOffset) in TopLevelBraceGroups(inner))
        {
            int line = LineOf(text, innerOffset + entryOffset);
            int at = open + 1 + entryOffset + 1;   // the entry's first character, inside its brace
            var entry = ParseEntry(new Entry(source.Substring(at, masked.Length), masked), line, result.Issues);
            if (entry != null) result.Entries.Add(entry);
        }

        if (result.Entries.Count == 0 && result.Issues.Count == 0)
            result.Issues.Add(new CoordCsvIssue(0, "", "", "the coordinate list is empty"));

        return result;
    }

    /// <summary>One entry's text twice, the same length: as written, and with its
    /// literals and comments masked (<see cref="MaskLiterals"/>). Keys are found in
    /// <see cref="Masked"/>; values are read from <see cref="Source"/> at the same offset.</summary>
    private readonly record struct Entry(string Source, string Masked);

    private static CoordEntry? ParseEntry(Entry entry, int line, List<CoordCsvIssue> issues)
    {
        var e = new CoordEntry
        {
            Uid = CoordText.Normalize(Str(entry, "uid"), CoordText.MaxUidLength),   // (B7)
        };

        var rawLabel = Str(entry, "label");
        if (rawLabel == null)
        {
            issues.Add(new CoordCsvIssue(line, "label", Trim(entry.Source), "no label field"));
            return null;
        }
        if (CoordText.HasBlockedChars(rawLabel))
            issues.Add(new CoordCsvIssue(line, "label", rawLabel, "control characters removed"));
        e.Label = CoordText.Normalize(rawLabel, CoordText.MaxLabelLength);
        if (e.Label.Length == 0)
        {
            issues.Add(new CoordCsvIssue(line, "label", rawLabel, "label is empty"));
            return null;
        }

        e.Group = CoordText.Normalize(Str(entry, "group") ?? "", CoordText.MaxGroupLength);
        e.Map = CoordText.Normalize(Str(entry, "map") ?? "", 256);

        bool bad = false;
        double N(string key)
        {
            var raw = RawNum(entry, key);
            if (raw != null && CoordPrecision.TryParse(raw, out var v))
                return CoordPrecision.Round(v);

            // Absent is fine (rotation fields are optional and default to 0). But a
            // key that IS present with a value the number regex could not match --
            // `x=oops`, a stray unit suffix, a comma decimal separator -- must be
            // reported, not silently imported as 0. That distinction is the whole
            // difference between a diagnostic and a quietly wrong coordinate.
            if (!HasKey(entry, key)) return 0;

            issues.Add(new CoordCsvIssue(line, key, ValueTextAfter(entry, key), "not a number"));
            bad = true;
            return 0;
        }

        e.X = N("x"); e.Y = N("y"); e.Z = N("z");
        e.Pitch = N("pitch"); e.Yaw = N("yaw"); e.Roll = N("roll");
        return bad ? null : e;
    }

    // ── Field extraction ────────────────────────────────────────────────

    /// <summary>Where the first code-level <c>key =</c> puts its value, or -1. The
    /// identifier lookbehind stops <c>xlabel=</c> shadowing <c>label=</c>.</summary>
    private static int ValueAt(Entry entry, string key)
    {
        var m = Regex.Match(entry.Masked, $@"(?<![A-Za-z0-9_]){Regex.Escape(key)}[ \t]*=",
                            RegexOptions.CultureInvariant);
        if (!m.Success) return -1;
        // Skip the blanks in the SOURCE: in the mask a string's own quotes are blanks too.
        int at = m.Index + m.Length;
        while (at < entry.Source.Length && (entry.Source[at] == ' ' || entry.Source[at] == '\t')) at++;
        return at;
    }

    /// <summary>
    /// Read a string field. Accepts BOTH quote styles — AOBMaker's parser silently
    /// ignores single-quoted values and falls back to a default, which is exactly the
    /// kind of quiet wrong answer this format is meant to avoid.
    /// </summary>
    private static string? Str(Entry entry, string key)
    {
        int at = ValueAt(entry, key);
        if (at < 0 || at >= entry.Source.Length) return null;
        char q = entry.Source[at];
        if (q != '\'' && q != '"') return null;
        // MaskLiterals already proved every string here closes on its own line.
        return Unescape(entry.Source[(at + 1)..CloseQuote(entry.Source, at)]);
    }

    /// <summary>True when the key appears at all, whatever follows it.</summary>
    private static bool HasKey(Entry entry, string key) => ValueAt(entry, key) >= 0;

    /// <summary>The raw text a key was assigned, up to the next code-level comma or
    /// brace — used only to quote the offending value back to the user in a diagnostic.</summary>
    private static string ValueTextAfter(Entry entry, string key)
    {
        int at = ValueAt(entry, key);
        if (at < 0) return "";
        int end = entry.Masked.IndexOfAny(new[] { ',', '}' }, at);
        return entry.Source[at..(end < 0 ? entry.Source.Length : end)].Trim();
    }

    private static string? RawNum(Entry entry, string key)
    {
        int at = ValueAt(entry, key);
        if (at < 0) return null;
        var m = Regex.Match(entry.Masked[at..], @"^-?(?:\d+\.?\d*|\.\d+)(?:[eE][+-]?\d+)?",
                            RegexOptions.CultureInvariant);
        return m.Success ? m.Value : null;
    }

    /// <summary>Reverse <see cref="CeLuaHygiene.EscapeLuaString"/>, including the
    /// decimal escapes it uses to break closing long brackets.</summary>
    internal static string Unescape(string s)
    {
        var sb = new System.Text.StringBuilder(s.Length);
        for (int i = 0; i < s.Length; i++)
        {
            if (s[i] != '\\') { sb.Append(s[i]); continue; }
            if (i + 1 >= s.Length) { sb.Append('\\'); break; }
            char n = s[++i];
            switch (n)
            {
                case 'n': sb.Append('\n'); break;
                case 'r': sb.Append('\r'); break;
                case 't': sb.Append('\t'); break;
                case '\\': sb.Append('\\'); break;
                case '\'': sb.Append('\''); break;
                case '"': sb.Append('"'); break;
                default:
                    if (char.IsDigit(n))
                    {
                        // \ddd decimal escape, up to three digits.
                        int v = n - '0', digits = 1;
                        while (digits < 3 && i + 1 < s.Length && char.IsDigit(s[i + 1]))
                        {
                            v = v * 10 + (s[++i] - '0');
                            digits++;
                        }
                        sb.Append(v <= 0xFF ? (char)v : '?');
                    }
                    else sb.Append(n);
                    break;
            }
        }
        return sb.ToString();
    }

    // ── Brace scanning ──────────────────────────────────────────────────

    /// <summary>Text between <paramref name="openIndex"/> and its matching brace,
    /// skipping braces that live inside quoted strings.</summary>
    internal static string? ExtractBalanced(string s, int openIndex)
    {
        if (openIndex < 0 || openIndex >= s.Length || s[openIndex] != '{') return null;
        int depth = 0;
        for (int i = openIndex; i < s.Length; i++)
        {
            char c = s[i];
            if (c == '\'' || c == '"') { i = SkipString(s, i); continue; }
            if (c == '{') depth++;
            else if (c == '}')
            {
                depth--;
                if (depth == 0) return s[(openIndex + 1)..i];
            }
        }
        return null;
    }

    /// <summary>Each top-level <c>{ … }</c> group with its offset inside
    /// <paramref name="inner"/>, so a bad entry can be reported by line.</summary>
    internal static IEnumerable<(string Text, int Offset)> TopLevelBraceGroups(string inner)
    {
        for (int i = 0; i < inner.Length; i++)
        {
            char c = inner[i];
            if (c == '\'' || c == '"') { i = SkipString(inner, i); continue; }
            if (c != '{') continue;
            var body = ExtractBalanced(inner, i);
            if (body == null) yield break;
            yield return (body, i);
            i += body.Length + 1;
        }
    }

    /// <summary>Fills a masked string literal. Not a blank: a string right after another
    /// (<c>'It''s'</c>) must not read as whitespace to <see cref="MayFollowAString"/>.</summary>
    private const char StringMark = '\u0001';

    /// <summary>
    /// <paramref name="s"/> with every string literal filled with <see cref="StringMark"/> and
    /// every comment blanked to spaces, line breaks kept, so a lookup on the result sees code
    /// only and every offset still matches the source. Each string's span goes to
    /// <paramref name="strings"/>. Null when a string or a long bracket never closes:
    /// <paramref name="openAt"/> is where it opened and <paramref name="what"/> says which.
    /// </summary>
    internal static string? MaskLiterals(string s, out int openAt, out string what,
                                         List<(int Start, int End)>? strings = null)
    {
        var m = s.ToCharArray();
        (openAt, what) = (-1, "");
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            int end, level;
            char fill = StringMark;
            if (c == '\'' || c == '"')
            {
                end = CloseQuote(s, i);
                if (end < 0)
                {
                    (openAt, what) = (i, $"unpaired quote: a string opened with {c} never closes on its line");
                    return null;
                }
                strings?.Add((i, end));
            }
            else if (c == '-' && i + 1 < s.Length && s[i + 1] == '-')
            {
                fill = ' ';
                if ((level = LongBracketLevel(s, i + 2)) >= 0)
                {
                    end = LongBracketClose(s, i + 2 + level + 2, level);
                    if (end < 0)
                    {
                        (openAt, what) = (i, "unclosed bracket: a --[[ comment never closes");
                        return null;
                    }
                }
                else
                {
                    int eol = s.IndexOfAny(new[] { '\r', '\n' }, i);
                    end = (eol < 0 ? s.Length : eol) - 1;
                }
            }
            else if (c == '[' && (level = LongBracketLevel(s, i)) >= 0)
            {
                end = LongBracketClose(s, i + level + 2, level);
                if (end < 0)
                {
                    (openAt, what) = (i, "unclosed bracket: a [[ string never closes");
                    return null;
                }
                strings?.Add((i, end));
            }
            else continue;

            for (int k = i; k <= end; k++)
                if (m[k] != '\n' && m[k] != '\r') m[k] = fill;
            i = end;
        }
        return new string(m);
    }

    /// <summary>Whether the code before a string, ending at <paramref name="before"/> in the
    /// masked text, is something an entry allows before one: <c>=</c>, a separator, or an
    /// opening brace or bracket.</summary>
    private static bool MayPrecedeAString(string masked, int before)
    {
        int i = before;
        while (i >= 0 && char.IsWhiteSpace(masked[i])) i--;
        return i >= 0 && "=,;{[".IndexOf(masked[i]) >= 0;
    }

    /// <summary>Whether what follows a string value at <paramref name="from"/> in the masked
    /// text (comments are blanks there) is something an entry allows after one: a
    /// separator, a closing brace or bracket, or the next <c>key =</c>.</summary>
    private static bool MayFollowAString(string masked, int from)
    {
        int i = from;
        while (i < masked.Length && char.IsWhiteSpace(masked[i])) i++;
        if (i >= masked.Length || ",;}]".IndexOf(masked[i]) >= 0) return true;
        if (!(char.IsLetter(masked[i]) || masked[i] == '_')) return false;
        while (i < masked.Length && (char.IsLetterOrDigit(masked[i]) || masked[i] == '_')) i++;
        while (i < masked.Length && (masked[i] == ' ' || masked[i] == '\t')) i++;
        return i < masked.Length && masked[i] == '=' && (i + 1 >= masked.Length || masked[i + 1] != '=');
    }

    /// <summary>Index of the quote closing the short string that opens at
    /// <paramref name="start"/>, or -1 when it does not close on its line (Lua refuses
    /// a raw line break inside one). A backslash escapes the next character.</summary>
    private static int CloseQuote(string s, int start)
    {
        char quote = s[start];
        for (int i = start + 1; i < s.Length; i++)
        {
            char c = s[i];
            if (c == '\\') { i++; continue; }
            if (c == quote) return i;
            if (c == '\n' || c == '\r') return -1;
        }
        return -1;
    }

    /// <summary>The level of a long bracket opening at <paramref name="at"/>
    /// (<c>[[</c> is 0, <c>[==[</c> is 2), or -1 when there is none.</summary>
    private static int LongBracketLevel(string s, int at)
    {
        if (at >= s.Length || s[at] != '[') return -1;
        int i = at + 1;
        while (i < s.Length && s[i] == '=') i++;
        return i < s.Length && s[i] == '[' ? i - at - 1 : -1;
    }

    /// <summary>Index of the last character of the closing long bracket of
    /// <paramref name="level"/>, searching from <paramref name="from"/>, or -1.</summary>
    private static int LongBracketClose(string s, int from, int level)
    {
        var close = "]" + new string('=', level) + "]";
        int at = from <= s.Length ? s.IndexOf(close, from, StringComparison.Ordinal) : -1;
        return at < 0 ? -1 : at + close.Length - 1;
    }

    /// <summary>Index of the closing quote of the string starting at
    /// <paramref name="start"/>, honouring backslash escapes.</summary>
    private static int SkipString(string s, int start)
    {
        char quote = s[start];
        for (int i = start + 1; i < s.Length; i++)
        {
            if (s[i] == '\\') { i++; continue; }
            if (s[i] == quote) return i;
            if (s[i] == '\n') return i;      // unterminated: don't run off the end
        }
        return s.Length - 1;
    }

    // ── Misc ────────────────────────────────────────────────────────────

    private static int LineOf(string text, int index)
    {
        int line = 1;
        for (int i = 0; i < index && i < text.Length; i++)
            if (text[i] == '\n') line++;
        return line;
    }

    private static string Trim(string s) => s.Length <= 60 ? s.Trim() : s[..60].Trim() + "…";

    internal static bool LooksLikeCheatTableXml(string s) =>
        s.Contains("<AssemblerScript", StringComparison.OrdinalIgnoreCase) ||
        s.TrimStart().StartsWith("<?xml", StringComparison.OrdinalIgnoreCase) ||
        s.Contains("<CheatTable", StringComparison.OrdinalIgnoreCase);

    /// <summary>Decode the FIVE entities <c>CheatTableBuilder.EscapeXml</c> emits.
    /// <c>&amp;amp;</c> last, so an escaped entity like <c>&amp;amp;lt;</c> is not
    /// collapsed twice.</summary>
    internal static string DecodeXmlEntities(string s) => s
        .Replace("&lt;", "<")
        .Replace("&gt;", ">")
        .Replace("&quot;", "\"")
        .Replace("&apos;", "'")
        .Replace("&amp;", "&");
}
