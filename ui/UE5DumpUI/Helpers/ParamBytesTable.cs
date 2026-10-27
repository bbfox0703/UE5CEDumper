using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using UE5DumpUI.Models;
using UE5DumpUI.Services;

namespace UE5DumpUI.Helpers;

/// <summary>
/// [PARAM-BYTES-SEGMENTS] A parameter copy's raw bytes cut into 8-byte segments, each read the ways a reader would
/// otherwise work out by hand: two 32-bit halves as unsigned, signed and float, the whole segment as a double, and
/// the whole segment as a 64-bit address. Every reading is little-endian, as the game wrote the bytes, so the address
/// column is the byte-swapped hex a pointer is typed as and nothing is left to convert. A tail shorter than a segment
/// gets only the readings its bytes cover: a value read past the copy would be made up.
/// The cut is fixed rather than following the parameters, because the raw bytes matter most when the function's
/// layout could not be read; when it was, the params cell names the parameters each segment holds.
/// </summary>
internal static class ParamBytesTable
{
    internal const int SegmentSize = 8;
    private const int Half = SegmentSize / 2;

    /// <summary>The first cell's values, naming the copy a row's bytes come from.</summary>
    internal const string AtCall = "at_call", After = "after";

    /// <summary>The CSV's header, in the cells' order. A data format, not UI text: a spreadsheet keys on these. The
    /// params cell is last because it is the game's own text: a wide (CJK) name takes two screen cells per character,
    /// and any reading after it would slide out of its column.</summary>
    internal static readonly string[] Columns =
    {
        "copy", "offset", "raw", "u32_lo", "u32_hi", "i32_lo", "i32_hi", "f32_lo", "f32_hi", "f64", "address", "params",
    };

    private const int U32Lo = 3, F64 = 9, Address = 10, ParamsCol = 11;

    // The one reading that is not a number to a spreadsheet and starts with a formula leader.
    private static readonly string NegativeInfinity = double.NegativeInfinity.ToString(CultureInfo.InvariantCulture);

    /// <summary>The copy's segments, one row of <see cref="Columns"/> cells each; <paramref name="copy"/> is the first
    /// cell, naming which copy the bytes are. <paramref name="fields"/> is the function's layout, or null without one.</summary>
    internal static List<string[]> Rows(string copy, byte[] data, IReadOnlyList<SnapParam>? fields = null)
    {
        var rows = new List<string[]>();
        if (data.Length == 0) return rows;
        // Wide enough for the last offset, so the offsets line up and sort as text.
        int digits = Math.Max(2, ((data.Length - 1) / SegmentSize * SegmentSize).ToString("X", CultureInfo.InvariantCulture).Length);
        for (int off = 0; off < data.Length; off += SegmentSize)
        {
            var seg = data.AsSpan(off, Math.Min(SegmentSize, data.Length - off));
            var row = new string[Columns.Length];
            Array.Fill(row, "");
            row[0] = copy;
            // "0x", never "+0x": a spreadsheet takes a leading '+' for a formula.
            row[1] = "0x" + off.ToString("X" + digits, CultureInfo.InvariantCulture);
            row[2] = Spaced(Convert.ToHexString(seg));
            if (fields != null) row[ParamsCol] = Holders(fields, off, seg.Length, data.Length);
            if (seg.Length >= Half) Read32(row, U32Lo, seg[..Half]);
            if (seg.Length >= SegmentSize)
            {
                Read32(row, U32Lo + 1, seg[Half..]);
                row[F64] = BinaryPrimitives.ReadDoubleLittleEndian(seg).ToString(CultureInfo.InvariantCulture);
                // "0x" keeps a spreadsheet from reading an all-digit address as a number and dropping its zeros.
                row[Address] = "0x" + BinaryPrimitives.ReadUInt64LittleEndian(seg).ToString("X16", CultureInfo.InvariantCulture);
            }
            rows.Add(row);
        }
        return rows;
    }

    /// <summary>4 bytes as the u32, i32 and f32 cells of one half: <paramref name="col"/> is its u32 cell, and each
    /// reading's lo and hi cells sit side by side.</summary>
    private static void Read32(string[] row, int col, ReadOnlySpan<byte> b)
    {
        row[col] = BinaryPrimitives.ReadUInt32LittleEndian(b).ToString(CultureInfo.InvariantCulture);
        row[col + 2] = BinaryPrimitives.ReadInt32LittleEndian(b).ToString(CultureInfo.InvariantCulture);
        row[col + 4] = BinaryPrimitives.ReadSingleLittleEndian(b).ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>The parameters whose bytes fall in the <paramref name="segLen"/> bytes at <paramref name="segStart"/>,
    /// by where they sit: the bare name when a parameter fills the segment (a struct spanning several fills each), lo= /
    /// hi= when it fills a half, and b&lt;n&gt;= with the byte it starts at otherwise. Bytes no parameter holds
    /// (padding) are not named, and neither is a parameter the copy does not reach: a layout can run past the copy
    /// (a doubtful arm's does). One the copy's end cuts off is marked with a trailing "…".</summary>
    private static string Holders(IReadOnlyList<SnapParam> fields, int segStart, int segLen, int dataLen)
    {
        var sb = new StringBuilder();
        foreach (var p in fields)
        {
            int start = p.Offset, end = p.Offset + p.Size * Math.Max(1, p.ArrayDim);
            int s = Math.Max(start, segStart) - segStart, e = Math.Min(end, segStart + segLen) - segStart;
            if (e <= s) continue;
            if (sb.Length > 0) sb.Append(' ');
            if (s == 0 && e == SegmentSize) sb.Append(p.Name);
            else if (s == 0 && e == Half) sb.Append("lo=").Append(p.Name);
            else if (s == Half && e == SegmentSize) sb.Append("hi=").Append(p.Name);
            else sb.Append('b').Append(s.ToString(CultureInfo.InvariantCulture)).Append('=').Append(p.Name);
            if (end > dataLen) sb.Append('…');
        }
        return sb.ToString();
    }

    private static string Spaced(string hex)
    {
        var sb = new StringBuilder(hex.Length + hex.Length / 2);
        for (int i = 0; i < hex.Length; i += 2)
        {
            if (i > 0) sb.Append(' ');
            sb.Append(hex, i, 2);
        }
        return sb.ToString();
    }

    /// <summary>The rows as CSV for the clipboard: the header, then a line per row, nothing padded. The params cell
    /// carries the game's own text (its parameter names), so it is quoted and formula-armoured by the rules the Call
    /// Trace export uses. Every other cell is hex, digits or the invariant culture's NaN / Infinity / -Infinity, and
    /// only -Infinity needs the armour: a spreadsheet takes a cell that starts with '-' and is not a number for a
    /// formula. A negative number is left as it is, since the armour would turn it into text.</summary>
    internal static string Csv(IReadOnlyList<string[]> rows)
    {
        var sb = new StringBuilder();
        sb.AppendJoin(',', Columns).AppendLine();
        foreach (var r in rows)
        {
            for (int c = 0; c < r.Length; c++)
            {
                if (c > 0) sb.Append(',');
                sb.Append(c == ParamsCol ? CoordCsvCodec.Field(CoordCsvCodec.Armour(r[c]))
                        : r[c] == NegativeInfinity ? CoordCsvCodec.Armour(r[c])
                        : r[c]);
            }
            sb.AppendLine();
        }
        return sb.ToString();
    }

    /// <summary>The rows as the panel shows them: the CSV's cells and separators, padded into columns so a segment
    /// reads across a line. The readings are right-aligned, so their digits line up.</summary>
    internal static string Aligned(IReadOnlyList<string[]> rows)
    {
        if (rows.Count == 0) return "";
        var width = new int[Columns.Length];
        for (int c = 0; c < Columns.Length; c++)
        {
            width[c] = Columns[c].Length;
            foreach (var r in rows) width[c] = Math.Max(width[c], r[c].Length);
        }
        var sb = new StringBuilder();
        Line(sb, Columns, width);
        foreach (var r in rows) Line(sb, r, width);
        return sb.ToString();
    }

    private static void Line(StringBuilder sb, string[] cells, int[] width)
    {
        for (int c = 0; c < cells.Length; c++)
        {
            if (c > 0) sb.Append(", ");
            // The last cell is not padded: trailing blanks would only widen the line.
            if (c == cells.Length - 1) sb.Append(cells[c]);
            else if (c >= U32Lo && c <= F64) sb.Append(cells[c].PadLeft(width[c]));
            else sb.Append(cells[c].PadRight(width[c]));
        }
        sb.AppendLine();
    }
}
