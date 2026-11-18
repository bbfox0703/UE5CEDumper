namespace UE5DumpUI.Models;

/// <summary>
/// How a fractional float/double is reduced to the integer the game DISPLAYS,
/// before any numeric compare — the C# mirror of the DLL's
/// <c>Radar::RoundMode</c> (dll/src/Radar.h). Games store HP/MP/progress as
/// floats (513.36, 99.6) but show a rounded integer; the user searches and
/// compares against what they SEE.
///
/// Applied to Float/Double values in EVERY numeric predicate (Exact / Bigger /
/// Smaller / Between vs a WHOLE target, plus the temporal Changed / Unchanged /
/// Increased / Decreased — reduce both sides). A FRACTIONAL target keeps an
/// exact-literal compare (precise intent — no reduce). Integer fields are
/// reduce-invariant (an integer reduces to itself); the mode only reaches them
/// when a FRACTIONAL target/bound is coerced to an integer.
///
/// Persisted per panel (Value Search / Snapshot / SPC) as the wire string
/// "Round" / "Trunc" / "Ceil"; default <see cref="Round"/> (half-away-from-zero).
/// Pure value enum (AOT-safe).
/// </summary>
public enum FloatRoundMode
{
    /// <summary>Half-away-from-zero (Math.Round AwayFromZero) — 11.6 → 12, 11.4 → 11.</summary>
    Round = 0,
    /// <summary>Toward zero (Math.Truncate) — 11.6 → 11, -11.6 → -11.</summary>
    Trunc,
    /// <summary>Toward +infinity (Math.Ceiling) — 11.1 → 12, 99.6 → 100.</summary>
    Ceil,
}

/// <summary>Wire-string ↔ <see cref="FloatRoundMode"/> mapping, shared by the
/// pipe layer (Value Search), the snapshot/SPC engines, and the persisted UI
/// options. Kept here (Models) so every layer agrees on the spelling.</summary>
public static class FloatRoundModeWire
{
    public const string Round = "Round";
    public const string Trunc = "Trunc";
    public const string Ceil  = "Ceil";

    /// <summary>Wire string for a mode (inverse of <see cref="Parse"/>).</summary>
    public static string ToWire(FloatRoundMode mode) => mode switch
    {
        FloatRoundMode.Trunc => Trunc,
        FloatRoundMode.Ceil  => Ceil,
        _                    => Round,
    };

    /// <summary>Parse a wire string; unknown / null falls back to
    /// <see cref="FloatRoundMode.Round"/> (backward compatible).</summary>
    public static FloatRoundMode Parse(string? s) => s switch
    {
        Trunc => FloatRoundMode.Trunc,
        Ceil  => FloatRoundMode.Ceil,
        _     => FloatRoundMode.Round,
    };
}
