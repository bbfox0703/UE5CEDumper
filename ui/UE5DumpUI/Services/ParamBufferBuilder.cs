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
            "NameProperty" or "ObjectProperty" or "ClassProperty"
                or "SoftObjectProperty" or "WeakObjectProperty"
                or "InterfaceProperty" => "0x0",
            _ => "0",
        };
    }

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
            case "ByteProperty":
            case "Int8Property":
            {
                byte val = ParseByte(text);
                buf[offset] = val;
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
                float val = float.TryParse(text, CultureInfo.InvariantCulture, out var f) ? f : 0f;
                if (available >= 4)
                    BinaryPrimitives.WriteSingleLittleEndian(buf.AsSpan(offset), val);
                break;
            }
            case "DoubleProperty":
            {
                double val = double.TryParse(text, CultureInfo.InvariantCulture, out var d) ? d : 0.0;
                if (available >= 8)
                    BinaryPrimitives.WriteDoubleLittleEndian(buf.AsSpan(offset), val);
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
            case "EnumProperty":
            {
                int val = (int)ParseLong(text);
                if (available >= 4)
                    BinaryPrimitives.WriteInt32LittleEndian(buf.AsSpan(offset), val);
                break;
            }
            default:
            {
                // Fallback by size
                switch (size)
                {
                    case 1:
                        buf[offset] = ParseByte(text);
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
                break;
            }
        }
    }

    private static byte ParseByte(string text)
    {
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return byte.TryParse(text.AsSpan(2), NumberStyles.HexNumber, null, out var b) ? b : (byte)0;
        return byte.TryParse(text, out var v) ? v : (byte)0;
    }

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
