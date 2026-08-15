using System.Buffers.Binary;
using System.Globalization;
using UE5DumpUI.Models;

namespace UE5DumpUI.Services;

/// <summary>
/// Builds a hex-encoded ProcessEvent parameter buffer from user input values.
/// C# equivalent of the Lua param-writing logic in InvokeScriptGenerator.
/// </summary>
public static class ParamBufferBuilder
{
    /// <summary>
    /// Build a hex-encoded parameter buffer from user input strings.
    /// Returns uppercase hex string (e.g. "3F800000") or empty string if parmsSize is 0.
    /// </summary>
    public static string BuildParamsHex(
        IReadOnlyList<FunctionParamModel> inputParams,
        IReadOnlyList<string> userValues,
        int parmsSize)
    {
        if (parmsSize <= 0) return "";

        var buf = new byte[parmsSize];

        for (int i = 0; i < inputParams.Count && i < userValues.Count; i++)
        {
            var param = inputParams[i];
            var text = userValues[i].Trim();
            if (param.Offset < 0 || param.Offset >= parmsSize) continue;

            WriteParam(buf, param.Offset, param.TypeName, param.Size, text);
        }

        return Convert.ToHexString(buf);
    }

    /// <summary>
    /// Get the default input value for a parameter type.
    /// </summary>
    public static string GetDefaultValue(string typeName)
    {
        return typeName switch
        {
            "FloatProperty" or "DoubleProperty" => "0.0",
            "BoolProperty" => "0",
            // String types start empty (a text value, not a number). Used by the
            // baked-script path via the dialog; the CE Lua builds the FString.
            "StrProperty" or "Utf8StrProperty" or "AnsiStrProperty" => "",
            "NameProperty" or "ObjectProperty" or "ClassProperty"
                or "SoftObjectProperty" or "SoftClassProperty"
                or "WeakObjectProperty" or "LazyObjectProperty"
                or "InterfaceProperty" => "0x0",
            _ => "0",
        };
    }

    /// <summary>
    /// True when a parameter type is a pointer to a UObject-flavoured value
    /// that benefits from the Stage 2 instance-picker UI (a button that opens
    /// the live-instance picker pre-filtered to the param's expected UClass).
    ///
    /// Includes every property type the DLL's WalkFunctions extracts an
    /// <c>objClassName</c> for. Excludes scalars / struct / containers / bool —
    /// those have their own editing paths (typed textbox, struct sub-field
    /// expansion, etc.) and shouldn't get a picker button.
    /// </summary>
    public static bool IsPickablePointerType(string typeName) =>
        typeName is "ObjectProperty"
                 or "ClassProperty"
                 or "WeakObjectProperty"
                 or "SoftObjectProperty"
                 or "SoftClassProperty"
                 or "InterfaceProperty"
                 or "LazyObjectProperty";

    /// <summary>
    /// True for UE string property types (FString / FUtf8String / FAnsiString).
    /// These can't be written as scalars into the params hex — the DLL builds
    /// the by-value FString (see <see cref="Models.InvokeStringParam"/>).
    /// </summary>
    public static bool IsStringType(string typeName) =>
        typeName is "StrProperty" or "Utf8StrProperty" or "AnsiStrProperty";

    /// <summary>True for the wide (UTF-16) FString; false for narrow FUtf8/FAnsiString.</summary>
    public static bool IsWideString(string typeName) => typeName == "StrProperty";

    /// <summary>
    /// Get a short display name for a parameter type.
    /// </summary>
    public static string ShortTypeName(string typeName)
    {
        return typeName switch
        {
            "BoolProperty" => "bool",
            "ByteProperty" => "uint8",
            "Int8Property" => "int8",
            "Int16Property" => "int16",
            "UInt16Property" => "uint16",
            "IntProperty" => "int32",
            "UInt32Property" => "uint32",
            "Int64Property" => "int64",
            "UInt64Property" => "uint64",
            "FloatProperty" => "float",
            "DoubleProperty" => "double",
            "NameProperty" => "FName",
            "ObjectProperty" => "UObject*",
            "ClassProperty" => "UClass*",
            "StrProperty" => "FString",
            "Utf8StrProperty" => "FUtf8String",
            "AnsiStrProperty" => "FAnsiString",
            "TextProperty" => "FText",
            "EnumProperty" => "enum",
            "StructProperty" => "struct",
            _ => typeName.Replace("Property", ""),
        };
    }

    /// <summary>
    /// Write a known struct's sub-field values into the buffer at the param's base offset.
    /// </summary>
    public static void WriteStructParam(
        byte[] buf, int paramOffset,
        IReadOnlyList<KnownStructLayouts.StructSubField> subFields,
        IReadOnlyList<string> subValues)
    {
        for (int i = 0; i < subFields.Count && i < subValues.Count; i++)
        {
            var sf = subFields[i];
            int absOffset = paramOffset + sf.Offset;
            if (absOffset < 0 || absOffset >= buf.Length) continue;
            WriteParam(buf, absOffset, sf.TypeName, sf.Size, subValues[i].Trim());
        }
    }

    /// <summary>
    /// Write a DLL-discovered dynamic struct's sub-field values into the buffer.
    /// Phase B fallback for structs not in KnownStructLayouts.
    /// </summary>
    public static void WriteStructParam(
        byte[] buf, int paramOffset,
        IReadOnlyList<DynamicStructField> subFields,
        IReadOnlyList<string> subValues)
    {
        for (int i = 0; i < subFields.Count && i < subValues.Count; i++)
        {
            var sf = subFields[i];
            int absOffset = paramOffset + sf.Offset;
            if (absOffset < 0 || absOffset >= buf.Length) continue;
            WriteParam(buf, absOffset, sf.TypeName, sf.Size, subValues[i].Trim());
        }
    }

    internal static void WriteParam(byte[] buf, int offset, string typeName, int size, string text)
    {
        int available = buf.Length - offset;
        if (available <= 0) return;

        switch (typeName)
        {
            case "BoolProperty":
            {
                // Same spellings the exported script accepts (true/false/yes/no/on/off/1/0).
                // This path used to run everything through ParseByte, so typing `true` made
                // FIRE send 0 while Copy AA Script baked 1 -- one dialog, two opposite calls
                // into a live game (audit #5 Y3).
                buf[offset] = BakedScriptGenerator.ParseBoolLiteral(text) == true ? (byte)1 : (byte)0;
                break;
            }
            case "ByteProperty":
            case "Int8Property":
            {
                buf[offset] = ParseByteOrSByte(text);
                break;
            }
            case "Int16Property":
            {
                short val = (short)ParseLong(text);
                if (available >= 2)
                    BinaryPrimitives.WriteInt16LittleEndian(buf.AsSpan(offset), val);
                break;
            }
            case "UInt16Property":
            {
                ushort val = (ushort)ParseULong(text);
                if (available >= 2)
                    BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(offset), val);
                break;
            }
            case "FloatProperty":
            {
                if (available >= 4)
                    BinaryPrimitives.WriteSingleLittleEndian(buf.AsSpan(offset), (float)ParseReal(text));
                break;
            }
            case "DoubleProperty":
            {
                if (available >= 8)
                    BinaryPrimitives.WriteDoubleLittleEndian(buf.AsSpan(offset), ParseReal(text));
                break;
            }
            case "Int64Property":
            {
                long val = ParseLong(text);
                if (available >= 8)
                    BinaryPrimitives.WriteInt64LittleEndian(buf.AsSpan(offset), val);
                break;
            }
            case "UInt64Property":
            case "NameProperty":
            case "ObjectProperty":
            case "ClassProperty":
            case "SoftObjectProperty":
            case "SoftClassProperty":
            case "WeakObjectProperty":
            case "LazyObjectProperty":
            case "InterfaceProperty":
            {
                ulong val = ParseULong(text);
                if (available >= 8)
                    BinaryPrimitives.WriteUInt64LittleEndian(buf.AsSpan(offset), val);
                break;
            }
            case "IntProperty":
            case "UInt32Property":
            {
                int val = (int)ParseLong(text);
                if (available >= 4)
                    BinaryPrimitives.WriteInt32LittleEndian(buf.AsSpan(offset), val);
                break;
            }
            case "EnumProperty":
            {
                // An enum's width is whatever the engine reported, NOT 4. This case used to
                // be grouped with IntProperty, so a 1-byte `enum class : uint8` param hit the
                // `available >= 4` guard, wrote NOTHING, and the game received 0 while the
                // exported script sent the user's value (audit #5 Y2).
                WriteBySize(buf, offset, available, size, text);
                break;
            }
            default:
            {
                // Fallback by size -- shared with EnumProperty so the two cannot drift.
                WriteBySize(buf, offset, available, size, text);
                break;
            }
        }
    }

    /// <summary>
    /// Write an integer value at whatever width the engine reported for the param.
    /// Used by the size-driven fallback and by EnumProperty, whose width is 1/2/4/8.
    /// </summary>
    private static void WriteBySize(byte[] buf, int offset, int available, int size, string text)
    {
        switch (size)
        {
            case 1:
                buf[offset] = ParseByteOrSByte(text);
                break;
            case 2:
                if (available >= 2)
                    BinaryPrimitives.WriteInt16LittleEndian(buf.AsSpan(offset), (short)ParseLong(text));
                break;
            case 8:
                if (available >= 8)
                    BinaryPrimitives.WriteUInt64LittleEndian(buf.AsSpan(offset), ParseULong(text));
                break;
            default:
                if (available >= 4)
                    BinaryPrimitives.WriteInt32LittleEndian(buf.AsSpan(offset), (int)ParseLong(text));
                break;
        }
    }

    /// <summary>
    /// One byte, accepting everything the exported script accepts: decimal, 0x hex, and
    /// NEGATIVE values for a signed Int8 (-1 -> 0xFF). The old parser used
    /// <c>byte.TryParse</c>, which rejects any negative outright and silently sent 0
    /// (audit #5 Y5).
    /// </summary>
    private static byte ParseByteOrSByte(string text)
        => BakedScriptGenerator.TryParseHexOrDecimal((text ?? "").Trim(), out var u)
               ? unchecked((byte)u)
               : (byte)0;

    /// <summary>
    /// Parse a real number the way the exported script does: InvariantCulture, no thousands
    /// separators, and non-finite values refused.
    /// <para>
    /// <c>TryParse(text, IFormatProvider, out _)</c> defaults to
    /// <c>NumberStyles.Float | NumberStyles.AllowThousands</c> and ACCEPTS "NaN" / "Infinity",
    /// so `1,5` fired as 15.0 while the baked script rendered it unparsed, and NaN reached the
    /// game as a real float (audit #5 Y4).
    /// </para>
    /// </summary>
    private static double ParseReal(string text)
        => double.TryParse((text ?? "").Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d)
           && double.IsFinite(d)
               ? d
               : 0.0;

    private static long ParseLong(string text)
    {
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return long.TryParse(text.AsSpan(2), NumberStyles.HexNumber, null, out var v) ? v : 0;
        return long.TryParse(text, out var r) ? r : 0;
    }

    private static ulong ParseULong(string text)
    {
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return ulong.TryParse(text.AsSpan(2), NumberStyles.HexNumber, null, out var v) ? v : 0;
        return ulong.TryParse(text, out var r) ? r : 0;
    }
}
