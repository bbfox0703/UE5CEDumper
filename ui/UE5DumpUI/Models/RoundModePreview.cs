using System.Globalization;

namespace UE5DumpUI.Models;

/// <summary>
/// Pure formatter that turns the numeric bound(s) of a Between / ordering /
/// absolute predicate into a LIVE "what will this actually match" preview
/// string, under the active <see cref="FloatRoundMode"/> (build 1702).
///
/// The rounding-mode switch (build 1672) reduces a fractional float/double to the
/// integer the game DISPLAYS before compare, and coerces a fractional Between /
/// absolute bound to an integer for INTEGER fields. The picker used to show only a
/// static example ("Between 10.9–11.1 → 11~11"); this helper computes the real
/// outcome for the values the user actually typed, e.g. Between 11.5~13.2 under
/// Round → integer fields match <c>12~13</c> while float fields keep <c>11.5~13.2</c>.
///
/// <para><b>Two interpretations</b> (mirroring <c>SnapshotNumeric.BetweenMatch</c>):
/// an INTEGER field coerces each bound via the mode (<c>CoerceIntTarget</c>) → the
/// <i>int range</i>; a FLOAT field with a fractional bound compares the raw value
/// literally → the <i>float range</i> (the entered bounds, unchanged). When EVERY
/// bound is a whole number the two interpretations coincide (a float value is just
/// reduced into the same integer window), so the split collapses to one range.</para>
///
/// Models-level (no Services dependency) so the per-row group models
/// (<see cref="GroupSlotInput"/>) and the SPC pick VM can recompute it reactively
/// as the user types. The reduce/coerce math is the single C# source of truth,
/// shared with <c>SnapshotNumeric</c> (which delegates here) and kept in lockstep
/// with the DLL's <c>Radar::ReduceRounded</c>. Pure / AOT-safe.
/// </summary>
public static class RoundModePreview
{
    /// <summary>Which field interpretation(s) the surface can match, so the preview
    /// only shows the range(s) that apply. A type-specific Value Search scan narrows
    /// to one; mixed numeric / group / SPC corpus scans show both.</summary>
    public enum Scope
    {
        /// <summary>Mixed numeric — both integer and float fields can match.</summary>
        Both,
        /// <summary>Integer-typed scan — only the coerced integer range applies.</summary>
        IntOnly,
        /// <summary>Float/double-typed scan — only the literal float range applies.</summary>
        FloatOnly,
    }

    // ---- pure reduce/coerce math (the canonical C# copy; SnapshotNumeric delegates here) ----

    /// <summary>Reduce a value to the integer the game DISPLAYS, per the mode. Round =
    /// half-away-from-zero (matches the live DLL + CE), Trunc = toward zero, Ceil =
    /// toward +inf. The C# mirror of the DLL's <c>Radar::ReduceRounded</c>.</summary>
    public static double Reduce(double x, FloatRoundMode mode) => mode switch
    {
        FloatRoundMode.Trunc => System.Math.Truncate(x),
        FloatRoundMode.Ceil  => System.Math.Ceiling(x),
        _                    => System.Math.Round(x, System.MidpointRounding.AwayFromZero),
    };

    /// <summary>True when <paramref name="x"/> is a whole number (no fractional part).</summary>
    public static bool IsWhole(double x) => x == System.Math.Floor(x);

    /// <summary>The integer a fractional <paramref name="target"/> coerces to for an
    /// INTEGER-typed field, per the mode (Between 10.9 → Round 11 / Trunc 10 / Ceil 11).
    /// A whole target is returned unchanged.</summary>
    public static double CoerceIntTarget(double target, FloatRoundMode mode) =>
        IsWhole(target) ? target : Reduce(target, mode);

    // ---- preview formatting ----

    private const string IntLabel   = "int ";
    private const string FloatLabel = "float ";
    private const string Arrow      = "→ ";
    private const string RangeSep   = "~";
    private const string SplitSep   = " · ";

    /// <summary>[W2-BETWEEN-PREVIEW] What a bound may look like, per the parser that will actually read it. The preview
    /// must refuse exactly what that parser refuses, or it previews a number nobody will match: <c>NumberStyles.Any</c>
    /// read an FVector's "1,2,3" as 123 and accepted "1,000" / "(5)" / "5-". Value Search bounds (single and group) go
    /// to the DLL -- <c>std::stod</c> / <c>std::stoll</c>, the whole string consumed: plain decimal, sign, exponent; no
    /// grouping, parentheses or trailing sign. A 0x-hex integer the DLL takes simply gets no preview, which misleads
    /// nobody.</summary>
    public const NumberStyles DllBoundStyles = NumberStyles.Float;

    /// <summary>SPC's absolute window is never read by the DLL: <c>SpcQueryViewModel</c>'s own <c>Lo()</c> / <c>Hi()</c>
    /// parse it with <c>NumberStyles.Any</c>, so its preview keeps that grammar. (The snapshot group matcher, which
    /// shares <see cref="GroupSlotInput"/>'s preview, parses Float + thousands; the DLL grammar there can only HIDE a
    /// preview for a grouped bound, never fabricate one.)</summary>
    public const NumberStyles SpcBoundStyles = NumberStyles.Any;

    private static bool TryParse(string? s, NumberStyles styles, out double v) =>
        double.TryParse((s ?? "").Trim(), styles, CultureInfo.InvariantCulture, out v);

    private static string Int(double x)   => x.ToString("0",        CultureInfo.InvariantCulture);
    private static string Float(double x) => x.ToString("0.######", CultureInfo.InvariantCulture);

    /// <summary>Live preview for a Between predicate with the two entered bounds.
    /// Empty when either bound is missing / unparseable (caller hides the label).</summary>
    public static string Between(string? loStr, string? hiStr, FloatRoundMode mode, Scope scope,
                                 NumberStyles styles = DllBoundStyles)
    {
        if (!TryParse(loStr, styles, out double lo) || !TryParse(hiStr, styles, out double hi)) return "";
        double a = System.Math.Min(lo, hi), b = System.Math.Max(lo, hi);

        // float range: the literal entered bounds (a float field is compared as-is when
        // a bound is fractional; when both are whole it reduces into the same window).
        string floatRange = $"{Float(a)}{RangeSep}{Float(b)}";
        // int range: each bound coerced to the displayed integer via the mode.
        string intRange   = $"{Int(CoerceIntTarget(a, mode))}{RangeSep}{Int(CoerceIntTarget(b, mode))}";

        bool bothWhole = IsWhole(a) && IsWhole(b);
        return Compose(intRange, floatRange, bothWhole, scope);
    }

    /// <summary>Live preview for an SPC absolute window (<c>Exact</c> / <c>Between</c> /
    /// <c>≥</c> / <c>≤</c>) — the kind tokens used by <c>SpcSnapshotPick.AbsKindOptions</c>.
    /// Empty for "(any value)" or unparseable bounds.</summary>
    public static string SpcAbsolute(string? kind, string? loStr, string? hiStr,
                                     FloatRoundMode mode, Scope scope) => kind switch
    {
        "Exact"   => Point("",  loStr, mode, scope, SpcBoundStyles),
        "Between" => Between(loStr, hiStr, mode, scope, SpcBoundStyles),
        "≥"       => Point("≥", loStr, mode, scope, SpcBoundStyles),
        "≤"       => Point("≤", hiStr, mode, scope, SpcBoundStyles),
        _         => "",
    };

    /// <summary>Single-bound preview (Exact / ≥ / ≤) with an optional comparison prefix.</summary>
    private static string Point(string prefix, string? valStr, FloatRoundMode mode, Scope scope, NumberStyles styles)
    {
        if (!TryParse(valStr, styles, out double v)) return "";
        string floatVal = $"{prefix}{Float(v)}";
        string intVal   = $"{prefix}{Int(CoerceIntTarget(v, mode))}";
        return Compose(intVal, floatVal, IsWhole(v), scope);
    }

    /// <summary>Assemble the final "→ …" string from the int / float renderings.
    /// When both bounds are whole the interpretations coincide → one range; a
    /// type-scoped surface shows only its own interpretation.</summary>
    private static string Compose(string intText, string floatText, bool whole, Scope scope) => scope switch
    {
        Scope.IntOnly   => $"{Arrow}{intText}",
        Scope.FloatOnly => $"{Arrow}{floatText}",
        _               => whole
            ? $"{Arrow}{intText}"
            : $"{Arrow}{IntLabel}{intText}{SplitSep}{FloatLabel}{floatText}",
    };
}
