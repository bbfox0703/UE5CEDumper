using System.Linq;
using System.Text;
using UE5DumpUI.Models;

namespace UE5DumpUI.Services;

/// <summary>
/// Pure, AOT-safe DataTable-native pivot. A DataTable is already a
/// RowName → struct map, so the pivot is <b>zero-config</b>: each row is its own
/// group keyed by RowName (always unique → Count 1, never a collision) and the
/// row struct's fields are the projected columns. No key discovery needed —
/// RowName <i>is</i> the key. Reuses the snapshot pivot's <see cref="PivotResultRow"/>
/// / results grid + CE handoff (Copy Address / HEX; a row is struct data, not a UObject). Kept separate
/// from the ViewModel so the rendering is unit-testable without a live pipe. See
/// docs/experimental-snapshot-spc-pivot.md §"Phase C — C4".
/// </summary>
public static class DataTablePivotEngine
{
    /// <summary>
    /// Build pivot rows from a DataTable walk: one row per DataTable row, keyed by
    /// RowName, projecting the selected value fields (all row fields when the
    /// selection is empty). The row's struct data address is the CE handoff target.
    /// </summary>
    public static PivotResult Build(DataTableWalkResult dt, IReadOnlyList<string> valueFields)
    {
        var result = new PivotResult
        {
            InstanceCount = dt.Rows.Count,
            GroupCount    = dt.Rows.Count,   // RowName is unique → one group per row
        };
        foreach (var row in dt.Rows)
        {
            result.Rows.Add(new PivotResultRow
            {
                KeyValue      = string.IsNullOrEmpty(row.RowName) ? "(unnamed)" : row.RowName,
                Count         = 1,
                ObjAddr       = row.DataAddr,
                IsDataTableRow = true,
                ValuesDisplay = RenderRow(row, valueFields),
            });
        }
        return result;
    }

    private static string RenderRow(DataTableRowInfo row, IReadOnlyList<string> valueFields)
    {
        var fields = valueFields.Count == 0
            ? (IEnumerable<LiveFieldValue>)row.Fields
            : valueFields
                .Select(n => row.Fields.FirstOrDefault(f => f.Name == n))
                .Where(f => f != null)
                .Select(f => f!);

        var sb = new StringBuilder();
        foreach (var f in fields)
        {
            if (sb.Length > 0) sb.Append("   ·   ");
            sb.Append(f.Name).Append('=').Append(f.DisplayValue);
        }
        return sb.ToString();
    }

    /// <summary>
    /// Distinct struct field descriptors across all rows, for the value-field
    /// picker (reuses the snapshot pivot's <see cref="PivotFieldInfo"/> grid).
    /// <see cref="PivotFieldInfo.DistinctCount"/> counts distinct rendered values;
    /// <see cref="PivotFieldInfo.InstanceCount"/> is how many rows carry the field.
    /// First-seen order is preserved for a stable picker.
    /// </summary>
    public static IReadOnlyList<PivotFieldInfo> Fields(DataTableWalkResult dt)
    {
        var order    = new List<string>();
        var types    = new Dictionary<string, string>();
        var distinct = new Dictionary<string, HashSet<string>>();
        var counts   = new Dictionary<string, int>();

        foreach (var row in dt.Rows)
            foreach (var f in row.Fields)
            {
                if (!types.ContainsKey(f.Name))
                {
                    order.Add(f.Name);
                    types[f.Name]    = f.TypeName;
                    distinct[f.Name] = new HashSet<string>();
                    counts[f.Name]   = 0;
                }
                distinct[f.Name].Add(string.IsNullOrEmpty(f.HexValue) ? f.DisplayValue : f.HexValue);
                counts[f.Name]++;
            }

        return order.Select(n => new PivotFieldInfo
        {
            Name          = n,
            DeclaredType  = types[n],
            DistinctCount = distinct[n].Count,
            InstanceCount = counts[n],
        }).ToList();
    }
}
