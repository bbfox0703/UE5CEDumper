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
    /// <see cref="GetLayout"/>, but ONLY when the layout agrees with the size the engine
    /// reported for this param. Returns null otherwise, which falls every caller through to
    /// the DLL-discovered fields — those came from the actual <c>UScriptStruct</c>.
    ///
    /// <para><b>The engine's size is ground truth; the layout is a guess keyed on a DETECTED UE
    /// version.</b> When they disagree the guess is wrong — a mis-detected version (LWC turns
    /// FVector's floats into doubles, doubling every offset) or a licensee fork with a modified
    /// struct, both of which this project has met. Acting on a wrong layout puts every sub-field
    /// at a wrong offset with nothing on screen saying so. (audit #5 Y7.)</para>
    ///
    /// <para><b>This lives HERE, next to the table it guards, because it did not before.</b> Y7's
    /// fix was written as a private helper inside <c>InvokeParamDialog</c>, so it protected the
    /// one call site in that view and could not be reached by the two in
    /// <c>StructReturnDecoder</c> (a Service cannot depend on a View) — leaving the invoke dialog
    /// refusing a size-contradicted layout for its INPUT boxes while accepting the very same
    /// layout for the RESULT grid. That is audit #5 AC2, i.e. the audit's own fix applied at 1 of
    /// 4 sites. A predicate that guards a table belongs with the table.</para>
    /// </summary>
    /// <param name="structName">UScriptStruct name, or null/empty for a non-struct param.</param>
    /// <param name="engineSize">Engine-reported byte size of the param. &lt;= 0 means the DLL did
    /// not report one, so there is nothing to contradict the guess and the layout is returned.</param>
    /// <param name="ueVersion">Detected UE version (505=UE5.5, 427=UE4.27; 0=unknown → UE4).</param>
    public static StructLayout? GetTrustedLayout(string? structName, int engineSize, int ueVersion)
    {
        if (string.IsNullOrEmpty(structName)) return null;

        var layout = GetLayout(structName, ueVersion);
        if (layout == null) return null;

        if (engineSize > 0 && layout.TotalSize != engineSize) return null;

        return layout;
    }

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
