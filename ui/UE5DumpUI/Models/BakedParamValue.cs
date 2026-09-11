namespace UE5DumpUI.Models;

/// <summary>
/// One baked parameter value collected from <see cref="Views.InvokeParamDialog"/>
/// when the user clicks "Copy AA Script (Baked)" -- carries the raw text the
/// user typed plus enough metadata for the generator to emit a typed Lua
/// literal at the correct offset within the params buffer.
///
/// For <c>StructProperty</c> arguments, the dialog flattens the struct into
/// one <see cref="BakedParamValue"/> per sub-field; each sub-field gets the
/// absolute offset (parent offset + sub-field offset within the struct).
/// The <see cref="Services.BakedScriptGenerator"/> doesn't need to know that
/// nesting happened.
/// </summary>
/// <param name="ParamName">Display name (used only in the generated Lua
///     comment + the helper's error message). For struct sub-fields this is
///     formatted as "ParentName.SubFieldName".</param>
/// <param name="UeTypeName">UE FProperty type name from
///     <see cref="FunctionParamModel.TypeName"/> (e.g. "IntProperty",
///     "FloatProperty", "ObjectProperty").</param>
/// <param name="Size">Size in bytes of this scalar (post struct flattening).
///     Used for sanity checks; the helper allocates from
///     <c>FunctionInfoModel.ParmsSize</c> and writes by type, not size.</param>
/// <param name="Offset">Absolute offset within the function's params buffer
///     where this value should be written.</param>
/// <param name="LiteralText">Raw user-entered text from the dialog TextBox
///     (e.g. "1000", "3.14", "0x7FF6CD120000", "true"). The generator parses
///     this once and emits a Lua number literal -- no runtime parsing in the
///     generated script.</param>
/// <param name="BoolFieldMask">For a BoolProperty struct sub-field PACKED into a byte shared with
///     siblings: its single-bit FieldMask, so the helper does a read-modify-write of that bit
///     instead of stamping the whole byte (which zeroed a sibling or landed on bit 0). 0 = native
///     or unresolved: today's whole-byte write. [A3-FIRE-STRUCT-BOOLMASK]</param>
public sealed record BakedParamValue(
    string ParamName,
    string UeTypeName,
    int Size,
    int Offset,
    string LiteralText,
    int BoolFieldMask = 0);
