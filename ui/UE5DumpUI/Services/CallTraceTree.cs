using UE5DumpUI.Models;

namespace UE5DumpUI.Services;

/// <summary>[LIVEFUNCS-TIMELINE-2026-10-04] The call tree as the Call Trace tab shows it (red: not built yet).</summary>
public sealed class CallTraceTree
{
    public CallTraceTree(CallTrace trace) => Trace = trace;
    public CallTrace Trace { get; }
    public IReadOnlyList<int> Visible => Array.Empty<int>();
    public bool IsExpanded(int call) => false;
    public void Toggle(int call) { }
    public void ExpandAll() { }
    public void CollapseAll() { }
    public int Reveal(int call) => -1;
    public static int[] Match(CallTrace t, string? filter, int cap, out bool capped) { capped = false; return Array.Empty<int>(); }
}
