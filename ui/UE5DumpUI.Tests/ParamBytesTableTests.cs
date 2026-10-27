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
        Assert.Equal("at_call,0x00,00 00 80 3F 00 00 00 C0,\"lo=A,B\",1065353216,3221225472,1065353216,-1073741824,1,-2,"
                     + "-2.000000473111868,0xC00000003F800000", lines[1]);
        Assert.StartsWith("at_call,0x08,70 3C 12 45 F6 7F 00 00,'=Cmd,", lines[2], StringComparison.Ordinal);
        // A negative reading is a number, not a formula: it is not armoured.
        Assert.DoesNotContain("'-", lines[1], StringComparison.Ordinal);
    }

    [Fact]
    public void The_aligned_view_lines_every_column_up()
    {
        var text = ParamBytesTable.Aligned(ParamBytesTable.Rows("at_call", Bytes,
            new[] { new SnapParam { Name = "Count", Offset = 0, Size = 4 } }));
        var lines = text.Split(Environment.NewLine).Where(l => l.Length > 0).ToArray();
        Assert.Equal(5, lines.Length);
        Assert.StartsWith("copy", lines[0], StringComparison.Ordinal);
        // Every line has its separators in the same places, so the last column starts at the same place too.
        var seps = lines.Select(l => Enumerable.Range(0, l.Length - 1).Where(i => l[i] == ',' && l[i + 1] == ' ').ToArray())
                        .ToArray();
        Assert.All(seps, s => Assert.Equal(seps[0], s));
        Assert.Equal(ParamBytesTable.Columns.Length - 1, seps[0].Length);
    }
}
