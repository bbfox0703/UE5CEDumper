namespace UE5DumpUI.Core;

/// <summary>
/// Where a class's OWN properties begin, as ONE copy of the rule.
///
/// <para>
/// The DLL prepends the <b>entire</b> SuperStruct chain to <c>ClassInfoModel.Fields</c>, so every
/// exporter that must not re-emit inherited properties needs this boundary. Two of them needed it
/// and only one had it: the SDK header emitter split correctly (audit #5 W2), while the USMAP
/// writer emitted the flattened list AND the super pointer, so every child repeated its whole
/// ancestry and every own-property schema index was shifted by the size of that ancestry
/// (<c>[USMAP-INHERITED-DUPES]</c>). Measured on the DumperTest 5.4 fixture before the fix: of
/// 27,543 properties present in both our export and the canonical writer's, the schema index
/// differed on 12,316 across 2,230 of 7,684 shared classes, and 1,919 of 2,407 cooked exports
/// (79.7%) parsed differently under our mapping than under Dumper-7's.
/// </para>
///
/// <para>
/// ⚠ <b>The primary boundary is the super's <c>PropertiesSize</c>, not
/// <see cref="Models.ClassInfoModel.OwnPropertiesStart"/>.</b> The latter only ever LOWERS it, for
/// the empty-base case: UE sets a native struct's <c>PropertiesSize</c> from
/// <c>CppStructOps-&gt;GetSize()</c> (<c>Class.cpp:947</c>), so an EMPTY USTRUCT reports <b>1</b>,
/// while C++ empty-base optimisation puts the derived struct's first member at offset <b>0</b> —
/// and <c>Offset &gt;= 1</c> then silently drops it. UE 5.8.2 ships 62 such bases with 302
/// property-bearing children.
/// </para>
///
/// <para>
/// ⚠ <b>A negative <c>ownPropsStart</c> must not enter the comparison.</b> It means "no
/// information" (an older DLL) or "this class declares nothing of its own"; folding <c>-1</c> or
/// <c>0</c> into a <c>min()</c> re-emits the entire inherited chain, which is audit #5 W2 again.
/// </para>
/// </summary>
internal static class PropertyOwnership
{
    /// <summary>
    /// The offset at or above which a field belongs to this class rather than to an ancestor.
    /// </summary>
    /// <param name="superName">The immediate super's name; empty when the class has no super.</param>
    /// <param name="superPropsSize">The immediate super's <c>PropertiesSize</c>; 0 when unknown.</param>
    /// <param name="ownPropsStart">Lowest offset among the class's own properties, or -1.</param>
    /// <param name="firstFieldOffset">
    /// The lowest offset in the field list, or <c>null</c> when it is empty. Used only by the
    /// legacy fallback for a DLL that sends no <paramref name="superPropsSize"/> — and it is a
    /// fallback precisely because it mis-splits when a derived class adds nothing of its own.
    /// </param>
    internal static int OwnStartOffset(
        string? superName, int superPropsSize, int ownPropsStart, int? firstFieldOffset)
    {
        if (superPropsSize > 0)
        {
            int ownStart = superPropsSize;
            if (ownPropsStart >= 0 && ownPropsStart < ownStart)
                ownStart = ownPropsStart;
            return ownStart;
        }

        if (firstFieldOffset.HasValue && !string.IsNullOrEmpty(superName))
            return firstFieldOffset.Value;

        return 0;
    }
}
