using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using UE5DumpUI.Models;

namespace UE5DumpUI.Services;

/// <summary>
/// Reads a coordinate block back out of a previously generated AA script (R7 — the
/// user pastes the WHOLE script and we parse only our own fenced region).
///
/// The block is read as Lua tokens and the COORDS table by Lua's own grammar for a
/// table constructor (B21). That is what makes the format tolerant of the edits a human
/// actually makes — reordered fields, a missing or extra comma, an entry split over
/// several lines, an inserted comment, unknown keys from a newer build — while a key
/// inside a label or a comment can never be read as a field, and a stray quote is
/// whatever Lua itself would refuse. Matching the text near each key (the AOBMaker
/// <c>TryParseConfig</c> mechanism this started from) let <c>label='Arena z=0 plane'</c>
/// set Z, and four rounds of checks on the characters beside each string each closed
/// one stray-quote shape and opened another.
///
/// Four defects of AOBMaker's parser are deliberately NOT inherited (each confirmed by
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

        // One issue, on the line of block offset `at`, and nothing imported: a stray quote is
        // an error, never a guess at where its field ends (the maintainer's rule, 2026-10-28).
        CoordCsvParseResult Stop(int at, string reason)
        {
            int sol = source.LastIndexOf('\n', Math.Min(at, source.Length - 1)) + 1;
            int eol = source.IndexOf('\n', at);
            result.Issues.Add(new CoordCsvIssue(LineOf(text, blockOffset + at), "",
                Trim(source[sol..(eol < 0 ? source.Length : eol)]),
                reason + " -- nothing was imported; fix it and import again"));
            result.Entries.Clear();
            return result;
        }

        var tokens = Tokenize(source, out var lexError);
        if (lexError is { } le) return Stop(le.At, le.Reason);

        // Optional: absent in blocks written before the field existed, which is why a
        // missing value leaves the user's current setting alone rather than zeroing it.
        for (int i = 0; i + 2 < tokens.Count; i++)
        {
            if (!tokens[i].Is(TokKind.Name, "Z_TOLERANCE") || !tokens[i + 1].IsSymbol("=")) continue;
            int n = i + 2;
            bool negative = tokens[n].IsSymbol("-");
            if (negative) n++;
            if (tokens[n].Kind == TokKind.Number &&
                CoordPrecision.TryParse((negative ? "-" : "") + tokens[n].Text, out var ztv))
                result.ZTolerance = CoordPrecision.Round(ztv);
            break;
        }

        int open = -1;
        for (int i = 0; i + 3 < tokens.Count && open < 0; i++)
            if (tokens[i].Is(TokKind.Name, "local") && tokens[i + 1].Is(TokKind.Name, "COORDS") &&
                tokens[i + 2].IsSymbol("=") && tokens[i + 3].IsSymbol("{"))
                open = i + 3;
        if (open < 0)
        {
            result.Issues.Add(new CoordCsvIssue(LineOf(text, blockOffset), "", "",
                "the block has no 'local COORDS = {' table"));
            return result;
        }

        var reader = new TableReader(tokens, source);
        var entries = reader.ReadCoords(open);
        if (reader.Unbalanced)
        {
            result.Issues.Add(new CoordCsvIssue(LineOf(text, blockOffset + tokens[open - 3].Start), "", "",
                "the COORDS table's braces are unbalanced"));
            return result;
        }
        if (reader.Error is { } re) return Stop(re.At, re.Reason);

        foreach (var entry in entries)
        {
            var parsed = ParseEntry(entry, LineOf(text, blockOffset + entry.Start), source, result.Issues);
            if (parsed != null) result.Entries.Add(parsed);
        }

        if (result.Entries.Count == 0 && result.Issues.Count == 0)
            result.Issues.Add(new CoordCsvIssue(0, "", "", "the coordinate list is empty"));

        return result;
    }

    private static CoordEntry? ParseEntry(LuaEntry entry, int line, string source, List<CoordCsvIssue> issues)
    {
        string? Str(string key) =>
            entry.Fields.TryGetValue(key, out var v) && v.Kind == TokKind.String ? v.Text : null;

        var e = new CoordEntry
        {
            Uid = CoordText.Normalize(Str("uid"), CoordText.MaxUidLength),   // (B7)
        };

        var rawLabel = Str("label");
        if (rawLabel == null)
        {
            issues.Add(new CoordCsvIssue(line, "label", Trim(source[entry.Start..entry.End]), "no label field"));
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

        e.Group = CoordText.Normalize(Str("group") ?? "", CoordText.MaxGroupLength);
        e.Map = CoordText.Normalize(Str("map") ?? "", 256);

        bool bad = false;
        double N(string key, bool required)
        {
            if (!entry.Fields.TryGetValue(key, out var v))
            {
                // A rotation field is optional and defaults to 0. A missing coordinate is
                // reported instead: an entry cut short (a stray brace, a comment eating the
                // rest of its line) must not land at the origin.
                if (!required) return 0;
                issues.Add(new CoordCsvIssue(line, key, "", $"no {key} field (x, y and z are required)"));
                bad = true;
                return 0;
            }
            if (v.Kind == TokKind.Number && CoordPrecision.TryParse(v.Text, out var d))
                return CoordPrecision.Round(d);

            // A key that IS present with something other than a number -- `x=oops`, a
            // quoted value, a hex literal -- is reported, not silently imported as 0.
            issues.Add(new CoordCsvIssue(line, key, source[v.Start..v.End].Trim(), "not a number"));
            bad = true;
            return 0;
        }

        e.X = N("x", true); e.Y = N("y", true); e.Z = N("z", true);
        e.Pitch = N("pitch", false); e.Yaw = N("yaw", false); e.Roll = N("roll", false);
        return bad ? null : e;
    }

    // ── Tokens ──────────────────────────────────────────────────────────

    private enum TokKind { Name, Number, String, Symbol, End }

    /// <param name="Text">A name, a numeral, a symbol, or a string's DECODED value.</param>
    /// <param name="Start">Source offset of the token's first character.</param>
    /// <param name="End">Source offset just past its last character.</param>
    private readonly record struct Tok(TokKind Kind, string Text, int Start, int End)
    {
        public bool Is(TokKind kind, string text) => Kind == kind && Text == text;
        public bool IsSymbol(string text) => Is(TokKind.Symbol, text);
    }

    // Longest first, so `...` is not read as `..` then `.`.
    private static readonly string[] s_symbols =
        { "...", "..", "==", "~=", "<=", ">=", "//", "::", "<<", ">>" };

    /// <summary>
    /// Lua 5.3's lexer, for the parts a coordinate block can hold: comments and long
    /// brackets skipped or read whole, strings decoded with Lua's escape rules, names,
    /// numerals, symbols. Any character Lua would refuse is a symbol the reader then
    /// refuses. <paramref name="error"/> is set for what the LEXER refuses: a short string
    /// that does not close on its line, an unclosed long bracket, an escape Lua does not
    /// know, a malformed number.
    /// </summary>
    private static List<Tok> Tokenize(string s, out (int At, string Reason)? error)
    {
        var tokens = new List<Tok>();
        error = null;
        int i = 0;
        while (true)
        {
            while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
            if (i >= s.Length) break;
            char c = s[i];
            int level;

            if (c == '-' && i + 1 < s.Length && s[i + 1] == '-')
            {
                if ((level = LongBracketLevel(s, i + 2)) >= 0)
                {
                    int close = LongBracketClose(s, i + 2 + level + 2, level);
                    if (close < 0) { error = (i, "unclosed bracket: a --[[ comment never closes"); return tokens; }
                    i = close + 1;
                }
                else
                {
                    int eol = s.IndexOfAny(new[] { '\r', '\n' }, i);
                    i = eol < 0 ? s.Length : eol;
                }
                continue;
            }

            if (c == '[' && (level = LongBracketLevel(s, i)) >= 0)
            {
                int bodyStart = i + level + 2;
                int close = LongBracketClose(s, bodyStart, level);
                if (close < 0) { error = (i, "unclosed bracket: a [[ string never closes"); return tokens; }
                int bodyEnd = close - level - 1;
                // Lua drops a line break right after the opening bracket.
                if (bodyStart < bodyEnd && s[bodyStart] == '\r') bodyStart++;
                if (bodyStart < bodyEnd && s[bodyStart] == '\n') bodyStart++;
                tokens.Add(new Tok(TokKind.String, s[bodyStart..bodyEnd], i, close + 1));
                i = close + 1;
                continue;
            }

            if (c == '\'' || c == '"')
            {
                var value = ReadShortString(s, i, out int next, out var strError);
                if (strError is { } se) { error = se; return tokens; }
                tokens.Add(new Tok(TokKind.String, value, i, next));
                i = next;
                continue;
            }

            if (IsIdentStart(c))
            {
                int j = i;
                while (j < s.Length && IsIdentChar(s[j])) j++;
                tokens.Add(new Tok(TokKind.Name, s[i..j], i, j));
                i = j;
                continue;
            }

            if (char.IsAsciiDigit(c) || (c == '.' && i + 1 < s.Length && char.IsAsciiDigit(s[i + 1])))
            {
                // Lua's read_numeral: digits, dots and exponent signs, then a letter or digit
                // still touching the numeral makes it malformed (`5px`, `1.2.3`).
                bool hex = c == '0' && i + 1 < s.Length && (s[i + 1] == 'x' || s[i + 1] == 'X');
                string expo = hex ? "Pp" : "Ee";
                int j = hex ? i + 2 : i;
                while (j < s.Length)
                {
                    if (expo.IndexOf(s[j]) >= 0)
                    {
                        j++;
                        if (j < s.Length && (s[j] == '+' || s[j] == '-')) j++;
                    }
                    else if (char.IsAsciiHexDigit(s[j]) || s[j] == '.') j++;
                    else break;
                }
                if (j < s.Length && (IsIdentChar(s[j]))) j++;
                var numeral = s[i..j];
                if (!IsWellFormedNumeral(numeral, hex))
                {
                    error = (i, $"a malformed number '{numeral}'");
                    return tokens;
                }
                tokens.Add(new Tok(TokKind.Number, numeral, i, j));
                i = j;
                continue;
            }

            var sym = Array.Find(s_symbols, t => string.CompareOrdinal(s, i, t, 0, t.Length) == 0) ?? c.ToString();
            tokens.Add(new Tok(TokKind.Symbol, sym, i, i + sym.Length));
            i += sym.Length;
        }
        tokens.Add(new Tok(TokKind.End, "", s.Length, s.Length));
        return tokens;
    }

    /// <summary>Whether a numeral Lua's lexer read would also convert, as Lua checks before
    /// calling it malformed. A well-formed hex numeral is still "not a number" to the entry:
    /// this format writes decimals only. (<c>1e400</c> parses, to infinity, and the entry
    /// reports that too.)</summary>
    private static bool IsWellFormedNumeral(string numeral, bool hex) => hex
        ? Regex.IsMatch(numeral, @"^0[xX](?:[0-9a-fA-F]+\.?[0-9a-fA-F]*|\.[0-9a-fA-F]+)(?:[pP][+-]?\d+)?$",
                        RegexOptions.CultureInvariant)
        : double.TryParse(numeral, NumberStyles.Float, CultureInfo.InvariantCulture, out _);

    /// <summary>
    /// One quoted string opening at <paramref name="start"/>, decoded with Lua 5.3's escape
    /// rules. Lua refuses a raw line break inside one and an escape it does not know, and so
    /// does this: either is how a stray quote or backslash shows.
    /// </summary>
    private static string ReadShortString(string s, int start, out int next, out (int At, string Reason)? error)
    {
        char quote = s[start];
        var sb = new StringBuilder();
        error = null;
        int i = start + 1;
        while (true)
        {
            if (i >= s.Length || s[i] == '\n' || s[i] == '\r')
            {
                error = (start, $"unpaired quote: a string opened with {quote} never closes on its line");
                next = i;
                return "";
            }
            char c = s[i];
            if (c == quote) { next = i + 1; return sb.ToString(); }
            if (c != '\\') { sb.Append(c); i++; continue; }

            if (i + 1 >= s.Length) { i++; continue; }   // the loop reports the unclosed string
            char n = s[i + 1];
            int at = i;
            i += 2;
            switch (n)
            {
                case 'a': sb.Append('\a'); break;
                case 'b': sb.Append('\b'); break;
                case 'f': sb.Append('\f'); break;
                case 'n': sb.Append('\n'); break;
                case 'r': sb.Append('\r'); break;
                case 't': sb.Append('\t'); break;
                case 'v': sb.Append('\v'); break;
                case '\\': sb.Append('\\'); break;
                case '"': sb.Append('"'); break;
                case '\'': sb.Append('\''); break;
                case '\n' or '\r':
                    // An escaped line break; \r\n or \n\r counts once.
                    if (i < s.Length && (s[i] == '\n' || s[i] == '\r') && s[i] != n) i++;
                    sb.Append('\n');
                    break;
                case 'z':
                    while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
                    break;
                case 'x':
                    if (i + 1 < s.Length && char.IsAsciiHexDigit(s[i]) && char.IsAsciiHexDigit(s[i + 1]))
                    {
                        sb.Append((char)Convert.ToInt32(s.Substring(i, 2), 16));
                        i += 2;
                        break;
                    }
                    error = (at, "unpaired quote: '\\x' needs two hex digits, so a backslash or quote in this string is stray");
                    next = i;
                    return "";
                case 'u':
                {
                    // Read wide: as an int, 80000000..FFFFFFFF turned negative, passed the range
                    // check and threw out of ConvertFromUtf32.
                    int close = i < s.Length && s[i] == '{' ? s.IndexOf('}', i) : -1;
                    if (close <= i + 1 || close - i - 1 > 16 ||
                        !ulong.TryParse(s.AsSpan(i + 1, close - i - 1), NumberStyles.AllowHexSpecifier,
                                        CultureInfo.InvariantCulture, out ulong cp))
                    {
                        error = (at, "unpaired quote: '\\u' needs {hex}, so a backslash or quote in this string is stray");
                        next = i;
                        return "";
                    }
                    if (cp > 0x10FFFF)
                    {
                        // Lua 5.3 -- CE's -- refuses this too ("UTF-8 value too large").
                        error = (at, "unpaired quote: a '\\u' escape above U+10FFFF, so a backslash or quote in this string is stray");
                        next = i;
                        return "";
                    }
                    if (cp is >= 0xD800 and <= 0xDFFF)
                    {
                        // Lua stores the raw bytes; a label is UTF-16 text and cannot hold one alone.
                        error = (at, $"'\\u{{{cp:X}}}' is a surrogate code point, which a label cannot hold");
                        next = i;
                        return "";
                    }
                    sb.Append(char.ConvertFromUtf32((int)cp));
                    i = close + 1;
                    break;
                }
                default:
                    if (char.IsAsciiDigit(n))
                    {
                        int v = n - '0', digits = 1;
                        while (digits < 3 && i < s.Length && char.IsAsciiDigit(s[i]))
                        {
                            v = v * 10 + (s[i++] - '0');
                            digits++;
                        }
                        if (v > 0xFF)
                        {
                            error = (at, "unpaired quote: a decimal escape above \\255, so a backslash or quote in this string is stray");
                            next = i;
                            return "";
                        }
                        sb.Append((char)v);
                        break;
                    }
                    error = (at, $"unpaired quote: '\\{n}' is not an escape Lua knows, so a backslash or quote in this string is stray");
                    next = i;
                    return "";
            }
        }
    }

    // ── The COORDS table, by Lua's grammar ──────────────────────────────

    /// <summary>A field's value: a decoded string, a numeral (a leading minus folded in), or
    /// anything else (a name, true/false/nil, a nested table) kept only to be reported.</summary>
    private readonly record struct LuaValue(TokKind Kind, string Text, int Start, int End);

    /// <summary>One entry: its <c>key = value</c> fields, and its source span for diagnostics.</summary>
    private sealed record LuaEntry(Dictionary<string, LuaValue> Fields, int Start, int End);

    /// <summary>
    /// Reads <c>local COORDS = { … }</c> as a Lua table constructor. Three levels, three
    /// rules: the table itself holds only entry tables; an entry holds only <c>key = value</c>
    /// fields (a value without a key is a stray quote's leftover, never this format); a
    /// table nested under an unknown key holds anything Lua allows. A comma may be missing
    /// before an entry or before an entry's next <c>key =</c> (documented tolerance, which
    /// Lua itself does not grant). The first fault sets <see cref="Error"/>; an unclosed
    /// table sets <see cref="Unbalanced"/>.
    /// </summary>
    private sealed class TableReader
    {
        private readonly List<Tok> _t;
        private readonly string _source;
        private int _i;

        public TableReader(List<Tok> tokens, string source) { _t = tokens; _source = source; }

        public (int At, string Reason)? Error { get; private set; }
        public bool Unbalanced { get; private set; }

        private bool Failed => Error != null || Unbalanced;
        private Tok Cur => _t[_i];
        private Tok Peek(int k = 1) => _t[Math.Min(_i + k, _t.Count - 1)];

        /// <summary>A fault at the current token -- or, when a comment lies between it and
        /// the token before, on that token's line: the comment swallowed a separator or a
        /// brace there. Only whitespace and comments separate two tokens, so `--` between
        /// them is a comment.</summary>
        private void Fail(string reason)
        {
            int at = Cur.Start;
            if (_i > 0 && CommentBetween(_t[_i - 1].End, Cur.Start)) at = _t[_i - 1].Start;
            Error = (at, reason);
        }

        private bool CommentBetween(int from, int to) =>
            to > from && _source.IndexOf("--", from, Math.Min(to, _source.Length) - from, StringComparison.Ordinal) >= 0;

        /// <summary>Lua's own limit on nested syntax (LUAI_MAXCCALLS); every level is a
        /// recursion here, and a stack overflow cannot be caught.</summary>
        private const int MaxDepth = 200;
        private int _depth;

        // The fields this format reads. One of them holding a table is a stray quote's
        // leftover: a deleted opening quote whose comment ate the line made the NEXT entry its value.
        private static readonly HashSet<string> s_knownKeys = new(StringComparer.Ordinal)
            { "uid", "label", "group", "map", "x", "y", "z", "pitch", "yaw", "roll" };

        private const string MissingBrace =
            "a '{' opens inside an entry, so the entry before it is missing its closing '}' " +
            "(or a stray quote or a comment swallowed it)";

        private bool IsKeyAssign => Cur.Kind == TokKind.Name && Peek().IsSymbol("=") && !Peek().IsSymbol("==");

        public List<LuaEntry> ReadCoords(int open)
        {
            var entries = new List<LuaEntry>();
            _i = open + 1;
            while (!Failed)
            {
                if (Cur.IsSymbol("}")) break;
                if (Cur.Kind == TokKind.End) { Unbalanced = true; return entries; }
                if (!Cur.IsSymbol("{"))
                {
                    Fail("unpaired quote or brace: text stands between the entries of the COORDS table, so a " +
                         "stray quote or brace ended an entry early");
                    return entries;
                }
                var entry = ReadEntry();
                if (Failed) return entries;
                entries.Add(entry);
                if (Cur.IsSymbol(",") || Cur.IsSymbol(";")) _i++;
                else if (!Cur.IsSymbol("}") && !Cur.IsSymbol("{") && Cur.Kind != TokKind.End)
                {
                    Fail("unpaired quote or brace: text follows an entry where a comma belongs, so a stray " +
                         "quote or brace ended it early");
                    return entries;
                }
            }
            if (Failed) return entries;

            // The rest of the block is data statements only -- the generator writes
            // `local Z_TOLERANCE = …` and `local GROUPS = { … }` there -- so it is read as such.
            // Whatever a stray brace leaves behind after closing the table early is never one:
            // counting braces missed `}}N{{`, which reads as the start of a statement.
            const string closedEarly =
                "unpaired quote or brace: the COORDS table closes before its last entry, so a stray quote or " +
                "brace ended an entry, and the table with it, early";
            int closeAt = Cur.Start;
            _i++;
            while (!Failed && Cur.Kind != TokKind.End)
            {
                if (Cur.IsSymbol(";")) { _i++; continue; }
                if (Cur.Is(TokKind.Name, "local")) _i++;
                if (!IsKeyAssign) { Error = (closeAt, closedEarly); return entries; }
                _i += 2;
                ReadValue();
            }
            if (Unbalanced) { Unbalanced = false; Error = (closeAt, closedEarly); }
            return entries;
        }

        private LuaEntry ReadEntry()
        {
            var fields = new Dictionary<string, LuaValue>(StringComparer.Ordinal);
            int start = Cur.Start;
            _i++;
            while (!Failed)
            {
                if (Cur.IsSymbol("}")) { int end = Cur.End; _i++; return new LuaEntry(fields, start, end); }
                if (Cur.Kind == TokKind.End) { Unbalanced = true; break; }

                if (IsKeyAssign)
                {
                    var key = Cur;
                    _i += 2;
                    var value = ReadValue();
                    if (Failed) break;
                    if (value.Kind == TokKind.Symbol && s_knownKeys.Contains(key.Text))
                    {
                        Error = (key.Start, $"unpaired quote: '{key.Text}' has a table for its value, so a quote " +
                                            "before it was deleted, or a comment swallowed the rest of its line");
                        break;
                    }
                    // A stray pair whose middle reads as `map=` makes a second key; Lua keeps the
                    // last and a reader might keep the first, so a repeated key is refused.
                    if (!fields.TryAdd(key.Text, value))
                    {
                        Error = (key.Start, $"the key '{key.Text}' appears twice in one entry " +
                                            "(a stray quote can turn text into a second key)");
                        break;
                    }
                }
                else if (Cur.IsSymbol("["))
                {
                    // A bracket key: valid Lua, not this format's -- read and ignored.
                    _i++;
                    ReadValue();
                    if (Failed) break;
                    if (!Cur.IsSymbol("]") || !Peek().IsSymbol("="))
                    {
                        Fail("unpaired quote: a bracket key is not closed with ']=', so a quote in a string " +
                             "was not escaped (\\' or \\\")");
                        break;
                    }
                    _i += 2;
                    ReadValue();
                    if (Failed) break;
                }
                else
                {
                    Fail(Cur.IsSymbol("{")
                        ? MissingBrace
                        : "unpaired quote: a value stands without a key, so a quote in a string before it was " +
                          "not escaped (\\' or \\\")");
                    break;
                }

                if (Cur.IsSymbol(",") || Cur.IsSymbol(";")) _i++;
                else if (Cur.Kind == TokKind.End) { Unbalanced = true; break; }
                else if (!Cur.IsSymbol("}") && !IsKeyAssign)
                {
                    Fail(Cur.IsSymbol("{")
                        ? MissingBrace
                        : "unpaired quote: text follows a value where a comma or the closing brace belongs, so a " +
                          "quote in a string was not escaped (\\' or \\\")");
                    break;
                }
            }
            return new LuaEntry(fields, start, Cur.Start);
        }

        /// <summary>A value: a string, a numeral with an optional minus, a name (true, false,
        /// nil, or anything a later check reports), or a table read by Lua's full rules.</summary>
        private LuaValue ReadValue()
        {
            var t = Cur;
            switch (t.Kind)
            {
                case TokKind.String:
                case TokKind.Number:
                case TokKind.Name:
                    _i++;
                    return new LuaValue(t.Kind, t.Text, t.Start, t.End);
            }
            if (t.IsSymbol("-") && Peek().Kind == TokKind.Number)
            {
                var num = Peek();
                _i += 2;
                return new LuaValue(TokKind.Number, "-" + num.Text, t.Start, num.End);
            }
            if (t.IsSymbol("{"))
            {
                ReadAnyTable();
                return new LuaValue(TokKind.Symbol, "{", t.Start, _t[Math.Max(0, _i - 1)].End);
            }
            if (t.Kind == TokKind.End) Unbalanced = true;
            else Fail("unpaired quote: no value follows '=', so a quote in a string was not escaped (\\' or \\\")");
            return default;
        }

        private void ReadAnyTable()
        {
            if (_depth >= MaxDepth)
            {
                Fail($"a table nests deeper than Lua allows ({MaxDepth} levels)");
                return;
            }
            _depth++;
            ReadAnyTableBody();
            _depth--;
        }

        private void ReadAnyTableBody()
        {
            _i++;
            while (!Failed)
            {
                if (Cur.IsSymbol("}")) { _i++; return; }
                if (Cur.Kind == TokKind.End) { Unbalanced = true; return; }
                if (Cur.IsSymbol("["))
                {
                    _i++;
                    ReadValue();
                    if (Failed) return;
                    if (!Cur.IsSymbol("]") || !Peek().IsSymbol("="))
                    {
                        Fail("unpaired quote: a bracket key is not closed with ']=', so a quote in a string " +
                             "was not escaped (\\' or \\\")");
                        return;
                    }
                    _i += 2;
                }
                else if (IsKeyAssign) _i += 2;
                ReadValue();
                if (Failed) return;
                if (Cur.IsSymbol(",") || Cur.IsSymbol(";")) _i++;
                else if (Cur.Kind == TokKind.End) { Unbalanced = true; return; }
                else if (!Cur.IsSymbol("}"))
                {
                    Fail("unpaired quote: text follows a value where a comma or the closing brace belongs, so a " +
                         "quote in a string was not escaped (\\' or \\\")");
                    return;
                }
            }
        }
    }

    // Lua names are ASCII; char.IsLetter would let `タワー=` pass as a key.
    private static bool IsIdentStart(char c) => c is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or '_';
    private static bool IsIdentChar(char c) => IsIdentStart(c) || c is >= '0' and <= '9';

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

    /// <summary>Reverse <see cref="CeLuaHygiene.EscapeLuaString"/>, including the
    /// decimal escapes it uses to break closing long brackets.</summary>
    internal static string Unescape(string s)
    {
        var sb = new StringBuilder(s.Length);
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
