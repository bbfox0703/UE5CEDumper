using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
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
    private readonly PropertySearchMatch _match;
    private readonly string _helperType;
    private TextBox _valueBox = null!;
    private TextBlock _errorLabel = null!;
    private Button _btnOk = null!;
    private Button _btnCancel = null!;

    /// <summary>Validated Lua literal (e.g. <c>"42"</c>, <c>"3.14"</c>,
    /// <c>"true"</c>). Null when the user cancels.</summary>
    public string? ValueLiteral { get; private set; }

    public FreezeValueDialog(PropertySearchMatch match)
    {
        _match = match;
        _helperType = HelperTypeFor(match);

        Title = "Freeze property value";
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

        // Read-only target details
        root.Children.Add(BuildLabelRow("Class:",    _match.ClassName));
        root.Children.Add(BuildLabelRow("Property:", _match.PropName));
        root.Children.Add(BuildLabelRow("Type:",     $"{_match.PropType} -> {_helperType}"));
        root.Children.Add(BuildLabelRow("Offset:",   _match.OffsetHex));

        // Value input
        var valueLbl = new TextBlock
        {
            Text = $"Freeze value ({_helperType}):",
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
            Content = "Create freeze script",
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
