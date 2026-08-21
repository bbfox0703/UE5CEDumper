using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UE5DumpUI.Helpers;
using UE5DumpUI.Services;
using UE5DumpUI.ViewModels;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// [SNAPINTERVAL-2026-08-20] — a below-minimum or emptied NumericUpDown must not reach a
/// non-nullable binding.
///
/// <para>The premise these rest on was <b>measured</b>, not assumed:
/// <c>NumericUpDown.ValueProperty.PropertyType</c> is <c>decimal?</c> in Avalonia 12.1.1, the
/// control exposes nothing to suppress the null, and <c>ClipValueToMinMax</c> defaults to
/// <c>false</c>. <see cref="NumericUpDownSurfaceTests"/> pins all three so an Avalonia upgrade that
/// changes any of them fails here rather than in front of a user.</para>
/// </summary>
public class NumericInputCoercionTests : IDisposable
{
    private readonly string _tempDir;
    private readonly SnapshotStore _store;

    public NumericInputCoercionTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"UE5DumpNumInput_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _store = new SnapshotStore(new MockPlatformService(_tempDir));
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); }
        catch { /* best effort */ }
    }

    private SnapshotViewModel Vm() =>
        new(new StubDumpService(), _store, new MockLoggingService());

    // ── the coercion itself ──────────────────────────────────────────────────

    /// <summary>
    /// An emptied box is mid-edit, not a request to change anything, so the value in force
    /// survives. This is the case that produced the reported InvalidCastException.
    /// </summary>
    [Fact]
    public void Coerce_Null_KeepsTheValueInForce()
    {
        Assert.Equal(900, NumericInput.Coerce(null, current: 900, min: 60, max: 86400));
        Assert.Equal(60, NumericInput.Coerce(null, current: 60, min: 60, max: 86400));
    }

    /// <summary>Even on the null path the result is in range — a persisted value can be junk.</summary>
    [Fact]
    public void Coerce_Null_StillClampsAnOutOfRangeCurrent()
    {
        Assert.Equal(60, NumericInput.Coerce(null, current: 5, min: 60, max: 86400));
        Assert.Equal(86400, NumericInput.Coerce(null, current: 999999, min: 60, max: 86400));
    }

    [Theory]
    [InlineData(30, 60)]          // the reported input: below the floor -> the floor
    [InlineData(59, 60)]
    [InlineData(60, 60)]          // boundary, inclusive
    [InlineData(900, 900)]
    [InlineData(86400, 86400)]    // boundary, inclusive
    [InlineData(86401, 86400)]
    [InlineData(0, 60)]
    [InlineData(-5, 60)]          // a NumericUpDown will hold a negative
    public void Coerce_ClampsIntoRange(int input, int expected)
        => Assert.Equal(expected, NumericInput.Coerce(input, current: 900, min: 60, max: 86400));

    /// <summary>
    /// Clamping happens in decimal, BEFORE the cast. Going to int first would overflow, and the
    /// control really can hold these: Maximum only constrains Value when ClipValueToMinMax is on.
    /// </summary>
    [Fact]
    public void Coerce_SurvivesValuesOutsideIntRange()
    {
        Assert.Equal(86400, NumericInput.Coerce(decimal.MaxValue, 900, 60, 86400));
        Assert.Equal(60, NumericInput.Coerce(decimal.MinValue, 900, 60, 86400));
        Assert.Equal(86400, NumericInput.Coerce(3_000_000_000m, 900, 60, 86400));
    }

    /// <summary>Typing "60.7" should land on the nearer whole value, not fall back to the floor.</summary>
    [Theory]
    [InlineData(60.4, 60)]
    [InlineData(60.5, 61)]
    [InlineData(60.7, 61)]
    [InlineData(120.5, 121)]
    public void Coerce_RoundsRatherThanTruncating(double input, int expected)
        => Assert.Equal(expected, NumericInput.Coerce((decimal)input, 900, 60, 86400));

    /// <summary>A reversed range is a wiring mistake and must be loud, not silently absorbed.</summary>
    [Fact]
    public void Coerce_ReversedRange_Throws()
        => Assert.Throws<ArgumentOutOfRangeException>(() => NumericInput.Coerce(5, 5, max: 1, min: 10));

    // ── the view-model façades ───────────────────────────────────────────────

    [Fact]
    public void IntervalFacade_EmptyBox_LeavesTheLoopValueAlone()
    {
        var vm = Vm();
        vm.AutoSnapshotIntervalSec = 900;

        vm.AutoSnapshotIntervalSecValue = null;

        // The auto loop reads the int, so THAT is what must not have moved.
        Assert.Equal(900, vm.AutoSnapshotIntervalSec);
        Assert.Equal(900m, vm.AutoSnapshotIntervalSecValue);
    }

    /// <summary>
    /// The reported repro, as a unit: type 30, commit it. The old code let this reach an int
    /// binding as null on the click-away path; now it is simply the floor either way.
    /// </summary>
    [Fact]
    public void IntervalFacade_BelowMinimum_ClampsToTheFloor()
    {
        var vm = Vm();
        vm.AutoSnapshotIntervalSecValue = 30m;
        Assert.Equal(60, vm.AutoSnapshotIntervalSec);
    }

    /// <summary>
    /// A programmatic change to the canonical int must re-notify the façade, or the control would
    /// keep painting the previous number after a settings load.
    /// </summary>
    [Fact]
    public void IntervalFacade_IsNotifiedWhenTheIntChanges()
    {
        var vm = Vm();
        var seen = 0;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(vm.AutoSnapshotIntervalSecValue)) seen++;
        };

        vm.AutoSnapshotIntervalSec = 1800;

        Assert.True(seen > 0, "setting the int raised no change for the façade");
        Assert.Equal(1800m, vm.AutoSnapshotIntervalSecValue);
    }

    /// <summary>All four inputs on the panel, not just the one that was reported.</summary>
    [Fact]
    public void EveryFacadeOnThePanel_AbsorbsAnEmptyBox()
    {
        var vm = Vm();
        vm.AutoSnapshotIntervalSec = 900;
        vm.AutoSnapshotCount = 10;
        vm.SnapshotMinFreePercent = 10;
        vm.SnapshotMinFreeGb = 50;

        vm.AutoSnapshotIntervalSecValue = null;
        vm.AutoSnapshotCountValue = null;
        vm.SnapshotMinFreePercentValue = null;
        vm.SnapshotMinFreeGbValue = null;

        Assert.Equal(900, vm.AutoSnapshotIntervalSec);
        Assert.Equal(10, vm.AutoSnapshotCount);
        Assert.Equal(10, vm.SnapshotMinFreePercent);
        Assert.Equal(50, vm.SnapshotMinFreeGb);
    }

    [Fact]
    public void EveryFacadeOnThePanel_ClampsBothEnds()
    {
        var vm = Vm();

        vm.AutoSnapshotCountValue = 0m;         Assert.Equal(1, vm.AutoSnapshotCount);
        vm.AutoSnapshotCountValue = 99999m;     Assert.Equal(10000, vm.AutoSnapshotCount);
        vm.SnapshotMinFreePercentValue = -1m;   Assert.Equal(0, vm.SnapshotMinFreePercent);
        vm.SnapshotMinFreePercentValue = 500m;  Assert.Equal(99, vm.SnapshotMinFreePercent);
        vm.SnapshotMinFreeGbValue = -1m;        Assert.Equal(0, vm.SnapshotMinFreeGb);
        vm.SnapshotMinFreeGbValue = 9_999_999m; Assert.Equal(100000, vm.SnapshotMinFreeGb);
    }

    // ── the AXAML must not offer a range the view model will not honour ──────

    /// <summary>
    /// The control's Minimum/Maximum and the view model's clamp are two statements of one range.
    /// If the control allows more than the coercion accepts, the user's input is silently snapped;
    /// if it allows less, the clamp is dead code. Read both files and require agreement.
    /// </summary>
    [Fact]
    public void PanelRanges_MatchTheViewModelClamps()
    {
        var axaml = File.ReadAllText(RepoFile("ui/UE5DumpUI/Views/SnapshotPanel.axaml"));
        var vm = File.ReadAllText(RepoFile("ui/UE5DumpUI/ViewModels/SnapshotViewModel.cs"));

        (string Binding, string MinConst, string MaxConst)[] expected =
        [
            ("AutoSnapshotIntervalSecValue", "AutoSnapshotMinIntervalSec",  "AutoSnapshotMaxIntervalSec"),
            ("AutoSnapshotCountValue",       "AutoSnapshotMinCount",        "AutoSnapshotMaxCount"),
            ("SnapshotMinFreePercentValue",  "SnapshotMinFreePercentFloor", "SnapshotMinFreePercentCeil"),
            ("SnapshotMinFreeGbValue",       "SnapshotMinFreeGbFloor",      "SnapshotMinFreeGbCeil"),
        ];

        foreach (var (binding, minConst, maxConst) in expected)
        {
            var tag = Regex.Match(axaml,
                @"<NumericUpDown\b[^>]*Value=""\{Binding " + Regex.Escape(binding) + @"\}""[^>]*>",
                RegexOptions.Singleline);
            Assert.True(tag.Success, $"no NumericUpDown in SnapshotPanel.axaml binds {binding}");

            Assert.Equal(ConstValue(vm, minConst), AttrValue(tag.Value, "Minimum", binding));
            Assert.Equal(ConstValue(vm, maxConst), AttrValue(tag.Value, "Maximum", binding));
        }
    }

    /// <summary>
    /// The rule, app-wide: no NumericUpDown anywhere may bind a non-nullable property.
    ///
    /// <para>The reported defect was one control on one panel, but the shape is the control's, not
    /// the panel's — a sweep at the time of the fix found <b>18</b> NumericUpDowns and every single
    /// one carried it. Scoping this test to SnapshotPanel would have pinned the reported instance
    /// and left the other seventeen to be re-discovered one bug report at a time.</para>
    ///
    /// <para>The convention checked is the <c>Value</c>-suffixed façade. It is a naming convention
    /// rather than a type check because the AXAML is read as text — but the compiler backs it up:
    /// these are compiled bindings (every panel sets <c>x:DataType</c>), so a façade that does not
    /// exist is a build error, and one that is not <c>decimal?</c> would reintroduce the conversion.</para>
    /// </summary>
    [Fact]
    public void NoNumericUpDownAnywhere_BindsANonNullableProperty()
    {
        var dir = Path.GetDirectoryName(RepoFile("ui/UE5DumpUI/Views/SnapshotPanel.axaml"))!;
        var offenders = new List<string>();
        var seen = 0;
        var parsed = 0;

        foreach (var file in Directory.EnumerateFiles(dir, "*.axaml"))
        {
            var text = File.ReadAllText(file);
            foreach (Match tag in Regex.Matches(text, @"<NumericUpDown\b[^>]*>", RegexOptions.Singleline))
            {
                seen++;
                // ⚠ The path may be followed by binding modifiers (`, Mode=TwoWay`), so do NOT
                // anchor on the closing brace. An earlier version did, and silently skipped the one
                // control in the app written that way — PointerPanel's InvokeTimeoutMs — which was
                // therefore left carrying the defect while this test reported everything clean.
                var bound = Regex.Match(tag.Value, @"Value=""\{Binding\s+(?<p>[A-Za-z0-9_.]+)");
                if (!bound.Success) continue;      // bound some other way; not this defect's shape
                parsed++;
                var prop = bound.Groups["p"].Value;
                if (!prop.EndsWith("Value", StringComparison.Ordinal))
                    offenders.Add($"{Path.GetFileName(file)}: {prop}");
            }
        }

        // Guard the guard — a glob that silently matches nothing would pass this vacuously.
        Assert.True(seen >= 19, $"only {seen} NumericUpDown controls found; the scan is not reaching the views");

        // Every one of them must actually have had its binding path read. A tag whose Value binding
        // the regex cannot parse is skipped above, and a skip is indistinguishable from a pass —
        // which is exactly how InvokeTimeoutMs stayed broken through a green run.
        Assert.True(parsed == seen,
            $"{seen - parsed} NumericUpDown control(s) had a Value binding this test could not parse, "
            + "so they were neither checked nor reported. Widen the regex rather than the tolerance.");

        Assert.True(offenders.Count == 0,
            "NumericUpDown bound straight at a non-nullable property — an emptied box will show the user "
            + "a raw InvalidCastException (see [SNAPINTERVAL-2026-08-20] and Helpers/NumericInput.cs):"
            + Environment.NewLine + "  " + string.Join(Environment.NewLine + "  ", offenders));
    }

    // ── KeepCurrentIfEmpty: the null rule for the 14 controls with no view-model range ──

    [Fact]
    public void KeepCurrentIfEmpty_Null_ReturnsTheCurrentValue()
    {
        Assert.Equal(500, NumericInput.KeepCurrentIfEmpty(null, current: 500));
        Assert.Equal(-12.5, NumericInput.KeepCurrentIfEmpty(null, current: -12.5));
    }

    [Fact]
    public void KeepCurrentIfEmpty_PassesAValueThrough()
    {
        Assert.Equal(700, NumericInput.KeepCurrentIfEmpty(700m, current: 500));
        Assert.Equal(-3.25, NumericInput.KeepCurrentIfEmpty(-3.25m, current: 0.0));
    }

    /// <summary>
    /// Deliberately NOT clamped — these controls' ranges live on the control or in an existing
    /// view-model guard, and several (the Teleport coordinates) have no range at all. Inventing
    /// bounds here would silently move a coordinate the user typed.
    /// </summary>
    [Fact]
    public void KeepCurrentIfEmpty_DoesNotInventBounds()
    {
        Assert.Equal(-99999, NumericInput.KeepCurrentIfEmpty(-99999m, current: 0));
        Assert.Equal(1e12, NumericInput.KeepCurrentIfEmpty(1_000_000_000_000m, current: 0.0));
    }

    /// <summary>A NumericUpDown with no Maximum holds more than an int can, so the cast needs a guard.</summary>
    [Fact]
    public void KeepCurrentIfEmpty_Int_SaturatesRatherThanOverflowing()
    {
        Assert.Equal(int.MaxValue, NumericInput.KeepCurrentIfEmpty(decimal.MaxValue, current: 0));
        Assert.Equal(int.MinValue, NumericInput.KeepCurrentIfEmpty(decimal.MinValue, current: 0));
    }

    // ── ToControlValue: the getter direction, which a plain cast would throw on ──

    /// <summary>
    /// The negative control for <see cref="NumericInput.ToControlValue"/>: prove the obvious
    /// implementation really does throw, so the guard is not cargo-culted. These doubles come out
    /// of the running game, and the throw would land in a property getter during rendering.
    /// </summary>
    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    [InlineData(1e30)]
    public void ToControlValue_HandlesWhatAPlainCastThrowsOn(double v)
    {
        Assert.Throws<OverflowException>(() => (decimal)v);   // what the generated code did first
        _ = NumericInput.ToControlValue(v);                   // must not throw
    }

    /// <summary>NaN becomes an empty box, which is what "not a number" honestly is.</summary>
    [Fact]
    public void ToControlValue_NaN_IsAnEmptyBox()
        => Assert.Null(NumericInput.ToControlValue(double.NaN));

    [Fact]
    public void ToControlValue_Infinities_Saturate()
    {
        Assert.Equal(decimal.MaxValue, NumericInput.ToControlValue(double.PositiveInfinity));
        Assert.Equal(decimal.MinValue, NumericInput.ToControlValue(double.NegativeInfinity));
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(1234.5)]
    [InlineData(-9876.25)]
    [InlineData(100000.0)]
    public void ToControlValue_OrdinaryCoordinatesRoundTrip(double v)
    {
        var control = NumericInput.ToControlValue(v);
        Assert.NotNull(control);
        Assert.Equal(v, NumericInput.KeepCurrentIfEmpty(control, current: double.MinValue));
    }

    private static int AttrValue(string tag, string attr, string who)
    {
        var m = Regex.Match(tag, attr + @"=""(-?\d+)""");
        Assert.True(m.Success, $"{who}: no {attr} on the control");
        return int.Parse(m.Groups[1].Value);
    }

    private static int ConstValue(string source, string name)
    {
        var m = Regex.Match(source, @"const\s+int\s+" + Regex.Escape(name) + @"\s*=\s*(-?\d+)\s*;");
        Assert.True(m.Success, $"no `const int {name}` in SnapshotViewModel.cs");
        return int.Parse(m.Groups[1].Value);
    }

    internal static string RepoFile(string relative)
    {
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 8 && dir is not null; i++, dir = Path.GetDirectoryName(dir))
        {
            var candidate = Path.Combine(dir, relative.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(candidate)) return candidate;
        }
        throw new FileNotFoundException($"could not locate {relative} from {AppContext.BaseDirectory}");
    }
}
