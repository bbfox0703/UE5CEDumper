using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UE5DumpUI.Models;
using UE5DumpUI.Services;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// P2 CSV round-trip — docs/teleport-coord-library-spec.md §5.
/// Every test here corresponds to a specific way a 4000-row hand-curated list gets
/// silently corrupted; none of them are hypothetical.
/// </summary>
public class CoordCsvWriteTests
{
    private static CoordEntry E(string label, string group = "", string map = "M",
                               double x = 0, double y = 0, double z = 0)
        => new() { Uid = "u1", Label = label, Group = group, Map = map, X = x, Y = y, Z = z };

    [Fact]
    public void Write_EmitsHeaderFirst()
    {
        var csv = CoordCsvCodec.Write(new[] { E("A") });
        Assert.StartsWith("uid,label,group,map,x,y,z,pitch,yaw,roll\n", csv);
    }

    [Fact]
    public void Write_UsesLfNotCrLf()
    {
        var csv = CoordCsvCodec.Write(new[] { E("A") });
        Assert.DoesNotContain("\r", csv);
    }

    [Fact]
    public void Write_QuotesAnEmbeddedComma()
    {
        // Without quoting, this one comma shifts every later column on re-import and
        // the entry is lost from the library with no diagnostic.
        var csv = CoordCsvCodec.Write(new[] { E("Chest, big one", "Chest") });
        Assert.Contains("\"Chest, big one\"", csv);
    }

    [Fact]
    public void Write_DoublesAnEmbeddedQuote()
    {
        var csv = CoordCsvCodec.Write(new[] { E("He said \"hi\"") });
        Assert.Contains("\"He said \"\"hi\"\"\"", csv);
    }

    [Fact]
    public void Write_DoesNotQuoteOrdinaryValues()
    {
        var csv = CoordCsvCodec.Write(new[] { E("Chest 1", "Chest") });
        Assert.Contains("u1,Chest 1,Chest,M,", csv);
    }

    [Fact]
    public void Write_KeepsCjkVerbatim()
    {
        var csv = CoordCsvCodec.Write(new[] { E("寶箱 3（上層）", "寶箱") });
        Assert.Contains("寶箱 3（上層）", csv);
    }

    [Theory]
    [InlineData("=Boss Arena")]
    [InlineData("+1 Chest")]
    [InlineData("-- boss room")]
    [InlineData("@home")]
    public void Write_ArmoursFormulaLeadingLabels(string label)
    {
        // Excel evaluates these. "=Boss Arena" displays #NAME?, and Excel SAVES the
        // displayed text -- so without armouring the label is destroyed on a round
        // trip through a spreadsheet, with no error anywhere.
        var csv = CoordCsvCodec.Write(new[] { E(label) });
        var dataLine = csv.Split('\n')[1];
        Assert.Contains("'" + label.TrimStart('"'), dataLine.Replace("\"", ""));
    }

    [Fact]
    public void ArmourThenUnarmour_IsTransparent()
    {
        foreach (var s in new[] { "=x", "+x", "-x", "@x", "plain", "", "a=b", "'quoted" })
            Assert.Equal(s, CoordCsvCodec.Unarmour(CoordCsvCodec.Armour(s)));
    }

    [Fact]
    public void Write_UsesTheCanonicalPrecision()
    {
        var csv = CoordCsvCodec.Write(new[] { E("A", x: CoordPrecision.Round(67162.3984375)) });
        Assert.Contains(",67162.398,", csv);
    }

    [Fact]
    public void WriteFile_EmitsAUtf8Bom()
    {
        // Deliberate exception to the repo's BOM-less rule: without it, Excel on a
        // zh-TW machine reads the file as ANSI and every CJK label is mojibake.
        var path = Path.Combine(Path.GetTempPath(), "ue5cd-bom-" + Guid.NewGuid().ToString("N")[..8] + ".csv");
        try
        {
            CoordCsvCodec.WriteFile(path, new[] { E("寶箱") });
            var bytes = File.ReadAllBytes(path);
            Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes.Take(3).ToArray());
        }
        finally { try { File.Delete(path); } catch { } }
    }
}

public class CoordCsvParseTests
{
    private static CoordCsvParseResult P(string csv) => CoordCsvCodec.Parse(csv);

    private const string Header = "uid,label,group,map,x,y,z,pitch,yaw,roll\n";

    [Fact]
    public void Parse_ReadsACleanRow()
    {
        var r = P(Header + "k3f9,Chest 1,Chest,Map01,87668.672,-24858.674,341.376,335.01,26.16,0\n");
        var e = Assert.Single(r.Entries);
        Assert.Equal("k3f9", e.Uid);
        Assert.Equal("Chest 1", e.Label);
        Assert.Equal("Map01", e.Map);
        Assert.Equal(87668.672, e.X);
        Assert.Equal(26.16, e.Yaw);
        Assert.Empty(r.Issues);
    }

    [Fact]
    public void Parse_EmptyGroup_DoesNotShiftColumns()
    {
        // THE regression this whole codec exists to avoid. The repo's only existing
        // delimited reader (BugItGoParser) splits with RemoveEmptyEntries, which here
        // would collapse 10 fields to 9 and make Group="Map02", Map="89828.133".
        var r = P(Header + "k3fb,Boss,,Map02,89828.133,-15534.243,850.328,346.88,0.93,0\n");
        var e = Assert.Single(r.Entries);
        Assert.Equal("Boss", e.Label);
        Assert.Equal("", e.Group);
        Assert.Equal("Map02", e.Map);
        Assert.Equal(89828.133, e.X);
    }

    [Fact]
    public void Parse_QuotedCommaStaysInOneField()
    {
        var r = P(Header + "u,\"Chest, big one\",Chest,Map02,1.5,-2.25,0,0,0,0\n");
        var e = Assert.Single(r.Entries);
        Assert.Equal("Chest, big one", e.Label);
        Assert.Equal("Chest", e.Group);
    }

    [Fact]
    public void Parse_DoubledQuoteIsUnescaped()
    {
        var r = P(Header + "u,\"He said \"\"hi\"\"\",Other,M,0,0,0,0,0,0\n");
        Assert.Equal("He said \"hi\"", Assert.Single(r.Entries).Label);
    }

    [Fact]
    public void Parse_StripsFormulaArmour()
    {
        var r = P(Header + "u,'=Boss Arena,Other,M,0,0,0,0,0,0\n");
        Assert.Equal("=Boss Arena", Assert.Single(r.Entries).Label);
    }

    [Fact]
    public void Parse_MatchesColumnsByNameNotPosition()
    {
        // Reordered + extra columns must both work -- the forward-compatibility that
        // makes a v2 column safe to add.
        var r = P("label,z,y,x,map,notes\nChest 1,3,2,1,Map01,ignore me\n");
        var e = Assert.Single(r.Entries);
        Assert.Equal("Chest 1", e.Label);
        Assert.Equal(1, e.X);
        Assert.Equal(2, e.Y);
        Assert.Equal(3, e.Z);
        Assert.Equal("Map01", e.Map);
    }

    [Fact]
    public void Parse_MissingRotationColumns_DefaultToZero()
    {
        var r = P("label,map,x,y,z\nA,M,1,2,3\n");
        var e = Assert.Single(r.Entries);
        Assert.Equal(0, e.Pitch);
        Assert.Equal(0, e.Yaw);
    }

    [Fact]
    public void Parse_MissingRequiredColumn_ReportsAndStops()
    {
        var r = P("label,group\nA,B\n");
        Assert.Empty(r.Entries);
        Assert.Contains(r.Issues, i => i.Reason.Contains("required column 'x'"));
    }

    [Fact]
    public void Parse_AcceptsCrLf()
    {
        // Excel always writes CRLF; a tool that rejected its own re-saved file would
        // be indefensible.
        var r = P("uid,label,map,x,y,z\r\nu,A,M,1,2,3\r\n");
        Assert.Single(r.Entries);
    }

    [Fact]
    public void Parse_ToleratesAUtf8Bom()
    {
        var r = P("﻿" + "label,map,x,y,z\nA,M,1,2,3\n");
        Assert.Single(r.Entries);
    }

    [Fact]
    public void Parse_SniffsSemicolonDelimiterAndCommaDecimals()
    {
        // What a de-DE / fr-FR Excel writes when it re-saves our file.
        var r = P("uid;label;group;map;x;y;z\nu;Boss;;Map02;89828,133;-15534,243;850,328\n");
        Assert.Equal(';', r.Delimiter);
        var e = Assert.Single(r.Entries);
        Assert.Equal(89828.133, e.X);
        Assert.Equal(-15534.243, e.Y);
    }

    [Fact]
    public void Parse_SniffsTabDelimiter()
    {
        var r = P("label\tmap\tx\ty\tz\nA\tM\t1\t2\t3\n");
        Assert.Equal('\t', r.Delimiter);
        Assert.Single(r.Entries);
    }

    [Fact]
    public void Parse_UnparseableNumber_SkipsRowWithLineNumber()
    {
        var r = P(Header +
                  "u1,Good,G,M,1,2,3,0,0,0\n" +
                  "u2,Bad,G,M,not-a-number,2,3,0,0,0\n" +
                  "u3,Good2,G,M,4,5,6,0,0,0\n");
        Assert.Equal(2, r.Entries.Count);                       // partial success
        var issue = Assert.Single(r.Issues);
        Assert.Equal(3, issue.Line);                            // 1-based, header is line 1
        Assert.Equal("x", issue.Column);
        Assert.Contains("not a number", issue.Reason);
    }

    // [A3-COORD-NONFINITE] double.TryParse ACCEPTS "NaN" / "Infinity" / an overflow like "1e400", and Round mapped them to
    // 0 without an issue -- a teleport to the world origin, the silent wrong coordinate B21 says must be a visible row.
    [Theory]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("-Infinity")]
    [InlineData("1e400")]   // overflows to +Infinity
    public void Parse_NonFiniteCoordinate_IsARejectedRow_NotAnOriginTeleport(string x)
    {
        var r = P(Header + $"u1,Bad,G,M,{x},2,3,0,0,0\n");
        Assert.Empty(r.Entries);
        var issue = Assert.Single(r.Issues);
        Assert.Equal("x", issue.Column);
    }

    [Fact]
    public void Parse_EmptyLabel_IsReportedNotSilentlyImported()
    {
        var r = P(Header + "u,,G,M,1,2,3,0,0,0\n");
        Assert.Empty(r.Entries);
        Assert.Contains(r.Issues, i => i.Reason.Contains("label is empty"));
    }

    [Fact]
    public void Parse_ControlCharsAreStrippedAndReported()
    {
        // Reachable from Excel: Alt+Enter writes a quoted field containing CRLF, and
        // a raw newline in a Lua single-quoted literal is a compile error -- so the
        // generated AA script would fail to load in CE.
        var r = P(Header + "u,\"Chest 3\r\n(upper floor)\",G,M,1,2,3,0,0,0\n");
        var e = Assert.Single(r.Entries);
        Assert.DoesNotContain("\n", e.Label);
        Assert.DoesNotContain("\r", e.Label);
        Assert.Contains(r.Issues, i => i.Column == "label" && i.Reason.Contains("control characters"));
    }

    [Fact]
    public void Parse_BlankLinesAreSkippedSilently()
    {
        var r = P(Header + "u,A,G,M,1,2,3,0,0,0\n\n\nu2,B,G,M,4,5,6,0,0,0\n");
        Assert.Equal(2, r.Entries.Count);
        Assert.Empty(r.Issues);
    }

    [Fact]
    public void Parse_EmptyInput_ReportsRatherThanThrowing()
    {
        Assert.Contains(P("").Issues, i => i.Reason.Contains("empty"));
        Assert.Contains(CoordCsvCodec.Parse(null).Issues, i => i.Reason.Contains("empty"));
    }

    [Fact]
    public void Parse_RoundsToTheCanonicalPrecision()
    {
        var r = P("label,map,x,y,z\nA,M,67162.3984375,0,0\n");
        Assert.Equal(67162.398, Assert.Single(r.Entries).X);
    }
}

public class CoordCsvRoundTripTests
{
    [Fact]
    public void WriteThenParse_IsLosslessAcrossTheHardCases()
    {
        var original = new List<CoordEntry>
        {
            new() { Uid="k3f9", Label="Mountain", Group="Fields", Map="Map01",
                    X=CoordPrecision.Round(67162.3984375), Y=CoordPrecision.Round(-20380.642578125),
                    Z=CoordPrecision.Round(35.79132080078125), Pitch=347.71, Yaw=31.98, Roll=0 },
            new() { Uid="k3fb", Label="Boss", Group="", Map="Map02",
                    X=89828.133, Y=-15534.243, Z=850.328, Pitch=346.88, Yaw=0.93, Roll=0 },
            new() { Uid="k3fc", Label="Chest, big one", Group="Chest", Map="Map02",
                    X=1.5, Y=-2.25, Z=0, Pitch=0, Yaw=0, Roll=0 },
            new() { Uid="k3fd", Label="He said \"hi\"", Group="Other", Map="Map02" },
            new() { Uid="k3fe", Label="寶箱 3（上層）", Group="寶箱", Map="Map03",
                    X=-4096.5, Y=8192.25, Z=120, Pitch=0, Yaw=180, Roll=0 },
            new() { Uid="k3ff", Label="=Boss Arena", Group="Other", Map="Map03",
                    X=10, Y=20, Z=30 },
            new() { Uid="k400", Label="1-2", Group="Zones", Map="Map03",
                    X=CoordPrecision.Round(1e-5), Y=0, Z=100000 },
        };

        var parsed = CoordCsvCodec.Parse(CoordCsvCodec.Write(original));

        Assert.Empty(parsed.Issues);
        Assert.Equal(original.Count, parsed.Entries.Count);
        for (int i = 0; i < original.Count; i++)
        {
            var a = original[i];
            var b = parsed.Entries[i];
            Assert.Equal(a.Uid, b.Uid);
            Assert.Equal(a.Label, b.Label);
            Assert.Equal(a.Group, b.Group);
            Assert.Equal(a.Map, b.Map);
            Assert.Equal(a.X, b.X);
            Assert.Equal(a.Y, b.Y);
            Assert.Equal(a.Z, b.Z);
            Assert.Equal(a.Pitch, b.Pitch);
            Assert.Equal(a.Yaw, b.Yaw);
            Assert.Equal(a.Roll, b.Roll);
        }
    }

    [Fact]
    public void WriteParseWrite_IsByteIdentical()
    {
        // Generate -> Parse -> Generate stability. AOBMaker only wrote this test for
        // one of its three generators and the other drifted out of idempotence.
        var entries = new List<CoordEntry>
        {
            new() { Uid="a", Label="Chest, 1", Group="G", Map="M",
                    X=CoordPrecision.Round(67162.3984375), Yaw=31.98 },
            new() { Uid="b", Label="=formula", Group="", Map="M2", Z=1e-5 },
        };
        var first = CoordCsvCodec.Write(entries);
        var second = CoordCsvCodec.Write(CoordCsvCodec.Parse(first).Entries);
        Assert.Equal(first, second);
    }

    [Fact]
    public void SampleTemplate_ParsesBackCleanly()
    {
        var parsed = CoordCsvCodec.Parse(CoordCsvCodec.SampleTemplate());
        Assert.Empty(parsed.Issues);
        Assert.Equal(3, parsed.Entries.Count);
        Assert.Equal("", parsed.Entries[2].Group);      // the empty-group sample row
    }
}

public class CoordCsvDiffTests
{
    private static CoordEntry E(string uid, string label, string group = "G", string map = "M",
                                double x = 0)
        => new() { Uid = uid, Label = label, Group = group, Map = map, X = x };

    [Fact]
    public void Diff_MatchesByUidFirst()
    {
        // Identity must survive a spreadsheet rewriting the label, or a merge
        // duplicates the row instead of updating it.
        var existing = new[] { E("k1", "Old name") };
        var incoming = new[] { E("k1", "New name") };
        var change = Assert.Single(CoordCsvCodec.Diff(existing, incoming, replace: false));
        Assert.Equal(CoordChangeKind.Changed, change.Kind);
        Assert.Contains(change.FieldDiffs, d => d.Contains("Old name → New name"));
    }

    [Fact]
    public void Diff_FallsBackToLabelGroupMapCaseInsensitively()
    {
        var existing = new[] { E("k1", "Chest 1", "Chest", "Map01") };
        var incoming = new[] { new CoordEntry { Label = "chest 1", Group = "CHEST", Map = "map01" } };
        var change = Assert.Single(CoordCsvCodec.Diff(existing, incoming, replace: false));
        Assert.NotEqual(CoordChangeKind.Added, change.Kind);
    }

    [Fact]
    public void Diff_NeverMatchesOnCoordinatesAlone()
    {
        // Deliberate: Excel rewrites coordinate text, so coordinate equality is not a
        // usable identity. Same position, different name = a genuinely new entry.
        var existing = new[] { E("k1", "Chest 1", "Chest", "Map01", x: 100) };
        var incoming = new[] { E("", "Totally different", "Other", "Map01", x: 100) };
        var change = Assert.Single(CoordCsvCodec.Diff(existing, incoming, replace: false));
        Assert.Equal(CoordChangeKind.Added, change.Kind);
    }

    [Fact]
    public void Diff_IdenticalRowIsUnchangedNotChanged()
    {
        var e = E("k1", "Chest 1");
        var change = Assert.Single(CoordCsvCodec.Diff(new[] { e }, new[] { e.Clone() }, replace: false));
        Assert.Equal(CoordChangeKind.Unchanged, change.Kind);
        Assert.Empty(change.FieldDiffs);
    }

    [Fact]
    public void Diff_MergeModeDoesNotReportRemovals()
    {
        var existing = new[] { E("k1", "Keep me") };
        var changes = CoordCsvCodec.Diff(existing, Array.Empty<CoordEntry>(), replace: false);
        Assert.Empty(changes);
    }

    [Fact]
    public void Diff_ReplaceModeReportsRemovals()
    {
        var existing = new[] { E("k1", "Will be deleted") };
        var change = Assert.Single(CoordCsvCodec.Diff(existing, Array.Empty<CoordEntry>(), replace: true));
        Assert.Equal(CoordChangeKind.Removed, change.Kind);
        Assert.Equal("Will be deleted", change.Label);
    }

    [Fact]
    public void Diff_SurfacesAnExcelDateMangling()
    {
        // The corruption no validator can detect: Excel turns the group "1-2" into a
        // date and saves the displayed text. Both files are valid CSV, so the ONLY
        // defence is showing this line to the user before committing.
        var existing = new[] { E("k1", "Chest 1", group: "1-2") };
        var incoming = new[] { E("k1", "Chest 1", group: "2001/1/2") };
        var change = Assert.Single(CoordCsvCodec.Diff(existing, incoming, replace: false));
        Assert.Equal(CoordChangeKind.Changed, change.Kind);
        Assert.Contains(change.FieldDiffs, d => d == "Group: 1-2 → 2001/1/2");
    }

    [Fact]
    public void Diff_ReportsNumericChangesInCanonicalText()
    {
        var existing = new[] { E("k1", "A", x: 1.5) };
        var incoming = new[] { E("k1", "A", x: 2.25) };
        var change = Assert.Single(CoordCsvCodec.Diff(existing, incoming, replace: false));
        Assert.Contains("X: 1.5 → 2.25", change.FieldDiffs);
    }

    [Fact]
    public void Diff_TwoIncomingRowsDoNotBothClaimTheSameExisting()
    {
        var existing = new[] { E("", "Chest 1", "Chest", "Map01") };
        var incoming = new[]
        {
            new CoordEntry { Label = "Chest 1", Group = "Chest", Map = "Map01", X = 1 },
            new CoordEntry { Label = "Chest 1", Group = "Chest", Map = "Map01", X = 2 },
        };
        var changes = CoordCsvCodec.Diff(existing, incoming, replace: false);
        Assert.Equal(2, changes.Count);
        Assert.Equal(1, changes.Count(c => c.Kind == CoordChangeKind.Changed));
        Assert.Equal(1, changes.Count(c => c.Kind == CoordChangeKind.Added));
    }

    [Fact]
    public void SummarizeDiff_ReadsLikeAConsentPrompt()
    {
        var existing = new[] { E("k1", "Same"), E("k2", "Will change"), E("k3", "Will go") };
        var incoming = new[] { E("k1", "Same"), E("k2", "Changed"), E("k9", "Brand new") };
        var s = CoordCsvCodec.SummarizeDiff(CoordCsvCodec.Diff(existing, incoming, replace: true));
        Assert.Contains("1 added", s);
        Assert.Contains("1 changed", s);
        Assert.Contains("1 unchanged", s);
        Assert.Contains("1 removed", s);
    }

    [Fact]
    public void SummarizeDiff_EmptyIsStatedNotBlank()
        => Assert.Equal("no changes", CoordCsvCodec.SummarizeDiff(Array.Empty<CoordChange>()));
}

public class CoordCsvFieldSplitTests
{
    [Theory]
    [InlineData("a,b,c", 3)]
    [InlineData("a,,c", 3)]          // the empty-group case
    [InlineData(",,", 3)]
    [InlineData("a", 1)]
    [InlineData("", 1)]
    public void SplitCsvLine_KeepsEmptyFields(string line, int expected)
        => Assert.Equal(expected, CoordCsvCodec.SplitCsvLine(line, ',').Count);

    [Fact]
    public void SplitCsvLine_HandlesQuotedDelimiterAndEscapedQuote()
    {
        var cells = CoordCsvCodec.SplitCsvLine("a,\"b,c\",\"d\"\"e\"", ',');
        Assert.Equal(new[] { "a", "b,c", "d\"e" }, cells);
    }

    [Fact]
    public void SniffDelimiter_PrefersCommaOnATie()
        => Assert.Equal(',', CoordCsvCodec.SniffDelimiter("a,b"));

    [Fact]
    public void SniffDelimiter_PicksTheRicherSplit()
    {
        Assert.Equal(';', CoordCsvCodec.SniffDelimiter("a;b;c;d"));
        Assert.Equal('\t', CoordCsvCodec.SniffDelimiter("a\tb\tc\td"));
    }

    /// <summary>
    /// B21 — a European decimal comma must NOT parse as a grouped-thousands number.
    /// "67162,398" used to become 67162398: a coordinate a thousand times out, accepted with
    /// no issue raised, because the decimal-comma repair only ran for ';'-delimited files.
    /// The accepted cost is that Excel's grouped "67,162.398" is now rejected — a rejected row
    /// is visible in the import preview; a silently mis-scaled one is not.
    /// </summary>
    [Theory]
    [InlineData("67162,398")]      // European decimal comma — the bug
    [InlineData("67,162.398")]     // Excel grouped thousands — the accepted casualty
    [InlineData("1,234")]
    public void CoordPrecision_RejectsAnyCommaInANumber(string text)
        => Assert.False(CoordPrecision.TryParse(text, out _));

    /// <summary>The formats that MUST keep working, including the exponent form our own
    /// exporter emits below 1e-5.</summary>
    [Theory]
    [InlineData("67162.398", 67162.398)]
    [InlineData("-1234.5", -1234.5)]
    [InlineData("1E-05", 1e-5)]
    [InlineData("  42  ", 42.0)]
    public void CoordPrecision_StillParsesInvariantForms(string text, double expected)
    {
        Assert.True(CoordPrecision.TryParse(text, out double v));
        Assert.Equal(expected, v, 9);
    }
}
