using UE5DumpUI.Helpers;
using UE5DumpUI.Models;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>[PARAM-BYTES-SEGMENTS] The Bytes tab's segment readings. The expected values were computed independently
/// (Python's struct, little-endian), not by the code under test.</summary>
public class ParamBytesTableTests
{
    // 1.0f / -2.0f; a user-mode pointer; 1.0 as a double; a 4-byte tail.
    private static readonly byte[] Bytes =
    {
        0x00, 0x00, 0x80, 0x3F, 0x00, 0x00, 0x00, 0xC0,
        0x70, 0x3C, 0x12, 0x45, 0xF6, 0x7F, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0xF0, 0x3F,
        0x09, 0x00, 0x00, 0x00,
    };

    private static int Col(string name) => Array.IndexOf(ParamBytesTable.Columns, name);
    private static string Cell(string[] row, string name) => row[Col(name)];

    [Fact]
    public void Each_segment_is_read_little_endian_every_way_and_the_address_needs_no_swapping()
    {
        var rows = ParamBytesTable.Rows(ParamBytesTable.AtCall, Bytes);
        Assert.Equal(4, rows.Count);
        Assert.All(rows, r => Assert.Equal(ParamBytesTable.Columns.Length, r.Length));
        Assert.Equal(new[] { "0x00", "0x08", "0x10", "0x18" }, rows.Select(r => Cell(r, "offset")));
        Assert.All(rows, r => Assert.Equal("at_call", Cell(r, "copy")));

        var a = rows[0];
        Assert.Equal("00 00 80 3F 00 00 00 C0", Cell(a, "raw"));
        Assert.Equal("1065353216", Cell(a, "u32_lo"));
        Assert.Equal("3221225472", Cell(a, "u32_hi"));
        Assert.Equal("1065353216", Cell(a, "i32_lo"));
        Assert.Equal("-1073741824", Cell(a, "i32_hi"));
        Assert.Equal("1", Cell(a, "f32_lo"));
        Assert.Equal("-2", Cell(a, "f32_hi"));
        Assert.Equal("-2.000000473111868", Cell(a, "f64"));
        Assert.Equal("0xC00000003F800000", Cell(a, "address"));

        var p = rows[1];
        Assert.Equal("1158823024", Cell(p, "u32_lo"));
        Assert.Equal("32758", Cell(p, "u32_hi"));
        Assert.Equal("2339.7773", Cell(p, "f32_lo"));
        Assert.Equal("6.9512910655086E-310", Cell(p, "f64"));
        Assert.Equal("0x00007FF645123C70", Cell(p, "address"));   // the bytes reversed: the pointer as typed

        var d = rows[2];
        Assert.Equal("1", Cell(d, "f64"));
        Assert.Equal("0", Cell(d, "f32_lo"));
        Assert.Equal("1.875", Cell(d, "f32_hi"));
        Assert.Equal("0x3FF0000000000000", Cell(d, "address"));
    }

    [Fact]
    public void A_short_last_segment_gets_only_the_readings_its_bytes_cover()
    {
        var tail = ParamBytesTable.Rows(ParamBytesTable.After, Bytes)[3];
        Assert.Equal("after", Cell(tail, "copy"));
        Assert.Equal("09 00 00 00", Cell(tail, "raw"));
        Assert.Equal("9", Cell(tail, "u32_lo"));
        Assert.Equal("9", Cell(tail, "i32_lo"));
        foreach (var c in new[] { "u32_hi", "i32_hi", "f32_hi", "f64", "address" })
            Assert.Equal("", Cell(tail, c));

        var three = Assert.Single(ParamBytesTable.Rows("after", new byte[] { 1, 2, 3 }));
        Assert.Equal("01 02 03", Cell(three, "raw"));
        foreach (var c in ParamBytesTable.Columns.Skip(Col("u32_lo")))
            Assert.Equal("", Cell(three, c));

        Assert.Empty(ParamBytesTable.Rows("after", Array.Empty<byte>()));
        Assert.Equal("", ParamBytesTable.Aligned(ParamBytesTable.Rows("after", Array.Empty<byte>())));
    }

    [Fact]
    public void The_offset_column_is_as_wide_as_the_last_offset()
    {
        var rows = ParamBytesTable.Rows("at_call", new byte[0x101]);
        Assert.Equal("0x000", Cell(rows[0], "offset"));
        Assert.Equal("0x100", Cell(rows[^1], "offset"));
    }

    [Fact]
    public void The_params_cell_names_what_each_segment_holds_and_where()
    {
        var fields = new[]
        {
            new SnapParam { Name = "Count", Offset = 0, Size = 4 },
            new SnapParam { Name = "Speed", Offset = 4, Size = 4 },
            new SnapParam { Name = "Target", Offset = 8, Size = 8 },
            new SnapParam { Name = "Location", Offset = 16, Size = 4, ArrayDim = 3 },   // 12 bytes: one segment and a half
        };
        var rows = ParamBytesTable.Rows("at_call", Bytes, fields);
        Assert.Equal(new[] { "lo=Count hi=Speed", "Target", "Location", "lo=Location" }, rows.Select(r => Cell(r, "params")));

        // Smaller than a half: the byte it starts at. Padding is not named.
        var small = new[]
        {
            new SnapParam { Name = "bA", Offset = 0, Size = 1 },
            new SnapParam { Name = "bB", Offset = 1, Size = 1 },
            new SnapParam { Name = "Value", Offset = 4, Size = 4 },
        };
        Assert.Equal("b0=bA b1=bB hi=Value", Cell(ParamBytesTable.Rows("at_call", new byte[8], small)[0], "params"));
        Assert.Equal("b2=Short", Cell(ParamBytesTable.Rows("at_call", new byte[8],
            new[] { new SnapParam { Name = "Short", Offset = 2, Size = 2 } })[0], "params"));

        // No layout: nothing to name.
        Assert.All(ParamBytesTable.Rows("at_call", Bytes), r => Assert.Equal("", Cell(r, "params")));
    }

    [Fact]
    public void The_CSV_is_unpadded_with_a_header_and_quotes_only_the_games_own_names()
    {
        var fields = new[]
        {
            new SnapParam { Name = "A,B", Offset = 0, Size = 4 },
            new SnapParam { Name = "=Cmd", Offset = 8, Size = 8 },
        };
        var csv = ParamBytesTable.Csv(ParamBytesTable.Rows("at_call", Bytes, fields));
        var lines = csv.Split(Environment.NewLine);
        Assert.Equal(string.Join(",", ParamBytesTable.Columns), lines[0]);
        Assert.Equal(6, lines.Length);   // the header, four segments, and the empty string after the last line break
        Assert.Equal("", lines[^1]);
        Assert.Equal("at_call,0x00,00 00 80 3F 00 00 00 C0,1065353216,3221225472,1065353216,-1073741824,1,-2,"
                     + "-2.000000473111868,0xC00000003F800000,\"lo=A,B\"", lines[1]);
        Assert.StartsWith("at_call,0x08,70 3C 12 45 F6 7F 00 00,", lines[2], StringComparison.Ordinal);
        Assert.EndsWith(",0x00007FF645123C70,'=Cmd", lines[2], StringComparison.Ordinal);
        // A negative reading is a number, not a formula: it is not armoured.
        Assert.DoesNotContain("'-", lines[1], StringComparison.Ordinal);
    }

    [Fact]
    public void Negative_infinity_is_armoured_in_the_CSV_because_a_spreadsheet_takes_it_for_a_formula()
    {
        // f32 -inf, f32 NaN; then f64 -inf. "-Infinity" starts with '-' and is not a number to a spreadsheet.
        var bytes = new byte[] { 0x00, 0x00, 0x80, 0xFF, 0x00, 0x00, 0xC0, 0x7F,
                                 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0xF0, 0xFF };
        var rows = ParamBytesTable.Rows("at_call", bytes);
        Assert.Equal("-Infinity", Cell(rows[0], "f32_lo"));       // the view shows the reading as it is
        Assert.Equal("NaN", Cell(rows[0], "f32_hi"));
        Assert.Equal("-Infinity", Cell(rows[1], "f64"));
        var lines = ParamBytesTable.Csv(rows).Split(Environment.NewLine);
        var cells = lines.Skip(1).Take(2).Select(l => l.Split(',')).ToArray();
        Assert.Equal("'-Infinity", cells[0][Col("f32_lo")]);
        Assert.Equal("NaN", cells[0][Col("f32_hi")]);
        Assert.Equal("'-Infinity", cells[1][Col("f64")]);
        Assert.Equal("-1048576", cells[1][Col("i32_hi")]);         // a negative number stays a number
    }

    [Fact]
    public void The_params_cell_names_only_bytes_the_copy_holds_and_marks_a_parameter_the_copy_cuts_off()
    {
        // A 9-byte copy (a layout that runs past the copy is what a doubtful arm carries): X has one byte in it, Y none.
        var fields = new[]
        {
            new SnapParam { Name = "Obj", Offset = 0, Size = 8 },
            new SnapParam { Name = "X", Offset = 8, Size = 4 },
            new SnapParam { Name = "Y", Offset = 12, Size = 4 },
        };
        var rows = ParamBytesTable.Rows("at_call", new byte[9], fields);
        Assert.Equal(new[] { "Obj", "b0=X…" }, rows.Select(r => Cell(r, "params")));

        // A whole segment that a longer parameter carries on past the copy's end.
        var big = ParamBytesTable.Rows("at_call", new byte[16], new[] { new SnapParam { Name = "Big", Offset = 8, Size = 16 } });
        Assert.Equal(new[] { "", "Big…" }, big.Select(r => Cell(r, "params")));
        // A half that ends the copy exactly is whole: nothing is cut.
        var half = ParamBytesTable.Rows("at_call", new byte[12], new[] { new SnapParam { Name = "Z", Offset = 8, Size = 4 } });
        Assert.Equal("lo=Z", Cell(half[1], "params"));
    }

    [Fact]
    public void The_aligned_view_lines_every_column_up()
    {
        // The names are the game's own text: a wide (CJK) name must not push the readings out of line.
        var text = ParamBytesTable.Aligned(ParamBytesTable.Rows("at_call", Bytes,
            new[] { new SnapParam { Name = "Count", Offset = 0, Size = 4 }, new SnapParam { Name = "速度", Offset = 8, Size = 8 } }));
        var lines = text.Split(Environment.NewLine).Where(l => l.Length > 0).ToArray();
        Assert.Equal(5, lines.Length);
        Assert.StartsWith("copy", lines[0], StringComparison.Ordinal);
        // Every line has its separators in the same places, so the last column starts at the same place too.
        var seps = lines.Select(l => Enumerable.Range(0, l.Length - 1).Where(i => l[i] == ',' && l[i + 1] == ' ').ToArray())
                        .ToArray();
        Assert.All(seps, s => Assert.Equal(seps[0], s));
        Assert.Equal(ParamBytesTable.Columns.Length - 1, seps[0].Length);
        // Equal string offsets are not equal screen columns once a wide character comes first: a CJK glyph takes two
        // cells of a monospace font. So no game text may stand before a reading: the names come last.
        Assert.All(lines, l => Assert.True(l[..l.LastIndexOf(", ", StringComparison.Ordinal)].All(ch => ch < 0x80), l));
    }
}
