namespace UE5DumpUI.Services;

/// <summary>
/// Hardcoded field layouts for common UE ScriptStruct types.
/// UE5 introduced Large World Coordinates (LWC) — FVector/FRotator/FQuat
/// changed from float to double. Integer-based structs are stable across versions.
/// </summary>
public static class KnownStructLayouts
{
    public sealed record StructSubField(string Name, string TypeName, int Offset, int Size);
    public sealed record StructLayout(string StructName, int TotalSize, IReadOnlyList<StructSubField> Fields);

    /// <summary>
    /// Get the field layout for a known struct. Returns null for unknown structs.
    /// </summary>
    /// <param name="structName">UScriptStruct name (e.g. "Vector", "Rotator").</param>
    /// <param name="ueVersion">UE version (e.g. 505=UE5.5, 427=UE4.27). 0=unknown, treated as UE4.</param>
    public static StructLayout? GetLayout(string structName, int ueVersion)
    {
        if (string.IsNullOrEmpty(structName)) return null;

        bool isUE5 = ueVersion >= 500;

        return structName switch
        {
            // --- Vector types (LWC-affected) ---
            "Vector" => isUE5
                ? MakeLayout("Vector", 24,
                    ("X", "DoubleProperty", 0, 8),
                    ("Y", "DoubleProperty", 8, 8),
                    ("Z", "DoubleProperty", 16, 8))
                : MakeLayout("Vector", 12,
                    ("X", "FloatProperty", 0, 4),
                    ("Y", "FloatProperty", 4, 4),
                    ("Z", "FloatProperty", 8, 4)),

            "Vector2D" => isUE5
                ? MakeLayout("Vector2D", 16,
                    ("X", "DoubleProperty", 0, 8),
                    ("Y", "DoubleProperty", 8, 8))
                : MakeLayout("Vector2D", 8,
                    ("X", "FloatProperty", 0, 4),
                    ("Y", "FloatProperty", 4, 4)),

            "Rotator" => isUE5
                ? MakeLayout("Rotator", 24,
                    ("Pitch", "DoubleProperty", 0, 8),
                    ("Yaw", "DoubleProperty", 8, 8),
                    ("Roll", "DoubleProperty", 16, 8))
                : MakeLayout("Rotator", 12,
                    ("Pitch", "FloatProperty", 0, 4),
                    ("Yaw", "FloatProperty", 4, 4),
                    ("Roll", "FloatProperty", 8, 4)),

            "Quat" => isUE5
                ? MakeLayout("Quat", 32,
                    ("X", "DoubleProperty", 0, 8),
                    ("Y", "DoubleProperty", 8, 8),
                    ("Z", "DoubleProperty", 16, 8),
                    ("W", "DoubleProperty", 24, 8))
                : MakeLayout("Quat", 16,
                    ("X", "FloatProperty", 0, 4),
                    ("Y", "FloatProperty", 4, 4),
                    ("Z", "FloatProperty", 8, 4),
                    ("W", "FloatProperty", 12, 4)),

            // --- Color types (stable across versions) ---
            "LinearColor" => MakeLayout("LinearColor", 16,
                ("R", "FloatProperty", 0, 4),
                ("G", "FloatProperty", 4, 4),
                ("B", "FloatProperty", 8, 4),
                ("A", "FloatProperty", 12, 4)),

            "Color" => MakeLayout("Color", 4,
                ("B", "ByteProperty", 0, 1),
                ("G", "ByteProperty", 1, 1),
                ("R", "ByteProperty", 2, 1),
                ("A", "ByteProperty", 3, 1)),

            // --- Integer types (stable) ---
            "IntPoint" => MakeLayout("IntPoint", 8,
                ("X", "IntProperty", 0, 4),
                ("Y", "IntProperty", 4, 4)),

            "IntVector" => MakeLayout("IntVector", 12,
                ("X", "IntProperty", 0, 4),
                ("Y", "IntProperty", 4, 4),
                ("Z", "IntProperty", 8, 4)),

            "Guid" => MakeLayout("Guid", 16,
                ("A", "UInt32Property", 0, 4),
                ("B", "UInt32Property", 4, 4),
                ("C", "UInt32Property", 8, 4),
                ("D", "UInt32Property", 12, 4)),

            // --- Tick/tag types (stable) ---
            "GameplayTag" => MakeLayout("GameplayTag", 8,
                ("TagName", "Int64Property", 0, 8)),

            "DateTime" => MakeLayout("DateTime", 8,
                ("Ticks", "Int64Property", 0, 8)),

            "Timespan" => MakeLayout("Timespan", 8,
                ("Ticks", "Int64Property", 0, 8)),

            _ => null,
        };
    }

    /// <summary>Check if a struct name has a known layout.</summary>
    public static bool IsKnown(string structName)
    {
        return structName is "Vector" or "Vector2D" or "Rotator" or "Quat"
            or "LinearColor" or "Color"
            or "IntPoint" or "IntVector" or "Guid"
            or "GameplayTag" or "DateTime" or "Timespan";
    }

    private static StructLayout MakeLayout(string name, int totalSize,
        params (string Name, string TypeName, int Offset, int Size)[] fields)
    {
        var list = new StructSubField[fields.Length];
        for (int i = 0; i < fields.Length; i++)
        {
            var f = fields[i];
            list[i] = new StructSubField(f.Name, f.TypeName, f.Offset, f.Size);
        }
        return new StructLayout(name, totalSize, list);
    }
}
