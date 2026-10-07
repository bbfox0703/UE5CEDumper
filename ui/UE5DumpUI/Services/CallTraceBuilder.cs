using UE5DumpUI.Models;

namespace UE5DumpUI.Services;

/// <summary>[LIVEFUNCS-TIMELINE-2026-10-04] Turns the ring's records into a call tree (red: not built yet).</summary>
public static class CallTraceBuilder
{
    public const int RecordSize = 40;

    public static TraceRecord[] Decode(byte[] data) => Array.Empty<TraceRecord>();

    public static CallTrace Build(ReadOnlySpan<TraceRecord> records, TraceInfo info,
                                  IEnumerable<TraceFuncName>? funcs = null, IEnumerable<TraceObjName>? objs = null)
        => new(0);
}
