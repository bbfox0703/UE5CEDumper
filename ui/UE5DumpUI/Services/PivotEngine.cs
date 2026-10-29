using System.Linq;
using System.Text;
using UE5DumpUI.Models;

namespace UE5DumpUI.Services;

/// <summary>
/// Pure, AOT-safe Class Pivot core: groups a class's captured instances by
/// intrinsic identity (normalised path) or by a chosen key field's value, then
/// projects the requested value fields per group. Differing values inside one
/// group render as a collision <c>⟨N: v1,v2,+M⟩</c> (ported from the Unity sister
/// project's 29e-2 polish). Kept separate from <see cref="SnapshotStore"/> so the
/// grouping/rendering is unit-testable without a database. See
/// docs/experimental-snapshot-spc-pivot.md §"Phase C".
/// </summary>
public static class PivotEngine
{
    // One instance's captured fields (field label -> (type, hex)).
    private sealed class Instance
    {
        public string NormPath = "";
        public string ObjAddr = "";
        public readonly Dictionary<string, (string type, string hex)> Fields = new();
    }

    public static PivotResult Build(IReadOnlyList<PivotInputRow> rows, PivotQuery q)
    {
        var result = new PivotResult();

        // 1. Fold the (instance, field) rows into per-instance records,
        //    preserving first-seen instance order for determinism.
        // [SNAPDIFF-SHADOWED-PROP] A row of a shadowed name is filed under its copy's label (name + offset), the label
        // the query names it by. Filed by name, the two copies overwrote each other: the last row won, in no defined order.
        var shadowedNames = new HashSet<string>(q.ShadowedFields.Values.Select(f => f.Name), StringComparer.Ordinal);
        var byIndex = new Dictionary<long, Instance>();
        var order = new List<long>();
        foreach (var r in rows)
        {
            if (!byIndex.TryGetValue(r.ObjectIndex, out var inst))
            {
                inst = new Instance { NormPath = r.NormPath, ObjAddr = r.ObjAddr };
                byIndex[r.ObjectIndex] = inst;
                order.Add(r.ObjectIndex);
            }
            var label = shadowedNames.Contains(r.PropName) ? PivotFieldInfo.LabelOf(r.PropName, r.PropOffset) : r.PropName;
            inst.Fields[label] = (r.DeclaredType, r.Hex);
        }
        result.InstanceCount = order.Count;

        // 2. Group instances by key value (identity = norm_path; field = the
        //    rendered key field, or "(missing)" when the instance lacks it). A
        //    multi-field key groups by the TUPLE of the rendered key segments.
        var keyFields = q.EffectiveKeyFields;
        var groups = new Dictionary<string, List<Instance>>();
        var groupOrder = new List<string>();
        foreach (var idx in order)
        {
            var inst = byIndex[idx];
            string key = q.KeyMode == PivotKeyMode.Field && keyFields.Count > 0
                ? RenderCompositeKey(inst, keyFields)
                : inst.NormPath;
            if (!groups.TryGetValue(key, out var list))
            {
                list = new List<Instance>();
                groups[key] = list;
                groupOrder.Add(key);
            }
            list.Add(inst);
        }
        result.GroupCount = groupOrder.Count;

        // 3. Build one row per group; sort by populousness then key. Most-
        //    populous groups first surfaces the meaningful aggregates.
        var built = new List<PivotResultRow>(groupOrder.Count);
        foreach (var key in groupOrder)
        {
            var members = groups[key];
            built.Add(new PivotResultRow
            {
                KeyValue      = key,
                Count         = members.Count,
                ObjAddr       = members[0].ObjAddr,
                ValuesDisplay = RenderValues(members, q.ValueFields),
            });
        }
        built.Sort((a, b) =>
        {
            int c = b.Count.CompareTo(a.Count);
            return c != 0 ? c : string.CompareOrdinal(a.KeyValue, b.KeyValue);
        });

        int max = q.MaxGroups > 0 ? q.MaxGroups : 5000;
        if (built.Count > max)
        {
            result.Truncated = true;
            built.RemoveRange(max, built.Count - max);
        }
        result.Rows.AddRange(built);
        return result;
    }

    /// <summary>Render one instance's composite group key: each key field's value
    /// (or "(missing)" when the instance lacks it), joined by " · ". A single key
    /// field renders verbatim (no separator), keeping one-element keys byte-identical
    /// to the legacy single-key path.</summary>
    private static string RenderCompositeKey(Instance inst, IReadOnlyList<string> keyFields)
    {
        if (keyFields.Count == 1)
            return inst.Fields.TryGetValue(keyFields[0], out var only)
                ? SnapshotNumeric.Render(only.type, only.hex) : "(missing)";
        var sb = new StringBuilder();
        for (int i = 0; i < keyFields.Count; i++)
        {
            if (i > 0) sb.Append(" · ");
            sb.Append(inst.Fields.TryGetValue(keyFields[i], out var kf)
                ? SnapshotNumeric.Render(kf.type, kf.hex) : "(missing)");
        }
        return sb.ToString();
    }

    private static string RenderValues(List<Instance> members, List<string> valueFields)
    {
        if (valueFields.Count == 0) return "";
        var sb = new StringBuilder();
        foreach (var field in valueFields)
        {
            var values = new List<string>(members.Count);
            foreach (var m in members)
                if (m.Fields.TryGetValue(field, out var fv))
                    values.Add(SnapshotNumeric.Render(fv.type, fv.hex));
            if (sb.Length > 0) sb.Append("   ·   ");
            sb.Append(field).Append('=').Append(RenderCell(values));
        }
        return sb.ToString();
    }

    /// <summary>Render a group's values for one field: the value when they all
    /// agree, else a collision <c>⟨N: v1,v2,v3,+M⟩</c> (N = contributing
    /// instances, up to 3 distinct shown). Public for unit tests.</summary>
    public static string RenderCell(IReadOnlyList<string> values)
    {
        if (values.Count == 0) return "—";
        var distinct = new List<string>();
        foreach (var v in values)
            if (!distinct.Contains(v)) distinct.Add(v);
        if (distinct.Count == 1) return distinct[0];

        var shown = distinct.Take(3);
        int extra = distinct.Count - 3;
        string inner = string.Join(",", shown) + (extra > 0 ? $",+{extra}" : "");
        return $"⟨{values.Count}: {inner}⟩";
    }
}
