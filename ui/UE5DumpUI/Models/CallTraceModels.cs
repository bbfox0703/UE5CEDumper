using System.Runtime.InteropServices;

namespace UE5DumpUI.Models;

// [LIVEFUNCS-TIMELINE-2026-10-04] The Live Funcs call trace: what pe_profile_start arms and the pe_trace_* commands
// read after Stop. The plan and its decisions: docs/live-funcs-timeline-plan.md.

/// <summary>One 40-byte slot of the DLL's ring, as <c>pe_trace_get</c> ships it (Linie::TraceRecord). The layout is
/// the wire format: the DLL static_asserts the same 40 bytes, and a test pins this side.</summary>
[StructLayout(LayoutKind.Sequential, Pack = 8)]
public readonly struct TraceRecord
{
    public const ulong ReturnBit = 1UL << 63;
    public const uint ScopeRootFlag = 1;

    /// <summary>The record's sequence number; <see cref="ReturnBit"/> set on a return record.</summary>
    public readonly ulong SeqKind;
    /// <summary>QueryPerformanceCounter when the hook saw it.</summary>
    public readonly ulong Ticks;
    /// <summary>Entry: the UFunction. Return: the sequence number of the call's entry record.</summary>
    public readonly ulong A;
    /// <summary>Entry: the object it was called on. Return: 0.</summary>
    public readonly ulong B;
    public readonly uint Tid;
    /// <summary>Entry: <see cref="ScopeRootFlag"/> when the call opened a ticked scope.</summary>
    public readonly uint Flags;

    public TraceRecord(ulong seqKind, ulong ticks, ulong a, ulong b, uint tid, uint flags)
    {
        SeqKind = seqKind; Ticks = ticks; A = a; B = b; Tid = tid; Flags = flags;
    }

    public bool IsReturn => (SeqKind & ReturnBit) != 0;
    public ulong Seq => SeqKind & ~ReturnBit;
}

/// <summary>The DLL's trace object (<c>trace</c> in the start / stop replies, and the head of every
/// <c>pe_trace_get</c> reply).</summary>
public sealed class TraceInfo
{
    public bool  Allocated  { get; init; }
    public bool  Tracing    { get; init; }
    /// <summary>False: a hook never left its write within Stop's wait, and the DLL does not hand the ring out.</summary>
    public bool  Quiesced   { get; init; } = true;
    public ulong Gen        { get; init; }
    public long  Bytes      { get; init; }
    public ulong Capacity   { get; init; }
    public ulong Written    { get; init; }
    public ulong FirstValid { get; init; }
    public ulong QpcFreq    { get; init; }
    public int   RecordSize { get; init; }
    public int   Ticked     { get; init; }
    public int   Excluded   { get; init; }
    /// <summary>[TRACE-UNLOADED-NAMES] Ticks the DLL left out at Start: their function was unloaded since the fetch
    /// that showed it (review UI-1). Only in a traced Start's reply.</summary>
    public int   TickedDropped { get; init; }

    public ulong Kept => Written - FirstValid;
}

/// <summary>What a Start asks of the trace.</summary>
public sealed class TraceStartOptions
{
    public long Bytes { get; init; }
    /// <summary>UFunction addresses (func_addr strings); empty = trace every call.</summary>
    public IReadOnlyList<string> Ticked { get; init; } = Array.Empty<string>();
    public bool ExcludePerFrame { get; init; }
}

/// <summary>One page of the stopped ring.</summary>
public sealed class TracePage
{
    public TraceInfo Info { get; init; } = new();
    public int Count { get; init; }
    /// <summary>Where the following page starts; paging ends at <see cref="TraceInfo.Written"/>.</summary>
    public ulong Next { get; init; }
    public byte[] Data { get; init; } = Array.Empty<byte>();
}

public sealed class TraceFuncName
{
    public ulong  Addr          { get; init; }
    /// <summary>The address still holds the function that fired.</summary>
    public bool   Live          { get; init; }
    /// <summary>[TRACE-UNLOADED-NAMES] Not live, and named from what the recording read at its first call.</summary>
    public bool   Unloaded      { get; init; }
    /// <summary>With <see cref="Unloaded"/>: another function has taken the address since.</summary>
    public bool   Recycled      { get; init; }
    /// <summary>The address held another function during the recording (review DLL-3): its calls are both functions',
    /// under the latest one's name.</summary>
    public bool   Reused        { get; init; }
    public string ClassName     { get; init; } = "";
    public string FuncName      { get; init; } = "";
    public uint   FunctionFlags { get; init; }
    /// <summary>[LIVEFUNCS-STEP2] A live native function's code entry (<c>UFunction::Func</c>), for a Cheat Engine
    /// address. 0 when the DLL sent "" (a script function, whose Func is the interpreter, or an entry not found) or
    /// nothing (a function not live, or a DLL from before code_addr).</summary>
    public ulong  CodeAddr      { get; init; }
    /// <summary>A name to show: live, or unloaded and named from its first call.</summary>
    public bool   Named => (Live || Unloaded) && FuncName.Length > 0;
}

public sealed class TraceObjName
{
    public ulong  Addr      { get; init; }
    /// <summary>False: the address no longer holds the object it held, so it was not read (a name is what is at the
    /// address NOW, resolved after Stop).</summary>
    public bool   Live      { get; init; }
    public string Name      { get; init; } = "";
    public string ClassName { get; init; } = "";
}

public sealed class TraceNamesPage<T>
{
    /// <summary>The recording the names belong to.</summary>
    public ulong Gen { get; init; }
    /// <summary>The DLL holds another recording than the one asked for, so nothing was named.</summary>
    public bool Stale { get; init; }
    public int Total { get; init; }
    public int Offset { get; init; }
    public List<T> Items { get; init; } = new();
    public bool Truncated { get; init; }
}
