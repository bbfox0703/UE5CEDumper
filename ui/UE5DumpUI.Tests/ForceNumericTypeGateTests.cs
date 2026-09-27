using System.Text.RegularExpressions;
using UE5DumpUI.Models;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// [UINT8PROP-DEAD] The Force-value gate exists twice: <c>PropertySearchMatch.CanForceNumeric</c>
/// decides whether the UI offers it, and Solide's <c>IntWidthOf</c> / <c>IsFloatType</c> decide
/// whether the DLL will hold it. Both listed <c>UInt8Property</c>, a class no engine emits
/// (uint8 is <c>ByteProperty</c>). These read the DLL's list out of its source, so the two
/// sides are compared directly rather than each against a copy.
/// </summary>
public class ForceNumericTypeGateTests
{
    // The numeric FProperty classes UE defines. There is no UInt8Property.
    private static readonly string[] UeNumericPropertyClasses =
    {
        "ByteProperty", "Int8Property", "Int16Property", "UInt16Property", "IntProperty",
        "UInt32Property", "Int64Property", "UInt64Property", "FloatProperty", "DoubleProperty",
    };

    private static HashSet<string> DllNumericTypes()
    {
        var h = File.ReadAllText(NumericInputCoercionTests.RepoFile("dll/src/Solide.h"));
        var ints = h[h.IndexOf("inline IntWidth IntWidthOf(", StringComparison.Ordinal)..];
        ints = ints[..ints.IndexOf("return { 0, false };", StringComparison.Ordinal)];
        var set = Regex.Matches(ints, @"typeName == ""(\w+)""").Select(m => m.Groups[1].Value).ToHashSet();

        var cpp = File.ReadAllText(NumericInputCoercionTests.RepoFile("dll/src/Solide.cpp"));
        var floats = cpp[cpp.IndexOf("bool IsFloatType(", StringComparison.Ordinal)..];
        floats = floats[..floats.IndexOf('}')];
        foreach (Match m in Regex.Matches(floats, @"t == ""(\w+)"""))
            set.Add(m.Groups[1].Value);
        return set;
    }

    [Fact]
    public void The_DLL_numeric_hold_gate_names_only_real_UE_property_classes()
    {
        var dll = DllNumericTypes();
        Assert.True(dll.Count >= 4, $"parsed too few DLL types: {string.Join(", ", dll)}");
        Assert.All(dll, t => Assert.Contains(t, UeNumericPropertyClasses));
    }

    [Fact]
    public void The_UI_offers_Force_value_for_exactly_the_types_the_DLL_holds()
    {
        var dll = DllNumericTypes();
        foreach (var t in UeNumericPropertyClasses.Concat(dll).Append("UInt8Property").Distinct())
        {
            var row = new PropertySearchMatch { PropType = t };
            Assert.True(row.CanForceNumeric == dll.Contains(t),
                $"{t}: the UI offers Force value = {row.CanForceNumeric}, the DLL holds it = {dll.Contains(t)}");
        }
    }
}
