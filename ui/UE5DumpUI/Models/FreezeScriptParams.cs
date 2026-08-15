namespace UE5DumpUI.Models;

/// <summary>
/// Input bundle for <see cref="Services.FreezeScriptGenerator"/>.
///
/// Captures the (class, property, type, value) tuple needed to produce a
/// CE AA Script that uses <c>ue5_freeze_helper.lua</c> to lock a property
/// horizontally across ALL live instances of <see cref="ClassName"/>.
///
/// Sourced from a <see cref="PropertySearchMatch"/> row + a single value
/// the user types in <c>FreezeValueDialog</c>. The "defining class" is
/// what flows in here -- that's the class where the property is actually
/// declared (build 610+ PropertySearch dedupe), and writing at the offset
/// hits every instance of that class or its subclasses.
/// </summary>
public sealed class FreezeScriptParams
{
    /// <summary>Exact UE class name (case-insensitive at DLL match time,
    /// but preserved verbatim for the generated CFG block).</summary>
    public required string ClassName { get; init; }

    /// <summary>Property name -- purely informational, embedded in the
    /// generated script's comment header so a future reader knows which
    /// field was targeted.</summary>
    public required string PropertyName { get; init; }

    /// <summary>Byte offset of the property within the UObject instance.</summary>
    public required int PropertyOffset { get; init; }

    /// <summary>UE property type string (e.g. "FloatProperty", "BoolProperty").
    /// Mapped to the freeze helper's <c>valueType</c> via
    /// <see cref="Services.FreezeScriptGenerator.MapToHelperType(string,int)"/>,
    /// together with <see cref="PropertySize"/>.</summary>
    public required string UeTypeName { get; init; }

    /// <summary>
    /// Engine-reported byte width of the property (<c>PropertySearchMatch.PropSize</c>).
    /// 0 when the DLL did not report one (older DLL, or a row built without a search
    /// match) — the mapping then falls back to its legacy 4-byte default.
    ///
    /// <para><b>Required on purpose.</b> <see cref="UeTypeName"/> alone does not
    /// determine the width of an <c>EnumProperty</c>, so a freeze script built without
    /// this wrote 4 bytes into a 1-byte <c>enum class : uint8</c> and destroyed its
    /// three neighbours (audit #5 Y15). Leaving it optional would let the next call
    /// site silently re-create that bug; <c>required</c> makes the compiler ask.</para>
    /// </summary>
    public required int PropertySize { get; init; }

    /// <summary>
    /// FBoolProperty FieldMask (<c>PropertySearchMatch.BoolFieldMask</c>) — the single
    /// bit this bool owns inside the byte at <see cref="PropertyOffset"/>. 0 for every
    /// non-bool type, for a native bool that owns its whole byte, and for a row that
    /// came from a DLL older than this field.
    ///
    /// <para><b>Required on purpose</b>, for the same reason as <see cref="PropertySize"/>
    /// and by the same precedent. UE packs <c>uint8 bFoo:1</c> bools eight to a byte, and
    /// without the mask the freeze tick wrote the whole byte ~16×/sec: up to 7 sibling
    /// bools clobbered, and — whenever the mask was not <c>0x01</c> — the intended bool
    /// never set at all, so the feature silently did nothing while corrupting its
    /// neighbours. The engine reported the mask all along and it was dropped at this
    /// boundary. Making it optional would let the next call site re-create the bug
    /// silently; <c>required</c> makes the compiler ask. (audit #5 AA1)</para>
    /// </summary>
    public required int BoolFieldMask { get; init; }

    /// <summary>User-supplied value as a literal Lua expression (already
    /// validated by <c>FreezeValueDialog</c>). For numerics this is a
    /// number literal; for bool it is the string "true" or "false".</summary>
    public required string ValueLiteral { get; init; }
}
