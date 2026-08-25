namespace UE5DumpUI.Models;

/// <summary>
/// One nesting hop beyond the outermost container, for a value buried in a
/// SEPARATELY-allocated nested container (e.g. a TArray&lt;int&gt; inside a
/// struct element of a TMap value inside a struct element of a TArray). Mirrors
/// the DLL's <c>ContainerHop</c>. Each hop is one container drilled INTO,
/// reached from the previous element's struct.
/// </summary>
public sealed class ContainerHop
{
    /// <summary>Container field offset within the PARENT element struct.</summary>
    public int FieldOffset { get; init; }
    /// <summary>Dotted name within that struct (e.g. "MsTuneData.MsTunes").</summary>
    public string FieldName { get; init; } = "";
    public string FieldType { get; init; } = "";
    public string InnerType { get; init; } = "";
    public int ElementIndex { get; init; }
    public int ElementSize { get; init; }
    /// <summary>(addr - elementStart) within this hop's element. Only meaningful
    /// on the deepest hop (intermediate hops are 0 — the leaf intra lives on the
    /// last hop).</summary>
    public int IntraOffset { get; init; }
    public string DataAddress { get; init; } = "";
    /// <summary>True when the hit is on the VALUE side of a Map pair (drilling
    /// the element lands on the value struct).</summary>
    public bool MapValueSide { get; init; }
    public string Note { get; init; } = "";
}

/// <summary>
/// One container-aware match: the address falls inside a UObject's
/// ArrayProperty heap buffer (TArray::Data), pinpointing element index
/// and intra-element offset.
/// </summary>
public sealed class ContainerMatch
{
    public string OwnerAddress { get; init; } = "";
    public int OwnerIndex { get; init; }
    public string OwnerName { get; init; } = "";
    public string OwnerClassName { get; init; } = "";

    /// <summary>Field offset within the owner UObject.</summary>
    public int FieldOffset { get; init; }
    public string FieldName { get; init; } = "";

    /// <summary>"ArrayProperty" (Map/Set future).</summary>
    public string FieldType { get; init; } = "";

    /// <summary>Inner element FProperty type (e.g., "ObjectProperty", "StructProperty").</summary>
    public string InnerType { get; init; } = "";

    public int ElementIndex { get; init; }
    public int ElementSize { get; init; }

    /// <summary>Byte offset within the element (addr - elementStart).</summary>
    public int IntraOffset { get; init; }

    /// <summary>TArray::Data base address (where the element buffer lives).</summary>
    public string DataAddress { get; init; } = "";

    /// <summary>TArray::Count (logical element count).</summary>
    public int Count { get; init; }

    /// <summary>
    /// Diagnostic note: empty for solid hits, "slack" for Array indices in
    /// [Count, Max), "freed" for Map/Set free-list slots. Lower-confidence
    /// matches — the memory still holds plausible data but the slot is
    /// formally unallocated.
    /// </summary>
    public string Note { get; init; } = "";

    /// <summary>
    /// Additional nesting levels for a deeply-nested value (DLL
    /// FindInContainersDeep). Empty for a shallow 1-level match. When non-empty,
    /// this match's own fields describe the OUTERMOST container and each
    /// <see cref="NestedChain"/> entry a deeper one; the last hop's
    /// <see cref="ContainerHop.IntraOffset"/> locates the value.
    /// </summary>
    public List<ContainerHop> NestedChain { get; init; } = new();

    /// <summary>True when this match descended through one or more nested
    /// containers (separate heap allocations) — i.e. the deep scan found it.</summary>
    public bool IsDeeplyNested => NestedChain.Count > 0;

    /// <summary>The intra-element offset of the value within the DEEPEST element
    /// (outermost <see cref="IntraOffset"/> when there's no nested chain).</summary>
    public int DeepestIntraOffset => NestedChain.Count > 0 ? NestedChain[^1].IntraOffset : IntraOffset;

    /// <summary>Display path: "OwnerName.FieldName[N].Hop[M]...[K]+0xK (note)".
    /// Spans the full nested chain for deeply-nested values.</summary>
    public string DisplayPath
    {
        get
        {
            var path = $"{OwnerName}.{FieldName}[{ElementIndex}]";
            foreach (var h in NestedChain)
                path += $".{h.FieldName}[{h.ElementIndex}]";
            if (DeepestIntraOffset > 0)
                path += $"+0x{DeepestIntraOffset:X}";
            var note = NestedChain.Count > 0 ? NestedChain[^1].Note : Note;
            if (!string.IsNullOrEmpty(note))
                path += $" ({note})";
            return path;
        }
    }
}

/// <summary>
/// Diagnostic counters for the container scan portion of a reverse-address
/// lookup. Lets callers tell whether a "no matches" result reflects a real
/// negative or a truncated scan that hit the time deadline.
/// </summary>
public sealed class ContainerScanStats
{
    public int ObjectsScanned { get; init; }
    public int ObjectsTotal { get; init; }
    public int ClassesPrimed { get; init; }
    public long DurationMs { get; init; }
    public bool DeadlineHit { get; init; }

    /// <summary>
    /// The recursive DEEP descent ran (the shallow pass found nothing and the caller
    /// opted in via <c>container_depth &gt; 1</c>). It matters to the reader because the
    /// deep pass is bounded by a per-container element probe cap as well as by the
    /// deadline — so a deep MISS is not proof of absence even when
    /// <see cref="DeadlineHit"/> is false. The DLL has always emitted this and the UI
    /// dropped it, which is half of why the "[scanned X/Y]" suffix could not do the one
    /// job it was added for. (audit #5 Z12)
    /// </summary>
    public bool DeepScan { get; init; }

    public bool IsComplete => !DeadlineHit && ObjectsScanned >= ObjectsTotal;
}

/// <summary>
/// Result of a reverse address lookup — given an arbitrary address,
/// find which UObject (if any) it belongs to.
/// </summary>
public sealed class AddressLookupResult
{
    /// <summary>Whether a matching UObject was found.</summary>
    public bool Found { get; init; }

    /// <summary>"exact" if the address is a UObject itself, "contains" if it falls inside one.</summary>
    public string MatchType { get; init; } = "";

    /// <summary>
    /// More precise confidence kind: "exact" / "contains" / "backward" / "nearest".
    /// "nearest" is a low-confidence fallback (addr is BEYOND PropertiesSize of the
    /// closest UObject) and should be presented as a hint rather than containment.
    /// Empty for older DLLs.
    /// </summary>
    public string MatchKind { get; init; } = "";

    /// <summary>True when the match is a real containment / exact (high confidence).</summary>
    public bool IsHighConfidence => MatchKind is "exact" or "contains";

    /// <summary>The owning UObject address.</summary>
    public string Address { get; init; } = "";

    /// <summary>InternalIndex in GObjects.</summary>
    public int Index { get; init; }

    /// <summary>Object name.</summary>
    public string Name { get; init; } = "";

    /// <summary>Class name of the object.</summary>
    public string ClassName { get; init; } = "";

    /// <summary>Outer object address.</summary>
    public string OuterAddr { get; init; } = "";

    /// <summary>Byte offset from object base (0 for exact match).</summary>
    public int OffsetFromBase { get; init; }

    /// <summary>The original query address.</summary>
    public string QueryAddress { get; init; } = "";

    /// <summary>
    /// Container-aware matches — when the address falls inside a UObject's
    /// ArrayProperty heap buffer rather than within the UObject itself.
    /// Empty list when the standard match was sufficient and container
    /// scan was not requested / produced no hits.
    /// </summary>
    public List<ContainerMatch> ContainerMatches { get; init; } = new();

    /// <summary>
    /// Diagnostic stats from the container scan; null when scan wasn't run.
    /// Inspect ContainerScan?.DeadlineHit to detect a truncated scan.
    /// </summary>
    public ContainerScanStats? ContainerScan { get; init; }
}
