using UE5DumpUI.Models;

namespace UE5DumpUI.Helpers;

/// <summary>[LIVEFUNCS-TIMELINE-2026-10-04] What the Call Trace tab exports (red: not built yet).</summary>
internal static class CallTraceExport
{
    internal static readonly string[] CsvColumns = Array.Empty<string>();
    internal static void WriteJsonl(CallTrace t, TextWriter w, DateTime savedAtUtc) { }
    internal static void WriteCsv(CallTrace t, TextWriter w) { }
}
