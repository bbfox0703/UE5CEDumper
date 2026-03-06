using System.Collections.Generic;

namespace UE5DumpUI.Models;

/// <summary>
/// A single row from a DataTable's RowMap.
/// </summary>
public sealed class DataTableRowInfo
{
    public int SparseIndex { get; init; }
    public string RowName { get; init; } = "";
    public string DataAddr { get; init; } = "";
    public List<LiveFieldValue> Fields { get; init; } = new();
}

/// <summary>
/// Result of walk_datatable_rows: DataTable row enumeration with field values.
/// </summary>
public sealed class DataTableWalkResult
{
    public int RowCount { get; init; }
    public int RowMapOffset { get; init; }
    public string RowStructAddr { get; init; } = "";
    public string RowStructName { get; init; } = "";
    public int FNameSize { get; init; }
    public int Stride { get; init; }
    public List<DataTableRowInfo> Rows { get; init; } = new();
}
