using UE5DumpUI.Models;

namespace UE5DumpUI.Core;

/// <summary>
/// The single client-side source of truth for TMap / TSet element geometry.
/// <para>
/// <b>Why this type exists.</b> The stride of a <c>TSparseArray&lt;TSetElement&lt;T&gt;&gt;</c> slot is
/// <c>Align(Align(sizeof(T), alignof(T)) + 8, alignof(T))</c>. The alignments are engine-side facts
/// that never cross the wire, so a client cannot derive the stride — it can only guess. Three
/// independent copies of that guess used to live in this codebase
/// (<c>LiveWalkerViewModel</c>, <c>CeXmlExportService</c>, <c>CsxExportService</c>), each carrying a
/// comment claiming it mirrored the DLL. When the DLL's own formula was corrected to take
/// <c>elemAlign</c>, all three silently went stale and every map element address the UI computed
/// itself fell 4+ bytes short past index 0 (audit #5 finding V2).
/// </para>
/// <para>
/// The fix is not a fourth copy of the arithmetic: the DLL now publishes the stride it actually
/// used to read the elements (<c>map_stride</c> / <c>set_stride</c>), and everything here prefers
/// that number. By construction the UI and the DLL can no longer disagree about where an element is.
/// </para>
/// </summary>
public static class ContainerGeometry
{
    /// <summary>
    /// The legacy client-side approximation, kept ONLY as a fallback for fields that carry no
    /// DLL-supplied stride (a pre-<c>map_stride</c> DLL, or a field reconstructed offline where no
    /// walk ever ran).
    /// <para>
    /// ⚠ This is <b>correct only when alignof(T) &lt;= 4</b>. It drops the trailing padding of an
    /// 8-aligned element: for a pair of size 12 containing a pointer it yields 20 where the engine
    /// strides 24. It is a best effort, not a second opinion — never prefer it over
    /// <see cref="LiveFieldValue.MapStride"/> / <see cref="LiveFieldValue.SetStride"/>.
    /// </para>
    /// </summary>
    public static int FallbackStride(int elemSize)
    {
        int hashStart = (elemSize + 3) & ~3;  // align the element to 4
        return hashStart + 8;                 // + HashNextId(4) + HashIndex(4)
    }

    /// <summary>
    /// Aligned byte offset of the value inside a TPair. The DLL computes this from the value's real
    /// alignment; the <see cref="LiveFieldValue.MapKeySize"/> fallback is only for fields that
    /// predate the field or never had element data read.
    /// </summary>
    public static int MapValueOffsetOf(LiveFieldValue field) =>
        field.MapValueOffset > 0 ? field.MapValueOffset : field.MapKeySize;

    /// <summary>
    /// Slot stride for a TMap's element storage — the DLL's own number when it supplied one.
    /// </summary>
    public static int MapStrideOf(LiveFieldValue field) =>
        field.MapStride > 0
            ? field.MapStride
            : FallbackStride(MapValueOffsetOf(field) + field.MapValueSize);

    /// <summary>
    /// Slot stride for a TSet's element storage — the DLL's own number when it supplied one.
    /// <para>
    /// TSet is the case the stale copies happened to get right: a bare <c>elemSize</c> is already a
    /// multiple of <c>alignof(T)</c>, so the DLL's <c>elemAlign</c> default of 4 reproduces
    /// <see cref="FallbackStride"/> exactly. Routed through here anyway so there is one rule.
    /// </para>
    /// </summary>
    public static int SetStrideOf(LiveFieldValue field) =>
        field.SetStride > 0 ? field.SetStride : FallbackStride(field.SetElemSize);

    /// <summary>
    /// Absolute address of a TMap element's <b>value</b> — the thing the row's TypeName describes,
    /// the thing an inline edit writes, and the thing a CE record must point at.
    /// <para>
    /// The element base is the <b>key</b> (the TPair starts with it), so anything that treats the
    /// element base as the value writes over the key. That was audit #5 finding V1.
    /// </para>
    /// </summary>
    /// <returns>0 when <paramref name="dataBase"/> or the stride is unusable.</returns>
    public static ulong MapValueAddress(LiveFieldValue container, ulong dataBase, int index)
    {
        int stride = MapStrideOf(container);
        if (dataBase == 0 || stride <= 0 || index < 0) return 0;
        return dataBase + (ulong)(index * stride) + (ulong)MapValueOffsetOf(container);
    }

    /// <summary>
    /// Absolute address of a TMap element's <b>key</b> (the TPair base).
    /// </summary>
    /// <returns>0 when <paramref name="dataBase"/> or the stride is unusable.</returns>
    public static ulong MapKeyAddress(LiveFieldValue container, ulong dataBase, int index)
    {
        int stride = MapStrideOf(container);
        if (dataBase == 0 || stride <= 0 || index < 0) return 0;
        return dataBase + (ulong)(index * stride);
    }
}
