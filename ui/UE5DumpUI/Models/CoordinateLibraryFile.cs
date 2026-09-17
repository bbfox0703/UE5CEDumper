using System;
using System.Globalization;
using System.Text.Json.Serialization;

namespace UE5DumpUI.Models;

/// <summary>
/// Persisted Teleport coordinate library for ONE game, stored as
/// %LOCALAPPDATA%\UE5CEDumper\teleport-coords.{module}.json.
///
/// Keyed by the game's EXE MODULE NAME, not its PE hash — deliberately unlike
/// <see cref="BookmarkFile"/>. Bookmarks store offsets and *should* die when the
/// game is patched; a hand-curated 4 000-entry coordinate list must not. The tool
/// makes no claim about whether a coordinate is still valid after a patch (that is
/// out of scope) — this key choice is only about not losing the user's data.
///
/// See docs/teleport-coord-library-spec.md §3 D1.
/// </summary>
public sealed class CoordinateLibraryFile
{
    /// <summary>Schema version for future migrations (v1 = initial).</summary>
    public int Version { get; set; } = 1;

    /// <summary>Module name the file was written for (informational; the file NAME
    /// is the real key).</summary>
    public string Module { get; set; } = "";

    public List<CoordEntry> Entries { get; set; } = new();

    /// <summary>
    /// Extra height (uu) added to Z at TELEPORT time — never baked into a stored
    /// entry. A saved position sits exactly on the floor it was captured on, and
    /// landing on that exact plane can drop the character through it, so a few units
    /// of clearance is the difference between arriving and falling.
    ///
    /// Library-wide rather than per-entry: it is a property of how the GAME resolves
    /// a teleport, not of any one place. That is also why it is absent from the CSV
    /// (a per-row column would imply per-row meaning) but present in the Lua export,
    /// whose picker performs the teleport itself and therefore needs the value.
    ///
    /// 0 = off, which is the default: silently moving someone's saved coordinates
    /// would be worse than the problem it solves.
    /// </summary>
    public double ZTolerance { get; set; }
}

/// <summary>
/// One saved position. All six numeric fields are rounded to
/// <see cref="CoordPrecision.Decimals"/> at CAPTURE time so every wire format
/// (JSON / Lua / CSV) round-trips bit-exactly — see <see cref="CoordPrecision"/>.
/// </summary>
public sealed class CoordEntry
{
    /// <summary>
    /// Opaque stable identity, generated on Add and preserved by every codec.
    /// Without it a merge-import has to fall back to coordinate equality, and a
    /// spreadsheet round trip rewrites the text — so nothing matches and a merge
    /// duplicates the whole library.
    ///
    /// MUST NOT be renamed to "id": AOBMaker's <c>CtIdRenumberService</c> classifies
    /// a script as an "ID-check script" on <c>((?&lt;![A-Za-z0-9_])id\s*=\s*)(\d+)</c>
    /// matching alone and rewrites those literals, so a user running "renumber CT IDs"
    /// on a table holding our generated script would silently renumber every entry.
    /// The lookbehind cannot match "uid" because of the leading 'u'.
    /// </summary>
    public string Uid { get; set; } = "";

    public string Label { get; set; } = "";
    public string Group { get; set; } = "";

    /// <summary>UWorld name captured at save time. Compared
    /// <see cref="StringComparison.OrdinalIgnoreCase"/> everywhere — CSV import makes
    /// this user-authored ("map01" vs "Map01") whether we like it or not.</summary>
    public string Map { get; set; } = "";

    public double X { get; set; }
    public double Y { get; set; }
    public double Z { get; set; }
    public double Pitch { get; set; }
    public double Yaw { get; set; }
    public double Roll { get; set; }

    public CoordEntry Clone() => new()
    {
        Uid = Uid, Label = Label, Group = Group, Map = Map,
        X = X, Y = Y, Z = Z, Pitch = Pitch, Yaw = Yaw, Roll = Roll,
    };

    /// <summary>True when both entries describe the same place with the same naming.
    /// Used as the merge fallback when <see cref="Uid"/> is absent.</summary>
    public bool SameIdentityAs(CoordEntry other) =>
        string.Equals(Label, other.Label, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(Group, other.Group, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(Map, other.Map, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// The ONE canonical precision rule for coordinate text, shared by the JSON store
/// and every export/import codec.
///
/// Coordinates are doubles, and a UE4 float widened to double is the normal case
/// (67162.3984375). Two properties are wanted at once and they look contradictory:
///
///  * <b>bit-exact round trip</b> — both the Lua fence and the CSV are round-trip
///    SOURCES, so a lossy format silently degrades the library on every cycle. The
///    repo's existing helpers are all lossy: "0.0###" (TeleportScriptGenerator) and
///    "0.000" (TeleportViewModel copy-as-BugItGo). They must not be reused here.
///  * <b>clean, diffable text</b> — a bare round-trip format on an UNROUNDED double
///    emits 67162.39800000001, which an author "cleans" by hand, so a 4 000-row diff
///    fills with semantically identical rows.
///
/// <see cref="Round"/> at capture resolves both: the stored double becomes the nearest
/// double to a 3-decimal literal, so the shortest round-trippable text IS "67162.398"
/// — clean AND bit-exact, and idempotent on the second cycle.
///
/// 0.001 uu is 10 µm; nothing in the teleport path is sensitive to it, and rounding
/// also denoises rotator values like 1.4210855e-14 to a clean 0.
/// </summary>
public static class CoordPrecision
{
    /// <summary>Decimal places kept. See the class remarks for why 3.</summary>
    public const int Decimals = 3;

    /// <summary>Round a captured coordinate and normalise negative zero (which would
    /// otherwise render as "-0" and diff against a plain "0").</summary>
    public static double Round(double v)
    {
        if (double.IsNaN(v) || double.IsInfinity(v)) return 0;
        var r = Math.Round(v, Decimals, MidpointRounding.AwayFromZero);
        return r == 0 ? 0 : r;   // collapses -0.0 to +0.0
    }

    /// <summary>Format for any wire format. "R" is shortest-round-trippable on
    /// .NET Core 3.0+; on a <see cref="Round"/>ed value it yields clean short text.</summary>
    public static string Text(double v) =>
        v.ToString("R", CultureInfo.InvariantCulture);

    /// <summary>Parse a coordinate from any wire format. AllowExponent is REQUIRED:
    /// "R" goes scientific below 1e-5, so a legitimately-exported small value comes
    /// back as "1E-05".
    ///
    /// <para><b>AllowThousands is deliberately NOT set</b> (audit #4 B21). With it, a European
    /// decimal comma parsed silently and WRONGLY: <c>"67162,398"</c> became <b>67162398</b> — a
    /// coordinate a thousand times out, accepted with no issue raised, because the
    /// decimal-comma repair only ran for <c>;</c>-delimited files. Dropping the flag also
    /// rejects Excel's grouped <c>"67,162.398"</c>, and that is the accepted trade: a rejected
    /// row is VISIBLE in the import preview and the user can fix the file, whereas a silently
    /// mis-scaled coordinate teleports them into the void with nothing to look at. Grouped
    /// thousands are in any case unusual in a coordinate export — ours never emits them
    /// (<see cref="Text"/> uses invariant "R").</para></summary>
    public static bool TryParse(string? s, out double value) =>
        double.TryParse((s ?? "").Trim(),
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture, out value)
        // [A3-COORD-NONFINITE] TryParse ACCEPTS "NaN", "Infinity" and an overflow like "1e400", and Round maps them to 0
        // without a word -- a teleport to the world origin. A non-finite coordinate is a rejected, VISIBLE row. Round is
        // left alone: it is also the pose-capture and writer path, where a NaN would reach generated Lua as a nil global.
        && double.IsFinite(value);
}

/// <summary>
/// Characters that cannot be escaped into a generated CE Lua script and so must be
/// rejected at EVERY ingress (UI edit, Lua import, CSV import). This is a property of
/// the <see cref="CoordEntry"/> model, not of any one wire format.
///
/// Note what is NOT here: the repo's convention is escape-not-block. Quotes,
/// backslashes, angle brackets, ampersands, CJK and closing long brackets
/// (<c>]]</c> / <c>]==]</c>) are all handled by the shared escapers and must not be
/// stripped — silently mutating a user's label is worse than the hazard.
/// See docs/teleport-coord-library-spec.md §4.5.
/// </summary>
public static class CoordText
{
    public const int MaxLabelLength = 64;
    public const int MaxGroupLength = 32;
    /// <summary>Cap for the entry uid. Generous next to the 6-char minted value and the
    /// 12-char pathological fallback, because an imported uid only has to be a stable
    /// token — but it is a text field read from a user-editable file, so it goes through
    /// the same Normalize gate as every other one. It used to be the ONE field that
    /// skipped it entirely. (B7)</summary>
    public const int MaxUidLength = 32;

    /// <summary>True when <paramref name="s"/> contains a character that cannot survive
    /// the emit path: NUL (luaL_dostring measures with strlen, so it truncates the
    /// chunk), CR/LF (a raw newline inside a Lua single-quoted literal is a compile
    /// error — reachable from Excel's Alt+Enter through a quoted CSV field), or any
    /// other C0 control (illegal in XML 1.0 with no entity form).</summary>
    public static bool HasBlockedChars(string? s)
    {
        if (string.IsNullOrEmpty(s)) return false;
        foreach (var c in s)
            if (c < 0x20 || c == 0x7F) return true;
        return false;
    }

    /// <summary>Remove blocked characters (import path — always paired with a
    /// per-row diagnostic so the mutation is never silent).</summary>
    public static string StripBlockedChars(string? s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        Span<char> buf = s!.Length <= 256 ? stackalloc char[s.Length] : new char[s.Length];
        int n = 0;
        foreach (var c in s)
            if (c >= 0x20 && c != 0x7F) buf[n++] = c;
        return new string(buf[..n]);
    }

    /// <summary>Normalise a user-entered label/group: strip blocked chars, trim, cap.</summary>
    public static string Normalize(string? s, int maxLength)
    {
        var t = StripBlockedChars(s).Trim();
        return t.Length > maxLength ? t[..maxLength] : t;
    }
}

/// <summary>
/// Source-generated JSON context (AOT/trimming — reflection JSON is disabled).
///
/// Deliberately does NOT set <c>DefaultIgnoreCondition = WhenWritingDefault</c>
/// (the BookmarkFile / UiOptionsSettings dialect): a legitimately-saved coordinate of
/// exactly 0.0 — or a Pitch/Yaw/Roll of 0, which is extremely common — would be
/// omitted from the JSON and reload as 0 by ACCIDENT rather than by record.
/// </summary>
[JsonSerializable(typeof(CoordinateLibraryFile))]
[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal partial class CoordinateLibraryJsonContext : JsonSerializerContext
{
}
