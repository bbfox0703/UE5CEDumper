using UE5DumpUI.Helpers;
using UE5DumpUI.Models;

namespace UE5DumpUI.Services;

/// <summary>
/// [LIVEFUNCS-TIMELINE-2026-10-04] The call tree as the Call Trace tab shows it: a flat list of the calls whose
/// ancestors are all expanded, in call order. The UI has no tree control; a flat virtualized list is how the Object
/// Tree shows hundreds of thousands of rows, and a trace can hold millions of calls, so the expanded state is a
/// column and the visible list is rebuilt from it, never a node object per call.
/// </summary>
public sealed class CallTraceTree
{
    private readonly CallTrace _t;
    private readonly bool[] _expanded;
    private int[] _visible = Array.Empty<int>();

    public CallTraceTree(CallTrace trace)
    {
        _t = trace;
        _expanded = new bool[trace.Count];
        Rebuild();
    }

    public CallTrace Trace => _t;
    /// <summary>The visible calls, top to bottom.</summary>
    public IReadOnlyList<int> Visible => _visible;
    public bool IsExpanded(int call) => _expanded[call];

    public void Toggle(int call)
    {
        if (_t.ChildCount[call] == 0) return;
        _expanded[call] = !_expanded[call];
        Rebuild();
    }

    public void ExpandAll()
    {
        for (int i = 0; i < _t.Count; i++) _expanded[i] = _t.ChildCount[i] > 0;
        Rebuild();
    }

    public void CollapseAll()
    {
        Array.Clear(_expanded);
        Rebuild();
    }

    /// <summary>Expands every ancestor of <paramref name="call"/> so it is visible; returns its row, or -1.</summary>
    public int Reveal(int call)
    {
        bool changed = false;
        for (int p = _t.Parent[call]; p >= 0; p = _t.Parent[p])
        {
            if (!_expanded[p]) { _expanded[p] = true; changed = true; }
        }
        if (changed) Rebuild();
        return Array.IndexOf(_visible, call);
    }

    private void Rebuild()
    {
        var list = new List<int>(Math.Max(_t.Roots.Length, 16));
        var stack = new Stack<int>();
        // Roots in order: push them reversed so the first pops first; the same for each expanded call's children.
        for (int r = _t.Roots.Length - 1; r >= 0; r--) stack.Push(_t.Roots[r]);
        var kids = new List<int>();
        while (stack.Count > 0)
        {
            int c = stack.Pop();
            list.Add(c);
            if (!_expanded[c]) continue;
            kids.Clear();
            for (int k = _t.FirstChild[c]; k >= 0; k = _t.NextSibling[k]) kids.Add(k);
            for (int k = kids.Count - 1; k >= 0; k--) stack.Push(kids[k]);
        }
        _visible = list.ToArray();
    }

    /// <summary>
    /// The calls matching every term of <paramref name="filter"/> in any of their class, function, object or object
    /// class (the shared space = AND matcher), in call order, at most <paramref name="cap"/>. Each term is matched
    /// once per distinct function and object, not once per call: a trace repeats the same few thousand names
    /// millions of times.
    /// </summary>
    public static int[] Match(CallTrace t, string? filter, int cap, out bool capped)
    {
        capped = false;
        var terms = ObjectTreeFilter.SplitTerms(filter);
        if (terms.Length == 0) return Array.Empty<int>();
        if (terms.Length > 64) terms = terms[..64];
        ulong all = terms.Length == 64 ? ulong.MaxValue : (1UL << terms.Length) - 1;

        var funcMask = new Dictionary<ulong, ulong>();
        var objMask = new Dictionary<ulong, ulong>();
        ulong MaskOf(params string?[] fields)
        {
            ulong m = 0;
            for (int k = 0; k < terms.Length; k++)
                if (ObjectTreeFilter.MatchesAllTerms(new[] { terms[k] }, fields)) m |= 1UL << k;
            return m;
        }

        var hits = new List<int>();
        for (int i = 0; i < t.Count; i++)
        {
            if (!funcMask.TryGetValue(t.Func[i], out var fm))
                funcMask[t.Func[i]] = fm = MaskOf(t.ClassName(i), t.FuncName(i));
            ulong om = 0;
            if (t.Obj[i] != 0 && !objMask.TryGetValue(t.Obj[i], out om))
                objMask[t.Obj[i]] = om = MaskOf(t.ObjName(i), t.ObjClass(i));
            if ((fm | om) != all) continue;
            if (hits.Count >= cap) { capped = true; break; }
            hits.Add(i);
        }
        return hits.ToArray();
    }
}
