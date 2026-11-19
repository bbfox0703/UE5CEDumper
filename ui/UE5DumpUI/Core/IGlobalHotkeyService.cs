namespace UE5DumpUI.Core;

/// <summary>
/// Registers an OS-global hotkey so an action can fire while another window
/// (the game) has focus. The Teleport panel's cursor teleport is the case it
/// exists for: the user keeps the game focused, presses the hotkey, and the
/// dumper teleports to whatever's under the in-game cursor.
///
/// Implementations live behind this interface per the Platform Abstraction
/// rule (the Win32 RegisterHotKey P/Invoke must not leak into Core / VMs).
/// </summary>
public interface IGlobalHotkeyService
{
    /// <summary>
    /// Register the cursor-teleport hotkey, auto-picking the first combo the OS
    /// accepts from a fixed ladder (Ctrl+F8 → Ctrl+F5, then Alt+F8 → Alt+F5).
    /// A combo already claimed by another app is skipped (RegisterHotKey fails),
    /// so the chosen combo is guaranteed free at registration time.
    ///
    /// <paramref name="onPressed"/> fires on a background thread each time the
    /// hotkey is pressed — marshal to the UI thread before touching VM state.
    /// Returns a handle whose <see cref="IGlobalHotkeyRegistration.Label"/> is
    /// the chosen combo (e.g. "Ctrl+F8") and whose Dispose unregisters it.
    /// Returns null when no candidate could be registered or the platform is
    /// unsupported.
    /// </summary>
    IGlobalHotkeyRegistration? RegisterCursorHotkey(Action onPressed);

    /// <summary>
    /// Register a SPECIFIC global hotkey (a user-chosen combo). Returns a handle
    /// on success, or null if the combo is already claimed elsewhere or the
    /// platform is unsupported. <paramref name="modifiers"/> uses the Win32 MOD_*
    /// bit flags (Alt=1, Control=2, Shift=4, Win=8); <paramref name="vk"/> is the
    /// virtual-key code. <paramref name="onPressed"/> fires on a background thread.
    /// </summary>
    IGlobalHotkeyRegistration? RegisterSpecific(uint modifiers, uint vk, string label, Action onPressed);
}

/// <summary>Win32 modifier bit flags for <see cref="IGlobalHotkeyService.RegisterSpecific"/>.</summary>
public static class HotkeyModifiers
{
    public const uint Alt = 0x0001;
    public const uint Control = 0x0002;
    public const uint Shift = 0x0004;
    public const uint Win = 0x0008;
}

/// <summary>An active global-hotkey registration. Dispose to unregister.</summary>
public interface IGlobalHotkeyRegistration : IDisposable
{
    /// <summary>Human-readable combo that was claimed, e.g. "Ctrl+F8".</summary>
    string Label { get; }
}
