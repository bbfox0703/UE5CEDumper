using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using UE5DumpUI.Core;
using UE5DumpUI.Models;
using UE5DumpUI.Services;

namespace UE5DumpUI.Views;

/// <summary>
/// Minimal single-input modal for capturing the freeze target value.
///
/// Triggered from <c>PropertySearchPanel</c>'s row-level "Freeze" button.
/// Shows the target (class / prop / offset / type) read-only so the user
/// can verify they're freezing the right thing, then collects ONE typed
/// value. Validates against the property's UE type before accepting —
/// including its WIDTH, because every writer downstream narrows in silence
/// (see <see cref="IntegerRange"/>).
///
/// Also used by Property Search → Force (Solide) via
/// <c>PropertySearchPanel.PromptForceValueAsync</c>, which reuses this dialog for its
/// per-type validation — so a check added here covers both features.
///
/// Returns the validated Lua literal on OK (e.g. <c>"100.0"</c>, <c>"true"</c>);
/// returns <c>null</c> on Cancel or invalid input that the user dismissed.
///
/// Code-behind only (no XAML / CompiledBinding) for AOT compatibility,
/// matching the project convention (see <see cref="ObjectInstancePickerDialog"/>).
/// </summary>
public sealed class FreezeValueDialog : Window
{
    /// <summary>
    /// Which action the value being typed will drive. The dialog is shared: the value
    /// validation, the type mapping and the scope summary are identical, but the
    /// ACTION is not, and every string that names it must follow (audit #5 AF22).
    ///
    /// <para><see cref="Freeze"/> generates a CE script the user then enables;
    /// <see cref="Force"/> writes the field immediately through the DLL and holds it on
    /// every live instance. Reusing the Freeze strings for Force put "Create freeze
    /// script" on the button of a flow that creates no script, and offered a remedy
    /// ("edit className in the generated CFG block") that has no CFG block to edit —
    /// the same unreachable-advice trap as Z10.</para>
    /// </summary>
    public enum Purpose { Freeze, Force }

    private readonly PropertySearchMatch _match;
    private readonly string _helperType;
    private readonly Purpose _purpose;
    private TextBox _valueBox = null!;
    private TextBlock _errorLabel = null!;
    private Button _btnOk = null!;
    private Button _btnCancel = null!;

    /// <summary>Validated Lua literal (e.g. <c>"42"</c>, <c>"3.14"</c>,
    /// <c>"true"</c>). Null when the user cancels.</summary>
    public string? ValueLiteral { get; private set; }

    // ⚠ NO DEFAULT ON `purpose`, deliberately. A default is the shape the original defect
    // had: the Force flow was `new FreezeValueDialog(match)` and silently inherited every
    // word of the Freeze wording, including CFG-block advice that means nothing for a
    // DLL-held value. Both call sites now state which flow they are, and a third that
    // forgets is a COMPILE ERROR rather than a dialog quietly lying about itself —
    // strictly stronger than any test, and the lesson working-lessons.md §2.2 recorded
    // ("make a wired-through field required"). [PARAMSSORT-2026-08-22 sweep]
    public FreezeValueDialog(PropertySearchMatch match, Purpose purpose)
    {
        _match = match;
        _helperType = HelperTypeFor(match);
        _purpose = purpose;

        Title = Str(Leaf.Title);
        Width = 520;
        MinWidth = 400;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;
        Background = new SolidColorBrush(Color.Parse("#1E1E1E"));

        var root = new StackPanel
        {
            Margin = new Thickness(16),
            Spacing = 8,
        };

        // Read-only target details. "Class" is the class the freeze will be KEYED on,
        // which is not always the one in the row's Class column — see HeldClassName.
        root.Children.Add(BuildLabelRow("Class:",    FreezeScriptGenerator.HeldClassName(
                                                         _match.ClassName, _match.DefiningClassName)));
        root.Children.Add(BuildLabelRow("Property:", _match.PropName));
        root.Children.Add(BuildLabelRow("Type:",     $"{_match.PropType} -> {_helperType}"));
        root.Children.Add(BuildLabelRow("Offset:",   _match.OffsetHex));
        // What the freeze will actually hold. Stated up front because it is the one
        // thing the user cannot infer from the row: a Property Search row for an
        // inherited field is keyed to the class that DECLARES it, so freezing "my
        // pawn's bCanBeDamaged" is really freezing every live Actor in the level.
        root.Children.Add(BuildLabelRow("Scope:",    ScopeSummary(_match)));

        var scopeWarning = ScopeWarning(_match, Str(Leaf.NarrowHint));
        if (scopeWarning != null)
        {
            root.Children.Add(new TextBlock
            {
                Text = scopeWarning,
                Foreground = new SolidColorBrush(Color.Parse("#DCDCAA")),
                FontSize = 11,
                TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                Margin = new Thickness(0, 4, 0, 0),
            });
        }

        // Value input
        var valueLbl = new TextBlock
        {
            Text = StrFormat(Leaf.ValueLabel, _helperType),
            Foreground = new SolidColorBrush(Color.Parse("#DCDCAA")),
            FontSize = 12,
            Margin = new Thickness(0, 8, 0, 2),
        };
        root.Children.Add(valueLbl);

        _valueBox = new TextBox
        {
            Text = SuggestedDefault(_helperType),
            FontSize = 13,
            FontFamily = new FontFamily("Consolas, Cascadia Mono, monospace"),
            Padding = new Thickness(6, 4),
        };
        _valueBox.KeyDown += OnValueKeyDown;
        root.Children.Add(_valueBox);

        // Inline error label (initially blank)
        _errorLabel = new TextBlock
        {
            Text = "",
            Foreground = new SolidColorBrush(Color.Parse("#F48771")),
            FontSize = 11,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            MinHeight = 14,
        };
        root.Children.Add(_errorLabel);

        // Hint about bool acceptance
        if (_helperType == "bool")
        {
            root.Children.Add(new TextBlock
            {
                Text = "Accepts: true / false / 1 / 0",
                Foreground = new SolidColorBrush(Color.Parse("#808080")),
                FontSize = 11,
            });
        }

        // Buttons
        var btnRow = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Margin = new Thickness(0, 12, 0, 0),
        };

        _btnCancel = new Button { Content = "Cancel", Padding = new Thickness(14, 4) };
        _btnCancel.Click += (_, _) => { ValueLiteral = null; Close(); };
        btnRow.Children.Add(_btnCancel);

        _btnOk = new Button
        {
            Content = Str(Leaf.Ok),
            Padding = new Thickness(14, 4),
            IsDefault = true,
        };
        _btnOk.Click += OnOkClicked;
        btnRow.Children.Add(_btnOk);

        root.Children.Add(btnRow);

        Content = root;

        // Focus the value box on open so the user can type immediately.
        Opened += (_, _) => _valueBox.Focus();
    }

    private static StackPanel BuildLabelRow(string label, string value)
    {
        var row = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 8 };
        row.Children.Add(new TextBlock
        {
            Text = label,
            Foreground = new SolidColorBrush(Color.Parse("#9CDCFE")),
            FontSize = 12,
            Width = 80,
        });
        row.Children.Add(new TextBlock
        {
            Text = value,
            Foreground = new SolidColorBrush(Color.Parse("#D4D4D4")),
            FontSize = 12,
            FontFamily = new FontFamily("Consolas, Cascadia Mono, monospace"),
        });
        return row;
    }

    /// <summary>
    /// The helper type this dialog validates against, for one search-result row.
    ///
    /// <para>Size-aware because an <c>EnumProperty</c>'s width comes from the engine and
    /// not from its type name. This MUST stay the same call
    /// <see cref="FreezeScriptGenerator.Generate"/> makes: the value is checked here
    /// against the writer the generated script will use, so a divergence would validate
    /// against one width and write with another (audit #5 Y15).</para>
    ///
    /// <para>A named method rather than an inline expression in the constructor, because
    /// the constructor needs an Avalonia runtime and cannot be unit-tested — this is the
    /// seam that lets the agreement be asserted.</para>
    /// </summary>
    internal static string HelperTypeFor(PropertySearchMatch match)
        => FreezeScriptGenerator.MapToHelperType(match.PropType, match.PropSize);

    /// <summary>
    /// One line naming every object the freeze will write to.
    ///
    /// <para>The generated script asks the DLL for the class <b>and every subclass of
    /// it</b> (<c>ue5_freeze_helper.lua</c>'s <c>derived</c>), matching what the Force
    /// submenu on the same row already does. That is the right scope — an exact-class
    /// pool for an inherited field holds whichever stray ancestor instance the level
    /// happens to have and misses the pawn entirely — but it is also much wider than
    /// "the thing I clicked", so the dialog says so before the script exists rather
    /// than after nothing appears to happen. (`[FREEZESCOPE-2026-08-18]`)</para>
    /// </summary>
    internal static string ScopeSummary(PropertySearchMatch match)
    {
        var held = FreezeScriptGenerator.HeldClassName(match.ClassName, match.DefiningClassName);
        if (string.IsNullOrEmpty(held)) return "every live instance of the matched class";
        return match.InheritedByCount > 0
            ? $"every live {held} and every subclass ({match.InheritedByCount} inherit this field)"
            : $"every live {held} and every subclass";
    }

    /// <summary>
    /// The caveat, or null when there is nothing to warn about.
    ///
    /// <para>Fires when the field is <b>inherited</b> — i.e. the class this freeze is
    /// keyed on is an ANCESTOR of whatever the user was actually looking at, which is
    /// the case for every <c>AActor</c> field the Property Search recipes recommend
    /// (<c>bCanBeDamaged</c>, <c>bHidden</c>, <c>bReplicates</c>). Also fires if the
    /// row's own class ever differs from the defining class, which post-dedup it does
    /// not, but the wire is free to change and the warning should not depend on that.
    /// </para>
    ///
    /// <para>Null for a field unique to its class: there is nothing surprising about
    /// freezing a game-specific field on the class that declares it, and a warning that
    /// fires every time is a warning nobody reads.</para>
    /// </summary>
    /// <param name="narrowHint">How to target fewer classes, IN THE FLOW THE USER IS IN.
    /// Passed rather than hardcoded because the two flows have different answers and the
    /// Freeze one is unreachable from Force — there is no generated CFG block to edit
    /// (audit #5 AF22).
    /// <para><b>Required, deliberately no default.</b> A warning that does not say how to
    /// narrow the scope is only an apology (the property
    /// <c>ScopeWarning_FiresWhenTheFieldIsInherited</c> has pinned since it was written),
    /// and a default would let a new call site silently drop it — which is the exact
    /// shape of the defect being fixed. Callers with genuinely no advice pass "".</para>
    /// </param>
    internal static string? ScopeWarning(PropertySearchMatch match, string narrowHint)
    {
        var held = FreezeScriptGenerator.HeldClassName(match.ClassName, match.DefiningClassName);
        bool differentRowClass = !string.IsNullOrEmpty(match.ClassName)
                                 && !string.Equals(match.ClassName, held, StringComparison.Ordinal);
        if (match.InheritedByCount <= 0 && !differentRowClass) return null;

        var s = $"⚠ {match.PropName} is declared on {held}, not on one specific object — "
              + $"so this holds the value on EVERY live {held} and subclass at once, not just "
              + "the one you were looking at.";
        return string.IsNullOrEmpty(narrowHint) ? s : s + " " + narrowHint;
    }

    // ---- purpose-scoped strings (en.axaml) -------------------------------------

    /// <summary>Which of the four purpose-scoped strings is wanted. An enum rather than a
    /// string so <see cref="KeyFor"/> is exhaustive over both axes and a typo cannot
    /// compile.</summary>
    internal enum Leaf { Title, ValueLabel, Ok, NarrowHint }

    /// <summary>
    /// The en.axaml key for one (purpose, leaf) pair — <b>every key written out in full</b>.
    ///
    /// <para>This used to interpolate the purpose and the leaf into the key, which is
    /// shorter and worse. <c>tools/check_axaml_strings.py</c> greps for literal key tokens
    /// precisely because a missing <c>StaticResource</c> raises at load time and takes the
    /// panel with it; an interpolated key is invisible to that grep, so the gate saw a
    /// dangling reference to the constant prefix ahead of the first brace and called all
    /// eight real keys dead. It went red the moment AF22 landed and would have stayed red,
    /// hiding every genuine missing key behind the noise. Composing a resource key at
    /// runtime also leans on <c>Enum.ToString</c>, which is metadata the trimmer has no
    /// reason to keep — a literal cannot rot that way either.</para>
    ///
    /// <para>Keep the arms literal. Rebuilding the string from <c>nameof</c> or a prefix
    /// constant would defeat the grep again for the same reason. For the same reason the
    /// prose here never spells a key out: the checker cannot tell a comment from a call
    /// site, so an example key in a doc comment IS a reference.</para>
    /// </summary>
    internal static string KeyFor(Purpose purpose, Leaf leaf) => (purpose, leaf) switch
    {
        (Purpose.Freeze, Leaf.Title)      => "str.ValuePrompt.Freeze.Title",
        (Purpose.Freeze, Leaf.ValueLabel) => "str.ValuePrompt.Freeze.ValueLabel",
        (Purpose.Freeze, Leaf.Ok)         => "str.ValuePrompt.Freeze.Ok",
        (Purpose.Freeze, Leaf.NarrowHint) => "str.ValuePrompt.Freeze.NarrowHint",
        (Purpose.Force,  Leaf.Title)      => "str.ValuePrompt.Force.Title",
        (Purpose.Force,  Leaf.ValueLabel) => "str.ValuePrompt.Force.ValueLabel",
        (Purpose.Force,  Leaf.Ok)         => "str.ValuePrompt.Force.Ok",
        (Purpose.Force,  Leaf.NarrowHint) => "str.ValuePrompt.Force.NarrowHint",
        // Unreachable from any in-repo call site: both enums are covered above. Reached
        // only by casting an undeclared value, which is a programmer error — and falling
        // back to the Freeze wording is the AF22 defect itself, so refuse instead.
        _ => throw new ArgumentOutOfRangeException(
                 nameof(purpose), $"No en.axaml wording defined for {purpose}/{leaf}"),
    };

    private string Str(Leaf leaf)
    {
        var key = KeyFor(_purpose, leaf);
        var s = Res.Get(key);
        // Res.Get returns "" when Application.Current is null (unit tests construct
        // no Avalonia app) or the key is missing. Falling back to the Freeze wording
        // would re-introduce exactly the defect this fixes, so fall back to the KEY —
        // visibly wrong beats plausibly wrong.
        return string.IsNullOrEmpty(s) ? key : s;
    }

    private string StrFormat(Leaf leaf, params object[] args)
    {
        var key = KeyFor(_purpose, leaf);
        var t = Res.Get(key);
        return string.IsNullOrEmpty(t) ? key : string.Format(t, args);
    }

    /// <summary>
    /// The "big number" a freeze usually wants. Clamped to what the target type can
    /// hold — pre-filling a flat 9999 put the DEFAULT out of range on every byte-wide
    /// property, so the user could accept a value they never typed and have it land as
    /// 15 (audit #5 Y9).
    /// </summary>
    private const decimal SuggestedMagnitude = 9999m;

    internal static string SuggestedDefault(string helperType) => helperType switch
    {
        "bool"              => "true",
        "float" or "double" => "9999.0",
        ""                  => "",  // unsupported type
        // Derived from the SAME range table the validator enforces, so the suggestion
        // can never drift into a value that its own OK button would reject.
        _                   => Math.Min(SuggestedMagnitude, IntegerRange(helperType).Max)
                                   .ToString(CultureInfo.InvariantCulture),
    };

    /// <summary>
    /// Inclusive range each integer helper type can actually hold.
    ///
    /// This dialog is the only place a width check can be USEFUL, because everything
    /// downstream narrows in silence: <c>ue5_freeze_helper.lua</c>'s writer is
    /// <c>writeByte(addr, math.floor(v) % 256)</c> and Solide's is
    /// <c>static_cast&lt;uint8_t&gt;(llround(value))</c> — both turn 9999 into 15 and
    /// report success (audit #5 Y9). The two consumers of this dialog are Freeze and
    /// Property Search → Force, so one check covers both.
    ///
    /// <c>decimal</c> because the set spans <c>long.MinValue</c>…<c>ulong.MaxValue</c>,
    /// which no single integral type covers.
    /// </summary>
    internal static (decimal Min, decimal Max) IntegerRange(string helperType) => helperType switch
    {
        "int8"   => (sbyte.MinValue,  sbyte.MaxValue),
        "int16"  => (short.MinValue,  short.MaxValue),
        "int32"  => (int.MinValue,    int.MaxValue),
        "int64"  => (long.MinValue,   long.MaxValue),
        "uint8"  => (byte.MinValue,   byte.MaxValue),
        "uint16" => (ushort.MinValue, ushort.MaxValue),
        "uint32" => (uint.MinValue,   uint.MaxValue),
        "uint64" => (ulong.MinValue,  ulong.MaxValue),
        _        => (long.MinValue,   long.MaxValue),
    };

    /// <summary>
    /// What the writers would ACTUALLY land for an out-of-range value — they mask to the
    /// field width. Naming it in the error turns "rejected" into "here is the 15 you
    /// would otherwise have spent an evening chasing".
    /// </summary>
    internal static decimal WrapToRange(decimal value, (decimal Min, decimal Max) range)
    {
        decimal span = range.Max - range.Min + 1;
        return ((value - range.Min) % span + span) % span + range.Min;
    }

    private void OnValueKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            OnOkClicked(this, new RoutedEventArgs());
            e.Handled = true;
        }
    }

    private void OnOkClicked(object? sender, RoutedEventArgs e)
    {
        var input = _valueBox.Text ?? "";
        var literal = ValidateAndConvert(input, _helperType, out var err);
        if (literal == null)
        {
            _errorLabel.Text = err;
            return;
        }
        ValueLiteral = literal;
        Close();
    }

    /// <summary>
    /// Convert user input to a Lua literal expression for the given
    /// helper type, OR return null with an error message in <paramref name="err"/>.
    /// </summary>
    public static string? ValidateAndConvert(string input, string helperType, out string err)
    {
        err = "";
        var trimmed = (input ?? "").Trim();
        if (trimmed.Length == 0)
        {
            err = "Value cannot be empty";
            return null;
        }

        switch (helperType)
        {
            case "bool":
                var lower = trimmed.ToLowerInvariant();
                if (lower is "true" or "1") return "true";
                if (lower is "false" or "0") return "false";
                err = "Expected: true / false / 1 / 0";
                return null;

            case "float" or "double":
                // TryParse accepts "NaN"/"Infinity" and overflow rounds to ±Infinity, and
                // ToString("R") then emits the bare word — not a Lua number literal, so the
                // emitted script reads it as an undefined global (nil) and freezes nothing.
                // Reject at the dialog, where the user can still see why. (B23)
                if (!double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var dv)
                    || !double.IsFinite(dv))
                {
                    err = $"Not a valid {helperType} number";
                    return null;
                }
                // A 4-byte field cannot hold every finite double, and the narrowing is
                // silent on both sides (CE's writeFloat, Solide's WriteFloatAt), so an
                // accepted 1e300 lands as +inf and 1e-300 lands as 0 (audit #5 Y9).
                if (helperType == "float")
                {
                    var fv = (float)dv;
                    if (!float.IsFinite(fv))
                    {
                        err = $"Too large for a 4-byte float (max ±{float.MaxValue:R}) — "
                            + "it would be written as infinity";
                        return null;
                    }
                    if (fv == 0f && dv != 0d)
                    {
                        err = $"Too small for a 4-byte float (smallest ±{float.Epsilon:R}) — "
                            + "it would be written as 0";
                        return null;
                    }
                }
                return dv.ToString("R", CultureInfo.InvariantCulture);

            case "int8" or "int16" or "int32" or "int64":
            {
                if (!long.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var sv))
                {
                    err = $"Not a valid {helperType} integer";
                    return null;
                }
                var srange = IntegerRange(helperType);
                if (sv < srange.Min || sv > srange.Max)
                {
                    err = $"{helperType} holds {srange.Min} to {srange.Max} — "
                        + $"{sv} would be written as {WrapToRange(sv, srange)}";
                    return null;
                }
                return sv.ToString(CultureInfo.InvariantCulture);
            }

            case "uint8" or "uint16" or "uint32" or "uint64":
            {
                if (!ulong.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var uv))
                {
                    err = $"Not a valid {helperType} unsigned integer";
                    return null;
                }
                var urange = IntegerRange(helperType);
                if (uv > urange.Max)
                {
                    err = $"{helperType} holds {urange.Min} to {urange.Max} — "
                        + $"{uv} would be written as {WrapToRange(uv, urange)}";
                    return null;
                }
                return uv.ToString(CultureInfo.InvariantCulture);
            }

            case "":
                err = "Type not supported by freeze v1 -- numerics + bool only";
                return null;

            default:
                err = $"Internal: unhandled helper type '{helperType}'";
                return null;
        }
    }
}
