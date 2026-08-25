using System;

namespace UE5DumpUI.Helpers;

/// <summary>
/// Coercion for a <c>NumericUpDown</c> bound to a whole-number setting.
/// [SNAPINTERVAL-2026-08-20]
///
/// <para><b>Why this exists.</b> <c>NumericUpDown.Value</c> is <c>decimal?</c> — measured, not
/// assumed: <c>ValueProperty.PropertyType</c> is <c>System.Nullable`1[System.Decimal]</c> in
/// Avalonia 12.1.1, and the control has no option to suppress the null (there is no
/// <c>IsNullable</c>/<c>AllowNull</c> on its surface). Clearing the text box therefore drives
/// <c>Value</c> to <c>null</c> on every one of them, unconditionally.</para>
///
/// <para>Binding that <c>decimal?</c> straight at a non-nullable <c>int</c> view-model property is
/// what produced the reported
/// <c>System.InvalidCastException: Could not convert '(null)' (null) to System.Int32</c>
/// in a validation line under the control. ⚠ It is specific to <b>compiled</b> bindings, which this
/// app turns on globally (<c>AvaloniaUseCompiledBindingsByDefault</c>). A reflection
/// <c>new Binding(...)</c> over the same property pair swallows the null silently and recovers on
/// the next keystroke — so a probe built on reflection bindings will report "no defect here" and be
/// wrong. Do not use one to re-check this.</para>
///
/// <para>Binding a <c>decimal?</c> property instead means no conversion happens at all, which is why
/// the fix is a nullable façade rather than a converter: two other routes were tried and
/// <b>measured to fail</b> before this one was chosen —
/// <c>NumericUpDown.TextConverter</c> never sees the empty-text path and additionally breaks
/// ordinary in-range commits, and a binding <c>Converter</c> returning
/// <c>BindingOperations.DoNothing</c> could not be shown to work on the compiled path.</para>
/// </summary>
public static class NumericInput
{
    /// <summary>
    /// Fold a <c>NumericUpDown.Value</c> into the whole number actually in use.
    ///
    /// <para><c>null</c> means the user emptied the box — mid-edit, not a request to change
    /// anything — so <paramref name="current"/> is returned unchanged and the control repaints the
    /// value still in effect. That matters beyond tidiness: the defect report noted the auto-capture
    /// loop kept running at its floor while the field showed blank, so the screen stopped telling
    /// the truth about what was in force.</para>
    ///
    /// <para>An out-of-range number is <b>clamped</b>, never rejected. The control's own
    /// <c>ClipValueToMinMax</c> defaults to <c>false</c> (measured), under which a below-minimum
    /// commit leaves <c>Value</c> silently at its previous number while the text box still shows what
    /// was typed — the same "display disagrees with reality" failure in a quieter form.</para>
    /// </summary>
    /// <param name="value">The control's raw value; <c>null</c> when the box is empty.</param>
    /// <param name="current">The value in force, returned as-is for an empty box.</param>
    /// <param name="min">Inclusive floor. Must not exceed <paramref name="max"/>.</param>
    /// <param name="max">Inclusive ceiling.</param>
    public static int Coerce(decimal? value, int current, int min, int max)
    {
        if (min > max)
            throw new ArgumentOutOfRangeException(nameof(min), $"min ({min}) exceeds max ({max}).");

        if (value is not { } d) return Math.Clamp(current, min, max);

        // Clamp in DECIMAL, before the cast. Going to int first would overflow on a value
        // outside int's range, and a NumericUpDown will happily hold one — Maximum only
        // constrains it when ClipValueToMinMax is on, which it is not by default.
        if (d <= min) return min;
        if (d >= max) return max;

        // Whole-number settings only. Round rather than truncate so typing "60.7" lands on the
        // nearer value instead of quietly dropping back to the floor.
        return (int)decimal.Round(d, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// The null rule on its own, for a control whose range is already expressed elsewhere.
    ///
    /// <para><c>null</c> means the box is empty — mid-edit — so the value in force is returned and
    /// nothing changes. That is the entire crash: without it a compiled binding tries to put
    /// <c>null</c> into an <c>int</c>/<c>double</c> and the user gets an <c>InvalidCastException</c>
    /// in a validation line.</para>
    ///
    /// <para>⚠ This deliberately does <b>not</b> clamp. Most of the controls that use it declare a
    /// <c>Minimum</c>/<c>Maximum</c> the control itself enforces on the text-commit path only when
    /// <c>ClipValueToMinMax</c> is set, and several declare no range at all (the Teleport
    /// coordinate fields). Adding a clamp here would invent bounds those inputs do not have. Where a
    /// real range exists in the view model, use <see cref="Coerce"/> instead and state it.</para>
    /// </summary>
    public static int KeepCurrentIfEmpty(decimal? value, int current)
    {
        if (value is not { } d) return current;
        // A NumericUpDown with no Maximum holds anything a decimal can, so the cast needs a guard.
        if (d <= int.MinValue) return int.MinValue;
        if (d >= int.MaxValue) return int.MaxValue;
        return (int)decimal.Round(d, MidpointRounding.AwayFromZero);
    }

    /// <inheritdoc cref="KeepCurrentIfEmpty(decimal?, int)"/>
    public static double KeepCurrentIfEmpty(decimal? value, double current)
        => value is { } d ? (double)d : current;

    /// <summary>
    /// Widen a <c>double</c> setting into what a <c>NumericUpDown</c> can hold, <b>without
    /// throwing</b>.
    ///
    /// <para>A plain <c>(decimal)someDouble</c> raises <see cref="OverflowException"/> for NaN,
    /// either infinity, and any magnitude above ~7.9e28. The doubles behind the Teleport
    /// coordinate boxes are read out of the running game, so all three are reachable — and the
    /// throw would land in a property getter during rendering, which is a far worse failure than
    /// the blank field this whole fix is about. NaN becomes an empty box, since "not a number" is
    /// exactly what an empty box means; the infinities saturate.</para>
    ///
    /// <para>Precision is unchanged by this: the control's value has always been
    /// <c>decimal</c>, so the double→decimal narrowing to ~15 significant digits already happened
    /// inside the binding. This only decides what to do at the edges it used to throw on.</para>
    /// </summary>
    public static decimal? ToControlValue(double v)
    {
        if (double.IsNaN(v)) return null;
        if (v <= (double)decimal.MinValue) return decimal.MinValue;
        if (v >= (double)decimal.MaxValue) return decimal.MaxValue;
        return (decimal)v;
    }

}
