using UE5DumpUI.Models;
using UE5DumpUI.Services;
using UE5DumpUI.ViewModels;
using UE5DumpUI.Views;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// Tests for <see cref="FreezeValueDialog.ValidateAndConvert"/>.
///
/// The dialog itself (Avalonia Window) can't be exercised in headless
/// tests without spinning up the runtime, so we promoted the value
/// parsing to a pure static method and test that. Covers:
///   - All 12 supported helper types accept canonical inputs
///   - Bool accepts true/false/1/0 (case insensitive) but rejects junk
///   - Float / double accept scientific notation + culture-invariant parse
///   - Signed vs unsigned integer handling
///   - Empty / whitespace rejection
///   - Unsupported type returns the v1-scope error
/// </summary>
public class FreezeValueDialogValidationTests
{
    [Theory]
    [InlineData("true",  "true")]
    [InlineData("True",  "true")]
    [InlineData("TRUE",  "true")]
    [InlineData("1",     "true")]
    [InlineData("false", "false")]
    [InlineData("False", "false")]
    [InlineData("0",     "false")]
    public void Validate_Bool_AcceptsAllForms(string input, string expected)
    {
        var literal = FreezeValueDialog.ValidateAndConvert(input, "bool", out var err);
        Assert.NotNull(literal);
        Assert.Equal(expected, literal);
        Assert.Equal("", err);
    }

    [Theory]
    [InlineData("yes")]
    [InlineData("no")]
    [InlineData("2")]
    [InlineData("")]
    public void Validate_Bool_RejectsOtherForms(string input)
    {
        var literal = FreezeValueDialog.ValidateAndConvert(input, "bool", out var err);
        Assert.Null(literal);
        Assert.NotEqual("", err);
    }

    [Theory]
    [InlineData("3.14",     "float")]
    [InlineData("-2.5",     "float")]
    [InlineData("1e10",     "float")]
    [InlineData("0.0",      "double")]
    [InlineData("-1.5e-3",  "double")]
    public void Validate_FloatDouble_AcceptsNumbers(string input, string helperType)
    {
        var literal = FreezeValueDialog.ValidateAndConvert(input, helperType, out var err);
        Assert.NotNull(literal);
        Assert.Equal("", err);
        // Round-trip parse to confirm we emitted a Lua-parseable literal.
        Assert.True(double.TryParse(literal,
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture,
            out _));
    }

    [Theory]
    [InlineData("not a number", "float")]
    [InlineData("",             "double")]
    [InlineData("   ",          "float")]
    public void Validate_FloatDouble_RejectsJunk(string input, string helperType)
    {
        var literal = FreezeValueDialog.ValidateAndConvert(input, helperType, out var err);
        Assert.Null(literal);
        Assert.NotEqual("", err);
    }

    [Theory]
    [InlineData("0",            "int32")]
    [InlineData("-1",           "int32")]
    [InlineData("2147483647",   "int32")]
    [InlineData("-9223372036854775808", "int64")]
    public void Validate_SignedInt_AcceptsRange(string input, string helperType)
    {
        var literal = FreezeValueDialog.ValidateAndConvert(input, helperType, out var err);
        Assert.NotNull(literal);
        Assert.Equal("", err);
        Assert.Equal(input.Trim(), literal);
    }

    [Theory]
    [InlineData("-1",                       "uint32")]
    [InlineData("not a number",             "uint32")]
    public void Validate_UnsignedInt_RejectsNegativeAndJunk(string input, string helperType)
    {
        var literal = FreezeValueDialog.ValidateAndConvert(input, helperType, out var err);
        Assert.Null(literal);
        Assert.NotEqual("", err);
    }

    [Theory]
    [InlineData("0",            "uint8")]
    [InlineData("255",          "uint8")]
    [InlineData("4294967295",   "uint32")]
    public void Validate_UnsignedInt_AcceptsRange(string input, string helperType)
    {
        var literal = FreezeValueDialog.ValidateAndConvert(input, helperType, out var err);
        Assert.NotNull(literal);
        Assert.Equal("", err);
    }

    [Fact]
    public void Validate_UnsupportedType_ExplainsV1Scope()
    {
        var literal = FreezeValueDialog.ValidateAndConvert("anything", "", out var err);
        Assert.Null(literal);
        Assert.Contains("v1", err);
    }

    // ------------------------------------------------------------------
    // Width enforcement (audit #5 Y9)
    //
    // Nothing downstream can report an over-wide value: ue5_freeze_helper.lua writes
    // `writeByte(addr, math.floor(v) % 256)` and Solide::WriteNumeric does
    // `static_cast<uint8_t>(llround(value))`. Both turn 9999 into 15 and report success,
    // so the dialog is the only place the user can be told.
    // ------------------------------------------------------------------

    [Theory]
    [InlineData("256",         "uint8",  "255")]
    [InlineData("9999",        "uint8",  "255")]
    [InlineData("65536",       "uint16", "65535")]
    [InlineData("4294967296",  "uint32", "4294967295")]
    public void Validate_UnsignedInt_RejectsAboveWidth(string input, string helperType, string max)
    {
        var literal = FreezeValueDialog.ValidateAndConvert(input, helperType, out var err);
        Assert.Null(literal);
        Assert.Contains(max, err);          // names the range…
        Assert.Contains("would be written as", err);   // …and the value that would land
    }

    [Theory]
    [InlineData("128",    "int8")]
    [InlineData("-129",   "int8")]
    [InlineData("9999",   "int8")]
    [InlineData("32768",  "int16")]
    [InlineData("-32769", "int16")]
    [InlineData("2147483648", "int32")]
    public void Validate_SignedInt_RejectsOutsideWidth(string input, string helperType)
    {
        var literal = FreezeValueDialog.ValidateAndConvert(input, helperType, out var err);
        Assert.Null(literal);
        Assert.Contains("would be written as", err);
    }

    [Fact]
    public void Validate_NamesTheExactValueThatWouldHaveLanded()
    {
        // The finding's own example. 9999 & 0xFF == 15 for uint8, and the int8
        // sign-extension of the same byte is also 15 — this is the number the user
        // would otherwise be staring at in-game with no explanation.
        FreezeValueDialog.ValidateAndConvert("9999", "uint8", out var uerr);
        Assert.Contains("would be written as 15", uerr);

        FreezeValueDialog.ValidateAndConvert("9999", "int8", out var serr);
        Assert.Contains("would be written as 15", serr);
    }

    [Theory]
    [InlineData("-128",  "int8")]
    [InlineData("127",   "int8")]
    [InlineData("255",   "uint8")]
    [InlineData("0",     "uint8")]
    [InlineData("32767", "int16")]
    [InlineData("65535", "uint16")]
    [InlineData("18446744073709551615", "uint64")]
    [InlineData("-9223372036854775808", "int64")]
    public void Validate_BoundaryValues_AreAccepted(string input, string helperType)
    {
        // The other direction, and the more important one: an inclusive bound must NOT
        // be rejected, or the check becomes an off-by-one that costs the user the top
        // value of every field.
        var literal = FreezeValueDialog.ValidateAndConvert(input, helperType, out var err);
        Assert.NotNull(literal);
        Assert.Equal("", err);
        Assert.Equal(input, literal);
    }

    [Theory]
    [InlineData("1e300")]      // overflows a 4-byte float → +inf
    [InlineData("-1e300")]
    public void Validate_Float_RejectsValuesThatWouldBecomeInfinity(string input)
    {
        var literal = FreezeValueDialog.ValidateAndConvert(input, "float", out var err);
        Assert.Null(literal);
        Assert.Contains("infinity", err);
    }

    [Fact]
    public void Validate_Float_RejectsValuesThatWouldCollapseToZero()
    {
        var literal = FreezeValueDialog.ValidateAndConvert("1e-300", "float", out var err);
        Assert.Null(literal);
        Assert.Contains("written as 0", err);
    }

    [Theory]
    [InlineData("1e300")]
    [InlineData("1e-300")]
    public void Validate_Double_StillAcceptsTheFullDoubleRange(string input)
    {
        // Negative control in the spec, not just the test run: the float narrowing check
        // must NOT fire for an 8-byte field, or DoubleProperty loses most of its range.
        var literal = FreezeValueDialog.ValidateAndConvert(input, "double", out var err);
        Assert.NotNull(literal);
        Assert.Equal("", err);
    }

    // ------------------------------------------------------------------
    // The pre-filled default must itself survive the check the OK button applies —
    // a flat "9999" meant a ByteProperty opened with an out-of-range value the user
    // never typed.
    // ------------------------------------------------------------------

    [Theory]
    [InlineData("uint8")]
    [InlineData("int8")]
    [InlineData("uint16")]
    [InlineData("int16")]
    [InlineData("uint32")]
    [InlineData("int32")]
    [InlineData("uint64")]
    [InlineData("int64")]
    [InlineData("float")]
    [InlineData("double")]
    [InlineData("bool")]
    public void SuggestedDefault_IsAlwaysAcceptedByValidateAndConvert(string helperType)
    {
        var suggested = FreezeValueDialog.SuggestedDefault(helperType);
        var literal = FreezeValueDialog.ValidateAndConvert(suggested, helperType, out var err);
        Assert.NotNull(literal);
        Assert.Equal("", err);
    }

    [Theory]
    [InlineData("uint8",  "255")]
    [InlineData("int8",   "127")]
    [InlineData("uint16", "9999")]   // 9999 fits — the clamp only bites on byte widths
    [InlineData("int32",  "9999")]
    public void SuggestedDefault_ClampsToWhatTheTypeHolds(string helperType, string expected)
        => Assert.Equal(expected, FreezeValueDialog.SuggestedDefault(helperType));

    [Theory]
    [InlineData("uint8",  9999,  15)]
    [InlineData("int8",   9999,  15)]
    [InlineData("int8",   -129,  127)]
    [InlineData("uint16", 65536, 0)]
    public void WrapToRange_MatchesWhatTheWritersDo(string helperType, long value, long expected)
    {
        // Cross-checks the arithmetic in the error message against the masking the Lua
        // and C++ writers perform, so the number we quote can't drift from the number
        // that lands.
        var range = FreezeValueDialog.IntegerRange(helperType);
        Assert.Equal(expected, FreezeValueDialog.WrapToRange(value, range));
    }

    // ------------------------------------------------------------------
    // Audit #5 Y15 — the dialog and the generated script must agree.
    //
    // The dialog validates the typed value against the helper type it computes
    // from (PropType, PropSize); FreezeScriptGenerator writes with the helper type
    // it computes from the SAME pair. If those two calls ever diverge, the value
    // is checked against one writer and handed to another — which is what this
    // audit's recurring root cause looks like (the report and the reality computed
    // by different code paths). These tests pin the pair together.
    // ------------------------------------------------------------------

    [Theory]
    [InlineData(1, "uint8",  "255",   "256")]
    [InlineData(2, "uint16", "65535", "65536")]
    [InlineData(4, "int32",  "9999",  "99999999999")]
    [InlineData(8, "int64",  "9999",  "99999999999999999999")]
    public void Validate_EnumProperty_ChecksTheWidthTheEngineReported(
        int propSize, string expectedHelper, string highestAccepted, string firstRejected)
    {
        // Same call the dialog constructor makes.
        var helperType = FreezeScriptGenerator.MapToHelperType("EnumProperty", propSize);
        Assert.Equal(expectedHelper, helperType);

        Assert.NotNull(FreezeValueDialog.ValidateAndConvert(highestAccepted, helperType, out var okErr));
        Assert.Equal("", okErr);

        Assert.Null(FreezeValueDialog.ValidateAndConvert(firstRejected, helperType, out var badErr));
        Assert.NotEqual("", badErr);
    }

    [Fact]
    public void Validate_OneByteEnum_NamesTheValueThatWouldHaveLanded()
    {
        // The whole point of Y15 + Y9 together: before the width was plumbed through,
        // a 1-byte enum was validated as int32, so 9999 was ACCEPTED here and then
        // written by a 4-byte writer over three neighbouring bytes.
        var helperType = FreezeScriptGenerator.MapToHelperType("EnumProperty", 1);
        var literal = FreezeValueDialog.ValidateAndConvert("9999", helperType, out var err);

        Assert.Null(literal);
        Assert.Contains("uint8", err);
        Assert.Contains("would be written as 15", err);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(0)]
    public void SuggestedDefault_ForEveryEnumWidth_SurvivesItsOwnValidator(int propSize)
    {
        // Y9's lesson applied to the new types this fix can now select: tightening a
        // validator without its pre-fill yields a dialog that rejects its own default.
        var helperType = FreezeValueDialog.HelperTypeFor(MakeMatch("EnumProperty", propSize));
        var suggested = FreezeValueDialog.SuggestedDefault(helperType);
        Assert.NotNull(FreezeValueDialog.ValidateAndConvert(suggested, helperType, out var err));
        Assert.Equal("", err);
    }

    // ------------------------------------------------------------------
    // The whole chain, end to end: the type the DIALOG validates against is the type
    // the GENERATED SCRIPT writes with. Every hop the width has to survive — the
    // dialog's mapping call, PropertySearchViewModel's params bundle, the generator's
    // own mapping — is exercised here, so dropping the size at ANY of them reds this.
    // ------------------------------------------------------------------

    [Theory]
    [InlineData("EnumProperty",   1, "uint8")]
    [InlineData("EnumProperty",   2, "uint16")]
    [InlineData("EnumProperty",   4, "int32")]
    [InlineData("EnumProperty",   8, "int64")]
    [InlineData("EnumProperty",   0, "int32")]   // size unreported → legacy default
    [InlineData("ByteProperty",   1, "uint8")]
    [InlineData("FloatProperty",  4, "float")]
    [InlineData("BoolProperty",   1, "bool")]
    [InlineData("Int64Property",  8, "int64")]
    public void DialogAndGeneratedScript_AgreeOnTheWriter(
        string propType, int propSize, string expectedHelper)
    {
        var match = MakeMatch(propType, propSize);

        // 1. What the dialog validates the user's input against.
        var helperType = FreezeValueDialog.HelperTypeFor(match);
        Assert.Equal(expectedHelper, helperType);

        // 2. A value it accepts for that type.
        var literal = FreezeValueDialog.ValidateAndConvert(
            FreezeValueDialog.SuggestedDefault(helperType), helperType, out var err);
        Assert.NotNull(literal);
        Assert.Equal("", err);

        // 3. What the script the user actually gets will write with.
        var script = FreezeScriptGenerator.Generate(
            PropertySearchViewModel.BuildFreezeParams(match, literal!));

        Assert.Contains($"valueType          = '{helperType}',", script);
        Assert.Contains($"value              = {literal},", script);
    }

    // ── Scope disclosure ([FREEZESCOPE-2026-08-18]) ─────────────────────────
    //
    // The dialog is the last screen before the script exists, and it was silent
    // about the one thing the user cannot infer from the row: the freeze is keyed
    // on the class that DECLARES the field, so "freeze my pawn's bCanBeDamaged" is
    // really "hold it on every live Actor and subclass in the level". Both strings
    // are pure statics for the same reason the validator is — the Window itself
    // needs an Avalonia runtime and cannot be exercised headless.

    [Fact]
    public void ScopeSummary_NamesTheHeldClassAndItsSubclasses()
    {
        var m = new PropertySearchMatch
        {
            ClassName = "Actor", DefiningClassName = "Actor",
            PropName = "bCanBeDamaged", PropType = "BoolProperty",
            PropOffset = 0x100, PropSize = 1, InheritedByCount = 4823,
        };

        var s = FreezeValueDialog.ScopeSummary(m);
        Assert.Contains("Actor", s);
        Assert.Contains("every subclass", s);
        Assert.Contains("4823", s);
    }

    [Fact]
    public void ScopeSummary_UsesTheClassTheScriptWillActuallyBeKeyedOn()
    {
        // The row's Class column and the class the freeze targets are not the same
        // field, and the dialog used to show the former while the generator used the
        // latter. Both now come from FreezeScriptGenerator.HeldClassName.
        var m = new PropertySearchMatch
        {
            ClassName = "BP_SpecificTeammate_C", DefiningClassName = "BP_Teammate_C",
            PropName = "Health", PropType = "FloatProperty",
            PropOffset = 0x4F8, PropSize = 4,
        };

        var s = FreezeValueDialog.ScopeSummary(m);
        Assert.Contains("BP_Teammate_C", s);
        Assert.DoesNotContain("BP_SpecificTeammate_C", s);

        // And it is the same name the emitted script keys on.
        var script = FreezeScriptGenerator.Generate(
            PropertySearchViewModel.BuildFreezeParams(m, "1.0"));
        Assert.Contains("className          = 'BP_Teammate_C',", script);
    }

    [Fact]
    public void ScopeWarning_FiresWhenTheFieldIsInherited()
    {
        var m = new PropertySearchMatch
        {
            ClassName = "Actor", DefiningClassName = "Actor",
            PropName = "bCanBeDamaged", PropType = "BoolProperty",
            PropOffset = 0x100, PropSize = 1, InheritedByCount = 4823,
        };

        var w = FreezeValueDialog.ScopeWarning(m);
        Assert.NotNull(w);
        Assert.Contains("bCanBeDamaged", w);
        Assert.Contains("Actor", w);
        // It must say how to narrow it, or it is only an apology.
        Assert.Contains("className", w);
    }

    [Fact]
    public void ScopeWarning_IsSilentForAFieldUniqueToItsClass()
    {
        // The control. A warning that fires on every row is a warning nobody reads,
        // and there is nothing surprising about freezing a game-specific field on
        // the only class that declares it.
        var m = new PropertySearchMatch
        {
            ClassName = "BP_Player_C", DefiningClassName = "BP_Player_C",
            PropName = "MyGameHealth", PropType = "FloatProperty",
            PropOffset = 0x4F8, PropSize = 4, InheritedByCount = 0,
        };

        Assert.Null(FreezeValueDialog.ScopeWarning(m));
    }

    [Fact]
    public void ScopeWarning_FiresWhenTheRowClassDiffersFromTheDefiningClass()
    {
        // Post-dedup these are equal, so this branch is defensive — but the row's
        // class is what the user READ and the defining class is what gets frozen,
        // so if the wire ever stops collapsing them the warning must not go quiet.
        var m = new PropertySearchMatch
        {
            ClassName = "BP_SpecificTeammate_C", DefiningClassName = "BP_Teammate_C",
            PropName = "Health", PropType = "FloatProperty",
            PropOffset = 0x4F8, PropSize = 4, InheritedByCount = 0,
        };

        var w = FreezeValueDialog.ScopeWarning(m);
        Assert.NotNull(w);
        Assert.Contains("BP_Teammate_C", w);
    }

    private static PropertySearchMatch MakeMatch(string propType, int propSize) => new()
    {
        ClassName         = "BP_Player_C",
        DefiningClassName = "BP_Player_C",
        PropName          = "Stance",
        PropType          = propType,
        PropOffset        = 0x2C1,
        PropSize          = propSize,
    };
}
