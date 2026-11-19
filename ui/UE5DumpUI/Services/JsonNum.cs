using System;
using System.Text.Json.Nodes;

namespace UE5DumpUI.Services;

/// <summary>
/// Numeric reads off a pipe <see cref="JsonNode"/> that <b>cannot throw</b>.
///
/// <para><b>Why this exists.</b> <c>JsonNode.GetValue&lt;long&gt;()</c> throws
/// <c>InvalidOperationException</c> whenever the JSON number does not fit an
/// <c>Int64</c> — and it reports the SAME message,
/// <i>"An element of type 'Number' cannot be converted to a 'System.Int64'"</i>,
/// for two very different causes: a fractional value, and a value merely out of
/// range. The ambiguity misleads: a DLL sentinel of <c>UINT64_MAX</c>
/// (18446744073709551615, "game-thread hook never fired") is merely out of range,
/// yet a strict read throws on it, blanking a whole Diagnostics card with a message
/// that points at decimals.</para>
///
/// <para><b>The rule these encode:</b> a diagnostics/telemetry read must degrade,
/// never throw. One unexpected field is worth a wrong number in one cell — it is
/// not worth blanking the panel the user opened to debug something else. Use
/// these for any payload whose numeric ranges you do not fully control; keep the
/// strict <c>GetValue&lt;T&gt;</c> where a bad value genuinely must fail loudly.</para>
/// </summary>
internal static class JsonNum
{
    /// <summary>Read as <see cref="long"/>. Out-of-range values saturate rather than
    /// throw (an unsigned 64-bit sentinel becomes <see cref="long.MaxValue"/>);
    /// fractional values truncate; anything else returns <paramref name="fallback"/>.</summary>
    public static long L(JsonNode? n, long fallback = 0)
    {
        if (n is not JsonValue v) return fallback;
        if (v.TryGetValue<long>(out var l)) return l;
        if (v.TryGetValue<ulong>(out var ul)) return ul > long.MaxValue ? long.MaxValue : (long)ul;
        if (v.TryGetValue<double>(out var d)) return Saturate(d);
        return fallback;
    }

    /// <summary>Read as <see cref="int"/>, saturating at the <c>Int32</c> bounds.</summary>
    public static int I(JsonNode? n, int fallback = 0)
    {
        if (n is not JsonValue v) return fallback;
        if (v.TryGetValue<int>(out var i)) return i;
        long l = L(n, fallback);
        return l > int.MaxValue ? int.MaxValue : l < int.MinValue ? int.MinValue : (int)l;
    }

    /// <summary>Read as <see cref="double"/>. Non-finite results collapse to
    /// <paramref name="fallback"/> so a NaN can never reach a format string.</summary>
    public static double D(JsonNode? n, double fallback = 0.0)
    {
        if (n is not JsonValue v) return fallback;
        if (v.TryGetValue<double>(out var d)) return double.IsFinite(d) ? d : fallback;
        if (v.TryGetValue<long>(out var l)) return l;
        if (v.TryGetValue<ulong>(out var ul)) return ul;
        return fallback;
    }

    /// <summary>Read as <see cref="bool"/>; anything unreadable is
    /// <paramref name="fallback"/>.</summary>
    public static bool B(JsonNode? n, bool fallback = false)
        => n is JsonValue v && v.TryGetValue<bool>(out var b) ? b : fallback;

    private static long Saturate(double d)
    {
        if (double.IsNaN(d)) return 0;
        if (d >= long.MaxValue) return long.MaxValue;
        if (d <= long.MinValue) return long.MinValue;
        return (long)d;
    }
}
