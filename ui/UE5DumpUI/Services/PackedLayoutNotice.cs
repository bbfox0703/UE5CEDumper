using UE5DumpUI.Models;

namespace UE5DumpUI.Services;

/// <summary>
/// Ambient flag marking that the connected game runs the UE5.7+ *** UNVERIFIED *** packed
/// FUObjectItem layout, so every export generated while it is active can embed a one-line
/// "best-effort" note. Held as process-wide state because the app is single-instance
/// (Mutex-guarded) and attaches to exactly one game at a time — the static export utilities
/// would otherwise need the flag threaded through dozens of call sites. Set from
/// <c>PointerPanelViewModel.Update</c> on every engine-state refresh.
/// </summary>
public static class PackedLayoutNotice
{
    /// <summary>True while connected to a game using the packed (UNVERIFIED) layout.</summary>
    public static bool IsActive { get; set; }

    /// <summary>The shared best-effort note text (no comment delimiters).</summary>
    public static string Note => EngineState.PackedExportNote;

    /// <summary>Leading XML comment line (with trailing newline) when active, else empty.
    /// Suitable for CE XML / CSX output.</summary>
    public static string XmlComment => IsActive ? $"<!-- {Note} -->\n" : "";

    /// <summary>Leading Lua/line comment (with trailing newline) when active, else empty.</summary>
    public static string LuaComment => IsActive ? $"-- {Note}\n" : "";

    /// <summary>Compact prefix for a CE memory-record name/description when active, else empty.
    /// Flags that the record's address was reconstructed via the unverified packed encoding.</summary>
    public static string RecordNamePrefix => IsActive ? "[UE5.7+ packed?] " : "";
}
