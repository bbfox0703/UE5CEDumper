using System;
using System.Linq;
using System.Reflection;
using Avalonia.Controls;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// The three Avalonia facts the <see cref="UE5DumpUI.Helpers.NumericInput"/> fix rests on.
/// [SNAPINTERVAL-2026-08-20]
///
/// <para>Each was measured against Avalonia 12.1.1 rather than assumed, and each is the kind of
/// thing a version bump can change silently. If one moves, the façades stop being the right shape —
/// so this fails here, on the next build, instead of turning back into a raw exception in a
/// validation line under the control.</para>
/// </summary>
public class NumericUpDownSurfaceTests
{
    /// <summary>
    /// The whole reason a façade is needed: an emptied box has nowhere to put "no number" except
    /// null, and null cannot cross a compiled binding into an <c>int</c>.
    /// </summary>
    [Fact]
    public void Value_IsNullableDecimal()
        => Assert.Equal(typeof(decimal?), NumericUpDown.ValueProperty.PropertyType);

    /// <summary>
    /// And there is no way to opt out of the null at the control. Were there an
    /// <c>IsNullable</c>-style switch, setting it would be the smaller fix — worth re-checking if
    /// this ever starts failing, because then it exists.
    /// </summary>
    [Fact]
    public void Control_OffersNoWayToSuppressTheNull()
    {
        var suppressors = typeof(NumericUpDown)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.PropertyType == typeof(bool))
            .Select(p => p.Name)
            .Where(n => n.Contains("Null", StringComparison.OrdinalIgnoreCase)
                     || n.Contains("Empty", StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.True(suppressors.Count == 0,
            "NumericUpDown now exposes " + string.Join(", ", suppressors)
            + " — re-read Helpers/NumericInput.cs; a control-level switch may be the smaller fix.");
    }

    /// <summary>
    /// Clamping is ours to do. With <c>ClipValueToMinMax</c> off — the default — a below-minimum
    /// commit leaves <c>Value</c> silently at its previous number while the text box still shows
    /// what was typed, so the display stops describing what is in force.
    /// </summary>
    [Fact]
    public void ClipValueToMinMax_DefaultsToFalse()
        => Assert.False(new NumericUpDown().ClipValueToMinMax);
}
