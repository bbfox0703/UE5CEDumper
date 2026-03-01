namespace UE5DumpUI.Models;

/// <summary>
/// Represents a symbol (label) for export to debugger symbol maps.
/// Used by SymbolExportService for x64dbg, Ghidra, and IDA exports.
/// </summary>
public sealed class SymbolEntry
{
    /// <summary>Absolute hex address (e.g. "0x7FF612345678").</summary>
    public string Address { get; init; } = "";

    /// <summary>Module-relative hex address (e.g. "0x1A2B3C0").</summary>
    public string ModuleRelative { get; init; } = "";

    /// <summary>Label text (e.g. "AActor", "GObjects").</summary>
    public string Name { get; init; } = "";

    /// <summary>Object class name (e.g. "Class", "ScriptStruct", "Enum").</summary>
    public string ClassName { get; init; } = "";

    /// <summary>Category for filtering: "Class", "Struct", "Enum", "Function", "Object".</summary>
    public string Category { get; init; } = "";
}
