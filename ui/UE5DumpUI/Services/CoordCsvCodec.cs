using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UE5DumpUI.Models;

namespace UE5DumpUI.Services;

/// <summary>One thing that went wrong on a single CSV line.</summary>
public sealed record CoordCsvIssue(int Line, string Column, string RawText, string Reason)
{
    public override string ToString() =>
        string.IsNullOrEmpty(Column)
            ? $"line {Line}: {Reason}"
            : $"line {Line} [{Column}]: {Reason} — '{Truncate(RawText)}'";

    private static string Truncate(string s) =>
        s.Length <= 40 ? s : s[..40] + "…";
}

/// <summary>What an import would do to one entry, decided BEFORE anything is committed.</summary>
public enum CoordChangeKind
{
    /// <summary>Not in the library — would be appended.</summary>
    Added,
    /// <summary>Matched an existing entry and differs.</summary>
    Changed,
    /// <summary>Matched an existing entry and is identical.</summary>
    Unchanged,
    /// <summary>In the library but absent from the file (Replace mode only).</summary>
    Removed,
}

/// <summary>One row of the import preview.</summary>
public sealed class CoordChange
{
    /// <summary>Settable: <see cref="CoordCsvCodec.Diff"/> decides Changed vs
    /// Unchanged only after comparing every field.</summary>
    public CoordChangeKind Kind { get; set; }
    public CoordEntry? Incoming { get; init; }
    public CoordEntry? Existing { get; init; }

    /// <summary>Per-field descriptions like "Group: 1-2 → 2001/1/2". This is the only
    /// thing that makes an Excel round trip safe: Excel silently coerces `1-2` to a
    /// date and `0012` to `12`, writes back the DISPLAYED text, and the result is
    /// perfectly valid CSV that no validator can reject.</summary>
    public List<string> FieldDiffs { get; } = new();

    /// <summary>Set when the importer had to mutate the incoming value (control
    /// characters removed, over-length label truncated). Never silent.</summary>
    public List<string> Mutations { get; } = new();

    public string Label => Incoming?.Label ?? Existing?.Label ?? "";
}

/// <summary>The full result of parsing a CSV, before any commit.</summary>
public sealed class CoordCsvParseResult
{
    public List<CoordEntry> Entries { get; } = new();
    public List<CoordCsvIssue> Issues { get; } = new();

    /// <summary>Detected delimiter (informational — a ';' file implies a European
    /// Excel re-save, where the decimal separator is a comma).</summary>
    public char Delimiter { get; set; } = ',';

    public bool HasEntries => Entries.Count > 0;

    /// <summary>Z tolerance carried by a LUA block (null when the block predates the
    /// field, or when the source is a CSV, which has no such column by design). A null
    /// leaves the user's current setting alone rather than zeroing it.</summary>
    public double? ZTolerance { get; set; }
}

/// <summary>
/// CSV round-trip for the Teleport coordinate library — docs/teleport-coord-library-spec.md §5.
///
/// The repo had NO CSV reader or writer before this, so every rule here is stated
/// rather than inherited. The ones that are not obvious:
///
///  * <b>RFC 4180 quoting is mandatory.</b> §4.5 permits a comma in a Label, and one
///    unquoted comma shifts every subsequent column — the entry is then either
///    dropped or, worse, imported with Group="…" and Map=a number.
///  * <b>Split with NO RemoveEmptyEntries.</b> The repo's only existing delimited
///    reader (<c>BugItGoParser</c>) uses it, and it is the obvious template — but an
///    empty Group (a real, common case) would collapse 10 fields to 9 and silently
///    shift Map→Group and X→Map.
///  * <b>UTF-8 WITH BOM on write.</b> A deliberate exception to the house BOM-less
///    rule (<c>DumpAllService</c>): CSV's whole purpose is spreadsheet editing, and a
///    BOM-less CJK export double-clicked on a zh-TW machine opens as mojibake, after
///    which the user "repairs" the names by retyping them.
///  * <b>Formula-injection armouring.</b> Excel evaluates a cell starting <c>= + - @</c>;
///    <c>=Boss Arena</c> displays as <c>#NAME?</c> and Excel saves the DISPLAYED text,
///    destroying the label with no error anywhere. Quoting does not mitigate it.
///  * <b>Header is authoritative and matched by NAME</b>, so reordered/extra columns
///    and a future v2 column are all handled — the same forward-compatibility that
///    made the named-field Lua format win over a positional one.
///  * <b>Quotes are read strictly, and an unpaired one stops the import.</b> No repair:
///    the maintainer's call (2026-10-28, B21). A lenient reader has to guess where the
///    stray quote's field ends, and the two lenient splitters this replaced guessed
///    differently — one stray quote merged every later record into one.
/// </summary>
public static class CoordCsvCodec
{
    // Canonical column order for WRITING. Reading matches by name, so this order is
    // a convention, not a contract.
    private static readonly string[] s_columns =
        { "uid", "label", "group", "map", "x", "y", "z", "pitch", "yaw", "roll" };

    /// <summary>Delimiters sniffed from the header line, in preference order.</summary>
    private static readonly char[] s_candidateDelimiters = { ',', ';', '\t' };

    /// <summary>Leading characters Excel treats as the start of a formula.</summary>
    private const string FormulaLeaders = "=+-@";

    // ── Write ───────────────────────────────────────────────────────────

    /// <summary>Serialize the library to RFC 4180 CSV (LF line endings, no BOM —
    /// the BOM is added by <see cref="WriteFile"/>, which owns the encoding).</summary>
    public static string Write(IEnumerable<CoordEntry> entries)
    {
        var sb = new StringBuilder(4096);
        sb.Append(string.Join(",", s_columns)).Append('\n');
        foreach (var e in entries)
        {
            sb.Append(Field(e.Uid)).Append(',');
            sb.Append(Field(Armour(e.Label))).Append(',');
            sb.Append(Field(Armour(e.Group))).Append(',');
            sb.Append(Field(Armour(e.Map))).Append(',');
            // Round defensively on write as well as at capture. Entries normally
            // arrive already rounded, but an entry built by any other path (a test, a
            // future importer, a hand-constructed row) would otherwise emit an
            // unrounded value that the reader then rounds -- breaking the
            // Write -> Parse -> Write byte-stability the whole precision rule exists
            // to provide. Rounding here makes that property unconditional.
            sb.Append(Num(e.X)).Append(',');
            sb.Append(Num(e.Y)).Append(',');
            sb.Append(Num(e.Z)).Append(',');
            sb.Append(Num(e.Pitch)).Append(',');
            sb.Append(Num(e.Yaw)).Append(',');
            sb.Append(Num(e.Roll)).Append('\n');
        }
        return sb.ToString();
    }

    /// <summary>Write to disk as UTF-8 WITH BOM. See the class remarks for why this
    /// one export deviates from the repo's BOM-less convention.</summary>
    public static void WriteFile(string path, IEnumerable<CoordEntry> entries)
        => File.WriteAllText(path, Write(entries), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

    /// <summary>A header + three illustrative rows, for users starting from scratch.</summary>
    public static string SampleTemplate() => Write(new[]
    {
        new CoordEntry { Uid = "k3f9", Label = "Mountain", Group = "Fields", Map = "Map01",
                         X = 67162.398, Y = -20380.643, Z = 35.791, Pitch = 347.71, Yaw = 31.98 },
        new CoordEntry { Uid = "k3fa", Label = "Chest 1", Group = "Chest", Map = "Map01",
                         X = 87668.672, Y = -24858.674, Z = 341.376, Pitch = 335.01, Yaw = 26.16 },
        new CoordEntry { Uid = "k3fb", Label = "Boss", Group = "", Map = "Map02",
                         X = 89828.133, Y = -15534.243, Z = 850.328, Pitch = 346.88, Yaw = 0.93 },
    });

    /// <summary>Canonical numeric text: round, then shortest-round-trippable.</summary>
    private static string Num(double v) => CoordPrecision.Text(CoordPrecision.Round(v));

    /// <summary>RFC 4180: quote when the value contains the delimiter, a quote, or
    /// leading/trailing whitespace; escape a quote by doubling it.</summary>
    internal static string Field(string? raw)
    {
        var s = raw ?? "";
        bool needsQuotes = s.Length > 0 &&
            (s.IndexOfAny(new[] { ',', ';', '\t', '"', '\n', '\r' }) >= 0 ||
             char.IsWhiteSpace(s[0]) || char.IsWhiteSpace(s[^1]));
        if (!needsQuotes) return s;
        return "\"" + s.Replace("\"", "\"\"") + "\"";
    }

    /// <summary>
    /// True when <paramref name="s"/> starts with a formula leader once any leading
    /// apostrophes are skipped. Armouring every such value, not only a bare leader, is what
    /// makes the round trip exact: an armoured value has this shape itself, so a label that
    /// really began <c>'=</c> must be armoured too, or <see cref="Unarmour"/> takes its own
    /// apostrophe for the armour and drops it.
    /// </summary>
    private static bool NeedsArmour(string s)
    {
        int i = 0;
        while (i < s.Length && s[i] == '\'') i++;
        return i < s.Length && FormulaLeaders.IndexOf(s[i]) >= 0;
    }

    /// <summary>Prefix a formula-shaped value with an apostrophe. Stripped again by
    /// <see cref="Unarmour"/>, so the round trip is transparent.</summary>
    internal static string Armour(string? raw)
    {
        var s = raw ?? "";
        return NeedsArmour(s) ? "'" + s : s;
    }

    /// <summary>Remove exactly one armouring apostrophe.</summary>
    internal static string Unarmour(string? raw)
    {
        var s = raw ?? "";
        return s.Length >= 2 && s[0] == '\'' && NeedsArmour(s[1..]) ? s[1..] : s;
    }

    // ── Read ────────────────────────────────────────────────────────────

    /// <summary>Read a CSV file, tolerating a UTF-8 BOM and any line ending.</summary>
    public static CoordCsvParseResult ParseFile(string path)
        => Parse(File.ReadAllText(path, Encoding.UTF8));   // .NET strips a leading BOM

    /// <summary>
    /// Parse CSV text into entries plus per-line diagnostics. Never throws and never
    /// silently drops a row: everything rejected lands in
    /// <see cref="CoordCsvParseResult.Issues"/> with a 1-based file line number. An
    /// unpaired quote is the one fault that is not per-row: every record after it is in
    /// doubt, so it is the only issue returned and nothing is imported.
    /// </summary>
    public static CoordCsvParseResult Parse(string? text)
    {
        var result = new CoordCsvParseResult();
        if (string.IsNullOrWhiteSpace(text))
        {
            result.Issues.Add(new CoordCsvIssue(0, "", "", "the file is empty"));
            return result;
        }

        // Strip a BOM defensively (a caller may hand us bytes decoded without one).
        var body = text!.TrimStart('﻿');
        // The delimiter must be known before any record is read: a quote opens a field
        // only at field start, and field start means "after a delimiter". The header's
        // first physical line is enough to sniff it.
        int eol = body.IndexOfAny(new[] { '\r', '\n' });
        char delimiter = SniffDelimiter(eol < 0 ? body : body[..eol]);
        result.Delimiter = delimiter;

        var lines = ReadRecords(body, delimiter, out var quoteError);
        if (quoteError != null)
        {
            result.Issues.Add(quoteError);
            return result;
        }
        if (lines.Count == 0)
        {
            result.Issues.Add(new CoordCsvIssue(0, "", "", "the file is empty"));
            return result;
        }

        // Header: map column name -> index.
        var headerLine = lines[0].Text;
        var headerCells = lines[0].Cells;
        var index = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < headerCells.Count; i++)
        {
            var name = headerCells[i].Trim().Trim('﻿');
            if (name.Length > 0 && !index.ContainsKey(name)) index[name] = i;
        }

        foreach (var required in new[] { "label", "x", "y", "z" })
        {
            if (!index.ContainsKey(required))
            {
                result.Issues.Add(new CoordCsvIssue(lines[0].Number, required, headerLine,
                    $"required column '{required}' is missing from the header"));
                return result;
            }
        }

        // A stray quote PAIR is valid grammar but merges or splits cells, shifting every
        // later column. RFC 4180 gives every record the header's cell count, so a record
        // that differs stops the import before any row is read.
        for (int li = 1; li < lines.Count; li++)
        {
            var rec = lines[li];
            if (rec.Text.Trim().Length == 0 || rec.Cells.Count == headerCells.Count) continue;
            result.Issues.Add(new CoordCsvIssue(rec.Number, "", rec.Text,
                $"this record has {rec.Cells.Count} cells and the header has {headerCells.Count}, so a stray " +
                "quote pair or delimiter merged or split them -- nothing was imported; fix it and import again"));
            return result;
        }

        // A ';' delimiter means a European Excel re-save, which also writes commas as
        // decimal separators. Accept that, but only in that case -- guessing per-value
        // would misread a thousands separator.
        bool commaDecimals = delimiter == ';';

        for (int li = 1; li < lines.Count; li++)
        {
            var (lineNo, raw, cells) = (lines[li].Number, lines[li].Text, lines[li].Cells);
            if (raw.Trim().Length == 0) continue;

            var entry = new CoordEntry();
            bool bad = false;

            string Cell(string col)
            {
                if (!index.TryGetValue(col, out int i) || i >= cells.Count) return "";
                return cells[i];
            }

            // Through the same gate as label/group. A uid is a text field read from a
            // user-editable CSV; it was the only one that skipped Normalize, so a control
            // character or a 4 KB cell went straight into the persisted library. (B7)
            entry.Uid = CoordText.Normalize(Cell("uid"), CoordText.MaxUidLength);

            foreach (var (col, maxLen, set) in new (string, int, Action<string>)[]
                     {
                         ("label", CoordText.MaxLabelLength, v => entry.Label = v),
                         ("group", CoordText.MaxGroupLength, v => entry.Group = v),
                         ("map",   256,                      v => entry.Map = v),
                     })
            {
                var rawCell = Unarmour(Cell(col));
                var norm = CoordText.Normalize(rawCell, maxLen);
                if (CoordText.HasBlockedChars(rawCell))
                {
                    result.Issues.Add(new CoordCsvIssue(lineNo, col, rawCell,
                        "control characters removed (they cannot survive a generated Lua script)"));
                }
                else if (norm.Length != rawCell.Trim().Length)
                {
                    result.Issues.Add(new CoordCsvIssue(lineNo, col, rawCell,
                        $"truncated to {maxLen} characters"));
                }
                set(norm);
            }

            if (entry.Label.Length == 0)
            {
                result.Issues.Add(new CoordCsvIssue(lineNo, "label", raw, "label is empty"));
                bad = true;
            }

            foreach (var (col, set) in new (string, Action<double>)[]
                     {
                         ("x", v => entry.X = v), ("y", v => entry.Y = v), ("z", v => entry.Z = v),
                         ("pitch", v => entry.Pitch = v), ("yaw", v => entry.Yaw = v),
                         ("roll", v => entry.Roll = v),
                     })
            {
                if (!index.ContainsKey(col)) { set(0); continue; }   // optional rotation columns
                var cell = Cell(col).Trim();
                if (cell.Length == 0) { set(0); continue; }
                var normalized = commaDecimals ? cell.Replace(',', '.') : cell;
                if (!CoordPrecision.TryParse(normalized, out var v))
                {
                    result.Issues.Add(new CoordCsvIssue(lineNo, col, cell, "not a number"));
                    bad = true;
                    continue;
                }
                set(CoordPrecision.Round(v));
            }

            if (!bad) result.Entries.Add(entry);
        }

        return result;
    }

    /// <summary>Pick the delimiter that yields the most cells on the header line.
    /// Comma wins ties, so a plain RFC 4180 file is never misread.</summary>
    internal static char SniffDelimiter(string header)
    {
        char best = ',';
        int bestCount = SplitCsvLine(header, ',').Count;
        foreach (var d in s_candidateDelimiters.Skip(1))
        {
            int n = SplitCsvLine(header, d).Count;
            if (n > bestCount) { best = d; bestCount = n; }
        }
        return best;
    }

    /// <param name="Number">The physical 1-based line the record STARTS on — what the
    /// user sees in their editor.</param>
    private sealed record SourceRecord(int Number, string Text, List<string> Cells);

    /// <summary>
    /// Read every record, quoting strictly to RFC 4180: a quote may open a field (at
    /// field start only), close it, or appear doubled inside it. Anything else is an
    /// unpaired quote, returned in <paramref name="error"/> to stop the import; no
    /// repair is attempted. Line breaks may be CRLF, LF or CR, and a quoted field may
    /// hold one (Excel's Alt+Enter writes exactly that). Fields are positional with no
    /// empty-entry removal: an empty Group must keep its column, or every later column
    /// shifts left.
    /// </summary>
    private static List<SourceRecord> ReadRecords(string text, char delimiter, out CoordCsvIssue? error)
    {
        var records = new List<SourceRecord>();
        var cells = new List<string>();
        var field = new StringBuilder();
        bool inQuotes = false, closedQuote = false;
        int physical = 1, lineStart = 0, recordLine = 1, recordStart = 0, quoteLine = 1, quoteLineStart = 0;
        error = null;

        CoordCsvIssue Unpaired(int line, int startOfLine, string what)
        {
            int end = text.IndexOfAny(new[] { '\r', '\n' }, startOfLine);
            return new CoordCsvIssue(line, "", end < 0 ? text[startOfLine..] : text[startOfLine..end],
                $"unpaired quote: {what} -- nothing was imported; fix the quote and import again");
        }

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                    else { inQuotes = false; closedQuote = true; }
                    continue;
                }
                field.Append(c);
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') { field.Append('\n'); i++; }
                if (c == '\r' || c == '\n') { physical++; lineStart = i + 1; }
                continue;
            }
            if (c == '"')
            {
                if (field.Length == 0 && !closedQuote)
                {
                    inQuotes = true;
                    (quoteLine, quoteLineStart) = (physical, lineStart);
                    continue;
                }
                error = Unpaired(physical, lineStart, closedQuote
                    ? "a quote inside a quoted field must be doubled (\"\"), and this one ends the field early"
                    : field.ToString().Trim().Length == 0
                        ? "a space before a field's opening quote -- nothing may come between the delimiter and the quote"
                        : "a quote in the middle of a field, which a quote may only open (or appear doubled inside a quoted one)");
                return records;
            }
            if (closedQuote && c != delimiter && c != '\r' && c != '\n')
            {
                // The space is the whole fault only when blanks alone run on to the delimiter, a
                // line break or the end; anything after them means the quote that "closed" the
                // field was an inner one left undoubled. Otherwise, a field that closed on a LATER
                // line than it opened usually means the opening quote was the stray one and this
                // "closing" quote opens a correct field: name both, the opening line first, or the
                // user is sent to fix a row that has nothing wrong with it.
                int k = i;
                while (k < text.Length && text[k] != delimiter && text[k] != '\r' && text[k] != '\n'
                       && char.IsWhiteSpace(text[k])) k++;
                bool onlySpace = k >= text.Length || text[k] == delimiter || text[k] == '\r' || text[k] == '\n';
                error = onlySpace
                    ? Unpaired(physical, lineStart,
                        "a space after a field's closing quote -- only the delimiter or a line break may follow it")
                    : quoteLine < physical
                        ? Unpaired(quoteLine, quoteLineStart,
                            $"a quoted field opened on this line runs on to line {physical}, where a quote ends it " +
                            "and text follows -- a quote on one of the two lines is unpaired")
                        : Unpaired(physical, lineStart,
                            "text follows a field's closing quote, so a quote inside it was not doubled (\"\")");
                return records;
            }
            if (c == delimiter)
            {
                cells.Add(field.ToString());
                field.Clear();
                closedQuote = false;
                continue;
            }
            if (c == '\r' || c == '\n')
            {
                int end = i;
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                cells.Add(field.ToString());
                records.Add(new SourceRecord(recordLine, text[recordStart..end], cells));
                cells = new List<string>();
                field.Clear();
                closedQuote = false;
                physical++;
                recordLine = physical;
                recordStart = lineStart = i + 1;
                continue;
            }
            field.Append(c);
        }
        if (inQuotes)
        {
            error = Unpaired(quoteLine, quoteLineStart, "a quoted field opened on this line never closes");
            return records;
        }
        if (recordStart < text.Length)   // the last record, with no line break after it
        {
            cells.Add(field.ToString());
            records.Add(new SourceRecord(recordLine, text[recordStart..], cells));
        }
        return records;
    }

    /// <summary>
    /// A lenient RFC 4180 field split, kept only to count the header's cells for
    /// <see cref="SniffDelimiter"/> — records are read by <see cref="ReadRecords"/>,
    /// which is strict, so a quote this tolerates still stops the import there.
    /// Positional with NO RemoveEmptyEntries, like the reader.
    /// </summary>
    internal static List<string> SplitCsvLine(string line, char delimiter)
    {
        var cells = new List<string>();
        var sb = new StringBuilder();
        bool inQuotes = false;

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                    else inQuotes = false;
                }
                else sb.Append(c);
                continue;
            }
            if (c == '"' && sb.Length == 0) { inQuotes = true; continue; }
            if (c == delimiter) { cells.Add(sb.ToString()); sb.Clear(); continue; }
            sb.Append(c);
        }
        cells.Add(sb.ToString());
        return cells;
    }

    // ── Diff (stage 1 of the two-stage import) ──────────────────────────

    /// <summary>
    /// Work out what an import WOULD do, without touching anything. Merge identity is
    /// uid-first, then the (Label, Group, Map) triple case-insensitively — never the
    /// coordinates, because a spreadsheet round trip rewrites those and nothing would
    /// match, turning a 4000-row merge into 8000 rows.
    /// </summary>
    public static List<CoordChange> Diff(
        IReadOnlyList<CoordEntry> existing, IReadOnlyList<CoordEntry> incoming, bool replace)
    {
        var changes = new List<CoordChange>();
        var matchedExisting = new HashSet<CoordEntry>();

        foreach (var inc in incoming)
        {
            var match = FindMatch(existing, inc, matchedExisting);
            if (match == null)
            {
                changes.Add(new CoordChange { Kind = CoordChangeKind.Added, Incoming = inc });
                continue;
            }
            matchedExisting.Add(match);

            var change = new CoordChange { Incoming = inc, Existing = match };
            AddFieldDiffs(change, match, inc);
            change.Kind = change.FieldDiffs.Count == 0
                ? CoordChangeKind.Unchanged
                : CoordChangeKind.Changed;
            changes.Add(change);
        }

        if (replace)
        {
            foreach (var old in existing)
                if (!matchedExisting.Contains(old))
                    changes.Add(new CoordChange { Kind = CoordChangeKind.Removed, Existing = old });
        }
        return changes;
    }

    private static CoordEntry? FindMatch(
        IReadOnlyList<CoordEntry> existing, CoordEntry inc, HashSet<CoordEntry> taken)
    {
        if (!string.IsNullOrEmpty(inc.Uid))
        {
            var byUid = existing.FirstOrDefault(
                e => !taken.Contains(e) && string.Equals(e.Uid, inc.Uid, StringComparison.Ordinal));
            if (byUid != null) return byUid;
        }
        return existing.FirstOrDefault(e => !taken.Contains(e) && e.SameIdentityAs(inc));
    }

    private static void AddFieldDiffs(CoordChange change, CoordEntry a, CoordEntry b)
    {
        void Text(string name, string x, string y)
        {
            if (!string.Equals(x, y, StringComparison.Ordinal))
                change.FieldDiffs.Add($"{name}: {x} → {y}");
        }
        void Num(string name, double x, double y)
        {
            if (x != y)
                change.FieldDiffs.Add($"{name}: {CoordPrecision.Text(x)} → {CoordPrecision.Text(y)}");
        }
        Text("Label", a.Label, b.Label);
        Text("Group", a.Group, b.Group);
        Text("Map", a.Map, b.Map);
        Num("X", a.X, b.X); Num("Y", a.Y, b.Y); Num("Z", a.Z, b.Z);
        Num("Pitch", a.Pitch, b.Pitch); Num("Yaw", a.Yaw, b.Yaw); Num("Roll", a.Roll, b.Roll);
    }

    /// <summary>One-line human summary of a diff, for the confirm prompt.</summary>
    public static string SummarizeDiff(IReadOnlyList<CoordChange> changes)
    {
        int add = changes.Count(c => c.Kind == CoordChangeKind.Added);
        int chg = changes.Count(c => c.Kind == CoordChangeKind.Changed);
        int same = changes.Count(c => c.Kind == CoordChangeKind.Unchanged);
        int rem = changes.Count(c => c.Kind == CoordChangeKind.Removed);
        var parts = new List<string>();
        if (add > 0) parts.Add($"{add} added");
        if (chg > 0) parts.Add($"{chg} changed");
        if (same > 0) parts.Add($"{same} unchanged");
        if (rem > 0) parts.Add($"{rem} removed");
        return parts.Count == 0 ? "no changes" : string.Join(", ", parts);
    }
}
